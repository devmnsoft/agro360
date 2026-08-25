. (Join-Path $PSScriptRoot 'common-local.ps1'); Assert-Database
& (Join-Path $PSScriptRoot 'migrate-local.ps1')
$jobs = @(); try { foreach ($p in @('Agro360.Api','Agro360.Worker','Agro360.Web')) { $jobs += Start-Process dotnet -ArgumentList @('run','--project',"src/Hosts/$p",'--no-launch-profile') -PassThru }; Write-Host 'Hosts iniciados. Ctrl+C encerra todos.'; $jobs | Wait-Process } finally { $jobs | Where-Object {-not $_.HasExited} | Stop-Process }
