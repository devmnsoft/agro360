# Checklist de homologação da Release Candidate

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
