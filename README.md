# Cosmic Crusade

A Windows standalone version of the Cosmic Crusade card game: play against an AI, or against a friend online.

**Online play:** fill in your name and the server on the menu, click HOST GAME and give your friend the code, or type their code and click JOIN. Player guides: [How to play](docs/players/HOW-TO-PLAY.md), [Rulebook](docs/players/RULEBOOK.md), [Glossary](docs/players/GLOSSARY.md), [Playing online](docs/players/PLAYING-ONLINE.md), [LAN guide](docs/players/LAN-GUIDE.md), [Host your own server](docs/players/HOST-YOUR-OWN-SERVER.md). Testing multiplayer from Visual Studio: [`docs/multiplayer/SETUP-GUIDE.md`](docs/multiplayer/SETUP-GUIDE.md). Design: [`docs/design/multiplayer.md`](docs/design/multiplayer.md).

**Installer:** double-click `publish.bat` (needs Inno Setup 6) to get `artifacts\CosmicCrusade-Setup-<version>.exe`: per user, no admin rights, nothing else to install. Steps: [`docs/release/INSTALLER.md`](docs/release/INSTALLER.md). The version lives in `Directory.Build.props`.

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
| `src/CosmicCrusade.Relay` | Local multiplayer relay (ASP.NET Core) for testing on one PC or playing on a LAN. |
| `server/cloudflare` | The same relay as a free Cloudflare Worker, for online play. |
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

**Effects pass 2 (this drop): card-specific effects, ambience and music** (`Views/GameView.Fx2.cs`)
- **Ambient layer** (follows whatever is active, `SyncAmbient`): Black Hole vortex, Uncharted Space nebulae, Atmosphere Generator dome with heat shimmer, Acid Rain drizzle, Stagnation fog (and a "ALL BECOME BLAARG" wash), Polarity Storm lightning (and flipping Rank badges); any other Warzone glows round its slot in its race colour.
- **Warzone moments:** Black Hole sucks Nullified Units into the vortex; Wormhole opens a portal while you choose; Dimensional Rift tears the board open.
- **Combat:** Shadow Strike slash and shatter, Target Practice crosshairs and shot, Second Thought rewind, High Noon sunset and tumbleweed, Xeno Laser charge-and-beam, Respond in Kind mirror flash.
- **Protection and control:** a breathing hex shield on Units that can't be Nullified or are Immune, green goo on Suppressed Units, Commandeer's grappling claw, tractor beams for One of Us and Arganium, Psychic Coercion tendrils, Shoot First's roulette, Recolonize's thruster burst.
- **Races and moments:** race-themed landings (dust, cracks and bones, psychic rings, goo, sparks and steam, a holo beam), Leader arrivals (horns, heavier shake, camera push-in), liquid-chrome copies, Ace's tactical grid, a gavel for The Empress / Senate Chambers, a Clean-up sweep, and a heartbeat with pulsing scores in close rounds.
- **Sound:** sounds for all of the above, hover and pick-up sounds in your hand, and a generated music bed whose pulse layer rises in close rounds (*Music* toggle).

**Effects pass: animation and sound**
- Assemble: two meshing cogs spin up over the Unit with sparks. Manipulate: a green slime splat with droplets and drips.
- Duels: blaster bolts fly back and forth (blue = yours, red = the AI's), then the loser(s) are smoked out.
- Draws from a card's ability: a violet psionic pulse from that card, and the drawn cards glow.
- Nullify: the Unit flashes, burns, crumbles to ash and embers, and a skull rises.
- Necrolites: becoming one (Already Dead, Abominations) or rising from the Nullified pile (Shallow Grave, Grim Angler, Reaper of Rot, Galactomancer, Hills of Decay) shows a flickering rot with wisps and drips.
- Also: a hex shield when a Nullify is prevented, fireworks when you win a round or the game, and a foil shimmer on every Leader.
- **Sound:** procedural effects (no asset files; written once to `%TEMP%\CosmicCrusadeSfx`). *Sound* toggle and volume slider in the side panel. `Controls/Sfx.cs`, effects in `Views/GameView.Fx.cs`.
- The setup screen's card list now includes the Warzones (their counts apply when the expansion is on).

**M3.3: every card is implemented**
- **Scripts** (`Engine/GameEngine.Scripts.cs`): Ace, Council Tactician (switch nodes one pair at a time, NONE to stop; rules in `Rules/Rearrange.cs`), Terraform (each controller may save their Unit in Standby, otherwise it is Nullified; then both nodes Lock), Mr Morph and Forced Mitosis (the Unit enters as a Blaarg copy of a Unit in play: `CardInstance.BecomeCopy`, undone when it leaves play), TR-1993.r (re-run chosen Units' "When you play" abilities, each as its controller).
- **Stack control:** Recolonize (`RedirectDeploy`), Commandeer (`GainControl` of the Order being Issued; a commandeered React may be pointed at another item), One of Us (the Unit moves to your side and stays in play).
- **AI:** greedy rearranging with Ace, sensible copy picks, Commandeer on your Actions, Forced Mitosis on big deploys, One of Us / Terraform when they swing the round.
- Every card face is now unmarked (no "not implemented" boxes). `M33Tests` covers the new cards.

**M3.2b: the Nullify React window, phase and "whenever" triggers, costs, deploying from piles**
- **Rulings:** #36 Leaders can never go into Standby (Sisters of Blight can't pick a node holding a Leader, Spreading Sickness can't answer a Leader's deploy, Y'nosh / Shallow Grave & co. can't choose one; if it would happen anyway nothing happens). #37 a Duel with matched Ranks and no deciding weakness Nullifies both Units (`Duel.Losers`; Crimson Redeemer still protects itself).
- **Nullify window (spec §7.2):** every Nullify of a Unit in play opens a `UnitNullifying` stack item with nested priority (`RunPriorityAsync` now only resolves down to its own starting depth). Already Dead (PreventEvent + becomes a Necrolite) and Respond in Kind are live. The window only shows on the stack once someone responds, so it reveals nothing. Replacements: Glorpaglorp and Fallout Shelter (Standby instead; Nullified if full), Black Hole (Retire / Standby → Nullify instead; if that Nullify is prevented the original move happens).
- **Phase triggers** (`Engine/GameEngine.Triggers.cs`): End of Main – The Empress, High Noon (all opposed pairs incl. Leaders, simultaneous), Weed out the Weak; Clean-up – Y'nosh first, Preferred Candidate (remains in play, +2), Lone Wolf / Mindless Mass / Grey Matter Master / Regeneratrix / Repair Drone. Order: `priority: First`, then the first player's, then the second player's, then `AfterRankModifiers` (High Noon). `WinRound` sets `RoundResult.WonBy`.
- **"Whenever ..." (OnEvent):** all 9 are live – Lance Corporal, Fast Eddie (the AI only Duels when it wins), Xeno Laser (once per round), Disturberus, Cortexella, Cntrl-C Cntrl-V, Rapid Fabrication, Mercenary Camp, Primordial Marsh. Refraction Network also stops the "when played" ones.
- **Senate Chambers:** checked after every turn; the round ends at once.
- **Costs and conditions** (`Rules/PlayRules.cs`): Mechatron and Scrapped Metal pay their Retire cost as they are played and can't be played without it (Suppressing Goop waives it); Caustic Creek and Dimensional Rift (adds the top 2 Warzones) check their play condition.
- **Deploying from piles:** Barkeep Bill, Galactomancer, Hills of Decay → `DeployFromPileAction`. You click the glowing Retired / Nullified pile, then pick the Unit and the node. Counts for Military Academy.
- Live now: 36/38 OnPlay, 20/23 Actions (not One of Us, Terraform, Wormhole), 7/10 Reacts (not Commandeer, Recolonize, Forced Mitosis), all 9 OnEvent, 3/3 End of Main, 7/7 Clean-up, 64/71 Static (inert: Abominations, Stagnation, Shoot First, Psychic Coercion, and the 3 RevealHands). `TriggerTests` covers the new rules.

**M3.2a: Static effects – the Rank layer, Immune/cannot be Nullified, suppression and rule changes**
- `Core/Rules/StaticLayer.cs` compiles every `"trigger": "Static"` entry once (all-or-nothing, like `EffectSupport`) and answers every query live from the board, so an anthem starts and stops the moment its source enters or leaves play. Sources: Units on Unit/Leader nodes, Ongoings on Order Nodes, the Active Warzone. Standby Units are neither sources nor targets.
- **Rank (spec §7.4):** layer 2 static Base Rank overrides (Primordium, The Voidslime, Cure Failure 349; persistent overrides such as Corporate Detainer sit on top), layer 3 static modifiers: flat anthems/debuffs (Blazing Saddles, the Champions, The Creeper, the Warzones), conditional (Rebel with a Cause & co.), counted (Boarding Party, Rise of the Tyrant), derived (Indominus, The Resonator), Remembered race (Containment Network), Acid Rain's floor 0. Polarity Storm inverts layers 3–4. A loop of mutually dependent Ranks is cut by ignoring statics on the inner read.
- **Races:** `StaticLayer.Races` (Consortium, The Voidslime) is used by Duels, filters, conditions, Assemble and the AI.
- **Protection:** Vigan is Immune (no statics from others, can't be chosen or Nullified by other cards). Diplomatic Immunity, Force Field and No Gods, No Masters stop Nullify (`NullifyPreventedEvent`, cyan shield pulse in the UI). A Unit being Deployed is not protected (errata).
- **Suppression:** Suppressing Goop (everything else, hands too: Assemble, placement overrides, Orders resolve with no effect), Atmosphere Generator (Warzones), Refraction Network ("when played" abilities), plus Gloop!'s flag.
- **Rule changes:** Potency Tank (+2 Manipulate), The Oculus (lowest total wins), Extreme Sanctions and Temporal Stasis (play restrictions, Reacts too), Even Odds, Mind Games, Military Academy (`PlayerState.DeployTurnsThisRound`). "As you Issue, choose a Race" (`OnIssue` → `ChooseRace`) is live.
- Live: 60/71 Static entries (58 cards). Inert: PlayFromZone (Barkeep Bill, Galactomancer, Hills of Decay), RevealHands (The All-knowing, Transparency, Oracle's Arch), OverrideDecision (Shoot First, Psychic Coercion), GrantAction (Abominations), ReplaceRaceWords (Stagnation), DeployRequiresPlayable (Mechatron). `StaticTests` pins the list.
- **AI:** deploys and Ongoings are valued by how much they change the margin with statics included (tried on the board via `GameState.WithTemporaryPlacement`), so it plays anthems and avoids self-harm. `AiChoices` picks races for Extreme Sanctions / Containment Network.
- **UI:** all Rank badges and totals refresh after any event that can switch a static on or off; right-click inspect lists the statics acting on a card.

**M3.1: the effect interpreter, "When you play" abilities, Lock/Unlock**
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
- **Presentation pass:** more animation and sound.
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
