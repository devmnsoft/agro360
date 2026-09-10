# Matriz de rastreabilidade v0.2.0

## Atualização pós-PR #98 — 2026-09-10

| Jornada | Estado | Camadas presentes | Lacunas para avanço |
|---|---|---|---|
| Administração MNSOFT | **parcial** | tela/API/serviço/tabelas SaaS e policies | MFA e assistência auditada persistente; uso/cobrança real; homologação de autorização por URL/export |
| Administração do Cliente | **parcial** | usuários/perfis/convites em SaaS | limites delegáveis, unidade, último administrador, revogação de sessões e E2E multi-tenant |
| Contratação modular | **parcial** | catálogo/planos/entitlements e cobrança interna | consolidar fontes, snapshot/dependências/transições e confirmação real de pagamento |
| Compras → qualidade → estoque → financeiro | **parcial** | tela, endpoint, serviço transacional, persistência e permissões | inspeção/liberação/rejeição/estorno completos e homologação de concorrência/rollback |
| Comercial → reserva → entrega → financeiro | **parcial** | pedido/preço/snapshot e camadas comerciais | reserva, entrega/devolução, recebível parcial e E2E concorrente |
| Produção agroindustrial | **parcial** | ordem/apontamento/qualidade e persistência | roteiro versionado, reservas/consumo, lote idempotente, rendimento/custo e E2E |
| Estoque, financeiro, CRM e qualidade isolados | **implementado sem homologação** | interface/API/serviço/persistência/autorização | integração ponta a ponta e execução em PostgreSQL/navegador |

AG-E0-003 passa de **com defeito** para **implementado não validado**: `006z_finance_receivables_legacy_bridge.sql` preserva a tabela da 001 antes da 007 e `007z_finance_receivables_legacy_data.sql` migra títulos válidos sem modificar checksums publicados. AG-E1-004 permanece **implementado não validado**; foi corrigida a divergência do ID Santa Clara (`...003`) entre validação e upsert.

## Consolidação atual — plano mestre Agro360 (2026-09-08)

Esta é a matriz canônica de execução; o nome do arquivo e as seções anteriores de sprint são preservados por compatibilidade. A seção atual prevalece sobre registros históricos, sem transformar resultados do banco descartável em certificação do ambiente do usuário. Fonte: [mestre integral](execucao/AGRO360-MASTER-PLAN.md); prioridades/aceites: [plano](execucao/EXECUTION-PLAN.md); execução: [checkpoint](EXECUTION-CHECKPOINT.md).

Estados: **não iniciado**, **parcial**, **com defeito**, **implementado não validado**, **validado**, **bloqueado**. Não há percentual de conclusão. O inventário abaixo cobre famílias identificadas em código, não certifica cada ação de todos os controllers. O detalhamento de cada ação/DTO/query é obrigatório antes de promover sua família a validada.

**Atualização AG-E1-004 (2026-09-09): implementado não validado.** O
provisionamento das duas fixtures agora é opt-in, bloqueia Production, usa o
hasher real e MFA/Data Protection persistente, revoga sessões e audita sem
segredos. O ambiente atual não possui .NET/PostgreSQL; portanto nenhum login foi
declarado ativo e os gates de banco/navegador permanecem obrigatórios.

**Atualização pós-PR #97 (2026-09-10):** AG-E1-004 permanece **implementado não validado**, agora com contrato explícito de Data Protection, validação de identidade/tenant/ID e provisionador PowerShell. AG-E5-001 permanece **parcial / implementado não validado**: fingerprint de idempotência, bloqueio de unidade incompatível, parcelas positivas e autorização específica de excesso estão na migration 067 e no serviço/controller. Quarentena, liberação única, estado agregado completo, reversão e homologação PostgreSQL continuam pendentes.

### Fatia verificada e lacunas prioritárias

| ID | Jornada / atores | Cadeia e efeitos | Estado e evidência |
|---|---|---|---|
| AG-E0-001 | Operação: instalação → prontidão → login → consulta → refresh/logout | `DatabaseHealthCheck` → metadados PostgreSQL; `Index.cshtml`/`agro360.js` → `POST /api/v1/auth/login` → `LoginCommand` → `IdentityService` → `tenancy_tenants`, `identity_users`, roles/permissions/entitlements; hash real, refresh persistido/revogado. `GET /api/v1/dashboard/command-center` → `CommandCenterResult` → `DashboardService` → fazendas, safras, animais, estoque, recebíveis/custos/alertas → `dashboard.read` e módulos reports/analytics | **validado no recorte HTTP/SQL**: banco vazio 503, instalado 200, login/dashboard 200, anônimo 401, refresh 200/replay 401, logout 204/revogado 401. Instalador executado e repetido. Não valida toda E1 |
| AG-E0-002 | Usuário: testar conexão na tela de acesso | `agro360.js::testApiConnection` → `/health`, Swagger; `service-worker.js` controla só shell público; sem escrita de negócio | **validado**: navegador antes mostrava sucesso com API inacessível por fallback HTML. Depois mostra falha e URL correta; health exige `Healthy`, Swagger exige JSON OpenAPI. Cache de bootstrap sem tenant removido |
| AG-E0-003 | Operação: atualização de versão com dados | `Agro360.Migrator/Program.cs` → `platform_schema_migrations`; `001_foundation.sql` cria schemas legados, instalador cria `agro360` | **com defeito**: `verify-e0.ps1 -CheckMigrations` falha na 007 com 42703, `due_on` inexistente; 001 define `due_date`. Upgrade com dados anteriores ainda não homologado. Não alterar checksums nem rodar em produção |
| AG-E0-004 | Integração do worktree | contratos/propriedades/SaaS/SQL e testes recebendo alterações concorrentes | **parcial**: última reexecução completa em `e0-8072988ddd92424aaa3eb21ec817af3f` passou restore/build, HTTP/SQL e 118 testes, zero ignorados. Integração de autoria/commit permanece pendente. Histórico e fronteira de autoria no checkpoint |
| AG-E1-001 | Pessoa → vínculo → contexto | Login exige tenant antes da pessoa; `identity_users` contém `tenant_id`; normalizador aceita 11/14 dígitos e consulta documento do usuário | **parcial**: login real existe; identidade global/vínculos/seleção pós-login e CNPJ organizacional não estão entregues. Não confundir identidade com cliente |
| AG-E1-002 | SuperAdmin → MFA → suporte assistido | `SaasControllers` restringe plataforma por role; schema contém apenas `mfa_enabled` para MFA | **não iniciado no recorte MFA real/assistência auditada**: não localizado desafio/verificação MFA nem sessão de assistência no código inspecionado. Administração global já possui endpoints; não significa E1 completa |
| AG-E1-003 | Admin cliente → usuários/perfis/convites | `/Saas`, `saas.js` → `SaasControllers`, `SaasContracts`, `SaasService` → `identity_*`, `saas_*`; permissões de usuários em evolução concorrente | **parcial**: revisar guards, revogação de tokens e último administrador. Não homologado nesta rodada; não duplicar entrega concorrente |
| AG-E1-004 | Operação → demo/provisionamento | instalador inclui Santa Clara e bootstrap fixo; `PasswordHasher` PBKDF2 real | **com defeito frente ao mestre**: demo não é opt-in e há credenciais universais na documentação histórica. Gate E0 usa senha aleatória só no banco descartável; isso não corrige o seed de produto |
| AG-E2-001 | Cliente/MNSOFT → catálogo/contrato | `SaasService`, `EcosystemService`, `CrmSaasService`; login combina `saas_plans`, `platform_module_catalog`/entitlements e `platform_marketplace_modules`/tenant_modules | **parcial**: fontes comerciais concorrentes; snapshot/preço/aceite/dependências e expiração de todos os caminhos precisam ser demonstrados |
| AG-E3-001 | Cobrança SaaS e split | `SaasControllers` billing/`SaasService`; `Sprint10Service::ControlledPaymentSplitProvider` retorna `SIM-...`, registrado em DI | **com defeito no split externo**: simulação não comprova pagamento. Cobrança interna existente **implementada não validada**; provedor real e reconciliação permanecem pendentes |

### Inventário operacional inicial

Convenções de caminho: controllers em `src/Hosts/Agro360.Api/Controllers`; contratos em `src/Modules/Agro360.Application/Contracts`; implementações em `src/Modules/Agro360.Infrastructure/Services`; páginas em `src/Hosts/Agro360.Web/Pages`. Tabelas atuais usam schema `agro360` e prefixos de módulo; os nomes pontuados da matriz histórica abaixo não são os nomes canônicos atuais. Policies constam nos controllers e em `Application/Permissions.cs`; o mapeamento comercial atual é `IdentityService.IsPermissionContracted` e deve ser substituído/consolidado em E2, não tratado como catálogo aprovado.

| ID / módulo | Página/ação → controller → contrato/serviço → dados | Permissão / contrato atual | Maturidade observada / próximo aceite |
|---|---|---|---|
| AG-E4-001 / Fazendas e unidades | `Properties/Index`, listar/criar/editar/arquivar → `PropertiesController` → `PropertyContracts`/`PropertyService` → `geo_farms`, `geo_fields`, `organization_organizations` | properties.* / properties | **parcial**, fluxo em edição concorrente; CRUD/área/conflito/FKs/tenant precisam de gate próprio |
| AG-E4-002 / Estoque | atalhos `/` → `InventoryController` → `InventoryContracts`/`InventoryService` → `inventory_products`, warehouses/balances/movements | inventory.* / inventory | **implementado não validado**; provar reservas/estornos/concorrência, não só KPI |
| AG-E4-003 / Seletores e busca | formulários/busca → `LookupsController`, `DiscoveryController` → contratos/`LookupService`, `GlobalSearchService`, `TraceabilityService` | policy da rota / módulo do dado | **implementado não validado**; busca/export não podem ampliar tenant/unidade |
| AG-E5-001 / Compras | `Procurement`, compras → `ProcurementController` e `OperationsController` → `ProcurementContracts`/`ProcurementService`, `OperationsService` → `procurement_*`, `purchasing_*` | purchasing.* / purchasing | **parcial**; commit local preservado integra recebimento a estoque/título com idempotência; dois cadastros de fornecedor e homologação PostgreSQL/rollback/concorrência ainda pendentes |
| AG-E5-002 / Comercial | `Commercial` → `CommercialController`, `Commercial360Controller`, `AgroSalesController` → contratos/`CommercialService`, `Commercial360Service`, `SalesService` → `commercial_*`, `sales_*`, recebíveis | commercial.* / commercial | **parcial**; transição do pedido normalizada, fechada e bloqueada em transação; alçada considera preço-base + preço negociado + desconto e grava snapshot da política. Pendente homologar banco, override, reserva/entrega/financeiro |
| AG-E5-003 / CRM | `Crm` → `CrmSaasController` → `CrmSaasContracts`/`CrmSaasService` → `crm_*` | crm.*, commercial-saas.* / commercial | **implementado não validado**; não confundir jornada SaaS com venda rural |
| AG-E5-004 / Financeiro/controladoria | `Finance` → `FinanceController` → contratos/`FinanceService` → `finance_*`, `cost_*` | finance.* / finance | **implementado não validado**; parcelas/baixas parciais/estorno/DRE conciliados |
| AG-E6-001 / Agricultura | `Agriculture` → `AgricultureController`, `Agriculture360Controller` → contratos/`AgricultureService`, `Agriculture360Service` → `agriculture_*` | agriculture.* / agriculture | **implementado não validado**; planejamento/campo/insumo/colheita/custo ponta a ponta |
| AG-E6-002 / Pecuária e especialidades | `/` seção livestock → `LivestockController`, `Livestock360Controller` → `Livestock360Contracts`/serviços → `livestock_*`, pastagem/leite associados | livestock.* / livestock | **implementado não validado**; regras/materialização possuem testes, jornada de manejo não foi percorrida |
| AG-E7-001 / Agroindústria | `Production` → `IndustrialProductionController` → contratos/`IndustrialProductionService` → `production_*` | production.* / agroindustry | **parcial**; apontamento valida estado, linha, operador e critérios com etapa anulável; conclusão usa status efetivo do lote aprovado, não aprovação histórica. Pendente roteiro versionado, banco, idempotência, consumo/reserva e retificação |
| AG-E7-002 / Qualidade e conformidade | `Compliance`, `/` rastreabilidade → `ComplianceControllers`, `Sprint10Controllers` → contratos/`ComplianceService`, `Sprint10Service` | compliance.*, traceability.* / environment-esg, traceability | **implementado não validado**; evidência, reprovação e bloqueio nos consumidores |
| AG-E8-001 / Armazenagem | `/` storage → `StorageController` → contratos/`StorageService` → `storage_*` | storage.* / inventory, warehousing | **implementado não validado**; pesagens, qualidade, lote, capacidade e expedição |
| AG-E8-002 / Logística e contratos | `/`, `Maps` → `LogisticsController`, `DeliveryContractsController`, `Sprint10Controllers` → `LogisticsService`, `DeliveryContractService`, `Sprint10Service` | logistics.*, regional-logistics.* / logistics | **implementado não validado**; parcial/ocorrência/prova entrega e custos |
| AG-E8-003 / Frota/manutenção/SST | `Fleet`, `Sst` → `FleetController`, `SstController`, `OperationsController` → `FleetService`, `SstService`, `OperationsService` → `fleet_*`, manutenção/SST | fleet.*, maintenance.*, sst.* / fleet, verticals/rural-hr | **implementado não validado**; peças, abastecimento, custo e evidências |
| AG-E8-004 / GED, uploads e certificados | `Documents`, portal → `DocumentsController` → `DocumentContracts`/`DocumentService` → `document_*`, metadados/storage | documents.*, evidences.*, dossiers.*, certificates.* / documents | **implementado não validado**; conteúdo real, download autorizado, validade, exclusão lógica e storage |
| AG-E8-005 / Fiscal | `Fiscal` → `FiscalController` → contratos/`FiscalService`, `FiscalEmissionService` e registry | fiscal.* / fiscal | **bloqueado para emissão externa**: registry recebe providers mas DI inspecionada não registra implementação real; estado NOT_CONFIGURED explícito. Fluxo interno não homologado |
| AG-E9-001 / Campo/mobile/offline | `Field`, service worker, `Mobile.Core` → `MobileControllers` → contratos/`MobileService` → fila e `mobile_*` | mobile.*, field-checklists.* / mobile | **parcial**: shell público separado nesta E0; reautorização, filas e conflitos multi-tenant ainda não demonstrados |
| AG-E9-002 / Mapas/GeoJSON | `Maps` → `MapsController` → contratos/`GeospatialService` → `geo_*` | maps.* / properties, analytics | **implementado não validado**; permissões, import/export, extensão/geometria e unidades |
| AG-E9-003 / BI/relatórios/inteligência | `Reports`, `Intelligence`, `Intelligence360` → `IntelligenceController`, `OperationalIntelligenceController`, `ExecutiveIntelligenceController` → serviços respectivos | intelligence.* / reports, intelligence, analytics etc. | **implementado não validado**; filtros, CSV, fontes/períodos e volume; não vender IA/provedor não demonstrado |
| AG-E9-004 / Integrações/API/webhooks/IoT | `Integrations` → `IntegrationsController` → `IntegrationService` e contratos | integrations.* / platform, marketplace | **implementado não validado**; assinatura/replay/segredos/dados externos e custo; conexão real não testada |
| AG-E9-005 / Trading/exportação | `Export` → `ExportTradingController` → `ExportTradingService`/contratos → `export_*` | export.* / export | **implementado não validado**; operação, documentos e provedores reais |
| AG-E9-006 / Idiomas/UX | layout/scripts/CSS, `docs/MULTILANGUAGE.md` | visualização não concede permissão | **parcial**; navegador verificou login/ajuda/erro, não responsividade completa. Indicador climático fixo removido; conectividade e responsividade ainda exigem homologação |
| AG-E10-001 / Cooperativas/ecossistema | `Cooperatives`, `Ecosystem` → `CooperativeControllers`, `EcosystemController` → serviços/contratos → `cooperative_*`, `platform_*` | cooperative.*, marketplace.*, partners.* / cooperatives, platform | **implementado não validado**; recebimento/assistência/marketplace e efeitos contábeis |
| AG-E10-002 / RH rural | `RuralHr` → `RuralHrController` → `RuralHrService`/contratos → `rural_hr_*` | rural-hr.* / verticals, rural-hr | **implementado não validado**; privacidade de pessoas, cargos vs perfis, custo/SST |
| AG-E10-003 / ESG/sustentabilidade | `Sustainability`, `Compliance` → `SustainabilityController`, `ComplianceControllers` → `SustainabilityService`, `EsgService`, `ComplianceService` | sustainability.*, esg.* / environment-esg | **implementado não validado**; metodologia/fatores/versionamento e evidências |
| AG-E10-004 / Portal externo | `Portal/*` → `PortalController` → `PortalService`/contratos → `portal_*` | portal.access / platform | **implementado não validado**; convite, acesso externo, arquivos e isolamento precisam de prova |
| AG-E10-005 / Suporte/CS | `Support`, `Crm` → `SupportController`, `CrmSaasController` → `SupportCustomerSuccessService`, `CrmSaasService` | support.*, customer-success.* / platform, commercial | **implementado não validado**; chamado/SLA e assistência não equivalem a impersonação auditada |
| AG-E10-006 / Workflows/tarefas/alertas | `Work`, redirects em Web Program → `WorkManagementController`, notifications SaaS → `WorkManagementService`, `SaasService` → `workflow_*`, notificações | work.* / platform | **implementado não validado**; aprovação, prazos/jobs e decisão auditável |
| AG-E10-007 / Governança/LGPD | `Governance` → `DataGovernanceController`, security/settings SaaS → `DataGovernanceService`, `SaasService` | governance.*, lgpd.*, security.* / platform | **implementado não validado**; consentimento/retenção/export, revogação e acesso restrito |
| AG-E10-008 / Implantação | `Deployment` → `DeploymentController` → `DeploymentService`/contratos | deployment.* / platform | **implementado não validado**; diagnósticos existem, provisionamento/restore em ambiente-alvo pendentes |
| AG-E10-009 / Worker/outbox | `Agro360.Worker/Program.cs`, `OutboxWorker.cs` → `LoggingOutboxPublisher` → `platform_outbox_messages` | processo técnico/RLS | **parcial**: publisher registra log, não entrega a sistema externo. Job, concorrência e retry não foram executados |
| AG-E10-010 / Backup, exportações e operação | `scripts/`, `deploy/`, migrations e releases SQL | credenciais externas/operador autorizado | **implementado não validado**; restauração/upgrade/carga não certificados. Nenhuma infraestrutura de produção modificada |

### Histórico preservado (não usar como status atual)

| Capacidade | Web | API/Application | Dapper/banco | Segurança/auditoria | Teste | Estado |
|---|---|---|---|---|---|---|
| Propriedade/talhão | atalho informativo | `PropertiesController`/contratos | `PropertyService`, `geo.*` | policy, RLS, audit | domínio/estrutura DB | FOUNDATION |
| Safra/plantio/colheita | atalho informativo | `AgricultureController` | `AgricultureService`, `agriculture.*`, estoque/custo/outbox | policy, RLS, audit | domínio | FOUNDATION |
| Estoque | KPI/atalho | `InventoryController` | `InventoryService`, `inventory.*` | policy, RLS, audit | domínio/constraint | FOUNDATION |
| Animal/pesagem/sanidade | KPI/atalho | `LivestockController` | `LivestockService`, `livestock.*`, custo/estoque | policy, RLS, audit | regras de GMD/carência | FOUNDATION |
| Venda/recebível | atalho informativo | `CommercialController` | `CommercialService`, `commercial.sales`, `finance.receivables` | policy, RLS, audit | regras de venda | FOUNDATION |
| Outbox | não aplicável | writer transacional/Worker | `platform.outbox_messages` | RLS, correlação, log sem payload | migration/integridade | FOUNDATION |
| Command Center | integrado | Discovery/dashboard | views e consultas Dapper | JWT/policy/RLS | arquitetura | CORE parcial |
| Alimentação/dieta | não existe | não existe | apenas fundações futuras | não aplicável | não existe | PLANNED |

`FOUNDATION` é deliberado enquanto não houver UI operacional conectada e E2E correspondente.

## Checkpoint de execução — 2026-09-08

Esta seção registra maturidade observada em execução e prevalece sobre declarações históricas de sprint quando houver conflito. Detalhes e comandos estão em `docs/EXECUTION-CHECKPOINT.md`.

| Capacidade/gate | Código disponível | Evidência de execução | Estado observado |
|---|---|---|---|
| Restore e build .NET 10 | solução e projetos `net10.0` | restore aprovado; build Release com 0 avisos/0 erros | FUNCIONANDO |
| Testes automatizados | 3 projetos xUnit v3; 114 casos descobertos | 110 aprovados, 4 ignorados por falta de banco, 0 falhas | CORRIGIDO / PARCIAL |
| PostgreSQL/PostGIS | instalador, migrator, health check e testes reais | readiness 503; conexão sem credencial disponível | QUEBRADO NO AMBIENTE |
| Swagger/OpenAPI | middleware e controllers | HTML/JSON 200; 657 operações publicadas | FUNCIONANDO SEM BANCO |
| Shell Web | Razor layout, CSS/JS e login modal | `/` 200 e modal presente | PARCIAL |
| Autenticação e refresh | controller, serviço e cliente Web conectados | login 500 com readiness 503 | NÃO HOMOLOGADO |
| Isolamento e seed | RLS, Dapper tenant-safe e testes PostgreSQL | 4 testes de integração ignorados | NÃO HOMOLOGADO |
| Fluxos verticais | camadas declaradas nas linhas anteriores | sem E2E autenticado neste checkpoint | FOUNDATION |
| Próxima entrega do plano mestre | não aplicável | anexo ausente na sessão/repositório | BLOQUEADO |

## Atualização Pecuária Integrada — 2026-09-10

| Jornada pecuária | Estado | Evidência presente | Gate pendente |
|---|---|---|---|
| Modelo animal/grupo/localização/insumo | **implementado sem homologação** | migration 068 separa entidades, modo de controle e conciliação | PostgreSQL limpo e incremental, concorrência |
| Cadastro e histórico individual | **implementado sem homologação** | contrato enriquecido, validação tenant/filiação e detalhe com timeline | API/Swagger/navegador e troca auditada de identificador |
| Pesagem individual e sanidade | **parcial** | idempotência, data de nascimento, estoque/custo/transação | correção auditada, limites configuráveis e restrições por finalidade |
| Movimentações | **parcial** | transferência individual anterior e ledger coletivo na migration | serviços de grupo, retroatividade, ajuste/estorno E2E |
| Manejos, alimentação, comercial e custos | **parcial** | eventos simples e integrações anteriores | ordens completas, devoluções/perdas, reserva/expedição e rateio |
| Dashboard, relatórios e UX | **parcial** | dashboard consolidado anterior | data de referência histórica, drill-down completo, CSV e telas operacionais |
