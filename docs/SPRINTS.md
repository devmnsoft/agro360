# Histórico de sprints

## Sprint 31 — Inteligência Operacional Agro

Central de decisão, recomendações auditáveis, scores com fatores, anomalias com observado/referência, prioridades, regras configuráveis e Agro360 Assistente baseado em dados reais.

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

## Sprint 30 — Integrações, API externa e Fiscal
Fundação produtiva multi-tenant para conectores e filas idempotentes, aplicações/API keys com hash e revogação, webhooks auditáveis, importação assistida, exportação CSV e documentos/rascunhos fiscais. Nenhuma autorização ou transmissão fiscal é simulada: provider oficial, certificado e ambiente homologado são pré-condições reais.

## Sprint 33 — Suporte, implantação e Customer Success

Entregue o vertical slice de atendimento multi-tenant: chamados e timeline, transições validadas, política e cálculo de SLA, conhecimento publicável, nove fases de implantação, adoção, NPS/feedback, conversão em backlog, comunicados com leitura e dashboard real resiliente a conjuntos vazios. A interface responsiva consolida as operações sem entrada manual de chaves técnicas.

## Sprint 34 — SST Rural
Central tenant-safe para trabalhadores, riscos calculados, EPI, treinamentos, exames administrativos mínimos, incidentes, investigação, ações, checklists, alertas, CSV, auditoria e design responsivo. Integrações externas permanecem explicitamente não configuradas sem provider real.

## Sprint 35 — Frota, máquinas e custo operacional

Entregue: modelo multi-tenant completo, regras de ativos/medidores/OS/abastecimento/paradas, API Dapper, dashboard e central web premium, CSV, auditoria, permissões e documentação. Integrações externas permanecem explicitamente não configuradas.

## Sprint 36 — Financeiro e Controladoria

Central financeira real com plano de contas, centros de custo, títulos, baixas e conciliação manual autorizada, orçamento versionado, fluxo de caixa, DRE, rentabilidade, CSV, auditoria, RLS e design executivo responsivo. PostgreSQL é externo por connection string e o full install inclui o schema 3.6.0. Homologar estados vazios, validações, permissões e isolamento por tenant. Nenhum arquivo binário deve ser gerado.

## Sprint 37 — Compras e Suprimentos
Fornecedores e homologação, catálogo, requisições, cotações comparáveis, alçadas, pedidos, recebimento, divergências, contratos de integração, CSV, RLS e central premium responsiva. Integrações permanecem pendentes até confirmação transacional real.

## Sprint 38 — PCP Agroindustrial
Entregues estrutura industrial, formulações imutáveis/versionadas, ordens com máquina de estados e histórico, execução, consumo transacional, perdas, lotes, qualidade, paradas, custos, rastreabilidade, dashboard real, CSV, permissões, RLS e testes de regras/arquitetura.

## Sprint 39 — Exportação e Trading
Módulo internacional tenant-safe com dashboard real, clientes, contratos, documentos, embarques, Incoterms, câmbio manual, custos, compliance, rastreabilidade e CSV.

## Sprint 40 — Fiscal Agro e Faturamento (concluída)
- Operações/séries/regras, faturamentos, documentos gerenciais, conferência de compras e divergências.
- Integrações transacionais e rastreáveis com estoque e financeiro; emissão externa explicitamente pendente.
- Dashboard real, workspace responsivo, RBAC, RLS, auditoria e onze relatórios CSV.

## Sprint 41 — Inteligência Executiva Agro e BI Operacional

Painel 360 tenant-safe, indicadores e snapshots históricos, alertas auditáveis, regras parametrizadas, radar de riscos, recomendações determinísticas com confirmação humana, auditoria transversal e exportações CSV. Ausência de dados nunca é apresentada como zero.

## Sprint 42 — Sustentabilidade Agro e ESG
- Modelo PostgreSQL/RLS para 15 agregados ambientais, dashboard real e 14 CSVs.
- Cadastro de conformidade por propriedade sem entrada de identificador técnico.
- Regras determinísticas de área, emissões pendentes sem fator, origem de lote e planos críticos.
- Interface premium responsiva e documentação de integrações oficiais pendentes.

## Sprint 43 — Campo Mobile PWA

Entregues modelo multi-tenant de perfis/atalhos, checklists versionados, execuções, evidências, QR persistido, ocorrências, fila idempotente, conflitos auditáveis, assinatura gerencial, localização autorizada, exportações e PWA mobile-first. Integrações nativas, ICP-Brasil e storage externo permanecem condicionados a providers reais.

## Sprint 45 — Plataforma SaaS Enterprise

Incorporada a evolução não executada da Sprint 44: super administrador único, cadastro e bloqueio de tenants, usuários/perfis tenant-scoped, catálogo e estado de módulos, planos, cobrança gerencial auditada, login por identificador fiscal, fallback de idioma, ajuda contextual e identidade MNSOFT. A migration 045 adiciona constraints, índices, auditoria e RLS.

Na revisão integrada, o endpoint de bloqueio foi corrigido para persistir `BLOCKED` (sem confundir bloqueio com suspensão), a troca de plano passou a rejeitar também planos inativos durante a edição e toda ativação/desativação de usuário passou a produzir evento de auditoria no mesmo commit transacional.

## Sprint 46 — Marketplace, Parceiros, APIs e Webhooks
Marketplace comercial persistente, aprovação MNSOFT, acessos temporários de parceiros, aplicações e chaves por tenant, webhooks HTTPS auditáveis, central interna de desenvolvedores e catálogo de planos. Segurança inclui hash irreversível, escopos explícitos, revogação, limites, RLS e auditoria.

## Sprint 47 — CRM e ciclo do cliente

A plataforma integra CRM, pipeline, propostas com total no backend, contratos SaaS, implantação assistida, suporte/SLA, saúde explicável, conhecimento e portal isolado. As novas rotas exigem permissões específicas, as tabelas usam auditoria/RLS por tenant e toda comunicação sem provedor permanece pendente na outbox. A experiência responsiva usa funil, timeline, badges e o componente recolhível **Como usar esta tela**. Consulte `docs/CRM-COMMERCIAL.md`, `docs/CUSTOMER-SUCCESS.md`, `docs/SUPPORT.md` e a migração `047_crm_customer_lifecycle.sql`.

## Sprint 48 — Governança, Migração, LGPD e Performance

Governança persistente e isolada por tenant: importação CSV pré-validada, qualidade de dados, exportação gerencial segura, solicitações LGPD, auditoria avançada, sessões e telemetria de performance. A migração `048_data_governance.sql` cria constraints, FKs, índices e RLS. Consulte `docs/DATA-GOVERNANCE.md`, `docs/IMPORT-MIGRATION.md`, `docs/LGPD-SECURITY.md` e `docs/PERFORMANCE.md`.

## Sprint 49 — Workflows inteligentes

Motor versionado e auditável para etapas, tarefas, aprovações, notificações, agenda, SLA/escalonamentos e automações declarativas. A central responsiva mantém ajuda contextual, isolamento por tenant e outbox sem simulação de envio.

## Sprint 50 — formulários e ajuda contextual

Validação backend continua sendo a fonte da verdade; a interface oferece resumo e erros por campo, loading, confirmação com consequência real e motivo nas ações definidas pela regra. Ajuda curta é recolhível e localizada em pt-BR, en-US e es-ES. Configurações e eventos de UX usam o schema `ui`, auditoria e RLS por tenant. Detalhes: `docs/UX-FORMS-VALIDATION.md` e `docs/CONTEXTUAL-HELP.md`.

### Sprint corretiva — schema PostgreSQL canônico
O banco consolidado usa somente `agro360`; nomes de tabela carregam o prefixo do módulo e SQL estático/Dapper deve ser qualificado. A homologação executa restore/build/test, instala o SQL com `ON_ERROR_STOP=1`, confirma a ausência dos schemas legados e testa isolamento: Super Admin enxerga todos os tenants; administradores e usuários permanecem limitados ao tenant ativo. Bloqueios de usuário/cliente, alterações de plano/módulo, cobrança e acesso global exigem confirmação, mensagem clara e auditoria. Login aceita e normaliza e-mail, CPF ou CNPJ; formulários usam seletores em vez de GUIDs e oferecem ajuda contextual em pt-BR, en-US e es-ES.

O bootstrap seguro recebe `AGRO360_SUPERADMIN_INITIAL_PASSWORD`, aplica o hash real da aplicação e marca troca obrigatória. Dados canônicos: Super Administrador MNSOFT; `superadmin@mnsoft.com.br`; CNPJ `18.160.057/0001-13`; perfil `SUPER_ADMIN`; status Ativo. Não existe senha padrão em produção.
