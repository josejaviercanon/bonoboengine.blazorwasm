# G23 — Particles


> **Category:** Guide · **Related:** [G1 Custom Code Recipes](./G1_custom_code_recipes.md) · [G2 Rendering & Graphics](./G2_rendering_and_graphics.md) · [G13 C# Performance](./G13_csharp_performance.md) · [G22 Parallax & Depth Layers](./G22_parallax_depth_layers.md)

---

## Two Approaches

There are two viable ways to do particles in MonoGame + Arch ECS:

| Approach | When to Use | Typical Scale |
|----------|-------------|---------------|
| **Struct pool** (simple `List<Particle>`) | Fire-and-forget effects, high particle counts, short-lived | 500–10,000 per frame |
| **ECS entities** (Arch entities with particle components) | Particles that interact with game systems (collision, physics, damage) | 50–500 per frame |

Most games use the **struct pool** for visual effects (sparks, smoke, blood) and **ECS entities** for gameplay-relevant projectiles that happen to look like particles.

---

## Approach 1: Struct Pool Particle System

A zero-allocation particle system using a fixed-size array. No GC pressure in steady state.

### Particle Struct

```csharp
namespace MyGame.Rendering;

/// <summary>A single particle in the pool. 64 bytes — cache-friendly.</summary>
public struct Particle
{
    public Vector2 Position;
    public Vector2 Velocity;
    public Vector2 Acceleration;
    public float Rotation;
    public float AngularVelocity;
    public float Scale;
    public float ScaleVelocity;     // Scale change per second
    public Color Color;
    public Color ColorEnd;          // Interpolate Color → ColorEnd over lifetime
    public float Lifetime;          // Total lifetime in seconds
    public float Elapsed;           // Time alive so far
    public bool IsAlive;
}
```

### ParticlePool

```csharp
namespace MyGame.Rendering;

/// <summary>
/// Fixed-capacity particle pool. No allocations after construction.
/// Particles are updated and rendered in a single pass.
/// </summary>
public sealed class ParticlePool
{
    private readonly Particle[] _particles;
    private int _activeCount;

    public ParticlePool(int capacity = 2048)
    {
        _particles = new Particle[capacity];
    }

    /// <summary>Emit a single particle. Returns false if pool is full.</summary>
    public bool Emit(ref Particle template)
    {
        if (_activeCount >= _particles.Length)
            return false;

        template.IsAlive = true;
        template.Elapsed = 0f;
        _particles[_activeCount++] = template;
        return true;
    }

    /// <summary>Update all living particles. Dead particles are swapped to the end.</summary>
    public void Update(float dt)
    {
        for (int i = _activeCount - 1; i >= 0; i--)
        {
            ref Particle p = ref _particles[i];
            p.Elapsed += dt;

            if (p.Elapsed >= p.Lifetime)
            {
                // Swap with last active particle and shrink pool
                p.IsAlive = false;
                _particles[i] = _particles[--_activeCount];
                continue;
            }

            // Physics integration
            p.Velocity += p.Acceleration * dt;
            p.Position += p.Velocity * dt;
            p.Rotation += p.AngularVelocity * dt;
            p.Scale += p.ScaleVelocity * dt;
        }
    }

    /// <summary>Render all living particles.</summary>
    public void Draw(SpriteBatch spriteBatch, Texture2D texture)
    {
        for (int i = 0; i < _activeCount; i++)
        {
            ref Particle p = ref _particles[i];
            float t = p.Elapsed / p.Lifetime; // 0 → 1 over lifetime

            // Interpolate color over lifetime
            Color color = Color.Lerp(p.Color, p.ColorEnd, t);

            spriteBatch.Draw(
                texture,
                p.Position,
                sourceRectangle: null,
                color,
                p.Rotation,
                origin: new Vector2(texture.Width / 2f, texture.Height / 2f),
                p.Scale,
                SpriteEffects.None,
                layerDepth: 0f);
        }
    }

    public int ActiveCount => _activeCount;
}
```

### Emitter Patterns

```csharp
/// <summary>Burst emission — emit N particles at once (explosion, impact).</summary>
public void EmitBurst(ParticlePool pool, Vector2 position, int count, Random rng)
{
    for (int i = 0; i < count; i++)
    {
        float angle = (float)(rng.NextDouble() * MathHelper.TwoPi);
        float speed = 50f + (float)rng.NextDouble() * 150f;

        Particle p = new()
        {
            Position = position,
            Velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed,
            Acceleration = new Vector2(0, 200f),  // Gravity
            Scale = 0.5f + (float)rng.NextDouble() * 0.5f,
            ScaleVelocity = -0.5f,  // Shrink over time
            Color = Color.Yellow,
            ColorEnd = new Color(255, 50, 0, 0),  // Fade to transparent red
            Lifetime = 0.3f + (float)rng.NextDouble() * 0.4f,
            Rotation = (float)(rng.NextDouble() * MathHelper.TwoPi),
            AngularVelocity = (float)(rng.NextDouble() - 0.5f) * 10f,
        };

        pool.Emit(ref p);
    }
}

/// <summary>Stream emission — emit particles continuously (fire, smoke trail).</summary>
public sealed class StreamEmitter
{
    private float _accumulator;
    private readonly float _emissionInterval; // Seconds between emissions

    public Vector2 Position { get; set; }
    public bool IsActive { get; set; } = true;

    public StreamEmitter(float particlesPerSecond)
    {
        _emissionInterval = 1f / particlesPerSecond;
    }

    public void Update(float dt, ParticlePool pool, Random rng)
    {
        if (!IsActive) return;

        _accumulator += dt;
        while (_accumulator >= _emissionInterval)
        {
            _accumulator -= _emissionInterval;
            EmitOne(pool, rng);
        }
    }

    private void EmitOne(ParticlePool pool, Random rng)
    {
        Particle p = new()
        {
            Position = Position + new Vector2(
                (float)(rng.NextDouble() - 0.5f) * 10f,
                (float)(rng.NextDouble() - 0.5f) * 10f),
            Velocity = new Vector2(0, -80f + (float)rng.NextDouble() * -40f), // Rise
            Scale = 0.3f + (float)rng.NextDouble() * 0.3f,
            ScaleVelocity = 0.5f,  // Grow
            Color = new Color(200, 200, 200, 200),
            ColorEnd = new Color(100, 100, 100, 0),  // Fade out
            Lifetime = 0.8f + (float)rng.NextDouble() * 0.5f,
        };

        pool.Emit(ref p);
    }
}
```

---

## Approach 2: ECS Particle Entities

For particles that need to interact with game systems (collision, damage, triggers):

### Components

```csharp
/// <summary>Marker tag for particle entities.</summary>
public struct ParticleTag : ITag { }

/// <summary>Particle lifetime tracking.</summary>
public struct ParticleLifetime
{
    public float Remaining;
    public float Total;
}

/// <summary>Visual properties that change over lifetime.</summary>
public struct ParticleVisual
{
    public Color StartColor;
    public Color EndColor;
    public float StartScale;
    public float EndScale;
}
```

### ParticleLifetimeSystem

```csharp
/// <summary>Destroys particles when their lifetime expires.</summary>
public partial class ParticleLifetimeSystem : BaseSystem<World, float>
{
    private readonly List<Entity> _toDestroy = new();

    public ParticleLifetimeSystem(World world) : base(world) { }

    [Query]
    [All<ParticleTag>]
    private void UpdateLifetime([Data] in float dt, Entity entity, ref ParticleLifetime lifetime)
    {
        lifetime.Remaining -= dt;
        if (lifetime.Remaining <= 0f)
            _toDestroy.Add(entity);
    }

    public override void AfterUpdate(in float dt)
    {
        foreach (Entity entity in _toDestroy)
        {
            if (entity.IsAlive())
                World.Destroy(entity);
        }
        _toDestroy.Clear();
    }
}
```

**Trade-off:** ECS particles have per-entity overhead from Arch's archetype storage. For 10,000 fire particles, the struct pool is 10-50x faster. For 50 bouncing coins that need collision, ECS is cleaner.

---

## Blending Modes

Particles often need additive blending (glow, fire, magic) rather than the default alpha blending.

```csharp
// Additive blending — colors add together, creating bright glowing effects
spriteBatch.Begin(
    sortMode: SpriteSortMode.Deferred,
    blendState: BlendState.Additive,
    samplerState: SamplerState.PointClamp,
    transformMatrix: camera.GetViewMatrix());

particlePool.Draw(spriteBatch, glowTexture);

spriteBatch.End();
```

| Blend Mode | Use Case |
|------------|----------|
| `BlendState.AlphaBlend` | Smoke, dust, debris — standard transparency |
| `BlendState.Additive` | Fire, sparks, magic, explosions — colors brighten |
| `BlendState.NonPremultiplied` | Textures with standard alpha (not premultiplied) |

**Mixed blending:** If you need both additive and alpha particles in the same scene, use two draw passes — one with each BlendState. Group particles by blend mode to minimize state changes.

---

## Particle Textures

Particle textures should be small and simple:

- **White circle** (4x4 to 16x16): Tinted by `Color` at draw time. Most versatile.
- **Soft gradient circle**: Smooth falloff, good for smoke and glow.
- **Spark/diamond**: Elongated, good for sparks and trails.
- **Square**: For pixel art games, a 1x1 white pixel scaled up.

```csharp
// Create a 1x1 white pixel texture at runtime (no content pipeline needed)
Texture2D pixelTexture = new Texture2D(GraphicsDevice, 1, 1);
pixelTexture.SetData(new[] { Color.White });
```

Tint white textures with the particle's `Color` for maximum flexibility — one texture serves all effects.

---

## Common Effects (Parameter Recipes)

### Explosion

```csharp
Velocity = RandomDirection() * Random(100, 300),
Acceleration = new Vector2(0, 300),   // Gravity pulls debris down
Color = Color.Orange,
ColorEnd = new Color(50, 50, 50, 0),  // Dark smoke, fade out
Scale = 0.5f, ScaleVelocity = -0.8f,  // Shrink
Lifetime = Random(0.2f, 0.6f),
// Burst: 30-60 particles
```

### Fire

```csharp
Position = emitterPos + new Vector2(Random(-8, 8), 0),
Velocity = new Vector2(Random(-10, 10), Random(-100, -60)),  // Rise with wobble
Acceleration = Vector2.Zero,
Color = new Color(255, 200, 50, 200),
ColorEnd = new Color(200, 50, 0, 0),  // Orange → transparent red
Scale = 0.4f, ScaleVelocity = -0.3f,
Lifetime = Random(0.4f, 0.8f),
// Stream: 40-80 particles/sec
```

### Smoke

```csharp
Velocity = new Vector2(Random(-20, 20), Random(-40, -20)),  // Slow rise
Acceleration = new Vector2(Random(-5, 5), -10),  // Slight drift
Color = new Color(150, 150, 150, 100),
ColorEnd = new Color(80, 80, 80, 0),
Scale = 0.3f, ScaleVelocity = 0.8f,  // Expand
Lifetime = Random(1.0f, 2.0f),
// Stream: 10-20 particles/sec, AlphaBlend
```

### Blood Splatter

```csharp
Velocity = hitDirection * Random(80, 200) + RandomDirection() * Random(20, 50),
Acceleration = new Vector2(0, 400),  // Heavy gravity
Color = new Color(180, 0, 0, 255),
ColorEnd = new Color(80, 0, 0, 0),
Scale = 0.2f, ScaleVelocity = 0f,    // Constant size
Lifetime = Random(0.3f, 0.6f),
// Burst: 10-25 particles
```

### Sparkle / Collect

```csharp
Velocity = RandomDirection() * Random(30, 80),
Acceleration = Vector2.Zero,
Color = Color.White,
ColorEnd = new Color(255, 255, 100, 0),  // Yellow glow, fade
Scale = 0.3f, ScaleVelocity = -0.4f,
Lifetime = Random(0.3f, 0.5f),
AngularVelocity = Random(-5, 5),
// Burst: 8-15 particles, Additive blending
```

### Trail (Behind Moving Entity)

```csharp
// Emit at entity's previous position each frame
Position = entityPosition,
Velocity = Vector2.Zero,         // Stays where spawned
Scale = 0.3f, ScaleVelocity = -0.5f,
Color = Color.Cyan,
ColorEnd = new Color(0, 100, 255, 0),
Lifetime = Random(0.2f, 0.4f),
// Stream: 30-60 particles/sec, Additive blending
```

---

## Performance Guidelines

| Metric | Budget (Desktop) | Budget (Mobile) |
|--------|-------------------|-----------------|
| Active particles | 5,000-10,000 | 500-2,000 |
| Particle texture size | 16x16 to 64x64 | 4x4 to 16x16 |
| Draw calls for particles | 1-3 (grouped by blend mode) | 1-2 |
| Update cost | <0.5ms | <0.3ms |

**Tips:**
- Pool size should be your expected maximum. Overflowing silently drops new particles — acceptable for visual effects.
- Use `ref` access to the particle array (as shown above) to avoid struct copies.
- Profile `Draw` separately from `Update`. Draw is usually the bottleneck (GPU fill rate), not Update (CPU).
- On mobile, reduce emission rates and lifetimes by 50% compared to desktop. Shorter-lived particles = fewer active particles = less fill.
- Additive blending has no overdraw cost (pixels just add), so additive particles are cheaper than alpha-blended particles at the same count.

---

## Integration with ObjectPool

If using the ObjectPool from [G1](./G1_custom_code_recipes.md) for complex particle objects (those with additional state beyond the struct), wrap the emit/recycle:

```csharp
// For simple visual-only particles, the ParticlePool above IS the pool.
// ObjectPool is only needed if particles carry non-trivial state (textures, callbacks).
```

For most games, the `ParticlePool` struct array replaces the need for a separate ObjectPool — it's already pooled by design.

---

## Production Particle Systems

As particle systems scale from prototyping to production, several patterns emerge as essential. These address real-world constraints — thousands of fire sources competing for pool slots, cross-system queries, and GPU fill rate limits.

### Budget-Based Emission

When many emitters compete for a shared pool (e.g. hundreds of burning tiles), naive emission starves later-iterated sources. Pre-count demand before emitting:

```csharp
// Count what needs particles this frame
int burningCount = 0, emberCount = 0;
for (int ty = startTY; ty <= endTY; ty++)
    for (int tx = startTX; tx <= endTX; tx++)
    {
        if (fireGrid.IsBurning(tx, ty)) burningCount++;
        else if (fireGrid.IsEmbers(tx, ty)) emberCount++;
    }

// Weight demand by visual importance (burning > embers)
float totalDemand = burningCount * 2.5f + emberCount * 0.25f;
float availableSlots = pool.Capacity - pool.ActiveCount;
float budgetThrottle = totalDemand > 0f
    ? MathHelper.Clamp(availableSlots / totalDemand, 0.1f, 1f)
    : 1f;
```

Layer a coarse **pool-load throttle** on top for safety — bucket thresholds avoid per-frame division:

```csharp
float poolLoad = (float)pool.ActiveCount / pool.Capacity;
float loadThrottle = poolLoad switch
{
    < 0.2f  => 1.0f,
    < 0.4f  => 0.65f,
    < 0.65f => 0.35f,
    _       => 0.15f
};
float throttle = Math.Min(loadThrottle, budgetThrottle);
```

### Alternating Iteration Direction

Grid-based emitters that iterate top-to-bottom create a visible starvation bias — top tiles always get pool slots first. Fix by alternating Y direction each frame:

```csharp
_reverseIteration = !_reverseIteration;
int yFrom = _reverseIteration ? endTY : startTY;
int yTo   = _reverseIteration ? startTY : endTY;
int yStep = _reverseIteration ? -1 : 1;

for (int ty = yFrom; ty != yTo + yStep; ty += yStep)
    // ... emit particles for row
```

### Kill Bounds

Particles that drift off-screen waste pool slots. Set kill bounds each frame from camera bounds + margin, and skip `OnParticleDeath` for culled particles (they weren't visible):

```csharp
/// <summary>
/// Particles outside KillBounds are force-killed to recycle pool slots.
/// Set each frame from camera bounds + generous margin.
/// </summary>
public Rectangle? KillBounds { get; set; }

// In Update:
if (kill.HasValue)
{
    Rectangle k = kill.Value;
    if (p.Position.X < k.X || p.Position.X > k.Right ||
        p.Position.Y < k.Y || p.Position.Y > k.Bottom)
    {
        // Force death — skip OnParticleDeath (no gameplay side-effects for culled particles)
        _activeCount--;
        if (i < _activeCount)
            _particles[i] = _particles[_activeCount];
        continue;
    }
}
```

### Tagged Particles and Cross-System Queries

Tag particles by type so external systems can read specific subsets without iterating the full pool:

```csharp
// In Particle struct:
public byte Tag; // 0 = untagged, 1 = ember, 2 = smoke, etc.

// In ParticlePool:
public void ForEachTagged(byte tag, Action<Vector2, float> callback)
{
    for (int i = 0; i < _activeCount; i++)
    {
        ref Particle p = ref _particles[i];
        if (p.Tag == tag)
            callback(p.Position, p.Alpha);
    }
}
```

**Use case:** The lighting system queries ember particles to place dynamic point lights on flying sparks:

```csharp
fireParticles.ForEachTagged(EmberTag, (pos, alpha) =>
{
    DrawLight(batch, pos.X, pos.Y, emberRadius * 0.5f, emberIntensity * alpha);
});
```

### Frustum-Culled Draw

Update processes all particles (physics must be correct), but Draw can skip off-screen particles. Pass camera bounds + padding to account for particle size and glow bleed:

```csharp
public void Draw(SpriteBatch batch, Texture2D pixel, Rectangle viewport, float padding)
{
    float left = viewport.X - padding;
    float top = viewport.Y - padding;
    float right = viewport.Right + padding;
    float bottom = viewport.Bottom + padding;

    for (int i = 0; i < _activeCount; i++)
    {
        ref Particle p = ref _particles[i];
        if (p.Position.X < left || p.Position.X > right ||
            p.Position.Y < top || p.Position.Y > bottom)
            continue;
        // ... draw particle
    }
}
```

### Velocity Delay Ramp

Fire particles look better when they "grow in place" before drifting upward. Add a delay period where velocity ramps smoothly from 0 to full:

```csharp
// In Particle struct:
public float VelocityDelay; // seconds before velocity fully kicks in

// In Update:
float velScale = (p.VelocityDelay > 0f && p.Life < p.VelocityDelay)
    ? p.Life / p.VelocityDelay
    : 1f;
p.Position += p.Velocity * velScale * dt;
```

This prevents the "instant drift" look where particles teleport upward the frame they spawn.

### Cached Interpolation

Avoid recomputing per-particle values across multiple draw passes (main, glow, bloom). Cache interpolation results during Update:

```csharp
// In Particle struct — computed once per Update, read in Draw:
public float T;                // Normalized lifetime [0..1]
public float InterpolatedSize; // Lerp(Size, SizeEnd, T)
public float Alpha;            // 1 - T (fade-out)

// In Update, after advancing Life:
p.T = p.Life / p.MaxLife;
p.InterpolatedSize = MathHelper.Lerp(p.Size, p.SizeEnd, p.T);
p.Alpha = 1f - p.T;
```

---

## See Also

- [G1 Custom Code Recipes](./G1_custom_code_recipes.md) — ObjectPool for general reuse
- [G2 Rendering & Graphics](./G2_rendering_and_graphics.md) — SpriteBatch, BlendState, post-processors
- [G13 C# Performance](./G13_csharp_performance.md) — zero-allocation patterns, struct best practices
- [G22 Parallax & Depth Layers](./G22_parallax_depth_layers.md) — render layer ordering for particles
