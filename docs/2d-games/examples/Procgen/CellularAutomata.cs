// =============================================================================
// CellularAutomata.cs — Cave generation via cellular automata
// Extracted from: G53 — Procedural Generation (Section 4)
// Guide: /G/G53_procedural_generation.md
// =============================================================================

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace U2DToolkit.Examples.Procgen
{
    /// <summary>
    /// Generates organic cave systems by simulating cellular automata rules
    /// on a randomly filled grid.
    /// <para>
    /// Process:
    /// 1. Fill grid randomly with walls (configurable fill chance).
    /// 2. Iterate smoothing passes: cells with 5+ wall neighbors become walls,
    ///    cells with 3 or fewer become floors.
    /// 3. Ensure connectivity by flood-filling isolated regions and tunneling
    ///    between them.
    /// </para>
    /// </summary>
    public sealed class CellularAutomataGenerator
    {
        private readonly SeededRandom _rng;

        public CellularAutomataGenerator(SeededRandom rng) => _rng = rng;

        /// <summary>
        /// Generate a cave map.
        /// </summary>
        /// <param name="width">Map width in tiles.</param>
        /// <param name="height">Map height in tiles.</param>
        /// <param name="fillChance">Initial wall probability (0.45–0.55 typical).</param>
        /// <param name="iterations">Smoothing passes (4–5 typical).</param>
        /// <returns>2D tile array where 0 = floor, 1 = wall.</returns>
        public int[,] Generate(int width, int height, float fillChance = 0.48f,
            int iterations = 5)
        {
            var map = new int[width, height]; // 0 = floor, 1 = wall

            // Step 1: Random fill (borders always walls)
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    map[x, y] = (x == 0 || x == width - 1 || y == 0 || y == height - 1)
                        ? 1
                        : _rng.NextBool(fillChance) ? 1 : 0;

            // Step 2: Iterate smoothing rules
            for (int i = 0; i < iterations; i++)
                map = Step(map, width, height);

            // Step 3: Ensure all floor regions are connected
            EnsureConnectivity(map, width, height);

            return map;
        }

        /// <summary>Apply one cellular automata step: birth/survival rules.</summary>
        private int[,] Step(int[,] map, int w, int h)
        {
            var next = new int[w, h];
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                {
                    int walls = CountNeighborWalls(map, x, y, w, h);
                    // Birth: empty cell with 5+ wall neighbors becomes wall
                    // Survival: wall cell stays wall if 4+ wall neighbors
                    next[x, y] = walls >= 5 ? 1 : (walls <= 3 ? 0 : map[x, y]);
                }
            return next;
        }

        /// <summary>Count wall neighbors in a 3×3 area (excluding center).</summary>
        private int CountNeighborWalls(int[,] map, int cx, int cy, int w, int h)
        {
            int count = 0;
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = cx + dx, ny = cy + dy;
                    // Out-of-bounds counts as wall
                    count += (nx < 0 || nx >= w || ny < 0 || ny >= h) ? 1 : map[nx, ny];
                }
            return count;
        }

        /// <summary>
        /// Flood-fill to find all separate floor regions, then carve tunnels
        /// to connect smaller regions to the largest one.
        /// </summary>
        private void EnsureConnectivity(int[,] map, int w, int h)
        {
            var visited = new bool[w, h];
            var regions = new List<List<Point>>();

            for (int x = 1; x < w - 1; x++)
                for (int y = 1; y < h - 1; y++)
                {
                    if (map[x, y] == 0 && !visited[x, y])
                    {
                        var region = FloodFill(map, visited, x, y, w, h);
                        regions.Add(region);
                    }
                }

            if (regions.Count <= 1) return;

            // Keep largest region, connect others to it
            regions.Sort((a, b) => b.Count.CompareTo(a.Count));
            var main = regions[0];

            for (int r = 1; r < regions.Count; r++)
            {
                // Find closest pair of points between regions
                int bestDist = int.MaxValue;
                Point bestA = default, bestB = default;
                foreach (var a in main)
                    foreach (var b in regions[r])
                    {
                        int dist = Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
                        if (dist < bestDist) { bestDist = dist; bestA = a; bestB = b; }
                    }
                // Carve a 2-wide tunnel between them
                CarveLine(map, bestA, bestB);
                main.AddRange(regions[r]);
            }
        }

        /// <summary>Flood-fill to collect all connected floor cells from a starting point.</summary>
        private List<Point> FloodFill(int[,] map, bool[,] visited,
            int sx, int sy, int w, int h)
        {
            var result = new List<Point>();
            var stack = new Stack<Point>();
            stack.Push(new Point(sx, sy));
            while (stack.Count > 0)
            {
                var p = stack.Pop();
                if (p.X < 0 || p.X >= w || p.Y < 0 || p.Y >= h) continue;
                if (visited[p.X, p.Y] || map[p.X, p.Y] != 0) continue;
                visited[p.X, p.Y] = true;
                result.Add(p);
                stack.Push(new Point(p.X + 1, p.Y));
                stack.Push(new Point(p.X - 1, p.Y));
                stack.Push(new Point(p.X, p.Y + 1));
                stack.Push(new Point(p.X, p.Y - 1));
            }
            return result;
        }

        /// <summary>Carve a 2-wide tunnel between two points.</summary>
        private void CarveLine(int[,] map, Point a, Point b)
        {
            int x = a.X, y = a.Y;
            while (x != b.X || y != b.Y)
            {
                map[x, y] = 0;
                // Widen the tunnel for better traversability
                if (x + 1 < map.GetLength(0)) map[x + 1, y] = 0;
                if (y + 1 < map.GetLength(1)) map[x, y + 1] = 0;

                if (Math.Abs(x - b.X) > Math.Abs(y - b.Y))
                    x += x < b.X ? 1 : -1;
                else
                    y += y < b.Y ? 1 : -1;
            }
        }
    }
}
