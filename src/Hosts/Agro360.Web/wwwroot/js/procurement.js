(() => {
    "use strict";
    const root = "/api/procurement";
    const escape = value => String(value ?? "").replace(/[&<>"']/g, char => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[char]));
    const request = (path, options = {}) => window.agro360Api(`${root}/${path}`, options);
    const badge = value => `<span class="proc-badge">${escape(value)}</span>`;
    const tables = {
        suppliers: { head: ["Fornecedor", "Categoria", "Prazo", "Status"], row: item => [item.legal_name, item.main_category, `${item.average_delivery_days} dias`, badge(item.status)] },
        catalog: { head: ["Código", "Item", "Categoria", "Tipo", "Situação"], row: item => [item.internal_code, item.name, item.category, item.item_type, badge(item.active ? "ATIVO" : "INATIVO")] },
        requisitions: { head: ["Número", "Prioridade", "Necessidade", "Itens", "Status"], row: item => [item.number, badge(item.priority), new Date(`${item.needed_on}T00:00`).toLocaleDateString("pt-BR"), item.item_count, badge(item.status)] },
        orders: { head: ["Número", "Fornecedor", "Entrega", "Total", "Status"], row: item => [item.number, item.supplier_name, new Date(`${item.delivery_on}T00:00`).toLocaleDateString("pt-BR"), Number(item.total).toLocaleString("pt-BR", { style: "currency", currency: "BRL" }), badge(item.status)] },
        receipts: { head: ["Recebimento", "Pedido", "Fornecedor", "Data", "Itens", "Estoque", "Financeiro", "Status", "Próxima ação"], row: item => [item.number, item.order_number, item.supplier_name, new Date(item.received_at).toLocaleString("pt-BR"), item.item_count, badge(item.stock_integration_status), badge(item.finance_integration_status), badge(item.status), `<button type="button" class="proc-link" data-receipt="${item.id}">${item.status === "DIVERGENT" ? "Inspecionar" : "Consultar"}</button>`] },
        "invoice-matches": { head: ["Documento", "Pedido", "Fornecedor", "Emissão", "Total", "Diferença", "Situação", "Ação"], row: item => [`${escape(item.document_number)}${item.document_series ? ` / ${escape(item.document_series)}` : ""}`, item.order_number, item.supplier_name, new Date(`${item.issued_on}T00:00`).toLocaleDateString("pt-BR"), money(item.total), money(item.difference_total), badge(item.status), `<button type="button" class="proc-link" data-match="${item.id}">Ver ${item.open_divergences} pendência(s)</button>`] }
    };
    const money = value => Number(value || 0).toLocaleString("pt-BR", { style: "currency", currency: "BRL" });

    async function dashboard() {
        const element = document.querySelector("#proc-kpis");
        try {
            const data = await request("dashboard");
            const values = [["Requisições abertas", data.requisitions_open], ["Urgentes", data.requisitions_urgent], ["Cotações em andamento", data.quotations_running], ["Pedidos para aprovar", data.orders_awaiting_approval], ["Recebimentos divergentes", data.divergent_receipts], ["Fornecedores ativos", data.active_suppliers], ["Fornecedores bloqueados", data.blocked_suppliers], ["Parcialmente recebidos", data.orders_partially_received], ["Comprado no mês", Number(data.purchased_month).toLocaleString("pt-BR", { style: "currency", currency: "BRL" })]];
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
        } catch (error) { box.innerHTML = `<div class="proc-empty">${escape(error.message)}</div>`; }
    }

    async function showReceipt(id) {
        const dialog = document.querySelector("#receipt-detail-dialog"); const content = dialog.querySelector(".receipt-detail-content");
        content.innerHTML = '<div class="proc-loading">Carregando recebimento…</div>'; if (!dialog.open) dialog.showModal();
        try {
            const data = await request(`receipts/${encodeURIComponent(id)}`), r = data.receipt;
            content.innerHTML = `<section class="receipt-summary"><strong>${escape(r.number)}</strong><span>Pedido ${escape(r.order_number)}</span><span>${escape(r.supplier_name)}</span>${badge(r.status)}</section><h3>Itens, lotes e disponibilidade</h3><div class="proc-table"><table><thead><tr><th>Item / lote</th><th>Pedido</th><th>Recebido</th><th>Bloqueado</th><th>Disponível</th><th>Rejeitado</th><th>Ação</th></tr></thead><tbody>${data.items.map(i => `<tr><td>${escape(i.name)}<small>${escape(i.supplier_lot || "Sem lote")} · ${escape(i.unit)}</small></td><td>${i.ordered_quantity}</td><td>${i.quantity}</td><td>${Math.max(0, Number(i.quarantine_quantity)-Number(i.released_quantity)-Number(i.rejected_quantity))}</td><td>${i.released_quantity || (i.quality_status === "NOT_REQUIRED" ? i.quantity : 0)}</td><td>${i.rejected_quantity}</td><td>${i.quality_status === "PENDING" ? `<button type="button" class="proc-primary" data-quality="${i.id}" data-max="${Number(i.quantity)-Number(i.released_quantity)-Number(i.rejected_quantity)}">Decidir</button>` : escape(i.quality_status)}</td></tr>`).join("")}</tbody></table></div><h3>Histórico de qualidade</h3>${data.history.length ? data.history.map(h => `<p><strong>${escape(h.result)}</strong> · aceita ${h.accepted_quantity}, rejeitada ${h.rejected_quantity} · ${escape(h.reason || "Sem ressalva")}</p>`).join("") : '<p class="proc-empty">Nenhuma decisão registrada.</p>'}`;
            content.querySelectorAll("[data-quality]").forEach(button => button.addEventListener("click", () => openQuality(button.dataset.quality, button.dataset.max, id)));
        } catch (error) { content.innerHTML = `<div class="proc-empty" role="alert">${escape(error.message)}</div>`; }
    }
    function openQuality(itemId, maximum, receiptId) { const form = document.querySelector("#quality-form"); form.reset(); form.elements.receiptItemId.value = itemId; form.elements.receiptId.value = receiptId; form.elements.acceptedQuantity.max = maximum; form.elements.rejectedQuantity.max = maximum; form.querySelector("[data-quality-max]").textContent = `${maximum} unidade(s) aguardando decisão.`; document.querySelector("#quality-dialog").showModal(); }
    async function showMatch(id) {
        const dialog = document.querySelector("#match-detail-dialog"), content = dialog.querySelector(".match-detail-content"); content.innerHTML = '<div class="proc-loading">Carregando conferência…</div>'; if (!dialog.open) dialog.showModal();
        try { const data = await request(`invoice-matches/${encodeURIComponent(id)}`), m = data.match; content.innerHTML = `<section class="receipt-summary"><strong>Documento ${escape(m.document_number)}</strong><span>Pedido ${escape(m.order_number)}</span><span>${escape(m.supplier_name)}</span>${badge(m.status)}</section><p><b>Contratado:</b> ${money(m.contracted_total)} · <b>Cobrado:</b> ${money(m.billed_total)} · <b>Diferença:</b> ${money(m.difference_total)}${m.difference_percent == null ? " (base zero)" : ` (${Number(m.difference_percent).toLocaleString("pt-BR")}%)`}</p><p>Validação fiscal: <b>${escape(m.fiscal_validation_status || "NOT_VALIDATED")}</b>. Conferência aprovada não significa pagamento.</p><h3>Linhas vinculadas</h3><div class="proc-table"><table><thead><tr><th>Item</th><th>Recebimento</th><th>Quantidade</th><th>Preço</th><th>Total</th></tr></thead><tbody>${data.lines.map(l => `<tr><td>${escape(l.item_name)}</td><td>${escape(l.receipt_number)}</td><td>${l.quantity} ${escape(l.unit)}</td><td>${money(l.unit_price)}</td><td>${money(l.total)}</td></tr>`).join("")}</tbody></table></div><h3>Divergências</h3>${data.divergences.length ? data.divergences.map(d => `<article class="proc-divergence"><header>${badge(d.status)} <b>${escape(d.type)}</b></header><p>${escape(d.description)}</p><p><b>Impacto:</b> ${escape(d.operational_impact)}<br><b>Ação:</b> ${escape(d.required_action)}</p>${d.status === "OPEN" ? `<button type="button" class="proc-primary" data-resolve="${d.id}">Decidir exceção</button>` : `<p><b>Decisão:</b> ${escape(d.resolution)} — ${escape(d.resolution_reason)}</p>`}</article>`).join("") : '<p class="proc-empty">Nenhuma divergência fora da tolerância registrada.</p>'}`; content.querySelectorAll("[data-resolve]").forEach(b => b.onclick = () => resolveDivergence(b.dataset.resolve, id)); } catch (error) { content.innerHTML = `<div class="proc-empty" role="alert">${escape(error.message)}</div>`; }
    }
    async function resolveDivergence(id, matchId) { const decision = prompt("Decisão: APPROVED_EXCEPTION, CORRECTION_REQUESTED ou REJECTED"); if (!decision) return; const justification = prompt("Justificativa obrigatória (será auditada):"); if (!justification) return; if (!await window.confirmDialog("Confirmar decisão", `A divergência será registrada como ${decision}. Isso não efetua pagamento.`, "Confirmar")) return; try { await request(`match-divergences/${encodeURIComponent(id)}/decision`, { method: "POST", body: JSON.stringify({ decision, justification, evidenceReference: null }) }); window.toastSuccess?.("Decisão registrada", "A conferência foi atualizada e o histórico preservado."); await showMatch(matchId); await list("invoice-matches"); } catch (error) { window.toastWarning?.("Não foi possível decidir", error.message); } }

    async function lookups() {
        try {
            const [suppliers, catalog, orders, matchOrders, options] = await Promise.all([request("suppliers?status=ACTIVE&pageSize=100"), request("catalog?status=ACTIVE&pageSize=100"), request("orders?status=APPROVED&pageSize=100"), request("orders?pageSize=100"), request("receipt-options")]);
            document.querySelectorAll('[data-lookup="suppliers"]').forEach(element => { element.innerHTML = '<option value="">Selecione pelo nome</option>' + suppliers.map(item => `<option value="${item.id}">${escape(item.legal_name)}</option>`).join(""); });
            document.querySelectorAll('[data-lookup="catalog"]').forEach(element => { element.innerHTML = '<option value="">Selecione pelo nome ou código</option>' + catalog.map(item => `<option value="${item.id}">${escape(item.internal_code)} · ${escape(item.name)}</option>`).join(""); });
            document.querySelectorAll('[data-lookup="orders"]').forEach(element => { element.innerHTML = '<option value="">Selecione pelo número</option>' + orders.map(item => `<option value="${item.id}">${escape(item.number)} · ${escape(item.supplier_name)}</option>`).join(""); });
            document.querySelectorAll('[data-lookup="match-orders"]').forEach(element => { element.innerHTML = '<option value="">Selecione pelo número</option>' + matchOrders.filter(item => !["DRAFT", "AWAITING_APPROVAL", "CANCELLED"].includes(item.status)).map(item => `<option value="${item.id}">${escape(item.number)} · ${escape(item.supplier_name)}</option>`).join(""); });
            document.querySelectorAll('[data-lookup="warehouses"]').forEach(element => { element.innerHTML = '<option value="">Não aplicável a serviço</option>' + options.warehouses.map(item => `<option value="${item.id}">${escape(item.code)} · ${escape(item.name)}</option>`).join(""); });
            document.querySelectorAll('[data-lookup="finance-accounts"]').forEach(element => { element.innerHTML = '<option value="">Selecione a conta</option>' + options.financeAccounts.map(item => `<option value="${item.id}">${escape(item.code)} · ${escape(item.name)}</option>`).join(""); });
            document.querySelectorAll('[data-lookup="products"]').forEach(element => { element.innerHTML = '<option value="">Selecione para material/ativo</option>' + options.products.map(item => `<option value="${item.id}">${escape(item.sku)} · ${escape(item.name)}</option>`).join(""); });
        } catch { window.toastWarning?.("Listas indisponíveis", "Não foi possível carregar todos os seletores autorizados."); }
    }

    async function loadPendingItems(orderId) {
        const element = document.querySelector('[name="purchaseOrderItemId"]');
        if (!orderId) { element.innerHTML = '<option value="">Selecione o pedido</option>'; return; }
        const rows = await request(`orders/${encodeURIComponent(orderId)}/pending-items`);
        element.innerHTML = rows.length ? rows.map(item => `<option value="${item.id}" data-lot="${item.requires_lot}" data-expiry="${item.requires_expiry}">${escape(item.name)} · saldo ${escape(item.pending_quantity)} ${escape(item.unit)}</option>`).join("") : '<option value="">Pedido sem saldo pendente</option>';
    }

    function body(form) {
        const values = new FormData(form);
        const result = Object.fromEntries(values);
        for (const name of ["averageDeliveryDays", "installments"]) if (name in result) result[name] = Number(result[name] || 0);
        for (const name of ["minimumStock", "quantity", "unitPrice", "discount", "freight", "taxes"]) if (name in result) result[name] = result[name] === "" ? null : Number(result[name]);
        for (const name of ["requiresLot", "requiresExpiry", "requiresDocument", "requiresInspection", "requiresApprovedSupplier", "overrideExcess", "submit"]) result[name] = values.has(name);
        if (form.dataset.endpoint === "suppliers") Object.assign(result, { stateRegistration: null, address: null, city: null, state: null, mainContact: null, paymentTerms: null, rejectionReason: null, tags: [] });
        if (form.dataset.endpoint === "catalog") Object.assign(result, { active: true, costCenterId: null, description: null, notes: null, relatedProductId: result.relatedProductId || null });
        if (form.dataset.endpoint === "requisitions") Object.assign(result, { costCenterId: null, propertyId: null, items: [{ catalogItemId: result.catalogItemId, quantity: result.quantity, unit: result.unit, notes: null }] });
        if (form.dataset.endpoint === "orders") Object.assign(result, { requisitionId: null, quotationId: null, costCenterId: null, propertyId: null, items: [{ catalogItemId: result.catalogItemId, quantity: result.quantity, unit: result.unit, unitPrice: result.unitPrice, discount: result.discount }] });
        if (form.dataset.endpoint === "receipts") Object.assign(result, { idempotencyKey: crypto.randomUUID(), warehouseId: result.warehouseId || null, financeAccountId: result.financeAccountId || null, excessJustification: result.excessJustification || null, items: [{ purchaseOrderItemId: result.purchaseOrderItemId, quantity: result.quantity, supplierLot: result.supplierLot || null, expiresOn: result.expiresOn || null, notes: result.notes || null }] });
        if (form.dataset.endpoint === "invoice-matches") { const option = form.elements.matchLine.selectedOptions[0], quantity = Number(result.matchQuantity), unitPrice = Number(result.matchUnitPrice), lineDiscount = Number(result.lineDiscount || 0), goodsTotal = Math.round((quantity * unitPrice - lineDiscount) * 100) / 100, discount = Number(result.documentDiscount || 0), freight = Number(result.matchFreight || 0), additionalAmount = Number(result.additionalAmount || 0); Object.assign(result, { documentSeries: result.documentSeries || null, goodsTotal, discount, freight, additionalAmount, total: Math.round((goodsTotal - discount + freight + additionalAmount) * 100) / 100, idempotencyKey: crypto.randomUUID(), items: [{ purchaseOrderItemId: option.dataset.orderItem, receiptItemId: option.value, description: result.lineDescription, quantity, unit: option.dataset.unit, unitPrice, discount: lineDiscount }] }); }
        return result;
    }
    async function loadMatchOptions(orderId) { const element = document.querySelector('[name="matchLine"]'); if (!orderId) { element.innerHTML = '<option value="">Selecione primeiro o pedido</option>'; return; } const rows = await request(`orders/${encodeURIComponent(orderId)}/match-options`); element.innerHTML = rows.length ? '<option value="">Selecione a linha conferida</option>' + rows.map(i => `<option value="${i.receipt_item_id}" data-order-item="${i.purchase_order_item_id}" data-unit="${escape(i.unit)}" data-price="${i.unit_price}" data-quantity="${i.available_quantity}">${escape(i.receipt_number)} · ${escape(i.name)} · saldo ${i.available_quantity} ${escape(i.unit)}</option>`).join("") : '<option value="">Sem aceite disponível</option>'; }

    document.querySelectorAll(".proc-tabs button").forEach(button => button.addEventListener("click", () => { document.querySelectorAll(".proc-tabs button,.proc-panel").forEach(item => item.classList.remove("active")); button.classList.add("active"); document.querySelector(`#${button.dataset.tab}`).classList.add("active"); }));
    document.querySelectorAll("[data-dialog]").forEach(button => button.addEventListener("click", () => document.querySelector(`#${button.dataset.dialog}`).showModal()));
    document.querySelectorAll(".proc-filter").forEach(form => form.addEventListener("submit", event => { event.preventDefault(); list(form.dataset.list, form); }));
    document.querySelectorAll(".proc-form").forEach(form => form.addEventListener("submit", async event => {
        event.preventDefault();
        if (!form.reportValidity()) return;
        const message = form.querySelector(".form-message");
        const button = form.querySelector(".proc-primary");
        if (form.dataset.endpoint === "receipts" && !await window.confirmDialog("Confirmar recebimento", "A operação fará a entrada física e criará previsões financeiras abertas. Escritas não são repetidas automaticamente.", "Receber")) return;
        button.disabled = true; message.textContent = "Salvando…";
        try {
            await request(form.dataset.endpoint, { method: "POST", body: JSON.stringify(body(form)) });
            form.closest("dialog").close(); form.reset(); message.textContent = "";
            window.toastSuccess?.("Operação concluída", "Os vínculos de estoque e financeiro foram persistidos sem duplicação.");
            await Promise.all([dashboard(), list(form.dataset.endpoint), lookups()]);
        } catch (error) { message.textContent = error.message; }
        finally { button.disabled = false; }
    }));
    document.querySelector("#quality-form").addEventListener("submit", async event => { event.preventDefault(); const form = event.currentTarget; if (!form.reportValidity()) return; const values = Object.fromEntries(new FormData(form)); if (!await window.confirmDialog("Confirmar decisão de qualidade", "Somente a quantidade aceita será liberada. A decisão permanecerá no histórico.", "Registrar decisão")) return; const button = form.querySelector(".proc-primary"); button.disabled = true; try { await request(`receipt-items/${values.receiptItemId}/quality-decisions`, { method: "POST", body: JSON.stringify({ acceptedQuantity: Number(values.acceptedQuantity), rejectedQuantity: Number(values.rejectedQuantity), result: values.result, reason: values.reason || null, evidenceReference: values.evidenceReference || null, idempotencyKey: crypto.randomUUID() }) }); document.querySelector("#quality-dialog").close(); await showReceipt(values.receiptId); window.toastSuccess?.("Decisão registrada", "A disponibilidade foi atualizada somente para a quantidade aceita."); } catch (error) { form.querySelector(".form-message").textContent = error.message; } finally { button.disabled = false; } });
    document.querySelector('[data-lookup="orders"]').addEventListener("change", event => loadPendingItems(event.target.value).catch(error => { document.querySelector('[name="purchaseOrderItemId"]').innerHTML = `<option value="">${escape(error.message)}</option>`; }));
    document.querySelector('[data-lookup="match-orders"]').addEventListener("change", event => loadMatchOptions(event.target.value).catch(error => { document.querySelector('[name="matchLine"]').innerHTML = `<option value="">${escape(error.message)}</option>`; }));
    document.querySelector('[name="matchLine"]').addEventListener("change", event => { const option = event.target.selectedOptions[0]; if (!option?.value) return; document.querySelector('[name="matchQuantity"]').value = option.dataset.quantity; document.querySelector('[name="matchUnitPrice"]').value = option.dataset.price; });
    document.querySelector("#match-dialog form").addEventListener("input", event => { const f = event.currentTarget, goods = Number(f.elements.matchQuantity.value || 0) * Number(f.elements.matchUnitPrice.value || 0) - Number(f.elements.lineDiscount.value || 0), total = goods - Number(f.elements.documentDiscount.value || 0) + Number(f.elements.matchFreight.value || 0) + Number(f.elements.additionalAmount.value || 0); f.querySelector("[data-match-total]").textContent = `Total calculado: ${money(total)}`; });
    document.querySelector("#proc-refresh").addEventListener("click", () => Promise.all([dashboard(), ...Object.keys(tables).map(name => list(name))]));
    dashboard(); Object.keys(tables).forEach(name => list(name)); lookups();
})();
