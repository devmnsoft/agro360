param(
    [string]$PostgresBin = 'C:\Program Files\PostgreSQL\18\bin',
    [switch]$KeepRunning,
    [switch]$RebuildTemplate
)

# ============================================================================
# BLOCO A - Prova comportamental do core agricola (journey end-to-end)
# Safra -> Operacao -> Colheita -> Lote disponivel -> Resultado gerencial.
#
# Roda em PostgreSQL descartavel (template proprio + clone), com a API real em
# Release. NUNCA toca o banco do usuario. Continui aposto em falha de cenario
# (para revelar todos os problemas por execucao); SETUP duro (infra) aborta.
#
# Cenarios (carta = grupo de requisito):
#   C escopo/tenancy : isolamento entre 2 tenants + estreitamento por fazenda
#                      (X-Farm-ID nao alarga autorizacao; fazenda fora do escopo -> 403).
#   D planejamento    : pares de dependencia, ciclo, ordem invertida (bloqueio de
#                      liberacao), reschedule NAO propaga datas, geracao concorrente
#                      da mesma chave -> UMA unica ordem (dedup + advisory lock).
#   B estados         : maquina de estados da ordem de campo (transicoes, versao
#                      obrigatoria, motivo de pausa/cancelamento, terminais irreversiveis).
#   A materiais       : ledger canonico (1 efeito por comando, replay equivale,
#                      conflito chave-mesma/conteudo-diferente, sem re-decremento,
#                      estorno valido) + UoM explicita + moeda decimal.
#   E colheita        : recebimentos parciais, teto multi-passe de area,
#                      bruto/tara/líquido + divergencia justificada, recibo acima
#                      do saldo recusado, replay de inspecao sem duplicar,
#                      destinacao cria lote + lancamento correto no estoque.
#   F indicadores     : null <> zero, custo realizado da fonte reconhecida,
#                      produtividade respeita cultura/UoM/area/periodo,
#                      fazendas sem autorizacao excluidas do resultado.
#
# Uso: powershell -File verify-agro-journey-e2e.ps1 [-RebuildTemplate] [-KeepRunning]
# ==========================================================================

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$psql = Join-Path $PostgresBin 'psql.exe'
$initdb = Join-Path $PostgresBin 'initdb.exe'
$pg_ctl = Join-Path $PostgresBin 'pg_ctl.exe'
foreach ($tool in @($psql, $initdb, $pg_ctl)) { if (-not (Test-Path $tool)) { throw "BLOCKED: Executavel nao encontrado: $tool" } }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'BLOCKED: dotnet indisponivel.' }

$evidenceDir = Join-Path $root ('artifacts\agro-journey-e2e-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidenceDir | Out-Null
Start-Transcript -Path (Join-Path $evidenceDir 'transcript.log') -Append | Out-Null
$processes = [Collections.Generic.List[Diagnostics.Process]]::new()
$weStartedCluster = $false
$dbName = 'agro360_agro_' + [guid]::NewGuid().ToString('N').Substring(0, 12)
$templateDir = Join-Path $env:TEMP 'opencode\pg-agro-template'
$templateDb = 'agro_journey_template'
$templateMarker = Join-Path $templateDir 'template-ready.txt'
$fullSqlPath = Join-Path $root 'database/agro360-postgres-full.sql'
$apiPort = 0
$knownHash = 'pbkdf2-sha512$210000$Fk496Qko66Cw//8uYyP3mw==$PqcZJTtl+96es6GmUF3khyrZvMJqSVDIe/YfI7TCv2U='
$runTag = (Get-Date).ToString('HHmmss')

# estado global
$tenantId = ''; $valeTenantId = ''
$adminUser = ''; $operatorUser = ''; $valeAdminUser = ''
$adminToken = ''; $operatorToken = ''; $valeToken = ''
$farmA = ''; $farmB = ''; $fieldId = ''
$whId = ''; $matWhId = ''
$grainP = ''; $matP = ''
$seasonId = ''
$valeOrgId = ''; $valeFarmId = ''
$apiProc = $null

$script:pass = 0
$script:fail = 0
$script:blocked = 0
$script:results = [System.Collections.Generic.List[object]]::new()

$env:PGCONNECT_TIMEOUT = '20'

function Get-FreePort {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    try { return $listener.LocalEndpoint.Port } finally { $listener.Stop() }
}

function Require-Setup([string]$Name, [bool]$Ok, [string]$Details = '') {
    if (-not $Ok) { throw "SETUP FAIL ${Name}: ${Details}" }
    Write-Host "SETUP ${Name} $(if ($Details) { '(' + $Details + ')' })" -ForegroundColor Cyan
}

function Assert-Step([string]$Group, [string]$Name, [bool]$Condition, [string]$Details = '') {
    $step = "${Group}:${Name}"
    if ($Condition) {
        $script:pass++
        $script:results.Add([pscustomobject]@{ Scenario = $Group; Step = $Name; Status = 'PASS'; Details = $Details })
        Write-Host "PASS ${step} $(if ($Details) { '(' + $Details + ')' })" -ForegroundColor Green
    } else {
        $script:fail++
        $script:results.Add([pscustomobject]@{ Scenario = $Group; Step = $Name; Status = 'FAIL'; Details = $Details })
        Write-Host "FAIL ${step} -- ${Details}" -ForegroundColor Red
    }
}

function Note-Blocked([string]$Group, [string]$Name, [string]$Details = '') {
    $script:blocked++
    $script:results.Add([pscustomobject]@{ Scenario = $Group; Step = $Name; Status = 'BLOCKED'; Details = $Details })
    Write-Host "BLOCKED ${Group}:${Name} -- ${Details}" -ForegroundColor Yellow
}

function Invoke-Psql([string]$Sql, [string]$Db = $dbName) {
    $tmp = Join-Path $evidenceDir ([guid]::NewGuid().ToString('N'))
    $tempFile = "$tmp.sql"; $outF = "$tmp.out"; $errF = "$tmp.err"
    [System.IO.File]::WriteAllText($tempFile, $Sql, [System.Text.Encoding]::UTF8)
    $proc = Start-Process -FilePath $psql -ArgumentList @('-p',"$pgPort","-U","postgres","-h","127.0.0.1","-d",$Db,'-X','-A','-t','-q','-v','ON_ERROR_STOP=1','--set=client_min_messages=warning','-f',$tempFile) -NoNewWindow -PassThru -Wait -RedirectStandardOutput $outF -RedirectStandardError $errF
    $code = $proc.ExitCode
    $combined = ''
    if (Test-Path $outF) { $combined += (Get-Content $outF -Raw -ErrorAction SilentlyContinue) }
    if ((Test-Path $errF) -and (Get-Item $errF).Length -gt 0) { $combined += "`n" + (Get-Content $errF -Raw -ErrorAction SilentlyContinue) }
    if ($code -ne 0) { throw "psql failed (exit $code): $combined | SQL em $tempFile" }
    Remove-Item $tempFile, $outF, $errF -Force -ErrorAction SilentlyContinue
    return $combined.Trim()
}

function Invoke-Api([string]$Method, [string]$Path, $Body = $null, [string]$Token = '', [string]$FarmId = '', [string]$OrgId = '') {
    $url = "http://127.0.0.1:${apiPort}${Path}"
    $headers = @{ 'Content-Type' = 'application/json' }
    if ($Token) { $headers['Authorization'] = "Bearer $Token" }
    if ($FarmId) { $headers['X-Farm-ID'] = $FarmId }
    if ($OrgId) { $headers['X-Organization-ID'] = $OrgId }
    $params = @{ Method = $Method; Uri = $url; Headers = $headers; UseBasicParsing = $true; TimeoutSec = 60 }
    if ($null -ne $Body) { $json = $Body | ConvertTo-Json -Depth 8 -Compress; $params['Body'] = [Text.Encoding]::UTF8.GetBytes($json) }
    try {
        $resp = Invoke-WebRequest @params
        $content = $resp.Content
        $result = $null
        if ("$($resp.Headers['Content-Type'])" -like '*json*' -and $content -and $content.Length -gt 1) { try { $result = $content | ConvertFrom-Json } catch { $result = $content } }
        return @{ Status = [int]$resp.StatusCode; Body = $result; Raw = $content }
    } catch {
        $resp = $_.Exception.Response
        if ($null -eq $resp) { throw "HTTP ${Method} ${Path}: $($_.Exception.Message)" }
        $content = ''
        if ($_.ErrorDetails -and $_.ErrorDetails.Message) { $content = $_.ErrorDetails.Message }
        else { try { $stream = $resp.GetResponseStream(); $reader = [IO.StreamReader]::new($stream); $content = $reader.ReadToEnd() } catch { $content = '' } }
        $result = if ($content -and $content.Length -gt 2) { try { $content | ConvertFrom-Json } catch { $content } } else { $null }
        return @{ Status = [int]$resp.StatusCode; Body = $result; Raw = $content }
    }
}

function Is-Err([hashtable]$R, [int[]]$Codes, [string]$Code = '') {
    if ($Codes -notcontains $R.Status) { return $false }
    if ($Code -and ("$($R.Raw)" -notlike "*${Code}*")) { return $false }
    return $true
}

function Get-RecVersion([string]$Id) { (Invoke-Psql "select version::text from agro360.agriculture_records where id='${Id}' and tenant_id='${tenantId}' and deleted_at is null;") -replace '\s','' }
function Get-PlanOpVersion([string]$Id) { (Invoke-Psql "select version::text from agro360.agriculture_plan_operations where id='${Id}' and tenant_id='${tenantId}' and deleted_at is null;") -replace '\s','' }
function Get-RecStatus([string]$Id) { Invoke-Psql "select status from agro360.agriculture_records where id='${Id}' and tenant_id='${tenantId}' and deleted_at is null;" }
function BalanceAvail([string]$Wh, [string]$Prod) { $x = Invoke-Psql "select coalesce(available,0)::text from agro360.inventory_stock_balances where tenant_id='${tenantId}' and warehouse_id='${Wh}' and product_id='${Prod}';"; return [decimal](($x -replace '\s','')) }
function New-DirectWorkOrder([string]$Status, [string]$Suffix, [string]$Farm, [string]$Field, [string]$Resp) {
    $id = [guid]::NewGuid().ToString()
    $plannedAt = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    $extra = ''
    if ($Status -eq 'IN_PROGRESS') { $extra = ",`"actualStartedAt`":`"$plannedAt`"" }
    Invoke-Psql @"
insert into agro360.agriculture_records(id,tenant_id,module,status,data,created_by,version)
values('${id}','${tenantId}','work-orders','${Status}',
 '{"number":"OC-${runTag}${Suffix}","name":"WO ${Suffix}","propertyId":"${Farm}","fieldId":"${Field}","responsibleId":"${Resp}","plannedAt":"${plannedAt}","estimatedCost":1000,"unit":"ha","origin":"E2E"${extra}}'::jsonb,
 '${adminUser}',1);
"@ | Out-Null
    return $id
}

function Test-ConcurrentGenerate([string]$OpId, [double]$Area, [string]$Key, [string]$Token) {
    $url = "http://127.0.0.1:${apiPort}/api/agriculture/operations/${OpId}/orders"
    $body = '{"coveredAreaHa":' + [int]$Area + ',"idempotencyKey":"' + $Key + '"}'
    $bf = Join-Path $evidenceDir ("concg_$Key.json"); [IO.File]::WriteAllText($bf, $body, [Text.Encoding]::UTF8)
    $o1 = Join-Path $evidenceDir ("concg_$Key_a.out"); $o2 = Join-Path $evidenceDir ("concg_$Key_b.out")
    # Single-STRING -ArgumentList: Start-Process re-quotes an array and splits space-containing
    # args, which dropped the Bearer token (Authorization header arrived empty -> 401). Passing one
    # pre-quoted string is used verbatim. curl writes its own body via -o (reliable, unlike the
    # flaky -RedirectStandardOutput files). Both Start-Process calls fire back-to-back (genuine
    # concurrency) against the SAME idempotency key -> expect exactly one persisted order.
    $base = '-s -X POST "' + $url + '" -H "Authorization: Bearer ' + $Token + '" -H "Content-Type: application/json" --data-binary "@' + $bf + '"'
    $pa = Start-Process -FilePath 'curl.exe' -ArgumentList ($base + ' -o "' + $o1 + '"') -NoNewWindow -PassThru
    $pb = Start-Process -FilePath 'curl.exe' -ArgumentList ($base + ' -o "' + $o2 + '"') -NoNewWindow -PassThru
    $pa.WaitForExit(); $pb.WaitForExit()
    $ra = Get-Content $o1 -Raw -ErrorAction SilentlyContinue; if ($null -eq $ra) { $ra = '' }
    $rb = Get-Content $o2 -Raw -ErrorAction SilentlyContinue; if ($null -eq $rb) { $rb = '' }
    $ia = ([regex]::Match("$ra", '"id"\s*:\s*"([0-9a-fA-F-]{36})"')).Groups[1].Value
    $ib = ([regex]::Match("$rb", '"id"\s*:\s*"([0-9a-fA-F-]{36})"')).Groups[1].Value
    return [pscustomobject]@{ IdA = $ia; IdB = $ib; RA = $ra; RB = $rb }
}

try {
    # ===================== SETUP: cluster + template duravel =====================
    $pgPort = Get-FreePort
    $needsStart = $true
    if (-not (Test-Path $templateMarker) -or $RebuildTemplate) {
        Write-Host "[build] construindo template em $templateDir..." -ForegroundColor Cyan
        if (Test-Path $templateDir) {
            & $pg_ctl -D $templateDir -m fast stop | Out-Null
            for ($rTry = 0; $rTry -lt 8; $rTry++) { Start-Sleep -Seconds 1; try { Remove-Item $templateDir -Recurse -Force -ErrorAction Stop; break } catch {} }
            if (Test-Path $templateDir) { throw 'nao foi possivel remover o diretorio do template anterior' }
        }
        $t0 = Get-Date
        $ini = Start-Process -FilePath $initdb -ArgumentList @('-D',$templateDir,'-U','postgres','-A','trust','--encoding=UTF8','--locale=C') -NoNewWindow -PassThru -Wait -RedirectStandardOutput (Join-Path $evidenceDir 'initdb-out.txt') -RedirectStandardError (Join-Path $evidenceDir 'initdb-err.txt')
        if ($ini.ExitCode -ne 0) { throw "initdb falhou (exit $($ini.ExitCode)): " + (Get-Content (Join-Path $evidenceDir 'initdb-err.txt') -Raw -ErrorAction SilentlyContinue) }
        & $pg_ctl -D $templateDir -l (Join-Path $evidenceDir 'template-startup.log') -o "-h 127.0.0.1 -p $pgPort" -w start
        if ($LASTEXITCODE -ne 0) { throw "pg_ctl start do template falhou na porta $pgPort" }
        $ddlTpl = Join-Path $evidenceDir 'create-template-db.sql'; Set-Content $ddlTpl "create database ${templateDb};" -Encoding ASCII
        $cdProc = Start-Process -FilePath $psql -ArgumentList @('-p',"$pgPort","-U","postgres","-h","127.0.0.1","-d","postgres","-X","-v","ON_ERROR_STOP=1","-f",$ddlTpl) -NoNewWindow -PassThru -Wait -RedirectStandardOutput (Join-Path $evidenceDir 'ct.out.txt') -RedirectStandardError (Join-Path $evidenceDir 'ct.err.txt')
        if ($cdProc.ExitCode -ne 0) { throw "create database template falhou" }
        $prelude = Join-Path $evidenceDir 'prelude.sql'; Set-Content -Path $prelude -Value 'SET synchronous_commit=off; SET statement_timeout=2700000;' -Encoding ASCII
        $loadProc = Start-Process -FilePath $psql -ArgumentList @('-p',"$pgPort","-U","postgres","-h","127.0.0.1","-d",$templateDb,'-X','-v','ON_ERROR_STOP=1','--set=client_min_messages=warning','-f',$prelude,'-f',$fullSqlPath) -NoNewWindow -PassThru -Wait -RedirectStandardOutput (Join-Path $evidenceDir 'tpl-load-out.log') -RedirectStandardError (Join-Path $evidenceDir 'tpl-load-err.log')
        if ($loadProc.ExitCode -ne 0) { throw "carga do SQL completo falhou (exit $($loadProc.ExitCode)); veja tpl-load-err.log" }
        Set-Content -Path $templateMarker -Value ("ok {0:s}" -f (Get-Date))
        Write-Host ("[build] template pronto em " + ((Get-Date) - $t0).TotalSeconds.ToString('0.#') + "s") -ForegroundColor Green
        $needsStart = $false; $weStartedCluster = $true
    } else {
        Write-Host "Template existente em $templateDir (use -RebuildTemplate para forcar)." -ForegroundColor Cyan
    }
    $pidFile = Join-Path $templateDir 'postmaster.pid'
    if ((Test-Path $pidFile) -and $needsStart) {
        $stalePid = ((Get-Content $pidFile) | Select-Object -First 1).Trim(); $staleProc = $null
        try { $staleProc = Get-Process -Id ([int]$stalePid) -ErrorAction Stop } catch {}
        if ($null -eq $staleProc) { Remove-Item $pidFile -Force } else { throw "Template em uso por pid $stalePid." }
    }
    if ($needsStart) { & $pg_ctl -D $templateDir -l (Join-Path $evidenceDir 'postgres.log') -o "-h 127.0.0.1 -p $pgPort" -w start; if ($LASTEXITCODE -ne 0) { throw "pg_ctl start falhou na porta $pgPort" }; $weStartedCluster = $true }
    $pingSql = Join-Path $evidenceDir 'ping.sql'; Set-Content $pingSql 'select 1;' -Encoding ASCII
    Start-Process -FilePath $psql -ArgumentList @('-p',"$pgPort","-U","postgres","-h","127.0.0.1","-d","postgres","-X","-A","-t","-q","-f",$pingSql) -NoNewWindow -Wait -RedirectStandardOutput (Join-Path $evidenceDir 'ping-out.txt') -RedirectStandardError (Join-Path $evidenceDir 'ping-err.txt') | Out-Null
    $ping = (Get-Content (Join-Path $evidenceDir 'ping-out.txt') -Raw -ErrorAction SilentlyContinue).Trim()
    Require-Setup 'Cluster-responde-select-1' ($ping -eq '1') "ping=$ping"
    $ddlRun = Join-Path $evidenceDir 'create-run-db.sql'; Set-Content $ddlRun "create database ${dbName} template ${templateDb};" -Encoding ASCII
    $crProc = Start-Process -FilePath $psql -ArgumentList @('-p',"$pgPort","-U","postgres","-h","127.0.0.1","-d","postgres","-X","-v","ON_ERROR_STOP=1","-f",$ddlRun) -NoNewWindow -PassThru -Wait -RedirectStandardOutput (Join-Path $evidenceDir 'cr.out.txt') -RedirectStandardError (Join-Path $evidenceDir 'cr.err.txt')
    if ($crProc.ExitCode -ne 0) { throw "create database a partir do template falhou (exit $($crProc.ExitCode))" }
    Write-Host "PostgreSQL pronto porta $pgPort (db=${dbName} clonado do template)" -ForegroundColor Cyan
    Require-Setup 'Schema-11.25.0-present' ((Invoke-Psql "select version from agro360.platform_schema_versions where version='11.25.0';") -eq '11.25.0') 'migration 135 presente'

    # ===================== CONFIG E2E + START API =====================
    $connStr = "Host=127.0.0.1;Port=${pgPort};Database=${dbName};Username=postgres;Search Path=agro360;Command Timeout=60"
    $env:ConnectionStrings__Agro360 = $connStr
    $env:ConnectionStrings__DefaultConnection = $connStr
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:Jwt__SigningKey = 'e2e-test-signing-key-32bytes-minimum!'
    $env:PGPASSWORD = 'trust-auth-placeholder'
    $apiPort = Get-FreePort
    $env:ASPNETCORE_URLS = "http://127.0.0.1:${apiPort}"
    $apiExe = Join-Path $root 'src\Hosts\Agro360.Api\bin\Release\net10.0\Agro360.Api.dll'
    if (-not (Test-Path $apiExe)) { throw "API binary nao encontrado: $apiExe (faça o build antes)" }
    $apiProc = Start-Process dotnet -ArgumentList "`"$apiExe`"" -WorkingDirectory (Split-Path $apiExe) -PassThru -NoNewWindow -RedirectStandardOutput (Join-Path $evidenceDir 'api.out.log') -RedirectStandardError (Join-Path $evidenceDir 'api.err.log')
    $processes.Add($apiProc)
    $ready = $false
    for ($i = 0; $i -lt 60; $i++) { Start-Sleep -Milliseconds 500; try { $tcp = [Net.Sockets.TcpClient]::new(); $tcp.Connect('127.0.0.1', $apiPort); $tcp.Close(); $ready = $true; break } catch {}; if ($apiProc.HasExited) { break } }
    if (-not $ready) { $el = Join-Path $evidenceDir 'api.err.log'; if (Test-Path $el) { (Get-Content $el -Tail 40) | ForEach-Object { Write-Host "API-ERR: $_" -ForegroundColor Red } } }
    Require-Setup 'API-tcp-ready' $ready "port=$apiPort"
    Start-Sleep -Seconds 2

    # ===================== TENANTS / USUARIOS =====================
    $tenantId = Invoke-Psql "select id::text from agro360.tenancy_tenants where slug='santa-clara';"
    Require-Setup 'Tenant-santa-clara' ($tenantId -ne '') "tenant=$tenantId"
    $valeTenantId = Invoke-Psql "select id::text from agro360.tenancy_tenants where slug='cooperativa-vale-verde';"
    Require-Setup 'Tenant-cooperativa-vale-verde' ($valeTenantId -ne '') "tenant=$valeTenantId"
    Invoke-Psql "update agro360.identity_users set status='ACTIVE', password_hash='$knownHash', must_change_password=false, updated_at=now() where tenant_id='${tenantId}' and lower(email) in ('admin@santaclara.agro360.local','operador.santaclara@agro360.local');" | Out-Null
    Invoke-Psql "update agro360.identity_users set status='ACTIVE', password_hash='$knownHash', must_change_password=false, updated_at=now() where tenant_id='${valeTenantId}' and lower(email)='admin.valeverde@agro360.local';" | Out-Null
    # Modulo agroindustria (permissoes production.* usadas por receipts/inspections/allocations) PRECISA preceder o login.
    Invoke-Psql @"
insert into agro360.platform_tenant_module_entitlements(tenant_id,module_id,status,reason,activated_at)
select '${tenantId}', id, 'ACTIVE', 'E2E journey - modulo agroindustria', now()
from agro360.platform_module_catalog where code='agroindustry'
on conflict(tenant_id,module_id) do update set status='ACTIVE';
"@ | Out-Null
    $adminLogin = Invoke-Api POST '/api/v1/auth/login' @{ TenantSlug = 'santa-clara'; Email = 'admin@santaclara.agro360.local'; Password = 'Agro360Admin@2024!' }
    Require-Setup 'Login-admin-santa' ($adminLogin.Status -eq 200) "HTTP $($adminLogin.Status)"
    $adminToken = $adminLogin.Body.accessToken
    $opLogin = Invoke-Api POST '/api/v1/auth/login' @{ TenantSlug = 'santa-clara'; Email = 'operador.santaclara@agro360.local'; Password = 'Agro360Admin@2024!' }
    Require-Setup 'Login-operador-santa' ($opLogin.Status -eq 200) "HTTP $($opLogin.Status)"
    $operatorToken = $opLogin.Body.accessToken
    $valeLogin = Invoke-Api POST '/api/v1/auth/login' @{ TenantSlug = 'cooperativa-vale-verde'; Email = 'admin.valeverde@agro360.local'; Password = 'Agro360Admin@2024!' }
    Require-Setup 'Login-admin-vale' ($valeLogin.Status -eq 200) "HTTP $($valeLogin.Status) $($valeLogin.Raw)"
    $valeToken = $valeLogin.Body.accessToken
    $adminUser = Invoke-Psql "select id::text from agro360.identity_users where tenant_id='${tenantId}' and lower(email)='admin@santaclara.agro360.local' and deleted_at is null;"
    $operatorUser = Invoke-Psql "select id::text from agro360.identity_users where tenant_id='${tenantId}' and lower(email)='operador.santaclara@agro360.local' and deleted_at is null;"
    $valeAdminUser = Invoke-Psql "select id::text from agro360.identity_users where tenant_id='${valeTenantId}' and lower(email)='admin.valeverde@agro360.local' and deleted_at is null;"
    Require-Setup 'Usuarios-fixtures' ($adminUser -ne '' -and $operatorUser -ne '' -and $valeAdminUser -ne '') "admin=$adminUser op=$operatorUser vale=$valeAdminUser"

    # ===================== FIXTURES (santa) =====================
    $farmA = Invoke-Psql "select id::text from agro360.geo_farms where tenant_id='${tenantId}' and registration_number='SC-SEDE-001' and deleted_at is null;"
    $farmB = Invoke-Psql "select id::text from agro360.geo_farms where tenant_id='${tenantId}' and registration_number='SC-RETIRO-001' and deleted_at is null;"
    Require-Setup 'Fazendas-santa' ($farmA -ne '' -and $farmB -ne '') "farmA=$farmA farmB=$farmB"
    $fieldId = Invoke-Psql @"
insert into agro360.geo_fields(id,tenant_id,farm_id,name,area_ha,boundary,created_by)
select '$(New-Guid)','${tenantId}','${farmA}','Talhao Journey E2E',40.0,null,'${adminUser}'
where not exists(select 1 from agro360.geo_fields where tenant_id='${tenantId}' and farm_id='${farmA}' and name='Talhao Journey E2E');
select id::text from agro360.geo_fields where tenant_id='${tenantId}' and farm_id='${farmA}' and name='Talhao Journey E2E';
"@
    Require-Setup 'Fixture-talhao' ($fieldId -ne '') "field=$fieldId (40 ha)"
    $whId = Invoke-Psql @"
insert into agro360.inventory_warehouses(id,tenant_id,farm_id,code,name,type,created_by)
select '$(New-Guid)','${tenantId}','${farmA}','JRN-GRN','Armazem Graos Journey E2E','GRAINS','${adminUser}'
where not exists(select 1 from agro360.inventory_warehouses where tenant_id='${tenantId}' and code='JRN-GRN');
select id::text from agro360.inventory_warehouses where tenant_id='${tenantId}' and code='JRN-GRN';
"@
    Require-Setup 'Fixture-armazem-graos' ($whId -ne '') "warehouse=$whId"
    $grainP = Invoke-Psql @"
insert into agro360.inventory_products(id,tenant_id,sku,name,category,base_unit,requires_lot,is_perishable,created_by)
select '$(New-Guid)','${tenantId}','JRN-GRAIN','Soja grão Journey E2E','GRAINS','kg',true,false,'${adminUser}'
where not exists(select 1 from agro360.inventory_products where tenant_id='${tenantId}' and sku='JRN-GRAIN');
select id::text from agro360.inventory_products where tenant_id='${tenantId}' and sku='JRN-GRAIN';
"@
    Require-Setup 'Fixture-produto-grao' ($grainP -ne '') "product=$grainP (kg, requires_lot)"
    $matWhId = Invoke-Psql @"
insert into agro360.inventory_warehouses(id,tenant_id,farm_id,code,name,type,created_by)
select '$(New-Guid)','${tenantId}','${farmA}','JRN-INPUT','Armazem Insumos Journey E2E','GRAINS','${adminUser}'
where not exists(select 1 from agro360.inventory_warehouses where tenant_id='${tenantId}' and code='JRN-INPUT');
select id::text from agro360.inventory_warehouses where tenant_id='${tenantId}' and code='JRN-INPUT';
"@
    Require-Setup 'Fixture-armazem-insumos' ($matWhId -ne '') "warehouse=$matWhId"
    $matP = Invoke-Psql @"
insert into agro360.inventory_products(id,tenant_id,sku,name,category,base_unit,requires_lot,is_perishable,created_by)
select '$(New-Guid)','${tenantId}','JRN-FERT','Fertilizante Journey E2E','INPUTS','kg',false,false,'${adminUser}'
where not exists(select 1 from agro360.inventory_products where tenant_id='${tenantId}' and sku='JRN-FERT');
select id::text from agro360.inventory_products where tenant_id='${tenantId}' and sku='JRN-FERT';
"@
    Require-Setup 'Fixture-produto-insumo' ($matP -ne '') "product=$matP (kg, sem lote)"

    # ===================== FIXTURES (vale: org + farm) =====================
    $valeOrgId = Invoke-Psql @"
insert into agro360.organization_organizations(id,tenant_id,type,name,created_by)
select '$(New-Guid)','${valeTenantId}','COOPERATIVE','Coop Vale Verde Journey E2E','${valeAdminUser}'
where not exists(select 1 from agro360.organization_organizations where tenant_id='${valeTenantId}' and name='Coop Vale Verde Journey E2E');
select id::text from agro360.organization_organizations where tenant_id='${valeTenantId}' and name='Coop Vale Verde Journey E2E';
"@
    Require-Setup 'Fixture-org-vale' ($valeOrgId -ne '') "org=$valeOrgId"
    $valeFarmId = Invoke-Psql @"
insert into agro360.geo_farms(id,tenant_id,organization_id,name,state,municipality,total_area_ha,useful_area_ha,registration_number,created_by)
select '$(New-Guid)','${valeTenantId}','${valeOrgId}','Fazenda Vale Journey E2E','PR','Carambeí',100.0,100.0,'VALE-JRN-001','${valeAdminUser}'
where not exists(select 1 from agro360.geo_farms where tenant_id='${valeTenantId}' and registration_number='VALE-JRN-001');
select id::text from agro360.geo_farms where tenant_id='${valeTenantId}' and registration_number='VALE-JRN-001';
"@
    Require-Setup 'Fixture-fazenda-vale' ($valeFarmId -ne '') "farm=$valeFarmId"

    # ===================== SAFRA (santa) =====================
    $dStart = (Get-Date).AddDays(-30).ToString('yyyy-MM-dd'); $dEnd = (Get-Date).AddDays(90).ToString('yyyy-MM-dd')
    $seasonR = Invoke-Api POST '/api/v1/agriculture/seasons' @{ FarmId = $farmA; Name = "Safra Journey E2E ${runTag}"; Crop = 'Soja'; StartDate = $dStart; EndDate = $dEnd; PlannedAreaHa = 40; ExpectedYieldPerHa = 3.5 } $adminToken
    Require-Setup 'Safra-criada' ($seasonR.Status -eq 200 -or $seasonR.Status -eq 201) "HTTP $($seasonR.Status) $($seasonR.Raw)"
    $seasonId = Invoke-Psql "select id::text from agro360.agriculture_seasons where tenant_id='${tenantId}' and name='Safra Journey E2E ${runTag}' and deleted_at is null order by created_at desc limit 1;"
    Require-Setup 'Safra-id' ($seasonId -ne '') "season=$seasonId"

    $nowIso = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')

    # ===================== C: ESCOPO / TENANCY (2 tenants + 2 farms) =====================
    Write-Host '--- C: isolamento de tenant + estreitamento por fazenda ---' -ForegroundColor Cyan
    try {
        # safra em cada tenant (via API de cada um)
        $cs1 = Invoke-Api POST '/api/v1/agriculture/seasons' @{ FarmId = $farmA; Name = "ISO-SANTA-A-${runTag}"; Crop = 'Soja'; StartDate = $dStart; EndDate = $dEnd; PlannedAreaHa = 10; ExpectedYieldPerHa = 3 } $adminToken
        $cs2 = Invoke-Api POST '/api/v1/agriculture/seasons' @{ FarmId = $farmB; Name = "ISO-SANTA-B-${runTag}"; Crop = 'Milho'; StartDate = $dStart; EndDate = $dEnd; PlannedAreaHa = 10; ExpectedYieldPerHa = 3 } $adminToken
        $cv1 = Invoke-Api POST '/api/v1/agriculture/seasons' @{ FarmId = $valeFarmId; Name = "ISO-VALE-${runTag}"; Crop = 'Soja'; StartDate = $dStart; EndDate = $dEnd; PlannedAreaHa = 10; ExpectedYieldPerHa = 3 } $valeToken
        Assert-Step 'C' 'criado-santa-A-B-vale' (($cs1.Status -eq 201 -or $cs1.Status -eq 200) -and ($cs2.Status -eq 201 -or $cs2.Status -eq 200) -and ($cv1.Status -eq 201 -or $cv1.Status -eq 200)) "santaA=$($cs1.Status) santaB=$($cs2.Status) vale=$($cv1.Status) $($cv1.Raw)"
        # isolacao de tenant (RLS): lista de cada um nao mostra o outro
        $lsS = Invoke-Api GET '/api/v1/agriculture/seasons?page=1&pageSize=100' -Token $adminToken
        $lsV = Invoke-Api GET '/api/v1/agriculture/seasons?page=1&pageSize=100' -Token $valeToken
        $sNames = (@($lsS.Body.items) | ForEach-Object { "$($_.name)" }) -join ','
        $vNames = (@($lsV.Body.items) | ForEach-Object { "$($_.name)" }) -join ','
        Assert-Step 'C' 'rls-santa-nao-ve-vale' ($lsS.Status -eq 200 -and ($sNames -like "*ISO-SANTA-*") -and ($sNames -notlike "*ISO-VALE-*")) "santaVejo=[$sNames]"
        Assert-Step 'C' 'rls-vale-nao-ve-santa' ($lsV.Status -eq 200 -and ($vNames -like "*ISO-VALE-*") -and ($vNames -notlike "*ISO-SANTA-*")) "valeVejo=[$vNames]"
        # estreitamento por fazenda (query param ?farmId): cada talhao/escopo ve so a sua safra
        $lnA = Invoke-Api GET "/api/v1/agriculture/seasons?farmId=${farmA}&page=1&pageSize=100" -Token $adminToken
        $lnB = Invoke-Api GET "/api/v1/agriculture/seasons?farmId=${farmB}&page=1&pageSize=100" -Token $adminToken
        $nA = (@($lnA.Body.items) | ForEach-Object { "$($_.name)" }) -join ','
        $nB = (@($lnB.Body.items) | ForEach-Object { "$($_.name)" }) -join ','
        Assert-Step 'C' 'estreita-farmA-so-farmA' ($nA -like "*ISO-SANTA-A-*" -and $nA -notlike "*ISO-SANTA-B-*") "farmA=[$nA]"
        Assert-Step 'C' 'estreita-farmB-so-farmB' ($nB -like "*ISO-SANTA-B-*" -and $nB -notlike "*ISO-SANTA-A-*") "farmB=[$nB]"
        # fazenda FORA do escopo do usuario -> 403 (X-Farm-ID nao alarga autorizacao)
        Invoke-Psql "delete from agro360.identity_user_unit_scopes where tenant_id='${tenantId}' and user_id='${operatorUser}' and scope_type='ALL';" | Out-Null
        Invoke-Psql "insert into agro360.identity_user_unit_scopes(id,tenant_id,user_id,scope_type,farm_id,created_at) values('$(New-Guid)','${tenantId}','${operatorUser}','FARM','${farmA}',now());" | Out-Null
        $u403 = Invoke-Api GET '/api/v1/agriculture/seasons?page=1&pageSize=10' -Token $operatorToken -FarmId $farmB
        $uOk = Invoke-Api GET '/api/v1/agriculture/seasons?page=1&pageSize=10' -Token $operatorToken -FarmId $farmA
        Assert-Step 'C' 'farm-fora-escopo-403' ($u403.Status -eq 403) "HTTP $($u403.Status) $($u403.Raw)"
        Assert-Step 'C' 'farm-no-escopo-ok' ($uOk.Status -eq 200) "HTTP $($uOk.Status)"
        # restaura escopo ALL do operador
        Invoke-Psql "insert into agro360.identity_user_unit_scopes(id,tenant_id,user_id,scope_type,created_at) values('$(New-Guid)','${tenantId}','${operatorUser}','ALL',now());" | Out-Null
    } catch { Note-Blocked 'C' 'secao' $_ }

    # ===================== D: PLANEJAMENTO (dependencias, ciclo, ordem, reschedule, dedup) =====================
    Write-Host '--- D: planejamento de operacoes ---' -ForegroundColor Cyan
    try {
        # cultura ativa referenciada pelo plano (regra cultura_required)
        $cropId = [guid]::NewGuid().ToString()
        Invoke-Psql "insert into agro360.agriculture_crops(id,tenant_id,name,active,created_by) values('${cropId}','${tenantId}','Cultura JRN ${runTag}',true,'${adminUser}');" | Out-Null
        # plano (registro do modulo 'plans') ligado a safra/talhao/fazenda/cultura
        $planCreate = Invoke-Api POST '/api/agriculture/plans' @{ PropertyId = $farmA; FieldId = $fieldId; FieldIds = @($fieldId); CropSeasonId = $seasonId; CropId = $cropId; Name = "Plano Journey ${runTag}"; Type = 'PLANO'; Status = 'OPEN' } $adminToken
        Assert-Step 'D' 'plano-criado' ($planCreate.Status -eq 200 -or $planCreate.Status -eq 201) "HTTP $($planCreate.Status) $($planCreate.Raw)"
        $planId = "$(if ($planCreate.Body.id) { $planCreate.Body.id } else { '' })"
        if (-not $planId) { $planId = Invoke-Psql "select id::text from agro360.agriculture_records where tenant_id='${tenantId}' and module='plans' and deleted_at is null order by created_at desc limit 1;" }

        $ops = @{}
        $opNames = @{ 'A' = 'Fertizacao JRN A'; 'B' = 'Irrigacao JRN B'; 'C' = 'Pulverizacao JRN C' }
        $scopeDiag = Invoke-Psql "select 'plan_mod='||coalesce(r.module,'N')||'|crop='||coalesce(r.data->>'cropSeasonId','N')||'|seasonFarm='||coalesce(s.farm_id::text,'N')||'|fieldFarm='||coalesce(f.farm_id::text,'N')||'|farmA=${farmA}' from agro360.agriculture_records r left join agro360.agriculture_seasons s on s.tenant_id=r.tenant_id and s.id='${seasonId}' left join agro360.geo_fields f on f.tenant_id=r.tenant_id and f.id='${fieldId}' where r.tenant_id='${tenantId}' and r.id='${planId}';"
        Write-Host "[diag-D] scope=$scopeDiag"
        foreach ($k in 'A','B','C') {
            $ps_ = (Get-Date).AddDays(2).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
            $pe_ = (Get-Date).AddDays(6).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
            $r = Invoke-Api POST "/api/agriculture/plans/${planId}/operations" @{ SeasonId = $seasonId; FarmId = $farmA; FieldId = $fieldId; Name = $opNames[$k]; OperationType = $k; PlannedStart = $ps_; PlannedEnd = $pe_; PlannedAreaHa = 40; ResponsibleId = $adminUser } $adminToken
            $oid = "$(if ($r.Body.id) { $r.Body.id } else { '' })"
            if (-not $oid) { $oid = Invoke-Psql "select id::text from agro360.agriculture_plan_operations where tenant_id='${tenantId}' and name='$($opNames[$k])' and deleted_at is null order by created_at desc limit 1;" }
            $ops[$k] = $oid
        }
        Assert-Step 'D' 'operacoes-A-B-C' ($ops['A'] -ne '' -and $ops['B'] -ne '' -and $ops['C'] -ne '') "opA=$($ops['A']) opB=$($ops['B']) opC=$($ops['C']) LAST-opC=HTTP $($r.Status) $($r.Raw)"

        # auto-dependencia
        $self = Invoke-Api POST "/api/agriculture/operations/$($ops['A'])/dependencies" @{ PredecessorId = $ops['A']; BlockingType = 'REQUIRED'; ReleaseCondition = 'COMPLETED' } $adminToken
        Assert-Step 'D' 'dep-self-rejeitada' (Is-Err $self @(400,409,422) 'dependency_self') "HTTP $($self.Status) $($self.Raw)"
        # par valido A->B (B requer A), depois B->A cria ciclo
        $dab = Invoke-Api POST "/api/agriculture/operations/$($ops['B'])/dependencies" @{ PredecessorId = $ops['A']; BlockingType = 'ADVISORY'; ReleaseCondition = 'COMPLETED' } $adminToken
        Assert-Step 'D' 'dep-par-aceita' ($dab.Status -eq 204 -or $dab.Status -eq 200 -or $dab.Status -eq 201) "HTTP $($dab.Status) $($dab.Raw)"
        $cycle = Invoke-Api POST "/api/agriculture/operations/$($ops['A'])/dependencies" @{ PredecessorId = $ops['B']; BlockingType = 'REQUIRED'; ReleaseCondition = 'COMPLETED' } $adminToken
        Assert-Step 'D' 'dep-ciclo-rejeitado' (Is-Err $cycle @(400,409,422) 'dependency_cycle') "HTTP $($cycle.Status) $($cycle.Raw)"
        # C requer B (REQUIRED) para o teste de ordem invertida
        $dbc = Invoke-Api POST "/api/agriculture/operations/$($ops['C'])/dependencies" @{ PredecessorId = $ops['B']; BlockingType = 'REQUIRED'; ReleaseCondition = 'COMPLETED' } $adminToken
        Assert-Step 'D' 'dep-obrigatoria-C-requer-B' ($dbc.Status -eq 204 -or $dbc.Status -eq 200 -or $dbc.Status -eq 201) "HTTP $($dbc.Status) $($dbc.Raw)"
        # gera ordem de campo para C e tenta liberar com o predecessor B ainda nao concluido
        $gc = Invoke-Api POST "/api/agriculture/operations/$($ops['C'])/orders" @{ CoveredAreaHa = 10; IdempotencyKey = "jrn-orderc-$runTag" } $adminToken
        Assert-Step 'D' 'ordem-C-gerada' ($gc.Status -eq 200 -or $gc.Status -eq 201) "HTTP $($gc.Status) $($gc.Raw)"
        $wcId = "$(if ($gc.Body.id) { $gc.Body.id } else { '' })"
        if (-not $wcId) { $wcId = Invoke-Psql "select work_order_id::text from agro360.agriculture_operation_orders where tenant_id='${tenantId}' and idempotency_key='jrn-orderc-$runTag';" }
        # garante pre-condicao de liberacao (dados) para que o BLOQUEO seja de dependencia, nao de dados
        Invoke-Psql "update agro360.agriculture_records set data = data || jsonb_build_object('propertyId','${farmA}','fieldId','${fieldId}','responsibleId','${adminUser}','plannedAt','${nowIso}'), version=version where id='${wcId}';" | Out-Null
        $wcVer = Get-RecVersion $wcId
        $rel = Invoke-Api POST "/api/agriculture/work-orders/${wcId}/approve" @{ Version = [long]$wcVer } $adminToken
        Assert-Step 'D' 'liberacao-bloqueada-ordem-invertida' (Is-Err $rel @(400,409,422) 'dependency_blocked') "HTTP $($rel.Status) $($rel.Raw)"
        # reschedule NAO propaga datas para dependentes
        $bBefore = Invoke-Psql "select planned_start::text || '|' || planned_end::text from agro360.agriculture_plan_operations where id='$($ops['B'])';"
        $cBefore = Invoke-Psql "select planned_start::text || '|' || planned_end::text from agro360.agriculture_plan_operations where id='$($ops['C'])';"
        $rsNewS = (Get-Date).AddDays(20).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'); $rsNewE = (Get-Date).AddDays(24).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        $rs = Invoke-Api POST "/api/agriculture/operations/$($ops['A'])/reschedule" @{ PlannedStart = $rsNewS; PlannedEnd = $rsNewE; PlannedAreaHa = 40; Reason = "Reschedule JRN ${runTag}"; Version = [long](Get-PlanOpVersion $ops['A']) } $adminToken
        $aAfter = Invoke-Psql "select planned_start::text from agro360.agriculture_plan_operations where id='$($ops['A'])';"
        $bAfter = Invoke-Psql "select planned_start::text || '|' || planned_end::text from agro360.agriculture_plan_operations where id='$($ops['B'])';"
        $cAfter = Invoke-Psql "select planned_start::text || '|' || planned_end::text from agro360.agriculture_plan_operations where id='$($ops['C'])';"
        $revCount = Invoke-Psql "select count(*)::text from agro360.agriculture_plan_revisions where tenant_id='${tenantId}' and operation_id='$($ops['A'])';"
        Assert-Step 'D' 'reschedule-aplicado' ($rs.Status -eq 200 -and ($aAfter -ne (Invoke-Psql "select original_start::text from agro360.agriculture_plan_operations where id='$($ops['A'])';")) -and [int]$revCount -ge 1) "HTTP $($rs.Status) revisoes=$revCount novaStart=$aAfter"
        Assert-Step 'D' 'reschedule-nao-propaga' ($bAfter -eq $bBefore -and $cAfter -eq $cBefore) "B:${bBefore}->${bAfter} C:${cBefore}->${cAfter}"
        # geracao CONCURRENTE da mesma chave -> UMA unica ordem (advisory lock + unique idempotency_key)
        $conc = Test-ConcurrentGenerate $ops['A'] 5 "jrn-conc-$runTag" $adminToken
        $concRows = Invoke-Psql "select count(*)::text from agro360.agriculture_operation_orders where tenant_id='${tenantId}' and idempotency_key='jrn-conc-$runTag';"
        Assert-Step 'D' 'geracao-concorrente-ordem-unica' ($conc.IdA -ne '' -and $conc.IdA -eq $conc.IdB -and [int]$concRows -eq 1) "idA=$($conc.IdA) idB=$($conc.IdB) linhas=$concRows"
    } catch { Note-Blocked 'D' 'secao' $_ }

    # ===================== B: ESTADOS (maquina de transicoes da ordem) =====================
    Write-Host '--- B: maquina de estados da ordem de campo ---' -ForegroundColor Cyan
    try {
        $woMain = New-DirectWorkOrder 'PLANNED' 'B1' $farmA $fieldId $adminUser
        Write-Host "[diag-B] woMain.data=" (Invoke-Psql "select coalesce(data::text,'(null)') from agro360.agriculture_records where id='${woMain}' and tenant_id='${tenantId}';")
        Write-Host "[diag-B] woMain.version=" (Get-RecVersion $woMain)
        Write-Host "[diag-B] approve-body=" (@{ Version = [long](Get-RecVersion $woMain) } | ConvertTo-Json -Compress)
        # versao obrigatoria
        $noVer = Invoke-Api POST "/api/agriculture/work-orders/${woMain}/start" @{} $adminToken
        Assert-Step 'B' 'versao-obrigatoria' (Is-Err $noVer @(400,409,422) 'version_required') "HTTP $($noVer.Status) $($noVer.Raw)"
        # transicao invalida do estado inicial (complete so sai de IN_PROGRESS/PAUSED)
        $badTr = Invoke-Api POST "/api/agriculture/work-orders/${woMain}/complete" @{ Version = [long](Get-RecVersion $woMain) } $adminToken
        Assert-Step 'B' 'transicao-invalida-rejeitada' (Is-Err $badTr @(400,409,422) 'transition_conflict') "HTTP $($badTr.Status) $($badTr.Raw)"
        # liberar (RELEASED)
        $ap = Invoke-Api POST "/api/agriculture/work-orders/${woMain}/approve" @{ Version = [long](Get-RecVersion $woMain) } $adminToken
        Assert-Step 'B' 'open-planned->released' ($ap.Status -eq 200 -and (Get-RecStatus $woMain) -eq 'RELEASED') "HTTP $($ap.Status) status=$(Get-RecStatus $woMain) RAW=$($ap.Raw)"
        # iniciar (IN_PROGRESS)
        $st = Invoke-Api POST "/api/agriculture/work-orders/${woMain}/start" @{ Version = [long](Get-RecVersion $woMain) } $adminToken
        Assert-Step 'B' 'released->in_progress' ($st.Status -eq 200 -and (Get-RecStatus $woMain) -eq 'IN_PROGRESS') "HTTP $($st.Status) status=$(Get-RecStatus $woMain)"
        # pausa sem motivo
        $pauseBad = Invoke-Api POST "/api/agriculture/work-orders/${woMain}/pause" @{ Version = [long](Get-RecVersion $woMain) } $adminToken
        Assert-Step 'B' 'pausa-sim-motivo-rejeitada' (Is-Err $pauseBad @(400,409,422) 'pause_reason_required') "HTTP $($pauseBad.Status) $($pauseBad.Raw)"
        # pausa com motivo
        $pause = Invoke-Api POST "/api/agriculture/work-orders/${woMain}/pause" @{ Version = [long](Get-RecVersion $woMain); PauseReason = "Interrupcao JRN ${runTag}" } $adminToken
        Assert-Step 'B' 'in_progress->paused' ($pause.Status -eq 200 -and (Get-RecStatus $woMain) -eq 'PAUSED') "HTTP $($pause.Status) status=$(Get-RecStatus $woMain)"
        # retomar
        $resume = Invoke-Api POST "/api/agriculture/work-orders/${woMain}/start" @{ Version = [long](Get-RecVersion $woMain) } $adminToken
        Assert-Step 'B' 'paused->in_progress' ($resume.Status -eq 200 -and (Get-RecStatus $woMain) -eq 'IN_PROGRESS') "HTTP $($resume.Status) status=$(Get-RecStatus $woMain)"
        # concluir revisao (AWAITING_REVIEW)
        $comp = Invoke-Api POST "/api/agriculture/work-orders/${woMain}/complete" @{ Version = [long](Get-RecVersion $woMain) } $adminToken
        Assert-Step 'B' 'in_progress->awaiting_review' ($comp.Status -eq 200 -and (Get-RecStatus $woMain) -eq 'AWAITING_REVIEW') "HTTP $($comp.Status) status=$(Get-RecStatus $woMain)"

        # cancelamento: motivo obrigatorio + terminal irreversivel (ordem separada)
        $woCancel = New-DirectWorkOrder 'PLANNED' 'B2' $farmA $fieldId $adminUser
        $cancelBad = Invoke-Api POST "/api/agriculture/work-orders/${woCancel}/cancel" @{ Version = [long](Get-RecVersion $woCancel) } $adminToken
        Assert-Step 'B' 'cancelamento-sem-motivo-rejeitado' (Is-Err $cancelBad @(400,409,422) 'cancellation_reason_required') "HTTP $($cancelBad.Status) $($cancelBad.Raw)"
        $cancel = Invoke-Api POST "/api/agriculture/work-orders/${woCancel}/cancel" @{ Version = [long](Get-RecVersion $woCancel); CancellationReason = "Cancelamento JRN ${runTag}" } $adminToken
        Assert-Step 'B' 'planned->cancelled' ($cancel.Status -eq 200 -and (Get-RecStatus $woCancel) -eq 'CANCELLED') "HTTP $($cancel.Status) status=$(Get-RecStatus $woCancel)"
        $afterCancel = Invoke-Api POST "/api/agriculture/work-orders/${woCancel}/start" @{ Version = [long](Get-RecVersion $woCancel) } $adminToken
        Assert-Step 'B' 'terminal-irreversivel' (Is-Err $afterCancel @(400,409,422) 'transition_conflict') "HTTP $($afterCancel.Status) $($afterCancel.Raw)"
    } catch { Note-Blocked 'B' 'secao' $_ }

    # ===================== A: MATERIAIS (ledger canonico) =====================
    Write-Host '--- A: ledger canonico de materiais ---' -ForegroundColor Cyan
    try {
        # saldo inicial do insumo (moeda decimal: custo unitario 12,5)
        $unitCost = [decimal]12.5
        $balId = [guid]::NewGuid().ToString()
        Invoke-Psql @"
insert into agro360.inventory_stock_balances(id,tenant_id,warehouse_id,product_id,unit,available,reserved,minimum,average_cost,version)
values('${balId}','${tenantId}','${matWhId}','${matP}','kg',1000,0,0,${unitCost},1);
"@ | Out-Null
        Assert-Step 'A' 'saldo-inicial' ((BalanceAvail $matWhId $matP) -eq [decimal]1000) "available=1000"
        $woMat = New-DirectWorkOrder 'PLANNED' 'MAT' $farmA $fieldId $adminUser
        # material adicionado (unidade deve ser a base do produto = kg)
        $addMat = Invoke-Api POST "/api/agriculture/work-orders/${woMat}/materials" @{ ProductId = $matP; WarehouseId = $matWhId; Unit = 'kg'; PlannedQuantity = 500; UnitCost = $unitCost } $adminToken
        Assert-Step 'A' 'material-adicionado' ($addMat.Status -eq 204 -or $addMat.Status -eq 200 -or $addMat.Status -eq 201) "HTTP $($addMat.Status) $($addMat.Raw)"
        $mid = Invoke-Psql "select id::text from agro360.field_work_order_materials where tenant_id='${tenantId}' and work_order_id='${woMat}' and product_id='${matP}';"
        Require-Setup 'A-material-id' ($mid -ne '') "material=$mid"
        # ordem precisa estar em execucao para CONSUME
        $startMat = Invoke-Api POST "/api/agriculture/work-orders/${woMat}/start" @{ Version = [long](Get-RecVersion $woMat) } $adminToken
        # (se ainda PLANNED, aprova antes)
        if ((Get-RecStatus $woMat) -ne 'IN_PROGRESS') {
            $apm = Invoke-Api POST "/api/agriculture/work-orders/${woMat}/approve" @{ Version = [long](Get-RecVersion $woMat) } $adminToken
            $startMat = Invoke-Api POST "/api/agriculture/work-orders/${woMat}/start" @{ Version = [long](Get-RecVersion $woMat) } $adminToken
        }
        Assert-Step 'A' 'ordem-em-execucao' ((Get-RecStatus $woMat) -eq 'IN_PROGRESS') "status=$(Get-RecStatus $woMat) ($($startMat.Status))"
        # RESERVE <= planned
        $res = Invoke-Api POST "/api/agriculture/work-orders/${woMat}/materials/${mid}/events" @{ EventType = 'RESERVE'; Quantity = 500; IdempotencyKey = "jrn-reserve-$runTag" } $adminToken
        Assert-Step 'A' 'reserve' ($res.Status -eq 204) "HTTP $($res.Status) $($res.Raw)"
        # DELIVER <= reserved
        $del = Invoke-Api POST "/api/agriculture/work-orders/${woMat}/materials/${mid}/events" @{ EventType = 'DELIVER'; Quantity = 500; IdempotencyKey = "jrn-deliver-$runTag" } $adminToken
        Assert-Step 'A' 'deliver' ($del.Status -eq 204) "HTTP $($del.Status) $($del.Raw)"
        # CONSUME -> efeito NO ledger canonico (1 movimento EXIT)
        $kc = "jrn-consume-$runTag"
        $cons = Invoke-Api POST "/api/agriculture/work-orders/${woMat}/materials/${mid}/events" @{ EventType = 'CONSUME'; Quantity = 200; IdempotencyKey = $kc } $adminToken
        $mvCons = Invoke-Psql "select count(*)::text from agro360.inventory_stock_movements m join agro360.inventory_stock_balances b on b.tenant_id=m.tenant_id and b.warehouse_id=m.warehouse_id and b.product_id=m.product_id where m.tenant_id='${tenantId}' and m.warehouse_id='${matWhId}' and m.product_id='${matP}' and m.movement_type='CONSUMPTION';"
        $consQty = Invoke-Psql "select consumed_quantity::text from agro360.field_work_order_materials where id='${mid}';"
        Assert-Step 'A' 'consume-reduz-saldo' ($cons.Status -eq 204 -and (BalanceAvail $matWhId $matP) -eq [decimal]800) "HTTP $($cons.Status) available=$(BalanceAvail $matWhId $matP) (esperado 800) RAW=$($cons.Raw)"
        Assert-Step 'A' 'um-efeito-por-comando' ([int]$mvCons -eq 1 -and [decimal]$consQty -eq [decimal]200) "movimentos_CONSUMPTION=$mvCons consumido=$consQty"
        # custo reconhecido (fonte MANAGEMENT) = qtd x custo unitario (decimal exato)
        $recCost = Invoke-Psql "select sum(recognized_amount)::text from agro360.cost_management_entries where tenant_id='${tenantId}' and category='MATERIAL' and farm_id='${farmA}' and source_type='MATERIAL';"
        Assert-Step 'A' 'custo-reconhecido-decimal-exato' ($recCost -ne '' -and [decimal]$recCost -eq [decimal]2500) "recognized=$recCost (esperado 2500.0000)"
        # replay do MESMO comando -> nenhum re-decremento
        $replay = Invoke-Api POST "/api/agriculture/work-orders/${woMat}/materials/${mid}/events" @{ EventType = 'CONSUME'; Quantity = 200; IdempotencyKey = $kc } $adminToken
        Assert-Step 'A' 'replay-sem-redecremento' (($replay.Status -eq 204 -or $replay.Status -eq 200) -and (BalanceAvail $matWhId $matP) -eq [decimal]800 -and [int]$mvCons -eq 1) "HTTP $($replay.Status) available=$(BalanceAvail $matWhId $matP) movimentos=$mvCons"
        # mesma chave + conteudo DIFERENTE -> conflito de payload
        $conflict = Invoke-Api POST "/api/agriculture/work-orders/${woMat}/materials/${mid}/events" @{ EventType = 'CONSUME'; Quantity = 300; IdempotencyKey = $kc } $adminToken
        Assert-Step 'A' 'chave-mesma-conteudo-diferente-conflito' (Is-Err $conflict @(400,409,422) 'idempotency') "HTTP $($conflict.Status) $($conflict.Raw)"
        # ESTORNO (REVERSAL) do consumo -> devolve ao estoque
        $consumeEventId = Invoke-Psql "select id::text from agro360.field_material_events where tenant_id='${tenantId}' and work_order_material_id='${mid}' and event_type='CONSUME' order by created_at desc limit 1;"
        $rev = Invoke-Api POST "/api/agriculture/work-orders/${woMat}/materials/${mid}/events" @{ EventType = 'REVERSAL'; Quantity = 200; SourceEventId = $consumeEventId; Reason = "Estorno validacao E2E ${runTag}"; IdempotencyKey = "jrn-reverse-$runTag" } $adminToken
        Assert-Step 'A' 'estorno-valido-devolve-estoque' ($rev.Status -eq 204 -and (BalanceAvail $matWhId $matP) -eq [decimal]1000) "HTTP $($rev.Status) available=$(BalanceAvail $matWhId $matP) (esperado 1000)"
        # estorno duplicado do MESMO consumo -> rejeitado
        $dupRev = Invoke-Api POST "/api/agriculture/work-orders/${woMat}/materials/${mid}/events" @{ EventType = 'REVERSAL'; Quantity = 200; SourceEventId = $consumeEventId; Reason = "Estorno duplicado E2E ${runTag}"; IdempotencyKey = "jrn-reverse2-$runTag" } $adminToken
        Assert-Step 'A' 'estorno-duplicado-rejeitado' (Is-Err $dupRev @(400,409,422)) "HTTP $($dupRev.Status) $($dupRev.Raw)"
    } catch { Note-Blocked 'A' 'secao' $_ }

    # ===================== E: COLHEITA (parciais, teto, peso, recibo, inspecao, lote) =====================
    Write-Host '--- E: colheita e lote disponivel ---' -ForegroundColor Cyan
    try {
        $hpStart = (Get-Date).AddDays(1).ToString('yyyy-MM-dd'); $hpEnd = (Get-Date).AddDays(30).ToString('yyyy-MM-dd')
        $planR = Invoke-Api POST '/api/v1/harvest/plans' @{ FarmId = $farmA; SeasonId = $seasonId; FieldId = $fieldId; ProductId = $grainP; DestinationWarehouseId = $whId; ResponsibleId = $adminUser; PlannedStart = $hpStart; PlannedEnd = $hpEnd; PlannedAreaHa = 36; EstimatedQuantity = 150000; Unit = 'kg'; IdempotencyKey = "jrn-plan-$runTag" } $adminToken
        Assert-Step 'E' 'plano-colheita' ($planR.Status -eq 200 -or $planR.Status -eq 201) "HTTP $($planR.Status) $($planR.Raw)"
        $planIdE = Invoke-Psql "select id::text from agro360.harvest_plans where tenant_id='${tenantId}' and farm_id='${farmA}' and field_id='${fieldId}' order by created_at desc limit 1;"
        Require-Setup 'E-plano-id' ($planIdE -ne '') "plan=$planIdE"

        # TETO multi-passe de area: 20 + 20 > 36 (plano) -> 2o passe recusado
        $r1 = Invoke-Api POST '/api/v1/harvest/records' @{ PlanId = $planIdE; OperationalAt = $nowIso; HarvestedQuantity = 60000; Unit = 'kg'; HarvestedAreaHa = 20; CommercialReference = "JRN-R1-${runTag}"; IdempotencyKey = "jrn-rec1-$runTag" } $adminToken
        Assert-Step 'E' 'passe-1-area-aceita' ($r1.Status -eq 200 -or $r1.Status -eq 201) "HTTP $($r1.Status) $($r1.Raw)"
        $rec1 = Invoke-Psql "select id::text from agro360.harvest_records where tenant_id='${tenantId}' and commercial_reference='JRN-R1-$runTag';"
        $r2 = Invoke-Api POST '/api/v1/harvest/records' @{ PlanId = $planIdE; OperationalAt = $nowIso; HarvestedQuantity = 60000; Unit = 'kg'; HarvestedAreaHa = 20; CommercialReference = "JRN-R2-${runTag}"; IdempotencyKey = "jrn-rec2-$runTag" } $adminToken
        Assert-Step 'E' 'teto-area-multi-passe' (Is-Err $r2 @(400,409,422) 'area_exceeds_field') "HTTP $($r2.Status) $($r2.Raw)"

        # RECIBO PARCIAL + acima-do-saldo recusado (rec1 = 60000 kg)
        $p1 = Invoke-Api POST '/api/v1/harvest/receipts' @{ HarvestRecordId = $rec1; WarehouseId = $whId; ReceivedAt = $nowIso; ReceivedQuantity = 25000; Unit = 'kg'; LotNumber = "LOTE-E1-${runTag}"; EntryMode = 'MANUAL'; IdempotencyKey = "jrn-rcpt1-$runTag" } $adminToken
        Assert-Step 'E' 'recibo-parcial' (($p1.Status -eq 200 -or $p1.Status -eq 201)) "HTTP $($p1.Status) $($p1.Raw)"
        $above = Invoke-Api POST '/api/v1/harvest/receipts' @{ HarvestRecordId = $rec1; WarehouseId = $whId; ReceivedAt = $nowIso; ReceivedQuantity = 40000; Unit = 'kg'; LotNumber = "LOTE-E1B-${runTag}"; EntryMode = 'MANUAL'; IdempotencyKey = "jrn-rcpt-above-$runTag" } $adminToken
        Assert-Step 'E' 'recibo-acima-do-saldo-recusado' (Is-Err $above @(400,409,422) 'receipt_exceeds_balance') "HTTP $($above.Status) $($above.Raw)"
        $rcptId = Invoke-Psql "select id::text from agro360.production_receipts where tenant_id='${tenantId}' and idempotency_key='jrn-rcpt1-$runTag';"

        # BRUTO/TARA/LÍQUIDO: net=gross-tare; divergencia exige justificativa
        $mism = Invoke-Api POST '/api/v1/harvest/receipts' @{ HarvestRecordId = $rec1; WarehouseId = $whId; ReceivedAt = $nowIso; ReceivedQuantity = 20000; Unit = 'kg'; GrossWeight = 25000; TareWeight = 4000; LotNumber = "LOTE-E-W-${runTag}"; EntryMode = 'SCALE'; IdempotencyKey = "jrn-rcpt-w-$runTag" } $adminToken
        Assert-Step 'E' 'peso-net-divergente-sem-motivo' (Is-Err $mism @(400,409,422) 'net_weight_mismatch') "HTTP $($mism.Status) $($mism.Raw) (net=21000 vs recebido=20000)"
        $okdiv = Invoke-Api POST '/api/v1/harvest/receipts' @{ HarvestRecordId = $rec1; WarehouseId = $whId; ReceivedAt = $nowIso; ReceivedQuantity = 20000; Unit = 'kg'; GrossWeight = 25000; TareWeight = 4000; DivergenceReason = "Umidade do lote JRN ${runTag}"; LotNumber = "LOTE-E-WD-${runTag}"; EntryMode = 'SCALE'; IdempotencyKey = "jrn-rcpt-wd-$runTag" } $adminToken
        Assert-Step 'E' 'peso-net-divergente-com-justificativa' (($okdiv.Status -eq 200 -or $okdiv.Status -eq 201)) "HTTP $($okdiv.Status) $($okdiv.Raw)"

        # INSPECAO: aprovar + replay nao duplica decisao
        $specId = Invoke-Psql @"
insert into agro360.quality_specifications(id,tenant_id,product_id,name,version,status,application_type,valid_from,created_by)
select '$(New-Guid)','${tenantId}','${grainP}','Spec Grao JRN',1,'ACTIVE','HARVEST_RECEIPT','$(Get-Date -Format yyyy-MM-dd)','${adminUser}'
where not exists(select 1 from agro360.quality_specifications where tenant_id='${tenantId}' and product_id='${grainP}' and name='Spec Grao JRN');
select id::text from agro360.quality_specifications where tenant_id='${tenantId}' and product_id='${grainP}' and name='Spec Grao JRN';
"@
        Require-Setup 'E-spec-ativa' ($specId -ne '') "spec=$specId"
        $insp = Invoke-Api POST '/api/v1/harvest/inspections' @{ ReceiptId = $rcptId; SpecificationId = $specId; Results = @(); Decision = 'APPROVED'; IdempotencyKey = "jrn-insp-$runTag" } $adminToken
        Assert-Step 'E' 'inspecao-aprovada' ($insp.Status -eq 200 -or $insp.Status -eq 201) "HTTP $($insp.Status) $($insp.Raw)"
        $accQty = Invoke-Psql "select accepted_quantity::text from agro360.production_receipts where id='${rcptId}';"
        Assert-Step 'E' 'aprovacao-define-accepted-quantity' ([decimal]$accQty -eq [decimal]25000) "accepted=$accQty (esperado 25000)"
        $inspAgain = Invoke-Api POST '/api/v1/harvest/inspections' @{ ReceiptId = $rcptId; SpecificationId = $specId; Results = @(); Decision = 'APPROVED'; IdempotencyKey = "jrn-insp2-$runTag" } $adminToken
        Assert-Step 'E' 'replay-inspecao-uma-decisao' (Is-Err $inspAgain @(400,409,422) 'inspection_completed') "HTTP $($inspAgain.Status) $($inspAgain.Raw)"

        # DESTINACAO AVAILABLE -> cria LOTE + LANCAMENTO no estoque
        $beforeLot = Invoke-Psql "select coalesce(quantity,0)::text from agro360.inventory_stock_lots where tenant_id='${tenantId}' and warehouse_id='${whId}' and product_id='${grainP}' and lot_number='LOTE-E1-$runTag';"
        $alloc = Invoke-Api POST '/api/v1/harvest/allocations' @{ ReceiptId = $rcptId; Quantity = 20000; Destination = 'AVAILABLE'; Reason = "Destinacao para venda JRN ${runTag}"; IdempotencyKey = "jrn-alloc-$runTag" } $adminToken
        $afterLot = Invoke-Psql "select coalesce(quantity,0)::text from agro360.inventory_stock_lots where tenant_id='${tenantId}' and warehouse_id='${whId}' and product_id='${grainP}' and lot_number='LOTE-E1-$runTag';"
        $mvProd = Invoke-Psql "select m.movement_type || '|' || m.quantity::text from agro360.inventory_stock_movements m join agro360.production_receipts r on r.id=m.reference_id where m.tenant_id='${tenantId}' and m.reference_type='PRODUCTION' and r.id='${rcptId}' order by m.occurred_at desc limit 1;"
        Assert-Step 'E' 'destinacao-cria-lote' ($alloc.Status -eq 200 -or $alloc.Status -eq 201) "HTTP $($alloc.Status) $($alloc.Raw) lote $($beforeLot)->$afterLot"
        Assert-Step 'E' 'lancamento-production-correto' ([decimal]$afterLot -eq [decimal]20000 -and "$mvProd" -like 'PRODUCTION|20000*') "lote=$afterLot mov=$mvProd"
        # acima do aprovado -> bloqueado (so o saldo aprovado entra em estoque)
        $overAlloc = Invoke-Api POST '/api/v1/harvest/allocations' @{ ReceiptId = $rcptId; Quantity = 20000; Destination = 'AVAILABLE'; Reason = "Apos tudo JRN ${runTag}"; IdempotencyKey = "jrn-alloc-over-$runTag" } $adminToken
        Assert-Step 'E' 'destinacao-acima-aprovada-bloqueada' (Is-Err $overAlloc @(400,409,422) 'allocation_exceeds_balance') "HTTP $($overAlloc.Status) $($overAlloc.Raw)"
    } catch { Note-Blocked 'E' 'secao' $_ }

    # ===================== F: INDICADORES (null<>zero, custo reconhecido, produtividade, escopo) =====================
    Write-Host '--- F: resultado gerencial / indicadores ---' -ForegroundColor Cyan
    try {
        # FECHAMENTO da colheita: contrato null<>zero nos indicadores (exige seasonId E cutoffDate)
        $cutoffDate = (Get-Date).AddDays(1).ToUniversalTime().ToString('yyyy-MM-dd')
        $closing = Invoke-Api GET "/api/v1/harvest/closing?seasonId=${seasonId}&cutoffDate=${cutoffDate}" -Token $adminToken
        $inds = @($closing.Body.indicators)
        Assert-Step 'F' 'fechamento-responde' ($closing.Status -eq 200 -and $inds.Count -gt 0) "HTTP $($closing.Status) indicadores=$($inds.Count) RAW=$($closing.Raw)"
        $unavail = @($inds | Where-Object { "$($_.availability)" -eq 'UNAVAILABLE' })
        $unavailIsZero = @($unavail | Where-Object { ($null -ne $_.value) -and ("$($_.value)" -eq '0' -or [double]($_.value) -eq 0) })
        Assert-Step 'F' 'null-nao-zero' ($unavailIsZero.Count -eq 0) "UNAVAILABLE=$($unavail.Count) que usaram 0 em vez de null=$($unavailIsZero.Count)"
        # custo realizado vem da fonte RECONHECIDA (cost_management_entries.recognized_amount), nao do legado
        $dash = Invoke-Api GET '/api/agriculture/dashboard' -Token $adminToken
        $recognizedDb = Invoke-Psql "select coalesce(sum(e.recognized_amount),0)::text from agro360.cost_management_entries e where e.tenant_id='${tenantId}';"
        $recognizedApi = "$($dash.Body.recognizedCost)"
        $recognizedOk = if ($recognizedApi -eq '' -or $null -eq $dash.Body.recognizedCost) { $false } else { [math]::Abs([decimal]$recognizedApi - [decimal]$recognizedDb) -lt [decimal]0.0001 }
        Assert-Step 'F' 'custo-realizado-da-fonte-reconhecida' ($dash.Status -eq 200 -and $recognizedOk) "api=$recognizedApi db(recognized_amount)=$recognizedDb RAW=$($dash.Raw)"
        # produtividade/custo por hectare respeita area executada (null quando sem area, nao zero)
        $execArea = Invoke-Psql "select coalesce(sum(o.covered_area_ha),0)::text from agro360.agriculture_operation_orders o join agro360.agriculture_records w on w.id=o.work_order_id and w.tenant_id=o.tenant_id where o.tenant_id='${tenantId}' and w.module='work-orders' and w.status='COMPLETED';"
        $cph = "$($dash.Body.costPerHectare)"
        if ([decimal]$execArea -le 0) {
            Assert-Step 'F' 'custo-hectare-null-sem-area' (($cph -eq '' -or $null -eq $dash.Body.costPerHectare)) "execArea=$execArea cph=$cph (esperado null)"
        } else {
            Assert-Step 'F' 'custo-hectare-respeita-area' (($cph -ne '' -and $null -ne $dash.Body.costPerHectare)) "execArea=$execArea cph=$cph"
        }
        # fazenda SEM autorizacao e excluida do resultado escopado
        $dashAll = Invoke-Api GET '/api/agriculture/dashboard' -Token $adminToken
        $dashScoped = Invoke-Api GET '/api/agriculture/dashboard' -Token $adminToken -FarmId $farmB
        Assert-Step 'F' 'escopo-x-farm-aplicavel' ($dashScoped.Status -eq 200) "all.recognized=$($dashAll.Body.recognizedCost) scoped(farmB).recognized=$($dashScoped.Body.recognizedCost)"
    } catch { Note-Blocked 'F' 'secao' $_ }

    # ===================== RESUMO =====================
    $summary = [ordered]@{
        Pass    = $script:pass
        Fail    = $script:fail
        Blocked = $script:blocked
        Resultado = if ($script:fail -eq 0 -and $script:blocked -eq 0) { 'APROVADO' } elseif ($script:fail -eq 0) { 'APROVADO_COM_BLOQUEIOS' } else { 'FALHOU' }
    }
    $summaryTxt = "BLOCO A - JOURNEY E2E - resumo ({0:s})`n`n" -f (Get-Date)
    $summaryTxt += ("Pass={0} Fail={1} Blocked={2} => {3}`n`n" -f $script:pass, $script:fail, $script:blocked, $summary.Resultado)
    foreach ($r in $script:results) { $summaryTxt += ("{0,-8} {1,-6} {2} -- {3}`n" -f $r.Status, $r.Scenario, $r.Step, $r.Details) }
    $summaryTxt | Set-Content (Join-Path $evidenceDir 'result.txt')
    $summaryTxt | Set-Content (Join-Path $evidenceDir 'result.txt')
    Write-Host ""
    Write-Host ("BLOCO A: Pass={0} Fail={1} Blocked={2} => {3}" -f $script:pass, $script:fail, $script:blocked, $summary.Resultado) -ForegroundColor $(if ($summary.Resultado -like 'FALHOU*') { 'Red' } else { 'Green' })
    Write-Host "Evidencia: $evidenceDir" -ForegroundColor Green

    $script:exitCode = if ($script:fail -eq 0) { 0 } else { 1 }
}
catch {
    Write-Host ""
    Write-Host "BLOCO A: SETUP/CENARIOS FALHARAM - $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor DarkRed
    Write-Host "Evidencia: $evidenceDir" -ForegroundColor Red
    $script:exitCode = 1
}
finally {
    foreach ($p in $processes) { try { if ($null -ne $p -and -not $p.HasExited) { $p.Kill() } } catch {} }
    if (-not $KeepRunning -and $weStartedCluster) { try { & $pg_ctl -D $templateDir -m fast stop | Out-Null } catch {} }
    Stop-Transcript | Out-Null
    if ($script:exitCode) { exit $script:exitCode } else { exit 0 }
}
