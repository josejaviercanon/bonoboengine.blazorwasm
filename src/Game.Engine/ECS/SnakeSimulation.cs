using System.Diagnostics;
using Arch.Core;
using Arch.Systems;
using Game.Engine.ECS.Systems;

namespace Game.Engine.ECS;

/// <summary>
///     Batched render signal for the snake scene. Emitted once per grid step (8 Hz),
///     carrying the full set of cell-aligned sprites plus score/game-over flags.
///     <see cref="Ate"/> and <see cref="FoodSpawned"/> are ECS-originated events:
///     the step system sets them and the client reacts (eat sound, food-spawn sound).
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
    bool FoodSpawned);

/// <summary>
///     Owns the snake Arch ECS world. The sim ticks at 60 Hz, advances the grid at
///     8 Hz (see <see cref="StepIntervalSeconds"/>) and emits one batched
///     <see cref="SnakeRenderSignal"/> per step. C# is the sole authority: input is
///     queued from the client but validated and applied by <see cref="SnakeInputSystem"/>.
/// </summary>
public sealed class SnakeSimulation : IDisposable
{
    public const int GridWidth = 40;
    public const int GridHeight = 30;
    public const float CellSize = 20f;
    public const int InitialLength = 6;
    public const int FoodRenderId = 1000;
    public const int MaxBufferedInput = 3;
    private const double TickIntervalSeconds = 1.0 / 60.0;
    private const double StepIntervalSeconds = 1.0 / 8.0;

    private static readonly SpriteColor BodyColor = new(22, 101, 52);
    private static readonly SpriteColor HeadColor = new(34, 197, 94);
    private static readonly SpriteColor FoodColor = new(34, 211, 238);

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
                new RenderId(FoodRenderId),
                cell,
                FoodColor,
                new SnakeFood());
            return;
        }
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
            stopwatch.Stop();

            _seq++;
            var stats = statsEntity == Entity.Null
                ? new SnakeStats(0, false)
                : _world.Get<SnakeStats>(statsEntity);
            var ate = stats.Ate;
            var foodSpawned = stats.FoodSpawned;
            if (ate || foodSpawned)
            {
                _world.Set(statsEntity, new SnakeStats(stats.Score, stats.GameOver, stats.Started));
            }
            OnRenderSignal?.Invoke(new SnakeRenderSignal(
                _seq, _world.Size, stopwatch.Elapsed.TotalMilliseconds,
                BuildSnapshot(), stats.Score, stats.GameOver, stats.Started, ate, foodSpawned));
        }
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
