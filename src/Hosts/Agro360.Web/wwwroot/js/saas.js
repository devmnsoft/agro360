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
    const requestedView = new URLSearchParams(location.search).get("view");
    let view = requestedView && (isSuperAdministrator || tenantViews.has(requestedView))
        ? requestedView
        : isSuperAdministrator ? "dashboard" : permissions.has("account.users.read") ? "users" : "account";

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

    function wireTenantActions() {
        content.querySelectorAll("[data-tenant-action]").forEach(btn => {
            btn.addEventListener("click", async () => {
                const tenantId = btn.dataset.tenantId;
                const tenantName = btn.dataset.tenantName;
                const action = btn.dataset.tenantAction;
                if (action === "inspect") {
                    openForm(`Inspecionar: ${escapeHtml(tenantName)}`,
                        `<p>Iniciará uma sessão de suporte assistido (2h). Todas as ações ficam registradas na auditoria.</p>
                         <label>Motivo <textarea name="reason" minlength="5" maxlength="1000" required></textarea></label>`,
                        async data => {
                            const result = await request(`/api/platform/tenants/${encodeURIComponent(tenantId)}/support-session`, { method: "POST", body: JSON.stringify({ reason: data.get("reason") }) });
                            // Show persistent support banner
                            let banner = document.querySelector("#support-banner");
                            if (!banner) {
                                banner = document.createElement("div");
                                banner.id = "support-banner";
                                banner.style.cssText = "position:fixed;top:0;left:0;width:100%;background:#d97706;color:#fff;padding:0.5rem 1rem;display:flex;align-items:center;gap:1rem;z-index:9999;font-weight:600;";
                                document.body.prepend(banner);
                            }
                            banner.innerHTML = `⚠️ Contexto de suporte ativo: <strong>${escapeHtml(result.tenantName)}</strong> (expira ${new Date(result.expiresAt).toLocaleTimeString("pt-BR")}) <button id="end-support-btn" style="margin-left:auto;background:#fff;color:#d97706;border:none;padding:0.25rem 0.75rem;border-radius:4px;cursor:pointer;font-weight:700">Encerrar</button>`;
                            document.querySelector("#end-support-btn").addEventListener("click", async () => {
                                await request(`/api/platform/tenants/${encodeURIComponent(tenantId)}/support-session/end`, { method: "POST" }).catch(() => {});
                                banner.remove();
                            });
                            return `Contexto de suporte iniciado. Token válido por 2h. Todas as ações são auditadas.`;
                        });
                } else {
                    const statusMap = { block: "BLOCKED", suspend: "SUSPENDED", activate: "ACTIVE" };
                    const labelMap = { block: "Bloquear", suspend: "Suspender", activate: "Ativar" };
                    openForm(`${labelMap[action]}: ${escapeHtml(tenantName)}`,
                        `<label>Justificativa <textarea name="reason" minlength="5" maxlength="1000" required></textarea></label>`,
                        async data => {
                            const endpoint = action === "activate" ? "activate" : action === "suspend" ? "suspend" : "block";
                            await request(`/api/platform/tenants/${encodeURIComponent(tenantId)}/${endpoint}`, { method: "POST", body: JSON.stringify({ reason: data.get("reason") }) });
                        });
                }
            });
        });
    }

    function billingTable(rows) {
        if (!rows.length) return '<div class="empty">Nenhuma cobrança encontrada.</div>';
        return `<div class="saas-card"><table class="saas-table"><thead><tr><th>Cliente</th><th>Competência</th><th>Valor</th><th>Recebido</th><th>Em aberto</th><th>Vencimento</th><th>Status</th><th>Ações</th></tr></thead><tbody>${rows.map(charge => `<tr><td>${escapeHtml(charge.tenantName)}</td><td>${escapeHtml(charge.competence)}</td><td>${escapeHtml(charge.amount)}</td><td>${escapeHtml(charge.paidAmount)}</td><td>${escapeHtml(charge.outstandingAmount)}</td><td>${escapeHtml(charge.dueOn)}</td><td>${escapeHtml(charge.status)}</td><td>${!["PAID", "CANCELLED"].includes(charge.status) ? `<button type="button" class="primary-button" data-charge-payment="${escapeHtml(charge.id)}" data-charge-name="${escapeHtml(charge.tenantName)}" data-charge-outstanding="${escapeHtml(charge.outstandingAmount)}">Registrar pagamento</button>` : "—"}</td></tr>`).join("")}</tbody></table></div>`;
    }

    function wireBillingActions() {
        content.querySelectorAll("[data-charge-payment]").forEach(button => button.addEventListener("click", () => {
            openForm("Registrar pagamento manual", `<p>Cliente: <strong>${escapeHtml(button.dataset.chargeName)}</strong><br>Saldo atual: <strong>${escapeHtml(button.dataset.chargeOutstanding)}</strong></p><p>Este registro não emite boleto, PIX ou documento fiscal. Excedentes serão preservados como crédito.</p><label>Identificação do pagamento <input name="reference" maxlength="160" required></label><label>Valor recebido <input name="amount" type="number" min="0.01" step="0.01" required></label><label>Data do pagamento <input name="paidOn" type="date" required></label><label>Observação <textarea name="observation" minlength="5" maxlength="1000" required></textarea></label>`, async data => {
                const result = await request(`/api/platform/billing/${encodeURIComponent(button.dataset.chargePayment)}/payments`, { method: "POST", body: JSON.stringify({ idempotencyKey: crypto.randomUUID(), paymentReference: data.get("reference"), amount: Number(data.get("amount")), paidOn: data.get("paidOn"), observation: data.get("observation") }) });
                return result.creditAmount > 0 ? `Pagamento conciliado. Crédito preservado: ${result.creditAmount}.` : "Pagamento conciliado com sucesso.";
            });
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
        const [plans, modules] = await Promise.all([
            request("/api/platform/plans"),
            request("/api/platform/modules").catch(() => [])
        ]);
        const modulesHtml = modules.length
            ? `<fieldset><legend>Módulos adicionais</legend><div class="saas-permission-grid">${modules.map(m => `<label><input type="checkbox" name="modules" value="${escapeHtml(m.code)}"> ${escapeHtml(m.name)}</label>`).join("")}</div></fieldset>`
            : ``;
        openForm("Nova organização",
            `<label>Identificador <input name="slug" pattern="[a-z0-9]+(?:-[a-z0-9]+)*" maxlength="80" required></label>
             <label>Razão social <input name="name" maxlength="180" required></label>
             <label>Tipo <select name="type" required>${["PRODUCER","COOPERATIVE","AGRIBUSINESS","CONSULTANCY","DISTRIBUTOR","CARRIER","OTHER"].map(v => option(v, v)).join("")}</select></label>
             <label>CPF/CNPJ <input name="document" maxlength="18" required></label>
             <label>Responsável <input name="responsibleName" maxlength="160" required></label>
             <label>E-mail do administrador <input name="responsibleEmail" type="email" maxlength="254" required></label>
             <label>Plano <select name="plan" required><option value="">Selecione…</option>${plans.map(p => option(p.id, p.name)).join("")}</select></label>
             ${modulesHtml}`,
            async data => {
                const selectedModules = data.getAll("modules");
                const payload = { slug: data.get("slug"), name: data.get("name"), type: data.get("type"), document: data.get("document"), responsibleName: data.get("responsibleName"), responsibleEmail: data.get("responsibleEmail"), planId: data.get("plan"), modules: selectedModules.length ? selectedModules : undefined };
                const result = await request("/api/platform/tenants", { method: "POST", body: JSON.stringify(payload) });
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
                content.innerHTML =
                    `<div class="saas-grid">
                        <article class="saas-card" role="button" tabindex="0" data-nav-view="tenants" title="Ver organizações">
                          <small>Organizações</small><strong>${escapeHtml(item.totalOrganizations)}</strong>
                        </article>
                        <article class="saas-card"><small>Ativas</small><strong>${escapeHtml(item.activeOrganizations)}</strong></article>
                        <article class="saas-card" data-nav-view="tenants"><small>Suspensas</small><strong>${escapeHtml(item.suspendedOrganizations)}</strong></article>
                        <article class="saas-card"><small>Novos no mês</small><strong>${escapeHtml(item.newThisMonth)}</strong></article>
                        <article class="saas-card"><small>Usuários ativos</small><strong>${escapeHtml(item.activeUsers)}</strong></article>
                        <article class="saas-card"><small>Próximos do limite</small><strong>${escapeHtml(item.nearLimit)}</strong></article>
                        <article class="saas-card"><small>Acima do limite</small><strong>${escapeHtml(item.aboveLimit)}</strong></article>
                        <article class="saas-card"><small>Convites pendentes</small><strong>${escapeHtml(item.pendingInvitations)}</strong></article>
                        <article class="saas-card" data-nav-view="audit"><small>Alertas de segurança</small><strong>${escapeHtml(item.securityAlerts)}</strong></article>
                        <article class="saas-card"><small>Upgrades</small><strong>${escapeHtml(item.upgradeRequests)}</strong></article>
                        <article class="saas-card"><small>Suporte</small><strong>${escapeHtml(item.supportRequests)}</strong></article>
                    </div>`;
                content.querySelectorAll("[data-nav-view]").forEach(card => {
                    card.style.cursor = "pointer";
                    card.addEventListener("click", () => {
                        view = card.dataset.navView;
                        history.replaceState(null, "", `${location.pathname}?view=${encodeURIComponent(view)}`);
                        load();
                    });
                });
            } else if (view === "tenants") {
                const rows = await get("/api/platform/tenants");
                let filtered = rows;
                const renderTenantsTable = () => {
                    const q = document.querySelector("#tenant-search")?.value.trim().toLowerCase() ?? "";
                    filtered = q ? rows.filter(r => r.name.toLowerCase().includes(q) || r.slug?.toLowerCase().includes(q) || r.document?.includes(q)) : rows;
                    document.querySelector("#tenant-list").innerHTML = filtered.length
                        ? `<div class="saas-card"><table class="saas-table"><thead><tr><th>Organização</th><th>Tipo</th><th>CPF/CNPJ</th><th>Responsável</th><th>Plano</th><th>Status</th><th>Ações</th></tr></thead><tbody>
                            ${filtered.map(row => `<tr>
                              <td>${escapeHtml(row.name)}</td><td>${escapeHtml(row.type)}</td><td>${escapeHtml(row.document)}</td>
                              <td>${escapeHtml(row.responsibleName)}</td><td>${escapeHtml(row.planName)}</td><td>${escapeHtml(row.status)}</td>
                              <td class="saas-actions">
                                ${row.status !== "BLOCKED" ? `<button type="button" class="danger-button" data-tenant-action="block" data-tenant-id="${escapeHtml(row.id)}" data-tenant-name="${escapeHtml(row.name)}">Bloquear</button>` : ""}
                                ${row.status !== "SUSPENDED" && row.status !== "BLOCKED" ? `<button type="button" class="secondary-button" data-tenant-action="suspend" data-tenant-id="${escapeHtml(row.id)}" data-tenant-name="${escapeHtml(row.name)}">Suspender</button>` : ""}
                                ${row.status !== "ACTIVE" ? `<button type="button" class="primary-button" data-tenant-action="activate" data-tenant-id="${escapeHtml(row.id)}" data-tenant-name="${escapeHtml(row.name)}">Ativar</button>` : ""}
                                <button type="button" class="secondary-button" data-tenant-action="inspect" data-tenant-id="${escapeHtml(row.id)}" data-tenant-name="${escapeHtml(row.name)}">Inspecionar</button>
                              </td>
                            </tr>`).join("")}
                           </tbody></table></div>`
                        : '<div class="empty">Nenhum resultado.</div>';
                    wireTenantActions();
                };
                content.innerHTML =
                    `<div class="saas-toolbar">
                       <button class="primary-button" data-create="tenant">Nova organização</button>
                       <input id="tenant-search" type="search" placeholder="Filtrar por nome, slug ou CPF/CNPJ…" style="flex:1;padding:0.4rem 0.6rem">
                     </div>
                     <div id="tenant-list"></div>`;
                renderTenantsTable();
                document.querySelector("#tenant-search").addEventListener("input", renderTenantsTable);
            } else if (view === "plans") {
                const rows = await get("/api/platform/plans");
                content.innerHTML = '<button class="primary-button" data-create="plan">Novo plano</button>' + table(rows, [["name", "Plano"], ["monthlyPrice", "Mensal informativo"], ["userLimit", "Usuários"], ["propertyLimit", "Propriedades"], ["deviceLimit", "Dispositivos"], ["active", "Ativo"]]);
            } else if (view === "billing") {
                content.innerHTML = billingTable(await get("/api/platform/billing"));
                wireBillingActions();
            } else if (view === "features") {
                const tenants = await get("/api/platform/tenants");
                content.innerHTML =
                    `<label class="saas-card">Cliente <select id="feature-tenant"><option value="">Selecione…</option>${tenants.map(item => `<option value="${escapeHtml(item.id)}">${escapeHtml(item.name)}</option>`).join("")}</select></label>
                     <div id="feature-list" class="empty">Selecione um cliente para ver as funcionalidades.</div>`;
                document.querySelector("#feature-tenant").addEventListener("change", async event => {
                    const box = document.querySelector("#feature-list");
                    if (!event.target.value) { box.innerHTML = "Selecione um cliente para ver as funcionalidades."; return; }
                    const tenantId = event.target.value;
                    const tenantName = event.target.options[event.target.selectedIndex].text;
                    const flags = await get(`/api/platform/tenants/${encodeURIComponent(tenantId)}/features`);
                    box.innerHTML =
                        `<table class="saas-table"><thead><tr><th>Funcionalidade</th><th>Plano</th><th>Override</th><th>Origem</th><th>Expira</th><th>Ação</th></tr></thead><tbody>
                         ${flags.map(f => `<tr><td>${escapeHtml(f.name)}</td><td>${f.planEnabled ? "Sim" : "Não"}</td><td>${f.tenantEnabled == null ? "—" : f.tenantEnabled ? "Ativo" : "Inativo"}</td><td>${escapeHtml(f.effectiveOrigin)}</td><td>${f.expiresAt ? new Date(f.expiresAt).toLocaleDateString("pt-BR") : "—"}</td>
                           <td><button type="button" class="secondary-button" data-feature-id="${escapeHtml(f.id)}" data-feature-name="${escapeHtml(f.name)}" data-tenant-id="${escapeHtml(tenantId)}" data-tenant-name="${escapeHtml(tenantName)}">Override</button></td></tr>`).join("")}
                         </tbody></table>`;
                    box.querySelectorAll("[data-feature-id]").forEach(btn => btn.addEventListener("click", () => {
                        openForm(`Override: ${escapeHtml(btn.dataset.featureName)}`,
                            `<p>Cliente: <strong>${escapeHtml(btn.dataset.tenantName)}</strong></p>
                             <label>Estado <select name="enabled"><option value="true">Habilitar</option><option value="false">Desabilitar</option></select></label>
                             <label>Justificativa <textarea name="reason" minlength="5" maxlength="1000" required></textarea></label>
                             <label>Expira em <input name="expiresAt" type="datetime-local" required></label>`,
                            async data => {
                                await request("/api/platform/features/override", { method: "PUT", body: JSON.stringify({ tenantId: btn.dataset.tenantId, featureId: btn.dataset.featureId, enabled: data.get("enabled") === "true", reason: data.get("reason"), expiresAt: new Date(data.get("expiresAt")).toISOString() }) });
                            });
                    }));
                });
            } else if (view === "audit") {
                const tenants = await get("/api/platform/tenants").catch(() => []);
                const buildAuditUrl = () => {
                    const params = new URLSearchParams();
                    const tid = document.querySelector("#audit-tenant")?.value; if (tid) params.set("tenantId", tid);
                    const act = document.querySelector("#audit-action")?.value.trim(); if (act) params.set("action", act);
                    const frm = document.querySelector("#audit-from")?.value; if (frm) params.set("from", frm);
                    const unt = document.querySelector("#audit-until")?.value; if (unt) params.set("until", unt);
                    return `/api/platform/audit${params.size ? "?" + params.toString() : ""}`;
                };
                const renderAudit = async () => {
                    const rows = await get(buildAuditUrl());
                    document.querySelector("#audit-list").innerHTML = table(rows, [["createdAt", "Data"], ["tenantName", "Cliente"], ["action", "Ação"], ["entityType", "Entidade"], ["reason", "Justificativa"]]);
                };
                content.innerHTML =
                    `<div class="saas-toolbar" style="flex-wrap:wrap;gap:0.5rem">
                       <select id="audit-tenant" style="flex:1"><option value="">Todos os clientes</option>${tenants.map(t => `<option value="${escapeHtml(t.id)}">${escapeHtml(t.name)}</option>`).join("")}</select>
                       <input id="audit-action" type="search" placeholder="Ação (ex: TENANT_STATUS_CHANGED)…" style="flex:1">
                       <label style="display:flex;align-items:center;gap:0.25rem">De <input id="audit-from" type="date" style="width:auto"></label>
                       <label style="display:flex;align-items:center;gap:0.25rem">Até <input id="audit-until" type="date" style="width:auto"></label>
                       <button id="audit-filter" class="primary-button" type="button">Filtrar</button>
                     </div>
                     <div id="audit-list"></div>`;
                await renderAudit();
                document.querySelector("#audit-filter").addEventListener("click", renderAudit);
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
                const sessionRows = sessions.length
                    ? `<div class="saas-card"><table class="saas-table"><thead><tr><th>Dispositivo</th><th>IP</th><th>Última atividade</th><th>Ação</th></tr></thead><tbody>${sessions.map(s => `<tr><td>${escapeHtml(s.device)}</td><td>${escapeHtml(s.ipAddress)}</td><td>${escapeHtml(s.lastSeenAt)}</td><td>${!s.revokedAt ? `<button type="button" class="danger-button" data-revoke-session="${escapeHtml(s.id)}">Revogar</button>` : "Revogada"}</td></tr>`).join("")}</tbody></table></div>`
                    : '<div class="empty">Nenhuma sessão ativa.</div>';
                const deviceRows = devices.length
                    ? `<div class="saas-card"><table class="saas-table"><thead><tr><th>Nome</th><th>Plataforma</th><th>Última atividade</th><th>Ação</th></tr></thead><tbody>${devices.map(d => `<tr><td>${escapeHtml(d.name)}</td><td>${escapeHtml(d.platform)}</td><td>${escapeHtml(d.lastSeenAt)}</td><td>${!d.revokedAt ? `<button type="button" class="danger-button" data-revoke-device="${escapeHtml(d.id)}">Revogar</button>` : "Revogado"}</td></tr>`).join("")}</tbody></table></div>`
                    : '<div class="empty">Nenhum dispositivo cadastrado.</div>';
                content.innerHTML = `<h2>Sessões ativas</h2>${sessionRows}<h2>Dispositivos autorizados</h2>${deviceRows}`;
                content.querySelectorAll("[data-revoke-session]").forEach(btn => btn.addEventListener("click", async () => {
                    btn.disabled = true;
                    try { await request(`/api/security/sessions/${encodeURIComponent(btn.dataset.revokeSession)}/revoke`, { method: "POST" }); await load(); }
                    catch (e) { btn.disabled = false; status.textContent = e.message; }
                }));
                content.querySelectorAll("[data-revoke-device]").forEach(btn => btn.addEventListener("click", async () => {
                    btn.disabled = true;
                    try { await request(`/api/security/devices/${encodeURIComponent(btn.dataset.revokeDevice)}/revoke`, { method: "POST" }); await load(); }
                    catch (e) { btn.disabled = false; status.textContent = e.message; }
                }));
            } else if (view === "account") {
                const [organization, plan, usage] = await Promise.all([get("/api/account/organization"), get("/api/account/plan"), get("/api/account/usage")]);
                content.innerHTML = cards({ "Minha organização": organization.name, "Meu plano": plan.name, "Usuários": `${usage.activeUsers} / ${usage.userLimit}`, "Propriedades": `${usage.properties} / ${usage.propertyLimit}`, "Armazenamento": `${usage.storageUsedMb} / ${usage.storageLimitMb} MB` }) + (permissions.has("account.subscription.manage") ? '<button class="primary-button" data-create="upgrade">Solicitar alteração de plano</button>' : "");
            } else if (view === "settings") {
                const item = await get("/api/settings/organization");
                content.innerHTML =
                    cards({ "Organização": item.organizationName, "Unidades": item.unitSystem, "Moeda": item.currency, "Fuso horário": item.timeZone, "Cultura": item.mainCulture, "Atividades": item.mainActivities.join(", ") }) +
                    (permissions.has("account.settings.manage") || isSuperAdministrator ? '<button class="secondary-button" id="settings-edit-btn">Editar configurações</button>' : "");
                document.querySelector("#settings-edit-btn")?.addEventListener("click", () => openForm("Editar configurações",
                    `<label>Nome da organização <input name="organizationName" value="${escapeHtml(item.organizationName)}" maxlength="180" required></label>
                     <label>Moeda (3 letras) <input name="currency" value="${escapeHtml(item.currency)}" maxlength="3" minlength="3" required></label>
                     <label>Fuso horário <input name="timeZone" value="${escapeHtml(item.timeZone)}" required></label>`,
                    async data => {
                        const payload = { ...item, organizationName: data.get("organizationName"), currency: data.get("currency"), timeZone: data.get("timeZone") };
                        await request("/api/settings/organization", { method: "PUT", body: JSON.stringify(payload) });
                    }));
            } else if (view === "onboarding") {
                let organization, users, invitations;
                try { organization = await get("/api/account/organization"); } catch { organization = {}; }
                try { users = await get("/api/users"); } catch { users = []; }
                try { invitations = await get("/api/invitations"); } catch { invitations = []; }
                // Detect admin by role code, not display name (display name may vary)
                const administrator = users.some(u => u.roles && u.roles.some(r => typeof r === "string" ? r.toLowerCase().includes("administrator") || r.toLowerCase().includes("administrador") : r?.code?.toLowerCase() === "tenant-administrator"));
                content.innerHTML = cards({
                    "Organização e responsável": organization.responsibleName ? "Concluído" : "Pendente",
                    "Plano selecionado": organization.planName ?? "Pendente",
                    "Administrador": administrator ? "Concluído" : "Pendente",
                    "Convites de equipe": invitations.filter(i => i.status === "PENDING").length,
                    "Situação": organization.status ?? "—"
                }) + (permissions.has("account.invitations.manage") ? '<button class="primary-button" data-create="invitation">Convidar membro da equipe</button>' : "");
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
            history.replaceState(null, "", `${location.pathname}?view=${encodeURIComponent(view)}`);
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
