# G13 — C# Performance


> **Category:** Guide · **Related:** [G15 Game Loop](./G15_game_loop.md) · [G14 Data Structures](./G14_data_structures.md) · [E1 Architecture Overview](../architecture/E1_architecture_overview.md) · [P12 Performance Budget](./P12_performance_budget.md) · [G33 Profiling & Optimization](./G33_profiling_optimization.md)

---

Performance techniques for C# game code. The goal: zero bytes allocated per frame in steady-state gameplay.

---

## Zero-Allocation Techniques

### Span\<T> and stackalloc

Span\<T> is a stack-only ref struct that provides a view over contiguous memory without copying.

```csharp
// BAD: allocates a new array every frame
public void ProcessNeighbors(Vector2 position)
{
    var neighbors = new Vector2[8]; // heap allocation!
    GetNeighbors(position, neighbors);
}

// GOOD: stack-allocated, zero GC pressure
public void ProcessNeighbors(Vector2 position)
{
    Span<Vector2> neighbors = stackalloc Vector2[8];
    GetNeighbors(position, neighbors);
}
```

Keep stackalloc under 1KB. For larger temporary buffers, fall back:
```csharp
Span<byte> buffer = count <= 256
    ? stackalloc byte[count]
    : new byte[count]; // falls back to heap for large allocations
```

### ArrayPool\<T>

Thread-safe, returns arrays in constant time (~44ns). Always return in finally blocks.

```csharp
var pool = ArrayPool<float>.Shared;
float[] buffer = pool.Rent(1024); // May return larger array
try
{
    ProcessData(buffer.AsSpan(0, 1024)); // Don't trust buffer.Length
}
finally
{
    pool.Return(buffer); // ALWAYS return, even on exceptions
}
```

### CollectionsMarshal

Zero-copy access to List internals and single-lookup Dictionary mutations:

```csharp
// Zero-copy Span over a List's internal array
var enemies = new List<Enemy>(100);
Span<Enemy> span = CollectionsMarshal.AsSpan(enemies);
for (int i = 0; i < span.Length; i++)
    span[i].Update(delta); // No bounds checking overhead

// Update dictionary value in-place with single hash lookup
ref var value = ref CollectionsMarshal.GetValueRefOrAddDefault(
    _entityPositions, entityId, out bool exists);
if (!exists) value = new Vector2(0, 0);
value.X += velocity.X * delta; // Mutate in place
```

### FrozenDictionary (.NET 8+)

43-69% faster reads than Dictionary for string keys, but 16x slower to create. Build once at startup:

```csharp
// Perfect for: tile type lookups, sprite atlas mappings, input bindings
var tileTypes = new Dictionary<string, TileData> { /* ... */ }
    .ToFrozenDictionary(); // One-time cost at load
```

---

## Value Type Best Practices

Use structs when: size ≤16 bytes (ideal), lifetime is short/method-scoped, value equality is natural. Game examples: Vector2, Color, TileCoord, DamageInfo, InputFrame.

```csharp
// Good game struct — small, value-semantic
public readonly struct DamageInfo
{
    public readonly int Amount;
    public readonly Vector2 Direction;
    public readonly DamageType Type;

    public DamageInfo(int amount, Vector2 direction, DamageType type)
    { Amount = amount; Direction = direction; Type = type; }
}
```

**Critical rules:**
- Mark structs `readonly struct` to prevent defensive copies when passed with `in`
- Implement `IEquatable<T>` on ALL struct dictionary keys — without it, every lookup boxes the key (~10x penalty)
- Use `ref` returns and `ref` locals to avoid copying large structs
- For GPU interop, use `[StructLayout(LayoutKind.Sequential)]` on vertex structs

**Record structs (C# 10+):** Auto-generate value equality, ToString(), and Deconstruct():
```csharp
public record struct TileCoord(int X, int Y);
public record struct InputFrame(int Tick, Vector2 Movement, bool JumpPressed);
```

**Boxing avoidance:**
```csharp
// BAD: boxes every call via non-generic interface
void Process(IComparable value) { }

// GOOD: generic constraint avoids boxing
void Process<T>(T value) where T : struct, IComparable<T> { }
```

---

## String Handling

Every string interpolation in Update() allocates.

```csharp
// BAD: allocates every frame
hudText = $"Score: {score}";

// GOOD: pre-cache common strings
private readonly string[] _cachedScoreStrings = new string[10000];
// Initialize at startup: _cachedScoreStrings[i] = i.ToString();

// GOOD: Span-based formatting
Span<char> buffer = stackalloc char[32];
score.TryFormat(buffer, out int written);

// GOOD: reuse StringBuilder
private readonly StringBuilder _sb = new(64);
_sb.Clear();
_sb.Append("Score: ").Append(score);
```

---

## Source Generators

Eliminate runtime reflection entirely. System.Text.Json source generator:

```csharp
[JsonSerializable(typeof(WeaponData))]
[JsonSerializable(typeof(LevelData))]
public partial class GameJsonContext : JsonSerializerContext { }

// Usage — zero reflection, AOT-friendly
var weapon = JsonSerializer.Deserialize(json, GameJsonContext.Default.WeaponData);
```

Arch.System.SourceGenerator auto-generates system boilerplate for ECS queries.

---

## GC Pressure Elimination

Gen0 collections are fast (<1ms), but Gen2 full collections can take 10-50ms+ and cause visible frame hitches.

**Measure per-frame allocations:**
```csharp
#if DEBUG
long before = GC.GetAllocatedBytesForCurrentThread();
// ... your frame logic ...
long after = GC.GetAllocatedBytesForCurrentThread();
long frameAlloc = after - before;
if (frameAlloc > 0) Debug.Log($"Frame allocated {frameAlloc} bytes!");
#endif
```

**The three biggest allocation sources:**

1. **LINQ in hot paths:** Each .Where(), .Select() allocates an enumerator. .ToList() allocates a new List. Closures add a display class plus delegate. Replace with manual for loops.

2. **String operations:** Every interpolation in Update() allocates. Cache or use StringBuilder.

3. **Delegate/closure captures:** Lambdas capturing local variables generate a heap-allocated DisplayClass. Use static lambdas (C# 9+) or cache delegates in fields.

```csharp
// BAD: allocates every frame
enemies.Where(e => e.Health > 0).ToList().ForEach(e => e.Update(delta));

// GOOD: zero allocation
for (int i = 0; i < enemies.Count; i++)
    if (enemies[i].Health > 0)
        enemies[i].Update(delta);
```

Objects ≥85,000 bytes go to the Large Object Heap (LOH), collected only during expensive Gen2 collections. Always use ArrayPool for large temporary arrays.

---

## SIMD Acceleration

System.Numerics.Vector2/3/4 auto-targets SSE/AVX instructions — ~35-40% improvement for simple vector operations, free just by using the right types.

```csharp
// Process 4 floats simultaneously with AVX2
public static void BatchMove(float[] positionsX, float[] velocitiesX, float delta, int count)
{
    var deltaVec = new Vector<float>(delta);
    int vectorSize = Vector<float>.Count; // 4 (SSE) or 8 (AVX2)

    int i;
    for (i = 0; i <= count - vectorSize; i += vectorSize)
    {
        var pos = new Vector<float>(positionsX, i);
        var vel = new Vector<float>(velocitiesX, i);
        (pos + vel * deltaVec).CopyTo(positionsX, i);
    }
    for (; i < count; i++) // Scalar remainder
        positionsX[i] += velocitiesX[i] * delta;
}
```

---

## Cache Efficiency

Modern CPUs access L1 cache in ~4 cycles but require 200+ cycles for main memory. Cache lines are typically 64 bytes.

**Structure of Arrays (SoA):** Store each field type in its own array. When a system only needs positions, entire cache lines contain only positions. This is exactly what Arch ECS achieves naturally.

```csharp
// BAD: Array of Structures — cache loads unused fields
struct Entity { Vector2 Position; Vector2 Velocity; int Health; string Name; }
Entity[] entities; // Iterating positions loads Name, Health, etc.

// GOOD: Structure of Arrays — cache-friendly (what Arch does)
float[] positionsX, positionsY;
float[] velocitiesX, velocitiesY;
```

**Hot/cold splitting:** Separate frequently-accessed data (position, velocity, health) from rarely-accessed data (name, metadata, debug info). In Arch, this means putting hot and cold data in separate components.

---

## Custom Allocators

Linear (arena) allocator for frame-temporary data:

```csharp
public class FrameAllocator
{
    private readonly byte[] _buffer;
    private int _offset;

    public FrameAllocator(int capacity) => _buffer = new byte[capacity];

    public Span<T> Alloc<T>(int count) where T : unmanaged
    {
        int size = count * Unsafe.SizeOf<T>();
        int aligned = (_offset + 7) & ~7; // 8-byte alignment
        if (aligned + size > _buffer.Length)
            throw new OutOfMemoryException("Frame allocator exhausted");
        _offset = aligned + size;
        return MemoryMarshal.Cast<byte, T>(_buffer.AsSpan(aligned, size));
    }

    public void Reset() => _offset = 0; // Called at frame end
}
```

---

## Async/Await Pitfalls

Standard async/await is problematic for game logic: each async method allocates a state machine class on the heap, Task objects are heap-allocated, and async tasks don't stop when game objects are destroyed.

**Rule:** Use async exclusively for I/O-bound work (asset loading, network requests, file I/O, scene transitions). For frame-to-frame game logic, use Ellpeck/Coroutine or state machines.

```csharp
// GOOD: async for I/O
public async Task<LevelData> LoadLevelAsync(string path)
{
    var json = await File.ReadAllTextAsync(path);
    return JsonSerializer.Deserialize<LevelData>(json);
}

// GOOD: Coroutine for gameplay sequencing (Ellpeck/Coroutine NuGet)
IEnumerator<Wait> SpawnWaveCoroutine()
{
    for (int i = 0; i < 10; i++)
    {
        SpawnEnemy();
        yield return new Wait(0.5f);
    }
}
```

---

## Common Memory Leak Sources

1. Event handler references — always unsubscribe in Dispose/Destroy
2. Static events — root subscribers forever, never use for game objects
3. Anonymous lambdas as event handlers — can't unsubscribe, avoid
4. Closures over local variables in loops — each iteration allocates
5. Unreturned ArrayPool buffers — ensure return in finally blocks
6. Captured references in async continuations — tasks keep objects alive
