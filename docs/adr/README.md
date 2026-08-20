# Architecture Decision Records

Architecture decisions for the Bonobo Engine are recorded here as ADRs, one file per decision.

## Template

```markdown
# ADR-{number}: {Title}

**Date:** {date}
**Status:** Accepted | Superseded by ADR-{n} | Deprecated

## Context
{What prompted this decision? What are the constraints?}

## Options Considered
1. **{Option A}** — {description}
2. **{Option B}** — {description}

## Decision
{Which option was chosen and why.}

## Consequences
- {What changes as a result}
- {What trade-offs were accepted}
```

## Process

- Create an ADR whenever an architecturally significant decision is made (architecture, library, approach).
- Number sequentially (`ADR-001`, `ADR-002`, …).
- Recommended entry points: `docs/game-development/session/session-prompt.md` (Decision Protocol) and `docs/game-development/ai-workflow/gamedev-rules.md` (Documentation Rules).

## Records

- **ADR-001** — [Three-Layer Engine Topology (Authoritative C# / Presentation World / PixiJS Render)](ADR-001-three-layer-topology.md)
- **ADR-002** — [Physics Layering (Box2D.NET Authoritative, Rapier Optional Presentation)](ADR-002-physics-layering.md)
- **ADR-003** — [Render-Bridge Evolution (Batched Snapshots → Shared-Memory HEAPF32 + Client Interpolation)](ADR-003-render-bridge-evolution.md)
- **ADR-004** — [glTF Is the Asset Contract, Not the ECS Architecture](ADR-004-gltf-asset-contract.md)
- **ADR-005** — [Presentation Physics Is Entity-Selective](ADR-005-presentation-physics-selective.md)
- **ADR-006** — [Domain Responsibility Matrix (C# Simulation vs PixiJS Presentation)](ADR-006-domain-responsibility-matrix.md)
- **ADR-007** — [Single-Player Co-Located Mode (Transport Seam + Conditional Compilation)](ADR-007-single-player-co-located-mode.md)
