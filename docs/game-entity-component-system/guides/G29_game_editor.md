# G29 — Game Editor


> **Category:** Guide · **Related:** [G16 Debugging](./G16_debugging.md) · [G30 Game Feel Tooling](./G30_game_feel_tooling.md) · [E1 Architecture Overview](../architecture/E1_architecture_overview.md)

# Replicating Godot's 2D editor in MonoGame/C#

**ImGui.NET with docking is the proven path to building a Godot-class 2D editor on MonoGame + Arch ECS, requiring roughly 10–12 weeks for a minimum viable editor and 20–24 weeks for a productive one.** Godot's editor comprises approximately 15 major subsystems — from dockable panels and a reflection-driven inspector to a full tilemap editor and animation timeline — most of which have viable C# implementation strategies using existing libraries. The Murder Engine (built on FNA + ImGui + ECS) demonstrates this approach works in production. This document catalogs every significant Godot 2D editor feature and maps it to a concrete MonoGame/C# implementation path with complexity estimates.

---

## Part 1: Godot's editor is its own best advertisement

Godot's editor is built entirely with its own UI framework — the `Control` node system — making the editor itself a Godot application. The central `EditorNode` singleton constructs the entire layout using nested `HSplitContainer`, `VSplitContainer`, and `TabContainer` nodes. This self-hosting approach means every widget in the editor (trees, text fields, color pickers, curve editors) is the same code developers use in their games. The architecture centers on three key singletons: **EditorNode** (UI construction and scene management), **EditorData** (undo/redo, scene metadata), and **EditorInterface** (scripting API for plugin access).

The main window divides into five zones: a **top bar** with menus, main screen tabs (2D/3D/Script/AssetLib), and playtest buttons; **left docks** defaulting to the Scene tree and FileSystem browser; the **center viewport** that switches between 2D, 3D, and Script editing; **right docks** defaulting to Inspector, Node (Signals/Groups), and History; and **bottom panels** for Output, Debugger, Animation, Audio, and Shader editing. Godot 4.6 unified the bottom panel system with the dock system via a new `EditorDockManager`, enabling drag-and-drop between all positions (sides and bottom) with blue outline preview, plus floating windows for multi-monitor setups.

### The Inspector: Godot's most sophisticated subsystem

The Inspector is arguably Godot's most complex editor feature. When a node is selected, `EditorInspector` calls `Object.get_property_list()` to enumerate every property, then `EditorInspector.instantiate_property_editor()` creates the appropriate widget based on the property's `Variant.Type` and `PropertyHint`. The mapping is comprehensive:

- **Numerics** → spinboxes with optional range sliders (controlled by `@export_range`)
- **Booleans** → checkboxes
- **Strings** → line edits, with variants for multiline, file paths (browse button), and enums (dropdown)
- **Colors** → inline swatch opening a full `ColorPicker`
- **Vectors** → multi-component numeric editors
- **Enums** → `OptionButton` dropdowns
- **Curves** → inline `CurveEditor`
- **Resources** → selector with New/Load/Quick Load, expandable inline editing
- **NodePaths** → path selector with scene tree dialog
- **Arrays/Dictionaries** → expandable indexed editors with add/remove/reorder

Properties organize into **categories** (inheritance layers like "Node2D", "CanvasItem"), **groups** (collapsible subsections), and **subgroups**. The `EditorInspectorPlugin` extension API provides hooks at every level: `_parse_begin`, `_parse_category`, `_parse_group`, `_parse_property`, and `_parse_end`, allowing plugins to inject or replace any property editor. Right-click context menus support copy/paste values, copy property paths, and documentation links. Drag-and-drop from the FileSystem dock onto resource properties is supported natively.

### Scene system: composition as the core abstraction

Godot's scene system treats **scenes as the fundamental reusable unit** — equivalent to Unity's prefabs but more flexible because every scene is simultaneously a template and an instance. A scene is a tree of nodes saved as a `.tscn` file (human-readable text format). Scenes compose by instancing other scenes as children, creating arbitrarily deep hierarchies. Scene inheritance allows creating variants that inherit the full node tree from a base scene, with property overrides propagating automatically.

The `.tscn` format (version 3 in Godot 4.x) contains five sections in order: file descriptor with UID, external resource references (`[ext_resource]`), internal sub-resources (`[sub_resource]`), nodes with parent paths and property overrides, and signal connections. Only non-default property values are stored, keeping files compact. **String-based UIDs** (`uid://...`) introduced in Godot 4 enable file renaming/moving without breaking references.

The node lifecycle follows a deterministic order: `_enter_tree()` propagates top-down, `_ready()` propagates bottom-up (children guaranteed initialized before parents), `_process()` and `_physics_process()` run every frame/tick, and `_exit_tree()` propagates top-down on removal. Autoloaded nodes persist across scene changes as children of the root viewport, serving as Godot's singleton pattern.

### 2D-specific tools form a deep feature set

Godot's 2D editor provides **14 distinct tool categories**, each with dedicated editor UI:

**Viewport and transform tools** include pan/zoom navigation, rulers with draggable guides, a ruler measurement tool (R key), configurable grid snap (offset, step, rotation step, scale step), smart snap (to parent, self, other nodes, guides), and four transform modes — Select (Q), Move (W), Rotate (E), Scale (S) — with visual gizmos, axis constraints, and multi-selection support.

**The TileMap editor** (redesigned in Godot 4, with `TileMapLayer` nodes replacing the old `TileMap` in 4.3+) includes atlas-based tileset creation, five painting tools (paint, line, rectangle, bucket fill, eraser), a terrain/autotile system replacing Godot 3's autotile, random scatter painting, per-tile physics/navigation/occlusion polygon editing, per-tile animation, custom data layers, multi-tile batch property editing, and separate layers as individual scene tree nodes with Y-sort and Z-index control. Scene-based tiles allow placing full scenes (chests, NPCs) as tile entries.

**The Animation system** centers on AnimationPlayer, which can animate **any property of any node** via the timeline editor. Track types include Value (property animation with nearest/linear/cubic interpolation), Method Call, Bezier Curve (with dedicated curve editor), Audio, Animation (trigger sub-animations), and Expression tracks. AnimationTree provides visual state machine editing with transition rules, blend trees (Blend2, Blend3, OneShot, TimeScale), and 1D/2D blend spaces with automatic triangulation.

**Physics tools** provide visual editing of CollisionShape2D (rectangle, circle, capsule, segment, with drag handles) and CollisionPolygon2D (vertex placement in viewport), plus auto-generation of collision polygons from sprite outlines via Sprite2D's toolbar menu. Debug draw overlays show all collision shapes and navigation meshes at runtime.

**Additional 2D systems** include GPUParticles2D/CPUParticles2D with ParticleProcessMaterial (emission shapes, velocity curves, color ramps, sub-emitters, turbulence), PointLight2D/DirectionalLight2D with shadow casting via LightOccluder2D, NavigationRegion2D with polygon drawing and bake button, Path2D with Bezier curve editing and PathFollow2D, Polygon2D with UV editing and skeleton bone weights, Line2D with width curves and gradient colors, Camera2D with limits/smoothing/drag margins, and the visual shader editor (node-graph interface with SDF nodes for 2D effects).

### Plugin system enables deep editor extension

The `EditorPlugin` class (requiring the `@tool` annotation) provides an extensive API: `add_control_to_dock()` for custom panels, `add_control_to_bottom_panel()` for bottom tabs, `add_inspector_plugin()` for custom inspectors, `add_import_plugin()` for asset importers, `_forward_canvas_gui_input()` for intercepting 2D editor input, `_has_main_screen()` for adding new main screen modes, and `add_custom_type()` for registering new node types. Container positions cover toolbar, canvas editor sides/bottom, inspector bottom, and project settings tabs. The `@tool` annotation combined with `Engine.is_editor_hint()` enables scripts that run in the editor for live preview, making property changes immediately visible.

---

## Part 2: The MonoGame/C# implementation roadmap

### ImGui.NET with docking is the clear winner for editor UI

After evaluating five UI framework options, **ImGui.NET with the docking branch** emerges as the only practical choice for a solo developer. The docking branch provides drag-and-dock windows, tab groups, split nodes, floating windows, `DockSpace`/`DockBuilder` APIs for programmatic layout, multi-viewport support (dragging windows to separate OS windows), and automatic layout persistence via `imgui.ini`. ImGui.NET wraps all of this fully.

The alternatives each have deal-breaking limitations. **Avalonia UI** offers professional native-desktop feel and the excellent Dock library by wieslawsoltes, but MonoGame integration is community-driven and immature — frame capture via `WriteableBitmap` adds latency, input forwarding between frameworks is complex, and the learning curve (XAML + MVVM + Avalonia internals) is steep. Templates exist (vilten/Avalonia-Monogame-Dock-Template) but are described as "first draft." **Myra** provides useful widgets (PropertyGrid, TreeView, FileDialog) but lacks IDE-style docking entirely — only SplitPane and TabControl. **Gum** excels at game UI layout but "docking" means child-element positioning (like WPF DockPanel), not panel management. WinForms/WPF options are Windows-only.

The **Murder Engine** (github.com/isadorasophia/murder, ~1.5K stars) proves this approach at production scale: a pixel-art ECS game engine on FNA with ImGui for its full editor — map editor, entity composition, animation editing, dialogue system, hot reload. Editor code lives in a separate C# project that references the game, cleanly separable from release builds. This is the single most relevant reference implementation.

### Bridging Arch ECS with a scene hierarchy

The conceptual gap between Godot's OOP node tree and Arch's ECS is bridged by **representing hierarchy as components**. Three approaches exist in the ECS literature:

The **recommended approach** uses flat storage with a `Parent` component (containing a GUID reference) and reconstructs tree views on demand for the editor UI. A `TransformPropagationSystem` topologically sorts entities and computes `WorldTransform = ParentWorldTransform * LocalTransform`. **Arch.Relationships** (v1.0.0 NuGet) provides built-in entity-to-entity relationship support for parent-child links, while **Arch.Persistence** (v2.0.0) handles JSON/binary serialization of entire worlds.

The hierarchy components would look like:

- `Parent { Entity Value }` — on every child entity
- `Children { List<Entity> Value }` — optional, cached for fast iteration
- `LocalTransform { Vector2 Position, float Rotation, Vector2 Scale }` — relative to parent
- `WorldTransform { Matrix2D Matrix }` — computed by propagation system
- `EntityName { string Value }` and `EntityGuid { Guid Value }` — for editor display and serialization

Arch benchmarks at ~204μs for 100 entities with parent/child relationships — more than adequate for an editor. For the scene tree panel, maintain a cached tree structure that rebuilds on structural changes (add/remove/reparent), displayed as an ImGui tree widget.

### Reflection-based property inspector maps directly to ImGui

The inspector is where ImGui shines. The implementation pattern uses `System.Reflection` to enumerate component fields at runtime, dispatching to type-specific ImGui widgets:

- `float` → `ImGui.DragFloat()` or `ImGui.SliderFloat()` with `[Range]` attribute
- `Vector2` → `ImGui.DragFloat2()`
- `bool` → `ImGui.Checkbox()`
- `string` → `ImGui.InputText()`
- `Color` → `ImGui.ColorEdit4()`
- `Enum` → `ImGui.Combo()` populated via `Enum.GetNames()`
- Nested types → collapsible `ImGui.TreeNode()` with recursive inspection

Custom attributes control display: `[Range(min, max)]`, `[Tooltip("...")]`, `[Header("Section")]`, `[HideInInspector]`, `[InspectorCallable]` for method buttons. Register a `Dictionary<Type, Action<string, object>>` for custom type drawers. Cache `FieldInfo` arrays per type since reflection metadata doesn't change. For Arch's struct components, use `world.Get<T>(entity)` to read, draw the inspector, then `world.Set(entity, modified)` to write back. **The Nez framework** demonstrates this pattern with `[InspectorDelegate]`, `[CustomInspector]`, and `[Inspectable]` attributes — a direct reference for API design. Estimated effort: **2–3 weeks**.

### 2D gizmos are simpler than they appear

2D transform gizmos avoid the ray-plane intersection complexity of 3D. The **move gizmo** draws X/Y axis arrows and a center square at the entity position, scaled inversely with camera zoom (`gizmoScale = baseSize / camera.Zoom`) for constant screen size. Hit testing checks mouse proximity to arrow bounding boxes. On drag, compute world-space delta and apply to `LocalTransform`, with optional grid snapping via `Math.Round(value / gridSize) * gridSize`. **Rotation** draws a circle; compute angle delta between consecutive mouse positions relative to the entity center. **Scale** draws corner handles; compute scale factor from distance ratios. Multi-selection transforms relative to a computed pivot point (center of selection bounds). MonoGame.Extended's `OrthographicCamera` provides the essential `ScreenToWorld`/`WorldToScreen` conversions. Estimated effort: **2–3 weeks** for move/rotate/scale, **+1 week** for multi-selection.

### Tilemap editor: the highest-value tool

For 2D game development, a tilemap editor arguably provides **more value per development hour than any other feature**. The implementation requires:

**Auto-tiling via bitmask lookup**: Check 4 cardinal neighbors (4-bit, 16 tiles) or all 8 neighbors (8-bit, 47 unique visual configurations) to select the correct tile variant. The 4-bit approach covers most needs and maps directly to a 16-entry lookup table. The **dual-tilemap technique** (only 5 tiles + rotations) is a newer approach gaining popularity for its dramatically simpler art pipeline.

**Painting tools**: Brush (single tile or brush-size area), Line (Bresenham's algorithm between two points), Rectangle (fill region), Bucket Fill (4-connected flood fill), and Eraser. After each paint operation, recompute auto-tile bitmasks for affected tiles and their neighbors.

**Editor UI**: An ImGui panel showing the tileset as a grid of selectable tiles (rendered as textured quads), layer management with visibility/lock toggles, and tool selection buttons. The tilemap data structure is a simple 2D array per layer, stored as tile indices. MonoGame.Extended's `TiledMapRenderer` handles loading Tiled (.tmx) maps for import but provides no editing API — custom data structures are required. Estimated effort: **3–4 weeks** for painting + 4-bit auto-tiling + layers.

### Animation timeline demands the most development time

The animation system is the **highest-complexity feature** in Godot's editor. A basic implementation requires a data model (`AnimationClip` → `AnimationTrack` → `Keyframe` with time/value/tangents), a horizontal scrollable timeline UI with track rows and diamond-shaped keyframe markers, playback controls (play/pause/scrub), and interpolation (linear, step, cubic Hermite). ImGui provides the drawing primitives (`ImDrawList` for custom rendering) but no timeline widget — the entire UI must be built from scratch. The bezier curve editor with tangent handles is an additional multi-week effort. Estimated effort: **4–6 weeks** for basic timeline, **+3–4 weeks** for curve editing. **Consider deferring this entirely** and using external tools like Aseprite for sprite animation or a simple code-driven animation system.

### Asset pipeline: bypass MGCB in the editor

MonoGame's Content Pipeline (MGCB) requires pre-compilation to .xnb format with no hot-reload support — unsuitable for editor iteration speed. The recommended approach uses **raw file loading in editor mode**: `Texture2D.FromFile(GraphicsDevice, path)` for images, `SoundEffect.FromStream()` for audio, and `System.Text.Json` for data files. Add `FileSystemWatcher`-based change detection (the **MonoGame.Reload** NuGet package wraps this) and reload assets on modification. Store asset references as relative paths resolved at load time. Reserve MGCB for final game builds where platform-specific compression and optimization matter. Estimated effort: **1–2 weeks**.

### Scene serialization with JSON and GUIDs

JSON is the pragmatic format choice — human-readable, diff-friendly for version control, and natively supported via `System.Text.Json`. Each entity gets a persistent GUID (not the runtime Entity ID from Arch, which changes between sessions). The scene file stores entities as objects with their GUID, name, parent GUID, and a dictionary of components serialized as JSON objects. Asset references store relative file paths. **Arch.Persistence** (v2.0.0) provides built-in world serialization with `TextSerializer`/`BinarySerializer` and transformation contexts for handling non-serializable types (textures become asset path strings). Version the format from day one for future migration. Estimated effort: **2–3 weeks**.

---

## The feature-to-implementation mapping

Every significant Godot feature maps to a concrete MonoGame/C# strategy. The following table covers the full scope:

| Godot Feature | MonoGame Implementation | Key Library | Effort | Priority |
|---|---|---|---|---|
| Dockable panel system | ImGui docking branch: `DockSpaceOverViewport()`, `DockBuilder` API | ImGui.NET | 1 week | P0 |
| Inspector (property editor) | Reflection-based component inspector with custom attributes | ImGui.NET + System.Reflection | 2–3 weeks | P0 |
| Scene tree panel | ImGui `TreeNodeEx()` over cached entity hierarchy | ImGui.NET + Arch.Relationships | 1 week | P0 |
| FileSystem browser | ImGui tree + grid view over project directory, `Directory.EnumerateFiles()` | ImGui.NET | 1–2 weeks | P1 |
| Output/console panel | ImGui scrolling text with category filters, capture `Console.WriteLine` | ImGui.NET | 0.5 weeks | P1 |
| Signal/event system | C# events/delegates on components; editor lists available events per entity | Custom | 2 weeks | P2 |
| Script editor | Defer to VS Code/Rider with hot reload; no built-in code editor needed | External IDE | 0 weeks | — |
| Node hierarchy + transforms | `Parent`/`Children`/`LocalTransform`/`WorldTransform` components, propagation system | Arch.Relationships | 2 weeks | P0 |
| Scene composition (instancing) | "Prefab" = scene JSON file; instantiate by deserializing and adding to world | Arch.Persistence + System.Text.Json | 2 weeks | P2 |
| Scene inheritance | Base scene JSON + override JSON merged at load time | Custom | 3 weeks | P3 |
| 2D viewport (pan/zoom/grid) | `OrthographicCamera` from MonoGame.Extended, custom grid renderer | MonoGame.Extended | 1 week | P0 |
| Transform gizmos (move/rotate/scale) | Custom gizmo renderer with hit testing, constant screen-size scaling | Custom + MonoGame.Extended | 3–4 weeks | P0 |
| Grid snapping | `Math.Round(value / gridSize) * gridSize` | Built-in | 0.5 weeks | P1 |
| TileMap editor | Custom tilemap data + ImGui palette + painting tools + bitmask auto-tiling | Custom | 3–4 weeks | P1 |
| Sprite/texture region editor | ImGui image display with rect selection overlay | ImGui.NET | 1 week | P2 |
| Animation timeline | Custom track/keyframe data model + ImGui `ImDrawList` timeline UI | Custom | 4–6 weeks | P3 |
| AnimationTree (state machine) | Node graph via ImGui or imnodes library; state machine with transitions | imnodes + Custom | 4+ weeks | P3 |
| Collision shape editing | Visual polygon/rect/circle editors with vertex handles in viewport | Custom | 2–3 weeks | P2 |
| Particle editor | Expose particle system properties in inspector; real-time viewport preview | ImGui.NET + Custom particles | 3 weeks | P2 |
| Visual shader editor | Node graph UI; defer to text shaders initially | imnodes (if needed) | 6+ weeks | P3 |
| Light2D system | Custom 2D lighting (shadow map or SDF approach); editor shows light radius | Custom | 4+ weeks | P3 |
| Navigation2D | NavMesh generation (Clipper library for polygon ops), visual polygon editing | Clipper2 + BrainAI | 3–4 weeks | P2 |
| Path2D/curve editing | Bezier curve editor with control point handles in viewport | Custom | 2 weeks | P2 |
| Camera2D (limits/smoothing) | MonoGame.Extended camera + custom limit/smoothing logic; editor visualizes bounds | MonoGame.Extended | 1 week | P1 |
| Project settings/input mapping | JSON config file + ImGui settings window; input mapping via Apos.Input | Apos.Input + System.Text.Json | 1–2 weeks | P1 |
| Play/stop from editor | Toggle between edit mode (ECS systems paused, gizmos active) and play mode | Custom | 0.5 weeks | P0 |
| Remote inspection | In-process — editor overlay reads live ECS state directly | ImGui.NET | 0 weeks (built-in) | — |
| Plugin/addon system | C# interface + assembly loading; expose editor APIs via service locator | System.Reflection + Custom | 4+ weeks | P3 |
| Undo/redo | Command pattern: `ICommand { Execute(), Undo() }` with stack | Custom | 2 weeks | P1 |
| Asset hot-reload | `FileSystemWatcher` + `Texture2D.FromFile()` reload on change | MonoGame.Reload | 1–2 weeks | P2 |
| Scene serialization | JSON with GUIDs, asset path references, format versioning | System.Text.Json + Arch.Persistence | 2–3 weeks | P0 |

---

## Architecture: game-first with ImGui overlay

The recommended architecture is **Model C: game-first with editor overlay**. The game is the main application; ImGui renders editor panels on top. Toggling between edit mode (ECS systems paused, gizmo rendering enabled, ImGui panels active) and play mode (full game execution, ImGui hidden or minimal) requires only a boolean flag and conditional system execution. This is what Murder Engine does, what Nez does, and what most successful indie engine-editors use.

**Project structure** should follow Murder's pattern: a `Game` project (pure game code, no editor references), an `Editor` project (ImGui panels, gizmos, inspector, all editor logic), and a `Shared` project (ECS components, systems, data types used by both). The editor project references the game project but not vice versa. Release builds exclude the editor project entirely. This separation is critical — editor code should never leak into shipped games.

The ImGui integration renders the game to a texture (via `RenderTarget2D`), then displays that texture inside an ImGui window using `ImGui.Image()`. This makes the game viewport a dockable panel alongside the hierarchy, inspector, and console. Input routing checks `ImGui.GetIO().WantCaptureMouse` / `WantCaptureKeyboard` to determine whether ImGui or the game viewport should receive input.

### What's uniquely free in this architecture

Several Godot features that require complex implementation in a standalone editor come **for free** with the in-game overlay approach. Remote scene inspection is unnecessary because the editor reads live ECS state directly. There's no inter-process communication to build. Play/edit mode switching is trivial. And the game viewport in the editor is guaranteed pixel-identical to the shipped game because it is the same renderer.

---

## Realistic scope for a solo developer

The single most important insight from experienced solo developers: **put as much in code as possible**. Karl Zylinski (CAT & ONION) advises that "a procedure with some parameters for creating certain types of game objects is often better than an editor." Build editor features only when they provide clear productivity gains over code-only workflows.

**Phase 1 — Minimum Viable Editor (~10–12 weeks)**: ImGui docking setup, scene hierarchy panel, reflection-based property inspector, 2D viewport with camera pan/zoom, entity selection (click and box select), move gizmo, JSON scene save/load, basic asset loading, edit/play mode toggle. This gets you a functional editor where you can visually place and configure entities.

**Phase 2 — Productive Editor (~10–12 additional weeks)**: Rotate/scale gizmos, grid snapping, undo/redo (command pattern), tilemap painting with 4-bit auto-tiling, tileset palette, copy/paste entities, asset browser panel, output console, camera limit visualization, project settings panel. This is where the editor starts saving significant time versus code-only workflows.

**Phase 3 — Advanced Features (3–6 months each, defer aggressively)**: Animation timeline, 8-bit auto-tiling, curve editor, prefab/scene instancing, collision polygon editor, particle editor, visual shader editor, navigation mesh editing, plugin system. Most of these can be substituted with external tools (Aseprite for animation, Tiled for complex tilemaps, text files for shaders) or code-only approaches indefinitely.

The **tilemap editor** and **property inspector** deliver the highest value-per-effort for 2D game development. The animation timeline delivers high value but at very high cost — external tools are the pragmatic choice until the editor is mature. Scene inheritance and the plugin system are organizational luxuries that a solo developer can simulate with C# inheritance and simple composition patterns.

---

## Conclusion: the 80/20 path forward

Godot's editor encompasses roughly **200 person-years of development** across 15+ major subsystems. Replicating it fully is neither possible nor necessary for a solo developer. The critical insight is that **80% of the productivity value comes from 5 features**: dockable panel layout (ImGui docking, 1 week), property inspector (reflection + ImGui, 2–3 weeks), scene hierarchy (ImGui tree, 1 week), 2D viewport with gizmos (MonoGame.Extended camera + custom gizmos, 3–4 weeks), and scene serialization (JSON + Arch.Persistence, 2–3 weeks). These five features total roughly 10–12 weeks and transform a code-only workflow into a visual one.

The technology stack — **MonoGame + Arch ECS + ImGui.NET (docking branch) + MonoGame.Extended + Arch.Persistence + System.Text.Json** — is proven by Murder Engine and provides every building block needed. Arch.Relationships handles entity hierarchy, Arch.Persistence handles world serialization, MonoGame.Extended provides the 2D camera, and ImGui.NET provides the entire editor UI layer with production-ready docking. The remaining user libraries (Gum for game UI, BrainAI for AI/pathfinding, Apos.Input for input, FontStashSharp for text) stay in the game layer and need no editor-specific integration beyond inspector support.

Start with Phase 1. Ship a game using the MVP editor. Add Phase 2 features driven by pain points encountered during actual game development. Defer Phase 3 indefinitely unless a specific feature becomes a clear bottleneck. The goal is making games, not making engines.

---

## Implementation Notes from MonoGameStudio

> These notes come from building MonoGameStudio — a 134-file 2D game editor (v0.1–v0.9) using MonoGame + Arch ECS + Hexa.NET.ImGui. The editor was built, the knowledge captured, and the source deleted. See [E8 — MonoGameStudio Post-Mortem](../architecture/E8_monogamestudio_postmortem.md) for the full story.

### Hexa.NET.ImGui specifics (not ImGui.NET)

**Hexa.NET.ImGui** (2.2.9) was chosen over ImGui.NET for active development and better .NET 10 support. The API surface is similar but has key differences:

**Texture protocol**: Backends must set the `RendererHasTextures` flag and process `ImTextureStatus.WantCreate/WantUpdates/WantDestroy` each frame. `ImTextureRef` replaces `IntPtr` for texture IDs — construct with `new ImTextureRef(null, texId)`. When reading draw commands, use `drawCmd->TexRef.GetTexID()` instead of `drawCmd.TextureId`.

**DockBuilder**: Functions are exposed as `ImGuiP.DockBuilder*()` (internal/private API surface). `ImGuiP.DockBuilderSplitNode()` takes `uint*` pointers — requires `unsafe` block.

**MenuItem ambiguity**: `MenuItem(label, null, ref bool)` is ambiguous between overloads — cast the null to `(string?)null` to resolve.

**Font loading**: `ImFontConfig` fields `MergeMode` and `PixelSnapH` are `byte` not `bool` — use `1`/`0`. `PushFont(font, size)` requires a size parameter, not just the font handle.

**IniFilename**: `ImGuiIOPtr.IniFilename` is read-only. Set via `io.Handle->IniFilename` with a stable native string (pin it or allocate with `Marshal.StringToHGlobalAnsi`).

**Key enum changes**: `ImGuiKey._0` is now `ImGuiKey.Key0`.

### Arch ECS 2.x specifics

**No EntityReference**: Arch 2.x doesn't include an `EntityReference` type. Build a custom `EntityRef` struct wrapping an Entity + generation counter, and always check `world.IsAlive(entity)` before access.

**Query iteration safety**: Never add/remove components inside an Arch query iteration (`world.Query()`). Collect entities to modify into a list, then apply changes after the query completes.

**Signature type**: `entity.GetComponentTypes()` returns `Arch.Core.Signature` — iterate with `foreach`, not array indexing.

**Namespace clashes**: Arch.Core has its own `ComponentRegistry` — use a `using` alias: `using ComponentRegistry = MonoGameStudio.Core.Serialization.ComponentRegistry;`. Similarly, `Arch.Core.World` clashes with any custom `World` namespace — fully qualify `Arch.Core.World` in serialization files.

### OpenGL Y-flip for RenderTarget2D in ImGui

When rendering the game scene to a `RenderTarget2D` and displaying it as an ImGui image, OpenGL's inverted texture coordinates cause an upside-down image. Fix by passing `uv0=(0,1), uv1=(1,0)` to `ImGui.Image()`:

```csharp
ImGui.Image(textureRef, size, new Vector2(0, 1), new Vector2(1, 0));
```

### Update/Draw loop order

The full frame cycle for an editor-over-game architecture:

```
Update():
  1. Input polling (keyboard, mouse state)
  2. ImGui NewFrame
  3. Input routing: ImGui capture → Gizmo interaction → Selection → Camera
  4. Editor panels update (all 17 panels call their Update/Draw ImGui code)
  5. If Play mode: ECS systems update (transforms, physics, animation, particles, etc.)
  6. Transform propagation (Local→World, recursive)

Draw():
  1. Set RenderTarget2D (game scene)
  2. Draw game world (sprites, tilemaps, particles via SpriteBatch)
  3. Draw gizmo overlays, selection rects, collider visualization
  4. Unset RenderTarget2D
  5. ImGui render (panels reference the RenderTarget2D as a texture)
  6. ImGui draw data → GPU
```

### Input routing priority

In Edit mode, input follows a strict priority chain:

1. **ImGui capture** — if `ImGui.GetIO().WantCaptureMouse` or `WantCaptureKeyboard`, ImGui consumes the input (panels, menus, text fields)
2. **Gizmo interaction** — if mouse is over a gizmo handle, gizmo system captures drag
3. **Selection** — click selects entity, Ctrl+click toggles multi-select, drag creates box select
4. **Camera** — middle mouse button pans, scroll wheel zooms

Each layer checks whether a higher-priority layer consumed the input before acting.

### Scene serialization: two-pass GUID linking

Scene files use JSON with `System.Text.Json`. Each entity has a persistent GUID (not the runtime Arch Entity ID, which changes every session). The serialization pattern:

**Save**: Iterate all entities → for each, serialize EntityGuid, EntityName, parent GUID (if any), and all registered components into a `SceneData` DTO → write JSON.

**Load (two-pass)**:
1. **Pass 1**: Deserialize JSON → create all entities in the Arch world → attach components → build a `Dictionary<Guid, Entity>` lookup
2. **Pass 2**: For each entity with a parent GUID → look up the parent Entity in the dictionary → set the `Parent` component

This two-pass approach avoids ordering dependencies — entities can reference parents that appear later in the file.

### Component descriptor pattern

`ComponentDescriptor<T>` provides typed `Has/Get/Set/Add/Remove` operations on entities without runtime reflection. The interface `IComponentDescriptor` allows generic code to work across component types:

```csharp
interface IComponentDescriptor {
    Type ComponentType { get; }
    string Category { get; }
    bool Has(World world, Entity entity);
    object Get(World world, Entity entity);
    void Set(World world, Entity entity, object value);
    void Add(World world, Entity entity);
    void Remove(World world, Entity entity);
}
```

Each built-in component registers a `ComponentDescriptor<T>` at startup. The inspector iterates all descriptors, calls `Has()` to find which components an entity has, then `Get()` to read values and `Set()` to write them back. `FieldDescriptor` wraps individual struct fields with compiled delegates for fast get/set.

For user-defined components from game project assemblies, `ExternalComponentLoader` + `DynamicComponentDescriptor` use reflection to create descriptors at runtime.

### macOS native integration

macOS platform integration uses direct Objective-C interop (`objc_msgSend`) via P/Invoke in `ObjCRuntime.cs` — no third-party binding libraries needed. This approach works with MonoGame's SDL2 window handle:

- **File dialogs**: `NSOpenPanel`/`NSSavePanel` via `ObjCRuntime` calls — wrapped behind `IFileDialogService` with a `FallbackFileDialogService` for non-macOS
- **Menu bar**: Native `NSMenu` construction for File/Edit/View/Help with keyboard shortcut equivalents
- **Title bar**: Custom `NSWindow` titlebar styling + traffic light (close/minimize/zoom) repositioning
- **Toolbar**: Native `NSToolbar` with play/pause/stop buttons

The key insight: you don't need Xamarin or MAUI for native macOS integration — raw `objc_msgSend` interop is sufficient for file dialogs, menus, and window chrome. Wrap each platform feature behind an interface so non-macOS platforms get a fallback implementation.