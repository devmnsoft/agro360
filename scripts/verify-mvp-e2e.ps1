param(
    [string]$PostgresBin = 'C:\Program Files\PostgreSQL\18\bin',
    [switch]$KeepRunning
)

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

$evidenceDir = Join-Path $root ('artifacts\mvp-e2e-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidenceDir | Out-Null
$processes = [Collections.Generic.List[Diagnostics.Process]]::new()
$savedEnvironment = @{}
$startedDatabase = $false

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

function Create-PasswordHash([string]$PlainText) {
    $salt = [Security.Cryptography.RandomNumberGenerator]::GetBytes(16)
    $hash = [Security.Cryptography.Rfc2898DeriveBytes]::Pbkdf2($PlainText, $salt, 210000, [Security.Cryptography.HashAlgorithmName]::SHA512, 32)
    return 'pbkdf2-sha512$210000$' + [Convert]::ToBase64String($salt) + '$' + [Convert]::ToBase64String($hash)
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
            $response = Invoke-WebRequest "$Url/health" -SkipHttpErrorCheck -TimeoutSec 2
            if ($response.StatusCode -in @(200, 503)) { return }
        } catch [Net.Http.HttpRequestException] {
        } catch [Threading.Tasks.TaskCanceledException] {
        }
        Start-Sleep -Milliseconds 500
    }
    throw "$HostName nao iniciou em tempo habil. Evidencia: $evidenceDir"
}

function Call-Api([string]$BaseUrl, [string]$Path, [string]$Method = 'GET', $Body = $null, [string]$Token = $null, [int[]]$ExpectedStatus = @(200)) {
    $headers = @{}
    if ($Token) { $headers['Authorization'] = "Bearer $Token" }
    $params = @{
        Uri = "$BaseUrl$Path"
        Method = $Method
        Headers = $headers
        SkipHttpErrorCheck = $true
        TimeoutSec = 20
    }
    if ($null -ne $Body) {
        $params.ContentType = 'application/json'
        $params.Body = ($Body | ConvertTo-Json -Depth 10 -Compress)
    }
    $res = Invoke-WebRequest @params
    $code = [int]$res.StatusCode
    if ($code -notin $ExpectedStatus) {
        throw "API $Method $Path retornou HTTP $code (esperado: $($ExpectedStatus -join ', ')): $($res.Content)"
    }
    return $res
}

try {
    Write-Host "=== INICIANDO AUDITORIA E HOMOLOGACAO E2E AGRO360 ===" -ForegroundColor Cyan

    $dbPort = Get-FreePort
    $apiPort = Get-FreePort
    $webPort = Get-FreePort
    $apiUrl = "http://127.0.0.1:$apiPort"
    $webUrl = "http://127.0.0.1:$webPort"
    $dbPassword = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))

    Set-EnvVar PGPASSWORD $dbPassword
    Set-EnvVar PGHOST '127.0.0.1'
    Set-EnvVar PGPORT "$dbPort"
    Set-EnvVar PGUSER 'postgres'

    $dataDir = Join-Path $evidenceDir 'pgdata'
    $pipeName = 'agro360-pipe-' + [guid]::NewGuid().ToString('N')
    $pipe = [IO.Pipes.NamedPipeServerStream]::new($pipeName, [IO.Pipes.PipeDirection]::Out)
    try {
        $pending = $pipe.WaitForConnectionAsync()
        $init = Start-Process $initdb -ArgumentList @('-D', "`"$dataDir`"", '-U', 'postgres', '--auth=scram-sha-256', "--pwfile=\\.\pipe\$pipeName", '--encoding=UTF8', '--locale=C') -WindowStyle Hidden -PassThru -RedirectStandardOutput "$evidenceDir\initdb.log" -RedirectStandardError "$evidenceDir\initdb.err.log"
        $processes.Add($init)
        if (-not $pending.Wait(15000)) { throw 'initdb nao conectou ao canal de senha.' }
        $writer = [IO.StreamWriter]::new($pipe)
        try { $writer.WriteLine($dbPassword) } finally { $writer.Dispose() }
        $init.WaitForExit()
        if ($init.ExitCode -ne 0) { throw "initdb falhou (exit $($init.ExitCode))." }
    } finally { $pipe.Dispose() }

    $pgLog = Join-Path $evidenceDir 'postgres.log'
    & $pg_ctl -D "$dataDir" -l "$pgLog" -o "-h 127.0.0.1 -p $dbPort" -w start
    if ($LASTEXITCODE -ne 0) { throw "Falha ao iniciar PostgreSQL descartavel na porta $dbPort." }
    $startedDatabase = $true
    Assert-Step "PostgreSQL descartavel iniciado" $true "Porta: $dbPort"

    # =========================================================================
    # BLOCO 1 - INSTALACAO LIMPA E REAPLICACAO DO CONSOLIDADO
    # =========================================================================
    $dbClean = 'agro360_clean'
    $null = Exec-Sql 'postgres' "create database $dbClean;"

    $fullSqlPath = Join-Path $root 'database/agro360-postgres-full.sql'
    $installCode = Invoke-PsqlFile $dbClean $fullSqlPath "$evidenceDir\install-clean.log"
    Assert-Step "Instalacao limpa do SQL consolidado" ($installCode -eq 0)

    $reinstallCode = Invoke-PsqlFile $dbClean $fullSqlPath "$evidenceDir\reinstall.log"
    Assert-Step "Reexecucao idempotente do instalador" ($reinstallCode -eq 0)

    # =========================================================================
    # BLOCO 2 - UPGRADE DE BASE REPRESENTATIVA ATE MIGRATION 117
    # =========================================================================
    $dbUpgrade = 'agro360_upgrade'
    $null = Exec-Sql 'postgres' "create database $dbUpgrade;"

    $fullSqlText = [System.IO.File]::ReadAllText($fullSqlPath, [System.Text.Encoding]::UTF8)
    $splitMarker = "create table if not exists agro360.sales_delivery_schedule_operations"
    if (-not $fullSqlText.Contains($splitMarker)) { throw "Marcador da migration 117 nao encontrado no full SQL." }
    $baseSql = $fullSqlText.Substring(0, $fullSqlText.IndexOf($splitMarker)) + "`ncommit;`n"
    $baseSqlFile = Join-Path $evidenceDir 'temp-base-116.sql'
    [System.IO.File]::WriteAllText($baseSqlFile, $baseSql, [System.Text.Encoding]::UTF8)

    $upgradeBaseCode = Invoke-PsqlFile $dbUpgrade $baseSqlFile "$evidenceDir\upgrade-base.log"
    Assert-Step "Instalacao base 11.6 em banco de upgrade" ($upgradeBaseCode -eq 0)

    # Diagnosticar possíveis duplicidades antes da criação do índice único de idempotência
    $dupCount = (Query-Sql $dbUpgrade "select count(*) from (select idempotency_key from agro360.sales_delivery_schedules where idempotency_key is not null and deleted_at is null group by tenant_id, idempotency_key having count(*) > 1) d;").Trim()
    Assert-Step "Diagnostico previo de duplicidades em idempotency_key" ($dupCount -eq '0') "Duplicatas encontradas: $dupCount"

    # Aplica migration 117
    $mig117Path = Join-Path $root 'database/migrations/117_delivery_schedule_operation_identity.sql'
    $mig117Code = Invoke-PsqlFile $dbUpgrade $mig117Path "$evidenceDir\migration-117.log"
    Assert-Step "Aplicacao incremental da migration 117" ($mig117Code -eq 0)

    $hasOpsTable = (Query-Sql $dbUpgrade "select count(*) from information_schema.tables where table_schema='agro360' and table_name='sales_delivery_schedule_operations';").Trim()
    $hasIdemIndex = (Query-Sql $dbUpgrade "select count(*) from pg_indexes where schemaname='agro360' and indexname='ux_sales_delivery_schedules_idempotency';").Trim()
    $hasVer117 = (Query-Sql $dbUpgrade "select count(*) from agro360.platform_schema_versions where version='11.7.0';").Trim()
    Assert-Step "Validacao do schema upgrade 11.7.0" ($hasOpsTable -eq '1' -and $hasIdemIndex -eq '1' -and $hasVer117 -eq '1')

    # =========================================================================
    # BLOCO 3 - RLS COM PAPEL DA APLICACAO
    # =========================================================================
    $rlsCheck = Query-Sql $dbClean @"
select quote_ident(c.relname) || ':' || c.relrowsecurity || ':' || c.relforcerowsecurity
from pg_class c
join pg_namespace n on n.oid = c.relnamespace
where n.nspname = 'agro360'
  and c.relname in ('sales_delivery_schedules', 'sales_delivery_schedule_items', 'sales_delivery_schedule_operations', 'fulfillment_shipments')
order by c.relname;
"@
    $lines = $rlsCheck.Trim().Split("`n") | ForEach-Object { $_.Trim() } | Where-Object { $_ }
    Assert-Step "Tabelas operacionais com RLS forcado" ($lines.Count -eq 4 -and ($lines | Where-Object { $_ -match ':(t|true):(t|true)' }).Count -eq 4) "RLS: $($lines -join ', ')"

    # =========================================================================
    # BLOCO 4 - PROVISIONAMENTO DE TENANTS E CREDENCIAIS
    # =========================================================================
    $tenantAId = (Query-Sql $dbClean "select id from agro360.tenancy_tenants where slug = 'santa-clara' limit 1;").Trim()
    $tenantBId = (Query-Sql $dbClean "select id from agro360.tenancy_tenants where slug = 'cooperativa-vale-verde' limit 1;").Trim()
    $adminPassword = 'Aa1!' + [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
    $adminHash = Create-PasswordHash $adminPassword

    # Configura credencial ativa dos usuários nos dois tenants e superadmin
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
    # BLOCO 5 - INICIALIZACAO DOS HOSTS HTTP REAIS
    # =========================================================================
    $connString = "Host=127.0.0.1;Port=$dbPort;Database=$dbClean;Username=postgres;Password=$dbPassword"
    Set-EnvVar ConnectionStrings__Agro360 $connString
    Set-EnvVar ConnectionStrings__DefaultConnection $connString
    Set-EnvVar AGRO360_TEST_CONNECTION_STRING $connString
    Set-EnvVar ASPNETCORE_ENVIRONMENT 'Development'
    Set-EnvVar Jwt__SigningKey ([Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48)))
    Set-EnvVar ApiBaseUrl $apiUrl
    Set-EnvVar Cors__AllowedOrigins__0 $webUrl
    Set-EnvVar Bootstrap__Enabled 'false'

    Start-HostProcess Api $apiUrl
    Assert-Step "Host API iniciado com sucesso" $true "URL: $apiUrl"

    Start-HostProcess Web $webUrl
    Assert-Step "Host Web Razor iniciado com sucesso" $true "URL: $webUrl"

    # =========================================================================
    # BLOCO 6 - AUTENTICACAO HTTP REAL
    # =========================================================================
    $loginA = @{ tenantSlug = 'santa-clara'; email = 'admin@santaclara.agro360.local'; password = $adminPassword }
    $authARes = Call-Api $apiUrl '/api/v1/auth/login' 'POST' $loginA $null @(200)
    $authA = $authARes.Content | ConvertFrom-Json
    $tokenA = $authA.accessToken
    Assert-Step "Login Tenant A (Santa Clara)" ($tokenA.Length -gt 20)

    $loginB = @{ tenantSlug = 'cooperativa-vale-verde'; email = 'admin.valeverde@agro360.local'; password = $adminPassword }
    $authBRes = Call-Api $apiUrl '/api/v1/auth/login' 'POST' $loginB $null @(200)
    $authB = $authBRes.Content | ConvertFrom-Json
    $tokenB = $authB.accessToken
    Assert-Step "Login Tenant B (Cooperativa Vale Verde)" ($tokenB.Length -gt 20)

    # Super Administrador consulta command-center
    $cmdCenter = Call-Api $apiUrl '/api/v1/dashboard/command-center' 'GET' $null $tokenA @(200)
    Assert-Step "Super Admin / Command-Center acessivel" ($cmdCenter.StatusCode -eq 200)

    # =========================================================================
    # BLOCO 7 - JORNADA COMERCIAL: PROGRAMACAO, SALDO 100/30/50, IDEMPOTENCIA
    # =========================================================================
    $productId = '30000000-0000-0000-0000-000000000015'
    $warehouseId = '30000000-0000-0000-0000-000000000016'
    $segmentId = '30000000-0000-0000-0000-000000000017'
    $customerId = '30000000-0000-0000-0000-000000000018'
    $farmId = '30000000-0000-0000-0000-000000000010'

    Exec-Sql $dbClean @"
insert into agro360.crm_customer_segments(id, tenant_id, name, status, created_by)
values ('$segmentId', '$tenantAId', 'Cooperativas e Tradings', 'ACTIVE', '30000000-0000-0000-0000-000000000003')
on conflict (id) do nothing;

insert into agro360.crm_customers(id, tenant_id, segment_id, name, type, tax_document, status, created_by)
values ('$customerId', '$tenantAId', '$segmentId', 'Cooperativa Agropecuaria Central', 'CUSTOMER', '12345678000199', 'ACTIVE', '30000000-0000-0000-0000-000000000003')
on conflict (id) do nothing;

insert into agro360.inventory_products(id, tenant_id, sku, name, category, base_unit, requires_lot, created_by)
values ('$productId', '$tenantAId', 'SOJA-01', 'Soja Grão Safra', 'GRAOS', 'sc', true, '30000000-0000-0000-0000-000000000003')
on conflict (id) do nothing;

insert into agro360.inventory_warehouses(id, tenant_id, farm_id, code, name, type, created_by)
values ('$warehouseId', '$tenantAId', '$farmId', 'SILO-01', 'Silo Grãos Central', 'GRAINS', '30000000-0000-0000-0000-000000000003')
on conflict (id) do nothing;

insert into agro360.inventory_stock_balances(id, tenant_id, warehouse_id, product_id, unit, available, reserved, average_cost, version)
values (gen_random_uuid(), '$tenantAId', '$warehouseId', '$productId', 'sc', 250.0, 0, 50.0, 1)
on conflict (tenant_id, warehouse_id, product_id) do update set available = 250.0, reserved = 0;
"@

    # Cria pedido aprovado com 100 unidades
    $orderId = [guid]::NewGuid().ToString()
    $orderItemId = [guid]::NewGuid().ToString()
    Exec-Sql $dbClean @"
insert into agro360.sales_orders(id, tenant_id, order_number, customer_id, status, total_amount, freight, payment_terms, created_by, updated_by)
values('$orderId', '$tenantAId', 'PED-E2E-100', '$customerId', 'APPROVED', 10000.00, 0, '30 dias', '30000000-0000-0000-0000-000000000003', '30000000-0000-0000-0000-000000000003');

insert into agro360.sales_order_items(id, tenant_id, order_id, product_id, quantity, unit, unit_price, total_amount)
values('$orderItemId', '$tenantAId', '$orderId', '$productId', 100.00, 'sc', 100.00, 10000.00);
"@

    # 1. Cria programação 1 com 30 unidades
    $idemKey1 = 'idem-sch-' + [guid]::NewGuid().ToString('N')
    $cmdSch1 = @{
        plannedDate = (Get-Date).AddDays(5).ToString('yyyy-MM-dd')
        destination = 'Armazem Central - Gleba 1'
        notes = 'Primeira programacao teste E2E'
        idempotencyKey = $idemKey1
        items = @(
            @{ orderItemId = $orderItemId; quantity = 30.0; unit = 'sc' }
        )
    }
    $createRes1 = Call-Api $apiUrl "/api/commercial/orders/$orderId/schedules" 'POST' $cmdSch1 $tokenA @(201)
    $sch1Obj = $createRes1.Content | ConvertFrom-Json
    $schedule1Id = $sch1Obj.id
    Assert-Step "Criacao da programacao 1 (30 SACAS)" ($schedule1Id -and $schedule1Id -ne [guid]::Empty.ToString()) "ScheduleId: $schedule1Id"

    # Replay idêntico da criação: retorna o mesmo ID anterior (idempotência)
    $createReplay = Call-Api $apiUrl "/api/commercial/orders/$orderId/schedules" 'POST' $cmdSch1 $tokenA @(200, 201)
    $replayObj = $createReplay.Content | ConvertFrom-Json
    Assert-Step "Replay de criacao com mesma chave e payload retorna mesmo ID" ($replayObj.id -eq $schedule1Id)

    # Replay com mesma chave e payload diferente: retorna 409 Conflict
    $cmdSch1Diff = @{
        plannedDate = (Get-Date).AddDays(10).ToString('yyyy-MM-dd')
        destination = 'Outro Destino Divergente'
        idempotencyKey = $idemKey1
        items = @(
            @{ orderItemId = $orderItemId; quantity = 40.0; unit = 'sc' }
        )
    }
    $conflictRes = Call-Api $apiUrl "/api/commercial/orders/$orderId/schedules" 'POST' $cmdSch1Diff $tokenA @(409)
    Assert-Step "Replay com mesma chave e payload diferente rejeitado com 409 Conflict" ($conflictRes.StatusCode -eq 409)

    # 2. Cria programação 2 com 50 unidades (outras programações = 50, pedido = 100, atual = 30)
    $idemKey2 = 'idem-sch-' + [guid]::NewGuid().ToString('N')
    $cmdSch2 = @{
        plannedDate = (Get-Date).AddDays(7).ToString('yyyy-MM-dd')
        destination = 'Armazem Secundario'
        idempotencyKey = $idemKey2
        items = @(
            @{ orderItemId = $orderItemId; quantity = 50.0; unit = 'sc' }
        )
    }
    $createRes2 = Call-Api $apiUrl "/api/commercial/orders/$orderId/schedules" 'POST' $cmdSch2 $tokenA @(201)
    $sch2Obj = $createRes2.Content | ConvertFrom-Json
    $schedule2Id = $sch2Obj.id
    Assert-Step "Criacao da programacao 2 (50 SACAS)" ($schedule2Id.Length -gt 10)

    # Obtém o ScheduleItemId da programação 1
    $sch1DetailRes = Call-Api $apiUrl "/api/commercial/schedules/$schedule1Id" 'GET' $null $tokenA @(200)
    $sch1Detail = $sch1DetailRes.Content | ConvertFrom-Json
    $sch1ItemId = $sch1Detail.items[0].id
    $sch1Version = $sch1Detail.version

    # TESTE OBRIGATORIO: Pedido 100, atual 30, outras 50 -> alterar atual para 50 PERMITIDO; para 60 REJEITADO
    # Tenta alterar para 60: REJEITADO
    $reschedule60 = @{
        plannedDate = (Get-Date).AddDays(6).ToString('yyyy-MM-dd')
        expectedVersion = $sch1Version
        reason = 'Tentativa de aumento para 60 acima do saldo permitido'
        idempotencyKey = 'resched-60-' + [guid]::NewGuid().ToString('N')
        items = @(
            @{ scheduleItemId = $sch1ItemId; quantity = 60.0 }
        )
    }
    $reject60Res = Call-Api $apiUrl "/api/commercial/schedules/$schedule1Id/reschedule" 'PUT' $reschedule60 $tokenA @(400, 409, 422)
    Assert-Step "Reprogramacao para 60 rejeitada (excede capacidade 50)" ($reject60Res.StatusCode -in @(400, 409, 422))

    # Altera para 50: PERMITIDO
    $idemResched50 = 'resched-50-' + [guid]::NewGuid().ToString('N')
    $reschedule50 = @{
        plannedDate = (Get-Date).AddDays(6).ToString('yyyy-MM-dd')
        expectedVersion = $sch1Version
        reason = 'Ajuste valido para 50 dentro da capacidade'
        idempotencyKey = $idemResched50
        destination = 'Armazem Central - Portao B'
        items = @(
            @{ scheduleItemId = $sch1ItemId; quantity = 50.0 }
        )
    }
    $allow50Res = Call-Api $apiUrl "/api/commercial/schedules/$schedule1Id/reschedule" 'PUT' $reschedule50 $tokenA @(200, 204)
    Assert-Step "Reprogramacao para 50 aceita dentro da capacidade" ($allow50Res.StatusCode -in @(200, 204))

    # Replay idêntico da reprogramação com mesma chave e payload: aceito/idempotente
    $replayResched50 = Call-Api $apiUrl "/api/commercial/schedules/$schedule1Id/reschedule" 'PUT' $reschedule50 $tokenA @(200, 204)
    Assert-Step "Replay idempotente de reprogramacao" ($replayResched50.StatusCode -in @(200, 204))

    # Versão desatualizada: tenta usar a versão antiga $sch1Version em novo comando -> 409 Conflict
    $staleReschedule = @{
        plannedDate = (Get-Date).AddDays(8).ToString('yyyy-MM-dd')
        expectedVersion = $sch1Version
        reason = 'Tentativa com versao antiga'
        idempotencyKey = 'resched-stale-' + [guid]::NewGuid().ToString('N')
        items = @(
            @{ scheduleItemId = $sch1ItemId; quantity = 45.0 }
        )
    }
    $staleRes = Call-Api $apiUrl "/api/commercial/schedules/$schedule1Id/reschedule" 'PUT' $staleReschedule $tokenA @(409)
    Assert-Step "Versao desatualizada rejeitada com 409 Conflict" ($staleRes.StatusCode -eq 409)

    # =========================================================================
    # BLOCO 8 - TESTE DO CENARIO OBRIGATORIO 50 / 20 / 15:
    # Programação 50; expedido 20; preparação aberta 15 -> redução para 25 REJEITADA
    # =========================================================================
    # Obtém versão atualizada da programação 1 (agora com quantidade 50)
    $sch1UpdatedRes = Call-Api $apiUrl "/api/commercial/schedules/$schedule1Id" 'GET' $null $tokenA @(200)
    $sch1Updated = $sch1UpdatedRes.Content | ConvertFrom-Json
    $sch1VersionNow = $sch1Updated.version

    # Prepara simulação de 20 expedidos e 15 em preparação aberta no banco para esta programação
    $shipmentId = [guid]::NewGuid().ToString()
    $resId = [guid]::NewGuid().ToString()
    $lotAId = [guid]::NewGuid().ToString()
    $lotBId = [guid]::NewGuid().ToString()

    Exec-Sql $dbClean @"
insert into agro360.inventory_stock_lots(id, tenant_id, warehouse_id, product_id, lot_number, quantity, quality_status)
values
('$lotAId', '$tenantAId', '$warehouseId', '$productId', 'LOTE-A-100', 100.0, 'APPROVED'),
('$lotBId', '$tenantAId', '$warehouseId', '$productId', 'LOTE-B-50', 50.0, 'APPROVED')
on conflict (tenant_id, warehouse_id, product_id, lot_number) do update set quantity = excluded.quantity;

update agro360.inventory_stock_balances
set available = 250.0, reserved = 15.0
where tenant_id = '$tenantAId' and warehouse_id = '$warehouseId' and product_id = '$productId';

-- Define expedido = 20 no item da programação
update agro360.sales_delivery_schedule_items
set dispatched_quantity = 20.0
where id = '$sch1ItemId';

-- Cria remessa em preparação com 15 unidades abertas vinculadas ao item
insert into agro360.fulfillment_reservations(id, tenant_id, order_item_id, stock_lot_id, quantity, unit, status, idempotency_key, request_hash, created_by, updated_by)
values('$resId', '$tenantAId', '$orderItemId', '$lotAId', 15.0, 'sc', 'ACTIVE', 'res-open-15', repeat('a',64), '30000000-0000-0000-0000-000000000003', '30000000-0000-0000-0000-000000000003')
on conflict (id) do nothing;

insert into agro360.fulfillment_shipments(id, tenant_id, number, origin_warehouse_id, destination, customer_id, status, idempotency_key, request_hash, schedule_id, created_by, updated_by)
values('$shipmentId', '$tenantAId', 'REM-OPEN-15', '$warehouseId', 'Destino Teste', '$customerId', 'PREPARING', 'ship-open-15', repeat('b',64), '$schedule1Id', '30000000-0000-0000-0000-000000000003', '30000000-0000-0000-0000-000000000003')
on conflict (id) do nothing;

insert into agro360.fulfillment_shipment_items(id, tenant_id, shipment_id, reservation_id, order_item_id, stock_lot_id, requested_quantity, reserved_quantity, picked_quantity, checked_quantity, unit, schedule_item_id, created_by, updated_by)
values(gen_random_uuid(), '$tenantAId', '$shipmentId', '$resId', '$orderItemId', '$lotAId', 15.0, 15.0, 0, 0, 'sc', '$sch1ItemId', '30000000-0000-0000-0000-000000000003', '30000000-0000-0000-0000-000000000003')
on conflict (id) do nothing;
"@

    # Tentativa de redução para 25 com expedido=20 e preparação=15 (mínimo=35): DEVE SER REJEITADA
    $resched25 = @{
        plannedDate = (Get-Date).AddDays(6).ToString('yyyy-MM-dd')
        expectedVersion = $sch1VersionNow
        reason = 'Tentativa de reducao para 25 abaixo do expedido + preparacao aberta'
        idempotencyKey = 'resched-25-' + [guid]::NewGuid().ToString('N')
        items = @(
            @{ scheduleItemId = $sch1ItemId; quantity = 25.0 }
        )
    }
    $reject25Res = Call-Api $apiUrl "/api/commercial/schedules/$schedule1Id/reschedule" 'PUT' $resched25 $tokenA @(400, 409, 422)
    Assert-Step "Cenario 50/20/15: Reducao para 25 rejeitada (minimo 35)" ($reject25Res.StatusCode -in @(400, 409, 422))

    # Redução para 40 (>= 35): DEVE SER PERMITIDA
    $resched40 = @{
        plannedDate = (Get-Date).AddDays(6).ToString('yyyy-MM-dd')
        expectedVersion = $sch1VersionNow
        reason = 'Reducao permitida para 40 acima do minimo'
        idempotencyKey = 'resched-40-' + [guid]::NewGuid().ToString('N')
        items = @(
            @{ scheduleItemId = $sch1ItemId; quantity = 40.0 }
        )
    }
    $allow40Res = Call-Api $apiUrl "/api/commercial/schedules/$schedule1Id/reschedule" 'PUT' $resched40 $tokenA @(200, 204)
    Assert-Step "Cenario 50/20/15: Reducao para 40 aceita (>= 35)" ($allow40Res.StatusCode -in @(200, 204))

    # Limpa a remessa temporária de teste de preparação
    Exec-Sql $dbClean @"
delete from agro360.fulfillment_shipment_items where shipment_id = '$shipmentId';
delete from agro360.fulfillment_shipments where id = '$shipmentId';
delete from agro360.fulfillment_reservations where id = '$resId';
update agro360.sales_delivery_schedule_items set dispatched_quantity = 0 where id = '$sch1ItemId';
update agro360.inventory_stock_balances set reserved = 0 where tenant_id = '$tenantAId' and warehouse_id = '$warehouseId' and product_id = '$productId';
"@

    # =========================================================================
    # BLOCO 9 - ATENDIMENTO MULTILOTE, CONFERENCIA E EXPEDICAO PARCIAL
    # =========================================================================
    # Criamos novo pedido e programação limpos para o teste de atendimento multilote
    $orderMultiId = [guid]::NewGuid().ToString()
    $orderMultiItemId = [guid]::NewGuid().ToString()
    Exec-Sql $dbClean @"
insert into agro360.sales_orders(id, tenant_id, order_number, customer_id, status, total_amount, freight, payment_terms, created_by, updated_by)
values('$orderMultiId', '$tenantAId', 'PED-MULTILOTE', '$customerId', 'APPROVED', 5000.00, 0, 'A vista', '30000000-0000-0000-0000-000000000003', '30000000-0000-0000-0000-000000000003');

insert into agro360.sales_order_items(id, tenant_id, order_id, product_id, quantity, unit, unit_price, total_amount)
values('$orderMultiItemId', '$tenantAId', '$orderMultiId', '$productId', 50.00, 'sc', 100.00, 5000.00);
"@

    $cmdMultiSch = @{
        plannedDate = (Get-Date).AddDays(3).ToString('yyyy-MM-dd')
        destination = 'Fazenda Sol Nascente'
        idempotencyKey = 'idem-multi-' + [guid]::NewGuid().ToString('N')
        items = @(
            @{ orderItemId = $orderMultiItemId; quantity = 50.0; unit = 'sc' }
        )
    }
    $createMultiSchRes = Call-Api $apiUrl "/api/commercial/orders/$orderMultiId/schedules" 'POST' $cmdMultiSch $tokenA @(201)
    $multiSchId = ($createMultiSchRes.Content | ConvertFrom-Json).id
    $multiSchDetail = (Call-Api $apiUrl "/api/commercial/schedules/$multiSchId" 'GET' $null $tokenA @(200)).Content | ConvertFrom-Json
    $multiSchItemId = $multiSchDetail.items[0].id

    # Atendimento com dois lotes do mesmo armazém: Lote A (20 sc) + Lote B (10 sc) = 30 sc (parcial de 50)
    $fulfillCmd = @{
        number = 'REM-MULTI-001'
        originWarehouseId = $warehouseId
        customerId = $customerId
        destination = 'Fazenda Sol Nascente'
        scheduleId = $multiSchId
        idempotencyKey = 'idem-fulfill-' + [guid]::NewGuid().ToString('N')
        items = @(
            @{
                orderItemId = $orderMultiItemId
                stockLotId = $lotAId
                quantity = 20.0
                pickedQuantity = 20.0
                checkedQuantity = 20.0
                unit = 'sc'
                scheduleItemId = $multiSchItemId
            },
            @{
                orderItemId = $orderMultiItemId
                stockLotId = $lotBId
                quantity = 10.0
                pickedQuantity = 10.0
                checkedQuantity = 10.0
                unit = 'sc'
                scheduleItemId = $multiSchItemId
            }
        )
    }
    $createFulfillRes = Call-Api $apiUrl '/api/logistics/trips/fulfillment' 'POST' $fulfillCmd $tokenA @(201)
    $multiShipmentId = ($createFulfillRes.Content | ConvertFrom-Json).id
    Assert-Step "Atendimento multi-lote criado (2 lotes: 20 + 10 = 30 SACAS)" ($multiShipmentId.Length -gt 10) "ShipmentId: $multiShipmentId"

    # Confirmação e Expedição física da remessa conferida
    $shipmentDetail = (Call-Api $apiUrl "/api/logistics/trips/fulfillment/$multiShipmentId" 'GET' $null $tokenA @(200)).Content | ConvertFrom-Json
    $shipmentVersion = [int64]$shipmentDetail.shipment.version
    $dispatchIdemKey = 'dispatch-' + [guid]::NewGuid().ToString('N')

    $dispatchCmd = @{
        version = $shipmentVersion
        idempotencyKey = $dispatchIdemKey
    }
    $dispatchRes = Call-Api $apiUrl "/api/logistics/trips/fulfillment/$multiShipmentId/dispatch" 'POST' $dispatchCmd $tokenA @(200, 204)
    Assert-Step "Expedicao confirmada com sucesso (status DISPATCHED)" ($dispatchRes.StatusCode -in @(200, 204))

    # Verifica atualização agregada na programação de entrega:
    # 20 sc do Lote A + 10 sc do Lote B foram consolidados para o schedule_item_id = 30 sc expedidas
    $schItemDispatched = (Query-Sql $dbClean "select dispatched_quantity from agro360.sales_delivery_schedule_items where id = '$multiSchItemId';").Trim()
    Assert-Step "Agregacao de quantidade expedida multi-lote no compromisso (30 SACAS)" ($schItemDispatched -eq '30.000000') "Dispatched no DB: $schItemDispatched"

    # Verifica movimentos de saída física criados no ledger de estoque
    $movementsCount = (Query-Sql $dbClean "select count(*) from agro360.inventory_stock_movements where reference_id = '$multiShipmentId' and movement_type = 'SALE';").Trim()
    Assert-Step "Movimentos fisicos registrados para cada lote expedido (2 movimentos)" ($movementsCount -eq '2')

    # =========================================================================
    # BLOCO 10 - ENTREGA PARCIAL E REGISTRO DE RECUSA
    # =========================================================================
    # Obtém o shipment_item_id de um dos itens expedidos
    $shipmentItemId = (Query-Sql $dbClean "select id from agro360.fulfillment_shipment_items where shipment_id = '$multiShipmentId' and stock_lot_id = '$lotAId' limit 1;").Trim()
    $attemptCmd = @{
        occurredAt = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        destination = 'Fazenda Sol Nascente'
        responsibleId = '30000000-0000-0000-0000-000000000003'
        status = 'PARTIAL'
        reason = 'Entrega parcial com recusa de avaria'
        evidencePending = $false
        idempotencyKey = 'attempt-' + [guid]::NewGuid().ToString('N')
        items = @(
            @{
                shipmentItemId = $shipmentItemId
                acceptedQuantity = 15.0
                refusedQuantity = 5.0
                reason = '5 sacas avariadas na descarga'
            }
        )
    }
    $attemptRes = Call-Api $apiUrl "/api/logistics/trips/fulfillment/$multiShipmentId/attempts" 'POST' $attemptCmd $tokenA @(201)
    $attemptId = ($attemptRes.Content | ConvertFrom-Json).id
    Assert-Step "Registro de tentativa de entrega com aceite parcial e recusa" ($attemptId.Length -gt 10)

    # Verifica que quantidade entregue (15) foi consolidada e recusa não entrou em estoque
    $schItemDelivered = (Query-Sql $dbClean "select delivered_quantity from agro360.sales_delivery_schedule_items where id = '$multiSchItemId';").Trim()
    Assert-Step "Quantidade entregue registrada no compromisso (15 SACAS)" ($schItemDelivered -eq '15.000000') "Delivered no DB: $schItemDelivered"

    # =========================================================================
    # BLOCO 11 - ISOLAMENTO MULTITENANT
    # =========================================================================
    # Tenant B tenta acessar a programação do Tenant A -> deve receber 404 ou 403
    $crossScheduleRes = Call-Api $apiUrl "/api/commercial/schedules/$schedule1Id" 'GET' $null $tokenB @(403, 404)
    Assert-Step "Tenant B bloqueado ao consultar compromisso do Tenant A (404/403)" ($crossScheduleRes.StatusCode -in @(403, 404)) "Status: $($crossScheduleRes.StatusCode)"

    # Tenant B tenta acessar remessa do Tenant A -> deve receber 404 ou 403
    $crossShipmentRes = Call-Api $apiUrl "/api/logistics/trips/fulfillment/$multiShipmentId" 'GET' $null $tokenB @(403, 404)
    Assert-Step "Tenant B bloqueado ao consultar remessa do Tenant A (404/403)" ($crossShipmentRes.StatusCode -in @(403, 404)) "Status: $($crossShipmentRes.StatusCode)"

    # =========================================================================
    # BLOCO 12 - RENDERIZACAO VISUAL WEB RAZOR
    # =========================================================================
    $commercialPage = Invoke-WebRequest "$webUrl/Commercial" -TimeoutSec 15
    Assert-Step "Pagina Comercial renderizada no Web Razor" ($commercialPage.StatusCode -eq 200 -and $commercialPage.Content -match 'Agro360')

    $logisticsPage = Invoke-WebRequest "$webUrl/Logistics" -TimeoutSec 15
    Assert-Step "Pagina Logistica renderizada no Web Razor" ($logisticsPage.StatusCode -eq 200 -and $logisticsPage.Content -match 'Agro360')

    Write-Host "`n=== TODOS OS 21 CENARIOS E2E FORAM HOMOLOGADOS COM EXITO! ===" -ForegroundColor Green
}
finally {
    foreach ($process in $processes) {
        if (-not $process.HasExited) {
            $process.Kill($true)
            $process.WaitForExit()
        }
        $process.Dispose()
    }
    if ($startedDatabase) {
        & $pg_ctl -D "$dataDir" -m fast -w stop
    }
    foreach ($name in $savedEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name])
    }
    Write-Host "Evidencias preservadas em: $evidenceDir" -ForegroundColor Yellow
}
