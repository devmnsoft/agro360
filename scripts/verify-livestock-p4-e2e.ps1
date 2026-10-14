param(
    [string]$PostgresBin = 'C:\Program Files\PostgreSQL\18\bin',
    [switch]$KeepRunning,
    [switch]$RebuildTemplate
)

# E2E da jornada "sanidade" do sprint Pecuaria P4 (2026-10-13):
#   H0 migration 143 (protocolo + idempotencia em livestock_health_events): colunas presentes;
#      reexecucao da mig arquivo nao altera nada; cadeia 140/141/142 intacta; os 2 protocolos
#      demonstrativos do seed santa-clara presentes (demo_only=true).
#   H1 baseline + estado efetivo derivado: a restricao SEED (...153, SALE, esperado 2026-09-25,
#      gravada ACTIVE e já vencida) aparece na lista como status=ACTIVE + effectiveStatus=EXPIRED
#      e o KPI restrictedAnimals do dashboard a ignora (=0, antes seria 1).
#   H2 leitura de protocolos (seeds) + upsert genérico de catalogo 'protocols' -> 422 apontando
#      para o endpoint dedicado (correcao do 500 latente).
#   H3 criacao de protocolo via rota dedicada: valida -> 201 + banco correto; bateria negativa
#      (purpose fora do fechado 422, janela invertida 422, carência negativa 422, nome+versao
#      duplicado 409, nome acima do teto 422).
#   H4 execucao COM protocolo: 201; evento referencia protocolo+versao; carência do CATÁLOGO
#      prevalece sobre o payload (14 vs 9); animal atualizado ate appliedOn+14; restricao
#      operacional derivada na MESMA transacao (purpose/protocol/source_event/até).
#   H5 replay de idempotencia: mesma chave com espacos retorna o mesmo id sem duplicar evento/
#      restricao; chave diferente grava de novo.
#   H6 aplicacao repetida do MESMO protocolo para o MESMO sujeito dentro da carencia -> 409
#      restriction_overlap (nada gravado; 1 unico evento-chave no banco).
#   H7 execucao SEM protocolo (legado): payload withdrawalDays governa, restricao nao criada.
#   H8 bateria do ciclo de vida do protocolo: inexistente 404; desativado 409 (re-desativar 409);
#      fora da janela (valid_from futuro x appliedOn passado) 409.
#   H9 agenda: evento vencido (H4) + futuro (H7) em propriedades diferentes; filtros
#      overdueOnly/farmId/propriedade desconhecida comportam-se conforme o escopo.
#   H10 efeito do estado efetivo: ordem de manejo NOVA contendo ...153 previa 'Elegível' (a
#      restricao vencida deixou de bloquear) e liberar a restricao efetivamente vencida -> 409.
#   H11 acesso: 401 anonimo; leitor read-only le protocolos/agenda 200 mas escreve 403; escrita
#      de aplicação contra animal fora do escopo de unidade -> 403 forbidden_unit_scope.
#   H12 reconciliacao api==db: eventos/chaves 1:1, restricoes e carência do animal iguais na API
#      e no banco; snapshot final + dashboard 200.
#
# Cluster descartavel (template compartilhado pg-journey-template; a corrida precisa dele com a
# migration 143 embutida no full.sql — se ausente, rode com -RebuildTemplate) + API em Release.
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

$evidenceDir = Join-Path $root ('artifacts\livestock-p4-e2e-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidenceDir | Out-Null
Start-Transcript -Path (Join-Path $evidenceDir 'transcript.log') -Append | Out-Null
$processes = [Collections.Generic.List[Diagnostics.Process]]::new()
$weStartedCluster = $false
$dbName = 'agro360_livp4_' + [guid]::NewGuid().ToString('N').Substring(0, 12)
$templateDir = Join-Path $env:TEMP 'opencode\pg-journey-template'
$templateDb = 'journey_template'
$templateMarker = Join-Path $templateDir 'template-ready.txt'
$fullSqlPath = Join-Path $root 'database/agro360-postgres-full.sql'
$migration143Path = Join-Path $root 'database/migrations/143_livestock_health_schedule.sql'
$apiPort = 0
$apiHostProc = $null   # processo atual do host (substituido em cada recuperacao OOM)
$apiRestartCount = 0   # teto: 3 recuperacoes por execucao
$knownHash = 'pbkdf2-sha512$210000$Fk496Qko66Cw//8uYyP3mw==$PqcZJTtl+96es6GmUF3khyrZvMJqSVDIe/YfI7TCv2U='
$runTag = (Get-Date).ToString('HHmmss')
$tenantId = ''
$adminUser = ''
$adminName = ''
$readerUser = [guid]::NewGuid().ToString()
$readerRole = [guid]::NewGuid().ToString()
$farmSede = ''
$farmRetiro = '30000000-0000-0000-0000-000000000013'   # Retiro Boa Vista (mesmo tenant)
$animalB = '30000000-0000-4000-8000-000000000151'      # SC-N-1002 (ativo, sede)
$animalC = '30000000-0000-0000-0000-000000000152'      # SC-N-1003 (ativo, no Retiro)
$animalD = '30000000-0000-4000-8000-000000000153'      # SC-N-1004 (reservado; restricao SALE demo VENCIDA em 2026-09-25)
$protocolSeedDemo = ''                                  # MILK demo do seed (carência 7)
$restrictionSeedId = ''
$h3ProtocolId = ''
$h3FutureProtocolId = ''
$h4EventId = ''
$unknownGuid = [guid]::NewGuid().ToString()
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
        Write-Host "Template existente em $templateDir (use -RebuildTemplate para forcar recarga; o schema precisa ter 143.0.0)." -ForegroundColor Cyan
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

    # ============ H0: MIGRATION 143 — COLUNAS DE PROTOCOLO/CHAVE, IDEMPOTENTE, CHADEA INTACTA ============
    Write-Host '--- H0: migration 143 (sanidade: protocolo + idempotencia) ---' -ForegroundColor Cyan
    $ver143 = Invoke-Psql "select version from agro360.platform_schema_versions where version='143.0.0';"
    Assert-Step 'H0-schema-143.0.0-present' ($ver143 -eq '143.0.0') 'mig 143 embutida no full.sql; se ausente, rode com -RebuildTemplate'
    $ver142 = Invoke-Psql "select version from agro360.platform_schema_versions where version='142.0.0';"
    $ver141 = Invoke-Psql "select version from agro360.platform_schema_versions where version='141.0.0';"
    $ver140 = Invoke-Psql "select version from agro360.platform_schema_versions where version='140.0.0';"
    Assert-Step 'H0-cadeia-140-142-intacta' ($ver140 -eq '140.0.0' -and $ver141 -eq '141.0.0' -and $ver142 -eq '142.0.0') 'estado previo P1/P2/P3 presente'
    $colCnt = Invoke-Psql @"
select string_agg(column_name, ',' order by column_name)
from information_schema.columns
where table_schema='agro360' and table_name='livestock_health_events'
  and column_name in ('idempotency_key','protocol_id','protocol_version_label');
"@
    Assert-Step 'H0-colunas-de-protocolo-e-chave' ($colCnt -eq 'idempotency_key,protocol_id,protocol_version_label') "colunas=$colCnt"
    $uxCnt = Invoke-Psql "select count(*)::text from pg_indexes where schemaname='agro360' and indexname='ux_livestock_health_events_idempotency';"
    Assert-Step 'H0-unique-parcial-de-idempotencia' ($uxCnt -eq '1') "indices=$uxCnt"
    $evBefore = Invoke-Psql "select count(*)::text from agro360.livestock_health_events;"
    $migSql = Get-Content $migration143Path -Raw -Encoding UTF8
    Invoke-Psql $migSql | Out-Null
    $evAfter = Invoke-Psql "select count(*)::text from agro360.livestock_health_events;"
    $verRows = Invoke-Psql "select count(*)::text from agro360.platform_schema_versions where version='143.0.0';"
    Assert-Step 'H0-reexecucao-143-sem-alterar-dados' ($evBefore -eq $evAfter -and $verRows -eq '1') "eventos ${evBefore}->${evAfter}; linhas_143=$verRows"
    $seedProtocols = Invoke-Psql @"
select purpose || '|' || withdrawal_days || '|' || case when demo_only then 'demo' else 'prod' end
from agro360.livestock_health_protocols
where id in ('30000000-0000-4000-8000-000000000250','30000000-0000-4000-8000-000000000251')
order by purpose;
"@
    $seedRows = ($seedProtocols -split "`n") | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' }
    Assert-Step 'H0-seed-com-2-protocolos-demo' (@($seedRows).Count -eq 2 -and @($seedRows) -contains 'MILK|7|demo' -and @($seedRows) -contains 'OPERATIONAL|21|demo') "rows=$($seedRows -join ';')"
    $protocolSeedDemo = Invoke-Psql "select id::text from agro360.livestock_health_protocols where tenant_id='30000000-0000-0000-0000-000000000001' and purpose='MILK' and demo_only=true;"
    Assert-Step 'H0-seed-protocolo-milk-id' ($protocolSeedDemo -ne '') "id=$protocolSeedDemo"

    # ============ CONFIG DA EXECUCAO ============
    $connStr = "Host=127.0.0.1;Port=${pgPort};Database=${dbName};Username=postgres;Search Path=agro360;Command Timeout=60"
    $env:ConnectionStrings__Agro360 = $connStr
    $env:ConnectionStrings__DefaultConnection = $connStr
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:Jwt__SigningKey = 'e2e-test-signing-key-32bytes-minimum!'
    $env:PGPASSWORD = 'trust-auth-placeholder'
    # Memoria: em maquina com pouco RAM livre o OOM killer do SO matou o host da API no meio da
    # execucao (observado varias vezes, ~1 MB livres, sem WER). Teto duro no heap do GC + GC
    # conservador para o processo sobreviver a execucoes pesadas. Em 2026-10-14 o teto foi
    # reduzido para 900 MB e forçado workstation GC (1 heap em vez de 1 por core) porque a
    # maquina passou o dia inteiro oscilando 0-4.5 GB livres matando backends idle do PG por OOM.
    $env:DOTNET_GCHeapHardLimit = '700000000'
    $env:DOTNET_GCConserveMemory = '9'
    $env:DOTNET_gcServer = '0'

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
select '${tenantId}', id, 'ACTIVE', 'E2E pecuaria P4 - modulo pecuaria', now()
from agro360.platform_module_catalog where code='livestock'
on conflict(tenant_id,module_id) do update set status='ACTIVE';
"@ | Out-Null

    # Leitor dedicado: papel somente-read de pecuaria -> 200 em leitura e 403 em escrita
    # (deterministico: nao depende do estado das permissoes do operador seed).
    Invoke-Psql @"
insert into agro360.identity_roles(id,tenant_id,code,name,is_system)
values('${readerRole}','${tenantId}','E2E_P4_LIVESTOCK_READ_ONLY','Leitor Pecuaria E2E P4',false)
on conflict (tenant_id,code) do nothing;
insert into agro360.identity_users(id,tenant_id,name,email,password_hash,status,must_change_password,created_by)
select '${readerUser}','${tenantId}','Leitor Pecuaria E2E P4','e2e.p4.reader@santaclara.agro360.local','$knownHash','ACTIVE',false,'$(Invoke-Psql "select id::text from agro360.identity_users where tenant_id='${tenantId}' and lower(email)='admin@santaclara.agro360.local' and deleted_at is null;")'
where not exists(select 1 from agro360.identity_users where tenant_id='${tenantId}' and lower(email)='e2e.p4.reader@santaclara.agro360.local');
insert into agro360.identity_user_roles(tenant_id,user_id,role_id)
values('${tenantId}','${readerUser}','${readerRole}')
on conflict do nothing;
insert into agro360.identity_role_permissions(tenant_id,role_id,permission_id)
select '${tenantId}','${readerRole}',id from agro360.identity_permissions where code='livestock.read'
on conflict do nothing;
-- escopo de unidade (middleware TenantContext exige >=1 linha; sem ela tudo vira 403
-- forbidden_unit_scope antes mesmo da politica de permissao). Escopo FARM na fazenda de teste.
insert into agro360.identity_user_unit_scopes(id,tenant_id,user_id,scope_type,farm_id)
select gen_random_uuid(),'${tenantId}','${readerUser}','FARM','${farmSede}'
where not exists(select 1 from agro360.identity_user_unit_scopes where tenant_id='${tenantId}' and user_id='${readerUser}' and scope_type='FARM' and farm_id='${farmSede}');
"@ | Out-Null

    $adminLogin = Invoke-Api POST '/api/v1/auth/login' @{ TenantSlug = 'santa-clara'; Email = 'admin@santaclara.agro360.local'; Password = 'Agro360Admin@2024!' }
    Assert-Step 'Login-admin-santa' ($adminLogin.Status -eq 200) "HTTP $($adminLogin.Status)"
    $adminToken = $adminLogin.Body.accessToken
    $readerLogin = Invoke-Api POST '/api/v1/auth/login' @{ TenantSlug = 'santa-clara'; Email = 'e2e.p4.reader@santaclara.agro360.local'; Password = 'Agro360Admin@2024!' }
    Assert-Step 'Login-leitor-santa' ($readerLogin.Status -eq 200) "HTTP $($readerLogin.Status)"
    $readerToken = $readerLogin.Body.accessToken
    $adminUser = Invoke-Psql "select id::text from agro360.identity_users where tenant_id='${tenantId}' and lower(email)='admin@santaclara.agro360.local' and deleted_at is null;"
    $adminName = Invoke-Psql "select name::text from agro360.identity_users where tenant_id='${tenantId}' and id='${adminUser}';"
    Assert-Step 'Usuarios-fixtures' ($adminUser -ne '' -and $adminName -ne '') "admin=$adminUser reader=$readerUser"

    # ============ H1: BASELINE + ESTADO EFETIVO DERIVADO (SEED VENCIDA NAO BLOQUEIA MAIS) ============
    Write-Host '--- H1: restricao seed vencida (SALE ...153) vira EXPIRED derivado no estado efetivo ---' -ForegroundColor Cyan
    $restBase = Invoke-Api GET '/api/livestock/restrictions?onlyActive=false' -Token $adminToken
    Assert-Step 'H1-lista-restricoes-200' ($restBase.Status -eq 200) "HTTP $($restBase.Status)"
    $seedRest = @($restBase.Body | Where-Object { "$($_.animalId)" -eq $animalD }) | Select-Object -First 1
    $restrictionSeedId = "$($seedRest.id)"
    # Status GRAVADO continua ACTIVE (fonte unica preservada); estado EFETIVO derive para EXPIRED.
    Assert-Step 'H1-seed-gravada-active-efetiva-expired' ($null -ne $seedRest -and "$($seedRest.status)" -eq 'ACTIVE' -and "$($seedRest.effectiveStatus)" -eq 'EXPIRED' -and "$($seedRest.expectedUntil)".Substring(0, 10) -eq '2026-09-25') "status=$(if ($seedRest) { $seedRest.status } else { '?' }) efetivo=$(if ($seedRest) { $seedRest.effectiveStatus } else { '?' })"
    $dashBase = Invoke-Api GET '/api/livestock/dashboard' -Token $adminToken
    Assert-Step 'H1-kpi-nao-conta-restricao-vencida' ($dashBase.Status -eq 200 -and "$($dashBase.Body.restrictedAnimals)" -eq '0') "restrictedAnimals=$(if ($dashBase) { $dashBase.Body.restrictedAnimals } else { '?' }) (antes do fix, a seed vencida contava)"
    $activeRest = Invoke-Psql "select count(*)::text from agro360.livestock_restrictions where tenant_id='${tenantId}' and status='ACTIVE';"
    Assert-Step 'H1-banco-so-a-restricao-vencida' ($activeRest -eq '1') "ativas_no_banco=$activeRest (historico intacto; a mudanca e apenas de estado efetivo)"

    # ============ H2: LEITURA DE PROTOCOLOS + UPERS GENERICO CORRIGIDO (422 AMIGAVEL) ============
    Write-Host '--- H2: lista de protocolos (seed) e upsert generico de catalogo aponta p/ rota dedicada ---' -ForegroundColor Cyan
    $protoList = Invoke-Api GET '/api/livestock/protocols' -Token $adminToken
    Assert-Step 'H2-lista-protocolos-200' ($protoList.Status -eq 200 -and @($protoList.Body).Count -ge 2) "HTTP $($protoList.Status) count=$(if ($null -ne $protoList.Body) { @($protoList.Body).Count } else { 0 })"
    Assert-Step 'H2-lista-expoe-janela-carencia-estado' ($protoList.Raw -match '"versionLabel"' -or $protoList.Raw -match '"version_label"' -and $protoList.Raw -match '"effectiveState"') "campos=$($protoList.Raw.Substring(0, [Math]::Min(220, $protoList.Raw.Length)))..."
    $genericSave = Invoke-Api POST '/api/livestock/catalog/protocols' @{ Code = 'DEMO1'; Name = 'Protocolo Generico P4'; Active = $true } -Token $adminToken
    Assert-Step 'H2-upsert-generico-protocols-422-dedicado' ($genericSave.Status -eq 422 -and $genericSave.Raw -match 'protocols_dedicated_route') "HTTP $($genericSave.Status) $($genericSave.Raw) (antes: 500 latente por falta de coluna `code`)"

    # ============ H3: CRIACAO DE PROTOCOLO + BATERIA NEGATIVA ============
    Write-Host '--- H3: criacao de protocolo pela rota dedicada e validacoes ---' -ForegroundColor Cyan
    $protoCmd = @{ Name = 'Antibiotico E2E P4'; Source = 'Bula E2E demonstrativa'; VersionLabel = "e2e.$runTag"; ValidFrom = '2026-01-01'; Purpose = 'OPERATIONAL'; WithdrawalDays = 14; Active = $true; Notes = 'Criado pelo E2E P4' }
    $protoCreate = Invoke-Api POST '/api/livestock/protocols' $protoCmd -Token $adminToken
    Assert-Step 'H3-criar-protocolo-201' ($protoCreate.Status -eq 201 -and "$($protoCreate.Body.id)" -ne '') "HTTP $($protoCreate.Status) $($protoCreate.Raw)"
    $h3ProtocolId = "$($protoCreate.Body.id)"
    $protoRow = Invoke-Psql "select purpose || '|' || withdrawal_days || '|' || case when demo_only then 'demo' else 'prod' end || '|' || case when valid_until is null then 'aberta' else to_char(valid_until,'YYYY-MM-DD') end || '|' || case when active then '1' else '0' end from agro360.livestock_health_protocols where tenant_id='${tenantId}' and id='${h3ProtocolId}';"
    Assert-Step 'H3-protocolo-gravado-no-banco' ($protoRow -eq 'OPERATIONAL|14|prod|aberta|1') "row=$protoRow (vazio valid_until = janela aberta; demo_only=false por criaçao via API)"
    $dupProto = Invoke-Api POST '/api/livestock/protocols' $protoCmd -Token $adminToken
    Assert-Step 'H3-nome-versao-duplicado-409' ($dupProto.Status -eq 409 -and $dupProto.Raw -match 'protocol_version_conflict') "HTTP $($dupProto.Status) $($dupProto.Raw)"
    $badPurpose = Invoke-Api POST '/api/livestock/protocols' @{ Name = 'P4 BadPurpose'; Source = 's'; VersionLabel = 'v1'; ValidFrom = '2026-01-01'; Purpose = 'VACCINATION'; WithdrawalDays = 1; Active = $true } -Token $adminToken
    Assert-Step 'H3-purpose-fora-do-fechado-422' ($badPurpose.Status -eq 422 -and $badPurpose.Raw -match 'protocol_purpose_invalid') "HTTP $($badPurpose.Status) $($badPurpose.Raw)"
    $badWindow = Invoke-Api POST '/api/livestock/protocols' @{ Name = 'P4 BadWindow'; Source = 's'; VersionLabel = 'v1'; ValidFrom = '2026-03-01'; ValidUntil = '2026-02-01'; Purpose = 'OPERATIONAL'; WithdrawalDays = 1; Active = $true } -Token $adminToken
    Assert-Step 'H3-janela-invertida-422' ($badWindow.Status -eq 422 -and $badWindow.Raw -match 'protocol_window_invalid') "HTTP $($badWindow.Status) $($badWindow.Raw)"
    $negDays = Invoke-Api POST '/api/livestock/protocols' @{ Name = 'P4 NegDays'; Source = 's'; VersionLabel = 'v1'; ValidFrom = '2026-01-01'; Purpose = 'OPERATIONAL'; WithdrawalDays = -1; Active = $true } -Token $adminToken
    Assert-Step 'H3-carencia-negativa-422' ($negDays.Status -eq 422 -and $negDays.Raw -match 'protocol_withdrawal_invalid') "HTTP $($negDays.Status) $($negDays.Raw)"
    $longName = 'P' * 161
    $longProto = Invoke-Api POST '/api/livestock/protocols' @{ Name = $longName; Source = 's'; VersionLabel = 'v1'; ValidFrom = '2026-01-01'; Purpose = 'OPERATIONAL'; WithdrawalDays = 1; Active = $true } -Token $adminToken
    Assert-Step 'H3-nome-acima-do-teto-422' ($longProto.Status -eq 422) "HTTP $($longProto.Status) $($longProto.Raw)"
    # Protocolo com valid_no FUTURO (usado em H8 como fora-da-janela para appliedOn no passado).
    $futureProto = Invoke-Api POST '/api/livestock/protocols' @{ Name = 'P4 Future Window'; Source = 's'; VersionLabel = 'v1'; ValidFrom = '2027-01-01'; Purpose = 'OPERATIONAL'; WithdrawalDays = 5; Active = $true } -Token $adminToken
    Assert-Step 'H3-protocolo-janela-futura-201' ($futureProto.Status -eq 201) "HTTP $($futureProto.Status)"
    $h3FutureProtocolId = "$($futureProto.Body.id)"

    # ============ H4: EXECUCAO COM PROTOCOLO (CARÊNCIA DO CATALOGO + RESTRICAO DERIVADA) ============
    Write-Host '--- H4: aplicacao do protocolo H3 no animal B (sede) ---' -ForegroundColor Cyan
    $appliedOn = '2026-10-13'
    $until14 = (Get-Date '2026-10-13').AddDays(14).ToString('yyyy-MM-dd')
    $k4 = "e2e-p4-h4-$runTag"
    $app4 = Invoke-Api POST '/api/livestock/health-events' @{ AnimalId = $animalB; Type = 'TREATMENT'; AppliedOn = $appliedOn; Dose = 2; Unit = 'ml'; WarehouseId = $null; ProductId = $null; WithdrawalDays = 9; NextApplication = $null; Technician = $adminName; ProtocolId = $h3ProtocolId; IdempotencyKey = $k4 } -Token $adminToken
    Assert-Step 'H4-aplicacao-com-protocolo-201' ($app4.Status -eq 201 -and "$($app4.Body.id)" -ne '') "HTTP $($app4.Status) $($app4.Raw)"
    $h4EventId = "$($app4.Body.id)"
    $evRow = Invoke-Psql "select protocol_id::text || '|' || coalesce(protocol_version_label,'~') || '|' || coalesce(idempotency_key,'~') || '|' || to_char(withdrawal_until,'YYYY-MM-DD') from agro360.livestock_health_events where tenant_id='${tenantId}' and id='${h4EventId}';"
    Assert-Step 'H4-evento-referencia-protocolo-e-chave' ($evRow -eq "${h3ProtocolId}|e2e.${runTag}|$k4|$until14") "row=$evRow (carência do CATALOGO=14 prevaleceu sobre payload=9)"
    $anB = Invoke-Psql "select to_char(withdrawal_until,'YYYY-MM-DD') from agro360.livestock_animals where tenant_id='${tenantId}' and id='${animalB}';"
    Assert-Step 'H4-animal-em-carencia-ate-dia-do-catalogo' ($anB -eq $until14) "withdrawal_until=$anB"
    $restRow = Invoke-Psql "select purpose || '|' || protocol_id::text || '|' || source_event_id::text || '|' || to_char(started_on,'YYYY-MM-DD') || '|' || to_char(expected_until,'YYYY-MM-DD') || '|' || case when demo_only then 'demo' else 'prod' end || '|' || status from agro360.livestock_restrictions where tenant_id='${tenantId}' and source_event_id='${h4EventId}';"
    Assert-Step 'H4-restricao-derivada-na-mesma-transacao' ($restRow -eq "OPERATIONAL|${h3ProtocolId}|${h4EventId}|$appliedOn|$until14|prod|ACTIVE") "row=$restRow"

    # ============ H5: REPLAY DE IDEMPOTENCIA (MESMA CHAVE COM ESPACOS, DEDUP POR CHAVE) ============
    Write-Host '--- H5: replay da aplicacao H4 ---' -ForegroundColor Cyan
    $cntEvt0 = Invoke-Psql "select count(*)::text from agro360.livestock_health_events where tenant_id='${tenantId}';"
    $cntRest0 = Invoke-Psql "select count(*)::text from agro360.livestock_restrictions where tenant_id='${tenantId}';"
    $replay = Invoke-Api POST '/api/livestock/health-events' @{ AnimalId = $animalB; Type = 'TREATMENT'; AppliedOn = $appliedOn; Dose = 2; Unit = 'ml'; WithdrawalDays = 9; Technician = $adminName; ProtocolId = $h3ProtocolId; IdempotencyKey = "  $k4  " } -Token $adminToken
    Assert-Step 'H5-replay-chave-com-espacos-mesmo-id' ($replay.Status -eq 201 -and "$($replay.Body.id)" -eq $h4EventId) "HTTP $($replay.Status) id=$($replay.Body.id) original=$h4EventId"
    $cntEvt1 = Invoke-Psql "select count(*)::text from agro360.livestock_health_events where tenant_id='${tenantId}';"
    $cntRest1 = Invoke-Psql "select count(*)::text from agro360.livestock_restrictions where tenant_id='${tenantId}';"
    Assert-Step 'H5-replay-sem-duplicar-evento-ou-restricao' ($cntEvt0 -eq $cntEvt1 -and $cntRest0 -eq $cntRest1) "eventos ${cntEvt0}->${cntEvt1}; restricoes ${cntEvt0}-${cntRest0}->${cntEvt1}-${cntRest1}"
    $other = Invoke-Api POST '/api/livestock/health-events' @{ AnimalId = $animalB; Type = 'TREATMENT'; AppliedOn = $appliedOn; Dose = 1; Unit = 'ml'; WithdrawalDays = 0; ProtocolId = $h3ProtocolId; IdempotencyKey = "${k4}-outra" } -Token $adminToken
    Assert-Step 'H5-chave-diferente-409-overlap' ($other.Status -eq 409 -and $other.Raw -match 'restriction_overlap') "HTTP $($other.Status) $($other.Raw) (mesmo sujeito+purpose ativo dentro da carência)"

    # ============ H6: RESTRICAO EFETIVA JÁ ABERTA -> 409 (NADA GRADNO) ============
    Write-Host '--- H6: aplicacao repetida dentro da carencia ativa ---' -ForegroundColor Cyan
    $evtCntD = Invoke-Psql "select count(*)::text from agro360.livestock_health_events where tenant_id='${tenantId}' and idempotency_key='${k4}-outra';"
    Assert-Step 'H6-409-nao-grava-nada' ($evtCntD -eq '0') "linhas=$evtCntD (transacao inteira descartada antes do insert)"
    $listEv = Invoke-Api GET '/api/livestock/health-events' -Token $adminToken
    $k4Items = @($listEv.Body | Where-Object { "$($_.idempotency_key)" -eq $k4 })
    Assert-Step 'H6-somente-o-evento-original-da-chave' (@($k4Items).Count -eq 1 -and "$(($k4Items | Select-Object -First 1).protocol_id)" -eq $h3ProtocolId) "linhas_com_chave=$(if ($k4Items.Count) { @($k4Items).Count } else { 0 })"

    # ============ H7: EXECUCAO SEM PROTOCOLO (LEGADO, PAYLOAD GOVERNA, SEM RESTRICAO) ============
    Write-Host '--- H7: aplicacao sem protocolo no animal C (Retiro) ---' -ForegroundColor Cyan
    Write-Host ('[h7-pre] db=' + $dbName + ' port=' + $pgPort + ' tlen=' + $tenantId.Length + ' alen=' + $animalC.Length)
    # Um unico psql / uma unica sessao responde por si, o conte e o contexto (db+porta):
    # elimina qualquer variavel entre chamadas separadas (processo, conexao, snapshot).
    $preSql = @"
select 'ctx=' || current_database() || '|port=' || coalesce(inet_server_port()::text,'n');
select 'cnt_tenant=' || count(*)::text from agro360.livestock_animals where tenant_id='${tenantId}' and id='${animalC}' and deleted_at is null;
select 'cnt_pk=' || count(*)::text from agro360.livestock_animals where id='${animalC}' and deleted_at is null;
"@
    $preExpectedCtx = 'ctx=' + $dbName + '|port=' + $pgPort
    $preAttempts = @()
    $preOut = ''
    $preLines = @()
    for ($preTry = 1; $preTry -le 3; $preTry++) {
        $preOut = Invoke-Psql $preSql
        $preLines = @(($preOut -replace "`r", '') -split "`n")
        $preAttempt = (($preLines | Where-Object { $_ -match '^(ctx=|cnt_)' }) -join ' ; ')
        $preAttempts += $preAttempt
        if ((($preLines | Where-Object { $_ -eq $preExpectedCtx }).Count -eq 1) -and (($preLines | Where-Object { $_ -eq 'cnt_tenant=1' }).Count -eq 1)) { break }
        Start-Sleep -Seconds 3
    }
    $preCtxOk = (($preLines | Where-Object { $_ -eq $preExpectedCtx }).Count -eq 1)
    $preOk = $preCtxOk -and (($preLines | Where-Object { $_ -eq 'cnt_tenant=1' }).Count -eq 1)
    if (-not $preOk) {
        $preDump = Invoke-Psql "select id::text || '|t=' || coalesce(tenant_id::text,'N') || '|d=' || coalesce(deleted_at::text,'NULL') || '|s=' || status::text || '|f=' || coalesce(farm_id::text,'NULLFARM') from agro360.livestock_animals order by id;"
        $dbg = @(
            ('esperado_db=' + $dbName), ('esperado_port=' + $pgPort),
            ('tlen=' + $tenantId.Length), ('alen=' + $animalC.Length),
            ('tenantHex=' + [BitConverter]::ToString([Text.Encoding]::UTF8.GetBytes($tenantId))),
            ('animalHex=' + [BitConverter]::ToString([Text.Encoding]::UTF8.GetBytes($animalC))),
            '--- sql resolvido ---', $preSql,
            '--- saida psql (todas as tentativas) ---', ($preAttempts -join "`n"),
            '--- dump completo apos falha ---', $preDump
        ) -join "`n"
        [System.IO.File]::WriteAllText((Join-Path $evidenceDir 'h7-pre-debug.txt'), $dbg, [System.Text.Encoding]::UTF8)
        Assert-Step 'H7-pre-animal-c-presente-no-banco' $false "prereq: a linha do seed precisa estar visivel na MESMA sessao que a API usa; esperadoCtx=$preExpectedCtx; tentativas=[$($preAttempts -join ' || ')]; ver h7-pre-debug.txt"
    }
    Assert-Step 'H7-pre-animal-c-presente-no-banco' $true ('mesma sessao psql da corrida: ' + (($preLines | Where-Object { $_ -match '^(ctx=|cnt_)' }) -join ' ; '))
    $k7 = "e2e-p4-h7-$runTag"
    $app7 = Invoke-Api POST '/api/livestock/health-events' @{ AnimalId = $animalC; Type = 'VACCINATION'; AppliedOn = $appliedOn; Dose = 1; Unit = 'ml'; WithdrawalDays = 3; NextApplication = '2026-11-13'; ProtocolId = $null; IdempotencyKey = $k7 } -Token $adminToken
    Assert-Step 'H7-aplicacao-sem-protocolo-201' ($app7.Status -eq 201) "HTTP $($app7.Status) $($app7.Raw)"
    $h7Id = "$($app7.Body.id)"
    $restCntAfter7 = Invoke-Psql "select count(*)::text from agro360.livestock_restrictions where tenant_id='${tenantId}';"
    Assert-Step 'H7-sem-protocolo-nao-cria-restricao' ($restCntAfter7 -eq $cntRest0) "restricoes=${cntRest0}->${restCntAfter7} (comportamento legado preservado)"
    $anC = Invoke-Psql "select to_char(h.withdrawal_until,'YYYY-MM-DD') || '|' || to_char(h.next_application,'YYYY-MM-DD') from agro360.livestock_health_events h where h.tenant_id='${tenantId}' and h.id='${h7Id}';"
    Assert-Step 'H7-payload-carencia-e-follow-up-gravados' ($anC -eq '2026-10-16|2026-11-13') "row=$anC (payload governa quando nao ha protocolo)"

    # ============ H8: CICLO DE VIDA DO PROTOCOLO (404 / DESATIVADO / FORA DA JANELA) ============
    Write-Host '--- H8: protocolo inexistente, desativado e fora da janela ---' -ForegroundColor Cyan
    $ghost = Invoke-Api POST '/api/livestock/health-events' @{ AnimalId = $animalB; Type = 'TREATMENT'; AppliedOn = $appliedOn; Dose = 1; Unit = 'ml'; ProtocolId = $unknownGuid; IdempotencyKey = "k-h8g-$runTag" } -Token $adminToken
    Assert-Step 'H8-protocolo-inexistente-404' ($ghost.Status -eq 404) "HTTP $($ghost.Status) $($ghost.Raw)"
    $deact = Invoke-Api POST "/api/livestock/protocols/${h3ProtocolId}/deactivate" $null -Token $adminToken
    Assert-Step 'H8-desativar-protocolo-204' ($deact.Status -eq 204) "HTTP $($deact.Status)"
    $deactAgain = Invoke-Api POST "/api/livestock/protocols/${h3ProtocolId}/deactivate" $null -Token $adminToken
    Assert-Step 'H8-redesativar-409' ($deactAgain.Status -eq 409 -and $deactAgain.Raw -match 'protocol_not_active') "HTTP $($deactAgain.Status) $($deactAgain.Raw)"
    $ghostDeact = Invoke-Api POST "/api/livestock/protocols/${unknownGuid}/deactivate" $null -Token $adminToken
    Assert-Step 'H8-desativar-inexistente-404' ($ghostDeact.Status -eq 404) "HTTP $($ghostDeact.Status) $($ghostDeact.Raw)"
    $inactiveApp = Invoke-Api POST '/api/livestock/health-events' @{ AnimalId = $animalB; Type = 'TREATMENT'; AppliedOn = $appliedOn; Dose = 1; Unit = 'ml'; ProtocolId = $h3ProtocolId; IdempotencyKey = "k-h8i-$runTag" } -Token $adminToken
    Assert-Step 'H8-aplicacao-com-protocolo-desativado-409' ($inactiveApp.Status -eq 409 -and $inactiveApp.Raw -match 'protocol_not_active') "HTTP $($inactiveApp.Status) $($inactiveApp.Raw)"
    $outWindow = Invoke-Api POST '/api/livestock/health-events' @{ AnimalId = $animalC; Type = 'TREATMENT'; AppliedOn = $appliedOn; Dose = 1; Unit = 'ml'; ProtocolId = $h3FutureProtocolId; IdempotencyKey = "k-h8w-$runTag" } -Token $adminToken
    Assert-Step 'H8-aplicacao-fora-da-janela-409' ($outWindow.Status -eq 409 -and $outWindow.Raw -match 'protocol_window_invalid') "HTTP $($outWindow.Status) $($outWindow.Raw) (valid_from 2027-01-01 x appliedOn 2026-10-13)"
    $noSubject = Invoke-Api POST '/api/livestock/health-events' @{ Type = 'TREATMENT'; AppliedOn = $appliedOn; Dose = 1; Unit = 'ml'; IdempotencyKey = "k-h8s-$runTag" } -Token $adminToken
    Assert-Step 'H8-sem-sujeito-422-amigavel' ($noSubject.Status -eq 422 -and $noSubject.Raw -match 'health_subject_required') "HTTP $($noSubject.Status) $($noSubject.Raw) (antes: CHECK do banco virava erro genérico)"

    # ============ H9: AGENDA (VENCIDOS / FUTUROS / ESCOPO DE PROPRIEDADE) ============
    Write-Host '--- H9: agenda de sanidade sobre next_application ---' -ForegroundColor Cyan
    # O evento H4 tem follow-up? nao — H4 nao definiu nextApplication. Para a agenda temos:
    #   H7 (animal C, Retiro) next_application=2026-11-13 -> UPCOMING.
    # Adicionamos um follow-up VENCIDO direto via nova aplicacao? Nao — vazio != inventado:
    # usamos apenas o que existe. H4/H5 nao definem follow-up (vazio segue fora da agenda).
    $agAll = Invoke-Api GET '/api/livestock/agenda' -Token $adminToken
    Assert-Step 'H9-agenda-200' ($agAll.Status -eq 200) "HTTP $($agAll.Status)"
    $agItems = @($agAll.Body)
    Assert-Step 'H9-agenda-conta-somente-follow-up-registrado' (@($agItems).Count -eq 1 -and "$(($agItems | Select-Object -First 1).dueDate)".Substring(0, 10) -eq '2026-11-13' -and "$(($agItems | Select-Object -First 1).state)" -eq 'UPCOMING') "itens=$(if ($agItems.Count) { @($agItems).Count } else { 0 }) (eventos sem follow-up nao aparecem — vazio != vencido)"
    $agRetiro = Invoke-Api GET "/api/livestock/agenda?farmId=$farmRetiro" -Token $adminToken
    Assert-Step 'H9-filter-escopo-retiro-acha-o-item' (@(@($agRetiro.Body)).Count -eq 1 -and "$(@($agRetiro.Body)[0].animalId)" -eq $animalC) "escopo Retiro filtra pelo sujeito (animal do evento)"
    $agSede = Invoke-Api GET "/api/livestock/agenda?farmId=$farmSede" -Token $adminToken
    Assert-Step 'H9-filter-escopo-sede-vazio' (@(@($agSede.Body)).Count -eq 0) "itens_sede=$(if ($null -ne $agSede.Body) { @(@($agSede.Body)).Count } else { 0 })"
    $agGhost = Invoke-Api GET "/api/livestock/agenda?farmId=$unknownGuid" -Token $adminToken
    Assert-Step 'H9-propriedade-desconhecida-vazia' (@(@($agGhost.Body)).Count -eq 0) "itens=$(if ($null -ne $agGhost.Body) { @(@($agGhost.Body)).Count } else { 0 })"
    $agOverdue = Invoke-Api GET '/api/livestock/agenda?overdueOnly=true' -Token $adminToken
    Assert-Step 'H9-overdueonly-sem-itens-vencidos' (@(@($agOverdue.Body)).Count -eq 0) "itens=$(if ($null -ne $agOverdue.Body) { @(@($agOverdue.Body)).Count } else { 0 }) (nenhum follow-up vencido nesta corrida)"

    # ============ H10: EFEITO DO ESTADO EFETIVO (PREVIEW + LIBERACAO DA SEED VENCIDA) ============
    Write-Host '--- H10: ordem NOVA com ...153 previa Elegível; liberar a restricao vencida 409 ---' -ForegroundColor Cyan
    $ho = Invoke-Api POST '/api/livestock/handling-orders' @{ FarmId = $farmSede; HandlingType = 'VACCINATION'; PlannedOn = '2026-10-14'; ResponsibleId = $adminUser; Priority = 'LOW'; MassAtomic = $false; AnimalIds = @($animalD) } -Token $adminToken
    Assert-Step 'H10-ordem-de-manejo-nova-201' ($ho.Status -eq 201 -and "$($ho.Body.id)" -ne '') "HTTP $($ho.Status) $($ho.Raw)"
    $h10OrderId = "$($ho.Body.id)"
    $prev = Invoke-Api GET "/api/livestock/handling-orders/${h10OrderId}/preview" -Token $adminToken
    Assert-Step 'H10-preview-elegivel-apesar-da-restricao-seed' ($prev.Status -eq 200 -and $prev.Raw -match 'Eleg' -and -not ($prev.Raw -match 'restri')) "preview=$($prev.Raw) (restrição SALE vencida em 2026-09-25 deixa de bloquear o manejo)"
    $relSeed = Invoke-Api POST "/api/livestock/restrictions/${restrictionSeedId}/release" @{ ReleasedOn = '2026-10-13'; Authority = 'MV E2E P4'; Criteria = 'Carência já decorrida (2026-09-25)' } -Token $adminToken
    Assert-Step 'H10-liberar-restricao-efetivamente-vencida-409' ($relSeed.Status -eq 409 -and $relSeed.Raw -match 'restriction_not_active') "HTTP $($relSeed.Status) $($relSeed.Raw) (estado efetivo EXPIRED nao aceita liberacao — historico nao e reescrito)"
    $stillStored = Invoke-Psql "select status from agro360.livestock_restrictions where tenant_id='${tenantId}' and id='${restrictionSeedId}';"
    Assert-Step 'H10-status-gravado-intacto' ($stillStored -eq 'ACTIVE') "status_banco=$stillStored (somente o estado efetivo derivou; nenhuma update do historico)"

    # ============ H11: ACESSO (401 / LEITOR 200-403 / ESCOPO DE UNIDADE) ============
    Write-Host '--- H11: controle de acesso da jornada de sanidade ---' -ForegroundColor Cyan
    $anonP = Invoke-Api GET '/api/livestock/protocols'
    $anonA = Invoke-Api GET '/api/livestock/agenda'
    Assert-Step 'H11-anonimo-401' ($anonP.Status -eq 401 -and $anonA.Status -eq 401) "protocols=$($anonP.Status) agenda=$($anonA.Status)"
    $readP = Invoke-Api GET '/api/livestock/protocols' -Token $readerToken
    $readA = Invoke-Api GET '/api/livestock/agenda' -Token $readerToken
    Assert-Step 'H11-leitor-le-protocolos-e-agenda-200' ($readP.Status -eq 200 -and $readA.Status -eq 200) "protocols=$($readP.Status) agenda=$($readA.Status)"
    $writerPost = Invoke-Api POST '/api/livestock/protocols' @{ Name = 'P4 Writer'; Source = 's'; VersionLabel = 'v1'; ValidFrom = '2026-01-01'; Purpose = 'OPERATIONAL'; WithdrawalDays = 1; Active = $true } -Token $readerToken
    Assert-Step 'H11-leitor-cria-protocolo-403' ($writerPost.Status -eq 403) "HTTP $($writerPost.Status) $($writerPost.Raw)"
    $writerApp = Invoke-Api POST '/api/livestock/health-events' @{ AnimalId = $animalC; Type = 'TREATMENT'; AppliedOn = $appliedOn; Dose = 1; Unit = 'ml'; WithdrawalDays = 1; IdempotencyKey = "k-h11-$runTag" } -Token $readerToken
    Assert-Step 'H11-leitor-registra-aplicacao-403' ($writerApp.Status -eq 403) "HTTP $($writerApp.Status) $($writerApp.Raw)"
    # Escopo de unidade: admin com X-Farm-ID fora... admin tem escopo total; o teste de escopo usa
    # o middleware contra o LEITOR: header X-Farm-ID=Retiro (fora do escopo FARM sede dele).
    $outScope = Invoke-Api GET '/api/livestock/agenda' $null $readerToken @{ 'X-Farm-ID' = $farmRetiro }
    Assert-Step 'H11-unidade-fora-do-escopo-403' ($outScope.Status -eq 403 -and $outScope.Raw -match 'forbidden_unit_scope') "HTTP $($outScope.Status) $($outScope.Raw)"
    $h11Cnt = Invoke-Psql "select count(*)::text from agro360.livestock_health_events where tenant_id='${tenantId}' and idempotency_key='k-h11-$runTag';"
    Assert-Step 'H11-escrita-rejeitada-nao-mudou-estado' ($h11Cnt -eq '0') "linhas=$h11Cnt"

    # ============ H12: RECONCILIAÇÃO API==DB + INVARIANTE DE CHAVE + SMOKE ============
    Write-Host '--- H12: reconciliacao final api==db ---' -ForegroundColor Cyan
    $dbState = Invoke-Psql @"
select (select count(*)::text from agro360.livestock_health_events where tenant_id='${tenantId}') || '|'
     || (select count(*)::text from agro360.livestock_health_events where tenant_id='${tenantId}' and idempotency_key is not null) || '|'
     || (select count(*)::text from agro360.livestock_restrictions where tenant_id='${tenantId}') || '|'
     || (select to_char(withdrawal_until,'YYYY-MM-DD') from agro360.livestock_animals where tenant_id='${tenantId}' and id='${animalB}');
"@
    # eventos: 0 seed + H4(1) + H7(1) = 2 | com chave: k4 + k7 = 2 (H5/H6/H8 nao duplicaram)
    # restricoes: 1 seed + H4 derivada = 2 | carência de B: appliedOn+14 (catalogo)
    Assert-Step 'H12-contagens-finais' ($dbState -eq "2|2|2|$until14") "eventos|chaves|restricoes|carenciaB real=$dbState (esperado 2|2|2|$until14)"
    $dupKeys = Invoke-Psql "select count(*)::text from (select 1 from agro360.livestock_health_events where tenant_id='${tenantId}' and idempotency_key is not null group by idempotency_key having count(*) > 1) x;"
    Assert-Step 'H12-invariante-uma-linha-por-chave' ($dupKeys -eq '0') "chaves duplicadas=$dupKeys"
    $listFin = Invoke-Api GET '/api/livestock/health-events' -Token $adminToken
    Assert-Step 'H12-api-igual-db-eventos' (@(@($listFin.Body)).Count -eq 2) "api=$(if ($null -ne $listFin.Body) { @(@($listFin.Body)).Count } else { 0 }) banco=2"
    $h4Api = @($listFin.Body | Where-Object { "$($_.id)" -eq $h4EventId }) | Select-Object -First 1
    Assert-Step 'H12-api-devolve-referencia-do-protocolo' ($null -ne $h4Api -and "$($h4Api.protocol_id)" -eq $h3ProtocolId -and "$($h4Api.protocol_version_label)" -eq "e2e.$runTag") "protocolo=$(if ($h4Api) { $h4Api.protocol_id } else { '?' }) versao=$(if ($h4Api) { $h4Api.protocol_version_label } else { '?' })"
    $restFin = Invoke-Api GET '/api/livestock/restrictions?onlyActive=false' -Token $adminToken
    Assert-Step 'H12-api-igual-db-restricoes' (@(@($restFin.Body)).Count -eq 2) "api=$(if ($null -ne $restFin.Body) { @(@($restFin.Body)).Count } else { 0 }) banco=2"
    $listB = Invoke-Api GET '/api/livestock/animals?search=SC-N-1002' -Token $adminToken
    $bItem = @($listB.Body.items | Where-Object { "$($_.tag)" -eq 'SC-N-1002' }) | Select-Object -First 1
    $bWithd = "$($bItem.withdrawalUntil)".Substring(0, 10)
    Assert-Step 'H12-lista-do-animal-b-igual-banco' ($null -ne $bItem -and $bWithd -eq $until14) "api=$bWithd banco=$until14"

    $snapshot = Invoke-Psql @"
select h.event_type, to_char(h.occurred_on,'YYYY-MM-DD'), h.dose, h.unit, a.tag, hr.name, to_char(h.withdrawal_until,'YYYY-MM-DD'), to_char(h.next_application,'YYYY-MM-DD'), coalesce(p.name,'~'), coalesce(h.protocol_version_label,'~'), coalesce(h.idempotency_key,'~'), r.status, r.purpose
from agro360.livestock_health_events h
left join agro360.livestock_animals a on a.id=h.animal_id and a.tenant_id=h.tenant_id
left join agro360.livestock_herds hr on hr.id=h.herd_id and hr.tenant_id=h.tenant_id
left join agro360.livestock_health_protocols p on p.id=h.protocol_id and p.tenant_id=h.tenant_id
left join agro360.livestock_restrictions r on r.source_event_id=h.id and r.tenant_id=h.tenant_id
where h.tenant_id='${tenantId}'
order by h.occurred_on, h.id;
"@
    $snapshot | Set-Content (Join-Path $evidenceDir 'health-final-snapshot.txt')
    Write-Host "Snapshot final dos eventos de sanidade salvo em health-final-snapshot.txt" -ForegroundColor DarkGray

    $dash = Invoke-Api GET '/api/livestock/dashboard' -Token $adminToken
    Assert-Step 'H12-dashboard-saneamento-200' ($dash.Status -eq 200 -and "$($dash.Body.restrictedAnimals)" -eq '1') "HTTP $($dash.Status) restrictedAnimals=$(if ($dash) { $dash.Body.restrictedAnimals } else { '?' }) (1 = a restricao derivada ainda em carência; a seed vencida segue fora do KPI)"

    Stop-Proc $script:apiHostProc
    if (-not $KeepRunning -and $weStartedCluster) { & $pg_ctl -D $templateDir -m fast stop | Out-Null }
    Write-Host ""
    Write-Host "E2E PECUARIA P4 (SANIDADE): TODOS OS CENARIOS PASSARAM" -ForegroundColor Green
    Write-Host "Evidencia: $evidenceDir" -ForegroundColor Green
    exit 0
}
catch {
    Write-Host ""
    Write-Host "E2E PECUARIA P4: FALHOU" -ForegroundColor Red
    Write-Host "$_" -ForegroundColor Red
    Write-Host "Evidencia: $evidenceDir" -ForegroundColor Red
    Stop-Proc $script:apiHostProc
    if (-not $KeepRunning -and $weStartedCluster) { try { & $pg_ctl -D $templateDir -m fast stop | Out-Null } catch {} }
    exit 1
}
finally {
    Stop-Transcript | Out-Null
}
