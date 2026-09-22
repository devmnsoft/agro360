#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

./scripts/validate-full-sql.sh database/agro360-postgres-full.sql

mapfile -t migrations < <(find database/migrations -maxdepth 1 -type f -name '*.sql' -print | sort)
for file in "${migrations[@]}"; do
  test -s "$file" || { echo "ERRO: migration vazia: $file" >&2; exit 1; }
  if rg -n -i '^[[:space:]]*rollback[[:space:]]*;' "$file"; then
    echo "ERRO: migration contém ROLLBACK explícito: $file" >&2
    exit 1
  fi
done

mapfile -t seeds < <(find database/seeds -maxdepth 1 -type f -name '*.sql' -print | sort)
seeds+=(
  database/agro360-postgres-seed-dev.sql
  database/seed-demo.sql
  database/seed-test-access.sql
)

for file in "${seeds[@]}"; do
  test -s "$file" || { echo "ERRO: seed vazio: $file" >&2; exit 1; }
  if rg -n -i '^[[:space:]]*(insert[[:space:]]+into|update|delete[[:space:]]+from|from|join)[[:space:]]+(identity|finance|audit|workflow|inventory|platform|tenancy|deployment)\.' "$file"; then
    echo "ERRO: seed usa namespace legado em vez do schema canônico agro360: $file" >&2
    exit 1
  fi
  if rg -n -i '^[[:space:]]*rollback[[:space:]]*;' "$file"; then
    echo "ERRO: seed contém ROLLBACK explícito: $file" >&2
    exit 1
  fi
done

# The access fixture is executable documentation for authentication and tenant
# isolation.  Keep these checks here as well as in the .NET suite so a runner
# without the SDK cannot silently publish a seed that resets credentials,
# reactivates access, or reintroduces the ambiguous PL/pgSQL variable.
access_seed=database/seed-test-access.sql
for forbidden in \
  'into[[:space:]]+user_id' \
  'password_hash[[:space:]]*=[[:space:]]*excluded\.password_hash' \
  'status[[:space:]]*=[[:space:]]*excluded\.status' \
  'on[[:space:]]+conflict[[:space:]]*\([^)]*\)[[:space:]]+do[[:space:]]+update[^;]*(password_hash|status)' \
  'alter[[:space:]]+table[^;]*disable[[:space:]]+row[[:space:]]+level[[:space:]]+security'
do
  if rg -n -i -U "$forbidden" "$access_seed"; then
    echo "ERRO: fixture de acesso pode redefinir credencial/status ou enfraquecer isolamento: $forbidden" >&2
    exit 1
  fi
done

rg -q -i 'v_user_id[[:space:]]+uuid' "$access_seed" || {
  echo 'ERRO: fixture de acesso deve usar v_user_id para evitar ambiguidade PL/pgSQL' >&2
  exit 1
}
rg -q -i 'on[[:space:]]+conflict[[:space:]]*\(user_id\)[[:space:]]+do[[:space:]]+nothing' "$access_seed" || {
  echo 'ERRO: fixture de acesso deve preservar a decisão existente de SuperAdmin' >&2
  exit 1
}

echo "Assets de banco validados: consolidado, ${#migrations[@]} migrations e ${#seeds[@]} seeds"
