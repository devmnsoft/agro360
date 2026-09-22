# Auditoria de estabilização — 2026-09-22

## Decisão do gate

**Gate bloqueado. Nenhum avanço funcional foi realizado.** O ambiente não possui o
SDK .NET nem cliente/servidor PostgreSQL. Assim, não é possível demonstrar build
Release, analyzers/nullable, execução da suíte, instalação limpa, reaplicação,
upgrade, RLS efetiva ou isolamento entre sessões. A regra de parada solicitada foi
aplicada antes de qualquer alteração funcional.

A auditoria encontrou, porém, dois seeds publicados ainda apontando para os
namespaces legados (`tenancy`, `identity`, `inventory` e `deployment`). Eles
falhariam depois da instalação do schema canônico único. A correção de baseline
foi feita antes de qualquer evolução da jornada: ambos agora usam `agro360`, e o
seed comercial reutiliza o tenant/usuário sem credencial que o instalador cria.

O inventário abaixo é uma auditoria estática: a presença de arquivo, endpoint,
tabela, regra ou teste é evidência de implementação parcial, nunca homologação do
fluxo completo.

## Escopo realmente inspecionado

- `README.md`, documentação de arquitetura, negócio e checkpoints em `docs/`;
- solução e projetos Domain, Application, Infrastructure, API, Web, Worker,
  Migrator e as três suítes de testes;
- entidades/regras, contratos, serviços Dapper, controllers e páginas Razor;
- layout/menu e testes estruturais de formulários sem IDs técnicos;
- migrations, seeds, releases e `database/agro360-postgres-full.sql`;
- marcadores de role, transação, RLS/policies e testes de fundação do banco.

## Matriz auditada

| Módulo | Classificação | Evidência estática | O que impede classificar como pronto |
|---|---|---|---|
| SaaS/Tenants/SuperAdmin | **Parcial** | `SaasGovernanceRules.cs`, `SaasService.cs`, `SaasControllers.cs`, página `Saas` e `SaasGovernanceTests.cs` cobrem tenants, planos, módulos, auditoria e rotas globais. | Backend, visão global, bloqueio de módulo e isolamento não foram exercitados com identidades e banco reais. |
| Usuários, perfis e ACL | **Parcial** | `IdentityContracts.cs`, `IdentityService.cs`, `IdentityController.cs`, policies e regressões de autenticação/role SuperAdmin. | Login, MFA, sessão, permissões e separação tenant/global não puderam ser executados. |
| Produtores, fazendas e talhões | **Parcial** | Domínio `Properties`, `PropertyService.cs`, `PropertiesController.cs`, página `Properties` e migration `051_properties_e2e.sql`. | CRUD, documentos, geolocalização, histórico e soft delete não foram validados ponta a ponta. |
| Culturas e safras | **Parcial** | `Season.cs`, contratos/serviços de agricultura e acompanhamento de safra, páginas `Agriculture`/`Harvest` e migrations 075–078/086. | Transições, estimativa, colheita excepcional e geração atômica de lote/estoque não foram executadas. |
| Pecuária | **Parcial** | Domínio `Livestock`, dois serviços operacionais, controller/página e regressões específicas. | Persistência, movimentações e isolamento não foram testados em PostgreSQL. |
| Insumos e estoque | **Parcial** | `Stock.cs`, `InventoryService.cs`, `StockControlService.cs`, controllers/página e migrations 005/082/087/088. | Saldo negativo, motivo, transferência, consumo e concorrência não foram exercitados. |
| Custos | **Parcial** | `SeasonCostRules.cs`, `SeasonCostService.cs`, controller/página `Costs` e migration `078_season_cost_allocation.sql`. | Apropriação real por safra/talhão/cultura/lote e dashboards não foram conciliados no banco. |
| Comercial, contratos e pedidos | **Parcial** | `CommercialRules.cs`, `Commercial360Service.cs`, controllers/página, migrations 098/099 e testes de saldo, total líquido, transições e comissão. | Cliente bloqueado, versionamento, desconto/aprovação e faturamento não foram validados como jornada integrada. |
| Logística | **Parcial** | `LogisticsService.cs`, `LogisticsController.cs`, página `Logistics` e migrations 008/071/088. | Expedição, entrega, ocorrência e atualização comercial não foram executadas ponta a ponta. |
| Financeiro | **Parcial** | `FinanceRules.cs`, `FinanceService.cs`, controller/página e migrations 006a/006z/007/036. | Contas, faturamento, cancelamento, margem e inadimplência não foram reconciliados em banco real. |
| Rastreabilidade | **Parcial** | Regras `GenealogyRules`/`PublicTraceabilityRules`, serviços/controllers público e interno, migrations 009/095/096 e testes contra exposição de `tenantId`. | Cadeia completa fazenda→entrega, imutabilidade e payload público não foram validados com dados persistidos. |
| Compliance/qualidade | **Parcial** | Domínio `Compliance`, serviços/controllers/páginas, migrations 090–093 e testes de formulários/fundação. | Validade, alertas, produtos configuráveis e bloqueios de lote não foram exercitados integralmente. |
| Dashboards e relatórios | **Parcial** | `DashboardService.cs`, serviços de inteligência, página inicial/`Reports` e contratos de dashboard. | Não foi demonstrado que todos os cards prioritários usam dados reais e tenant-scoped. |
| Notificações e auditoria | **Parcial** | Work management expõe notificações/alertas; outbox worker, audit logs, migration 070 e grants append-oriented existem. | Entrega, retries, eventos críticos e trilha das ações solicitadas não foram observados em execução. |

**Resumo:** nenhum módulo é “pronto com evidência” neste ambiente; não foi
encontrado módulo totalmente ausente, mas a cobertura transversal é parcial e as
garantias dinâmicas permanecem **não verificadas**. O gate de toolchain é
**quebrado no ambiente**, não uma conclusão de que o código-fonte esteja quebrado.

## Banco, migrations e segurança

### Evidência estática positiva

- O consolidado cria defensivamente `agro360_app NOLOGIN` antes dos grants.
- O validador estático confirma que o consolidado é autônomo, não contém
  `ROLLBACK` explícito, termina em `COMMIT`, mantém `ENABLE/FORCE RLS` e policy
  tenant-aware, e rejeita qualquer tentativa de desabilitar RLS.
- O gate de assets também verifica as migrations contra arquivos vazios e
  `ROLLBACK`, além de verificar todos os seis seeds distribuídos e rejeitar neles
  escrita ou leitura em namespaces legados.
- Existem `ENABLE/FORCE ROW LEVEL SECURITY`, policies tenant-aware e teste de
  integração que consulta tabelas tenant-aware, role e catálogo PostgreSQL.
- O Migrator declara lock, checksums e histórico; migrations e seeds usam padrões
  idempotentes em vários pontos.

### Limites e riscos

- Texto idempotente não prova reaplicação: somente duas execuções em base limpa e
  um upgrade representativo podem provar isso.
- RLS declarada não prova isolamento: proprietário de tabela, `BYPASSRLS`, grants,
  contexto de sessão e policies precisam de inspeção dinâmica.
- A criação de role exige privilégio administrativo; `NOLOGIN` evita que a role
  seja usada diretamente, mas os grants também precisam ser validados no catálogo.
- Migrations legadas com numeração próxima/duplicada (`006*`, `007*`, `064*`,
  `068*`, `097*`) exigem validação do ordenamento/checksum pelo Migrator; não devem
  ser renomeadas sem plano de compatibilidade.

## Bloqueio e causa raiz

| Verificação obrigatória | Resultado | Causa raiz |
|---|---|---|
| `dotnet restore` | **Bloqueada (exit 127)** | `dotnet` não está instalado ou não está no `PATH`. |
| `dotnet build -c Release` | **Bloqueada (exit 127)** | Sem o SDK fixado pelo repositório, compilador e analyzers não executam. |
| `dotnet test` | **Bloqueada (exit 127)** | Sem SDK, nenhuma suíte pode executar. |
| PostgreSQL limpo/reaplicação/upgrade | **Bloqueada** | `psql` e `pg_isready` não estão instalados; não há servidor verificável. |
| Validação estática de banco e seeds | **Passou** | `./scripts/validate-database-assets.sh`. |

Consequentemente, CA1859/CA1860/CA1861/CA1862/CA1716, erros C# citados,
materialização Dapper, nullable e compatibilidade binária permanecem **não
verificados dinamicamente**. Não houve suppression, remoção de teste/policy,
desativação de RLS nem alteração de schema.

## Alterações realizadas

- `database/seed-demo.sql`: migração das referências legadas para o schema
  canônico, correção do segmento inválido `MIXED` e reutilização da fixture demo;
- `database/seeds/sprint10-amazon-products.sql`: referências canônicas para tenant,
  usuário e produto;
- `scripts/validate-full-sql.sh`: regressões estáticas para RLS e `COMMIT` final;
- `scripts/validate-database-assets.sh`: gate único para consolidado e todos os
  seeds publicados.

Não houve avanço em código funcional, migrations, telas, contratos ou regras de
negócio porque o build e a instalação PostgreSQL continuam sem comprovação. Não
há mudança visual e, portanto, screenshot não é aplicável.

## Pendências para liberar o gate

1. Disponibilizar o SDK exigido por `global.json` e executar restore, build Release
   e todas as suítes sem ignorar analyzers.
2. Disponibilizar PostgreSQL/PostGIS e ferramentas cliente; criar banco descartável
   e aplicar o consolidado com `ON_ERROR_STOP=1` duas vezes.
3. Executar o Migrator sobre uma base representativa existente para validar
   ordenamento, checksums, upgrade e preservação de dados.
4. Consultar `pg_roles`, `pg_class.relrowsecurity/relforcerowsecurity`, `pg_policy`
   e grants; executar testes concorrentes com SuperAdmin e dois tenants.
5. Somente após o gate verde, fechar uma jornada vertical por vez (operação e
   colheita primeiro), sempre com banco, regra, endpoint, tela e teste E2E.

## Próximo passo recomendado

Preparar uma imagem/runner reproducível com .NET 10, PostgreSQL/PostGIS e `psql`,
executar o gate E0 documentado no README e anexar os logs. Se o gate passar,
priorizar a jornada **safra → manejo → colheita → lote → estoque → custo**, pois ela
forma a base para comercial, logística, rastreabilidade e financeiro sem criar
estruturas paralelas.
