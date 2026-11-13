# Cosmic Crusade: Spacecraft (optional module)

Status: **designed and implemented, not yet compiled** (7 Nov 2026; code drop `CosmicCrusade-Spacecraft-code.zip`, data `CosmicCrusade-Data-Spacecraft.zip`). The card list is Gavin's; the rulings S1–S28 are Gavin's answers to staged questions (7 Nov 2026). The module is **off by default** (`game-modes.json` `defaults.spacecraft = false`, a setup check box like Warzones and Archons). The 48 new entries are in `cards.json` (set `"Spacecraft"`), and the copy counts are in `pools/standard.json` under `spacecraft` and `spacecraftWarzones`.

## Concept (Gavin's brief)
Spacecraft form a second, elevated layer over the battlefield. A ship either lifts a ground Unit up (the Unit boards it) or is laid first and a Unit later lands in it, much like bestow in MTG. A ship has its own Rank and abilities. When crewed, the ship's Rank adds to the crew's, and both cards' abilities apply. Ground effects that hit the Unit in front of them miss an elevated crew, while a crewed Unit can hit at either level. Ships score, go to Standby, Retire and are Nullified like Units. If a ship is Nullified, its crew drops back to the ground unless the effect says both are destroyed. Leader and Champion ships are placed like their ground counterparts.

## Rulings (Gavin, 7 Nov 2026)
| # | Topic | Ruling |
|---|---|---|
| S1 | Boarding order | Both ways: a Unit can be Deployed into your empty ship on a Node, or a ship can be Deployed onto a Node where your Unit already stands. |
| S2 | Played | Only the card being Deployed is Played (its "When you play" fires). The card already on the Node isn't Played again. "When this becomes crewed" abilities still fire. |
| S3 | Capacity | At most one ship and one crewing Unit per Node. |
| S4 | Elevation | Ground effects that hit "the Opposing Unit" and Duels started from the ground miss a crewed (elevated) Unit. They can still hit the ship. Effects that choose any Unit can still hit the crew. |
| S5 | Duel Rank | A Duel against a crewed Node compares the **ship's Rank alone**. |
| S6 | Hitting down | A crewed ship hits both levels: its effects can reach an uncrewed ground Unit in front of it. |
| S7 | Race | Each card keeps its own races. Race buffs apply per card. In a Duel tie, the crewed pair counts as having both races. |
| S8 | Choosing the target | When a Nullify (or similar) could hit either the ship or its crew, **the controller of the effect's source** chooses between the valid targets. |
| S9 | Clean-up | Each card follows its own rules: the ship Retires, and a crew with a Standby-instead ability (Lone Wolf etc.) goes to Standby alone. A ship only stays if it has its own ability. |
| S10 | Standby | Ships can sit on Standby Nodes and advance like Units. A crewed pair in Standby advances together. **Leader ships never go to Standby** (ruling #36 applies). |
| S11 | Crew class | Follows the Node. On the Leader Node only a Leader or Champion may crew. On a Unit Node, a Champion ship takes any Unit allowed there. |
| S12 | Crew leaves | If the crew leaves play, the ship stays uncrewed, keeps scoring its own Rank and can be crewed again. |
| S13 | Fighters (Mothership) | Tokens (`0ST1`): created by the effect, not real cards. They vanish whenever they leave play and never go to a pile. |
| S14 | Vacuum | Non-spacecraft Units get **−2** Rank. |
| S15 | Dreadnought | Its Rank counts if a Leader **or Champion** crews it. |
| S16 | Earthrise | Counts every Space Cowboy card you control (Units and spacecraft), including its own crew but not Earthrise itself. |
| S17 | Dogfight | Every Node where both sides have a ship: those two ships Duel, all at once (ship Rank only, S5). |
| S18 | Self-Destruct | Can answer **any** spacecraft being Nullified, either side, as written. |
| S19 | Gravity Well | Elevation is off: ground Opposing-Unit effects and Duels reach ships and crews normally. |
| S20 | Wreckfield | Any player may Deploy a spacecraft from the Nullified pile on their turn, as if from hand (like Hills of Decay). |
| S21 | Ships are Units | Spacecraft count as Units for all base-game text (Acid Rain, Blazing Saddles, Planetscape, "X Units get +1"...). |
| S22 | Deck | Ships are mixed into the shared Unit deck and count toward the 5-Unit hand. Spacecraft Orders go in the Order deck. No new pile. |
| S23 | Scoring | A crewed Node counts ship + crew for the round total and for the opposed-pair tiebreak. |
| S24 | "When crewed" | Fires however crew arrives: Deploy, Beam Up, Mindscrambler swap, Abductor, Mothership. |
| S25 | No Leaders mode | Removes the 6 Leader ships (0SL1, 1SL1, 2SL1, 3SL1, 4SL1, 5SL1); they are Leader-class, so this is automatic. Champion ships stay and use Unit Nodes. |
| S26 | Copies | Leader ships 1, **Champion ships 2**, other ships 1, Orders 1, each Warzone 1. |
| S27 | Placeholder art | Starfield background, a generic ship silhouette in the faction colour, and the code and name in large text. |
| S28 | Distillator | Changed (7 Nov 2026): "Spacecraft your Opponents control get −1 Rank." |

### Readings assumed while encoding (confirm or change)
- **Dreadnought:** with no Leader/Champion crew it counts as 0 in the round total and the pair tiebreak; Duels still use its real Rank 4.
- **Research Vessel:** a continuous peek at the top card of each deck while crewed (not a one-off).
- **Abductor:** an on-play effect, only if Abductor is uncrewed; the abducted Unit crosses sides and control follows (ruling #15). Mandatory, as written.
- **Nuke from Orbit:** spacecraft themselves aren't hit; it Nullifies ground Units that aren't crewing.
- **Distillator:** "opposing" read as every spacecraft your Opponents control (Champion-style global effect), not just the one across.
- **Viscous Fog:** if a ship leaves play its crew still drops to the ground; everything else that moves a Unit in or out is blocked, including Deploys that would board.
- **Spore Pod:** Units move before the Lock (as Sisters of Blight); a Leader stays put but the Node is still Locked.
- **Exo-Frame:** Assembles the Rank it had as it left.
- **Refit:** not an additional cost (the Order can be issued with no spacecraft in hand; it then does nothing). X is the retired card's Base Rank.
- **Train of Thought:** "you may Draw 2 cards" (a yes/no, then the Unit/Order split), not "up to 2".
- **Remnant AI:** "cannot be crewed" already stops it being laid under a Unit, so the second clause was folded in.
- **Copies:** a spacecraft only copies spacecraft (Morphership), and a Unit only copies Units (Mr Morph, Forced Mitosis).
- **"The Unit on a node"** (Terraform, Sisters of Blight, Spore Pod) means the crew and the spacecraft, crew first. Effects that move one chosen card (Shallow Grave, One of Us, "place it in Standby") move only that card; a spacecraft leaving drops its crew to the ground. Ace's rearrangement and Arganium's switch move a spacecraft and its crew together.

## Cards
Ids follow the app's race digits (0 GC, 1 SC, 2 NE, 3 OV, 4 BL, 5 SY, 6 Neutral). `S` marks the module: SL1 = Leader ship, SL2 = Champion ship, SU = ship, SO = Order, ST = token.

| Code | Name | Type | Faction | Rank | Effect |
|---|---|---|---|---|---|
| 0SL1 | Dreadnought | Leader | Galactic Council | 4 | This spacecraft's Rank only counts if it is crewed by a Leader or Champion. |
| 0SL2 | Mothership | Champion | Galactic Council | 1 | When you play this spacecraft, put each ground Unit on your side without a spacecraft into a Rank 1 Galactic Council spacecraft called Fighter. |
| 0SU1 | Medical Frigate | Ship | Galactic Council | 2 | If this spacecraft's crew would be Nullified, you may place it in your Standby Zone instead. |
| 0SU2 | Research Vessel | Ship | Galactic Council | 2 | While this spacecraft is crewed, you may look at the top card of each deck. |
| 0SU3 | Requisitions Unit | Ship | Galactic Council | 1 | When you play this spacecraft, you may set an opposing spacecraft's Base Rank to 0. |
| 0SO1 | Decommission | Action | Galactic Council | – | Retire a spacecraft. |
| 0SO2 | Nuke from Orbit | Action | Galactic Council | – | If you control a Unit in a spacecraft, Nullify all ground Units not in a spacecraft. |
| 1SL1 | Earthrise | Leader | Space Cowboy | 1 | This spacecraft gets +2 Rank for each other Space Cowboy you control. |
| 1SL2 | High Roller | Champion | Space Cowboy | 0 | This spacecraft's Base Rank is equal to its crew's Base Rank. |
| 1SU1 | Remnant AI | Ship | Space Cowboy | 3 | This spacecraft cannot be crewed (so it can't be played onto a Unit either). |
| 1SU2 | Chrome Steed | Ship | Space Cowboy | 1 | When you play this spacecraft, you may move a Retired Unit to crew it. |
| 1SU3 | Blazing Saddle | Ship | Space Cowboy | 1 | If this spacecraft is crewed by a Space Cowboy, it gets +2 Rank. |
| 1SO1 | Greased Engines | Ongoing | Space Cowboy | – | Spacecraft you control get +1 Rank. |
| 1SO2 | Dogfight | Action | Space Cowboy | – | All opposed spacecraft Duel. |
| 2SL1 | Sarcophagar | Leader | Necrolite | 2 | When you play this spacecraft, you may crew it with a Unit from the Nullified pile. That Unit becomes a Necrolite. |
| 2SL2 | Ghost Ship | Champion | Necrolite | 1 | This spacecraft gets +1 Rank for each Nullified spacecraft. |
| 2SU1 | Hearse | Ship | Necrolite | 2 | When this spacecraft's crew is Nullified, you may Draw a card. |
| 2SU2 | Spore Pod | Ship | Necrolite | 1 | When this spacecraft leaves play, Lock its Node and the opposing Node. Units on those Nodes are placed in their controllers' Standby Zones. |
| 2SU3 | Scuttler | Ship | Necrolite | 2 | While this spacecraft is crewed, it gets +1 Rank whenever a Unit is Nullified. |
| 2SO1 | Shipwreck | Action | Necrolite | – | Place a Nullified spacecraft into your Standby Zone. |
| 2SO2 | Hull Rot | Ongoing | Necrolite | – | Crewed spacecraft get −1 Rank. Uncrewed spacecraft get −2 Rank. |
| 3SL1 | Train of Thought | Leader | Overmind | 3 | When this spacecraft becomes crewed, you may Draw 2 cards. |
| 3SL2 | Hive Carrier | Champion | Overmind | 2 | Crewed spacecraft you control get +1 Rank. |
| 3SU1 | Abductor | Ship | Overmind | 0 | When you play this spacecraft, if it is uncrewed and the Opposing Unit is a ground Unit, move that Unit onto this spacecraft. |
| 3SU2 | Mindscrambler | Ship | Overmind | 1 | When you play this spacecraft, you may switch the crews of any 2 spacecraft. |
| 3SU3 | Thought Courier | Ship | Overmind | 1 | When this spacecraft becomes crewed, you may Draw a card. |
| 3SO1 | Beam Up | Action | Overmind | – | Move one of your ground Units onto an uncrewed spacecraft you control. It isn't Played again. |
| 3SO2 | Hijack | React | Overmind | – | Gain control of a spacecraft as it is being Deployed. It is Deployed to one of your Empty Nodes instead. |
| 4SL1 | Morphership | Leader | Blaarg | 0 | This spacecraft may enter as a copy of another spacecraft, except it is a Blaarg. |
| 4SL2 | Distillator | Champion | Blaarg | 2 | Spacecraft your Opponents control get −1 Rank. |
| 4SU1 | Stagnant Station | Ship | Blaarg | 1 | You may play this spacecraft on an Opponent's Empty Unit Node. It cannot be crewed. |
| 4SU2 | Goo Bubble | Ship | Blaarg | 2 | This spacecraft's crew cannot be Nullified, but has no abilities. |
| 4SU3 | Gelatinous Hull | Ship | Blaarg | 1 | When this spacecraft becomes crewed, Manipulate 1. If its crew is a Blaarg, Manipulate 2 instead. |
| 4SO1 | Engulf | Action | Blaarg | – | Nullify a spacecraft. Its crew stays on the Node and becomes a Blaarg. |
| 4SO2 | Viscous Fog | Ongoing | Blaarg | – | Units can't board or leave spacecraft. |
| 5SL1 | The Crucible | Leader | Synthenoid | 2 | While this spacecraft is crewed, Rank you Assemble is doubled. |
| 5SL2 | Drone Hub | Champion | Synthenoid | 2 | Uncrewed spacecraft you control get +2 Rank. |
| 5SU1 | Exo-Frame | Ship | Synthenoid | 2 | If this spacecraft leaves play, Assemble its Rank onto its crew. |
| 5SU2 | Salvage Tug | Ship | Synthenoid | 2 | When you play this spacecraft, place a Retired spacecraft into your Standby Zone. |
| 5SU3 | Auto-Turret | Ship | Synthenoid | 2 | This spacecraft can't be crewed. When you play it, Nullify an opposing uncrewed spacecraft. |
| 5SO1 | Refit | Action | Synthenoid | – | Retire a spacecraft card from your Hand. A spacecraft in play gets +X Rank, where X is that card's Base Rank. |
| 5SO2 | Self-Destruct | React | Synthenoid | – | Issue this Order as a spacecraft is Nullified to Nullify the opposing spacecraft and its crew as well. |
| 0ST1 | Fighter | Token ship | Galactic Council | 1 | (Mothership token; no text.) |

### Spacecraft Warzones (neutral)
| Code | Name | Effect |
|---|---|---|
| 6W03 | Asteroid Belt | Spacecraft get −1 Rank. |
| 6W04 | Ion Storm | Spacecraft have no abilities. |
| 6W05 | Vacuum | Ground Units get −2 Rank (S14). |
| 6W06 | Wreckfield | Nullified spacecraft may be Deployed as if they were in Players' Hands (S20). |
| 6W07 | Gravity Well | Spacecraft and their crews can be affected from the ground (S19). |

## Totals
42 cards + 5 Warzones + 1 token: 30 ships (6 Leader, 6 Champion, 18 other) and 12 Orders.
With S26 copy counts: **36 ship copies** join the 150-card Unit deck (186 total, ~19% ships), 12 Orders join the 80-card Order deck (92), and 5 Warzones join the Warzone deck (only when Warzones is also on). `cards.json` now holds 237 entries.

## Card data format (as implemented)
- `"set": "Spacecraft"`, `"spacecraft": true` on every ship (they are `kind: "Unit"`, S21). `unitClass`: `Leader` / `Champion` as usual; **`Craft`** for ordinary ships (Unit Nodes only; printed "Spacecraft").
- `"token": true` on `0ST1` Fighter: never in a pool or deck (`DeckBuilder` and the setup list skip it).
- **Triggers:** `OnCrewed` (Train of Thought, Thought Courier, Gelatinous Hull), `OnCrewNullified` (Hearse).
- **Filter keys** (statics and the interpreter): `spacecraft`, `crewed` (a spacecraft with/without crew), `inSpacecraft` (a crew / a ground Unit). Static selector `"Crew"` (Goo Bubble).
- **Conditions:** statics `crewed`, `crewClassNot` (Dreadnought), `crewRace` (Blazing Saddle); interpreter `crewed` (Scuttler).
- **Amounts:** `{ "countOf": …, "times": 2 }` (Earthrise), `{ "baseRankOf": "Crew" }` (High Roller).
- **Static ops:** `CannotBeCrewed` (Self), `RestrictBoarding`, `DisableElevation`, `RevealTopCards`, `ModifyAssembledRank`, `RankDoesNotCount` (Self); `PlacementOverride` `OpponentUnitEmpty`.
- **Interpreted ops:** `Retire`, `DuelOpposedSpacecraft`.
- **Replacement:** `"replaces": { "event": "UnitNullifying", "unit": "Crew" }` (Medical Frigate: only its own crew).
- **React `when`:** `"spacecraft": true` (Hijack, Self-Destruct answer spacecraft only).
- **Scripts** (`GameEngine.Spacecraft.cs`): Mothership, CrewFromPile (Chrome Steed, Sarcophagar), SporePod, Abduct, SwapCrews (Mindscrambler), BeamUp, Hijack, GelatinousHull, Engulf, ExoFrame, Refit, SelfDestruct. Morphership reuses EnterAsCopy.

## How it's built (engine)
- **Model:** `Node.Card` is the spacecraft (or ground Unit); `Node.Crew` is the Unit aboard. `Node.Holds`, `Node.Occupants`. `GameState.MoveToNode` joins a Unit and a spacecraft on an occupied node (either order); `RemoveFromCurrentLocation` of a spacecraft drops its crew into `Node.Card`; `SwapContents` moves pairs together; tokens vanish in `MoveToPile` / `MoveToHand` / `MoveToPileBottom`. `PlayerState.UnitsInPlay` and `GameState.AllCards` include crews; `Clone` copies them.
- **Rules:** `Rules/Spacecraft.cs` (IsShip, CrewOf, ShipOf, InAir, IsElevated, Crewable, CanCrew, CanJoin, NodeRank, DuelRaces, ShipsToBoard). `Placement.CanDeploy` allows joining on your own node; Standby advances may join a lone Unit with your spacecraft, and a crewed spacecraft advances with its crew (`StackItem.RidingCrew`). `Scoring` pairs use `NodeRank` (ship + crew); `Duel` uses ship Rank and both races on a tie. `Rearrange` moves pairs. `DeckBuilder` gates the set on `GameOptions.Spacecraft`.
- **Elevation and S8:** every "Opposing Unit" read meets `Node.Card` (the spacecraft), so the crew is out of reach from the ground. In `EffectRunner.SelectAsync`, "OpposingUnit" from a source in the air (a spacecraft or a crew) or under Gravity Well asks the effect's controller: the spacecraft or its crew.
- **Engine:** `BoardAsync` (boarding without a Deploy; fires OnCrewed), `CrewArrivedAsync`, `LeaveMemory` (`$crew`, `$rank` for Exo-Frame), OnCrewNullified in `CompleteNullifyAsync`, Clean-up retires crew and spacecraft separately, crews are trigger and replacement sources, The Crucible multiplies Assemble. Events: `CrewBoardedEvent`, `TokenCreatedEvent`.
- **AI:** `AiChoices` (CrewFromPile, BeamUpUnit/Ship, SwapCrewsFrom/To, HijackNode); `HeuristicAi` reacts with Self-Destruct and Hijack. Deploy evaluation (`WithTemporaryPlacement`) handles joining.
- **UI:** setup check box; Craft prints "(Spacecraft)", Leader/Champion ships "(… ship)"; a crewed node draws the spacecraft up-left and the crew down-right in front (`BoardLayout.CardCenter`, `GameView.SyncCrewLayout`); clicking your spacecraft (or Unit) takes a selected deploy; boarding tractor beam / thruster sound; Fighter launch; hovering the Unit/Order deck shows the top card while you control a crewed Research Vessel.
- **Tests:** `SpacecraftTests` (data, module on/off, boarding both ways, crew class, Viscous Fog, ground vs air targeting, Gravity Well, ship-only Duels, Duel races, pair scoring, Dreadnought, Clean-up with Lone Wolf, tokens, Mothership, Beam Up, Hull Rot / Drone Hub, Self-Destruct, Hijack, Ace pairs, Clone, heuristic and Monte Carlo games). Sweep tests updated (237 cards, 100 statics, 46 OnPlay, 12 Reacts).

## Things to check on first build/run
- Compile errors (written blind). Likely spots: pattern variables in `StaticLayer` (`ParseFilter`, `ParseCondition`, `CondOk`), `GameEngine.Spacecraft.cs`, `GameView.Spacecraft.cs`, and the `Spacecraft` type vs the `Spacecraft` properties (`GameOptions`, `SetupViewModel`).
- Board: the crew offset looks right on both sides and in Standby; nothing is left misplaced after a crew drops (a spacecraft Nullified or Decommissioned).
- Human deploys: clicking your own spacecraft (or Unit) with a hand card selected boards it.
- How the AI uses spacecraft (it values them through `DeployGain`; a Stagnant Station on your side gives you +1).
