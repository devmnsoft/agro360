#!/usr/bin/env bash
set -Eeuo pipefail; source "$(dirname "$0")/common-local.sh"
require_database; "${ROOT}/scripts/migrate-local.sh"
pids=(); cleanup(){ echo 'Encerrando hosts...'; ((${#pids[@]})) && kill "${pids[@]}" 2>/dev/null || true; wait 2>/dev/null || true; }; trap cleanup EXIT INT TERM
for project in Agro360.Api Agro360.Worker Agro360.Web; do dotnet run --project "src/Hosts/$project" --no-launch-profile & pids+=("$!"); done
echo 'API, Worker e Web iniciados. Ctrl+C encerra todos.'; wait -n "${pids[@]}"
