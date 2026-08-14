# R1 — Library Stack & Install Commands


> **Category:** Reference · **Related:** [E1 Architecture Overview](../architecture/E1_architecture_overview.md) · [R2 Capability Matrix](./R2_capability_matrix.md) · [R3 Project Structure](./R3_project_structure.md)

---

## Tier 0: Core Architecture (always install)

| Package | Version | Purpose |
|---------|---------|---------|
| MonoGame.Framework.DesktopGL | Latest | Base framework (from template) |
| Arch | 2.1.0 | High-performance archetype ECS |
| Arch.System | 1.1.0 | Base system classes |
| Arch.System.SourceGenerator | Latest | Auto-gen system boilerplate |

```bash
dotnet add package Arch --version 2.1.0
dotnet add package Arch.System --version 1.1.0
dotnet add package Arch.System.SourceGenerator
```

> **Mobile AOT:** For iOS/Android, also add `Arch.AOT.SourceGenerator` for AOT-compatible system codegen:
> `dotnet add package Arch.AOT.SourceGenerator`

> **`Arch.Extended` doesn't exist** as a package. Use individual packages: `Arch.System`, `Arch.EventBus`, `Arch.Persistence`, `Arch.Relationships`, etc.

> **Core project setup:** `MonoGame.Framework.DesktopGL` in the Core project should use `PrivateAssets=all` — it's a compile-only reference. The Desktop launcher project provides the actual runtime. This allows iOS to substitute `MonoGame.Framework.iOS` without conflicts.

---

## Tier 1: Essential Infrastructure (install for any game)

| Package | Version | Purpose |
|---------|---------|---------|
| MonoGame.Extended | 5.3.1 | Camera, Tiled maps, collision shapes, math |
| MonoGame.Extended.Content.Pipeline | 5.3.1 | Tiled/atlas/Aseprite content importers |
| Gum.MonoGame | Latest | UI framework (MonoGame's official recommendation) → [G5](../guides/G5_ui_framework.md) |
| Apos.Input | 2.5.0 | Input handling with JustPressed tracking → [G7](../guides/G7_input_handling.md) |
| FontStashSharp.MonoGame | 1.3.7 | Runtime .ttf/.otf rendering at any size |
| Aether.Physics2D | 2.2.0 | Box2D-style rigid body physics → [G3](../guides/G3_physics_and_collision.md) |
| MonoGame.Aseprite | 6.3.1 | Direct .ase/.aseprite sprite import → [G8](../guides/G8_content_pipeline.md) |

```bash
dotnet add package MonoGame.Extended --version 5.3.1
dotnet add package MonoGame.Extended.Content.Pipeline --version 5.3.1
dotnet add package Gum.MonoGame
dotnet add package Apos.Input --version 2.5.0
dotnet add package FontStashSharp.MonoGame --version 1.3.7
dotnet add package Aether.Physics2D --version 2.2.0
dotnet add package MonoGame.Aseprite --version 6.3.1
```

---

## Tier 2: Genre-Specific (install when needed)

| Package | Purpose | Genres / Trigger |
|---------|---------|-----------------|
| LiteNetLib | Reliable UDP networking | Fighting (rollback), RTS, co-op → [G9](../guides/G9_networking.md) |
| FmodForFoxes + FmodForFoxes.Desktop | FMOD audio engine | Rhythm games, advanced audio → [G6](../guides/G6_audio.md) |
| Arch.Persistence | ECS world serialization | Sandbox, sim, any with save/load |
| Arch.Relationships | Entity-to-entity relationships | RPG (party members), RTS (squads) |
| ImGui.NET | Debug overlays and console | Development/debugging |
| Coroutine (Ellpeck) | Unity-style coroutines | Sequential async logic |
| MLEM + MLEM.Data | Text formatting, non-XNB content | Text-heavy games, custom content |

```bash
dotnet add package LiteNetLib
dotnet add package FmodForFoxes
dotnet add package FmodForFoxes.Desktop
dotnet add package Arch.Persistence
dotnet add package Arch.Relationships
dotnet add package ImGui.NET
dotnet add package Coroutine
dotnet add package MLEM
dotnet add package MLEM.Data
```

---

## Tier 3: Nice-to-Have (optional)

| Package/Repo | Purpose | Notes |
|-------------|---------|-------|
| MonoGame.Penumbra.DesktopGL / .WindowsDX | 2D lighting with soft shadows | v3.0.0, both platforms |
| MonoGame-Mojo | 2D lighting/shadows/normal mapping | GitHub source only (not on NuGet) |
| Roy-T.AStar | Standalone A* pathfinding | NuGet, no framework dependency |
| BrainAI | FSM, BT, GOAP, Utility AI, pathfinding, influence maps | GitHub source reference → [G4](../guides/G4_ai_systems.md) |
| Apos.Tweens | Fluent tweening API | Alternative to custom tweens |

```bash
# BrainAI: clone from GitHub, add as source reference or local package
# Roy-T.AStar: dotnet add package RoyT.AStar
```

---

## Serialization Note

**System.Text.Json** with source generators replaces both Newtonsoft.Json and Nez.Persistence. It's built into .NET — no package needed. Use `[JsonSerializable]` attributes for AOT-compatible serialization.

If you specifically need Newtonsoft.Json for compatibility: `dotnet add package Newtonsoft.Json --version 13.0.3`

---

## Custom Code (No Package Needed)

These are written as part of your project. ~1,000 lines total, ~14.5 hours of work. See [G1 Custom Code Recipes](../guides/G1_custom_code_recipes.md) for implementation.

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

## Platform-Specific Packages

### iOS

| Package | Version | Purpose |
|---------|---------|---------|
| MonoGame.Framework.iOS | 3.8.* | iOS/Metal runtime |
| MonoGame.Content.Builder.Task | 3.8.* | MGCB content pipeline |

```xml
<ItemGroup>
  <PackageReference Include="MonoGame.Framework.iOS" Version="3.8.*" />
  <PackageReference Include="MonoGame.Content.Builder.Task" Version="3.8.*" />
</ItemGroup>

<ItemGroup>
  <MonoGameContentReference Include="..\MyGame.Core\Content\MyGame.mgcb" />
</ItemGroup>
```

**Workload setup (macOS):**

```bash
dotnet workload restore                     # Auto-install from .csproj TFMs
sudo xcode-select -s /Applications/Xcode.app/Contents/Developer
```

**Note:** `MonoGame.Framework.iOS` 3.8.4.1 targets `net8.0-ios18.0` but works with `net10.0-ios` projects. Set `SupportedOSPlatformVersion` to `15.0` in .csproj.

> **TrimmerRootAssembly:** If using reflection to access MonoGame internals (e.g., ProMotion 120Hz `CADisplayLink` patching), add `<TrimmerRootAssembly Include="MonoGame.Framework" />` to the iOS .csproj. Without this, the IL trimmer strips private fields that reflection needs.

See [R3 Project Structure](./R3_project_structure.md) for the full iOS project layout and AppDelegate pattern.
