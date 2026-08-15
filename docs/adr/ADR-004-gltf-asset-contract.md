# ADR-004: glTF Is the Asset Contract, Not the ECS Architecture

**Date:** 2026-08-15
**Status:** Accepted (decision; glTF importer + skeletal ECS components not yet implemented)

## Context

Skeletal characters are authored in Blender (optionally via an AI agent using `bpy`) and exported as `.glb`. The question: how does Bonobo consume a `.glb` and turn it into ECS data simulated in C# and rendered by PixiJS — without letting the glTF node graph dictate the ECS entity graph.

## Options Considered

1. **One ECS entity per glTF node** — Rejected: creates dozens of independent entities per character purely because glTF contains nodes; poor cache locality, no data-orientation benefit.
2. **glTF drives the ECS entity graph 1:1** — Rejected: couples the asset format to the runtime architecture; hard to swap authoring tool or runtime independently.
3. **glTF = input asset format; ECS owns an optimized runtime representation; two decoupled pipelines** — Chosen.

## Decision

- **glTF is serialized input data, not the ECS architecture.** A skeletal character is **one** entity carrying data-oriented components:

```
CharacterEntity
├── SkeletonComponent { JointCount, ParentIndices[], LocalTransforms[], GlobalTransforms[],
│                       InverseBindMatrices[], JointEntities[] }   // contiguous arrays
├── AnimationPlayerComponent { CurrentClip, CurrentTime, PlaybackSpeed, Loop, State }
├── SkinnedMeshComponent
└── RenderComponent
```

Skeleton data lives as **contiguous arrays** (e.g. `ParentIndices: [-1, 0, 1, 1, 3, 4, …]`) for cache-friendly iteration.
- **The animation state machine belongs to the ECS (Bonobo-native), not to glTF.** glTF stores clips/samplers/channels/interpolation modes (LINEAR/STEP/CUBICSPLINE); the engine decides `CurrentClip`, `Time`, `Speed`, `Loop`, `BlendTarget`, `BlendWeight`.
- **Two independent pipelines communicate only through the asset contract (`.glb`):**
  - **Pipeline A — Authoring (offline):** AI Agent + Blender `bpy` → armature + animations → export → `.glb`. The AI is a content-generation assistant operating on authoring intent, **not** part of the game runtime.
  - **Pipeline B — Runtime:** `.glb` → glTF Importer (C#) → ECS → `AnimationSystem` → `TransformSystem` → `SkinningSystem` → joint matrix palette → PixiJS.

## Consequences

- Authoring pipeline is swappable (Blender ↔ another tool) without touching the runtime; runtime is swappable without touching authoring.
- `AI + Blender = Content Pipeline`, not `Game Runtime` — the AI agent does **not** live inside the game runtime for asset authoring.
- Runtime flow: `.glb → Import → AnimationClip → AnimationPlayerComponent → AnimationSystem → Local Joint Transforms → TransformSystem → Global Joint Transforms → SkinningSystem → Joint Matrix Palette → PixiJS/GPU`.
- Status: `src/Game.Engine/ECS/Components.cs` currently has only `Position`, `Velocity`, `SpriteColor`, `RenderId`; skeletal components + glTF importer are target work. `docs/2d-skeletal-animations/index.md` already covers the Blender→glTF→ECS pipeline and AI-agent insertion points.
