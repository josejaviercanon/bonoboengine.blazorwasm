# G54 — Fog of War & Visibility Systems



> **Category:** Guide · **Related:** [G39 2D Lighting](./G39_2d_lighting.md) · [G37 Tilemap Systems](./G37_tilemap_systems.md) · [G2 Rendering & Graphics](./G2_rendering_and_graphics.md) · [G27 Shaders & Visual Effects](./G27_shaders_and_effects.md) · [G40 Pathfinding](./G40_pathfinding.md)

Fog of war hides parts of the map the player hasn't seen or can't currently see. It's essential for strategy games (RTS, 4X), roguelikes, stealth games, and any game where information is power. This guide covers tile-based visibility tracking, efficient shadowcasting, smooth fog rendering with shaders, and full ECS integration.

---

## 1 — Visibility States

Every cell on the map exists in one of three states:

| State | Visual | Gameplay |
|---|---|---|
| **Unexplored** | Solid black | Player has never seen this tile. No information. |
| **Explored** | Dark / desaturated | Previously seen. Terrain visible, but entities hidden. |
| **Visible** | Fully lit | Currently in line of sight. Everything revealed. |

```csharp
public enum VisibilityState : byte
{
    Unexplored = 0,  // Never seen — render black
    Explored   = 1,  // Previously seen — render dimmed/desaturated
    Visible    = 2   // Currently in LOS — render fully
}
```

### The Fog Grid

A flat array stores per-cell state. Two layers: the **current frame** visibility (recalculated each update) and the **persistent explored** flag (never reverts to unexplored).

```csharp
public class FogGrid
{
    public int Width  { get; }
    public int Height { get; }

    // Current-frame visibility (cleared each update, then rebuilt)
    private readonly bool[] _visible;

    // Persistent — once true, stays true
    private readonly bool[] _explored;

    public FogGrid(int width, int height)
    {
        Width    = width;
        Height   = height;
        _visible  = new bool[width * height];
        _explored = new bool[width * height];
    }

    public VisibilityState this[int x, int y]
    {
        get
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height)
                return VisibilityState.Unexplored;
            int i = y * Width + x;
            if (_visible[i])  return VisibilityState.Visible;
            if (_explored[i]) return VisibilityState.Explored;
            return VisibilityState.Unexplored;
        }
    }

    public void ClearVisible() => Array.Clear(_visible, 0, _visible.Length);

    public void Reveal(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height) return;
        int i = y * Width + x;
        _visible[i]  = true;
        _explored[i] = true;   // Permanent
    }

    public bool IsVisible(int x, int y)  => InBounds(x, y) && _visible[y * Width + x];
    public bool IsExplored(int x, int y) => InBounds(x, y) && _explored[y * Width + x];
    public bool InBounds(int x, int y)   => x >= 0 && x < Width && y >= 0 && y < Height;
}
```

> **Key invariant:** `Visible` implies `Explored`. Once a cell is explored it never goes back to unexplored. The `_visible` array is transient — cleared and rebuilt every time vision sources move.

---

## 2 — Line-of-Sight (Raycasting)

The simplest visibility approach: cast a ray from the viewer to every candidate tile using **Bresenham's line algorithm**. If the ray hits a wall before reaching the target, the target is not visible.

### Bresenham's Line

```csharp
public static class Bresenham
{
    /// Walks from (x0,y0) to (x1,y1). Returns false if blocked.
    public static bool LineOfSight(
        int x0, int y0, int x1, int y1, Func<int, int, bool> isOpaque)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;

        while (true)
        {
            // Don't check the source tile itself
            if ((x0 != x1 || y0 != y1) == false)
                return true; // Reached target

            if (x0 == x1 && y0 == y1) return true;

            if (isOpaque(x0, y0) && !(x0 == x1 && y0 == y1))
                return false; // Blocked

            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x0 += sx; }
            if (e2 <  dx) { err += dx; y0 += sy; }
        }
    }
}
```

### Brute-Force Raycast FOV

Cast rays to every tile within the vision radius:

```csharp
public static void RaycastFOV(
    FogGrid fog, int cx, int cy, int radius, Func<int, int, bool> isOpaque)
{
    int r2 = radius * radius;
    for (int dy = -radius; dy <= radius; dy++)
    for (int dx = -radius; dx <= radius; dx++)
    {
        if (dx * dx + dy * dy > r2) continue;
        int tx = cx + dx, ty = cy + dy;
        if (!fog.InBounds(tx, ty)) continue;

        if (Bresenham.LineOfSight(cx, cy, tx, ty, isOpaque))
            fog.Reveal(tx, ty);
    }
}
```

### Symmetric vs Asymmetric Visibility

- **Asymmetric:** If A can see B, B might not see able to see A (ray path differs due to Bresenham stepping). This is the default.
- **Symmetric:** If A sees B, then B sees A. Requires checking both directions or using symmetric algorithms like **shadowcasting** or **diamond walls**.

For multiplayer fairness, prefer symmetric visibility. For single-player roguelikes, asymmetric is fine and cheaper.

> **Performance note:** Brute-force raycasting is O(r³) — for radius 10, that's ~1200 rays with up to 10 steps each. Fine for a single player. Terrible for 50 RTS units. Use shadowcasting instead.

---

## 3 — Shadowcasting Algorithm

Recursive shadowcasting is the gold standard for roguelike/strategy FOV. It processes the map in **8 octants**, scanning row-by-row outward from the source. Walls create shadow regions that are skipped entirely, making it dramatically faster than raycasting.

### How It Works

1. Divide the circle around the viewer into 8 octants (45° each).
2. For each octant, scan columns (or rows) moving outward.
3. Track a "slope window" `[startSlope, endSlope]` — the visible arc.
4. When you hit a wall, the visible arc shrinks. When a wall ends, recurse with the new window.

### Full Implementation

```csharp
public static class Shadowcast
{
    // Multipliers for the 8 octants — transforms (col, row) into (dx, dy)
    private static readonly int[,] OctantTransform = {
        // col-dx, col-dy, row-dx, row-dy
        {  1,  0,  0,  1 },  // octant 0: E-NE
        {  0,  1,  1,  0 },  // octant 1: N-NE
        {  0, -1,  1,  0 },  // octant 2: N-NW
        { -1,  0,  0,  1 },  // octant 3: W-NW
        { -1,  0,  0, -1 },  // octant 4: W-SW
        {  0, -1, -1,  0 },  // octant 5: S-SW
        {  0,  1, -1,  0 },  // octant 6: S-SE
        {  1,  0,  0, -1 },  // octant 7: E-SE
    };

    public static void ComputeFOV(
        FogGrid fog, int originX, int originY, int radius,
        Func<int, int, bool> isOpaque)
    {
        // Origin is always visible
        fog.Reveal(originX, originY);

        for (int octant = 0; octant < 8; octant++)
        {
            ScanOctant(fog, originX, originY, radius, isOpaque,
                       octant, 1, 1.0f, 0.0f);
        }
    }

    private static void ScanOctant(
        FogGrid fog, int ox, int oy, int radius,
        Func<int, int, bool> isOpaque,
        int octant, int row, float startSlope, float endSlope)
    {
        if (startSlope < endSlope) return;

        int r2 = radius * radius;
        float nextStart = startSlope;

        int xx = OctantTransform[octant, 0];
        int xy = OctantTransform[octant, 1];
        int yx = OctantTransform[octant, 2];
        int yy = OctantTransform[octant, 3];

        for (int j = row; j <= radius; j++)
        {
            bool blocked = false;

            for (int i = -j; i <= 0; i++)
            {
                // Slopes for the inner and outer edges of this cell
                float leftSlope  = (i - 0.5f) / (j + 0.5f);
                float rightSlope = (i + 0.5f) / (j - 0.5f);

                if (startSlope < rightSlope) continue;
                if (endSlope > leftSlope) break;

                // Transform octant-local (i, j) → map (dx, dy)
                int dx = i * xx + j * yx;
                int dy = i * xy + j * yy;
                int mapX = ox + dx;
                int mapY = oy + dy;

                // Within radius?
                if (dx * dx + dy * dy <= r2 && fog.InBounds(mapX, mapY))
                    fog.Reveal(mapX, mapY);

                bool cellOpaque = !fog.InBounds(mapX, mapY) || isOpaque(mapX, mapY);

                if (blocked)
                {
                    if (cellOpaque)
                    {
                        // Still in shadow — update start slope
                        nextStart = rightSlope;
                    }
                    else
                    {
                        // Emerged from wall — begin new scan
                        blocked = false;
                        startSlope = nextStart;
                    }
                }
                else if (cellOpaque && j < radius)
                {
                    // Entering a wall — recurse with narrowed window, then mark blocked
                    blocked = true;
                    ScanOctant(fog, ox, oy, radius, isOpaque,
                               octant, j + 1, startSlope, rightSlope);
                    nextStart = rightSlope;
                }
            }

            if (blocked) break; // Entire row was walls — done with this octant
        }
    }
}
```

**Performance:** Shadowcasting is O(r²) in the worst case (open field) but skips large shadow regions entirely. For a radius-20 source on a dungeon map, it typically visits only 30-50% of cells. Fast enough for dozens of simultaneous sources.

---

## 4 — Vision Range & Shape

### Circular Vision

The default — reveal everything within a radius. Already handled by the `radius` parameter in `Shadowcast.ComputeFOV`.

### Cone-Shaped Vision (Stealth Games)

For stealth games where enemies have a facing direction and limited field of view:

```csharp
public record struct VisionCone(
    float Direction,    // Radians, 0 = right
    float HalfAngle,    // Half the cone width in radians (e.g. π/4 for 90° cone)
    int   Range
);

public static void ComputeConeFOV(
    FogGrid fog, int ox, int oy, VisionCone cone,
    Func<int, int, bool> isOpaque)
{
    // First compute full circular FOV into a temp buffer
    var tempFog = new FogGrid(fog.Width, fog.Height);
    Shadowcast.ComputeFOV(tempFog, ox, oy, cone.Range, isOpaque);

    // Then filter: only reveal cells within the cone angle
    int r = cone.Range;
    for (int dy = -r; dy <= r; dy++)
    for (int dx = -r; dx <= r; dx++)
    {
        int tx = ox + dx, ty = oy + dy;
        if (!tempFog.IsVisible(tx, ty)) continue;

        float angle = MathF.Atan2(dy, dx);
        float diff  = MathF.Abs(AngleDiff(angle, cone.Direction));
        if (diff <= cone.HalfAngle)
            fog.Reveal(tx, ty);
    }
}

private static float AngleDiff(float a, float b)
{
    float d = a - b;
    while (d >  MathF.PI) d -= MathF.Tau;
    while (d < -MathF.PI) d += MathF.Tau;
    return d;
}
```

### Multiple Vision Sources

In party-based or strategy games, many entities contribute to the visible area. Simply run shadowcasting once per source — the `Reveal` calls accumulate:

```csharp
public static void ComputeTeamVision(
    FogGrid fog, ReadOnlySpan<(int X, int Y, int Radius)> sources,
    Func<int, int, bool> isOpaque)
{
    fog.ClearVisible();
    foreach (var (x, y, r) in sources)
        Shadowcast.ComputeFOV(fog, x, y, r, isOpaque);
}
```

### Elevation Blocking

Tiles at higher elevation can block vision to tiles behind them. Integrate with your heightmap:

```csharp
// An isOpaque function that considers elevation
bool IsOpaqueWithElevation(int x, int y, int viewerElevation)
{
    if (IsWall(x, y)) return true;
    return GetElevation(x, y) > viewerElevation; // Hills block LOS for ground units
}
```

---

## 5 — Fog Rendering

### The Naive Approach (Don't)

Drawing a semi-transparent black rectangle per tile works but looks terrible — hard grid edges break immersion.

### The Good Approach: Fog RenderTarget + Blur

1. Render fog state to a **low-resolution RenderTarget** (one pixel per tile or 2× for smoother results).
2. Apply a **blur pass** to soften edges.
3. Overlay the blurred fog texture on the world using alpha blending.

```csharp
public class FogRenderer : IDisposable
{
    private RenderTarget2D _fogTarget;
    private RenderTarget2D _fogBlurred;
    private Effect         _blurEffect;
    private Effect         _fogComposite;
    private readonly int   _tileSize;

    public FogRenderer(GraphicsDevice gd, int mapWidth, int mapHeight,
                       int tileSize, Effect blurEffect, Effect fogComposite)
    {
        _tileSize = tileSize;
        // 1 pixel per tile for the fog mask
        _fogTarget  = new RenderTarget2D(gd, mapWidth, mapHeight,
            false, SurfaceFormat.Color, DepthFormat.None);
        _fogBlurred = new RenderTarget2D(gd, mapWidth, mapHeight,
            false, SurfaceFormat.Color, DepthFormat.None);
        _blurEffect    = blurEffect;
        _fogComposite  = fogComposite;
    }

    /// Build the raw fog texture from the FogGrid.
    public void BuildFogTexture(GraphicsDevice gd, FogGrid fog)
    {
        var pixels = new Color[fog.Width * fog.Height];
        for (int y = 0; y < fog.Height; y++)
        for (int x = 0; x < fog.Width;  x++)
        {
            var state = fog[x, y];
            // R channel = visibility (0 = black, 128 = explored, 255 = visible)
            // We encode state into the red channel; shader interprets it
            byte val = state switch
            {
                VisibilityState.Visible  => 255,
                VisibilityState.Explored => 128,
                _                        => 0
            };
            pixels[y * fog.Width + x] = new Color(val, val, val, 255);
        }
        _fogTarget.SetData(pixels);
    }

    /// Apply a Gaussian blur to soften tile edges.
    public void BlurFog(GraphicsDevice gd, SpriteBatch sb)
    {
        // Horizontal pass → _fogBlurred
        gd.SetRenderTarget(_fogBlurred);
        gd.Clear(Color.Black);
        _blurEffect.Parameters["TexelSize"]?.SetValue(
            new Vector2(1f / _fogTarget.Width, 0));
        sb.Begin(effect: _blurEffect, samplerState: SamplerState.LinearClamp);
        sb.Draw(_fogTarget, _fogTarget.Bounds, Color.White);
        sb.End();

        // Vertical pass → _fogTarget (ping-pong)
        gd.SetRenderTarget(_fogTarget);
        gd.Clear(Color.Black);
        _blurEffect.Parameters["TexelSize"]?.SetValue(
            new Vector2(0, 1f / _fogBlurred.Height));
        sb.Begin(effect: _blurEffect, samplerState: SamplerState.LinearClamp);
        sb.Draw(_fogBlurred, _fogBlurred.Bounds, Color.White);
        sb.End();

        gd.SetRenderTarget(null);
    }

    /// Draw the fog overlay on top of the world.
    public void DrawFog(SpriteBatch sb, Rectangle worldBounds)
    {
        // The fog composite shader handles desaturation + darkening
        sb.Begin(
            effect: _fogComposite,
            blendState: BlendState.AlphaBlend,
            samplerState: SamplerState.LinearClamp);
        sb.Draw(_fogTarget, worldBounds, Color.White);
        sb.End();
    }

    public void Dispose()
    {
        _fogTarget?.Dispose();
        _fogBlurred?.Dispose();
    }
}
```

### Render Pipeline Order

```
1. Draw world (terrain, objects)  →  to worldTarget
2. Build fog texture from FogGrid  →  _fogTarget
3. Blur fog texture (2-pass Gaussian)
4. Composite: apply fog shader over worldTarget
5. Draw HUD / minimap on top
```

---

## 6 — Fog Shader (HLSL)

### Gaussian Blur Shader

```hlsl
// FogBlur.fx — Simple separable Gaussian blur
sampler TextureSampler : register(s0);

float2 TexelSize; // (1/width, 0) for horizontal, (0, 1/height) for vertical

static const float Weights[5] = { 0.227027, 0.194596, 0.121622, 0.054054, 0.016216 };
static const float Offsets[5] = { 0.0, 1.0, 2.0, 3.0, 4.0 };

float4 PS_Blur(float2 texCoord : TEXCOORD0) : COLOR0
{
    float4 color = tex2D(TextureSampler, texCoord) * Weights[0];

    for (int i = 1; i < 5; i++)
    {
        float2 offset = TexelSize * Offsets[i];
        color += tex2D(TextureSampler, texCoord + offset) * Weights[i];
        color += tex2D(TextureSampler, texCoord - offset) * Weights[i];
    }

    return color;
}

technique Blur
{
    pass P0
    {
        PixelShader = compile ps_3_0 PS_Blur();
    }
}
```

### Fog Composite Shader

This shader is applied over the world render. It reads the fog texture to decide how to modulate each pixel:

```hlsl
// FogComposite.fx — Applies fog of war to the scene
sampler SceneSampler : register(s0);  // The world render
texture  FogTexture;
sampler FogSampler = sampler_state
{
    Texture   = <FogTexture>;
    MinFilter = Linear;
    MagFilter = Linear;
    AddressU  = Clamp;
    AddressV  = Clamp;
};

float  ExploredBrightness;  // e.g., 0.35
float  ExploredSaturation;  // e.g., 0.3

float4 PS_FogComposite(float2 texCoord : TEXCOORD0) : COLOR0
{
    float4 scene = tex2D(SceneSampler, texCoord);
    float  fog   = tex2D(FogSampler, texCoord).r; // 0=unexplored, 0.5=explored, 1=visible

    if (fog > 0.75)
    {
        // Fully visible — no modification
        return scene;
    }
    else if (fog > 0.25)
    {
        // Explored — desaturate and darken
        float gray = dot(scene.rgb, float3(0.299, 0.587, 0.114));
        float3 desaturated = lerp(float3(gray, gray, gray), scene.rgb, ExploredSaturation);
        return float4(desaturated * ExploredBrightness, scene.a);
    }
    else
    {
        // Unexplored — solid black
        return float4(0, 0, 0, scene.a);
    }
}

technique FogOfWar
{
    pass P0
    {
        PixelShader = compile ps_3_0 PS_FogComposite();
    }
}
```

### Smooth Transitions

The linear texture sampling on the fog texture naturally interpolates between states, producing smooth gradients at tile boundaries. The shader's threshold checks (`0.75`, `0.25`) create soft transitions rather than hard cutoffs. Tune the fog target resolution (2× tiles for smoother results) and blur kernel size to taste.

---

## 7 — Dynamic Updates

### When to Recalculate

Fog updates are expensive relative to most per-frame work. Optimize by only recalculating when necessary:

```csharp
public class FogUpdateTracker
{
    private readonly Dictionary<Entity, Point> _lastPositions = new();

    /// Returns true if any vision source has moved to a new tile.
    public bool NeedsUpdate(World world)
    {
        bool changed = false;

        var query = new QueryDescription().WithAll<VisionSource, TilePosition>();
        world.Query(in query, (Entity entity, ref VisionSource vs, ref TilePosition pos) =>
        {
            var current = new Point(pos.X, pos.Y);
            if (!_lastPositions.TryGetValue(entity, out var last) || last != current)
            {
                _lastPositions[entity] = current;
                changed = true;
            }
        });

        return changed;
    }
}
```

### Incremental Updates

For large maps with many sources, full recalculation is wasteful when only one unit moved. Track dirty regions:

```csharp
public void IncrementalUpdate(
    FogGrid fog, Entity movedEntity, Point oldTile, Point newTile,
    int radius, Func<int, int, bool> isOpaque)
{
    // Clear visibility only in the old vision area
    // (But: other units might overlap! Need refcounting or full rebuild)

    // Simple approach: full clear + rebuild is safer for correctness.
    // Use incremental only if profiling proves it necessary.
    fog.ClearVisible();
    RebuildAllVision(fog, isOpaque);
}
```

> **Pragmatic advice:** Full clear + rebuild each frame (when something moves) is correct and simple. On a 256×256 map with 20 vision sources of radius 12, shadowcasting takes ~0.5ms. Only optimize if profiling shows it's a bottleneck.

### Throttling Updates

For very large maps or many units, throttle fog updates to every N frames or distribute across frames:

```csharp
private int _fogUpdateCounter;
private const int FogUpdateInterval = 3; // Every 3rd frame

public void Update()
{
    _fogUpdateCounter++;
    if (_fogUpdateCounter % FogUpdateInterval != 0) return;

    fog.ClearVisible();
    RebuildAllVision(fog, isOpaque);
    fogRenderer.BuildFogTexture(graphicsDevice, fog);
    fogRenderer.BlurFog(graphicsDevice, spriteBatch);
}
```

---

## 8 — Entity Visibility

Entities (enemies, items, NPCs) should respect the fog:

```csharp
public record struct LastKnownInfo(
    Point    Position,
    int      SpriteId,
    float    TimeSeen     // GameTime when last visible
);

public record struct FogVisibility(
    bool            CurrentlyVisible,
    LastKnownInfo?  LastKnown
);
```

### Visibility Query System

```csharp
public static class EntityVisibility
{
    /// Determine if an entity should be rendered, and where.
    public static FogVisibility GetVisibility(
        FogGrid fog, int entityTileX, int entityTileY,
        LastKnownInfo? previousLastKnown, float currentTime)
    {
        var state = fog[entityTileX, entityTileY];

        if (state == VisibilityState.Visible)
        {
            // Currently in LOS — fully visible, update last-known
            return new FogVisibility(true, new LastKnownInfo(
                new Point(entityTileX, entityTileY),
                -1, // Set actual sprite ID in caller
                currentTime));
        }

        if (state == VisibilityState.Explored && previousLastKnown.HasValue)
        {
            // Explored area — show "ghost" at last known position
            return new FogVisibility(false, previousLastKnown);
        }

        // Unexplored — completely hidden
        return new FogVisibility(false, null);
    }
}
```

### Rendering Ghosts

When the player has previously seen an enemy that has since moved out of sight, render a translucent "ghost" sprite at the last-known position:

```csharp
// In your entity render system:
if (fogVis.CurrentlyVisible)
{
    // Normal render at actual position
    DrawEntity(entity, position, Color.White);
}
else if (fogVis.LastKnown is { } lastKnown)
{
    // Ghost at last known position — dimmed and translucent
    var ghostColor = new Color(150, 150, 150, 100);
    DrawEntityAt(lastKnown.SpriteId, lastKnown.Position, ghostColor);
}
// else: not drawn at all
```

---

## 9 — Minimap Integration

The minimap shows exploration progress — vital for strategy and RPG games.

```csharp
public class MinimapFogRenderer
{
    private Texture2D _minimapTexture;

    public void UpdateMinimap(GraphicsDevice gd, FogGrid fog,
                              Func<int, int, Color> terrainColor)
    {
        if (_minimapTexture == null ||
            _minimapTexture.Width != fog.Width ||
            _minimapTexture.Height != fog.Height)
        {
            _minimapTexture?.Dispose();
            _minimapTexture = new Texture2D(gd, fog.Width, fog.Height);
        }

        var pixels = new Color[fog.Width * fog.Height];
        for (int y = 0; y < fog.Height; y++)
        for (int x = 0; x < fog.Width;  x++)
        {
            int i = y * fog.Width + x;
            var state = fog[x, y];

            pixels[i] = state switch
            {
                VisibilityState.Visible  => terrainColor(x, y),
                VisibilityState.Explored => DimColor(terrainColor(x, y), 0.4f),
                _                        => new Color(20, 20, 25) // Near-black
            };
        }

        _minimapTexture.SetData(pixels);
    }

    public void Draw(SpriteBatch sb, Rectangle minimapRect)
    {
        sb.Draw(_minimapTexture, minimapRect, Color.White);
    }

    private static Color DimColor(Color c, float factor)
    {
        return new Color(
            (int)(c.R * factor),
            (int)(c.G * factor),
            (int)(c.B * factor));
    }
}
```

### Minimap Entity Dots

Draw colored dots for visible entities on the minimap:

```csharp
public void DrawMinimapEntities(
    SpriteBatch sb, Rectangle minimapRect, FogGrid fog,
    ReadOnlySpan<(Point Tile, Color DotColor)> entities)
{
    float scaleX = (float)minimapRect.Width  / fog.Width;
    float scaleY = (float)minimapRect.Height / fog.Height;

    foreach (var (tile, color) in entities)
    {
        if (!fog.IsVisible(tile.X, tile.Y)) continue; // Only show visible entities

        var pos = new Vector2(
            minimapRect.X + tile.X * scaleX,
            minimapRect.Y + tile.Y * scaleY);

        // Draw a 2×2 dot (use a 1×1 white pixel texture)
        sb.Draw(_pixel, pos, null, color, 0, Vector2.Zero,
                new Vector2(Math.Max(2, scaleX), Math.Max(2, scaleY)),
                SpriteEffects.None, 0);
    }
}
```

---

## 10 — Strategy Game Patterns

### Team-Based Shared Vision

In RTS games, all allied units and buildings contribute to a shared fog grid:

```csharp
public record struct TeamVision(int TeamId);

public static void ComputeTeamFog(
    World world, Dictionary<int, FogGrid> teamFogs,
    Func<int, int, bool> isOpaque)
{
    // Clear all team grids
    foreach (var fog in teamFogs.Values)
        fog.ClearVisible();

    // Each vision source reveals for its team
    var query = new QueryDescription().WithAll<VisionSource, TilePosition, TeamVision>();
    world.Query(in query, (ref VisionSource vs, ref TilePosition pos, ref TeamVision team) =>
    {
        if (teamFogs.TryGetValue(team.TeamId, out var fog))
            Shadowcast.ComputeFOV(fog, pos.X, pos.Y, vs.Radius, isOpaque);
    });
}
```

### Competitive Fog (Re-closing)

In competitive strategy games (StarCraft-style), fog closes again when units leave. The three-state model handles this naturally:

- **Visible:** Currently in a friendly unit's LOS → full information.
- **Explored:** Was visible but no longer → terrain visible, but enemies hidden. Shows last-known building positions.
- **Unexplored:** Never seen → completely black.

The key is that `ClearVisible()` resets all cells to explored-or-unexplored each frame, and only current LOS re-promotes cells to visible. Enemies in explored-but-not-visible areas are hidden — they can move without the opponent knowing.

### Building Sight Ranges

Different structures provide different vision:

```csharp
// Example sight ranges
public static int GetSightRange(BuildingType type) => type switch
{
    BuildingType.WatchTower => 14,
    BuildingType.TownHall   => 10,
    BuildingType.Barracks   =>  7,
    BuildingType.Wall       =>  3,
    _                       =>  5
};
```

### Co-op Shared Exploration

In co-op, you may want *permanent* shared exploration — if Player A explored an area, Player B also sees it as explored even if neither is currently there:

```csharp
public class SharedExplorationGrid
{
    private readonly bool[] _teamExplored;
    private readonly int _width;

    public SharedExplorationGrid(int width, int height)
    {
        _width = width;
        _teamExplored = new bool[width * height];
    }

    /// Merge a player's fog into shared exploration
    public void MergeExplored(FogGrid playerFog)
    {
        for (int y = 0; y < playerFog.Height; y++)
        for (int x = 0; x < playerFog.Width;  x++)
        {
            if (playerFog.IsExplored(x, y))
                _teamExplored[y * _width + x] = true;
        }
    }

    /// Apply shared exploration back to a player's fog
    public void ApplyTo(FogGrid playerFog)
    {
        // This would require exposing a method on FogGrid to mark cells explored
        // without marking them visible. Add: fog.MarkExplored(x, y)
    }
}
```

---

## 11 — ECS Integration

### Components

```csharp
/// Entities that generate vision (players, units, towers).
public record struct VisionSource(
    int   Radius,
    float HalfAngle,     // π for full circle, smaller for cones
    float Direction       // Facing direction in radians (for cones)
)
{
    /// Full 360° circular vision
    public static VisionSource Circle(int radius) =>
        new(radius, MathF.PI, 0);

    /// Cone-shaped vision for stealth game enemies
    public static VisionSource Cone(int radius, float halfAngleDeg, float directionRad) =>
        new(radius, MathHelper.ToRadians(halfAngleDeg), directionRad);
}

/// Grid position in tile coordinates (you likely already have this).
public record struct TilePosition(int X, int Y);

/// Tag: this entity should be hidden when not in the player's FOV.
public record struct FogHideable;

/// Tracks the last-known state for entities that go out of sight.
public record struct LastKnownState(
    Point Position,
    int   SpriteIndex,
    float TimeLastSeen
);
```

### FogOfWarGrid Resource

In Arch ECS, shared data lives as a resource or a singleton. Wrap the fog grid:

```csharp
/// Shared resource — access via world.Get<FogOfWarResource>() or pass directly.
public class FogOfWarResource
{
    public FogGrid          Grid          { get; }
    public FogRenderer      Renderer      { get; }
    public FogUpdateTracker UpdateTracker { get; } = new();
    public bool             IsDirty       { get; set; } = true;

    public FogOfWarResource(int mapWidth, int mapHeight,
                            GraphicsDevice gd, int tileSize,
                            Effect blurEffect, Effect fogComposite)
    {
        Grid     = new FogGrid(mapWidth, mapHeight);
        Renderer = new FogRenderer(gd, mapWidth, mapHeight,
                                   tileSize, blurEffect, fogComposite);
    }
}
```

### FogUpdateSystem

The main system that ties everything together:

```csharp
public class FogUpdateSystem
{
    private readonly World              _world;
    private readonly FogOfWarResource   _fogResource;
    private readonly Func<int, int, bool> _isOpaque;

    public FogUpdateSystem(World world, FogOfWarResource fogResource,
                           Func<int, int, bool> isOpaque)
    {
        _world       = world;
        _fogResource = fogResource;
        _isOpaque    = isOpaque;
    }

    public void Update(float gameTime)
    {
        // 1. Check if any vision source moved
        if (!_fogResource.UpdateTracker.NeedsUpdate(_world) && !_fogResource.IsDirty)
            return;

        var fog = _fogResource.Grid;

        // 2. Clear current-frame visibility
        fog.ClearVisible();

        // 3. Recalculate FOV for all vision sources
        var query = new QueryDescription().WithAll<VisionSource, TilePosition>();
        _world.Query(in query, (ref VisionSource vs, ref TilePosition pos) =>
        {
            if (vs.HalfAngle >= MathF.PI - 0.01f)
            {
                // Full circle
                Shadowcast.ComputeFOV(fog, pos.X, pos.Y, vs.Radius, _isOpaque);
            }
            else
            {
                // Cone — use filtered FOV
                var cone = new VisionCone(vs.Direction, vs.HalfAngle, vs.Radius);
                ComputeConeFOV(fog, pos.X, pos.Y, cone, _isOpaque);
            }
        });

        // 4. Update entity visibility / last-known states
        UpdateEntityVisibility(gameTime);

        _fogResource.IsDirty = false;
    }

    private void UpdateEntityVisibility(float gameTime)
    {
        var fog = _fogResource.Grid;
        var query = new QueryDescription().WithAll<FogHideable, TilePosition>();

        _world.Query(in query, (Entity entity, ref TilePosition pos) =>
        {
            bool visible = fog.IsVisible(pos.X, pos.Y);

            if (visible)
            {
                // Update or create last-known state
                _world.AddOrGet(entity, new LastKnownState(
                    new Point(pos.X, pos.Y), 0, gameTime));
            }
        });
    }
}
```

### AI That Respects Fog

Enemies shouldn't cheat by knowing the player's position through fog. Query fog state in AI systems:

```csharp
public class EnemyAISystem
{
    private readonly FogOfWarResource _fog;

    public void Update(World world)
    {
        var query = new QueryDescription()
            .WithAll<EnemyAI, TilePosition, VisionSource>();

        world.Query(in query, (ref EnemyAI ai, ref TilePosition pos,
                               ref VisionSource vs) =>
        {
            // Build this enemy's personal FOV (or use team fog)
            bool canSeePlayer = CanEntitySeeTarget(
                pos.X, pos.Y, vs.Radius,
                _playerPos.X, _playerPos.Y);

            if (canSeePlayer)
            {
                ai.State       = AIState.Chasing;
                ai.LastKnownTarget = _playerPos;
            }
            else if (ai.State == AIState.Chasing)
            {
                // Lost sight — move to last known position, then search
                ai.State = AIState.Searching;
            }
            else if (ai.State == AIState.Searching &&
                     pos == ai.LastKnownTarget)
            {
                // Reached last known position, player gone — return to patrol
                ai.State = AIState.Patrolling;
            }
        });
    }

    private bool CanEntitySeeTarget(
        int ex, int ey, int radius, int tx, int ty)
    {
        int dx = tx - ex, dy = ty - ey;
        if (dx * dx + dy * dy > radius * radius) return false;
        return Bresenham.LineOfSight(ex, ey, tx, ty,
            (x, y) => /* your opacity check */false);
    }
}
```

### Full Render Integration

```csharp
// In your Game.Draw():
protected override void Draw(GameTime gameTime)
{
    var fogRes = _fogResource;

    // 1. Draw world to a RenderTarget
    GraphicsDevice.SetRenderTarget(_worldTarget);
    GraphicsDevice.Clear(Color.Black);
    _tilemapRenderer.Draw(_spriteBatch);
    _entityRenderer.Draw(_spriteBatch, fogRes.Grid); // Entities check fog

    // 2. Build & blur fog
    fogRes.Renderer.BuildFogTexture(GraphicsDevice, fogRes.Grid);
    fogRes.Renderer.BlurFog(GraphicsDevice, _spriteBatch);

    // 3. Composite world + fog to backbuffer
    GraphicsDevice.SetRenderTarget(null);
    fogRes.Renderer.DrawFog(_spriteBatch, _worldTarget.Bounds);

    // 4. Minimap + HUD (on top, unaffected by fog)
    _minimapRenderer.Draw(_spriteBatch, fogRes.Grid);
    _hud.Draw(_spriteBatch);

    base.Draw(gameTime);
}
```

---

## Quick Reference

| Concern | Approach | Complexity |
|---|---|---|
| Small map, 1 source | Brute-force raycasting | O(r³) |
| Any map, few sources | Shadowcasting | O(r²) per source |
| RTS, many sources | Shadowcasting + throttle | O(n·r²), skip unchanged |
| Smooth edges | Fog RenderTarget + Gaussian blur | 2 extra draw calls |
| Explored memory | Separate `bool[]` that never resets | Near-zero cost |
| Entity hiding | Query fog state before rendering | O(1) per entity |
| AI fairness | Per-team fog grids, AI queries own grid | Same as player fog |

### Performance Budget (Approximate)

- Shadowcasting radius 12: **~50μs** per source.
- 20 sources on a 256×256 map: **~1ms** total.
- Fog texture build + blur: **~0.3ms** GPU.
- Total budget for a 60fps frame: **16.6ms** — fog is well within budget.
