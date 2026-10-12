param(
    [string]$PostgresBin = 'C:\Program Files\PostgreSQL\18\bin',
    [switch]$KeepRunning,
    [switch]$RebuildTemplate
)

# E2E de recuperação automática de inspeções por evento operacional (seção 3 do incremento):
#   S1 caminho feliz: modelo publicado -> recibo -> intent STARTED + 1 run; replay do comando
#      nao duplica intent/run; genealogia da safra responde (correcao i.process/i.error_code).
#   S2 crash antes do intent: origem commitada SEM passar pela API (insert direto) ->
#      varredura do worker adota e inicia a inspecao SEM reenviar o comando original.
#   S3 PENDING_MODEL: sem modelo aplicavel -> pendencia de configuracao com ciclo longo
#      (gate por next_attempt_at: sem loop apertado); publicacao do modelo -> STARTED automatico.
#   S4 falha: origem sumiu -> tentativas com backoff -> DEAD_LETTERED com motivo
#      (error_code + last_error); operador sem compliance.approve recebe 403 no re-disparo.
#   S5 re-disparo manual: compliance.approve zera contagem, audita (audit_logs) e processa na hora;
#      origem restaurada + modelo publicado -> STARTED.
#   S6 concorrência: dois workers simultaneos -> exatamente 1 intent e 1 run por origem.
#   S7 reinicio: lease expirado de worker morto -> novo processo reclama e processa
#      (nenhum trabalho pendente perdido no restart).
#
# Cluster descartavel (template compartilhado com o E2E de compras) + API e Workers em Release.
# Overrides para E2E rapido: PollSeconds=2, ModelMissingPollSeconds=5, MaximumAttempts=3, LeaseSeconds=30.

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

$evidenceDir = Join-Path $root ('artifacts\inspection-recovery-e2e-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidenceDir | Out-Null
Start-Transcript -Path (Join-Path $evidenceDir 'transcript.log') -Append | Out-Null
$processes = [Collections.Generic.List[Diagnostics.Process]]::new()
$weStartedCluster = $false
$dbName = 'agro360_insprec_' + [guid]::NewGuid().ToString('N').Substring(0, 12)
$templateDir = Join-Path $env:TEMP 'opencode\pg-journey-template'
$templateDb = 'journey_template'
$templateMarker = Join-Path $templateDir 'template-ready.txt'
$fullSqlPath = Join-Path $root 'database/agro360-postgres-full.sql'
$apiPort = 0
$knownHash = 'pbkdf2-sha512$210000$Fk496Qko66Cw//8uYyP3mw==$PqcZJTtl+96es6GmUF3khyrZvMJqSVDIe/YfI7TCv2U='
$runTag = (Get-Date).ToString('HHmmss')
$tenantId = ''
$adminUser = ''
$operatorUser = ''
$farmA = ''
$whId = ''
$fieldId = ''
$seasonId = ''
$planId = ''
$w1 = $null
$w2 = $null
$w3 = $null
$apiProc = $null
$script:lastReceiptBody = $null

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

function Invoke-Api([string]$Method, [string]$Path, $Body = $null, [string]$Token = '') {
    $url = "http://127.0.0.1:${apiPort}${Path}"
    $headers = @{ 'Content-Type' = 'application/json' }
    if ($Token) { $headers['Authorization'] = "Bearer $Token" }
    $params = @{ Method = $Method; Uri = $url; Headers = $headers; UseBasicParsing = $true; TimeoutSec = 60 }
    if ($null -ne $Body) {
        $json = $Body | ConvertTo-Json -Depth 8 -Compress
        $params['Body'] = [Text.Encoding]::UTF8.GetBytes($json)
    }
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
        if ($null -eq $resp) { throw "HTTP ${Method} ${Path}: $($_.Exception.Message)" }
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
}

function Wait-Until([scriptblock]$Condition, [int]$TimeoutSec, [string]$What) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        if (& $Condition) { return $true }
        Start-Sleep -Milliseconds 1000
    }
    $ok = & $Condition
    if (-not $ok) { Write-Host "WARN Wait-Until timeout: $What" -ForegroundColor Yellow }
    return $ok
}

function Get-IntentParts([string]$Key) {
    # status | run_id | attempts | error_code | claimed_by | last_error(restante)
    $s = Invoke-Psql "select coalesce(status,'') || '|' || coalesce(run_id::text,'') || '|' || attempts || '|' || coalesce(error_code,'') || '|' || coalesce(claimed_by,'') || '|' || coalesce(left(last_error,300),'') from agro360.quality_inspection_event_intents where tenant_id='${tenantId}' and idempotency_key='${Key}' and deleted_at is null;"
    if (-not $s) { return $null }
    return ($s -split '\|', 6)
}

function Get-IntentKey([string]$ReceiptId) {
    # Atencao: Guid.ToString("N") em C# produz hex MINUSCULO (mesma regra do BuildKey).
    return 'EVT:HARVEST_RECEIPT:' + ([guid]$ReceiptId).ToString('N')
}

function Get-RunCount([string]$Key) {
    $c = Invoke-Psql "select count(*)::text from agro360.quality_inspection_runs where tenant_id='${tenantId}' and idempotency_key='${Key}' and deleted_at is null;"
    return [int]([regex]::Match($c, '\d+').Value)
}

function Get-IntentCount([string]$Key) {
    $c = Invoke-Psql "select count(*)::text from agro360.quality_inspection_event_intents where tenant_id='${tenantId}' and idempotency_key='${Key}' and deleted_at is null;"
    return [int]([regex]::Match($c, '\d+').Value)
}

function Publish-InspectionModel([string]$Code, [string]$Name, [string]$ProductId, [string]$Token) {
    $create = Invoke-Api POST '/api/inspections/models' @{ Code = $Code; Name = $Name; Description = "Modelo E2E $Code"; ProcessCode = 'HARVEST_RECEIPT'; Precedence = 100; AllowManualSelection = $false; ProductId = $ProductId } $Token
    if ($create.Status -ne 200 -and $create.Status -ne 201) { throw "criacao do modelo $Code falhou: HTTP $($create.Status) $($create.Raw)" }
    $modelId = Invoke-Psql "select id::text from agro360.quality_inspection_models where tenant_id='${tenantId}' and code='$Code' and deleted_at is null limit 1;"
    if (-not $modelId) { throw "modelo $Code nao encontrado no banco" }
    $vrow = Invoke-Psql "select v.id::text || '|' || v.row_version from agro360.quality_inspection_model_versions v join agro360.quality_inspection_models m on m.id = v.model_id where m.tenant_id='${tenantId}' and m.code='$Code' and v.status='DRAFT' order by v.version desc limit 1;"
    $versionId = $vrow.Split('|')[0]
    $rv = [long]$vrow.Split('|')[1]
    $upd = Invoke-Api PUT "/api/inspections/versions/${versionId}" @{
        ChangeReason = 'Versao inicial E2E'
        ExpectedRowVersion = $rv
        Sections = @(@{ StableKey = 'geral'; Name = 'Condicao fisica do lote'; SortOrder = 1; Criteria = @(@{ StableKey = 'umidade'; Name = 'Umididade'; CriterionType = 'NUMBER'; Required = $true; Critical = $false; AllowNotApplicable = $false; RequireNaJustification = $false; RequireReview = $false; Weight = 1.0; SortOrder = 1 }) })
    } $Token
    if ($upd.Status -ne 204) { throw "draft da versao $Code falhou: HTTP $($upd.Status) $($upd.Raw)" }
    $rv = [long](Invoke-Psql "select row_version::text from agro360.quality_inspection_model_versions where id='${versionId}';")
    $sub = Invoke-Api POST "/api/inspections/versions/${versionId}/submit-review" @{ ExpectedRowVersion = $rv; Reason = 'Revisao E2E' } $Token
    if ($sub.Status -ne 204) { throw "submit-review $Code falhou: HTTP $($sub.Status) $($sub.Raw)" }
    $rv = [long](Invoke-Psql "select row_version::text from agro360.quality_inspection_model_versions where id='${versionId}';")
    $today = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd')
    $pub = Invoke-Api POST "/api/inspections/versions/${versionId}/publish" @{ ValidFrom = $today; ExpectedRowVersion = $rv; ChangeReason = 'Publicacao E2E' } $Token
    if ($pub.Status -ne 204) { throw "publish $Code falhou: HTTP $($pub.Status) $($pub.Raw)" }
    $st = Invoke-Psql "select status from agro360.quality_inspection_model_versions where id='${versionId}';"
    if ($st -ne 'PUBLISHED') { throw "versao $Code status=$st (esperado PUBLISHED)" }
    Write-Host "     modelo $Code publicado (model=$modelId version=$versionId)" -ForegroundColor DarkGray
    return $modelId
}

function New-HarvestRecord([int]$Num, [double]$AreaHa, [double]$QtyKg, [string]$RefSuffix) {
    $iso = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    $ref = "APNT-R${Num}-${runTag}${RefSuffix}"
    $key = "rec-r${num}${runTag}${RefSuffix}"
    $r = Invoke-Api POST '/api/v1/harvest/records' @{ PlanId = $planId; OperationalAt = $iso; HarvestedQuantity = $QtyKg; Unit = 'kg'; HarvestedAreaHa = $AreaHa; CommercialReference = $ref; IdempotencyKey = $key } $script:adminToken
    if ($r.Status -ne 200 -and $r.Status -ne 201) { throw "apontamento R${Num} falhou: HTTP $($r.Status) $($r.Raw)" }
    $id = Invoke-Psql "select id::text from agro360.harvest_records where tenant_id='${tenantId}' and commercial_reference='$ref';"
    if (-not $id) { throw "apontamento $ref nao encontrado no banco" }
    return $id
}

function New-HarvestReceipt([int]$Num, [string]$RecordId, [string]$ProductId, [string]$LotSuffix, [double]$QtyKg, [string]$IsoOverride = '') {
    # O replay exige o MESMO conteudo (mesmo hash de idempotencia); por isso o chamante
    # pode fixar a ReceivedAt para reenviar byte-identicamente.
    $iso = if ($IsoOverride) { $IsoOverride } else { (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ') }
    $lot = "LOTE-R${Num}-${runTag}${LotSuffix}"
    $key = "rcpt-r${num}${runTag}${LotSuffix}"
    $body = @{ HarvestRecordId = $RecordId; WarehouseId = $whId; ReceivedAt = $iso; ReceivedQuantity = $QtyKg; Unit = 'kg'; LotNumber = $lot; EntryMode = 'MANUAL'; IdempotencyKey = $key }
    $r = Invoke-Api POST '/api/v1/harvest/receipts' $body $script:adminToken
    if ($r.Status -ne 200 -and $r.Status -ne 201) { throw "recibo R${Num} falhou: HTTP $($r.Status) $($r.Raw)" }
    $id = Invoke-Psql "select id::text from agro360.production_receipts where tenant_id='${tenantId}' and idempotency_key='$key';"
    if (-not $id) { throw "recibo $key nao encontrado no banco" }
    # Guarda o corpo EXATO enviado para o replay reenviar byte-identico (mesma variavel =>
    # mesmo ConvertTo-Json => mesmo binding no servidor => mesmo request_hash).
    $script:lastReceiptBody = $body
    return $id
}

function New-DirectReceipt([string]$RecordId, [string]$ProductId, [string]$Lot, [double]$QtyKg, [string]$Note) {
    # Insert direto com id gerado aqui: o estado exato de "origem commitada, processo caiu
    # antes de criar o intent". A chave do evento e derivada do proprio id (mesma regra do trigger).
    $rid = [guid]::NewGuid()
    $key = 'EVT:HARVEST_RECEIPT:' + ($rid.ToString('N'))
    Invoke-Psql @"
insert into agro360.production_receipts(id,tenant_id,harvest_record_id,warehouse_id,product_id,received_at,received_quantity,unit,lot_number,entry_mode,notes,idempotency_key,request_hash,created_by)
values('${rid}','${tenantId}','${RecordId}','${whId}','${ProductId}',now(),${QtyKg},'kg','${Lot}','MANUAL','${Note}','${key}',substr(md5(gen_random_uuid()::text)||md5('${key}')||md5('e2e-crash'),1,64),'${adminUser}');
"@ | Out-Null
    return @{ Id = $rid.ToString(); Key = $key }
}

function Start-RecoveryWorker([string]$Name) {
    $workerDll = Join-Path $root 'src\Hosts\Agro360.Worker\bin\Release\net10.0\Agro360.Worker.dll'
    if (-not (Test-Path $workerDll)) { throw "Worker binary nao encontrado: $workerDll (faça o build antes)" }
    $proc = Start-Process dotnet -ArgumentList "`"$workerDll`"" -WorkingDirectory (Split-Path $workerDll) -PassThru -NoNewWindow -RedirectStandardOutput (Join-Path $evidenceDir "${Name}.out.log") -RedirectStandardError (Join-Path $evidenceDir "${Name}.err.log")
    $processes.Add($proc)
    Start-Sleep -Seconds 4
    if ($proc.HasExited) { throw "worker ${Name} terminou cedo (exit $($proc.ExitCode)); veja ${Name}.err.log em $evidenceDir" }
    return $proc
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
        Write-Host "Template existente em $templateDir (use -RebuildTemplate para forcar recarga; o schema precisa ter 11.25.0)." -ForegroundColor Cyan
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

    $schemaVer = Invoke-Psql "select version from agro360.platform_schema_versions where version='11.25.0';"
    Assert-Step 'Schema-11.25.0-present' ($schemaVer -eq '11.25.0') 'migration 135 (recuperacao de intents) instalada; se ausente, rode com -RebuildTemplate'

    # ============ CONFIG DE E2E RAPIDA (antes dos workers) ============
    $connStr = "Host=127.0.0.1;Port=${pgPort};Database=${dbName};Username=postgres;Search Path=agro360;Command Timeout=60"
    $env:ConnectionStrings__Agro360 = $connStr
    $env:ConnectionStrings__DefaultConnection = $connStr
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:Jwt__SigningKey = 'e2e-test-signing-key-32bytes-minimum!'
    $env:PGPASSWORD = 'trust-auth-placeholder'
    $env:InspectionIntents__PollSeconds = '2'
    $env:InspectionIntents__BatchSize = '50'
    $env:InspectionIntents__MaximumAttempts = '3'
    $env:InspectionIntents__ModelMissingPollSeconds = '5'
    $env:InspectionIntents__LeaseSeconds = '30'

    # ============ START API HOST ============
    $apiPort = Get-FreePort
    $env:ASPNETCORE_URLS = "http://127.0.0.1:${apiPort}"
    $apiExe = Join-Path $root 'src\Hosts\Agro360.Api\bin\Release\net10.0\Agro360.Api.dll'
    if (-not (Test-Path $apiExe)) { throw "API binary not found at $apiExe. Run build first." }
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

    # ============ TENANT / USUARIOS ============
    $tenantId = Invoke-Psql "select id::text from agro360.tenancy_tenants where slug='santa-clara';"
    Assert-Step 'Tenant-santa-clara' ($tenantId -ne '') "tenant=$tenantId"
    Invoke-Psql "update agro360.identity_users set status='ACTIVE', password_hash='$knownHash', must_change_password=false, updated_at=now() where tenant_id='${tenantId}' and lower(email) in ('admin@santaclara.agro360.local','operador.santaclara@agro360.local');" | Out-Null

    # Modulo agroindustria (permissoes production.* usadas por receipts/inspections/allocations).
    # PRECISA preceder o login: as claims de permissao do JWT sao filtradas pelos modulos
    # contratados no momento da autenticacao (FilterEffectiveAccessAsync).
    Invoke-Psql @"
insert into agro360.platform_tenant_module_entitlements(tenant_id,module_id,status,reason,activated_at)
select '${tenantId}', id, 'ACTIVE', 'E2E recuperacao de inspecao - modulo agroindustria', now()
from agro360.platform_module_catalog where code='agroindustry'
on conflict(tenant_id,module_id) do update set status='ACTIVE';
"@ | Out-Null

    $adminLogin = Invoke-Api POST '/api/v1/auth/login' @{ TenantSlug = 'santa-clara'; Email = 'admin@santaclara.agro360.local'; Password = 'Agro360Admin@2024!' }
    Assert-Step 'Login-admin-santa' ($adminLogin.Status -eq 200) "HTTP $($adminLogin.Status)"
    $script:adminToken = $adminLogin.Body.accessToken
    $opLogin = Invoke-Api POST '/api/v1/auth/login' @{ TenantSlug = 'santa-clara'; Email = 'operador.santaclara@agro360.local'; Password = 'Agro360Admin@2024!' }
    Assert-Step 'Login-operador-santa' ($opLogin.Status -eq 200) "HTTP $($opLogin.Status)"
    $opToken = $opLogin.Body.accessToken
    $adminUser = Invoke-Psql "select id::text from agro360.identity_users where tenant_id='${tenantId}' and lower(email)='admin@santaclara.agro360.local' and deleted_at is null;"
    $operatorUser = Invoke-Psql "select id::text from agro360.identity_users where tenant_id='${tenantId}' and lower(email)='operador.santaclara@agro360.local' and deleted_at is null;"
    Assert-Step 'Usuarios-fixtures' ($adminUser -ne '' -and $operatorUser -ne '') "admin=$adminUser op=$operatorUser"
    $farmA = Invoke-Psql "select id::text from agro360.geo_farms where tenant_id='${tenantId}' and registration_number='SC-SEDE-001' and deleted_at is null;"
    Assert-Step 'Fazenda-fixture' ($farmA -ne '') "farm=$farmA"

    # Modulo agroindustria (permissoes production.* usadas por receipts/inspections/allocations).
    Invoke-Psql @"
insert into agro360.platform_tenant_module_entitlements(tenant_id,module_id,status,reason,activated_at)
select '${tenantId}', id, 'ACTIVE', 'E2E recuperacao de inspecao - modulo agroindustria', now()
from agro360.platform_module_catalog where code='agroindustry'
on conflict(tenant_id,module_id) do update set status='ACTIVE';
"@ | Out-Null

    # ============ FIXTURES AGRICOLAS ============
    $fieldId = Invoke-Psql @"
insert into agro360.geo_fields(id,tenant_id,farm_id,name,area_ha,boundary,created_by)
select '$(New-Guid)','${tenantId}','${farmA}','Talhao Recuperacao E2E',40.0,null,'${adminUser}'
where not exists(select 1 from agro360.geo_fields where tenant_id='${tenantId}' and farm_id='${farmA}' and name='Talhao Recuperacao E2E');
select id::text from agro360.geo_fields where tenant_id='${tenantId}' and farm_id='${farmA}' and name='Talhao Recuperacao E2E';
"@
    Assert-Step 'Fixture-talhao' ($fieldId -ne '') "field=$fieldId (40 ha)"

    Invoke-Psql @"
insert into agro360.agriculture_crops(id,tenant_id,name,active,created_by)
select '$(New-Guid)','${tenantId}','Soja Recuperacao E2E',true,'${adminUser}'
where not exists(select 1 from agro360.agriculture_crops where tenant_id='${tenantId}' and name='Soja Recuperacao E2E');
"@ | Out-Null

    $whId = Invoke-Psql @"
insert into agro360.inventory_warehouses(id,tenant_id,farm_id,code,name,type,created_by)
select '$(New-Guid)','${tenantId}','${farmA}','DEP-GRN-RECP','Armazem de Graos Recuperacao E2E','GRAINS','${adminUser}'
where not exists(select 1 from agro360.inventory_warehouses where tenant_id='${tenantId}' and code='DEP-GRN-RECP');
select id::text from agro360.inventory_warehouses where tenant_id='${tenantId}' and code='DEP-GRN-RECP';
"@
    Assert-Step 'Fixture-armazem-graos' ($whId -ne '') "warehouse=$whId"

    $grainP = ''; $grain2P = ''; $grain3P = ''; $grain4P = ''
    foreach ($i in 1..4) {
        $sku = "GRAIN-REC-E2E-${i}"
        $g = Invoke-Psql @"
insert into agro360.inventory_products(id,tenant_id,sku,name,category,base_unit,requires_lot,is_perishable,created_by)
select '$(New-Guid)','${tenantId}','${sku}','Soja grao Recuperacao E2E ${i}','GRAINS','kg',true,false,'${adminUser}'
where not exists(select 1 from agro360.inventory_products where tenant_id='${tenantId}' and sku='${sku}');
select id::text from agro360.inventory_products where tenant_id='${tenantId}' and sku='${sku}';
"@
        switch ($i) { 1 { $grainP = $g } 2 { $grain2P = $g } 3 { $grain3P = $g } 4 { $grain4P = $g } }
    }
    Assert-Step 'Fixture-produtos-grao' (($grainP + $grain2P + $grain3P + $grain4P).Trim().Length -gt 0) "p1..p4 criados"

    $dStart = (Get-Date).AddDays(-30).ToString('yyyy-MM-dd')
    $dEnd = (Get-Date).AddDays(90).ToString('yyyy-MM-dd')
    $seasonR = Invoke-Api POST '/api/v1/agriculture/seasons' @{ FarmId = $farmA; Name = 'Safra Recuperacao E2E'; Crop = 'Soja'; StartDate = $dStart; EndDate = $dEnd; PlannedAreaHa = 40; ExpectedYieldPerHa = 3.5 } $script:adminToken
    Assert-Step 'Safra-criada' ($seasonR.Status -eq 200 -or $seasonR.Status -eq 201) "HTTP $($seasonR.Status)"
    $seasonId = Invoke-Psql "select id::text from agro360.agriculture_seasons where tenant_id='${tenantId}' and name='Safra Recuperacao E2E' and deleted_at is null order by created_at desc limit 1;"
    Assert-Step 'Safra-id' ($seasonId -ne '') "season=$seasonId"

    $dPlanStart = (Get-Date).AddDays(1).ToString('yyyy-MM-dd')
    $dPlanEnd = (Get-Date).AddDays(30).ToString('yyyy-MM-dd')
    $planR = Invoke-Api POST '/api/v1/harvest/plans' @{ FarmId = $farmA; SeasonId = $seasonId; FieldId = $fieldId; ProductId = $grainP; DestinationWarehouseId = $whId; ResponsibleId = $adminUser; PlannedStart = $dPlanStart; PlannedEnd = $dPlanEnd; PlannedAreaHa = 36; EstimatedQuantity = 150000; Unit = 'kg'; IdempotencyKey = "plan-rec-$runTag" } $script:adminToken
    Assert-Step 'Plano-colheita-criado' ($planR.Status -eq 200 -or $planR.Status -eq 201) "HTTP $($planR.Status) $($planR.Raw)"
    $planId = Invoke-Psql "select id::text from agro360.harvest_plans where tenant_id='${tenantId}' and farm_id='${farmA}' and field_id='${fieldId}' order by created_at desc limit 1;"
    Assert-Step 'Plano-colheita-id' ($planId -ne '') "plan=$planId"

    # ============ WORKER 1 ============
    $w1 = Start-RecoveryWorker 'worker1'
    Write-Host 'Worker de recuperacao 1 no ar (poll=2s, modelPoll=5s, maxAttempts=3, lease=30s)' -ForegroundColor Cyan

    # ===================== S1: caminho feliz + replay =====================
    Write-Host '--- S1: modelo publicado -> recibo -> intent STARTED; replay sem duplicar ---' -ForegroundColor Cyan
    Publish-InspectionModel 'M-REC-GRAO-1' 'Umididade de Soja (grao 1)' $grainP $script:adminToken | Out-Null
    $rec1 = New-HarvestRecord 1 5 21000 'A'
    $rcpIso1 = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    $rcp1 = New-HarvestReceipt 1 $rec1 $grainP 'A' 21000 $rcpIso1
    $k1 = Get-IntentKey $rcp1
    $p1 = Get-IntentParts $k1
    Assert-Step 'S1-intent-STARTED' ($null -ne $p1 -and $p1[0] -eq 'STARTED') "status=$(if ($p1) { $p1[0] } else { 'sem-intent' })"
    Assert-Step 'S1-run-criado' ([int]$p1[1].Length -ge 32) "run=$($p1[1])"
    Assert-Step 'S1-exatamente-1-run' (Get-RunCount $k1 -eq 1) "runs=$(Get-RunCount $k1)"
    Assert-Step 'S1-attempts-zero' ($p1[2] -eq '0') "attempts=$($p1[2])"

    # Replay do MESMO comando: reenvia o corpo EXATO que criou o recibo R1 (mesma variavel).
    # Mesmo hashtable => mesmo ConvertTo-Json byte-a-byte => mesmo binding no servidor =>
    # mesmo request_hash => o Replay() reconhece a duplicata e devolve o ja existente (200/201).
    $replay = Invoke-Api POST '/api/v1/harvest/receipts' $script:lastReceiptBody $script:adminToken
    Assert-Step 'S1-replay-aceito' ($replay.Status -eq 200 -or $replay.Status -eq 201) "HTTP $($replay.Status)"
    Start-Sleep -Seconds 3
    Assert-Step 'S1-replay-sem-dup-intent' (Get-IntentCount $k1 -eq 1) "intents=$(Get-IntentCount $k1)"
    Assert-Step 'S1-replay-sem-dup-run' (Get-RunCount $k1 -eq 1) "runs=$(Get-RunCount $k1)"

    # Genealogia da safra: a consulta que falhava por i.process/i.error_code inexistentes.
    $genea = Invoke-Api GET "/api/v1/harvest/genealogy/seasons/${seasonId}" -Token $script:adminToken
    Assert-Step 'S1-genealogia-safra-200' ($genea.Status -eq 200) "HTTP $($genea.Status) (correcao process_code/error_code validada)"

    # ============ Contrato do GET event-intents (situacao/tentativa/motivo/proxima) ============
    $list = Invoke-Api GET '/api/inspections/event-intents?process=HARVEST_RECEIPT&limit=50' -Token $script:adminToken
    $listItem = $null
    if ($list.Status -eq 200 -and $list.Body) { $listItem = @($list.Body | Where-Object { "$($_.OriginId)" -eq "$rcp1" }) | Select-Object -First 1 }
    Assert-Step 'Contrato-lista-intents-campos' ($null -ne $listItem -and $null -ne $listItem.PSObject.Properties['Attempts'] -and $null -ne $listItem.PSObject.Properties['LastError'] -and $null -ne $listItem.PSObject.Properties['ErrorCode'] -and $null -ne $listItem.PSObject.Properties['NextAttemptAt']) "campos recuperacao presentes no payload"

    # ===================== S2: crash antes do intent =====================
    Write-Host '--- S2: origem commitada sem API (crash antes do intent) -> worker adota e inicia ---' -ForegroundColor Cyan
    $rec2 = New-HarvestRecord 2 5 18000 'B'
    $d2 = New-DirectReceipt $rec2 $grainP "LOTE-CRASH-$runTag" 18000 'Simulacao de crash: origem confirmada sem passagem pela API.'
    $rcp2 = $d2.Id
    $k2real = $d2.Key
    $beforeRuns = Get-RunCount $k2real
    Assert-Step 'S2-estado-inicial-sem-intent' ((Get-IntentCount $k2real) -eq 0 -and $beforeRuns -eq 0) "intent=0 runs=0 (origem existe, nenhum despacho)"
    $ok = Wait-Until { $x = Get-IntentParts $k2real; ($null -ne $x -and $x[0] -eq 'STARTED') } 30 'S2 intencao adotada'
    $p2 = Get-IntentParts $k2real
    Assert-Step 'S2-worker-adotou-e-iniciou' ($ok -and $p2[0] -eq 'STARTED' -and [int]$p2[1].Length -ge 32) "status=$(if ($p2) { $p2[0] } else { '?' }) run=$(if ($p2) { $p2[1] } else { '?' })"
    Assert-Step 'S2-exatamente-1-run' (Get-RunCount $k2real -eq 1) "runs=$(Get-RunCount $k2real) (recuperacao sem reenviar comando original)"
    $note2 = Invoke-Psql "select coalesce(notes,'') from agro360.quality_inspection_event_intents where tenant_id='${tenantId}' and idempotency_key='${k2real}';"
    Assert-Step 'S2-nota-de-adocao' ($note2 -like '*varredura*') "notes=$note2"

    # ===================== S3: PENDING_MODEL -> configuracao -> auto =====================
    Write-Host '--- S3: sem modelo -> PENDING_MODEL (ciclo longo); publico modelo -> STARTED ---' -ForegroundColor Cyan
    $rec3 = New-HarvestRecord 3 5 15000 'C'
    $rcp3 = New-HarvestReceipt 3 $rec3 $grain2P 'C' 15000
    $k3 = Get-IntentKey $rcp3
    $p3 = Get-IntentParts $k3
    Assert-Step 'S3-PENDING_MODEL' ($p3[0] -eq 'PENDING_MODEL') "status=$(if ($p3) { $p3[0] } else { '?' })"
    $p3err = Get-IntentParts $k3
    $p3lasterr = $p3err[5]
    Assert-Step 'S3-motivo-modelo-ausente' ($p3lasterr -like '*modelo*') "last_error=$p3lasterr"

    # Sem loop apertado: empurra next_attempt_at +20 min; o worker nao pode tocar antes do vencimento.
    Invoke-Psql "update agro360.quality_inspection_event_intents set next_attempt_at = now() + interval '20 minutes' where tenant_id='${tenantId}' and idempotency_key='${k3}';" | Out-Null
    $attBase = [int](Get-IntentParts $k3)[2]
    Start-Sleep -Seconds 9
    $attAfter = [int](Get-IntentParts $k3)[2]
    Assert-Step 'S3-gate-next_attempt_at' ($attAfter -eq $attBase) "attempts $attBase->$attAfter apos 9s com proxima tentativa +20min (sem loop apertado)"
    Invoke-Psql "update agro360.quality_inspection_event_intents set next_attempt_at = now() - interval '1 second' where tenant_id='${tenantId}' and idempotency_key='${k3}';" | Out-Null

    # Publica o modelo do produto 2: a pendencia de configuracao resolve sozinha.
    Publish-InspectionModel 'M-REC-GRAO-2' 'Umididade de Soja (grao 2)' $grain2P $script:adminToken | Out-Null
    $ok3 = Wait-Until { $x = Get-IntentParts $k3; ($null -ne $x -and $x[0] -eq 'STARTED') } 30 'S3 STARTED apos publicar modelo'
    $p3b = Get-IntentParts $k3
    Assert-Step 'S3-auto-start-apos-config' ($ok3 -and $p3b[0] -eq 'STARTED' -and [int]$p3b[1].Length -ge 32) "status=$(if ($p3b) { $p3b[0] } else { '?' }) run=$(if ($p3b) { $p3b[1] } else { '?' })"
    Assert-Step 'S3-exatamente-1-run' (Get-RunCount $k3 -eq 1) "runs=$(Get-RunCount $k3)"

    # ===================== S4: falha (origem some) -> backoff -> dead-letter =====================
    Write-Host '--- S4: origem excluida -> tentativas com backoff -> DEAD_LETTERED com motivo ---' -ForegroundColor Cyan
    $rec4 = New-HarvestRecord 4 5 12000 'D'
    $rcp4 = New-HarvestReceipt 4 $rec4 $grain3P 'D' 12000
    $k4 = Get-IntentKey $rcp4
    $p4a = Get-IntentParts $k4
    Assert-Step 'S4-PENDING_MODEL-inicial' ($p4a[0] -eq 'PENDING_MODEL') "status=$(if ($p4a) { $p4a[0] } else { '?' })"
    # production_receipts e tabela de hard delete (sem deleted_at): remover a linha faz a origem virar MISSING.
    Invoke-Psql "delete from agro360.production_receipts where tenant_id='${tenantId}' and id='${rcp4}';" | Out-Null
    $ok4 = Wait-Until { $x = Get-IntentParts $k4; ($null -ne $x -and $x[0] -eq 'DEAD_LETTERED') } 45 'S4 dead-letter'
    $p4 = Get-IntentParts $k4
    Assert-Step 'S4-DEAD_LETTERED' ($ok4 -and $p4[0] -eq 'DEAD_LETTERED') "status=$(if ($p4) { $p4[0] } else { '?' })"
    Assert-Step 'S4-error-code' ($p4[3] -eq 'inspection.origin_missing') "error_code=$(if ($p4) { $p4[3] } else { '?' })"
    Assert-Step 'S4-last-error-preenchido' ($p4[5].Trim().Length -gt 0) "last_error=$(if ($p4) { $p4[5] } else { '?' })"
    Assert-Step 'S4-attempts-registradas' ([int]$p4[2] -ge 2) "attempts=$(if ($p4) { $p4[2] } else { '?' }) (backoff progressivo ate o limite)"
    $dlAt = Invoke-Psql "select count(*)::text from agro360.quality_inspection_event_intents where tenant_id='${tenantId}' and idempotency_key='${k4}' and dead_lettered_at is not null;"
    Assert-Step 'S4-dead-lettered-at' ($dlAt -eq '1') "marcador persistido"

    # Permissao: operador (sem compliance.approve) nao re-dispara.
    $intent4Id = Invoke-Psql "select id::text from agro360.quality_inspection_event_intents where tenant_id='${tenantId}' and idempotency_key='${k4}';"
    $r403 = Invoke-Api POST "/api/inspections/event-intents/${intent4Id}/redispatch" @{ Reason = 'operador sem permissao' } $opToken
    Assert-Step 'S4-operator-403-redispatch' ($r403.Status -eq 403) "HTTP $($r403.Status) $($r403.Raw)"

    # ===================== S5: re-disparo manual auditado + recuperacao =====================
    Write-Host '--- S5: redispatch (compliance.approve) zera, audita e processa; origem volta -> STARTED ---' -ForegroundColor Cyan
    $r5 = Invoke-Api POST "/api/inspections/event-intents/${intent4Id}/redispatch" @{ Reason = 'E2E: reprocessamento apos verificacao da origem' } $script:adminToken
    Assert-Step 'S5-redispatch-aceito' ($r5.Status -eq 200) "HTTP $($r5.Status) $($r5.Raw)"
    $audit = Invoke-Psql "select coalesce(action,'') || '|' || coalesce(entity_type,'') from agro360.audit_logs where tenant_id='${tenantId}' and entity_type='quality_inspection_event_intent' and action='INSPECTION_INTENT_REDISPATCH' and entity_id=cast('${intent4Id}' as uuid) order by occurred_at desc limit 1;"
    Assert-Step 'S5-auditoria-gravada' ($audit -eq 'INSPECTION_INTENT_REDISPATCH|quality_inspection_event_intent') "audit_row=$audit"
    $p5a = Get-IntentParts $k4
    Assert-Step 'S5-contagem-zero' ($p5a[2] -eq '1') "attempts=$($p5a[2]) (reset 0 + nova tentativa imediata; origem ainda ausente)"
    Assert-Step 'S5-status-recuperavel' ($p5a[0] -eq 'PENDING') "status=$($p5a[0]) (falha re-agendada, nao erro para o cliente)"

    # Restaura a MESMA origem (mesmo id e chave) para provar que a recuperacao reabre o ciclo.
    Invoke-Psql @"
insert into agro360.production_receipts(id,tenant_id,harvest_record_id,warehouse_id,product_id,received_at,received_quantity,unit,lot_number,entry_mode,notes,idempotency_key,request_hash,created_by)
values('${rcp4}','${tenantId}','${rec4}','${whId}','${grain3P}',now(),12000,'kg','LOTE-R4-${runTag}D','MANUAL','Restauracao apos dead-letter E2E','rcpt-r4${runTag}D',substr(md5(gen_random_uuid()::text)||md5('rcpt-r4${runTag}D')||md5('e2e-restore'),1,64),'${adminUser}');
"@ | Out-Null
    Publish-InspectionModel 'M-REC-GRAO-3' 'Umididade de Soja (grao 3)' $grain3P $script:adminToken | Out-Null
    $ok5 = Wait-Until { $x = Get-IntentParts $k4; ($null -ne $x -and $x[0] -eq 'STARTED') } 30 'S5 STARTED apos restaurar origem'
    $p5 = Get-IntentParts $k4
    Assert-Step 'S5-STARTED-final' ($ok5 -and $p5[0] -eq 'STARTED' -and [int]$p5[1].Length -ge 32) "status=$(if ($p5) { $p5[0] } else { '?' }) run=$(if ($p5) { $p5[1] } else { '?' })"
    Assert-Step 'S5-exatamente-1-run' (Get-RunCount $k4 -eq 1) "runs=$(Get-RunCount $k4)"

    # ===================== S6: dois workers -> exatamente um efeito =====================
    Write-Host '--- S6: dois workers concorrentes -> 1 intent e 1 run por origem ---' -ForegroundColor Cyan
    $w2 = Start-RecoveryWorker 'worker2'
    $rec5a = New-HarvestRecord 5 5 11000 'E1'
    $rec5b = New-HarvestRecord 5 5 11000 'E2'
    $d5a = New-DirectReceipt $rec5a $grainP "LOTE-WA-$runTag" 11000 'Concorrencia: dois workers.'
    $d5b = New-DirectReceipt $rec5b $grainP "LOTE-WB-$runTag" 11000 'Concorrencia: dois workers.'
    $k5areal = $d5a.Key
    $k5breal = $d5b.Key
    $ok6a = Wait-Until { $x = Get-IntentParts $k5areal; ($null -ne $x -and $x[0] -eq 'STARTED') } 30 'S6 a STARTED'
    $ok6b = Wait-Until { $x = Get-IntentParts $k5breal; ($null -ne $x -and $x[0] -eq 'STARTED') } 30 'S6 b STARTED'
    Assert-Step 'S6-a-STARTED' ($ok6a) ''
    Assert-Step 'S6-b-STARTED' ($ok6b) ''
    Assert-Step 'S6-a-intent-unico' (Get-IntentCount $k5areal -eq 1) "intents=$(Get-IntentCount $k5areal)"
    Assert-Step 'S6-a-run-unico' (Get-RunCount $k5areal -eq 1) "runs=$(Get-RunCount $k5areal)"
    Assert-Step 'S6-b-intent-unico' (Get-IntentCount $k5breal -eq 1) "intents=$(Get-IntentCount $k5breal)"
    Assert-Step 'S6-b-run-unico' (Get-RunCount $k5breal -eq 1) "runs=$(Get-RunCount $k5breal)"
    Stop-Proc $w2

    # ===================== S7: reinicio do worker (lease expirado) =====================
    Write-Host '--- S7: worker morre com lease ativo; novo processo reclama e processa ---' -ForegroundColor Cyan
    $rec7 = New-HarvestRecord 6 5 9000 'F'
    $d7 = New-DirectReceipt $rec7 $grain4P "LOTE-RT-$runTag" 9000 'Reinicio: origem para o teste de lease.'
    $k7real = $d7.Key
    $ok7a = Wait-Until { $x = Get-IntentParts $k7real; ($null -ne $x -and $x[0] -eq 'PENDING_MODEL') } 30 'S7 adocao inicial'
    Assert-Step 'S7-adotado-PENDING_MODEL' ($ok7a) "status=$(if ((Get-IntentParts $k7real)) { (Get-IntentParts $k7real)[0] } else { '?' })"

    # Simula worker que morreu segurando o claim: lease vencido, contador parado.
    Invoke-Psql "update agro360.quality_inspection_event_intents set claimed_at = now() - interval '10 minutes', claimed_by = 'dead-worker-e2e', lease_expires_at = now() - interval '1 second', next_attempt_at = now() - interval '1 second' where tenant_id='${tenantId}' and idempotency_key='${k7real}';" | Out-Null
    $att7base = [int](Get-IntentParts $k7real)[2]
    Stop-Proc $w1
    $w3 = Start-RecoveryWorker 'worker3'
    $ok7b = Wait-Until {
        $pp = Get-IntentParts $k7real
        $pp -and [int]$pp[2] -gt $att7base -and $pp[4] -ne 'dead-worker-e2e'
    } 35 'S7 reclaim apos reinicio'
    $p7 = Get-IntentParts $k7real
    Assert-Step 'S7-reclamado-por-novo-processo' ($ok7b -and $p7[4] -ne 'dead-worker-e2e') "claimed_by=$(if ($p7) { $p7[4] } else { '?' })"
    Assert-Step 'S7-trabalho-avançou' ([int]$p7[2] -gt $att7base) "attempts ${att7base}->$($p7[2]) (nenhum pendente perdido no restart)"
    Assert-Step 'S7-lease-liberado-pos-processamento' ($p7[4] -eq '') "claim liberado apos o resultado"

    # ============ EVIDENCIA FINAL ============
    $snapshot = Invoke-Psql "select process_code, origin_type, status, attempts, coalesce(error_code,'-'), left(coalesce(last_error,'-'),80), next_attempt_at > now() as proxima_futuro, dead_lettered_at is not null as dead from agro360.quality_inspection_event_intents where tenant_id='${tenantId}' and deleted_at is null order by created_at;"
    $snapshot | Set-Content (Join-Path $evidenceDir 'intents-final-snapshot.txt')
    $runsSnapshot = Invoke-Psql "select count(*)::text from agro360.quality_inspection_runs where tenant_id='${tenantId}' and deleted_at is null;"
    $auditSnapshot = Invoke-Psql "select count(*)::text from agro360.audit_logs where tenant_id='${tenantId}' and entity_type='quality_inspection_event_intent' and action='INSPECTION_INTENT_REDISPATCH';"
    Write-Host ("Evidencia final: snapshot de intents salvo em intents-final-snapshot.txt; runs no tenant={0}; auditorias de re-disparo={1}" -f $runsSnapshot.Trim(), $auditSnapshot.Trim()) -ForegroundColor DarkGray

    Stop-Proc $w3
    Stop-Proc $apiProc
    if (-not $KeepRunning -and $weStartedCluster) { & $pg_ctl -D $templateDir -m fast stop | Out-Null }
    Write-Host ""
    Write-Host "E2E DE RECUPERACAO DE INSPECOES: TODOS OS CENARIOS PASSARAM" -ForegroundColor Green
    Write-Host "Evidencia: $evidenceDir" -ForegroundColor Green
}
catch {
    Write-Host ""
    Write-Host "E2E DE RECUPERACAO DE INSPECOES: FALHOU" -ForegroundColor Red
    Write-Host "$_" -ForegroundColor Red
    Write-Host "Evidencia: $evidenceDir" -ForegroundColor Red
    Stop-Proc $w1; Stop-Proc $w2; Stop-Proc $w3; Stop-Proc $apiProc
    if (-not $KeepRunning -and $weStartedCluster) { try { & $pg_ctl -D $templateDir -m fast stop | Out-Null } catch {} }
    exit 1
}
finally {
    Stop-Transcript | Out-Null
}
