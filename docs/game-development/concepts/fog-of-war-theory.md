# Fog of War -- Theory & Concepts

This document covers engine-agnostic fog of war and visibility system theory. For engine-specific implementations, see the relevant engine module.

---

## Visibility States

Every cell on the map exists in one of three states:

| State | Visual | Gameplay |
|-------|--------|----------|
| **Unexplored** | Solid black | Player has never seen this tile. No information |
| **Explored** | Dark / desaturated | Previously seen. Terrain visible, but entities hidden |
| **Visible** | Fully lit | Currently in line of sight. Everything revealed |

**Key invariant:** Visible implies Explored. Once a cell is explored, it never reverts to unexplored. The visibility layer is transient -- cleared and rebuilt every time vision sources move.

### The Fog Grid

Two boolean arrays per grid:

- `visible[]` -- current-frame visibility (cleared each update, rebuilt from scratch)
- `explored[]` -- persistent (once true, stays true forever)

```
function get_state(grid, x, y):
    if out_of_bounds(x, y): return UNEXPLORED
    index = y * width + x
    if visible[index]: return VISIBLE
    if explored[index]: return EXPLORED
    return UNEXPLORED

function reveal(grid, x, y):
    if out_of_bounds(x, y): return
    index = y * width + x
    visible[index] = true
    explored[index] = true    // permanent

function clear_visible(grid):
    set all visible[] to false
```

---

## Line-of-Sight (Raycasting)

The simplest visibility approach: cast a ray from the viewer to every candidate tile using Bresenham's line algorithm. If the ray hits an opaque cell before reaching the target, the target is not visible.

### Bresenham's Line

Walk from source to target using integer steps. At each step, check if the cell is opaque (wall). If opaque before reaching the target, line of sight is blocked.

```
function line_of_sight(x0, y0, x1, y1, is_opaque):
    dx = abs(x1 - x0), sx = sign(x1 - x0)
    dy = abs(y1 - y0), sy = sign(y1 - y0)
    err = dx - dy

    while true:
        if x0 == x1 and y0 == y1: return true   // reached target
        if is_opaque(x0, y0): return false        // blocked

        e2 = 2 * err
        if e2 > -dy: err -= dy; x0 += sx
        if e2 <  dx: err += dx; y0 += sy
```

### Brute-Force Raycast FOV

Cast rays to every tile within the vision radius:

```
function raycast_fov(fog, cx, cy, radius, is_opaque):
    r2 = radius * radius
    for dy = -radius to radius:
        for dx = -radius to radius:
            if dx*dx + dy*dy > r2: skip
            tx = cx + dx, ty = cy + dy
            if not in_bounds(tx, ty): skip
            if line_of_sight(cx, cy, tx, ty, is_opaque):
                reveal(fog, tx, ty)
```

**Performance:** O(r^3) -- for radius 10, about 1200 rays with up to 10 steps each. Fine for a single player. Terrible for 50 RTS units. Use shadowcasting instead.

### Symmetric vs Asymmetric Visibility

- **Asymmetric:** If A can see B, B might not see A (ray path differs due to Bresenham stepping). Default and cheaper.
- **Symmetric:** If A sees B, then B sees A. Required for multiplayer fairness. Use shadowcasting or diamond-wall algorithms.

---

## Shadowcasting Algorithm

Recursive shadowcasting is the gold standard for roguelike/strategy FOV. It processes the map in **8 octants**, scanning row-by-row outward from the source. Walls create shadow regions that are skipped entirely, making it dramatically faster than raycasting.

### How It Works

1. Divide the circle around the viewer into 8 octants (45 degrees each)
2. For each octant, scan columns/rows moving outward from the source
3. Track a "slope window" `[start_slope, end_slope]` representing the visible arc
4. When hitting a wall, the visible arc shrinks
5. When a wall ends, recurse with the narrowed window

### Octant Transformation

Each octant uses a transform matrix to map local (column, row) to world (dx, dy). This avoids writing 8 copies of the scan logic:

```
octant_transforms = [
    // col_dx, col_dy, row_dx, row_dy
    [ 1,  0,  0,  1],   // E-NE
    [ 0,  1,  1,  0],   // N-NE
    [ 0, -1,  1,  0],   // N-NW
    [-1,  0,  0,  1],   // W-NW
    [-1,  0,  0, -1],   // W-SW
    [ 0, -1, -1,  0],   // S-SW
    [ 0,  1, -1,  0],   // S-SE
    [ 1,  0,  0, -1],   // E-SE
]
```

### Scan Algorithm

```
function compute_fov(fog, origin_x, origin_y, radius, is_opaque):
    reveal(fog, origin_x, origin_y)
    for octant = 0 to 7:
        scan_octant(fog, origin_x, origin_y, radius, is_opaque,
                    octant, row=1, start_slope=1.0, end_slope=0.0)

function scan_octant(fog, ox, oy, radius, is_opaque,
                     octant, row, start_slope, end_slope):
    if start_slope < end_slope: return

    r2 = radius * radius
    next_start = start_slope
    xx, xy, yx, yy = octant_transforms[octant]

    for j = row to radius:
        blocked = false

        for i = -j to 0:
            // Slopes for inner and outer edges of this cell
            left_slope  = (i - 0.5) / (j + 0.5)
            right_slope = (i + 0.5) / (j - 0.5)

            if start_slope < right_slope: continue
            if end_slope > left_slope: break

            // Transform to map coordinates
            dx = i * xx + j * yx
            dy = i * xy + j * yy
            map_x = ox + dx
            map_y = oy + dy

            // Reveal if within radius
            if dx*dx + dy*dy <= r2 and in_bounds(map_x, map_y):
                reveal(fog, map_x, map_y)

            cell_opaque = not in_bounds(map_x, map_y) or is_opaque(map_x, map_y)

            if blocked:
                if cell_opaque:
                    next_start = right_slope
                else:
                    blocked = false
                    start_slope = next_start
            else if cell_opaque and j < radius:
                blocked = true
                scan_octant(fog, ox, oy, radius, is_opaque,
                            octant, j + 1, start_slope, right_slope)
                next_start = right_slope

        if blocked: break   // entire row was walls
```

**Performance:** O(r^2) worst case (open field) but skips large shadow regions. For a radius-20 source on a dungeon map, typically visits only 30--50% of cells. Fast enough for dozens of simultaneous sources.

---

## Vision Shapes

### Circular Vision

The default. Reveal everything within a radius. Handled by the radius parameter in shadowcasting.

### Cone-Shaped Vision (Stealth Games)

For enemies with a facing direction and limited field of view:

1. Compute full circular FOV into a temporary buffer
2. Filter: only reveal cells where the angle from the source falls within the cone

```
function compute_cone_fov(fog, ox, oy, range, direction, half_angle, is_opaque):
    temp_fog = new_fog_grid(fog.width, fog.height)
    compute_fov(temp_fog, ox, oy, range, is_opaque)

    for each (tx, ty) visible in temp_fog:
        dx = tx - ox
        dy = ty - oy
        angle = atan2(dy, dx)
        diff = angle_difference(angle, direction)
        if abs(diff) <= half_angle:
            reveal(fog, tx, ty)

function angle_difference(a, b):
    d = a - b
    while d >  PI: d -= 2*PI
    while d < -PI: d += 2*PI
    return d
```

### Multiple Vision Sources

In party-based or strategy games, run shadowcasting once per source. The reveal calls accumulate naturally since explored/visible arrays are combined:

```
function compute_team_vision(fog, sources, is_opaque):
    clear_visible(fog)
    for each (x, y, radius) in sources:
        compute_fov(fog, x, y, radius, is_opaque)
```

### Elevation Blocking

Tiles at higher elevation can block vision to tiles behind them. Integrate with your heightmap by making the opacity check consider elevation:

```
function is_opaque_with_elevation(x, y, viewer_elevation):
    if is_wall(x, y): return true
    return get_elevation(x, y) > viewer_elevation
```

---

## Dynamic Updates

### When to Recalculate

Fog updates are expensive relative to most per-frame work. Only recalculate when a vision source moves to a new tile:

```
for each vision_source:
    current_tile = world_to_tile(source.position)
    if current_tile != last_known_tile[source]:
        needs_update = true
```

### Update Throttling

For large maps with many units, throttle updates to every N frames or only when sources move.

**Practical advice:** Full clear + rebuild when something moves is correct and simple. On a 256x256 map with 20 vision sources of radius 12, shadowcasting takes about 0.5ms. Only optimize if profiling shows a bottleneck.

### Incremental Updates

For very large maps, you can track dirty regions rather than clearing the entire visibility array. However, overlapping vision sources make this error-prone (removing one source's contribution without affecting another's requires reference counting). Full rebuild is safer and simpler for most games.

---

## Fog Rendering

### The Naive Approach (Avoid)

Drawing a semi-transparent black rectangle per tile creates hard grid edges that break immersion.

### The Good Approach: Fog Texture + Blur

1. Render fog state to a **low-resolution texture** (one pixel per tile or 2x for smoother results)
2. Apply a **Gaussian blur** (two-pass separable: horizontal then vertical) to soften edges
3. Overlay the blurred fog texture on the world using alpha blending

### Render Pipeline Order

```
1. Draw world (terrain, entities)  ->  world render target
2. Build fog texture from fog grid  ->  fog render target
3. Blur fog texture (2-pass separable Gaussian)
4. Composite: apply fog over world render target
5. Draw HUD / minimap on top (unaffected by fog)
```

### Fog Compositing

Encode visibility states into the fog texture:

- Visible: white (255) -- no modification to the scene
- Explored: mid-gray (128) -- rendered darkened and desaturated
- Unexplored: black (0) -- rendered as solid black

Linear texture sampling naturally interpolates between states, producing smooth gradients at tile boundaries. The blur pass softens edges further.

### Explored Rendering

Explored but not visible areas should be:

- **Darkened** -- multiply brightness by 0.3--0.4
- **Desaturated** -- blend toward grayscale (use luminance: 0.299R + 0.587G + 0.114B) at about 30% saturation

This visually communicates "you saw this before but cannot see it now."

### Smooth Transitions

Use threshold checks with soft transitions rather than hard cutoffs. Example thresholds in a composite shader:

- fog value > 0.75: fully visible (no modification)
- fog value 0.25--0.75: explored (desaturate and darken)
- fog value < 0.25: unexplored (solid black)

The linear texture interpolation between adjacent tiles naturally produces gradients. Tune fog texture resolution (2x tiles for smoother results) and blur kernel size to taste.

---

## Entity Visibility

Entities (enemies, items, NPCs) must respect the fog:

- **Visible state:** Render normally at actual position. Update last-known info.
- **Explored state:** Show a "ghost" at last-known position (translucent, dimmed).
- **Unexplored state:** Do not render at all.

### Last-Known Information

Track per-entity:

- Last known position (tile coordinates)
- Last known sprite/appearance
- Time last seen

```
function get_entity_visibility(fog, entity_x, entity_y, previous_last_known, current_time):
    state = get_state(fog, entity_x, entity_y)

    if state == VISIBLE:
        // Currently in LOS -- fully visible, update last-known
        return { visible: true, last_known: { position: (entity_x, entity_y), time: current_time } }

    if state == EXPLORED and previous_last_known exists:
        // Show ghost at last known position
        return { visible: false, last_known: previous_last_known }

    // Unexplored -- completely hidden
    return { visible: false, last_known: null }
```

### Rendering Ghosts

When an entity was previously seen but is no longer in line of sight, render a translucent "ghost" sprite at the last-known position. Use a dimmed, semi-transparent tint to distinguish ghosts from live entities.

---

## Strategy Game Patterns

### Team-Based Shared Vision

All allied units and buildings contribute to a shared fog grid per team. Each team has its own fog grid instance. Run shadowcasting for every vision source on a team into that team's grid.

### Competitive Fog (Re-closing)

In StarCraft-style games, fog closes again when units leave. The three-state model handles this naturally:

- `clear_visible()` resets all cells to explored-or-unexplored each frame
- Only current line of sight re-promotes cells to visible
- Enemies in explored-but-not-visible areas are hidden and can move without the opponent knowing

### Building Sight Ranges

Different structures provide different vision radii (watchtower = 14, town hall = 10, barracks = 7, wall = 3). Use vision radius as a balancing lever.

### Co-op Shared Exploration

In co-op games, merge exploration across players. If Player A explored an area, Player B also sees it as explored. Implement by OR-merging each player's explored array into a shared team array.

### AI That Respects Fog

Enemy AI should not cheat by knowing the player's position through fog. AI systems should query their own team's fog grid before making decisions. When an AI loses sight of a target:

1. Switch from chasing to searching (move to last known position)
2. If target not found at last known position, return to patrol

---

## Minimap Integration

The minimap shows exploration progress using the same fog grid:

- **Visible tiles:** Show terrain color at full brightness
- **Explored tiles:** Show terrain color dimmed (40% brightness)
- **Unexplored tiles:** Show near-black

Only render entity dots on the minimap for entities in currently visible tiles.

---

## Performance Budget

| Concern | Approach | Complexity |
|---------|----------|------------|
| Small map, 1 source | Brute-force raycasting | O(r^3) |
| Any map, few sources | Shadowcasting | O(r^2) per source |
| RTS, many sources | Shadowcasting + throttle | O(n*r^2), skip unchanged |
| Smooth edges | Fog render target + Gaussian blur | 2 extra draw calls |
| Explored memory | Separate boolean array, never resets | Near-zero cost |
| Entity hiding | Query fog state before rendering | O(1) per entity |
| AI fairness | Per-team fog grids | Same as player fog |

### Approximate Timings

- Shadowcasting radius 12: approximately 50 microseconds per source
- 20 sources on a 256x256 map: approximately 1ms total
- Fog texture build + blur: approximately 0.3ms GPU
- Total is well within a 16.6ms (60fps) frame budget

---

---

## Related Engine Guides

- **MonoGame:** [G54 Fog of War](../../monogame-arch/guides/G54_fog_of_war.md) — Full ECS implementation with render targets, blur shaders, and entity ghost rendering
- **MonoGame:** [G39 2D Lighting](../../monogame-arch/guides/G39_2d_lighting.md) — Complementary lighting systems (deferred, normal maps) that interact with visibility
- **MonoGame:** [G40 Pathfinding](../../monogame-arch/guides/G40_pathfinding.md) — AI pathfinding that respects fog (last-known position, search behavior)
- **MonoGame:** [G58 Minimap](../../monogame-arch/guides/G58_minimap.md) — Minimap rendering with fog overlay integration
