# Audio Systems -- Theory & Concepts

This document covers engine-agnostic audio theory for games, including audio architecture, sound pooling, music crossfading, spatial audio, dynamic music, and sound design patterns. Pseudocode is used throughout. For engine-specific implementations, see the relevant engine module.

---

## Audio Architecture

### Category-Based Volume Mixing

Organize sounds into categories (buses) with independent volume controls. The effective volume of any sound is the product of the master volume and its category volume.

```
categories = {
    "music":   0.7,
    "sfx":     1.0,
    "ambient": 0.5,
    "ui":      0.8,
}
master_volume = 1.0

function effective_volume(category):
    return master_volume * categories[category]
```

This lets players independently control music vs effects, and lets the game duck categories during events (e.g., lower music during dialogue).

### Sound vs Music

Two fundamentally different playback models:

| Concern | Sound Effects (SFX) | Music |
|---|---|---|
| Instances | Many simultaneous | One (or layered tracks) |
| Duration | Short (0.1-5 seconds) | Long (minutes) |
| Loading | Fully decoded in memory | Streamed from disk |
| Playback | Fire-and-forget or controlled | Looping, crossfading |

---

## Sound Pooling

Playing many simultaneous sound effects (gunfire, footsteps, impacts) quickly exhausts the platform's audio channel limit. A sound pool manages a fixed set of audio instances.

```
struct SoundPool:
    instances: array[MAX_INSTANCES]
    cursor: int = 0

function play(pool, sound, volume, pitch, pan):
    -- Find a stopped slot
    slot = -1
    for i in 0..pool.instances.length:
        idx = (pool.cursor + i) % pool.instances.length
        if pool.instances[idx] is stopped or null:
            slot = idx
            break

    -- All slots busy: steal the oldest
    if slot == -1:
        slot = pool.cursor
        pool.instances[slot].stop()

    -- Play in the slot
    pool.instances[slot] = sound.create_instance()
    pool.instances[slot].volume = volume
    pool.instances[slot].pitch = pitch
    pool.instances[slot].pan = pan
    pool.instances[slot].play()

    pool.cursor = (slot + 1) % pool.instances.length
```

**Typical pool sizes:**
- 16-32 for most 2D games
- 64+ for action-heavy games with many simultaneous effects

---

## Music Crossfading

Abruptly switching music tracks is jarring. Crossfading blends the old track out while bringing the new track in.

```
struct MusicManager:
    current_track: audio_instance
    next_track: audio_instance
    fade_timer: float
    fade_duration: float
    is_fading: bool

function play_track(manager, new_track, fade_duration = 1.5):
    manager.next_track = new_track.create_instance()
    manager.next_track.volume = 0
    manager.next_track.looping = true
    manager.next_track.play()
    manager.fade_duration = fade_duration
    manager.fade_timer = 0
    manager.is_fading = true

function update(manager, dt, category_volume):
    if not manager.is_fading: return

    manager.fade_timer += dt
    t = clamp(manager.fade_timer / manager.fade_duration, 0, 1)

    if manager.current_track:
        manager.current_track.volume = (1 - t) * category_volume
    manager.next_track.volume = t * category_volume

    if t >= 1:
        manager.current_track.stop()
        manager.current_track = manager.next_track
        manager.next_track = null
        manager.is_fading = false
```

---

## 2D Spatial Audio

In 2D games, spatial audio uses distance-based volume falloff and horizontal panning to convey position.

```
function spatial_audio_2d(source_pos, listener_pos, max_distance):
    distance = dist(source_pos, listener_pos)

    -- Volume: quadratic falloff (sounds more natural than linear)
    volume = clamp(1 - (distance / max_distance), 0, 1)
    volume = volume * volume

    -- Pan: horizontal offset mapped to -1 (left) to 1 (right)
    dx = source_pos.x - listener_pos.x
    pan = clamp(dx / max_distance, -1, 1)

    return (volume, pan)
```

**Falloff curves:**
- **Linear** -- `1 - (d / max)` -- simple but unnatural
- **Quadratic** -- `(1 - d/max)^2` -- sounds more natural, preferred for most games
- **Logarithmic** -- mimics real-world sound propagation, but often too steep for game feel

---

## Dynamic Music (Vertical Layering)

Vertical layering plays multiple synchronized music tracks simultaneously, fading individual layers in and out based on game state.

```
struct LayeredMusic:
    layers: array of audio_instances  -- all same length, loop in sync
    target_volumes: array of float

function start_all(music):
    for layer in music.layers:
        layer.looping = true
        layer.play()             -- all start simultaneously

function set_layer_volume(music, layer_index, target):
    music.target_volumes[layer_index] = clamp(target, 0, 1)

function update(music, dt, lerp_speed = 3):
    for i in 0..music.layers.length:
        music.layers[i].volume = lerp(
            music.layers[i].volume,
            music.target_volumes[i],
            lerp_speed * dt
        )
```

**Example layer assignment:**
- Layer 0: ambient pad (always on)
- Layer 1: percussion (exploration)
- Layer 2: melody (story moments)
- Layer 3: combat strings (fade in during fights)

```
-- Entering combat:
set_layer_volume(music, 3, 1.0)

-- Leaving combat:
set_layer_volume(music, 3, 0.0)
```

---

## Sound Variation

Repeating the exact same sound for recurring events (footsteps, sword swings) sounds robotic. Two techniques fix this:

### Multiple Variants

Provide 3-5 variations of each sound and randomly select one, avoiding immediate repeats.

```
struct SoundVariant:
    variants: array of sounds
    last_index: int = -1

function play(variant, pool, volume, pan):
    -- Pick a random variant, avoiding the last one
    idx = random_int(0, variant.variants.length)
    while idx == variant.last_index and variant.variants.length > 1:
        idx = random_int(0, variant.variants.length)
    variant.last_index = idx

    -- Randomize pitch slightly (+/- 5%)
    pitch = random_float(-0.05, 0.05)

    pool.play(variant.variants[idx], volume, pitch, pan)
```

### Pitch Randomization

Even with a single sample, random pitch variation (+/- 5-10%) prevents the robotic feel.

---

## Impact Layering

Rich impacts come from layering multiple simultaneous sounds for a single game event:

| Layer | Purpose | Example |
|---|---|---|
| **Transient** | Sharp attack, initial crack | Click, snap |
| **Body** | Weight and substance | Thud, crunch |
| **Sweetener** | Character and texture | Shatter, ring, sizzle |
| **Tail** | Decay and space | Reverb, echo |

```
function play_impact(position, listener):
    vol, pan = spatial_audio_2d(position, listener, 800)
    play(transient_sfx, vol * 1.0, pan)
    play(body_sfx,      vol * 0.8, pan)
    play(sweetener_sfx, vol * 0.4, pan)
    -- Tail is typically handled by reverb DSP or a longer sample
```

---

## Audio Buses and Effects (Advanced)

Professional audio middleware (FMOD, Wwise) provides:

- **Bus mixing** -- hierarchical volume control (Master > SFX > Weapons > Rifle)
- **DSP effects** -- reverb, EQ, compression per bus
- **Parameters** -- drive sound behavior with game values (speed, health, surface type)
- **Snapshots** -- preset bus configurations for game states (underwater: muffle everything, boost low-end)

Even without middleware, understanding bus hierarchy helps organize audio code:

```
bus_hierarchy:
    Master
        Music
        SFX
            Weapons
            Footsteps
            Impacts
            UI
        Ambient
            Nature
            Crowd
        Voice
```

---

## Key Principles

- **Pool your sound instances** -- Prevent channel exhaustion and reduce allocation
- **Crossfade music transitions** -- Never hard-cut between tracks
- **Use spatial audio** -- Even simple pan + distance falloff adds immersion in 2D
- **Vary repeated sounds** -- Multiple variants + pitch randomization
- **Layer impacts** -- Transient + body + sweetener for satisfying hits
- **Respect player settings** -- Separate volume controls for music, SFX, and voice at minimum
- **Test on target hardware** -- Audio mixing on desktop speakers sounds nothing like phone speakers

> Implementation examples for specific engines are available in the engine-specific modules.
