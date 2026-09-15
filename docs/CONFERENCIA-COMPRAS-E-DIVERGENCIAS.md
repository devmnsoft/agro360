# Conferência de compras e divergências

## Escopo verificado em 15/09/2026

O diagnóstico partiu do código e do SQL, não dos títulos históricos de sprint. A solução usa .NET SDK 10.0.100, PostgreSQL, Dapper, transações com contexto de tenant e os hosts API, Web, Worker e Migrator. O ambiente desta execução não possui `dotnet` nem PostgreSQL; por isso compilação, migrations e cenários integrados continuam pendentes. A sintaxe do JavaScript e os gates estáticos foram executados.

## Mapa do ciclo

| Etapa | Situação encontrada | Implementação / dependência | Verificação aplicável |
|---|---|---|---|
| Necessidade e reposição | Parcial, com políticas, cálculo explicável e necessidade versionada | `/Replenishment`, `/api/replenishment`, `inventory_material_needs`; conversão unitária continua explícita e bloqueante | testes existentes de arquitetura; runtime PostgreSQL pendente |
| Requisição | Implementada; geração manual e por necessidade é persistida e idempotente na origem | `/Procurement`, `procurement_requisitions` e itens | repetir chave de confirmação; runtime pendente |
| Aprovação e cotação | Aprovação de pedido existe; cotação possui estrutura anterior, mas não ganhou jornada nesta entrega | `/api/procurement/orders/{id}/approve`; tabelas de quotation/approval | política/autorização no endpoint; E2E pendente |
| Pedido | Parcial: criação, total no backend, aprovação, eventos e saldos existem; edição crítica/versionamento comercial e cancelamento parcial não foram implementados | `ProcurementService`, `procurement_purchase_orders/items/events` | build/teste e banco pendentes |
| Recebimento e inspeção | Implementados anteriormente com idempotência, recebimento parcial, quarentena, aceite/rejeição e entrada única em estoque | endpoints `receipts` e `quality-decisions`; tabelas da migration 080 | suíte existente; execução integrada pendente |
| Estoque | Entrada ocorre somente após aceite quando há inspeção; unidade incompatível bloqueia | `inventory_apply_stock_movement` e links únicos | banco descartável pendente |
| Conferência financeira | Implementada nesta entrega como conferência operacional persistida; não é validação fiscal nem pagamento | `/api/procurement/invoice-matches`, documentos, linhas, matches e divergências da migration 084 | JS verificado; runtime pendente |
| Financeiro | Parcial e legado: recebimento cria previsão uma única vez por pedido | `finance_payables` e `procurement_order_financial_links`; conversão definitiva da previsão após conferência ainda pendente | integração PostgreSQL pendente |
| Central de pendências | Parcial: Central de Operações existente projeta aprovações, recebimentos e qualidade; a nova fila aparece na aba de conferências | `/Work` e `/Procurement#invoice-matches` | browser autenticado pendente |

## Fluxo da conferência entregue

1. O operador escolhe um pedido aprovado/não cancelado.
2. A API lista somente linhas de recebimento com quantidade aceita ainda não cobrada. Material sujeito à qualidade usa apenas a quantidade liberada; serviço usa o aceite registrado pelo recebimento.
3. O operador vincula explicitamente a linha da cobrança ao item e ao recebimento. Não há associação por nome.
4. A API recalcula itens e total em BRL com arredondamento monetário para duas casas (`AwayFromZero`), trava pedido/linhas e rejeita saldo excedido.
5. Documento, linhas, snapshot da tolerância e conferência são gravados na mesma transação. Número+série+fornecedor e chave de idempotência possuem constraints por tenant.
6. Diferenças de quantidade, preço, total e prazo são persistidas. Base zero apresenta valor absoluto e percentual ausente, sem divisão por zero.
7. Sem configuração, tolerâncias são zero (nunca aprovação irrestrita). Quando limites absoluto e percentual existem, ambos precisam ser respeitados — exceder qualquer um abre divergência.
8. Exceção exige `purchasing.approve`, justificativa e, quando configurado, pessoa diferente do autor. A regra aplicada permanece no snapshot histórico.
9. A decisão não valida fiscalmente o documento, não gera pagamento e não altera estoque. `MATCHED` significa somente conferência operacional.

## API e interface

- `GET/POST /api/procurement/invoice-matches` lista e registra conferências.
- `GET /api/procurement/invoice-matches/{id}` apresenta documento, linhas e divergências.
- `GET /api/procurement/orders/{id}/match-options` retorna saldos aceitos e ainda não cobrados.
- `POST /api/procurement/match-divergences/{id}/decision` resolve/rejeita com permissão e justificativa.
- `GET/PUT /api/procurement/match-tolerance` consulta/configura limites versionados da organização.
- `/Procurement`, aba **Conferência e divergências**, contém ajuda contextual, formulário e detalhe responsivo.

## Migração

Aplicar `database/migrations/084_procurement_invoice_matching.sql` pelo Migrator. A migration cria somente objetos incrementais no schema `agro360`, habilita RLS, preserva auditoria/autoria, usa exclusão lógica e adiciona constraints de unicidade e integridade. O instalador consolidado também contém a migration. Não altere migrations já aplicadas.

## Situação e continuidade

- **Implementada com verificação pendente:** persistência e API da conferência; tolerâncias; decisão de divergência; interface operacional; proteção de saldo, duplicidade, tenant e segregação.
- **Parcial:** detalhe completo/versionamento comercial do pedido; central unificada com a nova conferência; integração entre conferência aprovada e conversão da previsão financeira; cancelamento/substituição de documento via endpoint; telas de configuração de tolerância.
- **Não iniciada neste incremento:** edição/cancelamento parcial de pedido e devolução integrada da divergência.
- **Bloqueada por dependência externa no ambiente:** instalação limpa/upgrade PostgreSQL, geração de OpenAPI, inicialização dos hosts e teste em navegador desktop/mobile.

Próxima etapa sugerida: homologar a migration 084 em banco descartável, completar o versionamento comercial do pedido e converter (sem duplicar) a previsão financeira somente após uma política contábil explícita.
