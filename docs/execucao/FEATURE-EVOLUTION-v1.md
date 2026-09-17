# Evolução de funcionalidades Agro360 — v1 (2026-09-17)

Documento de produto. Não certifica implementação. Estado real permanece em
`docs/EXECUTION-CHECKPOINT.md` e `docs/TRACEABILITY-MATRIX-v0.2.0.md`.
Requisitos novos só entram no escopo vendável depois de regra, persistência,
serviço, autorização, tela, verificação e gate PostgreSQL.

Fonte verificada em `main` @ `8da8e2876f9a644a13c1c53f9e62d745ac917cd3`
(merge PR #140 — modelos de inspeção).

## 1. Contexto confirmado

Agro360 é a plataforma modular multi-tenant da MNSOFT para operação do
agronegócio (.NET 10, Razor, Dapper, PostgreSQL/PostGIS). A execução canônica
é nativa. Docker é opcional.

O plano mestre (`docs/execucao/AGRO360-MASTER-PLAN.md`) descreve E0–E10.
Sprints históricas 6–50 documentam fatias; nenhuma sprint declara o sistema
homologado. Código à frente dos gates: inspeções 092, CAPA/eficácia 090–091,
pós-venda 089, conferência de compras 084, custos/fechamento de safra 077–078,
colheita 075, beneficiamento 076, logística 071, frota 069, pecuária 068,
Central de Operações 073–074, sync móvel controlado.

## 2. Regras que não podem ser enfraquecidas

Ver `docs/BUSINESS-RULES.md` e ADRs em `docs/execucao/DECISIONS.md`.

Resumo operacional para qualquer evolução:

| Tema | Invariante |
|---|---|
| Tenant | Todo registro operacional pertence a um tenant; RLS + contexto autenticado |
| Plano | Histórico não some por downgrade; redução de limite não apaga dados |
| Dinheiro / quantidade | `decimal`/`numeric`; unidade do catálogo ou conversão explícita |
| Estoque | Negativo proibido; reserva ≠ saída; movimento imutável |
| Qualidade | Ausência de reprovação não é aprovação |
| Colheita | Estimativa ≠ apontada ≠ recebida ≠ aceita ≠ comercial |
| Logística | Reserva ≠ expedição ≠ entrega aceita ≠ retorno disponível |
| Fechamento | Snapshot gerencial; não encerra pedido, OS, título ou estoque |
| IA | Recomendação com fonte; ação crítica exige humano |
| SaaS | Contratação ≠ permissão; flag ≠ contrato; cobrança da plataforma ≠ financeiro rural |
| Mobile | Fila local não é conclusão; replay compara hash |

## 3. Inventário honesto por jornada

Estados: **validado** (E2E autenticado + PostgreSQL), **no código** (implementado
não validado), **parcial**, **ausente**, **bloqueado**.

| Jornada | Estado | Próxima evolução segura |
|---|---|---|
| Login / refresh / logout | no código (E0 exercitado em algumas máquinas) | Homologar provisionamento opt-in + MFA SuperAdmin (AG-E1-002) |
| Shell / menu / tema | no código | Reagrupar navegação, breadcrumb, faixa de contexto assistido |
| Propriedades / talhões | no código | Paginação e releitura pós-POST já descritas; E2E |
| Agricultura / plano da safra | no código (086) | UI de reprogramação e exceção de dependência |
| Colheita / recebimento | no código (075) | Critérios dinâmicos na inspeção ligada ao recebimento |
| Fechamento gerencial | no código (077) | Genealogia comercial/logística/custos até a safra |
| Custos da safra | parcial (078) | Reconciliação com origem única e sem soma entre unidades |
| Pecuária operacional | no código (068) | E2E; não misturar animal/lote/local/produto |
| Frota / OS / abastecimento | no código (069) | Disponibilidade + reserva confiável antes de offline |
| Compras / recebimento | no código | Unificar `purchasing` vs `procurement`; conferência 084 E2E |
| Comercial / preço | no código | Reserva → entrega → recebível sem status fictício |
| Produção / receita | parcial | Snapshot versionado receita → ordem (pendência explícita) |
| Expedição / entrega | no código (071) | Qualidade do retorno, perda com custo, frete, capacidade |
| Pós-venda / devolução | no código (089) | Ligar inspeção 092 ao recebimento da devolução |
| Qualidade / CAPA / eficácia | no código (090–091) | Encerrar só com verificação eficaz e restrição independente |
| Modelos de inspeção | no código (092, PR #140) | 15 cenários E2E; eventos de origem nos módulos reais |
| Central de Operações | no código | Projeções da safra/inspeção sem resolver a origem |
| Mobile / offline | parcial | Ampliar catálogo só depois da integridade no servidor |
| SaaS catálogo / cobrança | parcial | Sem PIX/boleto fictício; unificar entitlement no login |
| Fiscal externo | bloqueado | Adapter + webhook real; metadado ≠ documento autorizado |
| IoT / clima | parcial | Telemetria nunca escreve tabela de negócio direto |
| ESG / exportação | parcial | Dossiê público separado de dados internos |

## 4. Backlog priorizado desta versão (não inflar o escopo)

IDs estáveis. Um recorte por execução.

### P0 — integridade e homologação

1. **AG-E0-003** — caminho incremental canônico (`due_on` / pontes `006z`/`007z`) em banco descartável.
2. **AG-Q-092-E2E** — instalar 092 limpo + upgrade; publicar → executar → NC/restrição → reinspeção → agenda sem duplicidade.
3. **AG-E1-002** — SuperAdmin único, MFA real, suporte assistido com faixa visual e auditoria do ator real.
4. **AG-TPL-001** — template do shell: grupos do plano mestre, breadcrumb, skip-link, banner de contexto assistido.

### P1 — fechar ciclos já modelados

5. **AG-Q-EVT-001** — disparar inspeção por evento real de recebimento, produção, expedição e devolução. Sem segundo agendador.
6. **AG-E8-RET-001** — retorno: recebimento parcial → qualidade → destinação; perda com custo; capacidade de viagem; rateio de frete.
7. **AG-E6-GEN-001** — genealogia safra → colheita → estoque → expedição → receita, para o fechamento deixar de marcar “Não disponível” por omissão de vínculo.
8. **AG-E5-UNI-001** — uma identidade de fornecedor; pedido comercial com snapshot de preço; reserva ≠ entrega ≠ recebível.

### P2 — plataforma

9. **AG-E2/E3** — catálogo, snapshot de preço, cobrança interna pendente, suspensão com área de regularização.
10. **AG-E9-CAT** — ampliar comandos móveis somente para operações já idempotentes no servidor.
11. **AG-E10-A11Y** — ajuda contextual específica por página (hoje o layout usa texto genérico).

Fora desta versão: novo provedor fiscal, marketplace B2B completo, IA generativa externa, segundo frontend, EF Core.

## 5. Critério de aceite transversal de qualquer fatia

- Regra no Domain; SQL incremental com checksum; Dapper parametrizado; RLS.
- Endpoint autorizado por permissão de ação, não por nome de perfil.
- Tela com estados vazio/carregando/erro/negado; seletores por nome; sem GUID.
- Idempotência e concorrência (OCC ou lock) nas escritas críticas.
- Ajuda “Como usar esta tela” específica.
- Evidência: comando, ambiente, resultado. Compilação não encerra o recorte.
- Sem credencial, token, connection string ou payload sensível na documentação.

## 6. O que o template deve desbloquear

A evolução de UI não cria módulo. Ela reduz erro operacional:

- operador encontra a jornada na ordem do trabalho agropecuário;
- SuperAdmin não se confunde com funcionário do cliente;
- troca de contexto limpa cache e respostas atrasadas;
- mobile de campo herda os mesmos estados textuais (Pendente/Falhou).
