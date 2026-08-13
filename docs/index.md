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
   - *The Deterministic Tick:* processes input via a command pattern (e.g., `ExecuteCommand(PlayerMoveCommand)`), updates state, and fires off highly optimized, lightweight C# events detailing what changed (e.g., `OnEntityMoved(Id, NewX, NewY)`).
2. **The Presentation Layer (Blazor + PixiJS + Tailwind)** — a pure mirror of your C# state.
   - *Tailwind UI:* Blazor hooks into C# state to display inventories or menus using standard data-binding.
   - *PixiJS Canvas:* instead of polling C# for positions 60 times a second, PixiJS sits completely idle until the C# engine fires a state change event. When an event fires, Blazor forwards a highly optimized, flat payload to PixiJS via `IJSRuntime` to animate the sprite.

### ⚠️ The Performance Gold Rule: Avoid JSON Serialization

Calling `IJSRuntime` every single frame to pass a massive C# state tree to JavaScript will reduce your game's frame rate down to single digits. You **must** use a **Push-Based Delta Event** approach.

- ❌ **Bad (Polling):** PixiJS loops at 60fps and calls C# via interop — "Where is everyone right now?" C# serializes 500 characters into JSON and passes it back.
- ✅ **Good (Reactive Delta Push):** the C# engine finishes a tick. Only two monsters moved. C# calls a single JavaScript function: `DotNet.invokeMethod('UpdatePositions', [ { id: 4, x: 120, y: 80 }, { id: 9, x: 200, y: 300 } ])`. PixiJS updates just those two sprites.

**2.1. UI:** keep the presentation layer thin. Use Razor components and Tailwind CSS for menus, inventories, and HUD. Drop a standard HTML5 `<canvas>` inside that Razor view, and use a modular, object-oriented vanilla TypeScript/JavaScript file to initialize PixiJS and map incoming C# events directly to sprites. Bypassing the React wrapper keeps the application simple, clean, and fast.

## 🛠️ Step-by-Step Blueprint for the MVP

### Step 1: Define Your Command and Event System

Create a message-based communication boundary. This ensures your network transition later is as simple as routing these messages over a WebSocket connection instead of a local memory bridge.

```csharp
// The single source of truth contract

public record EntityMovedEvent(int EntityId, float X, float Y);

public class GameSimulation
{
    public Dictionary<int, GameEntity> Entities { get; } = new();

    // UI or Network triggers this
    public void ProcessCommand(MoveCommand cmd)
    {
        var entity = Entities[cmd.EntityId];
        entity.X = cmd.TargetX;
        entity.Y = cmd.TargetY;

        // Notify the rendering layers
        OnEntityMoved?.Invoke(new EntityMovedEvent(entity.Id, entity.X, entity.Y));
    }

    public event Action<EntityMovedEvent> OnEntityMoved;
}
```

### Step 2: The Skinny Blazor-to-PixiJS Bridge

In your shared Razor Component, subscribe to your C# engine events. When an event triggers, push the raw data directly down to PixiJS.

```razor
@inject IJSRuntime JS
@implements IDisposable

<div class="relative w-full h-screen">
    <!-- Tailwind HUD Layer -->
    <div class="absolute top-4 left-4 bg-slate-900/80 p-4 rounded text-white">
        Health: @PlayerHealth
    </div>

    <!-- PixiJS Canvas Holder -->
    <div id="pixi-container" class="w-full h-full"></div>
</div>

@code {
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            // Initialize PixiJS canvas locally
            await JS.InvokeVoidAsync("initPixi", "pixi-container");

            // Subscribe to authoritative C# state changes
            CoreEngine.OnEntityMoved += HandleEntityMoved;
        }
    }

    private async void HandleEntityMoved(EntityMovedEvent ev)
    {
        // Push only the delta change down to the Pixi rendering layer
        await JS.InvokeVoidAsync("pixiEngine.moveSprite", ev.EntityId, ev.X, ev.Y);
    }

    public void Dispose() => CoreEngine.OnEntityMoved -= HandleEntityMoved;
}
```

### Step 3: Fast JavaScript PixiJS Mirror

Keep your JavaScript thin. It shouldn't process game rules, combat math, or boundary checks; it should only load textures and move visual assets based on instructions from C#.

```javascript
window.pixiEngine = {
    app: null,
    sprites: new Map(),

    initPixi: function (containerId) {
        const container = document.getElementById(containerId);
        this.app = new PIXI.Application({ resizeTo: container });
        container.appendChild(this.app.view);
    },

    moveSprite: function (id, x, y) {
        const sprite = this.sprites.get(id);
        if (sprite) {
            // Animate or teleport to the authoritative coordinates
            sprite.x = x;
            sprite.y = y;
        }
    }
};
```

## 🚀 Future-Proofing for Authoritative Multiplayer

By designing your MVP this way, moving to a multiplayer model becomes a structural drop-in change:

- You pluck your Shared Core Engine project out of the client build and compile it into a headless ASP.NET Core console application hosted on Linux.
- Instead of your client UI executing commands directly against a local `GameSimulation` instance, your client UI serializes the `MoveCommand` and shoots it over a SignalR or WebSocket connection.
- The server runs the command through the exact same C# simulation code, processes the ticks, and broadcasts the `EntityMovedEvent` across the network to all connected clients.
- Your Blazor/PixiJS setup handles the network event exactly like it handled the local event during the MVP phase.

## Sourced Ecosystem Libraries & Starting Points

The architectural stack uses specialized, lightweight libraries designed for maximum performance, data serialization, and strict zero-allocation boundaries:

- **Game State Engine (Arch ECS):** a high-performance, ultra-lightweight C# Archetype Entity Component System. It avoids rigid class inheritance and allows you to process game world calculations (e.g., matching a parsed Town Entity to its structural Garrison Army Entities) inside structured, flat database-like chunks.
- **AOT-Friendly Persistence Loop (.NET System.Text.Json Source Generators):** essential for saving/loading mechanics. Using `JsonSourceGenerationOptions` forces compilation to produce specialized metadata ahead-of-time (Native AOT-safe). This ensures fast, allocation-free serialization when passing structural map files, flat JSON configs, and delta-state frames across the .NET-to-JavaScript bridge.
- **Canvas & Presentation Layer (PixiJS v8):** the leading HTML5 2D rendering pipeline. It provides code-only WebGL/WebGPU hardware acceleration inside the native .NET MAUI `BlazorWebView` container, entirely bypassing heavy game engine overhead.
- **UI Layout & Theme Canvas (Tailwind CSS):** handles responsive HUDs, non-overlapping contextual menus, popups, inventory windows, and system options cleanly using standard HTML/CSS.

## Specialized MCP Servers & Knowledge Bases

When working with an MCP-capable AI agent:

- **`docs/2d-games`** (137 files) and **`docs/game-development`** — structured game design patterns, structural gamedev guides, and documentation contexts mapping out MonoGame + Arch ECS cross-over ecosystems. It ensures the agent does not stray from professional game loop conventions.
  - `docs/2d-games` is the complete vendored "Universal 2D Engine Toolkit" (MonoGame-flavored stack).
  - `docs/game-development` is the curated, engine-agnostic subset (concepts, programming, game design, project management, AI workflow).
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
