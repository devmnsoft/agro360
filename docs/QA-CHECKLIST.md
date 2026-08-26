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
