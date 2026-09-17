# Pacote AG-TPL-001 — aplicar no clone local

Gerado em 2026-09-17 a partir de `origin/main` (`5c3a3dea`) + histórico `8da8e287`.

## O que vem neste zip

| Arquivo no zip | Destino no repositório |
|---|---|
| `docs/execucao/DECISIONS.md` | sobrescreve o ponteiro quebrado do PR #141 |
| `src/Hosts/Agro360.Web/Pages/Shared/_Layout.cshtml` | shell com grupos do plano mestre |
| `src/Hosts/Agro360.Web/wwwroot/css/shell-evolution.css` | já existe em `main`; reaplicar se estiver igual |
| `src/Hosts/Agro360.Web/wwwroot/js/shell-evolution.js` | já existe em `main`; reaplicar se estiver igual |
| `PROMPT-PROXIMA-EXECUCAO.md` | texto para colar no agente na rodada seguinte |

## Como aplicar

No clone `C:\MNSOFT\agro360` (ou equivalente):

```powershell
git checkout main
git pull
git checkout -b feat/ag-tpl-001-shell
```

Copie os arquivos deste zip por cima dos caminhos iguais. Não use `git add .`.

Revise hunk a hunk:

```powershell
git diff -- docs/execucao/DECISIONS.md
git diff -- src/Hosts/Agro360.Web/Pages/Shared/_Layout.cshtml
git diff --check
node --check src/Hosts/Agro360.Web/wwwroot/js/shell-evolution.js
```

Preserve `appsettings.json`, secrets e untracked locais.

## O que este pacote não faz

- Não homologa inspeção 092, MFA, retorno logístico nem genealogia.
- Não autoriza no cliente: menu oculto ≠ permissão.
- Não reescreve `agro360.css` / `agro360.js`.
- Não declara módulo vendável.

## Commit sugerido

```
feat(shell): aplicar AG-TPL-001 e restaurar DECISIONS.md

Agrupa o menu na ordem do plano mestre, adiciona skip-link,
breadcrumb, faixa assistida e ScreenHelp. Restaura ADRs E0–E10
apagadas no merge do PR #141 e prefixa ADR-TPL-01.
```
