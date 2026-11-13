# Cosmic Crusade relay (Cloudflare Worker)

The free online relay for multiplayer. It pairs two game apps under a room code and forwards their messages; it knows nothing about the rules.

- **Deploy:** in this folder, run `npx wrangler login` once, then `npx wrangler deploy`. Full walkthrough: `docs/players/HOST-YOUR-OWN-SERVER.md`.
- **Check:** open the printed `https://cosmic-crusade-relay.<you>.workers.dev` address in a browser. It should say "relay OK".
- **Live log:** `npx wrangler tail`.
- **Test the room logic offline:** `node test/relay.test.mjs`.
- **Protocol:** `docs/design/multiplayer.md`. Keep `PROTO` here equal to `NetProtocol.RelayVersion` in the app and `Room.Proto` in `src/CosmicCrusade.Relay`.
