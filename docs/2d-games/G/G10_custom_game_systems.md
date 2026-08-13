# G10 — Custom Game Systems
> **Category:** Guide · **Related:** [G1 Custom Code Recipes](./G1_custom_code_recipes.md) · [R2 Capability Matrix](../R/R2_capability_matrix.md) · [C1 Genre Reference](../C/C1_genre_reference.md)

> Comprehensive implementation guide for 10 essential game systems built with MonoGame + Arch ECS. These are systems no library provides well enough — you'll write them as reusable modules in your Core project. Each is genre-agnostic and composable. Store them in `src/Systems/` per the [project structure](../R/R3_project_structure.md).

---

## 1. Inventory System

Slot-based inventory with item stacking, categories, equip slots, and full ECS integration.

### Components

```csharp
// --- Item Definition (data, not ECS — lives in a registry) ---
public record ItemDef(
    string Id,
    string Name,
    string Description,
    ItemCategory Category,
    ItemRarity Rarity,
    int MaxStack,
    EquipSlot EquipSlot,        // None if not equippable
    Dictionary<string, float> Stats  // "damage": 10, "armor": 5, etc.
);

public enum ItemCategory { Weapon, Armor, Consumable, Material, Quest, Misc }
public enum ItemRarity { Common, Uncommon, Rare, Epic, Legendary }
public enum EquipSlot { None, Head, Chest, Legs, Feet, MainHand, OffHand, Ring, Amulet }

// --- ECS Components ---
public record struct InventorySlot(string ItemId, int Count);

public record struct Inventory(InventorySlot[] Slots, int Size)
{
    public Inventory(int size) : this(new InventorySlot[size], size)
    {
        for (int i = 0; i < size; i++)
            Slots[i] = new InventorySlot("", 0);
    }
}

public record struct Equipment(Dictionary<EquipSlot, string> Equipped)
{
    public Equipment() : this(new Dictionary<EquipSlot, string>()) { }
}

public record struct ItemPickupRequest(string ItemId, int Count);
public record struct ItemDropRequest(int SlotIndex, int Count);
```

### Item Registry

```csharp
public class ItemRegistry
{
    private readonly Dictionary<string, ItemDef> _items = new();

    public void Register(ItemDef def) => _items[def.Id] = def;
    public ItemDef Get(string id) => _items[id];
    public bool Exists(string id) => _items.ContainsKey(id);

    public void LoadFromJson(string json)
    {
        var defs = JsonSerializer.Deserialize<List<ItemDef>>(json);
        foreach (var def in defs!) Register(def);
    }
}
```

### Inventory Logic

```csharp
public static class InventoryOps
{
    /// <summary>Attempts to add items. Returns leftover count that didn't fit.</summary>
    public static int TryAdd(ref Inventory inv, string itemId, int count, ItemRegistry registry)
    {
        var def = registry.Get(itemId);
        int remaining = count;

        // First pass: fill existing stacks
        for (int i = 0; i < inv.Size && remaining > 0; i++)
        {
            ref var slot = ref inv.Slots[i];
            if (slot.ItemId == itemId && slot.Count < def.MaxStack)
            {
                int space = def.MaxStack - slot.Count;
                int add = Math.Min(space, remaining);
                slot = slot with { Count = slot.Count + add };
                remaining -= add;
            }
        }

        // Second pass: use empty slots
        for (int i = 0; i < inv.Size && remaining > 0; i++)
        {
            ref var slot = ref inv.Slots[i];
            if (slot.Count == 0)
            {
                int add = Math.Min(def.MaxStack, remaining);
                slot = new InventorySlot(itemId, add);
                remaining -= add;
            }
        }

        return remaining;
    }

    /// <summary>Removes count items. Returns actual amount removed.</summary>
    public static int Remove(ref Inventory inv, string itemId, int count)
    {
        int toRemove = count;
        for (int i = inv.Size - 1; i >= 0 && toRemove > 0; i--)
        {
            ref var slot = ref inv.Slots[i];
            if (slot.ItemId == itemId)
            {
                int take = Math.Min(slot.Count, toRemove);
                slot = slot with { Count = slot.Count - take };
                if (slot.Count == 0) slot = new InventorySlot("", 0);
                toRemove -= take;
            }
        }
        return count - toRemove;
    }

    /// <summary>Total count of a specific item across all slots.</summary>
    public static int CountItem(in Inventory inv, string itemId)
    {
        int total = 0;
        for (int i = 0; i < inv.Size; i++)
            if (inv.Slots[i].ItemId == itemId) total += inv.Slots[i].Count;
        return total;
    }

    /// <summary>Swap two slots (for drag-and-drop UI).</summary>
    public static void Swap(ref Inventory inv, int slotA, int slotB)
    {
        (inv.Slots[slotA], inv.Slots[slotB]) = (inv.Slots[slotB], inv.Slots[slotA]);
    }

    /// <summary>Equip item from inventory slot.</summary>
    public static bool TryEquip(ref Inventory inv, ref Equipment equip, int slotIndex, ItemRegistry registry)
    {
        ref var slot = ref inv.Slots[slotIndex];
        if (slot.Count == 0) return false;

        var def = registry.Get(slot.ItemId);
        if (def.EquipSlot == EquipSlot.None) return false;

        // Unequip current item in that slot (swap back to inventory)
        if (equip.Equipped.TryGetValue(def.EquipSlot, out var oldItemId))
        {
            equip.Equipped.Remove(def.EquipSlot);
            // Put old item where the new one was
            slot = new InventorySlot(oldItemId, 1);
        }
        else
        {
            slot = slot with { Count = slot.Count - 1 };
            if (slot.Count == 0) slot = new InventorySlot("", 0);
        }

        equip.Equipped[def.EquipSlot] = def.Id;
        return true;
    }
}
```

### Inventory ECS System

```csharp
public partial class InventoryPickupSystem : BaseSystem<World, float>
{
    private readonly ItemRegistry _registry;
    private readonly CommandBuffer _buffer;

    public InventoryPickupSystem(World world, ItemRegistry registry) : base(world)
    {
        _registry = registry;
        _buffer = new CommandBuffer(world);
    }

    [Query]
    [All<Inventory, ItemPickupRequest>]
    public void ProcessPickup(Entity entity, ref Inventory inv, ref ItemPickupRequest req)
    {
        int leftover = InventoryOps.TryAdd(ref inv, req.ItemId, req.Count, _registry);
        _buffer.Remove<ItemPickupRequest>(entity);

        if (leftover > 0)
        {
            // Inventory full — could spawn a dropped-item entity or notify UI
        }
    }

    public override void AfterUpdate(in float dt)
    {
        _buffer.Playback(World);
    }
}
```

---

## 2. Dialogue System

Node-based dialogue trees with conditional branching, variable tracking, typewriter rendering, and speaker portraits.

### Data Model

```csharp
// --- Dialogue Data (loaded from JSON, not ECS components) ---
public class DialogueNode
{
    public string Id { get; init; } = "";
    public string Speaker { get; init; } = "";
    public string Text { get; init; } = "";
    public string? Portrait { get; init; }
    public List<DialogueChoice> Choices { get; init; } = new();
    public List<DialogueEffect> Effects { get; init; } = new();
    public string? NextNodeId { get; init; }  // auto-advance if no choices
}

public class DialogueChoice
{
    public string Text { get; init; } = "";
    public string NextNodeId { get; init; } = "";
    public DialogueCondition? Condition { get; init; }
}

public class DialogueCondition
{
    public string Variable { get; init; } = "";
    public string Op { get; init; } = "=="; // ==, !=, >=, <=, >, <
    public int Value { get; init; }
}

public class DialogueEffect
{
    public string Type { get; init; } = "";   // "set_var", "add_item", "start_quest"
    public string Key { get; init; } = "";
    public int Value { get; init; }
}

public class DialogueTree
{
    public string Id { get; init; } = "";
    public string StartNodeId { get; init; } = "";
    public Dictionary<string, DialogueNode> Nodes { get; init; } = new();
}
```

### Variable Store

```csharp
public class DialogueVariables
{
    private readonly Dictionary<string, int> _vars = new();

    public int Get(string key) => _vars.GetValueOrDefault(key, 0);
    public void Set(string key, int value) => _vars[key] = value;
    public void Add(string key, int amount) => _vars[key] = Get(key) + amount;

    public bool Evaluate(DialogueCondition cond) => cond.Op switch
    {
        "==" => Get(cond.Variable) == cond.Value,
        "!=" => Get(cond.Variable) != cond.Value,
        ">=" => Get(cond.Variable) >= cond.Value,
        "<=" => Get(cond.Variable) <= cond.Value,
        ">"  => Get(cond.Variable) > cond.Value,
        "<"  => Get(cond.Variable) < cond.Value,
        _    => false
    };

    public Dictionary<string, int> Snapshot() => new(_vars);
    public void Restore(Dictionary<string, int> data) { _vars.Clear(); foreach (var kv in data) _vars[kv.Key] = kv.Value; }
}
```

### Dialogue Manager

```csharp
public class DialogueManager
{
    private readonly Dictionary<string, DialogueTree> _trees = new();
    private readonly DialogueVariables _variables;
    private DialogueTree? _activeTree;
    private DialogueNode? _activeNode;

    // Typewriter state
    private string _displayedText = "";
    private float _charTimer;
    private int _charIndex;
    private float _charsPerSecond = 30f;

    public bool IsActive => _activeTree != null;
    public string DisplayedText => _displayedText;
    public DialogueNode? CurrentNode => _activeNode;
    public bool IsTypewriterComplete => _activeNode != null && _charIndex >= _activeNode.Text.Length;

    public event Action<DialogueNode>? OnNodeEntered;
    public event Action<List<DialogueEffect>>? OnEffectsTriggered;
    public event Action? OnDialogueEnded;

    public DialogueManager(DialogueVariables variables) => _variables = variables;

    public void RegisterTree(DialogueTree tree) => _trees[tree.Id] = tree;

    public void LoadTreesFromJson(string json)
    {
        var trees = JsonSerializer.Deserialize<List<DialogueTree>>(json);
        foreach (var t in trees!) RegisterTree(t);
    }

    public void StartDialogue(string treeId)
    {
        _activeTree = _trees[treeId];
        EnterNode(_activeTree.StartNodeId);
    }

    public void AdvanceOrComplete()
    {
        if (_activeNode == null) return;

        // If typewriter is still going, complete it instantly
        if (!IsTypewriterComplete)
        {
            _displayedText = _activeNode.Text;
            _charIndex = _activeNode.Text.Length;
            return;
        }

        // Auto-advance if no choices
        if (_activeNode.Choices.Count == 0 && _activeNode.NextNodeId != null)
            EnterNode(_activeNode.NextNodeId);
        else if (_activeNode.Choices.Count == 0)
            EndDialogue();
    }

    public void SelectChoice(int choiceIndex)
    {
        if (_activeNode == null) return;
        var available = GetAvailableChoices();
        if (choiceIndex < 0 || choiceIndex >= available.Count) return;
        EnterNode(available[choiceIndex].NextNodeId);
    }

    public List<DialogueChoice> GetAvailableChoices()
    {
        if (_activeNode == null) return new();
        return _activeNode.Choices
            .Where(c => c.Condition == null || _variables.Evaluate(c.Condition))
            .ToList();
    }

    public void Update(float dt)
    {
        if (_activeNode == null || IsTypewriterComplete) return;
        _charTimer += dt;
        float interval = 1f / _charsPerSecond;
        while (_charTimer >= interval && _charIndex < _activeNode.Text.Length)
        {
            _charTimer -= interval;
            _charIndex++;
            _displayedText = _activeNode.Text[.._charIndex];
        }
    }

    private void EnterNode(string nodeId)
    {
        if (_activeTree == null || !_activeTree.Nodes.TryGetValue(nodeId, out var node))
        {
            EndDialogue();
            return;
        }
        _activeNode = node;
        _displayedText = "";
        _charIndex = 0;
        _charTimer = 0;

        // Apply effects
        if (node.Effects.Count > 0)
            OnEffectsTriggered?.Invoke(node.Effects);

        OnNodeEntered?.Invoke(node);
    }

    private void EndDialogue()
    {
        _activeTree = null;
        _activeNode = null;
        _displayedText = "";
        OnDialogueEnded?.Invoke();
    }
}
```

### ECS Integration

```csharp
// --- Components ---
public record struct DialogueTrigger(string TreeId, float InteractRange);
public record struct InDialogue(string TreeId);
public struct PlayerTag;  // zero-size tag

// --- System ---
public partial class DialogueInteractionSystem : BaseSystem<World, float>
{
    private readonly DialogueManager _dialogueManager;
    private readonly CommandBuffer _buffer;

    public DialogueInteractionSystem(World world, DialogueManager mgr) : base(world)
    {
        _dialogueManager = mgr;
        _buffer = new CommandBuffer(world);
    }

    [Query]
    [All<PlayerTag, Position>]
    public void CheckInteraction(Entity player, ref Position playerPos)
    {
        if (_dialogueManager.IsActive) return;

        // Check if interact key pressed (abstracted)
        if (!InputHelper.JustPressed(Keys.E)) return;

        var query = new QueryDescription().WithAll<DialogueTrigger, Position>();
        World.Query(in query, (Entity npc, ref DialogueTrigger trigger, ref Position npcPos) =>
        {
            float dist = Vector2.Distance(playerPos.Value, npcPos.Value);
            if (dist <= trigger.InteractRange)
            {
                _dialogueManager.StartDialogue(trigger.TreeId);
                _buffer.Add(player, new InDialogue(trigger.TreeId));
            }
        });
    }

    public override void AfterUpdate(in float dt)
    {
        _buffer.Playback(World);
    }
}
```

### Example Dialogue JSON

```json
{
  "Id": "blacksmith_intro",
  "StartNodeId": "greeting",
  "Nodes": {
    "greeting": {
      "Id": "greeting",
      "Speaker": "Blacksmith",
      "Text": "Welcome, traveler. Looking for a blade?",
      "Portrait": "blacksmith_neutral",
      "Choices": [
        { "Text": "What do you have?", "NextNodeId": "shop_intro" },
        { "Text": "I need repairs.", "NextNodeId": "repair_intro",
          "Condition": { "Variable": "has_broken_sword", "Op": ">=", "Value": 1 } },
        { "Text": "Goodbye.", "NextNodeId": "farewell" }
      ]
    }
  }
}
```

---

## 3. Save/Load System

Serializes ECS world state, player progress, and game variables to versioned JSON save files.

### Components & Interfaces

```csharp
// --- Save Metadata ---
public record struct SaveMetadata(
    string SaveName,
    int Version,
    DateTime Timestamp,
    TimeSpan PlayTime,
    string SceneName
);

// --- Marker: entities with this get serialized ---
public struct Persistent;  // zero-size tag

// --- Serializable snapshot of an entity ---
public class EntitySnapshot
{
    public Dictionary<string, JsonElement> Components { get; init; } = new();
}

public class SaveFile
{
    public SaveMetadata Metadata { get; init; }
    public List<EntitySnapshot> Entities { get; init; } = new();
    public Dictionary<string, int> DialogueVariables { get; init; } = new();
    public Dictionary<string, QuestSaveData> Quests { get; init; } = new();
    public Dictionary<string, object> CustomData { get; init; } = new();
}
```

### Save Serializer

```csharp
public class SaveSerializer
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private const int CurrentVersion = 3;

    public static string Serialize(SaveFile save) =>
        JsonSerializer.Serialize(save, JsonOpts);

    public static SaveFile Deserialize(string json)
    {
        var save = JsonSerializer.Deserialize<SaveFile>(json, JsonOpts)!;
        save = MigrateIfNeeded(save);
        return save;
    }

    private static SaveFile MigrateIfNeeded(SaveFile save)
    {
        var version = save.Metadata.Version;

        if (version < 2)
        {
            // v1→v2: added quest data — initialize empty if missing
            // (already defaults to empty dict via init)
        }
        if (version < 3)
        {
            // v2→v3: renamed "hp" component to "health"
            foreach (var entity in save.Entities)
            {
                if (entity.Components.Remove("hp", out var hpData))
                    entity.Components["health"] = hpData;
            }
        }

        return save with { Metadata = save.Metadata with { Version = CurrentVersion } };
    }
}
```

### Save Manager

```csharp
public class SaveManager
{
    private readonly string _saveDirectory;

    public SaveManager(string saveDir)
    {
        _saveDirectory = saveDir;
        Directory.CreateDirectory(saveDir);
    }

    public string GetSlotPath(int slot) =>
        Path.Combine(_saveDirectory, $"save_slot_{slot}.json");

    public SaveFile BuildSaveFile(World world, string saveName, string sceneName,
        TimeSpan playTime, DialogueVariables dialogueVars, QuestManager questMgr)
    {
        var entities = new List<EntitySnapshot>();
        var query = new QueryDescription().WithAll<Persistent>();

        World.Query(in query, (Entity entity) =>
        {
            var snapshot = new EntitySnapshot();
            // Serialize each component type the entity has
            SerializeIfPresent<Position>(world, entity, snapshot, "position");
            SerializeIfPresent<Health>(world, entity, snapshot, "health");
            SerializeIfPresent<Inventory>(world, entity, snapshot, "inventory");
            SerializeIfPresent<Equipment>(world, entity, snapshot, "equipment");
            SerializeIfPresent<AiState>(world, entity, snapshot, "aiState");
            SerializeIfPresent<QuestLog>(world, entity, snapshot, "questLog");
            SerializeIfPresent<StatusEffects>(world, entity, snapshot, "statusEffects");
            entities.Add(snapshot);
        });

        return new SaveFile
        {
            Metadata = new SaveMetadata(saveName, 3, DateTime.UtcNow, playTime, sceneName),
            Entities = entities,
            DialogueVariables = dialogueVars.Snapshot(),
            Quests = questMgr.Snapshot()
        };
    }

    private static void SerializeIfPresent<T>(World world, Entity entity,
        EntitySnapshot snapshot, string key) where T : struct
    {
        if (world.Has<T>(entity))
        {
            var comp = world.Get<T>(entity);
            snapshot.Components[key] = JsonSerializer.SerializeToElement(comp);
        }
    }

    public void Save(SaveFile file, int slot)
    {
        var json = SaveSerializer.Serialize(file);
        File.WriteAllText(GetSlotPath(slot), json);
    }

    public SaveFile? Load(int slot)
    {
        var path = GetSlotPath(slot);
        if (!File.Exists(path)) return null;
        var json = File.ReadAllText(path);
        return SaveSerializer.Deserialize(json);
    }

    public void Delete(int slot)
    {
        var path = GetSlotPath(slot);
        if (File.Exists(path)) File.Delete(path);
    }

    public SaveMetadata? PeekMetadata(int slot)
    {
        var save = Load(slot);
        return save?.Metadata;
    }

    public List<(int Slot, SaveMetadata Meta)> ListSaves(int maxSlots = 10)
    {
        var results = new List<(int, SaveMetadata)>();
        for (int i = 0; i < maxSlots; i++)
        {
            var meta = PeekMetadata(i);
            if (meta.HasValue) results.Add((i, meta.Value));
        }
        return results;
    }
}
```

### World Restoration

```csharp
public static class WorldRestorer
{
    public static void RestoreEntities(World world, SaveFile save)
    {
        // Clear existing persistent entities
        var query = new QueryDescription().WithAll<Persistent>();
        var toDestroy = new List<Entity>();
        world.Query(in query, (Entity e) => toDestroy.Add(e));
        foreach (var e in toDestroy) world.Destroy(e);

        // Recreate from snapshots
        foreach (var snapshot in save.Entities)
        {
            var entity = world.Create(new Persistent());

            DeserializeIfPresent<Position>(world, entity, snapshot, "position");
            DeserializeIfPresent<Health>(world, entity, snapshot, "health");
            DeserializeIfPresent<Inventory>(world, entity, snapshot, "inventory");
            DeserializeIfPresent<Equipment>(world, entity, snapshot, "equipment");
            DeserializeIfPresent<AiState>(world, entity, snapshot, "aiState");
            DeserializeIfPresent<QuestLog>(world, entity, snapshot, "questLog");
            DeserializeIfPresent<StatusEffects>(world, entity, snapshot, "statusEffects");
        }
    }

    private static void DeserializeIfPresent<T>(World world, Entity entity,
        EntitySnapshot snapshot, string key) where T : struct
    {
        if (snapshot.Components.TryGetValue(key, out var element))
        {
            var comp = element.Deserialize<T>();
            world.Add(entity, comp);
        }
    }
}
```

---

## 4. Procedural Generation Suite

All pure C# algorithms using seed-based RNG for reproducibility. Each outputs a `bool[,]` or `int[,]` grid suitable for tilemap population.

### Shared Types

```csharp
public record struct RoomRect(int X, int Y, int Width, int Height)
{
    public int CenterX => X + Width / 2;
    public int CenterY => Y + Height / 2;
    public bool Intersects(RoomRect other) =>
        X < other.X + other.Width && X + Width > other.X &&
        Y < other.Y + other.Height && Y + Height > other.Y;
}

public enum TileType { Wall = 0, Floor = 1, Corridor = 2, Door = 3 }
```

### 4a. BSP Dungeon Generation

Binary Space Partitioning for room-and-corridor dungeons (roguelikes).

```csharp
public class BspDungeon
{
    private readonly int _width, _height;
    private readonly int _minRoomSize;
    private readonly Random _rng;
    public int[,] Grid { get; }
    public List<RoomRect> Rooms { get; } = new();

    public BspDungeon(int width, int height, int minRoomSize = 6, int seed = 0)
    {
        _width = width;
        _height = height;
        _minRoomSize = minRoomSize;
        _rng = new Random(seed);
        Grid = new int[width, height]; // all walls (0)
    }

    public void Generate(int maxDepth = 5)
    {
        var root = new BspNode(0, 0, _width, _height);
        Split(root, 0, maxDepth);
        CreateRooms(root);
        ConnectRooms(root);
    }

    private void Split(BspNode node, int depth, int maxDepth)
    {
        if (depth >= maxDepth) return;
        if (node.Width < _minRoomSize * 2 && node.Height < _minRoomSize * 2) return;

        bool splitH = node.Width < node.Height
            ? true
            : node.Height < node.Width ? false : _rng.Next(2) == 0;

        if (splitH && node.Height < _minRoomSize * 2) splitH = false;
        if (!splitH && node.Width < _minRoomSize * 2) splitH = true;

        if (splitH)
        {
            int split = _rng.Next(_minRoomSize, node.Height - _minRoomSize);
            node.Left = new BspNode(node.X, node.Y, node.Width, split);
            node.Right = new BspNode(node.X, node.Y + split, node.Width, node.Height - split);
        }
        else
        {
            int split = _rng.Next(_minRoomSize, node.Width - _minRoomSize);
            node.Left = new BspNode(node.X, node.Y, split, node.Height);
            node.Right = new BspNode(node.X + split, node.Y, node.Width - split, node.Height);
        }

        Split(node.Left, depth + 1, maxDepth);
        Split(node.Right, depth + 1, maxDepth);
    }

    private void CreateRooms(BspNode node)
    {
        if (node.Left != null && node.Right != null)
        {
            CreateRooms(node.Left);
            CreateRooms(node.Right);
            return;
        }

        // Leaf — carve a room
        int roomW = _rng.Next(_minRoomSize - 2, node.Width - 2);
        int roomH = _rng.Next(_minRoomSize - 2, node.Height - 2);
        int roomX = node.X + _rng.Next(1, node.Width - roomW - 1);
        int roomY = node.Y + _rng.Next(1, node.Height - roomH - 1);

        var room = new RoomRect(roomX, roomY, roomW, roomH);
        node.Room = room;
        Rooms.Add(room);

        for (int x = room.X; x < room.X + room.Width; x++)
        for (int y = room.Y; y < room.Y + room.Height; y++)
            Grid[x, y] = (int)TileType.Floor;
    }

    private void ConnectRooms(BspNode node)
    {
        if (node.Left == null || node.Right == null) return;
        ConnectRooms(node.Left);
        ConnectRooms(node.Right);

        var roomA = GetRoom(node.Left);
        var roomB = GetRoom(node.Right);
        if (roomA == null || roomB == null) return;

        CarveCorridor(roomA.Value.CenterX, roomA.Value.CenterY,
                       roomB.Value.CenterX, roomB.Value.CenterY);
    }

    private void CarveCorridor(int x1, int y1, int x2, int y2)
    {
        // L-shaped corridor
        if (_rng.Next(2) == 0)
        {
            CarveHLine(x1, x2, y1);
            CarveVLine(y1, y2, x2);
        }
        else
        {
            CarveVLine(y1, y2, x1);
            CarveHLine(x1, x2, y2);
        }
    }

    private void CarveHLine(int x1, int x2, int y)
    {
        for (int x = Math.Min(x1, x2); x <= Math.Max(x1, x2); x++)
            if (x >= 0 && x < _width && y >= 0 && y < _height)
                Grid[x, y] = (int)TileType.Corridor;
    }

    private void CarveVLine(int y1, int y2, int x)
    {
        for (int y = Math.Min(y1, y2); y <= Math.Max(y1, y2); y++)
            if (x >= 0 && x < _width && y >= 0 && y < _height)
                Grid[x, y] = (int)TileType.Corridor;
    }

    private RoomRect? GetRoom(BspNode node)
    {
        if (node.Room.HasValue) return node.Room;
        if (node.Left != null) { var r = GetRoom(node.Left); if (r.HasValue) return r; }
        if (node.Right != null) return GetRoom(node.Right);
        return null;
    }

    private class BspNode
    {
        public int X, Y, Width, Height;
        public BspNode? Left, Right;
        public RoomRect? Room;
        public BspNode(int x, int y, int w, int h) { X = x; Y = y; Width = w; Height = h; }
    }
}
```

### 4b. Cellular Automata (Cave Generation)

```csharp
public class CellularAutomataCaves
{
    private readonly int _width, _height;
    private readonly Random _rng;
    public bool[,] Map { get; private set; }

    public CellularAutomataCaves(int width, int height, int seed = 0)
    {
        _width = width;
        _height = height;
        _rng = new Random(seed);
        Map = new bool[width, height]; // false = wall, true = floor
    }

    /// <param name="fillPercent">Chance each cell starts as floor (0.0-1.0)</param>
    /// <param name="iterations">Smoothing passes</param>
    /// <param name="birthThreshold">Neighbors needed to become floor</param>
    /// <param name="deathThreshold">Neighbors needed to stay floor</param>
    public void Generate(float fillPercent = 0.45f, int iterations = 5,
        int birthThreshold = 5, int deathThreshold = 4)
    {
        // Seed the map
        for (int x = 0; x < _width; x++)
        for (int y = 0; y < _height; y++)
        {
            // Borders always wall
            if (x == 0 || y == 0 || x == _width - 1 || y == _height - 1)
                Map[x, y] = false;
            else
                Map[x, y] = _rng.NextSingle() < fillPercent;
        }

        // Smoothing iterations
        for (int i = 0; i < iterations; i++)
            Smooth(birthThreshold, deathThreshold);
    }

    private void Smooth(int birth, int death)
    {
        var next = new bool[_width, _height];
        for (int x = 1; x < _width - 1; x++)
        for (int y = 1; y < _height - 1; y++)
        {
            int neighbors = CountFloorNeighbors(x, y);
            next[x, y] = Map[x, y]
                ? neighbors >= death   // survive
                : neighbors >= birth;  // born
        }
        Map = next;
    }

    private int CountFloorNeighbors(int cx, int cy)
    {
        int count = 0;
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
        {
            if (dx == 0 && dy == 0) continue;
            int nx = cx + dx, ny = cy + dy;
            if (nx >= 0 && ny >= 0 && nx < _width && ny < _height && Map[nx, ny])
                count++;
        }
        return count;
    }

    /// <summary>Flood fill to find and optionally cull disconnected regions.</summary>
    public List<List<Point>> FindRegions(bool targetValue = true)
    {
        var visited = new bool[_width, _height];
        var regions = new List<List<Point>>();

        for (int x = 0; x < _width; x++)
        for (int y = 0; y < _height; y++)
        {
            if (visited[x, y] || Map[x, y] != targetValue) continue;
            var region = FloodFill(x, y, targetValue, visited);
            regions.Add(region);
        }
        return regions;
    }

    private List<Point> FloodFill(int sx, int sy, bool target, bool[,] visited)
    {
        var points = new List<Point>();
        var stack = new Stack<Point>();
        stack.Push(new Point(sx, sy));

        while (stack.Count > 0)
        {
            var p = stack.Pop();
            if (p.X < 0 || p.Y < 0 || p.X >= _width || p.Y >= _height) continue;
            if (visited[p.X, p.Y] || Map[p.X, p.Y] != target) continue;
            visited[p.X, p.Y] = true;
            points.Add(p);
            stack.Push(new Point(p.X + 1, p.Y));
            stack.Push(new Point(p.X - 1, p.Y));
            stack.Push(new Point(p.X, p.Y + 1));
            stack.Push(new Point(p.X, p.Y - 1));
        }
        return points;
    }

    /// <summary>Remove all floor regions smaller than minSize.</summary>
    public void CullSmallRegions(int minSize = 16)
    {
        var regions = FindRegions(true);
        foreach (var region in regions)
        {
            if (region.Count < minSize)
                foreach (var p in region)
                    Map[p.X, p.Y] = false;
        }
    }
}
```

### 4c. Wave Function Collapse (Tile Constraint Propagation)

```csharp
public class WaveFunctionCollapse
{
    private readonly int _width, _height;
    private readonly int _tileCount;
    private readonly bool[,] _adjacency; // [tileA * 4 + dir, tileB] = allowed
    private readonly HashSet<int>[,] _wave;
    private readonly Random _rng;

    // Directions: 0=up, 1=right, 2=down, 3=left
    private static readonly (int Dx, int Dy)[] Dirs = { (0, -1), (1, 0), (0, 1), (-1, 0) };

    public int[,] Result { get; }

    public WaveFunctionCollapse(int width, int height, int tileCount,
        bool[,] adjacency, int seed = 0)
    {
        _width = width;
        _height = height;
        _tileCount = tileCount;
        _adjacency = adjacency; // [tileA * 4 + dir, tileB]
        _rng = new Random(seed);
        _wave = new HashSet<int>[width, height];
        Result = new int[width, height];

        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            _wave[x, y] = new HashSet<int>();
            for (int t = 0; t < tileCount; t++)
                _wave[x, y].Add(t);
        }
    }

    public bool Solve()
    {
        while (true)
        {
            // Find cell with minimum entropy (fewest possibilities)
            var (cx, cy) = FindMinEntropy();
            if (cx == -1) break; // all collapsed

            var options = _wave[cx, cy];
            if (options.Count == 0) return false; // contradiction

            // Collapse to random tile
            int chosen = options.ElementAt(_rng.Next(options.Count));
            options.Clear();
            options.Add(chosen);
            Result[cx, cy] = chosen;

            // Propagate constraints
            if (!Propagate(cx, cy)) return false;
        }
        return true;
    }

    private (int X, int Y) FindMinEntropy()
    {
        int minEntropy = int.MaxValue;
        int bestX = -1, bestY = -1;

        for (int x = 0; x < _width; x++)
        for (int y = 0; y < _height; y++)
        {
            int count = _wave[x, y].Count;
            if (count > 1 && count < minEntropy)
            {
                minEntropy = count;
                bestX = x;
                bestY = y;
                // Add noise to break ties
                if (_rng.Next(3) == 0) return (bestX, bestY);
            }
        }
        return (bestX, bestY);
    }

    private bool Propagate(int startX, int startY)
    {
        var stack = new Stack<(int X, int Y)>();
        stack.Push((startX, startY));

        while (stack.Count > 0)
        {
            var (x, y) = stack.Pop();
            var current = _wave[x, y];

            for (int d = 0; d < 4; d++)
            {
                int nx = x + Dirs[d].Dx, ny = y + Dirs[d].Dy;
                if (nx < 0 || ny < 0 || nx >= _width || ny >= _height) continue;

                var neighbor = _wave[nx, ny];
                if (neighbor.Count <= 1) continue;

                int before = neighbor.Count;
                neighbor.RemoveWhere(nTile =>
                {
                    // nTile is allowed only if at least one current tile allows it
                    foreach (int cTile in current)
                        if (_adjacency[cTile * 4 + d, nTile])
                            return false; // keep
                    return true; // remove
                });

                if (neighbor.Count == 0) return false; // contradiction
                if (neighbor.Count < before) stack.Push((nx, ny));
            }
        }
        return true;
    }
}

// --- Example adjacency setup for simple terrain tiles ---
// Tiles: 0=grass, 1=sand, 2=water, 3=forest
// AdjacencyBuilder helper:
public static class WfcAdjacencyBuilder
{
    public static bool[,] Build(int tileCount, List<(int From, int Dir, int To)> rules)
    {
        var adj = new bool[tileCount * 4, tileCount];
        foreach (var (from, dir, to) in rules)
        {
            adj[from * 4 + dir, to] = true;
            // Mirror: if A→right→B, then B→left→A
            int opposite = (dir + 2) % 4;
            adj[to * 4 + opposite, from] = true;
        }
        return adj;
    }
}
```

### 4d. Perlin/Simplex Noise

```csharp
public class SimplexNoise
{
    private readonly int[] _perm;

    private static readonly int[][] Grad3 =
    {
        new[]{1,1,0}, new[]{-1,1,0}, new[]{1,-1,0}, new[]{-1,-1,0},
        new[]{1,0,1}, new[]{-1,0,1}, new[]{1,0,-1}, new[]{-1,0,-1},
        new[]{0,1,1}, new[]{0,-1,1}, new[]{0,1,-1}, new[]{0,-1,-1}
    };

    public SimplexNoise(int seed = 0)
    {
        var rng = new Random(seed);
        _perm = new int[512];
        var p = Enumerable.Range(0, 256).ToArray();
        rng.Shuffle(p);
        for (int i = 0; i < 512; i++) _perm[i] = p[i & 255];
    }

    public float Noise2D(float xin, float yin)
    {
        const float F2 = 0.3660254f; // (sqrt(3)-1)/2
        const float G2 = 0.2113249f; // (3-sqrt(3))/6

        float s = (xin + yin) * F2;
        int i = FastFloor(xin + s), j = FastFloor(yin + s);
        float t = (i + j) * G2;
        float x0 = xin - (i - t), y0 = yin - (j - t);

        int i1, j1;
        if (x0 > y0) { i1 = 1; j1 = 0; } else { i1 = 0; j1 = 1; }

        float x1 = x0 - i1 + G2, y1 = y0 - j1 + G2;
        float x2 = x0 - 1f + 2f * G2, y2 = y0 - 1f + 2f * G2;

        int ii = i & 255, jj = j & 255;
        float n0 = Contrib(ii, jj, x0, y0);
        float n1 = Contrib(ii + i1, jj + j1, x1, y1);
        float n2 = Contrib(ii + 1, jj + 1, x2, y2);

        return 70f * (n0 + n1 + n2); // scale to [-1, 1]
    }

    private float Contrib(int gi, int gj, float x, float y)
    {
        float t = 0.5f - x * x - y * y;
        if (t < 0) return 0f;
        t *= t;
        int g = _perm[(gi + _perm[gj & 255]) & 255] % 12;
        return t * t * (Grad3[g][0] * x + Grad3[g][1] * y);
    }

    private static int FastFloor(float x) => x > 0 ? (int)x : (int)x - 1;

    /// <summary>Fractal Brownian Motion — layered noise for natural terrain.</summary>
    public float Fbm(float x, float y, int octaves = 6, float lacunarity = 2f, float gain = 0.5f)
    {
        float sum = 0f, amp = 1f, freq = 1f, maxAmp = 0f;
        for (int i = 0; i < octaves; i++)
        {
            sum += Noise2D(x * freq, y * freq) * amp;
            maxAmp += amp;
            amp *= gain;
            freq *= lacunarity;
        }
        return sum / maxAmp; // normalize to [-1, 1]
    }
}

// --- Usage: generate a height map for terrain/biomes ---
public static float[,] GenerateHeightMap(int width, int height, int seed,
    float scale = 0.02f, int octaves = 5)
{
    var noise = new SimplexNoise(seed);
    var map = new float[width, height];
    for (int x = 0; x < width; x++)
    for (int y = 0; y < height; y++)
        map[x, y] = (noise.Fbm(x * scale, y * scale, octaves) + 1f) * 0.5f; // [0,1]
    return map;
}
```

### ECS Integration — Procgen System

```csharp
public record struct MapSeed(int Seed);
public record struct GeneratedMap(int[,] Tiles, int Width, int Height);
public record struct GenerateMapRequest(string Algorithm, int Width, int Height);

public partial class ProcgenSystem : BaseSystem<World, float>
{
    private readonly CommandBuffer _buffer;

    public ProcgenSystem(World world) : base(world)
    {
        _buffer = new CommandBuffer(world);
    }

    [Query]
    [All<GenerateMapRequest, MapSeed>]
    public void ProcessGeneration(Entity entity, ref GenerateMapRequest req, ref MapSeed seed)
    {
        int[,] tiles = req.Algorithm switch
        {
            "bsp" => GenerateBsp(req.Width, req.Height, seed.Seed),
            "caves" => GenerateCaves(req.Width, req.Height, seed.Seed),
            _ => new int[req.Width, req.Height]
        };

        _buffer.Add(entity, new GeneratedMap(tiles, req.Width, req.Height));
        _buffer.Remove<GenerateMapRequest>(entity);
    }

    private static int[,] GenerateBsp(int w, int h, int seed)
    {
        var bsp = new BspDungeon(w, h, seed: seed);
        bsp.Generate();
        return bsp.Grid;
    }

    private static int[,] GenerateCaves(int w, int h, int seed)
    {
        var caves = new CellularAutomataCaves(w, h, seed);
        caves.Generate();
        caves.CullSmallRegions();
        var result = new int[w, h];
        for (int x = 0; x < w; x++)
        for (int y = 0; y < h; y++)
            result[x, y] = caves.Map[x, y] ? (int)TileType.Floor : (int)TileType.Wall;
        return result;
    }

    public override void AfterUpdate(in float dt) => _buffer.Playback(World);
}
```

---

## 5. Crafting System

Recipe-based crafting with station types, recipe discovery, and inventory integration.

### Data Model

```csharp
public record CraftingRecipe(
    string Id,
    string Name,
    CraftingStation RequiredStation,
    List<ItemRequirement> Inputs,
    string OutputItemId,
    int OutputCount,
    float CraftTime  // seconds, 0 for instant
);

public record ItemRequirement(string ItemId, int Count);

public enum CraftingStation { None, Workbench, Forge, AlchemyTable, Anvil, Loom }

// --- ECS Components ---
public record struct CraftingStationComp(CraftingStation Type);
public record struct CraftingProgress(string RecipeId, float Elapsed, float Duration);
public record struct CraftRequest(string RecipeId);
public record struct KnownRecipes(HashSet<string> RecipeIds)
{
    public KnownRecipes() : this(new HashSet<string>()) { }
}
```

### Recipe Registry

```csharp
public class RecipeRegistry
{
    private readonly Dictionary<string, CraftingRecipe> _recipes = new();
    private readonly Dictionary<CraftingStation, List<CraftingRecipe>> _byStation = new();

    public void Register(CraftingRecipe recipe)
    {
        _recipes[recipe.Id] = recipe;
        if (!_byStation.ContainsKey(recipe.RequiredStation))
            _byStation[recipe.RequiredStation] = new();
        _byStation[recipe.RequiredStation].Add(recipe);
    }

    public CraftingRecipe? Get(string id) => _recipes.GetValueOrDefault(id);

    public List<CraftingRecipe> GetForStation(CraftingStation station) =>
        _byStation.GetValueOrDefault(station, new());

    public bool CanCraft(CraftingRecipe recipe, in Inventory inv, ItemRegistry items)
    {
        foreach (var req in recipe.Inputs)
            if (InventoryOps.CountItem(in inv, req.ItemId) < req.Count)
                return false;
        return true;
    }

    public void LoadFromJson(string json)
    {
        var recipes = JsonSerializer.Deserialize<List<CraftingRecipe>>(json);
        foreach (var r in recipes!) Register(r);
    }
}
```

### Crafting ECS System

```csharp
public partial class CraftingSystem : BaseSystem<World, float>
{
    private readonly RecipeRegistry _recipeRegistry;
    private readonly ItemRegistry _itemRegistry;
    private readonly CommandBuffer _buffer;

    public CraftingSystem(World world, RecipeRegistry recipes, ItemRegistry items) : base(world)
    {
        _recipeRegistry = recipes;
        _itemRegistry = items;
        _buffer = new CommandBuffer(world);
    }

    /// <summary>Handles new craft requests — validates and starts crafting.</summary>
    [Query]
    [All<CraftRequest, Inventory, KnownRecipes>]
    [None<CraftingProgress>]
    public void StartCrafting(Entity entity, ref CraftRequest req,
        ref Inventory inv, ref KnownRecipes known)
    {
        var recipe = _recipeRegistry.Get(req.RecipeId);
        if (recipe == null || !known.RecipeIds.Contains(recipe.Id))
        {
            _buffer.Remove<CraftRequest>(entity);
            return;
        }

        if (!_recipeRegistry.CanCraft(recipe, in inv, _itemRegistry))
        {
            _buffer.Remove<CraftRequest>(entity);
            return;
        }

        // Consume ingredients
        foreach (var input in recipe.Inputs)
            InventoryOps.Remove(ref inv, input.ItemId, input.Count);

        if (recipe.CraftTime <= 0)
        {
            // Instant craft
            InventoryOps.TryAdd(ref inv, recipe.OutputItemId, recipe.OutputCount, _itemRegistry);
            _buffer.Remove<CraftRequest>(entity);
        }
        else
        {
            // Start timed craft
            _buffer.Add(entity, new CraftingProgress(recipe.Id, 0f, recipe.CraftTime));
            _buffer.Remove<CraftRequest>(entity);
        }
    }

    /// <summary>Ticks crafting progress and completes when done.</summary>
    [Query]
    [All<CraftingProgress, Inventory>]
    public void UpdateCrafting([Data] in float dt, Entity entity,
        ref CraftingProgress progress, ref Inventory inv)
    {
        progress = progress with { Elapsed = progress.Elapsed + dt };

        if (progress.Elapsed >= progress.Duration)
        {
            var recipe = _recipeRegistry.Get(progress.RecipeId);
            if (recipe != null)
                InventoryOps.TryAdd(ref inv, recipe.OutputItemId, recipe.OutputCount, _itemRegistry);
            _buffer.Remove<CraftingProgress>(entity);
        }
    }

    public override void AfterUpdate(in float dt) => _buffer.Playback(World);
}

// --- Recipe Discovery System ---
public partial class RecipeDiscoverySystem : BaseSystem<World, float>
{
    private readonly RecipeRegistry _recipeRegistry;
    private readonly CommandBuffer _buffer;

    public RecipeDiscoverySystem(World world, RecipeRegistry recipes) : base(world)
    {
        _recipeRegistry = recipes;
        _buffer = new CommandBuffer(world);
    }

    /// <summary>Auto-discover recipes when player picks up a key ingredient.</summary>
    [Query]
    [All<KnownRecipes, Inventory>]
    public void CheckDiscovery(Entity entity, ref KnownRecipes known, ref Inventory inv)
    {
        // Check all recipes — unlock if player has at least one ingredient
        foreach (var (id, recipe) in EnumerateRecipes())
        {
            if (known.RecipeIds.Contains(id)) continue;
            foreach (var input in recipe.Inputs)
            {
                if (InventoryOps.CountItem(in inv, input.ItemId) > 0)
                {
                    known.RecipeIds.Add(id);
                    // Trigger UI notification: "New recipe discovered!"
                    break;
                }
            }
        }
    }

    private IEnumerable<(string Id, CraftingRecipe Recipe)> EnumerateRecipes()
    {
        // In practice, expose an enumerator from RecipeRegistry
        yield break; // placeholder — iterate _recipeRegistry internally
    }
}
```

---

## 6. Quest / Objective System

State-machine-driven quests with typed objectives, rewards, and full ECS event integration.

### Data Model

```csharp
public enum QuestState { Unavailable, Available, Active, Completed, TurnedIn }
public enum ObjectiveType { Kill, Collect, ReachLocation, TalkToNpc, Interact, Custom }

public class QuestDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public List<QuestObjective> Objectives { get; init; } = new();
    public QuestReward Reward { get; init; } = new();
    public List<string> Prerequisites { get; init; } = new();  // quest IDs that must be TurnedIn
    public string? TurnInNpcId { get; init; }
}

public class QuestObjective
{
    public string Id { get; init; } = "";
    public ObjectiveType Type { get; init; }
    public string TargetId { get; init; } = "";  // enemy type, item id, location id, npc id
    public int RequiredCount { get; init; } = 1;
    public string Description { get; init; } = "";
}

public class QuestReward
{
    public int Experience { get; init; }
    public int Gold { get; init; }
    public List<ItemRequirement> Items { get; init; } = new();
}

// --- Runtime quest state ---
public class QuestInstance
{
    public string QuestId { get; init; } = "";
    public QuestState State { get; set; } = QuestState.Unavailable;
    public Dictionary<string, int> ObjectiveProgress { get; } = new(); // objectiveId → current count
}

// Save-friendly snapshot
public class QuestSaveData
{
    public QuestState State { get; init; }
    public Dictionary<string, int> Progress { get; init; } = new();
}
```

### Quest Manager

```csharp
public class QuestManager
{
    private readonly Dictionary<string, QuestDefinition> _definitions = new();
    private readonly Dictionary<string, QuestInstance> _instances = new();

    public event Action<string>? OnQuestActivated;
    public event Action<string>? OnQuestCompleted;
    public event Action<string, string>? OnObjectiveUpdated; // questId, objectiveId

    public void RegisterQuest(QuestDefinition def)
    {
        _definitions[def.Id] = def;
        _instances[def.Id] = new QuestInstance { QuestId = def.Id };
        foreach (var obj in def.Objectives)
            _instances[def.Id].ObjectiveProgress[obj.Id] = 0;
    }

    public void EvaluateAvailability()
    {
        foreach (var (id, inst) in _instances)
        {
            if (inst.State != QuestState.Unavailable) continue;
            var def = _definitions[id];
            bool prereqsMet = def.Prerequisites.All(p =>
                _instances.ContainsKey(p) && _instances[p].State == QuestState.TurnedIn);
            if (prereqsMet) inst.State = QuestState.Available;
        }
    }

    public bool TryActivate(string questId)
    {
        if (!_instances.TryGetValue(questId, out var inst)) return false;
        if (inst.State != QuestState.Available) return false;
        inst.State = QuestState.Active;
        OnQuestActivated?.Invoke(questId);
        return true;
    }

    /// <summary>Report progress for active quests matching the event.</summary>
    public void ReportKill(string enemyType)
        => ReportProgress(ObjectiveType.Kill, enemyType, 1);

    public void ReportCollect(string itemId, int count)
        => ReportProgress(ObjectiveType.Collect, itemId, count);

    public void ReportReachLocation(string locationId)
        => ReportProgress(ObjectiveType.ReachLocation, locationId, 1);

    public void ReportTalkTo(string npcId)
        => ReportProgress(ObjectiveType.TalkToNpc, npcId, 1);

    private void ReportProgress(ObjectiveType type, string targetId, int amount)
    {
        foreach (var (questId, inst) in _instances)
        {
            if (inst.State != QuestState.Active) continue;
            var def = _definitions[questId];

            foreach (var obj in def.Objectives)
            {
                if (obj.Type != type || obj.TargetId != targetId) continue;
                if (inst.ObjectiveProgress[obj.Id] >= obj.RequiredCount) continue;

                inst.ObjectiveProgress[obj.Id] =
                    Math.Min(inst.ObjectiveProgress[obj.Id] + amount, obj.RequiredCount);
                OnObjectiveUpdated?.Invoke(questId, obj.Id);
            }

            // Check if all objectives complete
            bool allDone = def.Objectives.All(o =>
                inst.ObjectiveProgress[o.Id] >= o.RequiredCount);
            if (allDone && inst.State == QuestState.Active)
            {
                inst.State = QuestState.Completed;
                OnQuestCompleted?.Invoke(questId);
            }
        }
    }

    public bool TryTurnIn(string questId)
    {
        if (!_instances.TryGetValue(questId, out var inst)) return false;
        if (inst.State != QuestState.Completed) return false;
        inst.State = QuestState.TurnedIn;
        EvaluateAvailability(); // unlock downstream quests
        return true;
    }

    public QuestReward? GetReward(string questId) =>
        _definitions.TryGetValue(questId, out var def) ? def.Reward : null;

    public List<QuestInstance> GetActiveQuests() =>
        _instances.Values.Where(i => i.State == QuestState.Active).ToList();

    public Dictionary<string, QuestSaveData> Snapshot() =>
        _instances.ToDictionary(kv => kv.Key, kv => new QuestSaveData
        {
            State = kv.Value.State,
            Progress = new Dictionary<string, int>(kv.Value.ObjectiveProgress)
        });

    public void Restore(Dictionary<string, QuestSaveData> data)
    {
        foreach (var (id, save) in data)
        {
            if (!_instances.TryGetValue(id, out var inst)) continue;
            inst.State = save.State;
            foreach (var (objId, count) in save.Progress)
                inst.ObjectiveProgress[objId] = count;
        }
    }
}
```

### ECS Integration

```csharp
// --- Components ---
public record struct QuestLog(List<string> ActiveQuestIds)
{
    public QuestLog() : this(new List<string>()) { }
}

public record struct QuestGiver(string QuestId);

public record struct KillEvent(string EnemyType);  // transient event component

// --- Systems ---
public partial class QuestKillTrackingSystem : BaseSystem<World, float>
{
    private readonly QuestManager _questMgr;
    private readonly CommandBuffer _buffer;

    public QuestKillTrackingSystem(World world, QuestManager questMgr) : base(world)
    {
        _questMgr = questMgr;
        _buffer = new CommandBuffer(world);
    }

    [Query]
    [All<KillEvent>]
    public void TrackKill(Entity entity, ref KillEvent evt)
    {
        _questMgr.ReportKill(evt.EnemyType);
        _buffer.Remove<KillEvent>(entity);
    }

    public override void AfterUpdate(in float dt) => _buffer.Playback(World);
}

public partial class QuestGiverInteractionSystem : BaseSystem<World, float>
{
    private readonly QuestManager _questMgr;

    public QuestGiverInteractionSystem(World world, QuestManager mgr) : base(world)
        => _questMgr = mgr;

    [Query]
    [All<QuestGiver, Position>]
    public void CheckInteraction(Entity npc, ref QuestGiver giver, ref Position npcPos)
    {
        // When player interacts with NPC in range...
        // (interaction check omitted for brevity — same pattern as DialogueInteractionSystem)
        if (_questMgr.TryTurnIn(giver.QuestId))
        {
            var reward = _questMgr.GetReward(giver.QuestId);
            // Apply reward to player inventory/xp
        }
        else
        {
            _questMgr.TryActivate(giver.QuestId);
        }
    }
}
```

---

## 7. Status Effect / Buff System

Timed and permanent effects with stackable modifiers, using a modifier stack for stat calculation.

### Components

```csharp
public enum EffectType { Poison, Burn, Freeze, Shield, SpeedBoost, Stun, Regen, Berserk }
public enum StackBehavior { Stack, Refresh, Unique }

public record struct StatusEffect(
    EffectType Type,
    float Duration,        // total duration (-1 for permanent)
    float Elapsed,
    float TickInterval,    // for DOT/HOT effects
    float TickTimer,
    float Value,           // damage per tick, shield amount, speed multiplier, etc.
    StackBehavior Stacking,
    int StackCount,
    int MaxStacks
);

public record struct StatusEffects(List<StatusEffect> Active)
{
    public StatusEffects() : this(new List<StatusEffect>()) { }
}

// Stat modifier — applied by effects to modify base stats
public record struct StatModifiers(
    float FlatDamage,
    float FlatArmor,
    float FlatSpeed,
    float MultDamage,   // 1.0 = no change
    float MultArmor,
    float MultSpeed
)
{
    public static StatModifiers Identity => new(0, 0, 0, 1f, 1f, 1f);
}

public record struct BaseStats(float Damage, float Armor, float Speed);
public record struct ComputedStats(float Damage, float Armor, float Speed);
```

### Effect Application

```csharp
public static class StatusEffectOps
{
    public static void Apply(ref StatusEffects effects, StatusEffect newEffect)
    {
        // Check stacking behavior
        for (int i = 0; i < effects.Active.Count; i++)
        {
            var existing = effects.Active[i];
            if (existing.Type != newEffect.Type) continue;

            switch (existing.Stacking)
            {
                case StackBehavior.Refresh:
                    effects.Active[i] = existing with { Elapsed = 0f };
                    return;
                case StackBehavior.Unique:
                    return; // already has it, do nothing
                case StackBehavior.Stack:
                    if (existing.StackCount < existing.MaxStacks)
                    {
                        effects.Active[i] = existing with
                        {
                            StackCount = existing.StackCount + 1,
                            Elapsed = 0f
                        };
                    }
                    return;
            }
        }

        effects.Active.Add(newEffect with { Elapsed = 0f, TickTimer = 0f, StackCount = 1 });
    }

    public static void RemoveByType(ref StatusEffects effects, EffectType type)
    {
        effects.Active.RemoveAll(e => e.Type == type);
    }

    public static bool Has(in StatusEffects effects, EffectType type)
    {
        foreach (var e in effects.Active)
            if (e.Type == type) return true;
        return false;
    }
}
```

### Status Effect ECS System

```csharp
public partial class StatusEffectSystem : BaseSystem<World, float>
{
    private readonly CommandBuffer _buffer;

    public StatusEffectSystem(World world) : base(world)
    {
        _buffer = new CommandBuffer(world);
    }

    [Query]
    [All<StatusEffects, Health>]
    public void TickEffects([Data] in float dt, Entity entity,
        ref StatusEffects effects, ref Health hp)
    {
        for (int i = effects.Active.Count - 1; i >= 0; i--)
        {
            var effect = effects.Active[i];
            effect = effect with { Elapsed = effect.Elapsed + dt };

            // DOT/HOT tick
            if (effect.TickInterval > 0)
            {
                effect = effect with { TickTimer = effect.TickTimer + dt };
                while (effect.TickTimer >= effect.TickInterval)
                {
                    effect = effect with { TickTimer = effect.TickTimer - effect.TickInterval };
                    float tickValue = effect.Value * effect.StackCount;

                    switch (effect.Type)
                    {
                        case EffectType.Poison:
                        case EffectType.Burn:
                            hp = hp with { Current = hp.Current - (int)tickValue };
                            break;
                        case EffectType.Regen:
                            hp = hp with { Current = Math.Min(hp.Current + (int)tickValue, hp.Max) };
                            break;
                    }
                }
            }

            // Expired?
            if (effect.Duration >= 0 && effect.Elapsed >= effect.Duration)
            {
                effects.Active.RemoveAt(i);
                continue;
            }

            effects.Active[i] = effect;
        }
    }
}

/// <summary>Recomputes final stats from base + all active modifiers.</summary>
public partial class StatComputeSystem : BaseSystem<World, float>
{
    public StatComputeSystem(World world) : base(world) { }

    [Query]
    [All<BaseStats, ComputedStats, StatusEffects>]
    public void ComputeStats(ref BaseStats baseStats, ref ComputedStats computed,
        ref StatusEffects effects)
    {
        var mods = StatModifiers.Identity;

        foreach (var effect in effects.Active)
        {
            float v = effect.Value * effect.StackCount;
            mods = effect.Type switch
            {
                EffectType.SpeedBoost => mods with { MultSpeed = mods.MultSpeed + v },
                EffectType.Berserk    => mods with { MultDamage = mods.MultDamage + v, MultArmor = mods.MultArmor - 0.2f },
                EffectType.Shield     => mods with { FlatArmor = mods.FlatArmor + v },
                EffectType.Freeze     => mods with { MultSpeed = mods.MultSpeed * 0.3f },
                EffectType.Stun       => mods with { MultSpeed = 0f },
                _ => mods
            };
        }

        computed = new ComputedStats(
            Damage: (baseStats.Damage + mods.FlatDamage) * mods.MultDamage,
            Armor:  Math.Max(0, (baseStats.Armor + mods.FlatArmor) * mods.MultArmor),
            Speed:  Math.Max(0, (baseStats.Speed + mods.FlatSpeed) * mods.MultSpeed)
        );
    }
}
```

### Visual Indicator Integration

```csharp
// Tint entity sprite based on active effects
public partial class StatusEffectVisualSystem : BaseSystem<World, float>
{
    public StatusEffectVisualSystem(World world) : base(world) { }

    [Query]
    [All<StatusEffects, SpriteRenderer>]
    public void ApplyVisuals(ref StatusEffects effects, ref SpriteRenderer sprite)
    {
        Color tint = Color.White;
        foreach (var effect in effects.Active)
        {
            tint = effect.Type switch
            {
                EffectType.Poison => BlendColor(tint, Color.Green, 0.3f),
                EffectType.Burn   => BlendColor(tint, Color.OrangeRed, 0.4f),
                EffectType.Freeze => BlendColor(tint, Color.CornflowerBlue, 0.5f),
                EffectType.Shield => BlendColor(tint, Color.Gold, 0.2f),
                _ => tint
            };
        }
        sprite = sprite with { Tint = tint };
    }

    private static Color BlendColor(Color a, Color b, float t) =>
        new Color(
            (int)MathHelper.Lerp(a.R, b.R, t),
            (int)MathHelper.Lerp(a.G, b.G, t),
            (int)MathHelper.Lerp(a.B, b.B, t),
            a.A);
}
```

---

## 8. Undo/Redo — Command Pattern

Stack-based command history with redo branch pruning. Essential for puzzle games, strategy games, and level editors.

### Core Framework

```csharp
public interface ICommand
{
    void Execute();
    void Undo();
    string Description { get; }
}

public class CommandHistory
{
    private readonly Stack<ICommand> _undoStack = new();
    private readonly Stack<ICommand> _redoStack = new();
    private readonly int _maxHistory;

    public int UndoCount => _undoStack.Count;
    public int RedoCount => _redoStack.Count;
    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public event Action<ICommand>? OnExecute;
    public event Action<ICommand>? OnUndo;
    public event Action<ICommand>? OnRedo;

    public CommandHistory(int maxHistory = 100) => _maxHistory = maxHistory;

    public void Execute(ICommand command)
    {
        command.Execute();
        _undoStack.Push(command);
        _redoStack.Clear(); // prune redo branch

        // Trim oldest if over limit
        if (_undoStack.Count > _maxHistory)
        {
            var temp = _undoStack.ToArray();
            _undoStack.Clear();
            for (int i = 0; i < _maxHistory; i++)
                _undoStack.Push(temp[i]);
        }

        OnExecute?.Invoke(command);
    }

    public bool Undo()
    {
        if (!CanUndo) return false;
        var cmd = _undoStack.Pop();
        cmd.Undo();
        _redoStack.Push(cmd);
        OnUndo?.Invoke(cmd);
        return true;
    }

    public bool Redo()
    {
        if (!CanRedo) return false;
        var cmd = _redoStack.Pop();
        cmd.Execute();
        _undoStack.Push(cmd);
        OnRedo?.Invoke(cmd);
        return true;
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }
}
```

### Concrete Commands

```csharp
// --- Puzzle: Move piece on grid ---
public class MovePieceCommand : ICommand
{
    private readonly World _world;
    private readonly Entity _entity;
    private readonly Point _from;
    private readonly Point _to;

    public string Description => $"Move piece {_entity} from {_from} to {_to}";

    public MovePieceCommand(World world, Entity entity, Point from, Point to)
    {
        _world = world; _entity = entity; _from = from; _to = to;
    }

    public void Execute()
    {
        ref var pos = ref _world.Get<GridPosition>(_entity);
        pos = new GridPosition(_to.X, _to.Y);
    }

    public void Undo()
    {
        ref var pos = ref _world.Get<GridPosition>(_entity);
        pos = new GridPosition(_from.X, _from.Y);
    }
}

// --- Level editor: Place tile ---
public class PlaceTileCommand : ICommand
{
    private readonly int[,] _tilemap;
    private readonly int _x, _y;
    private readonly int _newTile;
    private readonly int _oldTile;

    public string Description => $"Place tile {_newTile} at ({_x},{_y})";

    public PlaceTileCommand(int[,] tilemap, int x, int y, int newTile)
    {
        _tilemap = tilemap; _x = x; _y = y;
        _newTile = newTile;
        _oldTile = tilemap[x, y];
    }

    public void Execute() => _tilemap[_x, _y] = _newTile;
    public void Undo() => _tilemap[_x, _y] = _oldTile;
}

// --- Composite: group multiple commands into one undo step ---
public class CompositeCommand : ICommand
{
    private readonly List<ICommand> _commands;
    public string Description { get; }

    public CompositeCommand(string description, params ICommand[] commands)
    {
        Description = description;
        _commands = commands.ToList();
    }

    public void Execute()
    {
        foreach (var cmd in _commands) cmd.Execute();
    }

    public void Undo()
    {
        for (int i = _commands.Count - 1; i >= 0; i--)
            _commands[i].Undo();
    }
}

// --- Strategy: spawn/destroy entity ---
public class SpawnEntityCommand : ICommand
{
    private readonly World _world;
    private Entity _entity;
    private readonly Point _position;
    private readonly string _prefabId;
    private bool _alive;

    public string Description => $"Spawn {_prefabId} at {_position}";

    public SpawnEntityCommand(World world, string prefabId, Point position)
    {
        _world = world; _prefabId = prefabId; _position = position;
    }

    public void Execute()
    {
        if (!_alive)
        {
            _entity = _world.Create(
                new GridPosition(_position.X, _position.Y),
                new Persistent()
            );
            _alive = true;
        }
    }

    public void Undo()
    {
        if (_alive)
        {
            _world.Destroy(_entity);
            _alive = false;
        }
    }
}
```

### ECS Components & System

```csharp
public record struct GridPosition(int X, int Y);
public record struct UndoRedoInput(bool UndoPressed, bool RedoPressed);

public partial class UndoRedoSystem : BaseSystem<World, float>
{
    private readonly CommandHistory _history;

    public UndoRedoSystem(World world, CommandHistory history) : base(world)
        => _history = history;

    [Query]
    [All<UndoRedoInput, PlayerTag>]
    public void ProcessInput(ref UndoRedoInput input)
    {
        if (input.UndoPressed) _history.Undo();
        if (input.RedoPressed) _history.Redo();
        input = new UndoRedoInput(false, false);
    }
}
```

---

## 9. Wave / Spawn System

Data-driven enemy wave spawning with difficulty scaling, spawn points, and inter-wave timers.

### Data Model

```csharp
public class WaveDefinition
{
    public int WaveNumber { get; init; }
    public List<SpawnGroup> SpawnGroups { get; init; } = new();
    public float InterWaveDelay { get; init; } = 5f;  // seconds before this wave starts
    public float DifficultyMultiplier { get; init; } = 1f;
}

public class SpawnGroup
{
    public string EnemyPrefabId { get; init; } = "";
    public int Count { get; init; }
    public float SpawnInterval { get; init; } = 0.5f;  // seconds between spawns
    public string SpawnPointTag { get; init; } = "";   // which spawn point(s) to use
    public float DelayFromWaveStart { get; init; }      // stagger groups within a wave
}

// --- ECS Components ---
public record struct SpawnPoint(string Tag, Vector2 Position);

public record struct WaveState(
    int CurrentWave,
    int TotalWaves,
    WavePhase Phase,
    float Timer,
    int GroupIndex,
    int SpawnedInGroup,
    float SpawnTimer
);

public enum WavePhase { WaitingToStart, InterWaveDelay, Spawning, WaveActive, AllComplete }

public record struct EnemyTag(int Wave); // marks spawned enemies with their wave
public struct WaveManagerTag;            // singleton tag
```

### Wave Manager System

```csharp
public partial class WaveSpawnSystem : BaseSystem<World, float>
{
    private readonly List<WaveDefinition> _waves;
    private readonly CommandBuffer _buffer;
    private readonly Action<string, Vector2, float> _spawnEnemy; // prefabId, position, diffMult

    public event Action<int>? OnWaveStarted;
    public event Action<int>? OnWaveCleared;
    public event Action? OnAllWavesComplete;

    public WaveSpawnSystem(World world, List<WaveDefinition> waves,
        Action<string, Vector2, float> spawnEnemy) : base(world)
    {
        _waves = waves;
        _spawnEnemy = spawnEnemy;
        _buffer = new CommandBuffer(world);
    }

    [Query]
    [All<WaveManagerTag, WaveState>]
    public void UpdateWaves([Data] in float dt, ref WaveState state)
    {
        switch (state.Phase)
        {
            case WavePhase.WaitingToStart:
                break; // wait for external trigger

            case WavePhase.InterWaveDelay:
                state = state with { Timer = state.Timer + dt };
                if (state.Timer >= _waves[state.CurrentWave].InterWaveDelay)
                {
                    state = state with { Phase = WavePhase.Spawning, Timer = 0, GroupIndex = 0, SpawnedInGroup = 0, SpawnTimer = 0 };
                    OnWaveStarted?.Invoke(state.CurrentWave);
                }
                break;

            case WavePhase.Spawning:
                TickSpawning(ref state, dt);
                break;

            case WavePhase.WaveActive:
                // Check if all enemies from this wave are dead
                int alive = CountAliveEnemies(state.CurrentWave);
                if (alive == 0)
                {
                    OnWaveCleared?.Invoke(state.CurrentWave);
                    if (state.CurrentWave + 1 >= state.TotalWaves)
                    {
                        state = state with { Phase = WavePhase.AllComplete };
                        OnAllWavesComplete?.Invoke();
                    }
                    else
                    {
                        state = state with
                        {
                            CurrentWave = state.CurrentWave + 1,
                            Phase = WavePhase.InterWaveDelay,
                            Timer = 0
                        };
                    }
                }
                break;
        }
    }

    private void TickSpawning(ref WaveState state, float dt)
    {
        var wave = _waves[state.CurrentWave];
        if (state.GroupIndex >= wave.SpawnGroups.Count)
        {
            state = state with { Phase = WavePhase.WaveActive };
            return;
        }

        state = state with { Timer = state.Timer + dt };
        var group = wave.SpawnGroups[state.GroupIndex];

        // Wait for group delay
        if (state.Timer < group.DelayFromWaveStart) return;

        state = state with { SpawnTimer = state.SpawnTimer + dt };
        while (state.SpawnTimer >= group.SpawnInterval && state.SpawnedInGroup < group.Count)
        {
            state = state with
            {
                SpawnTimer = state.SpawnTimer - group.SpawnInterval,
                SpawnedInGroup = state.SpawnedInGroup + 1
            };

            var spawnPos = GetSpawnPosition(group.SpawnPointTag);
            _spawnEnemy(group.EnemyPrefabId, spawnPos, wave.DifficultyMultiplier);
        }

        if (state.SpawnedInGroup >= group.Count)
        {
            state = state with
            {
                GroupIndex = state.GroupIndex + 1,
                SpawnedInGroup = 0,
                SpawnTimer = 0
            };
        }
    }

    private Vector2 GetSpawnPosition(string tag)
    {
        var candidates = new List<Vector2>();
        var query = new QueryDescription().WithAll<SpawnPoint>();
        World.Query(in query, (ref SpawnPoint sp) =>
        {
            if (sp.Tag == tag || string.IsNullOrEmpty(tag))
                candidates.Add(sp.Position);
        });
        return candidates.Count > 0
            ? candidates[Random.Shared.Next(candidates.Count)]
            : Vector2.Zero;
    }

    private int CountAliveEnemies(int wave)
    {
        int count = 0;
        var query = new QueryDescription().WithAll<EnemyTag, Health>();
        World.Query(in query, (ref EnemyTag tag, ref Health hp) =>
        {
            if (tag.Wave == wave && hp.Current > 0) count++;
        });
        return count;
    }

    public void StartWaves(Entity managerEntity)
    {
        ref var state = ref World.Get<WaveState>(managerEntity);
        state = new WaveState(0, _waves.Count, WavePhase.InterWaveDelay, 0, 0, 0, 0);
    }

    public override void AfterUpdate(in float dt) => _buffer.Playback(World);
}
```

### Example Wave JSON

```json
[
  {
    "WaveNumber": 1,
    "InterWaveDelay": 3.0,
    "DifficultyMultiplier": 1.0,
    "SpawnGroups": [
      { "EnemyPrefabId": "slime", "Count": 5, "SpawnInterval": 0.8, "SpawnPointTag": "east" },
      { "EnemyPrefabId": "bat", "Count": 3, "SpawnInterval": 1.0, "SpawnPointTag": "north", "DelayFromWaveStart": 3.0 }
    ]
  },
  {
    "WaveNumber": 2,
    "InterWaveDelay": 5.0,
    "DifficultyMultiplier": 1.5,
    "SpawnGroups": [
      { "EnemyPrefabId": "slime", "Count": 8, "SpawnInterval": 0.5, "SpawnPointTag": "east" },
      { "EnemyPrefabId": "skeleton", "Count": 4, "SpawnInterval": 1.0, "SpawnPointTag": "west", "DelayFromWaveStart": 2.0 }
    ]
  }
]
```

---

## 10. Day/Night Cycle & Weather

Time-of-day simulation with ambient lighting curves, weather state machine, and gameplay effect integration.

### Components

```csharp
public record struct TimeOfDay(
    float CurrentHour,      // 0.0 - 24.0
    float TimeScale,        // real seconds per game hour
    bool Paused
);

public enum WeatherType { Clear, Cloudy, Rain, Storm, Snow, Fog }

public record struct Weather(
    WeatherType Current,
    WeatherType Next,
    float TransitionProgress,  // 0-1 blend between current and next
    float TransitionDuration,
    float TimeUntilChange,
    float MinDuration,
    float MaxDuration
);

public record struct AmbientLight(Color Color, float Intensity);
public struct DayNightManagerTag;  // singleton
```

### Ambient Lighting Curves

```csharp
public static class DayNightCurves
{
    // Ambient color at key hours — linearly interpolated between
    private static readonly (float Hour, Color Color)[] AmbientColors =
    {
        (0f,  new Color(10, 10, 40)),      // midnight
        (5f,  new Color(30, 30, 60)),      // pre-dawn
        (6f,  new Color(180, 120, 80)),    // dawn
        (8f,  new Color(255, 240, 220)),   // morning
        (12f, new Color(255, 255, 255)),   // noon
        (17f, new Color(255, 220, 180)),   // afternoon
        (19f, new Color(200, 100, 50)),    // sunset
        (20f, new Color(40, 40, 80)),      // dusk
        (24f, new Color(10, 10, 40)),      // midnight (wrap)
    };

    private static readonly (float Hour, float Intensity)[] IntensityCurve =
    {
        (0f, 0.15f), (5f, 0.2f), (6f, 0.5f), (8f, 0.9f),
        (12f, 1.0f), (17f, 0.9f), (19f, 0.5f), (20f, 0.2f), (24f, 0.15f),
    };

    public static Color GetAmbientColor(float hour)
    {
        hour %= 24f;
        for (int i = 0; i < AmbientColors.Length - 1; i++)
        {
            if (hour >= AmbientColors[i].Hour && hour <= AmbientColors[i + 1].Hour)
            {
                float t = (hour - AmbientColors[i].Hour) /
                          (AmbientColors[i + 1].Hour - AmbientColors[i].Hour);
                return LerpColor(AmbientColors[i].Color, AmbientColors[i + 1].Color, t);
            }
        }
        return AmbientColors[0].Color;
    }

    public static float GetAmbientIntensity(float hour)
    {
        hour %= 24f;
        for (int i = 0; i < IntensityCurve.Length - 1; i++)
        {
            if (hour >= IntensityCurve[i].Hour && hour <= IntensityCurve[i + 1].Hour)
            {
                float t = (hour - IntensityCurve[i].Hour) /
                          (IntensityCurve[i + 1].Hour - IntensityCurve[i].Hour);
                return MathHelper.Lerp(IntensityCurve[i].Intensity, IntensityCurve[i + 1].Intensity, t);
            }
        }
        return IntensityCurve[0].Intensity;
    }

    public static bool IsNight(float hour) => hour < 6f || hour >= 20f;
    public static bool IsDawn(float hour) => hour >= 5f && hour < 8f;
    public static bool IsDusk(float hour) => hour >= 17f && hour < 20f;

    private static Color LerpColor(Color a, Color b, float t) =>
        new Color(
            (int)MathHelper.Lerp(a.R, b.R, t),
            (int)MathHelper.Lerp(a.G, b.G, t),
            (int)MathHelper.Lerp(a.B, b.B, t),
            255);
}
```

### Day/Night System

```csharp
public partial class DayNightSystem : BaseSystem<World, float>
{
    public event Action<float>? OnHourChanged;  // fires each in-game hour

    public DayNightSystem(World world) : base(world) { }

    [Query]
    [All<DayNightManagerTag, TimeOfDay, AmbientLight>]
    public void UpdateDayNight([Data] in float dt, ref TimeOfDay time, ref AmbientLight ambient)
    {
        if (time.Paused) return;

        float prevHour = time.CurrentHour;
        float newHour = time.CurrentHour + (dt / time.TimeScale);
        if (newHour >= 24f) newHour -= 24f;
        time = time with { CurrentHour = newHour };

        // Fire hour events
        if ((int)prevHour != (int)newHour)
            OnHourChanged?.Invoke(newHour);

        // Update ambient from curves
        ambient = new AmbientLight(
            DayNightCurves.GetAmbientColor(newHour),
            DayNightCurves.GetAmbientIntensity(newHour)
        );
    }
}
```

### Weather System

```csharp
public partial class WeatherSystem : BaseSystem<World, float>
{
    private readonly Random _rng = new();

    // Weather modifies ambient light
    private static readonly Dictionary<WeatherType, (float IntensityMult, Color Tint)> WeatherEffects = new()
    {
        { WeatherType.Clear,  (1.0f, Color.White) },
        { WeatherType.Cloudy, (0.7f, new Color(200, 200, 210)) },
        { WeatherType.Rain,   (0.5f, new Color(150, 160, 180)) },
        { WeatherType.Storm,  (0.3f, new Color(100, 100, 130)) },
        { WeatherType.Snow,   (0.8f, new Color(220, 225, 240)) },
        { WeatherType.Fog,    (0.6f, new Color(180, 180, 190)) },
    };

    public event Action<WeatherType>? OnWeatherChanged;

    public WeatherSystem(World world) : base(world) { }

    [Query]
    [All<DayNightManagerTag, Weather, AmbientLight>]
    public void UpdateWeather([Data] in float dt, ref Weather weather, ref AmbientLight ambient)
    {
        // Transition between weather states
        if (weather.TransitionProgress < 1f && weather.Current != weather.Next)
        {
            weather = weather with
            {
                TransitionProgress = Math.Min(1f,
                    weather.TransitionProgress + dt / weather.TransitionDuration)
            };

            if (weather.TransitionProgress >= 1f)
            {
                weather = weather with { Current = weather.Next };
                OnWeatherChanged?.Invoke(weather.Current);
            }
        }

        // Timer for next change
        weather = weather with { TimeUntilChange = weather.TimeUntilChange - dt };
        if (weather.TimeUntilChange <= 0)
        {
            var nextType = PickNextWeather(weather.Current);
            weather = weather with
            {
                Next = nextType,
                TransitionProgress = 0f,
                TransitionDuration = 10f, // 10 seconds to transition
                TimeUntilChange = weather.MinDuration +
                    _rng.NextSingle() * (weather.MaxDuration - weather.MinDuration)
            };
        }

        // Apply weather tint to ambient light
        var (currentMult, currentTint) = WeatherEffects[weather.Current];
        var (nextMult, nextTint) = WeatherEffects[weather.Next];
        float t = weather.TransitionProgress;

        float intensityMod = MathHelper.Lerp(currentMult, nextMult, t);
        Color weatherTint = LerpColor(currentTint, nextTint, t);

        ambient = new AmbientLight(
            MultiplyColor(ambient.Color, weatherTint),
            ambient.Intensity * intensityMod
        );
    }

    private WeatherType PickNextWeather(WeatherType current)
    {
        // Weighted transitions — avoid jarring jumps
        return current switch
        {
            WeatherType.Clear => _rng.Next(10) switch
            {
                < 4 => WeatherType.Clear,
                < 7 => WeatherType.Cloudy,
                < 9 => WeatherType.Fog,
                _   => WeatherType.Rain,
            },
            WeatherType.Cloudy => _rng.Next(10) switch
            {
                < 3 => WeatherType.Clear,
                < 5 => WeatherType.Cloudy,
                < 8 => WeatherType.Rain,
                _   => WeatherType.Storm,
            },
            WeatherType.Rain => _rng.Next(10) switch
            {
                < 2 => WeatherType.Cloudy,
                < 5 => WeatherType.Rain,
                < 8 => WeatherType.Storm,
                _   => WeatherType.Clear,
            },
            WeatherType.Storm => _rng.Next(10) switch
            {
                < 4 => WeatherType.Rain,
                < 7 => WeatherType.Cloudy,
                _   => WeatherType.Storm,
            },
            _ => WeatherType.Clear
        };
    }

    private static Color LerpColor(Color a, Color b, float t) =>
        new Color(
            (int)MathHelper.Lerp(a.R, b.R, t),
            (int)MathHelper.Lerp(a.G, b.G, t),
            (int)MathHelper.Lerp(a.B, b.B, t), 255);

    private static Color MultiplyColor(Color a, Color b) =>
        new Color(a.R * b.R / 255, a.G * b.G / 255, a.B * b.B / 255, 255);
}
```

### Gameplay Effects

```csharp
/// <summary>
/// Adjusts gameplay based on time and weather:
/// - Crops grow during day, not night
/// - Some enemies only spawn at night
/// - Rain slows movement, storm deals periodic damage outdoors
/// </summary>
public partial class TimeWeatherGameplaySystem : BaseSystem<World, float>
{
    public TimeWeatherGameplaySystem(World world) : base(world) { }

    [Query]
    [All<DayNightManagerTag, TimeOfDay, Weather>]
    public void ApplyGameplayEffects([Data] in float dt, ref TimeOfDay time, ref Weather weather)
    {
        bool isNight = DayNightCurves.IsNight(time.CurrentHour);

        // Apply speed modifiers based on weather
        float speedMod = weather.Current switch
        {
            WeatherType.Rain  => 0.85f,
            WeatherType.Storm => 0.7f,
            WeatherType.Snow  => 0.8f,
            WeatherType.Fog   => 0.9f,
            _                 => 1.0f,
        };

        // Apply to all entities with outdoor movement
        var moveQuery = new QueryDescription().WithAll<BaseStats, ComputedStats, OutdoorTag>();
        World.Query(in moveQuery, (ref BaseStats baseStats, ref ComputedStats computed) =>
        {
            computed = computed with { Speed = computed.Speed * speedMod };
        });

        // Night-only spawner activation
        var spawnerQuery = new QueryDescription().WithAll<NightSpawner>();
        World.Query(in spawnerQuery, (ref NightSpawner spawner) =>
        {
            spawner = spawner with { Active = isNight };
        });
    }
}

// Supporting components
public struct OutdoorTag;
public record struct NightSpawner(bool Active, string EnemyPrefabId, float Interval, float Timer);

/// <summary>Crop growth tied to day/night cycle.</summary>
public partial class CropGrowthSystem : BaseSystem<World, float>
{
    public CropGrowthSystem(World world) : base(world) { }

    [Query]
    [All<CropState, Position>]
    public void GrowCrops([Data] in float dt, ref CropState crop)
    {
        if (!crop.IsDay) return; // only grow during daytime

        crop = crop with { GrowthProgress = crop.GrowthProgress + dt * crop.GrowthRate };
        if (crop.GrowthProgress >= crop.GrowthTarget)
        {
            crop = crop with
            {
                Stage = Math.Min(crop.Stage + 1, crop.MaxStage),
                GrowthProgress = 0f
            };
        }
    }
}

public record struct CropState(
    int Stage,
    int MaxStage,
    float GrowthProgress,
    float GrowthTarget,
    float GrowthRate,
    bool IsDay
);
```

---

## Integration Patterns

### System Pipeline (Recommended Order)

```csharp
public static Group BuildGameSystems(World world, GameServices services)
{
    return new Group("GameSystems",
        // Time & Environment (runs first — other systems read time/weather)
        new DayNightSystem(world),
        new WeatherSystem(world),
        new TimeWeatherGameplaySystem(world),

        // Input & Interaction
        new DialogueInteractionSystem(world, services.DialogueManager),
        new UndoRedoSystem(world, services.CommandHistory),

        // Game Logic
        new InventoryPickupSystem(world, services.ItemRegistry),
        new CraftingSystem(world, services.RecipeRegistry, services.ItemRegistry),
        new RecipeDiscoverySystem(world, services.RecipeRegistry),
        new StatusEffectSystem(world),
        new StatComputeSystem(world),
        new QuestKillTrackingSystem(world, services.QuestManager),
        new QuestGiverInteractionSystem(world, services.QuestManager),

        // Spawning
        new WaveSpawnSystem(world, services.WaveDefinitions, services.SpawnEnemy),

        // World Generation (on-demand — only processes GenerateMapRequest)
        new ProcgenSystem(world)
    );
}
```

### Shared Services Container

```csharp
public class GameServices
{
    public ItemRegistry ItemRegistry { get; init; } = new();
    public RecipeRegistry RecipeRegistry { get; init; } = new();
    public DialogueManager DialogueManager { get; init; }
    public DialogueVariables DialogueVariables { get; init; } = new();
    public QuestManager QuestManager { get; init; }
    public CommandHistory CommandHistory { get; init; } = new();
    public SaveManager SaveManager { get; init; }
    public List<WaveDefinition> WaveDefinitions { get; init; } = new();
    public Action<string, Vector2, float> SpawnEnemy { get; init; }

    public GameServices(string saveDir)
    {
        DialogueVariables = new DialogueVariables();
        DialogueManager = new DialogueManager(DialogueVariables);
        QuestManager = new QuestManager();
        SaveManager = new SaveManager(saveDir);
        CommandHistory = new CommandHistory();
        SpawnEnemy = (prefab, pos, diff) => { /* wire to your entity factory */ };
    }
}
```

### Cross-System Event Wiring

```csharp
// In your game initialization:
public void WireEvents(GameServices services)
{
    // Quest completion → dialogue variable
    services.QuestManager.OnQuestCompleted += questId =>
        services.DialogueVariables.Set($"quest_{questId}_done", 1);

    // Dialogue effect → quest activation
    services.DialogueManager.OnEffectsTriggered += effects =>
    {
        foreach (var effect in effects)
        {
            switch (effect.Type)
            {
                case "start_quest":
                    services.QuestManager.TryActivate(effect.Key);
                    break;
                case "set_var":
                    services.DialogueVariables.Set(effect.Key, effect.Value);
                    break;
                case "add_item":
                    // Add to player inventory via ECS
                    break;
            }
        }
    };

    // Wave cleared → quest progress
    var waveSystem = /* get from group */;
    waveSystem.OnWaveCleared += wave =>
        services.QuestManager.ReportProgress("survive_waves", wave + 1);

    // Day/Night → crop system sync
    var dayNightSystem = /* get from group */;
    dayNightSystem.OnHourChanged += hour =>
    {
        bool isDay = !DayNightCurves.IsNight(hour);
        var query = new QueryDescription().WithAll<CropState>();
        // Update all crops with current day/night state
    };
}
```

### Save/Load Integration

```csharp
public void QuickSave(World world, GameServices services, int slot = 0)
{
    var save = services.SaveManager.BuildSaveFile(
        world,
        saveName: $"QuickSave {DateTime.Now:g}",
        sceneName: CurrentScene.Name,
        playTime: _totalPlayTime,
        dialogueVars: services.DialogueVariables,
        questMgr: services.QuestManager
    );
    services.SaveManager.Save(save, slot);
}

public void QuickLoad(World world, GameServices services, int slot = 0)
{
    var save = services.SaveManager.Load(slot);
    if (save == null) return;

    WorldRestorer.RestoreEntities(world, save);
    services.DialogueVariables.Restore(save.DialogueVariables);
    services.QuestManager.Restore(save.Quests);
    LoadScene(save.Metadata.SceneName);
}
```

---

## Architecture Decision Guide

| System | Typical Lines | Best For |
|--------|--------------|----------|
| **Inventory** | 500-800 | RPG, survival, roguelike, any game with items |
| **Dialogue** | 400-600 | RPG, adventure, visual novel, any narrative game |
| **Save/Load** | 300-500 | Any game with persistence |
| **Procgen (BSP)** | ~200 | Roguelike dungeons |
| **Procgen (Cellular Automata)** | ~150 | Caves, organic terrain |
| **Procgen (WFC)** | ~400 | Complex tiled worlds with constraints |
| **Procgen (Noise)** | ~200 | Terrain, biomes, height maps |
| **Crafting** | ~300 | Survival, RPG, sandbox |
| **Quest/Objective** | 400-600 | RPG, adventure, open-world |
| **Status Effects** | 300-500 | RPG, roguelike, ARPG, card games |
| **Undo/Redo** | 100-200 | Puzzle, strategy, level editors |
| **Wave/Spawn** | 200-300 | Tower defense, survival, arena |
| **Day/Night + Weather** | 200-400 | Open-world, farming sim, survival |

**All systems are composable.** An open-world RPG might use all 10. A puzzle game might only need Undo/Redo + Save/Load. Pick what fits your genre from [C1 Genre Reference](../C/C1_genre_reference.md) and wire them through the shared `GameServices` container.
