# How to play Cosmic Crusade

A quick start in the app. You don't need to know the rules first: the game only lets you make legal moves and **highlights everything you can do**. When you want the details, see the [Rulebook](RULEBOOK.md) and the [Glossary](GLOSSARY.md).

---

## The goal in 30 seconds

- Each **round**, you and your opponent take turns putting **Units** onto the battlefield and playing **Orders**.
- When you've **both passed**, add up the **Rank** (strength) of each side's Units. **The higher total wins the round.**
- Win **3 rounds** to win the game.
- Every round starts with a fresh battlefield and your hand topped up to **5 Units and 3 Orders**. Spending everything early leaves you nothing to answer with, so timing matters as much as strength.

---

## 1. The main menu

| Area | What it's for |
|---|---|
| **Game setup** (left) | Rounds to win, AI difficulty (Easy / Normal / Hard), the **turn clock** (minutes per turn, 2 by default; 0 = off), deck mode, what happens when a deck runs out, and a seed (leave blank for a random shuffle). |
| Module check boxes | **Warzones**, **Archons**, **Spacecraft**, and **Leaders** (untick for No Leaders mode). Leave them off for your first games. |
| **Card pool** (middle) | Every card and how many copies are in the decks. Click a card to preview it; edit **Copies** to change the decks. **RESET COUNTS** puts them back. |
| **Player** (right) | **Your name**, shown on the score panel and to online opponents. |
| **Play online** (right) | Host or join a game with a friend: see [Playing online](PLAYING-ONLINE.md). |
| **PLAY VS AI** | Starts a game against the computer. |

**First game:** type your name, leave everything else as it is, and click **PLAY VS AI**.

---

## 2. The table

- **Your side is at the bottom (blue)**, your opponent's at the top (red).
- Your **hand** is the row of cards along the bottom. Your opponent's hand is face down at the top.
- Each side has:
  - three **Unit Nodes** (U1–U3) and a **Leader Node** (L), where Units fight and score;
  - three **Standby Nodes** behind them, where Units wait without scoring;
  - two **Order Nodes**, for Ongoing Orders.
- The **decks** and the **Retired**, **Nullified** and **Issued** piles are at the side. Their counts show on them.
- The **side panel** on the right shows:
  - the score;
  - both players' current totals, and a bar showing who's ahead right now;
  - a hint telling you what you can do;
  - the **game log** (everything that happened);
  - the **PASS** button.

---

## 3. Your turn

On your turn you do **one** thing, then it's your opponent's turn. The cards you can play **glow**.

| To… | Do this |
|---|---|
| **Deploy a Unit** | Click a glowing Unit in your hand, then click a glowing node. Leaders only go on your Leader Node. |
| **Issue an Action** | Click the glowing Action, then click the **ISSUED** pile (or press **Enter**). If it needs a target, the game asks you next. |
| **Issue an Ongoing** | Click it, then click a glowing **Order Node**. It keeps working until the end of the round. |
| **Advance from Standby** | Click your Unit in Standby, then the glowing node in front of it. |
| **Assemble** (Synthenoids) | Click a card with Assemble, then a **green** Synthenoid to add its Rank. |
| **Pass** | Click **PASS** or press **Space**. |
| **Change your mind** | Right-click (or **Esc**) cancels the card you picked. |

When a card asks you to choose something (a target, a node, "yes or no"), a panel appears. Pick, then click **CONFIRM** or press **Enter**.

**The round ends when you both pass one after the other.** Passing isn't final: if your opponent plays something after you pass, you can act again.

**Turn clock.** You have a set time to decide each turn (2 minutes unless you change it on the main menu); it's shown above the PASS button. It only runs while the game is waiting for you, and choosing targets for your card uses the same time. With **15 seconds** left, a fuse lights on the line across the middle of the board and burns towards the PASS button. If it burns out, you pass (or, in a React window, you let it happen; in a choice, the first option is picked). Online, the host's clock setting is used for both players.

**Tapping the table.** Click any bare part of the board for a little ripple. It doesn't do anything; it's just fun to poke while you think.

---

## 4. Reacting

Some Orders are **Reacts**: you play them *in response* to something, even on your opponent's turn. For example, *Shadow Strike* Nullifies a Unit while it's being deployed.

- With **Pause when I can React** ticked (side panel), the game stops and asks whenever one of your Reacts can answer what just happened.
  - The Reacts you can use glow; click one, or click **OK / PASS** to let it through.
  - Responses resolve **last one first**. The **STACK** panel shows what's waiting.
- Untick it if you'd rather never be asked.

---

## 5. Reading the board

- **Hover** any card to read it in the inspector.
- **Right-click** any card to see it full size (click, right-click or Esc to close).
- **Click a face-up pile** (Retired, Nullified, Issued, Active Warzone) to see every card in it, newest first. **LIST VIEW** / **CARD VIEW** switches between a compact list and the cards laid out in order. Hover to preview, right-click to enlarge, click outside or press Esc to close. (While a pile is glowing as part of your move, a click uses it instead; right-click still shows its top card.)
- **Magnify hand cards on hover** enlarges the hand card under your mouse.
- The **totals** and the bar at the top of the side panel show who would win if the round ended now.
- Chevrons between opposed nodes show who's winning each pair, which matters if the totals tie.
- The **log** lists every move, Duel and effect, with the reason when something couldn't happen.

---

## 6. How a round goes

1. **Start-up:** both hands are topped up to 5 Units and 3 Orders. *(Warzones)* A Warzone is revealed. Last round's winner goes first.
2. **Main Phase:** take turns until you've both passed one after the other.
3. **Scoring:** the totals are compared. If they tie, the player who wins more face-to-face pairs wins. A banner shows the result.
4. **Clean-up:** every Unit on the battlefield is retired and Ongoing Orders are discarded. **Units in Standby stay for next round.**

---

## 7. Tips for your first games

- **Don't rush.** Keeping Units in hand and passing can make your opponent commit first. You can still respond after they act.
- **Last word wins.** The final Unit of a round often decides it, so try to keep something back.
- **Leaders are strong but exposed:** many cards target "the Opposing Leader".
- **Standby is a bank.** Units there survive Clean-up, so you start next round with something already waiting.
- **Watch the races.** Many cards boost or punish a race, and tied Duels follow the cycle: Space Cowboys ▶ Necrolites ▶ Overmind ▶ Blaarg ▶ Synthenoids ▶ Space Cowboys.
- **Read the log** when something surprises you; it says what happened and why.
- **Start on Easy**, then try Normal and Hard: the AI simulates its options before each turn.

---

## 8. Settings at the table

| Setting | Does |
|---|---|
| **Sound**, **Music** | Turn effects and the music bed on or off; sliders set the volume. |
| **Reduce effects** | Tones down flashes, shake, lightning and camera moves. |
| **High / Low quality** | Low skips the heavier visual layers, for older PCs. |
| **Tilt**, **Zoom**, **Speed** | The board's angle, size and animation speed. |
| **MENU** | Back to the main menu (this ends the game). |

## Keyboard
| Key | Does |
|---|---|
| **Space** | Pass, or "no response" |
| **Enter** | Confirm a choice, or issue the selected Action |
| **Esc** | Close the enlarged card, then cancel your selection |

---

**Next:** [Playing online](PLAYING-ONLINE.md) · [Playing on your home network](LAN-GUIDE.md) · [Rulebook](RULEBOOK.md) · [Glossary](GLOSSARY.md)

*Cosmic Crusade game design, rules, theme and card content © 2026 Gavin Nicholls. All rights reserved. See LICENSE.*
