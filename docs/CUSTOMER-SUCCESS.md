# Customer Success — Sprint 47

A saúde é calculada exclusivamente com dados persistidos: adoção registrada em `customer_success_metrics`, chamados críticos abertos, contas vencidas e NPS. Regra `S47-1`: `40 + 60% da adoção - 15 por crítico (máx. 30) - 20 por cobrança vencida (máx. 40) - 10 se NPS < 7`, limitada a 0–100. A API devolve os insumos e uma recomendação explicável; não completa lacunas com dados inventados. Abaixo de 40 é risco alto, abaixo de 70 é médio.

Planos de ação, responsável e próxima reunião são auditáveis. Cobrança vencida tem prioridade na recomendação, seguida de baixa adoção e chamados críticos.
