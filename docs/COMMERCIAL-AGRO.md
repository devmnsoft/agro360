# Comercial Agro 360

## Escopo entregue

O módulo `/Commercial` usa dados persistidos e isolados por `tenant_id`: clientes/prospects, contatos, representantes, oportunidades e histórico do pipeline, tabelas de preço, contratos, pedidos e itens, comissões, splits, metas, entregas e faturamento previsto. A API nunca aceita o total informado pelo navegador: o total de cada item e do pedido é calculado no backend.

A nomenclatura histórica canônica permanece `crm_customers`, `sales_opportunities`, `sales_contracts`, `sales_orders`, `sales_order_items` e `sales_commissions`; duplicar esses agregados com tabelas paralelas `commercial_*` quebraria a fonte única de verdade. `commercial_deliveries`, `commercial_billing_forecasts` e `commercial_events` são novos agregados porque não possuíam equivalentes. `commercial_proposals` já pertence ao CRM SaaS e não é reutilizada para propostas rurais.

## Regras efetivas

* CPF e CNPJ opcionais são validados pelos dígitos verificadores no domínio e nunca apenas por tamanho.
* Oportunidade requer valor positivo; perda requer motivo; data prevista no passado é rejeitada.
* Cliente inativo ou bloqueado não recebe pedido sem a permissão explícita de override.
* Pedido exige item, produto, quantidade e preço positivos. O desconto respeita o máximo e jamais torna o total negativo.
* Comissão só nasce para pedido `APPROVED`; cancelar o pedido cancela previsões ainda não pagas.
* Entrega confirmada exige pedido, responsável e data; ocorrência e cancelamento exigem descrição/motivo.
* Previsão de faturamento é expectativa, não recebimento. O status `SETTLED` somente poderá ser aplicado pela baixa financeira real.

## Pendências de integração

A reserva transacional de estoque e a criação automática de parcelas por condição de pagamento ainda dependem de um contrato de integração entre Comercial, Estoque e Financeiro. Até ele existir, o sistema não simula reserva, liquidação, pagamento de comissão nem comprovante local. Evidências usam URL/documento. As propostas rurais precisam de agregado próprio para não se confundirem com propostas de assinatura SaaS.
