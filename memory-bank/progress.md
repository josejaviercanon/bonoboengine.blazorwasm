# Progress

## Status Legend

- ✅ Done & verified · 🔶 Present but partial/template · ❌ Not started

## What Works

- ✅ Monorepo structure: `bonoboWebGame.slnx` with 4 projects (`Game.Engine`, `Game.UI`,
  `Game.Web`, `Game.Maui`), all net10.0.
- ✅ `Game.Web` host: **static SSR only** Blazor app (no Interactive Server/SignalR); router discovers shared RCL routes via `AddAdditionalAssemblies`; `GET /api/ecs/stream` SSE (`event: sprite-move`, `SpriteState[]`); static assets + error/not-found pipeline.
- ✅ `Game.UI` frontend pipeline: Vite IIFE bundle + Tailwind v4 CLI → `wwwroot/dist`;
  `npm` scripts for build/watch.
- ✅ PixiJS bootstrap: `game.ts` `initGame(containerId)` with 0px-container fallback;
  `GameView.razor` full-viewport page at `/` with HUD bar; hello-world text + resize handler.
- ✅ MSBuild workaround for .NET 10 Vite-bundle precompression (MSB4018) in `Game.UI.csproj`.
- ✅ `Game.Maui` project scaffold (Android default TFM; icons/splash/fonts/raw assets).
- ✅ Documentation: `README.md` blueprint (synced with `docs/index.md`), `docs/index.md` source
  of truth, `docs/2d-games` + `docs/game-development` knowledge base (cross-linked),
  `AGENTS.md` agent rules.
- ✅ Agentic-dev docs: `docs/ai-agents/codebase-truth.md` (verified facts),
  `docs/adr/` infrastructure (ADR-001…ADR-006 recorded), `openspec/config.yaml` populated.
- ✅ Architecture topology: `docs/architecture/topology.md` (Implemented vs Target) +
  ADR-001 (three-layer topology), ADR-002 (Box2D.NET/Rapier layering), ADR-003 (render-bridge
  evolution), ADR-004 (glTF as asset contract), ADR-005 (entity-selective presentation
  physics), ADR-006 (domain responsibility matrix).
- ✅ Render-interpolation guide: `docs/architecture/render-interpolation.md` (2026-08-18) —
  α math, per-entity `InterpState` buffer (asteroids.ts reference), Rapier kinematic
  coupling (target), dual-physics isolation matrix, HEAPF32 future path. Cross-linked from
  docs/index.md, topology.md, ADR-003, SKILL.md.
- ✅ Game examples UX (2026-08-18): every game scene (snake, tetris, breakout, pacman,
  asteroids, racer) has a start overlay + play-again/restart wired to the server reset
  endpoints; racer gained `#racer-start-overlay` + `#racer-restart-button` (sim paused at
  boot, START/Space resumes, RESTART → `/api/racer/restart`).
- ✅ Interpolation post-rollout fixes (2026-08-18): `SnapshotBuffer` epoch-before-seq
  ordering (restart freeze) and `advance()` idle-redraw gate (FPS regression) — see
  `docs/architecture/render-interpolation.md` §7. All 29 Playwright E2E green.
- ✅ Memory bank initialized (this directory).
- ✅ Test infrastructure (2026-08): `Game.Tests` (xUnit v3, determinism/ECS/snapshot unit tests), `Game.Tests.Aot` (TUnit, AOT/trim pattern checks), `Game.Tests.UI` (Node/TS Playwright E2E vs `Game.Web`, Chrome channel, port 5902). Root `global.json` opts into Microsoft.Testing.Platform (required for `dotnet test` on .NET 10). All green: 21 .NET tests, 5 Playwright tests. Guide: `docs/testing-ui-E2E/index.md`.
- ✅ Playwright bootstrap-race fix: `App.razor` inline `load` script now polls up to 15 s for `window.initGame` (ES-module dynamic imports finish after `load`).
- ✅ Solution build is web-only for speed: `Game.Maui` commented out of `bonoboWebGame.slnx` (re-enable for native work).
- ✅ Verdict recorded: Playwright MCP server NOT needed — skills + `playwright-cli` + checked-in Playwright suite suffice (`docs/testing-ui-E2E/index.md`).

## In Progress / Template-State

- ✅ `Game.Engine`: Arch ECS implemented — `EcsSimulation` (60 Hz, `MovementSystem`/`ColorSystem`, batched `EcsRenderSignal` @1s, SSR `Snapshot()`), `Components.cs` (`Position`/`Velocity`/`SpriteColor`/`RenderId`). No commands/`ProcessCommand` yet.
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
- ❌ Box2D.NET authoritative physics wired into ECS loop (vendored `src/Box2D.NET`, unreferenced).
- ❌ Rapier presentation physics, entity-selective (`@dimforge/rapier2d` not installed).
- ❌ Shared-memory `HEAPF32` zero-copy render bridge + `TransformSnapshot` + JS interpolation.
- ❌ glTF importer + skeletal ECS components (`SkeletonComponent`/`AnimationPlayerComponent`).

## Known Issues

| Issue | Impact | Notes |
| --- | --- | --- |
| `npx tsc --noEmit` fails | Type-checking unavailable | `vite.config.ts` lacks Node type definitions |
| Standalone `.js` scripts in ESM dirs | `require()` fails with `ReferenceError` | `src/Game.Tests.UI/package.json` declares `"type": "module"`; use `import` syntax or `.cjs` extension. Also: `{ shell: true }` spawns orphan .NET processes; use `taskkill /pid /f /t` on Windows |
| Full-solution MAUI build | OS/workload dependent | Requires .NET MAUI workloads; TFMs vary per host OS |
| Static-web-asset races | Flaky parallel builds | Never run concurrent `dotnet` commands |

## Milestones

1. ✅ Scaffold + docs + memory bank baseline (2026-08-13)
2. ✅ Documentation overhaul for agentic development: codebase-truth, ADR infra, openspec config, doc repair (2026-08-13)
3. ⬜ Engine MVP: commands/events + one rendered sprite driven by a C# tick
4. ⬜ ECS-based world with push-delta rendering
5. ⬜ Save/load + MAUI parity
6. ⬜ Authoritative server host
