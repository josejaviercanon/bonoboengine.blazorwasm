# Code Examples

Working C# examples extracted from the guides. All use `record struct` ECS components with Arch v2.1.0. Copy, adapt, ship.

---

## Core Engine

Scene management, service locator, and the glue that holds everything together.

| File | What it does | Guide |
|------|-------------|-------|
| [`Scene.cs`](Core/Scene.cs) | Base scene class with lifecycle hooks | [G38](../G/G38_scene_management.md) |
| [`SceneManager.cs`](Core/SceneManager.cs) | Push/pop scene stack with transitions | [G38](../G/G38_scene_management.md) |
| [`GameServices.cs`](Core/GameServices.cs) | Service locator for ambient services | [G1](../G/G1_custom_code_recipes.md) |

??? example "SceneManager.cs"
    ```csharp title="Core/SceneManager.cs"
    --8<-- "examples/Core/SceneManager.cs"
    ```

---

## Character Controller

Kinematic platformer controller with coyote time, jump buffering, wall mechanics, and slope handling.

| File | What it does | Guide |
|------|-------------|-------|
| [`PlayerComponents.cs`](Character/PlayerComponents.cs) | ECS components for player state | [G52](../G/G52_character_controller.md) |
| [`PlayerControllerSystem.cs`](Character/PlayerControllerSystem.cs) | Full platformer controller system | [G52](../G/G52_character_controller.md) |
| [`CollisionResolver.cs`](Character/CollisionResolver.cs) | AABB collision resolution with slopes | [G3](../G/G3_physics_and_collision.md) |

??? example "PlayerComponents.cs"
    ```csharp title="Character/PlayerComponents.cs"
    --8<-- "examples/Character/PlayerComponents.cs"
    ```

---

## Tilemap

Tile rendering, autotiling with bitmask lookup, and tile-based collision detection.

| File | What it does | Guide |
|------|-------------|-------|
| [`TilemapRenderer.cs`](Tilemap/TilemapRenderer.cs) | Chunked tilemap rendering with culling | [G37](../G/G37_tilemap_systems.md) |
| [`AutoTiler.cs`](Tilemap/AutoTiler.cs) | 4-bit and 8-bit bitmask autotiling | [G37](../G/G37_tilemap_systems.md) |
| [`TileCollision.cs`](Tilemap/TileCollision.cs) | Tile-based AABB collision queries | [G37](../G/G37_tilemap_systems.md) |

??? example "AutoTiler.cs"
    ```csharp title="Tilemap/AutoTiler.cs"
    --8<-- "examples/Tilemap/AutoTiler.cs"
    ```

---

## Pathfinding

A* grid pathfinding and flow fields for mass unit movement.

| File | What it does | Guide |
|------|-------------|-------|
| [`AStarPathfinder.cs`](Pathfinding/AStarPathfinder.cs) | A* with heuristics (Manhattan, Octile, Euclidean) | [G40](../G/G40_pathfinding.md) |
| [`FlowField.cs`](Pathfinding/FlowField.cs) | Flow field for steering many units to one target | [G40](../G/G40_pathfinding.md) |
| [`PathComponents.cs`](Pathfinding/PathComponents.cs) | ECS components for path requests and results | [G40](../G/G40_pathfinding.md) |

??? example "AStarPathfinder.cs"
    ```csharp title="Pathfinding/AStarPathfinder.cs"
    --8<-- "examples/Pathfinding/AStarPathfinder.cs"
    ```

---

## Lighting

Deferred 2D lightmap rendering with point lights and shadow casting.

| File | What it does | Guide |
|------|-------------|-------|
| [`LightComponents.cs`](Lighting/LightComponents.cs) | Light and occluder ECS components | [G39](../G/G39_2d_lighting.md) |
| [`LightRenderer.cs`](Lighting/LightRenderer.cs) | Lightmap render target with additive blending | [G39](../G/G39_2d_lighting.md) |

??? example "LightRenderer.cs"
    ```csharp title="Lighting/LightRenderer.cs"
    --8<-- "examples/Lighting/LightRenderer.cs"
    ```

---

## Procedural Generation

BSP dungeon generation, cellular automata caves, and seeded randomness.

| File | What it does | Guide |
|------|-------------|-------|
| [`BSPDungeon.cs`](Procgen/BSPDungeon.cs) | Binary Space Partition dungeon rooms + corridors | [G53](../G/G53_procedural_generation.md) |
| [`CellularAutomata.cs`](Procgen/CellularAutomata.cs) | Cave generation with cellular automata rules | [G53](../G/G53_procedural_generation.md) |
| [`SeededRandom.cs`](Procgen/SeededRandom.cs) | Deterministic seeded random for reproducible worlds | [G53](../G/G53_procedural_generation.md) |

??? example "BSPDungeon.cs"
    ```csharp title="Procgen/BSPDungeon.cs"
    --8<-- "examples/Procgen/BSPDungeon.cs"
    ```

---

## Fog of War

Recursive shadowcasting visibility and fog rendering.

| File | What it does | Guide |
|------|-------------|-------|
| [`Shadowcaster.cs`](FogOfWar/Shadowcaster.cs) | Recursive shadowcasting algorithm (8 octants) | [G54](../G/G54_fog_of_war.md) |
| [`FogGrid.cs`](FogOfWar/FogGrid.cs) | Fog state grid (hidden/explored/visible) | [G54](../G/G54_fog_of_war.md) |
| [`FogRenderer.cs`](FogOfWar/FogRenderer.cs) | Fog overlay rendering with smooth transitions | [G54](../G/G54_fog_of_war.md) |

??? example "Shadowcaster.cs"
    ```csharp title="FogOfWar/Shadowcaster.cs"
    --8<-- "examples/FogOfWar/Shadowcaster.cs"
    ```

---

## Tweening & Easing

31 easing curves and a pooled tween engine.

| File | What it does | Guide |
|------|-------------|-------|
| [`Easing.cs`](Tween/Easing.cs) | All 31 standard easing functions | [G41](../G/G41_tweening.md) |
| [`TweenManager.cs`](Tween/TweenManager.cs) | Pooled tween engine with sequences | [G41](../G/G41_tweening.md) |

??? example "Easing.cs"
    ```csharp title="Tween/Easing.cs"
    --8<-- "examples/Tween/Easing.cs"
    ```

---

## Effects

Trail rendering, rope physics simulation, and visual juice.

| File | What it does | Guide |
|------|-------------|-------|
| [`TrailRenderer.cs`](Effects/TrailRenderer.cs) | Triangle strip trail with fading and tapering | [G60](../G/G60_trails_lines.md) |
| [`RopeSimulation.cs`](Effects/RopeSimulation.cs) | Verlet integration rope/chain physics | [G60](../G/G60_trails_lines.md) |

??? example "TrailRenderer.cs"
    ```csharp title="Effects/TrailRenderer.cs"
    --8<-- "examples/Effects/TrailRenderer.cs"
    ```

---

## Prefabs & Blueprints

Data-driven entity templates with JSON blueprints.

| File | What it does | Guide |
|------|-------------|-------|
| [`Blueprint.cs`](Prefabs/Blueprint.cs) | Blueprint data model with inheritance | [G43](../G/G43_entity_prefabs.md) |
| [`EntityFactory.cs`](Prefabs/EntityFactory.cs) | Factory that spawns entities from blueprints | [G43](../G/G43_entity_prefabs.md) |

---

## Transitions

Screen transition effects between scenes.

| File | What it does | Guide |
|------|-------------|-------|
| [`Transition.cs`](Transitions/Transition.cs) | Base transition with fade, wipe, iris patterns | [G42](../G/G42_screen_transitions.md) |

---

!!! tip "Support the toolkit"
    Everything here is free and open. If these examples saved you time, consider a [$1 tip on GitHub Sponsors](https://github.com/sponsors/sbenson2) — it keeps the docs growing.
