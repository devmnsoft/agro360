# Sprint 12 — Operação de Campo Mobile/Offline

A Sprint 12 entrega uma superfície PWA em `/field`, APIs autenticadas, serviço de aplicação por contrato, persistência PostgreSQL parametrizada com Dapper e regras puras no domínio. O fluxo cobre dashboard, registros rápidos (agricultura, pecuária, estoque e logística), evidências, GPS, QR e checklists.

## Fluxos

1. O bootstrap registra o dispositivo e devolve catálogos autorizados do tenant.
2. O formulário usa seletores por nome e valida no navegador; nunca solicita UUID.
3. Sem rede, o comando recebe chave idempotente e ID temporário e permanece no dispositivo.
4. O sync processa cada item isoladamente, reutiliza o resultado de uma chave já processada e mantém falhas pendentes.
5. Evidências guardam conteúdo, tipo, SHA-256 e vínculo; a API nunca revela armazenamento físico.
6. GPS negado não bloqueia o trabalho. Checklists exigem respostas e evidência nas perguntas de foto.

Endpoints estão sob `/api/mobile`, `/api/evidences`, `/api/geolocation/events`, `/api/qrcode` e `/api/checklists`. Todos, exceto resolução de QR público, exigem autenticação; RLS e o contexto da transação isolam tenants.
