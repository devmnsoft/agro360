(() => {
    "use strict";
    const content = document.querySelector("#saas-content");
    if (!content) return;
    const status = document.querySelector("#saas-status");
    const dialog = document.querySelector("#saas-dialog");
    const form = document.querySelector("#saas-form");
    const fields = document.querySelector("#dialog-fields");
    const session = (() => { try { return JSON.parse(localStorage.getItem("agro360.session")); } catch { return null; } })();
    const request = (path, options = {}) => window.agro360Api(path, options);
    const cache = { tenants: [], plans: [], users: [], roles: [] };
    let view = "dashboard";
    let operation = null;

    const helps = {
        dashboard: "Consulte indicadores globais e alertas. O acesso de suporte e toda ação administrativa são auditados.",
        tenants: "Cadastre e acompanhe organizações. CPF/CNPJ, responsável e plano são obrigatórios.",
        plans: "Defina limites, módulos e recursos comerciais. Cobrança externa não é simulada.",
        billing: "Acompanhe cobranças internas. Baixas são manuais, justificadas e auditadas.",
        features: "Confira recursos contratados por organização e a origem efetiva de cada liberação.",
        audit: "Consulte ações críticas e seus responsáveis.",
        users: "Gerencie somente usuários desta organização. Perfis e permissões são validados pelo backend.",
        roles: "Perfis de sistema são protegidos e ninguém pode conceder privilégios acima da própria alçada.",
        usage: "Compare consumo e limites contratados sem apagar dados existentes.",
        account: "Consulte o plano da organização e solicite upgrade para análise interna.",
        settings: "Configure preferências da organização; alterações ficam auditadas."
    };
    const escape = value => String(value ?? "").replace(/[&<>"']/g, char => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[char]));
    const cards = values => `<div class="saas-grid">${Object.entries(values).map(([key, value]) => `<article class="saas-card"><small>${escape(key)}</small><strong>${escape(value)}</strong></article>`).join("")}</div>`;
    const table = (rows, columns, actions) => rows.length
        ? `<div class="saas-card"><table class="saas-table"><thead><tr>${columns.map(column => `<th>${escape(column[1])}</th>`).join("")}${actions ? "<th>Ações</th>" : ""}</tr></thead><tbody>${rows.map(row => `<tr>${columns.map(column => `<td>${escape(Array.isArray(row[column[0]]) ? row[column[0]].join(", ") : row[column[0]])}</td>`).join("")}${actions ? `<td>${actions(row)}</td>` : ""}</tr>`).join("")}</tbody></table></div>`
        : '<div class="empty">Nenhum registro encontrado.</div>';
    const option = (value, label, selected = false) => `<option value="${escape(value)}" ${selected ? "selected" : ""}>${escape(label)}</option>`;
    const input = (label, name, type = "text", value = "", extra = "required") => `<label>${escape(label)}<input name="${escape(name)}" type="${escape(type)}" value="${escape(value)}" ${extra}></label>`;

    async function load() {
        content.setAttribute("aria-busy", "true");
        document.querySelector("#saas-help").textContent = helps[view] || "Use os filtros e ações disponíveis.";
        status.textContent = "Carregando…";
        try {
            if (view === "dashboard") {
                const item = await request("/api/platform/dashboard");
                content.innerHTML = cards({ "Organizações": item.totalOrganizations, "Ativas": item.activeOrganizations, "Suspensas": item.suspendedOrganizations, "Novos no mês": item.newThisMonth, "Usuários ativos": item.activeUsers, "Próximos do limite": item.nearLimit, "Acima do limite": item.aboveLimit, "Convites pendentes": item.pendingInvitations, "Logins recentes": item.recentLogins, "Alertas de segurança": item.securityAlerts, "Upgrades": item.upgradeRequests, "Suporte": item.supportRequests });
            } else if (view === "tenants") {
                cache.tenants = await request("/api/platform/tenants");
                content.innerHTML = '<button class="primary-button" data-create="tenant">Nova organização</button>' + table(cache.tenants, [["name", "Organização"], ["type", "Tipo"], ["document", "CPF/CNPJ"], ["responsibleName", "Responsável"], ["planName", "Plano"], ["status", "Status"]], row => `<button data-edit="tenant" data-key="${row.id}">Editar</button>`);
            } else if (view === "plans") {
                cache.plans = await request("/api/platform/plans");
                content.innerHTML = '<button class="primary-button" data-create="plan">Novo plano</button>' + table(cache.plans, [["name", "Plano"], ["monthlyPrice", "Mensal informativo"], ["userLimit", "Usuários"], ["propertyLimit", "Propriedades"], ["modules", "Módulos"], ["active", "Ativo"]], row => `<button data-edit="plan" data-key="${row.id}">Editar</button>`);
            } else if (view === "billing") {
                content.innerHTML = table(await request("/api/platform/billing"), [["tenantName", "Cliente"], ["planName", "Plano"], ["competence", "Período"], ["amount", "Valor"], ["dueOn", "Vencimento"], ["status", "Status"], ["paidOn", "Baixa manual"]]);
            } else if (view === "features") {
                cache.tenants = await request("/api/platform/tenants");
                content.innerHTML = `<label class="saas-card" title="Organização cujos módulos contratados serão consultados">Organização <select id="feature-tenant"><option value="">Selecione…</option>${cache.tenants.map(item => option(item.id, item.name)).join("")}</select></label><div id="feature-list" class="empty">Selecione uma organização.</div>`;
                document.querySelector("#feature-tenant").addEventListener("change", loadFeatures);
            } else if (view === "audit") {
                content.innerHTML = table(await request("/api/platform/audit"), [["createdAt", "Data"], ["tenantName", "Cliente"], ["action", "Ação"], ["entityType", "Entidade"], ["reason", "Justificativa"]]);
            } else if (view === "usage") {
                content.innerHTML = table(await request("/api/platform/usage"), [["tenantName", "Organização"], ["activeUsers", "Usuários atuais"], ["userLimit", "Limite"], ["properties", "Propriedades"], ["propertyLimit", "Limite propriedades"], ["storageUsedMb", "Armazenamento MB"]]);
            } else if (view === "users") {
                cache.users = await request("/api/users");
                content.innerHTML = '<button class="primary-button" data-create="user">Novo usuário</button>' + table(cache.users, [["name", "Nome"], ["email", "E-mail"], ["status", "Status"], ["roles", "Perfis"], ["lastAccess", "Último acesso"]], row => `<button data-edit="user" data-key="${row.id}">Editar</button>`);
            } else if (view === "roles") {
                cache.roles = await request("/api/roles");
                content.innerHTML = '<button class="primary-button" data-create="role">Novo perfil</button>' + table(cache.roles, [["name", "Perfil"], ["level", "Nível"], ["permissions", "Permissões"], ["systemRole", "Sistema"]], row => row.systemRole ? "Protegido" : `<button data-edit="role" data-key="${row.id}">Editar</button>`);
            } else if (view === "invitations") {
                content.innerHTML = '<button class="primary-button" data-create="invitation">Novo convite</button>' + table(await request("/api/invitations"), [["email", "E-mail"], ["roleName", "Perfil"], ["status", "Status"], ["expiresAt", "Expira em"]]);
            } else if (view === "notifications") {
                content.innerHTML = table(await request("/api/notifications"), [["priority", "Prioridade"], ["type", "Tipo"], ["title", "Título"], ["message", "Mensagem"], ["createdAt", "Data"]]);
            } else if (view === "security") {
                const [sessions, devices] = await Promise.all([request("/api/security/sessions"), request("/api/security/devices")]);
                content.innerHTML = "<h2>Sessões ativas</h2>" + table(sessions, [["device", "Dispositivo"], ["ipAddress", "IP"], ["lastSeenAt", "Última atividade"]]) + "<h2>Dispositivos autorizados</h2>" + table(devices, [["name", "Nome"], ["platform", "Plataforma"], ["lastSeenAt", "Última atividade"]]);
            } else if (view === "account") {
                const [organization, plan, usage] = await Promise.all([request("/api/account/organization"), request("/api/account/plan"), request("/api/account/usage")]);
                content.innerHTML = cards({ "Minha organização": organization.name, "Meu plano": plan.name, "Módulos contratados": plan.modules.join(", "), "Usuários": `${usage.activeUsers} / ${usage.userLimit}`, "Propriedades": `${usage.properties} / ${usage.propertyLimit}`, "Armazenamento": `${usage.storageUsedMb} / ${usage.storageLimitMb} MB` }) + '<button class="primary-button" data-create="upgrade">Solicitar upgrade</button>';
            } else if (view === "settings") {
                const item = await request("/api/settings/organization");
                content.innerHTML = cards({ "Organização": item.organizationName, "Unidades": item.unitSystem, "Moeda": item.currency, "Fuso horário": item.timeZone, "Cultura": item.mainCulture, "Atividades": item.mainActivities.join(", ") });
            } else if (view === "onboarding") {
                const [organization, users, invitations] = await Promise.all([request("/api/account/organization"), request("/api/users"), request("/api/invitations")]);
                const administrator = users.some(item => item.roles.some(role => role.includes("Administrador")));
                content.innerHTML = cards({ "Organização e responsável": organization.responsibleName ? "Concluído" : "Pendente", "Plano selecionado": organization.planName, "Administrador": administrator ? "Concluído" : "Pendente", "Convites de equipe": invitations.filter(item => item.status === "PENDING").length, "Situação": organization.status });
            }
            status.textContent = "Dados atualizados.";
        } catch (error) {
            status.textContent = "Falha no carregamento.";
            content.innerHTML = `<div class="error" role="alert">${escape(error.message)} <button id="retry">Tentar novamente</button></div>`;
            document.querySelector("#retry")?.addEventListener("click", load);
        } finally {
            content.setAttribute("aria-busy", "false");
        }
    }

    async function loadFeatures(event) {
        const box = document.querySelector("#feature-list");
        if (!event.target.value) { box.textContent = "Selecione uma organização."; return; }
        const flags = await request(`/api/platform/tenants/${encodeURIComponent(event.target.value)}/features`);
        box.innerHTML = table(flags, [["name", "Funcionalidade"], ["planEnabled", "Plano"], ["tenantEnabled", "Override"], ["effectiveOrigin", "Origem efetiva"], ["expiresAt", "Expira em"]]);
    }

    async function ensurePlans() { if (!cache.plans.length) cache.plans = await request("/api/platform/plans"); }
    async function ensureRoles() { if (!cache.roles.length) cache.roles = await request("/api/roles"); }
    function selected(kind, key) { return cache[`${kind}s`]?.find(item => item.id === key) ?? null; }

    async function openForm(kind, item = null) {
        operation = { kind, item };
        form.querySelector(".form-message").textContent = "";
        document.querySelector("#dialog-title").textContent = `${item ? "Editar" : "Novo"} ${kind === "tenant" ? "organização" : kind === "plan" ? "plano" : kind === "user" ? "usuário" : kind === "role" ? "perfil" : kind === "invitation" ? "convite" : "pedido de upgrade"}`;
        if (kind === "tenant") {
            await ensurePlans();
            fields.innerHTML = input("Nome da organização", "organizationName", "text", item?.name) + (!item ? input("Identificador", "organizationSlug", "text", "", 'required pattern="[a-z0-9]+(?:-[a-z0-9]+)*"') + input("CPF/CNPJ", "organizationDocument", "text") : "") + input("Tipo", "organizationType", "text", item?.type || "RURAL_PRODUCER") + input("Responsável", "responsibleName", "text", item?.responsibleName) + input("E-mail do responsável", "responsibleEmail", "email", item?.responsibleEmail) + `<label>Plano<select name="planChoice" required>${cache.plans.filter(plan => plan.active || plan.id === item?.planId).map(plan => option(plan.id, `${plan.name} · ${plan.modules.join(", ")}`, plan.id === item?.planId)).join("")}</select></label>`;
        } else if (kind === "plan") {
            fields.innerHTML = input("Nome", "planName", "text", item?.name) + input("Descrição", "planDescription", "text", item?.description) + input("Mensalidade informativa", "monthlyPrice", "number", item?.monthlyPrice ?? 0, 'required min="0" step="0.01"') + input("Anuidade informativa", "annualPrice", "number", item?.annualPrice ?? 0, 'required min="0" step="0.01"') + input("Limite de usuários", "userLimit", "number", item?.userLimit ?? 1, 'required min="1"') + input("Limite de propriedades", "propertyLimit", "number", item?.propertyLimit ?? 1, 'required min="1"') + input("Armazenamento (MB)", "storageLimitMb", "number", item?.storageLimitMb ?? 1024, 'required min="1"') + input("Dispositivos", "deviceLimit", "number", item?.deviceLimit ?? 1, 'required min="1"') + input("Módulos contratados (separados por vírgula)", "moduleSelection", "text", item?.modules?.join(", ") || "dashboard") + input("Recursos premium (separados por vírgula)", "premiumSelection", "text", item?.premiumFeatures?.join(", ") || "", "") + `<label><input name="active" type="checkbox" ${item?.active !== false ? "checked" : ""}> Plano ativo</label>`;
        } else if (kind === "user") {
            await ensureRoles();
            fields.innerHTML = input("Nome", "userName", "text", item?.name) + input("E-mail", "userEmail", "email", item?.email) + `<fieldset><legend>Perfis</legend>${cache.roles.map(role => `<label><input type="checkbox" name="roleChoice" value="${role.id}" ${item?.roles?.includes(role.name) ? "checked" : ""}> ${escape(role.name)}</label>`).join("")}</fieldset>`;
        } else if (kind === "role") {
            const permissions = [...new Set([...(session?.permissions ?? []), ...(item?.permissions ?? [])])].sort();
            fields.innerHTML = input("Nome", "roleName", "text", item?.name) + input("Nível", "roleLevel", "number", item?.level ?? 10, 'required min="1" max="90"') + `<fieldset><legend>Permissões</legend>${permissions.map(permission => `<label><input type="checkbox" name="permissionChoice" value="${escape(permission)}" ${item?.permissions?.includes(permission) ? "checked" : ""}> ${escape(permission)}</label>`).join("")}</fieldset>`;
        } else if (kind === "invitation") {
            await ensureRoles();
            fields.innerHTML = input("E-mail", "invitationEmail", "email") + `<label>Perfil<select name="invitationRole" required>${cache.roles.map(role => option(role.id, role.name)).join("")}</select></label>` + input("Validade em horas", "validForHours", "number", 72, 'required min="1" max="168"');
        } else {
            await ensurePlans();
            fields.innerHTML = `<label>Plano desejado<select name="upgradePlan" required>${cache.plans.filter(plan => plan.active).map(plan => option(plan.id, `${plan.name} · ${plan.modules.join(", ")}`)).join("")}</select></label>` + `<label>Justificativa<textarea name="upgradeReason" required minlength="5"></textarea></label><p>Nenhuma cobrança ou pagamento será executado. A solicitação seguirá para análise interna.</p>`;
        }
        dialog.showModal();
        fields.querySelector("input,select,textarea")?.focus();
    }

    form.addEventListener("submit", async event => {
        event.preventDefault();
        if (!form.reportValidity() || !operation) return;
        const button = form.querySelector(".primary-button");
        const message = form.querySelector(".form-message");
        const data = new FormData(form);
        button.disabled = true;
        message.textContent = "Salvando…";
        try {
            let path;
            let method = operation.item ? "PUT" : "POST";
            let body;
            if (operation.kind === "tenant") {
                path = operation.item ? `/api/platform/tenants/${operation.item.id}` : "/api/platform/tenants";
                body = { name: data.get("organizationName"), type: data.get("organizationType"), responsibleName: data.get("responsibleName"), responsibleEmail: data.get("responsibleEmail"), planId: data.get("planChoice") };
                if (!operation.item) Object.assign(body, { slug: data.get("organizationSlug"), document: data.get("organizationDocument") });
            } else if (operation.kind === "plan") {
                path = operation.item ? `/api/platform/plans/${operation.item.id}` : "/api/platform/plans";
                body = { name: data.get("planName"), description: data.get("planDescription"), monthlyPrice: Number(data.get("monthlyPrice")), annualPrice: Number(data.get("annualPrice")), userLimit: Number(data.get("userLimit")), propertyLimit: Number(data.get("propertyLimit")), storageLimitMb: Number(data.get("storageLimitMb")), deviceLimit: Number(data.get("deviceLimit")), modules: String(data.get("moduleSelection")).split(",").map(value => value.trim()).filter(Boolean), premiumFeatures: String(data.get("premiumSelection")).split(",").map(value => value.trim()).filter(Boolean), active: data.has("active") };
            } else if (operation.kind === "user") {
                path = operation.item ? `/api/users/${operation.item.id}` : "/api/users";
                body = { name: data.get("userName"), email: data.get("userEmail"), roleIds: data.getAll("roleChoice") };
            } else if (operation.kind === "role") {
                path = operation.item ? `/api/roles/${operation.item.id}` : "/api/roles";
                body = { name: data.get("roleName"), level: Number(data.get("roleLevel")), permissions: data.getAll("permissionChoice") };
            } else if (operation.kind === "invitation") {
                path = "/api/invitations";
                body = { email: data.get("invitationEmail"), roleId: data.get("invitationRole"), validForHours: Number(data.get("validForHours")) };
            } else {
                path = "/api/account/upgrade-requests";
                body = { requestedPlanId: data.get("upgradePlan"), reason: data.get("upgradeReason") };
            }
            await request(path, { method, body: JSON.stringify(body) });
            dialog.close();
            cache.tenants = []; cache.plans = []; cache.users = []; cache.roles = [];
            window.toastSuccess?.("Alteração salva", "A operação foi persistida e registrada para auditoria.");
            await load();
        } catch (error) {
            message.textContent = error.message;
        } finally {
            button.disabled = false;
        }
    });

    content.addEventListener("click", event => {
        const create = event.target.closest("[data-create]");
        if (create) { openForm(create.dataset.create); return; }
        const edit = event.target.closest("[data-edit]");
        if (edit) openForm(edit.dataset.edit, selected(edit.dataset.edit, edit.dataset.key));
    });
    document.querySelectorAll(".saas-tabs button").forEach(button => button.addEventListener("click", () => { document.querySelector(".saas-tabs .active").classList.remove("active"); button.classList.add("active"); view = button.dataset.view; load(); }));
    document.querySelector("#saas-refresh").addEventListener("click", load);
    document.querySelector("#saas-culture")?.addEventListener("change", event => { localStorage.setItem("agro360.culture", event.target.value); document.documentElement.lang = event.target.value; });
    const culture = localStorage.getItem("agro360.culture") || "pt-BR";
    document.querySelector("#saas-culture").value = culture;
    document.documentElement.lang = culture;
    load();
})();
