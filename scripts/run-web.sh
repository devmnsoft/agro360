#!/usr/bin/env bash
set -Eeuo pipefail
source "$(dirname "$0")/common-local.sh"
require_dotnet
exec dotnet run --project src/Hosts/Agro360.Web --no-launch-profile
