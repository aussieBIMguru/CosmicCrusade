# Cosmic Crusade: Card Catalogue Notes (v1)

`cards.json` holds all **177 card definitions**, transcribed from the card images (OCR plus a visual check of every number and sign).

| Set | Units | Orders | Warzones |
|---|---|---|---|
| Base | 87 (32 Leader, 5 Champion, 5 Brute, 27 Minion, 17 Underling, 1 Bystander) | 60 (29 Ongoing, 21 Action, 10 React) | none |
| Warzones | none | 3 (Wormhole, Atmosphere Generator, Dimensional Rift) | 27 |

Copy counts live in `pools/standard.json` (`base` and `warzones` sections, keyed by card id). Mode options live in `game-modes.json`. See rules spec Section 10.

## Card record
| Field | Notes |
|---|---|
| `id` | The image file code, e.g. `5U09`. The first character is the race (0 GC, 1 SC, 2 NE, 3 OV, 4 BL, 5 SY, 6 Neutral). L = Leader-folder unit, U = unit, O = order, W = warzone. The image is `images/<id>.jpg`. |
| `name` / `printedName` | Display name with typos fixed. `printedName` appears only when the card print differs. |
| `kind`, `unitClass`, `baseRank`, `orderType` | `baseRank` is `"X"` for Cure Failure 349 (its Base Rank is set by an effect). |
| `races` | Always a list, ready for Hybrids. |
| `keywords` | e.g. `Assemble` on Sprocket Spider and Cogwork Soldier. |
| `text` | Rules text exactly as the card states it (shown in the UI). |
| `flavor` | The italic flavour text. It never affects rules. |
| `effects[]` | The machine form. Each entry has a `trigger` and either a `do` list of ops or a `script` name. `note` holds errata and rulings. |

## Effect vocabulary used
**Triggers:** Static (71), OnPlay (38), OnResolve (23), React (10), OnEvent (9), CleanUp (7), OnLeavePlay (5), OnSelfNullified (5), EndOfMain (3), Replacement (3), OnIssue (2), PlayCost (2), AsEnters (1), StateCheck (1).

**Ops** (number of uses):
- **Rank:** ModifyRank 49, SetBaseRank 5, Manipulate 5, ModifyManipulate, AddAssembledRank, Assemble, InvertRankModifiers.
- **Movement:** Nullify 35, MoveToStandby 15, Lock 9, Unlock 6, ReturnToHand 2, RetireFromHand 3, RetireHand, NullifyFromHand, SwapNodeContents, GainControl 2, RemainInPlay.
- **Combat:** Duel 4, DuelAllOpposed.
- **Cards:** Draw 10, PlayFromZone 3, RevealHands 3, RevealWarzones.
- **Rules modifiers:** Immunity, CannotBeNullified 3, Suppress 4, SetRaces 4, ReplaceRaceWords, GrantKeyword 2, GrantAction, PlacementOverride 3, RestrictDeploy 2, RestrictPlay 2, OverrideDecision 2, RedirectDeploy, Retarget, PreventEvent, DeployRequiresPlayable, LimitTurns, ChooseRace 2, WinRound 2, InvertRoundWinner.

**Selectors:** `Self`, `OpposingUnit`, `OpposingLeader`, `Event.*`, `Bound.*`, `Remembered.*`, `Count.*`, or a filter object (`zone`, `kind`, `controller`/`owner`, `race`/`notRace`, `unitClass`, `excludeSelf`, `opposed`, ...). Adding `choose` to a filter makes it a player decision, with `min`/`max`.

## Cards handled by custom C# handlers (6)
| Card | Handler | Why it isn't data |
|---|---|---|
| Ace, Council Tactician | RearrangeAllNodes | Free-form rearrangement with legality checks |
| Terraform | Terraform | Per-controller choice: Standby or Nullify, then Lock |
| Mr Morph | EnterAsCopy | Copy effect (as it is Played) |
| Forced Mitosis | EnterAsCopy | Copy effect, chosen by the React's issuer |
| TR-1993.r | RetriggerAbilities | Re-runs other cards' on-play abilities |
| Wormhole | Wormhole | Look at the top 3 Warzones, choose one, reorder the deck |

## Hardest engine features (by card)
- **Stagnation:** race words in all card text become Blaarg. Effects must reference races as parameters that can be overridden, never as hard-coded checks.
- **Suppressing Goop / Gloop! / Refraction Network / Atmosphere Generator:** a suppression layer applied before abilities are evaluated.
- **Polarity Storm:** inverts the sign of rank modifiers.
- **Psychic Coercion / Shoot First / Mind Games:** decision-override hooks in the Deploy pipeline.
- **Commandeer / Second Thought:** React-on-React, which the priority stack already covers.
- **The Oculus / The Empress / Senate Chambers:** alternative round-win checks.
