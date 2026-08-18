# Codebase Truth — Verified Facts

Every fact below was verified against `.csproj`, `package.json`, `vite.config.ts`, `tsconfig.json`, and source files on 2026-08-13. When code and prose disagree, this file wins. Trust these files over `README.md` setup prose:

- `bonoboWebGame.slnx` (XML solution format — not `.sln`)
- `src/*/*.csproj`
- `src/Game.UI/package.json`

## Projects

| Project | SDK | Target(s) | References | Notable flags |
| --- | --- | --- | --- | --- |
| `src/Game.Engine` | `Microsoft.NET.Sdk` | `net10.0` | `Arch`, `Arch.Generators` (as analyzer), `Box2D.NET` (vendored, authoritative physics — ADR-002) | nullable + implicit usings; `GameSimulation.cs` + `RenderMessageEvent(string)` — `PublishHello()` raises `OnRenderMessage` with "Hello world to PixiJs Gaming!"; `ECS/` namespace `Game.Engine.ECS` — `[Component]` structs (`Position`, `Velocity`, `SpriteColor`, `RenderId`), `[Query]`-generated systems (`MovementSystem`, `ColorSystem`), `EcsSimulation` singleton service (60 Hz tick, batched `EcsRenderSignal` emitted at 1 s throttle, `Snapshot()` for SSR); `ECS/Asteroids/` — `AsteroidsSimulation` (Box2D.NET world per sim, contact events, single `AsteroidsGameSystem`), plus `SnakeSimulation`, `TetrisSimulation`, `BreakoutSimulation` |
| `src/Arch` | `Microsoft.NET.Sdk` | `net10.0` | vendored Arch ECS 2.1.0; `Collections.Pooled`, `MessagePack`, `Utf8Json`, `ZeroAllocJobScheduler` | no nullability; heavy template T4 sources; vendored warnings expected |
| `src/Arch.Generators` | `Microsoft.NET.Sdk` (netstandard2.0) | analyzer assembly | Roslyn 5.6.0 only | links `Arch.EventBus` / `Arch.Systems.SourceGenerator` / `Arch.AOT.SourceGenerator` sources; must NOT be referenced as a normal library (Roslyn can't resolve Arch.dll deps) |
| `src/Game.UI` | `Microsoft.NET.Sdk.Razor` | `net10.0` | `Game.Engine` | `<SupportedPlatform Include="browser" />`; `Microsoft.AspNetCore.Components.Web` 10.0.10; MSB4018 workaround target |
| `src/Game.Web` | `Microsoft.NET.Sdk.Web` | `net10.0` | `Game.UI` | `BlazorDisableThrowNavigationException=true` |
| `src/Game.Maui` | `Microsoft.NET.Sdk.Razor` + `UseMaui` | `net10.0-android`; adds `net10.0-ios`, `net10.0-maccatalyst` when not Linux; adds `net10.0-windows10.0.19041.0` on Windows | `Game.UI` | `OutputType=Exe`, `MauiXamlInflator=SourceGen`, `WindowsPackageType=None` (unpackaged), `<SingleProject>true` |

## Game.UI frontend pipeline

- Entry: `src/Game.UI/Frontend/game.ts` — exports `initGame(containerId)`, `renderText(message)`, `renderScene(message)`; also binds `(window as any).initGame` / `.renderText` / `.renderScene`.
  - Logs every pipeline step under the `[pixi-debug]` prefix (bundle load, container lookup/size, `app.init`, canvas append, text set, centering). `initGame` waits 50 ms for layout, then forces `100vw`/`100vh` if container measures 0×0; creates `Application` (`resizeTo`, `backgroundAlpha: 0`, `antialias`, `hello`). `renderText` creates/updates a centered PixiJS `Text` from an engine-driven payload and re-centers on window resize. `renderScene` dispatches the full parsed payload (not `{}`) to the registered scene builder — ECS scenes read `sprites`/`streamUrl` from it.
- Scenes live in `src/Game.UI/Frontend/scenes/`; `index.ts` `sceneRegistry` keys MUST match `ExamplesCatalog` ids. ECS scenario: `ecsSprites.ts` — sprites from SSR initial state, then moves each `sprite-move` SSE event (1/s server throttle). Games: `snake.ts`, `tetris.ts`, `breakout.ts`, `asteroids.ts`. `snake.ts` consumes authoritative `SnakeSpriteState` snapshots, interpolates previous/current positions at display Hz, and renders explicit good/bad food kinds. `SnakeSimulation` owns deadly-food fall and spawns replacement food when the normal food becomes bad. `asteroids.ts` is the reference pattern for the presentation layer: prev/curr interpolation (ADR-003), `@spd789562/particle-emitter` bursts (explosions) + persistent thrust flame on one `ParticleContainer`/texture source, Rapier debris field (JS-side only, ADR-002/005), `GlowFilter` from `pixi-filters`, `@pixi/sound` wavs from `wwwroot/audio/asteroids-*.wav`. No scene-destroy hook exists; the EventSource closes on `beforeunload` and logs transient errors for browser reconnect.
- CSS entry: `src/Game.UI/Frontend/app.css` — `@import "tailwindcss";` + `@theme` block (`--color-hud-bg`, `--color-mana`, `--font-game`).
- `vite.config.ts`: lib mode, IIFE format, entry `Frontend/game.ts`, name `GameViewport`, `fileName: 'game-bundle'`, `outDir: wwwroot/dist`, `emptyOutDir: true`, `sourcemap: true`.
- TypeScript uses the scoped composite model: root `tsconfig.json` references `tsconfig.app.json` (Frontend/**/*.ts, DOM libs) + `tsconfig.node.json` (vite.config.ts, `types: ["node"]`). Dev deps include `@types/node`. `npm run typecheck` = `tsc -b` and **passes**.
- `package.json` deps: `pixi.js ^8.19.0`. Dev deps: `vite ^8.2.1`, `typescript ^7.0.2`, `tailwindcss ^4.3.3`, `@tailwindcss/cli ^4.3.3`, `postcss ^8.5.26`, `autoprefixer ^10.5.4`, `@types/node ^24`.
- Scripts: `build:js` = `vite build`; `build:css` = `npx @tailwindcss/cli -i ./Frontend/app.css -o ./wwwroot/dist/app.css --minify`; `build` = js then css; `typecheck` = `tsc -b`; `watch:js` / `watch:css` are separate long-running watchers.

## MSB4018 workaround (`Game.UI.csproj`)

`.NET 10` static-web-asset precompression breaks on the Vite IIFE bundle. The csproj hooks `DiscoverPrecompressedAssetsDependsOn` and the `ExcludeViteBundleFromCompression` target removes any static web asset named `game-bundle.iife.js` before compression. Do not remove this target.

## Vendored libraries

- `src/Box2D.NET/Box2D.NET.csproj` — vendored C# 2D physics (ikpil port of Box2D v3). **Referenced** by `Game.Engine.csproj`; used by `AsteroidsSimulation` as the authoritative physics world (ADR-002). Per-world `b2CreateWorld` (gravity 0, `workerCount = 1` for determinism); shapes carry `enableContactEvents`; entity ids live in **body** `userData` (read back via `b2Body_GetUserData`); contact begin events resolve into game events. Debug builds enable Box2D asserts (`ENABLED` define) — don't create worlds from parallel threads (static world table).
- `src/BrainAI/BrainAI.csproj` — vendored C# pathfinding/AI (with `AI/`, `InfluenceMap/`, `Pathfinding/`, `Simulations/` READMEs). **Not referenced** by `Game.Engine.csproj`.
- `src/Temp/` — upstream samples/demos, **not** part of the solution/build: `Box2D.NET.Samples`, `Box2D.NET.Shared`, `BrainAI.Demo` (Godot `.tscn`/`project.godot`), `ECS-example`, `AsteroidsWasm` (reference Asteroids game — old Blazor WASM, git-ignored, own `.git`), and the source topology doc `Architectural Topology C# ECS Engine in .NET MAUI Hybrid Blazor WebAssembly.md`.

## Frontend dependencies (`src/Game.UI/package.json`)

Dependencies (all present): `pixi.js` ^8.19.0, `@pixi/ui` ^2.3.2, `@pixi/sound` ^6.0.1, `@pixi/tilemap` ^5.0.2, `pixi-viewport` ^6.0.3, `pixi-filters` ^6.1.5, `@spd789562/particle-emitter` ^1.0.2, `@dimforge/rapier2d` ^0.20.0 (presentation physics, JS-side only — ADR-002/005). Both `src/Game.UI/package.json` and `src/Game.Tests.UI/package.json` declare `"type": "module"` — all `.js` files in those directories parse as ESM; use `import` syntax (or `.cjs` for CommonJS).

## Render bridge (current)

- `GET /api/ecs/stream` SSE (`text/event-stream`) pushes `event: sprite-move` with batched `SpriteState[]` JSON (`Id, X, Y, R, G, B`) — **no velocity/rotation/tick**. Throttled to one signal per second (`EcsSimulation.SignalIntervalSeconds = 1.0`). Target: `TransformSnapshot` with kinematic data + pinned shared-memory `HEAPF32` transfer (ADR-003). No shared-memory/zero-copy path exists yet.

## Hosts

- `Game.Web/Program.cs`: **static SSR only** — no Interactive Server, no SignalR circuit, no reconnect modal. `AddRazorComponents()`; singletons for `EcsSimulation`, `SnakeSimulation`, `TetrisSimulation`, `BreakoutSimulation`, `AsteroidsSimulation` (60 Hz Arch ECS sims, batched render signals); `UseStatusCodePagesWithReExecute("/not-found")`; `UseHttpsRedirection`; `UseAntiforgery()` (**required** — Razor Components endpoints carry antiforgery metadata even for static SSR); `MapStaticAssets()`; `MapRazorComponents<App>().AddAdditionalAssemblies(typeof(GameView).Assembly).AddAdditionalAssemblies(typeof(ExamplesHome).Assembly)`. **SSE endpoints** (`text/event-stream`, one per game): `GET /api/ecs/stream` (`event: sprite-move`), `/api/snake/stream` (`snake-move`), `/api/tetris/stream` (`tetris-move`), `/api/breakout/stream` (`breakout-move`), `/api/asteroids/stream` (`asteroids-move`) — subscribe the sim's `OnRenderSignal`, push batched JSON, unsubscribe on abort. **Input POSTs** (client suggests, C# validates): `/api/{snake,tetris,breakout,asteroids}/input`, plus `/start` and `/restart` per game. Snake has no food-position POST: C# owns deadly-food movement. Endpoints live on the raw ASP.NET Core pipeline, not Blazor.
  - **Required:** RCL routes under static SSR only resolve with `AddAdditionalAssemblies` on `MapRazorComponents`; the `Router` `AdditionalAssemblies` parameter alone is not enough for direct HTTP requests.
- `Game.Web/Components/App.razor`: renders `<Routes />` + `game-bundle.iife.js`, then an inline `load`-event script that reads `#pixi-viewport[data-message]`, calls `initGame("pixi-viewport")`, then `renderText(message)` — all with `[pixi-debug]` console logs. **No `ReconnectModal`, no `<ImportMap />`, no `blazor.web.js`** — the served HTML contains zero `_framework` references. (Interactive Server leftovers `Counter.razor`/`Weather.razor` deleted; `Home.razor` deleted earlier — it shadowed GameView's `/` route.)
- `Game.Web/Components/Routes.razor`: `<Router AppAssembly="typeof(Program).Assembly" AdditionalAssemblies="new[] { typeof(GameView).Assembly }">` — discovers shared RCL routes.
- `Game.Maui/MainPage.xaml`: `BlazorWebView` with `RootComponent` → `Game.Maui.Components.Routes`.

## Key UI components

- `src/Game.UI/GameView.razor`: `@page "/"`, full-viewport fixed container (`position: fixed; inset 0; background #020617`), top HUD bar, `#pixi-viewport` div carrying the engine payload in `data-message`. Static SSR flow: `OnInitialized` subscribes `_simulation.OnRenderMessage` and calls `PublishHello()`; `HandleRenderMessage` stores `ev.Message` into `Message` (rendered into `data-message`); the client-side `load` script picks it up and pushes it to PixiJS. `Dispose` unsubscribes. **No `IJSRuntime` calls** (no interactivity on the web host). Uses inline styles — Tailwind classes not used yet.
- Template leftovers still present (not production code):
  - `Game.UI`: `Component1.razor`, `Component1.razor.css`, `ExampleJsInterop.cs`, `wwwroot/exampleJsInterop.js`.
  - `Game.Web`: Bootstrap lib assets.
  - `Game.Maui`: `Components/Pages/Counter.razor`, `Weather.razor`, `Home.razor`.

## Build commands

From repo root:

```powershell
dotnet build bonoboWebGame.slnx
dotnet test          # Game.Tests (xUnit v3) + Game.Tests.Aot (TUnit); needs global.json MTP opt-in
dotnet watch --project src/Game.Web
```

From `src/Game.UI` (build frontend first; never run two `dotnet` commands concurrently — static-web-asset compression races):

```powershell
npm ci
npm run build
npx tsc --noEmit    # KNOWN FAILURE: vite.config.ts lacks Node type definitions
```

## Environment constraints

- MAUI builds require .NET MAUI workloads; platform TFMs vary by host OS (Linux excludes iOS/MacCatalyst; Windows adds `net10.0-windows10.0.19041.0`).
- `bin/`, `obj/`, `node_modules/`, and `wwwroot/dist` output are git-ignored — never commit.
