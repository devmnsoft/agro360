#!/usr/bin/env bash
set -Eeuo pipefail

usage() { echo "Uso: $0 <arquivo.dump> [--force]" >&2; }
[[ $# -ge 1 && $# -le 2 ]] || { usage; exit 64; }
output=$1
[[ ${2:-} == "" || ${2:-} == "--force" ]] || { usage; exit 64; }
[[ ! -e "$output" || ${2:-} == "--force" ]] || {
  echo "Backup já existe: $output (use --force conscientemente)." >&2
  exit 73
}
command -v docker >/dev/null || { echo "docker não encontrado." >&2; exit 69; }
mkdir -p "$(dirname "$output")"
temporary="${output}.partial"
trap 'rm -f "$temporary"' EXIT

echo "[$(date -u +%FT%TZ)] Iniciando backup lógico consistente..." >&2
docker compose exec -T postgres sh -ceu '
  exec pg_dump --format=custom --no-owner --no-acl \
    --username="$POSTGRES_USER" --dbname="$POSTGRES_DB"
' >"$temporary"
[[ -s "$temporary" ]] || { echo "pg_dump produziu arquivo vazio." >&2; exit 74; }
mv -f "$temporary" "$output"
trap - EXIT
sha256sum "$output" >"${output}.sha256"
echo "[$(date -u +%FT%TZ)] Backup concluído: $output" >&2
