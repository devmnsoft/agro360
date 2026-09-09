param(
    [string]$PostgresBin = 'C:\Program Files\PostgreSQL\18\bin',
    [switch]$SkipBuild
)

# Verifies the real global-admin -> tenant -> invited user -> contracted module flow.
# Every credential and database are generated for one disposable execution.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
$evidence = Join-Path $root ('artifacts\saas-admin-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidence | Out-Null
$savedEnvironment = @{}
$apiProcess = $null
$startedDatabase = $false

function Set-TaskEnvironment([string]$Name, [string]$Value) {
    if (-not $savedEnvironment.ContainsKey($Name)) {
        $savedEnvironment[$Name] = [Environment]::GetEnvironmentVariable($Name)
    }
    [Environment]::SetEnvironmentVariable($Name, $Value)
}
function Get-FreePort {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    try { return $listener.LocalEndpoint.Port } finally { $listener.Stop() }
}
function Assert-Exit([string]$Step) {
    if ($LASTEXITCODE -ne 0) { throw "$Step falhou (exit $LASTEXITCODE). Evidência: $evidence" }
    Write-Output "PASS $Step"
}
function Invoke-SqlScalar([string]$Sql) {
    $result = $Sql | & "$PostgresBin\psql.exe" -X -q -t -A -v ON_ERROR_STOP=1 2>> "$evidence\sql.err.log"
    if ($LASTEXITCODE -ne 0) { throw "SQL falhou. Evidência: $evidence" }
    return ($result | Out-String).Trim()
}
function Invoke-Api([string]$Path, [int]$Status = 200, [string]$Method = 'GET', $Body = $null, $Headers = @{}) {
    $options = @{ Uri = "$apiUrl$Path"; Method = $Method; Headers = $Headers; SkipHttpErrorCheck = $true; TimeoutSec = 20 }
    if ($null -ne $Body) { $options.Body = $Body | ConvertTo-Json -Depth 8 -Compress; $options.ContentType = 'application/json' }
    $response = Invoke-WebRequest @options
    if ([int]$response.StatusCode -ne $Status) {
        throw "$Method $Path retornou $($response.StatusCode), esperado $Status. Evidência: $evidence"
    }
    Write-Output "PASS $Method $Path -> $Status"
    return $response
}
function ConvertTo-Base32([byte[]]$Bytes) {
    $alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
    $result = [Text.StringBuilder]::new()
    $buffer = 0
    $bits = 0
    foreach ($value in $Bytes) {
        $buffer = ($buffer -shl 8) -bor $value
        $bits += 8
        while ($bits -ge 5) {
            $bits -= 5
            [void]$result.Append($alphabet[($buffer -shr $bits) -band 31])
        }
        $buffer = $buffer -band ((1 -shl $bits) - 1)
    }
    if ($bits -gt 0) { [void]$result.Append($alphabet[($buffer -shl (5 - $bits)) -band 31]) }
    return $result.ToString()
}
function Get-Totp([byte[]]$Secret) {
    $counter = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds() / 30
    $input = [BitConverter]::GetBytes([int64][Math]::Floor($counter))
    if ([BitConverter]::IsLittleEndian) { [Array]::Reverse($input) }
    $hmac = [Security.Cryptography.HMACSHA256]::new($Secret)
    try { $hash = $hmac.ComputeHash($input) } finally { $hmac.Dispose() }
    $index = $hash[$hash.Length - 1] -band 0x0f
    $value = (([int]$hash[$index] -band 0x7f) -shl 24) -bor ([int]$hash[($index + 1)] -shl 16) -bor ([int]$hash[($index + 2)] -shl 8) -bor [int]$hash[($index + 3)]
    return ($value % 1000000).ToString('D6')
}
function Start-Api {
    $dll = Join-Path $root 'src\Hosts\Agro360.Api\bin\Release\net10.0\Agro360.Api.dll'
    $script:apiProcess = Start-Process dotnet -ArgumentList @("`"$dll`"", '--urls', $apiUrl) -WorkingDirectory (Join-Path $root 'src\Hosts\Agro360.Api') -WindowStyle Hidden -PassThru -RedirectStandardOutput "$evidence\api.log" -RedirectStandardError "$evidence\api.err.log"
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        if ($script:apiProcess.HasExited) { throw "API encerrou na inicialização. Evidência: $evidence" }
        try {
            $response = Invoke-WebRequest "$apiUrl/health" -SkipHttpErrorCheck -TimeoutSec 2
            if ($response.StatusCode -eq 200) { return }
        } catch [Net.Http.HttpRequestException] {
        } catch [Threading.Tasks.TaskCanceledException] {
        }
        Start-Sleep -Milliseconds 500
    }
    throw "API não iniciou em tempo hábil. Evidência: $evidence"
}

try {
    foreach ($tool in @('initdb', 'pg_ctl', 'psql')) {
        if (-not (Test-Path "$PostgresBin\$tool.exe")) { throw "BLOCKED: $tool indisponível em PostgresBin." }
    }
    if (-not $SkipBuild) {
        dotnet build MNSOFT.Agro360.sln -c Release --no-restore > "$evidence\build.log" 2>&1
        Assert-Exit 'build Release'
    }

    $databasePort = Get-FreePort
    $apiUrl = 'http://127.0.0.1:' + (Get-FreePort)
    $databasePassword = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    Set-TaskEnvironment PGPASSWORD $databasePassword
    Set-TaskEnvironment PGHOST '127.0.0.1'
    Set-TaskEnvironment PGPORT "$databasePort"
    Set-TaskEnvironment PGUSER 'postgres'
    Set-TaskEnvironment PGDATABASE 'agro360_saas_test'

    $pipeName = 'agro360-saas-' + [guid]::NewGuid().ToString('N')
    $pipe = [IO.Pipes.NamedPipeServerStream]::new($pipeName, [IO.Pipes.PipeDirection]::Out)
    try {
        $pending = $pipe.WaitForConnectionAsync()
        $init = Start-Process "$PostgresBin\initdb.exe" -ArgumentList @('-D', "`"$evidence\data`"", '-U', 'postgres', '--auth=scram-sha-256', "--pwfile=\\.\pipe\$pipeName", '--encoding=UTF8', '--locale=C') -WindowStyle Hidden -PassThru -RedirectStandardOutput "$evidence\initdb.log" -RedirectStandardError "$evidence\initdb.err.log"
        if (-not $pending.Wait(15000)) { throw 'initdb não abriu o canal de credencial.' }
        $writer = [IO.StreamWriter]::new($pipe)
        try { $writer.WriteLine($databasePassword) } finally { $writer.Dispose() }
        $init.WaitForExit()
        if ($init.ExitCode -ne 0) { throw "initdb falhou. Evidência: $evidence" }
    } finally { $pipe.Dispose() }
    & "$PostgresBin\pg_ctl.exe" -D "$evidence\data" -l "$evidence\postgres.log" -o "-h 127.0.0.1 -p $databasePort" -w start
    Assert-Exit 'start PostgreSQL isolado'
    $startedDatabase = $true
    Set-TaskEnvironment PGDATABASE 'postgres'
    [void](Invoke-SqlScalar 'create database agro360_saas_test;')
    Set-TaskEnvironment PGDATABASE 'agro360_saas_test'
    & "$PostgresBin\psql.exe" -X -v ON_ERROR_STOP=1 -f database/agro360-postgres-full.sql > "$evidence\install.log" 2>&1
    Assert-Exit 'instalação SQL limpa'
    & "$PostgresBin\psql.exe" -X -v ON_ERROR_STOP=1 -f database/agro360-postgres-full.sql > "$evidence\reinstall.log" 2>&1
    Assert-Exit 'reexecução SQL idempotente'
    if ((Invoke-SqlScalar "select count(*) from agro360.identity_users where tenant_id='00000000-0000-0000-0000-000000000001';") -ne '0') {
        throw 'O instalador não pode semear credencial global.'
    }
    Write-Output 'PASS instalador sem credencial global'

    $connection = "Host=127.0.0.1;Port=$databasePort;Database=agro360_saas_test;Username=postgres;Password=$databasePassword"
    $initialPassword = 'Ini1!' + [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
    $newPassword = 'New2!' + [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
    $tenantAdminPassword = 'Adm3!' + [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
    $restrictedPassword = 'Usr4!' + [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
    $totpBytes = [Security.Cryptography.RandomNumberGenerator]::GetBytes(20)
    Set-TaskEnvironment ConnectionStrings__Agro360 $connection
    Set-TaskEnvironment ASPNETCORE_ENVIRONMENT 'Development'
    Set-TaskEnvironment Jwt__SigningKey ([Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48)))
    Set-TaskEnvironment Cors__AllowedOrigins__0 'http://127.0.0.1:1'
    Set-TaskEnvironment Bootstrap__Enabled 'false'
    Set-TaskEnvironment DataProtection__KeysPath "$evidence\keys"
    Set-TaskEnvironment SuperAdmin__Email 'root.verify@agro360.local'
    Set-TaskEnvironment SuperAdmin__Password $initialPassword
    Set-TaskEnvironment SuperAdmin__TotpSecret (ConvertTo-Base32 $totpBytes)
    Start-Api
    [void](Invoke-Api '/health')

    $firstLogin = @{ tenantSlug = 'agro360-platform'; email = 'root.verify@agro360.local'; password = $initialPassword; mfaCode = Get-Totp $totpBytes }
    [void](Invoke-Api '/api/v1/auth/login' 400 'POST' $firstLogin)
    $firstLogin.newPassword = $newPassword
    $global = (Invoke-Api '/api/v1/auth/login' 200 'POST' $firstLogin).Content | ConvertFrom-Json
    if ($global.roles -notcontains 'SUPER_ADMIN' -or $global.permissions -notcontains 'platform.admin') { throw 'Autoridade global não foi emitida.' }
    Write-Output 'PASS primeiro acesso global exige troca, MFA e autoridade persistida'
    $globalHeaders = @{ Authorization = 'Bearer ' + $global.accessToken }
    $plans = (Invoke-Api '/api/platform/plans').Content | ConvertFrom-Json
    $plan = $plans | Where-Object name -eq 'Profissional' | Select-Object -First 1
    $otherPlan = $plans | Where-Object name -eq 'Cooperativa' | Select-Object -First 1
    if ($null -eq $plan -or $null -eq $otherPlan) { throw 'Planos estruturais não encontrados.' }

    $tenantCommand = @{ slug='verificacao-saas'; name='Fazenda Verificação SaaS'; type='PRODUCER'; document='16899535009'; responsibleName='Administradora Verificação'; responsibleEmail='admin.verify@agro360.local'; planId=$plan.id }
    $created = (Invoke-Api '/api/platform/tenants' 201 'POST' $tenantCommand $globalHeaders).Content | ConvertFrom-Json
    if ($created.administratorInvitation.deliveryStatus -ne 'PENDING_PROVIDER') { throw 'Convite não declarou entrega pendente.' }
    $acceptAdmin = @{ token=$created.administratorInvitation.activationToken; name='Administradora Verificação'; password=$tenantAdminPassword }
    [void](Invoke-Api '/api/invitations/accept' 200 'POST' $acceptAdmin)
    [void](Invoke-Api '/api/invitations/accept' 401 'POST' $acceptAdmin)
    [void](Invoke-Api "/api/platform/tenants/$($created.id)/activate" 204 'POST' @{ reason='Onboarding verificado automaticamente' } $globalHeaders)

    $admin = (Invoke-Api '/api/v1/auth/login' 200 'POST' @{ tenantSlug='verificacao-saas'; email='admin.verify@agro360.local'; password=$tenantAdminPassword }).Content | ConvertFrom-Json
    $adminHeaders = @{ Authorization = 'Bearer ' + $admin.accessToken }
    foreach ($permission in @('account.users.manage','account.roles.manage','account.invitations.manage','properties.read')) {
        if ($admin.permissions -notcontains $permission) { throw "Permissão ausente no administrador: $permission" }
    }
    [void](Invoke-Api '/api/account/organization' 200 'GET' $null $adminHeaders)
    [void](Invoke-Api '/api/v1/properties?page=1&pageSize=20' 200 'GET' $null $adminHeaders)
    $role = (Invoke-Api '/api/roles' 201 'POST' @{ name='Consulta de propriedades'; level=10; permissions=@('properties.read') } $adminHeaders).Content | ConvertFrom-Json
    [void](Invoke-Api '/api/roles' 403 'POST' @{ name='Escalação indevida'; level=10; permissions=@('platform.admin') } $adminHeaders)
    $invitation = (Invoke-Api '/api/invitations' 201 'POST' @{ email='viewer.verify@agro360.local'; roleId=$role.id; validForHours=24 } $adminHeaders).Content | ConvertFrom-Json
    [void](Invoke-Api '/api/invitations/accept' 200 'POST' @{ token=$invitation.activationToken; name='Pessoa Restrita'; password=$restrictedPassword })
    $restricted = (Invoke-Api '/api/v1/auth/login' 200 'POST' @{ tenantSlug='verificacao-saas'; email='viewer.verify@agro360.local'; password=$restrictedPassword }).Content | ConvertFrom-Json
    $restrictedHeaders = @{ Authorization = 'Bearer ' + $restricted.accessToken }
    [void](Invoke-Api '/api/v1/properties?page=1&pageSize=20' 200 'GET' $null $restrictedHeaders)
    [void](Invoke-Api '/api/users' 403 'GET' $null $restrictedHeaders)
    [void](Invoke-Api '/api/platform/tenants' 403 'GET' $null $restrictedHeaders)
    [void](Invoke-Api "/api/users/30000000-0000-0000-0000-000000000003/deactivate" 404 'POST' @{ reason='Teste de isolamento entre clientes' } $adminHeaders)

    $planBody = @{ name=$plan.name; description=$plan.description; monthlyPrice=$plan.monthlyPrice; annualPrice=$plan.annualPrice; userLimit=1; propertyLimit=$plan.propertyLimit; storageLimitMb=$plan.storageLimitMb; deviceLimit=$plan.deviceLimit; modules=$plan.modules; premiumFeatures=$plan.premiumFeatures; active=$true }
    [void](Invoke-Api "/api/platform/plans/$($plan.id)" 409 'PUT' $planBody $globalHeaders)
    $tenantUpdate = @{ name='Fazenda Verificação SaaS Atualizada'; type='PRODUCER'; responsibleName='Administradora Verificação'; responsibleEmail='admin.verify@agro360.local'; planId=$otherPlan.id }
    [void](Invoke-Api "/api/platform/tenants/$($created.id)" 204 'PUT' $tenantUpdate $globalHeaders)
    if ((Invoke-SqlScalar "select count(*) from agro360.audit_saas_events where tenant_id='$($created.id)' and event_type='TENANT_UPDATED' and details ? 'previousPlanId';") -ne '1') {
        throw 'Auditoria de troca de plano não preservou o plano anterior.'
    }
    Write-Output 'PASS atualização de cliente preserva plano anterior em auditoria'

    [void](Invoke-Api "/api/roles/$($role.id)" 204 'PUT' @{ name='Consulta removida'; level=10; permissions=@() } $adminHeaders)
    [void](Invoke-Api '/api/v1/properties?page=1&pageSize=20' 403 'GET' $null $restrictedHeaders)
    [void](Invoke-SqlScalar "update agro360.platform_super_admins set active=false where user_id='$($global.userId)';")
    [void](Invoke-Api '/api/platform/tenants' 403 'GET' $null $globalHeaders)
    Write-Output 'PASS autorização revalida banco e invalida privilégios retirados'

    $facts = Invoke-SqlScalar "select (select count(*) from agro360.saas_invitations where tenant_id='$($created.id)' and status='ACCEPTED')||','||(select count(*) from agro360.audit_saas_events where tenant_id='$($created.id)')||','||(select count(*) from agro360.identity_users where tenant_id='$($created.id)' and status='ACTIVE');"
    Set-Content -LiteralPath "$evidence\facts.txt" -Value "acceptedInvitations,auditEvents,activeUsers=$facts"
    Write-Output "PASS fluxo SaaS vertical completo. Evidência local: $evidence"
} finally {
    if ($null -ne $apiProcess -and -not $apiProcess.HasExited) { $apiProcess.Kill($true); $apiProcess.WaitForExit() }
    if ($startedDatabase) { & "$PostgresBin\pg_ctl.exe" -D "$evidence\data" -m fast -w stop | Out-Host }
    foreach ($name in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name]) }
    Write-Output "Evidência preservada (ignorada pelo Git): $evidence"
}
