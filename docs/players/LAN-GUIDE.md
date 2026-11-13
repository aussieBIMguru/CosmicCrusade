# Playing on your home network (LAN)

Two PCs in the same house (or office, or LAN party) can play each other with **no internet server and no accounts**. One PC runs the small **Cosmic Crusade LAN Server** that comes with the game; both games connect to it.

Both PCs need Cosmic Crusade installed, the **same version** (check the window title), and to be on the **same network** (the same Wi-Fi or router).

---

## Step 1: start the LAN Server (on one PC only)

Pick one PC to be the server; it can also be one of the players.

1. Open the **Start menu** and click **Cosmic Crusade LAN Server**.
2. A black window opens. It looks like this:

   ```
    Cosmic Crusade LAN Server
    On THIS PC, put this in the game's Server box:   ws://localhost:5080
    On OTHER PCs on your network, use one of these:
        ws://192.168.1.20:5080
    Leave this window open while you play. Close it to stop the server.
   ```

3. **Windows Firewall** will probably ask whether to allow it. Tick **Private networks** and click **Allow access**.
   - Windows may ask for an administrator password for this. That's the one time it's needed.
   - If you click Cancel by mistake, see *Troubleshooting* below.
4. **Leave that window open** while you play. Minimising it is fine.

Write down the `ws://192.168…` address it shows. If it shows more than one, the first is usually right.

## Step 2: point both games at it

On each PC, open Cosmic Crusade and look at the right-hand side of the menu:

| PC | Server box |
|---|---|
| The PC running the LAN Server | `ws://localhost:5080` |
| Every other PC | The address from step 1, e.g. `ws://192.168.1.20:5080` |

Fill in **Your name** too.

## Step 3: host and join

1. On one PC, click **HOST GAME** and read out the 5-letter code.
2. On the other, type the code next to **JOIN** and press **Enter**.

Everything else (chat, reconnecting after a drop) works as in [Playing online](PLAYING-ONLINE.md).

When you're done, close the LAN Server window.

---

## Troubleshooting

| Problem | Fix |
|---|---|
| The other PC says "Couldn't reach the server" | **Almost always the firewall** on the server PC. Press **Win**, type *Allow an app through Windows Firewall*, open it, click **Change settings**, find **CosmicCrusade-LanServer** and tick **Private**. If it isn't listed, click **Allow another app…** and pick `CosmicCrusade-LanServer.exe` from `%LocalAppData%\Programs\Cosmic Crusade`. |
| Still can't reach it | Check both PCs are on the same network, not one on guest Wi-Fi. On the server PC, open **Settings → Network & internet → (your network) → Properties** and make sure it's set to **Private network**: Windows blocks more on *Public*. |
| The address it showed doesn't work | On the server PC, press **Win**, type `cmd`, press Enter, type `ipconfig` and press Enter. Use the **IPv4 Address** under your Wi-Fi or Ethernet adapter, as `ws://<that address>:5080`. |
| "Couldn't reach the server" on the server PC itself | Check the LAN Server window is still open, and that its Server box says `ws://localhost:5080`. |
| Another program already uses port 5080 | Start the server on another port. Press **Win+R** and run `"%LocalAppData%\Programs\Cosmic Crusade\CosmicCrusade-LanServer.exe" --port 6000`, then use `:6000` instead of `:5080` in both games. |
| "…different version…" | Install the same setup file on both PCs. |

## Playing over the internet instead
A LAN Server only works inside your own network. For friends elsewhere, use a free online relay: see [Host your own server](HOST-YOUR-OWN-SERVER.md).
