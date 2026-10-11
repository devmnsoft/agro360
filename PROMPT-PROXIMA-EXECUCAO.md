Você trabalha exclusivamente no repositório https://github.com/devmnsoft/agro360 (MNSOFT Agro360). Não use outro produto.

Retome a partir de `docs/EXECUTION-CHECKPOINT.md` e `docs/TRACEABILITY-MATRIX-v0.2.0.md`. O último incremento registrado é **2026-10-10 — jornada completa de Compras, i18n das telas operacionais de Compras e correções de estabilidade**.

Baseline desta execução: branch `main`, HEAD inicial `ff6028e`; durante a rodada o usuário commitou `bce9dca` ("novos auste") — frontend de Compras, versão inicial do E2E e parte dos serviços. A árvore atual possui o delta não-commitado desta execução (hardening do `scripts/verify-procurement-journey-e2e.ps1`, `ProcurementService.cs`, `QuotationService.cs`, `WorkManagementContracts.cs`, `WorkManagementService.cs` e docs). **Não faça push, merge, publicação ou alteração em produção.**

Antes de alterar arquivos:
1. reconfirme HEAD e `git status --short`;
2. leia `README.md`, `docs/EXECUTION-CHECKPOINT.md`, `docs/TRACEABILITY-MATRIX-v0.2.0.md`, `docs/MULTILANGUAGE.md`, `docs/I18N-COVERAGE-REPORT.md`, `docs/FEATURE-MATRIX-2026-10-09.md`, `docs/FEATURE-MATRIX-2026-10-10.md`, `docs/BROWSER-HOMOLOGATION-COMPRAS-2026-10-10.md` e `docs/PROCUREMENT-SUPPLIES.md`;
3. trate `scripts/verify-procurement-journey-e2e.ps1` como referência de E2E da jornada (cluster próprio descartável, porta dinâmica) e `scripts/verify-language-farm-isolation-e2e.ps1` como referência de idioma/isolamento.

O que ficou implementado e validado nesta rodada:
- Jornada completa de Compras **E2E verde 11/11** em cluster descartável schema `11.24.0`: requisição → aprovação → cotação → comparação → decisão → pedido → aprovação → recebimento parcial → qualidade → estoque → previsão financeira → conferência documental → devolução, com SoD, tolerâncias/divergências, delegação de escopo e isolamento entre tenants na mesma execução. Evidência: `artifacts/procurement-journey-e2e-4e570923d65a468fb12bb97f1ec0414a`.
- Nove defeitos de código pré-existente corrigidos e comprovados pelo E2E (CTE Work `recorded_at`; Dapper posicional timestamptz→DateTime; `catalog_code` nas cotações; binding por linha no `DecideAsync`; 409 de confirmação; apropriação de custo por `order.PropertyId`; mapeamento REJECTED/PARTIALLY_REJECTED em `quality_status` varchar(20); idempotência de custo por linha com `source_key`).
- Critério **"menor proposta" esclarecido e preservado**: menor total da mercadoria por item (`round(qty×unit,2)` − desconto); frete/impostos informativos; justificativa ≥3 caracteres acima do menor; comparação exibe os dois valores em 4 idiomas. Mudança para "menor custo completo" registrada, não aplicada.
- Versionamento de propostas (reapresentação invalida versões anteriores; decisão fotografa totais) e contrato 409 `agro360.quotation.confirmation_required` sem `confirmLowestPrice`.
- Gates estáticos verdes: build Release 0/0; UnitTests 390/390; ArchitectureTests 186/186; format OK; rotas 946 OK; `node --check`; paridade i18n shell (103×3) e módulo Compras (511×4).
- **Zero novas migrações** neste incremento (134 / `11.24.0` segue a última). A dev DB do usuário foi migrada de `9.8.0` para `11.24.0` de forma incremental (099→134) com backup prévio em `Temp\opencode\devdb-backup-pre-migrate.sql`.
- Credenciais dev decodificadas do seed: `admin@santaclara.agro360.local` / `Agro360Cliente!!` e `operador.santaclara@agro360.local` / `Agro360Operador!` (tenant slug `santa-clara`) — validar empiricamente no login.

⚠️ Estado do ambiente dev (DESKTOP-CK86BSC) no fecho desta rodada:
- Máquina degradada: RAM física livre ~540 MB de 16 GB (Docker Desktop + vmmemWSL + IDEs/Chrome); todos os processos .NET Core nascidos desde ~11:00 sobem **extremamente devagar** (ex.: `pwsh -NoProfile -Command echo` levou 20 min); BSOD 0x139 processado no WER pela manhã, timeouts DCOM recorrentes, Malwarebytes em histórico de esgotamento de memória virtual.
- Shell foreground do harness intermitente; **preferir jobs de background** com saída redirecionada para arquivos em `Temp\opencode\`; probes HTTP via Code Mode `fetch` simples (sem `AbortSignal` nesse runtime).
- Hosts lançados por **tarefas agendadas** (jobs de background do agente morrem em restart do servidor e levam os hosts junto — diagnóstico da geração v4): `schtasks /Create /TN "Agro360HostApi5" /TR "C:\Users\NCELL-DEV-020\AppData\Local\Temp\opencode\launch-api5.bat" /SC ONCE /ST 00:00 /F` + `/Run` (idem Web/Poller). API `https://localhost:7081;http://0.0.0.0:8081`; Web `https://localhost:7080;http://0.0.0.0:7080`; CORS do API via env inclui `http://localhost:7080`. Logs em `Temp\opencode\hosts\api5.out.log`/`web5.out.log`; poller `READY6.txt` (BOTH-UP). Se as portas não aparecerem em tempo razoável, **peça ao usuário para reiniciar a máquina ou fechar VS/Malwarebytes/Ollama/chrome extras** antes de relançar; não refaça build sem necessidade (o binário Release está atual). O tenant `santa-clara` está vazio em Compras — criar fornecedores/catálogo/requisições pela UI durante a homologação.
- Não usar `dotnet test` (sai 5 neste host): unidades via `dotnet run --project tests\Agro360.UnitTests -c Release --no-build`.

Próxima entrega prioritária:
1. **Homologação visual autenticada no navegador real** da jornada de Compras (fluxo SoD: criar como operador, aprovar como admin) na matriz 4 idiomas (pt-BR/es-ES/en-US/fr-FR) × 360/768/1440 px + zoom nativo 200%: formulários preenchidos, erros, comparação de cotações, confirmações (incluindo o 409) e estado persistido; console e rede sem erros; screenshots em `artifacts/browser-homologacao-<data>/`. Registrar limites honestos sem declarar homologação completa quando algo ficar pendente.
2. Finalizar a documentação com o resultado visual: `docs/EXECUTION-CHECKPOINT.md` (separar resultados em aprovado/falhou/bloqueado/não executado), `docs/I18N-COVERAGE-REPORT.md`, `docs/TRACEABILITY-MATRIX-v0.2.0.md` e este prompt.
3. Carry-over já documentado de `CancelOrderAsync` (seleciona/incrementa coluna `version` inexistente no DDL): aplicar Option A (remover `version` do select/update, manter guard de status, ignorar `x.Version`, documentar lock otimista opcional para migration 135+).
4. Depois que Compras estabilizar (API + visual), iniciar a jornada Agro (plano → safrar → colheita → venda). Campo/Pecuária permanecem backlog explícito.

Pendências conhecidas:
- `git diff --check` falha por linhas em branco no EOF em arquivos modificados antes desta execução; não misture limpeza desses arquivos com entregas sem revisar autoria.
- Não usar `ADD CONSTRAINT IF NOT EXISTS` em SQL (inválido em PostgreSQL); seguir o padrão da migration 134 (loop sobre `pg_constraint` + `if not exists … execute …`).
- PowerShell 5.1: `${a.b}`/`${a[i]}`/`${$x.Prop}` são literais (use `$(...)`); `ConvertFrom-Json '[]'` → `$null`; PG18 com múltiplos `-c` retorna só o último resultado; `timeout /t` falha com stdin redirecionado (use `ping -n` como sleep em bat).
- Postgres reporta apenas o PRIMEIRO erro por query — auditar todos os ramos contra o DDL após editar SQL.
- Devolução ao fornecedor (migration 133) já auditada e integrada à jornada; mudança configurável do gatilho financeiro, Campo/Pecuária e integrações externas (IA/pagamento/fiscal) continuam no backlog posterior.
