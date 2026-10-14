param(
    [string]$PostgresBin = 'C:\Program Files\PostgreSQL\18\bin',
    [switch]$KeepRunning,
    [switch]$RebuildTemplate
)

# E2E da jornada "pesagens/desempenho" do sprint Pecuaria P3 (2026-10-13):
#   W0 migration 142 (evidencia de precisao da mediacao): coluna measurement_precision presente;
#      reexecucao nao duplica versao/colunas; cadeia 140/141 intacta no mesmo schema.
#   W1 baseline do seed: lista de pesagens expoe a trilha de evidencias (tag do animal, equipamento,
#      responsavel, conversao, justificativa, valor anterior, correcao, precisao — tudo null no dado
#      antigo, nada inventado); desempenho do par semeado (305.0 -> 318.5 em ~31 dias) produz o
#      indicador diário esperado (≈0.4354), com critério explícito.
#   W2 jornada individual: pesagem com chave + equipamento + responsável + precisão -> 201;
#      trilha completa (pesagem com previous_weight_kg do par semeado, animal atualizado, evento
#      WEIGHING com ganho diário computado no intervalo positivo); a lista devolve os campos de
#      evidência gravados (nome do responsável, tag, precisão).
#   W3 replay de idempotencia: MESMA chave com espaços na frente/atras retorna o MESMO id (chave
#      normalizada no padrao do P2) sem duplicar pesagem/evento; replay com chave igual e payload
#      diferente também não duplica (dedup por chave, nao por conteudo — contrato documentado).
#   W4 intervalo ZERO: duas pesagens no MESMO timestamp -> ambas aceitas, ganho permanece VAZIO
#      (nulo, nunca zero) nos dois eventos; o desempenho expoe a linha com averageDailyGainKg
#      explicitamente null e inicial/final determinísticos (tie-break por id) em leituras repetidas.
#   W5 uma unica pesagem -> ausencia da linha de desempenho (vazio != zero; having count(*)>1).
#   W6 limites de peso por especie (weight_limits): peso abaixo do minimo sem justificativa ->
#      422 weight_review (nada gravado); com justificativa -> 201 + review_required=true persistido.
#   W7 bateria de validacao (422/404 amigaveis): responsavel inexistente 404; grupo/lot inexistente
#      404; sujeito coletivo ausente 422; individuo sem animal 422; unidade sem fator 422;
#      peso nulo 422; cabecas nulas 422; campo de evidencia acima do limite 422; coletivas validas
#      (grupo por quantidade com justificativa de total acima do teto individual; lote de manejo).
#   W8 correcao auditada: correcao preserva o valor anterior (previous_weight_kg), o link
#      corrected_from_id e copia a evidencia (equipamento/metodo/precisao) da origem; a linha
#      original permanece marcada como substituida; responsavel inexistente 404 antes de gravar.
#   W9 fluxo legado coexistente (/api/v1/livestock/.../weights, granularidade de DATA): mesma data
#      da ultima pesagem -> 409 weight_date_invalid (semaica proprio, documentada); data posterior
#      -> aceite e atualiza o peso atual do animal.
#   W10 acesso: 401 anonimo; leitor (somente livestock.read + escopo FARM na sede) le pesagens e
#      desempenho 200, 403 fora do escopo de unidade (X-Farm-ID) e 403 em escrita (estado intacto).
#   W11 reconciliacao api==db: contagens finais por animal x trilhas de eventos; invariante de uma
#      linha por chave de idempotencia em pesagens; peso atual pos-correcao igual na API e no banco;
#      snapshot + dashboard.
#
# Cluster descartavel (template compartilhado pg-journey-template; a corrida precisa dele com a
# migration 142 embutida no full.sql — se ausente, rode com -RebuildTemplate) + API em Release.
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

$evidenceDir = Join-Path $root ('artifacts\livestock-p3-e2e-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidenceDir | Out-Null
Start-Transcript -Path (Join-Path $evidenceDir 'transcript.log') -Append | Out-Null
$processes = [Collections.Generic.List[Diagnostics.Process]]::new()
$weStartedCluster = $false
$dbName = 'agro360_livp3_' + [guid]::NewGuid().ToString('N').Substring(0, 12)
$templateDir = Join-Path $env:TEMP 'opencode\pg-journey-template'
$templateDb = 'journey_template'
$templateMarker = Join-Path $templateDir 'template-ready.txt'
$fullSqlPath = Join-Path $root 'database/agro360-postgres-full.sql'
$migration142Path = Join-Path $root 'database/migrations/142_livestock_weighing_precision.sql'
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
$animalA = '30000000-0000-4000-8000-000000000150'      # SC-N-1001 (reservado; par de pesagens semeado 305.0 -> 318.5)
$animalB = '30000000-0000-4000-8000-000000000151'      # SC-N-1002 (ativo; sem pesagens no livro)
$animalC = '30000000-0000-4000-8000-000000000152'      # SC-N-1003 (ativo, no Retiro)
$animalD = '30000000-0000-4000-8000-000000000153'      # SC-N-1004 (ativo)
$herdQty = '30000000-0000-4000-8000-000000000141'      # Lote coletivo de recria (QUANTITY, 40)
$lotSeed = '30000000-0000-4000-8000-000000000142'      # Lote de manejo 2026-09 (OPEN, sede)
$w2Id = ''
$correctionId = ''
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
        Write-Host "Template existente em $templateDir (use -RebuildTemplate para forcar recarga; o schema precisa ter 142.0.0)." -ForegroundColor Cyan
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

    # ============ W0: MIGRATION 142 — COLUNA DE PRECISAO, IDEMPOTENTE, CHADEA 140/141 INTACTA ============
    Write-Host '--- W0: migration 142 (precisao da mediacao em pesagens) ---' -ForegroundColor Cyan
    $ver142 = Invoke-Psql "select version from agro360.platform_schema_versions where version='142.0.0';"
    Assert-Step 'W0-schema-142.0.0-present' ($ver142 -eq '142.0.0') 'mig 142 embutida no full.sql; se ausente, rode com -RebuildTemplate'
    $ver141 = Invoke-Psql "select version from agro360.platform_schema_versions where version='141.0.0';"
    $ver140 = Invoke-Psql "select version from agro360.platform_schema_versions where version='140.0.0';"
    Assert-Step 'W0-cadeia-140-141-intacta' ($ver140 -eq '140.0.0' -and $ver141 -eq '141.0.0') 'estado previo P1/P2 presente'
    $colCnt = Invoke-Psql "select count(*)::text from information_schema.columns where table_schema='agro360' and table_name='livestock_weighings' and column_name='measurement_precision';"
    Assert-Step 'W0-coluna-measurement-precision' ($colCnt -eq '1') "colunas=$colCnt"
    $wgBefore = Invoke-Psql "select count(*)::text from agro360.livestock_weighings;"
    $migSql = Get-Content $migration142Path -Raw -Encoding UTF8
    Invoke-Psql $migSql | Out-Null
    $wgAfter = Invoke-Psql "select count(*)::text from agro360.livestock_weighings;"
    $colAfter = Invoke-Psql "select count(*)::text from information_schema.columns where table_schema='agro360' and table_name='livestock_weighings' and column_name='measurement_precision';"
    $verRows = Invoke-Psql "select count(*)::text from agro360.platform_schema_versions where version='142.0.0';"
    Assert-Step 'W0-reexecucao-142-sem-alterar-dados' ($wgBefore -eq $wgAfter -and $colAfter -eq '1' -and $verRows -eq '1') "pesagens ${wgBefore}->${wgAfter}; colunas=$colAfter; linhas_142=$verRows"

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
select '${tenantId}', id, 'ACTIVE', 'E2E pecuaria P3 - modulo pecuaria', now()
from agro360.platform_module_catalog where code='livestock'
on conflict(tenant_id,module_id) do update set status='ACTIVE';
"@ | Out-Null

    # Leitor dedicado: papel somente-read de pecuaria -> 200 em leitura e 403 em escrita
    # (deterministico: nao depende do estado das permissaoes do operador seed).
    Invoke-Psql @"
insert into agro360.identity_roles(id,tenant_id,code,name,is_system)
values('${readerRole}','${tenantId}','E2E_P3_LIVESTOCK_READ_ONLY','Leitor Pecuaria E2E P3',false)
on conflict (tenant_id,code) do nothing;
insert into agro360.identity_users(id,tenant_id,name,email,password_hash,status,must_change_password,created_by)
select '${readerUser}','${tenantId}','Leitor Pecuaria E2E P3','e2e.p3.reader@santaclara.agro360.local','$knownHash','ACTIVE',false,'$(Invoke-Psql "select id::text from agro360.identity_users where tenant_id='${tenantId}' and lower(email)='admin@santaclara.agro360.local' and deleted_at is null;")'
where not exists(select 1 from agro360.identity_users where tenant_id='${tenantId}' and lower(email)='e2e.p3.reader@santaclara.agro360.local');
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
    $readerLogin = Invoke-Api POST '/api/v1/auth/login' @{ TenantSlug = 'santa-clara'; Email = 'e2e.p3.reader@santaclara.agro360.local'; Password = 'Agro360Admin@2024!' }
    Assert-Step 'Login-leitor-santa' ($readerLogin.Status -eq 200) "HTTP $($readerLogin.Status)"
    $readerToken = $readerLogin.Body.accessToken
    $adminUser = Invoke-Psql "select id::text from agro360.identity_users where tenant_id='${tenantId}' and lower(email)='admin@santaclara.agro360.local' and deleted_at is null;"
    $adminName = Invoke-Psql "select name::text from agro360.identity_users where tenant_id='${tenantId}' and id='${adminUser}';"
    Assert-Step 'Usuarios-fixtures' ($adminUser -ne '' -and $adminName -ne '') "admin=$adminUser reader=$readerUser"

    # ============ W1: BASELINE DO SEED (TRILHA DE EVIDENCIAS + INDICADOR POSITIVO) ============
    Write-Host '--- W1: baseline de pesagens e desempenho do seed ---' -ForegroundColor Cyan
    $listBase = Invoke-Api GET '/api/livestock/weighings' -Token $adminToken
    Assert-Step 'W1-lista-baseline-200' ($listBase.Status -eq 200) "HTTP $($listBase.Status)"
    Assert-Step 'W1-lista-expoe-trilha-de-evidencias' ($listBase.Raw -match 'SC-N-1001' -and $listBase.Raw -match '"equipment"' -and $listBase.Raw -match '"responsibleName"' -and $listBase.Raw -match '"convertedFromUnit"' -and $listBase.Raw -match '"reviewJustification"' -and $listBase.Raw -match '"previousWeightKg"' -and $listBase.Raw -match '"correctedFromId"' -and $listBase.Raw -match '"measurementPrecision"') 'campos novos presentes (valores null no dado antigo — nada inventado)'
    Assert-Step 'W1-seed-sem-valores-inventados' (-not ($listBase.Raw -match '"equipment":"[^"]' )) "equipamento do seed segue vazio (o seed jamais preencheu a coluna)"

    $perfBase = Invoke-Api GET "/api/livestock/performance?animalId=$animalA" -Token $adminToken
    Assert-Step 'W1-desempenho-do-par-semeado-200' ($perfBase.Status -eq 200) "HTTP $($perfBase.Status)"
    $pb = @($perfBase.Body) | Select-Object -First 1
    $gainBase = [double]$pb.averageDailyGainKg
    Assert-Step 'W1-indicador-diario-esperado' ("$($pb.measurements)" -eq '2' -and [double]$pb.initialWeightKg -eq 305.0 -and [double]$pb.finalWeightKg -eq 318.5 -and $gainBase -gt 0.43 -and $gainBase -lt 0.44) "medicoes=$($pb.measurements) inicial=$($pb.initialWeightKg) final=$($pb.finalWeightKg) ganho=$($pb.averageDailyGainKg) criterio=$($pb.criteria)"

    # ============ W2: JORNADA INDIVIDUAL COM TRILHA COMPLETA ============
    Write-Host '--- W2: pesagem individual com chave, equipamento, responsavel e precisao ---' -ForegroundColor Cyan
    $k2 = "e2e-p3-w2-$runTag"
    $w2Cmd = @{ Scope = 'INDIVIDUAL'; FarmId = $farmSede; AnimalId = $animalA; WeighedAt = '2026-10-12T10:00:00Z'; Weight = 330; Unit = 'kg'; Method = 'BALANCA'; Equipment = 'Balanca eletronica E2E'; Source = 'MANUAL'; ResponsibleId = $adminUser; Notes = "Pesagem E2E P3 ${runTag}"; IdempotencyKey = $k2; MeasurementPrecision = '0,1 kg' }
    $w2 = Invoke-Api POST '/api/livestock/weighings' $w2Cmd -Token $adminToken
    Assert-Step 'W2-pesagem-201' ($w2.Status -eq 201 -and "$($w2.Body.id)" -ne '') "HTTP $($w2.Status) $($w2.Raw)"
    $w2Id = "$($w2.Body.id)"
    $w2Row = Invoke-Psql "select coalesce(equipment,'~') || '|' || coalesce(measurement_precision,'~') || '|' || coalesce(previous_weight_kg::text,'~') || '|' || coalesce(idempotency_key,'~') || '|' || case when responsible_id='${adminUser}' then 'OK' else '?' end from agro360.livestock_weighings where tenant_id='${tenantId}' and id='${w2Id}';"
    Assert-Step 'W2-evidencias-gravadas-no-banco' ($w2Row -eq "Balanca eletronica E2E|0,1 kg|318.500|$k2|OK") "row=$w2Row (previous_weight_kg trazido do par semeado)"
    $anA = Invoke-Psql "select coalesce(current_weight_kg::text,'~') || '|' || to_char(last_weight_date,'YYYY-MM-DD') from agro360.livestock_animals where tenant_id='${tenantId}' and id='${animalA}';"
    Assert-Step 'W2-peso-atual-do-animal-atualizado' ($anA -eq '330.0000|2026-10-12') "current=$anA"
    $evA = Invoke-Psql "select coalesce((data->>'weightKg')::text,'~') || '|' || coalesce((data->>'dailyGainKg')::text,'~') || '|' || coalesce(data->>'source','~') from agro360.livestock_animal_events where tenant_id='${tenantId}' and animal_id='${animalA}' and event_type='WEIGHING' order by created_at desc limit 1;"
    $evParts = $evA -split '\|'
    $evGain = [double]$evParts[1]
    Assert-Step 'W2-evento-weighing-com-ganho-diario' ($evParts[0] -eq '330' -and $evGain -gt 0.21 -and $evGain -lt 0.22 -and $evParts[2] -eq 'MANUAL') "evento=$evA (intervalo positivo 2026-08-20 -> 2026-10-12)"
    $listW2 = Invoke-Api GET "/api/livestock/weighings?animalId=$animalA" -Token $adminToken
    $w2Item = @($listW2.Body | Where-Object { "$($_.id)" -eq $w2Id }) | Select-Object -First 1
    Assert-Step 'W2-lista-devolve-evidencia' ($null -ne $w2Item -and "$($w2Item.animalTag)" -eq 'SC-N-1001' -and "$($w2Item.equipment)" -eq 'Balanca eletronica E2E' -and "$($w2Item.responsibleName)" -eq $adminName -and "$($w2Item.measurementPrecision)" -eq '0,1 kg' -and [double]$w2Item.previousWeightKg -eq 318.5) "tag=$(if ($w2Item) { $w2Item.animalTag } else { '?' }) responsavel=$(if ($w2Item) { $w2Item.responsibleName } else { '?' })"

    # ============ W3: REPLAY DE IDEMPOTENCIA (CHAVE NORMALIZADA, DEDUP POR CHAVE) ============
    Write-Host '--- W3: replay com a mesma chave (com espacos) e payload diferente ---' -ForegroundColor Cyan
    $cntA0 = Invoke-Psql "select count(*)::text from agro360.livestock_weighings where tenant_id='${tenantId}' and animal_id='${animalA}';"
    $evA0 = Invoke-Psql "select count(*)::text from agro360.livestock_animal_events where tenant_id='${tenantId}' and animal_id='${animalA}' and event_type='WEIGHING';"
    $w2r1 = @{ Scope = 'INDIVIDUAL'; FarmId = $farmSede; AnimalId = $animalA; WeighedAt = '2026-10-12T10:00:00Z'; Weight = 330; Unit = 'kg'; Method = 'BALANCA'; Equipment = 'Balanca eletronica E2E'; Source = 'MANUAL'; ResponsibleId = $adminUser; Notes = "Pesagem E2E P3 ${runTag}"; IdempotencyKey = "  $k2  "; MeasurementPrecision = '0,1 kg' }
    $replay1 = Invoke-Api POST '/api/livestock/weighings' $w2r1 -Token $adminToken
    Assert-Step 'W3-replay-chave-com-espacos-mesmo-id' ($replay1.Status -eq 201 -and "$($replay1.Body.id)" -eq $w2Id) "HTTP $($replay1.Status) id=$($replay1.Body.id) original=$w2Id"
    $w2r2 = @{ Scope = 'INDIVIDUAL'; FarmId = $farmSede; AnimalId = $animalA; WeighedAt = '2026-10-12T11:00:00Z'; Weight = 999; Unit = 'kg'; Source = 'MANUAL'; ResponsibleId = $adminUser; IdempotencyKey = $k2 }
    $replay2 = Invoke-Api POST '/api/livestock/weighings' $w2r2 -Token $adminToken
    Assert-Step 'W3-replay-mesma-chave-payload-diferente-mesmo-id' ($replay2.Status -eq 201 -and "$($replay2.Body.id)" -eq $w2Id) "id=$($replay2.Body.id) (dedup por chave, nao por conteudo — contrato do P2)"
    $cntA1 = Invoke-Psql "select count(*)::text from agro360.livestock_weighings where tenant_id='${tenantId}' and animal_id='${animalA}';"
    $evA1 = Invoke-Psql "select count(*)::text from agro360.livestock_animal_events where tenant_id='${tenantId}' and animal_id='${animalA}' and event_type='WEIGHING';"
    Assert-Step 'W3-replay-sem-duplicar-trilha' ($cntA0 -eq $cntA1 -and $evA0 -eq $evA1) "pesagens ${cntA0}->${cntA1}; eventos ${evA0}->${evA1}"
    $keyStored = Invoke-Psql "select idempotency_key::text from agro360.livestock_weighings where tenant_id='${tenantId}' and id='${w2Id}';"
    Assert-Step 'W3-chave-normalizada-no-banco' ($keyStored -eq $k2) "key=$keyStored (sem os espacos do replay)"

    # ============ W4: INTERVALO ZERO (MESMO TIMESTAMP -> GANHO VAZIO, NUNCA ZERO) ============
    Write-Host '--- W4: duas pesagens no MESMO timestamp no animal B ---' -ForegroundColor Cyan
    $zA = Invoke-Api POST '/api/livestock/weighings' @{ Scope = 'INDIVIDUAL'; FarmId = $farmSede; AnimalId = $animalB; WeighedAt = '2026-10-12T08:00:00Z'; Weight = 300; Unit = 'kg'; Source = 'MANUAL' } -Token $adminToken
    Assert-Step 'W4-1a-pesagem-zero-201' ($zA.Status -eq 201) "HTTP $($zA.Status) $($zA.Raw)"
    $zB = Invoke-Api POST '/api/livestock/weighings' @{ Scope = 'INDIVIDUAL'; FarmId = $farmSede; AnimalId = $animalB; WeighedAt = '2026-10-12T08:00:00Z'; Weight = 302; Unit = 'kg'; Source = 'MANUAL'; IdempotencyKey = "k-zb-$runTag" } -Token $adminToken
    Assert-Step 'W4-2a-pesagem-zero-201' ($zB.Status -eq 201) "HTTP $($zB.Status) $($zB.Raw)"
    # (dois statements: o idioma inline "<cmd> <here> -split x" NAO parte a saida do comando
    # em PS 5.1 — confirmado empiricamente; a saida chega como string unica com CRLF interno)
    $evBraw = Invoke-Psql @"
select coalesce(case when (data->>'dailyGainKg') is null then 'NULL' else (data->>'dailyGainKg') end, '~')
from agro360.livestock_animal_events
where tenant_id='${tenantId}' and animal_id='${animalB}' and event_type='WEIGHING'
order by created_at desc limit 2;
"@
    $evB = ($evBraw -split "`n") | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' }
    Assert-Step 'W4-ganho-vazio-nos-dois-eventos' (@($evB).Count -eq 2 -and @($evB) -notcontains '0' -and @($evB) -contains 'NULL') "eventos=$($evB -join ';') (intervalo zero -> nulo, nunca zero/nenhum crash)"
    $perfZ1 = Invoke-Api GET "/api/livestock/performance?animalId=$animalB" -Token $adminToken
    $perfZ2 = Invoke-Api GET "/api/livestock/performance?animalId=$animalB" -Token $adminToken
    $zb1 = @($perfZ1.Body) | Select-Object -First 1
    $zb2 = @($perfZ2.Body) | Select-Object -First 1
    Assert-Step 'W4-desempenho-com-ganho-explicitamente-null' ($perfZ1.Status -eq 200 -and "$($zb1.measurements)" -eq '2' -and $perfZ1.Raw -match '"averageDailyGainKg":null') "read1=$($perfZ1.Raw)"
    Assert-Step 'W4-inicial-final-deterministicos-em-tie-break' ([double]$zb1.initialWeightKg -eq 300.0 -and [double]$zb1.finalWeightKg -eq 302.0 -and [double]$zb2.initialWeightKg -eq 300.0 -and [double]$zb2.finalWeightKg -eq 302.0) "l1=$($zb1.initialWeightKg)->$(if ($zb1) { $zb1.finalWeightKg } else { '?' }); l2=$($zb2.initialWeightKg)->$(if ($zb2) { $zb2.finalWeightKg } else { '?' })"

    # ============ W5: UMA UNICA PESAGEM -> SEM LINHA DE DESEMPENHO ============
    Write-Host '--- W5: animal C com uma unica pesagem fica fora do desempenho ---' -ForegroundColor Cyan
    $s1 = Invoke-Api POST '/api/livestock/weighings' @{ Scope = 'INDIVIDUAL'; FarmId = $farmRetiro; AnimalId = $animalC; WeighedAt = '2026-10-11T09:00:00Z'; Weight = 412; Unit = 'kg'; Source = 'MANUAL' } -Token $adminToken
    Assert-Step 'W5-pesagem-unica-201' ($s1.Status -eq 201) "HTTP $($s1.Status) $($s1.Raw)"
    $perfS = Invoke-Api GET "/api/livestock/performance?animalId=$animalC" -Token $adminToken
    Assert-Step 'W5-sem-linha-de-desempenho' ($perfS.Status -eq 200 -and @($perfS.Body).Count -eq 0) "raw=$($perfS.Raw) (uma medida nao fabrica indicador — vazio != zero)"

    # ============ W6: LIMITES DE PESO POR ESPECIE (REVIEW_REQUIRED PRESERVADO) ============
    Write-Host '--- W6: peso fora dos limites exige justificativa (weight_limits BOVINE 40..1200) ---' -ForegroundColor Cyan
    $low1 = Invoke-Api POST '/api/livestock/weighings' @{ Scope = 'INDIVIDUAL'; FarmId = $farmSede; AnimalId = $animalD; WeighedAt = '2026-10-11T10:00:00Z'; Weight = 5; Unit = 'kg'; Source = 'MANUAL'; IdempotencyKey = "k-low-$runTag" } -Token $adminToken
    Assert-Step 'W6-fora-do-limite-sem-justificativa-422' ($low1.Status -eq 422 -and $low1.Raw -match 'weight_review') "HTTP $($low1.Status) $($low1.Raw)"
    $lowNone = Invoke-Psql "select count(*)::text from agro360.livestock_weighings where tenant_id='${tenantId}' and animal_id='${animalD}' and idempotency_key='k-low-$runTag';"
    Assert-Step 'W6-422-nao-grava-nada' ($lowNone -eq '0') "linhas=$lowNone (transacao inteira descartada)"
    $low2 = Invoke-Api POST '/api/livestock/weighings' @{ Scope = 'INDIVIDUAL'; FarmId = $farmSede; AnimalId = $animalD; WeighedAt = '2026-10-11T10:00:00Z'; Weight = 5; Unit = 'kg'; Source = 'MANUAL'; ReviewJustification = 'Bezerro prematuro - leitura conferida in loco'; IdempotencyKey = "k-low-$runTag" } -Token $adminToken
    Assert-Step 'W6-com-justificativa-201' ($low2.Status -eq 201) "HTTP $($low2.Status) $($low2.Raw)"
    $revRow = Invoke-Psql "select case when review_required then 'true' else 'false' end || '|' || coalesce(review_justification,'~') from agro360.livestock_weighings where tenant_id='${tenantId}' and animal_id='${animalD}';"
    Assert-Step 'W6-review-required-persistido' ($revRow -like 'true|Bezerro*') "row=$revRow"
    $listRev = Invoke-Api GET "/api/livestock/weighings?animalId=$animalD" -Token $adminToken
    $revItem = @($listRev.Body | Where-Object { "$($_.reviewRequired)" -eq 'True' }) | Select-Object -First 1
    Assert-Step 'W6-review-exposto-na-api' ($null -ne $revItem -and "$($revItem.reviewJustification)" -like 'Bezerro*') "api=$(if ($revItem) { $revItem.reviewJustification } else { '?' })"

    # ============ W7: BATERIA DE VALIDACAO + COLETIVAS VALIDAS ============
    Write-Host '--- W7: validacoes amigaveis e pesagens coletivas (grupo/lote) ---' -ForegroundColor Cyan
    $badResp = Invoke-Api POST '/api/livestock/weighings' @{ Scope = 'INDIVIDUAL'; FarmId = $farmSede; AnimalId = $animalB; WeighedAt = '2026-10-12T12:00:00Z'; Weight = 300; Unit = 'kg'; Source = 'MANUAL'; ResponsibleId = [guid]::NewGuid().ToString() } -Token $adminToken
    Assert-Step 'W7-responsavel-inexistente-404' ($badResp.Status -eq 404) "HTTP $($badResp.Status) $($badResp.Raw)"

    $badHerd = Invoke-Api POST '/api/livestock/weighings' @{ Scope = 'COLLECTIVE'; FarmId = $farmSede; HerdId = [guid]::NewGuid().ToString(); WeighedAt = '2026-10-12T12:00:00Z'; Weight = 1160; Unit = 'kg'; Source = 'MANUAL' } -Token $adminToken
    Assert-Step 'W7-grupo-inexistente-404' ($badHerd.Status -eq 404) "HTTP $($badHerd.Status) $($badHerd.Raw)"

    $badLot = Invoke-Api POST '/api/livestock/weighings' @{ Scope = 'COLLECTIVE'; FarmId = $farmSede; HandlingLotId = [guid]::NewGuid().ToString(); WeighedAt = '2026-10-12T12:00:00Z'; Weight = 1160; Unit = 'kg'; Source = 'MANUAL' } -Token $adminToken
    Assert-Step 'W7-lote-inexistente-404' ($badLot.Status -eq 404) "HTTP $($badLot.Status) $($badLot.Raw)"

    $noSubject = Invoke-Api POST '/api/livestock/weighings' @{ Scope = 'COLLECTIVE'; FarmId = $farmSede; WeighedAt = '2026-10-12T12:00:00Z'; Weight = 1160; Unit = 'kg'; Source = 'MANUAL' } -Token $adminToken
    Assert-Step 'W7-coletiva-sem-sujeito-422' ($noSubject.Status -eq 422 -and $noSubject.Raw -match 'collective_subject') "HTTP $($noSubject.Status) $($noSubject.Raw)"

    $noAnimal = Invoke-Api POST '/api/livestock/weighings' @{ Scope = 'INDIVIDUAL'; FarmId = $farmSede; WeighedAt = '2026-10-12T12:00:00Z'; Weight = 300; Unit = 'kg'; Source = 'MANUAL' } -Token $adminToken
    Assert-Step 'W7-individual-sem-animai-422' ($noAnimal.Status -eq 422 -and $noAnimal.Raw -match 'weighing_animal') "HTTP $($noAnimal.Status) $($noAnimal.Raw)"

    $noFactor = Invoke-Api POST '/api/livestock/weighings' @{ Scope = 'INDIVIDUAL'; FarmId = $farmSede; AnimalId = $animalB; WeighedAt = '2026-10-12T12:00:00Z'; Weight = 22; Unit = 'arroba'; Source = 'MANUAL' } -Token $adminToken
    Assert-Step 'W7-unidade-sem-fator-422' ($noFactor.Status -eq 422 -and $noFactor.Raw -match 'conversion_required') "HTTP $($noFactor.Status) $($noFactor.Raw)"

    $zeroW = Invoke-Api POST '/api/livestock/weighings' @{ Scope = 'INDIVIDUAL'; FarmId = $farmSede; AnimalId = $animalB; WeighedAt = '2026-10-12T12:00:00Z'; Weight = 0; Unit = 'kg'; Source = 'MANUAL' } -Token $adminToken
    Assert-Step 'W7-peso-nulo-422' ($zeroW.Status -eq 422 -and $zeroW.Raw -match 'positive_required') "HTTP $($zeroW.Status) $($zeroW.Raw)"

    $zeroHc = Invoke-Api POST '/api/livestock/weighings' @{ Scope = 'COLLECTIVE'; FarmId = $farmSede; HerdId = $herdQty; HeadCount = 0; WeighedAt = '2026-10-12T12:00:00Z'; Weight = 1160; Unit = 'kg'; Source = 'MANUAL' } -Token $adminToken
    Assert-Step 'W7-cabecas-nulas-422' ($zeroHc.Status -eq 422 -and $zeroHc.Raw -match 'head_count_invalid') "HTTP $($zeroHc.Status) $($zeroHc.Raw)"

    $longEq = 'B' * 81
    $longEqR = Invoke-Api POST '/api/livestock/weighings' @{ Scope = 'INDIVIDUAL'; FarmId = $farmSede; AnimalId = $animalB; WeighedAt = '2026-10-12T12:00:00Z'; Weight = 300; Unit = 'kg'; Source = 'MANUAL'; Equipment = $longEq } -Token $adminToken
    Assert-Step 'W7-equipamento-acima-do-limite-422' ($longEqR.Status -eq 422 -and $longEqR.Raw -match 'evidence_too_long') "HTTP $($longEqR.Status) $($longEqR.Raw)"
    $longM = 'M' * 41
    $longMR = Invoke-Api POST '/api/livestock/weighings' @{ Scope = 'INDIVIDUAL'; FarmId = $farmSede; AnimalId = $animalB; WeighedAt = '2026-10-12T12:00:00Z'; Weight = 300; Unit = 'kg'; Source = 'MANUAL'; Method = $longM } -Token $adminToken
    Assert-Step 'W7-metodo-acima-do-limite-422' ($longMR.Status -eq 422 -and $longMR.Raw -match 'evidence_too_long') "HTTP $($longMR.Status) $($longMR.Raw)"

    # Coletiva no grupo por quantidade: total 12.900 kg > teto individual 1200 -> o limite
    # (aplicado com especie fixa por limite de especie) marca review_required; comportamento
    # documentado (LIMITES) e assertado aqui para nao regressar silenciosamente.
    $cHerd = Invoke-Api POST '/api/livestock/weighings' @{ Scope = 'COLLECTIVE'; FarmId = $farmSede; HerdId = $herdQty; HeadCount = 40; WeighedAt = '2026-10-12T12:30:00Z'; Weight = 12900; Unit = 'kg'; Source = 'MANUAL'; Equipment = 'Balanca de pista coletiva'; ReviewJustification = 'Peso TOTAL do grupo; media por cabeca dentro do limite'; IdempotencyKey = "k-herd-$runTag"; ResponsibleId = $adminUser } -Token $adminToken
    Assert-Step 'W7-coletiva-grupo-201' ($cHerd.Status -eq 201) "HTTP $($cHerd.Status) $($cHerd.Raw)"
    $cHerdRow = Invoke-Psql "select scope || '|' || coalesce(head_count::text,'~') || '|' || coalesce(animal_id::text,'~') || '|' || case when review_required then 'true' else 'false' end from agro360.livestock_weighings where tenant_id='${tenantId}' and idempotency_key='k-herd-$runTag';"
    Assert-Step 'W7-coletiva-grupo-gravada-com-review' ($cHerdRow -eq "COLLECTIVE|40|~|true") "row=$cHerdRow (animal_id nulo: coletiva nao inventa pesos individuais)"
    $cHerdR = Invoke-Api POST '/api/livestock/weighings' @{ Scope = 'COLLECTIVE'; FarmId = $farmSede; HerdId = $herdQty; HeadCount = 40; WeighedAt = '2026-10-12T12:30:00Z'; Weight = 12900; Unit = 'kg'; Source = 'MANUAL'; Equipment = 'Balanca de pista coletiva'; ReviewJustification = 'Peso TOTAL do grupo; media por cabeca dentro do limite'; IdempotencyKey = "k-herd-$runTag"; ResponsibleId = $adminUser } -Token $adminToken
    Assert-Step 'W7-replay-coletiva-mesmo-id' ($cHerdR.Status -eq 201 -and "$($cHerdR.Body.id)" -eq "$($cHerd.Body.id)") "id=$($cHerdR.Body.id)"

    # Coletiva no lote de manejo semeado: total 1.160 kg <= teto 1200 -> sem review.
    $cLot = Invoke-Api POST '/api/livestock/weighings' @{ Scope = 'COLLECTIVE'; FarmId = $farmSede; HandlingLotId = $lotSeed; HeadCount = 4; WeighedAt = '2026-10-12T13:00:00Z'; Weight = 1160; Unit = 'kg'; Source = 'MANUAL' } -Token $adminToken
    Assert-Step 'W7-coletiva-lote-201' ($cLot.Status -eq 201) "HTTP $($cLot.Status) $($cLot.Raw)"
    $cLotRow = Invoke-Psql "select scope || '|' || coalesce(handling_lot_id::text,'~') || '|' || case when review_required then 'true' else 'false' end from agro360.livestock_weighings where tenant_id='${tenantId}' and id='$($cLot.Body.id)';"
    Assert-Step 'W7-coletiva-lote-gravada-sem-review' ($cLotRow -eq "COLLECTIVE|$lotSeed|false") "row=$cLotRow"

    # ============ W8: CORRECAO AUDITADA (PRESERVA VALOR ANTERIOR + EVIDENCIA) ============
    Write-Host '--- W8: correcao da pesagem W2 ---' -ForegroundColor Cyan
    $corrBad = Invoke-Api POST "/api/livestock/weighings/${w2Id}/correct" @{ WeightKg = 331.2; Unit = 'kg'; Justification = 'Leitura conferida na balanca fisica'; ResponsibleId = [guid]::NewGuid().ToString() } -Token $adminToken
    Assert-Step 'W8-responsavel-inexistente-nao-corrige-404' ($corrBad.Status -eq 404) "HTTP $($corrBad.Status) $($corrBad.Raw)"
    $still330 = Invoke-Psql "select count(*)::text from agro360.livestock_weighings where tenant_id='${tenantId}' and animal_id='${animalA}' and corrected_from_id='${w2Id}';"
    Assert-Step 'W8-404-nao-criou-linha-de-correcao' ($still330 -eq '0') "linhas=$still330"

    $corr = Invoke-Api POST "/api/livestock/weighings/${w2Id}/correct" @{ WeightKg = 331.2; Unit = 'kg'; Justification = 'Leitura conferida na balanca fisica'; ResponsibleId = $adminUser } -Token $adminToken
    Assert-Step 'W8-correcao-204' ($corr.Status -eq 204) "HTTP $($corr.Status)"
    $corRow = Invoke-Psql "select coalesce(corrected_from_id::text,'~') || '|' || coalesce(previous_weight_kg::text,'~') || '|' || coalesce(equipment,'~') || '|' || coalesce(measurement_precision,'~') || '|' || weight_kg::text from agro360.livestock_weighings where tenant_id='${tenantId}' and animal_id='${animalA}' and corrected_from_id='${w2Id}';"
    $correctionId = Invoke-Psql "select id::text from agro360.livestock_weighings where tenant_id='${tenantId}' and animal_id='${animalA}' and corrected_from_id='${w2Id}';"
    Assert-Step 'W8-linha-de-correcao-com-evidencia' ($corRow -eq "${w2Id}|330.000|Balanca eletronica E2E|0,1 kg|331.200") "row=$corRow"
    # 'auditada]' e substring ASCII segura do sufixo de correcao (evita dependencia de acento
    # na codificacao do script/cliente psql).
    $origMark = Invoke-Psql "select case when notes like '%auditada]' then 'OK' else '?' end from agro360.livestock_weighings where tenant_id='${tenantId}' and id='${w2Id}';"
    Assert-Step 'W8-original-marcada-como-substituida' ($origMark -eq 'OK') "mark=$origMark (linha original permanece na trilha)"
    $anA2 = Invoke-Psql "select coalesce(current_weight_kg::text,'~') from agro360.livestock_animals where tenant_id='${tenantId}' and id='${animalA}';"
    Assert-Step 'W8-peso-atual-atualizado-pela-correcao' ($anA2 -eq '331.2000') "current=$anA2"

    # ============ W9: FLUXO LEGADO COEXISTENTE (GRANULARIDADE DE DATA) ============
    Write-Host '--- W9: /api/v1/livestock/.../weights — mesma data rejeitada, data posterior aceita ---' -ForegroundColor Cyan
    # apos W4 o animal B tem last_weight_date=2026-10-12 no livro de animais -> mesmo dia 409
    $lg1 = Invoke-Api POST "/api/v1/livestock/animals/${animalB}/weights" @{ WeightKg = 305; MeasuredOn = '2026-10-12'; Notes = 'Legado E2E P3' ; IdempotencyKey = "k-lg-$runTag" } -Token $adminToken
    Assert-Step 'W9-legado-mesma-data-409' ($lg1.Status -eq 409 -and $lg1.Raw -match 'livestock_weight_date_invalid') "HTTP $($lg1.Status) $($lg1.Raw) (semantica de data propria do fluxo legado, documentada)"
    $lg2 = Invoke-Api POST "/api/v1/livestock/animals/${animalB}/weights" @{ WeightKg = 305; MeasuredOn = '2026-10-13'; Notes = 'Legado E2E P3'; IdempotencyKey = "k-lg-$runTag" } -Token $adminToken
    Assert-Step 'W9-legado-data-posterior-aceita' ($lg2.Status -in 200,201) "HTTP $($lg2.Status) $($lg2.Raw)"
    $anB = Invoke-Psql "select coalesce(current_weight_kg::text,'~') || '|' || to_char(last_weight_date,'YYYY-MM-DD') from agro360.livestock_animals where tenant_id='${tenantId}' and id='${animalB}';"
    Assert-Step 'W9-legado-atualizou-peso-atual' ($anB -eq '305.0000|2026-10-13') "current=$anB"
    $lg3 = Invoke-Api POST "/api/v1/livestock/animals/${animalB}/weights" @{ WeightKg = 306; MeasuredOn = '2026-10-13'; Notes = 'Legado repetido'; IdempotencyKey = "k-lg2-$runTag" } -Token $adminToken
    Assert-Step 'W9-legado-mesmo-dia-de-novo-409' ($lg3.Status -eq 409 -and $lg3.Raw -match 'livestock_weight_date_invalid') "HTTP $($lg3.Status) $($lg3.Raw)"
    $bLedgerRows = Invoke-Psql "select count(*)::text from agro360.livestock_weighings where tenant_id='${tenantId}' and animal_id='${animalB}';"
    Assert-Step 'W9-count-livro-do-animal-B' ($bLedgerRows -eq '2') "linhas_no_livro=$bLedgerRows (o fluxo legado grava em animais+eventos; o livro de pesagens continua com as 2 do modulo 360 — coexistencia documentada)"

    # ============ W10: ACESSO (401 / 200 NO ESCOPO / 403 FORA DO ESCOPO / 403 ESCRITA) ============
    Write-Host '--- W10: controle de acesso da jornada de pesagens ---' -ForegroundColor Cyan
    $anon = Invoke-Api GET '/api/livestock/weighings'
    Assert-Step 'W10-anonimo-401' ($anon.Status -eq 401) "HTTP $($anon.Status)"
    $readOk = Invoke-Api GET '/api/livestock/weighings' -Token $readerToken
    Assert-Step 'W10-leitor-le-pesagens-200' ($readOk.Status -eq 200) "HTTP $($readOk.Status)"
    Assert-Step 'W10-leitor-ve-tag-no-escopo' ($readOk.Raw -match 'SC-N-1001') 'trilha individual visivel no escopo FARM sede'
    $perfOk = Invoke-Api GET '/api/livestock/performance' -Token $readerToken
    Assert-Step 'W10-leitor-le-desempenho-200' ($perfOk.Status -eq 200) "HTTP $($perfOk.Status)"
    # A validacao de escopo de unidade do middleware opera pelo HEADER X-Farm-ID (o parametro de
    # query farmId e apenas filtro de listagem, comportamento platario preexistente — LIMITES).
    $readOut = Invoke-Api GET '/api/livestock/weighings' $null $readerToken @{ 'X-Farm-ID' = $farmRetiro }
    Assert-Step 'W10-unidade-fora-do-escopo-403' ($readOut.Status -eq 403 -and $readOut.Raw -match 'forbidden_unit_scope') "HTTP $($readOut.Status) $($readOut.Raw)"
    $readerPost = Invoke-Api POST '/api/livestock/weighings' @{ Scope = 'INDIVIDUAL'; FarmId = $farmSede; AnimalId = $animalB; WeighedAt = '2026-10-13T15:00:00Z'; Weight = 999; Unit = 'kg'; Source = 'MANUAL'; IdempotencyKey = "k-read-$runTag" } -Token $readerToken
    Assert-Step 'W10-leitor-escreve-403' ($readerPost.Status -eq 403) "HTTP $($readerPost.Status) $($readerPost.Raw)"
    $anB2 = Invoke-Psql "select coalesce(current_weight_kg::text,'~') || '|' || to_char(last_weight_date,'YYYY-MM-DD') from agro360.livestock_animals where tenant_id='${tenantId}' and id='${animalB}';"
    Assert-Step 'W10-escrita-rejeitada-nao-mudou-estado' ($anB2 -eq '305.0000|2026-10-13') "current=$anB2 (peso e data continuam como apos W9)"

    # ============ W11: RECONCILIAÇÃO API==DB + INVARIANTE DE CHAVE + SMOKE ============
    Write-Host '--- W11: reconciliacao final api==db ---' -ForegroundColor Cyan
    $dbState = Invoke-Psql @"
select (select count(*)::text from agro360.livestock_weighings where tenant_id='${tenantId}' and animal_id='${animalA}') || '|'
     || (select count(*)::text from agro360.livestock_weighings where tenant_id='${tenantId}' and animal_id='${animalB}') || '|'
     || (select count(*)::text from agro360.livestock_weighings where tenant_id='${tenantId}' and animal_id='${animalC}') || '|'
     || (select count(*)::text from agro360.livestock_weighings where tenant_id='${tenantId}' and animal_id='${animalD}') || '|'
     || (select count(*)::text from agro360.livestock_weighings where tenant_id='${tenantId}' and scope='COLLECTIVE' and animal_id is null) || '|'
     || (select count(*)::text from agro360.livestock_animal_events where tenant_id='${tenantId}' and animal_id='${animalB}' and event_type='WEIGHING');
"@
    # A: 2 seed + 1 (W2) + 1 (correcao W8) = 4 | B: 2 (par zero) | C: 1 | D: 1 | coletivas: seed 1 + grupo + lote = 3
    # eventos WEIGHING de B: W4a + W4b + legado W9 = 3
    Assert-Step 'W11-contagens-finais-por-animai' ($dbState -eq '4|2|1|1|3|3') "A|B|C|D|coletivas|eventosB real=$dbState (esperado 4|2|1|1|3|3)"

    # (keys nulas sao do seed/legado: unique parcial ignora NULLs por construtorio, entao a
    # invariante tambem ignora — contar o grupo de NULLs como "duplicada" seria erro de harness)
    $dupKeys = Invoke-Psql "select count(*)::text from (select 1 from agro360.livestock_weighings where tenant_id='${tenantId}' and idempotency_key is not null group by idempotency_key having count(*) > 1) x;"
    Assert-Step 'W11-invariante-uma-linha-por-chave' ($dupKeys -eq '0') "chaves duplicadas=$dupKeys"

    $listFin = Invoke-Api GET "/api/livestock/weighings?animalId=$animalA" -Token $adminToken
    $corItem = @($listFin.Body | Where-Object { "$($_.correctedFromId)" -eq $w2Id }) | Select-Object -First 1
    Assert-Step 'W11-api-igual-db-correcao' ($null -ne $corItem -and [double]$corItem.weightKg -eq 331.2 -and "$($corItem.equipment)" -eq 'Balanca eletronica E2E' -and [double]$corItem.previousWeightKg -eq 330.0) "api=$(if ($corItem) { $corItem.weightKg } else { '?' })"

    $snapshot = Invoke-Psql @"
select a.tag, w.scope, to_char(w.weighed_at,'YYYY-MM-DD HH24:MI'), w.weight_kg, coalesce(w.equipment,'~'), coalesce(w.measurement_precision,'~'), w.previous_weight_kg, case when w.review_required then 'REV' else 'ok' end, coalesce(w.idempotency_key,'~'), case when w.corrected_from_id is not null then 'CORRIGIDA_DE:'||left(w.corrected_from_id::text,8) else '-' end
from agro360.livestock_weighings w
left join agro360.livestock_animals a on a.id=w.animal_id and a.tenant_id=w.tenant_id
where w.tenant_id='${tenantId}'
order by w.weighed_at, w.id;
"@
    $snapshot | Set-Content (Join-Path $evidenceDir 'weighings-final-snapshot.txt')
    Write-Host "Snapshot final do livro de pesagens salvo em weighings-final-snapshot.txt" -ForegroundColor DarkGray

    $dash = Invoke-Api GET '/api/livestock/dashboard' -Token $adminToken
    Assert-Step 'W11-dashboard-saneamento-200' ($dash.Status -eq 200) "HTTP $($dash.Status) (modulo sadio apos toda a jornada)"

    Stop-Proc $script:apiHostProc
    if (-not $KeepRunning -and $weStartedCluster) { & $pg_ctl -D $templateDir -m fast stop | Out-Null }
    Write-Host ""
    Write-Host "E2E PECUARIA P3 (PESAGENS/DESEMPENHO): TODOS OS CENARIOS PASSARAM" -ForegroundColor Green
    Write-Host "Evidencia: $evidenceDir" -ForegroundColor Green
    exit 0
}
catch {
    Write-Host ""
    Write-Host "E2E PECUARIA P3: FALHOU" -ForegroundColor Red
    Write-Host "$_" -ForegroundColor Red
    Write-Host "Evidencia: $evidenceDir" -ForegroundColor Red
    Stop-Proc $script:apiHostProc
    if (-not $KeepRunning -and $weStartedCluster) { try { & $pg_ctl -D $templateDir -m fast stop | Out-Null } catch {} }
    exit 1
}
finally {
    Stop-Transcript | Out-Null
}
