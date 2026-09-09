(() => {
    "use strict";

    const app = document.getElementById("properties-app");
    if (!app) return;

    const apiBase = document.querySelector('meta[name="api-base"]')?.content?.replace(/\/$/, "") ?? "http://localhost:8081";
    const farmDialog = document.getElementById("farm-dialog");
    const fieldDialog = document.getElementById("field-dialog");
    const farmForm = document.getElementById("farm-form");
    const fieldForm = document.getElementById("field-form");
    const farmList = document.getElementById("farm-list");
    const fieldList = document.getElementById("field-list");
    const toast = document.getElementById("properties-toast");
    let farms = [];
    let fields = [];
    let selectedFarm = null;
    let editingFarm = null;
    let editingField = null;
    let toastTimer = 0;

    function session() {
        try { return JSON.parse(localStorage.getItem("agro360.session")); }
        catch { return null; }
    }

    function normalizePermission(value) {
        return String(value ?? "").toLowerCase().replace(/^agro360\./, "").replaceAll("_", ".");
    }

    function canWrite() {
        const current = session();
        return current?.roles?.includes("SUPER_ADMIN")
            || current?.permissions?.map(normalizePermission).includes("properties.write");
    }

    function token() {
        return session()?.accessToken ?? localStorage.getItem("agro360.accessToken");
    }

    async function request(path, options = {}) {
        const headers = new Headers(options.headers ?? {});
        const accessToken = token();
        if (accessToken) headers.set("Authorization", `Bearer ${accessToken}`);
        if (options.body) headers.set("Content-Type", "application/json");
        const response = await fetch(`${apiBase}${path}`, { ...options, headers });
        if (response.status === 401) throw new Error("Sua sessão expirou. Entre novamente para continuar.");
        if (!response.ok) {
            const problem = await response.json().catch(() => ({}));
            const validation = Object.values(problem.errors ?? {}).flat().join(" ");
            throw new Error(validation || problem.detail || problem.title || `Falha HTTP ${response.status}.`);
        }
        return response.status === 204 ? null : response.json();
    }

    function escapeHtml(value) {
        return String(value ?? "").replace(/[&<>"']/g, character => ({
            "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;"
        }[character]));
    }

    const area = value => `${new Intl.NumberFormat("pt-BR", { minimumFractionDigits: 0, maximumFractionDigits: 4 }).format(value ?? 0)} ha`;

    function notify(message, error = false) {
        clearTimeout(toastTimer);
        toast.textContent = message;
        toast.classList.toggle("error", error);
        toast.hidden = false;
        toastTimer = window.setTimeout(() => { toast.hidden = true; }, 5000);
    }

    function setBusy(form, busy, label) {
        form.setAttribute("aria-busy", String(busy));
        const button = form.querySelector('button[type="submit"]');
        button.disabled = busy;
        button.querySelector("span").textContent = busy ? "Salvando…" : label;
    }

    function renderMetrics() {
        document.getElementById("farm-count").textContent = farms.length;
        document.getElementById("farm-area").textContent = area(farms.reduce((sum, farm) => sum + Number(farm.totalAreaHa), 0));
        document.getElementById("field-count").textContent = selectedFarm ? fields.length : "—";
        const allocated = fields.reduce((sum, field) => sum + Number(field.areaHa), 0);
        document.getElementById("available-area").textContent = selectedFarm ? area(Math.max(0, Number(selectedFarm.totalAreaHa) - allocated)) : "—";
    }

    function farmActions(farm) {
        if (!canWrite()) return '<span class="muted">Somente leitura</span>';
        return `<button type="button" data-action="edit-farm" data-id="${farm.id}">Editar</button><button type="button" data-action="archive-farm" data-id="${farm.id}">Arquivar</button>`;
    }

    function renderFarms() {
        farmList.innerHTML = farms.length ? farms.map(farm => `
            <tr data-farm-id="${farm.id}" aria-selected="${selectedFarm?.id === farm.id}">
                <td><strong>${escapeHtml(farm.name)}</strong></td>
                <td>${escapeHtml(farm.state)}</td>
                <td>${area(farm.totalAreaHa)}</td>
                <td>${escapeHtml([farm.registrationNumber, farm.carNumber].filter(Boolean).join(" · ") || "Não informado")}</td>
                <td>${farmActions(farm)}</td>
            </tr>`).join("") : '<tr><td colspan="5" class="empty-state">Nenhuma fazenda encontrada. Ajuste a busca ou cadastre a primeira unidade.</td></tr>';
        renderMetrics();
    }

    function renderFields() {
        document.getElementById("fields-title").textContent = selectedFarm ? `Talhões de ${selectedFarm.name}` : "Talhões";
        document.getElementById("new-field").disabled = !selectedFarm || !canWrite();
        if (!selectedFarm) {
            fieldList.innerHTML = '<tr><td colspan="4" class="empty-state">Selecione uma fazenda para consultar seus talhões.</td></tr>';
        } else {
            fieldList.innerHTML = fields.length ? fields.map(field => `
                <tr>
                    <td><strong>${escapeHtml(field.name)}</strong></td>
                    <td>${area(field.areaHa)}</td>
                    <td>${field.boundaryGeoJson ? "Polygon informado" : "Não informada"}</td>
                    <td>${canWrite() ? `<button type="button" data-action="edit-field" data-id="${field.id}">Editar</button><button type="button" data-action="archive-field" data-id="${field.id}">Arquivar</button>` : '<span class="muted">Somente leitura</span>'}</td>
                </tr>`).join("") : '<tr><td colspan="4" class="empty-state">Esta fazenda ainda não possui talhões cadastrados.</td></tr>';
        }
        renderMetrics();
    }

    async function loadFarms(search = "") {
        farmList.innerHTML = '<tr><td colspan="5" class="empty-state">Carregando fazendas…</td></tr>';
        try {
            const result = await request(`/api/v1/properties?page=1&pageSize=100&search=${encodeURIComponent(search)}`);
            farms = result.items ?? [];
            if (selectedFarm) selectedFarm = farms.find(item => item.id === selectedFarm.id) ?? null;
            renderFarms();
            if (!selectedFarm) {
                fields = [];
                renderFields();
            }
        } catch (error) {
            farmList.innerHTML = `<tr><td colspan="5" class="empty-state">${escapeHtml(error.message)}</td></tr>`;
            notify(error.message, true);
        }
    }

    async function selectFarm(id) {
        selectedFarm = farms.find(farm => farm.id === id) ?? null;
        renderFarms();
        renderFields();
        if (!selectedFarm) return;
        fieldList.innerHTML = '<tr><td colspan="4" class="empty-state">Carregando talhões…</td></tr>';
        try {
            const result = await request(`/api/v1/properties/${selectedFarm.id}/fields?page=1&pageSize=100`);
            fields = result.items ?? [];
            renderFields();
        } catch (error) {
            fieldList.innerHTML = `<tr><td colspan="4" class="empty-state">${escapeHtml(error.message)}</td></tr>`;
            notify(error.message, true);
        }
    }

    async function openFarm(farm = null) {
        editingFarm = farm;
        farmForm.reset();
        farmForm.querySelector(".properties-form-message").textContent = "";
        document.getElementById("farm-dialog-title").textContent = farm ? "Editar fazenda" : "Nova fazenda";
        const organization = farmForm.elements.organizationId;
        organization.disabled = Boolean(farm);
        if (farm) {
            organization.value = farm.organizationId;
            farmForm.elements.name.value = farm.name;
            farmForm.elements.state.value = farm.state;
            farmForm.elements.totalAreaHa.value = farm.totalAreaHa;
            farmForm.elements.registrationNumber.value = farm.registrationNumber ?? "";
            farmForm.elements.carNumber.value = farm.carNumber ?? "";
        }
        farmDialog.showModal();
    }

    function openField(field = null) {
        if (!selectedFarm) return;
        editingField = field;
        fieldForm.reset();
        fieldForm.querySelector(".properties-form-message").textContent = "";
        document.getElementById("field-dialog-title").textContent = field ? "Editar talhão" : "Novo talhão";
        document.getElementById("field-farm-name").textContent = selectedFarm.name;
        if (field) {
            fieldForm.elements.name.value = field.name;
            fieldForm.elements.areaHa.value = field.areaHa;
            fieldForm.elements.boundaryGeoJson.value = field.boundaryGeoJson ?? "";
        }
        fieldDialog.showModal();
    }

    async function loadOrganizations() {
        const select = farmForm.elements.organizationId;
        try {
            const organizations = await request("/api/v1/property-organizations");
            select.innerHTML = '<option value="">Selecione pelo nome</option>' + organizations.map(item => `<option value="${item.id}">${escapeHtml(item.name)} · ${escapeHtml(item.type)}</option>`).join("");
        } catch (error) {
            select.innerHTML = '<option value="">Organizações indisponíveis</option>';
            notify(error.message, true);
        }
    }

    farmForm.addEventListener("submit", async event => {
        event.preventDefault();
        if (!farmForm.reportValidity()) return;
        setBusy(farmForm, true, "Salvar fazenda");
        const message = farmForm.querySelector(".properties-form-message");
        try {
            const values = Object.fromEntries(new FormData(farmForm));
            const body = {
                name: values.name.trim(),
                state: values.state,
                totalAreaHa: Number(values.totalAreaHa),
                registrationNumber: values.registrationNumber.trim() || null,
                carNumber: values.carNumber.trim() || null
            };
            if (editingFarm) {
                await request(`/api/v1/properties/${editingFarm.id}`, { method: "PUT", body: JSON.stringify({ ...body, version: editingFarm.version }) });
            } else {
                await request("/api/v1/properties", { method: "POST", body: JSON.stringify({ ...body, organizationId: values.organizationId }) });
            }
            farmDialog.close();
            notify(editingFarm ? "Fazenda atualizada com sucesso." : "Fazenda cadastrada com sucesso.");
            await loadFarms(document.getElementById("farm-search-text").value);
        } catch (error) {
            message.textContent = error.message;
        } finally {
            setBusy(farmForm, false, "Salvar fazenda");
        }
    });

    fieldForm.addEventListener("submit", async event => {
        event.preventDefault();
        if (!fieldForm.reportValidity() || !selectedFarm) return;
        setBusy(fieldForm, true, "Salvar talhão");
        const message = fieldForm.querySelector(".properties-form-message");
        try {
            const values = Object.fromEntries(new FormData(fieldForm));
            const body = { name: values.name.trim(), areaHa: Number(values.areaHa), boundaryGeoJson: values.boundaryGeoJson.trim() || null };
            if (editingField) {
                await request(`/api/v1/fields/${editingField.id}`, { method: "PUT", body: JSON.stringify({ ...body, version: editingField.version }) });
            } else {
                await request("/api/v1/fields", { method: "POST", body: JSON.stringify({ ...body, farmId: selectedFarm.id }) });
            }
            fieldDialog.close();
            notify(editingField ? "Talhão atualizado com sucesso." : "Talhão cadastrado com sucesso.");
            await selectFarm(selectedFarm.id);
        } catch (error) {
            message.textContent = error.message;
        } finally {
            setBusy(fieldForm, false, "Salvar talhão");
        }
    });

    document.getElementById("farm-search").addEventListener("submit", event => {
        event.preventDefault();
        loadFarms(event.currentTarget.elements.search.value.trim());
    });
    document.getElementById("new-farm").addEventListener("click", () => openFarm());
    document.getElementById("new-field").addEventListener("click", () => openField());
    document.querySelectorAll("[data-close]").forEach(button => button.addEventListener("click", () => document.getElementById(button.dataset.close)?.close()));

    farmList.addEventListener("click", async event => {
        const action = event.target.closest("[data-action]");
        if (!action) {
            const row = event.target.closest("[data-farm-id]");
            if (row) await selectFarm(row.dataset.farmId);
            return;
        }
        const farm = farms.find(item => item.id === action.dataset.id);
        if (!farm) return;
        if (action.dataset.action === "edit-farm") return openFarm(farm);
        if (action.dataset.action === "archive-farm") {
            if (!window.confirm(`Arquivar a fazenda “${farm.name}”? Ela deixará de aparecer nos seletores operacionais.`)) return;
            try {
                await request(`/api/v1/properties/${farm.id}?version=${farm.version}`, { method: "DELETE" });
                selectedFarm = null;
                fields = [];
                notify("Fazenda arquivada com sucesso.");
                await loadFarms(document.getElementById("farm-search-text").value);
            } catch (error) { notify(error.message, true); }
        }
    });

    fieldList.addEventListener("click", async event => {
        const action = event.target.closest("[data-action]");
        if (!action || !selectedFarm) return;
        const field = fields.find(item => item.id === action.dataset.id);
        if (!field) return;
        if (action.dataset.action === "edit-field") return openField(field);
        if (action.dataset.action === "archive-field") {
            if (!window.confirm(`Arquivar o talhão “${field.name}”? O histórico continuará preservado.`)) return;
            try {
                await request(`/api/v1/fields/${field.id}?version=${field.version}`, { method: "DELETE" });
                notify("Talhão arquivado com sucesso.");
                await selectFarm(selectedFarm.id);
            } catch (error) { notify(error.message, true); }
        }
    });

    const states = ["AC", "AL", "AP", "AM", "BA", "CE", "DF", "ES", "GO", "MA", "MT", "MS", "MG", "PA", "PB", "PR", "PE", "PI", "RJ", "RN", "RS", "RO", "RR", "SC", "SP", "SE", "TO"];
    farmForm.elements.state.innerHTML = '<option value="">Selecione</option>' + states.map(state => `<option value="${state}">${state}</option>`).join("");
    document.getElementById("new-farm").hidden = !canWrite();
    document.getElementById("new-field").hidden = !canWrite();
    Promise.all([loadOrganizations(), loadFarms()]);
})();
