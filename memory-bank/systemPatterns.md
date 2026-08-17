# System Patterns

## Layered Architecture

```
┌─────────────────────────────────────────────────────────┐
│ 1. SHARED CORE ENGINE  (src/Game.Engine — net10.0 lib)  │
│    Entities, grid, stats · simulation ticks ·           │
│    command/event system · pure C#, AOT-friendly         │
└───────────────────────────┬─────────────────────────────┘
                            │ events (delta push)
        ┌───────────────────┴───────────────────┐
        │ 2. PRESENTATION BRIDGE                │ 3. FUTURE SERVER HOST
        │    src/Game.UI (shared RCL)           │    ASP.NET Core + WS/SignalR,
        │    Blazor shell + IJSRuntime → PixiJS │    runs same Game.Engine
        └───────┬───────────────────┬───────────┘
          src/Game.Web        src/Game.Maui
     (Blazor Web App,      (MAUI Blazor Hybrid,
      Static SSR + SSE)   Android/iOS/MacCat/Win)
```

## Dependency Rules

- `Game.Engine` references **nothing** UI/platform. Keep it pure.
- `Game.UI` → `Game.Engine`; hosts (`Game.Web`, `Game.Maui`) → `Game.UI`.
- Hosts discover shared routes via `AdditionalAssemblies`
  (`Game.Web/Components/Routes.razor` uses `typeof(GameView).Assembly`).

## Command/Event Boundary (the key pattern)

- Inputs enter the engine only as **commands** (`ProcessCommand(MoveCommand …)`).
- State changes leave the engine only as **events** (`event Action<EntityMovedEvent> …`).
- The network transition later = routing the same messages over WebSockets instead of an
  in-memory bridge. Client UI treats network events identically to local ones.

## Rendering Pattern

- **Current bridge (static SSR):** C# emits a batched `EcsRenderSignal`; the web host pushes it to PixiJS via the `GET /api/ecs/stream` SSE endpoint (`event: sprite-move`, `SpriteState[]` JSON, 1 s throttle) — no `IJSRuntime` on the web host (no interactivity). ❌ No per-frame polling, ❌ no full-state JSON per frame.
- **Target bridge (ADR-003):** batched `TransformSnapshot` (pos + velocity + rotation + tick) → pinned shared-memory `HEAPF32` `Float32Array` view; client interpolates `P_render = P_prev + (P_curr − P_prev) × α`.
- JS entry: `Frontend/game.ts` exports `initGame(containerId)`, also exposed as
  `window.initGame`; `GameView.razor` calls it in `OnAfterRenderAsync(firstRender)`.
- Vite builds an **IIFE** bundle (`game-bundle.iife.js`) into `wwwroot/dist` for direct
  browser execution. `game.ts` waits ~50ms for layout, then falls back to 100vw/100vh if
  the container measures 0px (BlazorWebView timing quirk).

## Three-Layer Runtime Topology (ADR-001…ADR-006)

Within the Presentation Bridge, the runtime splits into three layers (see `docs/architecture/topology.md`):

```
C# AUTHORITATIVE WORLD   ECS + Box2D.NET (target): gameplay physics, collisions, rules
        │  fixed timestep → RenderSnapshot (Tick, Pos, Velocity)
        ▼
PRESENTATION WORLD       lightweight interpolation (default) + optional Rapier 2D
        │  display-Hz visual
        ▼
PIXIJS v8                sprites, containers, animation, camera, particles, GPU
```

- C# is the sole authority; PixiJS is a pure mirror (interpolation + optional presentation physics).
- Never move simulation back-and-forth through JS interop per frame; cross via batched render snapshots only.
- Box2D.NET = authoritative physics (vendored `src/Box2D.NET`, referenced, used by `AsteroidsSimulation`). Rapier (`@dimforge/rapier2d`, installed) = optional JS-side presentation physics, entity-selective (`PresentationPhysicsComponent.Mode`).
- glTF (`.glb`) = asset contract, not ECS architecture; a skeletal character = one entity with contiguous-array `SkeletonComponent`; the animation state machine belongs to the ECS.

## ECS Rules (implemented in `EcsSimulation`; rules below)

- Components: zero-logic public `struct`s only — no classes, no methods.
- Systems: stateless, iterate via `QueryDescription` / `Arch.Systems.BaseSystem`.
- Structural changes (create/destroy/add/remove) via command buffers or outside query
  loops — never mutate structure mid-query.

## Build Pipeline Pattern

- `npm run build` in `src/Game.UI` = Vite JS build **then** Tailwind CSS build; outputs to
  `src/Game.UI/wwwroot/dist` (generated — never hand-edit).
- `Game.UI.csproj` contains an MSBuild workaround (`ExcludeViteBundleFromCompression`
  target) removing `game-bundle.iife.js` from static-web-asset precompression to avoid
  MSB4018 on .NET 10. Keep it.
- Run frontend assets **before** `dotnet build`; never run multiple `dotnet` commands
  concurrently (static-web-asset compression can race).
