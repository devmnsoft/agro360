Você trabalha exclusivamente no repositório https://github.com/devmnsoft/agro360 (MNSOFT Agro360). Não use outro produto.

## Estado deixado pela execução atual (AG-GOV-001 + AG-EVO-INT-001) — 2026-10-05

Branch `main`. HEAD de referência: `de509bedf26306e669fcec9c076d153215f1e63c`. Tudo na árvore de trabalho, sem commit/push/merge; diffs locais do usuário preservados. `appsettings*.json` e `PostgreSqlConnectionConfiguration.cs` intactos; `database/releases/v0.*` intocados.

Gates verdes (re-executados após as edições desta rodada): build Release 0/0; UnitTests **350/350**; ArchitectureTests **182/182**; IntegrationTests **5/5** (base legada 11.16.0, PG 18); `dotnet format --verify-no-changes` exit 0; rotas **925** OK; `git diff --check` exit 0; validador do `full.sql` OK.

**Entregue (Bloco A — governance)**: fonte única dos grants (`EntitlementQueries.ModuleCodeSelect`, 6 cópias removidas, mapa grupo→módulo em `Permissions.ModulesForPermission`); bootstrap `tenant-administrator` ativo (nunca `platform.admin`); delegação validada no servidor (`CountUndelegableRolePermissionsAsync` + advisory locks ×3 + revalidação do aceite com `invitation_role_missing`/`invitation_authority_changed`/`plan_user_limit`); provisionamento com ator + `origin='PLAN'`; downgrade preserva adicionais e dados; writer `SetTenantModuleStatusAsync` + endpoint `PUT /api/platform/tenants/{id}/modules/{moduleCode}`; migration incremental 125 (schema 11.15.0) espelhada no consolidado (CHECKs com os 10 estados, `INACTIVE` + `origin`/`valid_until` nos entitlements, seeds do catálogo e snapshot retroativo).

**Entregue (Etapas 1–4 — evolução integrada do compromisso de entrega, com E2E verde completo em `artifacts/integrated-evolution-e2e-8be6749392f944f098f17e1c036b74cf`)**:
- Etapa 1: criação da programação lê reservas p/ validar saldo mas nunca escreve (assert negativo E2E: 0 reservas/movimentos, saldo intacto); reserva nasce só no atendimento multi-lote.
- Etapa 2: attempt FAILED leva remessa a IN_DELIVERY sem contar como entrega (compromisso DISPATCHED, entregue=0).
- Etapa 3: só `accepted_quantity` avança `delivered_quantity`; RECONCILED exige por item `accepted+returned+lost>=checked`; retorno da recusa move sacas de `refused` para `returned` (guard no serviço, sem migration — constraint 071 imutável; 409 `return.refused_exhausted`). Bugs latentes achados pelo E2E e corrigidos: joins inexistentes em ListReturns/ReturnDetail (500) e incompatibilidade aditiva com `fulfillment_shipment_items_check1` (422 no receipt).
- Etapa 4: migration 126 (schema 11.16.0, compatível PG 18) espelhada; liquidação administrativa idempotente (hash `{ScheduleId,ExpectedVersion,Reason}`, lock, replay 204/conflito 409), update guardado por versão, op row SETTLE + evento, zero escrita finance/fiscal; selagem (reschedule/cancel 409 `sales.schedule_settled_locked`; sobre liquidado 422 `settle_already_settled`; PLANNED 422 `settle_status_invalid`); custo ausente ≠ zero (`dispatchedTotalCost=null`/`available=false` sem movimentação).

**Matriz de baseline (resumo — tabela completa em `docs/EXECUTION-CHECKPOINT.md`, incluindo a nova matriz das Etapas 1–4 com 18 linhas aprovadas por E2E real)**: fixtures reais (Santa Clara `santa-clara`, Vale Verde `cooperativa-vale-verde`, Bloqueada `fazenda-bloqueada-teste`; duas fazendas `geo_farms` SC-SEDE-001/SC-RETIRO-001; perfis A–E, sendo C/D sem seed e criados sob demanda pelo B). Aprovados em nível de código/teste executado: fonte única (1), writer de bloqueio (4), provisionamento/origem (5), downgrade (6), delegação (7), locks ×3 (8), revalidação (9), limite (10), estados completos/migration (15), catálogo/snapshot (17), gates (21). **Falhados** por ausência real: transferência explícita do último admin (11) e vínculo usuário↔unidade (12). **Bloqueado**: escopo entre as duas fazendas (13, depende de 12). Nenhum mock substituiu execução real.

### Próximo ciclo — ordem obrigatória
1. **Remanescentes de homologação pendentes** (gate PG disponível): corrida concorrente pela última vaga (exatamente uma disputa consome; restante dos itens 8/10); login em tenant `SUSPENDED`/`BLOCKED` (restante do item 18); fixture TRIAL com janela passado/futuro (item 3) e módulos pendente/trial-expirado (restante do item 2); RLS com papel restrito (`set role agro360_app`) exercido sob HTTP nos fluxos novos de returns/liquidação. Registrar falhou/bloqueado/não executado com evidência real.
2. **Fundação mínima do vínculo usuário↔unidade** (item falho 12) usando as duas fazendas do Santa Clara, desbloqueando o item 13 (escopo entre fazendas do mesmo tenant).
3. **Navegador dos consoles e novas telas**: temas, 360/768/1440, zoom 200%, teclado — inclusive telas de returns e liquidação exercitadas por HTTP nesta rodada.
4. **CI do GitHub nesta árvore** (a última execução conhecida falhou em Format no baseline; gates locais estão verdes).
5. Manter invariantes nas próximas evoluções: programação≠reserva≠saída física; expedição≠prova de entrega; tentativa frustrada≠entrega; entrega≠liquidação; custo ausente≠zero; sem simular NF/crédito/pagamento.

---

## Estado anterior (AG-PROD-INT-001)

HEAD de referência: `35aaf6040664e7ed42e1b5cb0d63c60883478cef`.
As alterações foram compiladas com .NET 10 Release (0 erros, 0 avisos) e validadas integralmente em banco PostgreSQL 18 isolado via suítes automatizadas de integração e concorrência real.

### 1. Separação de Status e Matriz de Homologação

#### A. Implementação Presente
- **Contratos, Domínio e Regras de Produção Industrial**:
  - `IndustrialProductionRules.cs`: Validações de consumo direto e estorno de consumo (unidade compatível, quantidade estritamente positiva, motivo auditável obrigatório).
  - `IndustrialProductionContracts.cs`: DTOs canônicos `ProductionMaterialConsumptionCommand` (com `IdempotencyKey`), `ReverseMaterialConsumptionCommand` e assinatura de `ReverseConsumptionAsync` em `IIndustrialProductionService`.
  - `IndustrialProductionController.cs`: Endpoints REST `POST /api/production/consumptions` e `POST /api/production/consumptions/{id}/reverse`.
- **Serviço Industrial e Ledger de Estoque (`IndustrialProductionService.cs`)**:
  - `ConsumeAsync`:
    - Lock pessimista (`FOR UPDATE`) concorrente nas tabelas `inventory_stock_lots` e `inventory_stock_balances`.
    - Idempotência criptográfica via hash SHA-256 da requisição: reutilização da mesma chave com mesmo payload retorna o consumo existente sem deduplicações; alteração do payload rejeitada com HTTP 409 Conflict.
    - Proteção estrita de saldo descomprometido (`available - reserved >= qty`): rejeição imediata com HTTP 409 caso a reserva comprometa o saldo remanescente.
    - Dedução física atômica: decremento em `inventory_stock_lots` e no saldo disponível em `inventory_stock_balances`.
    - Lançamento no ledger de estoque: movimento canônico `CONSUMPTION` com referência `PRODUCTION_ORDER` e status `POSTED`.
  - `ReverseConsumptionAsync`:
    - Validação de status ativo (`POSTED`) e bloqueio de duplo estorno com HTTP 409 Conflict.
    - Reconstituição atômica de saldo no lote de origem e no armazém.
    - Lançamento compensatório no ledger: movimento `ADJUSTMENT_IN` com referência `PRODUCTION_CONSUMPTION_REVERSAL`.
    - Atualização do consumo para status `REVERSED`.
  - `RegisterOutputAsync` & Genealogia:
    - Registro de produtos acabados com qualidade `PENDING` (sem disponibilização prematura para venda) até deliberação de inspeção (`APPROVED`).
    - Vínculo rastreável entre produto acabado e insumos consumidos via `production_batch_traceability` com tipo canônico `'RAW_MATERIAL'`.
- **Interface e Formulários Operacionais (`forms.js`, `production.js`, `Production/Index.cshtml`)**:
  - `forms.js`: Funções modulares `enhanceForm` e `enhanceField` exportadas em `window.agro360Forms`.
  - `production.js`: Dialog acessível para Consumo Direto integrado a lookup contextual de lotes (`data-lookup="lots"`), dialog de estorno com justificativa obrigatória, e renderização das tabelas de insumos consumidos e lotes produzidos no detalhe da OP.
- **Esquema de Dados (Schema 11.8.0)**:
  - Migration incremental `database/migrations/118_production_material_consumption_integrity.sql` e consolidação em `database/agro360-postgres-full.sql`:
    - Colunas `idempotency_key` e `request_hash`, índice único `ux_prod_material_consumptions_idempotency`.
    - RLS forçado em `production_material_consumptions` e permissões DML para o papel da aplicação `agro360_app`.
    - Registro canônico da versão `11.8.0` em `platform_schema_versions`.

#### B. Teste Unitário / Estático Executado
- **Arquitetura**: 169/169 aprovados (100%).
- **Testes Unitários**: 270/270 aprovados (100%), incluindo 5 novos testes de regras de consumo e estorno em `IndustrialProductionRulesTests.cs`.
- **Análise Estática e Gates Frontend**:
  - `node scripts/verify-dispatch-confirmation.mjs` (PASS).
  - `node scripts/verify-offline-shell.mjs` (PASS).

#### C. Integração Executada (PostgreSQL 18 Real + HTTP)
- **Suíte E2E de Produção (`scripts/verify-production-e2e.ps1`)**: 15/15 cenários aprovados (Exit code 0):
  1. Instalação limpa do `agro360-postgres-full.sql` e versão 11.8.0 confirmada.
  2. Upgrade de base pré-existente via executável real do migrador (`Agro360.Migrator`) com preservação de registros.
  3. Validação de RLS com usuário sob papel restrito `agro360_app` (`rolsuper=false`, `rolbypassrls=false`): isolamento total do Tenant A, invisibilidade para Tenant B e 0 registros sem contexto.
  4. Boot do host API e autenticação JWT.
  5. Criação e aprovação imutável de formulação técnica.
  6. Ciclo de vida da OP (`PLANNED` -> `RELEASED` -> `IN_PRODUCTION`).
  7. Preparação de estoque (saldo 100 kg com 70 kg reservados).
  8. Consumo de 15 kg com dedução física atômica (lote 100 -> 85 kg) e movimento canônico `CONSUMPTION`.
  9. Replay idempotente (mesmo ID retornado sem duplicar dedução) e detecção de mutação (HTTP 409).
  10. Proteção de reserva: tentativa de consumir mais de 15 kg (disponível descomprometido = 15 kg) rejeitada com HTTP 409.
  11. Estorno integral com reconstituição de lote (85 -> 100 kg), movimento `ADJUSTMENT_IN` e bloqueio de duplo estorno (HTTP 409).
  12. Apontamento de produto acabado com inspeção `PENDING` (sem saldo comercial) e liberação com `APPROVED` gerando movimento `PRODUCTION`.
  13. Genealogia completa do lote consultada com rastreamento da matéria-prima.
  14. Isolamento multi-tenant da API (Tenant B recebe 403 Forbidden ao consultar OP do Tenant A).
- **Suíte E2E Comercial/Logística (`scripts/verify-mvp-e2e.ps1`)**: 21/21 cenários aprovados (Exit code 0).

#### D. Concorrência Real Executada
- **Cenário 11b de Concorrência**: Duas requisições HTTP paralelas disputando simultaneamente o mesmo saldo descomprometido via `HttpClient` e pessimistic locking no PostgreSQL:
  - Exatamente 1 requisição aprovada (HTTP 201) e exatamente 1 requisição rejeitada por saldo insuficiente (HTTP 409).
  - Zero deadlocks e integridade física absoluta (sem saldo negativo).

#### E. Homologação no Navegador
- Testes automatizados de DOM, scripts nativos e renderização HTTP das Razor Pages de Produção, Comercial e Logística concluídos com êxito (HTTP 200). Diálogos e formulários acessíveis sem dependência de Tailwind.

#### F. Falhas e Bloqueios
- **Zero falhas ativas**.
- Credenciais e configurações locais do usuário (`appsettings.Development.json`) estritamente preservadas.

---

## Gate para o Próximo Ciclo (Compras / Campo)

Com a jornada Comercial → Logística e o ciclo Produção → Consumo → Estoque → Qualidade 100% homologados com evidências de banco, migrador, papel restrito e concorrência, as próximas prioridades do roadmap são:

1. **Compras e Suprimentos (Procurement)**:
   - Requisição de Compra → Cotação de Preços → Pedido de Compra Aprovado → Recebimento Físico na Portaria/Armazém → Conferência Cega / Inspeção de Qualidade de Entrada → Disponibilização no Ledger de Estoque (`PURCHASE_RECEIPT`) → Títulos a Pagar no Financeiro.
2. **Ordens de Serviço de Campo e Agricultura (Field Operations)**:
   - Planejamento de Safra → Ordens de Aplicação/Plantio/Colheita → Apontamento de Insumos e Horas-Máquina → Apropriação de Custo Direto da Lavoura.
3. **Pecuária e Manejo Zootécnico (Livestock)**:
   - Movimentação de Pastos, Pesagens Periódicas e Sanidade/Vacinação com rastreabilidade de lote zootécnico.
