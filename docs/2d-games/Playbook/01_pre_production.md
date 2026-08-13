# 02 — Pre-Production Checklist
> **Phase:** Pre-Production (Weeks 2–3) · **Goal:** Answer every major question before writing game code
> **Related:** [00 Master Playbook](./00_master_playbook.md) · [E4 Solo Project Management](../E/E4_project_management.md) · [E6 Game Design Fundamentals](../E/E6_game_design_fundamentals.md) · [E9 Solo Dev Playbook](../E/E9_solo_dev_playbook.md)

---

> *"Weeks of coding can save you hours of planning."* — Unknown
>
> This document is everything you fill out, decide, and set up **before you write a single line of game code.**
> Work through it top to bottom. By the end, you'll have a game concept, a lightweight design doc,
> a scoped plan, art and tech decisions locked in, a repo ready to go, and a list of risks with
> a plan to prove each one out. Skip this and you'll pay for it in month 3.

---

## Table of Contents

1. [Game Concept Worksheet](#1-game-concept-worksheet)
2. [Genre Selection Guide](#2-genre-selection-guide)
3. [Design Doc Template (Lightweight GDD)](#3-design-doc-template-lightweight-gdd)
4. [Scope Assessment](#4-scope-assessment)
5. [Art Style Decision Matrix](#5-art-style-decision-matrix)
6. [Technical Decisions Checklist](#6-technical-decisions-checklist)
7. [Project Setup Checklist](#7-project-setup-checklist)
8. [Risk Assessment & Prototype Plan](#8-risk-assessment--prototype-plan)

---

## 1. Game Concept Worksheet

Fill this out first. If you can't fill every field, your idea isn't ready yet.

⏱️ *Time: 1–2 hours*

```
┌─────────────────────────────────────────────────────────────────────┐
│                      GAME CONCEPT WORKSHEET                        │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  Working Title: ___________________________________________________│
│                                                                     │
│  Elevator Pitch (2 sentences max):                                  │
│  _________________________________________________________________ │
│  _________________________________________________________________ │
│                                                                     │
│  "It's [Game A] meets [Game B]":                                    │
│  _________________________________________________________________ │
│                                                                     │
│  Genre: ________________________  Sub-genre: _____________________ │
│                                                                     │
│  Core Mechanic (the ONE thing the player does most):                │
│  _________________________________________________________________ │
│                                                                     │
│  What Makes It Different (unique hook):                             │
│  _________________________________________________________________ │
│                                                                     │
│  Core Fantasy (what the player FEELS like):                         │
│  _________________________________________________________________ │
│                                                                     │
│  Target Audience:                                                   │
│    Age Range: ____________  Skill Level: ☐ Casual ☐ Mid ☐ Core    │
│    Similar Games They Play: ______________________________________ │
│                                                                     │
│  Platform Targets:                                                  │
│    ☐ Windows   ☐ macOS   ☐ Linux                                  │
│    ☐ iOS       ☐ Android ☐ Web (unlikely with MonoGame)           │
│    ☐ Steam Deck (Linux + controller)                               │
│    Primary: ____________  Secondary: ____________                  │
│                                                                     │
│  Estimated Scope:                                                   │
│    ☐ Small  (3–4 months, <10 levels/areas, 1 core mechanic)       │
│    ☐ Medium (5–8 months, 10–30 levels/areas, 2–3 mechanics)       │
│    ☐ Large  (9–18 months, 30+ levels/areas, interlocking systems) │
│                                                                     │
│  Target Session Length: _____ minutes                               │
│  Total Playtime: _____ hours                                        │
│  Price Point: ☐ Free ☐ $1–5 ☐ $5–10 ☐ $10–20 ☐ $20+            │
│                                                                     │
│  Inspirations / References:                                         │
│    1. _________________________ (what to take: _________________)  │
│    2. _________________________ (what to take: _________________)  │
│    3. _________________________ (what to take: _________________)  │
│                                                                     │
│  One-Sentence "Done" Criteria:                                      │
│  (When is this game FINISHED? Be specific.)                         │
│  _________________________________________________________________ │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

### Concept Validation Questions

Before proceeding, answer honestly:

- [ ] Can you describe the core loop in one sentence?
- [ ] Have you played at least 3 games in this genre recently?
- [ ] Can you identify what your game does that those games don't?
- [ ] Does the scope match your available time? (Be honest.)
- [ ] Is the core mechanic fun in isolation, without art or sound?
- [ ] Could you build a playable prototype of the core mechanic in 1 week?

> **If you answered "no" to 2+ of these:** Step back. Play more games in your genre, narrow the scope, or find a simpler core mechanic. See [E6 Game Design Fundamentals](../E/E6_game_design_fundamentals.md) for design pillar methodology.

---

## 2. Genre Selection Guide

Use this alongside [C1 Genre Reference](../C/C1_genre_reference.md) (which maps genres → systems → toolkit docs) and [C2 Game Feel & Genre Craft](../C/C2_game_feel_and_genre_craft.md) (which covers *how to make each genre feel good*).

⏱️ *Time: 30 minutes to review, but take days to decide*

### Solo Dev Suitability Matrix

| Genre | Solo Feasibility | Dev Time (MVP) | Content Volume | Technical Complexity | Art Burden | Notes |
|-------|:---:|:---:|:---:|:---:|:---:|-------|
| **Action Platformer** | ⭐⭐⭐⭐⭐ | 3–5 mo | Medium | Medium | Low–Med | Best solo genre. Level design IS content. Small art sets stretch far |
| **Puzzle Platformer** | ⭐⭐⭐⭐⭐ | 2–4 mo | Low | Low–Med | Low | Fewest art assets. Mechanics create replayability |
| **Top-Down Action** | ⭐⭐⭐⭐ | 4–6 mo | Medium | Medium | Medium | 4/8-dir sprites multiply animation work |
| **Roguelite** | ⭐⭐⭐⭐ | 4–8 mo | Med–High | High | Low–Med | Procgen extends content, but balance is HARD |
| **Metroidvania** | ⭐⭐⭐ | 6–12 mo | High | High | High | Huge interconnected maps. Don't start here |
| **Turn-Based RPG** | ⭐⭐⭐ | 6–12 mo | Very High | Medium | High | Content treadmill: maps, enemies, items, dialogue, skills |
| **Visual Novel** | ⭐⭐⭐⭐ | 2–4 mo | Medium | Low | Med–High | Writing IS the game. Art is character portraits + backgrounds |
| **Tower Defense** | ⭐⭐⭐⭐ | 3–5 mo | Medium | Medium | Low–Med | One map can carry many waves. Good scope control |
| **Top-Down Shooter** | ⭐⭐⭐⭐⭐ | 2–4 mo | Low–Med | Medium | Low | Vampire Survivors proved minimal art works |
| **Card Game / Deckbuilder** | ⭐⭐⭐⭐ | 4–6 mo | High | Medium | Low | Card art is small images. Balance testing takes forever |
| **Simulation / Idle** | ⭐⭐⭐⭐ | 3–5 mo | Medium | Low–Med | Low | Numbers-driven. UI-heavy |

### Reading the Table

- **Solo Feasibility** — How realistic is shipping this alone? 5 stars = very doable
- **Dev Time** — MVP with ~20–30 hrs/week of focused work. Assume MonoGame + Arch ECS
- **Content Volume** — How much *stuff* (levels, dialogue, items, maps) you need to create
- **Art Burden** — How many unique sprites, animations, tilesets you need

### Genre Decision Checklist

- [ ] Reviewed genre requirements in [C1 Genre Reference](../C/C1_genre_reference.md)
- [ ] Studied feel techniques for chosen genre in [C2 Game Feel & Genre Craft](../C/C2_game_feel_and_genre_craft.md)
- [ ] Confirmed this genre matches my estimated scope (Section 4)
- [ ] I have personal experience *playing* this genre (at least 10+ hours)
- [ ] Identified which toolkit systems I'll need (from C1's "Systems" lists)

> **First game?** Pick Action Platformer, Puzzle Platformer, or Top-Down Shooter. These have the tightest feedback loops for learning, the smallest content requirements, and the most tutorial material in the wild.

---

## 3. Design Doc Template (Lightweight GDD)

This is not a 50-page GDD. It's a **living document** — 3–5 pages max — that captures decisions and evolves. Copy this template into your project repo.

⏱️ *Time: 3–6 hours to draft, then ongoing*

---

### 📄 Game Design Document: [Title]

**Version:** 0.1 · **Last Updated:** YYYY-MM-DD · **Author:** ___

---

#### 3.1 Vision Statement

> One paragraph. What is this game? What does it feel like to play? What emotion are you chasing?

*Write it here. Read it before every work session. If a feature doesn't serve this vision, cut it.*

#### 3.2 Design Pillars (pick 3, no more)

| Pillar | What It Means | Example Decision |
|--------|---------------|------------------|
| 1. ___________ | ___________ | ___________ |
| 2. ___________ | ___________ | ___________ |
| 3. ___________ | ___________ | ___________ |

> See [E6 Game Design Fundamentals § Design Pillars](../E/E6_game_design_fundamentals.md) for how to define and use these.

#### 3.3 Core Loop

Describe the **moment-to-moment** gameplay loop the player repeats for the entire game:

```
[ Action ] → [ Feedback ] → [ Reward ] → [ Decision ] → (repeat)
```

Fill in your game's specifics:

```
[ _________ ] → [ _________ ] → [ _________ ] → [ _________ ] → (repeat)
```

**Session loop** (what happens over a 30-minute play session):
```
_________________________________________________________________________
```

**Meta loop** (what happens across sessions / the whole game):
```
_________________________________________________________________________
```

> Reference: [G15 Game Loop](../G/G15_game_loop.md) for technical implementation of the core loop.

#### 3.4 Player Mechanics

| Mechanic | Description | Priority | In MVP? |
|----------|-------------|:--------:|:-------:|
| ___________ | ___________ | P0 | ☐ |
| ___________ | ___________ | P0 | ☐ |
| ___________ | ___________ | P1 | ☐ |
| ___________ | ___________ | P2 | ☐ |

P0 = Core (must ship) · P1 = Important (should ship) · P2 = Nice-to-have (cut first)

#### 3.5 Progression Systems

- **How does the player get stronger?** ___
- **How does the game get harder?** ___
- **How is new content introduced?** ___
- **What is the pacing curve?** (Intensity graph sketch: early/mid/late game)

#### 3.6 Content Plan

| Content Type | Count (MVP) | Count (Full) | Notes |
|-------------|:-----------:|:------------:|-------|
| Levels / Areas | ___ | ___ | |
| Enemy Types | ___ | ___ | |
| Boss Fights | ___ | ___ | |
| Items / Powerups | ___ | ___ | |
| NPCs / Dialogue | ___ | ___ | |
| Cutscenes | ___ | ___ | |
| Music Tracks | ___ | ___ | |
| SFX | ___ | ___ | |

#### 3.7 Art Style Direction

- **Style:** ☐ Pixel Art ☐ Hand-drawn ☐ Vector ☐ Mixed
- **Resolution:** ___ × ___ (see [Section 5](#5-art-style-decision-matrix) for decision matrix)
- **Color Palette:** ___ colors (link to palette: ___)
- **References:** (attach 3–5 screenshots of games with similar art direction)
- **Animation Approach:** ☐ Frame-by-frame (Aseprite) ☐ Skeletal (Spine) ☐ Tweened
- **Tileset Style:** ☐ Grid-locked ☐ Freeform ☐ Auto-tile ☐ N/A

> Reference: [G8 Content Pipeline](../G/G8_content_pipeline.md) for Aseprite integration, [G31 Animation State Machines](../G/G31_animation_state_machines.md) for sprite workflow.

#### 3.8 Audio Direction

- **Music Style:** ___ (genre, mood, BPM range, reference tracks)
- **Music Source:** ☐ Compose myself ☐ Commission ☐ Asset packs ☐ AI-generated
- **SFX Approach:** ☐ Record/Foley ☐ Synthesized (sfxr/Bfxr) ☐ Asset packs
- **Adaptive Audio?** ☐ Yes ☐ No — If yes, how? ___
- **Voice Acting?** ☐ No ☐ Grunts/gibberish ☐ Full VO

> Reference: [G6 Audio](../G/G6_audio.md) for MonoGame audio vs FMOD decision. If you need crossfading, ducking, or bus mixing → FMOD via FmodForFoxes.

#### 3.9 Controls

| Action | Keyboard | Gamepad | Touch (if mobile) |
|--------|----------|---------|-------------------|
| Move | WASD / Arrows | Left Stick / D-Pad | Virtual joystick |
| Jump / Confirm | Space / Z | A | Tap |
| Attack / Action | X / J | X | Button |
| Dash / Cancel | C / K | B | Swipe |
| Pause | Escape | Start | Pause button |
| Menu Navigate | Arrows | D-Pad / Stick | Touch |

> Reference: [G7 Input Handling](../G/G7_input_handling.md) for Apos.Input implementation, input buffering, and rebinding.

---

## 4. Scope Assessment

The number one killer of solo projects. Be brutally honest here.

⏱️ *Time: 1–2 hours*

### 4.1 Scope Estimation Worksheet

**Available dev time per week:** ___ hours
**Target release date:** ___
**Weeks until release:** ___
**Total available hours:** ___ hours

**Realistic productivity multiplier:** × 0.6
*(You will lose ~40% to bugs, refactoring, life, research, and motivation dips)*

**Actual productive hours:** ___ hours

Now estimate your major work areas:

| Work Area | Estimated Hours | Confidence | Notes |
|-----------|:--------------:|:----------:|-------|
| Core mechanics / gameplay | ___ | ☐ High ☐ Med ☐ Low | |
| Level/content creation | ___ | ☐ High ☐ Med ☐ Low | |
| Art assets | ___ | ☐ High ☐ Med ☐ Low | |
| Audio / Music | ___ | ☐ High ☐ Med ☐ Low | |
| UI / Menus | ___ | ☐ High ☐ Med ☐ Low | |
| Polish / Juice / Feel | ___ | ☐ High ☐ Med ☐ Low | |
| Testing / Bug fixes | ___ | ☐ High ☐ Med ☐ Low | |
| Platform/build/release | ___ | ☐ High ☐ Med ☐ Low | |
| Marketing / Store page | ___ | ☐ High ☐ Med ☐ Low | |
| **TOTAL** | **___** | | |

> **If TOTAL > Actual productive hours:** You must cut scope. No exceptions. See the Cut List below.

> **Low confidence items:** Multiply their estimate by 2×. You're wrong about how long things take when you've never done them before.

### 4.2 Red Flags for Scope Creep 🚩

Check any that apply to your project. Each one is a warning sign:

- [ ] "It'll have procedural generation" (but you've never built one)
- [ ] "There will be multiplayer" (add 3–6 months minimum)
- [ ] "Lots of different enemy types" (each one needs: art, animation, AI, balancing, playtesting)
- [ ] "A deep crafting/skill tree system" (design + balance + UI = months)
- [ ] "Branching narrative with consequences" (combinatorial explosion)
- [ ] "I'll figure out the art style later" (you won't; you'll redo it)
- [ ] "It's like [AAA game] but 2D" (that game had a team of 50+)
- [ ] You keep adding features to your notes instead of cutting them
- [ ] No clear "done" criteria — when is this game finished?
- [ ] The pitch requires the word "and" more than twice

> **3+ checked?** Your scope is too big. Cut or restructure before proceeding. See [E4 Solo Project Management](../E/E4_project_management.md) and [E9 Solo Dev Playbook](../E/E9_solo_dev_playbook.md).

### 4.3 The Cut List

Create three lists now, before you start. This is insurance.

**🟢 MVP (Must Ship)** — The game is not a game without these:
1. ___
2. ___
3. ___
4. ___
5. ___

**🟡 Should Ship** — Makes the game significantly better, but it works without them:
1. ___
2. ___
3. ___
4. ___
5. ___

**🔴 Cut First** — Cool ideas that you will sacrifice when time runs out:
1. ___
2. ___
3. ___
4. ___
5. ___

> **Rule:** When behind schedule, cut from 🔴 first, then 🟡. Never cut from 🟢 — if you can't ship 🟢, the project is too ambitious.

### 4.4 MVP Definition

Write your MVP in one paragraph. This is the **smallest version of your game that is still fun and complete.** Not a demo. Not a prototype. A small, finished game.

> _MVP:_ _______________________________________________________________
> _____________________________________________________________________
> _____________________________________________________________________

**MVP checklist:**
- [ ] Core mechanic is implemented and polished
- [ ] At least ___ levels/areas (minimum viable content)
- [ ] Main menu, pause, game over screens
- [ ] Save/load (if sessions > 15 minutes)
- [ ] Sound effects for all player actions
- [ ] At least 1 music track
- [ ] No game-breaking bugs
- [ ] Runs on target platform at 60fps

---

## 5. Art Style Decision Matrix

Lock this in early. Changing art style mid-project is a full restart on assets.

⏱️ *Time: 2–4 hours (including reference gathering)*

### 5.1 Style Comparison

| Factor | Pixel Art | Hand-Drawn / Painted | Vector / Clean |
|--------|:---------:|:--------------------:|:--------------:|
| **Learning curve** | Medium | High | Medium |
| **Speed per asset** | Fast (small), Slow (large) | Slow | Medium |
| **Animation ease** | Frame-by-frame (tedious but learnable) | Very tedious | Skeletal-friendly |
| **Consistency** | Easy to maintain | Hard — skill varies day to day | Easy |
| **Scaling** | Tricky (integer only) | Scales freely | Scales freely |
| **Tools** | Aseprite ($20, industry standard) | Krita, Photoshop, Procreate | Inkscape, Illustrator, Affinity |
| **Asset store fallback** | Tons of pixel art packs | Limited | Limited |
| **Solo dev recommendation** | ⭐⭐⭐⭐⭐ | ⭐⭐ | ⭐⭐⭐ |
| **Tile map friendly** | Excellent | Possible but harder | Possible |
| **Nostalgia factor** | High | Depends on style | Modern/clean |

### 5.2 Resolution Decision

Your game's **virtual resolution** determines how much world the player sees and how your art looks at every display size.

| Virtual Resolution | Pixel Size at 1080p | Style | Good For |
|:------------------:|:-------------------:|-------|----------|
| 320×180 | 6× | Chunky retro | NES-feel, Celeste-style |
| 384×216 | 5× | Classic retro | GBA-feel, good default for pixel art |
| 480×270 | 4× | Detailed pixel | SNES-feel, Stardew Valley |
| 640×360 | 3× | Hi-res pixel or small painted | Owlboy, detailed sprites |
| 960×540 | 2× | HD art, thin lines | Hand-drawn, vector |
| 1920×1080 | 1× | Native HD | Clean vector, UI-heavy games |

> **Decision:** My virtual resolution is **___ × ___**
> **Rationale:** ___

> Reference: [G19 Display, Resolution & Viewports](../G/G19_display_resolution_viewports.md) for implementation, scaling strategies, and aspect ratio handling. Also see [G25 Safe Areas & Adaptive Layout](../G/G25_safe_areas_adaptive_layout.md) for mobile.

### 5.3 Color Palette Planning

- [ ] **Chosen palette** (name or link): ___
  - Popular curated palettes: Lospec DB (lospec.com/palette-list), Endesga 32/64, PICO-8 (16 colors), Resurrect 64
- [ ] **Palette size:** ___ colors
  - 8–16: Tight, coherent, retro. Forces good design.
  - 32–64: Flexible. Good for detailed pixel art.
  - Unlimited: Only if you're confident in color theory.
- [ ] **Key colors assigned:**
  - Player: ___
  - Enemies: ___
  - Environment: ___
  - UI / Interactive elements: ___
  - Hazards / Danger: ___
- [ ] **Contrast check:** Player reads clearly against all background types?
- [ ] **Accessibility:** Tested with color-blind simulation? See [G35 Accessibility](../G/G35_accessibility.md)

### 5.4 Reference Board

Create a visual reference board before drawing anything. This is your art bible.

- [ ] Collect 10–20 screenshots from reference games
- [ ] Include: characters, environments, UI, effects, animations
- [ ] Arrange by category (characters / tiles / UI / effects)
- [ ] Note specific things to emulate: "I want Celeste's hair particle trail" or "Stardew's warm color grading"
- [ ] Store in project: `Assets/References/` (don't ship these, .gitignore them)

### 5.5 Sprite Specification Sheet

Lock these numbers before creating any art:

```
Character sprite size:    ___×___ pixels (e.g., 16×16, 32×32, 24×32)
Tile size:                ___×___ pixels (e.g., 16×16, 8×8)
Animation frame count:    Idle: ___ | Run: ___ | Jump: ___ | Attack: ___
Outline style:            ☐ None ☐ 1px dark ☐ 1px colored ☐ Selective
Sub-pixel animation:      ☐ Yes ☐ No (affects smoothness vs crispness)
```

> Reference: [G8 Content Pipeline](../G/G8_content_pipeline.md) for Aseprite → MonoGame workflow, [G28 Top-Down Perspective](../G/G28_top_down_perspective.md) for top-down sprite proportions.

---

## 6. Technical Decisions Checklist

Decide these before writing game code. Each choice is hard to change later.

⏱️ *Time: 2–3 hours*

### 6.1 Library Stack

Start with the full reference: [R1 Library Stack](../R/R1_library_stack.md) and [R2 Capability Matrix](../R/R2_capability_matrix.md).

**Tier 0 — Always Install (non-negotiable):**
- [x] MonoGame.Framework.DesktopGL
- [x] Arch 2.1.0 + Arch.System + Arch.System.SourceGenerator

**Tier 1 — Essential Infrastructure:**
- [ ] MonoGame.Extended (camera, collision shapes, math, Tiled maps)
- [ ] MonoGame.Extended.Content.Pipeline (Tiled/atlas importers)
- [ ] Gum.MonoGame (UI framework) → [G5](../G/G5_ui_framework.md)
- [ ] Apos.Input (input handling) → [G7](../G/G7_input_handling.md)
- [ ] FontStashSharp.MonoGame (runtime font rendering)
- [ ] MonoGame.Aseprite (direct .ase import) → [G8](../G/G8_content_pipeline.md)
- [ ] Aether.Physics2D (only if you need rigid body physics) → [G3](../G/G3_physics_and_collision.md)

**Tier 2 — Genre-Specific (pick what you need):**
- [ ] BrainAI (FSM, behavior trees, GOAP, pathfinding) → [G4](../G/G4_ai_systems.md)
- [ ] FmodForFoxes (advanced audio: buses, crossfade, spatial) → [G6](../G/G6_audio.md)
- [ ] LiteNetLib (networking) → [G9](../G/G9_networking.md)
- [ ] ImGui.NET (debug tools, editors) → [G16](../G/G16_debugging.md), [G29](../G/G29_game_editor.md)
- [ ] Arch.Relationships (entity hierarchies)
- [ ] Arch.EventBus (typed pub/sub)
- [ ] Arch.Persistence (save/load ECS state)

> **Decision:** My Tier 2 libraries: _______________________________________________

### 6.2 Resolution & Viewport Strategy

- [ ] **Virtual resolution:** ___ × ___ (from Section 5.2)
- [ ] **Scaling mode:**
  - ☐ Integer scaling (pixel-perfect, black bars at non-integer sizes)
  - ☐ Fractional scaling (fills screen, slight blur on pixel art)
  - ☐ Expand viewport (show more world on wider screens, like Terraria)
  - ☐ Letterbox/Pillarbox (fixed aspect ratio, bars on mismatch)
- [ ] **Target aspect ratio:** ☐ 16:9 ☐ 16:10 ☐ Flexible
- [ ] **Mobile support?** If yes, review [G25 Safe Areas](../G/G25_safe_areas_adaptive_layout.md)

> Reference: [G19 Display, Resolution & Viewports](../G/G19_display_resolution_viewports.md) — this doc has a complete decision table.

### 6.3 Input Scheme

- [ ] **Primary input:** ☐ Keyboard+Mouse ☐ Gamepad ☐ Touch ☐ All three
- [ ] **Simultaneous KB+Gamepad?** ☐ Yes ☐ No
- [ ] **Rebindable controls?** ☐ Yes ☐ No (Yes if selling on Steam)
- [ ] **Input buffering?** ☐ Yes (action games) ☐ No (turn-based/puzzle)
- [ ] **Analog movement?** ☐ Yes (stick) ☐ No (8-dir digital)

> Reference: [G7 Input Handling](../G/G7_input_handling.md) for Apos.Input setup, [C2 Game Feel](../C/C2_game_feel_and_genre_craft.md) for genre-specific input techniques (coyote time, jump buffering, etc.)

### 6.4 Save System Approach

Decide this early — it influences data architecture.

- [ ] **Does my game need saves?** (Sessions > 15 min = yes)
- [ ] **Save model:**
  - ☐ Auto-save only (checkpoints, save stations)
  - ☐ Manual save slots (1–3 slots)
  - ☐ Both
- [ ] **What to save:**
  - ☐ Player position + world state flags
  - ☐ Full ECS state snapshot (use Arch.Persistence)
  - ☐ Inventory / progression data only
  - ☐ Settings (separate file)
- [ ] **Save format:** ☐ JSON (debuggable) ☐ Binary (smaller, faster) ☐ Both
- [ ] **Save location:** `Environment.SpecialFolder.LocalApplicationData`
- [ ] **Cloud saves?** ☐ No ☐ Steam Cloud ☐ Other

> Reference: [G10 Custom Game Systems](../G/G10_custom_game_systems.md) for save/load patterns.

### 6.5 Scene Architecture

- [ ] **Scene types needed:** (check all)
  - ☐ Splash / Studio logo
  - ☐ Main menu
  - ☐ Gameplay (how many variants? ___)
  - ☐ Pause overlay
  - ☐ Inventory / Equipment
  - ☐ Dialogue / Cutscene
  - ☐ Battle (separate from field?)
  - ☐ Settings
  - ☐ Game Over / Results
  - ☐ Credits
- [ ] **Scene transition style:** ☐ Fade ☐ Wipe ☐ Pixelate ☐ Circle ☐ Cut

> Reference: [G1 Custom Code Recipes](../G/G1_custom_code_recipes.md) for scene manager implementation, [G42 Screen Transitions](../G/G42_screen_transitions.md) for transition effects, [G38 Scene Management](../G/G38_scene_management.md) for advanced scene patterns.

### 6.6 Camera Strategy

- [ ] **Camera type:**
  - ☐ Fixed (single screen, puzzle games)
  - ☐ Follow player (with deadzone + smoothing)
  - ☐ Room-based (snap to room boundaries, Zelda-style)
  - ☐ Free scroll (RTS, sim)
- [ ] **Camera features needed:**
  - ☐ Screen shake → [G20](../G/G20_camera_systems.md), [G30](../G/G30_game_feel_tooling.md)
  - ☐ Zoom in/out
  - ☐ Lookahead (camera leads player movement)
  - ☐ Camera bounds / limits
  - ☐ Split screen

> Reference: [G20 Camera Systems](../G/G20_camera_systems.md) for full implementation guide.

---

## 7. Project Setup Checklist

Everything you set up in the repo before writing game logic.

⏱️ *Time: 2–4 hours*

### 7.1 Repository Init

- [ ] Create Git repo: `git init` or create on GitHub/GitLab first
- [ ] Create `.gitignore` for MonoGame / C# / .NET:
  ```
  bin/
  obj/
  .vs/
  *.user
  *.suo
  .idea/
  *.DotSettings.user
  Content/bin/
  Content/obj/
  Assets/References/    # Don't ship reference art
  *.ase.bak             # Aseprite backups
  Thumbs.db
  .DS_Store
  ```
- [ ] Create `.gitattributes`:
  ```
  *.png binary
  *.ase binary
  *.ogg binary
  *.wav binary
  *.xnb binary
  ```
- [ ] Initial commit: "Initial project setup"
- [ ] Push to remote (GitHub recommended for Actions CI)
- [ ] Set up branch strategy: ☐ `main` only (solo) ☐ `main` + `dev` (team)

> Reference: [G44 Version Control](../G/G44_version_control.md) for Git workflow with game projects.

### 7.2 Solution & Project Structure

Follow the structure in [R3 Project Structure](../R/R3_project_structure.md):

- [ ] Create solution: `dotnet new sln -n MyGame`
- [ ] Create core project: `dotnet new mgdesktopgl -n MyGame.Core`
  - This holds 95%+ of your code. Platform-agnostic.
- [ ] Create launcher project: `dotnet new mgdesktopgl -n MyGame.Desktop`
  - Thin launcher that references Core. Contains `Program.cs` only.
- [ ] (Optional) Create `MyGame.iOS` / `MyGame.Android` launchers for mobile
- [ ] Set up `PrivateAssets=all` for MonoGame.Framework.DesktopGL in Core project
- [ ] Install Tier 0 + Tier 1 packages (from Section 6.1)
- [ ] Create folder structure in Core:
  ```
  src/
  ├── Core/           # GameApp, SceneManager, ServiceLocator
  ├── ECS/
  │   ├── Components/ # Pure data structs
  │   ├── Systems/    # Arch systems
  │   └── Tags/       # Tag components
  ├── Scenes/         # Scene subclasses
  ├── Rendering/      # Render layers, camera, post-processing
  ├── Collision/      # SpatialHash, shapes
  ├── Systems/        # Game-specific (Inventory, Dialogue, etc.)
  ├── Data/           # JSON data classes, constants
  └── Utils/          # Extensions, helpers
  ```
- [ ] Verify build: `dotnet build` succeeds
- [ ] Verify run: `dotnet run --project MyGame.Desktop` shows the cornflower blue window

### 7.3 Content Pipeline Setup

- [ ] Create `Content/` directory structure:
  ```
  Content/
  ├── Sprites/        # .ase / .aseprite files
  ├── Tilesets/       # Tileset .ase files
  ├── Maps/           # .tmx Tiled map files
  ├── Fonts/          # .ttf / .otf files
  ├── Audio/
  │   ├── Music/      # .ogg files
  │   └── SFX/        # .wav files
  ├── Shaders/        # .fx HLSL files
  └── Data/           # .json game data files
  ```
- [ ] Configure `Content.mgcb` with appropriate importers
- [ ] Test: load a placeholder sprite, display it on screen

> Reference: [G8 Content Pipeline](../G/G8_content_pipeline.md) for MGCB configuration and asset workflow.

### 7.4 CI / Build Automation

- [ ] Set up GitHub Actions (or equivalent):
  ```yaml
  # .github/workflows/build.yml
  name: Build
  on: [push, pull_request]
  jobs:
    build:
      runs-on: ubuntu-latest
      steps:
        - uses: actions/checkout@v4
        - uses: actions/setup-dotnet@v4
          with:
            dotnet-version: '9.0.x'
        - run: dotnet build --configuration Release
        - run: dotnet test --no-build --configuration Release
  ```
- [ ] Verify CI passes on first push

> Reference: [G32 Deployment & Platform Builds](../G/G32_deployment_platform_builds.md) for full CI/CD and publishing pipeline.

### 7.5 Task Tracking

Pick one and set it up. Don't track tasks in your head.

- [ ] **Tool chosen:** ☐ GitHub Issues + Projects ☐ Trello ☐ Notion ☐ Plain markdown TODO.md ☐ Other: ___
- [ ] Create initial task list from your MVP features (Section 4.3 🟢 list)
- [ ] Create milestones:
  - [ ] Milestone 1: Core mechanic prototype (Week 3–4)
  - [ ] Milestone 2: Vertical slice (Week 6–8)
  - [ ] Milestone 3: Alpha — all systems working (Week 12–16)
  - [ ] Milestone 4: Beta — content complete (Week 18–22)
  - [ ] Milestone 5: Release candidate (Week 23–24)

> Reference: [E4 Solo Project Management](../E/E4_project_management.md) for vertical slice methodology, Kanban tips, and avoiding the "tool-building trap."

---

## 8. Risk Assessment & Prototype Plan

Identify what could kill your project and prove it won't — **before** you're 3 months in.

⏱️ *Time: 1–2 hours to identify, 1–2 weeks to prototype*

### 8.1 Technical Risk Inventory

List everything about your game that you've **never built before** or that you're **unsure will work**:

| # | Risk / Unknown | Severity | My Experience | Needs Prototype? |
|:-:|----------------|:--------:|:-------------:|:-----------------:|
| 1 | ___________ | ☐ High ☐ Med ☐ Low | ☐ None ☐ Some ☐ Done it | ☐ Yes ☐ No |
| 2 | ___________ | ☐ High ☐ Med ☐ Low | ☐ None ☐ Some ☐ Done it | ☐ Yes ☐ No |
| 3 | ___________ | ☐ High ☐ Med ☐ Low | ☐ None ☐ Some ☐ Done it | ☐ Yes ☐ No |
| 4 | ___________ | ☐ High ☐ Med ☐ Low | ☐ None ☐ Some ☐ Done it | ☐ Yes ☐ No |
| 5 | ___________ | ☐ High ☐ Med ☐ Low | ☐ None ☐ Some ☐ Done it | ☐ Yes ☐ No |

**Common risks for 2D MonoGame projects:**

- Performance with large entity counts (1000+ enemies/bullets) — test with Arch ECS early
- Procedural generation quality (looks random, not designed)
- Tilemap rendering performance at scale → [G37 Tilemap Systems](../G/G37_tilemap_systems.md)
- Shader compatibility across platforms → [G27 Shaders & Effects](../G/G27_shaders_and_effects.md)
- Mobile touch input feeling responsive → [G7 Input Handling](../G/G7_input_handling.md)
- Pathfinding on large maps → [G40 Pathfinding](../G/G40_pathfinding.md)
- Complex UI layout (inventory, skill trees) → [G5 UI Framework](../G/G5_ui_framework.md)
- Save/load with complex game state → [G10 Custom Game Systems](../G/G10_custom_game_systems.md)
- Networking latency / desync (if multiplayer) → [G9 Networking](../G/G9_networking.md)
- Content pipeline issues with .ase/.tmx imports → [G8 Content Pipeline](../G/G8_content_pipeline.md)

### 8.2 Prototype Plan

For every "Yes" in the Needs Prototype column, define what you'll build and when.

| Prototype | What to Build | Success Criteria | Time Budget | Deadline |
|-----------|---------------|------------------|:-----------:|:--------:|
| ___________ | ___________ | ___________ | ___ days | Week ___ |
| ___________ | ___________ | ___________ | ___ days | Week ___ |
| ___________ | ___________ | ___________ | ___ days | Week ___ |

**Rules for prototypes:**
- Ugly is fine. Programmer art. Colored rectangles. No polish.
- Each prototype is **throwaway code** — don't try to reuse it
- If a prototype fails, you've saved yourself months. Celebrate that.
- If ALL prototypes succeed, you've de-risked the project. Proceed with confidence.

### 8.3 "Prove It Works" Milestones

These are **hard deadlines** where you must have evidence that the game works. If you can't hit them, the scope is wrong.

| Milestone | Deadline | Evidence Required | Pass? |
|-----------|:--------:|-------------------|:-----:|
| **Core mechanic is fun** | Week 3 | Someone (not you) plays it and wants to keep playing | ☐ |
| **Tech risks resolved** | Week 4 | All prototypes from 8.2 pass their success criteria | ☐ |
| **Vertical slice** | Week 8 | One complete level with final art, audio, UI, and feel | ☐ |
| **Content pipeline works** | Week 6 | Can create a new level in < 30 minutes using your tools | ☐ |
| **Runs on target platform** | Week 10 | Build + run on every target platform, 60fps | ☐ |

> **Failed a milestone?** Don't push through. Stop and honestly assess:
> - Cut scope (Section 4.3)?
> - Simplify the mechanic?
> - Switch to a less risky genre?
> - Accept a longer timeline?
>
> These are hard conversations. Have them at week 3, not month 6.

---

## Pre-Production Exit Checklist

**You are ready to start coding when ALL of these are true:**

- [ ] Game Concept Worksheet is fully filled out (Section 1)
- [ ] Genre chosen with full awareness of required systems (Section 2)
- [ ] Lightweight GDD written and shared with at least one person (Section 3)
- [ ] Scope is honest and a Cut List exists (Section 4)
- [ ] Art style, resolution, and palette are locked in (Section 5)
- [ ] Technical stack and architecture decisions are made (Section 6)
- [ ] Repo is initialized, builds, and runs (Section 7)
- [ ] Risks are identified with a prototype plan (Section 8)

> **Missing items?** Go back and finish them. Every hour spent here saves 5 hours during production.
> Once you check all boxes, proceed to **Phase 2: Vertical Slice** in the [Master Playbook](./00_master_playbook.md).

---

## Quick Reference: Key Doc Links by Topic

| Topic | Primary Doc | Also See |
|-------|------------|----------|
| Architecture & ECS | [E1 Architecture Overview](../E/E1_architecture_overview.md) | [G12 Design Patterns](../G/G12_design_patterns.md), [G18 Game Programming Patterns](../G/G18_game_programming_patterns.md) |
| Libraries & Packages | [R1 Library Stack](../R/R1_library_stack.md) | [R2 Capability Matrix](../R/R2_capability_matrix.md) |
| Project Structure | [R3 Project Structure](../R/R3_project_structure.md) | [G44 Version Control](../G/G44_version_control.md) |
| Game Design | [E6 Game Design Fundamentals](../E/E6_game_design_fundamentals.md) | [R4 Game Design Resources](../R/R4_game_design_resources.md) |
| Genre Planning | [C1 Genre Reference](../C/C1_genre_reference.md) | [C2 Game Feel & Genre Craft](../C/C2_game_feel_and_genre_craft.md) |
| Scope & Management | [E4 Solo Project Management](../E/E4_project_management.md) | [E9 Solo Dev Playbook](../E/E9_solo_dev_playbook.md) |
| Display & Resolution | [G19 Display & Viewports](../G/G19_display_resolution_viewports.md) | [G25 Safe Areas](../G/G25_safe_areas_adaptive_layout.md) |
| Input | [G7 Input Handling](../G/G7_input_handling.md) | [C2 Game Feel](../C/C2_game_feel_and_genre_craft.md) |
| Audio | [G6 Audio](../G/G6_audio.md) | |
| Content Pipeline | [G8 Content Pipeline](../G/G8_content_pipeline.md) | [G31 Animation](../G/G31_animation_state_machines.md) |
| Game Feel & Polish | [G30 Game Feel Tooling](../G/G30_game_feel_tooling.md) | [C2 Game Feel](../C/C2_game_feel_and_genre_craft.md) |
| Deployment | [G32 Deployment](../G/G32_deployment_platform_builds.md) | [G36 Publishing](../G/G36_publishing_distribution.md) |
| Accessibility | [G35 Accessibility](../G/G35_accessibility.md) | |
