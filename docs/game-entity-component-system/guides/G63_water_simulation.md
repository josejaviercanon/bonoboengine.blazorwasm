# G63 — 2D Water Simulation


> **Category:** Guide · **Related:** [G3 Physics & Collision](./G3_physics_and_collision.md) · [G27 Shaders & Visual Effects](./G27_shaders_and_effects.md) · [G23 Particles](./G23_particles.md) · [G2 Rendering & Graphics](./G2_rendering_and_graphics.md) · [G57 Weather Effects](./G57_weather_effects.md) · [G67 Object Pooling](./G67_object_pooling.md) · [G64 Combat & Damage](./G64_combat_damage_systems.md)

---

## Overview

Water simulation in 2D games uses a **spring-based column model** — a row of vertical springs whose heights ripple in response to disturbances. This guide covers the full pipeline: spring physics, rendering, splash effects, buoyancy, shaders, tile-based water, currents, underwater mechanics, and hazard variants (lava/acid). All code targets **MonoGame.Framework.DesktopGL** with **Arch ECS v2.1.0**.

---

## 1 — Water Surface Simulation

The surface is a 1D array of spring columns spanning the water body's width. Each column tracks a **height offset** from the rest position, a **velocity**, and propagates waves to its neighbors every frame.

```
Column 0    Column 1    Column 2    ...    Column N
  |            |~          |              |
  |            |           |~~            |
  |____________|___________|______________|   ← rest height
  |////////////|///////////|//////////////|   ← water body (filled)
  |____________|___________|______________|   ← bottom
```

Key parameters:
| Parameter | Typical Value | Purpose |
|-----------|--------------|---------|
| `Tension` | 0.025 | Spring stiffness (Hooke's constant) |
| `Dampening` | 0.025 | Velocity decay per frame |
| `Spread` | 0.25 | Wave propagation coefficient |
| `ColumnWidth` | 4–8 px | Spacing between columns |

---

## 2 — Spring Physics

Each column obeys **Hooke's law**: force = −tension × displacement. The update runs three passes every fixed-step tick.

```csharp
public struct WaterColumn
{
    public float Height;      // current offset from rest
    public float Velocity;    // vertical speed
    public float LeftDelta;   // propagation delta for left neighbor
    public float RightDelta;  // propagation delta for right neighbor
}

public static class WaterSpringPhysics
{
    public const float Tension   = 0.025f;
    public const float Dampening = 0.025f;
    public const float Spread    = 0.25f;

    /// <summary>Pass 1 — apply spring force + damping to each column.</summary>
    public static void UpdateColumns(Span<WaterColumn> cols)
    {
        for (int i = 0; i < cols.Length; i++)
        {
            float force = -Tension * cols[i].Height;
            cols[i].Velocity += force;
            cols[i].Velocity *= (1f - Dampening);
            cols[i].Height   += cols[i].Velocity;
        }
    }

    /// <summary>Pass 2+3 — propagate waves to neighbors (run 2–4 iterations for stability).</summary>
    public static void Propagate(Span<WaterColumn> cols, int iterations = 2)
    {
        for (int iter = 0; iter < iterations; iter++)
        {
            for (int i = 0; i < cols.Length; i++)
            {
                if (i > 0)
                {
                    cols[i].LeftDelta = Spread * (cols[i].Height - cols[i - 1].Height);
                    cols[i - 1].Velocity += cols[i].LeftDelta;
                }
                if (i < cols.Length - 1)
                {
                    cols[i].RightDelta = Spread * (cols[i].Height - cols[i + 1].Height);
                    cols[i + 1].Velocity += cols[i].RightDelta;
                }
            }

            // apply deltas after full pass
            for (int i = 0; i < cols.Length; i++)
            {
                if (i > 0)
                    cols[i - 1].Height += cols[i].LeftDelta;
                if (i < cols.Length - 1)
                    cols[i + 1].Height += cols[i].RightDelta;
            }
        }
    }
}
```

> **Stability tip:** keep `Tension` and `Dampening` under 0.05. Higher `Spread` makes waves travel faster but can oscillate if > 0.5.

---

## 3 — Rendering the Water Surface

Draw the water body as a **triangle strip** from each column's surface point down to the bottom, with a separate line pass for the surface edge.

```csharp
public static class WaterRenderer
{
    /// <summary>Build vertices for a filled water body with depth gradient.</summary>
    public static void BuildMesh(
        Span<WaterColumn> cols, Vector2 origin, float restY, float depth,
        float columnWidth, VertexPositionColor[] verts, out int vertCount)
    {
        Color surfaceColor = new Color(40, 130, 200, 160);  // lighter, semi-transparent
        Color bottomColor  = new Color(10, 40, 80, 220);    // darker, more opaque

        vertCount = 0;
        for (int i = 0; i < cols.Length; i++)
        {
            float x = origin.X + i * columnWidth;
            float surfaceY = origin.Y + restY + cols[i].Height;
            float bottomY  = origin.Y + restY + depth;

            verts[vertCount++] = new VertexPositionColor(
                new Vector3(x, surfaceY, 0), surfaceColor);
            verts[vertCount++] = new VertexPositionColor(
                new Vector3(x, bottomY, 0), bottomColor);
        }
    }

    /// <summary>Draw the surface line on top of the water body.</summary>
    public static void DrawSurfaceLine(
        SpriteBatch batch, Texture2D pixel, Span<WaterColumn> cols,
        Vector2 origin, float restY, float columnWidth, Color lineColor, float thickness = 2f)
    {
        for (int i = 0; i < cols.Length - 1; i++)
        {
            Vector2 a = new(origin.X + i * columnWidth,
                            origin.Y + restY + cols[i].Height);
            Vector2 b = new(origin.X + (i + 1) * columnWidth,
                            origin.Y + restY + cols[i + 1].Height);
            DrawLine(batch, pixel, a, b, lineColor, thickness);
        }
    }

    static void DrawLine(SpriteBatch b, Texture2D px, Vector2 a, Vector2 end,
                          Color c, float t)
    {
        Vector2 diff = end - a;
        float angle  = MathF.Atan2(diff.Y, diff.X);
        float length = diff.Length();
        b.Draw(px, a, null, c, angle, Vector2.Zero, new Vector2(length, t),
               SpriteEffects.None, 0);
    }
}
```

Render order: draw the filled triangle strip with `BasicEffect` (alpha blend enabled), then draw the surface line via `SpriteBatch`.

---

## 4 — Splash Effects

When an entity crosses the water surface, apply a velocity impulse to nearby columns and spawn splash particles.

```csharp
public static class WaterSplash
{
    /// <summary>Disturb columns near an impact point.</summary>
    public static void CreateSplash(
        Span<WaterColumn> cols, float localX, float columnWidth,
        float impactSpeed, float radius = 3f)
    {
        int center = (int)(localX / columnWidth);
        int halfR  = (int)radius;

        for (int i = Math.Max(0, center - halfR);
                 i < Math.Min(cols.Length, center + halfR + 1); i++)
        {
            float dist   = Math.Abs(i - center);
            float falloff = 1f - (dist / (halfR + 1));
            cols[i].Velocity += impactSpeed * falloff * 0.4f;
        }
    }

    /// <summary>Spawn upward-flying droplet particles at the splash site.</summary>
    public static void SpawnDroplets(
        World world, Vector2 splashPos, float impactSpeed, int baseCount = 6)
    {
        int count = (int)(baseCount * Math.Clamp(Math.Abs(impactSpeed) / 200f, 0.5f, 3f));
        var rng = Random.Shared;

        for (int i = 0; i < count; i++)
        {
            float angle = MathHelper.ToRadians(rng.Next(200, 340)); // upward arc
            float speed = Math.Abs(impactSpeed) * (0.3f + rng.NextSingle() * 0.5f);
            Vector2 vel = new(MathF.Cos(angle) * speed, MathF.Sin(angle) * speed);

            world.Create(
                new Position { Value = splashPos },
                new Velocity { Value = vel },
                new Particle
                {
                    Lifetime = 0.3f + rng.NextSingle() * 0.4f,
                    Color = new Color(150, 200, 255, 200),
                    Size = 2f + rng.NextSingle() * 2f,
                    Gravity = 400f
                });
        }
    }
}
```

Splash magnitude scales with `impactSpeed` — a gentle wade barely ripples; a fast-falling body sends big waves and many droplets.

---

## 5 — Buoyancy

Approximate submerged volume by how much of the entity's AABB overlaps the water surface, then apply an upward force proportional to that overlap.

```csharp
public record struct Buoyant(float Density, float DragCoefficient);

public static class BuoyancyPhysics
{
    public const float WaterDensity = 1.0f;
    public const float Gravity = 400f;

    public static void Apply(
        ref Velocity vel, in Position pos, in AABB box, in Buoyant buoyant,
        Span<WaterColumn> cols, Vector2 waterOrigin, float restY,
        float columnWidth, float dt)
    {
        float entityBottom = pos.Value.Y + box.Height;
        float entityTop    = pos.Value.Y;
        float surfaceY     = GetSurfaceAt(cols, pos.Value.X - waterOrigin.X,
                                           columnWidth, waterOrigin.Y + restY);

        if (entityBottom <= surfaceY) return; // above water

        // submerged fraction (0..1)
        float submergedDepth = Math.Min(entityBottom - surfaceY, box.Height);
        float submergedRatio = submergedDepth / box.Height;

        // buoyant force: upward, proportional to displaced volume vs density
        float buoyantForce = (WaterDensity / buoyant.Density) * Gravity * submergedRatio;
        vel.Value.Y -= buoyantForce * dt;

        // drag slows movement in water
        vel.Value.X *= (1f - buoyant.DragCoefficient * submergedRatio * dt);
        vel.Value.Y *= (1f - buoyant.DragCoefficient * submergedRatio * dt * 0.5f);
    }

    static float GetSurfaceAt(Span<WaterColumn> cols, float localX,
                               float colWidth, float baseY)
    {
        int idx = Math.Clamp((int)(localX / colWidth), 0, cols.Length - 1);
        return baseY + cols[idx].Height;
    }
}
```

- **Light objects** (`Density < 1.0`) float and bob on the surface.
- **Heavy objects** (`Density > 1.0`) sink slowly, drag reducing terminal velocity.

---

## 6 — Water Shader (HLSL)

A surface shader that scrolls a caustic texture, applies wave distortion, and tints color.

```hlsl
// WaterSurface.fx
sampler2D MainTexture : register(s0);
sampler2D CausticTexture : register(s1);

float  Time;
float  WaveAmplitude;   // 0.005
float  WaveFrequency;   // 10.0
float  CausticSpeed;    // 0.03
float  CausticScale;    // 8.0
float4 WaterTint;       // (0.2, 0.5, 0.8, 0.6)
float  DistortStrength; // 0.01

float4 PS_WaterSurface(float2 uv : TEXCOORD0) : COLOR0
{
    // wave distortion
    float2 distort;
    distort.x = sin(uv.y * WaveFrequency + Time * 3.0) * WaveAmplitude;
    distort.y = cos(uv.x * WaveFrequency + Time * 2.5) * WaveAmplitude * 0.5;

    float2 distortedUV = uv + distort;
    float4 scene = tex2D(MainTexture, distortedUV);

    // scrolling caustic overlay
    float2 causticUV = uv * CausticScale + float2(Time * CausticSpeed, Time * CausticSpeed * 0.7);
    float4 caustic = tex2D(CausticTexture, causticUV);

    // blend: scene + caustic highlight + water tint
    float4 result = scene;
    result.rgb += caustic.rgb * 0.15;
    result.rgb = lerp(result.rgb, WaterTint.rgb, WaterTint.a);
    result.a = 1.0;
    return result;
}

technique WaterSurface
{
    pass P0
    {
        PixelShader = compile ps_3_0 PS_WaterSurface();
    }
}
```

**Underwater distortion** — when the camera is below the water surface, apply a full-screen post-process:

```hlsl
// UnderwaterDistort.fx
sampler2D SceneTexture : register(s0);
float Time;
float4 UnderwaterTint; // (0.1, 0.3, 0.6, 0.4)

float4 PS_Underwater(float2 uv : TEXCOORD0) : COLOR0
{
    float2 d;
    d.x = sin(uv.y * 20.0 + Time * 2.0) * 0.003;
    d.y = cos(uv.x * 15.0 + Time * 1.5) * 0.002;

    float4 scene = tex2D(SceneTexture, uv + d);
    scene.rgb = lerp(scene.rgb, UnderwaterTint.rgb, UnderwaterTint.a);

    // light rays — brighter toward top of screen
    float ray = smoothstep(1.0, 0.0, uv.y) * 0.08;
    float rayPattern = sin(uv.x * 40.0 + Time) * 0.5 + 0.5;
    scene.rgb += ray * rayPattern;

    return scene;
}

technique Underwater
{
    pass P0 { PixelShader = compile ps_3_0 PS_Underwater(); }
}
```

---

## 7 — Tile-Based Water

For tile-map games, a simpler approach: animated tile IDs for water surface, fill tiles below, and metadata for flow.

```csharp
public record struct WaterTile(
    WaterTileType Type,
    FlowDirection Flow,
    byte AnimFrame,
    byte AnimFrameCount,
    float AnimTimer
);

public enum WaterTileType : byte
{
    None, Surface, Body, Waterfall, SurfaceEdgeLeft, SurfaceEdgeRight
}

public enum FlowDirection : byte
{
    None, Left, Right, Down
}

public static class TileWaterSystem
{
    const float FrameDuration = 0.15f;

    public static void AnimateTiles(Span<WaterTile> tiles, float dt)
    {
        for (int i = 0; i < tiles.Length; i++)
        {
            if (tiles[i].Type == WaterTileType.None) continue;

            tiles[i].AnimTimer += dt;
            if (tiles[i].AnimTimer >= FrameDuration)
            {
                tiles[i].AnimTimer -= FrameDuration;
                tiles[i].AnimFrame = (byte)((tiles[i].AnimFrame + 1)
                                             % tiles[i].AnimFrameCount);
            }
        }
    }

    /// <summary>Rise or lower water level by adding/removing tile rows.</summary>
    public static void SetWaterLevel(int[,] tileMap, int column, int newSurfaceRow,
                                      int bottomRow, int surfaceTileId, int bodyTileId)
    {
        for (int row = 0; row <= bottomRow; row++)
        {
            if (row < newSurfaceRow)
                tileMap[column, row] = 0; // air
            else if (row == newSurfaceRow)
                tileMap[column, row] = surfaceTileId;
            else
                tileMap[column, row] = bodyTileId;
        }
    }
}
```

Waterfall tiles use a `Down` flow direction and cycle through a vertical scroll animation. Place `SurfaceEdgeLeft`/`SurfaceEdgeRight` at boundaries for nicer visuals.

---

## 8 — Water Current / Flow

Apply a horizontal force to entities inside a water zone. Visual cues: scrolling UV offset and particles drifting in flow direction.

```csharp
public record struct WaterCurrent(Vector2 FlowDirection, float Strength);

public static class WaterCurrentPhysics
{
    /// <summary>Apply current force to an entity's velocity if inside the zone.</summary>
    public static void Apply(ref Velocity vel, in WaterCurrent current,
                              float submergedRatio, float dt)
    {
        if (submergedRatio <= 0f) return;

        Vector2 force = current.FlowDirection * current.Strength * submergedRatio;
        vel.Value += force * dt;
    }

    /// <summary>Calculate effective swim speed against current.</summary>
    public static float EffectiveSpeed(float swimSpeed, in WaterCurrent current,
                                        Vector2 swimDir)
    {
        float opposition = Vector2.Dot(
            Vector2.Normalize(current.FlowDirection), swimDir);
        // opposition < 0 means swimming against current
        float penalty = Math.Max(0f, -opposition) * current.Strength * 0.6f;
        return Math.Max(0f, swimSpeed - penalty);
    }
}
```

Visual flow: offset the caustic texture UV by `current.FlowDirection * Time * scrollSpeed`. Spawn ambient particles that drift in the flow direction for readable feedback.

---

## 9 — Underwater Physics

When the player is submerged, swap to swim controls: reduced gravity, directional swim input, air meter, and visual effects.

```csharp
public record struct Swimmer(
    float AirMax,
    float AirRemaining,
    float SwimSpeed,
    bool IsSubmerged
);

public static class UnderwaterPhysics
{
    const float UnderwaterGravityScale = 0.15f;
    const float BubbleInterval = 0.4f;

    public static void UpdateSwimmer(ref Swimmer swimmer, ref Velocity vel,
                                      in InputState input, float gravity, float dt)
    {
        if (!swimmer.IsSubmerged) return;

        // reduced gravity
        vel.Value.Y += gravity * UnderwaterGravityScale * dt;

        // directional swim
        Vector2 swimDir = input.MoveDirection; // normalized
        if (swimDir.LengthSquared() > 0.01f)
            vel.Value += swimDir * swimmer.SwimSpeed * dt;

        // drain air
        swimmer.AirRemaining -= dt;
        if (swimmer.AirRemaining <= 0f)
        {
            swimmer.AirRemaining = 0f;
            // trigger drowning damage via event/flag
        }
    }

    public static void SpawnBubbles(World world, in Position pos,
                                     ref float bubbleTimer, float dt)
    {
        bubbleTimer -= dt;
        if (bubbleTimer > 0f) return;
        bubbleTimer = BubbleInterval + Random.Shared.NextSingle() * 0.2f;

        world.Create(
            new Position { Value = pos.Value + new Vector2(0, -4) },
            new Velocity { Value = new Vector2(
                (Random.Shared.NextSingle() - 0.5f) * 10f, -30f) },
            new Particle
            {
                Lifetime = 0.8f + Random.Shared.NextSingle() * 0.6f,
                Color = new Color(200, 230, 255, 140),
                Size = 2f + Random.Shared.NextSingle() * 2f,
                Gravity = -20f // float upward
            });
    }
}
```

Render the underwater overlay shader (Section 6) when the camera's Y center is below the water surface.

---

## 10 — Lava / Acid Variants

Reuse the spring column system with different visuals and gameplay hooks.

```csharp
public enum LiquidType : byte { Water, Lava, Acid }

public record struct LiquidProperties(
    LiquidType Type,
    Color SurfaceColor,
    Color BodyColor,
    float DamagePerSecond,
    bool Glows
);

public static class LiquidVariants
{
    public static LiquidProperties Water => new(
        LiquidType.Water,
        new Color(40, 130, 200, 160),
        new Color(10, 40, 80, 220),
        DamagePerSecond: 0f,
        Glows: false);

    public static LiquidProperties Lava => new(
        LiquidType.Lava,
        new Color(255, 160, 30, 240),
        new Color(200, 50, 10, 250),
        DamagePerSecond: 50f,
        Glows: true);

    public static LiquidProperties Acid => new(
        LiquidType.Acid,
        new Color(100, 255, 50, 200),
        new Color(40, 120, 20, 230),
        DamagePerSecond: 25f,
        Glows: true);

    /// <summary>Apply contact damage when entity overlaps liquid surface.</summary>
    public static void ApplyContactDamage(ref Health health,
                                           in LiquidProperties liquid, float dt)
    {
        if (liquid.DamagePerSecond <= 0f) return;
        health.Current -= liquid.DamagePerSecond * dt;
    }

    /// <summary>Spawn rising bubble/ember particles from the surface.</summary>
    public static void SpawnSurfaceParticles(
        World world, Vector2 surfacePos, in LiquidProperties liquid, float dt)
    {
        if (Random.Shared.NextSingle() > 0.05f) return; // sparse

        Color pColor = liquid.Type switch
        {
            LiquidType.Lava => new Color(255, 200, 50, 200),
            LiquidType.Acid => new Color(150, 255, 100, 180),
            _ => new Color(200, 230, 255, 140)
        };

        world.Create(
            new Position { Value = surfacePos },
            new Velocity { Value = new Vector2(
                (Random.Shared.NextSingle() - 0.5f) * 8f, -20f) },
            new Particle
            {
                Lifetime = 0.5f + Random.Shared.NextSingle() * 0.5f,
                Color = pColor,
                Size = 2f + Random.Shared.NextSingle() * 3f,
                Gravity = -15f
            });
    }
}
```

For glow, render an additive-blend circle at the surface or use a light entity from your lighting system.

---

## 11 — ECS Integration

Tie everything together with Arch ECS components and systems.

### Components

```csharp
public record struct WaterBody(
    Vector2 Position,
    float Width,
    float Depth,
    int ColumnCount,
    float RestY,
    LiquidType LiquidType
);

public record struct WaterSurface(
    WaterColumn[] Columns,
    float ColumnWidth
);

public record struct WaterTrigger(bool Active);

public record struct Submerged(float Ratio, Entity WaterEntity);
```

### WaterSurfaceSystem — physics tick

```csharp
public class WaterSurfaceSystem : ISystem
{
    QueryDescription _query = new QueryDescription().WithAll<WaterBody, WaterSurface>();

    public void Update(World world, float dt)
    {
        world.Query(in _query, (ref WaterBody body, ref WaterSurface surface) =>
        {
            var cols = surface.Columns.AsSpan();
            WaterSpringPhysics.UpdateColumns(cols);
            WaterSpringPhysics.Propagate(cols);
        });
    }
}
```

### WaterRenderSystem — draw pass

```csharp
public class WaterRenderSystem : ISystem
{
    readonly VertexPositionColor[] _verts = new VertexPositionColor[2048];
    readonly BasicEffect _effect;
    readonly Texture2D _pixel;

    public WaterRenderSystem(GraphicsDevice gd, Texture2D pixel)
    {
        _pixel = pixel;
        _effect = new BasicEffect(gd) { VertexColorEnabled = true };
    }

    public void Draw(World world, SpriteBatch batch, Matrix view, Matrix projection)
    {
        var query = new QueryDescription().WithAll<WaterBody, WaterSurface>();
        var gd = batch.GraphicsDevice;

        _effect.View = view;
        _effect.Projection = projection;
        _effect.World = Matrix.Identity;

        world.Query(in query, (ref WaterBody body, ref WaterSurface surface) =>
        {
            var props = body.LiquidType switch
            {
                LiquidType.Lava => LiquidVariants.Lava,
                LiquidType.Acid => LiquidVariants.Acid,
                _ => LiquidVariants.Water
            };

            var cols = surface.Columns.AsSpan();
            WaterRenderer.BuildMesh(cols, body.Position, body.RestY, body.Depth,
                                     surface.ColumnWidth, _verts, out int count);

            // enable alpha blending
            gd.BlendState = BlendState.AlphaBlend;
            foreach (var pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                gd.DrawUserPrimitives(PrimitiveType.TriangleStrip, _verts, 0, count - 2);
            }

            // surface line
            batch.Begin(transformMatrix: view);
            WaterRenderer.DrawSurfaceLine(batch, _pixel, cols, body.Position,
                                           body.RestY, surface.ColumnWidth,
                                           props.SurfaceColor, 2f);
            batch.End();
        });
    }
}
```

### BuoyancySystem

```csharp
public class BuoyancySystem : ISystem
{
    public void Update(World world, float dt)
    {
        var waterQuery = new QueryDescription().WithAll<WaterBody, WaterSurface>();
        var entityQuery = new QueryDescription().WithAll<Position, Velocity, AABB, Buoyant>();

        // for each water body, check each buoyant entity
        world.Query(in waterQuery, (Entity wEnt, ref WaterBody body, ref WaterSurface surface) =>
        {
            world.Query(in entityQuery,
                (ref Position pos, ref Velocity vel, ref AABB box, ref Buoyant buoyant) =>
            {
                // bounds check — is entity horizontally within water?
                float left  = body.Position.X;
                float right = body.Position.X + body.Width;
                if (pos.Value.X + box.Width < left || pos.Value.X > right) return;

                BuoyancyPhysics.Apply(ref vel, in pos, in box, in buoyant,
                    surface.Columns.AsSpan(), body.Position, body.RestY,
                    surface.ColumnWidth, dt);
            });
        });
    }
}
```

### Splash detection via WaterTrigger

```csharp
public class WaterTriggerSystem : ISystem
{
    public void Update(World world, float dt)
    {
        var waterQuery = new QueryDescription().WithAll<WaterBody, WaterSurface>();
        var entityQuery = new QueryDescription()
            .WithAll<Position, Velocity, AABB>()
            .WithNone<Submerged>();

        world.Query(in waterQuery, (Entity wEnt, ref WaterBody body, ref WaterSurface surface) =>
        {
            float surfaceWorldY = body.Position.Y + body.RestY;

            world.Query(in entityQuery,
                (Entity entity, ref Position pos, ref Velocity vel, ref AABB box) =>
            {
                float bottom = pos.Value.Y + box.Height;
                if (bottom < surfaceWorldY) return;    // above surface
                float localX = pos.Value.X - body.Position.X;
                if (localX < 0 || localX > body.Width) return; // outside bounds

                // entering water — splash!
                WaterSplash.CreateSplash(surface.Columns.AsSpan(), localX,
                    surface.ColumnWidth, vel.Value.Y);
                WaterSplash.SpawnDroplets(world,
                    new Vector2(pos.Value.X, surfaceWorldY), vel.Value.Y);

                // tag as submerged
                world.Add(entity, new Submerged(0f, wEnt));
            });
        });
    }
}
```

### Factory helper

```csharp
public static class WaterFactory
{
    public static Entity CreateWaterBody(
        World world, Vector2 position, float width, float depth,
        int columnCount = 0, LiquidType type = LiquidType.Water)
    {
        if (columnCount <= 0)
            columnCount = Math.Max(8, (int)(width / 6f));

        float colWidth = width / columnCount;
        var columns = new WaterColumn[columnCount];

        return world.Create(
            new WaterBody(position, width, depth, columnCount, 0f, type),
            new WaterSurface(columns, colWidth));
    }
}
```

---

## Quick-Reference: Recommended Parameters

| Scenario | Tension | Dampening | Spread | Column Width |
|----------|---------|-----------|--------|-------------|
| Calm lake | 0.020 | 0.030 | 0.20 | 6 px |
| River | 0.025 | 0.020 | 0.30 | 5 px |
| Lava (viscous) | 0.015 | 0.040 | 0.15 | 8 px |
| Acid (bubbly) | 0.030 | 0.020 | 0.25 | 5 px |

---

## Summary

| System | Purpose |
|--------|---------|
| `WaterSpringPhysics` | Column spring simulation (Hooke's law + propagation) |
| `WaterRenderer` | Triangle-strip body + surface line drawing |
| `WaterSplash` | Impulse columns + spawn droplet particles |
| `BuoyancyPhysics` | Upward force proportional to submersion |
| `WaterSurface.fx` | Caustic scrolling, wave distortion, color tint |
| `UnderwaterDistort.fx` | Full-screen post-process with light rays |
| `TileWaterSystem` | Animated tile-based water for tile maps |
| `WaterCurrentPhysics` | Horizontal flow force on submerged entities |
| `UnderwaterPhysics` | Swim controls, air meter, bubble particles |
| `LiquidVariants` | Lava/acid reuse with damage + glow |
| ECS systems | `WaterSurfaceSystem`, `WaterRenderSystem`, `BuoyancySystem`, `WaterTriggerSystem` |

Start with `WaterFactory.CreateWaterBody()`, register the systems, and you have a complete 2D water simulation pipeline.

---

## Performance Notes

- **Pool splash particles** — frequent splash events create many short-lived entities. Use an object pool ([G67 Object Pooling](./G67_object_pooling.md)) for droplet particles instead of creating/destroying entities per splash.
- **Lava/acid contact damage** integrates with the damage pipeline from [G64 Combat & Damage Systems](./G64_combat_damage_systems.md) — apply damage via the standard `DamageEvent` rather than directly decrementing health to ensure i-frames, resistance, and UI feedback all trigger correctly.
