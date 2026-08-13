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
      Interactive Server)   Android/iOS/MacCat/Win)
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

- PixiJS sits idle until C# pushes a delta; Blazor forwards flat payloads via
  `IJSRuntime.InvokeVoidAsync`. ❌ No per-frame polling, ❌ no full-state JSON per frame.
- JS entry: `Frontend/game.ts` exports `initGame(containerId)`, also exposed as
  `window.initGame`; `GameView.razor` calls it in `OnAfterRenderAsync(firstRender)`.
- Vite builds an **IIFE** bundle (`game-bundle.iife.js`) into `wwwroot/dist` for direct
  browser execution. `game.ts` waits ~50ms for layout, then falls back to 100vw/100vh if
  the container measures 0px (BlazorWebView timing quirk).

## ECS Rules (planned, from `docs/index.md`)

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
