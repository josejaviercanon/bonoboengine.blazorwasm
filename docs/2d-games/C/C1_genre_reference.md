# C1 — Genre Reference
> **Category:** Catalog · **Related:** [R2 Capability Matrix](../R/R2_capability_matrix.md) · [G10 Custom Game Systems](../G/G10_custom_game_systems.md) · [E1 Architecture Overview](../E/E1_architecture_overview.md)

---

Comprehensive map of 2D game genres, the core mechanics each requires, and which toolkit systems serve them. All references updated for the post-Nez composed library stack.

---

## Action Platformer (Celeste, Hollow Knight, Dead Cells)

**Core Mechanics:**
- Tight character controller with coyote time, input buffering, variable jump height
- Tile-based or freeform collision with one-way platforms
- State machine for player states (idle, run, jump, wall-slide, dash, attack)
- Sprite animation with frame-perfect hitbox alignment
- Camera follow with lookahead, deadzone, smoothing, screen shake
- Parallax scrolling backgrounds

**Systems:** Arch ECS (player/enemies as entities), BrainAI FSM → [G4](../G/G4_ai_systems.md), custom SpatialHash + AABB/polygon collision → [G3](../G/G3_physics_and_collision.md), MonoGame.Extended camera, MonoGame.Aseprite sprites → [G8](../G/G8_content_pipeline.md), custom tweens → [G1](../G/G1_custom_code_recipes.md), Aether.Physics2D (optional for rope/chain physics)

---

## Metroidvania (Ori, Guacamelee)

**Core Mechanics:**
- All platformer mechanics PLUS:
- Interconnected world map with gated progression (abilities unlock areas)
- Minimap / world map system
- Save stations / persistent world state
- Ability system (wall jump, double jump, dash, grapple unlock over time)
- Backtracking-friendly level design tools

**Additional Systems:** Save/load → [G10](../G/G10_custom_game_systems.md), world state flags dictionary, minimap renderer (custom), Tiled map loader (MonoGame.Extended) → [G8](../G/G8_content_pipeline.md)

---

## Top-Down Action RPG (Zelda: ALTTP, Hyper Light Drifter)

**Core Mechanics:**
- 8-directional or free movement on 2D plane
- Melee/ranged combat with hitboxes and hurtboxes (collision triggers via SpatialHash)
- Enemy AI: patrol, chase, attack, flee
- Inventory system: items, equipment, consumables
- Dialogue system with branching choices
- Loot drops, chests, shops
- Dungeon/room transitions

**Systems:** Arch ECS + custom SpatialHash collision → [G3](../G/G3_physics_and_collision.md), BrainAI (behavior trees, GOAP) → [G4](../G/G4_ai_systems.md), custom inventory → [G10](../G/G10_custom_game_systems.md), custom dialogue tree parser → [G10](../G/G10_custom_game_systems.md), Tiled maps → [G8](../G/G8_content_pipeline.md)

---

## Turn-Based RPG (Final Fantasy, Octopath)

**Core Mechanics:**
- Turn order / initiative system (speed-based or timeline)
- Party management: formation, equipment, stats
- Skill/magic/ability trees
- Random encounters or visible enemies on field
- Battle scene separate from exploration scene
- Status effects with duration tracking
- XP/leveling curves

**Systems:** Custom scene manager (field vs battle scenes) → [G1](../G/G1_custom_code_recipes.md), custom turn manager, custom stat/buff system → [G10](../G/G10_custom_game_systems.md), Gum UI (menus, equipment screens, shops) → [G5](../G/G5_ui_framework.md), custom tweens (battle animations) → [G1](../G/G1_custom_code_recipes.md)

---

## Roguelike / Roguelite (Binding of Isaac, Dead Cells, Hades)

**Core Mechanics:**
- Procedural level generation: BSP rooms, cellular automata caves, wave function collapse
- Permadeath or meta-progression between runs
- Random item/power-up pools with synergies
- Increasing difficulty per floor/zone
- Minimap revealing explored areas

**Systems:** Custom procgen algorithms → [G10](../G/G10_custom_game_systems.md), seed-based RNG, item database as JSON Resources, Arch ECS for bullet patterns and enemy swarms in action roguelites

---

## Bullet Hell / Shoot 'em Up (Touhou, Enter the Gungeon, Ikaruga)

**Core Mechanics:**
- Thousands of projectiles on screen simultaneously
- Pattern scripting: radial, spiral, aimed, random spread
- Precise hitbox (often smaller than sprite)
- Score system with multipliers
- Boss patterns with phase transitions
- Graze mechanics (near-miss detection)

**Systems:** **Arch ECS (critical)** — bullets as pure data components iterated by BulletMovementSystem, BulletLifetimeSystem, BulletCollisionSystem. Custom object pooling → [G1](../G/G1_custom_code_recipes.md). Custom pattern scripting DSL or coroutine-based emitters.

---

## Tower Defense (Bloons TD, Kingdom Rush)

**Core Mechanics:**
- Path-based enemy waves with spawn scheduling
- Tower placement on grid or freeform
- Tower targeting AI: first, last, strongest, closest
- Upgrade trees per tower type
- Economy: currency earned from kills, spent on towers/upgrades
- Wave editor / wave data format

**Systems:** BrainAI pathfinding (A* on grid) → [G4](../G/G4_ai_systems.md), Arch ECS for towers + enemy waves (hundreds of creeps), custom wave scheduler → [G10](../G/G10_custom_game_systems.md), Gum UI (tower selection/upgrade panels) → [G5](../G/G5_ui_framework.md)

---

## Real-Time Strategy (StarCraft-style, simplified 2D)

**Core Mechanics:**
- Unit selection (box select, click select, control groups)
- Pathfinding for many units simultaneously (A* or flow fields)
- Fog of war
- Resource gathering and economy
- Build queues, tech trees
- Formation movement
- Minimap with unit tracking

**Systems:** **Arch ECS (essential)** for hundreds/thousands of units, custom SpatialHash → [G3](../G/G3_physics_and_collision.md), custom flow field pathfinding or BrainAI A* → [G4](../G/G4_ai_systems.md), MonoGame.Extended camera (zoom, pan), Gum UI (resource bars, build menus) → [G5](../G/G5_ui_framework.md), custom fog of war shader → [G2](../G/G2_rendering_and_graphics.md)

---

## Puzzle (Tetris, Baba Is You, Into the Breach)

**Core Mechanics:**
- Grid-based logic with clear rules
- Undo/redo system (Command pattern)
- Level editor / level data format
- Score / completion tracking
- Often turn-based or step-based rather than real-time

**Systems:** Custom grid data structure, Command pattern → [G10](../G/G10_custom_game_systems.md), level serialization (JSON), Gum UI (level select, score display) → [G5](../G/G5_ui_framework.md), custom tweens (piece movement animation) → [G1](../G/G1_custom_code_recipes.md)

---

## Card Game / Deck Builder (Slay the Spire, Balatro)

**Core Mechanics:**
- Card data model: cost, effects, types, rarity
- Deck, hand, draw pile, discard pile, exhaust pile
- Drag-and-drop card interaction or click-to-play
- Turn structure: draw phase, play phase, discard phase
- Status effects / buff system
- Procedural map with branching paths
- Shop / card reward selection

**Systems:** Custom card data as C# classes, custom deck/pile manager, Gum UI or custom card rendering → [G5](../G/G5_ui_framework.md), custom tweens (card draw/play animations) → [G1](../G/G1_custom_code_recipes.md), custom buff system → [G10](../G/G10_custom_game_systems.md). **No existing NuGet library is mature enough — roll your own data model.**

---

## Farming / Life Sim (Stardew Valley, Littlewood)

**Core Mechanics:**
- Tile-based world with interactable objects (crops, animals, furniture)
- Day/night cycle with time progression
- NPC schedules and relationship system
- Crafting system: recipes + materials → output
- Inventory with grid-based storage
- Seasonal calendar affecting gameplay
- Fishing/mining/foraging minigames

**Systems:** Tiled maps (MonoGame.Extended) → [G8](../G/G8_content_pipeline.md), custom time/calendar manager → [G10](../G/G10_custom_game_systems.md), custom NPC scheduler, custom crafting recipe database → [G10](../G/G10_custom_game_systems.md), custom inventory grid → [G10](../G/G10_custom_game_systems.md), custom scene manager → [G1](../G/G1_custom_code_recipes.md), Gum UI (inventory, crafting, dialogue, shop) → [G5](../G/G5_ui_framework.md)

---

## Fighting Game (Street Fighter, Skullgirls-style)

**Core Mechanics:**
- Frame-precise input reading with input buffering
- Hitbox/hurtbox system with frame data
- State machine with cancel windows (normal → special → super)
- Rollback netcode for online play
- Training mode with frame data display

**Systems:** Custom input buffer (ring buffer) → [G7](../G/G7_input_handling.md), custom frame data system, BrainAI FSM or custom hierarchical state machine → [G4](../G/G4_ai_systems.md), LiteNetLib for networking → [G9](../G/G9_networking.md), custom rollback implementation → [G9](../G/G9_networking.md), precise fixed-timestep physics

---

## Visual Novel / Narrative (Ace Attorney, VA-11 Hall-A)

**Core Mechanics:**
- Text rendering with typewriter effect
- Character portrait display with expressions
- Branching dialogue trees with choice points
- Background/scene switching
- Music and SFX triggers tied to script
- Save/load at any point in script

**Systems:** Custom script parser (Ink, Yarn, or custom JSON format), FontStashSharp for text rendering, custom tweens (fade transitions) → [G1](../G/G1_custom_code_recipes.md), Gum UI (choice buttons) → [G5](../G/G5_ui_framework.md), custom save state serialization → [G10](../G/G10_custom_game_systems.md)

---

## Sandbox / Survival (Terraria, Noita)

**Core Mechanics:**
- Destructible/constructible terrain (per-pixel or per-tile)
- Crafting and inventory
- Day/night cycle, weather
- Enemy spawning based on biome/time
- Lighting system (dynamic 2D lighting)
- Fluid simulation (water, lava) for Noita-style

**Systems:** Custom chunk-based tile world, **Arch ECS** (falling sand / pixel simulation for Noita-style — thousands of particles), custom or Penumbra lighting → [G2](../G/G2_rendering_and_graphics.md), custom crafting/inventory → [G10](../G/G10_custom_game_systems.md)

---

## Rhythm Game (Crypt of the NecroDancer, Muse Dash)

**Core Mechanics:**
- Audio-synced beat detection or authored beat maps
- Input timing windows (perfect, great, good, miss)
- Score multiplier chains
- Note highway or rhythm indicator rendering
- BPM-locked game logic

**Systems:** **FMOD via FmodForFoxes** (precise audio timing, beat callbacks) → [G6](../G/G6_audio.md) or MonoGame audio with manual beat tracking, custom beat map format (JSON/binary), custom timing judgment system, custom tweens (visual feedback) → [G1](../G/G1_custom_code_recipes.md)

---

## Vampire Survivors / Swarm Survival

**Core Mechanics:**
- Auto-attack mechanics, hundreds of enemies
- Level-up choice system (pick 1 of 3 upgrades)
- Scaling difficulty over time
- Area damage, projectile spray, aura effects
- Experience gems / magnet pickup

**Systems:** **Arch ECS (critical)** — this genre demands processing thousands of entities, custom SpatialHash for collision → [G3](../G/G3_physics_and_collision.md), custom level-up/upgrade system, custom object pooling → [G1](../G/G1_custom_code_recipes.md)

---

## Idle / Incremental (Cookie Clicker, Melvor Idle)

**Core Mechanics:**
- Big number math (beyond double precision)
- Offline progress calculation
- Prestige/rebirth systems
- Upgrade trees with exponential costs
- Minimal rendering, heavy on UI

**Systems:** Custom big number library or `decimal`/`BigInteger`, **Gum UI (primary interface)** → [G5](../G/G5_ui_framework.md), custom save system with timestamps for offline calc → [G10](../G/G10_custom_game_systems.md), minimal rendering needed

---

## Physics Puzzle (Angry Birds, Cut the Rope, World of Goo)

**Core Mechanics:**
- Full rigid body physics simulation
- Destructible structures
- Rope/spring/joint constraints
- Projectile trajectory prediction

**Systems:** **Aether.Physics2D (essential)** — full Box2D-style simulation with joints, springs, raycasting → [G3](../G/G3_physics_and_collision.md), custom level editor data format, Verlet integration for ropes (~200 lines) → [G3](../G/G3_physics_and_collision.md)
