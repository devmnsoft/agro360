#!/usr/bin/env bash
set -Eeuo pipefail

usage() { echo "Uso: $0 <arquivo.dump> <banco-destino> --confirm" >&2; }
[[ $# -eq 3 && $3 == "--confirm" ]] || { usage; exit 64; }
backup=$1
target=$2
[[ -s "$backup" ]] || { echo "Backup ausente ou vazio: $backup" >&2; exit 66; }
[[ "$target" =~ ^[a-zA-Z_][a-zA-Z0-9_]*$ ]] || { echo "Nome de banco inválido." >&2; exit 65; }
command -v docker >/dev/null || { echo "docker não encontrado." >&2; exit 69; }

source_db=$(docker compose exec -T postgres sh -ceu 'printf %s "$POSTGRES_DB"')
[[ "$target" != "$source_db" ]] || { echo "Restauração sobre o banco de origem é proibida." >&2; exit 65; }
if [[ -f "${backup}.sha256" ]]; then sha256sum --check "${backup}.sha256"; fi

echo "[$(date -u +%FT%TZ)] Recriando banco isolado $target..." >&2
docker compose exec -T postgres sh -ceu "dropdb --if-exists --username=\"\$POSTGRES_USER\" '$target'; createdb --username=\"\$POSTGRES_USER\" '$target'"
docker compose exec -T postgres sh -ceu "pg_restore --exit-on-error --no-owner --no-acl --username=\"\$POSTGRES_USER\" --dbname='$target'" <"$backup"
docker compose exec -T postgres sh -ceu "psql --username=\"\$POSTGRES_USER\" --dbname='$target' --set=ON_ERROR_STOP=1 --tuples-only --command='select count(*) from platform.schema_migrations;'"
echo "[$(date -u +%FT%TZ)] Restauração validada em $target." >&2
