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
