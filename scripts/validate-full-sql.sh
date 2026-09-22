#!/usr/bin/env bash
set -euo pipefail
file="${1:-database/agro360-postgres-full.sql}"
test -s "$file"
first_role_reference="$(rg -n -m1 -i '^[[:space:]]*(grant|revoke|create[[:space:]]+policy|alter[[:space:]]+default[[:space:]]+privileges)[^;]*\bagro360_app\b' "$file" | cut -d: -f1)"
first_role_creation="$(rg -n -m1 -i 'create[[:space:]]+role[[:space:]]+agro360_app[[:space:]]+nologin' "$file" | cut -d: -f1)"
if [[ -z "$first_role_creation" || -z "$first_role_reference" || "$first_role_creation" -gt "$first_role_reference" ]]; then
  echo "ERRO: agro360_app deve ser criada como NOLOGIN antes da primeira referencia" >&2; exit 1
fi
if rg -n -i '^[[:space:]]*rollback[[:space:]]*;' "$file"; then
  echo "ERRO: instalador consolidado contém ROLLBACK explícito" >&2; exit 1
fi
if rg -n '(^[[:space:]]*\\\\(i|include|ir)([[:space:]]|$)|Host=|Password=|/home/|/workspace/|[A-Za-z]:\\\\)' "$file"; then
  echo "ERRO: instalador contém include, conexão, segredo ou caminho local" >&2; exit 1
fi
for required in 'create schema if not exists agro360' 'create table' 'create index' 'create or replace view agro360.' 'create or replace function agro360.' 'create trigger' 'agro360.platform_schema_versions' 'agro360.storage_receipts' 'agro360.storage_lots' 'agro360.logistics_trips' 'agro360.traceability_lots' 'agro360.compliance_product_rules' 'agro360.rural_hr_people'; do
  rg -qi "$required" "$file" || { echo "ERRO: item ausente: $required" >&2; exit 1; }
done
if rg -ni '^\s*create schema( if not exists)?\s+(identity|finance|audit|workflow|inventory|platform|tenancy|operations|support|public)\b' "$file"; then
  echo "ERRO: instalador cria namespace legado" >&2; exit 1
fi
schema_declarations="$(rg -io '^[[:space:]]*create schema( if not exists)?[[:space:]]+[a-z_][a-z0-9_]*' "$file" | tr '[:upper:]' '[:lower:]' | sed -E 's/^[[:space:]]*create schema( if not exists)?[[:space:]]+//')"
if [[ "$schema_declarations" != "agro360" ]]; then
  echo "ERRO: o instalador deve declarar exclusivamente CREATE SCHEMA IF NOT EXISTS agro360" >&2; exit 1
fi
if rg -ni '\busing\s+gist\s*\(' "$file"; then
  echo "ERRO: o instalador não deve usar GiST; colunas JSONB devem usar GIN" >&2; exit 1
fi
echo "SQL consolidado validado: $file"
