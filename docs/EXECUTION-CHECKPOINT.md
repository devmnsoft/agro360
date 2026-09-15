# Checkpoint de execução do plano mestre

## Auditoria funcional de formulários e persistência territorial — 2026-09-15

Baseline `work`/`b1ce454`, solução `MNSOFT.Agro360.sln`, SDK exigido 10.0.100 e árvore inicial limpa. O inventário registrou 44 Razor Pages, 36 páginas operacionais com 80 formulários e os encadeamentos por módulo em `docs/execucao/FORM-AUDIT.md`. As páginas não exercitadas estão explicitamente classificadas como não executadas; existência de botão, endpoint ou SQL não foi tratada como aprovação.

No fluxo prioritário `/properties`, foram adicionadas consultas tenant-scoped por ID para releitura posterior a POST/PUT, paginação real no servidor e confirmação visual somente depois da nova consulta. A exclusão lógica já transacional/auditável agora também recarrega a lista antes do sucesso. Ordenação ganhou desempate estável e os testes arquiteturais existentes foram ampliados. Sem mudança de schema. Gates estáticos de JavaScript, shell e whitespace passaram. Restore/build/test/API/Web/PostgreSQL continuam não executados porque o contêiner não possui .NET/PostgreSQL; tentativa de obter o SDK foi bloqueada com HTTP 403.


## Custos por safra e apropriação gerencial — 2026-09-14

Implementada a jornada `/Costs` e `/api/finance/season-costs`: painel semântico, origens, pendências, apropriação direta, rateios reproduzíveis, prévia, confirmação concorrente/idempotente, histórico, estorno, conferência por corte e CSV filtrado. A migration 078 preserva `cost_entries`, cria projeção gerencial e apropriações auditáveis no schema `agro360`, incluindo vínculos legados. Fórmulas, diagnóstico, limitações e aceite estão em `docs/CUSTOS-SAFRA-APROPRIACAO.md`. JavaScript, SQL consolidado e whitespace passaram nos gates estáticos; runtime .NET/PostgreSQL/navegador segue pendente porque as ferramentas não existem no ambiente.

## Reabertura auditável e idempotência concorrente do fechamento — 2026-09-14

Diagnóstico deste checkout: repositório Agro360, solução `MNSOFT.Agro360.sln`, branch `work`, base `f71d6fe`; os cinco documentos canônicos solicitados existem. O fechamento 077, serviços, API e tela já estavam presentes. A compilação não pôde ser repetida porque o container atual não possui `dotnet`; a validação disponível ficou limitada aos gates estáticos descritos abaixo.

Entregue neste incremento: reabertura autorizada por `agriculture.write`, com justificativa obrigatória, trava por safra, conferência da versão vigente e chave idempotente. A versão fechada permanece imutável; a reabertura cria uma nova versão ligada por `supersedes_id`, recalcula indicadores e pendências e registra o ator real em auditoria. Conferência, geração e reabertura agora repetem a leitura da chave após a trava, evitando duplicidade em requisições concorrentes. A situação exibida passa a vir da versão mais recente, e a interface oferece a reabertura somente para o fechamento vigente.

Classificação verificada por inspeção e gates estáticos: fechamento/conferência **implementados, ainda não verificados em runtime**; colheita, recebimento, inspeção, destinação, estoque e produção vinculada **implementados, ainda não verificados em runtime**; custos por safra **parciais**; genealogia comercial/financeira completa e documentos configuráveis **ausentes/bloqueados pelos vínculos de origem ainda não modelados**. Não houve alteração de banco: a estrutura versionada 077 já preserva histórico, autoria, idempotência e encadeamento.

Continuidade: (1) homologar o fechamento operacional com .NET/PostgreSQL e navegador; (2) completar apropriação e reconciliação de custos; (3) completar rastreabilidade entre safra, produção e expedição; (4) evoluir comparações entre safras somente após reconciliar indicadores.

## Fechamento gerencial da safra — 2026-09-14

Estado encontrado: branch `work`, HEAD inicial `4fb47d7`, árvore limpa; SDK `dotnet` e PostgreSQL ausentes. Colheita/recebimento/qualidade/destinação (075), beneficiamento (076), estoque/reservas e Central (073/074) estão implementados sem verificação de execução nesta máquina. Pedidos, expedições/entregas/devoluções, custos e recebíveis existem, mas o vínculo completo à safra é **parcial**; por isso receita reconhecida e estoque atual por safra são explicitamente “Não disponível”, sem atribuição inventada.

Entregue: migration incremental 077 e consolidado; execução de conferência idempotente; fórmulas por data operacional e unidade; bloqueio para recebido/destinado em excesso; pendências operacionais separadas; snapshot versionado, revisão ligada à anterior, trava concorrente, fechamento imutável e sinal de lançamento retroativo. `/Harvest` ganhou escopo pesquisável, indicadores com definição/origem, ações, histórico/comparação com base zero protegida e CSV com mitigação de fórmula. O fechamento não encerra operações fiscais/contábeis.

Evidências: `node --check .../harvest.js`, `bash scripts/validate-full-sql.sh` e `git diff --check` aprovados. `dotnet restore/build/test`, PostgreSQL limpo/incremental, autenticação e navegador desktop/celular não foram executados por ausência do runtime/servidor; executar esses gates antes de homologar. Próxima etapa: completar genealogia comercial/logística/custos até a safra e integrar as cinco projeções à Central, sem duplicar seus mecanismos.


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

## Central de Operações derivada e acionável — 2026-09-11

Estado observado: branch `work`, árvore inicial limpa e sem `AGENTS.md`. O container segue sem `dotnet` e `psql`; por isso restore/build/testes .NET, aplicação autenticada, Swagger, PostgreSQL limpo/incremental e navegador real não puderam ser executados. A validação desta entrega é estática (JavaScript, SQL consolidado, diff e marcadores).

Implementado sem homologação de execução: `/Work` passou a priorizar uma Central de Operações paginada no servidor. As ocorrências são derivadas, em uma consulta, de aprovações atribuídas ao usuário, compras com saldo, divergências de recebimento, inspeções pendentes, expedições/retornos, manutenções próximas/vencidas e títulos a pagar. Cada ramo exige a permissão efetiva do módulo no banco; o login continua removendo permissões de módulos não contratados. A resposta informa organização, responsável, prazo, prioridade, motivo, ação e link de origem.

A migration 073 persiste somente interação (`VIEWED`/`ASSIGNED`) e seus eventos, isolados por tenant/RLS. Não existe comando para “resolver” ocorrência: ela desaparece somente quando o estado original deixa de atender ao predicado. O frontend preserva filtros na URL, pagina, bloqueia submissões repetidas, apresenta ajuda operacional e usa layout claro e responsivo.

Pendências reais: conferir os códigos de módulos/roles em bases atualizadas, executar 073 limpa e incremental, validar materialização Dapper, planos de consulta e RLS com dois tenants, e exercitar cada link/estado no navegador. Atividades agrícolas atrasadas, documentos obrigatórios e ordens industriais bloqueadas não entraram nesta fatia porque seus modelos precisam de uma regra canônica de prazo/obrigatoriedade antes de serem agregados sem falsos positivos. Favoritos persistidos e atribuição por seletor pesquisável (em vez do diálogo textual provisório) permanecem pendentes.

## Central confiável e recebimento de retornos — 2026-09-14

Baseline confirmada: branch `work`, HEAD inicial `e15dded` (merge do PR #107), árvore limpa. A migration incremental `074_operation_center_reliability.sql` separa leitura individual da atribuição compartilhada, preserva somente leituras legadas com autor conhecido, adiciona versão otimista e eventos de atribuição, transferência e retirada. A Central passou a contar e listar com o mesmo filtro em consultas separadas, desempate estável, prazo ausente explícito e sem os prazos artificiais de aprovação, compra e qualidade. O cliente preserva filtros/página na URL, cancela respostas antigas, recupera páginas vazias e usa modal pesquisável de pessoas elegíveis sem entrada manual de UUID. A marcação de leitura é best-effort e não bloqueia o destino.

Retornos agora possuem recebimentos físicos parciais idempotentes, condição, local, lote/evidência, saldo autorizado, controle de versão e destinações auditadas. Todo recebimento permanece em `AWAITING_QUALITY`; decisão física não declara conciliação financeira. A migration e o consolidado foram atualizados sem alterar a 073 aplicada.

Evidências neste container: `node --check src/Hosts/Agro360.Web/wwwroot/js/work.js`, `bash scripts/validate-full-sql.sh` e `git diff --check` aprovados. `dotnet restore` não pôde iniciar porque o runtime `dotnet` não está instalado; por isso build/testes, API autenticada, PostgreSQL descartável, isolamento RLS por role de aplicação e navegador real permanecem pendentes e não são chamados de homologação funcional. Continuidade: homologar Central/retornos com PostgreSQL e então seguir a ordem registrada no plano (assistente persistido; prazos reais das novas origens; integração à Central; expansão móvel).

## Jornada de colheita e recebimento — 2026-09-14

**Estado encontrado.** Branch `work`, HEAD inicial `72e13ee`, árvore limpa. Organização/tenant, propriedades/talhões/safras, unidades, depósitos/estoque, especificações de qualidade, centros de custo, autenticação/MFA/permissões e Central de Operações já possuíam fundações persistentes; agricultura tinha safra, plantio e um apontamento de colheita que lançava estoque cedo demais. Qualidade, rastreabilidade e custos tinham estruturas reais, mas não havia uma jornada agrícola de recebimento/classificação/destinação. Classificação operacional, custos por denominador e relatórios desta jornada estavam **parciais**; ocorrências específicas da Central permanecem **ausentes**. O ambiente não contém `dotnet`, `psql`, PostgreSQL nem navegador, logo capacidades anteriores e este incremento continuam implementados sem verificação de execução E2E nesta máquina.

**Implementado.** A migration incremental 075 e o instalador consolidado adicionam planejamento compatível com propriedade/safra/talhão/produto/destino, apontamento separado da estimativa, recebimento parcial concorrente, peso líquido calculado, snapshot da versão da especificação, inspeção obrigatória, destinação parcial e entrada no saldo existente somente da parcela aprovada. Todas as mutações usam tenant/ator do servidor, chaves idempotentes com hash e bloqueios `FOR UPDATE`; perda/descarte ficam como destinações físicas, sem apagar origem. Dashboard distingue planejado, colhido, recebido, pendente de recebimento/qualidade, aprovado, perda e custos apropriados, com denominadores indisponíveis quando zero e marca provisória explícita. A tela `/Harvest` usa seletores por nome, fluxo em etapas, feedback acessível, envio protegido, aviso de alterações e layout responsivo.

**Evidências locais.** `node --check src/Hosts/Agro360.Web/wwwroot/js/harvest.js`, `bash scripts/validate-full-sql.sh` e `git diff --check` aprovados. `dotnet restore/build/test` não executados porque o SDK não está instalado. Instalação limpa/upgrade, concorrência real, MFA/API autenticada, CSV, Central de Operações e navegador móvel não foram homologados. Próximo recorte: completar parâmetros de inspeção na própria tela, projeções reais na Central, exportação CSV e testes PostgreSQL/E2E; depois avançar beneficiamento, comercialização e fechamento gerencial.

### Correção de analisadores e conferência da colheita — 2026-09-14

**1. Compilação e serviço.** Baseline desta rodada: branch `work`, commit inicial `2e534b092bf9e212480c35523914ce409ac755ea`, sem alterações locais e sem `AGENTS.md`. O SDK requerido é o .NET `10.0.100` (`global.json`), mas o executável `dotnet` não existe no container. A causa de CA1725 era a implementação pública usar `c`/`ct`, divergindo dos nomes `command`/`cancellationToken` do contrato; os oito métodos foram alinhados e continuam propagando o token para transação e comandos Dapper. CA1861 vinha da criação da matriz literal de destinações em cada chamada de `AllocateAsync`; os valores constantes, apenas consultados por `Contains`, agora ficam em campo privado `static readonly`. A revisão também passou a rejeitar `kind` fora de `PLAN`, `HARVEST` e `RECEIPT`, sem transformar entrada do usuário em identificador SQL, e impede consolidação numérica do dashboard quando há mais de uma unidade.

**2. Integridade e retificações.** Planejamento, apontamento, recebimento parcial sob lock, inspeção versionada, destinação parcial, idempotência com comparação de hash, auditoria local e lançamento único no estoque da parcela aprovada estão **implementados sem execução verificada**, pois não há .NET/PostgreSQL. O isolamento aparece em todos os predicados pelo tenant e o ator vem do contexto autenticado, mas RLS com dois clientes permanece sem homologação. Retificação/reversão de apontamento, recebimento, inspeção e destinação continua **ausente**: não foi criado um falso cancelamento por troca de status, pois ainda faltam as regras canônicas para reserva, consumo, transferência, expedição e custos posteriores. Genealogia de lotes derivados, conversão versionada de unidades e reprocessamento industrial integrado estão **parciais/bloqueados por dependências** dos módulos correspondentes.

**3. Telas e ações.** O fluxo `/Harvest` segue **parcial**: planejamento, apontamento, recebimento e destinação possuem formulários, seletores para cadastros e escolha do registro de origem pela lista; o cliente agora descreve o efeito antes de confirmar e conserva a chave idempotente após falha de resposta, permitindo repetir a mesma requisição sem duplicar o efeito. Dashboard, histórico, ajuda, proteção contra duplo envio e aviso de alterações não salvas estão implementados sem navegador verificado. O formulário de inspeção ainda não coleta dinamicamente parâmetros/evidências e, portanto, não é considerado funcional para especificações com resultados obrigatórios. Detalhe consolidado por registro, autoria completa, retorno aos filtros da Central, popups especializados, retificações e rastreabilidade visual permanecem **parciais ou ausentes**.

**4. Validação integrada e continuidade.** A migration relacionada continua sendo somente `075_harvest_receipt_quality_costs.sql`, já espelhada no instalador completo; nenhuma migration foi criada para correções de analisador/cliente. `node --check`, validação do SQL consolidado e verificação do diff são os checks executáveis desta rodada. `dotnet restore`, `dotnet build` e `dotnet test` estão **bloqueados pela ausência do SDK**, enquanto instalação limpa/incremental, concorrência real, API/Swagger/login/MFA, permissões, estoque/custos, navegador responsivo e links da Central continuam sem homologação. A próxima entrega deve primeiro tornar a inspeção dinâmica e implementar retificações transacionais com dependências posteriores; somente depois deve ampliar detalhe, genealogia e integrações de reprocessamento/custos.

## Beneficiamento integrado à colheita — 2026-09-14

Estado observado: branch `work`, HEAD inicial `ae0e480`, árvore limpa. O container possui Node 20 e Python 3, mas não possui `dotnet` nem `psql`; por isso restore/build/testes .NET, instalação limpa/incremental, concorrência PostgreSQL e navegador autenticado não foram homologados.

O módulo industrial existente foi evoluído, sem criar módulo paralelo. Recebimentos de colheita aprovados agora podem ser reservados parcialmente diretamente para uma ordem e receita compatíveis, sem uma segunda entrada de estoque. A reserva trava ordem e recebimento, desconta outras reservas e compara conteúdo na repetição; o consumo é outro fato idempotente. Produto principal/coproduto nascem pendentes; perda/refugo são fatos distintos e exigem motivo. A aprovação final cria lote/movimento/saldo disponível uma única vez por lote. Cancelamento libera apenas reserva não consumida e preserva consumos e resultados. Rotas genéricas de alteração de status foram substituídas por ações explícitas.

Correções preventivas: `IndustrialProductionService` passou a propagar o `CancellationToken` à transação; aprovação de receita usa a coluna `id`; parâmetros Dapper preservam nomes SQL. `HarvestService` já apresentava nomes de parâmetros iguais ao contrato, tokens propagados e coleções permitidas estáticas somente-leitura. A migration 076 e o consolidado registram tabelas, FKs, checks, índices, RLS e versão 7.6.0. Evidências locais: `node --check`, validador SQL e `git diff --check`. Pendente: executar todos os gates .NET/PostgreSQL/E2E e validar atualização de uma base histórica cujo módulo industrial tenha sido instalado pelo consolidado.

## Consolidação operacional do beneficiamento — 2026-09-14

**Diagnóstico recebido.** Não havia anexo, stack trace ou mensagem de erro específica nesta tarefa; portanto nenhum erro relatado pelo usuário foi inventado ou declarado corrigido. A inspeção do fluxo real encontrou uma regressão verificável na página `/Production`: o botão **Decisão de qualidade** referenciava `#quality-dialog`, mas o diálogo não existia, fazendo a ação falhar no navegador antes de chamar a API. Também não havia detalhe operacional acionável de ordem, e o cliente criava uma nova chave idempotente a cada nova tentativa de reserva, consumo ou resultado após perda de resposta.

**Correções e jornada disponível.** O diálogo de qualidade final agora coleta ordem e lote por seletores, decisão, motivo, laudo e evidência; aprovação exige laudo/evidência também no domínio e continua materializando estoque somente dentro da transação da decisão. O lookup de lotes é tenant-scoped e exclui os já aprovados. A lista e o kanban abrem um detalhe com número comercial, produto, linha, responsável, estado, previsto, reservado, consumido, produzido, aguardando qualidade, disponível, perda, próxima transição e histórico. Liberação, início, conclusão e cancelamento usam as ações explícitas existentes; a confirmação descreve efeitos e o servidor revalida as regras sob lock. Chaves de reserva, consumo e resultado ficam estáveis enquanto o formulário não muda e só são descartadas depois da confirmação do servidor.

**Estado real reavaliado.** Correções estáticas do `HarvestService`, planejamento/apontamento, recebimento parcial, inspeção/destinação, reserva/consumo, produtos resultantes e rastreabilidade estão **implementados sem validação de execução .NET/PostgreSQL**. Qualidade agrícola continua **parcial** pela coleta dinâmica de parâmetros; qualidade final industrial agora possui formulário mínimo real e regra backend, também sem E2E. Custos permanecem **parciais** (consulta real, sem rateio/retificação completa). Central de Operações permanece **ausente para as ocorrências específicas da colheita/beneficiamento**; nenhuma pendência ou prazo fictício foi criado. Retificações transacionais de recebimento, consumo, produção, perda, coproduto e destinação continuam **ausentes** e não foram simuladas por exclusão ou troca genérica de status.

**Evidências e limites.** `node --check src/Hosts/Agro360.Web/wwwroot/js/production.js`, `bash scripts/validate-full-sql.sh` e `git diff --check` foram aprovados. `dotnet restore`, `dotnet build` e `dotnet test` não puderam executar porque `dotnet` não está instalado; PostgreSQL, `psql` e navegador também não estão disponíveis. Assim, concorrência real, instalação limpa/incremental, isolamento A/B, autorização HTTP, login/Swagger, estoque/custos e responsividade visual ainda exigem o procedimento nativo descrito no README. A próxima fatia segura é implementar retificação como fatos compensatórios com dependências posteriores e, depois, projetar somente pendências canônicas na Central.

## Correção de compilação e consolidação do fechamento da safra (2026-09-14)

### Diagnóstico e correções

- **CS8031 (`HarvestService`)**: a inferência de sobrecarga escolhia o executor `Task` para lambdas com `return`, agravada por construções target-typed. As operações de conferência, geração e conclusão agora selecionam explicitamente `InTenantTransactionAsync<T>` e constroem os DTOs concretos; transação, rollback, exceções e `CancellationToken` permanecem no `DatabaseExecutor`.
- **CS7036 (`SeasonClosingIndicatorDto`)**: os indicadores de área usavam a assinatura antiga, sem `Unit`, `Availability`, `Definition`, origem e explicação. As construções agora usam argumentos nomeados, unidade `ha` e definições aderentes às consultas e exclusões. Snapshots legados sem definição recebem apenas uma explicação de compatibilidade na leitura, sem recalcular nem regravar seus valores.
- **CA1859/CA1869**: os helpers privados retornam os tipos concretos `List<SeasonClosingIssueDto>` e `SeasonClosingIssueDto[]`; uma configuração JSON estática, pronta antes do uso, é compartilhada. JSON vazio válido continua vazio, enquanto JSON inválido/incompatível propaga erro e não é transformado em aprovação. Indicadores históricos são desserializados separadamente.
- **CA1068 (`IndustrialProductionService`)**: as duas sobrecargas privadas `Tx` agora recebem o token por último; todas as chamadas foram atualizadas, mantendo separadas as operações `Task` e `Task<T>` e propagando o token à transação e aos comandos.

### Fechamento e experiência de conferência

A conclusão reconsulta o mesmo escopo e corte sob a transação, reavalia bloqueios e compara os valores, unidades e estados atuais com o snapshot. Mudança relevante gera `closing.stale_snapshot`; bloqueio novo gera `closing.blocked`. A versão anterior e sua relação `supersedes_id` continuam imutáveis, e o fechamento gerencial não altera pedidos, estoque, ordens ou títulos.

A tela foi organizada em Escopo, Resumo, Indicadores, Pendências e Histórico. Inclui consulta explícita antes da conferência, propriedade/unidade derivadas da safra autorizada, período de referência, estados consolidado/provisório/indisponível, detalhes expansíveis de fórmula, origem acionável, regra/impacto/responsável da pendência, proteção contra envio duplicado, mensagens de carregamento/falha e comportamento responsivo. Confirmações continuam específicas e sucesso só aparece após resposta do servidor.

### Verificações e limitações

- `node --check src/Hosts/Agro360.Web/wwwroot/js/harvest.js`: aprovado.
- `git diff --check`: aprovado.
- `dotnet restore MNSOFT.Agro360.sln`: não executado porque o SDK `dotnet` não está instalado no ambiente (`dotnet: command not found`). Pelo mesmo motivo, build e testes .NET permanecem pendentes e devem ser executados com `dotnet restore MNSOFT.Agro360.sln && dotnet build MNSOFT.Agro360.sln --no-restore && dotnet test MNSOFT.Agro360.sln --no-build`.
- PostgreSQL e navegador não foram iniciados, pois API/Web não podem ser compilados sem o SDK. Permanecem pendentes os cenários integrados de banco (tenant cruzado, permissão, safra vazia, bloqueio, revisão/idempotência), login/Swagger/layout e a inspeção visual em navegador.

### Revalidação definitiva dos snapshots (2026-09-14)

O checkout foi reavaliado na branch `work`, a partir do commit `67f4bc657eabd0bc2625b161f8ecbff0b2e51655`, sem alterações locais. Há uma única implementação de cada serviço principal e o SDK exigido permanece .NET `10.0.100`. As correções de sobrecarga genérica, argumentos completos dos indicadores, retornos concretos, opções JSON compartilhadas e posição final dos tokens já estavam presentes neste checkout e foram preservadas.

A leitura dos snapshots foi endurecida: ausência histórica (`null`, texto nulo ou em branco) continua sendo tratada como coleção ausente e `[]` continua sendo uma coleção vazia válida. JSON malformado, raiz incompatível ou coleção estruturalmente inválida agora gera `closing.invalid_snapshot`, registra apenas o tipo do snapshot (nunca o conteúdo persistido) e impede que corrupção seja interpretada como conferência sem bloqueios. Definições ausentes em indicadores legados continuam identificadas como limitação histórica, sem recalcular ou alterar versões fechadas.

O restore e o build de diagnóstico foram tentados antes da edição, mas não iniciaram porque o executável `dotnet` não está instalado. Assim, restore/build/testes, banco, dois tenants, autorização, idempotência concorrente, login/Swagger e navegador real continuam pendentes de validação em ambiente com o SDK e PostgreSQL. As verificações estáticas executáveis desta rodada estão registradas no commit correspondente; não houve alteração estrutural de banco nem nova migration.

## Etapa E1 — jornada inicial, acessos e catálogo (15/09/2026)

Diagnóstico no início desta execução: repositório `/workspace/agro360`, solução `MNSOFT.Agro360.sln`, branch `work`, HEAD `aaa8fff` e SDK exigido `10.0.100` (`global.json`). A árvore estava limpa. O contêiner não oferece `dotnet`, `psql`, `docker` ou navegador, portanto não foi possível afirmar login real, renderização ou aplicação da migration; o PostgreSQL efetivamente usado pelo usuário também não está acessível neste ambiente.

Classificação baseada em código e fluxo:

- **Implementado e verificado estaticamente:** autenticação pesquisa tenant fora de RLS, normaliza e pesquisa e-mail/CPF dentro da transação do tenant, valida estado/exclusão/hash, mantém MFA do SuperAdmin e só então emite sessão (`IdentityService`); o provisionador Santa Clara é explícito e não redefine credencial existente. O catálogo agora evita pedido pendente duplicado no banco e na transação, persiste snapshot imutável da oferta, não ativa nem cria cobrança ao solicitar e audita solicitação/decisão.
- **Implementado sem verificação integrada neste contêiner:** bootstrap/convite, usuários/perfis, revogação de sessões, proteção do último administrador, propriedades existentes, preferências, módulos, decisão SuperAdmin, dashboards e custos por safra. Exigem SDK/PostgreSQL/API/navegador para comprovação ponta a ponta.
- **Parcial:** onboarding apresenta conclusão calculada dos registros atuais, mas edição passo a passo retomável ainda precisa ser consolidada; usuários ainda não exibem escopo por unidade nem histórico individual; catálogo não possui preço por módulo (por isso mostra “Consultar contratação”); console global ainda não oferece paginação em todas as listas; idioma da tela é preferência local e não a preferência persistida do usuário.
- **Ausente:** provedor de e-mail (o estado permanece corretamente `PENDING_PROVIDER`), cobrança recorrente/liquidação automática e telemetria de uso funcional por módulo. Nenhum desses estados é simulado como concluído.

Alterações desta rodada reutilizam `platform_marketplace_modules`, `platform_tenant_modules`, `platform_marketplace_requests` e a auditoria de integrações. A migration incremental `079_customer_module_requests.sql` adiciona `offer_snapshot` e unicidade parcial para pedido pendente; o mesmo conteúdo foi incorporado ao instalador canônico. “Meus módulos” passou a exibir catálogo/estado/dependências, confirmação explícita, acompanhamento de solicitações e aviso de que pedido não ativa nem cobra. O menu ganhou o agrupamento “Conta e módulos”.

Para provisionar localmente sem alterar uma senha já existente:

```bash
read -r -s AGRO360_PROVISION_SANTA_CLARA_PASSWORD; export AGRO360_PROVISION_SANTA_CLARA_PASSWORD
ConnectionStrings__Agro360='<connection-string-do-mesmo-banco-da-api>' dotnet run --project src/Hosts/Agro360.Migrator -- provision-santa-clara --environment Development
unset AGRO360_PROVISION_SANTA_CLARA_PASSWORD
```

Em conta já provisionada, o comando preserva a credencial e não requer a variável. Redefinição deliberada usa exclusivamente `reset-santa-clara-password`. Depois, iniciar API/Web com a mesma `ConnectionStrings__Agro360` e validar login, rota protegida, logout, nova entrada, bloqueio, troca de tenant, onboarding, escopo e custos por safra.

Próxima etapa concreta: concluir onboarding editável e escopo por unidade; depois consolidar cobrança recorrente e eventos de uso funcional, sem inferir uso por login ou abertura de tela.

## Central de Trabalho integrada — 15/09/2026

Baseline confirmada em `/workspace/agro360`: branch `work`, commit inicial `b9776f823c071e3d99967f53e1e2a0317d45c236`, solução `MNSOFT.Agro360.sln` e SDK requerido .NET `10.0.100`. Não há `AGENTS.md` no checkout ou no diretório pai. A inspeção confirmou que o login resolve o tenant no PostgreSQL, consulta o usuário dentro da transação tenant, compara a senha ao hash persistido e mantém MFA; os provisionadores continuam comandos explícitos e preservam credenciais existentes por padrão. Sem SDK e PostgreSQL no contêiner, login e primeira rota protegida permanecem sem homologação E2E.

A Central existente foi ampliada, sem tabela paralela de status operacional: pedidos realmente em `AWAITING_APPROVAL`, divergências abertas, inspeções pendentes, saldos de custo reconhecido ainda não apropriados, a última versão bloqueada de fechamento e tarefas manuais abertas são projeções das origens e desaparecem quando o predicado real deixa de valer. Leitura segue individual; atribuição permanece acompanhamento versionado/auditado e não decide o processo. As fontes são filtradas por permissão antes da união e a API ganhou período, vencidas e sem responsável, mantendo contagem filtrada independente da página e ordenação determinística.

A navegação de custo abre o lançamento apropriável no fluxo existente, que revalida versão e saldo sob lock e registra lote/linhas/auditoria de apropriação. O fechamento interpreta safra e corte recebidos pela Central. Confirmações de cadastro e apropriação de custos passaram a usar o popup compartilhado. Não houve alteração estrutural nem migration: foram reutilizadas as migrations 073/074, 077 e 078 e o schema `agro360`.

Verificações locais concluídas: sintaxe dos JavaScripts alterados, validador do SQL consolidado e `git diff --check`. `dotnet restore/build/test`, inicialização API/Web, PostgreSQL limpo/incremental, login/MFA, concorrência, RLS entre dois tenants e navegador desktop/mobile/teclado não puderam ser executados porque `dotnet`, `psql`, servidor PostgreSQL e navegador não estão disponíveis. Próxima etapa: homologar os gates citados; então concluir o detalhe/decisão contextual de pedido na tela de Compras e substituir os prompts legados ainda existentes em fechamento/estorno por diálogos com motivo validado, com aceite E2E das três jornadas e atualização imediata da Central.

## 2026-09-15 — Requisições internas e consumo rastreável

Implementada a migration incremental 081 e a tela `/Inventory`, reutilizando saldos/movimentos/lotes canônicos. O backend separa aprovação, reserva, entrega, consumo e devolução, com tenant, permissões, locks, versão e idempotência. Critérios e matriz de formulários: [REQUISICOES-INTERNAS-MATERIAIS.md](REQUISICOES-INTERNAS-MATERIAIS.md). Validação runtime permanece pendente porque a imagem não contém o SDK .NET/PostgreSQL.

## 2026-09-15 — Transferências e inventário físico

Baseline: branch `work`, commit inicial `191906c18c8bd1df73c09e44ad59c130e679759b`, solução `MNSOFT.Agro360.sln` e SDK requerido 10.0.100. A migration 082, serviço transacional e telas de Estoque implementam expedição/trânsito/recebimento parcial e inventário com bloqueio, recontagem, reconciliação e ajuste idempotente sobre movimentos canônicos. Fórmulas, diagnóstico, critérios e matriz estão em [TRANSFERENCIAS-INVENTARIO-FISICO.md](TRANSFERENCIAS-INVENTARIO-FISICO.md). O contêiner não possui `dotnet` ou PostgreSQL; runtime, banco e navegador seguem pendentes.

## 2026-09-15 — Planejamento de reposição e integração com Compras

Baseline analisada: branch `work`, commit inicial `7ac9546`, solução `MNSOFT.Agro360.sln`, SDK exigido `10.0.100`. Foram reutilizados saldo/reserva de estoque, requisições internas, transferências, catálogo, requisições/pedidos/recebimentos de Compras, autorização por políticas e contexto transacional de tenant.

A migration 083 adiciona políticas por produto+depósito e snapshots auditáveis de necessidades, sem criar saldo ou módulo de compras paralelo. Fórmula: `utilizável = disponível - reservado`; `necessidade operacional = max(0, desejado - (utilizável - demanda descoberta))`; `projetado = utilizável - demanda descoberta + pedidos confirmados no horizonte + transferências de entrada`; `sugestão estoque = max(0, desejado - projetado - cobertura existente)`. Demanda descoberta usa apenas `solicitado - reservado - entregue`; pedidos contam só o saldo pendente; requisição que já originou pedido deixa de integrar a cobertura de requisições. Entradas atrasadas são excluídas das entradas garantidas, e entradas posteriores à data necessária permanecem visíveis apenas na projeção do horizonte.

A conversão ocorre no backend. Uma necessidade positiva é convertida pelo fator cadastrado e então elevada ao lote mínimo e ao múltiplo de compra; zero permanece zero. A confirmação trava a necessidade, valida versão e idempotência e cria requisição `OPEN`, nunca pedido/recebimento/pagamento. A tela `/Replenishment` oferece políticas, filtros, análise, memória e encaminhamento; o acompanhamento continua no módulo de Compras. Transferências continuam no fluxo existente e não são movimentadas pela consulta.

Auditoria real de formulários deste recorte: inclusão e consulta de política; análise e releitura de necessidade; confirmação idempotente para compra; filtros e detalhe; preservação de erro no diálogo. Edição de política está disponível na API com concorrência otimista, mas ainda não ganhou acionador visual; dispensa está disponível na API com motivo, mas ainda não ganhou ação visual; seleção em lote e criação direta de transferência permanecem pendentes.

Verificação local: `dotnet restore/build/test` não puderam ser executados porque `dotnet` não está instalado; PostgreSQL e navegador também não estão disponíveis, logo migration, concorrência real, persistência por recarga e renderização desktop/mobile precisam ser homologadas em ambiente com SDK 10.0.100 e PostgreSQL. Foram executados checks estáticos de whitespace, referências e sincronização da migration com o SQL consolidado.
