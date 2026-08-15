# Agent Guide

## Repository Shape

- `bonoboWebGame.slnx` is the solution; projects target .NET 10.
- `src/Game.Engine` is a plain C# class library. Keep engine logic independent of UI and platform code. It hosts the Arch ECS (`Game.Engine.ECS`: components, `[Query]` systems, `EcsSimulation`) via vendored `src/Arch` + `src/Arch.Generators` (analyzer only).
- `src/Game.UI` is the shared Razor Class Library. It references `Game.Engine` and owns shared Razor components plus PixiJS assets.
- `src/Game.Web` is the Blazor Web App host. It uses **static SSR only** (no Interactive Server, no SignalR circuit, no reconnect modal) and discovers shared RCL routes via `AddAdditionalAssemblies` in `Program.cs` plus the `Router` `AdditionalAssemblies` in `Components/Routes.razor`. PixiJS is bootstrapped client-side from an inline `load`-event script in `Components/App.razor`, which reads the engine payload from `#pixi-viewport[data-message]`.
- `src/Game.Maui` is the .NET MAUI Blazor Hybrid host. It targets Android by default, plus iOS, Mac Catalyst, and Windows when supported by the OS/workloads.
- `src/Box2D.NET` and `src/BrainAI` are **vendored** C# libraries (physics; pathfinding/AI) but are **not referenced** by `Game.Engine.csproj` yet — treat as target dependencies, not active. `src/Temp/` holds upstream samples/demos (`Box2D.NET.Samples`, `Box2D.NET.Shared`, `BrainAI.Demo`, `ECS-example`) plus the source topology doc — not part of the build/solution.
- The PixiJS v8 ecosystem (`pixi.js`, `@pixi/ui`, `@pixi/sound`, `@pixi/tilemap`, `pixi-viewport`, `pixi-filters`, `@spd789562/particle-emitter`) is declared in `src/Game.UI/package.json`. **Rapier (`@dimforge/rapier2d`) it is optional presentation-physics only (ADR-002).

## Agent References

- `docs/pixijs-documentation-for-llms/pixyjs.md` — complete vendored PixiJS v8 API reference (llm.txt format). Consult it when writing or verifying any PixiJS code; prefer its API facts over memory or generic web knowledge.
- `net-microsoft-documentation` MCP server — official, up-to-date Microsoft Learn docs for .NET, ASP.NET Core, Blazor, and MAUI. Use it for framework/API verification.
- `docs/2d-games` and `docs/game-development` — game architecture and gamedev workflow references (see `docs/index.md`).
- `docs/architecture/topology.md` — engine topology deep-dive (Implemented vs Target): three-layer runtime, WASM→JS bridge, physics, skeletal pipelines, domain matrix, ecosystem matrix, implementation status.
- `docs/adr/` — Architecture Decision Records (ADR-001 topology … ADR-006 domain matrix). Read before changing cross-boundary, physics, render-bridge, or asset-pipeline decisions.

## Architectural Guardrails

These rules govern code that crosses the C#↔JS boundary or touches the simulation/presentation split. Backed by ADR-001…ADR-006 and `docs/architecture/topology.md`.

- **C# is the sole authoritative simulation.** Never run authoritative physics/logic in JS. Never implement the same authority in both layers (prevents desync/rollback). (ADR-001, ADR-006)
- **Cross the boundary via batched render snapshots, not per-entity per-frame interop.** The boundary concept is "the simulation produced a render snapshot," not "an entity moved." Never move simulation back-and-forth through JS interop every frame. (ADR-003)
- **Snapshots must carry temporal context** (prev+current position, velocity, rotation, tick) so the client can interpolate at display Hz. Current `SpriteState` lacks these — extending toward `TransformSnapshot` is the first bridge task. (ADR-003)
- **Keep any JS-side physics world (Rapier) resident** in JS/WASM; feed it snapshots at discrete boundaries. Use Rapier only for genuine visual dynamics (capes, ropes, ragdolls, debris); use cheap `lerp`/`slerp`/`spring` for plain interpolation. (ADR-002, ADR-005)
- **Box2D.NET is the authoritative physics engine** (vendored `src/Box2D.NET`, target — not yet wired into `Game.Engine`). Rapier is presentation-only, never authoritative. (ADR-002)
- **glTF (`.glb`) is the asset contract, not the ECS architecture.** Don't create one entity per glTF node; use contiguous arrays in `SkeletonComponent`. The animation state machine belongs to the ECS, not glTF. Authoring (AI+Blender) is an offline content pipeline, not part of the game runtime. (ADR-004)
- **Presentation-side work** (interpolation, camera smoothing, secondary motion, particles, animation blending from velocity) lives in PixiJS; C# only dictates root entity state. (ADR-005, ADR-006)
- **When adding packages:** the PixiJS ecosystem is already in `src/Game.UI/package.json`; do not duplicate. Rapier is the only planned addition, and only when presentation physics is actually needed.

## Commands

Build frontend assets before .NET commands. Do not run multiple `dotnet` commands concurrently; static-web-asset compression can race.

Run from repository root:

```powershell
dotnet build bonoboWebGame.slnx
dotnet test
```

Run from `src/Game.UI`:

```powershell
npm ci
npm run build
npm run typecheck # scoped tsconfig.app.json (Frontend) + tsconfig.node.json (vite.config.ts)
```

Run web host from repository root:

```powershell
dotnet watch --project src/Game.Web
```

`Game.UI` scripts build Vite JavaScript first, then Tailwind CSS. Vite reads `Frontend/game.ts` and writes generated files to `src/Game.UI/wwwroot/dist`; do not hand-edit generated output. `npm run watch:js` and `npm run watch:css` are separate long-running watchers.

MAUI builds require .NET MAUI workloads. Platform-specific target frameworks may make full-solution builds depend on host OS and installed workloads.

## Verification Notes

- No test projects currently exist; `dotnet test` may report no tests while still validating the solution.
- `bin/`, `obj/`, `node_modules/`, and other build output are ignored. Do not commit them.
- Trust `.csproj`, `.slnx`, `package.json`, and executable build output over setup prose in `README.md`.
- `docs/index.md` describe architecture; `docs/ai-agents/codebase-truth.md` holds verified API facts; record significant decisions in `docs/adr/`.
