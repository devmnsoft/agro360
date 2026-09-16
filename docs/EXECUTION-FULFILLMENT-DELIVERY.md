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
