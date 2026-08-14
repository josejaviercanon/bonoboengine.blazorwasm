# G3 — Physics & Collision

> **Category:** Guide · **Engine:** MonoGame · **Tier:** Free (§1–§5), Pro (§6–§13)
>
> **Related:** [G1 Custom Code Recipes](./G1_custom_code_recipes.md) · [G52 Character Controller](./G52_character_controller.md) · [G37 Tilemap Systems](./G37_tilemap_systems.md) · [G64 Combat & Damage](./G64_combat_damage_systems.md) · [G67 Object Pooling](./G67_object_pooling.md) · [G13 C# Performance](./G13_csharp_performance.md) · [G33 Profiling & Optimization](./G33_profiling_optimization.md) · [R2 Capability Matrix](../reference/R2_capability_matrix.md) · [Physics Theory](../../core/concepts/physics-theory.md)

> Comprehensive physics and collision guide for MonoGame 3.8.x + Arch ECS 2.1.x + Aether.Physics2D 2.2.0 + MonoGame.Extended 5.3.1. Covers collision primitives, spatial partitioning, character controllers, Verlet soft-body, fixed-point determinism, and production patterns.

---

## Physics Pipeline Overview

Understanding the execution order prevents the most common physics bugs (jitter, tunneling, one-frame-late detection):

```
┌─────────────────────────────────────────────────────────────┐
│                    GAME LOOP (per frame)                    │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  1. INPUT           Read gamepad / keyboard / touch         │
│       ↓                                                     │
│  2. MOVING PLATS    Move platforms, apply delta to riders   │
│       ↓                                                     │
│  3. GRAVITY         Apply gravity to velocity               │
│       ↓                                                     │
│  4. CHARACTER MOVE  Apply input → velocity (accel/friction) │
│       ↓                                                     │
│  ┌──────────────── FIXED TIMESTEP LOOP ─────────────────┐   │
│  │  5. INTEGRATE    velocity × dt → position            │   │
│  │       ↓                                              │   │
│  │  6. BROAD PHASE  Spatial hash / grid → candidate     │   │
│  │       ↓            pairs                             │   │
│  │  7. NARROW PHASE AABB / SAT / circle tests →         │   │
│  │       ↓            contacts                          │   │
│  │  8. RESOLVE      Push out via MTV, zero velocity     │   │
│  │       ↓            on collision axis                  │   │
│  │  9. TRIGGERS     Process sensor / area overlaps      │   │
│  └──────────────────────────────────────────────────────┘   │
│       ↓                                                     │
│  10. GROUND DETECT  Raycast feet, update grounded state     │
│       ↓                                                     │
│  11. ANIMATION      Pick animation from physics state       │
│       ↓                                                     │
│  12. RENDER         Draw with interpolated positions        │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

**Critical ordering rules:**
- Gravity BEFORE character movement — otherwise jumps feel floaty on the first frame
- Integrate BEFORE collision — resolve pushes objects out of overlap
- Y resolution BEFORE X resolution — prevents corner-catching on tilemap edges
- Ground detection AFTER all collision — ensures accurate `IsGrounded` state
- Render uses interpolated positions — smooth at any framerate (see §6)

---

## 1. Collision Primitives & Detection

### AABB Overlap + MTV (Minimum Translation Vector)

The workhorse of 2D collision. Two axis-aligned rectangles overlap when they overlap on BOTH axes. The MTV is the smallest push needed to separate them.

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

**When to use:** Platformers, tile collision, most entity-vs-entity checks. Fast (4 comparisons + 2 subtractions), no square root.

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

**When to use:** Bullet/projectile collision, explosion radius, round enemies/pickups. Natural for top-down games.

### Circle vs AABB

A common need (circular player hitbox vs rectangular walls):

```csharp
public static bool CircleAABBOverlap(Vector2 center, float radius,
    RectangleF rect, out Vector2 mtv)
{
    // Find closest point on AABB to circle center
    float closestX = MathHelper.Clamp(center.X, rect.Left, rect.Right);
    float closestY = MathHelper.Clamp(center.Y, rect.Top, rect.Bottom);

    Vector2 diff = center - new Vector2(closestX, closestY);
    float distSq = diff.LengthSquared();
    mtv = Vector2.Zero;

    if (distSq >= radius * radius) return false;

    float dist = MathF.Sqrt(distSq);
    if (dist < 0.0001f)
    {
        // Circle center is inside AABB — find shortest exit
        float exitRight = rect.Right - center.X + radius;
        float exitLeft = center.X - rect.Left + radius;
        float exitDown = rect.Bottom - center.Y + radius;
        float exitUp = center.Y - rect.Top + radius;
        float minExit = MathF.Min(MathF.Min(exitRight, exitLeft),
                                   MathF.Min(exitDown, exitUp));
        if (minExit == exitRight)      mtv = new Vector2(exitRight, 0);
        else if (minExit == exitLeft)  mtv = new Vector2(-exitLeft, 0);
        else if (minExit == exitDown)  mtv = new Vector2(0, exitDown);
        else                           mtv = new Vector2(0, -exitUp);
        return true;
    }

    mtv = (diff / dist) * (radius - dist);
    return true;
}
```

### SAT for Convex Polygons (MTV)

Separating Axis Theorem works for ANY convex shapes. More expensive than AABB/circle but handles rotated colliders.

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
            if (overlap <= 0) return false; // separating axis found

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

static Vector2 Centroid(ReadOnlySpan<Vector2> poly)
{
    Vector2 sum = Vector2.Zero;
    for (int i = 0; i < poly.Length; i++) sum += poly[i];
    return sum / poly.Length;
}
```

**Performance cost comparison:**

| Primitive | Comparisons | Sqrt? | Best For |
|---|---|---|---|
| AABB vs AABB | 4 | No | Tiles, most entities |
| Circle vs Circle | 1 multiply | Yes (on hit) | Bullets, round entities |
| Circle vs AABB | 2 clamp + 1 | Yes (on hit) | Player vs walls |
| SAT (n-gon) | 2n projections | No | Rotated shapes |

### Choosing the Right Collider Shape

```
Does the entity rotate?
├── No → Use AABB
│     Is it roughly square/rectangular?
│     ├── Yes → AABB (fastest)
│     └── No, it's round → Circle
└── Yes → 
      Is it roughly circular?
      ├── Yes → Circle (rotation-invariant)
      └── No → SAT convex polygon
            Does it need concave shapes?
            └── Yes → Decompose into convex hulls
```

---

## 2. Broad Phase: Spatial Partitioning

Narrow-phase tests (AABB, circle, SAT) are cheap individually but O(n²) pairwise. Broad phase reduces candidates to only nearby pairs.

### Spatial Hash Grid

Best for entities of similar size. O(1) insertion and lookup.

```csharp
public class SpatialHash<T>
{
    private readonly Dictionary<long, List<(T Item, RectangleF Bounds)>> _cells = new();
    private readonly int _cellSize;

    public SpatialHash(int cellSize = 64) => _cellSize = cellSize;

    public void Clear() => _cells.Clear();

    /// <summary>
    /// Insert an item. Call once per frame after position updates.
    /// Items spanning multiple cells are inserted into each.
    /// </summary>
    public void Insert(T item, RectangleF bounds)
    {
        int minX = (int)MathF.Floor(bounds.Left / _cellSize);
        int maxX = (int)MathF.Floor(bounds.Right / _cellSize);
        int minY = (int)MathF.Floor(bounds.Top / _cellSize);
        int maxY = (int)MathF.Floor(bounds.Bottom / _cellSize);

        for (int y = minY; y <= maxY; y++)
        for (int x = minX; x <= maxX; x++)
        {
            long key = ((long)x << 32) | (uint)y;
            if (!_cells.TryGetValue(key, out var list))
            {
                list = new List<(T, RectangleF)>(4);
                _cells[key] = list;
            }
            list.Add((item, bounds));
        }
    }

    /// <summary>
    /// Query all items whose cells overlap the given bounds.
    /// Returns candidates only — caller must do narrow-phase test.
    /// </summary>
    public void Query(RectangleF bounds, List<(T Item, RectangleF Bounds)> results)
    {
        results.Clear();
        int minX = (int)MathF.Floor(bounds.Left / _cellSize);
        int maxX = (int)MathF.Floor(bounds.Right / _cellSize);
        int minY = (int)MathF.Floor(bounds.Top / _cellSize);
        int maxY = (int)MathF.Floor(bounds.Bottom / _cellSize);

        HashSet<T>? seen = null; // avoid duplicates for multi-cell items

        for (int y = minY; y <= maxY; y++)
        for (int x = minX; x <= maxX; x++)
        {
            long key = ((long)x << 32) | (uint)y;
            if (!_cells.TryGetValue(key, out var list)) continue;

            foreach (var entry in list)
            {
                seen ??= new HashSet<T>();
                if (seen.Add(entry.Item))
                    results.Add(entry);
            }
        }
    }
}
```

**Cell size rule of thumb:** 2× the average entity size. Too small = entities span many cells (overhead). Too large = too many candidates per cell (defeats purpose).

### Uniform Grid (For Tilemaps)

When entities only collide with tiles (not each other), skip spatial hashing entirely — the tilemap IS the spatial structure:

```csharp
// Only check tiles near the entity — O(1) per entity
int minTX = Math.Max(0, (int)(bounds.Left / TileSize));
int maxTX = Math.Min(map.Width - 1, (int)(bounds.Right / TileSize));
int minTY = Math.Max(0, (int)(bounds.Top / TileSize));
int maxTY = Math.Min(map.Height - 1, (int)(bounds.Bottom / TileSize));
// Check only the ~4-9 tiles the entity overlaps
```

### Quadtree

Better than spatial hash when entity sizes vary dramatically (e.g., tiny bullets + huge bosses):

```csharp
public class Quadtree<T>
{
    private const int MaxItems = 8;
    private const int MaxDepth = 6;

    private readonly RectangleF _bounds;
    private readonly int _depth;
    private List<(T Item, RectangleF Bounds)>? _items;
    private Quadtree<T>?[]? _children;

    public Quadtree(RectangleF bounds, int depth = 0)
    {
        _bounds = bounds;
        _depth = depth;
    }

    public void Clear()
    {
        _items?.Clear();
        if (_children != null)
            for (int i = 0; i < 4; i++)
                _children[i]?.Clear();
        _children = null;
    }

    public void Insert(T item, RectangleF bounds)
    {
        if (!_bounds.Intersects(bounds)) return;

        if (_children != null)
        {
            for (int i = 0; i < 4; i++)
                _children[i]?.Insert(item, bounds);
            return;
        }

        _items ??= new List<(T, RectangleF)>(MaxItems);
        _items.Add((item, bounds));

        if (_items.Count > MaxItems && _depth < MaxDepth)
            Subdivide();
    }

    public void Query(RectangleF area, List<(T Item, RectangleF Bounds)> results)
    {
        if (!_bounds.Intersects(area)) return;

        if (_items != null)
            foreach (var entry in _items)
                if (entry.Bounds.Intersects(area))
                    results.Add(entry);

        if (_children != null)
            for (int i = 0; i < 4; i++)
                _children[i]?.Query(area, results);
    }

    private void Subdivide()
    {
        float hw = _bounds.Width / 2, hh = _bounds.Height / 2;
        _children = new Quadtree<T>[4];
        _children[0] = new(new(_bounds.X, _bounds.Y, hw, hh), _depth + 1);
        _children[1] = new(new(_bounds.X + hw, _bounds.Y, hw, hh), _depth + 1);
        _children[2] = new(new(_bounds.X, _bounds.Y + hh, hw, hh), _depth + 1);
        _children[3] = new(new(_bounds.X + hw, _bounds.Y + hh, hw, hh), _depth + 1);

        foreach (var item in _items!)
            for (int i = 0; i < 4; i++)
                _children[i]!.Insert(item.Item, item.Bounds);
        _items.Clear();
    }
}
```

### When to Use Which

| Structure | Best For | Entity Count | Size Variance |
|---|---|---|---|
| No broad phase | < 20 entities | Any | Any |
| Tile grid lookup | Entity vs tilemap | Any | N/A |
| Spatial hash | 50–5,000 similar-sized | High | Low |
| Quadtree | 50–5,000 varied-sized | Medium | High |
| Aether.Physics2D | Need joints/forces | Any | Any |

---

## 3. Raycasting

Raycasts answer "does a line hit something?" — used for ground detection, line-of-sight, bullet traces, and laser beams.

### Tilemap Raycasting (DDA Algorithm)

Digital Differential Analyzer steps through tiles along a ray, one tile at a time. Guaranteed to check every tile the ray passes through with no gaps.

```csharp
public static class TileRaycast
{
    /// <summary>
    /// Cast a ray through a tilemap. Returns true if a solid tile is hit.
    /// Uses DDA (Digital Differential Analyzer) for gap-free traversal.
    /// </summary>
    public static bool Cast(TileMap map, Vector2 origin, Vector2 direction,
        float maxDistance, out Vector2 hitPoint, out Vector2 hitNormal)
    {
        hitPoint = origin + direction * maxDistance;
        hitNormal = Vector2.Zero;

        if (direction.LengthSquared() < 0.0001f) return false;
        direction = Vector2.Normalize(direction);

        int tileSize = TileCollision.TileSize;
        int tileX = (int)MathF.Floor(origin.X / tileSize);
        int tileY = (int)MathF.Floor(origin.Y / tileSize);

        // Step direction (+1 or -1)
        int stepX = direction.X >= 0 ? 1 : -1;
        int stepY = direction.Y >= 0 ? 1 : -1;

        // Distance along ray to cross one full tile
        float tDeltaX = direction.X == 0 ? float.MaxValue : MathF.Abs(tileSize / direction.X);
        float tDeltaY = direction.Y == 0 ? float.MaxValue : MathF.Abs(tileSize / direction.Y);

        // Distance to next tile boundary
        float tMaxX = direction.X == 0 ? float.MaxValue :
            ((stepX > 0 ? (tileX + 1) * tileSize - origin.X : origin.X - tileX * tileSize)
             / MathF.Abs(direction.X));
        float tMaxY = direction.Y == 0 ? float.MaxValue :
            ((stepY > 0 ? (tileY + 1) * tileSize - origin.Y : origin.Y - tileY * tileSize)
             / MathF.Abs(direction.Y));

        float t = 0f;

        while (t < maxDistance)
        {
            if (tileX >= 0 && tileX < map.Width && tileY >= 0 && tileY < map.Height)
            {
                if (map.IsSolid(tileX, tileY))
                {
                    hitPoint = origin + direction * t;
                    // Normal points back toward ray origin
                    hitNormal = tMaxX < tMaxY
                        ? new Vector2(-stepX, 0)
                        : new Vector2(0, -stepY);
                    return true;
                }
            }

            // Step to next tile boundary
            if (tMaxX < tMaxY)
            {
                t = tMaxX;
                tMaxX += tDeltaX;
                tileX += stepX;
            }
            else
            {
                t = tMaxY;
                tMaxY += tDeltaY;
                tileY += stepY;
            }
        }

        return false;
    }

    /// <summary>
    /// Convenience: cast straight down from a point.
    /// Returns the Y coordinate of the first solid tile surface hit.
    /// </summary>
    public static float RaycastDown(TileMap map, float x, float startY, float maxDist)
    {
        if (Cast(map, new Vector2(x, startY), Vector2.UnitY, maxDist,
            out Vector2 hit, out _))
            return hit.Y;
        return startY + maxDist;
    }
}
```

### Entity Raycasting (Against Spatial Hash)

```csharp
/// <summary>
/// Cast a ray against all entities in a spatial hash.
/// Returns the closest hit within maxDistance.
/// </summary>
public static bool RaycastEntities<T>(SpatialHash<T> hash,
    Vector2 origin, Vector2 direction, float maxDistance,
    Func<T, RectangleF> getBounds,
    out T hitEntity, out float hitDistance, out Vector2 hitNormal,
    Func<T, bool>? filter = null)
{
    hitEntity = default!;
    hitDistance = maxDistance;
    hitNormal = Vector2.Zero;

    // Expand query area along ray path
    Vector2 endPoint = origin + direction * maxDistance;
    RectangleF queryArea = new(
        MathF.Min(origin.X, endPoint.X) - 1,
        MathF.Min(origin.Y, endPoint.Y) - 1,
        MathF.Abs(direction.X * maxDistance) + 2,
        MathF.Abs(direction.Y * maxDistance) + 2);

    var candidates = new List<(T Item, RectangleF Bounds)>();
    hash.Query(queryArea, candidates);

    bool found = false;
    foreach (var (item, bounds) in candidates)
    {
        if (filter != null && !filter(item)) continue;

        if (RayAABBIntersect(origin, direction, bounds, out float t, out Vector2 n)
            && t >= 0 && t < hitDistance)
        {
            hitEntity = item;
            hitDistance = t;
            hitNormal = n;
            found = true;
        }
    }

    return found;
}

/// <summary>
/// Ray vs AABB intersection test. Returns parameter t (distance along ray).
/// </summary>
public static bool RayAABBIntersect(Vector2 origin, Vector2 dir,
    RectangleF rect, out float tHit, out Vector2 normal)
{
    tHit = 0;
    normal = Vector2.Zero;

    float tMin = float.NegativeInfinity;
    float tMax = float.PositiveInfinity;
    Vector2 tMinNormal = Vector2.Zero;

    // X slab
    if (MathF.Abs(dir.X) > 0.0001f)
    {
        float t1 = (rect.Left - origin.X) / dir.X;
        float t2 = (rect.Right - origin.X) / dir.X;
        Vector2 n1 = new(-1, 0), n2 = new(1, 0);
        if (t1 > t2) { (t1, t2) = (t2, t1); (n1, n2) = (n2, n1); }
        if (t1 > tMin) { tMin = t1; tMinNormal = n1; }
        if (t2 < tMax) tMax = t2;
    }
    else if (origin.X < rect.Left || origin.X > rect.Right) return false;

    // Y slab
    if (MathF.Abs(dir.Y) > 0.0001f)
    {
        float t1 = (rect.Top - origin.Y) / dir.Y;
        float t2 = (rect.Bottom - origin.Y) / dir.Y;
        Vector2 n1 = new(0, -1), n2 = new(0, 1);
        if (t1 > t2) { (t1, t2) = (t2, t1); (n1, n2) = (n2, n1); }
        if (t1 > tMin) { tMin = t1; tMinNormal = n1; }
        if (t2 < tMax) tMax = t2;
    }
    else if (origin.Y < rect.Top || origin.Y > rect.Bottom) return false;

    if (tMin > tMax || tMax < 0) return false;

    tHit = tMin >= 0 ? tMin : tMax;
    normal = tMinNormal;
    return true;
}
```

### Common Raycast Use Cases

```csharp
// 1. Ground detection — 3-point feet probe
bool isGrounded = false;
float[] probeX = { left + 2, center, right - 2 };
for (int i = 0; i < 3; i++)
{
    if (TileRaycast.Cast(map, new Vector2(probeX[i], feetY),
        Vector2.UnitY, 2f, out _, out _))
    {
        isGrounded = true;
        break;
    }
}

// 2. Line of sight — enemy can see player?
bool canSee = !TileRaycast.Cast(map,
    enemyPos, Vector2.Normalize(playerPos - enemyPos),
    Vector2.Distance(enemyPos, playerPos), out _, out _);

// 3. Hitscan weapon — instant bullet trace
if (RaycastEntities(hash, gunTip, aimDir, 800f, e => e.Bounds,
    out Entity hitTarget, out float dist, out Vector2 hitNormal,
    e => e.Has<Health>())) // only hit damageable entities
{
    hitTarget.Get<Health>().Current -= 25;
    SpawnSparks(gunTip + aimDir * dist, hitNormal);
}

// 4. Laser beam — multi-hit raycast (pierce through enemies)
Vector2 beamOrigin = laserPos;
float remaining = maxBeamLength;
while (remaining > 0 && RaycastEntities(hash, beamOrigin, beamDir, remaining,
    e => e.Bounds, out Entity hit, out float d, out _, e => e.Has<Health>()))
{
    hit.Get<Health>().Current -= 10;
    beamOrigin += beamDir * (d + 0.1f); // step past hit
    remaining -= d;
}
```

---

## 4. Platformer Character Controller (Arch ECS)

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

### Jump System with Buffering

Coyote time and jump buffering are the two mechanics that separate "feels janky" from "feels great":

```csharp
public partial class JumpSystem : BaseSystem<World, float>
{
    public override void Update(in float dt)
    {
        World.Query(in _query, (ref Velocity vel, ref CharacterController cc) =>
        {
            // Buffer jump input
            if (InputManager.JumpPressed)
                cc.JumpBufferTimer = cc.JumpBufferTime;
            else
                cc.JumpBufferTimer -= dt;

            // Can jump: either grounded OR within coyote time
            bool canJump = cc.IsGrounded || cc.CoyoteTimer > 0f;

            // Execute jump if buffered AND can jump
            if (cc.JumpBufferTimer > 0f && canJump)
            {
                vel = new Velocity(vel.X, cc.JumpVelocity);
                cc.JumpBufferTimer = 0f;
                cc.CoyoteTimer = 0f; // consume coyote time
            }

            // Variable jump height: release button = cut upward velocity
            if (InputManager.JumpReleased && vel.Y < 0)
                vel = new Velocity(vel.X, vel.Y * 0.5f);

            // Terminal velocity
            if (vel.Y > cc.MaxFallSpeed)
                vel = new Velocity(vel.X, cc.MaxFallSpeed);
        });
    }
}
```

### Slope Handling

```csharp
// Slope detection via two raycasts at feet edges
float leftY  = TileRaycast.RaycastDown(map, left, bottom, 12f);
float rightY = TileRaycast.RaycastDown(map, right, bottom, 12f);
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
public record struct DropThrough(float Timer); // ~0.2s duration

// In collision system:
if (entity.Has<DropThrough>() && tile.Has<OneWayPlatform>())
    continue; // skip collision entirely while drop-through active

// In input system:
if (InputManager.DownPressed && InputManager.JumpPressed && cc.IsGrounded)
    entity.Add(new DropThrough(0.2f));
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
            World.Query(in _riderQuery, (ref Position riderPos, ref Collider col,
                ref CharacterController cc) =>
            {
                if (!cc.IsGrounded) return;

                // Check if rider is above this platform
                float riderBottom = riderPos.Y + col.OffsetY + col.Height / 2f;
                RectangleF platBounds = GetBounds(platPos, platform.Get<Collider>());

                if (riderBottom >= platBounds.Top - 2f &&
                    riderBottom <= platBounds.Top + 4f &&
                    riderPos.X >= platBounds.Left &&
                    riderPos.X <= platBounds.Right)
                {
                    riderPos = new Position(riderPos.X + delta.X, riderPos.Y + delta.Y);
                }
            });
        });
    }
}
```

---

## 5. Tile-Based Collision

### Efficient Tilemap Collision

```csharp
public static class TileCollision
{
    public const int TileSize = 16;

    /// <summary>
    /// Resolve entity position against solid tiles.
    /// Resolves Y first (gravity), then X (movement) — order prevents corner-catching.
    /// </summary>
    public static void ResolveCollisions(ref Position pos, ref Velocity vel,
        Collider col, TileMap map)
    {
        // Resolve Y first, then X (two-pass prevents corner-catching)
        for (int pass = 0; pass < 2; pass++)
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

            for (int ty = minTY; ty <= maxTY; ty++)
            for (int tx = minTX; tx <= maxTX; tx++)
            {
                if (!map.IsSolid(tx, ty)) continue;

                RectangleF tileBounds = new(tx * TileSize, ty * TileSize,
                    TileSize, TileSize);
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

    /// <summary>Check if a world-space pixel coordinate is inside a solid tile.</summary>
    public static bool IsSolidAt(float worldX, float worldY)
    {
        int tx = (int)(worldX / TileSize);
        int ty = (int)(worldY / TileSize);
        return CurrentMap.InBounds(tx, ty) && CurrentMap.IsSolid(tx, ty);
    }
}
```

### Slope Tiles

```csharp
public enum SlopeType
{
    None, Full,
    SlopeRight45,     // ◣ rises left-to-right
    SlopeLeft45,      // ◢ rises right-to-left
    SlopeRight22Low,  // gentle right, bottom half
    SlopeRight22High, // gentle right, top half
    SlopeLeft22Low,   // gentle left, bottom half
    SlopeLeft22High   // gentle left, top half
}

/// <summary>
/// For slope tiles, compute the surface Y at a given local X position.
/// Returns height from bottom of tile (0 = floor, tileSize = ceiling).
/// </summary>
public static float GetSlopeY(SlopeType type, float localX, int tileSize)
{
    return type switch
    {
        SlopeType.SlopeRight45    => tileSize - localX,
        SlopeType.SlopeLeft45     => localX,
        SlopeType.SlopeRight22Low => (tileSize - localX) * 0.5f,
        SlopeType.SlopeRight22High=> tileSize * 0.5f + (tileSize - localX) * 0.5f,
        SlopeType.SlopeLeft22Low  => localX * 0.5f,
        SlopeType.SlopeLeft22High => tileSize * 0.5f + localX * 0.5f,
        _ => 0f
    };
}

// During collision: compute surface height instead of full tile AABB
float localX = entityCenterX - (tx * TileSize);
float surfaceWorldY = ty * TileSize + TileSize - GetSlopeY(slopeType, localX, TileSize);
if (entityBottom > surfaceWorldY)
{
    pos = new Position(pos.X, surfaceWorldY - col.Height / 2f - col.OffsetY);
    vel = new Velocity(vel.X, MathF.Min(vel.Y, 0)); // stop downward velocity only
    cc.IsGrounded = true;
}
```

**Slope implementation tips:**
- **Snapping on descent:** Without special handling, characters "hop" when running downhill. Snap the character to the slope surface when `WasGrounded && !IsGrounded && vel.Y >= 0`.
- **Slope speed adjustment:** Optionally slow characters going uphill and accelerate downhill: `moveSpeed *= 1f - slopeAngle * 0.3f`
- **22° vs 45°:** Use 22° (two tiles per rise) for smooth natural terrain. Use 45° sparingly — they feel steep in gameplay even when they look fine in art.

---

## 6. Continuous Collision Detection (CCD)

Fast-moving objects (bullets, dashing characters) can tunnel through thin walls when their per-frame displacement exceeds the wall thickness. CCD solves this.

### Swept AABB

```csharp
/// <summary>
/// Returns time of impact [0..1] for a moving AABB against a static AABB.
/// Returns 1f if no collision. Normal is the collision surface normal.
/// </summary>
public static float SweptAABB(RectangleF moving, Vector2 velocity,
    RectangleF target, out Vector2 normal)
{
    normal = Vector2.Zero;

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

### Practical CCD for Bullets

For most games, full swept collision is overkill. A multi-step approach is simpler and sufficient:

```csharp
/// <summary>
/// Move a fast entity in sub-steps to prevent tunneling.
/// Automatically determines step count from speed.
/// </summary>
public static void MoveWithSubsteps(ref Position pos, ref Velocity vel,
    Collider col, TileMap map, float dt)
{
    Vector2 totalDisplacement = new(vel.X * dt, vel.Y * dt);
    float distance = totalDisplacement.Length();

    // How many sub-steps to guarantee no gap larger than half the collider
    float minDim = MathF.Min(col.Width, col.Height);
    int steps = Math.Max(1, (int)MathF.Ceiling(distance / (minDim * 0.5f)));

    float subDt = dt / steps;
    for (int i = 0; i < steps; i++)
    {
        pos = new Position(pos.X + vel.X * subDt, pos.Y + vel.Y * subDt);

        // Check collision after each sub-step
        if (TileCollision.IsSolidAt(pos.X, pos.Y))
        {
            // Hit something — resolve and stop
            TileCollision.ResolveCollisions(ref pos, ref vel, col, map);
            break;
        }
    }
}
```

### When to Use CCD

| Scenario | Speed (px/frame) | Wall Thickness | CCD Needed? |
|---|---|---|---|
| Player walking | 3–5 | 16px+ | No |
| Player dashing | 10–20 | 16px+ | Maybe (sub-steps) |
| Bullets | 15–40 | 16px+ | Yes (swept or sub-steps) |
| Hitscan | Instant | N/A | Use raycast instead |

**Rule of thumb:** If `speed * dt > colliderSize / 2`, you need CCD.

---

## 7. Physics Interpolation

### Fixed Timestep with Render Interpolation

Physics runs at a fixed rate (60 Hz) while rendering runs at the display's refresh rate (144 Hz, 240 Hz, or variable). Without interpolation, objects stutter.

```csharp
public class PhysicsLoop
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
            StepPhysics(FixedDt); // all physics systems
            _accumulator -= FixedDt;
        }

        // Save current position
        World.Query(in _physicsQuery, (ref PhysicsState state, ref Position pos) =>
        {
            state = state with { Current = pos };
        });
    }

    /// <summary>
    /// Call during Draw() to get smooth render position.
    /// Alpha interpolates between last two physics states.
    /// </summary>
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
- Physics rate: **60 Hz** — standard for platformers. Use **120 Hz** for bullet-hell (fast projectiles need tighter collision). Use **30 Hz** for strategy games (save CPU).
- Accumulator clamp: **0.25s** — prevents >15 physics steps per frame (spiral of death). If the game hitches, physics just slows down instead of catching up with a burst.
- **Never skip interpolation** — even on 60 Hz displays, frame timing variance makes physics stutter without it.

### Arch ECS System Ordering

```csharp
// Recommended system execution order:
// 1. InputSystem            — read gamepad/keyboard
// 2. MovingPlatformSystem   — move platforms, apply delta to riders
// 3. GravitySystem          — apply gravity to velocity
// 4. CharacterMoveSystem    — apply input to velocity
// 5. PhysicsStepSystem      — integrate velocity → position
// 6. TileCollisionSystem    — resolve vs tilemap
// 7. EntityCollisionSystem  — entity-vs-entity (spatial hash)
// 8. GroundDetectionSystem  — update grounded state
// 9. TriggerSystem          — process area overlaps, sensors
// 10. AnimationSystem       — pick animation from state
// 11. CameraSystem          — follow target with smoothing
// 12. RenderSystem          — draw with interpolated positions
```

**Why this order matters:**
- **Moving platforms before gravity** — platform delta is applied as position offset, not velocity. If gravity runs first, the rider gets pulled down then pushed up, causing jitter.
- **Tile collision before entity collision** — tiles are static and predictable. Resolve against the world first, then adjust for dynamic entities.
- **Ground detection after ALL collision** — otherwise `IsGrounded` is stale by one frame, causing double-jumps or missed landings.
- **Triggers after collision** — ensures entities are in their resolved positions before checking area overlaps.

---

## 8. Trigger Zones & Sensors

Trigger zones detect overlap without physical response — used for damage areas, pickups, level transitions, checkpoints, and dialogue prompts.

### ECS Trigger System

```csharp
public record struct TriggerZone(
    float Width, float Height,
    float OffsetX, float OffsetY,
    TriggerType Type,
    bool IsActive
);

public enum TriggerType { Damage, Pickup, LevelExit, Checkpoint, Dialogue, Custom }

// Events stored as components — systems can query for them
public record struct TriggerEvent(Entity Trigger, Entity Other, bool IsEnter);

public partial class TriggerSystem : BaseSystem<World, float>
{
    private readonly SpatialHash<Entity> _triggerHash = new(128);
    // Track previous overlaps to detect enter/exit
    private readonly Dictionary<(Entity, Entity), bool> _prevOverlaps = new();
    private readonly Dictionary<(Entity, Entity), bool> _currOverlaps = new();

    public override void Update(in float dt)
    {
        _triggerHash.Clear();
        _currOverlaps.Clear();

        // Populate spatial hash with trigger zones
        World.Query(in _triggerQuery, (Entity entity, ref Position pos,
            ref TriggerZone trigger) =>
        {
            if (!trigger.IsActive) return;
            RectangleF bounds = new(
                pos.X + trigger.OffsetX - trigger.Width / 2f,
                pos.Y + trigger.OffsetY - trigger.Height / 2f,
                trigger.Width, trigger.Height);
            _triggerHash.Insert(entity, bounds);
        });

        // Check all trigger-capable entities against triggers
        var candidates = new List<(Entity Item, RectangleF Bounds)>();
        World.Query(in _actorQuery, (Entity actor, ref Position pos, ref Collider col) =>
        {
            RectangleF actorBounds = GetBounds(pos, col);
            _triggerHash.Query(actorBounds, candidates);

            foreach (var (triggerEntity, triggerBounds) in candidates)
            {
                if (triggerEntity == actor) continue;
                if (actorBounds.Intersects(triggerBounds))
                {
                    var key = (triggerEntity, actor);
                    _currOverlaps[key] = true;

                    // Enter event: wasn't overlapping last frame
                    if (!_prevOverlaps.ContainsKey(key))
                        World.Create(new TriggerEvent(triggerEntity, actor, IsEnter: true));
                }
            }
        });

        // Exit events: was overlapping, no longer is
        foreach (var key in _prevOverlaps.Keys)
        {
            if (!_currOverlaps.ContainsKey(key))
                World.Create(new TriggerEvent(key.Item1, key.Item2, IsEnter: false));
        }

        (_prevOverlaps, _) = (_currOverlaps, _prevOverlaps);
    }
}
```

### Trigger Response System

```csharp
public partial class TriggerResponseSystem : BaseSystem<World, float>
{
    public override void Update(in float dt)
    {
        World.Query(in _eventQuery, (Entity eventEntity, ref TriggerEvent evt) =>
        {
            if (!evt.IsEnter) { World.Destroy(eventEntity); return; }

            ref var zone = ref evt.Trigger.Get<TriggerZone>();
            switch (zone.Type)
            {
                case TriggerType.Damage:
                    if (evt.Other.Has<Health>())
                    {
                        ref var health = ref evt.Other.Get<Health>();
                        ref var dmgZone = ref evt.Trigger.Get<DamageZoneData>();
                        health.Current -= dmgZone.DamagePerTick;
                    }
                    break;

                case TriggerType.Pickup:
                    if (evt.Other.Has<Inventory>())
                    {
                        ref var inv = ref evt.Other.Get<Inventory>();
                        ref var pickup = ref evt.Trigger.Get<PickupData>();
                        inv.Add(pickup.ItemId, pickup.Quantity);
                        World.Destroy(evt.Trigger); // consume pickup
                    }
                    break;

                case TriggerType.LevelExit:
                    SceneManager.LoadScene(evt.Trigger.Get<LevelExitData>().TargetLevel);
                    break;

                case TriggerType.Checkpoint:
                    evt.Other.Get<CheckpointData>().LastCheckpoint = 
                        evt.Trigger.Get<Position>();
                    break;
            }

            World.Destroy(eventEntity); // consume the event
        });
    }
}
```

---

## 9. Top-Down Physics

Top-down games need different physics from platformers — no gravity, 360° movement, wall sliding, and circular colliders are common.

### Top-Down Character Movement

```csharp
public record struct TopDownController(
    float MoveSpeed,       // 180f px/s
    float Acceleration,    // 1200f px/s²
    float Friction,        // 800f px/s² (ground friction when no input)
    float MaxSpeed,        // 220f px/s
    bool IsDashing,
    float DashTimer,
    float DashCooldown
);

public partial class TopDownMoveSystem : BaseSystem<World, float>
{
    public override void Update(in float dt)
    {
        World.Query(in _query, (ref Velocity vel, ref TopDownController td) =>
        {
            Vector2 input = InputManager.MoveDirection; // normalized stick/WASD

            if (input.LengthSquared() > 0.01f)
            {
                // Accelerate toward input direction
                vel = new Velocity(
                    vel.X + input.X * td.Acceleration * dt,
                    vel.Y + input.Y * td.Acceleration * dt);
            }
            else
            {
                // Apply friction when no input
                float speed = MathF.Sqrt(vel.X * vel.X + vel.Y * vel.Y);
                if (speed > 0)
                {
                    float drop = td.Friction * dt;
                    float newSpeed = MathF.Max(0, speed - drop);
                    float scale = newSpeed / speed;
                    vel = new Velocity(vel.X * scale, vel.Y * scale);
                }
            }

            // Clamp to max speed
            float currentSpeed = MathF.Sqrt(vel.X * vel.X + vel.Y * vel.Y);
            if (currentSpeed > td.MaxSpeed)
            {
                float s = td.MaxSpeed / currentSpeed;
                vel = new Velocity(vel.X * s, vel.Y * s);
            }
        });
    }
}
```

### Wall Sliding (Top-Down)

When a character hits a wall at an angle, they should slide along it instead of stopping dead:

```csharp
// After collision resolution gives us an MTV:
if (AABBOverlap(entityBounds, wallBounds, out Vector2 mtv))
{
    // Push out of wall
    pos = new Position(pos.X + mtv.X, pos.Y + mtv.Y);

    // Project velocity onto wall surface (slide)
    // Wall normal is the MTV direction
    Vector2 wallNormal = Vector2.Normalize(mtv);
    float velDotNormal = vel.X * wallNormal.X + vel.Y * wallNormal.Y;

    // Remove the component going into the wall, keep the sliding component
    vel = new Velocity(
        vel.X - wallNormal.X * velDotNormal,
        vel.Y - wallNormal.Y * velDotNormal);
}
```

### Top-Down Dash

```csharp
const float DashSpeed = 600f;    // px/s
const float DashDuration = 0.15f; // seconds
const float DashCooldown = 0.8f;  // seconds

// In update:
if (InputManager.DashPressed && td.DashCooldown <= 0f && !td.IsDashing)
{
    td.IsDashing = true;
    td.DashTimer = DashDuration;
    td.DashCooldown = DashCooldown;

    // Dash in facing direction (or input direction if held)
    Vector2 dashDir = input.LengthSquared() > 0.01f
        ? Vector2.Normalize(input)
        : entity.Get<Facing>().Direction;

    vel = new Velocity(dashDir.X * DashSpeed, dashDir.Y * DashSpeed);
    entity.Add(new Invincible(DashDuration)); // i-frames during dash
}

if (td.IsDashing)
{
    td.DashTimer -= dt;
    if (td.DashTimer <= 0)
    {
        td.IsDashing = false;
        // Reduce velocity to normal max speed
        vel = new Velocity(vel.X * 0.3f, vel.Y * 0.3f);
    }
}

td.DashCooldown -= dt;
```

---

## 10. Verlet Integration (Soft-Body Physics)

Verlet integration simulates ropes, chains, cloth, and soft bodies. Unlike velocity-based physics, Verlet stores position history — making it naturally stable and easy to constrain.

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

    /// <summary>Render with line segments or as a SpriteBatch chain.</summary>
    public void Draw(SpriteBatch sb, Texture2D pixel, Color color, float thickness = 2f)
    {
        for (int i = 0; i < Points.Length - 1; i++)
        {
            Vector2 a = Points[i].Position;
            Vector2 b = Points[i + 1].Position;
            Vector2 edge = b - a;
            float angle = MathF.Atan2(edge.Y, edge.X);
            float length = edge.Length();

            sb.Draw(pixel, a, null, color, angle,
                Vector2.Zero, new Vector2(length, thickness),
                SpriteEffects.None, 0f);
        }
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
    public int ConstraintIterations = 4;

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

    /// <summary>
    /// Tear cloth when stretch exceeds threshold.
    /// Creates holes where force is applied.
    /// </summary>
    public void TearCheck(float tearThreshold = 2.5f)
    {
        Constraints.RemoveAll(c =>
        {
            float dist = Vector2.Distance(Points[c.IndexA].Position, Points[c.IndexB].Position);
            return dist > c.RestLength * tearThreshold;
        });
    }

    /// <summary>Apply wind force to all non-pinned points.</summary>
    public void ApplyWind(Vector2 windForce, float turbulence = 0.3f)
    {
        for (int i = 0; i < Points.Length; i++)
        {
            if (Points[i].Pinned) continue;
            // Add some noise for natural-looking movement
            float noise = 1f + turbulence * MathF.Sin(i * 0.7f + _time * 3f);
            Points[i].ApplyForce(windForce * noise);
        }
    }
}
```

### Verlet Collision with World

```csharp
/// <summary>
/// Constrain Verlet points to stay outside solid tiles.
/// Call after constraint solving each frame.
/// </summary>
public static void VerletTileCollision(VerletPoint[] points, TileMap map)
{
    for (int i = 0; i < points.Length; i++)
    {
        if (points[i].Pinned) continue;

        int tx = (int)(points[i].Position.X / TileCollision.TileSize);
        int ty = (int)(points[i].Position.Y / TileCollision.TileSize);

        if (!map.InBounds(tx, ty) || !map.IsSolid(tx, ty)) continue;

        // Push point to nearest tile edge
        RectangleF tileBounds = new(tx * TileCollision.TileSize,
            ty * TileCollision.TileSize, TileCollision.TileSize, TileCollision.TileSize);
        Vector2 center = new(tileBounds.Center.X, tileBounds.Center.Y);
        Vector2 diff = points[i].Position - center;

        if (MathF.Abs(diff.X) / tileBounds.Width > MathF.Abs(diff.Y) / tileBounds.Height)
            points[i].Position = new Vector2(
                diff.X > 0 ? tileBounds.Right : tileBounds.Left,
                points[i].Position.Y);
        else
            points[i].Position = new Vector2(
                points[i].Position.X,
                diff.Y > 0 ? tileBounds.Bottom : tileBounds.Top);
    }
}
```

---

## 11. Aether.Physics2D v2.2.0

Use Aether when you need **real physics simulation** — joints, springs, motors, ragdolls, breakable objects, or any scenario where objects should react physically to forces.

**NuGet packages:**
- `Aether.Physics2D` — standalone, no framework dependency
- `Aether.Physics2D.MG` — MonoGame-specific (uses `Microsoft.Xna.Framework.Vector2`)
- `Aether.Physics2D.Diagnostics.MG` — debug rendering (draw bodies, joints, contacts)

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
Fixture groundFixture = ground.CreateRectangle(20f, 0.5f, 1f, Vector2.Zero);
groundFixture.Restitution = 0.3f;  // v2.2.0: fixture-level properties
groundFixture.Friction = 0.5f;     // (Body.SetRestitution/SetFriction removed in v2.2)

// Create dynamic box
Body box = physicsWorld.CreateBody(new Vector2(0, 0), 0f, BodyType.Dynamic);
Fixture boxFixture = box.CreateRectangle(1f, 1f, 1f, Vector2.Zero);
boxFixture.Restitution = 0.2f;
box.Mass = 1f;
```

### Stepping (Fixed Timestep)

```csharp
// In Update() — ALWAYS use fixed dt, never variable:
physicsWorld.Step(1f / 60f);

// Or with sub-stepping for more accuracy (ragdolls, stacking):
physicsWorld.Step(dt, velocityIterations: 8, positionIterations: 3);
// More iterations = more stable stacking but more CPU
```

### Contact Listeners

```csharp
// Per-fixture callbacks (v2.2.0 pattern):
Fixture playerFixture = playerBody.FixtureList[0];

playerFixture.OnCollision += (fixtureA, fixtureB, contact) =>
{
    // contact.Manifold has collision points
    contact.GetWorldManifold(out Vector2 normal, out FixedArray2<Vector2> points);
    // normal.Y < -0.5f means landing on top (remember: Y-down in screen space)
    return true; // return false to cancel collision
};

playerFixture.OnSeparation += (fixtureA, fixtureB, contact) =>
{
    // Objects stopped touching — update state
};

// Pre-solve: modify contact before resolution (one-way platform logic)
playerFixture.BeforeCollision += (fixtureA, fixtureB) =>
{
    Body other = fixtureB.Body;
    if (other.Tag is "OneWayPlatform" && playerBody.LinearVelocity.Y < 0)
        return false; // skip collision when moving up
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
    // No physical response occurs — sensor only detects overlap
    return true;
};
```

### Joint Examples

```csharp
// Revolute joint (hinge) — swinging door, flail weapon, pendulum
var hinge = JointFactory.CreateRevoluteJoint(physicsWorld, bodyA, bodyB, Vector2.Zero);
hinge.LowerLimit = -MathHelper.PiOver4;
hinge.UpperLimit = MathHelper.PiOver4;
hinge.LimitEnabled = true;
hinge.MotorSpeed = 2f;       // radians/sec
hinge.MaxMotorTorque = 100f;
hinge.MotorEnabled = true;

// Distance joint (spring) — bungee, suspension bridge, elastic tether
var spring = JointFactory.CreateDistanceJoint(physicsWorld, bodyA, bodyB);
spring.Length = 3f;        // rest length in meters
spring.Stiffness = 5f;    // spring constant (higher = stiffer)
spring.Damping = 0.7f;    // 0 = bouncy forever, 1 = critically damped

// Prismatic joint (slider) — elevator, piston, sliding door
var slider = JointFactory.CreatePrismaticJoint(physicsWorld, bodyA, bodyB,
    Vector2.Zero, new Vector2(0, 1)); // axis = vertical
slider.LowerLimit = 0f;
slider.UpperLimit = 5f;     // meters of travel
slider.LimitEnabled = true;

// Weld joint — glue two bodies (breakable: destroy joint when force exceeds threshold)
var weld = JointFactory.CreateWeldJoint(physicsWorld, bodyA, bodyB,
    bodyA.Position, bodyB.Position);
// In update: if (weld.GetReactionForce(1f/60f).Length() > breakThreshold)
//     physicsWorld.Remove(weld);

// Mouse joint — drag objects with cursor (great for debug/editor)
var mouse = JointFactory.CreateFixedMouseJoint(physicsWorld, body, cursorWorldPos);
mouse.MaxForce = 1000f * body.Mass; // strong enough to move but not teleport
```

### Collision Categories & Filtering

```csharp
// Define categories as bit flags
public static class PhysicsCategory
{
    public const Category Player    = Category.Cat1;  // 0x0001
    public const Category Enemy     = Category.Cat2;  // 0x0002
    public const Category Terrain   = Category.Cat3;  // 0x0004
    public const Category Projectile = Category.Cat4; // 0x0008
    public const Category Pickup    = Category.Cat5;  // 0x0010
    public const Category Sensor    = Category.Cat6;  // 0x0020
}

// Set on fixtures:
playerFixture.CollisionCategories = PhysicsCategory.Player;
playerFixture.CollidesWith = PhysicsCategory.Terrain | PhysicsCategory.Enemy
                           | PhysicsCategory.Pickup;

enemyFixture.CollisionCategories = PhysicsCategory.Enemy;
enemyFixture.CollidesWith = PhysicsCategory.Terrain | PhysicsCategory.Player
                          | PhysicsCategory.Projectile;

// Projectiles don't hit other projectiles or pickups:
bulletFixture.CollisionCategories = PhysicsCategory.Projectile;
bulletFixture.CollidesWith = PhysicsCategory.Terrain | PhysicsCategory.Enemy;
```

### Common Gotchas

1. **Units are meters, not pixels.** Divide pixel positions by `PixelsPerMeter` when setting body positions. Multiply back when rendering.
2. **Don't create/destroy bodies during Step().** Queue changes in a list and apply them before/after stepping.
3. **Body.Tag** is `object` — use it to link back to your ECS entity: `body.Tag = entity;`
4. **Removed in v2.2.0:** `Body.SetRestitution(float)`, `Body.SetFriction(float)`, `Body.SetCollisionCategories()`. Use fixture-level properties.
5. **Sleep management:** Bodies auto-sleep when stationary. Set `body.SleepingAllowed = false` for always-active bodies (player, enemies).
6. **Broadphase:** Default is DynamicTree. Fine for most games.
7. **Body count limits:** Aether handles 500–1,000 dynamic bodies well. Beyond that, performance degrades — consider object pooling (see [G67](./G67_object_pooling.md)) or converting distant bodies to kinematic/static.
8. **Debug rendering:** Add `Aether.Physics2D.Diagnostics.MG` package, create a `DebugView`, and call `debugView.RenderDebugData(projectionMatrix)` in Draw. Invaluable for visualizing collision shapes, joints, and contacts during development.

---

## 12. MonoGame.Extended v5.3.1 Collision

MonoGame.Extended provides collision detection (NOT physics simulation). Use it when you want spatial queries and overlap detection without the overhead of a full physics engine.

Current stable: **5.3.1** (NuGet `MonoGame.Extended`).

### Collision System Architecture

- **`ICollisionActor`** interface — implement `Bounds` (as `IShapeF`) and `OnCollision(CollisionEventArgs)`
- **Shape primitives:** `RectangleF`, `CircleF`, `SizeF`, `Point2`, `Vector2` extensions
- **Spatial algorithms:** `QuadTree` (default for "default" layer), `SpatialHash` (configurable per layer)
- **Layers:** Entities in the same custom layer don't collide with each other; only checked against "default" layer
- **`CollisionEventArgs.PenetrationVector`** — MTV for push-back resolution

### Using Extended Collision with Arch ECS

```csharp
/// <summary>
/// Bridge pattern: wrap Arch entity in ICollisionActor.
/// MonoGame.Extended needs objects, but our data lives in ECS components.
/// </summary>
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
        // Read back into ECS: apply penetration vector
        ref var pos = ref Entity.Get<Position>();
        pos = new Position(pos.X - args.PenetrationVector.X,
                          pos.Y - args.PenetrationVector.Y);
    }
}
```

### When to Use Extended vs Aether vs Custom

| Need | Use |
|---|---|
| Simple AABB/circle overlap | Custom (§1) |
| Spatial queries (area, nearest) | Extended or custom spatial hash (§2) |
| Joints, springs, motors | Aether (§11) |
| Ragdolls, breakable objects | Aether |
| Verlet ropes/cloth | Custom Verlet (§10) |
| Deterministic netcode | Custom fixed-point (§13) |
| Tilemap collision | Custom tile grid (§5) |

---

## 13. Fixed-Point Math for Deterministic Physics

### Why Fixed-Point?

IEEE 754 `float` produces different results across architectures (x86 vs ARM), compiler settings, and even instruction ordering. For **rollback netcode** and **replay systems**, physics must be bit-identical across all machines. Fixed-point guarantees this.

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

    // Arithmetic — all deterministic, no floats involved
    public static Fixed32 operator +(Fixed32 a, Fixed32 b) => new(a.RawValue + b.RawValue);
    public static Fixed32 operator -(Fixed32 a, Fixed32 b) => new(a.RawValue - b.RawValue);
    public static Fixed32 operator *(Fixed32 a, Fixed32 b) =>
        new((int)(((long)a.RawValue * b.RawValue) >> FractionalBits));
    public static Fixed32 operator /(Fixed32 a, Fixed32 b) =>
        new((int)(((long)a.RawValue << FractionalBits) / b.RawValue));

    // Unary
    public static Fixed32 operator -(Fixed32 a) => new(-a.RawValue);

    // Comparison
    public static bool operator <(Fixed32 a, Fixed32 b) => a.RawValue < b.RawValue;
    public static bool operator >(Fixed32 a, Fixed32 b) => a.RawValue > b.RawValue;
    public static bool operator <=(Fixed32 a, Fixed32 b) => a.RawValue <= b.RawValue;
    public static bool operator >=(Fixed32 a, Fixed32 b) => a.RawValue >= b.RawValue;
    public static bool operator ==(Fixed32 a, Fixed32 b) => a.RawValue == b.RawValue;
    public static bool operator !=(Fixed32 a, Fixed32 b) => a.RawValue != b.RawValue;
    public bool Equals(Fixed32 other) => RawValue == other.RawValue;
    public int CompareTo(Fixed32 other) => RawValue.CompareTo(other.RawValue);
    public override bool Equals(object? obj) => obj is Fixed32 f && Equals(f);
    public override int GetHashCode() => RawValue;

    // Math functions (all deterministic)
    public static Fixed32 Abs(Fixed32 v) => new(Math.Abs(v.RawValue));
    public static Fixed32 Min(Fixed32 a, Fixed32 b) => a.RawValue < b.RawValue ? a : b;
    public static Fixed32 Max(Fixed32 a, Fixed32 b) => a.RawValue > b.RawValue ? a : b;

    /// <summary>
    /// Sqrt via Newton's method — fully deterministic, no floating point.
    /// </summary>
    public static Fixed32 Sqrt(Fixed32 v)
    {
        if (v.RawValue <= 0) return new(0);
        long val = (long)v.RawValue << FractionalBits;
        long guess = val >> 1;
        for (int i = 0; i < 16; i++) // 16 iterations = full precision
            guess = (guess + val / guess) >> 1;
        return new((int)guess);
    }

    // Common constants
    public static readonly Fixed32 Zero = new(0);
    public static readonly Fixed32 One = FromInt(1);
    public static readonly Fixed32 Half = new(Scale / 2);

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
    public static FixedVec2 operator -(FixedVec2 a, FixedVec2 b) =>
        new(a.X - b.X, a.Y - b.Y);
    public static FixedVec2 operator *(FixedVec2 v, Fixed32 s) =>
        new(v.X * s, v.Y * s);

    public Fixed32 LengthSquared() => X * X + Y * Y;
    public Fixed32 Length() => Fixed32.Sqrt(LengthSquared());

    public FixedVec2 Normalized()
    {
        Fixed32 len = Length();
        if (len == Fixed32.Zero) return new(Fixed32.Zero, Fixed32.Zero);
        return new(X / len, Y / len);
    }

    public static Fixed32 Dot(FixedVec2 a, FixedVec2 b) => a.X * b.X + a.Y * b.Y;
    public static readonly FixedVec2 Zero = new(Fixed32.Zero, Fixed32.Zero);
}
```

### Libraries to Consider

- **[FixedMath.Net](https://github.com/asik/FixedMath.Net)** — Q31.32 (64-bit), battle-tested, includes sin/cos/atan2 lookup tables. Best for production rollback games.
- **Roll your own Q16.16** (above) for simpler 2D needs — less precision but faster multiply, sufficient for most 2D games.
- For Arch ECS: use `FixedVec2` in your Position/Velocity components; convert to `float` only at render time.

### Deterministic Physics Checklist

1. **No `float`/`double` in simulation** — fixed-point only
2. **Fixed iteration order** — sort entities by ID before processing
3. **No `Dictionary` iteration** (non-deterministic order) — use `SortedDictionary` or `List`
4. **Same timestep** — never use variable `dt` in simulation
5. **Serialize state as raw ints** — use checksums for desync detection
6. **Platform-independent RNG** — seed-based, integer-only PRNG (don't use `System.Random`)
7. **No `MathF`/`Math` in simulation** — float-based transcendentals are platform-dependent
8. **Test cross-platform** — run simulation on x86 and ARM, compare state checksums

---

## 14. Common Mistakes & Troubleshooting

### Problem: Character gets stuck on tile corners

**Symptom:** Walking along a flat floor, character randomly stops at tile seams.

**Cause:** AABB collision resolves on the shortest axis. At tile boundaries, the horizontal overlap can be tiny (< 1px), causing X-axis resolution that blocks movement.

**Fix:** Resolve Y-axis first in a separate pass, THEN X-axis:
```csharp
// Two-pass resolution (see §5 TileCollision.ResolveCollisions)
for (int pass = 0; pass < 2; pass++) { /* Y first, then X */ }
```

### Problem: Character falls through floor at high speed

**Symptom:** At high fall speeds (dashing downward, fast gravity), character tunnels through thin platforms.

**Cause:** Per-frame displacement exceeds platform thickness.

**Fix:** Use CCD sub-stepping (§6) or increase floor thickness to 2+ tiles.

### Problem: Jittery movement on moving platforms

**Symptom:** Character vibrates or stutters when riding a moving platform.

**Cause:** System ordering — gravity pulls character down, platform pushes up, every frame.

**Fix:** Run `MovingPlatformSystem` BEFORE `GravitySystem`. Apply platform delta as position offset, not velocity.

### Problem: Objects "teleport" through walls when pushed by other objects

**Symptom:** When two dynamic entities push each other, one can end up inside or through a wall.

**Cause:** Entity-vs-entity collision pushes entity into wall, but tile collision already ran.

**Fix:** Run tile collision AFTER entity collision, or add a second tile-collision pass:
```csharp
// 6. TileCollisionSystem
// 7. EntityCollisionSystem
// 7b. TileCollisionSystem (second pass — catch entities pushed into tiles)
```

### Problem: Coyote time allows double-jump

**Symptom:** Player can jump, then jump again mid-air during the coyote window.

**Cause:** Coyote timer isn't consumed on jump.

**Fix:** Set `cc.CoyoteTimer = 0` when jump executes (see §4 Jump System).

### Problem: Aether bodies move erratically or explode

**Symptom:** Bodies fly off screen or oscillate wildly.

**Cause:** Usually one of: (1) units in pixels instead of meters, (2) variable timestep, (3) creating/destroying bodies during `Step()`.

**Fix:**
```csharp
// Always divide by PixelsPerMeter:
body.Position = new Vector2(pixelX / 64f, pixelY / 64f);

// Always use fixed step:
physicsWorld.Step(1f / 60f); // NOT gameTime.ElapsedGameTime

// Queue body creation/destruction:
_bodiesToCreate.Add(bodyDef);
// Apply after Step():
foreach (var def in _bodiesToCreate) physicsWorld.CreateBody(def);
_bodiesToCreate.Clear();
```

### Problem: Verlet rope stretches and never recovers

**Symptom:** Rope gets longer over time, especially under load.

**Cause:** Too few constraint solver iterations.

**Fix:** Increase `ConstraintIterations` (8–16 for rigid ropes). For cloth, 4–6 is usually sufficient because slight stretch looks natural.

### Problem: Spatial hash returns duplicates

**Symptom:** Entity gets hit/damaged twice per frame.

**Cause:** Entity's AABB spans multiple cells → returned once per cell.

**Fix:** Use a `HashSet<T>` to deduplicate results (see §2 `SpatialHash.Query`).

---

## Quick Reference: Tuning Tables

### Platformer Feel Presets

| Parameter | Tight (Celeste) | Standard (Mario) | Floaty (Kirby) |
|---|---|---|---|
| Gravity | 1400 px/s² | 980 px/s² | 600 px/s² |
| Jump velocity | -420 px/s | -350 px/s | -280 px/s |
| Move speed | 250 px/s | 200 px/s | 150 px/s |
| Max fall speed | 400 px/s | 600 px/s | 300 px/s |
| Coyote time | 0.08s | 0.10s | 0.15s |
| Jump buffer | 0.10s | 0.08s | 0.06s |
| Jump cut multiplier | 0.4 | 0.5 | 0.6 |
| Ground accel | 1500 px/s² | 1000 px/s² | 600 px/s² |
| Air accel | 1200 px/s² | 800 px/s² | 500 px/s² |
| Ground friction | 1200 px/s² | 800 px/s² | 400 px/s² |
| Air friction | 200 px/s² | 100 px/s² | 50 px/s² |

### Top-Down Movement Presets

| Parameter | Fast (Action) | Standard (RPG) | Slow (Horror) |
|---|---|---|---|
| Move speed | 250 px/s | 180 px/s | 100 px/s |
| Acceleration | 2000 px/s² | 1200 px/s² | 600 px/s² |
| Friction | 1500 px/s² | 800 px/s² | 500 px/s² |
| Max speed | 300 px/s | 220 px/s | 120 px/s |
| Dash speed | 800 px/s | 600 px/s | 400 px/s |
| Dash duration | 0.10s | 0.15s | 0.20s |
| Dash cooldown | 0.5s | 0.8s | 1.5s |

### Physics System Budgets

| System | Budget (ms) | Notes |
|---|---|---|
| Broad phase | 0.1–0.3 | Spatial hash rebuild |
| Narrow phase (100 entities) | 0.05–0.2 | AABB tests |
| Tile collision (per entity) | 0.01–0.05 | Grid lookup, no broad phase needed |
| Aether Step (100 bodies) | 0.5–1.5 | Depends on iterations |
| Verlet rope (50 segments) | 0.02–0.08 | Per iteration |
| Verlet cloth (20×20) | 0.1–0.4 | Per iteration |
| Ground detection (3-ray) | 0.01 | Per entity |

### Aether.Physics2D Quick Reference

| Property | Typical Value | Notes |
|---|---|---|
| PixelsPerMeter | 64 | Conversion ratio |
| Gravity | (0, 9.8) | m/s², Earth-like |
| Restitution | 0.0–1.0 | 0 = no bounce, 1 = full bounce |
| Friction | 0.0–1.0 | 0 = ice, 0.5 = normal, 1 = rubber |
| Density | 1.0 | kg/m² (affects mass) |
| Linear damping | 0.0–5.0 | Air resistance (0 = none) |
| Angular damping | 0.0–5.0 | Rotation resistance |
| Velocity iterations | 6–8 | Higher = more stable stacking |
| Position iterations | 2–3 | Higher = less overlap |
| Max body count | ~500–1000 | Before perf degrades |

---

## Cross-References

| Topic | Guide |
|---|---|
| Full character controller | [G52 Character Controller](./G52_character_controller.md) |
| Tilemap rendering + loading | [G37 Tilemap Systems](./G37_tilemap_systems.md) |
| Hitbox/hurtbox + damage pipeline | [G64 Combat & Damage](./G64_combat_damage_systems.md) |
| Object pooling for projectiles | [G67 Object Pooling](./G67_object_pooling.md) |
| Knockback + hit reactions | [G64 §6 Knockback System](./G64_combat_damage_systems.md) |
| Screen shake from impacts | [G20 Camera Systems](./G20_camera_systems.md) |
| AI pathfinding + navigation | [G40 Pathfinding](./G40_pathfinding_navigation.md) |
| Profiling physics performance | [G33 Profiling & Optimization](./G33_profiling_optimization.md) |
| C# performance (structs, Span) | [G13 C# Performance](./G13_csharp_performance.md) |
| Networking + rollback | [Networking Theory](../../core/concepts/networking-theory.md) |
| Animation from physics state | [G31 Animation State Machines](./G31_animation_state_machines.md) |
| 2D lighting + shadows | [G39 2D Lighting](./G39_2d_lighting.md) |
| Game feel + juice | [G30 Game Feel Tooling](./G30_game_feel_tooling.md) |
