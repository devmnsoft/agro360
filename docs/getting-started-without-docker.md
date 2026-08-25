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

## Sprint 6

Após instalar `database/agro360-postgres-full.sql`, exporte `ConnectionStrings__Agro360` para API, Worker e Migrator. Execute em terminais separados: `dotnet run --project src/Hosts/Agro360.Api`, `dotnet run --project src/Hosts/Agro360.Web` e `dotnet run --project src/Hosts/Agro360.Worker`. Para migrations incrementais use `dotnet run --project src/Hosts/Agro360.Migrator`. Nenhum desses comandos requer Docker.

## Pecuária 360

Após instalar o schema v0.4.0, inicie API, Worker e Web em terminais separados. Nenhum host pressupõe PostgreSQL local ou Docker; todos recebem `ConnectionStrings__Agro360`.

```bash
export ConnectionStrings__Agro360='SUA CONNECTION STRING POSTGRESQL'
dotnet run --project src/Hosts/Agro360.Migrator -- migrate
dotnet run --project src/Hosts/Agro360.Api
dotnet run --project src/Hosts/Agro360.Worker
dotnet run --project src/Hosts/Agro360.Web
```

## Financeiro Agro

Após instalar o SQL único, execute nativamente `dotnet run --project src/Hosts/Agro360.Api`, `dotnet run --project src/Hosts/Agro360.Web` e `dotnet run --project src/Hosts/Agro360.Worker`. Todos leem `ConnectionStrings__Agro360`; o Migrator é executado com `dotnet run --project src/Hosts/Agro360.Migrator`.

## Hosts da Sprint 9

```bash
export ConnectionStrings__Agro360='Host=...;Database=...;Username=...;Password=...'
export Jwt__SigningKey='uma-chave-local-com-pelo-menos-32-caracteres'
psql "$ConnectionStrings__Agro360" -v ON_ERROR_STOP=1 -f database/agro360-postgres-full.sql
dotnet run --project src/Hosts/Agro360.Migrator
dotnet run --project src/Hosts/Agro360.Api
dotnet run --project src/Hosts/Agro360.Worker
dotnet run --project src/Hosts/Agro360.Web
```

Cada host também pode ser iniciado pelos scripts `scripts/run-*.sh`. Docker permanece opcional.

## Agricultura 360

Depois de instalar o banco e iniciar API e Web, acesse `http://localhost:8080/agriculture`. A API lê exclusivamente `ConnectionStrings__Agro360`; Docker não é necessário.
