// =============================================================================
// FlowField.cs — Flow field pathfinding for many-units-to-one-target scenarios
// Extracted from: G40 — Pathfinding (Section 4)
// Guide: /G/G40_pathfinding.md
// =============================================================================

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace U2DToolkit.Examples.Pathfinding
{
    /// <summary>
    /// A flow field computes a single direction field that every unit can query
    /// for movement toward a shared target. Cost is O(cells) once, then O(1)
    /// per unit per frame — ideal for RTS rally points, tower defense exits,
    /// or any scenario with 50+ units converging on the same destination.
    /// <para>
    /// Algorithm:
    /// 1. Integration field — Dijkstra from the goal outward; each cell stores total cost to goal.
    /// 2. Flow field — Each cell stores a direction vector toward its lowest-cost neighbor.
    /// </para>
    /// </summary>
    public class FlowField
    {
        public int Width { get; }
        public int Height { get; }

        private readonly float[] _integrationField;
        private readonly Vector2[] _flowDirections;

        /// <summary>8-directional neighbor offsets.</summary>
        private static readonly Point[] Neighbors =
        {
            new( 0, -1), new( 1, 0), new( 0, 1), new(-1, 0),
            new( 1, -1), new( 1, 1), new(-1, 1), new(-1,-1),
        };

        public FlowField(int width, int height)
        {
            Width = width;
            Height = height;
            _integrationField = new float[width * height];
            _flowDirections = new Vector2[width * height];
        }

        /// <summary>
        /// Build the flow field from a <see cref="GridGraph"/> toward a single goal cell.
        /// After calling this, use <see cref="GetDirection"/> to query movement vectors.
        /// </summary>
        /// <param name="graph">The grid with walkability and terrain cost data.</param>
        /// <param name="goal">The target cell all units should flow toward.</param>
        public void Build(GridGraph graph, Point goal)
        {
            // --- Integration field (Dijkstra from goal) ---
            Array.Fill(_integrationField, float.MaxValue);
            int goalIdx = goal.Y * Width + goal.X;
            _integrationField[goalIdx] = 0;

            var open = new Queue<Point>();
            open.Enqueue(goal);

            while (open.Count > 0)
            {
                var current = open.Dequeue();
                int curIdx = current.Y * Width + current.X;
                float curCost = _integrationField[curIdx];

                foreach (var dir in Neighbors)
                {
                    var neighbor = new Point(current.X + dir.X, current.Y + dir.Y);
                    if (!graph.IsWalkable(neighbor)) continue;

                    int nIdx = neighbor.Y * Width + neighbor.X;
                    float stepCost = (dir.X != 0 && dir.Y != 0) ? 1.414f : 1.0f;
                    float newCost = curCost + stepCost * graph.GetCost(neighbor);

                    if (newCost < _integrationField[nIdx])
                    {
                        _integrationField[nIdx] = newCost;
                        open.Enqueue(neighbor);
                    }
                }
            }

            // --- Flow directions (point toward cheapest neighbor) ---
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                int idx = y * Width + x;
                if (_integrationField[idx] >= float.MaxValue)
                {
                    _flowDirections[idx] = Vector2.Zero;
                    continue;
                }

                float bestCost = float.MaxValue;
                Vector2 bestDir = Vector2.Zero;

                foreach (var dir in Neighbors)
                {
                    var n = new Point(x + dir.X, y + dir.Y);
                    if (!graph.InBounds(n)) continue;
                    int nIdx = n.Y * Width + n.X;
                    if (_integrationField[nIdx] < bestCost)
                    {
                        bestCost = _integrationField[nIdx];
                        bestDir = new Vector2(dir.X, dir.Y);
                    }
                }

                _flowDirections[idx] = bestDir != Vector2.Zero
                    ? Vector2.Normalize(bestDir)
                    : Vector2.Zero;
            }
        }

        /// <summary>
        /// Query the flow direction at a given cell. Returns a normalized vector
        /// pointing toward the goal, or <see cref="Vector2.Zero"/> if the cell
        /// is unreachable or is the goal itself.
        /// </summary>
        public Vector2 GetDirection(Point cell)
        {
            if (cell.X < 0 || cell.X >= Width || cell.Y < 0 || cell.Y >= Height)
                return Vector2.Zero;
            return _flowDirections[cell.Y * Width + cell.X];
        }

        /// <summary>
        /// Get the total integration cost from a cell to the goal.
        /// Returns <see cref="float.MaxValue"/> if unreachable.
        /// </summary>
        public float GetIntegrationCost(Point cell)
        {
            if (cell.X < 0 || cell.X >= Width || cell.Y < 0 || cell.Y >= Height)
                return float.MaxValue;
            return _integrationField[cell.Y * Width + cell.X];
        }
    }
}
