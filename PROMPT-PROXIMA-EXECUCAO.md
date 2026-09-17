Você trabalha exclusivamente no repositório https://github.com/devmnsoft/agro360 (MNSOFT Agro360). Checkout alinhado a origin/main. Não use SIGOV-PLUS nem outro produto.

## Objetivo desta execução (um lote só)

Entregar AG-TPL-001 e o reparo documental obrigatório. Não abrir inspeção, logística, safra, fiscal, marketplace, IA, EF ou segundo frontend nesta rodada.

1. Restaurar docs/execucao/DECISIONS.md com o conteúdo deste pacote (histórico 8da8e287 + ADR-TPL-01 no topo).
2. Aplicar src/Hosts/Agro360.Web/Pages/Shared/_Layout.cshtml deste pacote.
3. Garantir os links de ~/css/shell-evolution.css e ~/js/shell-evolution.js.
4. Preservar href, data-permissions, data-super-admin e data-public-menu.
5. Atualizar o topo de docs/EXECUTION-CHECKPOINT.md e o recorte ativo de docs/execucao/EXECUTION-PLAN.md.

## Leituras obrigatórias

README.md, docs/TEMPLATE-SHELL.md, docs/execucao/FEATURE-EVOLUTION-v1.md, docs/execucao/PROMPT-CONTINUIDADE-REFINADO.md, docs/BUSINESS-RULES.md, docs/DESIGN-SYSTEM.md, docs/execucao/ADR-TPL-01.md.

Registre branch, HEAD, remoto e git status --short. Preserve appsettings, secrets e untracked do usuário.

## Regras

Stack Razor + Dapper + PostgreSQL. Tenant só do contexto autenticado. Menu oculto ≠ autorização. SuperAdmin em cliente exige faixa visual e auditoria do ator real. Sem mock, botão morto, GUID na UI, git add . ou binário. Tema autenticado claro.

## Verificação

git show 8da8e287:docs/execucao/DECISIONS.md | wc -l
node --check src/Hosts/Agro360.Web/wwwroot/js/shell-evolution.js
git diff --check

Se existir dotnet: restore/build Release. Encerrar com PR descrevendo motivação, arquivos, testes e pendência seguinte (AG-Q-092-E2E).
