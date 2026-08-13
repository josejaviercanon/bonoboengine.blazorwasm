// =============================================================================
// EntityFactory.cs — Entity spawning from blueprint definitions
// Extracted from: G43 — Entity Prefabs & Blueprint System (Section 4)
// Guide: /G/G43_entity_prefabs.md
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Arch.Core;
using Arch.Core.Extensions;
using Microsoft.Xna.Framework;

namespace U2DToolkit.Examples.Prefabs
{
    /// <summary>
    /// The single entry point for spawning entities from blueprints.
    /// Resolves blueprint inheritance, deserializes every component from JSON,
    /// and attaches them to a fresh Arch entity.
    /// <para>
    /// Supports:
    /// <list type="bullet">
    ///   <item>Blueprint inheritance (base → derived component merging).</item>
    ///   <item>Per-instance overrides (e.g., custom health for a specific enemy).</item>
    ///   <item>Position override shorthand.</item>
    ///   <item>Dynamic component attachment via cached reflection.</item>
    /// </list>
    /// </para>
    /// </summary>
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
        /// Spawn an entity from a blueprint with optional position override
        /// and per-instance component overrides.
        /// </summary>
        /// <param name="blueprintId">The blueprint ID to spawn.</param>
        /// <param name="position">If provided, overrides the Transform position.</param>
        /// <param name="overrides">
        /// Optional component-level overrides. Each entry replaces the
        /// corresponding component from the resolved blueprint entirely.
        /// </param>
        /// <returns>The newly created Arch entity.</returns>
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
        /// Use Arch's generic Set via cached reflection to attach a boxed struct.
        /// The MethodInfo for Add/Set is cached per component type for performance.
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

    // =========================================================================
    // Usage Example
    // =========================================================================
    //
    // // During initialization:
    // var registry = new BlueprintRegistry("Content/Blueprints");
    // registry.LoadAll();
    // var factory = new EntityFactory(world, registry);
    //
    // // Register component types:
    // ComponentTypeRegistry.Register<Transform>("Transform");
    // ComponentTypeRegistry.Register<Sprite>("Sprite");
    // ComponentTypeRegistry.Register<Health>("Health");
    // // ... etc.
    //
    // // Spawn a slime at a specific position:
    // var slime = factory.Spawn("slime", position: new Vector2(128, 256));
    //
    // // Spawn with custom overrides:
    // var toughSlime = factory.Spawn("slime", position: new Vector2(200, 300),
    //     overrides: new Dictionary<string, JsonElement>
    //     {
    //         ["Health"] = JsonSerializer.SerializeToElement(
    //             new Health(10, 10), SerializerCtx.Default.Health)
    //     });
    //
    // =========================================================================
}
