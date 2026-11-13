# Testing multiplayer from the source (developers)

This guide is for working on the code in Visual Studio. Players have their own guides in `docs/players/`:
- [Playing online](../players/PLAYING-ONLINE.md)
- [LAN guide](../players/LAN-GUIDE.md): uses the installed **LAN Server**
- [Host your own server](../players/HOST-YOUR-OWN-SERVER.md): the free Cloudflare relay, and baking its address into a build

How it all works (lockstep, relay protocol, reconnects): [`docs/design/multiplayer.md`](../design/multiplayer.md).

---

## Option 1: two game windows and the C# relay (no accounts, no internet)

1. Open `CosmicCrusade.sln`.
2. Right-click the **Solution** → **Configure Startup Projects…** → **Multiple startup projects**. Set **CosmicCrusade.Relay** and **CosmicCrusade.App** to *Start*, then click **OK**.
3. Press **F5**. A console window opens: that's the relay, and it prints the addresses to use. One game window opens too.
   - If Windows Firewall asks, allow **Private networks**.
4. Open a second game window: right-click **CosmicCrusade.App** → **Debug** → **Start New Instance**. Or run `src\CosmicCrusade.App\bin\Debug\net8.0-windows\CosmicCrusade.exe`.
5. In both windows, use different names, and set **Server** to `ws://localhost:5080`.
6. Window 1: **HOST GAME**, then click the code to copy it. Window 2: paste the code next to **JOIN** and press **Enter**.

Things to try:
- **Chat** between the windows.
- **A drop:** close the relay console and start it again (right-click CosmicCrusade.Relay → Debug → Start New Instance). Both games show "Reconnecting…" and carry on.
- **Leaving:** click MENU in one window; the other is told.
- **A second PC:** use the relay's printed `ws://192.168…:5080` address there.

The relay takes `--port 6000` (project properties → Debug → command-line arguments) or the `RELAY_URLS` environment variable.

## Option 2: the Cloudflare relay, run locally

This runs the exact code that goes online, without deploying. It needs Node.js.

1. Open PowerShell in `server\cloudflare` (File Explorer address bar → `powershell` → Enter).
2. Run `npx wrangler dev`. Type `y` if it asks to install wrangler. It's ready when it prints `Ready on http://localhost:8787`.
3. Check that `http://localhost:8787` in a browser says "relay OK".
4. Start two game windows as in Option 1, with **Server** set to `ws://localhost:8787`. The C# relay isn't needed.
5. **Ctrl+C** stops it.

## Automated tests
- `NetTests` (xUnit) play whole online games between two engines over an in-memory relay, including dropped connections, desync detection, the lobby, chat and leaving.
- `server\cloudflare\test\relay.test.mjs` tests the Worker's room logic: run `node test/relay.test.mjs` in `server\cloudflare`.

## Remember
- **Bump `NetProtocol.GameVersion`** whenever the engine's rules change. Different rules on each side would desync, and the version check catches it at the lobby.
- **Bump `<Version>` in `Directory.Build.props`** for each build you hand out.
- `NetProtocol.RelayVersion`, `PROTO` in `server/cloudflare/src/index.js` and `Room.Proto` in the C# relay must match.
