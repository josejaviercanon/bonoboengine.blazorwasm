# Animation Systems -- Theory & Concepts

This document covers engine-agnostic 2D animation theory, including sprite animation fundamentals, state machines, transition rules, and layered animation. For engine-specific implementations, see the relevant engine module.

---

## Sprite Animation Fundamentals

A sprite animation is a sequence of frames (sub-rectangles of a sprite sheet) displayed in order with per-frame timing. Key concepts:

- **Sprite sheet** -- a single texture containing all animation frames packed together
- **Animation tag** -- a named animation within the sheet (e.g., "idle", "run", "attack")
- **Frame duration** -- how long each frame displays (set by the artist, typically in milliseconds)
- **Playback modes** -- loop, play-once, ping-pong
- **Playback speed** -- multiplier on frame duration (1.0 = normal, 2.0 = double speed)

### Playback State

```
current_tag         -- which animation is playing
current_frame       -- index within the tag
timer               -- elapsed time on current frame
looping             -- does this animation repeat?
finished            -- true when a non-looping animation completes
playback_speed      -- speed multiplier
```

---

## Animation State Machine

A naive approach checks a dozen boolean flags every frame to pick an animation. A proper state machine has explicit states, typed transitions, and priority.

### State Definition

Each animation state has:

- **Name** -- matches the sprite sheet tag name (e.g., "idle", "run")
- **Loops** -- whether this animation repeats
- **Speed multiplier** -- playback speed for this state
- **Priority** -- higher = harder to interrupt (death > hurt > attack > movement)
- **Lock until complete** -- cannot be interrupted until the animation finishes
- **Fallback state** -- state to transition to when a non-looping animation finishes (e.g., attack falls back to idle)

### Transition Rules

A transition defines:

- **From state** -- which state this transition exits
- **To state** -- which state this transition enters
- **Condition** -- a function that evaluates game state (velocity, grounded, input, etc.)
- **Priority** -- among transitions from the same state, higher priority is checked first

### Evaluation Logic

```
function evaluate(current_state, context):
    if current_state.lock_until_complete and not context.animation_finished:
        return null    // locked

    for each transition from current_state (sorted by priority descending):
        if transition.condition(context):
            return transition.to_state

    if context.animation_finished and current_state.fallback:
        return current_state.fallback

    return null    // no transition
```

### Global Transitions

Some transitions can trigger from any state (e.g., hurt, death). Register them as transitions from every state to the target state, typically with high priority.

### Example: Platformer State Machine

```
States:
  idle     -- loops, priority 0
  run      -- loops, priority 0
  jump     -- no loop, priority 1, fallback: fall
  fall     -- loops, priority 1
  land     -- no loop, priority 2, lock, fallback: idle
  attack   -- no loop, priority 3, lock, fallback: idle
  hurt     -- no loop, priority 5, lock, fallback: idle
  death    -- no loop, priority 10, lock, no fallback

Transitions:
  idle -> run:     abs(velocity.x) > threshold AND grounded
  run -> idle:     abs(velocity.x) <= threshold AND grounded
  idle -> jump:    just_jumped
  run -> jump:     just_jumped
  jump -> fall:    velocity.y > 0 OR animation_finished
  idle -> fall:    not grounded
  run -> fall:     not grounded
  fall -> land:    grounded
  idle -> attack:  attack_pressed
  run -> attack:   attack_pressed
  * -> hurt:       hurt_this_frame (global, priority 5)
  * -> death:      is_dead (global, priority 10)
```

---

## Separation of State and Rendering

Keep animation state (what is playing, frame index, timing) separate from the rendering object (sprite sheet, texture). This allows:

- Game logic to read/write animation state without touching rendering
- AI systems to set animations without knowing about sprites
- Serialization and networking of animation state

---

## Directional Animation

For games with multi-directional sprites (top-down RPGs, 8-direction movement):

### Approaches

1. **Direction suffix** -- Append direction to tag name: "walk_down", "walk_up", "walk_left", "walk_right"
2. **Horizontal flip** -- Use one set of animations and flip the sprite horizontally for left/right
3. **Direction component** -- Store facing direction separately; the animation system resolves the correct tag

### Direction Resolution

```
function resolve_animation_name(base_name, facing):
    suffix = direction_to_suffix(facing)    // "down", "up", "left", "right"
    full_name = base_name + "_" + suffix
    if tag_exists(full_name):
        return full_name
    // Fallback: try horizontal flip of opposite direction
    return base_name + "_right" with horizontal_flip = (facing == left)
```

---

## Frame Events

Trigger gameplay actions on specific animation frames:

- **Attack hitbox activation** -- enable the hitbox on the "swing" frame, disable after
- **Footstep sounds** -- play a sound when the foot contacts the ground
- **Particle emission** -- spawn dust when landing
- **Projectile spawning** -- fire a projectile on the "release" frame

### Implementation Approaches

1. **Frame index callbacks** -- register callbacks for specific frame indices
2. **Animation event markers** -- embed event data in the animation file (supported by tools like Aseprite)
3. **Time-window checks** -- check if the current frame is within a range

---

## Priority System

Not all animations are equal. A death animation should never be interrupted by idle. Priority rules:

- Each state has a numeric priority
- A transition to a lower-priority state cannot interrupt a locked higher-priority state
- Global transitions (hurt, death) use high priority to override anything

Typical priority scale:
```
0: idle, run (movement)
1: jump, fall (aerial)
2: land (brief, locked)
3: attack (locked until complete)
5: hurt (overrides attack)
10: death (overrides everything)
```

---

## Layered Animation

Some characters need multiple simultaneous animations:

- **Base layer** -- legs/body movement (idle, walk, run)
- **Upper body layer** -- torso/arms (aiming, shooting, waving)
- **Overlay layer** -- effects (hit flash, status icons)

Each layer plays its own animation independently. During rendering, layers composite together. The upper body overrides the corresponding sprite regions while legs play the base animation.

---

## Sprite Flash (Hit Feedback)

When an entity takes damage, briefly replace all sprite colors with white (or another color) for 1--3 frames. This is handled as a rendering modifier:

```
if flash_active:
    draw sprite with solid white tint
else:
    draw sprite normally
```

Flash duration is typically 0.05--0.1 seconds. Can also be implemented as a shader that lerps toward a flash color.

---

## Best Practices

- **One definition per character type** -- the state machine definition is shared; per-entity state lives in a component
- **Keep state machines small** -- 5--10 states covers most characters; more suggests the design may need simplification
- **Author timing in your art tool** -- frame durations set by the artist are authoritative; override in code only when necessary
- **Consistent canvas sizes** -- all frames should be the same size to avoid origin shifting between animations

---

*Implementation examples are available in engine-specific modules.*
