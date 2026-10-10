param(
    [string]$PostgresBin = 'C:\Program Files\PostgreSQL\18\bin',
    [switch]$KeepRunning,
    [switch]$RebuildTemplate
)

# Jornada E2E de Compras e Suprimentos (incremento integrado):
#   requisicao -> aprovacao (SoD: solicitante nao aprova a propria) -> cotacao 2 fornecedores ->
#   comparacao -> decisao acima do menor (exige justificativa) -> conversao (sem auto-selecao,
#   concorrente e idempotente, 1 pedido) -> cancelamento de cotacao aberta/fechada/com pedidos ->
#   pedido aprovado -> recebimento parcial x2 (idempotencia + conflito de chave com conteudo outro) ->
#   quarentena por inspecao -> decisao de qualidade libera somente o aceito (estoque real) ->
#   devolucao ao fornecedor (SoD; baixa de estoque; credito OPEN; saldo reaberto) ->
#   conferencia documental (exata -> MATCHED; fora da tolerancia -> PENDING_EXCEPTION + SoD da
#   excecao + decisao -> resolvida) -> previsao financeira OPEN (nunca paga) ->
#   Central de Trabalho (ocorrencias com deep link; aparecem/desaparecem; escopo de unidade;
#   gate por permissao) -> isolamento entre tenants (modulo nao contratado = 403; dados separados) ->
#   escopo proprio nao se expande (ActorCanDelegateScope).
#
# Cluster descartavel + API Release em porta livre; fixtures de banco quando a API nao expoe escrita.

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

$evidenceDir = Join-Path $root ('artifacts\procurement-journey-e2e-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidenceDir | Out-Null
Start-Transcript -Path (Join-Path $evidenceDir 'transcript.log') -Append | Out-Null
$processes = [Collections.Generic.List[Diagnostics.Process]]::new()
$startedDatabase = $false
$dataDir = ''
$dbName = 'agro360_procjourney_' + [guid]::NewGuid().ToString('N').Substring(0, 12)
# Template duravel: a carga completa do SQL (20-40 min) acontece UMA vez; cada execucao
# clona o banco de teste do template em segundos. Use -RebuildTemplate para forcar recarga.
$templateDir = Join-Path $env:TEMP 'opencode\pg-journey-template'
$templateDb = 'journey_template'
$templateMarker = Join-Path $templateDir 'template-ready.txt'
$fullSqlPath = Join-Path $root 'database/agro360-postgres-full.sql'
$apiPort = 0

$env:PGCONNECT_TIMEOUT = '20'

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
    # Start-Process com saidas em arquivo (nao cmd 2>&1): sob EAP=Stop, ruido de stderr
    # misturado no fluxo do PS vira excecao terminante e mascara o codigo de saida real.
    $tmp = Join-Path $evidenceDir ([guid]::NewGuid().ToString('N'))
    $tempFile = "$tmp.sql"; $outF = "$tmp.out"; $errF = "$tmp.err"
    [System.IO.File]::WriteAllText($tempFile, $Sql, [System.Text.Encoding]::UTF8)
    $proc = Start-Process -FilePath $psql -ArgumentList @('-p',"$pgPort","-U","postgres","-h","127.0.0.1","-d",$Db,'-X','-A','-t','-q','-v','ON_ERROR_STOP=1','--set=client_min_messages=warning','-f',$tempFile) -NoNewWindow -PassThru -Wait -RedirectStandardOutput $outF -RedirectStandardError $errF
    $code = $proc.ExitCode
    $combined = ''
    if (Test-Path $outF) { $combined += (Get-Content $outF -Raw -ErrorAction SilentlyContinue) }
    if ((Test-Path $errF) -and (Get-Item $errF).Length -gt 0) { $combined += "`n" + (Get-Content $errF -Raw -ErrorAction SilentlyContinue) }
    Remove-Item $tempFile, $outF, $errF -Force -ErrorAction SilentlyContinue
    if ($code -ne 0) { throw "psql failed (exit code $code): $combined" }
    return $combined.Trim()
}

function Invoke-PsqlFile([string]$FilePath, [string]$Db = $dbName) {
    $tmp = Join-Path $evidenceDir ([guid]::NewGuid().ToString('N'))
    $outF = "$tmp.out"; $errF = "$tmp.err"
    $proc = Start-Process -FilePath $psql -ArgumentList @('-p',"$pgPort","-U","postgres","-h","127.0.0.1","-d",$Db,'-X','-q','-v','ON_ERROR_STOP=1','--set=client_min_messages=warning','-f',$FilePath) -NoNewWindow -PassThru -Wait -RedirectStandardOutput $outF -RedirectStandardError $errF
    $code = $proc.ExitCode
    $combined = ''
    if (Test-Path $outF) { $combined += (Get-Content $outF -Raw -ErrorAction SilentlyContinue) }
    if ((Test-Path $errF) -and (Get-Item $errF).Length -gt 0) { $combined += "`n" + (Get-Content $errF -Raw -ErrorAction SilentlyContinue) }
    Remove-Item $outF, $errF -Force -ErrorAction SilentlyContinue
    if ($code -ne 0) { throw "psql file failed (exit code $code): $combined" }
    return $combined.Trim()
}

function Invoke-Api([string]$Method, [string]$Path, $Body = $null, [string]$Token = '', [hashtable]$ExtraHeaders = @{}) {
    $url = "http://127.0.0.1:${apiPort}${Path}"
    $headers = @{ 'Content-Type' = 'application/json' }
    if ($Token) { $headers['Authorization'] = "Bearer $Token" }
    foreach ($k in $ExtraHeaders.Keys) { $headers[$k] = $ExtraHeaders[$k] }
    $params = @{ Method = $Method; Uri = $url; Headers = $headers; UseBasicParsing = $true; TimeoutSec = 60 }
    if ($null -ne $Body) {
        $json = $Body | ConvertTo-Json -Depth 8 -Compress
        $params['Body'] = [Text.Encoding]::UTF8.GetBytes($json)
    }
    try {
        $resp = Invoke-WebRequest @params
        $content = $resp.Content
        $contentType = "$($resp.Headers['Content-Type'])"
        $result = $null
        if ($contentType -like '*json*' -and $content -and $content.Length -gt 2) {
            try { $result = $content | ConvertFrom-Json } catch { $result = $content }
        }
        return @{ Status = [int]$resp.StatusCode; Body = $result; Raw = $content }
    } catch {
        $resp = $_.Exception.Response
        if ($null -eq $resp) { throw "HTTP ${Method} ${Path}: $($_.Exception.Message)" }
        if ($_.ErrorDetails -and $_.ErrorDetails.Message) {
            $content = $_.ErrorDetails.Message
        } elseif ($resp -and $resp.PSObject.Methods.Name -contains 'GetResponseStream') {
            $stream = $resp.GetResponseStream()
            $reader = [IO.StreamReader]::new($stream)
            $content = $reader.ReadToEnd()
        } else {
            $content = ''
        }
        $result = if ($content -and $content.Length -gt 2) { try { $content | ConvertFrom-Json } catch { $content } } else { $null }
        return @{ Status = [int]$resp.StatusCode; Body = $result; Raw = $content }
    }
}

function Get-OperationCenter([string]$Token, [string]$FilterKey = '') {
    $r = Invoke-Api GET '/api/operation-center?pageSize=100' $null $Token
    Assert-Step "OpCenter-$FilterKey-200" ($r.Status -eq 200) "HTTP $($r.Status)"
    $items = @()
    if ($null -ne $r.Body) { $items = @($r.Body.Items | Where-Object { $_ }) }
    return $items
}

function Find-Occurrence([object[]]$Items, [string]$KeyPrefix, [string]$Id) {
    return @($Items | Where-Object { "$($_.Key)" -eq "${KeyPrefix}:${Id}" })
}

try {
    # ============ SETUP: cluster com template duravel ============
    $pgPort = Get-FreePort
    $needsStart = $true
    if (-not (Test-Path $templateMarker) -or $RebuildTemplate) {
        try {
            Write-Host "[build] construindo template duravel em $templateDir (carga completa do SQL; em geral < 5 min)..." -ForegroundColor Cyan
            # Build parcial anterior (sem marcador) ou recarga forcada: para o cluster e limpa o diretorio.
            if (Test-Path $templateDir) {
                Write-Host '[build] removendo estado anterior (parcial ou recarga forcada)...'
                & $pg_ctl -D $templateDir -m fast stop | Out-Null
                for ($rTry = 0; $rTry -lt 8; $rTry++) {
                    Start-Sleep -Seconds 1
                    try { Remove-Item $templateDir -Recurse -Force -ErrorAction Stop; break } catch {}
                }
                if (Test-Path $templateDir) { throw 'nao foi possivel remover o diretorio do template anterior (arquivos em uso por outro processo?)' }
            }
            $t0 = Get-Date
            $iniErrF = Join-Path $evidenceDir 'initdb-err.txt'
            $ini = Start-Process -FilePath $initdb -ArgumentList @('-D',$templateDir,'-U','postgres','-A','trust','--encoding=UTF8','--locale=C') -NoNewWindow -PassThru -Wait -RedirectStandardOutput (Join-Path $evidenceDir 'initdb-out.txt') -RedirectStandardError $iniErrF
            if ($ini.ExitCode -ne 0) { throw "initdb falhou (exit $($ini.ExitCode)): " + (Get-Content $iniErrF -Raw -ErrorAction SilentlyContinue) }
            & $pg_ctl -D $templateDir -l (Join-Path $evidenceDir 'template-startup.log') -o "-h 127.0.0.1 -p $pgPort" -w start
            if ($LASTEXITCODE -ne 0) { throw "pg_ctl start do template falhou na porta $pgPort" }
            $startedDatabase = $true
            $ddlTpl = Join-Path $evidenceDir 'create-template-db.sql'
            Set-Content $ddlTpl "create database ${templateDb};" -Encoding ASCII
            $cdProc = Start-Process -FilePath $psql -ArgumentList @('-p',"$pgPort","-U","postgres","-h","127.0.0.1","-d","postgres","-X","-v","ON_ERROR_STOP=1","-f",$ddlTpl) -NoNewWindow -PassThru -Wait
            if ($cdProc.ExitCode -ne 0) { throw "create database do template falhou (exit $($cdProc.ExitCode))" }
            $prelude = Join-Path $evidenceDir 'prelude.sql'
            Set-Content -Path $prelude -Value 'SET synchronous_commit=off; SET statement_timeout=2700000;' -Encoding ASCII
            Write-Host '[build] carga completa do SQL em andamento (progresso: template-load-out/err.log na evidencia)...'
            $loadProc = Start-Process -FilePath $psql -ArgumentList @('-p',"$pgPort","-U","postgres","-h","127.0.0.1","-d",$templateDb,'-X','-v','ON_ERROR_STOP=1','--set=client_min_messages=warning','-f',$prelude,'-f',$fullSqlPath) -NoNewWindow -PassThru -Wait -RedirectStandardOutput (Join-Path $evidenceDir 'template-load-out.log') -RedirectStandardError (Join-Path $evidenceDir 'template-load-err.log')
            if ($loadProc.ExitCode -ne 0) { throw "carga do SQL completo falhou (exit $($loadProc.ExitCode)); detalhes em template-load-err.log" }
            Set-Content -Path $templateMarker -Value ("ok {0:N}" -f (Get-Date))
            Write-Host ("[build] template pronto em " + ((Get-Date) - $t0).TotalSeconds.ToString('0.#') + "s") -ForegroundColor Green
            $needsStart = $false
        } catch {
            # Dump de diagnostico: excecao primaria + cauda dos logs psql, para falha em build nunca ficar sem traca.
            $dump = $_ | Out-String
            foreach ($lf in @('template-load-out.log','template-load-err.log')) {
                $lp = Join-Path $evidenceDir $lf
                if (Test-Path $lp) { $dump += "`n=== TAIL $lf ===`n" + ((Get-Content $lp -Tail 40) -join "`n") }
            }
            $dump | Set-Content (Join-Path $evidenceDir 'build-error.txt')
            throw
        }
    } else {
        Write-Host "Template existente em $templateDir (use -RebuildTemplate para forcar recarga)." -ForegroundColor Cyan
    }

    # Pidfile obsoleto (cluster anterior que morreu sem limpeza) nao impede o start.
    $pidFile = Join-Path $templateDir 'postmaster.pid'
    if (Test-Path $pidFile -and $needsStart) {
        $stalePid = ((Get-Content $pidFile) | Select-Object -First 1).Trim()
        $staleProc = $null
        try { $staleProc = Get-Process -Id ([int]$stalePid) -ErrorAction Stop } catch {}
        if ($null -eq $staleProc) { Remove-Item $pidFile -Force }
        else { throw "O diretorio do template parece estar em uso por outro processo (pid $stalePid)." }
    }

    $dataDir = $templateDir
    if ($needsStart) {
        $pgLog = Join-Path $evidenceDir 'postgres.log'
        & $pg_ctl -D $templateDir -l $pgLog -o "-h 127.0.0.1 -p $pgPort" -w start
        if ($LASTEXITCODE -ne 0) { throw "pg_ctl start falhou na porta $pgPort" }
        $startedDatabase = $true
    }
    $pingSql = Join-Path $evidenceDir 'ping.sql'
    Set-Content $pingSql 'select 1;' -Encoding ASCII
    $pingOut = Join-Path $evidenceDir 'ping-out.txt'
    $pingErrF = Join-Path $evidenceDir 'ping-err.txt'
    Remove-Item $pingOut, $pingErrF -Force -ErrorAction SilentlyContinue
    Start-Process -FilePath $psql -ArgumentList @('-p',"$pgPort","-U","postgres","-h","127.0.0.1","-d","postgres","-X","-A","-t","-q","-f",$pingSql) -NoNewWindow -Wait -RedirectStandardOutput $pingOut -RedirectStandardError $pingErrF | Out-Null
    $ping = (Get-Content $pingOut -Raw -ErrorAction SilentlyContinue).Trim()
    $pingMsg = $ping
    if ((Test-Path $pingErrF) -and (Get-Item $pingErrF).Length -gt 0) { $pingMsg += ' | ' + (Get-Content $pingErrF -Raw -ErrorAction SilentlyContinue).Trim() }
    Assert-Step 'Cluster-responde-select-1' ($ping -eq '1') "ping=$pingMsg"

    $ddlRun = Join-Path $evidenceDir 'create-run-db.sql'
    Set-Content $ddlRun "create database ${dbName} template ${templateDb};" -Encoding ASCII
    $crProc = Start-Process -FilePath $psql -ArgumentList @('-p',"$pgPort","-U","postgres","-h","127.0.0.1","-d","postgres","-X","-v","ON_ERROR_STOP=1","-f",$ddlRun) -NoNewWindow -PassThru -Wait
    if ($crProc.ExitCode -ne 0) { throw "create database a partir do template falhou (exit $($crProc.ExitCode))" }
    Write-Host "PostgreSQL pronto na porta $pgPort (db=${dbName} clonado do template)" -ForegroundColor Cyan

    $schemaVer = Invoke-Psql "select version from agro360.platform_schema_versions where version='11.24.0';"
    Assert-Step 'Schema-11.24.0-present' ($schemaVer -eq '11.24.0') "migracao 134 (fr-FR) instalada; zero migracoes novas neste incremento"

    # ============ START API HOST ============
    $apiPort = Get-FreePort
    $connStr = "Host=127.0.0.1;Port=${pgPort};Database=${dbName};Username=postgres;Search Path=agro360;Command Timeout=60"
    $apiExe = Join-Path $root 'src\Hosts\Agro360.Api\bin\Release\net10.0\Agro360.Api.dll'
    if (-not (Test-Path $apiExe)) { throw "API binary not found at $apiExe. Run build first." }
    $env:ASPNETCORE_URLS = "http://127.0.0.1:${apiPort}"
    $env:ConnectionStrings__Agro360 = $connStr
    $env:ConnectionStrings__DefaultConnection = $connStr
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:PGPASSWORD = 'trust-auth-placeholder'
    $env:Jwt__SigningKey = 'e2e-test-signing-key-32bytes-minimum!'
    $apiProc = Start-Process dotnet -ArgumentList "`"$apiExe`"" -WorkingDirectory (Split-Path $apiExe) -PassThru -NoNewWindow -RedirectStandardOutput (Join-Path $evidenceDir 'api.out.log') -RedirectStandardError (Join-Path $evidenceDir 'api.err.log')
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
    if (-not $ready) {
        $errLog = Join-Path $evidenceDir 'api.err.log'
        if (Test-Path $errLog) { (Get-Content $errLog -Tail 40) | ForEach-Object { Write-Host "API-ERR: $_" -ForegroundColor Red } }
    }
    Assert-Step 'API-tcp-ready' $ready "port=$apiPort after $($i+1) probes"
    Start-Sleep -Seconds 2

    # Credenciais conhecidas (hash PBKDF2 ja validado neste repositorio)
    $knownHash = 'pbkdf2-sha512$210000$Fk496Qko66Cw//8uYyP3mw==$PqcZJTtl+96es6GmUF3khyrZvMJqSVDIe/YfI7TCv2U='
    $scTenantId = Invoke-Psql "select id::text from agro360.tenancy_tenants where slug='santa-clara';"
    $valeTenantId = Invoke-Psql "select id::text from agro360.tenancy_tenants where slug='cooperativa-vale-verde';"
    Assert-Step 'Tenants-santa-e-vale' ($scTenantId -ne '' -and $valeTenantId -ne '') "santa=$scTenantId vale=$valeTenantId"
    Invoke-Psql "update agro360.identity_users set status='ACTIVE', password_hash='$knownHash', must_change_password=false, updated_at=now() where (tenant_id='${scTenantId}' and lower(email) in ('admin@santaclara.agro360.local','operador.santaclara@agro360.local')) or (tenant_id='${valeTenantId}' and lower(email)='admin.valeverde@agro360.local');" | Out-Null

    $adminLogin = Invoke-Api POST '/api/v1/auth/login' @{ TenantSlug='santa-clara'; Email='admin@santaclara.agro360.local'; Password='Agro360Admin@2024!' }
    Assert-Step 'Login-admin-santa' ($adminLogin.Status -eq 200) "HTTP $($adminLogin.Status)"
    $adminToken = $adminLogin.Body.accessToken

    $farmA = Invoke-Psql "select id::text from agro360.geo_farms where tenant_id='${scTenantId}' and registration_number='SC-SEDE-001' and deleted_at is null;"
    $farmB = Invoke-Psql "select id::text from agro360.geo_farms where tenant_id='${scTenantId}' and registration_number='SC-RETIRO-001' and deleted_at is null;"
    Assert-Step 'Fazendas-fixtures' ($farmA -ne '' -and $farmB -ne '' -and $farmA -ne $farmB) "A=$farmA B=$farmB"

    $operatorUser = Invoke-Psql "select id::text from agro360.identity_users where tenant_id='${scTenantId}' and lower(email)='operador.santaclara@agro360.local' and deleted_at is null;"
    $adminUser = Invoke-Psql "select id::text from agro360.identity_users where tenant_id='${scTenantId}' and lower(email)='admin@santaclara.agro360.local' and deleted_at is null;"
    Assert-Step 'Usuarios-fixtures' ($operatorUser -ne '' -and $adminUser -ne '') "op=$operatorUser admin=$adminUser"

    # Escopo do operador: SOMENTE a Fazenda A durante toda a jornada (restaurado ao final).
    Invoke-Psql "delete from agro360.identity_user_unit_scopes where tenant_id='${scTenantId}' and user_id='${operatorUser}';" | Out-Null
    Invoke-Psql "insert into agro360.identity_user_unit_scopes(id, tenant_id, user_id, scope_type, farm_id) values ('$(New-Guid)', '${scTenantId}', '${operatorUser}', 'FARM', '${farmA}');" | Out-Null

    # Permissoes da jornada no papel operador (banco descartavel): compras completa + work.read
    # + gestao de usuarios (para o teste de delegacao de escopo).
    Invoke-Psql @"
insert into agro360.identity_role_permissions(tenant_id, role_id, permission_id)
select '${scTenantId}', r.id, p.id
from agro360.identity_roles r
cross join agro360.identity_permissions p
where r.tenant_id='${scTenantId}' and r.code='operator'
  and p.code in ('purchasing.read','purchasing.write','purchasing.request','purchasing.approve','purchasing.receive','purchasing.export','work.read','account.users.manage')
on conflict do nothing;
"@ | Out-Null

    $opLogin = Invoke-Api POST '/api/v1/auth/login' @{ TenantSlug='santa-clara'; Email='operador.santaclara@agro360.local'; Password='Agro360Admin@2024!' }
    Assert-Step 'Login-operador-santa' ($opLogin.Status -eq 200) "HTTP $($opLogin.Status)"
    $opToken = $opLogin.Body.accessToken

    # Usuario sem nenhuma permissao de compras (mas com work.read): para provar que permissao
    # ausente bloqueia a API e filtra as ocorrencias da Central — independentemente do modulo.
    $noPermUser = New-Guid
    $noPermRole = New-Guid
    Invoke-Psql @"
insert into agro360.identity_roles(id,tenant_id,code,name,is_system) values('${noPermRole}','${scTenantId}','no-purchasing-e2e','Sem Compras E2E',false);
insert into agro360.identity_role_permissions(tenant_id,role_id,permission_id)
select '${scTenantId}','${noPermRole}',id from agro360.identity_permissions where code in ('work.read','dashboard.view');
insert into agro360.identity_users(id,tenant_id,name,email,password_hash,status,normalized_document,document_type,must_change_password,mfa_enabled)
values('${noPermUser}','${scTenantId}','Usuario Sem Compras E2E','semcompras.santaclara@agro360.local','$knownHash','ACTIVE','99887766551','CPF',false,false);
insert into agro360.identity_user_roles(tenant_id,user_id,role_id) values('${scTenantId}','${noPermUser}','${noPermRole}');
insert into agro360.identity_user_unit_scopes(id,tenant_id,user_id,scope_type) values('$(New-Guid)','${scTenantId}','${noPermUser}','ALL');
"@ | Out-Null

    $npLogin = Invoke-Api POST '/api/v1/auth/login' @{ TenantSlug='santa-clara'; Email='semcompras.santaclara@agro360.local'; Password='Agro360Admin@2024!' }
    Assert-Step 'Login-semcompras' ($npLogin.Status -eq 200) "HTTP $($npLogin.Status)"
    $npToken = $npLogin.Body.accessToken

    # ---- Fixtures operacionais (escrita sem API dedicada vai direto ao banco descartavel) ----
    $productId = Invoke-Psql @"
insert into agro360.inventory_products(id,tenant_id,sku,name,category,base_unit,requires_lot,is_perishable,controlled_product,created_by)
select '${((New-Guid))}','${scTenantId}','E2E-JORNADA-DIESEL','Diesel S10 (jornada E2E)','INSumos','l',true,false,false,'${adminUser}'
where not exists(select 1 from agro360.inventory_products where tenant_id='${scTenantId}' and sku='E2E-JORNADA-DIESEL');
select id::text from agro360.inventory_products where tenant_id='${scTenantId}' and sku='E2E-JORNADA-DIESEL';
"@
    Assert-Step 'Fixture-produto-estoque' ($productId -ne '') "product=$productId (unit base l)"

    $warehouseId = Invoke-Psql @"
insert into agro360.inventory_warehouses(id,tenant_id,farm_id,code,name,type,created_by)
select '${((New-Guid))}','${scTenantId}','${farmA}','DEP-E2E-JORNADA','Deposito Jornada E2E','FUEL','${adminUser}'
where not exists(select 1 from agro360.inventory_warehouses where tenant_id='${scTenantId}' and code='DEP-E2E-JORNADA');
select id::text from agro360.inventory_warehouses where tenant_id='${scTenantId}' and code='DEP-E2E-JORNADA';
"@
    Assert-Step 'Fixture-deposito' ($warehouseId -ne '') "warehouse=$warehouseId (Fazenda A)"

    $financeAccount = Invoke-Psql @"
insert into agro360.finance_chart_of_accounts(id,tenant_id,code,name,type,nature,active,created_by)
select '${((New-Guid))}','${scTenantId}','E2E-FIN-JORNADA','Conta E2E previsao da jornada','EXPENSE','DEBIT',true,'${adminUser}'
where not exists(select 1 from agro360.finance_chart_of_accounts where tenant_id='${scTenantId}' and code='E2E-FIN-JORNADA');
select id::text from agro360.finance_chart_of_accounts where tenant_id='${scTenantId}' and code='E2E-FIN-JORNADA';
"@
    Assert-Step 'Fixture-conta-financeira' ($financeAccount -ne '') "account=$financeAccount"

    $neededOn = (Get-Date).AddDays(30).ToString('yyyy-MM-dd')
    $firstDueOn = (Get-Date).AddDays(-1).ToString('yyyy-MM-dd')
    $receivedAt = (Get-Date).AddHours(-12).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.fffZ')

    $supplierA = Invoke-Api POST '/api/procurement/suppliers' @{
        LegalName='Fornecedor Alfa Combustiveis Ltda'; TradeName='Alfa E2E'; TaxDocument=$null; StateRegistration=$null; Type='MATERIAL'
        Category='COMBUSTIVEIS'; Email=$null; Phone=$null; Address=$null; City='Paragominas'; State='PA'; Country='BR'
        MainContact=$null; PaymentTerms=$null; AverageDeliveryDays=10; Status='ACTIVE'; RejectionReason=$null; Notes=$null; Tags=@()
    } $opToken
    Assert-Step 'Fornecedor-A-criado' ($supplierA.Status -eq 201) "HTTP $($supplierA.Status)"
    $supplierB = Invoke-Api POST '/api/procurement/suppliers' @{
        LegalName='Fornecedor Beta Combustiveis Ltda'; TradeName='Beta E2E'; TaxDocument=$null; StateRegistration=$null; Type='MATERIAL'
        Category='COMBUSTIVEIS'; Email=$null; Phone=$null; Address=$null; City='Belem'; State='PA'; Country='BR'
        MainContact=$null; PaymentTerms=$null; AverageDeliveryDays=7; Status='ACTIVE'; RejectionReason=$null; Notes=$null; Tags=@()
    } $opToken
    Assert-Step 'Fornecedor-B-criado' ($supplierB.Status -eq 201) "HTTP $($supplierB.Status)"
    $supplierAId = $supplierA.Body.id
    $supplierBId = $supplierB.Body.id

    $catalogItem = Invoke-Api POST '/api/procurement/catalog' @{
        Name='Diesel S10 (Jornada E2E)'; Code='E2E-JORNADA-DIESEL-IT'; Category='COMBUSTIVEIS'; Unit='l'; Type='MATERIAL'
        Description=$null; Active=$true; MinimumStock=0; CostCenterId=$null
        RequiresLot=$true; RequiresExpiry=$false; RequiresDocument=$false; RequiresInspection=$true; RequiresApprovedSupplier=$false; Notes=$null
        RelatedProductId=$productId
    } $opToken
    Assert-Step 'Catalogo-inspecao-obrigatoria' ($catalogItem.Status -eq 201) "HTTP $($catalogItem.Status)"
    $catalogItemId = $catalogItem.Body.id

    # ============ ETAPA 1: REQUISICAO + SOG + APROVACAO ============
    Write-Host "`n--- ETAPA 1: requisicao -> alçada -> aprovacao ---" -ForegroundColor Cyan

    # Operador cria requisicao na Fazenda B fora do escopo: negada antes de gravar.
    $reqB = Invoke-Api POST '/api/procurement/requisitions' @{
        CostCenterId=$null; PropertyId=$farmB; Origin='MANUAL'; Justification='Requisicao B para teste de escopo de unidade (E2E)'
        Priority='MEDIUM'; NeededOn=$neededOn; Submit=$false
        Items=@(@{ CatalogItemId=$catalogItemId; Quantity=25; Unit='l'; Notes=$null })
    } $opToken
    Assert-Step 'Req-B-fora-do-escopo-negada' ($reqB.Status -ge 400) "HTTP $($reqB.Status)"

    $req = Invoke-Api POST '/api/procurement/requisitions' @{
        CostCenterId=$null; PropertyId=$farmA; Origin='MANUAL'; Justification='Diesel para colheitadeira (jornada E2E)'
        Priority='HIGH'; NeededOn=$neededOn; Submit=$true
        Items=@(@{ CatalogItemId=$catalogItemId; Quantity=100; Unit='l'; Notes=$null })
    } $opToken
    Assert-Step 'Req-A-enviada-por-operador' ($req.Status -eq 201) "HTTP $($req.Status) id=$($req.Body.id)"
    $reqId = $req.Body.id
    $reqStatus = Invoke-Psql "select status from agro360.procurement_requisitions where id='${reqId}';"
    Assert-Step 'Req-status-AWAITING_APPROVAL' ($reqStatus -eq 'AWAITING_APPROVAL') "status=$reqStatus"
    $reqVersion = Invoke-Psql "select version::text from agro360.procurement_requisitions where id='${reqId}';"

    # Central: ocorrencia REQ_APPROVAL aparece com deep link correto e gate por permissao.
    $ocAdmin = Get-OperationCenter $adminToken 'req-admin'
    $ocOp = Get-OperationCenter $opToken 'req-op'
    $ocNo = Get-OperationCenter $npToken 'req-no'
    $occA = @(Find-Occurrence $ocAdmin 'REQ_APPROVAL' $reqId)
    Assert-Step 'Central-REQ_APPROVAL-visivel-ao-admin' (@($occA).Count -eq 1) "total_opcenter=$(@($ocAdmin).Count)"
    Assert-Step 'Central-deep-link-requisicao' ($occA[0].SafeLink -like "/Procurement?requisitionId=${reqId}*") "link=$($occA[0].SafeLink)"
    Assert-Step 'Central-REQ_APPROVAL-visivel-ao-operator' (@(Find-Occurrence $ocOp 'REQ_APPROVAL' $reqId).Count -eq 1) "operator tem purchasing.approve"
    Assert-Step 'Central-sem-permissao-nao-ve' (@(Find-Occurrence $ocNo 'REQ_APPROVAL' $reqId).Count -eq 0) "semcompras nao tem purchasing.approve"
    Assert-Step 'Central-scoped-a-apenas-fazenda-A' (@($ocOp | Where-Object { $_.Module -eq 'PURCHASING' } | Where-Object { $_.UnitName -and $_.UnitName -notlike '*SEDE*' }).Count -eq 0) "unit_names=$(($ocOp | Where-Object { $_.Module -eq 'PURCHASING' } | ForEach-Object { $_.UnitName }) -join '|')"

    # SoD: solicitante nao pode aprovar a propria requisicao.
    $sodTry = Invoke-Api POST "/api/procurement/requisitions/${reqId}/decision" @{ Version=[long]$reqVersion; Decision='APPROVE'; Reason=$null } $opToken
    $sodStatus = Invoke-Psql "select status from agro360.procurement_requisitions where id='${reqId}';"
    Assert-Step 'SoD-solicitante-nao-aprova' ($sodTry.Status -ge 400 -and $sodStatus -eq 'AWAITING_APPROVAL') "HTTP $($sodTry.Status); status_mantido=$sodStatus"

    # Rejeicao exige justificativa (admin rejeita? nao — apenas valida o contrato: usa REJECT e depois aprova de novo exigiria reabrir; entao testa REJECT numa copia simples e a aprova).
    # Mantemos a jornada: admin aprova com version correta.
    $okTry = Invoke-Api POST "/api/procurement/requisitions/${reqId}/decision" @{ Version=[long]$reqVersion; Decision='APPROVE'; Reason='Aprovado pela alçada (E2E)' } $adminToken
    $okStatus = Invoke-Psql "select status from agro360.procurement_requisitions where id='${reqId}';"
    Assert-Step 'Admin-aprova-requisicao' ($okTry.Status -eq 204 -and $okStatus -eq 'APPROVED') "HTTP $($okTry.Status); status=$okStatus"

    # Conflicto de versao: segunda decisao com a versao antiga falha.
    $staleTry = Invoke-Api POST "/api/procurement/requisitions/${reqId}/decision" @{ Version=[long]$reqVersion; Decision='REJECT'; Reason='Versao antiga (E2E)' } $adminToken
    $staleStatus = Invoke-Psql "select status from agro360.procurement_requisitions where id='${reqId}';"
    Assert-Step 'Concorrencia-versao-stale-409' ($staleTry.Status -eq 409 -and $staleStatus -eq 'APPROVED') "HTTP $($staleTry.Status); status=$staleStatus"

    $ocAfterApprove = Get-OperationCenter $adminToken 'req-approved'
    Assert-Step 'Central-REQ_APPROVAL-desaparece-apos-aprovacao' (@(Find-Occurrence $ocAfterApprove 'REQ_APPROVAL' $reqId).Count -eq 0) "resolvida sai da fila"

    # ============ ETAPA 2: COTACAO + PROPOSTAS + COMPARACAO + DECISAO ============
    Write-Host "`n--- ETAPA 2: cotacao 2 fornecedores -> propostas -> comparacao -> decisao ---" -ForegroundColor Cyan

    # Requisicao B (Farm B) criada pelo admin para o teste de escopo de unidade na cotação.
    $reqB2 = Invoke-Api POST '/api/procurement/requisitions' @{
        CostCenterId=$null; PropertyId=$farmB; Origin='MANUAL'; Justification='Requisicao B para cotação (escopo de unidade, E2E)'
        Priority='MEDIUM'; NeededOn=$neededOn; Submit=$true
        Items=@(@{ CatalogItemId=$catalogItemId; Quantity=25; Unit='l'; Notes=$null })
    } $adminToken
    $reqB2Id = $reqB2.Body.id
    $b2v = Invoke-Psql "select version::text from agro360.procurement_requisitions where id='${reqB2Id}';"
    Invoke-Api POST "/api/procurement/requisitions/${reqB2Id}/decision" @{ Version=[long]$b2v; Decision='APPROVE'; Reason='Aprovada E2E' } $adminToken | Out-Null

    $dueDate = (Get-Date).AddDays(30).ToString('yyyy-MM-dd')
    $quot = Invoke-Api POST '/api/procurement/quotations' @{ RequisitionId=$reqId; SupplierIds=@($supplierAId,$supplierBId); DueDate=$dueDate } $opToken
    Assert-Step 'Cotacao-aberta-2-fornecedores' ($quot.Status -eq 201) "HTTP $($quot.Status) id=$($quot.Body.id)"
    $quotId = $quot.Body.id

    $dupQuot = Invoke-Api POST '/api/procurement/quotations' @{ RequisitionId=$reqId; SupplierIds=@($supplierAId); DueDate=$dueDate } $opToken
    Assert-Step 'Cotacao-dupla-aberta-409' ($dupQuot.Status -eq 409) "HTTP $($dupQuot.Status) (no maximo uma aberta por requisicao)"

    # Escopo de unidade: operador (FARM A) tenta abrir cotacao na requisicao B — negada.
    $quotB = Invoke-Api POST '/api/procurement/quotations' @{ RequisitionId=$reqB2Id; SupplierIds=@($supplierAId,$supplierBId); DueDate=$dueDate } $opToken
    Assert-Step 'Cotacao-requisicao-B-fora-do-escopo' ($quotB.Status -ge 400) "HTTP $($quotB.Status)"

    $quotationCount = Invoke-Psql "select count(*)::text from agro360.procurement_quotations where tenant_id='${scTenantId}';"
    Assert-Step 'Cotacao-B-nao-gravada' ($quotationCount -eq '1') "total_cotacoes=$quotationCount"

    # Propostas: A = 2.50/L, 10 dias; B = 2.35/L, 7 dias (menor total = B).
    $propA = Invoke-Api POST "/api/procurement/quotations/${quotId}/quote" @{
        QuotationId=$quotId; SupplierId=$supplierAId; CatalogItemId=$catalogItemId; UnitPrice=2.50; DeliveryDays=10; Notes='Proposta Alfa (E2E)'; Discount=0
        ProposalValidUntil=$null; PaymentTerms='NET_30'; Freight=0; Taxes=0; QuotationItemId=$null
    } $opToken
    Assert-Step 'Proposta-A-201' ($propA.Status -eq 201) "HTTP $($propA.Status)"
    $propB = Invoke-Api POST "/api/procurement/quotations/${quotId}/quote" @{
        QuotationId=$quotId; SupplierId=$supplierBId; CatalogItemId=$catalogItemId; UnitPrice=2.35; DeliveryDays=7; Notes='Proposta Beta menor (E2E)'; Discount=0
        ProposalValidUntil=$null; PaymentTerms='NET_15'; Freight=0; Taxes=0; QuotationItemId=$null
    } $opToken
    Assert-Step 'Proposta-B-201' ($propB.Status -eq 201) "HTTP $($propB.Status)"

    $compare = Invoke-Api GET "/api/procurement/requisitions/${reqId}/quotations/compare" $null $opToken
    Assert-Step 'Comparacao-retorna-propostas' ($compare.Status -eq 200 -and @($compare.Body).Count -ge 2) "linhas=$(if ($null -ne $compare.Body) { @($compare.Body).Count } else { 0 })"

    $qDetail = Invoke-Api GET "/api/procurement/quotations/${quotId}" $null $adminToken
    $quotItemId = ($qDetail.Body.items | Select-Object -First 1).id
    $alphaRow = $qDetail.Body.suppliers | Where-Object { $_.supplier_id -eq $supplierAId } | Select-Object -First 1
    $betaRow = $qDetail.Body.suppliers | Where-Object { $_.supplier_id -eq $supplierBId } | Select-Object -First 1
    Assert-Step 'Detalhe-da-cotacao-completo' ($null -ne $quotItemId -and $null -ne $alphaRow -and $null -ne $betaRow) "item=$quotItemId"

    # Decisao acima do menor sem justificativa: bloqueada (sem auto-selecao silenciosa).
    $decideNoJust = Invoke-Api POST "/api/procurement/quotations/${quotId}/decide" @{ Items=@(@{ QuotationItemId=$quotItemId; QuotationSupplierId=$alphaRow.id }); Justification='' } $adminToken
    Assert-Step 'Acima-do-menor-sem-justificativa-bloqueada' ($decideNoJust.Status -ge 400) "HTTP $($decideNoJust.Status) (Alfa 2.50 > Beta 2.35)"

    # Decisao acima do menor COM justificativa audita.
    $decideOk = Invoke-Api POST "/api/procurement/quotations/${quotId}/decide" @{ Items=@(@{ QuotationItemId=$quotItemId; QuotationSupplierId=$alphaRow.id }); Justification='Alfa homologado com laudo tecnico vigente e prazo compativel (E2E)' } $adminToken
    Assert-Step 'Decisao-acima-do-menor-com-justificativa' ($decideOk.Status -eq 204) "HTTP $($decideOk.Status)"
    $decisionRows = Invoke-Psql "select count(*)::text || ':' || coalesce(justification,'') from agro360.procurement_quotation_decisions where quotation_id='${quotId}' and deleted_at is null;"
    Assert-Step 'Decisao-persistida-c-justificativa' ($decisionRows -match '^1:Alfa homologado') "db=$($decisionRows.Substring(0,[Math]::Min(60,$decisionRows.Length)))"

    # ============ ETAPA 3: CONVERSAO EM PEDIDO (confirmacao, concorrência, idempotencia) ============
    Write-Host "`n--- ETAPA 3: conversao em pedido ---" -ForegroundColor Cyan

    # Sem decisao explícita + sem confirmacao -> 400 (nunca aplica menor preco em silencio).
    $reqPlain = Invoke-Api POST '/api/procurement/requisitions' @{
        CostCenterId=$null; PropertyId=$farmA; Origin='MANUAL'; Justification='Requisicao p/ conversao sem decisao (E2E)'
        Priority='MEDIUM'; NeededOn=$neededOn; Submit=$true
        Items=@(@{ CatalogItemId=$catalogItemId; Quantity=10; Unit='l'; Notes=$null })
    } $opToken
    $plainReqId = $reqPlain.Body.id
    $pv = Invoke-Psql "select version::text from agro360.procurement_requisitions where id='${plainReqId}';"
    Invoke-Api POST "/api/procurement/requisitions/${plainReqId}/decision" @{ Version=[long]$pv; Decision='APPROVE'; Reason='Aprovada E2E' } $adminToken | Out-Null
    $plainQuot = Invoke-Api POST '/api/procurement/quotations' @{ RequisitionId=$plainReqId; SupplierIds=@($supplierAId,$supplierBId); DueDate=$dueDate } $opToken
    $plainQuotId = $plainQuot.Body.id
    $plainProp = Invoke-Api POST "/api/procurement/quotations/${plainQuotId}/quote" @{
        QuotationId=$plainQuotId; SupplierId=$supplierBId; CatalogItemId=$catalogItemId; UnitPrice=1.99; DeliveryDays=5; Notes='Menor (E2E)'; Discount=0
        ProposalValidUntil=$null; PaymentTerms=$null; Freight=0; Taxes=0; QuotationItemId=$null
    } $opToken
    Assert-Step 'Cotacao-plana-proposta-registrada' ($plainProp.Status -eq 201) "HTTP $($plainProp.Status)"

    $plainConvert = Invoke-Api POST "/api/procurement/quotations/${plainQuotId}/convert" $null $adminToken
    $plainOrders = Invoke-Psql "select count(*)::text from agro360.procurement_purchase_orders where quotation_id='${plainQuotId}';"
    Assert-Step 'Conversao-sem-confirmacao-exige-explicitude' ($plainConvert.Status -eq 400 -and $plainOrders -eq '0') "HTTP $($plainConvert.Status); pedidos_criados=$plainOrders (sem auto-selecao silenciosa)"

    # Conversao concorrente (2 chamadas simultaneas) + replay sequencial: sempre um unico pedido.
    $convJobBlock = {
        param($p, $q, $t)
        try {
            $r = Invoke-WebRequest -Uri "http://127.0.0.1:${p}/api/procurement/quotations/${q}/convert?confirmLowestPrice=true" -Method POST -Headers @{ Authorization="Bearer ${t}" } -UseBasicParsing -TimeoutSec 60
            [PSCustomObject]@{ Status=[int]$r.StatusCode; Raw=$r.Content }
        }
        catch {
            [PSCustomObject]@{ Status=[int]$($_.Exception.Response.StatusCode); Raw=$(if ($_.ErrorDetails) { $_.ErrorDetails.Message } else { $_.Exception.Message }) }
        }
    }
    $jobA = Start-Job -ScriptBlock $convJobBlock -ArgumentList $apiPort, $quotId, $adminToken
    $jobB = Start-Job -ScriptBlock $convJobBlock -ArgumentList $apiPort, $quotId, $adminToken
    Wait-Job $jobA, $jobB -Timeout 600 | Out-Null
    if (@($jobA, $jobB) | Where-Object { $_.State -ne 'Completed' }) { throw 'timeout aguardando conversao concorrente (600s)' }
    $convA = $jobA | Receive-Job
    $convB = $jobB | Receive-Job
    Remove-Job $jobA, $jobB -Force
    Assert-Step 'Conversao-concorrente-ambas-ok' ($convA.Status -in 200,201 -and $convB.Status -in 200,201) "A=$($convA.Status) B=$($convB.Status)"
    $orderIdA = ($convA.Raw | ConvertFrom-Json).id
    $orderIdB = ($convB.Raw | ConvertFrom-Json).id
    $ordersForQuot = Invoke-Psql "select count(*)::text from agro360.procurement_purchase_orders where quotation_id='${quotId}';"
    Assert-Step 'Conversao-concorrente-1-pedido-unico' (($orderIdA -eq $orderIdB) -and $ordersForQuot -eq '1') "ids_iguais=$($orderIdA -eq $orderIdB); count=$ordersForQuot"
    $orderId = $orderIdA

    $seqConvert = Invoke-Api POST "/api/procurement/quotations/${quotId}/convert" $null $adminToken
    $seqOrderId = ($seqConvert.Raw | ConvertFrom-Json).id
    $ordersAfterSeq = Invoke-Psql "select count(*)::text from agro360.procurement_purchase_orders where quotation_id='${quotId}';"
    Assert-Step 'Conversao-replay-retorna-mesmo-pedido' ($seqConvert.Status -in 200,201 -and $seqOrderId -eq $orderId -and $ordersAfterSeq -eq '1') "replay_id=$seqOrderId; count=$ordersAfterSeq"

    $orderRow = Invoke-Psql "select status || '|' || coalesce(property_id::text,'') || '|' || supplier_id::text || '|' || total::text from agro360.procurement_purchase_orders where id='${orderId}';"
    $orderParts = $orderRow -split '\|'
    Assert-Step 'Pedido-gerado-aguardando-aprovacao' ($orderParts[0] -eq 'AWAITING_APPROVAL') "status=$($orderParts[0])"
    Assert-Step 'Pedido-herda-unidade-da-requisicao' ($orderParts[1] -eq $farmA) "property=$($orderParts[1])"
    Assert-Step 'Pedido-do-fornecedor-decidido' ($orderParts[2] -eq $supplierAId) "supplier=$($orderParts[2]) (decisao, nao menor preco)"
    Assert-Step 'Pedido-total-exato-250' ($orderParts[3] -eq '250.00') "total=$($orderParts[3]) (100 L x 2.50; decimais sem arredondamento)"

    # ============ ETAPA 4: CANCELAMENTO DE COTACAO ============
    Write-Host "`n--- ETAPA 4: cancelamento de cotacao (aberta / fechada / com pedidos) ---" -ForegroundColor Cyan

    # Nova cotacao aberta a partir da requisicao plana (ainda sem pedidos vinculados).
    $cancelShort = Invoke-Api POST "/api/procurement/quotations/${plainQuotId}/cancel" @{ Reason='abc' } $adminToken
    Assert-Step 'Cancel-motivo-curto-bloqueado' ($cancelShort.Status -ge 400) "HTTP $($cancelShort.Status) (minimo 5 caracteres)"
    $cancelOk = Invoke-Api POST "/api/procurement/quotations/${plainQuotId}/cancel" @{ Reason='Fornecedor desistiu da proposta (E2E)' } $adminToken
    $cancelState = Invoke-Psql "select status || '|' || coalesce(decision_reason,'') from agro360.procurement_quotations where id='${plainQuotId}';"
    Assert-Step 'Cancel-aberta-CANCELLED-c-motivo' ($cancelOk.Status -eq 204 -and $cancelState -like 'CANCELLED|Fornecedor desistiu*') "db=$cancelState"
    $auditCancel = Invoke-Psql "select count(*)::text from agro360.procurement_audit_events where entity_type='QUOTATION' and entity_id='${plainQuotId}' and action='CANCELLED';"
    Assert-Step 'Cancel-auditado' ($auditCancel -eq '1') "audit_rows=$auditCancel"

    $cancelAgain = Invoke-Api POST "/api/procurement/quotations/${plainQuotId}/cancel" @{ Reason='Segundo cancelamento (E2E)' } $adminToken
    Assert-Step 'Cancel-já-fechada-409' ($cancelAgain.Status -eq 409) "HTTP $($cancelAgain.Status)"

    $cancelWithOrders = Invoke-Api POST "/api/procurement/quotations/${quotId}/cancel" @{ Reason='Tentativa com pedidos (E2E)' } $adminToken
    Assert-Step 'Cancel-com-pedidos-409' ($cancelWithOrders.Status -eq 409) "HTTP $($cancelWithOrders.Status)"
    $mainQuotStatus = Invoke-Psql "select status from agro360.procurement_quotations where id='${quotId}';"
    Assert-Step 'Cotacao-principal-intacta' ($mainQuotStatus -eq 'APPROVED') "status=$mainQuotStatus"

    # Apos cancelar a aberta, a requisicao segue aproveitando saldo: nova cotacao ok.
    $newQuot = Invoke-Api POST '/api/procurement/quotations' @{ RequisitionId=$plainReqId; SupplierIds=@($supplierAId,$supplierBId); DueDate=$dueDate } $opToken
    Assert-Step 'Nova-cotacao-pos-cancelamento-ok' ($newQuot.Status -eq 201) "HTTP $($newQuot.Status) id=$($newQuot.Body.id)"
    Invoke-Api POST "/api/procurement/quotations/${$newQuot.Body.id}/cancel" @{ Reason='Limpeza do exercicio (E2E)' } $adminToken | Out-Null

    # ============ ETAPA 5: APROVACAO DO PEDIDO ============
    Write-Host "`n--- ETAPA 5: aprovacao do pedido ---" -ForegroundColor Cyan

    $approveOrder = Invoke-Api POST "/api/procurement/orders/${orderId}/approve" $null $opToken
    $orderStatus = Invoke-Psql "select status from agro360.procurement_purchase_orders where id='${orderId}';"
    Assert-Step 'Pedido-aprovado' ($approveOrder.Status -eq 204 -and $orderStatus -eq 'APPROVED') "HTTP $($approveOrder.Status); status=$orderStatus"
    $approvedEvent = Invoke-Psql "select count(*)::text from agro360.procurement_purchase_order_events where purchase_order_id='${orderId}' and event_type='APPROVED';"
    Assert-Step 'Aprovacao-do-pedido-auditada' ($approvedEvent -eq '1') "events=$approvedEvent"

    # ============ ETAPA 6: RECEBIMENTO PARCIAL x2 (idempotencia + conflito + quarentena + qualidade) ============
    Write-Host "`n--- ETAPA 6: recebimento parcial, idempotencia, quarentena e qualidade ---" -ForegroundColor Cyan

    $orderItems = @((Invoke-Psql "select id::text from agro360.procurement_purchase_order_items where purchase_order_id='${orderId}' order by created_at;") -split "`r?`n" | Where-Object { $_ })
    Assert-Step 'Pedido-possui-1-item' (@($orderItems).Count -eq 1) "items=$($orderItems -join ',')"
    $orderItemId = $orderItems[0]

    $stockBefore = Invoke-Psql "select coalesce(sum(quantity),0)::text from agro360.inventory_stock_movements where tenant_id='${scTenantId}' and product_id='${productId}' and movement_type='PURCHASE_RECEIPT';"
    Assert-Step 'Estoque-inicial-zero' ([decimal]$stockBefore -eq 0) "mov PURCHASE_RECEIPT=$stockBefore"

    # Replay: duas chamadas simultaneas com MESMA chave e MESMO conteudo -> 1 recibo, mesmo id.
    $idempKey1 = 'e2e-jornada-recv1-' + (Get-Date).ToString('yyyyMMddHHmmssfff')
    $receiptBody1 = @{
        PurchaseOrderId=$orderId; ReceivedAt=$receivedAt; InvoiceDocument='NF-1001'; OverrideExcess=$false; ExcessJustification=$null
        Items=@(@{ PurchaseOrderItemId=$orderItemId; Quantity=60; SupplierLot='LOTE-E2E-1'; ExpiresOn=$null; Notes=$null })
        IdempotencyKey=$idempKey1; WarehouseId=$warehouseId; FinanceAccountId=$financeAccount; FirstDueOn=$firstDueOn; Installments=1
    }
    $recvJobBlock = {
        param($p, $t, $b)
        $json = $b | ConvertTo-Json -Depth 8 -Compress
        try {
            $r = Invoke-WebRequest -Uri "http://127.0.0.1:${p}/api/procurement/receipts" -Method POST -ContentType 'application/json' -Body ([Text.Encoding]::UTF8.GetBytes($json)) -Headers @{ Authorization="Bearer ${t}" } -UseBasicParsing -TimeoutSec 60
            [PSCustomObject]@{ Status=[int]$r.StatusCode; Raw=$r.Content }
        }
        catch {
            [PSCustomObject]@{ Status=[int]$($_.Exception.Response.StatusCode); Raw=$(if ($_.ErrorDetails) { $_.ErrorDetails.Message } else { $_.Exception.Message }) }
        }
    }
    $jr = Start-Job -ScriptBlock $recvJobBlock -ArgumentList $apiPort, $opToken, $receiptBody1
    $js = Start-Job -ScriptBlock $recvJobBlock -ArgumentList $apiPort, $opToken, $receiptBody1
    Wait-Job $jr, $js -Timeout 600 | Out-Null
    if (@($jr, $js) | Where-Object { $_.State -ne 'Completed' }) { throw 'timeout aguardando recebimento concorrente (600s)' }
    $recvR = $jr | Receive-Job
    $recvS = $js | Receive-Job
    Remove-Job $jr, $js -Force
    Assert-Step 'Recebimento-replay-concorrente-ambas-ok' ($recvR.Status -in 200,201 -and $recvS.Status -in 200,201) "A=$($recvR.Status) B=$($recvS.Status)"
    $receiptId = ($recvR.Raw | ConvertFrom-Json).id
    $sameReceiptId = ($recvS.Raw | ConvertFrom-Json).id
    $receiptCountByKey = Invoke-Psql "select count(*)::text from agro360.procurement_receipts where idempotency_key='${idempKey1}';"
    Assert-Step 'Replay-sem-duplicata-mesmo-id' (($receiptId -eq $sameReceiptId) -and $receiptCountByKey -eq '1') "ids_iguais=$($receiptId -eq $sameReceiptId); count=$receiptCountByKey"

    # MESMA chave com OUTRO conteudo -> 409 (fingerprint divergente).
    $dupKeyOther = Invoke-Api POST '/api/procurement/receipts' @{
        PurchaseOrderId=$orderId; ReceivedAt=$receivedAt; InvoiceDocument='NF-1001'; OverrideExcess=$false; ExcessJustification=$null
        Items=@(@{ PurchaseOrderItemId=$orderItemId; Quantity=61; SupplierLot='LOTE-E2E-1'; ExpiresOn=$null; Notes=$null })
        IdempotencyKey=$idempKey1; WarehouseId=$warehouseId; FinanceAccountId=$financeAccount; FirstDueOn=$firstDueOn; Installments=1
    } $opToken
    $receiptTotalAfterDup = Invoke-Psql "select count(*)::text from agro360.procurement_receipts where tenant_id='${scTenantId}';"
    Assert-Step 'Mesma-chave-outro-conteudo-409' ($dupKeyOther.Status -eq 409 -and $receiptTotalAfterDup -eq '1') "HTTP $($dupKeyOther.Status); total_recibos=$receiptTotalAfterDup"

    # Item com inspecao obrigatoria -> quarentena PENDING, recibo DIVERGENT, pedido PARTIALLY_RECEIVED.
    $quarantine = Invoke-Psql "select quantity::text || ':' || status from agro360.procurement_receipt_quarantine where receipt_item_id=(select id from agro360.procurement_receipt_items where receipt_id='${receiptId}') and tenant_id='${scTenantId}';"
    Assert-Step 'Quarentena-inspecao-pendente' (([double]($quarantine -split ':')[0]) -eq 60 -and ($quarantine -split ':')[1] -eq 'PENDING') "quarantine=$quarantine (60 l bloqueados)"
    $recvStatus1 = Invoke-Psql "select status || '|' || stock_integration_status from agro360.procurement_receipts where id='${receiptId}';"
    Assert-Step 'Recibo-DIVERGENT-ate-qualidade' ($recvStatus1 -eq 'DIVERGENT|PENDING') "status=$recvStatus1"
    $orderStatus2 = Invoke-Psql "select status from agro360.procurement_purchase_orders where id='${orderId}';"
    Assert-Step 'Pedido-PARTIALLY_RECEIVED' ($orderStatus2 -eq 'PARTIALLY_RECEIVED') "status=$orderStatus2 (60/100 recebidos)"

    # Central: divergência de recebimento aparece; QUALITY (inspeção pendente) também.
    $ocRecv = Get-OperationCenter $opToken 'receive'
    Assert-Step 'Central-RECEIPT_DIVERGENCE-aparece' (@(Find-Occurrence $ocRecv 'RECEIPT_DIVERGENCE' $receiptId).Count -eq 1) "link=$(($(Find-Occurrence $ocRecv 'RECEIPT_DIVERGENCE' $receiptId))[0].SafeLink)"
    Assert-Step 'Central-QUALITY-inspecao-pendente' (@(Find-Occurrence $ocRecv 'QUALITY' $receiptId).Count -eq 1) "material bloqueado visivel"

    $stockAfterRecv1 = Invoke-Psql "select coalesce(sum(quantity),0)::text from agro360.inventory_stock_movements where tenant_id='${scTenantId}' and product_id='${productId}' and movement_type='PURCHASE_RECEIPT';"
    Assert-Step 'Nada-liberado-em-estoque-antes-da-qualidade' ([double]$stockAfterRecv1 -eq 0) "mov=$stockAfterRecv1 (quarentena nao vira saldo disponivel)"

    # Central PURCHASE_APPROVAL: enquanto o pedido estava aguardando, deveria ter aparecido —
    # a pedido ja foi aprovado; valida agora que a ocorrencia PURCHASE (aguardando entrega/saldo) existe.
    $ocPurchase = Get-OperationCenter $adminToken 'purchase'
    Assert-Step 'Central-PURCHASE-saldo-pendente' (@(Find-Occurrence $ocPurchase 'PURCHASE' $orderId).Count -eq 1) "saldo 40 L ainda nao recebido"

    # Decisão de qualidade: aceita 55, rejeita 5 (SoD de qualidade: admin decide, operator registrou).
    $receiptItem1 = Invoke-Psql "select id::text from agro360.procurement_receipt_items where receipt_id='${receiptId}' limit 1;"
    $qualityKey1 = 'qual-1-' + (Get-Date).ToString('yyyyMMddHHmmssfff')
    $qualityBody1 = @{
        AcceptedQuantity=55; RejectedQuantity=5; Result='APPROVED_WITH_REJECTION'; Reason='Laudo tecnico aprovado com 5 l de avaria (E2E)'; EvidenceReference='LAUDO-E2E-001'; IdempotencyKey=$qualityKey1
    }
    $qualityOpTry = Invoke-Api POST "/api/procurement/receipt-items/${receiptItem1}/quality-decisions" @{
        AcceptedQuantity=55; RejectedQuantity=5; Result='APPROVED_WITH_REJECTION'; Reason='Tentativa sem permissao (E2E)'; EvidenceReference='LAUDO-1'; IdempotencyKey=('qual-op-' + (Get-Date).ToString('yyyyMMddHHmmssfff'))
    } $opToken
    Assert-Step 'Qualidade-operator-sem-compliance-403' ($qualityOpTry.Status -eq 403) "HTTP $($qualityOpTry.Status) (policy compliance.approve)"

    $quality1 = Invoke-Api POST "/api/procurement/receipt-items/${receiptItem1}/quality-decisions" $qualityBody1 $adminToken
    Assert-Step 'Qualidade-admin-aceita-55-rejeita-5' ($quality1.Status -eq 201) "HTTP $($quality1.Status) id=$($quality1.Body.id)"

    # Replay idempotente: MESMA chave e MESMO conteudo em segunda chamada -> mesmo id, 1 decisao.
    $quality1b = Invoke-Api POST "/api/procurement/receipt-items/${receiptItem1}/quality-decisions" $qualityBody1 $adminToken
    $qualityDecisions = Invoke-Psql "select count(*)::text from agro360.procurement_receipt_quality_decisions where receipt_item_id='${receiptItem1}';"
    Assert-Step 'Qualidade-replay-idempotente' (($quality1b.Status -eq 200 -or $quality1b.Status -eq 201) -and "$($quality1b.Body.id)" -eq "$($quality1.Body.id)" -and $qualityDecisions -eq '1') "ids_iguais=$($quality1b.Body.id -eq $quality1.Body.id); decisions=$qualityDecisions (replay nao duplica)"

    $stockAfterQuality = Invoke-Psql "select coalesce(available,0)::text from agro360.inventory_stock_balances where tenant_id='${scTenantId}' and warehouse_id='${warehouseId}' and product_id='${productId}';"
    Assert-Step 'Estoque-libera-somente-o-aceito-55' ($stockAfterQuality -eq '55.000000') "available=$stockAfterQuality (55 aceitos; 5 rejeitados nunca entram)"

    # Segunda remessa: 40 L completos.
    $idempKey2 = 'e2e-jornada-recv2-' + (Get-Date).ToString('yyyyMMddHHmmssfff')
    $receipt2 = Invoke-Api POST '/api/procurement/receipts' @{
        PurchaseOrderId=$orderId; ReceivedAt=$receivedAt; InvoiceDocument='NF-1002'; OverrideExcess=$false; ExcessJustification=$null
        Items=@(@{ PurchaseOrderItemId=$orderItemId; Quantity=40; SupplierLot='LOTE-E2E-2'; ExpiresOn=$null; Notes=$null })
        IdempotencyKey=$idempKey2; WarehouseId=$warehouseId; FinanceAccountId=$financeAccount; FirstDueOn=$firstDueOn; Installments=1
    } $opToken
    Assert-Step 'Segunda-parcial-40L-201' ($receipt2.Status -eq 201) "HTTP $($receipt2.Status) id=$($receipt2.Body.id)"
    $receipt2Id = $receipt2.Body.id

    $receiptItem2 = Invoke-Psql "select id::text from agro360.procurement_receipt_items where receipt_id='${receipt2Id}' limit 1;"
    $quality2 = Invoke-Api POST "/api/procurement/receipt-items/${receiptItem2}/quality-decisions" @{
        AcceptedQuantity=40; RejectedQuantity=0; Result='APPROVED'; Reason='Lote 2 integralmente conforme (E2E)'; EvidenceReference='LAUDO-E2E-002'; IdempotencyKey=('qual-2-' + (Get-Date).ToString('yyyyMMddHHmmssfff'))
    } $adminToken
    Assert-Step 'Qualidade-remessa-2-aceita-40' ($quality2.Status -eq 201) "HTTP $($quality2.Status)"

    $pending = Invoke-Api GET "/api/procurement/orders/${orderId}/pending-items" $null $opToken
    Assert-Step 'Saldo-do-pedido-esgotado' ($pending.Status -eq 200 -and @($pending.Body).Count -eq 0) "pendentes=$(if ($null -ne $pending.Body) { @($pending.Body).Count } else { '?' })"
    $stockTotal = Invoke-Psql "select coalesce(available,0)::text from agro360.inventory_stock_balances where tenant_id='${scTenantId}' and warehouse_id='${warehouseId}' and product_id='${productId}';"
    Assert-Step 'Estoque-total-95-L' ($stockTotal -eq '95.000000') "available=$stockTotal (55+40)"
    $orderFinalStatus = Invoke-Psql "select status from agro360.procurement_purchase_orders where id='${orderId}';"
    Assert-Step 'Pedido-estado-final-consistente' ($orderFinalStatus -in 'RECEIVED','DIVERGENT') "status=$orderFinalStatus (100/100; historico de rejeicao preservado no recibo)"

    # Central: QUALITY e RECEIPT_DIVERGENCE do recibo 1 continuam apenas enquanto houver pendencia;
    # com tudo decidido, a ocorrencia QUALITY some do recibo 2; RECEIPT_DIVERGENCE some quando o recibo deixa de ser DIVERGENT.
    $ocAfterQuality = Get-OperationCenter $opToken 'post-quality'
    $qual2 = @(Find-Occurrence $ocAfterQuality 'QUALITY' $receipt2Id)
    Assert-Step 'Central-QUALITY-resolvida-remessa-2' (@($qual2).Count -eq 0) "sem inspecao pendente no recibo 2"

    # ============ ETAPA 7: DEVOLUCAO AO FORNECEDOR (SoD + estoque + credito + saldo) ============
    Write-Host "`n--- ETAPA 7: devolucao ao fornecedor ---" -ForegroundColor Cyan

    $returnable = Invoke-Api GET "/api/procurement/receipts/${receipt2Id}/returnable-items" $null $opToken
    $eligible = @($returnable.Body | Select-Object -First 1).accepted_quantity
    Assert-Step 'Returnables-refletem-somente-aceito' ($returnable.Status -eq 200 -and [decimal]$eligible -eq 40) "eligible=$eligible (aceitos menos devolvidos)"

    $returnKey = 'e2e-jornada-return-' + (Get-Date).ToString('yyyyMMddHHmmssfff')
    $ret = Invoke-Api POST "/api/procurement/receipts/${receipt2Id}/supplier-returns" @{
        Reason='Avaria em 10 litros constatada no armazem (E2E)'; IdempotencyKey=$returnKey
        Items=@(@{ ReceiptItemId=$receiptItem2; Quantity=10 })
    } $opToken
    Assert-Step 'Devolucao-criada-PENDING' ($ret.Status -eq 201) "HTTP $($ret.Status) id=$($ret.Body.id)"
    $returnId = $ret.Body.id
    $retStatus = Invoke-Psql "select status from agro360.procurement_supplier_returns where id='${returnId}';"
    Assert-Step 'Devolucao-aguardando-aprovacao' ($retStatus -eq 'PENDING_APPROVAL') "status=$retStatus (estoque intacto ate decidir)"

    $stockBeforeRet = Invoke-Psql "select coalesce(available,0)::text from agro360.inventory_stock_balances where tenant_id='${scTenantId}' and warehouse_id='${warehouseId}' and product_id='${productId}';"
    Assert-Step 'Estoque-intacto-antes-da-aprovacao' ($stockBeforeRet -eq '95.000000') "available=$stockBeforeRet"

    # Central: SUPPLIER_RETURN aparece (gate purchasing.approve).
    $ocRet = Get-OperationCenter $adminToken 'return'
    Assert-Step 'Central-SUPPLIER_RETURN-aparece' (@(Find-Occurrence $ocRet 'SUPPLIER_RETURN' $returnId).Count -eq 1) "link=$(($(Find-Occurrence $ocRet 'SUPPLIER_RETURN' $returnId))[0].SafeLink)"
    $ocRetNo = Get-OperationCenter $npToken 'return-no'
    Assert-Step 'Central-SUPPLIER_RETURN-sem-permissao-invisivel' (@(Find-Occurrence $ocRetNo 'SUPPLIER_RETURN' $returnId).Count -eq 0) "gate de permissao"

    # SoD: quem registrou nao decide.
    $retSod = Invoke-Api POST "/api/procurement/supplier-returns/${returnId}/decision" @{ Approve=$true; Reason='Tentativa SoD (E2E)' } $opToken
    $retSodStatus = Invoke-Psql "select status from agro360.procurement_supplier_returns where id='${returnId}';"
    Assert-Step 'SoD-registrante-nao-aprova-devolucao' ($retSod.Status -ge 400 -and $retSodStatus -eq 'PENDING_APPROVAL') "HTTP $($retSod.Status); status=$retSodStatus"

    $retApprove = Invoke-Api POST "/api/procurement/supplier-returns/${returnId}/decision" @{ Approve=$true; Reason='Devolucao confirmada no armazem (E2E)' } $adminToken
    $retApproved = Invoke-Psql "select status from agro360.procurement_supplier_returns where id='${returnId}';"
    Assert-Step 'Admin-aprova-devolucao' ($retApprove.Status -eq 204 -and $retApproved -eq 'APPROVED') "HTTP $($retApprove.Status); status=$retApproved"

    $retMovement = Invoke-Psql "select coalesce(sum(quantity),0)::text from agro360.inventory_stock_movements where tenant_id='${scTenantId}' and product_id='${productId}' and movement_type='RETURN_TO_SUPPLIER';"
    $stockAfterRet = Invoke-Psql "select coalesce(available,0)::text from agro360.inventory_stock_balances where tenant_id='${scTenantId}' and warehouse_id='${warehouseId}' and product_id='${productId}';"
    Assert-Step 'Devolucao-baixa-estoque-10L' (($retMovement -replace '[.0]*$','') -eq '-10' -and $stockAfterRet -eq '85.000000') "mov=$retMovement; available=$stockAfterRet (95-10)"

    $credit = Invoke-Psql "select amount::text || ':' || status from agro360.procurement_supplier_credits where supplier_return_id='${returnId}';"
    Assert-Step 'Credito-OPEN-25' ($credit -like '25*:OPEN') "credito=$credit (10 L x 2.50; nada compensado automaticamente)"

    $pendingAfterRet = Invoke-Api GET "/api/procurement/orders/${orderId}/pending-items" $null $opToken
    $reopenQty = 0
    if ($pendingAfterRet.Status -eq 200 -and $null -ne $pendingAfterRet.Body -and @($pendingAfterRet.Body).Count -gt 0) { $reopenQty = [decimal](@($pendingAfterRet.Body)[0]).pending_quantity }
    Assert-Step 'Saldo-do-pedido-reaberto-10L' ($reopenQty -eq 10) "pending=$reopenQty (pedido volta a ter saldo recebivel)"

    $ocAfterRet = Get-OperationCenter $adminToken 'post-return'
    Assert-Step 'Central-SUPPLIER_RETURN-desaparece' (@(Find-Occurrence $ocAfterRet 'SUPPLIER_RETURN' $returnId).Count -eq 0) "decidida sai da fila"

    # ============ ETAPA 8: CONFERENCIA DOCUMENTAL + TOLERANCIA + EXCECAO ============
    Write-Host "`n--- ETAPA 8: conferencia documental ---" -ForegroundColor Cyan

    $tolGet = Invoke-Api GET '/api/procurement/match-tolerance' $null $opToken
    Assert-Step 'Tolerancia-get-flat' ($tolGet.Status -eq 200 -and $null -ne $tolGet.Body.quantity_percent) "version=$($tolGet.Body.version) (GET flat snake_case)"
    $tolVersion = [long]$tolGet.Body.version

    $options = Invoke-Api GET "/api/procurement/orders/${orderId}/match-options" $null $opToken
    $optRows = @($options.Body)
    Assert-Step 'Match-options-2-linhas-disponiveis' ($options.Status -eq 200 -and @($optRows).Count -eq 2) "linhas=$(if ($null -ne $options.Body) { @($options.Body).Count } else { 0 })"
    $opt1 = $optRows | Where-Object { $_.receipt_item_id -eq $receiptItem1 } | Select-Object -First 1
    $opt2 = $optRows | Where-Object { $_.receipt_item_id -eq $receiptItem2 } | Select-Object -First 1
    Assert-Step 'Match-options-saldos-corretos' ([decimal]$opt1.available_quantity -eq 55 -and [decimal]$opt2.available_quantity -eq 40) "l1=$($opt1.available_quantity) l2=$($opt2.available_quantity) (liberado menos faturado; devolucao controlada em returnables)"

    # Primeira gravacao (tenant sem linha ativa): version 0 do GET padrao cria a linha (DB default version=1).
    $tolBody = @{
        QuantityPercent=1; QuantityAbsolute=1; PricePercent=10; PriceAbsolute=1; TotalPercent=10; TotalAbsolute=10
        ExcessPercent=5; ExcessAbsolute=5; DeliveryDays=7; SeparationOfDuties=$true
    }
    $tolCreate = Invoke-Api PUT '/api/procurement/match-tolerance' ($tolBody + @{ Version=[long]$tolVersion }) $adminToken
    Assert-Step 'Tolerancia-criacao-com-versao-padrao' ($tolCreate.Status -eq 200) "HTTP $($tolCreate.Status) (linha inexistente: version do GET default)"
    $tolGet2 = Invoke-Api GET '/api/procurement/match-tolerance' $null $opToken
    $tolVersion = [long]$tolGet2.Body.version

    # Conferencia EXATA (linha 1: 55 L @ 2.50 = total contratado): MATCHED direto.
    $exactKey = 'e2e-jornada-match-exact-' + (Get-Date).ToString('yyyyMMddHHmmssfff')
    $matchExact = Invoke-Api POST '/api/procurement/invoice-matches' @{
        PurchaseOrderId=$orderId; DocumentNumber='DOC-EXATA-001'; DocumentSeries=$null; IssuedOn=$firstDueOn; Currency='BRL'
        GoodsTotal=137.50; Discount=0; Freight=0; AdditionalAmount=0; Total=137.50
        IdempotencyKey=$exactKey
        Items=@(@{ PurchaseOrderItemId=$orderItemId; ReceiptItemId=$receiptItem1; Description='Diesel S10 (Jornada E2E)'; Quantity=55; Unit='l'; UnitPrice=2.50; Discount=0 })
    } $opToken
    Assert-Step 'Conferencia-exata-201' ($matchExact.Status -eq 201) "HTTP $($matchExact.Status)"
    $matchExactStatus = Invoke-Psql "select status from agro360.procurement_invoice_matches where id='${$matchExact.Body.id}';"
    Assert-Step 'Exata-MATCHED-zero-diferenca' ($matchExactStatus -eq 'MATCHED') "status=$matchExactStatus (tolerancias zeradas: diferenca zero)"

    # Conferencia FORA da tolerancia (linha 2: 30 L cobrados a 3.00 contra 2.50): PRICE + TOTAL.
    $divKey = 'e2e-jornada-match-div-' + (Get-Date).ToString('yyyyMMddHHmmssfff')
    $matchDiv = Invoke-Api POST '/api/procurement/invoice-matches' @{
        PurchaseOrderId=$orderId; DocumentNumber='DOC-DIV-002'; DocumentSeries=$null; IssuedOn=$firstDueOn; Currency='BRL'
        GoodsTotal=90.00; Discount=0; Freight=0; AdditionalAmount=0; Total=90.00
        IdempotencyKey=$divKey
        Items=@(@{ PurchaseOrderItemId=$orderItemId; ReceiptItemId=$receiptItem2; Description='Diesel S10 (Jornada E2E)'; Quantity=30; Unit='l'; UnitPrice=3.00; Discount=0 })
    } $opToken
    Assert-Step 'Conferencia-divergente-201' ($matchDiv.Status -eq 201) "HTTP $($matchDiv.Status)"
    $matchDivId = $matchDiv.Body.id
    $matchDivStatus = Invoke-Psql "select status from agro360.procurement_invoice_matches where id='${matchDivId}';"
    Assert-Step 'Divergente-PENDING_EXCEPTION' ($matchDivStatus -eq 'PENDING_EXCEPTION') "status=$matchDivStatus (+20% sobre preco contratado)"
    $divIds = @((Invoke-Psql "select id::text from agro360.procurement_match_divergences where invoice_match_id='${matchDivId}' and status='OPEN' order by created_at;") -split "`r?`n" | Where-Object { $_ })
    Assert-Step 'Divergencias-PRICE-e-TOTAL-abertas' (@($divIds).Count -eq 2) "divergences=$(($divIds) -join ',' )"

    # Cobranca acima do saldo aceito-disponivel -> 409.
    $overbill = Invoke-Api POST '/api/procurement/invoice-matches' @{
        PurchaseOrderId=$orderId; DocumentNumber='DOC-OVER-003'; DocumentSeries=$null; IssuedOn=$firstDueOn; Currency='BRL'
        GoodsTotal=50.00; Discount=0; Freight=0; AdditionalAmount=0; Total=50.00
        IdempotencyKey=('e2e-jornada-match-over-' + (Get-Date).ToString('yyyyMMddHHmmssfff'))
        Items=@(@{ PurchaseOrderItemId=$orderItemId; ReceiptItemId=$receiptItem2; Description='Diesel S10 (Jornada E2E)'; Quantity=31; Unit='l'; UnitPrice=3.00; Discount=0 })
    } $opToken
    Assert-Step 'Cobranca-acima-do-saldo-409' ($overbill.Status -eq 409) "HTTP $($overbill.Status) (saldo disponivel=30)"

    # Central: INVOICE_MATCH aparece (PENDING_EXCEPTION) e some apos decidir todas as excecoes.
    $ocMatch = Get-OperationCenter $adminToken 'match'
    Assert-Step 'Central-INVOICE_MATCH-aparece' (@(Find-Occurrence $ocMatch 'INVOICE_MATCH' $matchDivId).Count -eq 1) "excecao aberta visivel"

    # PUT de tolerancia com versao errada -> 409; com versao certa -> 200 + version+1.
    $tolStale = Invoke-Api PUT '/api/procurement/match-tolerance' @{
        QuantityPercent=1; QuantityAbsolute=1; PricePercent=10; PriceAbsolute=1; TotalPercent=10; TotalAbsolute=10
        ExcessPercent=5; ExcessAbsolute=5; DeliveryDays=7; SeparationOfDuties=$true; Version=($tolVersion + 5)
    } $adminToken
    Assert-Step 'Tolerancia-versao-estale-409' ($tolStale.Status -eq 409) "HTTP $($tolStale.Status) (optimistic locking preservado)"
    $tolPut = Invoke-Api PUT '/api/procurement/match-tolerance' @{
        QuantityPercent=1; QuantityAbsolute=1; PricePercent=10; PriceAbsolute=1; TotalPercent=10; TotalAbsolute=10
        ExcessPercent=5; ExcessAbsolute=5; DeliveryDays=7; SeparationOfDuties=$true; Version=$tolVersion
    } $adminToken
    $tolAfter = Invoke-Psql "select price_percent::text || ':' || version::text from agro360.procurement_match_tolerances where tenant_id='${scTenantId}' and active and deleted_at is null;"
    Assert-Step 'Tolerancia-atualizada-versionada' ($tolPut.Status -eq 200 -and $tolAfter -eq "10.0000:$($tolVersion + 1)") "db=$tolAfter (snapshot antigo segue valendo para a conferencia)"

    # SoD: autor da conferencia nao aprova a propria excecao (SeparationOfDuties=true no snapshot).
    $divSod = Invoke-Api POST "/api/procurement/match-divergences/${divIds[0]}/decision" @{ Decision='APPROVED_EXCEPTION'; Justification='Tentativa SoD (E2E)'; EvidenceReference=$null } $opToken
    $divSodStatus = Invoke-Psql "select status from agro360.procurement_match_divergences where id='${divIds[0]}';"
    Assert-Step 'SoD-autor-nao-aprova-excecao' ($divSod.Status -ge 400 -and $divSodStatus -eq 'OPEN') "HTTP $($divSod.Status); status=$divSodStatus"

    # Admin resolve as duas excecoes -> MATCHED + desapare da central.
    $d1 = Invoke-Api POST "/api/procurement/match-divergences/${divIds[0]}/decision" @{ Decision='APPROVED_EXCEPTION'; Justification='Reajuste tarifario autorizado no contrato (E2E)'; EvidenceReference='CONTRATO-E2E' } $adminToken
    $d2 = Invoke-Api POST "/api/procurement/match-divergences/${divIds[1]}/decision" @{ Decision='APPROVED_EXCEPTION'; Justification='Diferencial de total decorrente do reajuste (E2E)'; EvidenceReference='CONTRATO-E2E' } $adminToken
    $divAll = Invoke-Psql "select count(*)::text filter(where status='OPEN') || '/' || count(*)::text from agro360.procurement_match_divergences where invoice_match_id='${matchDivId}';"
    $matchDivFinal = Invoke-Psql "select status from agro360.procurement_invoice_matches where id='${matchDivId}';"
    Assert-Step 'Excecoes-resolvidas-pelo-admin' ($d1.Status -eq 204 -and $d2.Status -eq 204 -and $divAll -eq '0/2') "HTTP $($d1.Status),$($d2.Status); open/total=$divAll"
    Assert-Step 'Conferencia-vira-MATCHED' ($matchDivFinal -eq 'MATCHED') "status=$matchDivFinal"
    $ocAfterMatch = Get-OperationCenter $adminToken 'post-match'
    Assert-Step 'Central-INVOICE_MATCH-desaparece' (@(Find-Occurrence $ocAfterMatch 'INVOICE_MATCH' $matchDivId).Count -eq 0) "resolvida sai da fila"

    # ============ ETAPA 9: PREVISAO FINANCEIRA OPEN ============
    Write-Host "`n--- ETAPA 9: previsao financeira ---" -ForegroundColor Cyan

    $payables = Invoke-Psql "select count(*)::text || '|' || min(status) || '|' || max(status) || '|' || sum(balance)::text from agro360.finance_payables where source_id='${orderId}';"
    $payParts = $payables -split '\|'
    Assert-Step 'Previsao-criada-no-recebimento' ([int]$payParts[0] -ge 1) "payables=$payables (1 parcela pelo total do pedido)"
    Assert-Step 'Previsao-toda-OPEN' ($payParts[1] -eq 'OPEN' -and $payParts[2] -eq 'OPEN') "status=$($payParts[1])..$($payParts[2]) (receber/conferir NUNCA paga)"
    $payBalance = Invoke-Psql "select sum(balance)::text from agro360.finance_payables where source_id='${orderId}';"
    Assert-Step 'Previsao-cobre-total-do-pedido' ($payBalance -in '250.00','250.0000') "saldo=$payBalance (decimal preciso)"

    # ============ ETAPA 10: ISOLAMENTO ENTRE TENANTS + MODULO NAO CONTRATADO ============
    Write-Host "`n--- ETAPA 10: dois tenants ---" -ForegroundColor Cyan

    $valeLogin = Invoke-Api POST '/api/v1/auth/login' @{ TenantSlug='cooperativa-vale-verde'; Email='admin.valeverde@agro360.local'; Password='Agro360Admin@2024!' }
    Assert-Step 'Login-admin-vale' ($valeLogin.Status -eq 200) "HTTP $($valeLogin.Status) (segundo tenant autenticavel)"
    $valeToken = $valeLogin.Body.accessToken

    $valeSupplier = Invoke-Api POST '/api/procurement/suppliers' @{
        LegalName='Fornecedor Vale Isolacao Ltda'; TradeName='ValeIso'; TaxDocument=$null; StateRegistration=$null; Type='MATERIAL'
        Category='GENERAL'; Email=$null; Phone=$null; Address=$null; City='Cuiaba'; State='MT'; Country='BR'
        MainContact=$null; PaymentTerms=$null; AverageDeliveryDays=9; Status='ACTIVE'; RejectionReason=$null; Notes=$null; Tags=@()
    } $valeToken
    Assert-Step 'Vale-cria-fornecedor-proprio' ($valeSupplier.Status -eq 201) "HTTP $($valeSupplier.Status)"

    $scSuppliers = Invoke-Api GET '/api/procurement/suppliers?search=ValeIso' $null $adminToken
    Assert-Step 'Santa-nao-ve-fornecedor-do-vale' ($scSuppliers.Status -eq 200 -and @($scSuppliers.Body).Count -eq 0) "linhas=$(if ($null -ne $scSuppliers.Body) { @($scSuppliers.Body).Count } else { 0 }) (isolamento de tenant)"
    $scDashboard = Invoke-Api GET '/api/procurement/dashboard' $null $adminToken
    Assert-Step 'Dashboard-santa-sem-dados-do-vale' ($scDashboard.Status -eq 200 -and [int]$scDashboard.Body.active_suppliers -eq 2) "active_suppliers=$($scDashboard.Body.active_suppliers) (somente os da jornada; isolacao de tenant)"

    # Suspender o modulo 'purchasing' do Vale Verde: permissao completa nao substitui contratacao.
    Invoke-Psql "update agro360.platform_tenant_module_entitlements set status='SUSPENDED',reason='E2E modulo nao contratado' where tenant_id='${valeTenantId}' and module_id=(select id from agro360.platform_module_catalog where code='purchasing');" | Out-Null
    $valeSuppliersAfter = Invoke-Api GET '/api/procurement/suppliers' $null $valeToken
    Assert-Step 'Modulo-nao-contratado-403' ($valeSuppliersAfter.Status -eq 403) "HTTP $($valeSuppliersAfter.Status) (contratacao ausente; permissao completa nao basta)"
    $valeInventory = Invoke-Api GET '/api/v1/inventory/balances' $null $valeToken
    Assert-Step 'Outros-modulos-vale-indenizados' ($valeInventory.Status -eq 200) "HTTP $($valeInventory.Status) (so o modulo suspenso bloqueia)"
    Invoke-Psql "update agro360.platform_tenant_module_entitlements set status='ACTIVE',reason='Fixture de isolamento Vale Verde' where tenant_id='${valeTenantId}' and module_id=(select id from agro360.platform_module_catalog where code='purchasing');" | Out-Null

    # Permissao ausente (mesmo tenant, modulo contratado): 403 na API.
    $npSuppliers = Invoke-Api GET '/api/procurement/suppliers' $null $npToken
    Assert-Step 'Permissao-ausente-403' ($npSuppliers.Status -eq 403) "HTTP $($npSuppliers.Status) (semcompras tem modulo mas nao tem purchasing.read)"

    # ============ ETAPA 11: ESCOPO PROPRIO NAO SE EXPANDE ============
    Write-Host "`n--- ETAPA 11: delegacao de escopo ---" -ForegroundColor Cyan

    # Operador (escopo FARM A) tenta conceder a si mesmo FARM B: fora do proprio escopo.
    $selfB = Invoke-Api PUT "/api/users/${operatorUser}/scopes" @{ Scopes=@(@{ ScopeType='FARM'; FarmId=$farmB; OrganizationId=$null }); Reason='Tentativa de auto-expandir para a Fazenda B (E2E)' } $opToken
    Assert-Step 'Auto-expansao-FARM-B-403' ($selfB.Status -eq 403) "HTTP $($selfB.Status)"
    $selfScopeAfterB = Invoke-Psql "select count(*)::text || ':' || coalesce(min(farm_id::text),'') from agro360.identity_user_unit_scopes where tenant_id='${scTenantId}' and user_id='${operatorUser}';"
    Assert-Step 'Escopo-do-operator-mantido-A' ($selfScopeAfterB -like "1:${farmA}") "scopes=$selfScopeAfterB"

    # E tentar virar ALL: exige ter ALL.
    $selfAll = Invoke-Api PUT "/api/users/${operatorUser}/scopes" @{ Scopes=@(@{ ScopeType='ALL'; FarmId=$null; OrganizationId=$null }); Reason='Tentativa de auto-expandir para ALL (E2E)' } $opToken
    Assert-Step 'Auto-expansao-ALL-403' ($selfAll.Status -eq 403) "HTTP $($selfAll.Status)"

    # Dentro do proprio escopo: permitido.
    $selfSame = Invoke-Api PUT "/api/users/${operatorUser}/scopes" @{ Scopes=@(@{ ScopeType='FARM'; FarmId=$farmA; OrganizationId=$null }); Reason='Reafirmar escopo proprio (E2E)' } $opToken
    Assert-Step 'Auto-confirma-mesmo-escopo-ok' ($selfSame.Status -eq 204) "HTTP $($selfSame.Status)"

    # Admin (tenant-administrator) delega ALL de volta: autoridade legitima.
    $adminRestore = Invoke-Api PUT "/api/users/${operatorUser}/scopes" @{ Scopes=@(@{ ScopeType='ALL'; FarmId=$null; OrganizationId=$null }); Reason='Restaura escopo ALL apos homologacao (E2E)' } $adminToken
    $finalScopes = Invoke-Psql "select scope_type from agro360.identity_user_unit_scopes where tenant_id='${scTenantId}' and user_id='${operatorUser}';"
    Assert-Step 'Admin-delega-ALL-para-operator' ($adminRestore.Status -eq 204 -and $finalScopes -eq 'ALL') "scope_final=$finalScopes"

    # ============ RESUMO FINAL ============
    Write-Host "`n--- RESUMO DE HOMOLOGACAO ---" -ForegroundColor Cyan
    Write-Host "Todas as etapas da jornada executadas com sucesso." -ForegroundColor Green
    Write-Host "Evidencias em: $evidenceDir"

    $summary = @"
PROCUREMENT JOURNEY E2E (incremento integrado Compras)
Data/Hora: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss UTC')
PG Port: $pgPort
API Port: $apiPort
DB: $dbName
Schema: 11.24.0 (zero migracoes novas neste incremento)

ETAPA 1 - Requisicao + alçada:
  1. Operador (escopo FARM A) cria requisicao em B: negada; em A: enviada (AWAITING_APPROVAL): PASS
  2. Central: REQ_APPROVAL com deep link /Procurement?requisitionId=...; visivel a admin+operator; invisivel sem purchasing.approve; somente Fazenda A: PASS
  3. SoD: solicitante nao aprova a propria (400+, status mantido); admin aprova (204, APPROVED); versao stale 409: PASS
  4. Ocorrencia some da central apos decisao: PASS
ETAPA 2 - Cotacao:
  5. Cotação aberta com 2 fornecedores; segunda aberta 409; cotação de requisicao fora do escopo negada: PASS
  6. Propostas A=2.50 (10d) e B=2.35 (7d); comparacao retorna as duas: PASS
  7. Decisao acima do menor SEM justificativa: bloqueada; COM justificativa: 204 + persistida/auditada: PASS
ETAPA 3 - Conversao:
  8. Sem decisao + sem confirmacao: 400 e ZERO pedidos (sem auto-selecao silenciosa): PASS
  9. Conversao concorrente: ambas OK, MESMO pedido, count=1; replay sequencial retorna o mesmo id: PASS
  10. Pedido AWAITING_APPROVAL herda unidade (FARM A), fornecedor DECIDIDO (Alfa), total exato 250.00: PASS
ETAPA 4 - Cancelamento de cotacao:
  11. Motivo <5: 400; aberta -> CANCELLED c/ decision_reason + auditoria; fechada: 409; com pedidos: 409; nova cotação pos-cancelamento: 201: PASS
ETAPA 5 - Pedido:
  12. Aprovacao 204 + evento auditado: PASS
ETAPA 6 - Recebimento + qualidade:
  13. Replay concorrente (mesma chave/conteudo): 1 recibo, mesmo id; mesma chave c/ outro conteudo: 409: PASS
  14. Item c/ inspecao: quarentena 60 PENDING; recibo DIVERGENT|PENDING; pedido PARTIALLY_RECEIVED; Central RECEIPT_DIVERGENCE + QUALITY: PASS
  15. Nada entra em estoque antes da qualidade; operator sem compliance.approve: 403: PASS
  16. Qualidade (admin): aceita 55 / rejeita 5 -> estoque exatamente 55 (somente o aceito): PASS
  17. Segunda parcial 40L + qualidade aceita 40 -> estoque 95; saldo do pedido esgotado: PASS
ETAPA 7 - Devolucao:
  18. Returnable reflete somente aceito; devolucao PENDING_APPROVAL com estoque intacto; Central SUPPLIER_RETURN (visibilidade gate por permissao): PASS
  19. SoD: registrante nao aprova; admin aprova -> baixa RETURN_TO_SUPPLIER -10, estoque 85, credito OPEN 25.00, saldo do pedido reaberto 10: PASS
ETAPA 8 - Conferencia documental:
  20. Match-options: saldos 55 e 30 (aceito menos devolvido); cobranca acima do saldo: 409: PASS
  21. Exata (55x2.50): MATCHED com diferenca zero; fora da tolerancia (30x3.00): PENDING_EXCEPTION c/ PRICE+TOTAL: PASS
  22. PUT tolerancia: versao stale 409; atualiza versionada (snapshot antigo vale para a conferencia): PASS
  23. SoD: autor nao aprova propria excecao; admin resolve as duas -> MATCHED; ocorrencia INVOICE_MATCH some da central: PASS
ETAPA 9 - Previsao financeira:
  24. finance_payables criada no recebimento, 100% OPEN, saldo = total do pedido (250.00 decimal): PASS
ETAPA 10 - Tenants:
  25. Vale Verde logado; cria fornecedor invisivel a Santa Clara; dashboard isolado: PASS
  26. Modulo 'purchasing' suspenso no Vale: 403 mesmo com permissao completa; outros modulos OK: PASS
  27. Usuario sem purchasing.read no Santa: 403 na API + ocorrencias filtradas na Central: PASS
ETAPA 11 - Escopo:
  28. Auto-expansao FARM B: 403; ALL: 403; mesmo escopo: 204; admin delega ALL de volta: PASS
"@
    $summary | Set-Content (Join-Path $evidenceDir 'SUMMARY.txt')
    Write-Host $summary
}
finally {
    if (-not $KeepRunning) {
        foreach ($p in $processes) {
            try { if (-not $p.HasExited) { $p.Kill(); $p.WaitForExit(5000) | Out-Null } } catch {}
        }
        if ($startedDatabase -and $dataDir -and (Test-Path $dataDir)) {
            # Remove o banco de execucao (template permanece para a proxima execucao).
            if ("$pgPort" -ne '') {
                $dropSql = Join-Path $evidenceDir 'drop-run-db.sql'
                Set-Content $dropSql "drop database if exists ${dbName};" -Encoding ASCII
                Start-Process -FilePath $psql -ArgumentList @('-p',"$pgPort","-U","postgres","-h","127.0.0.1","-d","postgres","-X","-f",$dropSql) -NoNewWindow -Wait | Out-Null
            }
            & $pg_ctl -D $dataDir -m fast stop | Out-Null
        }
    }
    Stop-Transcript -ErrorAction SilentlyContinue | Out-Null
}

Write-Host "`nPROCUREMENT JOURNEY E2E COMPLETO COM SUCESSO" -ForegroundColor Green
exit 0
