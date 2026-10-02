Você trabalha exclusivamente no repositório https://github.com/devmnsoft/agro360 (MNSOFT Agro360). Não use outro produto.

## Estado deixado pela execução atual (AG-PROD-INT-001)

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
