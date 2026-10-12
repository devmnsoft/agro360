# Matriz de rastreabilidade v0.2.0

## Incremento 2026-10-11 — Jornada agrícola integrada (Bloco A), M6/M8, i18n das 5 telas agrícolas e regressão Compras

| Dependência/capacidade | Estado real | Evidência/limite |
|---|---|---|
| Bloco A 3.5 — cancelamento de pedido: validação comportamental E2E (A5) | **Validado em runtime (E2E)** | `scripts/verify-procurement-journey-e2e.ps1` ETAPA 12: sem motivo 422; versão stale 409; concorrência com mesma versão = exatamente 1×204 + 1×409; replay pós-sucesso 409; `CANCELLED` c/ motivo persistido + evento auditado + versão+1; pedido `PARTIALLY_RECEIVED` cancelado preserva recibos=2 / movimentos recebidos=95 / devolução=10 / disponível=85 (sem estorno automático); recebimento pós-cancelamento 422 `agro360.procurement_order_not_receivable`; requisição reaberta `APPROVED`. Execução 2026-10-11 12:13:32 UTC, 12/12 etapas PASS, `artifacts/procurement-journey-e2e-bc06fb363bde45678c2fb5cf4f30dc02`. Correção de filtro do próprio E2E: ledger normaliza `p_type` `PURCHASE_RECEIPT→RECEIPT` com bruto em `reference_type` (migration 106). |
| Jornada de Compras completa (regressão do incremento anterior) | **Validada em runtime (E2E)** | Mesma execução 12/12 (etapas 1–11 idênticas às do 2026-10-10: SoD, idempotência/concorrência, quarentena→qualidade→estoque só do aceito, conferência documental c/ exceção e SoD, previsão OPEN, isolamento de tenant, delegação de escopo). Zero novas migrations (schema `11.24.0`). |
| A1 — consumo canônico de material (deposição explícita, 1 efeito por comando, replay/conflicto) | **Implementado; verificado por build + testes estáticos** | `Agriculture360Service.ApplyMaterialEffectAsync`: exige `warehouseId`, chama `inventory_apply_stock_movement('EXIT')`, chave `agri:{recordId:N}` + fingerprint (mesma chave mesmo conteúdo = replay equivalente; diferente = 409 `agriculture.material_effect_conflict`), efeito único compartilhado create→transição, sem re-baixa em edição. Cobre infraestrutura compartilhada validada pelo E2E (função canônica, unique parcial de idempotência); jornada comportamental própria das telas agrícolas segue na homologação visual desta rodada (hosts v8 no ar). |
| A2 — máquinas de estados create/update/transition + códigos estáveis | **Implementado; verificado por build + testes estáticos** | CreateAsync bloqueia terminais (`agriculture.create_state_invalid`); UpdateAsync não altera estado; transições via matriz mínima com terminais irreversíveis (`agriculture.transition_conflict`); version guard uniforme; código de erro derivado de label PT mantido como alias onde consumido (flag L documentada). |
| A3 — escopo de fazenda em toda leitura/escrita + FieldIds do plano | **Implementado; verificado por build + testes estáticos** | `OperationalScopePolicy.FarmIdSql` aplicado em listas/lookups/referências/dashboards; `ValidateReferences` cruza **todos** os `FieldIds` do plano com a propriedade/fazenda; lookups com dimensão de fazenda narrowing por `@FarmScope` (properties permanece lista autorizada do usuário). |
| A4 — dashboard planejado × realizado × desconhecido/não-calculável (null ≠ 0) | **Implementado; verificado por build + testes estáticos** | `AgricultureDashboard` remodelado: área plantada real vs planejada separadas; custo realizado somente de fonte reconhecida; `ActualYield` de colheitas reais (null quando nenhuma); denominador de área efetiva; tudo respeitando escopo de fazenda. |
| A6 — colheita: teto multi-pass, líquido>0, reconciliação peso×quantidade, lotes canônicos | **Implementado; verificado por build + testes estáticos** | Teto cumulativo de área = área do talhão (`harvest.area_exceeds_field`); parciais limitadas ao saldo do registro (`harvest.receipt_exceeds_balance`); tara só em unidade de massa, bruto>tara obrigatório (`harvest.invalid_weight`/`harvest.tare_incompatible`); divergência líquido×quantidade exige motivo persistido (`harvest.net_weight_mismatch`); `AllocateAsync` passa a criar lotes via função canônica (`p_type='PRODUCTION'`); replay de recebimento ainda `AWAITING_INSPECTION` re-dispara inspeção (idempotente por origem+hash — `HarvestService.ReceiveAsync`). |
| M6 — geração de ordens idempotente + trava de dependência | **Corrigido e compilado** | `SeasonTrackingService.GenerateOrderAsync`: advisory lock + consulta de replay antes do insert; mesma chave idempotência com outra operação/área = 409 `agriculture.idempotency_conflict`; replay equivalente retorna o pedido existente. (Tupla de replay corrigida para elementos nullable pós-Dapper default.) |
| M8 — custo com origem estável (sem dupla apropriação) | **Corrigido e compilado** | `FieldOperationsService`/`PostingService`: `source_key` estável por evento em `cost_management_entries` (uq `(tenant,source_type,source_key)`); compra↔consumo apropria por fontes distintas; estorno referencia apropriação original. |
| Flag K — status numérico de safra com 3 leituras divergentes | **Resolvido com mapeador único** | `SeasonStatusCodes` (Infrastructure) derivado exclusivamente do enum `SeasonStatus` (1 PLANNED/2 ACTIVE/3 HARVESTED/4 CLOSED/5 CANCELLED = CHECK da coluna); `AgricultureService` e `SeasonTrackingService` consomem o mesmo CASE; teste `SeasonStatusHasSingleCanonicalMapper` proíbe novo CASE duplicado em `src` (187/187 architecture). |
| i18n das 5 telas agrícolas (pt-BR/en-US/es-ES/fr-FR) | **Validado em nível de dicionário/estático** | `agro-i18n.js`: 809 chaves × 4 idiomas, paridade total (gate reexecutado a cada edição); 5 JS transformados (`node --check`); marcadores `data-i18n-ag*` nas 5 telas; headers `X-Culture`/`X-Farm-Id` via `agroI18n.headers()` inclusive nos fetch próprios de `work.js`/`field.js`; moeda sempre BRL; códigos de valor do contrato permanecem brutos. Ver `docs/I18N-COVERAGE-REPORT.md`. |
| Gates estáticos do incremento | **Verdes** | Build Release da solução completa 0 erros/0 avisos (inclui M6/M8 + mapeador K); unit 390/390 e architecture 187/187 (execução direta da dll — wrapper `dotnet test` com quirk de ambiente, ver `docs/LIMITES.md` item 14); `verify-offline-shell.mjs` PASS; `check-api-routes.py` 946 rotas únicas; smoke server-side: 5 telas 200 + dicionário servido (196.730 bytes, guard `module.exports={AGRO_I18N}`). |
| Homologação visual autenticada (Compras + 5 telas agrícolas; 4 idiomas × 360/768/1440 + zoom 200%) | **Bloqueada por certificado** | Túnel do navegador desktop rejeita o self-signed (`ERR_CERT_COMMON_NAME_INVALID`) até a confiança única do certificado no desktop (runbook: confiar uma vez, nunca contornar). Certificado validado como correto (SAN localhost+192.168.0.10, thumbprint `C05DA1CAA96A04FDF9EB850D2281AB150531747F`, expira 2028; handshake OK via curl/schannel local). Hosts v8 prontos (`https://192.168.0.10:7080`=200, API health 200); não declarado homologação visual sem a matriz de screenshots em `artifacts/browser-homologacao-<data>/`. |
| Migrações | **135 + 136 aplicadas** | `135_inspection_intent_recovery.sql` → schema **11.25.0** (recuperação de inspeções; aplicada em `agro360_homolog` + templates) e `136_quality_bridge_receipt_item_material_link.sql` (coluna `procurement_receipt_items.work_order_material_id` nullable, sem FK); `full.sql` carrega ambas. |
| Jornada agrícola completa (Bloco A) — validação comportamental em **runtime** | **Validado em runtime (E2E) 59/59** | `scripts/verify-agro-journey-e2e.ps1` (cluster PG18 descartável + API Release + PostgreSQL próprios): **A** ledger canônico (1 efeito/consumo, replay sem re-decremento, conflito por chave, estorno válido→estoque devolvido, estorno duplicado 409); **B** máquina de estados (version guard + terminais irreversíveis); **C** escopo/tenant (2 tenants × 2 fazendas, 403 fora de escopo); **D** planejamento (grafo de dependências, liberação fora de sequência, reschedule sem propagação, geração concorrente→ordem única); **E** colheita→lote disponível (teto multi-pass, bruto/tara c/ justificativa, inspeção, destinação PRODUCTION); **F** resultado gerencial (null≠zero, custo da fonte reconhecida, custo/ha null s/ área, escopo por fazenda). **59/59 PASS** — `artifacts/agro-journey-e2e-ae861b9fb6f74d609fc16eaf0e43d776`. Promove **A1/A3/A4/A6/M6/M8** de "verificado por build+estáticos" a **validado em runtime**. |
| §3 — Recuperação automática de inspeções por evento operacional | **APROVADO (worker limpo)** | Migration 135 + `InspectionIntentProcessor`/`InspectionIntentRecoveryWorker` (claim/lease, backoff, dead-letter, adoção de origens órfãs **sem reenviar** o comando) + re-disparo auditado (`compliance.approve`). E2E `scripts/verify-inspection-recovery-e2e.ps1` S1–S7 todos passam. Evidência limpa: `artifacts/inspection-recovery-e2e-afc2e394c0f94a1faad4491221edcf7d`. |
| Defeitos de produto corrigidos na causa raiz durante o Bloco A | **Corrigidos + comprovados pelo E2E verde** | Sweep row-DTO (9 classes `{ get; init; }` + alias), Bug 1 coluna `work_order_material_id` (mig 136), Bug 2 join `inventory_products.base_unit` no fechamento, Bug 3 separador de escopo (`OperationalScopePolicy`), Bug 4 estorno via `reversed_amount` (evita `recognized_amount<0`). Detalhes em §12 da matriz `AGRO-JOURNEY-MATRIX-2026-10-11`. |
| Regressão Compras (preservação após mudanças de código compartilhado) | **Validada em runtime (E2E) 12/12** | `scripts/verify-procurement-journey-e2e.ps1` reexecutado após as correções de escopo/row-DTO/coluna nova: 12/12 etapas PASS, schema 11.24.0. `artifacts/procurement-journey-e2e-2381aa5ed86b450c97d7fc1c44372864`. |

## Incremento 2026-10-10 — Jornada completa de Compras, i18n de telas operacionais e defeitos de estabilidade

| Dependência/capacidade | Estado real | Evidência/limite |
|---|---|---|
| Jornada completa de Compras (requisição → aprovação → cotação → comparação → decisão → pedido → aprovação → recebimento parcial → qualidade → estoque → previsão financeira → conferência documental → devolução) | **Validada em runtime (E2E, cluster descartável)** | `scripts/verify-procurement-journey-e2e.ps1`: 11/11 etapas PASS, schema `11.24.0`; execução 2026-10-10 09:46:12 UTC, log/resumo em `artifacts/procurement-journey-e2e-4e570923d65a468fb12bb97f1ec0414a` (banco `agro360_procjourney_cc1f7caee07b`). SoD, divergências de conferência (MATCHED), previsão por pedido, delegação de escopo e isolamento entre tenants incluídos na mesma execução. |
| Critério "menor proposta" (regra preservada, significado esclarecido) | **Validado** | Menor **total da mercadoria por item** (`round(qty×unit,2)` − desconto, 2 casas, MidpointRounding.AwayFromZero); frete/impostos informativos; justificativa ≥3 caracteres quando acima do menor; comparação exibe os dois valores lado a lado em 4 idiomas (`procurement.js`). Alternativa "menor custo completo" registrada como mudança proposta, **não aplicada** como política. Zero ≠ valor ausente distinto na comparação. |
| Versionamento de propostas + contrato 409 de confirmação | **Validado** | Ressubmissão marca versões anteriores `available=false`; compare/decide/converte leem apenas disponíveis; decisão fotografa `selected_total`/`lowest_total`; conversão automática de itens sem decisão apenas com `confirmLowestPrice` explícito, senão 409 `agro360.quotation.confirmation_required` (contrato idêntico no front, `procurement.js`). |
| Devolução ao fornecedor (migration 133) integrada à jornada | **Auditada e validada no fluxo** | Sem duplicação de feature; proteção de disponibilidade em nível de ledger (`inventory_stock_movements.quantity` sempre > 0; direção em `movement_type=RETURN_TO_SUPPLIER`); idempotência de custo por linha de recibo (`source_key = id do item do recibo`, UNIQUE(tenant_id,source_type,source_key)); crédito reconhecido após aprovação da devolução (verificado no E2E: 25.00 OPEN). |
| SoD e tolerâncias/divergências de conferência documental | **Validado** | `tolerance_snapshot->>'SeparationOfDuties'` (PascalCase por serialização padrão) && `CreatedBy==ator` → 422; decisão ∈ {APPROVED_EXCEPTION, REJECTED, CORRECTION_REQUESTED} com justificativa obrigatória, senão 422 `procurement.match.decision_invalid`; divergência fechada → 409 `procurement.match.divergence_closed`; última OPEN não-REJEITADA + decisão → MATCHED; REJECTED → REJECTED. |
| Previsão financeira de pedido de compra | **Validado** | Criada **uma vez por pedido** (guard por `procurement_order_financial_links.forecast_exists`), dividindo `order.Total` pelas parcelas; não acumula por recebimento (E2E: exatamente 1 título 250.00 OPEN com `source_id=orderId`). |
| Isolamento de fazenda + delegação de escopo/permissões | **Validado** | Escopo `FARM-B` → 403 direto por API com IDs manipulados; ALL só delegável por admin ou quem possui ALL; mesmo escopo → 204; admin delega ALL. Tenant `valeverde` isolado (dashboard de Santa intocado; módulo SUSPENDED → 403 enquanto inventário responde 200; semcompras → 403). |
| Defeitos de estabilidade corrigidos neste incremento (código já commitado) | **Corrigidos e comprovados pelo E2E verde** | (1) CTE `WorkManagementService` `d.created_at`→`d.recorded_at`; (2) `OperationOccurrenceRow` DateTimeOffset→DateTime (Dapper 2.0.0 exige tipo CLR exato para construtor posicional; timestamptz→DateTime); (3) `QuotationService.DetailAsync` `c.code`→`c.internal_code as catalog_code`; (4) `DecideAsync` multi-row `ExecuteAsync(IEnumerable<object>)` liga somente propriedades presentes — objeto por linha com `tenant.UserId` para `@UserId`×3; (5) `ConvertPlanAsync` `confirmation_required` 409 (era 422) + catch de auditoria por código; (6) `ReceiveAsync` lançava custo com `CostCenterId ?? Guid.Empty` como farm_id FK → 409; agora apropria pelo `order.PropertyId`, itens SERVICE/sem propriedade pulam; (7) `DecideQualityAsync` mapeia decisão→`receipt_items.quality_status` varchar(20): todos rejeitados → REJECTED, misto → PARTIALLY_REJECTED; resultado exato em `procurement_receipt_quality_decisions.result`; (8) idempotência de reconhecimento de custo por linha de recibo (`source_key=id do item`). Regras Dapper comprovadas empiricamente (POCO tolerante; construtor posicional estrito; binding por propriedades presentes). |
| i18n das telas operacionais de Compras (pt-BR/en-US/es-ES/fr-FR) | **Validado em nível de dicionário/estático** | `procurement.js` `PROC_I18N`: 511 chaves × 4 idiomas com paridade (gate estático verde); strings parametrizadas por `tr()` com `{0}`; markup `data-i18n*`; `procurement.css` responsivo. Homologação visual autenticada pendente (ambiente degradado; hosts em subida lenta) — ver checkpoint. |
| Homologação visual autenticada (360/768/1440 px + zoom nativo 200%, 4 idiomas) | **Bloqueada por ambiente (em andamento)** | Ferramenta de navegador real disponível nesta sessão; máquina em estado degradado pós-restart (todos os processos .NET Core nascidos desde ~11:00 com CPU ≈ 0 por minutos; `pwsh -NoProfile -Command echo` levou 20 min; RAM física livre ~540 MB de 16 GB; Docker Desktop/CLI lento; canal foreground do shell intermitente). Dev DB migrada de `9.8.0`→`11.24.0` (backup prévio). Hosts relançados em jobs de background com logs garantidos; não declarado homologação visual sem screenshots da matriz. |

## Incremento 2026-10-09 — francês (fr-FR), i18n do shell e isolamento de fazenda em Compras (mesmo tenant)

| Dependência/capacidade | Estado real | Evidência/limite |
|---|---|---|
| Idioma fr-FR via catálogo canônico | **Validado em runtime** | Migration `134_french_language_support.sql` (schema `11.24.0`, aditiva, idempotente; espelhada no consolidado). Bloco L do E2E: catálogo `platform_languages` com 4 ativas, sem CHECK de cultura antigo nas 5 tabelas, insert/delete fr-FR ao vivo em `ui_message_templates`, login default pt-BR, PUT/GET de preferência com confirmação do servidor, re-login preservando fr-FR, `de-DE` → fallback `pt-BR` persistido, estado operacional invariante. Log `artifacts/e2e-rerun6.log`; resumo `artifacts/language-farm-isolation-e2e-52a7016f103243f08bb4433a6f4ff502/SUMMARY.txt`. |
| i18n do shell (pt-BR/en-US/es-ES/fr-FR) | **Validado em nível de dicionário/estático** | `uiTranslations` com 103 chaves por idioma e paridade total; 97/97 chaves usadas no `_Layout.cshtml` traduzidas (`node artifacts/check-i18n-keys.cjs`); seletor em nomes nativos; persistência somente após 200 do servidor com retry. Limitações documentadas em `docs/I18N-COVERAGE-REPORT.md` (toasts, `data-help`, cópias longas de ajuda, títulos Razor e chaves `helpTitle`/`help` do Ecosystem permanecem pt-BR). Navegador real não executado nesta rodada. |
| Isolamento de fazenda em Compras (mesmo tenant) | **Validado em runtime (E2E)** | Bloco F: operador com escopo `FARM` só na SC-SEDE-001 — LIST sem B (por `property_id` e número), DETAIL A=200/B=404/admin=200, escrita com `propertyId=B` no corpo = 403 sem efeito colateral (0 linhas), `X-Farm-ID=B` divergente = 403 `forbidden_unit_scope`, aprovação de pedido B por ID negada mantendo `AWAITING_APPROVAL`, recebimento de pedido B negado (422) com zero recibos, CSV do operador só com A (implícito e explícito), dashboard operador 1/0 vs admin 2/1. 58/58 PASS. |
| Invariantes de idioma (BRL/tributação/UoM/permissões/estado) | **Validado** | Fábricas `Intl` sempre `currency:"BRL"`; E2E confirmou lista de fazendas idêntica antes/depois da troca de cultura; recusas canônicas e códigos estáveis preservados. |
| Matriz plano→módulo→funcionalidade→permissão→menu→tela→endpoint→persistência→aceitação | **Entregue** | `docs/FEATURE-MATRIX-2026-10-09.md`. |
| Homologação visual autenticada (360/768/1440 px, zoom 200%) | **Não executado** | Sem ferramenta de navegador real nesta sessão; não declarado como homologação visual. |

## Incremento 2026-10-08 — Compras com escopo operacional e evidência descartável

| Dependência/capacidade | Estado real | Evidência/limite |
|---|---|---|
| Segurança de evidências de homologação | **Validado em runtime** | `scripts/verify-remaining-homologation.ps1` cria cluster PostgreSQL descartável próprio em porta dinâmica e bancos únicos; E2E PASS em `artifacts/remaining-homologation-e2e-b5b48586661a436c8fee660711ab4b87/SUMMARY.txt`. |
| Escopo operacional em Compras | **Implementado e parcialmente validado** | `ProcurementService` aplica escopo por `identity_user_unit_scopes` em requisições, pedidos, recebimentos, pendências e indicadores; cadastros compartilhados (fornecedores/catálogo) permanecem por tenant. Build/testes passaram; falta cenário E2E específico fazenda A vs B nos endpoints de Compras. |
| Indicadores de Compras | **Implementado e validado estaticamente/build** | Dashboard separa `DRAFT`, `AWAITING_APPROVAL`, aprovadas com saldo e urgentes elegíveis; cotações contam estados existentes. Build/testes/rotas passaram; falta validação visual com dados reais. |
| Cotações de Compras | **Parcial** | `QuotationService` grava cotações/propostas em tabelas canônicas e converte para pedido idempotentemente quando há vencedor único. UI completa de cotações, decisão por justificativa e múltiplos fornecedores continuam pendentes. |
| UI de Compras | **Parcial** | `/Procurement` remove referência interna de sprint, inclui unidade operacional nos formulários e preserva chave idempotente em reenvios. Sem homologação autenticada mobile/zoom/tema nesta rodada. |

## Incremento 2026-09-30 — MVP Operacional: Pedido, Programação e Atendimento Rastreável (AG-OPS-MVP-001)

| Dependência/capacidade | Estado real | Evidência/limite |
|---|---|---|
| Programação de Entregas e Compromissos Operacionais (AG-OPS-MVP-001) | **Validado em runtime e testes** | Migration 116 (schema 11.6.0) em `sales_delivery_schedules`, `sales_delivery_schedule_items`, `sales_delivery_schedule_revisions` com RLS multi-tenant; regras `CommercialRules` (`EnsureOrderCanBeScheduled`, `CalculateEligibleScheduleBalance`, `ValidateScheduleTransition`, `ValidateDeliverySchedule`, `ValidateReschedule`); controle OCC via `ExpectedVersion` em reprogramação e cancelamento; endpoints de CRUD/gestão em `Commercial360Controller`; vínculo direto com `fulfillment_shipments` (`schedule_id`, `schedule_item_id`) sincronizando status `PREPARING` → `DISPATCHED` → `PARTIALLY_DELIVERED`/`DELIVERED`; ocorrências automáticas na Central de Operações (`SCHEDULE_UNASSIGNED`, `SCHEDULE_LATE`, `DELIVERY_FAILED`); diagnóstico do piloto no Deployment Center; modais operacionais em `/Commercial` e aba "Compromissos" com indicadores em `/Logistics`; round-trip simétrico sem mutações acidentais; 169 arch tests PASS, 260 unit tests PASS (100%), build Release com 0 erros e 0 warnings. |

## Incremento 2026-09-28 — Estabilização de Suporte Assistido e Conclusão Comercial Proposta-Pedido (AG-SAAS-SUP-001 + AG-COM-PROP-001)

| Dependência/capacidade | Estado real | Evidência/limite |
|---|---|---|
| Suporte Assistido Estabilizado (AG-SAAS-SUP-001) | **Validado em runtime e testes** | Migration 114, RLS em `saas_support_sessions`, bloqueio pessimista, claims `support_session_id` e `support_scope`, checagem síncrona de revogação no DB em `PermissionAuthorization`, proibição de mutação em escopo somente-leitura (403), endpoint dedicado `/api/platform/support-session/end` para auto-encerramento, sincronização cross-tab via `storage`, tratamento diferenciado de revogação vs erro de rede, `verify-e0.ps1` PASS, 169 arch tests, 202 unit tests (incluindo `SaasSupportSessionRulesTests`). |
| Propostas Comerciais a Pedidos de Venda (AG-COM-PROP-001) | **Validado em runtime e testes** | Migrations 107/108, concorrência otimista (OCC via `ExpectedVersion` com HTTP 409 em conflito), elegibilidade estrita de compra (`CommercialRules.CustomerCanOrder` bloqueando prospects e inativos), conversão parcial/total com saldo de itens (`ProposalRemainingBalance`), número legível `PED-XXXX`, lookups limpos sem GUIDs técnicos, UI `/Commercial` com modais completos, breadcrumbs, guide contextual e badges de estado em português, race condition mitigation via `activeRequestId`, 169 arch tests, 202 unit tests (incluindo `CommercialProposalRulesTests`). |

## Incremento 7.7 — fechamento gerencial da safra (2026-09-14)

| Dependência/capacidade | Estado real | Evidência/limite |
|---|---|---|
| Colheita, recebimento, qualidade e destinação | Implementado sem verificação de execução | migrations 075/076 e `HarvestService`; runtime indisponível |
| Beneficiamento | Implementado sem verificação de execução | reserva/consumo/resultados 076; E2E pendente |
| Estoque e reservas | Parcial | movimentos reais reutilizados; genealogia de saldo atual por safra incompleta |
| Pedidos, expedições, entregas e devoluções | Parcial | módulos existentes preservados; atribuição exata à safra não presumida |
| Custos e recebíveis | Parcial | custo diretamente apropriado consolidado; receita sem política/vínculo fica indisponível |
| Central de Operações | Concluída e verificada estaticamente no recorte anterior | projeção 073/074 preservada; cinco novos tipos do fechamento ainda pendentes |
| Conferência e fechamento 7.7 | Implementado sem verificação de execução | migration 077, API e `/Harvest`; JS/SQL estáticos aprovados, .NET/PostgreSQL/E2E pendentes |


## Incremento E9 móvel controlado — evidência de 2026-09-11

- **Implementado, não homologado em runtime:** sessão curta e dispositivo revalidado; catálogo/lote/contrato explícitos; hash, replay e conflito por conteúdo; fila e rascunhos locais separados por contexto; estados locais sem falso “concluído”; cache v45 limitado ao shell.
- **Executado:** validação JS, regressão do service worker, validador do SQL consolidado e whitespace.
- **Pendente:** PostgreSQL/API/navegador autenticado, administração visual, comparação assistida de conflitos e operações especializadas de medidor, pesagem e tentativa de entrega.

## Incremento 7.1 — expedição e entrega (2026-09-10)

| Requisito | Evidência implementada | Estado |
|---|---|---|
| Pedido apto → reserva → separação/conferência | `071_fulfillment_delivery_journey.sql`; `CreateFulfillmentAsync`; fila filtrável `/Logistics` | Implementado, não homologado em PostgreSQL |
| Saída física única e concorrente | trava advisory por tenant/lote, transação, movimento `FULFILLMENT_SHIPMENT`, versão e idempotência | Implementado, E2E pendente |
| Parcial/recusa/nova tentativa | tentativas e itens imutáveis; limite pelo saldo conferido ainda não reconciliado | Implementado, E2E pendente |
| Retorno indisponível até recebimento/qualidade | retorno inicial `AWAITING_RECEIPT`, estados separados até `RELEASED` | Modelo implementado; endpoints de recebimento/qualidade pendentes |
| Rastreabilidade/indicadores | detalhe agregado e oito indicadores; tela clara e responsiva | Implementado, navegador pendente |
| Acesso Santa Clara/SuperAdmin | mecanismo existente preservado, sem credenciais em seed | Pendente: ambiente sem banco/.NET |
| Financeiro/fiscal/frete/viagem | conceitos preservados sem duplicar recebível nem simular autorização | Parcial/pendente |


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

**Atualização pecuária integrada (2026-09-10):** AG-E6-002 passou de fundação com KPI para **implementado não validado** com página `/livestock`, `ILivestockHerdService`, migration 068 e seed Santa Clara. Restore/build Release e 122 testes existentes aprovados nesta máquina; 4 testes PostgreSQL continuam ignorados sem connection string. Jornadas no navegador e cluster descartável não foram declaradas homologadas.

**Atualização frota/manutenção (2026-09-10):** AG-E8-003 passou a **implementado não validado** com página `/Fleet`, `IFleetOperationsService`, migration 069, seed Santa Clara e instalador consolidado corrigido (`platform_enable_tenant_rls` preservada). Restore/build Release e 124 testes existentes aprovados; instalação limpa SQL em cluster descartável **PASS**. Navegador, login real e concorrência de reserva **não** homologados.

**Atualização merge + exclusão lógica (2026-09-10):** marcadores commitados reconciliados em pecuária/Migrator/SQL/docs; migration `070` (`7.0.0`) padroniza soft-delete/auditoria em `fleet_%`/`livestock_%`; UI de archive/restore e autoria nas listagens/detalhes. Build/testes 127/4; SQL limpo PASS com `7.0.0`. E2E autenticado e soft-delete fora desses módulos **não** homologados.

**Atualização pós-PR #97 (2026-09-10):** AG-E1-004 permanece **implementado não validado**, agora com contrato explícito de Data Protection, validação de identidade/tenant/ID e provisionador PowerShell. AG-E5-001 permanece **parcial / implementado não validado**: fingerprint de idempotência, bloqueio de unidade incompatível, parcelas positivas e autorização específica de excesso estão na migration 067 e no serviço/controller. Quarentena, liberação única, estado agregado completo, reversão e homologação PostgreSQL continuam pendentes.

### Fatia verificada e lacunas prioritárias

| ID | Jornada / atores | Cadeia e efeitos | Estado e evidência |
|---|---|---|---|
| AG-E0-001 | Operação: instalação → prontidão → login → consulta → refresh/logout | `DatabaseHealthCheck` → metadados PostgreSQL; `Index.cshtml`/`agro360.js` → `POST /api/v1/auth/login` → `LoginCommand` → `IdentityService` → `tenancy_tenants`, `identity_users`, roles/permissions/entitlements; hash real, refresh persistido/revogado. `GET /api/v1/dashboard/command-center` → `CommandCenterResult` → `DashboardService` → fazendas, safras, animais, estoque, recebíveis/custos/alertas → `dashboard.read` e módulos reports/analytics | **validado no recorte HTTP/SQL**: banco vazio 503, instalado 200, login/dashboard 200, anônimo 401, refresh 200/replay 401, logout 204/revogado 401. Instalador executado e repetido. Não valida toda E1 |
| AG-E0-002 | Usuário: testar conexão na tela de acesso | `agro360.js::testApiConnection` → `/health`, Swagger; `service-worker.js` controla só shell público; sem escrita de negócio | **validado**: navegador antes mostrava sucesso com API inacessível por fallback HTML. Depois mostra falha e URL correta; health exige `Healthy`, Swagger exige JSON OpenAPI. Cache de bootstrap sem tenant removido |
| AG-E0-003 | Operação: atualização de versão com dados | `Agro360.Migrator/Program.cs` → `platform_schema_migrations`; `001_foundation.sql` cria schemas legados, instalador cria `agro360` | **com defeito**: `verify-e0.ps1 -CheckMigrations` falha na 007 com 42703, `due_on` inexistente; 001 define `due_date`. Upgrade com dados anteriores ainda não homologado. Não alterar checksums nem rodar em produção |
| AG-E0-004 | Integração do worktree | contratos/propriedades/SaaS/SQL e testes recebendo alterações concorrentes | **parcial**: última reexecução completa em `e0-8072988ddd92424aaa3eb21ec817af3f` passou restore/build, HTTP/SQL e 118 testes, zero ignorados. Integração de autoria/commit permanece pendente. Histórico e fronteira de autoria no checkpoint |
| AG-E1-001 | Pessoa → vínculo → contexto | Login exige tenant antes da pessoa; `identity_users` contém `tenant_id`; normalizador aceita 11/14 dígitos e consulta documento do usuário | **parcial**: login real existe; identidade global/vínculos/seleção pós-login e CNPJ organizacional não estão entregues. Não confundir identidade com cliente |
| AG-E1-002 | SuperAdmin → MFA → suporte assistido | `SaasControllers` restringe plataforma por role; `saas_support_sessions` armazena sessão com claims no JWT e checagem síncrona no DB | **validado no recorte de suporte assistido auditado e persistente (AG-SAAS-SUP-001)**: sessão persistida com RLS (114), claims `support_session_id` e `support_scope` no JWT, validação síncrona no DB em `PermissionAuthorization`, proibição de mutação em escopo somente leitura, revogação imediata, auto-encerramento e sincronização cross-tab via `storage`. Desafio/verificação de MFA independente permanece para próximo ciclo |
| AG-E1-003 | Admin cliente → usuários/perfis/convites | `/Saas`, `saas.js` → `SaasControllers`, `SaasContracts`, `SaasService` → `identity_*`, `saas_*`; permissões de usuários em evolução concorrente | **parcial**: revisar guards, revogação de tokens e último administrador. Não homologado nesta rodada; não duplicar entrega concorrente |
| AG-E1-004 | Operação → demo/provisionamento | instalador inclui Santa Clara e bootstrap fixo; `PasswordHasher` PBKDF2 real | **com defeito frente ao mestre**: demo não é opt-in e há credenciais universais na documentação histórica. Gate E0 usa senha aleatória só no banco descartável; isso não corrige o seed de produto |
| AG-E2-001 | Cliente/MNSOFT → catálogo/contrato | `SaasService`, `EcosystemService`, `CrmSaasService`; login combina `saas_plans`, `platform_module_catalog`/entitlements e `platform_marketplace_modules`/tenant_modules | **parcial**: fontes comerciais concorrentes; snapshot/preço/aceite/dependências e expiração de todos os caminhos precisam ser demonstrados |
| AG-E3-001 | Cobrança SaaS e split | `SaasControllers` billing/`SaasService`; `Sprint10Service::ControlledPaymentSplitProvider` retorna `SIM-...`, registrado em DI | **com defeito no split externo**: simulação não comprova pagamento. Cobrança interna existente **implementada não validada**; provedor real e reconciliação permanecem pendentes |

**Atualização Compras (2026-10-10):** AG-E5-001 avança de **parcial** para **validado em nível de API/E2E** quanto à jornada contratada: requisição → aprovação → cotação → comparação → decisão (com contrato 409 de confirmação) → pedido → aprovação → recebimento parcial → qualidade → estoque → previsão financeira → conferência documental → devolução ao fornecedor, com SoD, tolerâncias, isolamento de fazenda e devolução auditados na mesma execução (`artifacts/procurement-journey-e2e-4e570923d65a468fb12bb97f1ec0414a`). Nove defeitos de código pré-existente corrigidos e comprovados (lista no incremento 2026-10-10 desta matriz). Homologação **visual** autenticada da tela `/Procurement` permanece pendente por ambiente; cancelamento de pedido carrega um campo `version` inexistente no DDL (`ProcurementService.CancelOrderAsync`) — item de carry-over documentado, não está na jornada contratada deste incremento.

### Inventário operacional inicial

Convenções de caminho: controllers em `src/Hosts/Agro360.Api/Controllers`; contratos em `src/Modules/Agro360.Application/Contracts`; implementações em `src/Modules/Agro360.Infrastructure/Services`; páginas em `src/Hosts/Agro360.Web/Pages`. Tabelas atuais usam schema `agro360` e prefixos de módulo; os nomes pontuados da matriz histórica abaixo não são os nomes canônicos atuais. Policies constam nos controllers e em `Application/Permissions.cs`; o mapeamento comercial atual é `IdentityService.IsPermissionContracted` e deve ser substituído/consolidado em E2, não tratado como catálogo aprovado.

| ID / módulo | Página/ação → controller → contrato/serviço → dados | Permissão / contrato atual | Maturidade observada / próximo aceite |
|---|---|---|---|
| AG-E4-001 / Fazendas e unidades | `Properties/Index`, listar/criar/editar/arquivar → `PropertiesController` → `PropertyContracts`/`PropertyService` → `geo_farms`, `geo_fields`, `organization_organizations` | properties.* / properties | **parcial**, fluxo em edição concorrente; CRUD/área/conflito/FKs/tenant precisam de gate próprio |
| AG-E4-002 / Estoque | atalhos `/` → `InventoryController` → `InventoryContracts`/`InventoryService` → `inventory_products`, warehouses/balances/movements | inventory.* / inventory | **implementado não validado**; provar reservas/estornos/concorrência, não só KPI |
| AG-E4-003 / Seletores e busca | formulários/busca → `LookupsController`, `DiscoveryController` → contratos/`LookupService`, `GlobalSearchService`, `TraceabilityService` | policy da rota / módulo do dado | **implementado não validado**; busca/export não podem ampliar tenant/unidade |
| AG-E5-001 / Compras | `Procurement`, compras → `ProcurementController` e `OperationsController` → `ProcurementContracts`/`ProcurementService`, `OperationsService` → `procurement_*`, `purchasing_*` | purchasing.* / purchasing | **parcial**; commit local preservado integra recebimento a estoque/título com idempotência; dois cadastros de fornecedor e homologação PostgreSQL/rollback/concorrência ainda pendentes |
| AG-E5-002 / Comercial | `Commercial` → `CommercialController`, `Commercial360Controller`, `AgroSalesController` → contratos/`CommercialService`, `Commercial360Service`, `SalesService` → `commercial_*`, `sales_*`, recebíveis | commercial.* / commercial | **validado no fluxo Proposta → Pedido (AG-COM-PROP-001)**: propostas versionadas com OCC (`ExpectedVersion`/409 Conflict), elegibilidade de compra (`CommercialRules.CustomerCanOrder`), bloqueio de prospects, conversão parcial/total com saldo de itens (`ProposalRemainingBalance`), número legível `PED-XXXX`, UI `/Commercial` completa com modais sem alert(), `activeRequestId` e feedback vivo. Reserva física e expedição em logística tratadas nos recortes subsequentes |
| AG-E5-003 / CRM | `Crm` → `CrmSaasController` → `CrmSaasContracts`/`CrmSaasService` → `crm_*` | crm.*, commercial-saas.* / commercial | **implementado não validado**; não confundir jornada SaaS com venda rural |
| AG-E5-004 / Financeiro/controladoria | `Finance` → `FinanceController` → contratos/`FinanceService` → `finance_*`, `cost_*` | finance.* / finance | **implementado não validado**; parcelas/baixas parciais/estorno/DRE conciliados |
| AG-E6-001 / Agricultura | `Agriculture` → `AgricultureController`, `Agriculture360Controller` → contratos/`AgricultureService`, `Agriculture360Service` → `agriculture_*` | agriculture.* / agriculture | **implementado não validado**; planejamento/campo/insumo/colheita/custo ponta a ponta |
| AG-E6-002 / Pecuária e especialidades | `/livestock` + atalhos `/` → `LivestockController`, `Livestock360Controller` → `LivestockContracts`/`Livestock360Contracts`/`ILivestockHerdService` → `livestock_*`, `cost_entries`, `commercial_sales`, `finance_commercial_receivables`, estoque | livestock.read/write/sell | **implementado não validado**; cadastros, movimentações, manejo parcial, pesagem, restrição, alimentação, reserva e custos no código; sem E2E navegador/PostgreSQL desta sessão |
| AG-E7-001 / Agroindústria | `Production` → `IndustrialProductionController` → contratos/`IndustrialProductionService` → `production_*` | production.* / agroindustry | **parcial**; apontamento valida estado, linha, operador e critérios com etapa anulável; conclusão usa status efetivo do lote aprovado, não aprovação histórica. Pendente roteiro versionado, banco, idempotência, consumo/reserva e retificação |
| AG-E7-002 / Qualidade e conformidade | `Compliance`, `/` rastreabilidade → `ComplianceControllers`, `Sprint10Controllers` → contratos/`ComplianceService`, `Sprint10Service` | compliance.*, traceability.* / environment-esg, traceability | **implementado não validado**; evidência, reprovação e bloqueio nos consumidores |
| AG-E8-001 / Armazenagem | `/` storage → `StorageController` → contratos/`StorageService` → `storage_*` | storage.* / inventory, warehousing | **implementado não validado**; pesagens, qualidade, lote, capacidade e expedição |
| AG-E8-002 / Logística e contratos | `/`, `Maps` → `LogisticsController`, `DeliveryContractsController`, `Sprint10Controllers` → `LogisticsService`, `DeliveryContractService`, `Sprint10Service` | logistics.*, regional-logistics.* / logistics | **implementado não validado**; parcial/ocorrência/prova entrega e custos |
| AG-E8-003 / Frota/manutenção/SST | `/Fleet` + atalhos → `FleetController`, `SstController`, `OperationsController` → `FleetContracts`/`IFleetService`/`IFleetOperationsService`/`FleetService`/`FleetOperationsService` → `fleet_*` (medidores, bloqueios, reservas, OS, peças, abastecimentos, custos), SST | fleet.*, maintenance.*, sst.* / fleet, verticals/rural-hr | **implementado não validado** (frota operacional): cadastro unificado, leituras, preventiva, OS, disponibilidade, peças/estoque, abastecimento interno/externo, inspeção/liberação e custos no código; SQL limpo PASS; sem E2E navegador/API autenticada nesta sessão. SST permanece como antes |
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

## Evidência incremental — configuração guiada (2026-09-11)

| Requisito | Estado | Evidência / limite |
|---|---|---|
| Configuração retomável por cliente | Implementado sem homologação | `DeploymentService.ImplementationCenterAsync` deriva etapas de dados persistidos por tenant; `/Deployment` explica finalidade, impacto e impedimento. PostgreSQL/API autenticada não disponíveis neste ambiente. |
| Etapas conforme contratação | Implementado sem homologação | Depósitos, centros de custo e catálogos só são obrigatórios quando os módulos contratados os exigem. |
| Navegação contextual | Parcial | A aba canônica de `/Saas?view=` é preservada e a organização informada no login permanece no shell após refresh. Favoritos e histórico persistidos no servidor continuam pendentes. |
| Jornadas A e B | Implementadas sem homologação nesta rodada | Serviços transacionais de compras/recebimento e expedição/entrega foram preservados; retorno físico e E2E autenticado seguem pendentes. |

## Evidência incremental — Central de Operações (2026-09-11)

| Capacidade | Estado | Evidência, impacto e critério restante |
|---|---|---|
| Login/contexto/contratação | Implementada sem verificação nesta rodada | Central usa tenant/ator do servidor e permissões persistidas; login E2E e invalidação após troca de contrato exigem API/PostgreSQL. |
| Aprovações, compras, divergências, qualidade, logística, manutenção e contas | Implementada sem verificação de execução | Consulta agregada/paginada em `WorkManagementService`; concluir exige migration 073, plano de execução e cenários positivos/negativos por origem. |
| Visualizado e atribuído | Implementada sem verificação de execução | Estado e eventos em `agro360.operation_occurrence_*`; concluir exige concorrência e RLS com dois tenants. |
| Resolvido | Implementada por derivação, sem homologação | Não há botão de resolução; item cessa quando a operação original deixa de estar pendente. Validar cada transição de origem. |
| Atividades agrícolas, documentos e produção industrial | Parcial/ausente na central | Modelos existem, mas falta consolidar regra canônica de prazo/bloqueio. Impacto: central ainda não cobre todas as famílias solicitadas; depende da estabilização dos fluxos E6/E7/conformidade. |
| UX, ajuda, filtros e responsividade | Implementada sem navegador | `/Work`, `work.js` e CSS dedicado; concluir exige teclado, leitor de tela, viewport móvel e console sem erros. |
| Jornadas compras/qualidade e comercial/entrega | Parcial | Central navega para os fluxos já implementados; recebimento de retorno, conciliação financeira e E2E transacional permanecem critérios de conclusão. |

Próxima etapa: estabilizar esta fatia em banco descartável e navegador; depois incluir produção/campo/documentos apenas a partir de regras de domínio persistidas, antes de custos/conciliação, relatórios e mobilidade ampliada.

### Evidência incremental — 2026-09-14

| Item | Estado real | Evidência / limite |
|---|---|---|
| Central: leitura, atribuição e paginação | Implementado, não homologado em runtime | Migration 074, serviço/API e modal Web; SQL/JS estáticos aprovados; .NET e PostgreSQL indisponíveis |
| Logística: recebimento/destinação de retorno | Implementado parcialmente, não homologado | Recebimento parcial/idempotente e decisão auditada; liberação de saldo disponível e conciliação financeira ainda não declaradas |

## Atualização 2026-09-14 — colheita e produção agrícola

| Capacidade | Estado | Evidência / limite |
|---|---|---|
| Organização, autenticação/MFA, permissões e escopo | Implementada sem verificação nesta execução | Reutilizada sem alterar credenciais; SDK/API/navegador indisponíveis |
| Propriedades, talhões, safras e unidades | Parcial | Referências tenant/compatibilidade validadas na 075/`HarvestService`; cadastro anterior não re-homologado |
| Planejamento e apontamento de colheita | Implementado, não homologado | Plano ≠ realizado; área ≠ quantidade; idempotência e safra aberta |
| Recebimento parcial e conferência | Implementado, não homologado | Trava da origem, saldo, tara/massa, líquido backend e lote |
| Qualidade/classificação | Parcial | Reusa especificação/versão e resultados obrigatórios; coleta dinâmica completa na UI pendente |
| Destinação e estoque | Implementado, não homologado | Parcela aprovada entra no saldo/movimento existente; demais estados preservam genealogia |
| Rastreabilidade | Parcial | Consulta agrícola → apontamento → recebimento → destinação; documentos/operações posteriores ainda dependem dos módulos de origem |
| Custos da safra e indicadores | Parcial | Apropriado real e denominadores seguros; planejado/rateio/moedas/fechamento ainda não concluídos |
| Central de Operações | Ausente para os cinco novos tipos | Não foi criado checklist ou prazo fictício; projeção será adicionada no serviço canônico |
| Relatórios/CSV | Ausente neste recorte | Dashboard filtrável implementado; exportação segura permanece pendente |

### Incremento AG-E7-001 — beneficiamento integrado (2026-09-14)

| Capacidade | Estado | Evidência | Pendência |
|---|---|---|---|
| Recebimento aprovado → reserva parcial → consumo | Implementado, não homologado | contratos/serviço industrial; migration 076; locks e idempotência por hash | PostgreSQL concorrente e E2E autenticado |
| Resultado principal/coproduto/perda → qualidade → estoque | Implementado, não homologado | `production_output_requests`, lote PENDING, liberação com movimento `PRODUCTION_BATCH` | validar instalação limpa/upgrade e aprovação concorrente |
| Custos e rastreabilidade | Parcial | consulta de custos preserva pendência; genealogia industrial existente | rateio configurável, retificação e navegação agrícola expandida |
| UX operacional | Parcial | formulários com seletores, ajuda, bloqueio de duplo envio e foco de erro | detalhes expansíveis, confirmações completas e screenshot/navegador real |

### Consolidação operacional AG-E7-001 — 2026-09-14

| Capacidade | Estado | Evidência | Gate pendente |
|---|---|---|---|
| Qualidade final → lote disponível | Implementado, não homologado | diálogo real, seletor tenant-scoped, laudo/evidência obrigatórios no domínio e movimento transacional existente | API/PostgreSQL/navegador |
| Detalhe e transições da ordem | Implementado, não homologado | saldos distintos, histórico, próxima ação e endpoints explícitos com revalidação | E2E por perfil e responsividade |
| Reenvio de reserva/consumo/resultado | Implementado, não homologado | chave preservada após falha e trocada somente quando conteúdo muda ou servidor confirma | perda de resposta e concorrência real |
| Retificações e Central específica | Ausente | não houve atalho por status, exclusão ou prazo fictício | modelar fatos compensatórios e projeções canônicas |


## Incremento 2026-09-14 — reabertura do fechamento

| Capacidade | Estado | Evidência | Limite comprovado |
|---|---|---|---|
| Conferir e versionar fechamento operacional | Implementada, não verificada em runtime | `HarvestService`, `HarvestController`, `/Harvest`, migration 077 | SDK e PostgreSQL ausentes no container |
| Reabrir fechamento com histórico | Implementada, não verificada em runtime | `ReopenAsync` cria sucessora, exige motivo/versão, trava safra, audita e preserva a fechada | E2E autenticado pendente |
| Custos reconciliados por safra | Parcial | indicador usa somente `cost_entries.season_id` | integrações sem vínculo inequívoco não são somadas |
| Receita/estoque atual atribuídos à safra | Bloqueada por genealogia | indicadores explicitamente indisponíveis | completar vínculos produção–expedição–financeiro |

**Atualização requisições internas (2026-09-15):** jornada `/Inventory` e migration 081 implementadas sem homologação runtime. Solicitação, aprovação configurável, reserva concorrente, entrega parcial, apropriação de consumo e devolução condicionada são fatos persistidos separados; o saldo/movimento canônico é reutilizado. Restore/build/testes e PostgreSQL/E2E permanecem pendentes por ausência do SDK/banco neste contêiner. Evidências e limitações em `REQUISICOES-INTERNAS-MATERIAIS.md`.
