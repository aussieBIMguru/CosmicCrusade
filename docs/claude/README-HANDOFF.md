# Cosmic Crusade: Handoff (updated 7 Nov 2026, Archons + Spacecraft written, not yet compiled)

Cosmic Crusade is Gavin's own physical card game. It's being built as a **Windows standalone app** (C#, .NET 8, WPF/MVVM, Visual Studio) where **one human plays an AI**, with a fixed rules engine and configurable card pools. Later goals: two humans over a shared folder or bridge, then a web server.

The in-game AI is plain C# that runs locally: no network, API or LLM, so it's free and works offline.

## Status
- Design: rules spec v1.1 (`design/rules-spec.md`, 56 rulings + Archons A1–A12 / P1–P15 in `design/archons.md` + Spacecraft S1–S28 in `design/spacecraft.md`), **237 cards** encoded (`design/card-catalogue-notes.md`).
- **M1 → M3.3, effects passes 1–3 and M4a (Monte Carlo AI) built and confirmed working by Gavin** (7 Nov 2026: "it works perfectly").
- **Archons module + app icon, then the Spacecraft module, written but NOT compiled yet** (no .NET SDK in Claude's sandbox).
  - Code drop: `CosmicCrusade-Spacecraft-code.zip` (full solution: Archons + icon + Spacecraft, built on M4a). It supersedes `CosmicCrusade-Archons-code.zip` and the earlier loader patch.
  - Data drop: `CosmicCrusade-Data-Spacecraft.zip` (the whole `data/` folder; replace the repo's `data/` with it): everything from the Archons data drop, plus cards.json with 237 entries (48 Spacecraft: 30 spacecraft, 12 Orders, 5 Warzones 6W03–6W07, Fighter token 0ST1), `pools/standard.json` `spacecraft` + `spacecraftWarzones` sections, `game-modes.json` `spacecraft` option (off), `card-layout.json` `header.classDisplay`, placeholder art `art/0SL1..6W07.jpg` (generated: starfield, faction-coloured silhouette, code + name).
  - Next step: Gavin builds, runs the tests (`ArchonTests`, `SpacecraftTests`, updated sweep tests), and plays a game with *Archons* and *Spacecraft* ticked.

## Spacecraft module (7 Nov 2026)
Full design, rulings, assumed readings and implementation notes: `design/spacecraft.md`. Summary:
- Optional, **off by default** (setup check box). Spacecraft are Units in the shared Unit deck; their Orders go in the Order deck; their Warzones only with Warzones on.
- Model: `Node.Card` = the spacecraft (or ground Unit), `Node.Crew` = the Unit aboard. A spacecraft leaving drops its crew to the ground. Crews are in play everywhere (statics, scoring, triggers, "any Unit" choices). Ground "Opposing Unit" effects and Duels meet the spacecraft; from the air (or under Gravity Well) the effect's controller picks spacecraft or crew.
- Rules helper `Rules/Spacecraft.cs`; scripts `Engine/GameEngine.Spacecraft.cs`; UI `Views/GameView.Spacecraft.cs` (crew drawn offset in front of its spacecraft); tests `SpacecraftTests`.

### Spacecraft things to check on first build/run
- Compile errors (written blind): pattern variables in `StaticLayer` (`ParseFilter`, `ParseCondition`, `CondOk`), `GameEngine.Spacecraft.cs`, `GameView.Spacecraft.cs`, and the `Spacecraft` type vs the `Spacecraft` properties (`GameOptions`, `SetupViewModel`).
- Board: crew offset on both sides and in Standby; a crew that drops (its spacecraft Nullified / Decommissioned) recentres.
- Human deploys: with a hand card selected, clicking your own spacecraft (or Unit) boards it.
- Placeholder art and the "(Spacecraft)" / "(… ship)" class label on card faces.
- Fix 1 (7 Nov 2026, in the code drop): Gavin saw Chrome Steed's Retired Unit add Rank without visibly boarding. The engine boards it (tests `Chrome_Steed_crews_itself_with_a_retired_unit`, `Sarcophagar_raises_a_nullified_leader_as_a_necrolite_crew`); the UI now flies the card from its pile onto the spacecraft, rebuilds any stale view, and the crew offset is larger (±22, ±26). Confirm it shows in play.

## Archons module (7 Nov 2026)
Full design, rulings and implementation notes: `design/archons.md`. Summary:
- Optional, **off by default** (setup check box). A shared 12-card Archon deck. Once per game, at Start-up (first player first), a player may take the Archon draw: give up one Unit or Order draw, see 3, keep 1 (revealed); it must be played that round on your turn, or it is lost. Archons never enter the normal decks, hands or piles.
- Core: `Race.Archon`, `PileKind.ArchonDeck`, `LocationKind.ArchonSlot`, `PlayerState.ArchonTaken/ArchonSlot/PlayableCards`, `CardDefinition.IsArchon`, `GameOptions.Archons`; `GameState.MoveToArchonSlot/ReturnArchonToDeck` and Archon redirects in `MoveToPile`/`MoveToHand`; Clone covers all of it. Engine: `ArchonDrawAsync` (Start-up), `ArchonLostEvent` at Clean-up, `ExtraActionsAsync` (Open Source, `DecisionContext.ExtraAction`), `OfferCopyOfSourceAsync` (Androsiar), `_mainEndsNow` (Ceasefire), scripts Ceasefire / Insurrection / QuantumLeap. StaticLayer: ops `ExtraAction`, `CountAsZero`, `OfferCopyOfSelf`; Immunity can now be granted by a filter (`ImmunityGranter`, Deify), and the granter's own buffs still apply. `RankCalculator.TotalRank` honours CountAsZero.
- AI: `AiChoices` (ArchonDraw / ArchonSkip / ArchonPick with an `ArchonValue` table / CopyOfSelf / QuantumLeapDraw / QuantumLeapIssue); `HeuristicAi.ArchonNow` + `ArchonBeforePassing`; Monte Carlo treats Archon plays as normal actions and uses the heuristic for Open Source extra actions.
- UI (`Views/GameView.Archons.cs`): Archon deck pile beside Issued (1470, 860); a taken Archon flies into its owner's hand row (AI's face up; `SyncRevealedHands` keeps it); every Archon play gets an **invocation** (veil, turning rune ring, light rays, stardust, card + name; sound `ArchonInvoke`); per-card flourishes (Nebula beam, Planetscape planet burst, Ceasefire peace wave, Insurrection, Quantum Leap tesseract, Arsenal, Unstoppable shields, Galactic Siege shadow, Open Source data rain, Deify pillar + halo, The Unnameable darkness + quake, Androsiar mirror rings). New sounds: ArchonInvoke, ArchonBell, ArchonBeam, ArchonQuake. "END TURN" button text during an Open Source extra action.
- Tests (`ArchonTests`): all 12 Full; off by default / separate deck; taking and declining the draw; lost at Clean-up; leaving play returns home; Clone; Arsenal/Unstoppable; Unnameable; Deify; Galactic Siege; Open Source; Planetscape; Nebula Cannon; Ceasefire (with/without Leaders); Insurrection; Quantum Leap; Androsiar; Archon Leader deploys from the slot; heuristic and MC full games with Archons (all 12 back home at the end).

### Archons things to check on first build/run
- Compile errors (written blind). Likely spots: `GameView.Archons.cs` (WPF object initializers, tuple foreach), `StaticLayer` pattern `targets is null or { Self: true }`, `ArchonTests` helpers.
- Card faces: the Archon strip/art/colours render; The Unnameable shows Rank 10, Androsiar 4.
- The Start-up prompts read well (take the draw? which card to give up? pick 1 of 3). The AI's Archon shows face up in its hand row and flies from there when played.
- The invocation timing feels right (≈1.6 s; shorter with Reduce effects) and per-card flourishes don't collide with the normal Nullify/Lock animations (Nebula Cannon, Planetscape).
- Known UI simplification: when an Archon leaves play the generic animation may fly it toward the Retired/Nullified/Issued pile; the pile counts then come from the state (the card is really back in the Archon deck).

## App icon (7 Nov 2026)
Gavin's sketch: two C's back to front, like ⟨ ⟩. Built as two chevrons of five parallel bands in the five core race colours (Space Cowboy red, Necrolite green, Overmind blue, Blaarg yellow, Synthenoid grey) around a white four-point star, on a starry navy rounded square. Files: `src/CosmicCrusade.App/Assets/CosmicCrusade.ico` (16–256 px; `ApplicationIcon` for the exe/taskbar and `MainWindow.Icon` for the title bar) and `Assets/CosmicCrusade-1024.png` (shown beside the COSMIC title on the setup screen; also a standalone deliverable).

## M4a: Monte Carlo AI (built and working)
- `GameState.Clone(seed)`, `GameEngine.PlayOutRoundAsync(current, opponentJustPassed)`, `Ai/Determinizer`, `Ai/MonteCarloAi` (flat UCB1 over Main Phase actions; rollouts with two `HeuristicAi`s to the end of the round; value = round result + 0.05 × card economy unless the round can decide the game), `ActionKey.Of`, `AiDifficulty` Easy / Normal (900 ms, 500 rollouts) / Hard (2.5 s, 2000 rollouts) on the setup screen. Search runs on a background thread on clones only.
- Benchmark: `M4Tests.Benchmark_monte_carlo_vs_heuristic` (set `COSMIC_BENCH` to a game count).
- Next AI steps when wanted: full ISMCTS; search for priority windows and key choices; value the Archon draw by simulation; HeuristicAi clean-ups (raw-JSON text checks, `FillerOrderChance`). Known limits: determinization doesn't track cards seen going into the opponent's hand; Warzone deck order isn't re-shuffled; `StackItem` ids are global.

## Roadmap (Gavin, 7 Nov 2026)
1. ~~Archons~~ (written, above).
2. **Card banners:** clean up; a solid colour is fine (Gavin doesn't mind solid vs the crack texture). The Archon strip currently uses the crack texture.
3. **More card effects and sounds:** card-specific animations/sounds that aren't in yet (spacecraft have only boarding / Fighter visuals so far).
4. ~~Spacecraft~~ (written, above; real art still to come).
5. **Outer Rim (new race):** "has no faction", so it's immune to a lot of faction-related effects. Its Units / Orders / Warzones / ships interact with that, e.g. a neutral Champion whose faction you choose. Engine impact: decide how Outer Rim relates to the weakness cycle and to race-word rewriting (Stagnation), and how "no faction" differs from "no Race" (Consortium, The Voidslime).

Each new module needs design + rulings before code, the same way the base game, Archons and Spacecraft did. New mechanics must also work with `GameState.Clone` (add any new per-card or per-node state to it) so the AI can simulate them.

## Effects work (done, for reference)
- Core event additions: `CardDrawnEvent.Source`, `RankChangedEvent.Kind`, `RacesChangedEvent`, `RandomDeployNodeChosenEvent`; Archons: `ArchonTakenEvent`, `ArchonLostEvent`; Spacecraft: `CrewBoardedEvent`, `TokenCreatedEvent`.
- Pass 1 (`GameView.Fx.cs`): board helpers (`AddFx`, `FadeAndRemoveAsync`, `SparkBurst`, `BoardCenterOf`, `Solid`, `Rand`), cogs, slime, duels, psionic aura, death, decay, shield bubble, fireworks, Leader foil.
- Pass 2 (`GameView.Fx2.cs`): screen helpers (`AddScreenFx`, `FadeAndRemoveScreenAsync`, `Mid`), ambient layer (`SyncAmbient`/`StartAmbient`), Warzone moments, combat, control, race moments, camera (`EnsureCamera`, `CameraPush`, `Shake`), procedural sound (`Controls/Sfx.cs`, cached `%TEMP%\CosmicCrusadeSfx\*.v1.wav`; bump `v1` after changing a synth).
- Pass 3 (`Fx3.cs`, `Fx3Ambient.cs`, `FxSettings.cs`): Reduce effects / Quality, every Warzone ambient, board readout (pair chevrons, rolling totals, tug bar), atmosphere (lean, idle bob, sigils, trails, AI eye, taunts, riffle, deal), big moments (finale, deciding-Duel slow-mo).
- Lesson for blind-written code: watch for reused variable names in nested scopes (C# CS0136).

## Solution layout
- `src/CosmicCrusade.Core`
  - `Model/`: CardDatabase, CardDefinition (IsArchon, IsSpacecraft, IsToken), CardInstance (CloneCore), Node (Card + Crew), PlayerState (Archon slot), GameState (Clone, Archon deck/slot moves, joining/dropping crews, tokens), GameOptions (AiDifficulty, Archons, Spacecraft).
  - `Rules/`: DeckBuilder, RankCalculator, StaticLayer, PlayRules, Placement, Scoring, Duel, ReactConditions, AssembleRules, CardStatus, Rearrange, Spacecraft.
  - `Effects/`: EffectSupport, EffectContext, EffectRunner.
  - `Engine/`: GameEngine (+ `.Effects.cs`, `.Triggers.cs`, `.Scripts.cs`, `.Spacecraft.cs`), GameActions/LegalActions, GameEvents, Choices, IPlayerController, StackItem.
  - `Ai/`: HeuristicAi (+ RandomAi), AiChoices, AiVision, Determinizer, MonteCarloAi.
- `tests/CosmicCrusade.Core.Tests`: xUnit incl. soak, determinism, `RulesTests`, `OrderReactAssembleTests`, `EffectTests`, `StaticTests`, `TriggerTests`, `M32cTests`, `M33Tests`, `EffectsPassTests`, `M4Tests`, `ArchonTests`, `SpacecraftTests`.
- `src/CosmicCrusade.App`: `Assets/` (icon), `Views/` (GameView + partials `.Events`, `.Inspect`, `.Stack`, `.Choices`, `.Effects`, `.Fx`, `.Fx2`, `.Fx3`, `.Fx3Ambient`, `.Archons`, `.Spacecraft`; SetupView), `Controls/`, `Board/`, `Rendering/`, `ViewModels/`.

## Testing
- Scenario tests use `OrderReactAssembleTests.Run(state, p0, p1, throughCleanUp: false)` with `ScriptedController`: `.Turn(...)`, `.React(name)`, `Chooser`. The default answer to every choice is the first option / "Yes". Forced choices are never asked.
- `ArchonTests.Board()` / `SpacecraftTests.Board()`: an empty game with the module on, NoRefill, hand targets 0 (no Start-up draws) and player 0 going first (Round = 1, LastRoundWinner = 0). Put an Archon in a slot with `s.MoveToArchonSlot(card, player)`. Crew a spacecraft by placing the spacecraft, then a Unit on the same node (`TestData.Place` uses `MoveToNode`, which joins them).
- AI tests: `M4Tests.PausedGame(seed, turn)`; deterministic MC with `MonteCarloSettings { TimeBudgetMs = 0, MaxRollouts = N, Background = false }`.
- Board setup: `TestData.Place` (Order nodes too). Activate Warzones with `s.MoveToPile(instance, PileKind.ActiveWarzone)`.
- **Gotchas:**
  - A player whose only legal action is Pass is auto-passed. `Run` uses `StoppingSink`, which throws `ScriptDone` on the first `RoundScoredEvent`.
  - Pass `throughCleanUp: true` only for Clean-up tests, with `EmptyDeck = NoRefill`; a round with no actions then ends the game.
  - `EmptyGame()` uses Reshuffle: Start-up sweeps the Retired pile into the empty Unit deck. Set `EmptyDeck = NoRefill` when cards must stay in the Retired pile.
  - Tests that put cards in a deck must stop Start-up drawing them (fill hands, or set `HandUnitTarget`/`HandOrderTarget` to 0).
  - `TestData.Db.All.First(c => c.IsLeader)` is Vigan, who is Immune: pin a named Leader instead.
  - Sweep tests walk `TestData.Db.All` (all 237 cards, Spacecraft included) and count effects: 100 statics, 46 OnPlay cards, 12 Reacts. Card conservation checks must skip tokens (`PrintedDefinition.IsToken`).
- No .NET SDK is reachable from Claude's sandbox, so Gavin compiles and runs the tests.

## Key architecture facts
- The engine only asks controllers to pick from legal options. Every state change is a `GameEvent`, and the UI awaits each animation. Everything runs on the UI thread via async/await; all game RNG is seeded (the UI's `_fxRng` is cosmetic only). The MC search is the exception: it runs on a background thread, on clones only.
- Abilities resolve immediately (not on the stack); only the Nullify window uses the stack. Target choices are made at resolution.
- Statics are never stored: they are recomputed on every read from `card.Definition` (so copies and clones just work).
- All per-game state lives in `GameState` (+ cards and nodes), except the engine's own round flags, which are empty at a Main Phase turn. Keep it that way: new state goes on `GameState`/`PlayerState`/`CardInstance`/`Node` and into `Clone`.
- `NullifyAsync` is the single choke point for Nullify in play; `NullifyFromHandAsync` for hands. `GameState.MoveToPile` is the choke point that sends Archons home and makes tokens vanish.
- A node can hold a spacecraft (`Card`) and its crew (`Crew`): iterate `Node.Occupants` / `UnitsInPlay` for "Units on the board", and use `Node.Holds(card)` rather than `node.Card == card`.
- Race literals from card data must go through `StaticLayer.TextRace` (Stagnation).
- Board effects go on `BoardCanvas` (1600×1000, clipped, inside `BoardRoot`); screen effects on `FxCanvas` (`ProjectToFx` / `ProjectRect`). Board cards are placed with `PlaceOnBoard` → `BoardLayout.CardCenter` (crew offset).
- Card-specific effects key off the event's source card name (`PrintedDefinition.Name` for Archons).
- Units score for, and are controlled by, the side they sit on.
- `PoolDefinition` and `GameOptions` ignore pool sections / game-mode keys they don't know, so new modules can land in the data before the code.

## Prompt to start the next chat
> Here's my Cosmic Crusade code (zip) and data folder. Read claude/README-HANDOFF.md. Archons and Spacecraft are in: here are the build errors / test results / how they played: ...
