(() => {
    const api = document.querySelector('meta[name="api-base"]').content;
    const content = document.querySelector("#saas-content");
    const status = document.querySelector("#saas-status");
    const dialog = document.querySelector("#saas-dialog");
    const dialogForm = document.querySelector("#saas-form");
    const dialogFields = document.querySelector("#dialog-fields");
    const dialogMessage = dialogForm.querySelector(".form-message");
    const session = readSession();
    const permissions = new Set((session?.permissions ?? []).map(normalizePermission));
    const isSuperAdministrator = (session?.roles ?? []).includes("SUPER_ADMIN");
    const tenantViews = new Set(["onboarding", "users", "roles", "invitations", "security", "notifications", "settings", "account"]);
    let view = isSuperAdministrator ? "dashboard" : permissions.has("account.users.read") ? "users" : "account";

    const helps = {
        dashboard: "Consulte indicadores globais e alertas. O acesso de suporte e toda ação administrativa são auditados.",
        tenants: "Cadastre e acompanhe organizações. CPF/CNPJ, responsável e plano são obrigatórios; bloqueios e desbloqueios exigem justificativa.",
        plans: "Defina limites e recursos comerciais. Somente a super administração pode publicar ou desativar planos.",
        billing: "Acompanhe vencimentos e inadimplência. Baixas são manuais, justificadas e auditadas; nunca há pagamento automático.",
        features: "Selecione uma organização para conferir recursos do plano e liberações temporárias justificadas.",
        audit: "Consulte ações críticas e seus responsáveis. Filtre pela organização durante o atendimento.",
        users: "Inative ou reative usuários desta organização com justificativa. O próprio acesso e o último administrador ativo são protegidos; sessões do usuário alterado são revogadas.",
        roles: "Combine permissões por função. Alterações afetam usuários vinculados e são registradas na auditoria.",
        usage: "Compare consumo e limites contratados. Um limite atingido bloqueia novos registros sem apagar dados.",
        account: "Consulte plano e consumo da sua organização. Mudanças de plano são solicitações sujeitas à aprovação.",
        settings: "Configure idioma, moeda, fuso e preferências da organização. Campos obrigatórios são validados ao salvar."
    };

    function readSession() {
        try {
            return JSON.parse(localStorage.getItem("agro360.session") ?? "null");
        } catch {
            return null;
        }
    }

    function normalizePermission(value) {
        return String(value ?? "").trim().toLowerCase().replaceAll("_", ".");
    }

    function token() {
        return session?.accessToken ?? localStorage.getItem("agro360.accessToken");
    }

    function escapeHtml(value) {
        return String(value ?? "").replace(/[&<>"']/g, character => ({
            "&": "&amp;", "<": "&lt;", ">": "&gt;", "\"": "&quot;", "'": "&#39;"
        })[character]);
    }

    async function request(path, options = {}) {
        const headers = new Headers(options.headers ?? {});
        headers.set("Authorization", `Bearer ${token()}`);
        if (options.body && !headers.has("Content-Type")) headers.set("Content-Type", "application/json");
        const response = await fetch(api + path, { ...options, headers });
        if (!response.ok) {
            const problem = await response.json().catch(() => null);
            const trace = problem?.traceId ? ` Referência: ${problem.traceId}.` : "";
            throw new Error(`${problem?.detail ?? "Não foi possível concluir a operação."}${trace}`);
        }
        return response.status === 204 ? null : response.json();
    }

    async function get(path) {
        status.textContent = "Carregando…";
        try {
            const result = await request(path);
            status.textContent = "Dados atualizados.";
            return result;
        } catch (error) {
            status.textContent = "Falha no carregamento.";
            content.innerHTML = `<div class="error" role="alert">${escapeHtml(error.message)} <button id="retry" type="button">Tentar novamente</button></div>`;
            document.querySelector("#retry")?.addEventListener("click", load);
            throw error;
        }
    }

    function cards(values) {
        return `<div class="saas-grid">${Object.entries(values).map(([key, value]) =>
            `<article class="saas-card"><small>${escapeHtml(key)}</small><strong>${escapeHtml(value)}</strong></article>`).join("")}</div>`;
    }

    function table(rows, columns) {
        if (!rows.length) return '<div class="empty">Nenhum registro encontrado.</div>';
        return `<div class="saas-card"><table class="saas-table"><thead><tr>${columns.map(column =>
            `<th>${escapeHtml(column[1])}</th>`).join("")}</tr></thead><tbody>${rows.map(row =>
            `<tr>${columns.map(column => `<td>${escapeHtml(row[column[0]])}</td>`).join("")}</tr>`).join("")}</tbody></table></div>`;
    }

    function usersTable(rows) {
        if (!rows.length) return '<div class="empty">Nenhum usuário encontrado.</div>';
        const canManage = permissions.has("account.users.manage") || isSuperAdministrator;
        return `<div class="saas-card"><table class="saas-table"><thead><tr><th>Nome</th><th>E-mail</th><th>Status</th><th>Perfis</th><th>Último acesso</th>${canManage ? "<th>Ações</th>" : ""}</tr></thead><tbody>${rows.map(user => {
            const action = user.status === "ACTIVE" ? "deactivate" : user.status === "DISABLED" ? "activate" : null;
            const actionLabel = action === "deactivate" ? "Inativar" : "Reativar";
            const isCurrentUser = String(user.id).toLowerCase() === String(session?.userId ?? "").toLowerCase();
            const actionButton = canManage && action && !isCurrentUser
                ? `<button type="button" class="${action === "deactivate" ? "danger-button" : "primary-button"}" data-user-status="${action}" data-user-id="${escapeHtml(user.id)}" data-user-name="${escapeHtml(user.name)}">${actionLabel}</button>`
                : isCurrentUser ? "Seu usuário" : "";
            return `<tr><td>${escapeHtml(user.name)}</td><td>${escapeHtml(user.email)}</td><td>${escapeHtml(user.status)}</td><td>${escapeHtml(user.roles)}</td><td>${escapeHtml(user.lastAccess ?? "Nunca")}</td>${canManage ? `<td class="saas-actions">${actionButton}</td>` : ""}</tr>`;
        }).join("")}</tbody></table></div>`;
    }

    function wireUserActions() {
        content.querySelectorAll("[data-user-status]").forEach(button => button.addEventListener("click", () => {
            openUserStatusDialog(button.dataset.userId, button.dataset.userName, button.dataset.userStatus === "activate");
        }));
    }

    function openUserStatusDialog(userId, userName, activate) {
        document.querySelector("#dialog-title").textContent = activate ? "Reativar usuário" : "Inativar usuário";
        dialogFields.innerHTML = `<p>${activate ? "O usuário voltará a poder autenticar nesta organização." : "O usuário perderá o acesso e suas sessões atuais serão revogadas."}</p><p><strong>${escapeHtml(userName)}</strong></p><label for="user-status-reason">Justificativa <span aria-hidden="true">*</span><textarea id="user-status-reason" name="reason" minlength="5" maxlength="1000" required aria-describedby="user-status-help"></textarea><small id="user-status-help">Informe entre 5 e 1000 caracteres. A justificativa ficará na auditoria.</small></label>`;
        dialogMessage.textContent = "";
        dialogForm.onsubmit = async event => {
            event.preventDefault();
            if (event.submitter?.value === "cancel") {
                dialog.close();
                return;
            }
            const reason = dialogForm.elements.reason.value.trim();
            if (reason.length < 5 || reason.length > 1000) {
                dialogMessage.textContent = "Informe uma justificativa entre 5 e 1000 caracteres.";
                dialogForm.elements.reason.focus();
                return;
            }
            const submit = event.submitter;
            submit.disabled = true;
            dialogMessage.textContent = "Salvando…";
            try {
                await request(`/api/users/${encodeURIComponent(userId)}/${activate ? "activate" : "deactivate"}`, {
                    method: "POST",
                    body: JSON.stringify({ reason })
                });
                dialog.close();
                status.textContent = activate ? "Usuário reativado com sucesso." : "Usuário inativado e sessões revogadas.";
                await load();
            } catch (error) {
                dialogMessage.textContent = error.message;
            } finally {
                submit.disabled = false;
            }
        };
        dialog.showModal();
        dialogForm.elements.reason.focus();
    }

    function openForm(title, fields, submitAction) {
        document.querySelector("#dialog-title").textContent = title;
        dialogFields.innerHTML = fields;
        dialogMessage.textContent = "";
        dialogForm.onsubmit = async event => {
            event.preventDefault();
            if (event.submitter?.value === "cancel") { dialog.close(); return; }
            if (!dialogForm.reportValidity()) return;
            const submit = event.submitter;
            submit.disabled = true;
            dialogMessage.textContent = "Salvando…";
            try {
                const message = await submitAction(new FormData(dialogForm));
                if (message) {
                    dialogMessage.textContent = message;
                    submit.textContent = "Concluído";
                    await load();
                } else {
                    dialog.close();
                    await load();
                }
            } catch (error) {
                dialogMessage.textContent = error.message;
                submit.disabled = false;
            }
        };
        dialog.showModal();
        dialogForm.querySelector("input,select,textarea")?.focus();
    }

    function option(value, label) { return `<option value="${escapeHtml(value)}">${escapeHtml(label)}</option>`; }

    async function createUserForm() {
        const roles = (await request("/api/roles")).filter(role => !role.systemRole || role.level <= 100);
        openForm("Novo usuário e convite", `<label>Nome <input name="name" maxlength="160" required></label><label>E-mail <input name="email" type="email" maxlength="254" required></label><label>Perfil <select name="role" required><option value="">Selecione…</option>${roles.map(role => option(role.id, role.name)).join("")}</select></label>`, async data => {
            const payload = { name: data.get("name"), email: data.get("email"), roleIds: [data.get("role")] };
            await request("/api/users", { method: "POST", body: JSON.stringify(payload) });
            const invitation = await request("/api/invitations", { method: "POST", body: JSON.stringify({ email: payload.email, roleId: data.get("role"), validForHours: 72 }) });
            return `Comunicação pendente de provedor. Entregue uma única vez ao destinatário: ${location.origin}/Saas/Accept?token=${encodeURIComponent(invitation.activationToken)}`;
        });
    }

    async function createInvitationForm() {
        const roles = await request("/api/roles");
        openForm("Novo convite", `<label>E-mail <input name="email" type="email" maxlength="254" required></label><label>Perfil <select name="role" required><option value="">Selecione…</option>${roles.map(role => option(role.id, role.name)).join("")}</select></label><label>Validade em horas <input name="hours" type="number" min="1" max="168" value="72" required></label>`, async data => {
            const result = await request("/api/invitations", { method: "POST", body: JSON.stringify({ email: data.get("email"), roleId: data.get("role"), validForHours: Number(data.get("hours")) }) });
            return `Comunicação pendente de provedor. Link de uso único: ${location.origin}/Saas/Accept?token=${encodeURIComponent(result.activationToken)}`;
        });
    }

    function createRoleForm() {
        const assignable = [...permissions].filter(permission => permission !== "platform.admin").sort();
        openForm("Novo perfil", `<label>Nome <input name="name" maxlength="120" required></label><label>Nível <input name="level" type="number" min="1" max="90" value="10" required></label><fieldset><legend>Permissões</legend><div class="saas-permission-grid">${assignable.map(permission => `<label><input type="checkbox" name="permissions" value="${escapeHtml(permission)}"> ${escapeHtml(permission)}</label>`).join("")}</div></fieldset>`, async data => {
            const selected = data.getAll("permissions");
            if (!selected.length) throw new Error("Selecione ao menos uma permissão.");
            await request("/api/roles", { method: "POST", body: JSON.stringify({ name: data.get("name"), level: Number(data.get("level")), permissions: selected }) });
        });
    }

    async function createTenantForm() {
        const plans = await request("/api/platform/plans");
        openForm("Nova organização", `<label>Identificador <input name="slug" pattern="[a-z0-9]+(?:-[a-z0-9]+)*" maxlength="80" required></label><label>Razão social <input name="name" maxlength="180" required></label><label>Tipo <select name="type" required>${["PRODUCER","COOPERATIVE","AGRIBUSINESS","CONSULTANCY","DISTRIBUTOR","CARRIER","OTHER"].map(value => option(value, value)).join("")}</select></label><label>CPF/CNPJ <input name="document" maxlength="18" required></label><label>Responsável <input name="responsibleName" maxlength="160" required></label><label>E-mail do administrador <input name="responsibleEmail" type="email" maxlength="254" required></label><label>Plano <select name="plan" required><option value="">Selecione…</option>${plans.map(plan => option(plan.id, plan.name)).join("")}</select></label>`, async data => {
            const result = await request("/api/platform/tenants", { method: "POST", body: JSON.stringify({ slug: data.get("slug"), name: data.get("name"), type: data.get("type"), document: data.get("document"), responsibleName: data.get("responsibleName"), responsibleEmail: data.get("responsibleEmail"), planId: data.get("plan") }) });
            return `Organização provisionada. Comunicação pendente de provedor; link único do administrador: ${location.origin}/Saas/Accept?token=${encodeURIComponent(result.administratorInvitation.activationToken)}`;
        });
    }

    function createPlanForm() {
        openForm("Novo plano", `<label>Nome <input name="name" maxlength="100" required></label><label>Descrição <textarea name="description" maxlength="500" required></textarea></label><label>Mensalidade <input name="monthly" type="number" min="0" step="0.01" required></label><label>Anuidade <input name="annual" type="number" min="0" step="0.01" required></label><label>Usuários <input name="users" type="number" min="1" required></label><label>Propriedades <input name="properties" type="number" min="1" required></label><label>Armazenamento MB <input name="storage" type="number" min="1" required></label><label>Dispositivos <input name="devices" type="number" min="1" required></label><label>Módulos <input name="modules" placeholder="properties, inventory" required></label>`, async data => {
            await request("/api/platform/plans", { method: "POST", body: JSON.stringify({ name: data.get("name"), description: data.get("description"), monthlyPrice: Number(data.get("monthly")), annualPrice: Number(data.get("annual")), userLimit: Number(data.get("users")), propertyLimit: Number(data.get("properties")), storageLimitMb: Number(data.get("storage")), deviceLimit: Number(data.get("devices")), modules: String(data.get("modules")).split(",").map(value => value.trim()).filter(Boolean), premiumFeatures: [], active: true }) });
        });
    }

    async function upgradeForm() {
        const [plans, current] = await Promise.all([request("/api/platform/plans"), request("/api/account/plan")]);
        const available = plans.filter(plan => plan.active && plan.id !== current.id);
        openForm("Solicitar alteração de plano", `<label>Novo plano <select name="plan" required><option value="">Selecione…</option>${available.map(plan => option(plan.id, `${plan.name} — ${plan.monthlyPrice}`)).join("")}</select></label><label>Justificativa <textarea name="reason" minlength="5" maxlength="1000" required></textarea></label>`, async data => {
            await request("/api/account/upgrade-requests", { method: "POST", body: JSON.stringify({ requestedPlanId: data.get("plan"), reason: data.get("reason") }) });
        });
    }

    function wireCreateActions() {
        content.querySelector('[data-create="user"]')?.addEventListener("click", createUserForm);
        content.querySelector('[data-create="invitation"]')?.addEventListener("click", createInvitationForm);
        content.querySelector('[data-create="role"]')?.addEventListener("click", createRoleForm);
        content.querySelector('[data-create="tenant"]')?.addEventListener("click", createTenantForm);
        content.querySelector('[data-create="plan"]')?.addEventListener("click", createPlanForm);
        content.querySelector('[data-create="upgrade"]')?.addEventListener("click", upgradeForm);
    }

    async function load() {
        content.setAttribute("aria-busy", "true");
        document.querySelector("#saas-help").textContent = helps[view] ?? "Use os filtros e ações disponíveis. O acesso respeita tenant, módulo e permissões.";
        try {
            if (view === "dashboard") {
                const item = await get("/api/platform/dashboard");
                content.innerHTML = cards({ "Organizações": item.totalOrganizations, "Ativas": item.activeOrganizations, "Suspensas": item.suspendedOrganizations, "Novos no mês": item.newThisMonth, "Usuários ativos": item.activeUsers, "Próximos do limite": item.nearLimit, "Acima do limite": item.aboveLimit, "Convites pendentes": item.pendingInvitations, "Logins recentes": item.recentLogins, "Alertas de segurança": item.securityAlerts, "Upgrades": item.upgradeRequests, "Suporte": item.supportRequests });
            } else if (view === "tenants") {
                const rows = await get("/api/platform/tenants");
                content.innerHTML = '<button class="primary-button" data-create="tenant">Nova organização</button>' + table(rows, [["name", "Organização"], ["type", "Tipo"], ["document", "CPF/CNPJ"], ["responsibleName", "Responsável"], ["planName", "Plano"], ["status", "Status"]]);
            } else if (view === "plans") {
                const rows = await get("/api/platform/plans");
                content.innerHTML = '<button class="primary-button" data-create="plan">Novo plano</button>' + table(rows, [["name", "Plano"], ["monthlyPrice", "Mensal informativo"], ["userLimit", "Usuários"], ["propertyLimit", "Propriedades"], ["deviceLimit", "Dispositivos"], ["active", "Ativo"]]);
            } else if (view === "billing") {
                content.innerHTML = table(await get("/api/platform/billing"), [["tenantName", "Cliente"], ["planName", "Plano"], ["competence", "Período"], ["amount", "Valor"], ["dueOn", "Vencimento"], ["status", "Status"], ["paidOn", "Baixa manual"]]);
            } else if (view === "features") {
                const tenants = await get("/api/platform/tenants");
                content.innerHTML = '<label class="saas-card">Cliente <select id="feature-tenant"><option value="">Selecione…</option>' + tenants.map(item => `<option value="${escapeHtml(item.id)}">${escapeHtml(item.name)}</option>`).join("") + '</select></label><div id="feature-list" class="empty">Selecione um cliente para ver as funcionalidades.</div>';
                document.querySelector("#feature-tenant").addEventListener("change", async event => {
                    const box = document.querySelector("#feature-list");
                    if (!event.target.value) { box.innerHTML = "Selecione um cliente para ver as funcionalidades."; return; }
                    box.innerHTML = table(await get(`/api/platform/tenants/${encodeURIComponent(event.target.value)}/features`), [["name", "Funcionalidade"], ["planEnabled", "Liberada pelo plano"], ["tenantEnabled", "Override"], ["effectiveOrigin", "Origem efetiva"], ["expiresAt", "Expira em"]]);
                });
            } else if (view === "audit") {
                content.innerHTML = table(await get("/api/platform/audit"), [["createdAt", "Data"], ["tenantName", "Cliente"], ["action", "Ação"], ["entityType", "Entidade"], ["reason", "Justificativa"]]);
            } else if (view === "usage") {
                content.innerHTML = table(await get("/api/platform/usage"), [["tenantName", "Organização"], ["activeUsers", "Usuários atuais"], ["userLimit", "Limite"], ["properties", "Propriedades"], ["propertyLimit", "Limite propriedades"], ["storageUsedMb", "Armazenamento MB"]]);
            } else if (view === "users") {
                content.innerHTML = (permissions.has("account.users.manage") ? '<button class="primary-button" data-create="user">Novo usuário</button>' : "") + usersTable(await get("/api/users"));
                wireUserActions();
            } else if (view === "roles") {
                const rows = await get("/api/roles");
                content.innerHTML = (permissions.has("account.roles.manage") ? '<button class="primary-button" data-create="role">Novo perfil</button>' : "") + table(rows, [["name", "Perfil"], ["level", "Nível"], ["permissions", "Permissões"], ["systemRole", "Sistema"]]);
            } else if (view === "invitations") {
                content.innerHTML = (permissions.has("account.invitations.manage") ? '<button class="primary-button" data-create="invitation">Novo convite</button>' : "") + table(await get("/api/invitations"), [["email", "E-mail"], ["roleName", "Perfil"], ["status", "Status"], ["deliveryStatus", "Comunicação"], ["expiresAt", "Expira em"]]);
            } else if (view === "notifications") {
                content.innerHTML = table(await get("/api/notifications"), [["priority", "Prioridade"], ["type", "Tipo"], ["title", "Título"], ["message", "Mensagem"], ["createdAt", "Data"]]);
            } else if (view === "security") {
                const [sessions, devices] = await Promise.all([get("/api/security/sessions"), get("/api/security/devices")]);
                content.innerHTML = "<h2>Sessões ativas</h2>" + table(sessions, [["device", "Dispositivo"], ["ipAddress", "IP"], ["lastSeenAt", "Última atividade"]]) + "<h2>Dispositivos autorizados</h2>" + table(devices, [["name", "Nome"], ["platform", "Plataforma"], ["lastSeenAt", "Última atividade"]]);
            } else if (view === "account") {
                const [organization, plan, usage] = await Promise.all([get("/api/account/organization"), get("/api/account/plan"), get("/api/account/usage")]);
                content.innerHTML = cards({ "Minha organização": organization.name, "Meu plano": plan.name, "Usuários": `${usage.activeUsers} / ${usage.userLimit}`, "Propriedades": `${usage.properties} / ${usage.propertyLimit}`, "Armazenamento": `${usage.storageUsedMb} / ${usage.storageLimitMb} MB` }) + (permissions.has("account.subscription.manage") ? '<button class="primary-button" data-create="upgrade">Solicitar alteração de plano</button>' : "");
            } else if (view === "settings") {
                const item = await get("/api/settings/organization");
                content.innerHTML = cards({ "Organização": item.organizationName, "Unidades": item.unitSystem, "Moeda": item.currency, "Fuso horário": item.timeZone, "Cultura": item.mainCulture, "Atividades": item.mainActivities.join(", ") });
            } else if (view === "onboarding") {
                const [organization, users, invitations] = await Promise.all([get("/api/account/organization"), get("/api/users"), get("/api/invitations")]);
                const administrator = users.some(item => item.roles.some(role => role === "Administrador do Cliente"));
                content.innerHTML = cards({ "Organização e responsável": organization.responsibleName ? "Concluído" : "Pendente", "Plano selecionado": organization.planName, "Administrador": administrator ? "Concluído" : "Pendente", "Convites de equipe": invitations.filter(item => item.status === "PENDING").length, "Situação": organization.status });
            }
        } catch {
            // get() já apresenta erro recuperável e referência de atendimento.
        } finally {
            content.setAttribute("aria-busy", "false");
            wireCreateActions();
        }
    }

    const ui = {
        "en-US": { title: "SaaS Platform", help: "How to use this screen", refresh: "Refresh", loading: "Loading data…" },
        "es-ES": { title: "Plataforma SaaS", help: "Cómo usar esta pantalla", refresh: "Actualizar", loading: "Cargando datos…" },
        "pt-BR": { title: "Plataforma SaaS", help: "Como usar esta tela", refresh: "Atualizar", loading: "Carregando dados…" }
    };

    function applyCulture(culture) {
        const translation = ui[culture] ?? ui["pt-BR"];
        document.documentElement.lang = culture;
        document.querySelector(".saas-page h1").textContent = isSuperAdministrator ? translation.title : "Administração da conta";
        document.querySelector(".contextual-help summary").textContent = translation.help;
        document.querySelector("#saas-refresh").textContent = translation.refresh;
        if (["Carregando", "Loading", "Cargando"].some(term => status.textContent.includes(term))) status.textContent = translation.loading;
    }

    document.querySelectorAll(".saas-tabs button").forEach(button => {
        const requiredPermission = normalizePermission(button.dataset.permission);
        const visible = isSuperAdministrator
            ? button.dataset.global === "true"
            : tenantViews.has(button.dataset.view) && button.dataset.global !== "true" && (!requiredPermission || permissions.has(requiredPermission));
        button.hidden = !visible;
        button.classList.toggle("active", visible && button.dataset.view === view);
        button.addEventListener("click", () => {
            document.querySelector(".saas-tabs .active")?.classList.remove("active");
            button.classList.add("active");
            view = button.dataset.view;
            load();
        });
    });
    document.querySelector("#saas-culture")?.addEventListener("change", event => {
        localStorage.setItem("agro360.culture", event.target.value);
        applyCulture(event.target.value);
        load();
    });
    const culture = localStorage.getItem("agro360.culture") ?? "pt-BR";
    document.querySelector("#saas-culture").value = culture;
    applyCulture(culture);
    document.querySelector("#saas-refresh").addEventListener("click", load);
    load();
})();
