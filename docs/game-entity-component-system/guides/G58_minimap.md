# G58 — Minimap Systems



> **Category:** Guide · **Related:** [G37 Tilemap Systems](./G37_tilemap_systems.md) · [G54 Fog of War](./G54_fog_of_war.md) · [G20 Camera Systems](./G20_camera_systems.md) · [G5 UI Framework](./G5_ui_framework.md) · [G2 Rendering & Graphics](./G2_rendering_and_graphics.md)

---

## Overview

A minimap gives players spatial awareness — where they are, what's nearby, and what they've explored. This guide covers corner minimaps, full-screen overlays, room-based maps, fog-of-war integration, and dynamic updates, all wired through Arch ECS.

---

## 1 — Minimap Types

Three standard presentations, each suited to different moments of play:

| Type | Visibility | Use Case |
|------|-----------|----------|
| **Corner minimap** | Always on-screen (top-right/bottom-right) | Continuous navigation in open worlds, dungeons, RPGs |
| **Full-screen overlay** | Toggle with `M` or `Tab` | Detailed exploration review, waypoint placement, legend |
| **World map screen** | Opened from pause menu | Strategic overview, fast travel, quest tracking |

Most games combine two: a corner minimap for moment-to-moment play and a full-screen map for planning. Room-based games (metroidvania, Zelda-likes) often skip the corner minimap entirely and use a room grid overlay instead.

---

## 2 — Minimap Rendering

The core technique: render the world onto a small `RenderTarget2D`, then draw that texture in the UI layer.

### Pixel-Per-Tile Approach

For tile-based worlds, the simplest and sharpest approach maps **1 pixel = 1 tile**. A 200×150 tile world becomes a 200×150 pixel texture. No scaling artifacts, trivially fast.

```csharp
public record struct MinimapData
{
    public RenderTarget2D Texture;
    public int WidthInTiles;
    public int HeightInTiles;
    public bool IsDirty; // flag for incremental updates
}
```

### Color-Coding Tiles

Define a palette that maps tile IDs to minimap colors:

```csharp
public static class MinimapPalette
{
    static readonly Dictionary<int, Color> TileColors = new()
    {
        [0] = new Color(60, 60, 60),      // ground / floor
        [1] = new Color(30, 30, 30),       // wall
        [2] = new Color(40, 80, 160),      // water
        [3] = new Color(140, 90, 40),      // door
        [4] = new Color(220, 200, 50),     // chest / loot
        [5] = new Color(20, 120, 20),      // grass
    };

    public static Color Get(int tileId) =>
        TileColors.TryGetValue(tileId, out var c) ? c : Color.Magenta;
}
```

### Building the Minimap Texture

```csharp
public static void RebuildMinimap(
    GraphicsDevice gpu,
    ref MinimapData minimap,
    int[,] tileGrid)
{
    int w = tileGrid.GetLength(0);
    int h = tileGrid.GetLength(1);

    minimap.Texture ??= new RenderTarget2D(gpu, w, h);
    var pixels = new Color[w * h];

    for (int y = 0; y < h; y++)
    for (int x = 0; x < w; x++)
        pixels[y * w + x] = MinimapPalette.Get(tileGrid[x, y]);

    minimap.Texture.SetData(pixels);
    minimap.WidthInTiles = w;
    minimap.HeightInTiles = h;
    minimap.IsDirty = false;
}
```

`SetData` is fast for small textures (a 256×256 map is only 256 KB). For larger worlds, use incremental updates (§9).

---

## 3 — Minimap Camera

The minimap viewport defines which portion of the world texture is visible in the corner minimap.

```csharp
public record struct MinimapCamera
{
    public Vector2 Center;       // in tile coordinates
    public float Zoom;           // tiles visible in each axis = ViewSize / Zoom
    public int ViewSizePx;       // rendered size on screen (e.g. 180)
    public float MinZoom;        // 0.5 = zoomed out
    public float MaxZoom;        // 4.0 = zoomed in
}
```

### Following the Player

```csharp
public static Rectangle GetSourceRect(
    in MinimapCamera cam,
    in MinimapData minimap)
{
    // How many tiles fit in the view at current zoom
    float tilesVisible = cam.ViewSizePx / MathHelper.Clamp(cam.Zoom, cam.MinZoom, cam.MaxZoom);
    float half = tilesVisible * 0.5f;

    // Clamp to world bounds
    float left = MathHelper.Clamp(cam.Center.X - half, 0, minimap.WidthInTiles - tilesVisible);
    float top  = MathHelper.Clamp(cam.Center.Y - half, 0, minimap.HeightInTiles - tilesVisible);

    return new Rectangle(
        (int)left, (int)top,
        (int)tilesVisible, (int)tilesVisible);
}
```

Update `cam.Center` to the player's tile position each frame. Scroll wheel or `+`/`-` adjusts `Zoom`.

---

## 4 — Icons & Markers

### ECS Component

```csharp
public record struct MinimapMarker
{
    public MinimapIcon Icon;
    public Color Tint;
    public bool Pulse;           // pulsing animation for quest objectives
    public bool RequiresExplored; // only show if tile is explored (fog of war)
}

public enum MinimapIcon : byte
{
    PlayerArrow,
    EnemyDot,
    NpcDiamond,
    QuestObjective,
    Chest,
    DoorExit,
    CustomPOI
}
```

### Drawing Markers

Markers are drawn **after** the minimap texture, in screen space over the minimap rect:

```csharp
public static Vector2 WorldToMinimap(
    Vector2 worldTilePos,
    Rectangle sourceRect,
    Rectangle destRect)
{
    float nx = (worldTilePos.X - sourceRect.X) / sourceRect.Width;
    float ny = (worldTilePos.Y - sourceRect.Y) / sourceRect.Height;
    return new Vector2(
        destRect.X + nx * destRect.Width,
        destRect.Y + ny * destRect.Height);
}
```

### Player Arrow

Rotate a small triangle texture to face the player's movement direction:

```csharp
float angle = MathF.Atan2(playerVelocity.Y, playerVelocity.X);
var screenPos = WorldToMinimap(playerTilePos, srcRect, destRect);
spriteBatch.Draw(arrowTexture, screenPos, null, Color.White,
    angle, arrowOrigin, 1f, SpriteEffects.None, 0f);
```

### Pulsing Effect

```csharp
float pulse = 0.7f + 0.3f * MathF.Sin((float)gameTime.TotalGameTime.TotalSeconds * 4f);
Color pulsedColor = marker.Tint * pulse;
```

### Icon Scaling

Icons should stay a fixed pixel size regardless of minimap zoom. Since you draw them in screen space after the minimap texture blit, they naturally stay constant. If drawing into the render target instead, scale by `1 / zoom`.

---

## 5 — Fog of War Integration

Tie into the exploration data from [G54 Fog of War](./G54_fog_of_war.md). The minimap shows three states:

| State | Minimap Appearance |
|-------|-------------------|
| **Unexplored** | Black / fully hidden |
| **Previously explored** | Dimmed (50% opacity), no live markers |
| **Currently visible** | Full color, all markers shown |

### Applying Fog to the Minimap Texture

```csharp
public static void ApplyFogToMinimap(
    Color[] minimapPixels,
    FogState[,] fogGrid,
    int w, int h)
{
    for (int y = 0; y < h; y++)
    for (int x = 0; x < w; x++)
    {
        int i = y * w + x;
        switch (fogGrid[x, y])
        {
            case FogState.Unexplored:
                minimapPixels[i] = Color.Black;
                break;
            case FogState.Explored:
                minimapPixels[i] = Color.Lerp(Color.Black, minimapPixels[i], 0.45f);
                break;
            // FogState.Visible — leave as-is
        }
    }
}
```

### Filtering Markers by Fog

```csharp
bool showMarker = !marker.RequiresExplored
    || fogGrid[tileX, tileY] == FogState.Visible;
```

Enemy dots only appear in visible range. Quest objectives might show even in explored-but-not-visible areas (design choice).

---

## 6 — Room-Based Maps

For metroidvania and Zelda-style games, the map is a **grid of rooms** rather than a continuous tile view.

```csharp
public record struct RoomMapEntry
{
    public Point GridPosition;    // position on the room grid
    public Point SizeInCells;     // width/height in grid cells (most rooms are 1×1)
    public bool Discovered;
    public bool HasSavePoint;
    public bool HasBoss;
    public Color RoomColor;       // tint by area/biome
}

public record struct RoomConnection
{
    public Point RoomA;
    public Point RoomB;
    public Direction Side;        // which wall the connection is on
}
```

### Drawing the Room Grid

```csharp
public static void DrawRoomMap(
    SpriteBatch sb,
    Texture2D pixel,
    IReadOnlyList<RoomMapEntry> rooms,
    IReadOnlyList<RoomConnection> connections,
    Point currentRoomGrid,
    Rectangle destArea,
    int cellSize)  // pixels per grid cell on screen
{
    foreach (var room in rooms)
    {
        if (!room.Discovered) continue;

        var rect = new Rectangle(
            destArea.X + room.GridPosition.X * cellSize,
            destArea.Y + room.GridPosition.Y * cellSize,
            room.SizeInCells.X * cellSize,
            room.SizeInCells.Y * cellSize);

        Color fill = room.GridPosition == currentRoomGrid
            ? Color.White
            : room.RoomColor * 0.6f;

        sb.Draw(pixel, rect, fill);

        // Border
        DrawRectOutline(sb, pixel, rect, Color.Gray, 1);

        // Icons for save points, bosses
        if (room.HasSavePoint)
            sb.Draw(pixel, new Rectangle(rect.Center.X - 2, rect.Center.Y - 2, 4, 4), Color.Cyan);
        if (room.HasBoss)
            sb.Draw(pixel, new Rectangle(rect.Center.X - 2, rect.Center.Y - 2, 4, 4), Color.Red);
    }

    // Draw connections as gaps in walls (omit border segments at connection points)
    foreach (var conn in connections)
    {
        // Implementation: draw a small colored line between adjacent rooms
        var midA = new Vector2(
            destArea.X + (conn.RoomA.X + 0.5f) * cellSize,
            destArea.Y + (conn.RoomA.Y + 0.5f) * cellSize);
        var midB = new Vector2(
            destArea.X + (conn.RoomB.X + 0.5f) * cellSize,
            destArea.Y + (conn.RoomB.Y + 0.5f) * cellSize);
        DrawLine(sb, pixel, midA, midB, Color.Gray * 0.4f, 1);
    }
}
```

---

## 7 — Minimap Shapes

### Rectangular (Default)

Draw the minimap texture into a `Rectangle` destination. Add a border by drawing a slightly larger filled rect behind it.

```csharp
// Border
sb.Draw(pixel, new Rectangle(destRect.X - 2, destRect.Y - 2,
    destRect.Width + 4, destRect.Height + 4), Color.Black * 0.8f);
// Minimap
sb.Draw(minimap.Texture, destRect, sourceRect, Color.White);
```

### Circular (Stencil Masking)

Use the stencil buffer to clip the minimap to a circle:

```csharp
public static void DrawCircularMinimap(
    GraphicsDevice gpu,
    SpriteBatch sb,
    Texture2D minimapTex,
    Texture2D circleMask,  // white circle on transparent background
    Rectangle sourceRect,
    Vector2 center,
    int radius)
{
    var destRect = new Rectangle(
        (int)(center.X - radius), (int)(center.Y - radius),
        radius * 2, radius * 2);

    // Pass 1 — write circle mask to stencil
    var stencilWrite = new DepthStencilState
    {
        StencilEnable = true,
        StencilFunction = CompareFunction.Always,
        StencilPass = StencilOperation.Replace,
        ReferenceStencil = 1,
        DepthBufferEnable = false
    };

    // Pass 2 — draw minimap only where stencil == 1
    var stencilRead = new DepthStencilState
    {
        StencilEnable = true,
        StencilFunction = CompareFunction.Equal,
        ReferenceStencil = 1,
        StencilPass = StencilOperation.Keep,
        DepthBufferEnable = false
    };

    // Clear stencil
    gpu.Clear(ClearOptions.Stencil, Color.Transparent, 0, 0);

    // Write mask
    sb.Begin(depthStencilState: stencilWrite, blendState: new BlendState
    {
        ColorWriteChannels = ColorWriteChannels.None // don't write color, only stencil
    });
    sb.Draw(circleMask, destRect, Color.White);
    sb.End();

    // Draw minimap through mask
    sb.Begin(depthStencilState: stencilRead, samplerState: SamplerState.PointClamp);
    sb.Draw(minimapTex, destRect, sourceRect, Color.White);
    sb.End();

    // Draw circle border on top (normal blend)
    sb.Begin();
    sb.Draw(circleFrameTexture, destRect, Color.White);
    sb.End();
}
```

**Alternative:** Use a pixel shader that discards fragments outside a radius. Simpler if you already have a shader pipeline:

```hlsl
float2 uv = input.TexCoord - 0.5;
if (dot(uv, uv) > 0.25) discard; // radius 0.5 squared
```

---

## 8 — Full-Screen Map

Toggled with `M` or `Tab`. Renders the entire minimap texture scaled up, with pan/scroll and UI overlays.

### State

```csharp
public record struct FullScreenMap
{
    public bool IsOpen;
    public Vector2 PanOffset;     // in tile coords
    public float Zoom;            // 1.0 = 1 tile per N screen pixels
    public List<Vector2> Waypoints;
}
```

### Pan & Scroll Input

```csharp
public static void UpdateFullScreenMap(
    ref FullScreenMap map,
    KeyboardState kb,
    MouseState mouse,
    MouseState prevMouse,
    float dt)
{
    if (!map.IsOpen) return;

    // Arrow key panning
    float panSpeed = 60f / map.Zoom;
    if (kb.IsKeyDown(Keys.Left))  map.PanOffset.X -= panSpeed * dt;
    if (kb.IsKeyDown(Keys.Right)) map.PanOffset.X += panSpeed * dt;
    if (kb.IsKeyDown(Keys.Up))    map.PanOffset.Y -= panSpeed * dt;
    if (kb.IsKeyDown(Keys.Down))  map.PanOffset.Y += panSpeed * dt;

    // Scroll wheel zoom
    int scrollDelta = mouse.ScrollWheelValue - prevMouse.ScrollWheelValue;
    if (scrollDelta != 0)
        map.Zoom = MathHelper.Clamp(map.Zoom + scrollDelta * 0.001f, 0.5f, 8f);

    // Right-click to place waypoint
    if (mouse.RightButton == ButtonState.Pressed
        && prevMouse.RightButton == ButtonState.Released)
    {
        var worldPos = ScreenToMapTile(mouse.Position, map);
        map.Waypoints.Add(worldPos);
    }
}
```

### Drawing the Full-Screen Map

Render the minimap texture with `SamplerState.PointClamp` for crisp pixel art, overlaid with a semi-transparent background, legends, and layer toggle buttons. Markers scale with zoom so they remain readable.

---

## 9 — Dynamic Updates

When the world changes (a door opens, a bridge is built, terrain is destroyed), the minimap must reflect it.

### Strategy: Incremental vs Full Rebuild

| Approach | When to use |
|----------|------------|
| **Full rebuild** | Map load, area transitions, infrequent bulk changes |
| **Incremental** | Single tile edits, door toggles, real-time terrain modification |

### Incremental Update

```csharp
public static void UpdateMinimapTile(
    ref MinimapData minimap,
    int tileX, int tileY,
    int newTileId,
    FogState fog)
{
    Color c = MinimapPalette.Get(newTileId);
    if (fog == FogState.Unexplored) c = Color.Black;
    else if (fog == FogState.Explored) c = Color.Lerp(Color.Black, c, 0.45f);

    minimap.Texture.SetData(0, new Rectangle(tileX, tileY, 1, 1),
        new[] { c }, 0, 1);
}
```

`SetData` on a 1×1 rect is effectively free. Batch multiple changes per frame if needed.

### Dirty Region Approach

For games with frequent changes across a region, track a dirty rectangle and update it in one `SetData` call:

```csharp
public record struct MinimapDirtyRegion
{
    public Rectangle Bounds;
    public bool HasDirty;
}
```

Expand the bounds each time a tile changes, then flush once per frame.

---

## 10 — ECS Integration

### Components

```csharp
// Attach to any entity that should appear on the minimap
public record struct MinimapMarker
{
    public MinimapIcon Icon;
    public Color Tint;
    public bool Pulse;
    public bool RequiresExplored;
}

// Singleton resource holding minimap state
public record struct MinimapState
{
    public MinimapData Data;
    public MinimapCamera Camera;
    public FullScreenMap FullScreen;
    public bool Enabled;
}
```

### MinimapRenderSystem

```csharp
public class MinimapRenderSystem : ISystem
{
    private readonly QueryDescription _markerQuery = new QueryDescription()
        .WithAll<Position, MinimapMarker>();

    private readonly QueryDescription _playerQuery = new QueryDescription()
        .WithAll<Position, PlayerTag, Velocity>();

    public void Render(World world, SpriteBatch sb, Texture2D pixel,
        Texture2D arrowTex, Texture2D[] iconTextures,
        FogState[,] fogGrid, GameTime gameTime)
    {
        ref var state = ref world.Get<MinimapState>();
        if (!state.Enabled) return;

        // --- Update camera to follow player ---
        world.Query(in _playerQuery, (ref Position pos, ref PlayerTag _, ref Velocity vel) =>
        {
            state.Camera.Center = pos.TilePosition;
        });

        var srcRect = GetSourceRect(in state.Camera, in state.Data);
        var destRect = GetDestRect(state.Camera.ViewSizePx); // e.g. top-right corner

        // --- Draw minimap background ---
        sb.Draw(pixel, new Rectangle(destRect.X - 2, destRect.Y - 2,
            destRect.Width + 4, destRect.Height + 4), Color.Black * 0.85f);
        sb.Draw(state.Data.Texture, destRect, srcRect, Color.White);

        // --- Draw markers ---
        float time = (float)gameTime.TotalGameTime.TotalSeconds;

        world.Query(in _markerQuery, (ref Position pos, ref MinimapMarker marker) =>
        {
            int tx = (int)pos.TilePosition.X;
            int ty = (int)pos.TilePosition.Y;

            // Fog check
            if (marker.RequiresExplored
                && fogGrid[tx, ty] != FogState.Visible)
                return;

            // Off-screen check
            if (!srcRect.Contains(tx, ty)) return;

            var screenPos = WorldToMinimap(pos.TilePosition, srcRect, destRect);
            var color = marker.Tint;
            if (marker.Pulse)
                color *= 0.7f + 0.3f * MathF.Sin(time * 4f);

            var tex = iconTextures[(int)marker.Icon];
            var origin = new Vector2(tex.Width / 2f, tex.Height / 2f);
            sb.Draw(tex, screenPos, null, color, 0f, origin, 1f,
                SpriteEffects.None, 0f);
        });

        // --- Player arrow (drawn last, on top) ---
        world.Query(in _playerQuery, (ref Position pos, ref PlayerTag _, ref Velocity vel) =>
        {
            var screenPos = WorldToMinimap(pos.TilePosition, srcRect, destRect);
            float angle = MathF.Atan2(vel.Value.Y, vel.Value.X);
            var origin = new Vector2(arrowTex.Width / 2f, arrowTex.Height / 2f);
            sb.Draw(arrowTex, screenPos, null, Color.White, angle, origin,
                1f, SpriteEffects.None, 0f);
        });
    }

    private static Rectangle GetDestRect(int size)
    {
        // Top-right corner, 10px padding
        int screenW = 1280; // or pass viewport width
        return new Rectangle(screenW - size - 10, 10, size, size);
    }
}
```

---

## 11 — Practical Example: Dungeon Crawler Minimap

A complete, self-contained minimap for a dungeon crawler with corner minimap, player arrow, enemy dots, discovered rooms, and fog masking.

```csharp
// ---------- Components ----------

public record struct Position(Vector2 TilePosition);
public record struct Velocity(Vector2 Value);
public record struct PlayerTag;

public record struct MinimapMarker(
    MinimapIcon Icon,
    Color Tint,
    bool Pulse = false,
    bool RequiresExplored = true);

public enum MinimapIcon : byte
{
    PlayerArrow, EnemyDot, NpcDiamond, QuestStar, Chest, DoorExit
}

public enum FogState : byte { Unexplored, Explored, Visible }

// ---------- Minimap Manager ----------

public sealed class DungeonMinimap
{
    const int ViewPx = 180;       // corner minimap size on screen
    const int Padding = 10;

    RenderTarget2D _mapTex;
    readonly Texture2D _pixel;
    readonly Texture2D _arrowTex;
    readonly Texture2D[] _icons;  // indexed by MinimapIcon

    int _mapW, _mapH;
    Color[] _baseColors;          // tile colors without fog
    Color[] _displayColors;       // with fog applied

    Vector2 _camCenter;
    float _zoom = 2f;

    public DungeonMinimap(GraphicsDevice gpu,
        Texture2D pixel, Texture2D arrow, Texture2D[] icons)
    {
        _pixel = pixel;
        _arrowTex = arrow;
        _icons = icons;
    }

    // Call on map load or area transition
    public void Build(GraphicsDevice gpu, int[,] tiles, FogState[,] fog)
    {
        _mapW = tiles.GetLength(0);
        _mapH = tiles.GetLength(1);
        _mapTex?.Dispose();
        _mapTex = new RenderTarget2D(gpu, _mapW, _mapH);

        _baseColors = new Color[_mapW * _mapH];
        _displayColors = new Color[_mapW * _mapH];

        for (int y = 0; y < _mapH; y++)
        for (int x = 0; x < _mapW; x++)
            _baseColors[y * _mapW + x] = MinimapPalette.Get(tiles[x, y]);

        RefreshFog(fog);
    }

    // Call when fog changes (each frame or on exploration event)
    public void RefreshFog(FogState[,] fog)
    {
        Array.Copy(_baseColors, _displayColors, _baseColors.Length);

        for (int y = 0; y < _mapH; y++)
        for (int x = 0; x < _mapW; x++)
        {
            int i = y * _mapW + x;
            switch (fog[x, y])
            {
                case FogState.Unexplored:
                    _displayColors[i] = Color.Black;
                    break;
                case FogState.Explored:
                    _displayColors[i] = Color.Lerp(Color.Black, _displayColors[i], 0.45f);
                    break;
            }
        }

        _mapTex.SetData(_displayColors);
    }

    // Update a single tile after world change (door opened, etc.)
    public void PatchTile(int x, int y, int newTileId, FogState fog)
    {
        int i = y * _mapW + x;
        _baseColors[i] = MinimapPalette.Get(newTileId);

        Color c = _baseColors[i];
        if (fog == FogState.Unexplored) c = Color.Black;
        else if (fog == FogState.Explored) c = Color.Lerp(Color.Black, c, 0.45f);
        _displayColors[i] = c;

        _mapTex.SetData(0, new Rectangle(x, y, 1, 1), new[] { c }, 0, 1);
    }

    // Main draw call
    public void Draw(SpriteBatch sb, World world, FogState[,] fog,
        int screenW, GameTime gt)
    {
        // Player position for camera
        var playerQ = new QueryDescription().WithAll<Position, PlayerTag, Velocity>();
        Vector2 playerPos = Vector2.Zero;
        float playerAngle = 0f;

        world.Query(in playerQ, (ref Position p, ref PlayerTag _, ref Velocity v) =>
        {
            playerPos = p.TilePosition;
            playerAngle = MathF.Atan2(v.Value.Y, v.Value.X);
        });

        _camCenter = playerPos;

        // Source rect (what part of the map texture to show)
        float tilesVis = ViewPx / _zoom;
        float half = tilesVis * 0.5f;
        float sx = MathHelper.Clamp(_camCenter.X - half, 0, _mapW - tilesVis);
        float sy = MathHelper.Clamp(_camCenter.Y - half, 0, _mapH - tilesVis);
        var srcRect = new Rectangle((int)sx, (int)sy, (int)tilesVis, (int)tilesVis);

        // Destination rect (where on screen)
        var destRect = new Rectangle(screenW - ViewPx - Padding, Padding, ViewPx, ViewPx);

        // Background + border
        sb.Draw(_pixel, new Rectangle(destRect.X - 3, destRect.Y - 3,
            destRect.Width + 6, destRect.Height + 6), Color.Black);
        sb.Draw(_pixel, new Rectangle(destRect.X - 1, destRect.Y - 1,
            destRect.Width + 2, destRect.Height + 2), new Color(50, 50, 50));

        // Minimap texture
        sb.Draw(_mapTex, destRect, srcRect, Color.White);

        // Markers
        float time = (float)gt.TotalGameTime.TotalSeconds;
        var markerQ = new QueryDescription().WithAll<Position, MinimapMarker>();

        world.Query(in markerQ, (ref Position pos, ref MinimapMarker m) =>
        {
            int tx = (int)pos.TilePosition.X;
            int ty = (int)pos.TilePosition.Y;

            if (m.RequiresExplored && fog[tx, ty] != FogState.Visible) return;
            if (!srcRect.Contains(tx, ty)) return;

            float nx = (pos.TilePosition.X - srcRect.X) / (float)srcRect.Width;
            float ny = (pos.TilePosition.Y - srcRect.Y) / (float)srcRect.Height;
            var sp = new Vector2(destRect.X + nx * destRect.Width,
                                 destRect.Y + ny * destRect.Height);

            Color tint = m.Tint;
            if (m.Pulse) tint *= 0.6f + 0.4f * MathF.Sin(time * 5f);

            var tex = _icons[(int)m.Icon];
            sb.Draw(tex, sp, null, tint, 0f,
                new Vector2(tex.Width / 2f, tex.Height / 2f),
                1f, SpriteEffects.None, 0f);
        });

        // Player arrow (always on top)
        {
            float nx = (playerPos.X - srcRect.X) / (float)srcRect.Width;
            float ny = (playerPos.Y - srcRect.Y) / (float)srcRect.Height;
            var sp = new Vector2(destRect.X + nx * destRect.Width,
                                 destRect.Y + ny * destRect.Height);
            var origin = new Vector2(_arrowTex.Width / 2f, _arrowTex.Height / 2f);
            sb.Draw(_arrowTex, sp, null, Color.White, playerAngle, origin,
                1f, SpriteEffects.None, 0f);
        }
    }
}
```

### Wiring It Up

```csharp
// In Game.LoadContent or map load:
var minimap = new DungeonMinimap(GraphicsDevice, pixelTex, arrowTex, iconTextures);
minimap.Build(GraphicsDevice, tileGrid, fogGrid);

// Create entities with markers:
world.Create(new Position(new Vector2(15, 22)),
             new MinimapMarker(MinimapIcon.EnemyDot, Color.Red, RequiresExplored: true));

world.Create(new Position(new Vector2(30, 10)),
             new MinimapMarker(MinimapIcon.QuestStar, Color.Yellow, Pulse: true));

// In Game.Draw:
spriteBatch.Begin(samplerState: SamplerState.PointClamp);
minimap.Draw(spriteBatch, world, fogGrid, GraphicsDevice.Viewport.Width, gameTime);
spriteBatch.End();

// When a door opens at tile (12, 8):
minimap.PatchTile(12, 8, 0 /* floor */, fogGrid[12, 8]);
```

---

## Design Notes

- **Performance:** A pixel-per-tile minimap for a 512×512 world is only 1 MB. `SetData` is a CPU→GPU upload but negligible at these sizes. For worlds larger than ~2048², consider tiled/chunked minimap textures.
- **Zoom presets:** Give players 2–3 zoom presets (close / medium / far) rather than smooth zoom. Snapping feels better and avoids sub-pixel jitter.
- **Rotation:** Some games rotate the minimap so "up" always matches the player's facing direction. This is a simple rotation transform on the source rect and arrow angle, but test for player comfort — many find it disorienting.
- **Render order:** Background → minimap texture → markers → player arrow → border/frame. Drawing the frame last hides any marker bleed at edges.
- **Accessibility:** Don't rely on color alone for marker types. Use distinct shapes (dot, diamond, star, arrow) and consider a high-contrast mode.
- **SafeArea integration:** On devices with notches or Dynamic Island (iOS), position the minimap relative to safe area insets rather than raw screen edges. This prevents the minimap from being occluded:
  ```csharp
  // Position minimap inside safe area
  float safeRight = SafeArea.Right;   // pixels from right edge
  float safeTop = SafeArea.Top;       // pixels from top edge
  var destRect = new Rectangle(
      (int)(virtualWidth - minimapSize - padding - safeRight),
      (int)(padding + safeTop),
      minimapSize, minimapSize);
  ```
- **Interactive elements:** Minimaps can host lightweight UI controls beyond passive display. A wind direction slider, zoom toggle, or compass overlay adds gameplay value without requiring a separate HUD panel. Use a `ConsumedInput` flag to prevent minimap interactions from propagating to the game world:
  ```csharp
  public bool ConsumedInput { get; private set; }

  public void HandleInput(InputManager input)
  {
      ConsumedInput = false;
      if (IsSliderDragging || sliderBounds.Contains(input.MousePosition))
      {
          // Handle slider interaction
          ConsumedInput = true; // block downstream input processing
      }
  }
  ```
