# Cosmic Crusade: Handover to the next chat

**Updated:** 8 Nov 2026 (README split; `ai/` folder created). Latest work: player guides, licence, LAN Server, installer.

Cosmic Crusade is Gavin's own physical card game. It's being built as a **Windows standalone app** (C#, .NET 8, WPF/MVVM, Visual Studio) where one human plays an AI **or another human online**, with a fixed rules engine and configurable card pools.

The in-game AI is plain C# that runs locally: no network, API or LLM, so it's free and works offline.

## Status
- **8 Nov 2026: repo tidy.**
  - The README is now short and player-facing, with a placeholder hero image at `docs/images/hero.png` (to be replaced by a screenshot; see `docs/images/README.md`).
  - Build and architecture notes moved to `docs/developing.md`.
  - This handover moved from `docs/claude/README-HANDOFF.md` to `ai/HANDOVER.md`; the old README milestone notes are in `ai/MILESTONES.md`.
- **8 Nov 2026: docs, licence, LAN Server in the installer** (not yet built):
  - **Player guides** in `docs/players/`: HOW-TO-PLAY, RULEBOOK (player-facing rules incl. all modules), GLOSSARY, PLAYING-ONLINE, LAN-GUIDE, HOST-YOUR-OWN-SERVER. They ship in `{app}\docs`, are linked from the main menu ("New to Cosmic Crusade?", via `AppPaths.PlayerDoc`), and get a Start menu shortcut.
  - `docs/multiplayer/SETUP-GUIDE.md` is now the developer testing guide.
  - **Licence:** `LICENSE` is MIT for the software. A separate part says the game concepts, rules, theme, races, card names and text are © 2026 Gavin Nicholls, all rights reserved. Credits: card art generated with Google Gemini, engine developed with Claude. There's also `THIRD-PARTY-NOTICES.md`. Both are copied to the output, and the installer shows `LICENSE` as an information page.
  - **LAN Server:** the relay is published as a single self-contained `CosmicCrusade-LanServer.exe` into the app folder, with a Start menu shortcut. It now prints this PC's LAN addresses and takes `--port`.
  - **Default server:** `data/online.json` `defaultServer` pre-fills the Server box when the player hasn't set one (`AppSettings.DefaultServer`).
  - The stale "Milestone 3.1" text on the setup screen is replaced by the guide links.
- **8 Nov 2026: installer written** (not yet run: no Windows or Inno Setup in Claude's sandbox). Same setup as Throng:
  - `Directory.Build.props` `<Version>` (1.0.0, also shown in the window title);
  - `publish.bat` → `build\publish.ps1`: tests, self-contained win-x64 publish to `artifacts\app`, checks the exe, data and every art image, then Inno Setup → `artifacts\CosmicCrusade-Setup-<version>.exe` + SHA-256;
  - `installer\CosmicCrusade.iss`: per user, `%LocalAppData%\Programs\Cosmic Crusade`, Start menu + desktop shortcuts, AppId `5A56303A-43B9-4EFB-928C-D34F05229610`, which must never change;
  - VS publish profile `Release-win-x64.pubxml`, and `clean.bat`.
  - Steps: `docs/release/INSTALLER.md`. Gavin leaves `data\art` empty in zips; the publish script refuses to build without art unless given `-AllowMissingArt`.
- Multiplayer: Gavin built it and ran the tests. `NetTests.Dropped_connections…` failed only because the game was too short for 3 drops (fixed: drop every 7 messages, first to 5).
- **8 Nov 2026: online multiplayer written, not compiled yet.** Code drop: `CosmicCrusade-Multiplayer-code.zip` (full solution, built on the Spacecraft drop; the `data/` folder is unchanged). Design: `design/multiplayer.md`. Player and setup guide: `docs/multiplayer/SETUP-GUIDE.md`. Also in this drop: **ruling #57** (nothing in the Leader slot goes into Standby), clearer Standby logs plus regression tests for **Salvage Tug** (Gavin reported it not working), and choice labels showing player names instead of P0/P1.
  - Next step: Gavin builds, runs the tests (`NetTests`, new `SpacecraftTests`), then follows the guide: Part A (two windows + local relay), then Part C (Cloudflare deploy).
- Design: rules spec v1.1 (`design/rules-spec.md`, 57 rulings + Archons A1–A12 / P1–P15 in `design/archons.md` + Spacecraft S1–S28 in `design/spacecraft.md`), **237 cards** encoded (`design/card-catalogue-notes.md`).
- **M1 → M3.3, effects passes 1–3 and M4a (Monte Carlo AI) built and confirmed working by Gavin** (7 Nov 2026: "it works perfectly").
- **Archons module + app icon, then the Spacecraft module, written but NOT compiled yet** (no .NET SDK in Claude's sandbox).
  - Code drop: `CosmicCrusade-Spacecraft-code.zip` (full solution: Archons + icon + Spacecraft, built on M4a). It supersedes `CosmicCrusade-Archons-code.zip` and the earlier loader patch.
  - Data drop: `CosmicCrusade-Data-Spacecraft.zip` (the whole `data/` folder; replace the repo's `data/` with it): everything from the Archons data drop, plus cards.json with 237 entries (48 Spacecraft: 30 spacecraft, 12 Orders, 5 Warzones 6W03–6W07, Fighter token 0ST1), `pools/standard.json` `spacecraft` + `spacecraftWarzones` sections, `game-modes.json` `spacecraft` option (off), `card-layout.json` `header.classDisplay`, placeholder art `art/0SL1..6W07.jpg` (generated: starfield, faction-coloured silhouette, code + name).
  - Next step: Gavin builds, runs the tests (`ArchonTests`, `SpacecraftTests`, updated sweep tests), and plays a game with *Archons* and *Spacecraft* ticked.

## Online multiplayer (8 Nov 2026)
Full design: `design/multiplayer.md`. Summary:
- **Lockstep:** both apps run the engine from the host's options and seed; only decisions travel, as indices into `Legal` / `Options`. `LocalSeat` wraps the human and sends; `RemoteSeat` receives and checks a `StateHash` (desync → both games stop). Host = player 0, guest = player 1.
- **Relay:** pairs host and guest by a 5-letter room code and forwards text. Free online version: `server/cloudflare` (Worker + SQLite Durable Object, `npx wrangler deploy`). Local/LAN version: `src/CosmicCrusade.Relay` (ASP.NET Core, ws://localhost:5080).
- **Reliability:** 4 s heartbeat both ways (opponent silent for 15 s → "connection lost, waiting"), automatic reconnect into the same seat (secret key), numbered decision log plus `resume` re-sends missed moves, stuck detection (both waiting with nothing in flight → desync).
- **Lobby:** guest `hello` (version, name, cards.json fingerprint) → host `start` (options, names) or `reject`. **Bump `NetProtocol.GameVersion` when the rules change.**
- **App:** `Board/Perspective.cs` (Me/Opp; ~80 UI sites converted from hard-coded player 0, all "AI" strings now use the opponent's name); `Views/GameView.Online.cs` (chat, status line, waiting overlay, opponent left / desync handling); setup screen PLAYER + PLAY ONLINE panel (name, server, HOST / JOIN, code); `AppSettings` (`%AppData%\CosmicCrusade\settings.json`).
- **Not yet:** resume after an app restart, a rematch button, spectators, turn timer, hidden-information (server-authoritative) mode.

### Multiplayer things to check on first build/run
- Compile errors (written blind). Likely spots: `GameView` partials (the new `Me`/`Opp` static helpers vs any local names, pattern variables added to the round-result and game-over code in `GameView.Events.cs` and to `UpdateBoardReadout`), `SetupView.xaml` (new right-column panel), `NetConnection` (channels, `Interlocked.Exchange` on the transport), the relay's primary-constructor classes.
- `NetTests` uses a single-thread pump: if a lockstep test hangs it fails after 120 s with a cancellation.
- Guest view: your own side at the bottom, totals and banners from your point of view, the chevrons and tug bar pointing the right way.
- Chat focus: Space must not pass while typing; Esc or clicking the table returns the keyboard.

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
0. ~~Online multiplayer~~ (written, above).
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
  - `Net/`: NetProtocol (messages, options DTO, codes, fingerprint), NetTransport (WebSocket), NetConnection (heartbeat, reconnect, resend log), LockstepSeats (LocalSeat, RemoteSeat, StateHash), NetLobby.
- Release: `publish.bat`, `build/publish.ps1`, `installer/CosmicCrusade.iss`, `src/CosmicCrusade.App/Properties/PublishProfiles/Release-win-x64.pubxml`, `clean.bat`.
- `src/CosmicCrusade.Relay`: local relay (ASP.NET Core minimal API, port 5080).
- `server/cloudflare`: online relay (Cloudflare Worker), with a Node test of its room logic.
- `tests/CosmicCrusade.Core.Tests`: xUnit incl. soak, determinism, `RulesTests`, `OrderReactAssembleTests`, `EffectTests`, `StaticTests`, `TriggerTests`, `M32cTests`, `M33Tests`, `EffectsPassTests`, `M4Tests`, `ArchonTests`, `SpacecraftTests`, `NetTests` (+ `NetTestSupport`: in-memory relay, single-thread pump).
- `src/CosmicCrusade.App`: `Assets/` (icon), `Views/` (GameView + partials `.Events`, `.Inspect`, `.Stack`, `.Choices`, `.Effects`, `.Fx`, `.Fx2`, `.Fx3`, `.Fx3Ambient`, `.Archons`, `.Spacecraft`, `.Online`; SetupView), `Controls/`, `Board/` (incl. `Perspective`), `Rendering/`, `ViewModels/`, `AppSettings`.

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
- The UI never assumes "you are player 0": use `Me` / `Opp` / `OppName` (`Board/Perspective.cs`). The engine must stay deterministic (seeded RNG only, no iteration over hash order that can differ between processes): online play depends on it.
- `PoolDefinition` and `GameOptions` ignore pool sections / game-mode keys they don't know, so new modules can land in the data before the code.

## Prompt to start the next chat
> Here's my Cosmic Crusade code (zip; data/art left empty). Read ai/README.md, then ai/HANDOVER.md. Here are the build errors / test results / what I want next: ...
