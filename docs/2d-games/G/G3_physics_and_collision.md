# G3 — Physics & Collision
> **Category:** Guide · **Related:** [G1 Custom Code Recipes](./G1_custom_code_recipes.md) · [R2 Capability Matrix](../R/R2_capability_matrix.md)

> Comprehensive physics and collision guide for MonoGame 3.8.x + Arch ECS 2.1.x + Aether.Physics2D 2.2.0 + MonoGame.Extended 5.3.1.

---

## 1. Platformer Character Controller (Arch ECS)

### Components

```csharp
// Position and physics state
public record struct Position(float X, float Y);
public record struct Velocity(float X, float Y);
public record struct Gravity(float Force);  // typically 980f (pixels/s²)

// Character controller state
public record struct CharacterController(
    float MoveSpeed,       // 200f px/s
    float JumpVelocity,    // -350f px/s (negative = up)
    float CoyoteTime,      // 0.1f seconds
    float JumpBufferTime,  // 0.08f seconds
    float MaxFallSpeed,    // 600f px/s
    bool IsGrounded,
    bool WasGrounded,
    float CoyoteTimer,
    float JumpBufferTimer,
    float SlopeAngle       // current slope in radians
);

public record struct Collider(float Width, float Height, float OffsetX, float OffsetY);

// Tag components
public record struct OneWayPlatform;
public record struct MovingPlatform(Vector2 PreviousPosition);
```

### Ground Detection System

```csharp
public partial class GroundDetectionSystem : BaseSystem<World, float>
{
    private readonly QueryDescription _query = new QueryDescription()
        .WithAll<Position, Velocity, CharacterController, Collider>();

    public override void Update(in float dt)
    {
        World.Query(in _query, (ref Position pos, ref Velocity vel,
            ref CharacterController cc, ref Collider col) =>
        {
            cc.WasGrounded = cc.IsGrounded;
            cc.IsGrounded = false;

            // Cast 2px ray downward from bottom-center and both bottom corners
            float bottom = pos.Y + col.OffsetY + col.Height / 2f;
            float left   = pos.X + col.OffsetX - col.Width / 2f + 2f;
            float right  = pos.X + col.OffsetX + col.Width / 2f - 2f;
            float probeDepth = 2f; // pixels below feet

            // Check 3 points against tilemap/colliders
            for (int i = 0; i < 3; i++)
            {
                float px = i == 0 ? left : i == 1 ? right : pos.X + col.OffsetX;
                if (TileCollision.IsSolidAt(px, bottom + probeDepth))
                {
                    cc.IsGrounded = true;
                    break;
                }
            }

            // Coyote time management
            if (cc.IsGrounded)
                cc.CoyoteTimer = cc.CoyoteTime;
            else
                cc.CoyoteTimer -= dt;
        });
    }
}
```

### Slope Handling

```csharp
// Slope detection via two raycasts at feet edges
float leftY  = TileCollision.RaycastDown(left, bottom, 12f);
float rightY = TileCollision.RaycastDown(right, bottom, 12f);
cc.SlopeAngle = MathF.Atan2(rightY - leftY, right - left);

// Max walkable slope: ~46° (tan(46°) ≈ 1.03)
const float MaxSlopeAngle = 0.8f; // radians ≈ 45.8°

if (MathF.Abs(cc.SlopeAngle) <= MaxSlopeAngle && cc.IsGrounded)
{
    // Project horizontal velocity along slope surface
    Vector2 slopeNormal = new(-MathF.Sin(cc.SlopeAngle), MathF.Cos(cc.SlopeAngle));
    Vector2 slopeDir = new(slopeNormal.Y, -slopeNormal.X);
    float projectedSpeed = Vector2.Dot(new Vector2(vel.X, 0), slopeDir);
    vel = new Velocity(slopeDir.X * projectedSpeed, slopeDir.Y * projectedSpeed);
}
```

### One-Way Platforms

```csharp
// During collision resolution, skip one-way platforms when:
// 1. Player is moving upward (vel.Y < 0)
// 2. Player's feet are below platform top
bool IsOneWayPassable(float playerBottom, float platformTop, float velY)
{
    return velY < 0f || playerBottom > platformTop + 1f;
    // 1px tolerance prevents jitter at exact alignment
}

// Allow drop-through: set a DropThroughTimer component
// When active, skip all one-way platform collisions for ~0.2s
```

### Moving Platform Attachment

```csharp
public partial class MovingPlatformSystem : BaseSystem<World, float>
{
    // After moving platforms update their Position, compute delta
    // and apply it to any CharacterController standing on them
    public override void Update(in float dt)
    {
        World.Query(in _platformQuery, (Entity platform, ref Position platPos,
            ref MovingPlatform mp) =>
        {
            Vector2 delta = new(platPos.X - mp.PreviousPosition.X,
                                platPos.Y - mp.PreviousPosition.Y);
            mp = mp with { PreviousPosition = new Vector2(platPos.X, platPos.Y) };

            // Find riders: entities whose feet AABB overlaps platform top
            World.Query(in _riderQuery, (ref Position riderPos, ref Collider col) =>
            {
                riderPos = new Position(riderPos.X + delta.X, riderPos.Y + delta.Y);
            });
        });
    }
}
```

---

## 2. Collision Detection & Response

### AABB Overlap + MTV

```csharp
public static bool AABBOverlap(RectangleF a, RectangleF b, out Vector2 mtv)
{
    float overlapX = MathF.Min(a.Right, b.Right) - MathF.Max(a.Left, b.Left);
    float overlapY = MathF.Min(a.Bottom, b.Bottom) - MathF.Max(a.Top, b.Top);
    mtv = Vector2.Zero;

    if (overlapX <= 0 || overlapY <= 0) return false;

    // MTV = smallest penetration axis
    if (overlapX < overlapY)
        mtv = new Vector2(a.Center.X < b.Center.X ? -overlapX : overlapX, 0);
    else
        mtv = new Vector2(0, a.Center.Y < b.Center.Y ? -overlapY : overlapY);

    return true;
}
```

### Circle vs Circle

```csharp
public static bool CircleOverlap(Vector2 c1, float r1, Vector2 c2, float r2, out Vector2 mtv)
{
    Vector2 diff = c1 - c2;
    float distSq = diff.LengthSquared();
    float radiusSum = r1 + r2;
    mtv = Vector2.Zero;

    if (distSq >= radiusSum * radiusSum) return false;

    float dist = MathF.Sqrt(distSq);
    if (dist < 0.0001f)
    {
        mtv = new Vector2(radiusSum, 0); // degenerate: push right
        return true;
    }
    mtv = (diff / dist) * (radiusSum - dist);
    return true;
}
```

### SAT for Convex Polygons (MTV)

```csharp
public static bool SATOverlap(ReadOnlySpan<Vector2> polyA, ReadOnlySpan<Vector2> polyB,
    out Vector2 mtv)
{
    mtv = Vector2.Zero;
    float minOverlap = float.MaxValue;

    // Test axes from both polygons' edge normals
    for (int pass = 0; pass < 2; pass++)
    {
        var poly = pass == 0 ? polyA : polyB;
        for (int i = 0; i < poly.Length; i++)
        {
            Vector2 edge = poly[(i + 1) % poly.Length] - poly[i];
            Vector2 axis = new(-edge.Y, edge.X); // perpendicular
            float axisLen = axis.Length();
            if (axisLen < 0.0001f) continue;
            axis /= axisLen;

            Project(polyA, axis, out float minA, out float maxA);
            Project(polyB, axis, out float minB, out float maxB);

            float overlap = MathF.Min(maxA, maxB) - MathF.Max(minA, minB);
            if (overlap <= 0) return false;

            if (overlap < minOverlap)
            {
                minOverlap = overlap;
                mtv = axis * overlap;
            }
        }
    }

    // Ensure MTV points from B to A
    Vector2 centerDiff = Centroid(polyA) - Centroid(polyB);
    if (Vector2.Dot(mtv, centerDiff) < 0) mtv = -mtv;
    return true;
}

static void Project(ReadOnlySpan<Vector2> poly, Vector2 axis, out float min, out float max)
{
    min = max = Vector2.Dot(poly[0], axis);
    for (int i = 1; i < poly.Length; i++)
    {
        float d = Vector2.Dot(poly[i], axis);
        if (d < min) min = d;
        if (d > max) max = d;
    }
}
```

### Swept AABB (Continuous Collision Detection)

```csharp
/// <summary>
/// Returns time of impact [0..1] for a moving AABB against a static AABB.
/// Returns 1f if no collision. Normal is the collision surface normal.
/// </summary>
public static float SweptAABB(RectangleF moving, Vector2 velocity,
    RectangleF target, out Vector2 normal)
{
    normal = Vector2.Zero;

    // Broadphase: expand moving AABB by velocity
    float xInvEntry, yInvEntry, xInvExit, yInvExit;

    if (velocity.X > 0f)
    {
        xInvEntry = target.Left - moving.Right;
        xInvExit  = target.Right - moving.Left;
    }
    else
    {
        xInvEntry = target.Right - moving.Left;
        xInvExit  = target.Left - moving.Right;
    }

    if (velocity.Y > 0f)
    {
        yInvEntry = target.Top - moving.Bottom;
        yInvExit  = target.Bottom - moving.Top;
    }
    else
    {
        yInvEntry = target.Bottom - moving.Top;
        yInvExit  = target.Top - moving.Bottom;
    }

    float xEntry = velocity.X == 0f ? float.NegativeInfinity : xInvEntry / velocity.X;
    float yEntry = velocity.Y == 0f ? float.NegativeInfinity : yInvEntry / velocity.Y;
    float xExit  = velocity.X == 0f ? float.PositiveInfinity : xInvExit / velocity.X;
    float yExit  = velocity.Y == 0f ? float.PositiveInfinity : yInvExit / velocity.Y;

    float entryTime = MathF.Max(xEntry, yEntry);
    float exitTime  = MathF.Min(xExit, yExit);

    if (entryTime > exitTime || (xEntry < 0f && yEntry < 0f) || entryTime > 1f)
        return 1f;

    // Determine collision normal
    if (xEntry > yEntry)
        normal = new Vector2(xInvEntry < 0f ? 1f : -1f, 0f);
    else
        normal = new Vector2(0f, yInvEntry < 0f ? 1f : -1f);

    return entryTime;
}
```

---

## 3. Verlet Integration

### Core Verlet Particle

```csharp
public struct VerletPoint
{
    public Vector2 Position;
    public Vector2 OldPosition;
    public Vector2 Acceleration;
    public bool Pinned;
    public float Mass; // default 1f

    public void Update(float dt)
    {
        if (Pinned) return;
        Vector2 velocity = Position - OldPosition;
        OldPosition = Position;
        // Verlet: x(t+dt) = 2x(t) - x(t-dt) + a*dt²
        Position += velocity * 0.99f + Acceleration * (dt * dt); // 0.99 = damping
        Acceleration = Vector2.Zero;
    }

    public void ApplyForce(Vector2 force) => Acceleration += force / Mass;
}
```

### Distance Constraint (Ropes, Chains)

```csharp
public struct DistanceConstraint
{
    public int IndexA, IndexB;
    public float RestLength;
    public float Stiffness; // 0..1, use 1.0 for rigid

    public void Satisfy(Span<VerletPoint> points)
    {
        ref var a = ref points[IndexA];
        ref var b = ref points[IndexB];
        Vector2 diff = b.Position - a.Position;
        float dist = diff.Length();
        if (dist < 0.0001f) return;

        float error = (dist - RestLength) / dist;
        Vector2 correction = diff * error * 0.5f * Stiffness;

        if (!a.Pinned) a.Position += correction;
        if (!b.Pinned) b.Position -= correction;
    }
}
```

### Complete Rope/Chain System

```csharp
public class VerletRope
{
    public VerletPoint[] Points;
    public DistanceConstraint[] Constraints;
    public int ConstraintIterations = 8; // more = stiffer
    private readonly Vector2 _gravity = new(0, 980f);

    public VerletRope(Vector2 start, Vector2 end, int segments, float stiffness = 1f)
    {
        Points = new VerletPoint[segments + 1];
        Constraints = new DistanceConstraint[segments];
        float segLen = Vector2.Distance(start, end) / segments;

        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            Vector2 pos = Vector2.Lerp(start, end, t);
            Points[i] = new VerletPoint
            {
                Position = pos, OldPosition = pos,
                Mass = 1f, Pinned = (i == 0) // pin first point
            };
        }

        for (int i = 0; i < segments; i++)
            Constraints[i] = new DistanceConstraint
            { IndexA = i, IndexB = i + 1, RestLength = segLen, Stiffness = stiffness };
    }

    public void Update(float dt)
    {
        // Apply gravity + integrate
        for (int i = 0; i < Points.Length; i++)
        {
            Points[i].ApplyForce(_gravity);
            Points[i].Update(dt);
        }

        // Solve constraints multiple times for stability
        for (int iter = 0; iter < ConstraintIterations; iter++)
            for (int i = 0; i < Constraints.Length; i++)
                Constraints[i].Satisfy(Points);
    }
}
```

### 2D Cloth (Grid Mesh)

```csharp
public class VerletCloth
{
    public VerletPoint[] Points;
    public List<DistanceConstraint> Constraints = new();
    public int Width, Height;

    public VerletCloth(Vector2 origin, int w, int h, float spacing)
    {
        Width = w; Height = h;
        Points = new VerletPoint[w * h];

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int idx = y * w + x;
            Vector2 pos = origin + new Vector2(x * spacing, y * spacing);
            Points[idx] = new VerletPoint
            {
                Position = pos, OldPosition = pos, Mass = 1f,
                Pinned = (y == 0) // pin top row
            };

            // Horizontal constraint
            if (x > 0) Constraints.Add(new DistanceConstraint
                { IndexA = idx - 1, IndexB = idx, RestLength = spacing, Stiffness = 0.8f });
            // Vertical constraint
            if (y > 0) Constraints.Add(new DistanceConstraint
                { IndexA = idx - w, IndexB = idx, RestLength = spacing, Stiffness = 0.8f });
        }
    }

    // Tear cloth when stretch exceeds threshold
    public void TearCheck(float tearThreshold = 2.5f)
    {
        Constraints.RemoveAll(c =>
        {
            float dist = Vector2.Distance(Points[c.IndexA].Position, Points[c.IndexB].Position);
            return dist > c.RestLength * tearThreshold;
        });
    }
}
```

---

## 4. Aether.Physics2D v2.2.0

**NuGet packages:**
- `Aether.Physics2D` — standalone, no framework dependency
- `Aether.Physics2D.MG` — MonoGame-specific (uses `Microsoft.Xna.Framework.Vector2`)
- `Aether.Physics2D.Diagnostics.MG` — debug rendering

**Namespace (v2.0+):** `nkast.Aether.Physics2D` (changed from `tainicom.Aether.Physics2D`)

### World Setup

```csharp
using nkast.Aether.Physics2D.Dynamics;
using nkast.Aether.Physics2D.Common;

// Aether uses meters internally. Pick a pixel-to-meter ratio.
const float PixelsPerMeter = 64f;

World physicsWorld = new World(new Vector2(0, 9.8f)); // gravity in m/s²

// Create ground (static body)
Body ground = physicsWorld.CreateBody(new Vector2(0, 6f), 0f, BodyType.Static);
ground.CreateRectangle(20f, 0.5f, 1f, Vector2.Zero); // width, height in meters
ground.SetRestitution(0.3f);
ground.SetFriction(0.5f);

// Create dynamic box
Body box = physicsWorld.CreateBody(new Vector2(0, 0), 0f, BodyType.Dynamic);
box.CreateRectangle(1f, 1f, 1f, Vector2.Zero);
box.SetRestitution(0.2f);
box.Mass = 1f;
```

### Stepping (Fixed Timestep)

```csharp
// In Update():
physicsWorld.Step(1f / 60f); // fixed 60Hz step
// Or with sub-stepping:
physicsWorld.Step(dt, velocityIterations: 8, positionIterations: 3);
```

### Contact Listeners

```csharp
// Per-fixture callbacks (v2.2.0 pattern):
Fixture playerFixture = playerBody.FixtureList[0];

playerFixture.OnCollision += (fixtureA, fixtureB, contact) =>
{
    // contact.Manifold has collision points
    contact.GetWorldManifold(out Vector2 normal, out FixedArray2<Vector2> points);
    // normal.Y < -0.5f means landing on top
    return true; // return false to cancel collision
};

playerFixture.OnSeparation += (fixtureA, fixtureB, contact) =>
{
    // Objects stopped touching
};

// Pre-solve: modify contact before resolution
playerFixture.BeforeCollision += (fixtureA, fixtureB) =>
{
    // Return false to skip collision entirely (one-way platform logic)
    Body other = fixtureB.Body;
    if (other.Tag is "OneWayPlatform" && playerBody.LinearVelocity.Y < 0)
        return false;
    return true;
};
```

### Sensor Bodies (Triggers)

```csharp
Body sensorBody = physicsWorld.CreateBody(position, 0f, BodyType.Static);
Fixture sensorFixture = sensorBody.CreateCircle(2f, 0f); // radius, density
sensorFixture.IsSensor = true;

sensorFixture.OnCollision += (self, other, contact) =>
{
    // Triggered! other.Body entered the sensor area
    // No physical response occurs (it's a sensor)
    return true;
};
```

### Joint Examples

```csharp
// Revolute joint (hinge) — swinging door, flail weapon
var hinge = JointFactory.CreateRevoluteJoint(physicsWorld, bodyA, bodyB, Vector2.Zero);
hinge.LowerLimit = -MathHelper.PiOver4;
hinge.UpperLimit = MathHelper.PiOver4;
hinge.LimitEnabled = true;
hinge.MotorSpeed = 2f;       // radians/sec
hinge.MaxMotorTorque = 100f;
hinge.MotorEnabled = true;

// Distance joint (spring) — bungee, suspension
var spring = JointFactory.CreateDistanceJoint(physicsWorld, bodyA, bodyB);
spring.Length = 3f;        // rest length in meters
spring.Stiffness = 5f;    // spring constant
spring.Damping = 0.7f;

// Prismatic joint (slider) — elevator, piston
var slider = JointFactory.CreatePrismaticJoint(physicsWorld, bodyA, bodyB,
    Vector2.Zero, new Vector2(0, 1)); // axis = vertical
slider.LowerLimit = 0f;
slider.UpperLimit = 5f;
slider.LimitEnabled = true;
```

### Common Gotchas

1. **Units are meters, not pixels.** Divide pixel positions by `PixelsPerMeter` when setting body positions. Multiply back when rendering.
2. **Don't create/destroy bodies during Step().** Queue changes and apply them before/after stepping.
3. **Body.Tag** is `object` — use it to link back to your ECS entity: `body.Tag = entity;`
4. **Removed obsolete methods in v2.2.0:** `Body.SetRestitution(float)`, `Body.SetFriction(float)`, `Body.SetCollisionCategories()`, etc. Use fixture-level properties instead.
5. **Sleep management:** Bodies auto-sleep when stationary. Set `body.SleepingAllowed = false` for always-active bodies (player).
6. **Broadphase:** Default is DynamicTree. Switch to QuadTree for large open worlds with `Settings.AABBMultiplier`.

---

## 5. Tile-Based Collision

### Efficient Tilemap Collision

```csharp
public static class TileCollision
{
    public const int TileSize = 16;

    // Only check tiles near the entity — no spatial queries needed
    public static void ResolveCollisions(ref Position pos, ref Velocity vel,
        Collider col, TileMap map)
    {
        RectangleF bounds = new(
            pos.X + col.OffsetX - col.Width / 2f,
            pos.Y + col.OffsetY - col.Height / 2f,
            col.Width, col.Height);

        // Tile range overlapping entity bounds (clamp to map)
        int minTX = Math.Max(0, (int)(bounds.Left / TileSize));
        int maxTX = Math.Min(map.Width - 1, (int)(bounds.Right / TileSize));
        int minTY = Math.Max(0, (int)(bounds.Top / TileSize));
        int maxTY = Math.Min(map.Height - 1, (int)(bounds.Bottom / TileSize));

        // Resolve Y first (gravity), then X (movement) — order matters!
        for (int pass = 0; pass < 2; pass++)
        {
            // Recalculate bounds after each pass
            bounds = new RectangleF(
                pos.X + col.OffsetX - col.Width / 2f,
                pos.Y + col.OffsetY - col.Height / 2f,
                col.Width, col.Height);

            for (int ty = minTY; ty <= maxTY; ty++)
            for (int tx = minTX; tx <= maxTX; tx++)
            {
                if (!map.IsSolid(tx, ty)) continue;

                RectangleF tileBounds = new(tx * TileSize, ty * TileSize, TileSize, TileSize);
                if (!AABBOverlap(bounds, tileBounds, out Vector2 mtv)) continue;

                if (pass == 0) // Y resolution
                {
                    if (MathF.Abs(mtv.Y) > 0)
                    {
                        pos = new Position(pos.X, pos.Y + mtv.Y);
                        vel = new Velocity(vel.X, 0);
                    }
                }
                else // X resolution
                {
                    if (MathF.Abs(mtv.X) > 0)
                    {
                        pos = new Position(pos.X + mtv.X, pos.Y);
                        vel = new Velocity(0, vel.Y);
                    }
                }
            }
        }
    }
}
```

### Slope Tiles

```csharp
public enum SlopeType { None, Full, SlopeLeft45, SlopeRight45, SlopeLeft22Low, SlopeLeft22High }

// For 45° slopes: surface Y at local X position
public static float GetSlopeY(SlopeType type, float localX, int tileSize)
{
    return type switch
    {
        SlopeType.SlopeRight45 => tileSize - localX,              // ◣ rises left-to-right
        SlopeType.SlopeLeft45  => localX,                          // ◢ rises right-to-left
        SlopeType.SlopeLeft22Low  => localX * 0.5f,               // gentle slope, bottom half
        SlopeType.SlopeLeft22High => tileSize * 0.5f + localX * 0.5f, // gentle slope, top half
        _ => 0f
    };
}

// During collision: instead of full tile AABB, compute surface height
float localX = entityCenterX - (tx * TileSize);
float surfaceY = ty * TileSize + TileSize - GetSlopeY(slopeType, localX, TileSize);
if (entityBottom > surfaceY)
{
    pos = new Position(pos.X, surfaceY - col.Height / 2f - col.OffsetY);
    vel = new Velocity(vel.X, MathF.Min(vel.Y, 0)); // stop downward velocity
}
```

---

## 6. Physics Interpolation

### Fixed Timestep with Render Interpolation

```csharp
public class PhysicsInterpolation
{
    private const float FixedDt = 1f / 60f; // 60 Hz physics
    private float _accumulator;

    // Store previous + current state for interpolation
    public record struct PhysicsState(Position Previous, Position Current);

    public void Update(GameTime gameTime)
    {
        float frameDt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        frameDt = MathF.Min(frameDt, 0.25f); // clamp spiral of death
        _accumulator += frameDt;

        // Save previous positions before stepping
        World.Query(in _physicsQuery, (ref PhysicsState state, ref Position pos) =>
        {
            state = state with { Previous = pos };
        });

        while (_accumulator >= FixedDt)
        {
            StepPhysics(FixedDt); // your physics systems
            _accumulator -= FixedDt;
        }

        // Save current position
        World.Query(in _physicsQuery, (ref PhysicsState state, ref Position pos) =>
        {
            state = state with { Current = pos };
        });
    }

    // In Draw(): interpolate between Previous and Current
    public Vector2 GetRenderPosition(PhysicsState state)
    {
        float alpha = _accumulator / FixedDt;
        return new Vector2(
            MathHelper.Lerp(state.Previous.X, state.Current.X, alpha),
            MathHelper.Lerp(state.Previous.Y, state.Current.Y, alpha)
        );
    }
}
```

**Key values:**
- Physics rate: **60 Hz** (`FixedDt = 1/60`). Standard for platformers. Use 120 Hz for fast-paced bullet-hell.
- Accumulator clamp: **0.25s** max. Prevents >15 physics steps per frame (spiral of death).
- Alpha interpolation ensures smooth rendering at 144 Hz, 240 Hz, or any variable framerate.

### Arch ECS System Ordering

```csharp
// Recommended system execution order:
// 1. InputSystem            — read gamepad/keyboard
// 2. MovingPlatformSystem   — move platforms, apply delta to riders
// 3. GravitySystem          — apply gravity to velocity
// 4. CharacterMoveSystem    — apply input to velocity
// 5. PhysicsStepSystem      — integrate velocity → position
// 6. TileCollisionSystem    — resolve vs tilemap
// 7. EntityCollisionSystem  — entity-vs-entity
// 8. GroundDetectionSystem  — update grounded state
// 9. AnimationSystem        — pick animation from state
// 10. RenderSystem          — draw with interpolated positions
```

---

## 7. MonoGame.Extended v5.3.1 Collision

Current stable: **5.3.1** (NuGet `MonoGame.Extended`).

### Collision System Architecture

MonoGame.Extended provides `CollisionComponent` with layer-based spatial partitioning:

- **`ICollisionActor`** interface — implement `Bounds` (as `IShapeF`) and `OnCollision(CollisionEventArgs)`
- **Shape primitives:** `RectangleF`, `CircleF`, `SizeF`, `Point2`, `Vector2` extensions
- **Spatial algorithms:** `QuadTree` (default for "default" layer), `SpatialHash` (configurable per layer)
- **Layers:** Entities in the same custom layer don't collide with each other; only checked against "default" layer
- **`CollisionEventArgs.PenetrationVector`** — MTV for push-back resolution

### What's Evolving for MonoGame 3.8.6

MonoGame.Extended is maintained by **AristurtleDev** (Christopher Whitley) and the community under the `MonoGame-Extended` GitHub org. Key developments:

- **Documentation rewrite** for v5 is in progress (monogameextended.net)
- **MonoGame 3.8.6** targets .NET 8+ and NativeAOT — Extended is aligning with trimmable/AOT-safe patterns
- **Collision v5** simplified the API: removed some older collision resolution methods, streamlined `IShapeF`
- **Tiled map loader** updated — better integration with modern Tiled formats
- Extended does **not** include physics simulation — it's collision detection only. Pair with Aether.Physics2D or roll your own for full physics.

### Using Extended Collision with Arch ECS

```csharp
// Bridge pattern: wrap Arch entity in ICollisionActor
public class ArchCollisionActor : ICollisionActor
{
    public Entity Entity { get; }
    public IShapeF Bounds { get; set; }
    public string LayerName { get; set; } = "default";

    public ArchCollisionActor(Entity entity, RectangleF bounds)
    {
        Entity = entity;
        Bounds = bounds;
    }

    public void OnCollision(CollisionEventArgs args)
    {
        // Read back into ECS: push penetration vector to a CollisionResult component
        ref var pos = ref Entity.Get<Position>();
        pos = new Position(pos.X - args.PenetrationVector.X,
                          pos.Y - args.PenetrationVector.Y);
    }
}
```

---

## 8. Fixed-Point Math for Deterministic Physics

### Why Fixed-Point?

IEEE 754 `float` produces different results across architectures (x86 vs ARM), compiler settings, and even instruction ordering. For **rollback netcode** and **replay systems**, physics must be bit-identical. Fixed-point guarantees this.

### Q16.16 Implementation

```csharp
/// <summary>
/// 32-bit fixed-point number with 16 integer bits and 16 fractional bits.
/// Range: -32768.0 to 32767.99998 with precision of ~0.000015.
/// </summary>
public readonly struct Fixed32 : IEquatable<Fixed32>, IComparable<Fixed32>
{
    public const int FractionalBits = 16;
    public const int Scale = 1 << FractionalBits; // 65536
    public readonly int RawValue;

    private Fixed32(int raw) => RawValue = raw;

    // Conversions
    public static Fixed32 FromInt(int v) => new(v << FractionalBits);
    public static Fixed32 FromFloat(float v) => new((int)(v * Scale));
    public float ToFloat() => (float)RawValue / Scale;

    // Arithmetic — all deterministic, no floats
    public static Fixed32 operator +(Fixed32 a, Fixed32 b) => new(a.RawValue + b.RawValue);
    public static Fixed32 operator -(Fixed32 a, Fixed32 b) => new(a.RawValue - b.RawValue);
    public static Fixed32 operator *(Fixed32 a, Fixed32 b) =>
        new((int)(((long)a.RawValue * b.RawValue) >> FractionalBits));
    public static Fixed32 operator /(Fixed32 a, Fixed32 b) =>
        new((int)(((long)a.RawValue << FractionalBits) / b.RawValue));

    // Comparison
    public static bool operator <(Fixed32 a, Fixed32 b) => a.RawValue < b.RawValue;
    public static bool operator >(Fixed32 a, Fixed32 b) => a.RawValue > b.RawValue;
    public bool Equals(Fixed32 other) => RawValue == other.RawValue;
    public int CompareTo(Fixed32 other) => RawValue.CompareTo(other.RawValue);

    // Sqrt via Newton's method (fully deterministic)
    public static Fixed32 Sqrt(Fixed32 v)
    {
        if (v.RawValue <= 0) return new(0);
        long val = (long)v.RawValue << FractionalBits;
        long guess = val >> 1;
        for (int i = 0; i < 16; i++) // 16 iterations = full precision
            guess = (guess + val / guess) >> 1;
        return new((int)guess);
    }

    public override string ToString() => ToFloat().ToString("F4");
}
```

### Fixed-Point Vector2

```csharp
public struct FixedVec2
{
    public Fixed32 X, Y;

    public FixedVec2(Fixed32 x, Fixed32 y) { X = x; Y = y; }

    public static FixedVec2 operator +(FixedVec2 a, FixedVec2 b) =>
        new(a.X + b.X, a.Y + b.Y);
    public static FixedVec2 operator *(FixedVec2 v, Fixed32 s) =>
        new(v.X * s, v.Y * s);

    public Fixed32 LengthSquared() => X * X + Y * Y;
    public Fixed32 Length() => Fixed32.Sqrt(LengthSquared());

    public FixedVec2 Normalized()
    {
        Fixed32 len = Length();
        if (len.RawValue == 0) return new(Fixed32.FromInt(0), Fixed32.FromInt(0));
        return new(X / len, Y / len);
    }
}
```

### Libraries to Consider

- **[FixedMath.Net](https://github.com/asik/FixedMath.Net)** — Q31.32 (64-bit), battle-tested, includes sin/cos/atan2 lookup tables
- **Roll your own Q16.16** (above) for simpler 2D needs — less precision but faster multiply
- For Arch ECS: use `FixedVec2` in your Position/Velocity components; convert to `float` only at render time

### Deterministic Physics Checklist

1. **No `float`/`double` in simulation** — fixed-point only
2. **Fixed iteration order** — sort entities by ID before processing
3. **No `Dictionary` iteration** (non-deterministic order) — use sorted collections
4. **Same timestep** — never use variable `dt` in simulation
5. **Serialize state as raw ints** — checksums for desync detection
6. **Platform-independent RNG** — seed-based, integer-only PRNG

---

## Quick Reference: Typical Platformer Values

| Parameter | Value | Notes |
|---|---|---|
| Gravity | 980 px/s² | ~2× real gravity, feels snappy |
| Jump velocity | -350 px/s | Negative = upward |
| Move speed | 200 px/s | Ground movement |
| Max fall speed | 600 px/s | Terminal velocity cap |
| Coyote time | 0.08–0.12s | Grace period after leaving edge |
| Jump buffer | 0.06–0.10s | Pre-land jump input window |
| Tile size | 16 or 32 px | Common for pixel art |
| Physics rate | 60 Hz | Fixed timestep |
| Constraint iterations | 4–8 | Verlet solver passes |
| Aether px/meter | 64 | Conversion ratio |
