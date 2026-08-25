$ErrorActionPreference='Stop'; if (-not $env:PGDATABASE) { throw 'Defina PGDATABASE; use PGHOST/PGPORT/PGUSER e pgpass ou prompt.' }
$prefix=if($args[0]){$args[0]}else{"agro360-$(Get-Date -Format yyyyMMddTHHmmssZ)"}
& pg_dump --format=plain --no-owner --no-privileges --file="$prefix.sql"; if($LASTEXITCODE){throw 'pg_dump plain falhou.'}
& pg_dump --format=custom --no-owner --no-privileges --file="$prefix.backup"; if($LASTEXITCODE){throw 'pg_dump custom falhou.'}
