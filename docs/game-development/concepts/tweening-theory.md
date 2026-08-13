# Tweening & Easing -- Theory & Concepts

This document covers engine-agnostic tweening and easing theory. For engine-specific implementations, see the relevant engine module.

---

## What is Tweening?

Tweening (short for "in-betweening") interpolates a value from A to B over a duration, using an easing curve to control the rate of change. Almost every piece of game juice -- UI animations, combat feedback, camera effects -- is a tween.

### Core Parameters

- **From** -- starting value
- **To** -- ending value
- **Duration** -- time in seconds
- **Easing function** -- curve that maps normalized time [0, 1] to an output value
- **Setter callback** -- function that applies the interpolated value

### Basic Update Loop

```
function tween_update(tween, dt):
    tween.elapsed += dt
    raw_t = clamp(tween.elapsed / tween.duration, 0, 1)
    eased_t = tween.easing(raw_t)
    current = lerp(tween.from, tween.to, eased_t)
    tween.setter(current)

    if tween.elapsed >= tween.duration:
        tween.on_complete()
```

---

## Easing Functions

Every easing function takes `t` in [0, 1] and returns a mapped float. They are pure, stateless, and allocation-free.

### Easing Families

| Family | In | Out | InOut |
|--------|-----|------|-------|
| **Quad** | Slow start, accelerating | Fast start, decelerating | Slow-fast-slow |
| **Cubic** | Steeper acceleration | Steeper deceleration | More pronounced S-curve |
| **Quart/Quint** | Even steeper | Even steeper | Sharper S |
| **Sine** | Gentle, natural | Gentle, natural | Smooth S |
| **Expo** | Near-zero then explosive | Explosive then near-stop | Sharp center |
| **Circ** | Quarter-circle curve | Quarter-circle curve | Half-circle S |
| **Back** | Pulls back before going | Overshoots then settles | Both overshoot |
| **Elastic** | Spring wind-up | Spring overshoot oscillation | Both oscillate |
| **Bounce** | Bouncing lead-in | Bouncing landing | Both bounce |

### Common Formulas

```
Linear(t)     = t
QuadIn(t)     = t * t
QuadOut(t)    = t * (2 - t)
QuadInOut(t)  = t < 0.5 ? 2*t*t : -1 + (4-2*t)*t

CubicIn(t)    = t^3
CubicOut(t)   = (t-1)^3 + 1

SineIn(t)     = 1 - cos(t * PI/2)
SineOut(t)    = sin(t * PI/2)

BackIn(t)     = t*t*((s+1)*t - s)       // s = 1.70158
BackOut(t)    = (t-1)^2*((s+1)*(t-1)+s) + 1

BounceOut(t)  = piecewise quadratic (4 segments)
```

### Choosing an Easing

- **QuadOut** -- good default for most animations (natural deceleration)
- **BackOut** -- bouncy UI elements (menus sliding in)
- **ElasticOut** -- springy effects (item pickups, notifications)
- **SineInOut** -- smooth, subtle transitions
- **ExpoOut** -- screen shake decay, explosive start with gentle stop

---

## Tween Management

### Tween Manager

A central manager maintains a list of active tweens and updates them each frame:

```
function manager_update(dt):
    for i from active_count-1 down to 0:
        tween = tweens[i]
        tween_update(tween, dt)
        if tween.is_complete:
            tweens.remove(i)
            pool.return(tween)
```

### Object Pooling

For games with many simultaneous tweens, pool tween objects to avoid allocations:

- Pre-allocate a batch on startup
- Rent from pool when starting a tween
- Return to pool when complete or cancelled

---

## Tween Features

### Delay

Wait before starting the tween. Useful for staggering animations:

```
// Stagger menu items sliding in
for i in range(5):
    tween(from=-200, to=0, duration=0.4, ease=BackOut, delay=i*0.08)
```

### Looping

| Mode | Behavior |
|------|----------|
| **None** | Play once and complete |
| **Restart** | Reset to start and play again |
| **PingPong** | Reverse direction each cycle (yoyo) |

Loop count: -1 = infinite, N = play N additional times.

### Pause / Resume / Cancel

Tweens should support pausing (freeze elapsed time), resuming, and cancellation (immediate completion or removal).

---

## Chaining and Sequencing

### OnComplete Chaining

The simplest sequencing: start the next tween in the current tween's completion callback.

```
slide = tween(panel_x, -200, 0, 0.4, BackOut)
slide.on_complete = () =>
    tween(text_alpha, 0, 1, 0.3, SineOut)
```

### Tween Sequences

A sequence plays tweens one after another:

```
sequence = new TweenSequence()
sequence.append(slide_tween)
sequence.append(fade_tween)
sequence.on_complete = handle_done
sequence.start()
```

### Tween Groups (Parallel)

A group plays multiple tweens simultaneously and completes when all finish:

```
group = new TweenGroup()
group.add(scale_tween)
group.add(fade_tween)
group.on_complete = handle_done
```

---

## Multi-Type Interpolation

Tweens are not limited to floats. Use generic interpolation for:

- **Vector2** -- position, offset
- **Color** -- tint transitions
- **Rectangle** -- UI element bounds

Provide a lerp function for each type:

```
float_lerp(a, b, t) = a + (b - a) * t
vec2_lerp(a, b, t) = (float_lerp(a.x, b.x, t), float_lerp(a.y, b.y, t))
color_lerp(a, b, t) = (lerp each channel)
```

---

## Common Game Uses

| Use Case | Easing | Notes |
|----------|--------|-------|
| UI slide in | BackOut | Overshoots slightly, feels bouncy |
| UI slide out | CubicIn | Accelerates away |
| Fade in/out | SineOut / SineIn | Gentle, natural |
| Damage number float | QuadOut | Fast start, gentle drift |
| Knockback | ExpoOut | Sharp impact, quick settle |
| Health bar drain | QuadOut | Smooth visual catch-up |
| Hover bob | SineInOut + PingPong | Infinite gentle oscillation |
| Screen shake decay | ExpoOut | Violent start, smooth stop |
| Pickup magnet arc | QuadIn | Accelerates toward player |

---

## Tweens vs Coroutines

| Aspect | Tweens | Coroutines |
|--------|--------|------------|
| Best for | Simple A-to-B interpolation | Complex multi-step sequences |
| State | Minimal (from, to, elapsed) | Full execution frame |
| Allocation | Zero with pooling | Iterator allocation per start |
| Easing | Built-in curve library | Manual lerp + yield |

**Hybrid:** Use coroutines for complex sequences that fire tweens for individual animations.

---

*Implementation examples are available in engine-specific modules.*
