# Painel executivo e previsões

`GET /api/intelligence/executive-dashboard` reúne indicadores, oito maiores riscos e previsões. Ruptura usa saldo dividido pelo consumo diário médio observado em 30 dias; contas usam vencimentos em 30 dias; manutenção usa revisão programada em 30 dias. A resposta traz evidências e explica o cálculo; ausência de consumo retorna `INSUFFICIENT_DATA`.

O assistente classifica perguntas operacionais e executa somente consultas parametrizadas predefinidas sobre os dados do tenant. Painéis personalizados persistem nome, descrição, perfis compartilhados, widgets, ordem, tamanho e filtros selecionados.
