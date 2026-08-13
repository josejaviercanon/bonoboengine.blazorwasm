# Particle Systems -- Theory & Concepts

This document covers engine-agnostic particle system theory for 2D games. The concepts apply to any engine or framework. For engine-specific implementations, see the relevant engine module.

---

## Two Fundamental Approaches

| Approach | When to Use | Typical Scale |
|----------|-------------|---------------|
| **Struct pool** (flat array) | Fire-and-forget visual effects, high particle counts, short-lived | 500--10,000 per frame |
| **Entity-based** (ECS entities or game objects with particle components) | Particles that interact with game systems (collision, physics, damage) | 50--500 per frame |

Most games use the **struct pool** for visual effects (sparks, smoke, blood) and **entity-based particles** for gameplay-relevant projectiles that happen to look like particles.

---

## Particle Properties

Each particle typically carries:

- **Position** -- world-space location
- **Velocity** -- movement direction and speed
- **Acceleration** -- forces like gravity or wind
- **Rotation** and **angular velocity** -- spin
- **Scale** and **scale velocity** -- size change over lifetime (shrink or grow)
- **Color start / color end** -- interpolated over lifetime
- **Lifetime** -- total duration in seconds
- **Elapsed** -- time alive so far
- **IsAlive** -- whether the particle is active

**Normalized lifetime (t):** `t = elapsed / lifetime` gives a 0-to-1 value used for interpolating color, scale, and alpha.

---

## Pool-Based Particle System

A fixed-size array with zero allocations after construction. No garbage collection pressure in steady state.

### Core Update Loop

```
for i from active_count-1 down to 0:
    p = particles[i]
    p.elapsed += dt

    if p.elapsed >= p.lifetime:
        // Swap with last active particle, shrink pool
        particles[i] = particles[active_count - 1]
        active_count -= 1
        continue

    // Physics integration
    p.velocity += p.acceleration * dt
    p.position += p.velocity * dt
    p.rotation += p.angular_velocity * dt
    p.scale += p.scale_velocity * dt
```

### Core Draw Loop

```
for i from 0 to active_count:
    p = particles[i]
    t = p.elapsed / p.lifetime
    color = lerp(p.color_start, p.color_end, t)
    draw_sprite(texture, p.position, color, p.rotation, p.scale)
```

---

## Emitter Patterns

### Burst Emission

Emit N particles at once. Used for explosions, impacts, item pickups.

```
function emit_burst(pool, position, count):
    for i in range(count):
        angle = random(0, 2*PI)
        speed = random(min_speed, max_speed)
        particle = create_particle(
            position = position,
            velocity = (cos(angle), sin(angle)) * speed,
            ...
        )
        pool.emit(particle)
```

### Continuous (Stream) Emission

Emit particles at a steady rate. Used for fire, smoke, trails.

```
accumulator += dt
while accumulator >= emission_interval:
    accumulator -= emission_interval
    emit_one_particle()
```

The accumulator pattern ensures consistent emission rate regardless of frame rate.

---

## Blending Modes

| Blend Mode | Use Case |
|------------|----------|
| **Alpha blend** | Smoke, dust, debris -- standard transparency |
| **Additive** | Fire, sparks, magic, explosions -- colors brighten, no overdraw cost |

**Mixed blending:** If you need both additive and alpha particles, use two draw passes grouped by blend mode to minimize state changes.

---

## Particle Textures

- **White circle** (4x4 to 16x16): Tinted by color at draw time. Most versatile
- **Soft gradient circle**: Smooth falloff, good for smoke and glow
- **Spark/diamond**: Elongated, good for sparks and trails
- **1x1 white pixel**: For pixel art games, scaled up

Tint white textures with the particle's color for maximum flexibility -- one texture serves all effects.

---

## Common Effect Recipes

### Explosion
- Burst: 30--60 particles
- Velocity: random direction, 100--300 speed
- Acceleration: downward gravity
- Color: orange to dark smoke, fade out
- Scale: shrink over time
- Lifetime: 0.2--0.6s

### Fire
- Stream: 40--80 particles/sec
- Velocity: upward with horizontal wobble
- Color: yellow-orange to transparent red
- Scale: shrink
- Lifetime: 0.4--0.8s

### Smoke
- Stream: 10--20 particles/sec, alpha blend
- Velocity: slow upward drift
- Scale: grow over time
- Color: gray to transparent
- Lifetime: 1.0--2.0s

### Trail
- Stream: 30--60 particles/sec, additive blend
- Velocity: zero (stays where spawned)
- Scale: shrink
- Lifetime: 0.2--0.4s

---

## Production Patterns

### Budget-Based Emission

When many emitters compete for a shared pool, pre-count demand before emitting and apply a throttle factor. Weight demand by visual importance.

### Pool Load Throttle

Reduce emission rate as the pool fills up to avoid starvation:

```
pool_load = active_count / capacity
throttle = 1.0 if load < 0.2,  0.65 if load < 0.4,  0.35 if load < 0.65,  0.15 otherwise
```

### Kill Bounds

Particles that drift off-screen waste pool slots. Set kill bounds from camera bounds plus a margin. Force-kill any particle outside those bounds.

### Velocity Delay Ramp

Fire particles look better when they "grow in place" before drifting upward. Add a delay period where velocity ramps from 0 to full:

```
vel_scale = (elapsed < velocity_delay) ? elapsed / velocity_delay : 1.0
position += velocity * vel_scale * dt
```

### Frustum-Culled Draw

Update processes all particles (physics must be correct), but draw can skip off-screen particles. Pass camera bounds plus padding for particle size.

### Cached Interpolation

Compute normalized lifetime `t`, interpolated size, and alpha once during update. Read cached values in draw to avoid recomputing across multiple render passes.

---

## Performance Guidelines

| Metric | Desktop Budget | Mobile Budget |
|--------|---------------|---------------|
| Active particles | 5,000--10,000 | 500--2,000 |
| Texture size | 16x16 to 64x64 | 4x4 to 16x16 |
| Draw calls | 1--3 (by blend mode) | 1--2 |

**Tips:**
- Pool overflow silently drops new particles -- acceptable for visual effects
- Profile draw separately from update; draw is usually the bottleneck (GPU fill rate)
- On mobile, reduce emission rates and lifetimes by 50% compared to desktop

---

*Implementation examples are available in engine-specific modules.*
