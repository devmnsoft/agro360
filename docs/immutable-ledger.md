# Ledger imutável

`IImmutableLedgerService` separa Application do adaptador PostgreSQL e permite um futuro adaptador blockchain. O evento armazena tenant, entidade, identificador, tipo, JSON, hash anterior/atual SHA-256, instante, usuário, assinatura lógica e status. Trigger rejeita `UPDATE`/`DELETE`; correções são eventos compensatórios.

O hash cobre hash anterior, payload e metadados canônicos. `POST /api/ledger/validate` recalcula toda a cadeia do tenant, informando sequência rompida. Criação/correção de lote, mistura, aprovação de beneficiamento, certificado, mudança de comissão e split são registrados. Ledger externo/blockchain permanece opcional, nunca requisito operacional.
