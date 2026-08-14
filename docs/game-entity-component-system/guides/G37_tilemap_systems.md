# G37 — Tilemap Systems & Tiled Integration



> **Category:** Guide · **Related:** [G2 Rendering & Graphics](./G2_rendering_and_graphics.md) · [G8 Content Pipeline](./G8_content_pipeline.md) · [G3 Physics & Collision](./G3_physics_and_collision.md) · [G28 3/4 Top-Down Perspective](./G28_top_down_perspective.md)

---

## Table of Contents

1. [Overview](#overview)
2. [Tiled (.tmx) Loading](#1-tiled-tmx-loading)
3. [Tilemap Rendering](#2-tilemap-rendering)
4. [Autotiling](#3-autotiling)
5. [Tile Collision](#4-tile-collision)
6. [Chunk-Based Streaming](#5-chunk-based-streaming)
7. [Animated Tiles](#6-animated-tiles)
8. [Tile Properties & Metadata](#7-tile-properties--metadata)
9. [ECS Integration](#8-ecs-integration)
10. [Multiple Tile Layers](#9-multiple-tile-layers)
11. [Isometric & Hexagonal Tilemaps](#10-isometric--hexagonal-tilemaps)
12. [Performance Checklist](#performance-checklist)

---

## Overview

Tilemaps are the backbone of most 2D games — they define the world geometry, collision surfaces, visual layers, and spawn logic. This guide covers everything from loading Tiled editor maps into MonoGame, to efficient rendering with viewport culling, autotiling with bitmasks, collision extraction, chunk streaming for large worlds, animated tiles, and full Arch ECS integration.

**Key dependencies:**

| Package | Purpose |
|---|---|
| `MonoGame.Framework.DesktopGL` | Core framework, SpriteBatch rendering |
| `MonoGame.Extended` | Tiled map loading (`TiledMap`), cameras |
| `MonoGame.Extended.Tiled` | `.tmx` / `.tsx` content pipeline processors |
| `Arch` (v2.1.0) | ECS world, queries, components |

---

## 1 — Tiled (.tmx) Loading

### 1.1 Content Pipeline Setup

Add the MonoGame.Extended content pipeline references to your `.mgcb` file:

```
#-------------------------------- References --------------------------------#

/reference:..\..\packages\MonoGame.Extended.Content.Pipeline\lib\MonoGame.Extended.Content.Pipeline.dll

#---------------------------------- Content ---------------------------------#

/importer:TiledMapImporter
/processor:TiledMapProcessor
/build:Maps/world.tmx

/importer:TiledMapTilesetImporter
/processor:TiledMapTilesetProcessor
/build:Tilesets/terrain.tsx
```

**Directory structure convention:**

```
Content/
  Maps/
    world.tmx
    dungeon.tmx
  Tilesets/
    terrain.tsx
    terrain.png
    objects.tsx
    objects.png
```

> **Tip:** Tiled saves tileset image paths as relative. Keep `.tsx` and `.png` siblings or adjust paths in the `.tmx` to match your Content folder layout.

### 1.2 Loading via MonoGame.Extended

```csharp
using MonoGame.Extended.Tiled;
using MonoGame.Extended.Tiled.Renderers;

public class MapLoadingSystem
{
    private readonly World _world;

    public MapLoadingSystem(World world)
    {
        _world = world;
    }

    public TiledMap LoadMap(ContentManager content, string mapAsset)
    {
        // Load through the content pipeline (pre-processed .xnb)
        TiledMap map = content.Load<TiledMap>(mapAsset);

        // Inspect layers
        foreach (TiledMapTileLayer tileLayer in map.TileLayers)
        {
            Console.WriteLine($"Tile layer: {tileLayer.Name} " +
                $"({tileLayer.Width}x{tileLayer.Height})");
        }

        foreach (TiledMapObjectLayer objLayer in map.ObjectLayers)
        {
            Console.WriteLine($"Object layer: {objLayer.Name} " +
                $"({objLayer.Objects.Length} objects)");
        }

        return map;
    }
}
```

### 1.3 Manual XML Parsing (Pipeline-Free)

When you need runtime map loading (modding, procedural maps, hot-reload):

```csharp
using System.Xml.Linq;

public sealed class RawTiledLoader
{
    public record struct RawTileset(int FirstGid, int TileWidth, int TileHeight,
        int Columns, string ImageSource);

    public record struct RawTileLayer(string Name, int Width, int Height,
        int[] GidData);

    public record struct RawMapObject(int Id, string Name, string Type,
        float X, float Y, float Width, float Height,
        Dictionary<string, string> Properties);

    public record struct RawMap(int Width, int Height, int TileWidth, int TileHeight,
        List<RawTileset> Tilesets, List<RawTileLayer> TileLayers,
        List<List<RawMapObject>> ObjectLayers);

    public static RawMap Load(string tmxPath)
    {
        XDocument doc = XDocument.Load(tmxPath);
        XElement root = doc.Root!;

        int mapW = int.Parse(root.Attribute("width")!.Value);
        int mapH = int.Parse(root.Attribute("height")!.Value);
        int tileW = int.Parse(root.Attribute("tilewidth")!.Value);
        int tileH = int.Parse(root.Attribute("tileheight")!.Value);

        // Parse tilesets
        var tilesets = new List<RawTileset>();
        foreach (XElement ts in root.Elements("tileset"))
        {
            int firstGid = int.Parse(ts.Attribute("firstgid")!.Value);
            int tw = int.Parse(ts.Attribute("tilewidth")?.Value ?? tileW.ToString());
            int th = int.Parse(ts.Attribute("tileheight")?.Value ?? tileH.ToString());
            int cols = int.Parse(ts.Attribute("columns")?.Value ?? "1");
            string imgSrc = ts.Element("image")?.Attribute("source")?.Value ?? "";
            tilesets.Add(new RawTileset(firstGid, tw, th, cols, imgSrc));
        }

        // Parse tile layers (CSV encoding)
        var tileLayers = new List<RawTileLayer>();
        foreach (XElement layer in root.Elements("layer"))
        {
            string name = layer.Attribute("name")!.Value;
            int w = int.Parse(layer.Attribute("width")!.Value);
            int h = int.Parse(layer.Attribute("height")!.Value);

            string csv = layer.Element("data")!.Value.Trim();
            int[] gids = csv.Split(',')
                .Select(s => int.Parse(s.Trim()))
                .ToArray();

            tileLayers.Add(new RawTileLayer(name, w, h, gids));
        }

        // Parse object layers
        var objectLayers = new List<List<RawMapObject>>();
        foreach (XElement objGroup in root.Elements("objectgroup"))
        {
            var objects = new List<RawMapObject>();
            foreach (XElement obj in objGroup.Elements("object"))
            {
                var props = new Dictionary<string, string>();
                XElement? propsEl = obj.Element("properties");
                if (propsEl != null)
                {
                    foreach (XElement p in propsEl.Elements("property"))
                    {
                        props[p.Attribute("name")!.Value] =
                            p.Attribute("value")?.Value ?? p.Value;
                    }
                }

                objects.Add(new RawMapObject(
                    int.Parse(obj.Attribute("id")!.Value),
                    obj.Attribute("name")?.Value ?? "",
                    obj.Attribute("type")?.Value ?? "",
                    float.Parse(obj.Attribute("x")?.Value ?? "0"),
                    float.Parse(obj.Attribute("y")?.Value ?? "0"),
                    float.Parse(obj.Attribute("width")?.Value ?? "0"),
                    float.Parse(obj.Attribute("height")?.Value ?? "0"),
                    props
                ));
            }
            objectLayers.Add(objects);
        }

        return new RawMap(mapW, mapH, tileW, tileH, tilesets, tileLayers, objectLayers);
    }
}
```

### 1.4 JSON Format Loading

Tiled also exports `.tmj` (JSON). Useful for web builds or when you want `System.Text.Json`:

```csharp
using System.Text.Json;

public static RawMap LoadJson(string tmjPath)
{
    string json = File.ReadAllText(tmjPath);
    using JsonDocument doc = JsonDocument.Parse(json);
    JsonElement root = doc.RootElement;

    int mapW = root.GetProperty("width").GetInt32();
    int mapH = root.GetProperty("height").GetInt32();
    int tileW = root.GetProperty("tilewidth").GetInt32();
    int tileH = root.GetProperty("tileheight").GetInt32();

    // Similar extraction from root.GetProperty("layers"), "tilesets", etc.
    // Each tile layer has a "data" array of ints
    // Each object layer has an "objects" array
    // ... (follows same structure as XML)

    return new RawMap(mapW, mapH, tileW, tileH, /* ... */);
}
```

---

## 2 — Tilemap Rendering

### 2.1 ECS Components

```csharp
using Arch.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended.Tiled;

// Represents a loaded tilemap resource attached to an entity
public record struct TilemapComponent(
    TiledMap Map,
    TiledMapRenderer Renderer
);

// Camera bounds used for culling
public record struct CameraBounds(
    float X, float Y, float Width, float Height
);

// Tag for the active tilemap entity
public record struct ActiveMap;
```

### 2.2 MonoGame.Extended Renderer (Quick Start)

```csharp
public class TilemapRenderSystem
{
    private readonly World _world;
    private readonly QueryDescription _query = new QueryDescription()
        .WithAll<TilemapComponent, ActiveMap>();

    public TilemapRenderSystem(World world)
    {
        _world = world;
    }

    public void Update(GameTime gameTime, Matrix viewMatrix)
    {
        _world.Query(in _query, (ref TilemapComponent tilemap) =>
        {
            tilemap.Renderer.Update(gameTime);
        });
    }

    public void Draw(Matrix viewMatrix)
    {
        _world.Query(in _query, (ref TilemapComponent tilemap) =>
        {
            tilemap.Renderer.Draw(viewMatrix);
        });
    }
}
```

### 2.3 Manual SpriteBatch Rendering with Viewport Culling

For full control over draw order and culling:

```csharp
public sealed class ManualTilemapRenderer
{
    private readonly SpriteBatch _spriteBatch;

    public ManualTilemapRenderer(SpriteBatch spriteBatch)
    {
        _spriteBatch = spriteBatch;
    }

    /// <summary>
    /// Renders only tiles visible within the camera rectangle.
    /// </summary>
    public void DrawLayer(
        TiledMapTileLayer layer,
        TiledMap map,
        Texture2D tilesetTexture,
        int tilesetFirstGid,
        int tilesetColumns,
        Rectangle cameraBounds)
    {
        int tileW = map.TileWidth;
        int tileH = map.TileHeight;

        // Calculate visible tile range (clamp to layer bounds)
        int startCol = Math.Max(0, cameraBounds.X / tileW);
        int startRow = Math.Max(0, cameraBounds.Y / tileH);
        int endCol = Math.Min(layer.Width - 1,
            (cameraBounds.X + cameraBounds.Width) / tileW);
        int endRow = Math.Min(layer.Height - 1,
            (cameraBounds.Y + cameraBounds.Height) / tileH);

        for (int row = startRow; row <= endRow; row++)
        {
            for (int col = startCol; col <= endCol; col++)
            {
                TiledMapTile? tile = layer.GetTile((ushort)col, (ushort)row);
                if (tile == null || tile.Value.GlobalIdentifier == 0)
                    continue;

                int gid = tile.Value.GlobalIdentifier;
                int localId = gid - tilesetFirstGid;
                if (localId < 0) continue;

                // Calculate source rect in tileset texture
                int srcX = (localId % tilesetColumns) * tileW;
                int srcY = (localId / tilesetColumns) * tileH;
                Rectangle sourceRect = new(srcX, srcY, tileW, tileH);

                // Destination in world space
                Vector2 position = new(col * tileW, row * tileH);

                // Handle flipping flags
                SpriteEffects effects = SpriteEffects.None;
                if (tile.Value.IsFlippedHorizontally)
                    effects |= SpriteEffects.FlipHorizontally;
                if (tile.Value.IsFlippedVertically)
                    effects |= SpriteEffects.FlipVertically;

                _spriteBatch.Draw(
                    tilesetTexture,
                    position,
                    sourceRect,
                    Color.White,
                    0f,
                    Vector2.Zero,
                    1f,
                    effects,
                    0f
                );
            }
        }
    }

    /// <summary>
    /// Full draw call with SpriteBatch begin/end and camera transform.
    /// </summary>
    public void DrawAllLayers(
        TiledMap map,
        Texture2D tilesetTexture,
        int tilesetFirstGid,
        int tilesetColumns,
        Matrix cameraTransform,
        Rectangle cameraBounds,
        string[] layerOrder)
    {
        _spriteBatch.Begin(
            SpriteSortMode.Deferred,
            BlendState.AlphaBlend,
            SamplerState.PointClamp,  // Pixel-perfect for tiles
            null, null, null,
            cameraTransform
        );

        foreach (string layerName in layerOrder)
        {
            TiledMapTileLayer? layer = map.TileLayers
                .FirstOrDefault(l => l.Name == layerName);
            if (layer == null || !layer.IsVisible) continue;

            DrawLayer(layer, map, tilesetTexture,
                tilesetFirstGid, tilesetColumns, cameraBounds);
        }

        _spriteBatch.End();
    }
}
```

### 2.4 Culling Math Helper

```csharp
public static class TileCulling
{
    /// <summary>
    /// Returns the visible tile range given a camera rect and tile size.
    /// Adds a 1-tile margin to avoid pop-in at edges.
    /// </summary>
    public static (int StartCol, int StartRow, int EndCol, int EndRow)
        GetVisibleRange(Rectangle cameraBounds, int tileW, int tileH,
            int mapCols, int mapRows)
    {
        int startCol = Math.Max(0, cameraBounds.X / tileW - 1);
        int startRow = Math.Max(0, cameraBounds.Y / tileH - 1);
        int endCol = Math.Min(mapCols - 1,
            (cameraBounds.X + cameraBounds.Width) / tileW + 1);
        int endRow = Math.Min(mapRows - 1,
            (cameraBounds.Y + cameraBounds.Height) / tileH + 1);

        return (startCol, startRow, endCol, endRow);
    }
}
```

---

## 3 — Autotiling

### 3.1 Concepts

Autotiling automatically selects the correct tile sprite based on neighboring tiles. Two common schemes:

| Scheme | Bits | Neighbors Checked | Tile Variants |
|---|---|---|---|
| **4-bit (simple)** | 4 | Cardinal (N, E, S, W) | 16 |
| **8-bit (full)** | 8 | Cardinal + Diagonal | 47 unique (256 raw, collapsed) |

### 3.2 ECS Components

```csharp
// Marks tiles that participate in autotiling
public record struct AutotileTag(int TileType);

// Stores the computed bitmask for a tile position
public record struct AutotileMask(byte Mask);

// Autotile ruleset resource — maps bitmask → tile index
public record struct AutotileRuleset(
    int TileType,
    Dictionary<byte, int> MaskToTileIndex
);
```

### 3.3 4-Bit Bitmask Computation

```csharp
public static class Autotiler
{
    // Cardinal direction bit flags
    private const byte North = 1;  // bit 0
    private const byte East  = 2;  // bit 1
    private const byte South = 4;  // bit 2
    private const byte West  = 8;  // bit 3

    /// <summary>
    /// Computes a 4-bit bitmask based on cardinal neighbors matching
    /// the same tile type.
    /// </summary>
    public static byte Compute4BitMask(int[,] tileTypes, int col, int row,
        int targetType)
    {
        int rows = tileTypes.GetLength(0);
        int cols = tileTypes.GetLength(1);
        byte mask = 0;

        if (row > 0 && tileTypes[row - 1, col] == targetType)
            mask |= North;
        if (col < cols - 1 && tileTypes[row, col + 1] == targetType)
            mask |= East;
        if (row < rows - 1 && tileTypes[row + 1, col] == targetType)
            mask |= South;
        if (col > 0 && tileTypes[row, col - 1] == targetType)
            mask |= West;

        return mask;
    }

    /// <summary>
    /// Standard 4-bit lookup table for a 16-tile autotile tileset.
    /// Index = bitmask value, Value = tile index in the tileset.
    /// Arrange your tileset to match this mapping.
    /// </summary>
    public static readonly int[] Lookup4Bit = new int[16]
    {
        //  0=none, 1=N, 2=E, 3=NE, 4=S, 5=NS, 6=SE, 7=NSE,
        //  8=W, 9=NW, 10=EW, 11=NEW, 12=SW, 13=NSW, 14=SEW, 15=NSEW
             0,      1,    2,     3,    4,    5,     6,      7,
             8,      9,   10,    11,   12,   13,    14,     15
    };
}
```

### 3.4 8-Bit Bitmask Computation

```csharp
public static class Autotiler8Bit
{
    // Bit positions: N=0, NE=1, E=2, SE=3, S=4, SW=5, W=6, NW=7
    private static readonly (int dr, int dc, byte bit)[] Neighbors =
    {
        (-1,  0, 1 << 0),  // N
        (-1,  1, 1 << 1),  // NE
        ( 0,  1, 1 << 2),  // E
        ( 1,  1, 1 << 3),  // SE
        ( 1,  0, 1 << 4),  // S
        ( 1, -1, 1 << 5),  // SW
        ( 0, -1, 1 << 6),  // W
        (-1, -1, 1 << 7),  // NW
    };

    /// <summary>
    /// Computes an 8-bit bitmask. Diagonal neighbors are only counted
    /// if both adjacent cardinal neighbors are also present (standard
    /// corner-collapse rule).
    /// </summary>
    public static byte Compute8BitMask(int[,] tileTypes, int col, int row,
        int targetType)
    {
        int rows = tileTypes.GetLength(0);
        int cols = tileTypes.GetLength(1);

        bool Match(int r, int c) =>
            r >= 0 && r < rows && c >= 0 && c < cols &&
            tileTypes[r, c] == targetType;

        bool n  = Match(row - 1, col);
        bool e  = Match(row, col + 1);
        bool s  = Match(row + 1, col);
        bool w  = Match(row, col - 1);
        bool ne = Match(row - 1, col + 1);
        bool se = Match(row + 1, col + 1);
        bool sw = Match(row + 1, col - 1);
        bool nw = Match(row - 1, col - 1);

        byte mask = 0;
        if (n) mask |= 1 << 0;
        if (e) mask |= 1 << 2;
        if (s) mask |= 1 << 4;
        if (w) mask |= 1 << 6;

        // Diagonals only if both adjacent cardinals are set
        if (ne && n && e) mask |= 1 << 1;
        if (se && s && e) mask |= 1 << 3;
        if (sw && s && w) mask |= 1 << 5;
        if (nw && n && w) mask |= 1 << 7;

        return mask;
    }

    /// <summary>
    /// Collapses 256 raw masks into 47 unique tile indices.
    /// Build this table from your tileset layout.
    /// </summary>
    public static Dictionary<byte, int> Build47TileLookup()
    {
        // This would be populated based on your specific tileset arrangement.
        // The 47-tile minimal set covers all unique neighbor configurations
        // after corner-collapse.
        var lookup = new Dictionary<byte, int>();

        // Example entries (fill all 47):
        lookup[0b_0000_0000] = 0;   // Isolated
        lookup[0b_0001_0100] = 1;   // N+S (vertical corridor)
        lookup[0b_0100_0001] = 2;   // E+W (horizontal corridor)
        lookup[0b_0101_0101] = 3;   // All cardinals, no corners
        lookup[0b_1111_1111] = 46;  // Fully surrounded

        // ... remaining 42 entries based on tileset
        return lookup;
    }
}
```

### 3.5 Wang Tile Support

Wang tiles use edge/corner color matching rather than bitmasks. Tiled supports Wang tilesets natively:

```csharp
public record struct WangTile(
    int TileId,
    byte[] EdgeColors  // [Top, TopRight, Right, BottomRight, Bottom, BottomLeft, Left, TopLeft]
);

public static class WangTileResolver
{
    /// <summary>
    /// Finds a Wang tile whose edges match the required colors
    /// from adjacent tiles.
    /// </summary>
    public static int Resolve(WangTile[] wangTiles, byte[] requiredEdges)
    {
        foreach (WangTile wt in wangTiles)
        {
            bool match = true;
            for (int i = 0; i < 8; i++)
            {
                // 0 = wildcard (any color matches)
                if (requiredEdges[i] != 0 && wt.EdgeColors[i] != requiredEdges[i])
                {
                    match = false;
                    break;
                }
            }
            if (match) return wt.TileId;
        }
        return -1; // No match found — use fallback tile
    }
}
```

### 3.6 Autotile System (Full ECS Integration)

```csharp
public sealed class AutotileSystem
{
    private readonly int[,] _tileTypes;
    private readonly Dictionary<int, Dictionary<byte, int>> _rulesets;
    private readonly int _cols;
    private readonly int _rows;

    public AutotileSystem(int cols, int rows,
        Dictionary<int, Dictionary<byte, int>> rulesets)
    {
        _cols = cols;
        _rows = rows;
        _tileTypes = new int[rows, cols];
        _rulesets = rulesets;
    }

    public void SetTile(int col, int row, int tileType)
    {
        _tileTypes[row, col] = tileType;
    }

    /// <summary>
    /// Recomputes autotile indices for a region (e.g., after editing).
    /// Returns (col, row, newTileIndex) tuples for tiles that changed.
    /// </summary>
    public List<(int Col, int Row, int TileIndex)> RecomputeRegion(
        int startCol, int startRow, int endCol, int endRow)
    {
        var changes = new List<(int, int, int)>();

        for (int row = startRow; row <= endRow; row++)
        {
            for (int col = startCol; col <= endCol; col++)
            {
                int type = _tileTypes[row, col];
                if (type == 0 || !_rulesets.TryGetValue(type, out var lookup))
                    continue;

                byte mask = Autotiler.Compute4BitMask(
                    _tileTypes, col, row, type);

                if (lookup.TryGetValue(mask, out int tileIndex))
                {
                    changes.Add((col, row, tileIndex));
                }
            }
        }

        return changes;
    }
}
```

---

## 4 — Tile Collision

### 4.1 ECS Collision Components

```csharp
using Microsoft.Xna.Framework;

// Per-tile collision flag stored in a flat array
public record struct TileCollisionGrid(
    int Cols,
    int Rows,
    int TileWidth,
    int TileHeight,
    bool[] Solid  // row * Cols + col
);

// Freeform collision shape extracted from Tiled object layer
public record struct CollisionShape(
    Vector2 Position,
    Vector2 Size,
    CollisionShapeType ShapeType,
    Vector2[]? Polygon  // null unless ShapeType == Polygon
);

public enum CollisionShapeType
{
    Rectangle,
    Ellipse,
    Polygon,
    Polyline
}

// Tile collision flags (bitfield for per-tile properties)
[Flags]
public enum TileFlags : byte
{
    None     = 0,
    Solid    = 1 << 0,
    Platform = 1 << 1,  // One-way platform (solid from top only)
    Hazard   = 1 << 2,
    Ladder   = 1 << 3,
    Water    = 1 << 4,
    Slope    = 1 << 5,
}

public record struct TileFlagGrid(
    int Cols, int Rows, int TileWidth, int TileHeight,
    TileFlags[] Flags
);
```

### 4.2 Extracting Collision from Tiled Object Layers

```csharp
using MonoGame.Extended.Tiled;

public static class TileCollisionExtractor
{
    /// <summary>
    /// Builds a boolean collision grid from a tile layer where any
    /// non-empty tile is considered solid.
    /// </summary>
    public static TileCollisionGrid BuildFromTileLayer(
        TiledMapTileLayer layer, TiledMap map)
    {
        bool[] solid = new bool[layer.Width * layer.Height];

        for (int row = 0; row < layer.Height; row++)
        {
            for (int col = 0; col < layer.Width; col++)
            {
                TiledMapTile? tile = layer.GetTile((ushort)col, (ushort)row);
                solid[row * layer.Width + col] =
                    tile.HasValue && tile.Value.GlobalIdentifier != 0;
            }
        }

        return new TileCollisionGrid(
            layer.Width, layer.Height,
            map.TileWidth, map.TileHeight, solid);
    }

    /// <summary>
    /// Extracts freeform collision shapes from a Tiled object layer.
    /// Supports rectangles, ellipses, polygons, and polylines.
    /// </summary>
    public static List<CollisionShape> ExtractObjectShapes(
        TiledMapObjectLayer objectLayer)
    {
        var shapes = new List<CollisionShape>();

        foreach (TiledMapObject obj in objectLayer.Objects)
        {
            CollisionShapeType shapeType;
            Vector2[]? polygon = null;

            if (obj is TiledMapPolygonObject polyObj)
            {
                shapeType = CollisionShapeType.Polygon;
                polygon = polyObj.Points
                    .Select(p => new Vector2(
                        p.X + obj.Position.X,
                        p.Y + obj.Position.Y))
                    .ToArray();
            }
            else if (obj is TiledMapPolylineObject lineObj)
            {
                shapeType = CollisionShapeType.Polyline;
                polygon = lineObj.Points
                    .Select(p => new Vector2(
                        p.X + obj.Position.X,
                        p.Y + obj.Position.Y))
                    .ToArray();
            }
            else if (obj is TiledMapEllipseObject)
            {
                shapeType = CollisionShapeType.Ellipse;
            }
            else
            {
                shapeType = CollisionShapeType.Rectangle;
            }

            shapes.Add(new CollisionShape(
                new Vector2(obj.Position.X, obj.Position.Y),
                new Vector2(obj.Size.Width, obj.Size.Height),
                shapeType,
                polygon
            ));
        }

        return shapes;
    }

    /// <summary>
    /// Builds a TileFlagGrid from tile custom properties.
    /// In Tiled, set custom properties on individual tiles in the tileset:
    ///   "solid" (bool), "platform" (bool), "hazard" (bool), etc.
    /// </summary>
    public static TileFlagGrid BuildFlagGrid(
        TiledMapTileLayer layer, TiledMap map)
    {
        var flags = new TileFlags[layer.Width * layer.Height];

        for (int row = 0; row < layer.Height; row++)
        {
            for (int col = 0; col < layer.Width; col++)
            {
                TiledMapTile? tile = layer.GetTile((ushort)col, (ushort)row);
                if (!tile.HasValue || tile.Value.GlobalIdentifier == 0)
                    continue;

                TileFlags f = TileFlags.None;

                // Access tileset tile properties
                TiledMapTileset? tileset = map.GetTilesetByTileGlobalIdentifier(
                    tile.Value.GlobalIdentifier);
                if (tileset != null)
                {
                    int localId = tile.Value.GlobalIdentifier -
                        tileset.FirstGlobalIdentifier;
                    TiledMapTilesetTile? tsTile = tileset.Tiles
                        .FirstOrDefault(t => t.LocalTileIdentifier == localId);

                    if (tsTile?.Properties != null)
                    {
                        if (tsTile.Properties.ContainsKey("solid"))
                            f |= TileFlags.Solid;
                        if (tsTile.Properties.ContainsKey("platform"))
                            f |= TileFlags.Platform;
                        if (tsTile.Properties.ContainsKey("hazard"))
                            f |= TileFlags.Hazard;
                        if (tsTile.Properties.ContainsKey("ladder"))
                            f |= TileFlags.Ladder;
                    }
                }

                flags[row * layer.Width + col] = f;
            }
        }

        return new TileFlagGrid(layer.Width, layer.Height,
            map.TileWidth, map.TileHeight, flags);
    }
}
```

### 4.3 Tile-Based Collision Queries

```csharp
public static class TileCollisionQuery
{
    /// <summary>
    /// Checks if a world-space rectangle intersects any solid tile.
    /// </summary>
    public static bool Overlaps(TileCollisionGrid grid, Rectangle worldRect)
    {
        int startCol = Math.Max(0, worldRect.Left / grid.TileWidth);
        int startRow = Math.Max(0, worldRect.Top / grid.TileHeight);
        int endCol = Math.Min(grid.Cols - 1, worldRect.Right / grid.TileWidth);
        int endRow = Math.Min(grid.Rows - 1, worldRect.Bottom / grid.TileHeight);

        for (int row = startRow; row <= endRow; row++)
        {
            for (int col = startCol; col <= endCol; col++)
            {
                if (grid.Solid[row * grid.Cols + col])
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Returns all solid tile rectangles that overlap a world-space rect.
    /// Useful for collision response (find penetration depth per tile).
    /// </summary>
    public static List<Rectangle> GetOverlappingTiles(
        TileCollisionGrid grid, Rectangle worldRect)
    {
        var result = new List<Rectangle>();
        int startCol = Math.Max(0, worldRect.Left / grid.TileWidth);
        int startRow = Math.Max(0, worldRect.Top / grid.TileHeight);
        int endCol = Math.Min(grid.Cols - 1, worldRect.Right / grid.TileWidth);
        int endRow = Math.Min(grid.Rows - 1, worldRect.Bottom / grid.TileHeight);

        for (int row = startRow; row <= endRow; row++)
        {
            for (int col = startCol; col <= endCol; col++)
            {
                if (grid.Solid[row * grid.Cols + col])
                {
                    result.Add(new Rectangle(
                        col * grid.TileWidth, row * grid.TileHeight,
                        grid.TileWidth, grid.TileHeight));
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Flag-based query: get all tiles with specific flags in a region.
    /// </summary>
    public static List<(int Col, int Row, TileFlags Flags)> QueryFlags(
        TileFlagGrid grid, Rectangle worldRect, TileFlags requiredFlags)
    {
        var result = new List<(int, int, TileFlags)>();
        int startCol = Math.Max(0, worldRect.Left / grid.TileWidth);
        int startRow = Math.Max(0, worldRect.Top / grid.TileHeight);
        int endCol = Math.Min(grid.Cols - 1, worldRect.Right / grid.TileWidth);
        int endRow = Math.Min(grid.Rows - 1, worldRect.Bottom / grid.TileHeight);

        for (int row = startRow; row <= endRow; row++)
        {
            for (int col = startCol; col <= endCol; col++)
            {
                TileFlags f = grid.Flags[row * grid.Cols + col];
                if ((f & requiredFlags) == requiredFlags)
                    result.Add((col, row, f));
            }
        }
        return result;
    }
}
```

---

## 5 — Chunk-Based Streaming

### 5.1 Components

```csharp
// Represents a chunk of the world map
public record struct MapChunk(
    int ChunkX,         // Chunk coordinate (not pixel)
    int ChunkY,
    int[,] TileData,    // Local tile GIDs [rows, cols]
    bool IsLoaded
);

// Singleton resource tracking which chunks are active
public record struct ChunkManager(
    int ChunkWidthTiles,   // e.g., 32
    int ChunkHeightTiles,  // e.g., 32
    int TileWidth,
    int TileHeight,
    int LoadRadius,        // Chunks around camera to keep loaded
    Dictionary<(int, int), MapChunk> LoadedChunks
);

// Attached to the camera entity
public record struct ChunkTracker(
    int CurrentChunkX,
    int CurrentChunkY
);
```

### 5.2 Chunk Streaming System

```csharp
public sealed class ChunkStreamingSystem
{
    private readonly World _world;
    private readonly Func<int, int, MapChunk> _chunkLoader;
    private readonly QueryDescription _cameraQuery = new QueryDescription()
        .WithAll<CameraBounds, ChunkTracker>();

    public ChunkStreamingSystem(World world,
        Func<int, int, MapChunk> chunkLoader)
    {
        _world = world;
        _chunkLoader = chunkLoader;
    }

    public void Update(ref ChunkManager manager, Vector2 cameraCenter)
    {
        int chunkPixelW = manager.ChunkWidthTiles * manager.TileWidth;
        int chunkPixelH = manager.ChunkHeightTiles * manager.TileHeight;

        int camChunkX = (int)MathF.Floor(cameraCenter.X / chunkPixelW);
        int camChunkY = (int)MathF.Floor(cameraCenter.Y / chunkPixelH);

        int radius = manager.LoadRadius;

        // Determine which chunks should be loaded
        var desired = new HashSet<(int, int)>();
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                desired.Add((camChunkX + dx, camChunkY + dy));
            }
        }

        // Unload chunks that are out of range
        var toUnload = manager.LoadedChunks.Keys
            .Where(k => !desired.Contains(k))
            .ToList();

        foreach (var key in toUnload)
        {
            manager.LoadedChunks.Remove(key);
            // Optional: pool the chunk arrays for reuse
        }

        // Load new chunks that are needed
        foreach (var key in desired)
        {
            if (!manager.LoadedChunks.ContainsKey(key))
            {
                MapChunk chunk = _chunkLoader(key.Item1, key.Item2);
                manager.LoadedChunks[key] = chunk;
            }
        }
    }

    /// <summary>
    /// Renders all loaded chunks. Each chunk only renders visible tiles.
    /// </summary>
    public void Draw(ChunkManager manager, SpriteBatch spriteBatch,
        Texture2D tileset, int tilesetColumns, int tilesetFirstGid,
        Rectangle cameraBounds)
    {
        foreach (var (pos, chunk) in manager.LoadedChunks)
        {
            if (!chunk.IsLoaded) continue;

            int offsetX = pos.Item1 * manager.ChunkWidthTiles * manager.TileWidth;
            int offsetY = pos.Item2 * manager.ChunkHeightTiles * manager.TileHeight;

            // Quick AABB check: skip entire chunk if off-screen
            Rectangle chunkBounds = new(
                offsetX, offsetY,
                manager.ChunkWidthTiles * manager.TileWidth,
                manager.ChunkHeightTiles * manager.TileHeight);

            if (!cameraBounds.Intersects(chunkBounds))
                continue;

            // Render visible tiles in this chunk
            int startCol = Math.Max(0,
                (cameraBounds.X - offsetX) / manager.TileWidth);
            int startRow = Math.Max(0,
                (cameraBounds.Y - offsetY) / manager.TileHeight);
            int endCol = Math.Min(manager.ChunkWidthTiles - 1,
                (cameraBounds.Right - offsetX) / manager.TileWidth);
            int endRow = Math.Min(manager.ChunkHeightTiles - 1,
                (cameraBounds.Bottom - offsetY) / manager.TileHeight);

            for (int row = startRow; row <= endRow; row++)
            {
                for (int col = startCol; col <= endCol; col++)
                {
                    int gid = chunk.TileData[row, col];
                    if (gid == 0) continue;

                    int localId = gid - tilesetFirstGid;
                    int srcX = (localId % tilesetColumns) * manager.TileWidth;
                    int srcY = (localId / tilesetColumns) * manager.TileHeight;

                    spriteBatch.Draw(tileset,
                        new Vector2(offsetX + col * manager.TileWidth,
                                    offsetY + row * manager.TileHeight),
                        new Rectangle(srcX, srcY,
                            manager.TileWidth, manager.TileHeight),
                        Color.White);
                }
            }
        }
    }
}
```

### 5.3 Async Chunk Loading

For truly large worlds, load chunks on a background thread:

```csharp
public sealed class AsyncChunkLoader
{
    private readonly ConcurrentDictionary<(int, int), Task<MapChunk>> _pending = new();
    private readonly Func<int, int, MapChunk> _loadFunc;

    public AsyncChunkLoader(Func<int, int, MapChunk> loadFunc)
    {
        _loadFunc = loadFunc;
    }

    public void RequestChunk(int cx, int cy)
    {
        var key = (cx, cy);
        _pending.TryAdd(key, Task.Run(() => _loadFunc(cx, cy)));
    }

    /// <summary>
    /// Polls for completed chunk loads. Call each frame.
    /// Returns chunks that are ready to be inserted.
    /// </summary>
    public List<MapChunk> CollectReady()
    {
        var ready = new List<MapChunk>();
        var completed = _pending
            .Where(kvp => kvp.Value.IsCompletedSuccessfully)
            .ToList();

        foreach (var kvp in completed)
        {
            ready.Add(kvp.Value.Result);
            _pending.TryRemove(kvp.Key, out _);
        }
        return ready;
    }
}
```

---

## 6 — Animated Tiles

### 6.1 Components

```csharp
// Defines animation frames for a tile type
public record struct TileAnimation(
    int[] FrameGids,        // GIDs of each frame
    float[] FrameDurations, // Duration per frame in seconds
    float TotalDuration     // Sum of FrameDurations (cached)
);

// Tracks global animation state (all animated tiles share this clock)
public record struct TileAnimationClock(
    float ElapsedTime
);

// Registry: maps a tile GID to its animation data
public record struct AnimatedTileRegistry(
    Dictionary<int, TileAnimation> Animations
);
```

### 6.2 Parsing Tiled Animation Data

In Tiled, tile animations are defined per-tile in the tileset. Each animation frame specifies a `tileid` and `duration` (in milliseconds).

```csharp
public static class TileAnimationParser
{
    /// <summary>
    /// Extracts animated tile data from a TiledMap's tilesets.
    /// </summary>
    public static AnimatedTileRegistry BuildRegistry(TiledMap map)
    {
        var animations = new Dictionary<int, TileAnimation>();

        foreach (TiledMapTileset tileset in map.Tilesets)
        {
            foreach (TiledMapTilesetTile tile in tileset.Tiles)
            {
                if (tile.AnimationFrames == null || tile.AnimationFrames.Count == 0)
                    continue;

                int gid = tile.LocalTileIdentifier +
                    tileset.FirstGlobalIdentifier;

                int[] frameGids = tile.AnimationFrames
                    .Select(f => f.LocalTileIdentifier +
                        tileset.FirstGlobalIdentifier)
                    .ToArray();

                float[] durations = tile.AnimationFrames
                    .Select(f => f.Duration / 1000f)  // ms → seconds
                    .ToArray();

                float total = durations.Sum();

                animations[gid] = new TileAnimation(
                    frameGids, durations, total);
            }
        }

        return new AnimatedTileRegistry(animations);
    }

    /// <summary>
    /// Given elapsed time, resolves which frame GID to display.
    /// </summary>
    public static int ResolveFrame(TileAnimation anim, float elapsedTime)
    {
        float t = elapsedTime % anim.TotalDuration;
        float accumulated = 0f;

        for (int i = 0; i < anim.FrameGids.Length; i++)
        {
            accumulated += anim.FrameDurations[i];
            if (t < accumulated)
                return anim.FrameGids[i];
        }

        return anim.FrameGids[^1]; // Fallback to last frame
    }
}
```

### 6.3 Animation System

```csharp
public sealed class TileAnimationSystem
{
    private float _elapsed;

    public void Update(float deltaTime)
    {
        _elapsed += deltaTime;
    }

    /// <summary>
    /// Resolves the current display GID for a tile.
    /// If the tile isn't animated, returns the original GID.
    /// </summary>
    public int GetDisplayGid(int originalGid, AnimatedTileRegistry registry)
    {
        if (registry.Animations.TryGetValue(originalGid, out TileAnimation anim))
        {
            return TileAnimationParser.ResolveFrame(anim, _elapsed);
        }
        return originalGid;
    }
}
```

### 6.4 Integration into Rendering

Modify the tile draw loop to resolve animated frames:

```csharp
// Inside the manual renderer's inner loop:
int gid = tile.Value.GlobalIdentifier;

// Resolve animation frame
gid = _animSystem.GetDisplayGid(gid, _animRegistry);

int localId = gid - tilesetFirstGid;
// ... rest of draw logic unchanged
```

---

## 7 — Tile Properties & Metadata

### 7.1 Custom Properties in Tiled

In the Tiled editor, you can set custom properties on:
- **Individual tiles** (in the tileset) — e.g., `solid=true`, `damage=10`
- **Objects** (in object layers) — e.g., `spawn_type=enemy`, `trigger_id=door_1`
- **Layers** — e.g., `render_order=foreground`, `parallax_factor=0.5`

### 7.2 ECS Components for Metadata

```csharp
// Spawn point extracted from Tiled
public record struct SpawnPoint(
    Vector2 Position,
    string EntityType,      // "player", "enemy_goblin", "npc_merchant"
    Dictionary<string, string> Properties
);

// Trigger zone from Tiled
public record struct TriggerZone(
    Rectangle Bounds,
    string TriggerId,
    string Action,          // "load_map", "cutscene", "damage"
    Dictionary<string, string> Properties
);

// Damage zone tile
public record struct DamageZone(
    int DamagePerSecond,
    string DamageType       // "fire", "poison", "spike"
);
```

### 7.3 Reading Properties from Tiled Objects

```csharp
public static class TiledPropertyReader
{
    /// <summary>
    /// Extracts spawn points from a named object layer.
    /// Convention: objects with Type="spawn" in Tiled.
    /// </summary>
    public static List<SpawnPoint> ReadSpawnPoints(
        TiledMapObjectLayer layer)
    {
        var spawns = new List<SpawnPoint>();

        foreach (TiledMapObject obj in layer.Objects)
        {
            if (!string.Equals(obj.Type, "spawn",
                StringComparison.OrdinalIgnoreCase))
                continue;

            var props = new Dictionary<string, string>();
            foreach (var kvp in obj.Properties)
            {
                props[kvp.Key] = kvp.Value.ToString() ?? "";
            }

            spawns.Add(new SpawnPoint(
                new Vector2(obj.Position.X, obj.Position.Y),
                obj.Name,
                props
            ));
        }

        return spawns;
    }

    /// <summary>
    /// Extracts trigger zones from a named object layer.
    /// Convention: objects with Type="trigger" in Tiled.
    /// </summary>
    public static List<TriggerZone> ReadTriggerZones(
        TiledMapObjectLayer layer)
    {
        var triggers = new List<TriggerZone>();

        foreach (TiledMapObject obj in layer.Objects)
        {
            if (!string.Equals(obj.Type, "trigger",
                StringComparison.OrdinalIgnoreCase))
                continue;

            string triggerId = obj.Properties.ContainsKey("trigger_id")
                ? obj.Properties["trigger_id"].ToString() ?? ""
                : obj.Name;

            string action = obj.Properties.ContainsKey("action")
                ? obj.Properties["action"].ToString() ?? ""
                : "";

            var props = new Dictionary<string, string>();
            foreach (var kvp in obj.Properties)
                props[kvp.Key] = kvp.Value.ToString() ?? "";

            triggers.Add(new TriggerZone(
                new Rectangle(
                    (int)obj.Position.X, (int)obj.Position.Y,
                    (int)obj.Size.Width, (int)obj.Size.Height),
                triggerId,
                action,
                props
            ));
        }

        return triggers;
    }

    /// <summary>
    /// Reads a typed property with a default fallback.
    /// </summary>
    public static T GetProperty<T>(TiledMapObject obj, string key, T defaultValue)
    {
        if (!obj.Properties.ContainsKey(key))
            return defaultValue;

        string raw = obj.Properties[key].ToString() ?? "";

        if (typeof(T) == typeof(int))
            return (T)(object)int.Parse(raw);
        if (typeof(T) == typeof(float))
            return (T)(object)float.Parse(raw);
        if (typeof(T) == typeof(bool))
            return (T)(object)bool.Parse(raw);
        if (typeof(T) == typeof(string))
            return (T)(object)raw;

        return defaultValue;
    }
}
```

---

## 8 — ECS Integration

### 8.1 Tilemap as a Singleton / Resource

The tilemap is a shared resource, not per-entity data. Use Arch's singleton pattern:

```csharp
// The tilemap resource — one per loaded map
public record struct TilemapResource(
    TiledMap Map,
    TileCollisionGrid CollisionGrid,
    TileFlagGrid FlagGrid,
    AnimatedTileRegistry AnimationRegistry,
    int TileWidth,
    int TileHeight,
    int WidthInTiles,
    int HeightInTiles
);

// Seed component — triggers map loading when spawned
public record struct MapSeed(
    string MapAsset,    // Content path, e.g. "Maps/world"
    Vector2 SpawnOffset
);

// Tag: attached to the map entity after loading completes
public record struct MapLoaded;
```

### 8.2 Map Loading System

```csharp
public sealed class MapLoadSystem
{
    private readonly World _world;
    private readonly ContentManager _content;
    private readonly GraphicsDevice _graphics;

    private readonly QueryDescription _seedQuery = new QueryDescription()
        .WithAll<MapSeed>()
        .WithNone<MapLoaded>();

    public MapLoadSystem(World world, ContentManager content,
        GraphicsDevice graphics)
    {
        _world = world;
        _content = content;
        _graphics = graphics;
    }

    public void Update()
    {
        // Process any unloaded map seeds
        var toLoad = new List<(Entity Entity, MapSeed Seed)>();

        _world.Query(in _seedQuery, (Entity entity, ref MapSeed seed) =>
        {
            toLoad.Add((entity, seed));
        });

        foreach (var (entity, seed) in toLoad)
        {
            TiledMap map = _content.Load<TiledMap>(seed.MapAsset);

            // Build collision grid from "Collision" layer
            TiledMapTileLayer? collisionLayer = map.TileLayers
                .FirstOrDefault(l => l.Name == "Collision");
            TileCollisionGrid collisionGrid = collisionLayer != null
                ? TileCollisionExtractor.BuildFromTileLayer(collisionLayer, map)
                : new TileCollisionGrid(0, 0, 0, 0, Array.Empty<bool>());

            // Build flag grid from "Ground" layer with tile properties
            TiledMapTileLayer? groundLayer = map.TileLayers
                .FirstOrDefault(l => l.Name == "Ground");
            TileFlagGrid flagGrid = groundLayer != null
                ? TileCollisionExtractor.BuildFlagGrid(groundLayer, map)
                : new TileFlagGrid(0, 0, 0, 0, Array.Empty<TileFlags>());

            // Build animation registry
            AnimatedTileRegistry animRegistry =
                TileAnimationParser.BuildRegistry(map);

            // Create the renderer
            var renderer = new TiledMapRenderer(_graphics, map);

            // Attach components to the map entity
            _world.Add(entity, new TilemapComponent(map, renderer));
            _world.Add(entity, new TilemapResource(
                map, collisionGrid, flagGrid, animRegistry,
                map.TileWidth, map.TileHeight,
                map.Width, map.Height));
            _world.Add(entity, new MapLoaded());
            _world.Add(entity, new ActiveMap());

            // Spawn entities from object layers
            SpawnEntitiesFromObjects(map, seed.SpawnOffset);
        }
    }

    private void SpawnEntitiesFromObjects(TiledMap map, Vector2 offset)
    {
        foreach (TiledMapObjectLayer objLayer in map.ObjectLayers)
        {
            if (objLayer.Name == "Spawns")
            {
                var spawns = TiledPropertyReader.ReadSpawnPoints(objLayer);
                foreach (SpawnPoint spawn in spawns)
                {
                    SpawnEntityFromPoint(spawn, offset);
                }
            }
            else if (objLayer.Name == "Triggers")
            {
                var triggers = TiledPropertyReader.ReadTriggerZones(objLayer);
                foreach (TriggerZone trigger in triggers)
                {
                    _world.Create(trigger);
                }
            }
        }
    }

    private void SpawnEntityFromPoint(SpawnPoint spawn, Vector2 offset)
    {
        Vector2 pos = spawn.Position + offset;

        // Use a factory pattern based on entity type
        switch (spawn.EntityType.ToLowerInvariant())
        {
            case "player":
                _world.Create(
                    new Position(pos),
                    new PlayerTag(),
                    new Velocity(Vector2.Zero)
                );
                break;

            case "enemy":
                string enemyType = spawn.Properties
                    .GetValueOrDefault("enemy_type", "basic");
                _world.Create(
                    new Position(pos),
                    new EnemyTag(enemyType),
                    new Velocity(Vector2.Zero),
                    new Health(
                        int.Parse(spawn.Properties
                            .GetValueOrDefault("hp", "100")))
                );
                break;

            // Add more entity types as needed
        }
    }
}

// Supporting components used above
public record struct Position(Vector2 Value);
public record struct Velocity(Vector2 Value);
public record struct PlayerTag;
public record struct EnemyTag(string EnemyType);
public record struct Health(int Current);
```

### 8.3 The MapSeed Pattern

The `MapSeed` pattern decouples map loading from game flow:

```csharp
// To load a new map, just create a seed entity:
world.Create(new MapSeed("Maps/dungeon_01", Vector2.Zero));

// The MapLoadSystem picks it up next frame, loads the map,
// and replaces the seed with full map data.

// To transition between maps:
public static class MapTransition
{
    public static void TransitionTo(World world, string newMapAsset,
        Vector2 spawnOffset)
    {
        // Destroy current map entity
        var activeQuery = new QueryDescription().WithAll<ActiveMap>();
        var toDestroy = new List<Entity>();
        world.Query(in activeQuery, (Entity e) => toDestroy.Add(e));
        foreach (Entity e in toDestroy)
            world.Destroy(e);

        // Spawn new map seed
        world.Create(new MapSeed(newMapAsset, spawnOffset));
    }
}
```

---

## 9 — Multiple Tile Layers

### 9.1 Layer Conventions

A typical Tiled map uses these layers (bottom to top):

| Layer Name | Type | Purpose | Draw Order |
|---|---|---|---|
| `Ground` | Tile | Base terrain | 0 (first) |
| `GroundDecor` | Tile | Flowers, cracks, puddles | 1 |
| `Collision` | Tile | Invisible collision tiles | Not rendered |
| `Objects` | Object | Spawn points, triggers | Not rendered |
| `EntityLayer` | — | Virtual: entities draw here | 2 |
| `ForegroundDecor` | Tile | Tree tops, overhangs | 3 (last) |

### 9.2 Layer-Aware Rendering System

```csharp
public sealed class LayeredTilemapRenderer
{
    private readonly SpriteBatch _spriteBatch;
    private readonly ManualTilemapRenderer _tileRenderer;
    private readonly TileAnimationSystem _animSystem;

    // Layers to draw BELOW entities
    private static readonly string[] BackgroundLayers =
        { "Ground", "GroundDecor" };

    // Layers to draw ABOVE entities
    private static readonly string[] ForegroundLayers =
        { "ForegroundDecor" };

    // Layers that are never rendered
    private static readonly HashSet<string> HiddenLayers =
        new() { "Collision", "Navigation" };

    public LayeredTilemapRenderer(SpriteBatch spriteBatch,
        TileAnimationSystem animSystem)
    {
        _spriteBatch = spriteBatch;
        _tileRenderer = new ManualTilemapRenderer(spriteBatch);
        _animSystem = animSystem;
    }

    /// <summary>
    /// Call this before drawing entities.
    /// </summary>
    public void DrawBackground(TiledMap map, Texture2D tileset,
        int tilesetFirstGid, int tilesetColumns,
        Matrix cameraTransform, Rectangle cameraBounds)
    {
        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend,
            SamplerState.PointClamp, null, null, null, cameraTransform);

        foreach (string layerName in BackgroundLayers)
        {
            DrawLayerIfExists(map, tileset, tilesetFirstGid,
                tilesetColumns, cameraBounds, layerName);
        }

        _spriteBatch.End();
    }

    /// <summary>
    /// Call this after drawing entities.
    /// </summary>
    public void DrawForeground(TiledMap map, Texture2D tileset,
        int tilesetFirstGid, int tilesetColumns,
        Matrix cameraTransform, Rectangle cameraBounds)
    {
        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend,
            SamplerState.PointClamp, null, null, null, cameraTransform);

        foreach (string layerName in ForegroundLayers)
        {
            DrawLayerIfExists(map, tileset, tilesetFirstGid,
                tilesetColumns, cameraBounds, layerName);
        }

        _spriteBatch.End();
    }

    private void DrawLayerIfExists(TiledMap map, Texture2D tileset,
        int tilesetFirstGid, int tilesetColumns,
        Rectangle cameraBounds, string layerName)
    {
        TiledMapTileLayer? layer = map.TileLayers
            .FirstOrDefault(l => l.Name == layerName);
        if (layer == null || !layer.IsVisible) return;

        _tileRenderer.DrawLayer(layer, map, tileset,
            tilesetFirstGid, tilesetColumns, cameraBounds);
    }
}
```

### 9.3 Entity-Tile Interleaving (Y-Sort)

For top-down games where entities walk behind/in front of tile objects:

```csharp
public record struct YSortable(float Y);

// In your main Draw method:
public void Draw(GameTime gameTime)
{
    // 1. Draw ground layers
    _layeredRenderer.DrawBackground(map, tileset, /* ... */);

    // 2. Draw entities sorted by Y position
    _spriteBatch.Begin(SpriteSortMode.FrontToBack, BlendState.AlphaBlend,
        SamplerState.PointClamp, null, null, null, cameraTransform);

    // Entities use layerDepth = Y / maxWorldHeight for correct sorting
    var entityQuery = new QueryDescription().WithAll<Position, Sprite>();
    _world.Query(in entityQuery, (ref Position pos, ref Sprite sprite) =>
    {
        float depth = pos.Value.Y / _maxWorldHeight; // 0..1
        _spriteBatch.Draw(sprite.Texture, pos.Value, sprite.SourceRect,
            Color.White, 0f, sprite.Origin, 1f, SpriteEffects.None, depth);
    });

    _spriteBatch.End();

    // 3. Draw foreground layers (tree tops, overhangs, roofs)
    _layeredRenderer.DrawForeground(map, tileset, /* ... */);
}

// Simple sprite component
public record struct Sprite(
    Texture2D Texture,
    Rectangle SourceRect,
    Vector2 Origin
);
```

---

## 10 — Isometric & Hexagonal Tilemaps

### 10.1 Isometric Coordinate Conversion

Isometric (diamond/staggered) maps use a skewed coordinate system. Tiled supports `orientation="isometric"` natively.

```csharp
public static class IsometricHelper
{
    /// <summary>
    /// Converts tile grid coordinates to screen/world pixel position.
    /// Standard diamond isometric projection.
    /// </summary>
    public static Vector2 TileToScreen(int col, int row,
        int tileWidth, int tileHeight)
    {
        float x = (col - row) * (tileWidth / 2f);
        float y = (col + row) * (tileHeight / 2f);
        return new Vector2(x, y);
    }

    /// <summary>
    /// Converts screen/world pixel position to tile grid coordinates.
    /// </summary>
    public static (int Col, int Row) ScreenToTile(Vector2 screenPos,
        int tileWidth, int tileHeight)
    {
        float halfW = tileWidth / 2f;
        float halfH = tileHeight / 2f;

        float col = (screenPos.X / halfW + screenPos.Y / halfH) / 2f;
        float row = (screenPos.Y / halfH - screenPos.X / halfW) / 2f;

        return ((int)MathF.Floor(col), (int)MathF.Floor(row));
    }

    /// <summary>
    /// Staggered isometric (used by Tiled's "staggered" orientation).
    /// Even rows are offset by half a tile width.
    /// </summary>
    public static Vector2 StaggeredTileToScreen(int col, int row,
        int tileWidth, int tileHeight)
    {
        float x = col * tileWidth + (row % 2 == 0 ? 0 : tileWidth / 2f);
        float y = row * (tileHeight / 2f);
        return new Vector2(x, y);
    }
}
```

### 10.2 Isometric Rendering Order

Isometric tiles must be drawn back-to-front (painter's algorithm):

```csharp
public sealed class IsometricTileRenderer
{
    private readonly SpriteBatch _spriteBatch;

    public IsometricTileRenderer(SpriteBatch spriteBatch)
    {
        _spriteBatch = spriteBatch;
    }

    /// <summary>
    /// Renders isometric tiles in correct back-to-front order.
    /// Iterates rows top-to-bottom, columns left-to-right.
    /// </summary>
    public void DrawIsometric(int[,] tileData, int cols, int rows,
        int tileWidth, int tileHeight,
        Texture2D tileset, int tilesetColumns, int tilesetFirstGid,
        Rectangle cameraBounds)
    {
        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                int gid = tileData[row, col];
                if (gid == 0) continue;

                Vector2 screenPos = IsometricHelper.TileToScreen(
                    col, row, tileWidth, tileHeight);

                // Cull: skip tiles whose screen position is outside camera
                if (screenPos.X + tileWidth < cameraBounds.Left ||
                    screenPos.X > cameraBounds.Right ||
                    screenPos.Y + tileHeight < cameraBounds.Top ||
                    screenPos.Y > cameraBounds.Bottom)
                    continue;

                int localId = gid - tilesetFirstGid;
                int srcX = (localId % tilesetColumns) * tileWidth;
                int srcY = (localId / tilesetColumns) * tileHeight;

                _spriteBatch.Draw(tileset, screenPos,
                    new Rectangle(srcX, srcY, tileWidth, tileHeight),
                    Color.White);
            }
        }
    }
}
```

### 10.3 Hexagonal Coordinate Conversion

Hexagonal grids use offset, axial, or cube coordinates. Tiled uses offset coordinates with a stagger axis and index.

```csharp
public static class HexHelper
{
    // --- Pointy-top hex (Tiled stagger axis = "y") ---

    /// <summary>
    /// Converts hex offset coordinates to pixel position (pointy-top).
    /// </summary>
    public static Vector2 HexToPixel(int col, int row,
        int hexWidth, int hexHeight)
    {
        // Hex dimensions: width = sqrt(3) * size, height = 2 * size
        // For Tiled: hexWidth and hexHeight come from the map properties
        float x = col * hexWidth + (row % 2 == 1 ? hexWidth / 2f : 0);
        float y = row * (hexHeight * 0.75f);
        return new Vector2(x, y);
    }

    /// <summary>
    /// Converts pixel position to hex offset coordinates (pointy-top).
    /// Uses the standard rounding approach via cube coordinates.
    /// </summary>
    public static (int Col, int Row) PixelToHex(Vector2 pixel,
        int hexWidth, int hexHeight)
    {
        float size = hexHeight / 2f;

        // Convert to axial coordinates
        float q = (pixel.X * MathF.Sqrt(3f) / 3f - pixel.Y / 3f) / size;
        float r = (pixel.Y * 2f / 3f) / size;

        // Round axial to cube then back to offset
        return AxialRoundToOffset(q, r);
    }

    private static (int Col, int Row) AxialRoundToOffset(float q, float r)
    {
        float s = -q - r;

        int rq = (int)MathF.Round(q);
        int rr = (int)MathF.Round(r);
        int rs = (int)MathF.Round(s);

        float dq = MathF.Abs(rq - q);
        float dr = MathF.Abs(rr - r);
        float ds = MathF.Abs(rs - s);

        if (dq > dr && dq > ds)
            rq = -rr - rs;
        else if (dr > ds)
            rr = -rq - rs;

        // Axial (q, r) to offset (col, row) — pointy-top, odd-row offset
        int col = rq + (rr - (rr & 1)) / 2;
        int row = rr;
        return (col, row);
    }

    // --- Flat-top hex (Tiled stagger axis = "x") ---

    public static Vector2 FlatHexToPixel(int col, int row,
        int hexWidth, int hexHeight)
    {
        float x = col * (hexWidth * 0.75f);
        float y = row * hexHeight + (col % 2 == 1 ? hexHeight / 2f : 0);
        return new Vector2(x, y);
    }

    /// <summary>
    /// Returns the 6 neighbor offsets for pointy-top odd-row hex grid.
    /// </summary>
    public static (int dc, int dr)[] GetNeighborOffsets(int row)
    {
        if (row % 2 == 0) // Even row
        {
            return new (int, int)[]
            {
                ( 0, -1), ( 1, -1),  // Top-left, top-right
                (-1,  0), ( 1,  0),  // Left, right
                ( 0,  1), ( 1,  1),  // Bottom-left, bottom-right
            };
        }
        else // Odd row
        {
            return new (int, int)[]
            {
                (-1, -1), ( 0, -1),
                (-1,  0), ( 1,  0),
                (-1,  1), ( 0,  1),
            };
        }
    }
}
```

### 10.4 Hexagonal Rendering

```csharp
public sealed class HexTileRenderer
{
    private readonly SpriteBatch _spriteBatch;

    public HexTileRenderer(SpriteBatch spriteBatch)
    {
        _spriteBatch = spriteBatch;
    }

    public void DrawHexMap(int[,] tileData, int cols, int rows,
        int hexWidth, int hexHeight,
        Texture2D tileset, int tilesetColumns, int tilesetFirstGid,
        Rectangle cameraBounds)
    {
        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                int gid = tileData[row, col];
                if (gid == 0) continue;

                Vector2 pos = HexHelper.HexToPixel(
                    col, row, hexWidth, hexHeight);

                // Viewport culling
                if (pos.X + hexWidth < cameraBounds.Left ||
                    pos.X > cameraBounds.Right ||
                    pos.Y + hexHeight < cameraBounds.Top ||
                    pos.Y > cameraBounds.Bottom)
                    continue;

                int localId = gid - tilesetFirstGid;
                int srcX = (localId % tilesetColumns) * hexWidth;
                int srcY = (localId / tilesetColumns) * hexHeight;

                _spriteBatch.Draw(tileset, pos,
                    new Rectangle(srcX, srcY, hexWidth, hexHeight),
                    Color.White);
            }
        }
    }
}
```

### 10.5 Hex Pathfinding Integration

```csharp
/// <summary>
/// A* pathfinding on a hex grid using axial/offset coordinates.
/// </summary>
public static class HexPathfinding
{
    public static List<(int Col, int Row)>? FindPath(
        int[,] costMap, int cols, int rows,
        (int Col, int Row) start, (int Col, int Row) goal)
    {
        var open = new PriorityQueue<(int, int), float>();
        var cameFrom = new Dictionary<(int, int), (int, int)>();
        var gScore = new Dictionary<(int, int), float>();

        open.Enqueue(start, 0);
        gScore[start] = 0;

        while (open.Count > 0)
        {
            var current = open.Dequeue();
            if (current == goal)
                return ReconstructPath(cameFrom, current);

            var neighborOffsets = HexHelper.GetNeighborOffsets(current.Item2);
            foreach (var (dc, dr) in neighborOffsets)
            {
                int nc = current.Item1 + dc;
                int nr = current.Item2 + dr;

                if (nc < 0 || nc >= cols || nr < 0 || nr >= rows)
                    continue;
                if (costMap[nr, nc] < 0) continue; // Impassable

                float tentative = gScore[current] + costMap[nr, nc];
                var neighbor = (nc, nr);

                if (!gScore.ContainsKey(neighbor) ||
                    tentative < gScore[neighbor])
                {
                    gScore[neighbor] = tentative;
                    float h = HexDistance(neighbor, goal);
                    cameFrom[neighbor] = current;
                    open.Enqueue(neighbor, tentative + h);
                }
            }
        }

        return null; // No path found
    }

    private static float HexDistance((int, int) a, (int, int) b)
    {
        // Manhattan distance approximation for hex grids
        int dc = Math.Abs(a.Item1 - b.Item1);
        int dr = Math.Abs(a.Item2 - b.Item2);
        return dc + Math.Max(0, (dr - dc) / 2f);
    }

    private static List<(int, int)> ReconstructPath(
        Dictionary<(int, int), (int, int)> cameFrom, (int, int) current)
    {
        var path = new List<(int, int)> { current };
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

## Performance Checklist

| Technique | Impact | Section |
|---|---|---|
| **Viewport culling** — only iterate visible tile range | ★★★ | §2.3, §2.4 |
| **SamplerState.PointClamp** — avoids tile-edge bleeding | ★★★ | §2.3 |
| **SpriteSortMode.Deferred** — batch draw calls | ★★★ | §2.3 |
| **Chunk AABB skip** — reject entire off-screen chunks | ★★☆ | §5.2 |
| **Async chunk loading** — no stalls on chunk boundaries | ★★☆ | §5.3 |
| **Animation clock sharing** — single timer, not per-tile | ★★☆ | §6.3 |
| **Flag grid bitfield** — cache-friendly collision queries | ★★☆ | §4.1 |
| **Autotile dirty regions** — recompute only changed area | ★☆☆ | §3.6 |
| **RenderTarget caching** — render static layers once | ★★★ | — |

### RenderTarget Caching for Static Layers

For layers that rarely change (ground, decoration), render them once to a `RenderTarget2D` and just draw the texture each frame:

```csharp
public sealed class CachedLayerRenderer
{
    private RenderTarget2D? _cachedTarget;
    private bool _isDirty = true;
    private readonly GraphicsDevice _graphics;
    private readonly SpriteBatch _spriteBatch;

    public CachedLayerRenderer(GraphicsDevice graphics, SpriteBatch spriteBatch)
    {
        _graphics = graphics;
        _spriteBatch = spriteBatch;
    }

    public void Invalidate() => _isDirty = true;

    public void EnsureCache(TiledMap map, Texture2D tileset,
        int firstGid, int columns)
    {
        if (!_isDirty && _cachedTarget != null) return;

        int pixelW = map.WidthInPixels;
        int pixelH = map.HeightInPixels;

        _cachedTarget?.Dispose();
        _cachedTarget = new RenderTarget2D(_graphics, pixelW, pixelH);

        _graphics.SetRenderTarget(_cachedTarget);
        _graphics.Clear(Color.Transparent);

        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend,
            SamplerState.PointClamp);

        // Draw all static layers to the render target
        foreach (TiledMapTileLayer layer in map.TileLayers)
        {
            if (layer.Name is "Collision" or "Navigation") continue;
            // Full-map draw (no culling needed — done once)
            var fullBounds = new Rectangle(0, 0, pixelW, pixelH);
            var renderer = new ManualTilemapRenderer(_spriteBatch);
            renderer.DrawLayer(layer, map, tileset, firstGid, columns,
                fullBounds);
        }

        _spriteBatch.End();
        _graphics.SetRenderTarget(null);
        _isDirty = false;
    }

    /// <summary>
    /// Draws the cached layer texture with camera transform applied.
    /// Single draw call for the entire map.
    /// </summary>
    public void Draw(Matrix cameraTransform)
    {
        if (_cachedTarget == null) return;

        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend,
            SamplerState.PointClamp, null, null, null, cameraTransform);

        _spriteBatch.Draw(_cachedTarget, Vector2.Zero, Color.White);

        _spriteBatch.End();
    }
}
```

> **Trade-off:** RenderTarget caching uses GPU memory proportional to map size. For maps larger than ~4096×4096 pixels, prefer chunk-based rendering (§5) instead. For small-to-medium maps, this is the single biggest performance win.

---

## Quick Reference: Tiled Layer Naming Convention

Adopt consistent layer names across all maps:

```
Ground          → Base terrain tiles
GroundDecor     → Ground-level decoration (flowers, cracks)
Collision       → Invisible solid tiles (hidden, used for collision grid)
Navigation      → Pathfinding cost data (hidden)
Spawns          → Object layer: spawn points (type="spawn")
Triggers        → Object layer: trigger zones (type="trigger")
Entities        → Object layer: interactive objects
ForegroundDecor → Tiles rendered above entities (tree canopy, roof edges)
Foreground      → Full foreground overlay
```

This convention lets systems automatically find the right layers by name, minimizing configuration and human error across maps.

---

*This guide is part of the Universal 2D Engine Toolkit documentation. For rendering pipeline details see [G2](./G2_rendering_and_graphics.md), for content loading see [G8](./G8_content_pipeline.md), for physics see [G3](./G3_physics_and_collision.md).*
