# Host your own Cosmic Crusade server (free)

Online games need a **relay**: a tiny program on the internet that connects the two players' games under a room code and passes their moves between them. It knows nothing about the rules and stores nothing about the game.

This guide sets up **your own relay on Cloudflare's free plan**. Do it once, and it's always on. Then hand your friends a build of the game that already points at it.

**It costs nothing.** Cloudflare's free plan includes everything the relay needs. Going over a free limit just stops new connections until midnight UTC; the free plan never bills you. Normal play uses a tiny fraction of the limit (see *Costs and limits*).

**You need:**
- a Windows PC;
- an email address;
- about **20 minutes**;
- the Cosmic Crusade **source folder** (the one with `CosmicCrusade.sln` and `server\cloudflare`). The relay's code lives there, not in the installed game.

You don't need any web experience. Do the steps in order.

---

## Step 1: create a free Cloudflare account

1. Go to <https://dash.cloudflare.com/sign-up>.
2. Sign up with your email and a password, then click the link in the verification email.
3. You don't need a website, a domain or a payment card. If the dashboard asks what you want to do, skip it.

## Step 2: choose your workers.dev name

Your relay's address will be `https://cosmic-crusade-relay.<your-name>.workers.dev`.

1. In the Cloudflare dashboard, open **Workers & Pages** from the left menu. It may sit under **Compute (Workers)**; Cloudflare sometimes renames things.
2. If it asks you to choose a **workers.dev subdomain**, pick something short (e.g. your name) and confirm.

   If it doesn't ask, step 5 will.

## Step 3: install Node.js

Node.js gives you `npx wrangler`, Cloudflare's tool for uploading the relay.

1. Go to <https://nodejs.org> and download the **LTS** version for Windows.
2. Run the installer and accept the defaults.
3. Check it worked: press **Win**, type `powershell`, press **Enter**, then type `node --version` and press Enter. It should print a version, e.g. `v22.x.x`.

   If it says *not recognized*, close PowerShell and open a new one.

## Step 4: open PowerShell in the relay folder

1. In File Explorer, open the Cosmic Crusade source folder, then **server**, then **cloudflare**. You should see `wrangler.toml` and a `src` folder.
2. Click the **address bar** at the top of File Explorer, type `powershell` and press **Enter**.

   A PowerShell window opens with a prompt ending in `\server\cloudflare>`.

## Step 5: log in and deploy

Type each command and press **Enter**.

```powershell
npx wrangler login
```
- If it asks *"Need to install the following packages: wrangler… Ok to proceed?"*, type `y` and press Enter.
- A browser tab asks you to allow Wrangler access to your Cloudflare account. Click **Allow**, then close the tab.

```powershell
npx wrangler deploy
```
- After a few seconds it prints your address:
  ```
  Deployed cosmic-crusade-relay triggers
    https://cosmic-crusade-relay.yourname.workers.dev
  ```
- **Copy that address.** It's your server.
- If it asks you to register a workers.dev subdomain, follow its prompt, then run `npx wrangler deploy` again.

## Step 6: check it's live

Paste the address into a web browser. It should say:

```
Cosmic Crusade relay OK (protocol 1)
```

## Step 7: use it

Paste the address into the **Server** box on the game's menu. `https://…` or just `cosmic-crusade-relay.yourname.workers.dev` both work. Then host a game ([Playing online](PLAYING-ONLINE.md)).

---

## Step 8 (optional): give friends a build that's already set up

So your friends never have to type the address:

1. In the source folder, open `data\online.json` in Notepad.
2. Put your address between the quotes:
   ```json
   "defaultServer": "cosmic-crusade-relay.yourname.workers.dev"
   ```
3. Save, then build the installer: double-click `publish.bat` (see `docs\release\INSTALLER.md`).
4. Send your friends the new `artifacts\CosmicCrusade-Setup-<version>.exe`.

Their Server box is now pre-filled. Anyone can still type a different server, and their choice is remembered.

**Tip:** bump the version in `Directory.Build.props` for every build you send out, so everyone can see they're on the same one. Online games need matching versions.

---

## Looking after it

| Task | How |
|---|---|
| Watch games connect, live | In `server\cloudflare`, run `npx wrangler tail` (Ctrl+C stops it). |
| Update the relay after its code changes | In `server\cloudflare`, run `npx wrangler deploy` again. The address stays the same. |
| See usage | Cloudflare dashboard → **Workers & Pages** → **cosmic-crusade-relay** → Metrics. |
| Take it down | Cloudflare dashboard → the worker → **Settings** → **Delete**. |
| Test it on your PC first, without deploying | In `server\cloudflare`, run `npx wrangler dev`, then use `ws://localhost:8787` as the Server. Ctrl+C stops it. |

## Costs and limits

Free-plan limits reset daily at midnight UTC.
- **100,000 requests a day.** Each connection or reconnection counts as one request. Only every 20th message the relay receives counts as one request. Messages it sends, and its keep-alive pings, are free.
- **A game uses roughly 100–150 requests per hour of play**, so the limit covers hundreds of hours of play a day.
- **Storage:** each room keeps three tiny values for up to 6 hours.

Current limits: <https://developers.cloudflare.com/durable-objects/platform/pricing/>

## Other ways to host (advanced)

- **Your own always-on PC.** Run the **LAN Server** on it ([LAN guide](LAN-GUIDE.md)) and forward TCP port 5080 on your router to that PC. Friends then use `ws://<your public IP>:5080`.
  - The connection isn't encrypted.
  - Your public IP may change.
  - Your router and Windows Firewall both need opening.

  Cloudflare avoids all of this.
- **Any host that runs ASP.NET Core with WebSockets** can run the C# relay (`src\CosmicCrusade.Relay`). Free tiers elsewhere tend to sleep when idle, so the first connection waits while it wakes up.

The relay protocol is documented in `docs\design\multiplayer.md` if you want to write your own.

## Troubleshooting

| Problem | Fix |
|---|---|
| `npx` or `node` is "not recognized" | Close PowerShell and open a new one after installing Node.js. Reinstall Node.js LTS if it still fails. |
| `wrangler deploy` mentions Durable Objects or the free plan | Make sure PowerShell is in `server\cloudflare` (the folder with `wrangler.toml`). The relay uses the SQLite-backed Durable Objects that the free plan allows. |
| The browser shows "relay OK" but games can't connect | Use the address exactly as printed, with no `/room/…` on the end. Some work or school networks block WebSockets; try another network. |
| "This app version doesn't match the relay" | The game and the relay are from different releases. Deploy the relay from the same source folder you built the game from. |
