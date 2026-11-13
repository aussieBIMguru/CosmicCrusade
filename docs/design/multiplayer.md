# Cosmic Crusade: online multiplayer (design)

Status: **written 8 Nov 2026, not yet compiled** (no .NET SDK in Claude's sandbox). The relay's room logic is tested in Node (`server/cloudflare/test`, 9 passing). The C# side has xUnit tests in `NetTests`. Player guides: `docs/players/` (PLAYING-ONLINE, LAN-GUIDE, HOST-YOUR-OWN-SERVER); testing from Visual Studio: `docs/multiplayer/SETUP-GUIDE.md`.

## Goals (Gavin, 8 Nov 2026)
- Two people play each other, each in their own copy of the Windows app.
- Free and simple to host: no server to look after, no monthly bill.
- Chat between the players, player names set on the main menu, and detection of a dropped player.

## Architecture: lockstep over a dumb relay

```
 App (host, player 0)                Relay (room ABCDE)                App (guest, player 1)
 GameEngine + LocalSeat/RemoteSeat ⇄  pairs 2 WebSockets, forwards  ⇄ GameEngine + RemoteSeat/LocalSeat
```

- **Both apps run the full engine.** The engine is deterministic: all randomness comes from `GameState.Rng`, seeded from `GameOptions.Seed`. Every decision is a pick from a list both engines compute identically: `DecisionContext.Legal` or `ChoiceRequest.Options`. So the only thing that crosses the wire is each player's own decisions, as indices.
- **The relay knows nothing about the game.** It pairs a host and a guest under a room code, forwards text messages and reports connects and drops. It stores three small values per room (when it opened, plus the host's and guest's keys), which it forgets after 6 hours.
- **Why not host-authoritative?** The WPF table is built on `GameState` plus `GameEvent`s. Streaming state or events to a thin client would mean serialising most of the engine. Lockstep needed no engine changes at all.
- **Trade-off:** each app holds the whole state, both hands included. The UI never shows the opponent's hand, but a cheat with a debugger could read it. That's acceptable for friendly play. A future server-authoritative mode would fix it (see Next steps).

## Seats (`Core/Net/LockstepSeats.cs`)
| Seat | What it does |
|---|---|
| `LocalSeat(inner, net)` | Wraps the human's `HumanController`. After each decision it sends `{t:"d", n, k, v, h}`: `k` = `a` (turn action, index into `Legal`), `r` (React window, index or -1 for "no response"), `c` (ability choice, the picked option indices). `h` = `StateHash` *before* deciding. |
| `RemoteSeat(net)` | Waits for the opponent's next decision, checks its kind and checksum against our own state, and returns `Legal[i]` / the picks. Any mismatch raises `DesyncException` and tells the other side. `WaitingChanged` drives the UI's "thinking" eye. |

The host is engine player 0 and the guest player 1. `GameEngine(state, p0, p1)` is built with the local seat in your own index.

**`StateHash`** is FNV-1a over integers: round, phase, active and first player, last winner, and every card's location, instance id, definition id, base-rank override, persistent modifiers, races bitmask and suppression. It also covers deck and pile order, locks, round wins and the Archon state. It never uses `string.GetHashCode()`, which .NET randomises per process.

**Determinism audit (8 Nov 2026):** Core has no `DateTime`, `Guid` or unseeded `Random` in the rules path (only in the Monte Carlo search, which isn't used online). Every `HashSet<CardInstance>` that is iterated is only ever added to, so .NET walks it in insertion order whatever the hash codes. `Distinct` and `GroupBy` keep first-seen order. Caches keyed by string are lookups only.

## Connection (`Core/Net/NetConnection.cs`)
- `NetConnection.ForServer(server, role, code)` makes a fresh `WebSocketTransport` per attempt. `StartAsync` connects and reads the relay's first message (`joined` / `error`). A relay `error` becomes `NetRefusedException`; anything else becomes `NetException` ("Couldn't reach the server").
- **Heartbeat:** every 4 s the app sends `"ping"` to the relay, which answers `"pong"`; on Cloudflare that answer costs nothing. It also sends `{t:"hb", w, n, have}` to the opponent.
  - Hearing nothing at all for 15 s means our own link is dead: drop it and reconnect.
  - Hearing nothing from the opponent for 15 s raises `OpponentPresenceChanged(false)`.
- **Reconnect:** back-off from 0.5 s up to 8 s, for up to 3 minutes. The same room code and secret key make the relay give the seat back (the old socket is replaced). After that comes `Failed`.
- **No lost moves:** every decision is numbered and logged. Whenever the opponent (re)appears (`joined` with a peer present, or `peer-joined`), each side sends `{t:"resume", have:N}`, and the other side re-sends everything after N. Duplicates are ignored, and a gap triggers another resume. All sends go through one queue, in order.
- **Stuck detection:** in lockstep only one engine waits for the other at a time. If both report `w:true` with every decision delivered both ways, on two heartbeats in a row, that's a desync and both games stop. This catches the case where the two engines disagree about whose decision it is, which the checksum alone can't.
- **Leaving:** `LeaveAsync` sends `{t:"bye"}` and closes; the other side raises `OpponentQuit`. Closing the window sends it too (`MainWindow.OnClosing`).
- Events are raised on the `SynchronizationContext` that created the connection (the WPF dispatcher).

## Lobby (`Core/Net/NetLobby.cs`)
1. The host connects as `host` with a new 5-letter code. The alphabet has no 0/O or 1/I/L.
2. The guest connects as `guest` and sends `{t:"hello", ver, name, data}`. `data` is a SHA-256 fingerprint of `cards.json`, with line endings normalised. The guest repeats the hello every 3 s until it gets an answer.
3. The host checks `ver == NetProtocol.GameVersion` and that the data matches. If not, it sends `reject` and both sides show why. Otherwise it sends `{t:"start", ver, options, hostName, guestName}` and keeps it as `HelloReply`, so any later hello gets the same answer.
4. `options` (`NetOptions`) carries everything that affects the rules or the shuffle: rounds, empty-deck rule, modules, No Leaders, deck mode, seed, hand targets, every copy count and the leader-related card list. AI difficulty is left out.
5. A guest with the same name as the host gets " 2" added.

**Bump `NetProtocol.GameVersion` whenever the engine's rules change.** Mismatched rules would desync, and the version check turns that into a clear message before the game starts. `NetProtocol.RelayVersion` must match `PROTO` in `server/cloudflare/src/index.js` and `Room.Proto` in the C# relay.

## Relay protocol (identical in both relays)
- `GET /` → `Cosmic Crusade relay OK (protocol 1)` (health check).
- `GET /room/<CODE>?role=host|guest&key=<8–64 url-safe chars>&proto=1` (WebSocket).
- The first host's key opens the room. The same key replaces the old connection (a reconnect). A different key gets "already in use".
- A guest can only join an open room. The first guest's key is kept; a different key gets "already has two players".
- To the connecting app: `{t:"relay", ev:"joined", role, peer}`. To the other app: `peer-joined`. On close: `peer-left`. A refusal is `{t:"relay", ev:"error", msg}`, then close code 4001. A replaced socket is closed with code 4000.
- Text messages up to 64 KB are forwarded verbatim. `"ping"` → `"pong"`.

| Relay | Where | Use |
|---|---|---|
| Cloudflare Worker + SQLite Durable Object (one per room, hibernation API) | `server/cloudflare` (`wrangler.toml`, `src/index.js`, `test/relay.test.mjs`) | Online. Free plan. `npx wrangler deploy`. |
| ASP.NET Core minimal API | `src/CosmicCrusade.Relay` (port 5080; override with `RELAY_URLS`) | Testing on one PC (multiple startup projects), LAN play. |

**Free-plan budget:** a connection is 1 request, 20 incoming WebSocket messages count as 1 request, outgoing messages and auto-answered pings are free, and the limit is 100,000 requests a day. A game uses roughly 100–150 requests per hour. Between messages the Durable Object is idle and eligible for hibernation, so it isn't billed for duration.

## App
- `Board/Perspective.cs`: `Me` / `Opp`, `OppName`, `TheOpp`, `OppPossessive`… About 80 UI sites that assumed "you are player 0" now go through it, as do all the "AI" strings. `BoardLayout.Center` puts `Perspective.Me` at the bottom; result banners, totals, pair chevrons, the lean and the tug bar are all relative to `Me`. Two places deliberately keep engine indices: `IsDecidingDuel` (t0/t1) and anything reading `Total0`/`Total1` directly.
- `Views/GameView.Online.cs`: `OnlineSession(Net, Setup)`, seat wiring, the chat box (Enter sends, Esc / clicking the table returns keyboard control to the game), the connection status line, and the waiting overlay. The overlay appears after a 3 s grace period while the opponent is away, and at once while we reconnect; it has a LEAVE GAME button. `OnlineEnded` covers the opponent leaving, the connection being lost for good, and desyncs. Taunts are off online; the AI eye shows the opponent deciding.
- Setup screen (right column): **Your name** (also used against the AI), **Server**, HOST GAME (uses the setup on the left; click the code to copy it), JOIN + code, CANCEL. The name and server are saved in `%AppData%\CosmicCrusade\settings.json` (`AppSettings`).
- Online, PLAY AGAIN takes you back to the menu; for a rematch, host again.

## Not supported yet / next steps
- **Resuming after an app restart or crash.** The decision log makes this possible: replay the log into a fresh engine with a null sink, then rebuild the table. It needs a "fast-forward" mode in `GameView` and a stable player key kept across restarts.
- **Rematch button** (the same pair, a new seed) without going back to the lobby.
- **Spectators**: the relay would need a third role, and the spectator would get the decision log.
- **Turn timer / AFK handling.**
- **Server-authoritative mode** (hidden hands stay hidden): run `GameEngine` on the server and send each client only what its seat may see. That's a big change: events would have to be filtered and serialised per seat.
- **More than 2 players**: the engine is 2-player only (rules spec scope).

## Tests
- `NetTests` (in-memory relay with the same rules, run on a single-thread pump like the WPF dispatcher):
  - protocol round-trips, server-box parsing, codes/keys/names/fingerprints;
  - the state hash;
  - full heuristic-vs-heuristic lockstep games: base game, every module on, Random Draw + No Leaders;
  - guest drops every 37 messages, and the game still finishes identically;
  - a tampered seed is caught as a desync;
  - lobby refusal on different data, an unknown code, duplicate names, chat, leaving, and drop + return presence events.
- `server/cloudflare/test/relay.test.mjs` (`node test/relay.test.mjs`): the Worker's room logic against a fake Cloudflare runtime.
