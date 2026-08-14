# G14 — Data Structures


> **Category:** Guide · **Related:** [G13 C# Performance](./G13_csharp_performance.md) · [G3 Physics & Collision](./G3_physics_and_collision.md)

> Game-specific data structures beyond standard collections — spatial queries, ring buffers, object pools, bit manipulation, and ECS-friendly memory layouts with full C# implementations.

---

## 1. Ring Buffer (Circular Buffer)

Fixed-size, zero allocation after initialization, O(1) push/read. Essential for input history, replay systems, frame state buffering, and structured logging.

### Generic Implementation

```csharp
public class RingBuffer<T>
{
    private readonly T[] _buffer;
    private int _head;
    private int _count;

    public RingBuffer(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _buffer = new T[capacity];
    }

    public int Count => _count;
    public int Capacity => _buffer.Length;
    public bool IsFull => _count == _buffer.Length;

    /// <summary>Push an item, overwriting the oldest if full.</summary>
    public void Push(T item)
    {
        _buffer[_head] = item;
        _head = (_head + 1) % _buffer.Length;
        if (_count < _buffer.Length) _count++;
    }

    /// <summary>Index 0 = most recent, 1 = previous, etc.</summary>
    public T this[int ageIndex]
    {
        get
        {
            if ((uint)ageIndex >= (uint)_count)
                throw new IndexOutOfRangeException();
            int index = (_head - 1 - ageIndex + _buffer.Length) % _buffer.Length;
            return _buffer[index];
        }
    }

    /// <summary>Peek at the most recent item without removing.</summary>
    public T PeekNewest() => this[0];

    /// <summary>Peek at the oldest item still in the buffer.</summary>
    public T PeekOldest() => this[_count - 1];

    public void Clear()
    {
        Array.Clear(_buffer);
        _head = 0;
        _count = 0;
    }

    /// <summary>Enumerate from newest to oldest (allocation-free with struct enumerator).</summary>
    public Enumerator GetEnumerator() => new(this);

    public ref struct Enumerator
    {
        private readonly RingBuffer<T> _ring;
        private int _index;

        internal Enumerator(RingBuffer<T> ring) { _ring = ring; _index = -1; }

        public bool MoveNext() => ++_index < _ring._count;
        public T Current => _ring[_index];
    }
}
```

### Usage Patterns

```csharp
// --- Input history for combo detection ---
var inputHistory = new RingBuffer<InputFrame>(60); // 1 second at 60fps

// Each frame:
inputHistory.Push(new InputFrame(buttons, timestamp));

// Check for a combo: ↓ → punch within 15 frames
bool comboDetected =
    inputHistory.Count >= 3 &&
    inputHistory[2].Buttons == Buttons.Down &&
    inputHistory[1].Buttons == Buttons.Right &&
    inputHistory[0].Buttons == Buttons.Punch;

// --- Trail renderer (position history for visual trails) ---
var trail = new RingBuffer<Vector2>(20);
trail.Push(currentPosition); // each frame

// Render trail from newest to oldest with fading alpha
int i = 0;
foreach (var pos in trail)
{
    float alpha = 1f - (float)i / trail.Count;
    DrawCircle(pos, alpha);
    i++;
}

// --- Frame timing profiler ---
var frameTimes = new RingBuffer<float>(120);
frameTimes.Push(deltaTime);

float avgFrameTime = 0f;
foreach (var ft in frameTimes) avgFrameTime += ft;
avgFrameTime /= frameTimes.Count;
float avgFps = 1f / avgFrameTime;
```

**For replay/rewind state buffers:** Reuse game state objects instead of cloning them. The simulation should take two GameState objects (previous and write-here) to avoid GC pressure.

---

## 2. Priority Queue (.NET 6+)

Min-heap with O(log n) enqueue/dequeue, O(1) peek. Built into `System.Collections.Generic`.

### A* Pathfinding (Primary Use)

Insert duplicates and skip stale entries when dequeuing instead of implementing decrease-key:

```csharp
var frontier = new PriorityQueue<Vector2I, float>();
frontier.Enqueue(start, 0f);

while (frontier.Count > 0)
{
    var current = frontier.Dequeue();
    if (current == goal) break;
    if (visited.Contains(current)) continue; // Skip stale duplicates
    visited.Add(current);

    foreach (var next in GetNeighbors(current))
    {
        float newCost = costSoFar[current] + GetMoveCost(current, next);
        if (!costSoFar.ContainsKey(next) || newCost < costSoFar[next])
        {
            costSoFar[next] = newCost;
            float priority = newCost + Heuristic(next, goal);
            frontier.Enqueue(next, priority);
        }
    }
}
```

### Event Scheduling System

Key on scheduled time, dequeue all events where priority ≤ currentTime:

```csharp
public class EventScheduler
{
    private readonly PriorityQueue<GameEvent, float> _queue = new();

    public void Schedule(GameEvent evt, float triggerTime) =>
        _queue.Enqueue(evt, triggerTime);

    public void Update(float currentTime)
    {
        while (_queue.Count > 0 && _queue.TryPeek(out _, out float time) && time <= currentTime)
        {
            var evt = _queue.Dequeue();
            evt.Execute();
        }
    }
}

// Usage:
scheduler.Schedule(new SpawnWaveEvent(waveId: 3), triggerTime: 30.0f);
scheduler.Schedule(new DialogueEvent("Boss incoming!"), triggerTime: 29.0f);
// At time 30.0: both fire in order
```

### Turn-Based Initiative

```csharp
public class TurnManager
{
    private readonly PriorityQueue<Entity, int> _turnOrder = new();

    public void AddCombatant(Entity entity, int initiative) =>
        _turnOrder.Enqueue(entity, -initiative); // Negate: highest initiative goes first (min-heap)

    public Entity GetNextTurn() => _turnOrder.Dequeue();

    public void RequeueAfterTurn(Entity entity, int speed) =>
        _turnOrder.Enqueue(entity, -speed); // Re-insert with current speed as priority
}
```

---

## 3. Bit Flags & Collision Layers

Efficient collision layer filtering — a single bitwise AND determines if two objects should interact in O(1).

### Collision Layer Setup

```csharp
[Flags]
public enum CollisionLayer : uint
{
    None         = 0,
    Player       = 1 << 0,    // 1
    Enemy        = 1 << 1,    // 2
    PlayerBullet = 1 << 2,    // 4
    EnemyBullet  = 1 << 3,    // 8
    Terrain      = 1 << 4,    // 16
    Pickup       = 1 << 5,    // 32
    Trigger      = 1 << 6,    // 64
    NPC          = 1 << 7,    // 128
    Destructible = 1 << 8,    // 256
    // ... up to 1 << 31 for uint (32 layers)
}

// Component: what layer is this entity ON, and what layers does it COLLIDE WITH
public record struct Collider(
    CollisionLayer Layer,
    CollisionLayer Mask,
    float Radius);
```

### Collision Matrix

```csharp
public static class CollisionMatrix
{
    // Define collision rules once — each entry says "this layer collides with these layers"
    private static readonly Dictionary<CollisionLayer, CollisionLayer> Rules = new()
    {
        [CollisionLayer.Player]       = CollisionLayer.Enemy | CollisionLayer.EnemyBullet
                                      | CollisionLayer.Terrain | CollisionLayer.Pickup
                                      | CollisionLayer.Trigger,
        [CollisionLayer.Enemy]        = CollisionLayer.Player | CollisionLayer.PlayerBullet
                                      | CollisionLayer.Terrain,
        [CollisionLayer.PlayerBullet] = CollisionLayer.Enemy | CollisionLayer.Terrain
                                      | CollisionLayer.Destructible,
        [CollisionLayer.EnemyBullet]  = CollisionLayer.Player | CollisionLayer.Terrain,
        [CollisionLayer.Pickup]       = CollisionLayer.Player,
        [CollisionLayer.Trigger]      = CollisionLayer.Player | CollisionLayer.NPC,
    };

    public static CollisionLayer GetMask(CollisionLayer layer) =>
        Rules.GetValueOrDefault(layer, CollisionLayer.None);

    // One-instruction check
    public static bool ShouldCollide(Collider a, Collider b) =>
        (a.Layer & b.Mask) != 0 && (b.Layer & a.Mask) != 0;
}
```

### ECS Component Masks

```csharp
// Match entities to systems — single AND instruction
[Flags]
public enum ComponentMask : ulong
{
    None      = 0,
    Position  = 1 << 0,
    Velocity  = 1 << 1,
    Health    = 1 << 2,
    AiState   = 1 << 3,
    Collider  = 1 << 4,
    Sprite    = 1 << 5,
    // ... up to 64 components with ulong
}

// System requires Position + Velocity + Collider
const ComponentMask Required = ComponentMask.Position | ComponentMask.Velocity | ComponentMask.Collider;
bool matches = (entity.Mask & Required) == Required;
```

### Bit Manipulation Utilities

```csharp
public static class BitOps
{
    public static bool HasFlag(uint flags, int bit) => (flags & (1u << bit)) != 0;
    public static uint SetFlag(uint flags, int bit) => flags | (1u << bit);
    public static uint ClearFlag(uint flags, int bit) => flags & ~(1u << bit);
    public static uint ToggleFlag(uint flags, int bit) => flags ^ (1u << bit);
    public static int CountBits(uint flags) => System.Numerics.BitOperations.PopCount(flags);

    // Iterate set bits without checking all 32
    public static void ForEachSetBit(uint flags, Action<int> action)
    {
        while (flags != 0)
        {
            int bit = System.Numerics.BitOperations.TrailingZeroCount(flags);
            action(bit);
            flags &= flags - 1; // Clear lowest set bit
        }
    }
}
```

`HasFlag()` in .NET Core+ is JIT-optimized to a bitwise AND (no longer boxes like pre-.NET Core).

---

## 4. Spatial Data Structures

### Choosing the Right One

| Structure | Insert | Query | Memory | Best For |
|-----------|--------|-------|--------|----------|
| **Brute force** | — | O(n²) | None | < 100 entities |
| **Uniform grid** | O(1) | O(1) per cell | Fixed | Fixed-size worlds, uniform entities |
| **Spatial hash** | O(1) | O(k) neighbors | Dynamic | Most 2D games, varied world sizes |
| **Quadtree** | O(log n) | O(log n + k) | Dynamic | Mixed-size entities, range queries |
| **Loose quadtree** | O(log n) | O(log n + k) | Dynamic | RTS, varied-size entities |

**By entity count:**
- **~100 entities:** Brute-force O(n²) is only 10,000 checks — likely faster than maintaining any structure
- **~1,000 entities:** Spatial hashing wins handily
- **~10,000+ entities:** Spatial hashing dominates for uniform-size objects; loose quadtrees for varied-size

### Spatial Hash (Full Implementation)

```csharp
public class SpatialHash<T>
{
    private readonly float _cellSize;
    private readonly float _inverseCellSize;
    private readonly Dictionary<long, List<(Vector2 Pos, T Item)>> _cells = new();

    public SpatialHash(float cellSize)
    {
        _cellSize = cellSize;
        _inverseCellSize = 1f / cellSize;
    }

    public void Clear()
    {
        foreach (var list in _cells.Values) list.Clear();
        // Don't new() the lists — reuse to avoid GC
    }

    public void Insert(Vector2 pos, T item)
    {
        var key = CellKey(pos);
        if (!_cells.TryGetValue(key, out var list))
            _cells[key] = list = new(8);
        list.Add((pos, item));
    }

    /// <summary>Insert an AABB that may span multiple cells.</summary>
    public void InsertAABB(Vector2 min, Vector2 max, T item)
    {
        int x0 = (int)MathF.Floor(min.X * _inverseCellSize);
        int x1 = (int)MathF.Floor(max.X * _inverseCellSize);
        int y0 = (int)MathF.Floor(min.Y * _inverseCellSize);
        int y1 = (int)MathF.Floor(max.Y * _inverseCellSize);

        for (int x = x0; x <= x1; x++)
        for (int y = y0; y <= y1; y++)
        {
            var key = PackKey(x, y);
            if (!_cells.TryGetValue(key, out var list))
                _cells[key] = list = new(4);
            list.Add((min, item)); // Position is approximate; fine for broad phase
        }
    }

    public void QueryRadius(Vector2 center, float radius, List<T> results)
    {
        int minX = (int)MathF.Floor((center.X - radius) * _inverseCellSize);
        int maxX = (int)MathF.Floor((center.X + radius) * _inverseCellSize);
        int minY = (int)MathF.Floor((center.Y - radius) * _inverseCellSize);
        int maxY = (int)MathF.Floor((center.Y + radius) * _inverseCellSize);
        float r2 = radius * radius;

        for (int x = minX; x <= maxX; x++)
        for (int y = minY; y <= maxY; y++)
        {
            if (_cells.TryGetValue(PackKey(x, y), out var list))
                foreach (var (pos, item) in list)
                    if (Vector2.DistanceSquared(center, pos) <= r2)
                        results.Add(item);
        }
    }

    public void QueryCell(Vector2 pos, List<T> results)
    {
        if (_cells.TryGetValue(CellKey(pos), out var list))
            foreach (var (_, item) in list)
                results.Add(item);
    }

    private long CellKey(Vector2 p) =>
        PackKey((int)MathF.Floor(p.X * _inverseCellSize),
                (int)MathF.Floor(p.Y * _inverseCellSize));

    private static long PackKey(int x, int y) => ((long)x << 32) | (uint)y;
}
```

### Uniform Grid (Fixed-Size World)

Faster than spatial hash for bounded worlds — array indexing instead of dictionary lookup:

```csharp
public class UniformGrid<T>
{
    private readonly List<T>?[,] _cells;
    private readonly float _cellSize;
    private readonly float _inverseCellSize;
    public int Width { get; }
    public int Height { get; }

    public UniformGrid(int worldWidth, int worldHeight, float cellSize)
    {
        _cellSize = cellSize;
        _inverseCellSize = 1f / cellSize;
        Width = (int)MathF.Ceiling(worldWidth * _inverseCellSize);
        Height = (int)MathF.Ceiling(worldHeight * _inverseCellSize);
        _cells = new List<T>?[Width, Height];
    }

    public void Clear()
    {
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
            _cells[x, y]?.Clear();
    }

    public void Insert(Vector2 pos, T item)
    {
        int cx = Math.Clamp((int)(pos.X * _inverseCellSize), 0, Width - 1);
        int cy = Math.Clamp((int)(pos.Y * _inverseCellSize), 0, Height - 1);
        _cells[cx, cy] ??= new(4);
        _cells[cx, cy]!.Add(item);
    }

    public void QueryRadius(Vector2 center, float radius, List<T> results)
    {
        int minX = Math.Max(0, (int)((center.X - radius) * _inverseCellSize));
        int maxX = Math.Min(Width - 1, (int)((center.X + radius) * _inverseCellSize));
        int minY = Math.Max(0, (int)((center.Y - radius) * _inverseCellSize));
        int maxY = Math.Min(Height - 1, (int)((center.Y + radius) * _inverseCellSize));
        float r2 = radius * radius;

        for (int x = minX; x <= maxX; x++)
        for (int y = minY; y <= maxY; y++)
        {
            var list = _cells[x, y];
            if (list == null) continue;
            foreach (var item in list)
                results.Add(item); // Caller does fine-grained distance check
        }
    }
}
```

### Quadtree

Best for mixed-size entities and when you need subdivision-aware range queries:

```csharp
public class Quadtree<T>
{
    private const int MaxItems = 8;
    private const int MaxDepth = 8;

    private readonly Rectangle _bounds;
    private readonly int _depth;
    private readonly List<(Rectangle Rect, T Item)> _items = new();
    private Quadtree<T>?[]? _children;

    public Quadtree(Rectangle bounds, int depth = 0)
    {
        _bounds = bounds;
        _depth = depth;
    }

    public void Clear()
    {
        _items.Clear();
        _children = null;
    }

    public void Insert(Rectangle rect, T item)
    {
        if (!_bounds.Intersects(rect)) return;

        if (_children != null)
        {
            foreach (var child in _children)
                child?.Insert(rect, item);
            return;
        }

        _items.Add((rect, item));

        if (_items.Count > MaxItems && _depth < MaxDepth)
            Subdivide();
    }

    public void Query(Rectangle area, List<T> results)
    {
        if (!_bounds.Intersects(area)) return;

        foreach (var (rect, item) in _items)
            if (rect.Intersects(area))
                results.Add(item);

        if (_children != null)
            foreach (var child in _children)
                child?.Query(area, results);
    }

    private void Subdivide()
    {
        int hw = _bounds.Width / 2, hh = _bounds.Height / 2;
        int x = _bounds.X, y = _bounds.Y;

        _children = new Quadtree<T>?[4];
        _children[0] = new(new Rectangle(x, y, hw, hh), _depth + 1);
        _children[1] = new(new Rectangle(x + hw, y, hw, hh), _depth + 1);
        _children[2] = new(new Rectangle(x, y + hh, hw, hh), _depth + 1);
        _children[3] = new(new Rectangle(x + hw, y + hh, hw, hh), _depth + 1);

        // Re-insert existing items into children
        foreach (var item in _items)
            foreach (var child in _children)
                child?.Insert(item.Rect, item.Item);

        _items.Clear();
    }
}
```

### Spatial Structure Comparison

| Scenario | Recommendation |
|----------|---------------|
| Platformer (< 100 colliders) | Brute force or simple grid |
| Bullet hell (thousands of same-size projectiles) | Spatial hash, cell = 2× bullet size |
| RTS (varied unit sizes, range queries) | Loose quadtree |
| Cellular automata (fixed positions) | Direct grid indexing |
| Open world (large, sparse) | Spatial hash (dynamic, no bounds needed) |
| Top-down RPG (medium density) | Spatial hash |

**Cell size tuning:** Set cell size to ~2× the radius of the most common entity. Too small = entities span multiple cells. Too large = cells contain too many entities to filter.

---

## 5. Object Pools

Avoid GC pressure from frequent allocations — bullets, particles, damage numbers, VFX.

### Generic Object Pool

```csharp
public class ObjectPool<T> where T : class
{
    private readonly Stack<T> _available;
    private readonly Func<T> _factory;
    private readonly Action<T>? _onGet;
    private readonly Action<T>? _onReturn;

    public int CountAvailable => _available.Count;
    public int CountActive { get; private set; }

    public ObjectPool(Func<T> factory, int prewarm = 0,
        Action<T>? onGet = null, Action<T>? onReturn = null)
    {
        _factory = factory;
        _onGet = onGet;
        _onReturn = onReturn;
        _available = new Stack<T>(prewarm);

        for (int i = 0; i < prewarm; i++)
            _available.Push(_factory());
    }

    public T Get()
    {
        var item = _available.Count > 0 ? _available.Pop() : _factory();
        _onGet?.Invoke(item);
        CountActive++;
        return item;
    }

    public void Return(T item)
    {
        _onReturn?.Invoke(item);
        _available.Push(item);
        CountActive--;
    }
}
```

### Pooled Bullet System (ECS Pattern)

In Arch ECS, pool the component data rather than objects — entities are just IDs:

```csharp
// Instead of pooling objects, reuse entities by toggling an Active flag
public record struct Bullet(Vector2 Position, Vector2 Velocity, float Lifetime, bool Active);

public class BulletPool
{
    private readonly World _world;
    private readonly Stack<Entity> _inactive = new();

    public BulletPool(World world, int prewarm = 200)
    {
        _world = world;
        for (int i = 0; i < prewarm; i++)
        {
            var e = _world.Create(new Bullet(Vector2.Zero, Vector2.Zero, 0f, Active: false));
            _inactive.Push(e);
        }
    }

    public Entity Spawn(Vector2 position, Vector2 velocity, float lifetime)
    {
        Entity entity;
        if (_inactive.Count > 0)
        {
            entity = _inactive.Pop();
            _world.Set(entity, new Bullet(position, velocity, lifetime, Active: true));
        }
        else
        {
            entity = _world.Create(new Bullet(position, velocity, lifetime, Active: true));
        }
        return entity;
    }

    public void Despawn(Entity entity)
    {
        _world.Set(entity, new Bullet(Vector2.Zero, Vector2.Zero, 0f, Active: false));
        _inactive.Push(entity);
    }
}
```

### Array-Based Pool (Zero GC)

For value types and hot-path allocation:

```csharp
public struct ArrayPool<T> where T : struct
{
    private readonly T[] _items;
    private readonly bool[] _active;
    private int _nextFree;

    public ArrayPool(int capacity)
    {
        _items = new T[capacity];
        _active = new bool[capacity];
        _nextFree = 0;
    }

    public int Rent(out T item)
    {
        // Linear scan for next free slot (amortized O(1) if returns are frequent)
        for (int i = 0; i < _items.Length; i++)
        {
            int idx = (_nextFree + i) % _items.Length;
            if (!_active[idx])
            {
                _active[idx] = true;
                item = _items[idx];
                _nextFree = (idx + 1) % _items.Length;
                return idx;
            }
        }
        throw new InvalidOperationException("Pool exhausted");
    }

    public ref T GetRef(int handle) => ref _items[handle];

    public void Return(int handle)
    {
        _active[handle] = false;
        _items[handle] = default;
    }
}

// Usage:
var particles = new ArrayPool<Particle>(10_000);
int handle = particles.Rent(out _);
ref var p = ref particles.GetRef(handle);
p.Position = spawnPos;
p.Velocity = direction * speed;
p.Life = 1.0f;
```

---

## 6. ECS-Friendly Data Layouts

### Structure of Arrays (SoA) vs Array of Structures (AoS)

Arch ECS uses a chunk-based SoA layout internally, but understanding the principle helps when designing components and custom systems:

```csharp
// --- AoS: Array of Structures (typical OOP) ---
// Each entity is a struct with all fields together
struct EntityAoS
{
    public float PositionX, PositionY;  // 8 bytes
    public float VelocityX, VelocityY;  // 8 bytes
    public int Health;                   // 4 bytes
    public int Armor;                    // 4 bytes
    // Accessing Position loads Health/Armor into cache too — waste
}
EntityAoS[] entities = new EntityAoS[10_000];

// --- SoA: Structure of Arrays ---
// Each field is a separate contiguous array
struct EntityArrays
{
    public float[] PositionX;  // All X positions contiguous
    public float[] PositionY;  // All Y positions contiguous
    public float[] VelocityX;
    public float[] VelocityY;
    public int[] Health;
    public int[] Armor;
}

// Movement system only touches Position + Velocity arrays
// → Perfect cache utilization, no wasted bytes loaded
for (int i = 0; i < count; i++)
{
    arrays.PositionX[i] += arrays.VelocityX[i] * dt;
    arrays.PositionY[i] += arrays.VelocityY[i] * dt;
}
```

### Why This Matters in Arch

Arch stores components in archetypes — all entities with the same set of components are stored contiguously. Within an archetype, each component type is a separate array (SoA).

**Design implications:**

```csharp
// BAD: One fat component — systems that only need Position still load everything
public record struct EntityData(
    float X, float Y,
    float VelX, float VelY,
    int Health, int Armor,
    AiStateId State, float StateTimer);

// GOOD: Split into focused components — Arch stores each as a separate array
public record struct Position(float X, float Y);           // 8 bytes
public record struct Velocity(float X, float Y);           // 8 bytes
public record struct Health(int Current, int Max);          // 8 bytes
public record struct AiState(AiStateId Current, float Timer); // 8 bytes

// Movement system queries [Position, Velocity] — only those two arrays are loaded
// Health system queries [Health] — only that array is loaded
// Each system gets perfect cache behavior for its data
```

### Hot/Cold Splitting

Separate frequently accessed (hot) data from rarely accessed (cold) data:

```csharp
// HOT — accessed every frame by movement, rendering, collision
public record struct Position(float X, float Y);
public record struct Velocity(float X, float Y);
public record struct Sprite(int TextureId, Rectangle SourceRect);

// WARM — accessed regularly but not every frame
public record struct Health(int Current, int Max);
public record struct AiState(AiStateId Current, float Timer);

// COLD — accessed rarely (save/load, debug, editor)
public record struct EntityMeta(string Name, int SpawnerId, float CreatedAt);
public record struct DebugInfo(int FrameCreated, string SpawnReason);

// Arch handles this naturally: entities with different component sets
// live in different archetypes. Don't add cold components to hot entities
// unless you need them — it changes the archetype and splits the arrays.
```

### Avoiding Archetype Fragmentation

```csharp
// BAD: Adding/removing tag components frequently causes entity moves between archetypes
// Each add/remove copies ALL components to a new chunk
world.Add<Stunned>(entity);    // Move entity to [Position, Health, Stunned] archetype
world.Remove<Stunned>(entity); // Move back to [Position, Health] archetype

// BETTER: Use a field in an existing component
public record struct StatusEffects(StatusFlags Flags, float StunTimer);

[Flags]
public enum StatusFlags : byte
{
    None    = 0,
    Stunned = 1 << 0,
    Burning = 1 << 1,
    Frozen  = 1 << 2,
    Poisoned = 1 << 3,
}

// Toggle stun without changing archetype — zero cost
ref var status = ref world.Get<StatusEffects>(entity);
status.Flags |= StatusFlags.Stunned;
status.StunTimer = 2.0f;

// System check:
if ((status.Flags & StatusFlags.Stunned) != 0) { /* skip AI update */ }
```

---

## 7. Specialized Game Structures

### Sparse Set

O(1) add/remove/contains, O(n) iteration over dense array. Used internally by many ECS implementations:

```csharp
public class SparseSet
{
    private readonly int[] _sparse;   // entity ID → dense index
    private readonly int[] _dense;    // packed array of active entity IDs
    private int _count;

    public SparseSet(int maxEntities)
    {
        _sparse = new int[maxEntities];
        _dense = new int[maxEntities];
        Array.Fill(_sparse, -1);
    }

    public int Count => _count;
    public ReadOnlySpan<int> Values => _dense.AsSpan(0, _count);

    public bool Contains(int id) =>
        (uint)id < (uint)_sparse.Length &&
        _sparse[id] >= 0 && _sparse[id] < _count &&
        _dense[_sparse[id]] == id;

    public void Add(int id)
    {
        if (Contains(id)) return;
        _sparse[id] = _count;
        _dense[_count] = id;
        _count++;
    }

    public void Remove(int id)
    {
        if (!Contains(id)) return;
        int denseIdx = _sparse[id];
        int last = _dense[_count - 1];
        _dense[denseIdx] = last;
        _sparse[last] = denseIdx;
        _sparse[id] = -1;
        _count--;
    }
}
```

### Free List (Index Recycling)

Recycle integer handles without fragmentation — perfect for managing pool indices:

```csharp
public class FreeList
{
    private readonly Stack<int> _free = new();
    private int _next;

    public int Allocate() => _free.Count > 0 ? _free.Pop() : _next++;
    public void Release(int id) => _free.Push(id);
    public int HighWaterMark => _next;
}

// Usage: stable entity handles that survive add/remove cycles
var handles = new FreeList();
int h1 = handles.Allocate(); // 0
int h2 = handles.Allocate(); // 1
handles.Release(h1);
int h3 = handles.Allocate(); // 0 (recycled)
```

### Flat 2D Array Helpers

Avoid jagged arrays (`T[][]`) — use flat arrays with index math for cache-friendly 2D grids:

```csharp
public class Grid<T>
{
    private readonly T[] _data;
    public int Width { get; }
    public int Height { get; }

    public Grid(int width, int height)
    {
        Width = width;
        Height = height;
        _data = new T[width * height];
    }

    public ref T this[int x, int y] => ref _data[y * Width + x];

    public bool InBounds(int x, int y) =>
        (uint)x < (uint)Width && (uint)y < (uint)Height;

    public Span<T> GetRow(int y) => _data.AsSpan(y * Width, Width);

    public void Fill(T value) => Array.Fill(_data, value);

    // Flat index for serialization or parallel processing
    public int FlatIndex(int x, int y) => y * Width + x;
    public (int X, int Y) FromFlat(int index) => (index % Width, index / Width);
}

// Usage:
var tilemap = new Grid<TileId>(256, 256);
tilemap[10, 20] = TileId.Grass;

// Row-by-row processing is cache-friendly (contiguous memory)
for (int y = 0; y < tilemap.Height; y++)
{
    var row = tilemap.GetRow(y);
    for (int x = 0; x < row.Length; x++)
        ProcessTile(x, y, row[x]);
}
```

---

## 8. When to Use What — Decision Guide

| Need | Structure | Why |
|------|-----------|-----|
| Input history, trails, frame buffer | **Ring Buffer** | O(1), fixed size, no allocation |
| A* open set, event scheduling | **Priority Queue** | O(log n) insert/extract-min |
| Collision filtering (layers) | **Bit Flags** | O(1) bitwise AND |
| Broad-phase collision (uniform entities) | **Spatial Hash** | O(1) insert, dynamic size |
| Broad-phase collision (varied sizes) | **Quadtree** | Handles mixed sizes well |
| Bounded world, tile-based | **Uniform Grid** | Array indexing, fastest lookup |
| Bullet/particle reuse | **Object Pool** | Zero GC after warmup |
| Hot-path value type reuse | **Array Pool** | No boxing, ref access |
| ECS-like membership tracking | **Sparse Set** | O(1) add/remove/contains |
| Handle/ID recycling | **Free List** | Stable indices across time |
| 2D tile/map data | **Flat Grid** | Cache-friendly, Span-compatible |
| Component data for systems | **SoA via Arch** | Automatic, per-archetype arrays |

### Performance Rules of Thumb

- **Cache line = 64 bytes.** Design hot structs to fit within one or two cache lines.
- **Sequential > Random.** Iterating a flat array is 10-100× faster than chasing pointers in a tree.
- **Dictionary lookup** costs ~30-80ns. Array index costs ~1ns. Use arrays when keys are dense integers.
- **List<T> vs T[]:** No performance difference for iteration. `List<T>` has bounds checking overhead on add. Use `CollectionsMarshal.AsSpan(list)` to get a `Span<T>` for zero-overhead iteration.
- **Stack vs Heap:** `record struct` components live inline in Arch chunks (stack-like). `class` components add indirection. Prefer `record struct` for components.
- **Measure before optimizing.** Use BenchmarkDotNet ([G17 Testing](./G17_testing.md)) — intuition about performance is often wrong.
