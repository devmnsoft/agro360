param(
    [string]$PostgresBin = 'C:\Program Files\PostgreSQL\18\bin',
    [switch]$KeepRunning,
    [switch]$RebuildTemplate
)

# E2E da jornada "entradas/sai/movimentacoes" do sprint Pecuaria P2 (2026-10-13):
#   M1 migration 141 (idempotencia formal da movimentacao individual de animal): coluna
#      idempotency_key + unique parcial presentes; reexecucao nao duplica versao/linhas;
#      linha legada do seed (sem chave) permanece intacta com idempotency_key NULL.
#   M2 jornada individual: transferencia interna com chave -> 201 + trilha (movimento, evento,
#      animal na nova propriedade); replay com a MESMA chave retorna o mesmo id sem duplicar;
#      separacao notes x chave (o replay ANTES do P2 casava pela coluna livre notes — agora uma
#      chave diferente com notas iguais NAO deduplica); evento retroativo 409 retroactive_conflict;
#      LOCATION_CHANGE movimenta o piquete; saida individual EXIT+DEATH -> status 4 lido DEAD.
#   M3 bateria de validacao (422/409/404 amigaveis): tipo fora do conjunto fechado
#      movement_kind_invalid; motivo fora do catalogo reason_unknown; motivo inativo reason_inactive;
#      direcao do motivo incompativel com a operacao reason_direction_mismatch; saida comercial sem
#      reserva sale_requires_reservation; responsavel inexistente 404; sujeito misturado/ausente.
#   M4 grupo por quantidade (QUANTITY): transferencia em quantidade decreta o saldo e troca a
#      propriedade do grupo; replay estavel; exit decrementa; saldo negativo 409 negative_balance;
#      tipo invalido para grupo 422; direcao de motivo invalida 422.
#   M5 acesso: 401 sem token; leitor (somente livestock.read + escopo FARM na sede) le 200 o que
#      esta no escopo, 403 em propriedade fora do escopo e 403 em escrita.
#   M6 reconciliacao api==db: posicao final do animal x trilha de movimentos; invariante de
#      unicidade da chave de idempotencia nas duas tabelas; lista de movimentos expoe a jornada;
#      snapshot + dashboard.
#
# Cluster descartavel (template compartilhado pg-journey-template; a corrida precisa dele com a
# migration 141 embutida no full.sql — se ausente, rode com -RebuildTemplate) + API em Release.
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

$evidenceDir = Join-Path $root ('artifacts\livestock-p2-e2e-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidenceDir | Out-Null
Start-Transcript -Path (Join-Path $evidenceDir 'transcript.log') -Append | Out-Null
$processes = [Collections.Generic.List[Diagnostics.Process]]::new()
$weStartedCluster = $false
$dbName = 'agro360_livp2_' + [guid]::NewGuid().ToString('N').Substring(0, 12)
$templateDir = Join-Path $env:TEMP 'opencode\pg-journey-template'
$templateDb = 'journey_template'
$templateMarker = Join-Path $templateDir 'template-ready.txt'
$fullSqlPath = Join-Path $root 'database/agro360-postgres-full.sql'
$migration141Path = Join-Path $root 'database/migrations/141_livestock_animal_movement_idempotency.sql'
$apiPort = 0
$apiHostProc = $null   # processo atual do host (substituido em cada recuperacao OOM)
$apiRestartCount = 0   # teto: 3 recuperacoes por execucao
$knownHash = 'pbkdf2-sha512$210000$Fk496Qko66Cw//8uYyP3mw==$PqcZJTtl+96es6GmUF3khyrZvMJqSVDIe/YfI7TCv2U='
$runTag = (Get-Date).ToString('HHmmss')
$tenantId = ''
$adminUser = ''
$readerUser = [guid]::NewGuid().ToString()
$readerRole = [guid]::NewGuid().ToString()
$farmSede = ''
$farmRetiro = '30000000-0000-0000-0000-000000000013'   # Retiro Boa Vista (mesmo tenant)
$locAnimal = '30000000-0000-4000-8000-000000000151'    # SC-N-1002 (ativo, piquete 1)
$herdQty = '30000000-0000-4000-8000-000000000141'      # Lote coletivo de recria (QUANTITY, 40)
$paddockA = '30000000-0000-4000-8000-000000000121'     # Piquete 1
$paddockB = '30000000-0000-4000-8000-000000000122'     # Piquete 2
$movementSeed = '30000000-0000-4000-8000-000000000170' # transferencia demo do seed (SC-N-1003)
$animalD = ''
$animalE = ''
$movD1 = ''
$movD2 = ''
$movD3 = ''
$herdMov1 = ''
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

function Invoke-Api([string]$Method, [string]$Path, $Body = $null, [string]$Token = '', $ExtraHeaders = $null) {
    $url = "http://127.0.0.1:${apiPort}${Path}"
    $headers = @{ 'Content-Type' = 'application/json' }
    if ($Token) { $headers['Authorization'] = "Bearer $Token" }
    if ($null -ne $ExtraHeaders) { foreach ($hk in $ExtraHeaders.Keys) { $headers[$hk] = $ExtraHeaders[$hk] } }
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
        Write-Host "Template existente em $templateDir (use -RebuildTemplate para forcar recarga; o schema precisa ter 141.0.0)." -ForegroundColor Cyan
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

    $ver141 = Invoke-Psql "select version from agro360.platform_schema_versions where version='141.0.0';"
    Assert-Step 'Schema-141.0.0-present' ($ver141 -eq '141.0.0') 'mig 141 (idempotencia movimento de animal) instalada; se ausente, rode com -RebuildTemplate'
    $ver140 = Invoke-Psql "select version from agro360.platform_schema_versions where version='140.0.0';"
    Assert-Step 'Schema-140.0.0-present' ($ver140 -eq '140.0.0') 'mig 140 (backfill ledger identificadores) instalada; garante o estado previo completo'

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
    $farmSede = Invoke-Psql "select id::text from agro360.geo_farms where tenant_id='${tenantId}' and registration_number='SC-SEDE-001' and deleted_at is null;"
    Assert-Step 'Propriedade-fixture' ($farmSede -ne '') "farmSede=$farmSede farmRetiro=$farmRetiro"
    Invoke-Psql "update agro360.identity_users set status='ACTIVE', password_hash='$knownHash', must_change_password=false, updated_at=now() where tenant_id='${tenantId}' and lower(email) in ('admin@santaclara.agro360.local','operador.santaclara@agro360.local');" | Out-Null

    # Modulo pecuaria (permissoes livestock.* filtradas pelos modulos contratados NO LOGIN).
    Invoke-Psql @"
insert into agro360.platform_tenant_module_entitlements(tenant_id,module_id,status,reason,activated_at)
select '${tenantId}', id, 'ACTIVE', 'E2E pecuaria P2 - modulo pecuaria', now()
from agro360.platform_module_catalog where code='livestock'
on conflict(tenant_id,module_id) do update set status='ACTIVE';
"@ | Out-Null

    # Leitor dedicado: papel somente-read de pecuaria -> 200 em leitura e 403 em escrita
    # (deterministico: nao depende do estado das permissaoes do operador seed).
    Invoke-Psql @"
insert into agro360.identity_roles(id,tenant_id,code,name,is_system)
values('${readerRole}','${tenantId}','E2E_P2_LIVESTOCK_READ_ONLY','Leitor Pecuaria E2E P2',false)
on conflict (tenant_id,code) do nothing;
insert into agro360.identity_users(id,tenant_id,name,email,password_hash,status,must_change_password,created_by)
select '${readerUser}','${tenantId}','Leitor Pecuaria E2E P2','e2e.p2.reader@santaclara.agro360.local','$knownHash','ACTIVE',false,'$(Invoke-Psql "select id::text from agro360.identity_users where tenant_id='${tenantId}' and lower(email)='admin@santaclara.agro360.local' and deleted_at is null;")'
where not exists(select 1 from agro360.identity_users where tenant_id='${tenantId}' and lower(email)='e2e.p2.reader@santaclara.agro360.local');
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
select gen_random_uuid(),'${tenantId}','${readerUser}','FARM','${farmSede}'
where not exists(select 1 from agro360.identity_user_unit_scopes where tenant_id='${tenantId}' and user_id='${readerUser}' and scope_type='FARM' and farm_id='${farmSede}');
"@ | Out-Null

    $adminLogin = Invoke-Api POST '/api/v1/auth/login' @{ TenantSlug = 'santa-clara'; Email = 'admin@santaclara.agro360.local'; Password = 'Agro360Admin@2024!' }
    Assert-Step 'Login-admin-santa' ($adminLogin.Status -eq 200) "HTTP $($adminLogin.Status)"
    $adminToken = $adminLogin.Body.accessToken
    $readerLogin = Invoke-Api POST '/api/v1/auth/login' @{ TenantSlug = 'santa-clara'; Email = 'e2e.p2.reader@santaclara.agro360.local'; Password = 'Agro360Admin@2024!' }
    Assert-Step 'Login-leitor-santa' ($readerLogin.Status -eq 200) "HTTP $($readerLogin.Status)"
    $readerToken = $readerLogin.Body.accessToken
    $adminUser = Invoke-Psql "select id::text from agro360.identity_users where tenant_id='${tenantId}' and lower(email)='admin@santaclara.agro360.local' and deleted_at is null;"
    Assert-Step 'Usuarios-fixtures' ($adminUser -ne '') "admin=$adminUser reader=$readerUser"

    # Diagnosticas de acesso (P2): permissao efetiva do leitor x modulos contratados, antes do M5.
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

    # ============ M1: MIGRATION 141 — COLUMNA + UNIQUE PARCIAL, IDEMPOTENTE ============
    Write-Host '--- M1: migration 141 (idempotencia formal do movimento de animal) ---' -ForegroundColor Cyan
    $colCnt = Invoke-Psql "select count(*)::text from information_schema.columns where table_schema='agro360' and table_name='livestock_animal_movements' and column_name='idempotency_key';"
    Assert-Step 'M1-coluna-idempotency-key' ($colCnt -eq '1') "colunas=$colCnt"
    $idxCnt = Invoke-Psql "select count(*)::text from pg_indexes where schemaname='agro360' and tablename='livestock_animal_movements' and indexname='ux_livestock_animal_movements_idempotency';"
    Assert-Step 'M1-unique-parcial-presente' ($idxCnt -eq '1') "indices=$idxCnt"
    $amBefore = Invoke-Psql "select count(*)::text from agro360.livestock_animal_movements where tenant_id='${tenantId}';"
    $hmBefore = Invoke-Psql "select count(*)::text from agro360.livestock_herd_movements where tenant_id='${tenantId}';"
    $migSql = Get-Content $migration141Path -Raw -Encoding UTF8
    Invoke-Psql $migSql | Out-Null
    $amAfter = Invoke-Psql "select count(*)::text from agro360.livestock_animal_movements where tenant_id='${tenantId}';"
    $hmAfter = Invoke-Psql "select count(*)::text from agro360.livestock_herd_movements where tenant_id='${tenantId}';"
    Assert-Step 'M1-reexecucao-141-sem-alterar-dados' ($amBefore -eq $amAfter -and $hmBefore -eq $hmAfter) "animal ${amBefore}->${amAfter}; herd ${hmBefore}->${hmAfter}"
    $verRows = Invoke-Psql "select count(*)::text from agro360.platform_schema_versions where version='141.0.0';"
    Assert-Step 'M1-versao-unica-depois-de-reexecutar' ($verRows -eq '1') "linhas_141=$verRows"
    $seedKey = Invoke-Psql "select coalesce(idempotency_key,'~') || '|' || movement_kind || '|' || reason_code from agro360.livestock_animal_movements where tenant_id='${tenantId}' and id='${movementSeed}';"
    Assert-Step 'M1-linha-legada-do-seed-intacta' ($seedKey -eq '~|TRANSFER|INTERNAL_TRANSFER') "row=$seedKey (key=NULL no dado antigo, sem backfill inventado)"

    # ============ M2: JORNADA INDIVIDUAL (TRANSFERENCIA + REPLAY + TRILHA) ============
    Write-Host '--- M2: transferencia interna com chave de idempotencia ---' -ForegroundColor Cyan
    $birthD = (Get-Date).AddMonths(-20).ToString('yyyy-MM-dd')
    $today = (Get-Date).ToString('yyyy-MM-dd')
    $regD = Invoke-Api POST '/api/livestock/animals' @{ FarmId = $farmSede; Tag = "E2E-P2-D${runTag}"; Species = 'BOVINE'; Breed = 'Nelore'; Sex = 'M'; BirthDate = $birthD } -Token $adminToken
    Assert-Step 'M2-registro-D-201' ($regD.Status -eq 201) "HTTP $($regD.Status) $($regD.Raw)"
    $animalD = Invoke-Psql "select id::text from agro360.livestock_animals where tenant_id='${tenantId}' and tag='E2E-P2-D${runTag}' and deleted_at is null;"
    Assert-Step 'M2-animal-D-no-banco' ($animalD -ne '') "animalD=$animalD"
    $regE = Invoke-Api POST '/api/livestock/animals' @{ FarmId = $farmSede; Tag = "E2E-P2-E${runTag}"; Species = 'BOVINE'; Breed = 'Nelore'; Sex = 'F'; BirthDate = $birthD } -Token $adminToken
    Assert-Step 'M2-registro-E-201' ($regE.Status -eq 201) "HTTP $($regE.Status) $($regE.Raw)"
    $animalE = Invoke-Psql "select id::text from agro360.livestock_animals where tenant_id='${tenantId}' and tag='E2E-P2-E${runTag}' and deleted_at is null;"
    Assert-Step 'M2-animal-E-no-banco' ($animalE -ne '') "animalE=$animalE"

    $kA = "e2e-p2-xfer-$runTag"
    $xferCmd = @{ AnimalId = $animalD; Kind = 'TRANSFER'; ReasonCode = 'INTERNAL_TRANSFER'; OccurredOn = '2026-10-01'; ResponsibleId = $adminUser; ToFarmId = $farmRetiro; IdempotencyKey = $kA; Notes = "Transferencia E2E P2 ${runTag}" }
    $xfer = Invoke-Api POST '/api/livestock/movements' $xferCmd -Token $adminToken
    Assert-Step 'M2-transferencia-interna-201' ($xfer.Status -eq 201 -and "$($xfer.Body.id)" -ne '') "HTTP $($xfer.Status) $($xfer.Raw)"
    $movD1 = "$($xfer.Body.id)"
    $xferRow = Invoke-Psql "select movement_kind || '|' || reason_code || '|' || coalesce(idempotency_key,'~') || '|' || to_char(moved_on,'YYYY-MM-DD') from agro360.livestock_animal_movements where tenant_id='${tenantId}' and id='${movD1}';"
    Assert-Step 'M2-movimento-gravado-com-chave' ($xferRow -eq "TRANSFER|INTERNAL_TRANSFER|$kA|2026-10-01") "row=$xferRow"
    $farmNow = Invoke-Psql "select id::text from agro360.geo_farms where tenant_id='${tenantId}' and id=(select farm_id from agro360.livestock_animals where tenant_id='${tenantId}' and id='${animalD}');"
    Assert-Step 'M2-animal-na-nova-propriedade' ($farmNow -eq $farmRetiro) "farm=$farmNow"
    $evtT = Invoke-Psql "select event_type || '|' || (jsonb(data)->>'reason') from agro360.livestock_animal_events where tenant_id='${tenantId}' and animal_id='${animalD}' and event_type='TRANSFER' order by created_at desc limit 1;"
    Assert-Step 'M2-evento-TRANSFER-na-trilha' ($evtT -eq 'TRANSFER|INTERNAL_TRANSFER') "event=$evtT"

    $replay = Invoke-Api POST '/api/livestock/movements' $xferCmd -Token $adminToken
    Assert-Step 'M2-replay-mesma-chave-mesmo-id' ($replay.Status -eq 201 -and "$($replay.Body.id)" -eq $movD1) "HTTP $($replay.Status) id=$($replay.Body.id) original=$movD1"
    $rowsD = Invoke-Psql "select count(*)::text from agro360.livestock_animal_movements where tenant_id='${tenantId}' and animal_id='${animalD}';"
    $evtsD = Invoke-Psql "select count(*)::text from agro360.livestock_animal_events where tenant_id='${tenantId}' and animal_id='${animalD}' and event_type='TRANSFER';"
    Assert-Step 'M2-replay-sem-duplicar-trilha' ($rowsD -eq '1' -and $evtsD -eq '1') "movimentos=$rowsD eventos_TRANSFER=$evtsD (o evento ENTRY do proprio registro nao conta na trilha de transferencia)"

    # Separacao notes x chave: antes do P2 o replay casava pela coluna LIVRE notes. Agora a chave
    # e o unico mecanismo: chave DIFERENTE com notas IGUAIS a outro registro NAO deduplica.
    $sep1 = Invoke-Api POST '/api/livestock/movements' @{ AnimalId = $animalD; Kind = 'LOCATION_CHANGE'; ReasonCode = 'INTERNAL_TRANSFER'; OccurredOn = '2026-10-03'; ResponsibleId = $adminUser; IdempotencyKey = "k-sep-a-$runTag"; Notes = "k-sep-a-$runTag" } -Token $adminToken
    Assert-Step 'M2-movimento-sep1-201' ($sep1.Status -eq 201) "HTTP $($sep1.Status) $($sep1.Raw)"
    $movD2 = "$($sep1.Body.id)"
    $sep2 = Invoke-Api POST '/api/livestock/movements' @{ AnimalId = $animalD; Kind = 'LOCATION_CHANGE'; ReasonCode = 'INTERNAL_TRANSFER'; OccurredOn = '2026-10-04'; ResponsibleId = $adminUser; IdempotencyKey = "k-sep-b-$runTag"; Notes = "k-sep-a-$runTag" } -Token $adminToken
    Assert-Step 'M2-chave-diferente-com-notas-iguais-nao-deduplica' ($sep2.Status -eq 201 -and "$($sep2.Body.id)" -ne $movD2 -and "$($sep2.Body.id)" -ne $movD1) "id=$($sep2.Body.id) (antes do P2 este pedido voltaria $movD2 via notes)"
    $movD3 = "$($sep2.Body.id)"
    $rowsD3 = Invoke-Psql "select count(*)::text from agro360.livestock_animal_movements where tenant_id='${tenantId}' and animal_id='${animalD}';"
    Assert-Step 'M2-trilha-com-tres-movimentos-distintos' ($rowsD3 -eq '3') "movimentos=$rowsD3"

    $backdated = Invoke-Api POST '/api/livestock/movements' @{ AnimalId = $animalD; Kind = 'LOCATION_CHANGE'; ReasonCode = 'INTERNAL_TRANSFER'; OccurredOn = '2026-10-02'; ResponsibleId = $adminUser; IdempotencyKey = "k-back-$runTag" } -Token $adminToken
    Assert-Step 'M2-evento-retroativo-409' ($backdated.Status -eq 409 -and $backdated.Raw -match 'retroactive_conflict') "HTTP $($backdated.Status) $($backdated.Raw)"

    $loc = Invoke-Api POST '/api/livestock/movements' @{ AnimalId = $locAnimal; Kind = 'LOCATION_CHANGE'; ReasonCode = 'INTERNAL_TRANSFER'; OccurredOn = '2026-10-05'; ResponsibleId = $adminUser; ToPaddockId = $paddockB; IdempotencyKey = "k-loc-$runTag" } -Token $adminToken
    Assert-Step 'M2-location-change-muda-piquete-201' ($loc.Status -eq 201) "HTTP $($loc.Status) $($loc.Raw)"
    $padNow = Invoke-Psql "select id::text from agro360.livestock_paddocks where tenant_id='${tenantId}' and id=(select paddock_id from agro360.livestock_animals where tenant_id='${tenantId}' and id='${locAnimal}');"
    Assert-Step 'M2-animal-no-piquete-2' ($padNow -eq $paddockB) "paddock=$padNow"

    $exitE = Invoke-Api POST '/api/livestock/movements' @{ AnimalId = $animalE; Kind = 'EXIT'; ReasonCode = 'DEATH'; OccurredOn = $today; ResponsibleId = $adminUser; IdempotencyKey = "k-exit-$runTag" } -Token $adminToken
    Assert-Step 'M2-saida-individual-death-201' ($exitE.Status -eq 201) "HTTP $($exitE.Status) $($exitE.Raw)"
    $stE = Invoke-Psql "select status::text from agro360.livestock_animals where tenant_id='${tenantId}' and id='${animalE}';"
    Assert-Step 'M2-status-4-no-banco' ($stE -eq '4') "status=$stE"
    $detE = Invoke-Api GET "/api/livestock/animals/${animalE}" -Token $adminToken
    Assert-Step 'M2-detail-lido-DEAD' ($detE.Status -eq 200 -and "$($detE.Body.status)" -eq 'DEAD') "status=$(if ($detE.Body) { $detE.Body.status } else { '?' })"
    $exitRow = Invoke-Psql "select movement_kind || '|' || reason_code from agro360.livestock_animal_movements where tenant_id='${tenantId}' and animal_id='${animalE}' limit 1;"
    Assert-Step 'M2-trilha-da-saida' ($exitRow -eq 'EXIT|DEATH') "row=$exitRow"

    # ============ M3: BATERIA DE VALIDACAO (CATALOGO / TIPO / RESPONSÁVEL / RESERVA) ============
    Write-Host '--- M3: validacoes amigaveis do endpoint de movimentacao ---' -ForegroundColor Cyan
    $badKind = Invoke-Api POST '/api/livestock/movements' @{ AnimalId = $locAnimal; Kind = 'TELEPORT'; ReasonCode = 'INTERNAL_TRANSFER'; OccurredOn = '2026-10-06'; ResponsibleId = $adminUser } -Token $adminToken
    Assert-Step 'M3-tipo-fora-do-conjunto-422' ($badKind.Status -eq 422 -and $badKind.Raw -match 'movement_kind_invalid') "HTTP $($badKind.Status) $($badKind.Raw)"

    $bogusReason = Invoke-Api POST '/api/livestock/movements' @{ AnimalId = $locAnimal; Kind = 'TRANSFER'; ReasonCode = 'BOGUS'; OccurredOn = '2026-10-06'; ResponsibleId = $adminUser } -Token $adminToken
    Assert-Step 'M3-motivo-fora-do-catalogo-422' ($bogusReason.Status -eq 422 -and $bogusReason.Raw -match 'reason_unknown') "HTTP $($bogusReason.Status) $($bogusReason.Raw)"

    $dirMismatch = Invoke-Api POST '/api/livestock/movements' @{ AnimalId = $locAnimal; Kind = 'ENTRY'; ReasonCode = 'DEATH'; OccurredOn = '2026-10-06'; ResponsibleId = $adminUser } -Token $adminToken
    Assert-Step 'M3-direcao-incompativel-422' ($dirMismatch.Status -eq 422 -and $dirMismatch.Raw -match 'reason_direction_mismatch') "HTTP $($dirMismatch.Status) $($dirMismatch.Raw)"

    Invoke-Psql "update agro360.livestock_movement_reasons set active=false where tenant_id='${tenantId}' and code='DEATH';" | Out-Null
    $inactive = Invoke-Api POST '/api/livestock/movements' @{ AnimalId = $locAnimal; Kind = 'EXIT'; ReasonCode = 'DEATH'; OccurredOn = '2026-10-06'; ResponsibleId = $adminUser } -Token $adminToken
    Assert-Step 'M3-motivo-inativo-422' ($inactive.Status -eq 422 -and $inactive.Raw -match 'reason_inactive') "HTTP $($inactive.Status) $($inactive.Raw)"
    Invoke-Psql "update agro360.livestock_movement_reasons set active=true where tenant_id='${tenantId}' and code='DEATH';" | Out-Null
    $reactivated = Invoke-Psql "select case when active then 't' else 'f' end from agro360.livestock_movement_reasons where tenant_id='${tenantId}' and code='DEATH';"
    Assert-Step 'M3-catalogo-restaurado' ($reactivated -eq 't') "active=$reactivated"

    $saleGuard = Invoke-Api POST '/api/livestock/movements' @{ AnimalId = $locAnimal; Kind = 'EXIT'; ReasonCode = 'SALE'; OccurredOn = '2026-10-06'; ResponsibleId = $adminUser } -Token $adminToken
    Assert-Step 'M3-saida-comercial-sem-reserva-409' ($saleGuard.Status -eq 409 -and $saleGuard.Raw -match 'sale_requires_reservation') "HTTP $($saleGuard.Status) $($saleGuard.Raw)"

    $badResp = Invoke-Api POST '/api/livestock/movements' @{ AnimalId = $locAnimal; Kind = 'TRANSFER'; ReasonCode = 'INTERNAL_TRANSFER'; OccurredOn = '2026-10-06'; ResponsibleId = [guid]::NewGuid().ToString() } -Token $adminToken
    Assert-Step 'M3-responsavel-inexistente-404' ($badResp.Status -eq 404) "HTTP $($badResp.Status) $($badResp.Raw)"

    $mixed = Invoke-Api POST '/api/livestock/movements' @{ AnimalId = $locAnimal; HerdId = $herdQty; Kind = 'TRANSFER'; ReasonCode = 'INTERNAL_TRANSFER'; OccurredOn = '2026-10-06'; ResponsibleId = $adminUser; Quantity = 2; ToFarmId = $farmRetiro } -Token $adminToken
    Assert-Step 'M3-sujeito-misto-422' ($mixed.Status -eq 422 -and $mixed.Raw -match 'mixed_subject') "HTTP $($mixed.Status) $($mixed.Raw)"

    $noSubject = Invoke-Api POST '/api/livestock/movements' @{ Kind = 'TRANSFER'; ReasonCode = 'INTERNAL_TRANSFER'; OccurredOn = '2026-10-06'; ResponsibleId = $adminUser; ToFarmId = $farmRetiro } -Token $adminToken
    Assert-Step 'M3-sujeito-ausente-422' ($noSubject.Status -eq 422 -and $noSubject.Raw -match 'subject_required') "HTTP $($noSubject.Status) $($noSubject.Raw)"

    # ============ M4: GRUPO POR QUANTIDADE (QUANTITY) ============
    Write-Host '--- M4: movimentacao coletiva do grupo QUANTITY ---' -ForegroundColor Cyan
    $hc0 = Invoke-Psql "select head_count::text from agro360.livestock_herds where tenant_id='${tenantId}' and id='${herdQty}';"
    Assert-Step 'M4-saldo-inicial-do-grupo' ($hc0 -eq '40') "head_count=$hc0"

    $ht1 = Invoke-Api POST '/api/livestock/movements' @{ HerdId = $herdQty; Kind = 'TRANSFER'; ReasonCode = 'INTERNAL_TRANSFER'; OccurredOn = '2026-10-06'; ResponsibleId = $adminUser; ToFarmId = $farmRetiro; Quantity = 5; IdempotencyKey = "hk-t1-$runTag"; OriginNotes = "Saída coletiva E2E P2 ${runTag}" } -Token $adminToken
    Assert-Step 'M4-transferencia-em-quantidade-201' ($ht1.Status -eq 201) "HTTP $($ht1.Status) $($ht1.Raw)"
    $herdMov1 = "$($ht1.Body.id)"
    $hc1 = Invoke-Psql "select head_count::text from agro360.livestock_herds where tenant_id='${tenantId}' and id='${herdQty}';"
    Assert-Step 'M4-saldo-decrementado-40->35' ($hc1 -eq '35') "head_count=$hc1"
    $hfarm = Invoke-Psql "select id::text from agro360.geo_farms where tenant_id='${tenantId}' and id=(select farm_id from agro360.livestock_herds where tenant_id='${tenantId}' and id='${herdQty}');"
    Assert-Step 'M4-grupo-mudou-de-propriedade' ($hfarm -eq $farmRetiro) "farm=$hfarm"
    $hrow = Invoke-Psql "select movement_kind || '|' || quantity::text || '|' || coalesce(idempotency_key,'~') from agro360.livestock_herd_movements where tenant_id='${tenantId}' and id='${herdMov1}';"
    Assert-Step 'M4-movimento-coletivo-gravado' ($hrow -eq "TRANSFER|5|hk-t1-$runTag") "row=$hrow"

    $ht1r = Invoke-Api POST '/api/livestock/movements' @{ HerdId = $herdQty; Kind = 'TRANSFER'; ReasonCode = 'INTERNAL_TRANSFER'; OccurredOn = '2026-10-06'; ResponsibleId = $adminUser; ToFarmId = $farmRetiro; Quantity = 5; IdempotencyKey = "hk-t1-$runTag"; OriginNotes = "Saída coletiva E2E P2 ${runTag}" } -Token $adminToken
    Assert-Step 'M4-replay-coletivo-mesmo-id' ($ht1r.Status -eq 201 -and "$($ht1r.Body.id)" -eq $herdMov1) "HTTP $($ht1r.Status) id=$($ht1r.Body.id)"
    $hc1r = Invoke-Psql "select head_count::text from agro360.livestock_herds where tenant_id='${tenantId}' and id='${herdQty}';"
    $hkRows = Invoke-Psql "select count(*)::text from agro360.livestock_herd_movements where tenant_id='${tenantId}' and idempotency_key='hk-t1-$runTag';"
    Assert-Step 'M4-replay-coletivo-sem-segundo-debito' ($hc1r -eq '35' -and $hkRows -eq '1') "head_count=$hc1r linhas=$hkRows"

    $he1 = Invoke-Api POST '/api/livestock/movements' @{ HerdId = $herdQty; Kind = 'EXIT'; ReasonCode = 'DEATH'; OccurredOn = '2026-10-07'; ResponsibleId = $adminUser; Quantity = 1; IdempotencyKey = "hk-e1-$runTag" } -Token $adminToken
    Assert-Step 'M4-exit-coletivo-201' ($he1.Status -eq 201) "HTTP $($he1.Status) $($he1.Raw)"
    $hc2 = Invoke-Psql "select head_count::text from agro360.livestock_herds where tenant_id='${tenantId}' and id='${herdQty}';"
    Assert-Step 'M4-saldo-35->34' ($hc2 -eq '34') "head_count=$hc2"

    $hneg = Invoke-Api POST '/api/livestock/movements' @{ HerdId = $herdQty; Kind = 'EXIT'; ReasonCode = 'DISCARD'; OccurredOn = '2026-10-08'; ResponsibleId = $adminUser; Quantity = 999; IdempotencyKey = "hk-neg-$runTag" } -Token $adminToken
    Assert-Step 'M4-saldo-negativo-409' ($hneg.Status -eq 409 -and $hneg.Raw -match 'negative_balance') "HTTP $($hneg.Status) $($hneg.Raw)"

    $hLot = Invoke-Api POST '/api/livestock/movements' @{ HerdId = $herdQty; Kind = 'LOT_CHANGE'; ReasonCode = 'INTERNAL_TRANSFER'; OccurredOn = '2026-10-08'; ResponsibleId = $adminUser; Quantity = 2 } -Token $adminToken
    Assert-Step 'M4-tipo-invalido-para-grupo-422' ($hLot.Status -eq 422 -and $hLot.Raw -match 'movement_kind_invalid') "HTTP $($hLot.Status) $($hLot.Raw)"

    $hDir = Invoke-Api POST '/api/livestock/movements' @{ HerdId = $herdQty; Kind = 'ENTRY'; ReasonCode = 'DEATH'; OccurredOn = '2026-10-08'; ResponsibleId = $adminUser; Quantity = 2 } -Token $adminToken
    Assert-Step 'M4-direcao-invalida-em-grupo-422' ($hDir.Status -eq 422 -and $hDir.Raw -match 'reason_direction_mismatch') "HTTP $($hDir.Status) $($hDir.Raw)"

    # ============ M5: ACESSO (401 / 200 NO ESCOPO / 403 FORA DO ESCOPO / 403 ESCRITA) ============
    Write-Host '--- M5: controle de acesso da jornada de movimentacoes ---' -ForegroundColor Cyan
    $anon = Invoke-Api GET '/api/livestock/movements'
    Assert-Step 'M5-anonimo-401' ($anon.Status -eq 401) "HTTP $($anon.Status)"
    $readOk = Invoke-Api GET '/api/livestock/movements' -Token $readerToken
    Assert-Step 'M5-leitor-le-movimentos-200' ($readOk.Status -eq 200) "HTTP $($readOk.Status)"
    Assert-Step 'M5-leitor-ve-linha-do-seed-no-escopo' ($readOk.Raw -match 'SC-N-1003') "linha do seed (transferencia SC-SEDE-001 -> Retiro) visivel no escopo FARM sede"
    # A validacao de escopo de unidade do middleware opera pelo HEADER X-Farm-ID (o parametro de
    # query farmId e apenas filtro de listagem, comportamento platario preexistente em todos os
    # endpoints de pecuaria - registrado em LIMITES). Fora do escopo -> 403 forbidden_unit_scope.
    $readOut = Invoke-Api GET '/api/livestock/movements' $null $readerToken @{ 'X-Farm-ID' = $farmRetiro }
    Assert-Step 'M5-unidade-fora-do-escopo-403' ($readOut.Status -eq 403 -and $readOut.Raw -match 'forbidden_unit_scope') "HTTP $($readOut.Status) $($readOut.Raw)"
    $readerPost = Invoke-Api POST '/api/livestock/movements' @{ AnimalId = $locAnimal; Kind = 'LOCATION_CHANGE'; ReasonCode = 'INTERNAL_TRANSFER'; OccurredOn = '2026-10-09'; ResponsibleId = $adminUser; ToPaddockId = $paddockA; IdempotencyKey = "k-read-$runTag" } -Token $readerToken
    Assert-Step 'M5-leitor-escreve-403' ($readerPost.Status -eq 403) "HTTP $($readerPost.Status) $($readerPost.Raw)"
    $padStill = Invoke-Psql "select id::text from agro360.livestock_paddocks where tenant_id='${tenantId}' and id=(select paddock_id from agro360.livestock_animals where tenant_id='${tenantId}' and id='${locAnimal}');"
    Assert-Step 'M5-escrita-rejeitada-nao-mudou-estado' ($padStill -eq $paddockB) "paddock=$padStill (continua no Piquete 2)"

    # ============ M6: RECONCILIAÇÃO API==DB + INVARIANTE DE CHAVE + SMOKE ============
    Write-Host '--- M6: reconciliacao final api==db ---' -ForegroundColor Cyan
    $dbState = Invoke-Psql @"
select (select count(*)::text from agro360.livestock_animal_movements m where m.tenant_id='${tenantId}' and m.animal_id='${animalD}') || '|'
     || (select count(*)::text from agro360.livestock_animal_movements m where m.tenant_id='${tenantId}' and m.animal_id='${animalD}' and m.idempotency_key is not null) || '|'
     || (select case when farm_id='${farmRetiro}' then 'RETIRO' else 'SEDE' end from agro360.livestock_animals where tenant_id='${tenantId}' and id='${animalD}');
"@
    Assert-Step 'M6-trilha-d-completa-e-posicao-final' ($dbState -eq '3|3|RETIRO') "movimentos|com_chave|posicao (esperado 3|3|RETIRO)"

    $getD = Invoke-Api GET "/api/livestock/animals/${animalD}" -Token $adminToken
    Assert-Step 'M6-api-igual-db-posicao' ($getD.Status -eq 200 -and "$($getD.Body.status)" -eq 'ACTIVE' -and "$($getD.Body.farmId)" -eq $farmRetiro) "api.farmId=$(if ($getD.Body) { $getD.Body.farmId } else { '?' }) api.status=$(if ($getD.Body) { $getD.Body.status } else { '?' })"

    $listJ = Invoke-Api GET "/api/livestock/movements?farmId=$farmRetiro&kind=TRANSFER" -Token $adminToken
    $itemD = @($listJ.Body | Where-Object { "$($_.subject)" -like 'E2E-P2-D*' -and "$($_.kind)" -eq 'TRANSFER' }) | Select-Object -First 1
    Assert-Step 'M6-lista-expoe-jornada-do-animal-D' ($null -ne $itemD -and "$($itemD.reasonCode)" -eq 'INTERNAL_TRANSFER' -and "$($itemD.toFarmId)" -eq $farmRetiro) "subject=$(if ($itemD) { $itemD.subject } else { '?' }) reason=$(if ($itemD) { $itemD.reasonCode } else { '?' })"

    $dupKeys = Invoke-Psql @"
select count(*)::text from (
  select 1 from agro360.livestock_animal_movements where tenant_id='${tenantId}'
  group by idempotency_key having count(*) > 1
  union all
  select 1 from agro360.livestock_herd_movements where tenant_id='${tenantId}'
  group by idempotency_key having count(*) > 1
) x;
"@
    Assert-Step 'M6-invariante-uma-linha-por-chave' ($dupKeys -eq '0') "chaves duplicadas=$dupKeys"

    $snapshot = Invoke-Psql @"
select 'ANIMAL ' || m.id, a.tag, m.movement_kind, coalesce(m.reason_code,'~'), to_char(m.moved_on,'YYYY-MM-DD'), coalesce(m.idempotency_key,'~')
from agro360.livestock_animal_movements m
join agro360.livestock_animals a on a.id=m.animal_id and a.tenant_id=m.tenant_id
where m.tenant_id='${tenantId}' and (a.tag like 'E2E-P2-%' or m.id='${movementSeed}')
union all
select 'HERD   ' || m.id, h.name, m.movement_kind, coalesce(m.reason_code,'~'), to_char(m.occurred_on,'YYYY-MM-DD'), coalesce(m.idempotency_key,'~')
from agro360.livestock_herd_movements m
join agro360.livestock_herds h on h.id=m.herd_id and h.tenant_id=m.tenant_id
where m.tenant_id='${tenantId}' and (m.idempotency_key like 'hk-%' or m.origin_notes like 'Entrada coletiva%')
order by 3, 5, 1;
"@
    $snapshot | Set-Content (Join-Path $evidenceDir 'movements-final-snapshot.txt')
    Write-Host "Snapshot final da trilha de movimentos salvo em movements-final-snapshot.txt" -ForegroundColor DarkGray

    $dash = Invoke-Api GET '/api/livestock/dashboard' -Token $adminToken
    Assert-Step 'M6-dashboard-saneamento-200' ($dash.Status -eq 200) "HTTP $($dash.Status) (modulo sadio apos toda a jornada)"

    Stop-Proc $script:apiHostProc
    if (-not $KeepRunning -and $weStartedCluster) { & $pg_ctl -D $templateDir -m fast stop | Out-Null }
    Write-Host ""
    Write-Host "E2E PECUARIA P2 (ENTRADAS/SAI/MOVIMENTACOES): TODOS OS CENARIOS PASSARAM" -ForegroundColor Green
    Write-Host "Evidencia: $evidenceDir" -ForegroundColor Green
    exit 0
}
catch {
    Write-Host ""
    Write-Host "E2E PECUARIA P2: FALHOU" -ForegroundColor Red
    Write-Host "$_" -ForegroundColor Red
    Write-Host "Evidencia: $evidenceDir" -ForegroundColor Red
    Stop-Proc $script:apiHostProc
    if (-not $KeepRunning -and $weStartedCluster) { try { & $pg_ctl -D $templateDir -m fast stop | Out-Null } catch {} }
    exit 1
}
finally {
    Stop-Transcript | Out-Null
}
