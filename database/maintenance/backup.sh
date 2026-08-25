#!/usr/bin/env bash
set -Eeuo pipefail
: "${PGDATABASE:?Defina PGDATABASE; use PGHOST/PGPORT/PGUSER e .pgpass ou prompt para autenticação.}"
prefix="${1:-agro360-$(date -u +%Y%m%dT%H%M%SZ)}"
pg_dump --format=plain --no-owner --no-privileges --file="${prefix}.sql"
pg_dump --format=custom --no-owner --no-privileges --file="${prefix}.backup"
echo "Backups criados: ${prefix}.sql e ${prefix}.backup"
