. (Join-Path $PSScriptRoot 'common-local.ps1'); Assert-Database
& dotnet run --project src/Hosts/Agro360.Api --no-launch-profile
if ($LASTEXITCODE) { throw 'API encerrou com erro.' }
