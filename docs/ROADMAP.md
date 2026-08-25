# Roadmap e sprints

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
