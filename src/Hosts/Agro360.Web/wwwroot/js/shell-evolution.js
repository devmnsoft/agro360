(() => {
    "use strict";
    const banner = document.getElementById("assisted-context-banner");
    if (!banner) return;

    const titleEl = document.getElementById("assisted-context-title");
    const subEl = document.getElementById("assisted-context-subtitle");
    const endBtn = document.getElementById("end-assisted-session-btn");

    const reveal = () => {
        const session = window.agro360Session || {};
        const isSupportActive = Boolean(session.supportSession);
        banner.dataset.visible = isSupportActive ? "true" : "false";
        banner.hidden = !isSupportActive;

        if (isSupportActive) {
            const ss = session.supportSession;
            if (titleEl) titleEl.textContent = `Contexto de suporte assistido: ${ss.tenantName || session.activeOrganization || "Organização"}`;
            if (subEl) {
                const expires = ss.expiresAt ? new Date(ss.expiresAt).toLocaleTimeString("pt-BR") : "";
                subEl.textContent = `Você está operando no tenant autorizado (expira às ${expires}). Toda ação é auditada no ator real.`;
            }
        }
    };

    if (endBtn) {
        endBtn.addEventListener("click", async () => {
            endBtn.disabled = true;
            endBtn.textContent = "Encerrando…";
            try {
                const session = window.agro360Session || {};
                const tenantId = session.supportSession?.tenantId || session.tenantId;
                if (tenantId && window.agro360Api) {
                    await window.agro360Api(`/api/platform/tenants/${encodeURIComponent(tenantId)}/support-session/end`, { method: "POST" });
                }
            } catch (err) {
                console.error("Falha ao registrar encerramento no backend", err);
            } finally {
                const globalSessionStr = localStorage.getItem("agro360.global_session");
                if (globalSessionStr) {
                    try {
                        const globalSession = JSON.parse(globalSessionStr);
                        localStorage.removeItem("agro360.global_session");
                        if (window.agro360SetSession) {
                            window.agro360SetSession(globalSession);
                        } else {
                            localStorage.setItem("agro360.session", JSON.stringify(globalSession));
                        }
                    } catch (e) {
                        console.error("Falha ao restaurar sessão global", e);
                    }
                }
                banner.dataset.visible = "false";
                banner.hidden = true;
                window.location.href = "/Saas";
            }
        });
    }

    document.addEventListener("agro360:session", reveal);
    document.addEventListener("DOMContentLoaded", reveal);
    reveal();
})();
