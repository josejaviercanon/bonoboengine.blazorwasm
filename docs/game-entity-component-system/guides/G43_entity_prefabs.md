# G43 — Entity Prefabs & Blueprint System


> **Category:** Guide · **Related:** [G10 Custom Game Systems](./G10_custom_game_systems.md) · [G12 Design Patterns](./G12_design_patterns.md) · [G37 Tilemap Systems](./G37_tilemap_systems.md) · [G8 Content Pipeline](./G8_content_pipeline.md)

---

## 1 — What's a Prefab / Blueprint?

A **blueprint** (or prefab) is a data-driven template that describes which components an entity should have, and their default values. Instead of hard-coding entity creation in C#, you declare archetypes in JSON files and spawn them at runtime — optionally applying per-instance overrides (position, HP, color, etc.).

**Why bother?**

| Hard-coded | Data-driven |
|---|---|
| New enemy type = recompile | New enemy type = new JSON file |
| Designers need C# access | Designers edit JSON / Tiled |
| Variants require subclasses | Variants inherit from a base blueprint |
| Tuning requires restart | Hot-reload JSON at runtime |

The blueprint system sits between your raw ECS layer (Arch) and your content pipeline, acting as the bridge between *data authored by designers* and *entities living in the world*.

---

## 2 — Blueprint Data Model

### 2.1 — JSON Schema

A single blueprint file contains an ID, optional parent, and a list of component blocks:

```json
{
  "id": "slime",
  "inherits": "enemy_base",
  "components": {
    "Transform": { "x": 0, "y": 0 },
    "Sprite": { "texture": "enemies/slime", "frameWidth": 16, "frameHeight": 16 },
    "Health": { "current": 3, "max": 3 },
    "Velocity": { "x": 0, "y": 0 },
    "EnemyTag": {},
    "Collider": { "width": 14, "height": 12, "offsetY": 4 },
    "AnimationState": { "currentAnim": "idle", "frame": 0, "elapsed": 0 }
  }
}
```

Each key under `"components"` maps to a C# `record struct` by name. Empty objects (`{}`) mean "add the component with default values."

### 2.2 — Blueprint Record

```csharp
public sealed class Blueprint
{
    public string Id { get; set; } = "";
    public string? Inherits { get; set; }
    public Dictionary<string, JsonElement> Components { get; set; } = new();
}
```

### 2.3 — Blueprint Registry

```csharp
public sealed class BlueprintRegistry
{
    private readonly Dictionary<string, Blueprint> _blueprints = new();
    private readonly string _blueprintsDir;

    public BlueprintRegistry(string blueprintsDir)
    {
        _blueprintsDir = blueprintsDir;
    }

    public void LoadAll()
    {
        _blueprints.Clear();
        foreach (var file in Directory.EnumerateFiles(_blueprintsDir, "*.json", SearchOption.AllDirectories))
        {
            var json = File.ReadAllText(file);
            var bp = JsonSerializer.Deserialize<Blueprint>(json, SerializerCtx.Default.Blueprint);
            if (bp is not null && !string.IsNullOrEmpty(bp.Id))
                _blueprints[bp.Id] = bp;
        }
    }

    public Blueprint? Get(string id) =>
        _blueprints.TryGetValue(id, out var bp) ? bp : null;

    public IReadOnlyDictionary<string, Blueprint> All => _blueprints;

    /// <summary>Resolve a blueprint with all inherited components merged.</summary>
    public Dictionary<string, JsonElement> Resolve(string id)
    {
        var merged = new Dictionary<string, JsonElement>();
        var chain = BuildInheritanceChain(id);

        // Apply from root ancestor → most derived
        foreach (var bp in chain)
            foreach (var (key, value) in bp.Components)
                merged[key] = value;

        return merged;
    }

    private List<Blueprint> BuildInheritanceChain(string id)
    {
        var chain = new List<Blueprint>();
        var visited = new HashSet<string>();
        var current = id;

        while (current is not null)
        {
            if (!visited.Add(current))
                throw new InvalidOperationException($"Circular blueprint inheritance: {current}");

            var bp = Get(current) ?? throw new KeyNotFoundException($"Blueprint not found: {current}");
            chain.Add(bp);
            current = bp.Inherits;
        }

        chain.Reverse(); // root-first
        return chain;
    }

    /// <summary>Hot-reload: re-reads all files and replaces in-memory data.</summary>
    public void Reload() => LoadAll();
}
```

---

## 3 — Component Serialization

### 3.1 — Type Registry

Every component that can appear in a blueprint needs to be registered by its string name:

```csharp
public static class ComponentTypeRegistry
{
    private static readonly Dictionary<string, Type> _types = new(StringComparer.OrdinalIgnoreCase);

    public static void Register<T>(string name) where T : struct =>
        _types[name] = typeof(T);

    public static Type? Lookup(string name) =>
        _types.TryGetValue(name, out var t) ? t : null;

    public static IReadOnlyDictionary<string, Type> All => _types;
}
```

Register during startup:

```csharp
ComponentTypeRegistry.Register<Transform>("Transform");
ComponentTypeRegistry.Register<Sprite>("Sprite");
ComponentTypeRegistry.Register<Health>("Health");
ComponentTypeRegistry.Register<Velocity>("Velocity");
ComponentTypeRegistry.Register<EnemyTag>("EnemyTag");
ComponentTypeRegistry.Register<Collider>("Collider");
ComponentTypeRegistry.Register<AnimationState>("AnimationState");
ComponentTypeRegistry.Register<Loot>("Loot");
ComponentTypeRegistry.Register<Patrol>("Patrol");
ComponentTypeRegistry.Register<ContactDamage>("ContactDamage");
```

### 3.2 — Example Components

```csharp
public record struct Transform(float X, float Y, float Rotation, float ScaleX, float ScaleY)
{
    public Transform() : this(0, 0, 0, 1f, 1f) { }
}

public record struct Sprite(string Texture, int FrameWidth, int FrameHeight, int Layer)
{
    public Sprite() : this("", 16, 16, 0) { }
}

public record struct Health(int Current, int Max)
{
    public Health() : this(1, 1) { }
}

public record struct Velocity(float X, float Y)
{
    public Velocity() : this(0, 0) { }
}

public record struct EnemyTag();

public record struct Collider(float Width, float Height, float OffsetX, float OffsetY)
{
    public Collider() : this(16, 16, 0, 0) { }
}

public record struct AnimationState(string CurrentAnim, int Frame, float Elapsed)
{
    public AnimationState() : this("idle", 0, 0f) { }
}

public record struct ContactDamage(int Damage)
{
    public ContactDamage() : this(1) { }
}

public record struct Patrol(float LeftBound, float RightBound, float Speed)
{
    public Patrol() : this(0, 64, 30f) { }
}

public record struct Loot(string TableId)
{
    public Loot() : this("") { }
}
```

### 3.3 — Source-Generated JSON Context

Use `System.Text.Json` source generators for AOT-friendly, allocation-light serialization:

```csharp
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault)]
[JsonSerializable(typeof(Blueprint))]
[JsonSerializable(typeof(Transform))]
[JsonSerializable(typeof(Sprite))]
[JsonSerializable(typeof(Health))]
[JsonSerializable(typeof(Velocity))]
[JsonSerializable(typeof(EnemyTag))]
[JsonSerializable(typeof(Collider))]
[JsonSerializable(typeof(AnimationState))]
[JsonSerializable(typeof(ContactDamage))]
[JsonSerializable(typeof(Patrol))]
[JsonSerializable(typeof(Loot))]
[JsonSerializable(typeof(SpawnTable))]
[JsonSerializable(typeof(SpawnTableEntry))]
internal partial class SerializerCtx : JsonSerializerContext { }
```

### 3.4 — Deserializing a Component from JsonElement

```csharp
public static class ComponentDeserializer
{
    /// <summary>
    /// Deserialize a JsonElement into a boxed component struct using the type registry.
    /// </summary>
    public static object? Deserialize(string componentName, JsonElement element)
    {
        var type = ComponentTypeRegistry.Lookup(componentName);
        if (type is null) return null;

        return JsonSerializer.Deserialize(element.GetRawText(), type, SerializerCtx.Default);
    }
}
```

---

## 4 — Entity Factory

The `EntityFactory` is the single entry point for spawning entities from blueprints. It resolves inheritance, deserializes every component, and attaches them to a fresh Arch entity.

```csharp
public sealed class EntityFactory
{
    private readonly World _world;
    private readonly BlueprintRegistry _registry;

    public EntityFactory(World world, BlueprintRegistry registry)
    {
        _world = world;
        _registry = registry;
    }

    /// <summary>
    /// Spawn an entity from a blueprint with optional position override.
    /// </summary>
    public Entity Spawn(string blueprintId, Vector2? position = null,
                        Dictionary<string, JsonElement>? overrides = null)
    {
        var resolved = _registry.Resolve(blueprintId);

        // Apply per-instance overrides (component-level replacement)
        if (overrides is not null)
            foreach (var (key, value) in overrides)
                resolved[key] = value;

        // Create a bare entity
        var entity = _world.Create();

        // Attach each component
        foreach (var (name, element) in resolved)
        {
            var component = ComponentDeserializer.Deserialize(name, element);
            if (component is null) continue;

            AddComponentDynamic(entity, component);
        }

        // Apply position override directly on the Transform component
        if (position.HasValue && entity.Has<Transform>())
        {
            var t = entity.Get<Transform>();
            entity.Set(t with { X = position.Value.X, Y = position.Value.Y });
        }

        return entity;
    }

    /// <summary>
    /// Use Arch's generic Set via reflection to attach a boxed struct.
    /// Cache the MethodInfo per type for performance.
    /// </summary>
    private static readonly Dictionary<Type, Action<Entity, object>> _setters = new();

    private static void AddComponentDynamic(Entity entity, object component)
    {
        var type = component.GetType();

        if (!_setters.TryGetValue(type, out var setter))
        {
            // Build: entity.Add<T>(); entity.Set<T>(value);
            var addMethod = typeof(EntityExtensions)
                .GetMethods().First(m => m.Name == "Add" && m.GetGenericArguments().Length == 1)
                .MakeGenericMethod(type);

            var setMethod = typeof(EntityExtensions)
                .GetMethods().First(m => m.Name == "Set" && m.GetGenericArguments().Length == 1)
                .MakeGenericMethod(type);

            setter = (e, val) =>
            {
                addMethod.Invoke(null, new object[] { e });
                setMethod.Invoke(null, new object[] { e, val });
            };

            _setters[type] = setter;
        }

        setter(entity, component);
    }
}
```

### 4.1 — Usage

```csharp
// During initialization
var registry = new BlueprintRegistry("Content/Blueprints");
registry.LoadAll();
var factory = new EntityFactory(world, registry);

// Spawn a slime at a specific position
var slime = factory.Spawn("slime", position: new Vector2(128, 256));

// Spawn with custom overrides
var toughSlime = factory.Spawn("slime", position: new Vector2(200, 300),
    overrides: new Dictionary<string, JsonElement>
    {
        ["Health"] = JsonSerializer.SerializeToElement(
            new Health(10, 10), SerializerCtx.Default.Health)
    });
```

---

## 5 — Blueprint Inheritance & Composition

Inheritance lets you define a base archetype and create variants without duplicating data.

### 5.1 — Base Enemy Blueprint

**`Content/Blueprints/enemy_base.json`**
```json
{
  "id": "enemy_base",
  "components": {
    "Transform": { "x": 0, "y": 0, "scaleX": 1, "scaleY": 1 },
    "Velocity": {},
    "Health": { "current": 5, "max": 5 },
    "EnemyTag": {},
    "Collider": { "width": 16, "height": 16 },
    "ContactDamage": { "damage": 1 },
    "AnimationState": {}
  }
}
```

### 5.2 — Slime (extends enemy_base)

**`Content/Blueprints/enemies/slime.json`**
```json
{
  "id": "slime",
  "inherits": "enemy_base",
  "components": {
    "Sprite": { "texture": "enemies/slime", "frameWidth": 16, "frameHeight": 16 },
    "Health": { "current": 3, "max": 3 },
    "Collider": { "width": 14, "height": 12, "offsetY": 4 },
    "Patrol": { "leftBound": 0, "rightBound": 64, "speed": 25 },
    "Loot": { "tableId": "loot_slime" }
  }
}
```

The resolved component set merges `enemy_base` and `slime`. Slime's `Health` overrides the base entirely — component-level granularity, not field-level.

### 5.3 — Boss Slime (extends slime)

**`Content/Blueprints/enemies/boss_slime.json`**
```json
{
  "id": "boss_slime",
  "inherits": "slime",
  "components": {
    "Sprite": { "texture": "enemies/boss_slime", "frameWidth": 32, "frameHeight": 32 },
    "Health": { "current": 25, "max": 25 },
    "Collider": { "width": 28, "height": 24, "offsetY": 8 },
    "Transform": { "scaleX": 2, "scaleY": 2 },
    "ContactDamage": { "damage": 3 },
    "Loot": { "tableId": "loot_boss_slime" }
  }
}
```

**Inheritance chain:** `enemy_base` → `slime` → `boss_slime`. Each layer only declares what it changes.

### 5.4 — Resolution Order

```
enemy_base.Components   →  base layer
  ↓ merge
slime.Components        →  overrides Health, adds Sprite/Patrol/Loot
  ↓ merge
boss_slime.Components   →  overrides Sprite/Health/Collider/Transform/ContactDamage/Loot
```

Components not mentioned in a child are inherited unchanged. Components redeclared in a child **replace** the parent's version wholesale (simple override, no deep merge).

---

## 6 — Tiled Object Integration

Level designers place **point objects** or **rectangle objects** in Tiled with a custom property `blueprint` set to a blueprint ID. At load time, you iterate Tiled's object layers and call `EntityFactory.Spawn`.

### 6.1 — Tiled Object Properties

In Tiled, on an object in the "Entities" layer:

| Property | Type | Example |
|---|---|---|
| `blueprint` | `string` | `"slime"` |
| `health` | `int` | `10` (optional override) |
| `patrolRange` | `float` | `96` (optional) |

### 6.2 — Spawner Code

```csharp
public static class TiledEntitySpawner
{
    /// <summary>
    /// Spawn all blueprint-tagged objects from a Tiled map's object layers.
    /// Assumes you've already parsed the .tmx/.tmj into a TiledMap structure.
    /// </summary>
    public static List<Entity> SpawnFromMap(TiledMap map, EntityFactory factory)
    {
        var spawned = new List<Entity>();

        foreach (var layer in map.ObjectLayers)
        {
            foreach (var obj in layer.Objects)
            {
                var blueprintId = obj.GetStringProperty("blueprint");
                if (string.IsNullOrEmpty(blueprintId)) continue;

                var position = new Vector2(obj.X, obj.Y);

                // Build per-instance overrides from Tiled custom properties
                var overrides = BuildOverrides(obj);

                var entity = factory.Spawn(blueprintId, position, overrides);
                spawned.Add(entity);
            }
        }

        return spawned;
    }

    private static Dictionary<string, JsonElement>? BuildOverrides(TiledObject obj)
    {
        Dictionary<string, JsonElement>? overrides = null;

        // Example: override health from Tiled property
        if (obj.TryGetIntProperty("health", out int hp))
        {
            overrides ??= new();
            overrides["Health"] = JsonSerializer.SerializeToElement(
                new Health(hp, hp), SerializerCtx.Default.Health);
        }

        // Example: override patrol range
        if (obj.TryGetFloatProperty("patrolRange", out float range))
        {
            overrides ??= new();
            overrides["Patrol"] = JsonSerializer.SerializeToElement(
                new Patrol(-range / 2, range / 2, 30f), SerializerCtx.Default.Patrol);
        }

        return overrides;
    }
}
```

### 6.3 — Level Load Flow

```csharp
public void LoadLevel(string mapPath)
{
    var map = TiledMapLoader.Load(mapPath);

    // Build tilemap visuals (see G37)
    _tilemapRenderer.BuildFrom(map);

    // Spawn all entities from object layers
    var entities = TiledEntitySpawner.SpawnFromMap(map, _entityFactory);

    Console.WriteLine($"Spawned {entities.Count} entities from {mapPath}");
}
```

This gives designers full control over enemy placement, NPC positions, trigger zones, and item pickups — all without touching C#.

---

## 7 — Runtime Blueprint Editing

### 7.1 — ImGui Blueprint Inspector

```csharp
public sealed class BlueprintInspector
{
    private readonly BlueprintRegistry _registry;
    private string _selectedId = "";
    private string _searchFilter = "";

    public BlueprintInspector(BlueprintRegistry registry)
    {
        _registry = registry;
    }

    public void Draw()
    {
        if (!ImGui.Begin("Blueprint Inspector")) { ImGui.End(); return; }

        // Search bar
        ImGui.InputText("Filter", ref _searchFilter, 128);
        ImGui.Separator();

        // Blueprint list
        ImGui.BeginChild("list", new System.Numerics.Vector2(200, 0), ImGuiChildFlags.Border);
        foreach (var (id, _) in _registry.All)
        {
            if (!string.IsNullOrEmpty(_searchFilter) &&
                !id.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            if (ImGui.Selectable(id, _selectedId == id))
                _selectedId = id;
        }
        ImGui.EndChild();

        ImGui.SameLine();

        // Component editor
        ImGui.BeginChild("editor");
        if (!string.IsNullOrEmpty(_selectedId))
            DrawBlueprintEditor(_selectedId);
        ImGui.EndChild();

        ImGui.End();
    }

    private void DrawBlueprintEditor(string id)
    {
        var bp = _registry.Get(id);
        if (bp is null) { ImGui.Text("Not found"); return; }

        ImGui.Text($"Blueprint: {id}");
        if (bp.Inherits is not null)
            ImGui.TextColored(new System.Numerics.Vector4(0.6f, 0.8f, 1f, 1f),
                $"Inherits: {bp.Inherits}");
        ImGui.Separator();

        // Show resolved components
        var resolved = _registry.Resolve(id);
        foreach (var (name, element) in resolved)
        {
            if (ImGui.TreeNode(name))
            {
                var json = element.GetRawText();
                ImGui.TextWrapped(json);
                ImGui.TreePop();
            }
        }

        ImGui.Separator();
        if (ImGui.Button("Reload All Blueprints"))
            _registry.Reload();
    }
}
```

### 7.2 — Hot-Reload with File Watcher

```csharp
public sealed class BlueprintFileWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly BlueprintRegistry _registry;
    private DateTime _lastReload = DateTime.MinValue;

    public BlueprintFileWatcher(string directory, BlueprintRegistry registry)
    {
        _registry = registry;
        _watcher = new FileSystemWatcher(directory, "*.json")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
            IncludeSubdirectories = true,
            EnableRaisingEvents = true
        };
        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        // Debounce: ignore rapid successive events
        if ((DateTime.Now - _lastReload).TotalMilliseconds < 500) return;
        _lastReload = DateTime.Now;

        Console.WriteLine($"[Blueprints] Detected change in {e.Name}, reloading...");
        _registry.Reload();
    }

    public void Dispose() => _watcher.Dispose();
}
```

Initialize once:

```csharp
var watcher = new BlueprintFileWatcher("Content/Blueprints", registry);
// Now editing any JSON file triggers a live reload
```

---

## 8 — Spawn Tables & Weighted Random

### 8.1 — Spawn Table Data Model

```csharp
public sealed class SpawnTable
{
    public string Id { get; set; } = "";
    public List<SpawnTableEntry> Entries { get; set; } = new();
}

public sealed class SpawnTableEntry
{
    public string BlueprintId { get; set; } = "";
    public int Weight { get; set; } = 1;
    public int Min { get; set; } = 1;
    public int Max { get; set; } = 1;
}
```

### 8.2 — Example JSON

**`Content/Data/spawn_tables/loot_slime.json`**
```json
{
  "id": "loot_slime",
  "entries": [
    { "blueprintId": "coin_small",  "weight": 60, "min": 1, "max": 3 },
    { "blueprintId": "coin_large",  "weight": 20, "min": 1, "max": 1 },
    { "blueprintId": "health_orb",  "weight": 15, "min": 1, "max": 1 },
    { "blueprintId": "slime_jelly", "weight": 5,  "min": 1, "max": 1 }
  ]
}
```

### 8.3 — Weighted Random Roller

```csharp
public sealed class SpawnTableRoller
{
    private readonly Dictionary<string, SpawnTable> _tables = new();
    private readonly Random _rng = new();

    public void LoadAll(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories))
        {
            var json = File.ReadAllText(file);
            var table = JsonSerializer.Deserialize<SpawnTable>(json, SerializerCtx.Default.SpawnTable);
            if (table is not null)
                _tables[table.Id] = table;
        }
    }

    /// <summary>
    /// Roll a spawn table and return the selected blueprint IDs with quantities.
    /// </summary>
    public List<(string BlueprintId, int Count)> Roll(string tableId)
    {
        if (!_tables.TryGetValue(tableId, out var table))
            return new();

        var results = new List<(string, int)>();
        int totalWeight = table.Entries.Sum(e => e.Weight);
        int roll = _rng.Next(totalWeight);
        int cumulative = 0;

        foreach (var entry in table.Entries)
        {
            cumulative += entry.Weight;
            if (roll < cumulative)
            {
                int count = _rng.Next(entry.Min, entry.Max + 1);
                results.Add((entry.BlueprintId, count));
                break;
            }
        }

        return results;
    }

    /// <summary>
    /// Roll and immediately spawn the results via the factory.
    /// </summary>
    public List<Entity> RollAndSpawn(string tableId, EntityFactory factory, Vector2 position)
    {
        var entities = new List<Entity>();
        var results = Roll(tableId);

        foreach (var (blueprintId, count) in results)
        {
            for (int i = 0; i < count; i++)
            {
                // Slight scatter around the drop point
                var offset = new Vector2(
                    (_rng.NextSingle() - 0.5f) * 16f,
                    (_rng.NextSingle() - 0.5f) * 16f);
                entities.Add(factory.Spawn(blueprintId, position + offset));
            }
        }

        return entities;
    }
}
```

---

## 9 — Prefab Pooling

Combine blueprints with entity pooling to avoid allocation spikes during gameplay. See also [G14 Object Pooling](./G14_object_pooling.md).

### 9.1 — Blueprint-Aware Pool

```csharp
public sealed class EntityPool
{
    private readonly Dictionary<string, Queue<Entity>> _pools = new();
    private readonly EntityFactory _factory;
    private readonly World _world;

    public EntityPool(EntityFactory factory, World world)
    {
        _factory = factory;
        _world = world;
    }

    /// <summary>Pre-warm the pool with N inactive entities for a given blueprint.</summary>
    public void PreWarm(string blueprintId, int count)
    {
        if (!_pools.ContainsKey(blueprintId))
            _pools[blueprintId] = new Queue<Entity>();

        for (int i = 0; i < count; i++)
        {
            var entity = _factory.Spawn(blueprintId);
            entity.Add(new Inactive()); // marker component to skip in systems
            _pools[blueprintId].Enqueue(entity);
        }
    }

    /// <summary>Get an entity from the pool, or spawn a new one if empty.</summary>
    public Entity Get(string blueprintId, Vector2 position)
    {
        Entity entity;

        if (_pools.TryGetValue(blueprintId, out var queue) && queue.Count > 0)
        {
            entity = queue.Dequeue();
            entity.Remove<Inactive>();

            // Reset transform
            if (entity.Has<Transform>())
                entity.Set(entity.Get<Transform>() with { X = position.X, Y = position.Y });
        }
        else
        {
            entity = _factory.Spawn(blueprintId, position);
        }

        return entity;
    }

    /// <summary>Return an entity to the pool for later reuse.</summary>
    public void Return(string blueprintId, Entity entity)
    {
        entity.Add(new Inactive());

        if (!_pools.ContainsKey(blueprintId))
            _pools[blueprintId] = new Queue<Entity>();

        _pools[blueprintId].Enqueue(entity);
    }
}

/// <summary>Marker component: entity exists but is inactive (pooled).</summary>
public record struct Inactive();
```

### 9.2 — Usage in an Enemy Death System

```csharp
public void OnEnemyDeath(Entity entity, string blueprintId, EntityPool pool,
                          SpawnTableRoller roller, EntityFactory factory)
{
    var pos = new Vector2(entity.Get<Transform>().X, entity.Get<Transform>().Y);

    // Drop loot
    if (entity.Has<Loot>())
    {
        var tableId = entity.Get<Loot>().TableId;
        roller.RollAndSpawn(tableId, factory, pos);
    }

    // Return to pool instead of destroying
    pool.Return(blueprintId, entity);
}
```

---

## 10 — Practical Example: Complete Enemy Blueprint System

Putting it all together — the full file set for a working enemy blueprint pipeline.

### 10.1 — Blueprint JSON Files

**`Content/Blueprints/enemy_base.json`**
```json
{
  "id": "enemy_base",
  "components": {
    "Transform": {},
    "Velocity": {},
    "Health": { "current": 5, "max": 5 },
    "EnemyTag": {},
    "Collider": { "width": 16, "height": 16 },
    "ContactDamage": { "damage": 1 },
    "AnimationState": {}
  }
}
```

**`Content/Blueprints/enemies/slime.json`**
```json
{
  "id": "slime",
  "inherits": "enemy_base",
  "components": {
    "Sprite": { "texture": "enemies/slime", "frameWidth": 16, "frameHeight": 16 },
    "Health": { "current": 3, "max": 3 },
    "Collider": { "width": 14, "height": 12, "offsetY": 4 },
    "Patrol": { "leftBound": 0, "rightBound": 64, "speed": 25 },
    "Loot": { "tableId": "loot_slime" }
  }
}
```

**`Content/Blueprints/enemies/skeleton.json`**
```json
{
  "id": "skeleton",
  "inherits": "enemy_base",
  "components": {
    "Sprite": { "texture": "enemies/skeleton", "frameWidth": 16, "frameHeight": 24, "layer": 1 },
    "Health": { "current": 8, "max": 8 },
    "Collider": { "width": 12, "height": 22, "offsetY": 2 },
    "Patrol": { "leftBound": 0, "rightBound": 96, "speed": 35 },
    "ContactDamage": { "damage": 2 },
    "Loot": { "tableId": "loot_skeleton" }
  }
}
```

**`Content/Blueprints/enemies/bat.json`**
```json
{
  "id": "bat",
  "inherits": "enemy_base",
  "components": {
    "Sprite": { "texture": "enemies/bat", "frameWidth": 16, "frameHeight": 16 },
    "Health": { "current": 2, "max": 2 },
    "Collider": { "width": 12, "height": 10, "offsetY": 3 },
    "Loot": { "tableId": "loot_bat" }
  }
}
```

### 10.2 — Startup Wiring

```csharp
public class GameMain : Game
{
    private World _world;
    private BlueprintRegistry _blueprintRegistry;
    private EntityFactory _entityFactory;
    private EntityPool _entityPool;
    private SpawnTableRoller _spawnRoller;
    private BlueprintFileWatcher _blueprintWatcher;
    private BlueprintInspector _inspector;

    protected override void Initialize()
    {
        // Register all component types
        ComponentTypeRegistry.Register<Transform>("Transform");
        ComponentTypeRegistry.Register<Sprite>("Sprite");
        ComponentTypeRegistry.Register<Health>("Health");
        ComponentTypeRegistry.Register<Velocity>("Velocity");
        ComponentTypeRegistry.Register<EnemyTag>("EnemyTag");
        ComponentTypeRegistry.Register<Collider>("Collider");
        ComponentTypeRegistry.Register<AnimationState>("AnimationState");
        ComponentTypeRegistry.Register<ContactDamage>("ContactDamage");
        ComponentTypeRegistry.Register<Patrol>("Patrol");
        ComponentTypeRegistry.Register<Loot>("Loot");

        // ECS world
        _world = World.Create();

        // Blueprint system
        _blueprintRegistry = new BlueprintRegistry("Content/Blueprints");
        _blueprintRegistry.LoadAll();

        _entityFactory = new EntityFactory(_world, _blueprintRegistry);
        _entityPool = new EntityPool(_entityFactory, _world);
        _spawnRoller = new SpawnTableRoller();
        _spawnRoller.LoadAll("Content/Data/spawn_tables");

        // Pre-warm common enemies
        _entityPool.PreWarm("slime", 10);
        _entityPool.PreWarm("skeleton", 5);
        _entityPool.PreWarm("bat", 8);

        // Hot-reload watcher (debug builds only)
        #if DEBUG
        _blueprintWatcher = new BlueprintFileWatcher("Content/Blueprints", _blueprintRegistry);
        _inspector = new BlueprintInspector(_blueprintRegistry);
        #endif

        base.Initialize();
    }

    protected override void LoadContent()
    {
        // Load a Tiled map and spawn entities from object layers
        var map = TiledMapLoader.Load("Content/Maps/level_01.tmj");
        TiledEntitySpawner.SpawnFromMap(map, _entityFactory);
    }

    protected override void Update(GameTime gameTime)
    {
        // Run ECS systems...
    }

    protected override void Draw(GameTime gameTime)
    {
        // Render...

        #if DEBUG
        _inspector?.Draw();
        #endif
    }

    protected override void Dispose(bool disposing)
    {
        _blueprintWatcher?.Dispose();
        _world.Dispose();
        base.Dispose(disposing);
    }
}
```

---

## Quick Reference

| Concept | Key Type | File |
|---|---|---|
| Blueprint definition | `Blueprint` | `*.json` in `Content/Blueprints/` |
| Component registry | `ComponentTypeRegistry` | Startup registration |
| Spawning | `EntityFactory.Spawn()` | Pass blueprint ID + overrides |
| Tiled integration | `TiledEntitySpawner` | Object layer → entities |
| Inheritance | `BlueprintRegistry.Resolve()` | Walks chain root→leaf |
| Loot / spawn tables | `SpawnTableRoller` | `Content/Data/spawn_tables/` |
| Pooling | `EntityPool` | Pre-warm + Get/Return |
| Hot-reload | `BlueprintFileWatcher` | `FileSystemWatcher` on JSON dir |
| Debug editor | `BlueprintInspector` | ImGui window |

---

> **Design principle:** Entities are defined by data, not code. The blueprint system is the contract between designers and programmers — designers control *what* exists, programmers control *how* it behaves.
