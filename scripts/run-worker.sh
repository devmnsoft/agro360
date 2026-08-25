#!/usr/bin/env bash
set -Eeuo pipefail
source "$(dirname "$0")/common-local.sh"
require_database
exec dotnet run --project src/Hosts/Agro360.Worker --no-launch-profile
