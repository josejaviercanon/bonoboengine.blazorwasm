// =============================================================================
// Shadowcaster.cs — Recursive shadowcasting for field-of-view computation
// Extracted from: G54 — Fog of War & Visibility Systems (Section 3)
// Guide: /G/G54_fog_of_war.md
// =============================================================================

using System;

namespace U2DToolkit.Examples.FogOfWar
{
    /// <summary>
    /// Recursive shadowcasting — the gold standard for roguelike/strategy FOV.
    /// Processes the map in 8 octants, scanning row-by-row outward from the source.
    /// Walls create shadow regions that are skipped entirely, making it dramatically
    /// faster than brute-force raycasting.
    /// <para>
    /// Performance: O(r²) worst case (open field), but typically visits only 30–50%
    /// of cells on dungeon maps. Fast enough for dozens of simultaneous sources.
    /// </para>
    /// <para>
    /// How it works:
    /// 1. Divide the circle around the viewer into 8 octants (45° each).
    /// 2. For each octant, scan columns moving outward.
    /// 3. Track a "slope window" [startSlope, endSlope] — the visible arc.
    /// 4. When you hit a wall, the visible arc shrinks. When a wall ends, recurse.
    /// </para>
    /// </summary>
    public static class Shadowcast
    {
        /// <summary>
        /// Multipliers for the 8 octants — transforms (col, row) into (dx, dy).
        /// Layout: col-dx, col-dy, row-dx, row-dy.
        /// </summary>
        private static readonly int[,] OctantTransform =
        {
            {  1,  0,  0,  1 },  // octant 0: E-NE
            {  0,  1,  1,  0 },  // octant 1: N-NE
            {  0, -1,  1,  0 },  // octant 2: N-NW
            { -1,  0,  0,  1 },  // octant 3: W-NW
            { -1,  0,  0, -1 },  // octant 4: W-SW
            {  0, -1, -1,  0 },  // octant 5: S-SW
            {  0,  1, -1,  0 },  // octant 6: S-SE
            {  1,  0,  0, -1 },  // octant 7: E-SE
        };

        /// <summary>
        /// Compute field of view from an origin point with the given radius.
        /// Revealed cells are marked on the <see cref="FogGrid"/>.
        /// </summary>
        /// <param name="fog">The fog grid to reveal cells on.</param>
        /// <param name="originX">X coordinate of the viewer.</param>
        /// <param name="originY">Y coordinate of the viewer.</param>
        /// <param name="radius">Vision radius in cells.</param>
        /// <param name="isOpaque">Function returning true if a cell blocks vision.</param>
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

        /// <summary>
        /// Recursively scan one octant, tracking the visible slope window.
        /// </summary>
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

                    // Within radius? Reveal it
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
}
