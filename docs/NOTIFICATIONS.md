# Notificações e outbox — Sprint 49

Notificações internas são persistidas para usuários ativos e funcionam sem provedor. Templates são localizados em `pt-BR`, `en-US` e `es-ES`, declaram variáveis permitidas e rejeitam HTML executável. Variável desconhecida falha com código de domínio claro.

E-mail, WhatsApp e webhook só recebem `SENT` após confirmação real. Sem configuração, a mensagem entra em `notification_outbox` como `PENDING_NOT_CONFIGURED`; tentativas, erro, provedor e horário são auditáveis. Payloads não devem conter credenciais, tokens, documentos completos ou outros segredos.
