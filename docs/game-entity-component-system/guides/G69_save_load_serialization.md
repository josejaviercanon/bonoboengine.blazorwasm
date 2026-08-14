# G69 — Save/Load & Serialization Systems

> **Category:** Guide · **Related:** [G10 Custom Game Systems §3](./G10_custom_game_systems.md) · [G38 Scene Management](./G38_scene_management.md) · [G64 Combat & Damage](./G64_combat_damage_systems.md) · [G65 Economy & Shop](./G65_economy_shop_systems.md) · [G53 Procedural Generation](./G53_procedural_generation.md) · [G67 Object Pooling](./G67_object_pooling.md) · [G48 Online Services](./G48_online_services.md)

> A complete implementation guide for save/load systems in MonoGame + Arch ECS. Covers ECS world serialization, versioned save formats, migration pipelines, autosave, async I/O, cloud saves, save encryption, thumbnail captures, and genre-specific patterns. Everything is composable — use the pieces your game needs.

---

## Table of Contents

1. [Design Philosophy](#1--design-philosophy)
2. [Saveable Component Registry](#2--saveable-component-registry)
3. [ECS World Serialization](#3--ecs-world-serialization)
4. [Save File Format & Versioning](#4--save-file-format--versioning)
5. [Version Migration Pipeline](#5--version-migration-pipeline)
6. [Save Slot Manager](#6--save-slot-manager)
7. [Autosave System](#7--autosave-system)
8. [Quicksave / Quickload](#8--quicksave--quickload)
9. [Async Save/Load](#9--async-saveload)
10. [Save Thumbnails & Screenshots](#10--save-thumbnails--screenshots)
11. [Save Integrity & Validation](#11--save-integrity--validation)
12. [Encryption & Anti-Tamper](#12--encryption--anti-tamper)
13. [Save Data Compression](#13--save-data-compression)
14. [Scene & Level State Persistence](#14--scene--level-state-persistence)
15. [Settings vs Profile vs Game State](#15--settings-vs-profile-vs-game-state)
16. [Cloud Save Integration](#16--cloud-save-integration)
17. [Testing Save Systems](#17--testing-save-systems)
18. [Genre-Specific Patterns](#18--genre-specific-patterns)
19. [Common Mistakes & Anti-Patterns](#19--common-mistakes--anti-patterns)
20. [Tuning Reference](#20--tuning-reference)

---

## 1 — Design Philosophy

### The Save Paradox

Save systems are deceptive. They seem simple — "just serialize the world and write it to disk." In practice, save/load is one of the hardest systems in a game because:

- **Everything touches it.** Every system that has state (health, inventory, quests, map, AI) needs to participate in serialization.
- **It must survive updates.** A save from v1.0 needs to load in v1.3. Breaking saves breaks trust.
- **It must be fast.** Players expect saving to be instantaneous. An autosave that hitches for 200ms during combat is unacceptable.
- **It must be correct.** A corrupt save file is worse than no save file. A save that loads but puts the player in a broken state is worse than a crash.

### Core Principles

```
1. EXPLICIT > IMPLICIT    — Mark what gets saved, don't save "everything"
2. DATA > REFERENCES      — Serialize values, not object graphs
3. VERSIONED > FRAGILE    — Every save file carries its schema version
4. INCREMENTAL > MONOLITH — Separate concerns (settings, profile, game state)
5. VALIDATE > TRUST       — Verify integrity before loading
```

### Save Pipeline Overview

```
Save Request
    │
    ▼
┌──────────────┐
│ Pause World  │  ← Freeze ECS updates during snapshot
└──────┬───────┘
       │
       ▼
┌──────────────┐
│ Collect      │  ← Query all Persistent entities
│ Snapshots    │  ← Serialize registered components
└──────┬───────┘
       │
       ▼
┌──────────────┐
│ Gather Extra │  ← Dialogue vars, quest state, timers, scene data
│ Systems      │
└──────┬───────┘
       │
       ▼
┌──────────────┐
│ Build Save   │  ← Assemble SaveFile with metadata + version
│ File         │
└──────┬───────┘
       │
       ▼
┌──────────────┐
│ Serialize    │  ← JSON + optional compression + optional encryption
│ & Write      │  ← Write to temp file, then atomic rename
└──────────────┘
```

```
Load Request
    │
    ▼
┌──────────────┐
│ Read & Parse │  ← Read file, decompress, decrypt, deserialize
└──────┬───────┘
       │
       ▼
┌──────────────┐
│ Validate     │  ← CRC check, version check, schema validation
└──────┬───────┘
       │
       ▼
┌──────────────┐
│ Migrate      │  ← Apply version migrations (v1→v2→v3→current)
└──────┬───────┘
       │
       ▼
┌──────────────┐
│ Clear World  │  ← Destroy all Persistent entities
└──────┬───────┘
       │
       ▼
┌──────────────┐
│ Restore      │  ← Recreate entities, attach components
│ Entities     │  ← Restore extra system state
└──────┬───────┘
       │
       ▼
┌──────────────┐
│ Post-Load    │  ← Rebuild caches, spatial grids, nav meshes
│ Fixup        │  ← Re-resolve entity references
└──────────────┘
```

### ECS Fit

In an ECS architecture, "saving the game" means:

1. **Find** all entities tagged for persistence.
2. **Read** their component data (already plain structs — ideal for serialization).
3. **Write** it as a flat list of component bundles.
4. **On load**, create new entities and attach deserialized components.

This is much cleaner than OOP save systems because there's no object graph, no circular references, no inheritance hierarchies. ECS components are plain data — serialization is natural.

---

## 2 — Saveable Component Registry

The naive approach serializes components manually with `SerializeIfPresent<Position>()`, `SerializeIfPresent<Health>()`, etc. This breaks every time you add a new component. Instead, build a registry.

### Component Serializer Interface

```csharp
/// <summary>
/// Handles serialization and deserialization of a single component type.
/// Each component that needs saving gets one of these.
/// </summary>
public interface IComponentSerializer
{
    /// <summary>Component type name used as the key in save files.</summary>
    string TypeKey { get; }

    /// <summary>True if the given entity has this component.</summary>
    bool Has(World world, Entity entity);

    /// <summary>Serialize the component to a JsonElement.</summary>
    JsonElement Serialize(World world, Entity entity);

    /// <summary>Deserialize and attach the component to an entity.</summary>
    void Deserialize(World world, Entity entity, JsonElement data);

    /// <summary>Remove this component from the entity (for migration/cleanup).</summary>
    void Remove(World world, Entity entity);
}
```

### Generic Implementation

```csharp
/// <summary>
/// Generic serializer for any struct component.
/// Works out of the box for simple record structs.
/// Override for components needing custom logic (entity references, textures, etc.).
/// </summary>
public class ComponentSerializer<T> : IComponentSerializer where T : struct
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public string TypeKey { get; }

    public ComponentSerializer(string typeKey)
    {
        TypeKey = typeKey;
    }

    public bool Has(World world, Entity entity) => world.Has<T>(entity);

    public virtual JsonElement Serialize(World world, Entity entity)
    {
        var component = world.Get<T>(entity);
        return JsonSerializer.SerializeToElement(component, JsonOpts);
    }

    public virtual void Deserialize(World world, Entity entity, JsonElement data)
    {
        var component = JsonSerializer.Deserialize<T>(data, JsonOpts);
        world.Set(entity, component);
    }

    public void Remove(World world, Entity entity)
    {
        if (world.Has<T>(entity))
            world.Remove<T>(entity);
    }
}
```

### The Registry

```csharp
/// <summary>
/// Central registry of all saveable components.
/// Add new components here — the save system discovers them automatically.
/// </summary>
public class SaveableRegistry
{
    private readonly Dictionary<string, IComponentSerializer> _serializers = new();

    public IReadOnlyDictionary<string, IComponentSerializer> Serializers => _serializers;

    public SaveableRegistry()
    {
        // --- Core ---
        Register(new ComponentSerializer<Position>("position"));
        Register(new ComponentSerializer<Velocity>("velocity"));
        Register(new ComponentSerializer<Facing>("facing"));

        // --- Combat ---
        Register(new ComponentSerializer<Health>("health"));
        Register(new ComponentSerializer<DamageResistances>("damageResistances"));
        Register(new ComponentSerializer<StatusEffects>("statusEffects"));

        // --- Inventory & Economy ---
        Register(new ComponentSerializer<Inventory>("inventory"));
        Register(new ComponentSerializer<Equipment>("equipment"));
        Register(new ComponentSerializer<Wallet>("wallet"));

        // --- AI ---
        Register(new ComponentSerializer<AiState>("aiState"));
        Register(new ComponentSerializer<PatrolRoute>("patrolRoute"));

        // --- World ---
        Register(new ComponentSerializer<Interactable>("interactable"));
        Register(new ComponentSerializer<LootContainer>("lootContainer"));
        Register(new ComponentSerializer<DestructibleState>("destructible"));

        // --- Identity ---
        Register(new ComponentSerializer<EntityId>("entityId"));
        Register(new ComponentSerializer<EntityName>("entityName"));
        Register(new ComponentSerializer<Faction>("faction"));
    }

    public void Register(IComponentSerializer serializer)
    {
        _serializers[serializer.TypeKey] = serializer;
    }

    public void Unregister(string typeKey)
    {
        _serializers.Remove(typeKey);
    }
}
```

### Custom Serializer Example — Entity References

Components that reference other entities can't serialize raw `Entity` values (they're just integers that change between sessions). Use stable IDs:

```csharp
// --- Component ---
public record struct FollowTarget(Entity Target);

// --- Stable ID for cross-entity references ---
public record struct EntityId(string Id);

// --- Custom serializer that converts Entity → stable ID ---
public class FollowTargetSerializer : IComponentSerializer
{
    public string TypeKey => "followTarget";

    public bool Has(World world, Entity entity) => world.Has<FollowTarget>(entity);

    public JsonElement Serialize(World world, Entity entity)
    {
        var follow = world.Get<FollowTarget>(entity);
        // Save the stable ID, not the Entity handle
        string targetId = "";
        if (world.IsAlive(follow.Target) && world.Has<EntityId>(follow.Target))
            targetId = world.Get<EntityId>(follow.Target).Id;

        return JsonSerializer.SerializeToElement(new { targetId });
    }

    public void Deserialize(World world, Entity entity, JsonElement data)
    {
        string targetId = data.GetProperty("targetId").GetString() ?? "";
        // Store unresolved — the post-load fixup phase resolves it
        world.Set(entity, new UnresolvedFollowTarget(targetId));
    }

    public void Remove(World world, Entity entity)
    {
        if (world.Has<FollowTarget>(entity))
            world.Remove<FollowTarget>(entity);
    }
}

// --- Temporary component for unresolved references ---
public record struct UnresolvedFollowTarget(string TargetId);

// --- Post-load fixup system resolves unresolved references ---
public static class EntityReferenceResolver
{
    public static void ResolveAll(World world)
    {
        // Build lookup: stable ID → Entity
        var idLookup = new Dictionary<string, Entity>();
        var idQuery = new QueryDescription().WithAll<EntityId>();
        world.Query(in idQuery, (Entity e, ref EntityId id) =>
        {
            idLookup[id.Id] = e;
        });

        // Resolve follow targets
        var unresolvedQuery = new QueryDescription().WithAll<UnresolvedFollowTarget>();
        var toResolve = new List<(Entity Entity, string TargetId)>();

        world.Query(in unresolvedQuery, (Entity e, ref UnresolvedFollowTarget unresolved) =>
        {
            toResolve.Add((e, unresolved.TargetId));
        });

        foreach (var (entity, targetId) in toResolve)
        {
            world.Remove<UnresolvedFollowTarget>(entity);
            if (idLookup.TryGetValue(targetId, out var target))
                world.Set(entity, new FollowTarget(target));
            // else: target doesn't exist in this save — entity has no follow target
        }
    }
}
```

### Auto-Discovery with Attributes (Optional)

For large projects, use attributes instead of manual registration:

```csharp
/// <summary>
/// Mark a component struct as saveable. The registry discovers it via reflection at startup.
/// </summary>
[AttributeUsage(AttributeTargets.Struct)]
public class SaveableAttribute : Attribute
{
    public string TypeKey { get; }
    public SaveableAttribute(string typeKey) => TypeKey = typeKey;
}

// --- Usage ---
[Saveable("position")]
public record struct Position(Vector2 Value);

[Saveable("health")]
public record struct Health(int Current, int Max);

// --- Auto-discovery ---
public static class SaveableDiscovery
{
    public static void RegisterAll(SaveableRegistry registry, params Assembly[] assemblies)
    {
        var serializerType = typeof(ComponentSerializer<>);

        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                var attr = type.GetCustomAttribute<SaveableAttribute>();
                if (attr == null || !type.IsValueType) continue;

                var genericSerializer = serializerType.MakeGenericType(type);
                var instance = (IComponentSerializer)Activator.CreateInstance(
                    genericSerializer, attr.TypeKey)!;
                registry.Register(instance);
            }
        }
    }
}

// --- At startup ---
// var registry = new SaveableRegistry(); // starts empty
// SaveableDiscovery.RegisterAll(registry, typeof(Game1).Assembly);
```

---

## 3 — ECS World Serialization

### Entity Snapshot

```csharp
/// <summary>
/// Flat snapshot of a single entity's components.
/// Keys are component type keys from the registry, values are serialized data.
/// </summary>
public class EntitySnapshot
{
    public string? EntityId { get; set; }
    public string? Prefab { get; set; }  // Optional: prefab/archetype name for reconstruction
    public Dictionary<string, JsonElement> Components { get; set; } = new();
}
```

### World Serializer

```csharp
/// <summary>
/// Serializes all persistent entities in an ECS World to snapshots.
/// Only entities with the Persistent tag are included.
/// </summary>
public class WorldSerializer
{
    private readonly SaveableRegistry _registry;

    public WorldSerializer(SaveableRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>Serialize all persistent entities to a list of snapshots.</summary>
    public List<EntitySnapshot> SerializeWorld(World world)
    {
        var snapshots = new List<EntitySnapshot>();
        var query = new QueryDescription().WithAll<Persistent>();

        world.Query(in query, (Entity entity) =>
        {
            var snapshot = SerializeEntity(world, entity);
            snapshots.Add(snapshot);
        });

        return snapshots;
    }

    /// <summary>Serialize a single entity.</summary>
    public EntitySnapshot SerializeEntity(World world, Entity entity)
    {
        var snapshot = new EntitySnapshot();

        // Capture stable ID if present
        if (world.Has<EntityId>(entity))
            snapshot.EntityId = world.Get<EntityId>(entity).Id;

        // Capture prefab name if present (helps reconstruction)
        if (world.Has<PrefabSource>(entity))
            snapshot.Prefab = world.Get<PrefabSource>(entity).Name;

        // Serialize each registered component the entity has
        foreach (var (key, serializer) in _registry.Serializers)
        {
            if (serializer.Has(world, entity))
                snapshot.Components[key] = serializer.Serialize(world, entity);
        }

        return snapshot;
    }

    /// <summary>Count how many persistent entities exist (for progress reporting).</summary>
    public int CountPersistent(World world)
    {
        int count = 0;
        var query = new QueryDescription().WithAll<Persistent>();
        world.Query(in query, (Entity _) => count++);
        return count;
    }
}
```

### World Restorer

```csharp
/// <summary>
/// Restores entities from snapshots into an ECS World.
/// Handles prefab-based reconstruction and component deserialization.
/// </summary>
public class WorldRestorer
{
    private readonly SaveableRegistry _registry;
    private readonly PrefabRegistry? _prefabRegistry;

    public WorldRestorer(SaveableRegistry registry, PrefabRegistry? prefabRegistry = null)
    {
        _registry = registry;
        _prefabRegistry = prefabRegistry;
    }

    /// <summary>
    /// Clear all persistent entities, then recreate from snapshots.
    /// Returns the count of restored entities.
    /// </summary>
    public int RestoreWorld(World world, List<EntitySnapshot> snapshots)
    {
        // Phase 1: Destroy existing persistent entities
        DestroyPersistent(world);

        // Phase 2: Create entities from snapshots
        foreach (var snapshot in snapshots)
        {
            RestoreEntity(world, snapshot);
        }

        // Phase 3: Resolve cross-entity references
        EntityReferenceResolver.ResolveAll(world);

        return snapshots.Count;
    }

    private void DestroyPersistent(World world)
    {
        var query = new QueryDescription().WithAll<Persistent>();
        var toDestroy = new List<Entity>();
        world.Query(in query, (Entity e) => toDestroy.Add(e));
        foreach (var e in toDestroy)
            world.Destroy(e);
    }

    private void RestoreEntity(World world, EntitySnapshot snapshot)
    {
        Entity entity;

        // If the snapshot has a prefab, use it as the base (gets visuals, physics, etc.)
        if (snapshot.Prefab != null && _prefabRegistry != null &&
            _prefabRegistry.TrySpawn(world, snapshot.Prefab, out entity))
        {
            // Prefab created the entity with default components — we'll overwrite from save data
        }
        else
        {
            entity = world.Create<Persistent>();
        }

        // Ensure Persistent tag
        if (!world.Has<Persistent>(entity))
            world.Set(entity, new Persistent());

        // Deserialize saved components (overwrites prefab defaults)
        foreach (var (key, data) in snapshot.Components)
        {
            if (_registry.Serializers.TryGetValue(key, out var serializer))
                serializer.Deserialize(world, entity, data);
            // Unknown keys are silently skipped — forward compatibility
        }
    }
}
```

### Persistent Tag and Exclusions

```csharp
/// <summary>
/// Tag: entity should be saved. Add to any entity that needs persistence.
/// </summary>
public struct Persistent;

/// <summary>
/// Tag: entity is transient and should NOT be saved, even if other logic
/// might add Persistent to it. Use for temporary VFX, pooled projectiles, etc.
/// </summary>
public struct Transient;

/// <summary>
/// Tag: entity was spawned from a prefab and only needs delta serialization
/// (components that differ from the prefab default).
/// </summary>
public record struct PrefabSource(string Name);
```

**Rule: Be explicit about what gets saved.**

- Players, NPCs, interactable objects, opened chests → `Persistent`
- Particles, pooled bullets, UI entities, camera → no tag (transient by default)
- Static level geometry → loaded from scene data, not save data

---

## 4 — Save File Format & Versioning

### Save File Structure

```csharp
/// <summary>
/// The complete save file. This is what gets serialized to disk.
/// </summary>
public class SaveFile
{
    /// <summary>Format version and metadata.</summary>
    public SaveHeader Header { get; set; } = new();

    /// <summary>ECS entity snapshots.</summary>
    public List<EntitySnapshot> Entities { get; set; } = new();

    /// <summary>Per-system auxiliary state (dialogue, quests, timers, etc.).</summary>
    public Dictionary<string, JsonElement> SystemState { get; set; } = new();

    /// <summary>Scene/level persistence data.</summary>
    public SceneSaveData? SceneData { get; set; }

    /// <summary>Player profile reference (links to external profile file).</summary>
    public string? ProfileId { get; set; }
}

public class SaveHeader
{
    /// <summary>Save format version. Increment when save structure changes.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Game version that created this save (e.g., "1.2.0").</summary>
    public string GameVersion { get; set; } = "";

    /// <summary>Human-readable save name.</summary>
    public string Name { get; set; } = "";

    /// <summary>UTC timestamp when save was created.</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>Total play time at the moment of save.</summary>
    public TimeSpan PlayTime { get; set; }

    /// <summary>Current scene/level ID.</summary>
    public string SceneId { get; set; } = "";

    /// <summary>CRC32 checksum of the serialized body (everything except this field).</summary>
    public uint Checksum { get; set; }

    /// <summary>Optional: base64-encoded thumbnail image (PNG, max ~50KB).</summary>
    public string? Thumbnail { get; set; }

    /// <summary>Compression method used on the body, if any.</summary>
    public string Compression { get; set; } = "none";  // "none", "gzip", "brotli"

    /// <summary>Whether the body is encrypted.</summary>
    public bool Encrypted { get; set; } = false;
}
```

### JSON Serialization Options

```csharp
public static class SaveJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,           // Production: compact. Set true for debugging.
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new JsonStringEnumConverter(),
            new Vector2Converter(),      // MonoGame types need custom converters
            new RectangleConverter(),
            new ColorConverter(),
            new TimeSpanConverter()
        }
    };

    public static string Serialize(SaveFile save) =>
        JsonSerializer.Serialize(save, Options);

    public static SaveFile Deserialize(string json) =>
        JsonSerializer.Deserialize<SaveFile>(json, Options)
        ?? throw new SaveCorruptException("Failed to deserialize save file");
}
```

### MonoGame Type Converters

MonoGame's `Vector2`, `Rectangle`, `Color`, etc. don't serialize cleanly with `System.Text.Json` out of the box:

```csharp
public class Vector2Converter : JsonConverter<Vector2>
{
    public override Vector2 Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions opts)
    {
        reader.Read(); // StartObject → first property
        reader.Read(); float x = reader.GetSingle();
        reader.Read(); reader.Read(); float y = reader.GetSingle();
        reader.Read(); // EndObject
        return new Vector2(x, y);
    }

    public override void Write(Utf8JsonWriter writer, Vector2 value, JsonSerializerOptions opts)
    {
        writer.WriteStartObject();
        writer.WriteNumber("x", MathF.Round(value.X, 4));  // 4 decimals = sub-pixel precision
        writer.WriteNumber("y", MathF.Round(value.Y, 4));
        writer.WriteEndObject();
    }
}

public class RectangleConverter : JsonConverter<Rectangle>
{
    public override Rectangle Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions opts)
    {
        reader.Read(); reader.Read(); int x = reader.GetInt32();
        reader.Read(); reader.Read(); int y = reader.GetInt32();
        reader.Read(); reader.Read(); int w = reader.GetInt32();
        reader.Read(); reader.Read(); int h = reader.GetInt32();
        reader.Read();
        return new Rectangle(x, y, w, h);
    }

    public override void Write(Utf8JsonWriter writer, Rectangle value, JsonSerializerOptions opts)
    {
        writer.WriteStartObject();
        writer.WriteNumber("x", value.X);
        writer.WriteNumber("y", value.Y);
        writer.WriteNumber("w", value.Width);
        writer.WriteNumber("h", value.Height);
        writer.WriteEndObject();
    }
}

public class ColorConverter : JsonConverter<Color>
{
    public override Color Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions opts)
    {
        string hex = reader.GetString() ?? "FFFFFFFF";
        uint packed = Convert.ToUInt32(hex, 16);
        return new Color(
            (byte)((packed >> 24) & 0xFF),
            (byte)((packed >> 16) & 0xFF),
            (byte)((packed >> 8) & 0xFF),
            (byte)(packed & 0xFF)
        );
    }

    public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions opts)
    {
        writer.WriteStringValue($"{value.R:X2}{value.G:X2}{value.B:X2}{value.A:X2}");
    }
}
```

### Why JSON (Not Binary)?

| Factor | JSON | Binary (MessagePack, FlatBuffers) |
|--------|------|-----------------------------------|
| Human-readable | ✅ Debug-friendly | ❌ Opaque |
| Modding | ✅ Easy to edit | ❌ Needs tools |
| Size | ~2-5× larger | Compact |
| Speed | Adequate for <10MB | Faster for large worlds |
| Versioning | ✅ Additive fields work | Fragile with schema changes |
| Cross-platform | ✅ Universal | Library-dependent |

**Recommendation:** Start with JSON. Switch to binary only if save files exceed 5MB or save/load takes >500ms. Most 2D games never hit that threshold. If you do need binary, MessagePack with `contractless` mode is the closest to JSON's flexibility.

---

## 5 — Version Migration Pipeline

Save format changes are inevitable. New components, renamed fields, restructured data — migrations handle it.

### Migration Interface

```csharp
/// <summary>
/// A single migration step that transforms a save from one version to the next.
/// </summary>
public interface ISaveMigration
{
    /// <summary>Source version this migration applies to.</summary>
    int FromVersion { get; }

    /// <summary>Target version after migration.</summary>
    int ToVersion { get; }

    /// <summary>Human-readable description of what changed.</summary>
    string Description { get; }

    /// <summary>Transform the save data in-place.</summary>
    void Migrate(SaveFile save);
}
```

### Migration Registry

```csharp
/// <summary>
/// Manages the chain of migrations from any historical version to current.
/// </summary>
public class MigrationPipeline
{
    public const int CurrentVersion = 5;  // Bump this when adding a migration

    private readonly SortedList<int, ISaveMigration> _migrations = new();

    public MigrationPipeline()
    {
        Register(new Migration_1_to_2());
        Register(new Migration_2_to_3());
        Register(new Migration_3_to_4());
        Register(new Migration_4_to_5());
    }

    public void Register(ISaveMigration migration)
    {
        _migrations[migration.FromVersion] = migration;
    }

    /// <summary>
    /// Apply all necessary migrations to bring a save file to the current version.
    /// Returns the number of migrations applied.
    /// </summary>
    public int MigrateToLatest(SaveFile save)
    {
        int applied = 0;
        while (save.Header.Version < CurrentVersion)
        {
            if (!_migrations.TryGetValue(save.Header.Version, out var migration))
                throw new SaveCorruptException(
                    $"No migration path from version {save.Header.Version}. " +
                    $"Save may be from an incompatible game version.");

            migration.Migrate(save);
            save.Header.Version = migration.ToVersion;
            applied++;
        }
        return applied;
    }

    /// <summary>
    /// Check if a save file version is loadable (migration path exists).
    /// </summary>
    public bool CanMigrate(int fromVersion)
    {
        int v = fromVersion;
        while (v < CurrentVersion)
        {
            if (!_migrations.ContainsKey(v)) return false;
            v = _migrations[v].ToVersion;
        }
        return true;
    }
}
```

### Example Migrations

```csharp
/// <summary>v1→v2: Added wallet component, renamed "hp" to "health".</summary>
public class Migration_1_to_2 : ISaveMigration
{
    public int FromVersion => 1;
    public int ToVersion => 2;
    public string Description => "Rename hp→health, add wallet with 0 gold";

    public void Migrate(SaveFile save)
    {
        foreach (var entity in save.Entities)
        {
            // Rename "hp" → "health"
            if (entity.Components.Remove("hp", out var hpData))
            {
                // Transform: old {current, max} → new {current, max, shield}
                var hp = hpData.Deserialize<Dictionary<string, int>>()!;
                var health = new { current = hp["current"], max = hp["max"], shield = 0 };
                entity.Components["health"] =
                    JsonSerializer.SerializeToElement(health, SaveJson.Options);
            }

            // Add default wallet if entity has inventory but no wallet
            if (entity.Components.ContainsKey("inventory") &&
                !entity.Components.ContainsKey("wallet"))
            {
                var wallet = new { gold = 0, currencies = new Dictionary<string, int>() };
                entity.Components["wallet"] =
                    JsonSerializer.SerializeToElement(wallet, SaveJson.Options);
            }
        }
    }
}

/// <summary>v2→v3: Moved quest data from entities to SystemState.</summary>
public class Migration_2_to_3 : ISaveMigration
{
    public int FromVersion => 2;
    public int ToVersion => 3;
    public string Description => "Extract quest data from entities to SystemState";

    public void Migrate(SaveFile save)
    {
        // Collect quest logs from all entities
        var quests = new Dictionary<string, JsonElement>();

        foreach (var entity in save.Entities)
        {
            if (entity.Components.Remove("questLog", out var questData))
            {
                string eid = entity.EntityId ?? Guid.NewGuid().ToString();
                quests[eid] = questData;
            }
        }

        // Store centrally in SystemState
        if (quests.Count > 0)
        {
            save.SystemState["questManager"] =
                JsonSerializer.SerializeToElement(quests, SaveJson.Options);
        }
    }
}

/// <summary>v3→v4: Split Position into Position + GridPosition for grid-based entities.</summary>
public class Migration_3_to_4 : ISaveMigration
{
    public int FromVersion => 3;
    public int ToVersion => 4;
    public string Description => "Add gridPosition for entities on tile grid";

    public void Migrate(SaveFile save)
    {
        foreach (var entity in save.Entities)
        {
            // Entities with a tilemap occupant flag get a grid position
            if (entity.Components.ContainsKey("tilemapOccupant") &&
                entity.Components.TryGetValue("position", out var posData))
            {
                var pos = posData.Deserialize<Dictionary<string, float>>()!;
                int gridX = (int)(pos["x"] / 16f);  // Assuming 16px tiles
                int gridY = (int)(pos["y"] / 16f);
                var gridPos = new { x = gridX, y = gridY };
                entity.Components["gridPosition"] =
                    JsonSerializer.SerializeToElement(gridPos, SaveJson.Options);
            }
        }
    }
}

/// <summary>v4→v5: Scene data restructuring.</summary>
public class Migration_4_to_5 : ISaveMigration
{
    public int FromVersion => 4;
    public int ToVersion => 5;
    public string Description => "Restructure scene persistence data";

    public void Migrate(SaveFile save)
    {
        // Old format: scene data was in SystemState["sceneData"]
        // New format: save.SceneData is a first-class field
        if (save.SystemState.Remove("sceneData", out var sceneData))
        {
            save.SceneData = sceneData.Deserialize<SceneSaveData>(SaveJson.Options);
        }
    }
}
```

### Migration Testing

Every migration should have a test with a fixture save file:

```csharp
[TestMethod]
public void Migration_1_to_2_RenamesHp()
{
    // Load a real v1 save fixture
    var json = File.ReadAllText("TestData/save_v1.json");
    var save = SaveJson.Deserialize(json);

    Assert.AreEqual(1, save.Header.Version);

    var pipeline = new MigrationPipeline();
    int applied = pipeline.MigrateToLatest(save);

    Assert.AreEqual(MigrationPipeline.CurrentVersion, save.Header.Version);
    Assert.IsTrue(applied >= 1);

    // Verify hp was renamed to health with shield field
    var player = save.Entities.First(e => e.EntityId == "player");
    Assert.IsTrue(player.Components.ContainsKey("health"));
    Assert.IsFalse(player.Components.ContainsKey("hp"));

    var health = player.Components["health"];
    Assert.IsTrue(health.TryGetProperty("shield", out _));
}
```

**Keep old save fixtures forever.** Store `TestData/save_v1.json`, `save_v2.json`, etc. These are your regression tests against save corruption.

---

## 6 — Save Slot Manager

### Slot-Based Architecture

```csharp
/// <summary>
/// Manages save slots with metadata caching for fast menu display.
/// Handles file I/O, backup creation, and slot enumeration.
/// </summary>
public class SaveSlotManager
{
    private readonly string _saveDir;
    private readonly int _maxSlots;
    private readonly Dictionary<int, SaveHeader?> _headerCache = new();

    public int MaxSlots => _maxSlots;
    public string SaveDirectory => _saveDir;

    public SaveSlotManager(string saveDir, int maxSlots = 10)
    {
        _saveDir = saveDir;
        _maxSlots = maxSlots;
        Directory.CreateDirectory(saveDir);
    }

    /// <summary>Get the file path for a given slot number.</summary>
    public string GetSlotPath(int slot) =>
        Path.Combine(_saveDir, $"save_{slot:D2}.json");

    public string GetBackupPath(int slot) =>
        Path.Combine(_saveDir, $"save_{slot:D2}.bak");

    /// <summary>Check if a slot has a save file.</summary>
    public bool SlotExists(int slot) =>
        File.Exists(GetSlotPath(slot));

    /// <summary>
    /// Read just the header of a save file (fast — doesn't parse entities).
    /// Used for save slot menu display.
    /// </summary>
    public SaveHeader? PeekHeader(int slot, bool useCache = true)
    {
        if (useCache && _headerCache.TryGetValue(slot, out var cached))
            return cached;

        if (!SlotExists(slot))
        {
            _headerCache[slot] = null;
            return null;
        }

        try
        {
            // Read only enough to extract the header
            var json = File.ReadAllText(GetSlotPath(slot));
            var save = SaveJson.Deserialize(json);
            _headerCache[slot] = save.Header;
            return save.Header;
        }
        catch
        {
            _headerCache[slot] = null;
            return null;
        }
    }

    /// <summary>Get metadata for all slots (for save/load menu).</summary>
    public List<SaveSlotInfo> GetAllSlots()
    {
        var slots = new List<SaveSlotInfo>();
        for (int i = 0; i < _maxSlots; i++)
        {
            var header = PeekHeader(i);
            slots.Add(new SaveSlotInfo
            {
                Slot = i,
                IsEmpty = header == null,
                Header = header,
                FileSizeBytes = SlotExists(i)
                    ? new FileInfo(GetSlotPath(i)).Length
                    : 0
            });
        }
        return slots;
    }

    /// <summary>Write a save file to a slot with atomic write + backup.</summary>
    public void WriteSlot(int slot, SaveFile save)
    {
        if (slot < 0 || slot >= _maxSlots)
            throw new ArgumentOutOfRangeException(nameof(slot));

        var path = GetSlotPath(slot);
        var backupPath = GetBackupPath(slot);
        var tempPath = path + ".tmp";

        // Create backup of existing save
        if (File.Exists(path))
        {
            File.Copy(path, backupPath, overwrite: true);
        }

        // Write to temp file first (crash-safe)
        var json = SaveJson.Serialize(save);
        File.WriteAllText(tempPath, json);

        // Atomic rename (on most filesystems)
        File.Move(tempPath, path, overwrite: true);

        // Update cache
        _headerCache[slot] = save.Header;
    }

    /// <summary>Load a save file from a slot.</summary>
    public SaveFile? ReadSlot(int slot)
    {
        if (!SlotExists(slot)) return null;

        try
        {
            var json = File.ReadAllText(GetSlotPath(slot));
            return SaveJson.Deserialize(json);
        }
        catch (Exception ex)
        {
            // Try backup
            var backupPath = GetBackupPath(slot);
            if (File.Exists(backupPath))
            {
                try
                {
                    var json = File.ReadAllText(backupPath);
                    return SaveJson.Deserialize(json);
                }
                catch
                {
                    throw new SaveCorruptException(
                        $"Save slot {slot} and its backup are both corrupt.", ex);
                }
            }
            throw new SaveCorruptException(
                $"Save slot {slot} is corrupt and no backup exists.", ex);
        }
    }

    /// <summary>Delete a save slot (moves to backup, doesn't hard delete).</summary>
    public void DeleteSlot(int slot)
    {
        var path = GetSlotPath(slot);
        if (File.Exists(path))
        {
            // Move to .deleted for recovery (not hard delete)
            var deletedPath = path + ".deleted";
            File.Move(path, deletedPath, overwrite: true);
        }
        _headerCache.Remove(slot);
    }

    /// <summary>Invalidate the header cache (call after external file changes).</summary>
    public void InvalidateCache() => _headerCache.Clear();
}

public class SaveSlotInfo
{
    public int Slot { get; set; }
    public bool IsEmpty { get; set; }
    public SaveHeader? Header { get; set; }
    public long FileSizeBytes { get; set; }
}
```

### Platform-Specific Save Directories

```csharp
public static class SavePaths
{
    /// <summary>
    /// Get the appropriate save directory for the current platform.
    /// </summary>
    public static string GetSaveDirectory(string gameName)
    {
        // Windows: %APPDATA%/GameName/saves
        // macOS:   ~/Library/Application Support/GameName/saves
        // Linux:   ~/.local/share/GameName/saves
        // Steam:   Use Steam Cloud paths if available

        string basePath;

        if (OperatingSystem.IsWindows())
            basePath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        else if (OperatingSystem.IsMacOS())
            basePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Personal),
                "Library", "Application Support");
        else // Linux
            basePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Personal),
                ".local", "share");

        return Path.Combine(basePath, gameName, "saves");
    }

    /// <summary>
    /// Get separate directories for different data types.
    /// </summary>
    public static (string Saves, string Settings, string Profile) GetAllPaths(string gameName)
    {
        var baseDir = Path.GetDirectoryName(GetSaveDirectory(gameName))!;
        return (
            Saves: Path.Combine(baseDir, "saves"),
            Settings: Path.Combine(baseDir, "settings"),
            Profile: Path.Combine(baseDir, "profile")
        );
    }
}
```

---

## 7 — Autosave System

### Trigger-Based Autosave

Autosave at regular intervals is lazy. Good autosave triggers on *meaningful events*:

```csharp
/// <summary>
/// Autosave system that saves on meaningful game events, not just timers.
/// Uses a dedicated slot (slot 0 by default) separate from manual saves.
/// </summary>
public class AutosaveSystem
{
    private readonly SaveManager _saveManager;
    private readonly int _autosaveSlot;
    private readonly float _minInterval;  // Minimum seconds between autosaves
    private readonly int _rotationSlots;  // Number of rotating autosave slots

    private float _timeSinceLastSave;
    private int _currentRotation;
    private bool _pendingSave;

    public bool Enabled { get; set; } = true;
    public bool IsSaving { get; private set; }

    public AutosaveSystem(SaveManager saveManager, int autosaveSlot = 0,
        float minInterval = 60f, int rotationSlots = 3)
    {
        _saveManager = saveManager;
        _autosaveSlot = autosaveSlot;
        _minInterval = minInterval;
        _rotationSlots = rotationSlots;
    }

    /// <summary>
    /// Call this when a significant event occurs.
    /// The actual save happens in Update() to respect the minimum interval.
    /// </summary>
    public void RequestAutosave(string reason = "")
    {
        if (!Enabled) return;
        _pendingSave = true;
    }

    /// <summary>Call each frame. Handles pending saves with cooldown.</summary>
    public void Update(float deltaTime)
    {
        _timeSinceLastSave += deltaTime;

        if (_pendingSave && _timeSinceLastSave >= _minInterval)
        {
            _pendingSave = false;
            _timeSinceLastSave = 0;
            PerformAutosave();
        }
    }

    private void PerformAutosave()
    {
        IsSaving = true;

        // Rotate through autosave slots: autosave_0, autosave_1, autosave_2
        int slot = _autosaveSlot + (_currentRotation % _rotationSlots);
        _currentRotation++;

        _saveManager.SaveToSlot(slot, $"Autosave {_currentRotation}");

        IsSaving = false;
    }
}

/// <summary>
/// Events that should trigger autosave. Register these in your game systems.
/// </summary>
public static class AutosaveTriggers
{
    /// <summary>Meaningful events that warrant an autosave.</summary>
    public enum Trigger
    {
        SceneTransition,       // Entering a new area/level
        BossFightStart,        // Before a boss encounter
        QuestCompleted,        // Major progress milestone
        ShopClosed,            // After purchasing (prevent loss of purchases)
        CraftingCompleted,     // After crafting (prevent loss of materials)
        CheckpointReached,     // Explicit checkpoint in the world
        LongTimePassed,        // Fallback: 5+ min since last save
        InventoryChanged,      // Major inventory change (rare item acquired)
    }

    /// <summary>
    /// Decision matrix: should this trigger actually save?
    /// Prevents saving during combat, cutscenes, or other bad moments.
    /// </summary>
    public static bool ShouldAutosave(Trigger trigger, GameState state)
    {
        // Never save during these states
        if (state.IsInCombat && trigger != Trigger.BossFightStart)
            return false;
        if (state.IsInCutscene)
            return false;
        if (state.IsInDialogue)
            return false;
        if (state.IsPaused)
            return false;

        return true;
    }
}
```

### Autosave UI Indicator

```csharp
/// <summary>
/// Shows an autosave icon when saving is in progress.
/// Standard UX: spinning icon in corner, fades after save completes.
/// </summary>
public class AutosaveIndicator
{
    private readonly Texture2D _icon;
    private float _displayTimer;
    private float _rotation;
    private const float DisplayDuration = 2f;   // Show for 2s after save
    private const float SpinSpeed = 3f;          // Radians per second
    private const float FadeStart = 1.5f;        // Start fading at 1.5s

    public AutosaveIndicator(Texture2D saveIcon)
    {
        _icon = saveIcon;
    }

    public void OnSaveStarted()
    {
        _displayTimer = DisplayDuration;
    }

    public void Update(float dt)
    {
        if (_displayTimer > 0)
        {
            _displayTimer -= dt;
            _rotation += SpinSpeed * dt;
        }
    }

    public void Draw(SpriteBatch batch, Vector2 screenSize)
    {
        if (_displayTimer <= 0) return;

        float alpha = _displayTimer < (DisplayDuration - FadeStart)
            ? _displayTimer / (DisplayDuration - FadeStart)
            : 1f;

        var position = new Vector2(screenSize.X - 48, screenSize.Y - 48);
        var origin = new Vector2(_icon.Width / 2f, _icon.Height / 2f);

        batch.Draw(_icon, position, null, Color.White * alpha,
            _rotation, origin, 1f, SpriteEffects.None, 0f);
    }
}
```

### Rotating Autosave Slots

Why rotate? If the game crashes *during* an autosave, the file is corrupt. With rotation, the previous autosave is still intact:

```
autosave_0.json  ← Newest (possibly corrupt if crash happened during write)
autosave_1.json  ← Previous (safe)
autosave_2.json  ← Oldest (safe)
```

Three slots is the sweet spot — enough for safety without wasting disk space.

---

## 8 — Quicksave / Quickload

### Implementation

```csharp
/// <summary>
/// Quicksave uses a dedicated slot outside the normal save range.
/// F5 = quicksave, F9 = quickload (standard convention).
/// </summary>
public class QuicksaveManager
{
    private readonly SaveManager _saveManager;
    private readonly int _quicksaveSlot;
    private readonly int _maxQuicksaves;  // Rotating stack

    private int _quicksaveCount;
    private float _cooldown;
    private const float CooldownSeconds = 1f;  // Prevent accidental double-saves

    public QuicksaveManager(SaveManager saveManager, int quicksaveSlot = 90,
        int maxQuicksaves = 5)
    {
        _saveManager = saveManager;
        _quicksaveSlot = quicksaveSlot;
        _maxQuicksaves = maxQuicksaves;
    }

    public void Update(float dt)
    {
        if (_cooldown > 0) _cooldown -= dt;
    }

    /// <summary>F5: Save to the next rotating quicksave slot.</summary>
    public bool Quicksave()
    {
        if (_cooldown > 0) return false;

        int slot = _quicksaveSlot + (_quicksaveCount % _maxQuicksaves);
        _quicksaveCount++;
        _cooldown = CooldownSeconds;

        _saveManager.SaveToSlot(slot, $"Quicksave {_quicksaveCount}");
        return true;
    }

    /// <summary>F9: Load the most recent quicksave.</summary>
    public bool Quickload()
    {
        // Find the most recent quicksave
        SaveHeader? newest = null;
        int newestSlot = -1;

        for (int i = 0; i < _maxQuicksaves; i++)
        {
            int slot = _quicksaveSlot + i;
            var header = _saveManager.SlotManager.PeekHeader(slot);
            if (header != null && (newest == null || header.Timestamp > newest.Timestamp))
            {
                newest = header;
                newestSlot = slot;
            }
        }

        if (newestSlot < 0) return false;

        _saveManager.LoadFromSlot(newestSlot);
        return true;
    }

    /// <summary>Get list of quicksaves for a load menu.</summary>
    public List<SaveSlotInfo> GetQuicksaves()
    {
        var saves = new List<SaveSlotInfo>();
        for (int i = 0; i < _maxQuicksaves; i++)
        {
            int slot = _quicksaveSlot + i;
            var header = _saveManager.SlotManager.PeekHeader(slot);
            if (header != null)
            {
                saves.Add(new SaveSlotInfo
                {
                    Slot = slot,
                    IsEmpty = false,
                    Header = header
                });
            }
        }
        return saves.OrderByDescending(s => s.Header!.Timestamp).ToList();
    }
}
```

### Quicksave During Combat

Many games disable quicksave during combat. Others allow it but with consequences:

```csharp
public enum QuicksavePolicy
{
    Always,           // Quicksave anywhere, anytime (sandbox/builder games)
    OutOfCombat,      // Disable during combat (most action games)
    SafeZonesOnly,    // Only at save points/safe rooms (horror, soulslike)
    Checkpoints,      // No quicksave — only checkpoint saves (roguelike-adjacent)
}

public bool CanQuicksave(QuicksavePolicy policy, GameState state)
{
    return policy switch
    {
        QuicksavePolicy.Always => true,
        QuicksavePolicy.OutOfCombat => !state.IsInCombat,
        QuicksavePolicy.SafeZonesOnly => state.IsInSafeZone,
        QuicksavePolicy.Checkpoints => false,
        _ => false
    };
}
```

---

## 9 — Async Save/Load

### Why Async?

Serializing 500 entities with 5 components each to JSON takes ~20-80ms. That's 1-5 dropped frames at 60fps. Players notice.

The solution: serialize on a background thread, with careful synchronization.

### Save Pipeline (Background Thread)

```csharp
/// <summary>
/// Handles save/load on background threads to prevent frame drops.
/// The ECS snapshot is taken on the main thread (must be synchronous),
/// but serialization and I/O happen on a background thread.
/// </summary>
public class AsyncSaveManager
{
    private readonly WorldSerializer _worldSerializer;
    private readonly SaveSlotManager _slotManager;
    private readonly MigrationPipeline _migrations;
    private readonly SaveableRegistry _registry;

    private Task? _activeSaveTask;
    private Task<SaveFile?>? _activeLoadTask;

    public bool IsSaving => _activeSaveTask is { IsCompleted: false };
    public bool IsLoading => _activeLoadTask is { IsCompleted: false };

    public event Action? OnSaveStarted;
    public event Action? OnSaveCompleted;
    public event Action<Exception>? OnSaveFailed;
    public event Action<SaveFile>? OnLoadCompleted;
    public event Action<Exception>? OnLoadFailed;

    public AsyncSaveManager(WorldSerializer worldSerializer, SaveSlotManager slotManager,
        MigrationPipeline migrations, SaveableRegistry registry)
    {
        _worldSerializer = worldSerializer;
        _slotManager = slotManager;
        _migrations = migrations;
        _registry = registry;
    }

    /// <summary>
    /// Save the current game state to a slot asynchronously.
    /// Phase 1 (snapshot) runs on main thread.
    /// Phase 2 (serialize + write) runs on background thread.
    /// </summary>
    public void SaveAsync(World world, int slot, string saveName,
        string sceneId, TimeSpan playTime,
        Dictionary<string, JsonElement>? systemState = null)
    {
        if (IsSaving) return;  // Don't overlap saves

        OnSaveStarted?.Invoke();

        // Phase 1: Snapshot on main thread (must be synchronous — ECS is not thread-safe)
        var entities = _worldSerializer.SerializeWorld(world);
        var save = new SaveFile
        {
            Header = new SaveHeader
            {
                Version = MigrationPipeline.CurrentVersion,
                GameVersion = GameVersion.Current,
                Name = saveName,
                Timestamp = DateTime.UtcNow,
                PlayTime = playTime,
                SceneId = sceneId
            },
            Entities = entities,
            SystemState = systemState ?? new()
        };

        // Phase 2: Serialize + write on background thread
        _activeSaveTask = Task.Run(() =>
        {
            try
            {
                // Compute checksum before writing
                var bodyJson = JsonSerializer.Serialize(new
                {
                    save.Entities, save.SystemState, save.SceneData
                }, SaveJson.Options);
                save.Header.Checksum = Crc32.Compute(bodyJson);

                _slotManager.WriteSlot(slot, save);
                OnSaveCompleted?.Invoke();
            }
            catch (Exception ex)
            {
                OnSaveFailed?.Invoke(ex);
            }
        });
    }

    /// <summary>
    /// Load a save file asynchronously.
    /// Phase 1 (read + parse + migrate) runs on background thread.
    /// Phase 2 (restore world) must be called on main thread after completion.
    /// </summary>
    public void LoadAsync(int slot)
    {
        if (IsLoading) return;

        _activeLoadTask = Task.Run<SaveFile?>(() =>
        {
            try
            {
                var save = _slotManager.ReadSlot(slot);
                if (save == null) return null;

                // Validate checksum
                var bodyJson = JsonSerializer.Serialize(new
                {
                    save.Entities, save.SystemState, save.SceneData
                }, SaveJson.Options);
                var computed = Crc32.Compute(bodyJson);
                if (computed != save.Header.Checksum)
                    throw new SaveCorruptException(
                        $"Checksum mismatch (expected {save.Header.Checksum}, got {computed})");

                // Migrate if needed
                _migrations.MigrateToLatest(save);

                return save;
            }
            catch (Exception ex)
            {
                OnLoadFailed?.Invoke(ex);
                return null;
            }
        });
    }

    /// <summary>
    /// Call each frame. When load completes, restores the world on the main thread.
    /// </summary>
    public void Update(World world)
    {
        if (_activeLoadTask is { IsCompleted: true })
        {
            var task = _activeLoadTask;
            _activeLoadTask = null;

            if (task.Result != null)
            {
                // Restore on main thread (ECS operations)
                var restorer = new WorldRestorer(_registry);
                restorer.RestoreWorld(world, task.Result.Entities);
                OnLoadCompleted?.Invoke(task.Result);
            }
        }
    }
}
```

### Thread Safety Rules

```
╔═══════════════════════════════════════════════════╗
║  MAIN THREAD ONLY           BACKGROUND SAFE      ║
║  ─────────────────          ──────────────        ║
║  World queries               JSON serialize       ║
║  Entity create/destroy       JSON deserialize     ║
║  Component get/set           File read/write      ║
║  ECS system updates          Compression          ║
║  Post-load fixup             Encryption           ║
║                              Checksum compute     ║
║                              Migration transforms ║
╚═══════════════════════════════════════════════════╝
```

**The golden rule:** Read from ECS on the main thread, do everything else off it.

---

## 10 — Save Thumbnails & Screenshots

### Capturing the Thumbnail

```csharp
/// <summary>
/// Captures a small thumbnail of the current frame for save file display.
/// Uses MonoGame's RenderTarget2D to grab and downscale.
/// </summary>
public class SaveThumbnailCapture
{
    private readonly GraphicsDevice _graphics;
    private RenderTarget2D _captureTarget;
    private byte[]? _lastCapture;

    private const int ThumbWidth = 320;
    private const int ThumbHeight = 180;
    private const int MaxBase64Size = 50_000;  // ~50KB limit for inline thumbnail

    public SaveThumbnailCapture(GraphicsDevice graphics)
    {
        _graphics = graphics;
        _captureTarget = new RenderTarget2D(graphics, ThumbWidth, ThumbHeight);
    }

    /// <summary>
    /// Capture the current backbuffer as a thumbnail.
    /// Call this BEFORE SpriteBatch.End() in your main Draw().
    /// </summary>
    public void Capture(SpriteBatch spriteBatch, RenderTarget2D gameTarget)
    {
        // Downscale the game render to thumbnail size
        _graphics.SetRenderTarget(_captureTarget);
        _graphics.Clear(Color.Black);

        spriteBatch.Begin(samplerState: SamplerState.LinearClamp);
        spriteBatch.Draw(gameTarget, new Rectangle(0, 0, ThumbWidth, ThumbHeight), Color.White);
        spriteBatch.End();

        _graphics.SetRenderTarget(null);

        // Extract pixel data
        var data = new Color[ThumbWidth * ThumbHeight];
        _captureTarget.GetData(data);

        // Encode to PNG bytes
        using var ms = new MemoryStream();
        _captureTarget.SaveAsPng(ms, ThumbWidth, ThumbHeight);
        _lastCapture = ms.ToArray();
    }

    /// <summary>Get the last captured thumbnail as base64 PNG.</summary>
    public string? GetBase64Thumbnail()
    {
        if (_lastCapture == null) return null;

        string base64 = Convert.ToBase64String(_lastCapture);
        if (base64.Length > MaxBase64Size)
        {
            // Too large — try JPEG-style quality reduction
            // MonoGame doesn't have native JPEG, so we skip the thumbnail
            return null;
        }
        return base64;
    }
}
```

### Displaying Thumbnails in Save Menu

```csharp
/// <summary>
/// Renders a save slot in the save/load menu with thumbnail preview.
/// </summary>
public class SaveSlotRenderer
{
    private readonly Dictionary<int, Texture2D> _thumbnailCache = new();
    private readonly GraphicsDevice _graphics;

    public SaveSlotRenderer(GraphicsDevice graphics)
    {
        _graphics = graphics;
    }

    /// <summary>Decode a base64 thumbnail into a Texture2D (cached).</summary>
    public Texture2D? GetThumbnail(int slot, string? base64)
    {
        if (base64 == null) return null;

        if (_thumbnailCache.TryGetValue(slot, out var cached))
            return cached;

        try
        {
            byte[] data = Convert.FromBase64String(base64);
            using var ms = new MemoryStream(data);
            var texture = Texture2D.FromStream(_graphics, ms);
            _thumbnailCache[slot] = texture;
            return texture;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Draw a single save slot entry.</summary>
    public void DrawSlot(SpriteBatch batch, SpriteFont font, SaveSlotInfo info,
        Rectangle bounds, bool isSelected)
    {
        // Background
        var bgColor = isSelected ? new Color(60, 80, 120) : new Color(40, 40, 50);
        batch.Draw(Pixel, bounds, bgColor);

        if (info.IsEmpty)
        {
            batch.DrawString(font, "— Empty Slot —",
                new Vector2(bounds.X + 160, bounds.Center.Y - 8), Color.Gray);
            return;
        }

        // Thumbnail
        var thumb = GetThumbnail(info.Slot, info.Header?.Thumbnail);
        if (thumb != null)
        {
            var thumbRect = new Rectangle(bounds.X + 8, bounds.Y + 8, 128, 72);
            batch.Draw(thumb, thumbRect, Color.White);
        }

        // Text info
        int textX = bounds.X + 152;
        var header = info.Header!;

        batch.DrawString(font, header.Name,
            new Vector2(textX, bounds.Y + 8), Color.White);

        batch.DrawString(font, header.SceneId,
            new Vector2(textX, bounds.Y + 28), Color.LightGray);

        string timeStr = header.PlayTime.Hours > 0
            ? $"{header.PlayTime:h\\:mm\\:ss}"
            : $"{header.PlayTime:m\\:ss}";
        batch.DrawString(font, $"Play Time: {timeStr}",
            new Vector2(textX, bounds.Y + 48), Color.LightGray);

        batch.DrawString(font, header.Timestamp.ToLocalTime().ToString("MMM dd, yyyy  h:mm tt"),
            new Vector2(textX, bounds.Y + 68), Color.DarkGray);

        // File size
        string sizeStr = info.FileSizeBytes > 1_000_000
            ? $"{info.FileSizeBytes / 1_000_000.0:F1} MB"
            : $"{info.FileSizeBytes / 1_000.0:F0} KB";
        batch.DrawString(font, sizeStr,
            new Vector2(bounds.Right - 80, bounds.Y + 8), Color.DarkGray);
    }

    // Helper: 1x1 white pixel for drawing rectangles
    private static Texture2D? _pixel;
    private Texture2D Pixel
    {
        get
        {
            if (_pixel == null)
            {
                _pixel = new Texture2D(_graphics, 1, 1);
                _pixel.SetData(new[] { Color.White });
            }
            return _pixel;
        }
    }
}
```

---

## 11 — Save Integrity & Validation

### CRC32 Checksum

```csharp
/// <summary>
/// CRC32 implementation for save file integrity checking.
/// Detects accidental corruption (bit flips, truncated writes).
/// NOT a security measure — use encryption for tamper resistance.
/// </summary>
public static class Crc32
{
    private static readonly uint[] Table = GenerateTable();

    private static uint[] GenerateTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint crc = i;
            for (int j = 0; j < 8; j++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            table[i] = crc;
        }
        return table;
    }

    public static uint Compute(string text)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text);
        return Compute(bytes);
    }

    public static uint Compute(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
            crc = (crc >> 8) ^ Table[(crc ^ b) & 0xFF];
        return crc ^ 0xFFFFFFFF;
    }
}
```

### Full Validation Pipeline

```csharp
/// <summary>
/// Validates save files before loading. Catches corruption, version issues,
/// and incompatible saves early with clear error messages.
/// </summary>
public class SaveValidator
{
    private readonly MigrationPipeline _migrations;

    public SaveValidator(MigrationPipeline migrations)
    {
        _migrations = migrations;
    }

    public SaveValidationResult Validate(string json)
    {
        var result = new SaveValidationResult();

        // Step 1: Parse JSON
        SaveFile save;
        try
        {
            save = SaveJson.Deserialize(json);
        }
        catch (JsonException ex)
        {
            result.Errors.Add($"JSON parse error: {ex.Message}");
            return result;
        }

        result.Save = save;

        // Step 2: Header checks
        if (save.Header == null)
        {
            result.Errors.Add("Missing save header");
            return result;
        }

        if (save.Header.Version <= 0)
            result.Errors.Add($"Invalid version: {save.Header.Version}");

        if (save.Header.Version > MigrationPipeline.CurrentVersion)
            result.Errors.Add(
                $"Save is from a newer game version (save v{save.Header.Version}, " +
                $"game v{MigrationPipeline.CurrentVersion}). Update the game to load this save.");

        // Step 3: Migration path check
        if (save.Header.Version < MigrationPipeline.CurrentVersion)
        {
            if (!_migrations.CanMigrate(save.Header.Version))
                result.Errors.Add(
                    $"No migration path from v{save.Header.Version}. " +
                    $"This save may be from an incompatible game version.");
            else
                result.Warnings.Add(
                    $"Save will be migrated from v{save.Header.Version} " +
                    $"to v{MigrationPipeline.CurrentVersion}");
        }

        // Step 4: Checksum verification
        if (save.Header.Checksum != 0)
        {
            var bodyJson = JsonSerializer.Serialize(new
            {
                save.Entities, save.SystemState, save.SceneData
            }, SaveJson.Options);
            var computed = Crc32.Compute(bodyJson);
            if (computed != save.Header.Checksum)
                result.Errors.Add(
                    $"Checksum mismatch — file may be corrupt " +
                    $"(expected {save.Header.Checksum:X8}, computed {computed:X8})");
        }

        // Step 5: Entity sanity checks
        if (save.Entities.Count == 0)
            result.Warnings.Add("Save contains no entities");

        if (save.Entities.Count > 100_000)
            result.Warnings.Add($"Save contains {save.Entities.Count} entities — load may be slow");

        // Check for duplicate entity IDs
        var ids = save.Entities
            .Where(e => e.EntityId != null)
            .GroupBy(e => e.EntityId)
            .Where(g => g.Count() > 1)
            .ToList();
        foreach (var dup in ids)
            result.Warnings.Add($"Duplicate entity ID: {dup.Key} (×{dup.Count()})");

        // Step 6: Scene reference check
        if (string.IsNullOrEmpty(save.Header.SceneId))
            result.Warnings.Add("No scene ID in save — may load into wrong scene");

        result.IsValid = result.Errors.Count == 0;
        return result;
    }
}

public class SaveValidationResult
{
    public bool IsValid { get; set; }
    public SaveFile? Save { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}
```

---

## 12 — Encryption & Anti-Tamper

### When to Encrypt

| Game Type | Encrypt? | Why |
|-----------|----------|-----|
| Singleplayer sandbox | ❌ No | Let players mod saves |
| Singleplayer competitive (speedrun) | 🟡 Optional | Leaderboard integrity |
| Multiplayer with local saves | ✅ Yes | Prevent cheating |
| Games with microtransactions | ✅ Yes | Prevent currency manipulation |
| Games with achievements | 🟡 Optional | Achievement integrity |

### AES-256 Encryption

```csharp
/// <summary>
/// Encrypts and decrypts save file content using AES-256-CBC.
/// The key is derived from a machine-specific seed + game-specific salt.
/// </summary>
public static class SaveEncryption
{
    private const int KeySize = 256;
    private const int BlockSize = 128;
    private const int SaltSize = 16;
    private const int Iterations = 100_000;

    /// <summary>
    /// Derive a machine-unique encryption key.
    /// Uses machine name + game salt so saves can't be transferred between machines
    /// (if that's desired — remove machineId for transferable saves).
    /// </summary>
    private static byte[] DeriveKey(string gameSalt, bool machineSpecific = false)
    {
        string seed = gameSalt;
        if (machineSpecific)
            seed += Environment.MachineName;

        using var rfc = new Rfc2898DeriveBytes(
            System.Text.Encoding.UTF8.GetBytes(seed),
            System.Text.Encoding.UTF8.GetBytes("SaveFileSalt_v1"),
            Iterations,
            HashAlgorithmName.SHA256);

        return rfc.GetBytes(KeySize / 8);
    }

    /// <summary>Encrypt a JSON string to base64.</summary>
    public static string Encrypt(string plainJson, string gameSalt)
    {
        var key = DeriveKey(gameSalt);

        using var aes = Aes.Create();
        aes.KeySize = KeySize;
        aes.BlockSize = BlockSize;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;
        aes.GenerateIV();  // Random IV each time

        using var ms = new MemoryStream();
        // Write IV first (needed for decryption)
        ms.Write(aes.IV, 0, aes.IV.Length);

        using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
        using (var sw = new StreamWriter(cs))
        {
            sw.Write(plainJson);
        }

        return Convert.ToBase64String(ms.ToArray());
    }

    /// <summary>Decrypt a base64 string back to JSON.</summary>
    public static string Decrypt(string encryptedBase64, string gameSalt)
    {
        var key = DeriveKey(gameSalt);
        var encryptedBytes = Convert.FromBase64String(encryptedBase64);

        using var aes = Aes.Create();
        aes.KeySize = KeySize;
        aes.BlockSize = BlockSize;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;

        // Extract IV from start of data
        var iv = new byte[BlockSize / 8];
        Array.Copy(encryptedBytes, 0, iv, 0, iv.Length);
        aes.IV = iv;

        using var ms = new MemoryStream(encryptedBytes, iv.Length,
            encryptedBytes.Length - iv.Length);
        using var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
        using var sr = new StreamReader(cs);

        return sr.ReadToEnd();
    }
}
```

### Anti-Tamper Layers

For competitive games, combine multiple layers:

```csharp
/// <summary>
/// Multi-layer save protection for competitive/achievement games.
/// Layers: CRC32 (corruption) → HMAC (tamper) → AES (privacy).
/// </summary>
public class ProtectedSaveManager
{
    private readonly string _gameSalt;
    private readonly string _hmacKey;

    public ProtectedSaveManager(string gameSalt, string hmacKey)
    {
        _gameSalt = gameSalt;
        _hmacKey = hmacKey;
    }

    public string ProtectAndSerialize(SaveFile save)
    {
        // Layer 1: CRC32 for corruption detection
        var json = SaveJson.Serialize(save);
        save.Header.Checksum = Crc32.Compute(json);
        json = SaveJson.Serialize(save);  // Re-serialize with checksum

        // Layer 2: HMAC for tamper detection
        string hmac = ComputeHmac(json);

        // Layer 3: Encrypt everything
        string encrypted = SaveEncryption.Encrypt(json, _gameSalt);

        // Wrap in envelope
        var envelope = new SaveEnvelope
        {
            Data = encrypted,
            Hmac = hmac,
            Format = "aes256-cbc"
        };

        return JsonSerializer.Serialize(envelope);
    }

    public SaveFile DeserializeAndVerify(string envelope)
    {
        var env = JsonSerializer.Deserialize<SaveEnvelope>(envelope)!;

        // Layer 3: Decrypt
        string json = SaveEncryption.Decrypt(env.Data, _gameSalt);

        // Layer 2: Verify HMAC
        string expectedHmac = ComputeHmac(json);
        if (expectedHmac != env.Hmac)
            throw new SaveTamperedException("Save file has been modified externally.");

        // Layer 1: CRC verified during normal load
        return SaveJson.Deserialize(json);
    }

    private string ComputeHmac(string data)
    {
        using var hmac = new HMACSHA256(
            System.Text.Encoding.UTF8.GetBytes(_hmacKey));
        var hash = hmac.ComputeHash(
            System.Text.Encoding.UTF8.GetBytes(data));
        return Convert.ToBase64String(hash);
    }
}

public class SaveEnvelope
{
    public string Data { get; set; } = "";
    public string Hmac { get; set; } = "";
    public string Format { get; set; } = "";
}

public class SaveTamperedException : Exception
{
    public SaveTamperedException(string message) : base(message) { }
}
```

**Reality check:** Determined players WILL crack client-side encryption. These layers raise the bar from "edit JSON in Notepad" to "reverse-engineer the key derivation," which stops 99% of casual tampering. For true anti-cheat, save state must be server-authoritative.

---

## 13 — Save Data Compression

### GZip Compression

```csharp
/// <summary>
/// Compresses and decompresses save data using GZip.
/// Typical compression ratio for JSON save files: 5:1 to 10:1.
/// </summary>
public static class SaveCompression
{
    /// <summary>Compress a string to a GZip byte array.</summary>
    public static byte[] Compress(string json)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
        {
            gzip.Write(bytes, 0, bytes.Length);
        }
        return output.ToArray();
    }

    /// <summary>Decompress a GZip byte array to a string.</summary>
    public static string Decompress(byte[] compressed)
    {
        using var input = new MemoryStream(compressed);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        return reader.ReadToEnd();
    }

    /// <summary>Compress with Brotli (better ratio, slower — good for cloud saves).</summary>
    public static byte[] CompressBrotli(string json)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.Optimal))
        {
            brotli.Write(bytes, 0, bytes.Length);
        }
        return output.ToArray();
    }

    public static string DecompressBrotli(byte[] compressed)
    {
        using var input = new MemoryStream(compressed);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(brotli);
        return reader.ReadToEnd();
    }
}
```

### When to Compress

| Save Size (JSON) | Compressed | Compress? | Why |
|-------------------|-----------|-----------|-----|
| < 100KB | ~15KB | ❌ No | Not worth the code complexity |
| 100KB – 1MB | 20-150KB | 🟡 Optional | Nice for cloud saves |
| 1MB – 10MB | 150KB-1.5MB | ✅ Yes | Reduces I/O time noticeably |
| > 10MB | 1-2MB | ✅ Yes | Essential for large open worlds |

**Tip:** Compression actually *speeds up* save/load for large files because disk I/O is slower than CPU decompression. Writing 200KB of compressed data is faster than writing 2MB of raw JSON.

### Integration with Save Pipeline

```csharp
/// <summary>
/// Full save pipeline: serialize → compress → encrypt → write
/// Full load pipeline: read → decrypt → decompress → deserialize → migrate → validate
/// </summary>
public class SavePipeline
{
    private readonly SaveSlotManager _slotManager;
    private readonly MigrationPipeline _migrations;
    private readonly SaveValidator _validator;
    private readonly string? _encryptionSalt;
    private readonly bool _compress;

    public SavePipeline(SaveSlotManager slotManager, MigrationPipeline migrations,
        SaveValidator validator, bool compress = false, string? encryptionSalt = null)
    {
        _slotManager = slotManager;
        _migrations = migrations;
        _validator = validator;
        _compress = compress;
        _encryptionSalt = encryptionSalt;
    }

    public void WriteSave(int slot, SaveFile save)
    {
        var json = SaveJson.Serialize(save);

        if (_compress)
        {
            save.Header.Compression = "gzip";
            var compressed = SaveCompression.Compress(json);
            var encoded = Convert.ToBase64String(compressed);

            // Re-wrap with header (header is always uncompressed for peek)
            var wrapper = new CompressedSaveWrapper
            {
                Header = save.Header,
                CompressedBody = encoded
            };
            json = JsonSerializer.Serialize(wrapper, SaveJson.Options);
        }

        if (_encryptionSalt != null)
        {
            save.Header.Encrypted = true;
            json = SaveEncryption.Encrypt(json, _encryptionSalt);
        }

        File.WriteAllText(_slotManager.GetSlotPath(slot), json);
    }

    public SaveFile? ReadSave(int slot)
    {
        var path = _slotManager.GetSlotPath(slot);
        if (!File.Exists(path)) return null;

        var raw = File.ReadAllText(path);

        // Decrypt if needed
        if (_encryptionSalt != null)
        {
            try { raw = SaveEncryption.Decrypt(raw, _encryptionSalt); }
            catch { throw new SaveCorruptException("Failed to decrypt save file"); }
        }

        // Decompress if needed
        string json;
        try
        {
            var wrapper = JsonSerializer.Deserialize<CompressedSaveWrapper>(raw, SaveJson.Options);
            if (wrapper?.CompressedBody != null && wrapper.Header?.Compression == "gzip")
            {
                var compressed = Convert.FromBase64String(wrapper.CompressedBody);
                json = SaveCompression.Decompress(compressed);
            }
            else
            {
                json = raw;
            }
        }
        catch
        {
            json = raw;  // Not compressed — parse directly
        }

        // Validate
        var validation = _validator.Validate(json);
        if (!validation.IsValid)
            throw new SaveCorruptException(
                $"Save validation failed: {string.Join(", ", validation.Errors)}");

        // Migrate
        var save = validation.Save!;
        _migrations.MigrateToLatest(save);

        return save;
    }
}

public class CompressedSaveWrapper
{
    public SaveHeader? Header { get; set; }
    public string? CompressedBody { get; set; }
}
```

---

## 14 — Scene & Level State Persistence

### The Scene Problem

When a player leaves a room, opens a chest, kills an enemy, and returns later — the changes must persist. This is separate from "saving the game" — it's runtime scene state management.

### Scene State Data

```csharp
/// <summary>
/// Tracks persistent changes to scenes/levels.
/// Stored both in memory (runtime) and in save files.
/// </summary>
public class SceneSaveData
{
    /// <summary>Per-scene persistent state.</summary>
    public Dictionary<string, SceneState> Scenes { get; set; } = new();
}

public class SceneState
{
    /// <summary>Scene ID (e.g., "dungeon_floor_1", "town_square").</summary>
    public string SceneId { get; set; } = "";

    /// <summary>Has the player visited this scene?</summary>
    public bool Visited { get; set; }

    /// <summary>Entities that have been destroyed (chests opened, enemies killed).</summary>
    public HashSet<string> DestroyedEntities { get; set; } = new();

    /// <summary>Entities that have been modified (moved, state changed).</summary>
    public Dictionary<string, EntitySnapshot> ModifiedEntities { get; set; } = new();

    /// <summary>Entities spawned dynamically in this scene (dropped items, placed objects).</summary>
    public List<EntitySnapshot> SpawnedEntities { get; set; } = new();

    /// <summary>Scene-level flags (puzzle solved, bridge raised, etc.).</summary>
    public Dictionary<string, bool> Flags { get; set; } = new();

    /// <summary>Scene-level counters (kill count, visit count, etc.).</summary>
    public Dictionary<string, int> Counters { get; set; } = new();
}
```

### Scene Persistence Manager

```csharp
/// <summary>
/// Manages scene state across scene transitions.
/// Captures changes when leaving a scene, applies them when returning.
/// </summary>
public class ScenePersistenceManager
{
    private readonly SceneSaveData _data = new();
    private readonly WorldSerializer _worldSerializer;
    private string _currentSceneId = "";

    public SceneSaveData SaveData => _data;

    public ScenePersistenceManager(WorldSerializer serializer)
    {
        _worldSerializer = serializer;
    }

    /// <summary>Load from a save file's scene data.</summary>
    public void LoadFromSave(SceneSaveData? saveData)
    {
        _data.Scenes.Clear();
        if (saveData != null)
        {
            foreach (var (key, state) in saveData.Scenes)
                _data.Scenes[key] = state;
        }
    }

    /// <summary>
    /// Called when leaving a scene. Captures the current scene's delta state.
    /// </summary>
    public void CaptureSceneState(World world, string sceneId)
    {
        if (!_data.Scenes.TryGetValue(sceneId, out var state))
        {
            state = new SceneState { SceneId = sceneId };
            _data.Scenes[sceneId] = state;
        }

        state.Visited = true;

        // Capture modified persistent entities
        var persistentQuery = new QueryDescription()
            .WithAll<Persistent, SceneEntity, Modified>();

        world.Query(in persistentQuery, (Entity e, ref EntityId id, ref SceneEntity _) =>
        {
            var snapshot = _worldSerializer.SerializeEntity(world, e);
            state.ModifiedEntities[id.Id] = snapshot;
        });

        // Capture spawned entities (dropped items, placed objects)
        var spawnedQuery = new QueryDescription()
            .WithAll<Persistent, SceneEntity, DynamicSpawn>();

        state.SpawnedEntities.Clear();
        world.Query(in spawnedQuery, (Entity e) =>
        {
            var snapshot = _worldSerializer.SerializeEntity(world, e);
            state.SpawnedEntities.Add(snapshot);
        });

        _currentSceneId = "";
    }

    /// <summary>
    /// Called after loading a scene. Applies stored modifications.
    /// </summary>
    public void ApplySceneState(World world, string sceneId)
    {
        _currentSceneId = sceneId;

        if (!_data.Scenes.TryGetValue(sceneId, out var state))
            return;  // First visit — no modifications

        // Destroy entities that were previously destroyed
        var destroyList = new List<Entity>();
        var entityQuery = new QueryDescription().WithAll<EntityId, SceneEntity>();

        world.Query(in entityQuery, (Entity e, ref EntityId id) =>
        {
            if (state.DestroyedEntities.Contains(id.Id))
                destroyList.Add(e);
        });

        foreach (var e in destroyList)
            world.Destroy(e);

        // Apply modifications to surviving entities
        world.Query(in entityQuery, (Entity e, ref EntityId id) =>
        {
            if (state.ModifiedEntities.TryGetValue(id.Id, out var snapshot))
            {
                // Overwrite components with saved state
                foreach (var (key, data) in snapshot.Components)
                {
                    if (_worldSerializer.Registry.Serializers.TryGetValue(key, out var serializer))
                        serializer.Deserialize(world, e, data);
                }
            }
        });

        // Recreate dynamically spawned entities
        var restorer = new WorldRestorer(_worldSerializer.Registry);
        foreach (var snapshot in state.SpawnedEntities)
        {
            var entity = world.Create<Persistent, SceneEntity, DynamicSpawn>();
            foreach (var (key, data) in snapshot.Components)
            {
                if (_worldSerializer.Registry.Serializers.TryGetValue(key, out var serializer))
                    serializer.Deserialize(world, entity, data);
            }
        }

        // Resolve cross-entity references after all entities are restored
        EntityReferenceResolver.ResolveAll(world);
    }

    /// <summary>Mark an entity as destroyed in the current scene.</summary>
    public void MarkDestroyed(string entityId)
    {
        if (string.IsNullOrEmpty(_currentSceneId)) return;

        if (!_data.Scenes.TryGetValue(_currentSceneId, out var state))
        {
            state = new SceneState { SceneId = _currentSceneId };
            _data.Scenes[_currentSceneId] = state;
        }

        state.DestroyedEntities.Add(entityId);
    }

    /// <summary>Set a scene flag (puzzle solved, bridge lowered, etc.).</summary>
    public void SetFlag(string sceneId, string flag, bool value)
    {
        EnsureScene(sceneId).Flags[flag] = value;
    }

    /// <summary>Get a scene flag.</summary>
    public bool GetFlag(string sceneId, string flag)
    {
        if (_data.Scenes.TryGetValue(sceneId, out var state) &&
            state.Flags.TryGetValue(flag, out var value))
            return value;
        return false;
    }

    /// <summary>Increment a scene counter.</summary>
    public int IncrementCounter(string sceneId, string counter, int amount = 1)
    {
        var state = EnsureScene(sceneId);
        state.Counters.TryGetValue(counter, out int current);
        state.Counters[counter] = current + amount;
        return current + amount;
    }

    private SceneState EnsureScene(string sceneId)
    {
        if (!_data.Scenes.TryGetValue(sceneId, out var state))
        {
            state = new SceneState { SceneId = sceneId };
            _data.Scenes[sceneId] = state;
        }
        return state;
    }
}

/// <summary>Tag: entity belongs to the current scene (not global).</summary>
public struct SceneEntity;

/// <summary>Tag: entity was dynamically spawned (not from level data).</summary>
public struct DynamicSpawn;

/// <summary>Tag: entity has been modified since scene load.</summary>
public struct Modified;
```

### Scene Transition Integration

```csharp
/// <summary>
/// Integrates scene persistence with scene transitions.
/// Called by SceneManager during load/unload.
/// </summary>
public class SceneTransitionHandler
{
    private readonly ScenePersistenceManager _persistence;
    private readonly AutosaveSystem _autosave;

    public SceneTransitionHandler(ScenePersistenceManager persistence,
        AutosaveSystem autosave)
    {
        _persistence = persistence;
        _autosave = autosave;
    }

    public void OnSceneUnloading(World world, string leavingSceneId)
    {
        // Capture current scene state before unloading
        _persistence.CaptureSceneState(world, leavingSceneId);
    }

    public void OnSceneLoaded(World world, string enteringSceneId)
    {
        // Apply stored modifications to the newly loaded scene
        _persistence.ApplySceneState(world, enteringSceneId);

        // Trigger autosave on scene transition
        _autosave.RequestAutosave("scene_transition");
    }
}
```

---

## 15 — Settings vs Profile vs Game State

### Three Separate Files

Don't mix these. They have different lifecycles, different privacy needs, and different update frequencies:

```
saves/
  save_00.json          ← Game state (per-playthrough)
  save_01.json
  autosave_0.json
  autosave_1.json
  autosave_2.json
  quicksave_0.json

settings/
  settings.json         ← Player preferences (global, survives uninstall)

profile/
  profile.json          ← Achievements, unlocks, statistics (cross-playthrough)
```

### Settings File

```csharp
/// <summary>
/// Player preferences — applies across all save files.
/// Loaded at game start, saved immediately on change.
/// </summary>
public class GameSettings
{
    // --- Audio ---
    public float MasterVolume { get; set; } = 1.0f;
    public float MusicVolume { get; set; } = 0.8f;
    public float SfxVolume { get; set; } = 1.0f;
    public float VoiceVolume { get; set; } = 1.0f;
    public bool Subtitles { get; set; } = true;

    // --- Display ---
    public int ResolutionWidth { get; set; } = 1920;
    public int ResolutionHeight { get; set; } = 1080;
    public bool Fullscreen { get; set; } = true;
    public bool VSync { get; set; } = true;
    public int TargetFps { get; set; } = 60;  // 0 = uncapped
    public float UiScale { get; set; } = 1.0f;

    // --- Controls ---
    public Dictionary<string, Keys> KeyBindings { get; set; } = new()
    {
        ["MoveUp"] = Keys.W,
        ["MoveDown"] = Keys.S,
        ["MoveLeft"] = Keys.A,
        ["MoveRight"] = Keys.D,
        ["Jump"] = Keys.Space,
        ["Interact"] = Keys.E,
        ["Attack"] = Keys.J,
        ["Dash"] = Keys.K,
        ["Inventory"] = Keys.Tab,
        ["Pause"] = Keys.Escape,
        ["Quicksave"] = Keys.F5,
        ["Quickload"] = Keys.F9,
    };

    public float GamepadDeadzone { get; set; } = 0.15f;
    public bool GamepadVibration { get; set; } = true;
    public float MouseSensitivity { get; set; } = 1.0f;

    // --- Accessibility ---
    public bool ScreenShake { get; set; } = true;
    public float ScreenShakeIntensity { get; set; } = 1.0f;
    public bool FlashEffects { get; set; } = true;
    public bool HoldToToggle { get; set; } = false;
    public float TextSpeed { get; set; } = 1.0f;  // Dialogue text speed
    public bool Dyslexia { get; set; } = false;    // Use dyslexia-friendly font

    // --- Gameplay ---
    public string Language { get; set; } = "en";
    public bool ShowDamageNumbers { get; set; } = true;
    public bool ShowHealthBars { get; set; } = true;
    public bool AutosaveEnabled { get; set; } = true;
    public bool TutorialEnabled { get; set; } = true;
}

/// <summary>
/// Settings manager — saves immediately on change.
/// No versioning needed (add new fields with defaults, old files load fine).
/// </summary>
public class SettingsManager
{
    private readonly string _path;
    private GameSettings _settings = new();

    public GameSettings Settings => _settings;

    public event Action<GameSettings>? OnSettingsChanged;

    public SettingsManager(string settingsDir)
    {
        _path = Path.Combine(settingsDir, "settings.json");
        Directory.CreateDirectory(settingsDir);
        Load();
    }

    public void Load()
    {
        if (!File.Exists(_path))
        {
            _settings = new GameSettings();
            Save();  // Create default file
            return;
        }

        try
        {
            var json = File.ReadAllText(_path);
            _settings = JsonSerializer.Deserialize<GameSettings>(json, SaveJson.Options)
                ?? new GameSettings();
        }
        catch
        {
            _settings = new GameSettings();
        }
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions
        {
            WriteIndented = true,  // Settings should be human-readable
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        });
        File.WriteAllText(_path, json);
        OnSettingsChanged?.Invoke(_settings);
    }

    /// <summary>Modify settings with immediate save.</summary>
    public void Modify(Action<GameSettings> modifier)
    {
        modifier(_settings);
        Save();
    }
}
```

### Player Profile

```csharp
/// <summary>
/// Cross-playthrough persistent data — achievements, unlocks, play statistics.
/// Separate from save files so achievements persist even if saves are deleted.
/// </summary>
public class PlayerProfile
{
    public string ProfileId { get; set; } = Guid.NewGuid().ToString("N")[..8];

    // --- Achievements ---
    public HashSet<string> UnlockedAchievements { get; set; } = new();
    public Dictionary<string, float> AchievementProgress { get; set; } = new();

    // --- Unlocks ---
    public HashSet<string> UnlockedCharacters { get; set; } = new();
    public HashSet<string> UnlockedSkins { get; set; } = new();
    public HashSet<string> UnlockedModes { get; set; } = new();

    // --- Statistics ---
    public TimeSpan TotalPlayTime { get; set; }
    public int TotalDeaths { get; set; }
    public int TotalEnemiesKilled { get; set; }
    public int RunsCompleted { get; set; }
    public int RunsStarted { get; set; }
    public float BestSpeedrunTime { get; set; }  // Seconds
    public Dictionary<string, int> ItemsCollected { get; set; } = new();

    // --- Meta ---
    public DateTime FirstPlayDate { get; set; } = DateTime.UtcNow;
    public DateTime LastPlayDate { get; set; }
    public int SessionCount { get; set; }
}

/// <summary>
/// Profile manager — persists across save files.
/// Saved on achievement unlock, session end, and periodically.
/// </summary>
public class ProfileManager
{
    private readonly string _path;
    private PlayerProfile _profile = new();

    public PlayerProfile Profile => _profile;

    public event Action<string>? OnAchievementUnlocked;

    public ProfileManager(string profileDir)
    {
        _path = Path.Combine(profileDir, "profile.json");
        Directory.CreateDirectory(profileDir);
        Load();
    }

    public void Load()
    {
        if (!File.Exists(_path))
        {
            _profile = new PlayerProfile();
            Save();
            return;
        }

        try
        {
            var json = File.ReadAllText(_path);
            _profile = JsonSerializer.Deserialize<PlayerProfile>(json, SaveJson.Options)
                ?? new PlayerProfile();
        }
        catch
        {
            _profile = new PlayerProfile();
        }
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(_profile, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        });
        File.WriteAllText(_path, json);
    }

    public bool UnlockAchievement(string achievementId)
    {
        if (!_profile.UnlockedAchievements.Add(achievementId))
            return false;  // Already unlocked

        Save();
        OnAchievementUnlocked?.Invoke(achievementId);
        return true;
    }

    public void UpdateProgress(string achievementId, float progress, float target)
    {
        _profile.AchievementProgress[achievementId] = progress / target;
        if (progress >= target)
            UnlockAchievement(achievementId);
    }

    public void OnSessionStart()
    {
        _profile.SessionCount++;
        _profile.LastPlayDate = DateTime.UtcNow;
    }

    public void OnSessionEnd(TimeSpan sessionDuration)
    {
        _profile.TotalPlayTime += sessionDuration;
        Save();
    }
}
```

---

## 16 — Cloud Save Integration

### Architecture

```
Local Save → Upload Queue → Cloud Provider → Download on New Device → Local Save
                                 ↕
                          Conflict Resolution
```

### Cloud Save Provider Interface

```csharp
/// <summary>
/// Abstract cloud save provider. Implement for Steam Cloud, Epic, GOG, or custom.
/// </summary>
public interface ICloudSaveProvider
{
    /// <summary>Provider name for logging.</summary>
    string Name { get; }

    /// <summary>Is cloud save available (user logged in, feature enabled)?</summary>
    bool IsAvailable { get; }

    /// <summary>Upload a local save to cloud.</summary>
    Task<bool> UploadAsync(string fileName, byte[] data);

    /// <summary>Download a save from cloud.</summary>
    Task<byte[]?> DownloadAsync(string fileName);

    /// <summary>List all cloud save files.</summary>
    Task<List<CloudSaveInfo>> ListAsync();

    /// <summary>Delete a cloud save file.</summary>
    Task<bool> DeleteAsync(string fileName);

    /// <summary>Get cloud storage quota.</summary>
    Task<(long Used, long Total)> GetQuotaAsync();
}

public class CloudSaveInfo
{
    public string FileName { get; set; } = "";
    public long SizeBytes { get; set; }
    public DateTime LastModified { get; set; }
}
```

### Steam Cloud Implementation

```csharp
/// <summary>
/// Steam Cloud implementation using Steamworks.NET.
/// Steam Cloud is file-based — each save slot is a separate file.
/// Requires "Cloud" enabled in Steamworks app settings.
/// </summary>
public class SteamCloudProvider : ICloudSaveProvider
{
    public string Name => "Steam Cloud";
    public bool IsAvailable => SteamManager.Initialized && SteamRemoteStorage.IsCloudEnabledForApp();

    public Task<bool> UploadAsync(string fileName, byte[] data)
    {
        bool success = SteamRemoteStorage.FileWrite(fileName, data, data.Length);
        return Task.FromResult(success);
    }

    public Task<byte[]?> DownloadAsync(string fileName)
    {
        if (!SteamRemoteStorage.FileExists(fileName))
            return Task.FromResult<byte[]?>(null);

        int size = SteamRemoteStorage.GetFileSize(fileName);
        var buffer = new byte[size];
        int read = SteamRemoteStorage.FileRead(fileName, buffer, size);

        return Task.FromResult<byte[]?>(read > 0 ? buffer : null);
    }

    public Task<List<CloudSaveInfo>> ListAsync()
    {
        var files = new List<CloudSaveInfo>();
        int count = SteamRemoteStorage.GetFileCount();

        for (int i = 0; i < count; i++)
        {
            string name = SteamRemoteStorage.GetFileNameAndSize(i, out int size);
            files.Add(new CloudSaveInfo
            {
                FileName = name,
                SizeBytes = size,
                LastModified = DateTimeOffset.FromUnixTimeSeconds(
                    SteamRemoteStorage.GetFileTimestamp(name)).UtcDateTime
            });
        }

        return Task.FromResult(files);
    }

    public Task<bool> DeleteAsync(string fileName)
    {
        bool success = SteamRemoteStorage.FileDelete(fileName);
        return Task.FromResult(success);
    }

    public Task<(long Used, long Total)> GetQuotaAsync()
    {
        SteamRemoteStorage.GetQuota(out ulong total, out ulong available);
        return Task.FromResult(((long)(total - available), (long)total));
    }
}
```

### Cloud Sync Manager

```csharp
/// <summary>
/// Manages synchronization between local and cloud saves.
/// Handles conflict detection and resolution.
/// </summary>
public class CloudSyncManager
{
    private readonly ICloudSaveProvider _provider;
    private readonly SaveSlotManager _slotManager;
    private readonly Queue<CloudSyncOp> _uploadQueue = new();

    public bool SyncEnabled { get; set; } = true;
    public event Action<CloudConflict>? OnConflictDetected;

    public CloudSyncManager(ICloudSaveProvider provider, SaveSlotManager slotManager)
    {
        _provider = provider;
        _slotManager = slotManager;
    }

    /// <summary>Queue a save for cloud upload (happens asynchronously).</summary>
    public void QueueUpload(int slot)
    {
        if (!SyncEnabled || !_provider.IsAvailable) return;
        _uploadQueue.Enqueue(new CloudSyncOp { Slot = slot, Op = SyncOpType.Upload });
    }

    /// <summary>Process queued uploads. Call periodically (e.g., every 30 seconds).</summary>
    public async Task ProcessQueueAsync()
    {
        while (_uploadQueue.TryDequeue(out var op))
        {
            var path = _slotManager.GetSlotPath(op.Slot);
            if (!File.Exists(path)) continue;

            var data = await File.ReadAllBytesAsync(path);
            string cloudName = $"save_{op.Slot:D2}.json";
            await _provider.UploadAsync(cloudName, data);
        }
    }

    /// <summary>
    /// Sync all slots at game startup. Detects conflicts (local newer vs cloud newer).
    /// </summary>
    public async Task SyncOnStartupAsync()
    {
        if (!SyncEnabled || !_provider.IsAvailable) return;

        var cloudFiles = await _provider.ListAsync();
        var cloudMap = cloudFiles.ToDictionary(f => f.FileName, f => f);

        for (int slot = 0; slot < _slotManager.MaxSlots; slot++)
        {
            string cloudName = $"save_{slot:D2}.json";
            bool hasLocal = _slotManager.SlotExists(slot);
            bool hasCloud = cloudMap.ContainsKey(cloudName);

            if (hasLocal && hasCloud)
            {
                // Conflict check: compare timestamps
                var localHeader = _slotManager.PeekHeader(slot);
                var cloudInfo = cloudMap[cloudName];

                if (localHeader != null &&
                    Math.Abs((localHeader.Timestamp - cloudInfo.LastModified).TotalSeconds) > 5)
                {
                    // Timestamps differ significantly — conflict
                    var conflict = new CloudConflict
                    {
                        Slot = slot,
                        LocalTimestamp = localHeader.Timestamp,
                        CloudTimestamp = cloudInfo.LastModified,
                        LocalPlayTime = localHeader.PlayTime,
                    };
                    OnConflictDetected?.Invoke(conflict);
                }
            }
            else if (!hasLocal && hasCloud)
            {
                // Cloud-only: download
                var data = await _provider.DownloadAsync(cloudName);
                if (data != null)
                {
                    await File.WriteAllBytesAsync(_slotManager.GetSlotPath(slot), data);
                    _slotManager.InvalidateCache();
                }
            }
            else if (hasLocal && !hasCloud)
            {
                // Local-only: upload
                QueueUpload(slot);
            }
        }

        await ProcessQueueAsync();
    }
}

public class CloudConflict
{
    public int Slot { get; set; }
    public DateTime LocalTimestamp { get; set; }
    public DateTime CloudTimestamp { get; set; }
    public TimeSpan LocalPlayTime { get; set; }
}

public enum ConflictResolution
{
    KeepLocal,    // Local save wins
    KeepCloud,    // Cloud save wins
    KeepBoth,     // Duplicate cloud save to a new slot
    AskPlayer,    // Show UI prompt
}

public class CloudSyncOp
{
    public int Slot { get; set; }
    public SyncOpType Op { get; set; }
}

public enum SyncOpType { Upload, Download }
```

### Conflict Resolution UI

Show the player both options with clear information:

```
╔══════════════════════════════════════════════════╗
║  ⚠ Save Conflict — Slot 1                       ║
║                                                  ║
║  ┌─ LOCAL ─────────────┐ ┌─ CLOUD ────────────┐ ║
║  │ Mar 21, 2:30 PM     │ │ Mar 20, 11:45 PM   │ ║
║  │ Play Time: 8h 23m   │ │ Play Time: 7h 58m  │ ║
║  │ Forest Temple        │ │ Town Square        │ ║
║  │ [Thumbnail]          │ │ [Thumbnail]        │ ║
║  └──────────────────────┘ └────────────────────┘ ║
║                                                  ║
║  [ Use Local ]  [ Use Cloud ]  [ Keep Both ]     ║
╚══════════════════════════════════════════════════╝
```

---

## 17 — Testing Save Systems

### Test Categories

```csharp
/// <summary>
/// Save system test suite. Every game should test these scenarios.
/// </summary>
[TestClass]
public class SaveSystemTests
{
    // --- Round-Trip Tests ---

    [TestMethod]
    public void SaveAndLoad_PreservesAllComponents()
    {
        var world = CreateTestWorld();
        var player = CreateTestPlayer(world);
        var registry = new SaveableRegistry();
        var serializer = new WorldSerializer(registry);

        // Save
        var snapshots = serializer.SerializeWorld(world);

        // Create fresh world and restore
        var newWorld = new World();
        var restorer = new WorldRestorer(registry);
        restorer.RestoreWorld(newWorld, snapshots);

        // Verify
        var query = new QueryDescription().WithAll<Persistent, Health, Position>();
        int count = 0;
        newWorld.Query(in query, (ref Health h, ref Position p) =>
        {
            Assert.AreEqual(85, h.Current);
            Assert.AreEqual(100, h.Max);
            Assert.AreEqual(150f, p.Value.X);
            Assert.AreEqual(200f, p.Value.Y);
            count++;
        });
        Assert.AreEqual(1, count);
    }

    [TestMethod]
    public void SaveAndLoad_PreservesInventory()
    {
        var world = CreateTestWorld();
        var player = CreateTestPlayer(world);
        world.Set(player, new Inventory(new[]
        {
            new InventorySlot("sword_iron", 1),
            new InventorySlot("potion_health", 5),
            new InventorySlot("", 0),  // Empty slot
        }, 3));

        // Round-trip
        var save = FullSaveRoundTrip(world);

        // Verify inventory preserved exactly
        var query = new QueryDescription().WithAll<Inventory>();
        world.Query(in query, (ref Inventory inv) =>
        {
            Assert.AreEqual(3, inv.Size);
            Assert.AreEqual("sword_iron", inv.Slots[0].ItemId);
            Assert.AreEqual(1, inv.Slots[0].Count);
            Assert.AreEqual("potion_health", inv.Slots[1].ItemId);
            Assert.AreEqual(5, inv.Slots[1].Count);
            Assert.AreEqual("", inv.Slots[2].ItemId);
        });
    }

    // --- Migration Tests ---

    [TestMethod]
    public void Migration_V1_to_Latest_PreservesData()
    {
        var json = LoadTestFixture("save_v1.json");
        var save = SaveJson.Deserialize(json);

        var pipeline = new MigrationPipeline();
        int applied = pipeline.MigrateToLatest(save);

        Assert.IsTrue(applied > 0);
        Assert.AreEqual(MigrationPipeline.CurrentVersion, save.Header.Version);

        // Player entity should still exist with migrated components
        var player = save.Entities.First(e => e.EntityId == "player");
        Assert.IsTrue(player.Components.ContainsKey("health"));
        Assert.IsFalse(player.Components.ContainsKey("hp"));  // Old key removed
    }

    [TestMethod]
    public void Migration_FutureVersion_ThrowsCleanError()
    {
        var save = new SaveFile
        {
            Header = new SaveHeader { Version = 999 }
        };

        var pipeline = new MigrationPipeline();
        Assert.ThrowsException<SaveCorruptException>(() => pipeline.MigrateToLatest(save));
    }

    // --- Corruption Tests ---

    [TestMethod]
    public void Load_CorruptJson_FallsBackToBackup()
    {
        var slotManager = CreateTempSlotManager();

        // Create valid save
        var save = CreateTestSave();
        slotManager.WriteSlot(0, save);

        // Corrupt the primary file
        File.WriteAllText(slotManager.GetSlotPath(0), "{{{{corrupt}}}}");

        // Backup should exist from WriteSlot
        var loaded = slotManager.ReadSlot(0);
        Assert.IsNotNull(loaded);
        Assert.AreEqual("test_save", loaded.Header.Name);
    }

    [TestMethod]
    public void Load_TruncatedFile_ReportsCorruption()
    {
        var slotManager = CreateTempSlotManager();

        // Write a truncated file
        var save = CreateTestSave();
        var json = SaveJson.Serialize(save);
        File.WriteAllText(slotManager.GetSlotPath(0), json[..(json.Length / 2)]);

        Assert.ThrowsException<SaveCorruptException>(() => slotManager.ReadSlot(0));
    }

    [TestMethod]
    public void Checksum_Mismatch_DetectedDuringValidation()
    {
        var save = CreateTestSave();
        save.Header.Checksum = 12345;  // Wrong checksum

        var json = SaveJson.Serialize(save);
        var validator = new SaveValidator(new MigrationPipeline());
        var result = validator.Validate(json);

        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Errors.Any(e => e.Contains("Checksum")));
    }

    // --- Async Tests ---

    [TestMethod]
    public async Task AsyncSave_DoesNotBlockMainThread()
    {
        var asyncManager = CreateAsyncManager();
        var world = CreateTestWorld();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        asyncManager.SaveAsync(world, 0, "async_test", "test_scene", TimeSpan.Zero);

        // Main thread should return immediately (snapshot is fast, serialize is async)
        sw.Stop();
        Assert.IsTrue(sw.ElapsedMilliseconds < 50,
            $"Save initiation took {sw.ElapsedMilliseconds}ms — expected <50ms");

        // Wait for background completion
        await Task.Delay(500);
        Assert.IsFalse(asyncManager.IsSaving);
    }

    // --- Edge Cases ---

    [TestMethod]
    public void EmptyWorld_SavesAndLoadsCleanly()
    {
        var world = new World();
        var save = FullSaveRoundTrip(world);
        Assert.AreEqual(0, save.Entities.Count);
    }

    [TestMethod]
    public void MaxSlotBoundary_HandledGracefully()
    {
        var slotManager = new SaveSlotManager(CreateTempDir(), maxSlots: 3);
        Assert.ThrowsException<ArgumentOutOfRangeException>(
            () => slotManager.WriteSlot(3, CreateTestSave()));
        Assert.ThrowsException<ArgumentOutOfRangeException>(
            () => slotManager.WriteSlot(-1, CreateTestSave()));
    }

    [TestMethod]
    public void UnknownComponent_SkippedGracefully()
    {
        // Simulate loading a save with components the current version doesn't know about
        var snapshot = new EntitySnapshot
        {
            EntityId = "test",
            Components = new Dictionary<string, JsonElement>
            {
                ["health"] = JsonSerializer.SerializeToElement(new { current = 50, max = 100 }),
                ["futureComponent"] = JsonSerializer.SerializeToElement(new { foo = "bar" }),
            }
        };

        var world = new World();
        var registry = new SaveableRegistry();
        var restorer = new WorldRestorer(registry);

        // Should not throw — unknown components are silently skipped
        restorer.RestoreWorld(world, new List<EntitySnapshot> { snapshot });

        // Known component should be restored
        var query = new QueryDescription().WithAll<Health>();
        int count = 0;
        world.Query(in query, (ref Health h) =>
        {
            Assert.AreEqual(50, h.Current);
            count++;
        });
        Assert.AreEqual(1, count);
    }

    // --- Helper Methods ---

    private static World CreateTestWorld() => new();

    private static Entity CreateTestPlayer(World world)
    {
        var entity = world.Create<Persistent, EntityId, Health, Position>();
        world.Set(entity, new EntityId("player"));
        world.Set(entity, new Health(85, 100));
        world.Set(entity, new Position(new Vector2(150, 200)));
        return entity;
    }

    private static SaveFile CreateTestSave() => new()
    {
        Header = new SaveHeader
        {
            Version = MigrationPipeline.CurrentVersion,
            Name = "test_save",
            Timestamp = DateTime.UtcNow,
            SceneId = "test_scene"
        }
    };
}
```

### Save System Smoke Test Checklist

```
□ New game → save → load → player position matches
□ New game → save → quit → relaunch → load → works
□ Save v1 fixture → load in current version → migrates cleanly
□ Corrupt primary save → loads from backup
□ Full inventory → save → load → all items present
□ Quest progress → save → load → quest state correct
□ Scene transition → return → modified state persists
□ Autosave fires on scene transition
□ Quicksave/quickload round-trip
□ 10 quicksaves → oldest auto-deleted
□ Settings change → restart → settings preserved
□ Achievement unlock → new game → achievement still shown
□ Cloud sync → new device → saves download
□ Cloud conflict → player prompt shown
□ Save during combat (if allowed by policy) → loads mid-combat
□ Delete save → file moved to .deleted (recoverable)
```

---

## 18 — Genre-Specific Patterns

### Roguelike — Permadeath Saves

```csharp
/// <summary>
/// Roguelike save pattern: one slot, deleted on death, locked during runs.
/// Prevents save-scumming while allowing "suspend" saves.
/// </summary>
public class RoguelikeSaveManager
{
    private readonly SaveManager _saveManager;
    private readonly ProfileManager _profileManager;
    private const int RunSlot = 0;
    private bool _runActive;

    public bool HasSuspendedRun => _saveManager.SlotManager.SlotExists(RunSlot);

    public RoguelikeSaveManager(SaveManager saveManager, ProfileManager profileManager)
    {
        _saveManager = saveManager;
        _profileManager = profileManager;
    }

    /// <summary>Start a new run — delete any existing suspended save.</summary>
    public void StartNewRun()
    {
        _saveManager.SlotManager.DeleteSlot(RunSlot);
        _runActive = true;
        _profileManager.Profile.RunsStarted++;
    }

    /// <summary>Suspend the current run (save and quit). Save is deleted on resume.</summary>
    public void SuspendRun(World world, string sceneId, TimeSpan playTime)
    {
        if (!_runActive) return;

        _saveManager.SaveToSlot(RunSlot, "Suspended Run", sceneId, playTime);
        _runActive = false;
    }

    /// <summary>Resume a suspended run. Deletes the save immediately.</summary>
    public SaveFile? ResumeRun()
    {
        var save = _saveManager.SlotManager.ReadSlot(RunSlot);
        if (save == null) return null;

