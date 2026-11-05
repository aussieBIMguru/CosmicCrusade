# Cosmic Crusade: Engine Rules Spec (v1.0)

Source: *Rule_Book.docx* plus the designer's rulings (Section 9). This is the rulebook rewritten as rules an engine can follow.

Scope for v1: **2 players only, human vs AI, base game.** The Warzones expansion is an optional module (Section 8). Hybrids comes later, but the data model already allows a card to have more than one race.

---

## 1. Terminology (canonical meanings used by the engine)

| Term | Engine meaning |
|---|---|
| **Round** | One full cycle: Start-up → Main (turns alternate until everyone passes in a row) → End of Main (the round resolves) → Clean-up. |
| **Turn** | One player's go: a single action (or a Pass) in the Main Phase. "Turn" on cards means exactly this. |
| **Deploy** | Announce a Unit going to a target node, either from hand or by advancing it from Standby. This passes priority (Section 7.2). While being deployed, the Unit has **no abilities**. |
| **Played** | A deploy that survives all responses. The Unit enters the node and its "When you play" triggers fire. This applies to Standby advances too. |
| **Issue** | Announce an Order from hand. This passes priority. Actions and Reacts then resolve; Ongoing Orders go to an Order Node. |
| **In play** | The card is on a Unit Node, Leader Node or Order Node. Units in **Standby are not in play**: static effects don't buff them and general effects can't target them. Only effects that explicitly mention Standby affect them. |
| **Nullify** | Unit → Nullified pile; Order → Issued pile. |
| **Retire** | Unit → Retired pile; Order → Issued pile. |
| **Lock / Unlock** | A node flag that can apply to **any node on either side**: Unit, Leader, Order or Standby. Nothing can be played to a Locked node. A Locked node does **not** count as Empty. |
| **Empty** | A node with no card that is not Locked. |
| **Opposed** | The node directly across the control line holds a card. |
| **Allied** | Has the same controller. |
| **Manipulate N** | The player chooses **+N or −N** and applies it to **any Unit in play on either side**. It persists while that Unit stays in play (physically tracked with a Rank Modification card). Potency Tank increases N by 2. |
| **Suppress** | The listed abilities or effects on the target are ignored. |
| **Immune** | The card is unaffected by other effects or abilities, positive or negative, including static ones. Game-state events such as Clean-up still apply to it. |
| **Duel** | See Section 6. |
| **Assemble** | A **Main Phase action that uses your turn** (priority passes). Retire a card with Assemble from your hand and add its Rank to an allied Synthenoid in play. The added Rank lasts while that Unit stays in play. Cybernetic Enhancements also allows non-Synthenoid targets. |
| **Controller** | **A Unit's controller is the player whose side it sits on.** If a Unit moves across the control line (One of Us, Y'nosh, Arganium, The Creeper, Visiting Diplomat, The Oculus), control and Rank scoring go with the side. |

## 2. Races and weaknesses

Weakness cycle (A ▶ B means A wins ties against B):

`Space Cowboy ▶ Necrolite ▶ Overmind ▶ Blaarg ▶ Synthenoid ▶ Space Cowboy`

Galactic Council is neutral: it neither beats nor loses to any race.

Race is a **mutable set** on each card instance. Effects can change it ("becomes a Necrolite") or remove it (The Voidslime). Hybrids cards will have two races.

## 3. Card types

**Units** have a Name, Race(s), Base Rank, Class, Ability text and Story text.

| Class | Allowed nodes |
|---|---|
| Bystander | Unit Nodes. Visiting Diplomat may go on any Unit Node on either side. |
| Underling | Unit Nodes |
| Minion | Unit Nodes |
| Brute | Unit Nodes (vanilla Rank 4, no abilities) |
| Champion | Unit Nodes **or** Leader Node. The Leader Node holds one Leader *or* one Champion. |
| Leader (`*Leader*`) | Leader Node only |

**Orders** have a Name, Race, Subtype and Effect text.

| Subtype | When it can be issued | Where it goes |
|---|---|---|
| Action | On your own turn | Resolves → Issued pile |
| React | Any time its condition is met, in a React window | Resolves → Issued pile |
| Ongoing | On your own turn | An Empty Order Node; stays until Clean-up → Issued pile |

**Warzone** is an expansion card that behaves like a global Ongoing Order.

The **Locked Node** and **Rank Modification** cards are physical markers. The engine stores them as node flags and rank modifiers, not as cards.

## 4. Board and zones

**Shared:** Unit Deck, Order Deck, Retired Units pile, Nullified Units pile, Issued Orders pile. In the expansion, also the Warzone Deck and Active Warzone.

> **Confirmed:** the decks are **common to both players**. A "deck configuration" in the app sets the shared card pool (copies of each card). There are no per-player decks.

**Per player:**
- Hand: hidden from the opponent.
- 3 × Unit Node (U1–U3) and 1 × Leader Node (L).
- 3 × Standby Node (S1–S3). Si sits behind Ui and moves forward into it.
- 2 × Order Node (O1, O2).

**Opposition:** your Ui opposes the opponent's Ui, and your L opposes their L.

A player can only place cards on their own side unless an effect says otherwise. Control follows the side (see **Controller** in Section 1). Ace, Council Tactician is the exception: it can't move Units across sides (Ace errata).

**Hidden information:** hands are hidden unless The All-knowing, Transparency or Oracle's Arch reveal them. The AI only sees what a human in its seat could see.

## 5. Round sequence

### 5.1 Start-up
1. Each player **must** draw until they hold 5 Units and 3 Orders. A player who is already at or over a limit draws none of that type.
2. Effects carried over from the previous round resolve.
3. *(Warzones)* Reveal the top Warzone and make it Active.
4. First player: the winner of the previous round. In round 1, choose randomly. After a round with no winner, the previous round's first player goes first again.

**Drawing:** when an effect lets you draw N cards, you choose the deck for each card. "Lose your hand and draw that many" keeps the same split of Units and Orders.

**Empty deck:** a game option chosen at setup:
- (a) Reshuffle the matching pile into the deck (default).
- (b) The game ends after the current round.
- (c) No refill; play out your hands.

### 5.2 Main Phase
Players alternate turns. On a turn, the player does exactly one of these:
- Deploy a Unit from hand.
- Issue an Action or Ongoing Order from hand.
- Advance a Unit from Standby Si into an Empty Ui. This is a Deploy: priority passes, and if it survives, the Unit is Played and its on-play triggers fire.
- Assemble a card from hand.
- Deploy a Unit from a pile when a card allows it (Barkeep Bill, Galactomancer, Hills of Decay).
- Take another action that a card allows (e.g. Abominations "counts as a Turn").
- Pass.

The Main Phase ends when **both players pass in a row**. A pass is not permanent: if your opponent acts after you pass, you can act again.

### 5.3 End of Main Phase
1. End of Main triggers resolve. When several fire at once, the first player's triggers resolve first and each player chooses the order of their own. Rank modifiers resolve before Duels (High Noon errata).
2. Each player totals the Rank of their Units on all their **Unit Nodes and their Leader Node**.
3. The higher total wins the round, unless an effect says otherwise.
4. **If the totals tie:** compare each opposed pair of nodes (U1–U3 and L). The player whose unit has the higher Rank wins that pair; an empty node loses to an occupied one. Whoever wins more pairs wins the round. If that is also tied, nobody wins the round.

### 5.4 Clean-up (in this order)
1. Clean-up triggers resolve. Y'nosh has priority over other Unit Clean-up abilities.
2. Units on nodes are Retired.
3. Ongoing Orders are moved to the Issued pile.
4. All nodes are Unlocked.
5. *(Warzones)* Active Warzones go to the bottom of the Warzone deck in random order.
6. The round ends.

### 5.5 Game end
The first player to win **N rounds** wins the game. N is a game option (default 3). The empty-deck option "(b) game ends after the current round" also ends the game; the player with more round wins then wins, and an equal count is a draw.

## 6. Duel
1. Compare the current Ranks of the two units.
2. The lower Rank is Nullified (subject to prevention, e.g. Crimson Redeemer).
3. On a tie, check weaknesses using **current** races. If one unit beats the other, the weaker unit is Nullified. Otherwise nothing happens.

When one effect starts several Duels (e.g. High Noon), they resolve **simultaneously**: all Ranks are compared first, then every loser is Nullified at once. "Respond in Kind" works the same way.

## 7. Resolution model

### 7.1 Deploy and Issue pipeline
```
Announce (card leaves hand, target chosen)
  → Priority passes to the opponent; responses stack (Section 7.2)
  → if not cancelled: Played / Resolved
  → "When played" triggers
  → State-based checks
```
- A card being Deployed has no abilities, so it can be Nullified even if its text says it can't be (Shadow Strike / Target Practice errata).
- A cancelled card is **not** Played, and its on-play triggers don't fire.
- **Psychic Coercion:** the deploying player declares "I'm deploying a Unit", the opponent picks the node, and then the deploying player picks which Unit. The engine's decision order must support this split.
- **Mr Morph / Forced Mitosis:** the card is Deployed as written but Played as the chosen Unit. Copy effects are needed.
- **Mechatron:** cannot be Deployed unless it can be Played. The engine needs a "can this legally be played" check.

### 7.2 Priority and React chains (MTG Arena style)
- **After every action** (Deploy, Standby advance, Issue, a React, or a triggered effect going on the stack), **priority passes to the other player**. They either respond with a React whose condition is met, or confirm "OK, no response".
- Responses go on a **stack** and resolve last-in, first-out. Priority keeps alternating. The top of the stack resolves once both players pass priority in a row, and then priority passes again.
- Either player can React to anything whose condition is met, including their own cards. Reacts don't use a turn.
- **Auto-allow mode:** each player can toggle whether to auto-pass priority so they aren't prompted constantly. A useful default is to prompt only when the player holds a React whose condition is currently met. The AI controller answers priority requests directly and never needs the toggle.

### 7.3 Effect categories the engine must support
- **Triggered:** OnPlay, OnLeavePlay, OnCardPlayed (any card), StartUp, EndOfMain, CleanUp.
- **Static or continuous:** rank modifiers, conditional modifiers, rule changes (e.g. "you choose their node").
- **Replacement:** "if X would happen, instead…" (Already Dead, Nullify prevention).
- **Restrictions:** cannot be Nullified, cannot be Deployed, Immune, Suppress.
- **Once-per-round latches:** Xeno Laser stays in play but can't trigger again this round.

### 7.4 Rank calculation (proposed layers)
1. Base Rank from the card.
2. Base-rank overrides ("Rank becomes 1"). Keeper of Sanctions / Corporate Detainer apply on top of overrides and persist.
3. Static modifiers (±) from cards in play, recalculated live.
4. Manipulate counters and Assemble additions (persistent).
5. **There is no general floor.** Rank can go negative. Only cards that say so (e.g. Acid Rain) floor it at 0.

Immune units skip steps 2–4 for anything that comes from other cards.

## 8. Warzones expansion (optional module)
- Adds a Warzone Deck and an Active Warzone slot.
- Adds 15 Orders to the Order Deck.
- Start-up: reveal one Warzone; it applies immediately.
- Clean-up: Active Warzones go to the bottom of the deck in random order. If a Warzone is Retired or Nullified, it goes to the bottom of the deck as well.

## 9. Designer decisions (5 Nov 2026)
| # | Topic | Decision |
|---|---|---|
| 1 | Turn vs Round | A turn is one player's go (one action). A round lasts until everyone passes and the round resolves. The rulebook's "at the end of each turn during Clean-up" means once per round, because Clean-up only happens once a round. |
| 2 | Game win | First to N round wins. |
| 3 | Leader Node in Rank total | Counts. |
| 4 | Decks | Shared pool for both players. |
| 5 | Placement | Leaders go on the Leader Node only; Champions on a Unit or Leader Node; everything else on Unit Nodes. |
| 6 | Passing | A pass isn't final; the Main Phase ends when both players pass in a row. |
| 7 | Tied round | Most opposed node pairs won; if still tied, no score. |
| 8 | Standby | Advancing is a Deploy and then a Play (priority passes, on-play triggers fire). Standby units are not in play. |
| 9 | Reacts | MTG Arena-style priority after every action, with a stack and an auto-allow toggle. |
| 10 | Start-up draw | Mandatory. |
| 11 | Rank floor | No floor, except cards that state one (Acid Rain). |
| 12 | Simultaneous triggers | First player's triggers resolve first; each player orders their own. |
| 13 | Multiple Duels | Resolve simultaneously. |
| 14 | Lock targets | Any node on either side. |

### Card rulings (5 Nov 2026, from transcribing the full card set)
| # | Topic | Decision |
|---|---|---|
| 15 | Units on an opponent's node | Score for the side they sit on; control follows the side. |
| 16 | Arganium | Can swap across sides; control swaps with the side. |
| 17 | Manipulate N | ±N, player's choice, on any Unit in play on either side. |
| 18 | Assemble | A Main Phase action that uses your turn. |
| 19 | Terraform | The controller may move the Unit to Standby; otherwise it's Nullified before the Lock. |
| 20 | TR-1993.r | Re-runs the on-play abilities of any chosen Units in play, each as its controller. |
| 21 | Xeno Laser | Hits after the Unit is Played, so its on-play abilities fire first. |
| 22 | Senate Chambers | Checked immediately; the round ends at once. |
| 23 | Military Academy | Counts Unit deploys only (from any zone). |
| 24 | Mr Morph / Forced Mitosis | Copy a Unit in play. |
| 25 | Visiting Diplomat | Any Unit Node on either side (not Leader Nodes). |
| 26 | Card name typos | Fixed in the app; the printed name is kept as `printedName`. |
| 27 | Sheriff's Badge | +1 for non-Space Cowboys is correct. |
| 28 | High Noon | Leader Nodes duel too. |
| 29 | Mind Games | The opponent must Deploy to one of their own opposed nodes if able. |

### Final confirmations (5 Nov 2026)
| # | Topic | Decision |
|---|---|---|
| 30 | Rounds to win | Default is first to 3. |
| 31 | No-winner round | The previous first player goes first again. |
| 32 | Tiebreak, empty node | An occupied node always beats an empty one. |
| 33 | Blaarg signature Action | Adapt (5 copies). |
| 34 | No Leaders mode | Removes all five leader-related cards. |
| 35 | Random Draw | Equal odds per distinct card. |

There are no open rules questions.

## 10. Pre-game setup and modes
These settings are stored in `game-modes.json`; the default copy counts are in `pools/standard.json`. Every count can be edited per card in the setup screen.

**Default copy counts (Standard pool):**
| Cards | Copies |
|---|---|
| Leaders | 1 |
| Champions | 3 |
| Other Units (Underling, Minion, Brute, Bystander) | 2 |
| Visiting Diplomat | 5 |
| Actions, Reacts, Ongoings | 1 |
| Signature Actions (Blasters at Dawn, Shallow Grave, Meditate, Adapt, Salvage for Parts) | 5 |
| *Warzones:* each Warzone | 1 |
| *Warzones:* Black Hole, Uncharted Space | 5 |
| *Warzones:* Dimensional Rift, Wormhole | 5 |
| *Warzones:* Atmosphere Generator | 2 |

Totals: base game 150 Units and 80 Orders; Warzones adds 35 Warzones (matching the rulebook) and 12 Orders.

**Options:**
- **Rounds to win:** default 3.
- **Empty deck:** Reshuffle (default), End after round, or No refill.
- **Warzones:** on or off (off by default).
- **No Leaders:** removes Leader-class Units, both Leader Nodes, and the leader-related cards (Depose, Tyrant Toppler, Rise of the Tyrant, The Campaign Trail, and No Gods, No Masters, whose empty-Leader-Node condition would always be true). Champions stay and use Unit Nodes. Senate Chambers then means filling all 3 Unit Nodes. Opposed-pair tiebreaks use U1–U3 only.
- **Deck mode:**
  - **Standard:** use the pool counts.
  - **True Singleton:** every enabled card ×1.
  - **Random Draw:** virtual decks. Each draw picks a random enabled card of that deck's type (uniform per card), so decks never run out and the empty-deck option doesn't apply. Piles still fill normally for pile-based effects.
