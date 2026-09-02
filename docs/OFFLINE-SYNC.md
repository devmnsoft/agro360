# Sincronização offline

A fila local `agro360-offline-v1` contém somente comandos operacionais, com `idempotencyKey`, `temporaryId`, tipo, payload e horário. Tokens não são armazenados no cache do service worker. `POST /api/mobile/sync` cria um lote e valida tenant, usuário e dispositivo. A restrição única `(tenant_id,user_id,idempotency_key)` impede duplicação; o mapeamento temporário/definitivo permite relacionar registros posteriores.

Falhas parciais retornam os itens processados e preservam os demais. Erros e conflitos têm tabelas próprias e jamais causam exclusão. Para tentar novamente, abra **Campo Mobile** e toque em **Sincronizar**. Em conflito, compare as versões cliente/servidor, escolha a resolução autorizada e reenvie; a decisão deve ser auditada. Catálogos são atualizados online e a última cópia autorizada é usada offline.
