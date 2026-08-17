# Bonobo Blazorwasm Engine — The Stack

**This file is the source of truth for the engine's stack, architecture, and project structure.** It is maintained to match the repository; when code and prose disagree, this file (kept current) wins. Verified API facts live in `docs/ai-agents/codebase-truth.md`; build state and agent workflow live in `AGENTS.md`. `README.md` mirrors this file for GitHub visitors.

---

## Mission

Computing your entire game logic inside C#. It allows you to build a single, authoritative simulation engine that runs client-side inside .NET MAUI or Blazor today, and can be dropped directly onto a dedicated .NET Linux server tomorrow for authoritative multiplayer.

To make this architecture work without destroying performance, you must isolate the **Simulation Layer (C#)** from the **Presentation Layer (PixiJS/Tailwind)**.

Here is the architectural blueprint to achieve this zero-duplicate-work setup.

## 🧱 The Authoritative C# Architecture

To ensure your C# code can run both on the client (MVP) and the server (future), your core logic must have zero dependencies on UI libraries, MAUI, or Blazor.

You should split your codebase into three distinct layers:

```
+---------------------------------------------------------------------------------+
|                    1. SHARED CORE ENGINE (C# Class Library)                     |
|  - Holds Game State (Entities, Grid, Stats)     - Runs Simulation Ticks         |
|  - Command/Event System (Inputs, Actions)       - Pure C# Logic (AOT Friendly)  |
+---------------------------------------------------------------------------------+
                                       |
                   +-------------------+-------------------+
                   |                                       |
+--------------------------------------+   +--------------------------------------+
|        2. PRESENTATION BRIDGE        |   |       3. FUTURE SERVER HOSTER        |
|  - Blazor Component Shell            |   |  - ASP.NET Core Minimal API / WebSockets
|  - IJSRuntime Skinny Bridge          |   |  - Runs the exact same Core Engine   |
|  - Maps C# State changes to PixiJS   |   |  - Verifies incoming client commands |
+--------------------------------------+   +--------------------------------------+
```

1. **The Core Simulation Engine (Pure C#)** — a standard .NET Class Library. It knows absolutely nothing about graphics, rendering, or browsers.
   - *State Management:* manages coordinates, stats, pathfinding matrices, and entity maps.
   - *The Deterministic Tick:* runs the Arch ECS systems each fixed step (e.g., `MovementSystem`, `ColorSystem`) and emits one **batched** render signal (`EcsRenderSignal`) per interval — not one event per entity — so the presentation layer mirrors authoritative state without per-frame interop. A `ProcessCommand` command pattern is the planned input boundary (ADR-003).
2. **The Presentation Layer (Blazor + PixiJS + Tailwind)** — a pure mirror of your C# state.
   - *Tailwind UI:* Blazor hooks into C# state to display inventories or menus using standard data-binding.
   - *PixiJS Canvas:* instead of polling C# for positions 60 times a second, PixiJS sits idle until the C# engine emits a batched render signal. The host streams that batched payload to PixiJS (SSE on the web host; `IJSRuntime` on MAUI Hybrid), and PixiJS animates only the sprites that changed.

### ⚠️ The Performance Gold Rule: Avoid JSON Serialization

Polling C# from JavaScript every frame, or serializing the whole state tree per frame, will reduce your game's frame rate down to single digits. You **must** use a **Push-Based Delta Event** approach: the engine emits a batched render signal and the host streams it to PixiJS (SSE on the web host; `IJSRuntime` on MAUI Hybrid).

- ❌ **Bad (Polling):** PixiJS loops at 60fps and calls C# via interop — "Where is everyone right now?" C# serializes 500 characters into JSON and passes it back.
- ✅ **Good (Batched Delta Push):** the C# engine finishes a tick and emits one batched `EcsRenderSignal` (`SpriteState[]`). The web host streams it to PixiJS over SSE (`event: sprite-move`); PixiJS updates only the sprites that changed. (MAUI Hybrid uses `IJSRuntime` for the same batched push.)

**2.1. UI:** keep the presentation layer thin. Use Razor components and Tailwind CSS for menus, inventories, and HUD. Drop a standard HTML5 `<canvas>` inside that Razor view, and use a modular, object-oriented vanilla TypeScript/JavaScript file to initialize PixiJS and map incoming C# events directly to sprites. Bypassing the React wrapper keeps the application simple, clean, and fast.

## 🧬 Engine Topology: Simulation ↔ Presentation ↔ Render

The Authoritative C# Architecture above splits the **Presentation Bridge** into two further layers at runtime, yielding a three-layer topology (ADR-001). C# is the sole authority; PixiJS is a pure mirror that interpolates and may run presentation physics for visual flair only.

```
C# AUTHORITATIVE WORLD          ECS + Box2D.NET (target): gameplay physics, collisions, rules
        │  fixed timestep → RenderSnapshot (Tick, Pos, Velocity)
        ▼
PRESENTATION WORLD              lightweight interpolation (default) + optional Rapier 2D
        │  120/144/240 Hz visual
        ▼
PIXIJS v8                       sprites, containers, animation, camera, particles, GPU
```

- **Never** move simulation back-and-forth through JS interop every frame. Cross the boundary only via batched render snapshots.
- **Domain ownership (ADR-006):** C# owns game rules, collision, gravity, character controllers, deterministic networking. PixiJS owns interpolation, sprite transforms/animation, camera smoothing, secondary motion (cloth/ragdoll), particle physics.
- **Bridge status:** current = SSE `event: sprite-move` with batched `SpriteState[]` JSON (`/api/ecs/stream`); target = pinned shared memory + `HEAPF32` `Float32Array` view + client interpolation `P_render = P_prev + (P_curr − P_prev) × α` (ADR-003).
- **Physics:** Box2D.NET = authoritative (C# ECS loop, vendored at `src/Box2D.NET`, wired into `Game.Engine` and used by `AsteroidsSimulation`); Rapier `@dimforge/rapier2d` = optional presentation physics, entity-selective, used by the asteroids debris field (ADR-002, ADR-005).
- **Skeletal animation:** glTF (`.glb`) is the asset contract, not the ECS architecture — two decoupled pipelines (authoring: AI+Blender→`.glb`; runtime: `.glb`→importer→ECS→PixiJS); the animation state machine belongs to the ECS (ADR-004).

Full matrices (ecosystem integration, implementation status, packages) live in `docs/architecture/topology.md`. Decisions: `docs/adr/` (ADR-001…ADR-006).

## 🛠️ Step-by-Step Blueprint for the MVP

### Step 1: The Authoritative C# ECS Simulation

The authoritative simulation is an Arch ECS world in `src/Game.Engine` (not a `Dictionary` of entities). Components are zero-logic `[Component]` structs; systems are `[Query]`-generated. It ticks at 60 Hz and emits one **batched** `EcsRenderSignal` per second (throttled so the render pipeline isn't flooded). `Snapshot()` returns the initial state for SSR.

```csharp
// src/Game.Engine/ECS/Components.cs — pure-data structs
[Component] public struct Position    { public float X; public float Y; }
[Component] public struct Velocity    { public float X; public float Y; }
[Component] public struct SpriteColor { public byte R; public byte G; public byte B; }
[Component] public struct RenderId    { public int Id; }   // stable id → client sprite

// src/Game.Engine/ECS/EcsSimulation.cs — the authoritative tick
public record struct SpriteState(int Id, float X, float Y, byte R, byte G, byte B);
public sealed record EcsRenderSignal(long Seq, int EntityCount, double TickMs, IReadOnlyList<SpriteState> Sprites);

public sealed class EcsSimulation : IDisposable
{
    public event Action<EcsRenderSignal>? OnRenderSignal;   // batched delta → host
    public IReadOnlyList<SpriteState> Snapshot() { /* …initial SSR state… */ }
    // 60 Hz Timer → MovementSystem + ColorSystem → emits EcsRenderSignal @1s
}
```

No per-entity `EntityMoved` events and no `IJSRuntime` calls from the engine: state leaves the simulation only as a batched render signal (the "Performance Gold Rule"; ADR-003 refines this toward `TransformSnapshot` + shared-memory).

### Step 2: The Static-SSR Host + SSE Bridge

`Game.Web` is **static SSR only** (no Interactive Server, no SignalR circuit). It registers `EcsSimulation` as a singleton, maps the Razor components (discovering shared RCL routes via `AddAdditionalAssemblies`), and exposes one SSE endpoint that streams the batched render signal. No `IJSRuntime` on the web host.

```csharp
// src/Game.Web/Program.cs
builder.Services.AddRazorComponents();
builder.Services.AddSingleton<EcsSimulation>();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddAdditionalAssemblies(typeof(GameView).Assembly)
    .AddAdditionalAssemblies(typeof(ExamplesHome).Assembly);

// SSE push of batched ECS render signals (no SignalR).
app.MapGet("/api/ecs/stream", (EcsSimulation sim, HttpResponse response, CancellationToken ct) =>
{
    response.ContentType = "text/event-stream";
    var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    var writeSync = new object();

    Action<EcsRenderSignal> handler = signal =>
    {
        var json = JsonSerializer.Serialize(signal, jsonOptions);
        lock (writeSync)
        {
            response.WriteAsync($"event: sprite-move\ndata: {json}\n\n").GetAwaiter().GetResult();
        }
    };

    sim.OnRenderSignal += handler;

    var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    ct.Register(() =>
    {
        sim.OnRenderSignal -= handler;
        completed.TrySetResult();
    });
    return completed.Task;
});
```

The SSR page (`GameView.razor`) carries the initial payload to the client through a `data-message` attribute — no server circuit, no `IJSRuntime`:

```razor
@page "/hello"
@using Game.Engine
@implements IDisposable

<div id="pixi-viewport" data-message="@Message" style="width:100%;height:100%;"></div>

@code {
    private readonly GameSimulation _simulation = new();
    private string Message { get; set; } = "";

    protected override void OnInitialized()
    {
        _simulation.OnRenderMessage += HandleRenderMessage;
        _simulation.PublishHello();              // raises RenderMessageEvent → Message
    }

    private void HandleRenderMessage(RenderMessageEvent ev) => Message = ev.Message;

    public void Dispose() => _simulation.OnRenderMessage -= HandleRenderMessage;
}
```

### Step 3: The PixiJS Scene (SSE Consumer)

`src/Game.UI/Frontend/game.ts` bootstraps PixiJS (`initGame` / `renderText` / `renderScene`). The ECS scene opens an `EventSource` on the SSE URL, parses each `sprite-move` batch, and moves only the sprites that changed — a pure mirror of authoritative C# state. No game rules or boundary checks in JS.

```typescript
// src/Game.UI/Frontend/scenes/ecsSprites.ts (condensed)
interface EcsSpriteState  { id: number; x: number; y: number; r: number; g: number; b: number; }
interface EcsRenderSignal { seq: number; entityCount: number; tickMs: number; sprites: EcsSpriteState[]; }

// Initial positions come from the SSR payload → sprites render before the first tick.
const sprites = new Map<number, Sprite>();
for (const state of params.sprites ?? []) {
    const sprite = new Sprite(texture);
    sprite.anchor.set(0.5);
    sprite.position.set(state.x, state.y);
    sprite.tint = (state.r << 16) | (state.g << 8) | state.b;
    app.stage.addChild(sprite);
    sprites.set(state.id, sprite);
}

// Stream authoritative deltas over SSE — never poll C# per frame.
const source = new EventSource(params.streamUrl);
source.addEventListener('sprite-move', (event) => {
    const signal = JSON.parse(event.data) as EcsRenderSignal;
    for (const state of signal.sprites) {
        const sprite = sprites.get(state.id);
        if (sprite && !sprite.destroyed) sprite.position.set(state.x, state.y);
    }
});
source.onerror = () => source.close();
```

This is the interim SSE/JSON bridge; the target is batched `TransformSnapshot` + shared-memory `HEAPF32` zero-copy + client interpolation (ADR-003).

## 🚀 Future-Proofing for Authoritative Multiplayer

By designing your MVP this way, moving to a multiplayer model becomes a structural drop-in change:

- You pluck your Shared Core Engine project out of the client build and compile it into a headless ASP.NET Core console application hosted on Linux.
- Instead of your client UI executing commands directly against a local `GameSimulation` instance, your client UI serializes the `MoveCommand` and shoots it over a SignalR or WebSocket connection.
- The server runs the command through the exact same C# simulation code, processes the ticks, and broadcasts the batched `EcsRenderSignal` across the network to all connected clients.
- Your Blazor/PixiJS setup handles the network event exactly like it handled the local event during the MVP phase.

## Sourced Ecosystem Libraries & Starting Points

The architectural stack uses specialized, lightweight libraries designed for maximum performance, data serialization, and strict zero-allocation boundaries:

- **Game State Engine (Arch ECS):** a high-performance, ultra-lightweight C# Archetype Entity Component System. It avoids rigid class inheritance and allows you to process game world calculations (e.g., matching a parsed Town Entity to its structural Garrison Army Entities) inside structured, flat database-like chunks.
- **AOT-Friendly Persistence Loop (.NET System.Text.Json Source Generators):** essential for saving/loading mechanics. Using `JsonSourceGenerationOptions` forces compilation to produce specialized metadata ahead-of-time (Native AOT-safe). This ensures fast, allocation-free serialization when passing structural map files, flat JSON configs, and delta-state frames across the .NET-to-JavaScript bridge.
- **Canvas & Presentation Layer (PixiJS v8):** the leading HTML5 2D rendering pipeline. It provides code-only WebGL/WebGPU hardware acceleration inside the native .NET MAUI `BlazorWebView` container, entirely bypassing heavy game engine overhead.
- **UI Layout & Theme Canvas (Tailwind CSS):** handles responsive HUDs, non-overlapping contextual menus, popups, inventory windows, and system options cleanly using standard HTML/CSS.

## Specialized MCP Servers & Knowledge Bases

When working with an MCP-capable AI agent:

- **`docs/2d-games`** and **`docs/game-development`** — structured game design patterns, structural gamedev guides, and documentation contexts aligned with this engine's stack (Arch ECS simulation + PixiJS presentation + Blazor/Tailwind UI). They keep the agent anchored to professional game-loop conventions.
  - `docs/2d-games` is the "Universal 2D Engine Toolkit" reference, aligned to the Bonobo stack: architecture/reference docs reflect Arch ECS + PixiJS v8 + Tailwind + Blazor + System.Text.Json; concept guides are engine-agnostic.
  - `docs/game-development` is the curated, engine-agnostic subset (concepts, programming, game design, project management, AI workflow).
  - `docs/game-entity-component-system/` mirrors the toolkit reorganized into `guides/` + `reference/` and carries the Bonobo-specific ECS rules (`bonobo-ECS-rules.md`).
- **`net-microsoft-documentation` MCP server:** connects to Microsoft Learn via streamable HTTP, letting agents search documentation, fetch complete articles, and search code samples — trusted, up-to-date Microsoft knowledge ([source](https://learn.microsoft.com/en-us/training/support/mcp)).

## AI Agent Guidelines & System Instructions

When generating code, refactoring, or adding features in this repository, AI coding agents must adhere strictly to the following rules. (Full generation do/don't lists and game-dev workflow rules live in `AGENTS.md`; verified API facts live in `docs/ai-agents/codebase-truth.md`.)

### 1. Entity Component System (Arch ECS) Rules

- **Components as Data Structs:** components **must** be zero-logic, public C# `struct` value types for cache locality (e.g., `public struct Position { public Vector2 Value; }`). Never use classes or put methods inside ECS components.
- **Systems as Logic Processors:** systems must be stateless or process data purely through `QueryDescription` iterations or `Arch.Systems.BaseSystem` implementations.
- **Safe Structural Modifications:** entity creation, destruction, and component addition/removal must happen via Arch command buffers or outside query loops to prevent invalidating memory chunks during iteration. Never mutate structure mid-query.

## Asset Pipeline Workflow

- Assets live in a clearly defined directory structure; separate source files (PSD, Aseprite, Audacity projects) from exported/engine-ready files.
- Naming: lowercase with underscores, prefix by category (`ui_`, `sfx_`, `bgm_`, `vfx_`, `tile_`, `char_`, `env_`), frame/variant numbers as suffixes.
- Normalize audio to a consistent dB target; music loops must have clean loop points tested in-engine.
- Detailed art/audio pipeline rules: `docs/game-development/ai-workflow/gamedev-rules.md` and `docs/game-development/project-management/P5_art_pipeline.md` / `P6_audio_pipeline.md`.
