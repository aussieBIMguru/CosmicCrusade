# Cosmic Crusade 1.1.0

A table-feel update: a turn clock with a burning fuse, a way to look through the discard piles, and something to fidget with while you think.

## New

### ⏱ Turn clock
- Each player has a set time to decide on their turn: **2 minutes** by default.
- Change it on the main menu with **Turn clock (minutes per turn)**, from 0.5 to 10 minutes in half-minute steps. **0 turns it off.** Your setting is remembered.
- The time left shows above the **PASS** button. The clock only runs while the game is waiting for you, so animations and your opponent's thinking never use it up.
- Everything you do in one turn shares the same time: choosing targets and answering a React to your own play. Questions you get on your opponent's turn (React windows, Start-up prompts) each get the full time.
- **With 15 seconds left, a fuse lights** on the line across the middle of the board and burns towards the PASS button. It throws sparks, smoke and a crackle, and ticks for the last 5 seconds.
- If the fuse burns out, you **pass**. In a React window you let it happen, and in a choice the first option is picked. The log says what happened.
- **Online:** the host's clock setting is used for both players, and you can see your opponent's clock and fuse while they decide. The clock pauses while someone is reconnecting.

### 🗂 Look through the piles
- **Left-click a face-up pile** (Retired, Nullified, Issued or Active Warzone) to see every card in it, **newest first**, numbered from the top.
- Switch between **CARD VIEW** (the cards laid out in order) and **LIST VIEW** (name, race, type and printed Rank). The game remembers your choice until you close it.
- Hover a card to preview it large. Right-click it to see it full size.
- The viewer updates live as cards arrive. To close it, click outside it, press **CLOSE** or press **Esc**.
- Right-clicking a pile still shows its top card. When a pile is glowing as part of your move, a left-click on it makes the move.

### ✨ Tap the table
- Click any bare part of the board for a small ripple and a soft "tink": blue on your half, red on your opponent's.
- It doesn't affect the game and only you see it.

## Compatibility
- **Online:** 1.1.0 can still play against 1.0.0 (the game rules haven't changed). If the host is on 1.0.0, though, there's no turn clock for either player, so it's best if you both update.
- Settings, your name and your server address carry over from 1.0.0.

## Install
Download **`CosmicCrusade-Setup-1.1.0.exe`** below and run it. It installs over 1.0.0 in place, so there's no need to uninstall first. You can check the download against `CosmicCrusade-Setup-1.1.0.sha256.txt`.

---
Software under the MIT licence. Game concepts, rules, theme and card text © 2026 Gavin Nicholls. Card art generated with Google Gemini, and the engine was developed with Claude.
