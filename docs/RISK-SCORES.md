# Scores de risco

Cada score persiste dimensões operacional, financeira, logística, comercial, documental, fiscal, qualidade/compliance e rastreabilidade, além do score geral entre 0 e 100. A fórmula é uma média ponderada transparente: `sum(contribuição × peso) / sum(peso)`, limitada a 0–100. Cada fator registra descrição, observado, referência, peso, contribuição e módulo fonte; histórico e versão preservam a explicação no tempo.

Faixas padrão: baixo `0–24,99`, atenção `25–49,99`, alto `50–74,99`, crítico `75–100`. Configurações do tenant podem exigir aprovação ou bloqueio para lote, cliente, fornecedor ou documento fiscal crítico, mas a aplicação da consequência pertence ao workflow autorizado, nunca à view.
