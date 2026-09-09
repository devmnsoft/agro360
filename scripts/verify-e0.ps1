param(
    [string]$PostgresBin = 'C:\Program Files\PostgreSQL\18\bin',
    [switch]$SkipBuild,
    [switch]$CheckMigrations
)

# Windows/PowerShell 7. All database writes target a new, private cluster.
# No existing connection string, database, launch profile or process is changed.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
foreach ($tool in @('initdb', 'pg_ctl', 'psql')) {
    if (-not (Test-Path "$PostgresBin\$tool.exe")) { throw "BLOCKED: $tool indisponível em PostgresBin." }
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'BLOCKED: dotnet indisponível.' }
$evidence = Join-Path $root ('artifacts\e0-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidence | Out-Null
$processes = [Collections.Generic.List[Diagnostics.Process]]::new()
$savedEnvironment = @{}
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
function Invoke-Sql([string]$Sql) {
    $Sql | & "$PostgresBin\psql.exe" -X -q -v ON_ERROR_STOP=1 >> "$evidence\fixture.log" 2>&1
    Assert-Exit 'SQL fixture'
}
function Invoke-Check([string]$Path, [int]$Status = 200, [string]$Method = 'GET', $Body = $null, $Headers = @{}) {
    $options = @{ Uri = "$apiUrl$Path"; Method = $Method; Headers = $Headers; SkipHttpErrorCheck = $true; TimeoutSec = 20 }
    if ($null -ne $Body) { $options.Body = $Body | ConvertTo-Json -Compress; $options.ContentType = 'application/json' }
    $response = Invoke-WebRequest @options
    if ([int]$response.StatusCode -ne $Status) {
        # Never print response bodies: successful authentication contains tokens.
        throw "$Method $Path retornou $($response.StatusCode), esperado $Status. Evidência: $evidence"
    }
    Write-Host "PASS $Method $Path -> $Status"
    return $response
}
function Start-HostProcess([string]$HostName, [string]$Url) {
    $project = "src\Hosts\Agro360.$HostName"
    $dll = Join-Path $root "$project\bin\Release\net10.0\Agro360.$HostName.dll"
    $process = Start-Process dotnet -ArgumentList @("`"$dll`"", '--urls', $Url) -WorkingDirectory "$root\$project" -WindowStyle Hidden -PassThru -RedirectStandardOutput "$evidence\$HostName.log" -RedirectStandardError "$evidence\$HostName.err.log"
    $processes.Add($process)
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        if ($process.HasExited) { throw "$HostName encerrou na inicialização. Evidência: $evidence" }
        try {
            $response = Invoke-WebRequest "$Url/health" -SkipHttpErrorCheck -TimeoutSec 2
            if ($response.StatusCode -in @(200, 503)) { return }
        } catch [Net.Http.HttpRequestException] {
            # The listener can still be starting; retry within the bounded loop.
        } catch [Threading.Tasks.TaskCanceledException] {
            # Startup may take longer than one individual HTTP probe.
        }
        Start-Sleep -Milliseconds 500
    }
    throw "$HostName não iniciou em tempo hábil. Evidência: $evidence"
}

try {
    if (-not $SkipBuild) {
        dotnet restore MNSOFT.Agro360.sln > "$evidence\restore.log" 2>&1
        Assert-Exit 'restore'
        dotnet build MNSOFT.Agro360.sln -c Release --no-restore > "$evidence\build.log" 2>&1
        Assert-Exit 'build Release'
    }
    $databasePort = Get-FreePort
    $apiUrl = 'http://127.0.0.1:' + (Get-FreePort)
    $webUrl = 'http://127.0.0.1:' + (Get-FreePort)
    $password = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    Set-TaskEnvironment PGPASSWORD $password
    Set-TaskEnvironment PGHOST '127.0.0.1'
    Set-TaskEnvironment PGPORT "$databasePort"
    Set-TaskEnvironment PGUSER 'postgres'
    Set-TaskEnvironment PGDATABASE 'postgres'

    # initdb reads the generated password from a Windows named pipe, not a file
    # or a command-line argument. No universal development password is used.
    $pipeName = 'agro360-e0-' + [guid]::NewGuid().ToString('N')
    $pipe = [IO.Pipes.NamedPipeServerStream]::new($pipeName, [IO.Pipes.PipeDirection]::Out)
    try {
        $pending = $pipe.WaitForConnectionAsync()
        $init = Start-Process "$PostgresBin\initdb.exe" -ArgumentList @('-D', "`"$evidence\data`"", '-U', 'postgres', '--auth=scram-sha-256', "--pwfile=\\.\pipe\$pipeName", '--encoding=UTF8', '--locale=C') -WindowStyle Hidden -PassThru -RedirectStandardOutput "$evidence\initdb.log" -RedirectStandardError "$evidence\initdb.err.log"
        $processes.Add($init)
        if (-not $pending.Wait(15000)) { throw 'initdb não abriu o canal de credencial.' }
        $writer = [IO.StreamWriter]::new($pipe)
        try { $writer.WriteLine($password) } finally { $writer.Dispose() }
        $init.WaitForExit()
        if ($init.ExitCode -ne 0) { throw "initdb falhou. Evidência: $evidence" }
    } finally { $pipe.Dispose() }
    & "$PostgresBin\pg_ctl.exe" -D "$evidence\data" -l "$evidence\postgres.log" -o "-h 127.0.0.1 -p $databasePort" -w start
    Assert-Exit 'start PostgreSQL isolado'
    $startedDatabase = $true
    Invoke-Sql 'create database agro360_e0_test;'
    Set-TaskEnvironment PGDATABASE 'agro360_e0_test'
    $connection = "Host=127.0.0.1;Port=$databasePort;Database=agro360_e0_test;Username=postgres;Password=$password"
    Set-TaskEnvironment ConnectionStrings__Agro360 $connection
    Set-TaskEnvironment AGRO360_TEST_CONNECTION_STRING $connection
    Set-TaskEnvironment ASPNETCORE_ENVIRONMENT 'Development'
    Set-TaskEnvironment Jwt__SigningKey ([Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48)))
    Set-TaskEnvironment ApiBaseUrl $apiUrl
    Set-TaskEnvironment Cors__AllowedOrigins__0 $webUrl
    Set-TaskEnvironment Bootstrap__Enabled 'false'
    Start-HostProcess Api $apiUrl
    $null = Invoke-Check '/health/live'
    $null = Invoke-Check '/health' 503

    & "$PostgresBin\psql.exe" -X -v ON_ERROR_STOP=1 -f database/agro360-postgres-full.sql > "$evidence\install.log" 2>&1
    Assert-Exit 'instalação limpa'
    & "$PostgresBin\psql.exe" -X -v ON_ERROR_STOP=1 -f database/agro360-postgres-full.sql > "$evidence\reinstall.log" 2>&1
    Assert-Exit 'reexecução instalador'
    $null = Invoke-Check '/health'
    $openapi = Invoke-Check '/openapi/v1.json'
    if (($openapi.Content | ConvertFrom-Json).paths.PSObject.Properties.Count -lt 1) { throw 'OpenAPI sem paths.' }
    $null = Invoke-Check '/swagger/v1/swagger.json'
    $null = Invoke-Check '/api/v1/dashboard/command-center' 401

    # Only the freshly installed disposable fixture is assigned a random secret.
    $loginPassword = 'Aa1!' + [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    $salt = [Security.Cryptography.RandomNumberGenerator]::GetBytes(16)
    $hash = [Security.Cryptography.Rfc2898DeriveBytes]::Pbkdf2($loginPassword, $salt, 210000, [Security.Cryptography.HashAlgorithmName]::SHA512, 32)
    $encoded = 'pbkdf2-sha512$210000$' + [Convert]::ToBase64String($salt) + '$' + [Convert]::ToBase64String($hash)
    Invoke-Sql "update agro360.identity_users set password_hash='$encoded',status='ACTIVE',must_change_password=false where email='admin@santaclara.agro360.local';"
    $login = @{ tenantSlug = 'santa-clara'; email = 'admin@santaclara.agro360.local'; password = $loginPassword }
    $auth = (Invoke-Check '/api/v1/auth/login' 200 POST $login).Content | ConvertFrom-Json
    $headers = @{ Authorization = 'Bearer ' + $auth.accessToken }
    $null = Invoke-Check '/api/v1/dashboard/command-center' 200 GET $null $headers
    $refreshed = (Invoke-Check '/api/v1/auth/refresh' 200 POST @{ refreshToken = $auth.refreshToken }).Content | ConvertFrom-Json
    $null = Invoke-Check '/api/v1/auth/refresh' 401 POST @{ refreshToken = $auth.refreshToken }
    $null = Invoke-Check '/api/v1/auth/logout' 204 POST @{ refreshToken = $refreshed.refreshToken }
    $null = Invoke-Check '/api/v1/auth/refresh' 401 POST @{ refreshToken = $refreshed.refreshToken }
    $login.password = 'invalid-' + [guid]::NewGuid().ToString('N')
    $null = Invoke-Check '/api/v1/auth/login' 401 POST $login

    Start-HostProcess Web $webUrl
    $page = Invoke-WebRequest $webUrl -TimeoutSec 20
    if ($page.StatusCode -ne 200 -or $page.Content -notmatch 'Agro360') { throw 'Página inicial não renderizou.' }
    $asset = Invoke-WebRequest "$webUrl/js/agro360.js" -TimeoutSec 20
    if ($asset.StatusCode -ne 200) { throw 'Script global indisponível.' }
    Write-Output 'PASS Web Razor / e script global (HTTP; não substitui navegador)'
    dotnet test --solution MNSOFT.Agro360.sln -c Release --no-build > "$evidence\tests.log" 2>&1
    Assert-Exit 'testes existentes com PostgreSQL real'
    if ($CheckMigrations) {
        # Independent clean target: never replay legacy migrations over the
        # installed fixture, and never silently mark them as applied.
        Invoke-Sql 'create database agro360_incremental_test;'
        Set-TaskEnvironment ConnectionStrings__Agro360 ($connection.Replace('Database=agro360_e0_test;', 'Database=agro360_incremental_test;'))
        dotnet src/Hosts/Agro360.Migrator/bin/Release/net10.0/Agro360.Migrator.dll migrate --migrations "$root\database\migrations" > "$evidence\migrator.log" 2>&1
        Assert-Exit 'migrations em banco independente'
    }
    Write-Output "PASS E0 smoke HTTP/SQL. Evidência local: $evidence"
} finally {
    foreach ($process in $processes) {
        if (-not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
        $process.Dispose()
    }
    if ($startedDatabase) { & "$PostgresBin\pg_ctl.exe" -D "$evidence\data" -m fast -w stop }
    foreach ($name in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name]) }
    Write-Output "Evidência preservada (ignorada pelo Git): $evidence"
}
