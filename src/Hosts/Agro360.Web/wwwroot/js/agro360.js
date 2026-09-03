(() => {
    "use strict";

    const apiBase = document.querySelector('meta[name="api-base"]')?.content?.replace(/\/$/, "") ?? "http://localhost:8081";
    const apiUnavailableMessage = "Não foi possível conectar à API. Confirme se a Agro360.Api está rodando em http://localhost:8081 e abra http://localhost:8081/swagger.";
    const storageKeys = { session: "agro360.session", theme: "agro360.theme" };
    const state = { session: readJson(storageKeys.session), searchTimer: 0, selectedSearch: -1, refreshPromise: null, refreshStopped: false };
    const money = new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL", maximumFractionDigits: 0 });
    const number = new Intl.NumberFormat("pt-BR", { maximumFractionDigits: 1 });
    const relativeTime = new Intl.RelativeTimeFormat("pt-BR", { numeric: "auto" });

    const element = id => document.getElementById(id);
    const loginModal = element("login-modal");
    const palette = element("command-palette");

    function readJson(key) {
        try { return JSON.parse(localStorage.getItem(key)); } catch { return null; }
    }

    function persistSession(session) {
        state.session = session;
        if (session) {
            localStorage.setItem(storageKeys.session, JSON.stringify(session));
            state.refreshStopped = false;
        } else {
            localStorage.removeItem(storageKeys.session);
            localStorage.removeItem("agro360.accessToken");
            localStorage.removeItem("agro360.access_token");
            sessionStorage.clear();
        }
        renderUser();
    }

    function renderUser() {
        const user = state.session;
        element("user-name").textContent = user?.name ?? "Agro 360";
        element("user-email").textContent = user?.email ?? "Entrar na conta";
        element("user-initials").textContent = user?.name
            ? user.name.split(/\s+/).slice(0, 2).map(part => part[0]).join("").toUpperCase()
            : "A3";
    }

    async function api(path, options = {}, retry = true) {
        const headers = new Headers(options.headers ?? {});
        if (options.body && !headers.has("Content-Type")) headers.set("Content-Type", "application/json");
        if (state.session?.accessToken) headers.set("Authorization", `Bearer ${state.session.accessToken}`);
        headers.set("X-Timezone", Intl.DateTimeFormat().resolvedOptions().timeZone || "America/Belem");
        const response = await fetch(`${apiBase}${path}`, { ...options, headers });

        if (response.status === 401 && retry && !state.refreshStopped && state.session?.refreshToken) {
            const refreshed = await refreshSession();
            if (refreshed) return api(path, options, false);
        }

        if (!response.ok) {
            const problem = await response.json().catch(() => ({}));
            const supportCode = typeof problem.traceId === "string" ? problem.traceId : "";
            const detail = problem.detail || problem.title || `Falha HTTP ${response.status}`;
            const error = new Error(supportCode ? `${detail} Código de suporte: ${supportCode}.` : detail);
            error.status = response.status;
            error.problem = problem;
            throw error;
        }

        if (response.status === 204) return null;
        return response.json();
    }

    async function refreshSession() {
        if (state.refreshStopped || !state.session?.refreshToken) return false;
        if (state.refreshPromise) return state.refreshPromise;
        state.refreshPromise = performRefresh();
        try { return await state.refreshPromise; }
        finally { state.refreshPromise = null; }
    }

    async function performRefresh() {
        try {
            const response = await fetch(`${apiBase}/api/v1/auth/refresh`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ refreshToken: state.session.refreshToken })
            });
            if (!response.ok) throw new Error("Sessão expirada");
            persistSession(await response.json());
            return true;
        } catch {
            state.refreshStopped = true;
            persistSession(null);
            showLogin();
            toastWarning("Sessão expirada", "Sua sessão expirou. Faça login novamente.");
            return false;
        }
    }

    function showLogin() {
        loginModal.hidden = false;
        setTimeout(() => loginModal.querySelector("input")?.focus(), 50);
    }

    function hideLogin() { loginModal.hidden = true; }

    function validateLoginForm(form) {
        const messages = {
            tenantSlug: "Informe o identificador da sua organização.",
            email: "Informe um e-mail válido.",
            password: "A senha deve ter pelo menos 12 caracteres."
        };
        let valid = true;
        for (const input of form.querySelectorAll("input[required]")) {
            const fieldValid = input.checkValidity();
            input.setAttribute("aria-invalid", String(!fieldValid));
            element(`${input.name}-error`).textContent = fieldValid ? "" : messages[input.name];
            valid &&= fieldValid;
        }
        return valid;
    }

    async function login(event) {
        event.preventDefault();
        const form = event.currentTarget;
        const button = form.querySelector('button[type="submit"]');
        button.disabled = true;
        form.setAttribute("aria-busy", "true");
        button.querySelector("span").textContent = "Validando acesso...";
        try {
            if (!validateLoginForm(form)) {
                throw new Error("Preencha Cliente/Organização, e-mail e senha para continuar.");
            }
            const data = Object.fromEntries(new FormData(form));
            const response = await fetch(`${apiBase}/api/v1/auth/login`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(data)
            });
            const result = await response.json().catch(() => ({}));
            if (response.status === 401 || response.status === 403) {
                throw new Error(result.detail || "Cliente, e-mail ou senha inválidos.");
            }
            if (response.status === 400) {
                const validationMessage = result.detail
                    || Object.values(result.errors || {}).flat().join(" ");
                throw new Error(validationMessage || "Confira os dados informados.");
            }
            if (!response.ok) throw new Error(result.detail || "Não foi possível entrar.");
            persistSession(result);
            hideLogin();
            toastSuccess("Acesso confirmado", "Bem-vindo. Os dados respeitam sua organização e suas permissões.");
            await loadDashboard();
        } catch (error) {
            const detail = error instanceof TypeError
                ? apiUnavailableMessage
                : error.message;
            error instanceof TypeError
                ? toastError("API indisponível", detail)
                : toastError("Não foi possível entrar", detail);
        } finally {
            button.disabled = false;
            form.setAttribute("aria-busy", "false");
            button.querySelector("span").textContent = "Entrar no Agro360";
        }
    }

    async function testApiConnection() {
        const button = element("test-api-connection");
        button.disabled = true;
        button.classList.add("checking");
        try {
            const response = await fetch(`${apiBase}/health`, { headers: { Accept: "application/json" } });
            if (!response.ok) throw new Error(`A API respondeu com status ${response.status}. Verifique a conexão com o banco.`);
            const swagger = await fetch(`${apiBase}/swagger/v1/swagger.json`, { headers: { Accept: "application/json" } });
            if (!swagger.ok) {
                toastWarning("Swagger inacessível", `A API está online, mas o Swagger respondeu com status ${swagger.status}.`);
                return;
            }
            toastSuccess("API e Swagger conectados", "A Agro360.Api, o banco de dados e a documentação estão disponíveis.");
        } catch (error) {
            const detail = error instanceof TypeError
                ? apiUnavailableMessage
                : error.message;
            toastError("Falha na conexão", detail);
        } finally {
            button.disabled = false;
            button.classList.remove("checking");
        }
    }

    function logout() {
        if (!state.session) { showLogin(); return; }
        persistSession(null);
        resetDashboard();
        showLogin();
        toast("Sessão encerrada", "Os dados locais de autenticação foram removidos.");
    }

    async function loadDashboard() {
        if (!state.session) { showLogin(); return; }
        element("dashboard-subtitle").textContent = "Atualizando a malha de dados do tenant...";
        try {
            const result = await api("/api/v1/dashboard/command-center");
            renderDashboard(result);
            await loadLivestockDashboard();
            await loadStorageDashboard();
            element("sync-time").textContent = "sincronizado agora";
        } catch (error) {
            if (error.status !== 401) toast("Não foi possível atualizar", error.message, true);
            element("dashboard-subtitle").textContent = "Não foi possível consolidar os indicadores agora.";
        }
    }

    async function loadLivestockDashboard() {
        const host = element("livestock-kpis");
        if (!host) return;
        try {
            const d = await api("/api/livestock/dashboard");
            const cards = [
                ["Animais ativos", d.activeAnimals], ["Lotes ativos", d.activeHerds],
                ["Leite no mês", `${number.format(d.milkThisMonthLiters)} L`], ["Matrizes prenhes", d.pregnantFemales],
                ["Partos previstos", d.expectedBirths], ["Vacinas próximas", d.vaccinesDue],
                ["Em carência", d.inWithdrawal], ["Sobrelotação", d.overcapacityAlerts],
                ["Custo nutricional", money.format(d.nutritionCostMonth)], ["Custo sanitário", money.format(d.healthCostMonth)]
            ];
            host.replaceChildren(...cards.map(([label, value]) => { const card=document.createElement("article"); card.innerHTML=`<small>${escapeHtml(label)}</small><strong>${escapeHtml(value)}</strong>`; return card; }));
        } catch (error) { host.innerHTML=`<p class="error-state">Não foi possível carregar Pecuária 360: ${escapeHtml(error.message)}</p>`; }
    }

    async function loadStorageDashboard() {
        try {
            const d = await api("/api/storage/dashboard");
            setText("storage-total", `${number.format(d.totalCapacity)} t`);
            setText("storage-occupied", `${number.format(d.occupiedCapacity)} t`);
            setText("storage-available", `${number.format(d.availableCapacity)} t`);
            setText("storage-receipts", d.pendingReceipts);
            setText("storage-freight", money.format(d.freightThisMonth));
            setText("storage-alerts", `${d.blockedLots} lote(s) bloqueado(s) · ${d.qualityAlerts} alerta(s) de qualidade · ${d.capacityAlerts} alerta(s) de capacidade`);
        } catch (error) {
            setText("storage-alerts", `Não foi possível carregar armazenagem: ${error.message}`);
        }
    }

    function renderDashboard(result) {
        const kpi = result.kpis;
        setText("kpi-margin", money.format(kpi.estimatedMargin));
        setText("kpi-inventory", money.format(kpi.inventoryValue));
        setText("kpi-seasons", number.format(kpi.activeSeasons));
        setText("kpi-animals", number.format(kpi.activeAnimals));
        setText("kpi-area", `${number.format(kpi.totalAreaHa)} ha`);
        setText("kpi-farms", `${kpi.farms} ${kpi.farms === 1 ? "fazenda" : "fazendas"}`);
        setText("kpi-alerts", `${kpi.criticalAlerts} ${kpi.criticalAlerts === 1 ? "alerta crítico" : "alertas críticos"}`);
        element("dashboard-subtitle").textContent = `Posição consolidada em ${new Date(kpi.generatedAt).toLocaleString("pt-BR")}.`;
        renderOperations(result.recentOperations ?? []);
        renderPulse(kpi, result.recentOperations ?? []);
    }

    function renderOperations(operations) {
        const body = element("recent-operations");
        body.replaceChildren();
        if (!operations.length) {
            const row = document.createElement("tr");
            row.className = "empty-row";
            row.innerHTML = '<td colspan="5">Nenhuma operação registrada. Use um dos atalhos para iniciar.</td>';
            body.append(row);
            return;
        }

        operations.forEach(operation => {
            const row = document.createElement("tr");
            row.innerHTML = `
                <td><strong>${escapeHtml(operation.description)}</strong>${escapeHtml(operation.type)}</td>
                <td>${escapeHtml(operation.module)}</td>
                <td>${operation.amount == null ? "—" : money.format(operation.amount)}</td>
                <td><span class="status-badge">${escapeHtml(operation.status)}</span></td>
                <td>${formatRelative(operation.occurredAt)}</td>`;
            body.append(row);
        });
    }

    function renderPulse(kpi, operations) {
        const hasData = kpi.farms > 0;
        const field = hasData ? Math.min(100, 45 + kpi.activeSeasons * 9 + Math.min(25, operations.length * 2)) : 0;
        const stock = hasData ? Math.min(100, kpi.inventoryValue > 0 ? 86 : 42) : 0;
        const trace = hasData ? Math.min(100, 35 + operations.length * 5) : 0;
        const score = Math.round((field + stock + trace) / 3);
        setText("pulse-score", score || "—");
        setPulse("pulse-field", 0, field);
        setPulse("pulse-stock", 1, stock);
        setPulse("pulse-trace", 2, trace);
        element("pulse-insight").textContent = !hasData
            ? "Cadastre a primeira fazenda para iniciar o gêmeo digital da operação."
            : kpi.criticalAlerts > 0
                ? `${kpi.criticalAlerts} alerta(s) crítico(s) merecem priorização. A recomendação considera apenas dados autorizados.`
                : "A operação está estável. Continue registrando eventos para aumentar a qualidade das análises.";
    }

    function setPulse(id, index, value) {
        setText(id, value ? `${value}%` : "—");
        document.querySelectorAll(".pulse-bars b")[index]?.style.setProperty("--value", `${value}%`);
    }

    function resetDashboard() {
        ["kpi-margin", "kpi-inventory"].forEach(id => setText(id, "R$ —"));
        ["kpi-seasons", "kpi-animals", "pulse-score"].forEach(id => setText(id, "—"));
        renderOperations([]);
    }

    function openPalette() {
        if (!state.session) { showLogin(); return; }
        palette.hidden = false;
        const input = element("global-search");
        setTimeout(() => input.focus(), 30);
    }

    function closePalette() {
        palette.hidden = true;
        state.selectedSearch = -1;
    }

    function scheduleSearch(event) {
        clearTimeout(state.searchTimer);
        const query = event.target.value.trim();
        if (query.length < 2) {
            element("search-results").innerHTML = "<p>Digite pelo menos dois caracteres para buscar em toda a operação.</p>";
            return;
        }
        element("search-results").innerHTML = '<p><span class="loading-ring"></span> Buscando com seu escopo de acesso...</p>';
        state.searchTimer = window.setTimeout(() => runSearch(query), 260);
    }

    async function runSearch(query) {
        try {
            const results = await api(`/api/v1/search?query=${encodeURIComponent(query)}&limit=15`);
            const container = element("search-results");
            container.replaceChildren();
            if (!results.length) {
                container.innerHTML = "<p>Nenhum resultado autorizado foi encontrado.</p>";
                return;
            }
            results.forEach((item, index) => {
                const button = document.createElement("button");
                button.type = "button";
                button.className = "search-result";
                button.dataset.index = index;
                button.dataset.route = item.route;
                button.innerHTML = `<span>${escapeHtml(item.entityType.slice(0, 3))}</span><div><strong>${escapeHtml(item.title)}</strong><small>${escapeHtml(item.subtitle ?? item.entityType)}</small></div><i>abrir</i>`;
                button.addEventListener("click", () => {
                    closePalette();
                    const route = String(item.route ?? "");
                    if (route.startsWith("/") && !route.startsWith("//")) window.location.assign(route);
                    else toast("Resultado indisponível", "O destino retornado pela busca não é uma rota interna válida.", true);
                });
                container.append(button);
            });
        } catch (error) {
            element("search-results").innerHTML = `<p>${escapeHtml(error.message)}</p>`;
        }
    }

    function keydown(event) {
        if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "k") {
            event.preventDefault();
            openPalette();
        }
        if (event.key === "Escape") {
            closePalette();
            document.body.classList.remove("menu-open");
        }
    }

    function featureMessage(event) {
        const feature = event.currentTarget.dataset.feature;
        const finance = new Set(["chart-accounts", "cost-centers", "payables", "receivables", "cash-flow", "economic-results", "analytics"]);
        const storage = new Set(["stock", "storage-structures", "storage-receipts", "storage-quality", "storage-lots", "storage-processing", "storage-shipments", "storage-logistics", "storage-contracts"]);
        const livestock = new Set(["weighing", "animals", "herds", "pastures", "handling", "health", "reproduction", "nutrition", "production"]);
        const traceability = new Set(["traceability-lots", "lot-timeline", "processing-compliance", "immutable-ledger", "certificates", "regional-routes", "regional-trips", "sales-partners", "commissions", "revenue-split"]);
        let route = "/Agriculture";
        if (feature === "context") route = "/Saas";
        else if (finance.has(feature)) route = "/Intelligence";
        else if (storage.has(feature)) route = "/#storage";
        else if (livestock.has(feature)) route = "/#livestock";
        else if (traceability.has(feature)) route = feature === "processing-compliance" ? "/Compliance" : "/Maps";
        window.location.assign(route);
    }

    function toggleTheme() {
        const next = document.documentElement.dataset.theme === "dark" ? "light" : "dark";
        document.documentElement.dataset.theme = next;
        localStorage.setItem(storageKeys.theme, next);
    }

    function showToast(severity, title, detail) {
        const item = document.createElement("div");
        item.className = `toast ${severity}`;
        item.setAttribute("role", severity === "error" ? "alert" : "status");
        item.innerHTML = `<span class="toast-icon" aria-hidden="true"></span><div><strong>${escapeHtml(title)}</strong><small>${escapeHtml(detail)}</small></div><button type="button" aria-label="Fechar mensagem">×</button>`;
        item.querySelector("button").addEventListener("click", () => item.remove());
        element("toast-region").append(item);
        window.setTimeout(() => item.remove(), 5200);
    }

    const toastSuccess = (title, detail) => showToast("success", title, detail);
    const toastWarning = (title, detail) => showToast("warning", title, detail);
    const toastError = (title, detail) => showToast("error", title, detail);
    const toastInfo = (title, detail) => showToast("info", title, detail);
    const toast = (title, detail, error = false) => error ? toastError(title, detail) : toastSuccess(title, detail);

    function confirmDialog(title, message, confirmText = "Confirmar", cancelText = "Cancelar") {
        const dialog = element("action-confirmation");
        element("confirmation-title").textContent = title;
        element("confirmation-consequence").textContent = message;
        element("confirmation-submit").textContent = confirmText;
        dialog.querySelector('[value="cancel"]').textContent = cancelText;
        dialog.showModal();
        return new Promise(resolve => dialog.addEventListener("close", () => resolve(dialog.returnValue === "confirm"), { once: true }));
    }

    Object.assign(window, { toastSuccess, toastWarning, toastError, toastInfo, confirmDialog });

    function setText(id, value) { element(id).textContent = value; }
    function escapeHtml(value) { const span = document.createElement("span"); span.textContent = String(value ?? ""); return span.innerHTML; }
    function formatRelative(value) {
        const seconds = Math.round((new Date(value).getTime() - Date.now()) / 1000);
        if (Math.abs(seconds) < 60) return relativeTime.format(seconds, "second");
        const minutes = Math.round(seconds / 60);
        if (Math.abs(minutes) < 60) return relativeTime.format(minutes, "minute");
        const hours = Math.round(minutes / 60);
        if (Math.abs(hours) < 24) return relativeTime.format(hours, "hour");
        return new Date(value).toLocaleDateString("pt-BR");
    }

    function init() {
        const savedTheme = localStorage.getItem(storageKeys.theme);
        if (savedTheme) document.documentElement.dataset.theme = savedTheme;
        renderUser();
        element("login-form").addEventListener("submit", login);
        element("login-form").addEventListener("input", event => {
            if (event.target.matches("input[required]")) validateLoginForm(event.currentTarget);
        });
        element("test-api-connection").addEventListener("click", testApiConnection);
        element("logout-button").addEventListener("click", logout);
        element("search-trigger").addEventListener("click", openPalette);
        element("global-search").addEventListener("input", scheduleSearch);
        element("theme-button").addEventListener("click", toggleTheme);
        element("menu-button").addEventListener("click", () => document.body.classList.toggle("menu-open"));
        element("refresh-dashboard").addEventListener("click", loadDashboard);
        document.querySelectorAll("[data-feature]").forEach(button => button.addEventListener("click", featureMessage));
        document.addEventListener("keydown", keydown);
        palette.addEventListener("click", event => { if (event.target === palette) closePalette(); });
        if ("serviceWorker" in navigator) navigator.serviceWorker.register("/service-worker.js").catch(() => {});
        if (state.session) loadDashboard(); else showLogin();
    }

    init();
    async function loadOperationalDashboard() {
        try {
            const data = await api("/api/operations/dashboard");
            if (!data) return;
            const set = (id, value) => { const target = element(id); if (target) target.textContent = value; };
            set("ops-open-purchases", data.openPurchases);
            set("ops-awaiting", `${data.awaitingApproval} aguardando aprovação`);
            set("ops-low-stock", data.lowStockItems);
            set("ops-expiring", `${data.expiringItems} próximos do vencimento`);
            set("ops-assets", data.availableAssets);
            set("ops-maintenance", `${data.assetsInMaintenance} em manutenção`);
            set("ops-fuel", number.format(data.fuelThisMonth));
        } catch (error) { console.error("Falha ao carregar dashboard operacional", error); }
    }
    loadOperationalDashboard();

})();
