using System.Diagnostics;
using Arch.Core;
using Arch.Systems;
using Game.Engine.ECS.Systems;

namespace Game.Engine.ECS;

/// <summary>
///     Batched render signal for the snake scene. Emitted once per grid step (8 Hz),
///     carrying the full set of cell-aligned sprites plus score/game-over flags.
///     <see cref="Ate"/>, <see cref="FoodSpawned"/> and <see cref="FoodFalling"/> are
///     ECS-originated edge events consumed once: the client reacts (eat sound,
///     food-spawn sound, start of the presentation-physics food drop).
/// </summary>
public sealed record SnakeRenderSignal(
    long Seq,
    int EntityCount,
    double TickMs,
    IReadOnlyList<SpriteState> Sprites,
    int Score,
    bool GameOver,
    bool Started,
    bool Ate,
    bool FoodSpawned,
    bool FoodFalling);

/// <summary>
///     Owns the snake Arch ECS world. The sim ticks at 60 Hz, advances the grid at
///     8 Hz (see <see cref="StepIntervalSeconds"/>) and emits one batched
///     <see cref="SnakeRenderSignal"/> per step. C# is the sole authority: input is
///     queued from the client but validated and applied by <see cref="SnakeInputSystem"/>.
///     The food drop is a presentation-physics integration test: after
///     <see cref="FoodFallDelaySeconds"/> the ECS flags the food as falling (black,
///     deadly) and the client runs a Rapier gravity fall; the ECS only records the
///     initial position and, after the one-shot <see cref="FoodDropped"/> report, the
///     final position.
/// </summary>
public sealed class SnakeSimulation : IDisposable
{
    public const int GridWidth = 40;
    public const int GridHeight = 30;
    public const float CellSize = 20f;
    public const int InitialLength = 6;
    public const int FoodRenderId = 1000;
    public const int MaxBufferedInput = 3;
    public const double FoodFallDelaySeconds = 3.0;
    public const double FoodDropTimeoutSeconds = 5.0;
    private const double TickIntervalSeconds = 1.0 / 60.0;
    private const double StepIntervalSeconds = 1.0 / 8.0;

    private static readonly SpriteColor BodyColor = new(22, 101, 52);
    private static readonly SpriteColor HeadColor = new(34, 197, 94);
    private static readonly SpriteColor FoodColor = new(34, 211, 238);
    private static readonly SpriteColor BlackFoodColor = new(0, 0, 0);

    // Foods get monotonically increasing render ids so the client can tell a
    // freshly falling food apart from already-settled black foods.
    private static int _foodIdCounter = FoodRenderId;

    /// <summary>Next unique render id for a spawned food entity.</summary>
    public static int NextFoodId() => Interlocked.Increment(ref _foodIdCounter);

    private readonly World _world;
    private readonly Group<double> _systems;
    private readonly Timer _timer;
    private readonly Random _random = new();
    private readonly Queue<SnakeDir> _pendingInput = new();

    // Guards the world (step mutation + snapshot reads across timer and request threads).
    private readonly object _sync = new();
    private double _stepAccumulator;
    private long _seq;

    public event Action<SnakeRenderSignal>? OnRenderSignal;

    public SnakeSimulation()
    {
        _world = World.Create();
        _systems = new Group<double>(
            "Snake",
            new SnakeInputSystem(_world, _pendingInput),
            new SnakeStepSystem(_world, GridWidth, GridHeight, InitialLength,
                BodyColor, HeadColor, FoodColor, _random)
        );
        _systems.Initialize();
        SeedWorld();

        _timer = new Timer(Tick, null, TimeSpan.Zero, TimeSpan.FromSeconds(TickIntervalSeconds));
    }

    public int Score
    {
        get
        {
            lock (_sync)
            {
                var statsEntity = FindStatsEntity();
                return statsEntity == Entity.Null ? 0 : _world.Get<SnakeStats>(statsEntity).Score;
            }
        }
    }

    public bool IsGameOver
    {
        get
        {
            lock (_sync)
            {
                var statsEntity = FindStatsEntity();
                return statsEntity != Entity.Null && _world.Get<SnakeStats>(statsEntity).GameOver;
            }
        }
    }

    public bool IsStarted
    {
        get
        {
            lock (_sync)
            {
                var statsEntity = FindStatsEntity();
                return statsEntity != Entity.Null && _world.Get<SnakeStats>(statsEntity).Started;
            }
        }
    }

    /// <summary>Starts a fresh game. If the previous run is over, the world is reset first.</summary>
    public void Start()
    {
        lock (_sync)
        {
            var statsEntity = FindStatsEntity();
            var stats = statsEntity != Entity.Null
                ? _world.Get<SnakeStats>(statsEntity)
                : new SnakeStats(0, false);
            if (stats.GameOver)
            {
                ResetWorld();
                statsEntity = FindStatsEntity();
            }
            _world.Set(statsEntity, new SnakeStats(0, false, started: true));
        }
    }

    /// <summary>Current world snapshot for the initial SSR payload (game visible before first SSE tick).</summary>
    public IReadOnlyList<SpriteState> Snapshot()
    {
        lock (_sync)
        {
            return BuildSnapshot();
        }
    }

    /// <summary>Client-suggested direction. Validated and applied by the input system on the next step.</summary>
    public void QueueDirection(string direction)
    {
        if (!TryParseDirection(direction, out var dir)) return;
        lock (_sync)
        {
            if (_pendingInput.Count < MaxBufferedInput)
            {
                _pendingInput.Enqueue(dir);
            }
        }
    }

    /// <summary>Clears the world and restarts a fresh game (called on Space/Enter after game over).</summary>
    public void Reset()
    {
        lock (_sync)
        {
            ResetWorld();
        }
    }

    private void ResetWorld()
    {
        var entities = new Entity[_world.Size];
        _world.GetEntities(new QueryDescription(), entities.AsSpan());
        foreach (var entity in entities)
        {
            if (_world.IsAlive(entity)) _world.Destroy(entity);
        }
        _pendingInput.Clear();
        SeedWorld();
    }

    private void SeedWorld()
    {
        var startX = GridWidth / 2;
        var startY = GridHeight / 2;

        // Body extends to the right of the head; the snake starts moving left.
        for (var i = 1; i <= InitialLength - 1; i++)
        {
            _world.Create(
                new RenderId(i),
                new GridCell(startX + i, startY),
                BodyColor,
                new SnakeBody());
        }
        _world.Create(
            new RenderId(0),
            new GridCell(startX, startY),
            HeadColor,
            new SnakeDirection(SnakeDir.Left),
            new SnakeHead());
        _world.Create(new SnakeStats(0, false));
        RespawnFood();
    }

    private void RespawnFood()
    {
        var occupied = new HashSet<long>();
        var entities = new Entity[_world.Size];
        _world.GetEntities(new QueryDescription(), entities.AsSpan());
        foreach (var entity in entities)
        {
            if (!_world.IsAlive(entity) || !_world.Has<GridCell>(entity)) continue;
            var cell = _world.Get<GridCell>(entity);
            occupied.Add((cell.Y * (long)GridWidth) + cell.X);
        }

        for (var attempt = 0; attempt < 100; attempt++)
        {
            var cell = new GridCell(_random.Next(GridWidth), _random.Next(GridHeight));
            if (occupied.Contains((cell.Y * (long)GridWidth) + cell.X)) continue;
            _world.Create(
                new RenderId(NextFoodId()),
                cell,
                FoodColor,
                new FoodAge(0f),
                new SnakeFood());
            return;
        }
    }

    /// <summary>
    ///     One-shot final-position report from the presentation physics (Rapier drop).
    ///     The ECS records the final food cell; afterwards no more drop events of this
    ///     type are accepted for the current food. Black food stays black forever as a
    ///     permanent obstacle, and a fresh normal food spawns so play continues.
    /// </summary>
    public void FoodDropped(int x, int y)
    {
        lock (_sync)
        {
            var food = FindFallingFood();
            if (food == Entity.Null) return;

            var cell = ClampToGrid(new GridCell(x, y));
            if (CellOccupiedBySnake(cell))
            {
                cell = FindFreeBottomCell(cell.X);
            }
            _world.Set(food, cell);
            _world.Add<FoodSynced>(food);
            SpawnNormalFood();
        }
    }

    /// <summary>Spawns a new normal (cyan) food on a free cell and flags the spawn event.</summary>
    private void SpawnNormalFood()
    {
        var occupied = new HashSet<long>();
        var entities = new Entity[_world.Size];
        _world.GetEntities(new QueryDescription(), entities.AsSpan());
        foreach (var entity in entities)
        {
            if (!_world.IsAlive(entity) || !_world.Has<GridCell>(entity)) continue;
            var cell = _world.Get<GridCell>(entity);
            occupied.Add((cell.Y * (long)GridWidth) + cell.X);
        }

        for (var attempt = 0; attempt < 100; attempt++)
        {
            var cell = new GridCell(_random.Next(GridWidth), _random.Next(GridHeight));
            if (occupied.Contains((cell.Y * (long)GridWidth) + cell.X)) continue;
            _world.Create(
                new RenderId(NextFoodId()),
                cell,
                FoodColor,
                new FoodAge(0f),
                new SnakeFood());
            var statsEntity = FindStatsEntity();
            if (statsEntity != Entity.Null)
            {
                var stats = _world.Get<SnakeStats>(statsEntity);
                _world.Set(statsEntity, new SnakeStats(stats.Score, stats.GameOver, stats.Started, stats.Ate, foodSpawned: true));
            }
            return;
        }
    }

    private Entity FindFallingFood()
    {
        var entities = new Entity[_world.Size];
        _world.GetEntities(new QueryDescription(), entities.AsSpan());
        foreach (var entity in entities)
        {
            if (!_world.IsAlive(entity) || !_world.Has<SnakeFood>(entity)) continue;
            if (_world.Has<FoodFall>(entity) && !_world.Has<FoodSynced>(entity)) return entity;
        }
        return Entity.Null;
    }

    private GridCell ClampToGrid(GridCell cell) =>
        new(Math.Clamp(cell.X, 0, GridWidth - 1), Math.Clamp(cell.Y, 0, GridHeight - 1));

    private bool CellOccupiedBySnake(GridCell cell)
    {
        var entities = new Entity[_world.Size];
        _world.GetEntities(new QueryDescription(), entities.AsSpan());
        foreach (var entity in entities)
        {
            if (!_world.IsAlive(entity) || !_world.Has<GridCell>(entity)) continue;
            if (_world.Has<SnakeFood>(entity)) continue;
            if (_world.Get<GridCell>(entity).X == cell.X && _world.Get<GridCell>(entity).Y == cell.Y) return true;
        }
        return false;
    }

    private GridCell FindFreeBottomCell(int preferredX)
    {
        for (var radius = 0; radius < GridWidth; radius++)
        {
            foreach (var x in new[] { preferredX - radius, preferredX + radius })
            {
                if (x < 0 || x >= GridWidth) continue;
                var cell = new GridCell(x, GridHeight - 1);
                if (!CellOccupiedBySnake(cell)) return cell;
            }
        }
        return new GridCell(preferredX, GridHeight - 1);
    }

    private void Tick(object? _)
    {
        lock (_sync)
        {
            // Paused after game over: no stepping and no further signals until Start().
            // Otherwise the client would receive an endless stream of identical
            // game-over events and replay the end sound forever.
            var statsEntity = FindStatsEntity();
            var statsBeforeStep = statsEntity == Entity.Null
                ? new SnakeStats(0, false)
                : _world.Get<SnakeStats>(statsEntity);
            if (statsBeforeStep.GameOver) return;

            _stepAccumulator += TickIntervalSeconds;
            if (_stepAccumulator < StepIntervalSeconds) return;
            _stepAccumulator -= StepIntervalSeconds;

            var stopwatch = Stopwatch.StartNew();
            var dt = TickIntervalSeconds;
            _systems.BeforeUpdate(in dt);
            _systems.Update(in dt);
            _systems.AfterUpdate(in dt);

            var foodFalling = AdvanceFoodState(statsEntity, statsBeforeStep);
            stopwatch.Stop();

            _seq++;
            var stats = statsEntity == Entity.Null
                ? new SnakeStats(0, false)
                : _world.Get<SnakeStats>(statsEntity);
            var ate = stats.Ate;
            var foodSpawned = stats.FoodSpawned;
            if (ate || foodSpawned || foodFalling)
            {
                _world.Set(statsEntity, new SnakeStats(stats.Score, stats.GameOver, stats.Started));
            }
            OnRenderSignal?.Invoke(new SnakeRenderSignal(
                _seq, _world.Size, stopwatch.Elapsed.TotalMilliseconds,
                BuildSnapshot(), stats.Score, stats.GameOver, stats.Started, ate, foodSpawned, foodFalling));
        }
    }

    /// <summary>
    ///     Ages the food entity and drives the drop state machine: after
    ///     <see cref="FoodFallDelaySeconds"/> the food turns black and is flagged
    ///     falling (deadly, presentation physics takes over); after
    ///     <see cref="FoodDropTimeoutSeconds"/> without a client drop report the ECS
    ///     force-syncs the final position itself (authority fallback).
    ///     Returns true exactly once when the fall starts (edge event for the client).
    /// </summary>
    private bool AdvanceFoodState(Entity statsEntity, SnakeStats stats)
    {
        var food = FindFoodEntity();
        if (food == Entity.Null) return false;

        var age = _world.Get<FoodAge>(food);
        age.Seconds += (float)StepIntervalSeconds;
        _world.Set(food, age);

        if (!stats.Started || stats.GameOver) return false;

        var isFalling = _world.Has<FoodFall>(food);
        var isSynced = _world.Has<FoodSynced>(food);

        if (!isFalling && age.Seconds >= FoodFallDelaySeconds)
        {
            _world.Add<FoodFall>(food);
            _world.Set(food, BlackFoodColor);
            return true;
        }

        if (isFalling && !isSynced && age.Seconds >= FoodFallDelaySeconds + FoodDropTimeoutSeconds)
        {
            // No drop report arrived: force the final position (random free bottom
            // cell) and spawn a fresh normal food so the game keeps going.
            var cell = FindFreeBottomCell(_random.Next(GridWidth));
            _world.Set(food, cell);
            _world.Add<FoodSynced>(food);
            SpawnNormalFood();
        }
        return false;
    }

    /// <summary>Finds the currently playable (normal, non-falling) food entity.</summary>
    private Entity FindFoodEntity()
    {
        var entities = new Entity[_world.Size];
        _world.GetEntities(new QueryDescription(), entities.AsSpan());
        foreach (var entity in entities)
        {
            if (!_world.IsAlive(entity) || !_world.Has<SnakeFood>(entity)) continue;
            if (!_world.Has<FoodFall>(entity)) return entity;
        }
        return Entity.Null;
    }

    private Entity FindStatsEntity()
    {
        var entities = new Entity[_world.Size];
        _world.GetEntities(new QueryDescription(), entities.AsSpan());
        foreach (var entity in entities)
        {
            if (_world.IsAlive(entity) && _world.Has<SnakeStats>(entity)) return entity;
        }
        return Entity.Null;
    }

    /// <summary>
    ///     Builds the render snapshot. Order encodes draw order on the client:
    ///     food first, then body segments (ascending id), head last (on top).
    /// </summary>
    private IReadOnlyList<SpriteState> BuildSnapshot()
    {
        var entities = new Entity[_world.Size];
        _world.GetEntities(new QueryDescription(), entities.AsSpan());

        var states = new List<SpriteState>(entities.Length);
        foreach (var entity in entities)
        {
            if (!_world.IsAlive(entity) || !_world.Has<SnakeFood>(entity)) continue;
            states.Add(ToState(entity));
        }

        var body = new List<(int Id, Entity Entity)>();
        foreach (var entity in entities)
        {
            if (!_world.IsAlive(entity) || !_world.Has<SnakeBody>(entity)) continue;
            body.Add((_world.Get<RenderId>(entity).Id, entity));
        }
        body.Sort((a, b) => a.Id.CompareTo(b.Id));
        foreach (var (_, entity) in body)
        {
            states.Add(ToState(entity));
        }

        foreach (var entity in entities)
        {
            if (!_world.IsAlive(entity) || !_world.Has<SnakeHead>(entity)) continue;
            states.Add(ToState(entity));
        }
        return states;
    }

    private SpriteState ToState(Entity entity)
    {
        var cell = _world.Get<GridCell>(entity);
        var color = _world.Get<SpriteColor>(entity);
        return new SpriteState(
            _world.Get<RenderId>(entity).Id,
            (cell.X + 0.5f) * CellSize,
            (cell.Y + 0.5f) * CellSize,
            color.R, color.G, color.B);
    }

    private static bool TryParseDirection(string? value, out SnakeDir dir)
    {
        switch (value?.ToLowerInvariant())
        {
            case "up": dir = SnakeDir.Up; return true;
            case "down": dir = SnakeDir.Down; return true;
            case "left": dir = SnakeDir.Left; return true;
            case "right": dir = SnakeDir.Right; return true;
            default: dir = default; return false;
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
        _systems.Dispose();
        World.Destroy(_world);
    }
}
