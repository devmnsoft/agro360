# Banco portátil Agro 360

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
