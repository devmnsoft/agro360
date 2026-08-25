. (Join-Path $PSScriptRoot 'common-local.ps1'); Assert-Database
dotnet run --project src/Hosts/Agro360.Migrator -- migrate; if ($LASTEXITCODE) { throw 'Migration falhou.' }
