# Instalação PostgreSQL sem Docker

Requisitos: PostgreSQL com `psql`, .NET SDK definido em `global.json` e uma base vazia acessível. Não há host, usuário, senha ou banco embutido no SQL.

```bash
export ConnectionStrings__Agro360='Host=localhost;Port=5432;Database=agro360;Username=SEU_USUARIO;Password=SUA_SENHA'
psql "$ConnectionStrings__Agro360" -v ON_ERROR_STOP=1 -f database/agro360-postgres-full.sql
dotnet restore
dotnet run --project src/Hosts/Agro360.Api
dotnet run --project src/Hosts/Agro360.Web
```

O arquivo único contém todas as estruturas até a Sprint 15, extensões, permissões, índices e RLS. É idempotente para instalação/atualização estrutural; backups continuam obrigatórios antes de reaplicar em produção.

## Sprint 16

O arquivo `database/agro360-postgres-full.sql` permanece autocontido e inclui o schema `integrations`, RLS, índices, constraints e permissões. Não requer Docker nem scripts auxiliares:

```bash
psql "$ConnectionStrings__Agro360" -v ON_ERROR_STOP=1 -f database/agro360-postgres-full.sql
```

A connection string deve apontar para um banco PostgreSQL existente e deve vir de variável de ambiente ou secret manager, nunca do repositório.

## Sprint 17 — camada geoespacial

A instalação integral continua sendo um único comando, sem Docker e sem PostGIS obrigatório:

```bash
psql "$ConnectionStrings__Agro360" -v ON_ERROR_STOP=1 -f database/agro360-postgres-full.sql
```

O script cria `geospatial`, tabelas JSONB, índices, RLS e permissões `maps.read`/`maps.write`. Não contém host, usuário, senha ou banco fixos.

## Sprint 18
O arquivo único já inclui o schema `cooperative`, RLS, índices e permissões. Aplique sem Docker com `psql "$ConnectionStrings__Agro360" -f database/agro360-postgres-full.sql`.

## Sprint 19 — instalação única sem Docker

Defina uma conexão PostgreSQL e aplique o arquivo completo, que contém todas as versões até RH Rural/SST:

```bash
export ConnectionStrings__Agro360='Host=localhost;Port=5432;Database=agro360;Username=agro360;Password=troque-esta-senha'
psql "$ConnectionStrings__Agro360" -f database/agro360-postgres-full.sql
```

O script não seleciona banco/host e não inclui credenciais. A aplicação e o migrador leem a mesma variável de ambiente.
