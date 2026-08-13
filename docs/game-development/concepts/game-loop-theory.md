# Game Loop -- Theory & Concepts

This document covers engine-agnostic game loop theory, including fixed vs variable timesteps, accumulator patterns, frame independence, and rendering decoupled from logic. Pseudocode is used throughout. For engine-specific implementations, see the relevant engine module.

---

## The Core Problem

Games must update logic and render visuals in a continuous loop. The challenge: different machines run at different speeds, and even on the same machine, frame duration varies. Without careful design, game behavior becomes non-deterministic -- the same inputs produce different results depending on frame rate.

---

## Fixed vs Variable Timestep

### Variable Timestep

Each update receives the actual elapsed time since the last frame. Logic scales by delta time.

```
function update(dt):
    position += velocity * dt
```

- **Pros** -- Simple, smooth on all frame rates
- **Cons** -- Non-deterministic physics. Floating-point accumulation errors mean two machines running the same inputs diverge over time. High-speed objects can tunnel through walls on slow frames.

### Fixed Timestep

Logic always runs at a fixed interval (e.g., 1/60th of a second), regardless of actual frame rate.

```
function update(dt):
    position += velocity * FIXED_DT
```

- **Pros** -- Deterministic. Same inputs always produce same results. Physics is stable.
- **Cons** -- If the game runs slower than the fixed rate, simulation falls behind. If faster, logic is wasted.

### The Accumulator Pattern (Best of Both)

The standard solution: accumulate real elapsed time and consume it in fixed-size chunks.

```
FIXED_DT = 1 / 60
accumulator = 0

function frame_update(real_dt):
    accumulator += real_dt

    while accumulator >= FIXED_DT:
        simulate(FIXED_DT)
        accumulator -= FIXED_DT

    render()
```

- Logic always steps at a fixed rate (deterministic)
- Rendering runs at the display's native rate (smooth visuals)
- The leftover accumulator fraction can be used for interpolation

---

## Spiral of Death Prevention

If a frame takes too long (e.g., loading a resource, OS interruption), the accumulator grows large. The loop then tries to run many simulation steps to catch up, which makes the next frame even slower, causing more steps -- a feedback loop.

**Solution:** Cap the maximum accumulated time.

```
MAX_ACCUMULATION = FIXED_DT * 4  -- never simulate more than 4 steps

function frame_update(real_dt):
    accumulator += real_dt
    accumulator = min(accumulator, MAX_ACCUMULATION)

    while accumulator >= FIXED_DT:
        simulate(FIXED_DT)
        accumulator -= FIXED_DT
```

This means the game slows down rather than spiraling. In practice, players rarely notice a brief slowdown, but they always notice a freeze followed by a fast-forward.

---

## Interpolation for Smooth Rendering

With a fixed timestep, logic runs at (say) 60 Hz. If the display runs at 120 Hz, half the render frames show the exact same state -- causing micro-stutter.

Interpolation solves this by blending between the previous and current simulation states using the leftover accumulator fraction.

```
alpha = accumulator / FIXED_DT

rendered_position = lerp(previous_position, current_position, alpha)
```

- Adds one frame of visual latency (positions shown are slightly behind true state)
- Eliminates stutter at high refresh rates
- Many 2D games skip interpolation and accept minor visual quantization -- the stutter is less noticeable with pixel art

---

## Variable Render Rate

A common pattern decouples logic rate from display rate:

| Display Rate | Logic Rate | Logic Ticks Per Frame | Visual FPS |
|---|---|---|---|
| 60 Hz | 60 Hz | 1 | 60 |
| 120 Hz | 60 Hz | 0 or 1 | 120 |
| 30 Hz (power save) | 60 Hz | 2 | 30 |

At 120 Hz, roughly half the frames produce zero logic ticks (accumulator has not reached the fixed step yet), but the render pass still runs -- producing smoother visuals from the same game state.

At 30 Hz, each frame runs two logic ticks to keep simulation in sync.

---

## Culling and Batching

### Frustum Culling

Before rendering, test each object's bounds against the camera viewport. Objects entirely outside are skipped. Typically eliminates 50-80% of objects in a scrolling 2D game.

```
function should_render(object, camera_bounds):
    return overlaps(object.bounds, camera_bounds)
```

### Sprite Batching

Accumulate vertex data for all sprites sharing the same render state (texture, shader, blend mode), then issue a single draw call. Batches break when state changes.

**Strategies to minimize batch breaks:**
- Pack sprites into texture atlases
- Sort draw order to group same-texture sprites
- Minimize shader and blend mode changes

### Overdraw

Each pixel drawn on top of another pixel wastes fill rate. Target roughly 1x overdraw:
- Sort opaque sprites front-to-back
- Trim transparent sprite boundaries tightly
- Each full-screen post-processing effect adds 100% overdraw

---

## Common Game Loop Patterns

### Pattern 1: Simple Fixed Timestep

```
while game_running:
    dt = clock.tick(TARGET_FPS)
    process_input()
    update(FIXED_DT)
    render()
```

Simplest approach. Relies on the platform to cap frame rate. Logic always uses the same dt.

### Pattern 2: Accumulator with Interpolation

```
previous_time = now()
accumulator = 0

while game_running:
    current_time = now()
    frame_time = current_time - previous_time
    previous_time = current_time

    accumulator += min(frame_time, MAX_FRAME_TIME)

    while accumulator >= FIXED_DT:
        save_previous_state()
        process_input()
        update(FIXED_DT)
        accumulator -= FIXED_DT

    alpha = accumulator / FIXED_DT
    render(alpha)
```

The gold standard for games requiring deterministic physics with smooth visuals.

### Pattern 3: Semi-Fixed Timestep

```
while game_running:
    dt = clock.elapsed()
    dt = clamp(dt, MIN_DT, MAX_DT)
    process_input()
    update(dt)
    render()
```

A pragmatic middle ground. Clamping prevents extreme dt values while allowing some variation. Acceptable for games without strict physics requirements.

---

## Mobile-Specific Considerations

- **Thermal throttling** -- GPU frequency drops 30-40% under sustained load. Budget for worst-case performance, not peak.
- **Frame rate caps** -- 120 Hz increases display power draw by 30-45% vs 60 Hz. Cap non-action games to 60 Hz.
- **Dynamic quality scaling** -- Adjust resolution, particle counts, and post-processing based on thermal state.

---

## Key Principles

- **Fixed timestep for logic** -- Determinism matters for physics, networking, replays, and debugging
- **Measure before optimizing** -- Profile on target hardware in release builds
- **Focus on p99 frame times** -- Not averages. One 50ms spike per second ruins the feel.
- **Simplest correct solution** -- Often performs adequately
- **Cap accumulated time** -- Prevent the spiral of death

> Implementation examples for specific engines are available in the engine-specific modules.
