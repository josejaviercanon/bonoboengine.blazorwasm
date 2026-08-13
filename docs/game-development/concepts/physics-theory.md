# Physics & Collision -- Theory & Concepts

This document covers engine-agnostic 2D physics and collision detection theory. For engine-specific implementations, see the relevant engine module.

---

## Collision Detection

### AABB (Axis-Aligned Bounding Box)

The simplest and most common collision shape. Two AABBs overlap if they overlap on both axes.

**Overlap test:**
```
overlap_x = min(a.right, b.right) - max(a.left, b.left)
overlap_y = min(a.bottom, b.bottom) - max(a.top, b.top)
colliding = overlap_x > 0 AND overlap_y > 0
```

**Minimum Translation Vector (MTV):** Push along the axis with the smallest overlap to separate the shapes with minimal displacement.

### Circle vs Circle

```
diff = center_a - center_b
dist_sq = dot(diff, diff)
radius_sum = radius_a + radius_b
colliding = dist_sq < radius_sum * radius_sum

// MTV
dist = sqrt(dist_sq)
mtv = (diff / dist) * (radius_sum - dist)
```

### SAT (Separating Axis Theorem)

For convex polygon collision. If you can find an axis where the projections of two polygons do not overlap, they are not colliding. Test all edge normals from both polygons.

```
for each edge normal (axis) of both polygons:
    project polygon A onto axis -> (minA, maxA)
    project polygon B onto axis -> (minB, maxB)
    overlap = min(maxA, maxB) - max(minA, minB)
    if overlap <= 0: return NO COLLISION
    track smallest overlap for MTV
```

The MTV direction is the axis with the smallest overlap. Ensure it points from B to A.

### Swept AABB (Continuous Collision Detection)

For fast-moving objects that might tunnel through thin obstacles. Compute the time of impact [0..1] for a moving AABB against a static AABB using entry/exit times on each axis.

---

## Spatial Partitioning

Avoid checking every entity against every other entity (O(n^2)):

| Structure | Best For |
|-----------|----------|
| **Grid / Spatial Hash** | Uniform entity distribution, simple |
| **Quadtree** | Non-uniform distribution, dynamic |
| **Sweep and Prune** | Many moving objects, broad phase |

### Spatial Hash

Divide the world into fixed-size cells. Each entity is inserted into the cell(s) it overlaps. To check collisions for an entity, only test against entities in the same or neighboring cells.

---

## Physics Integration

### Euler Integration (Semi-Implicit)

The simplest approach. Update velocity first, then position:

```
velocity += acceleration * dt
position += velocity * dt
```

Sufficient for most 2D games. Accumulates error over time but acceptable for game physics.

### Verlet Integration

Position-based: uses current and previous position instead of explicit velocity. Inherently stable for constraint systems (ropes, cloth).

```
temp = position
position = 2 * position - old_position + acceleration * dt * dt
old_position = temp
```

**Velocity** is implicit: `velocity = position - old_position`. Damping is applied as a multiplier (e.g., 0.99).

### Distance Constraints (Ropes, Chains)

After integration, iteratively correct pairs of connected points to maintain their rest length:

```
for each constraint:
    diff = point_b.position - point_a.position
    dist = length(diff)
    error = (dist - rest_length) / dist
    correction = diff * error * 0.5 * stiffness
    point_a.position += correction
    point_b.position -= correction
```

More solver iterations = stiffer results. 4--8 iterations is typical.

---

## Fixed Timestep

Games must run physics at a fixed timestep (typically 60 Hz) regardless of frame rate. Variable timesteps cause non-deterministic physics.

### Accumulator Pattern

```
accumulator += frame_dt
accumulator = min(accumulator, max_dt)    // cap to prevent spiral of death

while accumulator >= fixed_dt:
    step_physics(fixed_dt)
    accumulator -= fixed_dt
```

### Render Interpolation

For smooth rendering at variable frame rates, interpolate between previous and current physics state:

```
alpha = accumulator / fixed_dt
render_position = lerp(previous_position, current_position, alpha)
```

This adds one frame of visual latency but eliminates micro-stutter at high refresh rates.

---

## Tile-Based Collision

### Efficient Resolution

Only check tiles near the entity -- no spatial queries needed:

1. Compute the tile range overlapping the entity's bounding box
2. For each solid tile in that range, perform AABB overlap test
3. Resolve collisions using MTV

### Axis Resolution Order

Resolve Y first (gravity), then X (movement). This prevents diagonal corner-catching artifacts. Recalculate bounds between passes.

### Slope Tiles

For 45-degree slopes, compute the surface height at the entity's horizontal position within the tile:

```
// Right slope (rises left to right)
surface_y = tile_size - local_x

// Left slope (rises right to left)
surface_y = local_x
```

If the entity's bottom is below the surface, push it up and zero downward velocity.

---

## Trigger Zones (Sensors)

Sensor bodies detect overlap without physical response. Use for:

- Item pickup areas
- Damage zones
- Area transitions (room changes, cutscene triggers)
- Dialogue interaction ranges

The sensor fires enter/exit callbacks but does not push objects apart.

---

## One-Way Platforms

Allow entities to pass through from below but stand on top:

```
function is_one_way_passable(entity_bottom, platform_top, velocity_y):
    return velocity_y < 0 OR entity_bottom > platform_top + tolerance
```

**Drop-through:** Set a timer that skips all one-way platform collisions for ~0.2 seconds, allowing the entity to fall through.

---

## Moving Platforms

After moving platforms update their position, compute the delta and apply it to any entity standing on them:

```
platform_delta = platform.position - platform.previous_position
// For each rider standing on this platform:
rider.position += platform_delta
```

---

## Deterministic Physics

For rollback netcode and replay systems, physics must produce identical results across machines:

1. No floating-point in simulation -- use fixed-point math
2. Fixed iteration order -- sort entities by ID before processing
3. No hash map iteration (non-deterministic order) -- use sorted collections
4. Same timestep -- never use variable dt in simulation
5. Platform-independent RNG -- seed-based, integer-only PRNG

---

## Typical Platformer Values

| Parameter | Value | Notes |
|-----------|-------|-------|
| Gravity | 980 px/s^2 | ~2x real gravity, feels snappy |
| Jump velocity | -350 px/s | Negative = upward |
| Move speed | 200 px/s | Ground movement |
| Max fall speed | 600 px/s | Terminal velocity cap |
| Coyote time | 0.08--0.12s | Grace period after leaving edge |
| Jump buffer | 0.06--0.10s | Pre-land jump input window |
| Physics rate | 60 Hz | Fixed timestep |

---

*Implementation examples are available in engine-specific modules.*
