param(
    [string]$PostgresBin = 'C:\Program Files\PostgreSQL\18\bin',
    [switch]$KeepRunning
)

# Auditoria E2E da evolucao integrada dos modulos (Etapas 1-4):
#  - Etapa 1: programacao != reserva != saida fisica (criar schedule nao escreve em reservas/estoque)
#  - Etapa 2: tentativa frustrada != entrega (FAILED -> IN_DELIVERY; recusa nao conta como entregue)
#  - Etapa 3: somente o aceite avanca a quantidade entregue (entrega parcial 25/30)
#  - Etapa 4: entrega != liquidacao (SETTLE administrativo: idempotente, selado, sem escritura financeira/fiscal)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$psql = Join-Path $PostgresBin 'psql.exe'
$initdb = Join-Path $PostgresBin 'initdb.exe'
$pg_ctl = Join-Path $PostgresBin 'pg_ctl.exe'

foreach ($tool in @($psql, $initdb, $pg_ctl)) {
    if (-not (Test-Path $tool)) { throw "BLOCKED: Executavel nao encontrado: $tool" }
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'BLOCKED: dotnet indisponivel.' }

$evidenceDir = Join-Path $root ('artifacts\integrated-evolution-e2e-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidenceDir | Out-Null
$processes = [Collections.Generic.List[Diagnostics.Process]]::new()
$savedEnvironment = @{}
$startedDatabase = $false
$dataDir = ''

function Set-EnvVar([string]$Name, [string]$Value) {
    if (-not $savedEnvironment.ContainsKey($Name)) {
        $savedEnvironment[$Name] = [Environment]::GetEnvironmentVariable($Name)
    }
    [Environment]::SetEnvironmentVariable($Name, $Value)
}

function Get-FreePort {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    try { return $listener.LocalEndpoint.Port } finally { $listener.Stop() }
}

function Assert-Step([string]$StepName, [bool]$Condition, [string]$Details = '') {
    if (-not $Condition) {
        throw "FAIL ${StepName}: $Details. Evidencia em $evidenceDir"
    }
    Write-Host "PASS $StepName $(if ($Details) { "($Details)" })" -ForegroundColor Green
}

function Get-RandomBase64([int]$BytesCount) {
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $bytes = [byte[]]::new($BytesCount)
    $rng.GetBytes($bytes)
    return [Convert]::ToBase64String($bytes)
}

function Create-PasswordHash([string]$PlainText) {
    $nodeScript = "const crypto = require('crypto'); const salt = crypto.randomBytes(16); const hash = crypto.pbkdf2Sync(process.argv[1], salt, 210000, 32, 'sha512'); console.log(['pbkdf2-sha512', '210000', salt.toString('base64'), hash.toString('base64')].join('$'));"
    $res = & node -e $nodeScript $PlainText
    return $res.Trim()
}

function Exec-Sql([string]$DbName, [string]$Sql) {
    $tempFile = Join-Path $evidenceDir ("cmd-" + [guid]::NewGuid().ToString('N') + ".sql")
    [System.IO.File]::WriteAllText($tempFile, $Sql, [System.Text.Encoding]::UTF8)
    $output = cmd.exe /c "`"$psql`" -d $DbName -X -q -v ON_ERROR_STOP=1 --set=client_min_messages=warning -f `"$tempFile`" 2>&1"
    $exitCode = $LASTEXITCODE
    Remove-Item -Path $tempFile -Force -ErrorAction SilentlyContinue
    if ($exitCode -ne 0) {
        throw "Exec-Sql falhou em $DbName (exit code $exitCode): $output"
    }
    return $output
}

function Query-Sql([string]$DbName, [string]$Sql) {
    $tempFile = Join-Path $evidenceDir ("query-" + [guid]::NewGuid().ToString('N') + ".sql")
    [System.IO.File]::WriteAllText($tempFile, $Sql, [System.Text.Encoding]::UTF8)
    $output = cmd.exe /c "`"$psql`" -d $DbName -X -A -t -q -v ON_ERROR_STOP=1 -f `"$tempFile`" 2>&1"
    $exitCode = $LASTEXITCODE
    Remove-Item -Path $tempFile -Force -ErrorAction SilentlyContinue
    if ($exitCode -ne 0) {
        throw "Query-Sql falhou em $DbName (exit code $exitCode): $output"
    }
    return $output
}

function Read-SqlLines([string]$DbName, [string]$Sql) {
    return (Query-Sql $DbName $Sql).Trim().Split("`n") | ForEach-Object { $_.Trim() } | Where-Object { $_ }
}

function Invoke-PsqlFile([string]$DbName, [string]$FilePath, [string]$LogPath) {
    $out = cmd.exe /c "`"$psql`" -d $DbName -X -v ON_ERROR_STOP=1 --set=client_min_messages=warning -f `"$FilePath`" > `"$LogPath`" 2>&1"
    return $LASTEXITCODE
}

function Start-HostProcess([string]$HostName, [string]$Url) {
    $project = "src\Hosts\Agro360.$HostName"
    $dll = Join-Path $root "$project\bin\Release\net10.0\Agro360.$HostName.dll"
    $process = Start-Process dotnet -ArgumentList @("`"$dll`"", '--urls', $Url) -WorkingDirectory "$root\$project" -WindowStyle Hidden -PassThru -RedirectStandardOutput "$evidenceDir\$HostName.log" -RedirectStandardError "$evidenceDir\$HostName.err.log"
    $processes.Add($process)
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        if ($process.HasExited) { throw "$HostName encerrou na inicializacao. Evidencia: $evidenceDir" }
        try {
            $response = Invoke-WebRequest "$Url/health" -UseBasicParsing -TimeoutSec 2
            if ($response.StatusCode -in @(200, 503)) { return }
        } catch {
        }
        Start-Sleep -Milliseconds 500
    }
    throw "$HostName nao iniciou em tempo habil. Evidencia: $evidenceDir"
}

function Call-Api([string]$BaseUrl, [string]$Path, [string]$Method = 'GET', $Body = $null, [string]$Token = $null, [int[]]$ExpectedStatus = @(200)) {
    $uri = "$BaseUrl$Path"
    $headers = @{}
    if ($Token) { $headers['Authorization'] = "Bearer $Token" }
    $json = $null
    if ($Body -ne $null) {
        if ($Body -is [string]) { $json = $Body } else { $json = $Body | ConvertTo-Json -Depth 10 -Compress }
    }
    $response = $null
    try {
        $p = @{
            Uri = $uri
            Method = $Method
            Headers = $headers
            TimeoutSec = 20
            UseBasicParsing = $true
        }
        if ($json -ne $null) {
            $p['Body'] = $json
            $p['ContentType'] = 'application/json; charset=utf-8'
        }
        $response = Invoke-WebRequest @p
    } catch [System.Net.WebException] {
        $response = $_.Exception.Response
    } catch {
        if ($_.Exception.Response) {
            $response = $_.Exception.Response
        } else {
            throw "Erro na chamada API ${Method} ${uri}: $($_.Exception.Message)"
        }
    }

    $statusCode = 0
    $content = ''
    if ($response -is [System.Net.HttpWebResponse]) {
        $statusCode = [int]$response.StatusCode
        $stream = $response.GetResponseStream()
        if ($stream) {
            $reader = [System.IO.StreamReader]::new($stream, [System.Text.Encoding]::UTF8)
            $content = $reader.ReadToEnd()
            $reader.Dispose()
        }
    } elseif ($response) {
        $statusCode = [int]$response.StatusCode
        $content = if ($response.Content) { $response.Content } else { '' }
    }

    if ($statusCode -notin $ExpectedStatus) {
        throw "API $Method $Path retornou HTTP $statusCode (esperado: $($ExpectedStatus -join ', ')): $content"
    }
    return [PSCustomObject]@{
        StatusCode = $statusCode
        Content = $content
    }
}

try {
    Write-Host "=== INICIANDO HOMOLOGACAO E2E DA EVOLUCAO INTEGRADA (ETAPAS 1-4) ===" -ForegroundColor Cyan

    $dbPort = Get-FreePort
    $apiPort = Get-FreePort
    $apiUrl = "http://127.0.0.1:$apiPort"
    $dbPassword = Get-RandomBase64 32

    Set-EnvVar PGPASSWORD $dbPassword
    Set-EnvVar PGHOST '127.0.0.1'
    Set-EnvVar PGPORT "$dbPort"
    Set-EnvVar PGUSER 'postgres'

    $dataDir = Join-Path $evidenceDir 'pgdata'
    $pipeName = 'agro360-evo-pipe-' + [guid]::NewGuid().ToString('N')
    $pipe = [IO.Pipes.NamedPipeServerStream]::new($pipeName, [IO.Pipes.PipeDirection]::Out)
    try {
        $pending = $pipe.WaitForConnectionAsync()
        $init = Start-Process $initdb -ArgumentList @('-D', "`"$dataDir`"", '-U', 'postgres', '--auth=scram-sha-256', "--pwfile=\\.\pipe\$pipeName", '--encoding=UTF8', '--locale=C') -WindowStyle Hidden -PassThru -RedirectStandardOutput "$evidenceDir\initdb.log" -RedirectStandardError "$evidenceDir\initdb.err.log"
        $processes.Add($init)
        if (-not $pending.Wait(15000)) { throw 'initdb nao conectou ao canal de senha.' }
        $writer = [IO.StreamWriter]::new($pipe)
        try { $writer.WriteLine($dbPassword) } finally { $writer.Dispose() }
        $init.WaitForExit()
        if (-not (Test-Path (Join-Path $dataDir 'PG_VERSION'))) { throw "initdb falhou (PG_VERSION ausente). Verifique $evidenceDir\initdb.log" }
    } finally { $pipe.Dispose() }

    $pgLog = Join-Path $evidenceDir 'postgres.log'
    & $pg_ctl -D "$dataDir" -l "$pgLog" -o "-h 127.0.0.1 -p $dbPort" -w start
    if ($LASTEXITCODE -ne 0) { throw "Falha ao iniciar PostgreSQL descartavel na porta $dbPort." }
    $startedDatabase = $true
    Assert-Step "PostgreSQL descartavel iniciado" $true "Porta: $dbPort"

    # =========================================================================
    # BLOCO 1 - INSTALACAO LIMPA E REAPLICACAO DO CONSOLIDADO (full.sql duplo)
    # =========================================================================
    $dbClean = 'agro360_clean'
    $null = Exec-Sql 'postgres' "create database $dbClean;"

    $fullSqlPath = Join-Path $root 'database/agro360-postgres-full.sql'
    $installCode = Invoke-PsqlFile $dbClean $fullSqlPath "$evidenceDir\install-clean.log"
    Assert-Step "Instalacao limpa do SQL consolidado" ($installCode -eq 0)

    $reinstallCode = Invoke-PsqlFile $dbClean $fullSqlPath "$evidenceDir\reinstall.log"
    Assert-Step "Reexecucao idempotente do instalador (full.sql aplicado 2x)" ($reinstallCode -eq 0)

    # =========================================================================
    # BLOCO 2 - UPGRADE DE BASE 11.15.0 COM A MIGRATION 126 (incremental real)
    # =========================================================================
    $dbUpgrade = 'agro360_upgrade'
    $null = Exec-Sql 'postgres' "create database $dbUpgrade;"

    $fullSqlText = [System.IO.File]::ReadAllText($fullSqlPath, [System.Text.Encoding]::UTF8)
    $splitMarker = '-- Migration 126 espelho'
    if (-not $fullSqlText.Contains($splitMarker)) { throw 'Marcador do espelho 126 nao encontrado no full SQL.' }
    $baseSql = $fullSqlText.Substring(0, $fullSqlText.IndexOf($splitMarker))
    $baseSqlFile = Join-Path $evidenceDir 'temp-base-1115.sql'
    [System.IO.File]::WriteAllText($baseSqlFile, $baseSql, [System.Text.Encoding]::UTF8)

    $upgradeBaseCode = Invoke-PsqlFile $dbUpgrade $baseSqlFile "$evidenceDir\upgrade-base.log"
    Assert-Step "Instalacao base 11.15.0 em banco de upgrade" ($upgradeBaseCode -eq 0)

    $ver15Before = (Query-Sql $dbUpgrade "select count(*) from agro360.platform_schema_versions where version='11.15.0';").Trim()
    $ver16Before = (Query-Sql $dbUpgrade "select count(*) from agro360.platform_schema_versions where version='11.16.0';").Trim()
    $settledColsBefore = (Query-Sql $dbUpgrade "select count(*) from information_schema.columns where table_schema='agro360' and table_name='sales_delivery_schedules' and column_name in ('settled_at','settled_by','settlement_reason');").Trim()
    Assert-Step "Base de upgrade em 11.15.0 sem nada de liquidacao" ($ver15Before -eq '1' -and $ver16Before -eq '0' -and $settledColsBefore -eq '0') "11.15.0=$ver15Before 11.16.0=$ver16Before cols=$settledColsBefore"

    $mig126Path = Join-Path $root 'database/migrations/126_delivery_schedule_settlement.sql'
    $mig126Code = Invoke-PsqlFile $dbUpgrade $mig126Path "$evidenceDir\migration-126.log"
    Assert-Step "Aplicacao incremental da migration 126 sobre a base 11.15.0" ($mig126Code -eq 0)

    $ver16After = (Query-Sql $dbUpgrade "select count(*) from agro360.platform_schema_versions where version='11.16.0';").Trim()
    $settledColsAfter = (Query-Sql $dbUpgrade "select count(*) from information_schema.columns where table_schema='agro360' and table_name='sales_delivery_schedules' and column_name in ('settled_at','settled_by','settlement_reason');").Trim()
    $pairCheck = (Query-Sql $dbUpgrade "select count(*) from pg_constraint where connamespace='agro360'::regnamespace and conname='ck_sales_delivery_schedules_settled_pair';").Trim()
    $opCheckDef = (Query-Sql $dbUpgrade "select pg_get_constraintdef(oid) from pg_constraint where connamespace='agro360'::regnamespace and conname='ck_sales_delivery_schedule_operations_operation';").Trim()
    $settleIndex = (Query-Sql $dbUpgrade "select count(*) from pg_indexes where schemaname='agro360' and indexname='ix_sales_delivery_schedules_settled';").Trim()
    Assert-Step "Schema 11.16.0 com colunas, constraint SETTLE e indice de liquidacao" ($ver16After -eq '1' -and $settledColsAfter -eq '3' -and $pairCheck -eq '1' -and $opCheckDef -match 'SETTLE' -and $settleIndex -eq '1') "constraint: $opCheckDef"

    # =========================================================================
    # BLOCO 3 - RLS FORCADO NAS TABELAS OPERACIONAIS
    # =========================================================================
    $rlsCheck = Query-Sql $dbClean @"
select quote_ident(c.relname) || ':' || c.relrowsecurity || ':' || c.relforcerowsecurity
from pg_class c
join pg_namespace n on n.oid = c.relnamespace
where n.nspname = 'agro360'
  and c.relname in ('sales_delivery_schedules', 'sales_delivery_schedule_items', 'sales_delivery_schedule_operations', 'fulfillment_shipments')
order by c.relname;
"@
    $rlsLines = $rlsCheck.Trim().Split("`n") | ForEach-Object { $_.Trim() } | Where-Object { $_ }
    Assert-Step "Tabelas operacionais com RLS forcado" ($rlsLines.Count -eq 4 -and ($rlsLines | Where-Object { $_ -match ':(t|true):(t|true)' }).Count -eq 4) "RLS: $($rlsLines -join ', ')"

    # =========================================================================
    # BLOCO 4 - PROVISIONAMENTO DE TENANTS E CREDENCIAIS
    # =========================================================================
    $tenantAId = (Query-Sql $dbClean "select id from agro360.tenancy_tenants where slug = 'santa-clara' limit 1;").Trim()
    $tenantBId = (Query-Sql $dbClean "select id from agro360.tenancy_tenants where slug = 'cooperativa-vale-verde' limit 1;").Trim()
    Assert-Step "Tenants de fixture localizados" ($tenantAId.Length -gt 10 -and $tenantBId.Length -gt 10)
    $adminPassword = 'Aa1!' + (Get-RandomBase64 24)
    $adminHash = Create-PasswordHash $adminPassword

    Exec-Sql $dbClean @"
update agro360.saas_plans
set modules = array['properties','agriculture','livestock','inventory','finance','reports','logistics','traceability','intelligence','environment-esg','agroindustry','purchasing','commercial','orders']
where name in ('Profissional', 'Growth', 'Enterprise');

update agro360.identity_users
set password_hash = '$adminHash', status = 'ACTIVE', must_change_password = false
where email in ('admin.santaclara@agro360.local', 'admin@santaclara.agro360.local', 'admin.valeverde@agro360.local', 'superadmin@agro360.local');

insert into agro360.platform_tenant_module_entitlements(tenant_id, module_id, status, reason, activated_at)
select '$tenantBId', id, 'ACTIVE', 'Isolamento Vale Verde', now()
from agro360.platform_module_catalog where code in ('agriculture','inventory','commercial','logistics','traceability','analytics')
on conflict (tenant_id, module_id) do nothing;

insert into agro360.identity_role_permissions(tenant_id, role_id, permission_id)
select r.tenant_id, r.id, p.id
from agro360.identity_roles r cross join agro360.identity_permissions p
where r.tenant_id in ('$tenantAId', '$tenantBId') and lower(r.code) = 'tenant-administrator'
on conflict do nothing;
"@

    # =========================================================================
    # BLOCO 5 - HOST API REAL
    # =========================================================================
    $connString = "Host=127.0.0.1;Port=$dbPort;Database=$dbClean;Username=postgres;Password=$dbPassword"
    Set-EnvVar ConnectionStrings__Agro360 $connString
    Set-EnvVar ConnectionStrings__DefaultConnection $connString
    Set-EnvVar AGRO360_TEST_CONNECTION_STRING $connString
    Set-EnvVar ASPNETCORE_ENVIRONMENT 'Development'
    Set-EnvVar Jwt__SigningKey (Get-RandomBase64 48)
    Set-EnvVar ApiBaseUrl $apiUrl
    Set-EnvVar Cors__AllowedOrigins__0 $apiUrl
    Set-EnvVar Bootstrap__Enabled 'false'

    Start-HostProcess Api $apiUrl
    Assert-Step "Host API iniciado com sucesso" $true "URL: $apiUrl"

    # =========================================================================
    # BLOCO 6 - AUTENTICACAO HTTP REAL
    # =========================================================================
    $loginA = @{ tenantSlug = 'santa-clara'; email = 'admin@santaclara.agro360.local'; password = $adminPassword }
    $authARes = Call-Api $apiUrl '/api/v1/auth/login' 'POST' $loginA $null @(200)
    $tokenA = ($authARes.Content | ConvertFrom-Json).accessToken
    Assert-Step "Login Tenant A (Santa Clara)" ($tokenA.Length -gt 20)

    $loginB = @{ tenantSlug = 'cooperativa-vale-verde'; email = 'admin.valeverde@agro360.local'; password = $adminPassword }
    $authBRes = Call-Api $apiUrl '/api/v1/auth/login' 'POST' $loginB $null @(200)
    $tokenB = ($authBRes.Content | ConvertFrom-Json).accessToken
    Assert-Step "Login Tenant B (Cooperativa Vale Verde)" ($tokenB.Length -gt 20)

    # =========================================================================
    # BLOCO 7 - ETAPA 1: PROGRAMACAO NAO CRIA RESERVA NEM SAIDA FISICA
    # Saldo com average_cost 0 (custo ausente no cadastro); a evolucao nao pode fabricar custo.
    # =========================================================================
    $productId = '30000000-0000-0000-0000-000000000015'
    $warehouseId = '30000000-0000-0000-0000-000000000016'
    $segmentId = '30000000-0000-0000-0000-000000000017'
    $customerId = '30000000-0000-0000-0000-000000000018'
    $farmId = '30000000-0000-0000-0000-000000000010'
    $lotAId = [guid]::NewGuid().ToString()
    $lotBId = [guid]::NewGuid().ToString()

    Exec-Sql $dbClean @"
insert into agro360.crm_customer_segments(id, tenant_id, name, status, created_by)
values ('$segmentId', '$tenantAId', 'Cooperativas e Tradings', 'ACTIVE', '30000000-0000-0000-0000-000000000003')
on conflict (id) do nothing;

insert into agro360.crm_customers(id, tenant_id, segment_id, name, type, tax_document, status, created_by)
values ('$customerId', '$tenantAId', '$segmentId', 'Cooperativa Agropecuaria Central', 'CUSTOMER', '12345678000199', 'ACTIVE', '30000000-0000-0000-0000-000000000003')
on conflict (id) do nothing;

insert into agro360.inventory_products(id, tenant_id, sku, name, category, base_unit, requires_lot, created_by)
values ('$productId', '$tenantAId', 'SOJA-EVO', 'Soja Grao Evolucao', 'GRAOS', 'sc', true, '30000000-0000-0000-0000-000000000003')
on conflict (id) do nothing;

insert into agro360.inventory_warehouses(id, tenant_id, farm_id, code, name, type, created_by)
values ('$warehouseId', '$tenantAId', '$farmId', 'SILO-EVO', 'Silo Evolucao Central', 'GRAINS', '30000000-0000-0000-0000-000000000003')
on conflict (id) do nothing;

insert into agro360.inventory_stock_balances(id, tenant_id, warehouse_id, product_id, unit, available, reserved, average_cost, version)
values (gen_random_uuid(), '$tenantAId', '$warehouseId', '$productId', 'sc', 100.0, 0, 0.0, 1)
on conflict (tenant_id, warehouse_id, product_id) do update set available = 100.0, reserved = 0, average_cost = 0.0;

insert into agro360.inventory_stock_lots(id, tenant_id, warehouse_id, product_id, lot_number, quantity, quality_status)
values
 ('$lotAId', '$tenantAId', '$warehouseId', '$productId', 'LOTE-EVO-A', 40.0, 'APPROVED'),
 ('$lotBId', '$tenantAId', '$warehouseId', '$productId', 'LOTE-EVO-B', 20.0, 'APPROVED')
on conflict (tenant_id, warehouse_id, product_id, lot_number) do update set quantity = excluded.quantity;
"@

    $orderId = [guid]::NewGuid().ToString()
    $orderItemId = [guid]::NewGuid().ToString()
    Exec-Sql $dbClean @"
insert into agro360.sales_orders(id, tenant_id, order_number, customer_id, status, total_amount, freight, payment_terms, created_by, updated_by)
values('$orderId', '$tenantAId', 'PED-EVO-30', '$customerId', 'APPROVED', 3000.00, 0, '30 dias', '30000000-0000-0000-0000-000000000003', '30000000-0000-0000-0000-000000000003');

insert into agro360.sales_order_items(id, tenant_id, order_id, product_id, quantity, unit, unit_price, total_amount)
values('$orderItemId', '$tenantAId', '$orderId', '$productId', 30.00, 'sc', 100.00, 3000.00);
"@
    Assert-Step "Pedido aprovado criado (30 SACAS)" $true "OrderId: $orderId"

    $idemSchKey = 'idem-sch-evo-' + [guid]::NewGuid().ToString('N')
    $cmdSch = @{
        plannedDate = (Get-Date).AddDays(5).ToString('yyyy-MM-dd')
        destination = 'Fazenda Sol Nascente'
        notes = 'Programacao da evolucao integrada'
        idempotencyKey = $idemSchKey
        items = @(
            @{ orderItemId = $orderItemId; quantity = 30.0; unit = 'sc' }
        )
    }
    $createSchRes = Call-Api $apiUrl "/api/commercial/orders/$orderId/schedules" 'POST' $cmdSch $tokenA @(201)
    $schedule1Id = ($createSchRes.Content | ConvertFrom-Json).id
    Assert-Step "Criacao da programacao de entrega (30 SACAS)" ($schedule1Id.Length -gt 10) "ScheduleId: $schedule1Id"

    $sch1Pre = (Call-Api $apiUrl "/api/commercial/schedules/$schedule1Id" 'GET' $null $tokenA @(200)).Content | ConvertFrom-Json
    $sch1ItemId = $sch1Pre.items[0].id
    $sch1Version = [int64]$sch1Pre.version
    Assert-Step "Programacao inicia em estado planejado" ($sch1Pre.Status -in @('PLANNED', 'PREPARING')) "Status: $($sch1Pre.Status) Version: $sch1Version"

    $resCountAfterCreate = (Query-Sql $dbClean "select count(*) from agro360.fulfillment_reservations where order_item_id='$orderItemId';").Trim()
    Assert-Step "Etapa 1: programacao nao cria reserva de estoque" ($resCountAfterCreate -eq '0') "reservas: $resCountAfterCreate"

    $balanceAfterCreate = (Query-Sql $dbClean "select available::bigint::text || ',' || reserved::bigint::text || ',' || case when average_cost is null then 'NULL' else average_cost::bigint::text end from agro360.inventory_stock_balances where tenant_id='$tenantAId' and product_id='$productId';").Trim()
    Assert-Step "Etapa 1: programacao nao toca saldo nem custo medio do lancamento" ($balanceAfterCreate -eq '100,0,0') "available,reserved,avg_cost: $balanceAfterCreate"

    $movCountAfterCreate = (Query-Sql $dbClean "select count(*) from agro360.inventory_stock_movements where tenant_id='$tenantAId' and product_id='$productId';").Trim()
    Assert-Step "Etapa 1: programacao nao gera movimento de estoque" ($movCountAfterCreate -eq '0') "movimentos do produto: $movCountAfterCreate"

    # =========================================================================
    # BLOCO 8 - RESERVA NO ATENDIMENTO E SAIDA FISICA NA EXPEDICAO
    # =========================================================================
    $fulfillCmd = @{
        number = 'REM-EVO-001'
        originWarehouseId = $warehouseId
        customerId = $customerId
        destination = 'Fazenda Sol Nascente'
        scheduleId = $schedule1Id
        idempotencyKey = 'fulfill-evo-' + [guid]::NewGuid().ToString('N')
        items = @(
            @{
                orderItemId = $orderItemId
                stockLotId = $lotAId
                quantity = 20.0
                pickedQuantity = 20.0
                checkedQuantity = 20.0
                unit = 'sc'
                scheduleItemId = $sch1ItemId
            },
            @{
                orderItemId = $orderItemId
                stockLotId = $lotBId
                quantity = 10.0
                pickedQuantity = 10.0
                checkedQuantity = 10.0
                unit = 'sc'
                scheduleItemId = $sch1ItemId
            }
        )
    }
    $createFulfillRes = Call-Api $apiUrl '/api/logistics/trips/fulfillment' 'POST' $fulfillCmd $tokenA @(201)
    $shipmentId = ($createFulfillRes.Content | ConvertFrom-Json).id
    Assert-Step "Atendimento multi-lote criado (lote A 20 + lote B 10)" ($shipmentId.Length -gt 10) "ShipmentId: $shipmentId"

    $shipmentStatusAfterCreate = (Query-Sql $dbClean "select status from agro360.fulfillment_shipments where id='$shipmentId';").Trim()
    Assert-Step "Remessa criada conferida e pronta para expedir" ($shipmentStatusAfterCreate -eq 'CHECKED') "Status: $shipmentStatusAfterCreate"

    $resCountAfterFulfill = (Query-Sql $dbClean "select count(*) from agro360.fulfillment_reservations where order_item_id='$orderItemId' and status='ACTIVE';").Trim()
    Assert-Step "Reserva criada apenas pelo atendimento (2 reservas ativas)" ($resCountAfterFulfill -eq '2') "reservas: $resCountAfterFulfill"

    $balanceAfterFulfill = (Query-Sql $dbClean "select available::bigint::text || ',' || reserved::bigint::text from agro360.inventory_stock_balances where tenant_id='$tenantAId' and product_id='$productId';").Trim()
    Assert-Step "Atendimento reserva saldo sem consumir disponivel" ($balanceAfterFulfill -eq '100,30') "available,reserved: $balanceAfterFulfill"

    $shipDetail = (Call-Api $apiUrl "/api/logistics/trips/fulfillment/$shipmentId" 'GET' $null $tokenA @(200)).Content | ConvertFrom-Json
    $shipVersion = [int64]$shipDetail.shipment.version
    $dispatchCmd = @{
        version = $shipVersion
        idempotencyKey = 'dispatch-evo-' + [guid]::NewGuid().ToString('N')
    }
    $dispatchRes = Call-Api $apiUrl "/api/logistics/trips/fulfillment/$shipmentId/dispatch" 'POST' $dispatchCmd $tokenA @(200, 204)
    $shipmentStatusAfterDispatch = (Query-Sql $dbClean "select status from agro360.fulfillment_shipments where id='$shipmentId';").Trim()
    Assert-Step "Expedicao confirmada (saida fisica DISPATCHED)" ($shipmentStatusAfterDispatch -eq 'DISPATCHED') "Status: $shipmentStatusAfterDispatch"

    $stockAfterDispatch = (Query-Sql $dbClean "select available::bigint::text || ',' || reserved::bigint::text || '|' || l.lot_number || '=' || l.quantity::bigint::text from agro360.inventory_stock_balances b join agro360.inventory_stock_lots l on l.tenant_id=b.tenant_id and l.warehouse_id=b.warehouse_id and l.product_id=b.product_id where b.tenant_id='$tenantAId' and b.product_id='$productId' order by l.lot_number;").Trim()
    $stockLines = $stockAfterDispatch.Trim().Split("`n") | ForEach-Object { $_.Trim() } | Where-Object { $_ }
    $balPart = $stockLines[0].Split('|')[0]
    $lotAQty = ($stockLines[0] -split '=')[1]
    $lotBQty = ($stockLines[1] -split '=')[1]
    Assert-Step "Saida fisica decrementa lotes e saldo disponivel" ($balPart -eq '70,0' -and $lotAQty -eq '20' -and $lotBQty -eq '10') "available,reserved=$balPart lotes A/B=$lotAQty/$lotBQty"

    $movSaleCount = (Query-Sql $dbClean "select count(*) from agro360.inventory_stock_movements where reference_id='$shipmentId' and movement_type='SALE';").Trim()
    $movCostZero = (Query-Sql $dbClean "select count(*) from agro360.inventory_stock_movements where reference_id='$shipmentId' and movement_type='SALE' and unit_cost=0 and total_cost=0;").Trim()
    $movQtys = ((Read-SqlLines $dbClean "select quantity::bigint from agro360.inventory_stock_movements where reference_id='$shipmentId' and movement_type='SALE' order by quantity desc;") -join ',')
    Assert-Step "Movimentos SALE criados com custo ausente no lancamento (unit_cost/total_cost 0)" ($movSaleCount -eq '2' -and $movCostZero -eq '2' -and $movQtys -eq '20,10') "movs=$movSaleCount custo_zero=$movCostZero qtds=$movQtys"

    $schStatusAfterDispatch = (Query-Sql $dbClean "select status from agro360.sales_delivery_schedules where id='$schedule1Id';").Trim()
    $schItemAfterDispatch = (Query-Sql $dbClean "select dispatched_quantity::bigint::text || '/' || delivered_quantity::bigint::text from agro360.sales_delivery_schedule_items where id='$sch1ItemId';").Trim()
    Assert-Step "Compromisso DISPATCHED com 30 expedidas e 0 entregues" ($schStatusAfterDispatch -eq 'DISPATCHED' -and $schItemAfterDispatch -eq '30/0') "status=$schStatusAfterDispatch expedido/entregue=$schItemAfterDispatch"

    # =========================================================================
    # BLOCOS 9/10/11 - ETAPAS 2 E 3: TENTATIVAS, RECUSA, RETORNO E RECONCILIACAO
    # Etapa 2: tentativa FAILED -> remessa IN_DELIVERY e compromisso mantem DISPATCHED (frustrada != entrega)
    # Etapa 3: somente aceite avanca delivered (recusa 5 + retorno recebido; aceite 15 + 10 -> 25/30)
    # =========================================================================
    $itemAId = (Query-Sql $dbClean "select id from agro360.fulfillment_shipment_items where shipment_id='$shipmentId' and stock_lot_id='$lotAId';").Trim()
    $itemBId = (Query-Sql $dbClean "select id from agro360.fulfillment_shipment_items where shipment_id='$shipmentId' and stock_lot_id='$lotBId';").Trim()
    Assert-Step "Itens da remessa localizados por lote" ($itemAId.Length -gt 10 -and $itemBId.Length -gt 10)

    $attemptFailed = @{
        occurredAt = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        destination = 'Fazenda Sol Nascente'
        responsibleId = '30000000-0000-0000-0000-000000000003'
        status = 'FAILED'
        reason = 'Problema no veiculo na primeira tentativa'
        evidencePending = $false
        idempotencyKey = 'attempt-failed-evo-' + [guid]::NewGuid().ToString('N')
        items = @(
            @{
                shipmentItemId = $itemAId
                acceptedQuantity = 0.0
                refusedQuantity = 5.0
                reason = '5 sacas recusadas na descarga'
            }
        )
    }
    $attemptFailedRes = Call-Api $apiUrl "/api/logistics/trips/fulfillment/$shipmentId/attempts" 'POST' $attemptFailed $tokenA @(201)
    Assert-Step "Tentativa frustrada registrada (FAILED, recusa 5)" ($attemptFailedRes.StatusCode -eq 201)

    $shipStatusFailed = (Query-Sql $dbClean "select status from agro360.fulfillment_shipments where id='$shipmentId';").Trim()
    $schStatusFailed = (Query-Sql $dbClean "select status from agro360.sales_delivery_schedules where id='$schedule1Id';").Trim()
    $deliveredAfterFailed = (Query-Sql $dbClean "select delivered_quantity::bigint from agro360.sales_delivery_schedule_items where id='$sch1ItemId';").Trim()
    Assert-Step "Etapa 2: tentativa frustrada leva remessa a IN_DELIVERY" ($shipStatusFailed -eq 'IN_DELIVERY') "remessa: $shipStatusFailed"
    Assert-Step "Etapa 2: frustrada nao conta como entrega (delivered=0, compromisso DISPATCHED)" ($deliveredAfterFailed -eq '0' -and $schStatusFailed -eq 'DISPATCHED') "entregue=$deliveredAfterFailed compromisso=$schStatusFailed"

    $attemptPartial = @{
        occurredAt = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        destination = 'Fazenda Sol Nascente'
        responsibleId = '30000000-0000-0000-0000-000000000003'
        status = 'PARTIAL'
        reason = 'Segunda tentativa com aceite parcial'
        evidencePending = $false
        idempotencyKey = 'attempt-partial-evo-' + [guid]::NewGuid().ToString('N')
        items = @(
            @{
                shipmentItemId = $itemAId
                acceptedQuantity = 15.0
                refusedQuantity = 0.0
                reason = 'Aceite de 15 sacas'
            }
        )
    }
    $attemptPartialRes = Call-Api $apiUrl "/api/logistics/trips/fulfillment/$shipmentId/attempts" 'POST' $attemptPartial $tokenA @(201)
    $shipStatusPartial = (Query-Sql $dbClean "select status from agro360.fulfillment_shipments where id='$shipmentId';").Trim()
    $schStatusPartial = (Query-Sql $dbClean "select status from agro360.sales_delivery_schedules where id='$schedule1Id';").Trim()
    $deliveredAfterPartial = (Query-Sql $dbClean "select delivered_quantity::bigint from agro360.sales_delivery_schedule_items where id='$sch1ItemId';").Trim()
    Assert-Step "Etapa 3: somente o aceite avanca a entrega (15 SACAS)" ($attemptPartialRes.StatusCode -eq 201 -and $deliveredAfterPartial -eq '15') "entregue=$deliveredAfterPartial"
    Assert-Step "Compromisso em PARTIALLY_DELIVERED apos aceite parcial" ($schStatusPartial -eq 'PARTIALLY_DELIVERED') "status: $schStatusPartial"
    Assert-Step "Remessa aguarda retorno da recusa pendente (RETURN_PENDING)" ($shipStatusPartial -eq 'RETURN_PENDING') "status: $shipStatusPartial"

    $returnCmd = @{
        shipmentItemId = $itemAId
        quantity = 5.0
        reason = 'Retorno das 5 sacas recusadas na descarga'
        idempotencyKey = 'return-evo-' + [guid]::NewGuid().ToString('N')
    }
    $returnRes = Call-Api $apiUrl '/api/logistics/trips/fulfillment/returns' 'POST' $returnCmd $tokenA @(201)
    $returnId = ($returnRes.Content | ConvertFrom-Json).id
    Assert-Step "Retorno da recusa registrado (5 SACAS)" ($returnId.Length -gt 10) "ReturnId: $returnId"

    $returnDetail = (Call-Api $apiUrl "/api/logistics/trips/fulfillment/returns/$returnId" 'GET' $null $tokenA @(200)).Content | ConvertFrom-Json
    $receiveCmd = @{
        quantity = 5.0
        unit = 'sc'
        condition = 'INTACT'
        warehouseId = $warehouseId
        expectedVersion = [int64]$returnDetail.return.version
        idempotencyKey = 'receipt-evo-' + [guid]::NewGuid().ToString('N')
    }
    $receiveRes = Call-Api $apiUrl "/api/logistics/trips/fulfillment/returns/$returnId/receipts" 'POST' $receiveCmd $tokenA @(201)
    $returnedQty = (Query-Sql $dbClean "select returned_quantity::bigint from agro360.fulfillment_shipment_items where id='$itemAId';").Trim()
    Assert-Step "Retorno recebido e conciliado no item (returned=5)" ($receiveRes.StatusCode -eq 201 -and $returnedQty -eq '5') "returned: $returnedQty"

    $attemptAccepted = @{
        occurredAt = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        destination = 'Fazenda Sol Nascente'
        responsibleId = '30000000-0000-0000-0000-000000000003'
        status = 'ACCEPTED'
        reason = 'Entrega do lote B integral'
        evidencePending = $false
        idempotencyKey = 'attempt-accepted-evo-' + [guid]::NewGuid().ToString('N')
        items = @(
            @{
                shipmentItemId = $itemBId
                acceptedQuantity = 10.0
                refusedQuantity = 0.0
                reason = 'Aceite de 10 sacas do lote B'
            }
        )
    }
    $attemptAcceptedRes = Call-Api $apiUrl "/api/logistics/trips/fulfillment/$shipmentId/attempts" 'POST' $attemptAccepted $tokenA @(201)
    $shipStatusFinal = (Query-Sql $dbClean "select status from agro360.fulfillment_shipments where id='$shipmentId';").Trim()
    $schStatusFinal = (Query-Sql $dbClean "select status from agro360.sales_delivery_schedules where id='$schedule1Id';").Trim()
    $schItemFinal = (Query-Sql $dbClean "select dispatched_quantity::bigint::text || '/' || delivered_quantity::bigint::text || '/' || quantity::bigint::text from agro360.sales_delivery_schedule_items where id='$sch1ItemId';").Trim()
    Assert-Step "Etapa 3: remessa RECONCILED com cobertura completa dos itens" ($shipStatusFinal -eq 'RECONCILED') "status: $shipStatusFinal"
    Assert-Step "Compromisso final em PARTIALLY_DELIVERED (25/30)" ($schStatusFinal -eq 'PARTIALLY_DELIVERED' -and $schItemFinal -eq '30/25/30') "status=$schStatusFinal expedido/entregue/qtd=$schItemFinal"

    # =========================================================================
    # BLOCO 12 - ETAPA 4: LIQUIDACAO ADMINISTRATIVA (ENTREGA != LIQUIDACAO)
    # =========================================================================
    $finCount = "select (select count(*) from agro360.finance_commercial_receivables where tenant_id='$tenantAId')::text||','||(select count(*) from agro360.finance_receivables where tenant_id='$tenantAId')::text||','||(select count(*) from agro360.fiscal_financial_integrations where tenant_id='$tenantAId')::text;"
    $finBaseline = (Query-Sql $dbClean $finCount).Trim()

    $sch1PreSettle = (Call-Api $apiUrl "/api/commercial/schedules/$schedule1Id" 'GET' $null $tokenA @(200)).Content | ConvertFrom-Json
    $settleActionOffered = @($sch1PreSettle.allowedActions) -contains 'settle'
    Assert-Step "Pre-settle: acao 'settle' oferecida ao estado ENTREGUE_PARCIAL" $settleActionOffered "actions: $($sch1PreSettle.allowedActions -join ', ')"

    # Versao desatualizada deve ser rejeitada com 409 ANTES da liquidacao real (guard de optimistic concurrency
    # sobre compromisso ainda nao liquidado; apos liquidado, o erro canonico e 422 settle_already_settled).
    $settleStale = @{
        expectedVersion = $sch1Version
        reason = 'Tentativa com versao desatualizada'
        idempotencyKey = 'settle-stale-evo-' + [guid]::NewGuid().ToString('N')
    }
    $settleStaleRes = Call-Api $apiUrl "/api/commercial/schedules/$schedule1Id/settle" 'POST' $settleStale $tokenA @(409)
    Assert-Step "Liquidacao com versao desatualizada rejeitada (409)" ($settleStaleRes.StatusCode -eq 409)

    $settleReason = 'Liquidacao administrativa apos entrega parcial homologada (25 de 30)'
    $settleKey = 'settle-evo-' + [guid]::NewGuid().ToString('N')
    $settleCmd = @{
        expectedVersion = $sch1Version
        reason = $settleReason
        idempotencyKey = $settleKey
    }
    # O version do schedule mudou durante as tentativas; usa a versao atualizada.
    $settleCmd.expectedVersion = [int64]$sch1PreSettle.version
    $settleRes = Call-Api $apiUrl "/api/commercial/schedules/$schedule1Id/settle" 'POST' $settleCmd $tokenA @(204)
    Assert-Step "Liquidacao executada (204 No Content)" ($settleRes.StatusCode -eq 204)

    $settleReplay = Call-Api $apiUrl "/api/commercial/schedules/$schedule1Id/settle" 'POST' $settleCmd $tokenA @(204)
    Assert-Step "Replay idempotente da liquidacao (mesma chave e payload, 204)" ($settleReplay.StatusCode -eq 204)

    $settleCmdDiff = @{
        expectedVersion = $settleCmd.expectedVersion
        reason = 'Motivo divergente para provar conflito de idempotencia'
        idempotencyKey = $settleKey
    }
    $settleConflict = Call-Api $apiUrl "/api/commercial/schedules/$schedule1Id/settle" 'POST' $settleCmdDiff $tokenA @(409)
    Assert-Step "Mesma chave com payload diferente rejeitada (409)" ($settleConflict.StatusCode -eq 409)

    # Novo comando (chave nova, versao atual) sobre compromisso ja liquidado: 422 canonico de dominio.
    $settleAgain = @{
        expectedVersion = [int64]$sch1PreSettle.version
        reason = 'Nova tentativa apos liquidacao (chave nova)'
        idempotencyKey = 'settle-again-evo-' + [guid]::NewGuid().ToString('N')
    }
    $settleAgainRes = Call-Api $apiUrl "/api/commercial/schedules/$schedule1Id/settle" 'POST' $settleAgain $tokenA @(422)
    Assert-Step "Liquidacao sobre o ja liquidado rejeitada (422 settle_already_settled)" ($settleAgainRes.Content -match 'sales\.settle_already_settled') "body: $($settleAgainRes.Content)"

    $sch1Post = (Call-Api $apiUrl "/api/commercial/schedules/$schedule1Id" 'GET' $null $tokenA @(200)).Content | ConvertFrom-Json
    $settledFilled = ($null -ne $sch1Post.settledAt) -and ($null -ne $sch1Post.settledBy) -and ($sch1Post.settlementReason -eq $settleReason)
    Assert-Step "Liquidacao registrada sem alterar o status operacional" $settledFilled "status: $($sch1Post.Status) settledAt: $($sch1Post.settledAt) motivo: $($sch1Post.settlementReason)"
    Assert-Step "Status operacional permanece PARTIALLY_DELIVERED" ($sch1Post.Status -eq 'PARTIALLY_DELIVERED') "status: $($sch1Post.Status)"

    $postActions = @($sch1Post.allowedActions)
    $settleGone = (-not ($postActions -contains 'settle'))
    $blockFilled = [string]$sch1Post.blockReason -like '*liquidado*'
    Assert-Step "Pos-settle: 'settle' sai das acoes e bloqueio registra liquidacao" ($settleGone -and $blockFilled) "actions: $($postActions -join ', ') block: $($sch1Post.blockReason)"

    $costReal = ($sch1Post.dispatchedTotalCost -eq 0) -and ($sch1Post.dispatchedCostAvailable -eq $true)
    Assert-Step "Liquidacao nao fabrica custo (read model = soma real dos movimentos: 0)" $costReal "total: $($sch1Post.dispatchedTotalCost) disponivel: $($sch1Post.dispatchedCostAvailable)"

    $finAfterSettle = (Query-Sql $dbClean $finCount).Trim()
    Assert-Step "SETTLE nao cria registro financeiro ou fiscal (sem simulacao de NF/credito)" ($finAfterSettle -eq $finBaseline) "antes=$finBaseline depois=$finAfterSettle"

    $movCostZeroAfter = (Query-Sql $dbClean "select count(*) from agro360.inventory_stock_movements where reference_id='$shipmentId' and movement_type='SALE' and unit_cost=0 and total_cost=0;").Trim()
    Assert-Step "SETTLE nao escreve nos movimentos (mesmos 2, custo inalterado)" ($movCostZeroAfter -eq '2') "custo_zero_inalterado: $movCostZeroAfter"

    $freshPost = (Call-Api $apiUrl "/api/commercial/schedules/$schedule1Id" 'GET' $null $tokenA @(200)).Content | ConvertFrom-Json
    $reschedSealed = @{
        plannedDate = (Get-Date).AddDays(6).ToString('yyyy-MM-dd')
        expectedVersion = [int64]$freshPost.version
        reason = 'Tentativa de reprogramar compromisso liquidado'
        idempotencyKey = 'resched-sealed-evo-' + [guid]::NewGuid().ToString('N')
        items = @(
            @{ scheduleItemId = $sch1ItemId; quantity = 30.0 }
        )
    }
    $reschedSealedRes = Call-Api $apiUrl "/api/commercial/schedules/$schedule1Id/reschedule" 'PUT' $reschedSealed $tokenA @(409)
    Assert-Step "Selagem: reprogramacao do liquidado rejeitada com codigo especifico" ($reschedSealedRes.Content -match 'sales\.schedule_settled_locked') "body: $($reschedSealedRes.Content)"

    $cancelSealed = @{
        expectedVersion = [int64]$freshPost.version
        reason = 'Tentativa de cancelar compromisso liquidado'
        idempotencyKey = 'cancel-sealed-evo-' + [guid]::NewGuid().ToString('N')
    }
    $cancelSealedRes = Call-Api $apiUrl "/api/commercial/schedules/$schedule1Id/cancel" 'POST' $cancelSealed $tokenA @(409)
    Assert-Step "Selagem: cancelamento do liquidado rejeitado com codigo especifico" ($cancelSealedRes.Content -match 'sales\.schedule_settled_locked') "body: $($cancelSealedRes.Content)"

    $expectedResultVersion = ([int64]$freshPost.version).ToString()
    $opsRow = (Query-Sql $dbClean "select operation || ':' || result_version::text from agro360.sales_delivery_schedule_operations where schedule_id='$schedule1Id' and operation='SETTLE' order by result_version;").Trim()
    Assert-Step "Operacao SETTLE registrada como primeira classe do compromisso" ($opsRow -eq "SETTLE:$expectedResultVersion") "row: $opsRow esperado: SETTLE:$expectedResultVersion"

    $eventCount = (Query-Sql $dbClean "select count(*) from agro360.sales_commercial_events where aggregate_id='$schedule1Id' and event_type='DELIVERY_SCHEDULE_SETTLED';").Trim()
    Assert-Step "Evento DELIVERY_SCHEDULE_SETTLED auditado" ($eventCount -eq '1') "eventos: $eventCount"

    # =========================================================================
    # BLOCO 13 - NEGATIVA: LIQUIDACAO DE COMPROMISSO NAO ENTREGUE E REJEITADA
    # =========================================================================
    $orderNegId = [guid]::NewGuid().ToString()
    $orderNegItemId = [guid]::NewGuid().ToString()
    Exec-Sql $dbClean @"
insert into agro360.sales_orders(id, tenant_id, order_number, customer_id, status, total_amount, freight, payment_terms, created_by, updated_by)
values('$orderNegId', '$tenantAId', 'PED-EVO-NEG', '$customerId', 'APPROVED', 1000.00, 0, '30 dias', '30000000-0000-0000-0000-000000000003', '30000000-0000-0000-0000-000000000003');

insert into agro360.sales_order_items(id, tenant_id, order_id, product_id, quantity, unit, unit_price, total_amount)
values('$orderNegItemId', '$tenantAId', '$orderNegId', '$productId', 10.00, 'sc', 100.00, 1000.00);
"@
    $cmdSchNeg = @{
        plannedDate = (Get-Date).AddDays(4).ToString('yyyy-MM-dd')
        destination = 'Armazem Negativo'
        idempotencyKey = 'idem-sch-neg-' + [guid]::NewGuid().ToString('N')
        items = @(
            @{ orderItemId = $orderNegItemId; quantity = 10.0; unit = 'sc' }
        )
    }
    $createSchNegRes = Call-Api $apiUrl "/api/commercial/orders/$orderNegId/schedules" 'POST' $cmdSchNeg $tokenA @(201)
    $scheduleNegId = ($createSchNegRes.Content | ConvertFrom-Json).id
    $schNeg = (Call-Api $apiUrl "/api/commercial/schedules/$scheduleNegId" 'GET' $null $tokenA @(200)).Content | ConvertFrom-Json
    $negCostAbsent = ($null -eq $schNeg.dispatchedTotalCost) -and ($schNeg.dispatchedCostAvailable -eq $false)
    Assert-Step "Custo ausente permanece nulo sem movimentacao (custo ausente != zero)" $negCostAbsent "total: $($schNeg.dispatchedTotalCost) disponivel: $($schNeg.dispatchedCostAvailable)"

    $settleNeg = @{
        expectedVersion = [int64]$schNeg.version
        reason = 'Tentativa de liquidar compromisso ainda planejado'
        idempotencyKey = 'settle-neg-' + [guid]::NewGuid().ToString('N')
    }
    $settleNegRes = Call-Api $apiUrl "/api/commercial/schedules/$scheduleNegId/settle" 'POST' $settleNeg $tokenA @(422)
    Assert-Step "Liquidacao de compromisso nao entregue rejeitada" ($settleNegRes.Content -match 'sales\.settle_status_invalid') "status: $($schNeg.Status) body: $($settleNegRes.Content)"

    # =========================================================================
    # BLOCO 14 - ISOLAMENTO MULTITENANT SOBRE O ENDPOINT DE LIQUIDACAO
    # =========================================================================
    $crossGet = Call-Api $apiUrl "/api/commercial/schedules/$schedule1Id" 'GET' $null $tokenB @(403, 404)
    Assert-Step "Tenant B nao consulta o compromisso do Tenant A" ($crossGet.StatusCode -in @(403, 404)) "Status: $($crossGet.StatusCode)"

    $crossSettle = @{
        expectedVersion = [int64]$freshPost.version
        reason = 'Tentativa cross-tenant de liquidacao'
        idempotencyKey = 'settle-cross-' + [guid]::NewGuid().ToString('N')
    }
    $crossSettleRes = Call-Api $apiUrl "/api/commercial/schedules/$schedule1Id/settle" 'POST' $crossSettle $tokenB @(403, 404)
    Assert-Step "Tenant B nao liquida o compromisso do Tenant A" ($crossSettleRes.StatusCode -in @(403, 404)) "Status: $($crossSettleRes.StatusCode)"

    Write-Host ""
    Write-Host "=== TODOS OS CENARIOS DA EVOLUCAO INTEGRADA (ETAPAS 1-4) FORAM HOMOLOGADOS COM EXITO! ===" -ForegroundColor Green
}
finally {
    foreach ($process in $processes) {
        try {
            if (-not $process.HasExited) {
                Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            }
        } catch { }
        try { $process.Dispose() } catch { }
    }
    if ($startedDatabase -and -not $KeepRunning) {
        & $pg_ctl -D "$dataDir" -m fast -w stop
    }
    foreach ($name in $savedEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name])
    }
    Write-Host "Evidencias preservadas em: $evidenceDir" -ForegroundColor Yellow
}
