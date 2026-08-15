# ADR-001: Three-Layer Engine Topology — Authoritative C# / Presentation World / PixiJS Render

**Date:** 2026-08-15
**Status:** Accepted (topology; only the C#-authoritative + SSE-delta layer is implemented — interpolation/Rapier/shared-memory are target work)

## Context

Bonobo integrates the PixiJS v8 ecosystem into .NET MAUI Hybrid / Blazor WebAssembly. This shifts execution from a pure JS runtime into a hybrid dual-runtime state machine: a C# WASM/Mono layer (game logic, ECS, physics) and a JS/WebGPU/WebGL2 layer (PixiJS render, shaders, audio). The WebAssembly memory boundary is the data-transfer link between them.

Two failure modes must be avoided:
- Per-entity, per-frame `IJSRuntime.InvokeVoidAsync` (60 FPS) saturates the interop marshalling boundary and breaches the frame budget.
- Running an authoritative physics engine in **both** WASM and JS desynchronizes: C# computes `x = 100`, PixiJS predicts `x = 103`, the next C# update forces a visual rollback and the entity jumps backward.

## Options Considered

1. **Single JS-authoritative stack** — PixiJS owns simulation + render. Rejected: loses the C# server-authority path and duplicates logic across JS (client) and C# (server).
2. **Dual authoritative engines** — C# sim + JS sim both authoritative. Rejected: inevitable desync and visual rollback jitter.
3. **Three-layer topology** — C# authoritative sim (fixed timestep) → render snapshot → JS presentation world (interpolation + optional presentation physics) → PixiJS render. Chosen.

## Decision

Adopt the three-layer topology:

```
C# AUTHORITATIVE WORLD          (ECS + Box2D.NET; gameplay physics, collisions, rules)
        │  fixed timestep → RenderSnapshot (Tick, Pos, Velocity)
        ▼
PRESENTATION WORLD              (lightweight custom interpolation + optional Rapier 2D)
        │  120/144/240 Hz visual
        ▼
PIXIJS v8                       (sprites, containers, animation, camera, particles, GPU)
```

- C# ECS is the **sole** authoritative simulation. The presentation layer is a pure mirror that interpolates/smooths authoritative state and may run presentation physics for visual dynamics only.
- Cross the boundary **only** via batched render snapshots — never per-entity per-frame interop, never move simulation back-and-forth through JS interop every frame.
- Keep any JS-side physics world (Rapier) **resident** in JS/WASM; feed it snapshots at discrete boundaries.

## Consequences

- Clean server-relocation path: the C# core can move to a dedicated ASP.NET server for authoritative multiplayer with **zero** PixiJS architectural change (PixiJS already consumes/interpolates discrete snapshots).
- Requires a fixed-timestep + accumulator interpolation layer on the JS side: `P_render = P_prev + (P_curr - P_prev) * α`, `α = (T_now - T_last_tick) / T_tick`.
- Operational ownership is codified in the Domain Responsibility Matrix (ADR-006) and `docs/architecture/topology.md`.
- Implementation status: the C#-authoritative layer (Arch ECS, `EcsSimulation` 60 Hz, batched `EcsRenderSignal`) and the SSE delta bridge exist; interpolation, Rapier, and shared-memory transfer are target work.
