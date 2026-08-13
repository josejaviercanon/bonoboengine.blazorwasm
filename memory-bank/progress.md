# Progress

## Status Legend

- ✅ Done & verified · 🔶 Present but partial/template · ❌ Not started

## What Works

- ✅ Monorepo structure: `bonoboWebGame.slnx` with 4 projects (`Game.Engine`, `Game.UI`,
  `Game.Web`, `Game.Maui`), all net10.0.
- ✅ `Game.Web` host: Interactive Server Blazor app; router discovers shared RCL routes via
  `AdditionalAssemblies`; static assets + error/not-found pipeline configured.
- ✅ `Game.UI` frontend pipeline: Vite IIFE bundle + Tailwind v4 CLI → `wwwroot/dist`;
  `npm` scripts for build/watch.
- ✅ PixiJS bootstrap: `game.ts` `initGame(containerId)` with 0px-container fallback;
  `GameView.razor` full-viewport page at `/` with HUD bar; hello-world text + resize handler.
- ✅ MSBuild workaround for .NET 10 Vite-bundle precompression (MSB4018) in `Game.UI.csproj`.
- ✅ `Game.Maui` project scaffold (Android default TFM; icons/splash/fonts/raw assets).
- ✅ Documentation: `README.md` blueprint, `docs/index.md` source of truth, `docs/2d-games`
  + `docs/game-development` knowledge base, `AGENTS.md` agent rules.
- ✅ Memory bank initialized (this directory).

## In Progress / Template-State

- 🔶 `Game.Engine`: only placeholder `Class1.cs` — no simulation, commands, or events yet.
- 🔶 `Game.UI`: template leftovers `Component1.razor`, `ExampleJsInterop.cs`;
  `wwwroot/exampleJsInterop.js`.
- 🔶 `Game.Web`: template demo pages (`Counter`, `Weather`) and Bootstrap lib assets.
- 🔶 TypeScript check: `npx tsc --noEmit` known to fail (missing Node types for
  `vite.config.ts`).

## Not Started

- ❌ Core simulation: `GameSimulation`, command/event records, deterministic tick loop.
- ❌ Arch ECS integration (components as structs, systems, command buffers).
- ❌ Reactive delta bridge: engine events → `IJSRuntime` → PixiJS sprite updates.
- ❌ Persistence: System.Text.Json source-generated serializers.
- ❌ Test projects (solution currently has none; `dotnet test` validates build only).
- ❌ Server host for authoritative multiplayer (phase 2 of the blueprint).

## Known Issues

| Issue | Impact | Notes |
| --- | --- | --- |
| `npx tsc --noEmit` fails | Type-checking unavailable | `vite.config.ts` lacks Node type definitions |
| Full-solution MAUI build | OS/workload dependent | Requires .NET MAUI workloads; TFMs vary per host OS |
| Static-web-asset races | Flaky parallel builds | Never run concurrent `dotnet` commands |

## Milestones

1. ✅ Scaffold + docs + memory bank baseline (2026-08-13)
2. ⬜ Engine MVP: commands/events + one rendered sprite driven by a C# tick
3. ⬜ ECS-based world with push-delta rendering
4. ⬜ Save/load + MAUI parity
5. ⬜ Authoritative server host
