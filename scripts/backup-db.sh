#!/usr/bin/env bash
set -Eeuo pipefail
exec "$(dirname "$0")/../database/maintenance/backup.sh" "$@"
