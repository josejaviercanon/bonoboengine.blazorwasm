# Agent Guide

## Repository Shape

- `bonoboWebGame.slnx` is the solution; projects target .NET 10.
- `src/Game.Engine` is a plain C# class library. Keep engine logic independent of UI and platform code. It hosts the Arch ECS (`Game.Engine.ECS`: components, `[Query]` systems, `EcsSimulation`) via vendored `src/Arch` + `src/Arch.Generators` (analyzer only).
- `src/Game.UI` is the shared Razor Class Library. It references `Game.Engine` and owns shared Razor components plus PixiJS assets.
- `src/Game.Web` is the Blazor Web App host. It uses **static SSR only** (no Interactive Server, no SignalR circuit, no reconnect modal) and discovers shared RCL routes via `AddAdditionalAssemblies` in `Program.cs` plus the `Router` `AdditionalAssemblies` in `Components/Routes.razor`. PixiJS is bootstrapped client-side from an inline `load`-event script in `Components/App.razor`, which reads the engine payload from `#pixi-viewport[data-message]`.
- `src/Game.Tests` is the xUnit v3 test project (determinism self-checks, ECS unit tests, snapshot shape). `src/Game.Tests.Aot` is the TUnit test project (AOT/trim pattern checks over the `Game.Engine` closure). Both are in the solution and run under the Microsoft.Testing.Platform runner opted in via root `global.json` — do not delete that file or `dotnet test` misbehaves on .NET 10.
- `src/Game.Tests.UI` is the Node/TypeScript Playwright E2E suite against the real `Game.Web` host. Not a `.csproj` — run from its folder via npm. Uses installed Chrome (`channel: 'chrome'`); config boots the host on port 5902. See `docs/testing-ui-E2E/index.md`.
- `src/Game.Maui` is the .NET MAUI Blazor Hybrid host. It targets Android by default, plus iOS, Mac Catalyst, and Windows when supported by the OS/workloads. **Currently commented out of `bonoboWebGame.slnx` (temporary web-only solution build for speed)** — build it directly with `dotnet build src/Game.Maui/Game.Maui.csproj` when doing native app work.
- `src/Box2D.NET` and `src/BrainAI` are **vendored** C# libraries (physics; pathfinding/AI) but are **not referenced** by `Game.Engine.csproj` yet — treat as target dependencies, not active. `src/Temp/` holds upstream samples/demos (`Box2D.NET.Samples`, `Box2D.NET.Shared`, `BrainAI.Demo`, `ECS-example`) plus the source topology doc — not part of the build/solution.
- The PixiJS v8 ecosystem (`pixi.js`, `@pixi/ui`, `@pixi/sound`, `@pixi/tilemap`, `pixi-viewport`, `pixi-filters`, `@spd789562/particle-emitter`) is declared in `src/Game.UI/package.json`. **Rapier (`@dimforge/rapier2d`) it is optional presentation-physics only (ADR-002).

## Agent References

- `docs/pixijs-documentation-for-llms/pixyjs.md` — complete vendored PixiJS v8 API reference (llm.txt format). Consult it when writing or verifying any PixiJS code; prefer its API facts over memory or generic web knowledge.
- `net-microsoft-documentation` MCP server — official, up-to-date Microsoft Learn docs for .NET, ASP.NET Core, Blazor, and MAUI. Use it for framework/API verification.
Summary of the scope an agent can search using this server:

  1. Programming Languages & RuntimesCore Languages: TypeScript, JavaScript, C#, F#, VB.NET, C++, Python, Java, Rust, PowerShell, Go.Frameworks & Runtimes: .NET Core / .NET 8+, ASP.NET Core, Node.js, React, Angular, Vue, Blazor, MAUI, WPF, WinUI.
  2. Microsoft Developer Tools & SDKsIDEs & Code Editors: Visual Studio, Visual Studio Code, Visual Studio Code Extensions (Copilot, Azure Tools).
  3. CLI & Command Line: Azure CLI (az), Azure Developer CLI (azd), PowerShell modules, Windows Terminal, WSL.
  4. SDKs: Azure SDKs across languages (Python, TypeScript, .NET, Java), Model Context Protocol (MCP) SDKs.

- `docs/2d-games` and `docs/game-development` — game architecture and gamedev workflow references (see `docs/index.md`).
- `docs/architecture/topology.md` — engine topology deep-dive (Implemented vs Target): three-layer runtime, WASM→JS bridge, physics, skeletal pipelines, domain matrix, ecosystem matrix, implementation status.
- `docs/adr/` — Architecture Decision Records (ADR-001 topology … ADR-006 domain matrix). Read before changing cross-boundary, physics, render-bridge, or asset-pipeline decisions.
- `.agents/skills/static-ssr-snapshot-bridge/SKILL.md` — mandatory rules for the `Game.Web` host + C#↔JS boundary (static SSR only, batched-snapshot SSE/POST bridge, strict TS payload types). Supersedes any generic "Blazor WASM + Vite + CustomElements" skill for this repo; load it before touching hosting, bootstrap, the render bridge, or Frontend interop.

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
dotnet test          # Game.Tests (xUnit v3) + Game.Tests.Aot (TUnit); MTP runner via global.json
```

Run from `src/Game.UI`:

```powershell
npm ci
npm run build
npm run typecheck # scoped tsconfig.app.json (Frontend) + tsconfig.node.json (vite.config.ts)
```

Run Playwright E2E from `src/Game.Tests.UI` (Node project; needs `npm ci` first):

```powershell
npm ci
npx playwright test        # boots Game.Web on port 5902, uses installed Chrome (channel: 'chrome')
npm run typecheck
```

For exploratory agent-driven browser work use the `playwright-cli` skill with Chrome: `playwright-cli open <url> --browser=chrome`. A Playwright MCP server is NOT needed — skills + playwright-cli + the checked-in Playwright suite cover this repo (verdict + rationale in `docs/testing-ui-E2E/index.md`).

Run web host from repository root:

```powershell
dotnet watch --project src/Game.Web
```

`Game.UI` scripts build Vite JavaScript first, then Tailwind CSS. Vite reads `Frontend/game.ts` and writes generated files to `src/Game.UI/wwwroot/dist`; do not hand-edit generated output. `npm run watch:js` and `npm run watch:css` are separate long-running watchers.

MAUI builds require .NET MAUI workloads. Platform-specific target frameworks may make full-solution builds depend on host OS and installed workloads.

## Verification Notes

- Test projects: `Game.Tests` (xUnit v3), `Game.Tests.Aot` (TUnit), `Game.Tests.UI` (Playwright, Node-only, not in the solution). Full guide: `docs/testing-ui-E2E/index.md`. `Game.Maui` is temporarily commented out of the solution (web-only builds for speed).
- After touching `Game.UI` RCL assets, kill any running `Game.Web.exe` before rebuilding. Static-asset 500s (`_content/Game.UI/dist/*`) = stale/raced `bin`/`obj` static-web-asset output; fix by killing the host and rebuilding (delete `src/Game.Web/bin`+`obj` if it persists).
- `bin/`, `obj/`, `node_modules/`, and other build output are ignored. Do not commit them.
- Trust `.csproj`, `.slnx`, `package.json`, and executable build output over setup prose in `README.md`.
- `docs/index.md` describe architecture; `docs/ai-agents/codebase-truth.md` holds verified API facts; record significant decisions in `docs/adr/`.
