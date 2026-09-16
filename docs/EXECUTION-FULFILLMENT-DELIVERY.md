# Registro de execução — atendimento, expedição e entrega

## Diagnóstico

O repositório usa .NET 10, Razor Pages, JavaScript/CSS nativos, PostgreSQL e Dapper. A jornada persistida já reutilizava `sales_orders`, `sales_order_items`, clientes, produtos, depósitos, lotes, saldos e movimentos. A migration 071 e `LogisticsService` já separavam reserva, conferência, saída, tentativa de entrega e retorno; a migration 074 completava recebimento e decisão de qualidade da devolução. Comercial, Fiscal e Financeiro continuam agregados distintos: expedir não emite documento fiscal, não comprova entrega e não liquida recebível.

## Matriz de conclusão

| Funcionalidade | Estado encontrado | Arquivos | Dependência | Critério nesta entrega |
|---|---|---|---|---|
| Pedido e aprovação | Existente | `Commercial360Service.cs`, `sales_orders` | permissão comercial | somente pedido aprovado entra na fila |
| Reserva concorrente | Parcial | `LogisticsService.cs`, migration 071 | saldo/lote | trava por lote, limita pela linha e pelo saldo |
| Qualidade e validade | Incompleto | `inventory_stock_lots` | cadastro real do lote | valida na reserva e novamente na saída |
| Separação/conferência | Existente | `fulfillment_shipment_items` | reserva ativa | quantidades positivas e divergência explícita |
| Expedição parcial | Defeito de saldo | reserva e saldo de estoque | migration 088 | baixa somente conferido e libera a diferença |
| Idempotência/atomicidade | Existente | serviço e índices únicos | PostgreSQL | repetição não cria segundo movimento; uma transação abrange a saída |
| Entrega/recusa | Existente | tentativas de entrega | expedição despachada | aceito e recusado não superam o expedido |
| Devolução | Existente | migrations 071/074 | recebimento e qualidade | recusa não repõe saldo; retorno aguarda decisão |
| Rastreabilidade | Parcial | detalhe de fulfillment | vínculos persistidos | expõe somente pedido, lote, tentativas e retornos reais |
| Fiscal/financeiro | Integração pendente | módulos Fiscal/Finance | fluxos próprios | nenhum efeito fiscal ou financeiro é simulado |

## Correções e regras consolidadas

* A disponibilidade agora é o menor valor entre saldo livre do lote e saldo ainda autorizável da linha do pedido. Reservas concorrentes continuam serializadas pela trava transacional por tenant e lote.
* Somente lotes `APPROVED` e não vencidos podem ser reservados. A saída revalida as mesmas condições, impedindo que uma conferência antiga autorize lote bloqueado depois.
* A saída usa o tipo canônico `SALE`, aceito pelo ledger de estoque. O vínculo `FULFILLMENT_SHIPMENT` preserva a finalidade sem criar um tipo incompatível.
* Reservas parciais registram quantidades consumida e liberada. Todo o valor reservado sai de `reserved`, mas apenas o conferido reduz o físico, evitando saldo preso.
* O script completo e a migration incremental preservam reservas históricas consumidas por meio de backfill.

## Manual operacional

**Finalidade.** Acompanhar o pedido aprovado até a reconciliação da entrega sem confundir cada marco.

**Pré-requisitos.** Cliente e produto aptos, pedido aprovado, depósito com saldo, lote aprovado e válido, permissões comercial/logística e contexto correto do tenant.

1. Em **Comercial / Pedidos**, crie e aprove o pedido conforme a alçada.
2. Em **Expedição e Entrega / Pedidos a expedir**, filtre cliente, prazo e situação.
3. Selecione lote e quantidades para reserva, separação e conferência. Registre a divergência quando forem diferentes.
4. Antes de confirmar a saída, revise pedido, destino, produto, lote e efeito físico. O servidor revalida saldo e qualidade.
5. Registre tentativas como aceita, parcial, recusada ou sem sucesso. Saída não equivale a entrega.
6. Para recusa, registre retorno; depois confirme o recebimento físico e a decisão de qualidade. Não há reposição automática em estoque liberado.

**Resultado esperado.** Movimento físico único e auditável, reserva reconciliada, quantidades em trânsito/aceitas/recusadas preservadas e nenhuma baixa financeira ou fiscal implícita.

## Evidências e pendências

Há cobertura estática na classe existente `AuthenticationAndLivestockRegressionTests`, incluindo tipo do movimento, revalidação de qualidade/validade e liberação parcial. O ambiente desta execução não disponibilizou o SDK `dotnet` nem PostgreSQL isolado; portanto restore, build, suíte, Swagger, hosts, concorrência real, rollback induzido e percurso desktop/mobile permanecem pendentes de execução. A UI atual oferece painel e fila reais, mas formulários operacionais completos de reserva, conferência, entrega e retorno ainda dependem da evolução dos seletores/lookups; não foram simulados nesta entrega.

## Incremento 8.9 — pós-venda operacional e devoluções rastreáveis (2026-09-16)

### Situação encontrada

A expedição transacional, tentativa de entrega, autorização simples de retorno, recebimento parcial e decisão básica de qualidade já existiam. O retorno físico permanecia separado da disponibilidade, porém não havia um caso de pós-venda que ligasse cliente, pedido, expedição, pendências, solução e decisão comercial. A tela logística expunha a fila de pedidos, mas não oferecia listagem paginada nem acompanhamento cronológico dos casos.

### Entrega

- A migration `089_after_sales_returns.sql` adiciona ocorrências numeradas, histórico imutável, soluções, ajustes comerciais e substituições, sempre no schema `agro360`, com RLS, chaves tenant-scoped e idempotência no banco.
- A ocorrência valida no servidor cliente/pedido/expedição/item/lote, unidade e quantidade elegível sob lock. Casos sem produto não exigem lote ou quantidade. A listagem alerta ocorrências semelhantes, sem bloquear o registro.
- A máquina de estados admite aberta, análise, aguardando informação, solução proposta, aguardando execução, resolvida e cancelada. Cancelamento/reabertura exigem justificativa e produzem evento; resolução é recusada enquanto ação obrigatória, retorno ou execução financeira estiver pendente.
- Propor solução não a conclui. Prazos e responsáveis continuam nulos quando não informados. Ajustes nascem como proposta e a tela declara **execução financeira pendente**; nenhum crédito, reembolso ou autorização fiscal é fabricado.
- A autorização física existente ganhou vínculo opcional com ocorrência, local, responsável, condições e prazo. Recebimento parcial mantém o saldo consultável e material indisponível até decisão de qualidade. Destinações parciais continuam limitadas por lock e versão otimista.
- `/AfterSales` usa o template claro e responsivo, filtros preservados no formulário, paginação de servidor, ordenação estável, alerta acessível e visão concentrada de origem, quantidade, pendências, soluções, retornos, ajustes e histórico.

### API e manual rápido

`GET /api/logistics/trips/after-sales` lista com `page`, `pageSize`, `search`, `customerId`, `status`, `assigneeId`, `type`, `from` e `to`. `GET /after-sales/{id}` retorna a visão do caso. `POST /after-sales` abre de forma idempotente; `POST /{id}/transitions`, `/solutions` e `/adjustments` tratam o caso conforme permissões `after-sales.read`, `after-sales.write` e `after-sales.approve`.

Na operação, abra o caso a partir da expedição para não digitar identificadores técnicos; informe fato e descrição, vincule item/lote apenas quando aplicável, proponha a solução e acompanhe as pendências. Recebimento de devolução representa presença física, não saldo vendável. Somente marque resolvida após a execução real indicada na visão do caso.

### Dependências e evidências

A execução de crédito/reembolso e o documento fiscal dependem dos fluxos financeiros/fiscais reais e permanecem explicitamente pendentes; a decisão de pós-venda não os simula. Geração de complemento/substituição registra a intenção e deve ser vinculada a uma expedição criada pelo fluxo normal de reserva, separação, conferência e despacho. Neste ambiente, o SDK .NET e PostgreSQL não estavam disponíveis; restore, build, testes, aplicação em base descartável, Swagger, inicialização e inspeção visual desktop/mobile não puderam ser executados. A verificação realizada foi estática e não substitui esses gates.
