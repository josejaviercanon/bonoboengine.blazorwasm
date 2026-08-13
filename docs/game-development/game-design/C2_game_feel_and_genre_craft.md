# C2 — Game Feel & Genre Design Craft


> **Category:** Catalog · **Related:** [C1 Genre Reference](./C1_genre_reference.md) · [G2 Rendering & Graphics](../../monogame-arch/guides/G2_rendering_and_graphics.md) · [G3 Physics & Collision](../../monogame-arch/guides/G3_physics_and_collision.md) · [G6 Audio](../../monogame-arch/guides/G6_audio.md) · [G7 Input Handling](../../monogame-arch/guides/G7_input_handling.md) · [G1 Custom Code Recipes](../../monogame-arch/guides/G1_custom_code_recipes.md) · [G30 Game Feel Tooling](../../monogame-arch/guides/G30_game_feel_tooling.md) · [R4 Game Design Resources](R4_game_design_resources.md)

---

Genre-specific mechanics with concrete tuning values, the complete "juice" toolkit (screen shake, hitstop, squash/stretch, particles, camera systems), and input responsiveness techniques. Where [C1 Genre Reference](./C1_genre_reference.md) maps "what systems do I need," this document covers "how to design them well."

---

## Platformers: The Science of Movement Feel

The foundation of a great platformer is **forgiving input handling** that widens timing windows in the player's favor.

### Key Techniques with Specific Values

| Technique | Description | Values |
|-----------|-------------|--------|
| **Coyote time** | Allows jumping after walking off a ledge | **5-10 frames** (~83-166ms at 60fps). Celeste uses 5 frames |
| **Jump buffering** | If jump is pressed while airborne, execute on landing | **6-10 frame** window |
| **Variable jump height** | Releasing jump early applies gravity multiplier | **2-3x** gravity on release |
| **Apex float** | Reduced gravity at jump peak when button held | **0.5x** gravity (Celeste) |
| **Corner correction** | Nudge player around clipped corners going upward | **2-4 pixels** horizontal |

### Jump Curve Design

Two parameters define a jump: **jump height** and **time to apex**.

- Gravity: `(-2 * jumpHeight) / (timeToApex^2)`
- Initial velocity: `(2 * jumpHeight) / timeToApex`
- Separate downward gravity multiplier (**1.5-3x**) makes descent faster than ascent, creating an asymmetric arc that feels responsive

### Level Design: Teach-Test-Twist

From Super Mario World 1-1: introduce a mechanic in a safe environment, present a simple test, then add a twist that demands mastery.

- Celeste teaches each chapter's mechanic independently, then combines with previously learned skills
- Nintendo designs levels with multiple routes for different skill levels
- Critical production note: the first level designed for Snakebird moved to position 9 by release — early designs are always too hard

---

## Metroidvanias: Ability Gating and Interconnected Worlds

The defining mechanic: new abilities unlock previously inaccessible areas. **Each ability should serve at least 3 purposes**: progress to new areas, create shortcuts in old areas, and reframe combat or puzzle spaces.

### Gate Types
- **Hard gates** — absolute barriers controlling the critical path
- **Soft gates** — high-level enemies or resource requirements that guide without blocking

### Map Design Principles
- Interconnected rooms with bidirectional traversal
- Regional identity (biome color, music, geometry) telegraphs location so clearly that players can navigate by memory
- Shortcut reveals — one-way doors that open from the far side — create satisfying loop closures
- Make collectibles visible but inaccessible on first visit, creating mental bookmarks that drive backtracking

### Power Progression
Two valid patterns:
1. Give all powers then remove them (creating desire to reclaim)
2. Start at weakest and build up

The best abilities are fun to use AND structurally meaningful — Ori's bash is simultaneously a combat tool, a traversal mechanic, and a puzzle solution.

---

## Top-Down Action/Adventure: Dungeon Design

The classic Zelda formula: **overworld + dungeons** where items serve triple duty as combat tool, puzzle solver, and exploration enabler.

- Each dungeon introduces one key item used to solve its puzzles and defeat its boss
- Room-by-room progression mixes puzzle, combat, and exploration with escalating complexity

Modern evolutions:
- **Tunic** — knowledge-gated discovery (in-game manual in a fictional language)
- **CrossCode** — real-time combat with puzzle elements
- **Soulslike influences** — stamina management, dodge-rolling, pattern recognition

Critical design principle: **combat should use the same verbs as exploration**.

---

## Roguelikes/Roguelites: Run-Based Design

### Procedural Generation Approaches

| Approach | Best For |
|----------|----------|
| **BSP trees** | Rectangular rooms connected by corridors |
| **Cellular automata** | Organic caves |
| **Hand-crafted room templates randomly connected** | Most common hybrid — ensures quality of individual rooms while varying layouts |

### Permadeath Design

Making death feel fair and educational:

- **Hades' philosophy**: "You should never feel like you just wasted your whole run for nothing." Death advances the story, NPCs have new dialogue, multiple currencies (Darkness for mirror upgrades, keys for weapons, nectar for relationships) tie to different progression types
- **Spelunky**: each death reveals mechanics not yet understood

### Item Synergy Systems

The heart of roguelite depth:
- **Hades' boon system** — themed abilities per Olympian god, with Duo boons requiring prerequisites from two gods
- **Binding of Isaac** — 700+ items visually modify the character and produce dramatically overpowered combinations by design
- Core principle: items should be individually useful but **exponentially powerful in combination**

---

## Turn-Based RPGs and Tactical Games

### The Four Virtues of Tactical Combat

1. **Emergent complexity** — complex gameplay from simple rules
2. **Clarity** — consequences visible ahead of time
3. **Determinism** — skilled play nearly always wins
4. **Sufficient tactical tools** — so skill trumps luck

### Key Design Techniques

- Use grid space to multiply complexity
- Specialize both player characters and enemies into distinct roles
- Directional facing for flanking bonuses
- Variable terrain with chokepoints and cover
- Manipulable terrain (destructible walls, placeable traps)
- Multiple objectives per encounter beyond "kill all enemies"

### Notable Action Economy Innovations

| Game | Innovation |
|------|-----------|
| **SMT's Press Turn** | Hitting weaknesses costs half a turn, rewarding type knowledge |
| **Into the Breach** | Perfect information — enemies telegraph all moves, turning each turn into a spatial puzzle |
| **Fire Emblem's Weapon Triangle** | Rock-paper-scissors creates tactical depth |

---

## Puzzle Games: Teaching Without Words

### Three Types of Puzzle Difficulty (Ramp Separately)

1. **Conceptual** — understanding new ideas
2. **Combinatorial** — managing larger possibility spaces
3. **Execution** — physical skill

Never spike two simultaneously.

### The Witness Method: 5 Conditions for Wordless Instruction

1. Keep the player in a small focused location
2. Provide a safe environment with no failure
3. Use the minimum puzzle needed
4. Make behavior guessable through intuition
5. Make the only way to solve require learning all intended rules

The "aha moment" requires the player to feel they *discovered* the solution, not stumbled onto it. Include plausible-but-wrong approaches that teach why the correct solution works.

---

## Shmups: Designing the Negative Space

The differentiator of bullet hell is **thoughtful use of bullets**, not sheer number. The spaces between bullet streams are the actual gameplay — design the safe paths, not just the dangerous ones.

### Pattern Types

| Pattern | Counter |
|---------|---------|
| Aimed | Streaming (moving perpendicular) |
| Fixed-angle | Geometric dodging |
| Spread/fan | Finding lanes |
| Spiral | Precise movement |
| Ring bursts | Positioning between gaps |

### Design Parameters

- **Hitbox size defines the entire game**: tiny hitboxes (danmaku) require more bullets for challenge; larger hitboxes require fewer but more threatening bullets
- Movement must have **zero inertia** in danmaku — perfectly responsive
- Scoring systems should incentivize natural gameplay: Touhou's grazing rewards touching bullets without getting hit; proximity DPS rewards aggressive positioning

---

## Strategy and Tower Defense: Resource Tension

The core design tension: **upgrade vs. expand** — players choose between deepening existing towers (upgrade) or broadening coverage (build new).

- **Kingdom Rush's barracks** — melee units that physically block enemy movement — was the genre's most important innovation, creating a stalling dimension absent from pure shooting
- Each new enemy type should demand a **new strategic response**: armored enemies require magic towers, fast enemies require stalling, flying enemies require ranged towers, healing enemies require burst damage

---

## Narrative and Visual Novels: Micro-Reactivity

### Branching Narrative Patterns

| Pattern | Description |
|---------|-------------|
| **Time cave** | Exponential branching |
| **Branch and bottleneck** | Diverge then reconverge at story beats |
| **Parallel paths** | 2-4 major routes |
| **Floating modules** | Self-contained segments in varying order |

### Key Design Insights

- **Disco Elysium's "micro-reactivity"**: thousands of moments where the game remembers trivial decisions (whether you shaved affects an aftershave conversation)
- **Limit true narrative agency while maintaining the aesthetics of control.** Players choose many small things; major beats remain fixed but feel player-driven
- **Undertale**: the seemingly cosmetic choice (spare or kill) can become THE meaningful choice, with the game remembering across playthroughs

---

## The Complete "Juice" Toolkit

### What Game Feel Actually Is

Steve Swink defines game feel as **"real-time control of virtual objects in a simulated space, with interactions emphasized by polish."** It's a closed loop: player provides input -> simulation processes it -> output feeds back through screen, speakers, and rumble. When this loop is tight, the controller becomes an extension of the player's senses.

Test: does interacting with the most basic mechanics feel satisfying on their own, divorced from content?

---

### Screen Shake

The highest-impact visual effect relative to effort.

| Parameter | Recommendation |
|-----------|---------------|
| **Algorithm** | Perlin noise (not random) for smoother, more organic shake |
| **Trauma system** | Track trauma value (0.0-1.0) that decays over time; displacement = `trauma^2 * maxOffset` |
| **Small impacts** | 2-5 pixels, 0.1-0.3s |
| **Medium impacts** | 5-10 pixels, 0.2-0.4s |
| **Large impacts** | 10-20 pixels, 0.3-0.5s |
| **Nausea prevention** | Cap accumulated magnitude |
| **Priority** | Player damage > explosion > enemy death > weapon fire |

---

### Hitstop

Freezing for a few frames on impact dramatically improves perceived hit weight.

| Attack Type | Duration |
|-------------|----------|
| Light attacks | **3-5 frames** |
| Medium attacks | **5-8 frames** |
| Heavy attacks | **8-13 frames** |

During the freeze:
- Offset the hit character's sprite by **1-2 pixels** left/right each frame to simulate shock vibration
- Particles, camera shake, and VFX continue at normal speed — only gameplay entities pause
- Masahiro Sakurai: hitstop is "a crucial effect" — without it, hits feel weak and are harder to visually confirm

---

### Squash and Stretch

| Phase | ScaleY | ScaleX | Duration |
|-------|--------|--------|----------|
| **Jump launch** (stretch) | 1.2 | 0.85 | 2-3 frames |
| **Landing** (squash) | 0.75 | 1.25 | 2-4 frames, then lerp back |

Always preserve volume — if one axis stretches, the perpendicular compresses. The Celeste trick: draw a squash frame at the *beginning* of a jump while the character is already rising, creating the impression of anticipation without adding input lag.

---

### Particle Effects Hierarchy

1. **Impact particles** — 5-15 small particles in a cone opposite the hit
2. **Trail effects** — low-alpha sprites at previous positions
3. **Landing dust** — 3-5 puffs scaled by fall distance
4. **Death effects** — 20-50 particles with lingering smoke
5. **Ambient particles** — dust motes, floating embers that make the world feel alive

---

### Flash Effects

| Effect | Technique |
|--------|-----------|
| **Damage flash** | Set sprite to white for **1-2 frames** via additive shader with `flashAmount` uniform |
| **Invincibility** | Alternate sprite visibility every **2-3 frames** |
| **Enemy damage** | Brief red tint |
| **Boss/player death** | White screen flash for **1-2 frames** |

---

## 2D Camera Systems

From Itay Keren's landmark GDC 2015 talk.

### Camera-Window (Dead Zone)

Player moves freely within a rectangle; the camera only scrolls when the player pushes against edges, eliminating unnecessary movement.

### Lerp-Smoothing (Frame-Rate Independent)

```
cameraPos += (targetPos - cameraPos) * (1 - pow(1 - smoothFactor, deltaTime))
```

| SmoothFactor | Result |
|-------------|--------|
| **0.05-0.15** | Smooth, cinematic follow |
| **0.2-0.4** | Responsive follow |

### Platform-Snapping

Vertically anchors the camera only when the player lands, preventing vertical jitter during jumps.

### Dual-Forward Focus (Super Mario World)

Uses a small center threshold — when the player crosses it, the camera lerps to provide forward view in the new direction, preventing oscillation.

### Pixel-Art Tip

Round camera position to the nearest pixel after all smoothing to prevent sub-pixel jitter.

---

## Input Responsiveness

**Coyote time** and **jump buffering** together are the most impactful input-feel improvements. Together they eliminate the most common "controls feel broken" complaints.

### Combo System Input Buffering

Store the last input with a timestamp in a queue; when the current action's recovery frames become cancellable, check for buffered input within a **6-12 frame window** (100-200ms).

### Timing Window Guidelines

| Window | Perception |
|--------|-----------|
| 1 frame (frame-perfect) | Frustrating for most players |
| **5-10 frames** | Feels "tight but fair" |
| 10+ frames | Feels generous/casual |

---

## Animation Principles Applied to Games

### Anticipation vs Input Lag

The most game-relevant of Disney's 12 principles creates a design tension:

- **Player characters**: keep anticipation to **1-3 frames** and start the gameplay action immediately while playing anticipation visually
- **Enemies**: longer anticipation (**5-15+ frames**) is desirable as it telegraphs attacks

### Easing Functions

| Function | Formula | Best For |
|----------|---------|----------|
| **Ease-out** | `t = 1 - (1-t)^2` | Responses to player input (fast start, slow end) |
| **Ease-in** | `t = t^2` | Wind-ups (slow start, fast end) |
| **Elastic ease-out** | Overshoot + bounce | Playful UI pop-ins |

Apply the easing function to the interpolation parameter before using it in lerp.

---

## Audio as Game Feel

### Impact Sound Layering

Build impact sounds from 3-4 layers:
1. **Transient** — sharp crack
2. **Body** — thud or metallic ring
3. **Sweetener** — high-frequency sizzle
4. **Tail** — rumble/reverb

### Variation and Pitch

- For frequently-heard sounds, create **5-10 variations** with random pitch shifting of **+/-5-10%** to prevent audio fatigue
- Rising pitch on combos (+1 semitone per successive hit) reinforces chain satisfaction
- Sound must be perfectly synchronized with visual impact — even **2-3 frames** of desync feels wrong

### Dynamic Music Through Vertical Layering

Play multiple audio tracks simultaneously, controlling volumes based on game state:
- Calm exploration = minimal percussion + ambient pads
- Combat = add drums and bass
- Boss phase 2 = add choir and brass

See [G6 Audio](../../monogame-arch/guides/G6_audio.md) for implementation details.

---

## Vlambeer's Implementation Priority Tiers

Jan Willem Nijman demonstrated transforming a bland side-scrolling shooter using approximately **30 incremental tricks**: basic sound effects -> bigger bullets -> increased fire rate -> spread for variety -> gun kickback -> permanent corpses and shell casings -> screen shake -> muzzle flash -> hitstop (literally `sleep(20)` — 20ms pause on enemy death) -> deeper bass in sounds -> explosion effects -> camera lerp lag.

Their philosophy: "Games are for fun, not to be logical" — make bullets visually huge even if unrealistic. Every small change compounds.

### Priority Order for Maximum Impact

**Tier 1 — Maximum impact, minimal effort:**
- Sound effects on core actions
- Screen shake on impacts
- Hitstop on hits (3-5 frame pause)
- Input buffering + coyote time

**Tier 2 — High impact, moderate effort:**
- Squash and stretch on core character
- Hit flash (white shader)
- Impact particles
- Knockback on hits
- Camera lerp follow

**Tier 3 — Polish layer:**
- Easing on all transitions
- Anticipation frames
- Post-processing effects
- Permanence (lingering corpses and debris)
- Ambient particles
- Dynamic audio

### Common Mistakes

- Too much shake (nausea)
- Wrong timing (effects arriving 2-3 frames late)
- Juicing unimportant things more than core actions
- Starting juice before mechanics are finalized
- Visual juice without corresponding audio

**The fundamental rule: juice is communication, not decoration** — every effect should convey that a hit connected, the player is in danger, or an action succeeded.
