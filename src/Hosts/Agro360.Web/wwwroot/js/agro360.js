(() => {
    "use strict";

    const apiBase = document.querySelector('meta[name="api-base"]')?.content?.replace(/\/$/, "") ?? "http://localhost:8081";
    const apiUnavailableMessage = "A API do Agro360 está indisponível. Tente novamente em instantes ou contate o suporte.";
    const storageKeys = { session: "agro360.session", theme: "agro360.theme", culture: "agro360.culture" };
    const state = { session: readJson(storageKeys.session), searchTimer: 0, selectedSearch: -1, refreshPromise: null, refreshStopped: false, activeIncidents: new Set() };
    window.agro360Session = state.session ?? null;
    // Idioma da interface na mesma convenção de forms.js/saas.js (localStorage agro360.culture).
    // O idioma só muda apresentação: permissões, plano, moeda contratual (BRL) e estados persistidos
    // são independentes da cultura; as formatações abaixo acompanham o idioma e nunca o definem.
    const supportedCultures = ["pt-BR", "en-US", "es-ES", "fr-FR"];
    const currentCulture = () => {
        const stored = localStorage.getItem(storageKeys.culture);
        return supportedCultures.includes(stored) ? stored : "pt-BR";
    };
    // Textos estáticos do shell (sidebar, busca, diálogos, login, alternador de contexto) nas quatro línguas aprovadas.
    // O pt-BR é o próprio HTML original (fallback capturado em tempo de execução em data-i18nFallback):
    // chave ausente nunca deixa o elemento vazio — a apresentação muda, nunca os estados persistidos.
    const uiTranslations = {
        "en-US": {
            "g.start": "Home", "l.home": "Home / Dashboard", "l.tasks": "Tasks & Alerts", "l.deploy": "Deployment Center",
            "g.prod": "Production & Field", "l.props": "Farms & Units", "l.agri": "Agriculture", "l.field": "Field Operations",
            "l.maps": "Operations Map", "l.harv": "Harvest & Receipt", "l.costs": "Season Costs", "l.prod2": "Agri-industrial Production",
            "g.peq": "Livestock", "l.live": "Livestock",
            "g.stock": "Stock & Purchasing", "l.inv": "Inventory & Warehouse", "l.repl": "Replenishment & Needs", "l.proc": "Purchasing & Suppliers",
            "g.com": "Commercial", "l.comm": "Sales & Commerce", "l.crm": "CRM & Customer Cycle", "l.after": "After-sales",
            "g.log": "Logistics", "l.logi": "Dispatch & Delivery",
            "g.fin": "Finance/Tax", "l.fin2": "Finance & Costs", "l.fisc": "Tax", "l.docs": "Documents & Evidence",
            "g.qual": "Quality & Traceability", "l.comp": "Quality & Compliance", "l.insp": "Templates & Inspections",
            "g.people": "People & Resources", "l.hr": "Rural HR", "l.sst": "Rural OHS", "l.fleet": "Fleet & Maintenance",
            "g.coop": "Cooperatives", "l.coop2": "Cooperatives & Marketplace",
            "g.sus": "Sustainability/Export", "l.sus2": "Sustainability & ESG", "l.exp": "Export & Trading",
            "g.rep": "Reports", "l.rep2": "Reports & Analytics", "l.int": "Agro360 Intelligence", "l.exe": "Executive Panel 360",
            "g.org": "My organization", "l.org2": "My Organization", "l.users": "Users & Profiles", "l.mods": "My Modules",
            "l.sett": "Settings", "l.integ": "Integrations", "l.sup": "Help / Support",
            "g.glob": "Global administration", "l.glob2": "MNSOFT Global Administration",
            "c.ctx": "Active context", "c.switch": "Change context",
            "t.search": "Search animal, farm, product, season...", "t.palette": "What are you looking for?",
            "t.paletteHint": "Type at least two characters to search across the operation.", "t.perms": "Results respect your permissions",
            "a.menu": "Open menu", "a.theme": "Toggle theme", "a.alerts": "Compliance alerts", "a.lang": "Interface language", "a.palette": "Global search",
            "b.end": "End context",
            "d.help": "How this screen works", "d.hint": "quick guidance",
            "lg.secure": "Secure access", "lg.title": "Welcome to Agro360",
            "lg.sub": "Your access is validated directly against the database and permissions are loaded according to your profile.",
            "lg.tenant": "Client / Organization", "lg.tenantHelp": "The unique identifier of your company's environment.",
            "lg.email": "Email, CPF or CNPJ", "lg.emailHelp": "Documents may be entered with or without formatting.",
            "lg.password": "Password", "lg.show": "Show", "lg.hide": "Hide",
            "lg.first": "First access or global administration", "lg.mfa": "MFA code", "lg.mfaHelp": "Required for global administration.",
            "lg.newpw": "New password", "lg.pwHelp": "Fill in when the initial access requires a password change.", "lg.newpw2": "Confirm new password",
            "lg.enter": "Sign in to Agro360", "lg.test": "Test API connection",
            "lg.footer": "Agro360 by MNSOFT · Encrypted session, isolated per client.",
            "d.csUnit": "Unit filter", "d.csTitle": "Switch operational context",
            "d.csDesc": "Select the farm or work unit. The selection filters the authorized set and updates queries and entries in this session.",
            "d.csSelect": "Active Unit / Farm", "d.csAll": "All authorized units (scope union)", "d.cancel": "Cancel", "d.apply": "Apply context",
            "d.confirmTitle": "Confirm the operation", "d.reason": "Justification",
            "d.reasonHint": "Provide an objective reason; it is sent to the endpoint and recorded in the audit log.",
            "d.back": "Back without changes", "d.confirmBtn": "Confirm action",
            "p.loading": "Loading authorized units…", "p.active": "Active context:", "p.activeEnd": ". The filter affects queries and reports.",
            "p.none": "No individual filter: showing the authorized union of units.", "p.fail": "Could not load the list of authorized units."
        },
        "es-ES": {
            "g.start": "Inicio", "l.home": "Inicio / Panel de control", "l.tasks": "Tareas y alertas", "l.deploy": "Centro de implementación",
            "g.prod": "Producción y campo", "l.props": "Haciendas y unidades", "l.agri": "Agricultura", "l.field": "Operación de campo",
            "l.maps": "Mapa operativo", "l.harv": "Cosecha y recepción", "l.costs": "Costos de la cosecha", "l.prod2": "Producción agroindustrial",
            "g.peq": "Ganadería", "l.live": "Ganadería",
            "g.stock": "Inventarios y compras", "l.inv": "Inventario y almacén", "l.repl": "Reposición y necesidades", "l.proc": "Compras y proveedores",
            "g.com": "Comercial", "l.comm": "Ventas y comercio", "l.crm": "CRM y ciclo del cliente", "l.after": "Posventa",
            "g.log": "Logística", "l.logi": "Despacho y entrega",
            "g.fin": "Finanzas/fiscal", "l.fin2": "Finanzas y costos", "l.fisc": "Fiscal", "l.docs": "Documentos y evidencias",
            "g.qual": "Calidad y trazabilidad", "l.comp": "Calidad y cumplimiento", "l.insp": "Plantillas e inspecciones",
            "g.people": "Personas y recursos", "l.hr": "RR. HH. rural", "l.sst": "SST rural", "l.fleet": "Flota y mantenimiento",
            "g.coop": "Cooperativas", "l.coop2": "Cooperativas y marketplace",
            "g.sus": "Sostenibilidad/exportación", "l.sus2": "Sostenibilidad y ESG", "l.exp": "Exportación y trading",
            "g.rep": "Informes", "l.rep2": "Informes y analítica", "l.int": "Inteligencia Agro360", "l.exe": "Panel ejecutivo 360",
            "g.org": "Mi organización", "l.org2": "Mi organización", "l.users": "Usuarios y perfiles", "l.mods": "Mis módulos",
            "l.sett": "Configuraciones", "l.integ": "Integraciones", "l.sup": "Ayuda / Soporte",
            "g.glob": "Administración global", "l.glob2": "Administración Global MNSOFT",
            "c.ctx": "Contexto activo", "c.switch": "Cambiar contexto",
            "t.search": "Buscar animal, hacienda, producto, cosecha...", "t.palette": "¿Qué estás buscando?",
            "t.paletteHint": "Escriba al menos dos caracteres para buscar en toda la operación.", "t.perms": "Los resultados respetan sus permisos",
            "a.menu": "Abrir menú", "a.theme": "Alternar tema", "a.alerts": "Alertas de cumplimiento", "a.lang": "Idioma de la interfaz", "a.palette": "Búsqueda global",
            "b.end": "Terminar contexto",
            "d.help": "Cómo funciona esta pantalla", "d.hint": "orientación rápida",
            "lg.secure": "Acceso seguro", "lg.title": "Bienvenido a Agro360",
            "lg.sub": "Su acceso se valida directamente en la base de datos y los permisos se cargan según su perfil.",
            "lg.tenant": "Cliente / Organización", "lg.tenantHelp": "Identificador exclusivo del ambiente de su empresa.",
            "lg.email": "Correo, CPF o CNPJ", "lg.emailHelp": "Los documentos pueden informarse con o sin máscara.",
            "lg.password": "Contraseña", "lg.show": "Mostrar", "lg.hide": "Ocultar",
            "lg.first": "Primer acceso o administración global", "lg.mfa": "Código MFA", "lg.mfaHelp": "Obligatorio para la administración global.",
            "lg.newpw": "Nueva contraseña", "lg.pwHelp": "Complete cuando el acceso inicial exija cambio de contraseña.", "lg.newpw2": "Confirmar nueva contraseña",
            "lg.enter": "Entrar en Agro360", "lg.test": "Probar conexión con la API",
            "lg.footer": "Agro360 de MNSOFT · Sesión cifrada e aislada por cliente.",
            "d.csUnit": "Filtro de unidad", "d.csTitle": "Cambiar contexto operativo",
            "d.csDesc": "Seleccione la hacienda o unidad de trabajo. La selección filtra el conjunto autorizado y actualiza consultas y registros en esta sesión.",
            "d.csSelect": "Unidad / Hacienda activa", "d.csAll": "Todas las unidades autorizadas (unión del ámbito)", "d.cancel": "Cancelar", "d.apply": "Aplicar contexto",
            "d.confirmTitle": "Confirme la operación", "d.reason": "Justificación",
            "d.reasonHint": "Indique un motivo objetivo; será enviado al endpoint y registrado en la auditoría.",
            "d.back": "Volver sin cambios", "d.confirmBtn": "Confirmar acción",
            "p.loading": "Cargando unidades autorizadas…", "p.active": "Contexto activo:", "p.activeEnd": ". El filtro afecta consultas y registros.",
            "p.none": "Sin filtro individual: mostrando la unión autorizada de unidades.", "p.fail": "No se pudo cargar la lista de unidades autorizadas."
        },
        "fr-FR": {
            "g.start": "Accueil", "l.home": "Accueil / Tableau de bord", "l.tasks": "Tâches et alertes", "l.deploy": "Centre de déploiement",
            "g.prod": "Production et terrain", "l.props": "Fermes et unités", "l.agri": "Agriculture", "l.field": "Opérations de terrain",
            "l.maps": "Carte opérationnelle", "l.harv": "Récolte et réception", "l.costs": "Coûts de la récolte", "l.prod2": "Production agroindustrielle",
            "g.peq": "Élevage", "l.live": "Élevage",
            "g.stock": "Stocks et achats", "l.inv": "Inventaire et entrepôt", "l.repl": "Réapprovisionnement et besoins", "l.proc": "Achats et fournisseurs",
            "g.com": "Commercial", "l.comm": "Commercial et ventes", "l.crm": "CRM et cycle client", "l.after": "Après-vente",
            "g.log": "Logistique", "l.logi": "Expédition et livraison",
            "g.fin": "Finances/Fiscalité", "l.fin2": "Finances et coûts", "l.fisc": "Fiscalité", "l.docs": "Documents et preuves",
            "g.qual": "Qualité et traçabilité", "l.comp": "Qualité et conformité", "l.insp": "Modèles et inspections",
            "g.people": "Personnel et ressources", "l.hr": "RH rural", "l.sst": "SST rurale", "l.fleet": "Flotte et maintenance",
            "g.coop": "Coopératives", "l.coop2": "Coopératives et marketplace",
            "g.sus": "Durabilité/Exportation", "l.sus2": "Durabilité et RSE", "l.exp": "Exportation et négoce",
            "g.rep": "Rapports", "l.rep2": "Rapports et analyses", "l.int": "Intelligence Agro360", "l.exe": "Tableau exécutif 360",
            "g.org": "Mon organisation", "l.org2": "Mon organisation", "l.users": "Utilisateurs et profils", "l.mods": "Mes modules",
            "l.sett": "Paramètres", "l.integ": "Intégrations", "l.sup": "Aide / Support",
            "g.glob": "Administration globale", "l.glob2": "Administration Globale MNSOFT",
            "c.ctx": "Contexte actif", "c.switch": "Changer de contexte",
            "t.search": "Rechercher animal, ferme, produit, récolte...", "t.palette": "Que recherchez-vous ?",
            "t.paletteHint": "Saisissez au moins deux caractères pour rechercher dans toute l'opération.", "t.perms": "Les résultats respectent vos permissions",
            "a.menu": "Ouvrir le menu", "a.theme": "Changer de thème", "a.alerts": "Alertes de conformité", "a.lang": "Langue de l'interface", "a.palette": "Recherche globale",
            "b.end": "Mettre fin au contexte",
            "d.help": "Comment fonctionne cet écran", "d.hint": "aide rapide",
            "lg.secure": "Accès sécurisé", "lg.title": "Bienvenue sur Agro360",
            "lg.sub": "Votre accès est validé directement en base de données et les autorisations sont chargées selon votre profil.",
            "lg.tenant": "Client / Organisation", "lg.tenantHelp": "Identifiant exclusif de l'environnement de votre entreprise.",
            "lg.email": "E-mail, CPF ou CNPJ", "lg.emailHelp": "Les documents peuvent être saisis avec ou sans masque.",
            "lg.password": "Mot de passe", "lg.show": "Afficher", "lg.hide": "Masquer",
            "lg.first": "Premier accès ou administration globale", "lg.mfa": "Code MFA", "lg.mfaHelp": "Obligatoire pour l'administration globale.",
            "lg.newpw": "Nouveau mot de passe", "lg.pwHelp": "À remplir lorsque l'accès initial impose un changement de mot de passe.", "lg.newpw2": "Confirmer le nouveau mot de passe",
            "lg.enter": "Se connecter à Agro360", "lg.test": "Tester la connexion à l'API",
            "lg.footer": "Agro360 par MNSOFT · Session chiffrée et isolée par client.",
            "d.csUnit": "Filtre d'unité", "d.csTitle": "Changer de contexte opérationnel",
            "d.csDesc": "Sélectionnez la ferme ou l'unité de travail. La sélection filtre l'ensemble autorisé et met à jour requêtes et saisies dans cette session.",
            "d.csSelect": "Unité / Ferme active", "d.csAll": "Toutes les unités autorisées (union des périmètres)", "d.cancel": "Annuler", "d.apply": "Appliquer le contexte",
            "d.confirmTitle": "Confirmez l'opération", "d.reason": "Justification",
            "d.reasonHint": "Indiquez une raison objective ; elle est envoyée à l'endpoint et enregistrée dans l'audit.",
            "d.back": "Retour sans modification", "d.confirmBtn": "Confirmer l'action",
            "p.loading": "Chargement des unités autorisées…", "p.active": "Contexte actif :", "p.activeEnd": ". Le filtre affecte les requêtes et les saisies.",
            "p.none": "Aucun filtre individuel : affichage de l'union autorisée des unités.", "p.fail": "Impossible de charger la liste des unités autorisées."
        }
    };
    let money, number, relativeTime;
    function applyCulture(locale) {
        const culture = supportedCultures.includes(locale) ? locale : "pt-BR";
        localStorage.setItem(storageKeys.culture, culture);
        document.documentElement.lang = culture;
        money = new Intl.NumberFormat(culture, { style: "currency", currency: "BRL", maximumFractionDigits: 0 });
        number = new Intl.NumberFormat(culture, { maximumFractionDigits: 1 });
        relativeTime = new Intl.RelativeTimeFormat(culture, { numeric: "auto" });
        document.querySelectorAll("[data-culture-select]").forEach(select => { select.value = culture; });
        applyUiText(culture);
        window.dispatchEvent(new CustomEvent("agro360:culture", { detail: culture }));
        return culture;
    }
    // Aplica textos/placeholders/títulos/aria-labels dos elementos marcados com data-i18n*.
    // O fallback é o próprio HTML (pt-BR), capturado na primeira aplicação; para pt-BR nada muda.
    function applyUiText(culture) {
        const dict = uiTranslations[culture];
        document.querySelectorAll("[data-i18n]").forEach(el => {
            if (el.dataset.i18nFallback === undefined) el.dataset.i18nFallback = el.textContent;
            const value = dict?.[el.dataset.i18n] ?? el.dataset.i18nFallback;
            if (el.textContent !== value) el.textContent = value;
        });
        document.querySelectorAll("[data-i18n-placeholder]").forEach(el => {
            if (el.dataset.i18nFallbackPlaceholder === undefined) el.dataset.i18nFallbackPlaceholder = el.placeholder ?? "";
            el.placeholder = dict?.[el.dataset.i18nPlaceholder] ?? el.dataset.i18nFallbackPlaceholder;
        });
        document.querySelectorAll("[data-i18n-title]").forEach(el => {
            if (el.dataset.i18nFallbackTitle === undefined) el.dataset.i18nFallbackTitle = el.getAttribute("title") ?? "";
            el.setAttribute("title", dict?.[el.dataset.i18nTitle] ?? el.dataset.i18nFallbackTitle);
        });
        document.querySelectorAll("[data-i18n-aria]").forEach(el => {
            if (el.dataset.i18nFallbackAria === undefined) el.dataset.i18nFallbackAria = el.getAttribute("aria-label") ?? "";
            el.setAttribute("aria-label", dict?.[el.dataset.i18nAria] ?? el.dataset.i18nFallbackAria);
        });
    }
    // O idioma é aplicado imediatamente neste dispositivo; a confirmação do servidor é um passo
    // separado. Sucesso só aparece após a resposta do backend; falha exibe aviso persistente com
    // "Tentar novamente" — a preferência local nunca é perdida silenciosamente.
    function setCulture(locale) {
        const culture = applyCulture(locale);
        saveCulturePreference(culture);
    }
    async function saveCulturePreference(culture) {
        const saved = culture ?? currentCulture();
        if (!state.session?.accessToken) return;
        try {
            await api("/api/v1/auth/preferences/language", { method: "PUT", body: JSON.stringify({ language: saved }) }, false);
            dismissCultureSaveNotice();
            toastSuccess("Idioma salvo", "A preferência foi confirmada pelo servidor e será reaplicada nos próximos acessos.");
        } catch {
            showCultureSaveNotice(saved);
        }
    }
    function showCultureSaveNotice(culture) {
        if (document.querySelector("[data-culture-save-notice]")) return;
        const item = document.createElement("div");
        item.className = "toast warning";
        item.setAttribute("role", "alert");
        item.dataset.cultureSaveNotice = culture;
        item.innerHTML = `<span class="toast-icon" aria-hidden="true"></span><div><strong>Idioma ativo apenas neste dispositivo</strong><small>${escapeHtml(culture)} já está aplicado aqui, mas o salvamento da preferência no servidor falhou. Use “Tentar novamente” para persistir.</small><div class="toast-actions"><button type="button" class="toast-retry">Tentar novamente</button></div></div><button type="button" aria-label="Fechar aviso">×</button>`;
        const close = () => item.remove();
        item.querySelector(".toast-retry").addEventListener("click", () => { close(); saveCulturePreference(item.dataset.cultureSaveNotice); });
        item.querySelector('button[aria-label="Fechar aviso"]').addEventListener("click", close);
        element("toast-region").append(item);
    }
    function dismissCultureSaveNotice() {
        document.querySelector("[data-culture-save-notice]")?.remove();
    }
    applyCulture(currentCulture());

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
            // Transitional keys consumed by module-specific clients. Keep synchronized.
            localStorage.setItem("agro360.accessToken", session.accessToken);
            localStorage.setItem("agro360.access_token", session.accessToken);
            localStorage.setItem("agro360.token", session.accessToken);
            // sessionStorage consumed by logistics.js / after-sales.js
            sessionStorage.setItem("agro360.accessToken", session.accessToken);
            sessionStorage.setItem("agro360.access_token", session.accessToken);
            sessionStorage.setItem("agro360.token", session.accessToken);
            // Login/refresh devolvem o idioma resolvido pelo servidor (preferência > tenant > pt-BR):
            // é a apresentação canônica da sessão, aplicada sem novo PUT.
            if (session.language) applyCulture(session.language);
            state.refreshStopped = false;
        } else {
            localStorage.removeItem(storageKeys.session);
            localStorage.removeItem("agro360.accessToken");
            localStorage.removeItem("agro360.access_token");
            localStorage.removeItem("agro360.token");
            localStorage.removeItem("agro360.global_session");
            sessionStorage.clear();
        }
        // Expose canonical global session for inter-module use
        window.agro360Session = session ?? null;
        window.dispatchEvent(new CustomEvent("agro360:session", { detail: session ?? null }));
        syncPageToken(session);
        renderUser();
        renderNavigation();
    }

    // Sincroniza a credencial da página no host Web (cookie HttpOnly protegido).
    async function syncPageToken(session) {
        if (!session?.accessToken) {
            try {
                await fetch("/auth/page", {
                    method: "POST",
                    headers: { "Content-Type": "application/json" },
                    body: JSON.stringify({ accessToken: "" })
                }).catch(() => { });
            } catch { }
            return;
        }
        try {
            const resp = await fetch("/auth/page", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ accessToken: session.accessToken })
            });
            if (resp.status === 401 || resp.status === 403) {
                const problem = await resp.json().catch(() => ({}));
                persistSession(null);
                if (typeof showToast === "function") {
                    showToast("danger", "Acesso revogado", problem.detail || "Sua credencial não é mais válida ou o acesso foi bloqueado.", "session-rejected");
                }
                if (typeof showLogin === "function") {
                    showLogin();
                }
            } else if (!resp.ok && typeof showToast === "function") {
                const problem = await resp.json().catch(() => ({}));
                showToast("danger", "Validação indisponível", problem.detail || "Não foi possível confirmar sua sessão. Tente novamente.", "session-validation-unavailable");
            }
        } catch {
            if (typeof showToast === "function") {
                showToast("danger", "Validação indisponível", "Não foi possível confirmar sua sessão. Tente novamente.", "session-validation-unavailable");
            }
        }
    }

    function normalizePermission(value) {
        return String(value ?? "")
            .toLowerCase()
            .replace(/^agro360\./, "")
            .replaceAll("_", ".");
    }

    function renderNavigation() {
        const permissions = new Set((state.session?.permissions ?? []).map(normalizePermission));
        const roles = state.session?.roles ?? [];
        const isSuperAdministrator = roles.some(r => r === "SUPER_ADMIN" || r === "PLATFORM_SUPER_ADMIN");
        const isSupportSession = Boolean(state.session?.supportSession);

        document.querySelectorAll(".main-nav a").forEach(link => {
            const required = (link.dataset.permissions ?? "").split(",").filter(Boolean).map(normalizePermission);
            const isGlobalAdminLink = link.dataset.superAdmin === "true";
            let allowed = false;

            if (isGlobalAdminLink) {
                allowed = isSuperAdministrator;
            } else if (link.dataset.publicMenu === "true") {
                allowed = true;
            } else if (isSupportSession) {
                // In support session, SuperAdmin has tenant-contracted permissions
                allowed = required.length === 0 || required.some(p => permissions.has(p));
            } else if (isSuperAdministrator) {
                // In global platform context, SuperAdmin can view all valid operational features
                allowed = true;
            } else if (state.session) {
                // Tenant user (Admin, Operator, etc.)
                allowed = required.length === 0 || required.some(p => permissions.has(p));
            }

            link.hidden = !allowed;
            link.setAttribute("aria-hidden", String(!allowed));
        });

        document.querySelectorAll(".main-nav .nav-label").forEach(label => {
            let sibling = label.nextElementSibling;
            let hasVisibleLink = false;
            while (sibling && !sibling.classList.contains("nav-label")) {
                if (sibling.matches("a:not([hidden])")) hasVisibleLink = true;
                sibling = sibling.nextElementSibling;
            }
            label.hidden = !hasVisibleLink;
        });

        const currentPath = window.location.pathname.toLowerCase().replace(/\/$/, "") || "/";
        let activePathAssigned = false;
        document.querySelectorAll(".main-nav a").forEach(link => {
            const linkPath = new URL(link.href, window.location.origin).pathname.toLowerCase().replace(/\/$/, "") || "/";
            const isCurrent = !activePathAssigned && linkPath === currentPath;
            link.classList.toggle("active", isCurrent);
            activePathAssigned ||= isCurrent;

            link.onclick = (e) => {
                const inSupport = Boolean(state.session?.supportSession);
                const isGlobal = link.dataset.superAdmin === "true" || link.dataset.publicMenu === "true" || link.getAttribute("href") === "/";
                if (isSuperAdministrator && !inSupport && !isGlobal) {
                    e.preventDefault();
                    agro360Feedback.toast(
                        "warning",
                        "Contexto de cliente necessário",
                        "Para operar recursos deste módulo, selecione uma organização e inicie a sessão de suporte assistido em Administração SaaS."
                    );
                    setTimeout(() => {
                        window.location.assign("/Saas?view=tenants");
                    }, 1200);
                }
            };
        });
    }

    function renderUser() {
        const user = state.session;
        element("user-name").textContent = user?.name ?? "Agro 360";
        element("user-email").textContent = user?.email ?? "Entrar na conta";
        element("user-initials").textContent = user?.name
            ? user.name.split(/\s+/).slice(0, 2).map(part => part[0]).join("").toUpperCase()
            : "A3";
        let activeFarm = null;
        try { activeFarm = JSON.parse(localStorage.getItem("agro360.active_farm") || "null"); } catch { }
        if (user?.supportSession) {
            element("active-farm").textContent = `${user.supportSession.tenantName || user.activeOrganization} (Suporte)`;
        } else if (activeFarm?.name) {
            element("active-farm").textContent = `${activeFarm.name}`;
        } else {
            element("active-farm").textContent = user?.activeOrganization ? `${user.activeOrganization} · Todas as unidades` : "Organização não selecionada";
        }
    }

    async function api(path, options = {}, retry = true) {
        const method = String(options.method ?? "GET").toUpperCase();
        const canReplay = method === "GET" || method === "HEAD" || method === "OPTIONS";
        const headers = new Headers(options.headers ?? {});
        if (options.body && !headers.has("Content-Type")) headers.set("Content-Type", "application/json");
        if (state.session?.accessToken) headers.set("Authorization", `Bearer ${state.session.accessToken}`);
        headers.set("X-Timezone", Intl.DateTimeFormat().resolvedOptions().timeZone || "America/Belem");
        headers.set("X-Culture", currentCulture());
        try {
            const activeFarm = JSON.parse(localStorage.getItem("agro360.active_farm") || "null");
            if (activeFarm?.id) headers.set("X-Farm-Id", activeFarm.id);
        } catch { }
        const response = await fetch(`${apiBase}${path}`, { ...options, headers });

        if (response.status === 401 && canReplay && retry && !state.refreshStopped) {
            if (state.session?.supportSession) {
                state.refreshStopped = true;
                const globalSessionStr = localStorage.getItem("agro360.global_session");
                if (globalSessionStr) {
                    try {
                        const globalSession = JSON.parse(globalSessionStr);
                        localStorage.removeItem("agro360.global_session");
                        persistSession(globalSession);
                        showToast("warning", "Contexto assistido encerrado", "A sessão assistida expirou ou foi revogada. O contexto global foi restaurado.", "support-session-expired");
                        return api(path, options, false);
                    } catch { }
                }
                persistSession(null);
                showLogin();
                showToast("warning", "Sessão expirada", "A sessão de suporte expirou. Faça login novamente.", "support-expired-login");
                return null;
            } else if (state.session?.refreshToken) {
                const refreshed = await refreshSession();
                if (refreshed) return api(path, options, false);
            }
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
            if (response.status === 401 || response.status === 403) {
                state.refreshStopped = true;
                persistSession(null);
                showLogin();
                showToast("warning", "Sessão expirada", "Sua sessão expirou. Faça login novamente.", "refresh-rejected");
                return false;
            }
            if (!response.ok) {
                const problem = await response.json().catch(() => ({}));
                const supportCode = problem.traceId ? ` Código de suporte: ${problem.traceId}.` : "";
                const error = new Error(`${problem.detail || "A renovação está temporariamente indisponível."}${supportCode}`);
                error.status = response.status;
                throw error;
            }
            const refreshed = await response.json();
            if (!state.session?.supportSession) {
                refreshed.activeOrganization = state.session?.activeOrganization;
            }
            persistSession(refreshed);
            return true;
        } catch (error) {
            state.refreshStopped = true;
            const detail = error instanceof TypeError
                ? "Não foi possível renovar a sessão por falha de rede. Seus dados locais foram preservados; tente novamente quando a API voltar."
                : error.message;
            showToast("warning", "Sessão não renovada", detail, "refresh-unavailable");
            error.handled = true;
            throw error;
        }
    }

    async function retrySessionRefresh() {
        state.refreshStopped = false;
        state.activeIncidents.delete("refresh-unavailable");
        return refreshSession();
    }

    function showLogin() {
        loginModal.hidden = false;
        setTimeout(() => loginModal.querySelector("input")?.focus(), 50);
    }

    function hideLogin() { loginModal.hidden = true; }

    function validateLoginForm(form) {
        const messages = {
            tenantSlug: "Informe o identificador da sua organização.",
            email: "Informe um e-mail, CPF ou CNPJ válido.",
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
                throw new Error("Preencha Cliente/Organização, e-mail, CPF ou CNPJ e senha para continuar.");
            }
            const data = Object.fromEntries(new FormData(form));
            if (data.newPassword && data.newPassword !== data.newPasswordConfirmation) {
                throw new Error("A confirmação da nova senha não corresponde.");
            }
            delete data.newPasswordConfirmation;
            const response = await fetch(`${apiBase}/api/v1/auth/login`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(data)
            });
            const result = await response.json().catch(() => ({}));
            if (response.status === 401) {
                const code = result.code || "";
                if (code === "mfa_required") {
                    const additionalStep = element("login-additional-step");
                    additionalStep.open = true;
                    const mfaInput = form.elements.mfaCode;
                    mfaInput.required = true;
                    mfaInput.focus();
                    throw new Error("Informe o código do aplicativo autenticador para concluir o acesso.");
                }
                const messages = { tenant_blocked: "Tenant bloqueado. Procure o suporte.", user_blocked: "Usuário bloqueado. Procure o administrador." };
                throw new Error(code === "mfa_invalid" ? "Código MFA inválido ou expirado." : (messages[code] || "Usuário ou senha inválidos."));
            }
            if (response.status === 403) {
                throw new Error(result.detail || "A conta ou organização não está autorizada para este acesso.");
            }
            if (response.status === 400) {
                const validationMessage = result.detail
                    || Object.values(result.errors || {}).flat().join(" ");
                throw new Error(validationMessage || "Confira os dados informados.");
            }
            if (!response.ok) {
                const supportCode = result.traceId ? ` Código de suporte: ${result.traceId}.` : "";
                const message = response.status === 503
                    ? "A API está temporariamente indisponível. Tente novamente em instantes."
                    : "Ocorreu uma falha interna. Tente novamente ou informe o código de atendimento ao suporte.";
                throw new Error(message + supportCode);
            }
            result.activeOrganization = data.tenantSlug;
            persistSession(result);
            hideLogin();
            toastSuccess("Acesso confirmado", "Bem-vindo. Os dados respeitam sua organização e suas permissões.");
            // Route based on role — avoid calling tenant-scoped dashboard for SuperAdmin
            const isSuperAdmin = (result.roles ?? []).some(r => r === "SUPER_ADMIN" || r === "PLATFORM_SUPER_ADMIN");
            if (isSuperAdmin) {
                window.location.href = "/Saas";
                return;
            }
            if (element("dashboard-subtitle")) {
                try {
                    await loadDashboard();
                } catch (e) {
                    // 403 means tenant has no operational permissions yet; page already handled
                    if (e.status !== 403) throw e;
                }
            } else {
                window.location.reload();
            }
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
            button.querySelector("span").textContent = (uiTranslations[currentCulture()] ?? {})["lg.enter"] ?? "Entrar no Agro360";
        }
    }

    document.querySelector(".password-toggle")?.addEventListener("click", event => {
        const button = event.currentTarget;
        const input = element(button.getAttribute("aria-controls"));
        const show = input.type === "password";
        input.type = show ? "text" : "password";
        button.setAttribute("aria-pressed", String(show));
        const ui = uiTranslations[currentCulture()] ?? {};
        button.textContent = show ? (ui["lg.hide"] ?? "Ocultar") : (ui["lg.show"] ?? "Mostrar");
        input.focus();
    });

    async function testApiConnection() {
        const button = element("test-api-connection");
        button.disabled = true;
        button.classList.add("checking");
        try {
            const response = await fetch(`${apiBase}/health`, { cache: "no-store", signal: AbortSignal.timeout(15000) });
            if (!response.ok) throw new Error(`A API respondeu com status ${response.status}. Verifique a conexão com o banco.`);
            if ((await response.text()).trim() !== "Healthy") throw new Error("Resposta de saúde inválida. Verifique a URL configurada para a API.");
            const swagger = await fetch(`${apiBase}/swagger/v1/swagger.json`, { cache: "no-store", signal: AbortSignal.timeout(15000), headers: { Accept: "application/json" } });
            if (!swagger.ok) {
                toastWarning("Swagger inacessível", `A API está online, mas o Swagger respondeu com status ${swagger.status}.`);
                return;
            }
            const specification = await swagger.json();
            if (typeof specification.openapi !== "string" || !specification.paths) throw new Error("A URL do Swagger não retornou um documento OpenAPI válido.");
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

    async function logout() {
        if (!state.session) { showLogin(); return; }
        const refreshToken = state.session.refreshToken;
        try {
            const response = await fetch(`${apiBase}/api/v1/auth/logout`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ refreshToken })
            });
            if (!response.ok) throw new Error(`Falha HTTP ${response.status}`);
        } catch {
            toastWarning("Logout parcial", "A sessão local foi encerrada, mas a API não confirmou a revogação. Revogue as sessões na área de segurança.");
        } finally {
            persistSession(null);
            resetDashboard();
            showLogin();
            toast("Sessão encerrada", "O refresh token foi revogado e os dados locais de autenticação foram removidos.");
        }
    }

    async function loadDashboard() {
        if (!state.session) { showLogin(); return; }
        const subtitle = element("dashboard-subtitle");
        if (!subtitle) return;
        subtitle.textContent = "Atualizando a malha de dados do tenant...";
        try {
            const result = await api("/api/v1/dashboard/command-center");
            renderDashboard(result);
            await loadLivestockDashboard();
            await loadStorageDashboard();
            element("sync-time").textContent = "sincronizado agora";
        } catch (error) {
            if (error.status !== 401 && !error.handled) toast("Não foi possível atualizar", error.message, true);
            subtitle.textContent = "Não foi possível consolidar os indicadores agora.";
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
        if (!body) return;
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
        if (feature === "context") {
            openContextSwitcherDialog();
            return;
        }
        else if (finance.has(feature)) route = "/Intelligence";
        else if (storage.has(feature)) route = "/#storage";
        else if (livestock.has(feature)) route = feature === "weighing" ? "/livestock?tab=weighings" : "/livestock";
        else if (traceability.has(feature)) route = feature === "processing-compliance" ? "/Compliance" : "/Maps";
        window.location.assign(route);
    }

    async function openContextSwitcherDialog() {
        if (!state.session) { showLogin(); return; }
        const dialog = element("context-switcher-dialog");
        if (!dialog) {
            window.location.assign("/Saas?view=users");
            return;
        }
        const select = element("context-farm-select");
        const preview = element("context-switcher-preview");
        let activeFarm = null;
        try { activeFarm = JSON.parse(localStorage.getItem("agro360.active_farm") || "null"); } catch { }

        const ui = uiTranslations[currentCulture()] ?? {};
        preview.textContent = ui["p.loading"] ?? "Carregando unidades autorizadas…";
        select.innerHTML = `<option value="">${escapeHtml(ui["d.csAll"] ?? "Todas as unidades autorizadas (união do escopo)")}</option>`;

        try {
            const properties = await api("/api/v1/properties").catch(() => ({ items: [] }));
            const farms = properties?.items ?? (Array.isArray(properties) ? properties : []);
            farms.forEach(f => {
                const opt = document.createElement("option");
                opt.value = f.id;
                opt.textContent = `${f.name} (${f.state || ""})`;
                if (activeFarm && activeFarm.id === f.id) opt.selected = true;
                select.appendChild(opt);
            });
            preview.textContent = activeFarm
                ? `${ui["p.active"] ?? "Contexto ativo:"} ${activeFarm.name}${ui["p.activeEnd"] ?? ". O filtro afeta consultas e relatórios."}`
                : ui["p.none"] ?? "Sem filtro individual: exibindo a união autorizada de unidades.";
        } catch {
            preview.textContent = ui["p.fail"] ?? "Não foi possível carregar a lista de unidades autorizadas.";
        }

        const form = element("context-switcher-form");
        const onDialogSubmit = (e) => {
            e.preventDefault();
            form.removeEventListener("submit", onDialogSubmit);
            if (e.submitter?.value === "apply") {
                const selectedId = select.value;
                if (!selectedId) {
                    localStorage.removeItem("agro360.active_farm");
                    toastSuccess("Contexto atualizado", "Exibindo união de todas as unidades autorizadas.");
                } else {
                    const selectedName = select.options[select.selectedIndex]?.text || "Fazenda";
                    localStorage.setItem("agro360.active_farm", JSON.stringify({ id: selectedId, name: selectedName }));
                    toastSuccess("Contexto atualizado", `Unidade ativa definida como: ${selectedName}`);
                }
                renderUser();
                window.dispatchEvent(new CustomEvent("agro360:unit-changed", { detail: select.value }));
                if (typeof load === "function") { load(); }
            }
            dialog.close();
        };

        form.addEventListener("submit", onDialogSubmit);
        dialog.showModal();
    }

    function toggleTheme() {
        const next = document.documentElement.dataset.theme === "dark" ? "light" : "dark";
        document.documentElement.dataset.theme = next;
        localStorage.setItem(storageKeys.theme, next);
    }

    function showToast(severity, title, detail, incidentKey = "") {
        if (incidentKey && state.activeIncidents.has(incidentKey)) return;
        if (incidentKey) state.activeIncidents.add(incidentKey);
        const item = document.createElement("div");
        item.className = `toast ${severity}`;
        item.setAttribute("role", severity === "error" ? "alert" : "status");
        item.innerHTML = `<span class="toast-icon" aria-hidden="true"></span><div><strong>${escapeHtml(title)}</strong><small>${escapeHtml(detail)}</small></div><button type="button" aria-label="Fechar mensagem">×</button>`;
        const dismiss = () => { item.remove(); if (incidentKey) state.activeIncidents.delete(incidentKey); };
        item.querySelector("button").addEventListener("click", dismiss);
        element("toast-region").append(item);
        window.setTimeout(dismiss, 5200);
    }

    const toastSuccess = (title, detail) => showToast("success", title, detail);
    const toastWarning = (title, detail) => showToast("warning", title, detail);
    const toastError = (title, detail) => showToast("error", title, detail);
    const toastInfo = (title, detail) => showToast("info", title, detail);
    const toast = (title, detail, error = false) => error ? toastError(title, detail) : toastSuccess(title, detail);

    function confirmDialog(title, message, confirmText = "Confirmar", cancelText = "Cancelar") {
        return agro360Feedback.confirm({ title, message, confirmText, cancelText }).then(res => Boolean(res?.confirmed ?? res));
    }

    const agro360Feedback = {
        toast: (severity, title, detail, incidentKey = "") => {
            const validSeverities = ["success", "warning", "error", "info"];
            let s = severity;
            let t = title;
            let d = detail;
            if (!validSeverities.includes(s)) {
                d = t;
                t = s;
                s = "info";
            }
            showToast(s, t, d || "", incidentKey);
        },
        success: (title, detail) => showToast("success", title, detail || ""),
        warning: (title, detail) => showToast("warning", title, detail || ""),
        error: (title, detail) => showToast("error", title, detail || ""),
        info: (title, detail) => showToast("info", title, detail || ""),
        confirm: (options, ...args) => {
            const ui = uiTranslations[currentCulture()] ?? {};
            let title = ui["d.confirmTitle"] ?? "Confirme a operação";
            let message = "";
            let confirmText = ui["d.confirmBtn"] ?? "Confirmar ação";
            let cancelText = ui["d.back"] ?? "Voltar sem alterar";
            let requireReason = false;
            let reasonPlaceholder = "";
            let minReasonLength = 3;

            if (typeof options === "string") {
                title = options;
                message = args[0] || "";
                confirmText = args[1] || confirmText;
                cancelText = args[2] || cancelText;
            } else if (options && typeof options === "object") {
                title = options.title || title;
                message = options.message || options.consequence || "";
                confirmText = options.confirmText || confirmText;
                cancelText = options.cancelText || cancelText;
                requireReason = Boolean(options.requireReason);
                reasonPlaceholder = options.reasonPlaceholder || "";
                minReasonLength = options.minReasonLength || 3;
            }

            const dialog = element("action-confirmation");
            if (!dialog) return Promise.resolve(typeof options === "object" ? { confirmed: false } : false);

            const form = element("confirmation-form");
            const titleEl = element("confirmation-title");
            const msgEl = element("confirmation-consequence");
            const reasonField = element("confirmation-reason-field");
            const reasonInput = element("confirmation-reason");
            const reasonError = element("confirmation-reason-error");
            const submitBtn = element("confirmation-submit");
            const cancelBtn = dialog.querySelector(".secondary-button");

            if (titleEl) titleEl.textContent = title;
            if (msgEl) msgEl.textContent = message;
            if (submitBtn) submitBtn.textContent = confirmText;
            if (cancelBtn) cancelBtn.textContent = cancelText;

            if (reasonField) {
                reasonField.hidden = !requireReason;
            }
            if (reasonInput) {
                reasonInput.value = "";
                if (reasonPlaceholder) reasonInput.placeholder = reasonPlaceholder;
            }
            if (reasonError) reasonError.textContent = "";

            return new Promise(resolve => {
                const cleanup = () => {
                    form?.removeEventListener("submit", onSubmit);
                    dialog?.removeEventListener("close", onClose);
                };

                const onSubmit = e => {
                    if (e.submitter && e.submitter.value === "cancel") {
                        cleanup();
                        dialog.close();
                        resolve(typeof options === "object" ? { confirmed: false } : false);
                        return;
                    }
                    if (requireReason) {
                        const val = reasonInput ? reasonInput.value.trim() : "";
                        if (val.length < minReasonLength) {
                            e.preventDefault();
                            if (reasonError) reasonError.textContent = `Informe uma justificativa com pelo menos ${minReasonLength} caracteres.`;
                            reasonInput?.focus();
                            return;
                        }
                        cleanup();
                        dialog.close();
                        resolve(typeof options === "object" ? { confirmed: true, reason: val } : true);
                        return;
                    }
                    cleanup();
                    dialog.close();
                    resolve(typeof options === "object" ? { confirmed: true } : true);
                };

                const onClose = () => {
                    cleanup();
                    resolve(typeof options === "object" ? { confirmed: false } : false);
                };

                form?.addEventListener("submit", onSubmit);
                dialog?.addEventListener("close", onClose, { once: true });
                dialog.showModal();
            });
        },
        handleError: (err, fallbackTitle = "Operação não concluída") => {
            let title = fallbackTitle;
            let detail = err?.message || "Ocorreu um erro inesperado.";
            const status = err?.status;

            if (status === 401 || err?.code === "authentication_required" || detail.includes("401") || detail.toLowerCase().includes("não autenticado") || detail.toLowerCase().includes("unauthorized")) {
                title = "Sessão expirada";
                detail = "Sua sessão expirou ou não está autenticada. Faça login novamente para prosseguir.";
                showToast("warning", title, detail, "auth-401");
                return;
            }
            if (status === 403 || err?.code === "forbidden" || detail.includes("403") || detail.toLowerCase().includes("não autorizado") || detail.toLowerCase().includes("permissão") || detail.toLowerCase().includes("forbidden")) {
                title = "Acesso não autorizado";
                detail = "Seu perfil não possui permissão para executar esta operação.";
                showToast("error", title, detail, "auth-403");
                return;
            }
            if (err instanceof TypeError || detail.includes("Failed to fetch") || detail.toLowerCase().includes("network") || detail.toLowerCase().includes("rede") || detail.toLowerCase().includes("conexão")) {
                title = "Falha de conexão";
                detail = "Não foi possível comunicar com o servidor. Verifique sua conexão de rede.";
                showToast("error", title, detail, "network-error");
                return;
            }

            const cleanDetail = detail.replace(/[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}/g, "").trim();
            showToast("error", title, cleanDetail || detail);
        }
    };

    Object.assign(window, {
        toastSuccess, toastWarning, toastError, toastInfo, confirmDialog, agro360Feedback,
        agro360Api: api, retrySessionRefresh, agro360SetSession: persistSession, agro360PersistSession: persistSession,
        agro360Culture: currentCulture, agro360SetCulture: setCulture, agro360SupportedCultures: supportedCultures
    });

    function setText(id, value) {
        const target = element(id);
        if (target) target.textContent = value;
    }
    function escapeHtml(value) { const span = document.createElement("span"); span.textContent = String(value ?? ""); return span.innerHTML; }
    function formatRelative(value) {
        const seconds = Math.round((new Date(value).getTime() - Date.now()) / 1000);
        if (Math.abs(seconds) < 60) return relativeTime.format(seconds, "second");
        const minutes = Math.round(seconds / 60);
        if (Math.abs(minutes) < 60) return relativeTime.format(minutes, "minute");
        const hours = Math.round(minutes / 60);
        if (Math.abs(hours) < 24) return relativeTime.format(hours, "hour");
        return new Date(value).toLocaleDateString(document.documentElement.lang || "pt-BR");
    }

    function init() {
        const savedTheme = localStorage.getItem(storageKeys.theme);
        if (savedTheme) document.documentElement.dataset.theme = savedTheme;
        renderUser();
        renderNavigation();
        element("login-form").addEventListener("submit", login);
        element("login-form").addEventListener("input", event => {
            if (event.target.matches("input[required]")) validateLoginForm(event.currentTarget);
        });
        element("test-api-connection").addEventListener("click", testApiConnection);
        element("logout-button").addEventListener("click", logout);
        element("search-trigger").addEventListener("click", openPalette);
        element("global-search").addEventListener("input", scheduleSearch);
        element("theme-button").addEventListener("click", toggleTheme);
        document.querySelectorAll("[data-culture-select]").forEach(select => {
            select.value = currentCulture();
            select.addEventListener("change", () => setCulture(select.value));
        });
        element("menu-button").addEventListener("click", () => document.body.classList.toggle("menu-open"));
        element("refresh-dashboard")?.addEventListener("click", loadDashboard);
        document.querySelectorAll("[data-feature]").forEach(button => button.addEventListener("click", featureMessage));
        document.addEventListener("keydown", keydown);
        palette.addEventListener("click", event => { if (event.target === palette) closePalette(); });
        window.addEventListener("storage", event => {
            if (event.key === storageKeys.session) {
                state.session = readJson(storageKeys.session);
                window.agro360Session = state.session ?? null;
                window.dispatchEvent(new CustomEvent("agro360:session", { detail: state.session ?? null }));
                renderUser();
                renderNavigation();
                if (state.session && element("dashboard-subtitle")) loadDashboard();
            }
        });
        if ("serviceWorker" in navigator) navigator.serviceWorker.register("/service-worker.js").catch(() => {});
        if (state.session && element("dashboard-subtitle")) loadDashboard(); else if (!state.session) showLogin();
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
    if (state.session && element("ops-open-purchases")) loadOperationalDashboard();

})();
