# PECUÁRIA — Programa de Sprints P1–P10

Data: 2026-10-12 · Estado: **plano consolidado por auditoria de código** (nada aqui é funcionalidade executada; integra o roadmap COMPLEMENT à entrega Bloco A).
Entrada: inventário exaustivo do módulo existente (38 tabelas `agro360.livestock_*`, 3 serviços, 2 controllers, SPA `/livestock`, seeds, testes, integrações) realizado antes de qualquer mudança — critério de entrada do programa.

## 1. Estado atual (evidência da auditoria)

| Camada | O que existe hoje | Evidência |
|---|---|---|
| Schema | 38 tabelas + view `livestock_paddock_occupancy`; RLS em todas; migrations 006/068/105 | `database\agro360-postgres-full.sql` |
| Serviços | `LivestockService.cs` (707 ln: register/weigh/treat/detalhe), `LivestockHerdService.cs` (1829 ln: o mais rico — reconciliação, movements, handling orders, pesagens/correções, GMD, restrições, reservas→saída→financeiro, custos, relatórios, CSV), `Livestock360Service.cs` (196 ln: CRUD/workflow simples + dashboard ~18 KPIs) | `src\Modules\Agro360.Infrastructure\Services\` |
| Rotas | `/api/v1/livestock` (5 rotas) + `Livestock360Controller` (~55 rotas) com políticas `livestock.read/write/sell` | `src\Hosts\Agro360.Api\Controllers\` |
| UI | SPA em abas em `/livestock` (overview/animais/movements/handling/pesagens/alimentação/restrições/comercial/custos/relatórios); menu gated por `livestock.read`; KPI na home | `_Layout.cshtml`, `wwwroot\js\livestock.js` |
| Permissões | `livestock.read/write/sell`; grupo `livestock => ["livestock"]` em `ModulesForPermission`; módulo CORE `platform_modules` | `Permissions.cs` L20-22/L210 |
| Seeds (santa-clara) | 2 rebanhos (1 INDIVIDUAL, 1 QUANTITY×40), **4 animais** SC-N-1001..1004, catálogos mínimos, 3 weighings, 1 handling order IN_PROGRESS, 1 reserva ACTIVE (animal 1001 → status 6), cadeia de insumo de ração | full.sql L4077–4413 |
| Testes | Somente regression de scan-fonte (Arch) + contratos por reflexão (Unit). **Nenhum E2E exercita as rotas de pecuária** | `tests\*` |
| i18n | Strings de área/menu em 4 idiomas; **texto de ajuda e vários labels de `livestock.js` hardcoded pt-BR** | `agro-i18n.js`, `livestock.js` |
| Outbox | `AnimalRegistered/Weighed/Treated` produzidos; worker consome genéricamente; **nenhum handler específico** | `LivestockService.cs` L126/217/396, `OutboxWorker.cs` |

### Status por domínio (base para as lacunas de cada sprint)

| Sprint | Domínio | Status atual | Lacunas principais vs regras do programa |
|---|---|---|---|
| P1 | Rebanho/identificação | **IMPLEMENTADO (núcleo)** | `livestock_identifier_history` é schema-only (mudança de tag/RFID sem histórico canônico — só `audit_logs`); status 5 com rótulos divergentes (`DISCARDED` × `SLAUGHTERED`) entre serviços; separação conhecido×estimado não explicitada; sem E2E de jornada |
| P2 | Entradas/saídas/movimentações | **IMPLEMENTADO (núcleo)** | Idempotência de movimento de animal guardada na coluna `notes` (frágil); `reason_code` livre sem validação contra catálogo; "saída/trânsito/recebimento" como etapas distintas inexistentes (TRANSFER é evento único — interno ≠ venda já respeitado via reservas); responsável no registro a conferir |
| P3 | Pesagens/desempenho | **IMPLEMENTADO** | Zero intervalo ⇒ sem indicador a confirmar em `PerformanceAsync`; campo de equipamento/responsável/precisão a conferir no DTO; valores suspeitos já têm `review_required` via `weight_limits` ✓ |
| P4 | Sanidade/agenda | **PARCIAL** | Sem motor de agenda/calendário; `livestock_health_protocols` sem seeds e sem consumidor funcional (só catálogo CRUD); protocolo versionado não ligado a execução; follow-up existe (`next_application`) e alertas via restrições/carência ✓ |
| P5 | Pastagens/piquetes | **PARCIAL** | Sem plano de rotação/repouso (`rest_days` existe mas sem engine); `livestock_locations` schema-only; "um local corrente por animal" a garantir na query; capacidade por parâmetro registrado existe (`capacity_au`) ✓; view de ocupação existe ✓ |
| P6 | Alimentação/confinação | **PARCIAL** | Sem distribuição diária automática por plano/rebanho; forrageira × matéria seca não distinguida (sem campo/conversão versionada); fornecido ≠ consumido ✓ (colunas separadas); baixa única de estoque ✓ (`Consume`) |
| P7 | Reprodução/ciclo de vida | **SÓ ESQUELETO** | Um único fluxo `AddReproduction`; sem diagnóstico por responsável, previsão×confirmação de parto, nascimento criando exatamente uma entrada de rebanho, links materno/paterno só quando conhecidos (FK auto-referente existe), perdas/abortos com histórico; zero seeds |
| P8 | Custos/valoração | **PARCIAL** | **Pecuária escreve na tabela legada `agro360.cost_entries` (source_type `ANIMAL_EVENT`) e NÃO no ledger canônico desta entrega `cost_management_entries`** (verificado: tabela separada, escritores distintos); `livestock_cost_allocations` só seed/só leitura; sem rateio documentado, custo/cabeça e custo/ganho sem denominadores válidos garantidos |
| P9 | Comercialização/transporte/rastreabilidade | **PARCIAL** | Ciclo reserva→saída física→confirmação financeira completo (dual model: legado `CommercialService` coexiste — risco de venda dupla a validar em runtime); transporte/frete **ausente** do módulo; divergência de entrega e critério de preço (cabeça × peso vivo) a implementar/validar |
| P10 | Leiteira/homologação | **PARCIAL** | Registro diário de leite com descarte/qualidade jsonb/alerta de carência; **sem** sessão de ordenha, tanque/lote, pipeline de homologação. Recomendação: entregar homologação completa do módulo corte; leiteira como próximo incremento explícito se a base for incompatível (confirma-se pela auditoria que a base é mínima) |

### Perguntas abertas (verificar em runtime durante os sprints)

1. Semântica oficial do status 5 (DISCARDED × SLAUGHTERED) — impacta relatórios/CSV.
2. `livestock_group_movements`, `livestock_locations`, `livestock_identifier_history`: dead schema ou reservadas ao roadmap (usar como canônicas quando o domínio exigir)?
3. `MoveAnimalAsync` aceita `reason_code` free-text — validação contra `livestock_movement_reasons` em runtime?
4. Coexistência dos dois modelos de venda: mesma venda gera reserva + fatura legada? (concorrência sobre `reservation_id`, status 3/6)
5. Gating de entitlement 'livestock' em downgrade não coberto por teste.
6. Eventos `Animal*` do outbox chegam a algum consumer/webhook configurável?
7. Descrição do módulo na plataforma ("interface operacional completa permanece pendente", FOUNDATION) parece defasada frente à SPA atual.

## 2. Regras transversais de cada sprint (invariáveis)

- Autenticação autorizada no servidor: tenant / módulo contratado / perfil / unidade (farm) — nenhuma projeção ou comando sem revalidação no backend.
- Menu de entrada correto; PT/EN/ES/FR nas telas tocadas; textos claros + seletores nomeados + seção "Como usar".
- Validação e confirmação de ações sensíveis; feedback explícito de persistência; história + auditoria em toda mutação.
- Concorrência (locks/versão), idempotência (replay sem duplicar efeito), migrações incrementais protegendo dados existentes.
- Fontes canônicas únicas (ledger de inventório para insumos; ledger de custos canônico; um local corrente por animal; sem balanços/status paralelos).
- Números em decimal com casa fixa; unidades explícitas; vazio ≠ zero; memória de cálculo em indicadores.
- Nunca inventar preços, dosagens, conversões ou sucesso de integração.

## 3. Portas por sprint (todas, sem exceção)

1. Build Release + Arch + Unit green (`rebuild-gates.ps1` → `GATES_DONE build=0 arch=0 unit=0`).
2. E2E comportamental da jornada do sprint em PG descartável (scripts `verify-livestock-p<N>-e2e.ps1` no padrão dos existentes), com efeitos de persistência provados por psql (API == DB).
3. Acesso permitido/negado: outro tenant 404/403, outra farm fora de escopo 403, papel sem `livestock.read/write/sell` negado, módulo suspenso negado.
4. Concorrência + idempotência: dois comandos simultâneos → um efeito; replay → sem duplicar.
5. Validação visual autenticada em 360/768/1440 px + zoom 200% + 4 idiomas nas telas alteradas (**em 2026-10-12 BLOQUEADA pelo certificado TP `C05DA1CAA96A04FDF9EB850D2281AB150531747F` não confiável no desktop — registrar BLOCKED, não silenciar**; desbloqueio: duplo clique em `C:\Users\NCELL-DEV-020\Desktop\Confiar-cert-agro360.bat`).
6. Matriz/jornadas/perfis/menus atualizados; resultado registrado como aprovado/falhou/bloqueado/não-executado.
7. Regressões preservadas a cada sprint: Bloco A journey E2E (`verify-agro-journey-e2e.ps1`), Compras (`verify-procurement-journey-e2e.ps1` 12/12), recuperação de inspeções (`verify-inspection-recovery-e2e.ps1`).

## 4. Ordem de execução

Dependência: P1 → P2 → (P3, P4, P5, P6 em paralelo conceitual, sequenciais na entrega) → P7 → P8 → P9 → P10. Cada sprint: **auditar o estado vigente primeiro** (o módulo evolui entre sprints), reaproveitar estruturas, implementar apenas lacunas, nunca recriar o que já existe.

Antes do programa começar: concluir a slice atual (Bloco A §3–§6 + migs 137/138, G/H/I verdes) e o Bloco B (§7–§8 lote disponível → venda + resultado comercial), que funda a base comercial usada por P9.

## 5. Entrega final do programa (critérios)

Capacidades reaproveitadas (esta auditoria) × implementações novas por sprint; regras e fontes técnicas adotadas (com autoria/revisão responsável quando técnicas); jornadas homologadas com evidência API/DB/UI por sprint; instruções de migração/homologação; pendências e limitações nomeadas; próximo incremento prioritário após P10.
