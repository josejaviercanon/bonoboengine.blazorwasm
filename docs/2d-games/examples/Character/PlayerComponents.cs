// ============================================================================
// PlayerComponents.cs — All Player ECS Components
// Extracted from: G52 — 2D Platformer Character Controller
// Part of: Universal 2D Engine Toolkit Examples
// ============================================================================

using Arch.Core;

namespace U2DToolkit.Examples.Character;

/// <summary>
/// World-space position with sub-pixel accumulator.
/// Sub-pixel accumulation ensures smooth movement at low speeds —
/// without it, a character moving 0.3px/frame rounds to 0 and never moves.
/// </summary>
public record struct Position(float X, float Y)
{
    /// <summary>Remainder from integer snapping on X axis. Accumulated each frame.</summary>
    public float RemainderX;

    /// <summary>Remainder from integer snapping on Y axis. Accumulated each frame.</summary>
    public float RemainderY;
}

/// <summary>
/// Current velocity in pixels per second.
/// Set directly each frame (kinematic approach) — not driven by forces.
/// </summary>
public record struct Velocity(float X, float Y);

/// <summary>
/// Axis-aligned bounding box relative to Position (offset from entity origin).
/// All collision detection works against this box.
/// </summary>
/// <param name="OffsetX">Horizontal offset from entity position to box left edge.</param>
/// <param name="OffsetY">Vertical offset from entity position to box top edge.</param>
/// <param name="Width">Width of the collision box in pixels.</param>
/// <param name="Height">Height of the collision box in pixels.</param>
public record struct ColliderBox(float OffsetX, float OffsetY, float Width, float Height)
{
    /// <summary>World-space left edge of the collider.</summary>
    public float Left(float posX)   => posX + OffsetX;

    /// <summary>World-space right edge of the collider.</summary>
    public float Right(float posX)  => posX + OffsetX + Width;

    /// <summary>World-space top edge of the collider.</summary>
    public float Top(float posY)    => posY + OffsetY;

    /// <summary>World-space bottom edge of the collider.</summary>
    public float Bottom(float posY) => posY + OffsetY + Height;
}

/// <summary>
/// Tracks whether the entity is standing on solid ground.
/// Includes ground normal (for slopes) and a reference to the
/// platform entity (for moving platforms).
/// </summary>
public record struct Grounded(bool IsGrounded)
{
    /// <summary>X component of the ground surface normal.</summary>
    public float NormalX = 0f;

    /// <summary>Y component of the ground surface normal. -1 = flat ground.</summary>
    public float NormalY = -1f;

    /// <summary>
    /// Entity reference of what we're standing on.
    /// Used for moving platform velocity transfer.
    /// </summary>
    public Entity? PlatformEntity = null;
}

/// <summary>
/// Full platformer controller configuration. All tuning parameters in one place.
/// <para>
/// Design philosophy: express jumps as <see cref="JumpHeight"/> and
/// <see cref="TimeToApex"/>, then call <see cref="DeriveJumpParameters"/>
/// to compute gravity and jump velocity automatically.
/// </para>
/// </summary>
public record struct PlayerController
{
    // ── Horizontal Movement ──────────────────────────────────────────

    /// <summary>Max horizontal speed in pixels/second.</summary>
    public float MoveSpeed;

    /// <summary>Ground acceleration in pixels/second².</summary>
    public float GroundAcceleration;

    /// <summary>Ground deceleration (friction) in pixels/second².</summary>
    public float GroundDeceleration;

    /// <summary>Air acceleration in pixels/second² (less than ground for committed arcs).</summary>
    public float AirAcceleration;

    /// <summary>Air deceleration in pixels/second² (low = floaty momentum).</summary>
    public float AirDeceleration;

    /// <summary>Extra acceleration multiplier when reversing direction. 1.0 = same as normal.</summary>
    public float TurnMultiplier;

    // ── Jump ─────────────────────────────────────────────────────────

    /// <summary>Desired jump apex height in pixels.</summary>
    public float JumpHeight;

    /// <summary>Seconds to reach the jump apex.</summary>
    public float TimeToApex;

    /// <summary>Derived: 2 * JumpHeight / (TimeToApex²). Call DeriveJumpParameters().</summary>
    public float Gravity;

    /// <summary>Derived: 2 * JumpHeight / TimeToApex. Call DeriveJumpParameters().</summary>
    public float JumpVelocity;

    /// <summary>Gravity multiplier when falling (1.5–2.5 typical). Makes descent snappy.</summary>
    public float FallGravityMultiplier;

    /// <summary>Terminal velocity in pixels/second.</summary>
    public float MaxFallSpeed;

    /// <summary>Reduced gravity near apex when jump is held (0.4–0.7). Creates hang time.</summary>
    public float ApexGravityMultiplier;

    /// <summary>Velocity range (px/s) considered "near apex" for ApexGravityMultiplier.</summary>
    public float ApexThreshold;

    // ── Multi-Jump ───────────────────────────────────────────────────

    /// <summary>Max number of jumps. 1 = normal, 2 = double jump, etc.</summary>
    public int MaxJumps;

    /// <summary>Remaining jumps before landing. Reset on ground contact.</summary>
    public int JumpsRemaining;

    // ── Coyote Time ──────────────────────────────────────────────────

    /// <summary>
    /// Seconds after leaving ground where jump is still allowed.
    /// Named after Wile E. Coyote — handles "jumped too late" feeling.
    /// 5–8 frames (0.083–0.133s) is standard.
    /// </summary>
    public float CoyoteTime;

    /// <summary>Current coyote time countdown.</summary>
    public float CoyoteTimer;

    // ── Jump Buffer ──────────────────────────────────────────────────

    /// <summary>
    /// Seconds to hold a buffered jump input.
    /// If jump is pressed before landing, it fires on contact.
    /// Handles "jumped too early" feeling. 6–8 frames (0.1–0.133s) standard.
    /// </summary>
    public float JumpBufferTime;

    /// <summary>Current jump buffer countdown.</summary>
    public float JumpBufferTimer;

    // ── Wall ─────────────────────────────────────────────────────────

    /// <summary>Max fall speed while wall-sliding (px/s). Much slower than normal fall.</summary>
    public float WallSlideSpeed;

    /// <summary>Horizontal velocity on wall jump (px/s). Pushes away from wall.</summary>
    public float WallJumpHVelocity;

    /// <summary>Vertical velocity on wall jump (px/s). Upward impulse.</summary>
    public float WallJumpVVelocity;

    /// <summary>Max seconds the player can cling to a wall before sliding.</summary>
    public float WallClingTime;

    /// <summary>Current wall cling countdown.</summary>
    public float WallClingTimer;

    // ── Dash ─────────────────────────────────────────────────────────

    /// <summary>Dash velocity in pixels/second.</summary>
    public float DashSpeed;

    /// <summary>How long a dash lasts in seconds.</summary>
    public float DashDuration;

    /// <summary>Seconds between dashes (cooldown).</summary>
    public float DashCooldown;

    /// <summary>Current dash timer countdown.</summary>
    public float DashTimer;

    /// <summary>Current dash cooldown countdown.</summary>
    public float DashCooldownTimer;

    /// <summary>Whether the entity is currently dashing (no gravity, fixed velocity).</summary>
    public bool IsDashing;

    // ── State ────────────────────────────────────────────────────────

    /// <summary>1 = facing right, -1 = facing left.</summary>
    public int FacingDirection;

    /// <summary>Whether the entity is currently touching a wall.</summary>
    public bool IsOnWall;

    /// <summary>1 = wall to right, -1 = wall to left.</summary>
    public int WallDirection;

    /// <summary>Whether the entity is currently on a ladder.</summary>
    public bool IsOnLadder;

    /// <summary>Grounded state from the previous frame (for transition detection).</summary>
    public bool WasGrounded;

    /// <summary>
    /// Calculate gravity and jump velocity from designer-friendly values.
    /// Must be called after setting <see cref="JumpHeight"/> and <see cref="TimeToApex"/>.
    /// <para>
    /// From kinematic equations:
    /// <c>gravity = 2 * jumpHeight / timeToApex²</c>,
    /// <c>jumpVelocity = 2 * jumpHeight / timeToApex</c>.
    /// </para>
    /// </summary>
    public void DeriveJumpParameters()
    {
        Gravity      = (2f * JumpHeight) / (TimeToApex * TimeToApex);
        JumpVelocity = (2f * JumpHeight) / TimeToApex;
    }

    /// <summary>
    /// Create a controller with sensible defaults — a balanced middle ground.
    /// Call <see cref="DeriveJumpParameters"/> after creation.
    /// </summary>
    public static PlayerController Default() => new()
    {
        MoveSpeed             = 200f,
        GroundAcceleration    = 1800f,
        GroundDeceleration    = 2400f,
        AirAcceleration       = 1200f,
        AirDeceleration       = 600f,
        TurnMultiplier        = 2.0f,

        JumpHeight            = 72f,
        TimeToApex            = 0.35f,
        FallGravityMultiplier = 2.0f,
        MaxFallSpeed          = 400f,
        ApexGravityMultiplier = 0.5f,
        ApexThreshold         = 40f,

        MaxJumps              = 1,
        JumpsRemaining        = 1,

        CoyoteTime            = 0.1f,   // ~6 frames at 60fps
        JumpBufferTime        = 0.133f, // ~8 frames at 60fps

        WallSlideSpeed        = 60f,
        WallJumpHVelocity     = 180f,
        WallJumpVVelocity     = 280f,
        WallClingTime         = 0.5f,

        DashSpeed             = 500f,
        DashDuration          = 0.15f,
        DashCooldown          = 0.4f,

        FacingDirection       = 1,
    };
}

/// <summary>
/// Snapshot of player input for the current frame.
/// Filled by the input system and consumed by the controller system.
/// </summary>
public record struct InputState
{
    /// <summary>Horizontal input: -1 (left) to 1 (right).</summary>
    public float X;

    /// <summary>Vertical input: -1 (up) to 1 (down).</summary>
    public float Y;

    /// <summary>True only on the frame jump was pressed.</summary>
    public bool JumpPressed;

    /// <summary>True while the jump button is held down.</summary>
    public bool JumpHeld;

    /// <summary>True only on the frame dash was pressed.</summary>
    public bool DashPressed;

    /// <summary>True while the down direction is held (for drop-through).</summary>
    public bool DownPressed;
}

/// <summary>
/// Float-precision rectangle for collision geometry.
/// Used instead of <c>Rectangle</c> to avoid integer truncation issues.
/// </summary>
public record struct RectF(float Left, float Top, float Right, float Bottom)
{
    /// <summary>Width of the rectangle.</summary>
    public float Width  => Right - Left;

    /// <summary>Height of the rectangle.</summary>
    public float Height => Bottom - Top;
}

/// <summary>Marks an entity as a one-way platform (pass through from below).</summary>
public record struct OneWayPlatform;

/// <summary>Marks an entity as a moving platform with constant velocity.</summary>
public record struct MovingPlatform(float VelocityX, float VelocityY);

/// <summary>Marks the player as currently dropping through a one-way platform.</summary>
public record struct DroppingThrough(float Timer);

/// <summary>Tracks invincibility frames (during dash, damage, etc.).</summary>
public record struct Invincible(float Timer);
