# Plano de execução Agro360

## Recorte concluído — Destinação e Qualidade de Retorno + Feedback Global AG-E8-RET-001 (2026-09-21)

1. **Concluído no código:**
   - Conferência de retorno físico (`ReceiveReturnAsync`) retendo o recebimento em `AWAITING_QUALITY` e disparando evento operacional pós-commit (`IOperationalInspectionTrigger`) sem auto-aprovação de lote.
   - Destinação final estritamente obrigatória (`DecideReturnAsync`) com validação de regras de qualidade em `StorageRules`: laudo `CONFORMING` não libera o lote de forma presumida; estados `NON_CONFORMING`, `INCONCLUSIVE`, `PENDING_MODEL`, `AMBIGUOUS` ou inspeções em andamento bloqueiam sumariamente `RELEASE`.
   - Perda física (`DISPOSE`) com quantidade, unidade e motivo obrigatório; custo opcional e desacoplado de inferências fiscais ou títulos financeiros duplicados.
   - Idempotência (`IdempotencyKey` + `ReturnDecisionExistingRow`) e controle de concorrência otimista (OCC).
   - DDL incremental `094_fulfillment_return_disposition.sql` (schema 9.4.0) e consolidação no full SQL, preservando 071, 092 e 093 imutáveis.
   - Contrato padronizado `window.agro360Feedback` exposto em `agro360.js`, com proibição estrita de `alert()` e SweetAlert, distinção `401 ≠ 403 ≠ rede`, expurgo de GUIDs em erros de tela, e integração nas jornadas de `/Logistics`, `/Inspections` e `/Harvest`.
2. **Evidências:** `dotnet build -c Release` PASS (0 erros/avisos), `dotnet test -c Release` 61/61 PASS, `node --check` PASS e `git diff --check` PASS.
3. **Próximo recorte ativo pós-merge (estrito):**
   - 1º `AG-E6-GEN-001` (Genealogia completa da safra até produção e expedição)
   - 2º `AG-E1-002` (MFA real, elevação SuperAdmin e suporte assistido auditado)

## Recorte concluído — Homologação E2E de Inspeções e Gatilhos AG-Q-092-E2E (2026-09-19 / 2026-09-20)

1. **Homologado com PASS (15/15 cenários em cluster efêmero PostgreSQL descartável):**
   - Instalação limpa Full SQL e upgrade incremental 9.2.0 → 9.3.0 sem perda de dados e com RLS forçado.
   - Criação de modelos, versionamento, critérios críticos e imutabilidade de versões publicadas (PUT rejeitado).
   - Gatilhos de colheita (`HARVEST_RECEIPT`) e devolução (`RETURN`) com retenção em inspeção (`AWAITING_INSPECTION` / `AWAITING_QUALITY`), criação de runs automáticas e sem destinação indevida de lotes.
   - Tratamento estrito de ausência de obrigatórios (HTTP 422), reprovação crítica com veredito `NON_CONFORMING`, restrição e caso na Central CAPA.
   - Reinspeção vinculada à run pai, ambiguidade de modelos (`AMBIGUOUS`), ausência de modelos (`PENDING_MODEL`).
   - Idempotência de replay operacional e de agendas periódicas (`schedule_generation_key`).
   - Governança e autorização multi-tenant (403 para usuários sem permissão; 404 e lista vazia para outros tenants).
   - Acessibilidade e responsividade da UI `/Inspections`: skip-link com foco CSS, Breadcrumb humano, ScreenHelp expansível e aba "Gatilhos por Evento" com status em português.
2. **Evidências:** documentadas integralmente em `docs/execucao/AG-Q-092-E2E-EVIDENCE.md`.
3. **Próximo recorte ativo pós-merge (estrito):**
   - 1º `AG-E8-RET-001` (Recebimento, conferência e qualidade definitiva de devolução/retorno)
   - 2º `AG-E6-GEN-001` (Genealogia completa da safra até produção e expedição)
   - 3º `AG-E1-002` (MFA real, elevação SuperAdmin e suporte assistido auditado)

## Recorte entregue — Gatilhos operacionais de inspeção por eventos AG-Q-EVT-001 (2026-09-18)

1. **Concluído no código:** contrato e serviço `IOperationalInspectionTrigger`, migration incremental `093_quality_inspection_event_intents.sql` + consolidado (092 imutável), ganchos pós-commit em `HarvestService.ReceiveAsync` (HARVEST_RECEIPT), `LogisticsService.ReceiveReturnAsync` (RETURN), `ProcurementService.ReceiveAsync` (PURCHASE_RECEIPT) e `IndustrialProductionService.RegisterOutputAsync` (PRODUCTION); endpoint `GET /api/inspections/event-intents`; UI `/Inspections` com breadcrumb, `@section ScreenHelp`, aba de gatilhos por evento com status em português; ADR-Q-EVT-01 e documentações.
2. **Ordem de continuidade pós-merge (estrita):**
   - 1º `AG-Q-092-E2E` (Validação E2E dos 15 cenários de inspeção de 092 em PostgreSQL descartável)
   - 2º `AG-E8-RET-001` (Recebimento, conferência e qualidade definitiva de devolução/retorno)
   - 3º `AG-E6-GEN-001` (Genealogia completa da safra até produção e expedição)
   - 4º `AG-E1-002` (MFA real, elevação SuperAdmin e suporte assistido auditado)
3. **Não criar:** segundo agendador, aprovação sem modelo ou exclusão física.

## Recorte ativo — modelos de inspeção 9.2 (2026-09-16)

1. **Concluído no código:** catálogo versionado, critérios tipados, publicação imutável, seleção por especificidade/precedência, execução guiada com rascunho/OCC, resultado backend, efeitos idempotentes em NC/restrição/ação, reinspeção, agendas + Worker, UI `/Inspections` e API `api/inspections`.
2. **Reutiliza:** Central CAPA 9.0–9.1, documentos/evidências, Outbox/Worker host, permissões `compliance.*` estendidas.
3. **Gate pendente:** instalação limpa/incremental PostgreSQL com 092/093, E2E autenticado dos 15 cenários (AG-Q-092-E2E).
4. **Não criar:** segunda central de NC, segundo agendador ou exclusão física.

## Recorte ativo — fechamento gerencial 7.7

1. **Concluído no código:** consolidação segura da safra, conferências quantitativas, custos vinculados, pendências acionáveis, execução idempotente, versões/snapshots e revisão retroativa.
2. **Decisão de integridade:** saldo atual e receita reconhecida ficam indisponíveis quando não há genealogia/política inequívoca; não há soma entre unidades nem edição de saldo.
3. **Gate pendente:** restore/build/test, PostgreSQL limpo e incremental, API autenticada, isolamento/autorização e navegador responsivo, pois este container não contém `dotnet`/`psql`.
4. **Próxima implementação:** vínculos confiáveis comercial/logística/custos e projeções do fechamento na Central; depois validar concorrência e dois tenants no banco descartável.


## Recorte ativo — estabilização logística 7.1

1. **Concluído no código:** schema aditivo, reservas concorrentes, conferência, saída física idempotente, tentativas parciais, recusa, registro inicial de retorno, consulta e indicadores.
2. **Gate bloqueado pelo ambiente:** provisionamento/login/MFA/refresh e instalação limpa/upgrade PostgreSQL (runtimes e connection string ausentes).
3. **Próxima implementação:** recebimento e decisão de qualidade do retorno, perdas com custo, disponibilidade/capacidade de viagem, documentos e rateio de frete.
4. **Somente depois:** cenário Santa Clara via operações de domínio e sincronização móvel com replay validado pelo servidor.


> Atualização de 2026-09-10 (merge + soft-delete): reconciliados contratos/serviços de pecuária e frota com marcadores commitados; instalador SQL limpo PASS com versões 6.8.0/6.9.0/7.0.0; exclusão lógica e autoria aplicadas às telas de pecuária/frota. AG-E8-003 e AG-E6-002 seguem **implementados não validados em E2E autenticado**. AG-E0-003 permanece com gate incremental obrigatório (`due_on` / `006z`/`007z` quando aplicável). Próximo recorte: logística confiável e, depois, MFA/assistência (AG-E1-002) / sync móvel (AG-E9). Estados em `../EXECUTION-CHECKPOINT.md` e `../TRACEABILITY-MATRIX-v0.2.0.md`.

Fonte aprovada: [plano mestre integral](AGRO360-MASTER-PLAN.md), incorporado em 2026-09-08. Requisitos não são evidências de implementação. Não reutilizar regras, namespaces ou diagnósticos do SIGOV-PLUS.

## Controles canônicos (sem duplicação)

- Matriz de funcionalidades: [TRACEABILITY-MATRIX-v0.2.0.md](../TRACEABILITY-MATRIX-v0.2.0.md), seção de consolidação atual; a tabela original é histórica.
- Checkpoint: [EXECUTION-CHECKPOINT.md](../EXECUTION-CHECKPOINT.md).
- Decisões: [DECISIONS.md](DECISIONS.md).
- Histórico de produto: [ROADMAP](../ROADMAP.md) e [SPRINTS](../SPRINTS.md), sem equivalência automática entre sprint e etapa E0–E10.

## Ordem e escopo desta versão

Concluir os fluxos existentes e os requisitos E0–E10 do mestre. Não acrescentar novos setores/provedores à versão sem decisão explícita. As linhas abaixo são entregas verificáveis; um módulo não é vendável porque possui controller ou migration. Cada ID mantém sua identidade nas próximas execuções.

| ID / prioridade | Entrega, atores e pré-condições | Estados, validações e efeitos | Dependência / aceite |
|---|---|---|---|
| AG-E0-001 / P0 | Operação: readiness real, API/Web, login no PostgreSQL | vazio/incompleto → 503; instalado → 200; leitura de metadados, sem DDL no health; liveness separado | `verify-e0.ps1`: instalação/reexecução, OpenAPI, login, dashboard, refresh/replay/logout e testes. Implementado e exercitado nesta rodada |
| AG-E0-002 / P0 | Usuário na tela de acesso: diagnóstico sem falso sucesso | rede falha → erro; health 200 exige `Healthy`; Swagger exige JSON OpenAPI; cache só shell público local; remover cache lookup legado | API indisponível nunca pode virar HTML 200. Regressão observada e corrigida no navegador |
| AG-E0-003 / P0 | Operação: caminho incremental canônico com dados anteriores | preservar checksums; mapear schemas legados → `agro360`; definir baseline sem registrar migration não executada | **Próxima entrega / defeito executado**: migration 007 falha com 42703 (`due_on` ausente; 001 define `due_date`). `-CheckMigrations` reproduz em banco independente. Não aplicar migrations em banco real por suposição |
| AG-E0-004 / P1 | Consolidação das alterações concorrentes | preservar autoria/diffs; reaplicar gates após última alteração; nenhum `git add .` | Não incluir automaticamente propriedades, SaaS, novas migrations/classes de teste produzidas por outra execução |
| AG-E1-001 / P0 | Pessoa e administradores: identidade global, vínculo e seleção do cliente | identidade/vínculo/organização ativos; CNPJ resolve organização, não pessoa; e-mail/CPF; bloqueio do vínculo isolado; queries/FKs/contexto autorizado | E0 estabilizada. Dois clientes, operador restrito, URL/API/exports negados; código atual é tenant-first |
| AG-E1-002 / P0 | MNSOFT: SuperAdmin único, MFA e suporte assistido | unicidade concorrente, MFA real, elevação, motivo/início/fim/ator real; último acesso protegido | AG-E1-001. Nenhum desafio MFA foi localizado; não considerar a coluna `mfa_enabled` implementação |
| AG-E1-003 / P0 | Administração cliente: usuários, perfis, convites e revogação | sem autoelevação/papel global/perfil estrangeiro; efeitos imediatos nos acessos; limites e último administrador | AG-E1-001. Há implementação concorrente de usuários: revisar, não duplicar; autorização de perfis/convites ainda exige auditoria |
| AG-E1-004 / P0 | Operação: provisionamento seguro e demo opt-in | senha local/entrada protegida, hash real, MFA confirmado, troca inicial, revogação e auditoria; produção sem demo | **Implementado, aguardando homologação PostgreSQL:** comando explícito valida fixtures e não roda em startup/seed. Executar login real antes de promover a validado |
| AG-E2-001 / P1 | Cliente/MNSOFT: catálogo → preço/pacote → aceite → contrato | decimal, snapshot, dependências, limites, vigência; contrato não equivale a permissão | E1. Unificar os três caminhos de entitlement hoje lidos no login; contrato antigo preserva preço |
| AG-E3-001 / P1 | MNSOFT/cliente: cobrança interna → conciliação → suspensão/regularização | pendente não é pago; retries/jobs idempotentes; exceções auditadas; separar financeiro rural | E2. Sem provedor não produzir PIX/boleto/split fictício; `ControlledPaymentSplitProvider` não é pagamento real |
| AG-E4-001 / P1 | Operador: cadastros estruturantes e estoque | fazenda/unidade/talhão/safra/parceiro/produto/depósito/lote; unidade coerente, saldo/reserva, compensação e concorrência | E1/E2; preservar trabalho concorrente de propriedades. Entrada → reserva → baixa → transferência → ajuste conciliados |
| AG-E5-001 / P1 | Comprador/vendedor/financeiro | cotação/alçada/pedido/recebimento parcial → estoque/título; proposta/reserva/entrega/recebível; baixa/estorno sem duplicação | E4. Unificar fornecedor `purchasing` vs `procurement`; validar rollback e segregação, não apenas CRUD |
| AG-E6-001 / P1 | Agrônomo/operador pecuário | planejamento → apontamento → consumo/custo/resultado; manejo/sanidade/reprodução com histórico; protocolos regulados não inventados | E4/E5. Agricultura, pecuária, leite/pastagens e especialidades já presentes; telas e regras devem ser exercitadas |
| AG-E7-001 / P1 | Produção/qualidade | receita versionada/aprovada → ordem → consumo → inspeção → lote; reprovação bloqueia expedição | E4/E5. Rastrear origem/destino e falha transacional sem divergência de estoque |
| AG-E8-001 / P1 | Logística/frota/fiscal | expedição parcial/entrega/prova; peças/manutenção/custos; documentos autorizados; fiscal solicitado ≠ autorizado | E4/E5/E7. **Frota/manutenção (fatia AG-E8-003) implementada não validada em E2E**; logística/expedição e fiscal externo seguem pendentes. Provedor fiscal não configurado permanece bloqueado |
| AG-E9-001 / P1 | Campo/analista/integrações | fila por tenant/dispositivo, reautorização, conflito/replay; CSV seguro, pt-BR/en/es, BI com fonte | E1 e fluxos de origem. Cache público E0 não entrega bootstrap offline seguro; projetar cache por identidade antes de reativar dados offline |
| AG-E10-001 / P1 | Operação e donos de módulos | suporte, RH/SST, portal, workflow/notificações, cooperativas, ESG, trading, IoT e BI; carga, acessibilidade e restore | Matriz inteira com critérios demonstrados ou retirada formal do escopo; jamais declarar sistema todo revisado por busca de arquivos |

## Como verificar e avançar

1. Registrar `git status --short`, branch/HEAD e diferenças desde o checkpoint; confirmar `origin` Agro360.
2. Executar `pwsh -File scripts/verify-e0.ps1 -PostgresBin 'C:\Program Files\PostgreSQL\18\bin'`. O script não usa o banco local configurado; cria cluster protegido e banco novos em `artifacts/` e encerra somente seus processos.
3. Para AG-E0-003, executar o migrator contra **outro banco descartável** com `ConnectionStrings__Agro360` e `--migrations database/migrations`; comparar objetos e dados com o instalador. Nunca usar a base do usuário para descobrir se a consolidação funciona.
4. Implementar uma entrega por vez, com Dapper/SQL, regras, autorização, endpoint, UI e ajuda onde aplicável; repetir cenários positivos, negativos e concorrentes.
5. Atualizar matriz e checkpoint citando ambiente/comandos, sem publicar segredo, hash, token ou capturas/binários.

Não houve autorização de push nesta rodada. A inclusão literal do mestre não autoriza infraestrutura de produção, comunicações ou cobranças externas.

## Próximo recorte após merge do PR #96 — 2026-09-09

- AG-E5-001/AG-E5-002: homologar em PostgreSQL descartável o recebimento integrado preservado do commit local e o pedido comercial com preço-base, desconto efetivo, snapshot e arredondamento por linha. Em seguida concluir reserva → entrega → recebível sem mudança fictícia de status.
- AG-E7-001: modelar o snapshot versionado do roteiro receita → etapas esperadas → ordem, gerar lote acabado operacional e validar consumo/reserva/qualidade com bloqueios efetivos.
- UI: completar jornada comercial de tabela de preços → cliente → pedido → análise/aprovação e exercitar a interação no navegador.

### Incremento AG-E6-002 — colheita e recebimento (2026-09-14)

Implementação vertical disponível na migration 075, API `/api/v1/harvest` e página `/Harvest`. Verificação estática concluída; build, banco descartável e E2E seguem como gates obrigatórios. Pendências ordenadas: (1) coleta dinâmica dos critérios e evidências na UI de Qualidade; (2) projeções canônicas na Central de Operações; (3) CSV autorizado com os mesmos filtros; (4) rateio/fechamento versionado; (5) homologação concorrente e móvel. Não iniciar beneficiamento/comercialização agrícola antes desses gates.

## Incremento AG-E6-003 — acompanhamento operacional da safra (2026-09-15)

- **Situação encontrada:** Agricultura 360 já possuía safra, planos genéricos em `agriculture_records`, ordens de campo, apontamentos, materiais, custos e fechamento de colheita. Faltavam operação planejada estruturada, cobertura parcial de ordens, dependências e uma leitura única por safra. O ambiente desta execução não contém o SDK .NET nem PostgreSQL; restore/build/test e instalação permanecem gates, não evidência concluída.
- **Implementado:** migration incremental 086 estende o plano existente com operações por talhão, dependências obrigatórias/orientativas sem ciclos, vínculo idempotente de ordens, cobertura/saldo e revisões imutáveis. A API valida tenant e contexto propriedade–safra–talhão, janela, concorrência e justificativa; ordens liberadas não são reescritas na reprogramação.
- **Acompanhamento:** `/Agriculture` consulta uma safra por nome e data de referência, mostrando área física separada da área trabalhada, operações, bloqueios, materiais, custos apropriados por moeda, produção e histórico. Ausência de origem é exibida como indisponível, sem conversão automática em zero.
- **Auditoria e autorização:** endpoints mantêm as políticas `agriculture.read/write`; autoria vem de `ITenantContext`; revisões guardam antes/depois, motivo e impacto nas ordens. As tabelas usam chave composta de tenant, RLS e exclusão lógica onde o registro pode ser desativado.
- **Conferência/fechamento:** esta visão não cria fechamento paralelo. O fechamento versionado existente em Colheita permanece canônico; pendências operacionais apontam para o registro de origem e são recalculadas em cada consulta.
- **Próxima etapa fundamentada:** homologar a migration 086 em banco descartável limpo e incremental, executar o fluxo autenticado com dois tenants e então acrescentar, no mesmo agregado, UI assistida para cadastrar/reprogramar operações e autorizar exceções de dependência conforme política delegável.

## AG-E6-GEN-002 — genealogia comercial e publicação pública

- **Entregue em código:** genealogia por safra/lote consolidada na UI, painel de pendências, snapshot público seguro, permissão específica, revogação motivada, migration 096 e testes unitários das regras/contrato.
- **Gate pendente:** o ambiente de 2026-09-21 não possui `dotnet` nem PostgreSQL; build/test/migration/E2E multi-tenant continuam obrigatórios e o recorte não está homologado.
- **Próximo recorte:** homologar em banco descartável e navegador nos quatro breakpoints; depois adicionar certificações publicáveis provenientes do cadastro canônico, sem texto simulado.

## AG-SaaS-COM-001 — incremento de administração comercial (2026-09-21)

Auditoria estática confirmou mecanismos SaaS canônicos existentes; o incremento 097 normaliza catálogo/dependências/entitlements de módulos, detalha cobrança gerencial e enriquece onboarding sem criar um segundo financeiro. O catálogo de planos deixou de ser público e regras unitárias cobrem valores decimais, evidência de baixa/cancelamento, progresso real, dependências e CSV seguro. Consulte `SAAS-COMMERCIAL-ADMIN.md`, `PLANS-MODULES-BILLING.md` e `ONBOARDING-ASSISTIDO.md`. SDK .NET e PostgreSQL não estão instalados neste ambiente, logo build, testes .NET, migration, RLS/E2E e validação visual permanecem pendentes; este registro não declara homologação.
