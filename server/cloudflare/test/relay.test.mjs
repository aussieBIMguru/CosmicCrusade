// Offline smoke test of the relay's room logic with a fake Cloudflare runtime.
// Run from server/cloudflare:   node test/relay.test.mjs
import { readFileSync, writeFileSync, mkdtempSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import assert from "node:assert/strict";

// ---- fake runtime -------------------------------------------------------------------------------------------
class FakeSocket {
  constructor(name) { this.name = name; this.sent = []; this.readyState = 1; this.peer = null; this.att = null; this.tags = []; this.closed = null; }
  accept() {}
  send(t) { if (this.readyState !== 1) throw new Error("closed"); this.sent.push(t); }
  close(code, reason) { if (this.readyState === 3) return; this.readyState = 3; this.closed = { code, reason }; }
  serializeAttachment(a) { this.att = structuredClone(a); }
  deserializeAttachment() { return this.att; }
  json() { return this.sent.map((s) => { try { return JSON.parse(s); } catch { return s; } }); }
}
globalThis.WebSocketPair = class { constructor() { const c = new FakeSocket("client"), s = new FakeSocket("server"); c.peer = s; s.peer = c; return [c, s]; } };
globalThis.WebSocketRequestResponsePair = class { constructor(a, b) { this.a = a; this.b = b; } };
globalThis.Response = class { constructor(body, init = {}) { this.body = body; this.status = init.status ?? 200; this.webSocket = init.webSocket; } async text() { return String(this.body ?? ""); } };

class FakeStorage {
  constructor() { this.map = new Map(); this.alarm = null; }
  async get(k) { return this.map.get(k); }
  async put(k, v) { if (typeof k === "object") { for (const [a, b] of Object.entries(k)) this.map.set(a, b); } else this.map.set(k, v); }
  async deleteAll() { this.map.clear(); }
  async setAlarm(t) { this.alarm = t; }
}
class FakeCtx {
  constructor() { this.storage = new FakeStorage(); this.sockets = []; }
  setWebSocketAutoResponse() {}
  acceptWebSocket(ws, tags) { ws.tags = tags; this.sockets.push(ws); }
  getWebSockets(tag) { return this.sockets.filter((s) => s.readyState !== 3 && (!tag || s.tags.includes(tag))); }
}

// Load index.js with the cloudflare:workers import swapped for a stub base class.
const src = readFileSync(new URL("../src/index.js", import.meta.url), "utf8")
  .replace('import { DurableObject } from "cloudflare:workers";', "class DurableObject { constructor(ctx, env) { this.ctx = ctx; this.env = env; } }");
const dir = mkdtempSync(join(tmpdir(), "relay-"));
writeFileSync(join(dir, "index.mjs"), src);
const mod = await import(join(dir, "index.mjs"));

// ---- helpers ------------------------------------------------------------------------------------------------
const rooms = new Map();
const env = { ROOMS: { idFromName: (n) => n, get: (id) => { if (!rooms.has(id)) rooms.set(id, new mod.Room(new FakeCtx(), {})); const room = rooms.get(id); return { fetch: (req) => room.fetch(req) }; } } };
const req = (path, upgrade = true) => ({ url: "https://relay.test" + path, headers: { get: (h) => (h === "Upgrade" && upgrade ? "websocket" : null) } });
async function connect(code, role, key) {
  const res = await mod.default.fetch(req(`/room/${code}?role=${role}&key=${key}&proto=1`), env);
  assert.equal(res.status, 101);
  return res.webSocket.peer; // the server end: what the room sends to the app lands in .sent
}
const room = (code) => rooms.get(code.toUpperCase());
async function closeFromApp(code, ws) { ws.readyState = 3; await room(code).webSocketClose(ws, 1006, ""); }

// ---- tests ---------------------------------------------------------------------------------------------------
let n = 0;
const test = async (name, fn) => { await fn(); n++; console.log("ok -", name); };

await test("health check", async () => {
  const r = await mod.default.fetch(req("/", false), env);
  assert.match(await r.text(), /relay OK/);
});

await test("non-websocket room request is refused", async () => {
  const r = await mod.default.fetch(req("/room/ABCD", false), env);
  assert.equal(r.status, 426);
});

await test("guest can't join a room nobody opened", async () => {
  const g = await connect("NOPE1", "guest", "guestkey123");
  assert.equal(g.json()[0].ev, "error");
  assert.equal(g.readyState, 3);
});

await test("host + guest pair up and messages are forwarded both ways", async () => {
  const h = await connect("abcd", "host", "hostkey1234");
  assert.deepEqual(h.json()[0], { t: "relay", ev: "joined", role: "host", peer: false });
  const g = await connect("ABCD", "guest", "guestkey123");
  assert.deepEqual(g.json()[0], { t: "relay", ev: "joined", role: "guest", peer: true });
  assert.equal(h.json()[1].ev, "peer-joined");

  await room("ABCD").webSocketMessage(h, '{"t":"chat","text":"hi"}');
  assert.equal(g.sent.at(-1), '{"t":"chat","text":"hi"}');
  await room("ABCD").webSocketMessage(g, '{"t":"d","n":1}');
  assert.equal(h.sent.at(-1), '{"t":"d","n":1}');
});

await test("a different host key can't steal an open room; a third player is refused", async () => {
  const x = await connect("ABCD", "host", "otherkey999");
  assert.match(x.json()[0].msg, /already in use/);
  const y = await connect("ABCD", "guest", "intruder1234");
  assert.match(y.json()[0].msg, /two players/);
});

await test("guest drop is reported; guest reconnect with its key replaces the old socket", async () => {
  const r = room("ABCD");
  const h = r.ctx.getWebSockets("host")[0];
  const g1 = r.ctx.getWebSockets("guest")[0];
  await closeFromApp("ABCD", g1);
  assert.equal(h.json().at(-1).ev, "peer-left");

  const g2 = await connect("ABCD", "guest", "guestkey123");
  assert.equal(g2.json()[0].peer, true);
  assert.equal(h.json().at(-1).ev, "peer-joined");
});

await test("host reconnect while old socket still looks open: old one closed, no false peer-left", async () => {
  const r = room("ABCD");
  const g = r.ctx.getWebSockets("guest")[0];
  const before = g.sent.length;
  const h1 = r.ctx.getWebSockets("host")[0];
  const h2 = await connect("ABCD", "host", "hostkey1234");
  assert.equal(h1.closed?.code, 4000);
  await r.webSocketClose(h1, 4000, "Replaced"); // the runtime reports the old socket's close later
  const evs = g.json().slice(before).map((m) => m.ev);
  assert.deepEqual(evs, ["peer-joined"]);
  // Messages from the replaced socket are ignored, the new one works.
  await r.webSocketMessage(h1, "old");
  await r.webSocketMessage(h2, "new");
  assert.equal(g.sent.at(-1), "new");
});

await test("wrong protocol is refused with a readable message", async () => {
  const res = await mod.default.fetch(req("/room/ZZZZ?role=host&key=hostkey1234&proto=0"), env);
  assert.match(res.webSocket.peer.json()[0].msg, /version/);
});

await test("expired room can be reopened by a new host", async () => {
  const r = room("ABCD");
  r.ctx.storage.map.set("openedAt", Date.now() - 7 * 3600 * 1000);
  const h = await connect("ABCD", "host", "brandnewkey1");
  assert.equal(h.json()[0].ev, "joined");
});

console.log(`\n${n} relay tests passed`);
