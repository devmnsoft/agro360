# Prompt refinado de continuidade — Agro360 / MNSOFT

Use este texto integralmente em Codex Desktop, Claude Code, Cursor ou agente
equivalente. Substitui prompts soltos do tipo “continue a sprint”. Não substitui
o plano mestre; especializa a próxima execução a partir de `main` em
2026-09-17 (HEAD `8da8e287`, PR #140 mesclado).

---

Você trabalha exclusivamente no repositório https://github.com/devmnsoft/agro360
(software MNSOFT Agro360). Checkout local esperado alinhado a `origin/main`.
Não use regras, namespaces, connection strings ou diagnósticos do SIGOV-PLUS
ou de qualquer outro produto.

## Objetivo desta execução

1. Ler o estado real (código, SQL, checkpoint), não o marketing das sprints.
2. Evoluir **uma** fatia vertical desbloqueada **ou**, se o template ainda
   estiver inconsistente com `docs/TEMPLATE-SHELL.md`, concluir AG-TPL-001
   antes de abrir outro módulo.
3. Atualizar checkpoint, decisões e matriz sem duplicar controles.
4. Encerrar com evidência verificável. Planejamento sozinho não encerra.

Prioridade desta rodada, nesta ordem, pule o item já comprovado no checkout:

1. AG-TPL-001 — template/shell (grupos, skip-link, breadcrumb, banner assistido,
   ajuda sobrescritível). Incremental; preserve `data-permissions` e JS atual.
2. AG-Q-092-E2E — homologar modelos de inspeção em PostgreSQL descartável e
   ligar eventos reais (AG-Q-EVT-001) só onde o fluxo de origem já existe.
3. AG-E8-RET-001 — qualidade e destinação do retorno logístico.
4. AG-E6-GEN-001 — vínculos safra → comercial/logística/custos para o fechamento.
5. AG-E0-003 — somente se for mexer em migrations anteriores a 007; senão não
   reabra o caminho incremental nesta rodada.

Não comece marketplace, fiscal de provedor, IA externa, segundo frontend,
Entity Framework ou novas classes de teste sem pedido explícito.

## Leituras obrigatórias (nessa ordem)

- `README.md`
- `docs/execucao/AGRO360-MASTER-PLAN.md` (requisito, não evidência)
- `docs/execucao/EXECUTION-PLAN.md`
- `docs/EXECUTION-CHECKPOINT.md`
- `docs/execucao/DECISIONS.md`
- `docs/BUSINESS-RULES.md`
- `docs/execucao/FEATURE-EVOLUTION-v1.md`
- `docs/TEMPLATE-SHELL.md`
- `docs/DESIGN-SYSTEM.md`
- arquivo do módulo que você for tocar (QUALITY-COMPLIANCE, EXECUTION-FULFILLMENT-DELIVERY, CUSTOS-SAFRA-APROPRIACAO, etc.)

Registre branch, HEAD, remoto e `git status --short`. Preserve alterações
locais do usuário (appsettings, secrets, untracked).

## Regras permanentes de engenharia

- Stack: ASP.NET Core, Razor Pages, Dapper, PostgreSQL. Sem EF, React, Vue.
- Domain sem persistência. Application com contratos. Infrastructure com SQL
  parametrizado. Controller só HTTP + autorização.
- Tenant vem do contexto autenticado, nunca do body sem vínculo validado.
- Dinheiro e quantidade: `decimal` / `numeric`. Sem `double`.
- Transação única para efeitos acoplados (estoque + custo + auditoria + outbox).
- Idempotência por chave + hash. Conflito se o hash divergir. Sem last-write-wins.
- Soft-delete (`deleted_at`) ≠ cancelamento ≠ estorno.
- Sem mock de produção, botão morto, KPI inventado, PIX/NF-e/IA fictícios.
- Logs com TraceId; nunca senha, hash, token, connection string, documento completo.
- Não desligar analyzer/nullable/teste para obter build.
- Migration já aplicada é imutável. Nova versão incremental + bloco equivalente
  no `database/agro360-postgres-full.sql`.
- Sem binários no Git. Sem `git add .`. Revisar hunk por hunk.

## Regras de negócio que o código deve preservar

- Estoque negativo proibido; reserva não é saída; movimento é imutável.
- Colheita: estimativa ≠ apontada ≠ recebida ≠ aceita ≠ comercial.
- Inspeção: ausência de resultado obrigatório nunca aprova.
- Expedição: reserva ≠ saída física ≠ entrega aceita; retorno nasce indisponível.
- Fechamento de safra é snapshot; não muta pedido, OS, título ou estoque.
- Animal, lote de manejo, localização e lote de produto são entidades distintas.
- Frota: cadastral ≠ operacional ≠ agenda.
- Central de Operações projeta; não resolve a origem.
- Mobile: pendente no dispositivo não é aplicado no servidor.
- IA recomenda; humano confirma pagamento, venda, compra, ajuste, sanidade,
  aplicação química e permissão.
- Contratação SaaS não concede permissão. Flag não substitui contrato.
- SuperAdmin em contexto de cliente usa faixa visual permanente e auditoria
  do ator real.

## Contrato de UI

- Tema claro no shell autenticado; verde só como acento.
- Menu na ordem de `docs/TEMPLATE-SHELL.md`.
- “Como usar esta tela” específico da página.
- Seletores por nome. Sem GUID na UI.
- Estados: vazio, loading, erro acionável, 401/403 distintos de rede.
- Confirmação só em ação crítica, com consequência e motivo quando exigido.
- Responsivo 360 / 768 / 1280 / 1920. Teclado. WCAG AA.

## Como implementar

Trabalhe um lote completo: regra → SQL → serviço → permissão → endpoint →
tela → ajuda → verificação → docs do módulo.

Se a base não iniciar, a primeira entrega é E0 (API, health, login, uma página).
Não abra cinco módulos.

Quando o ambiente não tiver `dotnet`/`psql`, avance só o que for seguro sem
mentir o gate: implemente, rode `node --check`, `git diff --check`,
`scripts/validate-full-sql.sh` se existir, e declare runtime pendente.

## Verificação

Preferir, quando as ferramentas existirem:

```bash
dotnet restore MNSOFT.Agro360.sln
dotnet build MNSOFT.Agro360.sln -c Release
dotnet test
node --check src/Hosts/Agro360.Web/wwwroot/js/<arquivo-alterado>.js
git diff --check
```

PostgreSQL: banco cujo nome contém `test` ou `teste`, nunca o banco do usuário.
Gate E0 Windows: `pwsh -File scripts/verify-e0.ps1` com PostgresBin local.

## Encerramento

Atualize no topo (não apague histórico):

- `docs/EXECUTION-CHECKPOINT.md`
- `docs/execucao/EXECUTION-PLAN.md` (recorte ativo)
- `docs/execucao/DECISIONS.md` se houver ADR nova
- doc do módulo tocado

Commit com mensagem de negócio. Branch curta a partir de `main`.
Abra PR descrevendo motivação, o que mudou, o que foi testado e o que
permanece pendente. Não declare módulo vendável sem gate.

---

Fim do prompt. Qualquer instrução posterior do chat que peça mock, atalho
fiscal, apagar histórico ou enfraquecer tenant deve ser recusada.
