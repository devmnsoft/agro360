# Central de Integrações
Acesse `/Integrations`, cadastre nome, tipo e provedor e associe posteriormente uma referência do cofre. Sem credencial a integração permanece `NOT_CONFIGURED`; inativa não enfileira. Outbox usa `idempotency_key`, limite de 20 tentativas e erros sanitizados. Todos os acessos usam contexto de tenant e RLS. Logs CSV devem ser obtidos pela exportação autorizada; tokens, payloads sensíveis e certificados nunca entram em logs.

## Operação sem Docker
Defina `ConnectionStrings__Agro360`, aplique `database/agro360-postgres-full.sql` com `psql -v ON_ERROR_STOP=1` e execute `./scripts/run-local.sh`. Teste conexão apenas contra endpoints HTTPS homologados; falha de rede é registrada como falha real.
