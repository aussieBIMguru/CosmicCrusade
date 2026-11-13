# Developing Cosmic Crusade

← [Back to README](../README.md)

Cosmic Crusade is C# / .NET 8 (WPF). The rules engine is UI-free and deterministic, with its own tests.

## Build and run from source

1. Open `CosmicCrusade.sln` in Visual Studio 2022 (17.8 or later, with the .NET 8 SDK and the .NET desktop workload).
2. Set **CosmicCrusade.App** as the startup project and press F5.
3. Run the tests from Test Explorer (**CosmicCrusade.Core.Tests**).

The `data/` folder (cards, art, strips and layout) is copied next to the exe automatically.

## Solution layout

| Project | What it is |
|---|---|
| `src/CosmicCrusade.Core` | Rules engine with no UI. `Model/` holds cards, instances, nodes, state and options. `Rules/` holds deck building, rank layers, statics, placement, scoring and duels. `Effects/` interprets the card effects in `cards.json`. `Engine/` holds the round loop, the priority stack, actions, events and controllers. `Ai/` holds the heuristic and Monte Carlo AIs. `Net/` is online play (lockstep seats, relay connection, lobby). |
| `tests/CosmicCrusade.Core.Tests` | xUnit tests: card data, every card's effects, rules and rulings, the modules, the AI, determinism, a 200-game soak test, and whole online games over an in-memory relay. |
| `src/CosmicCrusade.Relay` | Local multiplayer relay (ASP.NET Core) for testing on one PC or playing on a LAN. |
| `server/cloudflare` | The same relay as a free Cloudflare Worker, for online play. |
| `src/CosmicCrusade.App` | WPF front end: the setup screen (modes and per-card copy counts), the 3D board, the hand, animations and the side panel. |

### How the engine and UI connect

- `GameEngine.RunAsync()` runs the whole game and **only asks `IPlayerController`s to pick from legal actions**. The human UI, the AI and the online seats (`LocalSeat` / `RemoteSeat`) all implement that interface.
- Every state change is published as a `GameEvent` to an `IGameEventSink`. The `GameView` implements the sink and *awaits* each animation, so the board never runs ahead of what you see.
- Everything runs on the UI thread through async/await, so there is no locking. All randomness comes from `GameState.Rng`, which is seeded, so the same seed and the same decisions replay the same game. Online play depends on this: both players' apps run the same game and only send each other their decisions (see [design/multiplayer.md](design/multiplayer.md)).

### The 3D board

The board is an ordinary 2D `Canvas` (1600×1000) mapped onto a tilted plane in a `Viewport3D` using `Viewport2DVisual3D`. It looks 3D but stays fully clickable. Hands, flying cards and banners sit in flat overlays above it.

The camera **auto-fits**: `UpdateCamera` projects the four board corners with the same pinhole maths as WPF's `PerspectiveCamera`, then solves for the distance and look-at height. The whole board, including the perspective-widened near corners, then sits between the two hands at any window size or tilt. The **Zoom** slider (and the mouse wheel) is a multiplier on that fitted distance; 1.0 fits. **Tilt** and **Speed** sliders are also in the side panel.

`Themes/Theme.xaml` gives every standard control a dark style: scroll bars, combo boxes, text boxes, sliders, check boxes, list boxes, the data grid and tooltips. The window also uses a dark title bar.

Card faces are drawn in code from `card-layout.json` (`Rendering/CardFaceFactory.cs`). Each face is rasterised once for the board and kept as a vector for the zoom panel.

Tests: Test Explorer in Visual Studio (**CosmicCrusade.Core.Tests**), or `dotnet test` from the repo root.

Clean: close Visual Studio and run `clean.bat` before zipping the project. It deletes `bin`, `obj`, `TestResults`, `artifacts` and the relay's local cache.

## Card data

Everything about the cards lives in `data/`, which is copied next to the exe:

| File | What it holds |
|---|---|
| `cards.json` | Every card: text, Rank, class, races and the machine-readable `effects` the engine runs |
| `card-layout.json` | How card faces are drawn |
| `game-modes.json`, `pools/standard.json` | Setup options and default copy counts |
| `online.json` | `defaultServer` for online play |
| `art/`, `frames/` | Card art and race strips |

The rules the engine follows, and every designer ruling, are in the design docs (`design/` in the Claude Project: `rules-spec.md`, `archons.md`, `spacecraft.md`, `card-catalogue-notes.md`).

## Multiplayer

- Design (lockstep, relay protocol, reconnects): [design/multiplayer.md](design/multiplayer.md).
- Testing two windows from Visual Studio: [multiplayer/SETUP-GUIDE.md](multiplayer/SETUP-GUIDE.md).
- The relays: `src/CosmicCrusade.Relay` (LAN Server) and `server/cloudflare` (online).

## Releasing (installer)

1. Set `<Version>` in `Directory.Build.props`.
2. Make sure `data/art` has the card art.
3. Double-click `publish.bat`.

Full steps: [release/INSTALLER.md](release/INSTALLER.md).

## AI-assisted development

The engine and app are developed with Claude. The working notes live in [`ai/`](../ai/README.md):
- the handover for the next chat;
- the milestone history.
