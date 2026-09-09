(() => {
    "use strict";
    document.querySelector("#login-modal")?.setAttribute("hidden", "");
    const form = document.querySelector("#invitation-accept-form");
    const message = form.querySelector(".form-message");
    const api = document.querySelector('meta[name="api-base"]').content.replace(/\/$/, "");
    form.elements.token.value = new URLSearchParams(location.search).get("token") ?? "";
    form.addEventListener("submit", async event => {
        event.preventDefault();
        if (!form.reportValidity()) return;
        if (form.elements.password.value !== form.elements.confirmation.value) {
            message.textContent = "A confirmação não corresponde à nova senha.";
            form.elements.confirmation.focus();
            return;
        }
        const submit = form.querySelector('button[type="submit"]');
        submit.disabled = true;
        message.textContent = "Ativando acesso…";
        try {
            const response = await fetch(`${api}/api/invitations/accept`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ token: form.elements.token.value, name: form.elements.name.value, password: form.elements.password.value })
            });
            const result = await response.json().catch(() => null);
            if (!response.ok) throw new Error(result?.detail ?? "Não foi possível ativar o convite.");
            form.reset();
            message.textContent = `Acesso ativado para ${result.email}. Entre na organização ${result.tenantSlug}.`;
            submit.textContent = "Acesso ativado";
        } catch (error) {
            message.textContent = error.message;
            submit.disabled = false;
        }
    });
})();
