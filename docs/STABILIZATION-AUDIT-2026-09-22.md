# Auditoria de estabilização — 2026-09-22

## Escopo e gate

Esta auditoria precede qualquer avanço funcional. O gate não foi liberado porque o SDK .NET 10 e um cliente/servidor PostgreSQL não estão disponíveis no ambiente de execução. Por isso, nenhuma funcionalidade dos blocos B–E foi adicionada.

## Estrutura verificada

| Camada | Evidência | Situação |
|---|---|---|
| Domain | `src/Modules/Agro360.Domain` | Presente |
| Application | `src/Modules/Agro360.Application` | Presente |
| Infrastructure | `src/Modules/Agro360.Infrastructure` | Presente |
| Web/API | `src/Hosts/Agro360.Web`, `src/Hosts/Agro360.Api` | Presente |
| Worker | `src/Hosts/Agro360.Worker` | Presente |
| Migrator | `src/Hosts/Agro360.Migrator` | Presente |
| Tests | `tests/Agro360.UnitTests`, `tests/Agro360.IntegrationTests`, `tests/Agro360.ArchitectureTests` | Presente; não executado por falta do SDK |
| Banco | `database/migrations`, `database/seeds`, `database/agro360-postgres-full.sql` | Presente; execução real não verificada |

## Matriz dos módulos

As classificações abaixo comprovam somente a existência das camadas indicadas; não equivalem à homologação de um fluxo ponta a ponta.

| Área | Classificação | Evidência |
|---|---|---|
| SaaS/Tenants | Parcial | `SaasService.cs`, `SaasControllers.cs`, páginas `Saas` e testes `SaasGovernanceTests.cs` |
| Users/Auth/ACL | Parcial | `IdentityService.cs`, `IdentityController.cs` e `AuthenticationAndLivestockRegressionTests.cs` |
| Producers/Farms/Fields | Parcial | domínios `Properties`/`Agriculture`, serviços correspondentes e páginas `Properties`/`Agriculture` |
| Crops/Harvests | Parcial | `HarvestService.cs`, páginas `Harvest` e migrations de colheita |
| Inventory | Parcial | domínio, serviço, controller e páginas `Inventory` |
| Commercial/Sales/Contracts | Parcial | domínio `Commercial`, `Commercial360Service.cs`, controllers, páginas e testes de regras comerciais |
| Compliance/Quality | Parcial | domínio, serviço, controllers e páginas `Compliance`/`Inspections` |
| Logistics | Parcial | `LogisticsService.cs`, controller e páginas `Logistics` |
| Finance | Parcial | domínio, serviço, controller, páginas e migrations de finanças |
| Reports/Dashboard | Parcial | `DashboardService.cs`, páginas `Reports` e serviços de inteligência |
| Audit/Notifications | Parcial | tabelas/eventos no instalador e serviços operacionais; fluxo real não verificado |

Nenhuma área foi marcada como **Pronta com evidência**, pois build, testes, banco limpo, upgrade e fluxos funcionais não puderam ser executados neste ambiente.

## Bugs e riscos verificados estaticamente

- A criação idempotente de `agro360_app NOLOGIN` está no início do instalador consolidado, antes das concessões posteriores.
- O instalador preserva comandos de RLS e termina em `COMMIT`; não foi encontrado `ROLLBACK` explícito.
- A migration `098_commercial_contract_operations.sql` também cria a role defensivamente antes dos seus `GRANT`s.
- Há teste de integração que consulta a role e exige RLS/policies nas tabelas críticas, mas ele depende de `AGRO360_TEST_CONNECTION_STRING` e de uma base previamente instalada.
- A validação estática agora falha se a primeira menção à role não for sua criação ou se houver `ROLLBACK` explícito. Um teste de arquitetura cobre a mesma regressão e confirma que os marcadores de RLS continuam no instalador.
- CA1859, CA1860, CS1503, nullable, contratos duplicados e materialização Dapper permanecem **não verificados dinamicamente**, pois o compilador/analyzers não puderam rodar.

## Comandos e resultados

- `dotnet restore && dotnet build -c Release --no-restore && dotnet test -c Release --no-build`: não executado; o shell retornou `dotnet: command not found`.
- Consultas PostgreSQL solicitadas: não executadas; `psql` e uma instância de banco não estão disponíveis.
- `scripts/validate-full-sql.sh database/agro360-postgres-full.sql`: validação estática local disponível, sem substituir a instalação em banco limpo.

## Pendências para liberar o gate

1. Instalar o SDK fixado por `global.json` (10.0.100) e executar restore, build Release e toda a suíte.
2. Criar uma base PostgreSQL limpa com um usuário autorizado a criar roles e executar o instalador com `ON_ERROR_STOP=1`.
3. Executar novamente o instalador para validar idempotência e executar um upgrade representativo via Migrator.
4. Executar as consultas de role, policies e `rowsecurity`, além dos testes de integração com `AGRO360_TEST_CONNECTION_STRING`.
5. Só após todos os itens anteriores avançar para implementação funcional e validação visual.

## Riscos técnicos

- A presença textual de RLS não prova cobertura de todas as tabelas tenant-aware; a consulta dinâmica de integração é a evidência necessária.
- Testes estáticos não detectam incompatibilidades de construtores Dapper, nullable ou analyzers.
- Criar roles requer privilégio PostgreSQL apropriado; a conta de instalação deve possuir esse privilégio sem concedê-lo à role de aplicação `NOLOGIN`.
