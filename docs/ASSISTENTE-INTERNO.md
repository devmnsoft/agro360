# Agro360 Assistente

Sem provider externo, o assistente classifica perguntas permitidas e executa consultas Dapper predefinidas sobre contas, estoque, manutenção, lotes, rotas e alertas do tenant. A resposta informa intent/módulo, quantidade encontrada e ação sugerida. Resultado vazio é informado como “nenhum registro”; falta de permissão retorna autorização negada, não resultado vazio.

O assistente não altera dados e não executa ações destrutivas. Logs armazenam hash da consulta, intent, usuário, contagem, autorização e uso de provider — nunca texto sensível, token ou segredo.

Provider futuro fica desativado por padrão. Para habilitar, grave apenas uma referência de credencial em secret manager, endpoint HTTPS e consentimento explícito para dados sensíveis. Sem esses requisitos a constraint rejeita a configuração; habilitar provider não remove isolamento, permissão ou fontes.
