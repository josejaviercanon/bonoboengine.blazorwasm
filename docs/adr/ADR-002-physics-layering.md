# ADR-002: Physics Layering — Box2D.NET Authoritative, Rapier Optional Presentation

**Date:** 2026-08-15
**Status:** Accepted (implemented: Box2D.NET wired into `Game.Engine` and used by `AsteroidsSimulation`; Rapier `@dimforge/rapier2d` installed and used by the asteroids presentation layer)

## Context

The engine needs deterministic gameplay physics **and** smooth visuals at arbitrary monitor refresh rates (60/90/144/240 Hz). The topology doc evaluates Rapier (Rust → WASM, JS bindings `@dimforge/rapier2d`) vs Box2D.NET (pure C#).

## Options Considered

1. **Rapier as authoritative gameplay sim** — Rejected: moves the authoritative simulation into JS/WASM, reintroduces dual-authority desync, and splits the C# server-authority path.
2. **Box2D.NET in C# ECS loop + no JS physics** — Rejected: loses visual dynamics (cloth, ragdoll, debris, secondary motion).
3. **Box2D.NET authoritative in C# ECS + Rapier as optional JS-side presentation-physics runtime** — Chosen.

## Decision

- **Box2D.NET** (vendored at `src/Box2D.NET/Box2D.NET.csproj`) = authoritative gameplay physics, executed directly inside the C# ECS loop. Raycasts/AABB queries resolve in C# memory with **zero** interop marshalling.
- **Rapier** (`@dimforge/rapier2d`, 2D; **not** the deterministic build — determinism is unnecessary for presentation) = optional JS-side presentation physics for **visual dynamics only**, never authoritative.
- **Custom lightweight interpolation** (lerp / slerp / spring / damping / extrapolation) is the default, cheap path. Use Rapier **only** where genuine visual dynamics are required (capes, ropes, ragdolls, debris, soft visual effects) — see ADR-005.

## Consequences

- Clear four-part answer: **Box2D.NET** = "where is the object really?"; **interpolation** = "where to draw this frame?"; **Rapier** = "how should this visual object move dynamically?"; **PixiJS** = "how to render it?"
- Rapier's WASM loads asynchronously; it coexists with Blazor WASM + PixiJS. Keep the Rapier world resident in JS; never ping-pong simulation state through interop per frame.
- Status: `src/Box2D.NET` is wired into `Game.Engine.csproj` and used by `AsteroidsSimulation` as the authoritative physics world (gravity 0, `workerCount = 1`, circle bodies, contact events, screen wrap). `src/BrainAI` (pathfinding/AI) is vendored but unreferenced. Rapier `@dimforge/rapier2d` is in `src/Game.UI/package.json` and drives the asteroids debris field (presentation only).
