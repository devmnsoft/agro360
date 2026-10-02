Você trabalha exclusivamente no repositório https://github.com/devmnsoft/agro360 (MNSOFT Agro360). Não use outro produto.

## Estado deixado pela execução atual (AG-OPS-MVP-004)

HEAD de referência: `b12f971fb44f5f299aad95ed2feac8662384d688` (baseado em `1ff01c909367145092f849ed9ec6ee4dcc55fdc5`).
As alterações foram compiladas com .NET 10 Release (0 erros, 0 avisos) e validadas integralmente em banco PostgreSQL 18 isolado.

### 1. Separação de Status e Matriz de Homologação

#### A. Implementação Presente
- **Contratos e Regras de Negócio**:
  - `CommercialRules.cs`: Validações de capacidade elegível de agendamento (100/30/50), proteção do cenário 50/20/15 (`dispatchedQuantity + openPreparationQuantity`), controle de concorrência otimista e idempotência por hash SHA-256.
  - `Commercial360Service.cs`: Projeção de preparação aberta segregada de expedições e reservas sem separação; persistência auditável de operações em `sales_delivery_schedule_operations`.
  - `LogisticsService.cs`: Correção da query de consolidação de entrega em `sales_delivery_schedule_items` (sem referenciar a coluna inexistente `updated_by`) e conversão de `occurred_at` para UTC em compatibilidade com Npgsql 9 / PostgreSQL 18.
- **Frontend Operacional e Formulários**:
  - `forms.js`: Inicialização idempotente via `window.agro360Forms.enhanceForm`, gerador de IDs únicos sem colisão, limpeza garantida de `aria-busy` e spans de erro em reset/falha/sucesso.
  - `logistics.js`: Função `showScheduleFulfillment` com suporte a múltiplas linhas de alocação de lotes por item, cálculo dinâmico de totais/saldos restantes, tratamento de produtos sem lote (desabilitação com justificativa clara), chave de armazenamento local isolada por tenant (`agro360_${tenantId}_...`).
  - `logistics.css`: Estilização nativa (sem Tailwind), hierarquia visual e responsividade comprovada de 360px a 1920px.
- **Esquema de Dados (Schema 11.7.0)**:
  - Migrations 116 e 117 consolidadas em `database/agro360-postgres-full.sql` e migration incremental `database/migrations/117_delivery_schedule_operation_identity.sql`.
  - RLS forçado em `sales_delivery_schedules`, `sales_delivery_schedule_items`, `sales_delivery_schedule_operations` e `fulfillment_shipments`.

#### B. Teste Unitário / Estático Executado
- **Arquitetura**: 169/169 aprovados (100%). Falha pré-existente de conexão resolvida em `appsettings.json` removendo a chave redundante `DefaultConnection` sem apagar arquivos do usuário.
- **Testes Unitários**: 265/265 aprovados (100%), incluindo cenário 100/30/50 e o novo teste unitário do cenário 50/20/15 em `DeliveryScheduleRulesTests.cs`.
- **Análise Estática Frontend**: `node --check` em `forms.js`, `logistics.js` e `commercial.js` (aprovado).
- **Gate de Expedição**: `node scripts/verify-dispatch-confirmation.mjs` (aprovado nos 4 cenários).

#### C. Integração Executada (PostgreSQL 18 Real + HTTP)
- Suite automatizada `scripts/verify-mvp-e2e.ps1` com 21 cenários executados em PostgreSQL 18 descartável:
  1. Instalação limpa do SQL consolidado.
  2. Reexecução idempotente do instalador.
  3. Upgrade incremental da base 11.6 até migration 117.
  4. Validação de RLS forçado nas 4 tabelas operacionais.
  5. Autenticação e login simultâneo de Tenant A (`santa-clara`) e Tenant B (`cooperativa-vale-verde`).
  6. Super Administrador com acesso ao Command-Center.
  7. Criação da programação de entrega com replay idempotente (mesmo hash) e detecção de conflito (hash divergente -> 409).
  8. Reprogramação com validação de capacidade (50 aceito, 60 rejeitado com 422).
  9. Rejeição de versão desatualizada (OCC -> 409).
  10. Cenário 50/20/15 comprovado no banco: redução para 25 rejeitada (mínimo 35) e para 40 aceita.
  11. Atendimento multi-lote com 2 lotes (20 + 10 = 30 sacas) e expedição física confirmada.
  12. Agregação da quantidade expedida consolidada no item da programação (30 sacas).
  13. Registro de movimentos físicos de saída no ledger de estoque para cada lote.
  14. Registro de tentativa de entrega com aceite parcial (15 sacas) e recusa por avaria (5 sacas).
  15. Consolidação de quantidade entregue no item da programação (15 sacas).
  16. Isolamento multi-tenant estrito: Tenant B bloqueado ao tentar consultar compromissos ou remessas do Tenant A.
  17. Renderização das páginas Comercial e Logística no Web Razor (HTTP 200).

#### D. Homologação no Navegador
- Testes automatizados de DOM/scripts e renderização HTTP das Razor Pages concluídos com sucesso. Homologação visual interativa final de clique no navegador disponível via subagente ou inspeção com os hosts ativos.

#### E. Falhas e Bloqueios
- **Zero falhas ativas** nos testes unitários, de arquitetura e de integração E2E.
- As credenciais de teste locais do usuário e `appsettings.Development.json` foram estritamente preservadas.

---

## Gate para o Próximo Ciclo (Compras / Produção)

Com a jornada Comercial → Programação → Atendimento Multi-lote → Expedição → Entrega 100% homologada com evidências no banco e HTTP, os próximos módulos do backlog são:

1. **Compras (Procurement)**:
   - Requisição → Cotação → Pedido de Compra → Recebimento Físico → Inspeção de Qualidade → Entrada em Estoque → Títulos Financeiros no Ledger.
2. **Produção (Industrial / Agroindústria)**:
   - Ordem de Produção → Consumo Canônico de Insumos (`ConsumeAsync`) com baixa de lote → Apontamento de Produção → Lote Produzido → Inspeção de Qualidade.
   - Diagnosticar e resolver a divergência potencial de `ConsumeAsync` com saldo/ledger de estoque antes de liberar piloto industrial.
3. **Agricultura / Pecuária**:
   - Apontamentos de campo, manejo de rebanho e apropriação de custos diretos e indiretos.
