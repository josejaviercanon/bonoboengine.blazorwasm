# G66 — Building & Placement Systems

> **Category:** Guide · **Related:** [G37 Tilemap Systems](./G37_tilemap_systems.md) · [G65 Economy & Shop Systems](./G65_economy_shop_systems.md) · [G10 Custom Game Systems §1 Inventory](./G10_custom_game_systems.md) · [G5 UI Framework](./G5_ui_framework.md) · [G3 Physics & Collision](./G3_physics_and_collision.md) · [G4 AI Systems](./G4_ai_systems.md) · [G40 Pathfinding](./G40_pathfinding.md)

> Complete implementation guide for building, placement, and construction systems in MonoGame + Arch ECS. Covers grid-based and free-form placement, ghost previews, validity checking, building upgrades, construction queues, and destruction. Used by survival games (base building), strategy/RTS (structures & production buildings), tower defense (tower placement), and sandbox games. All systems are composable — use the pieces your genre needs.

---

## Table of Contents

1. [Design Philosophy](#1--design-philosophy)
2. [Building Definition & Registry](#2--building-definition--registry)
3. [Grid System](#3--grid-system)
4. [Ghost Preview & Cursor](#4--ghost-preview--cursor)
5. [Placement Validation](#5--placement-validation)
6. [Placement Execution](#6--placement-execution)
7. [Free-Form Placement](#7--free-form-placement)
8. [Construction & Build Time](#8--construction--build-time)
9. [Building Upgrades](#9--building-upgrades)
10. [Building Health & Destruction](#10--building-health--destruction)
11. [Snapping & Attachment](#11--snapping--attachment)
12. [Pathfinding Integration](#12--pathfinding-integration)
13. [Save/Load Integration](#13--saveload-integration)
14. [UI Integration](#14--ui-integration)
15. [Genre-Specific Patterns](#15--genre-specific-patterns)
16. [Tuning Reference](#16--tuning-reference)

---

## 1 — Design Philosophy

### Why a Dedicated Building System?

Building mechanics appear simple — "click grid, place thing" — until you need:

- **Validity rules** that vary by building type (walls only on edges, farms only on grass, towers only on path-adjacent tiles)
- **Ghost previews** that show exactly what you're about to place, tinted green/red for valid/invalid
- **Resource costs** integrated with your economy — check affordability *before* placement, deduct *after*
- **Construction time** with progress bars, worker assignment, and cancellation refunds
- **Pathfinding awareness** so placed buildings don't block the only route enemies can take

### Core Principles

1. **Separation of concerns.** Building *data* (what can be built) lives in a registry. Building *placement logic* (where/how) is a system. Building *entities* (what exists in the world) are ECS components. Keep them apart.
2. **Validate before committing.** Never place first and check later. The validation pipeline runs every frame during preview mode so the player always knows if placement is valid.
3. **Events drive side effects.** When a building is placed, upgraded, or destroyed, fire ECS events. Let economy, pathfinding, fog of war, and UI react independently.
4. **Grid is optional.** Grid-based placement is the common case, but free-form placement (snap-to-terrain, radius-based) uses the same validation pipeline with different position resolvers.

---

## 2 — Building Definition & Registry

### Building Definition

```csharp
/// <summary>
/// Static data describing a building type. Lives in registry, not ECS.
/// </summary>
public record BuildingDef(
    string Id,                  // "wall_wood", "farm", "barracks", "turret_basic"
    string DisplayName,         // "Wooden Wall"
    string Description,         // "Basic defensive wall. 100 HP."
    string Category,            // "defense", "production", "resource", "utility"
    string SpriteKey,           // Texture key for rendering
    string GhostSpriteKey,      // Texture key for placement preview (often same + transparency)
    Point Size,                 // Grid footprint in tiles (e.g., 1x1, 2x2, 3x2)
    BuildCost Cost,             // Resources required
    float BuildTimeSeconds,     // 0 = instant placement
    int MaxHealth,              // Hitpoints (-1 = indestructible)
    PlacementRule[] Rules,      // Validation rules (see §5)
    string[] Tags,              // "solid", "walkable", "powered", "snappable"
    string? UpgradesTo,         // Next tier building ID (null = max tier)
    Dictionary<string, float> Properties  // Genre-specific: "damage", "range", "production_rate"
);

/// <summary>
/// Resource cost for building or upgrading.
/// </summary>
public record BuildCost(
    Dictionary<string, int> Resources,  // "wood" → 50, "stone" → 20
    float BuildTimeMultiplier = 1f      // Modifier for upgrade costs
)
{
    public static BuildCost Free => new(new Dictionary<string, int>());

    public BuildCost Scaled(float factor) => new(
        Resources.ToDictionary(kv => kv.Key, kv => (int)(kv.Value * factor)),
        BuildTimeMultiplier
    );
}

/// <summary>
/// Categories for organizing the build menu.
/// </summary>
public static class BuildingCategory
{
    public const string Defense = "defense";
    public const string Production = "production";
    public const string Resource = "resource";
    public const string Utility = "utility";
    public const string Decoration = "decoration";
}
```

### Building Registry

```csharp
public class BuildingRegistry
{
    private readonly Dictionary<string, BuildingDef> _buildings = new();
    private readonly Dictionary<string, List<string>> _byCategory = new();

    public void Register(BuildingDef def)
    {
        _buildings[def.Id] = def;

        if (!_byCategory.ContainsKey(def.Category))
            _byCategory[def.Category] = new List<string>();
        _byCategory[def.Category].Add(def.Id);
    }

    public BuildingDef? Get(string id) =>
        _buildings.TryGetValue(id, out var def) ? def : null;

    public IReadOnlyList<string> GetByCategory(string category) =>
        _byCategory.TryGetValue(category, out var list)
            ? list.AsReadOnly()
            : Array.Empty<string>().AsReadOnly();

    public IEnumerable<BuildingDef> All => _buildings.Values;

    /// <summary>
    /// Load definitions from JSON data files.
    /// </summary>
    public void LoadFromJson(string json)
    {
        var defs = JsonSerializer.Deserialize<BuildingDef[]>(json);
        if (defs == null) return;
        foreach (var def in defs) Register(def);
    }
}
```

### Example Building Definitions

```csharp
// Survival game buildings
registry.Register(new BuildingDef(
    Id: "wall_wood",
    DisplayName: "Wooden Wall",
    Description: "Basic wall segment. Keeps out weak enemies.",
    Category: BuildingCategory.Defense,
    SpriteKey: "buildings/wall_wood",
    GhostSpriteKey: "buildings/wall_wood_ghost",
    Size: new Point(1, 1),
    Cost: new BuildCost(new() { ["wood"] = 10 }),
    BuildTimeSeconds: 2f,
    MaxHealth: 100,
    Rules: new[] { PlacementRule.NotOnWater, PlacementRule.NotOccupied },
    Tags: new[] { "solid", "snappable" },
    UpgradesTo: "wall_stone",
    Properties: new() { ["armor"] = 5f }
));

registry.Register(new BuildingDef(
    Id: "farm",
    DisplayName: "Farm Plot",
    Description: "Produces food over time. Must be on grass.",
    Category: BuildingCategory.Production,
    SpriteKey: "buildings/farm",
    GhostSpriteKey: "buildings/farm_ghost",
    Size: new Point(2, 2),
    Cost: new BuildCost(new() { ["wood"] = 20, ["stone"] = 5 }),
    BuildTimeSeconds: 5f,
    MaxHealth: 50,
    Rules: new[] { PlacementRule.OnTerrain("grass"), PlacementRule.NotOccupied },
    Tags: new[] { "walkable", "production" },
    UpgradesTo: "farm_irrigated",
    Properties: new() { ["food_per_second"] = 0.5f }
));

// Strategy game buildings
registry.Register(new BuildingDef(
    Id: "barracks",
    DisplayName: "Barracks",
    Description: "Trains infantry units.",
    Category: BuildingCategory.Production,
    SpriteKey: "buildings/barracks",
    GhostSpriteKey: "buildings/barracks_ghost",
    Size: new Point(3, 3),
    Cost: new BuildCost(new() { ["gold"] = 200, ["wood"] = 100 }),
    BuildTimeSeconds: 30f,
    MaxHealth: 500,
    Rules: new[] {
        PlacementRule.NotOnWater,
        PlacementRule.NotOccupied,
        PlacementRule.MinDistanceFromEnemy(10)
    },
    Tags: new[] { "solid", "powered" },
    UpgradesTo: "barracks_advanced",
    Properties: new() { ["train_speed"] = 1f, ["queue_size"] = 5f }
));
```

---

## 3 — Grid System

The grid converts between world coordinates and discrete tile positions. This is the foundation for all grid-based placement.

### Grid Component & Manager

```csharp
/// <summary>
/// Defines the placement grid. Attach to a world/level entity.
/// </summary>
public record struct BuildGrid(
    int Width,              // Grid width in tiles
    int Height,             // Grid height in tiles
    int TileSize,           // Pixels per tile (e.g., 32, 64)
    Vector2 Origin          // World position of grid origin (top-left)
);

/// <summary>
/// Tracks what occupies each grid cell.
/// </summary>
public class GridOccupancy
{
    private readonly Entity?[,] _cells;
    public int Width { get; }
    public int Height { get; }
    public int TileSize { get; }
    public Vector2 Origin { get; }

    public GridOccupancy(int width, int height, int tileSize, Vector2 origin)
    {
        Width = width;
        Height = height;
        TileSize = tileSize;
        Origin = origin;
        _cells = new Entity?[width, height];
    }

    /// <summary>
    /// Convert world position to grid coordinates.
    /// </summary>
    public Point WorldToGrid(Vector2 worldPos)
    {
        var local = worldPos - Origin;
        return new Point(
            (int)MathF.Floor(local.X / TileSize),
            (int)MathF.Floor(local.Y / TileSize)
        );
    }

    /// <summary>
    /// Convert grid coordinates to world position (top-left corner of tile).
    /// </summary>
    public Vector2 GridToWorld(Point gridPos) =>
        Origin + new Vector2(gridPos.X * TileSize, gridPos.Y * TileSize);

    /// <summary>
    /// Get the center of a tile in world coordinates.
    /// </summary>
    public Vector2 GridToWorldCenter(Point gridPos) =>
        GridToWorld(gridPos) + new Vector2(TileSize / 2f, TileSize / 2f);

    /// <summary>
    /// Snap a world position to the nearest grid-aligned position.
    /// </summary>
    public Vector2 SnapToGrid(Vector2 worldPos) =>
        GridToWorld(WorldToGrid(worldPos));

    /// <summary>
    /// Check if grid coordinates are within bounds.
    /// </summary>
    public bool InBounds(Point gridPos) =>
        gridPos.X >= 0 && gridPos.X < Width &&
        gridPos.Y >= 0 && gridPos.Y < Height;

    /// <summary>
    /// Check if a multi-tile footprint fits entirely within bounds.
    /// </summary>
    public bool FootprintInBounds(Point gridPos, Point size) =>
        InBounds(gridPos) && InBounds(new Point(gridPos.X + size.X - 1, gridPos.Y + size.Y - 1));

    /// <summary>
    /// Get the entity occupying a cell (null = empty).
    /// </summary>
    public Entity? GetOccupant(Point gridPos) =>
        InBounds(gridPos) ? _cells[gridPos.X, gridPos.Y] : null;

    /// <summary>
    /// Check if all cells in a footprint are empty.
    /// </summary>
    public bool IsFootprintClear(Point gridPos, Point size)
    {
        for (int x = gridPos.X; x < gridPos.X + size.X; x++)
        for (int y = gridPos.Y; y < gridPos.Y + size.Y; y++)
        {
            if (!InBounds(new Point(x, y)) || _cells[x, y] != null)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Occupy cells with a building entity.
    /// </summary>
    public void Occupy(Point gridPos, Point size, Entity entity)
    {
        for (int x = gridPos.X; x < gridPos.X + size.X; x++)
        for (int y = gridPos.Y; y < gridPos.Y + size.Y; y++)
        {
            if (InBounds(new Point(x, y)))
                _cells[x, y] = entity;
        }
    }

    /// <summary>
    /// Clear cells occupied by a building.
    /// </summary>
    public void Vacate(Point gridPos, Point size)
    {
        for (int x = gridPos.X; x < gridPos.X + size.X; x++)
        for (int y = gridPos.Y; y < gridPos.Y + size.Y; y++)
        {
            if (InBounds(new Point(x, y)))
                _cells[x, y] = null;
        }
    }

    /// <summary>
    /// Get all occupied cells (for pathfinding integration).
    /// </summary>
    public IEnumerable<Point> GetOccupiedCells()
    {
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
        {
            if (_cells[x, y] != null)
                yield return new Point(x, y);
        }
    }
}
```

### Tilemap Terrain Layer

Buildings often have terrain requirements (farms on grass, docks on water-adjacent). Use a terrain layer alongside the occupancy grid:

```csharp
public enum TerrainType : byte
{
    Grass,
    Dirt,
    Sand,
    Stone,
    Water,
    Snow,
    Void    // Unbuildable edge/out-of-bounds
}

public class TerrainGrid
{
    private readonly TerrainType[,] _terrain;
    public int Width { get; }
    public int Height { get; }

    public TerrainGrid(int width, int height)
    {
        Width = width;
        Height = height;
        _terrain = new TerrainType[width, height];
    }

    public TerrainType Get(Point pos) =>
        pos.X >= 0 && pos.X < Width && pos.Y >= 0 && pos.Y < Height
            ? _terrain[pos.X, pos.Y]
            : TerrainType.Void;

    public void Set(Point pos, TerrainType terrain)
    {
        if (pos.X >= 0 && pos.X < Width && pos.Y >= 0 && pos.Y < Height)
            _terrain[pos.X, pos.Y] = terrain;
    }

    /// <summary>
    /// Check if all tiles under a footprint match a terrain requirement.
    /// </summary>
    public bool AllMatch(Point pos, Point size, TerrainType required)
    {
        for (int x = pos.X; x < pos.X + size.X; x++)
        for (int y = pos.Y; y < pos.Y + size.Y; y++)
        {
            if (Get(new Point(x, y)) != required) return false;
        }
        return true;
    }

    /// <summary>
    /// Check if any tile under a footprint matches a terrain type.
    /// </summary>
    public bool AnyMatch(Point pos, Point size, TerrainType type)
    {
        for (int x = pos.X; x < pos.X + size.X; x++)
        for (int y = pos.Y; y < pos.Y + size.Y; y++)
        {
            if (Get(new Point(x, y)) == type) return true;
        }
        return false;
    }

    /// <summary>
    /// Check if any adjacent tile (4-directional) matches a terrain type.
    /// Useful for "near water" or "near road" placement rules.
    /// </summary>
    public bool HasAdjacentTerrain(Point pos, Point size, TerrainType type)
    {
        for (int x = pos.X - 1; x <= pos.X + size.X; x++)
        for (int y = pos.Y - 1; y <= pos.Y + size.Y; y++)
        {
            // Skip interior tiles
            if (x >= pos.X && x < pos.X + size.X &&
                y >= pos.Y && y < pos.Y + size.Y) continue;
            if (Get(new Point(x, y)) == type) return true;
        }
        return false;
    }
}
```

---

## 4 — Ghost Preview & Cursor

The ghost preview shows where a building will be placed and whether the position is valid. This is critical UX — without it, players can't tell if their placement will work until they click.

### Placement Mode Components

```csharp
/// <summary>
/// Attached to the "placement cursor" entity when the player enters build mode.
/// </summary>
public record struct PlacementGhost(
    string BuildingId,      // Which building def we're placing
    Point GridPosition,     // Current snapped grid position
    bool IsValid,           // Current position passes all validation rules
    string? InvalidReason,  // Why placement is invalid (for tooltip)
    float Rotation          // 0, 90, 180, 270 for rotatable buildings
);

/// <summary>
/// Visual tint for the ghost preview.
/// </summary>
public record struct GhostTint(
    Color ValidColor,       // Green-ish transparent
    Color InvalidColor      // Red-ish transparent
)
{
    public static GhostTint Default => new(
        ValidColor: new Color(100, 255, 100, 128),
        InvalidColor: new Color(255, 100, 100, 128)
    );

    public Color Current(bool isValid) => isValid ? ValidColor : InvalidColor;
}
```

### Ghost Update System

```csharp
public class PlacementGhostSystem : BaseSystem<World, float>
{
    private readonly GridOccupancy _grid;
    private readonly PlacementValidator _validator;

    public PlacementGhostSystem(World world, GridOccupancy grid, PlacementValidator validator)
        : base(world)
    {
        _grid = grid;
        _validator = validator;
    }

    public override void Update(in float deltaTime)
    {
        var mouseWorld = GetMouseWorldPosition(); // Camera.ScreenToWorld(Mouse.Position)

        var query = new QueryDescription().WithAll<PlacementGhost, Position, Sprite>();

        World.Query(in query, (ref PlacementGhost ghost, ref Position pos, ref Sprite sprite) =>
        {
            var registry = World.GetResource<BuildingRegistry>();
            var def = registry.Get(ghost.BuildingId);
            if (def == null) return;

            // Snap to grid
            var gridPos = _grid.WorldToGrid(mouseWorld);
            ghost.GridPosition = gridPos;

            // Update world position to snapped location
            var snapped = _grid.GridToWorld(gridPos);
            pos.Value = snapped;

            // Validate placement
            var result = _validator.Validate(def, gridPos, ghost.Rotation);
            ghost.IsValid = result.IsValid;
            ghost.InvalidReason = result.Reason;

            // Update visual tint
            var tint = GhostTint.Default;
            sprite.Color = tint.Current(ghost.IsValid);
        });
    }

    private Vector2 GetMouseWorldPosition()
    {
        var mouseScreen = Mouse.GetState().Position.ToVector2();
        // Assuming you have a camera system — see G20
        var camera = World.GetResource<Camera2D>();
        return camera.ScreenToWorld(mouseScreen);
    }
}
```

### Entering & Exiting Build Mode

```csharp
public class BuildModeManager
{
    private readonly World _world;
    private readonly GridOccupancy _grid;
    private readonly BuildingRegistry _registry;
    private Entity _ghostEntity;
    private bool _isActive;

    public bool IsActive => _isActive;
    public string? CurrentBuildingId { get; private set; }

    public BuildModeManager(World world, GridOccupancy grid, BuildingRegistry registry)
    {
        _world = world;
        _grid = grid;
        _registry = registry;
    }

    /// <summary>
    /// Enter build mode with a specific building selected.
    /// Called when player clicks a building in the build menu.
    /// </summary>
    public void EnterBuildMode(string buildingId)
    {
        var def = _registry.Get(buildingId);
        if (def == null) return;

        // Exit current mode if switching buildings
        if (_isActive) ExitBuildMode();

        _isActive = true;
        CurrentBuildingId = buildingId;

        // Spawn ghost entity
        _ghostEntity = _world.Create(
            new PlacementGhost(
                BuildingId: buildingId,
                GridPosition: Point.Zero,
                IsValid: false,
                InvalidReason: null,
                Rotation: 0f
            ),
            new Position(Vector2.Zero),
            new Sprite(def.GhostSpriteKey, GhostTint.Default.InvalidColor),
            new RenderLayer(RenderLayers.GhostPreview)  // Render above buildings
        );

        // Fire event for UI (show cancel button, hide build menu)
        _world.Create(new BuildModeEnteredEvent(buildingId));
    }

    /// <summary>
    /// Exit build mode, destroying the ghost preview.
    /// </summary>
    public void ExitBuildMode()
    {
        if (!_isActive) return;

        _isActive = false;
        CurrentBuildingId = null;

        if (_world.IsAlive(_ghostEntity))
            _world.Destroy(_ghostEntity);

        _world.Create(new BuildModeExitedEvent());
    }

    /// <summary>
    /// Rotate the current ghost (for buildings that support rotation).
    /// </summary>
    public void RotateGhost()
    {
        if (!_isActive) return;
        ref var ghost = ref _world.Get<PlacementGhost>(_ghostEntity);
        ghost.Rotation = (ghost.Rotation + 90f) % 360f;
    }
}
```

### Multi-Tile Ghost Rendering

For buildings larger than 1×1, render the ghost across all occupied tiles:

```csharp
public class GhostRenderSystem : BaseSystem<World, float>
{
    private readonly GridOccupancy _grid;
    private readonly SpriteBatch _spriteBatch;

    public GhostRenderSystem(World world, GridOccupancy grid, SpriteBatch spriteBatch)
        : base(world)
    {
        _grid = grid;
        _spriteBatch = spriteBatch;
    }

    public override void Update(in float deltaTime)
    {
        var query = new QueryDescription().WithAll<PlacementGhost, Position>();

        World.Query(in query, (ref PlacementGhost ghost, ref Position pos) =>
        {
            var registry = World.GetResource<BuildingRegistry>();
            var def = registry.Get(ghost.BuildingId);
            if (def == null) return;

            var tint = GhostTint.Default.Current(ghost.IsValid);

            // Draw the building sprite
            var texture = ContentManager.GetTexture(def.GhostSpriteKey);
            var worldPos = _grid.GridToWorld(ghost.GridPosition);
            var destRect = new Rectangle(
                (int)worldPos.X, (int)worldPos.Y,
                def.Size.X * _grid.TileSize,
                def.Size.Y * _grid.TileSize
            );
            _spriteBatch.Draw(texture, destRect, tint);

            // Draw grid cell outlines for the footprint
            for (int x = 0; x < def.Size.X; x++)
            for (int y = 0; y < def.Size.Y; y++)
            {
                var cellWorld = _grid.GridToWorld(
                    new Point(ghost.GridPosition.X + x, ghost.GridPosition.Y + y)
                );
                var cellRect = new Rectangle(
                    (int)cellWorld.X, (int)cellWorld.Y,
                    _grid.TileSize, _grid.TileSize
                );
                DrawRectOutline(_spriteBatch, cellRect, tint, 1);
            }

            // Draw invalid reason tooltip
            if (!ghost.IsValid && ghost.InvalidReason != null)
            {
                var tooltipPos = worldPos + new Vector2(0, -20);
                DrawTooltip(_spriteBatch, ghost.InvalidReason, tooltipPos, Color.Red);
            }
        });
    }

    private void DrawRectOutline(SpriteBatch sb, Rectangle rect, Color color, int thickness)
    {
        var pixel = ContentManager.GetPixelTexture();
        sb.Draw(pixel, new Rectangle(rect.X, rect.Y, rect.Width, thickness), color);
        sb.Draw(pixel, new Rectangle(rect.X, rect.Bottom - thickness, rect.Width, thickness), color);
        sb.Draw(pixel, new Rectangle(rect.X, rect.Y, thickness, rect.Height), color);
        sb.Draw(pixel, new Rectangle(rect.Right - thickness, rect.Y, thickness, rect.Height), color);
    }

    private void DrawTooltip(SpriteBatch sb, string text, Vector2 pos, Color color)
    {
        var font = ContentManager.GetFont("ui_small");
        sb.DrawString(font, text, pos, color);
    }
}
```

---

## 5 — Placement Validation

Validation is the heart of a good building system. Every frame during preview mode, the validator tells the player if placement is legal — and if not, why.

### Placement Rules

```csharp
/// <summary>
/// A single placement rule. Returns true if the position is valid.
/// </summary>
public abstract class PlacementRule
{
    public abstract string Name { get; }
    public abstract bool Check(PlacementContext ctx);
    public abstract string FailureMessage { get; }

    // --- Built-in rules ---

    public static readonly PlacementRule NotOccupied = new NotOccupiedRule();
    public static readonly PlacementRule NotOnWater = new TerrainExcludeRule(TerrainType.Water);
    public static readonly PlacementRule InBounds = new InBoundsRule();

    public static PlacementRule OnTerrain(string terrainName) =>
        new TerrainRequireRule(Enum.Parse<TerrainType>(terrainName, ignoreCase: true));

    public static PlacementRule MinDistanceFromEnemy(int tiles) =>
        new MinDistanceRule(tiles, "enemy");

    public static PlacementRule NearBuilding(string buildingId, int maxDistance) =>
        new NearBuildingRule(buildingId, maxDistance);

    public static PlacementRule RequireAdjacent(string tag) =>
        new AdjacentTagRule(tag);

    public static PlacementRule MaxPerPlayer(int count) =>
        new MaxCountRule(count);

    public static PlacementRule MustNotBlockPath =>
        new PathBlockRule();
}

/// <summary>
/// All the context a placement rule needs to make its decision.
/// </summary>
public record PlacementContext(
    BuildingDef Building,
    Point GridPosition,
    Point Size,            // After rotation
    float Rotation,
    GridOccupancy Grid,
    TerrainGrid Terrain,
    World World             // For querying other entities
);

/// <summary>
/// Result of validation — valid, or invalid with a human-readable reason.
/// </summary>
public record ValidationResult(bool IsValid, string? Reason)
{
    public static ValidationResult Valid => new(true, null);
    public static ValidationResult Invalid(string reason) => new(false, reason);
}
```

### Built-In Rule Implementations

```csharp
public class NotOccupiedRule : PlacementRule
{
    public override string Name => "Not Occupied";
    public override string FailureMessage => "Space is occupied";

    public override bool Check(PlacementContext ctx) =>
        ctx.Grid.IsFootprintClear(ctx.GridPosition, ctx.Size);
}

public class InBoundsRule : PlacementRule
{
    public override string Name => "In Bounds";
    public override string FailureMessage => "Outside buildable area";

    public override bool Check(PlacementContext ctx) =>
        ctx.Grid.FootprintInBounds(ctx.GridPosition, ctx.Size);
}

public class TerrainExcludeRule : PlacementRule
{
    private readonly TerrainType _excluded;

    public TerrainExcludeRule(TerrainType excluded) => _excluded = excluded;

    public override string Name => $"No {_excluded}";
    public override string FailureMessage => $"Cannot build on {_excluded}";

    public override bool Check(PlacementContext ctx) =>
        !ctx.Terrain.AnyMatch(ctx.GridPosition, ctx.Size, _excluded);
}

public class TerrainRequireRule : PlacementRule
{
    private readonly TerrainType _required;

    public TerrainRequireRule(TerrainType required) => _required = required;

    public override string Name => $"Requires {_required}";
    public override string FailureMessage => $"Must be placed on {_required}";

    public override bool Check(PlacementContext ctx) =>
        ctx.Terrain.AllMatch(ctx.GridPosition, ctx.Size, _required);
}

public class AdjacentTagRule : PlacementRule
{
    private readonly string _tag;

    public AdjacentTagRule(string tag) => _tag = tag;

    public override string Name => $"Adjacent to {_tag}";
    public override string FailureMessage => $"Must be adjacent to a {_tag} building";

    public override bool Check(PlacementContext ctx)
    {
        // Check all cells adjacent to the footprint
        for (int x = ctx.GridPosition.X - 1; x <= ctx.GridPosition.X + ctx.Size.X; x++)
        for (int y = ctx.GridPosition.Y - 1; y <= ctx.GridPosition.Y + ctx.Size.Y; y++)
        {
            // Skip interior cells
            if (x >= ctx.GridPosition.X && x < ctx.GridPosition.X + ctx.Size.X &&
                y >= ctx.GridPosition.Y && y < ctx.GridPosition.Y + ctx.Size.Y)
                continue;

            var occupant = ctx.Grid.GetOccupant(new Point(x, y));
            if (occupant == null) continue;

            // Check if the adjacent building has the required tag
            if (ctx.World.Has<Building>(occupant.Value))
            {
                ref var building = ref ctx.World.Get<Building>(occupant.Value);
                var def = ctx.World.GetResource<BuildingRegistry>().Get(building.DefId);
                if (def?.Tags.Contains(_tag) == true) return true;
            }
        }
        return false;
    }
}

public class MaxCountRule : PlacementRule
{
    private readonly int _maxCount;

    public MaxCountRule(int maxCount) => _maxCount = maxCount;

    public override string Name => $"Max {_maxCount}";
    public override string FailureMessage => $"Maximum of {_maxCount} already built";

    public override bool Check(PlacementContext ctx)
    {
        int count = 0;
        var query = new QueryDescription().WithAll<Building>();
        ctx.World.Query(in query, (ref Building b) =>
        {
            if (b.DefId == ctx.Building.Id) count++;
        });
        return count < _maxCount;
    }
}

/// <summary>
/// Prevents placement if it would block the only path between two points.
/// Critical for tower defense — prevents players from walling off the enemy path entirely.
/// </summary>
public class PathBlockRule : PlacementRule
{
    public override string Name => "Must not block path";
    public override string FailureMessage => "Would block enemy path";

    public override bool Check(PlacementContext ctx)
    {
        // Temporarily mark cells as occupied
        ctx.Grid.Occupy(ctx.GridPosition, ctx.Size, Entity.Null);

        // Check if path still exists (uses your pathfinding system — see G40)
        var pathfinder = ctx.World.GetResource<Pathfinder>();
        var pathExists = pathfinder.HasPath(
            ctx.World.GetResource<PathCheckpoints>().Start,
            ctx.World.GetResource<PathCheckpoints>().End
        );

        // Undo temporary occupation
        ctx.Grid.Vacate(ctx.GridPosition, ctx.Size);

        return pathExists;
    }
}
```

### Placement Validator

```csharp
public class PlacementValidator
{
    private readonly GridOccupancy _grid;
    private readonly TerrainGrid _terrain;
    private readonly World _world;

    public PlacementValidator(GridOccupancy grid, TerrainGrid terrain, World world)
    {
        _grid = grid;
        _terrain = terrain;
        _world = world;
    }

    /// <summary>
    /// Run all placement rules for a building at a position.
    /// Returns the first failure (or Valid if all pass).
    /// </summary>
    public ValidationResult Validate(BuildingDef def, Point gridPos, float rotation = 0f)
    {
        var size = GetRotatedSize(def.Size, rotation);

        var ctx = new PlacementContext(
            Building: def,
            GridPosition: gridPos,
            Size: size,
            Rotation: rotation,
            Grid: _grid,
            Terrain: _terrain,
            World: _world
        );

        // Always check bounds first
        if (!PlacementRule.InBounds.Check(ctx))
            return ValidationResult.Invalid(PlacementRule.InBounds.FailureMessage);

        // Run building-specific rules
        foreach (var rule in def.Rules)
        {
            if (!rule.Check(ctx))
                return ValidationResult.Invalid(rule.FailureMessage);
        }

        // Check resource affordability
        if (!CanAfford(def.Cost))
            return ValidationResult.Invalid("Not enough resources");

        return ValidationResult.Valid;
    }

    /// <summary>
    /// Run all rules and return ALL failures (for detailed UI feedback).
    /// </summary>
    public List<ValidationResult> ValidateAll(BuildingDef def, Point gridPos, float rotation = 0f)
    {
        var failures = new List<ValidationResult>();
        var size = GetRotatedSize(def.Size, rotation);

        var ctx = new PlacementContext(def, gridPos, size, rotation, _grid, _terrain, _world);

        if (!PlacementRule.InBounds.Check(ctx))
            failures.Add(ValidationResult.Invalid(PlacementRule.InBounds.FailureMessage));

        foreach (var rule in def.Rules)
        {
            if (!rule.Check(ctx))
                failures.Add(ValidationResult.Invalid(rule.FailureMessage));
        }

        if (!CanAfford(def.Cost))
            failures.Add(ValidationResult.Invalid("Not enough resources"));

        return failures;
    }

    private bool CanAfford(BuildCost cost)
    {
        var wallet = _world.GetResource<CurrencyManager>();
        foreach (var (currencyId, amount) in cost.Resources)
        {
            if (!wallet.CanAfford(currencyId, amount)) return false;
        }
        return true;
    }

    private Point GetRotatedSize(Point size, float rotation) =>
        rotation is 90f or 270f ? new Point(size.Y, size.X) : size;
}
```

---

## 6 — Placement Execution

When the player clicks to place a building and validation passes, the placement executor handles the transaction: deduct resources, create the entity, occupy grid cells, and fire events.

### Building Components

```csharp
/// <summary>
/// Core component for placed buildings.
/// </summary>
public record struct Building(
    string DefId,           // References BuildingDef.Id
    Point GridPosition,     // Where on the grid
    Point GridSize,         // Footprint (accounts for rotation)
    int CurrentHealth,
    int MaxHealth,
    float Rotation,
    int UpgradeLevel        // 0 = base, 1 = first upgrade, etc.
);

/// <summary>
/// Marks a building as under construction.
/// </summary>
public record struct UnderConstruction(
    float TotalTime,        // Total build time in seconds
    float ElapsedTime,      // Time spent so far
    bool Paused             // Construction paused (no workers, etc.)
)
{
    public float Progress => TotalTime > 0 ? MathHelper.Clamp(ElapsedTime / TotalTime, 0f, 1f) : 1f;
    public bool IsComplete => ElapsedTime >= TotalTime;
}

/// <summary>
/// Events fired during the building lifecycle.
/// </summary>
public record struct BuildingPlacedEvent(Entity Entity, string DefId, Point GridPosition);
public record struct BuildingCompletedEvent(Entity Entity, string DefId);
public record struct BuildingUpgradedEvent(Entity Entity, string DefId, int NewLevel);
public record struct BuildingDestroyedEvent(Entity Entity, string DefId, Point GridPosition, Point GridSize);
public record struct BuildModeEnteredEvent(string BuildingId);
public record struct BuildModeExitedEvent;
```

### Placement Executor

```csharp
public class PlacementExecutor
{
    private readonly World _world;
    private readonly GridOccupancy _grid;
    private readonly BuildingRegistry _registry;
    private readonly PlacementValidator _validator;

    public PlacementExecutor(
        World world, GridOccupancy grid,
        BuildingRegistry registry, PlacementValidator validator)
    {
        _world = world;
        _grid = grid;
        _registry = registry;
        _validator = validator;
    }

    /// <summary>
    /// Attempt to place a building. Returns the entity if successful, null if validation fails.
    /// </summary>
    public Entity? TryPlace(string buildingId, Point gridPos, float rotation = 0f)
    {
        var def = _registry.Get(buildingId);
        if (def == null) return null;

        // Final validation (re-check in case state changed since preview)
        var result = _validator.Validate(def, gridPos, rotation);
        if (!result.IsValid) return null;

        // Deduct resources
        var wallet = _world.GetResource<CurrencyManager>();
        foreach (var (currencyId, amount) in def.Cost.Resources)
        {
            wallet.Deduct(currencyId, amount, "building_placement");
        }

        // Determine size after rotation
        var size = rotation is 90f or 270f
            ? new Point(def.Size.Y, def.Size.X)
            : def.Size;

        // Create building entity
        var worldPos = _grid.GridToWorld(gridPos);
        var entity = _world.Create(
            new Building(
                DefId: buildingId,
                GridPosition: gridPos,
                GridSize: size,
                CurrentHealth: def.MaxHealth,
                MaxHealth: def.MaxHealth,
                Rotation: rotation,
                UpgradeLevel: 0
            ),
            new Position(worldPos),
            new Sprite(def.SpriteKey, Color.White)
        );

        // Add construction component if build time > 0
        if (def.BuildTimeSeconds > 0)
        {
            _world.Add(entity, new UnderConstruction(
                TotalTime: def.BuildTimeSeconds,
                ElapsedTime: 0f,
                Paused: false
            ));

            // Use construction sprite until complete
            ref var sprite = ref _world.Get<Sprite>(entity);
            sprite.Color = new Color(200, 200, 200, 180); // Dim until built
        }

        // Mark grid cells as occupied
        _grid.Occupy(gridPos, size, entity);

        // Fire placement event
        _world.Create(new BuildingPlacedEvent(entity, buildingId, gridPos));

        return entity;
    }

    /// <summary>
    /// Remove a building — refund partial resources and free grid cells.
    /// </summary>
    public void Demolish(Entity entity, float refundPercent = 0.5f)
    {
        if (!_world.Has<Building>(entity)) return;

        ref var building = ref _world.Get<Building>(entity);
        var def = _registry.Get(building.DefId);

        // Refund resources
        if (def != null && refundPercent > 0f)
        {
            var wallet = _world.GetResource<CurrencyManager>();
            foreach (var (currencyId, amount) in def.Cost.Resources)
            {
                int refund = (int)(amount * refundPercent);
                if (refund > 0)
                    wallet.Add(currencyId, refund, "building_demolish_refund");
            }
        }

        // Free grid cells
        _grid.Vacate(building.GridPosition, building.GridSize);

        // Fire event before destroying
        _world.Create(new BuildingDestroyedEvent(
            entity, building.DefId, building.GridPosition, building.GridSize
        ));

        _world.Destroy(entity);
    }
}
```

### Input Handling — Click to Place

```csharp
public class PlacementInputSystem : BaseSystem<World, float>
{
    private readonly BuildModeManager _buildMode;
    private readonly PlacementExecutor _executor;
    private readonly GridOccupancy _grid;
    private MouseState _prevMouse;

    public PlacementInputSystem(
        World world, BuildModeManager buildMode,
        PlacementExecutor executor, GridOccupancy grid)
        : base(world)
    {
        _buildMode = buildMode;
        _executor = executor;
        _grid = grid;
    }

    public override void Update(in float deltaTime)
    {
        var mouse = Mouse.GetState();
        var keyboard = Keyboard.GetState();

        if (!_buildMode.IsActive)
        {
            _prevMouse = mouse;
            return;
        }

        // Left-click: attempt placement
        if (mouse.LeftButton == ButtonState.Pressed &&
            _prevMouse.LeftButton == ButtonState.Released)
        {
            var query = new QueryDescription().WithAll<PlacementGhost>();
            World.Query(in query, (ref PlacementGhost ghost) =>
            {
                if (ghost.IsValid)
                {
                    var entity = _executor.TryPlace(
                        ghost.BuildingId, ghost.GridPosition, ghost.Rotation
                    );

                    if (entity != null)
                    {
                        // Play placement sound
                        World.Create(new PlaySoundEvent("sfx/build_place"));

                        // Optionally stay in build mode for rapid placement
                        // (survival games often let you place multiple walls quickly)
                        // _buildMode.ExitBuildMode();
                    }
                }
                else
                {
                    // Invalid placement — play error sound, flash ghost red
                    World.Create(new PlaySoundEvent("sfx/build_invalid"));
                }
            });
        }

        // Right-click or Escape: cancel build mode
        if (mouse.RightButton == ButtonState.Pressed ||
            keyboard.IsKeyDown(Keys.Escape))
        {
            _buildMode.ExitBuildMode();
        }

        // R key: rotate
        if (keyboard.IsKeyDown(Keys.R) && !_prevKeyboard.IsKeyDown(Keys.R))
        {
            _buildMode.RotateGhost();
        }

        _prevMouse = mouse;
    }
}
```

---

## 7 — Free-Form Placement

Not all games use a strict grid. Some need radius-based placement (e.g., placing trees, decorations, campfires) or snap-to-surface (placing on terrain meshes).

### Radius-Based Placement

```csharp
/// <summary>
/// For buildings that don't snap to a grid — checks distance to nearby buildings instead.
/// </summary>
public record struct FreeFormPlacement(
    float MinSpacing,       // Minimum distance from other buildings
    float SnapRadius,       // Snap to nearby snap points (0 = no snapping)
    bool AlignToTerrain     // Rotate to match terrain slope
);

public class FreeFormValidator
{
    private readonly World _world;

    public FreeFormValidator(World world) => _world = world;

    public ValidationResult Validate(BuildingDef def, Vector2 worldPos, float minSpacing)
    {
        // Check minimum spacing from other buildings
        var query = new QueryDescription().WithAll<Building, Position>();
        bool tooClose = false;

        _world.Query(in query, (ref Building b, ref Position p) =>
        {
            float dist = Vector2.Distance(p.Value, worldPos);
            if (dist < minSpacing) tooClose = true;
        });

        if (tooClose)
            return ValidationResult.Invalid($"Too close to another building (min {minSpacing}px)");

        // Check terrain validity at world position
        // (convert to grid for terrain checks if needed)
        return ValidationResult.Valid;
    }
}

/// <summary>
/// Place a free-form building at an arbitrary world position.
/// </summary>
public Entity? PlaceFreeForm(
    string buildingId, Vector2 worldPos, float rotation = 0f)
{
    var def = _registry.Get(buildingId);
    if (def == null) return null;

    // Use free-form validation instead of grid validation
    var freeForm = new FreeFormValidator(_world);
    var result = freeForm.Validate(def, worldPos, minSpacing: 32f);
    if (!result.IsValid) return null;

    // Deduct resources (same as grid placement)
    var wallet = _world.GetResource<CurrencyManager>();
    foreach (var (currencyId, amount) in def.Cost.Resources)
        wallet.Deduct(currencyId, amount, "building_placement");

    // Create entity at exact world position (no grid snapping)
    var entity = _world.Create(
        new Building(
            DefId: buildingId,
            GridPosition: Point.Zero,   // Not grid-aligned
            GridSize: def.Size,
            CurrentHealth: def.MaxHealth,
            MaxHealth: def.MaxHealth,
            Rotation: rotation,
            UpgradeLevel: 0
        ),
        new Position(worldPos),
        new Sprite(def.SpriteKey, Color.White),
        new FreeFormPlacement(MinSpacing: 32f, SnapRadius: 0f, AlignToTerrain: false)
    );

    return entity;
}
```

---

## 8 — Construction & Build Time

Many games don't instant-place buildings. Instead, buildings are placed as scaffolding and take time (or workers) to construct.

### Construction System

```csharp
public class ConstructionSystem : BaseSystem<World, float>
{
    private readonly BuildingRegistry _registry;

    public ConstructionSystem(World world, BuildingRegistry registry) : base(world)
    {
        _registry = registry;
    }

    public override void Update(in float deltaTime)
    {
        var query = new QueryDescription().WithAll<Building, UnderConstruction, Sprite>();

        World.Query(in query, (Entity entity, ref Building building,
            ref UnderConstruction construction, ref Sprite sprite) =>
        {
            if (construction.Paused) return;

            construction.ElapsedTime += deltaTime;

            // Update visual — gradually increase opacity as construction progresses
            float alpha = MathHelper.Lerp(0.3f, 1f, construction.Progress);
            sprite.Color = new Color(255, 255, 255, (int)(alpha * 255));

            if (construction.IsComplete)
            {
                // Remove construction component — building is now operational
                World.Remove<UnderConstruction>(entity);

                // Reset sprite to full opacity
                sprite.Color = Color.White;

                // Fire completion event
                World.Create(new BuildingCompletedEvent(entity, building.DefId));

                // Play completion sound
                World.Create(new PlaySoundEvent("sfx/build_complete"));
            }
        });
    }
}
```

### Worker-Based Construction (Strategy Games)

In RTS games, buildings often require workers and construct faster with more workers assigned:

```csharp
/// <summary>
/// Tracks workers assigned to a construction site.
/// </summary>
public record struct ConstructionSite(
    int AssignedWorkers,
    int MaxWorkers,
    float BaseSpeedPerWorker     // Build-time multiplier per worker
)
{
    public float SpeedMultiplier => AssignedWorkers * BaseSpeedPerWorker;
}

public class WorkerConstructionSystem : BaseSystem<World, float>
{
    public WorkerConstructionSystem(World world) : base(world) { }

    public override void Update(in float deltaTime)
    {
        var query = new QueryDescription()
            .WithAll<Building, UnderConstruction, ConstructionSite>();

        World.Query(in query, (Entity entity, ref Building building,
            ref UnderConstruction construction, ref ConstructionSite site) =>
        {
            if (site.AssignedWorkers == 0)
            {
                construction.Paused = true;
                return;
            }

            construction.Paused = false;
            // More workers = faster construction
            construction.ElapsedTime += deltaTime * site.SpeedMultiplier;

            if (construction.IsComplete)
            {
                // Release workers back to idle pool
                var workerPool = World.GetResource<WorkerPool>();
                workerPool.Release(site.AssignedWorkers);

                World.Remove<UnderConstruction>(entity);
                World.Remove<ConstructionSite>(entity);

                World.Create(new BuildingCompletedEvent(entity, building.DefId));
            }
        });
    }
}
```

### Construction Queue (Build Multiple)

```csharp
/// <summary>
/// Queue of buildings to construct in order.
/// Attach to a player/faction entity.
/// </summary>
public class BuildQueue
{
    private readonly Queue<BuildOrder> _orders = new();
    private readonly int _maxSize;

    public BuildQueue(int maxSize = 10) => _maxSize = maxSize;

    public int Count => _orders.Count;
    public bool IsFull => _orders.Count >= _maxSize;
    public IReadOnlyCollection<BuildOrder> Orders => _orders;

    public bool Enqueue(BuildOrder order)
    {
        if (IsFull) return false;
        _orders.Enqueue(order);
        return true;
    }

    public BuildOrder? Dequeue() =>
        _orders.Count > 0 ? _orders.Dequeue() : null;

    public BuildOrder? Peek() =>
        _orders.Count > 0 ? _orders.Peek() : null;

    public void Cancel(int index)
    {
        var list = _orders.ToList();
        if (index >= 0 && index < list.Count)
        {
            list.RemoveAt(index);
            _orders.Clear();
            foreach (var order in list) _orders.Enqueue(order);
        }
    }
}

public record BuildOrder(
    string BuildingId,
    Point GridPosition,
    float Rotation
);
```

---

## 9 — Building Upgrades

Buildings that improve over time — wooden walls become stone walls, basic turrets gain splash damage, farms become irrigated.

### Upgrade System

```csharp
public class BuildingUpgradeSystem
{
    private readonly World _world;
    private readonly BuildingRegistry _registry;
    private readonly PlacementValidator _validator;

    public BuildingUpgradeSystem(
        World world, BuildingRegistry registry, PlacementValidator validator)
    {
        _world = world;
        _registry = registry;
        _validator = validator;
    }

    /// <summary>
    /// Check if a building can be upgraded.
    /// </summary>
    public UpgradeInfo? GetUpgradeInfo(Entity entity)
    {
        if (!_world.Has<Building>(entity)) return null;

        ref var building = ref _world.Get<Building>(entity);
        var currentDef = _registry.Get(building.DefId);
        if (currentDef?.UpgradesTo == null) return null;

        var nextDef = _registry.Get(currentDef.UpgradesTo);
        if (nextDef == null) return null;

        var wallet = _world.GetResource<CurrencyManager>();
        bool canAfford = nextDef.Cost.Resources.All(
            kv => wallet.CanAfford(kv.Key, kv.Value)
        );

        // Check if building is currently under construction
        bool underConstruction = _world.Has<UnderConstruction>(entity);

        return new UpgradeInfo(
            CurrentDef: currentDef,
            NextDef: nextDef,
            CanAfford: canAfford,
            IsUnderConstruction: underConstruction,
            CanUpgrade: canAfford && !underConstruction
        );
    }

    /// <summary>
    /// Perform the upgrade. Returns false if upgrade fails.
    /// </summary>
    public bool TryUpgrade(Entity entity)
    {
        var info = GetUpgradeInfo(entity);
        if (info == null || !info.CanUpgrade) return false;

        ref var building = ref _world.Get<Building>(entity);

        // Deduct upgrade cost
        var wallet = _world.GetResource<CurrencyManager>();
        foreach (var (currencyId, amount) in info.NextDef.Cost.Resources)
            wallet.Deduct(currencyId, amount, "building_upgrade");

        // Update building definition
        building.DefId = info.NextDef.Id;
        building.MaxHealth = info.NextDef.MaxHealth;
        building.CurrentHealth = info.NextDef.MaxHealth; // Full heal on upgrade
        building.UpgradeLevel++;

        // Update sprite
        ref var sprite = ref _world.Get<Sprite>(entity);
        sprite.TextureKey = info.NextDef.SpriteKey;

        // Add construction time for the upgrade if applicable
        if (info.NextDef.BuildTimeSeconds > 0)
        {
            _world.Add(entity, new UnderConstruction(
                TotalTime: info.NextDef.BuildTimeSeconds * info.NextDef.Cost.BuildTimeMultiplier,
                ElapsedTime: 0f,
                Paused: false
            ));
        }

        // Fire upgrade event
        _world.Create(new BuildingUpgradedEvent(entity, building.DefId, building.UpgradeLevel));
        _world.Create(new PlaySoundEvent("sfx/build_upgrade"));

        return true;
    }
}

public record UpgradeInfo(
    BuildingDef CurrentDef,
    BuildingDef NextDef,
    bool CanAfford,
    bool IsUnderConstruction,
    bool CanUpgrade
);
```

---

## 10 — Building Health & Destruction

Buildings take damage from enemies (survival/TD) or opposing players (strategy). When health reaches zero, the building is destroyed and grid cells are freed.

### Damage System Integration

```csharp
/// <summary>
/// Process damage dealt to buildings.
/// Integrates with the combat system (G64) — buildings are valid damage targets.
/// </summary>
public class BuildingDamageSystem : BaseSystem<World, float>
{
    private readonly PlacementExecutor _executor;

    public BuildingDamageSystem(World world, PlacementExecutor executor) : base(world)
    {
        _executor = executor;
    }

    public override void Update(in float deltaTime)
    {
        // Process damage events targeting buildings
        var query = new QueryDescription().WithAll<DamageEvent>();

        World.Query(in query, (Entity eventEntity, ref DamageEvent dmg) =>
        {
            if (!World.Has<Building>(dmg.Target)) return;

            ref var building = ref World.Get<Building>(dmg.Target);

            // Apply damage (buildings might have armor — see G64)
            int finalDamage = CalculateBuildingDamage(dmg.Amount, building);
            building.CurrentHealth -= finalDamage;

            // Visual feedback — flash red
            if (World.Has<Sprite>(dmg.Target))
            {
                ref var sprite = ref World.Get<Sprite>(dmg.Target);
                World.Add(dmg.Target, new FlashEffect(
                    Color: Color.Red,
                    DurationSeconds: 0.15f,
                    OriginalColor: sprite.Color
                ));
            }

            // Spawn damage number
            if (World.Has<Position>(dmg.Target))
            {
                ref var pos = ref World.Get<Position>(dmg.Target);
                SpawnDamageNumber(pos.Value, finalDamage);
            }

            // Check for destruction
            if (building.CurrentHealth <= 0)
            {
                // Spawn destruction particles
                if (World.Has<Position>(dmg.Target))
                {
                    ref var pos = ref World.Get<Position>(dmg.Target);
                    SpawnDestructionEffect(pos.Value, building.DefId);
                }

                _executor.Demolish(dmg.Target, refundPercent: 0f); // No refund on destruction
            }

            // Consume the damage event
            World.Destroy(eventEntity);
        });
    }

    private int CalculateBuildingDamage(int rawDamage, in Building building)
    {
        var def = World.GetResource<BuildingRegistry>().Get(building.DefId);
        if (def == null) return rawDamage;

        // Buildings may have armor in their properties
        float armor = def.Properties.GetValueOrDefault("armor", 0f);
        return Math.Max(1, rawDamage - (int)armor);
    }

    private void SpawnDamageNumber(Vector2 pos, int damage)
    {
        // See G64 §14 for damage number implementation
        World.Create(new DamageNumber(
            Value: damage,
            Position: pos + new Vector2(0, -16),
            Color: Color.Orange,
            Lifetime: 0.8f
        ));
    }

    private void SpawnDestructionEffect(Vector2 pos, string defId)
    {
        // Spawn particles — rubble, dust, etc.
        World.Create(new ParticleBurst(
            Position: pos,
            Count: 12,
            SpriteKey: "particles/rubble",
            SpeedRange: (20f, 80f),
            LifetimeRange: (0.3f, 0.8f)
        ));

        World.Create(new PlaySoundEvent("sfx/build_destroy"));
    }
}
```

### Repair System

```csharp
/// <summary>
/// Allows players to repair damaged buildings for a fraction of the original cost.
/// </summary>
public class BuildingRepairSystem
{
    private readonly World _world;
    private readonly BuildingRegistry _registry;

    public BuildingRepairSystem(World world, BuildingRegistry registry)
    {
        _world = world;
        _registry = registry;
    }

    public RepairInfo GetRepairInfo(Entity entity)
    {
        if (!_world.Has<Building>(entity))
            return new RepairInfo(false, 0, 0, new());

        ref var building = ref _world.Get<Building>(entity);
        var def = _registry.Get(building.DefId);
        if (def == null)
            return new RepairInfo(false, 0, 0, new());

        int missingHealth = building.MaxHealth - building.CurrentHealth;
        if (missingHealth <= 0)
            return new RepairInfo(false, 0, 0, new());

        float damagePercent = (float)missingHealth / building.MaxHealth;

        // Repair costs scale with damage — 50% of build cost at 100% damage
        var repairCost = new Dictionary<string, int>();
        foreach (var (currencyId, amount) in def.Cost.Resources)
        {
            repairCost[currencyId] = Math.Max(1, (int)(amount * damagePercent * 0.5f));
        }

        var wallet = _world.GetResource<CurrencyManager>();
        bool canAfford = repairCost.All(kv => wallet.CanAfford(kv.Key, kv.Value));

        return new RepairInfo(canAfford, building.CurrentHealth, building.MaxHealth, repairCost);
    }

    public bool TryRepair(Entity entity)
    {
        var info = GetRepairInfo(entity);
        if (!info.CanAfford) return false;

        // Deduct repair cost
        var wallet = _world.GetResource<CurrencyManager>();
        foreach (var (currencyId, amount) in info.Cost)
            wallet.Deduct(currencyId, amount, "building_repair");

        // Restore full health
        ref var building = ref _world.Get<Building>(entity);
        building.CurrentHealth = building.MaxHealth;

        _world.Create(new PlaySoundEvent("sfx/build_repair"));
        return true;
    }
}

public record RepairInfo(
    bool CanAfford,
    int CurrentHealth,
    int MaxHealth,
    Dictionary<string, int> Cost
);
```

---

## 11 — Snapping & Attachment

Walls that auto-connect, pipes that link to adjacent pipes, modular base-building where rooms snap together.

### Wall Auto-Connect

```csharp
/// <summary>
/// Component for buildings that visually connect to adjacent buildings of the same type.
/// Uses a bitmask to select the correct sprite variant.
/// </summary>
public record struct AutoConnect(
    string BaseTextureKey,  // e.g., "buildings/wall_wood"
    byte ConnectionMask     // 4-bit mask: N=1, E=2, S=4, W=8
);

public class AutoConnectSystem : BaseSystem<World, float>
{
    private readonly GridOccupancy _grid;

    private static readonly Point[] Neighbors = {
        new(0, -1),  // North (bit 0)
        new(1, 0),   // East  (bit 1)
        new(0, 1),   // South (bit 2)
        new(-1, 0),  // West  (bit 3)
    };

    public AutoConnectSystem(World world, GridOccupancy grid) : base(world)
    {
        _grid = grid;
    }

    public override void Update(in float deltaTime)
    {
        var query = new QueryDescription().WithAll<Building, AutoConnect, Sprite>();

        World.Query(in query, (Entity entity, ref Building building,
            ref AutoConnect connect, ref Sprite sprite) =>
        {
            byte mask = 0;

            for (int i = 0; i < 4; i++)
            {
                var neighborPos = new Point(
                    building.GridPosition.X + Neighbors[i].X,
                    building.GridPosition.Y + Neighbors[i].Y
                );

                var neighbor = _grid.GetOccupant(neighborPos);
                if (neighbor == null) continue;

                // Check if the neighbor is the same type (or compatible type)
                if (World.Has<Building>(neighbor.Value))
                {
                    ref var neighborBuilding = ref World.Get<Building>(neighbor.Value);
                    if (AreConnectable(building.DefId, neighborBuilding.DefId))
                        mask |= (byte)(1 << i);
                }
            }

            if (mask != connect.ConnectionMask)
            {
                connect.ConnectionMask = mask;
                // Update sprite to the correct variant
                // Convention: "wall_wood_0" through "wall_wood_15" (16 variants)
                sprite.TextureKey = $"{connect.BaseTextureKey}_{mask}";
            }
        });
    }

    private bool AreConnectable(string defIdA, string defIdB)
    {
        // Same type always connects
        if (defIdA == defIdB) return true;

        // Upgraded variants connect (wall_wood connects to wall_stone)
        var registry = World.GetResource<BuildingRegistry>();
        var defA = registry.Get(defIdA);
        var defB = registry.Get(defIdB);

        if (defA == null || defB == null) return false;

        // Buildings with the "snappable" tag connect to each other
        return defA.Tags.Contains("snappable") && defB.Tags.Contains("snappable");
    }
}
```

> **Sprite convention**: Wall auto-tiling uses 16 sprite variants indexed by the 4-bit connection mask. A mask of `0` (no neighbors) is an isolated pillar. `5` (N+S) is a vertical wall. `15` (all four) is a cross/junction. Use the same bitmask approach as tilemap auto-tiling (see [G37 Tilemap Systems](./G37_tilemap_systems.md)).

---

## 12 — Pathfinding Integration

Buildings affect pathfinding. Solid buildings block movement. In tower defense, players must not be allowed to completely block the enemy path.

### Updating the Navigation Grid

```csharp
/// <summary>
/// Listens for building placement/destruction events and updates the pathfinding grid.
/// </summary>
public class BuildingNavUpdateSystem : BaseSystem<World, float>
{
    private readonly Pathfinder _pathfinder;

    public BuildingNavUpdateSystem(World world, Pathfinder pathfinder) : base(world)
    {
        _pathfinder = pathfinder;
    }

    public override void Update(in float deltaTime)
    {
        // Handle placed buildings
        var placedQuery = new QueryDescription().WithAll<BuildingPlacedEvent>();
        World.Query(in placedQuery, (Entity eventEntity, ref BuildingPlacedEvent evt) =>
        {
            var def = World.GetResource<BuildingRegistry>().Get(evt.DefId);
            if (def == null) return;

            // Only block nav if building is solid (not walkable)
            if (!def.Tags.Contains("walkable"))
            {
                var building = World.Get<Building>(evt.Entity);
                for (int x = 0; x < building.GridSize.X; x++)
                for (int y = 0; y < building.GridSize.Y; y++)
                {
                    _pathfinder.SetWalkable(
                        new Point(evt.GridPosition.X + x, evt.GridPosition.Y + y),
                        false
                    );
                }
            }

            World.Destroy(eventEntity); // Consume event
        });

        // Handle destroyed buildings
        var destroyedQuery = new QueryDescription().WithAll<BuildingDestroyedEvent>();
        World.Query(in destroyedQuery, (Entity eventEntity, ref BuildingDestroyedEvent evt) =>
        {
            // Free up the nav cells
            for (int x = 0; x < evt.GridSize.X; x++)
            for (int y = 0; y < evt.GridSize.Y; y++)
            {
                _pathfinder.SetWalkable(
                    new Point(evt.GridPosition.X + x, evt.GridPosition.Y + y),
                    true
                );
            }

            World.Destroy(eventEntity); // Consume event
        });
    }
}
```

### Tower Defense: Path Validation Before Placement

For tower defense games, the `PathBlockRule` (from §5) is essential — but you can also visualize the path during placement:

```csharp
/// <summary>
/// Draws the enemy path during build mode so players can see how their
/// placement affects routing.
/// </summary>
public class PathPreviewSystem : BaseSystem<World, float>
{
    private readonly Pathfinder _pathfinder;
    private readonly GridOccupancy _grid;
    private readonly SpriteBatch _spriteBatch;
    private List<Point>? _previewPath;

    public PathPreviewSystem(World world, Pathfinder pathfinder,
        GridOccupancy grid, SpriteBatch spriteBatch) : base(world)
    {
        _pathfinder = pathfinder;
        _grid = grid;
        _spriteBatch = spriteBatch;
    }

    public override void Update(in float deltaTime)
    {
        var query = new QueryDescription().WithAll<PlacementGhost>();
        World.Query(in query, (ref PlacementGhost ghost) =>
        {
            var def = World.GetResource<BuildingRegistry>().Get(ghost.BuildingId);
            if (def == null || def.Tags.Contains("walkable")) return;

            // Temporarily block the cells
            var size = def.Size; // Account for rotation if needed
            _grid.Occupy(ghost.GridPosition, size, Entity.Null);

            // Recalculate path
            var checkpoints = World.GetResource<PathCheckpoints>();
            _previewPath = _pathfinder.FindPath(checkpoints.Start, checkpoints.End);

            // Undo temporary block
            _grid.Vacate(ghost.GridPosition, size);

            // Draw the preview path
            if (_previewPath != null)
            {
                foreach (var point in _previewPath)
                {
                    var worldPos = _grid.GridToWorldCenter(point);
                    DrawDot(_spriteBatch, worldPos, Color.Yellow * 0.5f, 4f);
                }
            }
        });
    }
}
```

---

## 13 — Save/Load Integration

Building state must be persisted — grid occupancy, building health, construction progress, upgrade levels.

### Serialization

```csharp
/// <summary>
/// Serializable snapshot of all placed buildings.
/// </summary>
public record BuildingSaveData(
    List<BuildingRecord> Buildings
);

public record BuildingRecord(
    string DefId,
    int GridX,
    int GridY,
    float Rotation,
    int CurrentHealth,
    int UpgradeLevel,
    float? ConstructionElapsed,  // null = fully built
    float? ConstructionTotal
);

public class BuildingSaveSystem
{
    private readonly World _world;
    private readonly GridOccupancy _grid;
    private readonly BuildingRegistry _registry;

    public BuildingSaveSystem(World world, GridOccupancy grid, BuildingRegistry registry)
    {
        _world = world;
        _grid = grid;
        _registry = registry;
    }

    /// <summary>
    /// Serialize all buildings to a save-friendly format.
    /// </summary>
    public BuildingSaveData Save()
    {
        var records = new List<BuildingRecord>();

        var query = new QueryDescription().WithAll<Building>();
        _world.Query(in query, (Entity entity, ref Building b) =>
        {
            float? constructElapsed = null;
            float? constructTotal = null;

            if (_world.Has<UnderConstruction>(entity))
            {
                ref var construction = ref _world.Get<UnderConstruction>(entity);
                constructElapsed = construction.ElapsedTime;
                constructTotal = construction.TotalTime;
            }

            records.Add(new BuildingRecord(
                DefId: b.DefId,
                GridX: b.GridPosition.X,
                GridY: b.GridPosition.Y,
                Rotation: b.Rotation,
                CurrentHealth: b.CurrentHealth,
                UpgradeLevel: b.UpgradeLevel,
                ConstructionElapsed: constructElapsed,
                ConstructionTotal: constructTotal
            ));
        });

        return new BuildingSaveData(records);
    }

    /// <summary>
    /// Restore buildings from saved data.
    /// </summary>
    public void Load(BuildingSaveData data)
    {
        // Clear existing buildings
        var query = new QueryDescription().WithAll<Building>();
        var toDestroy = new List<Entity>();
        _world.Query(in query, (Entity entity, ref Building b) =>
        {
            _grid.Vacate(b.GridPosition, b.GridSize);
            toDestroy.Add(entity);
        });
        foreach (var entity in toDestroy) _world.Destroy(entity);

        // Recreate buildings from save data
        foreach (var record in data.Buildings)
        {
            var def = _registry.Get(record.DefId);
            if (def == null) continue;

            var gridPos = new Point(record.GridX, record.GridY);
            var size = record.Rotation is 90f or 270f
                ? new Point(def.Size.Y, def.Size.X)
                : def.Size;

            var worldPos = _grid.GridToWorld(gridPos);

            var entity = _world.Create(
                new Building(
                    DefId: record.DefId,
                    GridPosition: gridPos,
                    GridSize: size,
                    CurrentHealth: record.CurrentHealth,
                    MaxHealth: def.MaxHealth,
                    Rotation: record.Rotation,
                    UpgradeLevel: record.UpgradeLevel
                ),
                new Position(worldPos),
                new Sprite(def.SpriteKey, Color.White)
            );

            // Restore construction state
            if (record.ConstructionElapsed.HasValue)
            {
                _world.Add(entity, new UnderConstruction(
                    TotalTime: record.ConstructionTotal ?? def.BuildTimeSeconds,
                    ElapsedTime: record.ConstructionElapsed.Value,
                    Paused: false
                ));
            }

            // Restore auto-connect if applicable
            if (def.Tags.Contains("snappable"))
            {
                _world.Add(entity, new AutoConnect(
                    BaseTextureKey: def.SpriteKey,
                    ConnectionMask: 0
                ));
            }

            // Occupy grid cells
            _grid.Occupy(gridPos, size, entity);
        }
    }
}
```

---

## 14 — UI Integration

### Build Menu

```csharp
/// <summary>
/// Build menu data for UI rendering.
/// Groups buildings by category, shows costs and availability.
/// </summary>
public class BuildMenuProvider
{
    private readonly BuildingRegistry _registry;
    private readonly World _world;

    public BuildMenuProvider(BuildingRegistry registry, World world)
    {
        _registry = registry;
        _world = world;
    }

    /// <summary>
    /// Get all buildable buildings grouped by category.
    /// Each entry includes affordability status.
    /// </summary>
    public Dictionary<string, List<BuildMenuItem>> GetMenu()
    {
        var menu = new Dictionary<string, List<BuildMenuItem>>();
        var wallet = _world.GetResource<CurrencyManager>();

        foreach (var def in _registry.All)
        {
            if (!menu.ContainsKey(def.Category))
                menu[def.Category] = new List<BuildMenuItem>();

            bool canAfford = def.Cost.Resources.All(
                kv => wallet.CanAfford(kv.Key, kv.Value)
            );

            menu[def.Category].Add(new BuildMenuItem(
                BuildingId: def.Id,
                DisplayName: def.DisplayName,
                Description: def.Description,
                SpriteKey: def.SpriteKey,
                Cost: def.Cost.Resources,
                CanAfford: canAfford,
                BuildTime: def.BuildTimeSeconds
            ));
        }

        return menu;
    }
}

public record BuildMenuItem(
    string BuildingId,
    string DisplayName,
    string Description,
    string SpriteKey,
    Dictionary<string, int> Cost,
    bool CanAfford,
    float BuildTime
);
```

### Building Info Panel

When a player clicks an existing building, show its status:

```csharp
/// <summary>
/// Data for the building info/action panel shown when selecting a placed building.
/// </summary>
public record BuildingInfoPanel(
    string DisplayName,
    string Description,
    int CurrentHealth,
    int MaxHealth,
    int UpgradeLevel,
    float? ConstructionProgress,    // null = fully built
    UpgradeInfo? Upgrade,           // null = max level
    RepairInfo? Repair,             // null = full health
    bool CanDemolish,
    Dictionary<string, int> DemolishRefund
);

public class BuildingInfoProvider
{
    private readonly World _world;
    private readonly BuildingRegistry _registry;
    private readonly BuildingUpgradeSystem _upgradeSystem;
    private readonly BuildingRepairSystem _repairSystem;

    public BuildingInfoPanel? GetInfo(Entity entity)
    {
        if (!_world.Has<Building>(entity)) return null;

        ref var building = ref _world.Get<Building>(entity);
        var def = _registry.Get(building.DefId);
        if (def == null) return null;

        float? constructionProgress = null;
        if (_world.Has<UnderConstruction>(entity))
        {
            ref var construction = ref _world.Get<UnderConstruction>(entity);
            constructionProgress = construction.Progress;
        }

        var upgradeInfo = _upgradeSystem.GetUpgradeInfo(entity);
        var repairInfo = building.CurrentHealth < building.MaxHealth
            ? _repairSystem.GetRepairInfo(entity)
            : null;

        // Calculate demolish refund
        var refund = new Dictionary<string, int>();
        foreach (var (currencyId, amount) in def.Cost.Resources)
            refund[currencyId] = (int)(amount * 0.5f);

        return new BuildingInfoPanel(
            DisplayName: def.DisplayName,
            Description: def.Description,
            CurrentHealth: building.CurrentHealth,
            MaxHealth: building.MaxHealth,
            UpgradeLevel: building.UpgradeLevel,
            ConstructionProgress: constructionProgress,
            Upgrade: upgradeInfo,
            Repair: repairInfo,
            CanDemolish: true,
            DemolishRefund: refund
        );
    }
}
```

### Health Bar Over Buildings

```csharp
/// <summary>
/// Renders health bars above damaged buildings.
/// Only shows when building is damaged (not full health).
/// </summary>
public class BuildingHealthBarSystem : BaseSystem<World, float>
{
    private readonly SpriteBatch _spriteBatch;
    private readonly int _barWidth = 32;
    private readonly int _barHeight = 4;
    private readonly int _yOffset = -8;  // Above the building sprite

    public BuildingHealthBarSystem(World world, SpriteBatch spriteBatch)
        : base(world)
    {
        _spriteBatch = spriteBatch;
    }

    public override void Update(in float deltaTime)
    {
        var query = new QueryDescription().WithAll<Building, Position>();
        var pixel = ContentManager.GetPixelTexture();

        World.Query(in query, (ref Building building, ref Position pos) =>
        {
            if (building.CurrentHealth >= building.MaxHealth) return; // Full health — no bar

            float healthPercent = (float)building.CurrentHealth / building.MaxHealth;
            var barPos = pos.Value + new Vector2(-_barWidth / 2f, _yOffset);

            // Background (dark)
            _spriteBatch.Draw(pixel,
                new Rectangle((int)barPos.X, (int)barPos.Y, _barWidth, _barHeight),
                Color.Black * 0.7f);

            // Health fill
            var fillColor = healthPercent > 0.5f ? Color.Green
                : healthPercent > 0.25f ? Color.Yellow
                : Color.Red;

            _spriteBatch.Draw(pixel,
                new Rectangle((int)barPos.X, (int)barPos.Y,
                    (int)(_barWidth * healthPercent), _barHeight),
                fillColor);
        });
    }
}
```

### Construction Progress Bar

```csharp
/// <summary>
/// Shows a progress bar for buildings under construction.
/// </summary>
public class ConstructionProgressBarSystem : BaseSystem<World, float>
{
    private readonly SpriteBatch _spriteBatch;

    public ConstructionProgressBarSystem(World world, SpriteBatch spriteBatch)
        : base(world)
    {
        _spriteBatch = spriteBatch;
    }

    public override void Update(in float deltaTime)
    {
        var query = new QueryDescription().WithAll<UnderConstruction, Position>();
        var pixel = ContentManager.GetPixelTexture();

        World.Query(in query, (ref UnderConstruction construction, ref Position pos) =>
        {
            var barPos = pos.Value + new Vector2(-16, -12);

            // Background
            _spriteBatch.Draw(pixel,
                new Rectangle((int)barPos.X, (int)barPos.Y, 32, 4),
                Color.Black * 0.7f);

            // Progress fill (blue)
            _spriteBatch.Draw(pixel,
                new Rectangle((int)barPos.X, (int)barPos.Y,
                    (int)(32 * construction.Progress), 4),
                Color.CornflowerBlue);

            // Percentage text
            var font = ContentManager.GetFont("ui_tiny");
            string pct = $"{(int)(construction.Progress * 100)}%";
            _spriteBatch.DrawString(font, pct,
                barPos + new Vector2(0, -10), Color.White);
        });
    }
}
```

---

## 15 — Genre-Specific Patterns

### Survival: Base Building

Survival games emphasize free-form or grid-based base construction with resource gathering:

```csharp
// Typical survival building flow:
// 1. Player gathers wood/stone from the world
// 2. Opens build menu (B key or toolbar)
// 3. Selects structure → enters build mode
// 4. Ghost preview follows cursor, snaps to grid
// 5. Left-click places → deducts resources → starts construction timer
// 6. Buildings connect (walls auto-tile, doors detect adjacent walls)
// 7. Enemies attack buildings at night

// Key survival-specific features:
registry.Register(new BuildingDef(
    Id: "campfire",
    DisplayName: "Campfire",
    Description: "Provides warmth and light. Cook food here.",
    Category: BuildingCategory.Utility,
    SpriteKey: "buildings/campfire",
    GhostSpriteKey: "buildings/campfire_ghost",
    Size: new Point(1, 1),
    Cost: new BuildCost(new() { ["wood"] = 5, ["stone"] = 3 }),
    BuildTimeSeconds: 0f,   // Instant — survival games need fast sheltering
    MaxHealth: 30,
    Rules: new[] { PlacementRule.NotOccupied, PlacementRule.NotOnWater },
    Tags: new[] { "walkable", "light_source", "cooking" },
    UpgradesTo: "furnace",
    Properties: new() { ["light_radius"] = 128f, ["warmth_radius"] = 96f }
));

// Doors: special 1x1 buildings that are walkable and auto-detect wall neighbors
registry.Register(new BuildingDef(
    Id: "door_wood",
    DisplayName: "Wooden Door",
    Description: "Allows passage through walls. Opens for players, blocks enemies.",
    Category: BuildingCategory.Defense,
    SpriteKey: "buildings/door_wood",
    GhostSpriteKey: "buildings/door_wood_ghost",
    Size: new Point(1, 1),
    Cost: new BuildCost(new() { ["wood"] = 8 }),
    BuildTimeSeconds: 1f,
    MaxHealth: 60,
    Rules: new[] {
        PlacementRule.NotOccupied,
        PlacementRule.RequireAdjacent("snappable")  // Must be next to a wall
    },
    Tags: new[] { "walkable", "door", "snappable" },
    UpgradesTo: "door_reinforced",
    Properties: new() { ["open_speed"] = 0.3f }
));
```

### Tower Defense: Tower Placement

Tower defense placement is grid-based with strict path-blocking rules:

```csharp
// TD-specific: towers can only go on designated build tiles, never on the path
registry.Register(new BuildingDef(
    Id: "turret_basic",
    DisplayName: "Basic Turret",
    Description: "Fires at nearest enemy. Short range, fast fire rate.",
    Category: BuildingCategory.Defense,
    SpriteKey: "buildings/turret_basic",
    GhostSpriteKey: "buildings/turret_basic_ghost",
    Size: new Point(1, 1),
    Cost: new BuildCost(new() { ["gold"] = 50 }),
    BuildTimeSeconds: 0f,  // TD usually has instant placement
    MaxHealth: -1,          // Indestructible in most TD games
    Rules: new[] {
        PlacementRule.NotOccupied,
        PlacementRule.OnTerrain("buildable"),  // Only on designated tower spots
        PlacementRule.MustNotBlockPath          // Critical for maze TD
    },
    Tags: new[] { "solid", "tower" },
    UpgradesTo: "turret_rapid",
    Properties: new() { ["damage"] = 10f, ["range"] = 128f, ["fire_rate"] = 2f }
));
```

### Strategy/RTS: Base Construction

RTS building placement involves workers, tech trees, and fog of war:

```csharp
// RTS-specific: buildings require workers, have tech requirements
public record struct TechRequirement(string[] RequiredBuildings);

// Add tech checks to validation
public class TechRequirementRule : PlacementRule
{
    private readonly string[] _requiredBuildings;

    public TechRequirementRule(string[] requiredBuildings) =>
        _requiredBuildings = requiredBuildings;

    public override string Name => "Tech Requirement";
    public override string FailureMessage =>
        $"Requires: {string.Join(", ", _requiredBuildings)}";

    public override bool Check(PlacementContext ctx)
    {
        var query = new QueryDescription().WithAll<Building>();
        var existingTypes = new HashSet<string>();

        ctx.World.Query(in query, (ref Building b) => existingTypes.Add(b.DefId));

        return _requiredBuildings.All(req => existingTypes.Contains(req));
    }
}

// Example: Armory requires Barracks
registry.Register(new BuildingDef(
    Id: "armory",
    DisplayName: "Armory",
    Description: "Researches weapon upgrades. Requires Barracks.",
    Category: BuildingCategory.Production,
    SpriteKey: "buildings/armory",
    GhostSpriteKey: "buildings/armory_ghost",
    Size: new Point(2, 2),
    Cost: new BuildCost(new() { ["gold"] = 300, ["wood"] = 150 }),
    BuildTimeSeconds: 45f,
    MaxHealth: 400,
    Rules: new PlacementRule[] {
        PlacementRule.NotOccupied,
        PlacementRule.NotOnWater,
        new TechRequirementRule(new[] { "barracks" }),
        PlacementRule.MinDistanceFromEnemy(15)
    },
    Tags: new[] { "solid", "powered", "research" },
    UpgradesTo: null,
    Properties: new() { ["research_speed"] = 1f }
));
```

---

## 16 — Tuning Reference

### Survival Building Tuning

| Building | Cost | Build Time | HP | Notes |
|----------|------|------------|-----|-------|
| Campfire | 5 wood, 3 stone | Instant | 30 | Light + warmth source |
| Wooden Wall | 10 wood | 2s | 100 | Basic defense, auto-connects |
| Stone Wall | 20 stone, 5 wood | 4s | 300 | Upgrade from wood |
| Wooden Door | 8 wood | 1s | 60 | Walkable, blocks enemies |
| Chest | 12 wood | 1s | 50 | 20-slot storage |
| Workbench | 15 wood, 5 stone | 3s | 80 | Tier 1 crafting station |
| Farm Plot | 20 wood, 5 stone | 5s | 50 | Produces food |
| Spike Trap | 8 wood, 4 stone | 1s | 25 | Damages enemies on contact |
| Watchtower | 30 wood, 15 stone | 8s | 150 | Extended vision radius |

### Tower Defense Tower Tuning

| Tower | Cost | Damage | Range | Fire Rate | Special |
|-------|------|--------|-------|-----------|---------|
| Basic Turret | 50g | 10 | 128px | 2/s | — |
| Rapid Turret | 100g | 6 | 96px | 5/s | Upgrade of Basic |
| Sniper Tower | 150g | 50 | 256px | 0.5/s | High damage, slow |
| Splash Tower | 120g | 15 | 112px | 1/s | AoE damage |
| Slow Tower | 80g | 0 | 144px | — | 40% slow for 2s |
| Lightning Tower | 200g | 25 | 160px | 1.5/s | Chains to 3 targets |

### RTS Building Tuning

| Building | Cost | Build Time | HP | Requires | Produces |
|----------|------|------------|-----|----------|----------|
| Town Center | Free (start) | — | 2000 | — | Workers |
| Barracks | 200g, 100w | 30s | 500 | Town Center | Infantry |
| Armory | 300g, 150w | 45s | 400 | Barracks | Upgrades |
| Farm | 100g | 20s | 200 | Town Center | Food income |
| Tower | 150g, 100s | 25s | 600 | Barracks | Defense |
| Castle | 800g, 400s | 90s | 3000 | Armory | Elite units |

### Placement Feel Tuning

| Parameter | Recommended | Notes |
|-----------|-------------|-------|
| Ghost opacity (valid) | 50% green tint | Visible but clearly a preview |
| Ghost opacity (invalid) | 50% red tint | Immediately communicates "no" |
| Snap threshold | 0.5× tile size | Snaps when cursor is within half a tile of center |
| Rotation speed | Instant (90° steps) | No animation needed — just swap |
| Invalid placement sound | Short "buzz" or "thunk" | Don't annoy — play once per click, not continuously |
| Valid placement sound | Satisfying "click" or "thud" | Reinforce good placement |
| Construction visual | Fade from 30% → 100% opacity | Simple and readable |
| Demolish refund | 50% of build cost | Standard across genres |
| Repair cost | 25-50% of build cost at full damage | Scales with damage |

---

## Quick Reference — System Wiring

```
┌─────────────────┐     ┌──────────────┐     ┌───────────────────┐
│  Build Menu UI  │────▶│  BuildMode   │────▶│  PlacementGhost   │
│  (§14)          │     │  Manager     │     │  System (§4)      │
└─────────────────┘     └──────────────┘     └───────────────────┘
                                                       │
                                                       ▼
                                              ┌───────────────────┐
                                              │  Placement        │
                                              │  Validator (§5)   │
                                              └───────────────────┘
                                                       │
                                                  Valid? ──▶ Yes
                                                       │
                                                       ▼
┌─────────────────┐     ┌──────────────┐     ┌───────────────────┐
│  Economy (G65)  │◀────│  Placement   │────▶│  GridOccupancy    │
│  Deduct cost    │     │  Executor(§6)│     │  (§3)             │
└─────────────────┘     └──────────────┘     └───────────────────┘
                               │                       │
                               ▼                       ▼
                        ┌──────────────┐     ┌───────────────────┐
                        │ Construction │     │  Pathfinding      │
                        │ System (§8)  │     │  Update (§12)     │
                        └──────────────┘     └───────────────────┘
                               │
                          Completed?
                               │
                               ▼
                        ┌──────────────┐     ┌───────────────────┐
                        │  Auto-       │     │  Building Health   │
                        │  Connect(§11)│     │  & Damage (§10)   │
                        └──────────────┘     └───────────────────┘
```
