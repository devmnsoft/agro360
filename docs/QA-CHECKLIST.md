# Checklist de homologação da Release Candidate

## Sprint 31

- [ ] Testar dois tenants e UUID cruzado em dashboard, listas, detalhes e assistente.
- [ ] Validar aceite, recusa motivada, arquivo e histórico.
- [ ] Conferir score 0/100, fatores e anomalia observado/referência.
- [ ] Validar regra inativa, deduplicação, falha auditada, plano e feature flag.
- [ ] Homologar 320/768/1440 px, teclado, foco, loading, erro e contraste.

Antes de iniciar, instale o SQL completo, inicie API e Web, crie um tenant descartável e registre evidência (resultado, horário e usuário) para cada etapa.

- [ ] **Onboarding:** organização, administrador, propriedade, safra e cultura persistem e reaparecem após novo login.
- [ ] **Agricultura:** talhão, planejamento, aplicação com baixa de estoque, ocorrência e indicadores usam seletores, sem IDs digitados.
- [ ] **Pecuária:** lote/animal, manejo, sanidade e nutrição/reprodução atualizam os indicadores.
- [ ] **Compras/estoque:** fornecedor, compra, entrada, saldo e consumo operacional fecham quantidades e custos.
- [ ] **Pós-colheita:** romaneio, pesagens, qualidade, lote e expedição preservam rastreabilidade e capacidade.
- [ ] **Financeiro:** títulos, liquidações, centro de custo, caixa e resultado conciliam valores.
- [ ] **Rastreabilidade:** cadeia amazônica, beneficiamento, regra crítica, hash, certificado/QR e histórico são verificáveis.
- [ ] **Logística:** rota, janela, capacidade, planejamento e status persistem eventos.
- [ ] **Compliance:** exigência, evidência, não conformidade e dossiê refletem o status correto sem expor dados internos.
- [ ] **BI:** filtros por safra, propriedade, produto e lote funcionam; estados vazios não quebram gráficos.
- [ ] Repetir leitura/alteração com usuário de outro tenant e confirmar `403`/`404`, sem vazamento.
- [ ] Confirmar mensagens em português, loading, sucesso, erro, paginação e confirmação destrutiva em cada formulário usado.

## Sprint 21 — implantação comercial

Entregue onboarding assistido, cinco templates agro, módulos por tenant, checklist percentual, dashboard real e importação CSV com mapeamento, pré-visualização, erros por linha, confirmação, histórico e rollback auditável. A homologação exige executar o SQL completo, carregar opcionalmente o seed demo, validar isolamento por tenant e percorrer `/Deployment` em desktop e mobile.

## Sprint 22 — Comercial
- [ ] Executar restore/build/test e SQL completo em PostgreSQL limpo.
- [ ] Validar criação, edição, busca e paginação de cliente e prospect sem digitar IDs.
- [ ] Bloquear pedido para cliente inativo e exigir permissão para bloqueado.
- [ ] Exigir motivo em oportunidade perdida, atividade/pedido cancelado e comissão bloqueada/estornada.
- [ ] Rejeitar pedido vazio, quantidade zero e desconto acima do teto.
- [ ] Confirmar histórico de etapa, eventos de pedido e isolamento de tenant/RLS.
- [ ] Rejeitar comissão duplicada e pedido cancelado; validar cálculo fixo e percentual.
- [ ] Rejeitar participante duplicado, modalidade conflitante e soma de split acima de 100%.
- [ ] Conferir dashboard vazio e populado, navegação mobile e mensagens de erro.
- [ ] Confirmar ao usuário que split é interno e não executa pagamento bancário.

## Sprint 23 — Documentos e Evidências
- [ ] Upload permitido conclui e SHA-256 confere; extensão perigosa, arquivo vazio, >25 MB e traversal são recusados.
- [ ] Download exige autenticação, permissão e mesmo tenant; caminho físico nunca aparece na resposta.
- [ ] Nova versão exige motivo e versões antigas continuam disponíveis.
- [ ] Dropdowns de vínculo retornam somente registros do tenant; nenhuma tela pede GUID.
- [ ] Rejeição de evidência e revogação de certificado exigem motivo.
- [ ] Dossiê com pendência obrigatória não aprova.
- [ ] Consulta pública mostra somente campos essenciais e registra acesso com IP hasheado.
- [ ] Dashboard e CSV funcionam com banco vazio e com dados.

## Sprint 24 — Operação 360
- [ ] Criar tarefa usando dropdown de usuário ativo e validar título/prazo/prioridade.
- [ ] Confirmar descrição obrigatória na conclusão e motivo no cancelamento.
- [ ] Avaliar regras duas vezes e confirmar deduplicação de estoque/financeiro/SLA.
- [ ] Marcar alerta lido e resolvido preservando eventos.
- [ ] Aprovar e reprovar workflow; testar permissão, motivo e segregação.
- [ ] Ler uma/todas notificações e validar isolamento de destinatário/tenant.
- [ ] Filtrar agenda por período, módulo, responsável e prioridade.
- [ ] Confirmar dashboard vazio e populado e outbox não enviada sem provider.
- [ ] Executar restore, build, testes e `scripts/validate-full-sql.sh`.

## Sprint 25
- [ ] Dashboard vazio e populado não quebra; gráficos não inventam pontos.
- [ ] Período, propriedade, safra, cultura, cliente, status, região e responsável respeitam seleção e tenant.
- [ ] Relatório vazio/populado e CSV UTF-8 conferem com a consulta.
- [ ] Usuário sem permissão recebe 403 em consulta/exportação.
- [ ] Latitude/longitude inválidas e rota sem pontos são rejeitadas.
- [ ] Dois tenants não compartilham widgets, filtros, dropdowns, mapas ou exports.
- [ ] Layout homologado em 360/768/1280/1920 px, teclado e zoom 200%.

## Sprint 26 — Mobile/PWA

- [ ] Validar `/field` em 360 px, 390 px, tablet e desktop.
- [ ] Alternar modo avião e confirmar persistência real no IndexedDB.
- [ ] Reenviar a mesma chave e confirmar ausência de duplicidade.
- [ ] Negar GPS e validar justificativa manual obrigatória.
- [ ] Testar latitude fora da faixa e arquivo/MIME/tamanho inválidos.
- [ ] Confirmar que service worker não cacheia APIs/páginas privadas.
- [ ] Confirmar isolamento entre tenants e permissão `mobile.write`.

## Sprint 27 — Portal externo

- [ ] Aceitar convite válido e rejeitar expirado, revogado e já usado.
- [ ] Confirmar que token/senha não aparecem em banco aberto, logs ou payload da outbox.
- [ ] Confirmar que JWT externo recebe apenas `portal.access` e obtém 403 nas APIs internas.
- [ ] Repetir dashboard, solicitações, cotações e comunicados com dois tenants.
- [ ] Rejeitar cotação vazia, quantidade zero e quantidade indisponível.
- [ ] Validar login, aceite, catálogo, cotação e solicitação em 360, 768 e 1440 px, teclado e leitor de tela.
- [ ] Confirmar estado vazio sem dados e ausência de botões sem ação.
- [ ] Validar upload por extensão, MIME, tamanho e autorização no módulo documental antes da homologação.

## Sprint 28 — Qualidade e Compliance
Implementação persistente de requisitos configuráveis, especificações versionadas, inspeções, decisão auditável de lotes, não conformidades/CAPA, auditorias, beneficiamento e prontidão de exportação. Homologar regras e UX conforme [Qualidade e Compliance](QUALITY-COMPLIANCE.md) e [Prontidão para exportação](EXPORT-READINESS.md).

## Sprint 29 — SaaS

- [ ] Admin Tenant recebe 403 e nao visualiza menu da Administracao SaaS.
- [ ] Suspender/reativar/cancelar/override sem motivo falha no frontend e backend.
- [ ] Feature bloqueada desaparece do menu e a URL direta e bloqueada.
- [ ] Consumo em 80% alerta; em 100% bloqueia nova inclusao sem apagar registros.
- [ ] Baixa manual sem data falha e nenhuma cobranca e paga automaticamente.
- [ ] Onboarding incompleto nao conclui; logo/tamanho/cor invalidos sao rejeitados.
- [ ] Busca, dropdown, CSV, dashboard e auditoria nunca retornam tenant diferente.
- [ ] Validar 360, 768 e 1440 px, teclado, foco, contraste, loading e estado vazio.

## Sprint 30
- [ ] Validar RLS com dois tenants e permissões de XML/exportação.
- [ ] Confirmar que chave aparece uma vez, hash é persistido e revogação retorna 401.
- [ ] Confirmar escopo insuficiente (403) e rate limit (429).
- [ ] Testar webhook HTTPS/HMAC, falha real, backoff, retry e cancelamento.
- [ ] Importar CSV válido/inválido/duplicado e exportar arquivo vazio com cabeçalho.
- [ ] Importar XML fiscal, bloquear duplicidade e bloquear envio sem provider/certificado.
- [ ] Homologar 320/768/1024/1440 px, teclado, loading e estados vazios.

## Sprint 33 — atendimento e Customer Success

- [ ] Validar isolamento de chamados, artigos, feedback e dashboards entre dois tenants.
- [ ] Validar cancelamento/reabertura sem motivo e resolução sem resposta.
- [ ] Validar SLA Start/Enterprise, vencimento e alerta crítico.
- [ ] Validar rascunho/arquivo de artigo e avaliação única.
- [ ] Validar bloqueio do Go-live antes da homologação.
- [ ] Validar trilha vencida, nota 1–5 e feedback 0–10.
- [ ] Validar backlog invisível ao usuário externo e leitura de release.
- [ ] Testar 360, 768, 1024 e 1440 px, teclado e leitor de tela.
- [ ] Confirmar que nenhum campo solicita GUID e nenhum arquivo binário foi criado.

## Sprint 34 — SST Rural
- [ ] Validar dropdowns tenant-safe e ausência de ID/GUID manual.
- [ ] Validar duplicidade de documento/matrícula e bloqueio de EPI para inativo.
- [ ] Validar risco crítico/alerta, vencimentos, incidente grave/investigação e checklist obrigatório.
- [ ] Homologar `sst.medical.read`, logs sem saúde/documentos pessoais e download autorizado.
- [ ] Homologar vazio/loading/erro, teclado, contraste e 320/768/1440 px.
- [ ] Confirmar que nenhum arquivo binário foi produzido.

## Sprint 35 — homologação Frota

- [ ] Criar/editar ativo e validar código/placa duplicados no mesmo tenant.
- [ ] Confirmar que dropdowns nunca exibem outro tenant nem pedem GUID.
- [ ] Bloquear ativo inativo/baixado/vendido e OS crítica; testar conclusão/cancelamento.
- [ ] Rejeitar periodicidade, peça, custo ou abastecimento inválido e medidor regressivo.
- [ ] Validar custo idempotente, parada, disponibilidade, alertas, CSV e dashboard vazio.
- [ ] Homologar permissões `fleet.*`/`maintenance.*`, auditoria e ausência de dados sensíveis em logs.
- [ ] Testar 320, 768, 1024 e 1440 px, teclado, foco, contraste, loading, erro e empty state.
- [ ] Confirmar que nenhum arquivo binário novo foi produzido.

## Sprint 36 — Financeiro e Controladoria

Central financeira real com plano de contas, centros de custo, títulos, baixas e conciliação manual autorizada, orçamento versionado, fluxo de caixa, DRE, rentabilidade, CSV, auditoria, RLS e design executivo responsivo. PostgreSQL é externo por connection string e o full install inclui o schema 3.6.0. Homologar estados vazios, validações, permissões e isolamento por tenant. Nenhum arquivo binário deve ser gerado.
