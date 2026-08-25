. (Join-Path $PSScriptRoot 'common-local.ps1'); Assert-DotNet
dotnet restore MNSOFT.Agro360.sln; if ($LASTEXITCODE) { throw 'Restore falhou.' }
dotnet build MNSOFT.Agro360.sln --configuration Release --no-restore; if ($LASTEXITCODE) { throw 'Build falhou.' }
Assert-Database; dotnet run --project src/Hosts/Agro360.Migrator -- validate; if ($LASTEXITCODE) { throw 'Validação falhou.' }
