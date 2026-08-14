# R2 — Capability Matrix


> **Category:** Reference · **Related:** [R1 Library Stack](./R1_library_stack.md) · [E1 Architecture Overview](../architecture/E1_architecture_overview.md)

---

Quick lookup: what provides what.

| Capability | Provider | Notes | Guide |
|-----------|---------|-------|-------|
| **Core Architecture** | | | |
| Scene management | Custom code (~150 lines) | Update/Draw loop, transitions | [G1](../guides/G1_custom_code_recipes.md) |
| Entity system (all entities) | Arch ECS v2.1.0 | Mass AND unique entities | [E1](../architecture/E1_architecture_overview.md) |
| ECS source generators | Arch.System.SourceGenerator | Eliminates system boilerplate | |
| Entity relationships | Arch.Relationships | Party members, squads, parent-child | |
| Event bus | Arch.EventBus | Typed publish/subscribe | |
| **Rendering** | | | |
| 2D rendering pipeline | MonoGame SpriteBatch + custom layers | Render layers, depth sorting | [G2](../guides/G2_rendering_and_graphics.md) |
| Post-processing | Custom HLSL shaders | Bloom, blur, vignette, CRT, scanlines, heat haze, flash white, screen flash, shockwave | [G2](../guides/G2_rendering_and_graphics.md), [G27](../guides/G27_shaders_and_effects.md) |
| Deferred 2D lighting | Custom or Penumbra | Normal maps, point/spot lights | [G2](../guides/G2_rendering_and_graphics.md) |
| Custom shaders | MonoGame HLSL via MGFXC | .fx files cross-compiled to GLSL | [G2](../guides/G2_rendering_and_graphics.md), [G27](../guides/G27_shaders_and_effects.md) |
| Elemental shader effects | Custom HLSL | Fire, water, wind, earth, lightning, ice | [G27](../guides/G27_shaders_and_effects.md) |
| Sprite animation | MonoGame.Aseprite v6.3.1 | Direct .ase/.aseprite import | [G8](../guides/G8_content_pipeline.md) |
| Sprite animation (alternative) | MonoGame.Extended | SpriteSheet + AnimatedSprite | [G8](../guides/G8_content_pipeline.md) |
| Parallax scrolling | MonoGame.Extended | Multi-layer parallax | |
| Nine-patch sprites | MonoGame.Extended or MLEM | Scalable UI panels | |
| Line renderer | Custom (~50 lines) | Smooth or jagged edges | [G1](../guides/G1_custom_code_recipes.md) |
| Primitives drawing | MonoGame.Extended | Rectangles, circles, lines, polygons | |
| Screen transitions | Custom (~100 lines) | Fade, wipe, pixelate, circle-in/out | [G1](../guides/G1_custom_code_recipes.md) |
| **Collision & Physics** | | | |
| Collision broadphase | Custom SpatialHash (~80 lines) | Fast proximity queries, raycasts | [G3](../guides/G3_physics_and_collision.md) |
| Collision shapes | MonoGame.Extended + custom | AABB, circle, polygon (SAT) | [G3](../guides/G3_physics_and_collision.md) |
| Full physics simulation | Aether.Physics2D v2.2.0 | Rigid bodies, joints, raycasting, CCD | [G3](../guides/G3_physics_and_collision.md) |
| Rope/cloth/soft body | Custom Verlet (~150 lines) | Position-based constraints | [G3](../guides/G3_physics_and_collision.md) |
| **AI** | | | |
| FSM | BrainAI | Simple state + transition | [G4](../guides/G4_ai_systems.md) |
| Behavior Trees | BrainAI | Selector, sequence, decorator, leaf | [G4](../guides/G4_ai_systems.md) |
| GOAP | BrainAI | Precondition/effect action planning | [G4](../guides/G4_ai_systems.md) |
| Utility AI | BrainAI | Score-based action selection | [G4](../guides/G4_ai_systems.md) |
| Pathfinding (A*, BFS, Dijkstra) | BrainAI or Roy-T.AStar | Grid or custom graph | [G4](../guides/G4_ai_systems.md) |
| Influence Maps | BrainAI | Spatial scoring | [G4](../guides/G4_ai_systems.md) |
| **UI** | | | |
| UI framework | Gum.MonoGame | Forms, visual editor, anchor layout | [G5](../guides/G5_ui_framework.md) |
| Runtime fonts | FontStashSharp v1.3.7 | .ttf/.otf at any size, glyph atlases | |
| **Input** | | | |
| Input handling | Apos.Input v2.4.2 | Keyboard, mouse, gamepad, touch | [G7](../guides/G7_input_handling.md) |
| **Audio** | | | |
| Audio (basic) | MonoGame built-in | SoundEffect + Song | [G6](../guides/G6_audio.md) |
| Audio (advanced) | FMOD via FmodForFoxes | DSP, buses, beat sync, 3D spatial | [G6](../guides/G6_audio.md) |
| **Infrastructure** | | | |
| Tweening | Custom (~100 lines) | Any numeric property, easing curves | [G1](../guides/G1_custom_code_recipes.md) |
| Coroutines | Ellpeck/Coroutine | Unity-style yield | |
| Object pooling | Custom (~30 lines) | Generic Pool\<T> | [G1](../guides/G1_custom_code_recipes.md) |
| Debug console + overlays | ImGui.NET | Entity inspectors, perf graphs | |
| Serialization | System.Text.Json (built-in .NET) | JSON, AOT-compatible with source gen | |
| ECS serialization | Arch.Persistence | Save/load entire ECS worlds | |
| Networking | LiteNetLib | Reliable UDP, NAT traversal | [G9](../guides/G9_networking.md) |
| Procedural generation | Custom C# | BSP, cellular automata, WFC, noise | [G10](../guides/G10_custom_game_systems.md) |
| Content pipeline | MGCB + Extended importers | Tiled, atlas, Aseprite, fonts | [G8](../guides/G8_content_pipeline.md) |
| Tilemap loading | MonoGame.Extended | Tiled .tmx (ortho + iso) | [G8](../guides/G8_content_pipeline.md) |
| Cross-platform | MonoGame | Windows, Mac, Linux, iOS, Android | |
