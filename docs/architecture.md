# Arquitetura

O repositório aplica Clean Architecture por dependências: `Domain` contém regras e não referencia persistência; `Application` define contratos e abstrações; `Infrastructure` implementa acesso PostgreSQL com Dapper; os hosts API, Web, Worker e Migrator compõem e orquestram o processo.

Toda conexão é criada por `IDbConnectionFactory`. Serviços de infraestrutura usam queries parametrizadas e transações explícitas para gravações críticas. O tenant autenticado é propagado pelo contexto e protegido adicionalmente por Row-Level Security. A API centraliza correlação, tenant e respostas de exceção em middleware, mantendo controllers sem regras de domínio.

SQL de evolução vive somente em `database/migrations`; seeds são separados de schema e releases consolidados são artefatos portáveis. Decisões e limites detalhados continuam em `ARCHITECTURE.md` e `MODULE-CATALOG.md`.

## Fatia operacional da Sprint 6

`OperationalRules` mantém invariantes puras no Domain. Contratos/DTOs e `IOperationsService` ficam em Application. A implementação Infrastructure coordena Dapper, transações PostgreSQL, bloqueio pessimista, RLS e auditoria; controllers apenas traduzem HTTP e autorização. O arquivo SQL único e as migrações usam constraints como segunda linha de defesa.
