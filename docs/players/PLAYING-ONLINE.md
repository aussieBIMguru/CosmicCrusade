# Playing Cosmic Crusade online

Play a friend anywhere in the world. One of you **hosts** and gets a 5-letter code; the other **joins** with that code. You both need:

1. **The same version of the game.** The window title shows it, e.g. "Cosmic Crusade 1.0.0". The game checks this when you join.
2. **A server address.** If whoever gave you the game set one up, it's already filled in. Otherwise ask them for it, or see [Host your own server](HOST-YOUR-OWN-SERVER.md). For a game on your home network, see the [LAN guide](LAN-GUIDE.md).

---

## Before your first online game

On the main menu, right-hand side:
- **Your name**: your opponent sees it.
- **Server**: the address you were given, e.g. `cosmic-crusade-relay.yourfriend.workers.dev`. Leave it as it is if it's already filled in.

Both are remembered for next time.

---

## Hosting a game

1. Set up the game on the left: rounds to win, deck mode, Warzones / Archons / Spacecraft, card pool, seed. **Your opponent plays with exactly these settings.**
2. Click **HOST GAME**.
3. A big code appears, e.g. **K7QPA**. Read it out to your opponent, or **click it to copy** it and paste it into a message.
4. Wait on that screen. When they join, you both go straight to the table. **CANCEL** stops waiting.

## Joining a game

1. Type the host's code in the box next to **JOIN**. Capitals don't matter, and codes never use 0, O, 1, I or L.
2. Click **JOIN** (or press **Enter**).

---

## At the table

- You're always at the **bottom, in blue**. Your opponent's name appears wherever it would say "AI".
- The glowing **eye** over your opponent's hand means they're deciding.
- **Chat:** type in the box at the bottom right of the side panel and press **Enter**. Press **Esc**, or click the table, to give the keyboard back to the game (Space = pass).
- The line next to **MENU** shows the connection: green "● Connected to …", or amber if something's wrong.

## If someone's connection drops

- The games notice within about 15 seconds (sooner if the connection closes cleanly).
- **The game that dropped reconnects by itself** and carries on where it left off. Any moves made in the meantime are sent again, so nothing is lost.
- While your opponent is away you see **"Waiting for <name>"** over the table. You can keep chatting, or click **LEAVE GAME**.
- If your own connection drops you see **"Reconnecting…"**. The game keeps trying for 3 minutes.
- If either of you **closes the game or clicks MENU**, the other is told and the game ends.
- If a game **crashes or is restarted**, that match can't be picked up again. Host a new one.

## After the game

The chat stays open until one of you leaves. For a rematch, go back to the **MENU** and host again.

## Fair play

Each player's game keeps a full copy of the match, including both hands, so that the two copies stay identical. The game never shows you your opponent's hand. It's meant for playing with friends.

---

## Problems?

| What you see | What to do |
|---|---|
| "Couldn't reach the server" | Check the **Server** box for typos, and that you're online. Ask the person who runs the server if it's up. |
| "No game with that code" | Check the code, and that the host is still on the waiting screen. |
| "That game already has two players" | Someone else joined first. Ask the host to host again. |
| "…different version…" or "…different card data…" | You're not on the same build. Install the same setup file as your opponent. |
| "OUT OF SYNC" | The two games stopped matching, almost always because of different versions. Install the same build and host a new game. |
| "Reconnecting…" that doesn't recover | Check your internet. After 3 minutes the game gives up; go back to the menu and host again. |

**More:** [How to play](HOW-TO-PLAY.md) · [LAN guide](LAN-GUIDE.md) · [Host your own server](HOST-YOUR-OWN-SERVER.md)
