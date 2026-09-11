const SHELL_CACHE = "agro360-shell-v45";
const SHELL = ["/", "/field", "/css/agro360.css", "/css/field.css", "/js/agro360.js", "/js/field.js", "/icons/agro360.svg", "/manifest.webmanifest"];

self.addEventListener("install", event => {
    event.waitUntil(caches.open(SHELL_CACHE).then(cache => cache.addAll(SHELL)));
    self.skipWaiting();
});

self.addEventListener("activate", event => {
    // Remove the former unscoped lookup cache: it was shared between tenants.
    // Leave caches belonging to other applications alone.
    event.waitUntil(caches.keys().then(keys => Promise.all(keys
        .filter(key => key !== SHELL_CACHE && (key.startsWith("agro360-shell-") || key.startsWith("agro360-lookups-")))
        .map(key => caches.delete(key))))
        .then(() => self.clients.claim()));
});

self.addEventListener("fetch", event => {
    const url = new URL(event.request.url);
    // Never synthesize a successful response for health, OpenAPI, authenticated
    // data or another origin. API failure must reach the caller as a failure.
    if (event.request.method !== "GET"
        || url.origin !== self.location.origin
        || event.request.headers.has("Authorization")
        || !SHELL.includes(url.pathname)) return;

    event.respondWith(fetch(event.request).then(async response => {
        if (response.ok && response.type === "basic") {
            const cache = await caches.open(SHELL_CACHE);
            await cache.put(event.request, response.clone());
        }
        return response;
    }).catch(async () => {
        const cache = await caches.open(SHELL_CACHE);
        const cached = await cache.match(event.request) ?? await cache.match(url.pathname);
        return cached ?? Response.error();
    }));
});

self.addEventListener("sync", event => {
    if (event.tag === "agro360-sync") {
        event.waitUntil(self.clients.matchAll({ includeUncontrolled: true })
            .then(clients => clients.forEach(client => client.postMessage({ type: "SYNC_REQUESTED" }))));
    }
});
