# Tech Context

## Stack

| Area | Technology |
| --- | --- |
| Language / runtime | C# 14 / .NET 10 (`net10.0`, `net10.0-android` etc.) |
| Solution format | `bonoboWebGame.slnx` (XML solution) |
| Web host | Blazor Web App, **static SSR only** (no Interactive Server/SignalR) + SSE `/api/ecs/stream` |
| Native host | .NET MAUI Blazor Hybrid (Android default; iOS/MacCatalyst/Windows conditional) |
| Shared UI | Razor Class Library + `Microsoft.AspNetCore.Components.Web` 10.0.10 |
| 2D rendering | PixiJS ^8.19.0 (WebGL/WebGPU) |
| JS build | Vite ^8.2.1 (IIFE lib bundle, sourcemaps) |
| Language (frontend) | TypeScript ^7.0.2 |
| CSS | Tailwind CSS v4 via `@tailwindcss/cli` ^4.3.3, PostCSS, autoprefixer |
| ECS (planned) | Arch ECS |
| Serialization (planned) | System.Text.Json source generators (AOT-safe) |
| Authoritative physics (target) | Box2D.NET (vendored `src/Box2D.NET`, **not referenced**) |
| Presentation physics (target) | Rapier `@dimforge/rapier2d` (**not installed**) |
| Pathfinding/AI (target) | BrainAI (vendored `src/BrainAI`, **not referenced**) |

## Projects

- `src/Game.Engine/Game.Engine.csproj` — `Microsoft.NET.Sdk`, net10.0, nullable+implicit
  usings. Refs `Arch` + `Arch.Generators` (analyzer) only. **Implemented:** `EcsSimulation`
  (60 Hz Arch ECS, `MovementSystem`/`ColorSystem`, batched `EcsRenderSignal` @1s, SSR
  `Snapshot()`); `ECS/Components.cs` (`Position`/`Velocity`/`SpriteColor`/`RenderId`).
- `src/Game.UI/Game.UI.csproj` — `Microsoft.NET.Sdk.Razor`, net10.0; refs Game.Engine;
  `<SupportedPlatform Include="browser" />`; Vite-bundle precompression workaround target.
- `src/Game.Web/Game.Web.csproj` — `Microsoft.NET.Sdk.Web`, net10.0; refs Game.UI;
  `BlazorDisableThrowNavigationException=true`. `Program.cs` = **static SSR only**
  (`AddRazorComponents()`, `AddSingleton<EcsSimulation>()`, `UseAntiforgery()`,
  `MapStaticAssets()`, `MapRazorComponents<App>().AddAdditionalAssemblies(...)`); `GET
  /api/ecs/stream` SSE pushes `event: sprite-move` `SpriteState[]`. No Interactive Server,
  no SignalR circuit.
- `src/Game.Maui/Game.Maui.csproj` — `Microsoft.NET.Sdk.Razor` + `UseMaui`, `OutputType=Exe`,
  TFMs conditioned on host OS (Windows adds `net10.0-windows10.0.19041.0`); XAML source-gen
  inflator; unpackaged on Windows.

## Commands (from repo root unless noted)

```powershell
dotnet build bonoboWebGame.slnx     # build all
dotnet test                          # Game.Tests + Game.Tests.Aot (Playwright suite runs from src/Game.Tests.UI)
dotnet watch --project src/Game.Web  # run web host
```

```powershell
cd src/Game.UI
npm ci
npm run build        # vite build, then Tailwind CLI → wwwroot/dist
npm run watch:js     # separate long-running watcher
npm run watch:css    # separate long-running watcher
npx tsc --noEmit     # KNOWN FAILURE: vite.config.ts lacks Node type definitions
```

## Environment Notes

- Windows dev machine; MAUI targets expand per-OS; full-solution builds may require MAUI
  workloads installed.
- `bin/`, `obj/`, `node_modules/`, `wwwroot/dist` output are git-ignored — never commit.
- Frontend must be built before .NET build; avoid concurrent `dotnet` invocations.

## Key Files

- `src/Game.UI/Frontend/game.ts` — PixiJS bootstrap (`initGame`, exposed on `window`).
- `src/Game.UI/Frontend/app.css` — Tailwind v4 import + `@theme` game variables.
- `src/Game.UI/vite.config.ts` — IIFE lib entry `Frontend/game.ts` → `wwwroot/dist`.
- `src/Game.UI/GameView.razor` — `@page "/"`, full-viewport container + HUD, calls `initGame`.
- `src/Game.Web/Components/Routes.razor` — router with `AdditionalAssemblies` for the RCL.
- `docs/index.md` — architecture source of truth. `AGENTS.md` — agent/build rules.
