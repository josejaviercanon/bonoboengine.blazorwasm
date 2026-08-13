// ============================================================================
// PlayerControllerSystem.cs — Complete Character Controller System
// Extracted from: G52 — 2D Platformer Character Controller
// Part of: Universal 2D Engine Toolkit Examples
// ============================================================================

using Arch.Core;
using Arch.Core.Extensions;
using Microsoft.Xna.Framework;

namespace U2DToolkit.Examples.Character;

/// <summary>
/// Complete 2D platformer character controller as an Arch ECS system.
/// Integrates horizontal movement, gravity, jumping (with coyote time
/// and jump buffering), wall mechanics, dashing, and collision resolution
/// into a single coherent update loop.
/// <para>
/// Update order:
/// Input → Dash → Horizontal Movement → Gravity → Wall Detection →
/// Jump (coyote + buffer + wall jump) → Collision Resolution →
/// Corner Correction → Ground Detection → Platform Sync
/// </para>
/// </summary>
public class PlayerControllerSystem
{
    private readonly QueryDescription _playerQuery = new QueryDescription()
        .WithAll<Position, Velocity, PlayerController, ColliderBox, Grounded>();

    /// <summary>Solid geometry gathered each frame from the tilemap or collidable entities.</summary>
    private RectF[] _solids = Array.Empty<RectF>();
    private int _solidCount;

    /// <summary>
    /// Call once per frame before <see cref="Update"/> to provide current solid geometry.
    /// Typically built from the tilemap collision grid + dynamic collidable entities.
    /// </summary>
    public void SetSolids(RectF[] solids, int count)
    {
        _solids = solids;
        _solidCount = count;
    }

    /// <summary>
    /// Run the full character controller pipeline for all player entities.
    /// </summary>
    /// <param name="world">The Arch ECS world.</param>
    /// <param name="input">Current frame's input state.</param>
    /// <param name="dt">Delta time in seconds.</param>
    public void Update(World world, InputState input, float dt)
    {
        var solids = _solids.AsSpan(0, _solidCount);

        world.Query(in _playerQuery, (
            Entity entity,
            ref Position pos,
            ref Velocity vel,
            ref PlayerController ctrl,
            ref ColliderBox col,
            ref Grounded grounded) =>
        {
            // ═══════════════════════════════════════════
            //  1. LADDER CHECK — skip normal pipeline
            // ═══════════════════════════════════════════
            if (ctrl.IsOnLadder)
                return;

            // ═══════════════════════════════════════════
            //  2. DASH
            // ═══════════════════════════════════════════
            TryDash(ref vel, ref ctrl, input.DashPressed, input.X, input.Y);
            UpdateDash(ref vel, ref ctrl, dt);

            if (ctrl.IsDashing)
            {
                // During dash: skip gravity and normal movement, just collide
                CollisionResolver.MoveAndCollide(ref pos, ref vel, in col, solids, dt);
                return;
            }

            // ═══════════════════════════════════════════
            //  3. HORIZONTAL MOVEMENT
            // ═══════════════════════════════════════════
            ApplyHorizontalMovement(ref vel, ref ctrl, input.X, dt, grounded.IsGrounded);

            // ═══════════════════════════════════════════
            //  4. GRAVITY (with variable jump height + apex float)
            // ═══════════════════════════════════════════
            ApplyGravity(ref vel, ref ctrl, input.JumpHeld, dt);

            // ═══════════════════════════════════════════
            //  5. WALL DETECTION
            // ═══════════════════════════════════════════
            bool wasOnWall = ctrl.IsOnWall;
            ctrl.IsOnWall = false;

            if (!grounded.IsGrounded)
            {
                if (CheckWall(in pos, in col, solids, ctrl.FacingDirection))
                {
                    ctrl.IsOnWall = true;
                    ctrl.WallDirection = ctrl.FacingDirection;
                }
            }

            // Wall cling / slide
            if (ctrl.IsOnWall && !grounded.IsGrounded)
            {
                if (!wasOnWall)
                    ctrl.WallClingTimer = ctrl.WallClingTime;

                if (ctrl.WallClingTimer > 0f)
                {
                    vel = vel with { Y = 0f };
                    ctrl.WallClingTimer -= dt;
                }
                else
                {
                    vel = vel with { Y = Math.Min(vel.Y, ctrl.WallSlideSpeed) };
                }
            }

            // ═══════════════════════════════════════════
            //  6. JUMP (coyote time + buffer + wall jump)
            // ═══════════════════════════════════════════
            if (!grounded.IsGrounded)
                ctrl.CoyoteTimer = Math.Max(ctrl.CoyoteTimer - dt, 0f);

            if (input.JumpPressed)
                ctrl.JumpBufferTimer = ctrl.JumpBufferTime;
            ctrl.JumpBufferTimer = Math.Max(ctrl.JumpBufferTimer - dt, 0f);

            bool wantsJump = input.JumpPressed || ctrl.JumpBufferTimer > 0f;

            // Wall jump takes priority
            if (wantsJump && ctrl.IsOnWall && !grounded.IsGrounded)
            {
                vel = new Velocity(
                    -ctrl.WallDirection * ctrl.WallJumpHVelocity,
                    -ctrl.WallJumpVVelocity);
                ctrl.IsOnWall = false;
                ctrl.FacingDirection = -ctrl.WallDirection;
                ctrl.JumpBufferTimer = 0f;
                ctrl.CoyoteTimer = 0f;
                ctrl.JumpsRemaining = Math.Max(ctrl.JumpsRemaining - 1, 0);
            }
            // Normal / coyote / multi jump
            else if (TryJump(ref vel, ref ctrl, ref grounded, wantsJump))
            {
                ctrl.JumpBufferTimer = 0f;
            }

            // ═══════════════════════════════════════════
            //  7. COLLISION RESOLUTION
            // ═══════════════════════════════════════════
            CollisionResolver.MoveAndCollide(ref pos, ref vel, in col, solids, dt);

            // ═══════════════════════════════════════════
            //  8. CORNER CORRECTION (when hitting ceiling)
            // ═══════════════════════════════════════════
            if (vel.Y < 0)
                TryCornerCorrect(ref pos, in col, in vel, solids);

            // ═══════════════════════════════════════════
            //  9. GROUND DETECTION
            // ═══════════════════════════════════════════
            bool wasGrounded = grounded.IsGrounded;

            grounded.IsGrounded = CheckGround(
                in pos, in col, solids,
                out grounded.NormalX, out grounded.NormalY,
                out float groundY);

            // Handle drop-through timer
            if (world.Has<DroppingThrough>(entity))
            {
                ref var drop = ref world.Get<DroppingThrough>(entity);
                drop = drop with { Timer = drop.Timer - dt };
                if (drop.Timer <= 0f)
                    world.Remove<DroppingThrough>(entity);
            }

            // Landing
            if (grounded.IsGrounded && !wasGrounded)
            {
                ctrl.JumpsRemaining = ctrl.MaxJumps;
                ctrl.CoyoteTimer = ctrl.CoyoteTime;
                pos.Y = groundY - col.Height - col.OffsetY;

                // Execute buffered jump on landing
                if (ctrl.JumpBufferTimer > 0f)
                {
                    TryJump(ref vel, ref ctrl, ref grounded, true);
                    ctrl.JumpBufferTimer = 0f;
                }
            }

            // Left ground (without jumping)
            if (!grounded.IsGrounded && wasGrounded && vel.Y >= 0)
            {
                ctrl.CoyoteTimer = ctrl.CoyoteTime;
            }

            ctrl.WasGrounded = wasGrounded;

            // ═══════════════════════════════════════════
            // 10. DROP-THROUGH (one-way platforms)
            // ═══════════════════════════════════════════
            if (input.DownPressed && input.JumpPressed
                && grounded.IsGrounded && grounded.PlatformEntity.HasValue
                && world.Has<OneWayPlatform>(grounded.PlatformEntity.Value))
            {
                world.Add(entity, new DroppingThrough(Timer: 0.15f));
                grounded.IsGrounded = false;
                pos.Y += 2f;
            }
        });
    }

    // ── Horizontal Movement ──────────────────────────────────────────

    /// <summary>
    /// Accelerate/decelerate horizontally with different curves for
    /// ground vs air, and a turn multiplier for snappy direction changes.
    /// </summary>
    private static void ApplyHorizontalMovement(
        ref Velocity vel, ref PlayerController ctrl,
        float inputX, float dt, bool isGrounded)
    {
        float accel = isGrounded ? ctrl.GroundAcceleration : ctrl.AirAcceleration;
        float decel = isGrounded ? ctrl.GroundDeceleration : ctrl.AirDeceleration;

        if (Math.Abs(inputX) > 0.01f)
        {
            ctrl.FacingDirection = inputX > 0 ? 1 : -1;

            bool turning = (vel.X > 0 && inputX < 0) || (vel.X < 0 && inputX > 0);
            float effectiveAccel = turning ? accel * ctrl.TurnMultiplier : accel;

            float target = inputX * ctrl.MoveSpeed;
            vel = vel with { X = MoveToward(vel.X, target, effectiveAccel * dt) };
        }
        else
        {
            vel = vel with { X = MoveToward(vel.X, 0f, decel * dt) };
        }
    }

    // ── Gravity ──────────────────────────────────────────────────────

    /// <summary>
    /// Apply gravity with variable-height jump support and apex float.
    /// Three gravity zones: falling (heavy), rising with jump released (heavy),
    /// near apex with jump held (light).
    /// </summary>
    private static void ApplyGravity(
        ref Velocity vel, ref PlayerController ctrl,
        bool jumpHeld, float dt)
    {
        if (ctrl.IsDashing) return;

        float gravity = ctrl.Gravity;

        if (vel.Y > 0)
        {
            // Falling — heavier gravity for snappy descent
            gravity *= ctrl.FallGravityMultiplier;
        }
        else if (vel.Y < 0 && !jumpHeld)
        {
            // Released jump early — cut the arc short
            gravity *= ctrl.FallGravityMultiplier;
        }
        else if (Math.Abs(vel.Y) < ctrl.ApexThreshold && jumpHeld)
        {
            // Near the apex with jump held — float!
            gravity *= ctrl.ApexGravityMultiplier;
        }

        vel = vel with { Y = Math.Min(vel.Y + gravity * dt, ctrl.MaxFallSpeed) };
    }

    // ── Jump ─────────────────────────────────────────────────────────

    /// <summary>
    /// Attempt a jump. Checks grounded state, coyote time, and multi-jump.
    /// </summary>
    private static bool TryJump(
        ref Velocity vel, ref PlayerController ctrl,
        ref Grounded grounded, bool jumpPressed)
    {
        if (!jumpPressed) return false;

        bool canJump = grounded.IsGrounded
                    || ctrl.CoyoteTimer > 0f
                    || ctrl.JumpsRemaining > 0;

        if (!canJump) return false;

        if (!grounded.IsGrounded && ctrl.CoyoteTimer > 0f)
            ctrl.CoyoteTimer = 0f;

        vel = vel with { Y = -ctrl.JumpVelocity };
        ctrl.JumpsRemaining--;
        grounded.IsGrounded = false;

        return true;
    }

    // ── Dash ─────────────────────────────────────────────────────────

    private static void TryDash(
        ref Velocity vel, ref PlayerController ctrl,
        bool dashPressed, float inputX, float inputY)
    {
        if (!dashPressed || ctrl.IsDashing || ctrl.DashCooldownTimer > 0f)
            return;

        ctrl.IsDashing = true;
        ctrl.DashTimer = ctrl.DashDuration;
        ctrl.DashCooldownTimer = ctrl.DashCooldown;

        float dirX = inputX;
        float dirY = inputY;

        // If no input, dash in facing direction
        if (Math.Abs(dirX) < 0.1f && Math.Abs(dirY) < 0.1f)
        {
            dirX = ctrl.FacingDirection;
            dirY = 0f;
        }

        // Normalize for diagonal dashes
        float len = MathF.Sqrt(dirX * dirX + dirY * dirY);
        if (len > 0.01f)
        {
            dirX /= len;
            dirY /= len;
        }

        vel = new Velocity(dirX * ctrl.DashSpeed, dirY * ctrl.DashSpeed);
    }

    private static void UpdateDash(
        ref Velocity vel, ref PlayerController ctrl, float dt)
    {
        if (ctrl.DashCooldownTimer > 0f)
            ctrl.DashCooldownTimer -= dt;

        if (!ctrl.IsDashing) return;

        ctrl.DashTimer -= dt;
        if (ctrl.DashTimer <= 0f)
        {
            ctrl.IsDashing = false;
            vel = new Velocity(vel.X * 0.3f, 0f);
        }
    }

    // ── Ground Detection ─────────────────────────────────────────────

    private const float SkinWidth = 2f;
    private const float GroundCheckDistance = SkinWidth + 1f;
    private const int GroundRayCount = 3;

    /// <summary>
    /// Multi-ray ground detection. Casts rays downward from the entity's feet.
    /// </summary>
    private static bool CheckGround(
        in Position pos, in ColliderBox col,
        ReadOnlySpan<RectF> solids,
        out float normalX, out float normalY,
        out float groundY)
    {
        normalX = 0f;
        normalY = -1f;
        groundY = 0f;

        float left   = col.Left(pos.X) + SkinWidth;
        float right  = col.Right(pos.X) - SkinWidth;
        float bottom = col.Bottom(pos.Y) - SkinWidth;

        float shortest = float.MaxValue;
        bool anyHit = false;

        for (int i = 0; i < GroundRayCount; i++)
        {
            float t = GroundRayCount == 1 ? 0.5f : (float)i / (GroundRayCount - 1);
            float rayX = MathHelper.Lerp(left, right, t);
            float rayY1 = bottom;
            float rayY2 = bottom + GroundCheckDistance;

            for (int s = 0; s < solids.Length; s++)
            {
                ref readonly var solid = ref solids[s];
                if (rayX < solid.Left || rayX > solid.Right) continue;

                float surfaceY = solid.Top;
                if (surfaceY >= rayY1 && surfaceY <= rayY2)
                {
                    float dist = surfaceY - rayY1;
                    if (dist < shortest)
                    {
                        shortest = dist;
                        groundY = surfaceY;
                        anyHit = true;
                        normalX = 0f;
                        normalY = -1f;
                    }
                }
            }
        }

        return anyHit;
    }

    // ── Wall Detection ───────────────────────────────────────────────

    private const int WallRayCount = 3;

    private static bool CheckWall(
        in Position pos, in ColliderBox col,
        ReadOnlySpan<RectF> solids, int direction)
    {
        float sideX = direction > 0
            ? col.Right(pos.X) - SkinWidth
            : col.Left(pos.X) + SkinWidth;

        float top    = col.Top(pos.Y) + SkinWidth;
        float bottom = col.Bottom(pos.Y) - SkinWidth;

        for (int i = 0; i < WallRayCount; i++)
        {
            float t = WallRayCount == 1 ? 0.5f : (float)i / (WallRayCount - 1);
            float rayY = MathHelper.Lerp(top, bottom, t);
            float rayEnd = sideX + direction * GroundCheckDistance;

            for (int s = 0; s < solids.Length; s++)
            {
                ref readonly var solid = ref solids[s];
                if (rayY < solid.Top || rayY > solid.Bottom) continue;

                bool hit = direction > 0
                    ? (solid.Left >= sideX && solid.Left <= rayEnd)
                    : (solid.Right <= sideX && solid.Right >= rayEnd);

                if (hit) return true;
            }
        }

        return false;
    }

    // ── Corner Correction ────────────────────────────────────────────

    private const float MaxCornerCorrection = 6f;

    /// <summary>
    /// When hitting a ceiling by a few pixels, nudge horizontally to clear it.
    /// Prevents frustrating "clipped by 2 pixels" jump kills.
    /// </summary>
    private static void TryCornerCorrect(
        ref Position pos, in ColliderBox col, in Velocity vel,
        ReadOnlySpan<RectF> solids)
    {
        if (vel.Y >= 0) return;
        if (!HasCeilingCollision(pos, col, solids)) return;

        for (float offset = 1f; offset <= MaxCornerCorrection; offset += 1f)
        {
            var testRight = new Position(pos.X + offset, pos.Y);
            if (!HasCeilingCollision(testRight, col, solids))
            {
                pos.X += offset;
                return;
            }

            var testLeft = new Position(pos.X - offset, pos.Y);
            if (!HasCeilingCollision(testLeft, col, solids))
            {
                pos.X -= offset;
                return;
            }
        }
    }

    private static bool HasCeilingCollision(
        in Position pos, in ColliderBox col,
        ReadOnlySpan<RectF> solids)
    {
        float left  = col.Left(pos.X);
        float right = col.Right(pos.X);
        float top   = col.Top(pos.Y);

        for (int i = 0; i < solids.Length; i++)
        {
            ref readonly var s = ref solids[i];
            if (right > s.Left && left < s.Right && top < s.Bottom && top > s.Top)
                return true;
        }
        return false;
    }

    // ── Utility ──────────────────────────────────────────────────────

    private static float MoveToward(float current, float target, float maxDelta)
    {
        if (Math.Abs(target - current) <= maxDelta)
            return target;
        return current + Math.Sign(target - current) * maxDelta;
    }
}
