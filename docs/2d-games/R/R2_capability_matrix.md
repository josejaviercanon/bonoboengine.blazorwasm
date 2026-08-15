# R2 — Capability Matrix
> **Category:** Reference · **Related:** [R1 Library Stack](./R1_library_stack.md) · [E1 Architecture Overview](../E/E1_architecture_overview.md)

---

Quick lookup: what provides what.

| Capability | Provider | Notes | Guide |
|-----------|---------|-------|-------|
| **Core Architecture** | | | |
| Scene management | Custom code (~150 lines) | Simulation tick pump + transitions | [G1](../G/G1_custom_code_recipes.md) |
| Entity system (all entities) | Arch ECS (vendored source) | Mass AND unique entities | [E1](../E/E1_architecture_overview.md) |
| ECS source generators | Arch.System.SourceGenerator (via `Arch.Generators`) | Eliminates system boilerplate | |
| Entity relationships | Arch.Relationships (vendored) | Party members, squads, parent-child | |
| Event bus | Arch.EventBus (vendored) | Typed publish/subscribe | |
| **Rendering (PixiJS, client-side)** | | | |
| 2D rendering pipeline | PixiJS v8 (Container/Sprite batching) + custom layers | Render layers, depth sorting | [G2](../G/G2_rendering_and_graphics.md) |
| Post-processing | PixiJS GLSL filters (WebGL) | Bloom, blur, vignette, CRT, scanlines, heat haze, flash white, screen flash, shockwave | [G2](../G/G2_rendering_and_graphics.md), [G27](../G/G27_shaders_and_effects.md) |
| Deferred 2D lighting | Custom PixiJS filters | Normal maps, point/spot lights | [G2](../G/G2_rendering_and_graphics.md) |
| Custom shaders | PixiJS GLSL filters | GLSL fragment/vertex shaders | [G2](../G/G2_rendering_and_graphics.md), [G27](../G/G27_shaders_and_effects.md) |
| Elemental shader effects | Custom GLSL (PixiJS filters) | Fire, water, wind, earth, lightning, ice | [G27](../G/G27_shaders_and_effects.md) |
| Sprite animation | Aseprite → PixiJS spritesheet (Vite) | Direct .ase/.aseprite import | [G8](../G/G8_content_pipeline.md) |
| Sprite animation (alternative) | Custom PixiJS AnimatedSprite | Spritesheet + frames | [G8](../G/G8_content_pipeline.md) |
| Parallax scrolling | PixiJS layered Containers | Multi-layer parallax | |
| Nine-patch sprites | PixiJS NineSlicePlane / Tailwind | Scalable UI panels | |
| Line renderer | Custom (~50 lines, PixiJS Graphics) | Smooth or jagged edges | [G1](../G/G1_custom_code_recipes.md) |
| Primitives drawing | PixiJS Graphics | Rectangles, circles, lines, polygons | |
| Screen transitions | Custom (~100 lines) | Fade, wipe, pixelate, circle-in/out | [G1](../G/G1_custom_code_recipes.md) |
| **Collision & Physics (C#, simulation-side)** | | | |
| Collision broadphase | Custom SpatialHash (~80 lines) | Fast proximity queries, raycasts | [G3](../G/G3_physics_and_collision.md) |
| Collision shapes | Custom (System.Numerics) + custom | AABB, circle, polygon (SAT) | [G3](../G/G3_physics_and_collision.md) |
| Full physics simulation | Custom C# physics (optional future lib) | Rigid bodies, joints, raycasting, CCD | [G3](../G/G3_physics_and_collision.md) |
| Rope/cloth/soft body | Custom Verlet (~150 lines) | Position-based constraints | [G3](../G/G3_physics_and_collision.md) |
| **AI (C#, simulation-side)** | | | |
| FSM | BrainAI (optional, vendor as source) | Simple state + transition | [G4](../G/G4_ai_systems.md) |
| Behavior Trees | BrainAI (optional) | Selector, sequence, decorator, leaf | [G4](../G/G4_ai_systems.md) |
| GOAP | BrainAI (optional) | Precondition/effect action planning | [G4](../G/G4_ai_systems.md) |
| Utility AI | BrainAI (optional) | Score-based action selection | [G4](../G/G4_ai_systems.md) |
| Pathfinding (A*, BFS, Dijkstra) | BrainAI or Roy-T.AStar (optional) | Grid or custom graph | [G4](../G/G4_ai_systems.md) |
| Influence Maps | BrainAI (optional) | Spatial scoring | [G4](../G/G4_ai_systems.md) |
| **UI (Blazor + Tailwind)** | | | |
| UI framework | Blazor components + Tailwind CSS v4 | Data-bound HUD, menus, inventories | [G5](../G/G5_ui_framework.md) |
| Runtime fonts | PixiJS Text + web fonts | .ttf/.otf via CSS / PixiJS | |
| **Input** | | | |
| Input handling | Blazor/DOM events → commands | Keyboard, mouse, gamepad, touch | [G7](../G/G7_input_handling.md) |
| **Audio (client-side)** | | | |
| Audio (basic) | Web Audio (PixiJS sound / browser) | Sound effects, music | [G6](../G/G6_audio.md) |
| Audio (advanced) | Web Audio API (custom) | DSP, buses, beat sync, spatial | [G6](../G/G6_audio.md) |
| **Infrastructure** | | | |
| Tweening | Custom (~100 lines) | Any numeric property, easing curves | [G1](../G/G1_custom_code_recipes.md) |
| Coroutines | Ellpeck/Coroutine (optional) | Unity-style yield | |
| Object pooling | Custom (~30 lines) | Generic Pool\<T> | [G1](../G/G1_custom_code_recipes.md) |
| Debug console + overlays | Browser DevTools / Blazor | Entity inspectors, perf graphs | |
| Serialization | System.Text.Json (built-in .NET) | JSON, AOT-compatible with source gen | |
| ECS serialization | Arch.Persistence (vendored) | Save/load entire ECS worlds | |
| Networking (future) | ASP.NET Core SignalR / WebSockets | Authoritative server, same engine | [G9](../G/G9_networking.md) |
| Procedural generation | Custom C# | BSP, cellular automata, WFC, noise | [G10](../G/G10_custom_game_systems.md) |
| Asset pipeline | Vite + PixiJS Assets | Bundles sprites/audio/data → `wwwroot/dist` | [G8](../G/G8_content_pipeline.md) |
| Tilemap loading | Tiled .tmx → PixiJS loader (custom/TSJ) | Ortho + iso | [G8](../G/G8_content_pipeline.md) |
| Cross-platform | .NET 10 (Blazor Web + MAUI Hybrid) | Web, Android, iOS, MacCatalyst, Windows | |
