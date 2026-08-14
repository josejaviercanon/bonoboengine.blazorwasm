# P12 — Performance Budget Template



> **Summary:** A practical guide to setting and tracking performance budgets for your 2D MonoGame game. Plan for 60fps on modest hardware so you never hit "it works on my machine" surprises late in development.
>
> **See also:** [G33 — Profiling & Optimization](./G33_profiling_optimization.md) for deep-dive techniques referenced throughout this document.

---

## Table of Contents

1. [Why Budget Performance](#1--why-budget-performance)
2. [Target Hardware Specs](#2--target-hardware-specs)
3. [Frame Budget Breakdown](#3--frame-budget-breakdown)
4. [Entity Count Budget](#4--entity-count-budget)
5. [Draw Call Budget](#5--draw-call-budget)
6. [Memory Budget](#6--memory-budget)
7. [Content Budgets by Type](#7--content-budgets-by-type)
8. [Profiling Workflow](#8--profiling-workflow)
9. [Common Performance Pitfalls in 2D](#9--common-performance-pitfalls-in-2d)
10. [Optimization Techniques](#10--optimization-techniques)
11. [Platform-Specific Budgets](#11--platform-specific-budgets)
12. [Performance Testing Checklist](#12--performance-testing-checklist)
13. [Budget Template](#13--budget-template)

---

## 1 — Why Budget Performance

Most 2D games run fine. Sprites are small, logic is simple, modern hardware is absurdly fast. So why budget?

**Because "it runs fine" is the most dangerous phrase in game development.**

It runs fine on *your* machine — your dev rig with a discrete GPU, 32GB of RAM, and an NVMe drive. It does not run fine on a Steam Deck with 4GB of unified memory available for your game. It does not run fine on a 2019 laptop with an integrated Intel UHD 620. It does not run fine when your player has 47 Chrome tabs open.

### The Real Reasons to Budget

- **Surprises compound.** Each individual system is "fine." Together, 12 "fine" systems can blow past 16ms.
- **Late optimization is expensive.** Rearchitecting a particle system two weeks before launch is miserable. Setting a limit at the start is free.
- **Budgets are design constraints.** Knowing you can have ~200 entities on screen shapes level design early, when it's cheap to adjust.
- **Players on low-end hardware are your biggest audience.** The Steam Hardware Survey says most players aren't running top-tier rigs.

### The Rule

> **Set budgets early. Measure occasionally. Panic never.**

You don't need to profile every frame during development. You need to:
1. Define target hardware
2. Set rough budgets per system
3. Check in periodically (monthly, or at milestone boundaries)
4. Optimize only when a budget is exceeded

That's it. The rest of this document gives you the numbers.

---

## 2 — Target Hardware Specs

Define your minimum spec *before* you write code. Everything else flows from this.

### Hardware Tiers

| Tier | GPU | CPU | RAM | Storage | Examples |
|------|-----|-----|-----|---------|----------|
| **Low** | Integrated (Intel UHD 620, AMD Vega 8) | 4-core, ~2.5 GHz | 4 GB available | HDD or slow SSD | Steam Deck, 2018 laptops, budget desktops |
| **Medium** | Dedicated entry (GTX 1650, RX 570) | 4-core, ~3.5 GHz | 8 GB available | SSD | Mid-range laptops, older gaming PCs |
| **High** | Modern dedicated (RTX 3060+, RX 6600+) | 6+ core, ~4 GHz | 16 GB+ | NVMe SSD | Current gaming PCs |

### What Each Tier Means for 2D

For a typical 2D game, **Low tier is your target.** Here's why:

- **GPU is almost never the bottleneck** in 2D. Even integrated GPUs can push thousands of textured quads at 60fps. Your bottleneck will be CPU (game logic, GC pauses) or memory bandwidth (texture swaps).
- **CPU matters more than you think.** ECS iteration, physics broadphase, pathfinding — these are CPU-bound. An integrated chip with 4 cores at 2.5 GHz gives you roughly half the single-thread performance of a modern desktop.
- **RAM is the real constraint.** 4GB total means maybe 1–2 GB for your game after the OS. Your texture atlas strategy matters here.
- **Storage speed affects load times.** HDD users will notice 5-second level loads that take 0.3s on your NVMe.

> **Recommendation:** Target Low tier for gameplay (solid 60fps). Accept longer load times on HDD. Test on Medium tier regularly if you don't have Low-tier hardware.

---

## 3 — Frame Budget Breakdown

At 60fps, you have **16.67 milliseconds** per frame. Every frame. No exceptions.

### The Budget

```
┌─────────────────────────────────────────────────┐
│              16.67ms Frame Budget                │
├─────────────────────────────────────────────────┤
│                                                 │
│  ┌─────────────────────┐                        │
│  │  Update    ≤ 4.0ms  │  Game logic, ECS,      │
│  │  ████████░░░░░░░░░░ │  physics, AI, input    │
│  └─────────────────────┘                        │
│  ┌─────────────────────┐                        │
│  │  Draw      ≤ 8.0ms  │  Rendering, particles, │
│  │  ████████████████░░░ │  UI, text, effects     │
│  └─────────────────────┘                        │
│  ┌─────────────────────┐                        │
│  │  Framework ≤ 2.0ms  │  MonoGame overhead,     │
│  │  ████░░░░░░░░░░░░░░ │  present, swap, input  │
│  └─────────────────────┘                        │
│  ┌─────────────────────┐                        │
│  │  Headroom  ≤ 2.67ms │  GC pauses, OS jitter, │
│  │  █████░░░░░░░░░░░░░ │  background processes  │
│  └─────────────────────┘                        │
│                                                 │
│  Total: 4.0 + 8.0 + 2.0 + 2.67 = 16.67ms      │
└─────────────────────────────────────────────────┘
```

### Why These Numbers?

| Phase | Budget | Rationale |
|-------|--------|-----------|
| **Update** | ≤ 4ms | ECS iteration + physics + AI. If your game logic takes more than 4ms, you have too many systems or too-complex queries. |
| **Draw** | ≤ 8ms | Rendering dominates in 2D. SpriteBatch calls, particle rendering, UI draws. This is your largest slice. |
| **Framework** | ≤ 2ms | MonoGame's `Game.Tick`, input polling, audio mixing, `GraphicsDevice.Present`. You don't control this directly. |
| **Headroom** | ≤ 2.67ms | .NET GC pauses average 1–3ms for Gen0 collections. OS scheduling jitter adds more. Without headroom, any GC pause drops a frame. |

### Sub-Budgets Within Update (4ms total)

| System | Target | Notes |
|--------|--------|-------|
| Input processing | ≤ 0.2ms | Trivial unless you're doing complex input buffering |
| ECS world query + iteration | ≤ 1.5ms | Arch is fast — this covers all system queries |
| Physics / collision | ≤ 1.0ms | Broadphase + narrowphase for active bodies |
| AI / pathfinding | ≤ 0.8ms | Budget per-agent, spread across frames if needed |
| Audio triggers | ≤ 0.2ms | Fire-and-forget to audio engine |
| Misc (timers, events, state) | ≤ 0.3ms | Everything else |

### Sub-Budgets Within Draw (8ms total)

| System | Target | Notes |
|--------|--------|-------|
| Background / tilemap | ≤ 1.5ms | Culled to visible area only |
| Entity sprites | ≤ 2.5ms | Main SpriteBatch pass |
| Particles | ≤ 1.5ms | Can be the biggest cost — cap particle count |
| UI / HUD | ≤ 1.5ms | Text rendering is expensive — cache when possible |
| Post-processing / effects | ≤ 1.0ms | Shaders, screen transitions |

> **See [G33 — Profiling & Optimization](./G33_profiling_optimization.md)** for how to measure each of these phases.

---

## 4 — Entity Count Budget

Arch ECS is one of the fastest ECS implementations in C#. But "fast" has limits.

### Rough Entity Guidelines

| Entity Count | Comfort Level | Notes |
|-------------|---------------|-------|
| **1–10,000** | ✅ Comfortable | No special care needed. Iterate freely. |
| **10,000–50,000** | ⚠️ Careful | Watch system query complexity. Avoid querying all entities every frame. |
| **50,000–100,000** | 🔶 Optimized | Need spatial partitioning, chunked updates, query filtering. |
| **100,000+** | 🔴 Expert | Requires careful archetype design, parallel iteration, and profiling. |

### What Affects Entity Performance

It's not just count — it's **what you do with them.**

**Component count per entity:**
- 3–5 components: typical, fast archetype matching
- 8–10 components: still fine, slightly wider archetypes
- 15+: consider splitting into multiple entities or using tag components

**System query complexity:**
- `Query<Position, Sprite>` (2 components) — very fast
- `Query<Position, Velocity, Health, AI, Collision, Sprite>` (6 components) — still fast, but narrower archetype match
- Multiple queries per system — each query has overhead; combine where possible

**Update frequency:**
Not everything needs to update every frame. AI can run every 3rd frame. Off-screen entities can skip rendering entirely. Hybrid approaches (update nearby entities every frame, distant ones every 4th) stretch your budget dramatically.

### Practical Targets for a 2D Game

| Game Type | Typical On-Screen | Total in World | Notes |
|-----------|-------------------|----------------|-------|
| Platformer | 20–50 | 200–500 | Enemies, collectibles, platforms |
| Top-down RPG | 50–200 | 500–2,000 | NPCs, items, projectiles |
| Bullet hell | 200–2,000 | 200–2,000 | Bullets are entities — pool them! |
| Colony sim | 100–500 | 5,000–50,000 | Lots of background entities, stagger updates |
| Particle-heavy | 50 + 5,000 particles | Varies | Consider particles as non-ECS pooled objects |

> **Reference:** Arch ECS benchmarks show ~2.5M entity iterations/second for simple queries on modest hardware. At 10K entities, that's about 4ms of iteration budget — right at the Update limit. Keep queries lean.

---

## 5 — Draw Call Budget

A **draw call** is a command sent from your CPU to the GPU: "draw these triangles with this texture and these settings." Each one has overhead — not from the GPU doing work, but from the CPU preparing and submitting the command.

### MonoGame's SpriteBatch

MonoGame's `SpriteBatch` batches multiple `Draw` calls into a single GPU draw call *as long as* the texture and render state don't change. Every time the texture changes, SpriteBatch flushes — that's a new draw call.

```csharp
// This is ONE draw call (same texture atlas)
spriteBatch.Begin();
spriteBatch.Draw(atlas, pos1, sourceRect1, Color.White);
spriteBatch.Draw(atlas, pos2, sourceRect2, Color.White);
spriteBatch.Draw(atlas, pos3, sourceRect3, Color.White);
spriteBatch.End();

// This is THREE draw calls (different textures)
spriteBatch.Begin();
spriteBatch.Draw(textureA, pos1, Color.White);  // flush
spriteBatch.Draw(textureB, pos2, Color.White);  // flush
spriteBatch.Draw(textureC, pos3, Color.White);  // flush
spriteBatch.End();
```

### Draw Call Targets

| Tier | Max Draw Calls | Notes |
|------|---------------|-------|
| **Low** | ≤ 100 | Integrated GPUs have high per-call overhead |
| **Medium** | ≤ 300 | Comfortable for most 2D games |
| **High** | ≤ 500 | Plenty of headroom |

For context, a well-structured 2D game typically uses **20–60 draw calls.** If you're over 100, something is wrong with your batching.

### How to Minimize Draw Calls

1. **Use texture atlases.** Pack all sprites for a screen into one or two atlases. One atlas = one draw call for all sprites on it. (See [Section 6](#6--memory-budget) for atlas sizing.)

2. **Sort by texture.** If you use `SpriteSortMode.Deferred` (default), sprites draw in the order you call `Draw`. Group draws by texture:
   ```csharp
   // Good: grouped by texture
   DrawAllEnemies();    // all on enemies.png atlas
   DrawAllTerrain();    // all on terrain.png atlas
   DrawAllUI();         // all on ui.png atlas
   
   // Bad: interleaved textures
   DrawEnemy();         // enemies.png — flush
   DrawTile();          // terrain.png — flush
   DrawEnemy();         // enemies.png — flush (again!)
   ```

3. **Use `SpriteSortMode.Texture`** if you need automatic sorting by texture (SpriteBatch will reorder draws to minimize flushes). Trade-off: you lose draw order control.

4. **Minimize Begin/End pairs.** Each `End()` flushes. Structure your rendering as few Begin/End blocks as possible:
   ```csharp
   // Typical structure: 3-5 Begin/End blocks
   spriteBatch.Begin(/* world space, camera transform */);
     DrawTilemap();
     DrawEntities();
     DrawParticles();
   spriteBatch.End();
   
   spriteBatch.Begin(/* screen space, no transform */);
     DrawUI();
   spriteBatch.End();
   ```

5. **Watch for state changes.** Changing `BlendState`, `SamplerState`, or `Effect` (shader) forces a new Begin/End block → a flush. Plan your render passes to minimize state changes.

---

## 6 — Memory Budget

### Texture Memory

Textures live in GPU memory (VRAM). For integrated GPUs, that's shared with system RAM — so it's even more constrained.

**How to calculate texture memory:**

```
Memory = Width × Height × 4 bytes (RGBA)
```

| Texture Size | Memory | Notes |
|-------------|--------|-------|
| 256 × 256 | 256 KB | Small sprite sheet |
| 512 × 512 | 1 MB | Typical sprite atlas |
| 1024 × 1024 | 4 MB | Large atlas, common choice |
| 2048 × 2048 | 16 MB | Maximum recommended atlas size |
| 4096 × 4096 | 64 MB | Too large for Low tier — avoid |

### Texture Budget by Tier

| Tier | Total Texture Memory | Max Atlas Size | Max Atlases Loaded |
|------|---------------------|----------------|-------------------|
| **Low** | ≤ 128 MB | 2048 × 2048 | 8 |
| **Medium** | ≤ 256 MB | 2048 × 2048 | 16 |
| **High** | ≤ 512 MB | 4096 × 4096 | 32+ |

### Atlas Sizing Strategy

- **Target 1024 × 1024 or 2048 × 2048** for your main atlases. These are the sweet spot — large enough to hold lots of sprites, small enough to load fast and fit in VRAM.
- **One atlas per "context":** characters atlas, tileset atlas, UI atlas, effects atlas. When you change screens/levels, you only swap the atlases that differ.
- **Don't load what you don't need.** Unload atlases for areas the player isn't in.

### Audio Memory

| Format | Memory Usage | Quality | Notes |
|--------|-------------|---------|-------|
| **WAV (uncompressed)** | ~10 MB/min (stereo, 44.1kHz) | Lossless | Use for short SFX (< 2 sec) — fast to play, no decode overhead |
| **OGG (compressed)** | ~1 MB/min (stereo, 128kbps) | Near-lossless | Use for music and longer sounds — decoded on-the-fly |

| Tier | SFX Budget (loaded WAV) | Music Budget (streaming OGG) |
|------|------------------------|------------------------------|
| **Low** | ≤ 32 MB | 1–2 tracks streaming |
| **Medium** | ≤ 64 MB | 2–3 tracks streaming |
| **High** | ≤ 128 MB | 4+ tracks streaming |

### GC Pressure in C#

This is the silent killer of frame rates in C#/.NET games. The garbage collector (GC) pauses your game to clean up memory. Gen0 collections take 1–3ms. Gen1 can take 5–10ms. Gen2… don't let it happen during gameplay.

**The Rule: Zero allocations in Update and Draw.**

Common allocation sources to avoid:

| Allocation Source | Fix |
|------------------|-----|
| `new List<T>()` in Update | Pre-allocate, reuse, or use `stackalloc` |
| `foreach` on non-List collections | Use `for` loops or access the enumerator directly |
| `string + string` | Use `StringBuilder` or `string.Create` |
| LINQ queries (`.Where()`, `.Select()`) | Use manual loops — LINQ allocates iterators |
| `ToString()` on numbers | Cache formatted strings or use Span-based formatting |
| Boxing value types | Use generic collections, avoid `object` params |
| Lambda captures | Avoid closures over local variables in hot paths |
| `new Vector2(x, y)` | Reuse structs — but note: `Vector2` is a struct, no heap alloc. The issue is arrays/lists of them. |

> **Deep dive:** [G33 — Profiling & Optimization](./G33_profiling_optimization.md) covers GC profiling techniques and allocation tracking.

---

## 7 — Content Budgets by Type

Use these tables as starting guidelines. Adjust based on your target tier and actual profiling.

### Per-Screen / Per-Room Limits

| Content Type | Low Tier | Medium Tier | High Tier |
|-------------|----------|-------------|-----------|
| Visible tiles | ≤ 5,000 | ≤ 10,000 | ≤ 20,000 |
| Active entities (on-screen) | ≤ 100 | ≤ 250 | ≤ 500 |
| Active particles | ≤ 500 | ≤ 2,000 | ≤ 5,000 |
| Active sound channels | ≤ 8 | ≤ 16 | ≤ 32 |
| Loaded textures | ≤ 4 atlases | ≤ 8 atlases | ≤ 16 atlases |
| Active lights (if using lighting) | ≤ 8 | ≤ 20 | ≤ 50 |

### Per-Game Limits

| Resource | Low Tier | Medium Tier | High Tier |
|----------|----------|-------------|-----------|
| Total texture memory | ≤ 128 MB | ≤ 256 MB | ≤ 512 MB |
| Total audio (SFX loaded) | ≤ 32 MB | ≤ 64 MB | ≤ 128 MB |
| Total audio (music files) | ≤ 50 MB | ≤ 150 MB | ≤ 300 MB |
| Total content on disk | ≤ 200 MB | ≤ 500 MB | ≤ 1 GB |
| Peak RAM usage | ≤ 512 MB | ≤ 1 GB | ≤ 2 GB |
| Save file size | ≤ 1 MB | ≤ 5 MB | ≤ 10 MB |
| Initial load time (SSD) | ≤ 3 sec | ≤ 5 sec | ≤ 5 sec |
| Initial load time (HDD) | ≤ 8 sec | ≤ 10 sec | ≤ 10 sec |
| Scene transition time | ≤ 0.5 sec | ≤ 0.5 sec | ≤ 0.5 sec |

---

## 8 — Profiling Workflow

Don't guess where your performance goes. **Measure.**

But also: don't profile every frame. That's a waste of your time and it distorts results (profiling itself has overhead).

### When to Profile

- **At milestones:** Every 2–4 weeks during development
- **After adding major systems:** New particle system? Profile. New tilemap renderer? Profile.
- **When something feels slow:** Trust your instincts, then verify with data
- **Before release:** Full profiling pass on target hardware
- **Never:** Continuously during normal development (it'll make you paranoid)

### Profiling Tools

| Tool | What It Measures | When to Use |
|------|-----------------|-------------|
| **`Stopwatch` / `GameTime`** | Frame time per system | Always — cheap, leave in as debug overlay |
| **ImGui debug overlay** | Real-time frame breakdown | During development — see [G16 — Debugging](G16_debugging.md) |
| **`dotnet-trace`** | CPU sampling, method-level hotspots | When you know *something* is slow but not *what* |
| **`dotnet-counters`** | GC collections, allocation rate, thread pool | Monitoring GC pressure over time |
| **PerfView** | Deep GC analysis, allocation stacks | When GC pauses are killing your frame rate |
| **Visual Studio Profiler** | CPU, memory, GPU (Windows) | Full profiling sessions |
| **RenderDoc** | GPU draw calls, state changes | When you suspect GPU bottleneck (rare in 2D) |

### Simple In-Game Profiling

Build a lightweight profiler you keep in the game permanently:

```csharp
public static class PerfTimer
{
    private static readonly Stopwatch _sw = new();
    private static readonly Dictionary<string, double> _times = new();
    
    public static void Begin(string label)
    {
        _sw.Restart();
    }
    
    public static void End(string label)
    {
        _sw.Stop();
        _times[label] = _sw.Elapsed.TotalMilliseconds;
    }
    
    public static IReadOnlyDictionary<string, double> GetTimes() => _times;
}

// Usage in your game loop:
protected override void Update(GameTime gameTime)
{
    PerfTimer.Begin("Update.Physics");
    physicsSystem.Update(dt);
    PerfTimer.End("Update.Physics");
    
    PerfTimer.Begin("Update.AI");
    aiSystem.Update(dt);
    PerfTimer.End("Update.AI");
    
    // ... draw PerfTimer.GetTimes() in your debug overlay
}
```

### What to Look For

1. **Any single system > 2ms** — investigate immediately
2. **Total Update > 4ms** — too much logic per frame
3. **Total Draw > 8ms** — too many draw calls or too many sprites
4. **GC collections during gameplay** — allocations in hot path
5. **Frame time spikes** — GC pauses, content loading, or I/O on main thread
6. **Steady climb in memory** — memory leak (probably event handlers or growing collections)

> **Full profiling guide:** [G33 — Profiling & Optimization](./G33_profiling_optimization.md)

---

## 9 — Common Performance Pitfalls in 2D

These are the usual suspects. If your game is slow, check these first.

### 🗑️ GC Allocations in Hot Loops

**The #1 performance killer in C# games.**

```csharp
// BAD — allocates a new list every frame
void Update()
{
    var nearbyEnemies = enemies.Where(e => e.IsNearPlayer()).ToList();
}

// GOOD — reuse a pre-allocated list
private readonly List<Enemy> _nearbyEnemies = new(64);

void Update()
{
    _nearbyEnemies.Clear();
    for (int i = 0; i < enemies.Count; i++)
    {
        if (enemies[i].IsNearPlayer())
            _nearbyEnemies.Add(enemies[i]);
    }
}
```

### 🔗 LINQ in Update/Draw

LINQ is elegant. LINQ also allocates iterator objects, delegate objects, and intermediate collections. Never use LINQ in per-frame code.

```csharp
// BAD
var targets = entities.Where(e => e.Health > 0).OrderBy(e => e.Distance).First();

// GOOD
Entity closest = null;
float closestDist = float.MaxValue;
for (int i = 0; i < entities.Count; i++)
{
    if (entities[i].Health > 0 && entities[i].Distance < closestDist)
    {
        closest = entities[i];
        closestDist = entities[i].Distance;
    }
}
```

### 📝 String Concatenation

```csharp
// BAD — allocates new strings every frame
debugText = "FPS: " + fps + " Entities: " + count;

// GOOD — cache or use StringBuilder
_sb.Clear();
_sb.Append("FPS: ").Append(fps).Append(" Entities: ").Append(count);
debugText = _sb.ToString();

// BEST — only update when values change
if (fps != _lastFps || count != _lastCount)
{
    _sb.Clear();
    _sb.Append("FPS: ").Append(fps).Append(" Entities: ").Append(count);
    _cachedText = _sb.ToString();
}
```

### 🎨 Unnecessary SpriteBatch Flushes

Every texture change = a flush = a draw call. Drawing sprites in random texture order is the most common cause of high draw call counts.

### 👻 Drawing Off-Screen Entities

SpriteBatch will happily send off-screen sprites to the GPU. The GPU will clip them, but you've already paid the CPU cost of the `Draw` call and the batching overhead.

```csharp
// Always cull before drawing
if (camera.Bounds.Intersects(entity.Bounds))
{
    spriteBatch.Draw(entity.Texture, entity.Position, ...);
}
```

### 📦 Loading Content Every Frame

```csharp
// CATASTROPHIC — loads from disk every frame
void Draw()
{
    var texture = Content.Load<Texture2D>("player"); // cached by ContentManager, but still has lookup overhead
}

// GOOD — load once, store reference
private Texture2D _playerTexture;

void LoadContent()
{
    _playerTexture = Content.Load<Texture2D>("player");
}
```

### 🔄 Large Texture Swaps

Switching between many different textures forces the GPU to swap what's in its texture cache. With atlases, you avoid this entirely.

---

## 10 — Optimization Techniques

When profiling reveals a bottleneck, reach for these solutions.

### Spatial Hashing for Collision

Don't check every entity against every other entity. Use a spatial hash grid to only check nearby entities.

- O(n²) → O(n) for collision broadphase
- Typically 10–100× faster for large entity counts
- **See:** [G3 — Physics & Collision](G3_physics_and_collision.md) for implementation details

### Frustum / Camera Culling

Only process and draw what's visible:

```csharp
// Simple AABB camera culling
Rectangle cameraBounds = camera.GetVisibleArea();

foreach (var entity in entities)
{
    if (!cameraBounds.Intersects(entity.Bounds))
        continue; // skip entirely — no update, no draw
    
    entity.Update(dt);
    entity.Draw(spriteBatch);
}
```

For tilemaps, calculate the visible tile range instead of iterating all tiles:

```csharp
int startX = Math.Max(0, (int)(camera.Left / tileSize));
int startY = Math.Max(0, (int)(camera.Top / tileSize));
int endX = Math.Min(mapWidth, (int)(camera.Right / tileSize) + 1);
int endY = Math.Min(mapHeight, (int)(camera.Bottom / tileSize) + 1);
```

### Object Pooling

Reuse objects instead of creating/destroying them. Critical for bullets, particles, and effects.

```csharp
public class Pool<T> where T : new()
{
    private readonly Stack<T> _available = new();
    
    public T Get() => _available.Count > 0 ? _available.Pop() : new T();
    public void Return(T item) => _available.Push(item);
}
```

### Texture Atlases

Pack multiple sprites into single textures. Benefits:
- Fewer draw calls (same texture = same batch)
- Fewer texture swaps on GPU
- Better memory utilization (less padding waste)
- Faster loading (one file instead of hundreds)

### Dirty Flag Pattern

Only recalculate when something changes:

```csharp
private bool _transformDirty = true;
private Matrix _cachedTransform;

public Matrix Transform
{
    get
    {
        if (_transformDirty)
        {
            _cachedTransform = CalculateTransform();
            _transformDirty = false;
        }
        return _cachedTransform;
    }
}
```

Use for: UI layout, transform hierarchies, pathfinding grids, visibility maps.

### LOD for Particles

Distant or off-screen particles don't need full simulation:
- Reduce update frequency for distant particles
- Skip particles entirely when off-screen
- Use simpler rendering for small/distant particles
- Cap total particles with a hard limit — newest replace oldest

### Reducing State Changes

Group draws by render state to minimize expensive GPU state switches:

```
1. Draw all opaque sprites (BlendState.AlphaBlend, same atlas)
2. Draw all additive effects (BlendState.Additive)
3. Draw all UI elements (different sampler state, UI atlas)
```

Each group = one Begin/End pair = one state change.

> **Full optimization catalog:** [G33 — Profiling & Optimization](./G33_profiling_optimization.md)

---

## 11 — Platform-Specific Budgets

### Steam Deck

The Steam Deck is the new "minimum spec" for PC games on Steam.

| Resource | Budget |
|----------|--------|
| CPU | 4 cores @ 2.4–3.5 GHz (Zen 2) |
| GPU | 8 RDNA 2 CUs (≈ GTX 1050 level) |
| RAM | 16 GB unified (shared CPU/GPU) — but target ≤ 4 GB for your game |
| VRAM | Shared — keep textures small |
| TDP | 4–15W — thermal throttling is real |
| Display | 1280 × 800 — render at this resolution, don't supersample |
| Storage | 64 GB eMMC (slow!) to 512 GB NVMe |
| Battery | 2–8 hours — lower GPU/CPU usage = longer play sessions |

**Key constraints:**
- Thermal throttling will reduce CPU clock after sustained load. Leave headroom.
- Battery life matters — a game that drains battery in 90 minutes gets bad reviews.
- eMMC storage on the base model is *slow*. Optimize load times.

### Mobile (if targeting via FNA or similar)

See [G32 — Deployment & Platform Builds](G32_deployment_platform_builds.md) for setup details.

| Resource | Budget |
|----------|--------|
| CPU | 4–8 cores, but often throttled to 2 |
| GPU | Extremely limited — keep draw calls ≤ 50 |
| RAM | ≤ 1 GB for your game |
| Texture memory | ≤ 64 MB total |
| Battery | Critical — reduce to 30fps if needed |
| Thermals | Phones throttle aggressively after 10 min of heavy load |

### Web (if targeting via WASM)

| Resource | Budget |
|----------|--------|
| CPU | Single-threaded effective (WASM threading is limited) |
| RAM | ≤ 512 MB (browser tab limit) |
| Texture memory | ≤ 64 MB (WebGL limits) |
| Download size | ≤ 50 MB total (including runtime) |
| Load time | ≤ 5 seconds on broadband |

### Older Hardware / Potato PCs

Some of your players will run hardware from 2015. Intel HD 4000 GPUs, 4 GB RAM, spinning HDDs. If you care about this audience:

- Target ≤ 50 draw calls
- Keep total texture memory ≤ 64 MB
- Accept 720p as minimum resolution
- Test on the cheapest laptop you can find

---

## 12 — Performance Testing Checklist

Run through this checklist at major milestones and before release.

### Regular Testing Routine

- [ ] **Test on minimum-spec hardware** (or closest available)
  - If you don't have low-end hardware, use a VM or limit CPU affinity
  - Steam Deck is ideal if available
- [ ] **Measure frame time** — average, 99th percentile, and max
  - Average should be ≤ 14ms (leaving headroom)
  - 99th percentile should be ≤ 16ms
  - Max spike should be ≤ 33ms (one skipped frame, not two)
- [ ] **Stress test entity count**
  - Spawn 2× your expected max entities. Does it stay at 60fps?
  - Spawn 5× max. What's the actual breaking point?
- [ ] **Measure load times**
  - Initial game load on SSD and HDD (or estimate HDD as 10× SSD time)
  - Scene/level transition times
  - Asset loading during gameplay (should be zero — preload everything)
- [ ] **Monitor memory over 30 minutes of gameplay**
  - Does RSS (resident set size) grow steadily? That's a leak.
  - Use `dotnet-counters` to watch GC heap size over time
- [ ] **Check GC behavior**
  - Monitor Gen0/Gen1/Gen2 collection frequency
  - Gen0 every few seconds is normal
  - Gen1 every few minutes is acceptable
  - Gen2 during gameplay is a bug — find and fix the allocations
- [ ] **Test scene transitions**
  - No frame hitches during transitions
  - Memory from previous scene is released
  - No "loading" stall longer than your budget allows
- [ ] **Count draw calls**
  - Use your debug overlay or RenderDoc
  - Should be well within tier budget (≤ 100 for Low)
- [ ] **Profile each system individually**
  - No single system should exceed its sub-budget from [Section 3](#3--frame-budget-breakdown)
- [ ] **Test with VSync on and off**
  - VSync on: consistent 60fps, no tearing
  - VSync off: frame times still within budget (rules out "only fast because of vsync cap")
- [ ] **Test after minimizing/alt-tabbing**
  - Game should recover gracefully
  - No accumulated delta time causing physics explosions
- [ ] **Verify content sizes on disk**
  - Total build size within tier budget
  - No accidentally included debug assets or uncompressed audio

---

## 13 — Budget Template

Copy this template and fill it in for your game. Put it in your project wiki or `docs/` folder and update it as you develop.

---

```markdown
# Performance Budget — [Your Game Name]

## Target Spec
- **Minimum tier:** [Low / Medium / High]
- **Target resolution:** [1280×720 / 1920×1080]
- **Target frame rate:** 60fps (16.67ms per frame)
- **Target platform(s):** [Steam / Steam Deck / Mobile / Web]

## Frame Budget (16.67ms total)

| Phase | Budget | Measured | Status |
|-------|--------|----------|--------|
| Update (logic, ECS, physics, AI) | ___ms | ___ms | ✅ / ⚠️ / 🔴 |
| Draw (rendering, particles, UI) | ___ms | ___ms | ✅ / ⚠️ / 🔴 |
| Framework overhead | ~2ms | ___ms | ✅ / ⚠️ / 🔴 |
| Headroom (GC, OS) | ___ms | ___ms | ✅ / ⚠️ / 🔴 |
| **Total** | **16.67ms** | **___ms** | |

## Entity Budget

| Metric | Budget | Measured | Status |
|--------|--------|----------|--------|
| Max on-screen entities | ___ | ___ | ✅ / ⚠️ / 🔴 |
| Max total entities (world) | ___ | ___ | ✅ / ⚠️ / 🔴 |
| Max particles (on-screen) | ___ | ___ | ✅ / ⚠️ / 🔴 |
| Components per entity (avg) | ___ | ___ | ✅ / ⚠️ / 🔴 |

## Draw Call Budget

| Metric | Budget | Measured | Status |
|--------|--------|----------|--------|
| Max draw calls per frame | ___ | ___ | ✅ / ⚠️ / 🔴 |
| Number of texture atlases | ___ | ___ | ✅ / ⚠️ / 🔴 |
| SpriteBatch Begin/End pairs | ___ | ___ | ✅ / ⚠️ / 🔴 |

## Memory Budget

| Resource | Budget | Measured | Status |
|----------|--------|----------|--------|
| Total texture memory | ___MB | ___MB | ✅ / ⚠️ / 🔴 |
| Loaded SFX memory | ___MB | ___MB | ✅ / ⚠️ / 🔴 |
| Streaming audio tracks | ___ | ___ | ✅ / ⚠️ / 🔴 |
| Peak RAM usage | ___MB | ___MB | ✅ / ⚠️ / 🔴 |
| GC Gen0 frequency | every ___s | every ___s | ✅ / ⚠️ / 🔴 |
| GC Gen2 during gameplay | never | ___ | ✅ / 🔴 |

## Content Budget

| Content Type | Budget | Actual | Status |
|-------------|--------|--------|--------|
| Max tiles per screen | ___ | ___ | ✅ / ⚠️ / 🔴 |
| Max active sounds | ___ | ___ | ✅ / ⚠️ / 🔴 |
| Total build size (disk) | ___MB | ___MB | ✅ / ⚠️ / 🔴 |
| Initial load time (SSD) | ___s | ___s | ✅ / ⚠️ / 🔴 |
| Scene transition time | ___s | ___s | ✅ / ⚠️ / 🔴 |

## Per-Screen Budgets

| Screen / Level | Entities | Particles | Tiles | Draw Calls | Notes |
|---------------|----------|-----------|-------|------------|-------|
| Main Menu | ___ | ___ | ___ | ___ | |
| Level 1 | ___ | ___ | ___ | ___ | |
| Boss Fight | ___ | ___ | ___ | ___ | |
| ___________ | ___ | ___ | ___ | ___ | |
| ___________ | ___ | ___ | ___ | ___ | |

## Profiling Log

| Date | What Changed | Frame Time | Notes |
|------|-------------|------------|-------|
| YYYY-MM-DD | Baseline | ___ms | |
| YYYY-MM-DD | Added ___ | ___ms | |
| YYYY-MM-DD | Optimized ___ | ___ms | |

## Status Key
- ✅ Within budget
- ⚠️ Within 80-100% of budget — monitor closely
- 🔴 Over budget — optimize before shipping
```

---

## Summary

Performance budgeting isn't about premature optimization. It's about **knowing your limits before you hit them.**

The process is simple:
1. Pick your minimum hardware tier (probably Low)
2. Fill in the budget template with conservative targets
3. Build your game without worrying about performance
4. Profile at milestones — compare against your budgets
5. Optimize only what's over budget
6. Ship with confidence that it runs on real hardware

Most 2D games will never come close to these limits. That's the point — if you know the ceiling, you can build freely underneath it.

> **Next steps:**
> - [G33 — Profiling & Optimization](./G33_profiling_optimization.md) — deep-dive on profiling tools and techniques
> - [G16 — Debugging](G16_debugging.md) — setting up ImGui debug overlays
> - [G3 — Physics & Collision](G3_physics_and_collision.md) — spatial hashing implementation
> - [G32 — Deployment & Platform Builds](G32_deployment_platform_builds.md) — platform-specific build configuration
