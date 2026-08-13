# Active Context

## Current Focus (2026-08-13)

Repository initialization: baseline commit of the scaffolded monorepo + creation of this
memory bank. The project is at **scaffold stage** — solution structure, hosts, and the
PixiJS hello-world pipeline exist; the engine itself is not yet implemented.

## Recent Changes

- Expanded `README.md` from one line to the full architectural blueprint.
- Added `AGENTS.md` (agent/build rules), `bonoboWebGame.slnx`, `docs/` (architecture +
  2d-games + game-development knowledge base), and the four `src/` projects.
- Verified: `Game.UI` PixiJS hello-world renders via `initGame` from `GameView.razor`
  (per repo docs; build outputs present).

## Next Steps

1. Replace `Game.Engine/Class1.cs` with the real core: `GameSimulation`, command records
   (`MoveCommand`, …), event records (`EntityMovedEvent`, …) per `docs/index.md` Step 1.
2. Introduce Arch ECS into `Game.Engine` (struct components, systems, command buffers).
3. Wire the Blazor→PixiJS delta bridge in `Game.UI` (subscribe to engine events, forward
   via `IJSRuntime`) — replace hello-world text with sprite management.
4. Decide on `System.Text.Json` source-generator context for save/delta payloads.
5. Fix `npx tsc --noEmit` (add Node type definitions for `vite.config.ts`).
6. Add a test project for `Game.Engine` (none exist yet).

## Open Questions / Watch Items

- MAUI workloads availability on this machine for non-Android TFMs.
- Whether to keep template demo pages (`Counter`, `Weather`) in `Game.Web` or strip them.
- `ExampleJsInterop.cs` / `Component1.razor` in `Game.UI` are template leftovers.

## Working Agreements

- `docs/index.md` is the architecture source of truth; update it when code diverges.
- Follow `AGENTS.md`: build frontend first, no concurrent dotnet commands, never hand-edit
  `wwwroot/dist`, never commit build output.
- Keep `Game.Engine` free of UI/platform dependencies.
