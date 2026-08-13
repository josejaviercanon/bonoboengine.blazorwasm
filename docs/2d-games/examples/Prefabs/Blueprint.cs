// =============================================================================
// Blueprint.cs — Blueprint data model, registry, and component serialization
// Extracted from: G43 — Entity Prefabs & Blueprint System (Sections 2–3)
// Guide: /G/G43_entity_prefabs.md
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace U2DToolkit.Examples.Prefabs
{
    // =========================================================================
    // Blueprint Data Model
    // =========================================================================

    /// <summary>
    /// A data-driven template describing which components an entity should have,
    /// and their default values. Blueprints are defined in JSON files and support
    /// single-parent inheritance via <see cref="Inherits"/>.
    /// <para>
    /// JSON example:
    /// <code>
    /// {
    ///   "id": "slime",
    ///   "inherits": "enemy_base",
    ///   "components": {
    ///     "Health": { "current": 3, "max": 3 },
    ///     "Sprite": { "texture": "enemies/slime" }
    ///   }
    /// }
    /// </code>
    /// </para>
    /// </summary>
    public sealed class Blueprint
    {
        /// <summary>Unique identifier for this blueprint.</summary>
        public string Id { get; set; } = "";

        /// <summary>Optional parent blueprint ID. Components merge root → leaf.</summary>
        public string? Inherits { get; set; }

        /// <summary>
        /// Component data blocks keyed by component name.
        /// Each value is a <see cref="JsonElement"/> that will be deserialized
        /// into the corresponding C# record struct at spawn time.
        /// </summary>
        public Dictionary<string, JsonElement> Components { get; set; } = new();
    }

    // =========================================================================
    // Blueprint Registry
    // =========================================================================

    /// <summary>
    /// Loads, stores, and resolves blueprints from JSON files on disk.
    /// Handles inheritance chains and supports hot-reload.
    /// </summary>
    public sealed class BlueprintRegistry
    {
        private readonly Dictionary<string, Blueprint> _blueprints = new();
        private readonly string _blueprintsDir;

        /// <param name="blueprintsDir">Directory containing blueprint JSON files.</param>
        public BlueprintRegistry(string blueprintsDir)
        {
            _blueprintsDir = blueprintsDir;
        }

        /// <summary>Load (or reload) all blueprint JSON files from the directory tree.</summary>
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

        /// <summary>Look up a blueprint by ID.</summary>
        public Blueprint? Get(string id) =>
            _blueprints.TryGetValue(id, out var bp) ? bp : null;

        /// <summary>All loaded blueprints.</summary>
        public IReadOnlyDictionary<string, Blueprint> All => _blueprints;

        /// <summary>
        /// Resolve a blueprint with all inherited components merged.
        /// Components from derived blueprints override base components wholesale
        /// (no deep field-level merge).
        /// </summary>
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

        /// <summary>Build the inheritance chain from root ancestor to the given ID.</summary>
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

        /// <summary>Hot-reload: re-read all files and replace in-memory data.</summary>
        public void Reload() => LoadAll();
    }

    // =========================================================================
    // Component Type Registry
    // =========================================================================

    /// <summary>
    /// Maps component string names (as used in JSON blueprints) to their C# types.
    /// Every component that can appear in a blueprint must be registered at startup.
    /// </summary>
    public static class ComponentTypeRegistry
    {
        private static readonly Dictionary<string, Type> _types = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Register a component type by its JSON name.</summary>
        public static void Register<T>(string name) where T : struct =>
            _types[name] = typeof(T);

        /// <summary>Look up a component type by name.</summary>
        public static Type? Lookup(string name) =>
            _types.TryGetValue(name, out var t) ? t : null;

        /// <summary>All registered component types.</summary>
        public static IReadOnlyDictionary<string, Type> All => _types;
    }

    // =========================================================================
    // Component Deserializer
    // =========================================================================

    /// <summary>
    /// Deserializes a <see cref="JsonElement"/> into a boxed component struct
    /// using the <see cref="ComponentTypeRegistry"/>.
    /// </summary>
    public static class ComponentDeserializer
    {
        /// <summary>
        /// Deserialize a JSON element into the component struct registered
        /// under the given name. Returns null if the name is not registered.
        /// </summary>
        public static object? Deserialize(string componentName, JsonElement element)
        {
            var type = ComponentTypeRegistry.Lookup(componentName);
            if (type is null) return null;

            return JsonSerializer.Deserialize(element.GetRawText(), type, SerializerCtx.Default);
        }
    }

    // =========================================================================
    // Example Components
    // =========================================================================

    /// <summary>World position, rotation, and scale.</summary>
    public record struct Transform(float X, float Y, float Rotation, float ScaleX, float ScaleY)
    {
        public Transform() : this(0, 0, 0, 1f, 1f) { }
    }

    /// <summary>Sprite rendering data.</summary>
    public record struct Sprite(string Texture, int FrameWidth, int FrameHeight, int Layer)
    {
        public Sprite() : this("", 16, 16, 0) { }
    }

    /// <summary>Entity health points.</summary>
    public record struct Health(int Current, int Max)
    {
        public Health() : this(1, 1) { }
    }

    /// <summary>Movement velocity vector.</summary>
    public record struct Velocity(float X, float Y)
    {
        public Velocity() : this(0, 0) { }
    }

    /// <summary>Tag marking an entity as an enemy.</summary>
    public record struct EnemyTag();

    /// <summary>Collision box dimensions and offset.</summary>
    public record struct Collider(float Width, float Height, float OffsetX, float OffsetY)
    {
        public Collider() : this(16, 16, 0, 0) { }
    }

    /// <summary>Animation playback state.</summary>
    public record struct AnimationState(string CurrentAnim, int Frame, float Elapsed)
    {
        public AnimationState() : this("idle", 0, 0f) { }
    }

    /// <summary>Contact damage dealt to entities on collision.</summary>
    public record struct ContactDamage(int Damage)
    {
        public ContactDamage() : this(1) { }
    }

    /// <summary>Horizontal patrol behavior bounds and speed.</summary>
    public record struct Patrol(float LeftBound, float RightBound, float Speed)
    {
        public Patrol() : this(0, 64, 30f) { }
    }

    /// <summary>Reference to a loot/spawn table.</summary>
    public record struct Loot(string TableId)
    {
        public Loot() : this("") { }
    }

    // =========================================================================
    // Source-Generated JSON Context (AOT-friendly)
    // =========================================================================

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
    internal partial class SerializerCtx : JsonSerializerContext { }
}
