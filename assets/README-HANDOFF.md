# Cosmic Crusade: Handoff Bundle (5 Nov 2026)

Cosmic Crusade is Gavin's own physical card game. The plan is a **Windows standalone app** (C#, .NET 8, WPF/MVVM, Visual Studio) where **one human plays an AI**, with a fixed rules engine and configurable card pools. Later goals: two humans over a shared folder or bridge (e.g. Google Drive), then a web server.

**Status:** design is complete. All rules questions are answered (35 rulings) and all 177 cards are transcribed and encoded. **No code has been written yet.**

## What's in this bundle
| Path | What it is |
|---|---|
| `design/rules-spec.md` | **Engine rules spec v1.0.** The authoritative rules: the rulebook plus every designer ruling (Sections 9–10). Read this first. |
| `design/card-catalogue-notes.md` | The `cards.json` schema, the effect vocabulary (triggers, ops, selectors), the 6 cards needing custom C# handlers, and the hardest engine features. |
| `data/cards.json` | 177 card definitions (147 base, 30 Warzones expansion), with rules text, flavour text and machine-readable effects. |
| `data/pools/standard.json` | Default copy counts per card id (`base` and `warzones` sections). |
| `data/game-modes.json` | Pre-game options: rounds to win, empty-deck rule, Warzones, No Leaders, Standard / True Singleton / Random Draw. |
| `data/images/<id>.jpg` | All 177 card images, renamed to their card ids (780×1075). |
| `data/art/<id>.jpg` | **Artwork only**, cropped from each card (503×441). The top-right corner is rounded at radius 56; clip it in the UI. |
| `data/frames/strip_<style>.png` | Clean left-hand colour strips with the crack texture and no name (190×1075): one per race, plus Warzone and Neutral. |
| `data/card-layout.json` | The **card template**: every position, font, size and colour measured from the originals. The app composes cards from this, `cards.json`, the art and the strips. |
| `design/card-render-comparison.jpg` | Originals (top) vs programmatic renders (bottom). |
| `source/Rule_Book.docx` | The original rulebook. |
| `tools/build_cards.py` + `tools/split.json` | Regenerates `cards.json`: `python build_cards.py split.json cards.json`. To change a card, edit its entry in the script. |
| `tools/render_card.py` | Reference renderer (Python/Pillow) that builds card faces from the template; run as `python render_card.py <data dir> <out dir> [ids]`. It's the algorithm spec for the WPF `CardControl`. It uses stand-in fonts; the app should use **Haettenschweiler** (display) and **Calibri Italic** (flavour), both on Windows. |

## Card rendering
Cards are drawn in code, not shown as flat images, so text, Rank and race can change live (e.g. Stagnation, The Voidslime, Manipulate, typo fixes):
1. White 780×1075 canvas, scaled uniformly.
2. The race strip at x 0–190. Warzones use the Warzone strip; Neutral Orders use Neutral.
3. The art at (200,77), clipped with a top-right radius of 56.
4. The name, rotated to read bottom-to-top, centred at (112,540). White with a black outline and drop shadow; shrinks to fit 700 px.
5. Two header rows (`Race:` / `Rank:` or `Type:`). Race, Rank and "Order" use the race text colour; the bracketed class or subtype is grey #4B4B4B.
6. Ability text from y 706, width 560, size 50. Race names are drawn in their race colour and numbers in the card's colour. The text shrinks to fit (minimum 38) above y 1050, together with the flavour.
7. Flavour in grey italic, width 470, bottom-aligned around y 1010.

The original full card images stay in `data/images/` for a "view printed card" toggle.

## Key rules at a glance (details in the spec)
- 2 players with **shared** Unit and Order decks. Hands fill to 5 Units and 3 Orders each round.
- **Turn** = one action. **Round** = turns until both players pass in a row, then End of Main scoring, then Clean-up. First to 3 round wins takes the game.
- **Board per side:** 3 Unit Nodes, 1 Leader Node (holds a Leader or a Champion), 3 Standby, 2 Order Nodes.
- **MTG Arena-style priority** after every action, with a stack for Reacts and an auto-allow toggle.
- **Control follows the side a Unit sits on.** Standby Units are not in play.
- **Weakness cycle** (breaks Duel ties): Space Cowboy ▶ Necrolite ▶ Overmind ▶ Blaarg ▶ Synthenoid ▶ Space Cowboy. Galactic Council is neutral.

## Agreed architecture
1. **`CosmicCrusade.Core`** (.NET 8 class library, no UI): game state, phases, legal-action generation, priority stack, effect interpreter for `cards.json`, and the 6 scripted handlers. Deterministic with seeded RNG, so games replay exactly and can be cloned for the AI.
2. **`CosmicCrusade.Core.Tests`** (xUnit): rules tests per spec section and per card.
3. **`IPlayerController`**: the engine only asks controllers to pick from legal decisions, including priority responses and choices inside effects. Human UI, AI and a future network player all implement it.
4. **`CosmicCrusade.App`** (WPF/MVVM): board, card viewer and zoom, pre-game setup (modes and per-card counts), priority prompts with auto-allow.
5. **AI:** a heuristic bot first, then determinized Monte Carlo tree search. Hidden information must be respected: the AI only sees what its seat could see.

## Suggested next steps
1. Scaffold the solution and Core domain model: cards, instances, zones, nodes, game state, rank calculation layers.
2. Card loader, then the round loop: Start-up → Main → End of Main → Clean-up, with Pass and Deploy for vanilla Units.
3. Priority stack, Reacts, then the effect interpreter op by op, with tests for each.
4. A console runner (random-vs-random) to soak-test, then the WPF UI, then the AI.

## Prompt to start the next chat
> Here's my Cosmic Crusade handoff bundle. Read README-HANDOFF.md and design/rules-spec.md, then scaffold the .NET 8 solution (Core, Core.Tests, App) and start on step 1 of the suggested next steps.
