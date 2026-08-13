# Combat & Damage Systems — Theory & Concepts

This document covers engine-agnostic combat system design theory. For engine-specific implementations, see [G64 Combat & Damage Systems (MonoGame)](../../monogame-arch/guides/G64_combat_damage_systems.md) or the relevant engine module.

---

## Table of Contents

1. [Damage Pipeline Architecture](#damage-pipeline-architecture)
2. [Health & Resource Pools](#health--resource-pools)
3. [Damage Types & Resistances](#damage-types--resistances)
4. [Hitbox / Hurtbox Model](#hitbox--hurtbox-model)
5. [Invincibility Frames](#invincibility-frames)
6. [Knockback & Hit Reactions](#knockback--hit-reactions)
7. [Hit Stop & Screen Shake](#hit-stop--screen-shake)
8. [Projectile Systems](#projectile-systems)
9. [Melee Attack Design](#melee-attack-design)
10. [Critical Hits & Damage Variance](#critical-hits--damage-variance)
11. [Armor & Defense Models](#armor--defense-models)
12. [Status Effects & Damage Over Time](#status-effects--damage-over-time)
13. [Combo Systems](#combo-systems)
14. [Turn-Based Combat](#turn-based-combat)
15. [Difficulty Scaling & Damage Curves](#difficulty-scaling--damage-curves)
16. [Death, Respawn & Recovery](#death-respawn--recovery)
17. [Combat Feel & Feedback](#combat-feel--feedback)
18. [Anti-Patterns](#anti-patterns)
19. [Decision Framework](#decision-framework)

---

## Damage Pipeline Architecture

Every combat system benefits from a well-defined **damage pipeline** — a linear sequence of stages that transforms a raw attack intent into a final health change. This architecture makes the system extensible (add new stages without touching existing ones) and debuggable (log at each stage to see exactly what happened).

### Canonical Pipeline

```
Attack Intent
    → Accuracy Check (can it hit?)
    → Hit Detection (does the hitbox overlap a hurtbox?)
    → Base Damage Calculation (attacker stats + weapon + ability)
    → Damage Type Resolution (fire, physical, magic, etc.)
    → Resistance / Armor Reduction (defender stats)
    → Modifier Stack (buffs, debuffs, critical multiplier, range falloff)
    → Clamping (minimum damage floor, maximum cap)
    → Apply to Health Pool
    → Trigger Reactions (knockback, hit stop, VFX, sound, damage numbers)
    → Check Death Condition
```

### Design Principles

- **Immutable damage events**: Create a damage event struct/object at the start, pass it through the pipeline. Each stage reads and returns a modified copy — never mutate shared state mid-pipeline.
- **Ordered modifier application**: Define a clear order (percentage modifiers before flat modifiers, or vice versa). Document the order — it's a balance decision, not an implementation detail.
- **Minimum damage floor**: Always allow at least 1 damage (or some minimum). A player who lands a hit and sees "0" feels cheated. Exception: explicit "immune" or "absorb" effects should display differently than 0 damage.
- **Damage event metadata**: Carry source (who attacked), target (who was hit), position (where the hit landed), damage type, and flags (was_critical, was_backstab, etc.) through the entire pipeline. Reaction systems need this context.

### Event-Driven vs Direct Call

| Approach | Pros | Cons |
|----------|------|------|
| **Direct function call** | Simple, debuggable, no latency | Tight coupling between attacker and defender |
| **Event/message bus** | Loose coupling, easy to add listeners (UI, audio, achievements) | Harder to debug, ordering surprises |
| **Command pattern** | Undo-friendly (turn-based), replayable | More complex, allocation overhead |

**Recommendation**: Use direct calls for the core pipeline (accuracy → damage → apply), then fire an event AFTER damage is applied for reaction systems (VFX, audio, UI, achievements). This gives tight control where it matters and loose coupling where it helps.

---

## Health & Resource Pools

### Basic Health Model

```
Health:
    current: int
    maximum: int

TakeDamage(amount):
    current = max(0, current - amount)

Heal(amount):
    current = min(maximum, current + amount)

IsAlive():
    return current > 0
```

Use integers for health, not floats. Floating-point accumulation errors cause "unkillable" bugs where health hovers at 0.0001. If you need fractional damage, use fixed-point (store health as hundredths: 10000 = 100.00 HP).

### Segmented Health

Many games display health in discrete segments (Hollow Knight's masks, Zelda's hearts). The visual representation can differ from the internal value:

```
// Internal: 120 HP, Display: 6 masks of 20 HP each
segments = ceil(current / segment_size)
partial_fill = (current % segment_size) / segment_size
```

### Shield / Overshield

A separate pool that absorbs damage before health. Design decisions:

- **Does shield regenerate?** (Halo model: yes, after delay)
- **Does shield absorb all damage types?** (Some pierce shields)
- **Does overflow damage bleed through?** (Shield has 10, attack deals 25: does health take 15 or 0?)

```
TakeDamage(amount, type):
    if type.pierces_shield:
        health.current -= amount
        return

    absorbed = min(shield.current, amount)
    shield.current -= absorbed
    overflow = amount - absorbed

    if bleed_through_enabled:
        health.current -= overflow
```

### Resource Pools (Mana, Stamina, Energy)

Same structure as health but with different regeneration rules:

| Resource | Regen Model | Design Purpose |
|----------|-------------|----------------|
| **Mana** | Slow passive regen, potions | Gate powerful abilities, force resource management |
| **Stamina** | Fast regen when not used, pause on use | Limit action spam, create attack/defend rhythm |
| **Energy** | Per-turn grant (turn-based) or slow charge | Force prioritization each turn/cycle |
| **Rage/Heat** | Builds from combat, decays out of combat | Reward aggression, provide power spikes |
| **Ammo** | No regen, pickup-based | Resource scarcity, exploration reward |

---

## Damage Types & Resistances

### Type System Design

Define damage types as flags or enums. Common set:

```
DamageType:
    PHYSICAL    -- swords, arrows, falling
    FIRE        -- burn, explosion
    ICE         -- freeze, slow
    LIGHTNING   -- chain, stun
    POISON      -- DoT, weak
    HOLY        -- undead bonus
    DARK        -- living bonus
    TRUE        -- ignores all resistance
```

### Resistance Matrix

Each entity has a resistance value per damage type. The value can represent:

- **Flat reduction**: `final = base - resistance` (simple, good for low-number systems)
- **Percentage reduction**: `final = base * (1 - resistance_pct)` (scales better at high numbers)
- **Diminishing returns**: `final = base * (100 / (100 + armor))` (prevents 100% immunity)

**Diminishing returns formula** (used by many ARPGs and MOBAs):

```
damage_multiplier = 100 / (100 + resistance)

// resistance =   0 → 1.00× (no reduction)
// resistance =  50 → 0.67× (33% reduction)
// resistance = 100 → 0.50× (50% reduction)
// resistance = 200 → 0.33× (67% reduction)
// resistance = -50 → 1.33× (vulnerability!)
```

This formula has elegant properties: negative resistance = vulnerability (amplifies damage), and there's no hard cap — infinite resistance approaches but never reaches 100% reduction.

### Effectiveness Triangles

Many games use rock-paper-scissors relationships:

```
Fire → Ice → Lightning → Fire
Physical → Ranged → Magic → Physical
```

Implementation: a 2D lookup table mapping (attack_type, defender_type) → effectiveness_multiplier.

```
effectiveness[FIRE][ICE] = 2.0       // super effective
effectiveness[FIRE][FIRE] = 0.5      // not very effective
effectiveness[FIRE][PHYSICAL] = 1.0  // neutral
```

**Design tip**: Don't make weaknesses too extreme. A 2× multiplier is dramatic enough. 4× or higher leads to one-shot kills that feel unfair.

---

## Hitbox / Hurtbox Model

The hitbox/hurtbox separation is the foundation of spatial combat.

- **Hitbox**: The region that DEALS damage. Attached to attacks — sword swings, projectiles, area effects.
- **Hurtbox**: The region that RECEIVES damage. Attached to entities that can be harmed — the player's body, enemy weak points.

### Why Separate?

A single collision shape can't distinguish "I'm attacking" from "I can be attacked." Separation allows:

- A sword swing hitbox that extends beyond the character's hurtbox
- Attacks that don't damage the attacker
- Weak points (small hurtbox on a boss's eye) vs armored areas (no hurtbox on shielded body)
- Phase-based changes (boss exposes hurtbox only during specific animations)

### Collision Layer Setup

```
Layer assignment:
    Player Hitbox   → detects Enemy Hurtbox
    Enemy Hitbox    → detects Player Hurtbox
    Env Hazard      → detects Player Hurtbox AND Enemy Hurtbox

One-directional monitoring:
    Hitbox: actively monitors (checks for overlaps)
    Hurtbox: passively monitored (is detected by hitboxes)
```

This is a performance optimization — only hitboxes need to run overlap checks. Hurtboxes are passive receivers.

### Activation Patterns

Hitboxes should NOT be always active. Activate and deactivate them in sync with animations:

```
Attack Animation Timeline:
    Frame 0-5:   Windup (hitbox OFF)
    Frame 6-10:  Active (hitbox ON)
    Frame 11-15: Recovery (hitbox OFF)

// Prevent multi-hit: track which entities this hitbox has already hit
on_hitbox_activated:
    already_hit.clear()

on_hitbox_overlap(target):
    if target in already_hit: return
    already_hit.add(target)
    apply_damage(target)
```

### Shape Choices

| Shape | Use Case | Cost |
|-------|----------|------|
| Circle/Sphere | Body hurtbox, explosion radius | Cheapest |
| Rectangle/AABB | Sword slash, ground slam | Cheap |
| Capsule | Tall characters, spear thrust | Medium |
| Polygon | Irregular weapons, precise hitboxes | Expensive |
| Composite | Multiple shapes for one entity | Sum of parts |

---

## Invincibility Frames

After taking damage, grant a brief window of invincibility (i-frames) to prevent multi-hit stacking from a single attack or rapid successive hits.

### Implementation

```
IFrames:
    remaining: float = 0
    flash_timer: float = 0

TakeDamage(amount):
    if remaining > 0: return  // invincible
    apply_damage(amount)
    remaining = IFRAME_DURATION

Update(dt):
    if remaining > 0:
        remaining -= dt
        // Visual feedback: sprite flashing
        flash_timer += dt
        visible = (flash_timer % FLASH_PERIOD) < (FLASH_PERIOD / 2)
```

### Tuning Reference

| Game Type | i-Frame Duration | Notes |
|-----------|------------------|-------|
| Action (fast) | 0.3–0.5s | Short — dodging is the defense |
| Action (medium) | 0.5–1.0s | Standard — Zelda-like |
| Platformer | 1.0–2.0s | Long — hazards are frequent |
| Souls-like | 0.0s (dodge only) | No passive i-frames; dodge roll grants them |
| Bullet hell | 1.0–3.0s on respawn | Generous on respawn, none during normal play |

### Design Consideration

i-frames interact with the **damage pipeline**. They should be checked EARLY in the pipeline (before damage calculation) to avoid wasting computation on damage that will be discarded.

Some games grant **dodge i-frames** (invincible during dodge animation) separately from **damage i-frames** (invincible after getting hit). These use different timers and different visual feedback.

---

## Knockback & Hit Reactions

Knockback makes combat feel physical. Without it, attacks feel like they pass through enemies.

### Impulse Model

```
on_hit(source_pos, target, knockback_force):
    direction = normalize(target.pos - source_pos)
    target.velocity = direction * knockback_force

    // Decay over time (friction)
    Update(dt):
        velocity *= (1 - KNOCKBACK_FRICTION * dt)
        if length(velocity) < THRESHOLD:
            velocity = (0, 0)
```

### Curve-Based Knockback

For more control, use a curve that defines knockback strength over time:

```
// Curve: starts at 1.0, decays to 0 over duration
t = elapsed / KNOCKBACK_DURATION
strength = knockback_curve.evaluate(t)
position += direction * strength * MAX_DISTANCE * dt
```

Curve shapes:
- **Linear**: Constant deceleration. Feels robotic.
- **Ease-out**: Fast start, slow stop. Feels natural — most games use this.
- **Bounce**: Ease-out with a small reverse at the end. Feels cartoonish/exaggerated.

### Weight Classes

Heavier entities should resist knockback:

```
effective_knockback = base_force * (1.0 / target.weight_class)

Weight classes: Light (1.0), Medium (0.6), Heavy (0.3), Immovable (0.0)
```

### Hit Stun

Pair knockback with a brief state change that prevents the target from acting:

```
on_hit:
    target.state = HIT_STUN
    target.stun_timer = HIT_STUN_DURATION
    // During HIT_STUN: no movement input, no attacks, no abilities
    // After timer: return to IDLE
```

Hit stun duration determines whether combos are possible. If stun > time_between_attacks, the attacker can chain hits. This is THE core variable in fighting game balance.

---

## Hit Stop & Screen Shake

These are **game feel** techniques that communicate impact without changing gameplay mechanics.

### Hit Stop (Frame Freeze)

Pause the game for a few frames when a significant hit lands. This gives the player's eye time to register the impact.

```
on_significant_hit:
    time_scale = 0.0     // freeze everything
    freeze_timer = HIT_STOP_FRAMES / 60.0

Update(dt):
    if freeze_timer > 0:
        freeze_timer -= REAL_dt  // use real time, not game time
        if freeze_timer <= 0:
            time_scale = 1.0
```

**Selective hit stop**: Only freeze the attacker and target, not the entire game. This feels better in multiplayer or when multiple enemies are present.

| Impact Level | Freeze Frames (60 FPS) | Duration |
|-------------|------------------------|----------|
| Light | 2–3 | 33–50ms |
| Medium | 4–6 | 67–100ms |
| Heavy | 8–12 | 133–200ms |
| Critical/Kill | 12–20 | 200–333ms |

### Screen Shake

Rapidly offset the camera to communicate force.

**Perlin noise shake** (preferred over random): Smooth, organic motion without jarring discontinuities.

```
Trauma-based shake:
    trauma: float = 0  // 0 to 1
    shake_amount = trauma * trauma  // quadratic falloff

    offset_x = noise(seed + time * SPEED) * MAX_OFFSET * shake_amount
    offset_y = noise(seed + time * SPEED + 100) * MAX_OFFSET * shake_amount
    rotation = noise(seed + time * SPEED + 200) * MAX_ROTATION * shake_amount

    trauma -= DECAY_RATE * dt
    trauma = max(0, trauma)

on_hit:
    trauma = min(1.0, trauma + HIT_TRAUMA)
```

**Trauma is additive** — multiple hits in quick succession create escalating shake. The quadratic mapping (shake = trauma²) ensures light hits produce subtle shake while heavy hits are dramatic.

| Event | Trauma | Max Offset (px) |
|-------|--------|-----------------|
| Light hit | 0.2 | 2–4 |
| Heavy hit | 0.5 | 6–10 |
| Explosion | 0.8 | 12–16 |
| Boss slam | 1.0 | 16–24 |

**Directional shake**: Bias the shake toward the hit direction for the first few frames, then transition to omnidirectional. This subconsciously communicates WHERE the hit came from.

---

## Projectile Systems

### Projectile Types

| Type | Movement | Hit Detection | Use Case |
|------|----------|---------------|----------|
| **Linear** | Constant velocity, straight line | Circle/AABB overlap | Bullets, arrows |
| **Homing** | Steers toward target | Circle overlap | Missiles, magic |
| **Arcing** | Parabolic (gravity-affected) | Circle or point | Grenades, thrown items |
| **Beam/Ray** | Instant (raycast) | Line intersection | Lasers, sniper |
| **Area** | Stationary or expanding | Circle/rect overlap | Explosions, shockwaves |
| **Bouncing** | Reflects off surfaces | Circle + reflection | Ricochet bullets |

### Homing Projectile Steering

```
// Proportional Navigation (smooth tracking)
desired_direction = normalize(target.pos - projectile.pos)
current_direction = normalize(projectile.velocity)
steer = cross(current_direction, desired_direction) * TURN_RATE
projectile.velocity = rotate(projectile.velocity, steer * dt)
```

**Turn rate** determines how aggressively the projectile tracks:
- Low (1–2 rad/s): Wide arcs, dodgeable — feels fair
- Medium (3–5 rad/s): Moderate tracking — requires movement to dodge
- High (8+ rad/s): Near-instant tracking — feels unfair unless counterable

### Projectile Lifetime & Cleanup

Every projectile MUST have a maximum lifetime or range. Projectiles that fly forever accumulate and cause performance degradation.

```
MAX_LIFETIME = 5.0  // seconds
MAX_RANGE = 1000    // pixels/units from spawn point

Update(dt):
    lifetime += dt
    if lifetime > MAX_LIFETIME: destroy()
    if distance(pos, spawn_pos) > MAX_RANGE: destroy()
```

### Object Pooling

Projectile-heavy games (bullet hell, twin-stick) should pool projectiles rather than allocating/destroying them each frame. See [Object Pooling Theory](#) for the general pattern. Key points:

- Pre-allocate a pool of N projectiles at load time
- "Spawn" = reset and activate an inactive pooled instance
- "Destroy" = deactivate and return to pool
- Size the pool to peak expected count + 20% headroom

---

## Melee Attack Design

### Frame Data Model

Borrowed from fighting games, frame data defines the rhythm of melee attacks:

```
Attack Definition:
    startup_frames:  int   // windup before hitbox activates
    active_frames:   int   // hitbox is live
    recovery_frames: int   // cooldown after hitbox deactivates
    total = startup + active + recovery

    damage: int
    knockback: float
    hit_stun: float
```

**Design tension**: Short startup = responsive but hard to read. Long startup = readable but sluggish. The player must be able to SEE the windup (visual telegraph) proportional to how punishing the attack is.

### Telegraph Hierarchy

Scale the telegraph to the attack's power:

| Attack Tier | Startup | Visual Cue |
|-------------|---------|------------|
| Light (jab) | 2–4 frames | Subtle arm motion |
| Medium (slash) | 6–10 frames | Full swing windup, weapon trail |
| Heavy (smash) | 12–20 frames | Charge pose, glow, sound cue |
| Ultimate | 30–60 frames | Screen-wide warning, ground indicator, audio buildup |

### Cancel Windows

Allow players to interrupt one attack with another during specific frames:

```
// Cancel hierarchy: light → medium → heavy → special
can_cancel(current_attack, target_attack):
    if current_attack.tier >= target_attack.tier: return false
    if current_frame not in current_attack.cancel_window: return false
    return true
```

This is the foundation of **combo systems** — see [Combo Systems](#combo-systems) below.

---

## Critical Hits & Damage Variance

### Critical Hit Formula

```
if random() < crit_chance:
    final_damage = base_damage * crit_multiplier
    trigger_crit_feedback()  // flash, sound, bigger number
else:
    final_damage = base_damage
```

Typical values: crit_chance = 5–25%, crit_multiplier = 1.5–2.0×.

### Damage Variance

Add controlled randomness to prevent combat from feeling deterministic:

```
variance = random_range(-VARIANCE, +VARIANCE)
final_damage = base_damage * (1.0 + variance)

// VARIANCE = 0.1 means ±10% spread
// Attack with base 100 → deals 90–110 damage
```

**Design guideline**: Keep variance under ±15%. Higher variance makes damage unpredictable and frustrating for players trying to strategize. Exception: roguelikes intentionally use high variance (±25%+) as a design pillar.

### Pseudo-Random Distribution

True random crits feel "streaky" — players perceive 3 crits in a row as broken. Use pseudo-random distribution (PRD) to guarantee evenness:

```
// PRD: probability increases each non-crit, resets on crit
C = prd_constant_for(desired_chance)  // lookup table
accumulated = C

on_attack:
    if random() < accumulated:
        critical_hit()
        accumulated = C  // reset
    else:
        accumulated += C  // increase next chance
```

PRD guarantees the same average crit rate as true random but eliminates long streaks in either direction.

---

## Armor & Defense Models

### Model Comparison

| Model | Formula | Behavior | Best For |
|-------|---------|----------|----------|
| **Flat reduction** | `dmg - armor` | Devastating against weak attacks, useless against strong | Low-number systems (Souls-like) |
| **Percentage** | `dmg * (1 - armor%)` | Proportional reduction regardless of damage | Simple RPGs |
| **Diminishing** | `dmg * 100/(100+armor)` | Never reaches immunity, scales gracefully | Complex RPGs, MOBAs |
| **Threshold** | `if dmg < armor: dmg=0 else: full dmg` | Binary — either blocked or not | Puzzle/strategy games |
| **Layered** | Shields → Armor → Health | Multiple pools to deplete | Looter games, sci-fi |

### Armor Penetration

Many games include armor penetration stats to counter high-armor targets:

```
// Flat penetration: ignore a fixed amount of armor
effective_armor = max(0, armor - flat_penetration)

// Percentage penetration: ignore a percentage of remaining armor
effective_armor = effective_armor * (1 - pct_penetration)

// Apply diminishing returns with effective armor
final_damage = base * (100 / (100 + effective_armor))
```

**Flat pen is better against low-armor targets. Percentage pen is better against high-armor targets.** This creates meaningful itemization choices (which is the point of having both).

---

## Status Effects & Damage Over Time

### Common Status Effect Types

| Effect | Mechanic | Duration Model |
|--------|----------|----------------|
| **Poison/Bleed** | Periodic damage ticks | Duration + tick rate |
| **Burn** | Damage + defense reduction | Duration, may spread |
| **Freeze/Slow** | Movement speed reduction | Duration |
| **Stun** | Cannot act | Short duration (0.5–2s) |
| **Root** | Cannot move, can still act | Duration |
| **Silence** | Cannot use abilities | Duration |
| **Blind** | Reduced accuracy/vision | Duration |
| **Weakness** | Reduced damage output | Duration |
| **Vulnerability** | Increased damage taken | Duration |

### Stacking Rules

Define explicit stacking rules for EVERY status effect:

```
StackingPolicy:
    NONE        // second application refreshes duration only
    INTENSITY   // second application increases damage/slow amount
    DURATION    // second application extends duration
    INDEPENDENT // each application tracked separately (can be very strong)
```

**INDEPENDENT stacking is dangerous** — 10 poison stacks each dealing 5 DPS = 50 DPS, which can trivialize bosses. Cap independent stacks or use duration/intensity stacking instead.

### DoT Implementation

```
DamageOverTime:
    damage_per_tick: int
    tick_interval: float
    remaining_duration: float
    tick_timer: float

Update(dt):
    remaining_duration -= dt
    if remaining_duration <= 0:
        remove_effect()
        return

    tick_timer -= dt
    if tick_timer <= 0:
        apply_damage(damage_per_tick)
        tick_timer += tick_interval
```

**Important**: Tick on regular intervals, not per-frame. Per-frame DoT is framerate-dependent — 60 FPS players take twice the damage of 30 FPS players.

---

## Combo Systems

### Input Combo (Fighting Games)

Sequence of inputs within timing windows:

```
ComboDetector:
    sequence: [InputAction]
    window_per_step: float
    current_step: int = 0
    timer: float = 0

on_input(action):
    if action == sequence[current_step]:
        current_step += 1
        timer = window_per_step
        if current_step >= sequence.length:
            execute_combo()
            reset()
    else:
        reset()

Update(dt):
    timer -= dt
    if timer <= 0 and current_step > 0:
        reset()
```

### Chain Combo (Character Action Games)

Cancel the recovery of one attack into the startup of the next:

```
Combo Chain: Light → Light → Heavy
    Attack 1 (Light): 3 startup, 4 active, 6 recovery
    — cancel window during recovery frames 1-4 —
    Attack 2 (Light): 2 startup, 4 active, 6 recovery
    — cancel window during recovery frames 1-3 —
    Attack 3 (Heavy): 8 startup, 6 active, 12 recovery
    — no cancel (combo ender) —
```

The cancel window SHRINKS with each chain step (4 → 3 frames above). This creates a skill curve — continuing the combo requires tighter timing.

### Combo Counter & Scaling

```
combo_count: int = 0
combo_timer: float = 0
COMBO_WINDOW = 2.0  // seconds between hits to maintain combo

on_hit:
    combo_count += 1
    combo_timer = COMBO_WINDOW

    // Damage scaling prevents infinite combos from being too dominant
    // Proration: each successive hit in a combo deals less
    proration = max(MIN_PRORATION, 1.0 - (combo_count - 1) * PRORATION_RATE)
    final_damage = base_damage * proration

Update(dt):
    combo_timer -= dt
    if combo_timer <= 0:
        end_combo()
        combo_count = 0
```

**Proration** (damage scaling) is essential for balance. Without it, long combos are strictly better than short ones, which removes strategic variety. Typical proration: 100% → 90% → 80% → 70% → 60% (floor).

---

## Turn-Based Combat

### Turn Order Systems

| System | Description | Examples |
|--------|-------------|----------|
| **Fixed order** | Predetermined sequence, repeats each round | Classic JRPG |
| **Speed-based** | Sorted by speed stat each round | Pokémon |
| **Timeline/CTB** | Charge bar fills by speed, act when full | FFX, Chrono Trigger |
| **Initiative** | Roll + modifier, acts in order | D&D, Baldur's Gate |
| **Phase-based** | All of one side acts, then the other | XCOM, Into the Breach |

### Timeline System (Pseudocode)

```
Timeline:
    entries: [(entity, charge)]

    advance():
        // Find the entity closest to acting
        min_time = min(MAX_CHARGE - e.charge) / e.speed for e in entries)

        for e in entries:
            e.charge += e.speed * min_time
            if e.charge >= MAX_CHARGE:
                e.charge -= MAX_CHARGE
                return e.entity  // this entity acts next
```

### Action Economy

Each turn, an entity has a budget of actions:

```
Turn Budget:
    movement: 1 (can move)
    action:   1 (attack, ability, item)
    bonus:    1 (quick action, free ability)
    reaction: 1 (counter-attack, opportunity attack — spent on enemy turn)
```

The design of action economy determines the pacing and depth of turn-based combat. More action types = more decisions per turn = deeper but slower.

---

## Difficulty Scaling & Damage Curves

### Stat Growth Models

```
Linear:      stat(level) = base + (growth * level)
Exponential: stat(level) = base * (growth_rate ^ level)
S-Curve:     stat(level) = max / (1 + e^(-k*(level - midpoint)))
```

**Linear** is easiest to balance — the difference between level 5 and level 10 is the same as between level 50 and level 55. **Exponential** creates dramatic power spikes and requires content to scale accordingly. **S-Curve** starts slow, accelerates mid-game, then plateaus — ideal for systems with a natural cap.

### Damage Formula Examples

**Subtraction (Souls-like)**:
```
damage = attack - defense
// Simple but armor is devastating against weak attacks
```

**Multiplication (Percentage)**:
```
damage = attack * attack / (attack + defense)
// Self-balancing: high attack vs high defense still deals meaningful damage
```

**Level-scaled (ARPG)**:
```
damage = base * (1 + stat * coefficient) * skill_multiplier * crit * variance
// Many multiplicative layers — small stat changes compound dramatically
```

### Difficulty Adjustments

Avoid adjusting enemy HP/damage directly (players notice and resent it). Better approaches:

| Technique | Description | Player Perception |
|-----------|-------------|-------------------|
| Enemy aggression | Attack less frequently on Easy | Natural — "enemies are cautious" |
| Telegraph duration | Longer windups on Easy | Fair — "I had time to react" |
| Resource abundance | More health pickups on Easy | Organic — "I found stuff" |
| Hidden crit chance | Player crits more on Easy | Lucky — "I'm doing well" |
| Recovery time | Longer enemy recovery on Easy | Exploitable — "I found openings" |

---

## Death, Respawn & Recovery

### Death State Machine

```
ALIVE → DYING → DEAD → [RESPAWNING → ALIVE]

DYING:
    - Play death animation
    - Drop loot (if applicable)
    - Disable collision
    - No input accepted
    - Duration: 0.5–2.0s

DEAD:
    - Remove from active entities (or mark inactive)
    - Trigger game-over check (if player)
    - Start respawn timer (if respawn enabled)

RESPAWNING:
    - Choose spawn point
    - Reset health to full (or partial)
    - Grant i-frames (long: 2–3s)
    - Fade in / play spawn animation
    - Enable input
```

### Consequences of Death

| Consequence | Severity | Genre |
|-------------|----------|-------|
| Restart checkpoint | Low | Platformer, action |
| Lose currency (recoverable) | Medium | Souls-like |
| Lose currency (permanent) | High | Roguelike |
| Lose items/equipment | Very high | Hardcore survival |
| Permadeath | Maximum | Roguelike, strategy |
| No consequence (instant respawn) | None | Arena shooter, casual |

---

## Combat Feel & Feedback

Combat **feel** is the sum of all non-mechanical feedback that communicates impact. A combat system can be mechanically correct but feel terrible without these:

### Feedback Channels

| Channel | On Hit | On Kill |
|---------|--------|---------|
| **Visual** | Sprite flash white (1–2 frames), hit spark VFX, damage number | Death VFX (explosion, dissolve), screen flash |
| **Audio** | Impact sound (vary by material), grunt/bark | Death cry, environmental impact |
| **Camera** | Screen shake (trauma-based), hit stop | Slow-motion (0.5s), zoom |
| **Haptic** | Controller vibration (short burst) | Controller vibration (long rumble) |
| **Animation** | Hit reaction (flinch), knockback | Ragdoll, collapse, fly-back |
| **UI** | Health bar decrease (animated, not instant), combo counter | XP popup, level-up indicator |

### The White Flash

The single most impactful combat feel technique: on taking damage, render the sprite fully white for 1–2 frames. This is universally readable, cheap to implement, and works at any resolution or art style.

```
on_damage:
    flash_white = true
    flash_timer = 2 / 60  // 2 frames at 60 FPS

shader/render:
    if flash_white:
        output_color = WHITE  // ignore sprite texture
    else:
        output_color = texture_color
```

### Layering Feedback

Good combat feel comes from layering multiple channels simultaneously. A single sword hit should trigger:

1. Hitbox collision detected
2. Damage number spawns
3. Target flashes white (2 frames)
4. Hit spark VFX at contact point
5. Impact sound plays
6. Screen shake (small trauma)
7. Hit stop (2–4 frames)
8. Target enters hit stun
9. Knockback applied
10. Target health bar animates down

All 10 happen within 100ms. The player perceives them as a single "impact."

---

## Anti-Patterns

### 1. Damage Sponge
**Problem**: Enemies with massive HP pools that take minutes of hitting.
**Fix**: Use time-to-kill budgets. Normal enemies: 3–5 hits. Elites: 10–15 hits. Bosses: 30–60 seconds of sustained DPS. If combat is boring, the enemy has too much HP.

### 2. Instant Death
**Problem**: Player dies in one hit with no counterplay.
**Fix**: Every lethal attack should have a visible telegraph proportional to its damage. Alternatively, give the player a "last stand" mechanic (survive at 1 HP once).

### 3. Stat Check Combat
**Problem**: Combat outcome determined entirely by stat comparison, player skill irrelevant.
**Fix**: Ensure every enemy has learnable attack patterns. Even if stats matter, skilled players should be able to defeat higher-level enemies by playing well.

### 4. Invisible Damage
**Problem**: Player takes damage with no clear feedback about source or amount.
**Fix**: Every damage event triggers at least 3 feedback channels (visual + audio + UI minimum). Show damage numbers. Point the screen shake toward the damage source.

### 5. Unreadable Hitboxes
**Problem**: The attack visuals don't match the hitbox shape/timing.
**Fix**: Draw hitboxes in debug mode. The active hitbox should be fully enclosed by the attack animation. Players will accept losing when they can SEE the hit.

---

## Decision Framework

Use this tree to choose your combat architecture:

```
Is combat real-time or turn-based?

├── REAL-TIME
│   ├── How many entities in simultaneous combat?
│   │   ├── 1v1 → Frame data model, fighting game patterns
│   │   ├── 1 vs few (3–10) → Hitbox/hurtbox, i-frames, combo chains
│   │   └── 1 vs many (10+) → Area attacks, crowd damage, object pooling
│   ├── Is positioning important?
│   │   ├── Yes → Knockback, area effects, positioning abilities
│   │   └── No → Menu-based selection (MMO hotbar style)
│   └── Skill-based or stat-based?
│       ├── Skill → Short time-to-kill, readable telegraphs, i-frames
│       └── Stat → Longer TTK, resistance system, gear progression
│
└── TURN-BASED
    ├── Grid-based or abstract?
    │   ├── Grid → Positioning matters, line-of-sight, flanking bonuses
    │   └── Abstract → Party lineup, row-based targeting (front/back)
    ├── Simultaneous or sequential turns?
    │   ├── Simultaneous → Plan phase + resolution phase (Into the Breach)
    │   └── Sequential → Speed/initiative system (see Turn Order Systems)
    └── How deep is the action economy?
        ├── Simple (1 action) → Fast, casual (Pokémon)
        ├── Medium (2–3 actions) → Tactical (Fire Emblem)
        └── Complex (4+ action types) → Deep strategy (D&D, XCOM)
```

---

## Cross-References

- [G64 Combat & Damage Systems (MonoGame)](../../monogame-arch/guides/G64_combat_damage_systems.md) — Full C# implementation with Arch ECS
- [G4 AI Systems (MonoGame)](../../monogame-arch/guides/G4_AI_systems.md) — Enemy AI behaviors that drive combat
- [Physics & Collision Theory](./physics-theory.md) — Collision detection foundations
- [Camera Theory](./camera-theory.md) — Screen shake and hit stop camera integration
- [Animation Theory](./animation-theory.md) — Frame data and attack animation synchronization
- [Input Handling Theory](./input-handling-theory.md) — Input buffering for combat inputs
- [Character Controller Theory](./character-controller-theory.md) — Movement during combat states
- [G5 Physics & Collision (Godot)](../../godot-arch/guides/G5_physics_and_collision.md) — Hitbox/hurtbox setup in Godot
