# ADR-003: Render-Bridge Evolution — Batched Snapshots → Shared-Memory HEAPF32 + Client Interpolation

**Date:** 2026-08-15
**Status:** Accepted (interim SSE/JSON bridge implemented; shared-memory HEAPF32 zero-copy + velocity-bearing snapshot are target)

## Context

Simulation frequency and display frequency operate in different time domains (C# ticks 60 Hz; a monitor may run 144 Hz). Pushing position updates directly to PixiJS yields stepped, jittery motion. The naive per-entity `EntityMoved(x,y)` push saturates `IJSRuntime` at scale.

## Options Considered

1. **Keep per-entity event push** (`EntityMovedEvent` per entity per tick) — Rejected: saturates interop, carries no temporal context (no prev/velocity), jitter at high refresh rates.
2. **Full shared-memory HEAPF32 zero-copy immediately** — Target, but not the first evolutionary step.
3. **Evolve the bridge: batched kinematic snapshots now → pinned unmanaged shared memory + `Float32Array` view later** — Chosen.

## Decision

- The boundary concept is **"the simulation produced a render snapshot"**, not "an entity moved."
- Snapshots carry richer kinematic data so the client can interpolate/extrapolate:

```csharp
public readonly record struct TransformSnapshot(
    int EntityId, float X, float Y, float Rotation,
    float VelocityX, float VelocityY, float AngularVelocity, long Tick);
```

- **Target transfer path (zero-copy):** pin the C# transform block (`GCHandle.Alloc(..., GCHandleType.Pinned)` or `Marshal.AllocHGlobal`), pass an `IntPtr` to JS during setup, and create a typed-array view on the WASM heap:

```js
const floats = new Float32Array(wasmModule.HEAPF32.buffer, ptr, entityCount * Stride);
```

The PixiJS ticker then iterates `HEAPF32` in a single O(1) interop tick, streaming matrix data into WebGPU/WebGL batch buffers.
- The client owns interpolation at display Hz: `P_render = P_prev + (P_curr - P_prev) * α`, `α = (T_now - T_last_tick) / T_tick`.

## Consequences

- Eliminates per-frame JSON serialization and per-entity interop; enables smooth motion at any refresh rate.
- Requires a JS interpolation layer (prev + current state, accumulator `α`).
- Interim (current) implementation: `GET /api/ecs/stream` SSE pushes `event: sprite-move` with batched `SpriteState[]` JSON (`Id, X, Y, R, G, B`) at a 1 s throttle; `SpriteState` has **no** velocity/rotation/tick yet — extending it toward `TransformSnapshot` is the first bridge-evolution task.
- `SnakeSimulation` now applies this pattern with game-specific `SnakeSpriteState` snapshots (`PreviousX/Y`, velocity, kind, `StepMs`) and `snake.ts` ticker interpolation. Deadly-food movement stays in C# because it participates in authoritative collision; no client position-report endpoint is needed.
- The shared-memory / pinned-`IntPtr` / `HEAPF32` path is target architecture, not current code.
- Implementation guide for the client side (α math, per-entity prev/curr `InterpState` buffer, Rapier kinematic coupling, dual-physics isolation matrix): `docs/architecture/render-interpolation.md`.
