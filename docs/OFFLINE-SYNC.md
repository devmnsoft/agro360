# Sincronização offline controlada

O cliente mantém uma outbox no IndexedDB, sem senha ou token. Cada comando possui chave de idempotência, identificador temporário, tipo, payload, horário local, tentativas e estado. Ao sincronizar, `POST /api/mobile/sync` valida novamente usuário, tenant, permissão, tipo e payload dentro da transação do tenant.

Estados persistidos no servidor: `PENDING`, `SYNCING`, `SYNCED`, `FAILED`, `CONFLICT` e `CANCELLED`. Reenvios retornam `ALREADY_PROCESSED` sem materializar novamente a entidade. Erros permanecem na fila do aparelho; use **Tentar novamente**. Conflitos nunca usam “último a salvar ganha”: devem ser resolvidos por usuário autorizado, preservando versões cliente/servidor em `mobile.sync_conflicts`.

Operações suportadas nesta entrega: manejo rápido agrícola/pecuário/estoque/logística, ocorrência, check-in e evidência. Arquivos continuam sujeitos ao limite de 10 MB e à lista segura de MIME. O service worker não intercepta POST nem armazena respostas privadas.
