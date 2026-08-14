# G64 — Combat & Damage Systems

> **Category:** Guide · **Related:** [G3 Physics & Collision](./G3_physics_and_collision.md) · [G52 Character Controller](./G52_character_controller.md) · [G31 Animation State Machines](./G31_animation_state_machines.md) · [G30 Game Feel Tooling](./G30_game_feel_tooling.md) · [G10 Custom Game Systems §7 Status Effects](./G10_custom_game_systems.md)

> A complete implementation guide for combat, health, damage, hitbox/hurtbox, and knockback systems using MonoGame + Arch ECS. Covers both action (real-time) and turn-based patterns. Everything is composable — pick the pieces your genre needs.

---

## Table of Contents

1. [Design Philosophy](#1--design-philosophy)
2. [Health & Damage Components](#2--health--damage-components)
3. [Hitbox/Hurtbox System](#3--hitboxhurtbox-system)
4. [Damage Pipeline](#4--damage-pipeline)
5. [Invincibility Frames](#5--invincibility-frames)
6. [Knockback & Hit Reactions](#6--knockback--hit-reactions)
7. [Hitstop & Screen Shake](#7--hitstop--screen-shake)
8. [Projectile System](#8--projectile-system)
9. [Object Pooling](#9--object-pooling)
10. [Melee Attack System](#10--melee-attack-system)
11. [Damage Types & Resistances](#11--damage-types--resistances)
12. [Critical Hits & Damage Variance](#12--critical-hits--damage-variance)
13. [Turn-Based Combat Adapter](#13--turn-based-combat-adapter)
14. [Death & Respawn](#14--death--respawn)
15. [Damage Numbers & Feedback](#15--damage-numbers--feedback)
16. [Combat Tuning Reference](#16--combat-tuning-reference)

---

## 1 — Design Philosophy

### Data-Driven, Not Hard-Coded

Combat is the system players interact with most. Hard-coding damage values, cooldowns, or hitbox sizes into code makes balancing a nightmare. Instead:

- **Define weapons/attacks as data** (JSON, records, or a registry).
- **Route all damage through a single pipeline** so modifiers, resistances, and status effects apply uniformly.
- **Separate hit detection from damage application** — hitboxes detect collisions, the damage pipeline resolves outcomes.

### The Damage Pipeline

Every hit in the game flows through the same path:

```
Attack → Hit Detection → Damage Event → Modifiers → Apply → Feedback
```

This means a sword swing, a fireball, a poison tick, and fall damage all use the same resolution code. Add new damage sources by creating events, not by writing new systems.

### ECS Fit

Combat maps naturally onto ECS:

| Concept | ECS Role |
|---------|----------|
| Health, armor, resistances | Components on damageable entities |
| Hitbox, hurtbox | Components with shape data |
| Attacks | Short-lived entities or components with frame data |
| Damage events | Entities with `DamageEvent` components (processed and destroyed each frame) |
| Knockback | Velocity impulse applied to `Velocity` component |

---

## 2 — Health & Damage Components

### Core Components

```csharp
namespace MyGame.Combat;

/// <summary>Any entity that can take damage.</summary>
public record struct Health(
    float Current,
    float Max,
    float RegenPerSecond  // 0 for no regen
)
{
    public readonly float Ratio => Max > 0 ? Current / Max : 0f;
    public readonly bool IsDead => Current <= 0;
}

/// <summary>Optional shield/armor that absorbs damage before health.</summary>
public record struct Armor(
    float Current,
    float Max,
    float DamageReduction  // 0.0–1.0, percentage absorbed
);

/// <summary>Marks an entity as invincible for a duration (i-frames).</summary>
public record struct Invincible(float RemainingSeconds);

/// <summary>Tag: entity just died this frame.</summary>
public struct JustDied;

/// <summary>Tag: entity is alive and can be targeted.</summary>
public struct Alive;
```

### Health Regen System

```csharp
namespace MyGame.Combat;

using Arch.Core;

public static class HealthRegenSystem
{
    private static readonly QueryDescription Query = new QueryDescription()
        .WithAll<Health, Alive>();

    public static void Update(World world, float dt)
    {
        world.Query(in Query, (ref Health health) =>
        {
            if (health.RegenPerSecond <= 0 || health.Current >= health.Max)
                return;

            health.Current = MathF.Min(
                health.Current + health.RegenPerSecond * dt,
                health.Max
            );
        });
    }
}
```

---

## 3 — Hitbox/Hurtbox System

Hitboxes deal damage. Hurtboxes receive damage. They exist as separate components so an entity can have both (e.g., a player's sword hitbox + body hurtbox).

### Components

```csharp
namespace MyGame.Combat;

using Microsoft.Xna.Framework;

/// <summary>
/// A rectangle relative to entity position that can deal damage.
/// Active only during specific animation frames.
/// </summary>
public record struct Hitbox(
    Rectangle LocalBounds,   // Offset from entity position
    int Damage,
    float KnockbackForce,
    DamageType DamageType,
    int TeamId               // Prevents friendly fire
);

/// <summary>
/// A rectangle relative to entity position that receives damage.
/// </summary>
public record struct Hurtbox(
    Rectangle LocalBounds,
    int TeamId
);

/// <summary>Marks a hitbox as currently active (only checks active hitboxes).</summary>
public struct HitboxActive;

/// <summary>
/// Tracks which entities a hitbox has already hit this activation
/// to prevent multi-hit per swing.
/// </summary>
public record struct HitboxHitList(HashSet<Entity> AlreadyHit)
{
    public HitboxHitList() : this(new HashSet<Entity>()) { }
}

public enum DamageType
{
    Physical,
    Fire,
    Ice,
    Lightning,
    Poison,
    Holy,
    Dark,
    Pure  // Ignores all resistances
}
```

### World-Space Bounds Helper

```csharp
namespace MyGame.Combat;

using Microsoft.Xna.Framework;

public static class HitboxHelper
{
    /// <summary>
    /// Convert local-space hitbox/hurtbox bounds to world-space,
    /// accounting for entity position and facing direction.
    /// </summary>
    public static Rectangle ToWorldBounds(
        Rectangle localBounds,
        Vector2 entityPosition,
        bool facingLeft)
    {
        int x = facingLeft
            ? (int)entityPosition.X - localBounds.Right
            : (int)entityPosition.X + localBounds.X;

        int y = (int)entityPosition.Y + localBounds.Y;

        return new Rectangle(x, y, localBounds.Width, localBounds.Height);
    }
}
```

### Collision Detection System

```csharp
namespace MyGame.Combat;

using Arch.Core;
using Arch.Core.Extensions;
using Microsoft.Xna.Framework;

/// <summary>
/// Checks all active hitboxes against all hurtboxes each frame.
/// Creates DamageEvent entities for each new hit.
/// </summary>
public static class HitDetectionSystem
{
    private static readonly QueryDescription HitboxQuery = new QueryDescription()
        .WithAll<Hitbox, HitboxActive, HitboxHitList, Position, Facing>();

    private static readonly QueryDescription HurtboxQuery = new QueryDescription()
        .WithAll<Hurtbox, Position, Alive>();

    public static void Update(World world)
    {
        // Collect hurtbox entities (can't nest world.Query calls)
        var hurtboxes = new List<(Entity Entity, Rectangle WorldBounds, int TeamId)>();

        world.Query(in HurtboxQuery, (Entity entity, ref Hurtbox hurtbox, ref Position pos) =>
        {
            var worldBounds = HitboxHelper.ToWorldBounds(
                hurtbox.LocalBounds, pos.Value, false);
            hurtboxes.Add((entity, worldBounds, hurtbox.TeamId));
        });

        // Check each active hitbox against all hurtboxes
        world.Query(in HitboxQuery,
            (Entity attacker, ref Hitbox hitbox, ref HitboxHitList hitList,
             ref Position pos, ref Facing facing) =>
        {
            var attackBounds = HitboxHelper.ToWorldBounds(
                hitbox.LocalBounds, pos.Value, facing.Left);

            foreach (var (target, targetBounds, targetTeam) in hurtboxes)
            {
                // Skip same team (no friendly fire)
                if (hitbox.TeamId == targetTeam) continue;

                // Skip already hit this activation
                if (hitList.AlreadyHit.Contains(target)) continue;

                // Skip self
                if (attacker == target) continue;

                // AABB intersection test
                if (!attackBounds.Intersects(targetBounds)) continue;

                // Hit detected — create damage event
                hitList.AlreadyHit.Add(target);

                world.Create(new DamageEvent(
                    Source: attacker,
                    Target: target,
                    BaseDamage: hitbox.Damage,
                    DamageType: hitbox.DamageType,
                    KnockbackForce: hitbox.KnockbackForce,
                    KnockbackDirection: GetKnockbackDirection(pos.Value, targetBounds),
                    IsCritical: false  // Resolved in damage pipeline
                ));
            }
        });
    }

    private static Vector2 GetKnockbackDirection(Vector2 attackerPos, Rectangle targetBounds)
    {
        var targetCenter = targetBounds.Center.ToVector2();
        var dir = targetCenter - attackerPos;
        return dir == Vector2.Zero ? Vector2.UnitX : Vector2.Normalize(dir);
    }
}

// --- Supporting components (if not already in your project) ---
public record struct Position(Vector2 Value);
public record struct Facing(bool Left);
```

---

## 4 — Damage Pipeline

The central system that resolves all damage. Every damage source in the game creates a `DamageEvent` entity; this system processes them.

### Damage Event

```csharp
namespace MyGame.Combat;

using Arch.Core;
using Microsoft.Xna.Framework;

/// <summary>
/// A one-frame entity representing a pending damage application.
/// Created by hit detection, projectiles, DoTs, environmental hazards, etc.
/// </summary>
public record struct DamageEvent(
    Entity Source,
    Entity Target,
    int BaseDamage,
    DamageType DamageType,
    float KnockbackForce,
    Vector2 KnockbackDirection,
    bool IsCritical
);
```

### Damage Resolution System

```csharp
namespace MyGame.Combat;

using Arch.Core;
using Arch.Core.Extensions;
using Microsoft.Xna.Framework;

/// <summary>
/// Processes all DamageEvent entities each frame:
/// 1. Skip if target is invincible or dead
/// 2. Apply resistances
/// 3. Apply armor absorption
/// 4. Subtract from health
/// 5. Apply knockback
/// 6. Grant i-frames
/// 7. Emit feedback (damage numbers, sounds, screen shake)
/// 8. Destroy the event entity
/// </summary>
public static class DamageResolutionSystem
{
    private static readonly QueryDescription EventQuery = new QueryDescription()
        .WithAll<DamageEvent>();

    public static void Update(World world, GameFeedback feedback)
    {
        var toDestroy = new List<Entity>();
        var deathCandidates = new List<Entity>();

        world.Query(in EventQuery, (Entity eventEntity, ref DamageEvent evt) =>
        {
            toDestroy.Add(eventEntity);

            // Target still valid?
            if (!world.IsAlive(evt.Target)) return;

            // Target invincible?
            if (world.Has<Invincible>(evt.Target)) return;

            // Target already dead?
            if (!world.Has<Alive>(evt.Target)) return;

            // --- Resolve damage amount ---
            float damage = evt.BaseDamage;

            // Apply resistances (if target has them)
            if (world.TryGet<DamageResistances>(evt.Target, out var resistances))
            {
                float resist = resistances.GetResistance(evt.DamageType);
                damage *= (1f - MathHelper.Clamp(resist, -1f, 0.95f));
                // Negative resistance = vulnerability (takes MORE damage)
            }

            // Critical hit multiplier
            if (evt.IsCritical)
                damage *= 1.5f; // Or pull from attacker stats

            // Apply armor absorption
            if (world.TryGet<Armor>(evt.Target, out var armor) && armor.Current > 0)
            {
                float absorbed = damage * armor.DamageReduction;
                float armorDamage = MathF.Min(absorbed, armor.Current);
                armor = armor with { Current = armor.Current - armorDamage };
                world.Set(evt.Target, armor);
                damage -= armorDamage;
            }

            // Floor to int, minimum 1 (unless fully resisted)
            int finalDamage = MathF.Max(1, MathF.Round(damage));

            // --- Apply to health ---
            ref var health = ref world.Get<Health>(evt.Target);
            health = health with { Current = health.Current - finalDamage };

            // --- Knockback ---
            if (evt.KnockbackForce > 0 && world.Has<Velocity>(evt.Target))
            {
                ref var vel = ref world.Get<Velocity>(evt.Target);
                vel = vel with {
                    Value = vel.Value + evt.KnockbackDirection * evt.KnockbackForce
                };
            }

            // --- Grant i-frames ---
            if (!world.Has<Invincible>(evt.Target))
                world.Add(evt.Target, new Invincible(0.5f)); // 0.5s default

            // --- Feedback ---
            var targetPos = world.Get<Position>(evt.Target).Value;
            feedback.SpawnDamageNumber(targetPos, finalDamage, evt.IsCritical);
            feedback.PlayHitSound(evt.DamageType);

            if (evt.IsCritical)
                feedback.RequestScreenShake(0.15f, 4f);

            if (evt.KnockbackForce > 10f)
                feedback.RequestHitstop(3); // 3 frames of freeze

            // --- Check death ---
            if (health.IsDead)
                deathCandidates.Add(evt.Target);
        });

        // Clean up events
        foreach (var e in toDestroy)
            world.Destroy(e);

        // Process deaths
        foreach (var e in deathCandidates)
        {
            if (world.IsAlive(e) && world.Has<Alive>(e))
            {
                world.Remove<Alive>(e);
                world.Add(e, new JustDied());
            }
        }
    }
}

// --- Supporting components ---
public record struct Velocity(Vector2 Value);
```

---

## 5 — Invincibility Frames

I-frames prevent damage stacking. The player flashes, is untargetable, and the timer counts down.

```csharp
namespace MyGame.Combat;

using Arch.Core;
using Arch.Core.Extensions;

public static class InvincibilitySystem
{
    private static readonly QueryDescription Query = new QueryDescription()
        .WithAll<Invincible>();

    public static void Update(World world, float dt)
    {
        var toRemove = new List<Entity>();

        world.Query(in Query, (Entity entity, ref Invincible inv) =>
        {
            inv = inv with { RemainingSeconds = inv.RemainingSeconds - dt };
            if (inv.RemainingSeconds <= 0)
                toRemove.Add(entity);
        });

        foreach (var e in toRemove)
        {
            if (world.IsAlive(e))
                world.Remove<Invincible>(e);
        }
    }
}
```

### Visual Flashing

```csharp
namespace MyGame.Combat;

using Arch.Core;
using Microsoft.Xna.Framework;

/// <summary>
/// Makes invincible entities flash by toggling sprite visibility
/// at a fixed rate (e.g., 10 Hz).
/// </summary>
public static class InvincibilityRenderSystem
{
    private const float FlashRate = 10f; // Flashes per second

    private static readonly QueryDescription Query = new QueryDescription()
        .WithAll<Invincible, SpriteRenderer>();

    public static void Update(World world, float totalTime)
    {
        world.Query(in Query, (ref Invincible inv, ref SpriteRenderer sprite) =>
        {
            // Toggle visibility using sin wave
            bool visible = MathF.Sin(totalTime * FlashRate * MathF.PI * 2) > 0;
            sprite = sprite with { Visible = visible };
        });
    }
}

public record struct SpriteRenderer(bool Visible, Color Tint);
```

---

## 6 — Knockback & Hit Reactions

Knockback sells impact. There are two common approaches:

### Impulse Knockback (Simple)

Already shown in the damage pipeline above — add a velocity impulse. Works well for top-down and platformer games.

### Curve-Based Knockback (Polished)

For fighting games and metroidvanias, use a knockback curve that starts fast and decelerates:

```csharp
namespace MyGame.Combat;

using Microsoft.Xna.Framework;

/// <summary>
/// Knockback state that applies a decaying velocity over time.
/// Overrides normal movement while active.
/// </summary>
public record struct KnockbackState(
    Vector2 Direction,
    float Force,
    float Duration,
    float Elapsed
)
{
    /// <summary>Current knockback velocity using exponential decay.</summary>
    public readonly Vector2 CurrentVelocity
    {
        get
        {
            float t = Duration > 0 ? Elapsed / Duration : 1f;
            // Exponential ease-out: strong start, gentle end
            float strength = Force * MathF.Pow(1f - t, 2f);
            return Direction * strength;
        }
    }

    public readonly bool IsFinished => Elapsed >= Duration;
}
```

```csharp
namespace MyGame.Combat;

using Arch.Core;
using Arch.Core.Extensions;

public static class KnockbackSystem
{
    private static readonly QueryDescription Query = new QueryDescription()
        .WithAll<KnockbackState, Velocity>();

    public static void Update(World world, float dt)
    {
        var toRemove = new List<Entity>();

        world.Query(in Query, (Entity entity, ref KnockbackState kb, ref Velocity vel) =>
        {
            kb = kb with { Elapsed = kb.Elapsed + dt };
            vel = vel with { Value = kb.CurrentVelocity };

            if (kb.IsFinished)
                toRemove.Add(entity);
        });

        foreach (var e in toRemove)
        {
            if (world.IsAlive(e))
                world.Remove<KnockbackState>(e);
        }
    }
}
```

---

## 7 — Hitstop & Screen Shake

These two techniques are the difference between combat that feels flat and combat that feels *crunchy*.

### Hitstop (Frame Freeze)

Freeze the game for 2–5 frames on big hits. Both attacker and target freeze.

```csharp
namespace MyGame.Combat;

/// <summary>
/// Global hitstop manager. When active, game logic skips updates
/// but rendering continues (so the freeze frame is visible).
/// </summary>
public sealed class HitstopManager
{
    private int _freezeFrames;

    public bool IsActive => _freezeFrames > 0;

    public void Request(int frames)
    {
        // Take the larger of current and requested (don't stack)
        _freezeFrames = Math.Max(_freezeFrames, frames);
    }

    /// <summary>Call at the START of Update. Returns true if game should skip this frame.</summary>
    public bool Tick()
    {
        if (_freezeFrames <= 0) return false;
        _freezeFrames--;
        return true;
    }
}
```

### Integration in Game Loop

```csharp
// In your main Update method:
protected override void Update(GameTime gameTime)
{
    float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

    // Hitstop: skip gameplay but still update visual effects
    if (_hitstopManager.Tick())
    {
        // Still update: particles, screen shake, UI
        _particlePool.Update(dt);
        _screenShake.Update(dt);
        return; // Skip all gameplay systems
    }

    // Normal update: all systems run
    HitDetectionSystem.Update(_world);
    DamageResolutionSystem.Update(_world, _feedback);
    InvincibilitySystem.Update(_world, dt);
    KnockbackSystem.Update(_world, dt);
    // ... other systems
}
```

### Screen Shake

```csharp
namespace MyGame.Combat;

using Microsoft.Xna.Framework;

/// <summary>
/// Perlin-noise-based screen shake with trauma decay.
/// Add trauma (0–1) on impacts; it decays over time.
/// </summary>
public sealed class ScreenShake
{
    private float _trauma;   // 0–1, current shake intensity
    private float _decay;    // Trauma removed per second
    private float _maxOffset; // Maximum pixel displacement
    private float _time;

    public ScreenShake(float maxOffset = 8f, float decay = 3f)
    {
        _maxOffset = maxOffset;
        _decay = decay;
    }

    /// <summary>Add trauma (clamped to 1). Use 0.2–0.4 for light hits, 0.6–1.0 for heavy.</summary>
    public void AddTrauma(float amount) =>
        _trauma = MathF.Min(_trauma + amount, 1f);

    public void Update(float dt)
    {
        _time += dt;
        _trauma = MathF.Max(_trauma - _decay * dt, 0f);
    }

    /// <summary>
    /// Current camera offset to apply. Use trauma² for exponential falloff.
    /// </summary>
    public Vector2 Offset
    {
        get
        {
            if (_trauma <= 0.01f) return Vector2.Zero;

            float shake = _trauma * _trauma; // Quadratic falloff
            float offsetX = _maxOffset * shake * PerlinNoise(_time * 25f, 0);
            float offsetY = _maxOffset * shake * PerlinNoise(0, _time * 25f);

            return new Vector2(offsetX, offsetY);
        }
    }

    // Simplified noise — replace with proper Perlin for smoother results
    private static float PerlinNoise(float x, float y)
    {
        int xi = (int)MathF.Floor(x);
        float t = x - xi;
        float a = HashFloat(xi, (int)y);
        float b = HashFloat(xi + 1, (int)y);
        return MathHelper.Lerp(a, b, t * t * (3 - 2 * t));
    }

    private static float HashFloat(int x, int y)
    {
        int h = x * 374761393 + y * 668265263;
        h = (h ^ (h >> 13)) * 1274126177;
        return (h & 0x7FFFFFFF) / (float)0x7FFFFFFF * 2f - 1f;
    }
}
```

---

## 8 — Projectile System

Bullets, arrows, fireballs — anything that travels and deals damage on contact.

### Components

```csharp
namespace MyGame.Combat;

using Microsoft.Xna.Framework;

/// <summary>A traveling damage source.</summary>
public record struct Projectile(
    Vector2 Direction,
    float Speed,
    int Damage,
    DamageType DamageType,
    float KnockbackForce,
    int TeamId,
    float MaxLifetime,       // Auto-destroy after this many seconds
    float Elapsed,
    bool PierceTargets,      // Pass through or destroy on hit?
    int MaxPierceCount       // -1 = infinite
);

/// <summary>Tracks entities already hit by a piercing projectile.</summary>
public record struct ProjectileHitList(HashSet<Entity> AlreadyHit)
{
    public ProjectileHitList() : this(new HashSet<Entity>()) { }
}
```

### Projectile Movement & Collision System

```csharp
namespace MyGame.Combat;

using Arch.Core;
using Arch.Core.Extensions;
using Microsoft.Xna.Framework;

public static class ProjectileSystem
{
    private static readonly QueryDescription ProjectileQuery = new QueryDescription()
        .WithAll<Projectile, Position>();

    private static readonly QueryDescription HurtboxQuery = new QueryDescription()
        .WithAll<Hurtbox, Position, Alive>();

    public static void Update(World world, float dt)
    {
        var toDestroy = new List<Entity>();

        // Collect hurtboxes
        var hurtboxes = new List<(Entity Entity, Rectangle Bounds, int TeamId)>();
        world.Query(in HurtboxQuery, (Entity e, ref Hurtbox hb, ref Position pos) =>
        {
            hurtboxes.Add((e, HitboxHelper.ToWorldBounds(hb.LocalBounds, pos.Value, false), hb.TeamId));
        });

        world.Query(in ProjectileQuery, (Entity entity, ref Projectile proj, ref Position pos) =>
        {
            // Move
            pos = pos with { Value = pos.Value + proj.Direction * proj.Speed * dt };
            proj = proj with { Elapsed = proj.Elapsed + dt };

            // Lifetime expiry
            if (proj.Elapsed >= proj.MaxLifetime)
            {
                toDestroy.Add(entity);
                return;
            }

            // Collision check (simple point-in-rect or small rect)
            var projRect = new Rectangle(
                (int)pos.Value.X - 4, (int)pos.Value.Y - 4, 8, 8);

            var hitList = world.Has<ProjectileHitList>(entity)
                ? world.Get<ProjectileHitList>(entity)
                : new ProjectileHitList();

            foreach (var (target, bounds, teamId) in hurtboxes)
            {
                if (teamId == proj.TeamId) continue;
                if (hitList.AlreadyHit.Contains(target)) continue;
                if (!projRect.Intersects(bounds)) continue;

                // Create damage event
                var knockbackDir = proj.Direction;
                world.Create(new DamageEvent(
                    Source: entity,
                    Target: target,
                    BaseDamage: proj.Damage,
                    DamageType: proj.DamageType,
                    KnockbackForce: proj.KnockbackForce,
                    KnockbackDirection: knockbackDir,
                    IsCritical: false
                ));

                if (proj.PierceTargets)
                {
                    hitList.AlreadyHit.Add(target);
                    if (proj.MaxPierceCount > 0 &&
                        hitList.AlreadyHit.Count >= proj.MaxPierceCount)
                    {
                        toDestroy.Add(entity);
                        break;
                    }
                }
                else
                {
                    toDestroy.Add(entity);
                    break;
                }
            }
        });

        foreach (var e in toDestroy)
        {
            if (world.IsAlive(e))
                world.Destroy(e);
        }
    }
}
```

---

## 9 — Object Pooling

Projectile-heavy games (bullet hell, twin-stick shooters) can't afford per-frame allocations. Pool reusable entities.

### Generic Object Pool

```csharp
namespace MyGame.Core;

/// <summary>
/// Generic object pool for any reference type.
/// Pre-allocates objects, reuses them, grows only when necessary.
/// </summary>
public sealed class ObjectPool<T> where T : class
{
    private readonly Stack<T> _available;
    private readonly Func<T> _factory;
    private readonly Action<T>? _onGet;     // Reset state when taken from pool
    private readonly Action<T>? _onReturn;  // Clean up when returned
    private readonly int _maxSize;

    public int ActiveCount { get; private set; }
    public int AvailableCount => _available.Count;

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

        // Pre-warm
        for (int i = 0; i < initialCapacity; i++)
            _available.Push(_factory());
    }

    public T Get()
    {
        var obj = _available.Count > 0 ? _available.Pop() : _factory();
        _onGet?.Invoke(obj);
        ActiveCount++;
        return obj;
    }

    public void Return(T obj)
    {
        _onReturn?.Invoke(obj);
        ActiveCount--;

        if (_available.Count < _maxSize)
            _available.Push(obj);
        // If over max, let GC collect it — this caps memory usage
    }
}
```

### ECS Entity Pool Pattern

For Arch ECS, pooling means recycling entities instead of creating/destroying:

```csharp
namespace MyGame.Combat;

using Arch.Core;
using Arch.Core.Extensions;
using Microsoft.Xna.Framework;

/// <summary>
/// Projectile pool using ECS entity recycling.
/// Inactive projectiles have the Pooled tag; active ones don't.
/// </summary>
public sealed class ProjectilePool
{
    private readonly World _world;
    private readonly Queue<Entity> _inactive = new();

    public ProjectilePool(World world, int prewarm = 128)
    {
        _world = world;

        // Pre-create entities with all required components
        for (int i = 0; i < prewarm; i++)
        {
            var entity = world.Create(
                new Position(Vector2.Zero),
                new Projectile(Vector2.Zero, 0, 0, DamageType.Physical, 0, 0, 0, 0, false, 0),
                new SpriteRenderer(false, Color.White),
                new Pooled()
            );
            _inactive.Enqueue(entity);
        }
    }

    /// <summary>Activate a pooled projectile with the given data.</summary>
    public Entity Spawn(Vector2 position, Projectile data)
    {
        Entity entity;
        if (_inactive.Count > 0)
        {
            entity = _inactive.Dequeue();
            _world.Remove<Pooled>(entity);
        }
        else
        {
            // Pool empty — create new
            entity = _world.Create(
                new Position(position),
                data,
                new SpriteRenderer(true, Color.White)
            );
            return entity;
        }

        _world.Set(entity, new Position(position));
        _world.Set(entity, data);
        _world.Set(entity, new SpriteRenderer(true, Color.White));
        return entity;
    }

    /// <summary>Return a projectile to the pool (call instead of Destroy).</summary>
    public void Despawn(Entity entity)
    {
        if (!_world.IsAlive(entity)) return;

        _world.Set(entity, new SpriteRenderer(false, Color.White));
        _world.Add(entity, new Pooled());
        _inactive.Enqueue(entity);
    }
}

/// <summary>Tag: entity is in the pool, not active in the game world.</summary>
public struct Pooled;
```

> 🎯 **Performance note:** For bullet hell games with 1,000+ projectiles, use the struct-array approach from [G23 Particles](./G23_particles.md) instead of ECS entities. ECS is better for projectiles that need to interact with other systems (homing, status effects, bouncing).

---

## 10 — Melee Attack System

Frame-data-driven melee attacks with windup, active, and recovery phases — the standard for action games and fighting games.

### Attack Definition

```csharp
namespace MyGame.Combat;

using Microsoft.Xna.Framework;

/// <summary>
/// Defines a melee attack's timing and properties.
/// Frame counts at 60 FPS — adjust or use seconds if targeting variable FPS.
/// </summary>
public record AttackDef(
    string Id,
    string Name,
    int WindupFrames,         // Startup before hitbox activates
    int ActiveFrames,         // Hitbox is live
    int RecoveryFrames,       // Cooldown after hitbox deactivates
    Rectangle HitboxBounds,   // Local-space hitbox during active frames
    int Damage,
    DamageType DamageType,
    float KnockbackForce,
    bool CanCancel,           // Can this attack be cancelled into another?
    int CancelWindowStart,    // Frame when cancel becomes available
    string? AnimationName     // Animation to play
)
{
    public int TotalFrames => WindupFrames + ActiveFrames + RecoveryFrames;
}
```

### Attack State Machine

```csharp
namespace MyGame.Combat;

/// <summary>
/// Drives a melee attack through its phases.
/// Attach as a component to the attacking entity.
/// </summary>
public record struct AttackState(
    AttackDef Attack,
    int CurrentFrame,
    AttackPhase Phase
)
{
    public AttackState(AttackDef attack) : this(attack, 0, AttackPhase.Windup) { }

    public readonly bool IsInCancelWindow =>
        Attack.CanCancel && CurrentFrame >= Attack.CancelWindowStart;

    public readonly bool IsComplete =>
        Phase == AttackPhase.Finished;
}

public enum AttackPhase
{
    Windup,    // Can't move, can't be cancelled (commitment)
    Active,    // Hitbox is live
    Recovery,  // Hitbox off, still can't act (punish window)
    Finished   // Attack is done, return to idle
}
```

### Melee Attack System

```csharp
namespace MyGame.Combat;

using Arch.Core;
using Arch.Core.Extensions;

/// <summary>
/// Advances attack state machines, activating/deactivating hitboxes
/// based on the current phase.
/// </summary>
public static class MeleeAttackSystem
{
    private static readonly QueryDescription Query = new QueryDescription()
        .WithAll<AttackState>();

    public static void Update(World world)
    {
        var finished = new List<Entity>();

        world.Query(in Query, (Entity entity, ref AttackState state) =>
        {
            state = state with { CurrentFrame = state.CurrentFrame + 1 };
            int frame = state.CurrentFrame;
            var atk = state.Attack;

            // Determine phase
            if (frame <= atk.WindupFrames)
            {
                state = state with { Phase = AttackPhase.Windup };
            }
            else if (frame <= atk.WindupFrames + atk.ActiveFrames)
            {
                state = state with { Phase = AttackPhase.Active };

                // Ensure hitbox is active
                if (!world.Has<Hitbox>(entity))
                {
                    world.Add(entity, new Hitbox(
                        atk.HitboxBounds,
                        atk.Damage,
                        atk.KnockbackForce,
                        atk.DamageType,
                        world.Has<TeamMember>(entity)
                            ? world.Get<TeamMember>(entity).TeamId : 0
                    ));
                    world.Add(entity, new HitboxActive());
                    world.Add(entity, new HitboxHitList());
                }
            }
            else if (frame <= atk.TotalFrames)
            {
                state = state with { Phase = AttackPhase.Recovery };

                // Deactivate hitbox
                if (world.Has<HitboxActive>(entity))
                {
                    world.Remove<HitboxActive>(entity);
                    world.Remove<Hitbox>(entity);
                    world.Remove<HitboxHitList>(entity);
                }
            }
            else
            {
                state = state with { Phase = AttackPhase.Finished };
                finished.Add(entity);
            }
        });

        // Clean up finished attacks
        foreach (var e in finished)
        {
            if (world.IsAlive(e) && world.Has<AttackState>(e))
            {
                world.Remove<AttackState>(e);
                // Remove any leftover hitbox components
                if (world.Has<HitboxActive>(e)) world.Remove<HitboxActive>(e);
                if (world.Has<Hitbox>(e)) world.Remove<Hitbox>(e);
                if (world.Has<HitboxHitList>(e)) world.Remove<HitboxHitList>(e);
            }
        }
    }
}

public record struct TeamMember(int TeamId);
```

### Example: Defining Attacks

```csharp
// Light attack — fast, low commitment
var lightSlash = new AttackDef(
    Id: "light_slash",
    Name: "Light Slash",
    WindupFrames: 4,        // 67ms startup
    ActiveFrames: 6,        // 100ms active
    RecoveryFrames: 8,      // 133ms recovery
    HitboxBounds: new Rectangle(16, -16, 40, 32), // In front, roughly sword-length
    Damage: 10,
    DamageType: DamageType.Physical,
    KnockbackForce: 50f,
    CanCancel: true,
    CancelWindowStart: 12,  // Can cancel into heavy during recovery
    AnimationName: "slash_light"
);

// Heavy attack — slow, high damage, high knockback
var heavySlam = new AttackDef(
    Id: "heavy_slam",
    Name: "Heavy Slam",
    WindupFrames: 12,       // 200ms startup (punishable)
    ActiveFrames: 8,        // 133ms active
    RecoveryFrames: 20,     // 333ms recovery (very punishable)
    HitboxBounds: new Rectangle(8, -24, 56, 48), // Wider, taller
    Damage: 35,
    DamageType: DamageType.Physical,
    KnockbackForce: 150f,
    CanCancel: false,
    CancelWindowStart: 0,
    AnimationName: "slam_heavy"
);
```

---

## 11 — Damage Types & Resistances

Support elemental damage for RPGs, metroidvanias, and any game with damage variety.

```csharp
namespace MyGame.Combat;

/// <summary>
/// Per-entity damage resistances. Values are 0–1 (percentage reduced).
/// Negative values = vulnerability (takes extra damage).
/// </summary>
public record struct DamageResistances(
    float Physical,
    float Fire,
    float Ice,
    float Lightning,
    float Poison,
    float Holy,
    float Dark
)
{
    /// <summary>Get resistance value for a damage type.</summary>
    public readonly float GetResistance(DamageType type) => type switch
    {
        DamageType.Physical => Physical,
        DamageType.Fire => Fire,
        DamageType.Ice => Ice,
        DamageType.Lightning => Lightning,
        DamageType.Poison => Poison,
        DamageType.Holy => Holy,
        DamageType.Dark => Dark,
        DamageType.Pure => 0f, // Pure ignores all resistance
        _ => 0f,
    };

    /// <summary>Create resistances from a dictionary (e.g., loaded from JSON).</summary>
    public static DamageResistances FromDictionary(Dictionary<string, float> data)
    {
        return new DamageResistances(
            Physical: data.GetValueOrDefault("physical"),
            Fire: data.GetValueOrDefault("fire"),
            Ice: data.GetValueOrDefault("ice"),
            Lightning: data.GetValueOrDefault("lightning"),
            Poison: data.GetValueOrDefault("poison"),
            Holy: data.GetValueOrDefault("holy"),
            Dark: data.GetValueOrDefault("dark")
        );
    }
}
```

### Example: Monster with Resistances

```csharp
// Fire elemental: immune to fire, weak to ice, resistant to physical
world.Create(
    new Position(new Vector2(400, 300)),
    new Health(80, 80, 0),
    new Alive(),
    new Hurtbox(new Rectangle(-12, -16, 24, 32), teamId: 2),
    new DamageResistances(
        Physical: 0.3f,     // 30% physical reduction
        Fire: 1.0f,         // Immune to fire
        Ice: -0.5f,         // Takes 50% MORE ice damage
        Lightning: 0f,
        Poison: 0.8f,       // Nearly immune to poison
        Holy: 0f,
        Dark: 0f
    )
);
```

---

## 12 — Critical Hits & Damage Variance

Flat damage feels robotic. Add variance and crits for satisfying combat.

```csharp
namespace MyGame.Combat;

/// <summary>
/// Stats that affect outgoing damage.
/// Attach to any entity that deals damage (player, enemies).
/// </summary>
public record struct CombatStats(
    float CritChance,          // 0–1 (0.05 = 5%)
    float CritMultiplier,      // 1.5–3.0 typical
    float DamageVariance,      // 0–1 (0.1 = ±10% random variance)
    float BonusDamagePercent   // Flat % boost from buffs/equipment
);

public static class DamageCalculator
{
    private static readonly Random Rng = new();

    /// <summary>
    /// Calculate final damage with variance, crits, and bonuses.
    /// Call this before creating DamageEvent to set BaseDamage and IsCritical.
    /// </summary>
    public static (int Damage, bool IsCritical) Calculate(
        int baseDamage,
        CombatStats stats)
    {
        float damage = baseDamage;

        // Flat bonus
        damage *= (1f + stats.BonusDamagePercent);

        // Random variance (±variance%)
        if (stats.DamageVariance > 0)
        {
            float variance = 1f + (float)(Rng.NextDouble() * 2 - 1) * stats.DamageVariance;
            damage *= variance;
        }

        // Critical hit
        bool isCrit = Rng.NextDouble() < stats.CritChance;
        if (isCrit)
            damage *= stats.CritMultiplier;

        return (Math.Max(1, (int)MathF.Round(damage)), isCrit);
    }
}
```

---

## 13 — Turn-Based Combat Adapter

The systems above are real-time, but the same components work for turn-based games. The key difference: **turns replace frames**.

```csharp
namespace MyGame.Combat;

using Arch.Core;

/// <summary>
/// Turn-based combat controller. Instead of running systems every frame,
/// systems execute per-turn in a defined order.
/// </summary>
public sealed class TurnBasedCombatManager
{
    private readonly World _world;
    private readonly GameFeedback _feedback;
    private readonly Queue<Entity> _turnOrder = new();

    public Entity? CurrentActor { get; private set; }
    public CombatPhase Phase { get; private set; } = CombatPhase.SelectAction;

    public void BeginRound(IEnumerable<Entity> combatants)
    {
        // Sort by speed stat (or any initiative system)
        var sorted = combatants
            .Where(e => _world.Has<Alive>(e))
            .OrderByDescending(e => _world.Get<CombatStats>(e).CritChance) // Use speed stat
            .ToList();

        _turnOrder.Clear();
        foreach (var e in sorted)
            _turnOrder.Enqueue(e);

        NextTurn();
    }

    public void NextTurn()
    {
        if (_turnOrder.Count == 0)
        {
            Phase = CombatPhase.RoundEnd;
            return;
        }

        CurrentActor = _turnOrder.Dequeue();
        Phase = CombatPhase.SelectAction;
    }

    /// <summary>
    /// Execute a chosen action. Creates DamageEvent just like real-time,
    /// then the same DamageResolutionSystem processes it.
    /// </summary>
    public void ExecuteAttack(Entity target, AttackDef attack)
    {
        if (CurrentActor == null) return;

        var stats = _world.Has<CombatStats>(CurrentActor.Value)
            ? _world.Get<CombatStats>(CurrentActor.Value)
            : new CombatStats(0.05f, 1.5f, 0.1f, 0f);

        var (damage, isCrit) = DamageCalculator.Calculate(attack.Damage, stats);

        var attackerPos = _world.Get<Position>(CurrentActor.Value).Value;
        var targetPos = _world.Get<Position>(target).Value;

        _world.Create(new DamageEvent(
            Source: CurrentActor.Value,
            Target: target,
            BaseDamage: damage,
            DamageType: attack.DamageType,
            KnockbackForce: 0f,  // Usually no knockback in turn-based
            KnockbackDirection: Vector2.Normalize(targetPos - attackerPos),
            IsCritical: isCrit
        ));

        // Run damage resolution immediately
        DamageResolutionSystem.Update(_world, _feedback);

        Phase = CombatPhase.ActionResolving;
    }
}

public enum CombatPhase
{
    SelectAction,
    ActionResolving,
    RoundEnd
}
```

> 🎯 **Key insight:** The `DamageEvent` → `DamageResolutionSystem` pipeline is the same for real-time and turn-based. The only difference is *when* systems run: every frame vs per-turn.

---

## 14 — Death & Respawn

### Death Processing System

```csharp
namespace MyGame.Combat;

using Arch.Core;
using Arch.Core.Extensions;

/// <summary>
/// Processes JustDied entities: plays death effects, drops loot,
/// and either destroys or marks for respawn.
/// </summary>
public static class DeathSystem
{
    private static readonly QueryDescription Query = new QueryDescription()
        .WithAll<JustDied, Position>();

    public static void Update(World world, GameFeedback feedback, LootSystem? loot)
    {
        var processed = new List<Entity>();

        world.Query(in Query, (Entity entity, ref Position pos) =>
        {
            processed.Add(entity);

            // Death effects
            feedback.SpawnDeathParticles(pos.Value);
            feedback.PlayDeathSound();

            // Drop loot (if applicable)
            loot?.DropLoot(entity, pos.Value);

            // Check if this is the player
            if (world.Has<PlayerTag>(entity))
            {
                // Player death: trigger respawn sequence
                world.Add(entity, new RespawnTimer(2.0f)); // 2 second respawn
                world.Remove<JustDied>(entity);
            }
            else
            {
                // Enemy death: grant XP, destroy after death anim
                if (world.Has<XPValue>(entity))
                {
                    int xp = world.Get<XPValue>(entity).Value;
                    feedback.GrantXP(xp);
                }
                world.Destroy(entity);
            }
        });
    }
}

public struct PlayerTag;
public record struct RespawnTimer(float RemainingSeconds);
public record struct XPValue(int Value);
```

### Respawn System

```csharp
namespace MyGame.Combat;

using Arch.Core;
using Arch.Core.Extensions;
using Microsoft.Xna.Framework;

public static class RespawnSystem
{
    private static readonly QueryDescription Query = new QueryDescription()
        .WithAll<RespawnTimer, Health, Position>();

    public static void Update(World world, float dt, Vector2 respawnPoint)
    {
        var toRespawn = new List<Entity>();

        world.Query(in Query, (Entity entity, ref RespawnTimer timer) =>
        {
            timer = timer with { RemainingSeconds = timer.RemainingSeconds - dt };
            if (timer.RemainingSeconds <= 0)
                toRespawn.Add(entity);
        });

        foreach (var e in toRespawn)
        {
            if (!world.IsAlive(e)) continue;

            // Restore health
            ref var health = ref world.Get<Health>(e);
            health = health with { Current = health.Max };

            // Move to respawn point
            world.Set(e, new Position(respawnPoint));

            // Re-add alive tag
            world.Add(e, new Alive());

            // Grant brief invincibility
            if (!world.Has<Invincible>(e))
                world.Add(e, new Invincible(2.0f));

            // Clean up
            world.Remove<RespawnTimer>(e);
        }
    }
}
```

---

## 15 — Damage Numbers & Feedback

Floating damage numbers sell the impact of every hit.

```csharp
namespace MyGame.Combat;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

/// <summary>
/// Floating damage number that rises and fades out.
/// Managed as a simple struct array — not ECS (too ephemeral).
/// </summary>
public struct DamageNumber
{
    public Vector2 Position;
    public Vector2 Velocity;
    public string Text;
    public Color Color;
    public float Scale;
    public float Lifetime;
    public float Elapsed;
    public bool IsAlive;
}

public sealed class DamageNumberRenderer
{
    private readonly DamageNumber[] _numbers;
    private int _count;
    private readonly SpriteFont _font;

    public DamageNumberRenderer(SpriteFont font, int capacity = 64)
    {
        _font = font;
        _numbers = new DamageNumber[capacity];
    }

    public void Spawn(Vector2 position, int damage, bool isCritical)
    {
        if (_count >= _numbers.Length) return;

        var rng = new Random();
        _numbers[_count++] = new DamageNumber
        {
            Position = position + new Vector2(rng.Next(-8, 8), -16),
            Velocity = new Vector2(rng.Next(-20, 20), -80),
            Text = damage.ToString(),
            Color = isCritical ? Color.Gold : Color.White,
            Scale = isCritical ? 1.5f : 1.0f,
            Lifetime = isCritical ? 1.2f : 0.8f,
            Elapsed = 0f,
            IsAlive = true,
        };
    }

    public void Update(float dt)
    {
        for (int i = _count - 1; i >= 0; i--)
        {
            ref var num = ref _numbers[i];
            num.Elapsed += dt;

            if (num.Elapsed >= num.Lifetime)
            {
                // Swap with last, shrink
                _numbers[i] = _numbers[--_count];
                continue;
            }

            num.Position += num.Velocity * dt;
            num.Velocity.Y += 60f * dt; // Slight gravity arc
        }
    }

    public void Draw(SpriteBatch spriteBatch, Matrix cameraTransform)
    {
        // Draw in screen space (damage numbers shouldn't shake with camera)
        spriteBatch.Begin();

        for (int i = 0; i < _count; i++)
        {
            ref var num = ref _numbers[i];
            float t = num.Elapsed / num.Lifetime;
            float alpha = 1f - t * t; // Quadratic fade

            var color = num.Color * alpha;
            var origin = _font.MeasureString(num.Text) / 2f;

            // Scale pop: starts large, settles
            float scale = num.Scale * (1f + (1f - t) * 0.3f);

            spriteBatch.DrawString(
                _font, num.Text, num.Position, color,
                0f, origin, scale, SpriteEffects.None, 0f);
        }

        spriteBatch.End();
    }
}
```

---

## 16 — Combat Tuning Reference

Quick-reference values for common game genres. Start here, then playtest.

### Timing (at 60 FPS)

| Parameter | Platformer | Fighting | RPG Action | Bullet Hell |
|-----------|-----------|----------|------------|-------------|
| I-frame duration | 0.5–1.0s | 0.1–0.3s | 0.3–0.8s | 0.2–0.5s |
| Light attack startup | 3–6 frames | 4–7 frames | 4–8 frames | N/A |
| Light attack active | 4–8 frames | 3–5 frames | 5–10 frames | N/A |
| Light attack recovery | 6–12 frames | 8–15 frames | 8–14 frames | N/A |
| Heavy attack startup | 10–18 frames | 12–20 frames | 12–24 frames | N/A |
| Hitstop (light) | 2–3 frames | 5–8 frames | 2–4 frames | 0–1 frame |
| Hitstop (heavy) | 4–6 frames | 8–14 frames | 4–8 frames | 1–2 frames |
| Screen shake (light) | 0.05s, 2px | 0.08s, 3px | 0.05s, 2px | 0.03s, 1px |
| Screen shake (heavy) | 0.12s, 5px | 0.15s, 6px | 0.10s, 4px | 0.05s, 2px |

### Damage Balance Starting Points

| Parameter | Notes |
|-----------|-------|
| Player HP | 100 (easy to reason about percentages) |
| Light attack | 8–12% of target HP |
| Heavy attack | 20–30% of target HP |
| Trash mob HP | 1–3 player light attacks to kill |
| Elite HP | 8–15 player attacks |
| Boss HP | 30–60 seconds of optimal DPS |
| Crit chance | 5–10% base, up to 30% with investment |
| Crit multiplier | 1.5× (standard), 2.0× (glass cannon builds) |
| Armor reduction | 10–30% for light, 40–60% for heavy |
| Damage variance | ±5–15% (too much feels random, too little feels robotic) |

### Knockback

| Scenario | Force | Duration |
|----------|-------|----------|
| Light hit | 40–80 | 0.1–0.15s |
| Heavy hit | 120–200 | 0.2–0.3s |
| Boss slam | 250–400 | 0.3–0.5s |
| Explosion | 300–500 | 0.2–0.4s |

---

## GameFeedback Interface

All feedback calls in the damage pipeline route through this interface, making it easy to swap implementations (e.g., headless for testing):

```csharp
namespace MyGame.Combat;

using Microsoft.Xna.Framework;

/// <summary>
/// Facade for combat feedback effects.
/// The damage pipeline calls these — implementations handle particles,
/// sounds, screen shake, damage numbers, etc.
/// </summary>
public interface GameFeedback
{
    void SpawnDamageNumber(Vector2 position, int damage, bool isCritical);
    void PlayHitSound(DamageType type);
    void RequestScreenShake(float duration, float intensity);
    void RequestHitstop(int frames);
    void SpawnDeathParticles(Vector2 position);
    void PlayDeathSound();
    void GrantXP(int amount);
}

/// <summary>Null implementation for testing or headless mode.</summary>
public sealed class NullFeedback : GameFeedback
{
    public void SpawnDamageNumber(Vector2 position, int damage, bool isCritical) { }
    public void PlayHitSound(DamageType type) { }
    public void RequestScreenShake(float duration, float intensity) { }
    public void RequestHitstop(int frames) { }
    public void SpawnDeathParticles(Vector2 position) { }
    public void PlayDeathSound() { }
    public void GrantXP(int amount) { }
}
```

---

## System Execution Order

Order matters. Run combat systems in this sequence each frame:

```
1. Input → (player attack commands)
2. MeleeAttackSystem → (advance attack state machines, activate hitboxes)
3. ProjectileSystem → (move projectiles, check collisions)
4. HitDetectionSystem → (hitbox vs hurtbox, create DamageEvents)
5. DamageResolutionSystem → (resolve damage, apply knockback, grant i-frames)
6. KnockbackSystem → (apply decaying knockback velocity)
7. InvincibilitySystem → (tick down i-frame timers)
8. DeathSystem → (process deaths, spawn loot)
9. RespawnSystem → (tick respawn timers, revive)
10. DamageNumberRenderer.Update → (animate floating numbers)
```

> 🎯 **Critical:** Hit detection MUST run before damage resolution. Knockback MUST run after damage resolution (so the impulse is applied the same frame). Invincibility ticks last so i-frames are active for the correct duration.

---

## Related Guides

- [G3 Physics & Collision](./G3_physics_and_collision.md) — Collision shapes, broadphase, AABB
- [G10 §7 Status Effects](./G10_custom_game_systems.md) — DoT, buffs, debuffs (feeds into damage pipeline)
- [G30 Game Feel Tooling](./G30_game_feel_tooling.md) — Deeper screen shake, hitstop, juice
- [G31 Animation State Machines](./G31_animation_state_machines.md) — Sync attack anims with frame data
- [G52 Character Controller](./G52_character_controller.md) — Movement that integrates with knockback
- [G23 Particles](./G23_particles.md) — Hit sparks, blood, death effects
- [C2 Game Feel & Genre Craft](../../core/game-design/C2_game_feel_and_genre_craft.md) — Design theory behind combat feel
