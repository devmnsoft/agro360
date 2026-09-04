# Checklist de homologação da Release Candidate

## Sprint corretiva — dashboard pecuário e UX guiada

- [ ] Executar `dotnet restore`, `dotnet build`, `dotnet test` e `dotnet format`.
- [ ] Abrir `/swagger`, autenticar com usuário real, renovar o token e confirmar que erros mostram `traceId` sem stack trace.
- [ ] Abrir os dashboards principal e pecuário com banco vazio e populado; conferir zeros/listas vazias e dados reais.
- [ ] Confirmar aliases PascalCase, projeções `Row` e conversões explícitas nas agregações Dapper.
- [ ] Percorrer login, dashboards, tenants, usuários, perfis e pecuária; conferir mini manual, tooltips, toasts e confirmações.
- [ ] Homologar tema claro, contraste, teclado e larguras 320/768/1440 px.
- [ ] Confirmar que o commit não contém binários, segredos ou artefatos de build.
- [ ] Executar o SQL duas vezes com `ON_ERROR_STOP=1` e confirmar um único tenant `santa-clara`, usuário, perfil e vínculos de permissão.
- [ ] Entrar como Super Administrador e Administrador Santa Clara; conferir os 18 grupos na ordem prevista e ausência de itens sem permissão.
- [ ] Conferir `Como usar esta tela` em todas as Razor Pages, no desktop e mobile, inclusive após navegar diretamente por URL.
- [ ] Simular API indisponível, 400, 401, 403 e 404; validar loading finito, limpeza da sessão e mensagem acionável com código de suporte quando disponível.

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

## Sprint 37 — Compras e Suprimentos
- [ ] Executar restore, build e testes; aplicar o full SQL em PostgreSQL externo vazio.
- [ ] Validar dois tenants e permissões de solicitar, homologar, aprovar, receber e exportar.
- [ ] Validar estados vazios, filtros, CSV, responsividade e ausência de entrada manual de GUID.
- [ ] Exercitar bloqueios, menor preço, recebimento parcial, lote, validade e excesso.
- [ ] Confirmar que integrações nunca aparecem concluídas sem confirmação real.

## Sprint 38 — Produção Agroindustrial
- [ ] Criar, versionar e aprovar formulação; confirmar que versão aprovada não é editada.
- [ ] Criar ordem e negar liberação sem item; testar reserva insuficiente e permissões.
- [ ] Apontar etapa comum/crítica, temperatura, perda/refugo e evidência sem quantidade negativa.
- [ ] Consumir saldo real; negar excesso, vencido sem justificativa e bloqueado.
- [ ] Gerar lote, reprovar/bloquear/liberar qualidade e impedir encerramento pendente.
- [ ] Registrar parada com motivo; validar alerta/workflow configurado.
- [ ] Conferir rendimento, custo, dashboard vazio, CSV e rastreabilidade completa.
- [ ] Repetir chamadas com tenant distinto e confirmar ausência/404, RLS e permissões.

## Sprint 39 — Exportação e Trading
- [x] Migração e SQL completo incluem constraints, FKs e índices por tenant.
- [x] Total, câmbio e margem calculados com decimal no backend.
- [x] Cliente bloqueado/reprovado não recebe contrato.
- [x] Contrato e embarque validam estados, lote e saldo real.
- [x] API usa permissões específicas e consultas tenant-safe.
- [x] Dashboard e estados vazios não usam mocks.
- [x] CSV autorizado aplica filtros do tenant.
- [ ] Validar restore/build/test em ambiente com .NET SDK 9 (SDK ausente no executor atual).

## Sprint 40 — Fiscal e Faturamento
- [ ] Aplicar migration 040 em PostgreSQL limpo e validar RLS com dois tenants.
- [ ] Criar/inativar operação; criar regra decimal; validar conflito e vencimento.
- [ ] Faturar com cliente/item pesquisável; conferir total backend, parcelas, estoque e serviço.
- [ ] Bloquear lote bloqueado e saldo insuficiente; confirmar auditoria de usuário/data.
- [ ] Registrar NF-e/NFS-e/CT-e/MDF-e sem simular autorização; exigir motivo em rejeição/cancelamento.
- [ ] Conferir compra divergente e justificativa; verificar pendências financeira/estoque.
- [ ] Exportar os onze CSVs com filtros e autorização; testar estados vazios e responsividade.

## Sprint 41 — Inteligência executiva
- [ ] Painel vazio mostra indisponível e não zero inventado.
- [ ] Indicador sem fonte válida e percentual fora de 0–100 são rejeitados.
- [ ] Snapshot de falha preserva histórico e o painel continua disponível.
- [ ] Alerta crítico não é ignorado sem justificativa; resolução exige comentário.
- [ ] Recomendação alta/crítica não é rejeitada sem motivo.
- [ ] Filtros, CSV, detalhes e decisões não atravessam tenant.
- [ ] Usuário sem `intelligence.strategic` não gerencia indicador estratégico.
- [ ] Navegação, estados vazio/loading/erro e modal funcionam em 360, 768 e 1440 px.

## Sprint 42 — Sustentabilidade e ESG
- [ ] Aplicar migration 042 em PostgreSQL limpo e atualizado; validar RLS com dois tenants.
- [ ] Criar conformidade e rejeitar área produtiva maior que total/área negativa.
- [ ] Validar CAR informado, vencimento documental, reprovação com motivo e aprovação com usuário/data.
- [ ] Manter indicador sem fonte inválido e percentual entre 0–100; preservar medições.
- [ ] Confirmar emissão decimal e estado `PENDING_FACTOR` sem fator.
- [ ] Validar fornecedor crítico bloqueado na integração de compras configurada.
- [ ] Validar lote sem origem, fazenda bloqueada e ressalva sem justificativa.
- [ ] Validar projeto certificado somente com documento real; auditoria concluída imutável na aplicação.
- [ ] Validar responsável em ação crítica, comentário na conclusão, motivo no cancelamento e atraso no dashboard.
- [ ] Exercitar 14 CSVs com filtros/permissão/tenant e conteúdo vazio.
- [ ] Conferir responsividade, estados vazio/loading/erro e dropdown de propriedade sem GUID manual.
- [ ] Executar `dotnet restore`, `dotnet build` e `dotnet test` sem Docker.

## Sprint 43 — Campo Mobile PWA

- [ ] Aplicar migration 043 em banco limpo/atualizado e validar RLS com dois tenants.
- [ ] Validar atalhos de cada perfil e bloqueio por URL sem permissão.
- [ ] Criar checklist com item; bloquear vazio; aprovar e exigir nova versão para alteração.
- [ ] Executar checklist; bloquear resposta/evidência/observação/assinatura/localização obrigatórias ausentes.
- [ ] Aprovar evidência; exigir motivo na reprovação e referência real para foto/documento.
- [ ] Gerar QR de entidade real; rejeitar inexistente, outro tenant, inativo/bloqueado e token inválido.
- [ ] Bloquear ocorrência crítica sem responsável, resolução sem comentário e cancelamento sem motivo.
- [ ] Operar offline, recarregar, retentar sem duplicar e preservar falha; resolver conflito com comentário.
- [ ] Alterar conteúdo assinado e confirmar `CONTENT_CHANGED`; validar coordenadas e recusa de permissão.
- [ ] Exportar os 12 CSVs vazios e preenchidos com filtros, tenant e permissões.
- [ ] Conferir loading/empty/error, foco, toque e layout em 360, 768 e 1440 px; nenhum GUID manual.
- [ ] Executar `dotnet restore`, `dotnet build` e `dotnet test` sem Docker.


---

# Checklist QA — Sprint 45

- [ ] restore, build e testes em .NET 10
- [ ] único super administrador ativo e trilha global
- [ ] Fazenda Santa Clara: administrador, usuários, perfis e isolamento
- [ ] login por e-mail/CPF/CNPJ; bloqueios e limite de tentativas
- [ ] tenant/módulo/permissão bloqueados também por URL direta
- [ ] cobrança sem duplicidade, total no backend e baixa externa auditada
- [ ] fallback pt-BR e decimal nas três culturas
- [ ] ajuda contextual específica nas telas principais
- [ ] CSV filtrado, autorizado e sem segredo
- [ ] teclado, mobile, estados vazio/loading/erro
- [ ] full SQL equivale à sequência de migrations
- [ ] nenhum binário novo

# Checklist QA — Sprint 46
- [ ] Criar módulo, solicitar ativação, aprovar como super administrador e negar rota quando inativo.
- [ ] Criar/bloquear parceiro, conceder acesso temporário em um tenant e revogar sem afetar outro.
- [ ] Criar aplicação/chave; confirmar exibição única, somente hash no banco, escopo, rate limit e revogação.
- [ ] Criar webhook HTTPS; registrar sucesso/falha/retry finito e confirmar que falha não aborta operação.
- [ ] Validar RLS, auditoria, ações reais, ajuda e pt-BR/en-US/es-ES em todas as áreas novas.

## Sprint 47 — CRM e ciclo do cliente

A plataforma integra CRM, pipeline, propostas com total no backend, contratos SaaS, implantação assistida, suporte/SLA, saúde explicável, conhecimento e portal isolado. As novas rotas exigem permissões específicas, as tabelas usam auditoria/RLS por tenant e toda comunicação sem provedor permanece pendente na outbox. A experiência responsiva usa funil, timeline, badges e o componente recolhível **Como usar esta tela**. Consulte `docs/CRM-COMMERCIAL.md`, `docs/CUSTOMER-SUCCESS.md`, `docs/SUPPORT.md` e a migração `047_crm_customer_lifecycle.sql`.

## Sprint 48 — Governança, Migração, LGPD e Performance

Governança persistente e isolada por tenant: importação CSV pré-validada, qualidade de dados, exportação gerencial segura, solicitações LGPD, auditoria avançada, sessões e telemetria de performance. A migração `048_data_governance.sql` cria constraints, FKs, índices e RLS. Consulte `docs/DATA-GOVERNANCE.md`, `docs/IMPORT-MIGRATION.md`, `docs/LGPD-SECURITY.md` e `docs/PERFORMANCE.md`.

- [ ] CSV inválido, documento/e-mail inválido e duplicidade informam linha/coluna.
- [ ] Confirmação crítica, cancelamento e reprocessamento respeitam o estado do lote.
- [ ] Importação, achados, exportação, LGPD e auditoria não atravessam tenant.
- [ ] Exportações excluem credenciais, hashes e segredos e respeitam permissão.
- [ ] Recusa LGPD e mudança de achado exigem justificativa.
- [ ] Usuário/tenant bloqueado, módulo inativo e rota sem permissão retornam 403.
- [ ] Paginação limita 100 registros e consultas usam parâmetros e índices.

## Sprint 49 — Workflows
- [ ] Criar, versionar e iniciar fluxo; bloquear ativação sem etapa e edição da versão ativa.
- [ ] Exigir comentário/evidência, motivo de cancelamento/reabertura/reprovação e impedir autoaprovação segregada.
- [ ] Validar tarefa atrasada, alerta/escalonamento crítico e agenda no fuso do tenant.
- [ ] Persistir notificação interna e `PENDING_NOT_CONFIGURED` para canal externo sem provider.
- [ ] Validar template nos três idiomas, variável desconhecida e HTML inseguro.
- [ ] Bloquear automação inválida/SQL e confirmar idempotência por chave.
- [ ] Testar API/Dapper com PostgreSQL, RLS entre tenants, super admin e ajuda contextual em mobile.

## Sprint 50 — formulários e ajuda contextual

Validação backend continua sendo a fonte da verdade; a interface oferece resumo e erros por campo, loading, confirmação com consequência real e motivo nas ações definidas pela regra. Ajuda curta é recolhível e localizada em pt-BR, en-US e es-ES. Configurações e eventos de UX usam o schema `ui`, auditoria e RLS por tenant. Detalhes: `docs/UX-FORMS-VALIDATION.md` e `docs/CONTEXTUAL-HELP.md`.

### Sprint corretiva — schema PostgreSQL canônico
O banco consolidado usa somente `agro360`; nomes de tabela carregam o prefixo do módulo e SQL estático/Dapper deve ser qualificado. A homologação executa restore/build/test, instala o SQL com `ON_ERROR_STOP=1`, confirma a ausência dos schemas legados e testa isolamento: Super Admin enxerga todos os tenants; administradores e usuários permanecem limitados ao tenant ativo. Bloqueios de usuário/cliente, alterações de plano/módulo, cobrança e acesso global exigem confirmação, mensagem clara e auditoria. Login aceita e normaliza e-mail, CPF ou CNPJ; formulários usam seletores em vez de GUIDs e oferecem ajuda contextual em pt-BR, en-US e es-ES.

O bootstrap seguro recebe `AGRO360_SUPERADMIN_INITIAL_PASSWORD`, aplica o hash real da aplicação e marca troca obrigatória. Dados canônicos: Super Administrador MNSOFT; `superadmin@mnsoft.com.br`; CNPJ `18.160.057/0001-13`; perfil `SUPER_ADMIN`; status Ativo. Não existe senha padrão em produção.
# Checklist da sprint de prontidão operacional

- [ ] Entrar como Super Administrador em `agro360-platform` e validar menus globais.
- [ ] Entrar como Administrador do Cliente em `santa-clara` e confirmar isolamento.
- [ ] Abrir `/Deployment`, conferir contagens reais e seguir cada próxima ação.
- [ ] Abrir `/Work`, avaliar regras e confirmar que alertas correspondem a registros persistidos.
- [ ] Invalidar o access token, confirmar refresh único e mensagem de sessão expirada em falha.
- [ ] Validar estado vazio e responsividade a 100% de zoom e largura de 360 px.
