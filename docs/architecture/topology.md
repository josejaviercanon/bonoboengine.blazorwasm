# Architecture Topology — C# ECS Engine in .NET MAUI Hybrid / Blazor WebAssembly

> Detailed companion to `docs/index.md` (the architecture source of truth). Decisions live in `docs/adr/` (ADR-001 … ADR-006); verified facts in `docs/ai-agents/codebase-truth.md`. This file marks **Implemented** vs **Target** explicitly. When code and prose disagree, verified files win.

## The Dual-Runtime State Machine

Integrating PixiJS v8 into .NET MAUI Hybrid / Blazor WASM shifts execution into a hybrid dual-runtime:

1. **C# WASM / Mono Runtime Layer** — pure game logic, ECS entity lifecycle, system updates, spatial partitioning, state machines, physics.
2. **JS / WebGPU / WebGL2 Presentation Layer** — PixiJS v8, hardware-accelerated shaders, audio contexts, skeletal mesh rendering.
3. **WebAssembly Memory Boundary** — the high-speed data link from C# component arrays to PixiJS WebGPU pipelines.

```
C# ECS Engine Core (Arch / Box2D.NET target)
   |  native logic / systems                 |  fixed-step physics (Box2D.NET target)
   v                                         v
C# Transform & Motion Systems (contiguous)   C# Physics Engine (Box2D.NET target)
   |  zero-copy / direct HEAPF32 transfer (target)
   v
=============== WASM Interop Boundary (memory HEAP) ===============
   |  batched render snapshot / matrix buffer
   v
PixiJS v8 Presentation Layer (WebGPU / WebGL2 pipelines)
   |-- glTF 2.0   |-- @pixi/tilemap   |-- @pixi/sound   |-- pixi-filters
```

## Three Layers (ADR-001)

| Layer | Role | Status |
| --- | --- | --- |
| **1. C# Authoritative World** | ECS + Box2D.NET; gameplay physics, collisions, rules, deterministic tick. Sole authority. | Arch ECS implemented (`EcsSimulation` 60 Hz, `MovementSystem`/`ColorSystem`, batched `EcsRenderSignal`, SSR `Snapshot()`). Box2D.NET vendored, **not wired**. |
| **2. Presentation World** | Lightweight custom interpolation (default) + optional Rapier 2D (visual dynamics). Pure mirror of authoritative state. | Not implemented. |
| **3. PixiJS v8** | Sprites, containers, animation, camera, particles, GPU render. | Bootstrap implemented (`initGame`/`renderText`/`renderScene`, scenes, stats overlays). |

Rule: never move simulation back-and-forth through JS interop every frame. Keep any JS physics world resident; feed it snapshots at discrete boundaries.

## The WASM->JS Bridge (ADR-003)

**Problem:** per-entity `IJSRuntime.InvokeVoidAsync` at 60 FPS saturates interop; simulation (60 Hz) and display (144 Hz) differ in time domain -> jitter.

**Current (interim):** `GET /api/ecs/stream` SSE pushes `event: sprite-move` with batched `SpriteState[]` JSON (`Id, X, Y, R, G, B`) at a 1 s throttle. No velocity/rotation/tick.

**Target:** the boundary = "the simulation produced a render snapshot", carrying kinematic data:

```csharp
public readonly record struct TransformSnapshot(
    int EntityId, float X, float Y, float Rotation,
    float VelocityX, float VelocityY, float AngularVelocity, long Tick);

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct RenderTransform { public float X, Y, Rotation, ScaleX, ScaleY; }
```

Pin the C# block (`GCHandle.Alloc(..., Pinned)` / `Marshal.AllocHGlobal`), pass `IntPtr` to JS, view as `new Float32Array(wasmModule.HEAPF32.buffer, ptr, entityCount * Stride)`; the PixiJS ticker reads the matrix buffer in a single O(1) interop tick. Client interpolates: `P_render = P_prev + (P_curr - P_prev) * alpha`, `alpha = (T_now - T_last_tick) / T_tick`.

## Physics Architecture (ADR-002, ADR-005)

```
C# ECS + Box2D.NET (authoritative) -> snapshots -> JS bridge
   |-- custom interpolation (cheap / default)   |-- Rapier 2D (optional, visual dynamics)
   \-- both -> PixiJS v8
```

- **Box2D.NET** = authoritative gameplay physics in the C# ECS loop (raycasts/AABB queries zero-interop).
- **Custom lerp/slerp/spring** = default for plain interpolation (zero-overhead).
- **Rapier `@dimforge/rapier2d`** = optional, entity-selective (`PresentationPhysicsComponent { Mode = Interpolate | Spring | Rapier2D | CustomGpu }`), visual dynamics only (capes, ropes, ragdolls, debris). Not the deterministic build.
- Four answers: Box2D.NET = "where is it really?"; interpolation = "where to draw?"; Rapier = "how does it move dynamically?"; PixiJS = "how to render?"

## Skeletal Animation — Two Pipelines (ADR-004)

- **Pipeline A — Authoring (offline):** AI Agent + Blender `bpy` -> armature + animations -> `.glb`. `AI + Blender = Content Pipeline`, not game runtime.
- **Pipeline B — Runtime:** `.glb` -> glTF Importer (C#) -> ECS -> `AnimationSystem` -> `TransformSystem` -> `SkinningSystem` -> joint matrix palette -> PixiJS/GPU.

glTF = input asset format, **not** the ECS architecture. A character = one entity with data-oriented components:

```
SkeletonComponent { JointCount, ParentIndices[], LocalTransforms[], GlobalTransforms[],
                    InverseBindMatrices[], JointEntities[] }   // contiguous arrays
AnimationPlayerComponent { CurrentClip, CurrentTime, PlaybackSpeed, Loop, State }
SkinnedMeshComponent - RenderComponent
```

Animation state machine belongs to the ECS, not glTF. See `docs/2d-skeletal-animations/index.md`.

## Domain Responsibility Matrix (ADR-006)

| Responsibility | C# (Sim) | PixiJS (Pres.) |
| --- | :---: | :---: |
| Game rules & logic | Y | N |
| Collision & hit detection | Y | N |
| Gravity & impulses | Y | N |
| Character controllers | Y | N |
| Deterministic networking | Y | N |
| Position interpolation | N | Y |
| Sprite transforms & animation | N | Y |
| Camera smoothing | N | Y |
| Secondary motion (cloth, ragdoll) | N | Y |
| Particle physics & screen effects | N | Y |

## Ecosystem Integration Matrix

| Component | Runtime | State source of truth | Interop | Role |
| --- | --- | --- | --- | --- |
| PixiJS v8 core | JS (WebGPU/WebGL) | JS display tree | shared buffer (target) / SSE JSON (now) | View layer; consumes transform buffers |
| glTF 2.0 / `.glb` | JS (GPU skinning, target) | C# skeleton comps (target) | event-driven | animation triggers from C#; skinning on GPU |
| C# physics (Box2D.NET) | C# WASM | C# RigidBody comps | zero interop | solves dynamics in WASM memory |
| `@pixi/tilemap` | JS (WebGPU) | C# tile-map array | one-time / chunk | binary grid buffer on load; O(1) draw calls |
| `@pixi/sound` | JS (Web Audio) | C# sound comps | event-driven | C# `AudioSystem` -> spatial audio |
| `pixi-viewport` | JS | C# camera entity | shared buffer (target) | C# `CameraSystem` focus -> JS affine transform |
| `CullerPlugin` | JS | viewport bbox | zero (internal JS) | skips offscreen draw calls |
| `pixi-filters` | JS / GPU shader | C# render settings | low-freq mutation | post-processing (Bloom, CRT, Shockwave) |
| Blazor Razor HUD | C# / DOM overlay | C# reactive state | native data binding | HTML5 HUD over canvas; crisp, accessible |
| `@pixi/ui` | JS (canvas) | world-space containers | shared / event | world-space UI (enemy health bars, click targets) |

## Ecosystem Packages

The full PixiJS v8 stack is already declared in `src/Game.UI/package.json`: `pixi.js`, `@pixi/ui`, `@pixi/sound`, `@pixi/tilemap`, `pixi-viewport`, `pixi-filters`, `@spd789562/particle-emitter`.

**Not installed:** `@dimforge/rapier2d` (optional presentation physics — target). Vendored C# libs `src/Box2D.NET` (physics) and `src/BrainAI` (pathfinding/AI) exist but are **not referenced** by `Game.Engine.csproj`.

## Implementation Status

| Capability | Status |
| --- | --- |
| Arch ECS sim (60 Hz, systems, batched signal, SSR snapshot, SSE stream) | Implemented |
| PixiJS bootstrap (`initGame`/`renderText`/`renderScene`, scenes, stats) | Implemented |
| Static-SSR web host + SSE delta bridge (`/api/ecs/stream`) | Implemented |
| `SpriteState` -> `TransformSnapshot` (velocity/rotation/tick) | Target |
| Shared-memory `HEAPF32` zero-copy transfer | Target |
| JS interpolation layer (prev / curr / `alpha`) | Target |
| Box2D.NET authoritative physics in ECS loop | Target (vendored) |
| Rapier presentation physics (entity-selective) | Target (not installed) |
| glTF importer + skeletal ECS components | Target |
| Camera / tilemap / audio / culler integration | Target |
