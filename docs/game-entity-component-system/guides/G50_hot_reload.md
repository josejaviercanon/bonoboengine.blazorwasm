# G50 — Hot Reload & Live Editing


> **Category:** Guide · **Related:** [G26 Resource Loading & Caching](./G26_resource_loading_caching.md) · [G16 Debugging](./G16_debugging.md) · [G43 Entity Prefabs](./G43_entity_prefabs.md) · [G30 Game Feel Tooling](./G30_game_feel_tooling.md)

---

## Why Hot Reload Matters

The single biggest productivity killer in game development is the feedback loop: change a value, rebuild, restart, navigate back to the exact spot you were testing, check the result. A 30-second loop done 200 times a day is nearly two hours of dead time. Hot reload collapses that to under one second.

**What hot reload buys you:**
- Tweak enemy health, see the result instantly while standing next to the enemy
- Adjust a particle color, watch it change mid-animation
- Fix a dialogue typo without restarting the entire conversation tree
- Swap a texture and see every sprite using it update in-place
- Modify a shader and watch the screen transform in real time

This guide builds a complete hot-reload pipeline for data files, textures, shaders, tilemaps, and audio — all coordinated through a central manager with ImGui tooling.

---

## FileSystemWatcher for Data Files

`FileSystemWatcher` is the foundation. It fires events on a background thread when files change, so you need a thread-safe queue to bridge into the game's update loop.

```csharp
public enum AssetType { Json, Texture, Shader, TiledMap, Audio }

public readonly record struct FileChangeEvent(
    string FullPath,
    AssetType Type,
    DateTime Timestamp
);

/// <summary>
/// Wraps FileSystemWatcher with debouncing and thread-safe queuing.
/// Watcher events fire on a threadpool thread — never touch game state directly.
/// </summary>
public sealed class DebouncedFileWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly ConcurrentQueue<FileChangeEvent> _queue = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastSeen = new();
    private readonly TimeSpan _debounce = TimeSpan.FromMilliseconds(300);
    private readonly Dictionary<string, AssetType> _extensionMap;

    public DebouncedFileWatcher(string directory, Dictionary<string, AssetType> extensionMap)
    {
        _extensionMap = extensionMap;
        _watcher = new FileSystemWatcher(directory)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
            EnableRaisingEvents = true
        };

        foreach (var ext in extensionMap.Keys)
            _watcher.Filters.Add($"*{ext}");

        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.Renamed += (_, e) => OnChanged(null, new FileSystemEventArgs(
            WatcherChangeTypes.Changed, Path.GetDirectoryName(e.FullPath)!, e.Name!));
    }

    private void OnChanged(object? sender, FileSystemEventArgs e)
    {
        var now = DateTime.UtcNow;
        var path = e.FullPath;

        // Debounce: editors often write multiple times in rapid succession
        if (_lastSeen.TryGetValue(path, out var last) && now - last < _debounce)
            return;

        _lastSeen[path] = now;

        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (_extensionMap.TryGetValue(ext, out var type))
            _queue.Enqueue(new FileChangeEvent(path, type, now));
    }

    /// <summary>
    /// Drain all pending events. Call this from the main game thread in Update().
    /// </summary>
    public int DrainInto(List<FileChangeEvent> target)
    {
        int count = 0;
        while (_queue.TryDequeue(out var evt))
        {
            target.Add(evt);
            count++;
        }
        return count;
    }

    public void Dispose()
    {
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
    }
}
```

**Key design decisions:**
- **300ms debounce** — VS Code, Rider, and vim all trigger multiple write events per save. The debounce window collapses them into one.
- **ConcurrentQueue** — lock-free, safe for the watcher thread to enqueue and the game thread to drain.
- **Extension map** — routes file types to the correct reload handler.

---

## JSON Data Hot Reload

Entity blueprints, item databases, dialogue trees, and level configs all live in JSON. Reloading them at runtime means invalidating caches and swapping the parsed data in place.

```csharp
public sealed class JsonDataCache
{
    private readonly ConcurrentDictionary<string, (object Data, int Version)> _cache = new();

    /// <summary>
    /// Load or return cached data. The loader deserializes from file path.
    /// </summary>
    public T Get<T>(string path, Func<string, T> loader) where T : class
    {
        if (_cache.TryGetValue(path, out var entry))
            return (T)entry.Data;

        var data = loader(path);
        _cache[path] = (data!, 1);
        return data;
    }

    /// <summary>
    /// Invalidate and reload a single file. Returns true if the file was in cache.
    /// </summary>
    public bool Reload<T>(string path, Func<string, T> loader) where T : class
    {
        if (!_cache.TryGetValue(path, out var old))
            return false;

        try
        {
            var data = loader(path);
            _cache[path] = (data!, old.Version + 1);
            return true;
        }
        catch (Exception ex)
        {
            // Keep old data on parse failure — never break a running game
            System.Diagnostics.Debug.WriteLine($"[HotReload] JSON parse failed: {path}\n{ex.Message}");
            return false;
        }
    }

    public void InvalidateAll() => _cache.Clear();
    public int GetVersion(string path) => _cache.TryGetValue(path, out var e) ? e.Version : 0;
}
```

**Versioned data with migration** — when your JSON schema evolves, embed a version field and migrate on load:

```csharp
public record ItemDatabase(int SchemaVersion, List<ItemDef> Items);

public static ItemDatabase LoadItems(string path)
{
    var json = File.ReadAllText(path);
    var db = JsonSerializer.Deserialize<ItemDatabase>(json)!;

    // Migrate old schemas forward
    if (db.SchemaVersion < 2)
    {
        foreach (var item in db.Items)
            item.StackSize ??= 1; // Added in v2
    }

    return db;
}
```

---

## Texture Hot Reload

MGCB-compiled `.xnb` textures can't be swapped at runtime — the content pipeline bakes them in. For hot-reloadable textures, load from raw `.png` files using `Texture2D.FromStream()`.

```csharp
public sealed class TextureHotLoader
{
    private readonly GraphicsDevice _device;
    private readonly Dictionary<string, Texture2D> _textures = new();
    private readonly Dictionary<string, List<Action<Texture2D>>> _subscribers = new();

    public TextureHotLoader(GraphicsDevice device) => _device = device;

    /// <summary>
    /// Load a texture from a raw .png file (not .xnb). Subscribable for hot reload.
    /// </summary>
    public Texture2D Load(string path)
    {
        if (_textures.TryGetValue(path, out var existing))
            return existing;

        var tex = LoadFromDisk(path);
        _textures[path] = tex;
        return tex;
    }

    /// <summary>
    /// Subscribe to texture changes. Callback fires on main thread after reload.
    /// Use this to update sprite references, atlas caches, etc.
    /// </summary>
    public void OnReload(string path, Action<Texture2D> callback)
    {
        if (!_subscribers.TryGetValue(path, out var list))
        {
            list = new List<Action<Texture2D>>();
            _subscribers[path] = list;
        }
        list.Add(callback);
    }

    /// <summary>
    /// Called by HotReloadManager when a .png file changes. Main thread only.
    /// </summary>
    public void Reload(string path)
    {
        if (!_textures.ContainsKey(path)) return;

        try
        {
            var newTex = LoadFromDisk(path);
            var oldTex = _textures[path];
            _textures[path] = newTex;

            // Notify all subscribers (sprite caches, renderers, etc.)
            if (_subscribers.TryGetValue(path, out var subs))
                foreach (var cb in subs) cb(newTex);

            // Dispose old texture after subscribers have updated
            oldTex.Dispose();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HotReload] Texture reload failed: {path}\n{ex.Message}");
        }
    }

    private Texture2D LoadFromDisk(string path)
    {
        using var stream = File.OpenRead(path);
        var tex = Texture2D.FromStream(_device, stream);
        tex.Name = Path.GetFileNameWithoutExtension(path);
        return tex;
    }
}
```

**Important limitation:** `Texture2D.FromStream()` loads raw image files. It does **not** read `.xnb`. Your hot-reloadable textures must bypass the Content pipeline entirely. See the [Development Workflow](#development-workflow) section for how to structure this.

---

## Shader Hot Reload

Shaders are the trickiest asset to hot-reload because MonoGame requires compiled `.xnb` effect files. The approach: watch `.fx` source files, invoke MGCB to recompile, then reload the resulting `.xnb`.

```csharp
public sealed class ShaderHotLoader
{
    private readonly ContentManager _content;
    private readonly Dictionary<string, Effect> _effects = new();
    private readonly string _rawShaderDir;    // e.g. "RawAssets/Shaders"
    private readonly string _compiledDir;     // e.g. "Content/Shaders"
    private string _lastCompileError = "";

    public string LastCompileError => _lastCompileError;
    public bool HasError => _lastCompileError.Length > 0;

    public ShaderHotLoader(ContentManager content, string rawShaderDir, string compiledDir)
    {
        _content = content;
        _rawShaderDir = rawShaderDir;
        _compiledDir = compiledDir;
    }

    public Effect Load(string name)
    {
        if (_effects.TryGetValue(name, out var existing))
            return existing;

        var effect = _content.Load<Effect>($"Shaders/{name}");
        _effects[name] = effect;
        return effect;
    }

    /// <summary>
    /// Recompile a shader from .fx source, then reload the effect.
    /// Keeps the old shader if compilation fails.
    /// </summary>
    public bool Reload(string fxPath)
    {
        var name = Path.GetFileNameWithoutExtension(fxPath);
        _lastCompileError = "";

        // Invoke MGCB to compile the .fx to .xnb
        var outputXnb = Path.Combine(_compiledDir, $"{name}.xnb");
        var args = $"/build:\"{fxPath}\" /outputDir:\"{_compiledDir}\" " +
                   $"/processorParam:DebugMode=Auto /importer:EffectImporter " +
                   $"/processor:EffectProcessor /platform:DesktopGL";

        var psi = new ProcessStartInfo("mgcb", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var proc = Process.Start(psi)!;
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(5000);

            if (proc.ExitCode != 0)
            {
                _lastCompileError = stderr;
                System.Diagnostics.Debug.WriteLine($"[HotReload] Shader compile error:\n{stderr}");
                return false;
            }

            // Force ContentManager to drop its cache for this asset
            // (ContentManager caches by name — we unload and reload)
            _content.UnloadAsset($"Shaders/{name}");
            var newEffect = _content.Load<Effect>($"Shaders/{name}");

            if (_effects.TryGetValue(name, out var oldEffect))
                oldEffect.Dispose();

            _effects[name] = newEffect;
            return true;
        }
        catch (Exception ex)
        {
            _lastCompileError = ex.Message;
            return false;
        }
    }
}
```

> **Note:** `ContentManager` doesn't have a built-in `UnloadAsset` by name. You'll need a small extension or a custom `ContentManager` subclass that exposes its internal `loadedAssets` dictionary. Alternatively, use a fresh `ContentManager` instance per reload.

---

## Tiled Map Reload

Watching `.tmx` files lets you edit levels in Tiled and see changes appear in the running game. The key challenge is preserving entity state — you want to swap map geometry without killing the player or resetting NPC positions.

```csharp
public sealed class TiledMapReloader
{
    private readonly Func<string, TiledMapData> _loader;

    public TiledMapReloader(Func<string, TiledMapData> loader) => _loader = loader;

    /// <summary>
    /// Reload a .tmx file while preserving existing entity state.
    /// </summary>
    public void Reload(string tmxPath, World world, ref TiledMapData currentMap)
    {
        try
        {
            // Snapshot entity positions before reload
            var entityStates = new Dictionary<string, (Vector2 Pos, Dictionary<string, object> Extra)>();
            var query = new QueryDescription().WithAll<Position, EntityId>();

            world.Query(in query, (ref Position pos, ref EntityId id) =>
            {
                entityStates[id.Value] = (new Vector2(pos.X, pos.Y), new());
            });

            // Reload map geometry (tiles, collision layers)
            var newMap = _loader(tmxPath);
            currentMap.Layers = newMap.Layers;
            currentMap.Tileset = newMap.Tileset;
            currentMap.CollisionRects = newMap.CollisionRects;

            // Restore entity positions (entities survive the geometry swap)
            world.Query(in query, (ref Position pos, ref EntityId id) =>
            {
                if (entityStates.TryGetValue(id.Value, out var state))
                {
                    pos.X = state.Pos.X;
                    pos.Y = state.Pos.Y;
                }
            });

            // Only spawn entities that are NEW in the updated map
            foreach (var obj in newMap.ObjectLayer)
            {
                if (!entityStates.ContainsKey(obj.Id))
                    SpawnMapEntity(world, obj);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HotReload] Tiled reload failed: {tmxPath}\n{ex.Message}");
        }
    }

    private void SpawnMapEntity(World world, TiledObject obj)
    {
        // Create entity from map object properties — 
        // hook into your entity factory / blueprint system (see G43)
    }
}
```

---

## Audio Hot Reload

Sound effects can be reloaded from raw `.wav` files using `SoundEffect.FromStream()`. Music is trickier — `Song` doesn't support stream loading, so for hot-reloadable music consider using `SoundEffectInstance` for short tracks or a streaming audio library.

```csharp
public sealed class AudioHotLoader
{
    private readonly Dictionary<string, SoundEffect> _sounds = new();

    public SoundEffect Load(string path)
    {
        if (_sounds.TryGetValue(path, out var existing))
            return existing;

        var sfx = LoadFromDisk(path);
        _sounds[path] = sfx;
        return sfx;
    }

    public void Reload(string path)
    {
        if (!_sounds.ContainsKey(path)) return;

        try
        {
            var newSfx = LoadFromDisk(path);
            var oldSfx = _sounds[path];
            _sounds[path] = newSfx;

            // SoundEffect doesn't have subscribers like textures,
            // but any cached SoundEffectInstance references become invalid.
            // Systems should re-fetch from this loader after reload events.
            oldSfx.Dispose();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HotReload] Audio reload failed: {path}\n{ex.Message}");
        }
    }

    private SoundEffect LoadFromDisk(string path)
    {
        using var stream = File.OpenRead(path);
        return SoundEffect.FromStream(stream); // .wav only
    }
}
```

> **Limitation:** `SoundEffect.FromStream()` only supports `.wav`. For `.ogg` or `.mp3` hot reload, you'd need a third-party decoder (e.g., NVorbis) to produce PCM data and create the `SoundEffect` from raw buffers.

---

## Hot Reload Manager

The central coordinator that owns all watchers and dispatches reload events to the correct handler. Wrapped in `#if DEBUG` so it compiles out of release builds entirely.

```csharp
#if DEBUG
public sealed class HotReloadManager : IDisposable
{
    private readonly DebouncedFileWatcher _watcher;
    private readonly List<FileChangeEvent> _pendingEvents = new();
    private readonly JsonDataCache _jsonCache;
    private readonly TextureHotLoader _textureLoader;
    private readonly ShaderHotLoader _shaderLoader;
    private readonly TiledMapReloader _tiledReloader;
    private readonly AudioHotLoader _audioLoader;

    // Stats for ImGui panel
    private int _totalReloads;
    private readonly List<string> _reloadLog = new();
    private readonly Dictionary<AssetType, bool> _enabled = new()
    {
        [AssetType.Json] = true,
        [AssetType.Texture] = true,
        [AssetType.Shader] = true,
        [AssetType.TiledMap] = true,
        [AssetType.Audio] = true,
    };

    public IReadOnlyList<string> ReloadLog => _reloadLog;
    public int TotalReloads => _totalReloads;

    public HotReloadManager(
        string watchDirectory,
        JsonDataCache jsonCache,
        TextureHotLoader textureLoader,
        ShaderHotLoader shaderLoader,
        TiledMapReloader tiledReloader,
        AudioHotLoader audioLoader)
    {
        _jsonCache = jsonCache;
        _textureLoader = textureLoader;
        _shaderLoader = shaderLoader;
        _tiledReloader = tiledReloader;
        _audioLoader = audioLoader;

        var extMap = new Dictionary<string, AssetType>
        {
            [".json"] = AssetType.Json,
            [".png"] = AssetType.Texture,
            [".fx"] = AssetType.Shader,
            [".tmx"] = AssetType.TiledMap,
            [".wav"] = AssetType.Audio,
        };

        _watcher = new DebouncedFileWatcher(watchDirectory, extMap);
    }

    public void SetEnabled(AssetType type, bool enabled) => _enabled[type] = enabled;
    public bool IsEnabled(AssetType type) => _enabled[type];

    /// <summary>
    /// Call every frame from Game.Update(). Drains pending file changes
    /// and dispatches them to the appropriate reload handler.
    /// </summary>
    public void Update(World world, ref TiledMapData currentMap)
    {
        _pendingEvents.Clear();
        _watcher.DrainInto(_pendingEvents);

        foreach (var evt in _pendingEvents)
        {
            if (!_enabled[evt.Type]) continue;

            var name = Path.GetFileName(evt.FullPath);
            bool success = evt.Type switch
            {
                AssetType.Json    => _jsonCache.Reload<object>(evt.FullPath,
                                        p => JsonSerializer.Deserialize<object>(File.ReadAllText(p))!),
                AssetType.Texture => ReloadTexture(evt.FullPath),
                AssetType.Shader  => _shaderLoader.Reload(evt.FullPath),
                AssetType.TiledMap => ReloadTiled(evt.FullPath, world, ref currentMap),
                AssetType.Audio   => ReloadAudio(evt.FullPath),
                _ => false
            };

            var status = success ? "✓" : "✗";
            var entry = $"[{DateTime.Now:HH:mm:ss}] {status} {evt.Type}: {name}";
            _reloadLog.Add(entry);
            if (_reloadLog.Count > 50) _reloadLog.RemoveAt(0);
            _totalReloads++;
        }
    }

    private bool ReloadTexture(string path) { _textureLoader.Reload(path); return true; }
    private bool ReloadAudio(string path) { _audioLoader.Reload(path); return true; }
    private bool ReloadTiled(string path, World world, ref TiledMapData map)
    {
        _tiledReloader.Reload(path, world, ref map);
        return true;
    }

    public void Dispose() => _watcher.Dispose();
}
#endif
```

---

## .NET Hot Reload Limitations

`dotnet watch` provides code hot reload for .NET 8 apps. It's useful but has hard limits.

**What `dotnet watch` supports:**
- Editing method bodies (change logic inside an existing method)
- Modifying string literals, numeric constants
- Adding static methods and lambdas
- Editing LINQ expressions

**What `dotnet watch` does NOT support:**
- Adding new types or interfaces
- Changing struct layout (critical — your ECS components are structs)
- Modifying generic type parameters
- Adding or removing fields from classes/structs
- Changing method signatures
- Anything that alters the shape of a type

**Impact on ECS development:**

```csharp
// This component CAN'T be hot-reloaded via dotnet watch 
// if you add/remove a field — the struct layout changes
public struct Health
{
    public float Current;
    public float Max;
    // Adding "public float Shield;" requires full restart
}

// But this system logic CAN be hot-reloaded:
public void Execute(ref Health h, ref DamageEvent dmg)
{
    h.Current -= dmg.Amount; // ← change this formula freely with dotnet watch
}
```

**When to use what:**

| Scenario | Use |
|---|---|
| Tweaking gameplay values (HP, speed, damage) | JSON hot reload |
| Changing textures, maps, audio | File-based hot reload |
| Modifying system logic | `dotnet watch` |
| Adding new components or systems | Full restart |
| Adjusting shader code | Shader hot reload |

The custom file-watching approach from this guide complements `dotnet watch` — they cover different asset types and have different constraints.

---

## Development Workflow

Structure your project so debug builds load raw files (hot-reloadable) and release builds load compiled `.xnb` (optimized).

**Project layout:**

```
MyGame/
├── Content/                  # MGCB pipeline (release assets)
│   ├── Content.mgcb
│   ├── Textures/
│   └── Shaders/
├── RawAssets/                # Raw files for hot reload (debug only)
│   ├── Textures/
│   │   └── player.png
│   ├── Shaders/
│   │   └── lighting.fx
│   ├── Data/
│   │   ├── items.json
│   │   └── enemies.json
│   ├── Maps/
│   │   └── level01.tmx
│   └── Audio/
│       └── hit.wav
└── src/
    └── Game1.cs
```

**Conditional loading:**

```csharp
public sealed class AssetLoader
{
    private readonly ContentManager _content;
    private readonly GraphicsDevice _device;
    private readonly string _rawAssetsPath;

    public AssetLoader(ContentManager content, GraphicsDevice device)
    {
        _content = content;
        _device = device;
        _rawAssetsPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "RawAssets");
    }

    public Texture2D LoadTexture(string name)
    {
#if DEBUG
        // Debug: load raw .png (supports hot reload)
        var path = Path.Combine(_rawAssetsPath, "Textures", $"{name}.png");
        if (File.Exists(path))
        {
            using var stream = File.OpenRead(path);
            return Texture2D.FromStream(_device, stream);
        }
#endif
        // Release (or fallback): load compiled .xnb
        return _content.Load<Texture2D>($"Textures/{name}");
    }

    public string LoadJson(string name)
    {
#if DEBUG
        var path = Path.Combine(_rawAssetsPath, "Data", $"{name}.json");
        if (File.Exists(path))
            return File.ReadAllText(path);
#endif
        // Fallback: embed JSON as resources or load from Content
        throw new FileNotFoundException($"Data file not found: {name}");
    }
}
```

**`.csproj` configuration** — copy raw assets to output only in Debug:

```xml
<PropertyGroup>
  <Configurations>Debug;Release</Configurations>
</PropertyGroup>

<ItemGroup Condition="'$(Configuration)' == 'Debug'">
  <None Include="RawAssets/**/*" CopyToOutputDirectory="PreserveNewest" LinkBase="RawAssets" />
</ItemGroup>

<!-- Strip hot reload code from release -->
<PropertyGroup Condition="'$(Configuration)' == 'Release'">
  <DefineConstants>$(DefineConstants);RELEASE</DefineConstants>
</PropertyGroup>
```

---

## ImGui Integration

ImGui is the final piece — it lets you live-edit values, see them change instantly, and save modifications back to JSON. This creates a tight tuning loop: slide a value → see the game respond → save when happy.

```csharp
#if DEBUG
public static class HotReloadImGui
{
    private static bool _showPanel = true;
    private static bool _showTuning = true;

    /// <summary>
    /// Draw the hot reload status panel. Call from your ImGui render pass.
    /// </summary>
    public static void DrawReloadPanel(HotReloadManager manager)
    {
        if (!ImGui.Begin("Hot Reload", ref _showPanel)) { ImGui.End(); return; }

        ImGui.Text($"Total reloads: {manager.TotalReloads}");
        ImGui.Separator();

        // Per-type toggles
        foreach (AssetType type in Enum.GetValues<AssetType>())
        {
            bool enabled = manager.IsEnabled(type);
            if (ImGui.Checkbox(type.ToString(), ref enabled))
                manager.SetEnabled(type, enabled);
        }

        ImGui.Separator();
        ImGui.Text("Recent reloads:");

        foreach (var entry in manager.ReloadLog)
        {
            var color = entry.Contains("✓")
                ? new System.Numerics.Vector4(0.3f, 1f, 0.3f, 1f)
                : new System.Numerics.Vector4(1f, 0.3f, 0.3f, 1f);
            ImGui.TextColored(color, entry);
        }

        ImGui.End();
    }

    /// <summary>
    /// Live parameter tuning panel. Edits values in-memory and optionally
    /// saves them back to JSON.
    /// </summary>
    public static void DrawTuningPanel(GameConfig config, string configPath)
    {
        if (!ImGui.Begin("Game Tuning", ref _showTuning)) { ImGui.End(); return; }

        bool dirty = false;

        ImGui.Text("Player");
        dirty |= ImGui.SliderFloat("Move Speed", ref config.PlayerSpeed, 10f, 500f);
        dirty |= ImGui.SliderFloat("Jump Force", ref config.JumpForce, 100f, 1000f);
        dirty |= ImGui.SliderFloat("Gravity", ref config.Gravity, 100f, 2000f);
        dirty |= ImGui.SliderInt("Max HP", ref config.MaxHP, 1, 100);

        ImGui.Separator();
        ImGui.Text("Combat");
        dirty |= ImGui.SliderFloat("Attack Cooldown", ref config.AttackCooldown, 0.05f, 2f);
        dirty |= ImGui.SliderFloat("Knockback Force", ref config.KnockbackForce, 50f, 500f);
        dirty |= ImGui.DragFloat("I-Frames (sec)", ref config.InvincibilityDuration, 0.01f, 0f, 3f);

        ImGui.Separator();
        ImGui.Text("Camera");
        dirty |= ImGui.SliderFloat("Follow Smoothing", ref config.CameraSmoothing, 0.01f, 1f);
        dirty |= ImGui.SliderFloat("Lookahead", ref config.CameraLookahead, 0f, 200f);

        if (dirty)
            ImGui.TextColored(new System.Numerics.Vector4(1f, 1f, 0.3f, 1f), "* Unsaved changes");

        if (ImGui.Button("Save to JSON"))
        {
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(configPath, json);
        }

        ImGui.SameLine();
        if (ImGui.Button("Revert"))
        {
            var json = File.ReadAllText(configPath);
            var reverted = JsonSerializer.Deserialize<GameConfig>(json)!;
            // Copy fields back (or replace the ref if using a wrapper)
            config.CopyFrom(reverted);
        }

        ImGui.End();
    }
}

/// <summary>
/// Game-wide tunable config. Fields are public for ImGui binding.
/// Loaded from game_config.json, hot-reloadable.
/// </summary>
public class GameConfig
{
    public float PlayerSpeed { get; set; } = 150f;
    public float JumpForce { get; set; } = 400f;
    public float Gravity { get; set; } = 800f;
    public int MaxHP { get; set; } = 10;
    public float AttackCooldown { get; set; } = 0.3f;
    public float KnockbackForce { get; set; } = 200f;
    public float InvincibilityDuration { get; set; } = 0.5f;
    public float CameraSmoothing { get; set; } = 0.1f;
    public float CameraLookahead { get; set; } = 50f;

    public void CopyFrom(GameConfig other)
    {
        PlayerSpeed = other.PlayerSpeed;
        JumpForce = other.JumpForce;
        Gravity = other.Gravity;
        MaxHP = other.MaxHP;
        AttackCooldown = other.AttackCooldown;
        KnockbackForce = other.KnockbackForce;
        InvincibilityDuration = other.InvincibilityDuration;
        CameraSmoothing = other.CameraSmoothing;
        CameraLookahead = other.CameraLookahead;
    }
}
#endif
```

**The tuning loop in practice:**
1. Launch game with `dotnet run`
2. Open the ImGui tuning panel (bind to a key like F5)
3. Drag the "Jump Force" slider while the player is mid-air
4. See the physics change instantly
5. Happy? Click "Save to JSON" — the file writes, the file watcher sees it, and the reload manager logs it
6. Next launch loads your tuned values automatically

---

## Wiring It All Together

Here's how the hot reload system integrates into your `Game1` class:

```csharp
public class Game1 : Game
{
    private World _world;
    private TiledMapData _currentMap;

#if DEBUG
    private HotReloadManager _hotReload;
    private JsonDataCache _jsonCache;
    private TextureHotLoader _textureLoader;
    private ShaderHotLoader _shaderLoader;
    private AudioHotLoader _audioLoader;
    private GameConfig _gameConfig;
    private string _configPath;
#endif

    protected override void Initialize()
    {
#if DEBUG
        var rawDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "RawAssets");
        _jsonCache = new JsonDataCache();
        _textureLoader = new TextureHotLoader(GraphicsDevice);
        _shaderLoader = new ShaderHotLoader(Content, Path.Combine(rawDir, "Shaders"),
                                             Path.Combine(rawDir, "Shaders", "compiled"));
        _audioLoader = new AudioHotLoader();
        var tiledReloader = new TiledMapReloader(LoadTiledMap);

        _hotReload = new HotReloadManager(rawDir, _jsonCache, _textureLoader,
                                           _shaderLoader, tiledReloader, _audioLoader);

        _configPath = Path.Combine(rawDir, "Data", "game_config.json");
        _gameConfig = JsonSerializer.Deserialize<GameConfig>(File.ReadAllText(_configPath))!;
#endif
        base.Initialize();
    }

    protected override void Update(GameTime gameTime)
    {
#if DEBUG
        _hotReload.Update(_world, ref _currentMap);
#endif
        // ... normal game update
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        // ... normal game draw

#if DEBUG
        // ImGui render pass
        HotReloadImGui.DrawReloadPanel(_hotReload);
        HotReloadImGui.DrawTuningPanel(_gameConfig, _configPath);
#endif
        base.Draw(gameTime);
    }
}
```

Every `#if DEBUG` block vanishes in release builds. Zero runtime cost, zero binary bloat. The hot reload system exists only where it matters — during development.
