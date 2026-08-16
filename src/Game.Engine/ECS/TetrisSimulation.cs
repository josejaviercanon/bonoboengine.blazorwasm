using System.Diagnostics;
using Arch.Core;
using Arch.Systems;
using Game.Engine.ECS.Systems;

namespace Game.Engine.ECS;

/// <summary>
///     Batched render signal for the tetris scene. Emitted once per board mutation
///     (input applied, gravity drop, lock, line clear), carrying the full cell-aligned
///     sprite list plus score/rows/level/game-over flags. <see cref="Locked"/> and
///     <see cref="LinesCleared"/> are ECS-originated edge events consumed once by the
///     client (lock + clear sounds/feedback).
/// </summary>
public sealed record TetrisRenderSignal(
    long Seq,
    int EntityCount,
    double TickMs,
    IReadOnlyList<SpriteState> Sprites,
    int Score,
    int Rows,
    int Level,
    bool GameOver,
    bool Started,
    bool Locked,
    int LinesCleared);

/// <summary>
///     Owns the tetris Arch ECS world. The sim ticks at 60 Hz; the gravity system
///     advances the piece at the rows-based reference rate and emits one batched
///     <see cref="TetrisRenderSignal"/> per dirty batch. C# is the sole authority:
///     input is queued from the client, validated and applied by
///     <see cref="TetrisInputSystem"/>; the grid is a set of <see cref="TetrisBlock"/>
///     entities and line clearing is ECS entity destruction + cell shift.
/// </summary>
public sealed class TetrisSimulation : IDisposable
{
    public const int GridWidth = 10;
    public const int GridHeight = 20;
    public const float CellSize = 30f;
    public const int BlockRenderId = 1000;
    public const int MaxBufferedInput = 16;
    private const double TickIntervalSeconds = 1.0 / 60.0;

    // Settled blocks get monotonically increasing render ids so the client can tell
    // a fresh lock apart from already-settled blocks.
    private static int _blockIdCounter = BlockRenderId;

    /// <summary>Next unique render id for a settled block entity.</summary>
    public static int NextBlockId() => Interlocked.Increment(ref _blockIdCounter);

    private readonly World _world;
    private readonly Group<double> _systems;
    private readonly Timer _timer;
    private readonly Random _random;
    private readonly Queue<TetrisCommand> _pendingInput = new();
    private readonly TetrisInputSystem _inputSystem;
    private readonly TetrisGravitySystem _gravitySystem;

    // Guards the world (step mutation + snapshot reads across timer and request threads).
    private readonly object _sync = new();
    private long _seq;

    public event Action<TetrisRenderSignal>? OnRenderSignal;

    public TetrisSimulation() : this(new Random())
    {
    }

    /// <param name="seed">Seeded for deterministic tests.</param>
    public TetrisSimulation(int seed) : this(new Random(seed))
    {
    }

    /// <param name="random">Inject a <see cref="Random"/> for full control over the piece bag.</param>
    public TetrisSimulation(Random random)
    {
        _random = random;
        _world = World.Create();
        _gravitySystem = new TetrisGravitySystem(_world, GridWidth, GridHeight, _random, _pendingInput);
        _inputSystem = new TetrisInputSystem(_world, _pendingInput, _gravitySystem, GridWidth, GridHeight);
        _systems = new Group<double>("Tetris", _inputSystem, _gravitySystem);
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
                return statsEntity == Entity.Null ? 0 : _world.Get<TetrisStats>(statsEntity).Score;
            }
        }
    }

    public int Rows
    {
        get
        {
            lock (_sync)
            {
                var statsEntity = FindStatsEntity();
                return statsEntity == Entity.Null ? 0 : _world.Get<TetrisStats>(statsEntity).Rows;
            }
        }
    }

    public int Level
    {
        get
        {
            lock (_sync)
            {
                var statsEntity = FindStatsEntity();
                return statsEntity == Entity.Null ? 1 : _world.Get<TetrisStats>(statsEntity).Level;
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
                return statsEntity != Entity.Null && _world.Get<TetrisStats>(statsEntity).GameOver;
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
                return statsEntity != Entity.Null && _world.Get<TetrisStats>(statsEntity).Started;
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
                ? _world.Get<TetrisStats>(statsEntity)
                : new TetrisStats(0, 0, 1, false);
            if (stats.GameOver)
            {
                ResetWorld();
                statsEntity = FindStatsEntity();
            }
            _world.Set(statsEntity, new TetrisStats(0, 0, 1, false, started: true));
        }
    }

    /// <summary>Current world snapshot for the initial SSR payload (board visible before first SSE tick).</summary>
    public IReadOnlyList<SpriteState> Snapshot()
    {
        lock (_sync)
        {
            return BuildSnapshot();
        }
    }

    /// <summary>Client-suggested command. Validated and applied by the input system.</summary>
    public void QueueInput(string command)
    {
        if (!TryParseCommand(command, out var cmd)) return;
        lock (_sync)
        {
            if (_pendingInput.Count < MaxBufferedInput)
            {
                _pendingInput.Enqueue(cmd);
            }
        }
    }

    /// <summary>Clears the world and restarts a fresh game.</summary>
    public void Reset()
    {
        lock (_sync)
        {
            ResetWorld();
        }
    }

    /// <summary>
    ///     Deterministic test hook: parses and synchronously applies one command via the
    ///     input system. Returns false for unknown commands (the authority drops them).
    /// </summary>
    public bool ApplyInput(string command)
    {
        if (!TryParseCommand(command, out var cmd)) return false;
        lock (_sync)
        {
            _pendingInput.Enqueue(cmd);
            _inputSystem.Update(0);
            return true;
        }
    }

    /// <summary>Deterministic test hook: one gravity drop attempt (move down or lock).</summary>
    public void StepOnce()
    {
        lock (_sync)
        {
            _gravitySystem.SoftDrop();
        }
    }

    /// <summary>Test hook: places a settled block directly (board construction for line-clear tests).</summary>
    internal void PlaceBlock(int x, int y, TetrominoType type)
    {
        lock (_sync)
        {
            _world.Create(
                new RenderId(NextBlockId()),
                new GridCell(x, y),
                Tetromino.Color(type),
                new TetrisBlock());
        }
    }

    /// <summary>Test hook: runs the board's line-removal pass and returns the cleared row count.</summary>
    internal int ClearLinesForTest()
    {
        lock (_sync)
        {
            return _gravitySystem.RemoveLines();
        }
    }

    /// <summary>Test hook: forces a spawn + top-out check (occupied spawn cells must flag game over).</summary>
    internal void ForceSpawnNext()
    {
        lock (_sync)
        {
            _gravitySystem.SpawnNext();
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
        _world.Create(new TetrisStats(0, 0, 1, false));
        _gravitySystem.SpawnNext();
    }

    private void Tick(object? _)
    {
        lock (_sync)
        {
            // Paused after game over: no stepping and no further signals until Start().
            var statsEntity = FindStatsEntity();
            if (statsEntity == Entity.Null) return;
            if (_world.Get<TetrisStats>(statsEntity).GameOver) return;

            var stopwatch = Stopwatch.StartNew();
            var dt = TickIntervalSeconds;
            _systems.BeforeUpdate(in dt);
            _systems.Update(in dt);
            _systems.AfterUpdate(in dt);
            stopwatch.Stop();

            var stats = _world.Get<TetrisStats>(statsEntity);
            if (!stats.Dirty) return;

            // Consume the dirty + edge-event flags; the next signal starts clean.
            _world.Set(statsEntity, new TetrisStats(stats.Score, stats.Rows, stats.Level, stats.GameOver, stats.Started));

            _seq++;
            OnRenderSignal?.Invoke(new TetrisRenderSignal(
                _seq, _world.Size, stopwatch.Elapsed.TotalMilliseconds,
                BuildSnapshot(), stats.Score, stats.Rows, stats.Level, stats.GameOver, stats.Started,
                stats.Locked, stats.LinesCleared));
        }
    }

    private Entity FindStatsEntity()
    {
        var entities = new Entity[_world.Size];
        _world.GetEntities(new QueryDescription(), entities.AsSpan());
        foreach (var entity in entities)
        {
            if (_world.IsAlive(entity) && _world.Has<TetrisStats>(entity)) return entity;
        }
        return Entity.Null;
    }

    /// <summary>
    ///     Builds the render snapshot. Order encodes draw order on the client:
    ///     settled blocks first, active piece cells last (on top).
    /// </summary>
    private IReadOnlyList<SpriteState> BuildSnapshot()
    {
        var entities = new Entity[_world.Size];
        _world.GetEntities(new QueryDescription(), entities.AsSpan());

        var states = new List<SpriteState>(entities.Length);

        foreach (var entity in entities)
        {
            if (!_world.IsAlive(entity) || !_world.Has<TetrisBlock>(entity)) continue;
            var cell = _world.Get<GridCell>(entity);
            var color = _world.Get<SpriteColor>(entity);
            states.Add(ToState(_world.Get<RenderId>(entity).Id, cell, color));
        }

        var piece = TetrisGrid.FindPiece(_world);
        if (piece != Entity.Null)
        {
            var cell = _world.Get<GridCell>(piece);
            var tp = _world.Get<TetrisPiece>(piece);
            var type = (TetrominoType)tp.Type;
            var color = Tetromino.Color(type);
            var id = 0;
            foreach (var (x, y) in TetrisGrid.Cells(type, cell.X, cell.Y, tp.Rotation))
            {
                states.Add(ToState(id++, new GridCell(x, y), color));
            }
        }
        return states;
    }

    private static SpriteState ToState(int id, GridCell cell, SpriteColor color) =>
        new(id, (cell.X + 0.5f) * CellSize, (cell.Y + 0.5f) * CellSize, color.R, color.G, color.B);

    private static bool TryParseCommand(string? value, out TetrisCommand command)
    {
        switch (value?.ToLowerInvariant())
        {
            case "left": command = TetrisCommand.Left; return true;
            case "right": command = TetrisCommand.Right; return true;
            case "rotate": command = TetrisCommand.Rotate; return true;
            case "down": command = TetrisCommand.Down; return true;
            default: command = default; return false;
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
        _systems.Dispose();
        World.Destroy(_world);
    }
}