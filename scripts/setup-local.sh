#!/usr/bin/env bash
set -Eeuo pipefail; source "$(dirname "$0")/common-local.sh"
require_dotnet
echo 'Restaurando e compilando Agro 360...'
dotnet restore MNSOFT.Agro360.sln
dotnet build MNSOFT.Agro360.sln --configuration Release --no-restore
require_database
dotnet run --project src/Hosts/Agro360.Migrator -- validate
echo 'Ambiente local validado.'
