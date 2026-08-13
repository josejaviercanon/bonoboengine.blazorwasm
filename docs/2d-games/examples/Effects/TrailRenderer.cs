// =============================================================================
// TrailRenderer.cs — Trail strip rendering system
// Extracted from: G60 — Trail & Line Rendering (Sections 2–4)
// Guide: /G/G60_trails_lines.md
// =============================================================================

using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace U2DToolkit.Examples.Effects
{
    /// <summary>
    /// A single sample point in a trail, recording position, width, and timestamp.
    /// </summary>
    public struct TrailPoint
    {
        public Vector2 Position;
        public float   Width;
        public double  TimeStamp;  // total seconds when recorded
    }

    /// <summary>
    /// Ring buffer storing trail sample points. Supports O(1) push and expire
    /// operations with no allocation. Points are ordered oldest (tail) to
    /// newest (head).
    /// <para>
    /// Capacity of 64–128 points is plenty for most trails at 60 fps.
    /// </para>
    /// </summary>
    public sealed class TrailBuffer
    {
        private readonly TrailPoint[] _points;
        private int  _head;
        private int  _count;

        /// <summary>Number of active points in the buffer.</summary>
        public int   Count => _count;

        /// <summary>How long points live before being expired (seconds).</summary>
        public float Lifetime { get; set; }

        /// <param name="capacity">Maximum number of trail points (64–128 typical).</param>
        /// <param name="lifetime">Point lifetime in seconds before expiry.</param>
        public TrailBuffer(int capacity, float lifetime)
        {
            _points  = new TrailPoint[capacity];
            Lifetime = lifetime;
        }

        /// <summary>Push a new trail point at the head.</summary>
        public void Push(Vector2 pos, float width, double time)
        {
            _points[_head] = new TrailPoint { Position = pos, Width = width, TimeStamp = time };
            _head = (_head + 1) % _points.Length;
            if (_count < _points.Length) _count++;
        }

        /// <summary>Discard points older than <see cref="Lifetime"/> seconds.</summary>
        public void Expire(double currentTime)
        {
            while (_count > 0)
            {
                int tail = (_head - _count + _points.Length) % _points.Length;
                if (currentTime - _points[tail].TimeStamp > Lifetime) _count--;
                else break;
            }
        }

        /// <summary>
        /// Index into active points. Index 0 = oldest (tail), index Count-1 = newest (head).
        /// </summary>
        public TrailPoint this[int i]
        {
            get
            {
                int idx = (_head - _count + i + _points.Length) % _points.Length;
                return _points[idx];
            }
        }
    }

    /// <summary>
    /// Builds a <see cref="PrimitiveType.TriangleStrip"/> mesh from a
    /// <see cref="TrailBuffer"/> by offsetting each sample point perpendicularly
    /// to the direction of travel, producing left and right vertices.
    /// <para>
    /// Fading is applied along three axes:
    /// <list type="bullet">
    ///   <item><b>Alpha fade:</b> opacity from 0 (tail) to 1 (head).</item>
    ///   <item><b>Width taper:</b> geometry width shrinks toward the tail.</item>
    ///   <item><b>UV mapping:</b> u coordinate along length for texture scrolling.</item>
    /// </list>
    /// </para>
    /// </summary>
    public static class TrailMeshBuilder
    {
        /// <summary>
        /// Build a triangle strip from the trail buffer.
        /// Returns the number of vertices written (always even; 2 per point).
        /// </summary>
        /// <param name="buffer">The trail buffer to read from.</param>
        /// <param name="currentTime">Current game time for age-based fading.</param>
        /// <param name="verts">Pre-allocated vertex array to write into.</param>
        /// <returns>Number of vertices written. Need at least 4 for one triangle.</returns>
        public static int Build(
            TrailBuffer buffer,
            double currentTime,
            VertexPositionColorTexture[] verts)
        {
            int count = buffer.Count;
            if (count < 2) return 0;

            int vi = 0;
            for (int i = 0; i < count; i++)
            {
                TrailPoint pt = buffer[i];

                // --- direction & perpendicular ---
                Vector2 dir;
                if (i < count - 1)
                    dir = Vector2.Normalize(buffer[i + 1].Position - pt.Position);
                else
                    dir = Vector2.Normalize(pt.Position - buffer[i - 1].Position);

                Vector2 perp = new Vector2(-dir.Y, dir.X);

                // --- fade: 0 at tail, 1 at head ---
                float t     = count > 1 ? (float)i / (count - 1) : 1f;
                float age   = (float)(currentTime - pt.TimeStamp);
                float alpha = MathHelper.Clamp(1f - age / buffer.Lifetime, 0f, 1f) * t;
                float width = pt.Width * t;   // taper toward tail

                Color color = Color.White * alpha;
                float u     = t;              // UV.x along length

                Vector2 left  = pt.Position + perp * width * 0.5f;
                Vector2 right = pt.Position - perp * width * 0.5f;

                verts[vi++] = new VertexPositionColorTexture(
                    new Vector3(left, 0f), color, new Vector2(u, 0f));
                verts[vi++] = new VertexPositionColorTexture(
                    new Vector3(right, 0f), color, new Vector2(u, 1f));
            }
            return vi;
        }
    }

    /// <summary>
    /// Renders trail geometry using <see cref="GraphicsDevice.DrawUserPrimitives{T}"/>
    /// with a CPU-side vertex array — perfect for trails that change every frame.
    /// <para>
    /// Use <see cref="BlendState.NonPremultiplied"/> for standard alpha trails,
    /// or <see cref="BlendState.Additive"/> for glowing/fire effects.
    /// </para>
    /// </summary>
    public sealed class TrailRenderer
    {
        private readonly GraphicsDevice _gd;
        private readonly BasicEffect    _effect;
        private readonly VertexPositionColorTexture[] _verts;

        /// <param name="gd">Graphics device.</param>
        /// <param name="maxVerts">Maximum vertex count (2 per trail point).</param>
        public TrailRenderer(GraphicsDevice gd, int maxVerts = 256)
        {
            _gd    = gd;
            _verts = new VertexPositionColorTexture[maxVerts];

            _effect = new BasicEffect(gd)
            {
                VertexColorEnabled = true,
                TextureEnabled     = false,
                LightingEnabled    = false,
                World              = Matrix.Identity,
                View               = Matrix.Identity
            };
        }

        /// <summary>
        /// Render a trail buffer as a triangle strip.
        /// </summary>
        /// <param name="buffer">The trail data to render.</param>
        /// <param name="time">Current total game time (seconds).</param>
        /// <param name="projection">Orthographic projection matrix.</param>
        /// <param name="texture">Optional trail texture (gradient, pattern, etc.).</param>
        public void Draw(TrailBuffer buffer, double time, Matrix projection,
                         Texture2D? texture = null)
        {
            int vertCount = TrailMeshBuilder.Build(buffer, time, _verts);
            if (vertCount < 4) return;   // need at least 2 triangles

            _effect.Projection     = projection;
            _effect.TextureEnabled = texture != null;
            if (texture != null) _effect.Texture = texture;

            _gd.BlendState        = BlendState.NonPremultiplied;
            _gd.DepthStencilState = DepthStencilState.None;
            _gd.RasterizerState   = RasterizerState.CullNone;

            foreach (var pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                _gd.DrawUserPrimitives(
                    PrimitiveType.TriangleStrip,
                    _verts, 0, vertCount - 2);
            }
        }
    }
}
