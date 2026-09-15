# Auditoria funcional de formulários — 2026-09-15

## Escopo, baseline e método

Baseline: repositório `devmnsoft/agro360`, branch `work`, HEAD inicial `b1ce454`, solução `MNSOFT.Agro360.sln` e SDK fixado em .NET `10.0.100` por `global.json`. Foram lidos `README.md`, `CONTRIBUTING.md`, `.github/copilot-instructions.md`, o plano mestre e os documentos de execução. O worktree estava limpo. O contêiner não possui `dotnet`, PostgreSQL nem configuração descartável `AGRO360_TEST_CONNECTION_STRING`; por isso restore/build/test, inicialização e cenários destrutivos reais ficam classificados como **não executados por limitação**, e não como aprovados.

O inventário mecânico encontrou **44 Razor Pages**, **36 páginas operacionais com 80 elementos `<form>`** (mais dois formulários de infraestrutura no layout compartilhado), 36 scripts JavaScript de módulo e 40 controllers de API. A busca incluiu `form`, `dialog`, `fetch`, ações com `data-action`, upload, exportação, aprovação, cancelamento, estorno e arquivamento. “Presente” abaixo significa somente que o encadeamento foi localizado no código; não comprova funcionamento.

Classificações: **verificada** (execução e releitura realizadas), **com defeito** (falha reproduzida/confirmada), **incompleta** (contrato ou fluxo sem requisito necessário), **não aplicável**, e **não executada — ambiente**. As verificações estáticas são explicitamente separadas das funcionais.

## Matriz página → operação → persistência

Em todos os módulos autenticados, o controller correspondente usa contratos em `Agro360.Application.Contracts`, serviço `I*Service`/`*Service`, Dapper no schema `agro360` e políticas declaradas por `Permissions`; a autorização de cada ação ainda precisa ser exercitada com token permitido, negado e de outro tenant. A coluna “contrato/serviço/tabelas” registra o encadeamento localizado; `família do módulo` indica as tabelas prefixadas do módulo no instalador, cuja operação individual ainda não foi exercitada.

| Página/rota (forms) | Controller/endpoint | Contrato → serviço → repositório/tabelas | Operações previstas | Permissão | Resultado desta rodada |
|---|---|---|---|---|---|
| Agriculture `/agriculture` (1) | `AgricultureController`, `/api/agriculture` | contratos Agriculture → `IAgricultureService` → Dapper/tabelas `agriculture_*` | consulta, inclusão, transições | políticas Agriculture | não executada — ambiente |
| Commercial `/Commercial` (1) | `CommercialController` | contratos Commercial → serviço Commercial → `commercial_*` | consulta, vendas e ações de estado | políticas Commercial | não executada — ambiente |
| Compliance `/compliance` (1) | `ComplianceControllers` | contratos Compliance → serviço Compliance → `quality_*` | consulta, registros, aprovação | políticas Compliance | não executada — ambiente |
| Cooperatives `/Cooperatives` (2) | `CooperativeControllers` | contratos Cooperative → serviço Cooperative → família do módulo | consulta, inclusão, ações compostas | políticas Cooperative | não executada — ambiente |
| Costs `/Costs` (2) | `SeasonCostsController` | contratos SeasonCosts → serviço SeasonCosts → `cost_*` | filtros, apropriação, fechamento | políticas Costs | não executada — ambiente |
| CRM `/crm` (1) | `CrmSaasController` | contratos CRM → serviço CRM → `crm_*` | consulta, inclusão e estados | políticas CRM | não executada — ambiente |
| Deployment `/Deployment` (2) | `DeploymentController` | contratos Deployment → serviço Deployment → família do módulo | consulta e comandos operacionais | políticas Deployment | não executada — ambiente |
| Documents `/Documents` (2) | `DocumentsController` | multipart/contratos Documents → serviço/storage → `document_*` + storage externo | consulta, upload, vínculo, arquivo | políticas Documents | não executada — provedor/banco ausentes |
| Ecosystem `/ecosystem` (1) | `EcosystemController` | contratos Ecosystem → serviço Ecosystem → família do módulo | consulta, inclusão e integração | políticas Ecosystem | não executada — ambiente |
| Export `/Export` (2) | `ExportTradingController` | contratos Export → serviço Export → `export_*` | consulta, clientes, exportação | políticas Export | não executada — ambiente |
| Field `/field` (4) | `MobileControllers` | contratos Mobile/Offline → `IMobileService` → `mobile_*` | registro rápido, fila, sincronização | políticas Field | não executada — geolocalização/banco ausentes |
| Fiscal `/Fiscal` (1) | `FiscalController` | contratos Fiscal → serviço/provider → `fiscal_*` | consulta, emissão/cancelamento específicos | políticas Fiscal | não executada — provedor/banco ausentes |
| Fleet `/fleet` (1) | `FleetController` | contratos Fleet → `IFleetService` → `fleet_*` | consulta, manutenção, abastecimento | políticas Fleet | não executada — ambiente |
| Governance `/governance` (1) | `DataGovernanceController` | contratos Governance → serviço → auditoria/governança | filtro/exportação; eventos imutáveis | políticas Governance | não executada — ambiente |
| Harvest `/Harvest` (1) | `HarvestController` | contratos Harvest → serviço Harvest → `harvest_*` | consulta, apontamento, fechamento | políticas Harvest | não executada — ambiente |
| Integrations `/Integrations` (1) | `IntegrationsController` | contratos Integrations → serviço/provider → `integration_*` | consulta, configuração e reprocesso | políticas Integrations | não executada — provedor/banco ausentes |
| Intelligence `/intelligence` (6) | `IntelligenceController` | contratos Intelligence → serviço → `intelligence_*` | filtros, feedback e ações | políticas Intelligence | não executada — ambiente |
| Intelligence360 `/inteligencia-agro360` (1) | `OperationalIntelligenceController`/`ExecutiveIntelligenceController` | contratos Intelligence → serviços → família do módulo | consulta e filtros | políticas Intelligence | não executada — ambiente |
| Livestock `/livestock` (1) | `LivestockController`/`Livestock360Controller` | contratos Livestock → serviços → `livestock_*` | consulta, cadastro e eventos de negócio | políticas Livestock | não executada — ambiente |
| Logistics `/logistics` (1) | `LogisticsController` | contratos Logistics → serviço → `logistics_*` | consulta, inclusão e transições | políticas Logistics | não executada — ambiente |
| Maps `/Maps` (3) | `MapsController` | contratos Maps → serviço → dados geo no schema | filtros, camadas e consultas | políticas Maps | não executada — ambiente |
| Portal Accept `/Portal/Accept` (1) | `PortalController` | request de aceite → serviço Portal → `portal_*` | aceite de convite | acesso por token específico | não executada — ambiente |
| Portal Login `/Portal/Login` (1) | `PortalController` | login request → serviço Portal/Identity → identidades/sessões | autenticação externa | anônimo + controles de sessão | não executada — ambiente |
| Portal Marketplace `/Portal/Marketplace` (2) | `PortalController` | requests Portal → serviço Portal → `portal_*` | pesquisa, solicitação/contratação | políticas Portal | não executada — ambiente |
| Portal Requests `/Portal/Requests` (1) | `PortalController` | requests Portal → serviço Portal → `portal_*` | consulta e solicitações | políticas Portal | não executada — ambiente |
| Procurement `/Procurement` (10) | `ProcurementController`, `/api/procurement/*` | contratos Procurement → `IProcurementService` → `procurement_*`, estoque/financeiro | catálogo, requisição, pedido, recebimento, aprovação, cancelamento | políticas Procurement por ação | não executada — ambiente |
| Production `/Production` (11) | `IndustrialProductionController` | contratos Production → serviço → `production_*` | fórmulas, ordens, itens, qualidade, cancelamento | políticas Production por ação | não executada — ambiente |
| Properties `/properties` (3) | `PropertiesController`, `/api/v1/properties`, `/api/v1/fields` | `Create/Update*Command` → `IPropertyService` → `PropertyService` → `geo_farms`, `geo_fields`, `organization_organizations`, audit/outbox | incluir, consultar/paginar, editar, arquivar logicamente | `properties.read`/`properties.write` | **incompleta funcionalmente por ambiente; defeitos estáticos corrigidos** |
| Reports `/Reports` (1) | controllers de relatório por módulo | requests de filtro → serviços de relatório → tabelas autorizadas | filtro, paginação/exportação | políticas Reports/Export | não executada — ambiente |
| RuralHr `/RuralHr` (2) | `RuralHrController` | contratos RuralHr → serviço → `hr_*` | consulta, cadastros e alterações de estado | políticas RuralHr | não executada — ambiente |
| SaaS Accept `/Saas/Accept` (1) | `SaasControllers` | request de convite → `ISaasService` → `saas_*`/identidade | aceite de convite | token específico | não executada — ambiente |
| SaaS `/saas` (1) | `SaasControllers` | contratos SaaS → `ISaasService` → `saas_*` | organização, usuários, perfis, contratos e estados | políticas SaaS/Admin | não executada — ambiente |
| SST `/Sst` (4) | `SstController` | contratos SST → serviço SST → `sst_*` | trabalhador, risco, incidente e ações | políticas SST | não executada — ambiente |
| Support `/support` (3) | `SupportController` | contratos Support → serviço → `support_*` | tickets, mensagens e estados | políticas Support | não executada — ambiente |
| Sustainability `/sustentabilidade` (1) | `SustainabilityController` | contratos Sustainability → serviço → `sustainability_*` | consulta, indicadores e registros | políticas Sustainability | não executada — ambiente |
| Work `/Work` (2) | `WorkManagementController` | contratos Work → serviço → `work_*` | filtro, tarefas e comandos de estado | políticas Work | não executada — ambiente |

## Fluxo vertical corrigido: Fazendas e Talhões

A inspeção encontrou quatro defeitos verificáveis sem banco: (1) a Web requisitava até 100 registros e tratava a página como conjunto completo; (2) não havia controles de paginação; (3) a mensagem de sucesso era exibida antes de uma nova consulta; (4) não existia consulta por identificador para confirmar o valor recém-persistido.

Correções implementadas:

1. GET tenant-scoped e excluindo arquivados para fazenda e talhão por ID, protegido por `properties.read`.
2. Após POST/PUT, a Web relê o ID por uma nova requisição e só então fecha o diálogo e mostra confirmação.
3. Após DELETE lógico, a lista ativa é consultada novamente antes da confirmação; banco já registra `deleted_at`, `deleted_by`, autoria, versão e auditoria.
4. Paginação server-side de 25 itens, total vindo do backend, navegação anterior/próxima e busca preservada.
5. Duplo envio continua bloqueado por botão desabilitado e `aria-busy`; `finally` sempre restaura o botão. Conflito otimista usa `version` e HTTP de conflito do middleware.
6. O fluxo mantém organização e fazenda como seletores autorizados, sem pedir GUID ao usuário; área, UF, GeoJSON, duplicidade, vínculo tenant/fazenda e dependências são revalidados no backend.

### Resultado por operação de Properties

| Operação | Verificação estática | Verificação funcional |
|---|---|---|
| inclusão válida/inválida e releitura | contrato, validação, transação, audit, retorno e nova GET presentes | não executada — .NET/PostgreSQL ausentes |
| consulta/filtro/paginação/detalhe | filtro e total no SQL; tenant, `deleted_at`, `limit/offset`; ordem por nome | não executada — PostgreSQL ausente |
| edição válida/inválida/concorrente | allowlist do contrato, autoria backend, row count e `version` | não executada — PostgreSQL ausente |
| arquivamento e consulta posterior | `deleted_at/deleted_by`, dependência de talhões, audit, versão e reload | não executada — PostgreSQL ausente |
| sem permissão/outro tenant | policies no controller e `tenant_id=@TenantId` em toda consulta | não executada — tokens/banco ausentes |
| clique repetido | botão desabilitado até `finally` | não executada — navegador/API ausentes |

## Pendências reproduzíveis

1. Instalar .NET SDK 10.0.100 e PostgreSQL 14+ com PostGIS, `pgcrypto`, `pg_trgm` e `unaccent`.
2. Apontar `AGRO360_TEST_CONNECTION_STRING` somente para banco descartável contendo `test`/`teste` e executar `./scripts/test-local.sh`.
3. Executar `./scripts/verify.sh`, migrator, API e Web; aplicar os cenários da tabela acima com dois tenants e perfis read/write/denied.
4. Continuar a matriz na ordem do plano mestre. As 35 páginas não exercitadas não estão aprovadas por esta auditoria e ações de provedor externo devem permanecer pendentes até configuração real.

Não houve alteração estrutural de banco nesta rodada; migration e atualização do SQL consolidado não se aplicam. Nenhum binário foi adicionado.

## Incremento 8.0 — recebimento assistido, qualidade e liberação (2026-09-15)

| Página/ação | Inclusão e releitura | Transição/histórico | Permissão e tenant | Estado verificável |
|---|---|---|---|---|
| Procurement / novo recebimento | POST idempotente, saldo relido sob `FOR UPDATE`, lotes preservados por linha | presença física separada da disponibilidade; serviço não movimenta estoque | `purchasing.receive`, override separado, SQL tenant-scoped | verificação estática; runtime pendente por ausência de SDK/PostgreSQL |
| Procurement / detalhe | GET por ID relê resumo, itens, quarentena e decisões | sem edição/exclusão genérica | `purchasing.read`, joins por tenant | verificação estática |
| Procurement / inspeção/liberação | decisão parcial idempotente; nova consulta do detalhe após POST | histórico append-only; somente aceito gera movimento; rejeitado permanece rastreável | `compliance.approve`, RLS e tenant no comando | verificação estática; concorrência exige PostgreSQL descartável |
| Procurement / listagem | pedido, fornecedor, data, status e próxima ação | filtros continuam no formulário ao abrir/fechar detalhe | `purchasing.read` | verificação estática |

A migration `080_assisted_procurement_receipt.sql` cria uma representação de quarentena vinculada ao item, decisões históricas e vínculos idempotentes de liberação. Não foi implementado estorno/devolução nem encerramento autorizado de saldo neste incremento; essas operações permanecem pendências reais e não foram substituídas por exclusão genérica.
