# P11 · Polish & Juice Checklist



> *"Juice is the non-essential visual, audio, and haptic feedback that makes a game **feel** incredible."*

This checklist is your polish-phase companion. Work through it systematically after your core gameplay loop is solid. Every checkbox is a small thing on its own — stacked together, they're the difference between "functional prototype" and "this feels amazing."

**Key references:**
- [Game Feel Tooling](../../monogame-arch/guides/G30_game_feel_tooling.md) — engine-level systems that power juice
- [Game Feel & Genre Craft](../game-design/C2_game_feel_and_genre_craft.md) — design philosophy behind why this matters

---

## 1 · What Is Juice?

Juice is the **feedback layer** between player input and game response. A functional game registers a hit. A juicy game makes you *feel* the hit — the screen shakes, the enemy flashes white, particles burst, time freezes for two frames, and a crunchy sound plays. Same mechanic, completely different experience.

**Games that nail it:**
- **Celeste** — screen shake on dash, squash/stretch on landing, particles everywhere, camera that breathes with the player
- **Hollow Knight** — hitstop on nail strikes, directional recoil, enemy flash, camera shake tuned per-attack
- **Vlambeer games** (Nuclear Throne, Luftrausers) — the textbook. Screen shake, massive particles, aggressive camera, additive recoil. Vlambeer coined the modern vocabulary of juice.

### The 3 Pillars of Juice

| Pillar | What It Does | Example |
|---|---|---|
| **Visual Feedback** | Shows the player something happened | White flash, particles, squash/stretch |
| **Audio Feedback** | Confirms impact through sound | Crunchy hit SFX, pitch-varied footsteps |
| **Camera Response** | The world reacts physically | Screen shake, zoom, slow-mo |

Great juice layers all three simultaneously. A sword hit should flash the enemy (visual), play an impact sound (audio), *and* shake the camera (camera). One pillar alone feels thin. All three together feels unstoppable.

> **Rule of thumb:** If a player action has no feedback from at least 2 of the 3 pillars, it will feel flat. — See [C2: Game Feel & Genre Craft](../game-design/C2_game_feel_and_genre_craft.md)

---

## 2 · Screen Shake

**When to use:** Impacts, explosions, heavy landings, boss attacks, environmental destruction.

**Intensity:** 🔥 Dramatic for combat. Subtle for movement.

> Reference: [G20: Camera Systems](../../monogame-arch/guides/G20_camera_systems.md)

### Checklist

- [ ] **Basic shake on hit** — Camera offsets randomly on player/enemy damage
- [ ] **Directional shake** — Shake biased in the direction of impact (not just random)
- [ ] **Trauma-based system** — Accumulate "trauma" (0.0–1.0), shake intensity = trauma². Trauma decays over time. This prevents over-shaking from rapid hits
- [ ] **Explosion shake** — Larger trauma value, longer decay
- [ ] **Landing shake** — Brief, vertical-only shake when falling from height
- [ ] **Configurable per-event** — Different shake profiles for different events (light hit vs boss slam)
- [ ] **Player option to reduce/disable** — Accessibility. Always.

### Implementation Pattern

```
trauma = min(1.0, trauma + amount)
offset.x = max_offset * trauma² * random(-1, 1)
offset.y = max_offset * trauma² * random(-1, 1)
rotation = max_rotation * trauma² * random(-1, 1)
trauma = max(0, trauma - decay * delta)
```

### Tuning Guide

| Event | Trauma | Decay Rate | Notes |
|---|---|---|---|
| Light hit | 0.15–0.25 | Fast (5.0/s) | Barely noticeable but present |
| Heavy hit | 0.4–0.6 | Medium (3.0/s) | Player should feel it |
| Explosion | 0.7–1.0 | Slow (1.5/s) | Dramatic, screen-filling |
| Landing | 0.1–0.2 | Fast (6.0/s) | Quick vertical pulse |

> ⚠️ **Don't overdo it.** If the screen is always shaking, nothing feels impactful. Reserve strong shake for moments that matter. Vlambeer-level shake is a stylistic choice, not a default.

---

## 3 · Hitstop / Freeze Frames

**When to use:** Melee hits, heavy landings, boss attacks, parries, critical strikes.

**Intensity:** 🔥 Dramatic. This is one of the highest-impact techniques.

> Reference: [G15: Game Loop](../../monogame-arch/guides/G15_game_loop.md) — understanding the update loop is critical for clean hitstop

### Checklist

- [ ] **Basic hitstop on attack connect** — Freeze game for 2–5 frames on hit
- [ ] **Variable duration by attack weight** — Light attack = 2 frames, heavy = 4–5 frames
- [ ] **Boss attack hitstop** — Longer freeze (5–8 frames) on boss signature moves
- [ ] **Parry/perfect block freeze** — Extended freeze (6–10 frames) to reward timing
- [ ] **Attacker-only vs world freeze** — Decide: freeze everything, or just the attacker and target?
- [ ] **Landing hitstop** — 1–2 frame pause on heavy landing (pairs with squash)
- [ ] **Death freeze** — Brief pause (5–10 frames) when killing the last enemy or a boss

### Implementation Pattern

```
# On hit:
freeze_timer = freeze_frames / target_fps  # e.g., 3/60 = 0.05s

# In game loop:
if freeze_timer > 0:
    freeze_timer -= delta
    return  # Skip game update, still render
# else: normal update
```

**Key detail:** During hitstop, you should still process input buffering and render. The player should be able to queue their next action during the freeze. Only the game simulation pauses — see [G15](../../monogame-arch/guides/G15_game_loop.md) for separating update from render.

> **The test:** If a melee attack feels "clicky" and weightless, add 3 frames of hitstop. Instant improvement.

---

## 4 · Particles

**When to use:** Everywhere something happens physically. Impacts, movement, destruction, ambiance.

**Intensity:** Varies — dramatic for combat, subtle for ambiance.

> Reference: [G23: Particle Systems](../../monogame-arch/guides/G23_particles.md)

### Checklist

- [ ] **Impact particles** — Burst of 3–8 particles on hit (directional, away from impact)
- [ ] **Dust on landing** — Small puff at feet when landing from a jump
- [ ] **Run dust** — Kick-up particles while running (every N frames, not every frame)
- [ ] **Jump dust** — Burst at feet on jump takeoff
- [ ] **Wall-slide particles** — Dust/sparks trailing down while wall-sliding
- [ ] **Dash trail** — Afterimage or streak particles during dash
- [ ] **Death explosion** — Satisfying burst when enemies die (5–15 particles)
- [ ] **Projectile trails** — Faint trail behind bullets/arrows/spells
- [ ] **Collectible pickup burst** — Sparkle/pop when grabbing items
- [ ] **Ambient floating particles** — Dust motes, leaves, embers, snow (scene-dependent)
- [ ] **Heal/buff particles** — Rising sparkles on heal, aura particles on buff

### Design Tips

- Keep particle sprites **simple**: 2–4 frame animations, often just circles or squares
- Use **velocity inheritance** — particles should move in the direction of the action
- **Fade out**, don't pop out — alpha tween to 0 over lifetime
- **Randomize** size, speed, and lifetime slightly for organic feel
- Pool and recycle particles — see [G23](../../monogame-arch/guides/G23_particles.md) for object pooling patterns

---

## 5 · Squash & Stretch

**When to use:** Character movement — jumping, landing, getting hit, bouncing.

**Intensity:** 🎯 Subtle. ±10–20% scale. More than that looks rubbery unless it's your art style.

### Checklist

- [ ] **Jump stretch** — On jump start: scale to ~(0.85, 1.2) — narrower and taller
- [ ] **Fall stretch** — While falling fast: gradual stretch toward (0.9, 1.15)
- [ ] **Land squash** — On landing: scale to ~(1.2, 0.8) — wider and shorter
- [ ] **Hit squash** — On taking damage: brief squash (1.15, 0.85)
- [ ] **Bounce squash** — On bouncing off walls/surfaces
- [ ] **Recovery tween** — Always tween back to (1.0, 1.0) using ease-out-elastic or ease-out-back
- [ ] **Attack anticipation** — Slight squash before attack swing (wind-up)
- [ ] **Dash stretch** — Horizontal stretch during dash (1.2, 0.85)

### Implementation Pattern

```
# On land:
sprite.scale = (1.2, 0.8)
tween(sprite.scale, (1.0, 1.0), 0.15, ease_out_elastic)

# On jump:
sprite.scale = (0.85, 1.2)
tween(sprite.scale, (1.0, 1.0), 0.2, ease_out_back)
```

**Key principle:** Scale X and Y inversely to preserve apparent volume. If you stretch Y by +20%, squash X by -15–20%. This reads as elastic deformation, not resizing.

> **The test:** Record your character jumping and landing with/without squash-stretch. Without it, movement feels stiff. With it, movement feels alive.

---

## 6 · Camera Effects

**When to use:** Always. The camera is the player's window — it should feel alive.

**Intensity:** 🎯 Subtle for movement. 🔥 Dramatic for events.

> Reference: [G20: Camera Systems](../../monogame-arch/guides/G20_camera_systems.md)

### Checklist

- [ ] **Smooth follow** — Camera lerps to target, never snaps (lerp factor 0.05–0.15)
- [ ] **Lookahead** — Camera leads slightly in the direction the player faces/moves
- [ ] **Deadzone** — Small zone where player moves without camera following (reduces jitter)
- [ ] **Vertical deadzone** — Larger vertical deadzone to avoid jitter from small jumps
- [ ] **Landing snap** — Camera catches up faster after a long fall (increase lerp temporarily)
- [ ] **Zoom on events** — Slight zoom-in on boss encounters, zoom-out for large arenas
- [ ] **Slow-mo zoom** — Zoom in during slow-motion moments (kill cams, critical hits)
- [ ] **Scene transition lerp** — Camera smoothly pans to new focus on room/scene transitions
- [ ] **Camera bounds** — Camera stops at room edges (no void visible)
- [ ] **Boss camera** — Frame both player and boss, possibly wider FOV

### Tuning

| Parameter | Typical Value | Notes |
|---|---|---|
| Follow lerp | 0.08–0.12 | Lower = smoother, higher = snappier |
| Lookahead distance | 40–80px | Based on movement speed |
| Deadzone size | 16–32px | Small enough to feel responsive |
| Zoom speed | 0.5–1.0s tween | Never instant zoom |

---

## 7 · Tweens & Easing

**When to use:** Every value change that the player can see. Nothing should snap linearly.

**Intensity:** 🎯 Subtle to moderate. The player shouldn't notice tweens — they should notice when tweens are *missing*.

> Reference: [G41: Tweening](../../monogame-arch/guides/G41_tweening.md)

### Checklist

- [ ] **UI panel slide-in** — Menus ease in from edge (ease-out-back for bounce)
- [ ] **Health bar tween** — HP bar doesn't snap; it drains smoothly (ease-in-out-quad)
- [ ] **Damage number pop** — Numbers scale up, float upward, fade out
- [ ] **Score counter** — Numbers count up, not snap (ease-out-quad)
- [ ] **Item bob** — Collectibles gently bob up/down (sine wave, not tween — but same principle)
- [ ] **Button scale on hover** — Buttons grow ~5% on hover (ease-out-back)
- [ ] **Button squash on press** — Scale to 0.9 on press, 1.0 on release (ease-out-elastic)
- [ ] **Tooltip fade-in** — Tooltips alpha tween in, slight Y offset up
- [ ] **Screen fade** — Scene changes fade through black (ease-in-out-quad)
- [ ] **Notification slide** — Achievements/pickups slide in from edge, pause, slide out

### Common Curves

| Curve | Feel | Best For |
|---|---|---|
| `ease-out-back` | Overshoot + settle | UI panels appearing, popups |
| `ease-out-elastic` | Bouncy spring | Squash/stretch recovery, playful UI |
| `ease-in-out-quad` | Smooth start/stop | Health bars, camera movement |
| `ease-out-quad` | Fast start, gentle stop | Numbers counting up |
| `ease-in-cubic` | Slow start, fast end | Things flying off-screen |

> **Golden rule:** If something moves linearly, it looks robotic. Ease-out for things appearing. Ease-in for things leaving. Ease-in-out for things traveling.

---

## 8 · Screen Transitions

**When to use:** Every scene change, level transition, death/respawn, menu open/close.

**Intensity:** 🎯 Moderate. Should feel intentional, not flashy.

> Reference: [G42: Screen Transitions](../../monogame-arch/guides/G42_screen_transitions.md)

### Checklist

- [ ] **Fade to black** — The default. Fade out → load → fade in. 0.3–0.5s each direction
- [ ] **Circle close** — Iris-wipe closing on player position. Classic for death/level complete
- [ ] **Wipe** — Directional wipe (left-to-right, diagonal). Good for level-to-level
- [ ] **Pixelate** — Resolution drops to chunky pixels, transitions, restores. Retro feel
- [ ] **Diamond/shape wipe** — Expanding diamond from center. Zelda-style
- [ ] **Color flash** — Brief white/color flash before transition (impact moments)
- [ ] **No naked cuts** — Audit: does any scene change happen without a transition? Fix it

### Implementation Notes

- Transitions should be **scene-independent** — a transition manager that overlays on top
- Always **load during the opaque phase** (while screen is fully black/covered)
- Match transition style to game tone: pixel games → pixelate/iris. Modern → fade/wipe
- Keep total transition time under 1 second unless it's narrative (cutscene entry)

---

## 9 · UI Juice

**When to use:** Every UI element the player interacts with or sees change.

**Intensity:** 🎯 Subtle. UI juice should feel polished, not distracting.

> Reference: [G5: UI Framework](../../monogame-arch/guides/G5_ui_framework.md)

### Checklist

- [ ] **Button hover scale** — Grow 5–10% on hover (ease-out-back, ~0.1s)
- [ ] **Button press squash** — Scale to 0.9 on click, spring back (ease-out-elastic)
- [ ] **Menu item stagger** — List items slide in one-by-one with 30–50ms delay between each
- [ ] **Score pop** — Score text scales up briefly on change, then settles
- [ ] **Damage numbers** — Float upward, slight random X offset, fade out over 0.5–0.8s
- [ ] **Critical damage numbers** — Larger, different color, maybe shake
- [ ] **Health bar drain** — Smooth tween. Bonus: show a "ghost bar" that drains slower (white bar behind red bar)
- [ ] **Health bar shake** — Brief shake on the health bar itself when taking damage
- [ ] **Pickup notification** — Item icon + name slides in from edge, holds 2s, slides out
- [ ] **Inventory flash** — Brief highlight/glow on the inventory slot when item is added
- [ ] **XP bar fill** — Animate the fill with ease-out, flash on level-up
- [ ] **Combo counter** — Escalating size/color as combo increases, shake at high counts

---

## 10 · Audio Juice

**When to use:** Everywhere. Audio is 50% of game feel and the most underinvested area in indie games.

**Intensity:** 🔥 Dramatic impact. Audio feedback is almost never "too much."

> Reference: [G6: Audio](../../monogame-arch/guides/G6_audio.md)

### Checklist

- [ ] **Pitch variation on repeated sounds** — ±5–15% random pitch on footsteps, hits, coins. Prevents machine-gun repetition
- [ ] **Layered SFX** — Heavy attacks play 2–3 sounds layered: impact + swoosh + bass thud
- [ ] **Impact sync** — Impact sounds timed exactly with visual contact frame, not animation start
- [ ] **UI click sounds** — Every button press, hover, and menu action has a sound
- [ ] **UI confirm/cancel** — Distinct sounds for confirm vs back/cancel
- [ ] **Music ducking** — Lower music volume during important SFX (boss roar, dialogue)
- [ ] **Low-pass filter on pause** — Music goes muffled when game pauses
- [ ] **Off-screen audio cues** — Enemies off-screen have faint, directional audio hints
- [ ] **Environmental audio** — Wind, water, fire ambiance. Fades by proximity
- [ ] **Death sound** — Satisfying enemy death pop/crunch. Equally important: player death sound
- [ ] **Collectible jingle** — Ascending pitch for consecutive pickups (coin 1 = C, coin 2 = D, coin 3 = E...)
- [ ] **Landing sound** — Volume/pitch scales with fall distance
- [ ] **No silent actions** — Audit: does any player action have zero audio feedback? Fix it

### Audio Juice Quick Wins

1. Add pitch variation to your 3 most common sounds — instant improvement
2. Layer a bass "thud" under your main attack sound
3. Add UI click sounds to all buttons
4. Put a low-pass filter on music when paused

---

## 11 · Color & Flash

**When to use:** Damage, pickups, state changes, emphasis moments.

**Intensity:** 🔥 Dramatic but brief. Flashes should be 1–3 frames max.

### Checklist

- [ ] **White flash on hit** — Entity turns fully white for 1–2 frames on damage. The single most impactful visual juice technique
- [ ] **Damage tint** — After white flash, brief red tint that fades (0.1–0.2s)
- [ ] **Invincibility flash** — Rapid alpha toggle (visible/invisible every 3–4 frames) during i-frames
- [ ] **Collectible glow** — Items pulse gently (sine wave on alpha or additive blend)
- [ ] **Heal flash** — Green tint or white flash on healing
- [ ] **Critical hit flash** — Longer/brighter flash than normal hit, maybe yellow/orange
- [ ] **Screen flash** — Brief white overlay (alpha 0.3–0.5, fade over 0.1s) on explosions
- [ ] **Background color shift** — Subtle BG color change on boss phase transitions or events
- [ ] **Outline pulse** — Important objects pulse their outline color

### Implementation Pattern: White Flash

```
# Shader approach (preferred):
# On hit, set a "flash" uniform to 1.0
# In fragment shader: mix(texture_color, white, flash_amount)
# Tween flash_amount from 1.0 to 0.0 over 0.05–0.1s

# Non-shader approach:
# Swap to a pre-made white silhouette sprite for 1–2 frames
# Then swap back to normal sprite
```

> **Why white flash works:** It's a universal "something happened" signal. Players process it instantly. See [G30: Game Feel Tooling](../../monogame-arch/guides/G30_game_feel_tooling.md) for shader-based flash implementations.

---

## 12 · Time Manipulation

**When to use:** Kill moments, critical hits, boss transitions, death.

**Intensity:** 🔥 Dramatic. Time manipulation is a power move — use it for big moments only.

### Checklist

- [ ] **Kill slow-mo** — Brief 0.1–0.2s slow-motion (timescale 0.3) on killing the last enemy
- [ ] **Hit slow-mo** — Tiny slow-mo (0.05s, timescale 0.5) on critical hits
- [ ] **Speed ramp** — Slow → fast transition: slow-mo that accelerates back to normal (ease-in)
- [ ] **Death pause** — Freeze 0.3–0.5s on player death before game-over sequence
- [ ] **Boss intro slow-mo** — Brief slow-mo + zoom when boss appears
- [ ] **Parry time-stop** — Extended freeze on perfect parry (distinct from hitstop — this is 0.2–0.5s)
- [ ] **Combo finisher** — Slow-mo on the final hit of a combo chain

### Implementation Notes

- Time manipulation affects your **game delta**, not real time. UI, input, and audio should still run at normal speed
- Use a global `time_scale` multiplier on your delta time — see [G15: Game Loop](../../monogame-arch/guides/G15_game_loop.md)
- Tween `time_scale` back to 1.0 — don't snap it. Ease-in-quad feels natural
- **Layer with zoom:** Slow-mo + slight camera zoom-in = cinematic kill moment
- **Don't slow audio pitch** unless it's an intentional effect. Duck volume instead

---

## 13 · Post-Processing

**When to use:** Sparingly. Post-processing is seasoning, not the main course.

**Intensity:** ⚡ Minimal to subtle. Heavy post-processing is the #1 "indie game that looks amateur" tell.

> Reference: [G27: Shaders & Effects](../../monogame-arch/guides/G27_shaders_and_effects.md)

### Checklist

- [ ] **Bloom on bright elements** — Subtle glow on fire, magic, collectibles, UI highlights
- [ ] **Chromatic aberration on hit** — Very brief, very subtle RGB split on damage (0.05s, 1–2px offset)
- [ ] **Vignette** — Subtle darkening at screen edges. Draws focus to center
- [ ] **Damage vignette** — Red/dark vignette pulse on taking damage
- [ ] **Low-health vignette** — Persistent subtle red vignette when HP is low
- [ ] **CRT/scanline filter** — For retro-styled games only. Optional toggle
- [ ] **Color grading** — Consistent color palette shift per biome/level (warm for desert, cool for ice)
- [ ] **Desaturation on death** — Drain color when player dies (tween saturation to 0 over 0.5s)

### ⚠️ Post-Processing Pitfalls

- **Chromatic aberration:** 1–2 pixels max, for 1–3 frames. Permanent CA looks like a broken lens
- **Bloom:** Should enhance, not wash out. If everything glows, nothing glows
- **Shake + CA + bloom** all at once = visual noise. Pick one emphasis effect per moment
- **Performance:** Post-processing hits GPU. Profile on target hardware
- **Always offer toggles** for accessibility

---

## 14 · Environmental Polish

**When to use:** Once core juice is done. Environmental polish makes the *world* feel alive.

**Intensity:** 🎯 Subtle. Environmental effects are ambient — they shouldn't compete with gameplay.

> Reference: [G57: Weather Effects](../../monogame-arch/guides/G57_weather_effects.md)

### Checklist

- [ ] **Grass/foliage sway** — Plants bend when player walks through, spring back
- [ ] **Water ripples** — Circles expand from player/objects entering water
- [ ] **Destructible props** — Crates, pots, barrels break with particles and sound
- [ ] **Ambient wildlife** — Birds fly away when player approaches, butterflies flutter, fish jump
- [ ] **Wind effects** — Parallax layers shift slightly, particles drift, foliage leans
- [ ] **Torch/light flicker** — Point lights pulse subtly (noise-based, not sine — sine looks mechanical)
- [ ] **Dust motes** — Floating particles in indoor/cave scenes
- [ ] **Rain splashes** — Impact particles on surfaces during rain
- [ ] **Footprints/trails** — Temporary marks in snow, sand, mud
- [ ] **Interactable background** — Chains swing, banners wave, things react to player proximity

### Design Philosophy

Environmental polish rewards *observation*. The player who walks slowly through a forest and sees birds scatter, grass sway, and dust motes float feels like the world is real. It doesn't affect gameplay — it affects *immersion*.

> See [C2: Game Feel & Genre Craft](../game-design/C2_game_feel_and_genre_craft.md) for how environmental feedback ties into genre expectations.

---

## 15 · The Polish Priority List

Not everything is equal. When you're a solo dev with limited time, polish in this order — highest impact per effort first:

| Priority | Technique | Effort | Impact | Why |
|---|---|---|---|---|
| **1** | Screen shake | 🟢 Low | 🔴 Huge | 10 lines of code, transforms combat feel |
| **2** | Hitstop | 🟢 Low | 🔴 Huge | 5 lines of code, sells every hit |
| **3** | Particles on impact | 🟡 Medium | 🔴 Huge | Need a particle system, but basic one is quick |
| **4** | Sound effects | 🟡 Medium | 🔴 Huge | Sourcing/making sounds takes time, but audio is 50% of feel |
| **5** | Squash & stretch | 🟢 Low | 🟠 High | Simple scale tweens, big character feel improvement |
| **6** | UI tweens | 🟡 Medium | 🟠 High | Need tween system, then apply everywhere |
| **7** | Camera feel | 🟡 Medium | 🟠 High | Smooth follow + deadzone + lookahead |
| **8** | Screen transitions | 🟡 Medium | 🟡 Medium | Eliminates jarring cuts between scenes |
| **9** | Post-processing | 🟡 Medium | 🟡 Medium | Needs shaders. Easy to overdo. Be disciplined |
| **10** | Environmental | 🔴 High | 🟡 Medium | Lots of small systems. Do this last |

### The 80/20 Rule of Juice

Items 1–5 get you 80% of the feel with 20% of the effort. If you only have one weekend for polish, do these five things:

1. Add trauma-based screen shake to hits
2. Add 3-frame hitstop to your main attack
3. Burst 5 particles on every impact
4. Add pitch variation to your hit sound
5. Add squash on landing, stretch on jump

Your game will feel **dramatically** better. Everything else is refinement.

---

## 16 · The "Before & After" Test

The ultimate validation of your polish work:

### How to Do It

1. **Record 10 seconds** of core gameplay with ALL juice disabled (no shake, no particles, no hitstop, no tweens)
2. **Record the same 10 seconds** with all juice enabled
3. **Show both clips to someone** who hasn't seen your game
4. Ask: "Which feels better?" (They will always pick the juicy version)
5. Ask: "What's different?" (They usually can't articulate it — it just "feels better")

### What This Proves

- Juice is largely **subconscious**. Players don't think "nice screen shake" — they think "this game feels great"
- The delta between the two clips should be **dramatic**. If it's not, you haven't juiced enough
- This test also reveals **over-juicing**: if the juicy version looks chaotic or unreadable, dial it back

### Recording Tips

- Use your engine's built-in recording or OBS
- Capture the **same gameplay sequence** both times for fair comparison
- Include: attacking, getting hit, jumping/landing, picking up items, transitioning scenes
- Post both clips side-by-side on Twitter/social media — this content gets massive engagement

---

## Quick Reference: The Full Checklist

Copy this into your project tracker and check items off as you implement them:

### 🔴 Critical (Do First)
- [ ] Screen shake on hits/impacts
- [ ] Hitstop on melee/heavy attacks
- [ ] Impact particles on damage
- [ ] Hit SFX with pitch variation
- [ ] White flash on damage (1–2 frames)
- [ ] Squash on landing, stretch on jumping

### 🟠 High Priority
- [ ] Dust particles (landing, running, jumping)
- [ ] UI tween animations (ease, never linear)
- [ ] Camera smooth follow + deadzone
- [ ] Death particles/explosion
- [ ] Button hover/press feedback
- [ ] Landing sound scaled to fall height
- [ ] Damage tint after white flash
- [ ] Health bar smooth drain

### 🟡 Medium Priority
- [ ] Screen transitions on every scene change
- [ ] Camera lookahead
- [ ] Collectible pickup effects (particles + sound + UI notification)
- [ ] Kill slow-mo on last enemy
- [ ] Invincibility flash during i-frames
- [ ] Menu item stagger animation
- [ ] Music ducking during big events
- [ ] Damage numbers floating up
- [ ] UI click/confirm/cancel sounds

### 🟢 Nice to Have
- [ ] Chromatic aberration on hit (subtle!)
- [ ] Bloom on bright elements
- [ ] Environmental interaction (grass sway, water ripples)
- [ ] Ambient particles (dust, leaves)
- [ ] Low-health vignette
- [ ] Collectible ascending pitch jingle
- [ ] Speed ramp (slow → fast)
- [ ] Destructible props
- [ ] Footprints/trails
- [ ] CRT/retro filter (if appropriate)

---

> *"A game without juice is a spreadsheet with sprites."*
>
> Go make it feel amazing. For the full systems-level breakdown, see [G30: Game Feel Tooling](../../monogame-arch/guides/G30_game_feel_tooling.md) and [C2: Game Feel & Genre Craft](../game-design/C2_game_feel_and_genre_craft.md).
