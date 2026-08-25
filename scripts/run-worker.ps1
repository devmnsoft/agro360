. (Join-Path $PSScriptRoot 'common-local.ps1'); Assert-Database
& dotnet run --project src/Hosts/Agro360.Worker --no-launch-profile
if ($LASTEXITCODE) { throw 'Worker encerrou com erro.' }
