# Cobertura de textos e idiomas — incremento 2026-10-09

Inventário dos textos do shell web e cobertura nos quatro idiomas habilitados: `pt-BR` (fonte/fallback), `en-US`, `es-ES` e `fr-FR`. Este documento complementa `docs/MULTILANGUAGE.md`; não substitui o inventário de textos dos módulos operacionais.

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
7. **Telas dos módulos**: apenas o crome compartilhado foi traduzido; textos específicos de cada módulo (formulários operacionais, diálogos, mensagens inline) seguem em `pt-BR` — item de backlog por módulo.

## Invariantes confirmados

- **Moeda nunca muda com o idioma**: fábricas `Intl` recriadas por cultura usam sempre `currency: "BRL"` (`agro360.js`, `Intl.NumberFormat(culture, { style: "currency", currency: "BRL", ... })`).
- **Idioma não altera** permissões, plano, tributação, UoM ou estado persistido: validado no E2E (Bloco L) — a lista de propriedades/fazendas do tenant é idêntica antes e depois da troca para `fr-FR` e do fallback `de-DE → pt-BR`.
- **Persistência só após confirmação do servidor**: `PUT` confirmado com 200 antes de qualquer afirmação de sucesso; re-login reativa a cultura salva; cultura desconhecida cai para `pt-BR` com o valor persistido.
