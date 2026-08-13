// ============================================================================
// TilemapRenderer.cs — Manual Tilemap Rendering with Viewport Culling
// Extracted from: G37 — Tilemap Systems & Tiled Integration
// Part of: Universal 2D Engine Toolkit Examples
// ============================================================================

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended.Tiled;

namespace U2DToolkit.Examples.Tilemap;

/// <summary>
/// Manual SpriteBatch-based tilemap renderer with viewport culling.
/// Only draws tiles visible within the camera rectangle, providing
/// significant performance gains over full-map rendering.
/// <para>
/// Uses <see cref="SamplerState.PointClamp"/> for pixel-perfect tile
/// rendering without bleeding artifacts, and <see cref="SpriteSortMode.Deferred"/>
/// to batch draw calls efficiently.
/// </para>
/// </summary>
public sealed class TilemapRenderer
{
    private readonly SpriteBatch _spriteBatch;

    /// <summary>Creates a new tilemap renderer using the given SpriteBatch.</summary>
    public TilemapRenderer(SpriteBatch spriteBatch)
    {
        _spriteBatch = spriteBatch;
    }

    /// <summary>
    /// Renders a single tile layer, drawing only tiles visible within
    /// the camera rectangle. Handles horizontal and vertical flip flags
    /// from Tiled.
    /// </summary>
    /// <param name="layer">The tile layer to render.</param>
    /// <param name="map">The parent tilemap (for tile dimensions).</param>
    /// <param name="tilesetTexture">The tileset spritesheet texture.</param>
    /// <param name="tilesetFirstGid">First global ID of the tileset.</param>
    /// <param name="tilesetColumns">Number of tile columns in the tileset texture.</param>
    /// <param name="cameraBounds">The camera viewport rectangle in world space.</param>
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

                // Handle flipping flags from Tiled
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
    /// Full draw call with SpriteBatch begin/end, camera transform, and
    /// multi-layer support. Renders layers in the specified order with
    /// viewport culling.
    /// </summary>
    /// <param name="map">The tilemap to render.</param>
    /// <param name="tilesetTexture">The tileset spritesheet texture.</param>
    /// <param name="tilesetFirstGid">First global ID of the tileset.</param>
    /// <param name="tilesetColumns">Number of tile columns in the tileset.</param>
    /// <param name="cameraTransform">Camera view matrix for scrolling/zoom.</param>
    /// <param name="cameraBounds">Camera viewport in world-space pixels.</param>
    /// <param name="layerOrder">Layer names to draw, in bottom-to-top order.</param>
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
            SamplerState.PointClamp, // Pixel-perfect for tiles
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

/// <summary>
/// Utility for computing the visible tile range from a camera viewport.
/// Adds a 1-tile margin to avoid pop-in at edges during scrolling.
/// </summary>
public static class TileCulling
{
    /// <summary>
    /// Returns the visible tile range given a camera rect and tile size.
    /// Includes a 1-tile margin to prevent visual pop-in at viewport edges.
    /// </summary>
    /// <param name="cameraBounds">Camera viewport in world-space pixels.</param>
    /// <param name="tileW">Width of a single tile in pixels.</param>
    /// <param name="tileH">Height of a single tile in pixels.</param>
    /// <param name="mapCols">Total number of tile columns in the map.</param>
    /// <param name="mapRows">Total number of tile rows in the map.</param>
    /// <returns>Tuple of (StartCol, StartRow, EndCol, EndRow) clamped to map bounds.</returns>
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
