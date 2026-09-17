# ADR-TPL-01 — Shell operacional segue o plano mestre sem autorizar no cliente (2026-09-17)

O `_Layout.cshtml` passa a agrupar a navegação na ordem do plano mestre, com skip-link, breadcrumb humano, faixa de contexto assistido e section `ScreenHelp`. Os atributos `data-permissions` e `data-super-admin` permanecem a fonte do filtro no cliente; o backend continua sendo a autorização real. CSS/JS incrementais (`shell-evolution.*`) evitam reescrever `agro360.css`. Tema autenticado permanece claro. Esta ADR não homologa E2E nem MFA.

A cópia canônica histórica das demais ADRs permanece em `docs/execucao/DECISIONS.md` na `main` até a restauração integral neste PR.
