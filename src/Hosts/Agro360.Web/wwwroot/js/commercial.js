(() => {
    "use strict";

    const api = document.querySelector('meta[name="api-base"]')?.content?.replace(/\/$/, "") || "";
    const token = () => localStorage.getItem("agro360.token") || localStorage.getItem("agro360.accessToken");
    const headers = () => ({
        "Content-Type": "application/json",
        ...(token() ? { Authorization: `Bearer ${token()}` } : {})
    });

    let resource = sessionStorage.getItem("agro360.commercial.resource") || "dashboard";
    let page = 1;
    let total = 0;
    let activeRequestId = 0;

    const content = document.querySelector("#commercial-content");
    const toast = document.querySelector("#commercial-toast");
    const commercialDialog = document.querySelector("#commercial-dialog");
    const commercialForm = document.querySelector("#commercial-form");
    const proposalDialog = document.querySelector("#proposal-dialog");
    const proposalForm = document.querySelector("#proposal-form");
    const proposalDetailDialog = document.querySelector("#proposal-detail-dialog");
    const proposalAcceptDialog = document.querySelector("#proposal-accept-dialog");
    const proposalAcceptForm = document.querySelector("#proposal-accept-form");
    const proposalConvertDialog = document.querySelector("#proposal-convert-dialog");
    const proposalConvertForm = document.querySelector("#proposal-convert-form");
    const orderDetailDialog = document.querySelector("#order-detail-dialog");
    const scheduleCreateDialog = document.querySelector("#schedule-create-dialog");
    const scheduleCreateForm = document.querySelector("#schedule-create-form");
    const scheduleRescheduleDialog = document.querySelector("#schedule-reschedule-dialog");
    const scheduleRescheduleForm = document.querySelector("#schedule-reschedule-form");
    const scheduleCancelDialog = document.querySelector("#schedule-cancel-dialog");
    const scheduleCancelForm = document.querySelector("#schedule-cancel-form");
    const scheduleRevisionsDialog = document.querySelector("#schedule-revisions-dialog");

    let currentTenantId = null;
    let pendingAbortController = null;
    let currentDetailedOrder = null;

    const statusTranslations = {
        DRAFT: "Rascunho",
        SUBMITTED: "Submetida",
        APPROVED: "Aprovada",
        ACCEPTED: "Aceita",
        REJECTED: "Rejeitada",
        CANCELLED: "Cancelada",
        ACTIVE: "Ativo",
        BLOCKED: "Bloqueado",
        DELINQUENT: "Inadimplente",
        UNDER_REVIEW: "Em Análise",
        RESERVED: "Reservado",
        FULFILLMENT: "Em Atendimento",
        INVOICED: "Faturado",
        DELIVERED: "Entregue",
        RETURNED: "Devolvido",
        EXPECTED: "Previsto",
        PAID: "Pago",
        PLANNED: "Planejada",
        PREPARING: "Em Preparação",
        DISPATCHED: "Expedida",
        PARTIALLY_DELIVERED: "Parcialmente Entregue",
        COMPLETED: "Concluída",
        OPEN: "Aberto",
        PARTIALLY_FULFILLED: "Parcialmente Atendido"
    };

    function notify(message, isError = false) {
        toast.textContent = message;
        toast.style.background = isError ? "var(--red, #8c2f39)" : "var(--accent-strong, #143d2c)";
        toast.hidden = false;
        clearTimeout(notify.timer);
        notify.timer = setTimeout(() => {
            toast.hidden = true;
        }, 5000);
    }

    function formatMoney(value, currency = "BRL") {
        try {
            return Number(value || 0).toLocaleString("pt-BR", { style: "currency", currency: currency || "BRL" });
        } catch {
            return `${currency} ${Number(value || 0).toFixed(2)}`;
        }
    }

    function escapeHtml(value) {
        const container = document.createElement("div");
        container.textContent = value ?? "";
        return container.innerHTML;
    }

    function civilDate(value) {
        const match = String(value ?? "").match(/^(\d{4}-\d{2}-\d{2})/);
        return match ? match[1] : "";
    }

    function formatCivilDate(value) {
        const day = civilDate(value);
        if (!day) return "—";
        const [year, month, date] = day.split("-");
        return `${date}/${month}/${year}`;
    }

    function rememberIntent(storageKey, content) {
        const hash = JSON.stringify(content);
        let saved = null;
        try { saved = JSON.parse(sessionStorage.getItem(storageKey) || "null"); } catch { saved = null; }
        if (saved && saved.hash === hash && saved.key) return saved.key;
        const key = crypto.randomUUID();
        sessionStorage.setItem(storageKey, JSON.stringify({ hash, key }));
        return key;
    }

    function forgetIntent(storageKey) {
        sessionStorage.removeItem(storageKey);
    }

    async function request(path, options = {}) {
        if (!pendingAbortController || pendingAbortController.signal.aborted) {
            pendingAbortController = new AbortController();
        }
        const signal = options.signal || pendingAbortController.signal;
        const response = await fetch(`${api}${path}`, {
            ...options,
            signal,
            headers: { ...headers(), ...options.headers }
        });
        if (!response.ok) {
            const error = await response.json().catch(() => ({ title: "Não foi possível concluir." }));
            const err = new Error(error.detail || error.title || error.message || "Falha na operação.");
            err.status = response.status;
            err.data = error;
            throw err;
        }
        return response.status === 204 ? null : response.json();
    }

    async function loadLookups(container) {
        for (const select of container.querySelectorAll("[data-lookup]")) {
            const current = select.value;
            const lookupType = select.dataset.lookup;
            select.replaceChildren(new Option(select.dataset.placeholder || "Selecione...", ""));
            try {
                const searchParam = current ? `?search=${encodeURIComponent(current)}` : "";
                const rows = await request(`/api/commercial/lookups/${lookupType}${searchParam}`);
                let foundCurrent = false;
                for (const item of rows) {
                    const opt = new Option(item.label, item.id);
                    if (item.unit) opt.dataset.unit = item.unit;
                    if (item.basePrice) opt.dataset.price = item.basePrice;
                    select.add(opt);
                    if (item.id === current) foundCurrent = true;
                }
                if (current) {
                    if (foundCurrent) {
                        select.value = current;
                    } else {
                        // Preserva a seleção atual fora dos primeiros registros
                        select.add(new Option(`Registro Atual (${current.substring(0, 8)}...)`, current, true, true));
                        select.value = current;
                    }
                }
            } catch (error) {
                console.warn(`Falha ao carregar lookup ${lookupType}:`, error);
                const retryBtn = document.createElement("button");
                retryBtn.type = "button";
                retryBtn.className = "btn-sm btn-outline";
                retryBtn.textContent = "Tentar novamente";
                retryBtn.onclick = () => loadLookups(container);
                select.parentElement?.appendChild(retryBtn);
            }
        }
    }

    async function load() {
        const thisRequestId = ++activeRequestId;
        content.innerHTML = '<p class="empty-state">Carregando dados reais…</p>';

        try {
            if (resource === "dashboard") {
                const data = await request("/api/commercial/dashboard");
                if (thisRequestId !== activeRequestId) return;

                let metricsHtml =
                    `<article><small>Clientes ativos</small><strong>${data.activeCustomers}</strong></article>` +
                    `<article><small>Pipeline (BRL)</small><strong>${formatMoney(data.pipelineValue, "BRL")}</strong></article>` +
                    `<article><small>Receita prevista (BRL)</small><strong>${formatMoney(data.forecastRevenue, "BRL")}</strong></article>` +
                    `<article><small>Comissões previstas (BRL)</small><strong>${formatMoney(data.expectedCommissions, "BRL")}</strong></article>`;

                if (data.currencyTotals && data.currencyTotals.length > 0) {
                    metricsHtml += data.currencyTotals.map(ct =>
                        `<article><small>Pedidos em Carteira (${escapeHtml(ct.currency)})</small><strong>${formatMoney(ct.ordersTotal, ct.currency)}</strong></article>`
                    ).join("");
                }

                document.querySelector("#commercial-metrics").innerHTML = metricsHtml;
                content.innerHTML = '<h3 style="margin-top:0">Pedidos de Venda Recentes</h3>' + renderTable(data.orders, "orders");
                attachTableActions();
                return;
            }

            const query = new URLSearchParams({
                page,
                pageSize: 20,
                search: document.querySelector("#commercial-search").value,
                status: document.querySelector("#commercial-status").value
            });
            const data = await request(`/api/commercial/${resource}?${query}`);
            if (thisRequestId !== activeRequestId) return;
            total = data.total;
            content.innerHTML = renderTable(data.items, resource);
            document.querySelector("#page-label").textContent = `Página ${page} · ${total} registro(s)`;
            attachTableActions();
        } catch (error) {
            if (thisRequestId !== activeRequestId) return;
            content.innerHTML = `<div class="conflict-alert" style="margin:2rem 0"><strong>Falha na comunicação com o servidor:</strong> ${escapeHtml(error.message)}</div>`;
            notify(error.message, true);
        }
    }

    function renderTable(items, resType) {
        if (!items?.length) {
            return '<p class="empty-state">Nenhum registro encontrado nesta categoria.</p>';
        }

        if (resType === "proposals") {
            return `
            <table class="commercial-table">
                <thead>
                    <tr>
                        <th>Número</th>
                        <th>Cliente / Organização</th>
                        <th>Status</th>
                        <th>Moeda</th>
                        <th>Valor Total</th>
                        <th>Atualização</th>
                        <th style="width:140px">Ações</th>
                    </tr>
                </thead>
                <tbody>
                    ${items.map(item => {
                        const clientName = item.customerName || (item.detail || "").split(" · ")[0] || "—";
                        const currency = item.currency || (item.detail || "").split(" · ")[1] || "BRL";
                        const statusClass = `status-${(item.status || "").toLowerCase()}`;
                        const statusPt = statusTranslations[item.status] || item.status;
                        return `
                        <tr>
                            <td><strong>${escapeHtml(item.name)}</strong></td>
                            <td>${escapeHtml(clientName)}</td>
                            <td><span class="status-pill ${statusClass}">${escapeHtml(statusPt)}</span></td>
                            <td><span class="version-chip">${escapeHtml(currency)}</span></td>
                            <td><strong>${formatMoney(item.amount, currency)}</strong></td>
                            <td>${new Date(item.updatedAt).toLocaleDateString("pt-BR")}</td>
                            <td class="actions-cell">
                                <button type="button" class="btn-sm btn-action view-proposal-btn" data-id="${item.id}">Ver detalhes</button>
                            </td>
                        </tr>`;
                    }).join("")}
                </tbody>
            </table>`;
        }

        if (resType === "orders") {
            return `
            <table class="commercial-table">
                <thead>
                    <tr>
                        <th>Número</th>
                        <th>Cliente / Destinatário</th>
                        <th>Status</th>
                        <th>Moeda</th>
                        <th>Total</th>
                        <th>Atualização</th>
                        <th style="width:140px">Ações</th>
                    </tr>
                </thead>
                <tbody>
                    ${items.map(item => {
                        const clientName = item.customerName || (item.detail || "").split(" · ")[0] || "—";
                        const currency = item.currency || (item.detail || "").split(" · ")[1] || "BRL";
                        const statusClass = `status-${(item.status || "").toLowerCase()}`;
                        const statusPt = statusTranslations[item.status] || item.status;
                        return `
                        <tr>
                            <td><strong>${escapeHtml(item.name)}</strong></td>
                            <td>${escapeHtml(clientName)}</td>
                            <td><span class="status-pill ${statusClass}">${escapeHtml(statusPt)}</span></td>
                            <td><span class="version-chip">${escapeHtml(currency)}</span></td>
                            <td><strong>${formatMoney(item.amount, currency)}</strong></td>
                            <td>${new Date(item.updatedAt).toLocaleDateString("pt-BR")}</td>
                            <td class="actions-cell">
                                <button type="button" class="btn-sm btn-action view-order-btn" data-id="${item.id}">Ver detalhes</button>
                            </td>
                        </tr>`;
                    }).join("")}
                </tbody>
            </table>`;
        }

        return `
        <table class="commercial-table">
            <thead>
                <tr>
                    <th>Nome / Identificador</th>
                    <th>Detalhe</th>
                    <th>Status</th>
                    <th>Valor</th>
                    <th>Atualização</th>
                </tr>
            </thead>
            <tbody>
                ${items.map(item => {
                    const statusClass = `status-${(item.status || "").toLowerCase()}`;
                    const statusPt = statusTranslations[item.status] || item.status;
                    return `
                    <tr>
                        <td><strong>${escapeHtml(item.name)}</strong></td>
                        <td>${escapeHtml(item.detail || "—")}</td>
                        <td><span class="status-pill ${statusClass}">${escapeHtml(statusPt)}</span></td>
                        <td>${item.amount > 0 ? formatMoney(item.amount) : "—"}</td>
                        <td>${new Date(item.updatedAt).toLocaleDateString("pt-BR")}</td>
                    </tr>`;
                }).join("")}
            </tbody>
        </table>`;
    }

    function attachTableActions() {
        document.querySelectorAll(".view-proposal-btn").forEach(btn => {
            btn.onclick = () => openProposalDetails(btn.dataset.id);
        });
        document.querySelectorAll(".view-order-btn").forEach(btn => {
            btn.onclick = () => openOrderDetails(btn.dataset.id);
        });
    }

    // Gerenciamento de Linhas de Itens na Proposta
    let availableProducts = [];
    async function fetchProductsLookup() {
        if (!availableProducts.length) {
            try {
                availableProducts = await request("/api/commercial/lookups/products");
            } catch {
                availableProducts = [];
            }
        }
        return availableProducts;
    }

    async function addProposalItemRow(itemData = null) {
        const products = await fetchProductsLookup();
        const tbody = document.querySelector("#proposal-items-body");
        const tr = document.createElement("tr");

        const productOptions = products.map(p =>
            `<option value="${p.id}" data-unit="${escapeHtml(p.unit || "")}" data-price="${p.basePrice || ""}" ${itemData && itemData.productId === p.id ? "selected" : ""}>${escapeHtml(p.label)}</option>`
        ).join("");

        tr.innerHTML = `
            <td>
                <select class="item-product" required>
                    <option value="">Selecione…</option>
                    ${productOptions}
                </select>
            </td>
            <td>
                <input class="item-unit" value="${escapeHtml(itemData?.unit || "")}" placeholder="Ex: KG, SC" maxlength="20" required style="width:80px" />
            </td>
            <td>
                <input class="item-qty" type="number" step="0.0001" min="0.0001" value="${itemData?.quantity || 1}" required style="width:100px" />
            </td>
            <td>
                <input class="item-price" type="number" step="0.01" min="0.01" value="${itemData?.unitPrice != null ? itemData.unitPrice : ""}" placeholder="0,00" required style="width:110px" />
            </td>
            <td>
                <input class="item-discount" type="number" step="0.01" min="0" max="100" value="${itemData?.discountPercentage || 0}" style="width:80px" />
            </td>
            <td class="item-total-cell" style="font-weight:700">0,00</td>
            <td>
                <button type="button" class="btn-sm btn-danger remove-item-btn" title="Remover item">×</button>
            </td>
        `;

        const productSelect = tr.querySelector(".item-product");
        const unitInput = tr.querySelector(".item-unit");
        const priceInput = tr.querySelector(".item-price");

        productSelect.onchange = () => {
            const selectedOpt = productSelect.selectedOptions[0];
            if (selectedOpt) {
                if (selectedOpt.dataset.unit && !unitInput.value) {
                    unitInput.value = selectedOpt.dataset.unit;
                }
                if (selectedOpt.dataset.price && !priceInput.value) {
                    priceInput.value = selectedOpt.dataset.price;
                }
            }
            recalculateProposalTotals();
        };

        tr.querySelectorAll("input, select").forEach(input => {
            input.oninput = recalculateProposalTotals;
            input.onchange = recalculateProposalTotals;
        });

        tr.querySelector(".remove-item-btn").onclick = () => {
            if (tbody.children.length > 1) {
                tr.remove();
                recalculateProposalTotals();
            } else {
                notify("A proposta deve ter ao menos um item.", true);
            }
        };

        tbody.appendChild(tr);
        recalculateProposalTotals();
    }

    function recalculateProposalTotals() {
        let itemsTotal = 0;
        const currency = proposalForm.currency?.value || "BRL";

        document.querySelectorAll("#proposal-items-body tr").forEach(row => {
            const qty = parseFloat(row.querySelector(".item-qty")?.value) || 0;
            const price = parseFloat(row.querySelector(".item-price")?.value) || 0;
            const disc = parseFloat(row.querySelector(".item-discount")?.value) || 0;
            const lineTotal = Math.round(qty * price * (1 - disc / 100) * 100) / 100;
            itemsTotal += lineTotal;
            const totalCell = row.querySelector(".item-total-cell");
            if (totalCell) totalCell.textContent = formatMoney(lineTotal, currency);
        });

        const freight = parseFloat(proposalForm.freight?.value) || 0;
        const grandTotal = Math.round((itemsTotal + freight) * 100) / 100;

        document.querySelector("#summary-items-total").textContent = formatMoney(itemsTotal, currency);
        document.querySelector("#summary-freight").textContent = formatMoney(freight, currency);
        document.querySelector("#summary-grand-total").textContent = formatMoney(grandTotal, currency);
    }

    proposalForm.freight.oninput = recalculateProposalTotals;
    proposalForm.currency.onchange = recalculateProposalTotals;
    document.querySelector("#add-proposal-item-row").onclick = () => addProposalItemRow();

    // Visualização e Ações de Detalhes da Proposta
    let currentDetailedProposal = null;

    async function openProposalDetails(proposalId, targetVersion = null) {
        try {
            const url = targetVersion ? `/api/commercial/proposals/${proposalId}?version=${targetVersion}` : `/api/commercial/proposals/${proposalId}`;
            const proposal = await request(url);
            currentDetailedProposal = proposal;

            document.querySelector("#detail-proposal-number").textContent = `Proposta ${proposal.number}`;
            document.querySelector("#detail-proposal-subtitle").textContent =
                `Versão ${proposal.version} de ${proposal.currentVersion} ${proposal.version === proposal.currentVersion ? "(Atual)" : "(Histórica)"}`;
            document.querySelector("#detail-customer-name").textContent = proposal.customerName || "—";
            document.querySelector("#detail-status-badge").textContent = statusTranslations[proposal.status] || proposal.status;
            document.querySelector("#detail-version-label").textContent = `V${proposal.version}`;
            document.querySelector("#detail-currency").textContent = proposal.currency;
            document.querySelector("#detail-valid-until").textContent = new Date(proposal.validUntil).toLocaleDateString("pt-BR");
            document.querySelector("#detail-payment-terms").textContent = proposal.paymentTerms;

            const changeBox = document.querySelector("#detail-change-reason-box");
            if (proposal.changeReason) {
                changeBox.hidden = false;
                document.querySelector("#detail-change-reason").textContent = proposal.changeReason;
            } else {
                changeBox.hidden = true;
            }

            const tbody = document.querySelector("#detail-items-body");
            tbody.innerHTML = proposal.items.map(item => {
                const balance = Math.max(0, item.quantity - item.convertedQuantity);
                return `
                <tr>
                    <td><strong>${escapeHtml(item.productName || item.productId)}</strong></td>
                    <td>${escapeHtml(item.unit)}</td>
                    <td>${Number(item.quantity).toLocaleString("pt-BR")}</td>
                    <td>${formatMoney(item.unitPrice, proposal.currency)}</td>
                    <td>${item.discountPercentage}%</td>
                    <td><strong>${formatMoney(item.total, proposal.currency)}</strong></td>
                    <td>${Number(item.convertedQuantity).toLocaleString("pt-BR")}</td>
                    <td style="color:${balance > 0 ? "#65d6a6" : "#87a399"};font-weight:700">${Number(balance).toLocaleString("pt-BR")}</td>
                </tr>`;
            }).join("");

            document.querySelector("#detail-items-total").textContent = formatMoney(proposal.itemsTotal, proposal.currency);
            document.querySelector("#detail-freight").textContent = formatMoney(proposal.freight, proposal.currency);
            document.querySelector("#detail-grand-total").textContent = formatMoney(proposal.total, proposal.currency);

            renderDetailActions(proposal);
            proposalDetailDialog.showModal();
        } catch (error) {
            notify(`Falha ao abrir detalhes: ${error.message}`, true);
        }
    }

    function renderDetailActions(proposal) {
        const bar = document.querySelector("#detail-actions-bar");
        bar.innerHTML = "";
        const isCurrent = proposal.version === proposal.currentVersion;

        if (!isCurrent) {
            const btnCurrent = document.createElement("button");
            btnCurrent.type = "button";
            btnCurrent.className = "btn-sm btn-outline";
            btnCurrent.textContent = "Ver Versão Atual Mais Recente";
            btnCurrent.onclick = () => openProposalDetails(proposal.proposalId, proposal.currentVersion);
            bar.appendChild(btnCurrent);
            return;
        }

        if (proposal.status === "DRAFT") {
            const btnSubmit = document.createElement("button");
            btnSubmit.type = "button";
            btnSubmit.className = "btn-sm btn-action primary";
            btnSubmit.textContent = "Submeter para Aprovação";
            btnSubmit.onclick = async () => {
                try {
                    await request(`/api/commercial/proposals/${proposal.proposalId}/submit`, {
                        method: "POST",
                        body: JSON.stringify({ version: proposal.version, reason: "Submissão comercial" })
                    });
                    notify("Proposta submetida com sucesso.");
                    proposalDetailDialog.close();
                    await load();
                } catch (e) {
                    notify(e.message, true);
                }
            };
            bar.appendChild(btnSubmit);

            const btnRevise = document.createElement("button");
            btnRevise.type = "button";
            btnRevise.className = "btn-sm btn-outline";
            btnRevise.textContent = "Revisar Proposta";
            btnRevise.onclick = () => {
                proposalDetailDialog.close();
                openProposalEditModal(proposal);
            };
            bar.appendChild(btnRevise);
        } else if (proposal.status === "SUBMITTED") {
            const btnApprove = document.createElement("button");
            btnApprove.type = "button";
            btnApprove.className = "btn-sm btn-action primary";
            btnApprove.textContent = "Aprovar Proposta";
            btnApprove.onclick = async () => {
                try {
                    await request(`/api/commercial/proposals/${proposal.proposalId}/approve`, {
                        method: "POST",
                        body: JSON.stringify({ version: proposal.version, reason: "Aprovado pela gerência" })
                    });
                    notify("Proposta aprovada com sucesso.");
                    proposalDetailDialog.close();
                    await load();
                } catch (e) {
                    notify(e.message, true);
                }
            };
            bar.appendChild(btnApprove);

            const btnReject = document.createElement("button");
            btnReject.type = "button";
            btnReject.className = "btn-sm btn-danger";
            btnReject.textContent = "Rejeitar Proposta";
            btnReject.onclick = async () => {
                const reason = prompt("Informe a justificativa da rejeição:");
                if (!reason) return;
                try {
                    await request(`/api/commercial/proposals/${proposal.proposalId}/reject`, {
                        method: "POST",
                        body: JSON.stringify({ version: proposal.version, reason })
                    });
                    notify("Proposta rejeitada.");
                    proposalDetailDialog.close();
                    await load();
                } catch (e) {
                    notify(e.message, true);
                }
            };
            bar.appendChild(btnReject);

            const btnRevise = document.createElement("button");
            btnRevise.type = "button";
            btnRevise.className = "btn-sm btn-outline";
            btnRevise.textContent = "Revisar / Alterar";
            btnRevise.onclick = () => {
                proposalDetailDialog.close();
                openProposalEditModal(proposal);
            };
            bar.appendChild(btnRevise);
        } else if (proposal.status === "APPROVED") {
            const btnAccept = document.createElement("button");
            btnAccept.type = "button";
            btnAccept.className = "btn-sm btn-action primary";
            btnAccept.textContent = "Registrar Aceite do Cliente";
            btnAccept.onclick = () => {
                proposalDetailDialog.close();
                openAcceptModal(proposal);
            };
            bar.appendChild(btnAccept);

            const btnCancel = document.createElement("button");
            btnCancel.type = "button";
            btnCancel.className = "btn-sm btn-danger";
            btnCancel.textContent = "Cancelar Proposta";
            btnCancel.onclick = async () => {
                const reason = prompt("Informe o motivo do cancelamento:");
                if (!reason) return;
                try {
                    await request(`/api/commercial/proposals/${proposal.proposalId}/cancel`, {
                        method: "POST",
                        body: JSON.stringify({ version: proposal.version, reason })
                    });
                    notify("Proposta cancelada.");
                    proposalDetailDialog.close();
                    await load();
                } catch (e) {
                    notify(e.message, true);
                }
            };
            bar.appendChild(btnCancel);

            const btnRevise = document.createElement("button");
            btnRevise.type = "button";
            btnRevise.className = "btn-sm btn-outline";
            btnRevise.textContent = "Revisar Proposta";
            btnRevise.onclick = () => {
                proposalDetailDialog.close();
                openProposalEditModal(proposal);
            };
            bar.appendChild(btnRevise);
        } else if (proposal.status === "ACCEPTED") {
            const hasBalance = proposal.items.some(i => i.quantity - i.convertedQuantity > 0.000001);
            if (hasBalance) {
                const btnConvert = document.createElement("button");
                btnConvert.type = "button";
                btnConvert.className = "btn-sm btn-action primary";
                btnConvert.textContent = "Converter em Pedido de Venda";
                btnConvert.onclick = () => {
                    proposalDetailDialog.close();
                    openConvertModal(proposal);
                };
                bar.appendChild(btnConvert);
            } else {
                const spanDone = document.createElement("span");
                spanDone.style.color = "#65d6a6";
                spanDone.style.fontWeight = "700";
                spanDone.textContent = "✓ Saldo 100% convertido em pedidos de venda.";
                bar.appendChild(spanDone);
            }
        } else if (proposal.status === "REJECTED") {
            const btnRevise = document.createElement("button");
            btnRevise.type = "button";
            btnRevise.className = "btn-sm btn-outline";
            btnRevise.textContent = "Criar Nova Versão (Retornar a Rascunho)";
            btnRevise.onclick = () => {
                proposalDetailDialog.close();
                openProposalEditModal(proposal);
            };
            bar.appendChild(btnRevise);
        }
    }

    // Modal de Criação / Edição de Proposta
    async function openProposalCreateModal() {
        proposalForm.reset();
        proposalForm.proposalId.value = "";
        proposalForm.expectedVersion.value = "";
        document.querySelector("#proposal-modal-title").textContent = "Nova Proposta Comercial";
        document.querySelector("#save-proposal-submit").textContent = "Salvar Proposta";
        document.querySelector("#proposal-change-reason-group").hidden = true;
        document.querySelector("#proposal-conflict-banner").hidden = true;
        document.querySelector("#proposal-form-error").textContent = "";

        const today = new Date();
        today.setDate(today.getDate() + 15);
        proposalForm.validUntil.value = today.toISOString().split("T")[0];
        proposalForm.freight.value = "0.00";
        proposalForm.currency.value = "BRL";

        await loadLookups(proposalDialog);
        document.querySelector("#proposal-items-body").innerHTML = "";
        await addProposalItemRow();
        proposalDialog.showModal();
    }

    async function openProposalEditModal(proposal) {
        proposalForm.reset();
        proposalForm.proposalId.value = proposal.proposalId;
        proposalForm.expectedVersion.value = proposal.version;
        document.querySelector("#proposal-modal-title").textContent = `Revisar Proposta ${proposal.number} (Versão Atual: ${proposal.version})`;
        document.querySelector("#save-proposal-submit").textContent = "Gerar Nova Versão";
        document.querySelector("#proposal-conflict-banner").hidden = true;
        document.querySelector("#proposal-form-error").textContent = "";

        const requiresReason = proposal.status !== "DRAFT";
        document.querySelector("#proposal-change-reason-group").hidden = !requiresReason;

        await loadLookups(proposalDialog);

        proposalForm.customerId.value = proposal.customerId;
        proposalForm.currency.value = proposal.currency;
        proposalForm.validUntil.value = proposal.validUntil;
        proposalForm.paymentTerms.value = proposal.paymentTerms;
        proposalForm.freight.value = proposal.freight;
        if (proposal.opportunityId) proposalForm.opportunityId.value = proposal.opportunityId;
        if (proposal.representativeId) proposalForm.representativeId.value = proposal.representativeId;

        const tbody = document.querySelector("#proposal-items-body");
        tbody.innerHTML = "";
        for (const item of proposal.items) {
            await addProposalItemRow(item);
        }

        proposalDialog.showModal();
    }

    proposalForm.onsubmit = async event => {
        event.preventDefault();
        const errElement = document.querySelector("#proposal-form-error");
        errElement.textContent = "";
        document.querySelector("#proposal-conflict-banner").hidden = true;

        const isRevision = !!proposalForm.proposalId.value;
        const proposalId = proposalForm.proposalId.value;
        const expectedVersion = proposalForm.expectedVersion.value ? parseInt(proposalForm.expectedVersion.value, 10) : null;

        const rows = document.querySelectorAll("#proposal-items-body tr");
        if (!rows.length) {
            errElement.textContent = "Adicione ao menos um item à proposta.";
            return;
        }

        const items = [];
        for (const row of rows) {
            const productId = row.querySelector(".item-product")?.value;
            const unit = row.querySelector(".item-unit")?.value?.trim();
            const quantity = parseFloat(row.querySelector(".item-qty")?.value);
            const unitPrice = parseFloat(row.querySelector(".item-price")?.value);
            const discountPercentage = parseFloat(row.querySelector(".item-discount")?.value) || 0;

            if (!productId || !unit || quantity <= 0 || unitPrice <= 0) {
                errElement.textContent = "Preencha todos os campos obrigatórios dos itens com valores válidos.";
                return;
            }

            items.push({ productId, unit, quantity, unitPrice, discountPercentage });
        }

        const payload = {
            customerId: proposalForm.customerId.value,
            opportunityId: proposalForm.opportunityId.value || null,
            representativeId: proposalForm.representativeId.value || null,
            currency: proposalForm.currency.value,
            validUntil: proposalForm.validUntil.value,
            freight: parseFloat(proposalForm.freight.value) || 0,
            paymentTerms: proposalForm.paymentTerms.value.trim(),
            items,
            changeReason: proposalForm.changeReason?.value?.trim() || null,
            expectedVersion: expectedVersion
        };

        try {
            if (isRevision) {
                await request(`/api/commercial/proposals/${proposalId}`, {
                    method: "PUT",
                    body: JSON.stringify(payload)
                });
                notify("Nova versão da proposta gerada com sucesso.");
            } else {
                await request("/api/commercial/proposals", {
                    method: "POST",
                    body: JSON.stringify(payload)
                });
                notify("Proposta comercial criada com sucesso.");
            }
            proposalDialog.close();
            resource = "proposals";
            await load();
        } catch (error) {
            if (error.status === 409) {
                document.querySelector("#proposal-conflict-banner").hidden = false;
                document.querySelector("#proposal-conflict-msg").textContent = error.message;
                document.querySelector("#proposal-reload-latest").onclick = () => {
                    openProposalDetails(proposalId);
                };
            } else {
                errElement.textContent = error.message;
            }
            notify(error.message, true);
        }
    };

    // Modal de Aceite
    function openAcceptModal(proposal) {
        proposalAcceptForm.reset();
        proposalAcceptForm.proposalId.value = proposal.proposalId;
        proposalAcceptForm.version.value = proposal.version;
        document.querySelector("#accept-proposal-number").textContent = proposal.number;
        document.querySelector("#accept-version-number").textContent = proposal.version;
        proposalAcceptDialog.showModal();
    }

    proposalAcceptForm.onsubmit = async event => {
        event.preventDefault();
        const proposalId = proposalAcceptForm.proposalId.value;
        const payload = {
            version: parseInt(proposalAcceptForm.version.value, 10),
            evidenceType: proposalAcceptForm.evidenceType.value,
            evidenceReference: proposalAcceptForm.evidenceReference.value.trim(),
            acceptedAt: proposalAcceptForm.acceptedAt.value ? new Date(proposalAcceptForm.acceptedAt.value).toISOString() : null
        };

        try {
            await request(`/api/commercial/proposals/${proposalId}/accept`, {
                method: "POST",
                body: JSON.stringify(payload)
            });
            proposalAcceptDialog.close();
            notify("Aceite registrado com sucesso! A proposta já pode ser convertida em pedido.");
            await load();
            openProposalDetails(proposalId);
        } catch (error) {
            proposalAcceptForm.querySelector(".form-error").textContent = error.message;
            notify(error.message, true);
        }
    };

    // Modal de Conversão
    function openConvertModal(proposal) {
        proposalConvertForm.reset();
        proposalConvertForm.proposalId.value = proposal.proposalId;
        proposalConvertForm.version.value = proposal.version;
        proposalConvertForm.idempotencyKey.value = crypto.randomUUID();
        document.querySelector("#convert-proposal-number").textContent = `${proposal.number} (V${proposal.version})`;
        document.querySelector("#convert-success-alert").hidden = true;
        document.querySelector("#convert-submit-button").disabled = false;

        const tbody = document.querySelector("#convert-items-body");
        tbody.innerHTML = proposal.items.map(item => {
            const balance = Math.max(0, item.quantity - item.convertedQuantity);
            return `
            <tr data-item-id="${item.id}" data-balance="${balance}">
                <td><strong>${escapeHtml(item.productName || item.productId)}</strong></td>
                <td>${escapeHtml(item.unit)}</td>
                <td>${Number(item.quantity).toLocaleString("pt-BR")}</td>
                <td>${Number(item.convertedQuantity).toLocaleString("pt-BR")}</td>
                <td style="color:#65d6a6;font-weight:700">${Number(balance).toLocaleString("pt-BR")}</td>
                <td>
                    <input type="number" class="convert-qty-input" step="0.0001" min="0" max="${balance}" value="${balance}" style="width:120px" ${balance <= 0 ? "disabled" : ""} />
                </td>
            </tr>`;
        }).join("");

        document.querySelector("#convert-freight-note").textContent =
            `Moeda: ${proposal.currency} · O frete e rateios da proposta serão alocados de forma determinística pelo servidor conforme o saldo remanescente.`;

        proposalConvertDialog.showModal();
    }

    proposalConvertForm.onsubmit = async event => {
        event.preventDefault();
        const errElement = proposalConvertForm.querySelector(".form-error");
        errElement.textContent = "";

        const proposalId = proposalConvertForm.proposalId.value;
        const version = parseInt(proposalConvertForm.version.value, 10);
        const idempotencyKey = proposalConvertForm.idempotencyKey.value;

        const conversionItems = [];
        document.querySelectorAll("#convert-items-body tr").forEach(row => {
            const itemId = row.dataset.itemId;
            const balance = parseFloat(row.dataset.balance) || 0;
            const inputVal = parseFloat(row.querySelector(".convert-qty-input")?.value) || 0;
            if (inputVal > 0 && inputVal <= balance) {
                conversionItems.push({ proposalItemId: itemId, quantity: inputVal });
            }
        });

        if (!conversionItems.length) {
            errElement.textContent = "Informe ao menos uma quantidade válida maior que zero para converter em pedido.";
            return;
        }

        const payload = { version, idempotencyKey, items: conversionItems };
        const submitBtn = document.querySelector("#convert-submit-button");
        submitBtn.disabled = true;

        try {
            const result = await request(`/api/commercial/proposals/${proposalId}/convert`, {
                method: "POST",
                body: JSON.stringify(payload)
            });

            document.querySelector("#convert-success-alert").hidden = false;
            document.querySelector("#convert-success-details").textContent =
                `Pedido ${result.orderNumber || "criado"} no valor de ${formatMoney(result.total, result.currency)}.${result.existing ? " (Retorno de requisição anterior idempotente)" : ""}`;

            document.querySelector("#convert-goto-orders").onclick = () => {
                proposalConvertDialog.close();
                document.querySelectorAll("[data-resource]").forEach(item => item.classList.remove("active"));
                const orderBtn = document.querySelector('[data-resource="orders"]');
                if (orderBtn) orderBtn.classList.add("active");
                resource = "orders";
                sessionStorage.setItem("agro360.commercial.resource", resource);
                updateCreateButton();
                page = 1;
                load();
            };

            notify(`Pedido ${result.orderNumber || ""} gerado com sucesso!`);
            proposalConvertDialog.close();
            await load();
            await openOrderDetails(result.orderId);
        } catch (error) {
            submitBtn.disabled = false;
            errElement.textContent = error.message;
            notify(error.message, true);
        }
    };

    async function openOrderDetails(orderId) {
        if (!orderDetailDialog) return;
        try {
            const order = await request(`/api/commercial/orders/${orderId}`);
            if (!order) return;
            currentDetailedOrder = order;

            document.querySelector("#order-detail-title").textContent = `Pedido ${order.orderNumber}`;
            const pill = document.querySelector("#order-detail-status-pill");
            const statusClass = `status-${(order.status || "").toLowerCase()}`;
            pill.className = `status-pill ${statusClass}`;
            pill.textContent = statusTranslations[order.status] || order.status;

            document.querySelector("#order-detail-customer").textContent = order.customerName || "—";
            document.querySelector("#order-detail-proposal").textContent = order.proposalNumber
                ? `${order.proposalNumber} (Versão ${order.proposalVersion})`
                : "Venda Direta / Sem Proposta";
            document.querySelector("#order-detail-currency").textContent = order.currency || "BRL";
            document.querySelector("#order-detail-payment").textContent = order.paymentTerms || "Padrão";
            document.querySelector("#order-detail-delivery").textContent = order.expectedDelivery
                ? new Date(`${order.expectedDelivery}T12:00:00`).toLocaleDateString("pt-BR")
                : "A combinar";
            document.querySelector("#order-detail-items-total").textContent = formatMoney(order.itemsTotal, order.currency);
            document.querySelector("#order-detail-freight").textContent = formatMoney(order.freight, order.currency);
            document.querySelector("#order-detail-total").textContent = formatMoney(order.totalAmount, order.currency);

            const notesCard = document.querySelector("#order-detail-notes-card");
            if (order.notes) {
                notesCard.hidden = false;
                document.querySelector("#order-detail-notes-text").textContent = order.notes;
            } else {
                notesCard.hidden = true;
            }

            document.querySelector("#order-detail-next-action").textContent = order.nextPermittedAction || "—";

            // Itens com programação e saldo elegível
            const itemsBody = document.querySelector("#order-detail-items-body");
            if (order.items && order.items.length) {
                itemsBody.innerHTML = order.items.map(item => `
                    <tr>
                        <td><strong>${escapeHtml(item.productName || item.productId)}</strong></td>
                        <td>${escapeHtml(item.unit)}</td>
                        <td>${Number(item.quantity).toLocaleString("pt-BR")}</td>
                        <td>${formatMoney(item.unitPrice, order.currency)}</td>
                        <td>${item.discountPercentage ? `${Number(item.discountPercentage).toFixed(2)}%` : "0%"}</td>
                        <td><strong>${formatMoney(item.totalAmount, order.currency)}</strong></td>
                        <td>${Number(item.scheduledQuantity || 0).toLocaleString("pt-BR")}</td>
                        <td style="color:${(item.eligibleScheduleBalance || 0) > 0 ? "var(--accent)" : "var(--muted)"};font-weight:700">${Number(item.eligibleScheduleBalance || 0).toLocaleString("pt-BR")}</td>
                    </tr>
                `).join("");
            } else {
                itemsBody.innerHTML = '<tr><td colspan="8" class="empty-state">Nenhum item registrado.</td></tr>';
            }

            // Botão Nova Programação
            const canSchedule = ["APPROVED", "RESERVED", "FULFILLMENT"].includes(order.status) &&
                (order.items || []).some(i => Number(i.eligibleScheduleBalance) > 0);
            const createScheduleBtn = document.querySelector("#order-create-schedule-btn");
            if (createScheduleBtn) {
                createScheduleBtn.style.display = canSchedule ? "inline-block" : "none";
                createScheduleBtn.onclick = () => openScheduleCreateModal(order);
            }

            // Tabela de Programações de Entrega
            const schedulesBody = document.querySelector("#order-detail-schedules-body");
            if (order.schedules && order.schedules.length) {
                schedulesBody.innerHTML = order.schedules.map(s => {
                    const sClass = `status-${(s.status || "").toLowerCase()}`;
                    const sStatusPt = statusTranslations[s.status] || s.status;
                    const plannedDateFormatted = formatCivilDate(s.plannedDate);
                    const isRescheduled = s.originalPlannedDate && civilDate(s.originalPlannedDate) !== civilDate(s.plannedDate);
                    const originalDateFormatted = isRescheduled ? formatCivilDate(s.originalPlannedDate) : "";

                    const itemsSummary = (s.items || []).map(i =>
                        `${escapeHtml(i.productName)}: ${Number(i.quantity).toLocaleString("pt-BR")} ${escapeHtml(i.unit)}` +
                        (i.dispatchedQuantity > 0 ? ` (${Number(i.dispatchedQuantity).toLocaleString("pt-BR")} exp)` : "")
                    ).join("<br>");

                    const actions = s.allowedActions || [];
                    const canReschedule = actions.includes("reschedule");
                    const canCancel = actions.includes("cancel");
                    const hasRevisions = s.revisions && s.revisions.length > 0;

                    return `
                    <tr>
                        <td><strong>${escapeHtml(s.scheduleNumber)}</strong></td>
                        <td>
                            <strong>${plannedDateFormatted}</strong>
                            ${isRescheduled ? `<br><small style="color:var(--muted)">Original: ${originalDateFormatted}</small>` : ""}
                        </td>
                        <td>${escapeHtml(s.destination || "—")}</td>
                        <td>${escapeHtml(s.responsibleName || "Sem responsável")}</td>
                        <td><span class="status-pill ${sClass}">${escapeHtml(sStatusPt)}</span></td>
                        <td style="font-size:0.85rem">${itemsSummary || "—"}</td>
                        <td class="actions-cell">
                            ${canReschedule ? `<button type="button" class="btn-sm btn-outline schedule-reschedule-btn" data-schedule-id="${s.id}">Reprogramar</button>` : ""}
                            ${canCancel ? `<button type="button" class="btn-sm btn-danger schedule-cancel-btn" data-schedule-id="${s.id}">Cancelar</button>` : ""}
                            ${hasRevisions ? `<button type="button" class="btn-sm btn-outline schedule-revisions-btn" data-schedule-id="${s.id}">Revisões (${s.revisions.length})</button>` : ""}
                            ${s.blockReason ? `<small class="form-hint">${escapeHtml(s.blockReason)}</small>` : ""}
                        </td>
                    </tr>`;
                }).join("");

                schedulesBody.querySelectorAll(".schedule-reschedule-btn").forEach(btn => {
                    btn.onclick = () => {
                        const sched = order.schedules.find(x => x.id === btn.dataset.scheduleId);
                        if (sched) openScheduleRescheduleModal(sched, order);
                    };
                });
                schedulesBody.querySelectorAll(".schedule-cancel-btn").forEach(btn => {
                    btn.onclick = () => {
                        const sched = order.schedules.find(x => x.id === btn.dataset.scheduleId);
                        if (sched) openScheduleCancelModal(sched, order);
                    };
                });
                schedulesBody.querySelectorAll(".schedule-revisions-btn").forEach(btn => {
                    btn.onclick = () => {
                        const sched = order.schedules.find(x => x.id === btn.dataset.scheduleId);
                        if (sched) openScheduleRevisionsModal(sched);
                    };
                });
            } else {
                schedulesBody.innerHTML = '<tr><td colspan="7" style="text-align:center;color:var(--muted);padding:1rem">Nenhuma programação de entrega cadastrada para este pedido.</td></tr>';
            }

            // Fulfillments
            const fulfillmentsBody = document.querySelector("#order-detail-fulfillments-body");
            if (order.fulfillments && order.fulfillments.length) {
                fulfillmentsBody.innerHTML = order.fulfillments.map(f => {
                    const fClass = `status-${(f.status || "").toLowerCase()}`;
                    const fStatusPt = statusTranslations[f.status] || f.status;
                    return `
                    <tr>
                        <td><strong>${escapeHtml(f.shipmentNumber)}</strong></td>
                        <td><span class="status-pill ${fClass}">${escapeHtml(fStatusPt)}</span></td>
                        <td>${new Date(f.createdAt).toLocaleDateString("pt-BR")} ${new Date(f.createdAt).toLocaleTimeString("pt-BR")}</td>
                    </tr>`;
                }).join("");
            } else {
                fulfillmentsBody.innerHTML = '<tr><td colspan="3" style="text-align:center;color:var(--muted);padding:1rem">Nenhum atendimento logístico vinculado até o momento.</td></tr>';
            }

            // History
            const historyBody = document.querySelector("#order-detail-history-body");
            if (order.history && order.history.length) {
                historyBody.innerHTML = order.history.map(h => `
                    <tr>
                        <td><span class="version-chip">${escapeHtml(h.eventType)}</span></td>
                        <td>${escapeHtml(h.details || "—")}</td>
                        <td>${new Date(h.occurredAt).toLocaleDateString("pt-BR")} ${new Date(h.occurredAt).toLocaleTimeString("pt-BR")}</td>
                    </tr>
                `).join("");
            } else {
                historyBody.innerHTML = '<tr><td colspan="3" style="text-align:center;color:var(--muted);padding:1rem">Nenhum evento registrado.</td></tr>';
            }

            // Logistics button - preselected order without auto-reserving stock
            const logisticsBtn = document.querySelector("#order-detail-logistics-btn");
            if (logisticsBtn) {
                logisticsBtn.href = `/Logistics?orderId=${encodeURIComponent(order.id)}`;
            }

            orderDetailDialog.showModal();
        } catch (error) {
            notify(`Falha ao carregar detalhes do pedido: ${error.message}`, true);
        }
    }

    async function openScheduleCreateModal(order) {
        if (!scheduleCreateDialog) return;
        scheduleCreateForm.reset();
        scheduleCreateForm.orderId.value = order.id;
        document.querySelector("#schedule-create-order-number").textContent = order.orderNumber;
        scheduleCreateForm.querySelector(".form-error").textContent = "";

        const tomorrow = new Date();
        tomorrow.setDate(tomorrow.getDate() + 1);
        scheduleCreateForm.plannedDate.value = `${tomorrow.getFullYear()}-${String(tomorrow.getMonth() + 1).padStart(2, "0")}-${String(tomorrow.getDate()).padStart(2, "0")}`;

        await loadLookups(scheduleCreateDialog);

        const eligibleItems = (order.items || []).filter(i => (i.eligibleScheduleBalance || 0) > 0);
        const tbody = document.querySelector("#schedule-create-items-body");
        tbody.innerHTML = eligibleItems.map(item => `
            <tr data-order-item-id="${item.id}" data-unit="${escapeHtml(item.unit)}">
                <td><strong>${escapeHtml(item.productName || item.productId)}</strong></td>
                <td>${escapeHtml(item.unit)}</td>
                <td>${Number(item.quantity).toLocaleString("pt-BR")}</td>
                <td>${Number(item.scheduledQuantity || 0).toLocaleString("pt-BR")}</td>
                <td style="color:var(--accent);font-weight:700">${Number(item.eligibleScheduleBalance).toLocaleString("pt-BR")}</td>
                <td>
                    <input type="number" class="schedule-item-qty" step="0.0001" min="0" max="${item.eligibleScheduleBalance}" value="${item.eligibleScheduleBalance}" style="width:120px" required />
                </td>
            </tr>
        `).join("");

        scheduleCreateDialog.showModal();
    }

    scheduleCreateForm.onsubmit = async event => {
        event.preventDefault();
        const errElement = scheduleCreateForm.querySelector(".form-error");
        errElement.textContent = "";

        const orderId = scheduleCreateForm.orderId.value;
        const rows = document.querySelectorAll("#schedule-create-items-body tr");
        const items = [];

        for (const row of rows) {
            const orderItemId = row.dataset.orderItemId;
            const qtyInput = row.querySelector(".schedule-item-qty");
            const quantity = parseFloat(qtyInput?.value) || 0;
            if (quantity > 0) {
                items.push({ orderItemId, quantity, unit: row.dataset.unit });
            }
        }

        if (!items.length) {
            errElement.textContent = "Informe ao menos uma quantidade positiva para programar.";
            return;
        }

        const content = {
            plannedDate: scheduleCreateForm.plannedDate.value,
            destination: scheduleCreateForm.destination.value.trim(),
            responsibleId: scheduleCreateForm.responsibleId.value || null,
            notes: scheduleCreateForm.notes.value.trim() || null,
            items
        };
        const storageKey = `agro360.commercial.schedule.create.${orderId}`;
        const payload = { ...content, idempotencyKey: rememberIntent(storageKey, content) };
        const submitButton = scheduleCreateForm.querySelector("button[type=submit]");
        submitButton.disabled = true;

        try {
            await request(`/api/commercial/orders/${orderId}/schedules`, {
                method: "POST",
                body: JSON.stringify(payload)
            });
            forgetIntent(storageKey);
            scheduleCreateDialog.close();
            notify("Programação de entrega criada com sucesso.");
            await openOrderDetails(orderId);
            await load();
        } catch (error) {
            errElement.textContent = error.status === 409
                ? `${error.message} Os dados digitados foram mantidos. Atualize o pedido se outro usuário o alterou.`
                : error.message;
            notify(errMessage(error), true);
        } finally {
            submitButton.disabled = false;
        }
    };

    function errMessage(error) {
        return error.message || "Falha na operação.";
    }

    async function openScheduleRescheduleModal(schedule, order) {
        if (!scheduleRescheduleDialog) return;
        scheduleRescheduleForm.reset();
        scheduleRescheduleForm.scheduleId.value = schedule.id;
        scheduleRescheduleForm.expectedVersion.value = schedule.version;
        document.querySelector("#reschedule-number").textContent = schedule.scheduleNumber;
        document.querySelector("#reschedule-version").textContent = schedule.version;
        scheduleRescheduleForm.querySelector(".form-error").textContent = "";

        scheduleRescheduleForm.plannedDate.value = civilDate(schedule.plannedDate);
        if (schedule.destination) scheduleRescheduleForm.destination.value = schedule.destination;

        await loadLookups(scheduleRescheduleDialog);
        if (schedule.responsibleId) scheduleRescheduleForm.responsibleId.value = schedule.responsibleId;

        const tbody = document.querySelector("#schedule-reschedule-items-body");
        tbody.innerHTML = (schedule.items || []).map(item => {
            const orderItem = (order.items || []).find(oi => oi.id === item.orderItemId);
            const eligible = orderItem ? orderItem.eligibleScheduleBalance : 0;
            const maxAllowed = Math.round((item.quantity + eligible) * 10000) / 10000;
            const minAllowed = item.dispatchedQuantity || 0;

            return `
            <tr data-schedule-item-id="${item.id}" data-order-item-id="${item.orderItemId}">
                <td><strong>${escapeHtml(item.productName || item.productId)}</strong></td>
                <td>${escapeHtml(item.unit)}</td>
                <td>${Number(item.quantity).toLocaleString("pt-BR")}</td>
                <td>${Number(item.dispatchedQuantity || 0).toLocaleString("pt-BR")}</td>
                <td>${Number(eligible).toLocaleString("pt-BR")}</td>
                <td>
                    <input type="number" class="reschedule-item-qty" step="0.0001" min="${minAllowed}" max="${maxAllowed}" value="${item.quantity}" style="width:120px" required />
                </td>
            </tr>`;
        }).join("");

        scheduleRescheduleDialog.showModal();
    }

    scheduleRescheduleForm.onsubmit = async event => {
        event.preventDefault();
        const errElement = scheduleRescheduleForm.querySelector(".form-error");
        errElement.textContent = "";

        const scheduleId = scheduleRescheduleForm.scheduleId.value;
        const expectedVersion = parseInt(scheduleRescheduleForm.expectedVersion.value, 10);
        const rows = document.querySelectorAll("#schedule-reschedule-items-body tr");
        const items = [];

        for (const row of rows) {
            const scheduleItemId = row.dataset.scheduleItemId;
            const qtyInput = row.querySelector(".reschedule-item-qty");
            const quantity = parseFloat(qtyInput?.value) || 0;
            items.push({ scheduleItemId, quantity });
        }

        const content = {
            plannedDate: scheduleRescheduleForm.plannedDate.value,
            reason: scheduleRescheduleForm.reason.value.trim(),
            expectedVersion,
            destination: scheduleRescheduleForm.destination.value.trim() || null,
            responsibleId: scheduleRescheduleForm.responsibleId.value || null,
            replaceResponsible: true,
            items
        };
        const storageKey = `agro360.commercial.schedule.reschedule.${scheduleId}`;
        const payload = { ...content, idempotencyKey: rememberIntent(storageKey, content) };
        const submitButton = scheduleRescheduleForm.querySelector("button[type=submit]");
        submitButton.disabled = true;

        try {
            await request(`/api/commercial/schedules/${scheduleId}/reschedule`, {
                method: "PUT",
                body: JSON.stringify(payload)
            });
            forgetIntent(storageKey);
            scheduleRescheduleDialog.close();
            notify("Entrega reprogramada com sucesso.");
            if (currentDetailedOrder) await openOrderDetails(currentDetailedOrder.id);
            await load();
        } catch (error) {
            errElement.textContent = error.status === 409
                ? `${error.message} Os dados digitados foram mantidos.`
                : error.message;
            if (error.status === 409) {
                const refresh = document.createElement("button");
                refresh.type = "button";
                refresh.className = "btn-sm btn-outline";
                refresh.textContent = "Atualizar dados do servidor";
                refresh.onclick = () => openOrderDetails(currentDetailedOrder?.id || orderIdFromSchedule());
                errElement.append(" ", refresh);
            }
            notify(error.message, true);
        } finally {
            submitButton.disabled = false;
        }
    };

    function orderIdFromSchedule() {
        return scheduleCreateForm?.orderId?.value || "";
    }

    function openScheduleCancelModal(schedule, order) {
        if (!scheduleCancelDialog) return;
        scheduleCancelForm.reset();
        scheduleCancelForm.scheduleId.value = schedule.id;
        scheduleCancelForm.expectedVersion.value = schedule.version;
        document.querySelector("#cancel-schedule-number").textContent = schedule.scheduleNumber;
        scheduleCancelForm.querySelector(".form-error").textContent = "";
        scheduleCancelDialog.showModal();
    }

    scheduleCancelForm.onsubmit = async event => {
        event.preventDefault();
        const errElement = scheduleCancelForm.querySelector(".form-error");
        errElement.textContent = "";

        const scheduleId = scheduleCancelForm.scheduleId.value;
        const expectedVersion = parseInt(scheduleCancelForm.expectedVersion.value, 10);
        const reason = scheduleCancelForm.reason.value.trim();
        const content = { reason, expectedVersion };
        const storageKey = `agro360.commercial.schedule.cancel.${scheduleId}`;
        const submitButton = scheduleCancelForm.querySelector("button[type=submit]");
        submitButton.disabled = true;

        try {
            await request(`/api/commercial/schedules/${scheduleId}/cancel`, {
                method: "POST",
                body: JSON.stringify({ ...content, idempotencyKey: rememberIntent(storageKey, content) })
            });
            forgetIntent(storageKey);
            scheduleCancelDialog.close();
            notify("Programação de entrega cancelada.");
            if (currentDetailedOrder) await openOrderDetails(currentDetailedOrder.id);
            await load();
        } catch (error) {
            errElement.textContent = error.message;
            notify(error.message, true);
        } finally {
            submitButton.disabled = false;
        }
    };

    function openScheduleRevisionsModal(schedule) {
        if (!scheduleRevisionsDialog) return;
        document.querySelector("#revisions-modal-title").textContent = `Histórico - ${schedule.scheduleNumber}`;
        document.querySelector("#revisions-modal-subtitle").textContent = `${(schedule.revisions || []).length} revisão(ões) registrada(s)`;

        const revContent = document.querySelector("#schedule-revisions-content");
        if (schedule.revisions && schedule.revisions.length) {
            revContent.innerHTML = `
                <table class="items-table">
                    <thead>
                        <tr>
                            <th>Versão</th>
                            <th>Quando</th>
                            <th>Quem</th>
                            <th>Motivo</th>
                            <th>Data anterior</th>
                            <th>Nova data</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${schedule.revisions.map(r => `
                            <tr>
                                <td><span class="version-chip">V${r.version}</span></td>
                                <td>${formatCivilDate(r.createdAt)} ${new Date(r.createdAt).toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit" })}</td>
                                <td>${escapeHtml(r.actorName || "Usuário")}</td>
                                <td><strong>${escapeHtml(r.reason)}</strong></td>
                                <td>${formatCivilDate(r.previousDate)}</td>
                                <td>${formatCivilDate(r.newDate)}</td>
                            </tr>
                        `).join("")}
                    </tbody>
                </table>`;
        } else {
            revContent.innerHTML = '<p class="empty-state">Nenhuma revisão auditável registrada.</p>';
        }
        scheduleRevisionsDialog.showModal();
    }

    window.addEventListener("agro360:session", event => {
        const session = event.detail;
        const newTenantId = session?.tenantId || null;
        if (currentTenantId !== null && currentTenantId !== newTenantId) {
            if (pendingAbortController) {
                pendingAbortController.abort();
            }
            availableProducts = [];
            [commercialDialog, proposalDialog, proposalDetailDialog, proposalAcceptDialog, proposalConvertDialog, orderDetailDialog, scheduleCreateDialog, scheduleRescheduleDialog, scheduleCancelDialog, scheduleRevisionsDialog].forEach(d => {
                if (d && d.open) d.close();
            });
            notify("Organização alterada. Recarregando contexto comercial...");
            page = 1;
            load();
        }
        currentTenantId = newTenantId;
    });

    // Fechar dialogs
    document.querySelectorAll("dialog [data-close]").forEach(btn => {
        btn.onclick = () => {
            btn.closest("dialog")?.close();
        };
    });

    // Submissão do form de cliente / prospect
    commercialForm.onsubmit = async event => {
        event.preventDefault();
        if (!commercialForm.reportValidity()) return;

        const data = Object.fromEntries(new FormData(commercialForm));
        for (const key of ["representativeId", "taxDocument", "email", "phone", "notes"]) {
            if (!data[key]) data[key] = null;
        }

        try {
            await request("/api/commercial/customers", { method: "POST", body: JSON.stringify(data) });
            commercialDialog.close();
            notify(`${data.type === "PROSPECT" ? "Prospect" : "Cliente"} salvo com sucesso.`);
            resource = data.type === "PROSPECT" ? "prospects" : "customers";
            updateCreateButton();
            await load();
        } catch (error) {
            commercialForm.querySelector(".form-error").textContent = error.message;
            notify(error.message, true);
        }
    };

    function updateCreateButton() {
        const createBtn = document.querySelector("#create-commercial");
        if (resource === "proposals") {
            createBtn.hidden = false;
            createBtn.textContent = "+ Nova proposta";
            createBtn.onclick = openProposalCreateModal;
        } else if (resource === "prospects") {
            createBtn.hidden = false;
            createBtn.textContent = "+ Novo prospect";
            createBtn.onclick = () => {
                commercialForm.reset();
                commercialForm.type.value = "PROSPECT";
                commercialForm.querySelector("h2").textContent = "Novo prospect";
                loadLookups(commercialDialog);
                commercialDialog.showModal();
            };
        } else if (resource === "customers" || resource === "dashboard") {
            createBtn.hidden = false;
            createBtn.textContent = "+ Novo cliente";
            createBtn.onclick = () => {
                commercialForm.reset();
                commercialForm.type.value = "CUSTOMER";
                commercialForm.querySelector("h2").textContent = "Novo cliente";
                loadLookups(commercialDialog);
                commercialDialog.showModal();
            };
        } else {
            createBtn.hidden = true;
        }
    }

    // Abas
    document.querySelectorAll("[data-resource]").forEach(button => {
        button.onclick = () => {
            document.querySelectorAll("[data-resource]").forEach(item => item.classList.remove("active"));
            button.classList.add("active");
            resource = button.dataset.resource;
            sessionStorage.setItem("agro360.commercial.resource", resource);
            updateCreateButton();
            page = 1;
            load();
        };
    });

    let searchTimer;
    document.querySelector("#commercial-search").oninput = () => {
        clearTimeout(searchTimer);
        searchTimer = setTimeout(() => {
            page = 1;
            load();
        }, 300);
    };

    document.querySelector("#commercial-status").onchange = () => {
        page = 1;
        load();
    };

    document.querySelector("#previous-page").onclick = () => {
        if (page > 1) {
            page--;
            load();
        }
    };

    document.querySelector("#next-page").onclick = () => {
        if (page * 20 < total) {
            page++;
            load();
        }
    };

    // Inicialização
    const urlParams = new URLSearchParams(window.location.search);
    const viewParam = urlParams.get("view");
    const orderIdParam = urlParams.get("orderId");
    if (viewParam === "orders" || orderIdParam) {
        resource = "orders";
        sessionStorage.setItem("agro360.commercial.resource", resource);
    }

    const initialTab = document.querySelector(`[data-resource="${resource}"]`) || document.querySelector('[data-resource="dashboard"]');
    if (initialTab) {
        document.querySelectorAll("[data-resource]").forEach(item => item.classList.remove("active"));
        initialTab.classList.add("active");
    }
    updateCreateButton();
    load().then(() => {
        if (orderIdParam) {
            openOrderDetails(orderIdParam);
        }
    });
})();
