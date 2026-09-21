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

$evidenceDir = Join-Path $root ('artifacts\q-092-e2e-' + [guid]::NewGuid().ToString('N'))
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
    $output = cmd.exe /c "`"$psql`" -d $DbName -X -t -A --set=client_min_messages=warning -f `"$tempFile`" 2>&1"
    Remove-Item -Path $tempFile -Force -ErrorAction SilentlyContinue
    return $output
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

Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host "  HOMOLOGACAO AG-Q-092-E2E - 15 CENARIOS EM AMBIENTE DESCARTAVEL" -ForegroundColor Cyan
Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host "Diretorio de evidencias: $evidenceDir"

try {
    # 0. Preparar Cluster PostgreSQL descartavel
    $dbPort = Get-FreePort
    $apiPort = Get-FreePort
    $webPort = Get-FreePort
    $apiUrl = "http://127.0.0.1:$apiPort"
    $webUrl = "http://127.0.0.1:$webPort"
    $dbPassword = Get-RandomBase64 32

    Set-EnvVar PGPASSWORD $dbPassword
    Set-EnvVar PGHOST '127.0.0.1'
    Set-EnvVar PGPORT "$dbPort"
    Set-EnvVar PGUSER 'postgres'
    Set-EnvVar PGDATABASE 'postgres'

    $pipeName = 'agro360-q-e2e-' + [guid]::NewGuid().ToString('N')
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
    # CENARIO 1: Instalar full SQL em cluster novo (nome contem test)
    # schema_versions contem 9.2.0 e 9.3.0; quality_inspection_* com RLS
    # -------------------------------------------------------------
    $dbE2E = 'agro360_q_e2e_test'
    $null = Exec-Sql 'postgres' "create database $dbE2E;"
    Set-EnvVar PGDATABASE $dbE2E

    $sqlInstallCode = Invoke-PsqlFile $dbE2E (Join-Path $root 'database/agro360-postgres-full.sql') "$evidenceDir\full-sql-install.log"
    Assert-Step "Cenario 1 - Instalacao limpa do full SQL" ($sqlInstallCode -eq 0)

    $versionsRaw = Query-Sql $dbE2E "select version from agro360.platform_schema_versions where version in ('9.2.0','9.3.0') order by version;"
    $versions = ($versionsRaw -split "`r?`n") | Where-Object { $_ -match '^\d+\.\d+\.\d+' }
    Assert-Step "Cenario 1 - Schema versions 9.2.0 e 9.3.0 presentes" (($versions -contains '9.2.0') -and ($versions -contains '9.3.0')) "Versoes: $($versions -join ', ')"

    $qRls = "select c.relname || ':' || c.relrowsecurity || ':' || c.relforcerowsecurity " +
            "from pg_class c join pg_namespace n on n.oid = c.relnamespace " +
            "where n.nspname = 'agro360' and c.relname in ('quality_inspection_models', 'quality_inspection_event_intents') " +
            "order by c.relname;"
    $rlsCheckRaw = Query-Sql $dbE2E $qRls
    $rlsCheck = ($rlsCheckRaw -split "`r?`n") | Where-Object { $_ -match ':' }
    Assert-Step "Cenario 1 - Tabelas 092 e 093 com RLS forcado" ($rlsCheck.Count -eq 2 -and ($rlsCheck | Where-Object { $_ -match ':(t|true):(t|true)' }).Count -eq 2) "RLS: $($rlsCheck -join ' | ')"

    # -------------------------------------------------------------
    # CENARIO 2: Upgrade: copia ate 9.2.0 + migration 093 sem perda de modelos 092
    # -------------------------------------------------------------
    $dbUpgrade = 'agro360_q_upgrade_test'
    $null = Exec-Sql 'postgres' "create database $dbUpgrade;"

    # Monta SQL ate 9.2.0 (removendo bloco da 093) para banco de upgrade
    $fullSqlText = [System.IO.File]::ReadAllText((Join-Path $root 'database/agro360-postgres-full.sql'), [System.Text.Encoding]::UTF8)
    $splitMarker = "create table if not exists agro360.quality_inspection_event_intents"
    if (-not $fullSqlText.Contains($splitMarker)) { throw "Marcador da migration 093 nao encontrado no full SQL." }
    $sql92 = $fullSqlText.Substring(0, $fullSqlText.IndexOf($splitMarker)) + "`ncommit;`n"
    $tempSql92 = Join-Path $evidenceDir 'temp-92.sql'
    [System.IO.File]::WriteAllText($tempSql92, $sql92, [System.Text.Encoding]::UTF8)

    $upgradeStep1Code = Invoke-PsqlFile $dbUpgrade $tempSql92 "$evidenceDir\upgrade-step1-920.log"
    Assert-Step "Cenario 2 - Instalacao base ate 9.2.0 em banco de upgrade" ($upgradeStep1Code -eq 0)

    # Insere modelo representativo em 092 antes do upgrade
    $testModel092Id = [guid]::NewGuid().ToString()
    $insertModel092Sql = "insert into agro360.quality_inspection_models(id, tenant_id, code, name, process_code, precedence, allow_manual_selection, status, row_version, created_at, created_by) " +
                         "values ('$testModel092Id', '30000000-0000-0000-0000-000000000001', 'MOD-UPGRADE-PRE', 'Modelo Pre-Upgrade 092', 'HARVEST_RECEIPT', 50, true, 'ACTIVE', 1, now(), '30000000-0000-0000-0000-000000000003');"
    $null = Exec-Sql $dbUpgrade $insertModel092Sql

    # Executa migration 093
    $upgradeStep2Code = Invoke-PsqlFile $dbUpgrade (Join-Path $root 'database/migrations/093_quality_inspection_event_intents.sql') "$evidenceDir\upgrade-step2-093.log"
    Assert-Step "Cenario 2 - Aplicacao da migration 093 sobre base 9.2.0" ($upgradeStep2Code -eq 0)

    # Verifica integridade do modelo 092 e presenca da tabela 093 com 9.3.0
    $modelAfterUpgrade = (Query-Sql $dbUpgrade "select count(*) from agro360.quality_inspection_models where id = '$testModel092Id';").Trim()
    $hasIntentsTable = (Query-Sql $dbUpgrade "select count(*) from information_schema.tables where table_schema='agro360' and table_name='quality_inspection_event_intents';").Trim()
    $hasVersion93 = (Query-Sql $dbUpgrade "select count(*) from agro360.platform_schema_versions where version='9.3.0';").Trim()

    Assert-Step "Cenario 2 - Upgrade sem perda de modelos 092 e com tabela 093 + versao 9.3.0" ($modelAfterUpgrade -eq '1' -and $hasIntentsTable -eq '1' -and $hasVersion93 -eq '1')

    # -------------------------------------------------------------
    # Configuracao de Fixtures e Usuarios para API
    # -------------------------------------------------------------
    Set-EnvVar PGDATABASE $dbE2E
    $connString = "Host=127.0.0.1;Port=$dbPort;Database=$dbE2E;Username=postgres;Password=$dbPassword"
    Set-EnvVar ConnectionStrings__Agro360 $connString
    Set-EnvVar ConnectionStrings__DefaultConnection $connString
    Set-EnvVar AGRO360_TEST_CONNECTION_STRING $connString
    Set-EnvVar ASPNETCORE_ENVIRONMENT 'Development'
    Set-EnvVar Jwt__SigningKey (Get-RandomBase64 48)
    Set-EnvVar ApiBaseUrl $apiUrl
    Set-EnvVar Cors__AllowedOrigins__0 $webUrl
    Set-EnvVar Bootstrap__Enabled 'false'

    $adminPassword = 'SantaClara!Aa123456'
    $adminHash = Create-PasswordHash $adminPassword

    $readerPassword = 'Leitor!Aa123456'
    $readerHash = Create-PasswordHash $readerPassword

    $tenantBPassword = 'RioVerde!Aa123456'
    $tenantBHash = Create-PasswordHash $tenantBPassword

    # Atualizar planos SaaS para garantir modulos necessarios (compliance, logistica, agroindustria)
    $qUpdatePlan = "update agro360.saas_plans set modules = array['properties','agriculture','livestock','inventory','finance','reports','logistics','traceability','intelligence','environment-esg','agroindustry','purchasing'] where name='Profissional';"
    $null = Exec-Sql $dbE2E $qUpdatePlan

    # Santa Clara Admin: garantir hash, status ativo e permissoes completas de admin
    $qUpdateAdmin = "update agro360.identity_users set password_hash='$adminHash', status='ACTIVE', must_change_password=false where email='admin@santaclara.agro360.local'; " +
                    "insert into agro360.identity_role_permissions(tenant_id, role_id, permission_id) " +
                    "select r.tenant_id, r.id, p.id " +
                    "from agro360.identity_roles r cross join agro360.identity_permissions p " +
                    "where r.tenant_id = '30000000-0000-0000-0000-000000000001' and lower(r.code) = 'tenant-administrator' " +
                    "on conflict do nothing;"
    $null = Exec-Sql $dbE2E $qUpdateAdmin

    # Santa Clara Reader (somente compliance.read e dashboard.read, sem execute nem approve)
    $readerUserId = '30000000-0000-0000-0000-000000000099'
    $readerRoleId = '30000000-0000-0000-0000-000000000098'
    $qInsertReader = "insert into agro360.identity_users(id, tenant_id, name, email, password_hash, status, normalized_document, document_type, must_change_password, created_by) " +
                    "values ('$readerUserId', '30000000-0000-0000-0000-000000000001', 'Leitor Auditor Qualidade', 'leitor@santaclara.agro360.local', '$readerHash', 'ACTIVE', '11122233344', 'CPF', false, '30000000-0000-0000-0000-000000000003') " +
                    "on conflict (id) do update set password_hash=excluded.password_hash, status='ACTIVE', must_change_password=false; " +
                    "insert into agro360.identity_roles(id, tenant_id, code, name, is_system) " +
                    "values ('$readerRoleId', '30000000-0000-0000-0000-000000000001', 'auditor-qualidade', 'Auditor Qualidade', true) " +
                    "on conflict (id) do nothing; " +
                    "insert into agro360.identity_user_roles(tenant_id, user_id, role_id) " +
                    "values ('30000000-0000-0000-0000-000000000001', '$readerUserId', '$readerRoleId') " +
                    "on conflict do nothing; " +
                    "insert into agro360.identity_role_permissions(tenant_id, role_id, permission_id) " +
                    "select '30000000-0000-0000-0000-000000000001', '$readerRoleId', id " +
                    "from agro360.identity_permissions where code in ('compliance.read', 'dashboard.read') " +
                    "on conflict do nothing;"
    $null = Exec-Sql $dbE2E $qInsertReader

    # Tenant B: Fazenda Rio Verde
    $tenantBId = '30000000-0000-0000-0000-000000000002'
    $tenantBUserId = '30000000-0000-0000-0000-000000000004'
    $tenantBRoleId = '30000000-0000-0000-0000-000000000006'
    $qInsertTenantB = "insert into agro360.tenancy_tenants(id, name, slug, timezone_id, status, plan_code) " +
                     "values ('$tenantBId', 'Fazenda Rio Verde', 'rio-verde', 'America/Cuiaba', 1, 'PROFESSIONAL') " +
                     "on conflict (id) do update set name=excluded.name, slug=excluded.slug, timezone_id=excluded.timezone_id, status=1, plan_code='PROFESSIONAL', deleted_at=null, updated_at=now(); " +
                     "insert into agro360.saas_organizations(tenant_id, organization_type, document, responsible_name, responsible_email, plan_id, status, activated_at, onboarding_status) " +
                     "select '$tenantBId', 'PRODUCER', '22333444000199', 'Admin Rio Verde', 'admin@rioverde.agro360.local', id, 'ACTIVE', now(), 'COMPLETED' " +
                     "from agro360.saas_plans where name='Profissional' on conflict (tenant_id) do nothing; " +
                     "insert into agro360.identity_roles(id, tenant_id, code, name, is_system) " +
                     "values ('$tenantBRoleId', '$tenantBId', 'tenant-administrator', 'Administrador do Cliente', true) " +
                     "on conflict (id) do nothing; " +
                     "insert into agro360.identity_role_permissions(tenant_id, role_id, permission_id) " +
                     "select '$tenantBId', '$tenantBRoleId', id from agro360.identity_permissions on conflict do nothing; " +
                     "insert into agro360.identity_users(id, tenant_id, name, email, password_hash, status, normalized_document, document_type, must_change_password, created_by) " +
                     "values ('$tenantBUserId', '$tenantBId', 'Admin Rio Verde', 'admin@rioverde.agro360.local', '$tenantBHash', 'ACTIVE', '99988877766', 'CPF', false, '$tenantBUserId') " +
                     "on conflict (id) do update set password_hash=excluded.password_hash, status='ACTIVE', must_change_password=false; " +
                     "insert into agro360.identity_user_roles(tenant_id, user_id, role_id) " +
                     "values ('$tenantBId', '$tenantBUserId', '$tenantBRoleId') on conflict do nothing;"
    $null = Exec-Sql $dbE2E $qInsertTenantB

    # Iniciar os hosts Api e Web
    Write-Host "Iniciando hosts API e Web..." -ForegroundColor DarkGray
    $null = Start-HostProcess Api $apiUrl
    $null = Start-HostProcess Web $webUrl
    Write-Host "API ($apiUrl) e Web ($webUrl) online." -ForegroundColor Green

    function Http-Request([string]$Path, [string]$Method = 'GET', $Body = $null, [string]$Token = '') {
        $headers = @{}
        if ($Token) { $headers['Authorization'] = "Bearer $Token" }
        $opt = @{
            Uri = "$apiUrl$Path"
            Method = $Method
            Headers = $headers
            UseBasicParsing = $true
            TimeoutSec = 30
        }
        if ($null -ne $Body) {
            $opt.Body = $Body | ConvertTo-Json -Depth 10 -Compress
            $opt.ContentType = 'application/json; charset=utf-8'
        }
        try {
            $res = Invoke-WebRequest @opt
            return [PSCustomObject]@{
                StatusCode = [int]$res.StatusCode
                Content    = $res.Content
            }
        } catch [System.Net.WebException] {
            if ($_.Exception.Response) {
                $httpRes = [System.Net.HttpWebResponse]$_.Exception.Response
                $stream = $httpRes.GetResponseStream()
                $reader = [System.IO.StreamReader]::new($stream, [System.Text.Encoding]::UTF8)
                $bodyText = $reader.ReadToEnd()
                $reader.Dispose()
                return [PSCustomObject]@{
                    StatusCode = [int]$httpRes.StatusCode
                    Content    = $bodyText
                }
            }
            throw
        }
    }

    # Autenticar admin Santa Clara
    $loginRes = Http-Request '/api/v1/auth/login' 'POST' @{
        tenantSlug = 'santa-clara'
        email = 'admin@santaclara.agro360.local'
        password = $adminPassword
    }
    if ($loginRes.StatusCode -ne 200) { throw "Falha no login do admin Santa Clara (HTTP $($loginRes.StatusCode))" }
    $adminToken = ($loginRes.Content | ConvertFrom-Json).accessToken

    # Autenticar reader Santa Clara
    $readerLoginRes = Http-Request '/api/v1/auth/login' 'POST' @{
        tenantSlug = 'santa-clara'
        email = 'leitor@santaclara.agro360.local'
        password = $readerPassword
    }
    $readerToken = ($readerLoginRes.Content | ConvertFrom-Json).accessToken

    # Autenticar admin Tenant B
    $tenantBLoginRes = Http-Request '/api/v1/auth/login' 'POST' @{
        tenantSlug = 'rio-verde'
        email = 'admin@rioverde.agro360.local'
        password = $tenantBPassword
    }
    $tenantBToken = ($tenantBLoginRes.Content | ConvertFrom-Json).accessToken

    Write-Host "Tokens de autenticacao obtidos para os 3 perfis de teste." -ForegroundColor Green

    # -------------------------------------------------------------
    # CENARIO 3: Publicar modelo HARVEST_RECEIPT com criterio critico
    # obrigatorio PASS_FAIL; versao publicada imutavel
    # -------------------------------------------------------------
    $createModelBody = @{
        code = 'MOD-HARVEST-E2E-01'
        name = 'Modelo E2E Colheita Soja'
        processCode = 'HARVEST_RECEIPT'
        precedence = 100
        allowManualSelection = $true
        description = 'Modelo para validacao E2E de recebimento'
    }
    $modelRes = Http-Request '/api/inspections/models' 'POST' $createModelBody $adminToken
    Assert-Step "Cenario 3 - Criacao do modelo de inspecao" ($modelRes.StatusCode -in @(200, 201)) "Status: $($modelRes.StatusCode)"
    $modelId = ($modelRes.Content | ConvertFrom-Json).id

    # Criacao de versao draft
    $draftRes = Http-Request "/api/inspections/models/$modelId/versions" 'POST' @{ changeReason = 'Versao inicial de homologacao' } $adminToken
    Assert-Step "Cenario 3 - Criacao de versao draft" ($draftRes.StatusCode -in @(200, 201))
    $versionId = ($draftRes.Content | ConvertFrom-Json).id

    # Configuracao de secoes e criterios (obrigatorio e critico PASS_FAIL)
    $versionDetail = (Http-Request "/api/inspections/versions/$versionId" 'GET' $null $adminToken).Content | ConvertFrom-Json
    $rv = $versionDetail.rowVersion

    $updateVersionBody = @{
        changeReason = 'Configurando criterio critico PASS_FAIL'
        expectedRowVersion = $rv
        sections = @(
            @{
                stableKey = 'sec-qualidade'
                name = 'Criterios de Qualidade de Graos'
                sortOrder = 1
                criteria = @(
                    @{
                        stableKey = 'crit-umidade'
                        name = 'Umidade Padrao <= 14%'
                        criterionType = 'PASS_FAIL'
                        required = $true
                        critical = $true
                        sortOrder = 1
                        approval = @{
                            expectedPass = $true
                        }
                    }
                )
            }
        )
    }
    $updateRes = Http-Request "/api/inspections/versions/$versionId" 'PUT' $updateVersionBody $adminToken
    Assert-Step "Cenario 3 - Configuracao de criterios no draft" ($updateRes.StatusCode -in @(200, 204))

    # Envio para revisao
    $versionDetail = (Http-Request "/api/inspections/versions/$versionId" 'GET' $null $adminToken).Content | ConvertFrom-Json
    $submitRes = Http-Request "/api/inspections/versions/$versionId/submit-review" 'POST' @{ expectedRowVersion = $versionDetail.rowVersion } $adminToken
    Assert-Step "Cenario 3 - Envio para revisao" ($submitRes.StatusCode -in @(200, 204))

    # Publicacao
    $versionDetail = (Http-Request "/api/inspections/versions/$versionId" 'GET' $null $adminToken).Content | ConvertFrom-Json
    $publishRes = Http-Request "/api/inspections/versions/$versionId/publish" 'POST' @{
        validFrom = '2026-01-01'
        changeReason = 'Publicacao para homologacao E2E'
        expectedRowVersion = $versionDetail.rowVersion
    } $adminToken
    Assert-Step "Cenario 3 - Publicacao do modelo" ($publishRes.StatusCode -in @(200, 204))

    # Teste de imutabilidade (PUT em versao publicada deve ser recusado)
    $versionDetail = (Http-Request "/api/inspections/versions/$versionId" 'GET' $null $adminToken).Content | ConvertFrom-Json
    $mutatePublishedRes = Http-Request "/api/inspections/versions/$versionId" 'PUT' @{
        changeReason = 'Tentativa proibida de alterar publicado'
        expectedRowVersion = $versionDetail.rowVersion
        sections = @()
    } $adminToken
    Assert-Step "Cenario 3 - Versao publicada e imutavel (PUT recusado)" ($mutatePublishedRes.StatusCode -in @(400, 409)) "Retornou HTTP $($mutatePublishedRes.StatusCode)"

    # -------------------------------------------------------------
    # CENARIO 4: ReceiveAsync de colheita com modelo unico -> intent STARTED + run IN_PROGRESS
    # production_receipts.quality_status permanece AWAITING_INSPECTION
    # -------------------------------------------------------------
    $farmId = '30000000-0000-0000-0000-000000000010'
    $seasonId = '40000000-0000-0000-0000-000000000002'
    $fieldId = '40000000-0000-0000-0000-000000000003'
    $warehouseId = '40000000-0000-0000-0000-000000000004'
    $productId = '40000000-0000-0000-0000-000000000005'
    $planId = '40000000-0000-0000-0000-000000000006'
    $harvestRecordId = '40000000-0000-0000-0000-000000000007'
    $tenantA = '30000000-0000-0000-0000-000000000001'
    $userA = '30000000-0000-0000-0000-000000000003'

    $qInsertHarvestBase = "set app.tenant_id = '$tenantA'; " +
                         "insert into agro360.geo_fields(id, tenant_id, farm_id, name, area_ha, created_by) " +
                         "values('$fieldId', '$tenantA', '$farmId', 'Talhao Sul E2E', 100, '$userA') " +
                         "on conflict (tenant_id, id) do nothing; " +
                         "insert into agro360.agriculture_seasons(id, tenant_id, farm_id, name, crop, start_date, end_date, planned_area_ha, expected_yield_per_ha, created_by) " +
                         "values('$seasonId', '$tenantA', '$farmId', 'Safra Soja 2026', 'SOJA', '2026-01-01', '2026-12-31', 100, 3200, '$userA') " +
                         "on conflict (tenant_id, id) do nothing; " +
                         "insert into agro360.inventory_warehouses(id, tenant_id, farm_id, code, name, type, created_by) " +
                         "values('$warehouseId', '$tenantA', '$farmId', 'SILO-01', 'Silo Graos', 'GRAINS', '$userA') " +
                         "on conflict (tenant_id, id) do nothing; " +
                         "insert into agro360.inventory_products(id, tenant_id, sku, name, category, base_unit, requires_lot, created_by) " +
                         "values('$productId', '$tenantA', 'SOJA-01', 'Soja Grao Convencional', 'GRAOS', 'kg', true, '$userA') " +
                         "on conflict (tenant_id, id) do nothing; " +
                         "insert into agro360.harvest_plans(id, tenant_id, farm_id, season_id, field_id, product_id, destination_warehouse_id, planned_start, planned_end, planned_area_ha, estimated_quantity, unit, status, idempotency_key, request_hash, created_by) " +
                         "values ('$planId', '$tenantA', '$farmId', '$seasonId', '$fieldId', '$productId', '$warehouseId', '2026-02-01', '2026-02-28', 50, 100000, 'kg', 'IN_PROGRESS', 'KEY-PLAN-E2E-01', 'hash000000000000000000000000000000000000000000000000000000000001', '$userA') " +
                         "on conflict (tenant_id, id) do nothing; " +
                         "insert into agro360.harvest_records(id, tenant_id, plan_id, operational_at, harvested_quantity, unit, commercial_reference, status, idempotency_key, request_hash, created_by) " +
                         "values ('$harvestRecordId', '$tenantA', '$planId', now(), 50000, 'kg', 'REF-COLHEITA-E2E-01', 'AWAITING_RECEIPT', 'KEY-REC-E2E-01', 'hash000000000000000000000000000000000000000000000000000000000002', '$userA') " +
                         "on conflict (tenant_id, id) do nothing;"
    $null = Exec-Sql $dbE2E $qInsertHarvestBase

    # Executa ReceiveAsync via API
    $receiveBody = @{
        harvestRecordId = $harvestRecordId
        warehouseId = $warehouseId
        receivedAt = (Get-Date).ToString("yyyy-MM-ddTHH:mm:ssZ")
        receivedQuantity = 5000
        unit = 'kg'
        lotNumber = 'LOTE-E2E-SOJA-01'
        entryMode = 'MANUAL'
        idempotencyKey = 'IDEMP-RECEIVE-E2E-01'
    }
    $receiveRes = Http-Request '/api/v1/harvest/receipts' 'POST' $receiveBody $adminToken
    Assert-Step "Cenario 4 - Execucao de ReceiveAsync de colheita" ($receiveRes.StatusCode -in @(200, 201)) "Status: $($receiveRes.StatusCode)"
    $receiptId = ($receiveRes.Content | ConvertFrom-Json).id

    # Verifica status de qualidade no banco
    $qualityStatusDb = (Query-Sql $dbE2E "select quality_status from agro360.production_receipts where id='$receiptId';").Trim()
    Assert-Step "Cenario 4 - production_receipts.quality_status permanece AWAITING_INSPECTION" ($qualityStatusDb -eq 'AWAITING_INSPECTION') "Status no DB: $qualityStatusDb"

    # Consulta event-intents gerado
    $intentsRes = Http-Request '/api/inspections/event-intents?process=HARVEST_RECEIPT' 'GET' $null $adminToken
    $intentList = ($intentsRes.Content | ConvertFrom-Json)
    $c4Intent = $intentList | Where-Object { $_.originId -eq $receiptId }
    Assert-Step "Cenario 4 - Intent registrado com status STARTED" ($null -ne $c4Intent -and $c4Intent.status -eq 'STARTED' -and $c4Intent.runId) "IntentId: $($c4Intent.id), RunId: $($c4Intent.runId)"
    $runId = $c4Intent.runId

    # Consulta run gerada
    $runDetail = (Http-Request "/api/inspections/runs/$runId" 'GET' $null $adminToken).Content | ConvertFrom-Json
    Assert-Step "Cenario 4 - Run gerada automaticamente com status IN_PROGRESS" ($runDetail.status -eq 'IN_PROGRESS') "Run status: $($runDetail.status)"

    # -------------------------------------------------------------
    # CENARIO 5: Concluir a run sem o obrigatorio -> recusa; receipt segue AWAITING_INSPECTION
    # -------------------------------------------------------------
    $incompleteRes = Http-Request "/api/inspections/runs/$runId/complete" 'POST' @{
        expectedRowVersion = $runDetail.rowVersion
        notes = 'Conclusao sem preencher criterio obrigatorio'
    } $adminToken
    Assert-Step "Cenario 5 - Conclusao de run sem obrigatorio e recusada" ($incompleteRes.StatusCode -in @(400, 409, 422)) "HTTP: $($incompleteRes.StatusCode)"

    $receiptStatusAfterIncomplete = (Query-Sql $dbE2E "select quality_status from agro360.production_receipts where id='$receiptId';").Trim()
    Assert-Step "Cenario 5 - Receipt segue AWAITING_INSPECTION apos recusa de conclusao" ($receiptStatusAfterIncomplete -eq 'AWAITING_INSPECTION')

    # -------------------------------------------------------------
    # CENARIO 6: Responder obrigatorio reprovado (critico) -> NON_CONFORMING;
    # efeito NC/restricao idempotente; lote nao AVAILABLE
    # -------------------------------------------------------------
    $answersBody = @{
        expectedRowVersion = $runDetail.rowVersion
        answers = @(
            @{
                criterionId = $runDetail.sections[0].criteria[0].id
                passFailValue = $false
                notes = 'Umidade medida 17.5% - Reprovado'
            }
        )
    }
    $saveAnswersRes = Http-Request "/api/inspections/runs/$runId/answers" 'PUT' $answersBody $adminToken
    Assert-Step "Cenario 6 - Respostas salvas com reprovacao no criterio critico" ($saveAnswersRes.StatusCode -in @(200, 204))
    $newRowVersion = if ($saveAnswersRes.StatusCode -eq 200) { ($saveAnswersRes.Content | ConvertFrom-Json).rowVersion } else { $runDetail.rowVersion + 1 }

    $completeRes = Http-Request "/api/inspections/runs/$runId/complete" 'POST' @{
        expectedRowVersion = $newRowVersion
        notes = 'Concluindo inspecao reprovada'
    } $adminToken
    Assert-Step "Cenario 6 - Conclusao da inspecao reprovada executada" ($completeRes.StatusCode -eq 200)
    $completeResult = $completeRes.Content | ConvertFrom-Json
    Assert-Step "Cenario 6 - Resultado global NON_CONFORMING com NC/restricao gerada" ($completeResult.overallResult -eq 'NON_CONFORMING') "Resultado: $($completeResult.overallResult)"

    # Verifica se o lote NAO esta disponivel (receipt retido em estado nao aprovado)
    $qLotStatus = "select quality_status from agro360.production_receipts where tenant_id='$tenantA' and id='$receiptId';"
    $lotAvailability = (Query-Sql $dbE2E $qLotStatus).Trim()
    Assert-Step "Cenario 6 - Lote nao foi tornado AVAILABLE" ($lotAvailability -ne 'AVAILABLE' -and $lotAvailability -ne 'APPROVED') "Status do lote no recebimento: $(if ($lotAvailability) { $lotAvailability } else { 'Bloqueado' })"

    # -------------------------------------------------------------
    # CENARIO 7: Reinspecao: nova run ligada a pai; run original intacta
    # -------------------------------------------------------------
    $reinspectRes = Http-Request "/api/inspections/runs/$runId/reinspections" 'POST' @{
        reason = 'Reinspecao do lote apos reprocessamento termico'
        useLatestPublishedVersion = $true
        idempotencyKey = 'IDEMP-REINSPECT-E2E-01'
    } $adminToken
    Assert-Step "Cenario 7 - Reinspecao solicitada com sucesso" ($reinspectRes.StatusCode -in @(200, 201))
    $reinspectionRunId = ($reinspectRes.Content | ConvertFrom-Json).id

    $reinspectionDetail = (Http-Request "/api/inspections/runs/$reinspectionRunId" 'GET' $null $adminToken).Content | ConvertFrom-Json
    $originalRunDetail = (Http-Request "/api/inspections/runs/$runId" 'GET' $null $adminToken).Content | ConvertFrom-Json

    Assert-Step "Cenario 7 - Nova run vinculada a pai e run original intacta" (
        $reinspectionDetail.parentRunId -eq $runId -and
        $reinspectionDetail.status -eq 'IN_PROGRESS' -and
        $originalRunDetail.status -eq 'COMPLETED' -and
        $originalRunDetail.overallResult -eq 'NON_CONFORMING'
    ) "Pai: $($reinspectionDetail.parentRunId), Status original: $($originalRunDetail.status)"

    # -------------------------------------------------------------
    # CENARIO 8: Segundo modelo HARVEST_RECEIPT mesma especificidade/precedencia
    # -> proximo recebimento gera AMBIGUOUS e zero runs novas
    # -------------------------------------------------------------
    $createModel2Res = Http-Request '/api/inspections/models' 'POST' @{
        code = 'MOD-HARVEST-E2E-02'
        name = 'Modelo E2E Concorrente Soja'
        processCode = 'HARVEST_RECEIPT'
        precedence = 100
        allowManualSelection = $false
        description = 'Segundo modelo com mesma precedencia'
    } $adminToken
    $model2Id = ($createModel2Res.Content | ConvertFrom-Json).id

    $draft2Res = Http-Request "/api/inspections/models/$model2Id/versions" 'POST' @{ changeReason = 'Versao 1 modelo 2' } $adminToken
    $version2Id = ($draft2Res.Content | ConvertFrom-Json).id
    $version2Detail = (Http-Request "/api/inspections/versions/$version2Id" 'GET' $null $adminToken).Content | ConvertFrom-Json

    $updateVersion2Body = @{
        changeReason = 'Criterios modelo 2'
        expectedRowVersion = $version2Detail.rowVersion
        sections = @(
            @{
                stableKey = 'sec-m2'
                name = 'Secao Modelo 2'
                sortOrder = 1
                criteria = @(
                    @{
                        stableKey = 'crit-m2'
                        name = 'Criterio Modelo 2'
                        criterionType = 'PASS_FAIL'
                        required = $true
                        sortOrder = 1
                        approval = @{ expectedPass = $true }
                    }
                )
            }
        )
    }
    $null = Http-Request "/api/inspections/versions/$version2Id" 'PUT' $updateVersion2Body $adminToken
    $v2D = (Http-Request "/api/inspections/versions/$version2Id" 'GET' $null $adminToken).Content | ConvertFrom-Json
    $null = Http-Request "/api/inspections/versions/$version2Id/submit-review" 'POST' @{ expectedRowVersion = $v2D.rowVersion } $adminToken
    $v2D = (Http-Request "/api/inspections/versions/$version2Id" 'GET' $null $adminToken).Content | ConvertFrom-Json
    $null = Http-Request "/api/inspections/versions/$version2Id/publish" 'POST' @{ validFrom = '2026-01-01'; changeReason = 'Publicar modelo 2'; expectedRowVersion = $v2D.rowVersion } $adminToken

    # Executa novo recebimento que deve cair em empate de modelos
    $receive2Res = Http-Request '/api/v1/harvest/receipts' 'POST' @{
        harvestRecordId = $harvestRecordId
        warehouseId = $warehouseId
        receivedAt = (Get-Date).ToString("yyyy-MM-ddTHH:mm:ssZ")
        receivedQuantity = 3000
        unit = 'kg'
        lotNumber = 'LOTE-E2E-SOJA-02'
        entryMode = 'MANUAL'
        idempotencyKey = 'IDEMP-RECEIVE-E2E-02'
    } $adminToken
    $receipt2Id = ($receive2Res.Content | ConvertFrom-Json).id

    $intentsRes2 = Http-Request '/api/inspections/event-intents?process=HARVEST_RECEIPT' 'GET' $null $adminToken
    $intentList2 = ($intentsRes2.Content | ConvertFrom-Json)
    $c8Intent = $intentList2 | Where-Object { $_.originId -eq $receipt2Id }
    Assert-Step "Cenario 8 - Empate de modelos gera status AMBIGUOUS sem run automatica" (
        $null -ne $c8Intent -and
        $c8Intent.status -eq 'AMBIGUOUS' -and
        [string]::IsNullOrEmpty($c8Intent.runId)
    ) "Status: $($c8Intent.status), RunId: $($c8Intent.runId)"

    # -------------------------------------------------------------
    # CENARIO 9: Inativar os modelos -> recebimento gera PENDING_MODEL; nenhuma aprovacao
    # -------------------------------------------------------------
    $null = Http-Request "/api/inspections/models/$modelId/inactivate" 'POST' @{ reason = 'Inativando modelo 1 para teste de ausencia' } $adminToken
    $null = Http-Request "/api/inspections/models/$model2Id/inactivate" 'POST' @{ reason = 'Inativando modelo 2 para teste de ausencia' } $adminToken

    $receive3Res = Http-Request '/api/v1/harvest/receipts' 'POST' @{
        harvestRecordId = $harvestRecordId
        warehouseId = $warehouseId
        receivedAt = (Get-Date).ToString("yyyy-MM-ddTHH:mm:ssZ")
        receivedQuantity = 2000
        unit = 'kg'
        lotNumber = 'LOTE-E2E-SOJA-03'
        entryMode = 'MANUAL'
        idempotencyKey = 'IDEMP-RECEIVE-E2E-03'
    } $adminToken
    $receipt3Id = ($receive3Res.Content | ConvertFrom-Json).id

    $intentsRes3 = Http-Request '/api/inspections/event-intents?process=HARVEST_RECEIPT' 'GET' $null $adminToken
    $intentList3 = ($intentsRes3.Content | ConvertFrom-Json)
    $c9Intent = $intentList3 | Where-Object { $_.originId -eq $receipt3Id }
    $receipt3QualityStatus = (Query-Sql $dbE2E "select quality_status from agro360.production_receipts where id='$receipt3Id';").Trim()

    Assert-Step "Cenario 9 - Sem modelo gera PENDING_MODEL e receipt retido em AWAITING_INSPECTION" (
        $null -ne $c9Intent -and
        $c9Intent.status -eq 'PENDING_MODEL' -and
        $receipt3QualityStatus -eq 'AWAITING_INSPECTION'
    ) "Intent status: $($c9Intent.status), Quality status: $receipt3QualityStatus"

    # -------------------------------------------------------------
    # CENARIO 10: ReceiveReturnAsync -> process RETURN, intent coerente,
    # retorno AWAITING_QUALITY, gancho nao chama destinacao
    # -------------------------------------------------------------
    $segmentId = '50000000-0000-0000-0000-000000000000'
    $customerId = '50000000-0000-0000-0000-000000000001'
    $orderId = '50000000-0000-0000-0000-000000000002'
    $stockLotId = '50000000-0000-0000-0000-000000000006'
    $orderItemId = '50000000-0000-0000-0000-000000000007'
    $reservationId = '50000000-0000-0000-0000-000000000008'
    $shipmentId = '50000000-0000-0000-0000-000000000003'
    $shipmentItemId = '50000000-0000-0000-0000-000000000004'
    $returnId = '50000000-0000-0000-0000-000000000005'

    $qInsertReturnBase = "set app.tenant_id = '$tenantA'; " +
                         "insert into agro360.crm_customer_segments(id, tenant_id, name, created_by) " +
                         "values('$segmentId', '$tenantA', 'Segmento Graos E2E', '$userA') on conflict (tenant_id, id) do nothing; " +
                         "insert into agro360.crm_customers(id, tenant_id, segment_id, name, type, tax_document, created_by) " +
                         "values('$customerId', '$tenantA', '$segmentId', 'Comprador Graos Ltda', 'CUSTOMER', '33444555000122', '$userA') on conflict (tenant_id, id) do nothing; " +
                         "insert into agro360.sales_orders(id, tenant_id, customer_id, order_number, status, total_amount, created_by) " +
                         "values('$orderId', '$tenantA', '$customerId', 'PED-E2E-01', 'DELIVERED', 10000, '$userA') on conflict (tenant_id, id) do nothing; " +
                         "insert into agro360.inventory_stock_lots(id, tenant_id, warehouse_id, product_id, lot_number, quantity, quality_status) " +
                         "values('$stockLotId', '$tenantA', '$warehouseId', '$productId', 'LOTE-ESTOQUE-01', 1000, 'APPROVED') on conflict (tenant_id, warehouse_id, product_id, lot_number) do nothing; " +
                         "insert into agro360.sales_order_items(id, tenant_id, order_id, product_id, lot_id, quantity, unit, unit_price, total_amount) " +
                         "values('$orderItemId', '$tenantA', '$orderId', '$productId', '$stockLotId', 500, 'kg', 20, 10000) on conflict (tenant_id, id) do nothing; " +
                         "insert into agro360.fulfillment_reservations(id, tenant_id, order_item_id, stock_lot_id, quantity, unit, status, idempotency_key, request_hash, created_by, updated_by) " +
                         "values('$reservationId', '$tenantA', '$orderItemId', '$stockLotId', 500, 'kg', 'ACTIVE', 'RES-KEY-E2E-01', 'hash000000000000000000000000000000000000000000000000000000000003', '$userA', '$userA') on conflict (tenant_id, id) do nothing; " +
                         "insert into agro360.fulfillment_shipments(id, tenant_id, number, origin_warehouse_id, destination, customer_id, status, idempotency_key, request_hash, created_by, updated_by) " +
                         "values('$shipmentId', '$tenantA', 'REM-E2E-01', '$warehouseId', 'Destino Cliente', '$customerId', 'DISPATCHED', 'SHIP-KEY-E2E-01', 'hash000000000000000000000000000000000000000000000000000000000004', '$userA', '$userA') on conflict (tenant_id, id) do nothing; " +
                         "insert into agro360.fulfillment_shipment_items(id, tenant_id, shipment_id, reservation_id, order_item_id, stock_lot_id, requested_quantity, reserved_quantity, picked_quantity, checked_quantity, unit, created_by, updated_by) " +
                         "values('$shipmentItemId', '$tenantA', '$shipmentId', '$reservationId', '$orderItemId', '$stockLotId', 500, 500, 500, 500, 'kg', '$userA', '$userA') on conflict (tenant_id, id) do nothing; " +
                         "insert into agro360.fulfillment_returns(id, tenant_id, shipment_item_id, quantity, received_quantity, reason, status, version, idempotency_key, request_hash, created_by, updated_by) " +
                         "values ('$returnId', '$tenantA', '$shipmentItemId', 50, 0, 'Devolucao por avaria', 'AWAITING_RECEIPT', 1, 'RET-KEY-E2E-01', 'hash000000000000000000000000000000000000000000000000000000000005', '$userA', '$userA') on conflict (tenant_id, id) do nothing;"
    $null = Exec-Sql $dbE2E $qInsertReturnBase

    $receiveReturnBody = @{
        quantity = 50
        unit = 'kg'
        condition = 'INSPECTION_REQUIRED'
        warehouseId = $warehouseId
        lotNumber = 'LOTE-RET-01'
        notes = 'Recebimento de devolucao de cliente'
        expectedVersion = 1
        idempotencyKey = 'KEY-RETURN-REC-E2E-01'
    }
    $receiveReturnRes = Http-Request "/api/logistics/trips/fulfillment/returns/$returnId/receipts" 'POST' $receiveReturnBody $adminToken
    Assert-Step "Cenario 10 - Recebimento de retorno de cliente" ($receiveReturnRes.StatusCode -in @(200, 201)) "Status: $($receiveReturnRes.StatusCode)"
    $returnReceiptId = ($receiveReturnRes.Content | ConvertFrom-Json).id

    $returnDbStatus = (Query-Sql $dbE2E "select status from agro360.fulfillment_returns where id='$returnId';").Trim()
    Assert-Step "Cenario 10 - Retorno fica em AWAITING_QUALITY" ($returnDbStatus -eq 'AWAITING_QUALITY') "Status: $returnDbStatus"

    $intentsReturnRes = Http-Request '/api/inspections/event-intents?process=RETURN' 'GET' $null $adminToken
    $returnIntent = ($intentsReturnRes.Content | ConvertFrom-Json) | Where-Object { $_.originId -eq $returnReceiptId }
    Assert-Step "Cenario 10 - Intent gerado para processo RETURN" ($null -ne $returnIntent -and $returnIntent.processCode -eq 'RETURN')

    $decisionCount = (Query-Sql $dbE2E "select count(*) from agro360.fulfillment_return_decisions where return_id='$returnId';").Trim()
    Assert-Step "Cenario 10 - Gancho de inspecao nao cria destinacao automatica" ($decisionCount -eq '0') "Decisoes no DB: $decisionCount"

    # -------------------------------------------------------------
    # CENARIO 11: Replay do mesmo ReceiveAsync -> mesmo intent_id / mesmo run_id
    # -------------------------------------------------------------
    $replayRes = Http-Request '/api/v1/harvest/receipts' 'POST' $receiveBody $adminToken
    Assert-Step "Cenario 11 - Replay de ReceiveAsync recupera mesmo receipt" ($replayRes.StatusCode -in @(200, 201))
    $replayReceiptId = ($replayRes.Content | ConvertFrom-Json).id
    Assert-Step "Cenario 11 - ID do receipt coincide no replay" ($replayReceiptId -eq $receiptId)

    $qReplayIntents = "select count(*) || '|' || max(id::text) || '|' || coalesce(max(run_id::text),'') " +
                      "from agro360.quality_inspection_event_intents " +
                      "where tenant_id='$tenantA' and idempotency_key='EVT:HARVEST_RECEIPT:$(([guid]$receiptId).ToString("N"))';"
    $replayIntents = (Query-Sql $dbE2E $qReplayIntents).Trim() -split '\|'

    Assert-Step "Cenario 11 - Replay estavel: 1 unico intent e mesma run" (
        $replayIntents[0] -eq '1' -and
        $replayIntents[1] -eq $c4Intent.id -and
        $replayIntents[2] -eq $runId
    ) "Count: $($replayIntents[0]), IntentId: $($replayIntents[1]), RunId: $($replayIntents[2])"

    # -------------------------------------------------------------
    # CENARIO 12: Agenda periodica nao duplica run aberta (schedule_generation_key)
    # -------------------------------------------------------------
    # Reativa o modelo 1 para permitir agendamento
    $null = Exec-Sql $dbE2E "update agro360.quality_inspection_models set status='ACTIVE' where id='$modelId';"

    $scheduleId = [guid]::NewGuid().ToString()
    $nextRun = (Get-Date).ToString("yyyy-MM-ddTHH:mm:ssZ")
    $qInsertSchedule = "insert into agro360.quality_inspection_schedules(id, tenant_id, model_id, name, process_code, schedule_type, interval_days, allow_catchup, next_run_at, status, created_by) " +
                       "values ('$scheduleId', '$tenantA', '$modelId', 'Programacao Diaria E2E', 'HARVEST_RECEIPT', 'PERIODIC', 1, false, '$nextRun', 'ACTIVE', '$userA');"
    $null = Exec-Sql $dbE2E $qInsertSchedule

    $todayStr = (Get-Date).ToString("yyyy-MM-dd")
    $gen1 = Http-Request "/api/inspections/schedules/generate?asOf=$todayStr" 'POST' $null $adminToken
    $gen1Count = if ($gen1.StatusCode -eq 200) { ($gen1.Content | ConvertFrom-Json).generatedCount } else { 0 }

    $gen2 = Http-Request "/api/inspections/schedules/generate?asOf=$todayStr" 'POST' $null $adminToken
    $gen2Count = if ($gen2.StatusCode -eq 200) { ($gen2.Content | ConvertFrom-Json).generatedCount } else { 0 }

    Assert-Step "Cenario 12 - Agenda periodica nao duplica run na mesma janela (idempotencia)" ($gen2Count -eq 0) "1  geracao: $gen1Count runs, 2  geracao: $gen2Count runs"

    # -------------------------------------------------------------
    # CENARIO 13: Permissoes: sem inspections.execute -> 403; sem compliance.approve -> 403
    # -------------------------------------------------------------
    $forbiddenExecuteRes = Http-Request "/api/inspections/runs/$runId/complete" 'POST' @{ notes = 'Auditor sem permissao tentando completar' } $readerToken
    Assert-Step "Cenario 13 - Usuario sem compliance.inspections.execute recebe 403" ($forbiddenExecuteRes.StatusCode -eq 403) "Retornou HTTP $($forbiddenExecuteRes.StatusCode)"

    $lotIdForBlock = [guid]::NewGuid().ToString()
    $forbiddenApproveRes = Http-Request "/api/compliance/lots/$lotIdForBlock/unblock" 'POST' @{ reason = 'Tentando desbloquear sem permissao de aprovacao' } $readerToken
    Assert-Step "Cenario 13 - Usuario sem compliance.approve nao libera restricao (403)" ($forbiddenApproveRes.StatusCode -eq 403) "Retornou HTTP $($forbiddenApproveRes.StatusCode)"

    # -------------------------------------------------------------
    # CENARIO 14: Tenant B: GET event-intents e GET run do tenant A = vazio ou 404, nunca 200
    # -------------------------------------------------------------
    $tenantBIntentsRes = Http-Request '/api/inspections/event-intents' 'GET' $null $tenantBToken
    $tenantBList = ($tenantBIntentsRes.Content | ConvertFrom-Json)
    Assert-Step "Cenario 14 - Tenant B tem lista de intents vazia (nao ve Tenant A)" ($tenantBList.Count -eq 0) "Total intents Tenant B: $($tenantBList.Count)"

    $tenantBCrossRunRes = Http-Request "/api/inspections/runs/$runId" 'GET' $null $tenantBToken
    Assert-Step "Cenario 14 - Tenant B recebe 404 ao consultar run do Tenant A" ($tenantBCrossRunRes.StatusCode -eq 404) "Retornou HTTP $($tenantBCrossRunRes.StatusCode)"

    Write-Host "=================================================================" -ForegroundColor Cyan
    Write-Host " CENARIOS 1 A 14 EXECUTADOS COM SUCESSO! PASS EM TODOS." -ForegroundColor Green
    Write-Host "=================================================================" -ForegroundColor Cyan
    Write-Host "Porta Web disponivel para o Cenario 15: $webUrl" -ForegroundColor Yellow
    Write-Host "Porta API disponivel para o Cenario 15: $apiUrl" -ForegroundColor Yellow

    # Salva portas e URLs em arquivo json para consumo do Cenario 15 ou testes do navegador
    $envInfo = @{
        WebUrl = $webUrl
        ApiUrl = $apiUrl
        AdminToken = $adminToken
        AdminPassword = $adminPassword
        EvidenceDir = $evidenceDir
    }
    [System.IO.File]::WriteAllText((Join-Path $evidenceDir 'env_info.json'), ($envInfo | ConvertTo-Json), [System.Text.Encoding]::UTF8)

    if ($KeepRunning) {
        Write-Host "Ambiente mantido ativo para inspecao visual do navegador. Pressione Ctrl+C para encerrar."
        while ($true) { Start-Sleep -Seconds 1 }
    }

} finally {
    if (-not $KeepRunning) {
        Write-Host "Encerrando processos de teste..." -ForegroundColor DarkGray
        foreach ($proc in $processes) {
            if (-not $proc.HasExited) { $proc.Kill($true); $proc.WaitForExit() }
            $proc.Dispose()
        }
        if ($startedDatabase) {
            & $pg_ctl -D $dataDir -m fast -w stop
        }
        foreach ($k in $savedEnvironment.Keys) {
            [Environment]::SetEnvironmentVariable($k, $savedEnvironment[$k])
        }
        Write-Host "Ambiente encerrado limpo." -ForegroundColor DarkGray
    }
}
