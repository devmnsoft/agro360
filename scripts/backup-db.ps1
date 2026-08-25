& (Join-Path $PSScriptRoot '../database/maintenance/backup.ps1') @args
if ($LASTEXITCODE) { throw 'Backup falhou.' }
