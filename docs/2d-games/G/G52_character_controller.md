# G52 — 2D Platformer Character Controller

> **Category:** Guide · **Related:** [G3 Physics & Collision](./G3_physics_and_collision.md) · [C2 Game Feel & Genre Craft](../C/C2_game_feel_and_genre_craft.md) · [G30 Game Feel Tooling](./G30_game_feel_tooling.md) · [G7 Input Handling](./G7_input_handling.md) · [G53 Side-Scrolling Perspective](./G53_side_scrolling.md)

---

## Table of Contents

1. [Controller Philosophy](#1--controller-philosophy)
2. [Core Components](#2--core-components)
3. [Ground Detection](#3--ground-detection)
4. [Basic Movement](#4--basic-movement)
5. [Jump System](#5--jump-system)
6. [Coyote Time](#6--coyote-time)
7. [Jump Buffering](#7--jump-buffering)
8. [Wall Mechanics](#8--wall-mechanics)
9. [Slopes](#9--slopes)
10. [One-Way Platforms](#10--one-way-platforms)
11. [Moving Platforms](#11--moving-platforms)
12. [Ladders & Climbing](#12--ladders--climbing)
13. [Dash / Dodge](#13--dash--dodge)
14. [Corner Correction](#14--corner-correction)
15. [Collision Resolution](#15--collision-resolution)
16. [Complete ECS System](#16--complete-ecs-system)
17. [Tuning Reference Table](#17--tuning-reference-table)

---

## 1 — Controller Philosophy

### Kinematic vs Physics-Based

There are two fundamental approaches to building a character controller:

| Approach | Description | Used By |
|----------|-------------|---------|
| **Physics-based** | Apply forces/impulses to a rigid body and let the physics engine resolve movement | Some puzzle platformers, ragdoll games |
| **Kinematic** | Directly set velocity each frame, handle collision manually | Celeste, Hollow Knight, Dead Cells, Mega Man, Mario |

Almost every celebrated platformer uses kinematic control. The reason is simple: **physics engines solve for realism, not fun.** A physically accurate jump has a fixed parabolic arc determined by mass and force. A *fun* jump has variable height, coyote time, jump buffering, apex hang, and a dozen other lies we tell the player.

### The "Game Feel First" Approach

Design your controller parameters around **what feels right**, not what's physically correct:

- **Jump height** and **time to apex** are your primary inputs — gravity is *derived* from these.
- Fall speed gets a separate (higher) gravity multiplier so descents feel snappy.
- Acceleration curves differ between ground and air.
- The player can jump after walking off a ledge (coyote time) — physically impossible, but essential.

> 🎯 **Rule of thumb:** If you're using `AddForce()` on your player character, you've already lost control of game feel. Set velocity directly.

### Why ECS?

An ECS architecture separates *data* (components) from *behavior* (systems). This lets you:

- Reuse movement/collision logic for NPCs, enemies, and projectiles.
- Hot-swap controller components (e.g., switch from `PlayerController` to `CutsceneController`).
- Profile and optimize individual systems independently.
- Compose behaviors: an entity with `WallSlide` + `Dash` gets both mechanics with zero coupling.

---

## 2 — Core Components

Every component is a `record struct` for value-type semantics, stack allocation, and structural equality.

### Position & Velocity

```csharp
/// <summary>World-space position with sub-pixel accumulator.</summary>
public record struct Position(float X, float Y)
{
    /// <summary>Remainder from integer snapping. Accumulated each frame for smooth low-speed movement.</summary>
    public float RemainderX;
    public float RemainderY;
}

/// <summary>Current velocity in pixels per second.</summary>
public record struct Velocity(float X, float Y);
```

### Collider

```csharp
/// <summary>Axis-aligned bounding box relative to Position (offset from entity origin).</summary>
public record struct ColliderBox(float OffsetX, float OffsetY, float Width, float Height)
{
    public float Left(float posX)   => posX + OffsetX;
    public float Right(float posX)  => posX + OffsetX + Width;
    public float Top(float posY)    => posY + OffsetY;
    public float Bottom(float posY) => posY + OffsetY + Height;
}
```

### Grounded State

```csharp
/// <summary>Tracks whether the entity is standing on solid ground.</summary>
public record struct Grounded(bool IsGrounded)
{
    /// <summary>Normal vector of the ground surface (for slope handling).</summary>
    public float NormalX = 0f;
    public float NormalY = -1f;

    /// <summary>Entity reference of what we're standing on (for moving platforms).</summary>
    public Entity? PlatformEntity = null;
}
```

### Player Controller

This is the big one — all tuning parameters in one place.

```csharp
/// <summary>Full platformer controller configuration.</summary>
public record struct PlayerController
{
    // ── Horizontal Movement ──
    public float MoveSpeed;              // Max horizontal speed (px/s)
    public float GroundAcceleration;     // Ground acceleration (px/s²)
    public float GroundDeceleration;     // Ground deceleration / friction (px/s²)
    public float AirAcceleration;        // Air acceleration (px/s²)
    public float AirDeceleration;        // Air deceleration (px/s²)
    public float TurnMultiplier;         // Extra accel when reversing direction (1.0 = same as normal)

    // ── Jump ──
    public float JumpHeight;             // Desired jump apex in pixels
    public float TimeToApex;             // Seconds to reach apex
    public float Gravity;                // Derived: 2 * JumpHeight / (TimeToApex²)
    public float JumpVelocity;           // Derived: 2 * JumpHeight / TimeToApex
    public float FallGravityMultiplier;  // Gravity scale when falling (typically 1.5–2.5)
    public float MaxFallSpeed;           // Terminal velocity (px/s)
    public float ApexGravityMultiplier;  // Reduced gravity near apex when jump held (0.4–0.7)
    public float ApexThreshold;          // Velocity range considered "near apex" (px/s)

    // ── Multi-Jump ──
    public int MaxJumps;                 // 1 = normal, 2 = double jump, etc.
    public int JumpsRemaining;

    // ── Coyote Time ──
    public float CoyoteTime;            // Seconds after leaving ground where jump is still allowed
    public float CoyoteTimer;

    // ── Jump Buffer ──
    public float JumpBufferTime;         // Seconds to hold a buffered jump input
    public float JumpBufferTimer;

    // ── Wall ──
    public float WallSlideSpeed;         // Max fall speed while wall-sliding (px/s)
    public float WallJumpHVelocity;      // Horizontal velocity on wall jump (px/s)
    public float WallJumpVVelocity;      // Vertical velocity on wall jump (px/s)
    public float WallClingTime;          // Max seconds you can cling before sliding
    public float WallClingTimer;

    // ── Dash ──
    public float DashSpeed;              // Dash velocity (px/s)
    public float DashDuration;           // How long a dash lasts (s)
    public float DashCooldown;           // Seconds between dashes
    public float DashTimer;
    public float DashCooldownTimer;
    public bool  IsDashing;

    // ── State ──
    public int   FacingDirection;        // 1 = right, -1 = left
    public bool  IsOnWall;
    public int   WallDirection;          // 1 = wall to right, -1 = wall to left
    public bool  IsOnLadder;
    public bool  WasGrounded;            // Grounded state from previous frame

    /// <summary>Calculate gravity and jump velocity from designer-friendly values.</summary>
    public void DeriveJumpParameters()
    {
        // From kinematic equations:
        //   jumpHeight = v₀·t - ½·g·t²   (at apex, v=0 → v₀ = g·t)
        //   jumpHeight = ½·g·t²
        //   g = 2·jumpHeight / t²
        //   v₀ = g·t = 2·jumpHeight / t
        Gravity      = (2f * JumpHeight) / (TimeToApex * TimeToApex);
        JumpVelocity = (2f * JumpHeight) / TimeToApex;
    }

    /// <summary>Create with sensible defaults.</summary>
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
        WallClingTime          = 0.5f,

        DashSpeed             = 500f,
        DashDuration          = 0.15f,
        DashCooldown          = 0.4f,

        FacingDirection       = 1,
    };
}
```

### Auxiliary Tag Components

```csharp
/// <summary>Marks an entity as a one-way platform (pass through from below).</summary>
public record struct OneWayPlatform;

/// <summary>Marks an entity as a moving platform.</summary>
public record struct MovingPlatform(float VelocityX, float VelocityY);

/// <summary>Marks an entity as a ladder volume.</summary>
public record struct Ladder(float Top, float Bottom);

/// <summary>Marks the player as currently dropping through a one-way platform.</summary>
public record struct DroppingThrough(float Timer);

/// <summary>Tracks invincibility frames (used during dash, damage, etc.).</summary>
public record struct Invincible(float Timer);
```

---

## 3 — Ground Detection

Ground detection is the foundation everything else builds on. Get this wrong and jumping, slopes, and platforms all break.

### Multi-Ray Approach

Cast multiple rays downward from the bottom of the collider. A single center ray misses edges; three or more rays catch them.

```csharp
public static class GroundDetection
{
    /// <summary>Skin width — rays start slightly inside the collider to avoid surface-flush misses.</summary>
    public const float SkinWidth = 2f;

    /// <summary>How far below the collider to cast (just beyond the skin).</summary>
    public const float GroundCheckDistance = SkinWidth + 1f;

    /// <summary>Number of rays spread across the collider bottom.</summary>
    public const int RayCount = 3;

    /// <summary>
    /// Performs ground detection by casting rays downward from the entity's feet.
    /// Returns true if any ray hits, and outputs the best (shortest) hit normal.
    /// </summary>
    public static bool Check(
        in Position pos,
        in ColliderBox col,
        ReadOnlySpan<RectF> solids,
        out float normalX,
        out float normalY,
        out float groundY,
        out int hitSolidIndex)
    {
        normalX = 0f;
        normalY = -1f;
        groundY = 0f;
        hitSolidIndex = -1;

        float left   = col.Left(pos.X) + SkinWidth;
        float right  = col.Right(pos.X) - SkinWidth;
        float bottom = col.Bottom(pos.Y) - SkinWidth; // Start inside

        float shortest = float.MaxValue;
        bool  anyHit   = false;

        for (int i = 0; i < RayCount; i++)
        {
            float t     = RayCount == 1 ? 0.5f : (float)i / (RayCount - 1);
            float rayX  = MathHelper.Lerp(left, right, t);
            float rayY1 = bottom;
            float rayY2 = bottom + GroundCheckDistance;

            for (int s = 0; s < solids.Length; s++)
            {
                ref readonly var solid = ref solids[s];

                // Only consider solids whose horizontal span overlaps the ray X
                if (rayX < solid.Left || rayX > solid.Right) continue;

                // The top of this solid is a potential ground surface
                float surfaceY = solid.Top;

                // Is the surface within our ray range?
                if (surfaceY >= rayY1 && surfaceY <= rayY2)
                {
                    float dist = surfaceY - rayY1;
                    if (dist < shortest)
                    {
                        shortest      = dist;
                        groundY       = surfaceY;
                        hitSolidIndex = s;
                        anyHit        = true;

                        // For a flat AABB, normal is always (0, -1)
                        // Slope normals would come from tile metadata or edge detection
                        normalX = 0f;
                        normalY = -1f;
                    }
                }
            }
        }

        return anyHit;
    }
}
```

### IsGrounded State Management

Don't just flip `IsGrounded` on/off raw — track transitions for coyote time and landing events:

```csharp
// Inside the ground detection system each frame:
bool wasGrounded = grounded.IsGrounded;
grounded.IsGrounded = GroundDetection.Check(
    in pos, in col, solids,
    out grounded.NormalX, out grounded.NormalY,
    out float groundY, out int hitIdx);

// Just landed
if (grounded.IsGrounded && !wasGrounded)
{
    controller.JumpsRemaining = controller.MaxJumps;
    controller.CoyoteTimer    = controller.CoyoteTime;
    // Snap to ground surface
    pos.Y = groundY - col.Height - col.OffsetY;
}

// Just left ground (without jumping)
if (!grounded.IsGrounded && wasGrounded)
{
    controller.CoyoteTimer = controller.CoyoteTime;
    // Don't reset jumps here — coyote time preserves the "first" jump
}

controller.WasGrounded = wasGrounded;
```

---

## 4 — Basic Movement

### Horizontal Acceleration Model

Instant velocity changes feel robotic. Acceleration/deceleration curves add weight and responsiveness.

```csharp
public static class HorizontalMovement
{
    public static void Apply(
        ref Velocity vel,
        ref PlayerController ctrl,
        float inputX,   // -1, 0, or 1
        float dt,
        bool isGrounded)
    {
        // Pick accel/decel based on airborne state
        float accel = isGrounded ? ctrl.GroundAcceleration : ctrl.AirAcceleration;
        float decel = isGrounded ? ctrl.GroundDeceleration : ctrl.AirDeceleration;

        if (Math.Abs(inputX) > 0.01f)
        {
            // Update facing
            ctrl.FacingDirection = inputX > 0 ? 1 : -1;

            // Turning? Apply turn multiplier for snappier direction changes
            bool turning = (vel.X > 0 && inputX < 0) || (vel.X < 0 && inputX > 0);
            float effectiveAccel = turning ? accel * ctrl.TurnMultiplier : accel;

            // Accelerate toward target speed
            float target = inputX * ctrl.MoveSpeed;
            vel = vel with { X = MoveToward(vel.X, target, effectiveAccel * dt) };
        }
        else
        {
            // Decelerate to zero
            vel = vel with { X = MoveToward(vel.X, 0f, decel * dt) };
        }
    }

    private static float MoveToward(float current, float target, float maxDelta)
    {
        if (Math.Abs(target - current) <= maxDelta)
            return target;
        return current + Math.Sign(target - current) * maxDelta;
    }
}
```

### Why Separate Air/Ground Values?

| Parameter | Ground | Air | Effect |
|-----------|--------|-----|--------|
| Acceleration | High (1800) | Lower (1200) | Committed air trajectory, slight control |
| Deceleration | High (2400) | Low (600) | Crisp ground stops, floaty air momentum |

This creates the feel of *commitment* — once airborne, you can adjust but not instantly reverse. Ground movement is responsive and tight.

---

## 5 — Jump System

The jump system is where game feel lives or dies. Every sub-system here exists because a raw `velocity.Y = -jumpSpeed` feels terrible.

### Deriving Gravity from Designer Values

Instead of tuning raw gravity and velocity numbers, express jumps as:
- **Jump height** — how high in pixels
- **Time to apex** — how long in seconds to reach peak

```
gravity      = 2 * jumpHeight / timeToApex²
jumpVelocity = 2 * jumpHeight / timeToApex
```

This lets designers say "I want a 72px jump that takes 0.35s to peak" and get exact values. Change either input and the other adjusts to keep the curve feeling right.

### Variable-Height Jump

When the player releases the jump button early, multiply gravity to cut the jump short:

```csharp
public static class JumpSystem
{
    public static void ApplyGravity(
        ref Velocity vel,
        ref PlayerController ctrl,
        bool jumpHeld,
        float dt)
    {
        if (ctrl.IsDashing) return; // No gravity during dash

        float gravity = ctrl.Gravity;

        if (vel.Y > 0) // Falling (positive Y = downward in screen space)
        {
            // Heavier gravity on the way down
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

    public static bool TryJump(
        ref Velocity vel,
        ref PlayerController ctrl,
        ref Grounded grounded,
        bool jumpPressed)
    {
        if (!jumpPressed) return false;

        bool canJump = grounded.IsGrounded
                    || ctrl.CoyoteTimer > 0f
                    || ctrl.JumpsRemaining > 0;

        if (!canJump) return false;

        // If this is a coyote jump (not grounded but timer active), consume it
        if (!grounded.IsGrounded && ctrl.CoyoteTimer > 0f)
        {
            ctrl.CoyoteTimer = 0f;
        }

        vel = vel with { Y = -ctrl.JumpVelocity }; // Negative Y = upward
        ctrl.JumpsRemaining--;
        grounded.IsGrounded = false;

        return true;
    }
}
```

### Apex Float

When the player is near the peak of their jump (velocity close to zero) and holding the jump button, reduce gravity. This creates a brief hang time that:
- Makes precision platforming more forgiving.
- Feels satisfying — the character "hangs" at the top.
- Gives the player more air-control time.

The `ApexThreshold` value (e.g., 40 px/s) defines the velocity window where this kicks in. The `ApexGravityMultiplier` (e.g., 0.5) controls how much float you get.

### Multi-Jump (Double/Triple Jump)

Track `JumpsRemaining`. Reset to `MaxJumps` on landing. Each jump press decrements. The first jump might be a "coyote jump" (off the ground), so handle it:

```csharp
// On landing:
ctrl.JumpsRemaining = ctrl.MaxJumps;

// For double jump (MaxJumps = 2):
//   First jump:  grounded or coyote → doesn't consume from JumpsRemaining initially
//   Second jump: airborne, JumpsRemaining > 0 → consumes one
```

> ⚠️ **Gotcha:** If you reset `JumpsRemaining` to `MaxJumps` on landing but the initial ground jump also decrements it, the player effectively gets `MaxJumps` air jumps. For a true double jump, set `MaxJumps = 2` and consume one on each jump including the first.

---

## 6 — Coyote Time

Named after Wile E. Coyote running off a cliff and not falling until he looks down.

### What It Does

After walking off a ledge (not jumping), the player has a brief grace period where pressing jump still works. Without this, players feel like the game "ate" their input because they pressed jump 1-2 frames after leaving the edge.

### Implementation

```csharp
// In the ground detection / state update:
if (!grounded.IsGrounded && ctrl.WasGrounded && vel.Y >= 0)
{
    // Just walked off a ledge (didn't jump — velocity is downward or zero)
    ctrl.CoyoteTimer = ctrl.CoyoteTime;
}

// Tick down every frame when airborne
if (!grounded.IsGrounded)
{
    ctrl.CoyoteTimer -= dt;
}

// In the jump check, coyote time counts as "grounded":
bool canJump = grounded.IsGrounded || ctrl.CoyoteTimer > 0f || ctrl.JumpsRemaining > 0;
```

### Typical Values

| Frames (60fps) | Time (seconds) | Feel |
|-----------------|----------------|------|
| 3–4 | 0.050–0.066 | Barely noticeable, tight |
| 5–7 | 0.083–0.116 | Standard — feels fair |
| 8–10 | 0.133–0.166 | Generous — very forgiving |

Most shipped games use 5–8 frames (0.083–0.133s). Celeste uses ~5 frames.

---

## 7 — Jump Buffering

### What It Does

If the player presses jump while airborne (a few frames before landing), the jump executes the instant they touch ground. Without this, fast players who press jump 2 frames before landing get nothing.

### Implementation

```csharp
// When jump is pressed (regardless of grounded state):
if (jumpPressed)
{
    ctrl.JumpBufferTimer = ctrl.JumpBufferTime;
}

// Tick down
ctrl.JumpBufferTimer -= dt;

// On landing, check buffer:
if (grounded.IsGrounded && ctrl.JumpBufferTimer > 0f)
{
    // Execute the buffered jump
    vel = vel with { Y = -ctrl.JumpVelocity };
    ctrl.JumpBufferTimer = 0f;
    ctrl.JumpsRemaining = ctrl.MaxJumps - 1;
    grounded.IsGrounded = false; // We immediately leave the ground
}
```

### Timer vs Ring Buffer

A **timer** is simpler and works for single jump buffering. A **ring buffer** of recent inputs (storing the last N frames of button presses) is more flexible if you need to buffer multiple input types (jump, dash, attack). For most platformers, a timer is sufficient.

### Typical Values

| Frames (60fps) | Time (seconds) | Feel |
|-----------------|----------------|------|
| 4–5 | 0.066–0.083 | Tight — skilled players only |
| 6–8 | 0.100–0.133 | Standard — feels responsive |
| 9–12 | 0.150–0.200 | Very generous |

> 💡 **Coyote time + jump buffering together** is what makes a platformer feel "tight but fair." They solve opposite problems: coyote time handles "jumped too late" and buffering handles "jumped too early."

---

## 8 — Wall Mechanics

### Wall Detection

Mirror the ground-detection approach but cast horizontally:

```csharp
public static class WallDetection
{
    public const float SkinWidth = 2f;
    public const float WallCheckDistance = SkinWidth + 1f;
    public const int RayCount = 3;

    /// <summary>
    /// Check for a wall in the given direction (1 = right, -1 = left).
    /// Casts rays from the side of the collider.
    /// </summary>
    public static bool Check(
        in Position pos,
        in ColliderBox col,
        ReadOnlySpan<RectF> solids,
        int direction,
        out int hitSolidIndex)
    {
        hitSolidIndex = -1;

        float sideX = direction > 0
            ? col.Right(pos.X) - SkinWidth
            : col.Left(pos.X) + SkinWidth;

        float top    = col.Top(pos.Y) + SkinWidth;
        float bottom = col.Bottom(pos.Y) - SkinWidth;

        for (int i = 0; i < RayCount; i++)
        {
            float t    = RayCount == 1 ? 0.5f : (float)i / (RayCount - 1);
            float rayY = MathHelper.Lerp(top, bottom, t);
            float rayEnd = sideX + direction * WallCheckDistance;

            for (int s = 0; s < solids.Length; s++)
            {
                ref readonly var solid = ref solids[s];
                if (rayY < solid.Top || rayY > solid.Bottom) continue;

                bool hit = direction > 0
                    ? (solid.Left >= sideX && solid.Left <= rayEnd)
                    : (solid.Right <= sideX && solid.Right >= rayEnd);

                if (hit)
                {
                    hitSolidIndex = s;
                    return true;
                }
            }
        }

        return false;
    }
}
```

### Wall Slide

When the player is against a wall, falling, and holding the direction into the wall, cap fall speed:

```csharp
// In the movement system, after gravity:
if (ctrl.IsOnWall && vel.Y > 0 && inputX == ctrl.WallDirection)
{
    vel = vel with { Y = Math.Min(vel.Y, ctrl.WallSlideSpeed) };
}
```

### Wall Jump

A wall jump pushes the player **away** from the wall and **upward**:

```csharp
if (jumpPressed && ctrl.IsOnWall && !grounded.IsGrounded)
{
    vel = new Velocity(
        X: -ctrl.WallDirection * ctrl.WallJumpHVelocity,
        Y: -ctrl.WallJumpVVelocity
    );
    ctrl.IsOnWall = false;
    ctrl.CoyoteTimer = 0f;
    ctrl.FacingDirection = -ctrl.WallDirection;
}
```

> 🎮 **Design choice:** Some games (Celeste) let you wall jump without holding toward the wall. Others (Mega Man X) require it. The "no-hold" approach is more forgiving. Implement by checking `IsOnWall` rather than current input direction.

### Wall Cling

Optional mechanic: the player sticks to the wall for a brief period before sliding. Use a timer:

```csharp
if (ctrl.IsOnWall && !grounded.IsGrounded)
{
    if (ctrl.WallClingTimer > 0f)
    {
        vel = vel with { Y = 0f }; // Frozen on wall
        ctrl.WallClingTimer -= dt;
    }
    else
    {
        // Transition to wall slide
        vel = vel with { Y = Math.Min(vel.Y, ctrl.WallSlideSpeed) };
    }
}

// Reset cling timer when freshly touching a wall
if (ctrl.IsOnWall && !wasOnWall)
{
    ctrl.WallClingTimer = ctrl.WallClingTime;
}
```

---

## 9 — Slopes

Slopes are where many controllers fall apart. Without special handling, the player bounces down slopes, jitters on transitions, or slides on surfaces that should be walkable.

### Ground Normal and Slope Angle

The ground normal tells you the slope angle. For tile-based games, store normals per tile edge or compute from tile geometry:

```csharp
/// <summary>Get the slope angle in degrees from a surface normal.</summary>
public static float SlopeAngle(float normalX, float normalY)
{
    // Dot product with up vector (0, -1) in screen coords
    // Angle = acos(dot(normal, up))
    float dot = -normalY; // dot((nx,ny), (0,-1)) = -ny
    return MathF.Acos(Math.Clamp(dot, -1f, 1f)) * (180f / MathF.PI);
}
```

### Slope Movement

```csharp
public static class SlopeHandler
{
    /// <summary>Maximum climbable slope in degrees.</summary>
    public const float MaxSlopeAngle = 50f;

    /// <summary>
    /// Adjusts horizontal velocity for slope traversal.
    /// Projects the movement vector onto the slope surface.
    /// </summary>
    public static void AdjustForSlope(
        ref Velocity vel,
        in Grounded grounded,
        float dt)
    {
        if (!grounded.IsGrounded) return;

        float angle = SlopeAngle(grounded.NormalX, grounded.NormalY);
        if (angle < 0.5f) return; // Flat ground, no adjustment

        if (angle > MaxSlopeAngle)
        {
            // Too steep — slide down
            vel = vel with { X = vel.X + grounded.NormalX * 400f * dt };
            return;
        }

        // Project horizontal movement onto slope surface
        // Slope tangent is perpendicular to normal
        float tangentX = -grounded.NormalY;
        float tangentY = grounded.NormalX;

        // If moving right on a right-facing slope, tangent needs sign correction
        if (vel.X < 0)
        {
            tangentX = -tangentX;
            tangentY = -tangentY;
        }

        float speed = Math.Abs(vel.X);
        vel = new Velocity(tangentX * speed, tangentY * speed);
    }

    /// <summary>
    /// Snap the player to the ground when walking down slopes.
    /// Without this, the player "bounces" off slopes when descending.
    /// </summary>
    public static void SnapToSlope(
        ref Position pos,
        in ColliderBox col,
        ReadOnlySpan<RectF> solids,
        in Grounded grounded,
        float maxSnapDistance = 8f)
    {
        if (!grounded.IsGrounded) return;

        // Cast a ray further down than normal ground check
        if (GroundDetection.Check(in pos, in col, solids,
            out _, out _, out float groundY, out _))
        {
            float feetY = col.Bottom(pos.Y);
            float gap = groundY - feetY;

            if (gap > 0 && gap <= maxSnapDistance)
            {
                pos.Y += gap;
            }
        }
    }
}
```

### Key Slope Problems & Solutions

| Problem | Solution |
|---------|----------|
| Bouncing when running down slopes | Snap to ground on descent (see `SnapToSlope`) |
| Sliding on gentle slopes when idle | Zero out velocity on slopes below max angle when no input |
| Jittering at slope transitions | Increase snap distance, use multiple ground rays |
| Wrong speed on slopes | Project velocity onto slope tangent vector |
| Can walk up walls | Enforce `MaxSlopeAngle` check |

---

## 10 — One-Way Platforms

Platforms the player can jump through from below but stand on from above. Essential for most platformers.

### Detection Logic

Only collide when:
1. The player is **moving downward** (velocity.Y ≥ 0).
2. The player's **feet were above** the platform top on the previous frame.

```csharp
public static class OneWayPlatformCheck
{
    /// <summary>
    /// Determines if a one-way platform collision should be applied.
    /// </summary>
    public static bool ShouldCollide(
        in Position pos,
        in Position prevPos,
        in ColliderBox col,
        in RectF platform,
        float velocityY)
    {
        // Only collide when falling or stationary vertically
        if (velocityY < 0) return false;

        // Player's feet must have been at or above the platform top last frame
        float prevFeet = col.Bottom(prevPos.Y);
        if (prevFeet > platform.Top + 1f) return false;

        // Current feet are at or below the platform top
        float currFeet = col.Bottom(pos.Y);
        if (currFeet >= platform.Top) return true;

        return false;
    }
}
```

### Drop-Through

When the player presses down + jump (or just down), temporarily ignore one-way platforms:

```csharp
// When down+jump pressed on a one-way platform:
if (inputDown && jumpPressed && grounded.IsGrounded && grounded.PlatformEntity.HasValue)
{
    // Check if standing on a one-way platform
    if (world.Has<OneWayPlatform>(grounded.PlatformEntity.Value))
    {
        // Add a brief ignore timer
        world.Add(entity, new DroppingThrough(Timer: 0.15f));
        grounded.IsGrounded = false;
        pos.Y += 2f; // Nudge below the platform surface
    }
}

// In collision resolution, skip one-way platforms while DroppingThrough is active:
if (world.Has<DroppingThrough>(entity))
{
    ref var drop = ref world.Get<DroppingThrough>(entity);
    drop.Timer -= dt;
    if (drop.Timer <= 0f)
        world.Remove<DroppingThrough>(entity);
    // Skip one-way collision this frame
}
```

---

## 11 — Moving Platforms

### The Core Problem

When a platform moves, the player standing on it should move with it. There are two approaches:

| Approach | Pros | Cons |
|----------|------|------|
| **Velocity inheritance** | Simple, no coupling | Rounding errors, can drift |
| **Position parenting** | Exact tracking | Must handle attach/detach, rotation is complex |

### Velocity Transfer Approach

```csharp
public static class MovingPlatformSystem
{
    public static void Update(World world, float dt)
    {
        // First, update all platform positions
        var platformQuery = new QueryDescription().WithAll<Position, MovingPlatform, ColliderBox>();
        world.Query(in platformQuery, (ref Position pos, ref MovingPlatform mp) =>
        {
            pos.X += mp.VelocityX * dt;
            pos.Y += mp.VelocityY * dt;
        });

        // Then, apply platform velocity to any entity standing on one
        var riderQuery = new QueryDescription().WithAll<Position, Velocity, Grounded>();
        world.Query(in riderQuery, (ref Position pos, ref Velocity vel, ref Grounded grounded) =>
        {
            if (!grounded.IsGrounded || !grounded.PlatformEntity.HasValue)
                return;

            if (!world.Has<MovingPlatform>(grounded.PlatformEntity.Value))
                return;

            ref var mp = ref world.Get<MovingPlatform>(grounded.PlatformEntity.Value);

            // Add platform velocity to rider
            pos.X += mp.VelocityX * dt;
            pos.Y += mp.VelocityY * dt;
        });
    }
}
```

### Position Parenting Approach

For exact tracking (no drift), store the player's **local offset** from the platform center when they land, then reconstruct world position each frame:

```csharp
public record struct PlatformRider(Entity Platform, float LocalOffsetX, float LocalOffsetY);

// On landing on a moving platform:
var platformPos = world.Get<Position>(platformEntity);
world.Add(entity, new PlatformRider(
    Platform: platformEntity,
    LocalOffsetX: pos.X - platformPos.X,
    LocalOffsetY: pos.Y - platformPos.Y
));

// Each frame, before player movement:
if (world.Has<PlatformRider>(entity))
{
    ref var rider = ref world.Get<PlatformRider>(entity);
    var platformPos = world.Get<Position>(rider.Platform);
    pos.X = platformPos.X + rider.LocalOffsetX;
    pos.Y = platformPos.Y + rider.LocalOffsetY;
}

// On leaving the platform, remove PlatformRider and inherit velocity for momentum
```

### Attach / Detach

- **Attach** when ground detection identifies a moving platform entity.
- **Detach** when no longer grounded OR ground entity changes OR player jumps.
- On detach, optionally inherit the platform's velocity for momentum.

---

## 12 — Ladders & Climbing

### Ladder State Machine

Ladders override normal movement with vertical climbing. The player enters a distinct state:

```csharp
public static class LadderSystem
{
    public const float ClimbSpeed = 120f;

    public static void Update(
        ref Position pos,
        ref Velocity vel,
        ref PlayerController ctrl,
        ref Grounded grounded,
        in Ladder ladder,
        float inputX,
        float inputY,
        bool jumpPressed,
        float dt)
    {
        if (!ctrl.IsOnLadder)
        {
            // Check for ladder entry: player overlaps ladder and presses up/down
            if (Math.Abs(inputY) > 0.1f)
            {
                ctrl.IsOnLadder = true;
                vel = new Velocity(0f, 0f);
                grounded.IsGrounded = false;

                // Center player horizontally on ladder
                // (assumes ladder has a center X stored or computed)
            }
            return;
        }

        // ── On Ladder ──

        // Vertical movement
        vel = vel with { Y = -inputY * ClimbSpeed }; // Up = negative Y

        // Clamp to ladder bounds
        float feetY = pos.Y; // Adjust based on your collider offset
        if (feetY <= ladder.Top)
        {
            pos.Y = ladder.Top;
            ctrl.IsOnLadder = false; // Reached top — exit
        }
        else if (feetY >= ladder.Bottom)
        {
            pos.Y = ladder.Bottom;
            ctrl.IsOnLadder = false; // Reached bottom — exit
        }

        // Allow horizontal influence (slight left/right while climbing)
        vel = vel with { X = inputX * ClimbSpeed * 0.3f };

        // Jump off ladder
        if (jumpPressed)
        {
            ctrl.IsOnLadder = false;
            vel = new Velocity(inputX * ctrl.MoveSpeed * 0.5f, -ctrl.JumpVelocity * 0.7f);
        }

        // No gravity while on ladder
    }
}
```

### Ladder Design Considerations

- **Snap to ladder center** on entry for clean visuals.
- **Disable gravity** while climbing.
- **Allow jump-off** with directional input for fluid movement.
- **Animate** based on `vel.Y` — idle when stationary on ladder, climb up/down otherwise.
- **Exit at top/bottom** — at the top, some games play a "climb over" animation.

---

## 13 — Dash / Dodge

### Core Dash Implementation

```csharp
public static class DashSystem
{
    public static void TryDash(
        ref Velocity vel,
        ref PlayerController ctrl,
        bool dashPressed,
        float inputX,
        float inputY)
    {
        if (!dashPressed) return;
        if (ctrl.IsDashing) return;
        if (ctrl.DashCooldownTimer > 0f) return;

        ctrl.IsDashing = true;
        ctrl.DashTimer = ctrl.DashDuration;
        ctrl.DashCooldownTimer = ctrl.DashCooldown;

        // Determine dash direction
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

    public static void UpdateDash(
        ref Velocity vel,
        ref PlayerController ctrl,
        float dt)
    {
        // Tick cooldown
        if (ctrl.DashCooldownTimer > 0f)
            ctrl.DashCooldownTimer -= dt;

        if (!ctrl.IsDashing) return;

        ctrl.DashTimer -= dt;

        if (ctrl.DashTimer <= 0f)
        {
            ctrl.IsDashing = false;
            // Kill velocity or keep momentum — design choice
            vel = new Velocity(vel.X * 0.3f, 0f); // Bleed off most speed
        }

        // During dash: no gravity (handled in gravity system via IsDashing check)
    }
}
```

### Invincibility Frames

During a dash, grant a brief invincibility window:

```csharp
// When dash starts:
world.Add(entity, new Invincible(Timer: ctrl.DashDuration));

// In damage system:
if (world.Has<Invincible>(entity))
{
    ref var inv = ref world.Get<Invincible>(entity);
    inv.Timer -= dt;
    if (inv.Timer <= 0f)
        world.Remove<Invincible>(entity);
    return; // Skip damage
}
```

### Ghost Trail Effect

Spawn fading afterimages at the player's position during the dash:

```csharp
public record struct GhostTrail(float Alpha, float Lifetime);

// During dash, every N frames:
if (ctrl.IsDashing && frameCount % 3 == 0)
{
    var ghost = world.Create();
    world.Add(ghost, new Position(pos.X, pos.Y));
    world.Add(ghost, new GhostTrail(Alpha: 0.6f, Lifetime: 0.2f));
    world.Add(ghost, sprite); // Copy current sprite/frame
}

// Ghost trail system: fade and destroy
world.Query(in ghostQuery, (Entity e, ref GhostTrail trail) =>
{
    trail.Alpha -= dt / trail.Lifetime;
    if (trail.Alpha <= 0f)
        world.Destroy(e);
});
```

---

## 14 — Corner Correction

### The Problem

The player jumps, and the top-left corner of their collider clips a block by 2 pixels. Without correction, the jump is killed and the player falls. This is infuriating.

### The Fix

When a vertical collision is detected at the top of the player, check if nudging them left or right by a few pixels would clear the obstruction:

```csharp
public static class CornerCorrection
{
    /// <summary>Max pixels to nudge horizontally for corner correction.</summary>
    public const float MaxCorrection = 6f;

    /// <summary>Step size for checking offsets.</summary>
    public const float Step = 1f;

    /// <summary>
    /// Attempts to nudge the player horizontally to clear a ceiling/corner clip.
    /// Returns true if correction was applied.
    /// </summary>
    public static bool TryCorrect(
        ref Position pos,
        in ColliderBox col,
        in Velocity vel,
        ReadOnlySpan<RectF> solids)
    {
        // Only correct when moving upward (hitting a ceiling/corner)
        if (vel.Y >= 0) return false;

        // Check if there's a collision at the current position
        if (!HasCeilingCollision(pos, col, solids)) return false;

        // Try nudging left and right
        for (float offset = Step; offset <= MaxCorrection; offset += Step)
        {
            // Try right
            var testPos = new Position(pos.X + offset, pos.Y);
            if (!HasCeilingCollision(testPos, col, solids))
            {
                pos.X += offset;
                return true;
            }

            // Try left
            testPos = new Position(pos.X - offset, pos.Y);
            if (!HasCeilingCollision(testPos, col, solids))
            {
                pos.X -= offset;
                return true;
            }
        }

        return false; // No valid correction found
    }

    private static bool HasCeilingCollision(
        in Position pos,
        in ColliderBox col,
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
}
```

> 🎯 **Celeste** uses up to 4px of corner correction. Some games go as high as 8px. More correction = more forgiving, but too much and the player warps noticeably.

---

## 15 — Collision Resolution

### AABB Sweep Test

Move the collider incrementally and resolve overlaps axis by axis. This prevents tunneling and gives correct slide behavior.

```csharp
public static class CollisionResolver
{
    /// <summary>
    /// Moves an entity by the given velocity, resolving collisions against solid geometry.
    /// Uses axis-separated sweep: move X first, resolve, then move Y, resolve.
    /// </summary>
    public static void MoveAndCollide(
        ref Position pos,
        ref Velocity vel,
        in ColliderBox col,
        ReadOnlySpan<RectF> solids,
        float dt)
    {
        // ── Sub-pixel accumulation ──
        float moveX = vel.X * dt + pos.RemainderX;
        float moveY = vel.Y * dt + pos.RemainderY;

        int pixelsX = (int)MathF.Truncate(moveX);
        int pixelsY = (int)MathF.Truncate(moveY);

        pos.RemainderX = moveX - pixelsX;
        pos.RemainderY = moveY - pixelsY;

        // ── Move X ──
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
                // Hit floor or ceiling
                vel = vel with { Y = 0f };
                pos.RemainderY = 0f;
                break;
            }
        }
    }

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
}
```

### Sub-Pixel Accumulation

At low speeds, a character might move 0.3 pixels per frame. Without sub-pixel accumulation, this rounds to 0 and the character never moves. By storing the fractional remainder and adding it next frame, movement is smooth at any speed.

### Why Axis-Separated?

Moving X and Y simultaneously creates ambiguous corner cases — did we hit the wall or the floor? By resolving one axis at a time, collision response is always unambiguous:

1. Move along X → if blocked, zero X velocity
2. Move along Y → if blocked, zero Y velocity

The order (X-first or Y-first) can matter on slopes. X-first is standard for horizontal platformers.

---

## 16 — Complete ECS System

Here's the full `PlayerControllerSystem` integrating every mechanic into a single update loop.

```csharp
using Arch.Core;
using Arch.Core.Extensions;
using Microsoft.Xna.Framework;

/// <summary>
/// Complete 2D platformer character controller as an Arch ECS system.
/// Update order: Input → Dash → Horizontal → Gravity → Jump → Wall → Slope →
///               Collision → Corner Correction → Ground Detection → Platform Sync
/// </summary>
public class PlayerControllerSystem
{
    private readonly QueryDescription _playerQuery = new QueryDescription()
        .WithAll<Position, Velocity, PlayerController, ColliderBox, Grounded>();

    /// <summary>Solid geometry gathered each frame from the tilemap or collidable entities.</summary>
    private RectF[] _solids = Array.Empty<RectF>();
    private int _solidCount;

    /// <summary>
    /// Call once per frame before Update() to provide current solid geometry.
    /// </summary>
    public void SetSolids(RectF[] solids, int count)
    {
        _solids = solids;
        _solidCount = count;
    }

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
            // Store previous position for one-way platform checks
            var prevPos = pos;

            // ═══════════════════════════════════════════
            //  1. LADDER CHECK
            // ═══════════════════════════════════════════
            if (ctrl.IsOnLadder)
            {
                // Ladder movement is handled by LadderSystem separately
                // Skip normal movement pipeline
                return;
            }

            // ═══════════════════════════════════════════
            //  2. DASH
            // ═══════════════════════════════════════════
            DashSystem.TryDash(ref vel, ref ctrl, input.DashPressed, input.X, input.Y);
            DashSystem.UpdateDash(ref vel, ref ctrl, dt);

            if (ctrl.IsDashing)
            {
                // During dash: skip gravity and normal movement, just collide
                CollisionResolver.MoveAndCollide(ref pos, ref vel, in col, solids, dt);
                return;
            }

            // ═══════════════════════════════════════════
            //  3. HORIZONTAL MOVEMENT
            // ═══════════════════════════════════════════
            HorizontalMovement.Apply(ref vel, ref ctrl, input.X, dt, grounded.IsGrounded);

            // ═══════════════════════════════════════════
            //  4. GRAVITY
            // ═══════════════════════════════════════════
            JumpSystem.ApplyGravity(ref vel, ref ctrl, input.JumpHeld, dt);

            // ═══════════════════════════════════════════
            //  5. WALL DETECTION
            // ═══════════════════════════════════════════
            bool wasOnWall = ctrl.IsOnWall;
            ctrl.IsOnWall = false;

            if (!grounded.IsGrounded)
            {
                // Check wall in facing direction
                if (WallDetection.Check(in pos, in col, solids, ctrl.FacingDirection, out _))
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
            //  6. JUMP (including wall jump, coyote, buffer)
            // ═══════════════════════════════════════════

            // Tick coyote timer
            if (!grounded.IsGrounded)
                ctrl.CoyoteTimer = Math.Max(ctrl.CoyoteTimer - dt, 0f);

            // Buffer jump input
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
            else if (JumpSystem.TryJump(ref vel, ref ctrl, ref grounded, wantsJump))
            {
                ctrl.JumpBufferTimer = 0f;
            }

            // ═══════════════════════════════════════════
            //  7. SLOPE ADJUSTMENT
            // ═══════════════════════════════════════════
            SlopeHandler.AdjustForSlope(ref vel, in grounded, dt);

            // ═══════════════════════════════════════════
            //  8. COLLISION RESOLUTION
            // ═══════════════════════════════════════════
            CollisionResolver.MoveAndCollide(ref pos, ref vel, in col, solids, dt);

            // ═══════════════════════════════════════════
            //  9. CORNER CORRECTION
            // ═══════════════════════════════════════════
            if (vel.Y < 0) // Only when moving upward
            {
                CornerCorrection.TryCorrect(ref pos, in col, in vel, solids);
            }

            // ═══════════════════════════════════════════
            // 10. GROUND DETECTION
            // ═══════════════════════════════════════════
            bool wasGrounded = grounded.IsGrounded;

            grounded.IsGrounded = GroundDetection.Check(
                in pos, in col, solids,
                out grounded.NormalX, out grounded.NormalY,
                out float groundY, out int hitIdx);

            // Handle one-way platforms
            if (world.Has<DroppingThrough>(entity))
            {
                ref var drop = ref world.Get<DroppingThrough>(entity);
                drop.Timer -= dt;
                if (drop.Timer <= 0f)
                    world.Remove<DroppingThrough>(entity);
                // Don't count one-way platforms as ground while dropping through
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
                    JumpSystem.TryJump(ref vel, ref ctrl, ref grounded, true);
                    ctrl.JumpBufferTimer = 0f;
                }
            }

            // Left ground (without jumping)
            if (!grounded.IsGrounded && wasGrounded && vel.Y >= 0)
            {
                ctrl.CoyoteTimer = ctrl.CoyoteTime;
            }

            // Slope snapping (when grounded and walking downhill)
            if (grounded.IsGrounded && wasGrounded)
            {
                SlopeHandler.SnapToSlope(ref pos, in col, solids, in grounded);
            }

            ctrl.WasGrounded = wasGrounded;

            // ═══════════════════════════════════════════
            // 11. DROP-THROUGH (one-way platforms)
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
}
```

### Input State Helper

```csharp
/// <summary>Snapshot of player input for the current frame.</summary>
public record struct InputState
{
    public float X;           // -1 to 1 horizontal
    public float Y;           // -1 to 1 vertical (up = negative in some schemes)
    public bool JumpPressed;  // True only on the frame jump was pressed
    public bool JumpHeld;     // True while jump button is held
    public bool DashPressed;  // True only on the frame dash was pressed
    public bool DownPressed;  // True while down is held (for drop-through)
}
```

### RectF Helper

```csharp
/// <summary>Float-precision rectangle for collision geometry.</summary>
public record struct RectF(float Left, float Top, float Right, float Bottom)
{
    public float Width  => Right - Left;
    public float Height => Bottom - Top;
}
```

### System Registration

```csharp
// In your Game class or system runner:
var world = World.Create();

var playerControllerSystem = new PlayerControllerSystem();

// Create the player entity
var player = world.Create(
    new Position(100, 100),
    new Velocity(0, 0),
    PlayerController.Default(),
    new ColliderBox(OffsetX: -8, OffsetY: -16, Width: 16, Height: 32),
    new Grounded(false)
);

// Don't forget to derive jump parameters!
ref var ctrl = ref world.Get<PlayerController>(player);
ctrl.DeriveJumpParameters();

// In Update():
protected override void Update(GameTime gameTime)
{
    float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

    // Gather input
    var input = GatherInput();

    // Gather solid geometry from tilemap/level
    var solids = GatherSolids();
    playerControllerSystem.SetSolids(solids, solidCount);

    // Run the system
    playerControllerSystem.Update(world, input, dt);
}
```

---

## 17 — Tuning Reference Table

### Parameter Ranges by Game Feel

| Parameter | Tight / Responsive | Balanced | Floaty / Aerial |
|-----------|--------------------|----------|-----------------|
| **MoveSpeed** (px/s) | 160–200 | 200–280 | 120–160 |
| **GroundAccel** (px/s²) | 2000–3000 | 1400–2000 | 800–1400 |
| **GroundDecel** (px/s²) | 2500–4000 | 1800–2500 | 1000–1800 |
| **AirAccel** (px/s²) | 1000–1800 | 800–1200 | 400–800 |
| **AirDecel** (px/s²) | 400–800 | 300–600 | 100–400 |
| **TurnMultiplier** | 2.5–4.0 | 1.5–2.5 | 1.0–1.5 |
| **JumpHeight** (px) | 48–72 | 72–112 | 96–160 |
| **TimeToApex** (s) | 0.25–0.35 | 0.35–0.50 | 0.50–0.80 |
| **FallGravityMult** | 2.0–3.0 | 1.5–2.0 | 1.0–1.5 |
| **MaxFallSpeed** (px/s) | 350–500 | 300–400 | 200–300 |
| **CoyoteTime** (s) | 0.05–0.08 | 0.08–0.12 | 0.12–0.18 |
| **JumpBuffer** (s) | 0.06–0.10 | 0.10–0.15 | 0.15–0.20 |
| **WallSlideSpeed** (px/s) | 80–120 | 50–80 | 30–50 |

### Shipped Game Approximations

> ⚠️ These are **reverse-engineered estimates**, not official values. Pixel values assume 16px = 1 tile.

| Game | MoveSpeed | JumpHeight | TimeToApex | FallMult | Coyote | Buffer | Feel |
|------|-----------|------------|------------|----------|--------|--------|------|
| **Celeste** | ~190 | ~68 | ~0.30 | ~2.5 | ~0.08 | ~0.10 | Tight, precise, fast fall |
| **Hollow Knight** | ~170 | ~80 | ~0.40 | ~2.0 | ~0.10 | ~0.12 | Weighty, deliberate |
| **Dead Cells** | ~250 | ~64 | ~0.28 | ~2.8 | ~0.06 | ~0.08 | Snappy, action-focused |
| **Super Meat Boy** | ~280 | ~56 | ~0.25 | ~3.0 | ~0.05 | ~0.06 | Extreme precision, fast |
| **Ori** | ~200 | ~96 | ~0.45 | ~1.6 | ~0.12 | ~0.13 | Floaty, graceful |
| **Mega Man X** | ~160 | ~72 | ~0.38 | ~2.0 | ~0.00 | ~0.00 | Classic, no assists |
| **Shovel Knight** | ~140 | ~80 | ~0.42 | ~1.8 | ~0.08 | ~0.10 | Retro, moderate |

### Tuning Workflow

1. **Start with `PlayerController.Default()`** — it's a reasonable middle ground.
2. **Set jump height and time-to-apex first** — these define the core feel. Call `DeriveJumpParameters()`.
3. **Adjust fall gravity multiplier** — higher = snappier descent, lower = floatier.
4. **Tune ground accel/decel** — high decel = crisp stops, low decel = slidey (ice level!).
5. **Set air control** — less air accel = more committed jumps, more = more forgiving.
6. **Add coyote time and buffer** — start at 0.1s each, adjust to taste.
7. **Iterate, iterate, iterate** — game feel is subjective. Playtest constantly.

### Quick-Start Presets

```csharp
public static class ControllerPresets
{
    /// <summary>Tight, fast, precise — Celeste / Super Meat Boy style.</summary>
    public static PlayerController Tight() => new()
    {
        MoveSpeed             = 200f,
        GroundAcceleration    = 2800f,
        GroundDeceleration    = 3600f,
        AirAcceleration       = 1600f,
        AirDeceleration       = 600f,
        TurnMultiplier        = 3.0f,
        JumpHeight            = 64f,
        TimeToApex            = 0.28f,
        FallGravityMultiplier = 2.6f,
        MaxFallSpeed          = 450f,
        ApexGravityMultiplier = 0.5f,
        ApexThreshold         = 35f,
        MaxJumps              = 1,
        JumpsRemaining        = 1,
        CoyoteTime            = 0.07f,
        JumpBufferTime        = 0.09f,
        WallSlideSpeed        = 100f,
        WallJumpHVelocity     = 200f,
        WallJumpVVelocity     = 300f,
        WallClingTime          = 0.25f,
        DashSpeed             = 550f,
        DashDuration          = 0.12f,
        DashCooldown          = 0.3f,
        FacingDirection       = 1,
    };

    /// <summary>Floaty, graceful, exploration — Ori style.</summary>
    public static PlayerController Floaty() => new()
    {
        MoveSpeed             = 180f,
        GroundAcceleration    = 1200f,
        GroundDeceleration    = 1600f,
        AirAcceleration       = 900f,
        AirDeceleration       = 300f,
        TurnMultiplier        = 1.4f,
        JumpHeight            = 100f,
        TimeToApex            = 0.50f,
        FallGravityMultiplier = 1.4f,
        MaxFallSpeed          = 280f,
        ApexGravityMultiplier = 0.3f,
        ApexThreshold         = 50f,
        MaxJumps              = 2,
        JumpsRemaining        = 2,
        CoyoteTime            = 0.14f,
        JumpBufferTime        = 0.16f,
        WallSlideSpeed        = 40f,
        WallJumpHVelocity     = 160f,
        WallJumpVVelocity     = 260f,
        WallClingTime          = 0.8f,
        DashSpeed             = 480f,
        DashDuration          = 0.18f,
        DashCooldown          = 0.5f,
        FacingDirection       = 1,
    };

    /// <summary>Heavy, deliberate, combat — Hollow Knight style.</summary>
    public static PlayerController Heavy() => new()
    {
        MoveSpeed             = 160f,
        GroundAcceleration    = 2200f,
        GroundDeceleration    = 2800f,
        AirAcceleration       = 1000f,
        AirDeceleration       = 500f,
        TurnMultiplier        = 2.0f,
        JumpHeight            = 80f,
        TimeToApex            = 0.40f,
        FallGravityMultiplier = 2.0f,
        MaxFallSpeed          = 380f,
        ApexGravityMultiplier = 0.6f,
        ApexThreshold         = 45f,
        MaxJumps              = 1,
        JumpsRemaining        = 1,
        CoyoteTime            = 0.10f,
        JumpBufferTime        = 0.12f,
        WallSlideSpeed        = 70f,
        WallJumpHVelocity     = 170f,
        WallJumpVVelocity     = 270f,
        WallClingTime          = 0.4f,
        DashSpeed             = 500f,
        DashDuration          = 0.14f,
        DashCooldown          = 0.6f,
        FacingDirection       = 1,
    };
}
```

---

## Summary

A platformer character controller is a stack of small lies told to the player so the game *feels* right. None of these mechanics are physically accurate — they're better than that. They're **fun**.

The key systems, from foundation to polish:

| Layer | Systems | Purpose |
|-------|---------|---------|
| **Foundation** | Position, Velocity, Collider, Collision Resolution | Entity exists and can move |
| **Core** | Ground Detection, Horizontal Movement, Gravity, Jump | Playable character |
| **Feel** | Coyote Time, Jump Buffering, Variable Jump Height, Apex Float | "Why does this feel so good?" |
| **Advanced** | Wall Mechanics, Slopes, One-Way Platforms, Moving Platforms | Rich level design vocabulary |
| **Polish** | Corner Correction, Dash, Ladders, Ghost Trails | Completeness and flair |

Build them in that order. Get each layer feeling right before adding the next. And always, always playtest.

---

*Next: [G53 — Side-Scrolling Perspective](./G53_side_scrolling.md) for camera systems that follow your controller.*
