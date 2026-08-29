# Banco portátil Agro 360

## Sprint 31

O script completo cria as 17 estruturas de inteligência, constraints, índices e RLS. Em PostgreSQL externo execute `psql "$ConnectionStrings__Agro360" -v ON_ERROR_STOP=1 -f database/agro360-postgres-full.sql`. A aplicação lê `ConnectionStrings__Agro360`; container não é obrigatório.

PostgreSQL 14+ é suportado. PostGIS, `pgcrypto`, `pg_trgm` e `unaccent` são requeridos pelos recursos atuais; um administrador deve disponibilizá-los quando o usuário da aplicação não puder executar `CREATE EXTENSION`.

## Instalador canônico da Release Candidate

`agro360-postgres-full.sql` é o artefato único, completo e não segmentado. Execute-o em um banco vazio com `psql "$ConnectionStrings__Agro360" -f database/agro360-postgres-full.sql` ou carregue o arquivo integralmente no pgAdmin/DBeaver. Ele não contém `\\i`, conexão, proprietário ou credencial fixa.

Antes da publicação, rode `./scripts/validate-full-sql.sh`. A instalação registra `2.0.0-rc.1` em `platform.schema_versions`. Scripts históricos permanecem para auditoria, mas não são pré-requisitos do instalador canônico.

- `bootstrap/`: exemplos administrativos sem credenciais;
- `migrations/`: histórico imutável consumido pelo Migrator;
- `seeds/`: dados opcionais, selecionados explicitamente;
- `releases/v0.2.0/`: instalador autônomo para `psql`, pgAdmin ou DBeaver;
- `maintenance/`: diagnóstico, backup e restauração nativos.

Instalação recomendada: `dotnet run --project src/Hosts/Agro360.Migrator -- migrate`. Alternativamente execute `agro360-v0.2.0-full-install.sql` no banco vazio. No pgAdmin abra Query Tool, carregue o arquivo e execute; no DBeaver use **SQL Editor > Open SQL Script** e execute o script inteiro. Não use a conexão de produção para seeds de Development/Homologation.

## Sprint 21 e dados demonstrativos

`agro360-postgres-full.sql` continua sendo o instalador único e completo do schema. Execute-o com `psql "$ConnectionStrings__Agro360" -v ON_ERROR_STOP=1 -f database/agro360-postgres-full.sql`. Para uma apresentação comercial, carregue depois `database/seed-demo.sql`; o seed é idempotente e separado apenas por ser opcional. Não há dependência de Docker nem extensões fora da distribuição PostgreSQL (`pgcrypto`, `pg_trgm`, `unaccent`).

## Banco da Sprint 22
A migration `migrations/021_sprint22_commercial_crm.sql` cria CRM e comercial com chaves compostas por tenant, constraints, índices, RLS e eventos de integração. Ela já está incorporada integralmente a `agro360-postgres-full.sql`; o instalador completo não usa `\\i` nem depende de Docker ou credenciais fixas:
```bash
psql "$ConnectionStrings__Agro360" -f database/agro360-postgres-full.sql
```
O split persistido é um controle interno e não movimenta recursos bancários.

## Sprint 23
As tabelas do schema `documents` e os 17 tipos documentais estão incluídos no arquivo completo. Aplique sem Docker: `psql "$AGRO360_CONNECTION_STRING" -v ON_ERROR_STOP=1 -f database/agro360-postgres-full.sql`. O script não contém host, usuário ou senha e não usa `\i`. Arquivos são externos ao PostgreSQL e configurados por `Storage__RootPath`.

## Sprint 24

`migrations/022_sprint24_work_management.sql` cria o schema `operations`; ele já está incorporado em `agro360-postgres-full.sql` (sem `\\i`). Para PostgreSQL externo: `psql "$ConnectionStrings__Agro360" -v ON_ERROR_STOP=1 -f database/agro360-postgres-full.sql`. A connection string é fornecida pelo ambiente, sem host, usuário ou senha embutidos. O script inclui constraints, FKs, índices, RLS, workflows iniciais e o avaliador determinístico de estoque, financeiro e SLA.

## Sprint 25

`agro360-postgres-full.sql` inclui BI, relatórios, mapas, preferências e auditoria visual. Em PostgreSQL externo, configure `ConnectionStrings__Agro360`, execute `psql "$ConnectionStrings__Agro360" -v ON_ERROR_STOP=1 -f database/agro360-postgres-full.sql` e valide com `scripts/validate-full-sql.sh`. A migration incremental é `database/migrations/023_sprint25_bi_reports_maps_design.sql`. Docker não é necessário.

## Sprint 26

O script completo cria `field_operations`, amplia as tabelas `mobile` e instala constraints, índices e RLS da operação de campo. Execute o arquivo integral contra PostgreSQL externo com `psql "$ConnectionStrings__Postgres" -v ON_ERROR_STOP=1 -f database/agro360-postgres-full.sql`; ele é idempotente para a evolução documentada.

## Instalação Sprint 27 em PostgreSQL externo

Defina uma connection string externa (por exemplo, `export ConnectionStrings__Agro360='Host=db.exemplo;Port=5432;Database=agro360;Username=agro360;Password=***;SSL Mode=Require'`) e execute:

```bash
psql "$ConnectionStrings__Agro360" -v ON_ERROR_STOP=1 -f database/agro360-postgres-full.sql
```

O bloco `2.7.0` cria o schema `portal`, chaves, checks, índices e RLS, além dos nove perfis e termo inicial para tenants existentes. Execute o script completo em banco vazio; para tenants criados depois, o onboarding deve provisionar perfis/termo antes de emitir convites.

## Sprint 28

O bloco `2.8.0` do instalador completo cria o schema `quality`, 25 tabelas multi-tenant, constraints de estados/limites, índices operacionais e RLS. Execute em PostgreSQL externo com `psql "$ConnectionStrings__Agro360" -v ON_ERROR_STOP=1 -f database/agro360-postgres-full.sql`. Limites agroindustriais e referências normativas são cadastrados pelo tenant; o script não inventa normas.

## Sprint 29 — SaaS

O bloco `2.9.0` cria o catalogo SaaS de features/limites/permissoes e tabelas de status, assinatura, cobranca, consumo, override, onboarding, branding e auditoria, com FKs, checks, indices e RLS tenant-scoped. Em PostgreSQL externo execute `psql "$ConnectionStrings__Agro360" -v ON_ERROR_STOP=1 -f database/agro360-postgres-full.sql`. Nao ha banco embutido nem dependencia obrigatoria de Docker.

## Sprint 30
A migration `migrations/030_sprint30_integrations_fiscal.sql` também integra o instalador completo. Em PostgreSQL externo execute `psql "$ConnectionStrings__Agro360" -v ON_ERROR_STOP=1 -f database/agro360-postgres-full.sql`. Ela cria schemas/tabelas, FKs compostas, checks, índices e RLS multi-tenant para integrações, API, webhooks, jobs e fiscal. Segredos são apenas referências; faça backup do cofre separadamente.

## Sprint 33

O script integral cria o schema `support`, suas 26 tabelas, FKs, constraints, índices, RLS e permissões. Em PostgreSQL externo:

```bash
export ConnectionStrings__Agro360='Host=db.exemplo;Port=5432;Database=agro360;Username=agro360;Password=...;SSL Mode=Require'
psql "host=db.exemplo port=5432 dbname=agro360 user=agro360 sslmode=require" -v ON_ERROR_STOP=1 -f database/agro360-postgres-full.sql
```

Use secret manager para senha; nunca versionar connection string real. O sistema e a instalação não dependem de Docker. Não gere dump, PDF ou outro binário como parte desta sprint.

## Sprint 34 — SST Rural
Com PostgreSQL externo acessível, execute `psql "$ConnectionStrings__Agro360" -v ON_ERROR_STOP=1 -f database/agro360-postgres-full.sql`. O script cria o schema `sst`, tabelas, constraints, índices, FKs compostas, RLS, permissões e versão `3.4.0`. Não requer Docker e não contém segredo. Faça backup antes de produção.

## Banco da Sprint 35

Com PostgreSQL externo disponível, defina `ConnectionStrings__Agro360` e execute:

```bash
psql "$ConnectionStrings__Agro360" -v ON_ERROR_STOP=1 -f database/agro360-postgres-full.sql
```

A versão `3.5.0` cria as 21 tabelas `fleet_*`, FKs compostas, checks, unicidade, índices dimensionais, RLS e permissões. O script completo é idempotente e não depende de Docker. Faça backup e execute primeiro em homologação. Arquivos binários não devem ser gerados nesta sprint.
