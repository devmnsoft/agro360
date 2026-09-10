# Checkpoint de execução do plano mestre

## Incremento E0 — atualização incremental e correção da fixture — 2026-09-10

Baseline confirmado: branch `work`, HEAD inicial `b6bfd4d` (merge do PR #98), sem alterações locais e com `001eadf` posterior à referência `38bf393` do PR #97. Não há `AGENTS.md` no repositório ou em seu diretório pai.

- **AG-E0-003 implementado sem homologação:** as migrations novas `006z`/`007z` resolvem a colisão entre o formato de `finance.receivables` publicado na 001 e o formato esperado pela 007 sem alterar os arquivos publicados. A tabela antiga é preservada, títulos positivos são migrados de modo reexecutável e títulos zero permanecem no arquivo por violarem o novo invariante. Instalação/upgrade PostgreSQL ainda precisa ser executado em ambiente com `psql`/servidor.
- **AG-E1-004 corrigido sem homologação:** o provisionador comparava a conta Santa Clara com o ID `...002`, embora o instalador e o próprio upsert usem `...003`; a validação de identidade e a proteção contra ocupação do ID agora usam a fixture canônica.
- **Classificação das jornadas:** Administração MNSOFT, Administração do Cliente, contratação modular, compras/recebimento, comercial/entrega e produção permanecem **parciais**; estoque, financeiro, CRM e qualidade permanecem **implementados sem homologação** como discriminado na matriz. Nenhuma jornada foi promovida a homologada neste ambiente.

Próximo passo concreto: executar `verify-e0.ps1 -CheckMigrations` em PostgreSQL descartável, incluindo base somente com 001 e registros legados; depois fechar AG-E1-002 (MFA e acesso assistido auditado) antes de ampliar jornadas operacionais.

## Incremento de estabilização pós-PR #97 — 2026-09-10

Estado observado: branch `work`, HEAD inicial `38bf393`. Não havia alterações locais. O ambiente não oferece `dotnet`, `pwsh` nem `psql`; acesso, banco e jornadas permanecem **implementados sem homologação**.

- **Acesso:** API e Migrator compartilham nome da aplicação, propósito MFA e diretório persistente configurável de Data Protection. O Migrator carrega o ambiente selecionado, valida identidade/tenant/ID antes do upsert e há provisionador PowerShell sem Python.
- **Compras:** repetição compara fingerprint SHA-256 do comando; conteúdo divergente gera conflito. Unidade diferente da unidade-base é bloqueada até conversão explícita. Parcelamento impossível falha antes dos inserts. Excesso exige `purchasing.receipts.override-excess`.
- **Banco:** migration incremental `067_procurement_receipt_integrity.sql` e instalador consolidado receberam fingerprint, constraint e permissão. Instalação limpa e atualização anterior continuam pendentes.
- **Fora deste incremento:** quarentena/liberação, reversão e jornadas comercial/industrial. Essas lacunas não foram reclassificadas.

Passaram: `git diff --check`, `bash -n scripts/provision-homologation.sh`, `node scripts/verify-offline-shell.mjs` e `bash scripts/validate-full-sql.sh`. Restore/build/testes, parser PowerShell, PostgreSQL, MFA entre processos e navegador não foram executados pela ausência dos runtimes.

Atualizado em 2026-09-08. Este documento registra somente evidências reproduzíveis; presença de arquivo, rota ou tela não equivale a fluxo homologado.

## Fonte e regra de avanço

O anexo foi localizado e lido integralmente em `C:\Users\NCELL-DEV-020\Downloads\Agro360-Prompt-Mestre-Continuidade.md`. Sua cópia integral foi incorporada em [execucao/AGRO360-MASTER-PLAN.md](execucao/AGRO360-MASTER-PLAN.md). O bloqueio histórico por ausência do documento está resolvido. A ordem atual é [execucao/EXECUTION-PLAN.md](execucao/EXECUTION-PLAN.md), com decisões em [execucao/DECISIONS.md](execucao/DECISIONS.md). Este arquivo e a matriz v0.2.0 são reutilizados, não duplicados.

As regras de execução já confirmadas são:

1. preservar alterações locais e nunca usar `git reset --hard`;
2. classificar cada capacidade com evidência de código e de execução;
3. executar E0 antes de avançar quando restore, build, testes, banco, API ou Web não estiverem saudáveis;
4. considerar uma entrega concluída somente com banco, regras, autorização, serviço, endpoint, tela e validação;
5. atualizar este checkpoint e `docs/TRACEABILITY-MATRIX-v0.2.0.md` ao fim de cada incremento.

## Incremento E0 — diagnóstico confiável (estado atual)

- Repositório confirmado: `C:\MNSOFT\agro360`, remoto `https://github.com/devmnsoft/agro360.git`.
- HEAD inicial e ainda sem commit desta entrega: `4b650fb74fc6d73050a5d84d0540f3962bf75f8c`. Criada `codex/agro360-e0-runtime` a partir dos três commits locais; sem reset, pull, commit, push ou PR nesta entrega.
- SDK efetivo 10.0.400; PostgreSQL 18; PowerShell 7; .NET/Razor/Dapper preservados.
- **AG-E0-001 entregue:** readiness distinguindo conexão de schema mínimo; sem schema → 503, instalado → 200; liveness continua 200. Nenhuma migration/seed de produto alterada por este incremento.
- **AG-E0-002 entregue:** service worker limitado ao shell público da mesma origem, sem fallback para API/health/Swagger/dados autenticados; cache lookup legado invalidado. Diagnóstico Web valida conteúdo e usa a URL configurada.
- **Implementação própria:** `DatabaseHealthCheck.cs`, trechos de `agro360.js`, `service-worker.js`, método de regressão em `AuthenticationAndLivestockRegressionTests.cs`, `scripts/verify-e0.ps1`, `scripts/verify-offline-shell.mjs` e documentação de consolidação.
- **Não atribuir a esta entrega:** propriedades/SaaS/SQL/migrations 051 e 064, alterações de layout, novos testes de propriedades e ajustes MTP dos csproj. Surgiram em execuções concorrentes e foram preservados. Revisar hunks antes de commit; não usar `git add .`.

### Evidências reproduzíveis deste incremento

**Última execução completa: `artifacts/e0-8072988ddd92424aaa3eb21ec817af3f/` — `pwsh -File scripts/verify-e0.ps1`, exit 0. Restore aprovado; build Release com zero avisos/erros; instalador limpo e repetido; API/Web, login/dashboard/refresh/replay/logout aprovados; 118 testes aprovados, zero falhas e zero ignorados.** O gate incremental separado continua reprovado (item 7). As contagens anteriores abaixo documentam a evolução concorrente do worktree, não somam cobertura.

1. `artifacts/e0-1ae0bb43a9e54027a9690ad966e6013f/Api.log`: regressão original, `/health` 200 em banco vazio. Após correção, gate comprova 503 antes da instalação e 200 depois.
2. `artifacts/e0-29ec612e4919472a9f41924daec1aa2b/`: restore/build, instalador limpo/repetido, HTTP autenticado e 114 testes aprovados, zero ignorados. É evidência anterior aos novos testes de propriedades/SaaS concorrentes, não uma garantia do diff final.
3. `artifacts/e0-52826d86f0d34129bd1686076c602e10/`: smoke HTTP/SQL completo passou; suíte detectou 122 testes, 120 aprovados e duas falhas durante edições concorrentes (`PropertyRulesTests` código de erro de UF; `saas.js` temporariamente ausente). Nenhum teste foi desativado. Reexecução final registrada abaixo.
4. Navegador real, Web isolada em `http://127.0.0.1:52345`: página/login renderizados e ajuda do campo organização acessível. Com API deliberadamente inacessível, antes da correção foi exibido “API e Swagger conectados”; depois, “Falha na conexão” com a URL efetiva. Aba e processo temporários encerrados. Login completo no navegador, menus por perfil e responsividade móvel ainda não foram homologados.
5. `node scripts/verify-offline-shell.mjs`: executa o worker real em VM sem dependências, verifica bypass de health/OpenAPI/API/Authorization/outra origem/POST, shell offline, ausência de fallback HTML para CSS e invalidação apenas dos caches Agro360 antigos.
6. `artifacts/e0-2cd37a246a5a41e99eaa365f30720337/`: reexecução completa aprovada (restore, build Release, HTTP/SQL e 123/123 testes, zero ignorados). As duas falhas transitórias do item 3 não se repetiram; os respectivos arquivos foram corrigidos por suas execuções de origem.
7. `artifacts/e0-babba5c99ac145deb1e5ad522128470e/`: `-SkipBuild -CheckMigrations` passou todo smoke e 118/118 testes após reorganização concorrente dos testes. O gate adicional de migrations **falhou**, como deve registrar: `007_sprint8_finance.sql`, PostgreSQL `42703`, `column "due_on" does not exist`. A migration 001 cria recebíveis com `due_date`; a 007 usa `CREATE TABLE IF NOT EXISTS`, não adapta a tabela existente e tenta indexar `due_on`. Há também divergência de schemas legados/canônico. Não se alterou migration publicada para esconder o problema.
8. `dotnet format MNSOFT.Agro360.sln --verify-no-changes --no-restore --verbosity quiet`: exit 0, log `artifacts/e0-format-global.log`. Verificação focada nos dois C# desta entrega também exit 0. Parser PowerShell, `node --check` dos JS/MJS alterados e `git diff --check`: aprovados. Cópia do mestre comparada integralmente ao anexo, idêntica após normalização CRLF/LF.

Links Markdown locais dos controles/manuais atualizados: zero destinos ausentes. Nenhum segredo ou binário foi acrescentado aos arquivos da entrega; os dados/logs de teste permanecem apenas em `artifacts/`, ignorados pelo Git. Não foram criadas classes novas de teste por este incremento: acrescentou-se um método à classe existente e um script de regressão sem dependências.

### Comandos de retomada

```powershell
git status --short
git log -5 --oneline
pwsh -File scripts/verify-e0.ps1 -PostgresBin 'C:\Program Files\PostgreSQL\18\bin'
node scripts/verify-offline-shell.mjs
# Gate incremental separado, também em cluster descartável:
pwsh -File scripts/verify-e0.ps1 -SkipBuild -CheckMigrations -PostgresBin 'C:\Program Files\PostgreSQL\18\bin'
```

O script gera senhas aleatórias em memória e hash para `admin@santaclara.agro360.local` somente na instalação descartável; não oferece senha universal nem altera a conta do banco local. As contas demo/SuperAdmin existentes no instalador precisam do provisionamento seguro AG-E1-004. O login do SuperAdmin não foi validado nesta rodada. Os artefatos locais contêm logs e cluster encerrado, são ignorados pelo Git e não devem ser publicados como binário de entrega.

### Pendências e próxima entrega

**AG-E0-003: caminho incremental canônico — com defeito comprovado.** Primeiro bloqueio executado: migration 007, `due_on` inexistente após a 001 (`due_date`). O instalador usa `agro360`; `001_foundation.sql` cria schemas legados e o migrator possui histórico distinto. Provar instalação incremental/upgrade com dados anteriores e corrigir sem alterar checksums publicados. Não assumir que o instalador pode ser aplicado sobre qualquer versão existente. E0 não é declarada integralmente encerrada enquanto esse caminho estiver quebrado.

Depois: AG-E1-001–004 (identidade/vínculo/contexto, MFA/assistência, administração cliente, demo segura). E1 não está homologada. O menu inclui rótulos estáticos de clima/serviços online e há publisher/split simulados; detalhes na matriz. Isolamento completo com role não-superusuária, dois clientes, carga, HTTPS persistente e integração externa permanecem gates próprios. Não confundir teste com role dona do cluster com prova de RLS contra usuário de aplicação. A branch criada ainda não possui upstream; preservou-se o tracking inicial `main` → `origin/main`, sem publicar a nova branch.

## Histórico anterior — repositório e alterações preservadas

- Repositório: `C:\MNSOFT\agro360`.
- Branch observada no início: `main`, três commits à frente de `origin/main`. Durante a validação ela foi trocada externamente para `codex/agro360-e0-runtime`; este checkpoint não realizou a troca nem criou commit.
- Alterações locais preexistentes preservadas: configurações de inicialização da API/Web e `wwwroot/js/agro360.js`.
- Alterações concorrentes detectadas e preservadas durante a execução: controller, contratos, domínio e serviço de propriedades. Elas não pertencem a este checkpoint e não devem ser incluídas mecanicamente em commit futuro.

## Histórico anterior — E0 executada

### Falha encontrada

`dotnet test MNSOFT.Agro360.sln` retornava código 5 e descobria zero testes nos três projetos, embora existissem casos `[Fact]` e `[Theory]`. Os projetos usavam xUnit v3 com Microsoft Testing Platform no `global.json`, mas estavam configurados como bibliotecas e sem entrada MTP.

### Correção

Os três projetos de teste agora declaram:

- `OutputType=Exe`;
- `TestingPlatformDotnetTestSupport=true`;
- `UseMicrosoftTestingPlatformRunner=true`.

A opção VSTest legada `--logger "console;verbosity=minimal"` não deve ser usada no comando MTP deste repositório: ela fez a execução voltar a reportar zero testes. O comando canônico é `dotnet test MNSOFT.Agro360.sln`.

## Histórico anterior — evidências de execução

| Área | Estado | Evidência | Próximo gate |
|---|---|---|---|
| Restore | FUNCIONANDO | `dotnet restore MNSOFT.Agro360.sln`: sucesso | manter no CI |
| Build | FUNCIONANDO | `dotnet build -c Release --no-restore`: 0 avisos e 0 erros | repetir após cada incremento |
| Testes xUnit/MTP | CORRIGIDO | 114 descobertos; 110 aprovados; 4 ignorados; 0 falhas | executar os 4 testes PostgreSQL |
| PostgreSQL da aplicação | QUEBRADO NO AMBIENTE | `/health` respondeu 503; `psql -w` informou ausência de senha | configurar `ConnectionStrings__Agro360` sem versionar segredo |
| API/Swagger | PARCIAL | host ativo; Swagger HTML e JSON responderam 200; 657 operações OpenAPI | obter `/health` 200 e executar smoke autenticado |
| Web/shell | PARCIAL | `/` respondeu 200 e contém o modal de login | validar no navegador com API e banco saudáveis |
| Login | QUEBRADO NO AMBIENTE | `POST /api/v1/auth/login` respondeu 500 enquanto o banco estava indisponível | instalar/migrar banco e validar os dois perfis |
| Integração PostgreSQL | INCOMPLETA | 4 testes foram ignorados por ausência de `AGRO360_TEST_CONNECTION_STRING` | executar contra banco descartável homologado |
| Entregas verticais existentes | INCOMPLETAS | a matriz v0.2.0 ainda marca os fluxos centrais como `FOUNDATION` e sem E2E Web | reclassificar somente após E2E persistente |
| Plano mestre anexado | BLOQUEADO | arquivo não disponível nesta sessão ou no repositório | reanexar ou informar caminho local |

O build `Debug` posterior encontrou a API do usuário já ativa no PID 17912 e não pôde substituir DLLs bloqueadas. O processo não foi encerrado. A validação foi feita em `Release`, com diretório de saída separado.

## Histórico anterior — ações desbloqueadoras (substituídas pelo incremento acima)

1. disponibilizar o documento mestre;
2. fornecer a connection string de homologação por variável/secret manager;
3. executar instalador/migrações e os quatro testes de integração;
4. validar `/health`, login, refresh e isolamento de tenant;
5. selecionar no documento mestre a primeira entrega incompleta e sem dependência pendente.

## Prompt de continuidade

> Continue em `C:\MNSOFT\agro360`. Leia `docs/EXECUTION-CHECKPOINT.md`, `docs/TRACEABILITY-MATRIX-v0.2.0.md` e `docs/execucao/{AGRO360-MASTER-PLAN,EXECUTION-PLAN,DECISIONS}.md`. Preserve alterações concorrentes, revalide E0 e conclua AG-E0-003: migração incremental canônica, sem alterar checksums publicados, testada com dados anteriores. Atualize matriz/checkpoint e avance para E1 somente após os gates. Não faça push.

## Entrega de integridade — 2026-09-09

- **Causa:** limites eram aceitos do payload, comparações ocorriam antes da normalização e apontamentos podiam reabrir ordens finais.
- **Arquivos/regras:** contratos e serviços Commercial360/IndustrialProduction, regras comerciais, layout, gate SQL e testes arquiteturais existentes.
- **Aceite coberto estaticamente:** variações de caixa usam a mesma transição; transições inválidas são negadas; desconto usa política vigente no servidor; ordem industrial é bloqueada antes do apontamento; qualidade exige evidência positiva; clima demonstrativo foi removido.
- **Evidência:** rotas, shell offline e validação estrutural do SQL passaram. Build/test não foi executado porque o SDK .NET não está instalado; PostgreSQL não foi homologado neste ambiente.
- **Continuidade:** AG-E5-001 permanece parcial (recebimento/estoque/financeiro); AG-E7-001 permanece parcial (idempotência, reservas, consumo e retificação auditada). Esta entrega não declara as jornadas completas.

## Entrega de integração e lacunas PR #96 — 2026-09-09

- **Baseline real:** antes da edição, `main` estava em `74a2d5b0d016606e9ee957081b0fa91bb21fa110`, `origin/main` em `adc7cdd0f902a7fdb544f495e1907bf4825fffce`, com merge pendente por divergência local/remota. O merge foi realizado sem reset destrutivo; conflitos foram resolvidos preservando SaaS do PR #96 e o bloco local de recebimento de compras integrado.
- **AG-E5-001 parcial avançado:** pedido comercial agora consulta `base_price`/`maximum_discount` do servidor, impede contorno por `UnitPrice` reduzido, calcula total por soma de linhas arredondadas, e grava `price_table_id`, `base_unit_price` e `pricing_snapshot` nos itens. Migration incremental: `database/migrations/066_sales_order_pricing_snapshot.sql`.
- **AG-E7-001 parcial avançado:** `RecordAsync` usa `ProductionStepLookup` anulável, aceitando etapa válida não crítica com opcionais vazios; conclusão usa `production_batches.quality_status='APPROVED'`, bloqueando aprovação histórica seguida de bloqueio/reprovação.
- **UI comercial:** `commercial.js` foi formatado, corrigiu `returnform.reset()` e evita duplicação de opções nos lookups ao reabrir o formulário.
- **Testes acrescentados em classes existentes:** cálculo comercial cobre dois itens de `0,005` e desconto efetivo; testes arquiteturais cobrem snapshot comercial, read model de etapa e qualidade efetiva por lote.
- **Não homologado ainda:** banco PostgreSQL descartável, navegador, API/Web, concorrência e cenários E2E do prompt. Roteiro versionado receita → etapas esperadas → ordem, geração operacional de lote acabado, reservas industriais e sequência comercial reserva → entrega → recebível continuam pendentes.

## Provisionamento seguro de homologação — 2026-09-09

- O comando explícito `scripts/provision-homologation.sh` passou a receber senhas sem eco, gerar segredo TOTP individual e exigir confirmação no autenticador antes da persistência.
- O Migrator recusa Production, identifica host/porta/base/usuário sem revelar a connection string, valida os IDs/slugs das fixtures, usa o `PasswordHasher` real e Data Protection persistente, corrige os vínculos, revoga sessões e registra auditoria sem material secreto.
- O SQL consolidado permanece sem credencial conhecida e a documentação contraditória foi removida. Reexecução ocorre somente por comando operacional explícito; nunca no startup/seed.
- **Contas ainda não provisionadas neste ambiente:** não há SDK .NET nem cliente/servidor PostgreSQL instalados no contêiner. Retomada: `ConnectionStrings__Agro360='<segredo local>' ASPNETCORE_ENVIRONMENT=Homologation ./scripts/provision-homologation.sh`; depois iniciar API/Web e concluir login, troca, refresh e logout pelo navegador.

## Incremento E6 — fundação de Pecuária Integrada — 2026-09-10

Baseline: branch `work`, HEAD `770e1f9`, árvore inicialmente limpa. Não foram encontrados `AGENTS.md`. O ambiente desta execução não dispõe de `dotnet` nem `psql`, portanto código e banco permanecem **implementados sem homologação**.

- **Utilizável após migration 068:** cadastro individual preserva categoria, indicação de nascimento estimado, origem e observações; valida referências do tenant e impede evento anterior ao nascimento. `GET /api/v1/livestock/animals/{id}` retorna o cadastro e timeline ordenada pela data operacional, combinando eventos, transferências, manejos e sanidade.
- **Modelo criado:** localizações próprias; modo explícito de controle coletivo/individual; movimentos quantitativos auditáveis; histórico de identificadores; conciliação obrigatória antes da individualização. Lotes de estoque continuam independentes.
- **Integrações preservadas:** tratamento existente segue consumindo estoque e apropriando custo na mesma transação; rastreabilidade existente liga produto, aplicação e animal. Não foi criado financeiro paralelo.
- **Parciais:** cadastro/histórico, pesagem individual, tratamento/estoque/custo, propriedades/pastos e dashboard.
- **Não iniciadas neste recorte:** ordem de manejo completa, pesagem coletiva, alimentação com devolução/perda, reservas pecuárias, saída comercial integrada, rateios e exports CSV.
- **Sem homologação:** migration limpa/incremental, API/Web, Swagger, login/MFA, isolamento com role não proprietária, concorrência e navegador/mobile. Retomada: executar restore/build/test, `scripts/validate-full-sql.sh` e migration 068 em PostgreSQL descartável antes de ampliar os fluxos.

Continuidade ordenada: homologar 068; implementar movimentação individual/coletiva com estorno; depois ordens de manejo e pesagens; só então alimentação/estoque, comercial/financeiro e custos. Manutenção de equipamentos, logística e sincronização móvel permanecem posteriores a essas dependências.
