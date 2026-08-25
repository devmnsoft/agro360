# Arquitetura

O repositório aplica Clean Architecture por dependências: `Domain` contém regras e não referencia persistência; `Application` define contratos e abstrações; `Infrastructure` implementa acesso PostgreSQL com Dapper; os hosts API, Web, Worker e Migrator compõem e orquestram o processo.

Toda conexão é criada por `IDbConnectionFactory`. Serviços de infraestrutura usam queries parametrizadas e transações explícitas para gravações críticas. O tenant autenticado é propagado pelo contexto e protegido adicionalmente por Row-Level Security. A API centraliza correlação, tenant e respostas de exceção em middleware, mantendo controllers sem regras de domínio.

SQL de evolução vive somente em `database/migrations`; seeds são separados de schema e releases consolidados são artefatos portáveis. Decisões e limites detalhados continuam em `ARCHITECTURE.md` e `MODULE-CATALOG.md`.

## Fatia operacional da Sprint 6

`OperationalRules` mantém invariantes puras no Domain. Contratos/DTOs e `IOperationsService` ficam em Application. A implementação Infrastructure coordena Dapper, transações PostgreSQL, bloqueio pessimista, RLS e auditoria; controllers apenas traduzem HTTP e autorização. O arquivo SQL único e as migrações usam constraints como segunda linha de defesa.

## Slice Pecuária 360

Os contratos ficam em Application, regras invariantes no Domain e a orquestração transacional/Dapper em Infrastructure. O controller apenas traduz HTTP. Baixas sanitárias e nutricionais bloqueiam saldo, atualizam estoque e gravam o evento na mesma transação; todas as escritas críticas usam a trilha de auditoria compartilhada. O dashboard possui serviço e query consolidada próprios.

## Contextos Finance e Commercial (Sprint 8)

Contratos permanecem em Application; regras puras em Domain; serviços Dapper parametrizados em Infrastructure; controllers somente adaptam HTTP. Escritas financeiras são transacionais, filtradas por tenant e persistem auditoria. PostgreSQL aplica constraints, índices e RLS como defesa adicional.

## Armazenagem e logística (Sprint 9)

Regras determinísticas residem em Domain; contratos e comandos em Application; implementações Dapper em Infrastructure; controllers apenas traduzem HTTP. Descarga, transferência, processamento e despacho usam funções PostgreSQL transacionais para manter lote, capacidade, contrato e rastreabilidade atômicos. Todas as tabelas e queries operacionais aplicam `tenant_id`.
