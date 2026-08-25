# Sprint 10 — Rastreabilidade Amazônica

A v0.7.0 conecta origem, lote, mistura, beneficiamento, qualidade, certificado, expedição e venda sem restringir o catálogo a produtos amazônicos. Os seeds incluem açaí, tucupi, cacau, castanha-do-pará, mandioca/farinha e um produto genérico.

## Fluxo operacional

1. Cadastre o lote em `POST /api/traceability/lots`; propriedade ou produtor é obrigatório.
2. Registre transformação e composição em `/events`. Misturas exigem lote-fonte e quantidade.
3. Lance etapas em `POST /api/processing/compliance-events`. Regras por tenant/produto determinam tempo e temperatura mínimos.
4. Gere o certificado; a consulta pública por código retorna apenas origem e dados técnicos.
5. Verifique a cadeia em `POST /api/ledger/validate`.

Permissões separadas `traceability.read/write` preservam multitenancy. Todas as escritas usam Dapper parametrizado e transação com RLS.
