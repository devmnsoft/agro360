# Orçamento e rentabilidade agro

Orçamentos suportam período anual, mensal, safra, propriedade, talhão, cultura, centro de custo e projeto. Nome, período, categoria e valor positivo são obrigatórios. Aprovação registra autor/data; alterações futuras exigem nova versão, preservada em `finance_budget_versions`. Estouro é a diferença positiva entre realizado e previsto e deve gerar alerta operacional.

Rentabilidade cruza receita, custo direto e indireto pelos vínculos reais de safra, propriedade, talhão, produto, lote, cliente, contrato, pedido, rota e máquina. Sem vínculo, o agregado é **Não alocado**. Margem = receita − custos; margem percentual é zero quando receita é zero. Métricas por hectare/unidade somente são exibidas quando seus denominadores reais existem.

Acesse `/Finance`, filtre o período e escolha **Orçamentos** ou **Rentabilidade**. Para aprovar, use perfil com `finance.budget.approve`; para exportar, use `finance.export`.
