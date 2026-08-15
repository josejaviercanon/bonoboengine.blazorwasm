# ADR-006: Domain Responsibility Matrix — C# Simulation vs PixiJS Presentation

**Date:** 2026-08-15
**Status:** Accepted (operational rule; several presentation-side capabilities are not yet implemented)

## Context

To maintain architectural purity and prevent dual-authority desync, responsibilities must be strictly isolated. Implementing authoritative physics in both WASM and JS forces visual rollback (C# `x = 100`, PixiJS `x = 103`, next C# update jumps the entity backward).

## Decision

Adopt this ownership matrix:

| Responsibility | C# WebAssembly (Simulation) | PixiJS (Presentation) |
| --- | :---: | :---: |
| Game rules & logic | ✅ | ❌ |
| Collision & hit detection | ✅ | ❌ |
| Gravity & impulses | ✅ | ❌ |
| Character controllers | ✅ | ❌ |
| Deterministic networking | ✅ | ❌ |
| Position interpolation | ❌ | ✅ |
| Sprite transforms & animation | ❌ | ✅ |
| Camera smoothing | ❌ | ✅ |
| Secondary motion (cloth, ragdoll) | ❌ | ✅ |
| Particle physics & screen effects | ❌ | ✅ |

Rule: **never** implement authoritative physics in both layers. C# owns "where is it really?"; PixiJS owns "where/how do I draw it?"

## Consequences

- Clear ownership eliminates rollback/jitter; the C# core is relocatable to a server unchanged.
- Presentation-side capabilities (interpolation, camera spring, secondary motion, particles) are target work; the C# side is the existing authority.
- This matrix operationalizes ADR-001 (three-layer topology). Full ecosystem integration matrix lives in `docs/architecture/topology.md`.
