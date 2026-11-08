# AI working notes

Cosmic Crusade's engine and app are built with Claude across many chats. This folder is the memory between those chats. Players don't need any of it: see the [README](../README.md).

| File | What it is |
|---|---|
| [HANDOVER.md](HANDOVER.md) | **Start here.** Current status, what was built most recently and what to check, the module summaries, solution layout, testing tips, key architecture facts, and the roadmap. |
| [MILESTONES.md](MILESTONES.md) | The history, milestone by milestone (M1 → M3.3, the effects passes), as it used to appear in the README. |

The rules and design live in the Claude Project's `design/` docs: `rules-spec.md` (all rulings), `archons.md`, `spacecraft.md`, `multiplayer.md`, `card-catalogue-notes.md`, `installer.md`. Copies of `multiplayer.md` and `spacecraft.md` are in `docs/design/`.

## Starting a new chat

1. Run `clean.bat`, then zip the solution folder. Leaving `data\art` empty keeps the zip small.
2. Attach the zip, with any build errors, test failures or notes from playing.
3. Say: *"Read ai/README.md, then ai/HANDOVER.md."* Then say what you want next.

Before handing back a code drop, Claude updates **HANDOVER.md**: the status, things to check, and the next step.
