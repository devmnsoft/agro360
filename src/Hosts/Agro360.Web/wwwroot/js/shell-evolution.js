(() => {
    const banner = document.getElementById("assisted-context-banner");
    if (!banner) return;

    const reveal = () => {
        const session = window.agro360Session || {};
        const assisted = Boolean(session.superAdmin || session.assistedAccess || session.isPlatformAdmin);
        banner.dataset.visible = assisted ? "true" : "false";
        banner.hidden = !assisted;
    };

    document.addEventListener("agro360:session", reveal);
    document.addEventListener("DOMContentLoaded", reveal);
    reveal();
})();
