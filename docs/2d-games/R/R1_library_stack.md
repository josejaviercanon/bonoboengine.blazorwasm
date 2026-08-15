# R1 — Library Stack & Install Commands
> **Category:** Reference · **Related:** [E1 Architecture Overview](../E/E1_architecture_overview.md) · [R2 Capability Matrix](./R2_capability_matrix.md) · [R3 Project Structure](./R3_project_structure.md)

---

## Tier 0: Core Architecture (always included)

The simulation core is a pure .NET 10 class library with **zero** UI/platform dependencies. Arch ECS is vendored as **source** under `src/Arch/` (core, systems, event bus, persistence, relationships, AOT source generator) — it is **not** a NuGet package — and linked into `Game.Engine` via `ProjectReference`. Source generation is wired through `src/Arch.Generators/` (a `netstandard2.0` Roslyn analyzer pack) referenced from `Game.Engine` as an analyzer.

| Concern | Provider | Location |
|---------|----------|----------|
| Simulation runtime | .NET 10 (`net10.0`) | `src/Game.Engine/Game.Engine.csproj` |
| ECS (core + systems) | Arch — vendored source | `src/Arch/Arch.csproj` → `ProjectReference` |
| System / AOT source gen | Arch source generators | `src/Arch.Generators/` (analyzer, `OutputItemType="Analyzer"`) |
| Serialization | System.Text.Json (built-in .NET) + source generators | no package |

```xml
<!-- src/Game.Engine/Game.Engine.csproj (relevant refs) -->
<ItemGroup>
  <ProjectReference Include="..\Arch\Arch.csproj" />
</ItemGroup>
<ItemGroup>
  <Analyzer Include="..\Arch.Generators\Arch.Generators.csproj" />
</ItemGroup>
```

> **No NuGet for Arch.** Do not `dotnet add package Arch` — this repo vendors the source. Update Arch by pulling upstream into `src/Arch/`.
> **AOT:** Arch's AOT source generator ships through `Arch.Generators`; no separate `Arch.AOT.SourceGenerator` package is required.
> **No native game-framework runtime.** There is no native game-framework reference anywhere. The core has no `Game` class, no `GraphicsDevice`, no `Content.Load<T>`.

---

## Tier 1: Presentation Layer (Blazor + PixiJS + Tailwind)

The presentation layer is a shared Razor Class Library (`src/Game.UI`) that references `Game.Engine` and owns the PixiJS frontend plus the Blazor component shell. It is a pure mirror of C# state — never authoritative.

| Concern | Provider | Version | Notes |
|---------|----------|---------|-------|
| 2D rendering | PixiJS | ^8.19.0 | WebGL/WebGPU; bundled by Vite → `wwwroot/dist` |
| JS build | Vite + TypeScript | ^8.2.1 / ^7.0.2 | IIFE lib bundle (`Frontend/game.ts`) |
| UI components | Blazor (`Microsoft.AspNetCore.Components.Web`) | 10.0.10 | HUD, menus, inventories |
| CSS / theme | Tailwind CSS v4 (`@tailwindcss/cli`) | ^4.3.3 | responsive HUD/menus |
| Fonts / text | PixiJS Text + web fonts | — | no runtime font library |

```bash
# Frontend deps live in src/Game.UI/package.json, not in the .NET project
cd src/Game.UI
npm ci
npm run build     # Vite JS build, then Tailwind CLI → wwwroot/dist
```

> **Push-based bridge:** `Game.UI` forwards engine delta events to PixiJS via `IJSRuntime` (flat payloads, never per-frame polling). See `docs/index.md` "Performance Gold Rule".
> **No `Content.Load<T>` / MGCB.** Assets are bundled by Vite and resolved client-side by PixiJS by key/id.

---

## Tier 2: Hosts & Future Server

| Concern | Provider | Notes |
|---------|----------|-------|
| Web host | `src/Game.Web` — Blazor Web App (static SSR) | discovers shared RCL routes via `AddAdditionalAssemblies` |
| Native host | `src/Game.Maui` — .NET MAUI Blazor Hybrid | Android default; iOS/MacCatalyst/Windows conditional TFMs |
| Future server | ASP.NET Core + WebSockets/SignalR | runs the **same** `Game.Engine` authoritatively (phase 2) |
| Networking (future) | ASP.NET Core SignalR / raw WebSockets | web transport — not a native UDP library |

```bash
dotnet watch --project src/Game.Web      # run web host
dotnet build src/Game.Maui               # MAUI (requires MAUI workloads)
```

---

## Tier 3: Optional / Engine-Agnostic C# Libraries

C# libraries with **no** native game-framework dependency that you may vendor or add **inside `Game.Engine`** (never in the presentation layer) when a feature is needed. They are optional and not part of the MVP.

| Concern | Library | Notes |
|---------|---------|-------|
| ECS world serialization | Arch.Persistence (vendored in `src/Arch/`) | save/load entire worlds |
| Entity relationships | Arch.Relationships (vendored) | party/squads/parent-child |
| AI (FSM/BT/GOAP) | BrainAI (GitHub source) | engine-agnostic C#; vendor as source |
| Pathfinding | Roy-T.AStar (NuGet) | standalone A*; no framework dependency |
| Coroutines | Ellpeck/Coroutine (NuGet) | Unity-style yield; C# only |
| Debug overlays | browser DevTools / Blazor | no ImGui needed on web |

```bash
# Roy-T.AStar: dotnet add package RoyT.AStar
# BrainAI: clone from GitHub, vendor as source inside src/Game.Engine
```

> **Rule:** any C# library added for simulation must stay inside `Game.Engine` and remain free of UI/platform deps. Presentation concerns (audio, rendering, input) are handled client-side by PixiJS/Blazor, not by C# libraries.

---

## Serialization Note

**System.Text.Json** with source generators replaces both Newtonsoft.Json and Nez.Persistence. It's built into .NET — no package needed. Use `[JsonSerializable]` attributes for AOT-compatible serialization.

If you specifically need Newtonsoft.Json for compatibility: `dotnet add package Newtonsoft.Json --version 13.0.3`

---

## Custom Code (No Package Needed)

These are written as part of your project. ~1,000 lines total, ~14.5 hours of work. See [G1 Custom Code Recipes](../G/G1_custom_code_recipes.md) for implementation.

| Module | ~Lines |
|--------|--------|
| Scene manager | 150 |
| Render layer system | 200 |
| SpatialHash broadphase | 80 |
| Collision shapes (AABB, circle, polygon) | 150 |
| Tween system | 100 |
| Screen transitions | 100 |
| Post-processor pipeline | 150 |
| Object pool | 30 |
| Line renderer | 50 |

---

## Platform Hosts (no native game-framework runtime)

The engine ships no native game-framework runtime. Mobile/desktop targets are provided by the .NET MAUI Blazor Hybrid host (`src/Game.Maui`); the web target is the Blazor Web App host (`src/Game.Web`). There is no MGCB content pipeline and no platform-specific game framework package.

| Host | TFM | Notes |
|------|-----|-------|
| `src/Game.Web` | `net10.0` | Blazor Web App, static SSR; serves the shared RCL + PixiJS bundle |
| `src/Game.Maui` | `net10.0-android` (default) | MAUI Blazor Hybrid; adds `net10.0-ios`/`-maccatalyst`/`-windows10.0.19041.0` when OS/workloads allow |

```bash
# MAUI workloads must be installed for non-Android TFMs
dotnet workload restore
dotnet build src/Game.Maui                 # Android by default
dotnet build src/Game.Maui -t:Run -f net10.0-ios   # iOS sim/device (Apple Silicon)
```

> **No native iOS game framework / content pipeline.** Assets are bundled by Vite (`src/Game.UI/wwwroot/dist`) and served as static web assets; the `BlazorWebView` loads the same RCL components as the web host. Set `SupportedOSPlatformVersion` per MAUI guidance (e.g. `15.0` for iOS) in `Game.Maui.csproj`.

See [R3 Project Structure](./R3_project_structure.md) for the actual repo layout.
