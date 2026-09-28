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
                const scopeText = ss.scope === "SUPPORT_OPERATIONAL" ? "leitura e escrita autorizada" : "somente leitura operacional";
                subEl.textContent = `Você está operando no tenant autorizado em modo ${scopeText} (expira às ${expires}). Toda ação é auditada no ator real.`;
            }
        }
    };

    if (endBtn) {
        endBtn.addEventListener("click", async () => {
            endBtn.disabled = true;
            endBtn.textContent = "Encerrando…";
            let serverRevoked = false;
            let networkFailed = false;

            const session = window.agro360Session || {};
            const ss = session.supportSession;
            try {
                if (window.agro360Api) {
                    await window.agro360Api(`/api/platform/support-session/end`, {
                        method: "POST",
                        body: JSON.stringify({
                            sessionId: ss?.sessionId,
                            tenantId: ss?.tenantId || session.tenantId
                        })
                    });
                    serverRevoked = true;
                }
            } catch (err) {
                console.error("Falha ao registrar encerramento no backend", err);
                networkFailed = true;
            }

            const globalSessionStr = localStorage.getItem("agro360.global_session");
            if (globalSessionStr) {
                try {
                    const globalSession = JSON.parse(globalSessionStr);
                    localStorage.removeItem("agro360.global_session");
                    if (window.agro360SetSession) {
                        window.agro360SetSession(globalSession);
                    } else {
                        localStorage.setItem("agro360.session", JSON.stringify(globalSession));
                        window.dispatchEvent(new CustomEvent("agro360:session", { detail: globalSession }));
                        document.dispatchEvent(new CustomEvent("agro360:session", { detail: globalSession }));
                    }
                } catch (e) {
                    console.error("Falha ao restaurar sessão global", e);
                }
            } else {
                if (window.agro360SetSession) {
                    window.agro360SetSession(null);
                }
            }

            banner.dataset.visible = "false";
            banner.hidden = true;

            if (serverRevoked) {
                if (window.agro360Feedback) {
                    window.agro360Feedback.toast("success", "Sessão assistida encerrada", "Sessão revogada no servidor e contexto global restabelecido com sucesso.");
                }
            } else if (networkFailed) {
                if (window.agro360Feedback) {
                    window.agro360Feedback.toast("warning", "Contexto local restaurado", "A revogação no servidor não pôde ser confirmada por falha de conexão. O acesso local foi encerrado e a sessão expirará no servidor.");
                }
            }

            setTimeout(() => {
                window.location.assign("/Saas?view=tenants");
            }, 800);
        });
    }

    window.addEventListener("agro360:session", reveal);
    document.addEventListener("agro360:session", reveal);
    document.addEventListener("DOMContentLoaded", reveal);
    reveal();
})();
