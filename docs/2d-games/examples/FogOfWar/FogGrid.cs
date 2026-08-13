// =============================================================================
// FogGrid.cs — Per-cell fog state grid with visibility and exploration tracking
// Extracted from: G54 — Fog of War & Visibility Systems (Section 1)
// Guide: /G/G54_fog_of_war.md
// =============================================================================

using System;

namespace U2DToolkit.Examples.FogOfWar
{
    /// <summary>
    /// Every cell on the map exists in one of three visibility states.
    /// </summary>
    public enum VisibilityState : byte
    {
        /// <summary>Never seen — render solid black. No information.</summary>
        Unexplored = 0,

        /// <summary>Previously seen — render dimmed/desaturated. Terrain visible but entities hidden.</summary>
        Explored = 1,

        /// <summary>Currently in line of sight — render fully. Everything revealed.</summary>
        Visible = 2
    }

    /// <summary>
    /// A flat-array grid storing per-cell fog-of-war state.
    /// Maintains two layers:
    /// <list type="bullet">
    ///   <item><b>Visible</b> — transient, cleared each frame and rebuilt from vision sources.</item>
    ///   <item><b>Explored</b> — persistent, once true it stays true forever.</item>
    /// </list>
    /// <para>
    /// Key invariant: <c>Visible</c> implies <c>Explored</c>. Once a cell is explored
    /// it never reverts to unexplored. The <c>_visible</c> array is transient —
    /// cleared and rebuilt every time vision sources move.
    /// </para>
    /// </summary>
    public class FogGrid
    {
        public int Width  { get; }
        public int Height { get; }

        /// <summary>Current-frame visibility (cleared each update, then rebuilt).</summary>
        private readonly bool[] _visible;

        /// <summary>Persistent — once true, stays true forever.</summary>
        private readonly bool[] _explored;

        public FogGrid(int width, int height)
        {
            Width    = width;
            Height   = height;
            _visible  = new bool[width * height];
            _explored = new bool[width * height];
        }

        /// <summary>
        /// Get the visibility state of a cell.
        /// Out-of-bounds cells return <see cref="VisibilityState.Unexplored"/>.
        /// </summary>
        public VisibilityState this[int x, int y]
        {
            get
            {
                if (x < 0 || x >= Width || y < 0 || y >= Height)
                    return VisibilityState.Unexplored;
                int i = y * Width + x;
                if (_visible[i])  return VisibilityState.Visible;
                if (_explored[i]) return VisibilityState.Explored;
                return VisibilityState.Unexplored;
            }
        }

        /// <summary>
        /// Clear all current-frame visibility. Call at the start of each fog update
        /// before recomputing vision from all sources.
        /// </summary>
        public void ClearVisible() => Array.Clear(_visible, 0, _visible.Length);

        /// <summary>
        /// Mark a cell as visible (and explored). Called by FOV algorithms
        /// (shadowcasting, raycasting) when a cell is in line of sight.
        /// </summary>
        public void Reveal(int x, int y)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height) return;
            int i = y * Width + x;
            _visible[i]  = true;
            _explored[i] = true;   // Permanent
        }

        /// <summary>Check if a cell is currently visible this frame.</summary>
        public bool IsVisible(int x, int y)
            => InBounds(x, y) && _visible[y * Width + x];

        /// <summary>Check if a cell has ever been explored.</summary>
        public bool IsExplored(int x, int y)
            => InBounds(x, y) && _explored[y * Width + x];

        /// <summary>Bounds check helper.</summary>
        public bool InBounds(int x, int y)
            => x >= 0 && x < Width && y >= 0 && y < Height;
    }
}
