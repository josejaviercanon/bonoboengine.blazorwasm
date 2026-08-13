// ============================================================================
// CollisionResolver.cs — AABB Collision Resolution with Sub-Pixel Accumulation
// Extracted from: G52 — 2D Platformer Character Controller
// Part of: Universal 2D Engine Toolkit Examples
// ============================================================================

using Microsoft.Xna.Framework;

namespace U2DToolkit.Examples.Character;

/// <summary>
/// Axis-separated AABB sweep collision resolver.
/// Moves an entity by velocity, resolving collisions against solid
/// geometry one pixel at a time per axis.
/// <para>
/// Key features:
/// <list type="bullet">
///   <item><b>Sub-pixel accumulation:</b> At low speeds (e.g., 0.3 px/frame),
///   the fractional remainder is stored and added next frame. Without this,
///   slow movement rounds to 0 and the entity never moves.</item>
///   <item><b>Axis separation:</b> Move X first → resolve, then Y → resolve.
///   This prevents ambiguous corner cases and gives correct slide behavior.</item>
///   <item><b>Per-pixel stepping:</b> Moving one pixel at a time prevents
///   tunneling through thin walls at high speeds.</item>
/// </list>
/// </para>
/// </summary>
public static class CollisionResolver
{
    /// <summary>
    /// Moves an entity by the given velocity, resolving collisions against
    /// solid geometry using axis-separated sweep.
    /// <para>
    /// Order: X axis first, then Y axis. X-first is standard for horizontal
    /// platformers. The order can matter on slopes.
    /// </para>
    /// </summary>
    /// <param name="pos">Entity position (modified in place).</param>
    /// <param name="vel">Entity velocity (zeroed on axis if blocked).</param>
    /// <param name="col">Entity's collision box.</param>
    /// <param name="solids">Span of solid rectangles to collide against.</param>
    /// <param name="dt">Delta time in seconds.</param>
    public static void MoveAndCollide(
        ref Position pos,
        ref Velocity vel,
        in ColliderBox col,
        ReadOnlySpan<RectF> solids,
        float dt)
    {
        // ── Sub-pixel accumulation ──
        // Add velocity * dt to the accumulated remainder, then extract
        // the integer pixel count to actually move.
        float moveX = vel.X * dt + pos.RemainderX;
        float moveY = vel.Y * dt + pos.RemainderY;

        int pixelsX = (int)MathF.Truncate(moveX);
        int pixelsY = (int)MathF.Truncate(moveY);

        pos.RemainderX = moveX - pixelsX;
        pos.RemainderY = moveY - pixelsY;

        // ── Move X ──
        // Step one pixel at a time in the movement direction.
        // If a step would cause overlap, stop and zero X velocity.
        int signX = Math.Sign(pixelsX);
        while (pixelsX != 0)
        {
            var testPos = new Position(pos.X + signX, pos.Y);
            if (!OverlapsAnySolid(testPos, col, solids))
            {
                pos.X += signX;
                pixelsX -= signX;
            }
            else
            {
                // Hit a wall — stop horizontal movement
                vel = vel with { X = 0f };
                pos.RemainderX = 0f;
                break;
            }
        }

        // ── Move Y ──
        // Same approach on the vertical axis.
        int signY = Math.Sign(pixelsY);
        while (pixelsY != 0)
        {
            var testPos = new Position(pos.X, pos.Y + signY);
            if (!OverlapsAnySolid(testPos, col, solids))
            {
                pos.Y += signY;
                pixelsY -= signY;
            }
            else
            {
                // Hit floor or ceiling — stop vertical movement
                vel = vel with { Y = 0f };
                pos.RemainderY = 0f;
                break;
            }
        }
    }

    /// <summary>
    /// Checks if the entity's collider at the given position overlaps
    /// any solid rectangle.
    /// </summary>
    /// <param name="pos">The test position.</param>
    /// <param name="col">The entity's collision box.</param>
    /// <param name="solids">Span of solid rectangles.</param>
    /// <returns>True if any overlap is detected.</returns>
    private static bool OverlapsAnySolid(
        in Position pos,
        in ColliderBox col,
        ReadOnlySpan<RectF> solids)
    {
        float l = col.Left(pos.X);
        float r = col.Right(pos.X);
        float t = col.Top(pos.Y);
        float b = col.Bottom(pos.Y);

        for (int i = 0; i < solids.Length; i++)
        {
            ref readonly var s = ref solids[i];
            if (r > s.Left && l < s.Right && b > s.Top && t < s.Bottom)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Static overlap test between two AABBs. Useful for trigger zones,
    /// pickup collection, and other non-resolving collision checks.
    /// </summary>
    /// <param name="aLeft">Left edge of AABB A.</param>
    /// <param name="aTop">Top edge of AABB A.</param>
    /// <param name="aRight">Right edge of AABB A.</param>
    /// <param name="aBottom">Bottom edge of AABB A.</param>
    /// <param name="bLeft">Left edge of AABB B.</param>
    /// <param name="bTop">Top edge of AABB B.</param>
    /// <param name="bRight">Right edge of AABB B.</param>
    /// <param name="bBottom">Bottom edge of AABB B.</param>
    /// <returns>True if the two AABBs overlap.</returns>
    public static bool AABBOverlap(
        float aLeft, float aTop, float aRight, float aBottom,
        float bLeft, float bTop, float bRight, float bBottom)
    {
        return aRight > bLeft && aLeft < bRight &&
               aBottom > bTop && aTop < bBottom;
    }

    /// <summary>
    /// Computes the penetration depth between two overlapping AABBs.
    /// Returns the minimum translation vector (MTV) to separate them.
    /// </summary>
    /// <param name="aLeft">Left edge of AABB A.</param>
    /// <param name="aTop">Top edge of AABB A.</param>
    /// <param name="aRight">Right edge of AABB A.</param>
    /// <param name="aBottom">Bottom edge of AABB A.</param>
    /// <param name="bLeft">Left edge of AABB B.</param>
    /// <param name="bTop">Top edge of AABB B.</param>
    /// <param name="bRight">Right edge of AABB B.</param>
    /// <param name="bBottom">Bottom edge of AABB B.</param>
    /// <returns>
    /// The minimum translation vector to push A out of B,
    /// or <see cref="Vector2.Zero"/> if not overlapping.
    /// </returns>
    public static Vector2 GetPenetration(
        float aLeft, float aTop, float aRight, float aBottom,
        float bLeft, float bTop, float bRight, float bBottom)
    {
        if (!AABBOverlap(aLeft, aTop, aRight, aBottom,
                         bLeft, bTop, bRight, bBottom))
            return Vector2.Zero;

        float overlapLeft  = aRight - bLeft;
        float overlapRight = bRight - aLeft;
        float overlapTop   = aBottom - bTop;
        float overlapBot   = bBottom - aTop;

        // Find the smallest overlap axis
        float minX = overlapLeft < overlapRight ? -overlapLeft : overlapRight;
        float minY = overlapTop < overlapBot ? -overlapTop : overlapBot;

        // Push along the axis with smallest penetration
        if (Math.Abs(minX) < Math.Abs(minY))
            return new Vector2(minX, 0f);
        else
            return new Vector2(0f, minY);
    }
}
