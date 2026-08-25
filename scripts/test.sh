#!/usr/bin/env bash
set -Eeuo pipefail
source "$(dirname "$0")/common-local.sh"
require_dotnet
dotnet restore MNSOFT.Agro360.sln
dotnet build MNSOFT.Agro360.sln --configuration Release --no-restore
exec dotnet test MNSOFT.Agro360.sln --configuration Release --no-build
