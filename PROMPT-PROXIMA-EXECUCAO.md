Você trabalha exclusivamente no repositório https://github.com/devmnsoft/agro360 (MNSOFT Agro360). Não use outro produto.

Retome a partir de `docs/EXECUTION-CHECKPOINT.md` e `docs/TRACEABILITY-MATRIX-v0.2.0.md`. O último incremento registrado é **2026-10-09 — francês (fr-FR), i18n do shell e isolamento de fazenda em Compras**.

Baseline desta execução: branch `main`, HEAD inicial `307ed62`. A árvore possui alterações locais não-commitadas deste incremento (migration 134 + consolidado, `agro360.js`, `_Layout.cshtml`, consoles, scripts e docs) além de trabalho pré-existente preservado das rodadas anteriores. Não faça push, merge, publicação ou alteração em produção.

Antes de alterar arquivos:
1. reconfirme HEAD e `git status --short`;
2. leia `README.md`, `docs/EXECUTION-CHECKPOINT.md`, `docs/TRACEABILITY-MATRIX-v0.2.0.md`, `docs/MULTILANGUAGE.md`, `docs/I18N-COVERAGE-REPORT.md`, `docs/FEATURE-MATRIX-2026-10-09.md` e `docs/PROCUREMENT-SUPPLIES.md`;
3. trate `scripts/verify-language-farm-isolation-e2e.ps1` como referência de E2E descartável (cluster próprio, porta dinâmica); `scripts/verify-remaining-homologation.ps1` segue seguro na versão de cluster descartável.

O que ficou implementado e validado nesta rodada:
- migration `134_french_language_support.sql` (schema 11.24.0) idempotente + espelho na cauda de `database/agro360-postgres-full.sql`; fr-FR ativo no catálogo canônico `platform_languages`; CHECKs de cultura aceitando fr-FR nas 5 tabelas de templates persistidos;
- `scripts/verify-language-farm-isolation-e2e.ps1`: **58/58 PASS** em PostgreSQL 18 descartável (Bloco L idiomas + Bloco F isolamento fazenda A/B no tenant Santa Clara); log `artifacts/e2e-rerun6.log`, resumo `artifacts/language-farm-isolation-e2e-52a7016f103243f08bb4433a6f4ff502/SUMMARY.txt`;
- i18n do shell: dicionários en-US/es-ES/fr-FR com 103 chaves e paridade (auditoria `node artifacts/check-i18n-keys.cjs`), crome do `_Layout` marcado com `data-i18n*`, seletor em nomes nativos, persistência da preferência somente após confirmação do servidor com retry;
- gates estáticos: build Release 0/0; UnitTests 390/390; ArchitectureTests 186/186 (executar os testes via `dotnet exec <dll>`, pois `dotnet test` sai 5 neste host); format OK; rotas 945 OK; `node --check` dos JS alterados; `verify-offline-shell.mjs` PASS.

Próxima entrega prioritária:
1. homologação visual autenticada em navegador real: seletor de idioma, Compras e comparação de cotações em 360/768/1440 px e zoom nativo 200% — item bloqueado por falta de ferramenta de navegador na última sessão; registrar limites sem declarar homologação visual quando houver redirecionamento ou falta de sessão;
2. internacionalizar as cópias longas pendentes listadas em `docs/I18N-COVERAGE-REPORT.md`: começar pelos textos da tela de Compras, toasts dinâmicos, textos `data-help` e as chaves `helpTitle`/`help` sem prefixo do `Pages/Ecosystem/Index.cshtml` (converter para chaves canônicas prefixadas do dicionário de shell);
3. completar a jornada visual de cotações em `/Procurement` (abrir para requisição aprovada, propostas por item, decisão auditável, conversão idempotente para pedido) e homologar pela interface a sequência requisição → aprovação → cotação → pedido → aprovação → recebimento parcial → qualidade → estoque → previsão financeira;
4. manter os invariantes: idioma nunca altera moeda (BRL), tributação, UoM, permissões ou estado persistido; recusas canônicas e códigos estáveis.

Pendências conhecidas:
- `git diff --check` falha por linhas em branco no EOF em arquivos modificados antes desta execução; não misture limpeza desses arquivos com a próxima entrega sem revisar autoria.
- Não usar `ADD CONSTRAINT IF NOT EXISTS` em SQL (não é sintaxe válida em PostgreSQL); seguir o padrão da migration 134 (loop sobre `pg_constraint` + `if not exists … execute …`).
- Devolução ao fornecedor, mudança configurável do gatilho financeiro, Campo/Pecuária e integrações externas de IA/pagamento/fiscal continuam no backlog posterior.
