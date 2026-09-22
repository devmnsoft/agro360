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
seeds+=(database/agro360-postgres-seed-dev.sql database/seed-demo.sql)

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

echo "Assets de banco validados: consolidado, ${#migrations[@]} migrations e ${#seeds[@]} seeds"
