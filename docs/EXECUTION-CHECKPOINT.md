# Checkpoint de execução do plano mestre

## Central de trabalho móvel e sincronização controlada — 2026-09-11

Estado real: branch `work`, HEAD inicial `6f7d8eb`, árvore limpa e sem conflitos. O recorte reutiliza `/Work`, `/field`, `MobileService`, IndexedDB, service worker e tabelas `mobile_*`; não cria centrais ou filas paralelas. O ambiente atual não oferece `dotnet` nem `psql`, portanto build/testes .NET, PostgreSQL descartável, API/Web, autenticação/MFA e navegador real não foram homologados.

Implementado: contrato v1 explícito com origem/versão/dependências; catálogo fechado e lote máximo de 50; sessão móvel online de 12 horas vinculada a usuário, tenant e dispositivo; revalidação de dispositivo revogado; hash persistente e trava transacional por chave; replay de mesmo conteúdo recupera o resultado e conteúdo divergente vira conflito. A aplicação do efeito e o resultado ficam na mesma transação. O cliente agora sempre grava primeiro na fila local, distingue pendente/enviando/rejeitado, mantém motivo, usa a sessão emitida pelo bootstrap e separa IndexedDB/rascunhos por contexto autenticado. O shell v45 continua sem cache de API/autenticação; atualização não remove IndexedDB.

Limites honestos: são sincronizáveis apenas registros rápidos já suportados, ocorrência, check-in e evidência; baixa de estoque, aprovação, qualidade, financeiro e confirmação definitiva de entrega continuam exclusivamente no servidor. A central online existente permanece a fonte das tarefas de domínio. Administração visual completa de dispositivos, conflito comparativo, leitura de medidor/pesagem especializada e tentativa de entrega ainda são o próximo recorte. Revogação só é percebida offline na comunicação seguinte.

Evidências locais: `node --check src/Hosts/Agro360.Web/wwwroot/js/field.js`, `node scripts/verify-offline-shell.mjs`, `bash scripts/validate-full-sql.sh` e `git diff --check` aprovados. Não há alegação de sincronização homologada sem API/PostgreSQL.


## Estabilização de acesso + jornada de expedição/entrega — 2026-09-10

Estado real: branch `work`, HEAD inicial `a15b9fd`, árvore inicialmente limpa, sem índice de conflito e sem marcadores de merge. O container não possui `dotnet`, `psql` ou `pwsh`; portanto restore/build/testes .NET, instalação PostgreSQL, API/Web autenticadas, login, MFA, refresh e navegador **não foram executados** e permanecem pendentes. Não há connection string de homologação neste ambiente. O provisionamento seguro existente continua sendo `scripts/provision-homologation.sh`/Migrator; nenhuma senha, conta ou MFA foi redefinido.

Implementado como avanço independente: migration incremental `071_fulfillment_delivery_journey.sql`, consolidado SQL, contratos/serviço/endpoints e tela `/Logistics`. Reserva usa trava transacional por tenant+lote; expedição efetiva a saída uma vez; tentativa acumula somente saldo ainda em trânsito; recusa cria pendência e retorno nasce indisponível (`AWAITING_RECEIPT`, depois qualidade). Chaves idempotentes guardam hash e versões impedem despacho de edição antiga.

Evidências executadas: `node --check` do cliente logístico, `bash -n scripts/provision-homologation.sh`, `bash scripts/validate-full-sql.sh` e `git diff --check`, todos aprovados. Limites: vínculo de viagem/documentos/frete e recebimento/liberação física do retorno estão modelados, mas ainda não possuem toda a operação HTTP; integração fiscal segue pendente e nenhuma obrigação financeira nova é criada. O cenário Santa Clara não foi gravado sem PostgreSQL; não se declarou homologação local.

Próxima ação: executar o provisionador no mesmo `ConnectionStrings__Agro360` da API, confirmar MFA/troca inicial/login/refresh/rota protegida e aplicar 071 em base descartável limpa e em cópia de upgrade. Em seguida exercitar concorrência/idempotência e completar recebimento de retorno, perda, viagem/capacidade e conciliação financeira antes de sincronização móvel.


## Correção de merge + exclusão lógica (pecuária/frota) — 2026-09-10

Estado observado: branch `main`, HEAD `bf13d55` alinhado a `origin/main`. **Não havia merge/rebase Git ativo** (`git ls-files -u` vazio), porém marcadores `<<<<<<<`/`=======`/`>>>>>>>` estavam **commitados** em contratos/serviços pecuários, Migrator e no instalador SQL/documentação. Working tree reconciliada sem `git reset --hard` e **sem commit** (autorização explícita).

### Causas e reconciliação

| Sintoma | Causa | Resolução |
|---|---|---|
| CS8300 / contratos quebrados | Marcadores entre pecuária operacional e fundação | União de `OriginType`/`OriginNotes`/`PaddockId`/`FacilityId` com `Origin`/`BirthDateEstimated`; validações de ambos os lados |
| CS8999 / SQL cortado | Literal quebrada pelos marcadores | SQL único com parâmetros completos, incluindo `BirthDateEstimated` |
| Tipos de frota “ausentes” | Confusão pós-merge | Confirmados `IFleetOperationsService`, comandos e DI |
| CS0103 `Guard` | Soft-delete sem `using Agro360.SharedKernel` | Using adicionado |
| Full SQL / docs com marcadores | Merge commitado incompleto | HEAD operacional preservado; colunas aditivas `origin`/`internal_identifier`; bloco incoming alternativo (COLLECTIVE/locations) descartado do consolidado por não ser o modelo do código atual |
| Seed `internal_identifier` NOT NULL | Fundação forçava NOT NULL antes do seed | Coluna permanece nullable no instalador; índice único parcial |

### Exclusão lógica e auditoria

- Migration `070_audit_soft_delete.sql` (`7.0.0`): colunas de auditoria/exclusão em `fleet_%` e `livestock_%`; índices únicos ativos; `REVOKE DELETE/TRUNCATE` em `audit_logs` para `agro360_app` quando existir.
- Frota/pecuária: archive/restore com motivo, sem apagar histórico; restauração valida unicidade; listagens com autoria; detalhe com timeline de `audit_logs`.
- `deleted_at` é a fonte única de exclusão lógica. Cancelamento/estorno operacional **não** é substituído por soft-delete.

### Evidências

| Área | Resultado |
|---|---|
| Build Release | 0 avisos / 0 erros |
| Testes | 127 aprovados, 4 ignorados |
| JS | `node --check` fleet.js e livestock.js OK |
| SQL limpo + reexecução | **PASS** `artifacts/fleet-sql-d546c7b31e034946beb900dca7d97982` (`CHECK 4\|2\|2\|2\|3` com `7.0.0`) |
| Navegador autenticado / DELETE físico pela role app | **não executados** |

Pendências: E2E login/jornadas; AG-E0-003 incremental; soft-delete fora de pecuária/frota; gate `006z`/`007z` se presente no histórico remoto.

## Incremento frota / manutenção / abastecimento — 2026-09-10

Estado observado: branch `main`, HEAD `b6bfd4d` alinhado a `origin/main`. Alterações locais de pecuária (068) e frota (069) preservadas junto com `database/maintenance/provision-homologation-access.sql` e `scripts/provision-homologation-local.ps1`. Nenhum reset, commit, push ou PR nesta entrega.

**AG-E8-003 avançado (implementado não validado em navegador/API autenticada).** A frota deixou de ser só KPI: há jornada utilizável em `/Fleet` (menu “Frota e Manutenção”), `IFleetOperationsService`/`FleetOperationsService`, evolução de `FleetService`/`FleetController`/`FleetRules`, migration `069_fleet_maintenance_operations.sql` e seed demonstrativo Santa Clara.

- **Modelo:** situação cadastral ≠ status operacional ≠ ocupação na agenda. Placa e horímetro não são obrigatórios para implementos/estacionários. Inativação preserva OS, custos, abastecimentos e histórico.
- **Jornadas no código:** cadastro com propriedade/locação/energia/entrada em operação; leituras com reinicialização explícita e idempotência; planos preventivos com política `FIRST_CRITERION`/`CALENDAR_FIXED`/`METER_FIXED` e avaliação sem duplicar OS; solicitação → OS → reserva/consumo/devolução de peças; apontamento de tempo; inspeção (reprovação mantém bloqueio); liberação só sem impedimento não dispensável; reserva de ativo com conflito; abastecimento interno (baixa estoque uma vez) e externo (sem baixa); custos com origem; CSV com proteção de fórmula.
- **Correções desta rodada:** `OpenWorkOrder` grava `blocks_asset` e cria `fleet_operational_blocks`; cancelamento/conclusão liberam o bloqueio da OS e só tornam o ativo `AVAILABLE` se não houver outro impedimento; instalador consolidado deixou de dropar `platform_enable_tenant_rls` antes das seções 6.4.1/6.8.0/6.9.0.
- **Demo Santa Clara:** SC-TR-01 disponível, SC-IMP-01 em manutenção, leituras, plano próximo do vencimento, solicitação corretiva, OS aguardando peça, OS com inspeção pendente, abastecimentos interno/externo, reserva agrícola afetada. Usuários/senhas/MFA não foram alterados.

### Evidências desta máquina

| Área | Resultado |
|---|---|
| Restore | `dotnet restore MNSOFT.Agro360.sln`: sucesso |
| Build Release | `dotnet build -c Release --no-restore`: 0 avisos, 0 erros |
| Testes | `dotnet test`: 128 descobertos; 124 aprovados; 4 ignorados (PostgreSQL `AGRO360_TEST_CONNECTION_STRING`); 0 falhas |
| JS frota | `node --check src/Hosts/Agro360.Web/wwwroot/js/fleet.js`: sucesso |
| PostgreSQL 18 | instalador limpo + reexecução em cluster descartável `artifacts/fleet-sql-b22cac1c6ec84eb5afffda77e42204de`: **PASS** (`CHECK 4\|2\|2\|2\|2` — tabelas frota, ativos SC, OS, abastecimentos, versões 6.8.0/6.9.0) |
| Web HTTP `/Fleet` | host Release em loopback: **200** com marcadores `fleet-app`/`Como usar`/`fleet.js` (não substitui navegador autenticado) |
| Navegador / login real / jornadas API autenticadas | **não executados neste incremento** |

Não homologado: login, MFA, jornadas no navegador, reserva concorrente real, isolamento RLS com role de aplicação, upgrade incremental completo (AG-E0-003 `due_on` permanece). Compilação e inspeção estática não substituem E2E.

### Continuidade

Próximo recorte: logística (reservas/disponibilidade/movimentos confiáveis) e depois sincronização móvel com conflitos/replay controlados (AG-E8/AG-E9). Não introduzir edição offline de movimentos críticos sem essas garantias. AG-E0-003 permanece fora deste incremento.

## Incremento pecuário integrado — 2026-09-10

Estado observado: branch `main`, HEAD `b6bfd4d` alinhado a `origin/main`. Alterações locais preexistentes preservadas (`database/maintenance/provision-homologation-access.sql`, `scripts/provision-homologation-local.ps1`). Nenhum reset, commit, push ou PR nesta entrega.

**AG-E6-002 avançado (implementado não validado em PostgreSQL/navegador).** A pecuária deixou de ser só KPI no dashboard: há página `/livestock`, contratos operacionais, migration `068_livestock_herd_operations.sql` e seed demonstrativo da Fazenda Santa Clara.

- **Modelo:** animal identificado, lote de manejo, instalação/localização e lote de produto permanecem entidades distintas. Grupo `INDIVIDUAL` deriva cabeças dos animais; grupo `QUANTITY` movimenta quantidade e não recebe indivíduos sem conciliação explícita.
- **Jornadas no código:** cadastro/histórico/troca de brinco; entrada/transferência interna/saída; ordens de manejo com população planejada congelada e execução parcial; pesagem individual e coletiva (sem peso fictício por cabeça); restrições com liberação criteriosa; alimentação com devolução que não rebaixa estoque duas vezes; reserva comercial ≠ saída física ≠ obrigação financeira (recebível em `finance_commercial_receivables`); custos rastreados; CSV filtrado com proteção de fórmula.
- **SaaS:** rotas de escrita comercial usam `livestock.sell`; lookups pecuários não exigem `agriculture.read`; SuperAdmin não é promovido por payload.
- **Demo Santa Clara:** animais SC-N-1001..1004, lote coletivo de 40 cabeças, duas instalações, compra, transferência interna, manejo parcial, pesagens em datas distintas, consumo de ração, restrição operacional identificada como demonstrativa (não é orientação veterinária) e reserva comercial. Usuários/senhas/MFA não foram alterados.

### Evidências desta máquina

| Área | Resultado |
|---|---|
| Restore | `dotnet restore MNSOFT.Agro360.sln`: sucesso |
| Build Release | `dotnet build -c Release --no-restore`: 0 avisos, 0 erros |
| Testes | `dotnet test`: 126 descobertos; 122 aprovados; 4 ignorados (PostgreSQL `AGRO360_TEST_CONNECTION_STRING`); 0 falhas |
| JS pecuário | `node --check src/Hosts/Agro360.Web/wwwroot/js/livestock.js`: sucesso |
| `git diff --check` | sem erro de espaço |
| PostgreSQL 18 | `C:\Program Files\PowerShell\7\pwsh.exe -File scripts/verify-e0.ps1 -SkipBuild`: **PASS SQL fixture** no cluster `artifacts/e0-5e0bd13e60df4ab3aaa20bf3efc28c87` (instalador completo com 068). API não subiu: `ConnectionStrings:Agro360` e a chave legada `DefaultConnection` conflitam no ambiente Development desta máquina — falha pré-existente de configuração, não do SQL pecuário. |
| Navegador / login real | **não executado neste incremento** |

Não homologado: login, MFA, jornadas no navegador, concorrência real de reservas, isolamento RLS com role de aplicação. A instalação limpa do SQL consolidado (incluindo pecuária 068) passou no cluster descartável; a API isolada não foi exercitada por conflito de connection string no ambiente local.

### Continuidade

Próximo recorte operacional, preservando dependências: manutenção de equipamentos, logística e sincronização móvel (AG-E8/AG-E9), sem reabrir o modelo de rebanho. AG-E0-003 (migration 007 `due_on`) permanece com defeito conhecido e fora deste incremento.

## Incremento E0 — atualização incremental e correção da fixture — 2026-09-10

Baseline histórico (outro ambiente): branch `work`, HEAD inicial `b6bfd4d` (merge do PR #98). Relato preservado do lado remoto do merge:

- **AG-E0-003 implementado sem homologação:** migrations aditivas `006z`/`007z` (quando presentes) resolvem a colisão `due_date`/`due_on` sem alterar 001/007 publicadas. Gate PostgreSQL incremental continua obrigatório.
- **AG-E1-004:** fixture Santa Clara alinhada ao ID `...003` no provisionador.
- Nenhuma jornada foi promovida a homologada apenas por esse relato.

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

## Configuração guiada derivada e navegação contextual — 2026-09-11

Estado observado: branch `work`, HEAD inicial `c17d6f3`, árvore limpa, sem conflitos e sem migrations pendentes no índice. O ambiente continua sem `dotnet` e `psql`; por isso restore/build/testes .NET, PostgreSQL descartável, API/Web autenticadas, MFA e navegador real não foram homologados nesta rodada.

Diagnóstico atual das jornadas, sem promover presença de código a homologação: login/primeiro acesso, cadastro do cliente, contratação modular, configuração da organização, usuários/perfis, compras/recebimentos, estoque/qualidade, agricultura/pecuária, frota/manutenção, expedição/entregas e móvel estão **implementados sem homologação neste ambiente**. Permanecem parciais: validação real de contato no cadastro (integração de comunicação), alteração/suspensão/cancelamento comercial completos, configuração de depósitos/centros de custo dentro do assistente, recebimento físico do retorno logístico e operações críticas offline. As jornadas A e B possuem serviços transacionais já documentados, mas seguem sem E2E autenticado/PostgreSQL neste container.

Entrega deste recorte: `/Deployment` agora apresenta assistente retomável derivado dos dados persistidos (organização, preferências, propriedades, depósitos, centros de custo, usuários/perfis e catálogos) e somente exige dependências pertinentes aos módulos contratados. Clique não conclui etapa e a revisão final só conclui quando as dependências obrigatórias estiverem válidas. Os links da administração SaaS preservam a aba solicitada por query string e o shell mantém visível o identificador da organização autenticada inclusive após refresh do token.

Próxima etapa baseada nas lacunas: homologar o assistente com dois tenants e completar, sem novo cadastro paralelo, a edição persistida de preferências e os seletores paginados de depósito/centro de custo; depois exercitar retornos logísticos e falhas de integração das jornadas A/B.
