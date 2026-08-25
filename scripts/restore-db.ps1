& (Join-Path $PSScriptRoot '../database/maintenance/restore.ps1') @args
if ($LASTEXITCODE) { throw 'Restauração falhou.' }
