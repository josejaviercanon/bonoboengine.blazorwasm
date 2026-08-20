# Active Context

## Current Focus (2026-08-19)

**ADR-007 Phase 3 shipped: frontend typed-array consumer.** On top of Phase 1 (C#
`IRenderTransport` seam) and the Vite `define` frontend flag seam. The TS half of
shared-memory snapshots is done; Phase 2 (Game.Wasm host) remains to drive it live.

Phase 3 implemented (verified: typecheck green, live ingest-logic assertions green,
DCE verified on both bundles, full Playwright E2E 29/29 green):

- `Frontend/scenes/bufferLayout.ts` (NEW): float32 signal layout contract — 6-float
  standard header (`seq, epoch, entityCount, stride, stepMs, tickMs`), scene extras,
  then entity records; `readSignalHeader` / `decodeEntities` / `EntityDecoder<T>` /
  `floatBool`. Entity ids exact in float32 up to 2^24 (fine for these ECS ids).
- `interpolation.ts`: `SnapshotBuffer.ingestFromBuffer(floats, decode, entityBase)` →
  `BufferHeader | null` — decodes entities and runs the SAME epoch/seq/extrapolation
  path as JSON `ingest`; `null` = stale seq rejected.
- `signalSource.ts`: `SignalStream.addBufferListener(eventName, (floats) => …)` added;
  the non-selected transport's listener methods are no-op stubs → scenes register both
  SSE + buffer listeners with zero runtime branching. `LocalBufferProvider.onSignal`
  now delivers `Float32Array`.
- `game.ts`: `ScenePayload.bufferPtr/stride/entityCount` (Phase 2 host payload mirrors).
- `snake.ts`: reference consumer — in-scene float32 layout doc (header + 6 extras:
  score, gameOver, started, ate, foodSpawned, foodFalling; stride-11 sprites),
  `decodeSnakeSprite`, `addBufferListener('snake-move')` handler identical in
  semantics to the SSE handler. Other 6 scenes get decoders when Phase 2 pins each
  game's C# layout.
- DCE on minified dist: default build has EventSource, zero provider code; wasm-mode
  build has zero EventSource, buffer path present. Default (sse) bundle rebuilt into
  `wwwroot/dist` after the check.

Historical context — Phase 1 (render transport seam, zero behavior change):

- `src/Game.Engine/ECS/RenderTransport.cs`: `IRenderTransport<TSignal>` (`OnSignal` event +
  `Push`) + `ServerRenderTransport<TSignal>` (in-process event, byte-for-byte today's behavior).
- All 7 sims (`EcsSimulation`, Snake, Tetris, Breakout, Asteroids, Pacman, Racer) now emit via
  injected `_renderTransport.Push(...)`; their public `OnRenderSignal` events became **forwarding
  events** onto the transport — `Game.Web` `Program.cs` SSE endpoints and all tests unchanged.
- Optional ctor param `IRenderTransport<TSignal>? renderTransport = null` (defaults to
  `ServerRenderTransport`) — parameterless/seeded test ctors preserved (15 test files use `new`).
- `Game.Engine.csproj`: `IsMultiplayer`/`IsEcsServerSide` (default `true`); both `false` defines
  `SINGLE_PLAYER_LOCAL`. **No `#if` in simulation code** — mode is selected by injected transport.
- Input seam decision: **no `IInputTransport`** — the sims' public `QueueInput` surface IS the
  input contract; co-located host calls the same methods via interop. Documented in ADR-007.
- Docs: `docs/adr/ADR-007-single-player-co-located-mode.md`, ADR README index, topology.md
  implementation-status rows (Phase 2/3 targets listed).

Next (ADR-007): **Phase 2** — new `Game.Wasm` Blazor-WASM host project,
`DirectRenderTransport` (pinned `float[]` via `GCHandle` → `Float32Array` view,
writes per `bufferLayout.ts`), interop `QueueInput`, host-side
`registerLocalBufferProvider` wiring, own bootstrap + bridge rules (static-SSR-only
rule stays scoped to `Game.Web`). Then per-game float layouts for the remaining
6 scenes.

ADR-007 frontend flag seam (2026-08-19, done — symmetric to the C# MSBuild flags):

- `src/Game.UI/vite.config.ts`: `defineConfig(({ mode }) => …)` with `define:
  { __RENDER_SOURCE__: 'sse' | 'local-buffer' }`. Mode **`wasm`** selects local-buffer
  (`--mode local` is rejected by Vite — conflicts with `.local` env postfix). `npm run
  build:local` = `vite build --mode wasm && tailwind`.
- `Frontend/scenes/signalSource.ts`: scene CONST `RENDER_SOURCE` (compile-time from
  `__RENDER_SOURCE__`), `connectSignalStream(url)` → `SignalStream`
  (`addSignalListener`/`close`/`onInterrupted`), and `registerLocalBufferProvider()` —
  the Phase 3 typed-array hook point. All 7 game scenes refactored onto it (no more
  duplicated `new EventSource` blocks; no `MessageEvent` casts in scenes).
- DCE verified on minified output: default bundle has `EventSource` and zero local-buffer
  code; wasm-mode bundle has the local-buffer branch and **zero** `EventSource`. Scenes
  carry zero runtime transport branching.
- `npm run build:local` builds the local-buffer bundle today but no host serves it yet
  (Phase 2); scenes error with a clear console message if no provider is registered.

Gotcha confirmed for C# too: Snake/Tetris/Breakout/Asteroids/Racer `.cs` files have mixed EOL —
multi-line `old_text` matches fail; use single-line anchors.

Game-examples UX + interpolation regression fix (both complete, all 29 Playwright E2E green):

1. **Start/Play-Again UI on every game scene.** Snake/Tetris/Breakout/Asteroids/Pacman already had
   overlays wired to `/api/*/start`. Added the missing one to `racer.ts`: full-screen start overlay
   (`#racer-start-overlay`, START GAME button, also Space/Enter) — the sim is paused at boot via
   `POST /api/racer/pause` and resumed on START; a RESTART button (`#racer-restart-button`,
   `/api/racer/restart`) appears once running (endless game → restart is the play-again equivalent).
   Tuning-panel cancel/apply only resume when a race is actually running.
2. **SnapshotBuffer epoch bug (restart freeze).** `ingest` rejected `seq <= lastSeq` before the epoch
   check; after any server reset (epoch++, seq→0) all new signals were dropped as stale → dead board
   until reload. Fixed ordering: epoch change clears entries and resets `lastSeq` first. Scenes also
   reset client-only state on epoch change (asteroids: emitters/debris/ignition set; racer: parallax
   offsets + `previousPosition`).
3. **FPS regression after the interpolation rollout.** Scenes redraw full `Graphics` every ticker
   frame even with zero new snapshots (start overlay, game over, paused sims). Added
   `SnapshotBuffer.advance(stepMs)` → alpha or `null` (settled + no new data); every scene gates its
   draw on it. Asteroids particle/debris systems still tick every frame. Trade-off: pacman
   power-pellet pulse freezes while idle (documented).

SSE host-crash fix (2026-08-19, verified). All 7 `Game.Web` SSE endpoints except pacman used
sync-over-async (`WriteAsync(...).GetAwaiter().GetResult()` inside `lock`) invoked from the sim
timer tick. On client disconnect mid-write, `ObjectDisposedException` propagated through
`ServerRenderTransport.Push` ← `Tick` → **unhandled exception on a thread-pool thread killed the
whole host** (reproduced: 12 aborting SSE clients across asteroids/breakout/tetris → crash; E2E
runs randomly died after ~test 9). Fixed: all 7 endpoints now use the pacman pattern — bounded
`Channel<T>(2, DropOldest)`, sim callback only `TryWrite`s, `await foreach ReadAllAsync(ct)` +
`WriteAsync(..., ct)` + `FlushAsync(ct)`, `finally` unsubscribe. Churn re-test: host stays alive,
0 unhandled exceptions. Full E2E 29/29 green on a fresh host. Note: running the host manually
without `ASPNETCORE_ENVIRONMENT=Development` gives static-asset 500s (MapStaticAssets prod-mode
manifest) — always boot with Development like Playwright does.

Remaining known issue (NOT fixed, pre-existing, physics-side): under extreme combined load
(12 aborted SSE clients + minutes of headless asteroids + browser pages + restarts) Box2D
invariants fire (`world.locked == false` in `b2World_Step`, `b2_enlargedNode` in
`b2DynamicTree_ValidateNoEnlarged`) from `AsteroidsGameSystem.StepPhysics`, killing the host via
unhandled exception on the timer thread. Not reproducible in isolation (start→run→restart clean,
restart-while-streaming clean). `Tick` holds `_sync` for the whole step, so it is not SSE-path
reentrancy. Candidate follow-up: wrap sim-timer `Tick` bodies in try/catch that logs + resets the
sim instead of crashing the host.

Documented in `docs/architecture/render-interpolation.md` §7. Racer E2E updated/added in
`src/Game.Tests.UI/tests/racer.spec.ts` (overlay gates input; existing tests now START first).

Render-interpolation documentation. Reviewed an external interpolation/Rapier-coupling
blueprint against verified code, corrected it (wrong signal path, whole-map-copy churn,
hardcoded tick, "SLERP", "Blazor WASM" execution domain, kinematic coupling presented as
current), and captured the production pattern in
`docs/architecture/render-interpolation.md` (grounded in `asteroids.ts`/`snake.ts`).
Cross-linked from `docs/index.md`, `docs/architecture/topology.md`, ADR-003, and the
`static-ssr-snapshot-bridge` SKILL.md.

Gotcha: several `Frontend/scenes/*.ts` files have **mixed line endings** (`breakout.ts`, parts of
`racer.ts` are CRLF) — multi-line `old_text` editor matches can silently fail there; use single-line
anchors or a Node patch script.

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
