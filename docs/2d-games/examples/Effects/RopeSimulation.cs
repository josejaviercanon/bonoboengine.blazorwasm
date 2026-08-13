// =============================================================================
// RopeSimulation.cs — Verlet integration rope/chain physics
// Extracted from: G60 — Trail & Line Rendering (Section 9)
// Guide: /G/G60_trails_lines.md
// =============================================================================

using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace U2DToolkit.Examples.Effects
{
    /// <summary>
    /// A single node in a Verlet rope simulation.
    /// Stores current and previous position — velocity is implicit in their difference.
    /// </summary>
    public struct RopeNode
    {
        /// <summary>Current world-space position.</summary>
        public Vector2 Position;

        /// <summary>Position from the previous frame (velocity = Position - OldPosition).</summary>
        public Vector2 OldPosition;

        /// <summary>If true, this node is anchored and won't move during simulation.</summary>
        public bool    Pinned;
    }

    /// <summary>
    /// Verlet integration rope physics simulation. Each rope consists of nodes
    /// connected by distance constraints. Gravity is applied, then constraints
    /// are iteratively satisfied to maintain segment lengths.
    /// <para>
    /// Common uses:
    /// <list type="bullet">
    ///   <item>Grappling hooks — pin node[0] to ceiling, node[^1] to player.</item>
    ///   <item>Hanging chains — pin top node, let the rest dangle.</item>
    ///   <item>Bridge cables — pin both endpoints.</item>
    ///   <item>Decorative vines — pin top, apply light wind forces.</item>
    /// </list>
    /// </para>
    /// <para>
    /// Rendering: Walk the node array, compute perpendicular offsets from segment
    /// direction, emit left/right vertices as a triangle strip (same technique as
    /// trail rendering). Apply a rope/chain texture via UV mapping.
    /// </para>
    /// </summary>
    public sealed class RopeSimulation
    {
        /// <summary>All nodes in this rope (index 0 = first anchor).</summary>
        public readonly RopeNode[] Nodes;

        /// <summary>Rest length of each segment between consecutive nodes.</summary>
        public readonly float SegmentLength;

        /// <summary>Gravity acceleration in pixels/sec² (default 980).</summary>
        public float Gravity { get; set; } = 980f;

        /// <summary>
        /// Number of constraint-solving iterations per update.
        /// Higher = stiffer rope (5 is a good default).
        /// </summary>
        public int Iterations { get; set; } = 5;

        /// <summary>
        /// Create a rope from a start point to an end point with the given
        /// number of segments. Nodes are initially distributed linearly.
        /// The first node (index 0) is pinned by default.
        /// </summary>
        /// <param name="start">Position of the first (anchored) node.</param>
        /// <param name="end">Position of the last node.</param>
        /// <param name="segments">Number of segments (nodes = segments + 1).</param>
        /// <param name="segLen">Rest length of each segment in pixels.</param>
        public RopeSimulation(Vector2 start, Vector2 end, int segments, float segLen)
        {
            SegmentLength = segLen;
            Nodes = new RopeNode[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                Vector2 p = Vector2.Lerp(start, end, (float)i / segments);
                Nodes[i] = new RopeNode { Position = p, OldPosition = p };
            }
            Nodes[0].Pinned = true;  // anchor the first node by default
        }

        /// <summary>
        /// Step the rope simulation forward by <paramref name="dt"/> seconds.
        /// Applies Verlet integration (gravity + implicit velocity) then
        /// iteratively solves distance constraints.
        /// </summary>
        public void Update(float dt)
        {
            // --- Verlet integration ---
            for (int i = 0; i < Nodes.Length; i++)
            {
                if (Nodes[i].Pinned) continue;

                // Velocity is implicit: current - previous position
                Vector2 vel = Nodes[i].Position - Nodes[i].OldPosition;
                Nodes[i].OldPosition = Nodes[i].Position;
                Nodes[i].Position   += vel + new Vector2(0, Gravity) * dt * dt;
            }

            // --- Distance constraints (Jakobsen method) ---
            for (int iter = 0; iter < Iterations; iter++)
            {
                for (int i = 0; i < Nodes.Length - 1; i++)
                {
                    Vector2 delta = Nodes[i + 1].Position - Nodes[i].Position;
                    float   dist  = delta.Length();

                    if (dist < 0.0001f) continue; // avoid division by zero

                    float   diff  = (dist - SegmentLength) / dist * 0.5f;
                    Vector2 offset = delta * diff;

                    if (!Nodes[i].Pinned)     Nodes[i].Position     += offset;
                    if (!Nodes[i + 1].Pinned) Nodes[i + 1].Position -= offset;
                }
            }
        }

        /// <summary>
        /// Move a pinned node to a new position (e.g., to track an entity).
        /// Only affects pinned nodes.
        /// </summary>
        /// <param name="nodeIndex">Index of the node to move.</param>
        /// <param name="newPosition">New world-space position.</param>
        public void SetPinnedPosition(int nodeIndex, Vector2 newPosition)
        {
            if (nodeIndex >= 0 && nodeIndex < Nodes.Length && Nodes[nodeIndex].Pinned)
            {
                Nodes[nodeIndex].Position = newPosition;
                Nodes[nodeIndex].OldPosition = newPosition; // reset velocity
            }
        }

        /// <summary>
        /// Apply an external force (wind, explosion, etc.) to all unpinned nodes.
        /// </summary>
        /// <param name="force">Force vector in pixels/sec².</param>
        /// <param name="dt">Time step.</param>
        public void ApplyForce(Vector2 force, float dt)
        {
            for (int i = 0; i < Nodes.Length; i++)
            {
                if (Nodes[i].Pinned) continue;
                Nodes[i].Position += force * dt * dt;
            }
        }

        /// <summary>
        /// Build a triangle strip for rendering this rope.
        /// Uses perpendicular offsets from segment direction, same technique as trails.
        /// </summary>
        /// <param name="verts">Pre-allocated vertex array to write into.</param>
        /// <param name="width">Visual width of the rope in pixels.</param>
        /// <param name="color">Vertex color (tint).</param>
        /// <returns>Number of vertices written (2 per node).</returns>
        public int BuildMesh(VertexPositionColorTexture[] verts, float width, Color color)
        {
            if (Nodes.Length < 2) return 0;

            int vi = 0;
            for (int i = 0; i < Nodes.Length; i++)
            {
                // Compute direction from this node to the next (or from previous to this)
                Vector2 dir;
                if (i < Nodes.Length - 1)
                    dir = Vector2.Normalize(Nodes[i + 1].Position - Nodes[i].Position);
                else
                    dir = Vector2.Normalize(Nodes[i].Position - Nodes[i - 1].Position);

                Vector2 perp = new Vector2(-dir.Y, dir.X);

                // UV: u along rope length, v across width
                float u = (float)i / (Nodes.Length - 1);
                Vector2 left  = Nodes[i].Position + perp * width * 0.5f;
                Vector2 right = Nodes[i].Position - perp * width * 0.5f;

                verts[vi++] = new VertexPositionColorTexture(
                    new Vector3(left, 0f), color, new Vector2(u, 0f));
                verts[vi++] = new VertexPositionColorTexture(
                    new Vector3(right, 0f), color, new Vector2(u, 1f));
            }
            return vi;
        }
    }
}
