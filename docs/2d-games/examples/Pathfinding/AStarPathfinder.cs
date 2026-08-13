// =============================================================================
// AStarPathfinder.cs — A* pathfinding with grid graph
// Extracted from: G40 — Pathfinding (Sections 1–2)
// Guide: /G/G40_pathfinding.md
// =============================================================================

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace U2DToolkit.Examples.Pathfinding
{
    /// <summary>
    /// Common heuristic functions for grid-based pathfinding.
    /// Choose based on movement model: Manhattan for 4-dir, Octile for 8-dir,
    /// Euclidean for any-angle or navmesh.
    /// </summary>
    public static class Heuristics
    {
        /// <summary>Best for 4-directional (no diagonal) grids.</summary>
        public static float Manhattan(Point a, Point b)
            => MathF.Abs(a.X - b.X) + MathF.Abs(a.Y - b.Y);

        /// <summary>Best for any-angle movement or navmesh.</summary>
        public static float Euclidean(Point a, Point b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            return MathF.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Best for 8-directional grids (diagonal allowed).</summary>
        public static float Chebyshev(Point a, Point b)
            => MathF.Max(MathF.Abs(a.X - b.X), MathF.Abs(a.Y - b.Y));

        /// <summary>Octile — accurate for 8-dir with √2 diagonal cost. Default recommendation.</summary>
        public static float Octile(Point a, Point b)
        {
            float dx = MathF.Abs(a.X - b.X);
            float dy = MathF.Abs(a.Y - b.Y);
            return MathF.Max(dx, dy) + 0.41421356f * MathF.Min(dx, dy);
        }
    }

    /// <summary>
    /// Grid graph for pathfinding. Wraps walkability and terrain cost data
    /// in a flat array for cache-friendly access. Answers "Can I walk here?"
    /// and "How much does it cost?"
    /// </summary>
    public class GridGraph
    {
        public int Width { get; }
        public int Height { get; }

        private readonly byte[] _walkable;   // 0 = blocked, 1 = walkable
        private readonly float[] _cost;      // terrain weight per cell

        public GridGraph(int width, int height)
        {
            Width = width;
            Height = height;
            _walkable = new byte[width * height];
            _cost = new float[width * height];
            Array.Fill(_cost, 1.0f);
            Array.Fill(_walkable, (byte)1);
        }

        /// <summary>Check if a point lies within the grid bounds.</summary>
        public bool InBounds(Point p)
            => p.X >= 0 && p.X < Width && p.Y >= 0 && p.Y < Height;

        /// <summary>Check if a cell is within bounds and walkable.</summary>
        public bool IsWalkable(Point p)
            => InBounds(p) && _walkable[p.Y * Width + p.X] != 0;

        /// <summary>Get the terrain traversal cost multiplier for a cell.</summary>
        public float GetCost(Point p)
            => _cost[p.Y * Width + p.X];

        /// <summary>Mark a cell as walkable or blocked.</summary>
        public void SetWalkable(Point p, bool walkable)
        {
            if (InBounds(p))
                _walkable[p.Y * Width + p.X] = walkable ? (byte)1 : (byte)0;
        }

        /// <summary>Set the terrain cost multiplier for a cell (e.g., 3.0 for swamp).</summary>
        public void SetCost(Point p, float cost)
        {
            if (InBounds(p))
                _cost[p.Y * Width + p.X] = cost;
        }
    }

    /// <summary>
    /// Tracks dynamic obstacles using an occupancy counter per cell.
    /// A cell is walkable only when its occupancy count is zero.
    /// </summary>
    public class DynamicObstacleTracker
    {
        private readonly int[] _occupancy;
        private readonly GridGraph _graph;

        public DynamicObstacleTracker(GridGraph graph)
        {
            _graph = graph;
            _occupancy = new int[graph.Width * graph.Height];
        }

        public void AddObstacle(Point p)
        {
            int idx = p.Y * _graph.Width + p.X;
            _occupancy[idx]++;
            _graph.SetWalkable(p, false);
        }

        public void RemoveObstacle(Point p)
        {
            int idx = p.Y * _graph.Width + p.X;
            _occupancy[idx] = Math.Max(0, _occupancy[idx] - 1);
            if (_occupancy[idx] == 0)
                _graph.SetWalkable(p, true);
        }
    }

    /// <summary>
    /// A* pathfinder operating on a <see cref="GridGraph"/>.
    /// Supports 4-directional and 8-directional movement with configurable heuristics,
    /// diagonal corner-cutting prevention, and terrain cost weighting.
    /// </summary>
    public class AStarPathfinder
    {
        private readonly GridGraph _graph;
        private readonly Func<Point, Point, float> _heuristic;

        /// <summary>Direction offsets: 4 cardinal + 4 diagonal.</summary>
        private static readonly (Point Offset, float Cost)[] Dirs8 =
        {
            (new Point( 0, -1), 1.0f),   // N
            (new Point( 1,  0), 1.0f),   // E
            (new Point( 0,  1), 1.0f),   // S
            (new Point(-1,  0), 1.0f),   // W
            (new Point( 1, -1), 1.414f), // NE
            (new Point( 1,  1), 1.414f), // SE
            (new Point(-1,  1), 1.414f), // SW
            (new Point(-1, -1), 1.414f), // NW
        };

        /// <param name="graph">The grid to search over.</param>
        /// <param name="heuristic">Heuristic function; defaults to Octile.</param>
        public AStarPathfinder(GridGraph graph, Func<Point, Point, float>? heuristic = null)
        {
            _graph = graph;
            _heuristic = heuristic ?? Heuristics.Octile;
        }

        /// <summary>
        /// Find the shortest path from <paramref name="start"/> to <paramref name="goal"/>.
        /// Returns a list of grid points from start to goal, or null if no path exists.
        /// </summary>
        /// <param name="start">Starting cell.</param>
        /// <param name="goal">Target cell.</param>
        /// <param name="allowDiagonal">If true, uses 8 directions; otherwise 4.</param>
        public List<Point>? FindPath(Point start, Point goal, bool allowDiagonal = true)
        {
            int dirCount = allowDiagonal ? 8 : 4;

            var open = new PriorityQueue<Point, float>();
            var gScore = new Dictionary<Point, float>();
            var parent = new Dictionary<Point, Point>();
            var closed = new HashSet<Point>();

            gScore[start] = 0;
            open.Enqueue(start, _heuristic(start, goal));

            while (open.Count > 0)
            {
                var current = open.Dequeue();

                if (current == goal)
                    return ReconstructPath(parent, current);

                if (!closed.Add(current))
                    continue; // already processed

                for (int i = 0; i < dirCount; i++)
                {
                    var (offset, baseCost) = Dirs8[i];
                    var neighbor = new Point(current.X + offset.X, current.Y + offset.Y);

                    if (!_graph.IsWalkable(neighbor) || closed.Contains(neighbor))
                        continue;

                    // Prevent diagonal corner-cutting through walls
                    if (i >= 4 && !_graph.IsWalkable(new Point(current.X + offset.X, current.Y))
                               && !_graph.IsWalkable(new Point(current.X, current.Y + offset.Y)))
                        continue;

                    float terrainCost = _graph.GetCost(neighbor);
                    float tentativeG = gScore[current] + baseCost * terrainCost;

                    if (!gScore.TryGetValue(neighbor, out float existingG) || tentativeG < existingG)
                    {
                        gScore[neighbor] = tentativeG;
                        parent[neighbor] = current;
                        float f = tentativeG + _heuristic(neighbor, goal);
                        open.Enqueue(neighbor, f);
                    }
                }
            }

            return null; // no path found
        }

        /// <summary>
        /// Smooth a raw A* path by removing intermediate waypoints that have
        /// clear line-of-sight to an earlier waypoint (Bresenham LOS check).
        /// </summary>
        public List<Point> SmoothPath(List<Point> rawPath)
        {
            if (rawPath == null || rawPath.Count <= 2) return rawPath;

            var smoothed = new List<Point> { rawPath[0] };
            int current = 0;

            while (current < rawPath.Count - 1)
            {
                int furthest = current + 1;
                for (int i = rawPath.Count - 1; i > current + 1; i--)
                {
                    if (HasLineOfSight(rawPath[current], rawPath[i]))
                    {
                        furthest = i;
                        break;
                    }
                }
                smoothed.Add(rawPath[furthest]);
                current = furthest;
            }

            return smoothed;
        }

        /// <summary>Bresenham-style line-of-sight check on the grid.</summary>
        private bool HasLineOfSight(Point a, Point b)
        {
            int dx = Math.Abs(b.X - a.X), dy = Math.Abs(b.Y - a.Y);
            int sx = a.X < b.X ? 1 : -1, sy = a.Y < b.Y ? 1 : -1;
            int err = dx - dy;

            int x = a.X, y = a.Y;
            while (x != b.X || y != b.Y)
            {
                if (!_graph.IsWalkable(new Point(x, y))) return false;

                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x += sx; }
                if (e2 < dx)  { err += dx; y += sy; }
            }
            return true;
        }

        private static List<Point> ReconstructPath(Dictionary<Point, Point> parent, Point current)
        {
            var path = new List<Point> { current };
            while (parent.TryGetValue(current, out var prev))
            {
                path.Add(prev);
                current = prev;
            }
            path.Reverse();
            return path;
        }
    }
}
