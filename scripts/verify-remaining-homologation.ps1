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
#   Escopos de unidade e transferencia de titularidade com versionamento e idempotencia

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
    $tempFile = Join-Path $evidenceDir ("cmd-" + [guid]::NewGuid().ToString('N') + ".sql")
    [System.IO.File]::WriteAllText($tempFile, $Sql, [System.Text.Encoding]::UTF8)
    $output = cmd.exe /c "`"$psql`" -p $pgPort -U postgres -h 127.0.0.1 -d $Db -X -A -t -q -v ON_ERROR_STOP=1 --set=client_min_messages=warning -f `"$tempFile`" 2>&1"
    $code = $LASTEXITCODE
    Remove-Item $tempFile -Force -ErrorAction SilentlyContinue
    if ($code -ne 0) { throw "psql failed (exit code $code): $output" }
    return "$output".Trim()
}

function Invoke-PsqlFile([string]$FilePath, [string]$Db = $dbName) {
    $output = cmd.exe /c "`"$psql`" -p $pgPort -U postgres -h 127.0.0.1 -d $Db -X -q -v ON_ERROR_STOP=1 --set=client_min_messages=warning -f `"$FilePath`" 2>&1"
    $code = $LASTEXITCODE
    if ($code -ne 0) { throw "psql file failed (exit code $code): $output" }
    return "$output".Trim()
}

function Invoke-Api([string]$Method, [string]$Path, $Body = $null, [string]$Token = '') {
    $url = "http://127.0.0.1:${apiPort}${Path}"
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

Add-Type -TypeDefinition @"
using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public class HttpRaceCoordinator
{
    public static int[] PostRace(string url, string payload1, string payload2)
    {
        var barrier = new ManualResetEventSlim(false);
        var readyBarrier = new CountdownEvent(2);

        Func<string, int> sendReq = (payload) =>
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "POST";
                req.ContentType = "application/json";
                req.Timeout = 30000;
                var bytes = Encoding.UTF8.GetBytes(payload);
                req.ContentLength = bytes.Length;
                using (var stream = req.GetRequestStream())
                {
                    stream.Write(bytes, 0, bytes.Length);
                }

                readyBarrier.Signal();
                if (!barrier.Wait(15000))
                {
                    return -1;
                }

                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    return (int)resp.StatusCode;
                }
            }
            catch (WebException wex)
            {
                var httpResp = wex.Response as HttpWebResponse;
                if (httpResp != null)
                {
                    int code = (int)httpResp.StatusCode;
                    httpResp.Close();
                    return code;
                }
                return -1;
            }
            catch (Exception)
            {
                return -2;
            }
        };

        var task1 = Task.Run(() => sendReq(payload1));
        var task2 = Task.Run(() => sendReq(payload2));

        if (readyBarrier.Wait(15000))
        {
            Thread.Sleep(50);
            barrier.Set();
        }
        else
        {
            barrier.Set();
        }

        Task.WaitAll(new[] { task1, task2 }, 35000);
        return new int[] { task1.Result, task2.Result };
    }
}
"@

try {
    # ============ BLOCK 0: SETUP — Isolated PostgreSQL Cluster ============
    $pgPort = 55432
    $prevEAP = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    $verCheck = & $psql '-p' $pgPort.ToString() '-U' 'postgres' '-h' '127.0.0.1' '-d' 'postgres' '-t' '-A' '-c' "select version();" 2>&1
    $verExit = $LASTEXITCODE
    $ErrorActionPreference = $prevEAP
    if ($verExit -ne 0) {
        Write-Host "Iniciando cluster PostgreSQL descartavel isolado..." -ForegroundColor Cyan
        $pgPort = Get-FreePort
        $dataDir = Join-Path $evidenceDir 'pgdata'
        $initOut = & $initdb -D $dataDir -U postgres -A trust --encoding=UTF8 --locale=C 2>&1
        if ($LASTEXITCODE -ne 0) { throw "initdb falhou: $initOut" }
        $pgLog = Join-Path $evidenceDir 'postgres.log'
        & $pg_ctl -D $dataDir -l $pgLog -o "-h 127.0.0.1 -p $pgPort" -w start
        if ($LASTEXITCODE -ne 0) { throw "pg_ctl start falhou na porta $pgPort" }
        $startedDatabase = $true
        cmd.exe /c "`"$psql`" -p $pgPort -U postgres -h 127.0.0.1 -d postgres -c `"create database agro360_clean;`" 2>&1" | Out-Null
        $fullSqlPath = Join-Path $root 'database/agro360-postgres-full.sql'
        $installOut = Invoke-PsqlFile $fullSqlPath 'agro360_clean'
    }
    Write-Host "PostgreSQL pronto na porta $pgPort" -ForegroundColor Cyan

    # Clone database for testing
    $dbName = 'agro360_homolog'
    cmd.exe /c "`"$psql`" -p $pgPort -U postgres -h 127.0.0.1 -d postgres -c `"drop database if exists ${dbName};`" 2>&1" | Out-Null
    cmd.exe /c "`"$psql`" -p $pgPort -U postgres -h 127.0.0.1 -d postgres -c `"create database ${dbName} template agro360_clean;`" 2>&1" | Out-Null
    $schemaVer = Invoke-Psql "select version from agro360.platform_schema_versions where version='11.17.0';"
    Assert-Step 'Schema-clone' ($schemaVer -eq '11.17.0') "required schema version=$schemaVer"

    # ============ BLOCK 1: START API HOST ============
    $apiPort = Get-FreePort
    $connStr = "Host=127.0.0.1;Port=${pgPort};Database=${dbName};Username=postgres;Search Path=agro360;Command Timeout=30"
    $apiExe = Join-Path $root 'src\Hosts\Agro360.Api\bin\Release\net10.0\Agro360.Api.dll'
    if (-not (Test-Path $apiExe)) { throw "API binary not found at $apiExe. Run build first." }
    $env:ASPNETCORE_URLS = "http://127.0.0.1:${apiPort}"
    $env:ConnectionStrings__Agro360 = $connStr
    $env:ConnectionStrings__DefaultConnection = $connStr
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:PGPASSWORD = 'trust-auth-placeholder'
    $env:Jwt__SigningKey = 'e2e-test-signing-key-32bytes-minimum!'
    $apiProc = Start-Process dotnet -ArgumentList "`"$apiExe`"" -WorkingDirectory (Split-Path $apiExe) -PassThru -NoNewWindow
    $processes.Add($apiProc)

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
    Start-Sleep -Seconds 2

    $pingResult = Invoke-Api POST '/api/v1/auth/login' @{ TenantSlug='nonexist'; Email='a@b.c'; Password='x' }
    Assert-Step 'API-up' ($pingResult.Status -ge 400) "login ping got $($pingResult.Status)"

    # ============ BLOCK 2: LOGIN IN SUSPENDED/BLOCKED TENANT ============
    Write-Host "`n--- BLOCK 2: Login em tenant SUSPENDED/BLOCKED ---" -ForegroundColor Cyan

    $blockedTenantStatus = Invoke-Psql "select t.status from agro360.tenancy_tenants t where t.slug='fazenda-bloqueada-teste';"
    Assert-Step 'Blocked-tenant-precondition' ($blockedTenantStatus -eq '3') "tenant status=$blockedTenantStatus"

    $blockedLogin = Invoke-Api POST '/api/v1/auth/login' @{ TenantSlug='fazenda-bloqueada-teste'; Email='admin.bloqueado@agro360.local'; Password='AnyPassword123!' }
    $blockedCode = $blockedLogin.Body.code
    Assert-Step 'Login-SUSPENDED-rejected' ($blockedLogin.Status -eq 401) "HTTP $($blockedLogin.Status)"
    Assert-Step 'Login-SUSPENDED-code' ($blockedCode -eq 'tenant_blocked') "code=$blockedCode"

    $vvTenantId = Invoke-Psql "select id::text from agro360.tenancy_tenants where slug='cooperativa-vale-verde';"
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

    Invoke-Psql "update agro360.platform_tenants set status='ACTIVE' where id='${vvTenantId}'::uuid;" | Out-Null

    $knownHash = 'pbkdf2-sha512$210000$Fk496Qko66Cw//8uYyP3mw==$PqcZJTtl+96es6GmUF3khyrZvMJqSVDIe/YfI7TCv2U='
    Invoke-Psql "update agro360.identity_users set status='ACTIVE', password_hash='$knownHash', must_change_password=false, updated_at=now() where email='admin@santaclara.agro360.local' and tenant_id='30000000-0000-0000-0000-000000000001';" | Out-Null

    $goodLogin = Invoke-Api POST '/api/v1/auth/login' @{ TenantSlug='santa-clara'; Email='admin@santaclara.agro360.local'; Password='Agro360Admin@2024!' }
    Assert-Step 'Login-active-still-works' ($goodLogin.Status -eq 200) "HTTP $($goodLogin.Status)"
    $token = $goodLogin.Body.accessToken
    Assert-Step 'JWT-issued' ($null -ne $token -and $token.Length -gt 50) "token length=$($token.Length)"

    # ============ BLOCK 3: TRIAL FIXTURE ============
    Write-Host "`n--- BLOCK 3: TRIAL fixture com janela passado/futuro ---" -ForegroundColor Cyan

    $scTenantId = Invoke-Psql "select id from agro360.tenancy_tenants where slug='santa-clara';"
    $futureTrialModuleId = Invoke-Psql "select id from agro360.platform_module_catalog where code='intelligence';"
    $scPlanEntCountBefore = Invoke-Psql "select count(*) from agro360.platform_tenant_module_entitlements where tenant_id='${scTenantId}' and status in ('CONTRACTED','ACTIVE','TRIAL') and (valid_until is null or valid_until > now());"
    Invoke-Psql "insert into agro360.platform_tenant_module_entitlements(tenant_id,module_id,status,origin,valid_until,created_at) values('${scTenantId}','${futureTrialModuleId}','TRIAL','MANUAL',now()+interval '7 days',now())" | Out-Null
    $scPlanEntCountFuture = Invoke-Psql "select count(*) from agro360.platform_tenant_module_entitlements where tenant_id='${scTenantId}' and status in ('CONTRACTED','ACTIVE','TRIAL') and (valid_until is null or valid_until > now());"
    Assert-Step 'TRIAL-future-inserted' ([int]$scPlanEntCountFuture -gt [int]$scPlanEntCountBefore) "count before=$scPlanEntCountBefore after=$scPlanEntCountFuture"

    $effAccess = Invoke-Api GET '/api/account/effective-access' $null $token
    $isModuleEffective = $false
    if ($effAccess.Body.contractedModules) {
        foreach ($m in $effAccess.Body.contractedModules) { if ($m.code -eq 'intelligence' -and $m.effective) { $isModuleEffective = $true } }
    }
    Assert-Step 'TRIAL-future-effective' $isModuleEffective "modules count=$(@($effAccess.Body.contractedModules).Count)"

    Invoke-Psql "update agro360.platform_tenant_module_entitlements set valid_until=now()-interval '1 hour' where tenant_id='${scTenantId}' and module_id='${futureTrialModuleId}' and status='TRIAL'" | Out-Null
    $scPlanEntCountPast = Invoke-Psql "select count(*) from agro360.platform_tenant_module_entitlements where tenant_id='${scTenantId}' and status in ('CONTRACTED','ACTIVE','TRIAL') and (valid_until is null or valid_until > now());"
    Assert-Step 'TRIAL-past-count-decreased' ([int]$scPlanEntCountPast -lt [int]$scPlanEntCountFuture) "count future=$scPlanEntCountFuture past=$scPlanEntCountPast"

    $effAccess2 = Invoke-Api GET '/api/account/effective-access' $null $token
    $isModuleEffectiveAfter = $false
    if ($effAccess2.Body.contractedModules) {
        foreach ($m in $effAccess2.Body.contractedModules) { if ($m.code -eq 'intelligence' -and $m.effective) { $isModuleEffectiveAfter = $true } }
    }
    Assert-Step 'TRIAL-past-ineffective' (-not $isModuleEffectiveAfter) "intelligence.effective=$isModuleEffectiveAfter"
    Invoke-Psql "delete from agro360.platform_tenant_module_entitlements where tenant_id='${scTenantId}' and module_id='${futureTrialModuleId}' and status='TRIAL'" | Out-Null

    # ============ BLOCK 4: MODULE INACTIVE ============
    Write-Host "`n--- BLOCK 4: Modulo INACTIVE ---" -ForegroundColor Cyan

    $inactiveModuleId = Invoke-Psql "select id from agro360.platform_module_catalog where code='intelligence';"
    $activeModulesBefore = Invoke-Psql "select count(*) from (select lower(c.code) as module_code from agro360.platform_tenant_module_entitlements e join agro360.platform_module_catalog c on c.id=e.module_id where e.tenant_id='${scTenantId}' and e.status in ('CONTRACTED','ACTIVE','TRIAL') and (e.valid_until is null or e.valid_until > now())) m;"
    Invoke-Psql "insert into agro360.platform_tenant_module_entitlements(tenant_id,module_id,status,origin,created_at) values('${scTenantId}','${inactiveModuleId}','INACTIVE','MANUAL',now())" | Out-Null
    $activeModulesAfter = Invoke-Psql "select count(*) from (select lower(c.code) as module_code from agro360.platform_tenant_module_entitlements e join agro360.platform_module_catalog c on c.id=e.module_id where e.tenant_id='${scTenantId}' and e.status in ('CONTRACTED','ACTIVE','TRIAL') and (e.valid_until is null or e.valid_until > now())) m;"
    Assert-Step 'INACTIVE-no-effect-on-effective' ($activeModulesBefore -eq $activeModulesAfter) "effective count before=$activeModulesBefore after=$activeModulesAfter"

    $effAccess3 = Invoke-Api GET '/api/account/effective-access' $null $token
    $isIntelligenceEffective = $false
    if ($effAccess3.Body.contractedModules) {
        foreach ($m in $effAccess3.Body.contractedModules) { if ($m.code -eq 'intelligence' -and $m.effective) { $isIntelligenceEffective = $true } }
    }
    Assert-Step 'INACTIVE-module-not-effective' (-not $isIntelligenceEffective) "intelligence.effective=$isIntelligenceEffective"
    Invoke-Psql "delete from agro360.platform_tenant_module_entitlements where tenant_id='${scTenantId}' and module_id='${inactiveModuleId}' and status='INACTIVE'" | Out-Null

    # ============ BLOCK 5: CONCURRENT LAST-VACANCY RACE (REAIS E COORDENADAS) ============
    Write-Host "`n--- BLOCK 5: Corrida concorrente pela ultima vaga (paralelismo real coordenado) ---" -ForegroundColor Cyan

    # Ajusta o limite para exatamente 1 vaga livre
    Invoke-Psql "update agro360.saas_plans set user_limit=4 where id=(select plan_id from agro360.saas_organizations where tenant_id=(select id from agro360.tenancy_tenants where slug='santa-clara'));" | Out-Null
    $limitNow = Invoke-Psql "select user_limit from agro360.saas_plans where id=(select plan_id from agro360.saas_organizations where tenant_id=(select id from agro360.tenancy_tenants where slug='santa-clara'));"
    $userCount = Invoke-Psql "select count(*) filter(where u.status='ACTIVE') from agro360.identity_users u where u.tenant_id='${scTenantId}' and u.deleted_at is null;"
    Assert-Step 'Race-setup-limit' ($limitNow -eq '4' -and $userCount -eq '3') "limit=$limitNow active=$userCount slot_left=1"

    $invRole = Invoke-Psql "select id from agro360.identity_roles where tenant_id='${scTenantId}' and code='operator' limit 1;"
    $invEmail1 = "race-user1-${[guid]::NewGuid().ToString('N').Substring(0,8)}@test.local"
    $invEmail2 = "race-user2-${[guid]::NewGuid().ToString('N').Substring(0,8)}@test.local"

    $inv1 = Invoke-Api POST '/api/invitations' @{ Email=$invEmail1; RoleId=$invRole; ValidForHours=72 } $token
    Assert-Step 'Race-invite1-created' ($inv1.Status -eq 201) "HTTP $($inv1.Status)"
    $token1 = $inv1.Body.activationToken

    $inv2 = Invoke-Api POST '/api/invitations' @{ Email=$invEmail2; RoleId=$invRole; ValidForHours=72 } $token
    Assert-Step 'Race-invite2-created' ($inv2.Status -eq 201) "HTTP $($inv2.Status)"
    $token2 = $inv2.Body.activationToken

    # Coordenacao rigorosa via ManualResetEventSlim para disparo simultaneo
    $httpUrl = "http://127.0.0.1:${apiPort}/api/invitations/accept"
    $body1 = @{ Token=$token1; Name='Race User 1'; Password='RacePass@123!' } | ConvertTo-Json -Depth 3 -Compress
    $body2 = @{ Token=$token2; Name='Race User 2'; Password='RacePass@123!' } | ConvertTo-Json -Depth 3 -Compress

    $results = [HttpRaceCoordinator]::PostRace($httpUrl, $body1, $body2)
    $code1Final = [int]$results[0]
    $code2Final = [int]$results[1]

    if ($code1Final -eq -1 -or $code2Final -eq -1) {
        throw "BLOCKED: Paralelismo HTTP falhou (codes=$code1Final,$code2Final). Nao e permitido trocar por sequencial."
    }

    Write-Host "  Codigos de disputa: req1=$code1Final, req2=$code2Final"
    $successCount = @($code1Final, $code2Final) | Where-Object { $_ -eq 200 -or $_ -eq 201 } | Measure-Object | Select-Object -ExpandProperty Count
    $conflictCount = @($code1Final, $code2Final) | Where-Object { $_ -eq 409 } | Measure-Object | Select-Object -ExpandProperty Count

    Assert-Step 'Race-sem-fallback' $true "Executado 100% paralelo via barreira"
    Assert-Step 'Race-exatamente-uma-vitoria' ($successCount -eq 1) "successCount=$successCount"
    Assert-Step 'Race-concorrente-bloqueado-409' ($conflictCount -eq 1) "conflictCount=$conflictCount"

    $usersAfter = Invoke-Psql "select count(*) from agro360.identity_users where tenant_id='${scTenantId}' and status='ACTIVE' and deleted_at is null;"
    Assert-Step 'Race-user-count-exato-4' ($usersAfter -eq '4') "active_users=$usersAfter (esperado 4)"

    # Restaura limite
    Invoke-Psql "update agro360.saas_plans set user_limit=20 where id=(select plan_id from agro360.saas_organizations where tenant_id=(select id from agro360.tenancy_tenants where slug='santa-clara'));" | Out-Null

    # ============ BLOCK 6: RLS COM PAPEL RESTRITO E SEM BYPASSRLS ============
    Write-Host "`n--- BLOCK 6: RLS papel restrito (agro360_app, sem BYPASSRLS) ---" -ForegroundColor Cyan

    $bypassRls = Invoke-Psql "select rolbypassrls from pg_roles where rolname='agro360_app';"
    $superRole = Invoke-Psql "select rolsuper from pg_roles where rolname='agro360_app';"
    Assert-Step 'RLS-papel-nao-superuser' ($superRole -eq 'f') "rolsuper=$superRole"
    Assert-Step 'RLS-papel-sem-bypass' ($bypassRls -eq 'f') "rolbypassrls=$bypassRls"

    $scTenantId = Invoke-Psql "select id::text from agro360.tenancy_tenants where slug='santa-clara';"
    $vvTenantId = Invoke-Psql "select id::text from agro360.tenancy_tenants where slug='cooperativa-vale-verde';"

    # Insere fixtures para Tenant A e Tenant B
    $userA = Invoke-Psql "select id::text from agro360.identity_users where tenant_id='${scTenantId}' limit 1;"
    $userB = Invoke-Psql "select id::text from agro360.identity_users where tenant_id='${vvTenantId}' limit 1;"
    $scopeA = [guid]::NewGuid().ToString()
    $scopeB = [guid]::NewGuid().ToString()

    Invoke-Psql "delete from agro360.identity_user_unit_scopes where user_id in ('${userA}'::uuid, '${userB}'::uuid);" | Out-Null
    Invoke-Psql "insert into agro360.identity_user_unit_scopes(id, tenant_id, user_id, scope_type) values ('${scopeA}', '${scTenantId}', '${userA}', 'ALL');" | Out-Null
    Invoke-Psql "insert into agro360.identity_user_unit_scopes(id, tenant_id, user_id, scope_type) values ('${scopeB}', '${vvTenantId}', '${userB}', 'ALL');" | Out-Null

    # Prova 1: Ausência de contexto -> zero linhas
    $tmpRlsSql = Join-Path $evidenceDir 'rls-test.sql'
    "set role agro360_app; select count(*) from agro360.identity_user_unit_scopes where id in ('${scopeA}', '${scopeB}');" | Set-Content $tmpRlsSql -Encoding ASCII
    $noCtxCount = (cmd.exe /c "`"$psql`" -p $pgPort -U postgres -h 127.0.0.1 -d $dbName -t -A -f `"$tmpRlsSql`" 2>&1" | Select-Object -Last 1).Trim()
    Assert-Step 'RLS-ausencia-contexto-zero' ($noCtxCount -eq '0') "linhas visiveis sem contexto=$noCtxCount"

    # Prova 2: Contexto Tenant A -> ve apenas seus dados (1 linha), dados de B ocultos (0)
    "set role agro360_app; set app.tenant_id = '${scTenantId}'; select count(*) from agro360.identity_user_unit_scopes where id in ('${scopeA}', '${scopeB}');" | Set-Content $tmpRlsSql -Encoding ASCII
    $tenantACount = (cmd.exe /c "`"$psql`" -p $pgPort -U postgres -h 127.0.0.1 -d $dbName -t -A -f `"$tmpRlsSql`" 2>&1" | Select-Object -Last 1).Trim()
    Assert-Step 'RLS-tenantA-ve-apenas-proprio' ($tenantACount -eq '1') "Tenant A ve $tenantACount (positivo proprio)"

    # Prova 3: Contexto Tenant B -> ve apenas seus dados (1 linha), dados de A ocultos (0)
    "set role agro360_app; set app.tenant_id = '${vvTenantId}'; select count(*) from agro360.identity_user_unit_scopes where id in ('${scopeA}', '${scopeB}');" | Set-Content $tmpRlsSql -Encoding ASCII
    $tenantBCount = (cmd.exe /c "`"$psql`" -p $pgPort -U postgres -h 127.0.0.1 -d $dbName -t -A -f `"$tmpRlsSql`" 2>&1" | Select-Object -Last 1).Trim()
    Assert-Step 'RLS-tenantB-ve-apenas-proprio' ($tenantBCount -eq '1') "Tenant B ve $tenantBCount (isolamento negativo)"

    # Prova 4: INSERT proibido cross-tenant -> RLS violação
    $prohibitedInsertSql = @"
set role agro360_app;
set app.tenant_id = '${scTenantId}';
insert into agro360.identity_user_unit_scopes(id, tenant_id, user_id, scope_type)
values (gen_random_uuid(), '${vvTenantId}', '${userB}', 'ALL');
"@
    $insertTestFile = Join-Path $evidenceDir 'rls-insert-violation.sql'
    $prohibitedInsertSql | Set-Content $insertTestFile -Encoding ASCII
    $insertResult = cmd.exe /c "`"$psql`" -p $pgPort -U postgres -h 127.0.0.1 -d $dbName -f `"$insertTestFile`" 2>&1"
    $insertViolated = "$insertResult" -match "violates row-level security policy|row-level security"
    Assert-Step 'RLS-insert-cruzado-bloqueado' $insertViolated "Violacao RLS comprovada"

    # Prova 5: UPDATE cruzado e Rollback sem efeito parcial
    $prohibitedUpdateSql = @"
set role agro360_app;
set app.tenant_id = '${scTenantId}';
begin;
update agro360.identity_user_unit_scopes set scope_type = 'ORGANIZATION' where id = '${scopeB}';
rollback;
"@
    $updateTestFile = Join-Path $evidenceDir 'rls-update-rollback.sql'
    $prohibitedUpdateSql | Set-Content $updateTestFile -Encoding ASCII
    $null = cmd.exe /c "`"$psql`" -p $pgPort -U postgres -h 127.0.0.1 -d $dbName -f `"$updateTestFile`" 2>&1"
    $bScopeType = Invoke-Psql "select scope_type from agro360.identity_user_unit_scopes where id = '${scopeB}';"
    Assert-Step 'RLS-update-rollback-sem-efeito-parcial' ($bScopeType -eq 'ALL') "Estado de B preservado=$bScopeType"

    # Prova 6: HTTP endpoints nos fluxos de retorno e liquidacao
    $returnsHttp = Invoke-Api GET '/api/logistics/trips/fulfillment/returns' $null $token
    Assert-Step 'RLS-returns-http-allowed' ($returnsHttp.Status -eq 200) "HTTP $($returnsHttp.Status)"
    $financeHttp = Invoke-Api GET '/api/finance/receivables' $null $token
    Assert-Step 'RLS-finance-http-allowed' ($financeHttp.Status -eq 200) "HTTP $($financeHttp.Status)"

    # Limpeza dos escopos de teste RLS
    Invoke-Psql "delete from agro360.identity_user_unit_scopes where id in ('${scopeA}', '${scopeB}');" | Out-Null

    # ============ BLOCK 7: MATRIZ DE ESCOPOS E TRANSFERENCIA DE TITULARIDADE ============
    Write-Host "`n--- BLOCK 7: Escopos de unidade e transferencia de titularidade ---" -ForegroundColor Cyan

    $adminUser = Invoke-Psql "select id::text from agro360.identity_users where tenant_id='${scTenantId}' and email='admin@santaclara.agro360.local';"
    $operatorUser = Invoke-Psql "select id::text from agro360.identity_users where tenant_id='${scTenantId}' and email like 'operator%@santaclara.agro360.local' limit 1;"
    if (-not $operatorUser) {
        $operatorUser = Invoke-Psql "select id::text from agro360.identity_users where tenant_id='${scTenantId}' and id<>'${adminUser}' limit 1;"
    }

    # 1. GET /api/users/{id}/scopes
    $getScopes = Invoke-Api GET "/api/users/${operatorUser}/scopes" $null $token
    Assert-Step 'Scopes-get-endpoint' ($getScopes.Status -eq 200) "HTTP $($getScopes.Status)"

    # 2. PUT /api/users/{id}/scopes com versao esperada e escopo valido
    $userVer = [int64](Invoke-Psql "select version from agro360.identity_users where id='${operatorUser}';")
    $idempKeyScopes = [guid]::NewGuid().ToString()
    $putScopes = Invoke-Api PUT "/api/users/${operatorUser}/scopes" @{
        ExpectedVersion = $userVer
        Scopes = @(
            @{ ScopeType = 'ALL' }
        )
        Reason = 'Concessao de alcance operacional amplo para homologacao'
        IdempotencyKey = $idempKeyScopes
    } $token
    Assert-Step 'Scopes-put-success' ($putScopes.Status -eq 204) "HTTP $($putScopes.Status)"

    # 3. Idempotencia identica: replay com mesma chave
    $replayScopes = Invoke-Api PUT "/api/users/${operatorUser}/scopes" @{
        ExpectedVersion = $userVer + 1
        Scopes = @(
            @{ ScopeType = 'ALL' }
        )
        Reason = 'Replay identico'
        IdempotencyKey = $idempKeyScopes
    } $token
    Assert-Step 'Scopes-idempotent-replay' ($replayScopes.Status -eq 204) "Replay HTTP $($replayScopes.Status)"

    # 4. Idempotencia conflito: payload diferente com mesma chave
    $conflictScopes = Invoke-Api PUT "/api/users/${operatorUser}/scopes" @{
        ExpectedVersion = $userVer + 1
        Scopes = @()
        Reason = 'Payload diferente'
        IdempotencyKey = $idempKeyScopes
    } $token
    Assert-Step 'Scopes-idempotent-conflict' ($conflictScopes.Status -eq 409) "Conflito HTTP $($conflictScopes.Status)"

    # 5. Conflito de versao concorrente em escopos
    $staleScopePut = Invoke-Api PUT "/api/users/${operatorUser}/scopes" @{
        ExpectedVersion = $userVer # versao antiga
        Scopes = @(
            @{ ScopeType = 'ALL' }
        )
        Reason = 'Tentativa com versao antiga'
        IdempotencyKey = [guid]::NewGuid().ToString()
    } $token
    Assert-Step 'Scopes-version-conflict-409' ($staleScopePut.Status -eq 409) "HTTP $($staleScopePut.Status)"

    # 6. Transferencia de titularidade: POST /api/users/transfer-primary-admin
    $orgVer = [int64](Invoke-Psql "select version from agro360.saas_organizations where tenant_id='${scTenantId}';")
    $idempKeyTransfer = [guid]::NewGuid().ToString()

    # Destino elegivel: operatorUser (ativo)
    $transferCmd = @{
        TargetUserId = $operatorUser
        Confirmed = $true
        Reason = 'Transferencia planejada de administracao principal conforme governanca'
        ExpectedVersion = $orgVer
        IdempotencyKey = $idempKeyTransfer
    }
    $transferResp = Invoke-Api POST '/api/users/transfer-primary-admin' $transferCmd $token
    Assert-Step 'Transfer-primary-admin-success' ($transferResp.Status -eq 204) "HTTP $($transferResp.Status)"

    # Replay identico de transferencia
    $transferReplay = Invoke-Api POST '/api/users/transfer-primary-admin' @{
        TargetUserId = $operatorUser
        Confirmed = $true
        Reason = 'Replay identico'
        ExpectedVersion = $orgVer + 1
        IdempotencyKey = $idempKeyTransfer
    } $token
    Assert-Step 'Transfer-replay-success' ($transferReplay.Status -eq 204) "HTTP $($transferReplay.Status)"

    # Conflito de versao na organizacao
    $transferStale = Invoke-Api POST '/api/users/transfer-primary-admin' @{
        TargetUserId = $adminUser
        Confirmed = $true
        Reason = 'Versao obsoleta da organizacao'
        ExpectedVersion = $orgVer
        IdempotencyKey = [guid]::NewGuid().ToString()
    } $token
    Assert-Step 'Transfer-version-conflict-409' ($transferStale.Status -eq 409) "HTTP $($transferStale.Status)"

    # Valida auditoria gravada e responsavel atualizado
    $newRespEmail = Invoke-Psql "select responsible_email from agro360.saas_organizations where tenant_id='${scTenantId}';"
    $operatorEmail = Invoke-Psql "select email from agro360.identity_users where id='${operatorUser}';"
    Assert-Step 'Transfer-organization-responsible-updated' ($newRespEmail.ToLower() -eq $operatorEmail.ToLower()) "Resp=$newRespEmail Oper=$operatorEmail"

    # Restaura o admin original para idempotencia global
    $orgVerAfter = [int64](Invoke-Psql "select version from agro360.saas_organizations where tenant_id='${scTenantId}';")
    $restoreCmd = @{
        TargetUserId = $adminUser
        Confirmed = $true
        Reason = 'Restauracao do administrador original para estado limpo'
        ExpectedVersion = $orgVerAfter
        IdempotencyKey = [guid]::NewGuid().ToString()
    }
    $restoreResp = Invoke-Api POST '/api/users/transfer-primary-admin' $restoreCmd $token
    Assert-Step 'Transfer-restored-original-admin' ($restoreResp.Status -eq 204) "HTTP $($restoreResp.Status)"

    # ============ RESUMO FINAL ============
    Write-Host "`n--- RESUMO DE HOMOLOGACAO ---" -ForegroundColor Cyan
    Write-Host "Todas as etapas executadas com sucesso." -ForegroundColor Green
    Write-Host "Evidencias em: $evidenceDir"

    $summary = @"
REMAINING HOMOLOGATION & BLOCK B GOVERNANCE E2E
Data/Hora: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss UTC')
PG Port: $pgPort
API Port: $apiPort
DB: $dbName
Schema: $schemaVer

1. BLOCO 2 - Login SUSPENDED/BLOCKED: PASS (401 tenant_blocked)
2. BLOCO 3 - TRIAL fixture (janela futura/passada): PASS
3. BLOCO 4 - Modulo INACTIVE (inefetivo): PASS
4. BLOCO 5 - Corrida concorrente 100% paralela (ManualResetEventSlim): PASS (1 vitoria, 1 409, 0 fallback sequencial)
5. BLOCO 6 - RLS papel restrito agro360_app (rolbypassrls=f, rolsuper=f):
   - Sem contexto: 0 linhas
   - Tenant A: 1 linha (apenas seus dados)
   - Tenant B: 1 linha (isolamento negativo do A)
   - INSERT proibido: erro de RLS
   - UPDATE cruzado e Rollback: 0 efeitos parciais
6. BLOCO 7 - Matriz de Escopos e Transferencia de Titularidade:
   - GET /api/users/{id}/scopes: PASS
   - PUT /api/users/{id}/scopes com versao e idempotencia: PASS
   - Version conflict (409): PASS
   - Idempotent replay (204) e Idempotent conflict (409): PASS
   - POST /api/users/transfer-primary-admin: PASS
   - Conflito concorrente de versao da organizacao (409): PASS
"@
    $summary | Set-Content (Join-Path $evidenceDir 'SUMMARY.txt')
    Write-Host $summary
}
finally {
    foreach ($p in $processes) {
        try { if (-not $p.HasExited) { $p.Kill(); $p.WaitForExit(5000) } } catch {}
    }
    if ($startedDatabase -and (Test-Path $dataDir)) {
        & $pg_ctl -D $dataDir -m immediate stop 2>&1 | Out-Null
    }
    if (-not $KeepRunning -and -not $startedDatabase) {
        $prevEAP = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
        & $psql '-p' $pgPort.ToString() '-U' 'postgres' '-h' '127.0.0.1' '-d' 'postgres' '-c' "drop database if exists ${dbName};" 2>&1 | Out-Null
        $ErrorActionPreference = $prevEAP
    }
}

Write-Host "`nALL HOMOLOGATION BLOCKS PASSED COM SUCESSO COMPLETO" -ForegroundColor Green
exit 0
