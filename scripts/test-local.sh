#!/usr/bin/env bash
set -Eeuo pipefail; source "$(dirname "$0")/common-local.sh"
[[ -n "${AGRO360_TEST_CONNECTION_STRING:-}" ]] || { echo 'ERRO: defina AGRO360_TEST_CONNECTION_STRING para um banco exclusivo de teste.' >&2; exit 1; }
case "$AGRO360_TEST_CONNECTION_STRING" in *[Tt]est*|*[Tt]este*) ;; *) echo 'ERRO: a conexão de testes deve identificar explicitamente test/teste.' >&2; exit 1;; esac
export ConnectionStrings__Agro360="$AGRO360_TEST_CONNECTION_STRING"
"$ROOT/scripts/setup-local.sh"; "$ROOT/scripts/migrate-local.sh"
dotnet test MNSOFT.Agro360.sln --configuration Release --no-build
