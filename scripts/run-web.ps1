. (Join-Path $PSScriptRoot 'common-local.ps1'); Assert-DotNet
& dotnet run --project src/Hosts/Agro360.Web --no-launch-profile
if ($LASTEXITCODE) { throw 'Web encerrou com erro.' }
