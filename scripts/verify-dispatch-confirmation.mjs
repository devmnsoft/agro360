import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import vm from "node:vm";

// Executes the real logistics.js (wwwroot/js) inside a minimal browser sandbox
// to prove the physical-exit gate:
//   1) cancel confirmation ({ confirmed:false }) sends NO dispatch request;
//   2) an absent confirmation component (confirm() -> undefined) sends NO dispatch request;
//   3) an explicit confirmation POSTs exactly once with the expected version and an idempotency key;
//   4) a retry with the same content reuses the intent key (same key, no new identity).

const ORDER_ID = "11111111-2222-3333-4444-555555555555";
const SHIPMENT_ID = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
const SHIPMENT_VERSION = 7;

function makeElement(tag, dataset = {}) {
    const el = {
        tag,
        dataset: { ...dataset },
        hidden: false,
        style: {},
        _html: "",
        _listeners: {}
    };
    Object.defineProperty(el, "innerHTML", {
        get() { return el._html; },
        set(v) { el._html = String(v); el._cache = {}; }
    });
    el.addEventListener = (name, fn) => { (el._listeners[name] ??= []).push(fn); };
    el.setAttribute = () => {};
    el.removeAttribute = () => {};
    el.focus = () => {};
    el.prepend = () => {};
    el.append = () => {};
    el.appendChild = () => {};
    el.remove = () => {};
    el.reportValidity = () => true;
    el.classList = { add() { }, remove() { }, toggle() { }, contains() { return false; } };
    el.querySelector = (sel) => el.querySelectorAll(sel)[0] ?? null;
    el.querySelectorAll = (sel) => {
        const m = /^\[data-([a-zA-Z-]+)\]$/.exec(sel ?? "");
        if (!m) return [];
        el._cache ??= {};
        if (!el._cache[sel]) {
            const re = new RegExp(`<button[^>]*\\bdata-${m[1]}[^>]*>`, "gi");
            const found = [];
            let match;
            while ((match = re.exec(el._html))) {
                const ds = {};
                for (const a of match[0].matchAll(/\bdata-([a-z-]+)="([^"]*)"/gi)) {
                    ds[a[1].replace(/-([a-z])/g, (_, c) => c.toUpperCase())] = a[2];
                }
                found.push(makeElement("button", ds));
            }
            el._cache[sel] = found;
        }
        return el._cache[sel];
    };
    return el;
}

const toasts = [];
const handledErrors = [];
let confirmResult = undefined;
let uuidSeq = 0;
const posts = [];

const orderDetail = {
    order: { order_number: "PED-1024", customer: "Cliente Piloto", currency: "BRL", payment_terms: "NET 30" },
    items: [{ id: "item-1", product: "Grão", unit: "kg", ordered_quantity: 10, cancelled_quantity: 0, reserved_quantity: 5, picked_quantity: 5, dispatched_quantity: 0, pending_quantity: 10, fulfillment_version: 3 }],
    reservations: [],
    shipments: [{ id: SHIPMENT_ID, number: "EXP-01", status: "CHECKED", version: SHIPMENT_VERSION, dispatched_at: null }],
    history: [],
    schedules: [],
    scheduleItems: []
};

const fetchStub = async (path, options = {}) => {
    const method = options.method ?? "GET";
    if (method === "POST") {
        posts.push({ path, body: JSON.parse(options.body) });
        return { ok: true, status: 204, json: async () => null };
    }
    const json = (payload) => ({ ok: true, status: 200, json: async () => payload });
    if (path.startsWith("/api/logistics/trips/fulfillment/orders/")) return json(orderDetail);
    if (path.startsWith("/api/logistics/trips/fulfillment/queue")) return json({
        items: [{ order_id: ORDER_ID, customer: "Cliente Piloto", status: "APPROVED", quantity_reserved: 5, quantity_pending: 10 }],
        page: 1, pageSize: 20, total: 1
    });
    if (path.startsWith("/api/logistics/trips/fulfillment/indicators")) return json({});
    if (path.startsWith("/api/logistics/trips/fulfillment/returns")) return json([]);
    return json({});
};

const store = {
    data: new Map(),
    getItem(k) { return this.data.has(k) ? this.data.get(k) : null; },
    setItem(k, v) { this.data.set(k, String(v)); },
    removeItem(k) { this.data.delete(k); },
    clear() { this.data.clear(); }
};

class FakeFormData {
    constructor(form) {
        this.map = new Map();
        for (const name of Object.keys(form?.elements ?? {})) {
            this.map.set(name, String(form.elements[name].value ?? ""));
        }
    }
    [Symbol.iterator]() { return this.map.entries()[Symbol.iterator](); }
}

const windowStub = {
    location: { search: `?orderId=${ORDER_ID}` },
    agro360Feedback: {
        confirm: async () => confirmResult,
        toast: (...args) => { toasts.push(args); },
        handleError: (...args) => { handledErrors.push(args); }
    }
};

const contentEl = makeElement("section");
const indicatorsEl = makeElement("div");
const queueFilters = {
    addEventListener() { },
    elements: { page: { value: "1" }, pageSize: { value: "20" }, number: { value: "" }, customer: { value: "" }, dueUntil: { value: "" }, status: { value: "" } }
};

const documentStub = {
    querySelector(sel) {
        switch (sel) {
            case 'meta[name="api-base"]': return { content: "" };
            case "#logistics-content": return contentEl;
            case "#logistics-indicators": return indicatorsEl;
            case "#queue-filters": return queueFilters;
            default: return null;
        }
    },
    querySelectorAll() { return []; },
    createElement(tag) {
        const span = { _t: "" };
        Object.defineProperty(span, "textContent", {
            set(v) { span._t = String(v ?? ""); },
            get() { return span._t; }
        });
        Object.defineProperty(span, "innerHTML", {
            get() {
                return span._t
                    .replace(/&/g, "&amp;")
                    .replace(/</g, "&lt;")
                    .replace(/>/g, "&gt;")
                    .replace(/"/g, "&quot;");
            }
        });
        return span;
    }
};

const context = vm.createContext({
    console,
    window: windowStub,
    document: documentStub,
    fetch: fetchStub,
    URLSearchParams,
    FormData: FakeFormData,
    sessionStorage: store,
    localStorage: store,
    crypto: { randomUUID: () => `test-uuid-${++uuidSeq}` }
});

vm.runInContext(await readFile(new URL("../src/Hosts/Agro360.Web/wwwroot/js/logistics.js", import.meta.url), "utf8"), context);
const settle = () => new Promise(r => setTimeout(r, 30));
await settle();

const dispatchPath = `/api/logistics/trips/fulfillment/${SHIPMENT_ID}/dispatch`;

// Scenario 1: user cancels the confirmation -> the physical exit must not happen.
confirmResult = { confirmed: false };
let [btn] = contentEl.querySelectorAll("[data-dispatch]");
assert.ok(btn, "A expedição conferida deve expor o botão Expedir.");
btn._listeners.click[0]();
await settle();
assert.equal(posts.length, 0, "Cancelamento da confirmação não pode enviar nenhuma requisição de dispatch.");
assert.equal(posts.filter(p => p.path === dispatchPath).length, 0);

// Scenario 2: confirmation component missing/absent (undefined) -> also no exit.
confirmResult = undefined;
[btn] = contentEl.querySelectorAll("[data-dispatch]");
btn._listeners.click[0]();
await settle();
assert.equal(posts.length, 0, "Confirmação ausente não pode autorizar saída física.");

// Scenario 3: explicit confirmation -> exactly one POST with expected version + idempotency key.
confirmResult = { confirmed: true };
[btn] = contentEl.querySelectorAll("[data-dispatch]");
btn._listeners.click[0]();
await settle();
assert.equal(posts.length, 1, "Confirmação explícita deve enviar exatamente uma requisição de dispatch.");
assert.equal(posts[0].path, dispatchPath);
assert.equal(posts[0].body.version, SHIPMENT_VERSION, "O dispatch deve revalidar contra a versão esperada.");
assert.ok(typeof posts[0].body.idempotencyKey === "string" && posts[0].body.idempotencyKey.length >= 8, "O dispatch deve portar chave de idempotência.");

// Scenario 4: retry after the response (timeout/new attempt, same content) reuses the intent key.
[btn] = contentEl.querySelectorAll("[data-dispatch]");
assert.ok(btn, "Após a expedição, o detalhe é recarregado e segue oferecendo o botão.");
btn._listeners.click[0]();
await settle();
assert.equal(posts.length, 2, "Novo clique confirmado deve enviar um segundo dispatch.");
assert.equal(posts[1].body.idempotencyKey, posts[0].body.idempotencyKey, "Retry com mesmo conteúdo deve reaproveitar a chave de intenção (sem nova identidade).");

console.log("PASS dispatch gate: cancel/absent confirmation never dispatch; explicit confirmation posts once with version + idempotency key; retry reuses the key.");
