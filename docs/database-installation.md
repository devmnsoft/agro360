# Instalação PostgreSQL sem Docker

Pré-requisitos: PostgreSQL 14+ com PostGIS, `pgcrypto`, `pg_trgm` e `unaccent`. Crie banco/usuário conforme a política local e exporte a connection string (não a versione):

```bash
export ConnectionStrings__Agro360='Host=localhost;Port=5432;Database=agro360;Username=agro360_app;Password=ALTERAR'
psql "$ConnectionStrings__Agro360" -v ON_ERROR_STOP=1 -f database/agro360-postgres-full.sql
```

O arquivo consolidado é autocontido, idempotente e inclui todas as versões até 0.7.0. Não contém `\\i`, host, usuário, banco ou senha fixos. Alternativamente, execute migrations em ordem e depois `database/seeds/minimal-production.sql` e `database/seeds/sprint10-amazon-products.sql`.

Valide com `./scripts/validate-full-sql.sh` e rode a API com `dotnet run --project src/Hosts/Agro360.Api`. Docker Compose é apenas opcional.

## Sprint 11

O arquivo único já contém as tabelas, índices, constraints, políticas RLS e versão da Agricultura 360. Execute sem Docker:

```bash
psql "$ConnectionStrings__Agro360" -v ON_ERROR_STOP=1 -f database/agro360-postgres-full.sql
```

## Sprint 12 — instalação única

Sem Docker, configure uma instância PostgreSQL acessível e execute:

```bash
export ConnectionStrings__Agro360='Host=localhost;Port=5432;Database=agro360;Username=agro360_app;Password=troque-aqui'
psql "$ConnectionStrings__Agro360" -f database/agro360-postgres-full.sql
```

O arquivo é autônomo, transacional e inclui o schema `mobile` até a Sprint 12. A aplicação usa a mesma connection string e Dapper/Npgsql.
