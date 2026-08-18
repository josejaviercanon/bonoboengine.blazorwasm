# Active Context

## Current Focus (2026-08-18)

Render-interpolation documentation. Reviewed an external interpolation/Rapier-coupling
blueprint against verified code, corrected it (wrong signal path, whole-map-copy churn,
hardcoded tick, "SLERP", "Blazor WASM" execution domain, kinematic coupling presented as
current), and captured the production pattern in
`docs/architecture/render-interpolation.md` (grounded in `asteroids.ts`/`snake.ts`).
Cross-linked from `docs/index.md`, `docs/architecture/topology.md`, ADR-003, and the
`static-ssr-snapshot-bridge` SKILL.md.

Playwright standalone-script documentation hardening (2026-08-17). Documented the ESM (`"type": "module"`) constraint in `src/Game.Tests.UI/package.json`, process-lifecycle bugs (`{ shell: true }` orphaned .NET processes, `cwd` + `--project` path doubling), and the HTTP-readiness-polling anti-pattern. Updated `docs/testing-ui-E2E/index.md`, both `playwright-cli` skill references, `AGENTS.md`, `codebase-truth.md`, and memory bank.

Architecture-topology adoption + agentic-doc alignment. Past scaffold now: Arch ECS sim
(`EcsSimulation` 60 Hz) + static-SSR SSE bridge + PixiJS bootstrap are implemented. The
topology doc (ADR-001…ADR-006, `docs/architecture/topology.md`) now defines the **target**
runtime layers: interpolation, Box2D.NET authoritative physics, optional Rapier
presentation physics, shared-memory `HEAPF32` render bridge, glTF/skeletal pipeline.

## Recent Changes

- Expanded `README.md` from one line to the full architectural blueprint.
- Added `AGENTS.md` (agent/build rules), `bonoboWebGame.slnx`, `docs/` (architecture +
  2d-games + game-development knowledge base), and the four `src/` projects.
- Verified: `Game.UI` PixiJS hello-world renders via `initGame` from `GameView.razor`
  (per repo docs; build outputs present).
- Doc overhaul (agentic-dev readiness):
  - Created `docs/ai-agents/codebase-truth.md` (verified facts from csproj/package.json/source).
  - Repaired broken markdown in `docs/index.md` (source of truth) and synced `README.md` to match.
  - Deleted empty `docs/game/`; added cross-links between `docs/2d-games` and `docs/game-development` (new `index.md`).
  - Populated `openspec/config.yaml` with repo context; created `docs/adr/` infrastructure (README + template).
  - Adopted the topology doc: recorded ADR-001…ADR-006; added `docs/architecture/topology.md`
    (Implemented vs Target); embedded the topology blueprint in `docs/index.md`; added
    AGENTS.md architectural guardrails; extended `bonobo-ECS-rules.md` (skeletal/
    presentation-physics target shapes); reconciled `docs/2d-skeletal-animations/index.md`
    (two-pipeline + glTF-as-input rule); added verified facts to `codebase-truth.md`
    (Box2D.NET/BrainAI vendored-unreferenced, Rapier not installed, SSE bridge).

## Next Steps

1. Extend `SpriteState` → `TransformSnapshot` (velocity/rotation/tick) + add a JS
   interpolation layer (prev/curr/α) in `ecsSprites.ts` — first render-bridge evolution
   task (ADR-003). Pattern to copy: `docs/architecture/render-interpolation.md` +
   `asteroids.ts` `InterpState`/`ingest`/`nowAlpha`.
2. Rapier kinematic mirroring of authoritative entities (target, ADR-005
   `PresentationPhysicsComponent`): `KinematicPositionBased` bodies driven by
   `setNextKinematicTranslation/Rotation` from interpolated positions — see
   `docs/architecture/render-interpolation.md` §3.
3. Install `@dimforge/rapier2d` only when presentation physics is actually needed; add
   `PresentationPhysicsComponent` modes (ADR-005).
4. glTF importer + skeletal ECS components (`SkeletonComponent`, `AnimationPlayerComponent`)
   per ADR-004 / `docs/2d-skeletal-animations/index.md`.
5. Target: pinned shared-memory `HEAPF32` zero-copy transfer (ADR-003).
6. Add `ProcessCommand` + command/event records; add a `Game.Engine` test project.

## Open Questions / Watch Items

- MAUI workloads availability on this machine for non-Android TFMs.
- Whether to keep template demo pages (`Counter`, `Weather`) in `Game.Web` or strip them.
- `ExampleJsInterop.cs` / `Component1.razor` in `Game.UI` are template leftovers.

## Working Agreements

- `docs/index.md` is the architecture source of truth; update it when code diverges.
- Follow `AGENTS.md`: build frontend first, no concurrent dotnet commands, never hand-edit
  `wwwroot/dist`, never commit build output.
- Keep `Game.Engine` free of UI/platform dependencies.
- Record architecturally significant decisions in `docs/adr/` (ADR-001+).
