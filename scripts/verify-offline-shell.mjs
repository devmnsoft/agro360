import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import vm from "node:vm";

// Executes the real worker without a browser or any network/dependencies.
// Browser registration/update and visual behavior remain separate QA gates.
const handlers = new Map();
const deleted = [];
const cachedPaths = [];
const cache = {
    async match(key) {
        cachedPaths.push(typeof key === "string" ? key : key.url);
        return typeof key === "string" && key === "/field" ? new Response("offline field") : undefined;
    }
};
const context = vm.createContext({
    URL, Response,
    fetch: async () => { throw new TypeError("Network unavailable"); },
    caches: {
        async open() { return cache; },
        async keys() { return ["agro360-shell-v43", "agro360-shell-v44", "agro360-lookups-v43", "another-app-cache"]; },
        async delete(key) { deleted.push(key); return true; }
    },
    self: {
        location: { origin: "https://web.example" },
        clients: { async claim() {} },
        addEventListener(name, handler) { handlers.set(name, handler); }
    }
});
vm.runInContext(await readFile(new URL("../src/Hosts/Agro360.Web/wwwroot/service-worker.js", import.meta.url), "utf8"), context);

for (const path of ["/health", "/health/live", "/swagger/v1/swagger.json", "/openapi/v1.json", "/api/mobile/bootstrap", "/api/v1/auth/refresh", "/unknown.js"]) {
    let intercepted = false;
    handlers.get("fetch")({ request: new Request(`https://web.example${path}`), respondWith() { intercepted = true; } });
    assert.equal(intercepted, false, `Must not mask/cache ${path}`);
}
for (const request of [
    new Request("https://api.example/"),
    new Request("https://web.example/field", { headers: { Authorization: "Bearer fixture-not-a-token" } }),
    new Request("https://web.example/field", { method: "POST" })
]) {
    let intercepted = false;
    handlers.get("fetch")({ request, respondWith() { intercepted = true; } });
    assert.equal(intercepted, false, "Only unauthenticated local shell GETs may be intercepted");
}

let response;
handlers.get("fetch")({ request: new Request("https://web.example/field"), respondWith(promise) { response = promise; } });
assert.equal(await (await response).text(), "offline field");
cachedPaths.length = 0;
handlers.get("fetch")({ request: new Request("https://web.example/css/agro360.css"), respondWith(promise) { response = promise; } });
assert.equal((await response).type, "error", "Missing CSS must not become HTML 200");
assert.equal(cachedPaths.includes("/field"), false);

let activation;
handlers.get("activate")({ waitUntil(promise) { activation = promise; } });
await activation;
assert.deepEqual(deleted.sort(), ["agro360-lookups-v43", "agro360-shell-v43"]);
console.log("PASS offline shell: diagnostics/API/auth/cross-origin bypass; public shell fallback; scoped cache invalidation.");
