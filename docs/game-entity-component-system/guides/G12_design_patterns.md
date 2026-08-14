# G12 — Design Patterns


> **Category:** Guide · **Related:** [G11 Programming Principles](./G11_programming_principles.md) · [G1 Custom Code Recipes](./G1_custom_code_recipes.md) · [G10 Custom Game Systems](./G10_custom_game_systems.md)

---

Game-specific design patterns with full implementations for the MonoGame + Arch ECS stack.

---

## Observer Pattern / Event System

Enables UI, achievements, audio, and analytics to react to game events without coupling to the source.

**Critical pitfall:** Always unsubscribe when objects are destroyed, or memory leaks and null reference exceptions follow. This is the #1 memory leak in C# game code.

```csharp
// Listener must unsubscribe on destroy:
public class HealthBar : IDisposable
{
    private HealthComponent _health;

    public HealthBar(HealthComponent health)
    {
        _health = health;
        _health.HealthChanged += OnHealthChanged;
    }

    public void Dispose() => _health.HealthChanged -= OnHealthChanged;
}
```

### Event Bus (Decoupled Communication)

For communication between distant systems. In Arch, the built-in `Arch.EventBus` handles ECS-side events. For game-wide events:

```csharp
// Typed event bus — struct events avoid GC pressure
public static class GameEvents
{
    private static readonly Dictionary<Type, List<Delegate>> _handlers = new();

    public static void Subscribe<T>(Action<T> handler) where T : struct
    {
        var type = typeof(T);
        if (!_handlers.ContainsKey(type)) _handlers[type] = new();
        _handlers[type].Add(handler);
    }

    public static void Unsubscribe<T>(Action<T> handler) where T : struct
    {
        if (_handlers.TryGetValue(typeof(T), out var list))
            list.Remove(handler);
    }

    public static void Raise<T>(T evt) where T : struct
    {
        if (_handlers.TryGetValue(typeof(T), out var list))
            foreach (var handler in list)
                ((Action<T>)handler)(evt);
    }
}

// Events are value types — zero GC
public struct EnemyKilledEvent { public int EnemyId; public int KillerId; public int Points; }
public struct ScoreChangedEvent { public int NewScore; }
```

**Design note:** Synchronous dispatch can cause cascade issues when a handler raises another event mid-iteration. Queue events during the frame and process them once at frame end for deterministic ordering.

---

## Command Pattern

Turns actions into objects. Enables undo/redo, input rebinding, replay recording, network command buffering.

```csharp
public interface ICommand
{
    void Execute();
    void Undo();
}

public class MoveCommand : ICommand
{
    private readonly Entity _unit;
    private readonly Vector2 _from, _to;

    public MoveCommand(Entity unit, Vector2 to)
    {
        _unit = unit;
        _from = unit.Position;
        _to = to;
    }

    public void Execute() => _unit.Position = _to;
    public void Undo() => _unit.Position = _from;
}

public class CommandHistory
{
    private readonly List<ICommand> _history = new();
    private int _currentIndex = -1;

    public void Execute(ICommand command)
    {
        // Remove any commands after current (redo branch pruning)
        if (_currentIndex < _history.Count - 1)
            _history.RemoveRange(_currentIndex + 1, _history.Count - _currentIndex - 1);

        command.Execute();
        _history.Add(command);
        _currentIndex++;
    }

    public void Undo()
    {
        if (_currentIndex >= 0)
            _history[_currentIndex--].Undo();
    }

    public void Redo()
    {
        if (_currentIndex < _history.Count - 1)
            _history[++_currentIndex].Execute();
    }
}
```

**Essential for:** Puzzle games, strategy games, level editors → [G10](./G10_custom_game_systems.md).

---

## State Machines

The workhorse pattern for managing discrete states. Only one state is active at any time.

```csharp
public abstract class State
{
    public StateMachine Machine { get; set; }
    public virtual void Enter() { }
    public virtual void Exit() { }
    public virtual void Update(float delta) { }
    public virtual void HandleInput(/* input data */) { }
}

public class StateMachine
{
    public State CurrentState { get; private set; }
    private Dictionary<string, State> _states = new();

    public void AddState(string name, State state)
    {
        _states[name] = state;
        state.Machine = this;
    }

    public void TransitionTo(string stateName)
    {
        if (!_states.TryGetValue(stateName, out var newState)) return;
        CurrentState?.Exit();
        CurrentState = newState;
        CurrentState.Enter();
    }

    public void Update(float delta) => CurrentState?.Update(delta);
}
```

**Hierarchical State Machines:** Substates inherit common behavior from parent states — all "on ground" states (standing, walking, running) share jump and duck transitions.

**Pushdown Automata:** State stack for temporary states — firing a weapon while running pushes FiringState, then pops back to RunningState when complete.

**Scaling limit:** When FSM transitions grow geometrically with state count, switch to Behavior Trees via BrainAI → [G4](./G4_ai_systems.md).

---

## Service Locator (Singleton Alternative)

Singletons create global state that hides dependencies. Better alternative:

```csharp
public static class Services
{
    private static IAudioService _audio = new NullAudioService();
    private static IInputService _input = new NullInputService();

    public static IAudioService Audio => _audio;
    public static IInputService Input => _input;

    public static void Provide(IAudioService audio) => _audio = audio ?? new NullAudioService();
    public static void Provide(IInputService input) => _input = input ?? new NullInputService();
}

// NullAudioService does nothing — no null checks needed anywhere
public class NullAudioService : IAudioService
{
    public void PlaySound(string name) { }
    public void PlayMusic(string name) { }
    public void StopAll() { }
}
```

**Use Service Locator for:** 5-8 ambient services (audio, input, rendering, asset loading, scene management). **Use constructor DI for:** Everything else.

> **Gotcha:** Inside any class extending MonoGame's `Game` (e.g., `GameApp`), the name `Services` resolves to `Game.Services` (`GameServiceContainer`), not this static class. Use the fully qualified namespace: `YourNamespace.Services.Provide(...)`.

---

## Strategy Pattern

Encapsulates swappable algorithms changeable at runtime.

```csharp
public interface IMovementStrategy
{
    Vector2 CalculateVelocity(Entity actor, Vector2 input, float delta);
}

public class WalkMovement : IMovementStrategy
{
    public float Speed { get; set; } = 200f;
    public Vector2 CalculateVelocity(Entity actor, Vector2 input, float delta)
        => input.Normalized() * Speed;
}

public class FlyMovement : IMovementStrategy
{
    public float Speed { get; set; } = 300f;
    public Vector2 CalculateVelocity(Entity actor, Vector2 input, float delta)
        => input.Normalized() * Speed; // Plus vertical control
}

// Swap at runtime
public class PlayerController
{
    private IMovementStrategy _movement = new WalkMovement();
    public void SetMovement(IMovementStrategy strategy) => _movement = strategy;
}
```

---

## Flyweight Pattern

Share immutable data among many objects. 10,000 trees each storing their own mesh/texture data = massive waste. Share one flyweight, store only per-instance position.

```csharp
// Shared data (Flyweight) — loaded once, referenced by many
public class EnemyData
{
    public string Name { get; set; }
    public int BaseHealth { get; set; }
    public int BaseDamage { get; set; }
    public string SpritePath { get; set; }
}

// Per-instance state — each enemy has its own
public struct EnemyInstance
{
    public int CurrentHealth;
    public Vector2 Position;
    public EnemyData SharedData; // Reference to shared flyweight
}
```

In Arch ECS, this maps naturally to shared component references loaded from JSON.

---

## Factory Pattern

Centralizes object creation. Data-driven creation from type strings (loaded from level files, spawn tables, etc).

```csharp
public class EnemyFactory
{
    private readonly Dictionary<string, Func<Vector2, Entity>> _creators = new();

    public void Register(string type, Func<Vector2, Entity> creator)
        => _creators[type] = creator;

    public Entity Create(string type, Vector2 position)
    {
        if (!_creators.TryGetValue(type, out var creator))
            throw new ArgumentException($"Unknown enemy type: {type}");
        return creator(position);
    }
}
```
