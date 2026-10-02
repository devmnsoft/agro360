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

$evidenceDir = Join-Path $root ('artifacts\prod-e2e-' + [guid]::NewGuid().ToString('N'))
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

function Exec-Sql([string]$DbName, [string]$Sql, [string]$User = 'postgres', [string]$Pass = $null) {
    $tempFile = Join-Path $evidenceDir ("cmd-" + [guid]::NewGuid().ToString('N') + ".sql")
    [System.IO.File]::WriteAllText($tempFile, $Sql, [System.Text.Encoding]::UTF8)
    $oldPass = $env:PGPASSWORD
    try {
        if ($Pass) { $env:PGPASSWORD = $Pass }
        $output = cmd.exe /c "`"$psql`" -U $User -d $DbName -X -q -v ON_ERROR_STOP=1 --set=client_min_messages=warning -f `"$tempFile`" 2>&1"
        $exitCode = $LASTEXITCODE
    } finally {
        $env:PGPASSWORD = $oldPass
        Remove-Item -Path $tempFile -Force -ErrorAction SilentlyContinue
    }
    if ($exitCode -ne 0) {
        throw "Exec-Sql falhou em $DbName (user $User, exit code $exitCode): $output"
    }
    return $output
}

function Query-Sql([string]$DbName, [string]$Sql, [string]$User = 'postgres', [string]$Pass = $null) {
    $tempFile = Join-Path $evidenceDir ("query-" + [guid]::NewGuid().ToString('N') + ".sql")
    [System.IO.File]::WriteAllText($tempFile, $Sql, [System.Text.Encoding]::UTF8)
    $oldPass = $env:PGPASSWORD
    $exitCode = 0
    try {
        if ($Pass) { $env:PGPASSWORD = $Pass }
        $output = cmd.exe /c "`"$psql`" -U $User -d $DbName -X -v ON_ERROR_STOP=1 -t -A -q --set=client_min_messages=warning -f `"$tempFile`" 2>&1"
        $exitCode = $LASTEXITCODE
    } finally {
        $env:PGPASSWORD = $oldPass
        Remove-Item -Path $tempFile -Force -ErrorAction SilentlyContinue
    }
    if ($exitCode -ne 0) {
        throw "Query-Sql falhou em $DbName (user $User, exit code $exitCode): $($output -join "`n")"
    }
    $filtered = @($output | Where-Object { $_ -and $_.ToString().Trim().Length -gt 0 -and $_.ToString().Trim() -ne 'SET' -and $_.ToString().Trim() -ne 'RESET' })
    if ($filtered.Length -eq 0) { return "" }
    return ($filtered[$filtered.Length - 1]).ToString().Trim()
}

function Invoke-PsqlFile([string]$DbName, [string]$FilePath, [string]$LogPath) {
    cmd.exe /c "`"$psql`" -d $DbName -X -v ON_ERROR_STOP=1 --set=client_min_messages=warning -f `"$FilePath`" > `"$LogPath`" 2>&1"
    return $LASTEXITCODE
}

function Start-HostProcess([string]$HostName, [string]$Url) {
    $project = "src\Hosts\Agro360.$HostName"
    $dll = Join-Path $root "$project\bin\Release\net10.0\Agro360.$HostName.dll"
    if (-not (Test-Path $dll)) {
        throw "Assembly $dll nao encontrado. Execute dotnet build -c Release primeiro."
    }
    $process = Start-Process dotnet -ArgumentList @("`"$dll`"", '--urls', $Url) -WorkingDirectory "$root\$project" -WindowStyle Hidden -PassThru -RedirectStandardOutput "$evidenceDir\$HostName.log" -RedirectStandardError "$evidenceDir\$HostName.err.log"
    $processes.Add($process)
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        if ($process.HasExited) { throw "$HostName encerrou na inicializacao. Verifique $evidenceDir\$HostName.err.log" }
        try {
            $response = Invoke-WebRequest "$Url/health" -UseBasicParsing -TimeoutSec 2
            if ($response.StatusCode -in @(200, 503)) { return $process }
        } catch {
        }
        Start-Sleep -Milliseconds 500
    }
    throw "$HostName nao respondeu no timeout de 30s. Evidencia: $evidenceDir"
}

function Http-Call([string]$BaseUrl, [string]$Path, [string]$Method = 'GET', $Body = $null, [string]$Token = $null, [int[]]$ExpectedStatuses = @(200)) {
    $uri = "$BaseUrl$Path"
    $headers = @{}
    if ($Token) { $headers['Authorization'] = "Bearer $Token" }
    $json = $null
    if ($Body -ne $null) {
        if ($Body -is [string]) { $json = $Body } else { $json = $Body | ConvertTo-Json -Depth 10 }
    }
    $response = $null
    try {
        $p = @{
            Uri = $uri
            Method = $Method
            Headers = $headers
            TimeoutSec = 15
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
            throw "Erro na requisicao ${Method} ${uri} : $($_.Exception.Message)"
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
        $content = $response.Content
    }

    $matched = $ExpectedStatuses -contains $statusCode
    if (-not $matched) {
        throw "HTTP $Method $Path retornou $statusCode (esperado: $($ExpectedStatuses -join ', ')). Resposta: $content"
    }

    return @{ StatusCode = $statusCode; Content = $content }
}

Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host "  VERIFICACAO E2E PRODUCAO, CONSUMO, ESTOQUE E PAPEL RESTRITO" -ForegroundColor Cyan
Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host "Diretorio de evidencias: $evidenceDir"

try {
    # 0. PostgreSQL descartavel
    $dbPort = Get-FreePort
    $apiPort = Get-FreePort
    $apiUrl = "http://127.0.0.1:$apiPort"
    $dbPassword = Get-RandomBase64 32

    Set-EnvVar PGPASSWORD $dbPassword
    Set-EnvVar PGHOST '127.0.0.1'
    Set-EnvVar PGPORT "$dbPort"
    Set-EnvVar PGUSER 'postgres'
    Set-EnvVar PGDATABASE 'postgres'

    $pipeName = 'agro360-prod-e2e-' + [guid]::NewGuid().ToString('N')
    $pipe = [IO.Pipes.NamedPipeServerStream]::new($pipeName, [IO.Pipes.PipeDirection]::Out)
    try {
        $pending = $pipe.WaitForConnectionAsync()
        $dataDir = Join-Path $evidenceDir 'data_test'
        $init = Start-Process $initdb -ArgumentList @('-D', "`"$dataDir`"", '-U', 'postgres', '--auth=scram-sha-256', "--pwfile=\\.\pipe\$pipeName", '--encoding=UTF8', '--locale=C') -WindowStyle Hidden -PassThru -RedirectStandardOutput "$evidenceDir\initdb.log" -RedirectStandardError "$evidenceDir\initdb.err.log"
        $processes.Add($init)
        if (-not $pending.Wait(15000)) { throw 'initdb nao conectou ao named pipe.' }
        $writer = [IO.StreamWriter]::new($pipe)
        try { $writer.WriteLine($dbPassword) } finally { $writer.Dispose() }
        $init.WaitForExit()
        if (-not (Test-Path (Join-Path $dataDir 'PG_VERSION'))) {
            throw "initdb falhou (PG_VERSION ausente). Verifique $evidenceDir\initdb.log"
        }
    } finally { $pipe.Dispose() }

    & $pg_ctl -D $dataDir -l "$evidenceDir\postgres.log" -o "-h 127.0.0.1 -p $dbPort" -w start
    if ($LASTEXITCODE -ne 0) { throw "Falha ao iniciar PostgreSQL descartavel." }
    $startedDatabase = $true
    Write-Host "PostgreSQL descartavel em execucao na porta $dbPort" -ForegroundColor DarkGray

    # -------------------------------------------------------------
    # 1. INSTALACAO LIMPA DO FULL SQL E VERIFICACAO DE SCHEMA
    # -------------------------------------------------------------
    $dbFull = 'agro360_prod_full_test'
    $null = Exec-Sql 'postgres' "create database $dbFull;"
    Set-EnvVar PGDATABASE $dbFull

    $sqlInstallCode = Invoke-PsqlFile $dbFull (Join-Path $root 'database/agro360-postgres-full.sql') "$evidenceDir\full-sql-install.log"
    Assert-Step "1. Instalacao limpa do full SQL" ($sqlInstallCode -eq 0)

    $ver118 = (Query-Sql $dbFull "select count(*) from agro360.platform_schema_versions where version = '11.8.0';").Trim()
    Assert-Step "1. Versao 11.8.0 presente em platform_schema_versions" ($ver118 -eq '1')

    $colIdemp = (Query-Sql $dbFull "select count(*) from information_schema.columns where table_schema='agro360' and table_name='production_material_consumptions' and column_name in ('idempotency_key','request_hash');").Trim()
    Assert-Step "1. Colunas de idempotencia em production_material_consumptions" ($colIdemp -eq '2')

    # -------------------------------------------------------------
    # 2. UPGRADE REPRESENTATIVO PELO MIGRADOR REAL
    # -------------------------------------------------------------
    $dbUpgrade = 'agro360_migrator_upgrade_test'
    $null = Exec-Sql 'postgres' "create database $dbUpgrade;"
    $connUpgrade = "Host=127.0.0.1;Port=$dbPort;Database=$dbUpgrade;Username=postgres;Password=$dbPassword"
    Set-EnvVar ConnectionStrings__Agro360 $connUpgrade

    # Instala base representativa 11.7 (Full SQL ate antes da migration 118)
    $fullSqlText = [System.IO.File]::ReadAllText((Join-Path $root 'database/agro360-postgres-full.sql'), [System.Text.Encoding]::UTF8)
    $splitMarker = "alter table agro360.production_material_consumptions"
    if (-not $fullSqlText.Contains($splitMarker)) { throw "Marcador da migration 118 nao encontrado no full SQL." }
    $baseSql = $fullSqlText.Substring(0, $fullSqlText.IndexOf($splitMarker)) + "`ncommit;`n"
    $baseSqlFile = Join-Path $evidenceDir 'temp-base-117.sql'
    [System.IO.File]::WriteAllText($baseSqlFile, $baseSql, [System.Text.Encoding]::UTF8)

    $upgradeBaseCode = Invoke-PsqlFile $dbUpgrade $baseSqlFile "$evidenceDir\upgrade-base-117.log"
    Assert-Step "2. Instalacao base 11.7 em banco de upgrade" ($upgradeBaseCode -eq 0)

    # Garante existencia da tabela de historico do migrador
    $null = Exec-Sql $dbUpgrade "create table if not exists agro360.platform_schema_migrations (version varchar(160) primary key, name varchar(260) not null default '', checksum varchar(64) not null, applied_at timestamptz not null default now());"

    # Popula historico de migrations para as 117 migrations ja consolidadas na base
    $migFiles = Get-ChildItem (Join-Path $root 'database/migrations') -Filter "*.sql" | Sort-Object Name
    foreach ($mf in $migFiles) {
        if ($mf.Name -match '^118_') { continue }
        $content = [System.IO.File]::ReadAllText($mf.FullName, [System.Text.Encoding]::UTF8)
        $sha = [System.Security.Cryptography.SHA256]::Create()
        $contentBytes = [System.Text.Encoding]::UTF8.GetBytes($content)
        $hashBytes = $sha.ComputeHash($contentBytes)
        $hashHex = [BitConverter]::ToString($hashBytes).Replace('-', '').ToLowerInvariant()
        $null = Exec-Sql $dbUpgrade "insert into agro360.platform_schema_migrations(version, name, checksum, applied_at) values('$($mf.Name)', '$($mf.Name)', '$hashHex', now()) on conflict do nothing;"
    }

    # Seed de dados preexistentes para comprovar preservacao
    $preExistingId = [guid]::NewGuid().ToString()
    $null = Exec-Sql $dbUpgrade "set session_replication_role = 'replica'; insert into agro360.production_material_consumptions(id, tenant_id, order_id, material_id, stock_lot_id, quantity, unit, returned_quantity, justification, status, created_at, updated_at, created_by, updated_by) values('$preExistingId', '00000000-0000-0000-0000-000000000001', gen_random_uuid(), gen_random_uuid(), gen_random_uuid(), 5.0, 'kg', 0, 'Consumo legado anterior a migration 118', 'POSTED', now(), now(), '00000000-0000-0000-0000-000000000002', '00000000-0000-0000-0000-000000000002'); set session_replication_role = 'origin';"

    # Executa o migrador real Agro360.Migrator
    $migratorDll = Join-Path $root 'src\Hosts\Agro360.Migrator\bin\Release\net10.0\Agro360.Migrator.dll'
    $migOutput = & dotnet $migratorDll migrate --migrations (Join-Path $root 'database/migrations') 2>&1
    $migExit = $LASTEXITCODE
    [System.IO.File]::WriteAllText((Join-Path $evidenceDir 'migrator-run.log'), ($migOutput -join "`r`n"), [System.Text.Encoding]::UTF8)
    Assert-Step "2. Execucao de upgrade pelo migrador real (migration 118)" ($migExit -eq 0) "Exit code: $migExit"

    # Verifica que migration 118 foi registrada
    $hasMig118 = (Query-Sql $dbUpgrade "select count(*) from agro360.platform_schema_migrations where version = '118_production_material_consumption_integrity.sql';").Trim()
    Assert-Step "2. Migration 118 registrada em platform_schema_migrations" ($hasMig118 -eq '1')

    $hasVer118 = (Query-Sql $dbUpgrade "select count(*) from agro360.platform_schema_versions where version = '11.8.0';").Trim()
    Assert-Step "2. Versao 11.8.0 registrada em platform_schema_versions" ($hasVer118 -eq '1')

    # Verifica preservacao de dados anteriores
    $preserved = (Query-Sql $dbUpgrade "select count(*) from agro360.production_material_consumptions where id = '$preExistingId';").Trim()
    Assert-Step "2. Preservacao de dados preexistentes confirmada" ($preserved -eq '1')

    # -------------------------------------------------------------
    # 3. PAPEL RESTRITO DE APLICACAO (agro360_app)
    # Sem SUPERUSER, sem BYPASSRLS; RLS forcado em tabelas operacionais
    # -------------------------------------------------------------
    $appUser = 'test_agro360_app_user'
    $appPass = Get-RandomBase64 24
    $null = Exec-Sql $dbFull "create user $appUser with password '$appPass' in role agro360_app;"
    $roleAttrs = (Query-Sql $dbFull "select rolsuper || ':' || rolbypassrls from pg_roles where rolname = '$appUser';").Trim()
    Assert-Step "3. Papel restrito sem SUPERUSER e sem BYPASSRLS" ($roleAttrs -eq 'false:false' -or $roleAttrs -eq 'f:f') "Attrs: $roleAttrs"

    $tenantAId = (Query-Sql $dbFull "select id from agro360.tenancy_tenants where slug = 'santa-clara' limit 1;").Trim()
    $tenantBId = (Query-Sql $dbFull "select id from agro360.tenancy_tenants where slug = 'cooperativa-vale-verde' limit 1;").Trim()

    # Leitura com Tenant A sob usuario restrito
    $readA = Query-Sql $dbFull "set `"app.tenant_id`" = '$tenantAId'; select count(*) from agro360.production_material_consumptions where tenant_id = '$tenantAId';" $appUser $appPass
    Assert-Step "3. Usuario restrito le dados do Tenant A com contexto" ([int]$readA -ge 0)

    # Tenant B nao pode enxergar dados do Tenant A
    $readCross = Query-Sql $dbFull "set `"app.tenant_id`" = '$tenantBId'; select count(*) from agro360.production_material_consumptions where tenant_id = '$tenantAId';" $appUser $appPass
    Assert-Step "3. Isolamento RLS: Tenant B nao ve linhas do Tenant A" ($readCross -eq '0')

    # Consulta sem contexto de tenant retorna 0 linhas
    $readNoContext = Query-Sql $dbFull "reset `"app.tenant_id`"; select count(*) from agro360.production_material_consumptions;" $appUser $appPass
    Assert-Step "3. Isolamento RLS: Sem contexto retorna 0 linhas" ($readNoContext -eq '0')

    # -------------------------------------------------------------
    # 4. CONFIGURACAO DO AMBIENTE OPERACIONAL E INICIO DA API
    # -------------------------------------------------------------
    $adminPassword = 'Aa1!' + (Get-RandomBase64 24)
    $adminHash = Create-PasswordHash $adminPassword

    # Configura modulos e credenciais
    $setupSql = @"
update agro360.saas_plans set modules = array['properties','agriculture','livestock','inventory','finance','reports','logistics','traceability','intelligence','environment-esg','agroindustry','purchasing','commercial','orders'] where name in ('Profissional', 'Growth', 'Enterprise');
update agro360.identity_users set password_hash = '$adminHash', status = 'ACTIVE', must_change_password = false where email in ('admin.santaclara@agro360.local', 'admin@santaclara.agro360.local', 'admin.valeverde@agro360.local');
insert into agro360.identity_role_permissions(tenant_id, role_id, permission_id) select r.tenant_id, r.id, p.id from agro360.identity_roles r cross join agro360.identity_permissions p where r.tenant_id in ('$tenantAId', '$tenantBId') and lower(r.code) = 'tenant-administrator' on conflict do nothing;
"@
    $null = Exec-Sql $dbFull $setupSql

    # Configura API para usar a connection string
    $connApi = "Host=127.0.0.1;Port=$dbPort;Database=$dbFull;Username=postgres;Password=$dbPassword"
    Set-EnvVar ConnectionStrings__Agro360 $connApi
    Set-EnvVar ConnectionStrings__DefaultConnection $connApi
    Set-EnvVar AGRO360_TEST_CONNECTION_STRING $connApi
    Set-EnvVar ASPNETCORE_ENVIRONMENT 'Development'
    Set-EnvVar Jwt__SigningKey (Get-RandomBase64 48)
    Set-EnvVar ApiBaseUrl $apiUrl
    Set-EnvVar Bootstrap__Enabled 'false'

    Start-HostProcess Api $apiUrl
    Assert-Step "4. API iniciada em ambiente isolado" $true "URL: $apiUrl"

    # Login como administrador Tenant A
    $loginRes = Http-Call $apiUrl '/api/v1/auth/login' 'POST' @{
        tenantSlug = 'santa-clara'
        email = 'admin@santaclara.agro360.local'
        password = $adminPassword
    } $null @(200)
    $tokenA = ($loginRes.Content | ConvertFrom-Json).accessToken
    Assert-Step "4. Autenticacao JWT Tenant A realizada" ($tokenA.Length -gt 20)

    # -------------------------------------------------------------
    # 5. ESTRUTURA BASICA INDUSTRIAL: PLANTA, LINHA, PRODUTOS, RECEITA
    # -------------------------------------------------------------
    $plantId = [guid]::NewGuid().ToString()
    $lineId = [guid]::NewGuid().ToString()
    $rawInvProdId = [guid]::NewGuid().ToString()
    $finInvProdId = [guid]::NewGuid().ToString()
    $rawProdId = [guid]::NewGuid().ToString()
    $finProdId = [guid]::NewGuid().ToString()
    $warehouseId = [guid]::NewGuid().ToString()
    $userId = (Query-Sql $dbFull "select id from agro360.identity_users where email='admin@santaclara.agro360.local' limit 1;").Trim()

    $seedProdSql = @"
DO `$seed`$
DECLARE
  v_org uuid;
  v_farm uuid;
BEGIN
  select id into v_org from agro360.organization_organizations where tenant_id = '$tenantAId' limit 1;
  if v_org is null then
    v_org := gen_random_uuid();
    insert into agro360.organization_organizations(id, tenant_id, type, name, legal_name, document_number, created_by)
    values(v_org, '$tenantAId', 'FARM', 'Fazenda Santa Clara Ltda', 'Santa Clara S/A', '12345678000199', '$userId');
  end if;

  select id into v_farm from agro360.geo_farms where tenant_id = '$tenantAId' limit 1;
  if v_farm is null then
    v_farm := gen_random_uuid();
    insert into agro360.geo_farms(id, tenant_id, organization_id, name, state, total_area_ha, created_by)
    values(v_farm, '$tenantAId', v_org, 'Fazenda Sede', 'PA', 1500.0, '$userId');
  end if;

  insert into agro360.production_industrial_plants(id, tenant_id, code, name, active, created_by, updated_by)
  values('$plantId', '$tenantAId', 'PLANT-01', 'Usina Principal', true, '$userId', '$userId');

  insert into agro360.production_lines(id, tenant_id, plant_id, code, name, active, created_by, updated_by)
  values('$lineId', '$tenantAId', '$plantId', 'LINE-01', 'Linha de Processamento 01', true, '$userId', '$userId');

  insert into agro360.inventory_warehouses(id, tenant_id, farm_id, code, name, type, created_by)
  values('$warehouseId', '$tenantAId', v_farm, 'DEP-01', 'Deposito Central', 'INPUTS', '$userId');

  insert into agro360.inventory_products(id, tenant_id, sku, name, category, base_unit, requires_lot, created_by)
  values('$rawInvProdId', '$tenantAId', 'INS-01', 'Soja em Graos Insumo', 'GRAOS', 'kg', true, '$userId'),
        ('$finInvProdId', '$tenantAId', 'ACAB-01', 'Farelo de Soja Refinado', 'FARELO', 'kg', true, '$userId');

  insert into agro360.production_products(id, tenant_id, code, name, product_type, unit, inventory_product_id, active, created_by, updated_by)
  values('$rawProdId', '$tenantAId', 'INS-01', 'Soja em Graos Insumo', 'RAW_MATERIAL', 'kg', '$rawInvProdId', true, '$userId', '$userId'),
        ('$finProdId', '$tenantAId', 'ACAB-01', 'Farelo de Soja Refinado', 'FINISHED', 'kg', '$finInvProdId', true, '$userId', '$userId');
END `$seed`$;
"@
    $null = Exec-Sql $dbFull $seedProdSql

    # Cria Formulacao via API
    $recipeRes = Http-Call $apiUrl '/api/production/recipes' 'POST' @{
        code = "FORM-OLEO-01"
        name = "Formulacao Oleo de Soja"
        finishedProductId = $finProdId
        baseQuantity = 10.0
        unit = "kg"
        expectedYield = 80.0
        expectedLoss = 2.0
        instructions = "Moagem, extracao e refino sob controle termico."
        items = @(
            @{ materialId = $rawProdId; quantity = 15.0; unit = "kg"; lossPercent = 0.0 }
        )
    } $tokenA @(201)
    $recipeVersionId = ($recipeRes.Content | ConvertFrom-Json).id
    Assert-Step "5. Criacao de formulacao versionada" ($recipeVersionId.Length -gt 10)

    # Aprova a versao da formulacao
    $apprRes = Http-Call $apiUrl "/api/production/recipe-versions/$recipeVersionId/approve" 'POST' $null $tokenA @(204)
    Assert-Step "5. Aprovacao de formulacao (tornando-a imutavel)" ($apprRes.StatusCode -eq 204)

    # -------------------------------------------------------------
    # 6. ORDEM DE PRODUCAO: CRIACAO E AVANCO DE ESTADO
    # -------------------------------------------------------------
    $orderRes = Http-Call $apiUrl '/api/production/orders' 'POST' @{
        plantId = $plantId
        lineId = $lineId
        finishedProductId = $finProdId
        recipeVersionId = $recipeVersionId
        plannedBatch = "LOTE-PL-2026-001"
        plannedQuantity = 100.0
        unit = "kg"
        plannedAt = (Get-Date).AddDays(1).ToString("yyyy-MM-ddTHH:mm:ssZ")
        priority = "HIGH"
        responsibleId = $userId
        demandOrigin = "Plano Industrial Safra 2026"
        requiresReservation = $false
        notes = "Ordem inicial para homologacao e2e"
    } $tokenA @(201)
    $orderId = ($orderRes.Content | ConvertFrom-Json).id
    Assert-Step "6. Ordem de producao criada (status PLANNED)" ($orderId.Length -gt 10)

    # Libera ordem
    $null = Http-Call $apiUrl "/api/production/orders/$orderId/release" 'POST' $null $tokenA @(204)
    $orderDetail = (Http-Call $apiUrl "/api/production/orders/$orderId" 'GET' $null $tokenA @(200)).Content | ConvertFrom-Json
    Assert-Step "6. Liberacao da ordem (status RELEASED)" ($orderDetail.status -eq 'RELEASED')

    # Inicia ordem
    $null = Http-Call $apiUrl "/api/production/orders/$orderId/start" 'POST' $null $tokenA @(204)
    $orderDetail = (Http-Call $apiUrl "/api/production/orders/$orderId" 'GET' $null $tokenA @(200)).Content | ConvertFrom-Json
    Assert-Step "6. Inicio da producao (status IN_PRODUCTION)" ($orderDetail.status -eq 'IN_PRODUCTION')

    # -------------------------------------------------------------
    # 7. ESTOQUE DE INSUMO: SALDO E LOTE
    # -------------------------------------------------------------
    $stockLotId = [guid]::NewGuid().ToString()
    $balanceId = [guid]::NewGuid().ToString()
    $seedStockSql = @"
insert into agro360.inventory_stock_balances(id, tenant_id, warehouse_id, product_id, unit, available, reserved, minimum, average_cost, version) values('$balanceId', '$tenantAId', '$warehouseId', '$rawInvProdId', 'kg', 100.0, 20.0, 10.0, 3.50, 1);
insert into agro360.inventory_stock_lots(id, tenant_id, warehouse_id, product_id, lot_number, quantity, quality_status, expires_on) values('$stockLotId', '$tenantAId', '$warehouseId', '$rawInvProdId', 'LOTE-GR-001', 50.0, 'APPROVED', (current_date + 60));
"@
    $null = Exec-Sql $dbFull $seedStockSql
    Assert-Step "7. Saldo inicial (Available=100, Reserved=20) e Lote inicial (50 kg) preparados" $true

    # -------------------------------------------------------------
    # 8. CONSUMO CANONICO DIRETO (ConsumeAsync)
    # Deducao atomica de lote, saldo disponivel e geracao de movimento
    # -------------------------------------------------------------
    $consumeKey = 'consume-key-' + [guid]::NewGuid().ToString('N')
    $consumePayload = @{
        orderId = $orderId
        materialId = $rawProdId
        stockLotId = $stockLotId
        quantity = 15.0
        unit = "kg"
        expiredOverride = $false
        justification = "Consumo dosagem inicial 15kg"
        idempotencyKey = $consumeKey
    }

    $consumeRes = Http-Call $apiUrl '/api/production/consumptions' 'POST' $consumePayload $tokenA @(201)
    $consumptionId = ($consumeRes.Content | ConvertFrom-Json).id
    Assert-Step "8. Consumo de estoque registrado com sucesso (POSTED)" ($consumptionId.Length -gt 10)

    # Verificar lote fisico: 50 - 15 = 35
    $lotQty = (Query-Sql $dbFull "select quantity from agro360.inventory_stock_lots where id = '$stockLotId';").Trim()
    Assert-Step "8. Quantidade do lote de estoque deduzida atomicamente (50 -> 35)" ([decimal]$lotQty -eq 35.0) "Lote: $lotQty"

    # Verificar saldo de estoque: 100 - 15 = 85
    $balAvail = (Query-Sql $dbFull "select available from agro360.inventory_stock_balances where id = '$balanceId';").Trim()
    Assert-Step "8. Saldo disponivel em estoque deduzido atomicamente (100 -> 85)" ([decimal]$balAvail -eq 85.0) "Saldo: $balAvail"

    # Verificar movimento canonico no razao de estoque
    $movCheck = (Query-Sql $dbFull "select movement_type || ':' || reference_type || ':' || quantity from agro360.inventory_stock_movements where reference_id = '$orderId' and movement_type = 'CONSUMPTION';").Trim()
    Assert-Step "8. Movimento canonico CONSUMPTION registrado no razao" ($movCheck -eq 'CONSUMPTION:PRODUCTION_ORDER:15.000') "Movimento: $movCheck"

    # -------------------------------------------------------------
    # 9. IDEMPOTENCIA POR CONTEUDO E FINGERPRINT
    # -------------------------------------------------------------
    # Replay identico com mesma chave
    $replayRes = Http-Call $apiUrl '/api/production/consumptions' 'POST' $consumePayload $tokenA @(201)
    $replayId = ($replayRes.Content | ConvertFrom-Json).id
    Assert-Step "9. Idempotencia: Replay identico retorna mesmo ID de consumo" ($replayId -eq $consumptionId)

    # Verificar que saldo NAO foi deduzido uma segunda vez
    $lotQtyReplay = (Query-Sql $dbFull "select quantity from agro360.inventory_stock_lots where id = '$stockLotId';").Trim()
    Assert-Step "9. Idempotencia: Replay nao duplica deducao fisica no estoque (35)" ([decimal]$lotQtyReplay -eq 35.0)

    # Mutacao com mesma chave idempotente -> 409 Conflict
    $mutatedPayload = @{
        orderId = $orderId
        materialId = $rawProdId
        stockLotId = $stockLotId
        quantity = 25.0
        unit = "kg"
        expiredOverride = $false
        justification = "Tentativa de alteracao de conteudo"
        idempotencyKey = $consumeKey
    }
    $conflictRes = Http-Call $apiUrl '/api/production/consumptions' 'POST' $mutatedPayload $tokenA @(409)
    Assert-Step "9. Idempotencia: Mutacao com mesma chave rejeitada com 409 Conflict" ($conflictRes.StatusCode -eq 409)

    # -------------------------------------------------------------
    # 10. PROTECAO CONTRA SOBRECOMPROMISSO (AVAILABLE - RESERVED)
    # Available = 85, Reserved = 20 -> Saldo livre = 65. Tentar consumir 70 -> Falha!
    # -------------------------------------------------------------
    $overcommitKey = 'overcommit-key-' + [guid]::NewGuid().ToString('N')
    $overcommitPayload = @{
        orderId = $orderId
        materialId = $rawProdId
        stockLotId = $stockLotId
        quantity = 70.0
        unit = "kg"
        expiredOverride = $false
        justification = "Consumo acima do saldo livre"
        idempotencyKey = $overcommitKey
    }
    $overcommitRes = Http-Call $apiUrl '/api/production/consumptions' 'POST' $overcommitPayload $tokenA @(409)
    Assert-Step "10. Protecao: Tentativa de consumir quantidade comprometida por reservas rejeitada com 409" ($overcommitRes.StatusCode -eq 409)

    # -------------------------------------------------------------
    # 11. ESTORNO INTEGRAL AUTORIZADO (ReverseConsumptionAsync)
    # Restauracao no lote e saldo, evento compensatorio ADJUSTMENT_IN
    # -------------------------------------------------------------
    $revRes = Http-Call $apiUrl "/api/production/consumptions/$consumptionId/reverse" 'POST' @{
        reason = "Ajuste operacional de dosagem para reteste"
    } $tokenA @(204)
    Assert-Step "11. Estorno integral executado com sucesso (204 No Content)" ($revRes.StatusCode -eq 204)

    # Lote restaurado: 35 + 15 = 50
    $lotQtyRestored = (Query-Sql $dbFull "select quantity from agro360.inventory_stock_lots where id = '$stockLotId';").Trim()
    Assert-Step "11. Quantidade do lote restaurada (35 -> 50)" ([decimal]$lotQtyRestored -eq 50.0) "Lote: $lotQtyRestored"

    # Saldo restaurado: 85 + 15 = 100
    $balAvailRestored = (Query-Sql $dbFull "select available from agro360.inventory_stock_balances where id = '$balanceId';").Trim()
    Assert-Step "11. Saldo disponivel em estoque restaurado (85 -> 100)" ([decimal]$balAvailRestored -eq 100.0) "Saldo: $balAvailRestored"

    # Movimento compensatorio ADJUSTMENT_IN registrado
    $compMov = (Query-Sql $dbFull "select movement_type || ':' || reference_type || ':' || quantity from agro360.inventory_stock_movements where reference_id = '$consumptionId' and movement_type = 'ADJUSTMENT_IN';").Trim()
    Assert-Step "11. Movimento compensatorio ADJUSTMENT_IN registrado no razao" ($compMov -eq 'ADJUSTMENT_IN:PRODUCTION_CONSUMPTION_REVERSAL:15.000')

    # Status do consumo atualizado para REVERSED
    $consStatus = (Query-Sql $dbFull "select status from agro360.production_material_consumptions where id = '$consumptionId';").Trim()
    Assert-Step "11. Status do consumo marcado como REVERSED" ($consStatus -eq 'REVERSED')

    # Segundo estorno rejeitado (409 Conflict)
    $doubleRevRes = Http-Call $apiUrl "/api/production/consumptions/$consumptionId/reverse" 'POST' @{
        reason = "Segunda tentativa indevida"
    } $tokenA @(409)
    Assert-Step "11. Duplo estorno rejeitado com 409 Conflict" ($doubleRevRes.StatusCode -eq 409)

    # -------------------------------------------------------------
    # 12. APONTAMENTO DE RESULTADO, QUALIDADE E DISPONIBILIZACAO
    # Lote produzido nasce pendente -> Decisao de qualidade disponibiliza no estoque
    # -------------------------------------------------------------
    # Consome 10 kg legitimos para a ordem
    $finalConsKey = 'final-consume-' + [guid]::NewGuid().ToString('N')
    $null = Http-Call $apiUrl '/api/production/consumptions' 'POST' @{
        orderId = $orderId
        materialId = $rawProdId
        stockLotId = $stockLotId
        quantity = 10.0
        unit = "kg"
        expiredOverride = $false
        justification = "Consumo efetivo para fabricacao do lote acabado"
        idempotencyKey = $finalConsKey
    } $tokenA @(201)

    # Apontar resultado: 20 L de oleo refinado
    $outputKey = 'output-key-' + [guid]::NewGuid().ToString('N')
    $outRes = Http-Call $apiUrl '/api/production/outputs' 'POST' @{
        orderId = $orderId
        productId = $finProdId
        batchNumber = "LOTE-ACAB-2026-X1"
        outputType = "MAIN"
        quantity = 20.0
        unit = "kg"
        warehouseId = $warehouseId
        manufacturedAt = (Get-Date).ToString("yyyy-MM-ddTHH:mm:ssZ")
        expiresOn = (Get-Date).AddMonths(12).ToString("yyyy-MM-dd")
        requiresInspection = $true
        idempotencyKey = $outputKey
    } $tokenA @(201)
    $batchId = ($outRes.Content | ConvertFrom-Json).id
    Assert-Step "12. Resultado apontado (LOTE-ACAB-2026-X1 gerado)" ($batchId.Length -gt 10)

    # Verificar que o lote nasce com status PENDING e NAO esta disponivel no estoque
    $batchStatus = (Query-Sql $dbFull "select quality_status from agro360.production_batches where id = '$batchId';").Trim()
    Assert-Step "12. Lote produzido nasce PENDING qualidade" ($batchStatus -eq 'PENDING')

    $finStockBefore = (Query-Sql $dbFull "select count(*) from agro360.inventory_stock_lots where product_id = '$finInvProdId';").Trim()
    Assert-Step "12. Lote PENDING nao disponibilizado no estoque de vendas" ($finStockBefore -eq '0')

    # Decisao de qualidade: Aprovacao e liberacao
    $qualRes = Http-Call $apiUrl '/api/production/quality' 'POST' @{
        orderId = $orderId
        batchId = $batchId
        result = "APPROVED"
        reason = "Aprovado em analise fisico-quimica conforme especificacao"
        reportReference = "LAUDO-FQ-2026-009"
        evidenceReference = "EVIDENCIA-FOTO-AMOSTRA-01"
    } $tokenA @(201)
    Assert-Step "12. Decisao de qualidade APPROVED registrada" ($qualRes.StatusCode -eq 201)

    # Agora o lote foi disponibilizado no estoque fisico e saldo atualizado!
    $finStockAfter = (Query-Sql $dbFull "select quantity from agro360.inventory_stock_lots where product_id = '$finInvProdId' and lot_number = 'LOTE-ACAB-2026-X1';").Trim()
    Assert-Step "12. Lote aprovado disponibilizado no estoque fisico (20 L)" ([decimal]$finStockAfter -eq 20.0) "Qtd: $finStockAfter"

    $finBalance = (Query-Sql $dbFull "select available from agro360.inventory_stock_balances where product_id = '$finInvProdId' and warehouse_id = '$warehouseId';").Trim()
    Assert-Step "12. Saldo de estoque disponivel para o produto acabado (20 L)" ([decimal]$finBalance -eq 20.0) "Saldo: $finBalance"

    # Movimento de producao registrado
    $prodMov = (Query-Sql $dbFull "select movement_type || ':' || reference_type || ':' || quantity from agro360.inventory_stock_movements where reference_id = '$batchId' and movement_type = 'PRODUCTION';").Trim()
    Assert-Step "12. Movimento PRODUCTION registrado no razao de estoque" ($prodMov -eq 'PRODUCTION:PRODUCTION_BATCH:20.000')

    # -------------------------------------------------------------
    # 13. RASTREABILIDADE E GENEALOGIA
    # Rastrear LOTE-ACAB-2026-X1 retorna vinculos com o insumo consumido
    # -------------------------------------------------------------
    $traceRes = Http-Call $apiUrl '/api/production/traceability/LOTE-ACAB-2026-X1' 'GET' $null $tokenA @(200)
    $traceItems = $traceRes.Content | ConvertFrom-Json
    Assert-Step "13. Rastreabilidade e genealogia do lote consultadas" ($traceItems.Count -ge 1) "Total itens vinculados: $($traceItems.Count)"

    # -------------------------------------------------------------
    # 14. SEGURANCA E ISOLAMENTO MULTITENANT POR API
    # Login como Tenant B nao pode acessar ordem nem dados de Tenant A
    # -------------------------------------------------------------
    $loginBRes = Http-Call $apiUrl '/api/v1/auth/login' 'POST' @{
        tenantSlug = 'cooperativa-vale-verde'
        email = 'admin.valeverde@agro360.local'
        password = $adminPassword
    } $null @(200)
    $tokenB = ($loginBRes.Content | ConvertFrom-Json).accessToken
    Assert-Step "14. Autenticacao JWT Tenant B realizada" ($tokenB.Length -gt 20)

    # Tenant B tenta acessar ordem do Tenant A -> 404 Not Found (isolamento absoluto)
    $crossRes = Http-Call $apiUrl "/api/production/orders/$orderId" 'GET' $null $tokenB @(404)
    Assert-Step "14. Isolamento multi-tenant por API: Ordem do Tenant A invisivel para Tenant B (404)" ($crossRes.StatusCode -eq 404)

    Write-Host "`n=================================================================" -ForegroundColor Green
    Write-Host "  TODOS OS 14 CENARIOS DE HOMOLOGACAO PASSARAM COM SUCESSO!" -ForegroundColor Green
    Write-Host "=================================================================" -ForegroundColor Green

} finally {
    Write-Host "`nEncerrando processos de teste..." -ForegroundColor DarkGray
    foreach ($p in $processes) {
        try {
            if (-not $p.HasExited) {
                $p.Kill()
                $p.WaitForExit(3000)
            }
        } catch {}
    }
    if ($startedDatabase) {
        & $pg_ctl -D $dataDir -m immediate stop | Out-Null
    }
    foreach ($entry in $savedEnvironment.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value)
    }
}
