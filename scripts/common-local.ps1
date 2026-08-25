$ErrorActionPreference = 'Stop'
Set-Location (Resolve-Path (Join-Path $PSScriptRoot '..'))
function Assert-DotNet { if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '.NET SDK 10 não encontrado.' }; if ((dotnet --version) -notmatch '^10\.') { throw '.NET SDK 10 é obrigatório.' } }
function Assert-Database { Assert-DotNet; if (-not $env:ConnectionStrings__Agro360) { throw 'Defina ConnectionStrings__Agro360; a senha não será exibida.' }; dotnet run --project src/Hosts/Agro360.Migrator -- status; if ($LASTEXITCODE) { throw 'PostgreSQL 14+ indisponível ou configuração inválida.' } }
