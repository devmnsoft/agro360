. (Join-Path $PSScriptRoot 'common-local.ps1'); Assert-Database
$command = if ($args.Count) { $args } else { @('migrate') }
& dotnet run --project src/Hosts/Agro360.Migrator -- @command
if ($LASTEXITCODE) { throw 'Migrator falhou.' }
