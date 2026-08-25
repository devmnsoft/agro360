#!/usr/bin/env bash
set -Eeuo pipefail
: "${PGDATABASE:?Defina PGDATABASE para um banco de restauração já criado.}"
file="${1:?Uso: restore.sh arquivo.sql|arquivo.backup}"
case "$file" in *.backup) pg_restore --exit-on-error --no-owner --no-privileges --dbname="$PGDATABASE" "$file";; *.sql) psql --set=ON_ERROR_STOP=1 --dbname="$PGDATABASE" --file="$file";; *) echo 'Formato deve ser .sql ou .backup' >&2; exit 2;; esac
