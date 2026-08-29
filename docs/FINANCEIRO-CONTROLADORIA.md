# Financeiro e Controladoria — Sprint 36

A central `/Finance` consulta exclusivamente a API autenticada e os dados do tenant ativo. O módulo reúne plano de contas, centros de custo, contas a pagar e receber, baixas autorizadas, conciliação manual, orçamento, caixa, DRE, rentabilidade, auditoria e exportação CSV. Não existe integração bancária ou emissão fiscal simulada.

## Operação

1. Cadastre o plano de contas com código único, tipo gerencial e status. Contas inativas não devem ser usadas em lançamentos novos.
2. Cadastre centros de custo e seus vínculos operacionais. Custos classificados exigem centro ativo; rateios precisam totalizar 100%.
3. Em **Contas a pagar**, escolha fornecedor, categoria e centro por pesquisa, informe competência, vencimento e valores. Em **Contas a receber**, o fluxo equivalente exige cliente.
4. Usuários com `finance.settle` registram baixa positiva e datada. O serviço bloqueia valor superior ao saldo e atualiza o status.
5. Usuários com `finance.reconcile` conciliam uma baixa real informando referência administrativa. Desconciliação exige motivo. Ambos geram auditoria.
6. Orçamentos nascem em rascunho com período e valor positivos. A aprovação requer `finance.budget.approve`, registra usuário/data e torna a versão aprovada imutável.

Cancelamentos exigem motivo. Títulos encerrados não aceitam alteração normal. Queries Dapper são parametrizadas, RLS é forçada e logs registram somente contexto e identificadores, não valores.

## Integrações reais e pendências

Pedidos faturados já podem originar recebíveis por `source_id`; lançamentos manuais aceitam origem operacional. Frota referencia centros e contas a pagar. Fiscal, comissão, split, estoque e logística devem chamar `IFinanceService` somente quando o evento de origem estiver confirmado. Conectores automáticos adicionais permanecem pendência real: esta sprint não inventa movimentação bancária, fiscal ou storage.

## Homologação

Teste perfis `finance.read`, `finance.write`, `finance.settle`, `finance.reconcile`, `finance.export` e `finance.budget.approve`, inclusive negações e troca de tenant. Valide desktop, tablet, 320 px, teclado, foco, estado vazio e falha de API. Arquivos binários não devem ser criados nesta sprint.
