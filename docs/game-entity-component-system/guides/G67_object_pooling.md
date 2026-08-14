# G67 — Object Pooling & Recycling

> **Category:** Guide · **Related:** [G64 Combat & Damage §9](./G64_combat_damage_systems.md) · [G23 Particles](./G23_particles.md) · [G6 Audio](./G6_audio.md) · [P12 Performance Budget](./P12_performance_budget.md) · [G4 AI Systems](./G4_ai_systems.md) · [G10 Custom Game Systems](./G10_custom_game_systems.md)

> A complete implementation guide for object pooling and entity recycling in MonoGame + Arch ECS. Covers generic pools, ECS entity recycling, struct pools, audio/VFX/spawn pooling, warm-up strategies, diagnostics, and genre-specific patterns. Everything is composable — use the pieces your game needs.

---

## Table of Contents

1. [Why Pool?](#1--why-pool)
2. [Generic Object Pool](#2--generic-object-pool)
3. [Keyed / Multi-Type Pool](#3--keyed--multi-type-pool)
4. [ECS Entity Recycling](#4--ecs-entity-recycling)
5. [Struct Pools (Array-Based)](#5--struct-pools-array-based)
6. [Audio Source Pooling](#6--audio-source-pooling)
7. [VFX & Particle Pool Integration](#7--vfx--particle-pool-integration)
8. [Spawn & Wave Pooling](#8--spawn--wave-pooling)
9. [UI Element Pooling](#9--ui-element-pooling)
10. [Pool Warming Strategies](#10--pool-warming-strategies)
11. [Pool Monitoring & Diagnostics](#11--pool-monitoring--diagnostics)
12. [Automatic Pool Sizing](#12--automatic-pool-sizing)
13. [Thread Safety](#13--thread-safety)
14. [Genre-Specific Patterns](#14--genre-specific-patterns)
15. [Common Mistakes & Anti-Patterns](#15--common-mistakes--anti-patterns)
16. [Tuning Reference](#16--tuning-reference)

---

## 1 — Why Pool?

### The GC Problem

C# uses a garbage collector (GC). Every time you call `new`, you allocate heap memory. When objects die, the GC eventually collects them. In a desktop app this is fine. In a game running at 60fps with a 16.67ms frame budget, a GC pause of 5-15ms is catastrophic — that's a visible stutter.

The worst-case scenario:

```
Frame 1: Spawn 200 bullets   → 200 allocations
Frame 2: 180 bullets expire   → 180 objects become garbage
Frame 3: GC triggers          → 8ms pause → dropped frame
Frame 4: Spawn 200 more       → 200 more allocations
...repeat...
```

### What Pooling Solves

Instead of create-use-destroy, pool objects: create-use-return-reuse.

```
Startup: Pre-create 256 bullets in pool
Frame 1: Take 200 from pool   → 0 allocations
Frame 2: Return 180 to pool   → 0 garbage
Frame 3: Take 200 from pool   → 0 allocations
...no GC pressure...
```

**Rule of thumb:** If you create and destroy more than ~20 of something per second, pool it.

### What to Pool

| Always Pool | Sometimes Pool | Rarely Worth Pooling |
|-------------|---------------|---------------------|
| Projectiles (bullets, arrows, spells) | Enemy entities (depends on spawn rate) | Player entity (created once) |
| Particles & VFX | Pickup/loot drops | UI screens (created rarely) |
| Audio instances | Floating damage numbers | Tile entities (static) |
| Hit effects (sparks, blood) | Pathfinding nodes | Save/load data |
| Wave-spawned enemies | Network packets | Configuration objects |

### The Three Pool Strategies

This guide covers three fundamentally different approaches:

| Strategy | Best For | Overhead | GC Pressure |
|----------|----------|----------|-------------|
| **Generic pool** (`ObjectPool<T>`) | Reference types, managed objects | Low | Zero after warmup |
| **ECS entity recycling** | Arch entities with many components | Medium | Zero (entities are IDs) |
| **Struct pool** (array-based) | High-count value types (particles) | Minimal | Zero (stack-allocated) |

Pick the right one for each use case. Most games use all three.

---

## 2 — Generic Object Pool

The workhorse pool for any reference type. Pre-allocates, reuses, and caps memory.

### Core Implementation

```csharp
namespace MyGame.Core;

/// <summary>
/// Generic object pool for any reference type.
/// Pre-allocates objects, reuses them, caps total memory.
/// Thread-safe: NO (single-thread game loop only).
/// See §13 for thread-safe variant.
/// </summary>
public sealed class ObjectPool<T> where T : class
{
    private readonly Stack<T> _available;
    private readonly Func<T> _factory;
    private readonly Action<T>? _onGet;
    private readonly Action<T>? _onReturn;
    private readonly int _maxSize;

    // Diagnostics
    private int _totalCreated;
    private int _peakActive;

    public int ActiveCount { get; private set; }
    public int AvailableCount => _available.Count;
    public int TotalCreated => _totalCreated;
    public int PeakActive => _peakActive;
    public int TotalSize => ActiveCount + AvailableCount;

    public ObjectPool(
        Func<T> factory,
        int initialCapacity = 64,
        int maxSize = 1024,
        Action<T>? onGet = null,
        Action<T>? onReturn = null)
    {
        _factory = factory;
        _maxSize = maxSize;
        _onGet = onGet;
        _onReturn = onReturn;
        _available = new Stack<T>(initialCapacity);
        _totalCreated = initialCapacity;

        for (int i = 0; i < initialCapacity; i++)
            _available.Push(_factory());
    }

    /// <summary>Take an object from the pool (or create one if empty).</summary>
    public T Get()
    {
        T obj;
        if (_available.Count > 0)
        {
            obj = _available.Pop();
        }
        else
        {
            obj = _factory();
            _totalCreated++;
        }

        _onGet?.Invoke(obj);
        ActiveCount++;

        if (ActiveCount > _peakActive)
            _peakActive = ActiveCount;

        return obj;
    }

    /// <summary>Return an object to the pool for reuse.</summary>
    public void Return(T obj)
    {
        _onReturn?.Invoke(obj);
        ActiveCount--;

        if (_available.Count < _maxSize)
            _available.Push(obj);
        // Over max: let GC collect — caps memory usage
    }

    /// <summary>Return all tracked objects. Only useful if you maintain your own active list.</summary>
    public void Clear()
    {
        _available.Clear();
        ActiveCount = 0;
    }
}
```

### Usage: Damage Number Pool

```csharp
namespace MyGame.UI;

/// <summary>
/// Floating damage number that rises and fades.
/// Pooled because combat can spawn 10-30 per second.
/// </summary>
public class DamageNumber
{
    public Vector2 Position;
    public string Text = "";
    public Color Color;
    public float Alpha;
    public float Timer;
    public float Duration;
    public float RiseSpeed;
    public bool IsActive;

    public void Reset(Vector2 position, int damage, Color color, float duration = 0.8f)
    {
        Position = position;
        Text = damage.ToString();
        Color = color;
        Alpha = 1f;
        Timer = 0f;
        Duration = duration;
        RiseSpeed = 60f;
        IsActive = true;
    }
}

public sealed class DamageNumberManager
{
    private readonly ObjectPool<DamageNumber> _pool;
    private readonly List<DamageNumber> _active = new(64);

    public DamageNumberManager()
    {
        _pool = new ObjectPool<DamageNumber>(
            factory: () => new DamageNumber(),
            initialCapacity: 32,
            maxSize: 128,
            onGet: null,
            onReturn: dn =>
            {
                dn.IsActive = false;
                dn.Text = "";
            }
        );
    }

    public void Spawn(Vector2 position, int damage, Color color)
    {
        var dn = _pool.Get();
        dn.Reset(position, damage, color);
        _active.Add(dn);
    }

    public void Update(float dt)
    {
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var dn = _active[i];
            dn.Timer += dt;
            dn.Position.Y -= dn.RiseSpeed * dt;
            dn.Alpha = 1f - (dn.Timer / dn.Duration);

            if (dn.Timer >= dn.Duration)
            {
                _pool.Return(dn);
                _active.RemoveAt(i);
            }
        }
    }

    public void Draw(SpriteBatch spriteBatch, SpriteFont font)
    {
        foreach (var dn in _active)
        {
            spriteBatch.DrawString(
                font,
                dn.Text,
                dn.Position,
                dn.Color * dn.Alpha
            );
        }
    }
}
```

### Usage: Pathfinding Node Pool

A* and similar algorithms allocate many small node objects per query. Pooling prevents GC spikes during heavy pathfinding frames (multiple enemies recalculating paths simultaneously).

```csharp
namespace MyGame.AI;

public class PathNode
{
    public int X, Y;
    public float G, H;
    public float F => G + H;
    public PathNode? Parent;
    public bool InOpenSet;
    public bool InClosedSet;

    public void Reset(int x, int y)
    {
        X = x;
        Y = y;
        G = float.MaxValue;
        H = 0;
        Parent = null;
        InOpenSet = false;
        InClosedSet = false;
    }
}

public sealed class PathNodePool
{
    private readonly ObjectPool<PathNode> _pool;
    private readonly List<PathNode> _frameNodes = new(256);

    public PathNodePool(int capacity = 512)
    {
        _pool = new ObjectPool<PathNode>(
            factory: () => new PathNode(),
            initialCapacity: capacity,
            maxSize: capacity * 2
        );
    }

    /// <summary>Get a node for pathfinding. Tracked for bulk return.</summary>
    public PathNode Get(int x, int y)
    {
        var node = _pool.Get();
        node.Reset(x, y);
        _frameNodes.Add(node);
        return node;
    }

    /// <summary>Return all nodes from the current pathfinding query.</summary>
    public void ReturnAll()
    {
        foreach (var node in _frameNodes)
            _pool.Return(node);
        _frameNodes.Clear();
    }
}
```

### `IPoolable` Interface

For types that manage their own reset logic:

```csharp
namespace MyGame.Core;

/// <summary>
/// Implement on pooled objects that need deterministic reset/cleanup.
/// </summary>
public interface IPoolable
{
    /// <summary>Called when taken from pool. Reset all state.</summary>
    void OnPoolGet();

    /// <summary>Called when returned to pool. Release references to prevent leaks.</summary>
    void OnPoolReturn();
}

/// <summary>
/// ObjectPool variant that auto-calls IPoolable lifecycle methods.
/// </summary>
public sealed class AutoPool<T> where T : class, IPoolable
{
    private readonly ObjectPool<T> _inner;

    public int ActiveCount => _inner.ActiveCount;
    public int AvailableCount => _inner.AvailableCount;

    public AutoPool(Func<T> factory, int initialCapacity = 64, int maxSize = 1024)
    {
        _inner = new ObjectPool<T>(
            factory,
            initialCapacity,
            maxSize,
            onGet: obj => obj.OnPoolGet(),
            onReturn: obj => obj.OnPoolReturn()
        );
    }

    public T Get() => _inner.Get();
    public void Return(T obj) => _inner.Return(obj);
}
```

---

## 3 — Keyed / Multi-Type Pool

Games often need pools for multiple variants of the same concept — different bullet sprites, different enemy types, different VFX. A keyed pool manages separate sub-pools under one API.

### Keyed Pool Implementation

```csharp
namespace MyGame.Core;

/// <summary>
/// Manages separate ObjectPools per key (string, enum, int).
/// Use for: bullet types, enemy variants, VFX types, audio clips.
/// </summary>
public sealed class KeyedPool<TKey, TValue> where TKey : notnull where TValue : class
{
    private readonly Dictionary<TKey, ObjectPool<TValue>> _pools = new();
    private readonly Func<TKey, TValue> _factory;
    private readonly Action<TValue>? _onReturn;
    private readonly int _initialPerKey;
    private readonly int _maxPerKey;

    public KeyedPool(
        Func<TKey, TValue> factory,
        int initialPerKey = 16,
        int maxPerKey = 256,
        Action<TValue>? onReturn = null)
    {
        _factory = factory;
        _onReturn = onReturn;
        _initialPerKey = initialPerKey;
        _maxPerKey = maxPerKey;
    }

    /// <summary>Pre-create a sub-pool for a known key.</summary>
    public void Register(TKey key, int? initialCapacity = null)
    {
        if (_pools.ContainsKey(key)) return;

        _pools[key] = new ObjectPool<TValue>(
            factory: () => _factory(key),
            initialCapacity: initialCapacity ?? _initialPerKey,
            maxSize: _maxPerKey,
            onReturn: _onReturn
        );
    }

    public TValue Get(TKey key)
    {
        if (!_pools.TryGetValue(key, out var pool))
        {
            // Auto-register on first use
            Register(key);
            pool = _pools[key];
        }
        return pool.Get();
    }

    public void Return(TKey key, TValue obj)
    {
        if (_pools.TryGetValue(key, out var pool))
            pool.Return(obj);
    }

    /// <summary>Get diagnostics for all sub-pools.</summary>
    public IReadOnlyDictionary<TKey, (int Active, int Available, int Peak)> GetStats()
    {
        var stats = new Dictionary<TKey, (int, int, int)>();
        foreach (var (key, pool) in _pools)
            stats[key] = (pool.ActiveCount, pool.AvailableCount, pool.PeakActive);
        return stats;
    }
}
```

### Usage: Multi-Bullet Pool

```csharp
namespace MyGame.Combat;

public enum BulletType { Standard, Laser, Homing, Explosive, Spread }

/// <summary>
/// Bullet definitions loaded from data. Each type has different
/// sprite, speed, damage, and behavior.
/// </summary>
public record BulletDef(
    BulletType Type,
    string SpriteName,
    float Speed,
    int Damage,
    float Lifetime,
    bool Piercing
);

public class Bullet
{
    public BulletType Type;
    public Vector2 Position;
    public Vector2 Velocity;
    public float Timer;
    public float Lifetime;
    public int Damage;
    public bool Piercing;
    public bool Active;
    public Rectangle SourceRect; // Sprite region

    public void Activate(BulletDef def, Vector2 pos, Vector2 dir)
    {
        Type = def.Type;
        Position = pos;
        Velocity = Vector2.Normalize(dir) * def.Speed;
        Timer = 0;
        Lifetime = def.Lifetime;
        Damage = def.Damage;
        Piercing = def.Piercing;
        Active = true;
    }
}

public sealed class BulletManager
{
    private readonly KeyedPool<BulletType, Bullet> _pool;
    private readonly List<Bullet> _active = new(512);
    private readonly Dictionary<BulletType, BulletDef> _defs = new();

    public IReadOnlyList<Bullet> ActiveBullets => _active;

    public BulletManager(Dictionary<BulletType, BulletDef> definitions)
    {
        _defs = definitions;

        _pool = new KeyedPool<BulletType, Bullet>(
            factory: _ => new Bullet(),
            initialPerKey: 64,
            maxPerKey: 512,
            onReturn: b => { b.Active = false; }
        );

        // Pre-warm pools based on expected usage
        _pool.Register(BulletType.Standard, initialCapacity: 128);
        _pool.Register(BulletType.Laser, initialCapacity: 32);
        _pool.Register(BulletType.Homing, initialCapacity: 64);
        _pool.Register(BulletType.Explosive, initialCapacity: 32);
        _pool.Register(BulletType.Spread, initialCapacity: 128);
    }

    public void Fire(BulletType type, Vector2 position, Vector2 direction)
    {
        if (!_defs.TryGetValue(type, out var def)) return;

        var bullet = _pool.Get(type);
        bullet.Activate(def, position, direction);
        _active.Add(bullet);
    }

    public void Update(float dt)
    {
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var b = _active[i];
            b.Position += b.Velocity * dt;
            b.Timer += dt;

            if (b.Timer >= b.Lifetime || !b.Active)
            {
                _pool.Return(b.Type, b);
                _active.RemoveAt(i);
            }
        }
    }
}
```

### Usage: Multi-VFX Pool

```csharp
namespace MyGame.VFX;

public enum EffectType
{
    Hit_Spark,
    Explosion_Small,
    Explosion_Large,
    Heal_Glow,
    Level_Up,
    Dust_Puff,
    Blood_Splat,
    Shield_Break
}

public class VisualEffect
{
    public EffectType Type;
    public Vector2 Position;
    public float Rotation;
    public float Scale;
    public Color Tint;
    public float Timer;
    public float Duration;
    public int CurrentFrame;
    public int TotalFrames;
    public float FrameTime;
    public bool Active;

    public void Activate(EffectType type, Vector2 pos, float duration,
        int totalFrames, float rotation = 0, float scale = 1f)
    {
        Type = type;
        Position = pos;
        Rotation = rotation;
        Scale = scale;
        Tint = Color.White;
        Timer = 0;
        Duration = duration;
        CurrentFrame = 0;
        TotalFrames = totalFrames;
        FrameTime = duration / totalFrames;
        Active = true;
    }
}

public sealed class VFXManager
{
    private readonly KeyedPool<EffectType, VisualEffect> _pool;
    private readonly List<VisualEffect> _active = new(128);

    public VFXManager()
    {
        _pool = new KeyedPool<EffectType, VisualEffect>(
            factory: _ => new VisualEffect(),
            initialPerKey: 16,
            maxPerKey: 64,
            onReturn: vfx => { vfx.Active = false; }
        );

        // Pre-warm common effects
        _pool.Register(EffectType.Hit_Spark, initialCapacity: 32);
        _pool.Register(EffectType.Dust_Puff, initialCapacity: 24);
    }

    public void Spawn(EffectType type, Vector2 position, float duration = 0.3f,
        int frames = 6, float rotation = 0, float scale = 1f)
    {
        var vfx = _pool.Get(type);
        vfx.Activate(type, position, duration, frames, rotation, scale);
        _active.Add(vfx);
    }

    public void Update(float dt)
    {
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var vfx = _active[i];
            vfx.Timer += dt;
            vfx.CurrentFrame = (int)(vfx.Timer / vfx.FrameTime);

            if (vfx.Timer >= vfx.Duration)
            {
                _pool.Return(vfx.Type, vfx);
                _active.RemoveAt(i);
            }
        }
    }

    public void Draw(SpriteBatch spriteBatch, Dictionary<EffectType, Texture2D> sheets,
        int frameWidth, int frameHeight)
    {
        foreach (var vfx in _active)
        {
            if (!sheets.TryGetValue(vfx.Type, out var tex)) continue;

            var srcRect = new Rectangle(
                vfx.CurrentFrame * frameWidth, 0,
                frameWidth, frameHeight
            );

            spriteBatch.Draw(
                tex, vfx.Position, srcRect,
                vfx.Tint * (1f - vfx.Timer / vfx.Duration),
                vfx.Rotation,
                new Vector2(frameWidth / 2f, frameHeight / 2f),
                vfx.Scale,
                SpriteEffects.None, 0
            );
        }
    }
}
```

---

## 4 — ECS Entity Recycling

With Arch ECS, entities are lightweight IDs backed by archetype storage. Creating entities is fast, but destroying/creating in bulk still causes archetype reshuffling. For high-frequency spawns, recycle entities instead.

### The Pooled Tag Pattern

The core idea: inactive entities keep a `Pooled` tag component. Systems skip pooled entities. When you need a new entity, remove the tag and set its components.

```csharp
namespace MyGame.ECS;

/// <summary>Marker tag. Entities with this are in the pool — systems ignore them.</summary>
public record struct Pooled;

/// <summary>Tracks which pool an entity belongs to, for returning.</summary>
public record struct PoolMembership(string PoolId);
```

### Generic ECS Pool

```csharp
namespace MyGame.ECS;

using Arch.Core;
using Arch.Core.Extensions;

/// <summary>
/// Recycles Arch ECS entities by toggling a Pooled tag.
/// Entities stay in the World but are invisible to gameplay systems.
/// 
/// Usage pattern:
///   1. Pre-warm with component archetype
///   2. Spawn: Remove Pooled tag + Set component values
///   3. Despawn: Add Pooled tag
///   4. Systems: Query excludes Pooled
/// </summary>
public sealed class EcsPool
{
    private readonly World _world;
    private readonly string _poolId;
    private readonly Queue<Entity> _inactive = new();

    // Diagnostics
    private int _peakActive;
    public int ActiveCount { get; private set; }
    public int InactiveCount => _inactive.Count;
    public int TotalCount => ActiveCount + InactiveCount;
    public int PeakActive => _peakActive;

    public EcsPool(World world, string poolId)
    {
        _world = world;
        _poolId = poolId;
    }

    /// <summary>
    /// Pre-create entities with the given component archetype.
    /// All pre-warmed entities start pooled (inactive).
    /// </summary>
    public void Prewarm<T1>(int count, T1 template1) where T1 : struct
    {
        for (int i = 0; i < count; i++)
        {
            var entity = _world.Create(template1, new Pooled(), new PoolMembership(_poolId));
            _inactive.Enqueue(entity);
        }
    }

    public void Prewarm<T1, T2>(int count, T1 t1, T2 t2)
        where T1 : struct where T2 : struct
    {
        for (int i = 0; i < count; i++)
        {
            var entity = _world.Create(t1, t2, new Pooled(), new PoolMembership(_poolId));
            _inactive.Enqueue(entity);
        }
    }

    public void Prewarm<T1, T2, T3>(int count, T1 t1, T2 t2, T3 t3)
        where T1 : struct where T2 : struct where T3 : struct
    {
        for (int i = 0; i < count; i++)
        {
            var entity = _world.Create(t1, t2, t3, new Pooled(), new PoolMembership(_poolId));
            _inactive.Enqueue(entity);
        }
    }

    public void Prewarm<T1, T2, T3, T4>(int count, T1 t1, T2 t2, T3 t3, T4 t4)
        where T1 : struct where T2 : struct where T3 : struct where T4 : struct
    {
        for (int i = 0; i < count; i++)
        {
            var entity = _world.Create(t1, t2, t3, t4, new Pooled(), new PoolMembership(_poolId));
            _inactive.Enqueue(entity);
        }
    }

    /// <summary>
    /// Take an entity from the pool. Returns null if empty
    /// (caller should create a new entity manually).
    /// </summary>
    public Entity? TryGet()
    {
        if (_inactive.Count == 0) return null;

        var entity = _inactive.Dequeue();
        _world.Remove<Pooled>(entity);
        ActiveCount++;

        if (ActiveCount > _peakActive)
            _peakActive = ActiveCount;

        return entity;
    }

    /// <summary>Return an entity to the pool.</summary>
    public void Return(Entity entity)
    {
        if (!_world.IsAlive(entity)) return;

        _world.Add(entity, new Pooled());
        _inactive.Enqueue(entity);
        ActiveCount--;
    }
}
```

### Complete Projectile System with ECS Pooling

```csharp
namespace MyGame.Combat;

using Arch.Core;
using Arch.Core.Extensions;
using Microsoft.Xna.Framework;

// ──────────────────────────────────────
// Components
// ──────────────────────────────────────

public record struct Position(Vector2 Value);

public record struct Velocity(Vector2 Value);

public record struct Projectile(
    int Damage,
    DamageType Type,
    Entity Owner,           // Who fired it (for friendly fire checks)
    float Lifetime,         // Max seconds alive
    float Timer,            // Current age
    bool Piercing,          // Pass through enemies?
    int PierceCount,        // How many enemies pierced so far
    int MaxPierces          // 0 = infinite (if Piercing is true)
);

public record struct SpriteInfo(
    bool Visible,
    Color Tint,
    float Rotation,
    float Scale,
    Rectangle SourceRect
);

public enum DamageType { Physical, Fire, Ice, Lightning, Poison }

// ──────────────────────────────────────
// Projectile Pool Manager
// ──────────────────────────────────────

public sealed class ProjectilePoolManager
{
    private readonly World _world;
    private readonly EcsPool _pool;

    public ProjectilePoolManager(World world, int prewarm = 256)
    {
        _world = world;
        _pool = new EcsPool(world, "projectiles");

        // Pre-warm with default component values
        _pool.Prewarm(prewarm,
            new Position(Vector2.Zero),
            new Velocity(Vector2.Zero),
            new Projectile(0, DamageType.Physical, Entity.Null, 0, 0, false, 0, 0),
            new SpriteInfo(false, Color.White, 0, 1, Rectangle.Empty)
        );
    }

    /// <summary>
    /// Spawn a projectile. Uses pooled entity if available,
    /// creates new one otherwise.
    /// </summary>
    public Entity Spawn(
        Vector2 position, Vector2 velocity,
        int damage, DamageType type, Entity owner,
        float lifetime = 3f, bool piercing = false, int maxPierces = 0,
        Rectangle sourceRect = default, Color? tint = null)
    {
        Entity entity;
        var pooled = _pool.TryGet();

        if (pooled.HasValue)
        {
            entity = pooled.Value;
        }
        else
        {
            // Pool exhausted — create fresh entity
            entity = _world.Create(
                new Position(position),
                new Velocity(velocity),
                new Projectile(damage, type, owner, lifetime, 0, piercing, 0, maxPierces),
                new SpriteInfo(true, tint ?? Color.White, 0, 1, sourceRect),
                new PoolMembership("projectiles")
            );
            return entity;
        }

        // Reset all component data on pooled entity
        float rotation = MathF.Atan2(velocity.Y, velocity.X);

        _world.Set(entity, new Position(position));
        _world.Set(entity, new Velocity(velocity));
        _world.Set(entity, new Projectile(damage, type, owner, lifetime, 0, piercing, 0, maxPierces));
        _world.Set(entity, new SpriteInfo(true, tint ?? Color.White, rotation, 1, sourceRect));

        return entity;
    }

    /// <summary>Return a projectile to the pool (called by ProjectileSystem on expiry).</summary>
    public void Despawn(Entity entity)
    {
        // Hide sprite before pooling
        _world.Set(entity, new SpriteInfo(false, Color.Transparent, 0, 1, Rectangle.Empty));
        _pool.Return(entity);
    }

    public int ActiveCount => _pool.ActiveCount;
    public int PooledCount => _pool.InactiveCount;
}
```

### Projectile Movement + Lifetime System

```csharp
namespace MyGame.Combat;

using Arch.Core;

/// <summary>
/// Moves projectiles and handles lifetime expiry.
/// Excludes pooled entities via query filter.
/// </summary>
public sealed class ProjectileSystem
{
    private readonly World _world;
    private readonly ProjectilePoolManager _poolManager;
    private readonly QueryDescription _query;

    // Buffer despawns to avoid modifying archetypes during iteration
    private readonly List<Entity> _despawnBuffer = new(64);

    public ProjectileSystem(World world, ProjectilePoolManager poolManager)
    {
        _world = world;
        _poolManager = poolManager;

        // CRITICAL: Exclude Pooled entities from the query
        _query = new QueryDescription()
            .WithAll<Position, Velocity, Projectile>()
            .WithNone<Pooled>();
    }

    public void Update(float dt)
    {
        _despawnBuffer.Clear();

        _world.Query(in _query, (Entity entity, ref Position pos, ref Velocity vel, ref Projectile proj) =>
        {
            // Move
            pos = pos with { Value = pos.Value + vel.Value * dt };

            // Age
            proj = proj with { Timer = proj.Timer + dt };

            // Expire
            if (proj.Timer >= proj.Lifetime)
            {
                _despawnBuffer.Add(entity);
            }
        });

        // Process despawns after iteration
        foreach (var entity in _despawnBuffer)
        {
            _poolManager.Despawn(entity);
        }
    }
}
```

### Query Filter — The Critical Part

Every system that processes gameplay entities must exclude `Pooled`:

```csharp
// ✅ CORRECT: Excludes pooled entities
var query = new QueryDescription()
    .WithAll<Position, Velocity, SpriteInfo>()
    .WithNone<Pooled>();

// ❌ WRONG: Will process invisible pooled entities!
var query = new QueryDescription()
    .WithAll<Position, Velocity, SpriteInfo>();
```

**This is the #1 pooling bug.** Add one system without the filter and you get ghost entities — invisible bullets that still collide, invisible enemies that still pathfind, invisible VFX that still update. Use the diagnostic system in §11 to catch these.

---

## 5 — Struct Pools (Array-Based)

For extremely high-count, short-lived objects (thousands of particles, trail points), even class allocation overhead is too much. Use a flat array of structs with swap-remove.

### Fixed-Size Struct Pool

```csharp
namespace MyGame.Core;

/// <summary>
/// Fixed-capacity array pool for value types.
/// Zero allocations, zero GC. Cache-friendly iteration.
/// 
/// Uses swap-and-shrink removal — order is NOT preserved.
/// If draw order matters, sort the active slice before rendering.
/// </summary>
public sealed class StructPool<T> where T : struct
{
    private readonly T[] _items;
    private int _count;

    public int Count => _count;
    public int Capacity => _items.Length;
    public bool IsFull => _count >= _items.Length;

    /// <summary>Read-only span over active items.</summary>
    public ReadOnlySpan<T> Active => _items.AsSpan(0, _count);

    public StructPool(int capacity)
    {
        _items = new T[capacity];
        _count = 0;
    }

    /// <summary>Add an item. Returns the index, or -1 if full.</summary>
    public int Add(ref T item)
    {
        if (_count >= _items.Length) return -1;
        _items[_count] = item;
        return _count++;
    }

    /// <summary>Remove item at index using swap-and-shrink.</summary>
    public void RemoveAt(int index)
    {
        if (index < 0 || index >= _count) return;
        _count--;
        if (index < _count)
            _items[index] = _items[_count];
        _items[_count] = default;
    }

    /// <summary>Direct ref access for in-place mutation (no copy).</summary>
    public ref T this[int index] => ref _items[index];

    /// <summary>Remove all items without clearing memory.</summary>
    public void Clear() => _count = 0;
}
```

### Usage: Trail Points

```csharp
namespace MyGame.VFX;

public struct TrailPoint
{
    public Vector2 Position;
    public float Width;
    public float Alpha;
    public float Age;
    public float MaxAge;
}

public sealed class TrailRenderer
{
    private readonly StructPool<TrailPoint> _points;
    private readonly float _spawnInterval;
    private float _spawnTimer;

    public TrailRenderer(int maxPoints = 64, float spawnInterval = 0.016f)
    {
        _points = new StructPool<TrailPoint>(maxPoints);
        _spawnInterval = spawnInterval;
    }

    public void Update(float dt, Vector2 currentPosition, float width)
    {
        // Spawn new points
        _spawnTimer += dt;
        if (_spawnTimer >= _spawnInterval)
        {
            _spawnTimer = 0;
            var point = new TrailPoint
            {
                Position = currentPosition,
                Width = width,
                Alpha = 1f,
                Age = 0,
                MaxAge = 0.5f
            };
            _points.Add(ref point); // Silently drops if full — acceptable for trails
        }

        // Age and remove expired points
        for (int i = _points.Count - 1; i >= 0; i--)
        {
            ref var p = ref _points[i];
            p.Age += dt;
            p.Alpha = 1f - (p.Age / p.MaxAge);
            p.Width *= 0.98f; // Taper

            if (p.Age >= p.MaxAge)
                _points.RemoveAt(i);
        }
    }

    // Draw as connected quads — see G60 Trails & Lines for mesh generation
}
```

### Usage: Blood / Debris Decals

```csharp
namespace MyGame.VFX;

public struct Decal
{
    public Vector2 Position;
    public float Rotation;
    public float Scale;
    public Color Tint;
    public float Alpha;
    public float FadeTimer;
    public float FadeDuration;
    public int SpriteVariant; // Index into sprite sheet
}

public sealed class DecalManager
{
    private readonly StructPool<Decal> _decals;
    private readonly Random _rng = new();

    public DecalManager(int maxDecals = 256)
    {
        _decals = new StructPool<Decal>(maxDecals);
    }

    public void Spawn(Vector2 position, Color tint, float fadeDuration = 5f)
    {
        var decal = new Decal
        {
            Position = position,
            Rotation = (float)(_rng.NextDouble() * MathF.Tau),
            Scale = 0.8f + (float)_rng.NextDouble() * 0.4f,
            Tint = tint,
            Alpha = 1f,
            FadeTimer = 0,
            FadeDuration = fadeDuration,
            SpriteVariant = _rng.Next(4) // 4 variants
        };

        // If pool is full, oldest decal gets overwritten (swap-remove
        // removes the oldest visually because it's at the start)
        if (_decals.IsFull)
            _decals.RemoveAt(0);

        _decals.Add(ref decal);
    }

    public void Update(float dt)
    {
        for (int i = _decals.Count - 1; i >= 0; i--)
        {
            ref var d = ref _decals[i];
            d.FadeTimer += dt;
            d.Alpha = 1f - (d.FadeTimer / d.FadeDuration);

            if (d.FadeTimer >= d.FadeDuration)
                _decals.RemoveAt(i);
        }
    }

    public void Draw(SpriteBatch spriteBatch, Texture2D sheet, int variantSize)
    {
        var active = _decals.Active;
        for (int i = 0; i < active.Length; i++)
        {
            ref readonly var d = ref _decals[i];
            var src = new Rectangle(d.SpriteVariant * variantSize, 0, variantSize, variantSize);
            spriteBatch.Draw(
                sheet, d.Position, src,
                d.Tint * d.Alpha, d.Rotation,
                new Vector2(variantSize / 2f), d.Scale,
                SpriteEffects.None, 0
            );
        }
    }
}
```

### When to Use Struct Pools vs Object Pools

| Factor | StructPool | ObjectPool |
|--------|-----------|------------|
| Item size | < 128 bytes ideal, < 256 max | Any size |
| Allocation | Zero (array pre-allocated) | Zero after warmup |
| Iteration | Cache-friendly (contiguous memory) | Cache-unfriendly (heap scattered) |
| Item identity | No — swap-remove changes indices | Yes — references are stable |
| Max practical count | 1,000 – 100,000 | 100 – 10,000 |
| Callbacks (onGet/onReturn) | No | Yes |
| Complex state (references, nested objects) | No (value-type only) | Yes |
| Best for | Particles, trail points, decals, spatial hash entries | Bullets, enemies, UI elements, pathfinding nodes |

---

## 6 — Audio Source Pooling

Audio is one of the most overlooked pooling targets. Every `SoundEffect.CreateInstance()` allocates. In combat-heavy games, dozens of hit sounds, footsteps, and impacts can fire per second.

### Audio Pool

```csharp
namespace MyGame.Audio;

using Microsoft.Xna.Framework.Audio;

/// <summary>
/// Pools SoundEffectInstance objects per SoundEffect.
/// Prevents allocation spikes during combat or VFX-heavy moments.
/// </summary>
public sealed class AudioPool
{
    private readonly Dictionary<string, SoundEffect> _effects = new();
    private readonly Dictionary<string, Queue<SoundEffectInstance>> _pools = new();
    private readonly Dictionary<string, List<SoundEffectInstance>> _active = new();
    private readonly int _maxPerSound;
    private readonly int _prewarmCount;

    public AudioPool(int maxPerSound = 8, int prewarmCount = 4)
    {
        _maxPerSound = maxPerSound;
        _prewarmCount = prewarmCount;
    }

    /// <summary>Register a sound effect and pre-warm its pool.</summary>
    public void Register(string name, SoundEffect effect)
    {
        _effects[name] = effect;
        _pools[name] = new Queue<SoundEffectInstance>();
        _active[name] = new List<SoundEffectInstance>(_maxPerSound);

        for (int i = 0; i < _prewarmCount; i++)
        {
            var instance = effect.CreateInstance();
            _pools[name].Enqueue(instance);
        }
    }

    /// <summary>
    /// Play a pooled sound. Returns the instance for further control,
    /// or null if the sound limit is reached (prevents audio spam).
    /// </summary>
    public SoundEffectInstance? Play(
        string name,
        float volume = 1f,
        float pitch = 0f,
        float pan = 0f)
    {
        if (!_effects.ContainsKey(name)) return null;

        // Reclaim finished instances
        ReclaimFinished(name);

        // Enforce max concurrent instances per sound
        if (_active[name].Count >= _maxPerSound)
        {
            // Option 1: Steal oldest instance
            var oldest = _active[name][0];
            oldest.Stop();
            _active[name].RemoveAt(0);
            _pools[name].Enqueue(oldest);
        }

        // Get or create instance
        SoundEffectInstance instance;
        if (_pools[name].Count > 0)
        {
            instance = _pools[name].Dequeue();
        }
        else
        {
            instance = _effects[name].CreateInstance();
        }

        instance.Volume = volume;
        instance.Pitch = pitch;
        instance.Pan = pan;
        instance.Play();

        _active[name].Add(instance);
        return instance;
    }

    /// <summary>Play with random pitch variation (great for combat sounds).</summary>
    public SoundEffectInstance? PlayWithVariation(
        string name,
        float volume = 1f,
        float pitchRange = 0.15f,
        float pan = 0f)
    {
        float pitch = (Random.Shared.NextSingle() * 2f - 1f) * pitchRange;
        return Play(name, volume, pitch, pan);
    }

    /// <summary>Move finished instances back to pool.</summary>
    private void ReclaimFinished(string name)
    {
        var active = _active[name];
        for (int i = active.Count - 1; i >= 0; i--)
        {
            if (active[i].State == SoundState.Stopped)
            {
                _pools[name].Enqueue(active[i]);
                active.RemoveAt(i);
            }
        }
    }

    /// <summary>Reclaim all finished sounds. Call once per frame.</summary>
    public void Update()
    {
        foreach (var name in _active.Keys)
            ReclaimFinished(name);
    }

    /// <summary>Stop everything and return all instances to pools.</summary>
    public void StopAll()
    {
        foreach (var (name, active) in _active)
        {
            foreach (var inst in active)
            {
                inst.Stop();
                _pools[name].Enqueue(inst);
            }
            active.Clear();
        }
    }

    /// <summary>Dispose all instances (call on game exit).</summary>
    public void Dispose()
    {
        StopAll();
        foreach (var pool in _pools.Values)
        {
            while (pool.Count > 0)
                pool.Dequeue().Dispose();
        }
    }
}
```

### Integration with Combat

```csharp
// In your game initialization
var audioPool = new AudioPool(maxPerSound: 8, prewarmCount: 4);
audioPool.Register("sword_hit", Content.Load<SoundEffect>("audio/sword_hit"));
audioPool.Register("arrow_fire", Content.Load<SoundEffect>("audio/arrow_fire"));
audioPool.Register("explosion", Content.Load<SoundEffect>("audio/explosion"));
audioPool.Register("footstep_stone", Content.Load<SoundEffect>("audio/footstep_stone"));

// In combat damage resolution
void OnDamageDealt(DamageEvent evt)
{
    // Pitch variation prevents "machine gun effect" from repeated sounds
    audioPool.PlayWithVariation("sword_hit", volume: 0.7f, pitchRange: 0.2f);
}

// In game update loop
audioPool.Update(); // Reclaim finished instances
```

### Positional Audio with Pooling

```csharp
namespace MyGame.Audio;

/// <summary>
/// Extends AudioPool with distance-based volume and panning.
/// </summary>
public sealed class SpatialAudioPool
{
    private readonly AudioPool _pool;
    private readonly float _maxDistance;

    public SpatialAudioPool(AudioPool pool, float maxDistance = 800f)
    {
        _pool = pool;
        _maxDistance = maxDistance;
    }

    /// <summary>
    /// Play a sound at a world position relative to a listener.
    /// Automatically culls sounds beyond max distance.
    /// </summary>
    public SoundEffectInstance? PlayAt(
        string name, Vector2 worldPos, Vector2 listenerPos,
        float baseVolume = 1f, float pitchRange = 0f)
    {
        var delta = worldPos - listenerPos;
        float distance = delta.Length();

        // Cull distant sounds entirely — saves pool capacity
        if (distance > _maxDistance) return null;

        // Linear falloff
        float volume = baseVolume * (1f - distance / _maxDistance);

        // Stereo panning (-1 left, +1 right)
        float pan = MathHelper.Clamp(delta.X / (_maxDistance * 0.5f), -1f, 1f);

        float pitch = pitchRange > 0
            ? (Random.Shared.NextSingle() * 2f - 1f) * pitchRange
            : 0f;

        return _pool.Play(name, volume, pitch, pan);
    }

    public void Update() => _pool.Update();
}
```

---

## 7 — VFX & Particle Pool Integration

Particles are covered in detail in [G23 Particles](./G23_particles.md). This section shows how particle pools integrate with the broader pooling architecture.

### Particle Burst Pool

For one-shot particle effects (explosions, hit sparks, death puffs) where you don't want persistent emitters:

```csharp
namespace MyGame.VFX;

/// <summary>
/// Pools particle burst configurations.
/// Instead of allocating new particle arrays per burst,
/// reserves slices of a shared StructPool.
/// </summary>
public sealed class ParticleBurstPool
{
    private readonly StructPool<Particle> _particles;
    private readonly Random _rng = new();

    public ParticleBurstPool(int maxParticles = 4096)
    {
        _particles = new StructPool<Particle>(maxParticles);
    }

    public struct Particle
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public float Life;
        public float MaxLife;
        public Color Color;
        public float Size;
        public float Rotation;
        public float RotationSpeed;
        public float Gravity;
        public float Drag;
    }

    /// <summary>Emit a burst of particles.</summary>
    public void Burst(
        Vector2 origin,
        int count,
        Color color,
        float speedMin = 50f,
        float speedMax = 200f,
        float lifeMin = 0.2f,
        float lifeMax = 0.6f,
        float sizeMin = 2f,
        float sizeMax = 6f,
        float gravity = 0f,
        float drag = 0.98f,
        float spread = MathF.Tau) // Full circle by default
    {
        for (int i = 0; i < count; i++)
        {
            float angle = (float)(_rng.NextDouble() * spread - spread / 2f);
            float speed = speedMin + (float)_rng.NextDouble() * (speedMax - speedMin);

            var p = new Particle
            {
                Position = origin,
                Velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed,
                Life = 0,
                MaxLife = lifeMin + (float)_rng.NextDouble() * (lifeMax - lifeMin),
                Color = color,
                Size = sizeMin + (float)_rng.NextDouble() * (sizeMax - sizeMin),
                Rotation = (float)(_rng.NextDouble() * MathF.Tau),
                RotationSpeed = ((float)_rng.NextDouble() - 0.5f) * 10f,
                Gravity = gravity,
                Drag = drag
            };

            _particles.Add(ref p); // Silently drops if at capacity
        }
    }

    /// <summary>Directional burst (e.g., blood splatter in hit direction).</summary>
    public void DirectionalBurst(
        Vector2 origin, Vector2 direction, int count,
        Color color, float coneAngle = 0.5f,
        float speedMin = 100f, float speedMax = 300f)
    {
        float baseAngle = MathF.Atan2(direction.Y, direction.X);

        for (int i = 0; i < count; i++)
        {
            float angle = baseAngle + ((float)_rng.NextDouble() - 0.5f) * coneAngle;
            float speed = speedMin + (float)_rng.NextDouble() * (speedMax - speedMin);

            var p = new Particle
            {
                Position = origin,
                Velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed,
                Life = 0,
                MaxLife = 0.3f + (float)_rng.NextDouble() * 0.4f,
                Color = color,
                Size = 2f + (float)_rng.NextDouble() * 4f,
                Rotation = (float)(_rng.NextDouble() * MathF.Tau),
                RotationSpeed = ((float)_rng.NextDouble() - 0.5f) * 8f,
                Gravity = 300f,  // Blood/debris falls
                Drag = 0.96f
            };

            _particles.Add(ref p);
        }
    }

    public void Update(float dt)
    {
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            ref var p = ref _particles[i];
            p.Velocity.Y += p.Gravity * dt;
            p.Velocity *= p.Drag;
            p.Position += p.Velocity * dt;
            p.Life += dt;
            p.Rotation += p.RotationSpeed * dt;

            if (p.Life >= p.MaxLife)
                _particles.RemoveAt(i);
        }
    }

    public void Draw(SpriteBatch spriteBatch, Texture2D pixel)
    {
        var active = _particles.Active;
        for (int i = 0; i < active.Length; i++)
        {
            ref readonly var p = ref _particles[i];
            float alpha = 1f - (p.Life / p.MaxLife);
            float size = p.Size * (0.5f + alpha * 0.5f); // Shrink as it dies

            spriteBatch.Draw(
                pixel, p.Position, null,
                p.Color * alpha,
                p.Rotation,
                new Vector2(0.5f),
                size,
                SpriteEffects.None, 0
            );
        }
    }

    public int ActiveCount => _particles.Count;
    public int Capacity => _particles.Capacity;
}
```

---

## 8 — Spawn & Wave Pooling

Tower defense, bullet hell, and survival games spawn hundreds of entities in waves. Pre-warming pools for upcoming waves prevents frame spikes at wave boundaries.

### Wave-Aware Pool Manager

```csharp
namespace MyGame.Spawning;

/// <summary>
/// Manages pools for wave-spawned entities. Pre-warms pools
/// based on upcoming wave data to eliminate spawn-frame allocation.
/// </summary>
public sealed class WavePoolManager
{
    private readonly World _world;
    private readonly Dictionary<string, EcsPool> _pools = new();
    private readonly Dictionary<string, int> _peakPerWave = new();

    public WavePoolManager(World world)
    {
        _world = world;
    }

    /// <summary>
    /// Register an enemy type with its component archetype.
    /// </summary>
    public void RegisterEnemyType<T1, T2, T3>(string typeName, T1 t1, T2 t2, T3 t3, int initialCount = 0)
        where T1 : struct where T2 : struct where T3 : struct
    {
        var pool = new EcsPool(_world, $"enemy_{typeName}");
        pool.Prewarm(initialCount, t1, t2, t3);
        _pools[typeName] = pool;
    }

    /// <summary>
    /// Pre-warm pools for an upcoming wave.
    /// Call during the inter-wave countdown.
    /// </summary>
    public void PrepareForWave(WaveDefinition wave)
    {
        foreach (var spawn in wave.Spawns)
        {
            if (!_pools.TryGetValue(spawn.EnemyType, out var pool)) continue;

            int needed = spawn.Count - pool.InactiveCount;
            if (needed <= 0) continue;

            // Spread prewarm across frames to avoid single-frame spike
            // For immediate needs, just prewarm all at once
            PrewarmType(spawn.EnemyType, needed);
        }
    }

    private void PrewarmType(string typeName, int count)
    {
        // Implementation depends on knowing the component archetype.
        // In practice, store factory delegates during RegisterEnemyType.
        // Simplified here for clarity.
    }

    /// <summary>
    /// Get an entity from a typed pool.
    /// Returns null if pool doesn't exist or is empty (caller creates manually).
    /// </summary>
    public Entity? Spawn(string typeName)
    {
        if (!_pools.TryGetValue(typeName, out var pool)) return null;
        return pool.TryGet();
    }

    /// <summary>Return an entity to its pool.</summary>
    public void Despawn(string typeName, Entity entity)
    {
        if (_pools.TryGetValue(typeName, out var pool))
            pool.Return(entity);
    }

    /// <summary>Get diagnostic info for all pools.</summary>
    public Dictionary<string, (int Active, int Pooled, int Peak)> GetStats()
    {
        var stats = new Dictionary<string, (int, int, int)>();
        foreach (var (name, pool) in _pools)
            stats[name] = (pool.ActiveCount, pool.InactiveCount, pool.PeakActive);
        return stats;
    }
}

/// <summary>Data class for wave definitions.</summary>
public record WaveDefinition(
    int WaveNumber,
    SpawnEntry[] Spawns,
    float Duration
);

public record SpawnEntry(
    string EnemyType,
    int Count,
    float SpawnInterval,
    string SpawnPoint
);
```

### Staggered Pre-Warming

For very large waves (100+ enemies), pre-warming everything in one frame can itself cause a spike. Spread it across the countdown:

```csharp
namespace MyGame.Spawning;

/// <summary>
/// Spreads pool pre-warming across multiple frames.
/// Use during wave countdowns to avoid single-frame spikes.
/// </summary>
public sealed class StaggeredPrewarmer
{
    private readonly Queue<Action> _warmTasks = new();
    private int _tasksPerFrame;

    public bool IsComplete => _warmTasks.Count == 0;
    public int Remaining => _warmTasks.Count;

    /// <summary>
    /// Schedule prewarm work. Each task should be lightweight
    /// (creating 1-5 entities).
    /// </summary>
    public void Schedule(int totalEntities, Action<int> createOne, int batchSize = 5)
    {
        int batches = (totalEntities + batchSize - 1) / batchSize;
        _tasksPerFrame = Math.Max(1, batches / 60); // Spread across ~1 second

        for (int i = 0; i < totalEntities; i += batchSize)
        {
            int start = i;
            int count = Math.Min(batchSize, totalEntities - i);
            _warmTasks.Enqueue(() =>
            {
                for (int j = 0; j < count; j++)
                    createOne(start + j);
            });
        }
    }

    /// <summary>Call once per frame during the countdown.</summary>
    public void Update()
    {
        int processed = 0;
        while (_warmTasks.Count > 0 && processed < _tasksPerFrame)
        {
            _warmTasks.Dequeue().Invoke();
            processed++;
        }
    }
}
```

### Usage in Wave Controller

```csharp
// During wave countdown (3 seconds between waves)
if (waveCountdown > 0)
{
    if (!_prewarmer.IsComplete)
        _prewarmer.Update(); // Spreads creation across countdown frames

    waveCountdown -= dt;
    if (waveCountdown <= 0)
        StartWave(nextWave);
}

// When starting a wave
void PrepareNextWave(WaveDefinition next)
{
    // Pre-warm over the next 3-second countdown
    foreach (var spawn in next.Spawns)
    {
        _prewarmer.Schedule(spawn.Count, index =>
        {
            // Create pooled entity with default components
            wavePoolManager.PrepareForWave(next);
        });
    }
}
```

---

## 9 — UI Element Pooling

Scrolling lists, inventory grids, and chat logs constantly create and destroy UI elements. Pool them.

### List Item Pool

```csharp
namespace MyGame.UI;

/// <summary>
/// Pools UI list items for scrollable lists.
/// Only items visible in the viewport are active;
/// the rest stay in the pool.
/// </summary>
public sealed class VirtualizedList<TData>
{
    private readonly ObjectPool<ListItem> _pool;
    private readonly List<ListItem> _visibleItems = new();
    private readonly Func<TData, string> _textSelector;

    private IReadOnlyList<TData> _data = Array.Empty<TData>();
    private float _scrollOffset;
    private float _itemHeight;
    private float _viewportHeight;
    private int _firstVisible;
    private int _lastVisible;

    public sealed class ListItem
    {
        public int DataIndex;
        public Rectangle Bounds;
        public string Text = "";
        public Color BackgroundColor;
        public bool IsSelected;
        public bool IsHovered;

        public void Reset()
        {
            DataIndex = -1;
            Text = "";
            IsSelected = false;
            IsHovered = false;
            BackgroundColor = Color.Transparent;
        }
    }

    public VirtualizedList(
        float itemHeight, float viewportHeight,
        Func<TData, string> textSelector,
        int poolSize = 64)
    {
        _itemHeight = itemHeight;
        _viewportHeight = viewportHeight;
        _textSelector = textSelector;

        _pool = new ObjectPool<ListItem>(
            factory: () => new ListItem(),
            initialCapacity: poolSize,
            maxSize: poolSize * 2,
            onReturn: item => item.Reset()
        );
    }

    public void SetData(IReadOnlyList<TData> data)
    {
        _data = data;
        RecalculateVisible();
    }

    public void Scroll(float delta)
    {
        float maxScroll = Math.Max(0, _data.Count * _itemHeight - _viewportHeight);
        _scrollOffset = MathHelper.Clamp(_scrollOffset + delta, 0, maxScroll);
        RecalculateVisible();
    }

    private void RecalculateVisible()
    {
        int newFirst = Math.Max(0, (int)(_scrollOffset / _itemHeight));
        int visibleCount = (int)(_viewportHeight / _itemHeight) + 2; // +2 for partial items
        int newLast = Math.Min(_data.Count - 1, newFirst + visibleCount);

        // Return items no longer visible
        for (int i = _visibleItems.Count - 1; i >= 0; i--)
        {
            var item = _visibleItems[i];
            if (item.DataIndex < newFirst || item.DataIndex > newLast)
            {
                _pool.Return(item);
                _visibleItems.RemoveAt(i);
            }
        }

        // Create items now visible
        var existingIndices = new HashSet<int>(_visibleItems.Select(v => v.DataIndex));
        for (int i = newFirst; i <= newLast; i++)
        {
            if (existingIndices.Contains(i)) continue;

            var item = _pool.Get();
            item.DataIndex = i;
            item.Text = _textSelector(_data[i]);
            item.Bounds = new Rectangle(
                0,
                (int)(i * _itemHeight - _scrollOffset),
                400, // Width — set based on your layout
                (int)_itemHeight
            );
            _visibleItems.Add(item);
        }

        _firstVisible = newFirst;
        _lastVisible = newLast;
    }

    public void Draw(SpriteBatch spriteBatch, SpriteFont font, Vector2 offset)
    {
        foreach (var item in _visibleItems)
        {
            var pos = new Vector2(
                offset.X + item.Bounds.X + 8,
                offset.Y + item.Bounds.Y + 4
            );

            // Background
            if (item.IsSelected || item.IsHovered)
            {
                // Draw highlight rect
            }

            spriteBatch.DrawString(font, item.Text, pos, Color.White);
        }
    }
}
```

### Inventory Slot Pool

```csharp
namespace MyGame.UI;

/// <summary>
/// Pools inventory slot UI elements.
/// Useful for games with multiple inventory views
/// (player, chest, shop, crafting) that share slot rendering.
/// </summary>
public sealed class InventorySlotPool
{
    private readonly ObjectPool<InventorySlot> _pool;

    public sealed class InventorySlot
    {
        public int SlotIndex;
        public Rectangle Bounds;
        public Texture2D? ItemIcon;
        public int StackCount;
        public string Tooltip = "";
        public Color BorderColor;
        public bool IsEmpty;
        public bool IsHighlighted;

        public void Clear()
        {
            ItemIcon = null;
            StackCount = 0;
            Tooltip = "";
            BorderColor = Color.Gray;
            IsEmpty = true;
            IsHighlighted = false;
        }
    }

    public InventorySlotPool(int maxSlots = 128)
    {
        _pool = new ObjectPool<InventorySlot>(
            factory: () => new InventorySlot(),
            initialCapacity: maxSlots,
            maxSize: maxSlots,
            onReturn: slot => slot.Clear()
        );
    }

    /// <summary>
    /// Build a grid of inventory slots. Returns the active slots.
    /// Caller is responsible for returning them when the view closes.
    /// </summary>
    public List<InventorySlot> CreateGrid(
        int columns, int rows, int slotSize, int padding, Vector2 origin)
    {
        var slots = new List<InventorySlot>(columns * rows);

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                var slot = _pool.Get();
                slot.SlotIndex = y * columns + x;
                slot.Bounds = new Rectangle(
                    (int)origin.X + x * (slotSize + padding),
                    (int)origin.Y + y * (slotSize + padding),
                    slotSize, slotSize
                );
                slot.IsEmpty = true;
                slots.Add(slot);
            }
        }

        return slots;
    }

    /// <summary>Return all slots from a closed view.</summary>
    public void ReturnAll(List<InventorySlot> slots)
    {
        foreach (var slot in slots)
            _pool.Return(slot);
        slots.Clear();
    }
}
```

---

## 10 — Pool Warming Strategies

When and how you fill pools matters as much as the pool itself.

### Strategy Comparison

| Strategy | When | Pros | Cons |
|----------|------|------|------|
| **Constructor prewarm** | Game startup | Simple, predictable | Slow startup, wastes memory for unused pools |
| **Scene/level load** | On scene transition | Right-sized for level | Loading screen required |
| **First-use lazy** | First `Get()` call | Minimal waste | First-use frame spike |
| **Staggered** | Over multiple frames | No single-frame spike | Complex scheduling |
| **Predictive** | Before expected need | Best runtime perf | Requires prediction system |

### Scene-Based Pool Warming

```csharp
namespace MyGame.Scenes;

/// <summary>
/// Pool warming hints per scene/level.
/// Load from JSON or define in code.
/// </summary>
public record PoolWarmHint(
    string PoolId,
    int Count
);

public static class ScenePoolHints
{
    // Define expected pool sizes per scene
    public static readonly Dictionary<string, PoolWarmHint[]> Hints = new()
    {
        ["forest_level"] = new[]
        {
            new PoolWarmHint("projectiles", 128),
            new PoolWarmHint("enemy_slime", 30),
            new PoolWarmHint("enemy_bat", 20),
            new PoolWarmHint("vfx_hit_spark", 32),
            new PoolWarmHint("audio_hit", 8),
            new PoolWarmHint("damage_numbers", 24),
        },
        ["boss_arena"] = new[]
        {
            new PoolWarmHint("projectiles", 512), // Boss bullet hell
            new PoolWarmHint("enemy_minion", 50),
            new PoolWarmHint("vfx_explosion", 16),
            new PoolWarmHint("vfx_hit_spark", 64),
            new PoolWarmHint("audio_hit", 16),
            new PoolWarmHint("damage_numbers", 48),
        },
        ["tower_defense_map"] = new[]
        {
            new PoolWarmHint("projectiles", 256),
            new PoolWarmHint("enemy_grunt", 100),
            new PoolWarmHint("enemy_fast", 50),
            new PoolWarmHint("enemy_tank", 20),
            new PoolWarmHint("enemy_flying", 30),
            new PoolWarmHint("vfx_explosion", 32),
            new PoolWarmHint("vfx_hit_spark", 48),
        }
    };
}
```

### Loading Screen Integration

```csharp
namespace MyGame.Scenes;

/// <summary>
/// Warms pools during loading screen with progress reporting.
/// </summary>
public sealed class PoolWarmer
{
    public float Progress { get; private set; }
    public bool IsComplete { get; private set; }

    private readonly Queue<(string PoolId, int Count)> _tasks = new();
    private int _totalWork;
    private int _completedWork;

    public void Prepare(string sceneName)
    {
        if (!ScenePoolHints.Hints.TryGetValue(sceneName, out var hints)) return;

        foreach (var hint in hints)
        {
            _tasks.Enqueue((hint.PoolId, hint.Count));
            _totalWork += hint.Count;
        }
    }

    /// <summary>
    /// Process one batch per frame. Returns true when complete.
    /// </summary>
    public bool UpdateStep(Action<string, int> warmPool, int batchSize = 20)
    {
        if (_tasks.Count == 0)
        {
            IsComplete = true;
            Progress = 1f;
            return true;
        }

        int processed = 0;
        while (_tasks.Count > 0 && processed < batchSize)
        {
            var (poolId, count) = _tasks.Peek();
            int toCreate = Math.Min(count - processed, batchSize - processed);

            warmPool(poolId, toCreate);
            processed += toCreate;
            _completedWork += toCreate;

            if (toCreate >= count)
                _tasks.Dequeue();
        }

        Progress = _totalWork > 0 ? (float)_completedWork / _totalWork : 1f;
        return false;
    }
}
```

---

## 11 — Pool Monitoring & Diagnostics

Blind pooling leads to either wasted memory (pools too large) or runtime allocation (pools too small). Monitor your pools.

### Pool Diagnostic System

```csharp
namespace MyGame.Diagnostics;

/// <summary>
/// Aggregates diagnostics from all pools in the game.
/// Renders an overlay for development builds.
/// </summary>
public sealed class PoolDiagnostics
{
    private readonly Dictionary<string, PoolStats> _stats = new();
    private bool _visible;

    public record PoolStats(
        string Name,
        int Active,
        int Available,
        int PeakActive,
        int TotalCreated,
        float UtilizationPercent
    );

    public void Toggle() => _visible = !_visible;

    /// <summary>Register a pool for monitoring.</summary>
    public void Track(string name, Func<(int Active, int Available, int Peak, int Total)> statsProvider)
    {
        // Store provider — called each frame when visible
        _trackers[name] = statsProvider;
    }

    private readonly Dictionary<string, Func<(int Active, int Available, int Peak, int Total)>> _trackers = new();

    public void Update()
    {
        if (!_visible) return;

        _stats.Clear();
        foreach (var (name, provider) in _trackers)
        {
            var (active, available, peak, total) = provider();
            float utilization = total > 0 ? (float)peak / total * 100f : 0;

            _stats[name] = new PoolStats(name, active, available, peak, total, utilization);
        }
    }

    public void Draw(SpriteBatch spriteBatch, SpriteFont font)
    {
        if (!_visible || _stats.Count == 0) return;

        var pos = new Vector2(10, 10);
        var bgColor = new Color(0, 0, 0, 180);

        // Header
        spriteBatch.DrawString(font, "=== POOL DIAGNOSTICS ===", pos, Color.Cyan);
        pos.Y += 20;

        spriteBatch.DrawString(font,
            $"{"Pool",-24} {"Active",7} {"Avail",7} {"Peak",7} {"Total",7} {"Util%",7}",
            pos, Color.Gray);
        pos.Y += 16;

        foreach (var stat in _stats.Values.OrderByDescending(s => s.Active))
        {
            Color color = stat.UtilizationPercent switch
            {
                > 90 => Color.Red,       // Pool too small!
                > 70 => Color.Yellow,    // Getting tight
                < 20 => Color.Gray,      // Pool oversized
                _ => Color.White
            };

            spriteBatch.DrawString(font,
                $"{stat.Name,-24} {stat.Active,7} {stat.Available,7} " +
                $"{stat.PeakActive,7} {stat.TotalCreated,7} {stat.UtilizationPercent,6:F1}%",
                pos, color);
            pos.Y += 16;
        }

        // Warnings
        pos.Y += 8;
        foreach (var stat in _stats.Values)
        {
            if (stat.UtilizationPercent > 95)
            {
                spriteBatch.DrawString(font,
                    $"⚠ {stat.Name}: Peak {stat.PeakActive}/{stat.TotalCreated} — INCREASE POOL SIZE",
                    pos, Color.Red);
                pos.Y += 16;
            }
            else if (stat.UtilizationPercent < 10 && stat.TotalCreated > 32)
            {
                spriteBatch.DrawString(font,
                    $"💤 {stat.Name}: Only {stat.PeakActive}/{stat.TotalCreated} used — consider shrinking",
                    pos, Color.DarkGray);
                pos.Y += 16;
            }
        }
    }
}
```

### Usage: Registering Pools for Monitoring

```csharp
// During game initialization
var diagnostics = new PoolDiagnostics();

diagnostics.Track("Projectiles", () =>
    (projectilePool.ActiveCount, projectilePool.PooledCount,
     projectilePool.ActiveCount, projectilePool.ActiveCount + projectilePool.PooledCount));

diagnostics.Track("DamageNumbers", () =>
    (damagePool.ActiveCount, damagePool.AvailableCount,
     damagePool.PeakActive, damagePool.TotalCreated));

diagnostics.Track("AudioHits", () =>
    (audioPool.ActiveCount("sword_hit"), audioPool.AvailableCount("sword_hit"),
     0, 0)); // Simplified — real impl tracks per-sound stats

// Toggle with debug key
if (Keyboard.GetState().IsKeyDown(Keys.F3))
    diagnostics.Toggle();
```

### GC Monitoring

Complement pool diagnostics with GC tracking to prove pooling is working:

```csharp
namespace MyGame.Diagnostics;

/// <summary>
/// Tracks GC collections per generation.
/// Rising Gen0 count = allocations still happening somewhere.
/// </summary>
public sealed class GCMonitor
{
    private int _lastGen0;
    private int _lastGen1;
    private int _lastGen2;
    private float _checkInterval = 1f;
    private float _timer;

    // Rolling window of collections per second
    private readonly Queue<(float Time, int Gen0, int Gen1, int Gen2)> _history = new();

    public int Gen0PerSecond { get; private set; }
    public int Gen1PerSecond { get; private set; }
    public int Gen2PerSecond { get; private set; }

    public void Update(float dt)
    {
        _timer += dt;
        if (_timer < _checkInterval) return;
        _timer = 0;

        int gen0 = GC.CollectionCount(0);
        int gen1 = GC.CollectionCount(1);
        int gen2 = GC.CollectionCount(2);

        Gen0PerSecond = gen0 - _lastGen0;
        Gen1PerSecond = gen1 - _lastGen1;
        Gen2PerSecond = gen2 - _lastGen2;

        _lastGen0 = gen0;
        _lastGen1 = gen1;
        _lastGen2 = gen2;

        if (Gen0PerSecond > 5)
        {
            // Something is still allocating heavily!
            // Use dotnet-trace or VS profiler to find the source.
            System.Diagnostics.Debug.WriteLine(
                $"[GC WARNING] {Gen0PerSecond} Gen0 collections/sec — check allocations!");
        }
    }

    public string GetDisplayString() =>
        $"GC: Gen0={Gen0PerSecond}/s Gen1={Gen1PerSecond}/s Gen2={Gen2PerSecond}/s " +
        $"Heap={GC.GetTotalMemory(false) / 1024 / 1024}MB";
}
```

---

## 12 — Automatic Pool Sizing

Hard-coded pool sizes are guesswork. Use runtime data to auto-tune.

### Adaptive Pool

```csharp
namespace MyGame.Core;

/// <summary>
/// Wraps ObjectPool with automatic size adjustment based on usage patterns.
/// Grows when hitting capacity, shrinks when consistently underutilized.
/// </summary>
public sealed class AdaptivePool<T> where T : class
{
    private ObjectPool<T> _pool;
    private readonly Func<T> _factory;
    private readonly Action<T>? _onGet;
    private readonly Action<T>? _onReturn;

    // Tracking
    private readonly Queue<int> _peakHistory = new(); // Peak per interval
    private int _currentPeak;
    private float _timer;
    private readonly float _checkInterval;
    private readonly int _historyLength;
    private readonly float _growThreshold;    // Grow when utilization exceeds this
    private readonly float _shrinkThreshold;  // Shrink when utilization is below this
    private readonly int _minSize;
    private readonly int _maxSize;
    private readonly float _growFactor;

    public int CurrentCapacity => _pool.TotalSize;
    public int ActiveCount => _pool.ActiveCount;

    public AdaptivePool(
        Func<T> factory,
        int initialCapacity = 64,
        int minSize = 16,
        int maxSize = 4096,
        float checkIntervalSeconds = 10f,
        int historyLength = 6,    // Check last 60 seconds (6 × 10s)
        float growThreshold = 0.85f,
        float shrinkThreshold = 0.25f,
        float growFactor = 1.5f,
        Action<T>? onGet = null,
        Action<T>? onReturn = null)
    {
        _factory = factory;
        _onGet = onGet;
        _onReturn = onReturn;
        _minSize = minSize;
        _maxSize = maxSize;
        _checkInterval = checkIntervalSeconds;
        _historyLength = historyLength;
        _growThreshold = growThreshold;
        _shrinkThreshold = shrinkThreshold;
        _growFactor = growFactor;

        _pool = new ObjectPool<T>(factory, initialCapacity, maxSize, onGet, onReturn);
    }

    public T Get()
    {
        var obj = _pool.Get();

        if (_pool.ActiveCount > _currentPeak)
            _currentPeak = _pool.ActiveCount;

        return obj;
    }

    public void Return(T obj) => _pool.Return(obj);

    /// <summary>Call once per frame to track usage and auto-resize.</summary>
    public void Update(float dt)
    {
        _timer += dt;
        if (_timer < _checkInterval) return;
        _timer = 0;

        _peakHistory.Enqueue(_currentPeak);
        _currentPeak = _pool.ActiveCount;

        while (_peakHistory.Count > _historyLength)
            _peakHistory.Dequeue();

        if (_peakHistory.Count < 3) return; // Need enough data

        int maxPeak = _peakHistory.Max();
        int totalCapacity = _pool.TotalSize;
        float utilization = totalCapacity > 0 ? (float)maxPeak / totalCapacity : 1f;

        if (utilization > _growThreshold && totalCapacity < _maxSize)
        {
            // Pool is getting exhausted — grow
            int newCapacity = Math.Min(_maxSize, (int)(totalCapacity * _growFactor));
            int toAdd = newCapacity - totalCapacity;
            for (int i = 0; i < toAdd; i++)
            {
                var obj = _factory();
                _onReturn?.Invoke(obj);
                _pool.Return(obj);
            }
        }
        else if (utilization < _shrinkThreshold && totalCapacity > _minSize)
        {
            // Pool is oversized — don't actively shrink (let max cap handle it).
            // Just log for developer awareness.
            System.Diagnostics.Debug.WriteLine(
                $"[AdaptivePool] Underutilized: peak {maxPeak}/{totalCapacity} " +
                $"({utilization:P0}). Consider reducing initial size.");
        }
    }
}
```

---

## 13 — Thread Safety

MonoGame games are typically single-threaded (game loop on one thread). But if you use background threads for asset loading, pathfinding, or networking, you may need thread-safe pools.

### Thread-Safe Object Pool

```csharp
namespace MyGame.Core;

/// <summary>
/// Thread-safe ObjectPool using ConcurrentBag.
/// ~2-3x slower than single-threaded Stack version.
/// Use ONLY when pool is accessed from multiple threads.
/// </summary>
public sealed class ConcurrentObjectPool<T> where T : class
{
    private readonly System.Collections.Concurrent.ConcurrentBag<T> _available = new();
    private readonly Func<T> _factory;
    private readonly Action<T>? _onGet;
    private readonly Action<T>? _onReturn;
    private readonly int _maxSize;
    private int _activeCount;

    public int ActiveCount => _activeCount;

    public ConcurrentObjectPool(
        Func<T> factory,
        int initialCapacity = 64,
        int maxSize = 1024,
        Action<T>? onGet = null,
        Action<T>? onReturn = null)
    {
        _factory = factory;
        _maxSize = maxSize;
        _onGet = onGet;
        _onReturn = onReturn;

        for (int i = 0; i < initialCapacity; i++)
            _available.Add(_factory());
    }

    public T Get()
    {
        if (!_available.TryTake(out var obj))
            obj = _factory();

        _onGet?.Invoke(obj);
        Interlocked.Increment(ref _activeCount);
        return obj;
    }

    public void Return(T obj)
    {
        _onReturn?.Invoke(obj);
        Interlocked.Decrement(ref _activeCount);

        if (_available.Count < _maxSize)
            _available.Add(obj);
    }
}
```

### When to Use Thread-Safe Pools

| Scenario | Thread-Safe Needed? |
|----------|-------------------|
| Game loop only | No — use `ObjectPool<T>` |
| Background pathfinding | Yes — path nodes shared between threads |
| Async asset loading | Usually no — load on background, pool on main |
| Network packet processing | Yes — network thread creates, game thread consumes |
| Parallel physics | Yes — but consider per-thread pools instead |

### Per-Thread Pool (Best Performance)

For scenarios like parallel pathfinding, per-thread pools avoid contention entirely:

```csharp
namespace MyGame.Core;

/// <summary>
/// Thread-local pool — each thread gets its own pool.
/// Zero contention. Best performance for parallel workloads.
/// </summary>
public sealed class ThreadLocalPool<T> where T : class
{
    [ThreadStatic]
    private static ObjectPool<T>? _localPool;

    private readonly Func<T> _factory;
    private readonly int _initialCapacity;
    private readonly int _maxSize;

    public ThreadLocalPool(Func<T> factory, int initialCapacity = 32, int maxSize = 256)
    {
        _factory = factory;
        _initialCapacity = initialCapacity;
        _maxSize = maxSize;
    }

    private ObjectPool<T> GetPool()
    {
        return _localPool ??= new ObjectPool<T>(_factory, _initialCapacity, _maxSize);
    }

    public T Get() => GetPool().Get();
    public void Return(T obj) => GetPool().Return(obj);
}
```

---

## 14 — Genre-Specific Patterns

### Bullet Hell

The most pool-intensive genre. Thousands of active projectiles, constant spawn/despawn.

```csharp
namespace MyGame.BulletHell;

/// <summary>
/// Bullet hell pool configuration.
/// Key insight: bullets are uniform — same components, different data.
/// One large pool beats many small typed pools.
/// </summary>
public sealed class BulletHellPools
{
    public ProjectilePoolManager PlayerBullets { get; }
    public ProjectilePoolManager EnemyBullets { get; }
    public ParticleBurstPool HitEffects { get; }
    public AudioPool Audio { get; }

    public BulletHellPools(World world)
    {
        // Separate pools for player vs enemy — different collision masks,
        // different pool sizes (enemies shoot way more)
        PlayerBullets = new ProjectilePoolManager(world, prewarm: 128);
        EnemyBullets = new ProjectilePoolManager(world, prewarm: 2048);

        // Particle effects for bullet impacts
        HitEffects = new ParticleBurstPool(maxParticles: 8192);

        // Audio pool with tight per-sound limits to prevent cacophony
        Audio = new AudioPool(maxPerSound: 4, prewarmCount: 4);
    }

    /// <summary>Pool stats for diagnostics.</summary>
    public string GetStatus() =>
        $"Player: {PlayerBullets.ActiveCount}/{PlayerBullets.PooledCount + PlayerBullets.ActiveCount} | " +
        $"Enemy: {EnemyBullets.ActiveCount}/{EnemyBullets.PooledCount + EnemyBullets.ActiveCount} | " +
        $"Particles: {HitEffects.ActiveCount}/{HitEffects.Capacity}";
}
```

**Bullet hell tips:**
- Pre-warm 2000+ enemy bullet entities. Boss patterns can fill 1500+ simultaneously.
- Use struct pools for bullet trails (each bullet may leave 10-20 trail points).
- Audio: cap at 4 concurrent instances per sound type. 2000 bullets hitting walls = 4 sounds, not 2000.
- Consider fixed-timestep physics for bullet pools — variable dt causes tunneling at high speeds.

### Tower Defense

Waves of enemies with predictable composition. Pre-warm between waves.

```csharp
namespace MyGame.TowerDefense;

/// <summary>
/// Tower defense pool architecture.
/// Predictable spawns = perfect pre-warming opportunity.
/// </summary>
public sealed class TDPools
{
    public WavePoolManager Enemies { get; }
    public ProjectilePoolManager TowerProjectiles { get; }
    public ParticleBurstPool ExplosionEffects { get; }
    public VFXManager StatusEffects { get; } // Slow, poison, fire auras
    public AudioPool Audio { get; }

    public TDPools(World world)
    {
        Enemies = new WavePoolManager(world);
        TowerProjectiles = new ProjectilePoolManager(world, prewarm: 256);
        ExplosionEffects = new ParticleBurstPool(maxParticles: 2048);
        StatusEffects = new VFXManager();
        Audio = new AudioPool(maxPerSound: 6, prewarmCount: 4);
    }

    /// <summary>
    /// Called during wave countdown. Reads next wave
    /// and ensures pools are sized correctly.
    /// </summary>
    public void PrepareWave(WaveDefinition wave)
    {
        Enemies.PrepareForWave(wave);

        // Scale tower projectile pool based on tower count × fire rate
        // A map with 20 towers firing every 0.5s needs ~40 projectiles in flight
    }
}
```

**Tower defense tips:**
- Pre-warm enemy pools during the wave countdown (3-5 second gap).
- Tower projectiles are steady-state — pool size = towers × max_in_flight_per_tower.
- AoE explosions need particle burst pools. A single splash tower can trigger 10 bursts/second.
- Pool enemy health bars separately from enemies — they're UI elements with different lifecycle.

### Survival / Crafting

Open-ended spawning with no predictable waves. Adaptive pools work best.

```csharp
namespace MyGame.Survival;

/// <summary>
/// Survival game pool architecture.
/// Spawns are driven by world simulation — unpredictable.
/// Use adaptive pools that grow with demand.
/// </summary>
public sealed class SurvivalPools
{
    public AdaptivePool<TreeResource> Trees { get; }
    public AdaptivePool<RockResource> Rocks { get; }
    public AdaptivePool<DroppedItem> DroppedItems { get; }
    public ProjectilePoolManager Projectiles { get; }
    public ParticleBurstPool HarvestEffects { get; }
    public DecalManager BloodDecals { get; }

    public SurvivalPools(World world)
    {
        // Resources spawn/despawn as chunks load/unload
        Trees = new AdaptivePool<TreeResource>(
            factory: () => new TreeResource(),
            initialCapacity: 128,
            minSize: 32,
            maxSize: 1024,
            growThreshold: 0.8f
        );

        Rocks = new AdaptivePool<RockResource>(
            factory: () => new RockResource(),
            initialCapacity: 64,
            minSize: 16,
            maxSize: 512
        );

        // Items drop from harvesting, combat, crafting
        DroppedItems = new AdaptivePool<DroppedItem>(
            factory: () => new DroppedItem(),
            initialCapacity: 64,
            maxSize: 256
        );

        Projectiles = new ProjectilePoolManager(world, prewarm: 64);
        HarvestEffects = new ParticleBurstPool(maxParticles: 1024);
        BloodDecals = new DecalManager(maxDecals: 128);
    }

    public void Update(float dt)
    {
        Trees.Update(dt);
        Rocks.Update(dt);
        DroppedItems.Update(dt);
        HarvestEffects.Update(dt);
        BloodDecals.Update(dt);
    }
}

// Placeholder types for the example
public class TreeResource { public Vector2 Position; public int Health; public bool Active; }
public class RockResource { public Vector2 Position; public int Health; public bool Active; }
public class DroppedItem { public Vector2 Position; public string ItemId = ""; public float Lifetime; }
```

**Survival tips:**
- Chunk loading/unloading is the biggest pool stress test. When a new chunk loads, 50-100 resource entities may spawn.
- Dropped items need a global cap — 256 is usually enough. Oldest items despawn when cap is reached.
- Blood/damage decals should use struct pools with fixed capacity and overwrite-oldest behavior.
- Adaptive pools shine here because player behavior is unpredictable (combat-heavy vs. builder vs. explorer).

### Roguelike / Dungeon Crawler

Room-based spawning with floor transitions.

**Roguelike tips:**
- Pre-warm enemy pools per room when the player enters (door transition = natural loading moment).
- Projectile pools are moderate — 64-128 unless the player build is spell-heavy.
- Loot drops should pool because rooms can drop 10-20 items at once (chest + enemy drops).
- On floor transition, return ALL pooled entities and re-warm for the new floor's enemy roster.

---

## 15 — Common Mistakes & Anti-Patterns

### ❌ Forgetting to Return

The #1 pooling bug. Objects leak from the pool and eventually exhaust it.

```csharp
// ❌ BUG: Bullet goes off-screen but is never returned
if (bullet.Position.X > screenWidth)
    continue; // Just skips it — never returned!

// ✅ FIX: Always return
if (bullet.Position.X > screenWidth)
{
    pool.Return(bullet);
    activeBullets.RemoveAt(i);
    continue;
}
```

**Prevention:** Track active count in diagnostics. If ActiveCount rises monotonically, something isn't returning.

### ❌ Using Pooled Object After Return

Returning an object to the pool doesn't clear references you hold to it.

```csharp
// ❌ BUG: Using a returned bullet
var bullet = pool.Get();
activeBullets.Add(bullet);
// ... later ...
pool.Return(bullet);
activeBullets.RemoveAt(i);
// ... but somewhere else still has a reference to bullet!
otherSystem.ProcessBullet(bullet); // Operating on a pooled object!
```

**Prevention:** Use `onReturn` to null out or flag the object. Check an `IsActive` flag before operating.

### ❌ Missing Pooled Filter in ECS Queries

```csharp
// ❌ BUG: Processes invisible pooled entities
var query = new QueryDescription().WithAll<Position, Health>();

// ✅ FIX: Always exclude pooled
var query = new QueryDescription()
    .WithAll<Position, Health>()
    .WithNone<Pooled>();
```

**Prevention:** Create queries through a helper that always adds `.WithNone<Pooled>()`.

### ❌ Resetting Partially

Pooled objects carry state from their previous life. Miss one field and you get ghost behavior.

```csharp
// ❌ BUG: Forgot to reset PierceCount
public void Activate(Vector2 pos, Vector2 vel, int damage)
{
    Position = pos;
    Velocity = vel;
    Damage = damage;
    // PierceCount still has value from last use!
}

// ✅ FIX: Reset EVERYTHING
public void Activate(Vector2 pos, Vector2 vel, int damage)
{
    Position = pos;
    Velocity = vel;
    Damage = damage;
    PierceCount = 0;
    Timer = 0;
    Active = true;
    // ... all fields
}
```

**Prevention:** Use the `IPoolable` interface or `onGet` callback. Or use `record struct` with `with` expressions to ensure all fields are set explicitly.

### ❌ Pooling Everything

Not everything needs pooling. Over-pooling adds complexity for no benefit.

```csharp
// ❌ OVER-ENGINEERING: Pooling the player entity (created once)
var player = playerPool.Get();

// ❌ OVER-ENGINEERING: Pooling config objects (created at startup)
var settings = settingsPool.Get();

// ✅ Pool when: creation rate > ~20/sec OR GC shows spikes
```

### ❌ Enormous Initial Pools

```csharp
// ❌ Wastes 100MB of memory on startup
var pool = new ObjectPool<Enemy>(factory, initialCapacity: 100_000);

// ✅ Right-size based on expected peak
var pool = new ObjectPool<Enemy>(factory, initialCapacity: 64, maxSize: 512);
```

### ❌ Double-Return

Returning the same object twice corrupts the pool.

```csharp
// ❌ BUG: Double return
pool.Return(bullet); // Returned here...
// ... later, different code path ...
pool.Return(bullet); // Returned AGAIN — now pool has duplicate reference!

// When both copies are Get()'d, two consumers share the same object = chaos
```

**Prevention in debug builds:**

```csharp
public void Return(T obj)
{
    #if DEBUG
    if (_available.Contains(obj))
        throw new InvalidOperationException($"Double-return detected for {obj}!");
    #endif

    _onReturn?.Invoke(obj);
    ActiveCount--;

    if (_available.Count < _maxSize)
        _available.Push(obj);
}
```

---

## 16 — Tuning Reference

### Pool Size Guidelines

| Entity Type | Typical Initial | Typical Max | Notes |
|-------------|----------------|-------------|-------|
| Player bullets | 64-128 | 256 | Depends on fire rate + bullet lifetime |
| Enemy bullets (bullet hell) | 1024-2048 | 4096 | Boss patterns can use 1500+ |
| Enemy bullets (normal) | 64-128 | 512 | Scale with enemy count |
| Enemies (wave-based) | Wave peak × 1.2 | Wave peak × 2 | Pre-warm between waves |
| Enemies (open world) | 32-64 | 256 | Adaptive pool recommended |
| Particles (struct pool) | 2048-4096 | 8192 | Cheap — err on the side of more |
| Damage numbers | 16-32 | 64 | 4-8 visible at once typical |
| VFX (hit sparks, etc.) | 16-32 | 64 | Per effect type |
| Audio instances | 4-8 per sound | 8-16 | More = audio cacophony |
| Trail points | 64-128 per trail | 256 | Per entity with trail |
| Decals (blood, craters) | 128-256 | 512 | Cap at visual limit |
| Dropped items | 32-64 | 256 | Cap prevents item spam |
| Pathfinding nodes | 256-512 | 2048 | Per concurrent pathfind |
| UI list items | Viewport items × 2 | Viewport × 3 | Virtualized list |

### Memory Budget

| Pool | Per-Object Size | 1000 Objects | Notes |
|------|-----------------|-------------|-------|
| Struct (particle) | 32-64 bytes | 32-64 KB | Contiguous array |
| Simple component (position + velocity) | 16-32 bytes | 16-32 KB | Arch archetype storage |
| Full entity (6-8 components) | 128-256 bytes | 128-256 KB | Arch overhead included |
| Reference type (class) | 48-128 bytes + 24 header | 72-152 KB | Plus GC tracking overhead |
| Audio instance | ~200 bytes | 200 KB | XNA SoundEffectInstance |

### Performance Characteristics

| Operation | ObjectPool (Stack) | ConcurrentPool (Bag) | StructPool (Array) | EcsPool (Arch) |
|-----------|-------------------|---------------------|-------------------|---------------|
| Get | ~5 ns | ~30 ns | ~3 ns | ~50 ns (Remove component) |
| Return | ~5 ns | ~25 ns | ~5 ns (swap) | ~50 ns (Add component) |
| Iteration | N/A (no iteration) | N/A | ~0.5 ns/item (cache-friendly) | ~2 ns/item (archetype query) |
| Memory overhead | 24 bytes/slot (Stack) | 40 bytes/slot (Bag) | 0 (flat array) | ~8 bytes/entity (ID + version) |

### Decision Flowchart

```
Need to pool something?
│
├─ Is it a value type < 128 bytes?
│  ├─ Yes: High count (>500)? → StructPool
│  └─ No: ObjectPool<T>
│
├─ Is it an ECS entity?
│  ├─ Yes: High churn (>50/sec)? → EcsPool with Pooled tag
│  └─ No: Just create/destroy (Arch handles it)
│
├─ Is it accessed from multiple threads?
│  ├─ Yes: Few threads? → ConcurrentObjectPool
│  │       Many threads? → ThreadLocalPool
│  └─ No: ObjectPool<T>
│
├─ Does spawn count vary wildly?
│  ├─ Yes: AdaptivePool
│  └─ No: Fixed ObjectPool with scene-based sizing
│
└─ Is it an audio instance?
   └─ Yes: AudioPool (with per-sound limits)
```

### Integration Checklist

When adding pooling to an existing system:

1. **Measure first** — Profile with `GCMonitor`. If Gen0 collections are < 2/sec during gameplay, pooling may not be necessary.
2. **Identify hotspots** — Use Visual Studio Profiler or `dotnet-trace` to find allocation-heavy call sites.
3. **Start with the biggest offender** — Usually projectiles or particles.
4. **Add diagnostics immediately** — `PoolDiagnostics` from day one. You'll need the data.
5. **Test edge cases** — What happens when the pool is empty? When all objects are active? When the game runs for 2 hours?
6. **Size conservatively, then tune** — Start small (64), let adaptive sizing or diagnostics guide you up.
7. **Document pool relationships** — Which systems Get from which pools? Which Return? A mismatch = leak.

---

## Related Guides

- **[G64 Combat & Damage §9](./G64_combat_damage_systems.md)** — Projectile-specific pooling patterns
- **[G23 Particles](./G23_particles.md)** — Struct-based particle pool with emitters
- **[G6 Audio](./G6_audio.md)** — SoundEffect management
- **[P12 Performance Budget](./P12_performance_budget.md)** — Frame budget and GC targets
- **[G4 AI Systems](./G4_ai_systems.md)** — Pathfinding node allocation
- **[G10 Custom Game Systems §9](./G10_custom_game_systems.md)** — Wave/spawn system integration
