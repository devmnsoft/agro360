
# AG-COM-OPS-001 — Contratos e pedidos

## Fluxo operacional

O fluxo canônico é **contrato → pedido explícito → reserva → expedição → entrega aceita → retorno → inspeção e destinação → conciliação gerencial**. Cada etapa conserva identidade, estado e auditoria próprios. Criar, aprovar ou ativar contrato não cria pedido, reserva, movimento de estoque nem recebível. O pedido continua sendo criado por ação explícita e seu total é recalculado no backend a partir da política vigente.

Contratos usam número legível `CTR-*`, tipos `INTERNAL_SALE`, `COOPERATIVE`, `EXPORT`, `RECURRING_SUPPLY` e `TRADING`, moeda, unidade, preço, vigência, condições e Incoterm para exportação. As transições são `DRAFT → UNDER_REVIEW → APPROVED → ACTIVE`, com suspensão, cumprimento, encerramento e cancelamento motivado. Cliente bloqueado não pode ter contrato aprovado/ativado. Versões preservam o snapshot inicial; evolução futura deve expor edição relevante exclusivamente como nova versão.

## Segurança e limites

Todas as consultas e escritas incluem `tenant_id`, executam no contexto autenticado/RLS e exigem permissões comerciais no endpoint. A chave de idempotência é única por tenant. Esta entrega não emite NF-e, não aciona gateway, não cria pedido automaticamente e não transforma previsão gerencial em documento fiscal.
