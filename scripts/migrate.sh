#!/usr/bin/env bash
set -Eeuo pipefail
source "$(dirname "$0")/common-local.sh"
require_database
if (($# == 0)); then set -- migrate; fi
exec dotnet run --project src/Hosts/Agro360.Migrator -- "$@"
