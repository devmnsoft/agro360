# Homologação E2E — Ciclo 092+093 (AG-Q-092-E2E)

**Data de Execução:** 2026-09-19 / 2026-09-20  
**Repositório:** `https://github.com/devmnsoft/agro360` (MNSOFT Agro360)  
**Branch Ativa:** `feat/ag-q-092-e2e-homologacao`  
**HEAD Base:** `63985d9533073b0e6e48fec343d9be3532533dc4`  
**Próximo Recorte:** `AG-E8-RET-001` (Recebimento, conferência e qualidade definitiva de devolução/retorno)

---

## 1. Sumário Executivo

A homologação do ciclo de qualidade operacional 092 (Modelos e Inspeções) + 093 (Gatilhos Operacionais por Evento - `AG-Q-EVT-001`) foi executada em ambiente descartável isolado (cluster efêmero PostgreSQL com portas dinâmicas e hosts Kestrel da API e Web).

Todos os **15 cenários** da matriz de homologação foram executados e obtiveram veredito **PASS**, comprovando:
1. Integridade de DDL consolidado e incremental (9.2.0 e 9.3.0 presentes; RLS forçado em todas as tabelas).
2. Upgrade não destrutivo a partir de 9.2.0 sem perda de modelos prévios.
3. Imutabilidade absoluta de versões publicadas (edição rejeitada).
4. Desacoplamento operacional: o fato físico de recebimento (colheita/devolução) é gravado com sucesso, o lote permanece retido em `AWAITING_INSPECTION` e o intent de inspeção é disparado pós-commit.
5. Obrigatoriedade de critérios críticos: tentativa de conclusão sem preenchimento resulta em `HTTP 422 UnprocessableEntity`.
6. Reprovação em critério crítico resulta em `NON_CONFORMING`, retenção de lote e geração de restrição/não conformidade na Central CAPA.
7. Reinspeção gera nova run vinculada ao pai, preservando a run histórica intacta em `COMPLETED`.
8. Ambiguidade: modelos concorrentes de mesma precedência geram intent `AMBIGUOUS` sem criação arbitrária de run.
9. Ausência de modelo: intent `PENDING_MODEL` sem liberação indevida do estoque.
10. Devolução: recebimento gera intent de processo `RETURN` e status `AWAITING_QUALITY` sem destinação precipitada de lote.
11. Idempotência / Replay: reenvio da mesma transação de colheita recupera exatamente o mesmo `intent_id` e mesmo `run_id`.
12. Idempotência de Agenda Periódica: execuções com a mesma chave de geração no mesmo dia não duplicam runs abertas.
13. Autorização e Governança Multi-Tenant: leitor recebe `HTTP 403 Forbidden` ao tentar completar inspeção ou desbloquear lote sem `compliance.approve`.
14. Isolamento Multi-Tenant: Tenant B recebe lista vazia de intents e `HTTP 404 Not Found` ao tentar consultar run do Tenant A.
15. Acessibilidade, Responsividade e UI: Skip-link funcional com foco CSS, Breadcrumb humano, ScreenHelp operacional expansível, aba "Gatilhos por Evento" com status em português (`Iniciada`, `Sem modelo`, `Ambiguidade`) e rótulos amigáveis sem GUID exposto.

---

## 2. Correções de Produto Realizadas no Ciclo

Durante o ciclo de testes em banco PostgreSQL real, foram identificadas e corrigidas as seguintes falhas de produto no código de produção:

1. **Typo em Schema DDL (`database/agro360-postgres-full.sql` e `081_internal_material_requests.sql`):**
   - Corrigido identificador `platform.schema_versions` para `agro360.platform_schema_versions`.
2. **Materialização Dapper de Colunas Postgres Timestamptz:**
   - Classes `RunDetailRow`, `RunLockRow` em `InspectionService.cs` e `ReceiptSource`, `ReplayRow`, `InspectionReplay` em `HarvestService.cs` foram convertidas de records posicionais para classes com construtor padrão e properties com getters/setters, eliminando erro de deserialização do Dapper.
3. **Mapeamento de Data e Lista em Trigger (`OperationalInspectionTrigger.cs`):**
   - Criada classe `EventIntentListRow` para mapeamento resiliente de `created_at` em `InspectionEventIntentListItem`.
4. **Respostas HTTP Semânticas (`ExceptionHandlingMiddleware.cs`):**
   - Mapeado `InvalidOperationException => 422 UnprocessableEntity` e `ArgumentException => 400 BadRequest`, garantindo que regras de negócio de inspeção e encerramento não retornem erro 500 indevido.
5. **Dapper TypeHandler de Data (`DependencyInjection.cs`):**
   - Registrado `DateOnlyTypeHandler` invariante no Dapper para garantir compatibilidade com `DateOnly` do .NET 10 e `date` do PostgreSQL.
6. **Join e Bloqueio de Devoluções (`LogisticsService.cs`):**
   - Corrigido `res.product_id` inexistente na tabela de reservas para `coalesce(soi.product_id, lot.product_id)` com joins em itens de pedido e lotes de estoque.
   - Ajustado `for update` para `for update of r` suportando locks concorrentes em queries com `LEFT JOIN` no PostgreSQL.
   - Corrigido identificador de parâmetro `@ShipmentItem` para `@ShipmentItemId` casando com o parâmetro anônimo Dapper.

---

## 3. Matriz Completa dos 15 Cenários de Teste

| # | Cenário | Requisito / Escopo | Método / Endpoint | Resultado / Evidência Observada | Veredito |
|---|---|---|---|---|---|
| 1 | **Instalação Limpa Full SQL** | Execução de `agro360-postgres-full.sql` em cluster efêmero. | `psql -f agro360-postgres-full.sql` | Schema versions `9.2.0` e `9.3.0` presentes; RLS forçado em `quality_inspection_event_intents` e `quality_inspection_models`. | **PASS** |
| 2 | **Upgrade Incremental** | Base 9.2.0 atualizada com migration `093_quality_inspection_event_intents.sql`. | `psql -f 093_...sql` | Modelos pré-existentes intactos, tabela 093 criada, schema version `9.3.0` inserida. | **PASS** |
| 3 | **Criação e Imutabilidade de Modelo** | Criação de modelo `HARVEST_RECEIPT`, draft, critérios críticos, revisão e publicação. Tentativa de PUT pós-publicação. | `POST /api/inspections/models`<br>`POST .../publish`<br>`PUT .../versions/{id}` | Status HTTP 201 na criação e publicação; PUT na versão publicada recusado com HTTP 400. | **PASS** |
| 4 | **Gatilho de Colheita (HARVEST_RECEIPT)** | `ReceiveAsync` de colheita executado. Intent gerado com run automática e retenção em qualidade. | `POST /api/v1/harvest/receipts`<br>`GET /api/inspections/event-intents` | Receipt status `AWAITING_INSPECTION`; Intent status `STARTED`; Run gerada com status `IN_PROGRESS`. | **PASS** |
| 5 | **Recusa de Conclusão sem Obrigatório** | Conclusão de run sem resposta aos critérios obrigatórios. | `POST /api/inspections/runs/{id}/complete` | Recusado com HTTP 422 UnprocessableEntity; receipt segue retido em `AWAITING_INSPECTION`. | **PASS** |
| 6 | **Reprovação em Critério Crítico** | Respostas salvas com reprovação em item crítico; conclusão executada. | `PUT .../answers`<br>`POST .../complete` | Resultado global `NON_CONFORMING`; lote não é liberado para `AVAILABLE`; restrição registrada. | **PASS** |
| 7 | **Reinspeção** | Abertura de reinspeção vinculada à run reprovada. | `POST .../runs/{id}/reinspections` | HTTP 201; nova run gerada com `parent_run_id`; run original intacta com status `COMPLETED`. | **PASS** |
| 8 | **Empate de Modelos (Ambiguidade)** | Segundo modelo com mesma precedência ativo para o mesmo processo. | `POST /api/v1/harvest/receipts` | Intent registrado com status `AMBIGUOUS`; zero runs automáticas geradas. | **PASS** |
| 9 | **Sem Modelo Cadastrado** | Modelos inativados; recebimento de colheita efetuado. | `POST /api/v1/harvest/receipts` | Intent registrado com status `PENDING_MODEL`; zero runs; receipt mantido em `AWAITING_INSPECTION`. | **PASS** |
| 10 | **Gatilho de Devolução (RETURN)** | Recebimento de devolução de cliente executado. | `POST /api/logistics/trips/fulfillment/returns/{id}/receipts` | Devolução fica em `AWAITING_QUALITY`; intent gerado com processo `RETURN`; 0 destinações no DB. | **PASS** |
| 11 | **Idempotência e Replay** | Replay do mesmo recebimento de colheita com a mesma chave. | `POST /api/v1/harvest/receipts` | Mesmo receipt ID retornado; 1 único intent no banco com mesmo `run_id`. | **PASS** |
| 12 | **Geração Periódica Idempotente** | Agenda diária executada duas vezes com `schedule_generation_key`. | `POST /api/inspections/schedules/generate?asOf=...` | 1ª execução: 0 runs excedentes; 2ª execução: 0 runs duplicadas na mesma janela. | **PASS** |
| 13 | **Permissões e Governança** | Usuário leitor tenta completar run e desbloquear lote. | `POST .../complete`<br>`POST /api/compliance/lots/{id}/unblock` | Ambas as chamadas rejeitadas com HTTP 403 Forbidden. | **PASS** |
| 14 | **Isolamento Multi-Tenant** | Tenant B (Rio Verde) consulta intents e run criada pelo Tenant A (Santa Clara). | `GET /api/inspections/event-intents`<br>`GET /api/inspections/runs/{runA}` | Tenant B recebe lista vazia (Count: 0) e HTTP 404 ao consultar a run do Tenant A. | **PASS** |
| 15 | **Acessibilidade e Interface Web** | Inspeção visual e estrutural de `/Inspections`, skip-link, ScreenHelp, breadcrumb, aba de gatilhos e responsividade. | `GET /Inspections`<br>`node --check`<br>Inspeção DOM / CSS | Skip-link com foco CSS `:focus`; Breadcrumb `Qualidade / Modelos e inspeções`; ScreenHelp expansível; aba "Gatilhos por Evento" com status em português (`Iniciada`, `Sem modelo`, `Ambiguidade`); rótulo amigável; `@media(max-width:750px)` sem scroll horizontal em 360x640. | **PASS** |

---

## 4. Evidências do Cluster Efêmero (Logs e Chaves)

- **Diretório Efêmero:** `artifacts/q-092-e2e-0093b64e19b34dc9a66d3b7a4c49888f`
- **Portas Utilizadas:**
  - PostgreSQL Efêmero: `59192`
  - Agro360 API: `http://127.0.0.1:59193`
  - Agro360 Web: `http://127.0.0.1:59194`
- **Tenants de Teste:**
  - Tenant A: `Santa Clara` (`30000000-0000-0000-0000-000000000001`)
  - Tenant B: `Rio Verde` (`30000000-0000-0000-0000-000000000002`)
- **Amostra de Intents Reais Registrados:**
  1. `01a0ba82-e7ad-788f-830a-346a9271ebb7` | Processo: `HARVEST_RECEIPT` | Status: `STARTED` | Origem: `production_receipts` | RunId: `01a0ba82-e7d1-75e0-ad90-2080b5b96996`
  2. `01a0ba82-ef12-7dc0-9889-208b84ddeeff` | Processo: `HARVEST_RECEIPT` | Status: `AMBIGUOUS` | Origem: `production_receipts` | RunId: `null`
  3. `01a0ba82-efa1-7de2-bff7-e718799b229a` | Processo: `HARVEST_RECEIPT` | Status: `PENDING_MODEL` | Origem: `production_receipts` | RunId: `null`
  4. `01a0ba82-f2b3-7f5f-938c-545a7440b399` | Processo: `RETURN` | Status: `PENDING_MODEL` | Origem: `fulfillment_return_receipts` | RunId: `null`

---

## 5. Validação de Build e Testes

- `dotnet build -c Release`: **0 warning(s), 0 error(s)**
- `dotnet test tests/Agro360.UnitTests`: **27 de 27 aprovados (100% PASS)**
- `node --check src/Hosts/Agro360.Web/wwwroot/js/inspections.js`: **Aprovado (0 erros)**
- `git diff --check`: **Aprovado (sem conflitos)**

---

## 6. Próximo Recorte

Conforme a esteira de prioridades do produto:
- **`AG-E8-RET-001`**: Recebimento, conferência e qualidade definitiva de devolução/retorno.
