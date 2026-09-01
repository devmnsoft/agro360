# Roadmap e sprints

## Sprint 31 — concluída

Fundação funcional de inteligência determinística e multi-tenant. Evoluções futuras: executor periódico por feature flag, novos eventos e provider opcional após homologação de privacidade.

> Estado atual: Sprint 20 em Release Candidate (`2.0.0-rc.1`). A promoção para versão estável depende da homologação integral de `docs/QA-CHECKLIST.md`; itens posteriores continuam futuros e não devem aparecer como recursos entregues na interface.

## Estratégia de entrega

O programa é dividido em início, meio e fim. Cada sprint dura duas semanas, pode ser subdividida por equipe e termina com gate objetivo. Nenhum sprint avança por quantidade de telas; avança por fluxo utilizável.

### Início — provar a malha única

Objetivo: executar o primeiro fluxo agrícola e o fluxo animal sem ilhas de dados.

| Sprint | Entrega | Gate |
|---:|---|---|
| 0 | solução, Clean Architecture, Dapper, PostgreSQL/PostGIS, Docker, CI | build limpo e banco vazio migrável |
| 1 | tenant, organização, usuário, JWT, RBAC, RLS, auditoria | isolamento comprovado por teste |
| 2 | fazenda, talhão, geometria, catálogo de unidade e produto | fazenda/talhão persistidos e autorizados |
| 3 | safra, depósito, entrada, plantio e motor de custos | plantio baixa semente e gera custo atomicamente |
| 4 | colheita, produto, venda, recebível, dashboard e AgroGraph | primeiro fluxo E2E completo |
| 5 | animal, timeline, pesagem/GMD, sanidade/carência e venda | segundo fluxo E2E completo |

Estado atual: Sprints 0–5 entregaram a primeira fatia vertical de backend. A Sprint 5.5 candidata v0.2.0 adiciona hardening operacional de Outbox, migration corretiva, CI e recuperação. A homologação E2E pela interface permanece pendente; por isso agricultura, estoque, pecuária e comercial continuam `FOUNDATION`, sem antecipar a classificação `CORE`.

### Sprint 5.5 — gate honesto da candidata v0.2.0

- concluído no repositório: transações verticais existentes, RLS forçada, RBAC de API, retry limitado/dead-letter da Outbox, migração repetível, build das quatro imagens no CI e runbook de backup/restauração;
- exige ambiente com .NET 10 e Docker: restore, build, testes PostgreSQL/PostGIS, duas execuções do Migrator, smoke dos hosts e restauração temporária;
- pendente para promover fluxos a `CORE`: formulários Web conectados, E2E de browser, seed completo por perfis e homologação formal nas larguras-alvo;
- Sprint 7 mantém o backlog de pasto/confinamento/dieta e apropriação do milho ao lote; nenhuma fábrica de ração fictícia foi criada.

### Meio — completar operação e cadeias empresariais

| Sprint | Fases do mestre | Resultado esperado |
|---:|---|---|
| 6 | 5, 8 | frota, combustível, manutenção, compras e fornecedor |
| 7 | 7, 10 | pastagem, confinamento, dieta, rateio e custo/@ |
| 8 | 9, 11 | contas a pagar, caixa, bancos, contratos e simulação comercial |
| 9 | 12 | recepção, classificação, balança, secagem, silo e expedição |
| 10 | 13–14 | torre logística, QR de origem e cadeia de custódia ampliada |
| 11 | 15–16 | app MAUI Android, SQLite, mapas offline, Local Outbox e sync |
| 12 | 17–18 | GED, storage S3, OCR, ambiental, ESG, carbono e vencimentos |
| 13 | 19–20 | cooperativas, portal do cooperado, revendas, CRM e originação |
| 14 | 21 | agroindústria, qualidade e fábrica de ração |
| 15 | 22 | leite, aves, suínos, aquicultura e florestal |
| 16 | 23 | gateway IoT, MQTT/HTTP, dispositivos, validação e regras |

Gate do meio: terceiro fluxo E2E — milho colhido → armazém → fórmula de ração → confinamento → ganho de peso → custo real → venda/margem.

### Fim — inteligência, escala e release

| Sprint | Fases | Resultado esperado |
|---:|---|---|
| 17 | 24 | BI por perfil, Data Quality, Command Center e benchmark anônimo |
| 18 | 25 | Copilot contextual, agentes especializados e explicabilidade |
| 19 | 26 | produtividade, GMD, manutenção, caixa e anomalias |
| 20 | 27 | performance, segurança, OpenTelemetry, carga, backup e DR |
| 21 | 28 | homologação dos três fluxos, mobile em campo e integrações |
| 22 | 29 | release comercial, onboarding, operação e suporte |

## Backlog por fase do documento mestre

| Fase | Escopo | Dependência | Gate de encerramento |
|---:|---|---|---|
| 0 | fundação | nenhuma | restore/build/test/container |
| 1 | plataforma | 0 | RLS e RBAC comprovados |
| 2 | propriedades | 1 | mapa e documentos cadastrais |
| 3 | agricultura base | 2 | operação gera estoque/custo |
| 4 | estoque | 1 | inventário sem saldo negativo |
| 5 | máquinas | 2,4 | custo/hora e manutenção |
| 6 | pecuária | 2,4 | timeline, GMD, sanidade |
| 7 | pasto/confinamento | 6 | dieta, consumo e custo/@ |
| 8 | compras | 4 | pedido → estoque → financeiro |
| 9 | financeiro | 8,11 | caixa, conciliação e DRE |
| 10 | custos | 3–9 | apropriação/rateio reproduzível |
| 11 | comercial | 4,6,9 | contrato → venda → recebimento |
| 12 | armazenagem | 4,11 | balança e lote armazenado |
| 13 | logística | 11,12 | carga rastreada até entrega |
| 14 | rastreabilidade | 3–13 | origem consultável ponta a ponta |
| 15 | mobile | APIs estáveis | manejo real em Android |
| 16 | offline | 15 | 24h de campo e sync sem perda |
| 17 | GED | storage | versão, OCR, busca e retenção |
| 18 | ambiental/ESG | 2,17 | vencimento e inventário versionado |
| 19 | cooperativas | 11–14 | cooperado visualiza saldo autorizado |
| 20 | revendas | 4,8,11 | CRM → venda → entrega → cobrança |
| 21 | agroindústria | 4,10,14 | matéria-prima → lote → produto |
| 22 | verticais | 6,10,21 | KPIs e regras por vertical |
| 23 | IoT | regras/eventos | telemetria não grava negócio direto |
| 24 | BI | dados consistentes | KPIs reconciliados com fonte |
| 25 | IA | 24, RBAC | resposta autorizada e explicada |
| 26 | preditivo | histórico/qualidade | modelo versionado e monitorado |
| 27 | hardening | todos | SLO, pentest e carga aprovados |
| 28 | homologação | 27 | aceite formal dos cenários |
| 29 | release | 28 | zero crítico/migração/tela fictícia |

## Critérios de release comercial

- zero erro crítico conhecido;
- migração executa em banco limpo e sobre versão anterior;
- nenhuma tela importante sem backend;
- nenhum endpoint retorna mock em produção;
- nenhum módulo é anunciado concluído sem fluxo E2E;
- RLS, autorização, backup/restore e logs auditados;
- acessibilidade, responsividade, erros JS e links verificados;
- runbook de incidente, suporte, atualização e rollback aprovado.

## v0.7.0 — diferenciais estratégicos entregues

- Rastreabilidade amazônica e genérica; conformidade de beneficiamento; ledger relacional imutável; certificados; logística fluvial/vicinal; força de vendas e split.

## Expansões futuras opcionais

- Adaptador blockchain real, gateways de pagamento, certificadoras e fluxos de exportação.
- Evidências ESG/carbono, sensores IoT de temperatura e rastreamento GPS/hidrológico.

## Sprint 21 — implantação comercial

Entregue onboarding assistido, cinco templates agro, módulos por tenant, checklist percentual, dashboard real e importação CSV com mapeamento, pré-visualização, erros por linha, confirmação, histórico e rollback auditável. A homologação exige executar o SQL completo, carregar opcionalmente o seed demo, validar isolamento por tenant e percorrer `/Deployment` em desktop e mobile.

## Sprint 22 — concluída
- CRM Agro e força de vendas distribuída; funil, agenda, preços, pedidos, contratos e metas.
- Comissões e split interno com validações, auditoria e isolamento por tenant.
- Próxima evolução: consumidores assíncronos dos eventos comerciais para reserva física e conciliação/repasse por provedor financeiro homologado.

### Sprint 23 — entregue
Biblioteca documental agro integrada, evidências auditáveis, dossiês e certificados operacionais. Próxima evolução real: renderização PDF assinada; nesta entrega a exportação é CSV para não simular PDF.

### Sprint 24 — concluída
- Operação 360: tarefas, alertas, regras, workflows, notificações, agenda e dashboard.
- Outbox multicanal preparada, sem declarar envio quando provider não existe.
- Próxima evolução: providers externos contratados e avaliadores adicionais por evento de domínio.

### Sprint 25 — concluída
- Inteligência Agro360 e catálogo de relatórios reais por módulo.
- Persistência governada de BI, mapas operacionais, preferências e auditoria de UI.
- Central de Relatórios CSV e design system responsivo. PDF segue no backlog por depender de infraestrutura real.

### Sprint 26 — concluída no código

Base operacional do Agro360 Campo, sincronização offline, geolocalização, evidências e esquema de checklists. A assinatura biométrica e mapas offline permanecem fora do escopo por exigirem infraestrutura/dispositivo específicos.

### Sprint 27 — concluída

Portal externo B2B, controle de acesso por vínculo, Marketplace Agro360, cotações, solicitações, comunicados e fundações para homologação de fornecedores e atualizações logísticas. Próxima evolução: conectar conversão de cotação e recuperação de senha aos providers comerciais e de e-mail homologados.

## Sprint 28 — Qualidade e Compliance
Implementação persistente de requisitos configuráveis, especificações versionadas, inspeções, decisão auditável de lotes, não conformidades/CAPA, auditorias, beneficiamento e prontidão de exportação. Homologar regras e UX conforme [Qualidade e Compliance](QUALITY-COMPLIANCE.md) e [Prontidão para exportação](EXPORT-READINESS.md).

### Sprint 29 — concluida

Fundacao comercial SaaS persistente e auditavel. Proxima evolucao: integrar gateway contratado e storage de objetos homologado, sem simulacao de sucesso.

## Sprint 30 entregue
Central de integrações, contrato de API externa, webhooks com tentativas limitadas, jobs de import/export e fundação fiscal estão disponíveis. Próxima etapa real: homologar providers fiscais/SEFAZ e armazenamento criptografado específico de cada implantação antes de habilitar transmissão.

### Sprint 33 — concluída

- Central de atendimento, SLA, ajuda, implantação, treinamento, feedback, backlog e release notes.
- Próxima evolução real: agendamento do verificador de SLA no Worker, calendário de horário útil/feriados e providers externos certificados via outbox.

### Sprint 34 — concluída: fundação SST Rural
Dashboard, cadastros operacionais, mapa de riscos, entregas de EPI, validade de treinamentos/exames, incidentes, ações e checklists auditáveis. Próximo incremento real: adaptadores transacionais de estoque e providers externos somente mediante contrato/configuração.

## Sprint 35 — concluída

Central de Frota e Máquinas, disponibilidade, preventivas/corretivas, ordens de serviço, combustível, componentes, paradas e dimensões de custos. Próxima evolução real: habilitar providers contratados de telemetria/posto/fabricante; nenhum provider fictício foi incluído.

## Sprint 36 — Financeiro e Controladoria

Central financeira real com plano de contas, centros de custo, títulos, baixas e conciliação manual autorizada, orçamento versionado, fluxo de caixa, DRE, rentabilidade, CSV, auditoria, RLS e design executivo responsivo. PostgreSQL é externo por connection string e o full install inclui o schema 3.6.0. Homologar estados vazios, validações, permissões e isolamento por tenant. Nenhum arquivo binário deve ser gerado.

## Sprint 37 — concluída
Compras e suprimentos multi-tenant com cadeia auditável da requisição ao recebimento. Próximos incrementos: providers homologados para comunicação externa e PDF, sem simulação.

## Sprint 38 — concluída
PCP e beneficiamento agroindustrial disponíveis do insumo ao lote acabado. Próxima evolução real: conectores contábeis específicos por ERP e telemetria de equipamentos, sem estimativas quando a origem não estiver configurada.

## Sprint 39 — concluída
Fundação operacional de Exportação e Trading entregue. Próximas evoluções dependem de provedores reais contratados para câmbio e conhecimento de embarque eletrônico.

### Sprint 40 — entregue
Módulo Fiscal e Faturamento gerencial completo. Próxima evolução condicionada: homologar provedor fiscal e gestão segura de certificado antes de habilitar emissão SEFAZ/NFS-e.

## Sprint 41 — concluída
- Inteligência Agro360: painel executivo, governança de indicadores, metas, alertas, riscos, recomendações, auditoria e relatórios rastreáveis.
- Próximo: conectar fontes adicionais pelo contrato de integração e homologar fórmulas por tenant; provedor externo de IA permanece opcional e não configurado.

### Concluído — Sprint 42
Fundação operacional de Sustentabilidade e ESG, governança ambiental multi-tenant, inventário gerencial, rastreabilidade socioambiental e exportações CSV.

### Próximos incrementos reais
Configurar provedores oficiais de CAR/licenças e certificadoras; concluir adaptadores transacionais de bloqueio em compras/exportação conforme política de cada tenant; integrar upload documental usando o storage configurado.

## Sprint 43 — concluída

Campo Mobile PWA operacional com outbox local, sincronização idempotente e resolução humana de conflitos. Próximos incrementos reais: homologar storage documental por tenant, provider ICP-Brasil opcional e telemetria/background sync conforme suporte do dispositivo, sem simulação.

## Sprint 45 — concluída

Fundação Enterprise para super administração e área do cliente, autenticação por e-mail/documento, RBAC por tenant, catálogo de módulos, planos/cobrança gerencial, localização inicial e ajuda contextual. Pendências reais: configurar e homologar um provedor antes de qualquer pagamento online, completar traduções do legado e executar testes de integração contra PostgreSQL homologado.

## Sprint 46 — concluída
A plataforma dispõe da fundação operacional do ecossistema SaaS: módulos aprovados, parceiros com escopo/prazo, integrações seguras e catálogo comercial. Próximos conectores somente serão ativados após homologação real do fornecedor e gestão externa de segredos.

## Sprint 47 — CRM e ciclo do cliente

A plataforma integra CRM, pipeline, propostas com total no backend, contratos SaaS, implantação assistida, suporte/SLA, saúde explicável, conhecimento e portal isolado. As novas rotas exigem permissões específicas, as tabelas usam auditoria/RLS por tenant e toda comunicação sem provedor permanece pendente na outbox. A experiência responsiva usa funil, timeline, badges e o componente recolhível **Como usar esta tela**. Consulte `docs/CRM-COMMERCIAL.md`, `docs/CUSTOMER-SUCCESS.md`, `docs/SUPPORT.md` e a migração `047_crm_customer_lifecycle.sql`.

## Sprint 48 — Governança, Migração, LGPD e Performance

Governança persistente e isolada por tenant: importação CSV pré-validada, qualidade de dados, exportação gerencial segura, solicitações LGPD, auditoria avançada, sessões e telemetria de performance. A migração `048_data_governance.sql` cria constraints, FKs, índices e RLS. Consulte `docs/DATA-GOVERNANCE.md`, `docs/IMPORT-MIGRATION.md`, `docs/LGPD-SECURITY.md` e `docs/PERFORMANCE.md`.

## Sprint 49 — concluída
Fundação de workflows versionados, tarefas operacionais, aprovação segregada, templates localizados, outbox, SLA, agenda e automações idempotentes. Pendência real: homologar provedores externos antes de habilitar entrega de e-mail/WhatsApp.

## Sprint 50 — formulários e ajuda contextual

Validação backend continua sendo a fonte da verdade; a interface oferece resumo e erros por campo, loading, confirmação com consequência real e motivo nas ações definidas pela regra. Ajuda curta é recolhível e localizada em pt-BR, en-US e es-ES. Configurações e eventos de UX usam o schema `ui`, auditoria e RLS por tenant. Detalhes: `docs/UX-FORMS-VALIDATION.md` e `docs/CONTEXTUAL-HELP.md`.
