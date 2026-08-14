# G17 — Testing


> **Category:** Guide · **Related:** [G11 Programming Principles](./G11_programming_principles.md) · [G16 Debugging](./G16_debugging.md) · [G3 Physics & Collision](./G3_physics_and_collision.md) · [G13 C# Performance](./G13_csharp_performance.md) · [P12 Performance Budget](./P12_performance_budget.md)

> Comprehensive testing guide for MonoGame + Arch ECS projects covering unit tests, ECS system tests, integration patterns, fixtures, and performance benchmarking.

---

## 1. xUnit Project Setup for MonoGame

### Project Structure

```
MyGame/
├── MyGame/                     # Main game project
│   ├── Components/
│   ├── Systems/
│   └── MyGame.csproj
├── MyGame.Tests/               # Test project
│   ├── Unit/
│   ├── Integration/
│   ├── Fixtures/
│   ├── Helpers/
│   └── MyGame.Tests.csproj
└── MyGame.Benchmarks/          # Performance tests
    └── MyGame.Benchmarks.csproj
```

### Test Project (.csproj)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="xunit" Version="2.9.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.*" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="NSubstitute" Version="5.*" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\MyGame\MyGame.csproj" />
  </ItemGroup>
</Project>
```

### Running Tests

```bash
dotnet test                           # Run all tests
dotnet test --filter "Category=Unit"  # Run only unit tests
dotnet test --filter "FullyQualifiedName~HealthTests"  # Run specific class
dotnet watch test                     # Re-run on file changes
```

### Key Principle: Separate Logic from Framework

MonoGame's `Game` class, `GraphicsDevice`, and content pipeline are hard to instantiate in tests. Design around this:

```csharp
// BAD: logic embedded in Game subclass — untestable
public class MyGame : Game
{
    public void ApplyDamage(Entity e, int amount)
    {
        var hp = world.Get<Health>(e);
        hp.Current -= amount;
        // ... rendering, sound effects mixed in
    }
}

// GOOD: pure logic in standalone classes/static methods
public static class CombatMath
{
    public static int CalculateDamage(int baseDamage, float armor, float resistance)
    {
        float reduction = armor / (armor + 100f);
        return Math.Max(1, (int)(baseDamage * (1f - reduction) * (1f - resistance)));
    }
}

// Now trivially testable:
[Fact]
public void CalculateDamage_ArmorReducesDamage()
{
    int damage = CombatMath.CalculateDamage(baseDamage: 50, armor: 100f, resistance: 0f);
    Assert.Equal(25, damage); // 100 / (100+100) = 50% reduction
}
```

---

## 2. Unit Testing Pure Game Logic

### Testing Components

Components should be plain data — `record struct` in Arch. Test any validation or computed properties:

```csharp
public record struct Health(int Current, int Max)
{
    public float Percentage => Max > 0 ? (float)Current / Max : 0f;
    public bool IsDead => Current <= 0;

    public Health TakeDamage(int amount) =>
        this with { Current = Math.Max(0, Current - amount) };

    public Health Heal(int amount) =>
        this with { Current = Math.Min(Max, Current + amount) };
}

public class HealthTests
{
    [Fact]
    public void TakeDamage_ClampsAtZero()
    {
        var hp = new Health(10, 100);
        hp = hp.TakeDamage(50);
        Assert.Equal(0, hp.Current); // Would be -40 without clamping
    }

    [Fact]
    public void Heal_ClampsAtMax()
    {
        var hp = new Health(90, 100);
        hp = hp.Heal(50);
        Assert.Equal(100, hp.Current);
    }

    [Fact]
    public void Percentage_ReturnsCorrectRatio()
    {
        var hp = new Health(75, 100);
        Assert.Equal(0.75f, hp.Percentage, precision: 3);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(-5, true)]
    [InlineData(1, false)]
    public void IsDead_ReflectsCurrentHealth(int current, bool expected)
    {
        var hp = new Health(current, 100);
        Assert.Equal(expected, hp.IsDead);
    }
}
```

### Testing Math Utilities

Game math is a prime testing target — small errors cascade into visible bugs:

```csharp
public static class GameMath
{
    public static float Approach(float current, float target, float delta)
    {
        if (current < target)
            return MathF.Min(current + delta, target);
        return MathF.Max(current - delta, target);
    }

    public static Vector2 MoveToward(Vector2 current, Vector2 target, float maxDist)
    {
        var diff = target - current;
        float dist = diff.Length();
        if (dist <= maxDist || dist < float.Epsilon) return target;
        return current + (diff / dist) * maxDist;
    }

    public static float AngleBetween(Vector2 a, Vector2 b) =>
        MathF.Atan2(b.Y - a.Y, b.X - a.X);
}

public class GameMathTests
{
    [Fact]
    public void Approach_FromBelow_DoesNotOvershoot()
    {
        float result = GameMath.Approach(0f, 10f, 15f);
        Assert.Equal(10f, result);
    }

    [Fact]
    public void Approach_FromAbove_DoesNotOvershoot()
    {
        float result = GameMath.Approach(10f, 3f, 15f);
        Assert.Equal(3f, result);
    }

    [Fact]
    public void MoveToward_StopsAtTarget()
    {
        var result = GameMath.MoveToward(Vector2.Zero, new Vector2(3, 4), 100f);
        Assert.Equal(new Vector2(3, 4), result);
    }

    [Fact]
    public void MoveToward_MovesCorrectDistance()
    {
        var result = GameMath.MoveToward(Vector2.Zero, new Vector2(100, 0), 5f);
        Assert.Equal(5f, result.X, precision: 3);
        Assert.Equal(0f, result.Y, precision: 3);
    }

    [Theory]
    [InlineData(0, 0, 1, 0, 0f)]          // Right → 0 radians
    [InlineData(0, 0, 0, 1, MathF.PI / 2)] // Down → π/2
    public void AngleBetween_ReturnsCorrectAngle(
        float ax, float ay, float bx, float by, float expected)
    {
        float angle = GameMath.AngleBetween(new(ax, ay), new(bx, by));
        Assert.Equal(expected, angle, precision: 3);
    }
}
```

### Testing State Machines

```csharp
public class StateMachineTests
{
    private readonly StateMachine<AiStateId> _fsm;
    private readonly List<string> _log = new();

    public StateMachineTests()
    {
        _fsm = new StateMachine<AiStateId>();
        _fsm.AddState(new State<AiStateId>
        {
            Id = AiStateId.Idle,
            OnEnter = () => _log.Add("Enter:Idle"),
            OnExit = () => _log.Add("Exit:Idle"),
        });
        _fsm.AddState(new State<AiStateId>
        {
            Id = AiStateId.Chase,
            OnEnter = () => _log.Add("Enter:Chase"),
            OnExit = () => _log.Add("Exit:Chase"),
        });
        _fsm.SetInitialState(AiStateId.Idle);
    }

    [Fact]
    public void SetInitialState_CallsOnEnter()
    {
        Assert.Contains("Enter:Idle", _log);
    }

    [Fact]
    public void Transition_CallsExitThenEnter()
    {
        _log.Clear();
        _fsm.Transition(AiStateId.Chase);

        Assert.Equal(new[] { "Exit:Idle", "Enter:Chase" }, _log);
    }

    [Fact]
    public void Transition_ToSameState_DoesNothing()
    {
        _log.Clear();
        _fsm.Transition(AiStateId.Idle);

        Assert.Empty(_log);
    }
}
```

---

## 3. Mocking & Testing Arch ECS Systems

### Creating Test Worlds

Arch worlds are lightweight — create a fresh one per test:

```csharp
public class MovementSystemTests : IDisposable
{
    private readonly World _world;
    private readonly MovementSystem _system;

    public MovementSystemTests()
    {
        _world = World.Create();
        _system = new MovementSystem(_world);
    }

    public void Dispose() => World.Destroy(_world);

    [Fact]
    public void Update_AppliesVelocityToPosition()
    {
        var entity = _world.Create(
            new Position(0f, 0f),
            new Velocity(60f, 0f)
        );

        float dt = 1f / 60f;
        _system.Update(in dt);

        var pos = _world.Get<Position>(entity);
        Assert.Equal(1f, pos.X, precision: 3);
        Assert.Equal(0f, pos.Y, precision: 3);
    }

    [Fact]
    public void Update_MultipleEntities_AllMove()
    {
        var e1 = _world.Create(new Position(0, 0), new Velocity(10, 0));
        var e2 = _world.Create(new Position(0, 0), new Velocity(0, 10));

        float dt = 1f;
        _system.Update(in dt);

        Assert.Equal(10f, _world.Get<Position>(e1).X, precision: 3);
        Assert.Equal(10f, _world.Get<Position>(e2).Y, precision: 3);
    }
}
```

### Testing Systems That Query Multiple Archetypes

Verify that query filters work — entities missing required components shouldn't be processed:

```csharp
public class AiSystemTests : IDisposable
{
    private readonly World _world;
    private readonly AiStateSystem _system;

    public AiSystemTests()
    {
        _world = World.Create();
        _system = new AiStateSystem(_world);
    }

    public void Dispose() => World.Destroy(_world);

    [Fact]
    public void StunnedEntities_AreNotProcessed()
    {
        // This entity has Stunned tag — AiStateSystem uses [None<Stunned>]
        var entity = _world.Create(
            new AiState(AiStateId.Patrol, AiStateId.Idle, 0f),
            new Position(0, 0),
            new Health(100, 100),
            new Stunned()
        );

        float dt = 1f;
        _system.Update(in dt);

        // AI state should be unchanged
        var ai = _world.Get<AiState>(entity);
        Assert.Equal(AiStateId.Patrol, ai.Current);
        Assert.Equal(0f, ai.TimeInState); // Not incremented
    }

    [Fact]
    public void FleeState_WhenHealthLow()
    {
        var entity = _world.Create(
            new AiState(AiStateId.Patrol, AiStateId.Idle, 0f),
            new Position(0, 0),
            new Health(10, 100) // 10% HP
        );

        float dt = 1f / 60f;
        _system.Update(in dt);

        var ai = _world.Get<AiState>(entity);
        Assert.Equal(AiStateId.Flee, ai.Current);
    }
}
```

### Testing CommandBuffer Usage

When systems defer structural changes via `CommandBuffer`, verify them after playback:

```csharp
[Fact]
public void DeathSystem_RemovesAiComponents_AfterPlayback()
{
    var entity = _world.Create(
        new Health(0, 100),
        new AiState(AiStateId.Chase, AiStateId.Patrol, 5f),
        new AggroTarget()
    );

    float dt = 0f;
    _deathSystem.Update(in dt);      // Buffers changes
    _deathSystem.AfterUpdate(in dt); // Plays back the CommandBuffer

    Assert.False(_world.Has<AggroTarget>(entity));
    Assert.Equal(AiStateId.Dead, _world.Get<AiState>(entity).Current);
}
```

### Asserting Entity Counts with Queries

```csharp
[Fact]
public void SpawnerSystem_CreatesEntities()
{
    _world.Create(new Spawner { SpawnRate = 1f, Timer = 1f, Prefab = PrefabId.Slime });

    float dt = 1f;
    _spawnerSystem.Update(in dt);

    // Count all entities matching the spawned archetype
    var query = new QueryDescription().WithAll<Position, Health, AiState>();
    int count = 0;
    _world.CountEntities(in query);
    Assert.Equal(1, _world.CountEntities(in query));
}
```

---

## 4. Integration Testing Patterns

### Service Interfaces for Testability

Mock engine services through interfaces (see also [G12 Design Patterns](./G12_design_patterns.md)):

```csharp
public interface IInputService
{
    Vector2 GetMovementVector();
    bool IsActionJustPressed(string action);
}

public interface IAudioService
{
    void PlaySfx(string name);
    void PlayMusic(string name);
}

public interface ITimeService
{
    float DeltaTime { get; }
    float TotalTime { get; }
}
```

### Mock Implementations

```csharp
public class MockInputService : IInputService
{
    public Vector2 MovementToReturn { get; set; }
    public HashSet<string> PressedActions { get; set; } = new();

    public Vector2 GetMovementVector() => MovementToReturn;
    public bool IsActionJustPressed(string action) => PressedActions.Contains(action);
}

public class MockAudioService : IAudioService
{
    public List<string> PlayedSfx { get; } = new();
    public List<string> PlayedMusic { get; } = new();

    public void PlaySfx(string name) => PlayedSfx.Add(name);
    public void PlayMusic(string name) => PlayedMusic.Add(name);
}

public class MockTimeService : ITimeService
{
    public float DeltaTime { get; set; } = 1f / 60f;
    public float TotalTime { get; set; }
}
```

### Full System Integration Test

Wire up multiple systems with mock services and verify end-to-end behavior:

```csharp
public class PlayerCombatIntegrationTests : IDisposable
{
    private readonly World _world;
    private readonly MockInputService _input;
    private readonly MockAudioService _audio;
    private readonly PlayerAttackSystem _attackSystem;
    private readonly DamageSystem _damageSystem;
    private readonly DeathSystem _deathSystem;

    public PlayerCombatIntegrationTests()
    {
        _world = World.Create();
        _input = new MockInputService();
        _audio = new MockAudioService();
        _attackSystem = new PlayerAttackSystem(_world, _input, _audio);
        _damageSystem = new DamageSystem(_world);
        _deathSystem = new DeathSystem(_world);
    }

    public void Dispose() => World.Destroy(_world);

    [Fact]
    public void AttackInput_DamagesNearbyEnemy_KillsAtZeroHP()
    {
        // Arrange: player and a 1-HP enemy nearby
        var player = _world.Create(
            new Position(100, 100),
            new PlayerTag(),
            new Attack { Damage = 50, Range = 32f, Cooldown = 0f }
        );
        var enemy = _world.Create(
            new Position(110, 100),
            new Health(1, 100),
            new EnemyTag()
        );

        // Act: simulate attack input
        _input.PressedActions.Add("attack");
        float dt = 1f / 60f;
        _attackSystem.Update(in dt);
        _damageSystem.Update(in dt);
        _deathSystem.Update(in dt);
        _deathSystem.AfterUpdate(in dt);

        // Assert
        Assert.True(_world.Get<Health>(enemy).IsDead);
        Assert.Contains("hit_impact", _audio.PlayedSfx);
    }
}
```

---

## 5. Testing Game Systems In Depth

### Physics & Collision

```csharp
public class CollisionTests
{
    [Theory]
    [InlineData(0, 0, 10, 5, 5, 10, true)]     // Overlapping circles
    [InlineData(0, 0, 5, 100, 100, 5, false)]   // Far apart
    [InlineData(0, 0, 10, 15, 0, 10, true)]     // Edge-touching (dist=15, r1+r2=20)
    public void CircleVsCircle_DetectsCorrectly(
        float x1, float y1, float r1,
        float x2, float y2, float r2, bool expected)
    {
        bool result = CollisionMath.CirclesOverlap(
            new Vector2(x1, y1), r1,
            new Vector2(x2, y2), r2);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void AABB_Overlap_ReturnsMinimumTranslation()
    {
        var a = new AABB(0, 0, 20, 20);
        var b = new AABB(15, 10, 20, 20);

        var mtv = CollisionMath.GetMTV(a, b);

        Assert.True(mtv.HasValue);
        // Minimum push: 5 units on X axis
        Assert.Equal(-5f, mtv.Value.X, precision: 3);
    }

    [Fact]
    public void CollisionLayers_FilterCorrectly()
    {
        var player = new Collider
        {
            Layer = CollisionLayer.Player,
            Mask = CollisionLayer.Enemy | CollisionLayer.Terrain
        };
        var playerBullet = new Collider
        {
            Layer = CollisionLayer.PlayerBullet,
            Mask = CollisionLayer.Enemy | CollisionLayer.Terrain
        };

        // Player should NOT collide with own bullets
        Assert.False(ShouldCollide(player, playerBullet));
    }

    private static bool ShouldCollide(Collider a, Collider b) =>
        (a.Layer & b.Mask) != 0 && (b.Layer & a.Mask) != 0;
}
```

### AI Decision Testing

Test AI decision logic deterministically by controlling all inputs:

```csharp
public class UtilityAiTests
{
    [Fact]
    public void FleeAction_ScoresHighest_WhenLowHealth()
    {
        var actions = new List<UtilityAction>
        {
            new()
            {
                Name = "Attack",
                Considerations = { (e, w) => ResponseCurve.Linear(
                    w.Get<Health>(e).Percentage, m: 1f) } // Higher HP → more attack desire
            },
            new()
            {
                Name = "Flee",
                Considerations = { (e, w) => ResponseCurve.Linear(
                    1f - w.Get<Health>(e).Percentage, m: 1.5f) } // Lower HP → more flee desire
            },
        };

        var world = World.Create();
        var entity = world.Create(new Health(10, 100)); // 10% HP

        var selected = UtilitySelector.Select(actions, entity, world);

        Assert.Equal("Flee", selected!.Name);
        World.Destroy(world);
    }
}
```

### Inventory Operations

```csharp
public class InventoryTests
{
    private readonly Inventory _inventory;

    public InventoryTests()
    {
        _inventory = new Inventory(maxSlots: 10, maxStackSize: 64);
    }

    [Fact]
    public void AddItem_ToEmptyInventory_OccupiesFirstSlot()
    {
        bool added = _inventory.TryAdd(new Item(ItemId.HealthPotion, quantity: 5));

        Assert.True(added);
        Assert.Equal(5, _inventory.GetSlot(0).Quantity);
    }

    [Fact]
    public void AddItem_StacksWithExisting()
    {
        _inventory.TryAdd(new Item(ItemId.Arrow, quantity: 30));
        _inventory.TryAdd(new Item(ItemId.Arrow, quantity: 20));

        Assert.Equal(50, _inventory.GetSlot(0).Quantity);
        Assert.Equal(1, _inventory.OccupiedSlots); // Still one slot
    }

    [Fact]
    public void AddItem_SplitsOverflow_IntoNextSlot()
    {
        _inventory.TryAdd(new Item(ItemId.Arrow, quantity: 50));
        _inventory.TryAdd(new Item(ItemId.Arrow, quantity: 30));

        Assert.Equal(64, _inventory.GetSlot(0).Quantity); // Capped
        Assert.Equal(16, _inventory.GetSlot(1).Quantity); // Overflow
    }

    [Fact]
    public void AddItem_FullInventory_ReturnsFalse()
    {
        for (int i = 0; i < 10; i++)
            _inventory.TryAdd(new Item(ItemId.Sword, quantity: 1)); // Unstackable

        bool added = _inventory.TryAdd(new Item(ItemId.Shield, quantity: 1));
        Assert.False(added);
    }

    [Fact]
    public void RemoveItem_ReducesQuantity()
    {
        _inventory.TryAdd(new Item(ItemId.HealthPotion, quantity: 5));
        _inventory.TryRemove(ItemId.HealthPotion, quantity: 3);

        Assert.Equal(2, _inventory.GetSlot(0).Quantity);
    }

    [Fact]
    public void SwapSlots_ExchangesContents()
    {
        _inventory.TryAdd(new Item(ItemId.Sword, 1));
        _inventory.TryAdd(new Item(ItemId.Shield, 1));

        _inventory.SwapSlots(0, 1);

        Assert.Equal(ItemId.Shield, _inventory.GetSlot(0).Id);
        Assert.Equal(ItemId.Sword, _inventory.GetSlot(1).Id);
    }
}
```

---

## 6. Test Fixtures & Helpers

### MonoGame Type Helpers

MonoGame types like `Vector2`, `GameTime`, and `Rectangle` appear everywhere. Build helpers to reduce boilerplate:

```csharp
public static class TestHelpers
{
    // GameTime factory
    public static GameTime MakeGameTime(float totalSeconds, float deltaSeconds = 1f / 60f) =>
        new(TimeSpan.FromSeconds(totalSeconds), TimeSpan.FromSeconds(deltaSeconds));

    // Vector2 assertions with tolerance
    public static void AssertApprox(Vector2 expected, Vector2 actual, float tolerance = 0.01f)
    {
        Assert.InRange(actual.X, expected.X - tolerance, expected.X + tolerance);
        Assert.InRange(actual.Y, expected.Y - tolerance, expected.Y + tolerance);
    }

    // Quick entity builders
    public static Entity CreateEnemy(World world, float x, float y, int hp = 100) =>
        world.Create(
            new Position(x, y),
            new Health(hp, hp),
            new AiState(AiStateId.Idle, AiStateId.Idle, 0f),
            new EnemyTag()
        );

    public static Entity CreatePlayer(World world, float x, float y) =>
        world.Create(
            new Position(x, y),
            new Health(100, 100),
            new Velocity(0, 0),
            new PlayerTag()
        );

    // Simulate N frames
    public static void SimulateFrames(Action<float> updateFn, int frames, float dt = 1f / 60f)
    {
        for (int i = 0; i < frames; i++)
            updateFn(dt);
    }
}
```

### World Fixture (Shared Across Tests in a Class)

```csharp
public class WorldFixture : IDisposable
{
    public World World { get; }
    public MockInputService Input { get; } = new();
    public MockAudioService Audio { get; } = new();
    public MockTimeService Time { get; } = new();

    public WorldFixture()
    {
        World = World.Create();
    }

    public void Dispose() => World.Destroy(World);
}

// Use as a class fixture (one per test class)
public class EnemyBehaviorTests : IClassFixture<WorldFixture>
{
    private readonly WorldFixture _fix;
    public EnemyBehaviorTests(WorldFixture fix) => _fix = fix;

    [Fact]
    public void EnemySpawns_WithCorrectDefaults()
    {
        var enemy = TestHelpers.CreateEnemy(_fix.World, 50, 50);
        Assert.Equal(100, _fix.World.Get<Health>(enemy).Current);
    }
}
```

### Custom xUnit Assertions

```csharp
public static class GameAssert
{
    public static void IsAlive(World world, Entity entity) =>
        Assert.False(world.Get<Health>(entity).IsDead,
            $"Entity {entity} should be alive but is dead");

    public static void IsDead(World world, Entity entity) =>
        Assert.True(world.Get<Health>(entity).IsDead,
            $"Entity {entity} should be dead but is alive");

    public static void HasComponent<T>(World world, Entity entity) =>
        Assert.True(world.Has<T>(entity),
            $"Entity {entity} missing component {typeof(T).Name}");

    public static void InRange(Vector2 pos, Vector2 target, float range) =>
        Assert.True(Vector2.Distance(pos, target) <= range,
            $"Position {pos} not within {range} of {target}");
}
```

---

## 7. Deterministic Testing Patterns

### Seeded Random for Reproducible Tests

```csharp
public class SeededRandomTests
{
    [Fact]
    public void CriticalHit_IsReproducible()
    {
        var rng = new Random(seed: 42);
        var combat = new CombatResolver(rng);

        // Same seed → same results every time
        var result1 = combat.CalculateHit(attacker: 80, defender: 50);
        var result2 = combat.CalculateHit(attacker: 80, defender: 50);

        // These assertions are stable across runs because the seed is fixed
        Assert.True(result1.Hit);
        Assert.False(result1.Critical);
    }
}

// Game code uses injectable RNG
public class CombatResolver
{
    private readonly Random _rng;
    public CombatResolver(Random rng) => _rng = rng;

    public HitResult CalculateHit(int attacker, int defender)
    {
        float roll = _rng.NextSingle();
        float hitChance = attacker / (float)(attacker + defender);
        bool hit = roll < hitChance;
        bool crit = hit && roll < hitChance * 0.1f;
        return new HitResult(hit, crit);
    }
}
```

### Frame-Stepping for Time-Dependent Logic

```csharp
[Fact]
public void Cooldown_ExpiresAfterDuration()
{
    var world = World.Create();
    var entity = world.Create(new Cooldown { Remaining = 0.5f });
    var system = new CooldownSystem(world);

    // Step 30 frames at 60fps = 0.5 seconds
    TestHelpers.SimulateFrames(dt => system.Update(in dt), frames: 30);

    var cd = world.Get<Cooldown>(entity);
    Assert.True(cd.Remaining <= 0f);

    World.Destroy(world);
}

[Fact]
public void Timer_TriggersCallbackOnExpiry()
{
    bool triggered = false;
    var timer = new GameTimer(0.1f, () => triggered = true);

    timer.Update(0.05f); // 50ms
    Assert.False(triggered);

    timer.Update(0.06f); // 60ms — total 110ms, over threshold
    Assert.True(triggered);
}
```

---

## 8. Serialization & Save/Load Testing

```csharp
public class SaveSystemTests
{
    [Fact]
    public void SaveAndLoad_PreservesPlayerState()
    {
        var world = World.Create();
        var player = world.Create(
            new Position(150.5f, 200.3f),
            new Health(73, 100),
            new PlayerTag()
        );

        // Save
        var saveData = SaveSystem.Serialize(world);

        // Load into fresh world
        var world2 = World.Create();
        SaveSystem.Deserialize(world2, saveData);

        // Verify
        var query = new QueryDescription().WithAll<Position, Health, PlayerTag>();
        int count = 0;
        world2.Query(in query, (ref Position pos, ref Health hp) =>
        {
            Assert.Equal(150.5f, pos.X, precision: 1);
            Assert.Equal(73, hp.Current);
            count++;
        });
        Assert.Equal(1, count);

        World.Destroy(world);
        World.Destroy(world2);
    }

    [Fact]
    public void SaveData_RoundTrips_ThroughJson()
    {
        var original = new SaveData
        {
            PlayerPosition = new Vector2(100, 200),
            Health = 50,
            InventoryItems = { new SavedItem(ItemId.Sword, 1), new SavedItem(ItemId.Arrow, 30) }
        };

        string json = JsonSerializer.Serialize(original);
        var loaded = JsonSerializer.Deserialize<SaveData>(json);

        Assert.Equal(original.PlayerPosition.X, loaded!.PlayerPosition.X);
        Assert.Equal(original.InventoryItems.Count, loaded.InventoryItems.Count);
    }
}
```

---

## 9. Performance & Benchmark Testing

### BenchmarkDotNet Setup

```xml
<!-- MyGame.Benchmarks.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="BenchmarkDotNet" Version="0.14.*" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\MyGame\MyGame.csproj" />
  </ItemGroup>
</Project>
```

```csharp
// Program.cs
using BenchmarkDotNet.Running;
BenchmarkRunner.Run<SpatialQueryBenchmarks>();
```

### Benchmarking Spatial Queries

```csharp
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
public class SpatialQueryBenchmarks
{
    private SpatialHash<int> _hash = null!;
    private List<Vector2> _positions = null!;
    private readonly List<int> _results = new();

    [Params(100, 1000, 10_000)]
    public int EntityCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _hash = new SpatialHash<int>(64f);
        _positions = new List<Vector2>();
        var rng = new Random(42);

        for (int i = 0; i < EntityCount; i++)
        {
            var pos = new Vector2(rng.NextSingle() * 2000, rng.NextSingle() * 2000);
            _positions.Add(pos);
            _hash.Insert(pos, i);
        }
    }

    [Benchmark(Baseline = true)]
    public int BruteForce()
    {
        int count = 0;
        var center = new Vector2(1000, 1000);
        float r2 = 100f * 100f;
        foreach (var pos in _positions)
            if (Vector2.DistanceSquared(center, pos) <= r2) count++;
        return count;
    }

    [Benchmark]
    public int SpatialHash()
    {
        _results.Clear();
        _hash.QueryRadius(new Vector2(1000, 1000), 100f, _results);
        return _results.Count;
    }
}
```

### Benchmarking ECS System Throughput

```csharp
[MemoryDiagnoser]
public class EcsSystemBenchmarks
{
    private World _world = null!;
    private MovementSystem _movement = null!;

    [Params(1_000, 10_000, 100_000)]
    public int EntityCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _world = World.Create();
        _movement = new MovementSystem(_world);
        var rng = new Random(42);

        for (int i = 0; i < EntityCount; i++)
        {
            _world.Create(
                new Position(rng.NextSingle() * 1000, rng.NextSingle() * 1000),
                new Velocity(rng.NextSingle() * 100, rng.NextSingle() * 100)
            );
        }
    }

    [GlobalCleanup]
    public void Cleanup() => World.Destroy(_world);

    [Benchmark]
    public void MovementSystem_Update()
    {
        float dt = 1f / 60f;
        _movement.Update(in dt);
    }
}
```

### Running Benchmarks

```bash
dotnet run -c Release --project MyGame.Benchmarks

# Output example:
# | Method       | EntityCount |      Mean |   Gen0 | Allocated |
# |------------- |------------ |----------:|-------:|----------:|
# | BruteForce   |       1,000 |  4.52 μs |      - |         - |
# | SpatialHash  |       1,000 |  0.89 μs |      - |         - |
# | BruteForce   |      10,000 | 45.10 μs |      - |         - |
# | SpatialHash  |      10,000 |  1.02 μs |      - |         - |
```

---

## 10. Test Organization & Best Practices

### Naming Convention

```
[SystemUnderTest]_[Scenario]_[ExpectedResult]
```

```csharp
public void TakeDamage_MoreThanCurrentHealth_ClampsToZero() { }
public void Inventory_AddToFullSlots_ReturnsFalse() { }
public void AiState_HealthBelow20Percent_TransitionsToFlee() { }
```

### Test Categories with Traits

```csharp
[Trait("Category", "Unit")]
public class CombatMathTests { }

[Trait("Category", "Integration")]
public class FullGameLoopTests { }

[Trait("Category", "Performance")]
public class BenchmarkSanityTests { }
```

```bash
dotnet test --filter "Category=Unit"          # Fast feedback loop
dotnet test --filter "Category=Integration"   # CI pipeline
```

### What to Test — Decision Guide

| System | Test? | Why |
|--------|-------|-----|
| Damage formulas | ✅ Always | Pure math, bugs cause balance issues |
| State machine transitions | ✅ Always | Logic bugs cause AI glitches |
| Inventory add/remove/stack | ✅ Always | Edge cases everywhere |
| Serialization round-trip | ✅ Always | Data loss is catastrophic |
| Pathfinding results | ✅ Always | Wrong path = broken game |
| Collision detection math | ✅ Always | Off-by-one = objects pass through walls |
| ECS system queries | ✅ Worth it | Verifies component filters |
| Input → action mapping | ⚠️ Selectively | Through mock input services |
| Rendering output | ❌ Skip | Visual verification only |
| "Fun" / game feel | ❌ Skip | Subjective, needs playtesting |
| Audio timing | ❌ Skip | Needs runtime environment |

### CI Integration

```yaml
# .github/workflows/tests.yml
name: Tests
on: [push, pull_request]
jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v6
      - uses: actions/setup-dotnet@v6
        with:
          dotnet-version: '8.0.x'
      - run: dotnet test --configuration Release --logger "trx" --results-directory TestResults
      - uses: actions/upload-artifact@v4
        if: always()
        with:
          name: test-results
          path: TestResults/
```

---

## Quick Reference

```
dotnet new xunit -n MyGame.Tests          # Create test project
dotnet add reference ../MyGame/MyGame.csproj  # Reference game project
dotnet test                                # Run all
dotnet test --filter "FullyQualifiedName~Combat"  # Filter
dotnet test --verbosity detailed          # See output
dotnet watch test                          # Watch mode
```

**Core pattern:** Create a `World`, add entities with known components, run one system update, assert component values changed as expected. Mock services via interfaces. Keep game logic pure and framework-free.
