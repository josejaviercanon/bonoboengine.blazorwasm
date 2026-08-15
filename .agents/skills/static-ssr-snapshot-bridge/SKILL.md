---
name: static-ssr-snapshot-bridge
description: "Mandatory rules for the Bonobo engine's Game.Web host and C#<->JS boundary. Use when touching Blazor hosting, App.razor bootstrap, the render bridge, or any Frontend TS interop. Mandates: static-SSR-only hosting (no Blazor WASM, no CustomElements, no blazor.webassembly.js / window.Blazor.start), batched-snapshot SSE/POST boundary (no DotNet.invokeMethodAsync, no per-frame interop, no IJSRuntime on the web host), and strict TS payload types (no `any`). Triggers on: Blazor WASM, blazor.webassembly.js, window.Blazor.start, CustomElements, RegisterCustomElement, DotNet.invokeMethodAsync, IJSRuntime, JSInvokable, StateHasChanged, SSE, EventSource, text/event-stream, render snapshot, SpriteState, TransformSnapshot, EcsRenderSignal, game.ts, pixi-viewport, data-message, static SSR, interop, bridge, Game.Web, GameView, Program.cs."
license: MIT
---

# Static-SSR + Batched-Snapshot Bridge Rules

Authoritative rules for the `Game.Web` host and the C#↔JS boundary. Verified against `src/` on 2026-08-15. When code and prose disagree, `docs/ai-agents/codebase-truth.md` and `docs/adr/` win. These rules **supersede** any generic "Blazor WASM + Vite + CustomElements" skill for this repo (e.g. the rejected `vanilla-ts-blazor-wasm` pattern).

## 1. Hosting model — static SSR only

`src/Game.Web` is a Blazor Web App in **static SSR only** mode (`Microsoft.NET.Sdk.Web`, `net10.0`). It is **not** a Blazor WASM host. There is no client-side .NET runtime on the web host.

### Do

- `AddRazorComponents()` + `UseAntiforgery()` (required even for static SSR — Razor Components endpoints carry antiforgery metadata) + `MapStaticAssets()` + `MapRazorComponents<App>().AddAdditionalAssemblies(typeof(GameView).Assembly)` (`src/Game.Web/Program.cs`).
- Discover shared RCL routes via `AddAdditionalAssemblies` on `MapRazorComponents` **and** `Router AdditionalAssemblies` in `Components/Routes.razor` — both are required for direct HTTP requests to resolve.
- Bootstrap PixiJS from `Components/App.razor`: load `_content/Game.UI/dist/game-bundle.iife.js` (the Vite IIFE bundle), then an inline `window.addEventListener('load', …)` reads `#pixi-viewport[data-message]` and calls `initGame('pixi-viewport')` → `renderScene(message)`.
- Ship engine→client data through SSE endpoints + HTTP POST handlers on the raw ASP.NET Core pipeline (see §2).

### Don't (forbidden on the web host)

- ❌ `<script src="/_framework/blazor.webassembly.js" autostart="false">` — no `_framework/` folder exists on the web host.
- ❌ `window.Blazor.start()` — no `window.Blazor` runtime exists.
- ❌ `Microsoft.AspNetCore.Components.CustomElements` / `RegisterCustomElement` / Web Components — requires an interactive Blazor runtime; conflicts with static SSR. Also being retired in .NET 10.
- ❌ Treating `_framework/`/`_content/` as folders to drop into Vite's `public/`. `_content/Game.UI/dist/…` is an RCL static-web-asset path served by ASP.NET Core `MapStaticAssets()`; Vite writes **into** `wwwroot/dist`, not the reverse. `Game.UI.csproj` has a custom `ExcludeViteBundleFromCompression` target for `game-bundle.iife.js` — respect it.

> Note: the only `_framework/blazor.*.js` in the repo is `_framework/blazor.webview.js` in `src/Game.Maui/wwwroot/index.html` — that is the MAUI Blazor **WebView** (Hybrid, not WASM), a different host. The repo name `bonoboengine.blazorwasm` is legacy; the web host is static SSR.

## 2. Boundary — batched render snapshots, never per-frame interop

The boundary concept is **"the simulation produced a render snapshot"**, not "an entity moved." C# is the sole authoritative simulation (ADR-001, ADR-006). Never move simulation back-and-forth through JS interop every frame (ADR-003).

### Do

- **Server → client: SSE.** `GET /api/ecs/stream` and `GET /api/snake/stream` (`text/event-stream`) push batched `SpriteState[]` / snake signal JSON. Client consumes via `new EventSource(streamUrl)` + `addEventListener('sprite-move'/'snake-move', …)` (`src/Game.UI/Frontend/scenes/ecsSprites.ts`, `snake.ts`).
- **Client → server: HTTP POST.** `POST /api/snake/{input,start,restart}` via `fetch(…, { method: 'POST' })` (`snake.ts`). The simulation validates and applies on the next tick — JS only suggests; C# is sole authority.
- **Initial payload: server-rendered attribute.** `GameView.razor` renders the engine payload into `#pixi-viewport[data-message]`; the client `load` script reads it. No circuit, no interop for the initial frame.
- Evolve `SpriteState` (`Id, X, Y, R, G, B`) toward `TransformSnapshot` (velocity/rotation/tick) so the client can interpolate at display Hz (ADR-003 target; not yet implemented).

### Don't (forbidden on the web host)

- ❌ `DotNet.invokeMethodAsync('Assembly', 'Method', …)` from TS — requires a Blazor interactive runtime the web host does not have; also the rejected per-frame anti-pattern (ADR-001, ADR-003).
- ❌ `IJSRuntime` / `JSInvokable` on `Game.Web` — "no `IJSRuntime` calls (no interactivity on the web host)" (`codebase-truth.md`). `IJSRuntime` is the **MAUI Hybrid** path only, never the web host.
- ❌ Per-entity / per-frame JS↔C# calls — 60 FPS interop saturates the marshalling boundary and breaches the frame budget (ADR-001).
- ❌ `StateHasChanged` triggered from a JS-invocable callback — implies an interactive circuit; under static SSR there is no live client component instance to re-render. State mutations flow server→client as SSE deltas.

## 3. TypeScript — strict payload types, no `any`

Type every C#↔JS payload as an explicit TS interface. No `any`.

### Do

- Define SSE/HTTP payload interfaces matching the C# JSON contract: `EcsRenderSignal`, `SpriteState` (`Id, X, Y, R, G, B`), `SnakeRenderSignal`, `SnakeInputRequest`, and `ScenePayload` (`exampleId?, title?, sourceUrl?`).
- Parse defensively: `JSON.parse` into `unknown`, then narrow (the `renderScene` path in `src/Game.UI/Frontend/game.ts` is the model).
- Replace the existing `(window as any)` casts in `src/Game.UI/Frontend/game.ts` (L143–147) with a typed `Window` augmentation so the bundle's global API is type-checked:
  ```ts
  declare global {
    interface Window {
      initGame: (containerId: string) => Promise<void>;
      renderText: (message: string) => void;
      renderScene: (message: string) => Promise<void>;
      togglePixiStats: () => void;
      toggleCSharpStats: () => void;
    }
  }
  ```
- Keep `npm run typecheck` (`tsc -b`, scoped `tsconfig.app.json` + `tsconfig.node.json`) green before any `dotnet build`.

### Don't

- ❌ `any` for interop payloads.
- ❌ Hand-written `.d.ts` mirroring C# types for a `DotNet.invokeMethodAsync` surface — that surface does not exist here; payloads are JSON over SSE/HTTP, so type the JSON shapes, not a .NET method-signature table.

## References

- `docs/ai-agents/codebase-truth.md` — verified facts; the file that wins on disagreement.
- `docs/architecture/topology.md` — three-layer topology, WASM→JS bridge, implementation status (Implemented vs Target).
- `docs/adr/`: ADR-001 (three-layer topology), ADR-003 (render-bridge evolution), ADR-006 (domain responsibility matrix).
- `AGENTS.md` "Architectural Guardrails" — the positive boundary rules; this skill lists the forbidden anti-patterns and the TS typing rule.