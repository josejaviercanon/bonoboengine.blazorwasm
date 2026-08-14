# G46 — Modding Support



> **Category:** Guide · **Related:** [G43 Entity Prefabs](./G43_entity_prefabs.md) · [G8 Content Pipeline](./G8_content_pipeline.md) · [G26 Resource Loading & Caching](./G26_resource_loading_caching.md) · [G34 Localization](./G34_localization.md)

---

## Why Support Modding

Modding transforms a finished game into a living platform. Stardew Valley sold millions of extra copies because its mod scene kept it relevant for years. Minecraft's entire identity was shaped by mods long before official updates caught up. Terraria's tModLoader turned a complete game into a framework that players still pour thousands of hours into.

For a solo dev, the calculus is straightforward:

| Factor | Without Mods | With Mods |
|---|---|---|
| Content lifespan | Finite (your output) | Effectively infinite |
| Bug surface | You find them | Community finds + fixes them |
| Player retention | Months | Years |
| Community investment | Passive consumers | Active creators |
| Development cost | You build everything | Community extends for free |

The upfront cost is real — a mod system adds 2–4 weeks of architecture work. But the payoff compounds. Players who mod your game become evangelists. They write wiki pages, make YouTube tutorials, and recruit other players. You can't buy that kind of engagement.

The key insight: **you don't need a perfect mod system at launch.** Start with data-driven design (Section 2), add asset overrides (Section 3), and layer scripting later. Each step independently improves your architecture even if nobody ever writes a mod.

---

## Data-Driven Architecture

Every moddable system starts with the same principle: **separate data from code.** If an enemy's health is a hardcoded `const int HP = 50;`, nobody can change it without recompiling. If it lives in `entities/slime.json`, anyone can tweak it with a text editor.

### What Should Be Data

| Layer | Data Format | Examples |
|---|---|---|
| Entity stats | JSON | HP, speed, damage, AI type |
| Item definitions | JSON | Name, icon path, effects, recipe |
| Sprite sheets | PNG + JSON atlas | Character animations, tilesets |
| Audio | OGG/WAV | SFX, music tracks |
| Dialogue | JSON / custom DSL | NPC conversations, quest text |
| Maps / Levels | JSON / Tiled TMX | Tile placement, spawn points |
| UI layouts | JSON | Panel positions, element sizes |
| Localization | JSON per locale | All player-facing strings |

### The Content Root

Establish a single content root that all systems load from. This becomes the foundation for the override system in Section 3.

```csharp
/// <summary>
/// Central registry for resolving content paths. All game systems
/// load assets through this instead of touching the filesystem directly.
/// </summary>
public sealed class ContentRoot
{
    private readonly List<string> _searchPaths = new();

    /// <summary>Adds a path layer. Later layers override earlier ones.</summary>
    public void AddLayer(string absolutePath)
    {
        if (Directory.Exists(absolutePath))
            _searchPaths.Add(absolutePath);
    }

    /// <summary>
    /// Resolves a virtual path (e.g. "entities/slime.json") to the
    /// highest-priority physical file, or null if not found.
    /// </summary>
    public string? Resolve(string virtualPath)
    {
        // Walk layers in reverse — last added wins
        for (int i = _searchPaths.Count - 1; i >= 0; i--)
        {
            string full = Path.Combine(_searchPaths[i], virtualPath);
            if (File.Exists(full))
                return full;
        }
        return null;
    }

    /// <summary>
    /// Returns all files matching a virtual directory across every layer.
    /// Used for discovery (e.g. "give me all entity JSONs from base + mods").
    /// </summary>
    public IEnumerable<string> ResolveAll(string virtualDir, string pattern = "*.*")
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = _searchPaths.Count - 1; i >= 0; i--)
        {
            string dir = Path.Combine(_searchPaths[i], virtualDir);
            if (!Directory.Exists(dir)) continue;

            foreach (string file in Directory.EnumerateFiles(dir, pattern))
            {
                string relative = Path.GetRelativePath(dir, file);
                if (seen.Add(relative))
                    yield return file;
            }
        }
    }
}
```

### Entity Definition Example

Reference [G43 Entity Prefabs](./G43_entity_prefabs.md) for the full prefab system. Here's the JSON side:

```json
// content/entities/slime.json
{
    "id": "base:slime",
    "displayName": "loc:entity.slime.name",
    "components": {
        "health":    { "max": 50 },
        "sprite":    { "sheet": "sprites/enemies/slime.png", "animation": "idle" },
        "collider":  { "width": 14, "height": 12, "offset": [1, 4] },
        "ai":        { "type": "wander", "aggroRange": 80 },
        "lootTable": { "ref": "loot/slime_drops.json" }
    }
}
```

Every field references data, never behavior. The `ai.type` string maps to a code-side AI strategy — modders pick from existing strategies or (with Lua) write new ones.

---

## Asset Override System

The override system is the simplest and most powerful modding feature. A mod places a file at the same virtual path as a base asset, and the mod's version wins.

```
game/
├── content/              ← base layer (priority 0)
│   ├── entities/
│   │   └── slime.json
│   └── sprites/
│       └── enemies/
│           └── slime.png
└── mods/
    └── cute_slimes/      ← mod layer (priority 1)
        └── content/
            └── sprites/
                └── enemies/
                    └── slime.png   ← overrides base slime sprite
```

### Virtual File System

```csharp
/// <summary>
/// Layered virtual file system. Wraps ContentRoot with mod-aware
/// loading, JSON merging, and cache-friendly access.
/// </summary>
public sealed class VirtualFileSystem
{
    private readonly ContentRoot _root = new();
    private readonly Dictionary<string, string> _cache = new();

    public void MountBase(string basePath)
        => _root.AddLayer(basePath);

    public void MountMod(string modContentPath)
        => _root.AddLayer(modContentPath);

    /// <summary>Reads text content from the highest-priority layer.</summary>
    public string? ReadText(string virtualPath)
    {
        if (_cache.TryGetValue(virtualPath, out string? cached))
            return cached;

        string? resolved = _root.Resolve(virtualPath);
        if (resolved == null) return null;

        string text = File.ReadAllText(resolved);
        _cache[virtualPath] = text;
        return text;
    }

    /// <summary>Reads and deserializes JSON from the highest-priority layer.</summary>
    public T? ReadJson<T>(string virtualPath) where T : class
    {
        string? text = ReadText(virtualPath);
        return text != null
            ? JsonSerializer.Deserialize<T>(text, _jsonOptions)
            : null;
    }

    /// <summary>Loads a Texture2D from the highest-priority layer.</summary>
    public Texture2D? LoadTexture(GraphicsDevice device, string virtualPath)
    {
        string? resolved = _root.Resolve(virtualPath);
        if (resolved == null) return null;

        using var stream = File.OpenRead(resolved);
        return Texture2D.FromStream(device, stream);
    }

    /// <summary>Invalidates a single cached entry (used by hot-reload).</summary>
    public void Invalidate(string virtualPath)
        => _cache.Remove(virtualPath);

    /// <summary>Clears all cached content.</summary>
    public void InvalidateAll()
        => _cache.Clear();

    /// <summary>Enumerates all files in a virtual directory across all layers.</summary>
    public IEnumerable<string> Enumerate(string virtualDir, string pattern = "*.*")
        => _root.ResolveAll(virtualDir, pattern);

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}
```

### Load Order Priority

When multiple mods exist, order matters. The system resolves priority from the mod manifest dependency graph (Section 4). Mods loaded later override mods loaded earlier:

```
Layer 0: content/          (base game — always lowest priority)
Layer 1: mods/bugfix_pack/ (loaded first — lower priority mod)
Layer 2: mods/cute_slimes/ (loaded second — higher priority mod)
Layer 3: mods/overhaul/    (loaded last — highest priority mod)
```

If both `cute_slimes` and `overhaul` provide `sprites/enemies/slime.png`, the overhaul version wins. This is the "last wins" rule — simple, predictable, and easy for players to understand by reordering their mod list.

---

## Mod Loading Pipeline

### Mod Manifest

Every mod has a `mod.json` at its root:

```json
{
    "id": "cute_slimes",
    "name": "Cute Slimes Reskin",
    "version": "1.2.0",
    "author": "PixelArtist42",
    "description": "Replaces all slime sprites with adorable versions.",
    "gameVersionMin": "0.9.0",
    "gameVersionMax": "1.*",
    "dependencies": [],
    "loadAfter": ["bugfix_pack"],
    "tags": ["cosmetic", "sprites"]
}
```

```csharp
public sealed class ModManifest
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";
    public string GameVersionMin { get; set; } = "0.0.0";
    public string GameVersionMax { get; set; } = "*";
    public List<string> Dependencies { get; set; } = new();
    public List<string> LoadAfter { get; set; } = new();
    public List<string> Tags { get; set; } = new();
}

/// <summary>
/// Represents a discovered mod on disk, including its manifest
/// and the resolved path to its content folder.
/// </summary>
public sealed class ModInfo
{
    public ModManifest Manifest { get; init; } = new();
    public string RootPath { get; init; } = "";
    public string ContentPath => Path.Combine(RootPath, "content");
    public string ScriptsPath => Path.Combine(RootPath, "scripts");
    public bool Enabled { get; set; } = true;
}
```

### Discovery & Dependency Resolution

```csharp
/// <summary>
/// Discovers mods in the mods/ directory, validates manifests,
/// resolves load order via topological sort, and mounts them.
/// </summary>
public sealed class ModLoader
{
    private readonly VirtualFileSystem _vfs;
    private readonly List<ModInfo> _loadedMods = new();
    private readonly string _modsDir;
    private readonly string _gameVersion;

    public IReadOnlyList<ModInfo> LoadedMods => _loadedMods;

    public ModLoader(VirtualFileSystem vfs, string modsDir, string gameVersion)
    {
        _vfs = vfs;
        _modsDir = modsDir;
        _gameVersion = gameVersion;
    }

    /// <summary>Discovers, validates, sorts, and mounts all enabled mods.</summary>
    public List<string> LoadAll()
    {
        var errors = new List<string>();
        var discovered = DiscoverMods(errors);
        var sorted = TopologicalSort(discovered, errors);

        foreach (var mod in sorted)
        {
            if (Directory.Exists(mod.ContentPath))
                _vfs.MountMod(mod.ContentPath);

            _loadedMods.Add(mod);
            Console.WriteLine($"[Mod] Loaded: {mod.Manifest.Name} v{mod.Manifest.Version}");
        }

        return errors;
    }

    private List<ModInfo> DiscoverMods(List<string> errors)
    {
        var mods = new List<ModInfo>();
        if (!Directory.Exists(_modsDir)) return mods;

        foreach (string dir in Directory.GetDirectories(_modsDir))
        {
            string manifestPath = Path.Combine(dir, "mod.json");
            if (!File.Exists(manifestPath))
            {
                errors.Add($"Skipping {Path.GetFileName(dir)}: no mod.json");
                continue;
            }

            try
            {
                string json = File.ReadAllText(manifestPath);
                var manifest = JsonSerializer.Deserialize<ModManifest>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (manifest == null || string.IsNullOrEmpty(manifest.Id))
                {
                    errors.Add($"Skipping {Path.GetFileName(dir)}: invalid manifest");
                    continue;
                }

                mods.Add(new ModInfo { Manifest = manifest, RootPath = dir });
            }
            catch (JsonException ex)
            {
                errors.Add($"Skipping {Path.GetFileName(dir)}: {ex.Message}");
            }
        }

        return mods;
    }

    /// <summary>Topological sort using Kahn's algorithm for dependency ordering.</summary>
    private List<ModInfo> TopologicalSort(List<ModInfo> mods, List<string> errors)
    {
        var byId = mods.ToDictionary(m => m.Manifest.Id);
        var inDegree = mods.ToDictionary(m => m.Manifest.Id, _ => 0);
        var graph = mods.ToDictionary(m => m.Manifest.Id, _ => new List<string>());

        foreach (var mod in mods)
        {
            // Hard dependencies
            foreach (string dep in mod.Manifest.Dependencies)
            {
                if (!byId.ContainsKey(dep))
                {
                    errors.Add($"Mod '{mod.Manifest.Id}' requires missing dependency '{dep}'");
                    mod.Enabled = false;
                    continue;
                }
                graph[dep].Add(mod.Manifest.Id);
                inDegree[mod.Manifest.Id]++;
            }

            // Soft load-after hints
            foreach (string after in mod.Manifest.LoadAfter)
            {
                if (!byId.ContainsKey(after)) continue; // soft — skip if absent
                graph[after].Add(mod.Manifest.Id);
                inDegree[mod.Manifest.Id]++;
            }
        }

        var queue = new Queue<string>(
            inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var sorted = new List<ModInfo>();

        while (queue.Count > 0)
        {
            string current = queue.Dequeue();
            if (byId[current].Enabled)
                sorted.Add(byId[current]);

            foreach (string neighbor in graph[current])
            {
                inDegree[neighbor]--;
                if (inDegree[neighbor] == 0)
                    queue.Enqueue(neighbor);
            }
        }

        if (sorted.Count < mods.Count(m => m.Enabled))
            errors.Add("Circular dependency detected in mod load order");

        return sorted;
    }
}
```

---

## Content Hot-Loading

During development (and optionally in release builds), watch mod directories for changes and reload assets on the fly. This dramatically speeds up mod iteration.

```csharp
/// <summary>
/// Watches mod content directories for file changes and triggers
/// reload callbacks. Uses debouncing to batch rapid edits.
/// </summary>
public sealed class ModHotReloader : IDisposable
{
    private readonly VirtualFileSystem _vfs;
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly Dictionary<string, DateTime> _pendingReloads = new();
    private readonly TimeSpan _debounce = TimeSpan.FromMilliseconds(300);
    private readonly string _baseContentPath;

    public event Action<string>? OnAssetReloaded;

    public ModHotReloader(VirtualFileSystem vfs, string baseContentPath)
    {
        _vfs = vfs;
        _baseContentPath = baseContentPath;
    }

    public void Watch(string directory)
    {
        if (!Directory.Exists(directory)) return;

        var watcher = new FileSystemWatcher(directory)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
            EnableRaisingEvents = true
        };

        watcher.Changed += OnFileChanged;
        watcher.Created += OnFileChanged;
        watcher.Renamed += (s, e) => OnFileChanged(s, e);
        _watchers.Add(watcher);
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        // Convert physical path back to virtual path
        string virtualPath = ToVirtualPath(e.FullPath);
        if (virtualPath == null) return;

        lock (_pendingReloads)
        {
            _pendingReloads[virtualPath] = DateTime.UtcNow;
        }
    }

    /// <summary>Call each frame to flush debounced reload events.</summary>
    public void Update()
    {
        List<string>? ready = null;

        lock (_pendingReloads)
        {
            var now = DateTime.UtcNow;
            ready = _pendingReloads
                .Where(kv => now - kv.Value >= _debounce)
                .Select(kv => kv.Key)
                .ToList();

            foreach (string path in ready)
                _pendingReloads.Remove(path);
        }

        if (ready == null) return;

        foreach (string virtualPath in ready)
        {
            _vfs.Invalidate(virtualPath);
            OnAssetReloaded?.Invoke(virtualPath);
            Console.WriteLine($"[HotReload] Reloaded: {virtualPath}");
        }
    }

    private string? ToVirtualPath(string physicalPath)
    {
        // Try to extract relative path from any watched content root
        foreach (var w in _watchers)
        {
            if (physicalPath.StartsWith(w.Path, StringComparison.OrdinalIgnoreCase))
                return Path.GetRelativePath(w.Path, physicalPath)
                    .Replace('\\', '/');
        }
        return null;
    }

    public void Dispose()
    {
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();
    }
}
```

### Subscribing to Reloads

Systems register for the assets they care about:

```csharp
hotReloader.OnAssetReloaded += virtualPath =>
{
    if (virtualPath.StartsWith("entities/"))
        entityFactory.ReloadBlueprint(virtualPath);
    else if (virtualPath.StartsWith("sprites/"))
        spriteCache.Evict(virtualPath);
    else if (virtualPath.StartsWith("scripts/"))
        luaEngine.ReloadScript(virtualPath);
};
```

---

## Scriptable Mods (Lua Integration)

Asset overrides handle cosmetic and numeric changes. For behavior — new AI patterns, custom item effects, event-driven logic — you need scripting. Lua via [MoonSharp](https://www.moonsharp.org/) is the standard choice for .NET games: it's pure C#, requires no native binaries, and supports sandboxing out of the box.

```xml
<!-- Add to your .csproj -->
<PackageReference Include="MoonSharp" Version="2.0.0.0" />
```

### Sandboxed Script Engine

```csharp
/// <summary>
/// Manages Lua script execution with sandboxing. Mods cannot access
/// the filesystem, network, or OS functions — only the APIs we expose.
/// </summary>
public sealed class ModScriptEngine
{
    private readonly Dictionary<string, Script> _scripts = new();
    private readonly Dictionary<string, List<Closure>> _eventHooks = new();

    /// <summary>
    /// Loads a Lua script file into a sandboxed environment.
    /// CoreModules whitelist ensures no file/OS/network access.
    /// </summary>
    public void LoadScript(string modId, string luaPath)
    {
        var script = new Script(CoreModules.Preset_SoftSandbox);

        // Expose our game API table
        RegisterGameApi(script, modId);

        script.DoFile(luaPath);
        _scripts[$"{modId}:{Path.GetFileName(luaPath)}"] = script;
    }

    /// <summary>Fires a named event, calling all Lua hooks registered for it.</summary>
    public void FireEvent(string eventName, params object[] args)
    {
        if (!_eventHooks.TryGetValue(eventName, out var hooks)) return;

        foreach (var hook in hooks)
        {
            try
            {
                var dynArgs = args.Select(a => DynValue.FromObject(hook.OwnerScript, a));
                hook.Call(dynArgs.ToArray());
            }
            catch (ScriptRuntimeException ex)
            {
                Console.WriteLine($"[Lua] Error in {eventName}: {ex.DecoratedMessage}");
            }
        }
    }

    public void ReloadScript(string modId, string luaPath)
    {
        // Remove old hooks from this mod before reloading
        string key = $"{modId}:{Path.GetFileName(luaPath)}";
        _scripts.Remove(key);

        // Re-register (hooks re-added on DoFile via game.on calls)
        LoadScript(modId, luaPath);
    }

    private void RegisterGameApi(Script script, string modId)
    {
        var api = new Table(script);

        // game.on("eventName", function(...) end)
        api["on"] = (Action<string, Closure>)((eventName, callback) =>
        {
            if (!_eventHooks.ContainsKey(eventName))
                _eventHooks[eventName] = new List<Closure>();
            _eventHooks[eventName].Add(callback);
        });

        // game.log(message)
        api["log"] = (Action<string>)(msg =>
            Console.WriteLine($"[Lua:{modId}] {msg}"));

        // game.getEntity(id) — returns a table with entity data
        api["getEntity"] = (Func<string, Table?>)(id =>
            GetEntityAsTable(script, id));

        // game.setEntityField(id, component, field, value)
        api["setEntityField"] = (Action<string, string, string, DynValue>)(
            (id, comp, field, val) => SetEntityField(id, comp, field, val));

        // game.spawnEntity(blueprintId, x, y)
        api["spawnEntity"] = (Action<string, float, float>)(SpawnFromLua);

        script.Globals["game"] = api;
    }

    // Stubs — wire these to your actual ECS in production
    private Table? GetEntityAsTable(Script s, string id) => null;
    private void SetEntityField(string id, string comp, string field, DynValue val) { }
    private void SpawnFromLua(string blueprint, float x, float y) { }
}
```

### Example Lua Mod Script

```lua
-- mods/goblin_invasion/scripts/invasion.lua

game.log("Goblin Invasion mod loaded!")

local invasionActive = false
local goblinCount = 0
local MAX_GOBLINS = 20

-- Spawn goblins when night falls
game.on("onTimeChange", function(hour)
    if hour == 21 and not invasionActive then
        invasionActive = true
        goblinCount = 0
        game.log("The goblins are coming!")
    elseif hour == 6 and invasionActive then
        invasionActive = false
        game.log("The goblins retreat at dawn.")
    end
end)

-- Periodically spawn goblins during invasion
game.on("onTick", function(dt)
    if invasionActive and goblinCount < MAX_GOBLINS then
        if math.random() < 0.02 then
            local x = math.random(100, 900)
            local y = math.random(100, 600)
            game.spawnEntity("goblin_invasion:goblin", x, y)
            goblinCount = goblinCount + 1
        end
    end
end)

-- Custom drop table when goblins die
game.on("onEntityDeath", function(entityId, entityType)
    if entityType == "goblin_invasion:goblin" then
        if math.random() < 0.3 then
            game.log("A goblin dropped a rare gem!")
        end
    end
end)
```

### Available Event Hooks

Design your event surface deliberately. Start small and expand based on modder requests:

| Event | Arguments | When Fired |
|---|---|---|
| `onTick` | `deltaTime` | Every frame |
| `onEntitySpawn` | `entityId, blueprintId, x, y` | Entity created |
| `onEntityDeath` | `entityId, entityType` | Entity HP reaches 0 |
| `onDamage` | `targetId, sourceId, amount` | Damage applied |
| `onItemUse` | `playerId, itemId, x, y` | Player uses item |
| `onItemPickup` | `playerId, itemId, amount` | Item collected |
| `onTimeChange` | `hour` | In-game hour changes |
| `onMapLoad` | `mapId` | New map loaded |
| `onPlayerJoin` | `playerId` | Multiplayer join |
| `onSave` | `slotId` | Game saved |
| `onLoad` | `slotId` | Game loaded |

---

## Custom Entity / Item Definitions

Mods can add entirely new entities and items by placing JSON blueprints in their content folder. The system merges mod content with base content during loading.

```json
// mods/goblin_invasion/content/entities/goblin.json
{
    "id": "goblin_invasion:goblin",
    "displayName": "Goblin Raider",
    "components": {
        "health":   { "max": 30 },
        "sprite":   { "sheet": "sprites/goblin.png", "animation": "walk" },
        "collider": { "width": 12, "height": 14, "offset": [2, 2] },
        "ai":       { "type": "aggressive", "aggroRange": 120 },
        "lootTable": {
            "drops": [
                { "item": "base:gold_coin", "chance": 0.8, "min": 1, "max": 5 },
                { "item": "goblin_invasion:goblin_ear", "chance": 0.5 }
            ]
        }
    }
}
```

### Blueprint Merging

```csharp
/// <summary>
/// Collects entity/item blueprints from base + all mod layers.
/// Mod blueprints with the same ID as base blueprints override them.
/// New IDs are added to the registry.
/// </summary>
public sealed class BlueprintRegistry
{
    private readonly Dictionary<string, JsonElement> _blueprints = new();

    public void LoadFromDirectory(VirtualFileSystem vfs, string virtualDir)
    {
        foreach (string file in vfs.Enumerate(virtualDir, "*.json"))
        {
            try
            {
                string json = File.ReadAllText(file);
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("id", out var idProp))
                {
                    string id = idProp.GetString() ?? "";
                    _blueprints[id] = root.Clone(); // last loaded wins
                }
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"[Blueprint] Parse error in {file}: {ex.Message}");
            }
        }
    }

    public JsonElement? Get(string id)
        => _blueprints.TryGetValue(id, out var bp) ? bp : null;

    public IEnumerable<string> AllIds => _blueprints.Keys;
}
```

The namespaced ID convention (`modid:name`) prevents collisions. Base game content uses the `base:` prefix. Mods use their manifest ID as prefix.

---

## UI Mods

Full UI modding is complex. A practical approach is to provide named **hook points** — slots in the UI where mods can inject elements.

```csharp
/// <summary>
/// Simple UI mod hook system. The base UI declares named slots;
/// mods register content to fill those slots.
/// </summary>
public sealed class UiModHooks
{
    private readonly Dictionary<string, List<Action<SpriteBatch, Rectangle>>> _hooks = new();

    /// <summary>Mods call this to register a draw callback at a named slot.</summary>
    public void Register(string hookName, Action<SpriteBatch, Rectangle> drawCallback)
    {
        if (!_hooks.ContainsKey(hookName))
            _hooks[hookName] = new();
        _hooks[hookName].Add(drawCallback);
    }

    /// <summary>Base UI calls this where mod content should appear.</summary>
    public void Render(string hookName, SpriteBatch batch, Rectangle bounds)
    {
        if (!_hooks.TryGetValue(hookName, out var callbacks)) return;
        foreach (var cb in callbacks)
            cb(batch, bounds);
    }
}
```

Typical hook points: `"hud_top_right"`, `"inventory_sidebar"`, `"main_menu_buttons"`, `"pause_menu_extras"`. Lua mods can register UI hooks through the script API, drawing simple elements like status text or icon overlays.

---

## Mod Compatibility & Versioning

### API Versioning

Your mod API has a version independent of the game version. Follow semver:

- **Major** — Breaking changes (removed events, changed signatures). Mods targeting the old major version won't load.
- **Minor** — New features (new events, new API functions). Old mods still work.
- **Patch** — Bug fixes. No API changes.

```csharp
public static class ModApi
{
    public static readonly Version Current = new(1, 3, 0);

    /// <summary>
    /// Checks whether a mod's required API version range is compatible
    /// with the current API version.
    /// </summary>
    public static bool IsCompatible(string minVersion, string maxVersion)
    {
        var min = Version.Parse(minVersion);

        // Major version must match
        if (min.Major != Current.Major) return false;

        // Mod's minimum must not exceed current
        if (min > Current) return false;

        return true;
    }
}
```

### Conflict Detection

During load, check for overlapping asset paths between mods and warn the player:

```csharp
public static List<string> DetectConflicts(IReadOnlyList<ModInfo> mods)
{
    var conflicts = new List<string>();
    var fileOwners = new Dictionary<string, string>(); // virtualPath → modId

    foreach (var mod in mods)
    {
        if (!Directory.Exists(mod.ContentPath)) continue;

        foreach (string file in Directory.EnumerateFiles(
            mod.ContentPath, "*.*", SearchOption.AllDirectories))
        {
            string vPath = Path.GetRelativePath(mod.ContentPath, file);

            if (fileOwners.TryGetValue(vPath, out string? existingMod))
            {
                conflicts.Add(
                    $"Conflict: '{vPath}' overridden by '{mod.Manifest.Id}' " +
                    $"(was from '{existingMod}')");
            }

            fileOwners[vPath] = mod.Manifest.Id;
        }
    }

    return conflicts;
}
```

---

## Security Considerations

Mods are third-party code running on your player's machine. Take this seriously.

### Threat Model

| Threat | Lua Mods | .NET Assembly Mods |
|---|---|---|
| File system access | ❌ Blocked by sandbox | ✅ Full access |
| Network access | ❌ Blocked by sandbox | ✅ Full access |
| Native code execution | ❌ Not possible | ✅ Via P/Invoke |
| Memory corruption | ❌ Managed only | ⚠️ Possible via unsafe |
| Performance abuse | ⚠️ Instruction limits | ✅ Unrestricted |

### Why Lua-First

MoonSharp's `CoreModules.Preset_SoftSandbox` strips:
- `io` — no file operations
- `os` — no system calls
- `load`/`dofile` from arbitrary paths
- `debug` — no introspection abuse

You can add instruction count limits to prevent infinite loops:

```csharp
script.Options.InstructionLimit = 100_000; // kill after 100k ops
```

### If You Must Support .NET Plugins

Some games (like modding-heavy titles) need the power of compiled plugins. If you go this route:

1. **Load assemblies in an isolated `AssemblyLoadContext`** so they can be unloaded.
2. **Require code signing** — only load assemblies signed with a trusted key.
3. **Document the risk clearly** — players must opt in to "unsafe mods."
4. **Never auto-enable** .NET mods. Require explicit user action.

```csharp
// Extremely simplified — real implementation needs more guards
public sealed class PluginLoader
{
    public static void LoadSigned(string dllPath, string expectedPublicKey)
    {
        var asm = System.Reflection.Assembly.LoadFrom(dllPath);
        byte[]? key = asm.GetName().GetPublicKey();

        if (key == null || Convert.ToBase64String(key) != expectedPublicKey)
            throw new SecurityException($"Unsigned or mismatched assembly: {dllPath}");

        // Find and invoke the mod entry point
        var entryType = asm.GetTypes()
            .FirstOrDefault(t => t.GetInterface("IModPlugin") != null);
        if (entryType != null)
        {
            var plugin = (IModPlugin)Activator.CreateInstance(entryType)!;
            plugin.Initialize();
        }
    }
}

public interface IModPlugin
{
    void Initialize();
    void Shutdown();
}
```

---

## Steam Workshop Integration

If shipping on Steam, Workshop integration gives your mods discoverability, auto-updates, and one-click install.

### Workflow

```
Modder creates mod locally → Tests in mods/ folder
    → Publishes to Workshop via in-game tool or CLI
        → Players subscribe → Steam auto-downloads to workshop folder
            → Game discovers workshop mods alongside local mods
```

### Key Steamworks Calls

```csharp
/// <summary>
/// Minimal Workshop integration using Steamworks.NET.
/// Handles publishing and discovering subscribed items.
/// </summary>
public static class WorkshopIntegration
{
    /// <summary>Gets paths to all Workshop mods the player is subscribed to.</summary>
    public static List<string> GetSubscribedModPaths()
    {
        var paths = new List<string>();
        uint count = SteamUGC.GetNumSubscribedItems();
        var ids = new PublishedFileId_t[count];
        SteamUGC.GetSubscribedItems(ids, count);

        foreach (var id in ids)
        {
            if (SteamUGC.GetItemInstallInfo(id, out _, out string folder, 1024, out _))
                paths.Add(folder);
        }

        return paths;
    }

    /// <summary>
    /// Creates or updates a Workshop item from a mod folder.
    /// Call from an in-game "Publish Mod" UI.
    /// </summary>
    public static void PublishMod(string modPath, string title, string description,
        string previewImagePath, Action<bool> onComplete)
    {
        var createCall = SteamUGC.CreateItem(
            SteamUtils.GetAppID(), EWorkshopFileType.k_EWorkshopFileTypeCommunity);

        // In production, use CallResult<T> for async handling.
        // Simplified here for clarity:
        var handle = SteamUGC.StartItemUpdate(SteamUtils.GetAppID(),
            new PublishedFileId_t(0)); // 0 = new item

        SteamUGC.SetItemTitle(handle, title);
        SteamUGC.SetItemDescription(handle, description);
        SteamUGC.SetItemContent(handle, modPath);
        SteamUGC.SetItemPreview(handle, previewImagePath);
        SteamUGC.SetItemVisibility(handle,
            ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic);

        SteamUGC.SubmitItemUpdate(handle, "Initial upload");
        // Wire CallResult for completion callback
    }
}
```

Workshop mods are mounted the same way as local mods — the `ModLoader` just checks both `mods/` and the Workshop download directories.

---

## Practical Example — Complete Minimal Mod System

Pulling everything together into a working system that integrates with your MonoGame + Arch ECS game loop.

### Integration Point

```csharp
using Arch.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

/// <summary>
/// Central mod system that ties together VFS, mod loading, script engine,
/// hot-reloading, and blueprint registration. Initialize once at startup.
/// </summary>
public sealed class ModSystem : IDisposable
{
    public VirtualFileSystem Vfs { get; } = new();
    public ModLoader Loader { get; private set; } = null!;
    public ModScriptEngine ScriptEngine { get; } = new();
    public BlueprintRegistry Blueprints { get; } = new();
    public ModHotReloader? HotReloader { get; private set; }

    private readonly string _baseContentPath;
    private readonly string _modsPath;
    private readonly string _gameVersion;
    private readonly bool _enableHotReload;

    public ModSystem(string baseContentPath, string modsPath,
        string gameVersion, bool enableHotReload = false)
    {
        _baseContentPath = baseContentPath;
        _modsPath = modsPath;
        _gameVersion = gameVersion;
        _enableHotReload = enableHotReload;
    }

    /// <summary>
    /// Full initialization sequence. Call during Game.LoadContent or earlier.
    /// Returns a list of non-fatal warnings/errors for display.
    /// </summary>
    public List<string> Initialize()
    {
        var warnings = new List<string>();

        // 1. Mount base content
        Vfs.MountBase(_baseContentPath);

        // 2. Discover and load mods
        Loader = new ModLoader(Vfs, _modsPath, _gameVersion);
        warnings.AddRange(Loader.LoadAll());

        // 3. Detect conflicts
        warnings.AddRange(DetectConflicts(Loader.LoadedMods));

        // 4. Load blueprints from merged VFS
        Blueprints.LoadFromDirectory(Vfs, "entities");
        Blueprints.LoadFromDirectory(Vfs, "items");
        Blueprints.LoadFromDirectory(Vfs, "recipes");

        Console.WriteLine($"[ModSystem] {Blueprints.AllIds.Count()} blueprints registered");

        // 5. Load Lua scripts from each mod
        foreach (var mod in Loader.LoadedMods)
        {
            if (!Directory.Exists(mod.ScriptsPath)) continue;

            foreach (string lua in Directory.EnumerateFiles(
                mod.ScriptsPath, "*.lua", SearchOption.AllDirectories))
            {
                try
                {
                    ScriptEngine.LoadScript(mod.Manifest.Id, lua);
                }
                catch (Exception ex)
                {
                    warnings.Add($"Script error in {mod.Manifest.Id}: {ex.Message}");
                }
            }
        }

        // 6. Hot-reload (dev builds only)
        if (_enableHotReload)
        {
            HotReloader = new ModHotReloader(Vfs, _baseContentPath);

            foreach (var mod in Loader.LoadedMods)
            {
                if (Directory.Exists(mod.ContentPath))
                    HotReloader.Watch(mod.ContentPath);
                if (Directory.Exists(mod.ScriptsPath))
                    HotReloader.Watch(mod.ScriptsPath);
            }

            HotReloader.OnAssetReloaded += path =>
            {
                if (path.EndsWith(".json"))
                {
                    Blueprints.LoadFromDirectory(Vfs, "entities");
                    Blueprints.LoadFromDirectory(Vfs, "items");
                }
            };
        }

        return warnings;
    }

    /// <summary>Call every frame for hot-reload and Lua tick events.</summary>
    public void Update(GameTime gameTime)
    {
        HotReloader?.Update();

        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        ScriptEngine.FireEvent("onTick", dt);
    }

    // Convenience methods for firing events from game systems
    public void OnEntitySpawn(string entityId, string blueprintId, float x, float y)
        => ScriptEngine.FireEvent("onEntitySpawn", entityId, blueprintId, x, y);

    public void OnEntityDeath(string entityId, string entityType)
        => ScriptEngine.FireEvent("onEntityDeath", entityId, entityType);

    public void OnDamage(string targetId, string sourceId, float amount)
        => ScriptEngine.FireEvent("onDamage", targetId, sourceId, amount);

    public void OnItemUse(string playerId, string itemId, float x, float y)
        => ScriptEngine.FireEvent("onItemUse", playerId, itemId, x, y);

    public void Dispose() => HotReloader?.Dispose();
}
```

### Wiring Into Your Game

```csharp
public class MyGame : Game
{
    private ModSystem _mods = null!;
    private World _world = null!;

    protected override void LoadContent()
    {
        _world = World.Create();

        string basePath = Path.Combine(Content.RootDirectory, "data");
        string modsPath = Path.Combine(AppContext.BaseDirectory, "mods");

        #if DEBUG
        bool hotReload = true;
        #else
        bool hotReload = false;
        #endif

        _mods = new ModSystem(basePath, modsPath, "1.0.0", hotReload);

        var warnings = _mods.Initialize();
        foreach (string w in warnings)
            Console.WriteLine($"[Warning] {w}");
    }

    protected override void Update(GameTime gameTime)
    {
        _mods.Update(gameTime);
        base.Update(gameTime);
    }

    protected override void UnloadContent()
    {
        _mods.Dispose();
        _world.Dispose();
    }
}
```

### Example Mod File Structure

```
mods/
└── goblin_invasion/
    ├── mod.json
    ├── content/
    │   ├── entities/
    │   │   └── goblin.json
    │   ├── items/
    │   │   └── goblin_ear.json
    │   ├── sprites/
    │   │   └── goblin.png
    │   └── recipes/
    │       └── goblin_trophy.json
    └── scripts/
        └── invasion.lua
```

### Complete mod.json

```json
{
    "id": "goblin_invasion",
    "name": "Goblin Invasion",
    "version": "1.0.0",
    "author": "ModderSteve",
    "description": "Goblins attack your village every night!",
    "gameVersionMin": "0.9.0",
    "gameVersionMax": "1.*",
    "dependencies": [],
    "loadAfter": [],
    "tags": ["gameplay", "enemies", "events"]
}
```

---

## Summary

| Layer | Effort | Modding Power | When to Add |
|---|---|---|---|
| Data-driven design | Low | ★★☆☆☆ | Day 1 — do this regardless |
| Asset overrides (VFS) | Low | ★★★☆☆ | Early — improves your own workflow |
| Mod manifests + loader | Medium | ★★★☆☆ | Before first public build |
| Blueprint merging | Medium | ★★★★☆ | When you have stable entity/item formats |
| Lua scripting | Medium | ★★★★★ | When modders ask for behavior changes |
| Hot-reload | Low | ★☆☆☆☆ (dev QoL) | During active modding development |
| Workshop integration | Medium | ★★★☆☆ | When shipping on Steam |
| .NET plugins | High | ★★★★★ | Only if absolutely necessary |

Start with data-driven architecture — it makes your own development faster regardless of modding. Layer the VFS and asset overrides next; they're cheap and powerful. Add Lua scripting when the community is ready for it. Keep .NET assembly mods as a last resort, gated behind security warnings.

The best mod system is one that grows with your community. Ship the foundation, listen to what modders need, and expand from there.
