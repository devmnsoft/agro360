# Histórico de sprints

As Sprints 6–19 entregaram operação base, pecuária, financeiro, armazenagem, rastreabilidade amazônica, agricultura, mobile/offline, BI, governança SaaS, compliance, integrações, geoespacial, cooperativas e RH/SST. Os guias detalhados permanecem em `docs/sprint-*.md`.

## Sprint 20 — Release Candidate

- instalador PostgreSQL único auditável e marcador `2.0.0-rc.1`;
- execução e CI sem Docker;
- navegação principal com destinos reais e busca global que abre a rota autorizada;
- testes de arquitetura para portabilidade do SQL e integridade da navegação;
- checklist único dos dez fluxos de homologação.

## Sprint 21 — implantação comercial

Entregue onboarding assistido, cinco templates agro, módulos por tenant, checklist percentual, dashboard real e importação CSV com mapeamento, pré-visualização, erros por linha, confirmação, histórico e rollback auditável. A homologação exige executar o SQL completo, carregar opcionalmente o seed demo, validar isolamento por tenant e percorrer `/Deployment` em desktop e mobile.

## Sprint 22 — CRM Agro, vendas B2B, comissões e split
Entregue: CRM multi-tenant, equipe e regiões, funil auditado, agenda, preços, pedidos e contratos, cálculo idempotente de comissão, split interno validado, eventos de integração, dashboard real, UI responsiva, RBAC, SQL completo e testes de regras/arquitetura. O split não representa pagamento bancário.

## Sprint 23 — Documentos, evidências, dossiês e certificados
Concluída: armazenamento local configurável, SHA-256, versionamento, vínculos multi-módulo, validação de evidências, checklists de dossiê, certificados revogáveis, consulta pública mínima, dashboard real e CSV.

## Sprint 24 — Workflows, alertas, tarefas e notificações

Entregues central operacional persistente, regras determinísticas sem IA, deduplicação de alertas, aprovações com segregação, notificações por usuário, agenda, dashboard de saúde e outbox honesta. A migration `022_sprint24_work_management.sql` e o SQL completo suportam instalação externa sem Docker.

## Sprint 25 — BI executivo, mapas, relatórios e design unificado

Entregues a Central de Relatórios com CSV real, fundação multi-tenant para filtros/widgets/exportações, modelo geográfico de locais/áreas/rotas, preferências pessoais, auditoria de UI e primitives visuais responsivos. O PDF permanece pendente até haver infraestrutura real.

## Sprint 26 — Operação Mobile de Campo

Agro360 Campo mobile-first, PWA instalável, outbox IndexedDB, sincronização auditável/idempotente, ocorrências, check-ins geográficos, evidências e evolução de checklists de campo.

## Sprint 27 — Portal Externo e Marketplace B2B

Entregues: identidade externa isolada, convites com token hasheado e outbox, aceite de termos, dashboard responsivo por perfil, comunicados lidos, central de solicitações, catálogo com disponibilidade real, cotação transacional, schema com RLS e design premium mobile-first. Integrações de pedido/pagamento permanecem deliberadamente desabilitadas até homologação de providers reais.

## Sprint 28 — Qualidade e Compliance
Implementação persistente de requisitos configuráveis, especificações versionadas, inspeções, decisão auditável de lotes, não conformidades/CAPA, auditorias, beneficiamento e prontidão de exportação. Homologar regras e UX conforme [Qualidade e Compliance](QUALITY-COMPLIANCE.md) e [Prontidão para exportação](EXPORT-READINESS.md).

## Sprint 29 — Administracao SaaS

Governanca multiempresa, planos, assinaturas, cobrancas internas manuais, feature flags, limites, overrides, onboarding, white label, RBAC e auditoria administrativa. As invariantes possuem regras de dominio e instalacao PostgreSQL completa; pagamento automatico permanece fora do produto ate integracao real.
