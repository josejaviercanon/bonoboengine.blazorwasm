# G53 — Procedural Generation



> **Category:** Guide · **Related:** [G37 Tilemap Systems](./G37_tilemap_systems.md) · [G14 Data Structures](./G14_data_structures.md) · [G40 Pathfinding](./G40_pathfinding.md) · [E7 Emergent Puzzle Design](../../core/game-design/E7_emergent_puzzle_design.md) · [G10 Custom Game Systems](./G10_custom_game_systems.md)

Procedural generation turns algorithms into content — dungeons, terrain, loot, enemy encounters. This guide covers the core techniques, all wired into MonoGame + Arch ECS, with deterministic seeding so every run is reproducible.

---

## Table of Contents

1. [Seeded Random](#1-seeded-random)
2. [Noise Functions](#2-noise-functions)
3. [BSP Dungeon Generation](#3-bsp-dungeon-generation)
4. [Cellular Automata](#4-cellular-automata)
5. [Wave Function Collapse](#5-wave-function-collapse-wfc)
6. [Room Templates & Handcrafted Chunks](#6-room-templates--handcrafted-chunks)
7. [Random Walk / Drunkard's Walk](#7-random-walk--drunkards-walk)
8. [Terrain Generation](#8-terrain-generation)
9. [Item & Loot Generation](#9-item--loot-generation)
10. [Enemy Placement](#10-enemy-placement)
11. [Validation & Guarantees](#11-validation--guarantees)
12. [ECS Integration](#12-ecs-integration)
13. [Island / Archipelago Generation](#13-island--archipelago-generation)

---

## 1 — Seeded Random

Every procedural system must be deterministic. Same seed → same world. This lets players share seeds, enables replays, and makes bugs reproducible.

### Core Wrapper

```csharp
public sealed class SeededRandom
{
    public int Seed { get; }
    private Random _rng;

    public SeededRandom(int seed)
    {
        Seed = seed;
        _rng = new Random(seed);
    }

    public void Reset() => _rng = new Random(Seed);

    public int Next(int max) => _rng.Next(max);
    public int Next(int min, int max) => _rng.Next(min, max);
    public float NextFloat() => (float)_rng.NextDouble();
    public float NextFloat(float min, float max) => min + (max - min) * NextFloat();
    public bool NextBool(float chance = 0.5f) => NextFloat() < chance;

    public T Pick<T>(ReadOnlySpan<T> items) => items[Next(items.Length)];

    /// <summary>Weighted random selection. Returns index.</summary>
    public int WeightedIndex(ReadOnlySpan<float> weights)
    {
        float total = 0f;
        foreach (var w in weights) total += w;
        float roll = NextFloat() * total;
        float acc = 0f;
        for (int i = 0; i < weights.Length; i++)
        {
            acc += weights[i];
            if (roll < acc) return i;
        }
        return weights.Length - 1;
    }

    /// <summary>Fisher-Yates shuffle in place.</summary>
    public void Shuffle<T>(Span<T> span)
    {
        for (int i = span.Length - 1; i > 0; i--)
        {
            int j = Next(i + 1);
            (span[i], span[j]) = (span[j], span[i]);
        }
    }

    /// <summary>Derive a child seed for sub-generators (biome, loot, etc.).</summary>
    public int DeriveChildSeed(int channel) => unchecked(Seed * 31 + channel);
}
```

### Seed Display for Players

```csharp
// Convert seed to a readable alphanumeric string for sharing
public static string SeedToDisplay(int seed)
{
    const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no ambiguous I/1/O/0
    Span<char> buf = stackalloc char[8];
    uint v = unchecked((uint)seed);
    for (int i = 0; i < 8; i++)
    {
        buf[i] = chars[(int)(v % (uint)chars.Length)];
        v /= (uint)chars.Length;
    }
    return new string(buf);
}

public static int DisplayToSeed(ReadOnlySpan<char> display)
{
    const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    uint v = 0, mult = 1;
    for (int i = 0; i < display.Length; i++)
    {
        int idx = chars.IndexOf(display[i]);
        if (idx < 0) idx = 0;
        v += (uint)idx * mult;
        mult *= (uint)chars.Length;
    }
    return unchecked((int)v);
}
```

**Key rule:** Never mix `System.Random` instances across systems. Each subsystem (dungeon layout, loot, enemy placement) should derive its own child seed from the master seed so they stay independent.

---

## 2 — Noise Functions

Noise provides smooth, continuous randomness — essential for terrain, biomes, and organic shapes.

### Perlin Noise (2D)

```csharp
public static class PerlinNoise
{
    private static readonly int[] Perm = new int[512];

    static PerlinNoise()
    {
        int[] p = new int[256];
        for (int i = 0; i < 256; i++) p[i] = i;
        var rng = new Random(0);
        for (int i = 255; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (p[i], p[j]) = (p[j], p[i]);
        }
        for (int i = 0; i < 512; i++) Perm[i] = p[i & 255];
    }

    private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);
    private static float Lerp(float a, float b, float t) => a + t * (b - a);

    private static float Grad(int hash, float x, float y)
    {
        int h = hash & 3;
        float u = h < 2 ? x : y;
        float v = h < 2 ? y : x;
        return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
    }

    public static float Sample(float x, float y)
    {
        int xi = (int)MathF.Floor(x) & 255;
        int yi = (int)MathF.Floor(y) & 255;
        float xf = x - MathF.Floor(x);
        float yf = y - MathF.Floor(y);
        float u = Fade(xf), v = Fade(yf);

        int aa = Perm[Perm[xi] + yi];
        int ab = Perm[Perm[xi] + yi + 1];
        int ba = Perm[Perm[xi + 1] + yi];
        int bb = Perm[Perm[xi + 1] + yi + 1];

        return Lerp(
            Lerp(Grad(aa, xf, yf), Grad(ba, xf - 1f, yf), u),
            Lerp(Grad(ab, xf, yf - 1f), Grad(bb, xf - 1f, yf - 1f), u),
            v);
    }
}
```

### Fractal / Octave Noise

Layer multiple frequencies for natural-looking results:

```csharp
public static class FractalNoise
{
    /// <param name="octaves">Layers of detail (4-6 typical).</param>
    /// <param name="lacunarity">Frequency multiplier per octave (2.0 typical).</param>
    /// <param name="persistence">Amplitude multiplier per octave (0.5 typical).</param>
    public static float Sample(float x, float y, int octaves = 4,
        float lacunarity = 2f, float persistence = 0.5f)
    {
        float value = 0f, amplitude = 1f, frequency = 1f, maxAmp = 0f;
        for (int i = 0; i < octaves; i++)
        {
            value += PerlinNoise.Sample(x * frequency, y * frequency) * amplitude;
            maxAmp += amplitude;
            amplitude *= persistence;
            frequency *= lacunarity;
        }
        return value / maxAmp; // Normalize to roughly [-1, 1]
    }
}
```

**Library alternative:** For production, consider [FastNoiseLite](https://github.com/Auburn/FastNoiseLite) — single-file C# drop-in with Perlin, Simplex, OpenSimplex2, Cellular, and Value noise, all with fractal support and SIMD optimizations.

### When to Use Each Noise Type

| Noise | Best For |
|-------|----------|
| **Perlin** | Terrain heightmaps, smooth gradients |
| **Simplex** | Higher dimensions, less directional artifacts |
| **Value** | Blockier results, retro aesthetics |
| **Cellular/Voronoi** | Biome borders, crystal patterns, cracked ground |

---

## 3 — BSP Dungeon Generation

Binary Space Partitioning recursively splits space into rooms, producing well-distributed dungeon layouts.

```csharp
public sealed class BspNode
{
    public Rectangle Bounds;
    public BspNode? Left, Right;
    public Rectangle? Room;

    public bool IsLeaf => Left == null && Right == null;
}

public sealed class BspDungeonGenerator
{
    private readonly SeededRandom _rng;
    private const int MinNodeSize = 8;
    private const int RoomPadding = 2;

    public BspDungeonGenerator(SeededRandom rng) => _rng = rng;

    public (int[,] tiles, List<Rectangle> rooms) Generate(int width, int height)
    {
        var tiles = new int[width, height]; // 0 = wall, 1 = floor
        var root = new BspNode { Bounds = new Rectangle(0, 0, width, height) };
        Split(root);

        var rooms = new List<Rectangle>();
        CreateRooms(root, rooms);
        foreach (var room in rooms)
            CarveRoom(tiles, room);

        ConnectRooms(tiles, root);
        return (tiles, rooms);
    }

    private void Split(BspNode node)
    {
        if (node.Bounds.Width < MinNodeSize * 2 && node.Bounds.Height < MinNodeSize * 2)
            return;

        bool splitH = _rng.NextBool();
        if (node.Bounds.Width > node.Bounds.Height * 1.25f) splitH = false;
        if (node.Bounds.Height > node.Bounds.Width * 1.25f) splitH = true;

        int max = (splitH ? node.Bounds.Height : node.Bounds.Width) - MinNodeSize;
        if (max < MinNodeSize) return;

        int split = _rng.Next(MinNodeSize, max);

        if (splitH)
        {
            node.Left = new BspNode
            {
                Bounds = new Rectangle(node.Bounds.X, node.Bounds.Y,
                    node.Bounds.Width, split)
            };
            node.Right = new BspNode
            {
                Bounds = new Rectangle(node.Bounds.X, node.Bounds.Y + split,
                    node.Bounds.Width, node.Bounds.Height - split)
            };
        }
        else
        {
            node.Left = new BspNode
            {
                Bounds = new Rectangle(node.Bounds.X, node.Bounds.Y,
                    split, node.Bounds.Height)
            };
            node.Right = new BspNode
            {
                Bounds = new Rectangle(node.Bounds.X + split, node.Bounds.Y,
                    node.Bounds.Width - split, node.Bounds.Height)
            };
        }

        Split(node.Left);
        Split(node.Right);
    }

    private void CreateRooms(BspNode node, List<Rectangle> rooms)
    {
        if (!node.IsLeaf)
        {
            if (node.Left != null) CreateRooms(node.Left, rooms);
            if (node.Right != null) CreateRooms(node.Right, rooms);
            return;
        }

        int w = _rng.Next(node.Bounds.Width / 2, node.Bounds.Width - RoomPadding);
        int h = _rng.Next(node.Bounds.Height / 2, node.Bounds.Height - RoomPadding);
        int x = node.Bounds.X + _rng.Next(1, node.Bounds.Width - w - 1);
        int y = node.Bounds.Y + _rng.Next(1, node.Bounds.Height - h - 1);

        var room = new Rectangle(x, y, w, h);
        node.Room = room;
        rooms.Add(room);
    }

    private Point GetRoomCenter(BspNode node)
    {
        if (node.Room.HasValue)
            return node.Room.Value.Center;
        if (node.Left != null) return GetRoomCenter(node.Left);
        if (node.Right != null) return GetRoomCenter(node.Right);
        return node.Bounds.Center;
    }

    private void ConnectRooms(int[,] tiles, BspNode node)
    {
        if (node.IsLeaf) return;
        if (node.Left != null && node.Right != null)
        {
            ConnectRooms(tiles, node.Left);
            ConnectRooms(tiles, node.Right);

            var a = GetRoomCenter(node.Left);
            var b = GetRoomCenter(node.Right);
            CarveCorridor(tiles, a, b);
        }
    }

    private void CarveCorridor(int[,] tiles, Point a, Point b)
    {
        // L-shaped corridor
        int x = a.X, y = a.Y;
        while (x != b.X)
        {
            tiles[x, y] = 1;
            x += x < b.X ? 1 : -1;
        }
        while (y != b.Y)
        {
            tiles[x, y] = 1;
            y += y < b.Y ? 1 : -1;
        }
    }

    private void CarveRoom(int[,] tiles, Rectangle room)
    {
        for (int x = room.X; x < room.X + room.Width; x++)
            for (int y = room.Y; y < room.Y + room.Height; y++)
                if (x > 0 && x < tiles.GetLength(0) - 1 &&
                    y > 0 && y < tiles.GetLength(1) - 1)
                    tiles[x, y] = 1;
    }
}
```

---

## 4 — Cellular Automata

Produces organic cave systems by simulating simple growth rules on a random grid.

```csharp
public sealed class CellularAutomataGenerator
{
    private readonly SeededRandom _rng;

    public CellularAutomataGenerator(SeededRandom rng) => _rng = rng;

    /// <param name="fillChance">Initial wall probability (0.45–0.55 typical).</param>
    /// <param name="iterations">Smoothing passes (4-5 typical).</param>
    public int[,] Generate(int width, int height, float fillChance = 0.48f,
        int iterations = 5)
    {
        var map = new int[width, height]; // 0 = floor, 1 = wall

        // Step 1: Random fill
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                map[x, y] = (x == 0 || x == width - 1 || y == 0 || y == height - 1)
                    ? 1
                    : _rng.NextBool(fillChance) ? 1 : 0;

        // Step 2: Iterate
        for (int i = 0; i < iterations; i++)
            map = Step(map, width, height);

        // Step 3: Ensure connectivity
        EnsureConnectivity(map, width, height);

        return map;
    }

    private int[,] Step(int[,] map, int w, int h)
    {
        var next = new int[w, h];
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                int walls = CountNeighborWalls(map, x, y, w, h);
                // Birth: empty cell with 5+ wall neighbors becomes wall
                // Survival: wall cell stays wall if 4+ wall neighbors
                next[x, y] = walls >= 5 ? 1 : (walls <= 3 ? 0 : map[x, y]);
            }
        return next;
    }

    private int CountNeighborWalls(int[,] map, int cx, int cy, int w, int h)
    {
        int count = 0;
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = cx + dx, ny = cy + dy;
                count += (nx < 0 || nx >= w || ny < 0 || ny >= h) ? 1 : map[nx, ny];
            }
        return count;
    }

    private void EnsureConnectivity(int[,] map, int w, int h)
    {
        // Flood fill to find all regions, then connect them
        var visited = new bool[w, h];
        var regions = new List<List<Point>>();

        for (int x = 1; x < w - 1; x++)
            for (int y = 1; y < h - 1; y++)
            {
                if (map[x, y] == 0 && !visited[x, y])
                {
                    var region = FloodFill(map, visited, x, y, w, h);
                    regions.Add(region);
                }
            }

        if (regions.Count <= 1) return;

        // Keep largest region, connect others to it
        regions.Sort((a, b) => b.Count.CompareTo(a.Count));
        var main = regions[0];

        for (int r = 1; r < regions.Count; r++)
        {
            // Find closest pair of points between regions
            int bestDist = int.MaxValue;
            Point bestA = default, bestB = default;
            foreach (var a in main)
                foreach (var b in regions[r])
                {
                    int dist = Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
                    if (dist < bestDist) { bestDist = dist; bestA = a; bestB = b; }
                }
            // Carve tunnel between them
            CarveLine(map, bestA, bestB);
            main.AddRange(regions[r]);
        }
    }

    private List<Point> FloodFill(int[,] map, bool[,] visited,
        int sx, int sy, int w, int h)
    {
        var result = new List<Point>();
        var stack = new Stack<Point>();
        stack.Push(new Point(sx, sy));
        while (stack.Count > 0)
        {
            var p = stack.Pop();
            if (p.X < 0 || p.X >= w || p.Y < 0 || p.Y >= h) continue;
            if (visited[p.X, p.Y] || map[p.X, p.Y] != 0) continue;
            visited[p.X, p.Y] = true;
            result.Add(p);
            stack.Push(new Point(p.X + 1, p.Y));
            stack.Push(new Point(p.X - 1, p.Y));
            stack.Push(new Point(p.X, p.Y + 1));
            stack.Push(new Point(p.X, p.Y - 1));
        }
        return result;
    }

    private void CarveLine(int[,] map, Point a, Point b)
    {
        int x = a.X, y = a.Y;
        while (x != b.X || y != b.Y)
        {
            map[x, y] = 0;
            // Widen the tunnel
            if (x + 1 < map.GetLength(0)) map[x + 1, y] = 0;
            if (y + 1 < map.GetLength(1)) map[x, y + 1] = 0;

            if (Math.Abs(x - b.X) > Math.Abs(y - b.Y))
                x += x < b.X ? 1 : -1;
            else
                y += y < b.Y ? 1 : -1;
        }
    }
}
```

---

## 5 — Wave Function Collapse (WFC)

WFC generates tilemap layouts that satisfy adjacency constraints — coherent patterns from a set of rules.

```csharp
public sealed class WfcTile
{
    public int Id;
    public string Name;
    /// <summary>Allowed neighbor tile IDs per direction: 0=Up, 1=Right, 2=Down, 3=Left.</summary>
    public HashSet<int>[] Adjacency = new HashSet<int>[4];
    public float Weight = 1f;
}

public sealed class WfcGenerator
{
    private readonly SeededRandom _rng;
    private readonly WfcTile[] _tiles;
    private readonly int _width, _height;
    private HashSet<int>[,] _possible;    // which tiles remain possible per cell
    private int[,] _result;               // collapsed tile IDs (-1 = uncollapsed)

    public WfcGenerator(SeededRandom rng, WfcTile[] tiles, int width, int height)
    {
        _rng = rng;
        _tiles = tiles;
        _width = width;
        _height = height;
        _possible = new HashSet<int>[width, height];
        _result = new int[width, height];
    }

    public int[,]? Generate(int maxAttempts = 10)
    {
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            Initialize();
            bool success = Run();
            if (success) return _result;
        }
        return null; // All attempts hit contradictions
    }

    private void Initialize()
    {
        var allIds = _tiles.Select(t => t.Id).ToHashSet();
        for (int x = 0; x < _width; x++)
            for (int y = 0; y < _height; y++)
            {
                _possible[x, y] = new HashSet<int>(allIds);
                _result[x, y] = -1;
            }
    }

    private bool Run()
    {
        while (true)
        {
            var cell = FindLowestEntropy();
            if (cell == null) return true; // All collapsed

            int cx = cell.Value.X, cy = cell.Value.Y;
            if (_possible[cx, cy].Count == 0) return false; // Contradiction

            // Collapse: weighted random pick
            int chosen = WeightedPick(_possible[cx, cy]);
            _result[cx, cy] = chosen;
            _possible[cx, cy].Clear();
            _possible[cx, cy].Add(chosen);

            // Propagate constraints
            if (!Propagate(cx, cy)) return false;
        }
    }

    private Point? FindLowestEntropy()
    {
        int minEntropy = int.MaxValue;
        Point? best = null;
        for (int x = 0; x < _width; x++)
            for (int y = 0; y < _height; y++)
            {
                int count = _possible[x, y].Count;
                if (_result[x, y] != -1 || count <= 0) continue;
                // Add small noise to break ties randomly
                if (count < minEntropy ||
                    (count == minEntropy && _rng.NextBool(0.3f)))
                {
                    minEntropy = count;
                    best = new Point(x, y);
                }
            }
        return best;
    }

    private int WeightedPick(HashSet<int> options)
    {
        float total = 0f;
        foreach (int id in options)
            total += _tiles[id].Weight;
        float roll = _rng.NextFloat() * total;
        float acc = 0f;
        foreach (int id in options)
        {
            acc += _tiles[id].Weight;
            if (roll < acc) return id;
        }
        return options.First();
    }

    private static readonly (int dx, int dy, int dir, int opp)[] Dirs =
    {
        (0, -1, 0, 2), // Up
        (1, 0, 1, 3),  // Right
        (0, 1, 2, 0),  // Down
        (-1, 0, 3, 1)  // Left
    };

    private bool Propagate(int startX, int startY)
    {
        var stack = new Stack<Point>();
        stack.Push(new Point(startX, startY));

        while (stack.Count > 0)
        {
            var p = stack.Pop();
            foreach (var (dx, dy, dir, opp) in Dirs)
            {
                int nx = p.X + dx, ny = p.Y + dy;
                if (nx < 0 || nx >= _width || ny < 0 || ny >= _height) continue;
                if (_result[nx, ny] != -1) continue;

                // Collect all tiles the neighbor is allowed to be
                var allowed = new HashSet<int>();
                foreach (int myTile in _possible[p.X, p.Y])
                    foreach (int neighbor in _tiles[myTile].Adjacency[dir])
                        allowed.Add(neighbor);

                int before = _possible[nx, ny].Count;
                _possible[nx, ny].IntersectWith(allowed);
                if (_possible[nx, ny].Count == 0) return false; // Contradiction

                if (_possible[nx, ny].Count < before)
                    stack.Push(new Point(nx, ny));
            }
        }
        return true;
    }
}
```

### Extracting Adjacency Rules from Example Maps

```csharp
public static WfcTile[] ExtractRules(int[,] exampleMap, int tileCount)
{
    var tiles = new WfcTile[tileCount];
    for (int i = 0; i < tileCount; i++)
        tiles[i] = new WfcTile
        {
            Id = i, Name = $"Tile{i}",
            Adjacency = Enumerable.Range(0, 4).Select(_ => new HashSet<int>()).ToArray()
        };

    int w = exampleMap.GetLength(0), h = exampleMap.GetLength(1);
    for (int x = 0; x < w; x++)
        for (int y = 0; y < h; y++)
        {
            int id = exampleMap[x, y];
            if (y > 0)     tiles[id].Adjacency[0].Add(exampleMap[x, y - 1]); // Up
            if (x < w - 1) tiles[id].Adjacency[1].Add(exampleMap[x + 1, y]); // Right
            if (y < h - 1) tiles[id].Adjacency[2].Add(exampleMap[x, y + 1]); // Down
            if (x > 0)     tiles[id].Adjacency[3].Add(exampleMap[x - 1, y]); // Left
        }
    return tiles;
}
```

---

## 6 — Room Templates & Handcrafted Chunks

Mix hand-designed rooms with procedural stitching — the Dead Cells / Spelunky approach.

### Room Template Definition

```csharp
public enum ConnectionSide { Top, Right, Bottom, Left }

public readonly record struct ConnectionPoint(ConnectionSide Side, int Offset, int Width);

public sealed class RoomTemplate
{
    public string Name;
    public int[,] Tiles;         // The hand-designed layout
    public int Width, Height;
    public List<ConnectionPoint> Connections = new();
    public string[] Tags = [];   // "combat", "treasure", "start", "boss"

    public RoomTemplate(string name, int[,] tiles, params ConnectionPoint[] conns)
    {
        Name = name;
        Tiles = tiles;
        Width = tiles.GetLength(0);
        Height = tiles.GetLength(1);
        Connections.AddRange(conns);
    }
}

public readonly record struct PlacedRoom(RoomTemplate Template, int X, int Y);
```

### Room Graph Stitcher

```csharp
public sealed class RoomGraphGenerator
{
    private readonly SeededRandom _rng;
    private readonly RoomTemplate[] _templates;

    public RoomGraphGenerator(SeededRandom rng, RoomTemplate[] templates)
    {
        _rng = rng;
        _templates = templates;
    }

    public List<PlacedRoom> Generate(int roomCount)
    {
        var placed = new List<PlacedRoom>();
        var occupied = new HashSet<Point>(); // track occupied grid cells

        // Place first room at origin
        var first = PickTemplate("start");
        placed.Add(new PlacedRoom(first, 0, 0));
        MarkOccupied(occupied, 0, 0, first);

        for (int i = 1; i < roomCount; i++)
        {
            bool didPlace = false;
            // Try to attach to a random existing room's open connection
            var indices = Enumerable.Range(0, placed.Count).ToArray();
            _rng.Shuffle(indices.AsSpan());

            foreach (int pi in indices)
            {
                var parent = placed[pi];
                foreach (var conn in parent.Template.Connections)
                {
                    string tag = i == roomCount - 1 ? "boss" : "combat";
                    var child = PickTemplateWithConnection(tag, Opposite(conn.Side));
                    if (child == null) continue;

                    var (cx, cy) = GetAttachPosition(parent, conn, child,
                        Opposite(conn.Side));
                    if (IsAreaFree(occupied, cx, cy, child))
                    {
                        placed.Add(new PlacedRoom(child, cx, cy));
                        MarkOccupied(occupied, cx, cy, child);
                        didPlace = true;
                        break;
                    }
                }
                if (didPlace) break;
            }
        }
        return placed;
    }

    private RoomTemplate PickTemplate(string preferredTag)
    {
        var candidates = _templates.Where(t => t.Tags.Contains(preferredTag)).ToArray();
        if (candidates.Length == 0) candidates = _templates;
        return _rng.Pick(candidates.AsSpan());
    }

    private RoomTemplate? PickTemplateWithConnection(string tag, ConnectionSide side)
    {
        var candidates = _templates
            .Where(t => t.Connections.Any(c => c.Side == side))
            .ToArray();
        return candidates.Length > 0 ? _rng.Pick(candidates.AsSpan()) : null;
    }

    private static ConnectionSide Opposite(ConnectionSide s) => s switch
    {
        ConnectionSide.Top => ConnectionSide.Bottom,
        ConnectionSide.Bottom => ConnectionSide.Top,
        ConnectionSide.Left => ConnectionSide.Right,
        ConnectionSide.Right => ConnectionSide.Left,
        _ => s
    };

    private static (int x, int y) GetAttachPosition(PlacedRoom parent,
        ConnectionPoint parentConn, RoomTemplate child, ConnectionSide childSide)
    {
        return parentConn.Side switch
        {
            ConnectionSide.Right => (parent.X + parent.Template.Width, parent.Y),
            ConnectionSide.Left => (parent.X - child.Width, parent.Y),
            ConnectionSide.Bottom => (parent.X, parent.Y + parent.Template.Height),
            ConnectionSide.Top => (parent.X, parent.Y - child.Height),
            _ => (parent.X, parent.Y)
        };
    }

    private void MarkOccupied(HashSet<Point> set, int ox, int oy, RoomTemplate t)
    {
        for (int x = 0; x < t.Width; x++)
            for (int y = 0; y < t.Height; y++)
                set.Add(new Point(ox + x, oy + y));
    }

    private bool IsAreaFree(HashSet<Point> set, int ox, int oy, RoomTemplate t)
    {
        for (int x = 0; x < t.Width; x++)
            for (int y = 0; y < t.Height; y++)
                if (set.Contains(new Point(ox + x, oy + y))) return false;
        return true;
    }
}
```

---

## 7 — Random Walk / Drunkard's Walk

Dead-simple carving. A walker moves randomly, carving floor tiles. Good for caves, tunnels, or organic paths.

```csharp
public sealed class RandomWalkGenerator
{
    private readonly SeededRandom _rng;

    public RandomWalkGenerator(SeededRandom rng) => _rng = rng;

    private static readonly (int dx, int dy)[] CardinalDirs =
        { (0, -1), (1, 0), (0, 1), (-1, 0) };

    /// <param name="floorPercent">Stop when this fraction of tiles are floor.</param>
    /// <param name="bias">Direction bias: (0,0) unbiased, (1,0) biases right, etc.</param>
    public int[,] Generate(int width, int height, float floorPercent = 0.4f,
        (float bx, float by) bias = default, int walkerCount = 1)
    {
        var map = new int[width, height]; // 0 = wall, 1 = floor
        int target = (int)(width * height * floorPercent);
        int carved = 0;

        for (int w = 0; w < walkerCount; w++)
        {
            int x = width / 2, y = height / 2;
            int perWalkerTarget = target / walkerCount;
            int walkerCarved = 0;

            while (walkerCarved < perWalkerTarget)
            {
                if (map[x, y] == 0) { map[x, y] = 1; walkerCarved++; carved++; }

                // Pick direction with optional bias
                var (dx, dy) = PickDirection(bias);
                int nx = Math.Clamp(x + dx, 1, width - 2);
                int ny = Math.Clamp(y + dy, 1, height - 2);
                x = nx; y = ny;
            }
        }
        return map;
    }

    private (int dx, int dy) PickDirection((float bx, float by) bias)
    {
        if (bias != default && _rng.NextBool(0.3f))
        {
            // Biased step
            int dx = bias.bx > 0 ? 1 : bias.bx < 0 ? -1 : 0;
            int dy = bias.by > 0 ? 1 : bias.by < 0 ? -1 : 0;
            return (dx, dy);
        }
        return _rng.Pick(CardinalDirs.AsSpan());
    }
}
```

**Biased walk tips:**
- `bias: (1, 0)` → levels that trend rightward (side-scrollers)
- `bias: (0, 1)` → downward descent (vertical shafts)
- Multiple walkers with different biases create branching tunnel networks

---

## 8 — Terrain Generation

### Heightmap Terrain for Side-Scrollers

```csharp
public sealed class TerrainGenerator
{
    private readonly SeededRandom _rng;

    public TerrainGenerator(SeededRandom rng) => _rng = rng;

    public int[] GenerateHeightmap(int width, int baseHeight, int variance,
        float noiseScale = 0.05f)
    {
        var heights = new int[width];
        float offsetX = _rng.NextFloat() * 10000f;
        for (int x = 0; x < width; x++)
        {
            float noise = FractalNoise.Sample(x * noiseScale + offsetX, 0f,
                octaves: 4, persistence: 0.45f);
            heights[x] = baseHeight + (int)(noise * variance);
        }
        return heights;
    }

    /// <summary>Fill a tile grid from a heightmap.</summary>
    public void ApplyHeightmap(int[,] tiles, int[] heights, int surfaceTile = 1,
        int dirtTile = 2, int stoneTile = 3)
    {
        int w = tiles.GetLength(0), h = tiles.GetLength(1);
        for (int x = 0; x < w && x < heights.Length; x++)
        {
            int surfaceY = h - heights[x];
            for (int y = surfaceY; y < h; y++)
            {
                if (y == surfaceY) tiles[x, y] = surfaceTile;
                else if (y < surfaceY + 4) tiles[x, y] = dirtTile;
                else tiles[x, y] = stoneTile;
            }
        }
    }
}
```

### Biome Assignment from Dual Noise Layers

```csharp
public enum Biome { Ocean, Beach, Plains, Forest, Desert, Tundra, Mountain }

public static class BiomeMapper
{
    public static Biome GetBiome(float elevation, float moisture, float temperature)
    {
        if (elevation < 0.3f) return Biome.Ocean;
        if (elevation < 0.35f) return Biome.Beach;
        if (elevation > 0.8f) return Biome.Mountain;
        if (temperature < 0.25f) return Biome.Tundra;
        if (moisture < 0.3f) return Biome.Desert;
        if (moisture > 0.6f) return Biome.Forest;
        return Biome.Plains;
    }

    public static Biome[,] GenerateBiomeMap(int width, int height, SeededRandom rng,
        float scale = 0.02f)
    {
        var map = new Biome[width, height];
        float ox1 = rng.NextFloat() * 10000f, oy1 = rng.NextFloat() * 10000f;
        float ox2 = rng.NextFloat() * 10000f, oy2 = rng.NextFloat() * 10000f;
        float ox3 = rng.NextFloat() * 10000f, oy3 = rng.NextFloat() * 10000f;

        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                float elev = (FractalNoise.Sample(x * scale + ox1,
                    y * scale + oy1) + 1f) * 0.5f;
                float moist = (FractalNoise.Sample(x * scale + ox2,
                    y * scale + oy2) + 1f) * 0.5f;
                float temp = (FractalNoise.Sample(x * scale * 0.5f + ox3,
                    y * scale * 0.5f + oy3) + 1f) * 0.5f;
                map[x, y] = GetBiome(elev, moist, temp);
            }
        return map;
    }
}
```

### Chunk-Based Infinite Terrain

```csharp
public sealed class ChunkManager
{
    private readonly Dictionary<Point, int[,]> _chunks = new();
    private readonly SeededRandom _masterRng;
    private const int ChunkSize = 64;

    public ChunkManager(int masterSeed) =>
        _masterRng = new SeededRandom(masterSeed);

    public int[,] GetOrCreateChunk(int chunkX, int chunkY)
    {
        var key = new Point(chunkX, chunkY);
        if (_chunks.TryGetValue(key, out var existing))
            return existing;

        // Deterministic per-chunk seed
        int chunkSeed = unchecked(_masterRng.Seed * 73856093 ^ chunkX * 19349663
            ^ chunkY * 83492791);
        var chunkRng = new SeededRandom(chunkSeed);
        var terrain = new TerrainGenerator(chunkRng);

        var tiles = new int[ChunkSize, ChunkSize];
        var heights = terrain.GenerateHeightmap(ChunkSize,
            baseHeight: ChunkSize / 2, variance: 12);
        terrain.ApplyHeightmap(tiles, heights);

        _chunks[key] = tiles;
        return tiles;
    }

    /// <summary>Unload chunks far from the camera.</summary>
    public void UnloadDistant(Point currentChunk, int keepRadius = 3)
    {
        var toRemove = _chunks.Keys
            .Where(k => Math.Abs(k.X - currentChunk.X) > keepRadius ||
                        Math.Abs(k.Y - currentChunk.Y) > keepRadius)
            .ToList();
        foreach (var k in toRemove) _chunks.Remove(k);
    }
}
```

---

## 9 — Item & Loot Generation

### Rarity Tiers & Weighted Drops

```csharp
public enum Rarity { Common, Uncommon, Rare, Epic, Legendary }

public static class RarityWeights
{
    public static readonly (Rarity rarity, float weight)[] Default =
    {
        (Rarity.Common, 50f),
        (Rarity.Uncommon, 30f),
        (Rarity.Rare, 15f),
        (Rarity.Epic, 4f),
        (Rarity.Legendary, 1f)
    };

    public static Rarity Roll(SeededRandom rng,
        ReadOnlySpan<(Rarity rarity, float weight)> table)
    {
        Span<float> weights = stackalloc float[table.Length];
        for (int i = 0; i < table.Length; i++) weights[i] = table[i].weight;
        int idx = rng.WeightedIndex(weights);
        return table[idx].rarity;
    }
}
```

### Affix-Based Item Generation

```csharp
public readonly record struct ItemAffix(string Name, string StatKey, float Value);

public readonly record struct GeneratedItem(
    string FullName, Rarity Rarity, List<ItemAffix> Affixes);

public sealed class LootGenerator
{
    private readonly SeededRandom _rng;

    private static readonly string[] Prefixes =
        { "Blazing", "Frozen", "Ancient", "Swift", "Brutal", "Cursed", "Holy" };
    private static readonly string[] BaseItems =
        { "Sword", "Axe", "Staff", "Bow", "Shield", "Dagger", "Hammer" };
    private static readonly string[] Suffixes =
        { "of Power", "of the Whale", "of Haste", "of Thorns", "of Leech" };

    private static readonly (string stat, float min, float max)[] AffixPool =
    {
        ("Attack", 5f, 50f),
        ("Defense", 3f, 30f),
        ("Speed", 1f, 15f),
        ("CritChance", 0.01f, 0.15f),
        ("HP", 10f, 200f)
    };

    public LootGenerator(SeededRandom rng) => _rng = rng;

    public GeneratedItem Generate(int playerLevel)
    {
        var rarity = RarityWeights.Roll(_rng, RarityWeights.Default);
        int affixCount = rarity switch
        {
            Rarity.Common => 0,
            Rarity.Uncommon => 1,
            Rarity.Rare => 2,
            Rarity.Epic => 3,
            Rarity.Legendary => 4,
            _ => 0
        };

        string baseName = _rng.Pick(BaseItems.AsSpan());
        string prefix = affixCount > 0 ? _rng.Pick(Prefixes.AsSpan()) + " " : "";
        string suffix = affixCount > 1 ? " " + _rng.Pick(Suffixes.AsSpan()) : "";

        var affixes = new List<ItemAffix>();
        for (int i = 0; i < affixCount; i++)
        {
            var (stat, min, max) = _rng.Pick(AffixPool.AsSpan());
            float levelScale = 1f + playerLevel * 0.1f;
            float value = MathF.Round(_rng.NextFloat(min, max) * levelScale, 1);
            affixes.Add(new ItemAffix(stat, stat, value));
        }

        return new GeneratedItem($"{prefix}{baseName}{suffix}", rarity, affixes);
    }
}
```

**Balancing tips:**
- Cap stat ranges per rarity so Legendary doesn't break the game at level 1.
- Use `playerLevel` as a floor for minimum stat values — ensures loot stays relevant.
- Track the last N drops to avoid duplicates (pity timer / streak-breaking).

---

## 10 — Enemy Placement

### Population Budgets

```csharp
public readonly record struct EnemySpawn(string EnemyType, Point Position);

public sealed class EnemyPlacer
{
    private readonly SeededRandom _rng;

    private static readonly (string type, int cost, float minDifficulty)[] EnemyPool =
    {
        ("Slime", 1, 0f),
        ("Skeleton", 2, 0.2f),
        ("Bat", 1, 0.1f),
        ("Orc", 3, 0.4f),
        ("Wraith", 4, 0.6f),
        ("Golem", 6, 0.8f)
    };

    public EnemyPlacer(SeededRandom rng) => _rng = rng;

    /// <param name="difficulty">0.0 to 1.0, scales with dungeon depth.</param>
    /// <param name="budget">Total enemy cost allowed in this room/area.</param>
    public List<EnemySpawn> PlaceEnemies(Rectangle room, int[,] tiles,
        float difficulty, int budget)
    {
        var spawns = new List<EnemySpawn>();
        var floorTiles = GetFloorTiles(room, tiles);
        _rng.Shuffle(floorTiles.ToArray().AsSpan());
        int floorIdx = 0;

        // Filter enemies by difficulty threshold
        var available = EnemyPool
            .Where(e => e.minDifficulty <= difficulty)
            .ToArray();
        if (available.Length == 0 || floorTiles.Count == 0) return spawns;

        int spent = 0;
        while (spent < budget && floorIdx < floorTiles.Count)
        {
            var enemy = _rng.Pick(available.AsSpan());
            if (spent + enemy.cost > budget) break;

            spawns.Add(new EnemySpawn(enemy.type, floorTiles[floorIdx++]));
            spent += enemy.cost;
        }
        return spawns;
    }

    private List<Point> GetFloorTiles(Rectangle room, int[,] tiles)
    {
        var result = new List<Point>();
        for (int x = room.X + 1; x < room.X + room.Width - 1; x++)
            for (int y = room.Y + 1; y < room.Y + room.Height - 1; y++)
                if (x >= 0 && x < tiles.GetLength(0) &&
                    y >= 0 && y < tiles.GetLength(1) &&
                    tiles[x, y] == 1)
                    result.Add(new Point(x, y));
        return result;
    }
}
```

**Design rules:**
- Rooms near the entrance get lower budgets. Boss rooms get a single high-cost enemy plus minions.
- Keep at least 2 tiles of clearance from doors so the player can retreat.
- Scale `difficulty` by `roomIndex / totalRooms` for a natural curve.

---

## 11 — Validation & Guarantees

Every generated level must be completable. Never ship a broken seed.

### Reachability Check

```csharp
public static class LevelValidator
{
    /// <summary>Returns true if all target points are reachable from start.</summary>
    public static bool IsReachable(int[,] tiles, Point start, params Point[] targets)
    {
        int w = tiles.GetLength(0), h = tiles.GetLength(1);
        var visited = new bool[w, h];
        var queue = new Queue<Point>();
        queue.Enqueue(start);
        visited[start.X, start.Y] = true;

        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            foreach (var (dx, dy) in new[] { (1,0), (-1,0), (0,1), (0,-1) })
            {
                int nx = p.X + dx, ny = p.Y + dy;
                if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                if (visited[nx, ny] || tiles[nx, ny] != 1) continue;
                visited[nx, ny] = true;
                queue.Enqueue(new Point(nx, ny));
            }
        }

        foreach (var t in targets)
            if (!visited[t.X, t.Y]) return false;
        return true;
    }

    /// <summary>Validate a full dungeon: start reachable to exit and all key items.</summary>
    public static bool ValidateDungeon(int[,] tiles, Point start, Point exit,
        List<Point> keyItems)
    {
        var allTargets = new List<Point>(keyItems) { exit };
        return IsReachable(tiles, start, allTargets.ToArray());
    }
}
```

### Generation with Retry

```csharp
public static class SafeGenerator
{
    /// <summary>
    /// Generate until valid or max attempts reached. Increments seed on failure.
    /// </summary>
    public static (int[,] tiles, List<Rectangle> rooms, int usedSeed)?
        GenerateValidDungeon(int baseSeed, int width, int height,
            int maxAttempts = 50)
    {
        for (int i = 0; i < maxAttempts; i++)
        {
            int seed = baseSeed + i;
            var rng = new SeededRandom(seed);
            var gen = new BspDungeonGenerator(rng);
            var (tiles, rooms) = gen.Generate(width, height);

            if (rooms.Count < 3) continue;

            var start = rooms[0].Center;
            var exit = rooms[^1].Center;

            if (LevelValidator.IsReachable(tiles, start, exit))
                return (tiles, rooms, seed);
        }
        return null; // Should log an error — template/params likely broken
    }
}
```

**Critical path checklist:**
1. Start and exit are reachable from each other.
2. All locked doors have their key item reachable before the door.
3. No room is isolated (flood fill covers all floor tiles in one region).
4. Boss room is always the farthest from start (guarantee escalation).

---

## 12 — ECS Integration

Wire everything into Arch ECS as a generation service.

### Components

```csharp
public record struct GenerationSeed(int Value);

public record struct TileData(int[,] Tiles, int Width, int Height);

public record struct RoomData(List<Rectangle> Rooms);

public record struct GeneratedLevel(int Seed, bool Validated);

public record struct ChunkCoord(int X, int Y);

public record struct ProceduralItem(
    string Name, Rarity Rarity, List<ItemAffix> Affixes);
```

### Level Generation Service

```csharp
using Arch.Core;
using Arch.Core.Extensions;

public sealed class LevelGenerationService
{
    private readonly World _world;

    public LevelGenerationService(World world) => _world = world;

    /// <summary>Generate a full dungeon level and spawn it into the ECS world.</summary>
    public Entity GenerateLevel(int seed, int width = 80, int height = 60)
    {
        var result = SafeGenerator.GenerateValidDungeon(seed, width, height);
        if (result == null)
            throw new InvalidOperationException(
                $"Failed to generate valid level from seed {seed}");

        var (tiles, rooms, usedSeed) = result.Value;
        var rng = new SeededRandom(usedSeed);

        // Create level entity
        var levelEntity = _world.Create(
            new GenerationSeed(usedSeed),
            new TileData(tiles, width, height),
            new RoomData(rooms),
            new GeneratedLevel(usedSeed, true)
        );

        // Spawn tile entities (or write to tilemap — see G37)
        SpawnTileEntities(tiles, width, height);

        // Place enemies in each room
        var enemyPlacer = new EnemyPlacer(new SeededRandom(rng.DeriveChildSeed(1)));
        for (int i = 0; i < rooms.Count; i++)
        {
            float difficulty = (float)i / rooms.Count;
            int budget = 3 + (int)(difficulty * 10);
            var spawns = enemyPlacer.PlaceEnemies(rooms[i], tiles, difficulty, budget);
            foreach (var spawn in spawns)
                SpawnEnemy(spawn);
        }

        // Drop loot in treasure rooms
        var lootGen = new LootGenerator(new SeededRandom(rng.DeriveChildSeed(2)));
        for (int i = 1; i < rooms.Count - 1; i++)
        {
            if (rng.NextBool(0.3f)) // 30% chance of loot per room
            {
                var item = lootGen.Generate(playerLevel: 1 + i);
                SpawnLootDrop(rooms[i].Center, item);
            }
        }

        return levelEntity;
    }

    private void SpawnTileEntities(int[,] tiles, int w, int h)
    {
        // Typically you'd write directly to a tilemap component (see G37)
        // rather than creating one entity per tile. Shown here for clarity.
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                if (tiles[x, y] == 1) // Only spawn floor tiles as entities
                    _world.Create(
                        new ChunkCoord(x, y)
                        // Add Position, SpriteRef, etc.
                    );
    }

    private void SpawnEnemy(EnemySpawn spawn)
    {
        _world.Create(
            new ChunkCoord(spawn.Position.X, spawn.Position.Y)
            // Add enemy archetype components: Health, AI, SpriteRef, etc.
        );
    }

    private void SpawnLootDrop(Point pos, GeneratedItem item)
    {
        _world.Create(
            new ChunkCoord(pos.X, pos.Y),
            new ProceduralItem(item.FullName, item.Rarity, item.Affixes)
        );
    }
}
```

### Usage in Game Initialization

```csharp
public sealed class GameStartupSystem
{
    private readonly World _world;
    private readonly LevelGenerationService _levelGen;

    public GameStartupSystem(World world)
    {
        _world = world;
        _levelGen = new LevelGenerationService(world);
    }

    public void StartNewGame(int? playerSeed = null)
    {
        int seed = playerSeed ?? Environment.TickCount;
        Console.WriteLine($"World seed: {SeededRandom.SeedToDisplay(seed)} ({seed})");

        var levelEntity = _levelGen.GenerateLevel(seed);

        // Store master seed for save/load
        _world.Create(new GenerationSeed(seed));
    }

    public void LoadFromSeed(int seed)
    {
        // Deterministic: same seed → same world
        _levelGen.GenerateLevel(seed);
    }
}
```

### Deterministic Replay Seed Component

```csharp
/// <summary>Attach to the world root entity. Systems read this for reproducibility.</summary>
public record struct WorldSeed(int Master, int CurrentLevel, int LootChannel, int EnemyChannel);

public static class SeedChannels
{
    public const int Dungeon = 0;
    public const int Loot = 1;
    public const int Enemies = 2;
    public const int Terrain = 3;
    public const int Events = 4;
}
```

---

## 13 — Island / Archipelago Generation

A multi-pass pipeline for generating island-based worlds (top-down RPGs, survival games, strategy). The approach composes rotated ellipses into distance fields, then applies terrain passes for natural-looking results.

### Layout Templates

Pre-define island configurations as collections of ellipses. Each layout places islands at specific relative positions with size and rotation parameters:

```csharp
public record struct IslandShape(float X, float Y, float RadiusX, float RadiusY, float Angle);

public static List<IslandShape> LayoutDominantWithSatellites(int mapW, int mapH, Random rng)
{
    var islands = new List<IslandShape>();
    float cx = mapW / 2f, cy = mapH / 2f;

    // Large central island
    islands.Add(new(cx, cy, mapW * 0.25f, mapH * 0.2f, rng.NextSingle() * 0.5f));

    // 3-5 satellite islands at min distance from center
    AddSmallIslands(islands, cx, cy, mapW, mapH, rng, count: rng.Next(3, 6),
        minDist: mapW * 0.3f, sizeRange: (0.06f, 0.12f));

    return islands;
}
```

Variety comes from having 5-6 layout functions (continent, twin islands, archipelago chain, atoll, scattered) and picking one randomly per generation.

### Distance Field Composition

Convert island shapes to a distance field, then threshold for land:

```csharp
for (int y = 0; y < height; y++)
for (int x = 0; x < width; x++)
{
    float minDist = float.MaxValue;
    foreach (var island in shapes)
    {
        // Rotate point into ellipse's local space
        float dx = x - island.X, dy = y - island.Y;
        float cos = MathF.Cos(-island.Angle), sin = MathF.Sin(-island.Angle);
        float lx = dx * cos - dy * sin;
        float ly = dx * sin + dy * cos;

        // Ellipse distance (approximate)
        float d = (lx * lx) / (island.RadiusX * island.RadiusX)
                + (ly * ly) / (island.RadiusY * island.RadiusY);
        minDist = MathF.Min(minDist, d);
    }

    // d < 1.0 = inside ellipse → land
    if (minDist < 0.6f) map.SetTile(x, y, TileType.Grass);
    else if (minDist < 1.0f) map.SetTile(x, y, TileType.Sand);
}
```

### Multi-Pass Terrain Pipeline

Raw distance-field islands look artificial. Apply passes in order:

| Pass | Purpose | Technique |
|------|---------|-----------|
| **Shape** | Place island ellipses | Distance field composition |
| **Paths** | Connect islands with walkable bridges/sandbars | Biased drunkard's walk |
| **Variety** | Add terrain diversity (dirt, stone) | Fractal noise thresholds |
| **Clearings** | Create open areas on large islands | Circle stamp at island centers |
| **Smooth** | Remove jagged edges | Cellular automata (2-3 passes) |
| **Hierarchy** | Enforce sand buffers water | Any grass adjacent to water → sand |
| **Cleanup** | Remove isolated tiles | Non-water with <2 same-type neighbors → downgrade |

### Biased Drunkard's Walk for Paths

Standard drunkard's walk is too random for connecting islands. Bias 70% of steps toward the target:

```csharp
int x = startX, y = startY;
while (x != endX || y != endY)
{
    if (rng.NextSingle() < 0.7f)
    {
        // Move toward target
        if (Math.Abs(endX - x) > Math.Abs(endY - y))
            x += Math.Sign(endX - x);
        else
            y += Math.Sign(endY - y);
    }
    else
    {
        // Random step
        switch (rng.Next(4))
        {
            case 0: x++; break; case 1: x--; break;
            case 2: y++; break; case 3: y--; break;
        }
    }
    x = Math.Clamp(x, 0, width - 1);
    y = Math.Clamp(y, 0, height - 1);
    map.SetTile(x, y, TileType.Sand); // path material
}
```

### Terrain Hierarchy Enforcement

A critical post-processing step: grass should never be cardinally adjacent to water. Insert a sand buffer:

```csharp
public static void EnforceTerrainHierarchy(TileMap map, int width, int height)
{
    // Snapshot grid to avoid feedback during iteration
    TileType[,] snapshot = new TileType[width, height];
    for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            snapshot[x, y] = map.GetTile(x, y);

    for (int y = 1; y < height - 1; y++)
        for (int x = 1; x < width - 1; x++)
        {
            if (snapshot[x, y] == TileType.Water || snapshot[x, y] == TileType.Sand)
                continue;

            bool adjacentWater = snapshot[x, y-1] == TileType.Water ||
                                 snapshot[x, y+1] == TileType.Water ||
                                 snapshot[x-1, y] == TileType.Water ||
                                 snapshot[x+1, y] == TileType.Water;

            if (adjacentWater)
                map.SetTile(x, y, TileType.Sand);
        }
}
```

This ensures clean shorelines with natural-looking sand beaches around every island.

---

## Quick Reference

| Technique | Best For | Complexity |
|-----------|----------|------------|
| **Random Walk** | Quick caves, organic tunnels | Low |
| **Cellular Automata** | Natural cave systems | Low |
| **BSP** | Structured dungeon rooms | Medium |
| **Room Templates** | Designed feel + variety | Medium |
| **WFC** | Coherent tilemap patterns | High |
| **Noise Terrain** | Overworld, side-scroller ground | Low-Medium |
| **Affix Loot** | Diablo-style item variety | Medium |

**Golden rules:**
1. Always validate. Never ship a broken seed.
2. Separate RNG channels per subsystem (layout, loot, enemies).
3. Let players see and share seeds — it's free engagement.
4. Mix handcrafted with procedural — pure random feels soulless.
5. Test with thousands of seeds in automated runs, not just the ones that look good.
