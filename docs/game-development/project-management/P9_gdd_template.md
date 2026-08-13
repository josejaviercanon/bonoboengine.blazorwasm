# 13 — Game Design Document Template

> *Copy this entire file, rename it for your project, and fill in the blanks.*
> *Fields marked with `[___]` are yours to complete. Italic text is guidance — delete it once you've written your answer.*

---

## 1. Title Page

| Field | Value |
|---|---|
| **Game Title** | [___] |
| **Subtitle / Tagline** | [___] |
| **Document Version** | [___] *e.g., 0.1 — First Draft* |
| **Author(s)** | [___] |
| **Date** | [___] |
| **Genre** | [___] *e.g., Action-Platformer, Top-Down RPG, Roguelite Shooter* |
| **Platform Targets** | [___] *e.g., Windows, Linux, macOS, Steam Deck* |
| **Engine / Framework** | MonoGame + Arch ECS *(modify if different)* |
| **Target Rating** | [___] *e.g., E for Everyone, T for Teen* |
| **Status** | [___] *Concept / Pre-Production / Production / Polish* |

### Revision History

| Version | Date | Author | Changes |
|---|---|---|---|
| 0.1 | [___] | [___] | Initial draft |

---

## 2. Vision Statement

### Elevator Pitch (2-3 sentences)

[___]

*Example: "A hand-drawn metroidvania where you play as a lighthouse keeper exploring a sunken city beneath the waves. Tight combat meets environmental puzzle-solving as you restore light to forgotten places. Think Hollow Knight meets Spiritfarer."*

### The "X meets Y" Formula

**[___]** meets **[___]** with a twist of **[___]**

*This isn't reductive — it's communicative. It tells collaborators, publishers, and players what experience to expect. Pick references that capture different aspects: one for mechanics, one for tone/feel.*

### Core Fantasy

What does the player get to *feel* or *be*?

[___]

*Example: "You are the last cartographer in a world that's forgetting itself. You feel like an explorer reclaiming lost knowledge, piecing together a map that literally reshapes reality."*

### What Makes This Unique?

What's the one thing no other game does quite like yours?

[___]

*If you can't answer this in one sentence, keep refining until you can. This is your hook.*

> **🪞 Ask yourself:**
> - If I describe this game without using its genre, is it still interesting?
> - Would I be excited to play this if someone else made it?
> - Can I explain why someone should play *this* instead of [closest competitor]?

---

## 3. Design Pillars

Design pillars are 3-5 principles that guide every decision. When you're unsure whether a feature belongs, run it through your pillars. If it doesn't serve at least one, cut it.

| # | Pillar | What It Means in Practice |
|---|---|---|
| 1 | [___] | [___] |
| 2 | [___] | [___] |
| 3 | [___] | [___] |
| 4 | [___] | [___] |
| 5 | [___] | [___] |

*Examples:*
- *"**Readable in a Glance** — The player should always understand the game state within 1 second of looking at the screen. No clutter."*
- *"**Earned Mastery** — Progression comes from player skill, not stat inflation. A new player with perfect execution beats a veteran with bad habits."*
- *"**Cozy Danger** — The world is hostile but never cruel. Death is a setback, not a punishment. The tone stays warm even when stakes are high."*
- *"**Every Run Tells a Story** — Procedural elements combine to create memorable, shareable moments. No two runs feel the same."*
- *"**Juice Everything** — Every action has feedback. Screen shake, particles, sound, animation. If it doesn't feel good to press the button, it's not done."*

> **🪞 Ask yourself:**
> - If a team member proposes a feature, can I use these pillars to say yes or no?
> - Do any of my pillars contradict each other? (That's okay — creative tension is useful, but be aware of it.)
> - Are these specific to *my* game, or could they describe any game? (If they're generic, sharpen them.)

---

## 4. Game Overview

### Quick Facts

| Field | Value |
|---|---|
| **Primary Genre** | [___] |
| **Secondary Genre(s)** | [___] |
| **Camera / Perspective** | [___] *e.g., Side-scrolling, Top-down, Isometric, Fixed-screen* |
| **Art Style** | [___] *e.g., Pixel art 16-bit, Hand-drawn, Vector, Low-poly 2D* |
| **Tone / Mood** | [___] *e.g., Whimsical, Dark & oppressive, Chill & meditative* |
| **Target Audience** | [___] *Who is this for? Be specific. Age, gaming habits, taste.* |
| **Typical Play Session** | [___] *e.g., 15-30 minute runs, 1-2 hour sessions* |
| **Total Playtime (Main Path)** | [___] *e.g., 8-12 hours* |
| **Total Playtime (Completionist)** | [___] *e.g., 25-40 hours* |
| **Replayability** | [___] *None / Low / Medium / High — and why* |

### Comparable Titles

List 3-5 games and what you're taking from each:

| Game | What You're Borrowing |
|---|---|
| [___] | [___] *e.g., "Celeste — tight movement feel, assist mode philosophy"* |
| [___] | [___] |
| [___] | [___] |
| [___] | [___] |
| [___] | [___] |

*These aren't clones — they're touchstones. You're building a constellation of influences, not copying a star.*

> **🪞 Ask yourself:**
> - Can I describe my target player as a real person? ("Alex, 28, plays indie games on Steam Deck during commute, loves Hades, bounced off Elden Ring")
> - Is my estimated playtime realistic for my scope? (Solo dev rule of thumb: every hour of content takes 40-100 hours to build.)

---

## 5. Core Gameplay Loop

> *See [E6 — Game Design Fundamentals](../game-design/E6_game_design_fundamentals.md) for loop theory and pacing concepts.*

### The Three Loops

**Moment-to-Moment (every ~10 seconds):**

[___]

*Example: "See enemy → dodge attack → counter → collect drop. The core verb chain is: move, dodge, strike, loot."*

**Session Loop (every ~10 minutes):**

[___]

*Example: "Explore room cluster → find key item → unlock new path → reach checkpoint. Each 'floor' is a self-contained challenge arc."*

**Meta Loop (every ~1 hour / across sessions):**

[___]

*Example: "Complete a run → spend currency on permanent upgrades → unlock new weapon class → start new run with expanded options."*

### Loop Diagram

```
┌─────────────────────────────────────────────────┐
│                   META LOOP                      │
│  [___]                                           │
│  ┌───────────────────────────────────────────┐  │
│  │              SESSION LOOP                  │  │
│  │  [___]                                     │  │
│  │  ┌─────────────────────────────────────┐  │  │
│  │  │        MOMENT-TO-MOMENT             │  │  │
│  │  │  [___]                              │  │  │
│  │  │  Action → Feedback → Decision →     │  │  │
│  │  │  Action → ...                       │  │  │
│  │  └─────────────────────────────────────┘  │  │
│  └───────────────────────────────────────────┘  │
└─────────────────────────────────────────────────┘
```

### Core Verbs

What does the player *do*? List the primary actions:

| Verb | Input | Frequency | Feel Target |
|---|---|---|---|
| [___] | [___] | [___] | [___] |
| [___] | [___] | [___] | [___] |
| [___] | [___] | [___] | [___] |
| [___] | [___] | [___] | [___] |

*e.g., "Jump — A button — Constant — Snappy, responsive, 2-frame coyote time"*

> **🪞 Ask yourself:**
> - Is my moment-to-moment loop fun on its own, even without progression or story?
> - Does the session loop provide satisfying "arcs" — rising tension and release?
> - Does the meta loop give me a reason to come back tomorrow?

---

## 6. Mechanics

> *For each mechanic: what is it, how does the player use it, what does the system do in response, and what knobs can you tune?*

### 6.1 Movement

#### [Mechanic Name: ___]

| Field | Description |
|---|---|
| **Description** | [___] |
| **Player Input** | [___] *e.g., Left stick / WASD for direction, A / Space to jump* |
| **System Response** | [___] *e.g., Character accelerates to max speed over 0.1s, decelerates over 0.15s* |
| **Feel Target** | [___] *e.g., "Tight and responsive like Celeste, not floaty like LittleBigPlanet"* |
| **Tuning Variables** | [___] *e.g., max_speed, acceleration, deceleration, jump_height, gravity, coyote_time, jump_buffer* |
| **Edge Cases** | [___] *e.g., What happens at ledges? Slopes? Moving platforms?* |

*Repeat for each movement mechanic (walking, jumping, dashing, climbing, swimming, etc.)*

### 6.2 Combat

#### [Mechanic Name: ___]

| Field | Description |
|---|---|
| **Description** | [___] |
| **Player Input** | [___] |
| **System Response** | [___] |
| **Feel Target** | [___] |
| **Tuning Variables** | [___] *e.g., damage, attack_speed, hitbox_size, hitstun_duration, i-frames* |
| **Feedback** | [___] *e.g., Screen shake (2px, 0.1s), hit flash (white, 2 frames), SFX, particle burst* |

*Repeat for each combat mechanic (melee, ranged, special abilities, blocking, parrying, etc.)*

### 6.3 Progression Mechanics

#### [Mechanic Name: ___]

| Field | Description |
|---|---|
| **Description** | [___] |
| **How It's Earned** | [___] *e.g., XP from kills, quest completion, exploration* |
| **What It Unlocks** | [___] |
| **Pacing** | [___] *e.g., "Level up every 15-20 minutes in early game, every 30-45 in late game"* |
| **Tuning Variables** | [___] |

### 6.4 Exploration

#### [Mechanic Name: ___]

| Field | Description |
|---|---|
| **Description** | [___] |
| **Discovery Method** | [___] *e.g., Map reveal, fog of war, breadcrumbs, environmental cues* |
| **Rewards** | [___] *e.g., Lore, currency, equipment, shortcuts, cosmetics* |
| **Gating** | [___] *e.g., Ability-gated, key-gated, skill-gated, story-gated* |

### 6.5 Social / Multiplayer

*Skip if single-player only.*

[___] *or "N/A — Single-player only"*

### 6.6 Economy

#### [Mechanic Name: ___]

| Field | Description |
|---|---|
| **Currencies** | [___] *e.g., Gold (common), Essence (rare), Tokens (event)* |
| **Sources** | [___] *Where does currency come from?* |
| **Sinks** | [___] *Where does currency go?* |
| **Balance Target** | [___] *e.g., "Player should always feel slightly short of affording the next upgrade"* |

> **🪞 Ask yourself:**
> - Can I prototype each mechanic independently to test if it's fun?
> - Do any mechanics overlap or conflict? (Two healing systems, redundant currencies, etc.)
> - For every input, is the feedback satisfying? (No silent, invisible actions.)

---

## 7. Progression Systems

### Progression Overview

How does the player grow over the course of the game?

| System | Type | Description |
|---|---|---|
| [___] | [___] *e.g., Stat growth, Unlock tree, Equipment* | [___] |
| [___] | [___] | [___] |
| [___] | [___] | [___] |

### Power Curve

Describe how player power scales over the game:

```
Power
  ▲
  │          ╱‾‾‾‾‾
  │        ╱
  │      ╱
  │    ╱
  │  ╱
  │╱
  └──────────────────► Time
  Early    Mid    Late    Post
```

[___] *Describe your intended curve. Linear? Exponential? Stepped? Does the player plateau? Are there power spikes at key moments?*

### Unlock Sequence

What order does the player gain abilities/tools?

| Order | Unlock | When | Impact on Gameplay |
|---|---|---|---|
| 1 | [___] | [___] | [___] |
| 2 | [___] | [___] | [___] |
| 3 | [___] | [___] | [___] |
| ... | ... | ... | ... |

*Each unlock should open new possibilities — new areas to reach, new enemy strategies, new movement options.*

### Skill Tree / Upgrade Paths

*If applicable. Describe the structure:*

[___]

*Is it a tree (branching, exclusive choices), a web (interconnected), a list (linear unlocks), or freeform?*

> **🪞 Ask yourself:**
> - Does the player feel more powerful at the end than the beginning? How specifically?
> - Are there meaningful choices, or is there one "correct" build?
> - Can a player who skips optional progression still finish the game?

---

## 8. Content Plan

### 8.1 Levels / Areas / Worlds

| # | Area Name | Theme/Biome | Unique Mechanic | Est. Playtime | Est. Dev Time |
|---|---|---|---|---|---|
| 1 | [___] | [___] | [___] | [___] | [___] |
| 2 | [___] | [___] | [___] | [___] | [___] |
| 3 | [___] | [___] | [___] | [___] | [___] |
| ... | ... | ... | ... | ... | ... |

**Total areas:** [___]
**Total levels/rooms:** [___]

### 8.2 Enemy Roster

| Enemy | Area | Behavior | Difficulty | Unique Trait |
|---|---|---|---|---|
| [___] | [___] | [___] *e.g., Patrol, Chase, Ambush* | [___] *1-5* | [___] |
| [___] | [___] | [___] | [___] | [___] |
| [___] | [___] | [___] | [___] | [___] |

**Total enemy types:** [___]
**Variants (reskins/upgrades):** [___]

### 8.3 Boss List

| Boss | Area | Mechanics | Phases | Est. Fight Length |
|---|---|---|---|---|
| [___] | [___] | [___] | [___] | [___] |
| [___] | [___] | [___] | [___] | [___] |

### 8.4 Items & Equipment

| Item | Type | Effect | Source | Rarity |
|---|---|---|---|---|
| [___] | [___] *Weapon/Armor/Consumable/Key/Passive* | [___] | [___] | [___] |
| [___] | [___] | [___] | [___] | [___] |

**Total items:** [___]

### 8.5 NPCs

| NPC | Role | Location | Purpose |
|---|---|---|---|
| [___] | [___] *e.g., Shopkeeper, Quest-giver, Lore* | [___] | [___] |
| [___] | [___] | [___] | [___] |

### Content Volume Summary

| Content Type | Count | Est. Hours to Build |
|---|---|---|
| Areas/Worlds | [___] | [___] |
| Individual Rooms/Levels | [___] | [___] |
| Enemy Types | [___] | [___] |
| Boss Fights | [___] | [___] |
| Items | [___] | [___] |
| NPCs | [___] | [___] |
| **Total** | | **[___]** |

> **🪞 Ask yourself:**
> - Is my content volume realistic for my team size and timeline?
> - Am I front-loading content creation or can I ship a vertical slice first?
> - What's the minimum content needed for the game to feel "complete"?

---

## 9. Narrative & World

### Setting

| Field | Description |
|---|---|
| **World Name** | [___] |
| **Time Period / Era** | [___] |
| **Geography** | [___] |
| **Rules of the World** | [___] *e.g., Magic exists but has a cost, technology stopped advancing 100 years ago* |
| **Tone** | [___] *e.g., Melancholic hope, dark humor, childlike wonder* |

### Lore Summary

[___]

*2-3 paragraphs covering the world's history and current state. What happened before the game starts? What's the status quo the player enters?*

### Story Structure

| Element | Description |
|---|---|
| **Story Type** | [___] *e.g., Linear, Branching, Environmental-only, Emergent, None* |
| **Narrative Delivery** | [___] *e.g., Dialogue, Environmental storytelling, Cutscenes, Item descriptions, NPC conversations, None* |
| **Inciting Incident** | [___] *What kicks off the game?* |
| **Central Conflict** | [___] |
| **Key Turning Points** | [___] |
| **Resolution** | [___] |
| **Endings** | [___] *Single ending? Multiple? Secret ending?* |

### Key Characters

| Character | Role | Motivation | Arc |
|---|---|---|---|
| **Player Character** | [___] | [___] | [___] |
| [___] | [___] | [___] | [___] |
| [___] | [___] | [___] | [___] |

### Dialogue Approach

[___]

*How much dialogue is there? Is it voiced? Branching? Does the player character speak? What's the writing style?*

*Example: "Minimal dialogue. NPCs speak in 2-3 sentence fragments. No dialogue trees. Player character is silent. Tone is poetic and sparse — think Hyper Light Drifter, not Disco Elysium."*

> **🪞 Ask yourself:**
> - Does my narrative serve the gameplay, or compete with it?
> - Can a player skip/ignore the story and still enjoy the game?
> - Am I writing a novel or making a game? (Both is fine — just be intentional.)

---

## 10. Art Direction

### Style Overview

| Field | Description |
|---|---|
| **Art Style** | [___] *e.g., "16-bit pixel art with modern lighting effects"* |
| **Base Resolution** | [___] *e.g., 320×180 scaled 4x to 1280×720* |
| **Tile Size** | [___] *e.g., 16×16, 32×32* |
| **Sprite Size (Character)** | [___] *e.g., 16×24, 32×32* |
| **Color Palette** | [___] *e.g., Limited 32-color palette, specific palette name, custom* |
| **Color Approach** | [___] *e.g., Each area has a dominant color. Warm = safe, cool = danger.* |
| **Animation Style** | [___] *e.g., "Snappy 4-frame anims with smear frames for attacks"* |

### Reference Images / Games

| Reference | What to Take From It |
|---|---|
| [___] | [___] |
| [___] | [___] |
| [___] | [___] |

### Character Art

[___] *Describe your character design philosophy. Readable silhouettes? Exaggerated proportions? Realistic?*

### Environment Art

[___] *Layered parallax? Detailed single-layer? How do you handle foreground/background? Lighting approach?*

### UI Art Style

[___] *Diegetic (in-world)? Clean/minimal? Skeuomorphic? Pixel art UI or high-res overlay?*

### Animation Philosophy

[___]

*Example: "Prioritize game feel over visual fidelity. Anticipation frames are short (1-2f), action frames are instant, follow-through is generous (3-4f). Attacks use smear frames. Idle animations are charming and long (30+ frames with personality)."*

> **🪞 Ask yourself:**
> - Can I (or my artist) actually produce this art style consistently?
> - At the target resolution, are characters readable and distinct?
> - Does the art style match the game's tone?

---

## 11. Audio Direction

### Music

| Field | Description |
|---|---|
| **Style** | [___] *e.g., Chiptune, Orchestral, Lo-fi, Synth-wave, Ambient* |
| **Adaptive Music?** | [___] *Does the music change with gameplay state?* |
| **Track Count (Est.)** | [___] *e.g., 12-15 tracks* |
| **Reference Tracks** | [___] *Specific songs/OSTs that capture the vibe* |

### Sound Effects

| Category | Approach |
|---|---|
| **Player Actions** | [___] *e.g., Punchy, exaggerated, satisfying. No realism needed.* |
| **Enemy/World** | [___] |
| **UI** | [___] *e.g., Soft clicks, no harsh sounds, subtle confirmation tones* |
| **Ambience** | [___] *e.g., Wind, rain, cave drips — layered per environment* |

### Audio References

| Game / Soundtrack | What You Like About It |
|---|---|
| [___] | [___] |
| [___] | [___] |
| [___] | [___] |

### Audio Budget

| Asset Type | Est. Count | Source |
|---|---|---|
| Music Tracks | [___] | [___] *Self-made / Commissioned / Licensed / AI-assisted* |
| SFX | [___] | [___] |
| Voice Lines | [___] | [___] *or "None"* |

> **🪞 Ask yourself:**
> - Does the music reinforce the emotional arc of each area/moment?
> - Can I prototype with placeholder audio early? (Yes. Do this.)
> - What does silence sound like in my game? Is silence ever used intentionally?

---

## 12. UI/UX Design

> *See [G5 — UI Framework](../../monogame-arch/guides/G5_ui_framework.md) for implementation patterns, [G7 — Input Handling](../../monogame-arch/guides/G7_input_handling.md) for input architecture, and [G35 — Accessibility](../../monogame-arch/guides/G35_accessibility.md) for accessibility guidelines.*

### Screen Flow

```
[Title Screen]
    │
    ├── New Game → [Intro/Cutscene] → [Gameplay]
    ├── Continue → [Gameplay]
    ├── Settings → [Settings Menu]
    └── Quit
    
[Gameplay]
    │
    ├── Pause → [Pause Menu]
    │       ├── Resume
    │       ├── Settings
    │       └── Quit to Title
    ├── Inventory → [Inventory Screen]
    ├── Map → [Map Screen]
    ├── Death → [Game Over Screen]
    │       ├── Retry
    │       └── Quit to Title
    └── [Victory] → [Credits]
```

*Modify this flow to match your game. Add/remove screens as needed.*

### HUD Elements

| Element | Position | Always Visible? | Description |
|---|---|---|---|
| [___] *e.g., Health* | [___] *e.g., Top-left* | [___] | [___] |
| [___] | [___] | [___] | [___] |
| [___] | [___] | [___] | [___] |

**HUD Philosophy:** [___] *e.g., "Minimal. Only health and currency. Everything else is contextual and fades in when relevant."*

### Control Scheme

**Keyboard + Mouse:**

| Action | Key |
|---|---|
| Move | [___] |
| Jump / Confirm | [___] |
| Attack | [___] |
| Special | [___] |
| Dash/Dodge | [___] |
| Interact | [___] |
| Pause | [___] |
| Inventory/Map | [___] |

**Gamepad:**

| Action | Button |
|---|---|
| Move | [___] |
| Jump / Confirm | [___] |
| Attack | [___] |
| Special | [___] |
| Dash/Dodge | [___] |
| Interact | [___] |
| Pause | [___] |
| Inventory/Map | [___] |

**Remappable?** [___] *Yes (strongly recommended) / No*

### Accessibility

| Feature | Included? | Notes |
|---|---|---|
| Remappable controls | [___] | |
| Colorblind options | [___] | |
| Screen reader support | [___] | |
| Adjustable text size | [___] | |
| Difficulty options | [___] | *e.g., Assist mode, God mode, speed adjust* |
| Audio cues for visuals | [___] | |
| Subtitle options | [___] | |
| One-handed mode | [___] | |

*Not everything is feasible for every project — but consider each one. See [G35 — Accessibility](../../monogame-arch/guides/G35_accessibility.md) for implementation guidance.*

> **🪞 Ask yourself:**
> - Can a new player understand my HUD in the first 10 seconds?
> - Does my game work well on both keyboard and gamepad?
> - Have I tested with someone who doesn't play games regularly?

---

## 13. Technical Design

> *See [R1 — Library Stack](../../monogame-arch/reference/R1_library_stack.md) for the full library reference.*

### Architecture Overview

| Layer | Description |
|---|---|
| **ECS Framework** | Arch ECS *(or: [___])* |
| **Scene Management** | [___] *e.g., Stack-based scene manager with transition support* |
| **Physics** | [___] *e.g., Custom AABB, Box2D, Aether.Physics2D* |
| **Rendering** | [___] *e.g., SpriteBatch with custom shader pipeline* |
| **Audio Engine** | [___] *e.g., FMOD, MonoGame.Extended, custom* |
| **UI System** | [___] *e.g., Custom immediate-mode, retained-mode, library name* |
| **Serialization** | [___] *e.g., JSON via System.Text.Json, MessagePack* |

### Key ECS Components

| Component | Fields | Used By |
|---|---|---|
| [___] *e.g., Position* | [___] *e.g., float X, Y* | [___] *e.g., RenderSystem, PhysicsSystem* |
| [___] | [___] | [___] |
| [___] | [___] | [___] |
| [___] | [___] | [___] |

### Key Systems

| System | Update Order | Purpose |
|---|---|---|
| [___] *e.g., InputSystem* | [___] *e.g., 1* | [___] |
| [___] | [___] | [___] |
| [___] | [___] | [___] |

### Save System

| Field | Description |
|---|---|
| **What's Saved** | [___] *e.g., Player position, inventory, world state, quest flags* |
| **Save Method** | [___] *e.g., Manual save points, auto-save on room transition, continuous* |
| **Save Format** | [___] *e.g., JSON, binary, encrypted* |
| **Save Slots** | [___] |
| **Cloud Saves** | [___] *e.g., Steam Cloud, manual backup, none* |

### Performance Targets

| Metric | Target |
|---|---|
| **Frame Rate** | [___] *e.g., Locked 60fps* |
| **Max Entities** | [___] *e.g., 500 active entities per scene* |
| **Load Time** | [___] *e.g., < 2 seconds for scene transitions* |
| **Memory Budget** | [___] *e.g., < 512MB RAM* |
| **Min Spec** | [___] *e.g., Integrated graphics, 2015-era hardware* |

### Platform Considerations

[___] *e.g., "Steam Deck: verify all UI is readable at 1280×800. Ensure 60fps docked and handheld. Support suspend/resume."*

> **🪞 Ask yourself:**
> - Can I describe my architecture to someone in 2 minutes?
> - Are my performance targets realistic for my content volume?
> - Have I identified the riskiest technical challenge? What's the spike/prototype plan?

---

## 14. Scope & Schedule

> *See [Playbook 03 — Milestones & Sprints](03_milestones_sprints.md) for milestone planning methodology.*

### Scope Summary

| Metric | Value |
|---|---|
| **Total Estimated Hours** | [___] |
| **Team Size** | [___] |
| **Target Timeline** | [___] *e.g., 12 months to EA, 18 months to 1.0* |
| **Work Schedule** | [___] *e.g., 20 hrs/week evenings & weekends* |

### Milestone Timeline

| Milestone | Target Date | Deliverable | Hours Est. |
|---|---|---|---|
| **Concept / GDD** | [___] | This document, mood boards, reference collection | [___] |
| **Prototype** | [___] | Core loop playable (programmer art), 1 level | [___] |
| **Vertical Slice** | [___] | One complete area at shippable quality | [___] |
| **Alpha** | [___] | All mechanics implemented, 50%+ content | [___] |
| **Beta** | [___] | Content-complete, bug fixing, polish pass | [___] |
| **Release Candidate** | [___] | Feature-frozen, final QA | [___] |
| **Launch** | [___] | Ship it 🚀 | [___] |

### MVP Feature List (Must-Have for Launch)

- [ ] [___]
- [ ] [___]
- [ ] [___]
- [ ] [___]
- [ ] [___]

### Nice-to-Have (Post-Launch or If Time Allows)

- [ ] [___]
- [ ] [___]
- [ ] [___]

### Cut List (Things You've Already Decided to Cut)

*It's healthy to pre-cut. It means you've thought about scope.*

- [___] — *Reason: [___]*
- [___] — *Reason: [___]*

### Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| [___] *e.g., Burnout* | [___] | [___] | [___] *e.g., Enforce 2 days off/week, track hours* |
| [___] *e.g., Scope creep* | [___] | [___] | [___] *e.g., Feature freeze at Alpha, refer to this GDD* |
| [___] *e.g., Tech risk (netcode)* | [___] | [___] | [___] *e.g., Prototype first, cut MP if prototype fails* |
| [___] | [___] | [___] | [___] |

> **🪞 Ask yourself:**
> - If I cut 30% of the content, is the game still fun and complete?
> - What's my "if life happens" plan? Can I ship a smaller version?
> - Am I tracking my hours? (You should be. You'll be shocked.)

---

## 15. Marketing & Release

### Release Plan

| Field | Value |
|---|---|
| **Target Platforms** | [___] |
| **Storefront(s)** | [___] *e.g., Steam, itch.io, Epic, GOG* |
| **Pricing** | [___] *e.g., $14.99 USD* |
| **Early Access?** | [___] *Yes/No — if yes, what's the EA plan?* |
| **Demo?** | [___] *Yes/No — when?* |
| **Release Window** | [___] *e.g., Q2 2026, "when it's ready"* |

### Marketing Channels

| Channel | Strategy | Timeline |
|---|---|---|
| **Steam Page** | [___] *e.g., Live 6 months before launch* | [___] |
| **Social Media** | [___] *e.g., Weekly devlog on Twitter/Bluesky* | [___] |
| **Reddit** | [___] *e.g., Monthly updates in r/indiegaming* | [___] |
| **YouTube / TikTok** | [___] *e.g., Short devlog clips, GIF captures* | [___] |
| **Press / Streamers** | [___] *e.g., Press kit ready at Beta, keys to small streamers* | [___] |
| **Festivals / Events** | [___] *e.g., Steam Next Fest demo, indie showcases* | [___] |

### Goals

| Metric | Target |
|---|---|
| **Steam Wishlists at Launch** | [___] *Rule of thumb: 7,000-10,000 for a viable indie launch* |
| **First Month Sales** | [___] |
| **Revenue Goal** | [___] *What does success look like for you?* |
| **Review Target** | [___] *e.g., Mostly Positive (70%+)* |

> **🪞 Ask yourself:**
> - Am I building in public? (You should be — it's free marketing.)
> - Do I have a Steam page up yet? (Put it up as early as possible.)
> - What's my realistic financial outcome, and am I okay with it?

---

## 16. Appendices

### A. Glossary

| Term | Definition |
|---|---|
| [___] | [___] |
| [___] | [___] |
| [___] | [___] |

*Define game-specific terms, acronyms, and jargon your GDD uses.*

### B. Reference Links

| Resource | Link | Notes |
|---|---|---|
| Toolkit Docs | [___] | |
| Art References | [___] | |
| Music References | [___] | |
| Competitor Games | [___] | |
| Tutorials Used | [___] | |

### C. Inspiration Board

*List images, games, films, books, music — anything that feeds the creative vision.*

| Reference | Medium | What It Inspires |
|---|---|---|
| [___] | [___] | [___] |
| [___] | [___] | [___] |
| [___] | [___] | [___] |

### D. Competitive Analysis

*For each competitor/comparable game, analyze what they do well and where you differ.*

| Game | Strengths | Weaknesses | How Yours Differs |
|---|---|---|---|
| [___] | [___] | [___] | [___] |
| [___] | [___] | [___] | [___] |
| [___] | [___] | [___] | [___] |

---

## Using This Document

**This GDD is a living document.** Update it as your game evolves. Some sections will be sparse early on and fill in over production. That's normal.

**How to work with it:**

1. **Pre-production:** Fill out Sections 1-5 completely. Sketch 6-9. Leave 14-15 rough.
2. **Prototype phase:** Refine Section 6 (Mechanics) as you playtest. Update 7-8 with real data.
3. **Production:** Sections 8-13 become your source of truth. Track scope in Section 14.
4. **Pre-launch:** Section 15 becomes critical. Start marketing *before* the game is done.

**When in doubt, check your Design Pillars (Section 3).** They're your compass.

---

> *Template version 1.0 — Part of the [Universal 2D Engine Toolkit](../README.md)*
