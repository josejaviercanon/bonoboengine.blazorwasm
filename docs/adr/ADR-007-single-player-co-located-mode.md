# ADR-007: Single-Player Co-Located Mode — Transport Seam + Conditional Compilation

**Date:** 2026-08-19
**Status:** Accepted (Phase 1 + Phase 3 TS consumer implemented; Phase 2 WASM host is target work)

## Context

The static-SSR host ships every render snapshot across an HTTP boundary: C# serializes a
batched signal to JSON, `GET /api/{game}/stream` streams it as SSE, and the browser
`JSON.parse`s it before interpolation. That is the right topology for authoritative
multiplayer (ADR-001), but for a local single-player build it is pure overhead:
serialization + loopback HTTP + parse per signal, plus an HTTP POST round-trip per input.
The result is frame stutter and capped render rates in exactly the scenario (one player,
one machine) that needs neither.

Constraint: the C# simulation code (`Game.Engine`, 7 simulations) must remain the sole
authority and must not fork into two implementations. `Game.Web` stays static-SSR-only
(the `static-ssr-snapshot-bridge` skill rules still govern that host).

## Options Considered

1. **TypeScript ECS mirror** — re-implement simulations in TS so ECS and PixiJS share the
   JS heap. Rejected: dual authority and logic drift, violates ADR-001/ADR-006.
2. **Keep SSE only, tune it** — bigger batches, binary frames. Rejected: still crosses a
   network stack per tick; the boundary cost is inherent, not tunable away.
3. **Conditional compilation + transport seam; co-located host later** — introduce
   `IRenderTransport<TSignal>` in `Game.Engine` and MSBuild switches
   (`IsMultiplayer`/`IsEcsServerSide` → `SINGLE_PLAYER_LOCAL`), then (Phase 2) host the
   same engine in a Blazor WASM project where signals reach PixiJS via pinned shared
   memory. Chosen.

## Decision

- Every simulation emits its batched render signal through an injected
  `IRenderTransport<TSignal>` instead of raising its own event directly.
- `ServerRenderTransport<TSignal>` (default) preserves today's behavior exactly: it
  raises an in-process event; each simulation's public `OnRenderSignal` event becomes a
  forwarding event onto the transport, so the SSE endpoints in `Program.cs` are unchanged.
- Build switches in `Game.Engine.csproj`: `IsMultiplayer` and `IsEcsServerSide` default to
  `true` (server-authoritative host). `/p:IsMultiplayer=false /p:IsEcsServerSide=false`
  defines `SINGLE_PLAYER_LOCAL` for the future co-located host.
- **Input seam = the existing public `QueueInput` surface.** No separate
  `IInputTransport` indirection: a co-located host calls the same validated entry points
  via JS→WASM interop that the HTTP POST endpoints call today. C# stays sole authority.
- Phases:
  - **Phase 1 (this ADR, implemented):** interface + default transport + simulation
    refactor + build-flag plumbing. Zero behavior change; the seam exists.
  - **Phase 2 (target):** `Game.Wasm` Blazor-WASM host project; `DirectRenderTransport`
    writing snapshots into a pinned `float[]`/unmanaged block exposed to JS as a
    `Float32Array` view (ADR-003's HEAPF32 target); input via interop calling
    `QueueInput`.
  - **Phase 3 (target):** TypeScript `SnapshotBuffer` gains a typed-array ingest source
    (`ingestFromBuffer`) so scenes consume shared memory with the same interpolation
    math (`docs/architecture/render-interpolation.md` §5).

## Amendment (same day): Frontend half of the flag — Vite `define` + scene CONST

Phase 1 gave C# flag compilation; the frontend now has the symmetric mechanism:

- **Mechanism (researched):** Vite `define` replaces the identifier `__RENDER_SOURCE__`
  textually **before** Rollup tree-shaking, so the unselected transport branch is
  dead-code-eliminated from `wwwroot/dist/game-bundle.js`. Alternatives rejected:
  `import.meta.env.VITE_*` (stringly-typed, no DCE guarantee), a runtime query-param
  switch (ships both paths, pays for both), separate entry files (duplicates scene code).
  Vite `--mode` was chosen over a raw env var for cross-platform npm scripts.
  Note: mode name `local` is rejected by Vite (conflicts with `.local` env postfix) —
  the local mode is named **`wasm`**.
- **Scene CONST:** `src/Game.UI/Frontend/scenes/signalSource.ts` exports
  `RENDER_SOURCE: 'sse' | 'local-buffer'` (compile-time, from `__RENDER_SOURCE__`) and
  `connectSignalStream(url)` → `SignalStream` (`addSignalListener` / `close` /
  `onInterrupted`). All 7 game scenes (ecsSprites, snake, tetris, breakout, pacman,
  asteroids, racer) now connect through it instead of duplicating `new EventSource`
  blocks — one branch point per bundle, zero runtime branching in scenes.
- **Phase 3 hook point:** `registerLocalBufferProvider(provider)` — in a `wasm`-mode
  bundle the co-located host registers the typed-array bridge; if none is registered
  the scene logs a clear error instead of silently degrading. This branch does not
  exist in default (sse) bundles.
- **Builds:** `npm run build` → `'sse'` (default, `Game.Web`); `npm run build:local`
  → `vite build --mode wasm` → `'local-buffer'`.
- **Verified:** default bundle contains `EventSource` and zero `local-buffer` code;
  `wasm`-mode bundle contains the local-buffer branch and **zero** `EventSource`
  references (grep on minified output). Typecheck green; full Playwright E2E green.

## Amendment: Phase 3 implemented (frontend typed-array consumer)

Phase 3's TypeScript half is done; only the Phase 2 C# host (`DirectRenderTransport`
writer + `registerLocalBufferProvider` wiring) remains to light it up end-to-end.

- **Buffer layout contract — `Frontend/scenes/bufferLayout.ts`:** every signal
  buffer starts with a 6-float standard header
  (`seq, epoch, entityCount, stride, stepMs, tickMs` = `BUFFER_HEADER_LENGTH`),
  followed by scene-specific scalar extras (score/lives/event flags), then
  `entityCount × stride` entity records. `readSignalHeader`, `decodeEntities`,
  and a per-scene `EntityDecoder<T>` materialize entities straight out of the
  `Float32Array` — no JSON, no strings. Entity ids ride in float32 (exact to 2^24,
  fine for these examples' ECS id ranges).
- **`SnapshotBuffer.ingestFromBuffer(floats, decode, entityBase)`** — typed-array
  ingest that runs the *exact same* epoch/seq bookkeeping and interpolation as the
  JSON `ingest` (epoch-change clear, stale-seq rejection, extrapolation). Returns
  the `BufferHeader` when accepted (stats + `stepMs`), `null` on stale seq.
- **Transport split inside `SignalStream`:** `addSignalListener` (JSON text,
  SSE) and `addBufferListener` (Float32Array, local-buffer) coexist as siblings;
  the non-selected one is a no-op stub, so scenes register both and carry **zero
  runtime transport branching** — only the compiled-in listener ever fires.
  `LocalBufferProvider.onSignal` now delivers `Float32Array`.
- **`ScenePayload.bufferPtr/stride/entityCount`** (`game.ts`): diagnostics mirrors
  of the shared buffer for the Phase 2 host payload.
- **Reference consumer: snake** — full float32 layout documented in-scene
  (header + 6 snake extras: score, gameOver, started, ate, foodSpawned,
  foodFalling; stride-11 sprite records) and a `decodeSnakeSprite` +
  `addBufferListener('snake-move', …)` handler with semantics identical to the
  SSE handler. Remaining 6 scenes get their decoders when Phase 2 fixes each
  game's concrete C# layout.
- **DCE re-verified on minified output:** default bundle — `EventSource` present,
  zero `registerLocalBufferProvider`; wasm-mode bundle — zero `EventSource`,
  buffer path present.
- **Verified:** `tsc -b` green; live logic check of `ingestFromBuffer`
  (decode, prev-promotion, stale-seq rejection, epoch-change reset) via a
  throwaway esbuild-bundled node script; full Playwright E2E 29/29 green.

## Consequences

- Simulation code contains no transport `#if` — the mode is selected by which transport
  is injected, not by scattering preprocessor directives through the tick loops.
- `Game.Web` is untouched: static SSR, SSE bridge, antiforgery rules all unchanged. The
  static-SSR-only skill rules apply to `Game.Web`; `Game.Wasm` (Phase 2) will be a
  separate host with its own bootstrap and needs its own skill/bridge rules.
- Multiplayer path is unchanged: `ServerRenderTransport` + SSE remains the default build.
- Phase 2 requires a new host project and an amendment documenting that the WASM host is
  exempt from the static-SSR-only rule (that rule targets `Game.Web`).
- Tests construct simulations with `new` (parameterless/seeded ctors) — those overloads
  are preserved; the transport parameter is optional and defaults to
  `ServerRenderTransport`.
