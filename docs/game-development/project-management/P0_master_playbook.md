# 🎮 How to Make a 2D Game: From Idea to Launch



## The Master Playbook

**Stack:** MonoGame.Framework.DesktopGL · Arch ECS v2.1.0 · Composed Libraries · C#
**Timeline:** ~24 weeks (6 months) from idea to launch
**Audience:** Solo developers and small teams building 2D games

---

> *"A game is a series of interesting decisions."* — Sid Meier
>
> This playbook is the single document that walks you through every phase of making a 2D game.
> It doesn't teach you how to code a physics engine — the 76 docs in this toolkit do that.
> What this playbook does is tell you **when** to build what, **why** it matters, and **where**
> to find the detailed guide when you're ready.
>
> Read it front to back once. Then use it as your compass throughout development.

---

## How to Use This Document

Each phase has:

- **🎯 Goal** — What you're trying to achieve
- **⏱️ Time Estimate** — Realistic for a solo dev working ~20-30 hrs/week
- **📋 Steps** — Ordered tasks with doc references
- **🚦 Decision Gate** — What must be true before you move on
- **📚 Required Reading** — Docs to study for that phase
- **⚠️ Common Mistakes** — What kills projects at this stage

Doc references use the toolkit's naming convention:
- **R** = Reference (look things up) · **E** = Explanation (understand why)
- **G** = Guide (build things) · **C** = Catalog (plan your game)
- **Playbook** = Numbered files in this Playbook folder (e.g., `08_playtesting.md`)

Relative links point to the toolkit root (one level up from `Playbook/`).

---

## The Journey at a Glance

```
Week  1       ██░░░░░░░░░░░░░░░░░░░░░░  Phase 0: Ideation
Weeks 2-3     ████░░░░░░░░░░░░░░░░░░░░  Phase 1: Pre-Production
Weeks 4-8     ████████░░░░░░░░░░░░░░░░  Phase 2: Vertical Slice
Weeks 9-16    ████████████████░░░░░░░░  Phase 3: Alpha
Weeks 17-22   ██████████████████████░░  Phase 4: Beta & Polish
Weeks 23-24   ████████████████████████  Phase 5: Release Candidate
Launch Day    🚀                        Phase 6: Launch & Beyond
```

**Reality check:** These timelines assume a small-scope game (think Celeste, not Stardew Valley). Scale up by 2-4x for ambitious projects. The phases stay the same — the weeks expand.

---

## The Stack You're Building With

Before we start: know your tools.

```
MonoGame.Framework.DesktopGL     — Base framework (rendering, audio, input, content)
Arch ECS (v2.1.0)               — Entity Component System for ALL entities
MonoGame.Extended (v5.3.1)       — Camera, Tiled maps, collision shapes, math
Gum.MonoGame                     — UI framework
Apos.Input (v2.5.0)             — Input handling
FontStashSharp.MonoGame (v1.3.7) — Runtime font rendering
MonoGame.Aseprite (v6.3.1)      — Sprite animation from .aseprite files
Aether.Physics2D (v2.2.0)       — Full Box2D-style physics (when needed)
BrainAI                          — FSM, Behavior Trees, GOAP, pathfinding
ImGui.NET                        — Debug tooling and overlays
Coroutine (Ellpeck)              — Unity-style coroutines
~1,000 lines custom glue code    — Scene manager, render layers, tweens, etc.
```

📚 Full details: [R1 — Library Stack](../../monogame-arch/reference/R1_library_stack.md) · [R2 — Capability Matrix](../../monogame-arch/reference/R2_capability_matrix.md)

---

---

# Phase 0: Ideation

## 🗓️ Week 1 · ⏱️ ~10-15 hours

```
YOU ARE HERE (if you haven't started yet)
████░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░
```

### 🎯 Goal

Walk away from this week with a single, clear game concept you're excited to build — and confident you *can* build.

### Why This Phase Matters

Most failed projects fail here. Not because the developer lacked skill, but because they started building before they knew what they were building. One week of thinking saves months of wasted code.

---

### Step 1: Find Your Game Idea

**Where ideas come from:**
- A mechanic that feels fun in your head ("what if gravity switched every 10 seconds?")
- A genre you love, remixed ("Zelda but underwater with oxygen management")
- A feeling you want to create ("cozy, like tending a garden in rain")
- A constraint that forces creativity ("entire game on one screen")

**What makes a good solo-dev idea:**
- Core mechanic is explainable in one sentence
- Scope is small enough to finish (seriously — smaller than you think)
- You're excited to play it, not just build it
- It plays to your strengths (good at code? mechanic-heavy. good at art? visual-heavy.)

📚 **Read:** [C1 — Genre Reference](../game-design/C1_genre_reference.md) — Browse every 2D genre with their required mechanics and systems. This is your menu. Pick what excites you and matches your skill level.

**Exercise:** Write down 3-5 game ideas in one sentence each. Sleep on it. Pick the one you keep thinking about.

---

### Step 2: Define Your Game's Identity

For your chosen idea, answer these questions:

**The Core Mechanic:**
> What is the one thing the player does most? (jump, shoot, place, match, explore, talk)
> If this mechanic isn't fun in a gray-box prototype, nothing else will save the game.

**The Three Pillars:**
> Pick exactly three words/phrases that define your game's experience.
> Examples: "Precision · Discovery · Solitude" or "Chaos · Speed · Humor"
> Every feature you add must serve at least one pillar. If it doesn't, cut it.

**The Audience:**
> Who is this for? "Everyone" is not an answer. Be specific.
> "Players who liked Celeste but want more exploration" is an answer.

**The Hook:**
> What makes someone click on your Steam page? One sentence.
> "A puzzle-platformer where you control time by playing music."

---

### Step 3: Feasibility Check

This is where dreams meet reality. Be honest.

📚 **Read:** [E9 — Solo Dev Playbook](E9_solo_dev_playbook.md) — Realistic productivity data, scope management, what solo devs can actually ship.

**Ask yourself:**

| Question | Red Flag |
|----------|----------|
| Can I build the core mechanic in 2 weeks? | If no → mechanic is too complex or you need more skill-building first |
| Do I need online multiplayer? | If yes → add 3-6 months and significant complexity. Consider cutting it. |
| How much content does the game need? | 50+ levels as a solo dev = danger zone |
| Do I need custom art, or can I use a consistent simple style? | "AAA pixel art" as a solo dev = burnout |
| Have I shipped *anything* before? | If no → cut scope by 50%. Seriously. Ship something small first. |

**The Scope Gut Check:**
- **Tiny** (4-8 weeks): One mechanic, 10-20 levels, minimal story. *Perfect for first game.*
- **Small** (3-6 months): 2-3 mechanics, 30-50 levels or medium world, light story. *This playbook's target.*
- **Medium** (6-12 months): Multiple interlocking systems, full narrative, lots of content. *Experienced devs only.*
- **Large** (1-2+ years): Don't. Not for your first 2-3 games. *Even veterans struggle here.*

---

### Step 4: Write the 1-Page Pitch

Distill everything into a single page. This is your north star for the entire project.

```markdown
# [Game Title] — 1-Page Pitch

## Elevator Pitch
[2-3 sentences. What is this game?]

## Core Mechanic
[The one thing the player does most]

## Design Pillars
1. [Pillar 1]
2. [Pillar 2]
3. [Pillar 3]

## Genre & Perspective
[e.g., "Action-platformer, side-scrolling, pixel art"]

## Target Audience
[Who is this for?]

## Scope
[Tiny / Small / Medium] — Target: [X] weeks

## Inspirations
[2-3 games and what you're taking from each]

## Unique Hook
[Why would someone play THIS instead of the inspirations?]
```

Print this. Pin it above your monitor. Every decision you make should serve this document.

---

### 🚦 Decision Gate: Ready for Pre-Production?

✅ You have a clear, one-sentence game concept
✅ You can name the core mechanic
✅ You've defined 3 design pillars
✅ Your scope is honest (not aspirational)
✅ You have a 1-page pitch document
✅ You're still excited

❌ **If you're not excited:** Go back to Step 1. Grinding through a game you don't care about is the #1 project killer.

### ⚠️ Common Mistakes

- **"I'll figure it out as I go"** — No. Directionless development is how you end up with 6 months of disconnected systems and no game.
- **"My first game will be my dream game"** — Your first game should be small and finishable. Save the magnum opus for game #3.
- **"I need a totally original idea"** — No. Execution > originality. A well-made genre game beats a poorly-made innovative one.
- **"I'll add multiplayer later"** — Multiplayer is an architecture decision, not a feature. Decide now or never.

---

---

# Phase 1: Pre-Production

## 🗓️ Weeks 2-3 · ⏱️ ~30-40 hours

```
████████░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░
```

### 🎯 Goal

Turn your pitch into a concrete plan: a design document, a project structure, and a tech prototype that proves your risky mechanic works.

### Why This Phase Matters

Pre-production is where you make the cheap mistakes. Changing a design doc costs minutes. Changing architecture in month 4 costs weeks. Front-load the thinking.

---

### Step 1: Write the Game Design Document

Your pitch was the "what." The GDD is the "how."

📚 **Read:** [E6 — Game Design Fundamentals](../game-design/E6_game_design_fundamentals.md) — MDA framework, design pillars, player motivation, feedback loops, pacing
📚 **Use:** [13 — GDD Template](./13_gdd_template.md) — Fill-in-the-blank design document

**Your GDD should cover:**

1. **Vision Statement** — Expanded pitch (1 paragraph)
2. **Core Loop** — What the player does every 30 seconds, every 5 minutes, every session
3. **Mechanics Inventory** — Every mechanic, rated by priority (Must/Should/Could/Won't)
4. **Content Plan** — How many levels/areas/enemies/items, broken into milestones
5. **Progression** — How the player gets better/stronger/further
6. **Controls** — Input mapping for every action
7. **UI Screens** — Main menu, HUD, pause, inventory, settings (rough sketches)
8. **Art Direction** — Style, palette, resolution, reference images
9. **Audio Direction** — Music mood, SFX style, number of tracks needed
10. **Technical Risks** — What might not work? What needs a prototype?

**The MoSCoW Method for Features:**

| Priority | Meaning | Example |
|----------|---------|---------|
| **Must** | Game doesn't work without it | Core mechanic, basic enemy, win condition |
| **Should** | Expected by players | Save system, settings menu, polish |
| **Could** | Nice to have if time allows | Achievements, weather effects, bonus levels |
| **Won't** | Cut it. For real. | Multiplayer, level editor, mod support (for v1) |

Be ruthless with "Won't." Every "Could" you promote to "Should" adds weeks.

---

### Step 2: Choose Your Art Direction

You need to lock in two critical decisions early because they affect everything:

**Resolution & Scaling:**

📚 **Read:** [G19 — Display, Resolution & Viewports](../../monogame-arch/guides/G19_display_resolution_viewports.md)

| Style | Base Resolution | Tile Size | Character Size |
|-------|----------------|-----------|----------------|
| Chunky pixel art (Celeste) | 320×180 | 8×8 | 8×16 |
| Detailed pixel art (Dead Cells) | 480×270 | 16×16 | 32×48 |
| HD pixel art (Octopath) | 640×360 | 16×16 | 32×64 |
| Vector/HD art | 1920×1080 | N/A | Scalable |

**Pick one. Don't change it later.** Your entire art pipeline, camera system, and UI layout depend on this.

**Perspective:**

📚 **Choose one and read its guide:**
- [G56 — Side-Scrolling](../../monogame-arch/guides/G56_side_scrolling.md) — Platformers, run-and-gun, Metroidvania
- [G28 — 3/4 Top-Down](../../monogame-arch/guides/G28_top_down_perspective.md) — Zelda-like, RPGs, action-adventure
- [G49 — Isometric](../../monogame-arch/guides/G49_isometric.md) — Tactics, city builders, CRPGs

Each perspective has fundamentally different rendering, collision, and level design approaches. This isn't a cosmetic choice — it's an architectural one.

---

### Step 3: Scope Into Milestones

📚 **Read:** [E4 — Solo Project Management](E4_project_management.md) — Vertical slices, scope, tech debt management

Break your GDD into concrete milestones:

```
Vertical Slice (Weeks 4-8)
├── Core mechanic working
├── One complete level
├── One enemy type
├── Basic camera and movement
├── Placeholder art OK
└── QUESTION: Is this fun?

Alpha (Weeks 9-16)
├── All mechanics implemented
├── 50% of content
├── All major systems online
├── Art style established (not all final art)
└── First complete playthrough possible

Beta (Weeks 17-22)
├── All content complete
├── Polish pass
├── External playtesting
├── Performance acceptable
└── All known bugs fixed

Release Candidate (Weeks 23-24)
├── Platform builds working
├── Store pages up
├── Final testing
└── SHIP IT
```

**The Vertical Slice is your most important milestone.** Everything before it is planning. Everything after it is execution. The vertical slice is where you answer the only question that matters: **"Is this fun?"**

---

### Step 4: Identify and Prototype the Risky Mechanic

Every game has one mechanic that might not work. Find yours and prove it now.

**Examples of risky mechanics:**
- Gravity switching → Does it feel good? Is level design possible?
- Procedural generation → Are the generated levels actually fun?
- Time manipulation → Can the player understand what's happening?
- Physics-based building → Is it stable enough to be fun, chaotic enough to be interesting?

**Build a throwaway prototype:**
- Gray boxes, no art, no menus
- Just the risky mechanic in isolation
- Spend 3-5 days maximum
- Show it to someone. Watch them play. Don't explain anything.

If the mechanic doesn't feel fun in gray-box? **Pivot now.** Go back to Phase 0. It's week 2. You've lost nothing.

---

### Step 5: Set Up the Project

Now you write real code. But you set it up right from the start.

📚 **Read in order:**
1. [E1 — Architecture Overview](../../monogame-arch/architecture/E1_architecture_overview.md) — Understand the composed stack philosophy
2. [R1 — Library Stack](../../monogame-arch/reference/R1_library_stack.md) — Install all packages
3. [R3 — Project Structure](../../monogame-arch/reference/R3_project_structure.md) — Folder layout and solution organization
4. [G44 — Version Control](../../monogame-arch/guides/G44_version_control.md) — Git setup, .gitignore, LFS for assets

**Project setup checklist:**

- [ ] Create solution with `dotnet new sln`
- [ ] Create project with MonoGame DesktopGL template
- [ ] Install all Tier 1 NuGet packages (see R1)
- [ ] Set up folder structure (see R3)
- [ ] Initialize Git repo with proper .gitignore
- [ ] Configure Git LFS for binary assets (`.png`, `.aseprite`, `.ogg`, `.wav`)
- [ ] Write the basic `GameApp` class with fixed timestep
- [ ] Set up Arch ECS world
- [ ] Create a minimal scene manager (see [G1 — Custom Code Recipes](../../monogame-arch/guides/G1_custom_code_recipes.md))
- [ ] Verify it compiles and runs (blank colored window = success)
- [ ] Make your first commit: "Initial project setup"

📚 **Also useful now:** [E5 — AI-Assisted Dev Workflow](../ai-workflow/E5_ai_workflow.md) — If you're using AI coding assistants, structure your code for them from the start

---

### 🚦 Decision Gate: Ready for Vertical Slice?

✅ GDD written with MoSCoW priorities
✅ Resolution and perspective locked
✅ Milestones defined with content counts
✅ Risky mechanic prototyped and validated (or pivoted)
✅ Project compiles, ECS world runs, scene manager works
✅ Version control initialized with first commit

❌ **If the risky mechanic failed:** That's a success! You saved months. Go back to Phase 0 with a new idea or a new mechanic for the same idea. The project setup can be reused.

### ⚠️ Common Mistakes

- **Skipping the GDD** — "I have it all in my head" means you haven't found the contradictions yet. Writing it down forces clarity.
- **Bikeshedding the project structure** — Set up folders, move on. You can reorganize later.
- **Premature optimization of the stack** — Don't write custom renderers or ECS wrappers. Use the libraries as-is until they prove insufficient.
- **Not prototyping the risk** — If you're not sure the core mechanic will be fun, you MUST prove it before proceeding.
- **Art before architecture** — Don't commission or create final art until you've proven the game is fun.

---

---

# Phase 2: Vertical Slice

## 🗓️ Weeks 4-8 · ⏱️ ~100-125 hours

```
████████████████░░░░░░░░░░░░░░░░░░░░░░░░
```

### 🎯 Goal

Build one complete, playable slice of your game — start to finish, including a beginning, a challenge, and an end. This is the most important milestone in your entire project.

### What a Vertical Slice IS and ISN'T

| It IS | It ISN'T |
|-------|----------|
| One polished level/area from start to finish | The first 10% of the whole game |
| Representative of the final experience | A tech demo with no gameplay |
| Something you'd show to prove the game works | Something only you can appreciate |
| Built with placeholder art (final art OK but not required) | A visual showcase with no mechanics |
| Playable by someone who isn't you | A prototype only the developer can navigate |

**The vertical slice answers ONE question: "Is this fun?"**

If the answer is "no" after this phase, you either pivot the design or kill the project. Both are valid. Both are better than spending 4 more months on something that isn't fun.

---

### Implementation Order

Build systems in dependency order. Each step below builds on the previous one.

#### Week 4: Foundation Systems

**1. Game Loop & Scene Management**

📚 [G15 — Game Loop](../../monogame-arch/guides/G15_game_loop.md) · [G38 — Scene Management](../../monogame-arch/guides/G38_scene_management.md)

- Fixed timestep game loop (MonoGame default handles this)
- Scene manager with at least: `GameplayScene`, `PauseOverlay`
- Scene transitions (even a simple fade — see [G42 — Screen Transitions](../../monogame-arch/guides/G42_screen_transitions.md))

**2. Input System**

📚 [G7 — Input Handling](../../monogame-arch/guides/G7_input_handling.md)

- Wire up Apos.Input
- Map actions to inputs (Move, Jump, Attack, Interact, Pause)
- Support keyboard + at least one gamepad from day one
- Input buffering for action games (G7 covers this)

**3. Player Movement & Character Controller**

📚 [G52 — Character Controller](../../monogame-arch/guides/G52_character_controller.md) (for platformers)

Or implement movement appropriate to your perspective:
- Side-scrolling → G52 (kinematic controller, variable jump, coyote time)
- Top-down → [G28](../../monogame-arch/guides/G28_top_down_perspective.md) (8-directional movement, collision response)
- Isometric → [G49](../../monogame-arch/guides/G49_isometric.md) (coordinate conversion, diamond movement)

**This is where "feel" starts.** Spend extra time here. If moving around isn't satisfying, nothing built on top will be either.

#### Week 5: World Systems

**4. Camera**

📚 [G20 — Camera Systems](../../monogame-arch/guides/G20_camera_systems.md)

- Camera follow with smoothing
- Camera bounds (don't show outside the level)
- Dead zone so the camera doesn't jitter on small movements
- Optional: look-ahead in movement direction

**5. Tilemap & Level Loading**

📚 [G37 — Tilemap Systems](../../monogame-arch/guides/G37_tilemap_systems.md) · [G8 — Content Pipeline](../../monogame-arch/guides/G8_content_pipeline.md)

- Load Tiled (.tmx) maps via MonoGame.Extended
- Render tile layers (background, midground, foreground)
- Parse collision layer from Tiled
- Parse object layer for spawn points, triggers, items

**6. Collision**

📚 [G3 — Physics & Collision](../../monogame-arch/guides/G3_physics_and_collision.md)

- Tile collision (for most games, AABB vs tilemap is enough)
- Entity-vs-entity collision detection
- Decision: SpatialHash (simple) vs Aether.Physics2D (complex)?
  - **Use SpatialHash** for: platformers, top-down action, most games
  - **Use Aether** for: physics puzzles, Angry Birds-style, anything needing joints/forces

#### Week 6: Core Gameplay

**7. Your Core Mechanic**

This is unique to your game. Whatever you prototyped in Phase 1, now integrate it properly:
- Connected to the ECS
- Responding to real input
- Interacting with the tilemap and collision
- Feeling good (or at least functional — polish comes later)

**8. First Enemy / First Hazard**

📚 [G4 — AI Systems](../../monogame-arch/guides/G4_ai_systems.md) (for enemy AI patterns)

- One enemy type with basic behavior (patrol, chase, attack)
- One hazard type (spikes, pits, projectiles)
- Player can be hurt, player can hurt enemies
- Health system (even if it's just 3 hearts)

**9. First Complete Level**

Using Tiled, build one level that:
- Introduces the core mechanic
- Has a beginning (spawn point) and end (goal/exit)
- Contains at least one enemy encounter
- Contains at least one environmental challenge
- Takes 2-5 minutes to complete
- Has intentional pacing (easy → teach → challenge → reward)

#### Week 7: Integration & Polish Pass

**10. HUD (Minimal)**

📚 [G5 — UI Framework](../../monogame-arch/guides/G5_ui_framework.md)

- Health display
- Any core mechanic indicator (ammo, mana, timer, score)
- Don't build menus yet. A HUD is enough.

**11. Basic Audio**

📚 [G6 — Audio](../../monogame-arch/guides/G6_audio.md)

- Placeholder sound effects for: jump, attack, hit, enemy death, pickup
- One background music track (even a free loop)
- Audio feedback transforms a "tech demo" into something that feels like a game

**12. Basic Game Feel**

📚 [G30 — Game Feel Tooling](../../monogame-arch/guides/G30_game_feel_tooling.md) · [C2 — Game Feel & Genre Craft](../game-design/C2_game_feel_and_genre_craft.md)

- Screen shake on big impacts
- Hitstop/hitpause on damage
- Knockback on hit
- Landing squash on jump landing
- Even small amounts of juice make the vertical slice dramatically better

#### Week 8: Playtest & Decide

**13. Playtest the Vertical Slice**

📚 [08 — Playtesting Guide](./08_playtesting.md)

**How to playtest properly:**
1. Find 3-5 people who are NOT you (friends, online communities, local game dev meetups)
2. Hand them the controller/keyboard
3. **Say nothing.** Don't explain. Don't hint. Watch silently.
4. Record the session if possible (OBS is free)
5. After they finish, ask:
   - "What was that game about?"
   - "What was fun?"
   - "What was confusing?"
   - "Would you play more?"
6. Write down everything. Especially the things that hurt to hear.

**What to look for:**
- Do players understand the mechanic without explanation?
- Do they smile, laugh, or lean forward? (Good signs)
- Do they look confused, frustrated, or bored? (Bad signs)
- Where do they get stuck?
- What do they try that your game doesn't support? (Often reveals missing features)

---

### 🚦 Decision Gate: The Most Important Gate

This is where honesty matters more than anywhere else.

**✅ Proceed to Alpha if:**
- Playtesters found the core mechanic fun (not just "interesting")
- You can see how more content would make this better
- The technical foundation is solid enough to build on
- You're still excited to make this game

**🔄 Pivot if:**
- The mechanic is interesting but doesn't feel right — redesign the mechanic, not the game
- Playtesters liked the *world* but not the *gameplay* — rethink the core loop
- Tech is blocking the fun — consider whether the stack is right for this game

**❌ Kill the project if:**
- Nobody found it fun, including you
- The core mechanic doesn't work and can't be saved
- You've lost motivation entirely

**Killing a project after 8 weeks is a victory, not a failure.** You spent 8 weeks instead of 8 months learning this wasn't the one. Take what you learned, grieve briefly, and start Phase 0 again. Your project setup, engine knowledge, and systems code can all be reused.

### ⚠️ Common Mistakes

- **Not playtesting** — If only you have played it, you have no data. Your opinion is biased.
- **"It'll be fun once I add more content"** — If the core loop isn't fun with one level, 50 levels won't save it.
- **Spending too long on art** — Placeholder art is fine. Gray boxes are fine. The vertical slice tests *gameplay*, not visuals.
- **Building too many systems** — You need input, movement, camera, tilemap, collision, and your core mechanic. Not inventory, crafting, dialogue, and skill trees.
- **Ignoring the decision gate** — The sunken cost fallacy kills games. Be honest.

---

---

# Phase 3: Alpha

## 🗓️ Weeks 9-16 · ⏱️ ~200-250 hours

```
████████████████████████████████░░░░░░░░░
```

### 🎯 Goal

Get ALL major systems online and create the first complete playthrough. At the end of Alpha, someone can play your game from start to finish — even if it's rough, buggy, and uses placeholder art in places.

### The Alpha Mindset

Alpha is about **breadth**, not depth. You're building every system your game needs, but you're not polishing any of them yet. Get it working, get it in, move on. Polish is Phase 4's job.

---

### System Build Order

Build systems in this order. Each group can be parallelized, but the groups themselves are roughly sequential.

#### Weeks 9-10: Content Pipeline & Tools

**Entity Prefabs & Blueprints**

📚 [G43 — Entity Prefabs](../../monogame-arch/guides/G43_entity_prefabs.md)

- Data-driven entity definitions (JSON blueprints)
- Entity factory that spawns from blueprints
- Tiled object → entity spawning pipeline
- This pays for itself immediately — adding new enemies/items becomes minutes instead of hours

**Animation System**

📚 [G31 — Animation & Sprite State Machines](../../monogame-arch/guides/G31_animation_state_machines.md) · [G59 — Skeletal Animation](../../monogame-arch/guides/G59_skeletal_animation.md)

- Aseprite integration via MonoGame.Aseprite
- Animation state machines (Idle → Run → Jump → Attack → etc.)
- Directional sprites (for top-down games)
- Animation events (spawn projectile on frame 5, play sound on frame 3)

**Resource Management**

📚 [G26 — Resource Loading & Caching](../../monogame-arch/guides/G26_resource_loading_caching.md)

- Scoped content loading (per-scene, not all upfront)
- Asset caching strategy
- Loading screens for scene transitions

#### Weeks 11-12: Core Game Systems

**AI & Enemy Behaviors**

📚 [G4 — AI Systems](../../monogame-arch/guides/G4_ai_systems.md) · [G40 — Pathfinding](../../monogame-arch/guides/G40_pathfinding.md)

- Expand from one enemy type to all planned types
- Behavior patterns: patrol, chase, flee, attack, idle
- Use BrainAI's FSM for simple enemies, behavior trees for complex ones
- Pathfinding for enemies that need to navigate (A* on grid, or flow fields for swarms)

**UI Framework**

📚 [G5 — UI Framework](../../monogame-arch/guides/G5_ui_framework.md)

- Main menu (New Game, Continue, Settings, Quit)
- Pause menu
- HUD improvements (from vertical slice prototype to proper layout)
- Any game-specific UI: inventory screen, map screen, dialogue box
- Use Gum.MonoGame for layout — it handles scaling and anchoring

**Audio System (Full)**

📚 [G6 — Audio](../../monogame-arch/guides/G6_audio.md)

- Music system with crossfading between tracks
- Sound effect manager with variations (3 footstep sounds, randomly picked)
- Ambient sound layers
- Decision: MonoGame built-in audio vs FMOD?
  - **Built-in:** Fine for most indie games, simpler, no licensing
  - **FMOD:** Better for dynamic music, complex layering, AAA audio design

#### Weeks 13-14: Game-Specific Systems

These depend entirely on your game. Build what your GDD says you need.

**Save/Load System**

📚 [G10 — Custom Game Systems](../../monogame-arch/guides/G10_custom_game_systems.md) (save/load section)

- Decide save strategy: save points, autosave, save anywhere
- Serialize game state to JSON
- Handle versioning (so old saves work with new game versions)
- Save settings separately from game progress

**Inventory & Items** (if applicable)

📚 [G10 — Custom Game Systems](../../monogame-arch/guides/G10_custom_game_systems.md) (inventory section)

- Item data definitions
- Inventory container (grid, list, or weight-based)
- Item pickup, use, drop, equip
- UI for inventory management

**Dialogue System** (if applicable)

📚 [G62 — Narrative & Branching Story](../../monogame-arch/guides/G62_narrative_systems.md) · [G10](../../monogame-arch/guides/G10_custom_game_systems.md) (dialogue section)

- Dialogue data format (consider Yarn Spinner or Ink integration)
- Dialogue UI (text box, portrait, name)
- Branching choices
- Story flags and consequences

**Quest / Progression System** (if applicable)

📚 [G47 — Achievements & Progression](../../monogame-arch/guides/G47_achievements.md) · [G10](../../monogame-arch/guides/G10_custom_game_systems.md) (quests section)

- Quest state tracking
- Objective completion events
- Quest log UI
- Rewards and unlocks

#### Weeks 15-16: Content Production & Integration

**Lighting** (if your game uses it)

📚 [G39 — 2D Lighting & Shadows](../../monogame-arch/guides/G39_2d_lighting.md)

- Lightmap rendering
- Point and spot lights
- Ambient light control
- Day/night cycle (if applicable)

**Particles**

📚 [G23 — Particles](../../monogame-arch/guides/G23_particles.md)

- Particle emitter system
- Common effects: dust, sparks, blood/hit, smoke, magic
- Pool particles to avoid GC pressure

**Settings Menu**

📚 [G55 — Settings & Options Menu](../../monogame-arch/guides/G55_settings_menu.md)

- Audio volume (Master, Music, SFX)
- Display settings (fullscreen, resolution, vsync)
- Input remapping (important for accessibility!)
- Persist settings to file

**Build Your Levels**

Now you have all the systems. Build content:
- Create all planned levels/areas (use Tiled extensively)
- Place enemies, items, triggers, dialogue
- Create a complete critical path from start to finish
- It's OK if balance is off — that's what Beta is for

---

### Alpha Testing Checklist

Before moving to Beta, verify:

- [ ] Can play from title screen to credits
- [ ] All core mechanics work
- [ ] All enemy types are implemented
- [ ] All major UI screens exist
- [ ] Save and load works
- [ ] Settings persist
- [ ] No crashes on the critical path (crashes in edge cases OK)
- [ ] Audio plays for all major actions
- [ ] Frame rate is acceptable (>30 FPS minimum, even if not optimized)

### 🚦 Decision Gate: Ready for Beta?

✅ Complete playthrough is possible (rough but complete)
✅ All major systems are integrated
✅ No critical systems are missing
✅ You have a content list and know what's left to build

❌ **If major systems are still missing:** Stay in Alpha. Don't start polishing systems that aren't built yet.

### ⚠️ Common Mistakes

- **Polishing too early** — Don't spend 3 days on a particle effect when you haven't built the dialogue system yet. Get everything working first.
- **Building systems you don't need** — If your GDD says "Won't," don't build it.
- **Not making levels** — Systems without content are useless. Force yourself to build levels even when "just one more system" calls.
- **Ignoring performance** — You don't need to optimize yet, but if you're at 15 FPS, something is architecturally wrong. Fix it now. See [G33 — Profiling](../../monogame-arch/guides/G33_profiling_optimization.md).
- **Feature creep** — Your GDD's "Could" list is whispering. Don't listen. Ship the "Must" and "Should" first.

---

---

# Phase 4: Beta & Polish

## 🗓️ Weeks 17-22 · ⏱️ ~150-175 hours

```
████████████████████████████████████████░░
```

### 🎯 Goal

Content complete, fully polished, thoroughly tested. At the end of Beta, your game is *done* — it just hasn't been shipped yet.

### The Beta Mindset

Beta is about **depth**, not breadth. You're not building new systems. You're making existing ones excellent. This is where your game goes from "functional" to "delightful."

---

### Weeks 17-18: Content Completion

**Finish ALL content:**
- Every level/area built and populated
- Every enemy placed and balanced
- Every item/pickup in the game
- Every dialogue tree written
- Every cutscene implemented → [G45 — Cutscenes](../../monogame-arch/guides/G45_cutscenes.md)
- Every sound effect and music track in place

**Content freeze.** After this, no new content. Only improvements to existing content.

---

### Weeks 19-20: The Polish Pass

This is where your game comes alive. Polish is not optional — it's what separates "indie game" from "game jam prototype."

**Game Feel & Juice**

📚 [G30 — Game Feel Tooling](../../monogame-arch/guides/G30_game_feel_tooling.md) · [C2 — Game Feel & Genre Craft](../game-design/C2_game_feel_and_genre_craft.md)

The Juice Checklist:
- [ ] Screen shake on impacts (calibrated — too much is worse than none)
- [ ] Hitstop/freeze frames on big hits (2-5 frames)
- [ ] Squash and stretch on landing/jumping
- [ ] Knockback on damage (both player and enemies)
- [ ] Camera kick on important events
- [ ] Death animations and effects (never instant-disappear enemies)
- [ ] Pickup animations and particles
- [ ] Button press feedback in menus (scale, sound, color)

**Tweening & Animation**

📚 [G41 — Tweening & Easing](../../monogame-arch/guides/G41_tweening.md)

- UI elements animate in/out (don't just appear/disappear)
- Health bars tween smoothly
- Damage numbers float and fade
- Menu transitions feel snappy

**Screen Transitions**

📚 [G42 — Screen Transitions](../../monogame-arch/guides/G42_screen_transitions.md)

- Level transitions (fade, wipe, iris, dissolve — pick what fits your style)
- Scene transitions with loading (if levels are large)
- Death transition and respawn

**Visual Effects**

📚 [G60 — Trail & Line Rendering](../../monogame-arch/guides/G60_trails_lines.md) · [G57 — Weather Effects](../../monogame-arch/guides/G57_weather_effects.md)

- Weapon/attack trails
- Dash ghosts/afterimages
- Environmental weather (rain, snow, fog)
- Ambient particles (dust motes, fireflies, falling leaves)

**Tutorial & Onboarding**

📚 [G61 — Tutorial & Onboarding](../../monogame-arch/guides/G61_tutorial_onboarding.md)

- Teach mechanics through gameplay, not text walls
- Contextual button prompts
- Gated progression (teach jump before wall-jump)
- Optional tutorial that experienced players can skip

---

### Weeks 21-22: Testing, Accessibility & Performance

**Accessibility**

📚 [G35 — Accessibility](../../monogame-arch/guides/G35_accessibility.md)

- [ ] Colorblind mode (or colorblind-safe default palette)
- [ ] Input remapping (done in Alpha, verify it works everywhere)
- [ ] Adjustable difficulty
- [ ] Subtitle options
- [ ] Screen reader support for menus (if feasible)
- [ ] Respecting system accessibility settings

**Localization** (if shipping in multiple languages)

📚 [G34 — Localization](../../monogame-arch/guides/G34_localization.md)

- All strings externalized (no hardcoded text)
- Translation files for each language
- Font supports target scripts (CJK, Cyrillic, Arabic)
- UI handles variable text lengths
- RTL layout support (if needed)

**Performance Profiling**

📚 [G33 — Profiling & Optimization](../../monogame-arch/guides/G33_profiling_optimization.md) · [G13 — C# Performance](../../monogame-arch/guides/G13_csharp_performance.md)

- Profile every scene, find the bottlenecks
- Target frame budget: 16.67ms (60 FPS) or 33.33ms (30 FPS)
- Common culprits:
  - GC pressure (allocating in Update loop) → fix with pooling, Span, ArrayPool
  - Too many draw calls → batch sprites, use texture atlases
  - Expensive collision checks → use SpatialHash, reduce check frequency
  - Shader complexity → profile, simplify
- Profile on your minimum spec target, not your development machine

**External Playtesting**

📚 [08 — Playtesting Guide](./08_playtesting.md)

This is different from vertical slice playtesting. Now you're testing the *whole game*:

1. Find 5-10 fresh playtesters (people who haven't seen the game before)
2. Have them play the entire game
3. Track: completion time, death locations, quit points, confusion points
4. Collect feedback on: difficulty, pacing, story clarity, bugs
5. **Two rounds:** Fix major issues after round 1, test again in round 2

**Bug Fixing**

- Categorize bugs: Critical (crash/data loss) → High (gameplay broken) → Medium (annoying) → Low (cosmetic)
- Fix ALL critical and high bugs
- Fix medium bugs that affect common paths
- Low bugs: fix if time allows, otherwise document for post-launch patch

---

### 🚦 Decision Gate: Ready for Release Candidate?

✅ All content is in the game
✅ Polish pass complete (game feel, transitions, effects)
✅ External playtesters played the full game
✅ No critical or high-priority bugs remaining
✅ Performance is acceptable on target hardware
✅ Accessibility features implemented

❌ **If content is still missing:** You're not in Beta yet. Go back.
❌ **If playtesters found fundamental fun problems:** This is painful at week 22. Consider whether a targeted rework or scope cut can address it.

### ⚠️ Common Mistakes

- **Endless polish** — Polish has diminishing returns. Set a deadline and stop. "Good enough" ships; "perfect" doesn't.
- **Ignoring playtest feedback** — If 4/5 playtesters got stuck in the same spot, the spot is the problem, not the players.
- **Optimizing without profiling** — Don't guess where the bottleneck is. Measure. Then fix the actual problem.
- **Adding new features** — "Just one more thing" during Beta is how Beta becomes another Alpha.
- **Skipping accessibility** — It's not just ethical; it's practical. Remappable controls and colorblind support expand your audience significantly.

---

---

# Phase 5: Release Candidate

## 🗓️ Weeks 23-24 · ⏱️ ~40-60 hours

```
██████████████████████████████████████████
```

### 🎯 Goal

Build for all target platforms, set up store pages, and verify everything works. At the end of this phase, you press the button.

---

### Platform Builds

📚 [G32 — Deployment & Platform Builds](../../monogame-arch/guides/G32_deployment_platform_builds.md)

**For each target platform:**
- [ ] `dotnet publish` with correct runtime identifier
- [ ] Test the *published build* (not the dev build!)
- [ ] Verify all assets load correctly
- [ ] Verify save/load works
- [ ] Verify settings persist
- [ ] Check performance on actual target hardware
- [ ] Platform-specific quirks resolved (macOS notarization, Steam overlay, etc.)

**Common platform targets:**
| Platform | RID | Notes |
|----------|-----|-------|
| Windows x64 | `win-x64` | Most common target. Test on Windows 10 and 11. |
| macOS (Apple Silicon) | `osx-arm64` | Requires notarization for non-Steam distribution |
| macOS (Intel) | `osx-x64` | Still significant user base |
| Linux x64 | `linux-x64` | Test on Ubuntu/SteamOS if targeting Steam Deck |

---

### Store Page & Marketing

📚 [G36 — Publishing & Distribution](../../monogame-arch/guides/G36_publishing_distribution.md)

**Steam:**
- [ ] Steamworks account set up ($100 fee)
- [ ] Store page with screenshots, description, tags
- [ ] Capsule images (header, hero, small, library)
- [ ] Trailer (1-2 minutes, gameplay-focused)
- [ ] Coming Soon page live (ideally weeks before launch)
- [ ] Wishlist campaign (share the page everywhere)

**itch.io** (good for secondary distribution or early access):
- [ ] Game page with description and screenshots
- [ ] Set pricing (or "name your price")
- [ ] Upload builds for each platform

📚 **Also see:** [18 — Marketing Timeline](./18_marketing_timeline.md) — For a more detailed marketing plan

---

### Achievement & Online Integration

📚 [G47 — Achievements & Progression](../../monogame-arch/guides/G47_achievements.md) · [G48 — Online Services](../../monogame-arch/guides/G48_online_services.md)

If shipping on Steam:
- [ ] Steam achievements defined and integrated
- [ ] Cloud saves configured (if using)
- [ ] Rich presence working
- [ ] Leaderboards (if applicable)

---

### Crash Reporting

📚 [G51 — Crash Reporting](../../monogame-arch/guides/G51_crash_reporting.md)

- [ ] Global exception handler catches unhandled exceptions
- [ ] Crash dumps written to log file
- [ ] Error reporting (local logs at minimum, Sentry for remote if you want)
- [ ] Graceful crash message (not just disappearing window)

---

### Final Testing Checklist

Run through this on EVERY target platform:

**Critical Path:**
- [ ] New game → tutorial → all levels → final boss/ending → credits
- [ ] Save at multiple points → quit → reload → continue correctly
- [ ] Settings changes persist across sessions

**Edge Cases:**
- [ ] Alt-tab and resume
- [ ] Minimize and restore
- [ ] Unplug controller mid-game
- [ ] Plug in controller mid-game
- [ ] Window resize (if supported)
- [ ] Close game during save → no corruption
- [ ] Run out of disk space during save → graceful error

**Performance:**
- [ ] No memory leaks (play for 30+ minutes, check memory)
- [ ] No frame rate degradation over time
- [ ] Loading times acceptable

---

### 🚦 Decision Gate: Ready to Launch?

✅ Builds work on all target platforms
✅ Store page is live and reviewed
✅ Achievement/online integration tested
✅ Crash reporting working
✅ Final test pass completed on all platforms
✅ You have a launch date set

❌ **If builds have platform-specific bugs:** Fix them. This is your last chance.
❌ **If the store page isn't ready:** It needs to go live 2+ weeks before launch for wishlist accumulation.

### ⚠️ Common Mistakes

- **Testing only the dev build** — The published build is different. Test the actual thing you're shipping.
- **Skipping Mac/Linux testing** — "It works on Windows" means nothing. Each platform has quirks.
- **No crash reporting** — When (not if) a player crashes, you need to know why. Shipping blind is shipping scared.
- **Rushing the store page** — The store page sells your game more than the game itself (people buy before playing). Screenshots and description matter enormously.

---

---

# Phase 6: Launch & Beyond

## 🗓️ Launch Day + Ongoing

```
🚀 LAUNCH 🚀
```

### 🎯 Goal

Ship the game, support it post-launch, and learn from the experience.

---

### Launch Day

📚 [11 — Launch Checklist](./11_launch_checklist.md)

**The Night Before:**
- [ ] Final build uploaded to all platforms
- [ ] Store page reviewed one more time
- [ ] Launch announcement drafted (social media, devlog, press)
- [ ] Support channels ready (Discord, email, Steam forums)
- [ ] You've slept. Seriously. Launch day is stressful enough rested.

**Launch Day:**
- [ ] Press the publish button
- [ ] Post launch announcement
- [ ] Monitor crash reports
- [ ] Monitor Steam reviews / itch.io comments
- [ ] Respond to bug reports quickly
- [ ] **Don't** push a patch on day 1 unless it's a critical crash. Wait 24 hours.

**Launch Week:**
- [ ] Collect and categorize all bug reports
- [ ] Prepare first patch (fix critical + high priority bugs)
- [ ] Thank people who leave reviews
- [ ] Share any positive press/reviews/streams
- [ ] Monitor sales and wishlist conversion

---

### Post-Launch Support

**Patch Schedule:**
- Day 2-3: Hotfix patch (critical bugs only)
- Week 1-2: First major patch (bugs + small quality-of-life improvements)
- Month 1-2: Content update (if planned) or final polish patch
- After that: Only critical bug fixes unless you're doing ongoing content

**What to prioritize:**
1. Crashes and data loss (fix immediately)
2. Progression blockers (fix within 48 hours)
3. Common complaints from reviews (fix in first major patch)
4. Quality-of-life requests (if they align with your vision)

---

### Community & Modding

📚 [G46 — Modding Support](../../monogame-arch/guides/G46_modding_support.md)

If your game benefits from modding (and your architecture supports it):
- Data-driven designs (JSON blueprints) make modding natural
- Asset override systems let modders replace art/sound
- Lua scripting (via MoonSharp) for gameplay mods
- Steam Workshop integration for mod distribution

Even without formal mod support, a data-driven architecture lets passionate fans tinker. This extends your game's lifespan dramatically.

---

### The Post-Mortem

📚 [19 — Post-Mortem Template](./19_postmortem_template.md)

**Write a post-mortem 2-4 weeks after launch.** Not before — you need distance.

Cover:
1. **What went right** — Systems, decisions, and habits that worked
2. **What went wrong** — Mistakes, time sinks, bad decisions
3. **By the numbers** — Actual time vs estimated, actual scope vs planned, actual sales vs hoped
4. **What you'd do differently** — Concrete changes for the next project
5. **What you learned** — Skills gained, lessons internalized

**Share it** (anonymized if needed). The game dev community learns from post-mortems. Your honesty helps others avoid your mistakes.

---

### What's Next?

You shipped a game. That puts you ahead of ~95% of people who "want to make games." Here's what to do now:

1. **Rest.** You earned it. Take at least a week completely off from game dev.
2. **Celebrate.** You did something most people never will. Even if it sold 12 copies, you *shipped*.
3. **Reflect.** Write the post-mortem while it's fresh.
4. **Iterate.** Your next game will be better. You now know your tools, your pipeline, your weak spots.
5. **Go back to Phase 0.** The cycle continues.

---

---

# Appendix A: Systems Quick Reference

Every system in the toolkit mapped to its guide, sorted by typical build order:

| # | System | Guide | Phase |
|---|--------|-------|-------|
| 1 | Game Loop & Timestep | [G15](../../monogame-arch/guides/G15_game_loop.md) | 2 (Vertical Slice) |
| 2 | Scene Management | [G38](../../monogame-arch/guides/G38_scene_management.md) | 2 |
| 3 | Input Handling | [G7](../../monogame-arch/guides/G7_input_handling.md) | 2 |
| 4 | Character Movement | [G52](../../monogame-arch/guides/G52_character_controller.md) | 2 |
| 5 | Camera | [G20](../../monogame-arch/guides/G20_camera_systems.md) | 2 |
| 6 | Tilemap & Tiled | [G37](../../monogame-arch/guides/G37_tilemap_systems.md) | 2 |
| 7 | Collision & Physics | [G3](../../monogame-arch/guides/G3_physics_and_collision.md) | 2 |
| 8 | Content Pipeline | [G8](../../monogame-arch/guides/G8_content_pipeline.md) | 2 |
| 9 | Basic Audio | [G6](../../monogame-arch/guides/G6_audio.md) | 2 |
| 10 | Entity Prefabs | [G43](../../monogame-arch/guides/G43_entity_prefabs.md) | 3 (Alpha) |
| 11 | Animation | [G31](../../monogame-arch/guides/G31_animation_state_machines.md) | 3 |
| 12 | AI & Enemies | [G4](../../monogame-arch/guides/G4_ai_systems.md) | 3 |
| 13 | Pathfinding | [G40](../../monogame-arch/guides/G40_pathfinding.md) | 3 |
| 14 | UI Framework | [G5](../../monogame-arch/guides/G5_ui_framework.md) | 3 |
| 15 | Inventory | [G10](../../monogame-arch/guides/G10_custom_game_systems.md) | 3 |
| 16 | Dialogue | [G62](../../monogame-arch/guides/G62_narrative_systems.md) | 3 |
| 17 | Save/Load | [G10](../../monogame-arch/guides/G10_custom_game_systems.md) | 3 |
| 18 | Quests & Progression | [G47](../../monogame-arch/guides/G47_achievements.md) | 3 |
| 19 | Lighting | [G39](../../monogame-arch/guides/G39_2d_lighting.md) | 3 |
| 20 | Particles | [G23](../../monogame-arch/guides/G23_particles.md) | 3 |
| 21 | Settings Menu | [G55](../../monogame-arch/guides/G55_settings_menu.md) | 3 |
| 22 | Game Feel / Juice | [G30](../../monogame-arch/guides/G30_game_feel_tooling.md), [C2](../game-design/C2_game_feel_and_genre_craft.md) | 4 (Beta) |
| 23 | Tweening | [G41](../../monogame-arch/guides/G41_tweening.md) | 4 |
| 24 | Screen Transitions | [G42](../../monogame-arch/guides/G42_screen_transitions.md) | 4 |
| 25 | Trails & Lines | [G60](../../monogame-arch/guides/G60_trails_lines.md) | 4 |
| 26 | Weather Effects | [G57](../../monogame-arch/guides/G57_weather_effects.md) | 4 |
| 27 | Tutorial/Onboarding | [G61](../../monogame-arch/guides/G61_tutorial_onboarding.md) | 4 |
| 28 | Accessibility | [G35](../../monogame-arch/guides/G35_accessibility.md) | 4 |
| 29 | Localization | [G34](../../monogame-arch/guides/G34_localization.md) | 4 |
| 30 | Profiling & Optimization | [G33](../../monogame-arch/guides/G33_profiling_optimization.md), [G13](../../monogame-arch/guides/G13_csharp_performance.md) | 4 |
| 31 | Deployment | [G32](../../monogame-arch/guides/G32_deployment_platform_builds.md) | 5 (RC) |
| 32 | Publishing | [G36](../../monogame-arch/guides/G36_publishing_distribution.md) | 5 |
| 33 | Achievements | [G47](../../monogame-arch/guides/G47_achievements.md), [G48](../../monogame-arch/guides/G48_online_services.md) | 5 |
| 34 | Crash Reporting | [G51](../../monogame-arch/guides/G51_crash_reporting.md) | 5 |

---

# Appendix B: Genre → System Map

What you need depends on what you're making. Here's a quick matrix:

| System | Platformer | Top-Down RPG | Roguelike | Metroidvania | Tactics |
|--------|:---:|:---:|:---:|:---:|:---:|
| Character Controller (G52) | ✅ | — | — | ✅ | — |
| Top-Down Movement (G28) | — | ✅ | ✅ | — | — |
| Isometric (G49) | — | — | — | — | ✅ |
| Tilemap (G37) | ✅ | ✅ | ✅ | ✅ | ✅ |
| Physics (G3) | ✅ | △ | △ | ✅ | — |
| AI (G4) | ✅ | ✅ | ✅ | ✅ | ✅ |
| Pathfinding (G40) | △ | ✅ | ✅ | △ | ✅ |
| Dialogue (G62) | △ | ✅ | △ | ✅ | ✅ |
| Inventory (G10) | △ | ✅ | ✅ | ✅ | △ |
| Save System (G10) | ✅ | ✅ | △ | ✅ | ✅ |
| Procedural Gen (G53) | — | △ | ✅ | — | △ |
| Fog of War (G54) | — | — | ✅ | — | ✅ |
| Lighting (G39) | △ | ✅ | ✅ | ✅ | △ |
| Minimap (G58) | — | ✅ | ✅ | ✅ | △ |
| Weather (G57) | △ | ✅ | △ | △ | △ |

✅ = Essential · △ = Optional/Genre-dependent · — = Not needed

📚 **Full genre breakdown:** [C1 — Genre Reference](../game-design/C1_genre_reference.md)

---

# Appendix C: Doc Reference Index

Every document in the toolkit, organized by category:

### Reference Docs
| Doc | Title |
|-----|-------|
| [R1](../../monogame-arch/reference/R1_library_stack.md) | Library Stack & Install Commands |
| [R2](../../monogame-arch/reference/R2_capability_matrix.md) | Capability Matrix |
| [R3](../../monogame-arch/reference/R3_project_structure.md) | Project Structure |
| [R4](../game-design/R4_game_design_resources.md) | Game Design Resources |

### Explanation Docs
| Doc | Title |
|-----|-------|
| [E1](../../monogame-arch/architecture/E1_architecture_overview.md) | Architecture Overview |
| [E2](../../monogame-arch/architecture/E2_nez_dropped.md) | Why Nez Was Dropped |
| [E3](../../monogame-arch/architecture/E3_engine_alternatives.md) | Engine Alternatives Evaluated |
| [E4](E4_project_management.md) | Solo Project Management |
| [E5](../ai-workflow/E5_ai_workflow.md) | AI-Assisted Dev Workflow |
| [E6](../game-design/E6_game_design_fundamentals.md) | Game Design Fundamentals |
| [E7](../game-design/E7_emergent_puzzle_design.md) | Emergent Puzzle Design |
| [E8](../../monogame-arch/architecture/E8_monogamestudio_postmortem.md) | MonoGameStudio Post-Mortem |
| [E9](E9_solo_dev_playbook.md) | Solo Dev Playbook |

### Guide Docs
| Doc | Title |
|-----|-------|
| [G1](../../monogame-arch/guides/G1_custom_code_recipes.md) | Custom Code Recipes |
| [G2](../../monogame-arch/guides/G2_rendering_and_graphics.md) | Rendering & Graphics |
| [G3](../../monogame-arch/guides/G3_physics_and_collision.md) | Physics & Collision |
| [G4](../../monogame-arch/guides/G4_ai_systems.md) | AI Systems |
| [G5](../../monogame-arch/guides/G5_ui_framework.md) | UI Framework |
| [G6](../../monogame-arch/guides/G6_audio.md) | Audio |
| [G7](../../monogame-arch/guides/G7_input_handling.md) | Input Handling |
| [G8](../../monogame-arch/guides/G8_content_pipeline.md) | Content Pipeline |
| [G9](../../monogame-arch/guides/G9_networking.md) | Networking |
| [G10](../../monogame-arch/guides/G10_custom_game_systems.md) | Custom Game Systems |
| [G11](../../monogame-arch/guides/G11_programming_principles.md) | Programming Principles |
| [G12](../../monogame-arch/guides/G12_design_patterns.md) | Design Patterns |
| [G13](../../monogame-arch/guides/G13_csharp_performance.md) | C# Performance |
| [G14](../../monogame-arch/guides/G14_data_structures.md) | Data Structures |
| [G15](../../monogame-arch/guides/G15_game_loop.md) | Game Loop |
| [G16](../../monogame-arch/guides/G16_debugging.md) | Debugging |
| [G17](../../monogame-arch/guides/G17_testing.md) | Testing |
| [G18](../../monogame-arch/guides/G18_game_programming_patterns.md) | Game Programming Patterns |
| [G19](../../monogame-arch/guides/G19_display_resolution_viewports.md) | Display, Resolution & Viewports |
| [G20](../../monogame-arch/guides/G20_camera_systems.md) | Camera Systems |
| [G21](../../monogame-arch/guides/G21_coordinate_systems.md) | Coordinate Systems & Transforms |
| [G22](../../monogame-arch/guides/G22_parallax_depth_layers.md) | Parallax & Depth Layers |
| [G23](../../monogame-arch/guides/G23_particles.md) | Particles |
| [G24](../../monogame-arch/guides/G24_window_display_management.md) | Window & Display Management |
| [G25](../../monogame-arch/guides/G25_safe_areas_adaptive_layout.md) | Safe Areas & Adaptive Layout |
| [G26](../../monogame-arch/guides/G26_resource_loading_caching.md) | Resource Loading & Caching |
| [G27](../../monogame-arch/guides/G27_shaders_and_effects.md) | Shaders & Visual Effects |
| [G28](../../monogame-arch/guides/G28_top_down_perspective.md) | 3/4 Top-Down Perspective |
| [G29](../../monogame-arch/guides/G29_game_editor.md) | Game Editor |
| [G30](../../monogame-arch/guides/G30_game_feel_tooling.md) | Game Feel Tooling |
| [G31](../../monogame-arch/guides/G31_animation_state_machines.md) | Animation & Sprite State Machines |
| [G32](../../monogame-arch/guides/G32_deployment_platform_builds.md) | Deployment & Platform Builds |
| [G33](../../monogame-arch/guides/G33_profiling_optimization.md) | Profiling & Optimization |
| [G34](../../monogame-arch/guides/G34_localization.md) | Localization |
| [G35](../../monogame-arch/guides/G35_accessibility.md) | Accessibility |
| [G36](../../monogame-arch/guides/G36_publishing_distribution.md) | Publishing & Distribution |
| [G37](../../monogame-arch/guides/G37_tilemap_systems.md) | Tilemap Systems & Tiled |
| [G38](../../monogame-arch/guides/G38_scene_management.md) | Scene & Game State Management |
| [G39](../../monogame-arch/guides/G39_2d_lighting.md) | 2D Lighting & Shadows |
| [G40](../../monogame-arch/guides/G40_pathfinding.md) | Pathfinding |
| [G41](../../monogame-arch/guides/G41_tweening.md) | Tweening & Easing |
| [G42](../../monogame-arch/guides/G42_screen_transitions.md) | Screen Transitions |
| [G43](../../monogame-arch/guides/G43_entity_prefabs.md) | Entity Prefabs & Blueprints |
| [G44](../../monogame-arch/guides/G44_version_control.md) | Version Control |
| [G45](../../monogame-arch/guides/G45_cutscenes.md) | Cutscenes & Scripted Sequences |
| [G46](../../monogame-arch/guides/G46_modding_support.md) | Modding Support |
| [G47](../../monogame-arch/guides/G47_achievements.md) | Achievements & Progression |
| [G48](../../monogame-arch/guides/G48_online_services.md) | Online Services |
| [G49](../../monogame-arch/guides/G49_isometric.md) | Isometric Perspective |
| [G50](../../monogame-arch/guides/G50_hot_reload.md) | Hot Reload & Live Editing |
| [G51](../../monogame-arch/guides/G51_crash_reporting.md) | Crash Reporting |
| [G52](../../monogame-arch/guides/G52_character_controller.md) | 2D Platformer Character Controller |
| [G53](../../monogame-arch/guides/G53_procedural_generation.md) | Procedural Generation |
| [G54](../../monogame-arch/guides/G54_fog_of_war.md) | Fog of War & Visibility |
| [G55](../../monogame-arch/guides/G55_settings_menu.md) | Settings & Options Menu |
| [G56](../../monogame-arch/guides/G56_side_scrolling.md) | Side-Scrolling Perspective |
| [G57](../../monogame-arch/guides/G57_weather_effects.md) | Weather & Environmental Effects |
| [G58](../../monogame-arch/guides/G58_minimap.md) | Minimap Systems |
| [G59](../../monogame-arch/guides/G59_skeletal_animation.md) | 2D Skeletal Animation |
| [G60](../../monogame-arch/guides/G60_trails_lines.md) | Trail & Line Rendering |
| [G61](../../monogame-arch/guides/G61_tutorial_onboarding.md) | Tutorial & Onboarding |
| [G62](../../monogame-arch/guides/G62_narrative_systems.md) | Narrative & Branching Story |
| [G63](../../monogame-arch/guides/G63_water_simulation.md) | 2D Water Simulation |

### Catalog Docs
| Doc | Title |
|-----|-------|
| [C1](../game-design/C1_genre_reference.md) | Genre Reference |
| [C2](../game-design/C2_game_feel_and_genre_craft.md) | Game Feel & Genre Design Craft |

### Playbook Docs
| Doc | Title |
|-----|-------|
| [00](./00_master_playbook.md) | Master Playbook (this document) |
| [01](./01_project_template/) | Project Template |
| [02](./02_pre_production.md) | Pre-Production Checklist |
| [03](./03_production_milestones.md) | Production Milestones |
| [04](./04_starter_platformer/) | Platformer Starter Kit |
| [05](./05_starter_topdown_rpg/) | Top-Down RPG Starter Kit |
| [06](./06_starter_roguelike/) | Roguelike Starter Kit |
| [07](./07_daily_workflow.md) | Daily Dev Workflow |
| [08](./08_playtesting.md) | Playtesting Guide |
| [09](./09_art_pipeline.md) | Art Production Pipeline |
| [10](./10_audio_pipeline.md) | Audio Production Pipeline |
| [11](./11_launch_checklist.md) | Launch Checklist |
| [12](./12_pitfalls.md) | Common Pitfalls & Solutions |
| [13](./13_gdd_template.md) | Game Design Document Template |
| [14](./14_integration_map.md) | System Integration Map |
| [15](./15_polish_checklist.md) | Polish & Juice Checklist |
| [16](./16_performance_budget.md) | Performance Budget Template |
| [17](./17_release_pipeline.md) | Release Build Pipeline |
| [18](./18_marketing_timeline.md) | Marketing Timeline |
| [19](./19_postmortem_template.md) | Post-Mortem Template |

---

# Appendix D: Recommended Reading Order for New Developers

If you're new to MonoGame, ECS, or game dev in general, read these docs first:

1. [E1 — Architecture Overview](../../monogame-arch/architecture/E1_architecture_overview.md) — *Why* the stack is structured this way
2. [E6 — Game Design Fundamentals](../game-design/E6_game_design_fundamentals.md) — Design thinking before code
3. [R1 — Library Stack](../../monogame-arch/reference/R1_library_stack.md) — What you're installing and why
4. [G11 — Programming Principles](../../monogame-arch/guides/G11_programming_principles.md) — SOLID, composition over inheritance
5. [G12 — Design Patterns](../../monogame-arch/guides/G12_design_patterns.md) — Patterns you'll use daily
6. [G18 — Game Programming Patterns](../../monogame-arch/guides/G18_game_programming_patterns.md) — Game-specific patterns
7. [G15 — Game Loop](../../monogame-arch/guides/G15_game_loop.md) — How the frame cycle works
8. [G1 — Custom Code Recipes](../../monogame-arch/guides/G1_custom_code_recipes.md) — The glue code that ties it all together
9. [E9 — Solo Dev Playbook](E9_solo_dev_playbook.md) — Realistic expectations and productivity
10. [R4 — Game Design Resources](../game-design/R4_game_design_resources.md) — Books, talks, and communities

---

# Appendix E: The "No Really, Scope Smaller" Guide

Because everyone thinks they're the exception.

### Time Estimates for Common Features (Solo Dev)

| Feature | Optimistic | Realistic | With Polish |
|---------|:---:|:---:|:---:|
| Basic character controller | 2 days | 4 days | 1 week |
| One enemy type (with AI) | 1 day | 3 days | 5 days |
| One complete level | 2 days | 4 days | 1 week |
| Inventory system | 2 days | 1 week | 2 weeks |
| Dialogue system | 3 days | 1 week | 2 weeks |
| Save/Load | 1 day | 3 days | 1 week |
| Tilemap loading + rendering | 1 day | 2 days | 3 days |
| Full UI (menus, HUD, settings) | 3 days | 1.5 weeks | 3 weeks |
| Boss fight | 3 days | 1 week | 2 weeks |
| Cutscene system | 2 days | 1 week | 2 weeks |
| Procedural generation | 1 week | 3 weeks | 1+ month |
| Multiplayer (basic) | 2 weeks | 2 months | 4+ months |

**Rule of thumb:** Take your estimate, double it, then add 20%. That's closer to reality.

**The "Am I Scoped Right?" Test:**
- Count your "Must Have" features
- Estimate each using the "Realistic" column
- Add them up
- If the total exceeds your timeline → cut features until it fits
- If you can't cut anything → your game is too ambitious for this timeline

---

> *"The secret to shipping games is finishing them."*
>
> That's it. That's the whole secret. Every system in this toolkit, every doc in this knowledge base,
> every phase in this playbook — they all serve one purpose: helping you finish.
>
> The world has enough half-built engines and abandoned prototypes.
> Make a game. Ship it. Then make a better one.
>
> Good luck. Now go build something. 🎮

---

*This playbook is part of the [Universal 2D Engine Toolkit](../INDEX.md) — 76 documents covering every aspect of 2D game development with MonoGame + Arch ECS.*
