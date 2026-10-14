param(
    [string]$PostgresBin = 'C:\Program Files\PostgreSQL\18\bin',
    [switch]$KeepRunning,
    [switch]$RebuildTemplate
)

# E2E da jornada "rebanho + identificacao" do sprint Pecuaria P1 (2026-10-12):
#   L1 migration 140 (backfill do ledger canonical livestock_identifier_history): idempotente —
#      reexecucao nao duplica linhas abertas; seed SC-N-1001/1002 com EAR_TAG (+INTERNAL) vigentes.
#   L2 backfill visivel pela leitura canonical (detail): blocos de identificadores vem do historico
#      (mesmos aliases kind/value/assignedOn/retiredOn/reason) e nascimento conhecido x estimado
#      continua explicito (SC-N-1002 estimado, SC-N-1001 conhecido).
#   L3 acesso: 401 sem token; leitor (somente livestock.read) le 200 / escreve 403.
#   L4 registro de animal escreve O LEDGER CANONICO (EAR_TAG sempre; RFID quando presente, motivo
#      REGISTRATION) e NAO a tabela auxiliar legada; tag duplicada (case-insensitive) -> 409
#      livestock.tag_not_unique.
#   L5 troca dedicada de brinco (POST .../tag): motivo obrigatorio (422 sem ele), encerra a linha
#      anterior e abre a nova no ledger na mesma transacao, evento TAG_CHANGE com anterior/atuais,
#      replay sem duplicar linha aberta, conflito 409 tag_not_unique.
#   L6 troca dedicada de RFID (POST .../rfid — rota nova do P1): mesmo contrato do brinco, evento
#      RFID_CHANGE, replay estavel, conflito 409 rfid_not_unique.
#   L7 edicao cadastral generica (PUT animals/{id}) que altera brinco/RFID tambem alimenta o ledger
#      (motivo fixo EDIT_CADASTRAL, eventos TAG_CHANGE/RFID_CHANGE), valida unicidade amigavel e
#      permite remover o RFID (linha encerrada, sem substituto).
#   L8 status canonico: descarte (handling type=DISCARD) -> status 5 lido como DISCARDED nas quatro
#      superficies de leitura (lista v1, detalhe v1, GET 360, detail 360); reserva (status 6) lida
#      como RESERVED nas mesmas quatro (antes divergiam SLAUGHTERED/OBSERVATION/RESERVED).
#   L9 reconciliacao api==db (tag/rfid do animal vs ledger vigente vs detail) + snapshot + dashboard.
#
# Cluster descartavel (template compartilhado pg-journey-template; a corrida precisa dele com a
# migration 140 embutida no full.sql — se ausente, rode com -RebuildTemplate) + API em Release.
# Recuperacao OOM da API: conexao recusada -> reinicia o host na mesma porta e repete 1x (max 3x).

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

$evidenceDir = Join-Path $root ('artifacts\livestock-p1-e2e-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidenceDir | Out-Null
Start-Transcript -Path (Join-Path $evidenceDir 'transcript.log') -Append | Out-Null
$processes = [Collections.Generic.List[Diagnostics.Process]]::new()
$weStartedCluster = $false
$dbName = 'agro360_livp1_' + [guid]::NewGuid().ToString('N').Substring(0, 12)
$templateDir = Join-Path $env:TEMP 'opencode\pg-journey-template'
$templateDb = 'journey_template'
$templateMarker = Join-Path $templateDir 'template-ready.txt'
$fullSqlPath = Join-Path $root 'database/agro360-postgres-full.sql'
$migration140Path = Join-Path $root 'database/migrations/140_livestock_identifier_history_backfill.sql'
$apiPort = 0
$apiHostProc = $null   # processo atual do host (substituido em cada recuperacao OOM)
$apiRestartCount = 0   # teto: 3 recuperacoes por execucao
$knownHash = 'pbkdf2-sha512$210000$Fk496Qko66Cw//8uYyP3mw==$PqcZJTtl+96es6GmUF3khyrZvMJqSVDIe/YfI7TCv2U='
$runTag = (Get-Date).ToString('HHmmss')
$tenantId = ''
$adminUser = ''
$readerUser = [guid]::NewGuid().ToString()
$readerRole = [guid]::NewGuid().ToString()
$farmId = ''
$seedA = '30000000-0000-4000-8000-000000000150'   # SC-N-1001 (reservado, status 6)
$seedB = '30000000-0000-4000-8000-000000000151'   # SC-N-1002 (nascimento estimado)
$animalA = ''
$animalC = ''
$apiExe = ''

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
    $tmp = Join-Path $evidenceDir ([guid]::NewGuid().ToString('N'))
    $tempFile = "$tmp.sql"; $outF = "$tmp.out"; $errF = "$tmp.err"
    [System.IO.File]::WriteAllText($tempFile, $Sql, [System.Text.Encoding]::UTF8)
    $proc = Start-Process -FilePath $psql -ArgumentList @('-p',"$pgPort","-U","postgres","-h","127.0.0.1","-d",$Db,'-X','-A','-t','-q','-v','ON_ERROR_STOP=1','--set=client_min_messages=warning','-f',$tempFile) -NoNewWindow -PassThru -Wait -RedirectStandardOutput $outF -RedirectStandardError $errF
    $code = $proc.ExitCode
    $combined = ''
    if (Test-Path $outF) { $combined += (Get-Content $outF -Raw -ErrorAction SilentlyContinue) }
    if ((Test-Path $errF) -and (Get-Item $errF).Length -gt 0) { $combined += "`n" + (Get-Content $errF -Raw -ErrorAction SilentlyContinue) }
    if ($code -ne 0) { throw "psql failed (exit code $code): $combined | SQL em $tempFile" }
    Remove-Item $tempFile, $outF, $errF -Force -ErrorAction SilentlyContinue
    return $combined.Trim()
}

function Test-ApiTcp([int]$Rounds) {
    for ($i = 0; $i -lt $Rounds; $i++) {
        try { $tcp = [Net.Sockets.TcpClient]::new(); $tcp.Connect('127.0.0.1', $apiPort); $tcp.Close(); return $true } catch {}
        Start-Sleep -Milliseconds 500
    }
    return $false
}

function Start-ApiHostProcess([string]$LogTag) {
    $p = Start-Process dotnet -ArgumentList "`"$apiExe`"" -WorkingDirectory (Split-Path $apiExe) -PassThru -NoNewWindow -RedirectStandardOutput (Join-Path $evidenceDir ("api-${LogTag}.out.log")) -RedirectStandardError (Join-Path $evidenceDir ("api-${LogTag}.err.log"))
    $processes.Add($p) | Out-Null
    return $p
}

# A maquina sofre picos de pressao de RAM de outras apps; o OOM killer do SO pode matar o
# host da API silenciosamente no meio da corrida. Recuperacao: se o pedido nunca chegou
# (conexao RECUSADA), reinicia o host na mesma porta e repete o pedido exatamente UMA vez.
# O Postgres sobrevive a queda; tokens JWT sao stateless -> nada mais precisa re-logar.
function Restart-ApiHost([string]$Why) {
    if ($script:apiRestartCount -ge 3) { throw "A API caiu (${Why}) e nao voltou apos 3 reinicios; veja os logs api-*.log na pasta de evidencia." }
    $script:apiRestartCount++
    Write-Host ("[api-down] " + $Why + "; reiniciando host da API (reinicio $($script:apiRestartCount)) e repetindo o pedido...") -ForegroundColor Yellow
    $old = $script:apiHostProc
    if ($null -ne $old -and -not $old.HasExited) { try { Stop-Process -Id $old.Id -Force } catch {} }
    $script:apiHostProc = Start-ApiHostProcess ("r$($script:apiRestartCount)")
    if (-not (Test-ApiTcp 60)) { throw "A API nao escutou apos o reinicio (${Why})" }
    Start-Sleep -Seconds 2
}

function Is-ConnRefused([System.Management.Automation.ErrorRecord]$Err) {
    $msg = "$($Err.Exception.Message)"
    if ($msg -match 'conectar-se ao servidor remoto|Connection refused|No connection could be made') { return $true }
    $inner = $Err.Exception.InnerException
    while ($inner) { if ($inner -is [Net.Sockets.SocketException]) { return $true }; $inner = $inner.InnerException }
    return $false
}

function Invoke-Api([string]$Method, [string]$Path, $Body = $null, [string]$Token = '') {
    $url = "http://127.0.0.1:${apiPort}${Path}"
    $headers = @{ 'Content-Type' = 'application/json' }
    if ($Token) { $headers['Authorization'] = "Bearer $Token" }
    $params = @{ Method = $Method; Uri = $url; Headers = $headers; UseBasicParsing = $true; TimeoutSec = 60 }
    if ($null -ne $Body) {
        $json = $Body | ConvertTo-Json -Depth 8 -Compress
        $params['Body'] = [Text.Encoding]::UTF8.GetBytes($json)
    }
    $triedRestart = $false
    while ($true) {
        try {
            $resp = Invoke-WebRequest @params
            $content = $resp.Content
            $result = $null
            if ("$($resp.Headers['Content-Type'])" -like '*json*' -and $content -and $content.Length -gt 1) {
                try { $result = $content | ConvertFrom-Json } catch { $result = $content }
            }
            return @{ Status = [int]$resp.StatusCode; Body = $result; Raw = $content }
        } catch {
            $resp = $_.Exception.Response
            if ($null -ne $resp) {
                $content = ''
                if ($_.ErrorDetails -and $_.ErrorDetails.Message) { $content = $_.ErrorDetails.Message }
                else {
                    try {
                        $stream = $resp.GetResponseStream()
                        $reader = [IO.StreamReader]::new($stream)
                        $content = $reader.ReadToEnd()
                    } catch { $content = '' }
                }
                $result = if ($content -and $content.Length -gt 2) { try { $content | ConvertFrom-Json } catch { $content } } else { $null }
                return @{ Status = [int]$resp.StatusCode; Body = $result; Raw = $content }
            }
            # sem resposta HTTP = transporte. So em conexao recusada (host morto) o pedido
            # certamente nao foi processado -> segura para reiniciar + repetir UMA vez.
            if ($triedRestart -or -not (Is-ConnRefused $_)) { throw "HTTP ${Method} ${Path}: $($_.Exception.Message)" }
            Restart-ApiHost ("conexao recusada em ${Method} ${Path}")
            $triedRestart = $true
        }
    }
}

function Stop-Proc([Diagnostics.Process]$Proc) {
    if ($null -ne $Proc -and -not $Proc.HasExited) {
        try { $Proc.Kill() } catch {}
    }
}

try {
    # ============ SETUP: cluster com template duravel ============
    $pgPort = Get-FreePort
    $needsStart = $true
    if (-not (Test-Path $templateMarker) -or $RebuildTemplate) {
        try {
            Write-Host "[build] construindo template duravel em $templateDir (carga completa do SQL; em geral < 5 min)..." -ForegroundColor Cyan
            if (Test-Path $templateDir) {
                Write-Host '[build] removendo estado anterior (parcial ou recarga forcada)...'
                & $pg_ctl -D $templateDir -m fast stop | Out-Null
                for ($rTry = 0; $rTry -lt 8; $rTry++) {
                    Start-Sleep -Seconds 1
                    try { Remove-Item $templateDir -Recurse -Force -ErrorAction Stop; break } catch {}
                }
                if (Test-Path $templateDir) { throw 'nao foi possivel remover o diretorio do template anterior' }
            }
            $t0 = Get-Date
            $iniErrF = Join-Path $evidenceDir 'initdb-err.txt'
            $ini = Start-Process -FilePath $initdb -ArgumentList @('-D',$templateDir,'-U','postgres','-A','trust','--encoding=UTF8','--locale=C') -NoNewWindow -PassThru -Wait -RedirectStandardOutput (Join-Path $evidenceDir 'initdb-out.txt') -RedirectStandardError $iniErrF
            if ($ini.ExitCode -ne 0) { throw "initdb falhou (exit $($ini.ExitCode)): " + (Get-Content $iniErrF -Raw -ErrorAction SilentlyContinue) }
            & $pg_ctl -D $templateDir -l (Join-Path $evidenceDir 'template-startup.log') -o "-h 127.0.0.1 -p $pgPort" -w start
            if ($LASTEXITCODE -ne 0) { throw "pg_ctl start do template falhou na porta $pgPort" }
            $ddlTpl = Join-Path $evidenceDir 'create-template-db.sql'
            Set-Content $ddlTpl "create database ${templateDb};" -Encoding ASCII
            $cdProc = Start-Process -FilePath $psql -ArgumentList @('-p',"$pgPort","-U","postgres","-h","127.0.0.1","-d","postgres","-X","-v","ON_ERROR_STOP=1","-f",$ddlTpl) -NoNewWindow -PassThru -Wait -RedirectStandardOutput (Join-Path $evidenceDir 'create-template-db.out.txt') -RedirectStandardError (Join-Path $evidenceDir 'create-template-db.err.txt')
            if ($cdProc.ExitCode -ne 0) { throw "create database do template falhou (exit $($cdProc.ExitCode))" }
            $prelude = Join-Path $evidenceDir 'prelude.sql'
            Set-Content -Path $prelude -Value 'SET synchronous_commit=off; SET statement_timeout=2700000;' -Encoding ASCII
            Write-Host '[build] carga completa do SQL em andamento...'
            $loadProc = Start-Process -FilePath $psql -ArgumentList @('-p',"$pgPort","-U","postgres","-h","127.0.0.1","-d",$templateDb,'-X','-v','ON_ERROR_STOP=1','--set=client_min_messages=warning','-f',$prelude,'-f',$fullSqlPath) -NoNewWindow -PassThru -Wait -RedirectStandardOutput (Join-Path $evidenceDir 'template-load-out.log') -RedirectStandardError (Join-Path $evidenceDir 'template-load-err.log')
            if ($loadProc.ExitCode -ne 0) { throw "carga do SQL completo falhou (exit $($loadProc.ExitCode)); detalhes em template-load-err.log" }
            Set-Content -Path $templateMarker -Value ("ok {0:s}" -f (Get-Date))
            Write-Host ("[build] template pronto em " + ((Get-Date) - $t0).TotalSeconds.ToString('0.#') + "s") -ForegroundColor Green
            $needsStart = $false
            $weStartedCluster = $true
        } catch {
            $dump = $_ | Out-String
            foreach ($lf in @('template-load-out.log','template-load-err.log')) {
                $lp = Join-Path $evidenceDir $lf
                if (Test-Path $lp) { $dump += "`n=== TAIL $lf ===`n" + ((Get-Content $lp -Tail 40) -join "`n") }
            }
            $dump | Set-Content (Join-Path $evidenceDir 'build-error.txt')
            throw
        }
    } else {
        Write-Host "Template existente em $templateDir (use -RebuildTemplate para forcar recarga; o schema precisa ter 140.0.0)." -ForegroundColor Cyan
    }

    $pidFile = Join-Path $templateDir 'postmaster.pid'
    if ((Test-Path $pidFile) -and $needsStart) {
        $stalePid = ((Get-Content $pidFile) | Select-Object -First 1).Trim()
        $staleProc = $null
        try { $staleProc = Get-Process -Id ([int]$stalePid) -ErrorAction Stop } catch {}
        if ($null -eq $staleProc) { Remove-Item $pidFile -Force }
        else { throw "O diretorio do template parece estar em uso por outro processo (pid $stalePid)." }
    }

    if ($needsStart) {
        $pgLog = Join-Path $evidenceDir 'postgres.log'
        & $pg_ctl -D $templateDir -l $pgLog -o "-h 127.0.0.1 -p $pgPort" -w start
        if ($LASTEXITCODE -ne 0) { throw "pg_ctl start falhou na porta $pgPort" }
        $weStartedCluster = $true
    }
    $pingSql = Join-Path $evidenceDir 'ping.sql'
    Set-Content $pingSql 'select 1;' -Encoding ASCII
    $pingOut = Join-Path $evidenceDir 'ping-out.txt'
    $pingErrF = Join-Path $evidenceDir 'ping-err.txt'
    Remove-Item $pingOut, $pingErrF -Force -ErrorAction SilentlyContinue
    Start-Process -FilePath $psql -ArgumentList @('-p',"$pgPort","-U","postgres","-h","127.0.0.1","-d","postgres","-X","-A","-t","-q","-f",$pingSql) -NoNewWindow -Wait -RedirectStandardOutput $pingOut -RedirectStandardError $pingErrF | Out-Null
    $ping = (Get-Content $pingOut -Raw -ErrorAction SilentlyContinue).Trim()
    Assert-Step 'Cluster-responde-select-1' ($ping -eq '1') "ping=$ping"

    $ddlRun = Join-Path $evidenceDir 'create-run-db.sql'
    Set-Content $ddlRun "create database ${dbName} template ${templateDb};" -Encoding ASCII
    $crProc = Start-Process -FilePath $psql -ArgumentList @('-p',"$pgPort","-U","postgres","-h","127.0.0.1","-d","postgres","-X","-v","ON_ERROR_STOP=1","-f",$ddlRun) -NoNewWindow -PassThru -Wait -RedirectStandardOutput (Join-Path $evidenceDir 'create-run-db.out.txt') -RedirectStandardError (Join-Path $evidenceDir 'create-run-db.err.txt')
    if ($crProc.ExitCode -ne 0) { throw "create database a partir do template falhou (exit $($crProc.ExitCode))" }
    Write-Host "PostgreSQL pronto na porta $pgPort (db=${dbName} clonado do template)" -ForegroundColor Cyan

    $ver140 = Invoke-Psql "select version from agro360.platform_schema_versions where version='140.0.0';"
    Assert-Step 'Schema-140.0.0-present' ($ver140 -eq '140.0.0') 'mig 140 (backfill ledger identificadores) instalada; se ausente, rode com -RebuildTemplate'
    $ver139 = Invoke-Psql "select version from agro360.platform_schema_versions where version='139.0.0';"
    Assert-Step 'Schema-139.0.0-present' ($ver139 -eq '139.0.0') 'mig 139 (receivable x fulfillment) instalada; garante o estado previo completo'

    # ============ CONFIG DA EXECUCAO ============
    $connStr = "Host=127.0.0.1;Port=${pgPort};Database=${dbName};Username=postgres;Search Path=agro360;Command Timeout=60"
    $env:ConnectionStrings__Agro360 = $connStr
    $env:ConnectionStrings__DefaultConnection = $connStr
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:Jwt__SigningKey = 'e2e-test-signing-key-32bytes-minimum!'
    $env:PGPASSWORD = 'trust-auth-placeholder'
    # Memoria: em maquina com pouco RAM livre o OOM killer do SO matou o host da API no meio da
    # execucao (observado varias vezes, ~1 MB livres, sem WER). Teto duro no heap do GC + GC
    # conservador para o processo sobreviver a execucoes pesadas.
    $env:DOTNET_GCHeapHardLimit = '1400000000'
    $env:DOTNET_GCConserveMemory = '9'

    # ============ START API HOST ============
    $apiPort = Get-FreePort
    $env:ASPNETCORE_URLS = "http://127.0.0.1:${apiPort}"
    $apiExe = Join-Path $root 'src\Hosts\Agro360.Api\bin\Release\net10.0\Agro360.Api.dll'
    if (-not (Test-Path $apiExe)) { throw "API binary not found at $apiExe. Run build first." }
    $script:apiHostProc = Start-ApiHostProcess 'init'
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
        if ($script:apiHostProc.HasExited) { break }
    }
    if (-not $ready -and $script:apiHostProc.HasExited) {
        # Morte antes do ready (OOM durante o start): recupera em vez de falhar a execucao.
        Restart-ApiHost 'processo inicial terminou durante o start'
        $ready = Test-ApiTcp 60
    }
    if (-not $ready) {
        $errLog = Join-Path $evidenceDir 'api-init.err.log'
        if (Test-Path $errLog) { (Get-Content $errLog -Tail 40) | ForEach-Object { Write-Host "API-ERR: $_" -ForegroundColor Red } }
    }
    Assert-Step 'API-tcp-ready' $ready "port=$apiPort after $($i+1) probes"
    Start-Sleep -Seconds 2

    # ============ TENANT / USUARIOS / MODULO ============
    $tenantId = Invoke-Psql "select id::text from agro360.tenancy_tenants where slug='santa-clara';"
    Assert-Step 'Tenant-santa-clara' ($tenantId -ne '') "tenant=$tenantId"
    $farmId = Invoke-Psql "select id::text from agro360.geo_farms where tenant_id='${tenantId}' and registration_number='SC-SEDE-001' and deleted_at is null;"
    Assert-Step 'Propriedade-fixture' ($farmId -ne '') "farm=$farmId"
    Invoke-Psql "update agro360.identity_users set status='ACTIVE', password_hash='$knownHash', must_change_password=false, updated_at=now() where tenant_id='${tenantId}' and lower(email) in ('admin@santaclara.agro360.local','operador.santaclara@agro360.local');" | Out-Null

    # Modulo pecuaria (permissoes livestock.* filtradas pelos modulos contratados NO LOGIN).
    Invoke-Psql @"
insert into agro360.platform_tenant_module_entitlements(tenant_id,module_id,status,reason,activated_at)
select '${tenantId}', id, 'ACTIVE', 'E2E pecuaria P1 - modulo pecuaria', now()
from agro360.platform_module_catalog where code='livestock'
on conflict(tenant_id,module_id) do update set status='ACTIVE';
"@ | Out-Null

    # Leitor dedicado: papel somente-read de pecuaria -> 200 em leitura e 403 em escrita
    # (deterministico: nao depende do estado das permissaoes do operador seed).
    Invoke-Psql @"
insert into agro360.identity_roles(id,tenant_id,code,name,is_system)
values('${readerRole}','${tenantId}','E2E_P1_LIVESTOCK_READ_ONLY','Leitor Pecuaria E2E P1',false)
on conflict (tenant_id,code) do nothing;
insert into agro360.identity_users(id,tenant_id,name,email,password_hash,status,must_change_password,created_by)
select '${readerUser}','${tenantId}','Leitor Pecuaria E2E P1','e2e.p1.reader@santaclara.agro360.local','$knownHash','ACTIVE',false,'$(Invoke-Psql "select id::text from agro360.identity_users where tenant_id='${tenantId}' and lower(email)='admin@santaclara.agro360.local' and deleted_at is null;")'
where not exists(select 1 from agro360.identity_users where tenant_id='${tenantId}' and lower(email)='e2e.p1.reader@santaclara.agro360.local');
insert into agro360.identity_user_roles(tenant_id,user_id,role_id)
values('${tenantId}','${readerUser}','${readerRole}')
on conflict do nothing;
insert into agro360.identity_role_permissions(tenant_id,role_id,permission_id)
select '${tenantId}','${readerRole}',id from agro360.identity_permissions where code='livestock.read'
on conflict do nothing;
-- escopo de unidade (middleware TenantContext exige >=1 linha; sem ela tudo vira 403
-- forbidden_unit_scope antes mesmo da politica de permissao). Escopo FARM na fazenda de teste:
-- valido o caminho de auto-selecao de escopo unico + filtragem por fazenda.
insert into agro360.identity_user_unit_scopes(id,tenant_id,user_id,scope_type,farm_id)
select gen_random_uuid(),'${tenantId}','${readerUser}','FARM','${farmId}'
where not exists(select 1 from agro360.identity_user_unit_scopes where tenant_id='${tenantId}' and user_id='${readerUser}' and scope_type='FARM' and farm_id='${farmId}');
"@ | Out-Null

    $adminLogin = Invoke-Api POST '/api/v1/auth/login' @{ TenantSlug = 'santa-clara'; Email = 'admin@santaclara.agro360.local'; Password = 'Agro360Admin@2024!' }
    Assert-Step 'Login-admin-santa' ($adminLogin.Status -eq 200) "HTTP $($adminLogin.Status)"
    $adminToken = $adminLogin.Body.accessToken
    $readerLogin = Invoke-Api POST '/api/v1/auth/login' @{ TenantSlug = 'santa-clara'; Email = 'e2e.p1.reader@santaclara.agro360.local'; Password = 'Agro360Admin@2024!' }
    Assert-Step 'Login-leitor-santa' ($readerLogin.Status -eq 200) "HTTP $($readerLogin.Status)"
    $readerToken = $readerLogin.Body.accessToken
    $adminUser = Invoke-Psql "select id::text from agro360.identity_users where tenant_id='${tenantId}' and lower(email)='admin@santaclara.agro360.local' and deleted_at is null;"
    Assert-Step 'Usuarios-fixtures' ($adminUser -ne '') "admin=$adminUser reader=$readerUser"

    # Diagnosticas de acesso (P1): permissao efetiva do leitor x modulos contratados, antes do L3.
    # O handler autoriza ao vivo (join user->role->permission + ModuleCodeSelect), entao estas
    # duas verifies isolam se um 403 viria da permissao ou do contrato do modulo.
    $diagPerms = Invoke-Psql "select coalesce(string_agg(p.code, ',' order by p.code), '') from agro360.identity_user_roles ur join agro360.identity_role_permissions rp on rp.tenant_id=ur.tenant_id and rp.role_id=ur.role_id join agro360.identity_permissions p on p.id=rp.permission_id where ur.tenant_id='${tenantId}' and ur.user_id='${readerUser}';"
    Assert-Step 'Diag-leitor-permissao-papel' ($diagPerms -eq 'livestock.read') "perms_efetivas=$diagPerms"
    $diagModules = Invoke-Psql @"
select coalesce(string_agg(distinct lower(c.code), ',' order by lower(c.code)), '')
from agro360.platform_tenant_module_entitlements e
join agro360.platform_module_catalog c on c.id=e.module_id
where e.tenant_id='${tenantId}' and e.status in ('CONTRACTED','ACTIVE','TRIAL')
  and (e.activated_at is null or e.activated_at <= now())
  and (e.valid_until is null or e.valid_until > now());
"@
    Assert-Step 'Diag-modulo-pecuaria-contratado' ($diagModules -match '(^|,)livestock(,|$)') "modulos_contratados=$diagModules"

    # ============ L1: MIGRATION 140 — IDEMPOTENCIA + BACKFILL DO LEDGER CANONICO ============
    Write-Host '--- L1: backfill idempotente do historico canonico de identificadores ---' -ForegroundColor Cyan
    $hCountBefore = Invoke-Psql "select count(*)::text from agro360.livestock_identifier_history where tenant_id='${tenantId}';"
    $migSql = Get-Content $migration140Path -Raw -Encoding UTF8
    Invoke-Psql $migSql | Out-Null
    $hCountAfter = Invoke-Psql "select count(*)::text from agro360.livestock_identifier_history where tenant_id='${tenantId}';"
    Assert-Step 'L1-reexecucao-140-nao-duplica' ($hCountBefore -eq $hCountAfter) "linhas ${hCountBefore} -> ${hCountAfter} apos reexecutar a migration"
    $dupOpen = Invoke-Psql @"
select count(*)::text from (
  select 1 from agro360.livestock_identifier_history
  where tenant_id='${tenantId}' and valid_until is null
  group by animal_id, identifier_type having count(*) > 1
) x;
"@
    Assert-Step 'L1-max-1-linha-aberta-por-animal-e-tipo' ($dupOpen -eq '0') "grupos duplicados=$dupOpen"
    $seedEar = Invoke-Psql "select identifier || '|' || change_reason from agro360.livestock_identifier_history where tenant_id='${tenantId}' and animal_id='${seedA}' and identifier_type='EAR_TAG' and valid_until is null;"
    Assert-Step 'L1-seed-1001-ear-tag-vigente' ($seedEar -eq 'SC-N-1001|REGISTRATION') "row=$seedEar"
    $seedInt = Invoke-Psql "select count(*)::text from agro360.livestock_identifier_history where tenant_id='${tenantId}' and animal_id='${seedB}' and identifier_type='INTERNAL' and valid_until is null;"
    Assert-Step 'L1-seed-1002-internal-vigente' ($seedInt -eq '1') "internal rows=$seedInt"

    # ============ L2: BACKFILL VISIVEL PELA LEITURA CANONICA (DETAIL) ============
    Write-Host '--- L2: detail expoe o historico canônico + nascimento conhecido x estimado ---' -ForegroundColor Cyan
    $detA = Invoke-Api GET "/api/livestock/animals/${seedA}/detail" -Token $adminToken
    Assert-Step 'L2-detail-1001-200' ($detA.Status -eq 200) "HTTP $($detA.Status)"
    $idRows = @($detA.Body.identifiers)
    $earRow = @($idRows | Where-Object { "$($_.kind)" -eq 'EAR_TAG' -and "$($_.value)" -eq 'SC-N-1001' }) | Select-Object -First 1
    Assert-Step 'L2-detail-identificadores-do-historico' ($null -ne $earRow -and "$($earRow.retiredOn)".Trim() -in @('', 'null')) "linhas=$($idRows.Count) earTagAberta=$($null -ne $earRow)"
    $hasInternal = @($idRows | Where-Object { "$($_.kind)" -eq 'INTERNAL' }).Count -ge 1
    Assert-Step 'L2-detail-internal-historico' $hasInternal "identifiers includes INTERNAL do backfill"
    Assert-Step 'L2-nascimento-conhecido-1001' ("$($detA.Body.animal.birthDateEstimated)" -eq 'False') "birthDateEstimated=$($detA.Body.animal.birthDateEstimated)"
    $detB = Invoke-Api GET "/api/livestock/animals/${seedB}/detail" -Token $adminToken
    Assert-Step 'L2-nascimento-estimado-1002' ("$($detB.Body.animal.birthDateEstimated)" -eq 'True') "birthDateEstimated=$($detB.Body.animal.birthDateEstimated)"

    # ============ L3: ACESSO (401 / 403 / 200) ============
    Write-Host '--- L3: controle de acesso da jornada de rebanho ---' -ForegroundColor Cyan
    $anon = Invoke-Api GET '/api/livestock/animals?pageSize=5'
    Assert-Step 'L3-anonimo-401' ($anon.Status -eq 401) "HTTP $($anon.Status)"
    $readOk = Invoke-Api GET '/api/livestock/animals?pageSize=5' -Token $readerToken
    Assert-Step 'L3-leitor-le-200' ($readOk.Status -eq 200) "HTTP $($readOk.Status) $($readOk.Raw)"
    $readerReg = Invoke-Api POST '/api/livestock/animals' @{ FarmId = $farmId; Tag = "E2E-P1-X${runTag}"; Species = 'BOVINE'; Breed = 'Nelore'; Sex = 'M'; BirthDate = (Get-Date).AddMonths(-12).ToString('yyyy-MM-dd') } -Token $readerToken
    Assert-Step 'L3-leitor-registra-403' ($readerReg.Status -eq 403) "HTTP $($readerReg.Status) $($readerReg.Raw)"
    $readerTag = Invoke-Api POST "/api/livestock/animals/${seedB}/tag" @{ NewTag = "E2E-P1-Y${runTag}"; ChangedOn = (Get-Date).ToString('yyyy-MM-dd'); Reason = 'tentativa sem permissao' } -Token $readerToken
    Assert-Step 'L3-leitor-troca-brinco-403' ($readerTag.Status -eq 403) "HTTP $($readerTag.Status) $($readerTag.Raw)"

    # ============ L4: REGISTRO ESCREVE O LEDGER CANONICO (E NAO A AUXILIAR) ============
    Write-Host '--- L4: registro de animal abre linhas EAR_TAG/RFID no historico canônico ---' -ForegroundColor Cyan
    $today = (Get-Date).ToString('yyyy-MM-dd')
    $birthA = (Get-Date).AddMonths(-20).ToString('yyyy-MM-dd')
    $regA = Invoke-Api POST '/api/livestock/animals' @{ FarmId = $farmId; Tag = 'E2E-P1-A'; Rfid = 'RFID-E2E-P1-A'; Species = 'BOVINE'; Breed = 'Nelore'; Sex = 'M'; BirthDate = $birthA } -Token $adminToken
    Assert-Step 'L4-registro-A-201' ($regA.Status -eq 201) "HTTP $($regA.Status) $($regA.Raw)"
    $animalA = Invoke-Psql "select id::text from agro360.livestock_animals where tenant_id='${tenantId}' and tag='E2E-P1-A' and deleted_at is null;"
    Assert-Step 'L4-animais-no-banco' ($animalA -ne '') "animalA=$animalA"
    $histA = (Invoke-Psql @"
select identifier_type || '|' || identifier || '|' || change_reason from agro360.livestock_identifier_history
where tenant_id='${tenantId}' and animal_id='${animalA}' and valid_until is null order by identifier_type;
"@) -replace "`r",""
    Assert-Step 'L4-ledger-aberto-A' ($histA -eq "EAR_TAG|E2E-P1-A|REGISTRATION`nRFID|RFID-E2E-P1-A|REGISTRATION") "rows=$histA"
    $auxA = Invoke-Psql "select count(*)::text from agro360.livestock_animal_identifiers where tenant_id='${tenantId}' and animal_id='${animalA}';"
    Assert-Step 'L4-auxiliar-legada-sem-escrita' ($auxA -eq '0') "aux rows=$auxA (a partir do P1 so o ledger recebe escrita)"
    $regDup = Invoke-Api POST '/api/livestock/animals' @{ FarmId = $farmId; Tag = 'e2e-p1-a'; Species = 'BOVINE'; Breed = 'Nelore'; Sex = 'M'; BirthDate = $birthA } -Token $adminToken
    Assert-Step 'L4-tag-duplicada-case-insensitive-409' ($regDup.Status -eq 409 -and $regDup.Raw -match 'tag_not_unique') "HTTP $($regDup.Status) $($regDup.Raw)"

    $regC = Invoke-Api POST '/api/livestock/animals' @{ FarmId = $farmId; Tag = 'E2E-P1-C'; Rfid = 'RFID-E2E-P1-C'; Species = 'BOVINE'; Breed = 'Nelore'; Sex = 'M'; BirthDate = $birthA } -Token $adminToken
    Assert-Step 'L4-registro-C-201' ($regC.Status -eq 201) "HTTP $($regC.Status)"
    $animalC = Invoke-Psql "select id::text from agro360.livestock_animals where tenant_id='${tenantId}' and tag='E2E-P1-C' and deleted_at is null;"
    Assert-Step 'L4-animais-C-no-banco' ($animalC -ne '') "animalC=$animalC"

    # ============ L5: TROCA DEDICADA DE BRINCO ============
    Write-Host '--- L5: POST .../tag encerra+abre no ledger com motivo obrigatório ---' -ForegroundColor Cyan
    $tagCmd = @{ NewTag = 'E2E-P1-B'; ChangedOn = $today; Reason = 'Troca de brinco E2E P1' }
    $tag1 = Invoke-Api POST "/api/livestock/animals/${animalA}/tag" $tagCmd -Token $adminToken
    Assert-Step 'L5-troca-brinco-204' ($tag1.Status -eq 204) "HTTP $($tag1.Status) $($tag1.Raw)"
    $histA2 = (Invoke-Psql @"
select coalesce(identifier,'~') || '|' || coalesce(change_reason,'~') || '|' || case when valid_until is null then 'ABERTA' else 'ENCERRADA' end
from agro360.livestock_identifier_history
where tenant_id='${tenantId}' and animal_id='${animalA}' and identifier_type='EAR_TAG' order by valid_from desc limit 2;
"@) -replace "`r",""
    Assert-Step 'L5-ledger-encerra-antiga-abre-nova' ($histA2 -eq "E2E-P1-B|Troca de brinco E2E P1|ABERTA`nE2E-P1-A|REGISTRATION|ENCERRADA") "rows=$histA2"
    $tagNow = Invoke-Psql "select tag from agro360.livestock_animals where tenant_id='${tenantId}' and id='${animalA}';"
    Assert-Step 'L5-animals-tag-atualizada' ($tagNow -eq 'E2E-P1-B') "tag=$tagNow"
    $evtTag = Invoke-Psql "select (jsonb(data)->>'previous') || '->' || (jsonb(data)->>'current') || '|' || (jsonb(data)->>'reason') from agro360.livestock_animal_events where tenant_id='${tenantId}' and animal_id='${animalA}' and event_type='TAG_CHANGE' order by created_at desc limit 1;"
    Assert-Step 'L5-evento-TAG_CHANGE-com-anterior-e-motivo' ($evtTag -eq 'E2E-P1-A->E2E-P1-B|Troca de brinco E2E P1') "event=$evtTag"

    $tagReplay = Invoke-Api POST "/api/livestock/animals/${animalA}/tag" $tagCmd -Token $adminToken
    Assert-Step 'L5-replay-aceito-204' ($tagReplay.Status -eq 204) "HTTP $($tagReplay.Status) $($tagReplay.Raw)"
    $openCount = Invoke-Psql "select count(*)::text from agro360.livestock_identifier_history where tenant_id='${tenantId}' and animal_id='${animalA}' and identifier_type='EAR_TAG' and valid_until is null;"
    Assert-Step 'L5-replay-sem-duplicar-linha-aberta' ($openCount -eq '1') "abertas=$openCount"

    $noReason = Invoke-Api POST "/api/livestock/animals/${animalA}/tag" @{ NewTag = 'E2E-P1-Z'; ChangedOn = $today } -Token $adminToken
    Assert-Step 'L5-422-sem-motivo' ($noReason.Status -eq 422 -and $noReason.Raw -match 'Reason\.required') "HTTP $($noReason.Status) $($noReason.Raw)"

    $conflict = Invoke-Api POST "/api/livestock/animals/${animalC}/tag" @{ NewTag = 'E2E-P1-B'; ChangedOn = $today; Reason = 'deve conflitar' } -Token $adminToken
    Assert-Step 'L5-conflicto-tag-409' ($conflict.Status -eq 409 -and $conflict.Raw -match 'tag_not_unique') "HTTP $($conflict.Status) $($conflict.Raw)"

    # ============ L6: TROCA DEDICADA DE RFID (ROTA NOVA DO P1) ============
    Write-Host '--- L6: POST .../rfid (novo) com mesmo contrato do brinco ---' -ForegroundColor Cyan
    $rfidCmd = @{ NewRfid = 'RFID-E2E-P1-D'; ChangedOn = $today; Reason = 'Relogio novo E2E P1' }
    $rf1 = Invoke-Api POST "/api/livestock/animals/${animalA}/rfid" $rfidCmd -Token $adminToken
    Assert-Step 'L6-troca-rfid-204' ($rf1.Status -eq 204) "HTTP $($rf1.Status) $($rf1.Raw)"
    $histR = (Invoke-Psql @"
select coalesce(identifier,'~') || '|' || coalesce(change_reason,'~') || '|' || case when valid_until is null then 'ABERTA' else 'ENCERRADA' end
from agro360.livestock_identifier_history
where tenant_id='${tenantId}' and animal_id='${animalA}' and identifier_type='RFID' order by valid_from desc limit 2;
"@) -replace "`r",""
    Assert-Step 'L6-ledger-rfid-encerra-antiga-abre-nova' ($histR -eq "RFID-E2E-P1-D|Relogio novo E2E P1|ABERTA`nRFID-E2E-P1-A|REGISTRATION|ENCERRADA") "rows=$histR"
    $rfidNow = Invoke-Psql "select coalesce(rfid,'~') from agro360.livestock_animals where tenant_id='${tenantId}' and id='${animalA}';"
    Assert-Step 'L6-animals-rfid-atualizado' ($rfidNow -eq 'RFID-E2E-P1-D') "rfid=$rfidNow"
    $evtR = Invoke-Psql "select (jsonb(data)->>'previous') || '->' || (jsonb(data)->>'current') || '|' || (jsonb(data)->>'reason') from agro360.livestock_animal_events where tenant_id='${tenantId}' and animal_id='${animalA}' and event_type='RFID_CHANGE' order by created_at desc limit 1;"
    Assert-Step 'L6-evento-RFID_CHANGE-com-anterior-e-motivo' ($evtR -eq 'RFID-E2E-P1-A->RFID-E2E-P1-D|Relogio novo E2E P1') "event=$evtR"

    $rfReplay = Invoke-Api POST "/api/livestock/animals/${animalA}/rfid" $rfidCmd -Token $adminToken
    Assert-Step 'L6-replay-aceito-204' ($rfReplay.Status -eq 204) "HTTP $($rfReplay.Status)"
    $openR = Invoke-Psql "select count(*)::text from agro360.livestock_identifier_history where tenant_id='${tenantId}' and animal_id='${animalA}' and identifier_type='RFID' and valid_until is null;"
    Assert-Step 'L6-replay-sem-duplicar-linha-aberta' ($openR -eq '1') "abertas=$openR"

    $rfNoReason = Invoke-Api POST "/api/livestock/animals/${animalA}/rfid" @{ NewRfid = 'RFID-E2E-P1-Z'; ChangedOn = $today } -Token $adminToken
    Assert-Step 'L6-422-sem-motivo' ($rfNoReason.Status -eq 422 -and $rfNoReason.Raw -match 'Reason\.required') "HTTP $($rfNoReason.Status) $($rfNoReason.Raw)"

    $rfConflict = Invoke-Api POST "/api/livestock/animals/${animalA}/rfid" @{ NewRfid = 'RFID-E2E-P1-C'; ChangedOn = $today; Reason = 'deve conflitar' } -Token $adminToken
    Assert-Step 'L6-conflicto-rfid-409' ($rfConflict.Status -eq 409 -and $rfConflict.Raw -match 'rfid_not_unique') "HTTP $($rfConflict.Status) $($rfConflict.Raw)"

    # ============ L7: EDICAO CADASTRAL GENERICA (PUT) ALIMENTA O LEDGER ============
    Write-Host '--- L7: PUT animals/{id} alterando brinco/RFID -> EDIT_CADASTRAL no ledger ---' -ForegroundColor Cyan
    $putBody = @{ FarmId = $farmId; Tag = 'E2E-P1-E'; Rfid = 'RFID-E2E-P1-D'; Species = 'BOVINE'; Breed = 'Nelore'; Sex = 'M'; BirthDate = $birthA }
    $put1 = Invoke-Api PUT "/api/livestock/animals/${animalA}" $putBody -Token $adminToken
    Assert-Step 'L7-put-muda-brinco-200' ($put1.Status -eq 200) "HTTP $($put1.Status) $($put1.Raw)"
    $histE = Invoke-Psql "select identifier || '|' || change_reason from agro360.livestock_identifier_history where tenant_id='${tenantId}' and animal_id='${animalA}' and identifier_type='EAR_TAG' and valid_until is null;"
    Assert-Step 'L7-ledger-edit-cadastral' ($histE -eq 'E2E-P1-E|EDIT_CADASTRAL') "row=$histE"
    $evtE = Invoke-Psql "select (jsonb(data)->>'previous') || '->' || (jsonb(data)->>'current') || '|' || (jsonb(data)->>'reason') from agro360.livestock_animal_events where tenant_id='${tenantId}' and animal_id='${animalA}' and event_type='TAG_CHANGE' order by created_at desc limit 1;"
    Assert-Step 'L7-evento-TAG_CHANGE-edit-cadastral' ($evtE -eq 'E2E-P1-B->E2E-P1-E|EDIT_CADASTRAL') "event=$evtE"

    $putDup = @{ FarmId = $farmId; Tag = 'SC-N-1001'; Rfid = 'RFID-E2E-P1-D'; Species = 'BOVINE'; Breed = 'Nelore'; Sex = 'M'; BirthDate = $birthA }
    $put2 = Invoke-Api PUT "/api/livestock/animals/${animalA}" $putDup -Token $adminToken
    Assert-Step 'L7-put-tag-duplicada-409' ($put2.Status -eq 409 -and $put2.Raw -match 'tag_not_unique') "HTTP $($put2.Status) $($put2.Raw)"

    $putClear = @{ FarmId = $farmId; Tag = 'E2E-P1-E'; Rfid = $null; Species = 'BOVINE'; Breed = 'Nelore'; Sex = 'M'; BirthDate = $birthA }
    $put3 = Invoke-Api PUT "/api/livestock/animals/${animalA}" $putClear -Token $adminToken
    Assert-Step 'L7-put-remove-rfid-200' ($put3.Status -eq 200) "HTTP $($put3.Status) $($put3.Raw)"
    $openRClear = Invoke-Psql "select count(*)::text from agro360.livestock_identifier_history where tenant_id='${tenantId}' and animal_id='${animalA}' and identifier_type='RFID' and valid_until is null;"
    Assert-Step 'L7-rfid-encerrado-sem-substituto' ($openRClear -eq '0') "abertas=$openRClear"
    $rfidAfter = Invoke-Psql "select coalesce(rfid,'~') from agro360.livestock_animals where tenant_id='${tenantId}' and id='${animalA}';"
    Assert-Step 'L7-animals-rfid-nulo' ($rfidAfter -eq '~') "rfid=$rfidAfter"

    # ============ L8: STATUS CANONICO — DESCARTE => DISCARDED EM TODAS AS QUATRO LEITURAS ============
    Write-Host '--- L8: descarte (DISCARD) -> status 5 lido como DISCARDED; reserva lida como RESERVED ---' -ForegroundColor Cyan
    $hdl = Invoke-Api POST '/api/livestock/handling-events' @{ AnimalId = $animalA; Type = 'DISCARD'; OccurredOn = $today; Responsible = "E2E P1 ${runTag}"; EstimatedCost = 0 } -Token $adminToken
    Assert-Step 'L8-handling-discard-201' ($hdl.Status -eq 201) "HTTP $($hdl.Status) $($hdl.Raw)"
    $st5 = Invoke-Psql "select status::text from agro360.livestock_animals where tenant_id='${tenantId}' and id='${animalA}';"
    Assert-Step 'L8-status-5-no-banco' ($st5 -eq '5') "status=$st5"

    $listA = Invoke-Api GET '/api/livestock/animals?search=E2E-P1-E&pageSize=25' -Token $adminToken
    $itemA = @($listA.Body.items | Where-Object { "$($_.id)" -eq $animalA }) | Select-Object -First 1
    Assert-Step 'L8-lista-v1-DESCARDED' ($null -ne $itemA -and "$($itemA.status)" -eq 'DISCARDED') "status=$(if ($itemA) { $itemA.status } else { '?' })"
    $detV1 = Invoke-Api GET "/api/v1/livestock/animals/${animalA}" -Token $adminToken
    Assert-Step 'L8-detalhe-v1-DESCARDED' ($detV1.Status -eq 200 -and "$($detV1.Body.animal.status)" -eq 'DISCARDED') "status=$(if ($detV1.Body) { $detV1.Body.animal.status } else { '?' })"
    $get360 = Invoke-Api GET "/api/livestock/animals/${animalA}" -Token $adminToken
    Assert-Step 'L8-get-360-DESCARDED' ($get360.Status -eq 200 -and "$($get360.Body.status)" -eq 'DISCARDED') "status=$(if ($get360.Body) { $get360.Body.status } else { '?' })"
    $det360 = Invoke-Api GET "/api/livestock/animals/${animalA}/detail" -Token $adminToken
    Assert-Step 'L8-detail-360-DESCARDED' ($det360.Status -eq 200 -and "$($det360.Body.animal.status)" -eq 'DISCARDED') "status=$(if ($det360.Body) { $det360.Body.animal.status } else { '?' })"

    $listR = Invoke-Api GET '/api/livestock/animals?search=SC-N-1001&pageSize=25' -Token $adminToken
    $itemR = @($listR.Body.items | Where-Object { "$($_.id)" -eq $seedA }) | Select-Object -First 1
    Assert-Step 'L8-lista-v1-RESERVED' ($null -ne $itemR -and "$($itemR.status)" -eq 'RESERVED') "status=$(if ($itemR) { $itemR.status } else { '?' })"
    $detV1R = Invoke-Api GET "/api/v1/livestock/animals/${seedA}" -Token $adminToken
    Assert-Step 'L8-detalhe-v1-RESERVED' ($detV1R.Status -eq 200 -and "$($detV1R.Body.animal.status)" -eq 'RESERVED') "status=$(if ($detV1R.Body) { $detV1R.Body.animal.status } else { '?' })"
    $get360R = Invoke-Api GET "/api/livestock/animals/${seedA}" -Token $adminToken
    Assert-Step 'L8-get-360-RESERVED' ($get360R.Status -eq 200 -and "$($get360R.Body.status)" -eq 'RESERVED') "status=$(if ($get360R.Body) { $get360R.Body.status } else { '?' })"
    $det360R = Invoke-Api GET "/api/livestock/animals/${seedA}/detail" -Token $adminToken
    Assert-Step 'L8-detail-360-RESERVED' ($det360R.Status -eq 200 -and "$($det360R.Body.animal.status)" -eq 'RESERVED') "status=$(if ($det360R.Body) { $det360R.Body.animal.status } else { '?' })"

    # ============ L9: RECONCILIAÇÃO API==DB + SNAPSHOT + SMOKE ============
    Write-Host '--- L9: reconciliacao final api==db ---' -ForegroundColor Cyan
    $dbState = Invoke-Psql "select a.tag || '|' || coalesce(a.rfid,'~') || '|' || coalesce(h.identifier,'~') from agro360.livestock_animals a left join lateral (select identifier from agro360.livestock_identifier_history where tenant_id=a.tenant_id and animal_id=a.id and identifier_type='EAR_TAG' and valid_until is null limit 1) h on true where tenant_id='${tenantId}' and id='${animalA}';"
    Assert-Step 'L9-db-tag-coerente-com-ledger' ($dbState -eq 'E2E-P1-E|~|E2E-P1-E') "state=$dbState (animal.tag | rfid | ledger EAR_TAG aberto)"
    $detFinal = Invoke-Api GET "/api/livestock/animals/${animalA}/detail" -Token $adminToken
    Assert-Step 'L9-api-igual-db' ("$($detFinal.Body.animal.tag)" -eq 'E2E-P1-E') "api.tag=$($detFinal.Body.animal.tag)"
    $finalDup = Invoke-Psql @"
select count(*)::text from (
  select 1 from agro360.livestock_identifier_history
  where tenant_id='${tenantId}' and valid_until is null
  group by animal_id, identifier_type having count(*) > 1
) x;
"@
    Assert-Step 'L9-invariante-ledger-mantido' ($finalDup -eq '0') "grupos duplicados=$finalDup"

    $snapshot = Invoke-Psql @"
select a.tag, a.status, h.identifier_type, h.identifier, h.change_reason, case when h.valid_until is null then 'ABERTA' else 'ENCERRADA' end as linha
from agro360.livestock_animals a
left join agro360.livestock_identifier_history h on h.tenant_id = a.tenant_id and h.animal_id = a.id
where a.tenant_id='${tenantId}' and (a.tag like 'E2E-P1-%' or a.tag like 'SC-N-10%')
order by a.tag, h.identifier_type, h.valid_from desc;
"@
    $snapshot | Set-Content (Join-Path $evidenceDir 'ledger-final-snapshot.txt')
    $evts = Invoke-Psql "select event_type || '=' || count(*)::text from agro360.livestock_animal_events where tenant_id='${tenantId}' and animal_id in ('${animalA}','${animalC}') group by event_type order by event_type;"
    Write-Host "Eventos E2E por tipo: $($evts -join '; ')" -ForegroundColor DarkGray

    $dash = Invoke-Api GET '/api/livestock/dashboard' -Token $adminToken
    Assert-Step 'L9-dashboard-saneamento-200' ($dash.Status -eq 200) "HTTP $($dash.Status) (modulo sadio apos toda a jornada)"

    Stop-Proc $script:apiHostProc
    if (-not $KeepRunning -and $weStartedCluster) { & $pg_ctl -D $templateDir -m fast stop | Out-Null }
    Write-Host ""
    Write-Host "E2E PECUARIA P1 (REBANHO + IDENTIFICACAO): TODOS OS CENARIOS PASSARAM" -ForegroundColor Green
    Write-Host "Evidencia: $evidenceDir" -ForegroundColor Green
    exit 0
}
catch {
    Write-Host ""
    Write-Host "E2E PECUARIA P1: FALHOU" -ForegroundColor Red
    Write-Host "$_" -ForegroundColor Red
    Write-Host "Evidencia: $evidenceDir" -ForegroundColor Red
    Stop-Proc $script:apiHostProc
    if (-not $KeepRunning -and $weStartedCluster) { try { & $pg_ctl -D $templateDir -m fast stop | Out-Null } catch {} }
    exit 1
}
finally {
    Stop-Transcript | Out-Null
}
