# Cosmic Crusade: Rulebook

Cosmic Crusade is a card game of galactic war for **two players**. Each round, both players deploy Units onto a battlefield of nodes and issue Orders to change the fight. When both players have passed, the side with the higher **total Rank** on the battlefield wins the round. The first player to win **3 rounds** (or whatever was agreed) wins the game.

This rulebook covers the base game and the optional modules: Warzones, Archons and Spacecraft. New words are explained in the [Glossary](GLOSSARY.md). If you just want to start playing in the app, read [How to play](HOW-TO-PLAY.md) first; the app enforces every rule here for you.

---

## 1. The cards

### Units
Units fight on the battlefield. Every Unit has:
- **Name** and **Race(s)**. A Unit's race can change during play ("becomes a Necrolite"), or be removed altogether.
- **Base Rank**: its strength as printed. Effects then raise or lower it, and the result is its **Rank**.
- **Class**: decides where it may be placed.
- **Ability text**: what it does. Story text in italics is flavour only.

| Class | Where it can go | Notes |
|---|---|---|
| **Leader** | Your Leader Node only | Usually the strongest abilities. A Leader can **never** go into a Standby Zone. |
| **Champion** | A Unit Node **or** your Leader Node | Usually boosts its own race. Can go into Standby, but not from the Leader Node. |
| **Brute** | Unit Nodes | Rank 4, no ability. |
| **Minion** | Unit Nodes | The backbone of most armies. |
| **Underling** | Unit Nodes | Cheap, with useful tricks. |
| **Bystander** | Unit Nodes | Visiting Diplomat may go on any Unit Node, even your opponent's. |

### Orders
Orders are played ("issued") for an effect. Every Order has a race and a type:

| Type | When you can issue it | What happens to it |
|---|---|---|
| **Action** | On your turn | Takes effect, then goes to the Issued pile |
| **React** | Any time its condition happens, on either player's turn | Takes effect, then goes to the Issued pile. Doesn't use your turn. |
| **Ongoing** | On your turn | Sits on one of your Order Nodes and keeps working until the end of the round |

---

## 2. The races

| Race | Plays like | Beats in a tie |
|---|---|---|
| **Space Cowboys** | Gunslingers: Duels, shoot-outs and outlaw tricks | Necrolites |
| **Necrolites** | The undead: they come back from the Nullified pile and spread decay | Overmind |
| **Overmind** | Psionic minds: they read hands, choose for you and take control | Blaarg |
| **Blaarg** | Living slime: they Manipulate Ranks and seep into everything | Synthenoids |
| **Synthenoids** | Machines: they **Assemble** spare parts into bigger robots | Space Cowboys |
| **Galactic Council** | Politicians and their shadows: Nullify, sanctions and control | Nobody (neutral) |

The five warring races form a **weakness cycle**:

```
Space Cowboys ▶ Necrolites ▶ Overmind ▶ Blaarg ▶ Synthenoids ▶ Space Cowboys
```

The cycle only matters when a **Duel is tied** (section 7). The Galactic Council is neutral. So are **Archons** (Archons module) and **Neutral** cards.

---

## 3. The battlefield

Each player has their own side of the board:

```
      Opponent:   [O1][O2]      [S1][S2][S3]
                        [U1][U2][U3]   [L]
   ─────────────────── control line ───────────────────
                        [U1][U2][U3]   [L]
      You:        [O1][O2]      [S1][S2][S3]
```

| Node | Holds |
|---|---|
| **Unit Nodes** U1–U3 | One Unit each |
| **Leader Node** L | One Leader **or** one Champion |
| **Standby Nodes** S1–S3 | One Unit each, waiting. S1 sits behind U1, and so on. |
| **Order Nodes** O1–O2 | One Ongoing Order each |

- Your U1 **opposes** your opponent's U1, and so on; the two Leader Nodes oppose each other. A node is **Opposed** when the node directly across holds a card.
- **Standby is not "in play".** Units waiting there don't score and aren't affected by effects unless the effect mentions Standby.
- A node can be **Locked**. Nothing can be played to a Locked node, and a Locked node doesn't count as Empty. Every node is unlocked at the end of the round.
- Normally you only place cards on your own side. A Unit placed on your opponent's side by an effect belongs to them while it's there: it scores for them and they control it.

### Shared decks and piles
Both players draw from **the same two decks**: the **Unit deck** and the **Order deck**. Used cards go to three shared piles:

| Pile | What goes there |
|---|---|
| **Retired** | Units removed normally (e.g. at the end of each round) |
| **Nullified** | Units that were destroyed (Nullified) |
| **Issued** | Every Order once it's done: Actions and Reacts after they resolve, Ongoings at the end of the round, and Nullified or Retired Orders |

Some cards can bring cards back from these piles.

---

## 4. Setting up a game

1. Shuffle the Unit deck and the Order deck.
2. Agree how many round wins you need to win the game. The default is **3**.
3. Decide what happens **when a deck runs out**:
   - **Reshuffle** (default): shuffle the matching pile back in to make a new deck;
   - **End after round**: the game ends after the current round, and whoever has won more rounds wins;
   - **No refill**: play on with what's in your hands.
4. Choose who goes first in round 1 at random.

---

## 5. A round

Each round has four steps.

### 5.1 Start-up
1. **Draw up.** Each player **must** draw until they hold **5 Units and 3 Orders**. If you already have that many (or more) of a type, you draw none of it.
2. Any effects that carry over from the last round happen now.
3. *(Warzones)* Reveal the top Warzone; it applies at once.
4. **First player:** whoever won the last round goes first. If the last round had no winner, the same player as last round goes first again.

### 5.2 Main Phase
Players take **turns**, starting with the first player. On your turn you do **exactly one** of these:

- **Deploy** a Unit from your hand onto a legal node.
- **Issue** an Action, or an Ongoing onto an empty Order Node.
- **Advance** a Unit from a Standby Node into the empty Unit Node in front of it. This counts as a Deploy: it can be answered, and its "When you play" ability fires.
- **Assemble** (Synthenoids): retire a card with Assemble from your hand to add its Rank to one of your Synthenoids in play.
- Take an action a card gives you, such as deploying from a pile with Barkeep Bill or Galactomancer, or Abominations.
- **Pass.**

The Main Phase ends when **both players pass one after the other**. Passing isn't final: if your opponent does something after you pass, you get to act again.

### 5.3 Scoring the round
1. "At the end of the Main Phase" effects happen. When both players have some, the first player's go first, and each player's own happen in board order (Leader Node, then U1–U3, then Order Nodes). Rank changes happen before Duels.
2. Each player adds up the **Rank of every Unit on their Unit Nodes and Leader Node**. Standby doesn't count.
3. **The higher total wins the round.**
4. **If the totals are tied**, compare each opposed pair (U1, U2, U3 and L). The higher Rank wins the pair, and any Unit beats an empty node. Whoever wins **more pairs** wins the round. If that's tied too, nobody wins the round.

### 5.4 Clean-up
1. "During Clean-up" abilities happen. Y'nosh, Galactic Slaver goes before any other.
2. Every Unit on a Unit Node or Leader Node is **Retired**. Units in Standby stay for next round.
3. Ongoing Orders go to the Issued pile.
4. Every node is unlocked.
5. *(Archons)* An Archon taken this round but not played is lost.
6. *(Warzones)* The active Warzone goes to the bottom of the Warzone deck.

### 5.5 Winning
The first player to win the agreed number of rounds wins the game. If the game ends because a deck ran out ("End after round"), whoever has more round wins is the winner, and an equal number is a draw.

---

## 6. Deploying, issuing and responding

**Every** Deploy, Advance, Issue and React gives your opponent the chance to respond before it takes effect.

1. **Announce it.** Say which card and where it's going; the card leaves your hand.
2. **Responses.** Your opponent may answer with a React whose condition has just happened (for example "as a Unit is being Deployed"). You may then answer their React with one of yours, and so on. Responses resolve **last one first**. Either player can React, including to their own cards. Reacts don't use a turn.
3. **Resolve.** If nothing stopped it, the card takes effect:
   - a Unit is **Played**: it enters its node and its "When you play" ability fires;
   - an Action resolves;
   - an Ongoing lands on its Order Node.

Things to know:
- **A Unit being Deployed has no abilities yet.** So a Unit that "cannot be Nullified" can still be shot down by Shadow Strike or Target Practice while it's on its way in.
- **A cancelled card isn't Played**, so its "When you play" ability doesn't fire.
- Most choices (which Unit to Nullify, which node) are made **when the effect resolves**, not when the card is played.

---

## 7. Duels

When two Units **Duel**:
1. Compare their current Ranks. **The lower Rank is Nullified.**
2. **If the Ranks are tied**, check the weakness cycle using their current races. If one beats the other, the weaker Unit is Nullified. If neither beats the other (or each beats the other), **both are Nullified**.

When one card starts several Duels at once (High Noon, Dogfight), they all happen together: every Rank is compared first, then all the losers are Nullified at the same moment.

---

## 8. Rank

A Unit's Rank is worked out from its Base Rank as follows:
1. Start with its **Base Rank** as printed.
2. Apply anything that **sets** its Base Rank ("its Base Rank becomes 1").
3. Add or subtract every **ongoing modifier** from cards in play ("Space Cowboy Units get +1 Rank").
4. Add any **lasting changes** it has picked up: Manipulate, Assemble, or "this Unit gets +2 Rank" from an ability.

**There is no minimum**: Rank can go below 0, unless a card says otherwise (Acid Rain stops at 0).

- A Unit's lasting changes are lost when it leaves play.
- An **Immune** Unit ignores every effect and ability except its own, good or bad. Game rules such as Clean-up still apply to it. If another card made it Immune (Deify), that card's own bonus still applies.

---

## 9. Key actions

| Action | Meaning |
|---|---|
| **Nullify** | Destroy: a Unit goes to the Nullified pile; an Order goes to the Issued pile. |
| **Retire** | Remove normally: a Unit goes to the Retired pile; an Order goes to the Issued pile. |
| **Lock** / **Unlock** | Lock a node so nothing can be played to it (any node, either side) / free it again. |
| **Manipulate N** | Give **any** Unit in play, on either side, **+N or −N** Rank (your choice). It lasts while that Unit stays in play. |
| **Suppress** | The named abilities or effects are ignored. |
| **Assemble** | Retire the card from your hand to add its Rank to one of your Synthenoids in play. This uses your turn. |
| **Draw** | Take cards from the decks. For each card, you choose which deck to draw from. |

---

## 10. Rules worth remembering

These come up in play. The app handles them all.

- **Leaders never go into Standby**, for any reason. An effect that would put one there can't choose it, and if it would happen anyway, nothing happens to the Leader.
- **Nothing in the Leader slot goes into Standby either**, so a Champion on your Leader Node stays put. Champions on Unit Nodes can go into Standby as normal.
- **Control follows the side.** A Unit on your side is yours: it scores for you and you control it, even if it started as your opponent's (One of Us, Y'nosh, Arganium, The Creeper, Visiting Diplomat).
- **"Owner" on a card means the controller**: whoever's side the Unit is on.
- **Copies** (Mr Morph, Forced Mitosis, Androsiar) take the copied card's printed Rank, class and ability, not its current changes. A copy turns back into itself when it leaves play.
- **Xeno Laser** hits a Unit just after it's Played, so the Unit's "When you play" ability still happens first.
- **Senate Chambers** (Warzone) and **Insurrection** (Archon) win the round **immediately** when their condition is met.
- **The Oculus** (Warzone) turns everything around: the lower total wins, and in a tie, winning fewer pairs wins.
- **Polarity Storm** (Warzone) flips every Rank change on a Unit, including lasting ones.
- **Black Hole** (Warzone) Nullifies any Unit that would be Retired or put into Standby. If that Nullify is prevented, the Unit is Retired or moved after all.

---

## 11. Optional: Warzones

A Warzone is the battlefield's environment, and it affects both players.
- Add the **Warzone deck**: 25 battlefields, plus 5 copies each of *Uncharted Space* (no effect) and *Black Hole*.
- Add 12 extra Orders to the Order deck: 5 *Wormhole*, 5 *Dimensional Rift* and 2 *Atmosphere Generator*. With these you can swap, stack or switch off Warzones.
- At each round's Start-up, reveal the top Warzone; it applies straight away. At Clean-up it goes to the bottom of the deck.

Examples:
- *Planet Earth*: Space Cowboy Units get +1 Rank.
- *Training Grounds*: Underlings get +2 Rank.
- *Oracle's Arch*: everyone plays with their hand revealed.
- *Military Academy*: no more than 3 Deploy turns each.

---

## 12. Optional: Archons

Archons are 12 ancient power cards in a separate, shared **Archon deck**. Their race is Archon, which is neutral.

1. **Once per game**, at a round's Start-up, before drawing, you may take your **Archon draw**:
   - draw one card fewer that round (you choose Unit or Order);
   - look at 3 random Archons, keep 1, and shuffle the other 2 back.

   The first player decides first.
2. Your Archon is **revealed** to your opponent.
3. You must play it **that round**, on your turn. Playing it uses your turn, just like playing a card from your hand. If you don't, it's lost at Clean-up and your once-per-game draw is spent.
4. Archons never join your hand or the normal piles. Whenever one leaves play, it goes back into the Archon deck.

The Archons:
- **Leaders:** The Unnameable (Rank 10: your other Units count as 0 towards your total) and Androsiar (Rank 4: any Unit may enter as a copy of it).
- **Orders:** Nebula Cannon, Ceasefire, Archon Arsenal, Unstoppable, Galactic Siege, Insurrection, Open Source, Deify, Quantum Leap and Planetscape.

---

## 13. Optional: Spacecraft

Spacecraft add a second, airborne layer above the battlefield.

- **Ships are Units.** They're mixed into the Unit deck, they score, and they can be Retired, Nullified or sent to Standby like any Unit. Leader and Champion ships are placed like other Leaders and Champions.
- **Crewing.** One ship and one Unit can share a node:
  - deploy a Unit into your empty ship; or
  - deploy a ship onto a node where your Unit already stands.

  Only the card being deployed is Played. "When this becomes crewed" abilities fire however the crew arrives.
- **Scoring:** a crewed node scores the ship's Rank **plus** its crew's Rank, both for the round total and for the tiebreak.
- **Elevation.** A crew is up in the air.
  - Ground effects that hit "the Opposing Unit", and Duels started from the ground, meet the **ship** instead.
  - Effects that choose "any Unit" can still pick the crew.
  - A crewed ship's own effects can hit either level. When an effect could hit the ship or its crew, the player using it chooses which.
- **Duels** against a crewed node use the **ship's Rank alone**. In a tie, the pair counts as having both cards' races.
- **If a ship leaves play**, its crew drops to the ground and stays. If the crew leaves, the ship stays and can be crewed again.
- **Fighters** made by the Mothership are tokens: they vanish whenever they leave play.
- With Warzones also on, 5 spacecraft Warzones join the Warzone deck:
  - *Asteroid Belt*: spacecraft get −1 Rank;
  - *Ion Storm*: spacecraft have no abilities;
  - *Vacuum*: ground Units get −2 Rank;
  - *Wreckfield*: Nullified ships can be deployed;
  - *Gravity Well*: no elevation.

---

## 14. Game modes

| Mode | What changes |
|---|---|
| **No Leaders** | Leader Units, both Leader Nodes and the cards that need Leaders are removed: Depose, Tyrant Toppler, Rise of the Tyrant, The Campaign Trail and No Gods, No Masters (with Archons, also The Unnameable, Androsiar, Ceasefire and Deify). Champions stay and use Unit Nodes. Ties compare U1–U3 only, and Senate Chambers needs all 3 Unit Nodes filled. |
| **True Singleton** | One copy of every card. |
| **Random Draw** | No real decks: every draw is a random card of that type, so decks never run out. |
| **Card pool** | The number of copies of each card can be changed before the game. Default: Leaders 1, Champions 3, other Units 2, Visiting Diplomat 5, Orders 1, and 5 each of the five signature Actions (Blasters at Dawn, Shallow Grave, Meditate, Adapt, Salvage for Parts). |

---

*Cosmic Crusade game design, rules, theme and card content © 2026 Gavin Nicholls. All rights reserved. See LICENSE.*
