#!/usr/bin/env bash
set -euo pipefail

dotnet restore MNSOFT.Agro360.sln
dotnet format MNSOFT.Agro360.sln --verify-no-changes --no-restore
dotnet build MNSOFT.Agro360.sln --configuration Release --no-restore
dotnet test MNSOFT.Agro360.sln --configuration Release --no-build --collect:"XPlat Code Coverage"
