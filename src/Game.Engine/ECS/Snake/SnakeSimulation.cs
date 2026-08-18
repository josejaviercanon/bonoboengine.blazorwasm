using System.Diagnostics;
using Arch.Core;
using Arch.Systems;
using Game.Engine.ECS.Systems;

namespace Game.Engine.ECS.Snake;

/// <summary>
///     Batched render signal for the snake scene. Emitted once per grid step (8 Hz),
///     carrying the full set of sprites plus temporal positions and score/game-over flags.
///     <see cref="Ate"/>, <see cref="FoodSpawned"/> and <see cref="FoodFalling"/> are
///     ECS-originated edge events consumed once: the client reacts (eat sound,
///     food-spawn sound, start of the deadly-food fall).
/// </summary>
public readonly record struct SnakeSpriteState(
    int Id,
    float X,
    float Y,
    float PreviousX,
    float PreviousY,
    float VelocityX,
    float VelocityY,
    SnakeSpriteKind Kind,
    byte R,
    byte G,
    byte B);

public sealed partial record SnakeRenderSignal(
    long Seq,
    int EntityCount,
    double TickMs,
    double StepMs,
    IReadOnlyList<SnakeSpriteState> Sprites,
    int Score,
    bool GameOver,
    bool Started,
    bool Ate,
    bool FoodSpawned,
    bool FoodFalling);

public partial record SnakeRenderSignal
{
    public long Epoch { get; init; }
}

/// <summary>
///     Owns the snake Arch ECS world. The sim ticks at 60 Hz, advances the grid at
///     8 Hz (see <see cref="StepIntervalSeconds"/>) and emits one batched
///     <see cref="SnakeRenderSignal"/> per step. C# is the sole authority: input is
///     queued from the client but validated and applied by <see cref="SnakeInputSystem"/>,
///     and deadly-food movement is simulated here so collision and rendering cannot drift.
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
    // Four 8 Hz steps: ten times faster than the old five-second drop timeout.
    public const double FoodFallDurationSeconds = 0.5;
    private const double TickIntervalSeconds = 1.0 / 60.0;
    private const double StepIntervalSeconds = 1.0 / 8.0;

    private static readonly SpriteColor BodyColor = new(22, 101, 52);
    private static readonly SpriteColor HeadColor = new(34, 197, 94);
    private static readonly SpriteColor FoodColor = new(34, 211, 238);
    private static readonly SpriteColor BadFoodColor = new(239, 68, 68);

    // Foods get monotonically increasing render ids. Instance-based so parallel test
    // simulations never share id state.
    private int _foodIdCounter = FoodRenderId;

    /// <summary>Next unique render id for a spawned food entity (per simulation).</summary>
    public int NextFoodId() => _foodIdCounter++;

    private readonly World _world;
    private readonly Group<double> _systems;
    private readonly Timer? _timer;
    private readonly Random _random;
    private readonly Queue<SnakeDir> _pendingInput = new();

    // Guards the world (step mutation + snapshot reads across timer and request threads).
    private readonly object _sync = new();
    private double _stepAccumulator;
    private long _seq;
    private long _epoch;

    public event Action<SnakeRenderSignal>? OnRenderSignal;

    public SnakeSimulation(int? seed = null, bool startTimer = true)
    {
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
        _world = World.Create();
        _systems = new Group<double>(
            "Snake",
            new SnakeInputSystem(_world, _pendingInput),
            new SnakeStepSystem(_world, GridWidth, GridHeight, InitialLength,
                BodyColor, HeadColor, FoodColor, _random, NextFoodId)
        );
        _systems.Initialize();
        SeedWorld();

        if (startTimer)
        {
            _timer = new Timer(Tick, null, TimeSpan.Zero, TimeSpan.FromSeconds(TickIntervalSeconds));
        }
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
    public IReadOnlyList<SnakeSpriteState> Snapshot()
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

    /// <summary>Runs one fixed 60 Hz tick. Used by deterministic tests.</summary>
    public void StepOnce()
    {
        lock (_sync)
        {
            TickCore();
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
        _foodIdCounter = FoodRenderId;
        _stepAccumulator = 0;
        _seq = 0;
        _epoch++;
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
                new PreviousGridCell(startX + i, startY),
                BodyColor,
                new SnakeBody());
        }
        _world.Create(
            new RenderId(0),
            new GridCell(startX, startY),
            new PreviousGridCell(startX, startY),
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
                new PreviousGridCell(cell.X, cell.Y),
                FoodColor,
                new FoodAge(0f),
                new FoodKind(SnakeSpriteKind.GoodFood),
                new SnakeFood());
            return;
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
                new PreviousGridCell(cell.X, cell.Y),
                FoodColor,
                new FoodAge(0f),
                new FoodKind(SnakeSpriteKind.GoodFood),
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

    private void Tick(object? _)
    {
        lock (_sync)
        {
            TickCore();
        }
    }

    private void TickCore()
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
        var foodFalling = AdvanceFoodState(statsBeforeStep);
        var dt = TickIntervalSeconds;
        _systems.BeforeUpdate(in dt);
        _systems.Update(in dt);
        _systems.AfterUpdate(in dt);
        stopwatch.Stop();

        _seq++;
        var stats = statsEntity == Entity.Null
            ? new SnakeStats(0, false)
            : _world.Get<SnakeStats>(statsEntity);
        var ate = stats.Ate;
        var foodSpawned = stats.FoodSpawned;
        if (statsEntity != Entity.Null && (ate || foodSpawned || foodFalling))
        {
            _world.Set(statsEntity, new SnakeStats(stats.Score, stats.GameOver, stats.Started));
        }
        OnRenderSignal?.Invoke(new SnakeRenderSignal(
            _seq, _world.Size, stopwatch.Elapsed.TotalMilliseconds, StepIntervalSeconds * 1000,
            BuildSnapshot(), stats.Score, stats.GameOver, stats.Started, ate, foodSpawned, foodFalling)
        { Epoch = _epoch });
    }

    /// <summary>
    ///     Advances every food entity. Normal food ages until it becomes deadly red;
    ///     replacement food is spawned immediately. Deadly food then falls in the
    ///     authoritative ECS world and remains a settled obstacle at the bottom.
    ///     Returns true when at least one fall starts during this step.
    /// </summary>
    private bool AdvanceFoodState(SnakeStats stats)
    {
        if (!stats.Started || stats.GameOver) return false;

        var entities = new Entity[_world.Size];
        _world.GetEntities(new QueryDescription(), entities.AsSpan());
        var startedFall = false;
        foreach (var entity in entities)
        {
            if (!_world.IsAlive(entity) || !_world.Has<SnakeFood>(entity)) continue;

            if (_world.Has<FoodFall>(entity))
            {
                if (_world.Has<FoodSynced>(entity)) continue;

                var fall = _world.Get<FoodFall>(entity);
                fall.PreviousX = fall.X;
                fall.PreviousY = fall.Y;
                fall.ElapsedSeconds += (float)StepIntervalSeconds;
                var progress = Math.Clamp(fall.ElapsedSeconds / fall.DurationSeconds, 0f, 1f);
                fall.X = fall.StartX;
                fall.Y = fall.StartY + fall.VelocityY * fall.ElapsedSeconds;

                var currentY = Math.Clamp((int)MathF.Floor(fall.Y / CellSize), 0, GridHeight - 1);
                _world.Set(entity, new GridCell(fall.LandingX, currentY));

                if (progress >= 1f)
                {
                    fall.X = (fall.LandingX + 0.5f) * CellSize;
                    fall.Y = (fall.LandingY + 0.5f) * CellSize;
                    fall.PreviousX = fall.X;
                    fall.PreviousY = fall.Y;
                    fall.VelocityX = 0f;
                    fall.VelocityY = 0f;
                    _world.Set(entity, new GridCell(fall.LandingX, fall.LandingY));
                    _world.Add<FoodSynced>(entity);
                }

                _world.Set(entity, fall);
                continue;
            }

            if (!_world.Has<FoodAge>(entity)) continue;
            var age = _world.Get<FoodAge>(entity);
            age.Seconds += (float)StepIntervalSeconds;
            _world.Set(entity, age);
            if (age.Seconds < FoodFallDelaySeconds) continue;

            var cell = _world.Get<GridCell>(entity);
            _world.Add(entity, new FoodFall(
                (cell.X + 0.5f) * CellSize,
                (cell.Y + 0.5f) * CellSize,
                (float)FoodFallDurationSeconds,
                cell.X,
                GridHeight - 1,
                CellSize));
            _world.Set(entity, BadFoodColor);
            _world.Set(entity, new FoodKind(SnakeSpriteKind.BadFood));
            startedFall = true;

            // Bad food is now an obstacle, so replacement good food must not wait
            // for the old food to reach the bottom.
            SpawnNormalFood();
        }
        return startedFall;
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
    private IReadOnlyList<SnakeSpriteState> BuildSnapshot()
    {
        var entities = new Entity[_world.Size];
        _world.GetEntities(new QueryDescription(), entities.AsSpan());

        var states = new List<SnakeSpriteState>(entities.Length);
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

    private SnakeSpriteState ToState(Entity entity)
    {
        var color = _world.Get<SpriteColor>(entity);
        var kind = _world.Has<SnakeFood>(entity)
            ? _world.Get<FoodKind>(entity).Kind
            : _world.Has<SnakeHead>(entity) ? SnakeSpriteKind.Head : SnakeSpriteKind.Body;

        float x;
        float y;
        float previousX;
        float previousY;
        float velocityX;
        float velocityY;

        if (_world.Has<FoodFall>(entity))
        {
            var fall = _world.Get<FoodFall>(entity);
            x = fall.X;
            y = fall.Y;
            previousX = fall.PreviousX;
            previousY = fall.PreviousY;
            velocityX = fall.VelocityX;
            velocityY = fall.VelocityY;
            if (_world.Has<FoodSynced>(entity))
            {
                previousX = x;
                previousY = y;
                velocityX = 0f;
                velocityY = 0f;
            }
        }
        else
        {
            var cell = _world.Get<GridCell>(entity);
            var previous = _world.Has<PreviousGridCell>(entity)
                ? _world.Get<PreviousGridCell>(entity)
                : new PreviousGridCell(cell.X, cell.Y);
            x = (cell.X + 0.5f) * CellSize;
            y = (cell.Y + 0.5f) * CellSize;
            previousX = (previous.X + 0.5f) * CellSize;
            previousY = (previous.Y + 0.5f) * CellSize;
            velocityX = (x - previousX) / (float)StepIntervalSeconds;
            velocityY = (y - previousY) / (float)StepIntervalSeconds;
        }

        return new SnakeSpriteState(
            _world.Get<RenderId>(entity).Id,
            x,
            y,
            previousX,
            previousY,
            velocityX,
            velocityY,
            kind,
            color.R,
            color.G,
            color.B);
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
        _timer?.Dispose();
        _systems.Dispose();
        World.Destroy(_world);
    }
}
