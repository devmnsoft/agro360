# Agro360 Assistente

Sem provider externo, o assistente classifica perguntas permitidas e executa consultas Dapper predefinidas sobre contas, estoque, manutenção, lotes e viagens. A pergunta é validada novamente no servidor, e cada intenção revalida a permissão efetiva e o entitlement do módulo na transação da consulta. A resposta informa intent/módulo e ação sugerida. Contas são separadas entre vencidas e com vencimento nos próximos sete dias corridos; janelas de manutenção e viagens são explicitadas.

Consultas ligadas a uma unidade usam a fazenda ativa validada pelo middleware. Agregações sem vínculo de fazenda confiável (atualmente contas a pagar e viagens) exigem escopo efetivo de tenant completo. Usuários com escopo menor precisam selecionar uma fazenda autorizada quando a consulta permite esse filtro; o serviço nega a consulta quando não pode aplicar o escopo com segurança. Perguntas fora das intenções disponíveis são recusadas sem executar uma consulta genérica.

O assistente não altera dados e não executa ações destrutivas. Logs armazenam hash da consulta, intent, usuário, contagem, autorização e uso de provider — nunca texto sensível, token ou segredo.

As exportações executivas `indicators`, `snapshots`, `alerts`, `risks`, `recommendations` e `audit` consultam cada fonte correspondente, preservam o tenant, aplicam os filtros compatíveis com cada relatório e registram os filtros/paginação usados. Cabeçalho e colunas descrevem o conteúdo exportado; campos textuais passam pelo sanitizador CSV contra fórmulas.

Provider futuro fica desativado por padrão. Para habilitar, grave apenas uma referência de credencial em secret manager, endpoint HTTPS e consentimento explícito para dados sensíveis. Sem esses requisitos a constraint rejeita a configuração; habilitar provider não remove isolamento, permissão ou fontes.
