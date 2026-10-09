(() => {
    "use strict";
    const root = "/api/procurement";
    const idempotencyKeys = new WeakMap();
    // Vocabulário canônico de cotações (mesmo de QuotationService): aberta aceita propostas; conversível gera pedidos.
    const openQuotationStatuses = ["SENT", "PARTIAL", "RESPONDED", "ANALYSIS"];
    const convertibleQuotationStatuses = ["PARTIAL", "RESPONDED", "ANALYSIS", "APPROVED"];
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
    const tables = {
        suppliers: { head: ["Fornecedor", "Categoria", "Prazo", "Status"], row: item => [t(item.legal_name), t(item.main_category), `${Number(item.average_delivery_days || 0)} dias`, badge(item.status)] },
        catalog: { head: ["Código", "Item", "Categoria", "Tipo", "Situação"], row: item => [t(item.internal_code), t(item.name), t(item.category), t(item.item_type), badge(item.active ? "ATIVO" : "INATIVO")] },
        requisitions: { head: ["Número", "Prioridade", "Necessidade", "Itens", "Status", "Cotações"], row: item => {
            const actions = ["APPROVED", "PARTIALLY_FULFILLED"].includes(item.status)
                ? `<button type="button" class="proc-link" data-quote-request="${item.id}">Abrir cotação</button> <button type="button" class="proc-link" data-compare="${item.id}">Comparar</button>`
                : `<button type="button" class="proc-link" data-compare="${item.id}">Comparar</button>`;
            return [t(item.number), badge(item.priority), fmtDate(item.needed_on), t(item.item_count), badge(item.status), actions];
        } },
        quotations: { head: ["Número", "Requisição", "Unidade", "Participantes", "Com proposta", "Pedidos gerados", "Validade", "Status", "Ação"], row: item => {
            const actions = [`<button type="button" class="proc-link" data-quotation="${item.id}">Detalhar</button>`];
            if (openQuotationStatuses.includes(item.status)) actions.push(`<button type="button" class="proc-link" data-quote="${item.id}">Registrar proposta</button>`);
            return [t(item.number), t(item.requisition_number), t(item.operational_unit), t(item.supplier_count), t(item.responded_suppliers), t(item.converted_orders), fmtDate(item.valid_until), badge(item.status), actions.join(" ")];
        } },
        orders: { head: ["Número", "Fornecedor", "Entrega", "Total", "Status"], row: item => [t(item.number), t(item.supplier_name), fmtDate(item.delivery_on), money(item.total), badge(item.status)] },
        "supplier-returns": { head: ["Devolução", "Recebimento", "Pedido", "Fornecedor", "Total", "Status", "Decisão", "Ação"], row: item => [t(item.number), t(item.receipt_number), t(item.order_number), t(item.supplier_name), money(item.total), badge(item.status), item.decided_at ? `${fmtDateTime(item.decided_at)}${item.decision_reason ? ` · ${t(item.decision_reason)}` : ""}` : "Aguardando", `<button type="button" class="proc-link" data-return="${item.id}">Detalhes</button>${item.status === "PENDING_APPROVAL" ? ` <button type="button" class="proc-link" data-return-decision="${item.id}">Decidir</button>` : ""}`] },
        receipts: { head: ["Recebimento", "Pedido", "Fornecedor", "Data", "Itens", "Estoque", "Financeiro", "Status", "Próxima ação"], row: item => [t(item.number), t(item.order_number), t(item.supplier_name), fmtDateTime(item.received_at), t(item.item_count), badge(item.stock_integration_status), badge(item.finance_integration_status), badge(item.status), `<button type="button" class="proc-link" data-receipt="${item.id}">${item.status === "DIVERGENT" ? "Inspecionar" : "Consultar"}</button>`] },
        "invoice-matches": { head: ["Documento", "Pedido", "Fornecedor", "Emissão", "Total", "Diferença", "Situação", "Ação"], row: item => [`${t(item.document_number)}${item.document_series ? ` / ${t(item.document_series)}` : ""}`, t(item.order_number), t(item.supplier_name), fmtDate(item.issued_on), money(item.total), money(item.difference_total), badge(item.status), `<button type="button" class="proc-link" data-match="${item.id}">Ver ${t(item.open_divergences)} pendência(s)</button>`] }
    };

    async function dashboard() {
        const element = document.querySelector("#proc-kpis");
        try {
            const data = await request("dashboard");
            const values = [["Rascunhos", data.requisitions_draft], ["Aguardando aprovação", data.requisitions_awaiting_approval], ["Aprovadas com saldo", data.requisitions_approved_balance], ["Urgentes em fluxo", data.requisitions_urgent], ["Cotações em andamento", data.quotations_running], ["Pedidos para aprovar", data.orders_awaiting_approval], ["Recebimentos divergentes", data.divergent_receipts], ["Fornecedores ativos", data.active_suppliers], ["Fornecedores bloqueados", data.blocked_suppliers], ["Parcialmente recebidos", data.orders_partially_received], ["Comprado no mês", money(data.purchased_month)]];
            element.innerHTML = values.map(item => `<article class="proc-kpi"><strong>${escape(item[1] ?? 0)}</strong><span>${item[0]}</span></article>`).join("");
        } catch (error) { element.innerHTML = `<div class="proc-empty">${escape(error.message)}</div>`; }
    }

    async function list(name, form) {
        const box = document.querySelector(`[data-content="${name}"]`);
        box.innerHTML = '<div class="proc-loading">Carregando…</div>';
        try {
            const query = form ? new URLSearchParams(new FormData(form)) : "";
            const rows = await request(`${name}?${query}`);
            const definition = tables[name];
            box.innerHTML = rows.length ? `<table><thead><tr>${definition.head.map(item => `<th>${item}</th>`).join("")}</tr></thead><tbody>${rows.map(row => `<tr>${definition.row(row).map(value => `<td>${value ?? "—"}</td>`).join("")}</tr>`).join("")}</tbody></table>` : '<div class="proc-empty">Nenhum registro encontrado para os filtros aplicados.</div>';
            box.querySelectorAll("[data-receipt]").forEach(button => button.addEventListener("click", () => showReceipt(button.dataset.receipt)));
            box.querySelectorAll("[data-match]").forEach(button => button.addEventListener("click", () => showMatch(button.dataset.match)));
            box.querySelectorAll("[data-quotation]").forEach(button => button.addEventListener("click", () => showQuotation(button.dataset.quotation)));
            box.querySelectorAll("[data-quote]").forEach(button => button.addEventListener("click", () => openQuoteForm(button.dataset.quote)));
            box.querySelectorAll("[data-compare]").forEach(button => button.addEventListener("click", () => compareRequisition(button.dataset.compare)));
            box.querySelectorAll("[data-quote-request]").forEach(button => button.addEventListener("click", () => openQuotationRequest(button.dataset.quoteRequest)));
            box.querySelectorAll("[data-return]").forEach(button => button.addEventListener("click", () => showReturn(button.dataset.return)));
            box.querySelectorAll("[data-return-decision]").forEach(button => button.addEventListener("click", () => openReturnDecision(button.dataset.returnDecision)));
        } catch (error) { box.innerHTML = `<div class="proc-empty">${escape(error.message)}</div>`; }
    }

    async function showReceipt(id) {
        const dialog = document.querySelector("#receipt-detail-dialog"); const content = dialog.querySelector(".receipt-detail-content");
        content.innerHTML = '<div class="proc-loading">Carregando recebimento…</div>'; if (!dialog.open) dialog.showModal();
        try {
            const data = await request(`receipts/${encodeURIComponent(id)}`), r = data.receipt;
            content.innerHTML = `<section class="receipt-summary"><strong>${t(r.number)}</strong><span>Pedido ${t(r.order_number)}</span><span>${t(r.supplier_name)}</span>${badge(r.status)}</section><h3>Itens, lotes e disponibilidade</h3><div class="proc-table"><table><thead><tr><th>Item / lote</th><th>Pedido</th><th>Recebido</th><th>Bloqueado</th><th>Disponível</th><th>Rejeitado</th><th>Ação</th></tr></thead><tbody>${data.items.map(i => `<tr><td>${t(i.name)}<small>${t(i.supplier_lot || "Sem lote")} · ${t(i.unit)}</small></td><td>${t(i.ordered_quantity)}</td><td>${t(i.quantity)}</td><td>${Math.max(0, Number(i.quarantine_quantity)-Number(i.released_quantity)-Number(i.rejected_quantity))}</td><td>${i.released_quantity || (i.quality_status === "NOT_REQUIRED" ? i.quantity : 0)}</td><td>${t(i.rejected_quantity)}</td><td>${i.quality_status === "PENDING" ? `<button type="button" class="proc-primary" data-quality="${i.id}" data-max="${Number(i.quantity)-Number(i.released_quantity)-Number(i.rejected_quantity)}">Decidir</button>` : escape(i.quality_status)}</td></tr>`).join("")}</tbody></table></div><h3>Histórico de qualidade</h3>${data.history.length ? data.history.map(h => `<p><strong>${t(h.result)}</strong> · aceita ${t(h.accepted_quantity)}, rejeitada ${t(h.rejected_quantity)} · ${t(h.reason || "Sem ressalva")}</p>`).join("") : '<p class="proc-empty">Nenhuma decisão registrada.</p>'}${["PARTIAL", "RECEIVED", "DIVERGENT"].includes(r.status) ? `<p><button type="button" class="proc-primary" data-return-create="${escape(r.id)}">Devolver itens ao fornecedor</button> <small>Apenas o saldo aceito e ainda não devolvido; aprovação por outro usuário com alçada.</small></p>` : ""}`;
            content.querySelectorAll("[data-quality]").forEach(button => button.addEventListener("click", () => openQuality(button.dataset.quality, button.dataset.max, id)));
            content.querySelectorAll("[data-return-create]").forEach(button => button.addEventListener("click", () => openSupplierReturn(button.dataset.returnCreate)));
        } catch (error) { content.innerHTML = `<div class="proc-empty" role="alert">${escape(error.message)}</div>`; }
    }
    function openQuality(itemId, maximum, receiptId) { const form = document.querySelector("#quality-form"); form.reset(); form.elements.receiptItemId.value = itemId; form.elements.receiptId.value = receiptId; form.elements.acceptedQuantity.max = maximum; form.elements.rejectedQuantity.max = maximum; form.querySelector("[data-quality-max]").textContent = `${maximum} unidade(s) aguardando decisão.`; document.querySelector("#quality-dialog").showModal(); }
    // ── Devolução ao fornecedor: registro no recebimento → aprovação de outro usuário → crédito aberto ──
    async function openSupplierReturn(receiptId) {
        const dialog = document.querySelector("#supplier-return-dialog"), form = dialog.querySelector("form"), box = form.querySelector("[data-return-items]");
        form.reset(); form.elements.receiptId.value = receiptId; form.querySelector(".form-message").textContent = "";
        box.innerHTML = '<div class="proc-loading">Carregando itens retornáveis…</div>';
        form.querySelector("[data-return-submit]").disabled = true;
        dialog.showModal();
        try {
            const items = await request(`receipts/${encodeURIComponent(receiptId)}/returnable-items`);
            const eligible = items.map(item => ({ ...item, eligible: Math.max(0, Number(item.accepted_quantity) - Number(item.returned_quantity)) })).filter(item => item.eligible > 0);
            box.innerHTML = eligible.length ? `<div class="proc-table"><table><thead><tr><th>Item</th><th>Aceito</th><th>Já devolvido</th><th>Quantidade a devolver</th></tr></thead><tbody>${eligible.map(item => `<tr><td>${t(item.name)}<small>${t(item.supplier_lot || "Sem lote")} · ${t(item.unit)} · ${money(item.unit_price)}/${t(item.unit)}</small></td><td>${t(item.accepted_quantity)}</td><td>${t(item.returned_quantity)}</td><td><input name="qty-${escape(item.id)}" data-return-qty="${escape(item.id)}" type="number" min="0" max="${item.eligible}" step="0.001" value="0" aria-label="Quantidade devolvida de ${escape(item.name)}"></td></tr>`).join("")}</tbody></table></div>` : '<p class="proc-empty">Nenhum item com saldo aceito e ainda não devolvido neste recebimento.</p>';
            form.querySelector("[data-return-submit]").disabled = !eligible.length;
        } catch (error) { box.innerHTML = `<div class="proc-empty">${escape(error.message)}</div>`; }
    }
    async function showReturn(id) {
        const dialog = document.querySelector("#return-detail-dialog"), content = dialog.querySelector(".return-detail-content");
        content.innerHTML = '<div class="proc-loading">Carregando devolução…</div>'; if (!dialog.open) dialog.showModal();
        try {
            const data = await request(`supplier-returns/${encodeURIComponent(id)}`), h = data.header;
            content.innerHTML = `<section class="receipt-summary"><strong>${t(h.number)}</strong><span>Recebimento ${t(h.receipt_number)}</span><span>Pedido ${t(h.order_number)}</span><span>${t(h.supplier_name)}</span>${badge(h.status)}</section><p><b>Motivo:</b> ${t(h.reason)}</p>${h.decided_at ? `<p><b>Decisão:</b> ${fmtDateTime(h.decided_at)}${h.decision_reason ? ` · ${t(h.decision_reason)}` : ""}</p>` : ""}<h3>Itens devolvidos</h3><div class="proc-table"><table><thead><tr><th>Item</th><th>Quantidade</th><th>Custo unitário</th><th>Total</th></tr></thead><tbody>${data.items.map(i => `<tr><td>${t(i.name)}<small>${t(i.lot_number || "Sem lote")} · ${t(i.item_type)}</small></td><td>${t(i.quantity)} ${t(i.unit)}</td><td>${money(i.unit_cost)}</td><td>${money(Number(i.quantity) * Number(i.unit_cost))}</td></tr>`).join("")}</tbody></table></div><p>${h.credit_number ? `<b>Crédito ${t(h.credit_number)}:</b> ${money(h.credit_amount)} · <span class="proc-badge">${escape(h.credit_status)}</span> — crédito aberto não compensa títulos nem efetua pagamento.` : "Nenhum crédito gerado enquanto a devolução aguarda decisão."}</p>${h.status === "PENDING_APPROVAL" ? `<button type="button" class="proc-primary" data-return-decision-detail="${escape(h.id)}">Decidir devolução</button>` : ""}`;
            content.querySelectorAll("[data-return-decision-detail]").forEach(button => button.addEventListener("click", () => { dialog.close(); openReturnDecision(button.dataset.returnDecisionDetail); }));
        } catch (error) { content.innerHTML = `<div class="proc-empty">${escape(error.message)}</div>`; }
    }
    function openReturnDecision(id) { const form = document.querySelector("#return-decision-form"); form.reset(); form.elements.returnId.value = id; form.querySelector(".form-message").textContent = ""; document.querySelector("#return-decision-dialog").showModal(); }
    async function showMatch(id) {
        const dialog = document.querySelector("#match-detail-dialog"), content = dialog.querySelector(".match-detail-content"); content.innerHTML = '<div class="proc-loading">Carregando conferência…</div>'; if (!dialog.open) dialog.showModal();
        try { const data = await request(`invoice-matches/${encodeURIComponent(id)}`), m = data.match; content.innerHTML = `<section class="receipt-summary"><strong>Documento ${t(m.document_number)}</strong><span>Pedido ${t(m.order_number)}</span><span>${t(m.supplier_name)}</span>${badge(m.status)}</section><p><b>Contratado:</b> ${money(m.contracted_total)} · <b>Cobrado:</b> ${money(m.billed_total)} · <b>Diferença:</b> ${money(m.difference_total)}${m.difference_percent == null ? " (base zero)" : ` (${Number(m.difference_percent).toLocaleString(culture())}%)`}</p><p>Validação fiscal: <b>${t(m.fiscal_validation_status || "NOT_VALIDATED")}</b>. Conferência aprovada não significa pagamento.</p><h3>Linhas vinculadas</h3><div class="proc-table"><table><thead><tr><th>Item</th><th>Recebimento</th><th>Quantidade</th><th>Preço</th><th>Total</th></tr></thead><tbody>${data.lines.map(l => `<tr><td>${t(l.item_name)}</td><td>${t(l.receipt_number)}</td><td>${t(l.quantity)} ${t(l.unit)}</td><td>${money(l.unit_price)}</td><td>${money(l.total)}</td></tr>`).join("")}</tbody></table></div><h3>Divergências</h3>${data.divergences.length ? data.divergences.map(d => `<article class="proc-divergence"><header>${badge(d.status)} <b>${t(d.type)}</b></header><p>${t(d.description)}</p><p><b>Impacto:</b> ${t(d.operational_impact)}<br><b>Ação:</b> ${t(d.required_action)}</p>${d.status === "OPEN" ? `<button type="button" class="proc-primary" data-resolve="${d.id}">Decidir exceção</button>` : `<p><b>Decisão:</b> ${t(d.resolution)} — ${t(d.resolution_reason)}</p>`}</article>`).join("") : '<p class="proc-empty">Nenhuma divergência fora da tolerância registrada.</p>'}`; content.querySelectorAll("[data-resolve]").forEach(b => b.onclick = () => openDivergenceDialog(b.dataset.resolve, id)); } catch (error) { content.innerHTML = `<div class="proc-empty" role="alert">${escape(error.message)}</div>`; }
    }
    function openDivergenceDialog(divergenceId, matchId) {
        const dialog = document.querySelector("#divergence-dialog"), form = dialog.querySelector("form");
        form.reset(); form.elements.divergenceId.value = divergenceId; form.elements.matchId.value = matchId;
        dialog.showModal();
    }

    // ── Jornada de cotações: solicitar → registrar propostas → comparar → decidir (+justificativa) → converter ──
    async function openQuotationRequest(requisitionId) {
        const dialog = document.querySelector("#quotation-request-dialog"), form = dialog.querySelector("form");
        form.reset();
        const dayMs = 86400000; form.elements.dueDate.min = new Date(Date.now() + dayMs).toISOString().slice(0, 10);
        form.elements.dueDate.value = new Date(Date.now() + 8 * dayMs).toISOString().slice(0, 10);
        try {
            const [approved, partial] = await Promise.all([request("requisitions?status=APPROVED&pageSize=100"), request("requisitions?status=PARTIALLY_FULFILLED&pageSize=100")]);
            form.elements.requisitionId.innerHTML = optionList([...approved, ...partial], "id", item => `${t(item.number)} · prioridade ${t(item.priority)} · necessidade ${fmtDate(item.needed_on)}`, "Selecione a requisição aprovada");
            const suppliers = await request("suppliers?status=ACTIVE&pageSize=100");
            form.elements.supplierIds.innerHTML = suppliers.map(item => `<option value="${escape(item.id)}">${t(item.legal_name)}</option>`).join("");
            if (requisitionId) form.elements.requisitionId.value = requisitionId;
            dialog.showModal();
        } catch (error) { window.toastWarning?.("Não foi possível abrir a cotação", error.message); }
    }

    async function openQuoteForm(quotationId) {
        const dialog = document.querySelector("#quote-dialog"), form = dialog.querySelector("form");
        form.reset(); form.elements.quoteQuotationId.value = quotationId;
        try {
            const detail = await request(`quotations/${encodeURIComponent(quotationId)}`);
            const participants = detail.suppliers.filter(item => openQuotationStatuses.includes(item.status));
            if (!participants.length) throw new Error("A cotação não tem participantes aptos a novas propostas.");
            form.elements.participant.innerHTML = optionList(participants, "supplier_id", item => `${t(item.supplier_name)} · ${t(item.status)}`, "Selecione o fornecedor participante");
            form.elements.quotationItem.innerHTML = detail.items.map(item => `<option value="${escape(item.id)}" data-catalog="${escape(item.catalog_item_id)}" data-unit="${escape(item.unit)}">${t(item.catalog_name)} · saldo ${t(item.quantity)} ${t(item.unit)}</option>`).join("");
            form.elements.deliveryDays.max = 365;
            dialog.showModal();
        } catch (error) { window.toastWarning?.("Não foi possível registrar a proposta", error.message); }
    }

    async function showQuotation(id) {
        const dialog = document.querySelector("#quotation-detail-dialog"), content = dialog.querySelector(".quotation-detail-content");
        content.innerHTML = '<div class="proc-loading">Carregando cotação…</div>'; if (!dialog.open) dialog.showModal();
        try {
            const data = await request(`quotations/${encodeURIComponent(id)}`), q = data.quotation;
            const offersByItem = new Map(data.items.map(item => [item.id, data.responses.filter(response => response.quotation_item_id === item.id && response.available)]));
            const supplierName = id2 => (data.suppliers.find(s => s.id === id2) || {}).supplier_name || "—";
            const decidedItem = new Set(data.decisions.map(d => d.quotation_item_id));
            const canDecide = openQuotationStatuses.includes(q.status) && data.responses.some(r => r.available);
            const canConvert = convertibleQuotationStatuses.includes(q.status) && data.decisions.length > 0 && !data.converted_orders.length;
            content.innerHTML = `<section class="receipt-summary"><strong>${t(q.number)}</strong><span>Requisição ${t(q.requisition_number)} (${t(q.requisition_status)})</span><span>Unidade ${t(q.operational_unit)}</span><span>Propostas até ${fmtDate(q.valid_until)}</span>${badge(q.status)}</section>
            <h3>Itens e propostas</h3><div class="proc-table"><table><thead><tr><th>Item</th><th>Saldo</th><th>Menor total</th><th>Fornecedores com proposta</th></tr></thead><tbody>${data.items.map(i => `<tr><td>${t(i.catalog_code)} · ${t(i.catalog_name)}</td><td>${t(i.quantity)} ${t(i.unit)}</td><td>${Number(i.lowest_total) > 0 ? money(i.lowest_total) : "sem proposta"}</td><td>${t(i.quoted_by)}</td></tr>`).join("")}</tbody></table></div>
            <h3>Participantes</h3><div class="proc-table"><table><thead><tr><th>Fornecedor</th><th>Itens cotados</th><th>Total</th><th>Entrega</th><th>Pagamento</th><th>Status</th></tr></thead><tbody>${data.suppliers.map(s => `<tr><td>${t(s.supplier_name)}</td><td>${t(s.item_count)}</td><td>${money(s.grand_total)}</td><td>${s.delivery_days == null ? "—" : `${t(s.delivery_days)} dias`}</td><td>${t(s.payment_terms)}</td><td>${badge(s.status)}</td></tr>`).join("")}</tbody></table></div>
            ${data.decisions.length ? `<h3>Decisões registradas</h3><div class="proc-table"><table><thead><tr><th>Item</th><th>Fornecedor escolhido</th><th>Total escolhido</th><th>Menor total</th><th>Justificativa</th></tr></thead><tbody>${data.decisions.map(d => `<tr><td>${t(d.item_name)}</td><td>${t(d.supplier_name)}</td><td>${money(d.selected_total)}</td><td>${money(d.lowest_total)}</td><td>${t(d.justification)}</td></tr>`).join("")}</tbody></table></div>` : ""}
            ${data.converted_orders.length ? `<h3>Pedidos gerados</h3><div class="proc-table"><table><thead><tr><th>Pedido</th><th>Fornecedor</th><th>Total</th><th>Entrega</th><th>Status</th></tr></thead><tbody>${data.converted_orders.map(o => `<tr><td>${t(o.number)}</td><td>${t(o.supplier_name)}</td><td>${money(o.total)}</td><td>${fmtDate(o.delivery_on)}</td><td>${badge(o.status)}</td></tr>`).join("")}</tbody></table></div>` : ""}
            ${canDecide ? `<form id="decide-form" class="proc-decide"><h3>Escolha dos vencedores por item</h3><p>A seleção padrão é a menor proposta. Escolher uma proposta acima da menor exige justificativa auditável.</p><div class="proc-table"><table><thead><tr><th>Item</th><th>Proposta vencedora</th></tr></thead><tbody>${data.items.filter(i => offersByItem.get(i.id)?.length).map(i => `<tr><td>${t(i.catalog_name)} · ${t(i.quantity)} ${t(i.unit)}${decidedItem.has(i.id) ? " · já decidido" : ""}</td><td><select name="pick-${escape(i.id)}" data-item="${escape(i.id)}" data-lowest="${escape(i.lowest_total)}">${offersByItem.get(i.id).sort((a, b) => Number(a.total) - Number(b.total)).map(r => `<option value="${escape(r.quotation_supplier_id)}" data-total="${escape(r.total)}">${t(supplierName(r.quotation_supplier_id))} — ${money(r.total)}${Number(r.total) === Number(i.lowest_total) ? " · menor" : ""}</option>`).join("")}</select></td></tr>`).join("")}</tbody></table></div><label>Justificativa (obrigatória quando a escolha ficar acima da menor proposta)<textarea name="justification" minlength="3" maxlength="1000"></textarea></label><button class="proc-primary">Registrar decisão</button><span class="form-message" role="alert"></span></form>` : ""}
            ${canConvert ? `<p class="proc-help">A conversão gera um pedido por fornecedor vencedor, reaproveitando a decisão registrada; repetir a conversão devolve os mesmos pedidos sem duplicar.</p><button type="button" id="convert-quotation" class="proc-primary" data-id="${escape(id)}">Converter em pedidos</button>` : ""}`;
            const decideForm = content.querySelector("#decide-form");
            if (decideForm) decideForm.addEventListener("submit", event => submitDecision(event, id));
            const convertButton = content.querySelector("#convert-quotation");
            if (convertButton) convertButton.addEventListener("click", () => convertQuotation(convertButton.dataset.id));
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
        if (!items.length) { message.textContent = "Selecione uma proposta para cada item cotado."; return; }
        if (aboveLowest && justification.length < 3) { message.textContent = "A escolha ficou acima da menor proposta: justifique com pelo menos 3 caracteres."; return; }
        if (!await window.confirmDialog("Confirmar decisão da cotação", "As escolhas ficam registradas e habilitam a conversão em pedidos. Nenhum pedido é criado nesta etapa.", "Decidir")) return;
        try {
            await request(`quotations/${encodeURIComponent(quotationId)}/decide`, { method: "POST", body: JSON.stringify({ items, justification: justification || null }) });
            window.toastSuccess?.("Decisão registrada", "A cotação está pronta para conversão em pedidos.");
            await Promise.all([showQuotation(quotationId), list("quotations"), dashboard()]);
        } catch (error) { message.textContent = error.message; }
    }

    async function convertQuotation(quotationId) {
        if (!await window.confirmDialog("Converter cotação em pedidos", "Será criado um pedido de compra por fornecedor vencedor, vinculado à requisição e às decisões aprovadas. A conversão é uma transação única: falha em qualquer fornecedor não deixa pedido parcial, e repetir devolve os mesmos pedidos (cancelados incluem-se — o reabastecimento vem de novo pedido na requisição).", "Converter")) return;
        const path = `quotations/${encodeURIComponent(quotationId)}/convert`;
        try {
            let result;
            try {
                result = await request(path, { method: "POST" });
            } catch (error) {
                // Menor preço é recomendação: sem decisão explícita a API pede confirmação e nada foi gravado.
                if (error.status !== 409 || error.problem?.code !== "agro360.quotation.confirmation_required") throw error;
                const proceed = await window.confirmDialog("Confirmar recomendação de menor preço", `${error.message} Nenhum pedido foi criado ainda. Deseja confirmar a conversão aplicando o menor preço aos itens restantes?`, "Confirmar menor preço");
                if (!proceed) return;
                result = await request(`${path}?confirmLowestPrice=true`, { method: "POST" });
            }
            window.toastSuccess?.("Cotação convertida", `${result.order_ids.length} pedido(s) vinculado(s) à cotação para aprovação/entrega conforme as decisões.`);
            await Promise.all([showQuotation(quotationId), list("quotations"), list("orders"), dashboard()]);
        } catch (error) { window.toastWarning?.("Não foi possível converter", error.message); }
    }

    async function compareRequisition(requisitionId) {
        const dialog = document.querySelector("#compare-dialog"), content = dialog.querySelector(".compare-content");
        content.innerHTML = '<div class="proc-loading">Comparando cotações…</div>'; if (!dialog.open) dialog.showModal();
        try {
            const rows = await request(`requisitions/${encodeURIComponent(requisitionId)}/quotations/compare`);
            if (!rows.length) { content.innerHTML = '<div class="proc-empty">Esta requisição ainda não possui propostas registradas para comparar.</div>'; return; }
            const groups = new Map();
            rows.forEach(row => { const key = `${row.quotation_item_id}`; if (!groups.has(key)) groups.set(key, []); groups.get(key).push(row); });
            content.innerHTML = [...groups.entries()].map(([key, offers]) => `<h3>${t(offers[0].item)} · ${t(offers[0].quantity)} ${t(offers[0].unit)}</h3><p class="proc-help">Menor total do item: <b>${money(offers[0].lowest_total)}</b>${offers.some(offer => offer.quote_deadline) ? ` · propostas válidas até ${fmtDate(offers[0].quote_deadline)}` : ""}</p><div class="proc-table"><table><thead><tr><th>Cotação</th><th>Fornecedor</th><th>Unitário</th><th>Desconto</th><th>Total</th><th>Entrega</th><th>Pagamento</th><th>Situação</th></tr></thead><tbody>${offers.map(offer => `<tr><td>${t(offer.number)}</td><td>${t(offer.supplier)}</td><td>${money(offer.unit_price)}</td><td>${money(offer.discount)}</td><td><b>${money(offer.total)}</b></td><td>${offer.delivery_days == null ? "—" : `${t(offer.delivery_days)} dias`}</td><td>${t(offer.payment_terms)}</td><td>${offer.is_lowest ? badge("MENOR PROPOSTA") : ""} ${offer.decided ? badge("DECIDIDO") : ""}</td></tr>`).join("")}</tbody></table></div>`).join("");
        } catch (error) { content.innerHTML = `<div class="proc-empty" role="alert">${escape(error.message)}</div>`; }
    }

    async function lookups() {
        try {
            const [suppliers, catalog, orders, matchOrders, options] = await Promise.all([request("suppliers?status=ACTIVE&pageSize=100"), request("catalog?status=ACTIVE&pageSize=100"), request("orders?status=APPROVED&pageSize=100"), request("orders?pageSize=100"), request("receipt-options")]);
            document.querySelectorAll('[data-lookup="suppliers"]').forEach(element => { element.innerHTML = '<option value="">Selecione pelo nome</option>' + suppliers.map(item => `<option value="${item.id}">${escape(item.legal_name)}</option>`).join(""); });
            document.querySelectorAll('[data-lookup="catalog"]').forEach(fillCatalog);
            document.querySelectorAll('[data-lookup="orders"]').forEach(element => { element.innerHTML = '<option value="">Selecione pelo número</option>' + orders.map(item => `<option value="${item.id}">${escape(item.number)} · ${escape(item.supplier_name)}</option>`).join(""); });
            document.querySelectorAll('[data-lookup="match-orders"]').forEach(element => { element.innerHTML = '<option value="">Selecione pelo número</option>' + matchOrders.filter(item => !["DRAFT", "AWAITING_APPROVAL", "CANCELLED"].includes(item.status)).map(item => `<option value="${item.id}">${escape(item.number)} · ${escape(item.supplier_name)}</option>`).join(""); });
            document.querySelectorAll('[data-lookup="warehouses"]').forEach(element => { element.innerHTML = '<option value="">Não aplicável a serviço</option>' + options.warehouses.map(item => `<option value="${item.id}">${escape(item.code)} · ${escape(item.name)}</option>`).join(""); });
            document.querySelectorAll('[data-lookup="finance-accounts"]').forEach(element => { element.innerHTML = '<option value="">Selecione a conta</option>' + options.financeAccounts.map(item => `<option value="${item.id}">${escape(item.code)} · ${escape(item.name)}</option>`).join(""); });
            document.querySelectorAll('[data-lookup="products"]').forEach(element => { element.innerHTML = '<option value="">Selecione para material/ativo</option>' + options.products.map(item => `<option value="${item.id}">${escape(item.sku)} · ${escape(item.name)}</option>`).join(""); });
            document.querySelectorAll('[data-lookup="farms"]').forEach(element => { element.innerHTML = '<option value="">Usar contexto atual</option>' + (options.farms || []).map(item => `<option value="${item.id}">${escape(item.code)} · ${escape(item.name)}</option>`).join(""); });
        } catch { window.toastWarning?.("Listas indisponíveis", "Não foi possível carregar todos os seletores autorizados."); }
    }
    function fillCatalog(element) {
        return request("catalog?status=ACTIVE&pageSize=100").then(catalog => {
            element.innerHTML = '<option value="">Selecione pelo nome ou código</option>' + catalog.map(item => `<option value="${item.id}" data-unit="${escape(item.unit)}">${escape(item.internal_code)} · ${escape(item.name)}</option>`).join("");
        }).catch(() => undefined);
    }

    async function loadPendingItems(orderId) {
        const element = document.querySelector('[name="purchaseOrderItemId"]');
        if (!orderId) { element.innerHTML = '<option value="">Selecione o pedido</option>'; return; }
        const rows = await request(`orders/${encodeURIComponent(orderId)}/pending-items`);
        element.innerHTML = rows.length ? rows.map(item => `<option value="${item.id}" data-lot="${item.requires_lot}" data-expiry="${item.requires_expiry}">${escape(item.name)} · saldo ${escape(item.pending_quantity)} ${escape(item.unit)}</option>`).join("") : '<option value="">Pedido sem saldo pendente</option>';
    }

    // Linhas repetíveis de requisição/pedido: o mesmo item nunca é digitado duas vezes em campos separados.
    function initLines(container) {
        if (container.querySelector("[data-line]")) return;
        addLine(container);
    }
    function addLine(container) {
        const template = document.querySelector(container.dataset.template);
        const row = template.content.firstElementChild.cloneNode(true);
        container.querySelector("[data-lines-body]").appendChild(row);
        const catalog = row.querySelector("[data-lookup]");
        if (catalog) fillCatalog(catalog);
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
        if (form.dataset.endpoint === "receipts") Object.assign(result, { idempotencyKey: stableKey(form), warehouseId: result.warehouseId || null, financeAccountId: result.financeAccountId || null, excessJustification: result.excessJustification || null, items: [{ purchaseOrderItemId: result.purchaseOrderItemId, quantity: result.quantity, supplierLot: result.supplierLot || null, expiresOn: result.expiresOn || null, notes: result.notes || null }] });
        if (form.dataset.endpoint === "invoice-matches") { const option = form.elements.matchLine.selectedOptions[0], quantity = Number(result.matchQuantity), unitPrice = Number(result.matchUnitPrice), lineDiscount = Number(result.lineDiscount || 0), goodsTotal = Math.round((quantity * unitPrice - lineDiscount) * 100) / 100, discount = Number(result.documentDiscount || 0), freight = Number(result.matchFreight || 0), additionalAmount = Number(result.additionalAmount || 0); Object.assign(result, { documentSeries: result.documentSeries || null, goodsTotal, discount, freight, additionalAmount, total: Math.round((goodsTotal - discount + freight + additionalAmount) * 100) / 100, idempotencyKey: stableKey(form), items: [{ purchaseOrderItemId: option.dataset.orderItem, receiptItemId: option.value, description: result.lineDescription, quantity, unit: option.dataset.unit, unitPrice, discount: lineDiscount }] }); }
        return result;
    }
    function stableKey(form) { if (!idempotencyKeys.has(form)) idempotencyKeys.set(form, crypto.randomUUID()); return idempotencyKeys.get(form); }
    async function loadMatchOptions(orderId) { const element = document.querySelector('[name="matchLine"]'); if (!orderId) { element.innerHTML = '<option value="">Selecione primeiro o pedido</option>'; return; } const rows = await request(`orders/${encodeURIComponent(orderId)}/match-options`); element.innerHTML = rows.length ? '<option value="">Selecione a linha conferida</option>' + rows.map(i => `<option value="${i.receipt_item_id}" data-order-item="${i.purchase_order_item_id}" data-unit="${escape(i.unit)}" data-price="${i.unit_price}" data-quantity="${i.available_quantity}">${escape(i.receipt_number)} · ${escape(i.name)} · saldo ${i.available_quantity} ${escape(i.unit)}</option>`).join("") : '<option value="">Sem aceite disponível</option>'; }

    function activateTab(name) {
        const button = document.querySelector(`.proc-tabs button[data-tab="${name}"]`);
        const panel = document.querySelector(`#${name}`);
        if (!button || !panel) return false;
        document.querySelectorAll(".proc-tabs button,.proc-panel").forEach(item => item.classList.remove("active"));
        button.classList.add("active"); panel.classList.add("active");
        return true;
    }
    // Ligação profunda honesta: MyDay e afins usam /Procurement#requisitions|#receipts|#matches ou ?focus=<aba>.
    function focusFromLocation() {
        const token = (new URLSearchParams(location.search).get("focus") || location.hash.replace(/^#/, "")).toLowerCase();
        const aliases = { matches: "invoice-matches", matchesconferencia: "invoice-matches", conferencia: "invoice-matches", "invoice-matches": "invoice-matches", quotations: "quotations", quotes: "quotations", requisitions: "requisitions", requests: "requisitions", receipts: "receipts", orders: "orders", suppliers: "suppliers", catalog: "catalog", reports: "reports", "supplier-returns": "supplier-returns", returns: "supplier-returns", devolucoes: "supplier-returns" };
        const tab = aliases[token];
        if (tab) activateTab(tab);
    }
    document.querySelectorAll(".proc-tabs button").forEach(button => button.addEventListener("click", () => activateTab(button.dataset.tab)));
    document.querySelectorAll("[data-dialog]").forEach(button => button.addEventListener("click", () => { const dialog = document.querySelector(`#${button.dataset.dialog}`); dialog.querySelectorAll("[data-lines]").forEach(initLines); dialog.showModal(); }));
    document.querySelectorAll("[data-add-line]").forEach(button => button.addEventListener("click", () => addLine(document.querySelector(button.dataset.addLine))));
    document.addEventListener("click", event => { const remove = event.target.closest("[data-remove-line]"); if (!remove) return; const body = remove.closest("[data-lines-body]"); if (body.children.length > 1) remove.closest("[data-line]").remove(); });
    document.addEventListener("change", event => { const select = event.target; if (!select.matches("[data-lines] select[data-lookup]")) return; const unit = select.selectedOptions[0]?.dataset.unit; const unitInput = select.closest("[data-line]")?.querySelector('input[name="unit"]'); if (unit && unitInput) unitInput.value = unit; });
    document.querySelectorAll(".proc-filter").forEach(form => form.addEventListener("submit", event => { event.preventDefault(); list(form.dataset.list, form); }));
    document.querySelectorAll(".proc-form").forEach(form => form.addEventListener("submit", async event => {
        event.preventDefault();
        if (!form.reportValidity()) return;
        const message = form.querySelector(".form-message");
        const button = form.querySelector(".proc-primary");
        if (form.dataset.endpoint === "receipts" && !await window.confirmDialog("Confirmar recebimento", "A operação fará a entrada física e criará previsões financeiras abertas. Escritas não são repetidas automaticamente.", "Receber")) return;
        if (["requisitions", "orders"].includes(form.dataset.endpoint) && !collectLines(form.querySelector("[data-lines]"), ["catalogItemId"]).length) { message.textContent = "Informe ao menos um item com catálogo selecionado."; return; }
        button.disabled = true; message.textContent = "Salvando…";
        try {
            await request(form.dataset.endpoint, { method: "POST", body: JSON.stringify(body(form)) });
            idempotencyKeys.delete(form); form.closest("dialog").close(); form.reset(); message.textContent = "";
            window.toastSuccess?.("Operação concluída", "Os vínculos de estoque e financeiro foram persistidos sem duplicação.");
            await Promise.all([dashboard(), list(form.dataset.endpoint), lookups()]);
        } catch (error) { message.textContent = error.message; }
        finally { button.disabled = false; }
    }));
    document.querySelector("#quality-form").addEventListener("submit", async event => { event.preventDefault(); const form = event.currentTarget; if (!form.reportValidity()) return; const values = Object.fromEntries(new FormData(form)); if (!await window.confirmDialog("Confirmar decisão de qualidade", "Somente a quantidade aceita será liberada. A decisão permanecerá no histórico.", "Registrar decisão")) return; const button = form.querySelector(".proc-primary"); button.disabled = true; try { await request(`receipt-items/${values.receiptItemId}/quality-decisions`, { method: "POST", body: JSON.stringify({ acceptedQuantity: Number(values.acceptedQuantity), rejectedQuantity: Number(values.rejectedQuantity), result: values.result, reason: values.reason || null, evidenceReference: values.evidenceReference || null, idempotencyKey: stableKey(form) }) }); idempotencyKeys.delete(form); document.querySelector("#quality-dialog").close(); await showReceipt(values.receiptId); window.toastSuccess?.("Decisão registrada", "A disponibilidade foi atualizada somente para a quantidade aceita."); } catch (error) { form.querySelector(".form-message").textContent = error.message; } finally { button.disabled = false; } });
    document.querySelector("#divergence-form").addEventListener("submit", async event => { event.preventDefault(); const form = event.currentTarget; if (!form.reportValidity()) return; const values = Object.fromEntries(new FormData(form)); if (!await window.confirmDialog("Confirmar decisão", `A divergência será registrada como ${values.decision}. Isso não efetua pagamento.`, "Confirmar")) return; const button = form.querySelector(".proc-primary"); button.disabled = true; try { await request(`match-divergences/${encodeURIComponent(values.divergenceId)}/decision`, { method: "POST", body: JSON.stringify({ decision: values.decision, justification: values.justification, evidenceReference: values.evidenceReference || null }) }); document.querySelector("#divergence-dialog").close(); window.toastSuccess?.("Decisão registrada", "A conferência foi atualizada e o histórico preservado."); await showMatch(values.matchId); await list("invoice-matches"); } catch (error) { form.querySelector(".form-message").textContent = error.message; } finally { button.disabled = false; } });
    document.querySelector("#supplier-return-form").addEventListener("submit", async event => { event.preventDefault(); const form = event.currentTarget; if (!form.reportValidity()) return; const values = Object.fromEntries(new FormData(form)); const items = [...form.querySelectorAll("[data-return-qty]")].map(input => ({ receiptItemId: input.dataset.returnQty, quantity: Number(input.value) })).filter(item => item.quantity > 0); if (!items.length) { form.querySelector(".form-message").textContent = "Informe a quantidade de ao menos um item retornável."; return; } if (!await window.confirmDialog("Confirmar devolução ao fornecedor", "A devolução fica pendente de aprovação por outro usuário com alçada. Nenhum saldo muda antes da aprovação.", "Registrar devolução")) return; const button = form.querySelector(".proc-primary"); button.disabled = true; try { await request(`receipts/${values.receiptId}/supplier-returns`, { method: "POST", body: JSON.stringify({ reason: values.reason, items, idempotencyKey: stableKey(form) }) }); idempotencyKeys.delete(form); document.querySelector("#supplier-return-dialog").close(); await Promise.all([list("supplier-returns"), list("receipts")]); window.toastSuccess?.("Devolução registrada", "Aguardando decisão de um aprovador sem vínculo com o registro."); } catch (error) { form.querySelector(".form-message").textContent = error.message; } finally { button.disabled = false; } });
    document.querySelector("#return-decision-form").addEventListener("submit", async event => { event.preventDefault(); const form = event.currentTarget; if (!form.reportValidity()) return; const values = Object.fromEntries(new FormData(form)); const approve = values.decision === "APPROVE"; if (!approve && !(values.reason || "").trim()) { form.querySelector(".form-message").textContent = "A reprovação exige justificativa."; return; } if (!await window.confirmDialog(approve ? "Aprovar devolução" : "Reprovar devolução", approve ? "O estoque será baixado, o saldo do pedido será reaberto e um crédito aberto será gerado. Nenhuma conta é paga ou compensada automaticamente." : "A devolução será arquivada sem efeitos em estoque, pedido ou financeiro.", "Confirmar decisão")) return; const button = form.querySelector(".proc-primary"); button.disabled = true; try { await request(`supplier-returns/${values.returnId}/decision`, { method: "POST", body: JSON.stringify({ approve, reason: values.reason || null }) }); document.querySelector("#return-decision-dialog").close(); await Promise.all([list("supplier-returns"), list("receipts"), list("orders")]); window.toastSuccess?.(approve ? "Devolução aprovada" : "Devolução reprovada", approve ? "Estoque baixado, saldo do pedido reaberto e crédito aberto registrado." : "Nada mudou em estoque, pedido ou financeiro."); } catch (error) { form.querySelector(".form-message").textContent = error.message; } finally { button.disabled = false; } });
    document.querySelector("#quotation-request-form").addEventListener("submit", async event => {
        event.preventDefault();
        const form = event.currentTarget; if (!form.reportValidity()) return;
        const message = form.querySelector(".form-message"), button = form.querySelector(".proc-primary");
        const supplierIds = [...form.elements.supplierIds.selectedOptions].map(option => option.value);
        if (supplierIds.length < 2) { message.textContent = "Selecione ao menos dois fornecedores participantes."; return; }
        button.disabled = true; message.textContent = "Abrindo cotação…";
        try {
            await request("quotations", { method: "POST", body: JSON.stringify({ requisitionId: form.elements.requisitionId.value, supplierIds, dueDate: form.elements.dueDate.value }) });
            form.closest("dialog").close(); form.reset(); message.textContent = "";
            window.toastSuccess?.("Cotação solicitada", "Participantes registrados; registre as propostas recebidas de cada fornecedor.");
            await Promise.all([dashboard(), list("requisitions"), list("quotations")]);
        } catch (error) { message.textContent = error.message; } finally { button.disabled = false; }
    });
    document.querySelector("#quote-form").addEventListener("submit", async event => {
        event.preventDefault();
        const form = event.currentTarget; if (!form.reportValidity()) return;
        const message = form.querySelector(".form-message"), button = form.querySelector(".proc-primary");
        const values = Object.fromEntries(new FormData(form));
        const itemOption = form.elements.quotationItem.selectedOptions[0];
        button.disabled = true; message.textContent = "Registrando proposta…";
        try {
            await request(`quotations/${encodeURIComponent(values.quoteQuotationId)}/quote`, { method: "POST", body: JSON.stringify({
                supplierId: values.participant, quotationItemId: itemOption.value, catalogItemId: itemOption.dataset.catalog,
                unitPrice: Number(values.unitPrice), discount: Number(values.discount || 0), deliveryDays: Number(values.deliveryDays),
                paymentTerms: values.paymentTerms || null, proposalValidUntil: values.proposalValidUntil || null,
                freight: values.freight === "" ? null : Number(values.freight), taxes: values.taxes === "" ? null : Number(values.taxes),
                notes: values.notes || null
            }) });
            form.closest("dialog").close(); form.reset(); message.textContent = "";
            window.toastSuccess?.("Proposta registrada", "A proposta entra na comparação e conta para a decisão da cotação.");
            await Promise.all([list("quotations"), dashboard()]);
        } catch (error) { message.textContent = error.message; } finally { button.disabled = false; }
    });
    document.querySelector('[data-lookup="orders"]').addEventListener("change", event => loadPendingItems(event.target.value).catch(error => { document.querySelector('[name="purchaseOrderItemId"]').innerHTML = `<option value="">${escape(error.message)}</option>`; }));
    document.querySelector('[data-lookup="match-orders"]').addEventListener("change", event => loadMatchOptions(event.target.value).catch(error => { document.querySelector('[name="matchLine"]').innerHTML = `<option value="">${escape(error.message)}</option>`; }));
    document.querySelector('[name="matchLine"]').addEventListener("change", event => { const option = event.target.selectedOptions[0]; if (!option?.value) return; document.querySelector('[name="matchQuantity"]').value = option.dataset.quantity; document.querySelector('[name="matchUnitPrice"]').value = option.dataset.price; });
    document.querySelector("#match-dialog form").addEventListener("input", event => { const f = event.currentTarget, goods = Number(f.elements.matchQuantity.value || 0) * Number(f.elements.matchUnitPrice.value || 0) - Number(f.elements.lineDiscount.value || 0), total = goods - Number(f.elements.documentDiscount.value || 0) + Number(f.elements.matchFreight.value || 0) + Number(f.elements.additionalAmount.value || 0); f.querySelector("[data-match-total]").textContent = `Total calculado: ${money(total)}`; });
    document.querySelector("#proc-refresh").addEventListener("click", () => Promise.all([dashboard(), ...Object.keys(tables).map(name => list(name))]));
    window.addEventListener("agro360:culture", () => { dashboard(); Object.keys(tables).forEach(name => list(name)); });
    dashboard(); Object.keys(tables).forEach(name => list(name)); lookups(); focusFromLocation();
})();
