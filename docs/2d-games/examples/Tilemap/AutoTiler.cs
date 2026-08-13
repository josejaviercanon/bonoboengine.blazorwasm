// ============================================================================
// AutoTiler.cs — Bitmask Autotiling (4-bit and 8-bit)
// Extracted from: G37 — Tilemap Systems & Tiled Integration
// Part of: Universal 2D Engine Toolkit Examples
// ============================================================================

namespace U2DToolkit.Examples.Tilemap;

/// <summary>
/// ECS component that marks tiles participating in autotiling.
/// </summary>
/// <param name="TileType">The tile type ID for neighbor matching.</param>
public record struct AutotileTag(int TileType);

/// <summary>
/// ECS component storing the computed bitmask for a tile position.
/// </summary>
/// <param name="Mask">The computed neighbor bitmask.</param>
public record struct AutotileMask(byte Mask);

/// <summary>
/// 4-bit bitmask autotiling based on cardinal neighbors (N, E, S, W).
/// Produces 16 possible tile variants. Suitable for simple tilesets
/// with distinct edge/corner pieces.
/// <para>
/// Bit layout: North=1, East=2, South=4, West=8.
/// A mask of 0b1010 = West + East = horizontal corridor.
/// </para>
/// </summary>
public static class Autotiler
{
    // Cardinal direction bit flags
    private const byte North = 1;  // bit 0
    private const byte East  = 2;  // bit 1
    private const byte South = 4;  // bit 2
    private const byte West  = 8;  // bit 3

    /// <summary>
    /// Computes a 4-bit bitmask based on cardinal neighbors matching
    /// the same tile type. Checks N, E, S, W — if the neighbor has the
    /// same type, the corresponding bit is set.
    /// </summary>
    /// <param name="tileTypes">2D grid of tile types [row, col].</param>
    /// <param name="col">Column of the tile to compute.</param>
    /// <param name="row">Row of the tile to compute.</param>
    /// <param name="targetType">The tile type to match against neighbors.</param>
    /// <returns>A 4-bit bitmask (0–15) encoding cardinal neighbors.</returns>
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
    /// Index = bitmask value (0–15), Value = tile index in the tileset.
    /// Arrange your tileset to match this mapping.
    /// </summary>
    /// <remarks>
    /// Bitmask values:
    /// <list type="bullet">
    ///   <item>0 = isolated, 1 = N only, 2 = E only, 3 = N+E</item>
    ///   <item>4 = S only, 5 = N+S, 6 = S+E, 7 = N+S+E</item>
    ///   <item>8 = W only, 9 = N+W, 10 = E+W, 11 = N+E+W</item>
    ///   <item>12 = S+W, 13 = N+S+W, 14 = S+E+W, 15 = all</item>
    /// </list>
    /// </remarks>
    public static readonly int[] Lookup4Bit = new int[16]
    {
        0,  1,  2,  3,  4,  5,  6,  7,
        8,  9, 10, 11, 12, 13, 14, 15
    };
}

/// <summary>
/// 8-bit bitmask autotiling including diagonal neighbors.
/// Produces 47 unique tile variants (after corner-collapse) from 256 raw
/// bitmask combinations. Required for natural-looking terrain transitions.
/// <para>
/// Bit layout: N=0, NE=1, E=2, SE=3, S=4, SW=5, W=6, NW=7.
/// Diagonal bits are only set if both adjacent cardinal neighbors are
/// also present (corner-collapse rule).
/// </para>
/// </summary>
public static class Autotiler8Bit
{
    /// <summary>
    /// Computes an 8-bit bitmask checking all 8 neighbors.
    /// Diagonal neighbors are only counted if both adjacent cardinal
    /// neighbors are also present (standard corner-collapse rule).
    /// This prevents isolated corner tiles from appearing.
    /// </summary>
    /// <param name="tileTypes">2D grid of tile types [row, col].</param>
    /// <param name="col">Column of the tile to compute.</param>
    /// <param name="row">Row of the tile to compute.</param>
    /// <param name="targetType">The tile type to match against neighbors.</param>
    /// <returns>An 8-bit bitmask (0–255) encoding all neighbors.</returns>
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
    /// Builds a lookup table collapsing 256 raw masks into 47 unique
    /// tile indices. Populate based on your specific tileset arrangement.
    /// </summary>
    /// <returns>
    /// Dictionary mapping each possible bitmask to a tile index (0–46).
    /// </returns>
    public static Dictionary<byte, int> Build47TileLookup()
    {
        var lookup = new Dictionary<byte, int>();

        // Example entries — fill all 47 based on your tileset:
        lookup[0b_0000_0000] = 0;   // Isolated
        lookup[0b_0001_0100] = 1;   // N+S (vertical corridor)
        lookup[0b_0100_0001] = 2;   // E+W (horizontal corridor)
        lookup[0b_0101_0101] = 3;   // All cardinals, no corners
        lookup[0b_1111_1111] = 46;  // Fully surrounded

        // ... remaining 42 entries based on tileset layout
        return lookup;
    }
}

/// <summary>
/// Autotile system that recomputes tile indices for a region of the map.
/// Integrates with both 4-bit and 8-bit autotiling approaches.
/// <para>
/// Usage: call <see cref="SetTile"/> to modify the tile grid, then
/// <see cref="RecomputeRegion"/> to get the list of changed tile indices
/// that need to be applied back to the tilemap renderer.
/// </para>
/// </summary>
public sealed class AutotileSystem
{
    private readonly int[,] _tileTypes;
    private readonly Dictionary<int, Dictionary<byte, int>> _rulesets;
    private readonly int _cols;
    private readonly int _rows;

    /// <summary>
    /// Creates an autotile system for the given map dimensions.
    /// </summary>
    /// <param name="cols">Number of tile columns.</param>
    /// <param name="rows">Number of tile rows.</param>
    /// <param name="rulesets">
    /// Per-tile-type rulesets mapping bitmask → tile index.
    /// Key is the tile type ID, value is its bitmask lookup table.
    /// </param>
    public AutotileSystem(int cols, int rows,
        Dictionary<int, Dictionary<byte, int>> rulesets)
    {
        _cols = cols;
        _rows = rows;
        _tileTypes = new int[rows, cols];
        _rulesets = rulesets;
    }

    /// <summary>Set the tile type at a grid position.</summary>
    public void SetTile(int col, int row, int tileType)
    {
        _tileTypes[row, col] = tileType;
    }

    /// <summary>Get the tile type at a grid position.</summary>
    public int GetTile(int col, int row) => _tileTypes[row, col];

    /// <summary>
    /// Recomputes autotile indices for a rectangular region.
    /// Call after editing tiles to update their visual appearance.
    /// Returns (col, row, newTileIndex) tuples for tiles that changed.
    /// </summary>
    /// <param name="startCol">Left column of the region (inclusive).</param>
    /// <param name="startRow">Top row of the region (inclusive).</param>
    /// <param name="endCol">Right column of the region (inclusive).</param>
    /// <param name="endRow">Bottom row of the region (inclusive).</param>
    /// <returns>List of changed tiles with their new tile indices.</returns>
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

    /// <summary>
    /// Recomputes autotile indices for the entire map.
    /// Expensive — use <see cref="RecomputeRegion"/> for incremental updates.
    /// </summary>
    public List<(int Col, int Row, int TileIndex)> RecomputeAll()
    {
        return RecomputeRegion(0, 0, _cols - 1, _rows - 1);
    }
}
