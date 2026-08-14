# G4 — AI Systems

> **Category:** Guide · **Related:** [G40 Pathfinding](./G40_pathfinding.md) · [G64 Combat & Damage](./G64_combat_damage_systems.md) · [G52 Character Controller](./G52_character_controller.md) · [G31 Animation State Machines](./G31_animation_state_machines.md) · [G12 Design Patterns](./G12_design_patterns.md) · [G37 Tilemap Systems](./G37_tilemap_systems.md) · [G67 Object Pooling](./G67_object_pooling.md) · [R2 Capability Matrix](../reference/R2_capability_matrix.md) · [C1 Genre Reference](../../core/game-design/C1_genre_reference.md)

> Comprehensive implementation guide covering AI architectures, pathfinding, perception, group tactics, dynamic difficulty, and ECS integration patterns for MonoGame + Arch ECS. Every system is composable — combine FSMs, behavior trees, steering, and utility AI freely via ECS component composition.

---

## AI System Pipeline Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                        AI UPDATE PIPELINE                          │
│                   (runs once per AI tick, ~10-30Hz)                 │
│                                                                    │
│  ┌──────────┐   ┌──────────┐   ┌──────────┐   ┌───────────────┐   │
│  │Perception│──▶│ Decision │──▶│ Steering/ │──▶│   Physics/    │   │
│  │ System   │   │  System  │   │ Movement  │   │ Integration   │   │
│  └──────────┘   └──────────┘   └──────────┘   └───────────────┘   │
│       │              │              │                  │            │
│  Raycasts,      FSM/BT/GOAP/   Seek/Flee/       Apply forces,     │
│  cone checks,   Utility AI     Arrive/Wander    update position,   │
│  hearing,       evaluates &    accumulates       resolve collision  │
│  smell/tags     sets state     steering forces                     │
│       │              │              │                  │            │
│       ▼              ▼              ▼                  ▼            │
│  PerceivedEntities  AiState     SteeringForce    Position/Velocity │
│  (component)       (component)  (component)       (component)      │
└─────────────────────────────────────────────────────────────────────┘

Tick Rate Strategy:
  ─ Simple enemies:  5-10 Hz  (patrol, chase)
  ─ Combat AI:       15-20 Hz (reaction time matters)
  ─ Boss AI:         30 Hz    (tight patterns, phase transitions)
  ─ Steering/flocking: Every frame (smooth movement)
```

### AI Tick Rate Throttling

Not every entity needs to think every frame. Stagger AI updates to spread CPU cost:

```csharp
public record struct AiTick(float Interval, float Timer, int StaggerGroup);

public partial class AiTickSystem : BaseSystem<World, float>
{
    private int _frameCount;

    public AiTickSystem(World world) : base(world) { }

    [Query]
    [All<AiState, AiTick>]
    public void ThrottleAiTick([Data] in float dt, Entity entity, ref AiTick tick)
    {
        tick.Timer += dt;
        if (tick.Timer < tick.Interval) return;

        // Stagger: only update 1/4 of entities per AI frame
        if (_frameCount % 4 != tick.StaggerGroup) return;

        tick.Timer = 0f;
        // Entity is eligible for AI update this frame
        World.Add<AiUpdateThisFrame>(entity); // tag component
    }

    public override void BeforeUpdate(in float dt)
    {
        _frameCount++;
        // Clear previous frame's tags
        var query = new QueryDescription().WithAll<AiUpdateThisFrame>();
        World.Remove<AiUpdateThisFrame>(query);
    }
}

public struct AiUpdateThisFrame; // zero-size tag
```

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
    private readonly List<(T From, T To, float Time)> _transitionLog = new();

    public T CurrentStateId => _current.Id;
    public IReadOnlyList<(T From, T To, float Time)> TransitionLog => _transitionLog;

    public void AddState(State<T> state) => _states[state.Id] = state;

    public void SetInitialState(T id)
    {
        _current = _states[id];
        _current.OnEnter?.Invoke();
    }

    public void Transition(T to)
    {
        if (EqualityComparer<T>.Default.Equals(_current.Id, to)) return;

        _transitionLog.Add((_current.Id, to, /* game time */ 0f));
        if (_transitionLog.Count > 20) _transitionLog.RemoveAt(0); // ring buffer

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
    [All<AiState, Position, Health>]
    [None<Dead>]
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

### Pushdown Automaton (State Stack)

When you need to "pause" a state and resume it later (e.g., Patrol → interrupted by Investigate → back to Patrol):

```csharp
public class PushdownFSM<T> where T : Enum
{
    private readonly Dictionary<T, State<T>> _states = new();
    private readonly Stack<State<T>> _stack = new();

    public T CurrentStateId => _stack.Peek().Id;

    public void AddState(State<T> state) => _states[state.Id] = state;

    public void PushState(T id)
    {
        if (_stack.Count > 0) _stack.Peek().OnExit?.Invoke();
        _stack.Push(_states[id]);
        _stack.Peek().OnEnter?.Invoke();
    }

    public void PopState()
    {
        if (_stack.Count <= 1) return; // don't pop last state
        _stack.Pop().OnExit?.Invoke();
        _stack.Peek().OnEnter?.Invoke(); // resume previous
    }

    public void Update(float dt) => _stack.Peek().OnUpdate?.Invoke(dt);
}

// Example: Guard hears a sound, investigates, then resumes patrol
pushdownFSM.PushState(GuardState.Investigate);
// ... investigation completes ...
pushdownFSM.PopState(); // back to Patrol, remembering patrol waypoint
```

---

## 2. Behavior Trees

More expressive than FSMs for complex, multi-step decision-making. Nodes return `Success`, `Failure`, or `Running`.

### Node Types

| Node | Behavior | Use Case |
|------|----------|----------|
| **Sequence** | Runs children left→right; fails on first failure | "Do A, then B, then C" |
| **Selector** | Runs children left→right; succeeds on first success | "Try A, else B, else C" |
| **Parallel** | Runs all children simultaneously; configurable policy | "Do A while doing B" |
| **Decorator** | Wraps one child (Inverter, Repeater, UntilFail, Cooldown) | Modify child behavior |
| **Leaf** | Executes an action or checks a condition | Actual game logic |

### Execution Model

```
            Selector                     ← "Find something to do"
           /    |    \
      Sequence  Sequence  Leaf(Patrol)   ← Fallback: just patrol
      /    \     /    \
  Cond  Action  Cond  Action
  (enemy (attack (low  (flee)
  near?) enemy)  hp?)
```

**Key concept:** The tree is re-evaluated from the root every tick. `Running` nodes resume where they left off. This means higher-priority branches (left side) can interrupt lower-priority ones mid-execution.

### Custom Implementation

```csharp
public enum BtStatus { Success, Failure, Running }

public abstract class BtNode
{
    public abstract BtStatus Tick(BtContext ctx);
    public virtual void Reset() { } // called when parent aborts this branch
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

    public override void Reset()
    {
        _runningIndex = 0;
        foreach (var c in _children) c.Reset();
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

    public override void Reset()
    {
        _runningIndex = 0;
        foreach (var c in _children) c.Reset();
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

### Decorator Nodes

```csharp
public class Inverter : BtNode
{
    private readonly BtNode _child;
    public Inverter(BtNode child) => _child = child;
    public override BtStatus Tick(BtContext ctx) => _child.Tick(ctx) switch
    {
        BtStatus.Success => BtStatus.Failure,
        BtStatus.Failure => BtStatus.Success,
        _ => BtStatus.Running
    };
}

public class Cooldown : BtNode
{
    private readonly BtNode _child;
    private readonly float _cooldownTime;
    private float _timer;

    public Cooldown(BtNode child, float seconds)
    { _child = child; _cooldownTime = seconds; }

    public override BtStatus Tick(BtContext ctx)
    {
        _timer -= ctx.DeltaTime;
        if (_timer > 0) return BtStatus.Failure;
        var result = _child.Tick(ctx);
        if (result == BtStatus.Success) _timer = _cooldownTime;
        return result;
    }
}

public class Repeater : BtNode
{
    private readonly BtNode _child;
    private readonly int _maxRepeats; // -1 = infinite
    private int _count;

    public Repeater(BtNode child, int maxRepeats = -1)
    { _child = child; _maxRepeats = maxRepeats; }

    public override BtStatus Tick(BtContext ctx)
    {
        var result = _child.Tick(ctx);
        if (result == BtStatus.Running) return BtStatus.Running;
        _count++;
        if (_maxRepeats > 0 && _count >= _maxRepeats)
        { _count = 0; return BtStatus.Success; }
        _child.Reset();
        return BtStatus.Running; // keep repeating
    }
}

public class TimeLimit : BtNode
{
    private readonly BtNode _child;
    private readonly float _maxSeconds;
    private float _elapsed;

    public TimeLimit(BtNode child, float seconds)
    { _child = child; _maxSeconds = seconds; }

    public override BtStatus Tick(BtContext ctx)
    {
        _elapsed += ctx.DeltaTime;
        if (_elapsed >= _maxSeconds) { _elapsed = 0; _child.Reset(); return BtStatus.Failure; }
        return _child.Tick(ctx);
    }

    public override void Reset() { _elapsed = 0; _child.Reset(); }
}
```

### Parallel Node

```csharp
public enum ParallelPolicy { RequireAll, RequireOne }

public class Parallel : BtNode
{
    private readonly List<BtNode> _children = new();
    private readonly ParallelPolicy _successPolicy;
    private readonly ParallelPolicy _failPolicy;

    public Parallel(ParallelPolicy success, ParallelPolicy fail, params BtNode[] children)
    {
        _successPolicy = success;
        _failPolicy = fail;
        _children.AddRange(children);
    }

    public override BtStatus Tick(BtContext ctx)
    {
        int successes = 0, failures = 0;

        foreach (var child in _children)
        {
            var status = child.Tick(ctx);
            if (status == BtStatus.Success) successes++;
            else if (status == BtStatus.Failure) failures++;
        }

        if (_failPolicy == ParallelPolicy.RequireOne && failures > 0) return BtStatus.Failure;
        if (_failPolicy == ParallelPolicy.RequireAll && failures == _children.Count) return BtStatus.Failure;
        if (_successPolicy == ParallelPolicy.RequireOne && successes > 0) return BtStatus.Success;
        if (_successPolicy == ParallelPolicy.RequireAll && successes == _children.Count) return BtStatus.Success;

        return BtStatus.Running;
    }
}
```

### Blackboard Pattern

Shared data store for the behavior tree — avoids coupling between nodes:

```csharp
public class Blackboard
{
    private readonly Dictionary<string, object> _data = new();

    public T Get<T>(string key) => _data.TryGetValue(key, out var v) ? (T)v : default!;
    public T Get<T>(string key, T fallback) => _data.TryGetValue(key, out var v) ? (T)v : fallback;
    public void Set<T>(string key, T value) => _data[key] = value!;
    public bool Has(string key) => _data.ContainsKey(key);
    public void Remove(string key) => _data.Remove(key);
    public void Clear() => _data.Clear();
}

public class BtContext
{
    public Entity Entity { get; init; }
    public World World { get; init; }
    public Blackboard Blackboard { get; } = new();
    public float DeltaTime { get; set; }
    public float GameTime { get; set; }
}
```

### Builder API

Fluent API for constructing behavior trees without manual nesting:

```csharp
public class BtBuilder
{
    private readonly Stack<(string Type, List<BtNode> Children)> _stack = new();

    public BtBuilder Sequence() { _stack.Push(("Sequence", new())); return this; }
    public BtBuilder Selector() { _stack.Push(("Selector", new())); return this; }

    public BtBuilder Condition(Func<BtContext, bool> check)
    { _stack.Peek().Children.Add(new Condition(check)); return this; }

    public BtBuilder Do(Func<BtContext, BtStatus> action)
    { _stack.Peek().Children.Add(new ActionNode(action)); return this; }

    public BtBuilder Invert(BtNode node)
    { _stack.Peek().Children.Add(new Inverter(node)); return this; }

    public BtBuilder WithCooldown(float seconds, BtNode node)
    { _stack.Peek().Children.Add(new Cooldown(node, seconds)); return this; }

    public BtBuilder End()
    {
        var (type, children) = _stack.Pop();
        BtNode composite = type switch
        {
            "Sequence" => new Sequence(children.ToArray()),
            "Selector" => new Selector(children.ToArray()),
            _ => throw new InvalidOperationException($"Unknown composite: {type}")
        };

        if (_stack.Count > 0)
            _stack.Peek().Children.Add(composite);
        else
            _stack.Push(("Root", new() { composite }));

        return this;
    }

    public BtNode Build() => _stack.Pop().Children[0];
}

// Usage — Skeleton enemy BT:
var skeletonBt = new BtBuilder()
    .Selector()
        .Sequence()                                                // Priority 1: Attack
            .Condition(ctx => ctx.Blackboard.Get<float>("enemyDist") < 50f)
            .Do(ctx => AttackEnemy(ctx))
        .End()
        .Sequence()                                                // Priority 2: Flee
            .Condition(ctx => ctx.Blackboard.Get<float>("healthPct") < 0.3f)
            .Do(ctx => FleeToSafety(ctx))
        .End()
        .Do(ctx => Patrol(ctx))                                    // Priority 3: Patrol
    .End()
    .Build();
```

---

## 3. GOAP (Goal-Oriented Action Planning)

Agents declare goals and available actions; a planner finds the cheapest action sequence. Ideal for emergent AI where hand-authoring every behavior path is impractical.

### When to Use GOAP vs BT

| Factor | GOAP | Behavior Tree |
|--------|------|---------------|
| Number of actions | 10+ | <10 |
| Action combinatorics | High (many valid orderings) | Low (predictable paths) |
| Emergent behavior | Yes (planner discovers sequences) | No (author defines paths) |
| Debugging | Harder (inspect plans) | Easier (visualize tree) |
| Performance cost | Higher (planning search) | Lower (tree traversal) |
| Best for | Guards, NPCs, strategy AI | Platformer enemies, boss AI |

### Data Model

```csharp
public class WorldState : Dictionary<string, bool>
{
    public WorldState() { }
    public WorldState(WorldState other) : base(other) { }

    public bool Satisfies(WorldState desired) =>
        desired.All(kv => TryGetValue(kv.Key, out var v) && v == kv.Value);

    public int UnsatisfiedCount(WorldState desired) =>
        desired.Count(kv => !TryGetValue(kv.Key, out var v) || v != kv.Value);
}

public class GoapAction
{
    public string Name { get; init; } = "";
    public float Cost { get; init; } = 1f;
    public WorldState Preconditions { get; init; } = new();
    public WorldState Effects { get; init; } = new();
    public Func<bool>? IsValid { get; init; }        // runtime validity check
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
        WorldState current, GoapGoal goal, List<GoapAction> available,
        int maxIterations = 1000)
    {
        var open = new PriorityQueue<PlanNode, float>();
        var visited = new HashSet<string>(); // state fingerprints
        open.Enqueue(new(current, new(), 0), 0);
        int iterations = 0;

        while (open.Count > 0 && iterations++ < maxIterations)
        {
            var node = open.Dequeue();
            if (node.State.Satisfies(goal.DesiredState))
                return node.Actions;

            var fingerprint = StateFingerprint(node.State);
            if (!visited.Add(fingerprint)) continue;

            foreach (var action in available)
            {
                if (action.IsValid?.Invoke() == false) continue;
                if (!node.State.Satisfies(action.Preconditions)) continue;

                var newState = ApplyEffects(node.State, action.Effects);
                var newCost = node.Cost + action.Cost;
                var newActions = new List<GoapAction>(node.Actions) { action };
                var heuristic = newState.UnsatisfiedCount(goal.DesiredState);
                open.Enqueue(new(newState, newActions, newCost), newCost + heuristic);
            }
        }
        return null; // no plan found within iteration budget
    }

    private static WorldState ApplyEffects(WorldState s, WorldState effects)
    {
        var next = new WorldState(s);
        foreach (var kv in effects) next[kv.Key] = kv.Value;
        return next;
    }

    private static string StateFingerprint(WorldState s) =>
        string.Join(",", s.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value}"));

    private record PlanNode(WorldState State, List<GoapAction> Actions, float Cost);
}
```

### Guard AI Example

```csharp
var actions = new List<GoapAction>
{
    new() { Name = "Patrol",     Cost = 1,
            Effects = new() { { "atPatrolPoint", true } },
            Preconditions = new() { { "isArmed", true } } },
    new() { Name = "GetWeapon",  Cost = 2,
            Effects = new() { { "isArmed", true } },
            Preconditions = new() { { "atArmory", true } } },
    new() { Name = "GoToArmory", Cost = 3,
            Effects = new() { { "atArmory", true } } },
    new() { Name = "AttackIntruder", Cost = 1,
            Effects = new() { { "intruderDown", true } },
            Preconditions = new() { { "isArmed", true }, { "canSeeIntruder", true } } },
    new() { Name = "SearchArea", Cost = 2,
            Effects = new() { { "canSeeIntruder", true } },
            Preconditions = new() { { "heardNoise", true } } },
};

var goal = new GoapGoal { Name = "EliminateIntruder",
    DesiredState = new() { { "intruderDown", true } }, Priority = 10 };

var plan = GoapPlanner.Plan(currentWorldState, goal, actions);
// If guard heard noise but has no weapon:
// GoToArmory → GetWeapon → SearchArea → AttackIntruder
```

### GOAP with Plan Caching

Replanning every frame is expensive. Cache the plan and only replan when world state changes:

```csharp
public class GoapAgent
{
    private List<GoapAction>? _plan;
    private int _planStep;
    private WorldState _lastPlanState;
    private readonly List<GoapAction> _availableActions;
    private readonly List<GoapGoal> _goals;

    public void Update(WorldState currentState)
    {
        // Replan if: no plan, plan complete, or world state changed significantly
        if (_plan == null || _planStep >= _plan.Count ||
            !currentState.Satisfies(_lastPlanState))
        {
            var bestGoal = _goals.OrderByDescending(g => g.Priority).First();
            _plan = GoapPlanner.Plan(currentState, bestGoal, _availableActions);
            _planStep = 0;
            _lastPlanState = new WorldState(currentState);
        }

        if (_plan == null) return; // no valid plan — fallback behavior

        var action = _plan[_planStep];
        var result = action.Execute?.Invoke() ?? BtStatus.Success;
        if (result == BtStatus.Success) _planStep++;
        else if (result == BtStatus.Failure) _plan = null; // force replan
    }
}
```

---

## 4. Utility AI

Score every possible action with response curves; pick the highest. Handles nuance that BTs and FSMs struggle with.

### Response Curves

```csharp
public static class ResponseCurve
{
    // Each curve maps an input [0..1] to an output [0..1]

    public static float Linear(float x, float m = 1f, float b = 0f)
        => Math.Clamp(m * x + b, 0f, 1f);

    public static float Quadratic(float x, float exp = 2f)
        => Math.Clamp(MathF.Pow(x, exp), 0f, 1f);

    // Inverse quadratic — rises fast then plateaus
    public static float InverseQuadratic(float x, float exp = 2f)
        => 1f - Math.Clamp(MathF.Pow(1f - x, exp), 0f, 1f);

    public static float Logistic(float x, float steepness = 10f, float midpoint = 0.5f)
        => 1f / (1f + MathF.Exp(-steepness * (x - midpoint)));

    public static float Step(float x, float threshold = 0.5f)
        => x >= threshold ? 1f : 0f;

    // Custom curve via animation curve (pre-baked)
    public static float FromKeyframes(float x, (float Time, float Value)[] keyframes)
    {
        if (keyframes.Length == 0) return 0f;
        if (x <= keyframes[0].Time) return keyframes[0].Value;
        if (x >= keyframes[^1].Time) return keyframes[^1].Value;

        for (int i = 0; i < keyframes.Length - 1; i++)
        {
            var (t0, v0) = keyframes[i];
            var (t1, v1) = keyframes[i + 1];
            if (x >= t0 && x <= t1)
            {
                float t = (x - t0) / (t1 - t0);
                return v0 + (v1 - v0) * t; // linear interp between keyframes
            }
        }
        return keyframes[^1].Value;
    }
}
```

```
Response Curve Shapes:

Linear:           Quadratic:        Logistic:         InverseQuadratic:
1│    /            1│      /         1│    ───         1│  ───
 │   /              │     /           │   /             │ /
 │  /               │    /            │  │              │/
 │ /                │   /             │  │              │
 │/                 │  /              │ /               │
0└────── 1         0└────── 1        0└────── 1        0└────── 1
 Hunger→eat         Health→flee      "Panic threshold"  "Diminishing returns"
```

### Consideration Architecture

```csharp
public class Consideration
{
    public string Name { get; init; } = "";
    public Func<Entity, World, float> Input { get; init; } = (_, _) => 0f; // raw value
    public Func<float, float> Curve { get; init; } = x => x;               // response curve

    public float Evaluate(Entity e, World w) => Math.Clamp(Curve(Input(e, w)), 0f, 1f);
}

public class UtilityAction
{
    public string Name { get; init; } = "";
    public List<Consideration> Considerations { get; init; } = new();
    public Action<Entity, World> Execute { get; init; } = (_, _) => { };

    // Compensated multiplicative scoring (prevents one zero from killing everything)
    public float Score(Entity e, World w)
    {
        if (Considerations.Count == 0) return 0f;
        float score = 1f;
        int count = 0;
        foreach (var c in Considerations)
        {
            float v = c.Evaluate(e, w);
            if (v <= 0.001f) return 0f; // hard zero = veto
            score *= v;
            count++;
        }
        // Compensation factor: raise to 1/n power to normalize for # of considerations
        return MathF.Pow(score, 1f / count);
    }
}
```

### Scoring & Selection with Momentum

```csharp
public static class UtilitySelector
{
    // Simple: pick highest score
    public static UtilityAction? SelectBest(List<UtilityAction> actions, Entity e, World w)
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

    // With momentum: require new action to score significantly higher to switch
    // Prevents rapid flip-flopping between similarly-scored actions
    public static UtilityAction? SelectWithMomentum(
        List<UtilityAction> actions, Entity e, World w,
        UtilityAction? currentAction, float momentum = 1.2f)
    {
        UtilityAction? best = null;
        float bestScore = 0f;

        foreach (var a in actions)
        {
            var s = a.Score(e, w);
            // Current action gets a bonus to prevent flickering
            if (a == currentAction) s *= momentum;
            if (s > bestScore) { bestScore = s; best = a; }
        }
        return best;
    }

    // Weighted random: proportional to score (adds unpredictability)
    public static UtilityAction? SelectWeightedRandom(
        List<UtilityAction> actions, Entity e, World w)
    {
        float total = 0f;
        Span<float> scores = stackalloc float[actions.Count];
        for (int i = 0; i < actions.Count; i++)
        {
            scores[i] = actions[i].Score(e, w);
            total += scores[i];
        }
        if (total <= 0) return null;

        float roll = Random.Shared.NextSingle() * total;
        for (int i = 0; i < actions.Count; i++)
        {
            roll -= scores[i];
            if (roll <= 0) return actions[i];
        }
        return actions[^1];
    }
}
```

### Practical Example: NPC Daily Routine

```csharp
var npcActions = new List<UtilityAction>
{
    new()
    {
        Name = "Eat",
        Considerations = new()
        {
            new() { Name = "Hunger", Input = (e, w) => GetHunger(e, w), // 0=full, 1=starving
                    Curve = x => ResponseCurve.Quadratic(x, 2f) },      // urgency rises sharply
        },
        Execute = (e, w) => GoToFood(e, w)
    },
    new()
    {
        Name = "Sleep",
        Considerations = new()
        {
            new() { Name = "Fatigue", Input = (e, w) => GetFatigue(e, w),
                    Curve = x => ResponseCurve.Logistic(x, 8f, 0.7f) }, // triggers near exhaustion
            new() { Name = "Nighttime", Input = (_, _) => IsNight() ? 1f : 0.2f,
                    Curve = x => x },
        },
        Execute = (e, w) => GoToBed(e, w)
    },
    new()
    {
        Name = "Work",
        Considerations = new()
        {
            new() { Name = "Energy", Input = (e, w) => 1f - GetFatigue(e, w),
                    Curve = x => ResponseCurve.Linear(x) },
            new() { Name = "Daytime", Input = (_, _) => IsDaytime() ? 1f : 0.1f,
                    Curve = x => x },
        },
        Execute = (e, w) => GoToWork(e, w)
    },
    new()
    {
        Name = "Socialize",
        Considerations = new()
        {
            new() { Name = "Loneliness", Input = (e, w) => GetLoneliness(e, w),
                    Curve = x => ResponseCurve.InverseQuadratic(x) }, // diminishing returns
            new() { Name = "NearFriend", Input = (e, w) => FriendNearby(e, w) ? 1f : 0.3f,
                    Curve = x => x },
        },
        Execute = (e, w) => StartConversation(e, w)
    },
};
```

---

## 5. Steering Behaviors

Continuous movement using force accumulation. Combine atomic behaviors for rich emergent motion.

### Core Behaviors

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

    public static Vector2 Pursue(SteeringAgent a, SteeringAgent target)
    {
        // Predict where the target will be based on its velocity
        float lookAhead = Vector2.Distance(a.Position, target.Position) / a.MaxSpeed;
        var predicted = target.Position + target.Velocity * lookAhead;
        return Seek(a, predicted);
    }

    public static Vector2 Evade(SteeringAgent a, SteeringAgent threat)
    {
        float lookAhead = Vector2.Distance(a.Position, threat.Position) / a.MaxSpeed;
        var predicted = threat.Position + threat.Velocity * lookAhead;
        return Flee(a, predicted);
    }

    public static Vector2 Wander(SteeringAgent a, ref float wanderAngle, float radius = 30f,
        float dist = 60f, float jitter = 0.3f)
    {
        wanderAngle += (Random.Shared.NextSingle() - 0.5f) * jitter;
        var circleCenter = a.Velocity.LengthSquared() > 0.01f
            ? Vector2.Normalize(a.Velocity) * dist
            : new Vector2(dist, 0); // fallback when stationary
        var offset = new Vector2(MathF.Cos(wanderAngle), MathF.Sin(wanderAngle)) * radius;
        return Truncate(circleCenter + offset, a.MaxForce);
    }

    // Obstacle avoidance — cast ahead and push away from walls
    public static Vector2 ObstacleAvoidance(SteeringAgent a, Func<Vector2, bool> isBlocked,
        float lookAhead = 40f, float feelerSpread = 0.4f)
    {
        if (a.Velocity.LengthSquared() < 0.01f) return Vector2.Zero;
        var forward = Vector2.Normalize(a.Velocity);

        // Three feelers: center, left, right
        Vector2[] feelers = {
            a.Position + forward * lookAhead,
            a.Position + RotateVector(forward, feelerSpread) * lookAhead * 0.7f,
            a.Position + RotateVector(forward, -feelerSpread) * lookAhead * 0.7f,
        };

        var avoidForce = Vector2.Zero;
        foreach (var tip in feelers)
        {
            if (isBlocked(tip))
            {
                avoidForce += a.Position - tip; // push away from obstacle
            }
        }
        return Truncate(avoidForce, a.MaxForce);
    }

    private static Vector2 RotateVector(Vector2 v, float angle)
    {
        float cos = MathF.Cos(angle), sin = MathF.Sin(angle);
        return new Vector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
    }

    private static Vector2 Truncate(Vector2 v, float max) =>
        v.LengthSquared() > max * max ? Vector2.Normalize(v) * max : v;
}
```

### Flocking (Boids)

```csharp
public static class Flocking
{
    public static Vector2 Separation(SteeringAgent a, Span<Vector2> neighbors, float desiredDist = 25f)
    {
        var force = Vector2.Zero;
        foreach (var n in neighbors)
        {
            var diff = a.Position - n;
            float d = diff.Length();
            if (d > 0 && d < desiredDist)
                force += Vector2.Normalize(diff) / d; // stronger push when closer
        }
        return Steering.Truncate(force, a.MaxForce);
    }

    public static Vector2 Alignment(SteeringAgent a, Span<Vector2> neighborVelocities)
    {
        if (neighborVelocities.Length == 0) return Vector2.Zero;
        var avg = Vector2.Zero;
        foreach (var v in neighborVelocities) avg += v;
        avg /= neighborVelocities.Length;
        return Steering.Truncate(avg - a.Velocity, a.MaxForce);
    }

    public static Vector2 Cohesion(SteeringAgent a, Span<Vector2> neighbors)
    {
        if (neighbors.Length == 0) return Vector2.Zero;
        var center = Vector2.Zero;
        foreach (var n in neighbors) center += n;
        center /= neighbors.Length;
        return Steering.Seek(a, center);
    }

    // Combined flock force with tunable weights
    public static Vector2 Flock(SteeringAgent a, Span<Vector2> neighborPos,
        Span<Vector2> neighborVel,
        float wSep = 1.5f, float wAlign = 1.0f, float wCohesion = 1.0f)
    {
        var sep = Separation(a, neighborPos) * wSep;
        var align = Alignment(a, neighborVel) * wAlign;
        var coh = Cohesion(a, neighborPos) * wCohesion;
        return Steering.Truncate(sep + align + coh, a.MaxForce);
    }
}
```

### Steering Combination (Priority-Based)

When multiple steering behaviors are active, combine them by priority:

```csharp
public class SteeringPipeline
{
    private readonly List<(string Name, Func<Vector2> Behavior, float Weight)> _behaviors = new();
    private float _maxForce;

    public SteeringPipeline(float maxForce) => _maxForce = maxForce;

    // Higher priority behaviors are added first
    public void Add(string name, Func<Vector2> behavior, float weight = 1f) =>
        _behaviors.Add((name, behavior, weight));

    // Priority-based: earlier behaviors consume force budget first
    public Vector2 Calculate()
    {
        var totalForce = Vector2.Zero;
        float remaining = _maxForce;

        foreach (var (_, behavior, weight) in _behaviors)
        {
            if (remaining <= 0) break;
            var force = behavior() * weight;
            float magnitude = force.Length();
            if (magnitude > remaining)
                force = Vector2.Normalize(force) * remaining;
            totalForce += force;
            remaining -= force.Length();
        }
        return totalForce;
    }
}

// Usage:
var pipeline = new SteeringPipeline(agent.MaxForce);
pipeline.Add("Avoid", () => Steering.ObstacleAvoidance(agent, isBlocked));   // highest priority
pipeline.Add("Separate", () => Flocking.Separation(agent, neighbors));
pipeline.Add("Seek", () => Steering.Seek(agent, target));                    // lowest priority
var finalForce = pipeline.Calculate();
```

### SpatialHash for Neighbor Queries

```csharp
public class SpatialHash<T>
{
    private readonly float _cellSize;
    private readonly Dictionary<long, List<(Vector2 Pos, T Item)>> _cells = new();

    public SpatialHash(float cellSize) => _cellSize = cellSize;

    public void Clear()
    {
        foreach (var list in _cells.Values) list.Clear(); // reuse lists, avoid GC
    }

    public void Insert(Vector2 pos, T item)
    {
        var key = CellKey(pos);
        if (!_cells.TryGetValue(key, out var list))
            _cells[key] = list = new(8); // pre-size for typical density
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

    // K-nearest neighbors (sorted by distance)
    public void QueryNearest(Vector2 center, int k, List<(T Item, float Dist)> results)
    {
        // Start with cell size as radius, expand if needed
        float radius = _cellSize;
        var candidates = new List<T>();

        while (candidates.Count < k && radius < _cellSize * 10)
        {
            candidates.Clear();
            QueryRadius(center, radius, candidates);
            radius *= 2f;
        }

        results.Clear();
        foreach (var item in candidates)
        {
            // Need position for distance — alternative: store (pos, item) in results
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

// Extended: check vision with line-of-sight (walls block sight)
public static bool CanSee(Vector2 origin, Vector2 facing, Vision vision,
    Vector2 target, bool[,] blocked, int tileSize)
{
    if (!InVisionCone(origin, facing, vision, target)) return false;
    return HasLineOfSight(origin, target, blocked, tileSize);
}
```

### Hearing (Sound Propagation)

```csharp
public record struct SoundEvent(Vector2 Origin, float Radius, float Intensity, SoundType Type);
public enum SoundType { Footstep, Gunshot, Explosion, Speech, Breaking }
public record struct Hearing(float Sensitivity); // multiplier on radius

public static bool CanHear(Vector2 listenerPos, Hearing hearing, SoundEvent sound)
{
    float effectiveRadius = sound.Radius * hearing.Sensitivity;
    return Vector2.DistanceSquared(listenerPos, sound.Origin) <=
           effectiveRadius * effectiveRadius;
}

// Propagation through the world — sounds get quieter through walls
public static float SoundIntensityAt(Vector2 listenerPos, SoundEvent sound,
    bool[,] blocked, int tileSize)
{
    float dist = Vector2.Distance(listenerPos, sound.Origin);
    if (dist > sound.Radius) return 0f;

    float falloff = 1f - (dist / sound.Radius);

    // Count walls between source and listener (crude occlusion)
    int wallCount = CountWallsBetween(sound.Origin, listenerPos, blocked, tileSize);
    float occlusion = MathF.Pow(0.5f, wallCount); // each wall halves intensity

    return sound.Intensity * falloff * occlusion;
}
```

### Line-of-Sight Raycasting (Tile Map)

```csharp
public static bool HasLineOfSight(Vector2 from, Vector2 to, bool[,] blocked, int tileSize)
{
    // DDA ray march through the tile grid
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

public static int CountWallsBetween(Vector2 from, Vector2 to, bool[,] blocked, int tileSize)
{
    var dir = to - from;
    float dist = dir.Length();
    if (dist < 1f) return 0;
    dir /= dist;

    int walls = 0;
    float step = tileSize * 0.5f;
    for (float t = 0; t < dist; t += step)
    {
        var p = from + dir * t;
        int tx = (int)(p.X / tileSize);
        int ty = (int)(p.Y / tileSize);
        if (tx >= 0 && ty >= 0 && tx < blocked.GetLength(0) && ty < blocked.GetLength(1))
            if (blocked[tx, ty]) walls++;
    }
    return walls;
}
```

### Threat Memory System

AI agents should remember where they last saw threats, even after losing sight:

```csharp
public record struct ThreatMemory(Entity Target, Vector2 LastKnownPosition,
    float TimeSinceVisible, float ThreatLevel);

public class ThreatTracker
{
    private readonly Dictionary<Entity, ThreatMemory> _threats = new();
    private readonly float _forgetTime;

    public ThreatTracker(float forgetTime = 10f) => _forgetTime = forgetTime;

    public IEnumerable<ThreatMemory> KnownThreats => _threats.Values;
    public ThreatMemory? HighestThreat => _threats.Values
        .OrderByDescending(t => t.ThreatLevel)
        .Cast<ThreatMemory?>()
        .FirstOrDefault();

    public void UpdateVisible(Entity target, Vector2 position, float threatLevel)
    {
        _threats[target] = new(target, position, 0f, threatLevel);
    }

    public void Tick(float dt)
    {
        var toRemove = new List<Entity>();
        var keys = _threats.Keys.ToList();
        foreach (var key in keys)
        {
            var mem = _threats[key];
            mem = mem with { TimeSinceVisible = mem.TimeSinceVisible + dt,
                            ThreatLevel = mem.ThreatLevel * 0.99f }; // decay
            if (mem.TimeSinceVisible > _forgetTime) toRemove.Add(key);
            else _threats[key] = mem;
        }
        foreach (var key in toRemove) _threats.Remove(key);
    }

    public bool HasRecentThreat(float withinSeconds) =>
        _threats.Values.Any(t => t.TimeSinceVisible < withinSeconds);
}
```

### Perception ECS System

```csharp
public record struct PerceivedEntities(List<Entity> Visible, List<Entity> Heard);

public partial class PerceptionSystem : BaseSystem<World, float>
{
    private readonly SpatialHash<Entity> _spatialHash;
    private readonly List<Entity> _nearby = new();

    public PerceptionSystem(World world) : base(world)
    {
        _spatialHash = new SpatialHash<Entity>(64f);
    }

    public override void BeforeUpdate(in float dt)
    {
        // Rebuild spatial hash each frame
        _spatialHash.Clear();
        var allQuery = new QueryDescription().WithAll<Position>();
        World.Query(allQuery, (Entity e, ref Position pos) =>
            _spatialHash.Insert(pos.Value, e));
    }

    [Query]
    [All<Vision, Position, Facing, PerceivedEntities>]
    public void UpdatePerception(Entity self, ref Vision vision, ref Position pos,
        ref Facing facing, ref PerceivedEntities perceived)
    {
        perceived.Visible.Clear();
        perceived.Heard.Clear();

        _nearby.Clear();
        _spatialHash.QueryRadius(pos.Value, vision.Range, _nearby);

        foreach (var other in _nearby)
        {
            if (other == self) continue;
            ref var otherPos = ref World.Get<Position>(other);
            if (InVisionCone(pos.Value, facing.Direction, vision, otherPos.Value))
                perceived.Visible.Add(other);
        }
    }
}
```

---

## 7. Pathfinding

> **Deep dive:** See [G40 Pathfinding](./G40_pathfinding.md) for the full pathfinding guide including nav meshes, hierarchical pathfinding, and path smoothing.

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
            float tentG = gScore[current] +
                (next.X != current.X && next.Y != current.Y ? 1.414f : 1f); // diagonal cost
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

    static List<Point> ReconstructPath(Dictionary<Point, Point> cameFrom, Point current)
    {
        var path = new List<Point> { current };
        while (cameFrom.ContainsKey(current))
        {
            current = cameFrom[current];
            path.Add(current);
        }
        path.Reverse();
        return path;
    }

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

### Path Smoothing

Raw A* paths are jagged. Smooth them with line-of-sight checks:

```csharp
public static List<Point> SmoothPath(List<Point> path, bool[,] walkable, int tileSize)
{
    if (path.Count <= 2) return path;

    var smoothed = new List<Point> { path[0] };
    int current = 0;

    while (current < path.Count - 1)
    {
        // Find the furthest visible point from current
        int furthest = current + 1;
        for (int i = path.Count - 1; i > current + 1; i--)
        {
            var from = new Vector2(path[current].X * tileSize + tileSize / 2f,
                                   path[current].Y * tileSize + tileSize / 2f);
            var to = new Vector2(path[i].X * tileSize + tileSize / 2f,
                                 path[i].Y * tileSize + tileSize / 2f);
            if (HasLineOfSight(from, to, walkable, tileSize))
            {
                furthest = i;
                break;
            }
        }
        smoothed.Add(path[furthest]);
        current = furthest;
    }
    return smoothed;
}
```

### Flow Fields (RTS)

All units heading to the same target share one precomputed field — O(grid) cost vs O(unit × grid) for individual A*.

```csharp
public class FlowField
{
    public Vector2[,] Flow { get; }
    private readonly float[,] _cost;
    public int Width { get; }
    public int Height { get; }

    public FlowField(bool[,] walkable, Point goal)
    {
        Width = walkable.GetLength(0);
        Height = walkable.GetLength(1);
        _cost = new float[Width, Height];
        Flow = new Vector2[Width, Height];

        // Dijkstra from goal
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
            _cost[x, y] = float.MaxValue;
        _cost[goal.X, goal.Y] = 0;

        var queue = new Queue<Point>();
        queue.Enqueue(goal);

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            foreach (var n in GridNeighbors(cur, Width, Height))
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
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
        {
            float best = _cost[x, y];
            var dir = Vector2.Zero;
            foreach (var n in GridNeighbors(new(x, y), Width, Height))
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

    public Vector2 GetDirection(Vector2 worldPos, int tileSize)
    {
        int tx = (int)(worldPos.X / tileSize);
        int ty = (int)(worldPos.Y / tileSize);
        if (tx < 0 || ty < 0 || tx >= Width || ty >= Height) return Vector2.Zero;
        return Flow[tx, ty];
    }

    private static IEnumerable<Point> GridNeighbors(Point p, int w, int h)
    {
        if (p.X > 0) yield return new(p.X - 1, p.Y);
        if (p.X < w - 1) yield return new(p.X + 1, p.Y);
        if (p.Y > 0) yield return new(p.X, p.Y - 1);
        if (p.Y < h - 1) yield return new(p.X, p.Y + 1);
    }
}
```

### Jump Point Search

Optimization of A* on uniform-cost grids — prunes symmetric paths by "jumping" along straight lines. Use the same A* skeleton but replace neighbor expansion with JPS jump logic. Gains **5-10x** speedup on open maps. Libraries: `RoyT.AStar` or implement per the original Harabor & Grastien paper.

### Hierarchical Pathfinding (HPA*)

For large maps, divide the grid into clusters, precompute inter-cluster edges, then pathfind on the abstract graph first and refine within clusters. Reduces search space dramatically for open-world or RTS maps. See [G40 Pathfinding §HPA*](./G40_pathfinding.md) for full implementation.

---

## 8. Influence Maps

Spatial scoring grids for strategic AI decisions — where is dangerous, where is safe, where are resources.

```
Influence Map Visualization:

  ┌───┬───┬───┬───┬───┬───┬───┬───┐
  │ 0 │ 0 │ 0 │ 0 │ 0 │ 0 │ 0 │ 0 │
  ├───┼───┼───┼───┼───┼───┼───┼───┤
  │ 0 │ 2 │ 4 │ 2 │ 0 │ 0 │ 0 │ 0 │   Enemy stamped at (2,2)
  ├───┼───┼───┼───┼───┼───┼───┼───┤   Influence radiates outward
  │ 0 │ 4 │ 8 │ 4 │ 0 │ 0 │ 0 │ 0 │   Higher = more dangerous
  ├───┼───┼───┼───┼───┼───┼───┼───┤
  │ 0 │ 2 │ 4 │ 2 │ 0 │ 0 │-3 │ 0 │   Negative = allied/safe
  ├───┼───┼───┼───┼───┼───┼───┼───┤   (medkit at (6,3))
  │ 0 │ 0 │ 0 │ 0 │ 0 │ 0 │ 0 │ 0 │
  └───┴───┴───┴───┴───┴───┴───┴───┘

  Flee AI: move toward lowest value
  Engage AI: move toward highest value
  Tactical AI: find position with high enemy value + cover
```

### Implementation

```csharp
public class InfluenceMap
{
    private readonly float[,] _map;
    private readonly float[,] _buffer;
    public int Width { get; }
    public int Height { get; }

    public InfluenceMap(int w, int h)
    {
        Width = w; Height = h;
        _map = new float[w, h];
        _buffer = new float[w, h];
    }

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
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && ny >= 0 && nx < Width && ny < Height)
                    sum += _map[nx, ny] * diffusion / 8f;
            }
            _buffer[x, y] = sum * decay;
        }
        Array.Copy(_buffer, _map, _map.Length);
    }

    // Find the cell with the highest/lowest influence in a radius
    public Point FindExtreme(int cx, int cy, int searchRadius, bool findMax = true)
    {
        float bestValue = findMax ? float.MinValue : float.MaxValue;
        var bestPoint = new Point(cx, cy);

        for (int dx = -searchRadius; dx <= searchRadius; dx++)
        for (int dy = -searchRadius; dy <= searchRadius; dy++)
        {
            int nx = cx + dx, ny = cy + dy;
            if (nx < 0 || ny < 0 || nx >= Width || ny >= Height) continue;
            float val = _map[nx, ny];
            if ((findMax && val > bestValue) || (!findMax && val < bestValue))
            {
                bestValue = val;
                bestPoint = new Point(nx, ny);
            }
        }
        return bestPoint;
    }

    public void Clear() => Array.Clear(_map);
}
```

### Multi-Layer Influence Maps

Use separate maps for different concerns, then combine them for decisions:

```csharp
public class TacticalMap
{
    public InfluenceMap EnemyThreat { get; }
    public InfluenceMap AllyPresence { get; }
    public InfluenceMap CoverValue { get; }
    public InfluenceMap ResourceValue { get; }

    public TacticalMap(int w, int h)
    {
        EnemyThreat = new(w, h);
        AllyPresence = new(w, h);
        CoverValue = new(w, h);
        ResourceValue = new(w, h);
    }

    // Composite query: "find the safest covered position near resources"
    public Point FindTacticalPosition(int cx, int cy, int radius)
    {
        float bestScore = float.MinValue;
        var bestPoint = new Point(cx, cy);

        for (int dx = -radius; dx <= radius; dx++)
        for (int dy = -radius; dy <= radius; dy++)
        {
            int nx = cx + dx, ny = cy + dy;
            if (nx < 0 || ny < 0 || nx >= EnemyThreat.Width || ny >= EnemyThreat.Height) continue;

            float score =
                -EnemyThreat.GetInfluence(nx, ny) * 2f +   // avoid enemies (heavy weight)
                AllyPresence.GetInfluence(nx, ny) * 0.5f +  // prefer being near allies
                CoverValue.GetInfluence(nx, ny) * 1.5f +    // prefer cover
                ResourceValue.GetInfluence(nx, ny) * 0.3f;  // slight preference for resources

            if (score > bestScore) { bestScore = score; bestPoint = new(nx, ny); }
        }
        return bestPoint;
    }

    public void Update(float dt)
    {
        EnemyThreat.Propagate(decay: 0.85f);
        AllyPresence.Propagate(decay: 0.9f);
        // CoverValue and ResourceValue are static — don't propagate
    }
}
```

---

## 9. Squad & Group Tactics

### Squad Formation

```csharp
public enum FormationType { Line, Wedge, Circle, Column }

public static class FormationPatterns
{
    public static Vector2[] GetOffsets(FormationType type, int count, float spacing = 32f)
    {
        return type switch
        {
            FormationType.Line => LineFormation(count, spacing),
            FormationType.Wedge => WedgeFormation(count, spacing),
            FormationType.Circle => CircleFormation(count, spacing),
            FormationType.Column => ColumnFormation(count, spacing),
            _ => LineFormation(count, spacing)
        };
    }

    private static Vector2[] LineFormation(int count, float spacing)
    {
        var offsets = new Vector2[count];
        float start = -(count - 1) * spacing / 2f;
        for (int i = 0; i < count; i++)
            offsets[i] = new Vector2(start + i * spacing, 0);
        return offsets;
    }

    private static Vector2[] WedgeFormation(int count, float spacing)
    {
        var offsets = new Vector2[count];
        offsets[0] = Vector2.Zero; // leader at front
        for (int i = 1; i < count; i++)
        {
            int row = (i + 1) / 2;
            int side = (i % 2 == 0) ? 1 : -1;
            offsets[i] = new Vector2(-row * spacing, side * row * spacing * 0.7f);
        }
        return offsets;
    }

    private static Vector2[] CircleFormation(int count, float spacing)
    {
        var offsets = new Vector2[count];
        float radius = count * spacing / (2f * MathF.PI);
        for (int i = 0; i < count; i++)
        {
            float angle = 2f * MathF.PI * i / count;
            offsets[i] = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
        }
        return offsets;
    }

    private static Vector2[] ColumnFormation(int count, float spacing)
    {
        var offsets = new Vector2[count];
        for (int i = 0; i < count; i++)
            offsets[i] = new Vector2(-i * spacing, 0);
        return offsets;
    }
}
```

### Squad Coordinator

```csharp
public class SquadCoordinator
{
    public Entity Leader { get; private set; }
    public List<Entity> Members { get; } = new();
    public FormationType Formation { get; set; } = FormationType.Wedge;
    public SquadOrder CurrentOrder { get; private set; } = SquadOrder.Idle;

    public enum SquadOrder { Idle, MoveTo, Attack, Defend, Retreat, Regroup }

    private Vector2 _targetPosition;
    private Entity _targetEnemy;

    public void SetOrder(SquadOrder order, Vector2? position = null, Entity? enemy = null)
    {
        CurrentOrder = order;
        if (position.HasValue) _targetPosition = position.Value;
        if (enemy.HasValue) _targetEnemy = enemy.Value;
    }

    public void Update(World world, float dt)
    {
        if (Members.Count == 0) return;
        if (!world.IsAlive(Leader)) PromoteNewLeader(world);

        ref var leaderPos = ref world.Get<Position>(Leader);
        var offsets = FormationPatterns.GetOffsets(Formation, Members.Count);

        // Calculate formation facing
        var facing = CurrentOrder == SquadOrder.MoveTo
            ? Vector2.Normalize(_targetPosition - leaderPos.Value)
            : Vector2.UnitX;

        for (int i = 0; i < Members.Count; i++)
        {
            if (!world.IsAlive(Members[i])) continue;

            // Rotate offset by squad facing
            var rotated = RotateOffset(offsets[i], facing);
            var formationTarget = leaderPos.Value + rotated;

            // Set each member's navigation target
            ref var nav = ref world.Get<NavigationTarget>(Members[i]);
            nav.Target = formationTarget;
        }
    }

    private void PromoteNewLeader(World world)
    {
        Members.Remove(Leader);
        Leader = Members.FirstOrDefault();
    }

    private static Vector2 RotateOffset(Vector2 offset, Vector2 facing)
    {
        float angle = MathF.Atan2(facing.Y, facing.X);
        float cos = MathF.Cos(angle), sin = MathF.Sin(angle);
        return new Vector2(
            offset.X * cos - offset.Y * sin,
            offset.X * sin + offset.Y * cos);
    }
}
```

### Tactical Role Assignment

```csharp
public enum TacticalRole { Pointman, Flanker, Support, Sniper, Healer }

public static class TacticalAssignment
{
    // Assign roles based on unit capabilities
    public static Dictionary<Entity, TacticalRole> AssignRoles(
        List<Entity> members, World world)
    {
        var assignments = new Dictionary<Entity, TacticalRole>();

        // Score each unit for each role based on their stats
        foreach (var member in members)
        {
            ref var stats = ref world.Get<UnitStats>(member);
            var bestRole = TacticalRole.Pointman;
            float bestScore = 0f;

            foreach (var role in Enum.GetValues<TacticalRole>())
            {
                float score = ScoreForRole(stats, role);
                if (score > bestScore && !assignments.ContainsValue(role))
                {
                    bestScore = score;
                    bestRole = role;
                }
            }
            assignments[member] = bestRole;
        }
        return assignments;
    }

    private static float ScoreForRole(UnitStats stats, TacticalRole role) => role switch
    {
        TacticalRole.Pointman => stats.Health * 0.5f + stats.Armor * 0.5f,
        TacticalRole.Flanker  => stats.Speed * 0.4f + stats.Damage * 0.4f + stats.Stealth * 0.2f,
        TacticalRole.Support  => stats.Accuracy * 0.3f + stats.Range * 0.4f + stats.Ammo * 0.3f,
        TacticalRole.Sniper   => stats.Range * 0.5f + stats.Accuracy * 0.4f + stats.Stealth * 0.1f,
        TacticalRole.Healer   => stats.HealPower * 0.6f + stats.Health * 0.2f + stats.Speed * 0.2f,
        _ => 0f
    };
}
```

---

## 10. Dynamic Difficulty Adjustment (DDA)

### Player Performance Tracking

```csharp
public class PlayerPerformanceTracker
{
    private readonly Queue<float> _recentDeathTimes = new();
    private readonly Queue<bool> _recentHitResults = new();   // true = hit, false = miss
    private readonly Queue<float> _recentDamagesTaken = new();

    private readonly int _windowSize;
    private float _totalPlayTime;

    public PlayerPerformanceTracker(int windowSize = 20) => _windowSize = windowSize;

    // 0.0 = struggling badly, 1.0 = dominating
    public float SkillEstimate
    {
        get
        {
            float hitRate = _recentHitResults.Count > 0
                ? _recentHitResults.Count(r => r) / (float)_recentHitResults.Count
                : 0.5f;

            float survivalRate = _recentDeathTimes.Count > 0
                ? Math.Clamp(_recentDeathTimes.Average() / 120f, 0f, 1f) // 2min = good
                : 0.5f;

            float damageRate = _recentDamagesTaken.Count > 0
                ? 1f - Math.Clamp(_recentDamagesTaken.Average() / 50f, 0f, 1f) // lower = better
                : 0.5f;

            return hitRate * 0.4f + survivalRate * 0.35f + damageRate * 0.25f;
        }
    }

    public void RecordHit(bool didHit)
    {
        _recentHitResults.Enqueue(didHit);
        while (_recentHitResults.Count > _windowSize) _recentHitResults.Dequeue();
    }

    public void RecordDeath(float timeSurvived)
    {
        _recentDeathTimes.Enqueue(timeSurvived);
        while (_recentDeathTimes.Count > _windowSize / 2) _recentDeathTimes.Dequeue();
    }

    public void RecordDamage(float amount)
    {
        _recentDamagesTaken.Enqueue(amount);
        while (_recentDamagesTaken.Count > _windowSize) _recentDamagesTaken.Dequeue();
    }
}
```

### Difficulty Modifiers

```csharp
public class DynamicDifficulty
{
    private readonly PlayerPerformanceTracker _tracker;
    private float _difficultyLevel = 0.5f; // 0=easiest, 1=hardest
    private float _targetDifficulty = 0.5f;

    // Smooth transition speed — don't jerk difficulty around
    private const float AdjustSpeed = 0.02f;
    private const float DeadZone = 0.05f; // don't adjust within this range of target

    public DynamicDifficulty(PlayerPerformanceTracker tracker) => _tracker = tracker;

    public void Update(float dt)
    {
        float skill = _tracker.SkillEstimate;

        // Target difficulty tracks player skill with a slight lag
        _targetDifficulty = skill;

        // Smooth approach
        float diff = _targetDifficulty - _difficultyLevel;
        if (MathF.Abs(diff) > DeadZone)
            _difficultyLevel += MathF.Sign(diff) * AdjustSpeed * dt;

        _difficultyLevel = Math.Clamp(_difficultyLevel, 0f, 1f);
    }

    // --- Query modifiers for game systems ---

    // Enemy health: 60% to 140% of base
    public float EnemyHealthMod => 0.6f + _difficultyLevel * 0.8f;

    // Enemy damage: 70% to 130% of base
    public float EnemyDamageMod => 0.7f + _difficultyLevel * 0.6f;

    // Enemy accuracy: 40% to 90%
    public float EnemyAccuracy => 0.4f + _difficultyLevel * 0.5f;

    // Enemy reaction time: 1.5s to 0.3s
    public float EnemyReactionTime => 1.5f - _difficultyLevel * 1.2f;

    // Spawn count modifier: 70% to 150% of base
    public float SpawnCountMod => 0.7f + _difficultyLevel * 0.8f;

    // Health pickup frequency: more at low difficulty
    public float HealthPickupChance => 0.3f - _difficultyLevel * 0.2f;

    // Current level for debug display
    public float Level => _difficultyLevel;
}
```

### Integration with AI Systems

```csharp
[Query]
[All<AiState, Health, EnemyTag>]
public void ApplyDifficulty(Entity entity, ref Health hp, ref AiState ai)
{
    // Apply DDA to freshly spawned enemies
    if (ai.TimeInState < 0.1f && ai.Current == AiStateId.Idle)
    {
        hp.Max = (int)(hp.Max * _dda.EnemyHealthMod);
        hp.Current = hp.Max;

        if (World.Has<AttackDamage>(entity))
        {
            ref var dmg = ref World.Get<AttackDamage>(entity);
            dmg.BaseDamage = (int)(dmg.BaseDamage * _dda.EnemyDamageMod);
        }
    }
}
```

### DDA Tuning Table

| Skill Estimate | Enemy HP | Enemy Dmg | Reaction | Spawns | Pickups |
|:-:|:-:|:-:|:-:|:-:|:-:|
| 0.0 (struggling) | 60% | 70% | 1.5s | 70% | 30% |
| 0.25 | 80% | 85% | 1.0s | 85% | 25% |
| 0.5 (baseline) | 100% | 100% | 0.9s | 100% | 20% |
| 0.75 | 120% | 115% | 0.6s | 125% | 15% |
| 1.0 (dominating) | 140% | 130% | 0.3s | 150% | 10% |

> **Design note:** Never adjust core mechanics (controls, physics). Only adjust AI parameters. The player should always feel in control — they shouldn't realize difficulty is being adjusted.

---

## 11. Boss Pattern Design

### Phase-Based State Machine

```csharp
public record struct BossPhase(int PhaseIndex, float PhaseHealthThreshold);
public record struct AttackPattern(int CurrentAttack, float CooldownTimer);

public partial class BossAiSystem : BaseSystem<World, float>
{
    private static readonly BossAttack[][] PhaseAttacks = new[]
    {
        new[] { BossAttack.Slam, BossAttack.Charge, BossAttack.Slam },
        new[] { BossAttack.Fireball, BossAttack.Spin, BossAttack.Fireball, BossAttack.Charge },
        new[] { BossAttack.Enrage, BossAttack.Fireball, BossAttack.Slam, BossAttack.Spin },
    };

    [Query]
    public void UpdateBoss([Data] in float dt, ref BossPhase phase,
        ref AttackPattern pattern, ref Health hp, ref Position pos)
    {
        // Phase transitions based on HP thresholds
        float hpPct = (float)hp.Current / hp.Max;
        int targetPhase = hpPct switch
        {
            > 0.66f => 0,   // Phase 1: basic attacks
            > 0.33f => 1,   // Phase 2: adds ranged + combos
            _       => 2    // Phase 3: enrage + all attacks
        };

        if (targetPhase != phase.PhaseIndex)
        {
            phase.PhaseIndex = targetPhase;
            pattern.CurrentAttack = 0;
            // Trigger phase transition: invulnerability frames, dramatic animation, arena change
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

    private static float GetCooldown(int phase) => phase switch
    {
        0 => 2.0f,
        1 => 1.5f,
        2 => 0.8f,
        _ => 2.0f
    };
}
```

### Telegraph & Windup System

Give the player time to react — every attack should have a visible telegraph:

```csharp
public record struct AttackTelegraph(
    float WindupDuration,    // time before damage starts
    float ActiveDuration,    // time damage hitbox is active
    float RecoveryDuration,  // time after attack before next action
    float Timer,
    AttackPhase Phase
);

public enum AttackPhase { Idle, Windup, Active, Recovery }

[Query]
[All<AttackTelegraph, BossTag>]
public void UpdateTelegraph([Data] in float dt, ref AttackTelegraph telegraph)
{
    telegraph.Timer += dt;
    telegraph.Phase = telegraph.Timer switch
    {
        var t when t < telegraph.WindupDuration
            => AttackPhase.Windup,          // Show warning: glow, wind-up animation
        var t when t < telegraph.WindupDuration + telegraph.ActiveDuration
            => AttackPhase.Active,          // Hitbox active, deal damage
        var t when t < telegraph.WindupDuration + telegraph.ActiveDuration + telegraph.RecoveryDuration
            => AttackPhase.Recovery,        // Vulnerable window for the player
        _ => AttackPhase.Idle
    };
}
```

### Boss Design Tuning Table

| Phase | HP Range | Cooldown | Speed Mult | New Mechanics | Player Window |
|:-:|:-:|:-:|:-:|:-:|:-:|
| 1 | 100%-66% | 2.0s | 1.0× | Slam, Charge | Large recovery windows |
| 2 | 66%-33% | 1.5s | 1.3× | +Fireball, +Spin | Medium windows, dodge-rolls needed |
| 3 | 33%-0% | 0.8s | 1.6× | +Enrage (all attacks) | Tight windows, pattern memorization |
| Transition | — | — | 0× | I-frames, arena shift, heal minions | Catch breath, heal up |

---

## 12. ECS-Specific AI Patterns (Arch)

### Component Decomposition

Keep AI state in small, composable components — not monolithic "AIBrain" blobs:

```csharp
// State
public record struct AiState(AiStateId Current, AiStateId Previous, float TimeInState);
public record struct PatrolPath(Vector2[] Waypoints, int CurrentIndex);
public record struct AggroTarget(Entity Target, float LastSeenTime);
public record struct BehaviorTreeRef(BtNode Root);
public record struct NavigationTarget(Vector2 Target);

// Perception (tag components are zero-size)
public record struct Vision(float Range, float HalfAngleDeg);
public record struct Hearing(float Sensitivity);
public record struct PerceivedEntities(List<Entity> Visible, List<Entity> Heard);
public record struct Facing(Vector2 Direction);

// Steering
public record struct SteeringAgent(Vector2 Velocity, float MaxSpeed, float MaxForce);
public record struct SteeringForce(Vector2 Accumulated);

// Tags
public struct IsAggressive;    // zero-size tag — always chase on sight
public struct IsFleeing;       // zero-size tag — currently fleeing
public struct IsPatrolling;    // zero-size tag — on patrol route
public struct AiUpdateThisFrame; // zero-size tag — eligible for AI this frame
public struct EnemyTag;
public struct BossTag;
```

### System Pipeline

Order systems in a logical pipeline — perception → decision → action → physics:

```csharp
var aiSystems = new Group("AI",
    new AiTickSystem(world),         // throttles AI updates (staggering)
    new PerceptionSystem(world),     // populates PerceivedEntities
    new ThreatTrackingSystem(world), // updates ThreatTracker memories
    new BehaviorTreeSystem(world),   // ticks BTs, updates AiState
    new UtilityAiSystem(world),      // for entities using utility AI
    new SquadSystem(world),          // squad formation + coordination
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

// Aggressive enemies: separate query with tag filter
[Query]
[All<AiState, IsAggressive, PerceivedEntities>]
[None<Dead>]
public void ProcessAggressive(Entity entity, ref AiState ai, ref PerceivedEntities perceived)
{
    if (perceived.Visible.Count > 0)
        ai.Current = AiStateId.Chase; // always chase on sight
}
```

---

## 13. AI Debugging & Visualization

### Debug Overlay System

```csharp
public class AiDebugOverlay
{
    private readonly SpriteBatch _spriteBatch;
    private readonly SpriteFont _font;
    private bool _enabled;

    // Toggle sections
    public bool ShowStates { get; set; } = true;
    public bool ShowVisionCones { get; set; } = true;
    public bool ShowPaths { get; set; } = true;
    public bool ShowSteeringForces { get; set; } = true;
    public bool ShowInfluence { get; set; } = false;
    public bool ShowBtStatus { get; set; } = false;

    public void Toggle() => _enabled = !_enabled;

    public void Draw(World world)
    {
        if (!_enabled) return;
        _spriteBatch.Begin();

        // Draw current state above each AI entity
        if (ShowStates)
        {
            var query = new QueryDescription().WithAll<AiState, Position>();
            world.Query(query, (ref AiState ai, ref Position pos) =>
            {
                var color = ai.Current switch
                {
                    AiStateId.Idle   => Color.Gray,
                    AiStateId.Patrol => Color.Green,
                    AiStateId.Chase  => Color.Yellow,
                    AiStateId.Attack => Color.Red,
                    AiStateId.Flee   => Color.Cyan,
                    AiStateId.Dead   => Color.DarkGray,
                    _ => Color.White
                };
                DrawText($"{ai.Current} ({ai.TimeInState:F1}s)",
                    pos.Value + new Vector2(0, -20), color);
            });
        }

        // Draw vision cones
        if (ShowVisionCones)
        {
            var query = new QueryDescription().WithAll<Vision, Position, Facing>();
            world.Query(query, (ref Vision v, ref Position pos, ref Facing f) =>
            {
                DrawVisionCone(pos.Value, f.Direction, v.Range,
                    v.HalfAngleDeg, Color.Yellow * 0.2f);
            });
        }

        // Draw pathfinding paths
        if (ShowPaths)
        {
            var query = new QueryDescription().WithAll<PathComponent, Position>();
            world.Query(query, (ref PathComponent path, ref Position pos) =>
            {
                if (path.Waypoints == null) return;
                var prev = pos.Value;
                foreach (var wp in path.Waypoints)
                {
                    DrawLine(prev, wp, Color.Lime * 0.6f);
                    prev = wp;
                }
            });
        }

        // Draw steering force vectors
        if (ShowSteeringForces)
        {
            var query = new QueryDescription().WithAll<SteeringForce, Position>();
            world.Query(query, (ref SteeringForce sf, ref Position pos) =>
            {
                DrawArrow(pos.Value, pos.Value + sf.Accumulated * 5f, Color.Magenta);
            });
        }

        _spriteBatch.End();
    }

    // ... DrawText, DrawLine, DrawArrow, DrawVisionCone helper methods
}
```

### Behavior Tree Visualizer

```csharp
public class BtDebugger
{
    private readonly Dictionary<BtNode, BtStatus> _lastStatus = new();

    // Wrap tree execution with logging
    public BtStatus TickWithLog(BtNode root, BtContext ctx)
    {
        _lastStatus.Clear();
        var result = TickRecursive(root, ctx);
        return result;
    }

    private BtStatus TickRecursive(BtNode node, BtContext ctx)
    {
        var status = node.Tick(ctx);
        _lastStatus[node] = status;
        return status;
    }

    // Output tree state as string for debug console
    public string DumpTree(BtNode node, int depth = 0)
    {
        var sb = new StringBuilder();
        var indent = new string(' ', depth * 2);
        var statusChar = _lastStatus.TryGetValue(node, out var s) ? s switch
        {
            BtStatus.Success => "✓",
            BtStatus.Failure => "✗",
            BtStatus.Running => "→",
            _ => "?"
        } : "?";

        sb.AppendLine($"{indent}{statusChar} {node.GetType().Name}");

        // Recurse into composite children
        if (node is Sequence seq)
            foreach (var child in seq.Children) sb.Append(DumpTree(child, depth + 1));
        if (node is Selector sel)
            foreach (var child in sel.Children) sb.Append(DumpTree(child, depth + 1));

        return sb.ToString();
    }
}

// Output example:
// ✓ Selector
//   ✗ Sequence
//     ✗ Condition (enemy near)
//   ✓ Sequence
//     ✓ Condition (low hp)
//     → ActionNode (flee)
```

### Performance Profiling

```csharp
public class AiProfiler
{
    private readonly Dictionary<string, (double Total, int Count)> _timings = new();
    private Stopwatch _sw = new();

    public void Begin(string system) => _sw.Restart();

    public void End(string system)
    {
        _sw.Stop();
        var elapsed = _sw.Elapsed.TotalMilliseconds;
        if (_timings.TryGetValue(system, out var existing))
            _timings[system] = (existing.Total + elapsed, existing.Count + 1);
        else
            _timings[system] = (elapsed, 1);
    }

    public string Report()
    {
        var sb = new StringBuilder("=== AI Performance ===\n");
        foreach (var (name, (total, count)) in _timings.OrderByDescending(kv => kv.Value.Total))
        {
            sb.AppendLine($"  {name}: {total / count:F2}ms avg ({count} calls, {total:F1}ms total)");
        }
        return sb.ToString();
    }

    public void Reset() => _timings.Clear();
}
```

---

## 14. Common Mistakes & Troubleshooting

### ❌ Mistake 1: AI Updates Every Frame

**Problem:** Running pathfinding, behavior trees, and perception for 200 enemies at 60fps.

**Fix:** Stagger updates with `AiTick` (§Pipeline Overview). Most AI decisions don't change frame-to-frame.

```csharp
// Bad: 200 entities × 60fps = 12,000 AI ticks/second
[Query] public void Update(ref AiState ai) { /* expensive logic */ }

// Good: 200 entities × 10Hz ÷ 4 stagger groups = 500 AI ticks/second
[Query] [All<AiUpdateThisFrame>]
public void Update(ref AiState ai) { /* same logic, 24× fewer calls */ }
```

### ❌ Mistake 2: Pathfinding Every Frame

**Problem:** Entity recalculates A* path every frame while chasing the player.

**Fix:** Only repath when the target moves significantly or a timer expires.

```csharp
public record struct PathState(List<Point>? Path, Vector2 LastTargetPos, float RepathTimer);

[Query]
public void UpdatePath([Data] in float dt, ref PathState ps, ref Position pos, ref AggroTarget target)
{
    ps.RepathTimer -= dt;
    var targetPos = World.Get<Position>(target.Target).Value;

    // Only repath if target moved >2 tiles OR timer expired
    bool targetMoved = Vector2.DistanceSquared(targetPos, ps.LastTargetPos) > 64 * 64;
    if (ps.RepathTimer <= 0 || targetMoved)
    {
        ps.Path = AStar(walkable, ToTile(pos.Value), ToTile(targetPos));
        ps.LastTargetPos = targetPos;
        ps.RepathTimer = 0.5f; // max repath rate: 2Hz
    }
}
```

### ❌ Mistake 3: Perfect AI Aim

**Problem:** Enemies hit the player 100% of the time — feels unfair.

**Fix:** Add intentional inaccuracy that scales with difficulty.

```csharp
public static Vector2 AimWithInaccuracy(Vector2 origin, Vector2 target, float accuracy)
{
    var dir = Vector2.Normalize(target - origin);
    float spread = (1f - accuracy) * MathHelper.PiOver4; // up to 45° at 0 accuracy
    float angle = MathF.Atan2(dir.Y, dir.X);
    angle += (Random.Shared.NextSingle() - 0.5f) * 2f * spread;
    return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
}
```

### ❌ Mistake 4: State Machine Flickering

**Problem:** AI rapidly switches between Chase and Attack at the boundary distance.

**Fix:** Use hysteresis — different thresholds for entering and exiting a state.

```csharp
// Bad: same threshold for enter and exit
AiStateId.Chase when dist < 50 => AiStateId.Attack,
AiStateId.Attack when dist >= 50 => AiStateId.Chase,  // flickers at exactly 50

// Good: hysteresis band
AiStateId.Chase when dist < 45 => AiStateId.Attack,    // enter at 45
AiStateId.Attack when dist >= 55 => AiStateId.Chase,   // exit at 55 (10-unit dead zone)
```

### ❌ Mistake 5: GOAP Planner Runs Forever

**Problem:** With many actions, the planner explores thousands of states.

**Fix:** Set an iteration budget and validate action preconditions cheaply.

```csharp
// Always pass maxIterations to prevent runaway planning
var plan = GoapPlanner.Plan(state, goal, actions, maxIterations: 500);
if (plan == null)
{
    // Fallback behavior — don't freeze the game
    agent.SetFallbackBehavior();
}
```

### ❌ Mistake 6: Steering Forces Cancel Out

**Problem:** Seek + Separation produce opposing forces, entity vibrates in place.

**Fix:** Use priority-based steering (§5) where avoidance always wins.

### ❌ Mistake 7: Monolithic AI Component

**Problem:** One `AiBrain` component with 20 fields — every AI system queries it.

**Fix:** Decompose into small, focused components. Only add what each entity needs.

```csharp
// Bad
public record struct AiBrain(AiStateId State, float Timer, Vector2[] Patrol,
    Entity Target, float LastSeen, float Vision, float Hearing, ...);

// Good — compose what you need
World.Create(
    new AiState(AiStateId.Patrol, default, 0),
    new PatrolPath(waypoints, 0),
    new Vision(200f, 60f),
    new AiTick(0.1f, 0f, entityId % 4)   // 10Hz, staggered
);
// Aggressive enemies add more components:
World.Add<IsAggressive>(entity);
World.Add<AggroTarget>(entity, new(default, 0));
```

---

## Architecture Decision Guide

| Scenario | Recommended System | Complexity | CPU Cost |
|---|---|:-:|:-:|
| Simple enemy (2-4 states) | FSM | Low | Minimal |
| Complex multi-step behavior | Behavior Tree | Medium | Low |
| Many actions, continuous context | Utility AI | Medium | Low |
| Emergent/planning AI (guards, NPCs) | GOAP | High | Medium |
| NPC daily routines (Sims-style) | Utility AI | Medium | Low |
| Smooth movement/flocking | Steering Behaviors | Low | Low |
| RTS unit movement (many→one target) | Flow Fields | Medium | Medium (setup) |
| Single unit pathfinding | A* / JPS | Low | Medium |
| Strategic macro-AI (RTS commander) | Influence Maps | Medium | Low |
| Boss encounters | Phase FSM + Telegraph | Medium | Minimal |
| Squad coordination | Formation + Roles | Medium | Low |
| Adaptive difficulty | DDA + Performance Tracker | Low | Minimal |

### Combining Patterns (Real-World Example)

A guard NPC in a stealth game might use **all of these together**:

```
GOAP (high-level planning)
 └─ "I need to eliminate the intruder"
    └─ Plan: GoToArmory → GetWeapon → SearchArea → AttackIntruder

Behavior Tree (executing each GOAP action)
 └─ Sequence: Navigate → Interact → Validate
    └─ Navigate uses A* pathfinding + steering behaviors

Perception (vision + hearing)
 └─ Updates blackboard: "heard_noise", "can_see_player"
    └─ GOAP replans when world state changes

Utility AI (idle behavior selection)
 └─ When no threats: choose between Patrol, Chat, Eat based on needs

Influence Map (strategic awareness)
 └─ "Last seen intruder in sector 3" — searches there first

Dynamic Difficulty
 └─ Adjusts vision range, reaction time, patrol thoroughness
```

The ECS architecture makes this natural: each system reads/writes its own components without coupling. An entity can have `GoapAgent + BehaviorTreeRef + Vision + Hearing + SteeringAgent + UtilityNeeds` components simultaneously, and each system processes only the components it cares about.

---

## Genre-Specific AI Patterns

### Platformer

| System | Pattern | Notes |
|--------|---------|-------|
| Basic enemies | FSM (Patrol/Chase) | 2-3 states is enough |
| Flying enemies | Steering (Seek/Wander) | Sine-wave overlay for bob |
| Bosses | Phase FSM + Telegraph | 3 phases, learnable patterns |
| Difficulty | None or manual levels | Players expect consistent difficulty |

### Top-Down Shooter / Action RPG

| System | Pattern | Notes |
|--------|---------|-------|
| Grunts | FSM + A* pathfinding | Chase/shoot/cover |
| Elites | Behavior Tree | Multi-step tactics |
| Squads | Formation + Roles | Flanking, suppression |
| Bosses | GOAP or Phase FSM | Depends on complexity |

### RTS / Strategy

| System | Pattern | Notes |
|--------|---------|-------|
| Unit movement | Flow Fields | Shared fields per target |
| Macro AI | Influence Maps + Utility | Resource allocation |
| Micro AI | Steering + Flocking | Formation movement |
| Commander | GOAP | High-level strategy |

### Tower Defense

| System | Pattern | Notes |
|--------|---------|-------|
| Enemy pathing | Flow Fields (shared) | All enemies share one field |
| Special enemies | FSM (follow/ability/die) | Healers, bosses need states |
| Towers | Utility AI (target selection) | Closest? Strongest? Fastest? |
| Wave director | Utility AI | Adaptive spawn composition |

### Survival / Sandbox

| System | Pattern | Notes |
|--------|---------|-------|
| Wildlife | FSM + Steering | Graze/Flee/Attack |
| NPCs | Utility AI | Sims-style need satisfaction |
| Herds/flocks | Flocking behaviors | Boids for groups |
| Difficulty | DDA | Scale enemy aggression/spawns |

---

## Cross-References

- **[G40 Pathfinding](./G40_pathfinding.md)** — Full pathfinding guide: nav meshes, HPA*, JPS, path smoothing, dynamic obstacles
- **[G64 Combat & Damage](./G64_combat_damage_systems.md)** — Hitbox/hurtbox, damage pipeline, knockback, i-frames — AI attacks feed into this system
- **[G52 Character Controller](./G52_character_controller.md)** — Player movement + physics — AI steering forces integrate here
- **[G31 Animation State Machines](./G31_animation_state_machines.md)** — AI state changes trigger animation transitions
- **[G37 Tilemap Systems](./G37_tilemap_systems.md)** — Tile-based pathfinding, vision raycasting through tile grids
- **[G67 Object Pooling](./G67_object_pooling.md)** — Pool AI entities (especially projectiles, spawned enemies)
- **[G12 Design Patterns](./G12_design_patterns.md)** — Observer, Command, Strategy patterns used by AI systems
- **[G33 Profiling & Optimization](./G33_profiling_optimization.md)** — Performance budgeting for AI systems
- **[C1 Genre Reference](../../core/game-design/C1_genre_reference.md)** — Genre-specific AI requirements and complexity budgets
- **[R2 Capability Matrix](../reference/R2_capability_matrix.md)** — Which AI systems map to which engine features
