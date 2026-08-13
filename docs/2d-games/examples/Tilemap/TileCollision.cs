// ============================================================================
// TileCollision.cs — Tile Collision Grid & Queries
// Extracted from: G37 — Tilemap Systems & Tiled Integration
// Part of: Universal 2D Engine Toolkit Examples
// ============================================================================

using Microsoft.Xna.Framework;
using MonoGame.Extended.Tiled;

namespace U2DToolkit.Examples.Tilemap;

/// <summary>
/// Per-tile collision flag stored in a flat boolean array.
/// Built from a Tiled tile layer where any non-empty tile is solid.
/// </summary>
/// <param name="Cols">Number of tile columns in the grid.</param>
/// <param name="Rows">Number of tile rows in the grid.</param>
/// <param name="TileWidth">Width of each tile in pixels.</param>
/// <param name="TileHeight">Height of each tile in pixels.</param>
/// <param name="Solid">Flat array: <c>row * Cols + col</c> → is solid.</param>
public record struct TileCollisionGrid(
    int Cols,
    int Rows,
    int TileWidth,
    int TileHeight,
    bool[] Solid
);

/// <summary>
/// Bitfield flags for per-tile properties beyond simple solid/empty.
/// Supports one-way platforms, hazards, ladders, water, and slopes.
/// </summary>
[Flags]
public enum TileFlags : byte
{
    /// <summary>No special properties.</summary>
    None     = 0,
    /// <summary>Fully solid from all directions.</summary>
    Solid    = 1 << 0,
    /// <summary>One-way platform — solid from top only.</summary>
    Platform = 1 << 1,
    /// <summary>Damage zone (spikes, lava, etc.).</summary>
    Hazard   = 1 << 2,
    /// <summary>Climbable ladder volume.</summary>
    Ladder   = 1 << 3,
    /// <summary>Water / swimming zone.</summary>
    Water    = 1 << 4,
    /// <summary>Sloped surface.</summary>
    Slope    = 1 << 5,
}

/// <summary>
/// Extended collision grid with per-tile <see cref="TileFlags"/>.
/// Allows queries like "find all hazard tiles overlapping this rectangle."
/// </summary>
public record struct TileFlagGrid(
    int Cols,
    int Rows,
    int TileWidth,
    int TileHeight,
    TileFlags[] Flags
);

/// <summary>
/// Static methods for building collision grids from Tiled map layers.
/// </summary>
public static class TileCollisionExtractor
{
    /// <summary>
    /// Builds a boolean collision grid from a tile layer where any
    /// non-empty tile is considered solid. Quick setup for simple games.
    /// </summary>
    /// <param name="layer">The Tiled tile layer to extract from.</param>
    /// <param name="map">The parent map (for tile dimensions).</param>
    /// <returns>A <see cref="TileCollisionGrid"/> with solid flags set.</returns>
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
    /// Builds a <see cref="TileFlagGrid"/> from tile custom properties
    /// set in the Tiled editor. Reads "solid", "platform", "hazard",
    /// and "ladder" boolean properties from the tileset.
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

/// <summary>
/// Static query methods for tile-based collision checks. All methods
/// work in world-space pixel coordinates and convert to tile coordinates
/// internally.
/// </summary>
public static class TileCollisionQuery
{
    /// <summary>
    /// Checks if a world-space rectangle intersects any solid tile.
    /// Fast boolean test — use for simple "am I hitting a wall?" checks.
    /// </summary>
    /// <param name="grid">The collision grid to query.</param>
    /// <param name="worldRect">Rectangle in world-space pixels.</param>
    /// <returns>True if any solid tile overlaps the rectangle.</returns>
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
    /// Useful for collision response — find penetration depth per tile.
    /// </summary>
    /// <param name="grid">The collision grid to query.</param>
    /// <param name="worldRect">Rectangle in world-space pixels.</param>
    /// <returns>List of tile rectangles (in world-space pixels) that are solid.</returns>
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
    /// Useful for finding hazards, ladders, or platforms near an entity.
    /// </summary>
    /// <param name="grid">The flag grid to query.</param>
    /// <param name="worldRect">Rectangle in world-space pixels.</param>
    /// <param name="requiredFlags">
    /// Flags that must all be present on a tile to be included in results.
    /// </param>
    /// <returns>List of (Col, Row, Flags) tuples for matching tiles.</returns>
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
