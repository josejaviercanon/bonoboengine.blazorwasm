# 14 — System Integration Map

> **Category:** Playbook · **Purpose:** Navigate 76+ docs, understand system dependencies, decide what to build next  
> **Audience:** Solo indie dev building a 2D game with MonoGame + Arch ECS

---

## 1. The Big Picture

Every system in the toolkit falls into one of six layers. Higher layers depend on lower ones.

```
┌─────────────────────────────────────────────────────────────────────┐
│                          SHIP                                       │
│  Deployment (G32) · Profiling (G33) · Testing (G17) · Publish (G36)│
│  Crash Reporting (G51) · Version Control (G44) · Achievements (G47)│
├─────────────────────────────────────────────────────────────────────┤
│                          POLISH                                     │
│  Particles (G23) · Camera (G20) · Transitions (G42) · Tweening(G41)│
│  Game Feel (G30) · Lighting (G39) · Weather (G57) · Trails (G60)  │
│  Shaders (G27) · Parallax (G22) · Water (G63)                     │
├─────────────────────────────────────────────────────────────────────┤
│                          CONTENT                                    │
│  Tilemaps (G37) · Animation (G31) · Skeletal Anim (G59)           │
│  Dialogue (G62) · UI Framework (G5) · Cutscenes (G45)             │
│  Tutorial/Onboarding (G61) · Localization (G34) · Accessibility(G35)│
│  Settings Menu (G55) · Minimap (G58) · Fog of War (G54)           │
├─────────────────────────────────────────────────────────────────────┤
│                          GAMEPLAY                                   │
│  Physics/Collision (G3) · AI Systems (G4) · Pathfinding (G40)     │
│  Combat · Character Controller (G52) · Inventory · Save/Load       │
│  Procedural Gen (G53) · Networking (G9) · Modding (G46)           │
│  Custom Game Systems (G10) · Entity Prefabs (G43)                  │
├─────────────────────────────────────────────────────────────────────┤
│                       INFRASTRUCTURE                                │
│  Input Handling (G7) · Audio (G6) · Rendering (G2) · Content       │
│  Pipeline (G8) · Resource Loading (G26) · Display/Window (G24)     │
│  Resolution/Viewports (G19) · Coordinates (G21) · Hot Reload (G50)│
├─────────────────────────────────────────────────────────────────────┤
│                           CORE                                      │
│  Game Loop (G15) · ECS / Arch · Scene Management (G38)             │
│  Design Patterns (G12) · Programming Principles (G11)              │
│  C# Performance (G13) · Data Structures (G14)                      │
│  Architecture (E1) · Project Structure (R3)                         │
└─────────────────────────────────────────────────────────────────────┘
```

**Read direction:** Build bottom-up. Core first, then Infrastructure, then Gameplay, etc.

---

## 2. Dependency Graph

### Core Layer

| System | Doc | Depends On | Depended On By |
|--------|-----|-----------|----------------|
| Game Loop | [G15](../../monogame-arch/guides/G15_game_loop.md) | — (root) | Everything |
| ECS (Arch) | [R1](../../monogame-arch/reference/R1_library_stack.md), [E1](../../monogame-arch/architecture/E1_architecture_overview.md) | Game Loop | Everything |
| Scene Management | [G38](../../monogame-arch/guides/G38_scene_management.md) | Game Loop, ECS | All gameplay/content systems |
| Design Patterns | [G12](../../monogame-arch/guides/G12_design_patterns.md), [G18](../../monogame-arch/guides/G18_game_programming_patterns.md) | — | All systems (reference) |
| Project Structure | [R3](../../monogame-arch/reference/R3_project_structure.md) | — | All systems (reference) |

### Infrastructure Layer

| System | Doc | Depends On | Depended On By |
|--------|-----|-----------|----------------|
| Rendering | [G2](../../monogame-arch/guides/G2_rendering_and_graphics.md) | Game Loop, Coordinates | Camera, Particles, UI, Tilemaps, Lighting, Shaders, everything visual |
| Input Handling | [G7](../../monogame-arch/guides/G7_input_handling.md) | Game Loop | Character Controller, UI, Menus, Dialogue, Editor |
| Audio | [G6](../../monogame-arch/guides/G6_audio.md) | Game Loop, Content Pipeline | Combat, UI, Dialogue, Cutscenes, Weather |
| Content Pipeline | [G8](../../monogame-arch/guides/G8_content_pipeline.md) | Project Structure | Tilemaps, Animation, Audio, Sprites, all assets |
| Resource Loading | [G26](../../monogame-arch/guides/G26_resource_loading_caching.md) | Content Pipeline | Scene Management, Tilemaps, Animation |
| Display/Window | [G24](../../monogame-arch/guides/G24_window_display_management.md) | Game Loop | Resolution, Safe Areas, Rendering |
| Resolution/Viewports | [G19](../../monogame-arch/guides/G19_display_resolution_viewports.md) | Display, Rendering | Camera, UI, Safe Areas |
| Coordinate Systems | [G21](../../monogame-arch/guides/G21_coordinate_systems.md) | Rendering | Physics, Tilemaps, Camera, Input (screen↔world) |
| Hot Reload | [G50](../../monogame-arch/guides/G50_hot_reload.md) | Content Pipeline | Editor, Dev workflow |

### Gameplay Layer

| System | Doc | Depends On | Depended On By |
|--------|-----|-----------|----------------|
| Physics/Collision | [G3](../../monogame-arch/guides/G3_physics_and_collision.md) | Game Loop, ECS, Coordinates | Character Controller, Combat, AI, Platformer movement |
| AI Systems | [G4](../../monogame-arch/guides/G4_ai_systems.md) | ECS, Physics | Enemies, NPCs, Combat |
| Pathfinding | [G40](../../monogame-arch/guides/G40_pathfinding.md) | Tilemaps, AI | NPCs, Enemies, RTS units |
| Character Controller | [G52](../../monogame-arch/guides/G52_character_controller.md) | Input, Physics, ECS | Platformer, Top-Down, Side-Scrolling |
| Custom Game Systems | [G10](../../monogame-arch/guides/G10_custom_game_systems.md) | ECS | Combat, Inventory, Quests, Save/Load |
| Entity Prefabs | [G43](../../monogame-arch/guides/G43_entity_prefabs.md) | ECS, Content Pipeline | Spawning, Level Design, Editors |
| Procedural Gen | [G53](../../monogame-arch/guides/G53_procedural_generation.md) | Tilemaps, ECS | Roguelike, Level variety |
| Networking | [G9](../../monogame-arch/guides/G9_networking.md) | ECS, Game Loop | Multiplayer, Online Services |
| Modding | [G46](../../monogame-arch/guides/G46_modding_support.md) | Content Pipeline, Scripting | Community content |

### Content Layer

| System | Doc | Depends On | Depended On By |
|--------|-----|-----------|----------------|
| Tilemaps | [G37](../../monogame-arch/guides/G37_tilemap_systems.md) | Rendering, Content Pipeline, Coordinates | Pathfinding, Collision, Camera, Minimap, ProcGen |
| Animation State Machines | [G31](../../monogame-arch/guides/G31_animation_state_machines.md) | Rendering, ECS | Character Controller, Combat, NPCs |
| Skeletal Animation | [G59](../../monogame-arch/guides/G59_skeletal_animation.md) | Rendering, Content Pipeline | Characters, Cutscenes |
| Dialogue/Narrative | [G62](../../monogame-arch/guides/G62_narrative_systems.md) | UI, Input, Audio | Cutscenes, Quests, NPCs |
| UI Framework | [G5](../../monogame-arch/guides/G5_ui_framework.md) | Rendering, Input | Menus, HUD, Inventory, Dialogue, Settings |
| Cutscenes | [G45](../../monogame-arch/guides/G45_cutscenes.md) | Animation, Dialogue, Camera, Audio | Narrative flow |
| Tutorial/Onboarding | [G61](../../monogame-arch/guides/G61_tutorial_onboarding.md) | UI, Input, Scene Management | Player experience |
| Localization | [G34](../../monogame-arch/guides/G34_localization.md) | UI, Content Pipeline | All text display |
| Accessibility | [G35](../../monogame-arch/guides/G35_accessibility.md) | Input, UI, Audio | All player-facing systems |
| Settings Menu | [G55](../../monogame-arch/guides/G55_settings_menu.md) | UI, Input, Audio | Save/Load (prefs), Display |
| Minimap | [G58](../../monogame-arch/guides/G58_minimap.md) | Rendering, Camera, Tilemaps | HUD |
| Fog of War | [G54](../../monogame-arch/guides/G54_fog_of_war.md) | Tilemaps, Rendering | Roguelike, Strategy |

### Polish Layer

| System | Doc | Depends On | Depended On By |
|--------|-----|-----------|----------------|
| Camera Systems | [G20](../../monogame-arch/guides/G20_camera_systems.md) | Game Loop, Rendering, Coordinates | Parallax, Minimap, Particles, Screen Shake |
| Particles | [G23](../../monogame-arch/guides/G23_particles.md) | Rendering, ECS | Combat VFX, Weather, Environment |
| Parallax/Depth | [G22](../../monogame-arch/guides/G22_parallax_depth_layers.md) | Camera, Rendering | Visual depth, Side-Scrolling |
| Screen Transitions | [G42](../../monogame-arch/guides/G42_screen_transitions.md) | Scene Management, Rendering | Scene flow |
| Tweening | [G41](../../monogame-arch/guides/G41_tweening.md) | Game Loop | UI animation, Camera, Juice |
| Game Feel | [G30](../../monogame-arch/guides/G30_game_feel_tooling.md) | Input, Camera, Particles, Tweening | Everything player-facing |
| 2D Lighting | [G39](../../monogame-arch/guides/G39_2d_lighting.md) | Rendering, Shaders | Atmosphere, Fog of War |
| Shaders/Effects | [G27](../../monogame-arch/guides/G27_shaders_and_effects.md) | Rendering | Lighting, Water, Visual polish |
| Weather Effects | [G57](../../monogame-arch/guides/G57_weather_effects.md) | Particles, Rendering, Audio | Atmosphere |
| Trails/Lines | [G60](../../monogame-arch/guides/G60_trails_lines.md) | Rendering | Combat VFX, Movement trails |
| Water Simulation | [G63](../../monogame-arch/guides/G63_water_simulation.md) | Rendering, Shaders, Physics | Environment |

### Ship Layer

| System | Doc | Depends On | Depended On By |
|--------|-----|-----------|----------------|
| Profiling/Optimization | [G33](../../monogame-arch/guides/G33_profiling_optimization.md) | All systems | Performance targets |
| Testing | [G17](../../monogame-arch/guides/G17_testing.md) | All systems | Release confidence |
| Debugging | [G16](../../monogame-arch/guides/G16_debugging.md) | All systems | Development workflow |
| Deployment | [G32](../../monogame-arch/guides/G32_deployment_platform_builds.md) | Project Structure, Content Pipeline | Distribution |
| Publishing | [G36](../../monogame-arch/guides/G36_publishing_distribution.md) | Deployment | Store presence |
| Crash Reporting | [G51](../../monogame-arch/guides/G51_crash_reporting.md) | Deployment | Post-launch support |
| Version Control | [G44](../../monogame-arch/guides/G44_version_control.md) | — | All development |
| Achievements | [G47](../../monogame-arch/guides/G47_achievements.md) | Custom Systems, Online Services | Player engagement |
| Online Services | [G48](../../monogame-arch/guides/G48_online_services.md) | Networking | Achievements, Leaderboards |

### ASCII Dependency Flow

```
                          ┌──────────┐
                          │ Game Loop│ (G15)
                          │  (root)  │
                          └────┬─────┘
                    ┌──────────┼──────────┐
                    ▼          ▼          ▼
               ┌────────┐ ┌───────┐ ┌─────────┐
               │  ECS   │ │ Input │ │Rendering│
               │ (Arch) │ │ (G7)  │ │  (G2)   │
               └───┬────┘ └───┬───┘ └────┬────┘
                   │          │          │
          ┌────────┼──────┐   │    ┌─────┼──────────┐
          ▼        ▼      ▼   ▼    ▼     ▼          ▼
      ┌───────┐┌──────┐┌──────────┐┌───────┐ ┌──────────┐
      │Scenes ││Phys/ ││Character ││Camera │ │ Tilemaps │
      │ (G38) ││Collis││Controller││ (G20) │ │  (G37)   │
      └───┬───┘│(G3)  ││  (G52)   │└───┬───┘ └────┬─────┘
          │    └──┬───┘└──────────┘    │          │
          │       │                     │          │
          ▼       ▼                     ▼          ▼
      ┌───────┐┌──────┐          ┌──────────┐┌──────────┐
      │ UI    ││  AI  │          │ Parallax ││Pathfind- │
      │ (G5)  ││ (G4) │          │  (G22)   ││ing (G40) │
      └───────┘└──┬───┘          └──────────┘└──────────┘
                  │
            ┌─────┼──────┐
            ▼     ▼      ▼
        ┌──────┐┌─────┐┌───────┐
        │Combat││Path-││Dialog-│
        │      ││find ││ue(G62)│
        └──────┘│(G40)│└───────┘
                └─────┘
```

---

## 3. Implementation Order

### Phase 1 — Foundation (Week 1–2)

> Goal: Window opens, entity moves on screen, scenes switch.

| Step | System | Doc | What You Get |
|------|--------|-----|-------------|
| 1 | Project setup | [R3](../../monogame-arch/reference/R3_project_structure.md), [R1](../../monogame-arch/reference/R1_library_stack.md) | Clean solution, packages installed |
| 2 | Game loop | [G15](../../monogame-arch/guides/G15_game_loop.md) | Fixed timestep, Update/Draw split |
| 3 | ECS bootstrap | [E1](../../monogame-arch/architecture/E1_architecture_overview.md) | World, entities, basic systems |
| 4 | Rendering basics | [G2](../../monogame-arch/guides/G2_rendering_and_graphics.md) | SpriteBatch, draw order, textures |
| 5 | Input handling | [G7](../../monogame-arch/guides/G7_input_handling.md) | Keyboard/gamepad, action mapping |
| 6 | Coordinates | [G21](../../monogame-arch/guides/G21_coordinate_systems.md) | World↔screen transforms |
| 7 | Scene management | [G38](../../monogame-arch/guides/G38_scene_management.md) | Scene stack, transitions |
| 8 | Content pipeline | [G8](../../monogame-arch/guides/G8_content_pipeline.md) | Asset loading, MGCB setup |

**Milestone:** A sprite moves with input, scene switches work.

### Phase 2 — Core Gameplay (Week 3–4)

> Goal: Things collide, camera follows, world is tiled, stuff animates.

| Step | System | Doc | What You Get |
|------|--------|-----|-------------|
| 9 | Physics/Collision | [G3](../../monogame-arch/guides/G3_physics_and_collision.md) | AABB, spatial hashing, responses |
| 10 | Camera | [G20](../../monogame-arch/guides/G20_camera_systems.md) | Follow, deadzone, bounds |
| 11 | Tilemaps | [G37](../../monogame-arch/guides/G37_tilemap_systems.md) | Tile rendering, collision layers |
| 12 | Animation | [G31](../../monogame-arch/guides/G31_animation_state_machines.md) | Sprite animation, state machines |
| 13 | Character controller | [G52](../../monogame-arch/guides/G52_character_controller.md) | Movement, jump, gravity (if platformer) |
| 14 | Entity prefabs | [G43](../../monogame-arch/guides/G43_entity_prefabs.md) | Spawn templates, data-driven entities |

**Milestone:** Player moves through a tiled world, animated, with working collision.

### Phase 3 — Systems (Week 5–8)

> Goal: Game has AI, combat, items, dialogue, and persistence.

| Step | System | Doc | What You Get |
|------|--------|-----|-------------|
| 15 | AI systems | [G4](../../monogame-arch/guides/G4_ai_systems.md) | State machines, behavior trees |
| 16 | Pathfinding | [G40](../../monogame-arch/guides/G40_pathfinding.md) | A*, nav mesh for top-down |
| 17 | Combat | [G10](../../monogame-arch/guides/G10_custom_game_systems.md) | Damage, hitboxes, health |
| 18 | Inventory | [G10](../../monogame-arch/guides/G10_custom_game_systems.md) | Item system, equipment |
| 19 | Dialogue | [G62](../../monogame-arch/guides/G62_narrative_systems.md) | Dialogue trees, branching |
| 20 | Save/Load | [G10](../../monogame-arch/guides/G10_custom_game_systems.md), [R1](../../monogame-arch/reference/R1_library_stack.md) | Persistence, serialization |
| 21 | Custom systems | [G10](../../monogame-arch/guides/G10_custom_game_systems.md) | Quest tracking, scoring, etc. |

**Milestone:** Playable vertical slice — can fight, talk, save, resume.

### Phase 4 — Polish (Week 9–12)

> Goal: Game feels good, looks good, sounds good.

| Step | System | Doc | What You Get |
|------|--------|-----|-------------|
| 22 | UI framework | [G5](../../monogame-arch/guides/G5_ui_framework.md) | Menus, HUD, health bars |
| 23 | Audio | [G6](../../monogame-arch/guides/G6_audio.md) | Music, SFX, spatial audio |
| 24 | Particles | [G23](../../monogame-arch/guides/G23_particles.md) | Explosions, dust, impacts |
| 25 | Tweening | [G41](../../monogame-arch/guides/G41_tweening.md) | Smooth animations, easing |
| 26 | Screen transitions | [G42](../../monogame-arch/guides/G42_screen_transitions.md) | Fades, wipes, scene changes |
| 27 | Game feel | [G30](../../monogame-arch/guides/G30_game_feel_tooling.md) | Screen shake, hitstop, juice |
| 28 | Parallax | [G22](../../monogame-arch/guides/G22_parallax_depth_layers.md) | Background depth layers |
| 29 | Lighting | [G39](../../monogame-arch/guides/G39_2d_lighting.md) | Dynamic lights, shadows |
| 30 | Settings menu | [G55](../../monogame-arch/guides/G55_settings_menu.md) | Volume, display, controls |

**Milestone:** Game looks and feels like a real game. Playtesters enjoy it.

### Phase 5 — Ship (Week 13+)

> Goal: Game runs well, is tested, and reaches players.

| Step | System | Doc | What You Get |
|------|--------|-----|-------------|
| 31 | Profiling | [G33](../../monogame-arch/guides/G33_profiling_optimization.md) | 60 FPS everywhere |
| 32 | Testing | [G17](../../monogame-arch/guides/G17_testing.md) | Automated tests, regression |
| 33 | Debugging tools | [G16](../../monogame-arch/guides/G16_debugging.md) | Dev console, overlays |
| 34 | Localization | [G34](../../monogame-arch/guides/G34_localization.md) | Multi-language support |
| 35 | Accessibility | [G35](../../monogame-arch/guides/G35_accessibility.md) | Remapping, colorblind, text size |
| 36 | Deployment | [G32](../../monogame-arch/guides/G32_deployment_platform_builds.md) | Platform builds |
| 37 | Publishing | [G36](../../monogame-arch/guides/G36_publishing_distribution.md) | Store pages, marketing |
| 38 | Crash reporting | [G51](../../monogame-arch/guides/G51_crash_reporting.md) | Post-launch error tracking |
| 39 | Achievements | [G47](../../monogame-arch/guides/G47_achievements.md) | Platform achievements |

**Milestone:** Game is live. Players are playing.

---

## 4. System-to-Doc Reference Table

### G-Series: Guides (63 docs)

| # | System | Primary Doc | Related Docs | Key Library | Phase |
|---|--------|------------|-------------|-------------|-------|
| G1 | Custom Code Recipes | [G1](../../monogame-arch/guides/G1_custom_code_recipes.md) | G10, G12 | — | Ref |
| G2 | Rendering & Graphics | [G2](../../monogame-arch/guides/G2_rendering_and_graphics.md) | G19, G21, G27 | MonoGame SpriteBatch | 1 |
| G3 | Physics & Collision | [G3](../../monogame-arch/guides/G3_physics_and_collision.md) | G52, G37 | Aether.Physics2D or custom | 2 |
| G4 | AI Systems | [G4](../../monogame-arch/guides/G4_ai_systems.md) | G40, G10 | Custom FSM/BT | 3 |
| G5 | UI Framework | [G5](../../monogame-arch/guides/G5_ui_framework.md) | G7, G34, G55 | ImGui.NET or custom | 4 |
| G6 | Audio | [G6](../../monogame-arch/guides/G6_audio.md) | G8, R1 | FMOD or MonoGame audio | 4 |
| G7 | Input Handling | [G7](../../monogame-arch/guides/G7_input_handling.md) | G52, G5 | MonoGame Input | 1 |
| G8 | Content Pipeline | [G8](../../monogame-arch/guides/G8_content_pipeline.md) | R1, G26 | MGCB | 1 |
| G9 | Networking | [G9](../../monogame-arch/guides/G9_networking.md) | G48, G15 | LiteNetLib | 3+ |
| G10 | Custom Game Systems | [G10](../../monogame-arch/guides/G10_custom_game_systems.md) | G12, G18, E1 | Arch ECS | 3 |
| G11 | Programming Principles | [G11](../../monogame-arch/guides/G11_programming_principles.md) | G12, G13 | — | Ref |
| G12 | Design Patterns | [G12](../../monogame-arch/guides/G12_design_patterns.md) | G18, G11 | — | Ref |
| G13 | C# Performance | [G13](../../monogame-arch/guides/G13_csharp_performance.md) | G33, G14 | — | Ref |
| G14 | Data Structures | [G14](../../monogame-arch/guides/G14_data_structures.md) | G13, G40 | — | Ref |
| G15 | Game Loop | [G15](../../monogame-arch/guides/G15_game_loop.md) | E1, R3 | MonoGame Game class | 1 |
| G16 | Debugging | [G16](../../monogame-arch/guides/G16_debugging.md) | G33, G17 | ImGui.NET | 5 |
| G17 | Testing | [G17](../../monogame-arch/guides/G17_testing.md) | G16, G11 | xUnit / NUnit | 5 |
| G18 | Game Programming Patterns | [G18](../../monogame-arch/guides/G18_game_programming_patterns.md) | G12, G10 | — | Ref |
| G19 | Display Resolution & Viewports | [G19](../../monogame-arch/guides/G19_display_resolution_viewports.md) | G24, G25, G2 | MonoGame Viewport | 1 |
| G20 | Camera Systems | [G20](../../monogame-arch/guides/G20_camera_systems.md) | G22, G58, G21 | Custom | 2 |
| G21 | Coordinate Systems | [G21](../../monogame-arch/guides/G21_coordinate_systems.md) | G20, G37, G3 | — | 1 |
| G22 | Parallax & Depth Layers | [G22](../../monogame-arch/guides/G22_parallax_depth_layers.md) | G20, G2, G56 | Custom | 4 |
| G23 | Particles | [G23](../../monogame-arch/guides/G23_particles.md) | G2, G30 | Custom or library | 4 |
| G24 | Window & Display Management | [G24](../../monogame-arch/guides/G24_window_display_management.md) | G19, G25 | MonoGame GameWindow | 1 |
| G25 | Safe Areas & Adaptive Layout | [G25](../../monogame-arch/guides/G25_safe_areas_adaptive_layout.md) | G19, G24, G5 | Custom | 4 |
| G26 | Resource Loading & Caching | [G26](../../monogame-arch/guides/G26_resource_loading_caching.md) | G8, G38 | Custom | 2 |
| G27 | Shaders & Effects | [G27](../../monogame-arch/guides/G27_shaders_and_effects.md) | G2, G39 | HLSL / MonoGame Effect | 4 |
| G28 | Top-Down Perspective | [G28](../../monogame-arch/guides/G28_top_down_perspective.md) | G37, G52, C1 | Custom | 2 |
| G29 | Game Editor | [G29](../../monogame-arch/guides/G29_game_editor.md) | G5, G50, G43 | ImGui.NET | 5 |
| G30 | Game Feel & Tooling | [G30](../../monogame-arch/guides/G30_game_feel_tooling.md) | G41, G20, G23, C2 | Custom | 4 |
| G31 | Animation State Machines | [G31](../../monogame-arch/guides/G31_animation_state_machines.md) | G2, G59 | Custom | 2 |
| G32 | Deployment & Platform Builds | [G32](../../monogame-arch/guides/G32_deployment_platform_builds.md) | G36, R3 | dotnet publish | 5 |
| G33 | Profiling & Optimization | [G33](../../monogame-arch/guides/G33_profiling_optimization.md) | G13, G16 | dotTrace, PerfView | 5 |
| G34 | Localization | [G34](../../monogame-arch/guides/G34_localization.md) | G5, G62 | Custom or library | 5 |
| G35 | Accessibility | [G35](../../monogame-arch/guides/G35_accessibility.md) | G7, G5, G6 | Custom | 5 |
| G36 | Publishing & Distribution | [G36](../../monogame-arch/guides/G36_publishing_distribution.md) | G32, G47 | Steamworks.NET | 5 |
| G37 | Tilemap Systems | [G37](../../monogame-arch/guides/G37_tilemap_systems.md) | G3, G40, G20 | Tiled + custom loader | 2 |
| G38 | Scene Management | [G38](../../monogame-arch/guides/G38_scene_management.md) | G15, G26, G42 | Custom | 1 |
| G39 | 2D Lighting | [G39](../../monogame-arch/guides/G39_2d_lighting.md) | G27, G2 | Penumbra or custom | 4 |
| G40 | Pathfinding | [G40](../../monogame-arch/guides/G40_pathfinding.md) | G37, G4, G14 | Custom A* | 3 |
| G41 | Tweening | [G41](../../monogame-arch/guides/G41_tweening.md) | G15, G30 | Custom or library | 4 |
| G42 | Screen Transitions | [G42](../../monogame-arch/guides/G42_screen_transitions.md) | G38, G2 | Custom | 4 |
| G43 | Entity Prefabs | [G43](../../monogame-arch/guides/G43_entity_prefabs.md) | ECS, G8 | Arch + custom | 2 |
| G44 | Version Control | [G44](../../monogame-arch/guides/G44_version_control.md) | E4 | Git | 1 |
| G45 | Cutscenes | [G45](../../monogame-arch/guides/G45_cutscenes.md) | G31, G62, G20, G6 | Custom | 4 |
| G46 | Modding Support | [G46](../../monogame-arch/guides/G46_modding_support.md) | G8, G10 | Custom | 5 |
| G47 | Achievements | [G47](../../monogame-arch/guides/G47_achievements.md) | G48, G36 | Steamworks.NET | 5 |
| G48 | Online Services | [G48](../../monogame-arch/guides/G48_online_services.md) | G9, G36 | Steamworks.NET | 5 |
| G49 | Isometric | [G49](../../monogame-arch/guides/G49_isometric.md) | G37, G21, C1 | Custom | 2 |
| G50 | Hot Reload | [G50](../../monogame-arch/guides/G50_hot_reload.md) | G8, G26 | Custom file watcher | 3 |
| G51 | Crash Reporting | [G51](../../monogame-arch/guides/G51_crash_reporting.md) | G32 | Sentry or custom | 5 |
| G52 | Character Controller | [G52](../../monogame-arch/guides/G52_character_controller.md) | G3, G7, G31 | Custom | 2 |
| G53 | Procedural Generation | [G53](../../monogame-arch/guides/G53_procedural_generation.md) | G37, G14 | Custom | 3 |
| G54 | Fog of War | [G54](../../monogame-arch/guides/G54_fog_of_war.md) | G37, G2, G39 | Custom | 3 |
| G55 | Settings Menu | [G55](../../monogame-arch/guides/G55_settings_menu.md) | G5, G7, G6 | Custom | 4 |
| G56 | Side-Scrolling | [G56](../../monogame-arch/guides/G56_side_scrolling.md) | G52, G22, G20, C1 | Custom | 2 |
| G57 | Weather Effects | [G57](../../monogame-arch/guides/G57_weather_effects.md) | G23, G2, G6 | Custom | 4 |
| G58 | Minimap | [G58](../../monogame-arch/guides/G58_minimap.md) | G20, G37, G2 | Custom | 4 |
| G59 | Skeletal Animation | [G59](../../monogame-arch/guides/G59_skeletal_animation.md) | G2, G8 | Spine or custom | 3 |
| G60 | Trails & Lines | [G60](../../monogame-arch/guides/G60_trails_lines.md) | G2 | Custom | 4 |
| G61 | Tutorial & Onboarding | [G61](../../monogame-arch/guides/G61_tutorial_onboarding.md) | G5, G7, G38 | Custom | 4 |
| G62 | Narrative Systems | [G62](../../monogame-arch/guides/G62_narrative_systems.md) | G5, G7, G6 | YarnSpinner or custom | 3 |
| G63 | Water Simulation | [G63](../../monogame-arch/guides/G63_water_simulation.md) | G2, G27, G3 | Custom | 4 |

### R-Series: Reference (4 docs)

| # | Topic | Doc | Use For |
|---|-------|-----|---------|
| R1 | Library Stack | [R1](../../monogame-arch/reference/R1_library_stack.md) | Package list, install commands, version pins |
| R2 | Capability Matrix | [R2](../../monogame-arch/reference/R2_capability_matrix.md) | What each library provides, feature coverage |
| R3 | Project Structure | [R3](../../monogame-arch/reference/R3_project_structure.md) | Folder layout, solution architecture |
| R4 | Game Design Resources | [R4](../game-design/R4_game_design_resources.md) | Books, talks, external learning |

### E-Series: Essays (9 docs)

| # | Topic | Doc | Use For |
|---|-------|-----|---------|
| E1 | Architecture Overview | [E1](../../monogame-arch/architecture/E1_architecture_overview.md) | Big-picture ECS architecture decisions |
| E2 | Nez Dropped | [E2](../../monogame-arch/architecture/E2_nez_dropped.md) | Why we don't use Nez, historical context |
| E3 | Engine Alternatives | [E3](../../monogame-arch/architecture/E3_engine_alternatives.md) | Comparison of engines/frameworks |
| E4 | Project Management | [E4](E4_project_management.md) | Scope, milestones, shipping |
| E5 | AI Workflow | [E5](../ai-workflow/E5_ai_workflow.md) | Using AI tools in game dev |
| E6 | Game Design Fundamentals | [E6](../game-design/E6_game_design_fundamentals.md) | Core design principles |
| E7 | Emergent Puzzle Design | [E7](../game-design/E7_emergent_puzzle_design.md) | Systems-driven puzzle games |
| E8 | MonoGameStudio Postmortem | [E8](../../monogame-arch/architecture/E8_monogamestudio_postmortem.md) | Lessons from a real project |
| E9 | Solo Dev Playbook | [E9](E9_solo_dev_playbook.md) | Strategies for solo development |

### C-Series: Craft Guides (2 docs)

| # | Topic | Doc | Use For |
|---|-------|-----|---------|
| C1 | Genre Reference | [C1](../game-design/C1_genre_reference.md) | Genre conventions, mechanics breakdown |
| C2 | Game Feel & Genre Craft | [C2](../game-design/C2_game_feel_and_genre_craft.md) | Making each genre *feel* right |

---

## 5. Genre-Specific Build Orders

### 🏃 Platformer

```
Phase 1          Phase 2              Phase 3            Phase 4
───────          ───────              ───────            ───────
Game Loop ──→ Character Ctrl (G52) → Enemies (G4)    → Particles (G23)
ECS       ──→ Physics/Collis (G3) ─→ Combat (G10)   → Game Feel (G30)
Input (G7)──→ Tilemap (G37)       → Level Design    → Parallax (G22)
Rendering ──→ Camera (G20)        → Save/Load (G10) → Transitions (G42)
            → Animation (G31)     → UI/HUD (G5)     → Audio (G6)
            → Side-Scroll (G56)
```

**Key docs:** [G52](../../monogame-arch/guides/G52_character_controller.md), [G56](../../monogame-arch/guides/G56_side_scrolling.md), [G3](../../monogame-arch/guides/G3_physics_and_collision.md), [C1](../game-design/C1_genre_reference.md), [C2](../game-design/C2_game_feel_and_genre_craft.md)

### ⚔️ Top-Down RPG

```
Phase 1          Phase 2              Phase 3             Phase 4
───────          ───────              ───────             ───────
Game Loop ──→ Top-Down Mvmt (G28) → NPCs/AI (G4)     → Cutscenes (G45)
ECS       ──→ Tilemap (G37)       → Dialogue (G62)   → Particles (G23)
Input (G7)──→ Camera (G20)        → Inventory (G10)  → Lighting (G39)
Rendering ──→ Animation (G31)     → Combat (G10)     → Weather (G57)
            → Collision (G3)      → Save/Load (G10)  → Minimap (G58)
                                  → Pathfinding (G40) → Audio (G6)
                                  → UI/HUD (G5)
```

**Key docs:** [G28](../../monogame-arch/guides/G28_top_down_perspective.md), [G62](../../monogame-arch/guides/G62_narrative_systems.md), [G40](../../monogame-arch/guides/G40_pathfinding.md), [C1](../game-design/C1_genre_reference.md)

### 🎲 Roguelike

```
Phase 1          Phase 2              Phase 3             Phase 4
───────          ───────              ───────             ───────
Game Loop ──→ Grid Movement      → Turn System (G10) → Minimap (G58)
ECS       ──→ ProcGen (G53)      → Combat (G10)     → Particles (G23)
Input (G7)──→ Tilemap (G37)      → Items (G10)      → Lighting (G39)
Rendering ──→ FOV/FoW (G54)      → AI/Enemies (G4)  → Audio (G6)
            → Camera (G20)       → UI/HUD (G5)      → Game Feel (G30)
                                 → Save/Load (G10)
```

**Key docs:** [G53](../../monogame-arch/guides/G53_procedural_generation.md), [G54](../../monogame-arch/guides/G54_fog_of_war.md), [G14](../../monogame-arch/guides/G14_data_structures.md), [C1](../game-design/C1_genre_reference.md)

### 🧩 Puzzle

```
Phase 1          Phase 2              Phase 3             Phase 4
───────          ───────              ───────             ───────
Game Loop ──→ Core Mechanic (G10)→ Progression (G10) → Tweening (G41)
ECS       ──→ Level Loader (G37) → Undo System (G10)→ Particles (G23)
Input (G7)──→ Rendering (G2)     → UI/HUD (G5)     → Audio (G6)
            → Animation (G31)    → Save/Load (G10)  → Transitions (G42)
            → Camera (G20)       → Tutorial (G61)   → Game Feel (G30)
```

**Key docs:** [E7](../game-design/E7_emergent_puzzle_design.md), [G10](../../monogame-arch/guides/G10_custom_game_systems.md), [C1](../game-design/C1_genre_reference.md)

### 🏰 Isometric

Follow the Top-Down RPG order, but swap in:
- [G49](../../monogame-arch/guides/G49_isometric.md) for perspective-specific rendering and coordinate math
- Isometric tilemap setup from [G37](../../monogame-arch/guides/G37_tilemap_systems.md) + [G49](../../monogame-arch/guides/G49_isometric.md)

---

## 6. System Interaction Patterns

### Pattern 1: Input → Gameplay Flow

```
┌───────────┐    ┌──────────────┐    ┌────────────────┐    ┌──────────┐
│ Raw Input │───→│ Input System │───→│ Action Mapping  │───→│ Movement │
│ (keyboard,│    │   (G7)       │    │ "Jump","Attack" │    │ System   │
│ gamepad)  │    └──────────────┘    └────────────────┘    └──────────┘
└───────────┘
```

```csharp
// Input system reads raw state, produces actions
public partial class InputSystem : BaseSystem<World, float>
{
    public override void Update(in float dt)
    {
        var actions = InputManager.GetActions(); // G7 action mapping
        
        world.Query(in moveQuery, (ref Velocity vel, ref PlayerInput input) =>
        {
            input.MoveDirection = actions.MoveAxis;
            input.JumpPressed = actions.IsPressed("Jump");
        });
    }
}

// Movement system consumes actions, applies physics
public partial class MovementSystem : BaseSystem<World, float>
{
    public override void Update(in float dt)
    {
        world.Query(in query, (ref Position pos, ref Velocity vel, 
                               ref PlayerInput input) =>
        {
            vel.X = input.MoveDirection.X * Speed;
            if (input.JumpPressed && grounded)
                vel.Y = -JumpForce;
        });
    }
}
```

### Pattern 2: ECS System Communication

Systems communicate through **components**, not direct references.

```csharp
// System A writes a component
world.Query(in damageQuery, (Entity entity, ref Health hp, ref DamageEvent dmg) =>
{
    hp.Current -= dmg.Amount;
    if (hp.Current <= 0)
        world.Add<DeathMarker>(entity);   // Signal for other systems
    world.Remove<DamageEvent>(entity);     // Consume the event
});

// System B reacts to that component
world.Query(in deathQuery, (Entity entity, ref DeathMarker death, 
                             ref Position pos) =>
{
    SpawnParticles(pos.Value);             // G23
    PlaySound("death");                    // G6
    world.Destroy(entity);
});
```

**Key rule:** Systems run in a defined order. Use `world.Add<T>` / `world.Remove<T>` as signals between frames.

### Pattern 3: Scene Lifecycle

```
┌────────────┐    ┌────────────┐    ┌────────────┐    ┌────────────┐
│   Enter    │───→│   Update   │───→│  Transition│───→│    Exit    │
│ LoadAssets │    │ GameLoop   │    │  Fade out  │    │ Unload     │
│ SpawnEnts  │    │ InputProc  │    │  (G42)     │    │ Cleanup    │
│ InitSystems│    │ Physics    │    └────────────┘    │ DestroyEnts│
└────────────┘    │ Render     │                      └────────────┘
                  └────────────┘
```

```csharp
public class GameplayScene : Scene
{
    public override void Enter()
    {
        Assets.Load("level_01");          // G26
        world = new World();              // Arch ECS
        SpawnPlayer(world);               // G43
        LoadTilemap(world, "level_01");   // G37
        InitSystems(world);               // G15
    }
    
    public override void Update(float dt)
    {
        systemGroup.Update(dt);           // Runs all ECS systems
    }
    
    public override void Exit()
    {
        world.Dispose();
        Assets.Unload("level_01");
    }
}
```

### Pattern 4: Render Pipeline Order

```
 Draw Order (back to front):
 ─────────────────────────────
 1. Clear screen
 2. Begin SpriteBatch (camera transform)
 3. Parallax backgrounds          (G22)
 4. Tilemap ground layers         (G37)
 5. Entities (sorted by Y or Z)   (G2)
 6. Tilemap foreground layers     (G37)
 7. Particles                     (G23)
 8. Lighting overlay              (G39)
 9. End SpriteBatch
10. Begin SpriteBatch (screen-space, no camera)
11. UI / HUD                      (G5)
12. Screen transitions            (G42)
13. Debug overlays                (G16)
14. End SpriteBatch
```

---

## 7. The "What Do I Need?" Checklist

### Player Systems

| I want... | Read these |
|-----------|-----------|
| Player movement (platformer) | [G52](../../monogame-arch/guides/G52_character_controller.md), [G3](../../monogame-arch/guides/G3_physics_and_collision.md), [G56](../../monogame-arch/guides/G56_side_scrolling.md) |
| Player movement (top-down) | [G28](../../monogame-arch/guides/G28_top_down_perspective.md), [G3](../../monogame-arch/guides/G3_physics_and_collision.md), [G52](../../monogame-arch/guides/G52_character_controller.md) |
| Combat system | [G10](../../monogame-arch/guides/G10_custom_game_systems.md), [G3](../../monogame-arch/guides/G3_physics_and_collision.md) (hitboxes), [G30](../../monogame-arch/guides/G30_game_feel_tooling.md) (juice) |
| Inventory / items | [G10](../../monogame-arch/guides/G10_custom_game_systems.md), [G5](../../monogame-arch/guides/G5_ui_framework.md) (UI), [G12](../../monogame-arch/guides/G12_design_patterns.md) |
| Health / damage | [G10](../../monogame-arch/guides/G10_custom_game_systems.md), [G5](../../monogame-arch/guides/G5_ui_framework.md) (health bar) |

### World Building

| I want... | Read these |
|-----------|-----------|
| Tiled levels | [G37](../../monogame-arch/guides/G37_tilemap_systems.md), [G8](../../monogame-arch/guides/G8_content_pipeline.md), [G3](../../monogame-arch/guides/G3_physics_and_collision.md) (tile collision) |
| Procedural levels | [G53](../../monogame-arch/guides/G53_procedural_generation.md), [G37](../../monogame-arch/guides/G37_tilemap_systems.md), [G14](../../monogame-arch/guides/G14_data_structures.md) |
| Isometric world | [G49](../../monogame-arch/guides/G49_isometric.md), [G37](../../monogame-arch/guides/G37_tilemap_systems.md), [G21](../../monogame-arch/guides/G21_coordinate_systems.md) |
| Parallax backgrounds | [G22](../../monogame-arch/guides/G22_parallax_depth_layers.md), [G20](../../monogame-arch/guides/G20_camera_systems.md) |
| Dynamic water | [G63](../../monogame-arch/guides/G63_water_simulation.md), [G27](../../monogame-arch/guides/G27_shaders_and_effects.md), [G3](../../monogame-arch/guides/G3_physics_and_collision.md) |
| Weather / atmosphere | [G57](../../monogame-arch/guides/G57_weather_effects.md), [G23](../../monogame-arch/guides/G23_particles.md), [G6](../../monogame-arch/guides/G6_audio.md) |
| Day/night cycle | [G39](../../monogame-arch/guides/G39_2d_lighting.md), [G27](../../monogame-arch/guides/G27_shaders_and_effects.md) |

### NPCs & AI

| I want... | Read these |
|-----------|-----------|
| Enemy AI | [G4](../../monogame-arch/guides/G4_ai_systems.md), [G40](../../monogame-arch/guides/G40_pathfinding.md), [G31](../../monogame-arch/guides/G31_animation_state_machines.md) |
| NPC dialogue | [G62](../../monogame-arch/guides/G62_narrative_systems.md), [G5](../../monogame-arch/guides/G5_ui_framework.md), [G6](../../monogame-arch/guides/G6_audio.md) |
| Quest system | [G10](../../monogame-arch/guides/G10_custom_game_systems.md), [G62](../../monogame-arch/guides/G62_narrative_systems.md) |
| Pathfinding | [G40](../../monogame-arch/guides/G40_pathfinding.md), [G37](../../monogame-arch/guides/G37_tilemap_systems.md), [G14](../../monogame-arch/guides/G14_data_structures.md) |

### Visuals

| I want... | Read these |
|-----------|-----------|
| Sprite animation | [G31](../../monogame-arch/guides/G31_animation_state_machines.md), [G2](../../monogame-arch/guides/G2_rendering_and_graphics.md) |
| Skeletal animation | [G59](../../monogame-arch/guides/G59_skeletal_animation.md), [G8](../../monogame-arch/guides/G8_content_pipeline.md) |
| Particle effects | [G23](../../monogame-arch/guides/G23_particles.md), [G2](../../monogame-arch/guides/G2_rendering_and_graphics.md) |
| Shaders / post-processing | [G27](../../monogame-arch/guides/G27_shaders_and_effects.md), [G2](../../monogame-arch/guides/G2_rendering_and_graphics.md) |
| 2D lighting | [G39](../../monogame-arch/guides/G39_2d_lighting.md), [G27](../../monogame-arch/guides/G27_shaders_and_effects.md) |
| Trails / motion lines | [G60](../../monogame-arch/guides/G60_trails_lines.md), [G2](../../monogame-arch/guides/G2_rendering_and_graphics.md) |
| Screen shake / hitstop | [G30](../../monogame-arch/guides/G30_game_feel_tooling.md), [G20](../../monogame-arch/guides/G20_camera_systems.md), [C2](../game-design/C2_game_feel_and_genre_craft.md) |

### UI & Menus

| I want... | Read these |
|-----------|-----------|
| Main menu | [G5](../../monogame-arch/guides/G5_ui_framework.md), [G38](../../monogame-arch/guides/G38_scene_management.md) |
| HUD / health bar | [G5](../../monogame-arch/guides/G5_ui_framework.md), [G10](../../monogame-arch/guides/G10_custom_game_systems.md) |
| Settings menu | [G55](../../monogame-arch/guides/G55_settings_menu.md), [G5](../../monogame-arch/guides/G5_ui_framework.md) |
| Minimap | [G58](../../monogame-arch/guides/G58_minimap.md), [G20](../../monogame-arch/guides/G20_camera_systems.md), [G37](../../monogame-arch/guides/G37_tilemap_systems.md) |
| Tutorial / onboarding | [G61](../../monogame-arch/guides/G61_tutorial_onboarding.md), [G5](../../monogame-arch/guides/G5_ui_framework.md) |

### Persistence & Meta

| I want... | Read these |
|-----------|-----------|
| Save / load | [G10](../../monogame-arch/guides/G10_custom_game_systems.md), [R1](../../monogame-arch/reference/R1_library_stack.md) (Arch.Persistence) |
| Achievements | [G47](../../monogame-arch/guides/G47_achievements.md), [G48](../../monogame-arch/guides/G48_online_services.md), [G36](../../monogame-arch/guides/G36_publishing_distribution.md) |
| Localization | [G34](../../monogame-arch/guides/G34_localization.md), [G5](../../monogame-arch/guides/G5_ui_framework.md) |
| Accessibility | [G35](../../monogame-arch/guides/G35_accessibility.md), [G7](../../monogame-arch/guides/G7_input_handling.md), [G5](../../monogame-arch/guides/G5_ui_framework.md) |
| Modding support | [G46](../../monogame-arch/guides/G46_modding_support.md), [G8](../../monogame-arch/guides/G8_content_pipeline.md) |

### Polish & Feel

| I want... | Read these |
|-----------|-----------|
| Screen transitions | [G42](../../monogame-arch/guides/G42_screen_transitions.md), [G38](../../monogame-arch/guides/G38_scene_management.md) |
| Tweening / easing | [G41](../../monogame-arch/guides/G41_tweening.md) |
| Cutscenes | [G45](../../monogame-arch/guides/G45_cutscenes.md), [G62](../../monogame-arch/guides/G62_narrative_systems.md), [G20](../../monogame-arch/guides/G20_camera_systems.md) |
| Game feel / juice | [G30](../../monogame-arch/guides/G30_game_feel_tooling.md), [C2](../game-design/C2_game_feel_and_genre_craft.md), [G41](../../monogame-arch/guides/G41_tweening.md) |
| Fog of war | [G54](../../monogame-arch/guides/G54_fog_of_war.md), [G37](../../monogame-arch/guides/G37_tilemap_systems.md), [G39](../../monogame-arch/guides/G39_2d_lighting.md) |

### Shipping

| I want... | Read these |
|-----------|-----------|
| Build for multiple platforms | [G32](../../monogame-arch/guides/G32_deployment_platform_builds.md), [R3](../../monogame-arch/reference/R3_project_structure.md) |
| Publish on Steam | [G36](../../monogame-arch/guides/G36_publishing_distribution.md), [G47](../../monogame-arch/guides/G47_achievements.md), [R1](../../monogame-arch/reference/R1_library_stack.md) |
| Profile / optimize | [G33](../../monogame-arch/guides/G33_profiling_optimization.md), [G13](../../monogame-arch/guides/G13_csharp_performance.md) |
| Debug tools | [G16](../../monogame-arch/guides/G16_debugging.md), [G29](../../monogame-arch/guides/G29_game_editor.md) |
| Crash reporting | [G51](../../monogame-arch/guides/G51_crash_reporting.md), [G32](../../monogame-arch/guides/G32_deployment_platform_builds.md) |
| Automated testing | [G17](../../monogame-arch/guides/G17_testing.md), [G11](../../monogame-arch/guides/G11_programming_principles.md) |
| Networking / multiplayer | [G9](../../monogame-arch/guides/G9_networking.md), [G48](../../monogame-arch/guides/G48_online_services.md) |

---

## 8. Cross-Cutting Concerns

These systems touch almost everything. Integrate them early, keep them decoupled.

### Input (G7) — Touches Everything Interactive

```
                    ┌── UI menus (G5)
                    ├── Player movement (G52)
 Input Manager ─────├── Dialogue choices (G62)
    (G7)            ├── Editor controls (G29)
                    ├── Camera zoom/pan (G20)
                    └── Debug console (G16)
```

**Integration pattern:** Use an **action mapping layer** between raw input and consumers. Systems query actions ("Jump", "Interact"), never raw keys. This lets you remap controls, support gamepad+keyboard, and add accessibility without touching gameplay code.

### Rendering (G2) — Everything Visible

**Integration pattern:** All drawable entities have `Position` + `Sprite` components. A single `RenderSystem` queries them, sorts by layer/Y, draws them. Individual systems never call `SpriteBatch.Draw` directly — they set component values, the render system reads them.

**Layers:** Use an enum or int for draw order. Let tilemaps, entities, particles, and UI each claim a range.

### Audio (G6) — Ambient Everywhere

**Integration pattern:** Create an `AudioEvent` component or use a simple event bus. When combat deals damage, it raises "hit_sound". When a door opens, "door_open". The audio system listens and plays. No system imports the audio library directly.

### Debugging (G16) — Dev-Only Overlay

**Integration pattern:** Compile debug overlays behind `#if DEBUG` or a runtime toggle. Common overlays:
- Collision boxes (G3)
- AI state labels (G4)
- Pathfinding lines (G40)
- FPS counter (G33)
- Entity inspector (ECS)

Use ImGui.NET for in-game debug tools. Wrap it in a `DebugSystem` that draws after everything else.

### Profiling (G33) — Know Your Bottlenecks

**Integration pattern:** Wrap system updates in timing blocks. Track per-system milliseconds. Display in debug overlay. Profile early, optimize late.

```csharp
// Simple per-system profiling
var sw = Stopwatch.StartNew();
movementSystem.Update(dt);
DebugStats.Record("Movement", sw.Elapsed);
```

---

## Quick Navigation

| I'm starting from scratch | → [Phase 1](#phase-1--foundation-week-12) |
|---------------------------|-------------------------------------------|
| I have a game loop, now what? | → [Phase 2](#phase-2--core-gameplay-week-34) |
| I need a specific feature | → [What Do I Need?](#7-the-what-do-i-need-checklist) |
| I'm making a platformer | → [Platformer build order](#-platformer) |
| I'm making an RPG | → [RPG build order](#️-top-down-rpg) |
| I'm making a roguelike | → [Roguelike build order](#-roguelike) |
| I want to understand the architecture | → [E1](../../monogame-arch/architecture/E1_architecture_overview.md) |
| I want to know what libraries to use | → [R1](../../monogame-arch/reference/R1_library_stack.md), [R2](../../monogame-arch/reference/R2_capability_matrix.md) |
| I want the full doc index | → [INDEX](../INDEX.md) |

---

*This is the map. The 76+ docs are the territory. Pick your genre, find your phase, read the relevant guides, build.*
