# 2D Character Controller -- Theory & Concepts

This document covers engine-agnostic theory for 2D platformer character controllers, including kinematic vs physics-based approaches, ground detection, coyote time, jump buffering, wall mechanics, slopes, and tuning. Pseudocode is used throughout. For engine-specific implementations, see the relevant engine module.

---

## Kinematic vs Physics-Based

| Approach | Description | Used By |
|---|---|---|
| **Physics-based** | Apply forces/impulses to a rigid body; let the physics engine resolve movement | Puzzle platformers, ragdoll games |
| **Kinematic** | Directly set velocity each frame; handle collision manually | Celeste, Hollow Knight, Dead Cells, Mega Man, Mario |

Almost every celebrated platformer uses kinematic control. The reason: **physics engines solve for realism, not fun.** A physically accurate jump has a fixed parabolic arc. A *fun* jump has variable height, coyote time, jump buffering, apex hang, and many other intentional lies.

**Rule of thumb:** If you are using `add_force()` on your player character, you have already lost control of game feel. Set velocity directly.

---

## Deriving Jump Parameters from Design Values

Instead of tuning raw gravity and velocity numbers, express jumps as designer-friendly inputs:
- **Jump height** -- how high in pixels
- **Time to apex** -- how long in seconds to reach peak

Gravity and initial velocity are derived:

```
gravity       = 2 * jump_height / time_to_apex^2
jump_velocity = 2 * jump_height / time_to_apex
```

This lets designers say "I want a 72px jump that takes 0.35s to peak" and get exact values. Changing either input adjusts the curve while keeping the feel intentional.

---

## Ground Detection

Ground detection is the foundation everything else builds on. Get it wrong and jumping, slopes, and platforms all break.

### Multi-Ray Approach

Cast multiple rays downward from the bottom of the collider. A single center ray misses edges; three or more rays catch them.

```
SKIN_WIDTH = 2          -- rays start slightly inside the collider
CHECK_DISTANCE = 3      -- how far below to cast
RAY_COUNT = 3           -- spread across collider bottom

function check_ground(position, collider, solids):
    left  = collider.left(position) + SKIN_WIDTH
    right = collider.right(position) - SKIN_WIDTH
    bottom = collider.bottom(position) - SKIN_WIDTH

    best_distance = INFINITY
    is_grounded = false

    for i in 0..RAY_COUNT:
        t = i / (RAY_COUNT - 1)
        ray_x = lerp(left, right, t)
        ray_start = bottom
        ray_end = bottom + CHECK_DISTANCE

        for solid in solids:
            if ray_x < solid.left or ray_x > solid.right: continue
            if solid.top >= ray_start and solid.top <= ray_end:
                dist = solid.top - ray_start
                if dist < best_distance:
                    best_distance = dist
                    is_grounded = true

    return is_grounded
```

### State Transitions

Track transitions for coyote time and landing events:

```
was_grounded = is_grounded
is_grounded = check_ground(...)

-- Just landed
if is_grounded and not was_grounded:
    jumps_remaining = max_jumps
    snap_to_ground_surface()

-- Just left ground (without jumping)
if not is_grounded and was_grounded:
    coyote_timer = coyote_time
```

---

## Horizontal Movement

Instant velocity changes feel robotic. Acceleration and deceleration curves add weight and responsiveness.

```
function apply_horizontal_movement(velocity, input_x, dt, is_grounded):
    accel = ground_acceleration if is_grounded else air_acceleration
    decel = ground_deceleration if is_grounded else air_deceleration

    if abs(input_x) > 0.01:
        -- Turning? Apply turn multiplier for snappier direction changes
        turning = (velocity.x > 0 and input_x < 0) or
                  (velocity.x < 0 and input_x > 0)
        effective_accel = accel * turn_multiplier if turning else accel

        target = input_x * max_speed
        velocity.x = move_toward(velocity.x, target, effective_accel * dt)
    else:
        velocity.x = move_toward(velocity.x, 0, decel * dt)
```

### Why Separate Air and Ground Values?

| Parameter | Ground | Air | Effect |
|---|---|---|---|
| Acceleration | High | Lower | Committed air trajectory with slight control |
| Deceleration | High | Low | Crisp ground stops, floaty air momentum |

This creates the feel of *commitment* -- once airborne, the player can adjust but not instantly reverse.

---

## Variable-Height Jump

When the player releases the jump button early, increase gravity to cut the jump short. When near the apex with jump held, reduce gravity for a float effect.

```
function apply_gravity(velocity, jump_held, dt):
    gravity = base_gravity

    if velocity.y > 0:
        -- Falling: heavier gravity for snappy descent
        gravity *= fall_gravity_multiplier       -- typically 1.5-2.5

    else if velocity.y < 0 and not jump_held:
        -- Released jump early: cut the arc short
        gravity *= fall_gravity_multiplier

    else if abs(velocity.y) < apex_threshold and jump_held:
        -- Near apex with jump held: float!
        gravity *= apex_gravity_multiplier       -- typically 0.4-0.7

    velocity.y = min(velocity.y + gravity * dt, max_fall_speed)
```

### Apex Float

When the player is near the peak (velocity close to zero) and holding jump, reduced gravity creates brief hang time that:
- Makes precision platforming more forgiving
- Feels satisfying -- the character "hangs" at the top
- Gives the player more air-control time

---

## Coyote Time

Named after Wile E. Coyote running off a cliff. After walking off a ledge (not jumping), the player has a brief grace period where pressing jump still works.

Without this, players feel the game "ate" their input because they pressed jump 1-2 frames after leaving the edge.

```
-- When leaving ground without jumping:
if not is_grounded and was_grounded and velocity.y >= 0:
    coyote_timer = coyote_time

-- Tick down when airborne:
if not is_grounded:
    coyote_timer -= dt

-- In jump check, coyote time counts as grounded:
can_jump = is_grounded or coyote_timer > 0 or jumps_remaining > 0
```

### Typical Values

| Frames (60fps) | Seconds | Feel |
|---|---|---|
| 3-4 | 0.050-0.066 | Barely noticeable, tight |
| 5-7 | 0.083-0.116 | Standard -- feels fair |
| 8-10 | 0.133-0.166 | Generous -- very forgiving |

Most shipped games use 5-8 frames. Celeste uses roughly 5 frames.

---

## Jump Buffering

If the player presses jump while airborne (a few frames before landing), the jump executes the instant they touch ground. Without this, fast players who press jump 2 frames before landing get nothing.

```
-- When jump pressed (regardless of grounded state):
if jump_pressed:
    jump_buffer_timer = jump_buffer_time

-- Tick down:
jump_buffer_timer -= dt

-- On landing, check buffer:
if just_landed and jump_buffer_timer > 0:
    execute_jump()
    jump_buffer_timer = 0
```

### Timer vs Ring Buffer

A **timer** is simpler and works for single-input buffering. A **ring buffer** of recent inputs is more flexible for buffering multiple action types (jump, dash, attack). For most platformers, a timer is sufficient.

**Coyote time + jump buffering together** is what makes a platformer feel "tight but fair." They solve opposite problems: coyote time handles "jumped too late" and buffering handles "jumped too early."

---

## Wall Mechanics

### Wall Detection

Mirror the ground detection approach but cast horizontally from the side of the collider in the movement direction.

### Wall Slide

When the player is against a wall, falling, and holding toward the wall, cap the fall speed:

```
if on_wall and velocity.y > 0 and input_toward_wall:
    velocity.y = min(velocity.y, wall_slide_speed)
```

### Wall Jump

Push the player away from the wall and upward:

```
if jump_pressed and on_wall and not is_grounded:
    velocity.x = -wall_direction * wall_jump_h_velocity
    velocity.y = -wall_jump_v_velocity
    facing_direction = -wall_direction
```

### Wall Cling

Optional: the player sticks to the wall briefly before sliding. Use a timer that resets when first touching a wall:

```
if on_wall and not is_grounded:
    if wall_cling_timer > 0:
        velocity.y = 0        -- frozen on wall
        wall_cling_timer -= dt
    else:
        velocity.y = min(velocity.y, wall_slide_speed)
```

---

## One-Way Platforms

Platforms the player can jump through from below but stand on from above. Only collide when:

1. The player is moving downward (velocity.y >= 0)
2. The player's feet were above the platform top on the previous frame

### Drop-Through

When the player presses down (+ jump optionally), temporarily ignore one-way platforms for a brief duration (0.1-0.15 seconds).

---

## Slopes

Slopes are where many controllers break. Without special handling, the player bounces down slopes, jitters at transitions, or slides on walkable surfaces.

### Key Concepts

- **Ground normal** tells you the slope angle
- **Maximum walkable angle** (typically 45-50 degrees) separates walkable slopes from walls
- **Velocity projection** onto the slope tangent maintains correct speed on inclines

```
tangent = perpendicular(ground_normal)
speed = abs(velocity.x)
velocity = tangent * speed * sign(velocity.x)
```

### Common Slope Problems

| Problem | Solution |
|---|---|
| Bouncing when running down slopes | Snap to ground on descent |
| Sliding on gentle slopes when idle | Zero velocity below max angle with no input |
| Jittering at slope transitions | Increase snap distance, use multiple ground rays |
| Wrong speed on slopes | Project velocity onto slope tangent |
| Walking up walls | Enforce max slope angle check |

---

## Corner Correction

When the player's head clips the corner of a ceiling block during a jump, nudge them horizontally to clear the corner instead of killing their vertical velocity. Check a small horizontal range (2-4 pixels) on both sides.

```
function try_corner_correct(position, velocity, collider, solids, max_nudge):
    if velocity.y >= 0: return false  -- only during upward movement

    for nudge in 1..max_nudge:
        -- Try shifting left
        if not collides_at(position + (-nudge, 0), collider, solids):
            position.x -= nudge
            return true
        -- Try shifting right
        if not collides_at(position + (nudge, 0), collider, solids):
            position.x += nudge
            return true

    return false  -- blocked, kill vertical velocity
```

---

## Moving Platforms

When standing on a moving platform, the player inherits the platform's velocity:

```
if is_grounded and platform_entity exists:
    platform_vel = get_velocity(platform_entity)
    position += platform_vel * dt
```

Track which platform the player is standing on via ground detection (store the hit entity reference).

---

## Dash / Dodge

A burst of speed in a direction, typically with brief invincibility:

```
function start_dash(controller, velocity, facing):
    if controller.dash_cooldown_timer > 0: return
    controller.is_dashing = true
    controller.dash_timer = dash_duration
    velocity = facing_direction * dash_speed
    -- Optional: grant invincibility frames

function update_dash(controller, velocity, dt):
    if not controller.is_dashing: return
    controller.dash_timer -= dt
    if controller.dash_timer <= 0:
        controller.is_dashing = false
        controller.dash_cooldown_timer = dash_cooldown
        -- Optional: reduce velocity to normal speed
```

---

## Tuning Reference

| Parameter | Typical Range | Notes |
|---|---|---|
| Move speed | 150-250 px/s | Higher for fast-paced games |
| Ground acceleration | 1500-2500 px/s^2 | Higher = snappier |
| Ground deceleration | 2000-3000 px/s^2 | Higher = crisper stops |
| Air acceleration | 800-1500 px/s^2 | Lower than ground for commitment |
| Air deceleration | 400-800 px/s^2 | Low for floaty air control |
| Jump height | 48-96 px | Depends on tile size |
| Time to apex | 0.25-0.45 s | Lower = snappier, higher = floatier |
| Fall gravity multiplier | 1.5-2.5x | Higher = snappier descent |
| Max fall speed | 300-500 px/s | Terminal velocity |
| Coyote time | 0.05-0.15 s | 5-8 frames at 60fps |
| Jump buffer | 0.08-0.15 s | 5-9 frames at 60fps |
| Wall slide speed | 40-80 px/s | Slow descent on wall |
| Dash speed | 400-600 px/s | Burst movement |
| Dash duration | 0.1-0.2 s | Brief burst |

Start with defaults inspired by a game whose feel you admire, then iterate based on playtesting. The numbers are less important than the relationships between them.

> Implementation examples for specific engines are available in the engine-specific modules.
