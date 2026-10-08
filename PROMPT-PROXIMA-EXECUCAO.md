Você trabalha exclusivamente no repositório https://github.com/devmnsoft/agro360 (MNSOFT Agro360). Não use outro produto.

Retome a partir de `docs/EXECUTION-CHECKPOINT.md` e `docs/TRACEABILITY-MATRIX-v0.2.0.md`. O último incremento registrado é **2026-10-08 — Compras com escopo operacional e evidência descartável**.

Baseline desta última execução: branch `main`, HEAD inicial `c6f05c83b507ef434b7adde3c3589a3c46150ed9`. A árvore possuía alterações locais pré-existentes em Estoque/Work/IA/SaaS/SQL; preserve-as. Não faça push, merge, publicação ou alteração em produção.

Antes de alterar arquivos:
1. reconfirme HEAD e `git status --short`;
2. leia `README.md`, `docs/EXECUTION-CHECKPOINT.md`, `docs/TRACEABILITY-MATRIX-v0.2.0.md`, `docs/PROCUREMENT-SUPPLIES.md` e os arquivos de Compras/SaaS envolvidos;
3. trate `scripts/verify-remaining-homologation.ps1` como seguro somente na versão que cria cluster descartável próprio.

O que ficou implementado e validado:
- build Release 0/0;
- `dotnet test -c Release --no-build`: 568 aprovados, 5 ignorados por falta de `AGRO360_TEST_CONNECTION_STRING`;
- `dotnet format --verify-no-changes --no-restore`: sucesso;
- `python tools/check-api-routes.py`: 935 rotas únicas;
- `node --check` de `procurement.js`: sucesso;
- `node scripts/verify-offline-shell.mjs`: PASS;
- `scripts/verify-remaining-homologation.ps1`: PASS completo em PostgreSQL descartável, evidência em `artifacts/remaining-homologation-e2e-b5b48586661a436c8fee660711ab4b87/SUMMARY.txt`.

Próxima entrega prioritária:
1. criar/homologar cenários específicos de Compras com duas fazendas no mesmo tenant: usuário fazenda A não lista, detalha, altera, recebe, exporta CSV ou enxerga indicadores da fazenda B, inclusive por ID e por `propertyId` divergente no corpo;
2. completar UI real de Cotações em `/Procurement`: abrir cotação para requisição aprovada, fornecedores participantes, propostas por item, comparação, decisão auditável e conversão para pedido;
3. validar pela interface autenticada a jornada requisição → aprovação → cotação → pedido → aprovação → recebimento parcial → qualidade → estoque → previsão financeira → conferência documental;
4. executar navegador real em 360/768/1440 px e zoom nativo 200%, registrando limites sem declarar homologação visual quando houver redirecionamento ou falta de sessão.

Pendências conhecidas:
- `git diff --check` falha por linhas em branco no EOF em arquivos modificados antes da última execução; não misture limpeza desses arquivos com a próxima entrega sem revisar autoria.
- Devolução ao fornecedor, mudança configurável do gatilho financeiro, Campo/Pecuária e integrações externas de IA/pagamento/fiscal continuam no backlog posterior.
