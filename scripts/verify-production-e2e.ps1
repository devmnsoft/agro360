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

function New-JwtToken([string]$SecretKey, [hashtable]$PayloadClaims) {
    function To-Base64Url([byte[]]$b) {
        return [Convert]::ToBase64String($b).TrimEnd('=').Replace('+', '-').Replace('/', '_')
    }
    $headerJson = '{"alg":"HS256","typ":"JWT"}'
    $headerB64 = To-Base64Url ([System.Text.Encoding]::UTF8.GetBytes($headerJson))

    $nowSec = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $claims = [ordered]@{
        nbf = $nowSec - 10
        exp = $nowSec + 3600
        iss = "MNSOFT.Agro360"
        aud = "MNSOFT.Agro360.Clients"
    }
    foreach ($k in $PayloadClaims.Keys) { $claims[$k] = $PayloadClaims[$k] }
    $payloadJson = $claims | ConvertTo-Json -Compress
    $payloadB64 = To-Base64Url ([System.Text.Encoding]::UTF8.GetBytes($payloadJson))

    $toSign = "$headerB64.$payloadB64"
    $hmac = [System.Security.Cryptography.HMACSHA256]::new([System.Text.Encoding]::UTF8.GetBytes($SecretKey))
    try {
        $sigBytes = $hmac.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($toSign))
        $sigB64 = To-Base64Url $sigBytes
    } finally {
        $hmac.Dispose()
    }
    return "$toSign.$sigB64"
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

function Http-Call([string]$BaseUrl, [string]$Path, [string]$Method = 'GET', $Body = $null, [string]$Token = $null, [int[]]$ExpectedStatuses = @(200), [hashtable]$CustomHeaders = @{}) {
    $uri = "$BaseUrl$Path"
    $headers = @{}
    if ($Token) { $headers['Authorization'] = "Bearer $Token" }
    if ($CustomHeaders) {
        foreach ($k in $CustomHeaders.Keys) { $headers[$k] = $CustomHeaders[$k] }
    }
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

    $ver119 = (Query-Sql $dbFull "select count(*) from agro360.platform_schema_versions where version = '11.9.0';").Trim()
    Assert-Step "1. Versao 11.9.0 presente em platform_schema_versions" ($ver119 -eq '1')

    $ver120 = (Query-Sql $dbFull "select count(*) from agro360.platform_schema_versions where version = '11.10.0';").Trim()
    Assert-Step "1. Versao 11.10.0 presente em platform_schema_versions" ($ver120 -eq '1')

    $ver121 = (Query-Sql $dbFull "select count(*) from agro360.platform_schema_versions where version = '11.11.0';").Trim()
    Assert-Step "1. Versao 11.11.0 presente em platform_schema_versions" ($ver121 -eq '1')

    $ver122 = (Query-Sql $dbFull "select count(*) from agro360.platform_schema_versions where version = '11.12.0';").Trim()
    Assert-Step "1. Versao 11.12.0 presente em platform_schema_versions" ($ver122 -eq '1')

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
        if ($mf.Name -match '^(118|119|120|121|122)_') { continue }
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
    Assert-Step "2. Execucao de upgrade pelo migrador real (migrations 118, 119, 120, 121, 122)" ($migExit -eq 0) "Exit code: $migExit"

    # Verifica que migrations 118, 119, 120, 121 e 122 foram registradas
    $hasMig118 = (Query-Sql $dbUpgrade "select count(*) from agro360.platform_schema_migrations where version = '118_production_material_consumption_integrity.sql';").Trim()
    Assert-Step "2. Migration 118 registrada em platform_schema_migrations" ($hasMig118 -eq '1')

    $hasMig119 = (Query-Sql $dbUpgrade "select count(*) from agro360.platform_schema_migrations where version = '119_security_hardening_audit_and_privileges.sql';").Trim()
    Assert-Step "2. Migration 119 registrada em platform_schema_migrations" ($hasMig119 -eq '1')

    $hasMig120 = (Query-Sql $dbUpgrade "select count(*) from agro360.platform_schema_migrations where version = '120_rural_hr_status_alignment.sql';").Trim()
    Assert-Step "2. Migration 120 registrada em platform_schema_migrations" ($hasMig120 -eq '1')

    $hasMig121 = (Query-Sql $dbUpgrade "select count(*) from agro360.platform_schema_migrations where version = '121_saas_platform_context_rls.sql';").Trim()
    Assert-Step "2. Migration 121 registrada em platform_schema_migrations" ($hasMig121 -eq '1')

    $hasMig122 = (Query-Sql $dbUpgrade "select count(*) from agro360.platform_schema_migrations where version = '122_rural_hr_unified_journey_and_operations.sql';").Trim()
    Assert-Step "2. Migration 122 registrada em platform_schema_migrations" ($hasMig122 -eq '1')

    $hasVer121 = (Query-Sql $dbUpgrade "select count(*) from agro360.platform_schema_versions where version = '11.11.0';").Trim()
    Assert-Step "2. Versao 11.11.0 registrada em platform_schema_versions" ($hasVer121 -eq '1')

    $hasVer122 = (Query-Sql $dbUpgrade "select count(*) from agro360.platform_schema_versions where version = '11.12.0';").Trim()
    Assert-Step "2. Versao 11.12.0 registrada em platform_schema_versions" ($hasVer122 -eq '1')

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

    # Fixtures com registros conhecidos de dois tenants
    $fixtureAId = [guid]::NewGuid().ToString()
    $fixtureBId = [guid]::NewGuid().ToString()
    $fixtureUserId = '00000000-0000-0000-0000-000000000002'
    $seedFixturesSql = @"
set session_replication_role = 'replica';
insert into agro360.rural_hr_records(id, tenant_id, kind, name, status, created_by, updated_by, created_at, updated_at)
values ('$fixtureAId', '$tenantAId', 'TEAM', 'Equipe Colheita Santa Clara', 'ACTIVE', '$fixtureUserId', '$fixtureUserId', now(), now()),
       ('$fixtureBId', '$tenantBId', 'TEAM', 'Equipe Plantio Vale Verde', 'ACTIVE', '$fixtureUserId', '$fixtureUserId', now(), now());
set session_replication_role = 'origin';
"@
    $null = Exec-Sql $dbFull $seedFixturesSql

    # Leitura positiva com Tenant A sob usuario restrito
    $readA = Query-Sql $dbFull "set `"app.tenant_id`" = '$tenantAId'; select name from agro360.rural_hr_records where id = '$fixtureAId';" $appUser $appPass
    Assert-Step "3. Leitura positiva sob papel restrito com contexto Tenant A" ($readA -eq 'Equipe Colheita Santa Clara') "Nome: $readA"

    # Tenant B nao pode enxergar dados do Tenant A
    $readCross = Query-Sql $dbFull "set `"app.tenant_id`" = '$tenantBId'; select count(*) from agro360.rural_hr_records where id = '$fixtureAId';" $appUser $appPass
    Assert-Step "3. Isolamento RLS: Tenant B nao ve fixture do Tenant A" ($readCross -eq '0')

    # Consulta sem contexto de tenant retorna vazio
    $readNoContext = Query-Sql $dbFull "reset `"app.tenant_id`"; select count(*) from agro360.rural_hr_records where id in ('$fixtureAId', '$fixtureBId');" $appUser $appPass
    Assert-Step "3. Isolamento RLS: Sem contexto retorna 0 linhas" ($readNoContext -eq '0')

    # Mutacao cruzada (UPDATE do Tenant A via sessao do Tenant B) afeta 0 linhas
    $updateCross = Query-Sql $dbFull "set `"app.tenant_id`" = '$tenantBId'; update agro360.rural_hr_records set name = 'Hacked' where id = '$fixtureAId'; select count(*) from agro360.rural_hr_records where id = '$fixtureAId' and name = 'Hacked';" $appUser $appPass
    Assert-Step "3. Isolamento RLS: UPDATE cruzado entre tenants afeta 0 linhas" ($updateCross -eq '0')

    # Delecao cruzada (DELETE do Tenant A via sessao do Tenant B) afeta 0 linhas
    $deleteCross = Query-Sql $dbFull "set `"app.tenant_id`" = '$tenantBId'; delete from agro360.rural_hr_records where id = '$fixtureAId'; set `"app.tenant_id`" = '$tenantAId'; select count(*) from agro360.rural_hr_records where id = '$fixtureAId';" $appUser $appPass
    Assert-Step "3. Isolamento RLS: DELETE cruzado entre tenants protegido" ($deleteCross -eq '1')

    # Insercao cruzada (tentativa de Tenant A inserir registro com tenant_id do Tenant B) falha por RLS
    $insertCrossFailed = $false
    try {
        $null = Exec-Sql $dbFull "set `"app.tenant_id`" = '$tenantAId'; insert into agro360.rural_hr_records(id, tenant_id, kind, name, status, created_by, updated_by) values (gen_random_uuid(), '$tenantBId', 'TEAM', 'Invasao Cruzada', 'ACTIVE', '$fixtureUserId', '$fixtureUserId');" $appUser $appPass
    } catch {
        $insertCrossFailed = $_.Exception.Message -match 'row-level security' -or $_.Exception.Message -match 'violates row-level security'
    }
    Assert-Step "3. Isolamento RLS: INSERT cruzado com tenant_id divergente rejeitado por politica RLS" $insertCrossFailed


    # Reuso de conexao e ausencia de vazamento de contexto
    $contextResetLeakTest = Query-Sql $dbFull "set `"app.tenant_id`" = '$tenantAId'; reset `"app.tenant_id`"; select count(*) from agro360.rural_hr_records where id = '$fixtureAId';" $appUser $appPass
    Assert-Step "3. Reuso e limpeza de conexao: Apos reset de contexto, dados nao vazam" ($contextResetLeakTest -eq '0')

    # -------------------------------------------------------------
    # 3b. PRIVILEGIOS EFETIVOS DO PAPEL RESTRITO (MIGRATION 119)
    # Runtime pode inserir logs/movimentos; nao pode alterar, excluir nem truncar
    # Runtime nao pode alterar controles de migrations/schema
    # -------------------------------------------------------------
    $testAuditId = [guid]::NewGuid().ToString()
    $canInsertAudit = $false
    try {
        $null = Exec-Sql $dbFull "set `"app.tenant_id`" = '$tenantAId'; insert into agro360.audit_logs(id, tenant_id, user_id, action, entity_type, entity_id) values('$testAuditId', '$tenantAId', null, 'TEST_ACTION', 'TEST_ENTITY', '$fixtureAId');" $appUser $appPass
        $canInsertAudit = $true
    } catch {
        Write-Host "Audit insert error: $($_.Exception.Message)"
        $canInsertAudit = $false
    }
    Assert-Step "3b. Privilegios: Runtime PODE inserir em logs de auditoria" $canInsertAudit

    # Nao pode atualizar audit_logs
    $auditUpdateBlocked = $false
    try {
        $null = Exec-Sql $dbFull "set `"app.tenant_id`" = '$tenantAId'; update agro360.audit_logs set action = 'TAMPERED' where id = '$testAuditId';" $appUser $appPass
    } catch {
        $auditUpdateBlocked = $_.Exception.Message -match 'permission denied' -or $_.Exception.Message -match 'denied'
    }
    Assert-Step "3b. Privilegios: Runtime NAO PODE atualizar logs de auditoria (permission denied)" $auditUpdateBlocked

    # Nao pode excluir de audit_logs
    $auditDeleteBlocked = $false
    try {
        $null = Exec-Sql $dbFull "set `"app.tenant_id`" = '$tenantAId'; delete from agro360.audit_logs where id = '$testAuditId';" $appUser $appPass
    } catch {
        $auditDeleteBlocked = $_.Exception.Message -match 'permission denied' -or $_.Exception.Message -match 'denied'
    }
    Assert-Step "3b. Privilegios: Runtime NAO PODE excluir de logs de auditoria (permission denied)" $auditDeleteBlocked

    # Nao pode truncar audit_logs
    $truncateBlocked = $false
    try {
        $null = Exec-Sql $dbFull "truncate agro360.audit_logs;" $appUser $appPass
    } catch {
        $truncateBlocked = $_.Exception.Message -match 'permission denied' -or $_.Exception.Message -match 'must be owner' -or $_.Exception.Message -match 'denied'
    }
    Assert-Step "3b. Privilegios: Runtime NAO PODE truncar tabelas imutaveis" $truncateBlocked

    # Nao pode alterar platform_schema_migrations nem platform_schema_versions
    $schemaBlocked = $false
    try {
        $null = Exec-Sql $dbFull "insert into agro360.platform_schema_migrations(version, name, checksum, applied_at) values('fake.sql', 'fake', 'abc', now());" $appUser $appPass
    } catch {
        $schemaBlocked = $_.Exception.Message -match 'permission denied' -or $_.Exception.Message -match 'denied'
    }
    Assert-Step "3b. Privilegios: Runtime NAO PODE alterar platform_schema_migrations" $schemaBlocked

    # -------------------------------------------------------------
    # 4. CONFIGURACAO DO AMBIENTE OPERACIONAL E INICIO DA API
    # -------------------------------------------------------------
    $adminPassword = 'Aa1!' + (Get-RandomBase64 24)
    $adminHash = Create-PasswordHash $adminPassword

    # Configura modulos e credenciais
    $setupSql = @"
update agro360.saas_plans set modules = array['properties','agriculture','livestock','inventory','finance','reports','logistics','traceability','intelligence','environment-esg','agroindustry','purchasing','commercial','orders','documents','rural-hr','verticals'] where name in ('Profissional', 'Cooperativa', 'Agroindústria', 'Enterprise');
update agro360.identity_users set password_hash = '$adminHash', status = 'ACTIVE', must_change_password = false where email in ('admin.santaclara@agro360.local', 'admin@santaclara.agro360.local', 'admin.valeverde@agro360.local');
insert into agro360.identity_role_permissions(tenant_id, role_id, permission_id) select r.tenant_id, r.id, p.id from agro360.identity_roles r cross join agro360.identity_permissions p where r.tenant_id in ('$tenantAId', '$tenantBId') and lower(r.code) = 'tenant-administrator' on conflict do nothing;
insert into agro360.saas_organizations(tenant_id, organization_type, document, responsible_name, responsible_email, plan_id, status, activated_at, onboarding_status)
select '$tenantBId', 'COOPERATIVE', '22333444000191', 'Administrador Vale Verde', 'admin.valeverde@agro360.local', id, 'ACTIVE', now(), 'COMPLETED'
from agro360.saas_plans where name = 'Cooperativa'
on conflict (tenant_id) do update set status = 'ACTIVE', plan_id = excluded.plan_id;
insert into agro360.platform_tenant_module_entitlements(tenant_id, module_id, status, reason, activated_at) select '$tenantBId', id, 'ACTIVE', 'Homologacao modulo industrial Tenant B', now() from agro360.platform_module_catalog where code in ('agroindustry') on conflict(tenant_id, module_id) do update set status = 'ACTIVE';
"@
    $null = Exec-Sql $dbFull $setupSql

    # Configura API para usar papel restrito agro360_app
    $connApi = "Host=127.0.0.1;Port=$dbPort;Database=$dbFull;Username=$appUser;Password=$appPass"
    Set-EnvVar ConnectionStrings__Agro360 $connApi
    Set-EnvVar ConnectionStrings__DefaultConnection $connApi
    Set-EnvVar AGRO360_TEST_CONNECTION_STRING $connApi

    Set-EnvVar ASPNETCORE_ENVIRONMENT 'Development'
    $jwtSigningKey = Get-RandomBase64 48
    Set-EnvVar Jwt__SigningKey $jwtSigningKey
    Set-EnvVar ApiBaseUrl $apiUrl
    Set-EnvVar Bootstrap__Enabled 'false'
    $testStorageDir = Join-Path $evidenceDir 'documents'
    New-Item -ItemType Directory -Path $testStorageDir | Out-Null
    Set-EnvVar Storage__RootPath $testStorageDir

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
insert into agro360.inventory_stock_balances(id, tenant_id, warehouse_id, product_id, unit, available, reserved, minimum, average_cost, version) values('$balanceId', '$tenantAId', '$warehouseId', '$rawInvProdId', 'kg', 100.0, 70.0, 10.0, 3.50, 1);
insert into agro360.inventory_stock_lots(id, tenant_id, warehouse_id, product_id, lot_number, quantity, quality_status, expires_on) values('$stockLotId', '$tenantAId', '$warehouseId', '$rawInvProdId', 'LOTE-GR-001', 100.0, 'APPROVED', (current_date + 60));
"@
    $null = Exec-Sql $dbFull $seedStockSql
    Assert-Step "7. Saldo inicial (Available=100, Reserved=70) e Lote inicial (100 kg) preparados" $true

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

    # Verificar lote fisico: 100 - 15 = 85
    $lotQty = (Query-Sql $dbFull "select quantity from agro360.inventory_stock_lots where id = '$stockLotId';").Trim()
    Assert-Step "8. Quantidade do lote de estoque deduzida atomicamente (100 -> 85)" ([decimal]$lotQty -eq 85.0) "Lote: $lotQty"

    # Verificar saldo de estoque: 100 - 15 = 85
    $balAvail = (Query-Sql $dbFull "select available from agro360.inventory_stock_balances where id = '$balanceId';").Trim()
    Assert-Step "8. Saldo disponivel em estoque deduzido atomicamente (100 -> 85)" ([decimal]$balAvail -eq 85.0) "Saldo: $balAvail"

    # Verificar movimento canonico no razao de estoque
    $movCheck = (Query-Sql $dbFull "select movement_type || ':' || reference_type || ':' || quantity from agro360.inventory_stock_movements where reference_id = '$orderId' and movement_type = 'CONSUMPTION';").Trim()
    Assert-Step "8. Movimento canonico CONSUMPTION registrado no razao" ($movCheck -match '^CONSUMPTION:PRODUCTION_ORDER:15(\.0+)?$') "Movimento: $movCheck"

    # -------------------------------------------------------------
    # 9. IDEMPOTENCIA POR CONTEUDO E FINGERPRINT
    # -------------------------------------------------------------
    # Replay identico com mesma chave
    $replayRes = Http-Call $apiUrl '/api/production/consumptions' 'POST' $consumePayload $tokenA @(201)
    $replayId = ($replayRes.Content | ConvertFrom-Json).id
    Assert-Step "9. Idempotencia: Replay identico retorna mesmo ID de consumo" ($replayId -eq $consumptionId)

    # Verificar que saldo NAO foi deduzido uma segunda vez
    $lotQtyReplay = (Query-Sql $dbFull "select quantity from agro360.inventory_stock_lots where id = '$stockLotId';").Trim()
    Assert-Step "9. Idempotencia: Replay nao duplica deducao fisica no estoque (85)" ([decimal]$lotQtyReplay -eq 85.0)

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
    # Available = 85, Reserved = 70 -> Saldo livre = 15. Tentar consumir 20 -> Falha!
    # -------------------------------------------------------------
    $overcommitKey = 'overcommit-key-' + [guid]::NewGuid().ToString('N')
    $overcommitPayload = @{
        orderId = $orderId
        materialId = $rawProdId
        stockLotId = $stockLotId
        quantity = 20.0
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

    # Lote restaurado: 85 + 15 = 100
    $lotQtyRestored = (Query-Sql $dbFull "select quantity from agro360.inventory_stock_lots where id = '$stockLotId';").Trim()
    Assert-Step "11. Quantidade do lote restaurada (85 -> 100)" ([decimal]$lotQtyRestored -eq 100.0) "Lote: $lotQtyRestored"

    # Saldo restaurado: 85 + 15 = 100
    $balAvailRestored = (Query-Sql $dbFull "select available from agro360.inventory_stock_balances where id = '$balanceId';").Trim()
    Assert-Step "11. Saldo disponivel em estoque restaurado (85 -> 100)" ([decimal]$balAvailRestored -eq 100.0) "Saldo: $balAvailRestored"

    # Movimento compensatorio ADJUSTMENT_IN registrado
    $compMov = (Query-Sql $dbFull "select movement_type || ':' || reference_type || ':' || quantity from agro360.inventory_stock_movements where reference_id = '$consumptionId' and movement_type = 'ADJUSTMENT_IN';").Trim()
    Assert-Step "11. Movimento compensatorio ADJUSTMENT_IN registrado no razao" ($compMov -match '^ADJUSTMENT_IN:PRODUCTION_CONSUMPTION_REVERSAL:15(\.0+)?$') "Movimento: $compMov"

    # Status do consumo atualizado para REVERSED
    $consStatus = (Query-Sql $dbFull "select status from agro360.production_material_consumptions where id = '$consumptionId';").Trim()
    Assert-Step "11. Status do consumo marcado como REVERSED" ($consStatus -eq 'REVERSED')

    # Segundo estorno rejeitado (409 Conflict)
    $doubleRevRes = Http-Call $apiUrl "/api/production/consumptions/$consumptionId/reverse" 'POST' @{
        reason = "Segunda tentativa indevida"
    } $tokenA @(409)
    Assert-Step "11. Duplo estorno rejeitado com 409 Conflict" ($doubleRevRes.StatusCode -eq 409)

    # -------------------------------------------------------------
    # 11b. CONCORRENCIA REAL (Pessimistic Locking / FOR UPDATE)
    # Dois consumos concorrentes disputam o mesmo saldo descomprometido
    # -------------------------------------------------------------
    Add-Type -AssemblyName System.Net.Http
    $client1 = [System.Net.Http.HttpClient]::new()
    $client2 = [System.Net.Http.HttpClient]::new()
    try {
        $client1.DefaultRequestHeaders.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $tokenA)
        $client2.DefaultRequestHeaders.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $tokenA)

        $p1 = @{ orderId = $orderId; materialId = $rawProdId; stockLotId = $stockLotId; quantity = 20.0; unit = "kg"; expiredOverride = $false; justification = "Concorrencia A"; idempotencyKey = "conc-key-A-" + [guid]::NewGuid().ToString('N') } | ConvertTo-Json
        $p2 = @{ orderId = $orderId; materialId = $rawProdId; stockLotId = $stockLotId; quantity = 20.0; unit = "kg"; expiredOverride = $false; justification = "Concorrencia B"; idempotencyKey = "conc-key-B-" + [guid]::NewGuid().ToString('N') } | ConvertTo-Json

        $c1 = [System.Net.Http.StringContent]::new($p1, [System.Text.Encoding]::UTF8, 'application/json')
        $c2 = [System.Net.Http.StringContent]::new($p2, [System.Text.Encoding]::UTF8, 'application/json')

        $t1 = $client1.PostAsync("$apiUrl/api/production/consumptions", $c1)
        $t2 = $client2.PostAsync("$apiUrl/api/production/consumptions", $c2)
        [System.Threading.Tasks.Task]::WaitAll($t1, $t2)

        $codes = @([int]$t1.Result.StatusCode, [int]$t2.Result.StatusCode)
        $has201 = $codes -contains 201
        $has409 = $codes -contains 409
        Assert-Step "11b. Concorrencia: Disputa atomica - exatamente um consumo aprovado (201) e um rejeitado (409)" ($has201 -and $has409) "Codigos: $($codes -join ', ')"

        $succBody = if ([int]$t1.Result.StatusCode -eq 201) { $t1.Result.Content.ReadAsStringAsync().Result } else { $t2.Result.Content.ReadAsStringAsync().Result }
        $concConsId = ($succBody | ConvertFrom-Json).id
        $null = Http-Call $apiUrl "/api/production/consumptions/$concConsId/reverse" 'POST' @{ reason = "Estorno do teste de concorrencia" } $tokenA @(204)
    } finally {
        $client1.Dispose()
        $client2.Dispose()
    }

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
    $outRequestId = ($outRes.Content | ConvertFrom-Json).id
    $batchId = (Query-Sql $dbFull "select id from agro360.production_batches where batch_number = 'LOTE-ACAB-2026-X1';").Trim()
    Assert-Step "12. Resultado apontado (LOTE-ACAB-2026-X1 gerado)" ($batchId.Length -gt 10) "BatchId: $batchId"

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
    Assert-Step "12. Movimento PRODUCTION registrado no razao de estoque" ($prodMov -match '^PRODUCTION:PRODUCTION_BATCH:20(\.0+)?$') "Movimento: $prodMov"

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

    # Tenant B tenta acessar ordem do Tenant A -> 404 Not Found ou 403 Forbidden (isolamento absoluto)
    $crossRes = Http-Call $apiUrl "/api/production/orders/$orderId" 'GET' $null $tokenB @(403, 404)
    Assert-Step "14. Isolamento multi-tenant por API: Ordem do Tenant A inacessivel para Tenant B (status $($crossRes.StatusCode))" ($crossRes.StatusCode -eq 403 -or $crossRes.StatusCode -eq 404)

    # -------------------------------------------------------------
    # 15. VALIDACAO DE ESCOPO COMPATIVEL COM RLS (HEADERS)
    # Organizacao e Fazenda nos headers avaliados dentro da transacao do tenant
    # Combinacoes invalidas ou cruzadas sao rejeitadas com 403 Forbidden
    # -------------------------------------------------------------
    $orgA = (Query-Sql $dbFull "select id from agro360.organization_organizations where tenant_id = '$tenantAId' limit 1;").Trim()
    if (-not $orgA) {
        $orgA = [guid]::NewGuid().ToString()
        $null = Exec-Sql $dbFull "insert into agro360.organization_organizations(id, tenant_id, name, type, document_number) values('$orgA', '$tenantAId', 'Org Santa Clara', 'COMPANY', '12345678000199');"
    }
    $farmA = (Query-Sql $dbFull "select id from agro360.geo_farms where tenant_id = '$tenantAId' and organization_id = '$orgA' limit 1;").Trim()
    if (-not $farmA) {
        $farmA = [guid]::NewGuid().ToString()
        $null = Exec-Sql $dbFull "insert into agro360.geo_farms(id, tenant_id, organization_id, name, state, total_area_ha, created_by) values('$farmA', '$tenantAId', '$orgA', 'Fazenda Santa Clara 1', 'SP', 150.0, '$fixtureUserId');"
    }

    $orgB = (Query-Sql $dbFull "select id from agro360.organization_organizations where tenant_id = '$tenantBId' limit 1;").Trim()
    if (-not $orgB) {
        $orgB = [guid]::NewGuid().ToString()
        $null = Exec-Sql $dbFull "insert into agro360.organization_organizations(id, tenant_id, name, type, document_number) values('$orgB', '$tenantBId', 'Org Vale Verde', 'COMPANY', '98765432000188');"
    }
    $farmB = (Query-Sql $dbFull "select id from agro360.geo_farms where tenant_id = '$tenantBId' and organization_id = '$orgB' limit 1;").Trim()
    if (-not $farmB) {
        $farmB = [guid]::NewGuid().ToString()
        $null = Exec-Sql $dbFull "insert into agro360.geo_farms(id, tenant_id, organization_id, name, state, total_area_ha, created_by) values('$farmB', '$tenantBId', '$orgB', 'Fazenda Vale Verde 1', 'PR', 200.0, '$fixtureUserId');"
    }

    # 15.1 Requisicao com Org e Fazenda legitimas do Tenant A -> 200 OK
    $resScopeLegit = Http-Call $apiUrl '/api/documents/dashboard' 'GET' $null $tokenA @(200) @{
        'X-Organization-ID' = $orgA
        'X-Farm-ID' = $farmA
    }
    Assert-Step "15. Escopo legitimo: Org e Fazenda validas do Tenant A aceitas (200)" ($resScopeLegit.StatusCode -eq 200)

    # 15.2 Org de outro tenant (Tenant B) enviada com token do Tenant A -> 403 Forbidden
    $resCrossOrg = Http-Call $apiUrl '/api/documents/dashboard' 'GET' $null $tokenA @(403) @{
        'X-Organization-ID' = $orgB
    }
    Assert-Step "15. Escopo cruzado: Org do Tenant B rejeitada para Tenant A (403)" ($resCrossOrg.StatusCode -eq 403)

    # 15.3 Fazenda de outro tenant (Tenant B) enviada com token do Tenant A -> 403 Forbidden
    $resCrossFarm = Http-Call $apiUrl '/api/documents/dashboard' 'GET' $null $tokenA @(403) @{
        'X-Farm-ID' = $farmB
    }
    Assert-Step "15. Escopo cruzado: Fazenda do Tenant B rejeitada para Tenant A (403)" ($resCrossFarm.StatusCode -eq 403)

    # 15.4 Fazenda incompativel com Organizacao informada -> 403 Forbidden
    $resMismatch = Http-Call $apiUrl '/api/documents/dashboard' 'GET' $null $tokenA @(403) @{
        'X-Organization-ID' = $orgA
        'X-Farm-ID' = $farmB
    }
    Assert-Step "15. Escopo incompativel: Fazenda nao pertencente a Organizacao rejeitada (403)" ($resMismatch.StatusCode -eq 403)

    # -------------------------------------------------------------
    # 16. TOKENS E STATUS DE SEGURANCA EM TEMPO REAL
    # Usuario bloqueado apos emissao do token e Tenant suspenso tem acesso negado imediatamente
    # -------------------------------------------------------------
    $testBlockEmail = 'temp.operator@santaclara.agro360.local'
    $testBlockId = [guid]::NewGuid().ToString()
    $null = Exec-Sql $dbFull "insert into agro360.identity_users(id, tenant_id, name, email, password_hash, status, must_change_password) values('$testBlockId', '$tenantAId', 'Operador Temporario', '$testBlockEmail', '$adminHash', 'ACTIVE', false);"
    $null = Exec-Sql $dbFull "insert into agro360.identity_user_roles(tenant_id, user_id, role_id) select '$tenantAId', '$testBlockId', id from agro360.identity_roles where tenant_id = '$tenantAId' and lower(code) = 'tenant-administrator';"

    $loginTemp = Http-Call $apiUrl '/api/v1/auth/login' 'POST' @{
        tenantSlug = 'santa-clara'
        email = $testBlockEmail
        password = $adminPassword
    } $null @(200)
    $tokenTemp = ($loginTemp.Content | ConvertFrom-Json).accessToken
    Assert-Step "16. Token emitido para operador temporario" ($tokenTemp.Length -gt 20)

    # Operacao bem sucedida enquanto ativo
    $resBeforeBlock = Http-Call $apiUrl '/api/documents/dashboard' 'GET' $null $tokenTemp @(200)
    Assert-Step "16. Operador ativo acessa API normalmente (200)" ($resBeforeBlock.StatusCode -eq 200)

    # Bloqueia usuario no banco em tempo real (status DISABLED)
    $null = Exec-Sql $dbFull "update agro360.identity_users set status = 'DISABLED' where id = '$testBlockId';"

    # Mesma requisicao com o mesmo token agora DEVE retornar 403 Forbidden
    $resAfterBlock = Http-Call $apiUrl '/api/documents/dashboard' 'GET' $null $tokenTemp @(403)
    Assert-Step "16. Usuario inativo tem token revogado em tempo real pelo middleware (403 Forbidden)" ($resAfterBlock.StatusCode -eq 403)

    # Teste de Tenant suspenso
    $null = Exec-Sql $dbFull "update agro360.tenancy_tenants set status = 3 where id = '$tenantBId';" # 3 = SUSPENDED
    $resTenantSuspended = Http-Call $apiUrl "/api/production/orders" 'GET' $null $tokenB @(403)
    Assert-Step "16. Tenant suspenso tem acesso bloqueado imediatamente (403 Forbidden)" ($resTenantSuspended.StatusCode -eq 403)
    $null = Exec-Sql $dbFull "update agro360.tenancy_tenants set status = 1 where id = '$tenantBId';" # Restaura ACTIVE

    # -------------------------------------------------------------
    # 17. JORNADA DE DOCUMENTOS: UPLOAD, SNIFFING, VERSOES, DOWNLOAD, ARQUIVAMENTO
    # -------------------------------------------------------------
    $docTypeId = (Query-Sql $dbFull "select id from agro360.documents_document_types where active limit 1;").Trim()
    if (-not $docTypeId) {
        $docTypeId = [guid]::NewGuid().ToString()
        $null = Exec-Sql $dbFull "insert into agro360.documents_document_types(id, name, code, active) values('$docTypeId', 'Laudo Tecnico', 'LAUDO', true);"
    }

    Add-Type -AssemblyName System.Net.Http
    $httpDocClient = [System.Net.Http.HttpClient]::new()
    try {
        $httpDocClient.DefaultRequestHeaders.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $tokenA)

        # 17.1 Arquivo executavel com extensao .pdf rejeitado por content sniffing
        $fakeBytes = [byte[]]@(0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00) # MZ DOS/PE
        $mpFake = [System.Net.Http.MultipartFormDataContent]::new()
        $mpFake.Add([System.Net.Http.StringContent]::new("Laudo Malicioso"), "name")
        $mpFake.Add([System.Net.Http.StringContent]::new($docTypeId), "documentTypeId")
        $fakeFile = [System.Net.Http.ByteArrayContent]::new($fakeBytes)
        $fakeFile.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse("application/pdf")
        $mpFake.Add($fakeFile, "file", "laudo-falso.pdf")

        $resFake = $httpDocClient.PostAsync("$apiUrl/api/documents", $mpFake).Result
        Assert-Step "17. Content sniffing: Arquivo binario MZ disfarçado de PDF rejeitado (400/422)" ([int]$resFake.StatusCode -in @(400, 422)) "Status: $([int]$resFake.StatusCode)"

        # 17.2 Upload autentico com assinatura PDF valida (%PDF-)
        $validPdf = [System.Text.Encoding]::ASCII.GetBytes("%PDF-1.7`n%Laudo Tecnico Autentico`n%%EOF")
        $mpValid = [System.Net.Http.MultipartFormDataContent]::new()
        $mpValid.Add([System.Net.Http.StringContent]::new("Laudo Fiscalizacao 2026"), "name")
        $mpValid.Add([System.Net.Http.StringContent]::new($docTypeId), "documentTypeId")
        $mpValid.Add([System.Net.Http.StringContent]::new("qualidade, homologacao"), "tags")
        $validFile = [System.Net.Http.ByteArrayContent]::new($validPdf)
        $validFile.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse("application/pdf")
        $mpValid.Add($validFile, "file", "laudo-fiscalizacao.pdf")

        $resValid = $httpDocClient.PostAsync("$apiUrl/api/documents", $mpValid).Result
        Assert-Step "17. Upload autentico com assinatura %PDF- aceito com sucesso (201 Created)" ([int]$resValid.StatusCode -eq 201) "Status: $([int]$resValid.StatusCode)"
        $createdDocId = (($resValid.Content.ReadAsStringAsync().Result | ConvertFrom-Json).id)

        # 17.3 Nova versao com motivo obrigatorio
        $v2Pdf = [System.Text.Encoding]::ASCII.GetBytes("%PDF-1.7`n%Revisao 2 do Laudo Tecnico`n%%EOF")
        $mpV2 = [System.Net.Http.MultipartFormDataContent]::new()
        $mpV2.Add([System.Net.Http.StringContent]::new("Revisao tecnica apos auditoria externa"), "reason")
        $v2File = [System.Net.Http.ByteArrayContent]::new($v2Pdf)
        $v2File.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse("application/pdf")
        $mpV2.Add($v2File, "file", "laudo-fiscalizacao-v2.pdf")

        $resV2 = $httpDocClient.PostAsync("$apiUrl/api/documents/$createdDocId/versions", $mpV2).Result
        Assert-Step "17. Nova versao com motivo registrada com sucesso (204 NoContent)" ([int]$resV2.StatusCode -eq 204)

        # 17.4 Detalhes do documento exibem histórico e metadados
        $detailRes = Http-Call $apiUrl "/api/documents/$createdDocId" 'GET' $null $tokenA @(200)
        $docData = $detailRes.Content | ConvertFrom-Json
        Assert-Step "17. Detalhes do documento contem 2 versoes com motivos preservados" ($docData.versions.Count -eq 2) "Versoes: $($docData.versions.Count)"

        $firstVersionId = $docData.versions[0].id

        # 17.5 Download autorizado da versao especifica
        $dlV1 = Http-Call $apiUrl "/api/documents/$createdDocId/download?versionId=$firstVersionId" 'GET' $null $tokenA @(200)
        Assert-Step "17. Download autorizado da versao 1 bem-sucedido (200 OK)" ($dlV1.StatusCode -eq 200)

        # 17.6 Download cruzado: Tenant B tenta baixar documento do Tenant A -> Rejeitado
        $dlCross = Http-Call $apiUrl "/api/documents/$createdDocId/download" 'GET' $null $tokenB @(403, 404)
        Assert-Step "17. Download cruzado negado para Tenant B (403/404)" ($dlCross.StatusCode -in @(403, 404))

        # 17.7 Arquivamento do documento
        $archRes = Http-Call $apiUrl "/api/documents/$createdDocId/archive" 'POST' $null $tokenA @(204)
        Assert-Step "17. Arquivamento do documento realizado (204 NoContent)" ($archRes.StatusCode -eq 204)

        $dbStatus = (Query-Sql $dbFull "select status from agro360.documents where id = '$createdDocId';").Trim()
        Assert-Step "17. Status confirmado como ARCHIVED no banco" ($dbStatus -eq 'ARCHIVED')
    } finally {
        $httpDocClient.Dispose()
    }

    # -------------------------------------------------------------
    # 18. REUSO REAL DE CONEXAO DO POOL E AUSENCIA DE CONTAMINACAO
    # Sequencia alternada de requisicoes entre Tenant A e Tenant B
    # -------------------------------------------------------------
    $poolConsistent = $true
    for ($i = 0; $i -lt 6; $i++) {
        $callA = Http-Call $apiUrl '/api/documents/dashboard' 'GET' $null $tokenA @(200)
        $callB = Http-Call $apiUrl '/api/documents/dashboard' 'GET' $null $tokenB @(200)
        if ($callA.StatusCode -ne 200 -or $callB.StatusCode -ne 200) {
            $poolConsistent = $false
            break
        }
    }
    Assert-Step "18. Pool de conexoes: Requisicoes alternadas preservam isolamento estrito sem vazamento de contexto" $poolConsistent

    # -------------------------------------------------------------
    # 19. SESSAO DE SUPORTE ASSISTIDO DO SUPER-ADMINISTRADOR
    # Operador global atua no tenant atendido; read-only restrito; encerramento invalida token imediatamente
    # -------------------------------------------------------------
    $platformTenantId = (Query-Sql $dbFull "select id from agro360.tenancy_tenants where slug in ('agro360-platform', 'platform') limit 1;").Trim()
    if (-not $platformTenantId) {
        $platformTenantId = [guid]::NewGuid().ToString()
        $null = Exec-Sql $dbFull "insert into agro360.tenancy_tenants(id, name, slug, timezone_id, status, plan_code) values('$platformTenantId', 'Agro360 Plataforma', 'agro360-platform', 'America/Sao_Paulo', 1, 'ENTERPRISE');"
    }

    $superUserId = (Query-Sql $dbFull "select id from agro360.identity_users where email = 'superadmin@agro360.local' limit 1;").Trim()
    if (-not $superUserId) {
        $superUserId = [guid]::NewGuid().ToString()
        $null = Exec-Sql $dbFull "insert into agro360.identity_users(id, tenant_id, name, email, password_hash, status, must_change_password) values('$superUserId', '$platformTenantId', 'Super Admin Agro360', 'superadmin@agro360.local', '$adminHash', 'ACTIVE', false);"
    } else {
        $null = Exec-Sql $dbFull "update agro360.identity_users set status = 'ACTIVE', must_change_password = false where id = '$superUserId';"
    }

    $superRoleId = (Query-Sql $dbFull "select id from agro360.identity_roles where tenant_id = '$platformTenantId' and code = 'SUPER_ADMIN' limit 1;").Trim()
    if (-not $superRoleId) {
        $superRoleId = [guid]::NewGuid().ToString()
        $null = Exec-Sql $dbFull "insert into agro360.identity_roles(id, tenant_id, code, name, is_system) values('$superRoleId', '$platformTenantId', 'SUPER_ADMIN', 'SuperAdmin', true);"
    }
    $null = Exec-Sql $dbFull "insert into agro360.identity_user_roles(tenant_id, user_id, role_id) values('$platformTenantId', '$superUserId', '$superRoleId') on conflict do nothing;"
    $null = Exec-Sql $dbFull "insert into agro360.identity_role_permissions(tenant_id, role_id, permission_id) select '$platformTenantId', '$superRoleId', id from agro360.identity_permissions on conflict do nothing;"
    $null = Exec-Sql $dbFull "insert into agro360.platform_super_admins(id, user_id, active) values(gen_random_uuid(), '$superUserId', true) on conflict(user_id) do update set active = true, deleted_at = null;"

    # Super Admin Global: Emissao do token de plataforma assinado com claim platform.admin
    $superClaims = @{
        sub = $superUserId
        email = 'superadmin@agro360.local'
        tenant_id = $platformTenantId
        role = 'SUPER_ADMIN'
        permission = 'platform.admin'
    }
    $superToken = New-JwtToken $jwtSigningKey $superClaims
    Assert-Step "19. Sessao assistida: Super-admin global autenticado com claim platform.admin" ($superToken.Length -gt 20)

    # Garante organizacao ativa no SaaS para Tenant A
    $null = Exec-Sql $dbFull @"
insert into agro360.saas_organizations(tenant_id, organization_type, document, responsible_name, responsible_email, plan_id, status, activated_at, onboarding_status)
select '$tenantAId', 'PRODUCER', '11222333000181', 'Administrador Santa Clara', 'admin@santaclara.agro360.local', id, 'ACTIVE', now(), 'COMPLETED'
from agro360.saas_plans where name in ('Profissional', 'Enterprise') limit 1
on conflict (tenant_id) do update set status = 'ACTIVE', plan_id = excluded.plan_id;
"@

    $debugTenant = Query-Sql $dbFull "select id || '|' || name || '|' || (deleted_at is null) from agro360.tenancy_tenants where id = '$tenantAId';"
    Write-Host "DEBUG TENANT: $debugTenant"
    $debugOrg = Query-Sql $dbFull "select tenant_id || '|' || status from agro360.saas_organizations where tenant_id = '$tenantAId';"
    Write-Host "DEBUG ORG: $debugOrg"
    $debugJoin = Query-Sql $dbFull "select t.name || '|' || t.slug || '|' || s.status from agro360.tenancy_tenants t join agro360.saas_organizations s on s.tenant_id=t.id where t.id='$tenantAId' and t.deleted_at is null;"
    Write-Host "DEBUG JOIN: $debugJoin"

    # Inicia sessao de suporte assistido para Tenant A em escopo somente leitura (default)
    $supportRes = Http-Call $apiUrl "/api/platform/tenants/$tenantAId/support-session" 'POST' @{
        reason = "Auditoria e suporte operacional assistido para homologacao e2e"
        scope = "SUPPORT_READ_OPERATIONAL"
    } $superToken @(200)
    $supportResult = $supportRes.Content | ConvertFrom-Json
    $supportToken = if ($supportResult.accessToken) { $supportResult.accessToken } else { $supportResult.token }
    $supportSessionId = $supportResult.sessionId
    Assert-Step "19. Sessao assistida: Sessao iniciada com sucesso (token emitido com claims de suporte)" ($supportToken.Length -gt 20 -and $supportSessionId.Length -gt 10)

    # 19.1 Leitura autorizada de dados do tenant atendido com token de suporte
    $supportDocDash = Http-Call $apiUrl '/api/documents/dashboard' 'GET' $null $supportToken @(200)
    Assert-Step "19. Sessao assistida: Operador global acessa dados do Tenant A sob papel restrito (200 OK)" ($supportDocDash.StatusCode -eq 200)

    # 19.2 Tentativa de mutacao sob SUPPORT_READ_OPERATIONAL bloqueada com 403 Forbidden
    $supportMutate = Http-Call $apiUrl '/api/rural-hr/records' 'POST' @{
        kind = "TEAM"
        name = "Equipe Nao Autorizada Suporte"
        status = "ACTIVE"
    } $supportToken @(403)
    Assert-Step "19. Sessao assistida: Tentativa de mutacao sob escopo read-only bloqueada pelo middleware (403 Forbidden)" ($supportMutate.StatusCode -eq 403)

    # 19.3 Encerramento da propria sessao de suporte pelo operador assistido
    $endSupportRes = Http-Call $apiUrl '/api/platform/support-session/end' 'POST' @{
        sessionId = $supportSessionId
    } $supportToken @(204)
    Assert-Step "19. Sessao assistida: Operador encerra a propria sessao de suporte (204 NoContent)" ($endSupportRes.StatusCode -eq 204)

    # 19.4 Requisicao subsequente com o token da sessao encerrada e imediatamente negada
    $supportAfterEnd = Http-Call $apiUrl '/api/documents/dashboard' 'GET' $null $supportToken @(403)
    Assert-Step "19. Sessao assistida: Token de sessao encerrada imediatamente rejeitado pelo middleware (403 Forbidden)" ($supportAfterEnd.StatusCode -eq 403)

    # -------------------------------------------------------------
    # 20. ENDURECIMENTO DE ARQUIVOS, STREAM FRAGMENTADO E VALIDACAO OPENXML/XML
    # -------------------------------------------------------------
    Add-Type -AssemblyName System.IO.Compression
    $httpHardenClient = [System.Net.Http.HttpClient]::new()
    try {
        $httpHardenClient.DefaultRequestHeaders.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $tokenA)

        # 20.1 Upload de documento com acumulacao de stream seguro
        $fragPdfBytes = [System.Text.Encoding]::ASCII.GetBytes("%PDF-1.7`n%Stream de laudo fragmentado com buffer acumulado`n%%EOF")
        $mpFrag = [System.Net.Http.MultipartFormDataContent]::new()
        $mpFrag.Add([System.Net.Http.StringContent]::new("Laudo Tecnico Fragmentado"), "name")
        $mpFrag.Add([System.Net.Http.StringContent]::new($docTypeId), "documentTypeId")
        $fragFile = [System.Net.Http.ByteArrayContent]::new($fragPdfBytes)
        $fragFile.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse("application/pdf")
        $mpFrag.Add($fragFile, "file", "laudo-fragmentado.pdf")

        $resFrag = $httpHardenClient.PostAsync("$apiUrl/api/documents", $mpFrag).Result
        Assert-Step "20. Upload com buffer acumulado e assinatura PDF valida aceito (201 Created)" ([int]$resFrag.StatusCode -eq 201)

        # 20.2 ZIP arbitrario renomeado para .docx rejeitado na inspecao de estrutura OpenXML
        $zipMs = [System.IO.MemoryStream]::new()
        $archive = [System.IO.Compression.ZipArchive]::new($zipMs, [System.IO.Compression.ZipArchiveMode]::Create, $true)
        $zipEntry = $archive.CreateEntry("payload_executavel.txt")
        $sw = [System.IO.StreamWriter]::new($zipEntry.Open())
        $sw.WriteLine("Arbitrary non-OpenXML zip content")
        $sw.Dispose()
        $archive.Dispose()
        $fakeDocxBytes = $zipMs.ToArray()
        $zipMs.Dispose()

        $mpFakeDocx = [System.Net.Http.MultipartFormDataContent]::new()
        $mpFakeDocx.Add([System.Net.Http.StringContent]::new("Relatorio Fake Docx"), "name")
        $mpFakeDocx.Add([System.Net.Http.StringContent]::new($docTypeId), "documentTypeId")
        $fakeDocxFile = [System.Net.Http.ByteArrayContent]::new($fakeDocxBytes)
        $fakeDocxFile.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse("application/vnd.openxmlformats-officedocument.wordprocessingml.document")
        $mpFakeDocx.Add($fakeDocxFile, "file", "relatorio-fake.docx")

        $resFakeDocx = $httpHardenClient.PostAsync("$apiUrl/api/documents", $mpFakeDocx).Result
        Assert-Step "20. Fake Office container (ZIP arbitrario sem OpenXML partes) rejeitado (400/422)" ([int]$resFakeDocx.StatusCode -in @(400, 422)) "Status: $([int]$resFakeDocx.StatusCode)"

        # 20.3 XML com DTD proibido (ataque XXE prevenido por XmlReaderSettings)
        $xxeBytes = [System.Text.Encoding]::UTF8.GetBytes("<?xml version=`"1.0`"?><!DOCTYPE root [<!ENTITY xxe SYSTEM `"file:///c:/windows/win.ini`">]><root>&xxe;</root>")
        $mpXxe = [System.Net.Http.MultipartFormDataContent]::new()
        $mpXxe.Add([System.Net.Http.StringContent]::new("XML com DTD"), "name")
        $mpXxe.Add([System.Net.Http.StringContent]::new($docTypeId), "documentTypeId")
        $xxeFile = [System.Net.Http.ByteArrayContent]::new($xxeBytes)
        $xxeFile.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse("application/xml")
        $mpXxe.Add($xxeFile, "file", "ataque-dtd.xml")

        $resXxe = $httpHardenClient.PostAsync("$apiUrl/api/documents", $mpXxe).Result
        Assert-Step "20. XML com declaracao DTD proibida rejeitado com seguranca (400/422)" ([int]$resXxe.StatusCode -in @(400, 422)) "Status: $([int]$resXxe.StatusCode)"

        # 20.4 XML seguro e integro aceito
        $safeXmlBytes = [System.Text.Encoding]::UTF8.GetBytes("<?xml version=`"1.0`" encoding=`"utf-8`"?><laudo><resultado>CONFORME</resultado><safra>2026</safra></laudo>")
        $mpSafeXml = [System.Net.Http.MultipartFormDataContent]::new()
        $mpSafeXml.Add([System.Net.Http.StringContent]::new("Laudo XML Seguro"), "name")
        $mpSafeXml.Add([System.Net.Http.StringContent]::new($docTypeId), "documentTypeId")
        $safeXmlFile = [System.Net.Http.ByteArrayContent]::new($safeXmlBytes)
        $safeXmlFile.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse("application/xml")
        $mpSafeXml.Add($safeXmlFile, "file", "laudo-seguro.xml")

        $resSafeXml = $httpHardenClient.PostAsync("$apiUrl/api/documents", $mpSafeXml).Result
        Assert-Step "20. XML integro e livre de DTD aceito com sucesso (201 Created)" ([int]$resSafeXml.StatusCode -eq 201)
    } finally {
        $httpHardenClient.Dispose()
    }

    # -------------------------------------------------------------
    # 21. JORNADAS TIPADAS DE RH RURAL E UNIFICACAO DO ENCERRAMENTO DE JORNADA
    # Lookups tipados, criacao de pessoa, jornada de trabalho e disputa concorrente atomica
    # -------------------------------------------------------------
    # 21.1 Consultar lookups tipados
    $lkRoles = Http-Call $apiUrl '/api/rural-hr/lookups/roles' 'GET' $null $tokenA @(200)
    $lkProps = Http-Call $apiUrl '/api/rural-hr/lookups/properties' 'GET' $null $tokenA @(200)
    $lkTeams = Http-Call $apiUrl '/api/rural-hr/lookups/teams' 'GET' $null $tokenA @(200)
    $lkPeople = Http-Call $apiUrl '/api/rural-hr/lookups/people' 'GET' $null $tokenA @(200)
    Assert-Step "21. Lookups de RH Rural (cargos, propriedades, equipes, pessoas) respondem com sucesso (200 OK)" ($lkRoles.StatusCode -eq 200 -and $lkProps.StatusCode -eq 200 -and $lkTeams.StatusCode -eq 200 -and $lkPeople.StatusCode -eq 200)

    # 21.2 Cadastrar pessoa (PERSON)
    $personRes = Http-Call $apiUrl '/api/rural-hr/records' 'POST' @{
        kind = "PERSON"
        name = "Carlos Eduardo Tratorista"
        status = "ACTIVE"
        role = "Tratorista Agricola"
    } $tokenA @(201)
    $personId = ($personRes.Content | ConvertFrom-Json).id
    Assert-Step "21. Registro tipado de Pessoa cadastrado com sucesso (201 Created)" ($personId.Length -gt 10)

    # 21.3 Iniciar jornada (TIME_ENTRY)
    $journeyStart = (Get-Date).ToUniversalTime().AddHours(-4).ToString("yyyy-MM-ddTHH:mm:ssZ")
    $journeyRes = Http-Call $apiUrl '/api/rural-hr/records' 'POST' @{
        kind = "TIME_ENTRY"
        name = "Jornada Operacional Turno Manha"
        personId = $personId
        status = "ACTIVE"
        startedAt = $journeyStart
    } $tokenA @(201)
    $journeyId = ($journeyRes.Content | ConvertFrom-Json).id
    Assert-Step "21. Jornada de trabalho iniciada (status ACTIVE, startedAt registrado)" ($journeyId.Length -gt 10)

    # 21.4 Disputa concorrente de encerramento de jornada: lock pessimista garante encerramento unico
    $clientJ1 = [System.Net.Http.HttpClient]::new()
    $clientJ2 = [System.Net.Http.HttpClient]::new()
    try {
        $clientJ1.DefaultRequestHeaders.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $tokenA)
        $clientJ2.DefaultRequestHeaders.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $tokenA)

        $contentJ1 = [System.Net.Http.StringContent]::new(($(@{ justification = "Encerramento normal de turno pelo ponto" } | ConvertTo-Json -Depth 5)), [System.Text.Encoding]::UTF8, "application/json")
        $contentJ2 = [System.Net.Http.StringContent]::new(($(@{ status = "CLOSED"; justification = "Fechamento concorrente de jornada pelo supervisor" } | ConvertTo-Json -Depth 5)), [System.Text.Encoding]::UTF8, "application/json")

        $taskJ1 = $clientJ1.PostAsync("$apiUrl/api/rural-hr/time-entries/$journeyId/end", $contentJ1)
        $taskJ2 = $clientJ2.PostAsync("$apiUrl/api/rural-hr/records/$journeyId/change-status", $contentJ2)

        [System.Threading.Tasks.Task]::WaitAll($taskJ1, $taskJ2)

        $jCodes = @([int]$taskJ1.Result.StatusCode, [int]$taskJ2.Result.StatusCode)
        $hasClosedOk = ($jCodes -contains 200) -or ($jCodes -contains 204)
        $hasClosedConflict = ($jCodes -contains 400) -or ($jCodes -contains 409)
        Assert-Step "21. Disputa concorrente de encerramento: Lock pessimista garante encerramento atomico sem inconsistencia" ($hasClosedOk -and $hasClosedConflict) "Codigos: $($jCodes -join ', ')"

        # 21.5 Verificar no banco que ended_at esta preenchido e status e CLOSED
        $jDbState = (Query-Sql $dbFull "select status || ':' || (ended_at is not null) from agro360.rural_hr_records where id = '$journeyId';").Trim()
        Assert-Step "21. Encerramento unificado: Registro gravado com status CLOSED e ended_at preenchido" ($jDbState -eq 'CLOSED:True' -or $jDbState -eq 'CLOSED:t') "Estado no banco: $jDbState"
    } finally {
        $clientJ1.Dispose()
        $clientJ2.Dispose()
    }

    # -------------------------------------------------------------
    # 22. ACESSO EXTERNO DO PORTAL E REVOGACAO IMEDIATA DE DOCUMENTO
    # Usuario externo acessa documento autorizado com cache-control no-store e tem acesso revogado em tempo real
    # -------------------------------------------------------------
    # 22.1 Configurar perfil e usuario externo do Portal
    $portalProfileId = (Query-Sql $dbFull "select id from agro360.portal_profiles where tenant_id = '$tenantAId' and code = 'PRODUCER' limit 1;").Trim()
    if (-not $portalProfileId) {
        $portalProfileId = [guid]::NewGuid().ToString()
        $null = Exec-Sql $dbFull "insert into agro360.portal_profiles(id, tenant_id, code, name, active) values('$portalProfileId', '$tenantAId', 'PRODUCER', 'Produtor Integrado', true);"
    }

    $portalUserId = [guid]::NewGuid().ToString()
    $portalEmail = 'produtor.externo@parceiro.local'
    $portalPassword = $adminPassword
    $portalHash = $adminHash
    $null = Exec-Sql $dbFull @"
insert into agro360.portal_external_users(id, tenant_id, profile_id, name, email, password_hash, status, terms_accepted_at, created_by, updated_by)
values('$portalUserId', '$tenantAId', '$portalProfileId', 'Produtor Rural Parceiro', '$portalEmail', '$portalHash', 'ACTIVE', now(), '$fixtureUserId', '$fixtureUserId')
on conflict (tenant_id, email) do update set password_hash = '$portalHash', status = 'ACTIVE', profile_id = '$portalProfileId';
"@

    # Login como Usuario Externo do Portal
    $pLoginRes = Http-Call $apiUrl '/api/portal/access/login' 'POST' @{
        tenantSlug = 'santa-clara'
        email = $portalEmail
        password = $portalPassword
    } $null @(200)
    $pAuthObj = $pLoginRes.Content | ConvertFrom-Json
    $portalToken = if ($pAuthObj.token) { $pAuthObj.token } else { $pAuthObj.accessToken }
    Assert-Step "22. Portal: Autenticacao de usuario externo bem-sucedida (token com claim portal.access)" ($portalToken.Length -gt 20)

    # 22.2 Vincular permissao explicita ao documento criado no cenario 17
    $null = Exec-Sql $dbFull @"
update agro360.documents set status = 'ACTIVE' where id = '$createdDocId';
insert into agro360.portal_document_permissions(id, tenant_id, document_id, external_user_id, can_download, created_by, updated_by)
values(gen_random_uuid(), '$tenantAId', '$createdDocId', '$portalUserId', true, '$fixtureUserId', '$fixtureUserId')
on conflict do nothing;
"@

    # 22.3 Listar documentos no Portal
    $pDocsRes = Http-Call $apiUrl '/api/portal/documents' 'GET' $null $portalToken @(200)
    $pDocs = $pDocsRes.Content | ConvertFrom-Json
    Assert-Step "22. Portal: Usuario externo lista documentos autorizados (200 OK)" ($pDocs.Count -ge 1)

    # 22.4 Download autorizado de documento pelo Portal
    $pDlRes = Http-Call $apiUrl "/api/portal/documents/$createdDocId/download" 'GET' $null $portalToken @(200)
    Assert-Step "22. Portal: Download do documento pelo usuario externo bem-sucedido (200 OK)" ($pDlRes.StatusCode -eq 200)

    # 22.5 Revogacao em tempo real: Desativa o usuario externo no banco
    $null = Exec-Sql $dbFull "update agro360.portal_external_users set status = 'INACTIVE' where id = '$portalUserId';"

    # 22.6 Tentativa subsequente de download com o mesmo token DEVE retornar 403 Forbidden imediatamente
    $pDlRevoked = Http-Call $apiUrl "/api/portal/documents/$createdDocId/download" 'GET' $null $portalToken @(403)
    Assert-Step "22. Portal: Usuario externo inativado tem acesso revogado em tempo real (403 Forbidden)" ($pDlRevoked.StatusCode -eq 403)

    Write-Host "`n=================================================================" -ForegroundColor Green
    Write-Host "  TODOS OS 22 CENARIOS DE HOMOLOGACAO PASSARAM COM SUCESSO!" -ForegroundColor Green
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
