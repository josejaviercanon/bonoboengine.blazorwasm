# G40 — Pathfinding



> **Category:** Guide · **Related:** [G4 AI Systems](./G4_ai_systems.md) · [G14 Data Structures](./G14_data_structures.md) · [G3 Physics & Collision](./G3_physics_and_collision.md) · [G37 Tilemap Systems](./G37_tilemap_systems.md)

Pathfinding is the backbone of any AI that needs to move through a world. This guide covers everything from basic A* on a grid to flow fields, hierarchical search, navigation meshes, and full ECS integration with BrainAI and Arch.

---

## Table of Contents

1. [A* Algorithm](#1--a-algorithm)
2. [Grid Graph](#2--grid-graph)
3. [Jump Point Search (JPS)](#3--jump-point-search-jps)
4. [Flow Fields](#4--flow-fields)
5. [Hierarchical Pathfinding (HPA*)](#5--hierarchical-pathfinding-hpa)
6. [Navigation Mesh (2D)](#6--navigation-mesh-2d)
7. [Path Smoothing](#7--path-smoothing)
8. [Steering Behaviors](#8--steering-behaviors)
9. [BrainAI Integration](#9--brainai-integration)
10. [ECS Integration](#10--ecs-integration)
11. [Debug Visualization](#11--debug-visualization)

---

## 1 — A* Algorithm

A* finds the shortest path by expanding the most promising node first, using `f(n) = g(n) + h(n)` where `g` is cost-so-far and `h` is a heuristic estimate to the goal.

### 1.1 — Heuristics

```csharp
public static class Heuristics
{
    // Best for 4-directional (no diagonal) grids
    public static float Manhattan(Point a, Point b)
        => MathF.Abs(a.X - b.X) + MathF.Abs(a.Y - b.Y);

    // Best for any-angle movement or navmesh
    public static float Euclidean(Point a, Point b)
    {
        float dx = a.X - b.X;
        float dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    // Best for 8-directional grids (diagonal allowed)
    public static float Chebyshev(Point a, Point b)
        => MathF.Max(MathF.Abs(a.X - b.X), MathF.Abs(a.Y - b.Y));

    // Octile — accurate for 8-dir with √2 diagonal cost
    public static float Octile(Point a, Point b)
    {
        float dx = MathF.Abs(a.X - b.X);
        float dy = MathF.Abs(a.Y - b.Y);
        return MathF.Max(dx, dy) + 0.41421356f * MathF.Min(dx, dy);
    }
}
```

**Choosing a heuristic:** If `h` never overestimates, A* is optimal. Manhattan is admissible for 4-dir; Octile for 8-dir. Euclidean is always admissible but may under-estimate on grids, expanding more nodes.

### 1.2 — A* Node

```csharp
public struct PathNode : IComparable<PathNode>
{
    public Point Position;
    public float G; // cost from start
    public float H; // heuristic to goal
    public float F => G + H;
    public Point Parent;

    public int CompareTo(PathNode other)
    {
        int cmp = F.CompareTo(other.F);
        return cmp != 0 ? cmp : H.CompareTo(other.H); // tie-break on H
    }
}
```

### 1.3 — Full A* Implementation

```csharp
public class AStarPathfinder
{
    private readonly GridGraph _graph;
    private readonly Func<Point, Point, float> _heuristic;

    // Direction offsets: 4-directional + 4 diagonal
    private static readonly (Point Offset, float Cost)[] Dirs8 =
    {
        (new Point( 0, -1), 1.0f),  // N
        (new Point( 1,  0), 1.0f),  // E
        (new Point( 0,  1), 1.0f),  // S
        (new Point(-1,  0), 1.0f),  // W
        (new Point( 1, -1), 1.414f), // NE
        (new Point( 1,  1), 1.414f), // SE
        (new Point(-1,  1), 1.414f), // SW
        (new Point(-1, -1), 1.414f), // NW
    };

    public AStarPathfinder(GridGraph graph, Func<Point, Point, float> heuristic = null)
    {
        _graph = graph;
        _heuristic = heuristic ?? Heuristics.Octile;
    }

    public List<Point> FindPath(Point start, Point goal, bool allowDiagonal = true)
    {
        int dirCount = allowDiagonal ? 8 : 4;

        var open = new PriorityQueue<Point, float>();
        var gScore = new Dictionary<Point, float>();
        var parent = new Dictionary<Point, Point>();
        var closed = new HashSet<Point>();

        gScore[start] = 0;
        open.Enqueue(start, _heuristic(start, goal));

        while (open.Count > 0)
        {
            var current = open.Dequeue();

            if (current == goal)
                return ReconstructPath(parent, current);

            if (!closed.Add(current))
                continue; // already processed

            for (int i = 0; i < dirCount; i++)
            {
                var (offset, baseCost) = Dirs8[i];
                var neighbor = new Point(current.X + offset.X, current.Y + offset.Y);

                if (!_graph.IsWalkable(neighbor) || closed.Contains(neighbor))
                    continue;

                // Prevent diagonal corner-cutting
                if (i >= 4 && !_graph.IsWalkable(new Point(current.X + offset.X, current.Y))
                           && !_graph.IsWalkable(new Point(current.X, current.Y + offset.Y)))
                    continue;

                float terrainCost = _graph.GetCost(neighbor);
                float tentativeG = gScore[current] + baseCost * terrainCost;

                if (!gScore.TryGetValue(neighbor, out float existingG) || tentativeG < existingG)
                {
                    gScore[neighbor] = tentativeG;
                    parent[neighbor] = current;
                    float f = tentativeG + _heuristic(neighbor, goal);
                    open.Enqueue(neighbor, f);
                }
            }
        }

        return null; // no path found
    }

    private static List<Point> ReconstructPath(Dictionary<Point, Point> parent, Point current)
    {
        var path = new List<Point> { current };
        while (parent.TryGetValue(current, out var prev))
        {
            path.Add(prev);
            current = prev;
        }
        path.Reverse();
        return path;
    }
}
```

### 1.4 — Terrain Weights

Different tiles can have different movement costs. Swamp might cost 3× while road costs 0.5×. The `GridGraph.GetCost()` method returns this multiplier, and A* multiplies it by the base step cost. This preserves optimality as long as your heuristic doesn't overestimate the *cheapest possible* path.

---

## 2 — Grid Graph

The grid graph is the data structure A* searches over. It wraps your tilemap and answers two questions: "Can I walk here?" and "How much does it cost?"

### 2.1 — Core Grid Graph

```csharp
public class GridGraph
{
    public int Width { get; }
    public int Height { get; }

    private readonly byte[] _walkable;   // 0 = blocked, 1 = walkable
    private readonly float[] _cost;      // terrain weight per cell

    public GridGraph(int width, int height)
    {
        Width = width;
        Height = height;
        _walkable = new byte[width * height];
        _cost = new float[width * height];
        Array.Fill(_cost, 1.0f);
        Array.Fill(_walkable, (byte)1);
    }

    public bool InBounds(Point p)
        => p.X >= 0 && p.X < Width && p.Y >= 0 && p.Y < Height;

    public bool IsWalkable(Point p)
        => InBounds(p) && _walkable[p.Y * Width + p.X] != 0;

    public float GetCost(Point p)
        => _cost[p.Y * Width + p.X];

    public void SetWalkable(Point p, bool walkable)
    {
        if (InBounds(p))
            _walkable[p.Y * Width + p.X] = walkable ? (byte)1 : (byte)0;
    }

    public void SetCost(Point p, float cost)
    {
        if (InBounds(p))
            _cost[p.Y * Width + p.X] = cost;
    }
}
```

### 2.2 — Building from Tilemap Data

```csharp
public static GridGraph BuildFromTilemap(int[,] tileIds, Dictionary<int, float> tileCosts)
{
    int w = tileIds.GetLength(0);
    int h = tileIds.GetLength(1);
    var graph = new GridGraph(w, h);

    for (int y = 0; y < h; y++)
    for (int x = 0; x < w; x++)
    {
        int tileId = tileIds[x, y];
        if (tileCosts.TryGetValue(tileId, out float cost))
        {
            graph.SetWalkable(new Point(x, y), true);
            graph.SetCost(new Point(x, y), cost);
        }
        else
        {
            // Unknown tile or wall — mark as blocked
            graph.SetWalkable(new Point(x, y), false);
        }
    }

    return graph;
}
```

### 2.3 — Dynamic Obstacles

When entities (crates, doors, units) occupy cells at runtime, you need to update the graph dynamically. Two strategies:

1. **Stamp/unstamp** — Flip walkability when an obstacle spawns/despawns. Simple, but can conflict if two obstacles share a cell.
2. **Occupancy counter** — Track how many blockers sit on each cell. Cell is walkable only when count == 0.

```csharp
public class DynamicObstacleTracker
{
    private readonly int[] _occupancy;
    private readonly GridGraph _graph;

    public DynamicObstacleTracker(GridGraph graph)
    {
        _graph = graph;
        _occupancy = new int[graph.Width * graph.Height];
    }

    public void AddObstacle(Point p)
    {
        int idx = p.Y * _graph.Width + p.X;
        _occupancy[idx]++;
        _graph.SetWalkable(p, false);
    }

    public void RemoveObstacle(Point p)
    {
        int idx = p.Y * _graph.Width + p.X;
        _occupancy[idx] = Math.Max(0, _occupancy[idx] - 1);
        if (_occupancy[idx] == 0)
            _graph.SetWalkable(p, true);
    }
}
```

---

## 3 — Jump Point Search (JPS)

JPS is an optimization for **uniform-cost grids** (all walkable cells cost 1). Instead of expanding every neighbor, it "jumps" along straight lines until it finds a turning point (forced neighbor). This dramatically prunes the open set.

### 3.1 — When to Use JPS vs A*

| Scenario | Best Choice |
|---|---|
| Uniform cost grid, large open areas | **JPS** — huge speedup |
| Weighted terrain (swamp, road, etc.) | **A*** — JPS assumes uniform cost |
| Small maps (< 50×50) | **A*** — JPS overhead not worth it |
| Lots of narrow corridors | **A*** — JPS advantage shrinks |

### 3.2 — JPS Implementation Sketch

```csharp
public class JumpPointSearch
{
    private readonly GridGraph _graph;

    public JumpPointSearch(GridGraph graph) => _graph = graph;

    /// <summary>
    /// Jump from (cx,cy) in direction (dx,dy). Returns the jump point or null.
    /// </summary>
    private Point? Jump(int cx, int cy, int dx, int dy, Point goal)
    {
        int nx = cx + dx, ny = cy + dy;
        if (!_graph.IsWalkable(new Point(nx, ny)))
            return null;
        if (new Point(nx, ny) == goal)
            return new Point(nx, ny);

        // Check for forced neighbors
        if (dx != 0 && dy != 0)
        {
            // Diagonal: forced neighbor if blocked cardinally but open diagonally
            if ((!_graph.IsWalkable(new Point(nx - dx, ny)) && _graph.IsWalkable(new Point(nx - dx, ny + dy))) ||
                (!_graph.IsWalkable(new Point(nx, ny - dy)) && _graph.IsWalkable(new Point(nx + dx, ny - dy))))
                return new Point(nx, ny);

            // Recurse cardinally before continuing diagonally
            if (Jump(nx, ny, dx, 0, goal) != null || Jump(nx, ny, 0, dy, goal) != null)
                return new Point(nx, ny);
        }
        else
        {
            // Horizontal
            if (dx != 0)
            {
                if ((!_graph.IsWalkable(new Point(nx, ny - 1)) && _graph.IsWalkable(new Point(nx + dx, ny - 1))) ||
                    (!_graph.IsWalkable(new Point(nx, ny + 1)) && _graph.IsWalkable(new Point(nx + dx, ny + 1))))
                    return new Point(nx, ny);
            }
            // Vertical
            else
            {
                if ((!_graph.IsWalkable(new Point(nx - 1, ny)) && _graph.IsWalkable(new Point(nx - 1, ny + dy))) ||
                    (!_graph.IsWalkable(new Point(nx + 1, ny)) && _graph.IsWalkable(new Point(nx + 1, ny + dy))))
                    return new Point(nx, ny);
            }
        }

        return Jump(nx, ny, dx, dy, goal);
    }

    // The main pathfinding loop is standard A*, but instead of expanding
    // all 8 neighbors, you call Jump() in each direction and only enqueue
    // jump points. Reconstruct path by connecting jump points with straight lines.
}
```

The outer search loop is identical to A*—use a `PriorityQueue`, track g-scores, reconstruct via parent map. The difference is that `GetNeighbors()` returns jump points instead of immediate neighbors.

---

## 4 — Flow Fields

When many units need to path to the **same target** (RTS rally point, tower-defense exit), computing individual A* paths is wasteful. A flow field computes a single field that every unit can query for their next move direction.

### 4.1 — Algorithm Overview

1. **Cost field** — Each cell stores its traversal cost (from GridGraph).
2. **Integration field** — BFS/Dijkstra from the goal outward. Each cell stores total cost to reach the goal.
3. **Flow field** — Each cell stores a direction vector pointing toward its lowest-cost neighbor.

### 4.2 — Implementation

```csharp
public class FlowField
{
    public int Width { get; }
    public int Height { get; }

    private readonly float[] _integrationField;
    private readonly Vector2[] _flowDirections;

    private static readonly Point[] Neighbors =
    {
        new( 0, -1), new( 1, 0), new( 0, 1), new(-1, 0),
        new( 1, -1), new( 1, 1), new(-1, 1), new(-1,-1),
    };

    public FlowField(int width, int height)
    {
        Width = width;
        Height = height;
        _integrationField = new float[width * height];
        _flowDirections = new Vector2[width * height];
    }

    /// <summary>
    /// Build the flow field from grid graph toward a single goal cell.
    /// </summary>
    public void Build(GridGraph graph, Point goal)
    {
        // --- Integration field (Dijkstra from goal) ---
        Array.Fill(_integrationField, float.MaxValue);
        int goalIdx = goal.Y * Width + goal.X;
        _integrationField[goalIdx] = 0;

        var open = new Queue<Point>();
        open.Enqueue(goal);

        while (open.Count > 0)
        {
            var current = open.Dequeue();
            int curIdx = current.Y * Width + current.X;
            float curCost = _integrationField[curIdx];

            foreach (var dir in Neighbors)
            {
                var neighbor = new Point(current.X + dir.X, current.Y + dir.Y);
                if (!graph.IsWalkable(neighbor)) continue;

                int nIdx = neighbor.Y * Width + neighbor.X;
                float stepCost = (dir.X != 0 && dir.Y != 0) ? 1.414f : 1.0f;
                float newCost = curCost + stepCost * graph.GetCost(neighbor);

                if (newCost < _integrationField[nIdx])
                {
                    _integrationField[nIdx] = newCost;
                    open.Enqueue(neighbor);
                }
            }
        }

        // --- Flow directions (point toward cheapest neighbor) ---
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            int idx = y * Width + x;
            if (_integrationField[idx] >= float.MaxValue)
            {
                _flowDirections[idx] = Vector2.Zero;
                continue;
            }

            float bestCost = float.MaxValue;
            Vector2 bestDir = Vector2.Zero;

            foreach (var dir in Neighbors)
            {
                var n = new Point(x + dir.X, y + dir.Y);
                if (!graph.InBounds(n)) continue;
                int nIdx = n.Y * Width + n.X;
                if (_integrationField[nIdx] < bestCost)
                {
                    bestCost = _integrationField[nIdx];
                    bestDir = new Vector2(dir.X, dir.Y);
                }
            }

            _flowDirections[idx] = bestDir != Vector2.Zero
                ? Vector2.Normalize(bestDir)
                : Vector2.Zero;
        }
    }

    /// <summary>
    /// Query the flow direction at a world position (converted to cell coords).
    /// </summary>
    public Vector2 GetDirection(Point cell)
    {
        if (cell.X < 0 || cell.X >= Width || cell.Y < 0 || cell.Y >= Height)
            return Vector2.Zero;
        return _flowDirections[cell.Y * Width + cell.X];
    }

    public float GetIntegrationCost(Point cell)
    {
        if (cell.X < 0 || cell.X >= Width || cell.Y < 0 || cell.Y >= Height)
            return float.MaxValue;
        return _integrationField[cell.Y * Width + cell.X];
    }
}
```

### 4.3 — When Flow Fields Beat A*

- **Many units, one target:** Flow field cost is `O(cells)` once, then each unit does `O(1)` per step. For N units, A* is `O(N × cells × log)`, flow field is `O(cells + N)`.
- **RTS / tower defense** with 50+ units converging on the same point.
- **Dynamic re-routing** — rebuild the field when the goal moves; all units automatically adjust.

Flow fields are **not** worth it for few units with different destinations — use A* instead.

---

## 5 — Hierarchical Pathfinding (HPA*)

For large maps (200×200+), A* can be too slow. HPA* divides the map into clusters, precomputes connectivity between clusters, then searches the abstract graph first and refines locally.

### 5.1 — Concept

1. **Divide** the map into rectangular clusters (e.g., 10×10 cells each).
2. **Find entrances** — border cells where two adjacent clusters are both walkable.
3. **Build abstract graph** — nodes are entrance points, edges are intra-cluster paths (precomputed via A*).
4. **High-level search** — A* on the abstract graph to find cluster-to-cluster route.
5. **Refine** — Within each cluster the unit traverses, run local A* for the actual path.

### 5.2 — Implementation Sketch

```csharp
public class HPACluster
{
    public Rectangle Bounds;
    public List<Point> EntranceNodes = new();
    // Precomputed intra-cluster distances between all entrance pairs
    public Dictionary<(Point, Point), float> InternalDistances = new();
}

public class HPAGraph
{
    private readonly GridGraph _baseGraph;
    private readonly int _clusterSize;
    private HPACluster[,] _clusters;

    // Abstract graph: entrance nodes and edges
    private Dictionary<Point, List<(Point Target, float Cost)>> _abstractEdges = new();

    public HPAGraph(GridGraph baseGraph, int clusterSize = 10)
    {
        _baseGraph = baseGraph;
        _clusterSize = clusterSize;
        BuildClusters();
        FindEntrances();
        PrecomputeInternalPaths();
    }

    private void BuildClusters()
    {
        int cw = (_baseGraph.Width + _clusterSize - 1) / _clusterSize;
        int ch = (_baseGraph.Height + _clusterSize - 1) / _clusterSize;
        _clusters = new HPACluster[cw, ch];

        for (int cy = 0; cy < ch; cy++)
        for (int cx = 0; cx < cw; cx++)
        {
            _clusters[cx, cy] = new HPACluster
            {
                Bounds = new Rectangle(
                    cx * _clusterSize, cy * _clusterSize,
                    Math.Min(_clusterSize, _baseGraph.Width - cx * _clusterSize),
                    Math.Min(_clusterSize, _baseGraph.Height - cy * _clusterSize))
            };
        }
    }

    private void FindEntrances()
    {
        // Walk horizontal and vertical cluster borders.
        // Consecutive walkable border pairs form an entrance.
        // Add the midpoint of each entrance run as an entrance node.
        // ... (scan borders, group contiguous walkable pairs, pick midpoints)
    }

    private void PrecomputeInternalPaths()
    {
        // For each cluster, run A* between all pairs of its entrance nodes
        // (clipped to cluster bounds). Store distances in InternalDistances.
        // Add edges to _abstractEdges for both inter-cluster (cost 1)
        // and intra-cluster connections.
    }

    /// <summary>
    /// High-level path: insert start/goal into abstract graph temporarily,
    /// run A*, then remove them. Returns list of entrance waypoints.
    /// </summary>
    public List<Point> FindAbstractPath(Point start, Point goal)
    {
        // 1. Find which cluster start/goal belong to
        // 2. Temporarily connect them to that cluster's entrance nodes
        // 3. A* on abstract graph
        // 4. Return waypoint sequence
        return new List<Point>(); // placeholder
    }
}
```

**Performance:** On a 500×500 map with 10×10 clusters, A* searches ~2,500 abstract nodes instead of 250,000. The refinement A* runs on 10×10 sub-grids, which is nearly instant.

---

## 6 — Navigation Mesh (2D)

Grids are convenient but wasteful in open areas. A **navmesh** decomposes walkable space into convex polygons. Pathfinding searches polygon adjacency, then the funnel algorithm smooths the result.

### 6.1 — When to Use Navmesh vs Grid

| Factor | Grid | Navmesh |
|---|---|---|
| Freeform geometry (non-tile) | Poor fit | Natural fit |
| Tile-based maps | Natural fit | Overkill |
| Memory for large open areas | Wastes cells | Few polygons |
| Implementation complexity | Simple | Complex |
| Dynamic obstacles | Easy (flip cells) | Harder (re-mesh) |

### 6.2 — Data Structures

```csharp
public class NavPoly
{
    public int Id;
    public Vector2[] Vertices;        // wound CCW
    public List<NavEdge> Edges = new();
}

public class NavEdge
{
    public int NeighborPolyId;        // -1 if boundary
    public Vector2 Left, Right;       // portal endpoints
}

public class NavMesh
{
    public List<NavPoly> Polygons = new();

    /// <summary>
    /// Find which polygon contains a world point (point-in-convex-polygon test).
    /// </summary>
    public int FindContainingPoly(Vector2 point)
    {
        for (int i = 0; i < Polygons.Count; i++)
        {
            if (IsInsideConvex(Polygons[i].Vertices, point))
                return i;
        }
        return -1; // outside navmesh
    }

    private static bool IsInsideConvex(Vector2[] verts, Vector2 p)
    {
        for (int i = 0; i < verts.Length; i++)
        {
            var a = verts[i];
            var b = verts[(i + 1) % verts.Length];
            float cross = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
            if (cross < 0) return false;
        }
        return true;
    }
}
```

### 6.3 — Funnel Algorithm (Simple String Pulling)

After A* on the polygon adjacency graph gives you a sequence of portals, the funnel algorithm finds the shortest path through them:

```csharp
public static List<Vector2> FunnelPath(Vector2 start, Vector2 goal, List<NavEdge> portals)
{
    var path = new List<Vector2> { start };

    var apex = start;
    var left = start;
    var right = start;
    int apexIdx = 0, leftIdx = 0, rightIdx = 0;

    for (int i = 0; i < portals.Count; i++)
    {
        var pLeft = portals[i].Left;
        var pRight = portals[i].Right;

        // Update right
        if (Cross2D(apex, right, pRight) <= 0)
        {
            if (apex == right || Cross2D(apex, left, pRight) > 0)
            {
                right = pRight;
                rightIdx = i;
            }
            else
            {
                path.Add(left);
                apex = left;
                apexIdx = leftIdx;
                left = apex;
                right = apex;
                leftIdx = apexIdx;
                rightIdx = apexIdx;
                i = apexIdx;
                continue;
            }
        }

        // Update left
        if (Cross2D(apex, left, pLeft) >= 0)
        {
            if (apex == left || Cross2D(apex, right, pLeft) < 0)
            {
                left = pLeft;
                leftIdx = i;
            }
            else
            {
                path.Add(right);
                apex = right;
                apexIdx = rightIdx;
                left = apex;
                right = apex;
                leftIdx = apexIdx;
                rightIdx = apexIdx;
                i = apexIdx;
                continue;
            }
        }
    }

    path.Add(goal);
    return path;
}

private static float Cross2D(Vector2 o, Vector2 a, Vector2 b)
    => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
```

---

## 7 — Path Smoothing

Raw A* paths on grids look jagged. Smoothing makes them natural.

### 7.1 — Line-of-Sight Smoothing

Walk the path and remove intermediate waypoints that have clear line-of-sight to an earlier waypoint.

```csharp
public static List<Point> SmoothPathLOS(List<Point> rawPath, GridGraph graph)
{
    if (rawPath == null || rawPath.Count <= 2) return rawPath;

    var smoothed = new List<Point> { rawPath[0] };
    int current = 0;

    while (current < rawPath.Count - 1)
    {
        int furthest = current + 1;

        // Find the furthest point we can see directly
        for (int i = rawPath.Count - 1; i > current + 1; i--)
        {
            if (HasLineOfSight(graph, rawPath[current], rawPath[i]))
            {
                furthest = i;
                break;
            }
        }

        smoothed.Add(rawPath[furthest]);
        current = furthest;
    }

    return smoothed;
}

/// <summary>
/// Bresenham-style line-of-sight check on the grid.
/// </summary>
private static bool HasLineOfSight(GridGraph graph, Point a, Point b)
{
    int dx = Math.Abs(b.X - a.X), dy = Math.Abs(b.Y - a.Y);
    int sx = a.X < b.X ? 1 : -1, sy = a.Y < b.Y ? 1 : -1;
    int err = dx - dy;

    int x = a.X, y = a.Y;
    while (x != b.X || y != b.Y)
    {
        if (!graph.IsWalkable(new Point(x, y))) return false;

        int e2 = 2 * err;
        if (e2 > -dy) { err -= dy; x += sx; }
        if (e2 < dx)  { err += dx; y += sy; }
    }
    return true;
}
```

### 7.2 — Catmull-Rom Spline for Curved Paths

After reducing waypoints, you can interpolate a smooth curve through them:

```csharp
public static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
{
    float t2 = t * t, t3 = t2 * t;
    return 0.5f * (
        (2f * p1) +
        (-p0 + p2) * t +
        (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
        (-p0 + 3f * p1 - 3f * p2 + p3) * t3
    );
}

/// <summary>
/// Generates a smooth curve through waypoints. Returns sampled positions.
/// </summary>
public static List<Vector2> SplinePath(List<Vector2> waypoints, int samplesPerSegment = 8)
{
    if (waypoints.Count < 2) return new List<Vector2>(waypoints);

    var result = new List<Vector2>();
    for (int i = 0; i < waypoints.Count - 1; i++)
    {
        var p0 = waypoints[Math.Max(i - 1, 0)];
        var p1 = waypoints[i];
        var p2 = waypoints[i + 1];
        var p3 = waypoints[Math.Min(i + 2, waypoints.Count - 1)];

        for (int s = 0; s < samplesPerSegment; s++)
        {
            float t = s / (float)samplesPerSegment;
            result.Add(CatmullRom(p0, p1, p2, p3, t));
        }
    }

    result.Add(waypoints[^1]);
    return result;
}
```

---

## 8 — Steering Behaviors

Pathfinding gives you a list of waypoints. Steering behaviors handle the frame-to-frame movement that actually follows the path smoothly.

### 8.1 — Core Steering Forces

```csharp
public static class Steering
{
    public static Vector2 Seek(Vector2 position, Vector2 target, float maxSpeed)
    {
        var desired = Vector2.Normalize(target - position) * maxSpeed;
        return desired; // subtract current velocity for true steering force
    }

    public static Vector2 Flee(Vector2 position, Vector2 threat, float maxSpeed)
        => -Seek(position, threat, maxSpeed);

    public static Vector2 Arrive(Vector2 position, Vector2 target, float maxSpeed, float slowRadius)
    {
        var offset = target - position;
        float dist = offset.Length();
        if (dist < 0.001f) return Vector2.Zero;

        float rampedSpeed = maxSpeed * (dist / slowRadius);
        float clampedSpeed = MathF.Min(rampedSpeed, maxSpeed);
        return (offset / dist) * clampedSpeed;
    }

    /// <summary>
    /// Steer away from the nearest obstacle using a forward feeler ray.
    /// </summary>
    public static Vector2 ObstacleAvoidance(
        Vector2 position, Vector2 velocity, float lookAhead,
        Func<Vector2, Vector2, (bool Hit, Vector2 Normal)> raycast)
    {
        if (velocity.LengthSquared() < 0.001f) return Vector2.Zero;

        var dir = Vector2.Normalize(velocity);
        var feeler = position + dir * lookAhead;
        var (hit, normal) = raycast(position, feeler);

        if (!hit) return Vector2.Zero;
        return normal * velocity.Length(); // push away from wall
    }
}
```

### 8.2 — Path Following

```csharp
public record struct PathFollower(
    List<Vector2> Waypoints,
    int CurrentIndex,
    float WaypointRadius
);

public static class PathFollowing
{
    public static Vector2 FollowPath(
        ref PathFollower follower, Vector2 position, float maxSpeed, float arrivalRadius)
    {
        if (follower.Waypoints == null || follower.CurrentIndex >= follower.Waypoints.Count)
            return Vector2.Zero;

        var target = follower.Waypoints[follower.CurrentIndex];
        float dist = Vector2.Distance(position, target);

        // Advance to next waypoint when close enough
        if (dist < follower.WaypointRadius && follower.CurrentIndex < follower.Waypoints.Count - 1)
        {
            follower.CurrentIndex++;
            target = follower.Waypoints[follower.CurrentIndex];
        }

        // Use arrive for the last waypoint, seek for intermediate ones
        bool isLast = follower.CurrentIndex == follower.Waypoints.Count - 1;
        return isLast
            ? Steering.Arrive(position, target, maxSpeed, arrivalRadius)
            : Steering.Seek(position, target, maxSpeed);
    }
}
```

### 8.3 — Combining Steering Forces

When you have multiple behaviors active (follow path + avoid obstacles), use weighted blending:

```csharp
public static Vector2 CombineSteering(
    Vector2 pathForce, float pathWeight,
    Vector2 avoidForce, float avoidWeight,
    float maxForce)
{
    var combined = pathForce * pathWeight + avoidForce * avoidWeight;
    if (combined.LengthSquared() > maxForce * maxForce)
        combined = Vector2.Normalize(combined) * maxForce;
    return combined;
}
```

Obstacle avoidance typically gets a higher weight (e.g., 2.0) than path following (1.0) so the unit dodges walls even if it means temporarily deviating from the path.

---

## 9 — BrainAI Integration

BrainAI ships with ready-to-use pathfinding classes. Use them when you don't need a custom algorithm.

### 9.1 — AstarGridGraph

```csharp
using BrainAI.Pathfinding.AStar;

// Create the grid
var brainGrid = new AstarGridGraph(mapWidth, mapHeight);

// Mark walls
foreach (var wall in wallPositions)
    brainGrid.Walls.Add(wall);

// Set terrain weights (optional)
brainGrid.WeightedNodes[new Point(5, 3)] = 3; // swamp tile

// Allow or disallow diagonal movement
brainGrid.AllowDiagonalSearch = true;

// Find path
var path = AStarPathfinder.Search(brainGrid, start, goal);
// path is List<Point> or null if no path exists
```

### 9.2 — BreadthFirstPathfinder

For unweighted graphs (all costs equal), BFS is simpler and faster:

```csharp
using BrainAI.Pathfinding.BreadthFirst;

var graph = new UnweightedGridGraph(mapWidth, mapHeight);
foreach (var wall in wallPositions)
    graph.Walls.Add(wall);

var path = BreadthFirstPathfinder.Search(graph, start, goal);
```

### 9.3 — When to Use Library vs Custom

| Scenario | Recommendation |
|---|---|
| Standard grid pathfinding | **BrainAI** — tested, ready to go |
| Need flow fields or HPA* | **Custom** — BrainAI doesn't provide these |
| Need time-sliced pathfinding | **Custom** — BrainAI's Search is synchronous |
| Weighted terrain, A* | **BrainAI** — supports `WeightedNodes` |
| Navmesh | **Custom** — BrainAI focuses on grids |
| Prototype / game jam | **BrainAI** — fastest time to working pathfinding |

---

## 10 — ECS Integration

### 10.1 — Components

```csharp
/// <summary>
/// Attach to an entity to request a path. Consumed by PathfindingSystem.
/// </summary>
public record struct PathRequest(
    Point Start,
    Point Goal,
    bool AllowDiagonal = true
);

/// <summary>
/// Attached by PathfindingSystem when a path is found (or failed).
/// </summary>
public record struct PathResult(
    List<Point> Waypoints,   // null if no path
    int CurrentIndex,
    bool Failed
);

/// <summary>
/// Marks an entity as currently following a path.
/// </summary>
public record struct PathFollowing(
    float Speed,
    float WaypointRadius,
    float ArrivalRadius
);

/// <summary>
/// Tag component for flow-field-based movement instead of waypoint paths.
/// </summary>
public record struct FlowFieldFollower(
    float Speed
);
```

### 10.2 — PathfindingSystem (Time-Sliced)

Processing every path request in a single frame causes spikes. Time-slicing spreads the work across frames.

```csharp
public class PathfindingSystem
{
    private readonly AStarPathfinder _pathfinder;
    private readonly int _maxPerFrame;

    public PathfindingSystem(GridGraph graph, int maxRequestsPerFrame = 3)
    {
        _pathfinder = new AStarPathfinder(graph);
        _maxPerFrame = maxRequestsPerFrame;
    }

    public void Update(World world)
    {
        int processed = 0;

        var query = new QueryDescription().WithAll<PathRequest, Position>();

        world.Query(in query, (Entity entity, ref PathRequest req, ref Position pos) =>
        {
            if (processed >= _maxPerFrame) return;
            processed++;

            var path = _pathfinder.FindPath(req.Start, req.Goal, req.AllowDiagonal);

            if (path != null)
            {
                // Optionally smooth the path
                path = PathSmoothing.SmoothPathLOS(path, _pathfinder._graph);

                world.Add(entity, new PathResult(
                    Waypoints: path,
                    CurrentIndex: 0,
                    Failed: false
                ));
            }
            else
            {
                world.Add(entity, new PathResult(
                    Waypoints: null,
                    CurrentIndex: 0,
                    Failed: true
                ));
            }

            // Remove the request — it's been handled
            world.Remove<PathRequest>(entity);
        });
    }
}
```

### 10.3 — MovementSystem (Following Waypoints)

```csharp
public class PathMovementSystem
{
    public void Update(World world, float dt)
    {
        var query = new QueryDescription()
            .WithAll<Position, PathResult, PathFollowing>();

        world.Query(in query, (Entity entity,
            ref Position pos, ref PathResult path, ref PathFollowing follow) =>
        {
            if (path.Waypoints == null || path.CurrentIndex >= path.Waypoints.Count)
            {
                // Path complete or invalid — clean up
                world.Remove<PathResult>(entity);
                return;
            }

            var targetCell = path.Waypoints[path.CurrentIndex];
            var targetWorld = new Vector2(
                targetCell.X * TileSize + TileSize * 0.5f,
                targetCell.Y * TileSize + TileSize * 0.5f);

            var offset = targetWorld - pos.Value;
            float dist = offset.Length();

            bool isLast = path.CurrentIndex == path.Waypoints.Count - 1;

            if (dist < follow.WaypointRadius)
            {
                if (isLast)
                {
                    // Arrived at destination
                    pos.Value = targetWorld;
                    world.Remove<PathResult>(entity);
                    return;
                }
                path.CurrentIndex++;
                return;
            }

            // Move toward waypoint
            float speed = follow.Speed;
            if (isLast && dist < follow.ArrivalRadius)
                speed *= (dist / follow.ArrivalRadius); // slow down on arrival

            var direction = offset / dist;
            pos.Value += direction * speed * dt;
        });
    }

    private const float TileSize = 16f; // adjust to your tile size
}
```

### 10.4 — Flow Field Movement System

```csharp
public class FlowFieldMovementSystem
{
    private readonly FlowField _flowField;
    private readonly float _tileSize;

    public FlowFieldMovementSystem(FlowField flowField, float tileSize = 16f)
    {
        _flowField = flowField;
        _tileSize = tileSize;
    }

    public void Update(World world, float dt)
    {
        var query = new QueryDescription()
            .WithAll<Position, FlowFieldFollower>();

        world.Query(in query, (ref Position pos, ref FlowFieldFollower follower) =>
        {
            var cell = new Point(
                (int)(pos.Value.X / _tileSize),
                (int)(pos.Value.Y / _tileSize));

            var dir = _flowField.GetDirection(cell);
            if (dir == Vector2.Zero) return; // at goal or unreachable

            pos.Value += dir * follower.Speed * dt;
        });
    }
}
```

### 10.5 — Requesting a Path

```csharp
// Somewhere in your AI or input handling:
world.Add(entity, new PathRequest(
    Start: WorldToCell(transform.Position),
    Goal: WorldToCell(targetPosition),
    AllowDiagonal: true
));

world.Add(entity, new PathFollowing(
    Speed: 120f,
    WaypointRadius: 4f,
    ArrivalRadius: 16f
));
```

---

## 11 — Debug Visualization

Visualizing pathfinding data is critical for tuning. All examples below use a `SpriteBatch` or primitive drawing helper.

### 11.1 — Drawing Paths

```csharp
public static class PathDebug
{
    /// <summary>
    /// Draw a path as connected line segments.
    /// </summary>
    public static void DrawPath(SpriteBatch batch, Texture2D pixel,
        List<Point> path, float tileSize, Color color, float thickness = 2f)
    {
        if (path == null || path.Count < 2) return;

        for (int i = 0; i < path.Count - 1; i++)
        {
            var a = CellCenter(path[i], tileSize);
            var b = CellCenter(path[i + 1], tileSize);
            DrawLine(batch, pixel, a, b, color, thickness);
        }

        // Draw waypoint dots
        foreach (var p in path)
        {
            var center = CellCenter(p, tileSize);
            batch.Draw(pixel,
                new Rectangle((int)(center.X - 2), (int)(center.Y - 2), 4, 4),
                Color.White);
        }
    }

    private static Vector2 CellCenter(Point cell, float tileSize)
        => new(cell.X * tileSize + tileSize * 0.5f,
               cell.Y * tileSize + tileSize * 0.5f);

    public static void DrawLine(SpriteBatch batch, Texture2D pixel,
        Vector2 a, Vector2 b, Color color, float thickness)
    {
        var diff = b - a;
        float angle = MathF.Atan2(diff.Y, diff.X);
        float length = diff.Length();

        batch.Draw(pixel,
            new Rectangle((int)a.X, (int)a.Y, (int)length, (int)thickness),
            null, color, angle, Vector2.Zero, SpriteEffects.None, 0);
    }
}
```

### 11.2 — Drawing Grid Overlays (Walkability + Costs)

```csharp
public static void DrawGridOverlay(SpriteBatch batch, Texture2D pixel,
    GridGraph graph, float tileSize, float alpha = 0.3f)
{
    for (int y = 0; y < graph.Height; y++)
    for (int x = 0; x < graph.Width; x++)
    {
        var p = new Point(x, y);
        var rect = new Rectangle(
            (int)(x * tileSize), (int)(y * tileSize),
            (int)tileSize, (int)tileSize);

        if (!graph.IsWalkable(p))
        {
            batch.Draw(pixel, rect, Color.Red * alpha);
        }
        else
        {
            float cost = graph.GetCost(p);
            if (cost > 1.0f)
            {
                // Shade expensive terrain yellow-to-orange
                float t = MathHelper.Clamp((cost - 1f) / 4f, 0f, 1f);
                var color = Color.Lerp(Color.Yellow, Color.OrangeRed, t);
                batch.Draw(pixel, rect, color * alpha);
            }
        }
    }
}
```

### 11.3 — Drawing Flow Field Arrows

```csharp
public static void DrawFlowField(SpriteBatch batch, Texture2D pixel,
    FlowField field, float tileSize, Color color)
{
    for (int y = 0; y < field.Height; y++)
    for (int x = 0; x < field.Width; x++)
    {
        var dir = field.GetDirection(new Point(x, y));
        if (dir == Vector2.Zero) continue;

        var center = new Vector2(
            x * tileSize + tileSize * 0.5f,
            y * tileSize + tileSize * 0.5f);

        var tip = center + dir * tileSize * 0.35f;
        PathDebug.DrawLine(batch, pixel, center, tip, color, 1f);

        // Small arrowhead
        var perp = new Vector2(-dir.Y, dir.X) * 3f;
        var arrowBack = tip - dir * 4f;
        PathDebug.DrawLine(batch, pixel, tip, arrowBack + perp, color, 1f);
        PathDebug.DrawLine(batch, pixel, tip, arrowBack - perp, color, 1f);
    }
}
```

### 11.4 — Drawing NavMesh Polygons

```csharp
public static void DrawNavMesh(SpriteBatch batch, Texture2D pixel,
    NavMesh mesh, Color edgeColor, Color fillColor)
{
    foreach (var poly in mesh.Polygons)
    {
        // Draw edges
        for (int i = 0; i < poly.Vertices.Length; i++)
        {
            var a = poly.Vertices[i];
            var b = poly.Vertices[(i + 1) % poly.Vertices.Length];
            PathDebug.DrawLine(batch, pixel, a, b, edgeColor, 1f);
        }

        // Draw portal edges in a different color
        foreach (var edge in poly.Edges)
        {
            if (edge.NeighborPolyId >= 0)
            {
                PathDebug.DrawLine(batch, pixel,
                    edge.Left, edge.Right, Color.Cyan * 0.5f, 2f);
            }
        }
    }
}
```

### 11.5 — Drawing A* Open/Closed Sets

For debugging A* itself, expose the open and closed sets and render them as colored cell overlays:

```csharp
public static void DrawSearchState(SpriteBatch batch, Texture2D pixel,
    HashSet<Point> closedSet, PriorityQueue<Point, float> openSet,
    float tileSize)
{
    // Closed set — light blue
    foreach (var p in closedSet)
    {
        batch.Draw(pixel,
            new Rectangle((int)(p.X * tileSize), (int)(p.Y * tileSize),
                (int)tileSize, (int)tileSize),
            Color.LightBlue * 0.4f);
    }

    // Open set — light green (note: iterating PQ requires a copy)
    // In practice, maintain a parallel HashSet<Point> for the open set
    // to avoid PQ iteration overhead during debug rendering.
}
```

### 11.6 — Toggling Debug Modes

Wrap all debug drawing behind a flag so it's zero-cost in release:

```csharp
public static class PathDebugConfig
{
    public static bool ShowPaths = false;
    public static bool ShowGrid = false;
    public static bool ShowFlowField = false;
    public static bool ShowNavMesh = false;
    public static bool ShowSearchState = false;
}

// In your draw loop:
if (PathDebugConfig.ShowPaths)
    PathDebug.DrawPath(batch, pixel, currentPath, 16f, Color.Lime);
if (PathDebugConfig.ShowGrid)
    PathDebug.DrawGridOverlay(batch, pixel, gridGraph, 16f);
if (PathDebugConfig.ShowFlowField)
    PathDebug.DrawFlowField(batch, pixel, flowField, 16f, Color.White);
if (PathDebugConfig.ShowNavMesh)
    PathDebug.DrawNavMesh(batch, pixel, navMesh, Color.Yellow, Color.Green * 0.2f);
```

---

## Quick Reference — Algorithm Selection

| Units | Destinations | Map Size | Best Algorithm |
|---|---|---|---|
| 1-10 | Different | Small-Medium | **A*** |
| 1-10 | Different | Large (500+) | **HPA*** |
| 50+ | Same target | Any | **Flow Field** |
| Any | Any | Uniform cost grid | **JPS** (or A* + JPS) |
| Any | Any | Freeform geometry | **NavMesh** |
| 1-5 | Different | Small | **BFS** (unweighted) |

---

## Performance Tips

1. **Object pooling** — Reuse `List<Point>` and `Dictionary` allocations across pathfinding calls. A* generates lots of garbage otherwise.
2. **Time-slicing** — Never run more than 2-3 A* searches per frame. Queue excess requests.
3. **Cache paths** — If many units go to the same place, compute once and share (or use flow fields).
4. **Hierarchical first** — On large maps, always try HPA* before raw A*.
5. **Profile heuristics** — Octile is usually the best default for 8-directional grids. Manhattan overestimates diagonals and expands too many nodes.
6. **Grid resolution** — Bigger cells = fewer nodes = faster search. Use the coarsest grid your gameplay allows.
7. **Early exit** — If start == goal, return immediately. If goal is unreachable (flood-fill precompute), skip A* entirely.
8. **Connectivity regions** — Precompute connected components with flood fill. If start and goal are in different regions, no path exists — skip the search.

---

*Pathfinding is one of those systems you build once and lean on forever. Get A* working first, add smoothing, then graduate to flow fields or HPA* when your game demands it. BrainAI covers the basics; go custom when you need time-slicing, flow fields, or navmesh.*
