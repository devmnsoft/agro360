#!/usr/bin/env bash
set -Eeuo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"; cd "$ROOT"
require_dotnet() { command -v dotnet >/dev/null || { echo 'ERRO: instale o .NET SDK 10.' >&2; exit 1; }; dotnet --version | grep -Eq '^10\.' || { echo "ERRO: .NET 10 requerido; detectado $(dotnet --version)." >&2; exit 1; }; }
require_database() { [[ -n "${ConnectionStrings__Agro360:-}" ]] || { echo 'ERRO: defina ConnectionStrings__Agro360 (a senha não será exibida).' >&2; exit 1; }; require_dotnet; dotnet run --project src/Hosts/Agro360.Migrator -- status >/dev/null || { echo 'ERRO: PostgreSQL 14+ indisponível ou configuração inválida.' >&2; exit 1; }; }
