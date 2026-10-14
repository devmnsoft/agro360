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
#   J bloco B         : venda do lote da safra via caminho canonico (politica de
#                      preco explicita, maquina DRAFT->REVIEW->APPROVED, reserva
#                      ->saida->aceite parcial), recebivel financeiro idempotente
#                      por aceite (sem duplicar no resend), composicao parcial
#                      preservada, resultado comercial bruto/retornos/liquido/
#                      margem reconciliado api==db, pagamento nao altera o
#                      reconhecimento.
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
$script:apiHostProc = $null
$script:apiRestartCount = 0

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

# A maquina soffre picos de pressao de RAM de outras apps; o OOM killer do SO ja matou o
# host da API silenciosamente no meio da corrida (log abrupto, sem WER) em execucoes 2 e 4/5.
# Recuperacao: se o pedido nunca chegou (conexao RECUSADA), reinicia o host e repete o pedido.
# O Postgres fica vivo durante a queda; tokens JWT sao stateless -> nada mais precisa re-logar.
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

function Invoke-Api([string]$Method, [string]$Path, $Body = $null, [string]$Token = '', [string]$FarmId = '', [string]$OrgId = '') {
    $url = "http://127.0.0.1:${apiPort}${Path}"
    $headers = @{ 'Content-Type' = 'application/json' }
    if ($Token) { $headers['Authorization'] = "Bearer $Token" }
    if ($FarmId) { $headers['X-Farm-ID'] = $FarmId }
    if ($OrgId) { $headers['X-Organization-ID'] = $OrgId }
    $params = @{ Method = $Method; Uri = $url; Headers = $headers; UseBasicParsing = $true; TimeoutSec = 60 }
    if ($null -ne $Body) { $json = $Body | ConvertTo-Json -Depth 8 -Compress; $params['Body'] = [Text.Encoding]::UTF8.GetBytes($json) }
    $triedRestart = $false
    while ($true) {
        try {
            $resp = Invoke-WebRequest @params
            $content = $resp.Content
            $result = $null
            if ("$($resp.Headers['Content-Type'])" -like '*json*' -and $content -and $content.Length -gt 1) { try { $result = $content | ConvertFrom-Json } catch { $result = $content } }
            return @{ Status = [int]$resp.StatusCode; Body = $result; Raw = $content }
        } catch {
            $errResp = $_.Exception.Response
            if ($null -ne $errResp) {
                $content = ''
                if ($_.ErrorDetails -and $_.ErrorDetails.Message) { $content = $_.ErrorDetails.Message }
                else { try { $stream = $errResp.GetResponseStream(); $reader = [IO.StreamReader]::new($stream); $content = $reader.ReadToEnd() } catch { $content = '' } }
                $result = if ($content -and $content.Length -gt 2) { try { $content | ConvertFrom-Json } catch { $content } } else { $null }
                return @{ Status = [int]$errResp.StatusCode; Body = $result; Raw = $content }
            }
            # sem resposta HTTP = transporte. So em conexao recusada (host morto) o pedido
            # certamente nao foi processado -> segura para reiniciar + repetir UMA vez.
            if ($triedRestart -or -not (Is-ConnRefused $_)) { throw "HTTP ${Method} ${Path}: $($_.Exception.Message)" }
            Restart-ApiHost ("conexao recusada em ${Method} ${Path}")
            $triedRestart = $true
        }
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
    $schemaCnt = Invoke-Psql "select count(*)::text from agro360.platform_schema_versions where version in ('11.25.0','137.0.0','138.0.0','139.0.0','140.0.0','141.0.0','142.0.0','143.0.0');"
    Require-Setup 'Schema-137-138-139-140-141-142-143-presentes' ($schemaCnt -eq '8') "versoes_presentes=$schemaCnt (esperado 8); se faltar, rode com -RebuildTemplate"
    # Caminho de UPGRADE em banco descartavel: as migrations incrementais 137+...+143 devem ser
    # idempotentes sobre um banco que ja veio atualizado pela full.sql (install limpo).
    $upg = $true; $upgErr = ''
    try {
        [void](Invoke-Psql (Get-Content (Join-Path $root 'database\migrations\137_cost_reversal_net_integrity.sql') -Raw))
        [void](Invoke-Psql (Get-Content (Join-Path $root 'database\migrations\138_receipt_item_work_order_material_fk.sql') -Raw))
        [void](Invoke-Psql (Get-Content (Join-Path $root 'database\migrations\139_fulfillment_acceptance_receivable_link.sql') -Raw))
        [void](Invoke-Psql (Get-Content (Join-Path $root 'database\migrations\140_livestock_identifier_history_backfill.sql') -Raw))
        [void](Invoke-Psql (Get-Content (Join-Path $root 'database\migrations\141_livestock_animal_movement_idempotency.sql') -Raw))
        [void](Invoke-Psql (Get-Content (Join-Path $root 'database\migrations\142_livestock_weighing_precision.sql') -Raw))
        [void](Invoke-Psql (Get-Content (Join-Path $root 'database\migrations\143_livestock_health_schedule.sql') -Raw))
    } catch { $upg = $false; $upgErr = "$_" }
    Require-Setup 'Migrations-137-a-143-idempotentes' $upg "upgrade sobre banco atualizado: $upgErr"

    # ===================== CONFIG E2E + START API =====================
    $connStr = "Host=127.0.0.1;Port=${pgPort};Database=${dbName};Username=postgres;Search Path=agro360;Command Timeout=60"
    $env:ConnectionStrings__Agro360 = $connStr
    $env:ConnectionStrings__DefaultConnection = $connStr
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:Jwt__SigningKey = 'e2e-test-signing-key-32bytes-minimum!'
    $env:PGPASSWORD = 'trust-auth-placeholder'
    # Memoria: em maquina com pouco RAM livre o OOM killer do SO matou o host da API no
    # meio da execucao (observado com ~0.5 GB livres, 2x seguidas, logs abruptos, sem WER).
    # Teto duro no heap do GC + GC conservador para o processo sobreviver a execucoes pesadas.
    $env:DOTNET_GCHeapHardLimit = '1400000000'
    $env:DOTNET_GCConserveMemory = '9'
    $apiPort = Get-FreePort
    $env:ASPNETCORE_URLS = "http://127.0.0.1:${apiPort}"
    $apiExe = Join-Path $root 'src\Hosts\Agro360.Api\bin\Release\net10.0\Agro360.Api.dll'
    if (-not (Test-Path $apiExe)) { throw "API binary nao encontrado: $apiExe (faça o build antes)" }
    $apiProc = Start-ApiHostProcess 'init'
    $script:apiHostProc = $apiProc
    $ready = $false
    for ($i = 0; $i -lt 60; $i++) { Start-Sleep -Milliseconds 500; try { $tcp = [Net.Sockets.TcpClient]::new(); $tcp.Connect('127.0.0.1', $apiPort); $tcp.Close(); $ready = $true; break } catch {}; if ($apiProc.HasExited) { break } }
    if (-not $ready) { $el = Join-Path $evidenceDir 'api-init.err.log'; if (Test-Path $el) { (Get-Content $el -Tail 40) | ForEach-Object { Write-Host "API-ERR: $_" -ForegroundColor Red } } }
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

    # ===================== G: CUSTO LIQUIDO (bruto/estornado/liquido + reconciliacao) =====================
    Write-Host '--- G: custo liquido (estorno parcial, total, reconciliacao api==db) ---' -ForegroundColor Cyan
    try {
        $woG = New-DirectWorkOrder 'PLANNED' 'COST' $farmA $fieldId $adminUser
        $addG = Invoke-Api POST "/api/agriculture/work-orders/${woG}/materials" @{ ProductId = $matP; WarehouseId = $matWhId; Unit = 'kg'; PlannedQuantity = 400; UnitCost = $unitCost } $adminToken
        Assert-Step 'G' 'material-adicionado' (($addG.Status -in 200,201,204)) "HTTP $($addG.Status) $($addG.Raw)"
        $midG = Invoke-Psql "select id::text from agro360.field_work_order_materials where tenant_id='${tenantId}' and work_order_id='${woG}' and product_id='${matP}';"
        Require-Setup 'G-material-id' ($midG -ne '') "material=$midG"
        [void](Invoke-Api POST "/api/agriculture/work-orders/${woG}/start" @{ Version = [long](Get-RecVersion $woG) } $adminToken)
        if ((Get-RecStatus $woG) -ne 'IN_PROGRESS') {
            [void](Invoke-Api POST "/api/agriculture/work-orders/${woG}/approve" @{ Version = [long](Get-RecVersion $woG) } $adminToken)
            [void](Invoke-Api POST "/api/agriculture/work-orders/${woG}/start" @{ Version = [long](Get-RecVersion $woG) } $adminToken)
        }
        Assert-Step 'G' 'ordem-em-execucao' ((Get-RecStatus $woG) -eq 'IN_PROGRESS') "status=$(Get-RecStatus $woG)"
        $rg = Invoke-Api POST "/api/agriculture/work-orders/${woG}/materials/${midG}/events" @{ EventType = 'RESERVE'; Quantity = 400; IdempotencyKey = "jrn-g-reserve-$runTag" } $adminToken
        $dg = Invoke-Api POST "/api/agriculture/work-orders/${woG}/materials/${midG}/events" @{ EventType = 'DELIVER'; Quantity = 400; IdempotencyKey = "jrn-g-deliver-$runTag" } $adminToken
        Assert-Step 'G' 'reserve-e-deliver' ($rg.Status -eq 204 -and $dg.Status -eq 204) "reserve=$($rg.Status) deliver=$($dg.Status) $($dg.Raw)"
        $cg = Invoke-Api POST "/api/agriculture/work-orders/${woG}/materials/${midG}/events" @{ EventType = 'CONSUME'; Quantity = 100; IdempotencyKey = "jrn-g-consume-$runTag" } $adminToken
        Assert-Step 'G' 'consumo-100' ($cg.Status -eq 204) "HTTP $($cg.Status) $($cg.Raw)"
        $consumeEventIdG = Invoke-Psql "select id::text from agro360.field_material_events where tenant_id='${tenantId}' and work_order_material_id='${midG}' and event_type='CONSUME' order by created_at desc limit 1;"
        $entryG = Invoke-Psql "select id::text from agro360.cost_management_entries where tenant_id='${tenantId}' and source_type='MATERIAL' and source_key='${consumeEventIdG}';"
        Require-Setup 'G-custo-criado' ($entryG -ne '') "entry=$entryG"
        $gVals = Invoke-Psql "select recognized_amount::text||'|'||reversed_amount::text from agro360.cost_management_entries where id='${entryG}';"
        Assert-Step 'G' 'bruto-1250-sem-estorno' ("$gVals" -eq '1250.0000|0.0000') "rec|rev=$gVals (esperado 1250.0000|0.0000)"
        # ESTORNO PARCIAL 40 (de 100 consumidos): estorno financeiro = 40 x 12.50 = 500; liquido = 750
        $revP = Invoke-Api POST "/api/agriculture/work-orders/${woG}/materials/${midG}/events" @{ EventType = 'REVERSAL'; Quantity = 40; SourceEventId = $consumeEventIdG; Reason = "Estorno parcial JRN ${runTag}"; IdempotencyKey = "jrn-g-rev1-$runTag" } $adminToken
        Assert-Step 'G' 'estorno-parcial-aceito' ($revP.Status -eq 204) "HTTP $($revP.Status) $($revP.Raw)"
        $rDb = Invoke-Psql "select reversed_amount::text||'|'||(recognized_amount-reversed_amount)::text from agro360.cost_management_entries where id='${entryG}';"
        Assert-Step 'G' 'db-estorno-500-liquido-750' ("$rDb" -eq '500.0000|750.0000') "rev|net=$rDb"
        $revPR = Invoke-Api POST "/api/agriculture/work-orders/${woG}/materials/${midG}/events" @{ EventType = 'REVERSAL'; Quantity = 40; SourceEventId = $consumeEventIdG; Reason = "Estorno parcial JRN ${runTag}"; IdempotencyKey = "jrn-g-rev1-$runTag" } $adminToken
        $rDb2 = Invoke-Psql "select reversed_amount::text from agro360.cost_management_entries where id='${entryG}';"
        Assert-Step 'G' 'replay-estorno-sem-duplicar' (($revPR.Status -in 200,204) -and "$rDb2" -eq '500.0000') "HTTP $($revPR.Status) rev=$rDb2"
        # estorno acima do saldo restante (restam 60) -> rejeitado
        $revOver = Invoke-Api POST "/api/agriculture/work-orders/${woG}/materials/${midG}/events" @{ EventType = 'REVERSAL'; Quantity = 61; SourceEventId = $consumeEventIdG; Reason = "Acima do saldo JRN ${runTag}"; IdempotencyKey = "jrn-g-revover-$runTag" } $adminToken
        Assert-Step 'G' 'estorno-acima-do-saldo-rejeitado' (Is-Err $revOver @(409) 'exceeds_remaining') "HTTP $($revOver.Status) $($revOver.Raw)"
        # DETALHE via API: linhas expoes bruto/estornado/liquido (memoria de calculo)
        $listG = Invoke-Api GET "/api/finance/season-costs?farmId=${farmA}&pageSize=200" -Token $adminToken
        $itemG = @($listG.Body.items) | Where-Object { "$($_.id)" -eq $entryG } | Select-Object -First 1
        Assert-Step 'G' 'detalhe-api-bruto-estornado-liquido' ($itemG -ne $null -and [decimal]$itemG.reversed_amount -eq [decimal]500 -and [decimal]$itemG.recognized_net -eq [decimal]750) "api rev=$($itemG.reversed_amount) net=$($itemG.recognized_net)"
        # OVERVIEW: resumo reconciliado com o banco (total do tenant)
        $ov = Invoke-Api GET '/api/finance/season-costs/overview' -Token $adminToken
        $ovDb = Invoke-Psql "select coalesce(sum(reversed_amount),0)::text||'|'||coalesce(sum(recognized_amount-reversed_amount),0)::text||'|'||coalesce(sum(recognized_amount-reversed_amount-allocated_amount),0)::text from agro360.cost_management_entries where tenant_id='${tenantId}' and deleted_at is null;"
        $ovOk = $ov.Body.summary -and [math]::Abs([decimal]$ov.Body.summary.reversed - [decimal]($ovDb.Split('|')[0])) -lt 0.0001 -and [math]::Abs([decimal]$ov.Body.summary.recognized_net - [decimal]($ovDb.Split('|')[1])) -lt 0.0001 -and [math]::Abs([decimal]$ov.Body.summary.pending_net - [decimal]($ovDb.Split('|')[2])) -lt 0.0001
        Assert-Step 'G' 'overview-reconciliado-com-db' ($ov.Status -eq 200 -and $ovOk) "api=$($ov.Body.summary.reversed)|$($ov.Body.summary.recognized_net)|$($ov.Body.summary.pending_net) db=$ovDb"
        # CSV exporta bruto/estornado/liquido por lancamento
        $csv = Invoke-Api GET '/api/finance/season-costs/export.csv' -Token $adminToken
        $csvOk = $csv.Status -eq 200 -and ("$($csv.Raw)" -like '*ReconhecidoBruto;Estornado;ReconhecidoLiquido;*') -and ("$($csv.Raw)" -like '*1250.0000;500.0000;750.0000;*')
        Assert-Step 'G' 'csv-com-colunas-liquidas' $csvOk "status=$($csv.Status) temColunas=$("$($csv.Raw)" -like '*ReconhecidoLiquido*') temLinha=$("$($csv.Raw)" -like '*1250.0000;500.0000;750.0000;*')"
        # FECHAMENTO (closing review): pendente agora e LIQUIDO, com memoria de calculo
        $cutoffG = (Get-Date).AddDays(1).ToUniversalTime().ToString('yyyy-MM-dd')
        $cr = Invoke-Api GET "/api/finance/season-costs/closing/${seasonId}?cutoffDate=${cutoffG}" -Token $adminToken
        $crDb = Invoke-Psql "select coalesce(sum(recognized_amount-reversed_amount-allocated_amount),0)::text||'|'||coalesce(sum(reversed_amount),0)::text||'|'||coalesce(sum(recognized_amount-allocated_amount),0)::text from agro360.cost_management_entries where tenant_id='${tenantId}' and farm_id='${farmA}' and status='OPEN' and deleted_at is null;"
        $crOk = $cr.Body.totals -and [math]::Abs([decimal]$cr.Body.totals.pending - [decimal]($crDb.Split('|')[0])) -lt 0.0001 -and [math]::Abs([decimal]$cr.Body.totals.reversed_open - [decimal]($crDb.Split('|')[1])) -lt 0.0001 -and [math]::Abs([decimal]$cr.Body.totals.pending_gross - [decimal]($crDb.Split('|')[2])) -lt 0.0001
        Assert-Step 'G' 'closing-pendente-liquido-reconciliado' ($cr.Status -eq 200 -and $crOk) "api=$($cr.Body.totals.pending_gross)/$($cr.Body.totals.reversed_open)/$($cr.Body.totals.pending) db=$crDb"
        # DASHBOARD: reconhecido liquido exposto junto com o bruto (bruto permanece para comparacao)
        $dashG = Invoke-Api GET '/api/agriculture/dashboard' -Token $adminToken
        $netDb = Invoke-Psql "select coalesce(sum(recognized_amount-reversed_amount),0)::text from agro360.cost_management_entries where tenant_id='${tenantId}';"
        $grossDb = Invoke-Psql "select coalesce(sum(recognized_amount),0)::text from agro360.cost_management_entries where tenant_id='${tenantId}';"
        $dashGOk = $dashG.Body.recognizedNetCost -ne $null -and $dashG.Body.recognizedCost -ne $null -and [math]::Abs([decimal]$dashG.Body.recognizedNetCost - [decimal]$netDb) -lt 0.0001 -and [math]::Abs([decimal]$dashG.Body.recognizedCost - [decimal]$grossDb) -lt 0.0001
        Assert-Step 'G' 'dashboard-bruto-e-liquido-reconciliados' ($dashG.Status -eq 200 -and $dashGOk) "api gross=$($dashG.Body.recognizedCost) net=$($dashG.Body.recognizedNetCost) db=$grossDb/$netDb"
        # ESTORNO do restante (60) -> total: liquido 0 e o lancamento sai da central (COST_PENDING)
        $revF = Invoke-Api POST "/api/agriculture/work-orders/${woG}/materials/${midG}/events" @{ EventType = 'REVERSAL'; Quantity = 60; SourceEventId = $consumeEventIdG; Reason = "Estorno restante JRN ${runTag}"; IdempotencyKey = "jrn-g-rev2-$runTag" } $adminToken
        $rF = Invoke-Psql "select reversed_amount::text||'|'||(recognized_amount-reversed_amount)::text from agro360.cost_management_entries where id='${entryG}';"
        Assert-Step 'G' 'estorno-total-liquido-zero' ($revF.Status -eq 204 -and "$rF" -eq '1250.0000|0.0000') "rev|net=$rF"
        $ocG = Invoke-Api GET '/api/operation-center?page=1&pageSize=200' -Token $adminToken
        $costKeysG = @($ocG.Body.items | ForEach-Object { "$($_.key)" })
        Assert-Step 'G' 'custo-liquido-zero-sai-da-central' ($ocG.Status -eq 200 -and -not ($costKeysG -contains "COST_PENDING:$entryG")) "COST_PENDING na lista=$(if ($costKeysG -contains "COST_PENDING:$entryG") { 'sim' } else { 'nao' })"
        $revDup = Invoke-Api POST "/api/agriculture/work-orders/${woG}/materials/${midG}/events" @{ EventType = 'REVERSAL'; Quantity = 1; SourceEventId = $consumeEventIdG; Reason = "Pos-liquido-zero JRN ${runTag}"; IdempotencyKey = "jrn-g-rev3-$runTag" } $adminToken
        Assert-Step 'G' 'estorno-pos-liquido-zero-rejeitado' (Is-Err $revDup @(409) 'reversal_duplicate') "HTTP $($revDup.Status) $($revDup.Raw)"
    } catch { Note-Blocked 'G' 'secao' $_ }

    # ===================== H: CENTRAL AGRICOLA (custodia, responsavel, inspecao) =====================
    Write-Host '--- H: central de trabalho agricola (origem, causa, revalidacao, escopo) ---' -ForegroundColor Cyan
    try {
        # --- H1 CUSTODY: ordem com material entregue e sem baixa completa
        $woH1 = New-DirectWorkOrder 'PLANNED' 'CUSTODY' $farmA $fieldId $adminUser
        $addH = Invoke-Api POST "/api/agriculture/work-orders/${woH1}/materials" @{ ProductId = $matP; WarehouseId = $matWhId; Unit = 'kg'; PlannedQuantity = 100; UnitCost = $unitCost } $adminToken
        Assert-Step 'H' 'h1-material-adicionado' (($addH.Status -in 200,201,204)) "HTTP $($addH.Status) $($addH.Raw)"
        $midH1 = Invoke-Psql "select id::text from agro360.field_work_order_materials where tenant_id='${tenantId}' and work_order_id='${woH1}' and product_id='${matP}';"
        Require-Setup 'H1-material-id' ($midH1 -ne '') "material=$midH1"
        [void](Invoke-Api POST "/api/agriculture/work-orders/${woH1}/start" @{ Version = [long](Get-RecVersion $woH1) } $adminToken)
        if ((Get-RecStatus $woH1) -ne 'IN_PROGRESS') {
            [void](Invoke-Api POST "/api/agriculture/work-orders/${woH1}/approve" @{ Version = [long](Get-RecVersion $woH1) } $adminToken)
            [void](Invoke-Api POST "/api/agriculture/work-orders/${woH1}/start" @{ Version = [long](Get-RecVersion $woH1) } $adminToken)
        }
        $rh1a = Invoke-Api POST "/api/agriculture/work-orders/${woH1}/materials/${midH1}/events" @{ EventType = 'RESERVE'; Quantity = 100; IdempotencyKey = "jrn-h1-reserve-$runTag" } $adminToken
        $rh1b = Invoke-Api POST "/api/agriculture/work-orders/${woH1}/materials/${midH1}/events" @{ EventType = 'DELIVER'; Quantity = 100; IdempotencyKey = "jrn-h1-deliver-$runTag" } $adminToken
        $rh1c = Invoke-Api POST "/api/agriculture/work-orders/${woH1}/materials/${midH1}/events" @{ EventType = 'CONSUME'; Quantity = 30; IdempotencyKey = "jrn-h1-consume-$runTag" } $adminToken
        Assert-Step 'H' 'h1-entregue-parte-consumida' ($rh1a.Status -eq 204 -and $rh1b.Status -eq 204 -and $rh1c.Status -eq 204) "reserve=$($rh1a.Status) deliver=$($rh1b.Status) consume=$($rh1c.Status)"
        $h1key = "CUSTODY_PENDING:$woH1"
        $oc1 = Invoke-Api GET '/api/operation-center?page=1&pageSize=200' -Token $adminToken
        $custItem = @($oc1.Body.items) | Where-Object { "$($_.key)" -eq $h1key } | Select-Object -First 1
        Assert-Step 'H' 'custodia-aparece-na-central' ($oc1.Status -eq 200 -and $custItem -ne $null) "titulo=$($custItem.title) causa=$($custItem.reason) proxima=$($custItem.action_label)"
        # isolamento entre tenants: vale nao ve nada de santa
        $ocV = Invoke-Api GET '/api/operation-center?page=1&pageSize=200' -Token $valeToken
        $valeKeys = @($ocV.Body.items | ForEach-Object { "$($_.key)" })
        Assert-Step 'H' 'vale-nao-ve-custodia-santa' (-not ($valeKeys -contains $h1key)) "valeItems=$($valeKeys.Count)"
        # escopo por fazenda: forca scope FARM(farmA) no operador; X-Farm-Id=farmB -> 403; farmA -> 200
        Invoke-Psql "delete from agro360.identity_user_unit_scopes where tenant_id='${tenantId}' and user_id='${operatorUser}' and scope_type='ALL';" | Out-Null
        Invoke-Psql "delete from agro360.identity_user_unit_scopes where tenant_id='${tenantId}' and user_id='${operatorUser}' and scope_type='FARM' and farm_id is distinct from '${farmA}';" | Out-Null
        if (-not (Invoke-Psql "select exists(select 1 from agro360.identity_user_unit_scopes where tenant_id='${tenantId}' and user_id='${operatorUser}' and scope_type='FARM' and farm_id='${farmA}')::text;") -eq 't') {
            Invoke-Psql "insert into agro360.identity_user_unit_scopes(id,tenant_id,user_id,scope_type,farm_id,created_at) values('$(New-Guid)','${tenantId}','${operatorUser}','FARM','${farmA}',now());" | Out-Null
        }
        # A central exige work.read (politica do endpoint). O operador do fixture nao tem essa
        # permissao no papel; concede via role_permissions e re-logina (o JWT embute as
        # permissoes; a concessao so vale a partir de um novo login).
        [void](Invoke-Psql "insert into agro360.identity_role_permissions(tenant_id,role_id,permission_id) select ur.tenant_id,ur.role_id,p.id from agro360.identity_user_roles ur cross join agro360.identity_permissions p where ur.tenant_id='${tenantId}' and ur.user_id='${operatorUser}' and p.code='work.read' on conflict do nothing;")
        $opReLogin = Invoke-Api POST '/api/v1/auth/login' @{ TenantSlug = 'santa-clara'; Email = 'operador.santaclara@agro360.local'; Password = 'Agro360Admin@2024!' }
        if ($opReLogin.Status -eq 200) { $operatorToken = $opReLogin.Body.accessToken }
        $opOut = Invoke-Api GET '/api/operation-center?page=1&pageSize=10' -Token $operatorToken -FarmId $farmB
        $opIn = Invoke-Api GET '/api/operation-center?page=1&pageSize=10' -Token $operatorToken -FarmId $farmA
        Assert-Step 'H' 'escopo-farm-nao-alarga-na-central' ($opOut.Status -eq 403 -and $opIn.Status -eq 200) "farmB=$($opOut.Status) farmA=$($opIn.Status)"
        Invoke-Psql "insert into agro360.identity_user_unit_scopes(id,tenant_id,user_id,scope_type,created_at) values('$(New-Guid)','${tenantId}','${operatorUser}','ALL',now());" | Out-Null
        # atribuir na central != aprovar a origem: ordem segue em execucao; evento ASSIGNED registrado
        $asg = Invoke-Api PUT "/api/operation-center/${h1key}/assignment" @{ ResponsibleId = $operatorUser; Reason = "Acompanhar custodias JRN ${runTag}"; ExpectedVersion = 0 } $adminToken
        Assert-Step 'H' 'atribuicao-na-central-aceita' ($asg.Status -eq 204) "HTTP $($asg.Status) $($asg.Raw)"
        $asgEvents = Invoke-Psql "select count(*)::text from agro360.operation_occurrence_events where occurrence_key='${h1key}' and event_type='ASSIGNED';"
        Assert-Step 'H' 'atribuicao-registrada-em-eventos' ([int]$asgEvents -ge 1) "eventos_ASSIGNED=$asgEvents (a origem NAO muda de status)"
        [void](Invoke-Api POST "/api/operation-center/${h1key}/view" $null $adminToken)
        [void](Invoke-Api POST "/api/operation-center/${h1key}/view" $null $adminToken)
        $viewEvents = Invoke-Psql "select count(*)::text from agro360.operation_occurrence_events where occurrence_key='${h1key}' and event_type='VIEWED' and actor_id='${adminUser}';"
        Assert-Step 'H' 'reenvio-view-sem-evento-duplicado' ([int]$viewEvents -le 1) "eventos_VIEWED=$viewEvents"
        # resolver a origem (baixa do restante) -> ocorrencia sai da central
        $rh1d = Invoke-Api POST "/api/agriculture/work-orders/${woH1}/materials/${midH1}/events" @{ EventType = 'CONSUME'; Quantity = 70; IdempotencyKey = "jrn-h1-consume2-$runTag" } $adminToken
        $oc2 = Invoke-Api GET '/api/operation-center?page=1&pageSize=200' -Token $adminToken
        $stillH1 = @($oc2.Body.items) | Where-Object { "$($_.key)" -eq $h1key }
        Assert-Step 'H' 'resolver-origem-tira-da-central' ($rh1d.Status -eq 204 -and $stillH1.Count -eq 0) "consume2=$($rh1d.Status) aindaPresente=$($stillH1.Count)"

        # --- H2 RESOURCE_UNASSIGNED: ordem planejada sem responsavel
        $woH2 = [guid]::NewGuid().ToString()
        $pl2 = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        [void](Invoke-Psql @"
insert into agro360.agriculture_records(id,tenant_id,module,status,data,created_by,version)
values('${woH2}','${tenantId}','work-orders','PLANNED',
 '{"number":"OC-${runTag}SEMRESP","name":"WO SemResponsavel","propertyId":"${farmA}","fieldId":"${fieldId}","responsibleId":"","plannedAt":"${pl2}","estimatedCost":500,"unit":"ha","origin":"E2E"}'::jsonb,
 '${adminUser}',1);
"@)
        $h2key = "RESOURCE_UNASSIGNED:$woH2"
        $oc3 = Invoke-Api GET '/api/operation-center?page=1&pageSize=200' -Token $adminToken
        $unItem = @($oc3.Body.items) | Where-Object { "$($_.key)" -eq $h2key } | Select-Object -First 1
        Assert-Step 'H' 'sem-responsavel-aparece-na-central' ($oc3.Status -eq 200 -and $unItem -ne $null) "titulo=$($unItem.title) causa=$($unItem.reason)"
        $asg2 = Invoke-Api PUT "/api/operation-center/${h2key}/assignment" @{ ResponsibleId = $operatorUser; Reason = "Acompanhar designacao JRN ${runTag}"; ExpectedVersion = 0 } $adminToken
        $h2Status = Get-RecStatus $woH2
        Assert-Step 'H' 'atribuir-nao-aprova-ordem' ($asg2.Status -eq 204 -and $h2Status -eq 'PLANNED') "assignment=$($asg2.Status) statusOrdem=$h2Status"
        [void](Invoke-Psql "update agro360.agriculture_records set data=jsonb_set(data,'{responsibleId}',to_jsonb('${adminUser}'::text)),updated_at=now() where id='${woH2}' and tenant_id='${tenantId}';")
        $oc4 = Invoke-Api GET '/api/operation-center?page=1&pageSize=200' -Token $adminToken
        $stillH2 = @($oc4.Body.items) | Where-Object { "$($_.key)" -eq $h2key }
        Assert-Step 'H' 'designar-responsavel-tira-da-central' ($stillH2.Count -eq 0) "aindaPresente=$($stillH2.Count)"
        $valeHasH2 = @(@($ocV.Body.items) | ForEach-Object { "$($_.key)" }) -contains $h2key
        Assert-Step 'H' 'vale-nao-ve-sem-responsavel-santa' (-not $valeHasH2) "valeVeu=$valeHasH2"

        # --- H3 INSPECTION_PENDING: dead-letter deterministica + re-disparo auditado
        $intentH = ''
        if ($rcptId) {
            $intentH = Invoke-Psql "select i.id::text from agro360.quality_inspection_event_intents i where i.tenant_id='${tenantId}' and i.process_code='HARVEST_RECEIPT' and i.origin_id='${rcptId}' order by i.created_at desc limit 1;"
        }
        if ($intentH -eq '') {
            $intentH = Invoke-Psql "select i.id::text from agro360.quality_inspection_event_intents i where i.tenant_id='${tenantId}' and i.process_code='HARVEST_RECEIPT' order by i.created_at desc limit 1;"
        }
        Require-Setup 'H3-intent-existe' ($intentH -ne '') "nenhum intent HARVEST_RECEIPT para marcar dead-letter"
        $intentCountBefore = Invoke-Psql "select count(*)::text from agro360.quality_inspection_event_intents where tenant_id='${tenantId}' and origin_type='PRODUCTION_RECEIPT' or (tenant_id='${tenantId}' and id='${intentH}');"
        $intentCountBefore = Invoke-Psql "select count(*)::text from agro360.quality_inspection_event_intents i join agro360.production_receipts r on r.id=i.origin_id and r.tenant_id=i.tenant_id where i.tenant_id='${tenantId}' and i.origin_id=(select origin_id from agro360.quality_inspection_event_intents where id='${intentH}');"
        # next_attempt_at permanece como esta: a coluna e NOT NULL e o processor da app nunca
        # a zera em dead-letter (mantem o ultimo agendamento; o claim so pega PENDING/PENDING_MODEL).
        [void](Invoke-Psql "update agro360.quality_inspection_event_intents set status='DEAD_LETTERED', error_code='E2E_SIMULATED_PERMANENT', attempts=10, dead_lettered_at=now(), claimed_at=null, claimed_by=null, lease_expires_at=null, updated_at=now() where id='${intentH}' and tenant_id='${tenantId}';")
        $h3key = "INSPECTION_PENDING:$intentH"
        $oc5 = Invoke-Api GET '/api/operation-center?page=1&pageSize=200' -Token $adminToken
        $dlItem = @($oc5.Body.items) | Where-Object { "$($_.key)" -eq $h3key } | Select-Object -First 1
        Assert-Step 'H' 'deadletter-aparece-na-central' ($oc5.Status -eq 200 -and $dlItem -ne $null) "titulo=$($dlItem.title) prioridade=$($dlItem.priority) causa=$($dlItem.reason)"
        # operario sem compliance.* nao acessa a interface de intents
        $opList = Invoke-Api GET '/api/inspections/event-intents?limit=10' -Token $operatorToken
        Assert-Step 'H' 'operador-sem-compliance-nao-ve-intents' ($opList.Status -eq 403) "HTTP $($opList.Status)"
        # re-disparo exige motivo (auditoria)
        $rdNoReason = Invoke-Api POST "/api/inspections/event-intents/${intentH}/redispatch" @{} $adminToken
        Assert-Step 'H' 'redispatch-sem-motivo-422' (Is-Err $rdNoReason @(422) 'redispatch_reason_required') "HTTP $($rdNoReason.Status) $($rdNoReason.Raw)"
        # re-disparo valido: processa na hora, nao duplica intent, auditado
        $rd = Invoke-Api POST "/api/inspections/event-intents/${intentH}/redispatch" @{ Reason = "E2E H: reprocesso apos verificacao da origem ${runTag}" } $adminToken
        Assert-Step 'H' 'redispatch-com-motivo-aceito' ($rd.Status -eq 200) "HTTP $($rd.Status) $($rd.Raw)"
        $intentCountAfter = Invoke-Psql "select count(*)::text from agro360.quality_inspection_event_intents i join agro360.production_receipts r on r.id=i.origin_id and r.tenant_id=i.tenant_id where i.tenant_id='${tenantId}' and i.origin_id=(select origin_id from agro360.quality_inspection_event_intents where id='${intentH}');"
        Assert-Step 'H' 'redispatch-nao-duplica-intent' ("$intentCountBefore" -eq "$intentCountAfter") "antes=$intentCountBefore depois=$intentCountAfter"
        $auditN = Invoke-Psql "select count(*)::text from agro360.audit_logs where tenant_id='${tenantId}' and action='INSPECTION_INTENT_REDISPATCH' and entity_id='${intentH}';"
        Assert-Step 'H' 'redispatch-auditado-com-ator' ([int]$auditN -ge 1) "audits=$auditN"
        # lista expoe situacao derivada e elegibilidade; estado avancou de DEAD_LETTERED
        $li = Invoke-Api GET '/api/inspections/event-intents?limit=200' -Token $adminToken
        $it = @($li.Body) | Where-Object { "$($_.id)" -eq $intentH } | Select-Object -First 1
        $itFinal = Invoke-Psql "select status from agro360.quality_inspection_event_intents where id='${intentH}';"
        $liOk = $it -ne $null -and "$($it.situation)" -ne '' -and "$($it.status)" -ne 'DEAD_LETTERED' -and "$($it.status)" -eq "$itFinal"
        Assert-Step 'H' 'lista-com-situacao-consistente' ($liOk) "situacao=$($it.situation) statusApi=$($it.status) statusDb=$itFinal canRedispatch=$($it.canRedispatch)"
        # central reflete o estado final: presente so se o intent segue em estado pendente
        $oc6 = Invoke-Api GET '/api/operation-center?page=1&pageSize=200' -Token $adminToken
        $stillH3 = @($oc6.Body.items) | Where-Object { "$($_.key)" -eq $h3key }
        $expectH3 = @('PENDING','PENDING_MODEL','DEAD_LETTERED') -contains "$itFinal"
        Assert-Step 'H' 'central-reflete-estado-final' (([int]$stillH3.Count -gt 0) -eq $expectH3) "statusFinal=$itFinal esperadoNaCentral=$expectH3 presente=$($stillH3.Count)"
    } catch { Note-Blocked 'H' 'secao' $_ }

    # ===================== I: GOVERNANCA DE PAPEIS X CONTRATO (módulo não contratado / suspenso) =====================
    Write-Host '--- I: perfis x contrato de modulo (salvar papel, suspender, acesso efetivo) ---' -ForegroundColor Cyan
    try {
        $contractedRaw = Invoke-Psql @"
select string_agg(distinct lower(contracted_modules.code),',') from (
  select c.code from agro360.platform_tenant_module_entitlements e
    join agro360.platform_module_catalog c on c.id=e.module_id
   where e.tenant_id='${tenantId}' and e.status in ('CONTRACTED','ACTIVE','TRIAL')
     and (e.activated_at is null or e.activated_at<=now()) and (e.valid_until is null or e.valid_until>now())
  union
  select m.code from agro360.platform_tenant_modules tm
    join agro360.platform_marketplace_modules m on m.id=tm.module_id
   where tm.tenant_id='${tenantId}' and tm.status='ACTIVE' and (tm.trial_ends_at is null or tm.trial_ends_at>now())
) contracted_modules
where not exists(select 1 from agro360.platform_tenant_module_entitlements be join agro360.platform_module_catalog bc on bc.id=be.module_id
   where be.tenant_id='${tenantId}' and lower(bc.code)=lower(contracted_modules.code) and be.status in ('BLOCKED','SUSPENDED','DELINQUENT','INACTIVE'))
  and not exists(select 1 from agro360.platform_tenant_modules btm join agro360.platform_marketplace_modules bm on bm.id=btm.module_id
   where btm.tenant_id='${tenantId}' and lower(bm.code)=lower(contracted_modules.code) and btm.status in ('BLOCKED','SUSPENDED','DELINQUENT','INACTIVE'));
"@
        $contractedMods = @(($contractedRaw -split ',') | Where-Object { $_ })
        $adminPermsRaw = Invoke-Psql "select string_agg(distinct p.code,',') from agro360.identity_user_roles ur join agro360.identity_role_permissions rp on rp.tenant_id=ur.tenant_id and rp.role_id=ur.role_id join agro360.identity_permissions p on p.id=rp.permission_id where ur.tenant_id='${tenantId}' and ur.user_id='${adminUser}';"
        $adminPerms = @(($adminPermsRaw -split ',') | Where-Object { $_ })
        $candidates = @('livestock','cooperative','fleet','storage','logistics','documents','mobile','intelligence','esg','sustainability','maps','production','purchasing')
        $uncontractedMod = ''; foreach ($m in $candidates) { if (($contractedMods -notcontains $m) -and ($adminPerms -contains "${m}.read")) { $uncontractedMod = $m; break } }
        Require-Setup 'I-modulo-sem-contrato-disponivel' ($uncontractedMod -ne '') "contratados=[$($contractedMods -join ',')] nenhum candidato fora do contrato"
        # salvar papel com permissao de modulo NAO contratado -> negado
        $rUn = Invoke-Api POST '/api/roles' @{ name = "JRN SemContrato ${runTag}"; level = 10; permissions = @("${uncontractedMod}.read") } $adminToken
        Assert-Step 'I' 'papel-modulo-sem-contrato-negado' (Is-Err $rUn @(403) 'acima do contratado') "modulo=$uncontractedMod HTTP $($rUn.Status) $($rUn.Raw)"
        # controle positivo: modulo contratado salva OK (limpo apos)
        $okMod = ''; foreach ($m in $contractedMods) { if ($adminPerms -contains "${m}.read") { $okMod = $m; break } }
        if ($okMod) {
            $rOk = Invoke-Api POST '/api/roles' @{ name = "JRN Contratado ${runTag}"; level = 10; permissions = @("${okMod}.read") } $adminToken
            Assert-Step 'I' 'papel-modulo-contratado-ok' ($rOk.Status -eq 201) "modulo=$okMod HTTP $($rOk.Status) $($rOk.Raw)"
            [void](Invoke-Psql "delete from agro360.identity_role_permissions where tenant_id='${tenantId}' and role_id='$($rOk.Body.id)'; delete from agro360.saas_role_metadata where tenant_id='${tenantId}' and role_id='$($rOk.Body.id)'; delete from agro360.identity_roles where tenant_id='${tenantId}' and id='$($rOk.Body.id)';")
        } else { Note-Blocked 'I' 'papel-modulo-contratado-ok' 'nenhum modulo contratado com permissao .read do admin' }
        # SUSPENSAO: perfis preservados (nao sao destruidos), gravacao negada com motivo
        # distinto, acesso efetivo negado pelo gate de runtime, e tudo volta ao reativar.
        $agRow = Invoke-Psql "select e.status from agro360.platform_tenant_module_entitlements e join agro360.platform_module_catalog c on c.id=e.module_id where e.tenant_id='${tenantId}' and lower(c.code)='agriculture' limit 1;"
        if ($agRow -ne '') {
            [void](Invoke-Psql "update agro360.platform_tenant_module_entitlements e set status='SUSPENDED' where e.tenant_id='${tenantId}' and e.module_id in (select id from agro360.platform_module_catalog where code='agriculture');")
            $rolesS = Invoke-Api GET '/api/roles' -Token $adminToken
            Assert-Step 'I' 'perfis-preservados-apos-suspensao' ($rolesS.Status -eq 200 -and @($rolesS.Body).Count -gt 0) "perfis=$(@($rolesS.Body).Count) (nada foi destruido)"
            $rSusp = Invoke-Api POST '/api/roles' @{ name = "JRN Suspenso ${runTag}"; level = 10; permissions = @('agriculture.read') } $adminToken
            Assert-Step 'I' 'papel-modulo-suspenso-negado-mensagem-distinta' (Is-Err $rSusp @(403) 'suspenso') "HTTP $($rSusp.Status) $($rSusp.Raw)"
            $accDenied = Invoke-Api GET '/api/v1/agriculture/seasons?page=1&pageSize=1' -Token $adminToken
            Assert-Step 'I' 'acesso-efetivo-negado-com- modulo-suspenso' ($accDenied.Status -eq 403) "HTTP $($accDenied.Status) (gate de runtime condiciona ao contrato)"
            [void](Invoke-Psql "update agro360.platform_tenant_module_entitlements e set status='${agRow}' where e.tenant_id='${tenantId}' and e.module_id in (select id from agro360.platform_module_catalog where code='agriculture');")
            $rAgain = Invoke-Api POST '/api/roles' @{ name = "JRN Reativado ${runTag}"; level = 10; permissions = @('agriculture.read') } $adminToken
            $accBack = Invoke-Api GET '/api/v1/agriculture/seasons?page=1&pageSize=1' -Token $adminToken
            Assert-Step 'I' 'reativacao-libera-gravacao-e-acesso' ($rAgain.Status -eq 201 -and $accBack.Status -eq 200) "role=$($rAgain.Status) access=$($accBack.Status)"
            [void](Invoke-Psql "delete from agro360.identity_role_permissions where tenant_id='${tenantId}' and role_id='$($rAgain.Body.id)'; delete from agro360.saas_role_metadata where tenant_id='${tenantId}' and role_id='$($rAgain.Body.id)'; delete from agro360.identity_roles where tenant_id='${tenantId}' and id='$($rAgain.Body.id)';")
        } else { Note-Blocked 'I' 'ciclo-suspensao-agriculture' 'entitlement agriculture ausente no fixture' }
    } catch { Note-Blocked 'I' 'secao' $_ }

    # ===================== J: BLOCO B (lote disponivel da safra -> venda + resultado comercial) =====================
    Write-Host '--- J: bloco B - venda do lote da safra + resultado comercial ---' -ForegroundColor Cyan
    try {
        # fixtures: segmento comercial (catalogo sem escritor C#; fixture deterministica por tenant) + conta de receita
        $segJ = New-Guid
        [void](Invoke-Psql @"
insert into agro360.crm_customer_segments(id,tenant_id,name,status,created_by)
values('$segJ','${tenantId}','Segmento Journey J','ACTIVE','${adminUser}')
on conflict (tenant_id,name) do nothing;
"@)
        $accJ = Invoke-Api POST '/api/finance/chart-of-accounts' @{ Code = "REV-J-${runTag}"; Name = 'Receita de vendas J'; Type = 'REVENUE'; Nature = 'CREDIT' } $adminToken
        Assert-Step 'J' 'conta-receita-criada' ($accJ.Status -eq 201) "HTTP $($accJ.Status) $($accJ.Raw)"
        $custJ = Invoke-Api POST '/api/commercial/customers' @{ Name = "Cliente Jornada J ${runTag}"; SegmentId = $segJ; Type = 'CUSTOMER' } $adminToken
        Assert-Step 'J' 'cliente-criado' ($custJ.Status -eq 201) "HTTP $($custJ.Status) $($custJ.Raw)"
        $custJId = "$($custJ.Body.id)"
        Require-Setup 'J-cliente-id' ($custJId -ne '' -and $custJId -ne 'System.Reflection.PropertyInfo') "cust=$custJId"
        [void](Invoke-Psql "update agro360.crm_customers set status='ACTIVE' where tenant_id='${tenantId}' and id='${custJId}';")
        $custOp = Invoke-Api POST '/api/commercial/customers' @{ Name = "Cliente Negado J ${runTag}"; SegmentId = $segJ; Type = 'CUSTOMER' } $operatorToken
        Assert-Step 'J' 'cadastro-sem-comercial-write-negado' ($custOp.Status -eq 403) "HTTP $($custOp.Status) $($custOp.Raw)"
        # criterio de preco explicito: sem politica de preco ativa para o segmento/produto, o pedido e rejeitado
        $ordBody = @{ CustomerId = $custJId; Items = @(@{ ProductId = $grainP; Quantity = 30000; Unit = 'KG'; UnitPrice = 1.1; DiscountPercentage = 0 }); Freight = 0 }
        $ordNoPol = Invoke-Api POST '/api/commercial/orders' $ordBody $adminToken
        Assert-Step 'J' 'politica-falta-rejeitada' ($ordNoPol.Status -eq 422 -and "$($ordNoPol.Raw)" -match 'price_policy_missing') "HTTP $($ordNoPol.Status) $($ordNoPol.Raw)"
        $ptJ = New-Guid; $ptiJ = New-Guid
        [void](Invoke-Psql @"
insert into agro360.sales_price_tables(id,tenant_id,segment_id,name,valid_from,valid_to,status,is_default)
values('$ptJ','${tenantId}','$segJ','Tabela Journey J',current_date-7,current_date+120,'ACTIVE',false);
insert into agro360.sales_price_table_items(id,tenant_id,price_table_id,product_id,unit,base_price,maximum_discount)
values('$ptiJ','${tenantId}','$ptJ','${grainP}','KG',1.1000,10.0000);
"@)
        $ordR = Invoke-Api POST '/api/commercial/orders' $ordBody $adminToken
        Assert-Step 'J' 'pedido-do-lote-criado' ($ordR.Status -eq 201) "HTTP $($ordR.Status) $($ordR.Raw)"
        $orderJ = "$($ordR.Body.id)"
        Require-Setup 'J-pedido-id' ($orderJ -ne '') "order=$orderJ"
        # base de preco registrada no item: total = qtd x unit_price e base da politica persistida
        $oiDb = Invoke-Psql "select id::text||'|'||total_amount::text||'|'||base_unit_price::text from agro360.sales_order_items where tenant_id='${tenantId}' and order_id='${orderJ}';"
        $oiParts = @("$oiDb".Split('|')); $oiJ = "$($oiParts[0])"
        Assert-Step 'J' 'preco-registrado-com-criterio' (($oiParts.Count -eq 3) -and [math]::Abs([decimal]$oiParts[1] - [decimal]33000.00) -lt 0.0001 -and [math]::Abs([decimal]$oiParts[2] - [decimal]1.10) -lt 0.0001) "total|base=$($oiParts[1..2]) (esperado 33000.00|1.1000)"
        # desconto acima do maximo da politica do segmento (10%) e rejeitado
        $ordDesc = Invoke-Api POST '/api/commercial/orders' @{ CustomerId = $custJId; Items = @(@{ ProductId = $grainP; Quantity = 30000; Unit = 'KG'; UnitPrice = 0.5; DiscountPercentage = 0 }); Freight = 0 } $adminToken
        Assert-Step 'J' 'desconto-acima-politica-rejeitado' ($ordDesc.Status -eq 422 -and "$($ordDesc.Raw)" -match 'discount_exceeded') "HTTP $($ordDesc.Status) $($ordDesc.Raw)"
        # lote gerado pela safra (cenario E) esta apto ao item do pedido
        $lotQty = Invoke-Psql "select coalesce(quantity,0)::text from agro360.inventory_stock_lots where tenant_id='${tenantId}' and warehouse_id='${whId}' and product_id='${grainP}' and lot_number='LOTE-E1-$runTag';"
        Require-Setup 'J-lote-da-safra-20000' ([decimal]"$lotQty" -eq 20000) "qty=$lotQty (esperado 20000)"
        $lotJ = Invoke-Psql "select id::text from agro360.inventory_stock_lots where tenant_id='${tenantId}' and warehouse_id='${whId}' and product_id='${grainP}' and lot_number='LOTE-E1-$runTag';"
        $elig = Invoke-Api GET "/api/logistics/trips/fulfillment/order-items/${oiJ}/eligible-lots" -Token $adminToken
        Assert-Step 'J' 'lote-aptos-ao-pedido' ($elig.Status -eq 200 -and "$($elig.Raw)" -match ([regex]::Escape($lotJ))) "HTTP $($elig.Status) $($elig.Raw)"
        $fuCmd = @{ Number = "J-B1-${runTag}"; OriginWarehouseId = $whId; CustomerId = $custJId; Destination = 'Journey Bloco B'; IdempotencyKey = "jrn-j-res-$runTag"; Items = @(@{ OrderItemId = $oiJ; StockLotId = $lotJ; Quantity = 20000; PickedQuantity = 20000; CheckedQuantity = 20000; Unit = 'kg' }) }
        # sem aprovacao do pedido, a reserva/expedicao do lote e bloqueada (gate antes do efeito)
        $fuEarly = Invoke-Api POST '/api/logistics/trips/fulfillment' $fuCmd $adminToken
        Assert-Step 'J' 'expedicao-antes-aprovacao-rejeitada' ($fuEarly.Status -eq 409) "HTTP $($fuEarly.Status) $($fuEarly.Raw)"
        # maquina de estados do pedido: DRAFT nao aprova direto (DRAFT -> UNDER_REVIEW -> APPROVED)
        $stDirect = Invoke-Api POST "/api/commercial/orders/${orderJ}/status" @{ Status = 'APPROVED' } $adminToken
        Assert-Step 'J' 'draf-nao-aprova-direto' ($stDirect.Status -eq 422 -and "$($stDirect.Raw)" -match 'order_transition_invalid') "HTTP $($stDirect.Status) $($stDirect.Raw)"
        $subAdm = Invoke-Api POST "/api/commercial/orders/${orderJ}/status" @{ Status = 'UNDER_REVIEW' } $adminToken
        $ordStatus0 = Invoke-Psql "select status from agro360.sales_orders where tenant_id='${tenantId}' and id='${orderJ}';"
        Assert-Step 'J' 'pedido-enviado-em-analise' (($subAdm.Status -in 200,204) -and "$ordStatus0" -eq 'UNDER_REVIEW') "HTTP $($subAdm.Status) status=$ordStatus0"
        # aprovacao: operador (sem commercial.orders.approve) e negado; admin aprova
        $stOp = Invoke-Api POST "/api/commercial/orders/${orderJ}/status" @{ Status = 'APPROVED' } $operatorToken
        Assert-Step 'J' 'aprovacao-sem-permissao-negada' ($stOp.Status -eq 403) "HTTP $($stOp.Status) $($stOp.Raw)"
        $stAdm = Invoke-Api POST "/api/commercial/orders/${orderJ}/status" @{ Status = 'APPROVED' } $adminToken
        $ordStatus = Invoke-Psql "select status from agro360.sales_orders where tenant_id='${tenantId}' and id='${orderJ}';"
        Assert-Step 'J' 'pedido-aprovado' (($stAdm.Status -in 200,204) -and "$ordStatus" -eq 'APPROVED') "HTTP $($stAdm.Status) status=$ordStatus"
        $fuR = Invoke-Api POST '/api/logistics/trips/fulfillment' $fuCmd $adminToken
        Assert-Step 'J' 'reserva-lote-criada' ($fuR.Status -eq 201) "HTTP $($fuR.Status) $($fuR.Raw)"
        $shipJ = "$($fuR.Body.id)"
        Require-Setup 'J-expedicao-id' ($shipJ -ne '') "shipment=$shipJ"
        $bal = Invoke-Psql "select b.available::text||'|'||b.reserved::text from agro360.inventory_stock_balances b where b.tenant_id='${tenantId}' and b.warehouse_id='${whId}' and b.product_id='${grainP}';"
        $balParts = @("$bal".Split('|'))
        Assert-Step 'J' 'reserva-consome-disponibilidade' (([decimal]$balParts[1] -eq 20000) -and ([decimal]$balParts[0] - [decimal]$balParts[1]) -eq 0) "available|reserved=$bal (invariante available-reserved=0; baixa fisica so no dispatch)"
        # replay: mesma chave + mesmo conteudo retorna a MESMA expedicao, sem duplicar reserva
        $fuR2 = Invoke-Api POST '/api/logistics/trips/fulfillment' $fuCmd $adminToken
        $resCount = Invoke-Psql "select count(*)::text||'|'||count(distinct reservation_id)::text from agro360.fulfillment_shipment_items where tenant_id='${tenantId}' and shipment_id='${shipJ}';"
        Assert-Step 'J' 'replay-reserva-sem-duplicar' ($fuR2.Status -eq 201 -and "$($fuR2.Body.id)" -eq $shipJ -and "$resCount" -eq '1|1') "id=$($fuR2.Body.id) itens|reservas=$resCount (esperado 1|1)"
        $fuCmdBad = @{ Number = "J-B1-${runTag}"; OriginWarehouseId = $whId; CustomerId = $custJId; Destination = 'Journey Bloco B'; IdempotencyKey = "jrn-j-res-$runTag"; Items = @(@{ OrderItemId = $oiJ; StockLotId = $lotJ; Quantity = 19000; PickedQuantity = 19000; CheckedQuantity = 19000; Unit = 'kg' }) }
        $fuBad = Invoke-Api POST '/api/logistics/trips/fulfillment' $fuCmdBad $adminToken
        Assert-Step 'J' 'replay-reserva-outro-conteudo-conflicto' ($fuBad.Status -eq 409) "HTTP $($fuBad.Status) $($fuBad.Raw)"
        # saida fisica (dispatch): replay sem duplicar movimento; versao divergente conflita
        $shipV = [long](Invoke-Psql "select version::text from agro360.fulfillment_shipments where tenant_id='${tenantId}' and id='${shipJ}';")
        $disR = Invoke-Api POST "/api/logistics/trips/fulfillment/${shipJ}/dispatch" @{ Version = $shipV; IdempotencyKey = "jrn-j-dis-$runTag" } $adminToken
        $mov = Invoke-Psql "select count(*)::text||'|'||coalesce(sum(quantity),0)::text from agro360.inventory_stock_movements where tenant_id='${tenantId}' and movement_type='SALE' and reference_type='FULFILLMENT_SHIPMENT' and reference_id='${shipJ}';"
        $lotAfter = Invoke-Psql "select coalesce(quantity,0)::text from agro360.inventory_stock_lots where tenant_id='${tenantId}' and id='${lotJ}';"
        Assert-Step 'J' 'saida-fisica-lote-consumido' ($disR.Status -eq 204 -and "$($mov.Split('|')[0])" -eq '1' -and ([decimal]"$($mov.Split('|')[1])" -eq 20000) -and ([decimal]"$lotAfter" -eq 0)) "mov=count|qty=$mov lote=$lotAfter (esperado 1|20000 e lote 0)"
        $disR2 = Invoke-Api POST "/api/logistics/trips/fulfillment/${shipJ}/dispatch" @{ Version = $shipV; IdempotencyKey = "jrn-j-dis-$runTag" } $adminToken
        $movCount2 = Invoke-Psql "select count(*)::text from agro360.inventory_stock_movements where tenant_id='${tenantId}' and movement_type='SALE' and reference_type='FULFILLMENT_SHIPMENT' and reference_id='${shipJ}';"
        Assert-Step 'J' 'resend-saida-sem-duplicar' ($disR2.Status -eq 204 -and "$movCount2" -eq '1') "mov=$movCount2"
        $disR3 = Invoke-Api POST "/api/logistics/trips/fulfillment/${shipJ}/dispatch" @{ Version = $shipV + 1; IdempotencyKey = "jrn-j-dis-$runTag" } $adminToken
        Assert-Step 'J' 'saida-versao-diferente-conflicto' ($disR3.Status -eq 409) "HTTP $($disR3.Status) $($disR3.Raw)"
        # tentativas: frustrada (recusa parcial) NAO fatura; aceite parcial fatura SOMENTE o aceito (18000 x 1.10 = 19800)
        $siJ = Invoke-Psql "select id::text from agro360.fulfillment_shipment_items where tenant_id='${tenantId}' and shipment_id='${shipJ}';"
        $occAt1 = (Get-Date).ToUniversalTime().ToString('o')
        $at1 = Invoke-Api POST "/api/logistics/trips/fulfillment/${shipJ}/attempts" @{ OccurredAt = $occAt1; Destination = 'Journey Bloco B'; ResponsibleId = $adminUser; Status = 'FAILED'; Reason = "Veiculo atrasado (JRN ${runTag})"; IdempotencyKey = "jrn-j-at1-$runTag"; Items = @(@{ ShipmentItemId = $siJ; AcceptedQuantity = 0; RefusedQuantity = 2000; Reason = 'Avaria parcial' }) } $adminToken
        $rec1 = Invoke-Psql "select count(*)::text from agro360.finance_receivables where tenant_id='${tenantId}' and fulfillment_attempt_id is not null;"
        Assert-Step 'J' 'entrega-frustrada-sem-faturamento' ($at1.Status -eq 201 -and "$rec1" -eq '0') "attempt=$($at1.Status) recebiveis=$rec1 (esperado 0)"
        $occAt2 = (Get-Date).ToUniversalTime().ToString('o')
        $at2Cmd = @{ OccurredAt = $occAt2; Destination = 'Journey Bloco B'; ResponsibleId = $adminUser; Status = 'ACCEPTED'; IdempotencyKey = "jrn-j-at2-$runTag"; Items = @(@{ ShipmentItemId = $siJ; AcceptedQuantity = 18000; RefusedQuantity = 0 }) }
        $at2 = Invoke-Api POST "/api/logistics/trips/fulfillment/${shipJ}/attempts" $at2Cmd $adminToken
        $recDb = Invoke-Psql "select original_amount::text||'|'||balance::text||'|'||status||'|'||fulfillment_attempt_id::text||'|'||customer_name from agro360.finance_receivables where tenant_id='${tenantId}' and fulfillment_attempt_id is not null;"
        $recParts = @("$recDb".Split('|'))
        Assert-Step 'J' 'aceite-parcial-emite-recebeivel' ($at2.Status -eq 201 -and $recParts.Count -ge 5 -and [math]::Abs([decimal]$recParts[0] - 19800.00) -lt 0.0001 -and [math]::Abs([decimal]$recParts[1] - 19800.00) -lt 0.0001 -and "$($recParts[2])" -eq 'OPEN' -and "$($recParts[4])" -like "Cliente Jornada J ${runTag}") "recebeivel=$recDb (esperado 19800.00|19800.00|OPEN|<attempt>|<cliente>)"
        $attRec = "$($recParts[3])"
        $shipSt = Invoke-Psql "select status from agro360.fulfillment_shipments where tenant_id='${tenantId}' and id='${shipJ}';"
        Assert-Step 'J' 'expedicao-aberta-com-recusa-pendente' ("$shipSt" -eq 'RETURN_PENDING') "status=$shipSt (recusa 2000 aguardando retorno)"
        # replay do aceite: MESMA tentativa (conteudo identico), sem duplicar recebivel
        $at2r = Invoke-Api POST "/api/logistics/trips/fulfillment/${shipJ}/attempts" $at2Cmd $adminToken
        $recCount2 = Invoke-Psql "select count(*)::text from agro360.finance_receivables where tenant_id='${tenantId}' and fulfillment_attempt_id is not null;"
        Assert-Step 'J' 'resend-aceite-sem-duplicar-recebeivel' ($at2r.Status -eq 201 -and "$($at2r.Body.id)" -eq $attRec -and "$recCount2" -eq '1') "attempt=$($at2r.Body.id) recebiveis=$recCount2 (esperado 1)"
        # visibilidade financeira: admin (finance.read) ve o recebivel; operador e negado
        $recList = Invoke-Api GET '/api/finance/receivables' -Token $adminToken
        $recOp = Invoke-Api GET '/api/finance/receivables' -Token $operatorToken
        Assert-Step 'J' 'recebeivel-visivel-por-finance-read' ($recList.Status -eq 200 -and "$($recList.Raw)" -match '19800' -and $recOp.Status -eq 403) "list=$($recList.Status) operator=$($recOp.Status)"
        # composicao preservada: pedido 30000 com 20000 expedidos segue aberto para os 10000 restantes
        $comp = Invoke-Psql "select oi.quantity::text||'|'||coalesce(sum(si.checked_quantity),0)::text||'|'||so.status from agro360.sales_order_items oi join agro360.sales_orders so on so.tenant_id=oi.tenant_id and so.id=oi.order_id left join agro360.fulfillment_shipment_items si on si.tenant_id=oi.tenant_id and si.order_item_id=oi.id left join agro360.fulfillment_shipments fs on fs.tenant_id=si.tenant_id and fs.id=si.shipment_id and fs.deleted_at is null and fs.status<>'CANCELLED' where oi.tenant_id='${tenantId}' and oi.id='${oiJ}' group by oi.quantity, so.status;"
        $compParts = @("$comp".Split('|'))
        Assert-Step 'J' 'composicao-parcial-preservada' (([decimal]$compParts[0] -eq 30000) -and ([decimal]$compParts[1] -eq 20000) -and "$($compParts[2])" -eq 'FULFILLMENT') "qtd|expedido|status=$comp (esperado 30000|20000|FULFILLMENT)"
        # resultado comercial: bruto/retornos/liquido/margem com memoria de calculo + reconciliacao api==db
        $cutoffJ = (Get-Date).AddDays(1).ToUniversalTime().ToString('yyyy-MM-dd')
        $cloJ = Invoke-Api GET "/api/v1/harvest/closing?seasonId=${seasonId}&cutoffDate=${cutoffJ}" -Token $adminToken
        $indsJ = @($cloJ.Body.indicators)
        $iNet = @($indsJ | Where-Object { $_.code -eq 'recognized-revenue' })[0]
        $iGross = @($indsJ | Where-Object { $_.code -eq 'commercial-revenue-gross' })[0]
        $iRet = @($indsJ | Where-Object { $_.code -eq 'commercial-revenue-returns' })[0]
        $iMargin = @($indsJ | Where-Object { $_.code -eq 'commercial-margin' })[0]
        $apropJ = Invoke-Psql "select coalesce(sum(a.amount),0)::text from agro360.cost_allocations a where a.tenant_id='${tenantId}' and a.season_id='${seasonId}' and a.confirmed_at::date<=to_date('${cutoffJ}','YYYY-MM-DD') and a.status='CONFIRMED';"
        $marginExp = [decimal]19800.00 - [decimal]"$apropJ"
        Assert-Step 'J' 'resultado-comercial-indicadores' ($cloJ.Status -eq 200 -and $iNet -and $iGross -and $iRet -and $iMargin -and "$($iNet.availability)" -eq 'AVAILABLE' -and [math]::Abs([decimal]$iNet.value - 19800.00) -lt 0.0001 -and [math]::Abs([decimal]$iGross.value - 19800.00) -lt 0.0001 -and [math]::Abs([decimal]$iRet.value) -lt 0.0001 -and [math]::Abs([decimal]$iMargin.value - $marginExp) -lt 0.0001) "net=$($iNet.value) gross=$($iGross.value) ret=$($iRet.value) margin=$($iMargin.value) apropDb=$apropJ (esperado 19800/19800/0/$marginExp)"
        $dbRev = Invoke-Psql @"
with season_receipts as (select distinct q.lot_number from agro360.production_receipts q join agro360.harvest_records r on r.tenant_id=q.tenant_id and r.id=q.harvest_record_id join agro360.harvest_plans p on p.tenant_id=r.tenant_id and p.id=r.plan_id where p.tenant_id='${tenantId}' and p.season_id='${seasonId}' and q.received_at::date <= '${cutoffJ}'),
season_lots as (select l.id lot_id from agro360.inventory_stock_lots l where l.tenant_id='${tenantId}' and l.lot_number in (select lot_number from season_receipts)),
sd as (select si.id, coalesce(sum(dai.accepted_quantity),0) acc, coalesce((select sum(ret.quantity) from agro360.fulfillment_returns ret where ret.tenant_id=si.tenant_id and ret.shipment_item_id=si.id and ret.status in ('RELEASED','BLOCKED','DISPOSED')),0) retq, oi.unit_price up from agro360.fulfillment_shipment_items si join agro360.fulfillment_shipments s on s.tenant_id=si.tenant_id and s.id=si.shipment_id and s.deleted_at is null join agro360.sales_order_items oi on oi.tenant_id=si.tenant_id and oi.id=si.order_item_id left join agro360.fulfillment_delivery_attempt_items dai on dai.tenant_id=si.tenant_id and dai.shipment_item_id=si.id left join agro360.fulfillment_delivery_attempts da on da.tenant_id=dai.tenant_id and da.id=dai.attempt_id and da.occurred_at::date <= '${cutoffJ}' where si.tenant_id='${tenantId}' and si.stock_lot_id in (select lot_id from season_lots) group by si.id, oi.unit_price)
select coalesce(sum(greatest(0,sd.acc-sd.retq)*sd.up),0)::text||'|'||coalesce(sum(sd.acc*sd.up),0)::text from sd;
"@
        $dbRevParts = @("$dbRev".Split('|'))
        Assert-Step 'J' 'resultado-reconciliado-com-db' ([math]::Abs([decimal]$dbRevParts[0] - [decimal]$iNet.value) -lt 0.0001 -and [math]::Abs([decimal]$dbRevParts[1] - [decimal]$iGross.value) -lt 0.0001) "db(net|bruto)=$($dbRevParts[0])|$($dbRevParts[1]) api=$($iNet.value)|$($iGross.value)"
        # liquidez nao altera reconhecimento: pagar o recebivel fecha o financeiro, a receita da safra nao muda
        $rid = Invoke-Psql "select id::text from agro360.finance_receivables where tenant_id='${tenantId}' and fulfillment_attempt_id='${attRec}';"
        $revAcc = Invoke-Psql "select id::text from agro360.finance_chart_of_accounts where tenant_id='${tenantId}' and type='REVENUE' and active order by display_order limit 1;"
        $settle = Invoke-Api POST "/api/finance/receivables/${rid}/receive" @{ Amount = 19800.00; Date = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd'); AccountId = $revAcc; Notes = "Liquidacao JRN ${runTag}" } $adminToken
        $recFinal = Invoke-Psql "select status||'|'||balance::text from agro360.finance_receivables where tenant_id='${tenantId}' and id='${rid}';"
        $cloJ2 = Invoke-Api GET "/api/v1/harvest/closing?seasonId=${seasonId}&cutoffDate=${cutoffJ}" -Token $adminToken
        $iNet2 = @(@($cloJ2.Body.indicators) | Where-Object { $_.code -eq 'recognized-revenue' })[0]
        Assert-Step 'J' 'pagamento-nao-altera-reconhecimento' (($settle.Status -in 200,204) -and "$($recFinal.Split('|')[0])" -eq 'RECEIVED' -and ([decimal]"$($recFinal.Split('|')[1])" -eq 0) -and [math]::Abs([decimal]$iNet2.value - 19800.00) -lt 0.0001) "settle=$($settle.Status) titulo=$recFinal net2=$($iNet2.value) (receita inalterada)"
    } catch { Note-Blocked 'J' 'secao' $_ }

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
