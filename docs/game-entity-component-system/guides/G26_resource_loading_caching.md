# G26 — Resource Loading & Caching


> **Category:** Guide · **Related:** [G8 Content Pipeline](./G8_content_pipeline.md) · [G13 C# Performance](./G13_csharp_performance.md) · [G15 Game Loop](./G15_game_loop.md) · [R3 Project Structure](../reference/R3_project_structure.md)

---

## Resource Types in MonoGame

Your game loads three categories of resources, each with different lifecycle and caching behavior:

| Category | Format | Loaded Via | Cached By |
|----------|--------|-----------|-----------|
| **MGCB assets** | .xnb (compiled) | `Content.Load<T>()` | ContentManager (automatic) |
| **Raw files** | .json, .csv, .txt | `TitleContainer.OpenStream()` or `File.Read` | You (manual) |
| **Bundled binaries** | .ttf, .png (uncompiled) | `TitleContainer.OpenStream()` | You (manual) |

---

## MGCB Assets (Content.Load)

MonoGame's `ContentManager` compiles assets at build time (via MGCB) and loads them at runtime. This is the primary asset path for textures, sounds, effects, and maps.

### Automatic Caching

`ContentManager` caches every loaded asset by its asset name. Calling `Content.Load<Texture2D>("sprites/player")` twice returns the **same object** — no disk I/O on the second call.

```csharp
// First call: loads from .xnb file, caches result
Texture2D playerTex = Content.Load<Texture2D>("sprites/player");

// Second call: returns cached instance immediately
Texture2D sameTex = Content.Load<Texture2D>("sprites/player");

// playerTex == sameTex (same object reference)
```

### Disposal

All assets loaded through a `ContentManager` are disposed when that `ContentManager` is disposed:

```csharp
Content.Unload(); // Disposes ALL assets loaded by this ContentManager
```

**This is all-or-nothing.** You cannot unload a single asset — `Unload()` releases everything. This is fine for most games (load everything at startup, never unload).

### Scoped ContentManagers

For large games with distinct levels, create separate `ContentManager` instances per scope:

```csharp
/// <summary>Load level-specific assets in a scoped ContentManager.</summary>
public sealed class LevelResources : IDisposable
{
    private readonly ContentManager _content;

    public LevelResources(IServiceProvider services, string levelName)
    {
        _content = new ContentManager(services, "Content");
        Tilemap = _content.Load<TiledMap>($"tilemaps/{levelName}");
        Music = _content.Load<Song>($"audio/music/{levelName}");
    }

    public TiledMap Tilemap { get; }
    public Song Music { get; }

    /// <summary>Unloads all level-specific assets.</summary>
    public void Dispose()
    {
        _content.Unload();
    }
}
```

**Usage:**

```csharp
// During scene transition:
_levelResources?.Dispose();  // Unload previous level's assets
_levelResources = new LevelResources(Services, "forest_01");
```

### What MGCB Handles

| Asset Type | MGCB Processor | Runtime Type |
|-----------|----------------|-------------|
| .png, .jpg, .bmp | Texture | `Texture2D` |
| .fx (HLSL shader) | Effect | `Effect` |
| .wav | Sound Effect | `SoundEffect` |
| .mp3, .ogg | Song | `Song` |
| .spritefont | Sprite Font | `SpriteFont` |
| .tmx (Tiled map) | Extended Content Pipeline | `TiledMap` |
| .ase/.aseprite | MonoGame.Aseprite | `AsepriteFile` |

---

## Runtime JSON Data

Game data files (items, dialogue, levels, enemy definitions) are loaded at runtime from JSON using `System.Text.Json` with source generators.

### Loading from Content Directory

For JSON files processed by MGCB as `/copy`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>Load a JSON file from the Content directory (cross-platform).</summary>
public static T LoadJson<T>(string contentPath, JsonTypeInfo<T> typeInfo)
{
    using Stream stream = TitleContainer.OpenStream($"Content/{contentPath}");
    return JsonSerializer.Deserialize(stream, typeInfo)
        ?? throw new InvalidOperationException($"Failed to deserialize {contentPath}");
}

// Usage:
ItemDatabase items = LoadJson("data/items.json", AppJsonContext.Default.ItemDatabase);
```

### Source Generator for AOT Compatibility

`System.Text.Json` source generators are required for iOS (AOT-only, no reflection):

```csharp
[JsonSerializable(typeof(ItemDatabase))]
[JsonSerializable(typeof(DialogueTree))]
[JsonSerializable(typeof(LevelDefinition))]
internal partial class AppJsonContext : JsonSerializerContext { }
```

### Caching JSON Data

Unlike MGCB assets, JSON data has no automatic caching. Cache it yourself:

```csharp
/// <summary>Simple cache for deserialized JSON data.</summary>
public sealed class DataCache
{
    private readonly Dictionary<string, object> _cache = new();

    public T Get<T>(string path, JsonTypeInfo<T> typeInfo) where T : class
    {
        if (_cache.TryGetValue(path, out object? cached))
            return (T)cached;

        T data = LoadJson(path, typeInfo);
        _cache[path] = data;
        return data;
    }

    public void Clear() => _cache.Clear();
}
```

---

## FontStashSharp Fonts

FontStashSharp loads .ttf/.otf files at runtime (not through MGCB). The `FontSystem` object should be created once and reused.

```csharp
/// <summary>Global font manager — create once, use everywhere.</summary>
public sealed class FontManager : IDisposable
{
    private readonly FontSystem _fontSystem;

    public FontManager()
    {
        _fontSystem = new FontSystem();

        // Load bundled font from Content (cross-platform, works on iOS)
        using Stream fontStream = TitleContainer.OpenStream("Content/fonts/JetBrainsMono-Regular.ttf");
        _fontSystem.AddFont(fontStream);
    }

    /// <summary>Get a font at a specific pixel size. FontStashSharp caches internally.</summary>
    public SpriteFontBase GetFont(float size) => _fontSystem.GetFont(size);

    public void Dispose() => _fontSystem.Dispose();
}
```

**Key behavior:** `FontSystem.GetFont(size)` caches rasterized glyph atlases internally. Requesting size 24 the first time rasterizes the glyphs; subsequent calls at size 24 return the cached atlas. Different sizes create separate atlases.

**Don't create new FontSystem objects per scene** — that discards the glyph cache and re-rasterizes everything. Create one at startup and keep it alive.

### Bundling Fonts

Fonts must be bundled via MGCB `/copy` to work on all platforms (especially iOS, where system font paths are inaccessible):

In your `.mgcb` file:
```
#begin fonts/JetBrainsMono-Regular.ttf
/copy:fonts/JetBrainsMono-Regular.ttf
```

---

## Texture Atlases

Texture atlases pack multiple sprites into a single texture, reducing draw call batches (SpriteBatch only breaks batches on texture switches).

### Loading Atlases

With MonoGame.Extended:

```csharp
// Loaded via MGCB (Extended Content Pipeline processes the atlas)
SpriteSheet atlas = Content.Load<SpriteSheet>("sprites/atlas");
TextureRegion2D playerRegion = atlas.GetRegion("player_idle");

spriteBatch.Draw(playerRegion, position, Color.White);
```

With MonoGame.Aseprite (direct .ase import):

```csharp
AsepriteFile aseFile = Content.Load<AsepriteFile>("sprites/player");
SpriteSheet spriteSheet = aseFile.CreateSpriteSheet(GraphicsDevice);
AnimatedSprite sprite = spriteSheet.CreateAnimatedSprite("idle");
```

### Atlas Strategies

| Strategy | When to Use |
|----------|-------------|
| One big atlas (2048x2048) | Small games, <500 sprites total |
| Per-scene atlas | Large games with distinct levels |
| Per-category atlas | Separate atlas for characters, tiles, UI, effects |

**2048x2048 is the safe maximum** for all hardware (including Reach profile). HiDef supports 4096x4096.

**Don't atlas unrelated assets together** if they're never on screen at the same time — you're wasting memory loading an atlas for sprites that won't be drawn.

---

## Scene-Scoped vs Global Resources

| Scope | What | Lifetime |
|-------|------|----------|
| **Global** | Fonts, shared UI textures, audio engine, player sprite | App lifetime |
| **Scene-scoped** | Level tilemaps, level music, level-specific sprites | Scene lifetime |
| **Transient** | Particle textures, dialog portraits loaded on demand | Shorter than scene |

### Architecture Pattern

```csharp
public class GameApp : Game
{
    // Global resources — loaded once in Initialize, never unloaded
    private FontManager _fonts;
    private Texture2D _uiAtlas;
    private SoundEffect _clickSound;

    // Scene-scoped resources — swapped on scene transitions
    private ContentManager _sceneContent;

    protected override void Initialize()
    {
        _fonts = new FontManager();
        _uiAtlas = Content.Load<Texture2D>("ui/atlas");
        _clickSound = Content.Load<SoundEffect>("audio/sfx/click");
    }

    public void LoadScene(string sceneName)
    {
        _sceneContent?.Unload();
        _sceneContent = new ContentManager(Services, "Content");
        // Scene-specific loads use _sceneContent
    }
}
```

---

## Loading Screens

For scenes with many assets, show a loading screen while loading:

### Synchronous Loading (Simple)

```csharp
/// <summary>Load all scene assets with a progress callback.</summary>
public void LoadSceneAssets(Action<float> onProgress)
{
    string[] assets = { "tilemaps/level1", "sprites/enemies", "audio/music/level1" };

    for (int i = 0; i < assets.Length; i++)
    {
        Content.Load<object>(assets[i]);
        onProgress((float)(i + 1) / assets.Length);
    }
}
```

This blocks the game loop during loading. For short loads (<2 seconds), this is fine — draw a static loading screen before starting.

### Async Loading (Background Thread)

For longer loads, load assets on a background thread:

```csharp
/// <summary>Load assets asynchronously with progress tracking.</summary>
public async Task LoadSceneAssetsAsync(ContentManager content, IProgress<float> progress,
    CancellationToken ct = default)
{
    string[] textures = { "sprites/enemies", "sprites/items", "sprites/tileset" };

    for (int i = 0; i < textures.Length; i++)
    {
        ct.ThrowIfCancellationRequested();

        // Content.Load is NOT thread-safe for GPU resources (Texture2D).
        // Load the raw data on the background thread, create GPU resources on main thread.
        // For simplicity, use the synchronous approach for small asset counts.
        await Task.Run(() => content.Load<Texture2D>(textures[i]), ct);

        progress.Report((float)(i + 1) / textures.Length);
    }
}
```

**Important caveat:** MonoGame's `ContentManager.Load<Texture2D>()` creates GPU resources, which must happen on the main thread on some platforms. The safest approach for cross-platform:

1. Load non-GPU data (JSON, audio) on background thread
2. Queue GPU resource creation (textures, effects) for the main thread
3. Process the queue one item per frame during the loading screen

For most 2D games, synchronous loading with a static splash screen is sufficient. Async loading adds complexity that's only justified for large open-world games.

---

## Memory Management

### What to Dispose

| Resource | Disposal |
|----------|----------|
| ContentManager assets | `Content.Unload()` disposes all at once |
| RenderTarget2D | Must dispose manually if created outside ContentManager |
| FontSystem | Must dispose manually |
| Custom Texture2D (runtime-created) | Must dispose manually |
| SoundEffectInstance | Dispose when done, or let finalizer handle it |

### Common Leaks

**Creating RenderTarget2D every frame:** Each `new RenderTarget2D()` allocates GPU memory. Create once, reuse, and recreate only on resize.

**Loading the same font file repeatedly:** Each `new FontSystem()` + `AddFont()` allocates a new glyph atlas. Keep one `FontManager` alive.

**Forgetting scoped ContentManagers:** If you create `new ContentManager()` per scene but never call `Unload()` on the old one, textures accumulate in GPU memory.

### Memory Budgets (Rough Guidelines)

| Platform | Texture Budget | Total Asset Budget |
|----------|---------------|-------------------|
| Desktop | 512MB+ | Generous — limited by disk speed |
| iPhone (recent) | ~200MB before warnings | ~400MB before termination |
| iPad | ~300MB | ~600MB |
| Older iOS devices | ~100MB | ~200MB |

These are approximate — iOS doesn't publish hard limits. Monitor memory with Xcode Instruments (Allocations + VM Tracker) to find your actual ceiling.

---

## Comparison to Godot's Resource System

| Godot | MonoGame Equivalent |
|-------|-------------------|
| `preload("res://sprite.png")` | `Content.Load<Texture2D>("sprite")` — loaded at scene init, cached |
| `load("res://sprite.png")` | Same as above — MonoGame always caches by name |
| Reference counting (auto-free) | Manual — `ContentManager.Unload()` or `IDisposable` |
| `.tres` custom resource | JSON file + `System.Text.Json` deserialization |
| `ResourceLoader.load_threaded_request()` | `Task.Run(() => Content.Load<T>(...))` with caveats |
| Scene instancing (`PackedScene`) | No equivalent — ECS entities are created via factory methods |

The main difference: Godot reference-counts resources and frees them when unused. MonoGame uses explicit scoping — you decide when to load and unload via ContentManager lifetime.

---

## Common Pitfalls

**Calling `Content.Load` in `Draw()`:** This works (returns cached), but the first call does I/O. If the asset isn't cached yet, you'll get a frame spike. Load everything in `Initialize()` or scene setup.

**Using `File.ReadAllBytes()` on iOS:** The iOS sandbox doesn't allow arbitrary file system access. Always use `TitleContainer.OpenStream()` for content files — it works on all platforms.

**Premature optimization with async loading:** For games with <100 assets, synchronous loading takes <1 second. The complexity of async loading isn't worth it until you actually measure a loading time problem.

**Not disposing `ContentManager` on scene transitions:** If each scene creates its own `ContentManager` and loads 50MB of textures, and you never dispose the old one, you'll run out of GPU memory after a few transitions.

**Assuming `Content.Load` is free after first call:** The cache lookup is fast but not zero-cost. Don't call it in hot loops — cache the reference in a field.

---

## See Also

- [G8 Content Pipeline](./G8_content_pipeline.md) — MGCB build configuration, importers, processors
- [G13 C# Performance](./G13_csharp_performance.md) — zero-allocation patterns, avoiding GC pressure
- [R3 Project Structure](../reference/R3_project_structure.md) — where Content/ and Resources/ directories live
- [G15 Game Loop](./G15_game_loop.md) — frame timing impact of loading during gameplay
