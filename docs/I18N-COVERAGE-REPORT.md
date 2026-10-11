# Cobertura de textos e idiomas — incrementos 2026-10-09, 2026-10-10 e 2026-10-11

Inventário dos textos do shell web, das telas operacionais de Compras e das cinco telas agrícolas (jornada 2026-10-11) e cobertura nos quatro idiomas habilitados: `pt-BR` (fonte/fallback), `en-US`, `es-ES` e `fr-FR`. Este documento complementa `docs/MULTILANGUAGE.md`; o inventário dos demais módulos operacionais segue como backlog.

## Incremento 2026-10-11 — telas agrícolas (`/Agriculture`, `/field`, `/Harvest`, `/Costs`, `/Work`)

- Dicionário **compartilhado** em `src/Hosts/Agro360.Web/wwwroot/js/agro-i18n.js` (`AGRO_I18N`): **809 chaves em cada um dos quatro idiomas**, com **paridade total** verificada por gate estático (mesmo conjunto de chaves nos 4 dicionários, valores não vazios; checker `check_parity.js` reexecutado a cada edição do dicionário).
- Transformação dos 5 JS de página (`agriculture360.js`, `harvest.js`, `work.js`, `costs.js`, `field.js`): strings PT-BR hardcoded → `tr()` com marcadores `{0}` (nenhuma concatenação de texto traduzível em runtime); numeração/data/dinheiro pelos helpers `agroI18n.number/money/date/dateTime` (**moeda sempre BRL** em qualquer cultura). Todos passaram em `node --check`.
- Marcações nas 5 telas Razor: `data-i18n-ag` (texto), `data-i18n-ag-ph` (placeholder), `data-i18n-ag-aria` (aria-label); aplicador estático no próprio `agro-i18n.js` + listener do evento `agro360:culture` em cada página (re-render imediato na troca de idioma, incluindo o título do painel ativo).
- Contrato cultural herdado do shell: culturas suportadas = `["pt-BR","en-US","es-ES","fr-FR"]`; resolução preguiçosa (localStorage `agro360.culture` → fallback `pt-BR`), independente da ordem de carga dos scripts; `agroI18n.headers()` injeta `X-Culture` (e `X-Farm-Id` quando o escopo existe) em todo fetch próprio — `work.js` e `field.js`, que divergiam do `api()` canônico, agora enviam os headers (itens pendentes do mapa front do incremento).
- Testes de arquitetura atualizados sem perder a invariant: asserções que procuravam literais PT agora validam **chave no cliente + valor pt-BR no dicionário** (`tr("arc.noCompatibleOption")` e `tr("har.zeroBasePct")`).
- Escolhas estáveis documentadas (ver `docs/LIMITES.md`): códigos de valor do contrato permanecem brutos (`NORMAL`, `ALTA`, `APPROVED`, `LOSS`, …); nomes de arquivo de exportação não se localizam; dinheiro ausente exibe `cs.unavailable` (nunca R$ 0,00); textos livres (motivos/notas) chegam na língua da UI e persistem como enviados.
- Limite honesto: cobertura nível dicionário/estático + smoke server-side (5 páginas 200, dicionário servido, paridade 809×4); a **confirmação visual autenticada** (360/768/1440 px + zoom nativo 200% × 4 idiomas) depende da confiança única do certificado desktop no túnel do navegador — não declarar as telas como visualmente homologadas sem os screenshots correspondentes em `artifacts/browser-homologacao-<data>/`.

## Incremento 2026-10-10 — telas operacionais de Compras (`/Procurement`)

- Dicionário de módulo em `src/Hosts/Agro360.Web/wwwroot/js/procurement.js` (`PROC_I18N`): **511 chaves em cada um dos quatro idiomas** (`pt-BR` como fonte + `en-US`, `es-ES`, `fr-FR`), com **paridade total** verificada pelo gate estático do incremento (mesmo conjunto de chaves nos quatro dicionários, valores não vazios).
- Strings parametrizadas exclusivamente por `tr()` com marcadores `{0}` (nenhuma concatenação de texto traduzível em runtime); rótulos da **comparação de cotações** exibem lado a lado os dois valores do critério ("menor total da mercadoria" e total informado) nos quatro idiomas.
- Marcações `data-i18n*` nas telas de Compras (formulários, diálogos, estados) e CSS dedicado responsivo (`procurement.css`).
- Consequência para a lista "Excluído deste inventário": o item 7 ("telas dos módulos") fica **parcialmente coberto** — Compras opera com dicionário completo em nível estático; as demais telas de módulo continuam em `pt-BR` quando a cultura ativa é outra.
- Limite honesto: a cobertura de Compras é **nível de dicionário/estático + API E2E**; a confirmação visual autenticada da matriz (360/768/1440 px + zoom nativo 200% × 4 idiomas) estava em andamento no fecho desta seção e depende do ambiente dev recuperar desempenho — não declarar o módulo como visualmente homologado sem os screenshots correspondentes em `artifacts/browser-homologacao-<data>/`.

## Camada de tradução

- Idiomas habilitados somente pelo catálogo canônico `platform_languages.active` (migration `134_french_language_support.sql`, schema `11.24.0`). Nenhum hard-code de lista de culturas em código.
- Dicionários de shell em `src/Hosts/Agro360.Web/wwwroot/js/agro360.js` (`uiTranslations`): três dicionários completos (`en-US`, `es-ES`, `fr-FR`). O `pt-BR` é capturado em runtime do próprio markup via `data-i18nFallback*` (o texto original permanece no HTML como fonte).
- Marcação no `Pages/Shared/_Layout.cshtml`: `data-i18n` (texto), `data-i18n-placeholder`, `data-i18n-title` e `data-i18n-aria` (atributos `aria-label`).
- Aplicação: `applyUiText(culture)` chamado por `applyCulture`; persistência em `setCulture` → `PUT /api/v1/auth/preferences/language`, com toast de sucesso somente após confirmação do servidor (HTTP 200) e aviso persistente com "Tentar novamente" em falha.

## Resultado da inspeção (reprodutível)

Checker: `node artifacts/check-i18n-keys.cjs`

- 103 chaves por idioma em `en-US`, `es-ES` e `fr-FR` — **paridade total** (mesmo conjunto de chaves nos três dicionários).
- `_Layout.cshtml` usa **97 chaves distintas**; **0 sem tradução** em qualquer idioma (as 97 existem nos 3 dicionários com valores não vazios).
- 6 chaves presentes apenas no dicionário e não usadas pelo `_Layout`: `lg.hide`, `p.loading`, `p.active`, `p.activeEnd`, `p.none`, `p.fail` — usadas por handlers dinâmicos do `agro360.js` (estados de carregamento/paginação); esperado, sem lacuna.
- Seletor de idioma nativo em `_Layout.cshtml` (`#top-culture`): nomes nativos `Português`, `English`, `Español`, `Français` (o valor `value` continua sendo o código BCP-47). Os consoles SaaS/Ecosystem/Platform usam o mesmo mecanismo via `window.agro360SetCulture`.

## O que está coberto

Crome do shell: topbar (busca, seletor de idioma, tema, alertas), menu lateral, paleta de comandos, breadcrumb (`aria-label`), resumo da ajuda de tela (`summary`/`small`), formulário de login (rótulos, placeholders técnicos, ajuda curta por campo, botões de mostrar/ocultar), estados de sessão/logout e rótulos de ações compartilhados referenciados pelos dicionários acima.

## Excluído deste inventário (limitações documentadas)

Não são chaves do dicionário de shell e permanecem em `pt-BR` quando a cultura ativa é outra:

1. **Toasts dinâmicos** montados em JS (ex.: o próprio aviso "Idioma ativo apenas neste dispositivo") — strings fixas em código nesta rodada.
2. **Atributos `data-help`** — texto longo de ajuda contextual governado por `docs/CONTEXTUAL-HELP.md` e pelas tabelas `agro360.ui_*`; fora do escopo deste incremento.
3. **Cópias longas de ajuda contextual** (parágrafos da história do login, seções `ScreenHelp` próprias de cada página Razor).
4. **Parágrafos da ajuda genérica de tela** em `_Layout.cshtml` (bloco `Para que serve / Como trabalhar / Regras e resultado`) — traduzidos apenas `summary` e `small`; o corpo segue `pt-BR`.
5. **Títulos de página `@ViewData["Title"]`** — definidos em cada página Razor (servidor), fora do dicionário cliente.
6. **Lacuna conhecida específica**: `Pages/Ecosystem/Index.cshtml` (linha 7) usa chaves sem prefixo `helpTitle`/`help` que não existem no dicionário de shell → mantém `pt-BR` nos demais idiomas até virar chave canônica (ex.: `d.helpTitle`).
7. **Telas dos módulos**: apenas o crome compartilhado era traduzido; no incremento 2026-10-10 a tela de **Compras** ganhou dicionário completo (511 chaves × 4 idiomas) e no incremento 2026-10-11 as cinco telas agrícolas (`/Agriculture`, `/field`, `/Harvest`, `/Costs`, `/Work`) ganharam o dicionário compartilhado `agro-i18n.js` (809 chaves × 4 idiomas), ambos com paridade validada estaticamente. Os demais módulos operacionais (pecuária, logística, financeiro, etc.) seguem em `pt-BR` — item de backlog por módulo.

## Invariantes confirmados

- **Moeda nunca muda com o idioma**: fábricas `Intl` recriadas por cultura usam sempre `currency: "BRL"` (`agro360.js`, `Intl.NumberFormat(culture, { style: "currency", currency: "BRL", ... })`).
- **Idioma não altera** permissões, plano, tributação, UoM ou estado persistido: validado no E2E (Bloco L) — a lista de propriedades/fazendas do tenant é idêntica antes e depois da troca para `fr-FR` e do fallback `de-DE → pt-BR`.
- **Persistência só após confirmação do servidor**: `PUT` confirmado com 200 antes de qualquer afirmação de sucesso; re-login reativa a cultura salva; cultura desconhecida cai para `pt-BR` com o valor persistido.
