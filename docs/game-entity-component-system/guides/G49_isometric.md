# G49 — Isometric Perspective


> **Category:** Guide · **Related:** [G28 3/4 Top-Down Perspective](./G28_top_down_perspective.md) · [G37 Tilemap Systems](./G37_tilemap_systems.md) · [G2 Rendering & Graphics](./G2_rendering_and_graphics.md) · [G20 Camera Systems](./G20_camera_systems.md) · [G40 Pathfinding](./G40_pathfinding.md)

---

## 1 — What Is Isometric?

Isometric projection renders a 3D world onto a 2D screen without perspective foreshortening. Every tile, no matter how far from the "camera," is the same size. This creates the distinctive diamond-grid look seen in classics like **Diablo**, **Baldur's Gate**, **Into the Breach**, and **Hades**.

### True Isometric vs Dimetric

| Property | True Isometric | Dimetric (2:1 Game Standard) |
|---|---|---|
| Angle from horizontal | 30° | 26.565° (arctan 0.5) |
| Pixel ratio (width:height) | ~1.73:1 | **2:1** |
| Tile shape | Slightly tall diamond | Clean 2:1 diamond |
| Pixel alignment | Fractional — causes shimmer | **Exact** — every pixel lands on integer |

**Why 2:1 dimetric won:** A 30° angle produces irrational coordinates (√3 everywhere). The 2:1 ratio (`arctan(0.5) ≈ 26.565°`) maps perfectly to pixel grids: for every 2 pixels horizontal, you move 1 pixel vertical. No sub-pixel jitter, no anti-aliasing needed on tile edges. Every game that calls itself "isometric" actually uses 2:1 dimetric.

### Visual Characteristics

- Tiles are **diamond-shaped** (rhombus) on screen
- The world grid is **rotated 45°** relative to the screen
- Objects farther "north" in the world appear higher on screen
- No vanishing point — parallel lines stay parallel
- Sprites facing the camera show a ¾ view (front + one side)

---

## 2 — Coordinate Systems

Two coordinate spaces matter:

- **World space (grid):** Integer `(col, row)` tile coordinates. `(0,0)` is the top corner of the diamond map. `col` increases to the right along the iso axis, `row` increases downward along the other iso axis.
- **Screen space (pixels):** `(screenX, screenY)` in pixels. `(0,0)` is the top-left of the viewport.

### The Projection

For a tile of dimensions `TileWidth × TileHeight` (e.g., 64×32 for 2:1):

```
screenX = (col - row) * (TileWidth  / 2)
screenY = (col + row) * (TileHeight / 2)
```

The inverse (screen → world):

```
col = (screenX / (TileWidth / 2) + screenY / (TileHeight / 2)) / 2
row = (screenY / (TileHeight / 2) - screenX / (TileWidth / 2)) / 2
```

### Tile Origin Convention

The **origin of a tile sprite** should be the **top-center of the diamond** — the point where the top two edges meet. This means the sprite is drawn offset by `(-TileWidth/2, 0)` from that anchor. All coordinate math targets this anchor point.

```csharp
/// <summary>Core isometric math. All methods are pure — no side effects.</summary>
public static class IsoMath
{
    public const int TileW = 64;
    public const int TileH = 32;
    public const int HalfW = TileW / 2; // 32
    public const int HalfH = TileH / 2; // 16

    /// <summary>Convert grid (col, row) to screen pixel position (top-center of diamond).</summary>
    public static Vector2 WorldToScreen(float col, float row)
    {
        float sx = (col - row) * HalfW;
        float sy = (col + row) * HalfH;
        return new Vector2(sx, sy);
    }

    /// <summary>Convert screen pixel to fractional grid coordinates.</summary>
    public static Vector2 ScreenToWorld(float sx, float sy)
    {
        float col = (sx / HalfW + sy / HalfH) * 0.5f;
        float row = (sy / HalfH - sx / HalfW) * 0.5f;
        return new Vector2(col, row);
    }

    /// <summary>Snap fractional grid coordinate to integer tile.</summary>
    public static Point SnapToTile(Vector2 world)
        => new((int)MathF.Floor(world.X), (int)MathF.Floor(world.Y));
}
```

---

## 3 — Diamond vs Staggered Layout

### Diamond Layout (Rotated Grid)

The entire map is rotated 45°. Tile `(0,0)` is at the top, and the map forms a diamond shape on screen. This is the most common approach for freely scrolling worlds.

```
        (0,0)
       /      \
    (0,1)    (1,0)
   /    \   /    \
(0,2)  (1,1)  (2,0)
```

**Math:** Exactly the `WorldToScreen` / `ScreenToWorld` above.

**Pros:** Clean coordinate math, easy camera control, natural for open-world maps.

### Staggered Layout

Tiles are arranged in offset rows (like brickwork). Even rows are shifted right by half a tile width. The map forms a rectangle on screen.

```
Row 0:  [0,0] [1,0] [2,0]
Row 1:    [0,1] [1,1] [2,1]    ← shifted right by HalfW
Row 2:  [0,2] [1,2] [2,2]
```

```csharp
public static Vector2 StaggeredToScreen(int col, int row)
{
    float sx = col * IsoMath.TileW + (row % 2 == 1 ? IsoMath.HalfW : 0);
    float sy = row * IsoMath.HalfH;
    return new Vector2(sx, sy);
}

public static Point StaggeredFromScreen(float sx, float sy)
{
    int row = (int)(sy / IsoMath.HalfH);
    float xOffset = (row % 2 == 1) ? IsoMath.HalfW : 0;
    int col = (int)((sx - xOffset) / IsoMath.TileW);
    return new Point(col, row);
}
```

**Pros:** Rectangular map bounds, simpler culling, natural for grid-based strategy games.
**Cons:** Odd/even row branching pollutes every calculation. Neighbor lookup is asymmetric.

### When to Use Which

| Scenario | Recommended |
|---|---|
| Open-world RPG / ARPG | Diamond |
| Turn-based tactics on rectangular board | Staggered |
| Scrolling with camera freedom | Diamond |
| Tile editor with rectangular map bounds | Staggered |

**This guide uses diamond layout for all remaining examples.**

---

## 4 — Tile Rendering

### ECS Components

```csharp
/// <summary>Marks an entity as occupying an isometric tile.</summary>
public record struct IsoTile(int Col, int Row, int Height = 0);

/// <summary>Tile visual: which sprite to draw.</summary>
public record struct IsoTileSprite(
    Texture2D Texture,
    Rectangle Source,
    int PixelHeight   // total sprite height (may exceed TileH for tall tiles)
);

/// <summary>An entity that exists in isometric space (not tile-locked).</summary>
public record struct IsoPosition(float Col, float Row, float Height);

/// <summary>Depth value computed each frame for sorting.</summary>
public record struct IsoDepth(float Value);

/// <summary>Renderable sprite for an iso entity.</summary>
public record struct IsoSprite(
    Texture2D Texture,
    Rectangle Source,
    Vector2 Origin      // sprite-local origin (feet position)
);
```

### Tile Dimensions

For 2:1 dimetric with a 64×32 base:
- Tile image is **64 px wide**, **32 px tall** (just the flat diamond)
- Tiles with vertical content (walls, trees) have sprites taller than 32 px — the extra height extends *upward* from the diamond top
- The sprite origin for drawing is `(TileW/2, PixelHeight - TileH)` — the top-center of the diamond portion

### Draw Order: Painter's Algorithm

In isometric view, tiles with higher `(col + row)` are "closer" to the camera and must be drawn **later** (on top). Within the same `(col + row)`, higher `col` is drawn later.

```csharp
/// <summary>Renders all iso tiles back-to-front.</summary>
public sealed class IsoTileRenderSystem
{
    private readonly QueryDescription _tileQuery = new QueryDescription()
        .WithAll<IsoTile, IsoTileSprite>();

    private readonly List<(IsoTile tile, IsoTileSprite sprite)> _sortBuffer = new();

    public void Render(World world, SpriteBatch batch, Vector2 cameraOffset)
    {
        _sortBuffer.Clear();

        world.Query(in _tileQuery, (ref IsoTile tile, ref IsoTileSprite spr) =>
        {
            _sortBuffer.Add((tile, spr));
        });

        // Sort: back-to-front by (col + row), then by col for ties
        _sortBuffer.Sort((a, b) =>
        {
            int sumA = a.tile.Col + a.tile.Row;
            int sumB = b.tile.Col + b.tile.Row;
            int cmp = sumA.CompareTo(sumB);
            return cmp != 0 ? cmp : a.tile.Col.CompareTo(b.tile.Col);
        });

        foreach (var (tile, spr) in _sortBuffer)
        {
            Vector2 screen = IsoMath.WorldToScreen(tile.Col, tile.Row);
            // Offset for height (each height unit lifts the tile visually)
            screen.Y -= tile.Height * IsoMath.HalfH;
            // Translate to sprite draw position (top-left corner of sprite image)
            screen.X -= IsoMath.HalfW;
            screen.Y -= (spr.PixelHeight - IsoMath.TileH);
            // Apply camera
            screen -= cameraOffset;

            batch.Draw(spr.Texture, screen, spr.Source, Color.White);
        }
    }
}
```

---

## 5 — Depth Sorting

Depth sorting is the single hardest problem in isometric rendering. Get it wrong and sprites pop in front of walls they should be behind.

### Basic Rule

```
depth = col + row
```

Entities with higher depth are closer to the camera. Draw low-depth first.

### Sorting Entities Among Tiles

Entities that move freely (players, NPCs) must be sorted *with* tiles, not in a separate pass. The depth system assigns a floating-point depth and everything — tiles and entities — goes into one sorted draw list.

```csharp
/// <summary>Computes depth for all iso-positioned entities each frame.</summary>
public sealed class IsoDepthSystem
{
    private readonly QueryDescription _entityQuery = new QueryDescription()
        .WithAll<IsoPosition, IsoDepth>();

    public void Update(World world)
    {
        world.Query(in _entityQuery, (ref IsoPosition pos, ref IsoDepth depth) =>
        {
            // Base depth from grid position
            // Subtract height so elevated entities sort above ground-level ones at same tile
            depth = new IsoDepth(pos.Col + pos.Row - pos.Height * 0.01f);
        });
    }
}
```

### Multi-Tile Objects

A building that spans 2×3 tiles can't have a single depth. The standard solution:

1. **Sort anchor:** Pick the tile closest to the camera (highest `col + row`) as the anchor. The entire object sorts at that depth.
2. **Footprint blocking:** All tiles in the footprint are marked as occupied for pathfinding but only the anchor drives sort order.

```csharp
/// <summary>A multi-tile object with an explicit sort anchor.</summary>
public record struct IsoFootprint(
    int AnchorCol,    // col of the front-most tile
    int AnchorRow,    // row of the front-most tile
    int Width,        // extent in col direction
    int Length         // extent in row direction
);
```

### Z-Height Sorting

When tiles have different elevations, depth becomes 3D:

```
depth = col + row + height * heightWeight
```

Choose `heightWeight` carefully. A value too small causes elevated tiles to sort behind ground tiles incorrectly. A value too large breaks lateral sorting. For most games, `heightWeight = 0.5` works — it interleaves height with position correctly when height steps are integer values.

### Sort-Stable Tie-Breaking

When two entities share the same depth, use a stable tiebreaker to prevent flicker:

```csharp
// In your unified sort:
int cmp = a.Depth.CompareTo(b.Depth);
if (cmp != 0) return cmp;
// Tie-break by col (entities further right draw later)
cmp = a.Col.CompareTo(b.Col);
if (cmp != 0) return cmp;
// Final tie-break by entity ID for stability
return a.EntityId.CompareTo(b.EntityId);
```

---

## 6 — Mouse Picking / Click Detection

### Step 1: Screen → World (Coarse)

Apply the inverse projection to get fractional grid coordinates:

```csharp
// screenPos is mouse position + camera offset (world-space screen pos)
Vector2 worldPos = IsoMath.ScreenToWorld(screenPos.X, screenPos.Y);
Point coarseTile = IsoMath.SnapToTile(worldPos);
```

This gives the correct tile most of the time, but the diamond edges are imprecise with floor-rounding alone.

### Step 2: Pixel-Perfect Picking

The coarse pick can be off by one tile at the diamond edges. To fix this, check which quadrant of the bounding rectangle the mouse falls in:

```csharp
public static class IsoPicker
{
    /// <summary>
    /// Returns the exact tile coordinate under the given world-space screen position.
    /// Uses sub-tile math: determines which quadrant of the tile's bounding box
    /// the point falls in, then checks if it's inside the diamond.
    /// </summary>
    public static Point Pick(Vector2 screenWorldPos)
    {
        // Coarse tile from inverse projection
        Vector2 fractional = IsoMath.ScreenToWorld(screenWorldPos.X, screenWorldPos.Y);
        int col = (int)MathF.Floor(fractional.X);
        int row = (int)MathF.Floor(fractional.Y);

        // Get the top-center screen position of this candidate tile
        Vector2 tileScreen = IsoMath.WorldToScreen(col, row);

        // Offset from tile's top-center (the diamond peak)
        float dx = screenWorldPos.X - tileScreen.X;
        float dy = screenWorldPos.Y - tileScreen.Y;

        // The diamond spans: x in [-HalfW, +HalfW], y in [0, TileH]
        // Diamond edges satisfy: |dx| / HalfW + dy / TileH <= 1  (for top half)
        //                    and: |dx| / HalfW + (TileH - dy) / TileH <= 1 (bottom)
        // Simplified: a point is inside if |dx| / HalfW + |dy - HalfH| / HalfH <= 1

        // But since the coarse pick is usually right, we just check the four corners.
        // If the point is in the top-left triangle, the real tile is (col-1, row).
        // Top-right → (col, row-1). Bottom-left → (col, row+1). Bottom-right → (col+1, row).

        // Normalize within the tile's bounding box [0..TileW, 0..TileH]
        float nx = dx + IsoMath.HalfW; // 0..TileW
        float ny = dy;                  // 0..TileH

        // Check if outside the diamond (in one of four corner triangles)
        // Top-left: nx/HalfW + (HalfH - ny)/HalfH > 1 when ny < HalfH
        if (ny < IsoMath.HalfH)
        {
            // Top half
            if (nx < IsoMath.HalfW)
            {
                // Top-left quadrant
                if (nx + ny * 2 < IsoMath.HalfW)
                    return new Point(col - 1, row); // neighbor to the upper-left
            }
            else
            {
                // Top-right quadrant
                if ((IsoMath.TileW - nx) + ny * 2 < IsoMath.HalfW)
                    return new Point(col, row - 1);
            }
        }
        else
        {
            // Bottom half
            float by = ny - IsoMath.HalfH;
            if (nx < IsoMath.HalfW)
            {
                // Bottom-left quadrant
                if (nx + (IsoMath.HalfH - by) * 2 < IsoMath.HalfW)
                    return new Point(col, row + 1);
            }
            else
            {
                // Bottom-right quadrant
                if ((IsoMath.TileW - nx) + (IsoMath.HalfH - by) * 2 < IsoMath.HalfW)
                    return new Point(col + 1, row);
            }
        }

        return new Point(col, row);
    }

    /// <summary>Returns fractional sub-tile position (0..1, 0..1) within the picked tile.</summary>
    public static Vector2 SubTilePosition(Vector2 screenWorldPos, Point tile)
    {
        Vector2 tileScreen = IsoMath.WorldToScreen(tile.X, tile.Y);
        float dx = screenWorldPos.X - tileScreen.X;
        float dy = screenWorldPos.Y - tileScreen.Y;
        // Map diamond space to unit square
        float u = (dx / IsoMath.HalfW + dy / IsoMath.HalfH) * 0.5f;
        float v = (dy / IsoMath.HalfH - dx / IsoMath.HalfW) * 0.5f;
        return new Vector2(
            Math.Clamp(u, 0f, 1f),
            Math.Clamp(v, 0f, 1f));
    }
}
```

---

## 7 — Isometric Movement

### The Diagonal Illusion

In isometric view, the grid axes are rotated 45°. What looks like "up" on screen is actually diagonal in world space (decreasing both col and row). Moving along a single world axis (increasing col only) appears as diagonal movement on screen (down-right).

### 8-Direction Mapping

Map player input directions to world-space deltas:

```csharp
/// <summary>Maps screen-apparent directions to world-space movement vectors.</summary>
public static class IsoDirection
{
    //                                        col    row
    public static readonly Vector2 North = new(-1f,  -1f);  // screen: up
    public static readonly Vector2 South = new( 1f,   1f);  // screen: down
    public static readonly Vector2 East  = new( 1f,  -1f);  // screen: right
    public static readonly Vector2 West  = new(-1f,   1f);  // screen: left
    public static readonly Vector2 NE    = new( 0f,  -1f);  // screen: up-right
    public static readonly Vector2 NW    = new(-1f,   0f);  // screen: up-left
    public static readonly Vector2 SE    = new( 1f,   0f);  // screen: down-right
    public static readonly Vector2 SW    = new( 0f,   1f);  // screen: down-left

    /// <summary>All 8 directions for iteration.</summary>
    public static readonly Vector2[] All = { North, NE, East, SE, South, SW, West, NW };
}
```

### Speed Correction

Cardinal screen directions (N/S/E/W) map to diagonal world movement — they traverse `√2` world units per step. Axis-aligned screen diagonals (NE/NW/SE/SW) map to single-axis world movement — exactly 1 unit per step.

To make on-screen speed consistent, normalize the direction vector:

```csharp
public record struct IsoVelocity(float Speed);

public sealed class IsoMovementSystem
{
    private readonly QueryDescription _query = new QueryDescription()
        .WithAll<IsoPosition, IsoVelocity>();

    public void Update(World world, Vector2 inputDir, float dt)
    {
        if (inputDir == Vector2.Zero) return;
        inputDir = Vector2.Normalize(inputDir);

        world.Query(in _query, (ref IsoPosition pos, ref IsoVelocity vel) =>
        {
            pos = new IsoPosition(
                pos.Col + inputDir.X * vel.Speed * dt,
                pos.Row + inputDir.Y * vel.Speed * dt,
                pos.Height);
        });
    }
}
```

---

## 8 — Elevation / Height

### Height Model

Each tile stores an integer `Height`. One height unit equals `HalfH` pixels of vertical screen offset (16 px for 64×32 tiles). A cliff that is 2 units tall lifts the tile surface 32 px — exactly one tile height.

### Rendering with Height

```csharp
public static Vector2 WorldToScreenWithHeight(float col, float row, float height)
{
    Vector2 screen = IsoMath.WorldToScreen(col, row);
    screen.Y -= height * IsoMath.HalfH;
    return screen;
}
```

### Visual Elements

| Element | Implementation |
|---|---|
| **Flat elevated tile** | Same tile sprite, drawn higher by `height * HalfH` |
| **Cliff face** | Separate sprite drawn between the elevated tile and ground level |
| **Stairs / Ramp** | Sprite blending between two heights; walkable at fractional heights |
| **Walls on elevated tiles** | Drawn at tile position with height offset baked in |

### Sorting with Height

Elevated tiles must sort correctly against ground tiles. A tile at `(3, 2, height=2)` should draw in front of a ground tile at `(3, 3, height=0)` — the elevated tile is visually "above" but spatially "behind."

The depth formula accounts for this:

```csharp
float depth = col + row;
// Height adjusts depth slightly so tall tiles on the same row sort above ground tiles
// but don't break the fundamental back-to-front order
float adjustedDepth = depth - height * 0.001f;
```

The key insight: **height affects screen Y but should barely affect sort depth.** Height doesn't move an object closer or farther from the camera — it moves it up. Use a tiny height offset for tie-breaking only.

### Jumping Between Elevations

```csharp
/// <summary>Tracks vertical movement for jumping/falling.</summary>
public record struct IsoVertical(float VelocityZ, bool Grounded);

public sealed class IsoGravitySystem
{
    private const float Gravity = 20f;

    private readonly QueryDescription _query = new QueryDescription()
        .WithAll<IsoPosition, IsoVertical>();

    public void Update(World world, float dt)
    {
        world.Query(in _query, (ref IsoPosition pos, ref IsoVertical vert) =>
        {
            if (vert.Grounded) return;

            float newVelZ = vert.VelocityZ - Gravity * dt;
            float newHeight = pos.Height + newVelZ * dt;

            // TODO: check ground height at (pos.Col, pos.Row) from tilemap
            float groundHeight = 0f; // placeholder

            if (newHeight <= groundHeight)
            {
                newHeight = groundHeight;
                newVelZ = 0f;
                vert = new IsoVertical(0f, true);
            }
            else
            {
                vert = new IsoVertical(newVelZ, false);
            }

            pos = new IsoPosition(pos.Col, pos.Row, newHeight);
        });
    }
}
```

---

## 9 — Isometric Pathfinding

### A* on Diamond Grids

The grid is a normal 2D grid — pathfinding doesn't care about the visual projection. The difference is in **neighbor definitions** and **cost calculations**.

### Neighbors for Diamond Grids

Each tile has up to 8 neighbors (4 cardinal + 4 diagonal in grid space):

```csharp
public static class IsoGrid
{
    /// <summary>Cardinal and diagonal neighbor offsets in grid space.</summary>
    public static readonly Point[] Neighbors8 =
    {
        new( 0, -1), // grid-north   (screen: up-right)
        new( 1,  0), // grid-east    (screen: down-right)
        new( 0,  1), // grid-south   (screen: down-left)
        new(-1,  0), // grid-west    (screen: up-left)
        new( 1, -1), // grid-NE      (screen: right)
        new( 1,  1), // grid-SE      (screen: down)
        new(-1,  1), // grid-SW      (screen: left)
        new(-1, -1), // grid-NW      (screen: up)
    };

    /// <summary>4-neighbor variant for games that restrict diagonal movement.</summary>
    public static readonly Point[] Neighbors4 =
    {
        new( 0, -1), new( 1, 0), new( 0, 1), new(-1, 0)
    };
}
```

### Height-Aware Pathfinding

```csharp
/// <summary>Isometric tilemap for pathfinding queries.</summary>
public sealed class IsoTilemap
{
    public int Width  { get; }
    public int Height { get; }

    private readonly int[,] _heights;      // elevation per tile
    private readonly bool[,] _walkable;    // passability

    public IsoTilemap(int width, int height)
    {
        Width = width;
        Height = height;
        _heights = new int[width, height];
        _walkable = new bool[width, height];
    }

    public bool InBounds(int col, int row)
        => col >= 0 && col < Width && row >= 0 && row < Height;

    public bool IsWalkable(int col, int row)
        => InBounds(col, row) && _walkable[col, row];

    public int GetHeight(int col, int row)
        => InBounds(col, row) ? _heights[col, row] : 0;

    public void Set(int col, int row, int height, bool walkable)
    {
        _heights[col, row] = height;
        _walkable[col, row] = walkable;
    }
}

/// <summary>A* pathfinder that respects elevation.</summary>
public sealed class IsoPathfinder
{
    private const int MaxClimbHeight = 1;  // max elevation change per step
    private const float DiagonalCost = 1.414f;
    private const float CardinalCost = 1.0f;
    private const float ClimbCostMultiplier = 1.5f;

    private readonly IsoTilemap _map;

    public IsoPathfinder(IsoTilemap map) => _map = map;

    public List<Point>? FindPath(Point start, Point goal)
    {
        if (!_map.IsWalkable(goal.X, goal.Y)) return null;

        var open = new PriorityQueue<Point, float>();
        var cameFrom = new Dictionary<Point, Point>();
        var gScore = new Dictionary<Point, float> { [start] = 0 };

        open.Enqueue(start, Heuristic(start, goal));

        while (open.Count > 0)
        {
            Point current = open.Dequeue();
            if (current == goal) return ReconstructPath(cameFrom, current);

            float currentG = gScore[current];
            int currentHeight = _map.GetHeight(current.X, current.Y);

            for (int i = 0; i < IsoGrid.Neighbors8.Length; i++)
            {
                var offset = IsoGrid.Neighbors8[i];
                Point neighbor = new(current.X + offset.X, current.Y + offset.Y);

                if (!_map.IsWalkable(neighbor.X, neighbor.Y)) continue;

                int neighborHeight = _map.GetHeight(neighbor.X, neighbor.Y);
                int heightDiff = Math.Abs(neighborHeight - currentHeight);

                // Can't climb/drop more than MaxClimbHeight in one step
                if (heightDiff > MaxClimbHeight) continue;

                // Diagonal neighbors (indices 4-7) cost more
                float baseCost = i < 4 ? CardinalCost : DiagonalCost;
                float climbCost = heightDiff * ClimbCostMultiplier;
                float tentativeG = currentG + baseCost + climbCost;

                if (!gScore.TryGetValue(neighbor, out float existingG) || tentativeG < existingG)
                {
                    gScore[neighbor] = tentativeG;
                    cameFrom[neighbor] = current;
                    float f = tentativeG + Heuristic(neighbor, goal);
                    open.Enqueue(neighbor, f);
                }
            }
        }

        return null; // no path found
    }

    private static float Heuristic(Point a, Point b)
    {
        // Chebyshev distance — appropriate for 8-connected grid
        int dx = Math.Abs(a.X - b.X);
        int dy = Math.Abs(a.Y - b.Y);
        return Math.Max(dx, dy) + (DiagonalCost - 1f) * Math.Min(dx, dy);
    }

    private static List<Point> ReconstructPath(Dictionary<Point, Point> cameFrom, Point current)
    {
        var path = new List<Point> { current };
        while (cameFrom.ContainsKey(current))
        {
            current = cameFrom[current];
            path.Add(current);
        }
        path.Reverse();
        return path;
    }
}
```

---

## 10 — Camera for Isometric

### Isometric Camera Bounds

The camera stores its position in **screen space** (pixel offset). To center on a world tile, convert the tile to screen coordinates and offset by half the viewport:

```csharp
public record struct IsoCameraState(
    Vector2 Position,     // screen-space offset (top-left of viewport)
    float Zoom,
    int ViewportWidth,
    int ViewportHeight);

public sealed class IsoCameraSystem
{
    /// <summary>Center the camera on a world-space grid position.</summary>
    public static IsoCameraState CenterOn(
        IsoCameraState cam, float col, float row, float height = 0f)
    {
        Vector2 target = IsoMath.WorldToScreen(col, row);
        target.Y -= height * IsoMath.HalfH;
        target.X -= cam.ViewportWidth  / (2f * cam.Zoom);
        target.Y -= cam.ViewportHeight / (2f * cam.Zoom);
        return cam with { Position = target };
    }

    /// <summary>Clamp camera to isometric map bounds.</summary>
    public static IsoCameraState Clamp(IsoCameraState cam, int mapCols, int mapRows)
    {
        // Compute the four extreme screen positions of the map diamond
        Vector2 top    = IsoMath.WorldToScreen(0, 0);
        Vector2 right  = IsoMath.WorldToScreen(mapCols, 0);
        Vector2 bottom = IsoMath.WorldToScreen(mapCols, mapRows);
        Vector2 left   = IsoMath.WorldToScreen(0, mapRows);

        float minX = left.X  - IsoMath.HalfW;
        float maxX = right.X + IsoMath.HalfW - cam.ViewportWidth / cam.Zoom;
        float minY = top.Y;
        float maxY = bottom.Y + IsoMath.TileH - cam.ViewportHeight / cam.Zoom;

        Vector2 clamped = new(
            Math.Clamp(cam.Position.X, minX, Math.Max(minX, maxX)),
            Math.Clamp(cam.Position.Y, minY, Math.Max(minY, maxY)));

        return cam with { Position = clamped };
    }

    /// <summary>Build the view transform matrix for SpriteBatch.</summary>
    public static Matrix GetViewMatrix(IsoCameraState cam)
    {
        return Matrix.CreateTranslation(-cam.Position.X, -cam.Position.Y, 0)
             * Matrix.CreateScale(cam.Zoom, cam.Zoom, 1f);
    }
}
```

### Tile Culling

Only render tiles visible in the viewport. Transform the viewport corners to world space and iterate the bounding range:

```csharp
public static Rectangle GetVisibleTileRange(IsoCameraState cam)
{
    float invZoom = 1f / cam.Zoom;
    // Four viewport corners in world-screen space
    Vector2 tl = cam.Position;
    Vector2 br = cam.Position + new Vector2(
        cam.ViewportWidth * invZoom, cam.ViewportHeight * invZoom);

    // Convert to world coords and expand by 2 tiles for margin
    Vector2 wTL = IsoMath.ScreenToWorld(tl.X, tl.Y);
    Vector2 wBR = IsoMath.ScreenToWorld(br.X, br.Y);
    // Also check the other two corners for the full diamond range
    Vector2 wTR = IsoMath.ScreenToWorld(br.X, tl.Y);
    Vector2 wBL = IsoMath.ScreenToWorld(tl.X, br.Y);

    int minCol = (int)MathF.Floor(MathF.Min(MathF.Min(wTL.X, wBR.X), MathF.Min(wTR.X, wBL.X))) - 2;
    int maxCol = (int)MathF.Ceiling(MathF.Max(MathF.Max(wTL.X, wBR.X), MathF.Max(wTR.X, wBL.X))) + 2;
    int minRow = (int)MathF.Floor(MathF.Min(MathF.Min(wTL.Y, wBR.Y), MathF.Min(wTR.Y, wBL.Y))) - 2;
    int maxRow = (int)MathF.Ceiling(MathF.Max(MathF.Max(wTL.Y, wBR.Y), MathF.Max(wTR.Y, wBL.Y))) + 2;

    return new Rectangle(minCol, minRow, maxCol - minCol, maxRow - minRow);
}
```

### Minimap

The minimap is just the isometric projection drawn at a tiny scale. Each tile becomes a single colored pixel or small diamond:

```csharp
public static void DrawMinimap(
    SpriteBatch batch, IsoTilemap map, Texture2D pixel,
    Vector2 minimapPos, float scale, IsoCameraState cam)
{
    for (int r = 0; r < map.Height; r++)
    for (int c = 0; c < map.Width; c++)
    {
        if (!map.IsWalkable(c, r)) continue;
        Vector2 screen = IsoMath.WorldToScreen(c, r) * scale;
        Color color = map.GetHeight(c, r) > 0 ? Color.Gray : Color.DarkGreen;
        batch.Draw(pixel, minimapPos + screen, null, color, 0,
            Vector2.Zero, scale * 2f, SpriteEffects.None, 0);
    }

    // Draw camera viewport rectangle on minimap
    Vector2 camWorld = IsoMath.ScreenToWorld(cam.Position.X, cam.Position.Y);
    Vector2 camMini = IsoMath.WorldToScreen(camWorld.X, camWorld.Y) * scale + minimapPos;
    // (Draw rectangle outline at camMini with appropriate size)
}
```

---

## 11 — Common Pitfalls

### Seam Artifacts Between Tiles

**Symptom:** 1-pixel gaps or lines visible between tiles, especially during scrolling or at certain zoom levels.

**Causes and fixes:**
- **Floating-point positions:** Round draw positions to integers: `new Vector2(MathF.Round(x), MathF.Round(y))`
- **Texture filtering:** Use `SamplerState.PointClamp` in your `SpriteBatch.Begin()` call — linear filtering bleeds adjacent texels
- **Tile atlas bleeding:** Add 1–2 px padding between tiles in your sprite atlas. Use `Rectangle` source rects that don't touch atlas edges
- **Camera sub-pixel:** Snap camera position to integer pixels before building the view matrix

### Sorting Edge Cases with Large Sprites

**Problem:** A tall tree sprite at `(3, 2)` visually overlaps tile `(3, 3)` but sorts behind it.

**Fix:** For sprites taller than one tile, offset the sort depth forward. A common approach: sort by the entity's *feet position*, not its visual center. The `IsoDepth` calculation already does this if `IsoPosition` represents the entity's ground contact point.

For very large sprites (buildings spanning many tiles), the footprint/anchor approach from §5 is necessary. There is no universal automatic solution — level designers must set correct anchors.

### Off-By-One in Coordinate Conversion

**Symptom:** Mouse picking selects the wrong tile at diamond edges.

**Root cause:** `(int)` cast truncates toward zero, but `MathF.Floor()` truncates toward negative infinity. For negative coordinates, these differ:
- `(int)(-0.5f)` → `0` ❌
- `(int)MathF.Floor(-0.5f)` → `-1` ✅

**Rule:** Always use `MathF.Floor()` for world-to-tile snapping.

### Performance with Large Visible Tile Counts

A 1920×1080 viewport with 64×32 tiles can show ~2000 tiles simultaneously. With entities and multi-layer rendering, draw call count explodes.

**Mitigations:**
- **Tile culling** (§10): Only iterate visible tiles. Don't query the entire map
- **SpriteBatch batching:** Use a single `SpriteBatch.Begin/End` with one texture atlas. Every atlas switch forces a new batch
- **Hybrid sorting:** Sort tiles by chunk (16×16 groups), only re-sort chunks when entities move between them
- **Pre-baked ground layer:** Render static ground tiles to a `RenderTarget2D` and only redraw when the camera moves significantly. Draw entities on top
- **LOD for zoom-out:** At low zoom levels, swap detailed tiles for simplified versions or a pre-rendered minimap

### Tile Dimensions Don't Match Art

If your artist delivers 128×64 tiles but your math uses 64×32, everything renders at double scale with wrong picking. `TileW` and `TileH` in `IsoMath` must match the pixel dimensions of the **diamond portion** of your tile art. Tall sprites (walls, trees) have extra height *above* the diamond — that height is tracked separately in `PixelHeight`.

---

## Quick Reference

```
World → Screen:  sx = (col - row) * HalfW      sy = (col + row) * HalfH
Screen → World:  col = (sx/HalfW + sy/HalfH)/2   row = (sy/HalfH - sx/HalfW)/2
Depth:           col + row  (higher = closer to camera = draw later)
Height offset:   screenY -= height * HalfH
Diamond test:    |dx|/HalfW + |dy - HalfH|/HalfH <= 1
```

---

*See also: [G37 Tilemap Systems](./G37_tilemap_systems.md) for general tile storage, [G40 Pathfinding](./G40_pathfinding.md) for A\* details, [G20 Camera Systems](./G20_camera_systems.md) for camera fundamentals, [G28 3/4 Top-Down Perspective](./G28_top_down_perspective.md) for the non-isometric alternative.*
