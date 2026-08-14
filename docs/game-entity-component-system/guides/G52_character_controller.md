# G52 — 2D Platformer Character Controller



> **Category:** Guide · **Related:** [G3 Physics & Collision](./G3_physics_and_collision.md) · [C2 Game Feel & Genre Craft](../../core/game-design/C2_game_feel_and_genre_craft.md) · [G30 Game Feel Tooling](./G30_game_feel_tooling.md) · [G7 Input Handling](./G7_input_handling.md) · [G53 Side-Scrolling Perspective](./G53_side_scrolling.md) · [G20 Camera Systems](./G20_camera_systems.md) · [G64 Combat & Damage Systems](./G64_combat_damage_systems.md) · [G31 Animation & State Machines](./G31_animation_state_machines.md) · [G37 Tilemap Systems](./G37_tilemap_systems.md) · [G67 Object Pooling](./G67_object_pooling.md) · [combat-theory](../../core/concepts/combat-theory.md) · [camera-theory](../../core/concepts/camera-theory.md)

---

## Table of Contents

1. [Controller Philosophy](#1--controller-philosophy)
2. [Controller Pipeline Overview](#2--controller-pipeline-overview)
3. [Core Components](#3--core-components)
4. [Ground Detection](#4--ground-detection)
5. [Basic Movement](#5--basic-movement)
6. [Jump System](#6--jump-system)
7. [Coyote Time](#7--coyote-time)
8. [Jump Buffering](#8--jump-buffering)
9. [Wall Mechanics](#9--wall-mechanics)
10. [Slopes](#10--slopes)
11. [One-Way Platforms](#11--one-way-platforms)
12. [Moving Platforms](#12--moving-platforms)
13. [Ladders & Climbing](#13--ladders--climbing)
14. [Dash / Dodge](#14--dash--dodge)
15. [Corner Correction](#15--corner-correction)
16. [Collision Resolution](#16--collision-resolution)
17. [Crouching & Sliding](#17--crouching--sliding)
18. [Swimming & Water Physics](#18--swimming--water-physics)
19. [Top-Down Character Controller](#19--top-down-character-controller)
20. [State Machine Integration](#20--state-machine-integration)
21. [Camera Integration](#21--camera-integration)
22. [Debug Visualization](#22--debug-visualization)
23. [Complete ECS System](#23--complete-ecs-system)
24. [Common Mistakes & Troubleshooting](#24--common-mistakes--troubleshooting)
25. [Tuning Reference Table](#25--tuning-reference-table)

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

## 2 — Controller Pipeline Overview

Understanding execution order is critical. Systems that run in the wrong order cause jitter, missed inputs, or physics desync.

```
Frame Start
│
├─ 1. Input Gathering
│     Read keyboard/gamepad state, apply deadzones, detect press/hold/release
│
├─ 2. Timer Updates
│     Tick coyote timer, jump buffer timer, dash cooldown, invincibility frames
│
├─ 3. State Checks
│     Is the player on a ladder? In water? Dashing? Crouching?
│     → Route to specialized sub-pipeline if needed
│
├─ 4. Horizontal Movement
│     Apply acceleration/deceleration based on input + ground/air state
│     Apply turn multiplier if reversing direction
│
├─ 5. Gravity
│     Apply base gravity, fall multiplier, or apex float
│     Skip if dashing, on ladder, or swimming
│
├─ 6. Wall Detection
│     Cast horizontal rays for wall contact
│     Apply wall slide speed cap, start/tick wall cling timer
│
├─ 7. Jump Resolution
│     Priority: wall jump → coyote jump → ground jump → multi-jump
│     Consume jump buffer if conditions met
│
├─ 8. Slope Adjustment
│     Project velocity onto slope tangent
│     Snap to ground on descent to prevent bouncing
│
├─ 9. Collision Resolution (Axis-Separated Sweep)
│     Move X → resolve → Move Y → resolve
│     Accumulate sub-pixel remainder
│
├─ 10. Corner Correction
│      If ceiling hit while moving up, nudge horizontally to clear
│
├─ 11. Ground Detection
│      Multi-ray downcast, update IsGrounded, detect landing/leaving
│      Reset jumps on landing, start coyote timer on leaving
│
├─ 12. Platform Sync
│      Apply moving platform velocity to rider
│      Handle one-way platform drop-through
│
├─ 13. Camera Feedback
│      Report landing impact, dash direction, wall contact to camera system
│
└─ 14. Animation State Update
      Map controller state → animation: idle, run, jump, fall, wall-slide, dash...
```

### Why This Order Matters

| Order Issue | Symptom | Fix |
|------------|---------|-----|
| Gravity before jump | Jump impulse partially eaten by gravity same frame | Jump sets velocity AFTER gravity |
| Ground detection before collision | Player detected as grounded, then collision pushes them up | Collision first, then ground detect |
| Corner correction after ground detect | Correction nudges player, but ground state is stale | Corner correction before ground detect |
| Wall detect before horizontal move | Wall state uses last frame's position | Detect walls after moving horizontally |
| Camera before movement | Camera reads stale position, causes 1-frame lag | Camera reads after all movement resolves |

---

## 3 — Core Components

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

    // ── Crouch ──
    public float CrouchSpeedMultiplier;  // Speed scale when crouching (0.4–0.6)
    public bool  IsCrouching;
    public float StandingHeight;         // Full collider height
    public float CrouchingHeight;        // Reduced collider height

    // ── State ──
    public int   FacingDirection;        // 1 = right, -1 = left
    public bool  IsOnWall;
    public int   WallDirection;          // 1 = wall to right, -1 = wall to left
    public bool  IsOnLadder;
    public bool  IsInWater;
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

        CrouchSpeedMultiplier = 0.45f,
        StandingHeight        = 32f,
        CrouchingHeight       = 20f,

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

/// <summary>Marks a region as a water volume with buoyancy properties.</summary>
public record struct WaterZone(float SurfaceY, float Drag, float BuoyancyForce);

/// <summary>Tracks landing impact for camera shake and VFX.</summary>
public record struct LandingImpact(float Speed, float Time);
```

---

## 4 — Ground Detection

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
    ctrl.JumpsRemaining = ctrl.MaxJumps;
    ctrl.CoyoteTimer    = ctrl.CoyoteTime;
    // Snap to ground surface
    pos.Y = groundY - col.Height - col.OffsetY;

    // Track impact for camera shake / dust VFX
    float impactSpeed = Math.Abs(vel.Y);
    if (impactSpeed > 200f) // Threshold for "hard landing"
    {
        world.Add(entity, new LandingImpact(impactSpeed, 0f));
    }
}

// Just left ground (without jumping)
if (!grounded.IsGrounded && wasGrounded)
{
    controller.CoyoteTimer = controller.CoyoteTime;
    // Don't reset jumps here — coyote time preserves the "first" jump
}

controller.WasGrounded = wasGrounded;
```

### Edge Detection for Ledge Hang

Some games need to know if the player is at the edge of a platform (for ledge grab animations or preventing accidental falls):

```csharp
public static class EdgeDetection
{
    /// <summary>
    /// Checks if only one side of the character has ground below it.
    /// Returns 0 if no edge, 1 if edge to the right, -1 if edge to the left.
    /// </summary>
    public static int CheckEdge(
        in Position pos,
        in ColliderBox col,
        ReadOnlySpan<RectF> solids)
    {
        float bottom = col.Bottom(pos.Y);
        float checkY = bottom + GroundDetection.GroundCheckDistance;

        // Check left foot
        float leftX = col.Left(pos.X) + GroundDetection.SkinWidth;
        bool leftGround = HasGroundAt(leftX, bottom, checkY, solids);

        // Check right foot
        float rightX = col.Right(pos.X) - GroundDetection.SkinWidth;
        bool rightGround = HasGroundAt(rightX, bottom, checkY, solids);

        if (leftGround && !rightGround) return 1;  // Edge to the right
        if (!leftGround && rightGround) return -1; // Edge to the left
        return 0; // No edge (both have ground or both don't)
    }

    private static bool HasGroundAt(float x, float fromY, float toY, ReadOnlySpan<RectF> solids)
    {
        for (int i = 0; i < solids.Length; i++)
        {
            ref readonly var s = ref solids[i];
            if (x >= s.Left && x <= s.Right && s.Top >= fromY && s.Top <= toY)
                return true;
        }
        return false;
    }
}
```

---

## 5 — Basic Movement

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

        // Apply crouch speed reduction
        float maxSpeed = ctrl.MoveSpeed;
        if (ctrl.IsCrouching)
            maxSpeed *= ctrl.CrouchSpeedMultiplier;

        if (Math.Abs(inputX) > 0.01f)
        {
            // Update facing
            ctrl.FacingDirection = inputX > 0 ? 1 : -1;

            // Turning? Apply turn multiplier for snappier direction changes
            bool turning = (vel.X > 0 && inputX < 0) || (vel.X < 0 && inputX > 0);
            float effectiveAccel = turning ? accel * ctrl.TurnMultiplier : accel;

            // Accelerate toward target speed
            float target = inputX * maxSpeed;
            vel = vel with { X = MoveToward(vel.X, target, effectiveAccel * dt) };
        }
        else
        {
            // Decelerate to zero
            vel = vel with { X = MoveToward(vel.X, 0f, decel * dt) };
        }
    }

    public static float MoveToward(float current, float target, float maxDelta)
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

### Speed Curves: Linear vs Exponential

The `MoveToward` approach gives **linear acceleration** — constant rate of speed change. Some games prefer **exponential smoothing** for a different feel:

```csharp
// Linear (constant acceleration) — Celeste, Dead Cells
vel.X = MoveToward(vel.X, target, accel * dt);

// Exponential (asymptotic approach) — some Metroidvanias
// Reaches ~63% of target in 'smoothTime' seconds
vel.X = vel.X + (target - vel.X) * (1f - MathF.Exp(-dt / smoothTime));

// Instant (no acceleration) — retro NES-style
vel.X = inputX * maxSpeed;
```

| Style | Feel | Used By |
|-------|------|---------|
| Linear | Predictable, easy to tune | Celeste, Dead Cells, Hollow Knight |
| Exponential | Smooth start, crisp stop | Some Metroidvanias |
| Instant | Snappy, retro | Classic Mega Man, old-school NES |

---

## 6 — Jump System

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

### Gravity Zones Diagram

```
                Jump Pressed
                    │
    ┌───────────────┼───────────────┐
    │               ▼               │
    │   ╔═══════════════════════╗   │
    │   ║   RISING (vel.Y < 0)  ║   │
    │   ║                       ║   │
    │   ║  Jump held:           ║   │
    │   ║    gravity = base     ║   │
    │   ║                       ║   │
    │   ║  Jump released:       ║   │
    │   ║    gravity = base ×   ║   │
    │   ║    fallMultiplier     ║   │
    │   ║    (cuts jump short)  ║   │
    │   ╚═══════════╤═══════════╝   │
    │               │               │
    │   ╔═══════════▼═══════════╗   │
    │   ║  APEX (|vel.Y| < 40) ║   │
    │   ║                       ║   │
    │   ║  Jump held:           ║   │
    │   ║    gravity = base ×   ║   │
    │   ║    apexMultiplier     ║   │
    │   ║    (0.4–0.7 = float!) ║   │
    │   ╚═══════════╤═══════════╝   │
    │               │               │
    │   ╔═══════════▼═══════════╗   │
    │   ║  FALLING (vel.Y > 0)  ║   │
    │   ║                       ║   │
    │   ║  gravity = base ×     ║   │
    │   ║  fallMultiplier       ║   │
    │   ║  (1.5–3.0 = snappy)   ║   │
    │   ║                       ║   │
    │   ║  Capped at            ║   │
    │   ║  MaxFallSpeed         ║   │
    │   ╚═══════════════════════╝   │
    └───────────────────────────────┘
```

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

### Double-Jump Visual Differentiation

Signal to the player that they've used their extra jump:

```csharp
// When a non-grounded jump fires (air jump):
if (!grounded.IsGrounded && !wasCoyote)
{
    // Spawn a puff / wing effect at player's feet
    SpawnDoubleJumpVFX(pos);
    // Optionally reduce the jump velocity for subsequent jumps
    vel = vel with { Y = -ctrl.JumpVelocity * 0.85f };
}
```

---

## 7 — Coyote Time

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

## 8 — Jump Buffering

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

### Generic Input Buffer

For games that need to buffer multiple actions (jump, dash, attack, interact):

```csharp
/// <summary>
/// A generic input buffer that tracks the most recent press time for any action.
/// Useful when multiple mechanics need buffering (jump, dash, attack).
/// </summary>
public class InputBuffer
{
    private readonly Dictionary<string, float> _buffers = new();
    private float _defaultDuration;

    public InputBuffer(float defaultDuration = 0.133f)
    {
        _defaultDuration = defaultDuration;
    }

    /// <summary>Record that an action was pressed this frame.</summary>
    public void Press(string action)
    {
        _buffers[action] = _defaultDuration;
    }

    /// <summary>Check if an action is buffered and consume it.</summary>
    public bool Consume(string action)
    {
        if (_buffers.TryGetValue(action, out float timer) && timer > 0f)
        {
            _buffers[action] = 0f;
            return true;
        }
        return false;
    }

    /// <summary>Check if an action is buffered without consuming it.</summary>
    public bool Peek(string action)
    {
        return _buffers.TryGetValue(action, out float timer) && timer > 0f;
    }

    /// <summary>Tick all buffers. Call once per frame.</summary>
    public void Update(float dt)
    {
        foreach (var key in _buffers.Keys.ToArray())
        {
            _buffers[key] = Math.Max(_buffers[key] - dt, 0f);
        }
    }
}

// Usage:
inputBuffer.Press("jump");
inputBuffer.Press("dash");
// ...later...
if (grounded.IsGrounded && inputBuffer.Consume("jump"))
    ExecuteJump();
```

### Typical Values

| Frames (60fps) | Time (seconds) | Feel |
|-----------------|----------------|------|
| 4–5 | 0.066–0.083 | Tight — skilled players only |
| 6–8 | 0.100–0.133 | Standard — feels responsive |
| 9–12 | 0.150–0.200 | Very generous |

> 💡 **Coyote time + jump buffering together** is what makes a platformer feel "tight but fair." They solve opposite problems: coyote time handles "jumped too late" and buffering handles "jumped too early."

---

## 9 — Wall Mechanics

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

### Wall Jump Input Lock

Without a brief input lock, the player can immediately hold back toward the wall and cancel the horizontal push, making wall jumps feel sluggish:

```csharp
public record struct WallJumpLock(float Timer, int AwayDirection);

// When wall jump fires:
world.Add(entity, new WallJumpLock(Timer: 0.12f, AwayDirection: -ctrl.WallDirection));

// In horizontal movement, override input during lock:
if (world.Has<WallJumpLock>(entity))
{
    ref var wjLock = ref world.Get<WallJumpLock>(entity);
    wjLock.Timer -= dt;

    if (wjLock.Timer <= 0f)
    {
        world.Remove<WallJumpLock>(entity);
    }
    else
    {
        // During lock, only allow input in the away direction (or neutral)
        // This prevents immediately re-grabbing the same wall
        if (Math.Sign(inputX) == -wjLock.AwayDirection)
            inputX = 0f; // Block input toward the wall
    }
}
```

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

### Ledge Grab

When the player's top is near a wall's top edge, snap them into a ledge-hang position:

```csharp
public static class LedgeGrab
{
    public const float GrabRange = 8f; // How far below the ledge edge to detect

    public static bool TryGrab(
        in Position pos,
        in ColliderBox col,
        ReadOnlySpan<RectF> solids,
        int wallDirection,
        out float ledgeX,
        out float ledgeY)
    {
        ledgeX = 0f;
        ledgeY = 0f;

        float sideX = wallDirection > 0 ? col.Right(pos.X) : col.Left(pos.X);
        float headY = col.Top(pos.Y);

        for (int s = 0; s < solids.Length; s++)
        {
            ref readonly var solid = ref solids[s];

            // Is the wall face adjacent?
            bool adjacent = wallDirection > 0
                ? (solid.Left >= sideX && solid.Left <= sideX + 4f)
                : (solid.Right <= sideX && solid.Right >= sideX - 4f);

            if (!adjacent) continue;

            // Is our head near the top of this wall?
            float edgeTop = solid.Top;
            if (headY >= edgeTop - GrabRange && headY <= edgeTop + GrabRange)
            {
                // Make sure there's open space above the ledge (room to climb up)
                bool spaceAbove = !HasSolidAt(sideX, edgeTop - col.Height, solids);
                if (spaceAbove)
                {
                    ledgeX = wallDirection > 0 ? solid.Left : solid.Right;
                    ledgeY = edgeTop;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasSolidAt(float x, float y, ReadOnlySpan<RectF> solids)
    {
        for (int i = 0; i < solids.Length; i++)
        {
            ref readonly var s = ref solids[i];
            if (x >= s.Left && x <= s.Right && y >= s.Top && y <= s.Bottom)
                return true;
        }
        return false;
    }
}
```

---

## 10 — Slopes

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
| Speed boost going downhill | Optionally add/subtract slope factor based on direction |

### Slope Speed Modifiers

Some games speed the player up going downhill and slow them uphill:

```csharp
/// <summary>
/// Applies optional speed scaling based on slope direction.
/// Uphill = slower, Downhill = faster. Scale of 0.0 = no effect.
/// </summary>
public static void ApplySlopeSpeedModifier(
    ref Velocity vel,
    in Grounded grounded,
    float slopeSpeedScale = 0.3f)
{
    if (!grounded.IsGrounded) return;

    float angle = SlopeAngle(grounded.NormalX, grounded.NormalY);
    if (angle < 1f) return;

    // Determine if going uphill or downhill
    // If normal points right and we're moving right, we're going uphill
    bool goingUphill = (grounded.NormalX > 0 && vel.X > 0)
                    || (grounded.NormalX < 0 && vel.X < 0);

    float modifier = goingUphill
        ? 1f - (angle / 90f * slopeSpeedScale)  // Slow down uphill
        : 1f + (angle / 90f * slopeSpeedScale); // Speed up downhill

    vel = vel with { X = vel.X * modifier };
}
```

---

## 11 — One-Way Platforms

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

## 12 — Moving Platforms

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

### Crushing Detection

Moving platforms can crush the player against walls or ceilings. Detect and respond:

```csharp
/// <summary>
/// After applying platform movement to the rider, check if they're now
/// overlapping a solid. If so, the platform is crushing them.
/// </summary>
public static bool CheckCrushing(
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

// After platform moves rider:
if (CheckCrushing(in pos, in col, solids))
{
    // Options: kill player, push player out, or reset platform
    // Most games kill or deal damage
    DealDamage(entity, crushDamage: 999);
}
```

---

## 13 — Ladders & Climbing

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

## 14 — Dash / Dodge

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

### Dash Variants

| Variant | Behavior | Used By |
|---------|----------|---------|
| **Directional dash** | 8-way input direction | Celeste, Hyper Light Drifter |
| **Horizontal only** | Always dash left/right | Dead Cells |
| **Ground dash / slide** | Dash only when grounded, slide along ground | Mega Man X |
| **Air dash** | Dash only in air, resets on landing | Hollow Knight (Mothwing Cloak) |
| **Chain dash** | Can dash again during a dash window | Celeste (wavedash) |

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

## 15 — Corner Correction

### The Problem

The player jumps, and the top-left corner of their collider clips a block by 2 pixels. Without correction, the jump is killed and the player falls. This is infuriating.

```
       Before Correction          After Correction
    ┌──────────┐                ┌──────────┐
    │  Block   │                │  Block   │
    └──────┬───┘                └──────────┘
      ┌────┴──┐  ← Blocked!         ┌──────┐  ← Nudged right!
      │Player │                      │Player│
      └───────┘                      └──────┘
      clips by 2px                   jump succeeds
```

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

## 16 — Collision Resolution

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

    public static bool OverlapsAnySolid(
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

### Tunneling Prevention

At very high speeds (dash, knockback), the per-pixel sweep loop handles most tunneling. But for extreme velocities, limit movement per frame:

```csharp
// Before MoveAndCollide, cap velocity to prevent processing thousands of pixels
const float MaxPixelsPerFrame = 32f; // At 60fps, this allows 1920 px/s
float speed = MathF.Sqrt(vel.X * vel.X + vel.Y * vel.Y);
if (speed * dt > MaxPixelsPerFrame)
{
    float scale = MaxPixelsPerFrame / (speed * dt);
    vel = new Velocity(vel.X * scale, vel.Y * scale);
}
```

---

## 17 — Crouching & Sliding

### Basic Crouch

Crouching reduces the collider height, slows movement, and unlocks crouch-specific actions (slide, crawl under low gaps):

```csharp
public static class CrouchSystem
{
    public static void Update(
        ref PlayerController ctrl,
        ref ColliderBox col,
        ref Position pos,
        in Grounded grounded,
        ReadOnlySpan<RectF> solids,
        bool crouchHeld,
        float dt)
    {
        bool wantsCrouch = crouchHeld && grounded.IsGrounded;

        if (wantsCrouch && !ctrl.IsCrouching)
        {
            // Enter crouch — shrink collider from top
            float heightDiff = ctrl.StandingHeight - ctrl.CrouchingHeight;
            col = col with
            {
                OffsetY = col.OffsetY + heightDiff,
                Height = ctrl.CrouchingHeight
            };
            ctrl.IsCrouching = true;
        }
        else if (!wantsCrouch && ctrl.IsCrouching)
        {
            // Try to stand up — check for ceiling
            float heightDiff = ctrl.StandingHeight - ctrl.CrouchingHeight;
            var standCol = col with
            {
                OffsetY = col.OffsetY - heightDiff,
                Height = ctrl.StandingHeight
            };

            // Can we fit?
            if (!CollisionResolver.OverlapsAnySolid(pos, standCol, solids))
            {
                col = standCol;
                ctrl.IsCrouching = false;
            }
            // else: stay crouching — ceiling is too low
        }
    }
}
```

### Crouch Slide

A common action-platformer mechanic: while running, press crouch to slide along the ground with decaying speed:

```csharp
public record struct CrouchSlide(float Timer, float InitialSpeed);

public static class CrouchSlideSystem
{
    public const float SlideDecay = 0.92f; // Per-frame velocity multiplier
    public const float MinSlideSpeed = 40f;
    public const float MaxSlideDuration = 0.6f;
    public const float MinRunSpeed = 100f; // Must be moving this fast to trigger

    public static void TrySlide(
        ref Velocity vel,
        ref PlayerController ctrl,
        bool crouchPressed,
        World world,
        Entity entity)
    {
        if (!crouchPressed) return;
        if (!ctrl.IsCrouching) return; // Must already be in crouch
        if (Math.Abs(vel.X) < MinRunSpeed) return;
        if (world.Has<CrouchSlide>(entity)) return; // Already sliding

        world.Add(entity, new CrouchSlide(
            Timer: MaxSlideDuration,
            InitialSpeed: Math.Abs(vel.X) * 1.3f // Speed boost on entry
        ));

        vel = vel with { X = ctrl.FacingDirection * Math.Abs(vel.X) * 1.3f };
    }

    public static void UpdateSlide(
        ref Velocity vel,
        ref PlayerController ctrl,
        World world,
        Entity entity,
        float dt)
    {
        if (!world.Has<CrouchSlide>(entity)) return;

        ref var slide = ref world.Get<CrouchSlide>(entity);
        slide.Timer -= dt;

        // Decay velocity
        vel = vel with { X = vel.X * SlideDecay };

        // End slide when too slow or timer expires
        if (Math.Abs(vel.X) < MinSlideSpeed || slide.Timer <= 0f)
        {
            world.Remove<CrouchSlide>(entity);
        }
    }
}
```

### Design Decisions

| Decision | Option A | Option B |
|----------|----------|----------|
| Crouch collider change | Shrink from top (head ducks) | Shrink from bottom (feet tuck) |
| Can attack while crouching? | Yes (crouch-attack animations) | No (must stand first) |
| Slide trigger | Crouch while running | Dedicated slide button |
| Slide under gaps | Automatic when path is clear | Requires holding crouch |

---

## 18 — Swimming & Water Physics

### Water Zone Detection

Use `Area2D` overlap (or ECS equivalent) to detect when the player enters a water volume:

```csharp
public static class WaterSystem
{
    public const float WaterGravityScale = 0.35f;   // Much reduced gravity
    public const float WaterMaxFallSpeed = 80f;      // Slow sinking
    public const float WaterMoveSpeedScale = 0.6f;   // Slower horizontal
    public const float WaterJumpScale = 0.7f;         // Weaker jumps
    public const float SurfaceSnapRange = 8f;         // Pixels near surface to "bob"
    public const float BuoyancyStrength = 150f;       // Upward force near surface

    public static void Update(
        ref Position pos,
        ref Velocity vel,
        ref PlayerController ctrl,
        in WaterZone water,
        float inputX,
        float inputY,
        bool jumpPressed,
        float dt)
    {
        float playerCenterY = pos.Y; // Adjust for collider

        // ── Buoyancy: pushes player up near the surface ──
        float depth = playerCenterY - water.SurfaceY;
        if (depth > 0 && depth < 64f) // Within 64px of surface
        {
            float buoyancy = BuoyancyStrength * (1f - depth / 64f);
            vel = vel with { Y = vel.Y - buoyancy * dt };
        }

        // ── Water drag ──
        vel = new Velocity(
            vel.X * (1f - water.Drag * dt),
            vel.Y * (1f - water.Drag * dt)
        );

        // ── Swim movement ──
        float swimSpeed = ctrl.MoveSpeed * WaterMoveSpeedScale;
        if (Math.Abs(inputX) > 0.1f)
        {
            vel = vel with { X = HorizontalMovement.MoveToward(
                vel.X, inputX * swimSpeed, ctrl.GroundAcceleration * 0.5f * dt) };
            ctrl.FacingDirection = inputX > 0 ? 1 : -1;
        }

        // ── Vertical swim (press up to swim upward) ──
        if (inputY < -0.1f) // Pressing up
        {
            vel = vel with { Y = HorizontalMovement.MoveToward(
                vel.Y, -swimSpeed * 0.8f, ctrl.GroundAcceleration * 0.4f * dt) };
        }
        else if (inputY > 0.1f) // Pressing down — dive faster
        {
            vel = vel with { Y = HorizontalMovement.MoveToward(
                vel.Y, swimSpeed * 0.5f, ctrl.GroundAcceleration * 0.3f * dt) };
        }

        // ── Water gravity (much weaker) ──
        vel = vel with { Y = Math.Min(vel.Y + ctrl.Gravity * WaterGravityScale * dt,
                                       WaterMaxFallSpeed) };

        // ── Jump out of water (near surface) ──
        if (jumpPressed && depth < SurfaceSnapRange)
        {
            vel = vel with { Y = -ctrl.JumpVelocity * WaterJumpScale };
            ctrl.IsInWater = false;
        }

        // ── Cap speeds ──
        vel = new Velocity(
            Math.Clamp(vel.X, -swimSpeed, swimSpeed),
            Math.Clamp(vel.Y, -swimSpeed, WaterMaxFallSpeed)
        );
    }
}
```

### Water Entry/Exit

```csharp
// Entering water:
if (overlapsWaterZone && !ctrl.IsInWater)
{
    ctrl.IsInWater = true;

    // Kill most vertical momentum on entry (splash!)
    vel = vel with { Y = vel.Y * 0.3f };

    // Spawn splash VFX at water surface
    SpawnSplash(water.SurfaceY, pos.X, Math.Abs(vel.Y));

    // Reset jumps (can jump out of water)
    ctrl.JumpsRemaining = ctrl.MaxJumps;
}

// Exiting water (jumped out or walked out):
if (!overlapsWaterZone && ctrl.IsInWater)
{
    ctrl.IsInWater = false;
    // Optional: small speed boost on exit for satisfying feel
}
```

### Water Interaction Table

| Property | Surface | Underwater | Deep Water |
|----------|---------|------------|------------|
| Gravity | 35% | 35% | 35% |
| Move speed | 60% | 60% | 50% |
| Jump | 70% strength | Swim upward | Swim upward |
| Dash | Available, shorter | Shorter, more drag | Not available |
| Fall speed cap | 80 px/s | 80 px/s | 60 px/s |
| Coyote time | Not applicable | Not applicable | Not applicable |

---

## 19 — Top-Down Character Controller

Not all character controllers are platformers. Top-down games (Zelda, Binding of Isaac, Stardew Valley) need a different movement model.

### Core Differences from Platformer

| Aspect | Platformer | Top-Down |
|--------|-----------|----------|
| Axes | Horizontal + gravity | Both axes player-controlled |
| Gravity | Always present | None (or isometric fake) |
| Jump | Vertical mechanic | Often not present (or visual-only) |
| Facing | Left/right only | 4 or 8 directions |
| Collision | Feet-based, ground detection | Full AABB overlap |

### Top-Down Components

```csharp
/// <summary>Top-down character controller configuration.</summary>
public record struct TopDownController
{
    public float MoveSpeed;
    public float Acceleration;
    public float Deceleration;
    public float DashSpeed;
    public float DashDuration;
    public float DashCooldown;
    public float DashTimer;
    public float DashCooldownTimer;
    public bool  IsDashing;
    public int   FacingX;       // -1, 0, 1
    public int   FacingY;       // -1, 0, 1 (4 or 8 direction)
    public bool  IsAttacking;
    public float AttackTimer;

    public static TopDownController Default() => new()
    {
        MoveSpeed    = 160f,
        Acceleration = 2000f,
        Deceleration = 2400f,
        DashSpeed    = 400f,
        DashDuration = 0.15f,
        DashCooldown = 0.5f,
        FacingX      = 0,
        FacingY      = 1, // Default facing down
    };
}
```

### 8-Direction Movement with Diagonal Normalization

```csharp
public static class TopDownMovement
{
    public static void Apply(
        ref Velocity vel,
        ref TopDownController ctrl,
        float inputX,
        float inputY,
        float dt)
    {
        if (ctrl.IsDashing || ctrl.IsAttacking) return;

        // Normalize diagonal input to prevent faster diagonal movement
        float magnitude = MathF.Sqrt(inputX * inputX + inputY * inputY);
        if (magnitude > 1f)
        {
            inputX /= magnitude;
            inputY /= magnitude;
        }

        if (magnitude > 0.1f)
        {
            // Update facing direction (snap to 4 or 8 directions)
            UpdateFacing(ref ctrl, inputX, inputY);

            // Accelerate toward target
            float targetX = inputX * ctrl.MoveSpeed;
            float targetY = inputY * ctrl.MoveSpeed;

            vel = new Velocity(
                HorizontalMovement.MoveToward(vel.X, targetX, ctrl.Acceleration * dt),
                HorizontalMovement.MoveToward(vel.Y, targetY, ctrl.Acceleration * dt)
            );
        }
        else
        {
            // Decelerate to stop
            vel = new Velocity(
                HorizontalMovement.MoveToward(vel.X, 0f, ctrl.Deceleration * dt),
                HorizontalMovement.MoveToward(vel.Y, 0f, ctrl.Deceleration * dt)
            );
        }
    }

    /// <summary>Snap facing to the nearest 4-direction (or 8 if you prefer).</summary>
    private static void UpdateFacing(ref TopDownController ctrl, float inputX, float inputY)
    {
        // 4-direction: pick dominant axis
        if (Math.Abs(inputX) > Math.Abs(inputY))
        {
            ctrl.FacingX = Math.Sign(inputX);
            ctrl.FacingY = 0;
        }
        else
        {
            ctrl.FacingX = 0;
            ctrl.FacingY = Math.Sign(inputY);
        }
    }
}
```

### Top-Down Collision (Full AABB)

Unlike platformer collision (which uses axis-separated ground/ceiling/wall), top-down collision checks all four sides equally:

```csharp
public static class TopDownCollision
{
    /// <summary>
    /// Axis-separated sweep for top-down movement.
    /// Same algorithm as platformer but no special ground/ceiling handling.
    /// </summary>
    public static void MoveAndCollide(
        ref Position pos,
        ref Velocity vel,
        in ColliderBox col,
        ReadOnlySpan<RectF> solids,
        float dt)
    {
        // Identical to CollisionResolver.MoveAndCollide
        // The difference is conceptual: there's no "ground" or "ceiling",
        // just obstacles in all directions
        CollisionResolver.MoveAndCollide(ref pos, ref vel, in col, solids, dt);
    }

    /// <summary>
    /// Slide along walls when moving diagonally into a corner.
    /// The axis-separated sweep handles this automatically —
    /// blocked on X still allows Y movement and vice versa.
    /// </summary>
}
```

### Top-Down Dash (Omnidirectional)

```csharp
public static void TryTopDownDash(
    ref Velocity vel,
    ref TopDownController ctrl,
    bool dashPressed,
    float inputX,
    float inputY)
{
    if (!dashPressed || ctrl.IsDashing || ctrl.DashCooldownTimer > 0f) return;

    ctrl.IsDashing = true;
    ctrl.DashTimer = ctrl.DashDuration;
    ctrl.DashCooldownTimer = ctrl.DashCooldown;

    // Dash in input direction, or facing direction if no input
    float dirX = inputX;
    float dirY = inputY;
    if (Math.Abs(dirX) < 0.1f && Math.Abs(dirY) < 0.1f)
    {
        dirX = ctrl.FacingX;
        dirY = ctrl.FacingY;
    }

    // Normalize
    float len = MathF.Sqrt(dirX * dirX + dirY * dirY);
    if (len > 0.01f)
    {
        dirX /= len;
        dirY /= len;
    }

    vel = new Velocity(dirX * ctrl.DashSpeed, dirY * ctrl.DashSpeed);
}
```

---

## 20 — State Machine Integration

A controller with many states (grounded, airborne, wall-sliding, dashing, crouching, swimming, climbing, attacking) quickly becomes a tangle of if/else chains. A state machine keeps it organized.

### Controller State Enum

```csharp
public enum ControllerState
{
    Grounded,
    Airborne,
    WallSliding,
    WallClinging,
    LedgeHanging,
    Dashing,
    Crouching,
    CrouchSliding,
    Swimming,
    Climbing,
    Attacking,
    Knockback,
    Dead
}
```

### State Transition Table

```
         ┌──────────┐   jump    ┌──────────┐
    ┌───→│ Grounded │──────────→│ Airborne │←──────────┐
    │    └────┬─────┘           └────┬─────┘           │
    │         │ crouch               │ touch wall      │
    │    ┌────▼──────┐          ┌────▼──────────┐      │
    │    │ Crouching │          │ WallSliding   │      │
    │    └────┬──────┘          └────┬──────────┘      │
    │         │ run+crouch           │ near edge       │
    │    ┌────▼──────────┐      ┌────▼──────────┐      │
    │    │ CrouchSliding │      │ LedgeHanging  │      │
    │    └───────────────┘      └───────────────┘      │
    │                                                   │
    │    ┌──────────┐  end     ┌──────────┐            │
    │    │ Dashing  │─────────→│ Airborne │            │
    │    └──────────┘          └──────────┘            │
    │                                                   │
    │    ┌──────────┐  exit    ┌──────────┐            │
    │    │ Swimming │─────────→│ Airborne │            │
    │    └──────────┘          └──────────┘            │
    │                                                   │
    │    ┌──────────┐  land                            │
    └────│ Climbing │──────────────────────────────────┘
         └──────────┘
```

### State Machine Pattern

```csharp
public interface IControllerState
{
    /// <summary>Called when entering this state.</summary>
    void Enter(ref PlayerController ctrl, ref Velocity vel);

    /// <summary>Called every frame while in this state.</summary>
    ControllerState Update(
        ref Position pos,
        ref Velocity vel,
        ref PlayerController ctrl,
        ref ColliderBox col,
        ref Grounded grounded,
        in InputState input,
        ReadOnlySpan<RectF> solids,
        float dt);

    /// <summary>Called when leaving this state.</summary>
    void Exit(ref PlayerController ctrl, ref Velocity vel);
}

/// <summary>Example: Grounded state handles horizontal movement, crouch entry, jump, dash.</summary>
public class GroundedState : IControllerState
{
    public void Enter(ref PlayerController ctrl, ref Velocity vel)
    {
        ctrl.JumpsRemaining = ctrl.MaxJumps;
    }

    public ControllerState Update(
        ref Position pos, ref Velocity vel, ref PlayerController ctrl,
        ref ColliderBox col, ref Grounded grounded, in InputState input,
        ReadOnlySpan<RectF> solids, float dt)
    {
        // Horizontal movement
        HorizontalMovement.Apply(ref vel, ref ctrl, input.X, dt, isGrounded: true);

        // Check transitions (priority order)
        if (input.DashPressed && ctrl.DashCooldownTimer <= 0f)
            return ControllerState.Dashing;

        if (input.JumpPressed || ctrl.JumpBufferTimer > 0f)
        {
            vel = vel with { Y = -ctrl.JumpVelocity };
            ctrl.JumpsRemaining--;
            ctrl.JumpBufferTimer = 0f;
            return ControllerState.Airborne;
        }

        if (input.DownPressed)
            return ControllerState.Crouching;

        if (!grounded.IsGrounded)
        {
            ctrl.CoyoteTimer = ctrl.CoyoteTime;
            return ControllerState.Airborne;
        }

        return ControllerState.Grounded; // Stay in current state
    }

    public void Exit(ref PlayerController ctrl, ref Velocity vel) { }
}
```

### State Machine Host

```csharp
public class ControllerStateMachine
{
    private readonly Dictionary<ControllerState, IControllerState> _states = new();
    private ControllerState _current = ControllerState.Airborne;

    public void Register(ControllerState id, IControllerState state)
    {
        _states[id] = state;
    }

    public void Update(
        ref Position pos, ref Velocity vel, ref PlayerController ctrl,
        ref ColliderBox col, ref Grounded grounded, in InputState input,
        ReadOnlySpan<RectF> solids, float dt)
    {
        var next = _states[_current].Update(
            ref pos, ref vel, ref ctrl, ref col, ref grounded, in input, solids, dt);

        if (next != _current)
        {
            _states[_current].Exit(ref ctrl, ref vel);
            _current = next;
            _states[_current].Enter(ref ctrl, ref vel);
        }
    }

    public ControllerState Current => _current;
}
```

### When to Use a State Machine

| Controller Complexity | Approach | Why |
|----------------------|----------|-----|
| ≤4 states (ground, air, wall, dash) | Inline if/else | Simple enough, no overhead |
| 5–8 states | Enum + switch | Organized but still flat |
| 8+ states with transitions | Interface-based state machine | Clean separation, easy to add states |
| Complex hierarchical (Celeste-level) | Hierarchical state machine | See [G31 Animation & State Machines](./G31_animation_state_machines.md) |

---

## 21 — Camera Integration

The character controller and camera system must communicate for juice effects. See [G20 Camera Systems](./G20_camera_systems.md) for the full camera guide.

### Camera Events from Controller

```csharp
/// <summary>Events the controller can emit for camera, VFX, and audio systems.</summary>
public record struct ControllerEvents
{
    public bool Landed;
    public float LandingSpeed;       // Vertical speed at impact
    public bool Jumped;
    public bool WallJumped;
    public bool DashStarted;
    public float DashDirectionX;
    public float DashDirectionY;
    public bool Damaged;
    public float DamageDirectionX;
}

// Example camera response:
if (events.Landed && events.LandingSpeed > 300f)
{
    // Strong landing — vertical shake
    float trauma = Math.Min(events.LandingSpeed / 800f, 0.6f);
    cameraShake.AddTrauma(trauma);
    // Y squash on camera (settle effect)
    cameraOffset.Y += 6f;
}

if (events.DashStarted)
{
    // Brief directional punch in dash direction
    cameraOffset.X += events.DashDirectionX * 4f;
    cameraOffset.Y += events.DashDirectionY * 4f;
}
```

### Look-Ahead Integration

The camera should look ahead in the player's movement direction. The controller provides the data:

```csharp
// From controller to camera each frame:
float lookAheadX = ctrl.FacingDirection * 40f;
float lookAheadY = 0f;

// When falling, look down
if (vel.Y > 200f)
    lookAheadY = 20f;

// When wall sliding, look toward open space (away from wall)
if (ctrl.IsOnWall)
    lookAheadX = -ctrl.WallDirection * 50f;

camera.SetLookAhead(lookAheadX, lookAheadY);
```

### Vertical Camera Snap

For platformers, the camera shouldn't smoothly follow every pixel of vertical movement — it should snap to the ground level when the player lands:

```csharp
// Camera Y tracking:
if (grounded.IsGrounded)
{
    // Snap to player Y (or smooth with fast lerp)
    cameraTargetY = pos.Y;
    cameraSnapTimer = 0f;
}
else if (vel.Y > 0 && pos.Y > cameraTargetY + deadzone)
{
    // Only follow downward after passing a deadzone threshold
    cameraTargetY = pos.Y - deadzone;
}
// When rising (jumping), DON'T follow — let the player leave the frame top
// This prevents the camera from bouncing with every jump
```

---

## 22 — Debug Visualization

### Controller Debug Overlay

Essential during development. Toggle with a debug key:

```csharp
public static class ControllerDebug
{
    public static bool Enabled = false;

    public static void Draw(
        SpriteBatch batch,
        BitmapFont font,
        in Position pos,
        in Velocity vel,
        in PlayerController ctrl,
        in ColliderBox col,
        in Grounded grounded)
    {
        if (!Enabled) return;

        float x = pos.X + 20f;
        float y = pos.Y - 60f;
        float lineH = 12f;

        // ── State indicators ──
        DrawText(batch, font, x, y, $"State: {GetStateLabel(ctrl, grounded)}");
        y += lineH;
        DrawText(batch, font, x, y,
            $"Vel: ({vel.X:F1}, {vel.Y:F1})  Speed: {MathF.Sqrt(vel.X*vel.X+vel.Y*vel.Y):F0}");
        y += lineH;
        DrawText(batch, font, x, y,
            $"Grounded: {grounded.IsGrounded}  Facing: {(ctrl.FacingDirection > 0 ? "R" : "L")}");
        y += lineH;

        // ── Timers (show as bars) ──
        DrawTimer(batch, x, y, "Coyote", ctrl.CoyoteTimer, ctrl.CoyoteTime);
        y += lineH;
        DrawTimer(batch, x, y, "Buffer", ctrl.JumpBufferTimer, ctrl.JumpBufferTime);
        y += lineH;
        DrawTimer(batch, x, y, "Dash CD", ctrl.DashCooldownTimer, ctrl.DashCooldown);
        y += lineH;
        DrawText(batch, font, x, y, $"Jumps: {ctrl.JumpsRemaining}/{ctrl.MaxJumps}");
        y += lineH;

        // ── Collider box ──
        DrawRect(batch, col.Left(pos.X), col.Top(pos.Y), col.Width, col.Height,
            grounded.IsGrounded ? Color.Green : Color.Red, filled: false);

        // ── Ground detection rays ──
        float left = col.Left(pos.X) + GroundDetection.SkinWidth;
        float right = col.Right(pos.X) - GroundDetection.SkinWidth;
        float bottom = col.Bottom(pos.Y) - GroundDetection.SkinWidth;

        for (int i = 0; i < GroundDetection.RayCount; i++)
        {
            float t = GroundDetection.RayCount == 1 ? 0.5f
                : (float)i / (GroundDetection.RayCount - 1);
            float rayX = MathHelper.Lerp(left, right, t);
            DrawLine(batch, rayX, bottom, rayX,
                bottom + GroundDetection.GroundCheckDistance,
                grounded.IsGrounded ? Color.Green : Color.Yellow);
        }

        // ── Velocity vector ──
        DrawLine(batch, pos.X, pos.Y,
            pos.X + vel.X * 0.1f, pos.Y + vel.Y * 0.1f, Color.Cyan);

        // ── Wall detection indicators ──
        if (ctrl.IsOnWall)
        {
            float wallX = ctrl.WallDirection > 0
                ? col.Right(pos.X) + 2f
                : col.Left(pos.X) - 2f;
            DrawLine(batch, wallX, col.Top(pos.Y), wallX, col.Bottom(pos.Y), Color.Orange);
        }
    }

    private static string GetStateLabel(in PlayerController ctrl, in Grounded grounded)
    {
        if (ctrl.IsDashing)   return "DASH";
        if (ctrl.IsOnLadder)  return "CLIMB";
        if (ctrl.IsInWater)   return "SWIM";
        if (ctrl.IsOnWall)    return "WALL";
        if (ctrl.IsCrouching) return "CROUCH";
        if (grounded.IsGrounded) return "GROUND";
        return "AIR";
    }

    private static void DrawTimer(SpriteBatch batch, float x, float y,
        string label, float current, float max)
    {
        float pct = max > 0 ? Math.Clamp(current / max, 0f, 1f) : 0f;
        // Draw background bar
        DrawRect(batch, x, y, 80f, 8f, Color.DarkGray, filled: true);
        // Draw filled portion
        if (pct > 0f)
            DrawRect(batch, x, y, 80f * pct, 8f, Color.Yellow, filled: true);
        // Label
        DrawText(batch, null, x + 85f, y, $"{label}: {current:F2}s");
    }

    // DrawRect, DrawLine, DrawText — use your preferred debug primitives
    private static void DrawRect(SpriteBatch b, float x, float y, float w, float h,
        Color c, bool filled) { /* ... */ }
    private static void DrawLine(SpriteBatch b, float x1, float y1, float x2, float y2,
        Color c) { /* ... */ }
    private static void DrawText(SpriteBatch b, BitmapFont f, float x, float y,
        string text) { /* ... */ }
}
```

### Input History Visualizer

Shows the last N frames of input, invaluable for debugging input timing issues:

```csharp
public class InputHistoryVisualizer
{
    private readonly (InputState input, bool grounded, bool jumped)[] _history;
    private int _writeIndex;
    private readonly int _frames;

    public InputHistoryVisualizer(int frames = 60)
    {
        _frames = frames;
        _history = new (InputState, bool, bool)[frames];
    }

    public void Record(in InputState input, bool grounded, bool jumped)
    {
        _history[_writeIndex] = (input, grounded, jumped);
        _writeIndex = (_writeIndex + 1) % _frames;
    }

    public void Draw(SpriteBatch batch, float x, float y)
    {
        // Draw a horizontal timeline: each column = 1 frame
        // Row 1: Jump pressed (red dot)
        // Row 2: Grounded (green bar)
        // Row 3: Jump executed (blue dot)
        // Row 4: Horizontal input (bar direction)

        for (int i = 0; i < _frames; i++)
        {
            int idx = (_writeIndex + i) % _frames;
            var h = _history[idx];
            float cx = x + i * 3f;

            if (h.input.JumpPressed)
                DrawRect(batch, cx, y, 2, 2, Color.Red, true);
            if (h.grounded)
                DrawRect(batch, cx, y + 4, 2, 2, Color.Green, true);
            if (h.jumped)
                DrawRect(batch, cx, y + 8, 2, 2, Color.Blue, true);
            if (Math.Abs(h.input.X) > 0.1f)
                DrawRect(batch, cx, y + 12, 2, 2,
                    h.input.X > 0 ? Color.Cyan : Color.Magenta, true);
        }
    }
}
```

### Trajectory Prediction

Draw the predicted jump arc before the player jumps (useful for debugging and some gameplay mechanics):

```csharp
public static class TrajectoryPreview
{
    /// <summary>
    /// Simulates the jump trajectory and returns sample points.
    /// Call from debug draw, NOT from gameplay logic.
    /// </summary>
    public static Vector2[] Predict(
        in Position pos,
        in PlayerController ctrl,
        float inputX,
        int sampleCount = 30,
        float stepTime = 0.016f)
    {
        var points = new Vector2[sampleCount];
        float simX = pos.X;
        float simY = pos.Y;
        float simVelX = inputX * ctrl.MoveSpeed;
        float simVelY = -ctrl.JumpVelocity;

        for (int i = 0; i < sampleCount; i++)
        {
            points[i] = new Vector2(simX, simY);

            // Simulate gravity (simplified — no wall/ground collision)
            float gravity = ctrl.Gravity;
            if (simVelY > 0) gravity *= ctrl.FallGravityMultiplier;

            simVelY = Math.Min(simVelY + gravity * stepTime, ctrl.MaxFallSpeed);
            simX += simVelX * stepTime;
            simY += simVelY * stepTime;
        }

        return points;
    }
}
```

---

## 23 — Complete ECS System

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

    /// <summary>Events emitted this frame for camera/VFX/audio systems.</summary>
    public ControllerEvents Events { get; private set; }

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
        var events = new ControllerEvents();

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
            float prevVelY = vel.Y;

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
            //  2. WATER CHECK
            // ═══════════════════════════════════════════
            if (ctrl.IsInWater)
            {
                // Water movement handled by WaterSystem
                // Skip normal gravity/movement pipeline
                return;
            }

            // ═══════════════════════════════════════════
            //  3. CROUCH
            // ═══════════════════════════════════════════
            CrouchSystem.Update(ref ctrl, ref col, ref pos, in grounded, solids,
                input.DownPressed, dt);

            // ═══════════════════════════════════════════
            //  4. DASH
            // ═══════════════════════════════════════════
            bool wasDashing = ctrl.IsDashing;
            DashSystem.TryDash(ref vel, ref ctrl, input.DashPressed, input.X, input.Y);
            DashSystem.UpdateDash(ref vel, ref ctrl, dt);

            if (!wasDashing && ctrl.IsDashing)
            {
                events.DashStarted = true;
                float len = MathF.Sqrt(vel.X * vel.X + vel.Y * vel.Y);
                if (len > 0.01f)
                {
                    events.DashDirectionX = vel.X / len;
                    events.DashDirectionY = vel.Y / len;
                }
            }

            if (ctrl.IsDashing)
            {
                // During dash: skip gravity and normal movement, just collide
                CollisionResolver.MoveAndCollide(ref pos, ref vel, in col, solids, dt);
                return;
            }

            // ═══════════════════════════════════════════
            //  5. HORIZONTAL MOVEMENT
            // ═══════════════════════════════════════════
            HorizontalMovement.Apply(ref vel, ref ctrl, input.X, dt, grounded.IsGrounded);

            // ═══════════════════════════════════════════
            //  6. GRAVITY
            // ═══════════════════════════════════════════
            JumpSystem.ApplyGravity(ref vel, ref ctrl, input.JumpHeld, dt);

            // ═══════════════════════════════════════════
            //  7. WALL DETECTION
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
            //  8. JUMP (including wall jump, coyote, buffer)
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
                events.WallJumped = true;
            }
            // Normal / coyote / multi jump
            else if (JumpSystem.TryJump(ref vel, ref ctrl, ref grounded, wantsJump))
            {
                ctrl.JumpBufferTimer = 0f;
                events.Jumped = true;
            }

            // ═══════════════════════════════════════════
            //  9. SLOPE ADJUSTMENT
            // ═══════════════════════════════════════════
            SlopeHandler.AdjustForSlope(ref vel, in grounded, dt);

            // ═══════════════════════════════════════════
            // 10. COLLISION RESOLUTION
            // ═══════════════════════════════════════════
            CollisionResolver.MoveAndCollide(ref pos, ref vel, in col, solids, dt);

            // ═══════════════════════════════════════════
            // 11. CORNER CORRECTION
            // ═══════════════════════════════════════════
            if (vel.Y < 0) // Only when moving upward
            {
                CornerCorrection.TryCorrect(ref pos, in col, in vel, solids);
            }

            // ═══════════════════════════════════════════
            // 12. GROUND DETECTION
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

                events.Landed = true;
                events.LandingSpeed = Math.Abs(prevVelY);

                // Execute buffered jump on landing
                if (ctrl.JumpBufferTimer > 0f)
                {
                    JumpSystem.TryJump(ref vel, ref ctrl, ref grounded, true);
                    ctrl.JumpBufferTimer = 0f;
                    events.Jumped = true;
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
            // 13. DROP-THROUGH (one-way platforms)
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

        Events = events;
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
    public bool DownPressed;  // True while down is held (for drop-through / crouch)
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

    // Read events for camera/VFX/audio
    var events = playerControllerSystem.Events;
    if (events.Landed)
        cameraShake.AddTrauma(Math.Min(events.LandingSpeed / 800f, 0.5f));
    if (events.DashStarted)
        cameraShake.AddTrauma(0.15f);
}
```

---

## 24 — Common Mistakes & Troubleshooting

### ❌ 1. Jittery Movement on Slopes

**Symptom:** Player vibrates or bounces when walking on slopes.

**Cause:** Ground detection alternates between "grounded" and "airborne" each frame because the player lifts off the slope surface during horizontal movement.

**Fix:** Increase the ground snap distance in `SnapToSlope` and ensure slope snapping runs AFTER collision resolution:

```csharp
// Wrong: snap distance too small
SlopeHandler.SnapToSlope(ref pos, in col, solids, in grounded, maxSnapDistance: 2f);

// Right: generous snap distance
SlopeHandler.SnapToSlope(ref pos, in col, solids, in grounded, maxSnapDistance: 8f);
```

---

### ❌ 2. Jump Doesn't Fire / "Eaten" Inputs

**Symptom:** Player presses jump but nothing happens, especially when landing or at ledge edges.

**Cause:** Missing either coyote time (jumped 1 frame too late after leaving a ledge) or jump buffering (pressed jump 1 frame too early before landing).

**Fix:** Ensure both systems are active with reasonable values:

```csharp
// Verify both are non-zero:
ctrl.CoyoteTime    = 0.1f;  // ~6 frames
ctrl.JumpBufferTime = 0.133f; // ~8 frames

// Verify buffer is consumed on landing:
if (grounded.IsGrounded && ctrl.JumpBufferTimer > 0f)
{
    JumpSystem.TryJump(ref vel, ref ctrl, ref grounded, true);
    ctrl.JumpBufferTimer = 0f;
}
```

---

### ❌ 3. Player Sticks to Ceilings

**Symptom:** Player hits a ceiling and velocity doesn't reset, causing them to hover.

**Cause:** Collision resolution zeros velocity but corner correction then nudges the player, and the ceiling state isn't rechecked.

**Fix:** After corner correction, verify the player is actually clear:

```csharp
// After corner correction:
if (CornerCorrection.TryCorrect(ref pos, in col, in vel, solids))
{
    // Correction found — jump continues upward
}
else
{
    // No correction possible — truly hit ceiling, zero Y velocity
    vel = vel with { Y = 0f };
}
```

---

### ❌ 4. Wall Jump Doesn't Push Away

**Symptom:** Player wall-jumps but immediately re-grabs the same wall.

**Cause:** No input lock after wall jump. The player holds toward the wall, horizontal movement overrides the wall-jump push within 1-2 frames.

**Fix:** Add a brief input lock (see §9 Wall Jump Input Lock):

```csharp
// Block input toward the wall for 0.1–0.15 seconds after wall jump
world.Add(entity, new WallJumpLock(Timer: 0.12f, AwayDirection: -ctrl.WallDirection));
```

---

### ❌ 5. Moving Platform Desync

**Symptom:** Player slides off moving platforms or jitters when riding them.

**Cause:** Platform movement and player movement happen in the wrong order, or velocity is applied in the wrong frame.

**Fix:** Platform movement must happen BEFORE player collision resolution. Apply platform velocity to the player's position directly (not to their velocity):

```csharp
// Right: move player with platform BEFORE player's own movement
pos.X += platformVelocity.X * dt;
pos.Y += platformVelocity.Y * dt;
// Then run normal movement/collision
```

---

### ❌ 6. Sub-Pixel Drift

**Symptom:** At low speeds, the player moves at inconsistent speeds or sometimes stops entirely.

**Cause:** Fractional pixel movement is being discarded instead of accumulated.

**Fix:** Use the remainder accumulator pattern (see §16):

```csharp
// Wrong: truncate every frame (loses fractional movement)
pos.X += (int)(vel.X * dt);

// Right: accumulate remainders
float moveX = vel.X * dt + pos.RemainderX;
int pixelsX = (int)MathF.Truncate(moveX);
pos.RemainderX = moveX - pixelsX;
// Then move pixelsX whole pixels with collision checking
```

---

### ❌ 7. Crouch Can't Stand Up Under Low Ceiling

**Symptom:** Player enters a crouch tunnel and can never uncrouch, even after leaving.

**Cause:** The uncrouch check only tests the expanded collider at the current position, not checking if the player is still under a low ceiling.

**Fix:** Always check for ceiling before expanding the collider:

```csharp
// Before expanding collider:
var standCol = col with { OffsetY = col.OffsetY - heightDiff, Height = ctrl.StandingHeight };
if (!CollisionResolver.OverlapsAnySolid(pos, standCol, solids))
{
    col = standCol;
    ctrl.IsCrouching = false;
}
// If overlaps, keep crouching — the player is under a ceiling
```

---

### Quick Diagnostic Checklist

| Symptom | First Check | Second Check |
|---------|-------------|--------------|
| Can't jump | Is `JumpsRemaining > 0`? | Is ground detection working? |
| Falls through floor | Is collision sweep pixel-by-pixel? | Are solids loaded correctly? |
| Floats after hitting ceiling | Is Y velocity zeroed on ceiling hit? | Corner correction interfering? |
| Slides on flat ground | Is deceleration > 0? | Is `MoveToward` reaching 0? |
| Double jump triggers on ground | Is first jump consuming from `JumpsRemaining`? | Counting issue with MaxJumps? |
| Dash goes through walls | Is `MoveAndCollide` used during dash? | Cap dash speed to prevent tunneling |
| Velocity feels wrong between builds