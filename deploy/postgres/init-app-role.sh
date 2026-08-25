#!/usr/bin/env bash
set -euo pipefail

if [[ -z "${APP_DB_PASSWORD:-}" ]]; then
    echo "APP_DB_PASSWORD is required" >&2
    exit 1
fi

psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" --set=app_password="$APP_DB_PASSWORD" <<'SQL'
select format('create role agro360_app login password %L nosuperuser nocreatedb nocreaterole noinherit', :'app_password')
where not exists (select 1 from pg_roles where rolname = 'agro360_app')
\gexec
SQL
