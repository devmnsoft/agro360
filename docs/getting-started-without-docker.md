# Execução local sem Docker

Instale o .NET SDK indicado em `global.json` e PostgreSQL 14+ com PostGIS, `pgcrypto`, `pg_trgm` e `unaccent`. Defina a conexão exclusivamente por configuração:

```bash
export ConnectionStrings__Agro360='Host=SERVIDOR;Database=BANCO;Username=USUARIO;Password=SENHA'
./scripts/setup-local.sh
./scripts/migrate.sh migrate
./scripts/run-api.sh       # outro terminal
./scripts/run-worker.sh    # outro terminal
./scripts/run-web.sh       # outro terminal
```

No PowerShell use `$env:ConnectionStrings__Agro360='...'` e os scripts `.ps1` equivalentes. API, Web, Worker, Migrator e testes são processos .NET nativos; Docker Compose é apenas uma alternativa opcional.

Para publicar, execute `dotnet publish src/Hosts/Agro360.Api -c Release -o artifacts/api` e repita para Web, Worker e Migrator. Em falhas, confira `dotnet --info`, acesso TCP ao PostgreSQL, extensões com `./scripts/migrate.sh validate` e os logs estruturados do host.
