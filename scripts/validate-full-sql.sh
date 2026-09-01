#!/usr/bin/env bash
set -euo pipefail
file="${1:-database/agro360-postgres-full.sql}"
test -s "$file"
if rg -n '(^[[:space:]]*\\\\(i|include|ir)([[:space:]]|$)|Host=|Password=|/home/|/workspace/|[A-Za-z]:\\\\)' "$file"; then
  echo "ERRO: instalador contém include, conexão, segredo ou caminho local" >&2; exit 1
fi
for required in 'create schema if not exists agro360' 'create table' 'create index' 'create or replace view agro360.' 'create or replace function agro360.' 'create trigger' 'agro360.platform_schema_versions' 'agro360.storage_receipts' 'agro360.storage_lots' 'agro360.logistics_trips' 'agro360.traceability_lots' 'agro360.compliance_product_rules' 'agro360.rural_hr_people' 'superadmin@mnsoft.com.br' 'pbkdf2-sha512'; do
  rg -qi "$required" "$file" || { echo "ERRO: item ausente: $required" >&2; exit 1; }
done
if rg -ni '^\s*create schema( if not exists)?\s+(identity|finance|audit|workflow|inventory|platform|tenancy|operations|support|public)\b' "$file"; then
  echo "ERRO: instalador cria namespace legado" >&2; exit 1
fi
echo "SQL consolidado validado: $file"
