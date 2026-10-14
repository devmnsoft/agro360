# Checkpoint de continuidade

- Repositório: `devmnsoft/agro360`.
- Base inicial: branch `main`, commit `4b650fb74fc6d73050a5d84d0540f3962bf75f8c`.
- Alteração preexistente preservada: `.github/copilot-instructions.md` não rastreado.
- Configuração canônica: `ConnectionStrings:Agro360`; senha separada opcional em `PostgreSql:Password`; passfile em `PostgreSql:Passfile`.
- Banco: migration `064_procurement_receipt_integrations.sql` e instalador completo atualizados.
- Validação local: build sem avisos; 110 testes passaram; 4 testes de PostgreSQL foram ignorados pela ausência de `AGRO360_TEST_CONNECTION_STRING`.
- Cenário HTTP sem banco: liveness 200, readiness 503, login 503 com código/TraceId e refresh inválido 401 com código/TraceId.
- Navegador: Compras, o formulário integrado de recebimento e SaaS renderizaram; sem API/banco, as páginas exibiram indisponibilidade e tentativa manual, sem sucesso falso.
- Qualidade mecânica: JavaScript, 640 rotas, SQL consolidado, `git diff --check` e formatação dos C# alterados aprovados. A verificação global de formato segue bloqueada pela dívida preexistente de finais de linha CRLF fora deste escopo.
- Bloqueio externo: não há segredo PostgreSQL, passfile, `AGRO360_TEST_CONNECTION_STRING` ou container ativo nesta máquina. Configure localmente e reinicie os hosts antes da homologação real.

Próximo prompt: `Configure o segredo PostgreSQL local e execute a homologação real de migration, login/refresh, SaaS e recebimento idempotente.`

---

## Atualização — incremento 2026-10-11 (baseline `main @ dddc0bc`)

- **Núcleo agrícola (Bloco A) — 59/59 APROVADO em runtime** (E2E em cluster PG18 descartável + API Release + PostgreSQL próprios): A ledger canônico (1 efeito/consumo, replay sem re-decremento, conflito por chave, estorno válido→estoque devolvido, estorno duplicado 409); B máquina de estados (version guard + terminais irreversíveis); C escopo/tenant (2 tenants × 2 fazendas, 403 fora de escopo); D planejamento (grafo de dependências, liberação fora de sequência, reschedule sem propagação, geração concorrente→ordem única); E colheita→lote disponível (teto multi-pass, bruto/tara c/ justificativa, inspeção, destinação PRODUCTION); F resultado gerencial (null≠zero, custo da fonte reconhecida, custo/ha null sem área, escopo por fazenda). Evidência `artifacts/agro-journey-e2e-ae861b9fb6f74d609fc16eaf0e43d776`.
- **Recuperação automática de inspeções (§3) — APROVADO** (worker limpo; S1–S7). Evidência `artifacts/inspection-recovery-e2e-afc2e394c0f94a1faad4491221edcf7d`.
- **Regressão Compras preservada — 12/12** após as mudanças de código compartilhado (run `procurement-journey-e2e-2381aa5ed86b450c97d7fc1c44372864`).
- **Defeitos de produto corrigidos na causa raiz**: sweep de row-DTO (9 classes → `{ get; init; }` + alias PascalCase), Bug 1 coluna `procurement_receipt_items.work_order_material_id` (mig 136), Bug 2 join `inventory_products.base_unit` no fechamento, Bug 3 separador de escopo (`OperationalScopePolicy`), Bug 4 estorno via `reversed_amount` (evita `recognized_amount<0`).
- **Migrations**: 135 (schema 11.25.0 — recuperação de inspeções) + 136 (`work_order_material_id` nullable, sem FK); `full.sql` atualizado.
- **Gates**: build Release 0/0; architecture 187/187; unit 390/390.
- **Bloqueado (ação única do usuário)**: homologação **visual** no browser — confiar o self-signed (thumbprint `C05DA1CAA96A04FDF9EB850D2281AB150531747F`) na loja `Root` via `Confiar-cert-agro360.bat`; homologação visual não declarada enquanto o cert não estiver confiável.
- **Sem push/merge/publicação nesta entrega.** O trabalho de produto do Bloco A (Bug 1–4, sweep de row‑DTO, mig 136) e os docs desta atualização seguem **não commitados** sobre `HEAD = c215af7` (que já contém o §3, commitado em 2026‑10‑11 21:10; baseline original do incremento = `dddc0bc`). Artigos completos: matriz `docs/AGRO-JOURNEY-MATRIX-2026-10-11.md` (§14 entrega aprovado/falhou/bloqueado/não-executado); limites `docs/LIMITES.md`; rastreabilidade `docs/TRACEABILITY-MATRIX-v0.2.0.md`.

Próximo prompt: `(1) Confiar o certificado na loja Root e preencher a matriz visual (4 idiomas × 360/768/1440 px + zoom 200%); (2) Central de Trabalho do núcleo agrícola (tipos FIELD_BLOCKED / RESOURCE_UNASSIGNED / CUSTODY_PENDING / INSPECTION_PENDING / COST_PENDING); (3) governança SaaS (contrato de módulo no SaveRoleAsync + superfície de UI para estado suspenso).`

---

## Atualização — incremento 2026-10-12 (baseline `main @ 79b59c9`)

- **Resultado líquido confiável da safra (§3) — APROVADO (E2E cenário G)**: netting bruto/estornado/líquido em detalhe/lista/overview/CSV/fechamento/dashboard com memória de cálculo; estorno parcial + total com guarda cumulativa (`agriculture.reversal_exceeds_remaining` / `reversal_duplicate`); evento REVERSAL canônico único por consumo via upsert; reconciliação api==db (overview 3000|5542|750; dashboard bruto 8542 / líquido 5542).
- **Central de Trabalho agrícola (§4) — APROVADO (E2E cenário H)**: ocorrências `CUSTODY_PENDING` / `RESOURCE_UNASSIGNED` / `INSPECTION_PENDING` / `COST_PENDING` com origem, causa, unidade, prazo, impacto e próxima ação + deep link; permissão (`work.read`) e escopo antes da projeção (farmB 403 / farmA 200); `view`/assignment sem duplicata em reenvio; resolver a origem tira da central; atribuição ≠ aprovação (ordem PLANNED); isolamento entre tenants.
- **Interface de recuperação de inspeções (§5) — APROVADO (E2E H3 + suíte dedicada revalidada)**: situação × prioridade × próxima ação na central; re-disparo exige motivo (422 `inspection.redispatch_reason_required`) e é auditado com ator; não duplica intent; `situation`/`canRedispatch` api==db; suíte S1–S7 passa de novo.
- **Governança de papéis × contratação (§6) — APROVADO (prova API, E2E I)**: `SaveRoleAsync` nega papel acima do contratado com mensagens **distintas** ("módulo não contratado: {permissão}" × "módulo suspenso: {módulo}"); suspensão preserva perfis e nega acesso efetivo no runtime; reativação libera gravação/acesso.
- **Migrations 137 + 138** (`cost_reversal_net_integrity` + `receipt_item_work_order_material_fk`) + `full.sql`; gates de setup do E2E (presença das versões {11.25.0, 137.0.0, 138.0.0} + idempotência real). **Upgrade `agro360_homolog` documentado: 136 → 137 → 138** (pré-validação read-only; ainda não aplicado).
- **Gates**: build Release + architecture 187/187 + unit 390/390 (`GATES_DONE build=0 arch=0 unit=0`) após as correções finais.
- **Jornada E2E ampliada (cenários A–I): 103 PASS / 0 FAIL / 0 BLOCKED — APROVADO** (`artifacts/agro-journey-e2e-b28d889875594792b694c610ecf5b21a`). Regressões: Compras **12/12** (`procurement-journey-e2e-668e9b3e…`) e recuperação de inspeções **S1–S7** (`inspection-recovery-e2e-683b5aba…`).
- **Programa Pecuária P1–P10 registrado** com auditoria de entrada concluída (38 tabelas `agro360.livestock_*`, 3 serviços, ~60 rotas, seeds, testes; status/lacunas por sprint): `docs/PECUARIA-PROGRAMA-P1-P10.md`.
- **Sem push/merge/publicação**; todo o delta desta entrega segue **não commitado** sobre `main @ 79b59c9`.
- **Bloqueado (ação única do usuário)**: homologação **visual** — confiar o certificado `C05DA1CAA96A04FDF9EB850D2281AB150531747F` via `Confiar-cert-agro360.bat` (browser somente via `https://192.168.0.10:7080`).

Próximo prompt: `(1) Bloco B (§7–§8): lote disponível → venda + resultado comercial (auditar os agregados comerciais existentes; evoluir sem criar módulo paralelo; memória de cálculo; reconciliação api==db); (2) iniciar o programa Pecuária em P1 (rebanho/identificação) com re-auditoria do estado vigente e portas por sprint; (3) assim que o certificado for confiável, preencher a matriz visual (4 idiomas × 360/768/1440 px + zoom 200%) incl. portas visuais de cada sprint de Pecuária.`
