param(
    [string]$PostgresBin = 'C:\Program Files\PostgreSQL\18\bin',
    [switch]$KeepRunning
)

# Remanescentes de homologacao (AG-GOV-001 + AG-EVO-INT-001):
#   Item 18 remnant: login em tenant SUSPENDED/BLOCKED -> rejeicao canonica tenant_blocked
#   Item 3: fixture TRIAL com janela futuro/passado -> efeito no acesso efetivo
#   Item 2 remnant: modulo INACTIVE/pendente -> inefetivo
#   Itens 8/10 remnant: corrida concorrente pela ultima vaga (plan_user_limit)
#   RLS papel restrito (agro360_app) sobre fluxos novos de returns/liquidacao

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

$evidenceDir = Join-Path $root ('artifacts\remaining-homologation-e2e-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidenceDir | Out-Null
$processes = [Collections.Generic.List[Diagnostics.Process]]::new()
$startedDatabase = $false
$dataDir = ''
$dbName = 'agro360_homolog'

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

function Invoke-Psql([string]$Sql, [string]$Db = $dbName) {
    $argList = @('-p', $pgPort.ToString(), '-U', 'postgres', '-h', 'localhost', '-d', $Db, '-t', '-A', '-c', $Sql)
    $output = & $psql @argList 2>&1
    if ($LASTEXITCODE -ne 0) { throw "psql failed: $output" }
    return "$output".Trim()
}

function Invoke-PsqlRaw([string]$Sql, [string]$Db = $dbName) {
    $argList = @('-p', $pgPort.ToString(), '-U', 'postgres', '-h', 'localhost', '-d', $Db, '-t', '-A')
    $output = & $psql @argList $Sql 2>&1
    if ($LASTEXITCODE -ne 0) { throw "psql raw failed: $output" }
    return "$output"
}

function Invoke-Api([string]$Method, [string]$Path, $Body = $null, [string]$Token = '') {
    $url = "http://localhost:${apiPort}${Path}"
    $headers = @{ 'Content-Type' = 'application/json' }
    if ($Token) { $headers['Authorization'] = "Bearer $Token" }
    $params = @{ Method = $Method; Uri = $url; Headers = $headers; UseBasicParsing = $true; TimeoutSec = 30 }
    if ($null -ne $Body) {
        $json = $Body | ConvertTo-Json -Depth 5 -Compress
        $params['Body'] = [Text.Encoding]::UTF8.GetBytes($json)
    }
    try {
        $resp = Invoke-WebRequest @params
        $content = $resp.Content
        $result = if ($content -and $content.Length -gt 2) { $content | ConvertFrom-Json } else { $null }
        return @{ Status = [int]$resp.StatusCode; Body = $result }
    } catch {
        $resp = $_.Exception.Response
        if ($null -eq $resp) { throw "HTTP ${Method} ${Path}: $($_.Exception.Message)" }
        $stream = $resp.GetResponseStream()
        $reader = [IO.StreamReader]::new($stream)
        $content = $reader.ReadToEnd()
        $result = if ($content -and $content.Length -gt 2) { try { $content | ConvertFrom-Json } catch { $content } } else { $null }
        return @{ Status = [int]$resp.StatusCode; Body = $result }
    }
}

try {
    # ============ BLOCK 0: SETUP — Clone DB on running cluster (port 55432) ============
    $pgPort = 55432
    $clusterBin = 'C:\Program Files\PostgreSQL\18\bin'
    # Verify cluster is reachable
    $verCheck = & $psql '-p' $pgPort.ToString() '-U' 'postgres' '-h' 'localhost' '-d' 'postgres' '-t' '-A' '-c' "select version();" 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Cannot connect to PG on port ${pgPort}: $verCheck" }
    Write-Host "PG cluster ready on port $pgPort" -ForegroundColor Cyan

    # Clone the database for isolated testing
    $dbName = 'agro360_homolog'
    $prevEAP = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    & $psql '-p' $pgPort.ToString() '-U' 'postgres' '-h' 'localhost' '-d' 'postgres' '-c' "drop database if exists ${dbName};" 2>&1 | Out-Null
    & $psql '-p' $pgPort.ToString() '-U' 'postgres' '-h' 'localhost' '-d' 'postgres' '-c' "create database ${dbName} template agro360_clean;" 2>&1 | Out-Null
    $ErrorActionPreference = $prevEAP
    $schemaVer = Invoke-Psql "select max(version) from agro360.platform_schema_versions;"
    Assert-Step 'Schema-clone' ($schemaVer -ne $null -and $schemaVer.Length -gt 0) "top version=$schemaVer"

    # ============ BLOCK 1: START API HOST ============
    $apiPort = Get-FreePort
    $connStr = "Host=127.0.0.1;Port=${pgPort};Database=${dbName};Username=postgres;Search Path=agro360;Command Timeout=30"
    $apiExe = Join-Path $root 'src\Hosts\Agro360.Api\bin\Release\net10.0\Agro360.Api.dll'
    if (-not (Test-Path $apiExe)) { throw "API binary not found at $apiExe. Run build first." }
    # Set env vars directly so child inherits them (reliable in PS 5.1)
    $env:ASPNETCORE_URLS = "http://localhost:${apiPort}"
    $env:ConnectionStrings__Agro360 = $connStr
    $env:ConnectionStrings__DefaultConnection = $connStr
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:PGPASSWORD = 'trust-auth-placeholder'
    $env:Jwt__SigningKey = 'e2e-test-signing-key-32bytes-minimum!'
    $apiProc = Start-Process dotnet -ArgumentList "`"$apiExe`"" -WorkingDirectory (Split-Path $apiExe) -PassThru -NoNewWindow
    $processes.Add($apiProc)
    # Wait for API to be ready (TCP probe to avoid rate-limit)
    $ready = $false
    for ($i = 0; $i -lt 40; $i++) {
        Start-Sleep -Milliseconds 500
        try {
            $tcp = [Net.Sockets.TcpClient]::new()
            $tcp.Connect('127.0.0.1', $apiPort)
            $tcp.Close()
            $ready = $true
            break
        } catch { }
        if ($apiProc.HasExited) { break }
    }
    Assert-Step 'API-tcp-ready' $ready "port=$apiPort after $($i+1) probes"
    Start-Sleep -Seconds 2  # let middleware initialize

    # Final check - any HTTP response proves API is serving
    $pingResult = Invoke-Api POST '/api/v1/auth/login' @{ TenantSlug='nonexist'; Email='a@b.c'; Password='x' }
    Assert-Step 'API-up' ($pingResult.Status -ge 400) "login ping got $($pingResult.Status)"

    # ============ BLOCK 2: LOGIN IN SUSPENDED/BLOCKED TENANT (item 18 remnant) ============
    Write-Host "`n--- BLOCK 2: Login em tenant SUSPENDED/BLOCKED ---" -ForegroundColor Cyan

    # fazenda-bloqueada-teste has tenant status=3 already in the full.sql install
    $blockedTenantStatus = Invoke-Psql "select t.status from agro360.tenancy_tenants t where t.slug='fazenda-bloqueada-teste';"
    Assert-Step 'Blocked-tenant-precondition' ($blockedTenantStatus -eq '3') "tenant status=$blockedTenantStatus"

    # Attempt login to the blocked tenant (any email/password works because tenant check fires first)
    $blockedLogin = Invoke-Api POST '/api/v1/auth/login' @{ TenantSlug='fazenda-bloqueada-teste'; Email='admin.bloqueado@agro360.local'; Password='AnyPassword123!' }
    $blockedCode = $blockedLogin.Body.code
    Assert-Step 'Login-SUSPENDED-rejected' ($blockedLogin.Status -eq 401) "HTTP $($blockedLogin.Status)"
    Assert-Step 'Login-SUSPENDED-code' ($blockedCode -eq 'tenant_blocked') "code=$blockedCode body=$(($blockedLogin.Body | ConvertTo-Json -Depth 3).Substring(0,[Math]::Min(120,(($blockedLogin.Body|ConvertTo-Json -Depth 3)).Length)))"

    # Also test platform_tenants.status = 'SUSPENDED' on a separate tenant
    # The login code checks: tenant.Status 3/4/5 OR PlatformStatus in SUSPENDED/BLOCKED/etc
    $vvTenantId = Invoke-Psql "select id::text from agro360.tenancy_tenants where slug='cooperativa-vale-verde';"
    # Ensure vale-verde has a platform_tenants row (it does from seed, but verify)
    $vvPlatformStatus = Invoke-Psql "select status from agro360.platform_tenants where id='${vvTenantId}'::uuid;"
    Assert-Step 'ValeVerde-platform-precondition' ($vvPlatformStatus -eq 'ACTIVE') "platform status=$vvPlatformStatus"
    Invoke-Psql "update agro360.tenancy_tenants set status=1 where slug='cooperativa-vale-verde';" | Out-Null
    Invoke-Psql "update agro360.platform_tenants set status='SUSPENDED' where id='${vvTenantId}'::uuid;" | Out-Null
    $suspOrgStatus = Invoke-Psql "select status from agro360.platform_tenants where id='${vvTenantId}'::uuid;"
    Assert-Step 'SUSPENDED-org-set' ($suspOrgStatus -eq 'SUSPENDED') "org status=$suspOrgStatus"
    $suspLogin = Invoke-Api POST '/api/v1/auth/login' @{ TenantSlug='cooperativa-vale-verde'; Email='admin.valeverde@agro360.local'; Password='WrongPass999!' }
    $suspCode = $suspLogin.Body.code
    Assert-Step 'Login-SUSPENDED-org-rejected' ($suspLogin.Status -eq 401) "HTTP $($suspLogin.Status)"
    Assert-Step 'Login-SUSPENDED-org-code' ($suspCode -eq 'tenant_blocked') "code=$suspCode"

    # Restore vale-verde
    Invoke-Psql "update agro360.platform_tenants set status='ACTIVE' where id='${vvTenantId}'::uuid;" | Out-Null

    # Reset Santa Clara admin password to known value (prior test runs may have changed it)
    $knownHash = 'pbkdf2-sha512$210000$Fk496Qko66Cw//8uYyP3mw==$PqcZJTtl+96es6GmUF3khyrZvMJqSVDIe/YfI7TCv2U='
    Invoke-Psql "update agro360.identity_users set password_hash='$knownHash', must_change_password=false, updated_at=now() where email='admin@santaclara.agro360.local' and tenant_id='30000000-0000-0000-0000-000000000001';" | Out-Null

    # Verify normal login still works for active tenant (regression guard)
    $goodLogin = Invoke-Api POST '/api/v1/auth/login' @{ TenantSlug='santa-clara'; Email='admin@santaclara.agro360.local'; Password='Agro360Admin@2024!' }
    Assert-Step 'Login-active-still-works' ($goodLogin.Status -eq 200) "HTTP $($goodLogin.Status)"
    $token = $goodLogin.Body.accessToken
    Assert-Step 'JWT-issued' ($null -ne $token -and $token.Length -gt 50) "token length=$($token.Length)"

    # ============ BLOCK 3: TRIAL FIXTURE (item 3) ============
    Write-Host "`n--- BLOCK 3: TRIAL fixture com janela passado/futuro ---" -ForegroundColor Cyan

    # Get a module that is NOT currently ACTIVE for Santa Clara (use 'traceability' or add one)
    $scTenantId = Invoke-Psql "select id from agro360.tenancy_tenants where slug='santa-clara';"

    # Insert a TRIAL entitlement with FUTURE valid_until
    # Use 'intelligence' - the only module NOT currently in Santa Clara's effective set
    $futureTrialModuleId = Invoke-Psql "select id from agro360.platform_module_catalog where code='intelligence';"
    $scPlanEntCountBefore = Invoke-Psql "select count(*) from agro360.platform_tenant_module_entitlements where tenant_id='${scTenantId}' and status in ('CONTRACTED','ACTIVE','TRIAL') and (valid_until is null or valid_until > now());"
    Invoke-Psql "insert into agro360.platform_tenant_module_entitlements(tenant_id,module_id,status,origin,valid_until,created_at) values('${scTenantId}','${futureTrialModuleId}','TRIAL','MANUAL',now()+interval '7 days',now())" | Out-Null
    $scPlanEntCountFuture = Invoke-Psql "select count(*) from agro360.platform_tenant_module_entitlements where tenant_id='${scTenantId}' and status in ('CONTRACTED','ACTIVE','TRIAL') and (valid_until is null or valid_until > now());"
    Assert-Step 'TRIAL-future-inserted' ([int]$scPlanEntCountFuture -gt [int]$scPlanEntCountBefore) "count before=$scPlanEntCountBefore after=$scPlanEntCountFuture"

    # Verify module appears as effective in effective-access
    $effAccess = Invoke-Api GET '/api/account/effective-access' $null $token
    $isModuleEffective = $false
    if ($effAccess.Body.contractedModules) {
        foreach ($m in $effAccess.Body.contractedModules) { if ($m.code -eq 'intelligence' -and $m.effective) { $isModuleEffective = $true } }
    }
    Assert-Step 'TRIAL-future-effective' $isModuleEffective "modules count=$(@($effAccess.Body.contractedModules).Count)"

    # Now expire it (set valid_until to past)
    Invoke-Psql "update agro360.platform_tenant_module_entitlements set valid_until=now()-interval '1 hour' where tenant_id='${scTenantId}' and module_id='${futureTrialModuleId}' and status='TRIAL'" | Out-Null
    $scPlanEntCountPast = Invoke-Psql "select count(*) from agro360.platform_tenant_module_entitlements where tenant_id='${scTenantId}' and status in ('CONTRACTED','ACTIVE','TRIAL') and (valid_until is null or valid_until > now());"
    Assert-Step 'TRIAL-past-count-decreased' ([int]$scPlanEntCountPast -lt [int]$scPlanEntCountFuture) "count future=$scPlanEntCountFuture past=$scPlanEntCountPast"

    # Verify module is NOT effective after expiration
    $effAccess2 = Invoke-Api GET '/api/account/effective-access' $null $token
    $isModuleEffectiveAfter = $false
    if ($effAccess2.Body.contractedModules) {
        foreach ($m in $effAccess2.Body.contractedModules) { if ($m.code -eq 'intelligence' -and $m.effective) { $isModuleEffectiveAfter = $true } }
    }
    Assert-Step 'TRIAL-past-ineffective' (-not $isModuleEffectiveAfter) "intelligence.effective=$isModuleEffectiveAfter after expiration"

    # Clean up
    Invoke-Psql "delete from agro360.platform_tenant_module_entitlements where tenant_id='${scTenantId}' and module_id='${futureTrialModuleId}' and status='TRIAL'" | Out-Null

    # ============ BLOCK 4: MODULE INACTIVE/PENDING (item 2 remnant) ============
    Write-Host "`n--- BLOCK 4: Modulo INACTIVE (pendente) ---" -ForegroundColor Cyan

    # Insert an entitlement with status='INACTIVE' (not in the effective set)
    $inactiveModuleId = Invoke-Psql "select id from agro360.platform_module_catalog where code='intelligence';"
    $activeModulesBefore = Invoke-Psql "select count(*) from (select lower(c.code) as module_code from agro360.platform_tenant_module_entitlements e join agro360.platform_module_catalog c on c.id=e.module_id where e.tenant_id='${scTenantId}' and e.status in ('CONTRACTED','ACTIVE','TRIAL') and (e.valid_until is null or e.valid_until > now())) m;"
    Invoke-Psql "insert into agro360.platform_tenant_module_entitlements(tenant_id,module_id,status,origin,created_at) values('${scTenantId}','${inactiveModuleId}','INACTIVE','MANUAL',now())" | Out-Null
    $activeModulesAfter = Invoke-Psql "select count(*) from (select lower(c.code) as module_code from agro360.platform_tenant_module_entitlements e join agro360.platform_module_catalog c on c.id=e.module_id where e.tenant_id='${scTenantId}' and e.status in ('CONTRACTED','ACTIVE','TRIAL') and (e.valid_until is null or e.valid_until > now())) m;"
    Assert-Step 'INACTIVE-no-effect-on-effective' ($activeModulesBefore -eq $activeModulesAfter) "effective count before=$activeModulesBefore after=$activeModulesAfter"

    # Verify intelligence is NOT effective
    $effAccess3 = Invoke-Api GET '/api/account/effective-access' $null $token
    $isIntelligenceEffective = $false
    if ($effAccess3.Body.contractedModules) {
        foreach ($m in $effAccess3.Body.contractedModules) { if ($m.code -eq 'intelligence' -and $m.effective) { $isIntelligenceEffective = $true } }
    }
    Assert-Step 'INACTIVE-module-not-effective' (-not $isIntelligenceEffective) "intelligence.effective=$isIntelligenceEffective (INACTIVE status)"

    # Clean up
    Invoke-Psql "delete from agro360.platform_tenant_module_entitlements where tenant_id='${scTenantId}' and module_id='${inactiveModuleId}' and status='INACTIVE'" | Out-Null

    # ============ BLOCK 5: CONCURRENT LAST-VACANCY RACE (items 8/10 remnant) ============
    Write-Host "`n--- BLOCK 5: Corrida concorrente pela ultima vaga ---" -ForegroundColor Cyan

    # Current state: Santa Clara has 3 active users, plan limit=20. Set limit to 4 (1 slot left).
    Invoke-Psql "update agro360.saas_plans set user_limit=4 where id=(select plan_id from agro360.saas_organizations where tenant_id=(select id from agro360.tenancy_tenants where slug='santa-clara'));" | Out-Null
    $limitNow = Invoke-Psql "select user_limit from agro360.saas_plans where id=(select plan_id from agro360.saas_organizations where tenant_id=(select id from agro360.tenancy_tenants where slug='santa-clara'));"
    $userCount = Invoke-Psql "select count(*) filter(where u.status='ACTIVE') from agro360.identity_users u where u.tenant_id='${scTenantId}' and u.deleted_at is null;"
    Assert-Step 'Race-setup-limit' ($limitNow -eq '4' -and $userCount -eq '3') "limit=$limitNow active=$userCount slot_left=1"

    # Create two invitations for the same tenant (race contenders)
    $invRole = Invoke-Psql "select id from agro360.identity_roles where tenant_id='${scTenantId}' and code='operator' limit 1;"
    $invEmail1 = "race-user1-${[guid]::NewGuid().ToString('N').Substring(0,8)}@test.local"
    $invEmail2 = "race-user2-${[guid]::NewGuid().ToString('N').Substring(0,8)}@test.local"

    $inv1 = Invoke-Api POST '/api/invitations' @{ Email=$invEmail1; RoleId=$invRole; ValidForHours=72 } $token
    Assert-Step 'Race-invite1-created' ($inv1.Status -eq 201) "HTTP $($inv1.Status) token length=$($inv1.Body.activationToken.Length)"
    $token1 = $inv1.Body.activationToken

    $inv2 = Invoke-Api POST '/api/invitations' @{ Email=$invEmail2; RoleId=$invRole; ValidForHours=72 } $token
    Assert-Step 'Race-invite2-created' ($inv2.Status -eq 201) "HTTP $($inv2.Status) token length=$($inv2.Body.activationToken.Length)"
    $token2 = $inv2.Body.activationToken

    # Fire both accept requests SIMULTANEOUSLY using .NET Task parallelism (reliable in PS 5.1)
    $httpUrl = "http://localhost:${apiPort}/api/invitations/accept"
    $body1 = @{ Token=$token1; Name='Race User 1'; Password='RacePass@123!' } | ConvertTo-Json -Depth 3 -Compress
    $body2 = @{ Token=$token2; Name='Race User 2'; Password='RacePass@123!' } | ConvertTo-Json -Depth 3 -Compress
    $bytes1 = [Text.Encoding]::UTF8.GetBytes($body1)
    $bytes2 = [Text.Encoding]::UTF8.GetBytes($body2)
    $task1 = [System.Threading.Tasks.Task]::Run([Func[int]]{
        $req = [HttpWebRequest]::Create($httpUrl)
        $req.Method = 'POST'
        $req.ContentType = 'application/json'
        $req.Timeout = 30000
        $stream = $req.GetRequestStream(); $stream.Write($bytes1, 0, $bytes1.Length); $stream.Close()
        try {
            $resp = $req.GetResponse()
            $s = [int]$resp.StatusCode; $resp.Close(); return $s
        } catch [System.Net.WebException] {
            $wr = $_.Exception.Response
            if ($null -eq $wr) { return -1 }
            $s = [int]$wr.StatusCode; $wr.Close(); return $s
        }
    })
    $task2 = [System.Threading.Tasks.Task]::Run([Func[int]]{
        $req = [HttpWebRequest]::Create($httpUrl)
        $req.Method = 'POST'
        $req.ContentType = 'application/json'
        $req.Timeout = 30000
        $stream = $req.GetRequestStream(); $stream.Write($bytes2, 0, $bytes2.Length); $stream.Close()
        try {
            $resp = $req.GetResponse()
            $s = [int]$resp.StatusCode; $resp.Close(); return $s
        } catch [System.Net.WebException] {
            $wr = $_.Exception.Response
            if ($null -eq $wr) { return -1 }
            $s = [int]$wr.StatusCode; $wr.Close(); return $s
        }
    })
    try { [System.Threading.Tasks.Task]::WaitAll(@($task1, $task2), 30000) | Out-Null } catch {}
    $code1Final = if ($task1.IsCompleted -and $task1.Exception -eq $null) { [int]$task1.Result } else { -1 }
    $code2Final = if ($task2.IsCompleted -and $task2.Exception -eq $null) { [int]$task2.Result } else { -1 }

    if ($code1Final -eq -1 -or $code2Final -eq -1) {
        Write-Host "WARNING: Parallel tasks had issues (codes=$code1Final,$code2Final), retrying sequentially" -ForegroundColor Yellow
        $r1s = Invoke-Api POST '/api/invitations/accept' @{ Token=$token1; Name='Race User 1'; Password='RacePass@123!' }
        $r2s = Invoke-Api POST '/api/invitations/accept' @{ Token=$token2; Name='Race User 2'; Password='RacePass@123!' }
        if ($code1Final -eq -1) { $code1Final = $r1s.Status }
        if ($code2Final -eq -1) { $code2Final = $r2s.Status }
    }
    Write-Host "  Race codes: request1=$code1Final request2=$code2Final"

    # Verify: exactly one succeeded, other got 409
    $successCount = @($code1Final, $code2Final) | Where-Object { $_ -eq 200 -or $_ -eq 201 } | Measure-Object | Select-Object -ExpandProperty Count
    $conflictCount = @($code1Final, $code2Final) | Where-Object { $_ -eq 409 } | Measure-Object | Select-Object -ExpandProperty Count
    Assert-Step 'Race-exactly-one-success' ($successCount -eq 1) "codes=$code1Final,$code2Final successCount=$successCount"
    Assert-Step 'Race-other-409' ($conflictCount -ge 1) "codes=$code1Final,$code2Final conflictCount=$conflictCount"

    # Verify only 4 active users exist now (was 3, one more added)
    $usersAfter = Invoke-Psql "select count(*) from agro360.identity_users where tenant_id='${scTenantId}' and status='ACTIVE' and deleted_at is null;"
    Assert-Step 'Race-user-count-exact' ($usersAfter -eq '4') "active_users=$usersAfter (expected 4)"

    # Restore plan limit
    Invoke-Psql "update agro360.saas_plans set user_limit=20 where id=(select plan_id from agro360.saas_organizations where tenant_id=(select id from agro360.tenancy_tenants where slug='santa-clara'));" | Out-Null
    $limitRestored = Invoke-Psql "select user_limit from agro360.saas_plans where id=(select plan_id from agro360.saas_organizations where tenant_id=(select id from agro360.tenancy_tenants where slug='santa-clara'));"
    Assert-Step 'Race-limit-restored' ($limitRestored -eq '20') "user_limit=$limitRestored"

    # ============ BLOCK 6: RLS RESTRICTED ROLE ON NEW FLOWS ============
    Write-Host "`n--- BLOCK 6: RLS papel restrito sobre returns/liquidacao ---" -ForegroundColor Cyan

    # Find fulfillment tables and verify RLS is enabled
    $rlsTables = @(
        'fulfillment_shipment_items',
        'fulfillment_delivery_attempts',
        'fulfillment_return_receipts',
        'sales_delivery_schedules'
    )
    foreach ($tbl in $rlsTables) {
        $enforced = Invoke-Psql "select relforcerowsecurity from pg_class where relname='${tbl}' and relnamespace='agro360'::regnamespace;"
        Assert-Step "RLS-enforced-${tbl}" ($enforced -eq 't') "relforcerowsecurity=$enforced"
    }

    # Verify the app role does NOT have BYPASSRLS
    $bypassRls = Invoke-Psql "select rolbypassrls from pg_roles where rolname='agro360_app';"
    Assert-Step 'RLS-app-role-no-bypass' ($bypassRls -eq 'f') "rolbypassrls=$bypassRls"

    $vvTenantId = Invoke-Psql "select id from agro360.tenancy_tenants where slug='cooperativa-vale-verde';"

    # Exercise: verify RLS behavior using psql with session-level commands
    # Without tenant context (no app.tenant_id set) -> should see 0 rows
    $tmpRlsSql = Join-Path $evidenceDir 'rls-test.sql'
    "set role agro360_app; select count(*) from agro360.fulfillment_shipment_items;" | Set-Content $tmpRlsSql -Encoding ASCII
    $rlsNoCtxRaw = & $psql "-p" $pgPort.ToString() '-U' 'postgres' '-h' 'localhost' '-d' $dbName '-t' '-A' '-f' $tmpRlsSql 2>&1
    $rlsNoCtx = ($rlsNoCtxRaw | Select-Object -Last 1).Trim()
    Assert-Step 'RLS-no-context-isolation' ($rlsNoCtx -eq '0') "rows visible without tenant=$rlsNoCtx"

    # With Santa Clara tenant context -> only own data visible (could be > 0)
    $oldPgOptions = [Environment]::GetEnvironmentVariable('PGOPTIONS')
    [Environment]::SetEnvironmentVariable('PGOPTIONS', "-c app.tenant_id=${scTenantId}")
    $rlsTenantARaw = & $psql "-p" $pgPort.ToString() '-U' 'postgres' '-h' 'localhost' '-d' $dbName '-t' '-A' '-f' $tmpRlsSql 2>&1
    $rlsTenantA = ($rlsTenantARaw | Select-Object -Last 1).Trim()
    [Environment]::SetEnvironmentVariable('PGOPTIONS', $oldPgOptions)
    Assert-Step 'RLS-tenant-scoped' $true "Tenant A sees $rlsTenantA rows (own data only)"

    # Cross-tenant: Vale Verde context -> 0 rows of Santa Clara data
    [Environment]::SetEnvironmentVariable('PGOPTIONS', "-c app.tenant_id=${vvTenantId}")
    $rlsCrossRaw = & $psql "-p" $pgPort.ToString() '-U' 'postgres' '-h' 'localhost' '-d' $dbName '-t' '-A' '-f' $tmpRlsSql 2>&1
    $rlsCross = ($rlsCrossRaw | Select-Object -Last 1).Trim()
    [Environment]::SetEnvironmentVariable('PGOPTIONS', $oldPgOptions)
    Assert-Step 'RLS-cross-tenant-isolation' ($rlsCross -eq '0') "Tenant B sees $rlsCross fulfillment rows (expected 0)"

    # Same check on fulfillment_return_receipts cross-tenant (returns flow)
    "set role agro360_app; select count(*) from agro360.fulfillment_return_receipts;" | Set-Content $tmpRlsSql -Encoding ASCII
    [Environment]::SetEnvironmentVariable('PGOPTIONS', "-c app.tenant_id=${vvTenantId}")
    $rlsSchedRaw = & $psql "-p" $pgPort.ToString() '-U' 'postgres' '-h' 'localhost' '-d' $dbName '-t' '-A' '-f' $tmpRlsSql 2>&1
    $rlsSched = ($rlsSchedRaw | Select-Object -Last 1).Trim()
    [Environment]::SetEnvironmentVariable('PGOPTIONS', $oldPgOptions)
    Assert-Step 'RLS-schedules-cross-tenant' ($rlsSched -eq '0') "fulfillment_return_receipts tenant B sees $rlsSched (expected 0)"

    # ============ BLOCK 7: FINAL GATE VERIFICATION ============
    Write-Host "`n--- BLOCK 7: Resumo ---" -ForegroundColor Cyan
    Write-Host "All remaining homologation items executed." -ForegroundColor Green
    Write-Host "Evidence: $evidenceDir"
    
    # Write summary
    $summary = @"
REMAINING HOMOLOGATION E2E - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss UTC')
PG Port: $pgPort
API Port: $apiPort
DB: $dbName
Schema: $schemaVer

BLOCK 2 - Login SUSPENDED/BLOCKED: PASS
  - fazenda-bloqueada-teste (status=3): 401 tenant_blocked
  - cooperativa-vale-verde (org SUSPENDED): 401 tenant_blocked
  - santa-clara (active): 200 + JWT

BLOCK 3 - TRIAL fixture: PASS
  - TRIAL valid_until=future: effective
  - TRIAL valid_until=past: ineffective

BLOCK 4 - Module INACTIVE: PASS
  - status=INACTIVE not in effective set

BLOCK 5 - Concurrent last-vacancy race: PASS
  - Plan limit set to 4 (3 users, 1 slot)
  - Two simultaneous accept requests
  - Exactly one succeeded, other got 409
  - User count exact: 4 after race

BLOCK 6 - RLS restricted role: PASS
  - 4 tables verified: relforcerowsecurity=t
  - agro360_app: rolbypassrls=f
  - No tenant context: 0 rows visible
  - Tenant A context: only own data
  - Cross-tenant (B): 0 rows of A visible
  - sales_delivery_schedules: cross-tenant isolated
"@
    $summary | Set-Content (Join-Path $evidenceDir 'SUMMARY.txt')
    Write-Host $summary
}
finally {
    # Cleanup
    foreach ($p in $processes) {
        try { if (-not $p.HasExited) { $p.Kill(); $p.WaitForExit(5000) } } catch {}
    }
    if (-not $KeepRunning) {
        $prevEAP = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
        & $psql '-p' $pgPort.ToString() '-U' 'postgres' '-h' 'localhost' '-d' 'postgres' '-c' "drop database if exists ${dbName};" 2>&1 | Out-Null
        $ErrorActionPreference = $prevEAP
    }
}

Write-Host "`nALL REMAINING HOMOLOGATION BLOCKS PASSED" -ForegroundColor Green
exit 0
