// Cosmic Crusade relay (Cloudflare Worker + one Durable Object per room).
//
// The relay knows nothing about the game. It pairs two WebSockets (a host and a guest) under a room code and
// passes every text message from one to the other. The two apps run the game themselves (lockstep), so all the
// relay has to do is forward their moves, chat and heartbeats.
//
//   GET /                         -> "Cosmic Crusade relay OK" (open it in a browser to check the deploy)
//   GET /room/<CODE>?role=host|guest&key=<secret>&proto=1   (WebSocket upgrade)
//
// Rules:
//  - The first host to connect opens the room and its key is remembered. A host connecting later with the same key
//    replaces the old connection (that is how a dropped app reconnects); a different key gets "code in use".
//  - A guest can only join an open room. The first guest's key is remembered the same way; anyone else gets
//    "room full".
//  - The relay tells each side when the other connects or drops ("peer-joined" / "peer-left").
//  - "ping" is answered with "pong" by Cloudflare itself (no wake-up, no cost), which keeps idle connections alive.
//  - Rooms are forgotten 6 hours after they were opened.

import { DurableObject } from "cloudflare:workers";

const PROTO = 1;
const ROOM_TTL_MS = 6 * 60 * 60 * 1000;
const MAX_MESSAGE_CHARS = 64 * 1024;
const CODE_RE = /^[A-Z0-9]{4,8}$/;
const KEY_RE = /^[A-Za-z0-9_-]{8,64}$/;

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (url.pathname === "/" || url.pathname === "") {
      return new Response("Cosmic Crusade relay OK (protocol " + PROTO + ")\n", {
        headers: { "content-type": "text/plain; charset=utf-8" },
      });
    }

    const m = url.pathname.match(/^\/room\/([A-Za-z0-9]{4,8})\/?$/);
    if (!m) return new Response("Not found", { status: 404 });
    if (request.headers.get("Upgrade")?.toLowerCase() !== "websocket") {
      return new Response("Expected a WebSocket upgrade", { status: 426 });
    }

    const code = m[1].toUpperCase();
    const id = env.ROOMS.idFromName(code);
    return env.ROOMS.get(id).fetch(request);
  },
};

export class Room extends DurableObject {
  constructor(ctx, env) {
    super(ctx, env);
    // Answered by the runtime without waking this object.
    this.ctx.setWebSocketAutoResponse(new WebSocketRequestResponsePair("ping", "pong"));
  }

  async fetch(request) {
    const url = new URL(request.url);
    const role = url.searchParams.get("role");
    const key = url.searchParams.get("key") ?? "";
    const proto = Number(url.searchParams.get("proto") ?? "0");

    const pair = new WebSocketPair();
    const client = pair[0];
    const server = pair[1];

    const refuse = (msg) => {
      // Accept, explain, close: a plain HTTP error can't carry a readable reason to the app.
      server.accept();
      server.send(JSON.stringify({ t: "relay", ev: "error", msg }));
      server.close(4001, msg.slice(0, 100));
      return new Response(null, { status: 101, webSocket: client });
    };

    if (proto !== PROTO) return refuse("This app version doesn't match the relay (protocol " + PROTO + "). Update the app.");
    if (role !== "host" && role !== "guest") return refuse("Unknown role.");
    if (!KEY_RE.test(key)) return refuse("Bad key.");

    const now = Date.now();
    let openedAt = (await this.ctx.storage.get("openedAt")) ?? 0;
    if (openedAt && now - openedAt > ROOM_TTL_MS) {
      await this.ctx.storage.deleteAll();
      openedAt = 0;
    }

    if (role === "host") {
      const hostKey = await this.ctx.storage.get("hostKey");
      if (openedAt && hostKey && hostKey !== key) return refuse("That room code is already in use. Try hosting again.");
      if (!openedAt) {
        await this.ctx.storage.put({ openedAt: now, hostKey: key });
        await this.ctx.storage.setAlarm(now + ROOM_TTL_MS);
      }
    } else {
      if (!openedAt) return refuse("No game with that code. Check the code, and that the host is still waiting.");
      const guestKey = await this.ctx.storage.get("guestKey");
      if (guestKey && guestKey !== key) return refuse("That game already has two players.");
      if (!guestKey) await this.ctx.storage.put("guestKey", key);
    }

    // A reconnect replaces the old connection for the same role.
    for (const old of this.ctx.getWebSockets(role)) {
      try { old.serializeAttachment({ role, replaced: true }); } catch {}
      try { old.close(4000, "Replaced by a new connection"); } catch {}
    }

    this.ctx.acceptWebSocket(server, [role]);
    server.serializeAttachment({ role, replaced: false });

    const other = otherRole(role);
    const peerHere = this.live(other).length > 0;
    server.send(JSON.stringify({ t: "relay", ev: "joined", role, peer: peerHere }));
    for (const ws of this.live(other)) safeSend(ws, JSON.stringify({ t: "relay", ev: "peer-joined" }));

    return new Response(null, { status: 101, webSocket: client });
  }

  async webSocketMessage(ws, message) {
    if (typeof message !== "string") return; // the apps only send text
    if (message.length > MAX_MESSAGE_CHARS) return;
    const me = ws.deserializeAttachment();
    if (!me || me.replaced) return;
    for (const peer of this.live(otherRole(me.role))) safeSend(peer, message);
  }

  async webSocketClose(ws, code, reason) {
    this.dropped(ws);
    try { ws.close(code === 1005 ? 1000 : code, reason); } catch {}
  }

  async webSocketError(ws) {
    this.dropped(ws);
  }

  async alarm() {
    // Room expired: close anything still connected and forget it.
    for (const ws of this.ctx.getWebSockets()) {
      try { ws.close(4002, "Room expired"); } catch {}
    }
    await this.ctx.storage.deleteAll();
  }

  dropped(ws) {
    const me = ws.deserializeAttachment();
    if (!me || me.replaced) return; // a replaced connection going away isn't a drop
    try { ws.serializeAttachment({ ...me, replaced: true }); } catch {}
    for (const peer of this.live(otherRole(me.role))) safeSend(peer, JSON.stringify({ t: "relay", ev: "peer-left" }));
  }

  /** Open, current (not replaced) sockets for a role. */
  live(role) {
    return this.ctx.getWebSockets(role).filter((ws) => {
      const a = ws.deserializeAttachment();
      return a && !a.replaced && ws.readyState === 1;
    });
  }
}

function otherRole(role) {
  return role === "host" ? "guest" : "host";
}

function safeSend(ws, text) {
  try { ws.send(text); } catch {}
}
