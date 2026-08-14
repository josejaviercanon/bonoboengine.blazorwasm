# G60 — Trail & Line Rendering



> **Category:** Guide · **Related:** [G2 Rendering & Graphics](./G2_rendering_and_graphics.md) · [G23 Particles](./G23_particles.md) · [G27 Shaders & Visual Effects](./G27_shaders_and_effects.md) · [G13 C# Performance](./G13_csharp_performance.md)

---

## 1 — Trail Rendering Basics

A **trail** is a strip of geometry that follows a moving object and fades over time. The renderer records positions each frame, connects them into a mesh (usually a triangle strip), and applies alpha fade so the tail dissolves behind the head.

**Common use cases:**

| Effect | Description |
|---|---|
| Sword swing | Arc trail attached to the blade tip during attack animations |
| Bullet trace | Thin, fast-fading line behind projectiles |
| Player dash | Wide trail or ghost images left behind during a dash |
| Magic effects | Spiralling, coloured ribbons following spell projectiles |

The core loop is always the same: **record → build mesh → render → expire old points**.

---

## 2 — Point-Based Trail System

Store trail sample points in a **ring buffer**. Each point carries position, timestamp, and width. Every frame, push the emitter's current position; discard points whose age exceeds the trail lifetime.

```csharp
public struct TrailPoint
{
    public Vector2 Position;
    public float   Width;
    public double  TimeStamp;  // total seconds when recorded
}

public sealed class TrailBuffer
{
    readonly TrailPoint[] _points;
    int  _head;
    int  _count;
    
    public int   Count => _count;
    public float Lifetime { get; set; }

    public TrailBuffer(int capacity, float lifetime)
    {
        _points  = new TrailPoint[capacity];
        Lifetime = lifetime;
    }

    public void Push(Vector2 pos, float width, double time)
    {
        _points[_head] = new TrailPoint { Position = pos, Width = width, TimeStamp = time };
        _head = (_head + 1) % _points.Length;
        if (_count < _points.Length) _count++;
    }

    /// <summary>Discard points older than Lifetime seconds.</summary>
    public void Expire(double currentTime)
    {
        while (_count > 0)
        {
            int tail = (_head - _count + _points.Length) % _points.Length;
            if (currentTime - _points[tail].TimeStamp > Lifetime) _count--;
            else break;
        }
    }

    /// <summary>Index 0 = oldest (tail), index Count-1 = newest (head).</summary>
    public TrailPoint this[int i]
    {
        get
        {
            int idx = (_head - _count + i + _points.Length) % _points.Length;
            return _points[idx];
        }
    }
}
```

A capacity of 64–128 points is plenty for most trails at 60 fps.

---

## 3 — Triangle Strip Generation

To give a trail visible width, offset each sample point **perpendicularly** to the direction of travel, producing two vertices per point — left and right. Wire them in order and you get a `TriangleStrip`.

```csharp
public static class TrailMeshBuilder
{
    /// <summary>
    /// Builds a triangle strip from the trail buffer.
    /// Returns the number of vertices written.
    /// </summary>
    public static int Build(
        TrailBuffer buffer,
        double currentTime,
        VertexPositionColorTexture[] verts)
    {
        int count = buffer.Count;
        if (count < 2) return 0;

        int vi = 0;
        for (int i = 0; i < count; i++)
        {
            TrailPoint pt = buffer[i];

            // --- direction & perpendicular ---
            Vector2 dir;
            if (i < count - 1)
                dir = Vector2.Normalize(buffer[i + 1].Position - pt.Position);
            else
                dir = Vector2.Normalize(pt.Position - buffer[i - 1].Position);

            Vector2 perp = new Vector2(-dir.Y, dir.X);

            // --- fade: 0 at tail, 1 at head ---
            float t     = count > 1 ? (float)i / (count - 1) : 1f;
            float age   = (float)(currentTime - pt.TimeStamp);
            float alpha = MathHelper.Clamp(1f - age / buffer.Lifetime, 0f, 1f) * t;
            float width = pt.Width * t;   // taper toward tail

            Color color = Color.White * alpha;
            float u     = t;              // UV.x along length

            Vector2 left  = pt.Position + perp * width * 0.5f;
            Vector2 right = pt.Position - perp * width * 0.5f;

            verts[vi++] = new VertexPositionColorTexture(
                new Vector3(left, 0f), color, new Vector2(u, 0f));
            verts[vi++] = new VertexPositionColorTexture(
                new Vector3(right, 0f), color, new Vector2(u, 1f));
        }
        return vi;
    }
}
```

### Corner handling

At sharp bends the perpendicular vectors of adjacent segments diverge, causing spikes. Two mitigations:

- **Miter join** — average the perpendicular of the incoming and outgoing segments. Fast, but spikes at very sharp angles (clamp miter length to 2× width).
- **Bevel join** — insert an extra triangle at the bend. Cleaner for slow, curvy trails.

For most game trails (sword swings, projectiles), the emitter moves fast enough that angles stay shallow and a simple averaged perpendicular works fine.

---

## 4 — Trail Rendering with DrawUserPrimitives

MonoGame's `GraphicsDevice.DrawUserPrimitives` renders from a CPU-side vertex array — perfect for trails that change every frame.

```csharp
public sealed class TrailRenderer
{
    readonly GraphicsDevice _gd;
    readonly BasicEffect    _effect;
    readonly VertexPositionColorTexture[] _verts;

    public TrailRenderer(GraphicsDevice gd, int maxVerts = 256)
    {
        _gd    = gd;
        _verts = new VertexPositionColorTexture[maxVerts];

        _effect = new BasicEffect(gd)
        {
            VertexColorEnabled = true,
            TextureEnabled     = false,
            LightingEnabled    = false,
            World              = Matrix.Identity,
            View               = Matrix.Identity
        };
    }

    public void Draw(TrailBuffer buffer, double time, Matrix projection, Texture2D? texture = null)
    {
        int vertCount = TrailMeshBuilder.Build(buffer, time, _verts);
        if (vertCount < 4) return;   // need at least 2 triangles

        _effect.Projection     = projection;
        _effect.TextureEnabled = texture != null;
        if (texture != null) _effect.Texture = texture;

        _gd.BlendState        = BlendState.NonPremultiplied;
        _gd.DepthStencilState = DepthStencilState.None;
        _gd.RasterizerState   = RasterizerState.CullNone;

        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _gd.DrawUserPrimitives(
                PrimitiveType.TriangleStrip,
                _verts, 0, vertCount - 2);
        }
    }
}
```

Use `BlendState.NonPremultiplied` for standard alpha trails, or `BlendState.Additive` for glowing/fire effects.

---

## 5 — Trail Fading

Three fading axes work together for polished trails:

| Axis | What changes | How |
|---|---|---|
| **Alpha fade** | Opacity | `alpha = t` where `t` goes 0 (tail) → 1 (head) |
| **Width taper** | Geometry width | `width = baseWidth * t` |
| **Color gradient** | Vertex color | Lerp from tail color to head color along `t` |

The `TrailMeshBuilder.Build` method above already demonstrates alpha and width. For a colour gradient, replace the fixed `Color.White * alpha` with:

```csharp
Color tailColor = Color.Red;
Color headColor = Color.Yellow;
Color color     = Color.Lerp(tailColor, headColor, t) * alpha;
```

This gives a fire-like fade from yellow (newest) to red (oldest) before vanishing.

---

## 6 — Textured Trails

Apply a 1D gradient texture (horizontal: head → tail) or a tiling pattern to the trail strip. The `u` coordinate mapped along the strip length handles this automatically.

### Scrolling UVs for animated trails

Offset `u` by accumulated time to create motion:

```csharp
float scrollOffset = (float)(currentTime * scrollSpeed) % 1f;
float u = t + scrollOffset;
```

### Texture ideas

| Look | Texture |
|---|---|
| Fire | Orange-to-transparent horizontal gradient, additive blend |
| Ice | Light blue with white sparkle noise, alpha blend |
| Electric | Jagged white-on-black strip, additive blend, fast scroll |

---

## 7 — Line Rendering

MonoGame has no built-in thick line primitive. To render lines with width, build a **quad per segment** — the same perpendicular-offset technique as trails but with exactly two points.

```csharp
public static class LineRenderer
{
    static readonly VertexPositionColorTexture[] _quad = new VertexPositionColorTexture[4];

    public static void DrawLine(
        GraphicsDevice gd, BasicEffect effect,
        Vector2 a, Vector2 b, float thickness, Color color)
    {
        Vector2 dir  = Vector2.Normalize(b - a);
        Vector2 perp = new Vector2(-dir.Y, dir.X) * thickness * 0.5f;

        _quad[0] = Vert(a + perp, color, 0, 0);
        _quad[1] = Vert(a - perp, color, 0, 1);
        _quad[2] = Vert(b + perp, color, 1, 0);
        _quad[3] = Vert(b - perp, color, 1, 1);

        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            gd.DrawUserPrimitives(PrimitiveType.TriangleStrip, _quad, 0, 2);
        }
    }

    static VertexPositionColorTexture Vert(Vector2 p, Color c, float u, float v)
        => new(new Vector3(p, 0), c, new Vector2(u, v));

    /// <summary>Draws a dashed line by stepping along the segment.</summary>
    public static void DrawDashedLine(
        GraphicsDevice gd, BasicEffect effect,
        Vector2 a, Vector2 b, float thickness, Color color,
        float dashLen = 8f, float gapLen = 6f)
    {
        Vector2 dir     = b - a;
        float   length  = dir.Length();
        Vector2 normDir = dir / length;
        float   cursor  = 0f;
        bool    draw    = true;

        while (cursor < length)
        {
            float segLen = draw ? dashLen : gapLen;
            float end    = MathF.Min(cursor + segLen, length);
            if (draw)
                DrawLine(gd, effect, a + normDir * cursor, a + normDir * end, thickness, color);
            cursor = end;
            draw   = !draw;
        }
    }
}
```

**Anti-aliasing:** Apply a 1-pixel texture with soft edges (white centre, transparent edges) and enable texture on the effect. The UV `v` coordinate (0 → 1 across thickness) maps to this gradient.

---

## 8 — Laser Beams

A laser beam is a textured quad from point A to point B with animated UVs and optional additive glow.

```csharp
public static class LaserBeamRenderer
{
    public static void Draw(
        GraphicsDevice gd, BasicEffect effect, Texture2D beamTex,
        Vector2 origin, Vector2 target,
        float width, float time, float scrollSpeed = 3f)
    {
        Vector2 dir  = Vector2.Normalize(target - origin);
        Vector2 perp = new Vector2(-dir.Y, dir.X) * width * 0.5f;

        // pulsing width
        float pulse = 1f + 0.15f * MathF.Sin(time * 12f);
        perp *= pulse;

        float len    = Vector2.Distance(origin, target);
        float uScale = len / beamTex.Width;  // tile texture along beam
        float scroll = (time * scrollSpeed) % 1f;

        var verts = new VertexPositionColorTexture[4];
        verts[0] = new(new Vector3(origin + perp, 0), Color.White, new Vector2(scroll, 0));
        verts[1] = new(new Vector3(origin - perp, 0), Color.White, new Vector2(scroll, 1));
        verts[2] = new(new Vector3(target + perp, 0), Color.White, new Vector2(scroll + uScale, 0));
        verts[3] = new(new Vector3(target - perp, 0), Color.White, new Vector2(scroll + uScale, 1));

        effect.TextureEnabled = true;
        effect.Texture        = beamTex;
        gd.BlendState         = BlendState.Additive;
        gd.SamplerStates[0]   = SamplerState.LinearWrap;

        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            gd.DrawUserPrimitives(PrimitiveType.TriangleStrip, verts, 0, 2);
        }
    }
}
```

**Glow:** Render the beam twice — once at normal width, once at 3× width with lower alpha. Additive blending stacks naturally into a glow halo.

**Hit detection:** Cast a ray from origin along direction, test against collidable geometry, and clamp `target` to the hit point.

---

## 9 — Rope / Chain Rendering

### Verlet integration

Each rope node stores **current** and **previous** position. Integration is implicit velocity:

```csharp
public struct RopeNode
{
    public Vector2 Position;
    public Vector2 OldPosition;
    public bool    Pinned;       // anchored nodes don't move
}

public sealed class RopeSimulation
{
    public readonly RopeNode[] Nodes;
    public readonly float SegmentLength;
    const float Gravity   = 980f;
    const int   Iterations = 5;

    public RopeSimulation(Vector2 start, Vector2 end, int segments, float segLen)
    {
        SegmentLength = segLen;
        Nodes = new RopeNode[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            Vector2 p = Vector2.Lerp(start, end, (float)i / segments);
            Nodes[i] = new RopeNode { Position = p, OldPosition = p };
        }
        Nodes[0].Pinned = true;  // anchor the top
    }

    public void Update(float dt)
    {
        // --- verlet integration ---
        for (int i = 0; i < Nodes.Length; i++)
        {
            if (Nodes[i].Pinned) continue;
            Vector2 vel = Nodes[i].Position - Nodes[i].OldPosition;
            Nodes[i].OldPosition = Nodes[i].Position;
            Nodes[i].Position   += vel + new Vector2(0, Gravity) * dt * dt;
        }

        // --- distance constraints ---
        for (int iter = 0; iter < Iterations; iter++)
        {
            for (int i = 0; i < Nodes.Length - 1; i++)
            {
                Vector2 delta = Nodes[i + 1].Position - Nodes[i].Position;
                float   dist  = delta.Length();
                float   diff  = (dist - SegmentLength) / dist * 0.5f;
                Vector2 offset = delta * diff;

                if (!Nodes[i].Pinned)     Nodes[i].Position     += offset;
                if (!Nodes[i + 1].Pinned) Nodes[i + 1].Position -= offset;
            }
        }
    }
}
```

### Rendering as a triangle strip

Same perpendicular-offset approach as trails. Walk the node array, compute perpendicular from segment direction, emit left/right verts. Apply a rope/chain texture.

### Grapple mechanics

Pin `Nodes[0]` to a ceiling hook, `Nodes[^1]` to the player. Each frame update the pinned node's position to match its anchor entity. The simulation handles the swing naturally.

---

## 10 — Lightning / Electric Arcs

### Midpoint displacement algorithm

1. Start with a single segment from A to B.
2. Find the midpoint, offset it **perpendicular** to the segment by a random amount scaled by segment length.
3. Recurse on each half, halving the offset scale.
4. After 4–6 levels of recursion you get a jagged bolt.

```csharp
public static class LightningGenerator
{
    static readonly Random _rng = new();

    public static List<Vector2> Generate(
        Vector2 start, Vector2 end,
        int subdivisions = 5, float jitter = 80f)
    {
        var points = new List<Vector2> { start, end };

        float offset = jitter;
        for (int gen = 0; gen < subdivisions; gen++)
        {
            var next = new List<Vector2> { points[0] };
            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector2 a   = points[i];
                Vector2 b   = points[i + 1];
                Vector2 mid = (a + b) * 0.5f;

                Vector2 dir  = Vector2.Normalize(b - a);
                Vector2 perp = new Vector2(-dir.Y, dir.X);
                float   disp = ((float)_rng.NextDouble() * 2f - 1f) * offset;

                next.Add(mid + perp * disp);
                next.Add(b);
            }
            points = next;
            offset *= 0.5f;   // reduce displacement each level
        }
        return points;
    }
}
```

### Branching

At each midpoint, roll a chance (10–20%) to spawn a **branch** — a shorter bolt from the midpoint toward a random offset of the endpoint. Render branches with lower alpha and thinner width.

### Animation

Regenerate the bolt every 2–4 frames. The natural randomness creates a flickering, alive look. Lerp between two generated bolts for smoother animation if needed.

### Rendering

Render each segment as a thick line (Section 7) with `BlendState.Additive`. Draw the bolt twice: once thin and bright, once wide and dim for glow.

```csharp
// in Draw:
var bolt = LightningGenerator.Generate(start, end);
for (int i = 0; i < bolt.Count - 1; i++)
{
    // glow pass
    LineRenderer.DrawLine(gd, effect, bolt[i], bolt[i + 1], 8f, Color.Cyan * 0.3f);
    // core pass
    LineRenderer.DrawLine(gd, effect, bolt[i], bolt[i + 1], 2f, Color.White);
}
```

---

## 11 — Ghost Trail / After-Image

A dash ghost captures snapshots of the entity's sprite at intervals and renders fading copies.

```csharp
public struct GhostSnapshot
{
    public Vector2           Position;
    public float             Rotation;
    public Rectangle         SourceRect;
    public SpriteEffects     Flip;
    public double            SpawnTime;
}

public sealed class GhostTrail
{
    readonly GhostSnapshot[] _snapshots;
    int _head, _count;
    
    public float Lifetime  { get; set; } = 0.4f;
    public float Interval  { get; set; } = 0.04f;
    public Color Tint      { get; set; } = new Color(80, 140, 255); // blue dash
    
    double _lastSnap;

    public GhostTrail(int capacity = 12)
        => _snapshots = new GhostSnapshot[capacity];

    public void TryCapture(Vector2 pos, float rot, Rectangle src, SpriteEffects flip, double time)
    {
        if (time - _lastSnap < Interval) return;
        _lastSnap = time;

        _snapshots[_head] = new GhostSnapshot
        {
            Position = pos, Rotation = rot,
            SourceRect = src, Flip = flip, SpawnTime = time
        };
        _head = (_head + 1) % _snapshots.Length;
        if (_count < _snapshots.Length) _count++;
    }

    public void Draw(SpriteBatch sb, Texture2D atlas, double time)
    {
        for (int i = 0; i < _count; i++)
        {
            int idx = (_head - _count + i + _snapshots.Length) % _snapshots.Length;
            ref var snap = ref _snapshots[idx];

            float age   = (float)(time - snap.SpawnTime);
            if (age > Lifetime) continue;

            float alpha = 1f - age / Lifetime;
            Color color = Tint * (alpha * 0.6f);

            sb.Draw(atlas, snap.Position, snap.SourceRect, color,
                snap.Rotation, Vector2.Zero, 1f, snap.Flip, 0f);
        }
    }
}
```

**Tint presets:** `Color(80, 140, 255)` for dodge/dash, `Color(255, 60, 60)` for damage, `Color(200, 255, 200)` for heal.

---

## 12 — ECS Integration

### Components

```csharp
using Arch.Core;

/// <summary>Emits a position-based trail behind an entity.</summary>
[record struct]
public record struct TrailEmitter(
    TrailBuffer  Buffer,
    float        Width,
    bool         Emitting,
    Color        HeadColor,
    Color        TailColor,
    Texture2D?   Texture,
    BlendState   Blend
);

/// <summary>A laser beam from owner toward a target direction.</summary>
public record struct LaserComponent(
    float   MaxLength,
    float   Width,
    float   ScrollSpeed,
    Texture2D BeamTexture,
    bool    Active
);

/// <summary>Verlet rope attached between two points.</summary>
public record struct RopeComponent(
    RopeSimulation Sim,
    Texture2D?     Texture,
    float          Width
);

/// <summary>Ghost after-images for dash effects.</summary>
public record struct GhostTrailComponent(
    GhostTrail Trail,
    bool       Active
);
```

### TrailEmitSystem — record positions each frame

```csharp
public sealed class TrailEmitSystem
{
    readonly World _world;
    readonly QueryDescription _query = new QueryDescription()
        .WithAll<TrailEmitter, Position2D>();

    public TrailEmitSystem(World world) => _world = world;

    public void Update(double totalTime)
    {
        _world.Query(in _query, (ref TrailEmitter trail, ref Position2D pos) =>
        {
            trail.Buffer.Expire(totalTime);
            if (trail.Emitting)
                trail.Buffer.Push(pos.Value, trail.Width, totalTime);
        });
    }
}
```

### TrailRenderSystem — build mesh and draw

```csharp
public sealed class TrailRenderSystem
{
    readonly World _world;
    readonly TrailRenderer _renderer;
    readonly QueryDescription _query = new QueryDescription()
        .WithAll<TrailEmitter>();

    public TrailRenderSystem(World world, GraphicsDevice gd)
    {
        _world    = world;
        _renderer = new TrailRenderer(gd);
    }

    public void Draw(double totalTime, Matrix projection)
    {
        _world.Query(in _query, (ref TrailEmitter trail) =>
        {
            _renderer.Draw(trail.Buffer, totalTime, projection, trail.Texture);
        });
    }
}
```

### RopePhysicsSystem

```csharp
public sealed class RopePhysicsSystem
{
    readonly World _world;
    readonly QueryDescription _query = new QueryDescription()
        .WithAll<RopeComponent>();

    public RopePhysicsSystem(World world) => _world = world;

    public void Update(float dt)
    {
        _world.Query(in _query, (ref RopeComponent rope) =>
        {
            rope.Sim.Update(dt);
        });
    }
}
```

### Wiring it up in Game1

```csharp
// In LoadContent / Initialize:
var world = new World();

var trailEmitSys   = new TrailEmitSystem(world);
var trailRenderSys = new TrailRenderSystem(world, GraphicsDevice);
var ropePhysSys    = new RopePhysicsSystem(world);

// Create a trail entity:
var trailBuf = new TrailBuffer(128, lifetime: 0.5f);
world.Create(
    new Position2D(Vector2.Zero),
    new TrailEmitter(trailBuf, Width: 12f, Emitting: true,
        HeadColor: Color.White, TailColor: Color.Red,
        Texture: null, Blend: BlendState.NonPremultiplied)
);

// In Update:
trailEmitSys.Update(gameTime.TotalGameTime.TotalSeconds);
ropePhysSys.Update((float)gameTime.ElapsedGameTime.TotalSeconds);

// In Draw:
Matrix proj = Matrix.CreateOrthographicOffCenter(
    0, GraphicsDevice.Viewport.Width,
    GraphicsDevice.Viewport.Height, 0, 0, 1);
trailRenderSys.Draw(gameTime.TotalGameTime.TotalSeconds, proj);
```

---

## Quick Reference

| Technique | Key Class / Method | Blend Mode |
|---|---|---|
| Position trail | `TrailBuffer` + `TrailMeshBuilder` | NonPremultiplied |
| Fire / glow trail | Same + gradient texture | Additive |
| Thick line | `LineRenderer.DrawLine` | NonPremultiplied |
| Dashed line | `LineRenderer.DrawDashedLine` | NonPremultiplied |
| Laser beam | `LaserBeamRenderer.Draw` | Additive |
| Rope / chain | `RopeSimulation` + strip render | NonPremultiplied |
| Lightning bolt | `LightningGenerator` + line renderer | Additive |
| Ghost / after-image | `GhostTrail` + SpriteBatch | NonPremultiplied |

---

## Performance Notes

- **Reuse vertex arrays** — allocate once, fill each frame. Trails are inherently dynamic geometry; avoid allocations in the hot loop.
- **Ring buffers** beat `List<T>` for trails: no shifting, O(1) push/expire, cache-friendly.
- **Batch by blend state** — render all additive trails together, then all alpha trails, to minimise state changes.
- **Cap point count** — 64–128 points per trail is visually smooth at 60 fps. More than 256 is almost never needed.
- **Lightning regeneration** — regenerate every 3–4 frames, not every frame. The flicker looks intentional and halves the cost.

---

*Trail and line rendering is geometry generation at its simplest — perpendicular offsets, triangle strips, and alpha fading cover 90% of the effects you'll need. Master the trail buffer and strip builder, and everything else (lasers, ropes, lightning, ghosts) is a variation on the same theme.*
