# Project Brief — Bonobo Engine (blazorwasm)

## Mission

Build a **2D game engine stack** where the entire game simulation lives in pure C#, runnable
client-side today (Blazor Web App + .NET MAUI Blazor Hybrid) and on a dedicated .NET Linux
server tomorrow for authoritative multiplayer — with **zero duplicate work** between the two.

## Core Idea

Isolate the **Simulation Layer (C#)** from the **Presentation Layer (PixiJS / Tailwind / Blazor)**:

- `Game.Engine` — authoritative simulation: game state, deterministic ticks, command/event
  system. Knows nothing about graphics, browsers, or UI.
- `Game.UI` — shared Razor Class Library: Blazor component shell + IJSRuntime bridge that
  maps C# state-change events to PixiJS sprites; Tailwind for HUD/menus.
- `Game.Web` — Blazor Web App host (**static SSR only** + SSE `/api/ecs/stream`; no Interactive Server).
- `Game.Maui` — .NET MAUI Blazor Hybrid host (Android default; iOS, Mac Catalyst, Windows
  when OS/workloads allow).
- Future: ASP.NET Core server host running the exact same engine, verifying client commands
  and broadcasting events over WebSockets/SignalR.

## The Performance Gold Rule

**Never poll C# from JS, never serialize the whole state tree per frame, never move simulation back-and-forth through JS interop every frame.** Cross the C#↔JS boundary only via **batched render snapshots** (ADR-003): the engine emits a `TransformSnapshot` (pos + velocity + rotation + tick); the client interpolates `P_render = P_prev + (P_curr − P_prev) × α` at display Hz. C# is the sole authority; PixiJS is a pure mirror that may run optional presentation physics (Rapier) for visual dynamics only. See `docs/architecture/topology.md` and ADR-001…ADR-006.

## Scope (MVP)

1. Command/event boundary in `Game.Engine` (records like `EntityMovedEvent`,
   `GameSimulation.ProcessCommand(...)`).
2. Skinny Blazor→PixiJS bridge in `Game.UI` (subscribe to engine events, forward deltas).
3. PixiJS v8 canvas bootstrapped from `Frontend/game.ts` (already working: hello-world text).
4. Same shared UI running in both `Game.Web` and `Game.Maui`.

## Sources of Truth

- `docs/index.md` — architecture and stack source of truth (wins over prose elsewhere).
- `AGENTS.md` — build commands, repo shape, agent workflow rules.
- `.csproj`, `.slnx`, `package.json` — trust these over setup prose in `README.md`.

## Repository

- Origin: https://github.com/josejaviercanon/bonoboengine.blazorwasm.git
- Branch: `main`
- Solution: `bonoboWebGame.slnx` (4 projects under `/src/`).
