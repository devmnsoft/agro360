# Qualidade e Compliance — Sprint 28

A Central usa requisitos parametrizados por tenant; o Agro360 não presume limites ou normas. Cadastre área, severidade, aplicabilidade, validade, responsável, evidências e se a falha crítica bloqueia o lote. Relacionamentos são selecionados nos catálogos autorizados do tenant, nunca digitados como UUID.

## Operação

1. Cadastre e ative o requisito; requisitos inativos não abrem pendências.
2. Publique uma especificação vigente para o produto. A edição de versão ativa deve criar versão nova, preservando inspeções anteriores.
3. Abra a inspeção, selecione produto/lote e registre todos os parâmetros obrigatórios. Resultado crítico fora da tolerância impede aprovação e origina não conformidade/hold.
4. Bloqueio, quarentena, reprovação e cancelamento exigem motivo; liberação exige `compliance.approve` e gera histórico auditável.
5. Na não conformidade, registre causa raiz e CAPA com responsável/prazo. O encerramento exige ações obrigatórias concluídas e evidência ou justificativa.
6. Auditorias exigem escopo e checklist; achado crítico gera não conformidade. Relatório reprovado exige motivo.

## Beneficiamento e exportação

Configure por produto as etapas, tempo/temperatura mínimos, checklist, foto, evidência, responsável e aprovação. O exemplo tucupi deve ser configurado pela organização; nenhum limite normativo é fornecido implicitamente. A prontidão de exportação agrega requisitos, inspeção, lote, documentos e evidências: item obrigatório pendente mantém o dossiê bloqueado e lote retido/reprovado impede certificado.

## Homologação

Valide perfis de leitura, escrita e aprovação; isolamento de tenant; histórico de decisões; arquivo protegido; CSV; telas vazias; teclado; contraste; desktop, tablet e celular. Teste também venda, expedição e certificado com lote bloqueado.

## Evolução 9.0 — Central de qualidade e CAPA (2026-09-16)

### Estado encontrado e matriz de entrega

| Funcionalidade | Implementação encontrada | Dependência reutilizada | Alteração desta entrega | Critério de conclusão |
|---|---|---|---|---|
| Central | dashboard genérico, sem fila CAPA | permissões `compliance.*`, inspeções e documentos | indicadores reais para inspeções, casos, restrições, ações, verificações, reaberturas e documentos; filtros e navegação acessível | consulta falha explicitamente e cada indicador direciona à área de origem |
| Caso de qualidade | não conformidade simples ligada a `compliance_subjects` | usuários, produtos, lotes, evidências e auditorias existentes | número legível, descrição, base de quantidade, identificação, idempotência, versão, exclusão lógica e vínculos de origem | tenant + chave de idempotência impedem repetição sem impedir ocorrências distintas |
| Tratamento | estados legados e fechamento direto | autorização `compliance.approve` | estados OPEN → ANALYSIS → IN_TREATMENT → AWAITING_VERIFICATION → CLOSED, cancelamento e reabertura validados | fechamento requer ações obrigatórias e verificação eficaz |
| Contenção | decisão sobrescrevia o status do lote | `storage_lots` e histórico de decisões | restrições independentes, autoria, motivo e liberação individual; status só volta a disponível sem restrição remanescente | dois motivos coexistem e liberar um não remove o outro |
| Análise | causa raiz sobrescrita no caso | evidências existentes | versões append-only com hipótese separada de causa e conclusão inconclusiva justificada | nenhuma hipótese é apresentada como diagnóstico confirmado |
| Ações | texto único no caso | usuário ativo do tenant e mecanismo de tarefas por vínculo opcional | plano tipado, status, resultado, evidência, concorrência e histórico de prorrogação | cancelada não equivale a concluída; prazo anterior permanece |
| Eficácia | ausente | evidências e usuários | critério, período, resultado, responsável e snapshot das ações | ineficaz/inconclusiva mantém tratamento pendente; mudança relevante invalida avaliação |
| Rastreabilidade | vínculos pontuais de auditoria/lote | recebimento, campo, produção, expedição, entrega, devolução e pós-venda | vínculos tipados sem fundir o status dos processos | lacunas permanecem explícitas e nenhum movimento/comunicação é inferido |

### Regras operacionais

A descrição registra **o problema**; contenção limita o impacto imediato; correção elimina o efeito observado; ação corretiva trata a causa para reduzir recorrência. Encerrar o tratamento administrativo não libera lote, não encerra tarefa, não aprova ajuste financeiro e não repete devolução ou movimento de estoque. A liberação exige `compliance.approve`, motivo e revalidação de todas as restrições ativas.

A migration `090_quality_nonconformity_capa.sql` é incremental, mantém a tabela de não conformidades existente como identidade do caso, adiciona eventos, análises versionadas, ações, histórico de prazos, verificações, restrições e vínculos. Os objetos permanecem no schema `agro360`, recebem RLS de tenant e não usam exclusão física.

### Manual resumido

1. Na Central, filtre o período e abra a fila correspondente.
2. Confirme a origem real e a base da quantidade; lote é opcional quando não há material.
3. Antes de conter, confira lote, tipo e alcance. Cada motivo deve ser liberado separadamente.
4. Registre hipótese como hipótese; confirme causa somente com evidência suficiente ou declare análise inconclusiva com justificativa.
5. Execute ações obrigatórias, registrando resultado. Para prorrogar, informe o motivo.
6. Verifique a eficácia com critério e período coerentes. Resultado ineficaz volta o plano ao tratamento; inconclusivo continua pendente.
7. Antes de encerrar, confira ações, evidências, verificação, restrições e pendências externas. Restrições podem permanecer quando a política autorizar.

### Evidências e pendências

A inspeção estática confirmou migração e instalador consolidados, regras de transição no domínio, liberação independente no serviço e controles responsivos/teclado na página. O ambiente desta execução não possui o SDK .NET nem PostgreSQL/`psql`; restore, build, testes, Swagger, inicialização e cenários transacionais em base isolada continuam gates obrigatórios, e não são declarados como executados. A integração de cada serviço de expedição/reserva à função `compliance_lot_has_active_restriction` deve ser homologada antes de produção; o vínculo opcional `task_id` evita criar uma segunda central, mas a sincronização com tarefas existentes permanece uma integração explícita, sem atualização circular automática.

## Evolução 9.1 — verificação de eficácia rastreável

A verificação passou a ser uma decisão própria, posterior à execução. Ela registra tipo e versão do critério, método, responsável, prazo, período de observação, evidências exigidas, referência e medição quantitativas estruturadas, resultado observado, conclusão, data e autor do contexto autenticado. Os resultados persistidos continuam sendo `EFFECTIVE`, `INEFFECTIVE` e `INCONCLUSIVE`; os textos exibidos são traduzidos somente na interface.

### Como usar

**Finalidade.** Comprovar se o conjunto de ações produziu o efeito esperado sem confundir execução, avaliação, confirmação de eficácia e encerramento.

**Pré-requisitos.** O caso deve estar em `AWAITING_VERIFICATION`, as ações obrigatórias devem refletir sua execução real, o verificador precisa de `compliance.approve` e a versão exibida deve continuar atual. Quando `compliance_parameters.segregateEffectivenessVerifier` estiver habilitado, quem concluiu uma ação do caso não pode decidir sua eficácia.

**Etapas.** Abra a não conformidade, confira plano, bloqueios e histórico; escolha critério qualitativo, quantitativo ou documental; informe método, responsável e prazo; registre período, evidências necessárias e observação; confirme a conclusão. Critério quantitativo exige unidade, operador, referência e medição. Percentual exige base positiva. A interface envia uma chave idempotente e o servidor revalida tenant, estado e versão dentro da mesma transação.

**Resultado esperado.** A avaliação é acrescentada ao histórico com snapshot das ações. `EFFECTIVE` habilita a análise de encerramento, mas não encerra o caso nem libera lote; `INEFFECTIVE` sinaliza revisão do plano; `INCONCLUSIVE` mantém acompanhamento. Uma nova avaliação não altera decisões anteriores.

### Recorrência, fechamento e limitações

A consulta de possíveis recorrências usa somente produto, lote, unidade, classificação, processo, causa informada e período dos dados autorizados do tenant. A tela mostra o critério consultado, não une ocorrências e não interpreta ausência de candidatos como prova de ausência de recorrência. O vínculo entre casos requer justificativa e preserva ambos os casos.

O fechamento volta a calcular no servidor causa, evidência/justificativa, ações obrigatórias e última eficácia, valida a matriz central de transições e usa versão otimista. Restrições de lote continuam independentes; nenhuma avaliação ou transição as libera. A migration incremental `091_quality_effectiveness_verification.sql` amplia os registros existentes sem excluí-los e adiciona vínculos justificados com RLS.

## Evolução 9.2 — Modelos de inspeção, checklists versionados e execução guiada (2026-09-16)

### Estado encontrado

| Capacidade | Antes desta entrega | Nesta entrega |
|---|---|---|
| Especificação por produto (`quality_specifications`) | Numérica/min-max, usada na colheita | Preservada; não substituída |
| Inspeção operacional (`quality_inspections`) | Conclusão pontual sem checklist guiado completo | Ponte opcional `model_version_id`/`run_id` |
| CAPA / restrições / tarefas | Central 9.0–9.1 | Reutilizada pelos efeitos da inspeção |
| Modelos configuráveis por processo | Ausentes | Catálogo + versões + seções + critérios |
| Execução com rascunho/concorrência | Parcial na colheita | Runs com `row_version`, `last_saved_at` e progresso |
| Programação periódica | Ausente para qualidade | Schedules + `InspectionScheduleWorker` |

### Regras implementadas

1. Versão publicada é imutável; alteração gera novo rascunho (cópia com `stable_key` preservado).
2. Publicação supersede a versão PUBLISHED anterior; vigência auditada em `quality_inspection_model_audits`.
3. Seleção por processo + especificidade (produto > categoria > unidade) + precedência; empate sem política → ambiguidade explícita (sem escolha arbitrária).
4. Ausência de modelo obrigatório gera pendência, nunca aprovação automática.
5. Resposta ausente ≠ zero; N/A só quando permitido e com justificativa quando exigida; unidades incompatíveis não são comparadas.
6. Critério crítico reprovado não é compensado por média/pesos; N/A fica fora da base ponderada.
7. Resultado (`CONFORMING` / `NON_CONFORMING` / `INCONCLUSIVE`) é calculado no backend e lista os critérios determinantes.
8. Efeitos (NC, restrição, ação, revisão) são idempotentes por `(run, criterion, effect_type)` e não liberam restrições independentes.
9. Reinspeção não sobrescreve a concluída; preserva histórico e pode usar nova versão publicada.
10. Geração periódica usa chave `schedule_generation_key` e não cria backlog ilimitado quando `allow_catchup=false`.

### Telas e API

- Página `/Inspections` (Modelos, Inspeções, Execução, Resultado, Programações) com ajuda contextual.
- API `api/inspections/*` com permissões separadas: `compliance.inspection-models.write`, `compliance.inspection-models.publish`, `compliance.inspections.execute` (liberação de material permanece em `compliance.approve`).
- Worker `InspectionScheduleWorker` no host existente (sem mecanismo paralelo).

### Migração

- Incremental: `092_quality_inspection_models.sql` (schema `agro360`, RLS, soft-delete, versão de schema `9.2.0`).
- Instalador consolidado atualizado em `database/agro360-postgres-full.sql`.

### Manual resumido

1. Cadastre o modelo (código, processo, precedência, categoria/unidade quando couber).
2. Monte seções/critérios no rascunho; envie para revisão; publique com vigência.
3. Inicie a inspeção pelo contexto; se houver ambiguidade, escolha autorizada ou bloqueio claro.
4. Salve rascunhos (só confirma “salvo” após resposta do servidor); conclua somente com obrigatórios respondidos.
5. Trate NC/restrições pelos fluxos já existentes da Central; nova inspeção aprovada não remove bloqueios independentes.

### Evidências e pendências

- Restore/build Release: êxito (0 avisos/erros).
- Testes: `ComplianceFormTests` (7) e `UnitTests` (11) aprovados; suíte completa de Architecture ainda contém falhas pré-existentes (`HarvestClosing…`, `ProcessCentral…`) e a falha local de `DefaultConnection` nos `appsettings` do usuário (não commitados).
- Integração PostgreSQL limpa/incremental, Swagger autenticado, navegador desktop/mobile e cenários transacionais 1–15 da especificação: **não homologados nesta máquina nesta entrega**; gates obrigatórios antes de produção.
- Integrações de ponta a ponta com recebimento de compra, produção industrial, armazenagem, expedição e devolução: ganchos operacionais por evento implementados via `IOperationalInspectionTrigger` na entrega AG-Q-EVT-001.

### Evolução AG-Q-EVT-001 — Gatilhos operacionais por evento (2026-09-18)

- **Contrato e Serviço**: `IOperationalInspectionTrigger` / `OperationalInspectionTrigger`.
- **Ganchos Conectados (pós-commit do fato de origem)**:
  - `HarvestService.ReceiveAsync` → `HARVEST_RECEIPT`, originId = `production_receipts.id`.
  - `LogisticsService.ReceiveReturnAsync` → `RETURN`, originId = `fulfillment_return_receipts.id`.
  - `ProcurementService.ReceiveAsync` → `PURCHASE_RECEIPT`, originId = `purchase_receipts.id`.
  - `IndustrialProductionService.RegisterOutputAsync` → `PRODUCTION`, originId = `industrial_batches.id`.
- **Idempotência**: Chave única `EVT:{process}:{originId:N}` e hash estável SHA-256 do payload.
- **Tabela de Intents**: `agro360.quality_inspection_event_intents` (Migration incremental `093_quality_inspection_event_intents.sql`, schema `9.3.0`, RLS habilitado, grant `agro360_app`).
- **Estados do Intent**: `PENDING`, `STARTED`, `AMBIGUOUS`, `PENDING_MODEL`, `SKIPPED_NO_ACTOR`.
- **Regras Operacionais**:
  - O gancho roda **estritamente após o commit** da transação de origem; falhas de modelo ou técnicas nunca desfazem o recebimento/apontamento.
  - O recebimento mantém o material em `AWAITING_INSPECTION`; o gancho não move para `AVAILABLE` e não destina devolução.
  - Sem modelo → `PENDING_MODEL` (nunca aprova automaticamente).
  - Empate de modelos → `AMBIGUOUS` sem run arbitrária.
  - Sem usuário/inspetor → `SKIPPED_NO_ACTOR`.
  - Modelo único → `STARTED` com início de run via `StartRunAsync` (modo `AUTOMATIC`).
- **API e UI**:
  - `GET /api/inspections/event-intents`.
  - `/Inspections`: breadcrumb `Qualidade / Modelos e inspeções`, `@section ScreenHelp`, aba de gatilhos por evento com status em português.
- **Homologação E2E (AG-Q-092-E2E — 2026-09-19 / 2026-09-20)**:
  - Suíte dos 15 cenários operacionais de modelos e gatilhos de inspeção homologada com **100% PASS** em cluster PostgreSQL efêmero descartável.
  - Validados: instalação limpa e upgrade incremental (9.2.0 e 9.3.0 com RLS), imutabilidade de versões publicadas, gatilhos de colheita e devolução, obrigatoriedade de critérios críticos (HTTP 422), reprovação crítica gerando restrição/NC, reinspeção com pai, ambiguidade, ausência de modelo, idempotência de replay e de agenda periódica, governança multi-tenant e responsividade mobile.
  - Relatório de evidências: `docs/execucao/AG-Q-092-E2E-EVIDENCE.md`.
  - Próximo recorte: `AG-E8-RET-001`.
