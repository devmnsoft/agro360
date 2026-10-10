param(
    [string]$PostgresBin = 'C:\Program Files\PostgreSQL\18\bin',
    [switch]$KeepRunning
)

# Homologacao E2E - idiomas (fr-FR) + isolamento de fazenda no mesmo tenant (Santa Clara):
#   BLOCO L: fr-FR ativo no catalogo canonico, CHECKs dos templates aceitam a cultura,
#            PUT/GET preferencia de idioma, persistencia apos re-login, fallback pt-BR para cultura invalida.
#   BLOCO F: usuario com escopo FARM somente na Fazenda A (SC-SEDE-001) NAO enxerga/lista/detalha/
#            altera/recebe/exporta/consome indicadores da Fazenda B (SC-RETIRO-001), inclusive com
#            propertyId divergente no corpo e contexto X-Farm-ID divergente.

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

$evidenceDir = Join-Path $root ('artifacts\language-farm-isolation-e2e-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidenceDir | Out-Null
$processes = [Collections.Generic.List[Diagnostics.Process]]::new()
$startedDatabase = $false
$dataDir = ''
$dbName = 'agro360_langfarm_' + [guid]::NewGuid().ToString('N').Substring(0, 12)
$cleanDbName = 'agro360_langfarm_clean_' + [guid]::NewGuid().ToString('N').Substring(0, 12)

# Garante que nenhum psql fique pendurado para sempre em connect (libpq connection timeout).
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
    $tempFile = Join-Path $evidenceDir ("cmd-" + [guid]::NewGuid().ToString('N') + ".sql")
    [System.IO.File]::WriteAllText($tempFile, $Sql, [System.Text.Encoding]::UTF8)
    $output = cmd.exe /c "`"$psql`" -p $pgPort -U postgres -h 127.0.0.1 -d $Db -X -A -t -q -v ON_ERROR_STOP=1 --set=client_min_messages=warning -f `"$tempFile`" 2>&1"
    $code = $LASTEXITCODE
    Remove-Item $tempFile -Force -ErrorAction SilentlyContinue
    if ($code -ne 0) { throw "psql failed (exit code $code): $output" }
    # Array multi-linha nativa deve ser unida por quebra de linha (e nao por $OFS/espaco),
    # para que consumidores facam split em "`n" de forma confiavel.
    return (($output -join "`n")).Trim()
}

function Invoke-PsqlFile([string]$FilePath, [string]$Db = $dbName) {
    $output = cmd.exe /c "`"$psql`" -p $pgPort -U postgres -h 127.0.0.1 -d $Db -X -q -v ON_ERROR_STOP=1 --set=client_min_messages=warning -f `"$FilePath`" 2>&1"
    $code = $LASTEXITCODE
    if ($code -ne 0) { throw "psql file failed (exit code $code): $output" }
    return (($output -join "`n")).Trim()
}

function Invoke-Api([string]$Method, [string]$Path, $Body = $null, [string]$Token = '', [hashtable]$ExtraHeaders = @{}) {
    $url = "http://127.0.0.1:${apiPort}${Path}"
    $headers = @{ 'Content-Type' = 'application/json' }
    if ($Token) { $headers['Authorization'] = "Bearer $Token" }
    foreach ($k in $ExtraHeaders.Keys) { $headers[$k] = $ExtraHeaders[$k] }
    $params = @{ Method = $Method; Uri = $url; Headers = $headers; UseBasicParsing = $true; TimeoutSec = 30 }
    if ($null -ne $Body) {
        $json = $Body | ConvertTo-Json -Depth 6 -Compress
        $params['Body'] = [Text.Encoding]::UTF8.GetBytes($json)
    }
    try {
        $resp = Invoke-WebRequest @params
        $content = $resp.Content
        # Respostas nao-JSON (ex.: exportacao CSV text/csv) nao podem passar por ConvertFrom-Json;
        # senao o erro de parse cai no catch e a chamada 200 vira excecao falsa.
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

try {
    # ============ BLOCK 0: SETUP — Isolated PostgreSQL Cluster ============
    Write-Host "Iniciando cluster PostgreSQL descartavel isolado..." -ForegroundColor Cyan
    $pgPort = Get-FreePort
    $dataDir = Join-Path $evidenceDir 'pgdata'
    $initOut = & $initdb -D $dataDir -U postgres -A trust --encoding=UTF8 --locale=C 2>&1
    if ($LASTEXITCODE -ne 0) { throw "initdb falhou: $initOut" }
    $pgLog = Join-Path $evidenceDir 'postgres.log'
    & $pg_ctl -D $dataDir -l $pgLog -o "-h 127.0.0.1 -p $pgPort" -w start
    if ($LASTEXITCODE -ne 0) { throw "pg_ctl start falhou na porta $pgPort" }
    $startedDatabase = $true
    cmd.exe /c "`"$psql`" -p $pgPort -U postgres -h 127.0.0.1 -d postgres -c `"create database ${cleanDbName};`" 2>&1" | Out-Null
    $fullSqlPath = Join-Path $root 'database/agro360-postgres-full.sql'
    Invoke-PsqlFile $fullSqlPath $cleanDbName
    cmd.exe /c "`"$psql`" -p $pgPort -U postgres -h 127.0.0.1 -d postgres -c `"create database ${dbName} template ${cleanDbName};`" 2>&1" | Out-Null
    Write-Host "PostgreSQL pronto na porta $pgPort" -ForegroundColor Cyan

    $schemaVer = Invoke-Psql "select version from agro360.platform_schema_versions where version='11.24.0';"
    Assert-Step 'Schema-11.24.0-present' ($schemaVer -eq '11.24.0') "migracao 134 (fr-FR) instalada"
    $schemaVerPrev = Invoke-Psql "select version from agro360.platform_schema_versions where version='11.17.0';"
    Assert-Step 'Schema-chain-intact' ($schemaVerPrev -eq '11.17.0') "versoes anteriores preservadas"

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

    # Fixtures de credencial conhecidas (hash PBKDF2 ja validado neste repositorio)
    $knownHash = 'pbkdf2-sha512$210000$Fk496Qko66Cw//8uYyP3mw==$PqcZJTtl+96es6GmUF3khyrZvMJqSVDIe/YfI7TCv2U='
    $scTenantId = Invoke-Psql "select id::text from agro360.tenancy_tenants where slug='santa-clara';"
    Invoke-Psql "update agro360.identity_users set status='ACTIVE', password_hash='$knownHash', must_change_password=false, updated_at=now() where tenant_id='${scTenantId}' and lower(email) in ('admin@santaclara.agro360.local','operador.santaclara@agro360.local');" | Out-Null

    $adminLogin = Invoke-Api POST '/api/v1/auth/login' @{ TenantSlug='santa-clara'; Email='admin@santaclara.agro360.local'; Password='Agro360Admin@2024!' }
    Assert-Step 'Login-admin' ($adminLogin.Status -eq 200) "HTTP $($adminLogin.Status)"
    $adminToken = $adminLogin.Body.accessToken
    Assert-Step 'JWT-admin' ($null -ne $adminToken -and $adminToken.Length -gt 50) "token length=$($adminToken.Length)"

    # ============ BLOCK L: CATALOG CANONICO DE IDIOMAS + PREFERENCIA PERSISTIDA ============
    Write-Host "`n--- BLOCK L: fr-FR na cadeia canonica de idiomas ---" -ForegroundColor Cyan

    $activeCultures = (Invoke-Psql "select culture from agro360.platform_languages where active order by culture;" ) -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ }
    foreach ($expected in @('en-US', 'fr-FR', 'pt-BR', 'es-ES')) {
        Assert-Step "Catalogo-$expected-ativo" (@($activeCultures) -contains $expected) "ativas: $($activeCultures -join ', ')"
    }

    # Nao pode restar nenhum CHECK antigo de cultura sem fr-FR nas 5 tabelas restritas.
    $staleChecks = Invoke-Psql @"
select count(*) from pg_constraint c
where c.contype = 'c'
  and pg_get_constraintdef(c.oid) ilike '%culture%'
  and pg_get_constraintdef(c.oid) like '%pt-BR%'
  and pg_get_constraintdef(c.oid) not like '%fr-FR%'
  and c.conrelid::regclass::text in (
      'agro360.platform_developer_docs','agro360.operations_notification_templates',
      'agro360.ui_contextual_help','agro360.ui_message_templates','agro360.ui_action_confirmations');
"@
    Assert-Step 'Checks-sem-cultura-antiga' ($staleChecks -eq '0') "CHECKs antigos restantes=$staleChecks"

    # Prova viva: o template persistido aceita fr-FR (insert + delete transacionalmente verificaveis).
    $probeOk = Invoke-Psql @"
insert into agro360.ui_message_templates(id,tenant_id,code,module,culture,message_type,title,message,status,created_at,updated_at)
values (gen_random_uuid(),'${scTenantId}','e2e-fr-probe','procurement','fr-FR','INFO','Probe E2E','Probe E2E fr-FR','ACTIVE',now(),now());
delete from agro360.ui_message_templates where tenant_id='${scTenantId}' and code='e2e-fr-probe' and culture='fr-FR';
select 'ok';
"@
    Assert-Step 'ui_message_templates-frFR-aceito' ($probeOk -match 'ok') "probe insert/delete fr-FR executado"

    # Preferencia: default pt-BR -> fr-FR confirmado pelo servidor -> sobrevive ao re-login -> fallback de cultura invalida.
    Assert-Step 'Login-idioma-default-ptBR' ($adminLogin.Body.language -eq 'pt-BR') "linguagem resolvida no login=$($adminLogin.Body.language)"

    # Estado operacional nao pode mudar por causa do idioma.
    $propsBefore = Invoke-Api GET '/api/v1/properties' $null $adminToken
    Assert-Step 'Props-linha-baseline' ($propsBefore.Status -eq 200) "HTTP $($propsBefore.Status)"
    $propsBaseline = ($propsBefore.Body | ConvertTo-Json -Depth 8)

    $getLang = Invoke-Api GET '/api/v1/auth/preferences/language' $null $adminToken
    Assert-Step 'Preferencia-get-initial' ($getLang.Status -eq 200 -and $getLang.Body.language -eq 'pt-BR') "HTTP $($getLang.Status) lang=$($getLang.Body.language)"

    $putFr = Invoke-Api PUT '/api/v1/auth/preferences/language' @{ Language='fr-FR' } $adminToken
    Assert-Step 'Preferencia-put-frFR-200' ($putFr.Status -eq 200) "HTTP $($putFr.Status)"
    Assert-Step 'Preferencia-put-frFR-confirmado' ($putFr.Body.language -eq 'fr-FR') "resposta do servidor=$($putFr.Body.language)"

    $getLang2 = Invoke-Api GET '/api/v1/auth/preferences/language' $null $adminToken
    Assert-Step 'Preferencia-get-frFR-persistida' ($getLang2.Status -eq 200 -and $getLang2.Body.language -eq 'fr-FR') "lang=$($getLang2.Body.language)"

    $prefsRow = Invoke-Psql "select coalesce(language,'(nenhuma)') from agro360.platform_user_preferences where tenant_id='${scTenantId}' and user_id=(select id from agro360.identity_users where tenant_id='${scTenantId}' and lower(email)='admin@santaclara.agro360.local');"
    Assert-Step 'Preferencia-row-ptBR-table' ($prefsRow -eq 'fr-FR') "platform_user_preferences.language=$prefsRow"

    $propsAfter = Invoke-Api GET '/api/v1/properties' $null $adminToken
    $propsAfterJson = ($propsAfter.Body | ConvertTo-Json -Depth 8)
    Assert-Step 'Idioma-nao-altera-dados' ($propsBaseline -eq $propsAfterJson) "lista de fazendas identica antes/depois de fr-FR"

    $relogin = Invoke-Api POST '/api/v1/auth/login' @{ TenantSlug='santa-clara'; Email='admin@santaclara.agro360.local'; Password='Agro360Admin@2024!' }
    Assert-Step 'Relogin-idioma-frFR-sobrevive' ($relogin.Status -eq 200 -and $relogin.Body.language -eq 'fr-FR') "login voltou linguagem=$($relogin.Body.language)"
    $adminToken = $relogin.Body.accessToken

    $putInvalid = Invoke-Api PUT '/api/v1/auth/preferences/language' @{ Language='de-DE' } $adminToken
    Assert-Step 'Cultura-invalida-fallback' ($putInvalid.Status -eq 200 -and $putInvalid.Body.language -eq 'pt-BR') "HTTP $($putInvalid.Status) lang=$($putInvalid.Body.language) (de-DE fora do catalogo volta ao pt-BR)"
    $getLang3 = Invoke-Api GET '/api/v1/auth/preferences/language' $null $adminToken
    Assert-Step 'Fallback-persistido' ($getLang3.Body.language -eq 'pt-BR') "lang=$($getLang3.Body.language)"

    # ============ BLOCK F: ISOLAMENTO DE FAZENDA NO MESMO TENANT ============
    Write-Host "`n--- BLOCK F: Duas fazendas no mesmo tenant (escopo FARM A x B) ---" -ForegroundColor Cyan

    $farmA = Invoke-Psql "select id::text from agro360.geo_farms where tenant_id='${scTenantId}' and registration_number='SC-SEDE-001' and deleted_at is null;"
    $farmB = Invoke-Psql "select id::text from agro360.geo_farms where tenant_id='${scTenantId}' and registration_number='SC-RETIRO-001' and deleted_at is null;"
    Assert-Step 'Fazendas-fixtures' ($farmA -ne '' -and $farmB -ne '' -and $farmA -ne $farmB) "A=$farmA B=$farmB"

    $operatorUser = Invoke-Psql "select id::text from agro360.identity_users where tenant_id='${scTenantId}' and lower(email)='operador.santaclara@agro360.local' and deleted_at is null;"
    Assert-Step 'Operador-existe' ($operatorUser -ne '') "user=$operatorUser"

    # O papel operador recebe as permissoes de compras apenas nesta homologacao (banco descartavel).
    Invoke-Psql @"
insert into agro360.identity_role_permissions(tenant_id, role_id, permission_id)
select '${scTenantId}', r.id, p.id
from agro360.identity_roles r
cross join agro360.identity_permissions p
where r.tenant_id='${scTenantId}' and r.code='operator'
  and p.code in ('purchasing.read','purchasing.write','purchasing.request','purchasing.approve','purchasing.receive','purchasing.export')
on conflict do nothing;
"@ | Out-Null

    # Escopo do operador: SOMENTE a Fazenda A (substitui o ALL padrao do fixture).
    $scopeId = [guid]::NewGuid().ToString()
    Invoke-Psql "delete from agro360.identity_user_unit_scopes where tenant_id='${scTenantId}' and user_id='${operatorUser}';" | Out-Null
    Invoke-Psql "insert into agro360.identity_user_unit_scopes(id, tenant_id, user_id, scope_type, farm_id) values ('${scopeId}', '${scTenantId}', '${operatorUser}', 'FARM', '${farmA}');" | Out-Null
    # farm_id precisa de funcao agregada junto ao count(*); min(farm_id::text) porque min(uuid) nao existe;
    # o cast preserva a ordem lexicografica adequada para comparar com o guid esperado.
    $scopeCheck = Invoke-Psql "select count(*) || ':' || coalesce(min(farm_id::text),'(null)') from agro360.identity_user_unit_scopes where tenant_id='${scTenantId}' and user_id='${operatorUser}';"
    Assert-Step 'Escopo-operador-FARM-A' ($scopeCheck -eq "1:$farmA") "escopos=$scopeCheck"

    $opLogin = Invoke-Api POST '/api/v1/auth/login' @{ TenantSlug='santa-clara'; Email='operador.santaclara@agro360.local'; Password='Agro360Admin@2024!' }
    Assert-Step 'Login-operador' ($opLogin.Status -eq 200) "HTTP $($opLogin.Status)"
    $opToken = $opLogin.Body.accessToken

    # Fixtures operacionais criados pelo admin (escopo ALL): catalogo, fornecedor, pedidos de compra A/B e ordem B.
    $catalogItem = Invoke-Api POST '/api/procurement/catalog' @{
        Name = 'Diesel S10 (E2E isolamento)'; Code = 'E2E-DIESEL-ISOL'; Category = 'COMBUSTIVEIS'; Unit = 'L'; Type = 'SERVICE'
        Description = $null; Active = $true; MinimumStock = 0; CostCenterId = $null
        RequiresLot = $false; RequiresExpiry = $false; RequiresDocument = $false; RequiresInspection = $false; RequiresApprovedSupplier = $false; Notes = $null
    } $adminToken
    Assert-Step 'Fixture-catalogo' ($catalogItem.Status -eq 201) "HTTP $($catalogItem.Status)"
    $catalogItemId = $catalogItem.Body.id

    $supplier = Invoke-Api POST '/api/procurement/suppliers' @{
        LegalName = 'Fornecedor Isolacao E2E Ltda'; TradeName = 'FornIsolE2E'; TaxDocument = $null; StateRegistration = $null; Type = 'MATERIAL'
        Category = 'GENERAL'; Email = $null; Phone = $null; Address = $null; City = 'Paragominas'; State = 'PA'; Country = 'BR'
        MainContact = $null; PaymentTerms = $null; AverageDeliveryDays = 5; Status = 'ACTIVE'; RejectionReason = $null; Notes = $null; Tags = @()
    } $adminToken
    Assert-Step 'Fixture-fornecedor' ($supplier.Status -eq 201) "HTTP $($supplier.Status)"
    $supplierId = $supplier.Body.id

    $neededOn = (Get-Date).AddDays(30).ToString('yyyy-MM-dd')
    $reqA = Invoke-Api POST '/api/procurement/requisitions' @{
        CostCenterId = $null; PropertyId = $farmA; Origin = 'MANUAL'
        Justification = 'Requisicao E2E isolacao - Fazenda A (SEDE)'; Priority = 'MEDIUM'; NeededOn = $neededOn; Submit = $false
        Items = @(@{ CatalogItemId = $catalogItemId; Quantity = 100; Unit = 'L'; Notes = $null })
    } $adminToken
    Assert-Step 'Fixture-requisicao-A' ($reqA.Status -eq 201) "HTTP $($reqA.Status) id=$($reqA.Body.id)"
    $reqAId = $reqA.Body.id

    $reqB = Invoke-Api POST '/api/procurement/requisitions' @{
        CostCenterId = $null; PropertyId = $farmB; Origin = 'MANUAL'
        Justification = 'Requisicao E2E isolacao - Fazenda B (RETIRO)'; Priority = 'MEDIUM'; NeededOn = $neededOn; Submit = $false
        Items = @(@{ CatalogItemId = $catalogItemId; Quantity = 40; Unit = 'L'; Notes = $null })
    } $adminToken
    Assert-Step 'Fixture-requisicao-B' ($reqB.Status -eq 201) "HTTP $($reqB.Status) id=$($reqB.Body.id)"
    $reqBId = $reqB.Body.id

    $orderB = Invoke-Api POST '/api/procurement/orders' @{
        SupplierId = $supplierId; RequisitionId = $null; QuotationId = $null; CostCenterId = $null; PropertyId = $farmB
        PaymentTerms = 'NET_30'; DeliveryOn = $neededOn; DeliveryAddress = 'Retiro Boa Vista - Paragominas/PA'
        Freight = 0; Taxes = 0
        Items = @(@{ CatalogItemId = $catalogItemId; Quantity = 40; Unit = 'L'; UnitPrice = 6.5; Discount = 0 })
    } $adminToken
    Assert-Step 'Fixture-ordem-B' ($orderB.Status -eq 201) "HTTP $($orderB.Status) id=$($orderB.Body.id)"
    $orderBId = $orderB.Body.id
    $orderBItem = Invoke-Psql "select id::text from agro360.procurement_purchase_order_items where tenant_id='${scTenantId}' and purchase_order_id='${orderBId}' limit 1;"
    Assert-Step 'Fixture-item-ordem-B' ($orderBItem -ne '') "item=$orderBItem"

    $reqANumber = Invoke-Psql "select number from agro360.procurement_requisitions where tenant_id='${scTenantId}' and id='${reqAId}';"
    $reqBNumber = Invoke-Psql "select number from agro360.procurement_requisitions where tenant_id='${scTenantId}' and id='${reqBId}';"

    # ---- LIST: so a Fazenda A aparece ----
    $opList = Invoke-Api GET '/api/procurement/requisitions' $null $opToken
    Assert-Step 'List-operador-200' ($opList.Status -eq 200) "HTTP $($opList.Status)"
    $opPropertyIds = @($opList.Body | ForEach-Object { $_.property_id })
    Assert-Step 'List-operador-sem-B' (-not ($opPropertyIds -contains $farmB)) "property_ids vistos: $(($opPropertyIds | Select-Object -Unique) -join ',' )"
    Assert-Step 'List-operador-com-A' ($opPropertyIds -contains $farmA) "requisicao A presente na lista"
    $opNumbers = @($opList.Body | ForEach-Object { $_.number })
    Assert-Step 'List-numeros-apenas-A' (($opNumbers -contains $reqANumber) -and -not ($opNumbers -contains $reqBNumber)) "numeros=$(($opNumbers -join ','))"

    $adminList = Invoke-Api GET '/api/procurement/requisitions' $null $adminToken
    $adminPropertyIds = @($adminList.Body | ForEach-Object { $_.property_id })
    Assert-Step 'List-admin-ve-as-duas' (($adminPropertyIds -contains $farmA) -and ($adminPropertyIds -contains $farmB)) "admin escopo ALL"

    # ---- DETAIL por ID ----
    $opDetailA = Invoke-Api GET "/api/procurement/requisitions/${reqAId}" $null $opToken
    Assert-Step 'Detail-A-autorizada' ($opDetailA.Status -eq 200) "HTTP $($opDetailA.Status)"
    $opDetailB = Invoke-Api GET "/api/procurement/requisitions/${reqBId}" $null $opToken
    Assert-Step 'Detail-B-negada' ($opDetailB.Status -eq 404) "HTTP $($opDetailB.Status) (fazenda fora do escopo nao e visivel)"
    $adminDetailB = Invoke-Api GET "/api/procurement/requisitions/${reqBId}" $null $adminToken
    Assert-Step 'Detail-B-admin-ok' ($adminDetailB.Status -eq 200) "admin enxerga B"

    # ---- CHANGE: escrita com propertyId divergente no corpo ----
    $changeB = Invoke-Api POST '/api/procurement/requisitions' @{
        CostCenterId = $null; PropertyId = $farmB; Origin = 'MANUAL'
        Justification = 'Tentativa E2E: escrita na Fazenda B sem autorizacao'; Priority = 'MEDIUM'; NeededOn = $neededOn; Submit = $false
        Items = @(@{ CatalogItemId = $catalogItemId; Quantity = 10; Unit = 'L'; Notes = $null })
    } $opToken
    Assert-Step 'Change-body-B-403' ($changeB.Status -eq 403) "HTTP $($changeB.Status) body=$(if ($changeB.Body -is [System.String]) { $changeB.Body.Substring(0, [Math]::Min(80, $changeB.Body.Length)) } else { $changeB.Body.code })"
    $changeAfter = Invoke-Psql "select count(*) from agro360.procurement_requisitions where tenant_id='${scTenantId}' and property_id='${farmB}' and justification='Tentativa E2E: escrita na Fazenda B sem autorizacao';"
    Assert-Step 'Change-body-B-sem-lado-colateral' ($changeAfter -eq '0') "linhas criadas=$changeAfter"

    # Contexto X-Farm-ID divergente: middleware nega antes do service.
    $changeCtx = Invoke-Api POST '/api/procurement/requisitions' @{
        CostCenterId = $null; PropertyId = $farmB; Origin = 'MANUAL'
        Justification = 'Tentativa E2E: contexto X-Farm-ID divergente'; Priority = 'MEDIUM'; NeededOn = $neededOn; Submit = $false
        Items = @(@{ CatalogItemId = $catalogItemId; Quantity = 10; Unit = 'L'; Notes = $null })
    } $opToken @{ 'X-Farm-ID' = $farmB }
    Assert-Step 'Change-contexto-XFarmID-B-403' ($changeCtx.Status -eq 403) "HTTP $($changeCtx.Status) type=$(if ($changeCtx.Body -isnot [System.String]) { $changeCtx.Body.type } else { '?' })"

    # Mudanca de estado por ID em entidade da Fazenda B: aprovar pedido B.
    $approveB = Invoke-Api POST "/api/procurement/orders/${orderBId}/approve" 'null' $opToken
    Assert-Step 'Change-status-pedido-B-negado' ($approveB.Status -in 403, 404) "HTTP $($approveB.Status) (pedido B fora do escopo)"
    $orderBStatus = Invoke-Psql "select status from agro360.procurement_purchase_orders where tenant_id='${scTenantId}' and id='${orderBId}';"
    Assert-Step 'Pedido-B-mantido-AWAITING_APPROVAL' ($orderBStatus -eq 'AWAITING_APPROVAL') "status=$orderBStatus"

    # ---- RECEIVE: recebimento de pedido da Fazenda B ----
    # Sem bloco DO/`$:$$:` aqui: aqui-string dupla expandiria `$$` antes do psql.
    # INSERT condicional simples (select ... where not exists) cumpre o mesmo papel, sem variaveis PL/pgSQL.
    $financeAccountId = Invoke-Psql @"
insert into agro360.finance_chart_of_accounts(id,tenant_id,code,name,type,nature,active,created_by)
select gen_random_uuid(),'${scTenantId}','E2E-FIN-ISOL','Conta E2E custo isolacao','EXPENSE','DEBIT',true,
  (select id from agro360.identity_users where tenant_id='${scTenantId}' and lower(email)='admin@santaclara.agro360.local')
where not exists(select 1 from agro360.finance_chart_of_accounts where tenant_id='${scTenantId}' and code='E2E-FIN-ISOL');
select id::text from agro360.finance_chart_of_accounts where tenant_id='${scTenantId}' and code='E2E-FIN-ISOL' limit 1;
"@
    $receiptsBefore = Invoke-Psql "select count(*) from agro360.procurement_receipts where tenant_id='${scTenantId}';"
    $receiveB = Invoke-Api POST '/api/procurement/receipts' @{
        PurchaseOrderId = $orderBId; ReceivedAt = '2026-10-09T12:00:00Z'; InvoiceDocument = $null
        OverrideExcess = $false; ExcessJustification = $null
        Items = @(@{ PurchaseOrderItemId = $orderBItem; Quantity = 5; SupplierLot = $null; ExpiresOn = $null; Notes = $null })
        IdempotencyKey = "e2e-receive-blockf-$([guid]::NewGuid().ToString('N'))"; WarehouseId = $null
        FinanceAccountId = $financeAccountId; FirstDueOn = '2026-12-31'; Installments = 1
    } $opToken
    Assert-Step 'Receive-pedido-B-negado' ($receiveB.Status -ge 400) "HTTP $($receiveB.Status) (pedido B nao e receivel para o escopo A)"
    $receiptsAfter = Invoke-Psql "select count(*) from agro360.procurement_receipts where tenant_id='${scTenantId}';"
    Assert-Step 'Receive-sem-recebimento-criado' ($receiptsAfter -eq $receiptsBefore) "antes=$receiptsBefore depois=$receiptsAfter"

    # Positivo: o proprio fluxo do operador funciona na Fazenda A.
    $reqADetail = Invoke-Api GET "/api/procurement/requisitions/${reqAId}" $null $opToken
    Assert-Step 'Positive-detalhe-A-contexto-automatico' ($reqADetail.Status -eq 200) "escopo unico FARM A aplicado como contexto"

    # ---- EXPORT: CSV respeita o escopo ----
    $opExport = Invoke-Api GET '/api/procurement/reports/requisitions.csv' $null $opToken
    Assert-Step 'Export-operador-200' ($opExport.Status -eq 200) "HTTP $($opExport.Status)"
    Assert-Step 'Export-CSV-contem-A' ($opExport.Raw -like "*${reqANumber}*") "CSV do operador tem $reqANumber"
    Assert-Step 'Export-CSV-sem-B' ($opExport.Raw -notlike "*${reqBNumber}*") "CSV do operador nao tem $reqBNumber"

    $opExportCtxB = Invoke-Api GET '/api/procurement/reports/requisitions.csv' $null $opToken @{ 'X-Farm-ID' = $farmA }
    Assert-Step 'Export-contexto-A-explicito-200' ($opExportCtxB.Status -eq 200) "HTTP $($opExportCtxB.Status)"
    Assert-Step 'Export-contexto-A-apenas-A' (($opExportCtxB.Raw -like "*${reqANumber}*") -and ($opExportCtxB.Raw -notlike "*${reqBNumber}*")) "CSV com contexto explicito A"

    # ---- INDICATORS: dashboard reflete somente a fazenda autorizada ----
    $opDash = Invoke-Api GET '/api/procurement/dashboard' $null $opToken
    Assert-Step 'Dashboard-operador-200' ($opDash.Status -eq 200) "HTTP $($opDash.Status)"
    Assert-Step 'Dashboard-operador-so-A' ([int]$opDash.Body.requisitions_draft -eq 1) "requisitions_draft=$($opDash.Body.requisitions_draft) (esperado 1: apenas a requisicao A)"
    Assert-Step 'Dashboard-operador-sem-ordem-B' ([int]$opDash.Body.orders_awaiting_approval -eq 0) "orders_awaiting_approval=$($opDash.Body.orders_awaiting_approval) (esperado 0: pedido B invisivel)"

    $adminDash = Invoke-Api GET '/api/procurement/dashboard' $null $adminToken
    Assert-Step 'Dashboard-admin-ve-as-duas' ([int]$adminDash.Body.requisitions_draft -eq 2) "admin requisitions_draft=$($adminDash.Body.requisitions_draft) (esperado 2: A+B)"
    Assert-Step 'Dashboard-admin-ordem-B' ([int]$adminDash.Body.orders_awaiting_approval -eq 1) "admin orders_awaiting_approval=$($adminDash.Body.orders_awaiting_approval) (esperado 1: pedido B)"

    # Restauracao do escopo do operador (estado limpo no banco descartavel).
    Invoke-Psql "delete from agro360.identity_user_unit_scopes where id='${scopeId}';" | Out-Null
    Invoke-Psql "insert into agro360.identity_user_unit_scopes(id, tenant_id, user_id, scope_type) values ('$(New-Guid)', '${scTenantId}', '${operatorUser}', 'ALL');" | Out-Null

    # ============ RESUMO FINAL ============
    Write-Host "`n--- RESUMO DE HOMOLOGACAO ---" -ForegroundColor Cyan
    Write-Host "Todos os blocos executados com sucesso." -ForegroundColor Green
    Write-Host "Evidencias em: $evidenceDir"

    $summary = @"
LANGUAGE + FARM ISOLATION E2E
Data/Hora: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss UTC')
PG Port: $pgPort
API Port: $apiPort
DB: $dbName
Schema: 11.24.0 (mig 134 fr-FR) + 11.17.0 preservada

BLOCO L - Idiomas (fr-FR):
  1. Catalogo platform_languages ativo: pt-BR, en-US, es-ES, fr-FR: PASS
  2. Sem CHECK antigo de cultura sem fr-FR (5 tabelas): PASS
  3. ui_message_templates aceita insert fr-FR: PASS
  4. Login default: language=pt-BR: PASS
  5. PUT preferencia fr-FR: 200 + confirmado pelo servidor: PASS
  6. GET preferencia: fr-FR persistida em platform_user_preferences: PASS
  7. Re-login: language=fr-FR sobrevive: PASS
  8. Estado operacional invariante (lista de fazendas igual antes/depois): PASS
  9. Cultura invalida (de-DE): fallback pt-BR persistido: PASS

BLOCO F - Isolamento de fazenda (mesmo tenant Santa Clara):
  1. Operador com escopo FARM somente SC-SEDE-001 (A); fixtures A+B criadas pelo admin (ALL): PASS
  2. LIST /api/procurement/requisitions: operador ve A, nunca B (ID e numero): PASS
  3. DETAIL por ID: A=200, B=404; admin ve B: PASS
  4. CHANGE: POST requisicao com propertyId=B no corpo=403 (sem lado colateral);
     mesmo com contexto X-Farm-ID=B=403; aprovar pedido B por ID negado e status mantido: PASS
  5. RECEIVE: recebimento de pedido B negado e zero recibos criados: PASS
  6. EXPORT CSV: operador exporta apenas A (com e sem X-Farm-ID=A): PASS
  7. INDICATORS dashboard: operador 1 draft/0 ordens (so A); admin 2 drafts/1 ordem (A+B): PASS
"@
    $summary | Set-Content (Join-Path $evidenceDir 'SUMMARY.txt')
    Write-Host $summary
}
finally {
    if (-not $KeepRunning) {
        foreach ($p in $processes) {
            try { if (-not $p.HasExited) { $p.Kill(); $p.WaitForExit(5000) | Out-Null } } catch {}
        }
    }
    if ($startedDatabase -and (-not $KeepRunning) -and (Test-Path $dataDir)) {
        & $pg_ctl -D $dataDir -m immediate stop 2>&1 | Out-Null
    }
}

Write-Host "`nLANGUAGE + FARM ISOLATION E2E COMPLETO COM SUCESSO" -ForegroundColor Green
exit 0
