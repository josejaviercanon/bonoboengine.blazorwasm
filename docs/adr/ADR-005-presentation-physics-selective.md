# ADR-005: Presentation Physics Is Entity-Selective

**Date:** 2026-08-15
**Status:** Accepted (decision; `PresentationPhysicsComponent` not yet implemented; Rapier not installed)

## Context

Making Rapier mandatory for every entity wastes browser-side physics cost on entities that only need plain interpolation. The topology doc states: for simple A→B smoothing, Rapier is overkill — `lerp(prev, curr, α)` has virtually zero overhead.

## Options Considered

1. **Rapier for all entities** — Rejected: overkill for plain interpolation; cost not proportional to need.
2. **No Rapier at all** — Rejected: loses genuine visual dynamics (capes, ropes, ragdolls, debris).
3. **Per-entity `PresentationPhysicsComponent { Mode }`** — Chosen.

## Decision

- Presentation-physics cost scales with genuine need via a per-entity mode:

```csharp
public struct PresentationPhysicsComponent
{
    public PresentationPhysicsMode Mode; // Interpolate | Spring | Rapier2D | CustomGpu
}
```

- Routing by intent:
  - `Enemy`, `Player` → `Interpolate`
  - `Camera` → `Spring`
  - `Cape`, `Debris`, `Ropes`, `Ragdoll` → `Rapier2D`
  - `Particles` → `CustomGpu` (local gravity/turbulence; the C# ECS is ignorant of their coordinates)
- Use Rapier **only** where visual dynamics are required; use custom interpolation for everything else.

## Consequences

- Browser-side physics cost stays proportional to what actually needs it.
- Secondary motion (hair, capes, weapon sway, UI indicators, sprite squash/stretch), camera spring tracking, visual particles, and animation-blend-from-velocity all live in the PixiJS presentation domain — C# only dictates root entity velocity.
- Status: the component is target/planned; no presentation-physics layer exists yet. Depends on ADR-002 (Rapier optional) and ADR-003 (snapshots carry velocity).
