# Auditoria e matriz da jornada agrícola — incremento 2026-10-11

Documento de trabalho do incremento "jornada agrícola integrada" (planejamento de safra → execução de campo → materiais → custos → colheita → qualidade → rastreabilidade), incluindo as correções do Bloco A e a regressão visual (que incorpora a homologação pendente de Compras).

## Baseline registrado (seção 1 do prompt)

| Item | Valor |
|---|---|
| Branch / HEAD | `main` / `05a15ec6d02e6a822ebdf0bd428c8c5830a19ed1` ("novos ajustes", 2026-10-11 07:34:41 -0300) |
| `git status --porcelain` | limpo (delta do incremento anterior absorvido pelo usuário neste commit) |
| SDK | .NET `10.0.400` (solução `net10.0`) |
| PostgreSQL | 18 em WSL/localhost:5432 (`vmmemWSL`) |
| Migration mais alta | `134_french_language_support.sql` → schema `11.24.0` (reconfirmado) |
| Evidência E2E Compras | `artifacts/procurement-journey-e2e-4e570923d65a468fb12bb97f1ec0414a/` íntegra; **11/11 etapas PASS**, schema 11.24.0, 2026-10-10 09:46:12 UTC — registro reconfirmado |
| Banco de homologação | `agro360_homolog` (limpo do full.sql atual, 861 tabelas, 0 pares de health faltantes) |
| Hosts v6 → **v7** | v6 finalizado 11:29 (PIDs 29340/31408); v7 no ar 11:29 com ambiente idêntico (bat `launch-api7/web7.bat` em Temp/opencode; URLs 7081/8081 + 7080/7083; mesmo pfx SAN; `agro360_homolog`). Pronto c/ curl: 7080=200, 8081/health=Healthy, 7081=https 200 |

## Reconfirmação dos registros anteriores

1. **Jornada Compras 11 etapas verde** — confirmado (SUMMARY + transcript íntegros).
2. **i18n operacional de Compras nos 4 idiomas** — confirmado: `wwwroot/js/procurement.js` → `PROC_I18N` 511 chaves × 4 idiomas, único módulo operacional totalmente traduzido.
3. **Homologação visual pendente** — confirmada; entra na regressão visual deste incremento (seção 11).
4. **`CancelOrderAsync` × coluna `version`** — **registro obsoleto, corrigido**: `version bigint not null default 1` existe nos dois caminhos — migration `100_procurement_requisition_lifecycle.sql` (L3–7) e full.sql (L6860–6864); verificado diretamente em `agro360_homolog` e na dev DB. O diagnóstico antigo leu só o CREATE TABLE (L2477, sem `version`). **Bloco A 3.5 vira validação comportamental** (concorrência/replay/parcial recebido) — o serviço já usa `for update` + `row.Version != x.Version` + guarda de status, coerente com o contrato.

## Síntese dos três mapas de código

### Canônico (ledger/custo/outbox/permissoes/concorrencia/migrações)

- Movimento canônico = função `agro360.inventory_apply_stock_movement(tenant,warehouse,product,quantity[c/sinal],cost,type,reference,lot,expires,user,reason)` (migration 106; full.sql L7345; definição legacy anterior em L1241 superada por `create or replace`): advisory lock `tenant:warehouse:product` + balance `for update`, custo médio móvel, baixa de lote quando `p_lot` informado, bloqueio por contagem aberta (trigger `inventory_stock_movement_count_guard`, hint `count.movement_blocked`), INSERT em `inventory_stock_movements` com `movement_type` mapeado de `p_type` (`ENTRY/PURCHASE_RECEIPT→RECEIPT`, `EXIT/MAINTENANCE/FUEL→CONSUMPTION`, `ADJUST±`, **outros tipos passam crus e batem no CHECK da tabela** — consumir de agricultura deve usar `'EXIT'`). `idempotency_key` preenchido pelo chamador (UNIQUE parcial `(tenant_id,idempotency_key)`).
- Escritores diretos de saldo existem em ~15 serviços (Harvest, Logistics, MaterialRequest, StockControl, IndustrialProduction, Livestock*, Commercial*, Fleet, Sales) — todos com lock+ledger+chave; `Agriculture360Service.ApplyEffects` é o único sem depósito/ledger/lock.
- Custo: `cost_entries` (legado operacional) + `cost_management_entries` (canônico, `source_key`, unique `(tenant,source_type,source_key)`, planned/committed/recognized/paid/allocated/reversed `numeric(18,4)`, backfill `'legacy:'||id`). Escrita via `PostingService.PostMaterialCostAsync/PostActivityCostAsync`; `SeasonCostService` tem engine completa Preview→Confirm→Reverse (batches idempotentes, métodos DIRECT/PERCENTAGE/AREA/PRODUCTION/HOURS/EQUAL, `RoundMoney` 4 casas AwayFromZero + restorno na última linha, snapshot jsonb, review de fechamento vs corte).
- Fechamento com snapshot: modelo `harvest_closing_runs/versions` (cadeia `supersedes_id`, revalidação antes do close, advisory lock `{tenant}:harvest-closing:{season}`).
- Pós-commit: `OperationalInspectionTrigger.TryStartFromOriginAsync` (intents `quality_inspection_event_intents`, key `EVT:{processCode}:{originId:N}` + hash) ou outbox `EnqueueAsync`→`OutboxWorker` (backoff exponencial, dead-letter).
- Permissão: policy por código + re-checagem DB (claim, roles, tenant, módulo contratado); farm scope via `X-Farm-ID` validado contra `identity_user_unit_scopes` (403 `forbidden_unit_scope`) + filtros manuais `@FarmScope`; RLS `app.tenant_id` como fronteira. Controllers agrícolas conformes.
- Dinheiro: `numeric(18,4)` valores / `numeric(20,6)` quantidades, `decimal` nativo, `SeasonCostRules.RoundMoney`. Migrações: runner com checksum SHA256; próxima = `135_*` → `11.25.0`.

### Front (telas agrícolas, contratos, menu, central, gates)

- Telas: `/agriculture` (8 módulos), `/field` (PWA offline), `/Harvest`, `/Costs`, `/Work` (Central), `/livestock`, `/properties`, `/Production`, `/Maps`.
- **i18n: 0 chaves traduzidas em qualquer JS agrícola** (100% PT-BR hardcoded: campos, estados, ações, `prompt/confirm/alert`, erros, ajuda, `toLocaleString("pt-BR")`).
- `work.js` e `field.js` usam `fetch` próprio **sem `X-Culture`/`X-Farm-Id`** (divergem do `api()` canônico de `agro360.js`).
- Menu hardcoded em `_Layout.cshtml` (`data-permissions`) + entitlement narrowing via `/api/account/effective-access`; contexto ativo em localStorage → header `X-Farm-Id`.
- Central de Trabalho projeta 22 tipos: `APPROVAL, PURCHASE, PURCHASE_APPROVAL, REQ_APPROVAL, QUOTATION, SUPPLIER_RETURN, RECEIPT_DIVERGENCE, INVOICE_MATCH, QUALITY, QUALITY_CASE, SHIPMENT, MAINTENANCE, PAYABLE, COST_PENDING, SEASON_CLOSING, FIELD_WORK, TASK, GENEALOGY_BREAK, SCHEDULE_UNASSIGNED, SCHEDULE_LATE, DELIVERY_FAILED`.
- Gates estáticos: `node scripts/verify-offline-shell.mjs`, `node --check` por JS, `python tools/check-api-routes.py`.

### Núcleo agrícola (endpoints, máquinas, colheita, dashboard, lookups)

- `Agriculture360Controller` → `api/agriculture/{module}` (GET/POST/PUT) + ações `approve|revise|start|pause|complete|cancel` + endpoints de plano (`overview/plans/operations/dependencies/orders/reschedule`) + endpoints de ordem (detail/resources/work-logs/materials/events/review). **Não existe rota genérica `/transition`** — apenas as seis ações verbais.
- Tabela polimórfica única `agriculture_records` (jsonb `data`) para os 8 módulos; histórico `agriculture_status_history`.
- Máquina de estados: **só `work-orders` tem máquina** (parelhas L112 + pré-requisitos de RELEASED + trava de dependência REQUIRED de predecessora não concluída L119–125 + conferência `ReviewAsync` versionada sem issues BLOCKER nem material em custódia). Os outros 7 módulos: sem validação de estado de origem; `CreateAsync` aceita os 12 estados (inclusive terminais); `UpdateAsync` genérico aceita **qualquer estado-alvo** (inclusive terminal) em registro não-terminal, full-replace de `data`, sem predicado de versão.
- Efeito de estoque (`ApplyEffects` L159): dispara **somente no CreateAsync como COMPLETED** (aplications/field-notes com produto+quantidade); UPDATE cru sem `warehouse_id`/ledger/lock/lote; mensagem cita "produto vencido" sem checar validade; assimetria: `complete` via transição não baixa.
- Materiais de ordem de campo (`FieldOperationsService.ApplyMaterialEventAsync`): eventos RESERVE/DELIVER/CONSUME/RETURN/LOSS/RELEASE/REVERSAL com contadores encadeados (RESERVE≤planned, DELIVER≤reserved, CONSUME+RETURN+LOSS≤delivered), idempotência com fingerprint de payload, REVERSAL exige igual quantidade e único por consumo (custo negativo), **baixa física via função canônica** (CONSUME/LOSS→EXIT neg; RETURN/REVERSAL→ENTRY pos; `p_lot=null`), trava de quarentena `procurement_receipt_quarantine` no CONSUME (incidente `REJECTION_AT_USE`), custo por `PostingService`.
- Colheita (`HarvestService`): planos idempotentes + coerência de fazenda + área≤talhão + unidade=base_unit; registros **sem teto cumulativo** de área/qtde vs plano (multi-pass ilimitado); recebimentos parciais limitados ao saldo do registro; bruto≥tara aceito **com igualdade (líquido 0)**; líquido **nunca reconciliado** com `received_quantity`; inspeção disparada **direto após commit, sem outbox** — crash entre commit e trigger ⇒ replay cai no short-circuit `Replay` (L93) e **não dispara de novo** ⇒ recebidamente preso em `AWAITING_INSPECTION`; intents presos (`PENDING/PENDING_MODEL`) nunca re-acionados automaticamente; `InspectAsync` all-or-nothing (aceita 100% ou 0, sem parcial); `AllocateAsync` escreve saldo+movement à mão (**sem `inventory_stock_lots`**, custo médio não recalculado); closing versionado com revalidação; genealogia `operational_genealogy_links` com checagem de ciclo app-side.
- Dashboard genérico (`DashboardAsync` L148): `PlantedArea`=soma de área **planejada** de safras ativas; `PlannedCost`=soma de `estimatedCost` **todos status**; `ActualCost`=**estimado** das linhas COMPLETED; `CostPerHectare`=esse numerador ÷ área de **todos os registros**; `ExpectedYield`=soma de **todas** as safras; **`ActualYield`=0 constante**. Sem filtro de fazenda (tenant-wide). Dashboard de colheita (`HarvestService` L194–209) usa tabelas reais + guarda de unidade (`harvest.incompatible_dashboard_units`).
- `LookupService`: 16 fontes, **todas tenant-only** (ignoram `X-Farm-ID`); expõem legados `purchasing_suppliers`/`purchasing_purchase_orders`.
- Erros: 3 famílias misturadas — códigos gerados de labels PT com acento (`agriculture.talhão_required`), domínios estáveis (`harvest.*`, `inspection.*`, `closing.*`) e outliers prefixados (`agro360.agriculture_transition_invalid`, `agro360.agriculture_stock_unavailable`…).
- Rastreabilidade: `traceability_nodes/edges` escritos por `AgricultureService` (plantio/colheita) + Commercial/Livestock; `operational_genealogy_links` (095/102, imutáveis, ciclo app-side em Harvest); `public_trace_publications` (096) em `PublicTraceabilityService`.
- `purchasing_*` (legado) e `procurement_*` (moderno) coexistem sem bridge; consumidores legados: `OperationsService`, `LookupService`, `DocumentService`.
- Migrations agrícolas: 075/077/078/085/086/087/092/093/095/096/101/102/106; várias tabelas-base existem **só no full.sql** (instalador canônico é o full.sql — migrations assumem seu baseline).
- Testes: unit `GenealogyRulesTests`, `PublicTraceabilityRulesTests`, `OperationalInspectionTriggerTests`; architecture (form tests c/ idempotência de estorno e integridade de execução); integration `ProcurementConversionJourneyTests`. **Sem teste direto de `ApplyEffects`, tare/parciais de `ReceiveAsync` ou upsert de `AllocateAsync`.**

### Flags suspeitos consolidados (evidência → mudança)

| # | Flag | Evidência | Mudança |
|---|---|---|---|
| A | Consumo desconta todos os depósitos do produto (sem `warehouse_id`) | `Agriculture360Service` L159 | A1 |
| B | Assimétrico: só create-as-COMPLETED baixa; edição/transição p/ COMPLETED não baixa; msg cita validade sem checar | L79/L159 vs L100–146 | A1/A2 |
| C | Sem máquina de estados em 7 dos 8 módulos | L110–127 | A2 |
| D | CreateAsync aceita estados terminais (APPROVED/COMPLETED/CANCELLED/CLOSED) | L151–156 | A2 |
| D2 | UpdateAsync genérico chega a estado terminal por edição (alvo livre) | L92 | A2 |
| E | Indicadores com constantes/fontes erradas; `ActualYield=0` | L148 | A4 |
| F | Colheita sem teto cumulativo; líquido×quantidade não reconciliado; bruto==tara aceito | `HarvestService` L66–139 | A6 |
| G | Inspeção pós-commit fire-and-forget; replay não re-dispara; intents presos não re-acionados | `HarvestService` L92–136; trigger L179–192 | A6 |
| H | `AllocateAsync` sem lotes, custo médio não recalculado (3 caminhos distintos de escrita) | L181–187 | A6 |
| I | 16 lookups sem escopo de fazenda; `ValidateReferences` não cruza com contexto selecionado; `FieldIds` do plano não validados (só `FieldId`) | L13–32, L158 | A3 |
| J | Suprecedores/pedidos duplicados `purchasing_*`×`procurement_*` sem bridge | mapa §10 | manter (comprovar consumidores); bridge em próxima expansão |
| K | Status numérico de safra com 3 leituras diferentes (3→HARVESTED vs COMPLETED vs CLOSED) | `Season.cs`, `SeasonTrackingService` L18, `AgricultureService` L105 | normalizar num mapeador único (A3) |
| L | Códigos de erro derivados de labels PT (acentuados) misturados a domínios estáveis | `Agriculture360Rules` | padronizar mapa label→código estável (A2, sem quebra: manter atuais como alias quando possível) |
| M | Dupla definição da função canônica no full.sql (L1241 vs L7345); merge `data\|\|` mantém chaves obsoletas | full.sql; L91 | registrar como limite; remover legado se seguro (não bloqueia) |

## Matriz por jornada (estado real → evidência → lacuna → dependência → alteração → aceite)

| # | Jornada | Estado real | Lacuna → alteração | Aceite |
|---|---|---|---|---|
| M1 | Consumo de material em aplicação/caderno | UPDATE cru, sem depósito/ledger/lock/lote; dispara só em create-as-COMPLETED | **A1**: exigir `warehouseId`; chamar `inventory_apply_stock_movement('EXIT')` com `reference_id=registro`, `idempotency_key='agri:{recordId}'` + fingerprint (mesma chave mesmo conteúdo=replay equivalente; diferente=409 `agriculture.material_effect_conflict`); efeito único compartilhado create×transição; erro honesto (`insufficient stock` vs vencido); front: seletor de depósito (lookup filtrado pela fazenda) | A |
| M2 | Cancelamento de pedido (Compras) | Coerente com schema (`version` existe nos 2 caminhos + dev DB) | **A5**: ETAPA 12 no E2E — cancelamento concorrente (2× mesma versão → 1× 204 + 1× 409), replay pós-sucesso → conflito de estado, pedido `PARTIALLY_RECEIVED` cancelável mantendo recebimentos/movimentos | K |
| M3 | Estados e efeitos de negócio | Só work-orders tem máquina; criação/edição atingem terminais | **A2**: create bloqueia terminais (APPROVED/COMPLETED/CANCELLED/CLOSED) — applications/field-notes recebidos já COMPLETED viram create+complete atômico (compatibilidade); UpdateAsync não altera estado (remove `@Status` do SET, ignora `command.Status`); machine genérico mínimo: terminais irreversíveis, `COMPLETED` só de estados ativos, `APPROVED` de {OPEN,PLANNED,REVISION}, `REVISION` de APPROVED, `CLOSED` de {COMPLETED,APPROVED}, cancel de não-terminal com motivo; version guard uniforme; conferência de ordem mantém gate dedicado | B |
| M4 | Contexto e lookups | Lookups tenant-only; referências não cruzam com `X-Farm-Id`; `FieldIds` não validados | **A3**: narrowing por `@FarmScope` nas fontes com dimensão de fazenda (fields, production-areas, crop-seasons, warehouses, storage-lots; `properties` permanece lista de unidades autorizadas do usuário); `ValidateReferences` + listas + dashboard com cruzamento fazenda; validar **todos** `FieldIds` do plano ⊂ propriedade/fazenda; mapeador único de status numérico de safra | C |
| M5 | Indicadores agrícolas | Estimado apresentado como realizado; `ActualYield=0`; denominadores mistos | **A4**: separar planejado×realizado×não-calculável (null ≠ 0); `ActualCost` de fonte real reconhecida; `ActualYield` de colheitas reais (null se nenhuma); custo/ha com denominador de área efetiva e período; tudo respeitando `@FarmScope` | J |
| M6 | Planejamento executável | `SeasonTrackingService` + trava de dependência em release de ordem | Verifier detalhado do serviço (ciclos em dependências, reprogramação→sucessoras, geração de ordens idempotente) antes de mudar; aceitar: 2 ops dependentes, liberação fora de ordem negada, reprogramação com impacto, geração sem duplicar | D |
| M7 | Execução de campo e materiais | Engine de eventos completa (contadores, quarentena, estorno imutável, baixa canônica) | Documentar etapa de baixa física (CONSUME/LOSS/RETURN); revisar correções de apontamento confirmado (histórico); `p_lot=null` — lote quando produto exige (A1/A6) | E |
| M8 | Custo com origem | Engines completas; risco de dupla apropriação compra×consumo e de `source_key` por material em campo | Auditor de escritas: confirmar `source_key` estável por evento (ler `PostingService`), reconciliar operação vs materiais/apontamentos, tarifa ausente→pendência, estorno referencia apropriação original | F |
| M9 | Colheita/recebimento/qualidade | Fluxo completo com parciais e closing versionado | **A6**: líquido obrigatório >0 com peso; divergência líquido×quantidade exige motivo (422 `harvest.net_weight_mismatch`); teto cumulativo de área multi-pass = área do talhão; replay re-dispara inspeção quando recebidamente ainda `AWAITING_INSPECTION`; re-ação periódico de intents pendentes (via worker existente); `AllocateAsync` passa a criar lotes (função canônica) | G,H |
| M10 | Genealogia/rastreabilidade pública | Tabelas + publicação opaca + ciclo app-side | Verifier: lote derivado de 2 origens → destinação → snapshot público sem vazamento; garantir lotes após A6 | I |
| M11 | Central de Trabalho | 22 tipos projetados com permissão por tipo | Auditor por tipo: causa/próxima ação/URL/revalidação/atualização pós-resolução (testes ao vivo) | J |
| M12 | i18n telas agrícolas | 0 chaves traduzidas; work/field sem headers de cultura/fazenda | **A7**: dicionários 4 idiomas nos JS agrícolas (padrão `PROC_I18N` + evento culture), paridade verificável; `work.js`/`field.js` passam a enviar `X-Culture`/`X-Farm-Id` | L |
| M13 | Regressão visual Compras | API verde; visual nunca executada; **hosts AMARRADOS** | Executar `HOMOLOG-RUNBOOK.md` em `agro360_homolog` (4 idiomas × 360/768/1440 + zoom 200%, screenshots, console/network, contagens §9) | L |

## Plano de implementação (ordem por dependência)

1. **A1** — `Agriculture360Service.ApplyEffects` → comando canônico (M1): contrato ganha campo de depósito (`AgricultureCommand` — confirmar nome exato em `Agriculture360Contracts.cs`); front adiciona seletor; E2E de reenvio com 2 depósitos.
2. **A2** — Máquinas de estados create/update/transition + códigos estáveis (M3).
3. **A3** — Escopo de fazenda em lookups/listas/dashboards/referências + `FieldIds` + mapeador de safra (M4).
4. **A4** — Reescrita das métricas do dashboard genérico (M5).
5. **A5** — ETAPA 12 de cancelamento no E2E de Compras (M2).
6. **A6** — Colheita: peso/parciais/tetos/recuperação de inspeção/lotamento (M9).
7. Auditorias leves: M6 (deep-read de `SeasonTrackingService`), M7, M8 (`PostingService.source_key`), M10, M11.
8. **A7** — i18n dos módulos agrícolas (M12) + headers nos fetch próprios.
9. **M13** — Regressão visual completa (Compras + telas agrícolas) nos hosts v6.
10. Docs finais: checkpoint (aprovado/falhou/bloqueado/não executado), rastreabilidade, i18n report, prompt de continuidade, `LIMITES.md`.

Meta: **zero novas migrations** se nenhum A exigir DDL (função, índices e tabelas já existem; monitorar — qualquer nova migration sai como `135_*` → `11.25.0` com append no full.sql).

## Log de execução — sessão 2 (build + hosts v7 + gates + E2E + browser) — 2026-10-11

1. **Build e hosts v7** — hosts v6 finalizados (PIDs 29340/31408); build Release da solução completa GREEN incluindo M6 (`SeasonTrackingService.GenerateOrderAsync`: advisory lock + replay `agriculture.idempotency_conflict`) e M8 (`source_key` estável em `FieldOperationsService`/`PostingService`). Um erro de compilação do fix M6 foi sanado na hora: tupla non-nullable do replay → elementos nullable `(Guid? WorkOrderId, Guid? OperationId, decimal CoveredArea)` (sem linha, o Dapper retorna default). Bats `launch-api7.bat`/`launch-web7.bat` replicam o ambiente v6 byte a byte (exceto nome de log). Prontidão confirmada por curl: `https://192.168.0.10:7080`=200, `http://…:8081/health`=Healthy, `https://…:7081/health`=200.
2. **Gates estáticos** — `node scripts/verify-offline-shell.mjs` PASS; `python tools/check-api-routes.py` OK (946 operações únicas); unit tests **390/390 pass** executando a dll diretamente (o wrapper `dotnet test` falha neste ambiente com exit 5/"Zero testes" por quirk de descoberta MTP — pré-existente, documentar em LIMITES); architecture tests **186/186 pass** após atualizar 2 asserções que procuravam literais PT movidos para o dicionário i18n:
   - `AgricultureFormTests.FieldOrderTransitionsProtectConcurrentChangesAndServerTimestamps` → agora asserciona `tr("arc.noCompatibleOption")` no cliente **e** o valor pt-BR no dicionário `agro-i18n.js`.
   - `CanonicalSchemaTests.HarvestClosingIsImmutableTenantScopedAndActionable` → agora asserciona `tr("har.zeroBasePct")` no cliente **e** o valor pt-BR no dicionário.
3. **E2E Compras (12 etapas)** — cluster PG18 descartável clonado do template durável (schema 11.24.0; zero migrations novas).
   - **Run 1 (11:44–11:53)**: etapas 1–11 100% PASS (SoD de aprovação/devolução, idempotência de conversão/recebimento/decisão de qualidade, quarentena→estoque só do aceito, match documental c/ exceção+SoD, previsão financeira OPEN, isolamento de tenant, delegação de escopo). Etapa 12: 6/7 PASS; **1 assert falhou** — `Cancel-parcial-preserva-recibos-e-estoque` leu `mov_recebido=0` (esperado 95). Raiz: a função canônica (migration 106) **normaliza** `p_type` `PURCHASE_RECEIPT`→`movement_type='RECEIPT'` e preserva o tipo bruto em `reference_type`; o filtro do script usava `movement_type='PURCHASE_RECEIPT'` (nunca existiria — os asserts anteriores desse filtro esperavam 0, por isso nunca falharam). Correção no script (3 filtros → `movement_type='RECEIPT' AND reference_type='PURCHASE_RECEIPT'`) + detalhe do assert documenta a normalização. Demais métricas da etapa batiam (recibos=2, devolução=10, disponível=85).
   - **Run 2 (12:04)**: falha transiente de ambiente — API do E2E parou de aceitar TCP ~7 s após o "Application started", sem exceção logada (nenhuma entrada relevante no Event Log de Aplicação).
   - **Run 3 (12:13 UTC)**: **12/12 etapas PASS** — "PROCUREMENT JOURNEY E2E COMPLETO COM SUCESSO". Etapa 12 íntegra: sem motivo 422, versão stale 409, concorrência mesma versão = exatamente 1×204+1×409, replay pós-sucesso 409, `CANCELLED` c/ motivo + evento + versão+1, pedido parcialmente recebido preserva recibos=2 / mov_recebido=95 / mov_devolução=10 / disponível=85 (sem estorno automático), recebimento pós-cancelamento 422, requisição reaberta para `APPROVED`. Evidência: `artifacts/procurement-journey-e2e-bc06fb363bde45678c2fb5cf4f30dc02`.
4. **Smoke server-side das 5 telas (não autenticado)** — `/Agriculture`, `/Work`, `/Harvest`, `/field`, `/Costs` respondem 200 com o shell público (len idêntico, por design offline-shell); `/js/agro-i18n.js` servido (196.730 bytes; guard `module.exports={AGRO_I18N}` presente). Os marcadores `data-i18n-ag*` só renderizam autenticado → verificação final no browser.
5. **Browser** — túnel do desktop rejeita o self-signed (`ERR_CERT_COMMON_NAME_INVALID`). Certificado validado como correto (SAN `localhost`+`192.168.0.10`, thumbprint `C05DA1CAA96A04FDF9EB850D2281AB150531747F`, expira 2028). Pedido feito ao usuário para confiar no certificado no desktop; importação feita por ele — porém caiu nas lojas `CA`/`My`, e autoassinado só é confiado a partir de `Root` (em aberto nesta sessão).
6. **Raiz do bloqueio do browser encontrada e corrigida (TLS)** — os hosts v6→v8 serviam o **certificado dev padrão do .NET** (CN=localhost, thumbprint `31D899C4…`) em vez do nosso pfx: a variável `ASPNETCORE_Kestrel__Certificate__FilePath` (usada nos bats desde v6) mapeia para `Kestrel:Certificate:FilePath`, propriedade que **não existe** em `KestrelServerOptions` — e nem existe `Kestrel:CertificateFilePath` consumível por binding neste host (testado com caminho inexistente: sem efeito). Com o binding sem cert, o fallback automático dev-cert do .NET 8+ assumia, e o nome não casava com o IP de acesso. Fix aplicado: `Program.cs` de Web e Api ganhou bloco explícito que, quando `Kestrel:CertificateFilePath` está na configuração, carrega o PKCS#12 via `X509CertificateLoader.LoadPkcs12FromFile` e aplica com `options.ConfigureHttpsDefaults(https => https.ServerCertificate = …)` (sem config, comportamento padrão intacto — zero efeito no E2E, que sobe a API sem essa variável). Bats atualizados para `ASPNETCORE_Kestrel__CertificateFilePath`/`CertificatePassword` (formata plana, chega à configuração como `Kestrel:CertificateFilePath`). Hosts **v9** no ar: thumbprint `C05DA1CA…` + SAN `DNS:localhost, DNS:192.168.0.10` verificados via handshake TLS nos dois ports (7080 web / 7081 api); health 200 nos três endpoints; erro do túnel evoluiu de `COMMON_NAME_INVALID` para `AUTHORITY_INVALID` (só falta o trust na loja Root). Solução completa Release 0/0; architecture 187/187 revalidado após o change.

## Incremento de 2026-10-11 (baseline `dddc0bc`) — seções do prompt "jornada completa + recuperação + governança"

Novo baseline registrado: `main @ dddc0bc` ("novos ajustes", 2026-10-11 15:58 -0300), árvore limpa no início — absorve todo o delta do incremento anterior (A1–A6/M6/M8, Flag K, i18n 809×4, E2E Compras 12/12, fix TLS). A partir daqui os itens são os da seção do prompt atual; resultados separados em aprovado/falhou/bloqueado/não executado no fechamento (§14).

### §2 Bloco A — Prova comportamental do núcleo agrícola (gate) — APROVADO 59/59

**Instrumento**: `scripts/verify-agro-journey-e2e.ps1` — harness de integração com **cluster PG18 descartável** clonado do template durável `pg-agro-template` (schema **11.25.0**; services + API Release + PostgreSQL próprios; fixtures identificáveis; **nunca** o banco do usuário/`agro360_homolog`). Dois tenants (Santa Clara `30000000-…0001` e Cooperativa Vale Verde `394bcb4c-…`), duas fazendas na Santa (Sede `…0010` / Retiro Boa Vista `…0013`) e um operador com escopo estreitado (prova de 403). Cenários **A–F**.

**Gates na validação**: build Release **0/0**; architecture **187/187**; unit **390/390** (`sh_12ea70f20001QPDNdydMOhWVqc`) — todos os defeitos de produto abaixo foram corrigidos na **causa raiz** antes do verde (nunca se ajustou expectativa para esconder erro de negócio).

**Causas raiz corrigidas durante a validação** (defeitos de produto reais, não de teste):
- **Sweep de row-DTO (Npgsql 10/Dapper)** — 9 row-DTOs convertidos de *positional record* para classe `{ get; init; }` (o Npgsql 10 liga row-DTOs **por nome**; record posicional falha mesmo com colunas aliasadas) + selects `t.*`/snake_case exigindo alias PascalCase explícito de cada coluna ligada. Itens: `RecordRow`, `MaterialRow`, `OrderRow` (FieldOperationsService); `DashboardCore`, `AgricultureInputUnit` (Agriculture360Service/Contracts); `ClosingTotals`, `ClosingVersionRow`, `PreviousClosing` (HarvestService); `OperationRow` + `OperationSql` reescrito com aliases (SeasonTrackingService).
- **Bug 1 (consumo → 500 `42703 work_order_material_id does not exist`)** — o bloco de quarentena da *Quality Bridge* e a query de incidente referenciam `procurement_receipt_items.work_order_material_id`, inexistente. Fix: coluna nullable `work_order_material_id uuid` no `full.sql` + **migration 136** (`136_quality_bridge_receipt_item_material_link.sql`, sem FK — risco mínimo ao Compras).
- **Bug 2 (fechamento → 500 `42703 l.unit does not exist`)** — `inventory_stock_lots` não tem `unit` (a unidade vive em `inventory_products.base_unit`). Fix: query de stock-lots do fechamento faz `left join inventory_products p` selecionando `lower(coalesce(nullif(p.base_unit,''),'un')) Unit`.
- **Bug 3 (dashboard → 500 `42601 trailing junk near "2and"`)** — `OperationalScopePolicy.Sql/FarmIdSql/FarmSql` (strings-brutas C#) tinham o espaço inicial stripado → retornavam `and (…) **sem** separador` → `status=2and`. Fix: prefixar `" "` em cada helper + na constante `RecordScope` (Agriculture360Service).
- **Bug 4 (estorno → 422 `check_violation` no `cost_management_entries_check`)** — o REVERSAL publicava um lançamento com `recognized_amount` **negativo** (violava `recognized_amount>=0` e `allocated<=recognized`). Fix (canônico): em vez de lançar entrada negativa, o estorno incrementa o `reversed_amount` do lançamento **original** do consumo (coluna da migration 078; identifica-se por `source_type='MATERIAL' AND source_key=<id do evento de consumo>`). Todos os CHECKs seguem verdes, o histórico do livro canônico é preservado e não há mudança de constraint global (protege Compras/finanças). Sem nova migration (a coluna já existe).

**Ajustes de test-harness (não de negócio)** no `verify-agro-journey-e2e.ps1`: sintaxe de variável braceado `${ops['X']}` (indexer de hashtable exige subexpressão `$($ops['X'])` — 7 usos no bloco de reschedule); corpo JSON do teste de geração concorrente com `${Key}` escapado por crase (emitia literal → JSON inválido); `Start-Process -ArgumentList` em **array** divide argumentos com espaço e derrubava o token Bearer (**401**) → string pré-quotada única + `-o` do curl para capturar o corpo; guard `.IsReadOnly` em array vazio no assert `null-nao-zero` (o invariant é só `$unavailIsZero.Count -eq 0`).

**Resultado: 59/59 APROVADO** — evidência `artifacts/agro-journey-e2e-ae861b9fb6f74d609fc16eaf0e43d776`. Por cenário:
- **A · ledger canônico de materiais (12/12)**: consumo 200 → available 1000→800 com **exatamente 1** movimento CONSUMPTION; custo reconhecido **2500.0000** (decimal exato); replay idempotente **sem re-decrementar**; mesma chave com conteúdo diferente → 409 `idempotency_payload_conflict`; **estorno válido devolve o estoque (available volta a 1000)**; estorno duplicado → 409 `reversal_duplicate`.
- **B · máquina de estados (12/12)**: versão obrigatória (422); transição inválida (409); PLANNED→RELEASED→IN_PROGRESS→PAUSED→IN_PROGRESS→AWAITING_REVIEW; pausa sem motivo (422); cancelamento sem motivo (422); PLANNED→CANCELLED; terminal irreversível (409).
- **C · escopo/tenant (7/7)**: isolamento entre tenants (Santa não vê Vale e vice-versa); estreitamento por fazenda (farmA só vê farmA; farmB só farmB); fazenda fora do escopo → 403 `forbidden_unit_scope`; dentro → 200.
- **D · planejamento (12/12)**: grafo de dependências (self 422 / par 204 / ciclo 409 / obrigatória C-requer-B); ordem gerada; liberação fora de sequência bloqueada (409 `dependency_blocked`); reschedule aplicado (revisões=1) **sem propagar datas aos irmãos**; **geração concorrente com a mesma chave → uma única ordem (idA==idB, 1 linha)**.
- **E · colheita → lote disponível (12/12)**: plano de colheita; passe 1 com área aceita; teto de área multi-passe (409 `area_exceeds_field`); recibo parcial; recibo acima do saldo recusado (409 `receipt_exceeds_balance`); peso líquido divergente sem motivo (422) / com justificativa (201); inspeção aprovada define quantidade aceita; replay de inspeção sem duplicar decisão (409); destinação cria o lote com movimento PRODUCTION correto; destinação acima do aprovado bloqueada (409).
- **F · resultado gerencial (5/5)**: fechamento responde com **14 indicadores** (todos AVAILABLE/PROVISIONAL; nenhum UNAVAILABLE neste fixture); **null ≠ zero** (nenhum UNAVAILABLE exibe 0); custo realizado vem da fonte reconhecida (**api == db `sum(recognized_amount)` = 7292.0000**); custo/hectare **NULL** sem área executada; escopo por fazenda aplicado.

---

### §3 Recuperação automática de inspeções por evento operacional — IMPLEMENTADO (gates verdes)

**Diagnóstico confirmado** (auditoria de infraestrutura): trigger `OperationalInspectionTrigger` fire-and-forget pós-commit; intents presos (`PENDING`/`PENDING_MODEL`) nunca re-acionados; crash entre commit do recebidamente e criação do intent deixa a origem em `AWAITING_INSPECTION` sem intent nenhum (replay do comando cai no short-circuit idempotente e não re-dispara); worker de recuperação inexistente. Bug latente adicional: query de genealogia do `HarvestService` lia `i.process`/`i.error_code` inexistentes na join com intents.

**Correção (causa raiz)**:
- Migration `135_inspection_intent_recovery.sql` → schema **11.25.0** (aplicada em `agro360_homolog`; append no full.sql): intents ganham `attempts`, `last_error`, `error_code`, `next_attempt_at`, `claimed_at`, `claimed_by`, `lease_expires_at`, `dead_lettered_at`; status `RESOLVED` e `DEAD_LETTERED`; índices due/dead-letter. Script idempotente (guards `do $$` no pg_constraint — PostgreSQL não tem `ADD CONSTRAINT IF NOT EXISTS` em ALTER TABLE).
- Máquina de estados única `InspectionIntentProcessor` compartilhada por disparo inline, worker e re-disparo manual: short-circuit de estados terminais; revalidação de origem (MISSING → retry/backoff → dead-letter `inspection.origin_missing`; SETTLED → adota run existente pela chave idempotente ou RESOLVED; PENDING → `StartRunAsync` AUTOMATIC com o ator original); `AMBIGUOUS` em `inspection.ambiguous_model`; corrida na criação → adota run vencedor (`persistence.unique_violation`); ator inativo → `inspection.inspector_inactive` imediato; sem modelo → `PENDING_MODEL` (ciclo 30 min, teto 336 → `inspection.model_missing`); falha transitória → `PENDING` + backoff exponencial `min(3600, 2^attempts)` (semântica do outbox), teto 10 → `DEAD_LETTERED inspection.creation_failed`.
- `InspectionIntentRecoveryService`: varredura de adoção de origens confirmadas sem intent (janela 7 dias, teto 30/ciclo) para os 4 emissores (production_receipts `AWAITING_INSPECTION`; production_batches `PENDING`; procurement_receipts + fulfillment_return_receipts existência) + claim/lease `FOR UPDATE SKIP LOCKED` (lease 300 s, reclaim por expiração). Worker por tenant ativo `InspectionIntentRecoveryWorker` registrado em `Agro360.Worker`.
- Re-disparo manual auditado: `POST /api/inspections/event-intents/{id}/redispatch` (política `compliance.approve`), corpo `{Reason?, ActorId?}` (ator validado ACTIVE), zera contagem/erro/dead-letter/claim, anexa razão às notas, grava `audit_logs` action `INSPECTION_INTENT_REDISPATCH` e processa imediatamente (best-effort). 422 `inspection.intent_not_redispatchable` fora de PENDING/PENDING_MODEL/AMBIGUOUS/DEAD_LETTERED.
- Trigger refatorado delega ao processor; corrida no primeiro insert → re-leitura do vencedor + replay silencioso (mesmo hash) / 409 `inspection.idempotency_conflict` (hash diferente); chave/hash canônicos em `InspectionEventKeys`.
- List `GET /api/inspections/event-intents` estendido (aditivo) com `Attempts`, `LastError`, `ErrorCode`, `NextAttemptAt` — contrato mínimo de interface (situação, última tentativa, motivo, próxima tentativa).
- Fix `HarvestService`: genealogia `i.process` → `i.process_code` (coluna real) + `i.error_code` agora existe.
- Config em `InspectionIntents` (PollSeconds=15, BatchSize=20, LeaseSeconds=300, MaximumAttempts=10, ModelMissingPollSeconds=1800, ModelMissingMaximumAttempts=336, OriginSweepWindowDays=7).
- `notes` do intent acumulam ("|"-separadas) em vez de sobrescrever: histórico de tentativas preservado + nota de adoção sobrevive ao processamento.

**Gates**: build Release 0/0; architecture 187/187; unit 390/390; migration aplicada no homolog (8/8 colunas verificadas); hosts **v10** no ar (API pid 37724 health 200 / Web pid 20980).

**Aceite comportamental (E2E)**: script `scripts/verify-inspection-recovery-e2e.ps1` — cluster PG18 descartável (template durável, schema 11.25.0) + API + workers Release, overrides rápidos (poll 2 s, modelPoll 5 s, maxAttempts 3, lease 30 s). Cenários S1–S7: feliz+replay+genealogia; crash-antes-do-intent (insert direto, adoção pela varredura SEM reenviar comando); PENDING_MODEL c/ gate por `next_attempt_at` (sem loop apertado) → publicação de modelo → STARTED automático; origem excluída → backoff → DEAD_LETTERED + 403 do operador sem `compliance.approve`; re-disparo auditado (contagem zerada + auditoria) → restauração → STARTED; 2 workers concorrentes → exatamente 1 intent e 1 run por origem; reinício do worker com lease vencido → reclaim pelo novo processo. **Resultado do E2E (11/10/2026): APROVADO — TODOS OS CENÁRIOS PASSARAM (S1–S7).** Evidência: `artifacts/inspection-recovery-e2e-c8025a066959440583bb8e24106ac2b3` (snapshot de intents, runs no tenant=6, auditorias de re-disparo=1). Cenários comprovados ao vivo: **S1** modelo publicado → recibo → intent STARTED + replay byte-idêntico SEM duplicar intent/run + genealogia da safra 200; **S2** origem commitada FORA da API (crash antes do intent existir) → worker adota pela varredura e inicia SEM reenviar o comando original (1 intent / 1 run / nota de varredura); **S3** sem modelo → `PENDING_MODEL` com motivo "Nenhum modelo aplicável para iniciar a inspeção", gate por `next_attempt_at` sem loop apertado (attempts 1→1 após 9 s), publicação do modelo → STARTED automático; **S4** origem excluída → backoff progressivo (attempts 0→3) → `DEAD_LETTERED` com `error_code=inspection.origin_missing` + `last_error` preenchido + marcador persistido + operador sem `compliance.approve` recebe **403** no re-disparo; **S5** re-disparo auditado (contagem zerada + linha `INSPECTION_INTENT_REDISPATCH`) → restauração da origem → STARTED com exatamente 1 run; **S6** 2 workers concorrentes → exatamente 1 intent e 1 run por origem; **S7** worker morre com lease ativo → novo processo reclama (lease vencido) e processa, nenhum pendente perdido no reinício.

**Correções de causa raiz aplicadas durante a validação (idempotentes)**: (1) os métodos de recuperação do worker usavam o overload de tenant AMBIENTE de `InTenantTransactionAsync` → `ForbiddenException` fora da requisição → overload explícito de `tenantId` em `DatabaseExecutor` + 4 chamadas apontadas para ele (`AdoptFromCandidatesAsync`/`ClaimDueIntentsForTenantAsync`/`MarkProcessingFailureAsync`/`MarkSkippedNoActorAsync`); (2) a varredura de candidatos filtrava `q.deleted_at is null` em `production_receipts` (tabela de hard delete, sem a coluna) → erro 42703 abortava a adoção → filtro removido e pré-filtro de dedupe alinhado ao casing minúsculo `{id:N}` de `InspectionEventKeys.BuildKey`; (3) o pré-requisito "sem intent inicialmente" do S2 competia com o adotador de poll 2 s → re-formulado como fato determinístico (origem commitada fora da API), mantendo a prova de recuperação-sem-reenvio nos asserts de 1 intent/1 run/nota de varredura; (4) S3/S4 dependiam de recibo via API, cujo produto é herdado do plano único de colheita (grão 1, já modelado) e ignora o produto do comando — logo o pressuposto "produto sem modelo" nunca se concretizava → convertidos a insert direto (`New-DirectReceipt`) com produtos sem modelo explícitos (grão 2/grão 3), padrão já validado no S7 (grão 4); (5) o desfecho de *origem ausente* não liberava o claim (`ClearClaim`) como todos os demais caminhos de retry → o lease de 30 s engavetava o backoff curto além da janela → adicionado `ClearClaim: true` (o backoff passa a governar a re-tentativa); (6) **migração 135** não soltava o CHECK original de 5 status, pois `pg_get_constraintdef` normaliza `status in (...)` para `= ANY (ARRAY[...])` e o padrão `like '%status in%...'` nunca casava — coexistiam dois CHECKs e o antigo rejeitava `DEAD_LETTERED`/`RESOLVED` → substituição nome-independente pela coluna (`ilike '%(status)%'` + `not ilike '%dead_lettered%'`), aplicada a `migration 135`, `full.sql` e à homolog ao vivo.

**Reconfirmação limpa (12/10/2026)**: nova execução do script com `-RebuildTemplate` e binários já contendo as correções Dapper → **S1–S7 todos passaram** e o **worker saiu limpo** (log `worker*.out.log` sem qualquer erro de materialização: `OutboxWorker` publica eventos normalmente; ciclos de recuperação com `Failed=0`). Evidência limpa: `artifacts/inspection-recovery-e2e-afc2e394c0f94a1faad4491221edcf7d`. Dois ajustes de *test-harness* (não de negócio): o assert de S7 (liberação do lease pós-processamento) foi trocado para `Wait-Until { claimed_by == '' }` — a invariante real, pois o claim é liberado no MESMO UPDATE que incrementa `attempts` — em vez de uma leitura única imediata (corrida); e foi adicionado `exit 0` explícito no caminho de sucesso para o exit code ser fiel ao gate (antes ele herdava `$LASTEXITCODE` residual do `pg_ctl stop`). A prova de negócio é idêntica à do c8025a06, agora sem ruído no worker.

*Depois do verde dos cenários, foram tratados também os dois defeitos de materialização Dapper pré-existentes no worker (`OutboxEnvelope` e `ScheduleDueRow`, positional records que falhavam todo ciclo) — registrados na seção **§12 Dívida técnica** (abaixo).*

---

### §4 Planejamento de safra — APROVADO (via Bloco A · D)
Comprovado comportamentalmente no cenário **D** do Bloco A (12/12): grafos de dependência (self/ciclo/obrigatória), liberação fora de sequência bloqueada, reschedule com revisão e **sem propagação involuntária de datas** às atividades irmãs, e geração concorrente de ordens pela mesma chave idempotente consolidando em **uma única ordem** (advisory lock de transação + chave única em `SeasonTrackingService.GenerateOrderAsync`).

### §5 Ordens / apontamentos / custos — APROVADO (via Bloco A · B + A)
Comprovado no Bloco A: máquina de estados da ordem de campo com versionamento otimista (cenário **B**, 12/12) e ledger canônico de materiais com reconhecimento de custo em **decimal exato**, replay idempotente e estorno válido (cenário **A**, 12/12).

### §6 Colheita e lote disponível — APROVADO (via Bloco A · E)
Comprovado no Bloco A (12/12): jornada completa do apontamento de colheita ao **lote disponível** — teto de área por passe, recibo parcial, bruto/tara via divergência de peso líquido com justificativa, decisão de qualidade e destinação criando o lote com o movimento PRODUCTION correto.

### §7 Qualidade / disponibilidade / rastreabilidade — APROVADO (via Bloco A · E+F + §3)
Qualidade como gate de disponibilidade (só o aprovado entra em estoque — Compras etapa 6 e Bloco A E), genealogia rastreável por lote, e **recuperação automática de inspeções** (§3, E2E `afc2e394`, worker limpo) cobrindo crash/retry/backoff/dead-letter/re-disparo auditado.

### §8 Painel de resultado da safra — APROVADO (via Bloco A · F)
Comprovado no Bloco A (5/5): painel de fechamento com 14 indicadores respeitando **null ≠ zero**, origem reconhecida do custo realizado e custo/hectare nulo sem área executada. *Limitação registrada* (§12): os painéis somam `recognized_amount` **sem descontar** `reversed_amount` (estornos de material ficam rastreados, mas o dash mostra o bruto até o refinamento da consulta).

### §9 Central de Trabalho — PARCIALMENTE comprovado neste slice
A **regressão de Compras (12/12, run `2381aa5e`)** exercitou a Central de Trabalho de ponta a ponta nas ocorrências do módulo (REQ_APPROVAL, RECEIPT_DIVERGENCE, QUALITY, PURCHASE, SUPPLIER_RETURN, INVOICE_MATCH): `GET operation-center` 200, deep links corretos, **aparecer/desaparecer da fila após a decisão**, gate por permissão (invisível sem o token) e estreitamento por unidade. *Não executado para o núcleo agrícola*: tipos de ocorrência específicos (FIELD_BLOCKED, RESOURCE_UNASSIGNED, CUSTODY_PENDING, INSPECTION_PENDING, COST_PENDING) ainda não modelados — gap documentado para a próxima fatia.

### §10 Governança SaaS / menus — PARCIALMENTE comprovado neste slice
Suspensão de módulo por tenant comprovada na regressão de Compras (etapa 10): módulo 'purchasing' suspenso no Vale → **403 mesmo com permissão completa**, demais módulos íntegros; usuário sem `purchasing.read` → 403 na API + ocorrências filtradas na Central. *Não executado*: verificação de contrato de módulo no `SaveRoleAsync` (salvar papel pode referenciar módulo não contratado) e superfície de UI para estado suspenso — gaps documentados.

### §11 Idiomas / design
- **i18n — APROVADO**: paridade **809 chaves × 4 idiomas**, 0 ausentes/extras/vazias (matriz anterior desta seção).
- **Visual no browser — BLOQUEADO** (ação única do usuário): a raiz TLS já foi corrigida (hosts v9 servem o pfx correto, thumbprint `C05DA1CA…`, SAN `localhost`+`192.168.0.10`); o túnel evoluiu de `COMMON_NAME_INVALID` para `AUTHORITY_INVALID` — falta o **trust na loja `Root`** (duplo-clique em `Confiar-cert-agro360.bat`). Homologação visual **não declarada** enquanto o cert não estiver confiável; a matriz 4 idiomas × 360/768/1440 px + zoom 200% será preenchida após o trust.

---

### §12 Dívida técnica — tratada por impacto comprovado

| Item | Raiz | Correção | Estado |
| --- | --- | --- | --- |
| `OutboxEnvelope` (Agro360.Worker) | Row-DTO *positional record*; o Npgsql 10 liga row-DTOs **por nome** → erro de materialização a cada ciclo do `OutboxWorker` (`Falha no ciclo do Outbox Worker`). | Classe simples `{ get; init; }` (ctor parâmetro-less ⇒ bind por nome) + query com aliases snake_case→props PascalCase. | **RESOLVIDO + verificado** (E2E afc2e394: worker limpo; `OutboxWorker` publica eventos). |
| `ScheduleDueRow` (Agro360.Worker) | Idem, no `InspectionScheduleWorker`; além disso `ends_on date`→`DateOnly` e `next_run_at timestamptz`→`DateTimeOffset`. | Classe `{ get; init; }` + aliases de coluna para props; comparação vs `ToScheduleTimestamp` (`DateTimeOffset`). | **RESOLVIDO + verificado** (idem). |
| Sweep row-DTO do núcleo agrícola (9 classes) | Mesma regra do worker: *positional records* que o Npgsql 10 não materializa; selects `t.*`/snake_case sem alias PascalCase. | Classes `{ get; init; }` + alias de coluna. Afeta FieldOperationsService (`RecordRow`/`MaterialRow`/`OrderRow`), Agriculture360Service/Contracts (`DashboardCore`/`AgricultureInputUnit`), HarvestService (`ClosingTotals`/`ClosingVersionRow`/`PreviousClosing`), SeasonTrackingService (`OperationRow` + `OperationSql` aliased). | **RESOLVIDO + verificado** (Bloco A 59/59, `ae861b9f`). |
| Bug 1 — coluna `procurement_receipt_items.work_order_material_id` ausente | A *Quality Bridge* (quarentena) + query de incidente do consumo de material referenciam a coluna inexistente → 500 `42703`. | Coluna nullable no `full.sql` + **migration 136** (sem FK, mínimo risco ao Compras). | **RESOLVIDO + verificado** (Bloco A A). |
| Bug 2 — fechamento lia `inventory_stock_lots.unit` (não existe) | A unidade está em `inventory_products.base_unit` → 500 `42703 l.unit`. | Query de stock-lots do fechamento faz `left join inventory_products` selecionando `base_unit`. | **RESOLVIDO + verificado** (Bloco A F). |
| Bug 3 — separador de escopo ausente | `OperationalScopePolicy.Sql/FarmIdSql/FarmSql` (e `RecordScope`) retornavam `and (…) **sem** espaço inicial` → SQL `status=2and` (42601) no dashboard. | Prefixar `" "` em cada helper + constante. | **RESOLVIDO + verificado** (Bloco A F + dashboard). |
| Bug 4 — estorno de material lançava `recognized_amount` negativo | O REVERSAL publicava entrada negativa → violava `cost_management_entries_check` (`recognized>=0` e `allocated<=recognized`) → 422. | Estorno incrementa `reversed_amount` do lançamento **original** (migration 078); canônico, sem mudança de constraint global. | **RESOLVIDO + verificado** (Bloco A A). |

**Regra consolidada** (Npgsql 10.0.0, default moderno — sem flag legacy nem type-mapper global): row-DTOs = classes simples `{ get; init; }` (nunca record posicional); alias obrigatório de coluna underscore para prop PascalCase; `timestamptz`→`DateTimeOffset`, `date`→`DateOnly`.

**Nota de desenho (estorno de custo)**: o livro canônico usa a coluna `reversed_amount` do lançamento original em vez de mutar/excluir a entrada ou publicar valor negativo — preserva o histórico e a trilha de origem do custo. Refinamento aberto: os dashboards de custo realizado ainda somam `recognized_amount` bruto (ver §8).

---

### §13 Banco de dados / validação
- **Migrations incrementais**: `135_inspection_intent_recovery.sql` (schema **11.25.0** — aplicada em `agro360_homolog` e nos templates) e `136_quality_bridge_receipt_item_material_link.sql` (coluna `work_order_material_id` **nullable**, sem FK). `full.sql` carrega ambas; o gate de setup do E2E checa a presença da **135**.
- **Regressão Compras (12 etapas)** = `scripts/verify-procurement-journey-e2e.ps1` — **12/12 PASS** (run `procurement-journey-e2e-2381aa5ed86b450c97d7fc1c44372864`, schema 11.24.0): valida que as mudanças de código compartilhado (separadores de escopo, row-DTOs, coluna nova) **não quebram o Compras**; cobre SoD, idempotência/concorrência, quarentena→estoque só do aceito, conferência documental c/ exceção+SoD, previsão financeira OPEN, isolamento de tenant e **cancelamento parcial preservando recibos(2)/movimentos(95+10)/estoque(85)** (o assert que falhava no incremento anterior agora passa: ledger normaliza `PURCHASE_RECEIPT`→`RECEIPT` com `reference_type` preservado).
- **Gates de saída**: build Release **0/0**; architecture **187/187**; unit **390/390** (`sh_12ea70f20001QPDNdydMOhWVqc`).
- **Sem push/merge/publicação nesta entrega.** Nenhum commit novo foi criado agora: o trabalho de produto do Bloco A (Bug 1–4, sweep de row‑DTO, migration 136) e os docs desta seção seguem **não commitados** sobre `HEAD = c215af7`. Nota histórica: o §3 (recuperação de inspeções) já havia sido commitado como `c215af7` (marcelo, 2026‑10‑11 21:10); o **baseline original** do incremento permanece `dddc0bc` (2026‑10‑11 15:58).

### §14 Entrega / fechamento do incremento
| Bloco | Estado | Evidência |
| --- | --- | --- |
| §1 Baseline `main @ dddc0bc` | APROVADO | árvore limpa no início |
| §2 Bloco A (gate) — A·B·C·D·E·F | **APROVADO 59/59** | `agro-journey-e2e-ae861b9f…` |
| §3 Recuperação de inspeções | **APROVADO** (worker limpo) | `inspection-recovery-e2e-afc2e394…` |
| §4–§8 jornada agrícola (via Bloco A) | APROVADO (comportamental) | cruzado com Bloco A |
| §9 Central de Trabalho | Parcial (Compras 12/12) / não-executado (núcleo agrícola) | `procurement-journey-e2e-2381aa5e…` |
| §10 Governança SaaS | Parcial (módulo suspenso) / não-executado (contrato de papel + UI) | idem |
| §11 i18n / visual | i18n APROVADO / **visual BLOQUEADO** (trust do cert) | matriz i18n 809×4 |
| §12 Dívida técnica | RESOLVIDA (linhas acima) | gates verdes |
| §13 DB / validação | APROVADO (migrations + Compras 12/12 + gates) | idem |
| Cenários executados com falha | **nenhum** | — |

**Não executado nesta fatia** (documentado, não falha): tipos de ocorrência da Central do núcleo agrícola (§9); verificação de contrato de módulo no `SaveRoleAsync` + superfície de estado suspenso (§10); desconto de `reversed_amount` nos dashboards de custo (§8).

**Próxima fatia**: (1) homologação **visual do browser** — única ação manual pendente (confiar o certificado `C05DA1CA…` na loja `Root` via `Confiar-cert-agro360.bat`) e preencher a matriz 4 idiomas × 360/768/1440 px + zoom 200%; (2) **Central de Trabalho do núcleo agrícola** (tipos FIELD_BLOCKED / RESOURCE_UNASSIGNED / CUSTODY_PENDING / INSPECTION_PENDING / COST_PENDING); (3) **governança SaaS** (contrato de módulo no `SaveRoleAsync` + UI de estado suspenso).

> Escopo deste incremento: prova comportamental do **núcleo agrícola** + recuperação de inspeções + preservação do Compras. **Não declara o Agro360 inteiro homologado.**

---

## Incremento de 2026-10-12 (baseline `main @ 79b59c9`) — resultado líquido confiável, Central de Trabalho agrícola, interface de recuperação, governança de papéis + roadmap Pecuária P1–P10

Prompt COMPLEMENT integrado à entrega: roadmap de **Pecuária em 10 sprints (P1–P10)** registrado como programa com auditoria de entrada concluída — `docs/PECUARIA-PROGRAMA-P1-P10.md` (inventário exaustivo do módulo existente antes de qualquer mudança: 38 tabelas `agro360.livestock_*`, 3 serviços, ~60 rotas, seeds, testes, integrações; status e lacunas por domínio; regras transversais e portas por sprint; ordem de execução P1→P10). Os sprints executam somente após concluir esta fatia e o Bloco B.

### §3 Bloco A — Resultado líquido confiável da safra — APROVADO (E2E cenário G)
- Auditoria de todos os leitores de `cost_management_entries` seguida de netting canônico: **líquido = reconhecido − estornado** (sem saldo paralelo); memória de cálculo explícita na resposta (`definition`).
- Estorno parcial + total sem exceder o elegível: guarda cumulativa em quantidade (409 `agriculture.reversal_exceeds_remaining` com o saldo restante na mensagem) e guard atômico em valor no lançamento; estorno após líquido zero → 409 `agriculture.reversal_duplicate`.
- Eventos REVERSAL: um registro canônico por consumo (índice parcial `ux_field_material_single_reversal`); estornos parciais sucessivos **acumulam via upsert** (replay não duplica; cada ação permanece auditada por chave + auditoria).
- Exposição bruto/estornado/líquido em: detalhe, lista, overview, CSV (`ReconhecidoBruto;Estornado;ReconhecidoLiquido;Apropriado;PendenteLiquido`), fechamento (`pending_net`) e dashboard (`recognizedCost` bruto + `recognizedNetCost`).
- Provas da execução (api==db): parcial 40 → rev=500|net=750; replay sem duplicar; 61 rejeitado ("restam 60.000000"); overview api==db (3000|5542|750); dashboard gross=8542|net=5542 == db; closing pendente-líquido 750; estorno total → 1250|0 e COST_PENDING sai da central; estorno posterior rejeitado `reversal_duplicate`.

### §4 Central de Trabalho agrícola — APROVADO (E2E cenário H)
- Ocorrências projetadas de fontes reais: `CUSTODY_PENDING` (material entregue sem baixa de consumo/devolução/perda), `RESOURCE_UNASSIGNED` (ordem sem responsável), `INSPECTION_PENDING` (inclui dead-letter) e `COST_PENDING` (líquido pendente > apropriado) — cada uma com origem, causa, unidade, prazo, impacto e próxima ação; deep link `/Work`.
- Permissão/escopo antes da projeção: sem `work.read` → 403; operador escopo farmA → farmB 403 / farmA 200 (**X-Farm-Id não alarga autorização**); outro tenant invisível (vale não vê ocorrências de santa-clara).
- `view`/assignment geram eventos únicos (reenvio não duplica); **atribuição ≠ aprovação** (ordem permanece PLANNED); resolver a origem tira a ocorrência da central (revalidação de estado no backend, sem cache).

### §5 Interface de recuperação de inspeções — APROVADO (E2E H3 + suíte dedicada revalidada)
- Situação/causa/próxima ação visíveis na central (DEAD_LETTERED = prioridade HIGH + "exige re-disparo manual com justificativa"); estados **falta modelo × falha transitória × falha permanente** diferenciados no texto.
- Re-disparo exige motivo (422 `inspection.redispatch_reason_required`) e fica auditado (`INSPECTION_INTENT_REDISPATCH` com ator da identidade autenticada); não duplica intent; lista expõe `situation` + `canRedispatch` com api==db; origem que saiu do estado pendente é RESOLVED sem nova ação.
- Suíte dedicada `verify-inspection-recovery-e2e.ps1` revalidada neste incremento: S1–S7 todos passam (adoção de origens órfãs sem reenviar o comando, PENDING_MODEL com ciclo longo, backoff→dead-letter com motivo, redispatch zera+audita+processa, dois workers concorrentes → 1 intent/1 run, lease recuperado após restart).

### §6 Governança de papéis × contratação — APROVADO (prova API, E2E cenário I)
- `SaveRoleAsync` valida o contrato com a mesma fonte canônica do gate de runtime: perfil concedendo recurso de módulo **não contratado** → 403 "módulo não contratado: {permissão}"; módulo **suspenso** → 403 "módulo suspenso: {módulo}" (**mensagens distintas**, mesma classe de negação); módulo contratado → 201.
- Suspensão preserva perfis (nada destruído) e nega acesso efetivo no runtime (403); reativação libera gravação (201) e acesso (200).

### §12/§13 — Defeitos corrigidos nesta fatia (causa raiz, todos cobertos pelo E2E verde)
1. `SeasonCostService.Params` — expansão POCO do Dapper enviava NULLs **sem tipo** → Postgres 42P08 (`could not determine data type of parameter`) nos predicados `@P is null`; parâmetros agora com `DbType` explícito (list/overview/export.csv: 500 → 200).
2. Estorno parcial #2 batia no unique `(tenant_id, source_event_id) where event_type='REVERSAL'` (`persistence.unique_violation`) → upsert `ON CONFLICT ... DO UPDATE` acumulando quantidade na linha canônica do consumo.
3. `SaasService.SaveRoleAsync` — a classificação suspenso × não-contratado checava código de permissão contra conjunto de **módulos**; agora resolve os módulos de cada permissão via `Permissions.ModulesForPermission`.
4. Harness: operador do fixture sem `work.read` (concessão via `identity_role_permissions` + re-login — o JWT embute as permissões); aspas erradas em `jsonb_set(...,'to_jsonb('...'::text)')`; `next_attempt_at` NOT NULL no force de dead-letter (coluna nunca nula no processor; o claim só pega `PENDING`/`PENDING_MODEL`).

### §13 Banco de dados
- Migrations incrementais **137** (`cost_reversal_net_integrity`) e **138** (`receipt_item_work_order_material_fk`); `full.sql` atualizado; **gates de setup do E2E** verificam presença das versões {11.25.0, 137.0.0, 138.0.0} + idempotência real (upgrade sobre banco atualizado).
- **Upgrade documentado para `agro360_homolog`: 136 → 137 → 138** — pré-validação read-only em 2026-10-12: homolog @ 11.25.0 (mig 135), mig 136 ainda não aplicada; precondições limpas (`field_work_order_materials UNIQUE(tenant_id,id)` como alvo da FK, contadores de custódia íntegros, zero lançamentos com `reversed_amount > recognized_amount`, links nulos pós-136 → 138 aplica limpo). Ainda **não aplicado** na homolog.
- Instalação limpa e upgrade validados em cluster PG18 descartável (template derivado do `full.sql`).

### §14 Entrega / fechamento do incremento
| Item | Estado | Evidência |
| --- | --- | --- |
| Jornada A–I (Bloco A original + G/H/I novos) | **APROVADO 103 PASS / 0 FAIL / 0 BLOCKED** | `artifacts/agro-journey-e2e-b28d889875594792b694c610ecf5b21a` |
| Gates estáticos (build/arch/unit) | APROVADO | `GATES_DONE build=0 arch=0 unit=0` (187/187 · 390/390) |
| Regressão Compras (12 etapas) | APROVADO 12/12 | `artifacts/procurement-journey-e2e-668e9b3ef3234891992eb5e74c79072b` (2026-10-12 08:28 UTC, schema 11.24.0) |
| Regressão recuperação de inspeções (S1–S7) | APROVADO | `artifacts/inspection-recovery-e2e-683b5aba64304d9a87938d76a40cbff5` |
| Homologação visual do browser | **BLOQUEADO** (trust do cert `C05DA1CA…` no desktop) | `Confiar-cert-agro360.bat` — ação única do usuário |
| Programa Pecuária P1–P10 | PLANEJADO (auditoria de entrada concluída) | `docs/PECUARIA-PROGRAMA-P1-P10.md` |
| Cenários executados com falha | **nenhum** | — |

**Não executado nesta fatia** (documentado, não falha): §7–§8 comercialização de lote disponível → venda + resultado comercial; §9 lotes repetidos/safras recorrentes; sprints P1–P10.

**Próxima jornada**: **Bloco B (§7–§8)** — evolução da comercialização: lote disponível → venda (auditar os agregados comerciais existentes; evoluir, **sem criar módulo paralelo**) + resultado comercial com memória de cálculo e reconciliação; funda a base usada pelo sprint P9. Em seguida, o programa de Pecuária inicia em **P1 (rebanho/identificação)** com re-auditoria do estado vigente.

**Git**: baseline `main @ 79b59c9`; todo o trabalho desta entrega segue **não commitado** (sem push/merge/publicação).

> Escopo deste incremento: resultado líquido confiável + Central de Trabalho agrícola + interface de recuperação + governança de papéis, com regressões preservadas e programa de Pecuária planejado. **Não declara o Agro360 inteiro homologado.**

---

## Bloco B (incremento de 2026-10-12) — §7–§8 comercialização do lote da safra → venda + resultado comercial — **APROVADO (E2E cenário J)**

Escopo do prompt: evoluir o caminho comercial **canônico** (`sales_orders → fulfillment → dispatch → aceite formal`) **sem criar módulo paralelo de vendas**; política de preço explícita; recebível financeiro idempotente por aceite formal; resultado comercial com memória de cálculo e reconciliação **api==db**; novo cenário de jornada **J** sem quebrar A–I; registrar aprovado/falhou/bloqueado/não-executado.

### Implementação (apenas gaps; nenhum ledger/status comercial novo em paralelo)

- **Gap identificado na auditoria**: o ciclo financeiro da venda ficava aberto — a migration 126 fixou explicitamente que o SETTLE de programação de entrega é administrativo (sem escritura financeira/fiscal), e nenhum elo ligava aceite formal de entrega ao título a receber.
- **Migration 139** `fulfillment_acceptance_receivable_link` (version `139.0.0`, transacional, idempotente): `finance_receivables.fulfillment_attempt_id` UUID nulo por padrão (histórico dos outros caminhos inalterado) + FK **composta** `(tenant_id, fulfillment_attempt_id) → fulfillment_delivery_attempts(tenant_id,id)` (elo no mesmo tenant) + unique parcial `ux_finance_receivables_fulfillment_attempt` (`where fulfillment_attempt_id is not null`) — **no máximo UM recebível por tentativa**, idempotência garantida pelo banco mesmo fora do replay por chave+hash. Embutida no `full.sql`.
- **`LogisticsService.RecordDeliveryAttemptAsync`**: o recebível é emitido **somente no aceite formal** (tentativa com quantidade aceita > 0): `Σ quantidade aceita × unit_price` do item comercial, `Round(…, 2, AwayFromZero)`; status `OPEN` **nunca liquidado automaticamente** (simetria Compras: título OPEN no evento físico); sem conta contábil de receita ativa → 422 `sales.receivable_account_missing` (falha antes de escrever, transação inteira); auditoria `sales.receivable_emitted`; replay = chave idempotência + hash + unique parcial.
- **Teste de arquitetura evoluído** (`IntegratedEvolutionIntegrityTests.JourneyWritersNeverWriteFinancialOrFiscalTables`): `Commercial360Service` (arquivo inteiro) + fatia de dispatch do `LogisticsService` **não podem** tocar `finance_*/fiscal_*/saas_billing*`; a fatia de aceite **deve** insertar em `finance_receivables` com `fulfillment_attempt_id` (evolução trancada em teste — regressão se o elo desaparecer).
- **`HarvestService` (memória de cálculo comercial)**: SQL `GrossRevenue` = `Σ AcceptedQty × UnitPrice` sobre os itens de remessa da safra; três indicadores novos após `recognized-revenue`, com availability espelhando o da receita (vazio ≠ zero; pendências → `UNAVAILABLE` + explicação):
  - `commercial-revenue-gross` — Σ aceita × unit_price (mesma base do faturamento, deduzidos retornos destinados);
  - `commercial-revenue-returns` — `max(0, bruto − reconhecida)` (dedução por retornos);
  - `commercial-margin` — `reconhecida − Σ cost_allocations CONFIRMED à safra (confirmed_at ≤ corte)` — mesmo critério do closing `/api/finance/season-costs/closing`; custos reconhecidos **sem alocação** não entram na margem (critério explícito na `Definition`).

### Prova comportamental — cenário J da jornada (execução aprovada)

Execução 7 de 2026-10-12: **132 PASS / 0 FAIL / 0 BLOCKED** (A–I idênticos + J), evidência `artifacts/agro-journey-e2e-ca8b7b6ee7f64d69bcf73694fcae8cc4`:

- **Política de preço explícita**: venda sem política → 422 `price_policy_missing`; desconto além do teto → 422 `discount_exceeded`; pedido 30.000 un @ 1,10 = **33.000,00**.
- **Máquina de estados do pedido (lei de domínio existente, sem mudança)**: criação direta em estado operacional → 422; `DRAFT → UNDER_REVIEW`; aprovação por operador sem `commercial.*` → 403; admin → `APPROVED`.
- **Reserva**: invariante `available − reserved = 0` (reserva só incrementa `reserved`; o débito físico do `available` acontece no dispatch); replay concorrente → 1 reserva (itens|reservas = 1|1); mesma chave com outro conteúdo → 409.
- **Dispatch**: movimento `SALE` único no lote (mov|qty = 1|20000); replay sem duplicar; versão stale → 409.
- **Aceite × faturamento**: tentativa `FAILED` **não** fatura nada; aceite **parcial** (18.000 das 20.000 expedidas) → receivable **19.800,00** (`18.000 × 1,10`) `OPEN` com `fulfillment_attempt_id` ligando ao lote; remessa vira `RETURN_PENDING` (recusado = 2.000); replay do aceite não duplica o recebível (unique parcial).
- **Portas de permissão**: `/api/finance/receivables` 200 com `finance.read` / 403 sem.
- **Composição e resultado comercial (api==db)**: pedido 30.000 | cumprido 20.000 | status `FULFILLMENT` (parcial cumprido segue no estado operacional); indicadores da safra: bruto **19.800,00** | reconhecida **19.800,00** | returns **0** | margem **19.800,00 − apropriados(0)**; reconciliação com psql direto (net|bruto no banco == API); liquidação manual do título (204 `RECEIVED`, saldo 0) **não altera** a receita reconhecida nem o resultado (momentos distintos).

### Banco de dados

- Migration **139** incremental + `full.sql`; gates de setup do E2E agora verificam {11.25.0, 137.0.0, 138.0.0, **139.0.0**} + idempotência real.
- **Upgrade documentado para `agro360_homolog`: 136 → 137 → 138 → 139** (precondições limpas validadas read-only; homolog @ 11.25.0/135). Ainda **não aplicado**.

### Entrega / fechamento

| Item | Estado | Evidência |
| --- | --- | --- |
| Jornada A–J (Bloco A original + G/H/I + J comercial) | **APROVADO 132 PASS / 0 FAIL / 0 BLOCKED** | `artifacts/agro-journey-e2e-ca8b7b6ee7f64d69bcf73694fcae8cc4` |
| Gates estáticos (build/arch/unit) | APROVADO | `GATES_DONE build=0 arch=0 unit=0` (187/187 · 390/390) |
| Regressão Compras (12 etapas) | APROVADO 12/12 | `artifacts/procurement-journey-e2e-e9ccde19f270413a931e4b6869bd10c5` (2026-10-12 12:34 UTC) |
| Regressão recuperação de inspeções (S1–S7) | APROVADO | `artifacts/inspection-recovery-e2e-d9062554213249c9a4a73857f47bcd36` (auto-recuperação da API usada 1× ao vivo) |
| Homologação visual do browser | **BLOQUEADO** (trust do cert `C05DA1CA…` no desktop) | `Confiar-cert-agro360.bat` — ação única do usuário |
| Cenários executados com falha | **nenhum** (histórico de execuções 2–6: mortes de infra RAM absorvidas/diagnosticadas, ver EXECUTION-CHECKPOINT) | — |

**Não executado nesta fatia** (documentado, não falha): §9 lotes repetidos/safras recorrentes; Pecuária P1–P10; Governo360 G1–G10.

**Próxima jornada**: **Pecuária P1 (rebanho/identificação)** por `docs/PECUARIA-PROGRAMA-P1-P10.md` (auditoria de entrada concluída; re-auditar estado vigente antes de executar); depois P2→P10; depois Governo360 G1→G10.

> Escopo desta seção: Bloco B §7–§8 com prova E2E, regressões preservadas e limites documentados (ver `docs/LIMITES.md`). **Não declara o Agro360 inteiro homologado.**

## Pecuária P1 (incremento de 2026-10-12) — rebanho + identificação: ledger canônico de identificadores — **APROVADO (E2E 68/0/0)**

Base: `docs/PECUARIA-PROGRAMA-P1-P10.md` §4 (ordem P1 → P2 → …). Re-auditoria de entrada já concluída na sessão anterior; esta fatia implementou **apenas gaps** e rodou os gates por sprint (build/arch/unit + E2E próprio + regressões A–J/Compras/S1–S7 + visual).

### Implementação (apenas gaps; nenhum ledger/status paralelo criado)

- **Ledger canônico de identificadores** `livestock_identifier_history` consolidado como fonte única de verdade (tipos travados por CHECK em `INTERNAL`/`EAR_TAG`/`RFID`; unique parcial ≤ 1 linha vigente por (tenant, tipo, valor)). Writer estático `LivestockIdentifierLedger` (abrir/alterar/fechar dentro da transação do comando); a tabela auxiliar legada `livestock_animal_identifiers` ficou **frozen para escrita**. SISBOV fora do escopo do ledger (LIMITES #36).
- Os quatro fluxos passam pelo único writer: **registro** (EAR_TAG sempre + RFID quando presente, motivo `REGISTRATION`), **troca dedicada de brinco** (`POST .../tag`, motivo obrigatório → 422 `Reason.required`), **troca dedicada de RFID** (`POST .../rfid` — rota nova do P1, mesmo contrato) e **edição cadastral genérica** (`PUT .../animals/{id}`, motivo fixo `EDIT_CADASTRAL`). Conflitos amigáveis 409 `livestock.tag_not_unique`/`livestock.rfid_not_unique`; eventos `TAG_CHANGE`/`RFID_CHANGE` com anterior→atual+motivo; replay não duplica linha aberta.
- **Status canônico em todas as superfícies**: enum renomeado `Slaughtered→Discarded` (abate = espécie de descarte justificado); as quatro superfícies de leitura (lista v1, detalhe v1, GET 360, detail 360) unificadas: status 5 → `DISCARDED` (antes divergia para `SLAUGHTERED`), status 6 → `RESERVED` (antes divergia para `OBSERVATION`).
- **Defeitos de produto corrigidos na causa raiz** (encontrados no E2E do próprio P1): (1) materialização do `AnimalDto` na listagem por mapping direto de registro com ctor primário de 29 parâmetros — bug latente que nunca havia passado por E2E; alinhado ao padrão do detalhe (linha POCO + `ToDto()`), e a listagem passou a expor by-name; (2) a divergência de rótulos de status acima.
- UI no escopo mínimo: mapa de status 4 idiomas + chaves na central de i18n, pill DISCARDED e tabela de identificadores no modal de detalhe; o restante do `livestock.js` segue pt-BR (LIMITES #39).

### Prova comportamental — E2E do P1 (execução aprovada)

`scripts/verify-livestock-p1-e2e.ps1`: **68 PASS / 0 FAIL / 0 BLOCKED** em cluster PG18 descartável c/ template incl. 140 — `artifacts/livestock-p1-e2e-8e7549bcc87b4d8eaddbd8b861cdbf3b`. L1 backfill idempotente (reexecução 8→8 linhas) · L2 detail expõe o histórico + nascimento conhecido×estimado · L3 acesso (anônimo 401; leitor c/ apenas `livestock.read` + escopo FARM + módulo contratado: lê 200 / escreve 403) · L4 registro escreve no ledger (auxiliar = 0 linhas p/ animais E2E) + 409 case-insensitive · L5/L6 trocas dedicadas (204, encerra+abre, evento, replay estável, 422 sem motivo, 409 conflito) · L7 PUT `EDIT_CADASTRAL` + remoção de RFID fecha linha sem substituto · L8 descarte → status 5 lido como `DISCARDED` e reserva → `RESERVED` nas quatro superfícies (8/8) · L9 reconciliação api==db + snapshot final + dashboard 200. Histórico honesto das execuções anteriores à aprovada (causas raiz isoladas e corrigidas uma a uma, sem pulo silencioso): 403 `forbidden_unit_scope` no fixture do leitor (middleware exige ≥ 1 escopo em `identity_user_unit_scope(s)`), materialização da listagem (defeito latente de produto) e precedência de operadores no SQL do próprio harness — detalhes em EXECUTION-CHECKPOINT §P1.

### Banco de dados

- Migration **140** (`140_livestock_identifier_history_backfill`, version `140.0.0`): backfill do estado vigente como linhas abertas (EAR_TAG←tag, RFID←rfid quando presente, INTERNAL←internal_identifier quando presente) com ON CONFLICT contra o próprio unique parcial do ledger; **zero DDL**; embutida no `full.sql`; gate de setup da jornada agora valida {11.25.0, 137–140} + idempotência incluindo a 140; templates `pg-journey-template` e `pg-agro-template` rebuildidos com ela.
- **Upgrade documentado para `agro360_homolog`: 136 → 137 → 138 → 139 → 140** (pré-validação read-only 2026-10-12: ledger existente com 0 linhas, índice parcial presente, 4 animais-fonte → backfill esperado de 8 linhas sem colisão; homolog @ 11.25.0/135). Ainda **não aplicado**. Nota de versão descoberta na validação: migs ≤ 135 registram versões de release (mig 135 = `11.25.0`); migs 136+ usam número de arquivo (`136.0.0`…`140.0.0`) — `max(version)` léxico em `platform_schema_versions` é enganoso.

### Entrega / fechamento

| Item | Estado | Evidência |
| --- | --- | --- |
| E2E do P1 (L1–L9) | **APROVADO 68 PASS / 0 FAIL / 0 BLOCKED** | `artifacts/livestock-p1-e2e-8e7549bcc87b4d8eaddbd8b861cdbf3b` |
| Gates estáticos (build/arch/unit) | APROVADO | `GATES_DONE build=0 arch=0 unit=0` (187/187 · 390/390) |
| Regressão jornada A–J (template fixado a 140) | APROVADO 132/0/0 | `artifacts/agro-journey-e2e-0f99060fc6f445b59abce8af609add5f` |
| Regressão Compras (12 etapas) | APROVADO 12/12 | `artifacts/procurement-journey-e2e-4a875726daff4e7c9a806597004b0f96` |
| Regressão recuperação de inspeções (S1–S7) | APROVADO (todos os cenários) | `artifacts/inspection-recovery-e2e-c3add84e30174d00bfcf422d270a40b0` |
| Homologação visual do browser | **BLOQUEADO** (trust do cert TP no desktop) | `Confiar-cert-agro360.bat` — ação única do usuário (programa §3: registrado, nunca pulado) |
| Cenários executados com falha | **nenhum** (execuções prévias do E2E P1: causas raiz isoladas/corrigidas antes de revalidar; nada pulado em silêncio) | — |

**Não executado nesta fatia** (documentado, não falha): Pecuária P2–P10; Governo360 G1–G10; §9/§10/§14 do programa anterior (pendentes para depois).

**Próxima jornada**: **Pecuária P2 (entradas/saídas/movimentações)** por programa §4 — re-auditar o estado vigente antes de começar (o módulo evoluiu entre sprints; a idempotência de movimento hoje vive na coluna `notes`, frágil, e `reason_code` é livre sem catálogo). Depois de P10: Governo360 G1→G10.

> Escopo desta seção: P1 com prova E2E, regressões preservadas e limites documentados (ver `docs/LIMITES.md` §H). **Não declara o Agro360 inteiro homologado.**

## Pecuária P2 (incremento de 2026-10-13) — entradas/saídas/movimentações: idempotência formal + motivo validado no catálogo — **APROVADO (E2E 68/0/0)**

Base: `docs/PECUARIA-PROGRAMA-P1-P10.md` §4 (ordem P1 → P2 → …). Re-auditoria de estado vigente concluída antes da implementação (DDL de movimentos/católogo, serviço, rotas, UI); implementados **apenas gaps** confirmados na auditoria.

### Implementação (apenas gaps; nenhum ledger/estágio paralelo criado)

- **Idempotência formal do movimento individual** (migration 141): coluna dedicada `livestock_animal_movements.idempotency_key varchar(160)` + unique parcial `(tenant_id, idempotency_key) where not null` — o indivíduo herda exatamente o padrão que o grupo (`livestock_herd_movements`) e os demais domínios (Compras/Inspeção/Jornada J) já usavam. Replay no endpoint agora casa pela coluna (antes: query `where notes=@Key` sobre coluna livre que nunca recebia a chave). **Sem backfill**: `notes` sempre foi texto livre; linhas legadas permanecem com key NULL (índice parcial ignora NULLs — zero colisão por construção). Sem fingerprint de conteúdo: mesma chave = replay do primeiro efeito; guarda de concorrência real é o unique parcial (segundo inseridor → 409).
- **Validações amigáveis no endpoint de movimentação**: motivo deve existir + estar ativo no catálogo do tenant (`livestock_movement_reasons`) e direção compatível quando a operação expressa direção — 422 `reason_unknown`/`reason_inactive`/`reason_direction_mismatch`; conjunto fechado de kind por sujeito (animal ≠ grupo; `REVERSAL`/`RECONCILE` exclusivos de fluxo interno) — 422 `movement_kind_invalid`; responsável conferido como usuário do mesmo tenant (404). Regras em `LivestockRules.EnsureMovementKind`/`EnsureReasonDirection` com unit test. Fluxos internos mantêm seus códigos fixos (`SALE` na saída comercial por reserva, `RECONCILE` na conciliação).
- **Defeito de produto corrigido na causa raiz** (encontrado no E2E do próprio P2): 5 sítios de listagem (`ListMovementsAsync` ×2, `ListWeighingsAsync`, `PerformanceAsync`, `CostSummaryAsync`) vinculavam `@From`/`@To` como `DateOnly?` nulos sem tipo → Postgres 42P08 → **500 em qualquer chamada sem `from`/`to`** (incl. a aba de movimentações da SPA); corrigido com sentinels `DateOnly.MinValue`/`MaxValue`, guards SQL inalterados.
- UI inalterada por decisão (seleção mínima): o formulário de movimentações mantém os 5 motivos hardcoded (coerentes com o seed do catálogo) e não envia `idempotencyKey` — feature API-first; seletor dinâmico de catálogo segue backlog (mesmo padrão do i18n parcial do P1). Etapas "saída/trânsito/recebimento" continuam **fora do modelo**: TRANSFER permanece evento único e atômico (transporte/frete programados para o sprint P9).

### Prova comportamental — E2E do P2 (execução aprovada)

`scripts/verify-livestock-p2-e2e.ps1`: **68 PASS / 0 FAIL / 0 BLOCKED** em cluster PG18 descartável c/ template incl. 141 — `artifacts/livestock-p2-e2e-4b60a16e571546bcb5005e04f15cea27`. M1 idempotência da migration 141 (reexecução estável; linha legada do seed intacta com key NULL) · M2 jornada individual (registro D/E; TRANSFER interna c/ chave → 201 + trilha completa; replay mesmo id sem duplicar; **chave diferente com notas iguais NÃO deduplica** — prova do mecanismo novo; retroativo 409; LOCATION_CHANGE move piquete; EXIT+DEATH → status 4 lido `DEAD`) · M3 bateria de validações (tipo/motivo desconhecido/inativo/direção/SALE-sem-reserva/responsável 404/sujeito misto/ausente) · M4 grupo QUANTITY (TRANSFER 5 c/ chave → saldo 35 + troca de propriedade; replay estável sem segundo débito; EXIT → 34; saldo negativo 409; tipo inválido p/ grupo 422) · M5 acesso (anônimo 401; leitor lê 200 no escopo FARM e vê a linha do seed; header `X-Farm-ID` fora do escopo → 403 `forbidden_unit_scope`; escrita 403 sem mutar estado) · M6 reconciliação api==db + invariante uma-linha-por-chave nas duas tabelas + lista expõe a jornada + snapshot final + dashboard 200. Histórico honesto das 4 execuções prévias à aprovada (assertion do harness contando o evento ENTRY do registro; discovery do boolean `true`/`false` do Postgres 18; defeito 42P08 do produto; contrato de escopo via header `X-Farm-ID` vs query) — cada causa isolada antes de revalidar, em EXECUTION-CHECKPOINT §P2.

### Banco de dados

- Migration **141** (`141_livestock_animal_movement_idempotency`, version `141.0.0`): aditiva (1 coluna + unique index, `if not exists`), guard contra mig 068 ausente, versão `on conflict do nothing`; embutida no `full.sql` (bloco final, após o commit da 140); gate de setup da jornada agora valida {11.25.0, 137–141} = 6 versões + idempotência real incluindo a 141; templates `pg-journey-template` (P2/Compras/inspeção) e `pg-agro-template` (journey) rebuildidos com ela.
- **Upgrade documentado para `agro360_homolog`: 136 → 137 → 138 → 139 → 140 → 141** (pré-validação read-only da 141 em 2026-10-13: tabela presente pela mig 068 com 1 linha existente → key NULL pós-migration, zero colisão; coluna/índice/versão ausentes; tabela irmã já com o padrão; homolog @ 11.25.0/135). Ainda **não aplicado**.

### Entrega / fechamento

| Item | Estado | Evidência |
| --- | --- | --- |
| E2E do P2 (M1–M6) | **APROVADO 68 PASS / 0 FAIL / 0 BLOCKED** | `artifacts/livestock-p2-e2e-4b60a16e571546bcb5005e04f15cea27` |
| Gates estáticos (build/arch/unit) | APROVADO | `GATES_DONE build=0 arch=0 unit=0` (187/187 · 391/391) |
| Regressão jornada A–J (template rebuilt p/ 141) | APROVADO 132/0/0 | `artifacts/agro-journey-e2e-c440b7b761014793ad1da0456a3d237a` |
| Regressão Compras (12 etapas) | APROVADO 12/12 | `artifacts/procurement-journey-e2e-edb3b9d5f9864ff08ff0dccbb582b8ed` |
| Regressão recuperação de inspeções (S1–S7) | APROVADO (todos os cenários; auto-recuperação OOM 1× ao vivo) | `artifacts/inspection-recovery-e2e-659ffc526779449494537da3381fb504` |
| Homologação visual do browser | **BLOQUEADO** (trust do cert TP no desktop) | `Confiar-cert-agro360.bat` — ação única do usuário (programa §3: registrado, nunca pulado; acumula com o gate do P1) |
| Cenários executados com falha | **nenhum** nas execuções finais (execuções prévias do E2E P2: causas raiz isoladas/corrigidas antes de revalidar; nada pulado em silêncio) | histórico em EXECUTION-CHECKPOINT §P2 |

**Não executado nesta fatia** (documentado, não falha): Pecuária P3–P10 (P9 formaliza transporte/frete + dedup cross-path; P10 leiteira pode vir como incremento explícito); Governo360 G1–G10; §9/§10/§14 do programa anterior (pendentes para depois).

**Próxima jornada**: **Pecuária P3 (pesagens/desempenho)** por programa §4 — re-auditar o estado vigente antes de começar (o módulo evolui entre sprints; `RecordWeighingAsync`/`ListWeighingsAsync`/`PerformanceAsync` foram tocados na correção 42P08 do P2 — conferir o estado atual, inclusive o comportamento "zero intervalo ⇒ sem indicador"). Depois P4→P10; então Governo360 G1→G10.

> Escopo desta seção: P2 com prova E2E, regressões preservadas e limites documentados (ver `docs/LIMITES.md` §I). **Não declara o Agro360 inteiro homologado.**

## Pecuária P3 (incremento de 2026-10-13) — pesagens/desempenho: trilha de evidência, idempotência formal e desempenho determinístico — **APROVADO (E2E 74/0/0)**

Base: `docs/PECUARIA-PROGRAMA-P1-P10.md` §4 (ordem P1 → P2 → P3 → …). Re-auditoria de estado vigente concluída antes da implementação (DDL de pesagens/limites/eventos, serviços 360 e legado, rotas, UI, seeds, testes); implementados **apenas gaps** confirmados na auditoria — nenhum ledger/estágio paralelo criado.

### Implementação (apenas gaps)

- **Trilha de evidência + precisão de medição (migration 142):** coluna `measurement_precision varchar(20)` aditiva, sem backfill (seed permanece nulo — nada inventado); tetos método ≤ 40 / equipamento ≤ 80 / precisão ≤ 20 (422 `livestock.evidence_too_long`; somente espaços → null); projeção da lista expandida (`animalTag`, equipamento, responsável, lote de manejo, justificativa de revisão, `previousWeightKg`, `correctedFromId`, precisão, conversão) — NULL em dados antigos.
- **Idempotência formal da pesagem:** chave normalizada (`null : Trim()`) + pré-check pela coluna + persistida em insert/evento; dedup por chave apenas (payload diferente → mesmo id; sem fingerprint de conteúdo) — contrato do P2.
- **Validações amigáveis:** responsável = usuário do tenant no registro E na correção (404, sem efeito colateral); sujeitos coletivos validados na origem (grupo 404 / lote 404 / sem sujeito 422 / cabeças ≤ 0 422); conversão de unidade exige fator válido e explícito.
- **Desempenho determinístico e honesto:** agregações ordenadas `weighed_at, id` (timestamp igual ⇒ ordem de inserção, determinístico); intervalo zero ⇒ ganho diário NULL (eventos e indicador — nunca zero, nunca crash); exatamente 1 pesagem ⇒ sem linha (vazio ≠ zero).
- **Correção auditada:** nova linha com `corrected_from_id` + `previous_weight_kg` + evidência copiada; original marcada "substituída por correção auditada" (trilha preservada); 404 não cria linha; peso atual do animal refletido pela correção.
- **UI:** o form de pesagem agora envia equipamento/precisão/responsável (busca de pessoas); o caminho coletivo segue API-first (mesmo padrão do movimento no P2).

### Gaps aceitos (LIMITES §J, itens 52–67)

Dois escritores de pesagem coexistem (legado `DateOnly` × 360 timestamp — unificação é backlog de produto); dois calculadores de ganho diário coexistem (semânticas preservadas, não unificadas em silêncio); espécie fixa BOVINE na conferência de limites coletivas; pesagem 360 retrodatada atualiza `current_weight_kg` (último registrado ≠ último medido); as pesagens do seed não têm eventos WEIGHING (sem retrospectiva inventada); `responsible_id`/`herd_id`/`handling_lot_id` sem FK (validação de serviço, padrão do P2). Sequência de upgrade da homolog documentada até **142** (pré-validada read-only em 2026-10-13, ainda não aplicada).

### Aceite das portas (programa §3)

| Porta | Resultado | Evidência |
|---|---|---|
| Gates build/arch/unit | APROVADO (build=0 arch=0 unit=0; 187/187 · 392/392) | `rebuild-gates.ps1` em 2026-10-13 |
| E2E próprio do sprint (W0–W11) | APROVADO (74/0/0) | `artifacts/livestock-p3-e2e-6c12c9fb08824f618c2a2b16335bdd45` |
| Regressão jornada A–J | APROVADO (132/0/0; `pg-agro-template` reconstruído em 142; gate de setup {11.25.0,137–142}=7 + upgrade idempotente incluindo a 142) | `artifacts/agro-journey-e2e-146cc0ba740e4195aaaff5561a5a7513` |
| Regressão Compras (12 etapas) | APROVADO (12/12) | `artifacts/procurement-journey-e2e-e97166b4fd14425db341b4ed6ba97c2d` |
| Regressão recuperação de inspeções (S1–S7) | APROVADO (todos os cenários; a execução de 02:36 UTC caiu só em S7 por OOM ambiental — worker morto junto do host — e a reexecução com harness inalterado passou integralmente) | `artifacts/inspection-recovery-e2e-1e4f930ee40845c58bd8f24aca0fd975` |
| Homologação visual do browser | **BLOQUEADO** (trust do cert TP no desktop) | `Confiar-cert-agro360.bat` — ação única do usuário (programa §3: registrado, nunca pulado; acumula com P1 e P2) |
| Cenários executados com falha | **nenhum** nas execuções finais (execuções prévias do E2E P3: causas raiz de harness isoladas/corrigidas antes de revalidar — BOM/interpolação/esperanças de contagem NULL; nada pulado em silêncio) | histórico em EXECUTION-CHECKPOINT §P3 |

**Não executado nesta fatia** (documentado, não falha): Pecuária P4–P10; Governo360 G1–G10; §9/§10/§14 do programa anterior.

**Próxima jornada**: **Pecuária P4 (sanidade/agenda)** por programa §4 — re-auditar o estado vigente antes de começar (inventário: motor de agenda/calendário ausente; `livestock_health_protocols` sem seeds e sem consumidor funcional — conferir o estado real antes de qualquer mudança). Depois P5→P10; então Governo360 G1→G10.

> Escopo desta seção: P3 com prova E2E, regressões preservadas e limites documentados (ver `docs/LIMITES.md` §J). **Não declara o Agro360 inteiro homologado.**
