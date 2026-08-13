# E2 — Why Nez Was Dropped
> **Category:** Explanation · **Related:** [E1 Architecture Overview](./E1_architecture_overview.md) · [E8 MonoGameStudio Post-Mortem](./E8_monogamestudio_postmortem.md) · [R1 Library Stack](../R/R1_library_stack.md)

---

## What Nez Was

[Nez](https://github.com/prime31/Nez) was a MonoGame framework created by prime31 that aimed to be a batteries-included solution for 2D game development. It wrapped MonoGame with a comprehensive feature set:

| Feature Area | What Nez Provided |
|---|---|
| **Scene Management** | `Scene` class with entity lists, content scope, transitions |
| **Entity-Component System** | Custom EC model — entities with updatable/renderable components |
| **Physics & Collision** | Built-in broadphase (SpatialHash), collision shapes, raycasting |
| **Rendering** | Render layers, post-processors, screen-space effects, sprite batching |
| **UI** | IMGUI-style in-game UI system, tables, buttons, sliders |
| **Sprites** | Sprite atlas support, Aseprite import, sprite animations |
| **Tweening** | Property tweening with easing functions |
| **Misc** | Timers, coroutines, debug console, AI (FSM, BT, GOAP, utility), pathfinding |

For a solo developer, this looked like a dream — one NuGet package (well, one git submodule) and you had everything. The early prototyping experience was genuinely fast. The problems only became clear at scale.

---

## Why It Was Dropped

### 1. Monolithic dependency, single maintainer

Nez was maintained primarily by one person (prime31). When updates slowed, **everything** froze — physics, rendering, ECS, UI, all of it. You couldn't update the collision system without pulling in changes to the scene manager. You couldn't fix a rendering bug without recompiling the entire framework.

This is the fundamental risk of a monolith: when it stops moving, you stop moving. The toolkit's core philosophy ([E1](./E1_architecture_overview.md)) exists specifically because of this lesson.

### 2. Nez's ECS wasn't actually an ECS

Nez used an Entity-Component model, not a true Entity-Component-System architecture. The difference matters:

| | Nez EC | Arch ECS |
|---|---|---|
| **Data layout** | Components on heap-allocated entities | Struct components in cache-friendly archetypes |
| **Iteration** | Virtual dispatch per component per frame | Bulk query over contiguous memory |
| **Systems** | Logic lives inside components (`Update()`, `Render()`) | Logic lives in systems that query components |
| **Scaling** | Degrades with entity count (GC pressure, cache misses) | Designed for thousands of entities (bullets, particles, swarms) |
| **Testability** | Components coupled to entity lifecycle | Systems are pure functions over data |

With Nez, a `PlayerMovementComponent` would override `Update()` and directly mutate the entity. With Arch, a `PlayerMovementSystem` queries for `PlayerTag + Position + Velocity` and processes them in a tight loop. The Arch approach is faster, more testable, and composes better.

For a Vampire Survivors-style game with hundreds of entities, Nez's component model would choke. Arch handles it without breaking a sweat.

### 3. Tightly coupled systems

Nez's systems were deeply intertwined:

- The renderer assumed Nez's scene and entity model
- Physics was coupled to Nez's component lifecycle
- Post-processors assumed Nez's render pipeline
- The UI system assumed Nez's input handling

Want to use a different physics engine? You'd fight Nez's collision system the whole way. Want a proper UI framework like Gum? You'd have to bypass Nez's rendering to make it work. Every "replacement" became a battle against the framework rather than a clean swap.

### 4. Update and maintenance concerns

- **Not on NuGet** — Nez required a git submodule, which meant managing source compilation, version pinning, and merge conflicts when pulling updates
- **MonoGame version lag** — Nez sometimes trailed behind MonoGame releases, blocking adoption of new features and .NET versions
- **No .NET 10 path** — Modern .NET compatibility required framework-level changes that weren't guaranteed

### 5. Architecture decisions made for you

Nez had opinions about how your game should be structured:

- One `Scene` active at a time (limiting for overlays, pause menus, picture-in-picture)
- Component lifecycle tied to entity add/remove (no deferred processing)
- Rendering order controlled by Nez's layer system (less flexible than a custom approach)
- Input handling baked in (couldn't cleanly substitute Apos.Input or other solutions)

These weren't necessarily bad decisions, but they were **someone else's decisions**. When your game needed something different, you were fighting the framework.

---

## Feature-by-Feature Replacement Map

Every feature Nez provided has a replacement in the composed stack — most are better than the original.

| Nez Feature | Replacement | Notes |
|---|---|---|
| Entity-Component model | **Arch ECS** (v2.1.0) | True ECS with archetypes, cache-friendly, handles thousands of entities |
| Scene management | **Custom** (~150 lines) | Scene manager with transitions — [G1](../G/G1_custom_code_recipes.md) |
| Physics / collision | **Aether.Physics2D** (v2.2.0) | Full Box2D-style physics — [G3](../G/G3_physics_and_collision.md) |
| SpatialHash broadphase | **Custom** (~80 lines) | Simple, no dependency — [G1](../G/G1_custom_code_recipes.md) |
| Collision shapes | **MonoGame.Extended** (v5.3.1) | AABB, circle, polygon + custom shapes (~150 lines) |
| Render layers | **Custom** (~200 lines) | Full control over sort order and camera assignment |
| Post-processors | **Custom** (~150 lines) | RenderTarget2D chain, your effects — [G2](../G/G2_rendering_and_graphics.md) |
| Sprite rendering | **MonoGame.Aseprite** (v6.3.1) | Direct .aseprite import, better workflow — [G8](../G/G8_content_pipeline.md) |
| Sprite atlas | **MonoGame.Extended** or custom | Atlas packing, texture regions |
| UI system | **Gum.MonoGame** | Visual editor, forms controls, official MonoGame recommendation — [G5](../G/G5_ui_framework.md) |
| Tweening | **Custom** (~100 lines) | Property tweens with easing — [G1](../G/G1_custom_code_recipes.md) |
| Screen transitions | **Custom** (~100 lines) | Fade, slide, etc. — [G1](../G/G1_custom_code_recipes.md) |
| Timers | **Custom** or Coroutine (Ellpeck) | Unity-style coroutines for sequential logic |
| Debug console | **ImGui.NET** | Industry-standard debug tooling |
| AI (FSM, BT, GOAP) | **BrainAI** | Same feature set, standalone library — [G4](../G/G4_ai_systems.md) |
| Pathfinding | **BrainAI** | A*, breadth-first, Dijkstra |
| Input handling | **Apos.Input** (v2.5.0) | JustPressed tracking, multi-device — [G7](../G/G7_input_handling.md) |
| Fonts | **FontStashSharp.MonoGame** (v1.3.7) | Runtime .ttf/.otf at any size |
| Camera | **MonoGame.Extended** | Camera2D with viewport handling — [G20](../G/G20_camera_systems.md) |

The total custom code budget is ~1,000 lines ([E1](./E1_architecture_overview.md)) — about 14.5 hours of implementation. That's less time than you'd spend fighting Nez's architecture for a single non-standard feature.

---

## Migration Path: Nez → Composed Stack

If you have an existing Nez project and want to migrate, here's the practical approach. **Don't try to migrate everything at once** — do it system by system.

### Phase 1: ECS Migration (highest impact)

1. **Install Arch ECS** alongside Nez (they can coexist temporarily)
2. **Move data to Arch components** — convert Nez `Component` subclasses into Arch struct components. Strip the `Update()` logic out.
3. **Create Arch systems** — the logic that was in `Component.Update()` becomes queries in dedicated systems
4. **Migrate entity creation** — replace `scene.CreateEntity()` with `world.Create()` calls
5. **Remove Nez entities** once all logic is in Arch systems

### Phase 2: Rendering

1. **Replace Nez's renderer** with custom render layers (~200 lines) that draw Arch entities
2. **Replace post-processors** with a custom RenderTarget2D chain (~150 lines)
3. **Switch sprites** to MonoGame.Aseprite for .aseprite files

### Phase 3: Systems

1. **Physics** → Aether.Physics2D (sync positions between Arch components and Aether bodies)
2. **UI** → Gum.MonoGame (this is the biggest change — Gum has a completely different model)
3. **Input** → Apos.Input (straightforward swap)
4. **AI** → BrainAI (API is similar to Nez's AI, since BrainAI is partially derived from it)

### Phase 4: Remove Nez

1. **Replace scene manager** with custom implementation (~150 lines)
2. **Replace tweens, timers** with custom code (~100 lines each)
3. **Remove the Nez git submodule**
4. **Celebrate** — you now own your entire architecture

### Estimated effort

For a small-to-medium game: **2–4 weeks** of focused work, depending on how deeply Nez is embedded. The ECS migration is the hardest part; everything else is mostly mechanical replacement.

---

## Lessons Learned: Framework vs Library Composition

### The framework trap

A framework gives you speed at the start and takes it away at the end. Early on, everything is free — scene management, physics, rendering, all wired up. But the moment you need something the framework doesn't support, you're either:

1. **Working around it** — hacking in features the framework wasn't designed for
2. **Forking it** — now you maintain a framework
3. **Rebuilding it** — accepting the sunk cost and starting over

With Nez, all three happened across different features.

### The composition advantage

A composed library stack has a higher initial cost (you write ~1,000 lines of glue code) but a dramatically lower ongoing cost:

- **Library dies?** Swap it. MonoGame.Extended stops updating? Write a camera in 200 lines. Gum gets abandoned? Switch to Myra or build a simple UI.
- **Need something different?** Add it. No framework to fight — your glue code is yours to modify.
- **Want to upgrade .NET?** Each library moves independently. You're not waiting for one maintainer to update the whole stack.

### The real cost comparison

| | Nez | Composed Stack |
|---|---|---|
| **Day 1 cost** | Near zero (everything included) | ~14.5 hours (write glue code) |
| **Month 6 cost** | Rising (fighting framework limits) | Stable (each piece works independently) |
| **When something breaks** | Wait for maintainer or fork the whole framework | Replace one library |
| **When you need something unusual** | Fight the framework | Write it / add a library |
| **Long-term risk** | Total dependency on one project | Distributed across many projects + your code |

**14.5 hours of upfront work buys you permanent architectural independence.** That's the trade, and it's overwhelmingly worth it.

### The broader principle

This isn't just about Nez — it applies to any monolith framework for any domain. The question is always: **do you want to rent someone else's architecture, or own yours?**

For a solo developer building a game they expect to work on for months or years, ownership wins every time. The composed stack described in [E1](./E1_architecture_overview.md) is the result of learning this lesson the hard way.
