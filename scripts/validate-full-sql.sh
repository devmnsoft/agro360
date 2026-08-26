#!/usr/bin/env bash
set -euo pipefail
file="${1:-database/agro360-postgres-full.sql}"
test -s "$file"
if rg -n '(^[[:space:]]*\\\\(i|include|ir)([[:space:]]|$)|Host=|Password=|/home/|/workspace/|[A-Za-z]:\\\\)' "$file"; then
  echo "ERRO: instalador contém include, conexão, segredo ou caminho local" >&2; exit 1
fi
for required in 'create schema' 'create table' 'create index' 'create or replace view' 'create or replace function' 'create trigger' 'platform.schema_versions' 'storage.receipts' 'storage.lots' 'logistics.trips' 'traceability.lots' 'compliance.product_rules' 'rural_hr.people'; do
  rg -qi "$required" "$file" || { echo "ERRO: item ausente: $required" >&2; exit 1; }
done
echo "SQL consolidado validado: $file"
