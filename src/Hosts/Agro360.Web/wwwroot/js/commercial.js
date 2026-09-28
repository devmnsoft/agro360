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
        FULFILLMENT: "Faturamento/Separação",
        INVOICED: "Faturado",
        DELIVERED: "Entregue",
        RETURNED: "Devolvido",
        EXPECTED: "Previsto",
        PAID: "Pago"
    };

    function notify(message, isError = false) {
        toast.textContent = message;
        toast.style.background = isError ? "#8c2f39" : "#143d2c";
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

    async function request(path, options = {}) {
        const response = await fetch(`${api}${path}`, {
            ...options,
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
                const rows = await request(`/api/commercial/lookups/${lookupType}`);
                for (const item of rows) {
                    select.add(new Option(item.label, item.id));
                }
                if (current) select.value = current;
            } catch (error) {
                console.warn(`Falha ao carregar lookup ${lookupType}:`, error);
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
                document.querySelector("#commercial-metrics").innerHTML =
                    `<article><small>Clientes ativos</small><strong>${data.activeCustomers}</strong></article>` +
                    `<article><small>Pipeline</small><strong>${formatMoney(data.pipelineValue)}</strong></article>` +
                    `<article><small>Receita prevista</small><strong>${formatMoney(data.forecastRevenue)}</strong></article>` +
                    `<article><small>Comissões previstas</small><strong>${formatMoney(data.expectedCommissions)}</strong></article>`;
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
                        const parts = (item.detail || "").split(" · ");
                        const clientName = parts[0] || "—";
                        const currency = parts[1] || "BRL";
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
                    </tr>
                </thead>
                <tbody>
                    ${items.map(item => {
                        const parts = (item.detail || "").split(" · ");
                        const clientName = parts[0] || "—";
                        const currency = parts[1] || "BRL";
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
            `<option value="${p.id}" ${itemData && itemData.productId === p.id ? "selected" : ""}>${escapeHtml(p.label)}</option>`
        ).join("");

        tr.innerHTML = `
            <td>
                <select class="item-product" required>
                    <option value="">Selecione…</option>
                    ${productOptions}
                </select>
            </td>
            <td>
                <input class="item-unit" value="${escapeHtml(itemData?.unit || "SACAS")}" maxlength="20" required style="width:80px" />
            </td>
            <td>
                <input class="item-qty" type="number" step="0.0001" min="0.0001" value="${itemData?.quantity || 1}" required style="width:100px" />
            </td>
            <td>
                <input class="item-price" type="number" step="0.01" min="0.01" value="${itemData?.unitPrice || 100.00}" required style="width:110px" />
            </td>
            <td>
                <input class="item-discount" type="number" step="0.01" min="0" max="100" value="${itemData?.discountPercentage || 0}" style="width:80px" />
            </td>
            <td class="item-total-cell" style="font-weight:700">0,00</td>
            <td>
                <button type="button" class="btn-sm btn-danger remove-item-btn" title="Remover item">×</button>
            </td>
        `;

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
            await load();
        } catch (error) {
            submitBtn.disabled = false;
            errElement.textContent = error.message;
            notify(error.message, true);
        }
    };

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
    const initialTab = document.querySelector(`[data-resource="${resource}"]`) || document.querySelector('[data-resource="dashboard"]');
    if (initialTab) {
        document.querySelectorAll("[data-resource]").forEach(item => item.classList.remove("active"));
        initialTab.classList.add("active");
    }
    updateCreateButton();
    load();
})();
