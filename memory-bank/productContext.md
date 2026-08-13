# Product Context

## Why This Exists

Most web/hybrid game setups force a painful choice: write game logic in JavaScript (losing
C# tooling and server authority) or write it twice (client JS + server C#). Bonobo avoids
both by making **C# the single authoritative language** for simulation, with the browser/
native shell acting purely as a *mirror* of engine state.

## Problems Being Solved

1. **Duplicate logic** — one engine codebase serves the MVP client and the future
   authoritative multiplayer server.
2. **Interop performance** — naive Blazor↔JS bridges serialize state per frame and kill
   frame rate. Solved with push-based delta events over `IJSRuntime`.
3. **Heavy engine overhead** — PixiJS v8 (WebGL/WebGPU) renders inside the BlazorWebView
   without a full game-engine runtime.
4. **Cross-platform reach** — one shared RCL (`Game.UI`) targets web (Blazor Server) and
   mobile/desktop (.NET MAUI Hybrid) from the same components and assets.

## Intended Experience

- HUD, menus, inventories: standard HTML/CSS via Blazor + Tailwind (responsive, accessible).
- Game world: hardware-accelerated 2D canvas via PixiJS, updated reactively.
- Game feel and design guidance lives in `docs/2d-games` and `docs/game-development`.

## Planned Engine Conventions (from `docs/index.md`)

- **Arch ECS** for game state: components are zero-logic public `struct`s (cache locality);
  systems process data via `QueryDescription` / `BaseSystem`; structural changes via command
  buffers or outside query loops — never mid-query.
- **System.Text.Json source generators** for AOT-safe persistence and delta frames.
- **PixiJS v8** for rendering; **Tailwind CSS v4** for UI layout/theme.

## Knowledge Bases for Agents

- `docs/2d-games`, `docs/game-development` — genre references, game feel, architecture
  decision records (e.g. "Nez dropped", engine alternatives), AI workflow, solo-dev playbook.
- Microsoft Learn MCP server noted in `docs/index.md` for trusted .NET documentation.
