(() => {
    "use strict";
    const app = document.getElementById("fleet-app");
    if (!app) return;
    const apiBase = document.querySelector('meta[name="api-base"]')?.content?.replace(/\/$/, "") ?? "http://localhost:8081";
    const dialog = document.getElementById("fleet-dialog");
    const form = document.getElementById("fleet-form");
    const fields = document.getElementById("dialog-fields");
    const errorBox = document.getElementById("dialog-error");
    const content = document.getElementById("content");
    const toast = document.getElementById("fleet-toast");
    const helps = {
        overview: ["Painel de disponibilidade, OS, preventiva e custos.", "Ativos e permissões de frota/manutenção.", "Atualize e abra a origem nas abas.", "Não grava dados.", "Se um indicador estiver vazio, não há registros suficientes — não é zero conclusivo."],
        assets: ["Cadastro unificado de máquinas, veículos, implementos e estacionários.", "Tipo de ativo e código operacional.", "Placa só quando aplicável. Situação cadastral ≠ ocupação.", "Gera histórico do ativo.", "Inativação preserva OS, custos e abastecimentos."],
        agenda: ["Reservas e conflitos de uso.", "Ativo cadastralmente ativo.", "Confirme o período; conflitos são validados no backend.", "Marca ocupação RESERVED quando livre.", "Cancelar libera só a reserva vigente."],
        readings: ["Leituras de odômetro/horímetro ordenadas pela data do evento.", "Medidor habilitado no ativo.", "Reinicialização é evento explícito com justificativa.", "Atualiza o estado atual sem reescrever custos encerrados.", "Corrija com nova leitura auditada."],
        plans: ["Preventiva por data, horas, km ou primeiro critério.", "Ativo e periodicidade positiva.", "Avalie planos para gerar alertas sem duplicar OS.", "Não altera OS existentes silenciosamente.", "Cancelar OS não cumpre a manutenção."],
        requests: ["Solicitação corretiva com triagem automática em OS.", "Ativo apto.", "Marque se bloqueia o equipamento.", "Cria OS e bloqueio operacional quando necessário.", "Reprovação/cancelamento exigem motivo."],
        orders: ["Ordens preventivas/corretivas até inspeção.", "OS aberta.", "Transições no backend; conclusão exige inspeção aprovada.", "Serviço executado ≠ inspeção ≠ disponível.", "Reabertura é autorizada e auditada."],
        parts: ["Reserva, consumo e devolução vinculados à OS.", "Depósito e produto aprovados.", "Planejamento não movimenta estoque; reserva ≠ consumo.", "Baixa única e rastreável.", "Peça removida não volta como nova automaticamente."],
        fuel: ["Abastecimento interno (baixa estoque) e externo (sem baixa).", "Combustível e quantidade positiva.", "Tanque cheio quando usar capacidade.", "Custo com origem REFUELING sem duplicar.", "Não calcule km/l com um único ponto isolado."],
        costs: ["Custos por tipo e período com origem.", "Abastecimentos e peças lançados.", "Consulte o detalhamento.", "Sem rateio fictício.", "Falta de dados ≠ zero."],
        reports: ["CSV do conjunto filtrado autorizado.", "Filtros da tela.", "Protege fórmula e separadores.", "Não grava.", "Arquivo vazio significa filtro sem registros."]
    };
    let tab = new URLSearchParams(location.search).get("tab") || "overview";
    let action = "asset";
    let toastTimer = 0;
    let selectedAsset = null;

    const session = () => { try { return JSON.parse(localStorage.getItem("agro360.session")); } catch { return null; } };
    const escapeHtml = value => String(value ?? "").replace(/[&<>"']/g, ch => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[ch]));
    async function request(path, options = {}) {
        const headers = new Headers(options.headers ?? {});
        const token = session()?.accessToken ?? localStorage.getItem("agro360.accessToken");
        if (token) headers.set("Authorization", `Bearer ${token}`);
        if (options.body) headers.set("Content-Type", "application/json");
        const response = await fetch(`${apiBase}/api/fleet${path}`, { ...options, headers });
        if (response.status === 401) throw new Error("Sua sessão expirou. Entre novamente.");
        if (!response.ok) {
            const problem = await response.json().catch(() => ({}));
            throw new Error(Object.values(problem.errors ?? {}).flat().join(" ") || problem.detail || problem.title || `Falha HTTP ${response.status}.`);
        }
        if (response.status === 204) return null;
        const type = response.headers.get("content-type") || "";
        if (type.includes("csv")) return response.blob();
        return response.json();
    }
    function notify(message, isError = false) {
        clearTimeout(toastTimer);
        toast.textContent = message;
        toast.classList.toggle("error", isError);
        toast.hidden = false;
        toastTimer = window.setTimeout(() => { toast.hidden = true; }, 5000);
    }
    function setHelp() {
        const texts = helps[tab] || helps.overview;
        const labels = ["Para que serve", "O que precisa estar cadastrado", "Como realizar", "O que muda no sistema", "Como corrigir"];
        document.getElementById("tab-help").innerHTML = texts.map((text, i) => `<p><strong>${labels[i]}:</strong> ${text}</p>`).join("");
    }
    function statusPill(value) {
        const danger = /MAINTENANCE|UNAVAILABLE|WAITING|REJECTED|CANCELLED/.test(value || "");
        const warn = /RESERVED|INSPECTION|DUE|PENDING/.test(value || "");
        return `<span class="status-pill${danger ? " danger" : warn ? " warn" : ""}">${escapeHtml(value ?? "—")}</span>`;
    }
    function table(headers, rows, empty) {
        if (!rows?.length) return `<p class="empty-state">${empty}</p>`;
        return `<div class="table-wrap"><table><thead><tr>${headers.map(h => `<th>${h}</th>`).join("")}</tr></thead><tbody>${rows.join("")}</tbody></table></div>`;
    }
    async function loadDashboard() {
        const d = await request("/dashboard");
        document.getElementById("metrics").innerHTML = [
            ["Disponíveis", d.availableAssets], ["Em operação", d.operatingAssets], ["Em manutenção", d.maintenanceAssets],
            ["OS abertas", d.openWorkOrders], ["Aguardando peça", d.waitingParts], ["Bloqueios ativos", d.activeBlocks],
            ["Preventivas vencidas", d.overdueMaintenances], ["Reservas", d.activeReservations],
            ["Combustível no mês", `${Number(d.monthFuelQuantity || 0).toLocaleString("pt-BR")} L`],
            ["Custo no mês", Number(d.totalCost || 0).toLocaleString("pt-BR", { style: "currency", currency: "BRL" })],
            ["Disponibilidade", `${Number(d.availabilityPercent || 0).toFixed(1)}%`], ["Paradas abertas", d.openDowntimes]
        ].map(([label, value]) => `<article><small>${label}</small><strong>${escapeHtml(value)}</strong></article>`).join("");
        return d;
    }

    async function render() {
        setHelp();
        content.innerHTML = `<p class="empty-state">Carregando…</p>`;
        document.querySelectorAll(".fleet-tabs button").forEach(button => button.classList.toggle("active", button.dataset.tab === tab));
        document.getElementById("primary-action").hidden = ["overview", "costs", "reports", "agenda"].includes(tab);
        try {
            await loadDashboard();
            const q = new URLSearchParams({ page: "1", pageSize: "50" });
            if (document.getElementById("search").value) q.set("search", document.getElementById("search").value);
            if (document.getElementById("status-filter").value) q.set("status", document.getElementById("status-filter").value);
            if (tab === "overview") {
                content.innerHTML = `<div class="preview-box"><p>Disponibilidade e custos usam o período corrente. Serviço executado, inspeção aprovada e equipamento disponível são etapas distintas.</p><p>Use as abas para abrir os registros de origem de cada indicador.</p></div>`;
                return;
            }
            if (tab === "assets") {
                const rows = await request(`/assets?${q}`);
                content.innerHTML = table(["Código", "Nome", "Tipo", "Operacional", "Placa", "Medidores", "Ações"],
                    (rows ?? []).map(x => `<tr>
                        <td>${escapeHtml(x.internalCode)}</td><td>${escapeHtml(x.name)}</td><td>${escapeHtml(x.type)}</td>
                        <td>${statusPill(x.status)}</td><td>${escapeHtml(x.plate || "—")}</td>
                        <td>${x.odometer} km · ${x.hourMeter} h</td>
                        <td><button type="button" data-detail="${x.id}">Detalhe</button></td></tr>`),
                    "Nenhum equipamento autorizado. Cadastre o primeiro ativo.");
                return;
            }
            if (tab === "agenda") {
                content.innerHTML = `<div class="preview-box"><p>Selecione um equipamento em Equipamentos → Detalhe para consultar disponibilidade e conflitos do período.</p><p>Reservas incompatíveis e bloqueios não dispensáveis impedem a confirmação.</p></div>`;
                return;
            }
            if (tab === "readings") {
                if (!selectedAsset) { content.innerHTML = `<p class="empty-state">Abra o detalhe de um equipamento para ver leituras.</p>`; return; }
                const rows = await request(`/assets/${selectedAsset}/readings`);
                content.innerHTML = table(["Quando", "Medidor", "Físico", "Acumulado", "Origem", "Reset"],
                    (rows ?? []).map(x => `<tr><td>${escapeHtml(x.occurredAt)}</td><td>${escapeHtml(x.meterKind)}</td><td>${x.physicalValue}</td><td>${x.operationalAccumulated}</td><td>${escapeHtml(x.origin)}</td><td>${x.isReset ? "Sim" : "Não"}</td></tr>`),
                    "Sem leituras. Registre a primeira medição.");
                return;
            }
            if (tab === "plans") {
                const rows = await request("/maintenance-plans");
                content.innerHTML = `<div class="preview-box"><button type="button" id="evaluate-plans" class="secondary-button">Avaliar preventivas</button></div>` +
                    table(["Ativo", "Tipo", "Política", "Próxima", "Próximo medidor", "Status"],
                        (rows ?? []).map(x => `<tr><td>${escapeHtml(x.assetName)}</td><td>${escapeHtml(x.maintenanceType)}</td><td>${escapeHtml(x.duePolicy)}</td><td>${escapeHtml(x.nextExecutionAt || "—")}</td><td>${x.nextMeter ?? "—"}</td><td>${statusPill(x.status)}</td></tr>`),
                        "Nenhum plano preventivo.");
                document.getElementById("evaluate-plans")?.addEventListener("click", async () => {
                    try { await request("/maintenance-plans/evaluate", { method: "POST", body: "{}" }); notify("Preventivas avaliadas. Alertas sem OS duplicada."); }
                    catch (e) { notify(e.message, true); }
                });
                return;
            }
            if (tab === "requests") {
                const rows = await request("/requests");
                content.innerHTML = table(["Ativo", "Classe", "Severidade", "Problema", "Bloqueia", "Status"],
                    (rows ?? []).map(x => `<tr><td>${escapeHtml(x.assetName)}</td><td>${escapeHtml(x.defectClass)}</td><td>${statusPill(x.severity)}</td><td>${escapeHtml(x.problemDescription)}</td><td>${x.blocksAsset ? "Sim" : "Não"}</td><td>${statusPill(x.status)}</td></tr>`),
                    "Nenhuma solicitação corretiva.");
                return;
            }
            if (tab === "orders") {
                const rows = await request(`/work-orders?${q}`);
                content.innerHTML = table(["Código", "Ativo", "Tipo", "Prioridade", "Status", "Prazo", "Ações"],
                    (rows ?? []).map(x => `<tr><td>${escapeHtml(x.code)}</td><td>${escapeHtml(x.assetName)}</td><td>${escapeHtml(x.type)}</td><td>${statusPill(x.priority)}</td><td>${statusPill(x.status)}</td><td>${x.dueAt ? new Date(x.dueAt).toLocaleDateString("pt-BR") : "—"}</td><td><button type="button" data-order="${x.id}">Abrir</button></td></tr>`),
                    "Nenhuma ordem de serviço.");
                return;
            }
            if (tab === "parts") {
                content.innerHTML = `<div class="preview-box"><p>Abra uma OS para reservar, consumir ou devolver peças. Planejamento não movimenta estoque; reserva não é consumo.</p></div>`;
                return;
            }
            if (tab === "fuel") {
                const rows = await request("/refuelings");
                content.innerHTML = table(["Quando", "Ativo", "Origem", "Qtd", "Total", "Tanque cheio"],
                    (rows ?? []).map(x => `<tr><td>${escapeHtml(x.occurredAt)}</td><td>${escapeHtml(x.assetName)}</td><td>${escapeHtml(x.source)}</td><td>${x.quantity}</td><td>${x.totalValue}</td><td>${x.tankFull ? "Sim" : "Não"}</td></tr>`),
                    "Nenhum abastecimento.");
                return;
            }
            if (tab === "costs") {
                const rows = await request("/costs");
                content.innerHTML = table(["Tipo", "Valor", "Lançamentos", "Período"],
                    (rows ?? []).map(x => `<tr><td>${escapeHtml(x.costType)}</td><td>${x.value}</td><td>${x.entries}</td><td>${escapeHtml(x.periodStart)} → ${escapeHtml(x.periodEnd)}</td></tr>`),
                    "Sem custos no período. Falta de dados não é apresentada como zero conclusivo.");
                return;
            }
            if (tab === "reports") {
                content.innerHTML = `<div class="preview-box"><p>A exportação usa o conjunto filtrado autorizado, não só a página atual.</p></div>`;
            }
        } catch (error) {
            content.innerHTML = `<p class="empty-state">${escapeHtml(error.message)}</p>`;
            notify(error.message, true);
        }
    }

    async function lookup(kind) { return request(`/lookups/${kind}`); }
    const select = (name, label, rows, required = true) => `<label>${label}<select name="${name}" ${required ? "required" : ""}><option value="">Selecione</option>${(rows ?? []).map(x => `<option value="${x.id}">${escapeHtml(x.name)}</option>`).join("")}</select></label>`;

    async function openForm() {
        errorBox.textContent = "";
        const map = { assets: "asset", readings: "reading", plans: "plan", requests: "request", orders: "order", fuel: "fuel", parts: "part" };
        action = map[tab] || "asset";
        document.getElementById("dialog-title").textContent = { asset: "Novo equipamento", reading: "Nova leitura", plan: "Plano preventivo", request: "Solicitação corretiva", order: "Nova OS", fuel: "Abastecimento", part: "Reservar peça" }[action];
        try {
            if (action === "asset") {
                const [types, properties, operators] = await Promise.all(["asset-types", "properties", "operators"].map(lookup));
                fields.innerHTML = `
                    <label>Código operacional<input name="internalCode" required maxlength="40" data-help="Único no cliente. Não use GUID." /></label>
                    <label>Nome<input name="name" required maxlength="160" /></label>
                    ${select("assetTypeId", "Tipo", types)}
                    <label>Situação cadastral<select name="cadastralStatus" data-help="Independente da ocupação na agenda."><option value="ACTIVE">Ativo</option><option value="INACTIVE">Inativo</option><option value="WRITTEN_OFF">Baixado</option><option value="SOLD">Vendido</option></select></label>
                    <label>Status operacional<select name="status" data-help="Disponibilidade operacional atual."><option value="AVAILABLE">Disponível</option><option value="OPERATING">Em operação</option><option value="MAINTENANCE">Manutenção</option><option value="UNAVAILABLE">Indisponível</option><option value="RESERVED">Reservado</option></select></label>
                    <label>Propriedade/locação<select name="ownership"><option value="OWNED">Próprio</option><option value="LEASED">Arrendado</option><option value="RENTED">Alugado</option><option value="THIRD_PARTY">Terceiro</option></select></label>
                    <label>Marca<input name="brand" maxlength="80" /></label>
                    <label>Modelo<input name="model" maxlength="80" /></label>
                    <label>Ano<input name="year" type="number" min="1900" max="2200" /></label>
                    <label>Nº de série / fabricação<input name="serialNumber" maxlength="80" /></label>
                    <label>Placa<input name="plate" maxlength="20" data-help="Opcional para implementos e estacionários." /></label>
                    ${select("propertyId", "Unidade/propriedade", properties, false)}
                    ${select("mainOperatorId", "Responsável operacional", operators, false)}
                    <label>Entrada em operação<input name="commissionedOn" type="date" /></label>
                    <label>Fonte de energia<input name="energySource" maxlength="40" placeholder="DIESEL, ELÉTRICO…" data-help="Opcional. Compatível com abastecimento." /></label>
                    <label>Odômetro (km)<input name="odometer" type="number" min="0" step="0.01" value="0" data-help="Deixe 0 se o equipamento não possui odômetro." /></label>
                    <label>Horímetro (h)<input name="hourMeter" type="number" min="0" step="0.01" value="0" data-help="Deixe 0 se não houver horímetro." /></label>
                    <label>Capacidade combustível<input name="fuelCapacity" type="number" min="0" step="0.001" data-help="Unidade do combustível cadastrado." /></label>
                    <label class="wide">Observações / histórico<textarea name="notes"></textarea></label>`;
            } else if (action === "reading") {
                const assets = await lookup("assets");
                fields.innerHTML = `${select("assetId", "Equipamento", assets)}
                    <label>Medidor<select name="meterKind" required><option value="HOUR_METER">Horímetro</option><option value="ODOMETER">Odômetro</option><option value="ENGINE_HOURS">Horas de motor</option></select></label>
                    <label>Valor<input name="physicalValue" type="number" min="0" step="0.01" required /></label>
                    <label>Unidade<input name="unit" value="h" required /></label>
                    <label>Data/hora<input name="occurredAt" type="datetime-local" required /></label>
                    <label>Reinicialização?<select name="isReset"><option value="false">Não</option><option value="true">Sim, evento explícito</option></select></label>
                    <label class="wide">Justificativa<textarea name="justification" data-help="Obrigatória em reinicialização ou redução autorizada."></textarea></label>`;
            } else if (action === "request") {
                const assets = await lookup("assets");
                fields.innerHTML = `${select("assetId", "Equipamento", assets)}
                    <label>Classe do defeito<input name="defectClass" required maxlength="80" /></label>
                    <label>Severidade<select name="severity"><option>MEDIUM</option><option>HIGH</option><option>CRITICAL</option><option>LOW</option></select></label>
                    <label class="wide">Descrição<textarea name="problemDescription" required></textarea></label>
                    <label><input type="checkbox" name="blocksAsset" /> Bloqueia o equipamento</label>`;
            } else if (action === "order") {
                const assets = await lookup("assets");
                fields.innerHTML = `${select("assetId", "Equipamento", assets)}
                    <label>Tipo<select name="type"><option value="PREVENTIVE">Preventiva</option><option value="CORRECTIVE">Corretiva</option><option value="INSPECTION">Inspeção</option></select></label>
                    <label>Prioridade<select name="priority"><option>MEDIUM</option><option>HIGH</option><option>CRITICAL</option><option>LOW</option></select></label>
                    <label>Prazo<input name="dueAt" type="datetime-local" /></label>
                    <label class="wide">Descrição<textarea name="description" required></textarea></label>
                    <label><input type="checkbox" name="blockAsset" /> Bloquear ativo</label>`;
            } else if (action === "fuel") {
                const [assets, fuels, warehouses, products] = await Promise.all(["assets", "fuel-types", "warehouses", "products"].map(lookup));
                fields.innerHTML = `${select("assetId", "Equipamento", assets)}${select("fuelTypeId", "Combustível", fuels)}
                    <label>Origem<select name="source"><option value="INTERNAL">Interno (baixa estoque)</option><option value="EXTERNAL">Externo (sem baixa)</option></select></label>
                    <label>Quantidade<input name="quantity" type="number" min="0.001" step="0.001" required /></label>
                    <label>Valor unitário<input name="unitPrice" type="number" min="0" step="0.0001" required /></label>
                    <label>Data/hora<input name="occurredAt" type="datetime-local" required /></label>
                    ${select("warehouseId", "Depósito (interno)", warehouses, false)}
                    ${select("productId", "Produto (interno)", products, false)}
                    <label><input type="checkbox" name="tankFull" /> Tanque completo</label>
                    <label class="wide">Observações<textarea name="notes"></textarea></label>`;
            } else if (action === "plan") {
                const assets = await lookup("assets");
                fields.innerHTML = `${select("assetId", "Equipamento", assets)}
                    <label>Tipo<input name="maintenanceType" required maxlength="80" /></label>
                    <label>Controle<select name="controlUnit" data-help="Sem leitura válida não há previsão por horímetro."><option value="HOUR_METER">Horímetro</option><option value="ODOMETER">Odômetro</option><option value="DATE">Data</option></select></label>
                    <label>Periodicidade<input name="periodicity" type="number" min="0.01" step="0.01" required /></label>
                    <label>Política de vencimento<select name="duePolicy" data-help="FIRST_CRITERION vence no primeiro critério atingido."><option value="FIRST_CRITERION">Primeiro critério</option><option value="CALENDAR_FIXED">Calendário fixo</option><option value="METER_FIXED">Medidor fixo</option></select></label>
                    <label>Status<select name="status"><option>ACTIVE</option><option>INACTIVE</option></select></label>
                    <label>Próxima data<input name="nextExecutionAt" type="datetime-local" /></label>
                    <label>Próximo medidor<input name="nextMeter" type="number" min="0" step="0.01" /></label>
                    <label>Custo estimado<input name="estimatedCost" type="number" min="0" step="0.01" value="0" /></label>
                    <label class="wide">Descrição<textarea name="description" required></textarea></label>
                    <label class="wide">Checklist (um item por linha)<textarea name="checklist" required>Inspeção visual&#10;Lubrificação</textarea></label>`;
            } else {
                fields.innerHTML = `<p class="wide">Abra uma OS para gerenciar peças nesta versão da tela.</p>`;
            }
            dialog.showModal();
        } catch (e) { notify(e.message, true); }
    }

    form.addEventListener("submit", async event => {
        event.preventDefault();
        if (!form.reportValidity()) return;
        errorBox.textContent = "";
        const raw = Object.fromEntries(new FormData(form));
        const button = form.querySelector('[type="submit"]');
        button.disabled = true;
        try {
            if (action === "asset") {
                await request("/assets", { method: "POST", body: JSON.stringify({
                    internalCode: raw.internalCode, name: raw.name, assetTypeId: raw.assetTypeId, status: raw.status,
                    cadastralStatus: raw.cadastralStatus || "ACTIVE", ownership: raw.ownership || "OWNED",
                    brand: raw.brand || null, model: raw.model || null,
                    year: raw.year ? Number(raw.year) : null, serialNumber: raw.serialNumber || null,
                    plate: raw.plate || null, propertyId: raw.propertyId || null, mainOperatorId: raw.mainOperatorId || null,
                    commissionedOn: raw.commissionedOn || null, energySource: raw.energySource || null,
                    odometer: Number(raw.odometer || 0), hourMeter: Number(raw.hourMeter || 0),
                    fuelCapacity: raw.fuelCapacity ? Number(raw.fuelCapacity) : null, notes: raw.notes || null
                }) });
            } else if (action === "reading") {
                await request("/readings", { method: "POST", body: JSON.stringify({
                    assetId: raw.assetId, meterKind: raw.meterKind, physicalValue: Number(raw.physicalValue), unit: raw.unit,
                    occurredAt: new Date(raw.occurredAt).toISOString(), origin: "MANUAL", isReset: raw.isReset === "true",
                    justification: raw.justification || null
                }) });
                selectedAsset = raw.assetId;
            } else if (action === "request") {
                await request("/requests", { method: "POST", body: JSON.stringify({
                    assetId: raw.assetId, defectClass: raw.defectClass, severity: raw.severity,
                    problemDescription: raw.problemDescription, blocksAsset: raw.blocksAsset === "on"
                }) });
            } else if (action === "order") {
                await request("/work-orders", { method: "POST", body: JSON.stringify({
                    assetId: raw.assetId, type: raw.type, priority: raw.priority,
                    dueAt: raw.dueAt ? new Date(raw.dueAt).toISOString() : null,
                    description: raw.description, blockAsset: raw.blockAsset === "on"
                }) });
            } else if (action === "fuel") {
                await request("/refuelings/operational", { method: "POST", body: JSON.stringify({
                    assetId: raw.assetId, fuelTypeId: raw.fuelTypeId, source: raw.source,
                    quantity: Number(raw.quantity), unitPrice: Number(raw.unitPrice),
                    occurredAt: new Date(raw.occurredAt).toISOString(),
                    warehouseId: raw.warehouseId || null, productId: raw.productId || null,
                    tankFull: raw.tankFull === "on", notes: raw.notes || null
                }) });
            } else if (action === "plan") {
                await request("/maintenance-plans", { method: "POST", body: JSON.stringify({
                    assetId: raw.assetId, maintenanceType: raw.maintenanceType, description: raw.description,
                    periodicity: Number(raw.periodicity), controlUnit: raw.controlUnit,
                    duePolicy: raw.duePolicy || "FIRST_CRITERION",
                    nextExecutionAt: raw.nextExecutionAt ? new Date(raw.nextExecutionAt).toISOString() : null,
                    nextMeter: raw.nextMeter ? Number(raw.nextMeter) : null, status: raw.status,
                    estimatedCost: Number(raw.estimatedCost || 0),
                    checklist: String(raw.checklist || "").split("\n").map(x => x.trim()).filter(Boolean)
                }) });
            }
            dialog.close();
            notify("Operação registrada.");
            await render();
        } catch (error) {
            errorBox.textContent = error.message;
            notify(error.message, true);
        } finally {
            button.disabled = false;
        }
    });

    content.addEventListener("click", async event => {
        const detail = event.target.closest("[data-detail]");
        if (detail) {
            selectedAsset = detail.dataset.detail;
            try {
                const data = await request(`/assets/${selectedAsset}/detail`);
                const blocks = (data.blocks ?? []).map(b => `<p>${escapeHtml(b.kind)}: ${escapeHtml(b.reason)}${b.dispensable ? " (dispensável)" : " (impeditivo)"}</p>`).join("") || "<p>Sem bloqueios ativos.</p>";
                content.innerHTML = `<article class="preview-box">
                    <h3>${escapeHtml(data.asset.name)} (${escapeHtml(data.asset.internalCode)})</h3>
                    <p>Operacional ${statusPill(data.asset.status)} · Cadastral ${escapeHtml(data.asset.cadastralStatus)} · Tipo ${escapeHtml(data.asset.typeKind || data.asset.typeName)}</p>
                    <p>Por que indisponível:</p>${blocks}
                    <p><button type="button" data-tab-jump="readings">Ver leituras</button>
                    <button type="button" data-availability="${selectedAsset}">Consultar agenda</button>
                    <button type="button" id="back-list">Voltar</button></p>
                </article>`;
                document.getElementById("back-list")?.addEventListener("click", () => { tab = "assets"; render(); });
            } catch (e) { notify(e.message, true); }
            return;
        }
        const jump = event.target.closest("[data-tab-jump]");
        if (jump) { tab = jump.dataset.tabJump; render(); return; }
        const availability = event.target.closest("[data-availability]");
        if (availability) {
            const from = new Date().toISOString();
            const until = new Date(Date.now() + 7 * 86400000).toISOString();
            try {
                const rows = await request(`/assets/${availability.dataset.availability}/availability?from=${encodeURIComponent(from)}&until=${encodeURIComponent(until)}`);
                content.insertAdjacentHTML("afterbegin", `<div class="preview-box"><h4>Agenda (7 dias)</h4>${(rows ?? []).map(r => `<p>${escapeHtml(r.kind)} · ${escapeHtml(r.startsAt)} → ${escapeHtml(r.endsAt)} · ${escapeHtml(r.detail || r.status)}</p>`).join("") || "<p>Sem conflitos no período.</p>"}</div>`);
            } catch (e) { notify(e.message, true); }
            return;
        }
        const order = event.target.closest("[data-order]");
        if (order) {
            try {
                const data = await request(`/work-orders/${order.dataset.order}`);
                content.innerHTML = `<article class="preview-box">
                    <h3>${escapeHtml(data.order.code)} · ${escapeHtml(data.order.assetName)}</h3>
                    <p>Status ${statusPill(data.order.status)}. Bloqueia ativo: ${data.order.blocks_asset || data.order.blocksAsset ? "sim" : "não"}.</p>
                    <h4>Peças</h4>${(data.parts ?? []).map(p => `<p>${escapeHtml(p.description)} · reservado ${p.reserved_quantity ?? p.reservedQuantity ?? 0} · consumido ${p.consumed_quantity ?? p.consumedQuantity ?? 0}</p>`).join("") || "<p>Sem peças.</p>"}
                    <h4>Inspeções</h4>${(data.inspections ?? []).map(i => `<p>${escapeHtml(i.result)} · falhas impeditivas ${i.blockingFailures ?? i.blocking_failures ?? 0}</p>`).join("") || "<p>Sem inspeção.</p>"}
                    <p><button type="button" id="inspect-ok">Aprovar inspeção</button>
                    <button type="button" id="inspect-fail">Reprovar inspeção</button>
                    <button type="button" id="back-orders">Voltar</button></p>
                </article>`;
                document.getElementById("back-orders")?.addEventListener("click", () => { tab = "orders"; render(); });
                document.getElementById("inspect-ok")?.addEventListener("click", async () => {
                    try { await request(`/work-orders/${order.dataset.order}/inspect`, { method: "POST", body: JSON.stringify({ result: "APPROVED", checklist: ["OK"], blockingFailures: 0, notes: "Aprovado na homologação" }) }); notify("Inspeção aprovada."); render(); }
                    catch (e) { notify(e.message, true); }
                });
                document.getElementById("inspect-fail")?.addEventListener("click", async () => {
                    try { await request(`/work-orders/${order.dataset.order}/inspect`, { method: "POST", body: JSON.stringify({ result: "REJECTED", checklist: ["Item impeditivo"], blockingFailures: 1, notes: "Mantém bloqueio" }) }); notify("Inspeção reprovada; bloqueio mantido."); render(); }
                    catch (e) { notify(e.message, true); }
                });
            } catch (e) { notify(e.message, true); }
        }
    });

    document.getElementById("export").addEventListener("click", async () => {
        const kind = tab === "orders" ? "orders" : tab === "fuel" ? "refuelings" : "assets";
        try {
            const blob = await request(`/reports/export?kind=${kind}`);
            const url = URL.createObjectURL(blob);
            const a = document.createElement("a"); a.href = url; a.download = `frota-${kind}.csv`; a.click(); URL.revokeObjectURL(url);
        } catch (e) { notify(e.message, true); }
    });
    document.querySelectorAll(".fleet-tabs button").forEach(button => button.addEventListener("click", () => {
        tab = button.dataset.tab; history.replaceState(null, "", `/fleet?tab=${tab}`); render();
    }));
    document.getElementById("primary-action").addEventListener("click", openForm);
    document.getElementById("refresh").addEventListener("click", render);
    document.getElementById("search").addEventListener("change", render);
    document.getElementById("status-filter").addEventListener("change", render);
    document.querySelectorAll("[data-close]").forEach(button => button.addEventListener("click", () => dialog.close()));
    render().catch(error => notify(error.message, true));
})();
