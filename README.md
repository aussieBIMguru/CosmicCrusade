# Cosmic Crusade

A Windows standalone version of the Cosmic Crusade card game, with a human player against an AI.

The app uses C#, .NET 8 and WPF. The rules engine is UI-free, deterministic, and has its own tests.

## Build and run

1. Open `CosmicCrusade.sln` in Visual Studio 2022 (17.8 or later, with the .NET 8 SDK and the .NET desktop workload).
2. Set **CosmicCrusade.App** as the startup project and press F5.
3. Run the tests from Test Explorer (**CosmicCrusade.Core.Tests**).

The `data/` folder (cards, art, strips and layout) is copied next to the exe automatically.

## Solution layout

| Project | What it is |
|---|---|
| `src/CosmicCrusade.Core` | Rules engine with no UI. `Model/` holds cards, instances, nodes, state and options. `Rules/` holds deck building, rank layers, placement, scoring and duels. `Engine/` holds the round loop, the priority stack, actions, events and controllers. `Ai/` holds the heuristic and random bots. |
| `tests/CosmicCrusade.Core.Tests` | xUnit tests: card data, deck modes, placement, scoring tie-breaks, duels, determinism, empty-deck rules, and a 200-game soak test. |
| `src/CosmicCrusade.App` | WPF front end: the setup screen (modes and per-card copy counts), the 3D board, the hand, animations and the side panel. |

### How the engine and UI connect

- `GameEngine.RunAsync()` runs the whole game and **only asks `IPlayerController`s to pick from legal actions**. The human UI, the AI and a future network seat all implement that interface.
- Every state change is published as a `GameEvent` to an `IGameEventSink`. The `GameView` implements the sink and *awaits* each animation, so the board never runs ahead of what you see.
- Everything runs on the UI thread through async/await, so there is no locking. All randomness comes from `GameState.Rng`, which is seeded, so the same seed and the same decisions replay the same game.

### The 3D board

The board is an ordinary 2D `Canvas` (1600×1000) mapped onto a tilted plane in a `Viewport3D` using `Viewport2DVisual3D`. It looks 3D but stays fully clickable. Hands, flying cards and banners sit in flat overlays above it.

The camera **auto-fits**: `UpdateCamera` projects the four board corners with the same pinhole maths as WPF's `PerspectiveCamera`, then solves for the distance and look-at height. The whole board, including the perspective-widened near corners, then sits between the two hands at any window size or tilt. The **Zoom** slider (and the mouse wheel) is a multiplier on that fitted distance; 1.0 fits. **Tilt** and **Speed** sliders are also in the side panel.

`Themes/Theme.xaml` gives every standard control a dark style: scroll bars, combo boxes, text boxes, sliders, check boxes, list boxes, the data grid and tooltips. The window also uses a dark title bar.

Card faces are drawn in code from `card-layout.json` (`Rendering/CardFaceFactory.cs`). Each face is rasterised once for the board and kept as a vector for the zoom panel.

## Milestone status

**M1 + M1.1 (this drop):**
- Shared decks with all three deck modes, No Leaders and the empty-deck options.
- Mandatory start-up draw.
- Main Phase turns: deploy from hand, advance from Standby, pass. Passing isn't final.
- The priority stack skeleton.
- End-of-Main scoring with the opposed-node tiebreak.
- Clean-up, and first to N rounds.
- Heuristic AI.
- Animated 3D table.
- **Warzones (M1.1):** a Warzone deck and an Active Warzone slot on the board. The top Warzone is revealed at Start-up with a flip and a banner, then returns to the bottom of the deck at Clean-up. Warzone *effects* aren't active yet, except The Oculus's placement rule.
- **Placement overrides (M1.1):** read from `PlacementOverride` in `cards.json`. Visiting Diplomat can go on any Unit Node on either side, and The Creeper on either Leader Node. While The Oculus is active, any unit may go on the opponent's nodes. A unit scores for, and is controlled by, the side it sits on.

**M1.2:** hover magnifier for hand cards and right-click inspect (`Views/GameView.Inspect.cs`).

**M3.1 (this drop): the effect interpreter, "When you play" abilities, Lock/Unlock**
- `Core/Effects/`: `EffectSupport` decides whether an encoded effect is fully understood. A card's ability runs only when every key, op, selector, filter and condition is supported; otherwise it stays inert and logs "not implemented yet". `EffectRunner` interprets `cards.json` effects: optional "you may" prompts, conditions (`unit`/`race`/`notRace`, `exists`, `selfOpposed`), selectors (`Self`, `OpposingUnit`, `OpposingLeader`, `Event.*`, `Bound.*`, `Chosen.*`, `Remembered.*`, plain filters, `choose` with min/max/count/perPlayer), filters and counts.
- Ops: Nullify (incl. on the stack), ModifyRank (`ifRace` amounts), SetBaseRank, Lock, Unlock, MoveToStandby (behind its node; Retire if no room; Assemble cards to hand for SAL-VGR), ReturnToHand, Draw (deck + up to N, N split between decks, same split as retired), Duel (incl. protect), Manipulate (any Unit, ±N), RetireFromHand, RetireHand, SwapNodeContents (control follows the side), Suppress, Assemble from piles.
- Triggers: **OnPlay** (fires for deploys and Standby advances; the controller is the side it lands on), **OnLeavePlay** (Nullify, Retire incl. Clean-up, Standby, return to hand: so Roundup Gang & co. Unlock their node), **OnSelfNullified** (Twisted Firestarter & co.). Leaving play resets Rank changes, races, memory and suppression.
- Live now: 36/38 When-you-play abilities (not Ace or TR-1993.r, which are scripted), all 5 leave-play and 5 if-Nullified abilities, 17/23 Actions (not Terraform, One of Us, Caustic Creek, Scrapped Metal, Wormhole, Dimensional Rift), and 5/10 Reacts (Shadow Strike, Target Practice, Second Thought, Spreading Sickness, Gloop!). `EffectTests` pins these lists.
- Choices: `IPlayerController.ChooseAsync(ChoiceRequest)` (default: first options). The human gets a choice panel; options on the board also glow and can be clicked. The AI uses `AiChoices` (purpose-driven: hurt their Units, help yours, lock their empty nodes, best Arganium switch...). The heuristic AI also avoids deploying Steady Shooter into a Duel it would lose.

**M2:**
- **Orders:** Action and Ongoing Orders are issued from hand on your turn. Each one goes on the stack, priority passes, then it resolves. Actions go to the Issued pile. Ongoings sit on an Empty Order Node until Clean-up.
- **Reacts and the priority stack (spec §7.2):** after every action, priority passes to the other player. A player is only asked when they hold a React whose `when` condition is met (`UnitDeploying`, `OrderIssuing` with `orderType`, `targetNodeOpposed`). Responses stack and resolve LIFO. React-on-React works.
- **Live React effects:** the counters Shadow Strike, Target Practice and Second Thought (`Nullify … cancelsPlay`) work now. A Nullified Unit goes to the Nullified pile and an Order to the Issued pile; neither is Played. (Since M3.1 all Reacts go through the effect interpreter.)
- **Assemble (ruling #18):** a turn action. The Unit goes on the stack, then is Retired and its printed Rank is added to an allied Synthenoid in play. The keyword comes from the card or from `GrantKeyword` on an Ongoing in play or the Active Warzone, so Cybernetic Enhancements (including `assembleOnto: AnyUnit`) and Production Facility already work.
- **UI:** a stack panel (left), a priority prompt (glowing Reacts, *OK – no response*), the *Pause when I can React* auto-allow toggle, Orders flying hand → stack → Order Node / Issued pile, a Nullify effect, and an Assemble effect.
- **AI:** counters opposing deploys worth stopping (Rank 3+ or round-swinging), protects its own plays with Second Thought, assembles, and plays Cybernetic Enhancements when it holds Synthenoids. `FillerOrderChance` (default 0.25) makes it occasionally issue effect-less Orders so you can see them; set it to 0 to stop that.
- **Not yet:** `UnitNullifying` React windows (Respond in Kind, Already Dead) need Nullify events from the interpreter. Neither do PlayCost cards (Mechatron, Scrapped Metal), Commandeer's control change, or Order targets.

**Next:**
- **M3.2:** Static effects (the rank layer and rule changes: 71 Static entries), the Nullify event with `UnitNullifying` React windows (Respond in Kind, Already Dead), StartUp/EndOfMain/CleanUp/OnEvent triggers, PlayCost/playCondition, and Ongoing and Warzone effects.
- **M3.3:** The 6 scripted handlers (Ace, Terraform, Mr Morph, Forced Mitosis, TR-1993.r, Wormhole), Commandeer, Recolonize, One of Us.
- **M4:** AI upgrade to determinized MCTS.
- **Visuals:** Particle trails on flying cards, nullify disintegration, race-themed node effects, sound.

## Controls

- **Click a unit in your hand**, then a glowing node, to deploy it.
- **Click a glowing Standby unit**, then its Unit Node, to advance it.
- **Ongoing Order:** click it, then a glowing Order Node. **Action Order:** click it, then the gold ISSUED pile (or press **Enter**).
- **Assemble:** click a unit with Assemble; allied units it can join glow green. Click one to Assemble.
- **Ability choices:** a gold panel at the top asks (Yes/No, a target, a node, how many...). Click an option there or the glowing card/node on the board; for multi-choices toggle them, then CONFIRM (**Enter**).
- **Reacts:** when you can respond, the status says *Respond?* and your usable Reacts pulse purple. Click one to respond, or **OK – no response** (**Space**). Untick *Pause when I can React* to never be asked. You aren't asked about answering your own plays.
- **Space** or **PASS** passes. **Right-click** empty space or **Esc** cancels a selection.
- Hover any card to see it large in the side panel.
- Hovering a hand card also floats a large, readable copy just above it (turn off with *Magnify hand cards on hover* in the side panel).
- **Right-click any face-up card** (hand, board, face-up pile, or the side-panel preview) to enlarge it full height. Any click or **Esc** closes it.
