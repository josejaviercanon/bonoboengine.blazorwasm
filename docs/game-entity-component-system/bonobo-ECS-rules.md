# Bonobo Engine + Arch ECS — AI Rules

Engine-specific rules for the Bonobo engine: an authoritative C# simulation backed by the Arch ECS framework, presented through PixiJS. These supplement the engine-agnostic rules in `docs/game-development/ai-workflow/gamedev-rules.md`.

> **Source of truth:** `docs/index.md` (architecture), `AGENTS.md` (build/workflow), `openspec/config.yaml` (project context). When this file and those disagree, they win.

---

## Architecture Context

### Tech Stack

- **Language:** C# 14 / .NET 10 (`net10.0`)
- **Simulation:** `src/Game.Engine` — a pure C# class library. Authoritative game state, deterministic ticks, **zero** UI/platform dependencies.
- **ECS:** Arch — vendored as **source** under `src/Arch/` (core, systems, event bus, persistence, relationships, AOT source generator). **Not** a NuGet package. Linked into `Game.Engine` via `ProjectReference`.
- **Source generators:** `src/Arch.Generators/` — a `netstandard2.0` Roslyn analyzer pack that links Arch's `Arch.Systems.SourceGenerator` + `Arch.AOT.SourceGenerator` + `Arch.EventBus` sources. Referenced from `Game.Engine` as an analyzer (`OutputItemType="Analyzer"`).
- **Presentation:** PixiJS v8 — rendered client-side; bundled by Vite + TypeScript in `src/Game.UI/Frontend/`, output to `src/Game.UI/wwwroot/dist`.
- **UI:** Blazor components + Tailwind CSS v4 (in `src/Game.UI`, the shared Razor Class Library).
- **Hosts:** `src/Game.Web` (Blazor Web App, static SSR) and `src/Game.Maui` (.NET MAUI Blazor Hybrid, Android default; iOS/MacCatalyst/Windows conditional).
- **Serialization:** System.Text.Json with source generators (AOT-friendly, allocation-free). Planned.
- **NOT in this stack:** MonoGame, Gum, Apos.Input, Aether.Physics2D, MonoGame.Extended, MonoGame.Aseprite, FontStashSharp, MGCB content pipeline, FMOD. Generic gamedev lore referencing these lives in `docs/2d-games/` (the untouched upstream MonoGame toolkit) and should be treated as illustrative, not prescriptive.

### Project Structure (actual)

```
bonoboengine.blazorwasm/
├── bonoboWebGame.slnx          # .NET 10 XML solution
├── src/
│   ├── Game.Engine/            # Pure C# class lib — authoritative simulation (Arch ECS lives here)
│   │   ├── Game.Engine.csproj  # refs Arch.csproj + Arch.Generators (analyzer)
│   │   └── GameSimulation.cs   # Sim entry: World, systems, ProcessCommand, events out
│   ├── Game.UI/                # Shared Razor Class Library — refs Game.Engine
│   │   ├── Frontend/           # PixiJS/TypeScript source (Vite entry: game.ts)
│   │   ├── wwwroot/dist/       # Generated JS/CSS — DO NOT hand-edit
│   │   └── Game.UI.csproj
│   ├── Game.Web/               # Blazor Web App host (static SSR)
│   ├── Game.Maui/              # .NET MAUI Blazor Hybrid host
│   ├── Arch/                   # Vendored Arch ECS source (net10.0, T4 templates)
│   │   └── Arch.csproj
│   └── Arch.Generators/        # Roslyn analyzer pack (links Arch source generators)
│       └── Arch.Generators.csproj
└── docs/
    ├── index.md                       # Architecture source of truth
    ├── game-entity-component-system/  # ← Bonobo-adapted ECS docs (this file)
    └── 2d-games/                      # Upstream MonoGame+Arch reference (illustrative)
```

### ECS folder conventions inside Game.Engine

```
src/Game.Engine/
├── Components/          # Pure data structs (ECS components)
├── Systems/             # ECS systems (logic only)
├── Tags/                # Zero-size marker components (PlayerTag, EnemyTag, ...)
├── Events/              # Delta event types emitted to the presentation layer
├── Commands/            # Command types processed via ProcessCommand
└── GameSimulation.cs    # World lifecycle, system registration, tick pump
```


---

## ECS Code Generation Rules

### Components: Pure Data Only

Components MUST be pure data structs. No methods, no logic, no constructors with side effects. The engine is pure C# with no UI dependencies, so components carry simulation data only — **never** rendering/GPU types (no `Texture2D`, no pixel rectangles). Render intent is expressed as data (asset keys, layer indices) that the presentation layer consumes via delta events.

```csharp
// CORRECT: Pure data component
public struct Position
{
    public float X;
    public float Y;
}

public struct Velocity
{
    public float X;
    public float Y;
}

public struct Health
{
    public int Current;
    public int Max;
}

// Render intent as data — the presentation layer (PixiJS) reads this via delta events.
// No GPU types; just an asset key + layer + tint as numbers.
public struct Sprite
{
    public int AssetId;     // maps to a PixiJS texture/sprite key
    public int Layer;       // render layer index
    public uint Tint;       // ARGB packed
}
```

```csharp
// WRONG: Logic in a component
public struct Health
{
    public int Current;
    public int Max;

    public void TakeDamage(int amount) => Current -= amount;  // NO! Logic belongs in systems
    public bool IsDead => Current <= 0;  // NO! Computed properties belong in systems
}
```

### Systems: Logic Lives Here

Systems process components. Single responsibility; query for exactly the components they need. Systems must NOT touch UI, `IJSRuntime`, or anything platform-specific — they mutate simulation state only. State leaves the engine exclusively via events.

```csharp
// System that moves entities with Position and Velocity
public class MovementSystem : BaseSystem<World, float>
{
    private readonly QueryDescription _query = new QueryDescription()
        .WithAll<Position, Velocity>();

    public MovementSystem(World world) : base(world) { }

    public override void Update(in float deltaTime)
    {
        World.Query(in _query, (ref Position pos, ref Velocity vel) =>
        {
            pos.X += vel.X * deltaTime;
            pos.Y += vel.Y * deltaTime;
        });
    }
}
```

### Arch ECS Type Signatures

Always specify complete type signatures in Arch queries. The Arch API requires explicit type parameters:

```csharp
// QueryDescription — specify all required components
new QueryDescription().WithAll<Position, Velocity, Health>();

// Query execution — ref parameters must match WithAll types
World.Query(in query, (ref Position pos, ref Velocity vel, ref Health hp) => { ... });

// Entity creation — use component tuple
var entity = World.Create(
    new Position { X = 0, Y = 0 },
    new Velocity { X = 0, Y = 0 },
    new Health { Current = 100, Max = 100 }
);

// Get/Set components on entity
ref var pos = ref World.Get<Position>(entity);
World.Set(entity, new Velocity { X = 5, Y = 0 });
```

**Common mistakes to avoid:**
- Do not forget `ref` on query lambda parameters — Arch passes by reference for mutation.
- Do not use `World.Query` with fewer parameters than the `WithAll` specifies.
- Do not store entity references across frames without checking `World.IsAlive(entity)`.

### Safe Structural Modifications

Entity creation, destruction, and component add/remove MUST happen via Arch command buffers or outside query loops — never mid-query (invalidates memory chunks during iteration).

```csharp
// WRONG: mutating structure inside a query
World.Query(in query, (Entity e, ref Health hp) =>
{
    if (hp.Current <= 0) World.Destroy(e);  // NO! Chunk invalidated mid-iteration
});

// CORRECT: queue structural changes, apply after the query
var toDestroy = new List<Entity>();  // or a pooled buffer
World.Query(in query, (Entity e, ref Health hp) =>
{
    if (hp.Current <= 0) toDestroy.Add(e);
});
foreach (var e in toDestroy) World.Destroy(e);

---

## C# Conventions

### Naming

- **PascalCase** for types, methods, properties, public fields, and constants.
- **camelCase** for local variables and private fields.
- **_camelCase** (underscore prefix) for private instance fields.
- **ALL_CAPS** is NOT used — use PascalCase for constants.

### Patterns

- Use `readonly struct` for components when possible (if the component is not mutated in-place).
- Prefer `ref` returns and `in` parameters for performance-sensitive code paths.
- Use `Span<T>` and `stackalloc` for temporary allocations in hot paths.
- Avoid LINQ in per-frame code (allocates on the heap).
- Avoid `async/await` in the simulation tick — the engine must be deterministic and synchronous. Use command buffers / manual state machines. Async is confined to the Blazor bridge layer.

### String and Logging

- Use string interpolation (`$"text {var}"`) for debug logging.
- Never allocate strings per-frame in release builds.

---

## Build and Run Commands

**Build rule (critical):** Frontend assets build FIRST, then .NET. Never run multiple `dotnet` commands concurrently — static-web-asset compression can race.

```bash
# 1. Build frontend (Vite JS, then Tailwind CSS) — from src/Game.UI
cd src/Game.UI
npm ci
npm run build          # writes to wwwroot/dist — never hand-edit

# 2. Build the whole solution — from repo root
cd ../..
dotnet build bonoboWebGame.slnx

# 3. Run the web host (hot reload)
dotnet watch --project src/Game.Web

# Type-check only (note: tsc --noEmit currently fails on vite.config.ts Node types)
cd src/Game.UI
npx tsc --noEmit
```

**Build rule:** Run `dotnet build bonoboWebGame.slnx` after every code change. Do not accumulate changes without building.

> **MAUI builds** require .NET MAUI workloads; platform-specific TFMs may make full-solution builds depend on host OS/workloads. No test projects currently exist; `dotnet test` validates the solution but reports no tests.


---

## Bonobo Engine File Boundaries

### Asset Pipeline

- Game assets (sprites, audio, fonts) are bundled by **Vite** from `src/Game.UI/Frontend/` into `src/Game.UI/wwwroot/dist/`.
- Do not hand-edit `wwwroot/dist/` — it is generated output; trust `npm run build`.
- There is no MGCB content pipeline and no `Content.Load<T>`. Assets are referenced by key/id from the C# side and resolved client-side by PixiJS.

### File Responsibilities

| File/Directory | Responsibility | AI Should Not |
|---|---|---|
| `GameSimulation.cs` | World lifecycle, system registration, tick pump, `ProcessCommand`, event emission | Add rendering or UI logic |
| `Components/` | Data struct definitions only | Add methods, logic, or UI types |
| `Systems/` | All game logic (simulation) | Touch `IJSRuntime`, Blazor, or platform APIs |
| `Events/` | Delta event types emitted to the presentation layer | Contain game logic |
| `Commands/` | Input/action command types consumed by `ProcessCommand` | Contain rendering |
| `Game.UI/` (Razor + Frontend) | Presentation: Blazor components, PixiJS, Tailwind | Contain authoritative simulation logic |
| `Game.Web/` / `Game.Maui/` | Hosts: bootstrap, wiring, static SSR / MAUI shell | Contain game or ECS logic |

### Simulation Lifecycle (NOT a MonoGame Update/Draw loop)

The Bonobo engine has **no** `Initialize/LoadContent/Update/Draw` lifecycle. It is a deterministic tick pump separated from rendering:

```
 Input (DOM/Blazor)
        │
        ▼
 ProcessCommand(cmd)      ← only entry point for input; validates & queues
        │
        ▼
 Simulation Tick          ← GameSimulation.Update(dt): runs all Arch systems
   • MovementSystem
   • CollisionSystem
   • ... (logic only, no rendering)
        │
        ▼
 Delta Events Out         ← e.g. EntityMovedEvent(id, x, y), SpriteChangedEvent(...)
        │
        ▼
 Blazor Bridge (IJSRuntime) ← push-based, flat payload, NEVER poll
        │
        ▼
 PixiJS renders the delta   ← presentation layer is a pure mirror
```

**Rules:**
- Commands enter the engine **only** via `ProcessCommand`. Never poke entity state directly from UI.
- State leaves the engine **only** via events. Systems never call `IJSRuntime` or JS interop.
- Never poll C# from JS per-frame. Use push-based delta events (the "Performance Gold Rule" from `docs/index.md`).
- The simulation tick is synchronous and deterministic. No `async/await` inside systems.
- Rendering is entirely client-side (PixiJS); the C# side never issues draw calls.

---

## Integration with Core Rules

These Bonobo + Arch rules build on top of the engine-agnostic rules in `docs/game-development/ai-workflow/gamedev-rules.md`. Specifically:

- **Code generation principles** from core rules apply — small units, build after every change, one concern per generation.
- **Asset pipeline** — Vite `wwwroot/dist` is the "engine-ready" format; `npm run build` produces it.
- **Scope control** — the engine is a thin authoritative sim + Arch; resist building a heavyweight framework on top. ~1K lines of custom glue is the budget (see `docs/2d-games/E/E1_architecture_overview.md` for the philosophy).
- **Task structure** — tasks should be completable in one session (≤2 hours per `openspec/config.yaml`).

When core rules and Bonobo rules conflict, Bonobo rules take precedence for this repository.

> **Generic gamedev reference:** `docs/2d-games/` holds the upstream "Universal 2D Engine Toolkit" (MonoGame + Arch flavored). It is valuable for engine-agnostic concepts (game loops, pooling, pathfinding, AI patterns), but its MonoGame/Gum/Apos/MGCB specifics do **not** apply here. Treat MonoGame mentions there as illustrative, not prescriptive.

