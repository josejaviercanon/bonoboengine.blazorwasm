# G4 — AI Systems
> **Category:** Guide · **Related:** [R2 Capability Matrix](../R/R2_capability_matrix.md) · [C1 Genre Reference](../C/C1_genre_reference.md)

> Comprehensive implementation guide covering AI architectures, pathfinding, perception, and ECS integration patterns for MonoGame + Arch ECS.

---

## 1. Finite State Machines (FSM)

The workhorse of game AI. Simple, debuggable, and perfect for entities with clearly defined behavioral modes.

### Core Implementation

```csharp
// --- Components ---
public record struct AiState(AiStateId Current, AiStateId Previous, float TimeInState);
public enum AiStateId { Idle, Patrol, Chase, Attack, Flee, Dead }

// --- FSM Definition ---
public class StateMachine<T> where T : Enum
{
    private readonly Dictionary<T, State<T>> _states = new();
    private State<T> _current;

    public T CurrentStateId => _current.Id;

    public void AddState(State<T> state) => _states[state.Id] = state;

    public void SetInitialState(T id)
    {
        _current = _states[id];
        _current.OnEnter?.Invoke();
    }

    public void Transition(T to)
    {
        if (EqualityComparer<T>.Default.Equals(_current.Id, to)) return;
        _current.OnExit?.Invoke();
        _current = _states[to];
        _current.OnEnter?.Invoke();
    }

    public void Update(float dt) => _current.OnUpdate?.Invoke(dt);
}

public class State<T> where T : Enum
{
    public T Id { get; init; }
    public Action? OnEnter { get; init; }
    public Action? OnExit { get; init; }
    public Action<float>? OnUpdate { get; init; }
}
```

### Arch ECS Integration

```csharp
public partial class AiStateSystem : BaseSystem<World, float>
{
    public AiStateSystem(World world) : base(world) { }

    [Query]
    public void UpdateState([Data] in float dt, Entity entity,
        ref AiState ai, ref Position pos, ref Health hp)
    {
        ai.TimeInState += dt;

        var next = ai.Current switch
        {
            AiStateId.Patrol when CanSeePlayer(entity) => AiStateId.Chase,
            AiStateId.Chase when !CanSeePlayer(entity)  => AiStateId.Patrol,
            AiStateId.Chase when InAttackRange(entity)   => AiStateId.Attack,
            AiStateId.Attack when !InAttackRange(entity) => AiStateId.Chase,
            _ when hp.Current < hp.Max * 0.2f            => AiStateId.Flee,
            _ => ai.Current
        };

        if (next != ai.Current)
        {
            ai.Previous = ai.Current;
            ai.Current = next;
            ai.TimeInState = 0f;
        }
    }
}
```

### Hierarchical FSM (HFSM)

Nest state machines — a "Combat" super-state contains sub-states like Engage, Retreat, UseAbility:

```csharp
public class HierarchicalState<T> : State<T> where T : Enum
{
    public StateMachine<T>? SubMachine { get; init; }
}

// Usage: the Combat state has its own internal FSM
var combatSub = new StateMachine<CombatSubState>();
combatSub.AddState(new State<CombatSubState> { Id = CombatSubState.Engage, ... });
combatSub.AddState(new State<CombatSubState> { Id = CombatSubState.Retreat, ... });

// Parent FSM updates sub-machine automatically
var combatState = new HierarchicalState<AiStateId>
{
    Id = AiStateId.Attack,
    OnEnter = () => combatSub.SetInitialState(CombatSubState.Engage),
    OnUpdate = dt => combatSub.Update(dt),
};
```

---

## 2. Behavior Trees

More expressive than FSMs for complex, multi-step decision-making. Nodes return `Success`, `Failure`, or `Running`.

### Node Types

| Node | Behavior |
|------|----------|
| **Sequence** | Runs children left→right; fails on first failure |
| **Selector** | Runs children left→right; succeeds on first success |
| **Parallel** | Runs all children simultaneously; configurable success/fail policy |
| **Decorator** | Wraps one child (Inverter, Repeater, UntilFail, Cooldown) |
| **Leaf** | Executes an action or checks a condition |

### Custom Implementation

```csharp
public enum BtStatus { Success, Failure, Running }

public abstract class BtNode
{
    public abstract BtStatus Tick(BtContext ctx);
}

public class Sequence : BtNode
{
    private readonly List<BtNode> _children = new();
    private int _runningIndex;

    public Sequence(params BtNode[] children) => _children.AddRange(children);

    public override BtStatus Tick(BtContext ctx)
    {
        for (int i = _runningIndex; i < _children.Count; i++)
        {
            var status = _children[i].Tick(ctx);
            if (status == BtStatus.Running) { _runningIndex = i; return BtStatus.Running; }
            if (status == BtStatus.Failure) { _runningIndex = 0; return BtStatus.Failure; }
        }
        _runningIndex = 0;
        return BtStatus.Success;
    }
}

public class Selector : BtNode
{
    private readonly List<BtNode> _children = new();
    private int _runningIndex;

    public Selector(params BtNode[] children) => _children.AddRange(children);

    public override BtStatus Tick(BtContext ctx)
    {
        for (int i = _runningIndex; i < _children.Count; i++)
        {
            var status = _children[i].Tick(ctx);
            if (status == BtStatus.Running) { _runningIndex = i; return BtStatus.Running; }
            if (status == BtStatus.Success) { _runningIndex = 0; return BtStatus.Success; }
        }
        _runningIndex = 0;
        return BtStatus.Failure;
    }
}

// Leaf nodes — conditions and actions
public class Condition : BtNode
{
    private readonly Func<BtContext, bool> _check;
    public Condition(Func<BtContext, bool> check) => _check = check;
    public override BtStatus Tick(BtContext ctx) =>
        _check(ctx) ? BtStatus.Success : BtStatus.Failure;
}

public class ActionNode : BtNode
{
    private readonly Func<BtContext, BtStatus> _action;
    public ActionNode(Func<BtContext, BtStatus> action) => _action = action;
    public override BtStatus Tick(BtContext ctx) => _action(ctx);
}
```

### Blackboard Pattern

Shared data store for the behavior tree — avoids coupling between nodes:

```csharp
public class Blackboard
{
    private readonly Dictionary<string, object> _data = new();

    public T Get<T>(string key) => (T)_data[key];
    public void Set<T>(string key, T value) => _data[key] = value!;
    public bool Has(string key) => _data.ContainsKey(key);
}

public class BtContext
{
    public Entity Entity { get; init; }
    public World World { get; init; }
    public Blackboard Blackboard { get; } = new();
    public float DeltaTime { get; set; }
}
```

### Builder API

```csharp
public class BtBuilder
{
    private readonly Stack<List<BtNode>> _stack = new();

    public BtBuilder Sequence() { _stack.Push(new()); return this; }
    public BtBuilder Selector() { _stack.Push(new()); return this; }

    public BtBuilder Condition(Func<BtContext, bool> check)
    { _stack.Peek().Add(new Condition(check)); return this; }

    public BtBuilder Do(Func<BtContext, BtStatus> action)
    { _stack.Peek().Add(new ActionNode(action)); return this; }

    public BtBuilder End()
    {
        var children = _stack.Pop();
        var node = _stack.Count > 0 ? /* ... composite */ null : children[0];
        // Simplified — real impl tracks composite type
        return this;
    }

    public BtNode Build() => _stack.Pop()[0];
}

// Usage:
var tree = new BtBuilder()
    .Selector()
        .Sequence()
            .Condition(ctx => ctx.Blackboard.Get<float>("enemyDist") < 50f)
            .Do(ctx => AttackEnemy(ctx))
        .End()
        .Sequence()
            .Condition(ctx => ctx.Blackboard.Get<float>("health") < 0.3f)
            .Do(ctx => FleeToSafety(ctx))
        .End()
        .Do(ctx => Patrol(ctx))
    .End()
    .Build();
```

---

## 3. GOAP (Goal-Oriented Action Planning)

Agents declare goals and available actions; a planner finds the cheapest action sequence. Ideal for emergent AI.

### Data Model

```csharp
public class WorldState : Dictionary<string, bool> { }

public class GoapAction
{
    public string Name { get; init; } = "";
    public float Cost { get; init; } = 1f;
    public WorldState Preconditions { get; init; } = new();
    public WorldState Effects { get; init; } = new();
    public Func<bool>? IsValid { get; init; }        // runtime check
    public Func<BtStatus>? Execute { get; init; }     // perform the action
}

public class GoapGoal
{
    public string Name { get; init; } = "";
    public WorldState DesiredState { get; init; } = new();
    public float Priority { get; init; }
}
```

### A* Planner

```csharp
public static class GoapPlanner
{
    public static List<GoapAction>? Plan(
        WorldState current, GoapGoal goal, List<GoapAction> available)
    {
        var open = new PriorityQueue<PlanNode, float>();
        open.Enqueue(new(current, new(), 0), 0);

        while (open.Count > 0)
        {
            var node = open.Dequeue();
            if (GoalMet(node.State, goal.DesiredState))
                return node.Actions;

            foreach (var action in available)
            {
                if (action.IsValid?.Invoke() == false) continue;
                if (!PreconditionsMet(node.State, action.Preconditions)) continue;

                var newState = ApplyEffects(node.State, action.Effects);
                var newCost = node.Cost + action.Cost;
                var newActions = new List<GoapAction>(node.Actions) { action };
                var heuristic = EstimateDistance(newState, goal.DesiredState);
                open.Enqueue(new(newState, newActions, newCost), newCost + heuristic);
            }
        }
        return null; // no plan found
    }

    private static bool GoalMet(WorldState current, WorldState desired) =>
        desired.All(kv => current.TryGetValue(kv.Key, out var v) && v == kv.Value);

    private static bool PreconditionsMet(WorldState s, WorldState pre) =>
        pre.All(kv => s.TryGetValue(kv.Key, out var v) && v == kv.Value);

    private static WorldState ApplyEffects(WorldState s, WorldState effects)
    {
        var next = new WorldState(s);
        foreach (var kv in effects) next[kv.Key] = kv.Value;
        return next;
    }

    private static float EstimateDistance(WorldState s, WorldState goal) =>
        goal.Count(kv => !s.TryGetValue(kv.Key, out var v) || v != kv.Value);

    private record PlanNode(WorldState State, List<GoapAction> Actions, float Cost);
}
```

### Guard AI Example

```csharp
var actions = new List<GoapAction>
{
    new() { Name = "Patrol",     Cost = 1, Effects = {{ "atPatrolPoint", true }},
            Preconditions = {{ "isArmed", true }} },
    new() { Name = "GetWeapon",  Cost = 2, Effects = {{ "isArmed", true }},
            Preconditions = {{ "atArmory", true }} },
    new() { Name = "GoToArmory", Cost = 3, Effects = {{ "atArmory", true }} },
    new() { Name = "AttackIntruder", Cost = 1, Effects = {{ "intruderDown", true }},
            Preconditions = {{ "isArmed", true }, { "canSeeIntruder", true }} },
};

var goal = new GoapGoal { Name = "EliminateIntruder",
    DesiredState = {{ "intruderDown", true }}, Priority = 10 };

var plan = GoapPlanner.Plan(currentWorldState, goal, actions);
// Result: GoToArmory → GetWeapon → AttackIntruder
```

---

## 4. Utility AI

Score every possible action with response curves; pick the highest. Handles nuance that BTs and FSMs struggle with.

### Response Curves

```csharp
public static class ResponseCurve
{
    public static float Linear(float x, float m = 1f, float b = 0f)
        => Math.Clamp(m * x + b, 0f, 1f);

    public static float Quadratic(float x, float exp = 2f)
        => Math.Clamp(MathF.Pow(x, exp), 0f, 1f);

    public static float Logistic(float x, float steepness = 10f, float midpoint = 0.5f)
        => 1f / (1f + MathF.Exp(-steepness * (x - midpoint)));

    public static float Step(float x, float threshold = 0.5f)
        => x >= threshold ? 1f : 0f;
}
```

### Scoring & Selection

```csharp
public class UtilityAction
{
    public string Name { get; init; } = "";
    public List<Func<Entity, World, float>> Considerations { get; init; } = new();
    public Action<Entity, World> Execute { get; init; } = (_, _) => { };

    // Compensated multiplicative scoring (prevents one zero from killing everything)
    public float Score(Entity e, World w)
    {
        if (Considerations.Count == 0) return 0f;
        float score = 1f;
        foreach (var c in Considerations)
            score *= c(e, w);
        // Compensation factor: raise to 1/n power to normalize
        return MathF.Pow(score, 1f / Considerations.Count);
    }
}

public static class UtilitySelector
{
    public static UtilityAction? Select(List<UtilityAction> actions, Entity e, World w)
    {
        UtilityAction? best = null;
        float bestScore = 0f;
        foreach (var a in actions)
        {
            var s = a.Score(e, w);
            if (s > bestScore) { bestScore = s; best = a; }
        }
        return best;
    }
}
```

**When to use over BT/FSM:** When an agent has many actions and context varies continuously (hunger, fear, health, ammo). Utility AI avoids the combinatorial explosion of BT conditions and FSM transitions. Think Sims-style needs or dynamic squad tactics.

---

## 5. Steering Behaviors

Continuous movement using force accumulation. Combine atomic behaviors for rich emergent motion.

```csharp
public record struct SteeringAgent(Vector2 Position, Vector2 Velocity, float MaxSpeed, float MaxForce);

public static class Steering
{
    public static Vector2 Seek(SteeringAgent a, Vector2 target)
    {
        var desired = Vector2.Normalize(target - a.Position) * a.MaxSpeed;
        return Truncate(desired - a.Velocity, a.MaxForce);
    }

    public static Vector2 Flee(SteeringAgent a, Vector2 threat) =>
        -Seek(a, threat);

    public static Vector2 Arrive(SteeringAgent a, Vector2 target, float slowRadius = 100f)
    {
        var offset = target - a.Position;
        float dist = offset.Length();
        if (dist < 1f) return -a.Velocity; // brake
        float speed = dist < slowRadius ? a.MaxSpeed * (dist / slowRadius) : a.MaxSpeed;
        var desired = (offset / dist) * speed;
        return Truncate(desired - a.Velocity, a.MaxForce);
    }

    public static Vector2 Wander(SteeringAgent a, ref float wanderAngle, float radius = 30f,
        float dist = 60f, float jitter = 0.3f)
    {
        wanderAngle += (Random.Shared.NextSingle() - 0.5f) * jitter;
        var circleCenter = Vector2.Normalize(a.Velocity) * dist;
        var offset = new Vector2(MathF.Cos(wanderAngle), MathF.Sin(wanderAngle)) * radius;
        return Truncate(circleCenter + offset, a.MaxForce);
    }

    // --- Flocking ---
    public static Vector2 Separation(SteeringAgent a, Span<Vector2> neighbors, float desiredDist = 25f)
    {
        var force = Vector2.Zero;
        foreach (var n in neighbors)
        {
            var diff = a.Position - n;
            float d = diff.Length();
            if (d > 0 && d < desiredDist)
                force += Vector2.Normalize(diff) / d;
        }
        return Truncate(force, a.MaxForce);
    }

    public static Vector2 Alignment(SteeringAgent a, Span<Vector2> neighborVelocities)
    {
        if (neighborVelocities.Length == 0) return Vector2.Zero;
        var avg = Vector2.Zero;
        foreach (var v in neighborVelocities) avg += v;
        avg /= neighborVelocities.Length;
        return Truncate(avg - a.Velocity, a.MaxForce);
    }

    public static Vector2 Cohesion(SteeringAgent a, Span<Vector2> neighbors)
    {
        if (neighbors.Length == 0) return Vector2.Zero;
        var center = Vector2.Zero;
        foreach (var n in neighbors) center += n;
        center /= neighbors.Length;
        return Seek(a, center);
    }

    private static Vector2 Truncate(Vector2 v, float max) =>
        v.LengthSquared() > max * max ? Vector2.Normalize(v) * max : v;
}
```

### SpatialHash for Neighbor Queries

```csharp
public class SpatialHash<T>
{
    private readonly float _cellSize;
    private readonly Dictionary<long, List<(Vector2 Pos, T Item)>> _cells = new();

    public SpatialHash(float cellSize) => _cellSize = cellSize;

    public void Clear() => _cells.Clear();

    public void Insert(Vector2 pos, T item)
    {
        var key = CellKey(pos);
        if (!_cells.TryGetValue(key, out var list))
            _cells[key] = list = new();
        list.Add((pos, item));
    }

    public void QueryRadius(Vector2 center, float radius, List<T> results)
    {
        int minX = (int)MathF.Floor((center.X - radius) / _cellSize);
        int maxX = (int)MathF.Floor((center.X + radius) / _cellSize);
        int minY = (int)MathF.Floor((center.Y - radius) / _cellSize);
        int maxY = (int)MathF.Floor((center.Y + radius) / _cellSize);
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

    private long CellKey(Vector2 p) =>
        PackKey((int)MathF.Floor(p.X / _cellSize), (int)MathF.Floor(p.Y / _cellSize));

    private static long PackKey(int x, int y) => ((long)x << 32) | (uint)y;
}
```

---

## 6. Perception Systems

### Vision Cone (Field of View)

```csharp
public record struct Vision(float Range, float HalfAngleDeg);

public static bool InVisionCone(Vector2 origin, Vector2 facing, Vision vision, Vector2 target)
{
    var toTarget = target - origin;
    float dist = toTarget.Length();
    if (dist > vision.Range || dist < 0.01f) return false;

    float dot = Vector2.Dot(Vector2.Normalize(facing), toTarget / dist);
    float halfAngleRad = MathHelper.ToRadians(vision.HalfAngleDeg);
    return dot >= MathF.Cos(halfAngleRad);
}
```

### Hearing (Sound Propagation)

```csharp
public record struct SoundEvent(Vector2 Origin, float Radius, float Intensity);
public record struct Hearing(float Sensitivity); // multiplier on radius

public static bool CanHear(Vector2 listenerPos, Hearing hearing, SoundEvent sound)
{
    float effectiveRadius = sound.Radius * hearing.Sensitivity;
    return Vector2.DistanceSquared(listenerPos, sound.Origin) <=
           effectiveRadius * effectiveRadius;
}
```

### Line-of-Sight Raycasting (Tile Map)

```csharp
public static bool HasLineOfSight(Vector2 from, Vector2 to, bool[,] blocked, int tileSize)
{
    // Bresenham / DDA ray march through the tile grid
    var dir = to - from;
    float dist = dir.Length();
    if (dist < 1f) return true;
    dir /= dist;

    float step = tileSize * 0.5f;
    for (float t = 0; t < dist; t += step)
    {
        var p = from + dir * t;
        int tx = (int)(p.X / tileSize);
        int ty = (int)(p.Y / tileSize);
        if (tx >= 0 && ty >= 0 && tx < blocked.GetLength(0) && ty < blocked.GetLength(1))
            if (blocked[tx, ty]) return false;
    }
    return true;
}
```

---

## 7. Pathfinding

### A* on Grids

```csharp
public static List<Point>? AStar(bool[,] walkable, Point start, Point goal)
{
    int w = walkable.GetLength(0), h = walkable.GetLength(1);
    var open = new PriorityQueue<Point, float>();
    var cameFrom = new Dictionary<Point, Point>();
    var gScore = new Dictionary<Point, float> { [start] = 0 };

    open.Enqueue(start, Heuristic(start, goal));

    while (open.Count > 0)
    {
        var current = open.Dequeue();
        if (current == goal) return ReconstructPath(cameFrom, current);

        foreach (var next in Neighbors(current, w, h))
        {
            if (!walkable[next.X, next.Y]) continue;
            float tentG = gScore[current] + (next.X != current.X && next.Y != current.Y ? 1.414f : 1f);
            if (tentG < gScore.GetValueOrDefault(next, float.MaxValue))
            {
                cameFrom[next] = current;
                gScore[next] = tentG;
                open.Enqueue(next, tentG + Heuristic(next, goal));
            }
        }
    }
    return null;

    static float Heuristic(Point a, Point b) =>
        MathF.Abs(a.X - b.X) + MathF.Abs(a.Y - b.Y); // Manhattan

    static IEnumerable<Point> Neighbors(Point p, int w, int h)
    {
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
        {
            if (dx == 0 && dy == 0) continue;
            int nx = p.X + dx, ny = p.Y + dy;
            if (nx >= 0 && ny >= 0 && nx < w && ny < h)
                yield return new Point(nx, ny);
        }
    }
}
```

### Flow Fields (RTS)

All units heading to the same target share one precomputed field — O(grid) cost vs O(unit × grid) for individual A*.

```csharp
public class FlowField
{
    public Vector2[,] Flow { get; }
    private readonly float[,] _cost;

    public FlowField(bool[,] walkable, Point goal)
    {
        int w = walkable.GetLength(0), h = walkable.GetLength(1);
        _cost = new float[w, h];
        Flow = new Vector2[w, h];

        // Dijkstra from goal
        for (int x = 0; x < w; x++)
        for (int y = 0; y < h; y++)
            _cost[x, y] = float.MaxValue;
        _cost[goal.X, goal.Y] = 0;

        var queue = new Queue<Point>();
        queue.Enqueue(goal);

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            foreach (var n in GridNeighbors(cur, w, h))
            {
                if (!walkable[n.X, n.Y]) continue;
                float newCost = _cost[cur.X, cur.Y] + 1;
                if (newCost < _cost[n.X, n.Y])
                {
                    _cost[n.X, n.Y] = newCost;
                    queue.Enqueue(n);
                }
            }
        }

        // Build flow vectors (point toward lowest-cost neighbor)
        for (int x = 0; x < w; x++)
        for (int y = 0; y < h; y++)
        {
            float best = _cost[x, y];
            var dir = Vector2.Zero;
            foreach (var n in GridNeighbors(new(x, y), w, h))
            {
                if (_cost[n.X, n.Y] < best)
                {
                    best = _cost[n.X, n.Y];
                    dir = new Vector2(n.X - x, n.Y - y);
                }
            }
            Flow[x, y] = dir == Vector2.Zero ? Vector2.Zero : Vector2.Normalize(dir);
        }
    }
}
```

### Jump Point Search

Optimization of A* on uniform-cost grids — prunes symmetric paths by "jumping" along straight lines. Use the same A* skeleton but replace neighbor expansion with JPS jump logic. Gains **5-10x** speedup on open maps. Libraries: `RoyT.AStar` or implement per the original Harabor & Grastien paper.

### Hierarchical Pathfinding (HPA*)

For large maps, divide the grid into clusters, precompute inter-cluster edges, then pathfind on the abstract graph first and refine within clusters. Reduces search space dramatically for open-world or RTS maps.

---

## 8. Influence Maps

Spatial scoring grids for strategic AI decisions — where is dangerous, where is safe, where are resources.

```csharp
public class InfluenceMap
{
    private readonly float[,] _map;
    private readonly float[,] _buffer;
    public int Width { get; }
    public int Height { get; }

    public InfluenceMap(int w, int h) { Width = w; Height = h; _map = new float[w, h]; _buffer = new float[w, h]; }

    public void SetInfluence(int x, int y, float value) => _map[x, y] = value;
    public float GetInfluence(int x, int y) => _map[x, y];

    // Stamp radial influence (e.g., enemy presence, resource value)
    public void Stamp(int cx, int cy, float strength, int radius)
    {
        for (int dx = -radius; dx <= radius; dx++)
        for (int dy = -radius; dy <= radius; dy++)
        {
            int nx = cx + dx, ny = cy + dy;
            if (nx < 0 || ny < 0 || nx >= Width || ny >= Height) continue;
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            if (dist <= radius)
                _map[nx, ny] += strength * (1f - dist / radius);
        }
    }

    // Diffuse + decay each frame for smooth propagation
    public void Propagate(float decay = 0.9f, float diffusion = 0.1f)
    {
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
        {
            float sum = _map[x, y] * (1f - diffusion);
            int count = 0;
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && ny >= 0 && nx < Width && ny < Height)
                { sum += _map[nx, ny] * diffusion / 8f; count++; }
            }
            _buffer[x, y] = sum * decay;
        }
        Array.Copy(_buffer, _map, _map.Length);
    }

    public void Clear() => Array.Clear(_map);
}

// Usage: threat map — stamp enemy positions, query safest cell for fleeing AI
var threatMap = new InfluenceMap(mapWidth, mapHeight);
threatMap.Clear();
// Each frame: stamp enemies
foreach (var enemyPos in enemyPositions)
    threatMap.Stamp((int)enemyPos.X / tileSize, (int)enemyPos.Y / tileSize, 1f, 8);
threatMap.Propagate();
// Find minimum-threat cell near agent for retreat destination
```

---

## 9. Boss Pattern Design

### Phase-Based State Machine

```csharp
public record struct BossPhase(int PhaseIndex, float PhaseHealthThreshold);
public record struct AttackPattern(int CurrentAttack, float CooldownTimer);

public partial class BossAiSystem : BaseSystem<World, float>
{
    private static readonly BossAttack[][] PhaseAttacks = new[]
    {
        new[] { BossAttack.Slam, BossAttack.Charge, BossAttack.Slam },            // Phase 1
        new[] { BossAttack.Fireball, BossAttack.Spin, BossAttack.Fireball, BossAttack.Charge }, // Phase 2
        new[] { BossAttack.Enrage, BossAttack.Fireball, BossAttack.Slam, BossAttack.Spin },     // Phase 3
    };

    [Query]
    public void UpdateBoss([Data] in float dt, ref BossPhase phase,
        ref AttackPattern pattern, ref Health hp, ref Position pos)
    {
        // Phase transitions based on HP thresholds
        float hpPct = (float)hp.Current / hp.Max;
        int targetPhase = hpPct switch
        {
            > 0.66f => 0,
            > 0.33f => 1,
            _       => 2
        };
        if (targetPhase != phase.PhaseIndex)
        {
            phase.PhaseIndex = targetPhase;
            pattern.CurrentAttack = 0;
            // Trigger phase transition animation/effect here
        }

        // Cycle through attack pattern
        pattern.CooldownTimer -= dt;
        if (pattern.CooldownTimer <= 0)
        {
            var attacks = PhaseAttacks[phase.PhaseIndex];
            ExecuteAttack(attacks[pattern.CurrentAttack]);
            pattern.CurrentAttack = (pattern.CurrentAttack + 1) % attacks.Length;
            pattern.CooldownTimer = GetCooldown(phase.PhaseIndex);
        }
    }

    // Difficulty scaling: later phases have shorter cooldowns, faster projectiles
    private static float GetCooldown(int phase) => phase switch
    {
        0 => 2.0f,
        1 => 1.5f,
        2 => 0.8f,
        _ => 2.0f
    };
}
```

---

## 10. ECS-Specific AI Patterns (Arch)

### Component Decomposition

Keep AI state in small, composable components — not monolithic "AIBrain" blobs:

```csharp
// State
public record struct AiState(AiStateId Current, AiStateId Previous, float TimeInState);
public record struct PatrolPath(Vector2[] Waypoints, int CurrentIndex);
public record struct AggroTarget(Entity Target, float LastSeenTime);
public record struct BehaviorTreeRef(BtNode Root);

// Perception (tag components are zero-size)
public record struct Vision(float Range, float HalfAngleDeg);
public record struct Hearing(float Sensitivity);
public record struct PerceivedEntities(List<Entity> Visible, List<Entity> Heard);

// Steering
public record struct SteeringAgent(Vector2 Velocity, float MaxSpeed, float MaxForce);
public record struct SteeringForce(Vector2 Accumulated);

// Tags
public struct IsAggressive;  // zero-size tag component
public struct IsFleeing;
```

### System Pipeline

Order systems in a logical pipeline — perception → decision → action → physics:

```csharp
var aiSystems = new Group("AI",
    new PerceptionSystem(world),     // populates PerceivedEntities
    new BehaviorTreeSystem(world),   // ticks BTs, updates AiState
    new UtilityAiSystem(world),      // for entities using utility AI
    new SteeringSystem(world),       // accumulates steering forces
    new PathFollowingSystem(world),  // follows A* paths
    new BossAiSystem(world)          // boss-specific logic
);

// In game loop:
aiSystems.BeforeUpdate(in dt);
aiSystems.Update(in dt);
aiSystems.AfterUpdate(in dt);
```

### CommandBuffer for Safe Structural Changes

Never add/remove components mid-query. Use Arch's `CommandBuffer`:

```csharp
[Query]
public void CheckDeath([Data] in float dt, Entity entity, ref Health hp)
{
    if (hp.Current <= 0)
    {
        // DON'T: World.Remove<AiState>(entity)  ← breaks iteration
        // DO: buffer the change
        _buffer.Add(entity, new AiState(AiStateId.Dead, default, 0));
        _buffer.Remove<AggroTarget>(entity);
    }
}

public override void AfterUpdate(in float dt)
{
    _buffer.Playback(World); // apply all buffered changes safely
}
```

### Query Filtering for AI Archetypes

```csharp
// Only query entities that have AI + are alive + are not stunned
[Query]
[All<AiState, Position, Health>]
[None<Stunned, Dead>]
public void ProcessAi(Entity entity, ref AiState ai, ref Position pos, ref Health hp)
{
    // Only processes matching archetypes — zero overhead for non-AI entities
}
```

---

## Architecture Decision Guide

| Scenario | Recommended System |
|---|---|
| Simple enemy with 2-4 states | FSM |
| Complex multi-step behavior | Behavior Tree |
| Many actions, continuous context | Utility AI |
| Emergent/planning AI (guards, NPCs) | GOAP |
| Smooth movement/flocking | Steering Behaviors |
| RTS unit movement (many→one target) | Flow Fields |
| Single unit pathfinding | A* / JPS |
| Strategic macro-AI | Influence Maps |
| Boss encounters | Phase FSM + Attack Patterns |

**Combine freely** — a guard might use GOAP for high-level planning, a BT to execute each action step, steering behaviors for movement, and A* for pathfinding. The ECS architecture makes this natural: each system reads/writes its own components without coupling.
