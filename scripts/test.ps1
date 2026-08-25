. (Join-Path $PSScriptRoot 'common-local.ps1'); Assert-DotNet
dotnet restore MNSOFT.Agro360.sln; if ($LASTEXITCODE) { throw 'Restore falhou.' }
dotnet build MNSOFT.Agro360.sln --configuration Release --no-restore; if ($LASTEXITCODE) { throw 'Build falhou.' }
dotnet test MNSOFT.Agro360.sln --configuration Release --no-build; if ($LASTEXITCODE) { throw 'Testes falharam.' }
