# Render Interpolation & Presentation-Physics Coupling — Implementation Guide

> Reviewed blueprint for the dual-physics, asymmetric-language topology (C# Box2D.NET authoritative sim ← boundary → TypeScript PixiJS + optional Rapier2D presentation). Grounded in verified code: `src/Game.UI/Frontend/scenes/asteroids.ts` (reference pattern), `snake.ts`, and ADR-003/005/006. When code and prose disagree, verified files win.

## 1. Mathematical Foundation

The authoritative C# simulation runs a fixed timestep `Δt_sim` (e.g., 16.66 ms for 60 Hz). The PixiJS presentation runs at the display's variable rate (60/144/240 Hz). Mapping incoming snapshots directly onto sprites produces stepped motion whenever display Hz > sim Hz. The client therefore treats itself as a **delayed, state-buffering consumer** and evaluates each frame at a fractional progression `α` between the last two authoritative states:

```
α = (T_now − T_last_signal) / Δt_sim        (clamped to [0, 1] — no extrapolation)
P_render = P_prev + (P_curr − P_prev) × α    (LERP, per axis)
θ_render = θ_prev + wrap(θ_curr − θ_prev) × α  (shortest-path angular LERP)
```

Angular wrap must be applied before scaling with α, or bodies rotating across ±π will spin the long way round:

```typescript
let delta = curr.rotation - prev.rotation;
if (delta > Math.PI) delta -= Math.PI * 2;
else if (delta < -Math.PI) delta += Math.PI * 2;
rotation = prev.rotation + delta * alpha;
```

In 2D this is plain shortest-path angular LERP — **no quaternion SLERP** (that is a 3D-only concern).

**Derive `Δt_sim` from the signal, do not hardcode it.** The server owns the tick rate; ship it in the payload (`stepMs` in `SnakeRenderSignal`, `tickMs` in every signal). `snake.ts` does exactly this: `stepMs = Math.max(1, signalStepMs)` on ingest, `alpha = min(1, (performance.now() − lastSignalAt) / stepMs)` on draw. Hardcoding `16.666` breaks the moment the server tick changes.

## 2. State Buffering — the shipped pattern (per-entity `InterpState`)

⚠️ Do **not** copy the whole buffer per push (`this.previous = new Map(this.current)` allocates a fresh Map per snapshot — churn at 60 pushes/s × N entities). The shipped pattern (`asteroids.ts` L400–423) keeps one in-place `InterpState` per entity:

```typescript
interface InterpState {
    prev: AsteroidSpriteState;
    curr: AsteroidSpriteState;
    at: number;              // performance.now() at ingest
}

const interp = new Map<number, InterpState>();
let lastSignalAt = performance.now();
const TICK_MS = 1000 / 60;  // fallback; prefer the signal's tickMs/stepMs

const ingest = (states: AsteroidSpriteState[]) => {
    const now = performance.now();
    lastSignalAt = now;
    const seen = new Set<number>();
    for (const state of states) {
        seen.add(state.id);
        const existing = interp.get(state.id);
        if (existing) {
            existing.prev = existing.curr;   // shift, no allocation
            existing.curr = state;
            existing.at = now;
        } else {
            // New entity: prev = curr → α-safe, renders at its spawn pose.
            interp.set(state.id, { prev: state, curr: state, at: now });
        }
    }
    for (const id of interp.keys()) {
        if (!seen.has(id)) interp.delete(id);  // despawn pruning
    }
};

const nowAlpha = () => Math.min(1, (performance.now() - lastSignalAt) / TICK_MS);
```

Why per-entity state instead of two global maps:

- **Spawn handling** — a first-seen entity must not interpolate from `(0,0)`.
- **Despawn pruning** — entities absent from a batch are removed the same tick.
- **Zero per-push allocation** — only the incoming signal array is new memory.

The ticker then interpolates once per frame and draws (`asteroids.ts` `drawWorld`, L539–551):

```typescript
const onTicker = (ticker: Ticker) => {
    const dt = Math.min(ticker.deltaMS / 1000, 1 / 30);  // clamp huge frame gaps
    const alpha = nowAlpha();
    for (const { prev, curr } of interp.values()) {
        const x = prev.x + (curr.x - prev.x) * alpha;
        const y = prev.y + (curr.y - prev.y) * alpha;
        // …shortest-path angular lerp for rotation, then draw…
    }
};
```

`dt` clamping (`Math.min(deltaMS/1000, 1/30)`) matters for the particle emitters and any Rapier stepping — a background-tab stall must not inject a multi-second physics jump.

### Implementation status per scene

| Scene | SSE event | Signal carries interpolation data? | Client interpolation |
| --- | --- | --- | --- |
| `asteroids.ts` | `asteroids-move` | Yes — rotation, `vx`/`vy`, kind, size | ✅ prev/curr + α (reference pattern, ADR-003) |
| `snake.ts` | `snake-move` | Yes — `previousX/Y`, velocity, kind, `stepMs` | ✅ prev/curr + α (signal-borne step rate) |
| `ecsSprites.ts` | `sprite-move` | ❌ — `SpriteState` = `Id, X, Y, R, G, B` only | ❌ direct mapping (target: `TransformSnapshot`) |
| `tetris.ts`, `breakout.ts` | `tetris-move`, `breakout-move` | Game-specific | ❌ raw redraw (grid/step games — acceptable) |

Extending generic `SpriteState` → `TransformSnapshot` (velocity/rotation/tick, ADR-003) plus this buffer pattern in `ecsSprites.ts` is the open bridge-evolution task.

## 3. Coupling Rapier2D (presentation) with Box2D.NET (authoritative)

**Asymmetric rigidity:** authoritative bodies drive the presentation world; the presentation world never exerts forces back onto authoritative bodies. In the Rapier world, mirrored authoritative entities are `RigidBodyType.KinematicPositionBased`; pure presentation bodies (debris, sparks, cloth) are `RigidBodyType.Dynamic`. Collision resolution is one-way: kinematic → dynamic.

Verified API (installed `@dimforge/rapier2d` typings): `RigidBodyDesc.newKinematicPositionBased()`, `body.setNextKinematicTranslation({ x, y })`, `body.setNextKinematicRotation(angleInRadians)`, `body.isKinematic()`.

```typescript
function updateRapierKinematicBody(id: number, x: number, y: number, rotation: number): void {
    const kinematicBody = rapierBodies.get(id);
    if (kinematicBody && kinematicBody.isKinematic()) {
        // Force the Rapier body to exactly match the interpolated C# state.
        kinematicBody.setNextKinematicTranslation({ x, y });
        kinematicBody.setNextKinematicRotation(rotation);
    }
}
```

Ticker flow, in order:

1. Compute interpolated `P_render` for each authoritative entity (Section 2).
2. Push it into its Rapier kinematic body via `setNextKinematicTranslation/Rotation` (Rapier then estimates the body's velocity, so debris receives correct impulses from a moving ship).
3. `physicsWorld.step()` — the world stays **resident** in JS; never rebuilt, never round-tripped through interop (ADR-002).
4. Read the resulting `Dynamic` body translations and draw them.

**Current status (ADR-005):** the asteroids scene runs Rapier debris as `Dynamic` bodies only (`spawnDebris` → `RigidBodyDesc.dynamic()`, zero gravity, restitution 0.6, 1.8 s lifetime, removed and freed on expiry; `physicsWorld.free()` on cleanup). Kinematic mirroring of authoritative entities is **target work**, gated on the per-entity `PresentationPhysicsComponent { Mode }` routing (`Interpolate | Spring | Rapier2D | CustomGpu`). Default remains cheap lerp; pay the Rapier cost only for genuine visual dynamics (cape-ropes, ragdolls, debris).

## 4. Dual-Physics Isolation Matrix

| Parameter | Authoritative Layer (C# / Box2D.NET) | Presentation Layer (TS / PixiJS / Rapier) |
| --- | --- | --- |
| Execution domain | Server-side static-SSR ASP.NET Core host (`Game.Web`) — **not** Blazor WASM | Browser main thread (Vite IIFE bundle) |
| Clock / timestep | Fixed `Δt_sim` (60 Hz `EcsSimulation` / game sims) | Variable, V-Sync bounded (`PIXI.Ticker`) |
| Physics role | Gameplay mechanics, hitboxes, mass, contact events (Asteroids) | Visual debris, particles, secondary motion only |
| Body mapping | `b2BodyType` Dynamic/Static | `KinematicPositionBased` (mirrors C#) / `Dynamic` (local VFX) |
| Collision resolution | Two-way (force ↔ force) | One-way (kinematic → dynamic) |
| State output | Computes `S_n`, emits one batched signal per interval | Consumes `S_n`, buffers prev/curr, evaluates `(1−α)S_{n−1} + αS_n` |
| Interpolation | None | LERP translation, shortest-path angular LERP rotation |

Determinism corollary (ADR-002): the authoritative world uses deterministic single-worker Box2D.NET; the Rapier JS build is **not** deterministic and does not need to be — presentation-only.

## 5. Future: `HEAPF32` shared memory (ADR-003 target)

When the SSE/JSON bridge evolves to pinned shared-memory transfer (`GCHandle.Alloc(..., Pinned)` / `Marshal.AllocHGlobal` → `IntPtr` → `new Float32Array(wasmHeap, ptr, entityCount * Stride)`), the buffer math is unchanged: `ingest` reads prev/curr straight from stride-indexed floats over a zero-copy view. Design the `InterpState` buffer so the snapshot *source* is swappable — never let JSON parsing leak into the interpolation loop itself.

## 6. Review corrections applied to the source blueprint

1. Interpolation is **not** on the generic `sprite-move`/`EcsRenderSignal` path — it is implemented in `snake.ts` and `asteroids.ts` with game-specific signals. `ecsSprites.ts` direct-maps today.
2. Whole-map copy per push replaced with in-place per-entity `InterpState` (allocation churn, spawn/despawn semantics).
3. Fixed 16.666 constant replaced with signal-borne `stepMs`/`tickMs`.
4. "SLERP for rotation" corrected to shortest-path angular LERP (2D).
5. "Execution Domain: Blazor WebAssembly (AOT)" corrected to the static-SSR server host (no Blazor WASM exists in this repo).
6. Rapier kinematic coupling marked target work (ADR-005 `PresentationPhysicsComponent`); current Rapier usage is Dynamic debris only. API usage verified against installed typings.

## 7. Post-rollout fixes (SnapshotBuffer era)

Two defects found after the `SnapshotBuffer` rollout to all scenes:

1. **Epoch-before-seq ordering (restart freeze).** `SnapshotBuffer.ingest` rejected `seq <= lastSeq` *before* checking the epoch. Every sim reset (`SnakeSimulation.Reset`, racer restart, etc.) bumps `_epoch` and zeroes `_seq`, so the new run's first signals (seq 1, 2, … ≤ the dead run's seq) were dropped as stale — the scene froze on the dead board until a page reload. Fix: on epoch change, clear entries and reset `lastSeq = -1` **before** the stale-seq rejection. Scenes additionally reset client-only state on epoch change (asteroids: explosion emitters, Rapier debris, ignition bookkeeping; racer: parallax offsets, `previousPosition`).
2. **Idle full-redraw per ticker frame (FPS regression).** The rollout moved scenes from "draw once per SSE signal" to "draw every ticker frame". While a sim streams at 60 Hz that is inherent, but on start overlays, game-over screens and paused sims no signals arrive — yet every scene kept clearing and rebuilding its entire `Graphics` at display Hz on identical data. Fix: `SnapshotBuffer.advance(stepMs)` returns the render alpha, or `null` when nothing can have changed (no ingest since the last draw and α already settled at 1). Scenes early-out on `null`; presentation-only systems that genuinely need every frame (asteroids particle emitters, Rapier debris stepping) keep running unconditionally. Known trade-off: pacman's power-pellet pulse (a `performance.now()` sine drawn inside `draw`) freezes while the sim is idle.

Redraw-gate pattern for every scene:

```typescript
const onTicker = () => {
    const alpha = interpolation.advance(stepMs);
    if (alpha !== null) draw(alpha);
};
```
