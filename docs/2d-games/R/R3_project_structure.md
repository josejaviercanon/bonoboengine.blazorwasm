# R3 — Project Structure
> **Category:** Reference · **Related:** [R1 Library Stack](./R1_library_stack.md) · [E1 Architecture Overview](../E/E1_architecture_overview.md)

---

## Solution Layout

```
bonoboengine.blazorwasm/
├── bonoboWebGame.slnx              # .NET 10 XML solution
├── src/
│   ├── Game.Engine/                # Pure C# class lib (net10.0) — authoritative simulation
│   │   ├── Game.Engine.csproj      # refs Arch.csproj + Arch.Generators (analyzer)
│   │   ├── Components/             # Pure data structs (Position, Velocity, BulletData...)
│   │   ├── Systems/                 # Arch systems (MovementSystem, CollisionSystem...)
│   │   ├── Tags/                    # Zero-size markers (PlayerTag, EnemyTag, ProjectileTag...)
│   │   ├── Events/                  # Delta event types emitted to the presentation layer
│   │   ├── Commands/                # Input/action command types consumed by ProcessCommand
│   │   └── (GameSimulation.cs)      # World lifecycle, tick pump, ProcessCommand, events out
│   │
│   ├── Game.UI/                     # Shared Razor Class Library — refs Game.Engine
│   │   ├── Frontend/                # PixiJS/TypeScript source (Vite entry: game.ts)
│   │   ├── wwwroot/dist/            # Generated JS/CSS — DO NOT hand-edit
│   │   ├── GameView.razor           # @page "/", full-viewport PixiJS container + HUD
│   │   └── Game.UI.csproj
│   │
│   ├── Game.Web/                    # Blazor Web App host (static SSR)
│   │   ├── Components/              # App.razor, Routes.razor (AdditionalAssemblies)
│   │   ├── Program.cs               # MapStaticAssets, AddInteractiveServerComponents
│   │   └── Game.Web.csproj
│   │
│   ├── Game.Maui/                   # .NET MAUI Blazor Hybrid host
│   │   └── Game.Maui.csproj          # net10.0-android default; iOS/MacCat/Windows conditional
│   │
│   ├── Arch/                        # Vendored Arch ECS source (net10.0, T4 templates)
│   │   └── Arch.csproj
│   └── Arch.Generators/             # Roslyn analyzer pack (links Arch source generators)
│       └── Arch.Generators.csproj   # netstandard2.0, OutputItemType="Analyzer"
│
└── docs/
    ├── index.md                     # Architecture source of truth
    ├── 2d-games/                    # Bonobo-aligned + engine-agnostic concept toolkit
    ├── game-development/            # Engine-agnostic subset
    └── game-entity-component-system/# Mirrored toolkit (guides/ + reference/) + Bonobo ECS rules
```

---

## Key Principles

**`Game.Engine` holds the entire authoritative simulation** with zero UI/platform dependencies. Hosts (`Game.Web`, `Game.Maui`) are thin bootstrappers; the shared presentation lives in `Game.UI`.

**`Game.Engine/Components/` is for Arch-specific data.** Components are pure `struct` value types (no methods). `Systems/` are stateless Arch query systems. `Tags/` are zero-size markers.

**`Game.Engine/Systems/` is for game logic.** Inventory, dialogue, crafting, combat — custom C# modules that run inside the simulation tick. They must never touch `IJSRuntime`, Blazor, or platform APIs.

**`Game.UI/` is the presentation layer.** Blazor components + Tailwind for HUD/menus; PixiJS (bundled by Vite to `wwwroot/dist`) for the 2D canvas. It is a pure mirror of engine state via push-based delta events.

**`Frontend/` is the asset pipeline.** Vite bundles sprites/audio/data into `wwwroot/dist`; there is no MGCB and no `Content.Load<T>`. Assets are resolved client-side by PixiJS by key/id.

---

## Host Platforms

The engine ships two hosts that load the same shared `Game.UI` Razor Class Library. There is no per-platform game class, no `UIApplicationDelegate`, and no raw iOS bootstrapping — .NET MAUI handles the native shell.

### Game.Web (Blazor Web App)

Static SSR host. `Program.cs` wires `MapStaticAssets()`, interactive server components, and discovers shared RCL routes via `AddAdditionalAssemblies(typeof(GameView).Assembly)`. PixiJS is bootstrapped client-side from `Components/App.razor` (inline `load`-event script reading `#pixi-viewport[data-message]`).

### Game.Maui (.NET MAUI Blazor Hybrid)

Uses `<UseMaui>true</UseMaui>` + a `BlazorWebView`. Targets `net10.0-android` by default; `net10.0-ios`/`-maccatalyst`/`-windows10.0.19041.0` are added conditionally when OS/workloads allow. XAML source-gen inflator is enabled; Windows is unpackaged.

> **No native game-framework reflection.** MAUI provides the native window, lifecycle, and input. There are no `TrimmerRootAssembly` reflection hacks, no display-link patching, and no platform-specific game-framework package references.

### Game.Maui.csproj (excerpt)

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
  <PropertyGroup>
    <TargetFrameworks>net10.0-android</TargetFrameworks>
    <OutputType>Exe</OutputType>
    <UseMaui>true</UseMaui>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Game.UI\Game.UI.csproj" />
  </ItemGroup>
</Project>
```

iOS/MacCatalyst/Windows TFMs are appended to `TargetFrameworks` when the host OS and MAUI workloads support them. Set `SupportedOSPlatformVersion` (e.g. `15.0` for iOS) per MAUI guidance. There is no MGCB content reference and no platform-specific game-framework package — assets ship as static web assets under `Game.UI/wwwroot/dist`.

### Build & Run

```bash
# Web host
dotnet watch --project src/Game.Web

# MAUI (Android default; install MAUI workloads for other TFMs)
dotnet build src/Game.Maui
dotnet build src/Game.Maui -t:Run -f net10.0-android

# iOS sim/device (Apple Silicon, requires MAUI iOS workloads)
dotnet build src/Game.Maui -t:Run -f net10.0-ios -r iossimulator-arm64
```

> **Frontend first.** Run `npm run build` in `src/Game.UI` before `dotnet build` so static web assets are present. Never run multiple `dotnet` commands concurrently (static-web-asset compression can race).
> **Generated output.** `wwwroot/dist` is produced by Vite/Tailwind — never hand-edit or commit it. iOS launch screens, Info.plist, and orientation settings are handled by MAUI's platform conventions, not hand-authored storyboard/plist files.
