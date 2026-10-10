(() => {
    "use strict";
    const root = "/api/procurement";
    const idempotencyKeys = new WeakMap();
    // Vocabulário canônico de cotações (mesmo de QuotationService): aberta aceita propostas; conversível gera pedidos.
    const openQuotationStatuses = ["SENT", "PARTIAL", "RESPONDED", "ANALYSIS"];
    const convertibleQuotationStatuses = ["PARTIAL", "RESPONDED", "ANALYSIS", "APPROVED"];
    let pendingOrderHighlight = null;
    const escape = value => String(value ?? "").replace(/[&<>"']/g, char => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[char]));
    const t = value => escape(value ?? "—");
    const request = (path, options = {}) => window.agro360Api(`${root}/${path}`, options);
    const badge = value => `<span class="proc-badge">${escape(value)}</span>`;
    // Formatação segue o idioma da interface (agro360.js), com fallback para a convenção localStorage/lang.
    // A moeda exibida permanece BRL: idioma é apresentação, nunca contrato.
    const culture = () => (window.agro360Culture ? window.agro360Culture() : null) || localStorage.getItem("agro360.culture") || document.documentElement.lang || "pt-BR";
    const money = value => Number(value || 0).toLocaleString(culture(), { style: "currency", currency: "BRL" });
    const fmtDate = value => value ? new Date(`${String(value).slice(0, 10)}T00:00`).toLocaleDateString(culture()) : "—";
    const fmtDateTime = value => value ? new Date(value).toLocaleString(culture()) : "—";
    const optionList = (rows, valueKey, labelFn, placeholder) => `<option value="">${escape(placeholder)}</option>` + rows.map(item => `<option value="${escape(item[valueKey])}">${labelFn(item)}</option>`).join("");

    // ── i18n do módulo: textos da tela de Compras nas quatro línguas; pt-BR é a fonte canônica.
    // O idioma muda apenas a apresentação: valores, status canônicos, permissões e contrato permanecem BRL/pt-independentes.
    const PROC_I18N = {
        "pt-BR": {
            "heroEyebrow": "Cadeia de suprimentos", "heroTitleA": "Compras", "heroTitleB": "Suprimentos",
            "heroSubtitle": "Da necessidade ao recebimento, com alçada, evidência e rastreabilidade por unidade operacional.",
            "refresh": "Atualizar indicadores", "kpiLoading": "Carregando indicadores reais…",
            "navAria": "Áreas de compras",
            "tabSuppliers": "Fornecedores", "tabCatalog": "Catálogo", "tabRequisitions": "Requisições", "tabApprovals": "Aprovações", "tabQuotations": "Cotações", "tabOrders": "Pedidos", "tabReceipts": "Recebimentos", "tabReturns": "Devoluções ao fornecedor", "tabMatches": "Conferência e divergências", "tabReports": "Relatórios",
            "suppliersTitle": "Fornecedores e homologação", "suppliersDesc": "Cadastro, risco e validade documental.", "newSupplier": "Novo fornecedor", "phSupplierSearch": "Buscar razão social ou fantasia", "allStatuses": "Todos os status", "filterBtn": "Filtrar", "exportCsv": "Exportar CSV",
            "catalogTitle": "Catálogo comprável", "catalogDesc": "Materiais, serviços e ativos com requisitos de recebimento.", "newItem": "Novo item", "phCatalogSearch": "Buscar item ou código", "activeInactive": "Ativos e inativos",
            "reqsTitle": "Requisições internas", "reqsDesc": "Necessidades operacionais com prioridade e centro de custo.", "newReq": "Nova requisição",
            "reqHelpSummary": "Fluxo e responsabilidades", "reqHelpBody": "Salve em rascunho para revisar ou envie à aprovação. Após o envio, rejeitar e cancelar exige justificativa; o solicitante não pode aprovar a própria demanda. Pedidos consomem somente o saldo aprovado.",
            "phReqSearch": "Buscar número", "allOpt": "Todos",
            "quotTitle": "Cotações", "quotDesc": "Participantes, propostas registradas e conversão auditável para pedido.", "newQuotation": "Nova cotação",
            "quotHelpSummary": "Jornada da cotação", "quotHelpBody": "<b>Fluxo:</b> abra a cotação a partir de uma requisição aprovada com ao menos dois fornecedores participantes, registre as propostas reais recebidas de cada fornecedor, compare os totais por item, decida o vencedor de cada item (escolher proposta acima da menor exige justificativa auditável) e converta em pedidos — um por fornecedor vencedor, sem duplicar se repetido. Nenhuma proposta é simulada nem enviada automaticamente.",
            "phQuotSearch": "Buscar número da cotação ou da requisição",
            "approvalsTitle": "Aprovações", "approvalsDesc": "Fila real de decisões de alçada: requisições enviadas e pedidos criados aguardando aprovação.",
            "approvalsReqsTitle": "Requisições para decisão", "approvalsOrdersTitle": "Pedidos para decisão",
            "ordersTitle": "Pedidos de compra", "ordersDesc": "Aprovação, entrega prevista e acompanhamento de saldo.", "newOrder": "Novo pedido", "phOrderSearch": "Número ou fornecedor",
            "receiptsTitle": "Recebimento e conferência", "receiptsDesc": "Presença física, decisão de qualidade e disponibilidade são eventos separados.", "newReceipt": "Registrar recebimento",
            "receiptHelpSummary": "Como usar", "receiptHelpBody": "Selecione um pedido aprovado, confira o saldo calculado, informe quantidade, lote, validade e depósito, depois revise e confirme. Materiais com inspeção ou validade vencida permanecem bloqueados; somente uma decisão autorizada libera a quantidade aceita. A previsão financeira fica aberta e não representa pagamento.",
            "phReceiptSearch": "Recebimento, pedido ou fornecedor",
            "returnsTitle": "Devoluções ao fornecedor", "returnsDesc": "Baixa auditável no estoque com crédito aberto; decisão separada do registro.",
            "returnHelpSummary": "Como usar", "returnHelpBody": "Registre a devolução no detalhe de um recebimento concluído ou divergente, respeitando o saldo aceito e ainda não devolvido de cada linha. A aprovação baixa o estoque, reabre o saldo do pedido e gera crédito aberto — nenhuma conta é paga ou compensada automaticamente. Quem registrou a devolução não pode aprová-la.",
            "phReturnSearch": "Devolução, recebimento ou fornecedor",
            "matchesTitle": "Conferência de cobranças", "matchesDesc": "Compara contrato, aceite e cobrança sem presumir validação fiscal ou pagamento.", "newMatch": "Conferir cobrança",
            "matchHelpSummary": "Como usar", "matchHelpBody": "<b>Pré-requisitos:</b> pedido aprovado e recebimento aceito. Selecione explicitamente a linha recebida, informe os valores do documento e confirme. <b>Resultado:</b> diferenças dentro das tolerâncias ficam conferidas; as demais aguardam decisão autorizada. O documento continua “não validado fiscalmente”; conferência aprovada não significa pagamento e nenhuma conta é paga por esta ação. Consulte o manual de Compras e Suprimentos.",
            "searchLbl": "Busca", "phMatchSearch": "Documento, pedido ou fornecedor", "situationLbl": "Situação", "tolerancesBtn": "Tolerâncias",
            "reportsTitle": "Relatórios de compras", "reportsDesc": "Arquivos CSV respeitam filtros, permissões e tenant.", "reportSuppliers": "Fornecedores", "reportRequisitions": "Requisições", "reportOrders": "Pedidos de compra",
            "lineItem": "Item do catálogo *", "qtyRequired": "Quantidade *", "unitRequired": "Unidade *", "removeLineAria": "Remover linha", "loadingItems": "Carregando itens ativos…", "lineItemSimple": "Item *", "unitPriceRequired": "Preço unitário *", "discountLbl": "Desconto",
            "closeAria": "Fechar", "select": "Selecione", "daysUnit": "{0} dias",
            "hSupplier": "Fornecedor", "hCategory": "Categoria", "hPrazo": "Prazo", "hStatus": "Status", "hCode": "Código", "hItem": "Item", "hType": "Tipo", "hSituation": "Situação", "hNumber": "Número", "hPriority": "Prioridade", "hNeeded": "Necessidade", "hItems": "Itens", "hQuotations": "Cotações", "hRequisition": "Requisição", "hUnitCol": "Unidade", "hParticipants": "Participantes", "hWithProposal": "Com proposta", "hGeneratedOrders": "Pedidos gerados", "hValidity": "Validade", "hAction": "Ação", "hDeliveryOn": "Entrega", "hTotal": "Total", "hReturn": "Devolução", "hReceipt": "Recebimento", "hOrder": "Pedido", "hDate": "Data", "hStock": "Estoque", "hFinance": "Financeiro", "hNextAction": "Próxima ação", "hDocument": "Documento", "hIssuedOn": "Emissão", "hDifference": "Diferença", "hDecision": "Decisão", "waiting": "Aguardando",
            "openQuotation": "Abrir cotação", "compareBtn": "Comparar", "detailsBtn": "Detalhar", "registerProposal": "Registrar proposta", "inspect": "Inspecionar", "viewBtn": "Consultar", "pendingCount": "Ver {0} pendência(s)", "decideBtn": "Decidir", "approveBtn": "Aprovar", "rejectBtn": "Rejeitar", "cancelBtn": "Cancelar", "approveOrderBtn": "Aprovar pedido", "cancelOrderBtn": "Cancelar pedido",
            "kpiDraft": "Rascunhos", "kpiAwaitingApproval": "Aguardando aprovação", "kpiApprovedBalance": "Aprovadas com saldo", "kpiUrgent": "Urgentes em fluxo", "kpiQuotations": "Cotações em andamento", "kpiOrdersApprove": "Pedidos para aprovar", "kpiDivergent": "Recebimentos divergentes", "kpiActiveSuppliers": "Fornecedores ativos", "kpiBlockedSuppliers": "Fornecedores bloqueados", "kpiPartial": "Parcialmente recebidos", "kpiMonthPurchased": "Comprado no mês",
            "loading": "Carregando…", "empty": "Nenhum registro encontrado para os filtros aplicados.", "operationFailed": "Operação não concluída",
            "receiptFor": "Pedido {0}", "itemsLotsAvail": "Itens, lotes e disponibilidade", "colItemLot": "Item / lote", "colOrdered": "Pedido", "colReceived": "Recebido", "colBlocked": "Bloqueado", "colAvailable": "Disponível", "colRejected": "Rejeitado", "colAction": "Ação", "noLot": "Sem lote",
            "qualityHistory": "Histórico de qualidade", "acceptedRejected": "aceita {0}, rejeitada {1}", "noReserve": "Sem ressalva", "noQualityDecisions": "Nenhuma decisão registrada.",
            "returnCreate": "Devolver itens ao fornecedor", "returnCreateHint": "Apenas o saldo aceito e ainda não devolvido; aprovação por outro usuário com alçada.",
            "waitingDecision": "{0} unidade(s) aguardando decisão.",
            "loadingReceipt": "Carregando recebimento…", "loadingReturn": "Carregando devolução…", "loadingMatch": "Carregando conferência…", "loadingQuotation": "Carregando cotação…", "comparing": "Comparando cotações…",
            "loadingReturnable": "Carregando itens retornáveis…", "colAccepted": "Aceito", "colReturned": "Já devolvido", "colReturnQty": "Quantidade a devolver", "noEligibleItems": "Nenhum item com saldo aceito e ainda não devolvido neste recebimento.",
            "reasonLbl": "Motivo:", "decisionLbl": "Decisão:", "returnedItems": "Itens devolvidos", "colUnitCost": "Custo unitário",
            "creditText": "Crédito {0}:", "creditOpenNote": "crédito aberto não compensa títulos nem efetua pagamento.", "noCreditYet": "Nenhum crédito gerado enquanto a devolução aguarda decisão.", "decideReturnBtn": "Decidir devolução",
            "documentLbl": "Documento {0}", "contracted": "Contratado:", "billedLbl": "Cobrado:", "differenceLbl": "Diferença:", "zeroBase": "(base zero)", "fiscalValidation": "Validação fiscal:", "matchApprovedNote": "Conferência aprovada não significa pagamento.", "linkedLines": "Linhas vinculadas", "colQty": "Quantidade", "colPrice": "Preço",
            "divergencesLbl": "Divergências", "impactLbl": "Impacto:", "actionLbl": "Ação:", "resolveException": "Decidir exceção", "decisionResult": "Decisão:", "noDivergences": "Nenhuma divergência fora da tolerância registrada.",
            "quotOption": "{0} · prioridade {1} · necessidade {2}", "openFailed": "Não foi possível abrir a cotação", "noEligibleParticipants": "A cotação não tem participantes aptos a novas propostas.", "registerFailed": "Não foi possível registrar a proposta", "selParticipantPh": "Selecione o fornecedor participante", "quotItemOption": "{0} · saldo {1} {2}", "selQuotedItemPh": "Selecione o item da cotação",
            "requisitionOf": "Requisição {0} ({1})", "unitOf": "Unidade {0}", "proposalsUntil": "Propostas até {0}",
            "itemsProposals": "Itens e propostas", "colBalance": "Saldo", "colLowest": "Menor total", "colWithOffers": "Fornecedores com proposta", "noOffer": "sem proposta",
            "participantsSection": "Participantes", "colQuotedItems": "Itens cotados", "colDeliveryDays": "Entrega", "colPayment": "Pagamento",
            "registeredDecisions": "Decisões registradas", "colChosenSupplier": "Fornecedor escolhido", "colChosenTotal": "Total escolhido",
            "generatedOrdersSection": "Pedidos gerados",
            "winnersSelection": "Escolha dos vencedores por item", "defaultSelectionNote": "A seleção padrão é a menor proposta. Escolher uma proposta acima da menor exige justificativa auditável.", "colWinningProposal": "Proposta vencedora", "alreadyDecided": "já decidido", "lowestTag": "menor",
            "justificationField": "Justificativa (obrigatória quando a escolha ficar acima da menor proposta)", "registerDecisionBtn": "Registrar decisão",
            "convertNote": "A conversão gera um pedido por fornecedor vencedor, reaproveitando a decisão registrada; repetir a conversão devolve os mesmos pedidos sem duplicar.", "convertBtn": "Converter em pedidos",
            "selectProposalMsg": "Selecione uma proposta para cada item cotado.", "aboveLowestMsg": "A escolha ficou acima da menor proposta: justifique com pelo menos 3 caracteres.",
            "confirmDecisionTitle": "Confirmar decisão da cotação", "confirmDecisionMsg": "As escolhas ficam registradas e habilitam a conversão em pedidos. Nenhum pedido é criado nesta etapa.",
            "decisionRegistered": "Decisão registrada", "decisionRegisteredSub": "A cotação está pronta para conversão em pedidos.",
            "convertTitle": "Converter cotação em pedidos", "convertMsg": "Será criado um pedido de compra por fornecedor vencedor, vinculado à requisição e às decisões aprovadas. A conversão é uma transação única: falha em qualquer fornecedor não deixa pedido parcial, e repetir devolve os mesmos pedidos (cancelados incluem-se — o reabastecimento vem de novo pedido na requisição).",
            "confirmLowestTitle": "Confirmar recomendação de menor preço", "confirmLowestMsg": "{0} Nenhum pedido foi criado ainda. Deseja confirmar a conversão aplicando o menor preço aos itens restantes?",
            "converted": "Cotação convertida", "convertedSub": "{0} pedido(s) vinculado(s) à cotação para aprovação/entrega conforme as decisões.", "convertFailed": "Não foi possível converter",
            "noProposalsYet": "Esta requisição ainda não possui propostas registradas para comparar.",
            "lowestNote": "Menor total da mercadoria (unitário × quantidade − desconto):", "fullCostNote": "menor custo completo com frete e tributos:", "validUntilNote": "propostas válidas até {0}",
            "colQuotation": "Cotação", "colUnitPrice": "Unitário", "colDiscount": "Desconto", "colFreight": "Frete", "colTaxes": "Tributos", "colGoodsTotal": "Total mercadoria", "colFullCost": "Custo completo", "lowestProposal": "MENOR PROPOSTA", "decidedTag": "DECIDIDO",
            "cancelAction": "Cancelar", "cancelTitle": "Cancelar cotação", "cancelHint": "Somente cotações abertas podem ser canceladas. A justificativa permanece no histórico auditável.", "cancelReason": "Justificativa do cancelamento *", "phCancelReason": "Explique o motivo real do cancelamento", "cancelConfirmBtn": "Confirmar cancelamento", "cancelledToast": "Cotação cancelada", "cancelledToastSub": "A cotação foi arquivada com justificativa auditável; a requisição pode receber nova cotação.",
            "selectByName": "Selecione pelo nome", "selectByNumber": "Selecione pelo número", "naService": "Não aplicável a serviço", "selectAccount": "Selecione a conta", "selectForMaterial": "Selecione para material/ativo", "useContext": "Usar contexto atual", "selectByCode": "Selecione pelo nome ou código", "listsUnavailable": "Listas indisponíveis", "listsUnavailableSub": "Não foi possível carregar todos os seletores autorizados.",
            "selPendingItemPh": "Selecione o item pendente", "noPendingBalance": "Pedido sem saldo pendente", "balanceWord": "saldo", "matchOption": "{0} · {1} · saldo {2} {3}", "selectMatchLine": "Selecione a linha conferida", "noAcceptedAvailable": "Sem aceite disponível",
            "confirmReceiveTitle": "Confirmar recebimento", "confirmReceiveMsg": "A operação fará a entrada física e criará previsões financeiras abertas. Escritas não são repetidas automaticamente.", "confirmReceiveBtn": "Receber",
            "minOneItem": "Informe ao menos um item com catálogo selecionado.", "saving": "Salvando…", "operationDone": "Operação concluída", "operationDoneSub": "Os vínculos de estoque e financeiro foram persistidos sem duplicação.",
            "confirmQualityTitle": "Confirmar decisão de qualidade", "confirmQualityMsg": "Somente a quantidade aceita será liberada. A decisão permanecerá no histórico.", "qualityRegisteredSub": "A disponibilidade foi atualizada somente para a quantidade aceita.",
            "confirmDivergence": "Confirmar decisão", "confirmDivergenceMsg": "A divergência será registrada como {0}. Isso não efetua pagamento.", "divergenceResolvedSub": "A conferência foi atualizada e o histórico preservado.",
            "confirmReturnTitle": "Confirmar devolução ao fornecedor", "confirmReturnMsg": "A devolução fica pendente de aprovação por outro usuário com alçada. Nenhum saldo muda antes da aprovação.", "confirmReturnBtn": "Registrar devolução",
            "minOneReturnQty": "Informe a quantidade de ao menos um item retornável.", "returnRegistered": "Devolução registrada", "returnRegisteredSub": "Aguardando decisão de um aprovador sem vínculo com o registro.",
            "approveReturnTitle": "Aprovar devolução", "approveReturnMsg": "O estoque será baixado, o saldo do pedido será reaberto e um crédito aberto será gerado. Nenhuma conta é paga ou compensada automaticamente.",
            "rejectReturnTitle": "Reprovar devolução", "rejectReturnMsg": "A devolução será arquivada sem efeitos em estoque, pedido ou financeiro.",
            "rejectRequiresReason": "A reprovação exige justificativa.", "returnApprovedToast": "Devolução aprovada", "returnApprovedSub": "Estoque baixado, saldo do pedido reaberto e crédito aberto registrado.", "returnRejectedToast": "Devolução reprovada", "returnRejectedSub": "Nada mudou em estoque, pedido ou financeiro.",
            "minTwoSuppliers": "Selecione ao menos dois fornecedores participantes.", "openingQuotation": "Abrindo cotação…", "quotationRequested": "Cotação solicitada", "quotationRequestedSub": "Participantes registrados; registre as propostas recebidas de cada fornecedor.",
            "registeringProposal": "Registrando proposta…", "proposalRegistered": "Proposta registrada", "proposalRegisteredSub": "A proposta entra na comparação e conta para a decisão da cotação.",
            "qualityDialogTitle": "Decisão de qualidade", "acceptedQty": "Quantidade aceita", "rejectedQty": "Quantidade rejeitada", "resultLbl": "Resultado", "qApproved": "Aprovado", "qConditional": "Aprovado com condição", "qRejected": "Reprovado", "evidenceLbl": "Evidência / documento", "phReportRef": "Número ou referência do laudo", "justificationField2": "Justificativa", "phJustReq": "Obrigatória para rejeição ou condição", "registerReleaseBtn": "Registrar e liberar elegível",
            "supplierDialogTitle": "Novo fornecedor", "legalName": "Razão social", "tradeName": "Nome fantasia", "taxDoc": "Documento fiscal", "typeLbl": "Tipo", "mainCategory": "Categoria principal", "emailLbl": "E-mail", "phoneLbl": "Telefone", "countryLbl": "País", "statusLbl": "Status", "stActive": "Ativo", "stReview": "Em homologação", "stInactive": "Inativo", "stBlocked": "Bloqueado", "avgDelivery": "Prazo médio (dias)", "notesLbl": "Observações", "saveSupplierBtn": "Salvar fornecedor",
            "catalogDialogTitle": "Novo item", "nameLbl": "Nome", "internalCode": "Código interno", "categoryLbl": "Categoria", "unitField": "Unidade", "stockProduct": "Produto de estoque", "phStockProduct": "Selecione para material/ativo", "minStock": "Estoque mínimo", "requirements": "Exigências", "reqLot": "Lote", "reqExpiry": "Validade", "reqDocument": "Documento", "reqInspection": "Inspeção", "reqApprovedSupplier": "Fornecedor homologado", "saveItemBtn": "Salvar item",
            "reqDialogTitle": "Nova requisição", "originLbl": "Origem / necessidade", "phOrigin": "Manutenção, Lavoura, Qualidade", "operationalUnit": "Unidade operacional", "priorityLbl": "Prioridade", "neededOn": "Data necessária", "justificationField3": "Justificativa", "itemsRequested": "Itens solicitados", "unitConsistency": "A unidade deve coincidir com o cadastro do item; conversões são explícitas.", "addItem": "Adicionar item", "submitNow": "Enviar imediatamente à fila de aprovação", "saveReqBtn": "Salvar requisição",
            "orderDialogTitle": "Novo pedido", "supplierLbl": "Fornecedor", "orderItems": "Itens do pedido", "paymentTerms": "Condição de pagamento", "deliveryOn": "Entrega prevista", "deliveryAddress": "Endereço de entrega", "createForApproval": "Criar para aprovação",
            "receiptDialogTitle": "Registrar recebimento", "approvedOrderSel": "Pedido aprovado", "receivedAt": "Data/hora", "warehouseLbl": "Depósito para entrada física", "financeAccount": "Conta da previsão financeira", "firstDueOn": "Primeiro vencimento", "installments": "Parcelas", "invoiceDoc": "Nota/documento", "overrideExcess": "Autorizar excesso", "excessJustification": "Justificativa do excesso", "notesWide": "Observações",
            "receiptFooter": "A confirmação registra a presença física. Itens com inspeção ou validade vencida ficam em quarentena e não entram no saldo disponível; a previsão financeira permanece aberta, nunca paga.", "confirmReceiveFull": "Conferir e receber",
            "pendingItemLbl": "Item pendente *", "lotLbl": "Lote do fornecedor", "expiryLbl": "Validade", "dupPendingItem": "O mesmo item pendente não pode aparecer em duas linhas do recebimento.", "minOnePendingLine": "Informe ao menos uma linha com item pendente e quantidade maior que zero.",
            "newQuotationTitle": "Nova cotação", "quotRequestHint": "Somente requisições aprovadas ou com saldo aprovado aceitam cotação, e existe no máximo uma cotação em aberto por requisição.", "approvedReqLbl": "Requisição aprovada *", "selApprovedReqPh": "Selecione a requisição aprovada", "dueDateLbl": "Prazo para receber propostas *", "participantsLbl": "Fornecedores participantes *", "loadingSuppliers": "Carregando fornecedores ativos…", "atLeastTwo": "Ao menos dois fornecedores. Segure Ctrl para marcar vários.", "requestQuotationBtn": "Solicitar cotação",
            "quoteTitle": "Registrar proposta recebida", "quoteHint": "Digitada exatamente como o fornecedor enviou; nada é cotado nem enviado automaticamente.", "participantLbl": "Fornecedor participante *", "quotedItemLbl": "Item cotado *", "unitPriceLbl": "Preço unitário *", "discountUnit": "Desconto por unidade", "deliveryDays": "Prazo de entrega (dias) *", "proposalValidity": "Validade da proposta", "taxesLbl": "Impostos", "registerQuoteBtn": "Registrar proposta",
            "quotDetailTitle": "Detalhes da cotação", "quotDetailHelpSummary": "Como usar", "quotDetailHelpBody": "Compare as propostas registradas, escolha o vencedor por item (a seleção padrão é a menor proposta) e registre a decisão. Somente a conversão cria pedidos — um por fornecedor vencedor, sem duplicar se repetida.",
            "compareTitle": "Comparação de cotações da requisição", "compareHelpSummary": "Como usar", "compareHelpBody": "Todas as propostas vigentes das cotações da requisição, ordenadas por item. A menor proposta de cada item está marcada; decisões já registradas aparecem sinalizadas.",
            "receiptDetailTitle": "Detalhes do recebimento", "receiptDetailHelpSummary": "Como usar", "receiptDetailHelpBody": "Consulte itens, lotes e quantidades bloqueadas, disponíveis e rejeitadas. Use Decidir somente após realizar a inspeção e reunir a evidência aplicável.",
            "divergenceTitle": "Decidir divergência de conferência", "divergenceHint": "A justificativa é obrigatória e permanece no histórico. Nenhuma destas decisões efetua pagamento.", "decisionFieldLbl": "Decisão *", "approveException": "Aprovar exceção", "requestCorrection": "Solicitar correção", "rejectBilling": "Rejeitar cobrança", "justificationReq": "Justificativa *", "evidenceLbl2": "Evidência / documento", "phProofRef": "Número ou referência do comprovante", "confirmDecisionBtn": "Confirmar decisão",
            "returnDialogTitle": "Devolver itens ao fornecedor", "returnDialogHint": "Somente o saldo aceito e ainda não devolvido pode voltar ao fornecedor. A devolução aguarda aprovação de outro usuário; nada sai do estoque antes dela.", "returnReason": "Motivo da devolução *", "phReturnReason": "Descreva o motivo real da devolução", "registerReturnBtn": "Registrar devolução",
            "returnDecisionTitle": "Decidir devolução ao fornecedor", "returnDecisionHint": "Aprovar baixa o estoque, reabre o saldo do pedido e gera crédito aberto sem compensar títulos automaticamente. Reprovar não altera estoque, pedido nem financeiro.", "approveReturnOpt": "Aprovar devolução", "rejectReturnOpt": "Reprovar devolução", "phReasonReq": "Obrigatória para reprovação",
            "returnDetailTitle": "Detalhes da devolução ao fornecedor", "returnDetailHelpSummary": "Como usar", "returnDetailHelpBody": "Consulte itens devolvidos, status da decisão e o crédito aberto. Crédito aberto não representa pagamento nem compensação automática de contas a pagar.",
            "matchFormTitle": "Conferir cobrança parcial", "matchFormHint": "Campos com * são obrigatórios. A vinculação é manual: nomes semelhantes nunca são associados automaticamente.", "orderSel": "Pedido *", "matchLineLabel": "Linha recebida e aceita *", "selOrderPh": "Selecione o pedido", "selOrderFirstPh": "Selecione primeiro o pedido", "documentNumber": "Número do documento *", "series": "Série", "issueDate": "Emissão *", "currency": "Moeda *", "currencyHelp": "Outra moeda exige política de câmbio e será bloqueada.", "billedQty": "Quantidade cobrada *", "lineDiscount": "Desconto da linha", "generalDiscount": "Desconto geral", "freight": "Frete", "additional": "Valores adicionais", "lineDescription": "Descrição da linha *", "phLineDoc": "Descrição exatamente como consta no documento", "calcTotalInit": "Total calculado: R$ 0,00", "calcTotal": "Total calculado", "registerMatchBtn": "Conferir e registrar", "matchDetailTitle": "Resultado da conferência",
            "toleranceTitle": "Tolerâncias de conferência", "toleranceHint": "Limites dentro dos quais a diferença entre contrato, aceite e cobrança é conferida sem exceção. Alterações ficam versionadas e auditadas.",
            "tolQtyPercent": "Divergência de quantidade (%)", "tolQtyAbs": "Divergência de quantidade absoluta", "tolPricePercent": "Divergência de preço (%)", "tolPriceAbs": "Divergência de preço absoluta", "tolTotalPercent": "Divergência de total (%)", "tolTotalAbs": "Divergência de total absoluto", "tolExcessPercent": "Excesso de recebimento (%)", "tolExcessAbs": "Excesso de recebimento absoluto", "tolDeliveryDays": "Tolerância de entrega (dias)", "tolSod": "Segregação de funções (não permitir conferir e decidir na mesma pessoa)",
            "saveTolerances": "Salvar tolerâncias", "toleranceSaved": "Tolerâncias salvas", "toleranceSavedSub": "As novas regras valem para as próximas conferências; as já registradas não mudam.",
            "reqDetailTitle": "Detalhes da requisição", "originWord": "Origem", "unitWord": "Unidade", "costCenterWord": "Centro de custo", "justificationWord": "Justificativa", "requesterWord": "Solicitante", "colEvent": "Evento", "colReason": "Motivo", "colVersion": "Versão", "colDateTime": "Data/hora", "colQuantity": "Quantidade", "colPending": "Saldo pendente", "noHistory": "Nenhum evento registrado.",
            "reasonDialogTitle": "Justificar decisão", "reasonDialogHint": "A justificativa é enviada ao endpoint e permanece no histórico de auditoria.", "reasonField": "Justificativa *", "phReasonGeneric": "Explique o motivo da sua decisão", "minReasonLen": "Informe um motivo com pelo menos 5 caracteres.",
            "confirmReqApprove": "Aprovar requisição", "confirmReqApproveMsg": "O saldo aprovado passa a alimentar cotações e pedidos; a decisão fica auditada.",
            "reqApproved": "Requisição aprovada", "reqApprovedSub": "O saldo aprovado alimenta cotações e pedidos.", "reqRejected": "Requisição rejeitada", "reqRejectedSub": "A requisição foi arquivada com justificativa; nada mudou em estoque ou financeiro.",
            "orderApproved": "Pedido aprovado", "orderApprovedSub": "O pedido está pronto para entrega e recebimento.", "orderCancelled": "Pedido cancelado", "orderCancelledSub": "O saldo da requisição volta a ficar disponível."
        },
        "en-US": {
            "heroEyebrow": "Supply chain", "heroTitleA": "Purchasing", "heroTitleB": "Procurement",
            "heroSubtitle": "From need to receipt, with authority, evidence and traceability per operational unit.",
            "refresh": "Refresh metrics", "kpiLoading": "Loading real metrics…",
            "navAria": "Purchasing areas",
            "tabSuppliers": "Suppliers", "tabCatalog": "Catalog", "tabRequisitions": "Requisitions", "tabApprovals": "Approvals", "tabQuotations": "Quotations", "tabOrders": "Orders", "tabReceipts": "Receipts", "tabReturns": "Supplier returns", "tabMatches": "Matching & exceptions", "tabReports": "Reports",
            "suppliersTitle": "Suppliers & homologation", "suppliersDesc": "Registration, risk and document validity.", "newSupplier": "New supplier", "phSupplierSearch": "Search by legal or trade name", "allStatuses": "All statuses", "filterBtn": "Filter", "exportCsv": "Export CSV",
            "catalogTitle": "Purchasable catalog", "catalogDesc": "Materials, services and assets with receiving requirements.", "newItem": "New item", "phCatalogSearch": "Search item or code", "activeInactive": "Active and inactive",
            "reqsTitle": "Internal requisitions", "reqsDesc": "Operational needs with priority and cost center.", "newReq": "New requisition",
            "reqHelpSummary": "Flow and responsibilities", "reqHelpBody": "Save as draft to review or submit for approval. After submission, rejection and cancellation require a reason; the requester cannot approve their own demand. Orders consume only the approved balance.",
            "phReqSearch": "Search number", "allOpt": "All",
            "quotTitle": "Quotations", "quotDesc": "Participants, recorded proposals and auditable conversion into orders.", "newQuotation": "New quotation",
            "quotHelpSummary": "Quotation journey", "quotHelpBody": "<b>Flow:</b> open the quotation from an approved requisition with at least two participating suppliers, record the real proposals received from each supplier, compare totals per item, decide the winner of each item (choosing above the lowest requires an auditable justification) and convert into orders — one per winning supplier, no duplicates when repeated. No proposal is simulated or sent automatically.",
            "phQuotSearch": "Search quotation or requisition number",
            "approvalsTitle": "Approvals", "approvalsDesc": "Real decision queue: submitted requisitions and created orders awaiting approval.",
            "approvalsReqsTitle": "Requisitions to decide", "approvalsOrdersTitle": "Orders to decide",
            "ordersTitle": "Purchase orders", "ordersDesc": "Approval, scheduled delivery and balance tracking.", "newOrder": "New order", "phOrderSearch": "Number or supplier",
            "receiptsTitle": "Receiving & inspection", "receiptsDesc": "Physical presence, quality decision and availability are separate events.", "newReceipt": "Register receipt",
            "receiptHelpSummary": "How to use", "receiptHelpBody": "Select an approved order, check the calculated balance, enter quantity, lot, expiry and warehouse, then review and confirm. Materials under inspection or past expiry stay blocked; only an authorized decision releases the accepted quantity. The financial forecast stays open and is not a payment.",
            "phReceiptSearch": "Receipt, order or supplier",
            "returnsTitle": "Supplier returns", "returnsDesc": "Auditable stock decrease with open credit; decision separate from registration.",
            "returnHelpSummary": "How to use", "returnHelpBody": "Register the return in the detail of a completed or divergent receipt, respecting each line's accepted and not yet returned balance. Approval decreases stock, reopens the order balance and generates open credit — no account is paid or offset automatically. The user who registered the return cannot approve it.",
            "phReturnSearch": "Return, receipt or supplier",
            "matchesTitle": "Invoice matching", "matchesDesc": "Compares contract, acceptance and billing without assuming fiscal validation or payment.", "newMatch": "Match invoice",
            "matchHelpSummary": "How to use", "matchHelpBody": "<b>Prerequisites:</b> approved order and accepted receipt. Explicitly select the received line, enter the document values and confirm. <b>Result:</b> differences within tolerance are matched; others await an authorized decision. The document remains “not fiscally validated”; an approved match is not a payment and no account is paid by this action. Consult the Procurement manual.",
            "searchLbl": "Search", "phMatchSearch": "Document, order or supplier", "situationLbl": "Situation", "tolerancesBtn": "Tolerances",
            "reportsTitle": "Procurement reports", "reportsDesc": "CSV files respect filters, permissions and tenant.", "reportSuppliers": "Suppliers", "reportRequisitions": "Requisitions", "reportOrders": "Purchase orders",
            "lineItem": "Catalog item *", "qtyRequired": "Quantity *", "unitRequired": "Unit *", "removeLineAria": "Remove line", "loadingItems": "Loading active items…", "lineItemSimple": "Item *", "unitPriceRequired": "Unit price *", "discountLbl": "Discount",
            "closeAria": "Close", "select": "Select", "daysUnit": "{0} days",
            "hSupplier": "Supplier", "hCategory": "Category", "hPrazo": "Lead time", "hStatus": "Status", "hCode": "Code", "hItem": "Item", "hType": "Type", "hSituation": "Situation", "hNumber": "Number", "hPriority": "Priority", "hNeeded": "Needed by", "hItems": "Items", "hQuotations": "Quotations", "hRequisition": "Requisition", "hUnitCol": "Unit", "hParticipants": "Participants", "hWithProposal": "With proposal", "hGeneratedOrders": "Generated orders", "hValidity": "Valid until", "hAction": "Action", "hDeliveryOn": "Delivery", "hTotal": "Total", "hReturn": "Return", "hReceipt": "Receipt", "hOrder": "Order", "hDate": "Date", "hStock": "Stock", "hFinance": "Financial", "hNextAction": "Next action", "hDocument": "Document", "hIssuedOn": "Issued", "hDifference": "Difference", "hDecision": "Decision", "waiting": "Waiting",
            "openQuotation": "Open quotation", "compareBtn": "Compare", "detailsBtn": "Details", "registerProposal": "Record proposal", "inspect": "Inspect", "viewBtn": "View", "pendingCount": "View {0} pending item(s)", "decideBtn": "Decide", "approveBtn": "Approve", "rejectBtn": "Reject", "cancelBtn": "Cancel", "approveOrderBtn": "Approve order", "cancelOrderBtn": "Cancel order",
            "kpiDraft": "Drafts", "kpiAwaitingApproval": "Awaiting approval", "kpiApprovedBalance": "Approved with balance", "kpiUrgent": "Urgent in flow", "kpiQuotations": "Quotations running", "kpiOrdersApprove": "Orders to approve", "kpiDivergent": "Divergent receipts", "kpiActiveSuppliers": "Active suppliers", "kpiBlockedSuppliers": "Blocked suppliers", "kpiPartial": "Partially received", "kpiMonthPurchased": "Purchased this month",
            "loading": "Loading…", "empty": "No records found for the applied filters.", "operationFailed": "Operation not completed",
            "receiptFor": "Order {0}", "itemsLotsAvail": "Items, lots and availability", "colItemLot": "Item / lot", "colOrdered": "Ordered", "colReceived": "Received", "colBlocked": "Blocked", "colAvailable": "Available", "colRejected": "Rejected", "colAction": "Action", "noLot": "No lot",
            "qualityHistory": "Quality history", "acceptedRejected": "accepted {0}, rejected {1}", "noReserve": "No remark", "noQualityDecisions": "No decisions recorded.",
            "returnCreate": "Return items to supplier", "returnCreateHint": "Only accepted and not yet returned balance; approval by another user with authority.",
            "waitingDecision": "{0} unit(s) awaiting decision.",
            "loadingReceipt": "Loading receipt…", "loadingReturn": "Loading return…", "loadingMatch": "Loading match…", "loadingQuotation": "Loading quotation…", "comparing": "Comparing quotations…",
            "loadingReturnable": "Loading returnable items…", "colAccepted": "Accepted", "colReturned": "Already returned", "colReturnQty": "Quantity to return", "noEligibleItems": "No item with accepted and not yet returned balance in this receipt.",
            "reasonLbl": "Reason:", "decisionLbl": "Decision:", "returnedItems": "Returned items", "colUnitCost": "Unit cost",
            "creditText": "Credit {0}:", "creditOpenNote": "open credit does not offset titles nor makes payments.", "noCreditYet": "No credit generated while the return awaits decision.", "decideReturnBtn": "Decide return",
            "documentLbl": "Document {0}", "contracted": "Contracted:", "billedLbl": "Billed:", "differenceLbl": "Difference:", "zeroBase": "(zero base)", "fiscalValidation": "Fiscal validation:", "matchApprovedNote": "An approved match is not a payment.", "linkedLines": "Linked lines", "colQty": "Quantity", "colPrice": "Price",
            "divergencesLbl": "Divergences", "impactLbl": "Impact:", "actionLbl": "Action:", "resolveException": "Decide exception", "decisionResult": "Decision:", "noDivergences": "No divergence outside tolerance recorded.",
            "quotOption": "{0} · priority {1} · needed {2}", "openFailed": "Could not open the quotation", "noEligibleParticipants": "The quotation has no eligible participants for new proposals.", "registerFailed": "Could not record the proposal", "selParticipantPh": "Select the participating supplier", "quotItemOption": "{0} · balance {1} {2}", "selQuotedItemPh": "Select the quotation item",
            "requisitionOf": "Requisition {0} ({1})", "unitOf": "Unit {0}", "proposalsUntil": "Proposals until {0}",
            "itemsProposals": "Items and proposals", "colBalance": "Balance", "colLowest": "Lowest total", "colWithOffers": "Suppliers with proposal", "noOffer": "no proposal",
            "participantsSection": "Participants", "colQuotedItems": "Quoted items", "colDeliveryDays": "Delivery", "colPayment": "Payment",
            "registeredDecisions": "Recorded decisions", "colChosenSupplier": "Chosen supplier", "colChosenTotal": "Chosen total",
            "generatedOrdersSection": "Generated orders",
            "winnersSelection": "Choose winners per item", "defaultSelectionNote": "The default selection is the lowest proposal. Choosing above the lowest requires an auditable justification.", "colWinningProposal": "Winning proposal", "alreadyDecided": "already decided", "lowestTag": "lowest",
            "justificationField": "Justification (required when the choice is above the lowest proposal)", "registerDecisionBtn": "Record decision",
            "convertNote": "Conversion creates one order per winning supplier, reusing the recorded decision; repeating the conversion returns the same orders without duplicating.", "convertBtn": "Convert to orders",
            "selectProposalMsg": "Select a proposal for every quoted item.", "aboveLowestMsg": "The choice is above the lowest proposal: justify with at least 3 characters.",
            "confirmDecisionTitle": "Confirm quotation decision", "confirmDecisionMsg": "Choices are recorded and enable conversion into orders. No order is created at this step.",
            "decisionRegistered": "Decision recorded", "decisionRegisteredSub": "The quotation is ready for conversion into orders.",
            "convertTitle": "Convert quotation into orders", "convertMsg": "One purchase order will be created per winning supplier, linked to the requisition and the approved decisions. Conversion is a single transaction: failure at any supplier leaves no partial order, and repeating returns the same orders (cancelled ones included — restocking comes from a new order on the requisition).",
            "confirmLowestTitle": "Confirm lowest-price recommendation", "confirmLowestMsg": "{0} No order was created yet. Do you want to confirm the conversion applying the lowest price to the remaining items?",
            "converted": "Quotation converted", "convertedSub": "{0} order(s) linked to the quotation for approval/delivery according to the decisions.", "convertFailed": "Could not convert",
            "noProposalsYet": "This requisition still has no recorded proposals to compare.",
            "lowestNote": "Lowest goods total (unit × quantity − discount):", "fullCostNote": "lowest full cost with freight and taxes:", "validUntilNote": "proposals valid until {0}",
            "colQuotation": "Quotation", "colUnitPrice": "Unitary", "colDiscount": "Discount", "colFreight": "Freight", "colTaxes": "Taxes", "colGoodsTotal": "Goods total", "colFullCost": "Full cost", "lowestProposal": "LOWEST PROPOSAL", "decidedTag": "DECIDED",
            "cancelAction": "Cancel", "cancelTitle": "Cancel quotation", "cancelHint": "Only open quotations can be cancelled. The justification remains in the auditable history.", "cancelReason": "Cancellation justification *", "phCancelReason": "Explain the real reason for cancelling", "cancelConfirmBtn": "Confirm cancellation", "cancelledToast": "Quotation cancelled", "cancelledToastSub": "The quotation was archived with an auditable justification; the requisition may receive a new quotation.",
            "selectByName": "Select by name", "selectByNumber": "Select by number", "naService": "Not applicable to services", "selectAccount": "Select the account", "selectForMaterial": "Select for material/asset", "useContext": "Use current context", "selectByCode": "Select by name or code", "listsUnavailable": "Lists unavailable", "listsUnavailableSub": "Could not load all authorized selectors.",
            "selPendingItemPh": "Select the pending item", "noPendingBalance": "Order without pending balance", "balanceWord": "balance", "matchOption": "{0} · {1} · balance {2} {3}", "selectMatchLine": "Select the matched line", "noAcceptedAvailable": "No accepted balance available",
            "confirmReceiveTitle": "Confirm receipt", "confirmReceiveMsg": "The operation performs the physical intake and creates open financial forecasts. Writes are not repeated automatically.", "confirmReceiveBtn": "Receive",
            "minOneItem": "Provide at least one item with a selected catalog entry.", "saving": "Saving…", "operationDone": "Operation completed", "operationDoneSub": "Stock and financial links were persisted without duplication.",
            "confirmQualityTitle": "Confirm quality decision", "confirmQualityMsg": "Only the accepted quantity will be released. The decision stays in the history.", "qualityRegisteredSub": "Availability was updated only for the accepted quantity.",
            "confirmDivergence": "Confirm decision", "confirmDivergenceMsg": "The divergence will be recorded as {0}. This does not make any payment.", "divergenceResolvedSub": "The match was updated and the history preserved.",
            "confirmReturnTitle": "Confirm supplier return", "confirmReturnMsg": "The return stays pending approval by another user with authority. No balance changes before approval.", "confirmReturnBtn": "Register return",
            "minOneReturnQty": "Enter the quantity for at least one returnable item.", "returnRegistered": "Return registered", "returnRegisteredSub": "Awaiting decision by an approver with no link to the registration.",
            "approveReturnTitle": "Approve return", "approveReturnMsg": "Stock will be decreased, the order balance reopened and an open credit generated. No account is paid or offset automatically.",
            "rejectReturnTitle": "Reject return", "rejectReturnMsg": "The return will be archived without effects on stock, order or finance.",
            "rejectRequiresReason": "Rejection requires a justification.", "returnApprovedToast": "Return approved", "returnApprovedSub": "Stock decreased, order balance reopened and open credit registered.", "returnRejectedToast": "Return rejected", "returnRejectedSub": "Nothing changed in stock, order or finance.",
            "minTwoSuppliers": "Select at least two participating suppliers.", "openingQuotation": "Opening quotation…", "quotationRequested": "Quotation requested", "quotationRequestedSub": "Participants registered; record the proposals received from each supplier.",
            "registeringProposal": "Recording proposal…", "proposalRegistered": "Proposal recorded", "proposalRegisteredSub": "The proposal enters the comparison and counts for the quotation decision.",
            "qualityDialogTitle": "Quality decision", "acceptedQty": "Accepted quantity", "rejectedQty": "Rejected quantity", "resultLbl": "Result", "qApproved": "Approved", "qConditional": "Conditionally approved", "qRejected": "Rejected", "evidenceLbl": "Evidence / document", "phReportRef": "Laboratory report number or reference", "justificationField2": "Justification", "phJustReq": "Required for rejection or condition", "registerReleaseBtn": "Record and release eligible",
            "supplierDialogTitle": "New supplier", "legalName": "Legal name", "tradeName": "Trade name", "taxDoc": "Tax document", "typeLbl": "Type", "mainCategory": "Main category", "emailLbl": "E-mail", "phoneLbl": "Phone", "countryLbl": "Country", "statusLbl": "Status", "stActive": "Active", "stReview": "Under homologation", "stInactive": "Inactive", "stBlocked": "Blocked", "avgDelivery": "Average lead time (days)", "notesLbl": "Notes", "saveSupplierBtn": "Save supplier",
            "catalogDialogTitle": "New item", "nameLbl": "Name", "internalCode": "Internal code", "categoryLbl": "Category", "unitField": "Unit", "stockProduct": "Stock product", "phStockProduct": "Select for material/asset", "minStock": "Minimum stock", "requirements": "Requirements", "reqLot": "Lot", "reqExpiry": "Expiry", "reqDocument": "Document", "reqInspection": "Inspection", "reqApprovedSupplier": "Homologated supplier", "saveItemBtn": "Save item",
            "reqDialogTitle": "New requisition", "originLbl": "Origin / need", "phOrigin": "Maintenance, Crop, Quality", "operationalUnit": "Operational unit", "priorityLbl": "Priority", "neededOn": "Needed by", "justificationField3": "Justification", "itemsRequested": "Requested items", "unitConsistency": "The unit must match the item registration; conversions are explicit.", "addItem": "Add item", "submitNow": "Submit immediately to the approval queue", "saveReqBtn": "Save requisition",
            "orderDialogTitle": "New order", "supplierLbl": "Supplier", "orderItems": "Order items", "paymentTerms": "Payment terms", "deliveryOn": "Scheduled delivery", "deliveryAddress": "Delivery address", "createForApproval": "Create for approval",
            "receiptDialogTitle": "Register receipt", "approvedOrderSel": "Approved order", "receivedAt": "Date/time", "warehouseLbl": "Warehouse for physical intake", "financeAccount": "Forecast financial account", "firstDueOn": "First due date", "installments": "Installments", "invoiceDoc": "Invoice/document", "overrideExcess": "Authorize excess", "excessJustification": "Excess justification", "notesWide": "Notes",
            "receiptFooter": "Confirmation records physical presence. Items under inspection or past expiry go to quarantine and do not enter available balance; the financial forecast stays open, never paid.", "confirmReceiveFull": "Check and receive",
            "pendingItemLbl": "Pending item *", "lotLbl": "Supplier lot", "expiryLbl": "Expiry", "dupPendingItem": "The same pending item cannot appear in two receipt lines.", "minOnePendingLine": "Provide at least one line with a pending item and quantity greater than zero.",
            "newQuotationTitle": "New quotation", "quotRequestHint": "Only approved or partially fulfilled requisitions accept a quotation, and at most one quotation is open per requisition.", "approvedReqLbl": "Approved requisition *", "selApprovedReqPh": "Select the approved requisition", "dueDateLbl": "Deadline to receive proposals *", "participantsLbl": "Participating suppliers *", "loadingSuppliers": "Loading active suppliers…", "atLeastTwo": "At least two suppliers. Hold Ctrl to mark several.", "requestQuotationBtn": "Request quotation",
            "quoteTitle": "Record received proposal", "quoteHint": "Typed exactly as the supplier sent it; nothing is quoted or sent automatically.", "participantLbl": "Participating supplier *", "quotedItemLbl": "Quoted item *", "unitPriceLbl": "Unit price *", "discountUnit": "Discount per unit", "deliveryDays": "Delivery time (days) *", "proposalValidity": "Proposal validity", "taxesLbl": "Taxes", "registerQuoteBtn": "Record proposal",
            "quotDetailTitle": "Quotation details", "quotDetailHelpSummary": "How to use", "quotDetailHelpBody": "Compare recorded proposals, choose the winner per item (the default selection is the lowest proposal) and record the decision. Only conversion creates orders — one per winning supplier, no duplicates when repeated.",
            "compareTitle": "Comparison of requisition quotations", "compareHelpSummary": "How to use", "compareHelpBody": "All current proposals of the requisition's quotations, ordered by item. The lowest proposal of each item is marked; recorded decisions appear flagged.",
            "receiptDetailTitle": "Receipt details", "receiptDetailHelpSummary": "How to use", "receiptDetailHelpBody": "Check items, lots and blocked, available and rejected quantities. Use Decide only after performing the inspection and gathering the applicable evidence.",
            "divergenceTitle": "Decide matching divergence", "divergenceHint": "The justification is required and remains in the history. None of these decisions makes a payment.", "decisionFieldLbl": "Decision *", "approveException": "Approve exception", "requestCorrection": "Request correction", "rejectBilling": "Reject invoice", "justificationReq": "Justification *", "evidenceLbl2": "Evidence / document", "phProofRef": "Voucher number or reference", "confirmDecisionBtn": "Confirm decision",
            "returnDialogTitle": "Return items to supplier", "returnDialogHint": "Only the accepted and not yet returned balance can go back to the supplier. The return awaits approval by another user; nothing leaves stock before it.", "returnReason": "Return reason *", "phReturnReason": "Describe the real reason for the return", "registerReturnBtn": "Register return",
            "returnDecisionTitle": "Decide supplier return", "returnDecisionHint": "Approving decreases stock, reopens the order balance and generates open credit without offsetting titles automatically. Rejecting changes no stock, order or finance.", "approveReturnOpt": "Approve return", "rejectReturnOpt": "Reject return", "phReasonReq": "Required for rejection",
            "returnDetailTitle": "Supplier return details", "returnDetailHelpSummary": "How to use", "returnDetailHelpBody": "Check returned items, decision status and the open credit. Open credit is not a payment nor an automatic offset of accounts payable.",
            "matchFormTitle": "Match partial invoice", "matchFormHint": "Fields with * are required. Linking is manual: similar names are never associated automatically.", "orderSel": "Order *", "matchLineLabel": "Received and accepted line *", "selOrderPh": "Select the order", "selOrderFirstPh": "Select the order first", "documentNumber": "Document number *", "series": "Series", "issueDate": "Issue date *", "currency": "Currency *", "currencyHelp": "Another currency requires an exchange policy and will be blocked.", "billedQty": "Billed quantity *", "lineDiscount": "Line discount", "generalDiscount": "General discount", "freight": "Freight", "additional": "Additional amounts", "lineDescription": "Line description *", "phLineDoc": "Description exactly as stated on the document", "calcTotalInit": "Calculated total: R$ 0.00", "calcTotal": "Calculated total", "registerMatchBtn": "Check and register", "matchDetailTitle": "Matching result",
            "toleranceTitle": "Matching tolerances", "toleranceHint": "Limits within which differences between contract, acceptance and billing are matched without exception. Changes are versioned and audited.",
            "tolQtyPercent": "Quantity variance (%)", "tolQtyAbs": "Absolute quantity variance", "tolPricePercent": "Price variance (%)", "tolPriceAbs": "Absolute price variance", "tolTotalPercent": "Total variance (%)", "tolTotalAbs": "Absolute total variance", "tolExcessPercent": "Receiving excess (%)", "tolExcessAbs": "Absolute receiving excess", "tolDeliveryDays": "Delivery tolerance (days)", "tolSod": "Separation of duties (do not allow the same person to match and decide)",
            "saveTolerances": "Save tolerances", "toleranceSaved": "Tolerances saved", "toleranceSavedSub": "The new rules apply to the next matches; recorded ones do not change.",
            "reqDetailTitle": "Requisition details", "originWord": "Origin", "unitWord": "Unit", "costCenterWord": "Cost center", "justificationWord": "Justification", "requesterWord": "Requester", "colEvent": "Event", "colReason": "Reason", "colVersion": "Version", "colDateTime": "Date/time", "colQuantity": "Quantity", "colPending": "Pending balance", "noHistory": "No events recorded.",
            "reasonDialogTitle": "Justify decision", "reasonDialogHint": "The justification is sent to the endpoint and remains in the audit history.", "reasonField": "Justification *", "phReasonGeneric": "Explain the reason for your decision", "minReasonLen": "Enter a reason with at least 5 characters.",
            "confirmReqApprove": "Approve requisition", "confirmReqApproveMsg": "The approved balance becomes available for quotations and orders; the decision is audited.",
            "reqApproved": "Requisition approved", "reqApprovedSub": "The approved balance feeds quotations and orders.", "reqRejected": "Requisition rejected", "reqRejectedSub": "The requisition was archived with a justification; nothing changed in stock or finance.",
            "orderApproved": "Order approved", "orderApprovedSub": "The order is ready for delivery and receiving.", "orderCancelled": "Order cancelled", "orderCancelledSub": "The requisition balance becomes available again."
        },
        "es-ES": {
            "heroEyebrow": "Cadena de suministro", "heroTitleA": "Compras", "heroTitleB": "Suministros",
            "heroSubtitle": "De la necesidad al recibimiento, con autoridad, evidencia y trazabilidad por unidad operativa.",
            "refresh": "Actualizar indicadores", "kpiLoading": "Cargando indicadores reales…",
            "navAria": "Áreas de compras",
            "tabSuppliers": "Proveedores", "tabCatalog": "Catálogo", "tabRequisitions": "Requisiciones", "tabApprovals": "Aprobaciones", "tabQuotations": "Cotizaciones", "tabOrders": "Pedidos", "tabReceipts": "Recibos", "tabReturns": "Devoluciones al proveedor", "tabMatches": "Conciliación y divergencias", "tabReports": "Informes",
            "suppliersTitle": "Proveedores y homologación", "suppliersDesc": "Registro, riesgo y vigencia documental.", "newSupplier": "Nuevo proveedor", "phSupplierSearch": "Buscar razón social o nombre comercial", "allStatuses": "Todos los estados", "filterBtn": "Filtrar", "exportCsv": "Exportar CSV",
            "catalogTitle": "Catálogo comprable", "catalogDesc": "Materiales, servicios y activos con requisitos de recibimiento.", "newItem": "Nuevo ítem", "phCatalogSearch": "Buscar ítem o código", "activeInactive": "Activos e inactivos",
            "reqsTitle": "Requisiciones internas", "reqsDesc": "Necesidades operativas con prioridad y centro de costo.", "newReq": "Nueva requisición",
            "reqHelpSummary": "Flujo y responsabilidades", "reqHelpBody": "Guarde en borrador para revisar o envíe a aprobación. Después del envío, rechazar y cancelar exige justificación; el solicitante no puede aprobar su propia demanda. Los pedidos consumen solo el saldo aprobado.",
            "phReqSearch": "Buscar número", "allOpt": "Todos",
            "quotTitle": "Cotizaciones", "quotDesc": "Participantes, propuestas registradas y conversión auditable a pedido.", "newQuotation": "Nueva cotización",
            "quotHelpSummary": "Jornada de la cotización", "quotHelpBody": "<b>Flujo:</b> abra la cotización desde una requisición aprobada con al menos dos proveedores participantes, registre las propuestas reales recibidas de cada proveedor, compare los totales por ítem, decida el ganador de cada ítem (elegir por encima del menor exige justificación auditable) y convierta en pedidos — uno por proveedor ganador, sin duplicar si se repite. Ninguna propuesta es simulada ni enviada automáticamente.",
            "phQuotSearch": "Buscar número de cotización o de requisición",
            "approvalsTitle": "Aprobaciones", "approvalsDesc": "Cola real de decisiones: requisiciones enviadas y pedidos creados que esperan aprobación.",
            "approvalsReqsTitle": "Requisiciones para decidir", "approvalsOrdersTitle": "Pedidos para decidir",
            "ordersTitle": "Pedidos de compra", "ordersDesc": "Aprobación, entrega programada y seguimiento de saldo.", "newOrder": "Nuevo pedido", "phOrderSearch": "Número o proveedor",
            "receiptsTitle": "Recibimiento y verificación", "receiptsDesc": "Presencia física, decisión de calidad y disponibilidad son eventos separados.", "newReceipt": "Registrar recibo",
            "receiptHelpSummary": "Cómo usar", "receiptHelpBody": "Seleccione un pedido aprobado, verifique el saldo calculado, informe cantidad, lote, vencimiento y depósito, luego revise y confirme. Materiales en inspección o vencidos permanecen bloqueados; solo una decisión autorizada libera la cantidad aceptada. La previsión financiera queda abierta y no es un pago.",
            "phReceiptSearch": "Recibo, pedido o proveedor",
            "returnsTitle": "Devoluciones al proveedor", "returnsDesc": "Baja auditable en inventario con crédito abierto; decisión separada del registro.",
            "returnHelpSummary": "Cómo usar", "returnHelpBody": "Registre la devolución en el detalle de un recibo concluido o divergente, respetando el saldo aceptado y aún no devuelto de cada línea. La aprobación baja el inventario, reabre el saldo del pedido y genera crédito abierto — ninguna cuenta se paga ni compensa automáticamente. Quien registró la devolución no puede aprobarla.",
            "phReturnSearch": "Devolución, recibo o proveedor",
            "matchesTitle": "Conciliación de cobros", "matchesDesc": "Compara contrato, aceptación y cobro sin presuponer validación fiscal o pago.", "newMatch": "Conciliar cobro",
            "matchHelpSummary": "Cómo usar", "matchHelpBody": "<b>Prerrequisitos:</b> pedido aprobado y recibo aceptado. Seleccione explícitamente la línea recibida, informe los valores del documento y confirme. <b>Resultado:</b> diferencias dentro de las tolerancias quedan conciliadas; las demás esperan decisión autorizada. El documento sigue “sin validar fiscalmente”; conciliación aprobada no significa pago y ninguna cuenta es pagada por esta acción. Consulte el manual de Compras y Suministros.",
            "searchLbl": "Búsqueda", "phMatchSearch": "Documento, pedido o proveedor", "situationLbl": "Situación", "tolerancesBtn": "Tolerancias",
            "reportsTitle": "Informes de compras", "reportsDesc": "Los CSV respetan filtros, permisos y tenant.", "reportSuppliers": "Proveedores", "reportRequisitions": "Requisiciones", "reportOrders": "Pedidos de compra",
            "lineItem": "Ítem del catálogo *", "qtyRequired": "Cantidad *", "unitRequired": "Unidad *", "removeLineAria": "Quitar línea", "loadingItems": "Cargando ítems activos…", "lineItemSimple": "Ítem *", "unitPriceRequired": "Precio unitario *", "discountLbl": "Descuento",
            "closeAria": "Cerrar", "select": "Seleccione", "daysUnit": "{0} días",
            "hSupplier": "Proveedor", "hCategory": "Categoría", "hPrazo": "Plazo", "hStatus": "Estado", "hCode": "Código", "hItem": "Ítem", "hType": "Tipo", "hSituation": "Situación", "hNumber": "Número", "hPriority": "Prioridad", "hNeeded": "Necesidad", "hItems": "Ítems", "hQuotations": "Cotizaciones", "hRequisition": "Requisición", "hUnitCol": "Unidad", "hParticipants": "Participantes", "hWithProposal": "Con propuesta", "hGeneratedOrders": "Pedidos generados", "hValidity": "Validez", "hAction": "Acción", "hDeliveryOn": "Entrega", "hTotal": "Total", "hReturn": "Devolución", "hReceipt": "Recibo", "hOrder": "Pedido", "hDate": "Fecha", "hStock": "Inventario", "hFinance": "Financiero", "hNextAction": "Próxima acción", "hDocument": "Documento", "hIssuedOn": "Emisión", "hDifference": "Diferencia", "hDecision": "Decisión", "waiting": "Esperando",
            "openQuotation": "Abrir cotización", "compareBtn": "Comparar", "detailsBtn": "Detallar", "registerProposal": "Registrar propuesta", "inspect": "Inspeccionar", "viewBtn": "Consultar", "pendingCount": "Ver {0} pendiente(s)", "decideBtn": "Decidir", "approveBtn": "Aprobar", "rejectBtn": "Rechazar", "cancelBtn": "Cancelar", "approveOrderBtn": "Aprobar pedido", "cancelOrderBtn": "Cancelar pedido",
            "kpiDraft": "Borradores", "kpiAwaitingApproval": "Esperando aprobación", "kpiApprovedBalance": "Aprobadas con saldo", "kpiUrgent": "Urgentes en flujo", "kpiQuotations": "Cotizaciones en curso", "kpiOrdersApprove": "Pedidos para aprobar", "kpiDivergent": "Recibos divergentes", "kpiActiveSuppliers": "Proveedores activos", "kpiBlockedSuppliers": "Proveedores bloqueados", "kpiPartial": "Recibidos parcialmente", "kpiMonthPurchased": "Comprado en el mes",
            "loading": "Cargando…", "empty": "No se encontraron registros para los filtros aplicados.", "operationFailed": "Operación no completada",
            "receiptFor": "Pedido {0}", "itemsLotsAvail": "Ítems, lotes y disponibilidad", "colItemLot": "Ítem / lote", "colOrdered": "Pedido", "colReceived": "Recibido", "colBlocked": "Bloqueado", "colAvailable": "Disponible", "colRejected": "Rechazado", "colAction": "Acción", "noLot": "Sin lote",
            "qualityHistory": "Historial de calidad", "acceptedRejected": "acepta {0}, rechaza {1}", "noReserve": "Sin reserva", "noQualityDecisions": "Ninguna decisión registrada.",
            "returnCreate": "Devolver ítems al proveedor", "returnCreateHint": "Solo el saldo aceptado y aún no devuelto; aprobación por otro usuario con autoridad.",
            "waitingDecision": "{0} unidad(es) esperando decisión.",
            "loadingReceipt": "Cargando recibo…", "loadingReturn": "Cargando devolución…", "loadingMatch": "Cargando conciliación…", "loadingQuotation": "Cargando cotización…", "comparing": "Comparando cotizaciones…",
            "loadingReturnable": "Cargando ítems devolvibles…", "colAccepted": "Aceptado", "colReturned": "Ya devuelto", "colReturnQty": "Cantidad a devolver", "noEligibleItems": "Ningún ítem con saldo aceptado y aún no devuelto en este recibo.",
            "reasonLbl": "Motivo:", "decisionLbl": "Decisión:", "returnedItems": "Ítems devueltos", "colUnitCost": "Costo unitario",
            "creditText": "Crédito {0}:", "creditOpenNote": "el crédito abierto no compensa títulos ni efectúa pagos.", "noCreditYet": "Ningún crédito generado mientras la devolución espera decisión.", "decideReturnBtn": "Decidir devolución",
            "documentLbl": "Documento {0}", "contracted": "Contratado:", "billedLbl": "Cobrado:", "differenceLbl": "Diferencia:", "zeroBase": "(base cero)", "fiscalValidation": "Validación fiscal:", "matchApprovedNote": "La conciliación aprobada no significa pago.", "linkedLines": "Líneas vinculadas", "colQty": "Cantidad", "colPrice": "Precio",
            "divergencesLbl": "Divergencias", "impactLbl": "Impacto:", "actionLbl": "Acción:", "resolveException": "Decisar excepción", "decisionResult": "Decisión:", "noDivergences": "Ninguna divergencia fuera de la tolerancia registrada.",
            "quotOption": "{0} · prioridad {1} · necesidad {2}", "openFailed": "No se pudo abrir la cotización", "noEligibleParticipants": "La cotización no tiene participantes aptos para nuevas propuestas.", "registerFailed": "No se pudo registrar la propuesta", "selParticipantPh": "Seleccione el proveedor participante", "quotItemOption": "{0} · saldo {1} {2}", "selQuotedItemPh": "Seleccione el ítem de la cotización",
            "requisitionOf": "Requisición {0} ({1})", "unitOf": "Unidad {0}", "proposalsUntil": "Propuestas hasta {0}",
            "itemsProposals": "Ítems y propuestas", "colBalance": "Saldo", "colLowest": "Menor total", "colWithOffers": "Proveedores con propuesta", "noOffer": "sin propuesta",
            "participantsSection": "Participantes", "colQuotedItems": "Ítems cotizados", "colDeliveryDays": "Entrega", "colPayment": "Pago",
            "registeredDecisions": "Decisiones registradas", "colChosenSupplier": "Proveedor elegido", "colChosenTotal": "Total elegido",
            "generatedOrdersSection": "Pedidos generados",
            "winnersSelection": "Elección de ganadores por ítem", "defaultSelectionNote": "La selección predeterminada es la menor propuesta. Elegir por encima del menor exige justificación auditable.", "colWinningProposal": "Propuesta ganadora", "alreadyDecided": "ya decidido", "lowestTag": "menor",
            "justificationField": "Justificación (obligatoria cuando la elección quede por encima de la menor propuesta)", "registerDecisionBtn": "Registrar decisión",
            "convertNote": "La conversión genera un pedido por proveedor ganador, reutilizando la decisión registrada; repetir la conversión devuelve los mismos pedidos sin duplicar.", "convertBtn": "Convertir en pedidos",
            "selectProposalMsg": "Seleccione una propuesta para cada ítem cotizado.", "aboveLowestMsg": "La elección quedó por encima de la menor propuesta: justifique con al menos 3 caracteres.",
            "confirmDecisionTitle": "Confirmar decisión de la cotización", "confirmDecisionMsg": "Las elecciones quedan registradas e habilitan la conversión en pedidos. No se crea ningún pedido en esta etapa.",
            "decisionRegistered": "Decisión registrada", "decisionRegisteredSub": "La cotización está lista para convertir en pedidos.",
            "convertTitle": "Convertir cotización en pedidos", "convertMsg": "Se creará un pedido de compra por proveedor ganador, vinculado a la requisición y a las decisiones aprobadas. La conversión es una transacción única: la falla en cualquier proveedor no deja pedido parcial, y repetir devuelve los mismos pedidos (los cancelados incluidos — el reabastecimiento viene de un nuevo pedido en la requisición).",
            "confirmLowestTitle": "Confirmar recomendación de menor precio", "confirmLowestMsg": "{0} Aún no se creó ningún pedido. ¿Desea confirmar la conversión aplicando el menor precio a los ítems restantes?",
            "converted": "Cotización convertida", "convertedSub": "{0} pedido(s) vinculado(s) a la cotización para aprobación/entrega según las decisiones.", "convertFailed": "No se pudo convertir",
            "noProposalsYet": "Esta requisición aún no tiene propuestas registradas para comparar.",
            "lowestNote": "Menor total de la mercancía (unitario × cantidad − descuento):", "fullCostNote": "menor costo completo con flete e impuestos:", "validUntilNote": "propuestas válidas hasta {0}",
            "colQuotation": "Cotización", "colUnitPrice": "Unitario", "colDiscount": "Descuento", "colFreight": "Flete", "colTaxes": "Impuestos", "colGoodsTotal": "Total mercancía", "colFullCost": "Costo completo", "lowestProposal": "MENOR PROPUESTA", "decidedTag": "DECIDIDO",
            "cancelAction": "Cancelar", "cancelTitle": "Cancelar cotización", "cancelHint": "Solo cotizaciones abiertas pueden ser canceladas. La justificación permanece en el historial auditable.", "cancelReason": "Justificación del cancelamiento *", "phCancelReason": "Explique el motivo real del cancelamiento", "cancelConfirmBtn": "Confirmar cancelamiento", "cancelledToast": "Cotización cancelada", "cancelledToastSub": "La cotización fue archivada con justificación auditable; la requisición puede recibir una nueva cotización.",
            "selectByName": "Seleccione por nombre", "selectByNumber": "Seleccione por número", "naService": "No aplica a servicio", "selectAccount": "Seleccione la cuenta", "selectForMaterial": "Seleccione para material/activo", "useContext": "Usar contexto actual", "selectByCode": "Seleccione por nombre o código", "listsUnavailable": "Listas no disponibles", "listsUnavailableSub": "No fue posible cargar todos los selectores autorizados.",
            "selPendingItemPh": "Seleccione el ítem pendiente", "noPendingBalance": "Pedido sin saldo pendiente", "balanceWord": "saldo", "matchOption": "{0} · {1} · saldo {2} {3}", "selectMatchLine": "Seleccione la línea conciliada", "noAcceptedAvailable": "Sin saldo aceptado disponible",
            "confirmReceiveTitle": "Confirmar recibimiento", "confirmReceiveMsg": "La operación hará la entrada física y creará previsiones financieras abiertas. Las escrituras no se repiten automáticamente.", "confirmReceiveBtn": "Recibir",
            "minOneItem": "Informe al menos un ítem con catálogo seleccionado.", "saving": "Guardando…", "operationDone": "Operación completada", "operationDoneSub": "Los vínculos de inventario y financiero fueron persistidos sin duplicación.",
            "confirmQualityTitle": "Confirmar decisión de calidad", "confirmQualityMsg": "Solo la cantidad aceptada será liberada. La decisión permanecerá en el historial.", "qualityRegisteredSub": "La disponibilidad fue actualizada solo para la cantidad aceptada.",
            "confirmDivergence": "Confirmar decisión", "confirmDivergenceMsg": "La divergencia será registrada como {0}. Esto no efectúa pago.", "divergenceResolvedSub": "La conciliación fue actualizada y el historial preservado.",
            "confirmReturnTitle": "Confirmar devolución al proveedor", "confirmReturnMsg": "La devolución queda pendiente de aprobación por otro usuario con autoridad. Ningún saldo cambia antes de la aprobación.", "confirmReturnBtn": "Registrar devolución",
            "minOneReturnQty": "Informe la cantidad de al menos un ítem retornable.", "returnRegistered": "Devolución registrada", "returnRegisteredSub": "Esperando decisión de un aprobador sin vínculo con el registro.",
            "approveReturnTitle": "Aprobar devolución", "approveReturnMsg": "El inventario será bajado, el saldo del pedido reabierto y un crédito abierto generado. Ninguna cuenta se paga ni compensa automáticamente.",
            "rejectReturnTitle": "Rechazar devolución", "rejectReturnMsg": "La devolución será archivada sin efectos en inventario, pedido ni financiero.",
            "rejectRequiresReason": "El rechazo exige justificación.", "returnApprovedToast": "Devolución aprobada", "returnApprovedSub": "Inventario bajado, saldo del pedido reabierto y crédito abierto registrado.", "returnRejectedToast": "Devolución rechazada", "returnRejectedSub": "Nada cambió en inventario, pedido ni financiero.",
            "minTwoSuppliers": "Seleccione al menos dos proveedores participantes.", "openingQuotation": "Abriendo cotización…", "quotationRequested": "Cotización solicitada", "quotationRequestedSub": "Participantes registrados; registre las propuestas recibidas de cada proveedor.",
            "registeringProposal": "Registrando propuesta…", "proposalRegistered": "Propuesta registrada", "proposalRegisteredSub": "La propuesta entra en la comparación y cuenta para la decisión de la cotización.",
            "qualityDialogTitle": "Decisión de calidad", "acceptedQty": "Cantidad aceptada", "rejectedQty": "Cantidad rechazada", "resultLbl": "Resultado", "qApproved": "Aprobado", "qConditional": "Aprobado con condición", "qRejected": "Rechazado", "evidenceLbl": "Evidencia / documento", "phReportRef": "Número o referencia del informe", "justificationField2": "Justificación", "phJustReq": "Obligatoria para rechazo o condición", "registerReleaseBtn": "Registrar y liberar elegible",
            "supplierDialogTitle": "Nuevo proveedor", "legalName": "Razón social", "tradeName": "Nombre comercial", "taxDoc": "Documento fiscal", "typeLbl": "Tipo", "mainCategory": "Categoría principal", "emailLbl": "Correo", "phoneLbl": "Teléfono", "countryLbl": "País", "statusLbl": "Estado", "stActive": "Activo", "stReview": "En homologación", "stInactive": "Inactivo", "stBlocked": "Bloqueado", "avgDelivery": "Plazo promedio (días)", "notesLbl": "Observaciones", "saveSupplierBtn": "Guardar proveedor",
            "catalogDialogTitle": "Nuevo ítem", "nameLbl": "Nombre", "internalCode": "Código interno", "categoryLbl": "Categoría", "unitField": "Unidad", "stockProduct": "Producto de inventario", "phStockProduct": "Seleccione para material/activo", "minStock": "Inventario mínimo", "requirements": "Exigencias", "reqLot": "Lote", "reqExpiry": "Vencimiento", "reqDocument": "Documento", "reqInspection": "Inspección", "reqApprovedSupplier": "Proveedor homologado", "saveItemBtn": "Guardar ítem",
            "reqDialogTitle": "Nueva requisición", "originLbl": "Origen / necesidad", "phOrigin": "Mantenimiento, Cultivo, Calidad", "operationalUnit": "Unidad operativa", "priorityLbl": "Prioridad", "neededOn": "Fecha necesaria", "justificationField3": "Justificación", "itemsRequested": "Ítems solicitados", "unitConsistency": "La unidad debe coincidir con el registro del ítem; las conversiones son explícitas.", "addItem": "Agregar ítem", "submitNow": "Enviar inmediatamente a la cola de aprobación", "saveReqBtn": "Guardar requisición",
            "orderDialogTitle": "Nuevo pedido", "supplierLbl": "Proveedor", "orderItems": "Ítems del pedido", "paymentTerms": "Condición de pago", "deliveryOn": "Entrega prevista", "deliveryAddress": "Dirección de entrega", "createForApproval": "Crear para aprobación",
            "receiptDialogTitle": "Registrar recibo", "approvedOrderSel": "Pedido aprobado", "receivedAt": "Fecha/hora", "warehouseLbl": "Depósito para entrada física", "financeAccount": "Cuenta de la previsión financiera", "firstDueOn": "Primer vencimiento", "installments": "Cuotas", "invoiceDoc": "Nota/documento", "overrideExcess": "Autorizar exceso", "excessJustification": "Justificación del exceso", "notesWide": "Observaciones",
            "receiptFooter": "La confirmación registra la presencia física. Ítems en inspección o vencidos quedan en cuarentena y no entran al saldo disponible; la previsión financiera permanece abierta, nunca pagada.", "confirmReceiveFull": "Verificar y recibir",
            "pendingItemLbl": "Ítem pendiente *", "lotLbl": "Lote del proveedor", "expiryLbl": "Vencimiento", "dupPendingItem": "El mismo ítem pendiente no puede aparecer en dos líneas del recibo.", "minOnePendingLine": "Informe al menos una línea con ítem pendiente y cantidad mayor que cero.",
            "newQuotationTitle": "Nueva cotización", "quotRequestHint": "Solo requisiciones aprobadas o con saldo aprobado aceptan cotización, y existe a lo sumo una cotización abierta por requisición.", "approvedReqLbl": "Requisición aprobada *", "selApprovedReqPh": "Seleccione la requisición aprobada", "dueDateLbl": "Plazo para recibir propuestas *", "participantsLbl": "Proveedores participantes *", "loadingSuppliers": "Cargando proveedores activos…", "atLeastTwo": "Al menos dos proveedores. Mantenga Ctrl para marcar varios.", "requestQuotationBtn": "Solicitar cotización",
            "quoteTitle": "Registrar propuesta recibida", "quoteHint": "Digitada exactamente como el proveedor la envió; nada se cotiza ni se envía automáticamente.", "participantLbl": "Proveedor participante *", "quotedItemLbl": "Ítem cotizado *", "unitPriceLbl": "Precio unitario *", "discountUnit": "Descuento por unidad", "deliveryDays": "Plazo de entrega (días) *", "proposalValidity": "Validez de la propuesta", "taxesLbl": "Impuestos", "registerQuoteBtn": "Registrar propuesta",
            "quotDetailTitle": "Detalles de la cotización", "quotDetailHelpSummary": "Cómo usar", "quotDetailHelpBody": "Compare las propuestas registradas, elija el ganador por ítem (la selección predeterminada es la menor propuesta) y registre la decisión. Solo la conversión crea pedidos — uno por proveedor ganador, sin duplicar si se repite.",
            "compareTitle": "Comparación de cotizaciones de la requisición", "compareHelpSummary": "Cómo usar", "compareHelpBody": "Todas las propuestas vigentes de las cotizaciones de la requisición, ordenadas por ítem. La menor propuesta de cada ítem está marcada; las decisiones registradas aparecen señalizadas.",
            "receiptDetailTitle": "Detalles del recibo", "receiptDetailHelpSummary": "Cómo usar", "receiptDetailHelpBody": "Consulte ítems, lotes y cantidades bloqueadas, disponibles y rechazadas. Use Decidir solo después de realizar la inspección y reunir la evidencia aplicable.",
            "divergenceTitle": "Decisar divergencia de conciliación", "divergenceHint": "La justificación es obligatoria y permanece en el historial. Ninguna de estas decisiones efectúa pago.", "decisionFieldLbl": "Decisión *", "approveException": "Aprobar excepción", "requestCorrection": "Solicitar corrección", "rejectBilling": "Rechazar cobro", "justificationReq": "Justificación *", "evidenceLbl2": "Evidencia / documento", "phProofRef": "Número o referencia del comprobante", "confirmDecisionBtn": "Confirmar decisión",
            "returnDialogTitle": "Devolver ítems al proveedor", "returnDialogHint": "Solo el saldo aceptado y aún no devuelto puede volver al proveedor. La devolución espera aprobación de otro usuario; nada sale del inventario antes de ella.", "returnReason": "Motivo de la devolución *", "phReturnReason": "Describa el motivo real de la devolución", "registerReturnBtn": "Registrar devolución",
            "returnDecisionTitle": "Decisar devolución al proveedor", "returnDecisionHint": "Aprobar baja el inventario, reabre el saldo del pedido y genera crédito abierto sin compensar títulos automáticamente. Rechazar no altera inventario, pedido ni financiero.", "approveReturnOpt": "Aprobar devolución", "rejectReturnOpt": "Rechazar devolución", "phReasonReq": "Obligatoria para rechazo",
            "returnDetailTitle": "Detalles de la devolución al proveedor", "returnDetailHelpSummary": "Cómo usar", "returnDetailHelpBody": "Consulte ítems devueltos, estado de la decisión y el crédito abierto. Crédito abierto no representa pago ni compensación automática de cuentas por pagar.",
            "matchFormTitle": "Conciliar cobro parcial", "matchFormHint": "Los campos con * son obligatorios. La vinculación es manual: nombres similares nunca se asocian automáticamente.", "orderSel": "Pedido *", "matchLineLabel": "Línea recibida y aceptada *", "selOrderPh": "Seleccione el pedido", "selOrderFirstPh": "Seleccione primero el pedido", "documentNumber": "Número del documento *", "series": "Serie", "issueDate": "Emisión *", "currency": "Moneda *", "currencyHelp": "Otra moneda exige política de cambio y será bloqueada.", "billedQty": "Cantidad cobrada *", "lineDiscount": "Descuento de la línea", "generalDiscount": "Descuento general", "freight": "Flete", "additional": "Valores adicionales", "lineDescription": "Descripción de la línea *", "phLineDoc": "Descripción exactamente como consta en el documento", "calcTotalInit": "Total calculado: R$ 0,00", "calcTotal": "Total calculado", "registerMatchBtn": "Verificar y registrar", "matchDetailTitle": "Resultado de la conciliación",
            "toleranceTitle": "Tolerancias de conciliación", "toleranceHint": "Límites dentro de los cuales la diferencia entre contrato, aceptación y cobro se concilia sin excepción. Las alteraciones quedan versionadas y auditadas.",
            "tolQtyPercent": "Divergencia de cantidad (%)", "tolQtyAbs": "Divergencia de cantidad absoluta", "tolPricePercent": "Divergencia de precio (%)", "tolPriceAbs": "Divergencia de precio absoluta", "tolTotalPercent": "Divergencia de total (%)", "tolTotalAbs": "Divergencia de total absoluto", "tolExcessPercent": "Exceso de recibimiento (%)", "tolExcessAbs": "Exceso de recibimiento absoluto", "tolDeliveryDays": "Tolerancia de entrega (días)", "tolSod": "Segregación de funciones (no permitir conciliar y decidir la misma persona)",
            "saveTolerances": "Guardar tolerancias", "toleranceSaved": "Tolerancias guardadas", "toleranceSavedSub": "Las nuevas reglas valen para las próximas conciliaciones; las ya registradas no cambian.",
            "reqDetailTitle": "Detalles de la requisición", "originWord": "Origen", "unitWord": "Unidad", "costCenterWord": "Centro de costo", "justificationWord": "Justificación", "requesterWord": "Solicitante", "colEvent": "Evento", "colReason": "Motivo", "colVersion": "Versión", "colDateTime": "Fecha/hora", "colQuantity": "Cantidad", "colPending": "Saldo pendiente", "noHistory": "Ningún evento registrado.",
            "reasonDialogTitle": "Justificar decisión", "reasonDialogHint": "La justificación se envía al endpoint y permanece en el historial de auditoría.", "reasonField": "Justificación *", "phReasonGeneric": "Explique el motivo de su decisión", "minReasonLen": "Informe un motivo con al menos 5 caracteres.",
            "confirmReqApprove": "Aprobar requisición", "confirmReqApproveMsg": "El saldo aprobado pasa a alimentar cotizaciones y pedidos; la decisión queda auditada.",
            "reqApproved": "Requisición aprobada", "reqApprovedSub": "El saldo aprobado alimenta cotizaciones y pedidos.", "reqRejected": "Requisición rechazada", "reqRejectedSub": "La requisición fue archivada con justificación; nada cambió en inventario o financiero.",
            "orderApproved": "Pedido aprobado", "orderApprovedSub": "El pedido está listo para entrega y recibimiento.", "orderCancelled": "Pedido cancelado", "orderCancelledSub": "El saldo de la requisición vuelve a quedar disponible."
        },
        "fr-FR": {
            "heroEyebrow": "Chaîne d'approvisionnement", "heroTitleA": "Achats", "heroTitleB": "Approvisionnements",
            "heroSubtitle": "Du besoin à la réception, avec autorité, preuve et traçabilité par unité opérationnelle.",
            "refresh": "Actualiser les indicateurs", "kpiLoading": "Chargement des indicateurs réels…",
            "navAria": "Zones d'achat",
            "tabSuppliers": "Fournisseurs", "tabCatalog": "Catalogue", "tabRequisitions": "Requêtes internes", "tabApprovals": "Approbations", "tabQuotations": "Devis", "tabOrders": "Commandes", "tabReceipts": "Réceptions", "tabReturns": "Retours fournisseur", "tabMatches": "Vérification et divergences", "tabReports": "Rapports",
            "suppliersTitle": "Fournisseurs et homologation", "suppliersDesc": "Enregistrement, risque et validité documentaire.", "newSupplier": "Nouveau fournisseur", "phSupplierSearch": "Rechercher par raison sociale ou nom commercial", "allStatuses": "Tous les statuts", "filterBtn": "Filtrer", "exportCsv": "Exporter CSV",
            "catalogTitle": "Catalogue achetable", "catalogDesc": "Matériaux, services et actifs avec exigences de réception.", "newItem": "Nouvel article", "phCatalogSearch": "Rechercher article ou code", "activeInactive": "Actifs et inactifs",
            "reqsTitle": "Requêtes internes", "reqsDesc": "Besoins opérationnels avec priorité et centre de coût.", "newReq": "Nouvelle requête",
            "reqHelpSummary": "Flux et responsabilités", "reqHelpBody": "Enregistrez en brouillon pour réviser ou envoyez à l'approbation. Après envoi, refuser et annuler exigent une justification ; le demandeur ne peut pas approuver sa propre demande. Les commandes ne consomment que le solde approuvé.",
            "phReqSearch": "Rechercher le numéro", "allOpt": "Tous",
            "quotTitle": "Devis", "quotDesc": "Participants, propositions enregistrées et conversion auditable vers commande.", "newQuotation": "Nouveau devis",
            "quotHelpSummary": "Parcours du devis", "quotHelpBody": "<b>Flux :</b> ouvrez le devis depuis une requête approuvée avec au moins deux fournisseurs participants, enregistrez les propositions réelles reçues de chaque fournisseur, comparez les totaux par article, décidez du gagnant de chaque article (choisir au-dessus du plus bas exige une justification auditable) et convertissez en commandes — une par fournisseur gagnant, sans duplication si répété. Aucune proposition n'est simulée ni envoyée automatiquement.",
            "phQuotSearch": "Rechercher le numéro du devis ou de la requête",
            "approvalsTitle": "Approbations", "approvalsDesc": "File réelle des décisions : requêtes envoyées et commandes créées en attente d'approbation.",
            "approvalsReqsTitle": "Requêtes à décider", "approvalsOrdersTitle": "Commandes à décider",
            "ordersTitle": "Commandes d'achat", "ordersDesc": "Approbation, livraison prévue et suivi du solde.", "newOrder": "Nouvelle commande", "phOrderSearch": "Numéro ou fournisseur",
            "receiptsTitle": "Réception et vérification", "receiptsDesc": "Présence physique, décision de qualité et disponibilité sont des événements séparés.", "newReceipt": "Enregistrer la réception",
            "receiptHelpSummary": "Comment utiliser", "receiptHelpBody": "Sélectionnez une commande approuvée, vérifiez le solde calculé, saisissez quantité, lot, péremption et dépôt, puis relisez et confirmez. Les matériaux sous inspection ou périmés restent bloqués ; seule une décision autorisée libère la quantité acceptée. La prévision financière reste ouverte et ne représente pas un paiement.",
            "phReceiptSearch": "Réception, commande ou fournisseur",
            "returnsTitle": "Retours fournisseur", "returnsDesc": "Sortie auditable du stock avec crédit ouvert ; décision séparée de l'enregistrement.",
            "returnHelpSummary": "Comment utiliser", "returnHelpBody": "Enregistrez le retour dans le détail d'une réception terminée ou divergente, en respectant le solde accepté et non encore retourné de chaque ligne. L'approbation sort le stock, rouvre le solde de la commande et génère un crédit ouvert — aucun compte n'est payé ni compensé automatiquement. L'utilisateur qui a enregistré le retour ne peut pas l'approuver.",
            "phReturnSearch": "Retour, réception ou fournisseur",
            "matchesTitle": "Vérification des factures", "matchesDesc": "Compare contrat, acceptation et facture sans présumer de validation fiscale ou paiement.", "newMatch": "Vérifier la facture",
            "matchHelpSummary": "Comment utiliser", "matchHelpBody": "<b>Prérequis :</b> commande approuvée et réception acceptée. Sélectionnez explicitement la ligne reçue, saisissez les valeurs du document et confirmez. <b>Résultat :</b> les écarts dans les tolérances sont vérifiés ; les autres attendent une décision autorisée. Le document reste « non validé fiscalement » ; une vérification approuvée n'est pas un paiement et aucun compte n'est payé par cette action. Consultez le manuel Achats et Approvisionnements.",
            "searchLbl": "Recherche", "phMatchSearch": "Document, commande ou fournisseur", "situationLbl": "Situation", "tolerancesBtn": "Tolérances",
            "reportsTitle": "Rapports achats", "reportsDesc": "Les CSV respectent filtres, permissions et tenant.", "reportSuppliers": "Fournisseurs", "reportRequisitions": "Requêtes internes", "reportOrders": "Commandes d'achat",
            "lineItem": "Article du catalogue *", "qtyRequired": "Quantité *", "unitRequired": "Unité *", "removeLineAria": "Supprimer la ligne", "loadingItems": "Chargement des articles actifs…", "lineItemSimple": "Article *", "unitPriceRequired": "Prix unitaire *", "discountLbl": "Remise",
            "closeAria": "Fermer", "select": "Sélectionner", "daysUnit": "{0} jours",
            "hSupplier": "Fournisseur", "hCategory": "Catégorie", "hPrazo": "Délai", "hStatus": "Statut", "hCode": "Code", "hItem": "Article", "hType": "Type", "hSituation": "Situation", "hNumber": "Numéro", "hPriority": "Priorité", "hNeeded": "Besoin", "hItems": "Articles", "hQuotations": "Devis", "hRequisition": "Requête", "hUnitCol": "Unité", "hParticipants": "Participants", "hWithProposal": "Avec proposition", "hGeneratedOrders": "Commandes générées", "hValidity": "Validité", "hAction": "Action", "hDeliveryOn": "Livraison", "hTotal": "Total", "hReturn": "Retour", "hReceipt": "Réception", "hOrder": "Commande", "hDate": "Date", "hStock": "Stock", "hFinance": "Financier", "hNextAction": "Prochaine action", "hDocument": "Document", "hIssuedOn": "Émission", "hDifference": "Écart", "hDecision": "Décision", "waiting": "En attente",
            "openQuotation": "Ouvrir le devis", "compareBtn": "Comparer", "detailsBtn": "Détails", "registerProposal": "Enregistrer la proposition", "inspect": "Inspecter", "viewBtn": "Consulter", "pendingCount": "Voir {0} pendance(s)", "decideBtn": "Décider", "approveBtn": "Approuver", "rejectBtn": "Rejeter", "cancelBtn": "Annuler", "approveOrderBtn": "Approuver la commande", "cancelOrderBtn": "Annuler la commande",
            "kpiDraft": "Brouillons", "kpiAwaitingApproval": "En attente d'approbation", "kpiApprovedBalance": "Approuvées avec solde", "kpiUrgent": "Urgentes en flux", "kpiQuotations": "Devis en cours", "kpiOrdersApprove": "Commandes à approuver", "kpiDivergent": "Réceptions divergentes", "kpiActiveSuppliers": "Fournisseurs actifs", "kpiBlockedSuppliers": "Fournisseurs bloqués", "kpiPartial": "Partiellement reçues", "kpiMonthPurchased": "Achété ce mois",
            "loading": "Chargement…", "empty": "Aucun enregistrement trouvé pour les filtres appliqués.", "operationFailed": "Opération non terminée",
            "receiptFor": "Commande {0}", "itemsLotsAvail": "Articles, lots et disponibilité", "colItemLot": "Article / lot", "colOrdered": "Commandé", "colReceived": "Reçu", "colBlocked": "Bloqué", "colAvailable": "Disponible", "colRejected": "Rejeté", "colAction": "Action", "noLot": "Sans lot",
            "qualityHistory": "Historique qualité", "acceptedRejected": "accepté {0}, rejeté {1}", "noReserve": "Sans réserve", "noQualityDecisions": "Aucune décision enregistrée.",
            "returnCreate": "Renvoyer les articles au fournisseur", "returnCreateHint": "Uniquement le solde accepté et non encore retourné ; approbation par un autre utilisateur habilité.",
            "waitingDecision": "{0} unité(s) en attente de décision.",
            "loadingReceipt": "Chargement de la réception…", "loadingReturn": "Chargement du retour…", "loadingMatch": "Chargement de la vérification…", "loadingQuotation": "Chargement du devis…", "comparing": "Comparaison des devis…",
            "loadingReturnable": "Chargement des articles retournables…", "colAccepted": "Accepté", "colReturned": "Déjà retourné", "colReturnQty": "Quantité à retourner", "noEligibleItems": "Aucun article avec solde accepté et non encore retourné dans cette réception.",
            "reasonLbl": "Motif :", "decisionLbl": "Décision :", "returnedItems": "Articles retournés", "colUnitCost": "Coût unitaire",
            "creditText": "Crédit {0} :", "creditOpenNote": "le crédit ouvert ne compense pas les titres ni n'effectue de paiement.", "noCreditYet": "Aucun crédit généré pendant que le retour attend la décision.", "decideReturnBtn": "Décider du retour",
            "documentLbl": "Document {0}", "contracted": "Contraté :", "billedLbl": "Facturé :", "differenceLbl": "Écart :", "zeroBase": "(base zéro)", "fiscalValidation": "Validation fiscale :", "matchApprovedNote": "Une vérification approuvée n'est pas un paiement.", "linkedLines": "Lignes liées", "colQty": "Quantité", "colPrice": "Prix",
            "divergencesLbl": "Divergences", "impactLbl": "Impact :", "actionLbl": "Action :", "resolveException": "Décider l'exception", "decisionResult": "Décision :", "noDivergences": "Aucune divergence hors tolérance enregistrée.",
            "quotOption": "{0} · priorité {1} · besoin {2}", "openFailed": "Impossible d'ouvrir le devis", "noEligibleParticipants": "Le devis n'a pas de participants aptes à de nouvelles propositions.", "registerFailed": "Impossible d'enregistrer la proposition", "selParticipantPh": "Sélectionnez le fournisseur participant", "quotItemOption": "{0} · solde {1} {2}", "selQuotedItemPh": "Sélectionnez l'article du devis",
            "requisitionOf": "Requête {0} ({1})", "unitOf": "Unité {0}", "proposalsUntil": "Propositions jusqu'au {0}",
            "itemsProposals": "Articles et propositions", "colBalance": "Solde", "colLowest": "Plus bas total", "colWithOffers": "Fournisseurs avec proposition", "noOffer": "sans proposition",
            "participantsSection": "Participants", "colQuotedItems": "Articles cotés", "colDeliveryDays": "Livraison", "colPayment": "Paiement",
            "registeredDecisions": "Décisions enregistrées", "colChosenSupplier": "Fournisseur choisi", "colChosenTotal": "Total choisi",
            "generatedOrdersSection": "Commandes générées",
            "winnersSelection": "Choix des gagnants par article", "defaultSelectionNote": "La sélection par défaut est la proposition la plus basse. Choisir au-dessus du plus bas exige une justification auditable.", "colWinningProposal": "Proposition gagnante", "alreadyDecided": "déjà décidé", "lowestTag": "plus bas",
            "justificationField": "Justification (obligatoire lorsque le choix dépasse la proposition la plus basse)", "registerDecisionBtn": "Enregistrer la décision",
            "convertNote": "La conversion génère une commande par fournisseur gagnant, en réutilisant la décision enregistrée ; répéter la conversion renvoie les mêmes commandes sans dupliquer.", "convertBtn": "Convertir en commandes",
            "selectProposalMsg": "Sélectionnez une proposition pour chaque article coté.", "aboveLowestMsg": "Le choix dépasse la proposition la plus basse : justifiez avec au moins 3 caractères.",
            "confirmDecisionTitle": "Confirmer la décision du devis", "confirmDecisionMsg": "Les choix sont enregistrés et permettent la conversion en commandes. Aucune commande n'est créée à cette étape.",
            "decisionRegistered": "Décision enregistrée", "decisionRegisteredSub": "Le devis est prêt pour la conversion en commandes.",
            "convertTitle": "Convertir le devis en commandes", "convertMsg": "Une commande d'achat sera créée par fournisseur gagnant, liée à la requête et aux décisions approuvées. La conversion est une transaction unique : l'échec chez un fournisseur ne laisse pas de commande partielle, et répéter renvoie les mêmes commandes (les annulées incluses — le réapprovisionnement vient d'une nouvelle commande sur la requête).",
            "confirmLowestTitle": "Confirmer la recommandation au prix le plus bas", "confirmLowestMsg": "{0} Aucune commande n'a été créée. Voulez-vous confirmer la conversion en appliquant le prix le plus bas aux articles restants ?",
            "converted": "Devis converti", "convertedSub": "{0} commande(s) liée(s) au devis pour approbation/livraison selon les décisions.", "convertFailed": "Impossible de convertir",
            "noProposalsYet": "Cette requête n'a pas encore de propositions enregistrées à comparer.",
            "lowestNote": "Plus bas total de la marchandise (unitaire × quantité − remise) :", "fullCostNote": "plus bas coût complet avec fret et taxes :", "validUntilNote": "propositions valables jusqu'au {0}",
            "colQuotation": "Devis", "colUnitPrice": "Unitaire", "colDiscount": "Remise", "colFreight": "Fret", "colTaxes": "Taxes", "colGoodsTotal": "Total marchandise", "colFullCost": "Coût complet", "lowestProposal": "PLUS BASSE PROPOSITION", "decidedTag": "DÉCIDÉ",
            "cancelAction": "Annuler", "cancelTitle": "Annuler le devis", "cancelHint": "Seuls les devis ouverts peuvent être annulés. La justification demeure dans l'historique auditable.", "cancelReason": "Justification de l'annulation *", "phCancelReason": "Expliquez le motif réel de l'annulation", "cancelConfirmBtn": "Confirmer l'annulation", "cancelledToast": "Devis annulé", "cancelledToastSub": "Le devis a été archivé avec une justification auditable ; la requête peut recevoir un nouveau devis.",
            "selectByName": "Sélectionner par nom", "selectByNumber": "Sélectionner par numéro", "naService": "Non applicable à un service", "selectAccount": "Sélectionner le compte", "selectForMaterial": "Sélectionner pour matériel/actif", "useContext": "Utiliser le contexte actuel", "selectByCode": "Sélectionner par nom ou code", "listsUnavailable": "Listes indisponibles", "listsUnavailableSub": "Impossible de charger tous les sélecteurs autorisés.",
            "selPendingItemPh": "Sélectionnez l'article en attente", "noPendingBalance": "Commande sans solde en attente", "balanceWord": "solde", "matchOption": "{0} · {1} · solde {2} {3}", "selectMatchLine": "Sélectionnez la ligne vérifiée", "noAcceptedAvailable": "Aucun solde accepté disponible",
            "confirmReceiveTitle": "Confirmer la réception", "confirmReceiveMsg": "L'opération effectuera l'entrée physique et créera des prévisions financières ouvertes. Les écritures ne sont pas répétées automatiquement.", "confirmReceiveBtn": "Réceptionner",
            "minOneItem": "Indiquez au moins un article avec catalogue sélectionné.", "saving": "Enregistrement…", "operationDone": "Opération terminée", "operationDoneSub": "Les liens de stock et financier ont été persistés sans duplication.",
            "confirmQualityTitle": "Confirmer la décision de qualité", "confirmQualityMsg": "Seule la quantité acceptée sera libérée. La décision demeure dans l'historique.", "qualityRegisteredSub": "La disponibilité a été mise à jour uniquement pour la quantité acceptée.",
            "confirmDivergence": "Confirmer la décision", "confirmDivergenceMsg": "La divergence sera enregistrée comme {0}. Cela n'effectue aucun paiement.", "divergenceResolvedSub": "La vérification a été mise à jour et l'historique préservé.",
            "confirmReturnTitle": "Confirmer le retour fournisseur", "confirmReturnMsg": "Le retour reste en attente d'approbation par un autre utilisateur habilité. Aucun solde ne change avant l'approbation.", "confirmReturnBtn": "Enregistrer le retour",
            "minOneReturnQty": "Indiquez la quantité d'au moins un article retornable.", "returnRegistered": "Retour enregistré", "returnRegisteredSub": "En attente de décision d'un approbateur sans lien avec l'enregistrement.",
            "approveReturnTitle": "Approuver le retour", "approveReturnMsg": "Le stock sera diminué, le solde de la commande rouvert et un crédit ouvert généré. Aucun compte n'est payé ni compensé automatiquement.",
            "rejectReturnTitle": "Rejeter le retour", "rejectReturnMsg": "Le retour sera archivé sans effet sur stock, commande ou finances.",
            "rejectRequiresReason": "Le rejet exige une justification.", "returnApprovedToast": "Retour approuvé", "returnApprovedSub": "Stock diminué, solde de commande rouvert et crédit ouvert enregistré.", "returnRejectedToast": "Retour rejeté", "returnRejectedSub": "Rien n'a changé en stock, commande ou finances.",
            "minTwoSuppliers": "Sélectionnez au moins deux fournisseurs participants.", "openingQuotation": "Ouverture du devis…", "quotationRequested": "Devis demandé", "quotationRequestedSub": "Participants enregistrés ; enregistrez les propositions reçues de chaque fournisseur.",
            "registeringProposal": "Enregistrement de la proposition…", "proposalRegistered": "Proposition enregistrée", "proposalRegisteredSub": "La proposition entre dans la comparaison et compte pour la décision du devis.",
            "qualityDialogTitle": "Décision de qualité", "acceptedQty": "Quantité acceptée", "rejectedQty": "Quantité rejetée", "resultLbl": "Résultat", "qApproved": "Approuvé", "qConditional": "Approuvé avec condition", "qRejected": "Rejeté", "evidenceLbl": "Preuve / document", "phReportRef": "Numéro ou référence du rapport", "justificationField2": "Justification", "phJustReq": "Obligatoire pour rejet ou condition", "registerReleaseBtn": "Enregistrer et libérer l'éligible",
            "supplierDialogTitle": "Nouveau fournisseur", "legalName": "Raison sociale", "tradeName": "Nom commercial", "taxDoc": "Document fiscal", "typeLbl": "Type", "mainCategory": "Catégorie principale", "emailLbl": "E-mail", "phoneLbl": "Téléphone", "countryLbl": "Pays", "statusLbl": "Statut", "stActive": "Actif", "stReview": "En homologation", "stInactive": "Inactif", "stBlocked": "Bloqué", "avgDelivery": "Délai moyen (jours)", "notesLbl": "Observations", "saveSupplierBtn": "Enregistrer le fournisseur",
            "catalogDialogTitle": "Nouvel article", "nameLbl": "Nom", "internalCode": "Code interne", "categoryLbl": "Catégorie", "unitField": "Unité", "stockProduct": "Produit de stock", "phStockProduct": "Sélectionner pour matériel/actif", "minStock": "Stock minimum", "requirements": "Exigences", "reqLot": "Lot", "reqExpiry": "Péremption", "reqDocument": "Document", "reqInspection": "Inspection", "reqApprovedSupplier": "Fournisseur homologué", "saveItemBtn": "Enregistrer l'article",
            "reqDialogTitle": "Nouvelle requête", "originLbl": "Origine / besoin", "phOrigin": "Maintenance, Culture, Qualité", "operationalUnit": "Unité opérationnelle", "priorityLbl": "Priorité", "neededOn": "Date nécessaire", "justificationField3": "Justification", "itemsRequested": "Articles demandés", "unitConsistency": "L'unité doit coïncider avec l'enregistrement de l'article ; les conversions sont explicites.", "addItem": "Ajouter un article", "submitNow": "Envoyer immédiatement à la file d'approbation", "saveReqBtn": "Enregistrer la requête",
            "orderDialogTitle": "Nouvelle commande", "supplierLbl": "Fournisseur", "orderItems": "Articles de la commande", "paymentTerms": "Conditions de paiement", "deliveryOn": "Livraison prévue", "deliveryAddress": "Adresse de livraison", "createForApproval": "Créer pour approbation",
            "receiptDialogTitle": "Enregistrer la réception", "approvedOrderSel": "Commande approuvée", "receivedAt": "Date/heure", "warehouseLbl": "Dépôt pour entrée physique", "financeAccount": "Compte de la prévision financière", "firstDueOn": "Première échéance", "installments": "Échéances", "invoiceDoc": "Facture/document", "overrideExcess": "Autoriser l'excédent", "excessJustification": "Justification de l'excédent", "notesWide": "Observations",
            "receiptFooter": "La confirmation enregistre la présence physique. Les articles sous inspection ou périmés passent en quarantaine et n'entrent pas dans le solde disponible ; la prévision financière reste ouverte, jamais payée.", "confirmReceiveFull": "Vérifier et réceptionner",
            "pendingItemLbl": "Article en attente *", "lotLbl": "Lot du fournisseur", "expiryLbl": "Péremption", "dupPendingItem": "Le même article en attente ne peut pas figurer dans deux lignes de réception.", "minOnePendingLine": "Indiquez au moins une ligne avec article en attente et quantité supérieure à zéro.",
            "newQuotationTitle": "Nouveau devis", "quotRequestHint": "Seules les requêtes approuvées ou partiellement exécutées acceptent un devis, et au plus un devis est ouvert par requête.", "approvedReqLbl": "Requête approuvée *", "selApprovedReqPh": "Sélectionnez la requête approuvée", "dueDateLbl": "Délai pour recevoir les propositions *", "participantsLbl": "Fournisseurs participants *", "loadingSuppliers": "Chargement des fournisseurs actifs…", "atLeastTwo": "Au moins deux fournisseurs. Maintenez Ctrl pour en marquer plusieurs.", "requestQuotationBtn": "Demander le devis",
            "quoteTitle": "Enregistrer la proposition reçue", "quoteHint": "Saisie exactement comme le fournisseur l'a envoyée ; rien n'est coté ni envoyé automatiquement.", "participantLbl": "Fournisseur participant *", "quotedItemLbl": "Article coté *", "unitPriceLbl": "Prix unitaire *", "discountUnit": "Remise par unité", "deliveryDays": "Délai de livraison (jours) *", "proposalValidity": "Validité de la proposition", "taxesLbl": "Taxes", "registerQuoteBtn": "Enregistrer la proposition",
            "quotDetailTitle": "Détails du devis", "quotDetailHelpSummary": "Comment utiliser", "quotDetailHelpBody": "Comparez les propositions enregistrées, choisissez le gagnant par article (la sélection par défaut est la plus basse proposition) et enregistrez la décision. Seule la conversion crée des commandes — une par fournisseur gagnant, sans duplication si répétée.",
            "compareTitle": "Comparaison des devis de la requête", "compareHelpSummary": "Comment utiliser", "compareHelpBody": "Toutes les propositions en vigueur des devis de la requête, triées par article. La plus basse proposition de chaque article est marquée ; les décisions enregistrées apparaissent signalées.",
            "receiptDetailTitle": "Détails de la réception", "receiptDetailHelpSummary": "Comment utiliser", "receiptDetailHelpBody": "Consultez articles, lots et quantités bloquées, disponibles et rejetées. Utilisez Décider seulement après avoir réalisé l'inspection et réuni la preuve applicable.",
            "divergenceTitle": "Décider de la divergence de vérification", "divergenceHint": "La justification est obligatoire et demeure dans l'historique. Aucune de ces décisions n'effectue de paiement.", "decisionFieldLbl": "Décision *", "approveException": "Approuver l'exception", "requestCorrection": "Demander correction", "rejectBilling": "Rejeter la facture", "justificationReq": "Justification *", "evidenceLbl2": "Preuve / document", "phProofRef": "Numéro ou référence du justificatif", "confirmDecisionBtn": "Confirmer la décision",
            "returnDialogTitle": "Renvoyer les articles au fournisseur", "returnDialogHint": "Seul le solde accepté et non encore retourné peut revenir au fournisseur. Le retour attend l'approbation d'un autre utilisateur ; rien ne sort du stock avant elle.", "returnReason": "Motif du retour *", "phReturnReason": "Décrivez le motif réel du retour", "registerReturnBtn": "Enregistrer le retour",
            "returnDecisionTitle": "Décider du retour fournisseur", "returnDecisionHint": "Approuver diminue le stock, rouvre le solde de la commande et génère un crédit ouvert sans compenser automatiquement les titres. Rejeter n'altère ni stock, ni commande, ni finances.", "approveReturnOpt": "Approuver le retour", "rejectReturnOpt": "Rejeter le retour", "phReasonReq": "Obligatoire pour un rejet",
            "returnDetailTitle": "Détails du retour fournisseur", "returnDetailHelpSummary": "Comment utiliser", "returnDetailHelpBody": "Consultez les articles retournés, l'état de la décision et le crédit ouvert. Un crédit ouvert ne représente ni paiement ni compensation automatique des comptes fournisseurs.",
            "matchFormTitle": "Vérifier la facture partielle", "matchFormHint": "Les champs avec * sont obligatoires. Le lien est manuel : des noms similaires ne sont jamais associés automatiquement.", "orderSel": "Commande *", "matchLineLabel": "Ligne reçue et acceptée *", "selOrderPh": "Sélectionnez la commande", "selOrderFirstPh": "Sélectionnez d'abord la commande", "documentNumber": "Numéro du document *", "series": "Série", "issueDate": "Émission *", "currency": "Devise *", "currencyHelp": "Une autre devise exige une politique de change et sera bloquée.", "billedQty": "Quantité facturée *", "lineDiscount": "Remise de ligne", "generalDiscount": "Remise générale", "freight": "Fret", "additional": "Montants additionnels", "lineDescription": "Description de la ligne *", "phLineDoc": "Description exactement comme constée sur le document", "calcTotalInit": "Total calculé : R$ 0,00", "calcTotal": "Total calculé", "registerMatchBtn": "Vérifier et enregistrer", "matchDetailTitle": "Résultat de la vérification",
            "toleranceTitle": "Tolérances de vérification", "toleranceHint": "Limites au-delà desquelles l'écart entre contrat, acceptation et facturation est vérifié sans exception. Les modifications sont versionnées et auditées.",
            "tolQtyPercent": "Divergence de quantité (%)", "tolQtyAbs": "Divergence de quantité absolue", "tolPricePercent": "Divergence de prix (%)", "tolPriceAbs": "Divergence de prix absolue", "tolTotalPercent": "Divergence de total (%)", "tolTotalAbs": "Divergence de total absolue", "tolExcessPercent": "Excédent de réception (%)", "tolExcessAbs": "Excédent de réception absolu", "tolDeliveryDays": "Tolérance de livraison (jours)", "tolSod": "Ségrégation des tâches (ne pas permettre de vérifier et décider sur la même personne)",
            "saveTolerances": "Enregistrer les tolérances", "toleranceSaved": "Tolérances enregistrées", "toleranceSavedSub": "Les nouvelles règles s'appliquent aux prochaines vérifications ; les enregistrées ne changent pas.",
            "reqDetailTitle": "Détails de la requête", "originWord": "Origine", "unitWord": "Unité", "costCenterWord": "Centre de coût", "justificationWord": "Justification", "requesterWord": "Demandeur", "colEvent": "Événement", "colReason": "Motif", "colVersion": "Version", "colDateTime": "Date/heure", "colQuantity": "Quantité", "colPending": "Solde en attente", "noHistory": "Aucun événement enregistré.",
            "reasonDialogTitle": "Justifier la décision", "reasonDialogHint": "La justification est envoyée à l'endpoint et demeure dans l'historique d'audit.", "reasonField": "Justification *", "phReasonGeneric": "Expliquez le motif de votre décision", "minReasonLen": "Indiquez un motif avec au moins 5 caractères.",
            "confirmReqApprove": "Approuver la requête", "confirmReqApproveMsg": "Le solde approuvé alimente les devis et commandes ; la décision est auditée.",
            "reqApproved": "Requête approuvée", "reqApprovedSub": "Le solde approuvé alimente les devis et commandes.", "reqRejected": "Requête rejetée", "reqRejectedSub": "La requête a été archivée avec justification ; rien n'a changé en stock ou en finances.",
            "orderApproved": "Commande approuvée", "orderApprovedSub": "La commande est prête pour la livraison et la réception.", "orderCancelled": "Commande annulée", "orderCancelledSub": "Le solde de la requête redevient disponible."
        }
    };
    const dict = () => PROC_I18N[culture()] ?? PROC_I18N["pt-BR"];
    const tr = (key, ...args) => {
        let text = dict()[key] ?? PROC_I18N["pt-BR"][key] ?? key;
        args.forEach((arg, index) => { text = text.split(`{${index}}`).join(String(arg ?? "")); });
        return text;
    };
    // Aplica textos/placeholders/aria-labels estáticos marcados com data-i18n-pr* na própria tela.
    // O fallback é o HTML original (pt-BR), capturado na primeira aplicação; para pt-BR nada muda.
    function applyProcI18n() {
        const source = dict();
        document.querySelectorAll("[data-i18n-pr]").forEach(el => {
            if (el.dataset.i18nPrFallback === undefined) el.dataset.i18nPrFallback = el.textContent;
            const value = source[el.dataset.i18nPr] ?? el.dataset.i18nPrFallback;
            if (value.includes("<")) el.innerHTML = value; else if (el.textContent !== value) el.textContent = value;
        });
        document.querySelectorAll("[data-i18n-ph]").forEach(el => {
            if (el.dataset.i18nPhFallback === undefined) el.dataset.i18nPhFallback = el.placeholder ?? "";
            el.placeholder = source[el.dataset.i18nPh] ?? el.dataset.i18nPhFallback;
        });
        document.querySelectorAll("[data-i18n-aria]").forEach(el => {
            if (el.dataset.i18nAriaFallback === undefined) el.dataset.i18nAriaFallback = el.getAttribute("aria-label") ?? "";
            el.setAttribute("aria-label", source[el.dataset.i18nAria] ?? el.dataset.i18nAriaFallback);
        });
    }

    const tables = {
        suppliers: { head: ["hSupplier", "hCategory", "hPrazo", "hStatus"], row: item => [t(item.legal_name), t(item.main_category), `${Number(item.average_delivery_days || 0)} ${tr("daysUnit", "")}`, badge(item.status)] },
        catalog: { head: ["hCode", "hItem", "categoryLbl", "hType", "hSituation"], row: item => [t(item.internal_code), t(item.name), t(item.category), t(item.item_type), badge(item.active ? "ACTIVE" : "INACTIVE")] },
        requisitions: { head: ["hNumber", "hPriority", "hNeeded", "hItems", "hStatus", "hQuotations"], row: item => {
            const actions = ["APPROVED", "PARTIALLY_FULFILLED"].includes(item.status)
                ? `<button type="button" class="proc-link" data-quote-request="${item.id}">${tr("openQuotation")}</button> <button type="button" class="proc-link" data-compare="${item.id}">${tr("compareBtn")}</button>`
                : `<button type="button" class="proc-link" data-compare="${item.id}">${tr("compareBtn")}</button>`;
            return [t(item.number), badge(item.priority), fmtDate(item.needed_on), t(item.item_count), badge(item.status), actions];
        } },
        quotations: { head: ["hNumber", "hRequisition", "hUnitCol", "hParticipants", "hWithProposal", "hGeneratedOrders", "hValidity", "hStatus", "hAction"], row: item => {
            const actions = [`<button type="button" class="proc-link" data-quotation="${item.id}">${tr("detailsBtn")}</button>`];
            if (openQuotationStatuses.includes(item.status)) {
                actions.push(`<button type="button" class="proc-link" data-quote="${item.id}">${tr("registerProposal")}</button>`);
                actions.push(`<button type="button" class="proc-link" data-quote-cancel="${item.id}">${tr("cancelBtn")}</button>`);
            }
            return [t(item.number), t(item.requisition_number), t(item.operational_unit), t(item.supplier_count), t(item.responded_suppliers), t(item.converted_orders), fmtDate(item.valid_until), badge(item.status), actions.join(" ")];
        } },
        orders: { head: ["hNumber", "hSupplier", "hDeliveryOn", "hTotal", "hStatus"], row: item => [`<span data-order-id="${escape(item.id)}">${t(item.number)}</span>`, t(item.supplier_name), fmtDate(item.delivery_on), money(item.total), badge(item.status)] },
        "supplier-returns": { head: ["hReturn", "hReceipt", "hOrder", "hSupplier", "hTotal", "hStatus", "hDecision", "hAction"], row: item => [t(item.number), t(item.receipt_number), t(item.order_number), t(item.supplier_name), money(item.total), badge(item.status), item.decided_at ? `${fmtDateTime(item.decided_at)}${item.decision_reason ? ` · ${t(item.decision_reason)}` : ""}` : tr("waiting"), `<button type="button" class="proc-link" data-return="${item.id}">${tr("detailsBtn")}</button>${item.status === "PENDING_APPROVAL" ? ` <button type="button" class="proc-link" data-return-decision="${item.id}">${tr("decideBtn")}</button>` : ""}`] },
        receipts: { head: ["hReceipt", "hOrder", "hSupplier", "hDate", "hItems", "hStock", "hFinance", "hStatus", "hNextAction"], row: item => [t(item.number), t(item.order_number), t(item.supplier_name), fmtDateTime(item.received_at), t(item.item_count), badge(item.stock_integration_status), badge(item.finance_integration_status), badge(item.status), `<button type="button" class="proc-link" data-receipt="${item.id}">${item.status === "DIVERGENT" ? tr("inspect") : tr("viewBtn")}</button>`] },
        "invoice-matches": { head: ["hDocument", "hOrder", "hSupplier", "hIssuedOn", "hTotal", "hDifference", "hSituation", "hAction"], row: item => [`${t(item.document_number)}${item.document_series ? ` / ${t(item.document_series)}` : ""}`, t(item.order_number), t(item.supplier_name), fmtDate(item.issued_on), money(item.total), money(item.difference_total), badge(item.status), `<button type="button" class="proc-link" data-match="${item.id}">${tr("pendingCount", escape(item.open_divergences))}</button>`] }
    };
    const listNames = () => Object.keys(tables);

    async function dashboard() {
        const element = document.querySelector("#proc-kpis");
        try {
            const data = await request("dashboard");
            const values = [["kpiDraft", data.requisitions_draft], ["kpiAwaitingApproval", data.requisitions_awaiting_approval], ["kpiApprovedBalance", data.requisitions_approved_balance], ["kpiUrgent", data.requisitions_urgent], ["kpiQuotations", data.quotations_running], ["kpiOrdersApprove", data.orders_awaiting_approval], ["kpiDivergent", data.divergent_receipts], ["kpiActiveSuppliers", data.active_suppliers], ["kpiBlockedSuppliers", data.blocked_suppliers], ["kpiPartial", data.orders_partially_received], ["kpiMonthPurchased", money(data.purchased_month)]];
            element.innerHTML = values.map(item => `<article class="proc-kpi"><strong>${escape(item[1] ?? 0)}</strong><span>${tr(item[0])}</span></article>`).join("");
        } catch (error) { element.innerHTML = `<div class="proc-empty">${escape(error.message)}</div>`; }
    }

    async function list(name, form) {
        const box = document.querySelector(`[data-content="${name}"]`);
        box.innerHTML = `<div class="proc-loading">${tr("loading")}</div>`;
        try {
            const query = form ? new URLSearchParams(new FormData(form)) : "";
            const rows = await request(`${name}?${query}`);
            const definition = tables[name];
            const head = definition.head.map(key => tr(key));
            box.innerHTML = rows.length ? `<table><thead><tr>${head.map(item => `<th>${escape(item)}</th>`).join("")}</tr></thead><tbody>${rows.map(row => `<tr>${definition.row(row).map(value => `<td>${value ?? "—"}</td>`).join("")}</tr>`).join("")}</tbody></table>` : `<div class="proc-empty">${tr("empty")}</div>`;
            box.querySelectorAll("[data-receipt]").forEach(button => button.addEventListener("click", () => showReceipt(button.dataset.receipt)));
            box.querySelectorAll("[data-match]").forEach(button => button.addEventListener("click", () => showMatch(button.dataset.match)));
            box.querySelectorAll("[data-quotation]").forEach(button => button.addEventListener("click", () => showQuotation(button.dataset.quotation)));
            box.querySelectorAll("[data-quote]").forEach(button => button.addEventListener("click", () => openQuoteForm(button.dataset.quote)));
            box.querySelectorAll("[data-quote-cancel]").forEach(button => button.addEventListener("click", () => openQuotationCancel(button.dataset.quoteCancel)));
            box.querySelectorAll("[data-compare]").forEach(button => button.addEventListener("click", () => compareRequisition(button.dataset.compare)));
            box.querySelectorAll("[data-quote-request]").forEach(button => button.addEventListener("click", () => openQuotationRequest(button.dataset.quoteRequest)));
            box.querySelectorAll("[data-return]").forEach(button => button.addEventListener("click", () => showReturn(button.dataset.return)));
            box.querySelectorAll("[data-return-decision]").forEach(button => button.addEventListener("click", () => openReturnDecision(button.dataset.returnDecision)));
            if (name === "orders" && pendingOrderHighlight) {
                const target = box.querySelector(`[data-order-id="${pendingOrderHighlight}"]`);
                if (target) { target.closest("tr").classList.add("row-flash"); target.scrollIntoView({ block: "center", behavior: "smooth" }); }
                pendingOrderHighlight = null;
            }
        } catch (error) { box.innerHTML = `<div class="proc-empty" role="alert">${escape(error.message)}</div>`; }
    }

    // ── Fila de aprovações (alçada): requisitions AWAITING_APPROVAL + orders AWAITING_APPROVAL ──
    async function loadApprovals() {
        const box = document.querySelector('[data-content="approvals"]');
        if (!box) return;
        box.innerHTML = `<div class="proc-loading">${tr("loading")}</div>`;
        try {
            const [queue, orders] = await Promise.all([request("approval-queue?pageSize=100"), request("orders?status=AWAITING_APPROVAL&pageSize=100")]);
            if (!queue.length && !orders.length) { box.innerHTML = `<div class="proc-empty">${tr("empty")}</div>`; return; }
            const reqRows = queue.map(item => `<tr><td>${t(item.number)}</td><td>${badge(item.priority)}</td><td>${fmtDate(item.needed_on)}</td><td>${t(item.operational_unit)}</td><td>${t(item.requester)}</td><td><button type="button" class="proc-primary" data-req-approve="${escape(item.id)}" data-version="${item.version}">${tr("approveBtn")}</button> <button type="button" class="proc-link" data-req-reject="${escape(item.id)}" data-version="${item.version}">${tr("rejectBtn")}</button> <button type="button" class="proc-link" data-req-detail="${escape(item.id)}">${tr("detailsBtn")}</button></td></tr>`).join("");
            const orderRows = orders.map(item => `<tr><td>${t(item.number)}</td><td>${t(item.supplier_name)}</td><td>${fmtDate(item.delivery_on)}</td><td>${money(item.total)}</td><td>${badge(item.status)}</td><td><button type="button" class="proc-primary" data-order-approve="${escape(item.id)}">${tr("approveOrderBtn")}</button> <button type="button" class="proc-link" data-order-cancel="${escape(item.id)}" data-version="${item.version}">${tr("cancelOrderBtn")}</button></td></tr>`).join("");
            box.innerHTML = `<section><h3>${tr("approvalsReqsTitle")}</h3>${queue.length ? `<div class="proc-table"><table><thead><tr><th>${tr("hNumber")}</th><th>${tr("hPriority")}</th><th>${tr("hNeeded")}</th><th>${tr("hUnitCol")}</th><th>${tr("requesterWord")}</th><th>${tr("hAction")}</th></tr></thead><tbody>${reqRows}</tbody></table></div>` : `<p class="proc-empty">${tr("empty")}</p>`}</section>
            <section><h3>${tr("approvalsOrdersTitle")}</h3>${orders.length ? `<div class="proc-table"><table><thead><tr><th>${tr("hNumber")}</th><th>${tr("hSupplier")}</th><th>${tr("hDeliveryOn")}</th><th>${tr("hTotal")}</th><th>${tr("hStatus")}</th><th>${tr("hAction")}</th></tr></thead><tbody>${orderRows}</tbody></table></div>` : `<p class="proc-empty">${tr("empty")}</p>`}</section>`;
            box.querySelectorAll("[data-req-approve]").forEach(button => button.addEventListener("click", async () => {
                if (!await window.confirmDialog(tr("confirmReqApprove"), tr("confirmReqApproveMsg"), tr("approveBtn"))) return;
                button.disabled = true;
                try {
                    await request(`requisitions/${encodeURIComponent(button.dataset.reqApprove)}/decision`, { method: "POST", body: JSON.stringify({ version: Number(button.dataset.version), decision: "APPROVE", reason: null }) });
                    window.toastSuccess?.(tr("reqApproved"), tr("reqApprovedSub"));
                    await Promise.all([loadApprovals(), list("requisitions"), dashboard()]);
                } catch (error) { window.toastError?.(tr("operationFailed"), error.message); button.disabled = false; }
            }));
            box.querySelectorAll("[data-req-reject]").forEach(button => button.addEventListener("click", () => openDecisionReason({ kind: "REQ_REJECT", id: button.dataset.reqReject, version: button.dataset.version })));
            box.querySelectorAll("[data-req-detail]").forEach(button => button.addEventListener("click", () => openRequisitionDetail(button.dataset.reqDetail)));
            box.querySelectorAll("[data-order-approve]").forEach(button => button.addEventListener("click", async () => {
                if (!await window.confirmDialog(tr("approveOrderBtn"), tr("orderApprovedSub"), tr("approveBtn"))) return;
                button.disabled = true;
                try {
                    await request(`orders/${encodeURIComponent(button.dataset.orderApprove)}/approve`, { method: "POST", body: JSON.stringify("") });
                    window.toastSuccess?.(tr("orderApproved"), tr("orderApprovedSub"));
                    await Promise.all([loadApprovals(), list("orders"), dashboard()]);
                } catch (error) { window.toastError?.(tr("operationFailed"), error.message); button.disabled = false; }
            }));
            box.querySelectorAll("[data-order-cancel]").forEach(button => button.addEventListener("click", () => openDecisionReason({ kind: "ORDER_CANCEL", id: button.dataset.orderCancel, version: button.dataset.version })));
        } catch (error) { box.innerHTML = `<div class="proc-empty" role="alert">${escape(error.message)}</div>`; }
    }
    const reloadApprovals = () => Promise.all([loadApprovals(), dashboard()]);
    function openDecisionReason(payload) {
        const dialog = document.querySelector("#decision-reason-dialog"), form = dialog.querySelector("form");
        form.reset(); form.querySelector(".form-message").textContent = "";
        form.elements.decisionKind.value = payload.kind; form.elements.decisionId.value = payload.id; form.elements.decisionVersion.value = payload.version ?? 0;
        dialog.showModal();
    }
    async function openRequisitionDetail(id) {
        const dialog = document.querySelector("#requisition-detail-dialog"), content = dialog.querySelector(".requisition-detail-content");
        content.innerHTML = `<div class="proc-loading">${tr("loading")}</div>`; if (!dialog.open) dialog.showModal();
        try {
            const data = await request(`requisitions/${encodeURIComponent(id)}`), r = data.requisition;
            content.innerHTML = `<section class="receipt-summary"><strong>${t(r.number)}</strong><span>${tr("originWord")}: ${t(r.origin)}</span><span>${tr("unitWord")}: ${t(r.operational_unit)}</span><span>${tr("costCenterWord")}: ${t(r.cost_center)}</span><span>${tr("neededOn")}: ${fmtDate(r.needed_on)}</span>${badge(r.priority)} ${badge(r.status)}${r.approved_at ? `<small>${fmtDateTime(r.approved_at)}</small>` : ""}</section><p><b>${tr("justificationWord")}:</b> ${t(r.justification)}</p><h3>${tr("itemsRequested")}</h3><div class="proc-table"><table><thead><tr><th>${tr("hItem")}</th><th>${tr("colQuantity")}</th><th>${tr("hUnitCol")}</th><th>${tr("colPending")}</th></tr></thead><tbody>${data.items.map(i => `<tr><td>${t(i.catalog_name)}</td><td>${t(i.quantity)}</td><td>${t(i.unit)}</td><td>${t(i.pending_quantity)}</td></tr>`).join("")}</tbody></table></div><h3>${tr("colEvent")}s</h3>${data.history.length ? `<div class="proc-table"><table><thead><tr><th>${tr("colEvent")}</th><th>${tr("colReason")}</th><th>${tr("colVersion")}</th><th>${tr("colDateTime")}</th></tr></thead><tbody>${data.history.map(h => `<tr><td>${badge(h.event_type)}</td><td>${t(h.reason || "—")}</td><td>${t(h.version)}</td><td>${fmtDateTime(h.created_at)}</td></tr>`).join("")}</tbody></table></div>` : `<p class="proc-empty">${tr("noHistory")}</p>`}`;
        } catch (error) { content.innerHTML = `<div class="proc-empty" role="alert">${escape(error.message)}</div>`; }
    }

    async function showReceipt(id) {
        const dialog = document.querySelector("#receipt-detail-dialog"); const content = dialog.querySelector(".receipt-detail-content");
        content.innerHTML = `<div class="proc-loading">${tr("loadingReceipt")}</div>`; if (!dialog.open) dialog.showModal();
        try {
            const data = await request(`receipts/${encodeURIComponent(id)}`), r = data.receipt;
            content.innerHTML = `<section class="receipt-summary"><strong>${t(r.number)}</strong><span>${tr("receiptFor", t(r.order_number))}</span><span>${t(r.supplier_name)}</span>${badge(r.status)}</section><h3>${tr("itemsLotsAvail")}</h3><div class="proc-table"><table><thead><tr><th>${tr("colItemLot")}</th><th>${tr("colOrdered")}</th><th>${tr("colReceived")}</th><th>${tr("colBlocked")}</th><th>${tr("colAvailable")}</th><th>${tr("colRejected")}</th><th>${tr("colAction")}</th></tr></thead><tbody>${data.items.map(i => `<tr><td>${t(i.name)}<small>${t(i.supplier_lot || tr("noLot"))} · ${t(i.unit)}</small></td><td>${t(i.ordered_quantity)}</td><td>${t(i.quantity)}</td><td>${Math.max(0, Number(i.quarantine_quantity)-Number(i.released_quantity)-Number(i.rejected_quantity))}</td><td>${i.released_quantity || (i.quality_status === "NOT_REQUIRED" ? i.quantity : 0)}</td><td>${t(i.rejected_quantity)}</td><td>${i.quality_status === "PENDING" ? `<button type="button" class="proc-primary" data-quality="${i.id}" data-max="${Number(i.quantity)-Number(i.released_quantity)-Number(i.rejected_quantity)}">${tr("decideBtn")}</button>` : escape(i.quality_status)}</td></tr>`).join("")}</tbody></table></div><h3>${tr("qualityHistory")}</h3>${data.history.length ? data.history.map(h => `<p><strong>${t(h.result)}</strong> · ${tr("acceptedRejected", t(h.accepted_quantity), t(h.rejected_quantity))} · ${t(h.reason || tr("noReserve"))}</p>`).join("") : `<p class="proc-empty">${tr("noQualityDecisions")}</p>`}${["PARTIAL", "RECEIVED", "DIVERGENT"].includes(r.status) ? `<p><button type="button" class="proc-primary" data-return-create="${escape(r.id)}">${tr("returnCreate")}</button> <small>${tr("returnCreateHint")}</small></p>` : ""}`;
            content.querySelectorAll("[data-quality]").forEach(button => button.addEventListener("click", () => openQuality(button.dataset.quality, button.dataset.max, id)));
            content.querySelectorAll("[data-return-create]").forEach(button => button.addEventListener("click", () => openSupplierReturn(button.dataset.returnCreate)));
        } catch (error) { content.innerHTML = `<div class="proc-empty" role="alert">${escape(error.message)}</div>`; }
    }
    function openQuality(itemId, maximum, receiptId) { const form = document.querySelector("#quality-form"); form.reset(); form.elements.receiptItemId.value = itemId; form.elements.receiptId.value = receiptId; form.elements.acceptedQuantity.max = maximum; form.elements.rejectedQuantity.max = maximum; form.querySelector("[data-quality-max]").textContent = tr("waitingDecision", maximum); document.querySelector("#quality-dialog").showModal(); }
    // ── Devolução ao fornecedor: registro no recebimento → aprovação de outro usuário → crédito aberto ──
    async function openSupplierReturn(receiptId) {
        const dialog = document.querySelector("#supplier-return-dialog"), form = dialog.querySelector("form"), box = form.querySelector("[data-return-items]");
        form.reset(); form.elements.receiptId.value = receiptId; form.querySelector(".form-message").textContent = "";
        box.innerHTML = `<div class="proc-loading">${tr("loadingReturnable")}</div>`;
        form.querySelector("[data-return-submit]").disabled = true;
        dialog.showModal();
        try {
            const items = await request(`receipts/${encodeURIComponent(receiptId)}/returnable-items`);
            const eligible = items.map(item => ({ ...item, eligible: Math.max(0, Number(item.accepted_quantity) - Number(item.returned_quantity)) })).filter(item => item.eligible > 0);
            box.innerHTML = eligible.length ? `<div class="proc-table"><table><thead><tr><th>${tr("hItem")}</th><th>${tr("colAccepted")}</th><th>${tr("colReturned")}</th><th>${tr("colReturnQty")}</th></tr></thead><tbody>${eligible.map(item => `<tr><td>${t(item.name)}<small>${t(item.supplier_lot || tr("noLot"))} · ${t(item.unit)} · ${money(item.unit_price)}/${t(item.unit)}</small></td><td>${t(item.accepted_quantity)}</td><td>${t(item.returned_quantity)}</td><td><input name="qty-${escape(item.id)}" data-return-qty="${escape(item.id)}" type="number" min="0" max="${item.eligible}" step="0.001" value="0" aria-label="${escape(tr("colReturnQty") + " " + item.name)}"></td></tr>`).join("")}</tbody></table></div>` : `<p class="proc-empty">${tr("noEligibleItems")}</p>`;
            form.querySelector("[data-return-submit]").disabled = !eligible.length;
        } catch (error) { box.innerHTML = `<div class="proc-empty">${escape(error.message)}</div>`; }
    }
    async function showReturn(id) {
        const dialog = document.querySelector("#return-detail-dialog"), content = dialog.querySelector(".return-detail-content");
        content.innerHTML = `<div class="proc-loading">${tr("loadingReturn")}</div>`; if (!dialog.open) dialog.showModal();
        try {
            const data = await request(`supplier-returns/${encodeURIComponent(id)}`), h = data.header;
            content.innerHTML = `<section class="receipt-summary"><strong>${t(h.number)}</strong><span>${tr("hReceipt")} ${t(h.receipt_number)}</span><span>${tr("hOrder")} ${t(h.order_number)}</span><span>${t(h.supplier_name)}</span>${badge(h.status)}</section><p><b>${tr("reasonLbl")}</b> ${t(h.reason)}</p>${h.decided_at ? `<p><b>${tr("decisionLbl")}</b> ${fmtDateTime(h.decided_at)}${h.decision_reason ? ` · ${t(h.decision_reason)}` : ""}</p>` : ""}<h3>${tr("returnedItems")}</h3><div class="proc-table"><table><thead><tr><th>${tr("hItem")}</th><th>${tr("colQuantity")}</th><th>${tr("colUnitCost")}</th><th>${tr("hTotal")}</th></tr></thead><tbody>${data.items.map(i => `<tr><td>${t(i.name)}<small>${t(i.lot_number || tr("noLot"))} · ${t(i.item_type)}</small></td><td>${t(i.quantity)} ${t(i.unit)}</td><td>${money(i.unit_cost)}</td><td>${money(Number(i.quantity) * Number(i.unit_cost))}</td></tr>`).join("")}</tbody></table></div><p>${h.credit_number ? `<b>${tr("creditText", t(h.credit_number))}</b> ${money(h.credit_amount)} · <span class="proc-badge">${escape(h.credit_status)}</span> — ${tr("creditOpenNote")}` : tr("noCreditYet")}</p>${h.status === "PENDING_APPROVAL" ? `<button type="button" class="proc-primary" data-return-decision-detail="${escape(h.id)}">${tr("decideReturnBtn")}</button>` : ""}`;
            content.querySelectorAll("[data-return-decision-detail]").forEach(button => button.addEventListener("click", () => { dialog.close(); openReturnDecision(button.dataset.returnDecisionDetail); }));
        } catch (error) { content.innerHTML = `<div class="proc-empty" role="alert">${escape(error.message)}</div>`; }
    }
    function openReturnDecision(id) { const form = document.querySelector("#return-decision-form"); form.reset(); form.elements.returnId.value = id; form.querySelector(".form-message").textContent = ""; document.querySelector("#return-decision-dialog").showModal(); }
    async function showMatch(id) {
        const dialog = document.querySelector("#match-detail-dialog"), content = dialog.querySelector(".match-detail-content"); content.innerHTML = `<div class="proc-loading">${tr("loadingMatch")}</div>`; if (!dialog.open) dialog.showModal();
        try { const data = await request(`invoice-matches/${encodeURIComponent(id)}`), m = data.match; content.innerHTML = `<section class="receipt-summary"><strong>${tr("documentLbl", t(m.document_number))}</strong><span>${tr("hOrder")} ${t(m.order_number)}</span><span>${t(m.supplier_name)}</span>${badge(m.status)}</section><p><b>${tr("contracted")}</b> ${money(m.contracted_total)} · <b>${tr("billedLbl")}</b> ${money(m.billed_total)} · <b>${tr("differenceLbl")}</b> ${money(m.difference_total)}${m.difference_percent == null ? ` ${tr("zeroBase")}` : ` (${Number(m.difference_percent).toLocaleString(culture())}%)`}</p><p>${tr("fiscalValidation")} <b>${t(m.fiscal_validation_status || "NOT_VALIDATED")}</b>. ${tr("matchApprovedNote")}</p><h3>${tr("linkedLines")}</h3><div class="proc-table"><table><thead><tr><th>${tr("hItem")}</th><th>${tr("hReceipt")}</th><th>${tr("colQty")}</th><th>${tr("colPrice")}</th><th>${tr("hTotal")}</th></tr></thead><tbody>${data.lines.map(l => `<tr><td>${t(l.item_name)}</td><td>${t(l.receipt_number)}</td><td>${t(l.quantity)} ${t(l.unit)}</td><td>${money(l.unit_price)}</td><td>${money(l.total)}</td></tr>`).join("")}</tbody></table></div><h3>${tr("divergencesLbl")}</h3>${data.divergences.length ? data.divergences.map(d => `<article class="proc-divergence"><header>${badge(d.status)} <b>${t(d.type)}</b></header><p>${t(d.description)}</p><p><b>${tr("impactLbl")}</b> ${t(d.operational_impact)}<br><b>${tr("actionLbl")}</b> ${t(d.required_action)}</p>${d.status === "OPEN" ? `<button type="button" class="proc-primary" data-resolve="${d.id}">${tr("resolveException")}</button>` : `<p><b>${tr("decisionResult")}</b> ${t(d.resolution)} — ${t(d.resolution_reason)}</p>`}</article>`).join("") : `<p class="proc-empty">${tr("noDivergences")}</p>`}`; content.querySelectorAll("[data-resolve]").forEach(b => b.onclick = () => openDivergenceDialog(b.dataset.resolve, id)); } catch (error) { content.innerHTML = `<div class="proc-empty" role="alert">${escape(error.message)}</div>`; }
    }
    function openDivergenceDialog(divergenceId, matchId) {
        const dialog = document.querySelector("#divergence-dialog"), form = dialog.querySelector("form");
        form.reset(); form.elements.divergenceId.value = divergenceId; form.elements.matchId.value = matchId;
        dialog.showModal();
    }

    // ── Jornada de cotações: solicitar → registrar propostas → comparar → decidir (+justificativa) → converter/cancelar ──
    async function openQuotationRequest(requisitionId) {
        const dialog = document.querySelector("#quotation-request-dialog"), form = dialog.querySelector("form");
        form.reset();
        const dayMs = 86400000; form.elements.dueDate.min = new Date(Date.now() + dayMs).toISOString().slice(0, 10);
        form.elements.dueDate.value = new Date(Date.now() + 8 * dayMs).toISOString().slice(0, 10);
        try {
            const [approved, partial] = await Promise.all([request("requisitions?status=APPROVED&pageSize=100"), request("requisitions?status=PARTIALLY_FULFILLED&pageSize=100")]);
            form.elements.requisitionId.innerHTML = optionList([...approved, ...partial], "id", item => tr("quotOption", t(item.number), t(item.priority), fmtDate(item.needed_on)), tr("selApprovedReqPh"));
            const suppliers = await request("suppliers?status=ACTIVE&pageSize=100");
            form.elements.supplierIds.innerHTML = suppliers.map(item => `<option value="${escape(item.id)}">${t(item.legal_name)}</option>`).join("");
            if (requisitionId) form.elements.requisitionId.value = requisitionId;
            dialog.showModal();
        } catch (error) { window.toastWarning?.(tr("openFailed"), error.message); }
    }

    async function openQuoteForm(quotationId) {
        const dialog = document.querySelector("#quote-dialog"), form = dialog.querySelector("form");
        form.reset(); form.elements.quoteQuotationId.value = quotationId;
        try {
            const detail = await request(`quotations/${encodeURIComponent(quotationId)}`);
            const participants = detail.suppliers.filter(item => openQuotationStatuses.includes(item.status));
            if (!participants.length) throw new Error(tr("noEligibleParticipants"));
            form.elements.participant.innerHTML = optionList(participants, "supplier_id", item => `${t(item.supplier_name)} · ${t(item.status)}`, tr("selParticipantPh"));
            form.elements.quotationItem.innerHTML = detail.items.map(item => `<option value="${escape(item.id)}" data-catalog="${escape(item.catalog_item_id)}" data-unit="${escape(item.unit)}">${tr("quotItemOption", t(item.catalog_name), t(item.quantity), t(item.unit))}</option>`).join("");
            form.elements.deliveryDays.max = 365;
            dialog.showModal();
        } catch (error) { window.toastWarning?.(tr("registerFailed"), error.message); }
    }
    function openQuotationCancel(quotationId) {
        const form = document.querySelector("#quotation-cancel-form");
        form.reset(); form.querySelector(".form-message").textContent = ""; form.elements.quotationId.value = quotationId;
        document.querySelector("#quotation-cancel-dialog").showModal();
    }

    async function showQuotation(id) {
        const dialog = document.querySelector("#quotation-detail-dialog"), content = dialog.querySelector(".quotation-detail-content");
        content.innerHTML = `<div class="proc-loading">${tr("loadingQuotation")}</div>`; if (!dialog.open) dialog.showModal();
        try {
            const data = await request(`quotations/${encodeURIComponent(id)}`), q = data.quotation;
            const offersByItem = new Map(data.items.map(item => [item.id, data.responses.filter(response => response.quotation_item_id === item.id && response.available)]));
            const supplierName = id2 => (data.suppliers.find(s => s.id === id2) || {}).supplier_name || "—";
            const decidedItem = new Set(data.decisions.map(d => d.quotation_item_id));
            const canDecide = openQuotationStatuses.includes(q.status) && data.responses.some(r => r.available);
            const canConvert = convertibleQuotationStatuses.includes(q.status) && data.decisions.length > 0 && !data.converted_orders.length;
            content.innerHTML = `<section class="receipt-summary"><strong>${t(q.number)}</strong><span>${tr("requisitionOf", t(q.requisition_number), t(q.requisition_status))}</span><span>${tr("unitOf", t(q.operational_unit))}</span><span>${tr("proposalsUntil", fmtDate(q.valid_until))}</span>${badge(q.status)}${openQuotationStatuses.includes(q.status) ? ` <button type="button" class="proc-link" data-quote-cancel-detail="${escape(id)}">${tr("cancelBtn")}</button>` : ""}</section>
            <h3>${tr("itemsProposals")}</h3><div class="proc-table"><table><thead><tr><th>${tr("hItem")}</th><th>${tr("colBalance")}</th><th>${tr("colLowest")}</th><th>${tr("colWithOffers")}</th></tr></thead><tbody>${data.items.map(i => `<tr><td>${t(i.catalog_code)} · ${t(i.catalog_name)}</td><td>${t(i.quantity)} ${t(i.unit)}</td><td>${Number(i.lowest_total) > 0 ? money(i.lowest_total) : tr("noOffer")}</td><td>${t(i.quoted_by)}</td></tr>`).join("")}</tbody></table></div>
            <h3>${tr("participantsSection")}</h3><div class="proc-table"><table><thead><tr><th>${tr("hSupplier")}</th><th>${tr("colQuotedItems")}</th><th>${tr("hTotal")}</th><th>${tr("colDeliveryDays")}</th><th>${tr("colPayment")}</th><th>${tr("hStatus")}</th></tr></thead><tbody>${data.suppliers.map(s => `<tr><td>${t(s.supplier_name)}</td><td>${t(s.item_count)}</td><td>${money(s.grand_total)}</td><td>${s.delivery_days == null ? "—" : tr("daysUnit", t(s.delivery_days))}</td><td>${t(s.payment_terms)}</td><td>${badge(s.status)}</td></tr>`).join("")}</tbody></table></div>
            ${data.decisions.length ? `<h3>${tr("registeredDecisions")}</h3><div class="proc-table"><table><thead><tr><th>${tr("hItem")}</th><th>${tr("colChosenSupplier")}</th><th>${tr("colChosenTotal")}</th><th>${tr("colLowest")}</th><th>${tr("justificationField2")}</th></tr></thead><tbody>${data.decisions.map(d => `<tr><td>${t(d.item_name)}</td><td>${t(d.supplier_name)}</td><td>${money(d.selected_total)}</td><td>${money(d.lowest_total)}</td><td>${t(d.justification)}</td></tr>`).join("")}</tbody></table></div>` : ""}
            ${data.converted_orders.length ? `<h3>${tr("generatedOrdersSection")}</h3><div class="proc-table"><table><thead><tr><th>${tr("hOrder")}</th><th>${tr("hSupplier")}</th><th>${tr("hTotal")}</th><th>${tr("hDeliveryOn")}</th><th>${tr("hStatus")}</th></tr></thead><tbody>${data.converted_orders.map(o => `<tr><td>${t(o.number)}</td><td>${t(o.supplier_name)}</td><td>${money(o.total)}</td><td>${fmtDate(o.delivery_on)}</td><td>${badge(o.status)}</td></tr>`).join("")}</tbody></table></div>` : ""}
            ${canDecide ? `<form id="decide-form" class="proc-decide"><h3>${tr("winnersSelection")}</h3><p>${tr("defaultSelectionNote")}</p><div class="proc-table"><table><thead><tr><th>${tr("hItem")}</th><th>${tr("colWinningProposal")}</th></tr></thead><tbody>${data.items.filter(i => offersByItem.get(i.id)?.length).map(i => `<tr><td>${t(i.catalog_name)} · ${t(i.quantity)} ${t(i.unit)}${decidedItem.has(i.id) ? ` · ${tr("alreadyDecided")}` : ""}</td><td><select name="pick-${escape(i.id)}" data-item="${escape(i.id)}" data-lowest="${escape(i.lowest_total)}">${offersByItem.get(i.id).sort((a, b) => Number(a.total) - Number(b.total)).map(r => `<option value="${escape(r.quotation_supplier_id)}" data-total="${escape(r.total)}">${t(supplierName(r.quotation_supplier_id))} — ${money(r.total)}${Number(r.total) === Number(i.lowest_total) ? ` · ${tr("lowestTag")}` : ""}</option>`).join("")}</select></td></tr>`).join("")}</tbody></table></div><label>${tr("justificationField")}<textarea name="justification" minlength="3" maxlength="1000"></textarea></label><button class="proc-primary">${tr("registerDecisionBtn")}</button><span class="form-message" role="alert"></span></form>` : ""}
            ${canConvert ? `<p class="proc-help">${tr("convertNote")}</p><button type="button" id="convert-quotation" class="proc-primary" data-id="${escape(id)}">${tr("convertBtn")}</button>` : ""}`;
            const decideForm = content.querySelector("#decide-form");
            if (decideForm) decideForm.addEventListener("submit", event => submitDecision(event, id));
            const convertButton = content.querySelector("#convert-quotation");
            if (convertButton) convertButton.addEventListener("click", () => convertQuotation(convertButton.dataset.id));
            content.querySelectorAll("[data-quote-cancel-detail]").forEach(button => button.addEventListener("click", () => openQuotationCancel(button.dataset.quoteCancelDetail)));
        } catch (error) { content.innerHTML = `<div class="proc-empty" role="alert">${escape(error.message)}</div>`; }
    }

    async function submitDecision(event, quotationId) {
        event.preventDefault();
        const form = event.currentTarget;
        const items = [...form.querySelectorAll("select[data-item]")].map(select => ({
            quotationItemId: select.dataset.item,
            quotationSupplierId: select.value
        })).filter(line => line.quotationSupplierId);
        const justification = form.elements.justification.value.trim();
        const aboveLowest = [...form.querySelectorAll("select[data-item]")].some(select => {
            const option = select.selectedOptions[0];
            return option && Number(option.dataset.total) > Number(select.dataset.lowest);
        });
        const message = form.querySelector(".form-message");
        if (!items.length) { message.textContent = tr("selectProposalMsg"); return; }
        if (aboveLowest && justification.length < 3) { message.textContent = tr("aboveLowestMsg"); return; }
        if (!await window.confirmDialog(tr("confirmDecisionTitle"), tr("confirmDecisionMsg"), tr("registerDecisionBtn"))) return;
        try {
            await request(`quotations/${encodeURIComponent(quotationId)}/decide`, { method: "POST", body: JSON.stringify({ items, justification: justification || null }) });
            window.toastSuccess?.(tr("decisionRegistered"), tr("decisionRegisteredSub"));
            await Promise.all([showQuotation(quotationId), list("quotations"), dashboard()]);
        } catch (error) { message.textContent = error.message; }
    }

    async function convertQuotation(quotationId) {
        if (!await window.confirmDialog(tr("convertTitle"), tr("convertMsg"), tr("convertBtn"))) return;
        const path = `quotations/${encodeURIComponent(quotationId)}/convert`;
        try {
            let result;
            try {
                result = await request(path, { method: "POST" });
            } catch (error) {
                // Menor preço é recomendação: sem decisão explícita a API pede confirmação e nada foi gravado.
                if (error.status !== 409 || error.problem?.code !== "agro360.quotation.confirmation_required") throw error;
                const proceed = await window.confirmDialog(tr("confirmLowestTitle"), tr("confirmLowestMsg", escape(error.message)), tr("convertBtn"));
                if (!proceed) return;
                result = await request(`${path}?confirmLowestPrice=true`, { method: "POST" });
            }
            window.toastSuccess?.(tr("converted"), tr("convertedSub", result.order_ids.length));
            await Promise.all([showQuotation(quotationId), list("quotations"), list("orders"), dashboard()]);
        } catch (error) { window.toastWarning?.(tr("convertFailed"), error.message); }
    }

    async function compareRequisition(requisitionId) {
        const dialog = document.querySelector("#compare-dialog"), content = dialog.querySelector(".compare-content");
        content.innerHTML = `<div class="proc-loading">${tr("comparing")}</div>`; if (!dialog.open) dialog.showModal();
        try {
            const rows = await request(`requisitions/${encodeURIComponent(requisitionId)}/quotations/compare`);
            if (!rows.length) { content.innerHTML = `<div class="proc-empty">${tr("noProposalsYet")}</div>`; return; }
            const groups = new Map();
            rows.forEach(row => { const key = `${row.quotation_item_id}`; if (!groups.has(key)) groups.set(key, []); groups.get(key).push(row); });
            // Memória do custo comparado por item: cada componente é exibido e o custo completo
            // (mercadoria + frete + tributos) é derivado somente dos valores registrados — nenhuma
            // conversão de unidade ou taxa é inventada.
            const allInCost = offer => Number(offer.total) + Number(offer.freight || 0) + Number(offer.taxes || 0);
            content.innerHTML = [...groups.entries()].map(([key, offers]) => {
                const lowestAllIn = Math.min(...offers.map(allInCost));
                return `<h3>${t(offers[0].item)} · ${t(offers[0].quantity)} ${t(offers[0].unit)}</h3>
                <p class="proc-help">${tr("lowestNote")} <b>${money(offers[0].lowest_total)}</b> · ${tr("fullCostNote")} <b>${money(lowestAllIn)}</b>${offers.some(offer => offer.quote_deadline) ? ` · ${tr("validUntilNote", fmtDate(offers[0].quote_deadline))}` : ""}</p>
                <div class="proc-table"><table><thead><tr><th>${tr("colQuotation")}</th><th>${tr("hSupplier")}</th><th>${tr("colUnitPrice")}</th><th>${tr("colDiscount")}</th><th>${tr("colFreight")}</th><th>${tr("colTaxes")}</th><th>${tr("colGoodsTotal")}</th><th>${tr("colFullCost")}</th><th>${tr("colDeliveryDays")}</th><th>${tr("colPayment")}</th><th>${tr("hSituation")}</th></tr></thead><tbody>${offers.map(offer => {
                    const allIn = allInCost(offer);
                    return `<tr><td>${t(offer.number)}</td><td>${t(offer.supplier)}</td><td>${money(offer.unit_price)}</td><td>${money(offer.discount)}</td><td>${money(offer.freight ?? 0)}</td><td>${money(offer.taxes ?? 0)}</td><td><b>${money(offer.total)}</b></td><td>${money(allIn)}${allIn === lowestAllIn ? ` · ${tr("lowestTag")}` : ""}</td><td>${offer.delivery_days == null ? "—" : tr("daysUnit", t(offer.delivery_days))}</td><td>${t(offer.payment_terms)}</td><td>${offer.is_lowest ? badge(tr("lowestProposal")) : ""} ${offer.decided ? badge(tr("decidedTag")) : ""}</td></tr>`;
                }).join("")}</tbody></table></div>`;
            }).join("");
        } catch (error) { content.innerHTML = `<div class="proc-empty" role="alert">${escape(error.message)}</div>`; }
    }

    async function lookups() {
        try {
            const [suppliers, catalog, orders, matchOrders, options] = await Promise.all([request("suppliers?status=ACTIVE&pageSize=100"), request("catalog?status=ACTIVE&pageSize=100"), request("orders?status=APPROVED&pageSize=100"), request("orders?pageSize=100"), request("receipt-options")]);
            document.querySelectorAll('[data-lookup="suppliers"]').forEach(element => { element.innerHTML = `<option value="">${tr("selectByName")}</option>` + suppliers.map(item => `<option value="${item.id}">${escape(item.legal_name)}</option>`).join(""); });
            document.querySelectorAll('[data-lookup="catalog"]').forEach(fillCatalog);
            document.querySelectorAll('[data-lookup="orders"]').forEach(element => { element.innerHTML = `<option value="">${tr("selectByNumber")}</option>` + orders.map(item => `<option value="${item.id}">${escape(item.number)} · ${escape(item.supplier_name)}</option>`).join(""); });
            document.querySelectorAll('[data-lookup="match-orders"]').forEach(element => { element.innerHTML = `<option value="">${tr("selectByNumber")}</option>` + matchOrders.filter(item => !["DRAFT", "AWAITING_APPROVAL", "CANCELLED"].includes(item.status)).map(item => `<option value="${item.id}">${escape(item.number)} · ${escape(item.supplier_name)}</option>`).join(""); });
            document.querySelectorAll('[data-lookup="warehouses"]').forEach(element => { element.innerHTML = `<option value="">${tr("naService")}</option>` + options.warehouses.map(item => `<option value="${item.id}">${escape(item.code)} · ${escape(item.name)}</option>`).join(""); });
            document.querySelectorAll('[data-lookup="finance-accounts"]').forEach(element => { element.innerHTML = `<option value="">${tr("selectAccount")}</option>` + options.financeAccounts.map(item => `<option value="${item.id}">${escape(item.code)} · ${escape(item.name)}</option>`).join(""); });
            document.querySelectorAll('[data-lookup="products"]').forEach(element => { element.innerHTML = `<option value="">${tr("selectForMaterial")}</option>` + options.products.map(item => `<option value="${item.id}">${escape(item.sku)} · ${escape(item.name)}</option>`).join(""); });
            document.querySelectorAll('[data-lookup="farms"]').forEach(element => { element.innerHTML = `<option value="">${tr("useContext")}</option>` + (options.farms || []).map(item => `<option value="${item.id}">${escape(item.code)} · ${escape(item.name)}</option>`).join(""); });
        } catch { window.toastWarning?.(tr("listsUnavailable"), tr("listsUnavailableSub")); }
    }
    function fillCatalog(element) {
        return request("catalog?status=ACTIVE&pageSize=100").then(catalog => {
            element.innerHTML = `<option value="">${tr("selectByCode")}</option>` + catalog.map(item => `<option value="${item.id}" data-unit="${escape(item.unit)}">${escape(item.internal_code)} · ${escape(item.name)}</option>`).join("");
        }).catch(() => undefined);
    }

    // Recebimento multi-linha: cada linha escolhe um item pendente do pedido; o mesmo item
    // nunca aparece em duas linhas (evita digitação duplicada e confusão de efeitos).
    async function loadPendingItems(orderId) {
        const selects = document.querySelectorAll('#receipt-dialog select[name="purchaseOrderItemId"]');
        if (!orderId) { selects.forEach(select => { select.innerHTML = `<option value="">${tr("selPendingItemPh")}</option>`; }); return; }
        const rows = await request(`orders/${encodeURIComponent(orderId)}/pending-items`);
        const html = rows.length
            ? `<option value="">${tr("selPendingItemPh")}</option>` + rows.map(item => `<option value="${item.id}" data-lot="${item.requires_lot}" data-expiry="${item.requires_expiry}" data-pending="${item.pending_quantity}">${escape(`${item.name} · ${tr("balanceWord")} ${item.pending_quantity} ${item.unit}`)}</option>`).join("")
            : `<option value="">${tr("noPendingBalance")}</option>`;
        selects.forEach(select => { select.innerHTML = html; });
    }

    // Linhas repetíveis de requisição/pedido/recebimento: o mesmo item nunca é digitado duas vezes em campos separados.
    function initLines(container) {
        if (container.querySelector("[data-line]")) return;
        addLine(container);
    }
    function addLine(container) {
        const template = document.querySelector(container.dataset.template);
        const row = template.content.firstElementChild.cloneNode(true);
        container.querySelector("[data-lines-body]").appendChild(row);
        translateNode(row);
        const catalog = row.querySelector("[data-lookup]");
        if (catalog) fillCatalog(catalog);
    }
    // A linha clonada vem de <template> (fora da árvore DOM) e por isso o applyProcI18n não a alcança:
    // traduo aqui na clonagem; a troca de idioma re-aplica no documento inteiro via evento culture.
    function translateNode(root) {
        const source = dict();
        root.querySelectorAll("[data-i18n-pr]").forEach(el => {
            const value = source[el.dataset.i18nPr];
            if (value !== undefined && !value.includes("<") && el.textContent !== value) el.textContent = value;
        });
        root.querySelectorAll("[data-i18n-ph]").forEach(el => {
            const value = source[el.dataset.i18nPh];
            if (value !== undefined) el.placeholder = value;
        });
        root.querySelectorAll("[data-i18n-aria]").forEach(el => {
            const value = source[el.dataset.i18nAria];
            if (value !== undefined) el.setAttribute("aria-label", value);
        });
    }
    function collectLines(container, fields) {
        const rows = [];
        container.querySelectorAll("[data-line]").forEach(row => {
            const values = {}; let filled = false;
            for (const name of fields) {
                const input = row.querySelector(`[name="${name}"]`);
                const raw = (input?.value ?? "").trim();
                if (raw) filled = true;
                values[name] = raw;
            }
            if (filled) rows.push(values);
        });
        return rows;
    }

    function body(form) {
        const values = new FormData(form);
        const result = Object.fromEntries(values);
        for (const name of ["averageDeliveryDays", "installments"]) if (name in result) result[name] = Number(result[name] || 0);
        for (const name of ["minimumStock", "quantity", "unitPrice", "discount", "freight", "taxes"]) if (name in result) result[name] = result[name] === "" ? null : Number(result[name]);
        for (const name of ["requiresLot", "requiresExpiry", "requiresDocument", "requiresInspection", "requiresApprovedSupplier", "overrideExcess", "submit"]) result[name] = values.has(name);
        if (form.dataset.endpoint === "suppliers") Object.assign(result, { stateRegistration: null, address: null, city: null, state: null, mainContact: null, paymentTerms: null, rejectionReason: null, tags: [] });
        if (form.dataset.endpoint === "catalog") Object.assign(result, { active: true, costCenterId: null, description: null, notes: null, relatedProductId: result.relatedProductId || null });
        if (form.dataset.endpoint === "requisitions") {
            const lines = collectLines(form.querySelector("[data-lines]"), ["catalogItemId", "quantity", "unit"]).map(line => ({ catalogItemId: line.catalogItemId, quantity: Number(line.quantity), unit: line.unit, notes: null }));
            Object.assign(result, { costCenterId: result.costCenterId || null, propertyId: result.propertyId || null, items: lines });
        }
        if (form.dataset.endpoint === "orders") {
            const lines = collectLines(form.querySelector("[data-lines]"), ["catalogItemId", "quantity", "unit", "unitPrice", "discount"]).map(line => ({ catalogItemId: line.catalogItemId, quantity: Number(line.quantity), unit: line.unit, unitPrice: Number(line.unitPrice), discount: Number(line.discount || 0), requisitionItemId: null }));
            Object.assign(result, { requisitionId: result.requisitionId || null, quotationId: null, costCenterId: result.costCenterId || null, propertyId: result.propertyId || null, items: lines });
            delete result.freight; result.freight = Number(result.freight || 0); result.taxes = Number(result.taxes || 0);
        }
        if (form.dataset.endpoint === "receipts") {
            const container = form.querySelector('[data-lines][data-template="#receipt-line-template"]');
            const lines = [...container.querySelectorAll("[data-line]")].map(row => {
                const select = row.querySelector('select[name="purchaseOrderItemId"]');
                const quantity = Number(row.querySelector('input[name="quantity"]')?.value || 0);
                if (!select?.value || !(quantity > 0)) return null;
                return { purchaseOrderItemId: select.value, quantity, supplierLot: row.querySelector('input[name="supplierLot"]')?.value?.trim() || null, expiresOn: row.querySelector('input[name="expiresOn"]')?.value || null, notes: null };
            }).filter(Boolean);
            Object.assign(result, { idempotencyKey: stableKey(form), warehouseId: result.warehouseId || null, financeAccountId: result.financeAccountId || null, firstDueOn: result.firstDueOn || null, excessJustification: result.excessJustification || null, items: lines });
            delete result.purchaseOrderItemId; delete result.quantity; delete result.supplierLot; delete result.expiresOn; delete result.notes;
        }
        if (form.dataset.endpoint === "invoice-matches") { const option = form.elements.matchLine.selectedOptions[0], quantity = Number(result.matchQuantity), unitPrice = Number(result.matchUnitPrice), lineDiscount = Number(result.lineDiscount || 0), goodsTotal = Math.round((quantity * unitPrice - lineDiscount) * 100) / 100, discount = Number(result.documentDiscount || 0), freight = Number(result.matchFreight || 0), additionalAmount = Number(result.additionalAmount || 0); Object.assign(result, { documentSeries: result.documentSeries || null, goodsTotal, discount, freight, additionalAmount, total: Math.round((goodsTotal - discount + freight + additionalAmount) * 100) / 100, idempotencyKey: stableKey(form), items: [{ purchaseOrderItemId: option.dataset.orderItem, receiptItemId: option.value, description: result.lineDescription, quantity, unit: option.dataset.unit, unitPrice, discount: lineDiscount }] }); }
        return result;
    }
    function stableKey(form) { if (!idempotencyKeys.has(form)) idempotencyKeys.set(form, crypto.randomUUID()); return idempotencyKeys.get(form); }
    async function loadMatchOptions(orderId) { const element = document.querySelector('[name="matchLine"]'); if (!orderId) { element.innerHTML = `<option value="">${tr("selOrderFirstPh")}</option>`; return; } const rows = await request(`orders/${encodeURIComponent(orderId)}/match-options`); element.innerHTML = rows.length ? `<option value="">${tr("selectMatchLine")}</option>` + rows.map(i => `<option value="${i.receipt_item_id}" data-order-item="${i.purchase_order_item_id}" data-unit="${escape(i.unit)}" data-price="${i.unit_price}" data-quantity="${i.available_quantity}">${tr("matchOption", escape(i.receipt_number), escape(i.name), i.available_quantity, escape(i.unit))}</option>`).join("") : `<option value="">${tr("noAcceptedAvailable")}</option>`; }

    // ── Tolerâncias de conferência: GET/PUT versionado; conflito (409) orienta recarregar ──
    async function openTolerance() {
        const dialog = document.querySelector("#tolerance-dialog"), form = dialog.querySelector("form");
        form.reset(); form.querySelector(".form-message").textContent = "";
        try {
            const data = await request("match-tolerance");
            form.elements.toleranceVersion.value = data.version ?? 0;
            form.elements.quantityPercent.value = data.quantity_percent ?? 0;
            form.elements.quantityAbsolute.value = data.quantity_absolute ?? 0;
            form.elements.pricePercent.value = data.price_percent ?? 0;
            form.elements.priceAbsolute.value = data.price_absolute ?? 0;
            form.elements.totalPercent.value = data.total_percent ?? 0;
            form.elements.totalAbsolute.value = data.total_absolute ?? 0;
            form.elements.excessPercent.value = data.excess_percent ?? 0;
            form.elements.excessAbsolute.value = data.excess_absolute ?? 0;
            form.elements.deliveryDays.value = data.delivery_days ?? 0;
            form.elements.separationOfDuties.checked = Boolean(data.separation_of_duties);
            dialog.showModal();
        } catch (error) { window.toastWarning?.(tr("operationFailed"), error.message); }
    }

    function activateTab(name) {
        const button = document.querySelector(`.proc-tabs button[data-tab="${name}"]`);
        const panel = document.querySelector(`#${name}`);
        if (!button || !panel) return false;
        document.querySelectorAll(".proc-tabs button,.proc-panel").forEach(item => item.classList.remove("active"));
        button.classList.add("active"); panel.classList.add("active");
        return true;
    }
    // Ligação profunda honesta: MyDay/Central usam ?focus=<aba>, e a Central de Trabalho usa
    // parâmetros de registro (purchaseId/receiptId/quotationId/requisitionId/returnId/matchId) com returnTo=/Work.
    function focusFromLocation() {
        const params = new URLSearchParams(location.search);
        const token = (params.get("focus") || location.hash.replace(/^#/, "")).toLowerCase();
        const aliases = { matches: "invoice-matches", matchesconferencia: "invoice-matches", conferencia: "invoice-matches", "invoice-matches": "invoice-matches", quotations: "quotations", quotes: "quotations", requisitions: "requisitions", requests: "requisitions", receipts: "receipts", orders: "orders", suppliers: "suppliers", catalog: "catalog", reports: "reports", approvals: "approvals", "supplier-returns": "supplier-returns", returns: "supplier-returns", devolucoes: "supplier-returns" };
        const tab = aliases[token];
        if (tab) activateTab(tab);
        else if (params.get("action") === "approval") activateTab("approvals");
        const quotationId = params.get("quotationId"); if (quotationId) { activateTab("quotations"); showQuotation(quotationId); }
        const receiptId = params.get("receiptId"); if (receiptId) { activateTab("receipts"); showReceipt(receiptId); }
        const returnId = params.get("returnId"); if (returnId) { activateTab("supplier-returns"); showReturn(returnId); }
        const matchId = params.get("matchId"); if (matchId) { activateTab("invoice-matches"); showMatch(matchId); }
        const requisitionId = params.get("requisitionId"); if (requisitionId) { activateTab("requisitions"); openRequisitionDetail(requisitionId); }
        const purchaseId = params.get("purchaseId") || params.get("orderId");
        if (purchaseId) { activateTab("orders"); pendingOrderHighlight = purchaseId; }
    }
    document.querySelectorAll(".proc-tabs button").forEach(button => button.addEventListener("click", () => activateTab(button.dataset.tab)));
    document.querySelectorAll("[data-dialog]").forEach(button => button.addEventListener("click", () => { const dialog = document.querySelector(`#${button.dataset.dialog}`); dialog.querySelectorAll("[data-lines]").forEach(initLines); dialog.showModal(); }));
    document.querySelectorAll("[data-add-line]").forEach(button => button.addEventListener("click", () => addLine(document.querySelector(button.dataset.addLine))));
    document.addEventListener("click", event => { const remove = event.target.closest("[data-remove-line]"); if (!remove) return; const body = remove.closest("[data-lines-body]"); if (body.children.length > 1) remove.closest("[data-line]").remove(); });
    document.addEventListener("change", event => { const select = event.target; if (!select.matches("[data-lines] select[data-lookup]")) return; const unit = select.selectedOptions[0]?.dataset.unit; const unitInput = select.closest("[data-line]")?.querySelector('input[name="unit"]'); if (unit && unitInput) unitInput.value = unit; });
    // Linha de recebimento: selecionar o item pendente informa o saldo máximo da linha.
    document.querySelector("#receipt-dialog").addEventListener("change", event => {
        const select = event.target.closest('select[name="purchaseOrderItemId"]');
        if (!select) return;
        const option = select.selectedOptions[0];
        const quantityInput = select.closest("[data-line]")?.querySelector('input[name="quantity"]');
        if (quantityInput) quantityInput.max = option?.dataset.pending || "";
    });
    document.querySelectorAll(".proc-filter").forEach(form => form.addEventListener("submit", event => { event.preventDefault(); list(form.dataset.list, form); }));
    document.querySelectorAll(".proc-form").forEach(form => form.addEventListener("submit", async event => {
        event.preventDefault();
        if (!form.reportValidity()) return;
        const message = form.querySelector(".form-message");
        const button = form.querySelector(".proc-primary");
        if (form.dataset.endpoint === "receipts") {
            const lines = collectLines(form.querySelector('[data-lines][data-template="#receipt-line-template"]'), ["purchaseOrderItemId", "quantity"]);
            if (!lines.length) { message.textContent = tr("minOnePendingLine"); return; }
            const ids = lines.map(line => line.purchaseOrderItemId);
            if (new Set(ids).size !== ids.length) { message.textContent = tr("dupPendingItem"); return; }
        }
        if (form.dataset.endpoint === "receipts" && !await window.confirmDialog(tr("confirmReceiveTitle"), tr("confirmReceiveMsg"), tr("confirmReceiveBtn"))) return;
        if (["requisitions", "orders"].includes(form.dataset.endpoint) && !collectLines(form.querySelector("[data-lines]"), ["catalogItemId"]).length) { message.textContent = tr("minOneItem"); return; }
        button.disabled = true; message.textContent = tr("saving");
        try {
            const result = await request(form.dataset.endpoint, { method: "POST", body: JSON.stringify(body(form)) });
            idempotencyKeys.delete(form); form.closest("dialog").close(); form.reset(); message.textContent = "";
            window.toastSuccess?.(tr("operationDone"), tr("operationDoneSub"));
            await Promise.all([dashboard(), list(form.dataset.endpoint), lookups()]);
            // Retorno imediato do registro persistido: abre o detalhe do que acabou de ser gravado.
            const createdId = result?.id;
            if (createdId) {
                if (form.dataset.endpoint === "requisitions") await openRequisitionDetail(createdId);
                else if (form.dataset.endpoint === "receipts") await showReceipt(createdId);
                else if (form.dataset.endpoint === "invoice-matches") await showMatch(createdId);
            }
        } catch (error) { message.textContent = error.message; }
        finally { button.disabled = false; }
    }));
    document.querySelector("#quality-form").addEventListener("submit", async event => { event.preventDefault(); const form = event.currentTarget; if (!form.reportValidity()) return; const values = Object.fromEntries(new FormData(form)); if (!await window.confirmDialog(tr("confirmQualityTitle"), tr("confirmQualityMsg"), tr("registerDecisionBtn"))) return; const button = form.querySelector(".proc-primary"); button.disabled = true; try { await request(`receipt-items/${values.receiptItemId}/quality-decisions`, { method: "POST", body: JSON.stringify({ acceptedQuantity: Number(values.acceptedQuantity), rejectedQuantity: Number(values.rejectedQuantity), result: values.result, reason: values.reason || null, evidenceReference: values.evidenceReference || null, idempotencyKey: stableKey(form) }) }); idempotencyKeys.delete(form); document.querySelector("#quality-dialog").close(); await showReceipt(values.receiptId); window.toastSuccess?.(tr("decisionRegistered"), tr("qualityRegisteredSub")); } catch (error) { form.querySelector(".form-message").textContent = error.message; } finally { button.disabled = false; } });
    document.querySelector("#divergence-form").addEventListener("submit", async event => { event.preventDefault(); const form = event.currentTarget; if (!form.reportValidity()) return; const values = Object.fromEntries(new FormData(form)); if (!await window.confirmDialog(tr("confirmDivergence"), tr("confirmDivergenceMsg", values.decision), tr("confirmDecisionBtn"))) return; const button = form.querySelector(".proc-primary"); button.disabled = true; try { await request(`match-divergences/${encodeURIComponent(values.divergenceId)}/decision`, { method: "POST", body: JSON.stringify({ decision: values.decision, justification: values.justification, evidenceReference: values.evidenceReference || null }) }); document.querySelector("#divergence-dialog").close(); window.toastSuccess?.(tr("decisionRegistered"), tr("divergenceResolvedSub")); await showMatch(values.matchId); await list("invoice-matches"); } catch (error) { form.querySelector(".form-message").textContent = error.message; } finally { button.disabled = false; } });
    document.querySelector("#supplier-return-form").addEventListener("submit", async event => { event.preventDefault(); const form = event.currentTarget; if (!form.reportValidity()) return; const values = Object.fromEntries(new FormData(form)); const items = [...form.querySelectorAll("[data-return-qty]")].map(input => ({ receiptItemId: input.dataset.returnQty, quantity: Number(input.value) })).filter(item => item.quantity > 0); if (!items.length) { form.querySelector(".form-message").textContent = tr("minOneReturnQty"); return; } if (!await window.confirmDialog(tr("confirmReturnTitle"), tr("confirmReturnMsg"), tr("confirmReturnBtn"))) return; const button = form.querySelector("[data-return-submit]") || form.querySelector(".proc-primary"); button.disabled = true; try { const result = await request(`receipts/${values.receiptId}/supplier-returns`, { method: "POST", body: JSON.stringify({ reason: values.reason, items, idempotencyKey: stableKey(form) }) }); idempotencyKeys.delete(form); document.querySelector("#supplier-return-dialog").close(); await Promise.all([list("supplier-returns"), list("receipts"), loadApprovals()]); window.toastSuccess?.(tr("returnRegistered"), tr("returnRegisteredSub")); if (result?.id) await showReturn(result.id); } catch (error) { form.querySelector(".form-message").textContent = error.message; } finally { button.disabled = false; } });
    document.querySelector("#return-decision-form").addEventListener("submit", async event => { event.preventDefault(); const form = event.currentTarget; if (!form.reportValidity()) return; const values = Object.fromEntries(new FormData(form)); const approve = values.decision === "APPROVE"; if (!approve && !(values.reason || "").trim()) { form.querySelector(".form-message").textContent = tr("rejectRequiresReason"); return; } if (!await window.confirmDialog(approve ? tr("approveReturnTitle") : tr("rejectReturnTitle"), approve ? tr("approveReturnMsg") : tr("rejectReturnMsg"), tr("confirmDecisionBtn"))) return; const button = form.querySelector(".proc-primary"); button.disabled = true; try { await request(`supplier-returns/${values.returnId}/decision`, { method: "POST", body: JSON.stringify({ approve, reason: values.reason || null }) }); document.querySelector("#return-decision-dialog").close(); await Promise.all([list("supplier-returns"), list("receipts"), list("orders"), loadApprovals()]); window.toastSuccess?.(approve ? tr("returnApprovedToast") : tr("returnRejectedToast"), approve ? tr("returnApprovedSub") : tr("returnRejectedSub")); } catch (error) { form.querySelector(".form-message").textContent = error.message; } finally { button.disabled = false; } });
    // Decisão com justificativa (rejeição de requisição / cancelamento de pedido): versionada.
    document.querySelector("#decision-reason-form").addEventListener("submit", async event => { event.preventDefault(); const form = event.currentTarget; if (!form.reportValidity()) return; const values = Object.fromEntries(new FormData(form)); const reason = (values.reason || "").trim(); const message = form.querySelector(".form-message"); if (reason.length < 5) { message.textContent = tr("minReasonLen"); return; } const button = form.querySelector(".proc-primary"); button.disabled = true; message.textContent = tr("saving"); try { if (values.decisionKind === "REQ_REJECT") { await request(`requisitions/${encodeURIComponent(values.decisionId)}/decision`, { method: "POST", body: JSON.stringify({ version: Number(values.decisionVersion), decision: "REJECT", reason }) }); window.toastSuccess?.(tr("reqRejected"), tr("reqRejectedSub")); } else { await request(`orders/${encodeURIComponent(values.decisionId)}/cancel`, { method: "POST", body: JSON.stringify({ version: Number(values.decisionVersion), reason }) }); window.toastSuccess?.(tr("orderCancelled"), tr("orderCancelledSub")); } document.querySelector("#decision-reason-dialog").close(); await Promise.all([loadApprovals(), list("requisitions"), list("orders"), dashboard()]); } catch (error) { message.textContent = error.message; } finally { button.disabled = false; } });
    document.querySelector("#quotation-request-form").addEventListener("submit", async event => {
        event.preventDefault();
        const form = event.currentTarget; if (!form.reportValidity()) return;
        const message = form.querySelector(".form-message"), button = form.querySelector(".proc-primary");
        const supplierIds = [...form.elements.supplierIds.selectedOptions].map(option => option.value);
        if (supplierIds.length < 2) { message.textContent = tr("minTwoSuppliers"); return; }
        button.disabled = true; message.textContent = tr("openingQuotation");
        try {
            const result = await request("quotations", { method: "POST", body: JSON.stringify({ requisitionId: form.elements.requisitionId.value, supplierIds, dueDate: form.elements.dueDate.value }) });
            form.closest("dialog").close(); form.reset(); message.textContent = "";
            window.toastSuccess?.(tr("quotationRequested"), tr("quotationRequestedSub"));
            await Promise.all([dashboard(), list("requisitions"), list("quotations")]);
            if (result?.id) await showQuotation(result.id);
        } catch (error) { message.textContent = error.message; } finally { button.disabled = false; }
    });
    document.querySelector("#quotation-cancel-form").addEventListener("submit", async event => {
        event.preventDefault();
        const form = event.currentTarget; if (!form.reportValidity()) return;
        const values = Object.fromEntries(new FormData(form)); const reason = (values.reason || "").trim();
        const message = form.querySelector(".form-message");
        if (reason.length < 5) { message.textContent = tr("minReasonLen"); return; }
        const button = form.querySelector(".proc-primary"); button.disabled = true; message.textContent = tr("saving");
        try {
            await request(`quotations/${encodeURIComponent(values.quotationId)}/cancel`, { method: "POST", body: JSON.stringify({ reason }) });
            form.closest("dialog").close(); form.reset(); message.textContent = "";
            window.toastSuccess?.(tr("cancelledToast"), tr("cancelledToastSub"));
            await Promise.all([showQuotation(values.quotationId), list("quotations"), dashboard()]);
        } catch (error) { message.textContent = error.message; } finally { button.disabled = false; }
    });
    document.querySelector("#tolerance-form").addEventListener("submit", async event => {
        event.preventDefault();
        const form = event.currentTarget; if (!form.reportValidity()) return;
        const values = Object.fromEntries(new FormData(form));
        const message = form.querySelector(".form-message");
        const button = form.querySelector(".proc-primary"); button.disabled = true; message.textContent = tr("saving");
        try {
            await request("match-tolerance", { method: "PUT", body: JSON.stringify({
                quantityPercent: Number(values.quantityPercent), quantityAbsolute: Number(values.quantityAbsolute),
                pricePercent: Number(values.pricePercent), priceAbsolute: Number(values.priceAbsolute),
                totalPercent: Number(values.totalPercent), totalAbsolute: Number(values.totalAbsolute),
                excessPercent: Number(values.excessPercent), excessAbsolute: Number(values.excessAbsolute),
                deliveryDays: Number(values.deliveryDays), separationOfDuties: form.elements.separationOfDuties.checked,
                version: Number(values.toleranceVersion)
            }) });
            form.closest("dialog").close();
            window.toastSuccess?.(tr("toleranceSaved"), tr("toleranceSavedSub"));
        } catch (error) { message.textContent = error.message; } finally { button.disabled = false; }
    });
    document.querySelector("#quote-form").addEventListener("submit", async event => {
        event.preventDefault();
        const form = event.currentTarget; if (!form.reportValidity()) return;
        const message = form.querySelector(".form-message"), button = form.querySelector(".proc-primary");
        const values = Object.fromEntries(new FormData(form));
        const itemOption = form.elements.quotationItem.selectedOptions[0];
        button.disabled = true; message.textContent = tr("registeringProposal");
        try {
            await request(`quotations/${encodeURIComponent(values.quoteQuotationId)}/quote`, { method: "POST", body: JSON.stringify({
                supplierId: values.participant, quotationItemId: itemOption.value, catalogItemId: itemOption.dataset.catalog,
                unitPrice: Number(values.unitPrice), discount: Number(values.discount || 0), deliveryDays: Number(values.deliveryDays),
                paymentTerms: values.paymentTerms || null, proposalValidUntil: values.proposalValidUntil || null,
                freight: values.freight === "" ? null : Number(values.freight), taxes: values.taxes === "" ? null : Number(values.taxes),
                notes: values.notes || null
            }) });
            form.closest("dialog").close(); form.reset(); message.textContent = "";
            window.toastSuccess?.(tr("proposalRegistered"), tr("proposalRegisteredSub"));
            await Promise.all([list("quotations"), dashboard()]);
        } catch (error) { message.textContent = error.message; } finally { button.disabled = false; }
    });
    document.querySelector('[data-lookup="orders"]').addEventListener("change", event => loadPendingItems(event.target.value).catch(error => { document.querySelectorAll('#receipt-dialog select[name="purchaseOrderItemId"]').forEach(select => select.innerHTML = `<option value="">${escape(error.message)}</option>`); }));
    document.querySelector('[data-lookup="match-orders"]').addEventListener("change", event => loadMatchOptions(event.target.value).catch(error => { document.querySelector('[name="matchLine"]').innerHTML = `<option value="">${escape(error.message)}</option>`; }));
    document.querySelector('[name="matchLine"]').addEventListener("change", event => { const option = event.target.selectedOptions[0]; if (!option?.value) return; document.querySelector('[name="matchQuantity"]').value = option.dataset.quantity; document.querySelector('[name="matchUnitPrice"]').value = option.dataset.price; });
    document.querySelector("#match-dialog form").addEventListener("input", event => { const f = event.currentTarget, goods = Number(f.elements.matchQuantity.value || 0) * Number(f.elements.matchUnitPrice.value || 0) - Number(f.elements.lineDiscount.value || 0), total = goods - Number(f.elements.documentDiscount.value || 0) + Number(f.elements.matchFreight.value || 0) + Number(f.elements.additionalAmount.value || 0); f.querySelector("[data-match-total]").textContent = `${tr("calcTotal")}: ${money(total)}`; });
    document.querySelector("#proc-refresh").addEventListener("click", () => Promise.all([dashboard(), loadApprovals(), ...listNames().map(name => list(name))]));
    document.querySelector("#proc-tolerances")?.addEventListener("click", openTolerance);
    window.addEventListener("agro360:culture", () => { applyProcI18n(); dashboard(); loadApprovals(); listNames().forEach(name => list(name)); });
    applyProcI18n();
    dashboard(); listNames().forEach(name => list(name)); loadApprovals(); lookups(); focusFromLocation();
})();
