# Release notes — 2.0.0-rc.1

A Sprint 20 consolida o Agro360 como candidato a release nativo. A navegação deixou de apresentar atalhos meramente informativos: as áreas principais apontam para módulos operacionais, e resultados da busca global agora abrem somente rotas internas válidas. O instalador PostgreSQL completo ganhou identificação da RC e validação estrutural ampliada.

## Limitações de homologação

Testes que exercem persistência exigem um PostgreSQL descartável configurado por `AGRO360_TEST_CONNECTION_STRING`; Docker não é necessário. A aprovação para produção depende da execução integral de `docs/QA-CHECKLIST.md` no ambiente-alvo.
