(() => {
    "use strict";
    const root = "/api/procurement";
    const escape = value => String(value ?? "").replace(/[&<>"']/g, char => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[char]));
    const request = (path, options = {}) => window.agro360Api(`${root}/${path}`, options);
    const badge = value => `<span class="proc-badge">${escape(value)}</span>`;
    const normalizePermission = value => String(value ?? "").toLowerCase().replace(/^agro360\./, "").replaceAll("_", ".");
    const session = (() => { try { return JSON.parse(localStorage.getItem("agro360.session")); } catch { return null; } })();
    const permissions = new Set((session?.permissions ?? []).map(normalizePermission));
    const isSuperAdministrator = (session?.roles ?? []).includes("SUPER_ADMIN");
    const can = permission => isSuperAdministrator || permissions.has(permission);
    const receiptActions = item => {
        const actions = [];
        if (item.quality_pending && can("purchasing.receipts.inspect")) {
            actions.push(`<button type="button" class="proc-link" data-receipt-quality="approve" data-receipt-id="${escape(item.id)}">Liberar</button>`);
            actions.push(`<button type="button" class="proc-link" data-receipt-quality="reject" data-receipt-id="${escape(item.id)}">Reprovar</button>`);
        }
        if (item.status !== "CANCELLED" && can("purchasing.receipts.cancel")) actions.push(`<button type="button" class="proc-link danger" data-receipt-cancel data-receipt-id="${escape(item.id)}">Cancelar</button>`);
        return actions.join(" ") || "—";
    };
    const tables = {
        suppliers: { head: ["Fornecedor", "Categoria", "Prazo", "Status"], row: item => [item.legal_name, item.main_category, `${item.average_delivery_days} dias`, badge(item.status)] },
        catalog: { head: ["Código", "Item", "Categoria", "Tipo", "Situação"], row: item => [item.internal_code, item.name, item.category, item.item_type, badge(item.active ? "ATIVO" : "INATIVO")] },
        requisitions: { head: ["Número", "Prioridade", "Necessidade", "Itens", "Status"], row: item => [item.number, badge(item.priority), new Date(`${item.needed_on}T00:00`).toLocaleDateString("pt-BR"), item.item_count, badge(item.status)] },
        orders: { head: ["Número", "Fornecedor", "Entrega", "Total", "Status"], row: item => [item.number, item.supplier_name, new Date(`${item.delivery_on}T00:00`).toLocaleDateString("pt-BR"), Number(item.total).toLocaleString("pt-BR", { style: "currency", currency: "BRL" }), badge(item.status)] },
        receipts: { head: ["Recebimento", "Pedido", "Fornecedor", "Data", "Itens", "Estoque", "Financeiro", "Status", "Ações"], row: item => [item.number, item.order_number, item.supplier_name, new Date(item.received_at).toLocaleString("pt-BR"), item.item_count, badge(item.stock_integration_status), badge(item.finance_integration_status), badge(item.status), receiptActions(item)] }
    };

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
        } catch (error) { box.innerHTML = `<div class="proc-empty">${escape(error.message)}</div>`; }
    }

    async function lookups() {
        try {
            const [suppliers, catalog, orders, options] = await Promise.all([request("suppliers?status=ACTIVE&pageSize=100"), request("catalog?status=ACTIVE&pageSize=100"), request("orders?status=APPROVED&pageSize=100"), request("receipt-options")]);
            document.querySelectorAll('[data-lookup="suppliers"]').forEach(element => { element.innerHTML = '<option value="">Selecione pelo nome</option>' + suppliers.map(item => `<option value="${item.id}">${escape(item.legal_name)}</option>`).join(""); });
            document.querySelectorAll('[data-lookup="catalog"]').forEach(element => { element.innerHTML = '<option value="">Selecione pelo nome ou código</option>' + catalog.map(item => `<option value="${item.id}">${escape(item.internal_code)} · ${escape(item.name)}</option>`).join(""); });
            document.querySelectorAll('[data-lookup="orders"]').forEach(element => { element.innerHTML = '<option value="">Selecione pelo número</option>' + orders.map(item => `<option value="${item.id}">${escape(item.number)} · ${escape(item.supplier_name)}</option>`).join(""); });
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
        for (const name of ["requiresLot", "requiresExpiry", "requiresDocument", "requiresInspection", "requiresApprovedSupplier", "overrideExcess"]) result[name] = values.has(name);
        if (form.dataset.endpoint === "suppliers") Object.assign(result, { stateRegistration: null, address: null, city: null, state: null, mainContact: null, paymentTerms: null, rejectionReason: null, tags: [] });
        if (form.dataset.endpoint === "catalog") Object.assign(result, { active: true, costCenterId: null, description: null, notes: null, relatedProductId: result.relatedProductId || null });
        if (form.dataset.endpoint === "requisitions") Object.assign(result, { costCenterId: null, propertyId: null, items: [{ catalogItemId: result.catalogItemId, quantity: result.quantity, unit: result.unit, notes: null }] });
        if (form.dataset.endpoint === "orders") Object.assign(result, { requisitionId: null, quotationId: null, costCenterId: null, propertyId: null, items: [{ catalogItemId: result.catalogItemId, quantity: result.quantity, unit: result.unit, unitPrice: result.unitPrice, discount: result.discount }] });
        if (form.dataset.endpoint === "receipts") Object.assign(result, { idempotencyKey: crypto.randomUUID(), warehouseId: result.warehouseId || null, financeAccountId: result.financeAccountId || null, excessJustification: result.excessJustification || null, items: [{ purchaseOrderItemId: result.purchaseOrderItemId, quantity: result.quantity, supplierLot: result.supplierLot || null, expiresOn: result.expiresOn || null, notes: result.notes || null }] });
        return result;
    }

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
    document.querySelector('[data-lookup="orders"]').addEventListener("change", event => loadPendingItems(event.target.value).catch(error => { document.querySelector('[name="purchaseOrderItemId"]').innerHTML = `<option value="">${escape(error.message)}</option>`; }));
    document.querySelector('[data-content="receipts"]').addEventListener("click", async event => {
        const qualityButton = event.target.closest("[data-receipt-quality]");
        const cancelButton = event.target.closest("[data-receipt-cancel]");
        const button = qualityButton ?? cancelButton;
        if (!button) return;
        const reason = window.prompt(qualityButton ? "Informe o parecer da inspeção (mínimo 5 caracteres):" : "Informe o motivo do cancelamento (mínimo 5 caracteres):");
        if (!reason || reason.trim().length < 5) return window.toastWarning?.("Justificativa obrigatória", "Informe ao menos 5 caracteres para manter a trilha de auditoria.");
        const approve = qualityButton?.dataset.receiptQuality === "approve";
        const action = qualityButton ? (approve ? "liberar este recebimento para o estoque" : "reprovar esta inspeção") : "cancelar este recebimento e compensar seus efeitos elegíveis";
        if (!await window.confirmDialog("Confirmar decisão", `Deseja ${action}? A justificativa será auditada.`, "Confirmar")) return;
        button.disabled = true;
        try {
            await request(`receipts/${encodeURIComponent(button.dataset.receiptId)}/${qualityButton ? "quality" : "cancel"}`, { method: "POST", body: JSON.stringify(qualityButton ? { approve, reason: reason.trim() } : { reason: reason.trim() }) });
            window.toastSuccess?.("Decisão registrada", qualityButton ? "A situação de qualidade e estoque foi atualizada." : "O recebimento foi cancelado com compensação rastreável.");
            await Promise.all([dashboard(), list("receipts"), list("orders")]);
        } catch (error) { window.toastWarning?.("Operação não concluída", error.message); }
        finally { button.disabled = false; }
    });
    document.querySelector("#proc-refresh").addEventListener("click", () => Promise.all([dashboard(), ...Object.keys(tables).map(name => list(name))]));
    dashboard(); Object.keys(tables).forEach(name => list(name)); lookups();
})();
