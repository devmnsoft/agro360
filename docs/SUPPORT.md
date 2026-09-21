# Suporte, Autoatendimento e Base de Conhecimento Agro360

A central de suporte e autoatendimento do Agro360 integra os clientes externos e parceiros operacionais às equipes internas de atendimento, com gestão de chamados orientada a SLA e base de conhecimento pública.

## Base de Conhecimento do Portal (`/Portal/Support`)

- **Artigos Públicos**: Disponibilizados em `GET /api/portal/support/articles`, filtrados por categoria (`PRIMEIRO_ACESSO`, `RASTREABILIDADE`, `COTACOES`, `DOCUMENTOS`, `INTEGRACOES`).
- **Segregação de Conteúdo**: Artigos de procedência interna, playbooks de engenharia ou manuais confidenciais possuem restrição de audiência e nunca são expostos na API pública do portal.

## Gestão de Chamados e Solicitações

- **Abertura de Chamados (`POST /api/portal/requests`)**:
  - Classificação por categoria, assunto, descrição detalhada e nível de prioridade inicial.
  - Anexos e evidências técnicas validados conforme extensão permitida (PDF, PNG, JPG, JPEG), tamanho máximo e sanitização MIME.
- **Timeline e Interações (`GET /api/portal/requests/{id}`)**:
  - Histórico cronológico auditado de todas as mensagens e mudanças de status (`portal_request_events` e `portal_messages`).
- **Cancelamento Motivado (`POST /api/portal/requests/{id}/cancel`)**:
  - O cancelamento de qualquer chamado por parte do usuário externo exige o preenchimento de justificativa obrigatória registrada para auditoria.

## Política de Comunicação e Outbox

- Notificações por e-mail ou SMS permanecem registradas na fila transacional (`communication_outbox`).
- Em ambientes sem provedor SMTP/SMS ativado, o status é registrado como `PENDING_NOT_CONFIGURED`. O sistema nunca simula entrega fictícia.
