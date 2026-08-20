using System.Diagnostics;
using Arch.Core;
using Arch.Systems;
using Game.Engine.ECS;

namespace Game.Engine.ECS.Pacman;

/// <summary>Pacman sprite state crossing the batched C# to PixiJS bridge.</summary>
public readonly record struct PacmanSpriteState(
    int Id,
    float X,
    float Y,
    float PreviousX,
    float PreviousY,
    float VelocityX,
    float VelocityY,
    float Rotation,
    PacmanSpriteKind Kind,
    PacmanDirection Direction,
    PacmanGhostMode Mode,
    bool Visible,
    byte R,
    byte G,
    byte B);

/// <summary>One batched Pacman snapshot. Edge flags are consumed by the client once.</summary>
public sealed partial record PacmanRenderSignal(
    long Seq,
    int EntityCount,
    double TickMs,
    IReadOnlyList<PacmanSpriteState> Sprites,
    int Score,
    int Lives,
    int Level,
    int PelletsRemaining,
    bool GameOver,
    bool Started,
    bool Frightened,
    bool AtePellet,
    bool AtePowerPellet,
    bool GhostEaten,
    bool Died,
    bool LevelUp);

public partial record PacmanRenderSignal
{
    public double StepMs { get; init; } = PacmanConfig.TickIntervalSeconds * 1000d;
    public long Epoch { get; init; }
}

/// <summary>
/// Owns authoritative Pacman ECS state. Timer ticks are fixed at 60 Hz; client input
/// is queued and validated by ECS systems; render output is batched SSE-friendly data.
/// </summary>
public sealed class PacmanSimulation : IDisposable
{
    public const int MazeWidth = PacmanMaze.Width;
    public const int MazeHeight = PacmanMaze.Height;
    public const float CellSize = PacmanMaze.CellSize;
    public const float BoardWidth = PacmanMaze.BoardWidth;
    public const float BoardHeight = PacmanMaze.BoardHeight;
    public const int PlayerRenderId = 1;
    public const int FirstGhostRenderId = 10;
    public const int FirstPelletRenderId = 1000;

    private readonly World _world;
    private readonly Queue<PacmanDirection> _pendingInput = new();
    private readonly PacmanInputSystem _inputSystem;
    private readonly PacmanStepSystem _stepSystem;
    private readonly Group<float> _systems;
    private readonly Timer? _timer;
    private readonly object _sync = new();
    private long _seq;
    private long _epoch;

    public PacmanSimulation(int seed = 0, bool startTimer = true,
        IRenderTransport<PacmanRenderSignal>? renderTransport = null)
    {
        _world = World.Create();
        _renderTransport = renderTransport ?? new ServerRenderTransport<PacmanRenderSignal>();
        _inputSystem = new PacmanInputSystem(_world, _pendingInput);
        _stepSystem = new PacmanStepSystem(_world, new Random(seed));
        _systems = new Group<float>("Pacman", _inputSystem, _stepSystem);
        _systems.Initialize();
        SeedWorld();

        if (startTimer)
        {
            _timer = new Timer(Tick, null, TimeSpan.Zero,
                TimeSpan.FromSeconds(PacmanConfig.TickIntervalSeconds));
        }
    }

    private readonly IRenderTransport<PacmanRenderSignal> _renderTransport;

    /// <summary>
    ///     Batched render signal, delivered through the <see cref="IRenderTransport{TSignal}"/> seam
    ///     (ADR-007). Forwarding event keeps the SSE host subscription contract unchanged.
    /// </summary>
    public event Action<PacmanRenderSignal>? OnRenderSignal
    {
        add => _renderTransport.OnSignal += value;
        remove => _renderTransport.OnSignal -= value;
    }

    public int Score => ReadStats().Score;
    public int Lives => ReadStats().Lives;
    public int Level => ReadStats().Level;
    public int PelletsRemaining => ReadStats().PelletsRemaining;
    public bool IsStarted => ReadStats().Started;
    public bool IsGameOver => ReadStats().GameOver;
    public bool IsFrightened => ReadStats().Frightened;

    public IReadOnlyList<string> MazeRows => PacmanMaze.Rows;

    public IReadOnlyList<PacmanSpriteState> Snapshot()
    {
        lock (_sync)
        {
            return BuildSnapshot();
        }
    }

    /// <summary>Starts current run, or creates a fresh run after game-over.</summary>
    public void Start()
    {
        lock (_sync)
        {
            var statsEntity = FindStats();
            if (statsEntity == Entity.Null) return;

            var stats = _world.Get<PacmanStats>(statsEntity);
            if (stats.GameOver)
            {
                ResetWorld();
                statsEntity = FindStats();
                stats = _world.Get<PacmanStats>(statsEntity);
            }

            stats.Started = true;
            stats.GameOver = false;
            _world.Set(statsEntity, stats);
        }
    }

    /// <summary>Resets to initial state without starting play.</summary>
    public void Reset()
    {
        lock (_sync)
        {
            ResetWorld();
        }
    }

    /// <summary>Queues a client-suggested direction; invalid values are ignored.</summary>
    public void QueueDirection(string? direction)
    {
        if (!TryParseDirection(direction, out var parsed)) return;

        lock (_sync)
        {
            if (_pendingInput.Count < 4) _pendingInput.Enqueue(parsed);
        }
    }

    /// <summary>Runs one fixed simulation tick. Intended for deterministic tests.</summary>
    public void StepOnce()
    {
        lock (_sync)
        {
            TickCore();
        }
    }

    private void Tick(object? state)
    {
        lock (_sync)
        {
            TickCore();
        }
    }

    private void TickCore()
    {
        var statsEntity = FindStats();
        if (statsEntity == Entity.Null) return;

        var before = _world.Get<PacmanStats>(statsEntity);
        if (!before.Started || before.GameOver) return;

        var stopwatch = Stopwatch.StartNew();
        var dt = PacmanConfig.TickIntervalSeconds;
        _systems.BeforeUpdate(in dt);
        _systems.Update(in dt);
        _systems.AfterUpdate(in dt);

        var stats = _world.Get<PacmanStats>(statsEntity);
        if (!stats.GameOver && stats.PelletsRemaining <= 0)
        {
            CompleteLevel(statsEntity, ref stats);
        }

        stopwatch.Stop();
        _seq++;
        var snapshot = BuildSnapshot();
        var signal = new PacmanRenderSignal(
            _seq,
            snapshot.Count,
            stopwatch.Elapsed.TotalMilliseconds,
            snapshot,
            stats.Score,
            stats.Lives,
            stats.Level,
            stats.PelletsRemaining,
            stats.GameOver,
            stats.Started,
            stats.Frightened,
            stats.AtePellet,
            stats.AtePowerPellet,
            stats.GhostEaten,
            stats.Died,
            stats.LevelUp)
        { Epoch = _epoch };

        stats.AtePellet = false;
        stats.AtePowerPellet = false;
        stats.GhostEaten = false;
        stats.Died = false;
        stats.LevelUp = false;
        _world.Set(statsEntity, stats);
        _renderTransport.Push(signal);
    }

    private void CompleteLevel(Entity statsEntity, ref PacmanStats stats)
    {
        stats.Level++;
        stats.PelletsRemaining = PacmanMaze.PelletCount;
        stats.ModeIndex = 0;
        stats.ModeRemaining = PacmanConfig.ModeFirstScatterSeconds;
        stats.Frightened = false;
        stats.FrightenedRemaining = 0f;
        stats.GhostChain = 0;
        stats.LevelUp = true;

        DestroyPellets();
        CreatePellets();
        _stepSystem.ResetActors(stats);
        _world.Set(statsEntity, stats);
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
        _seq = 0;
        _epoch++;
        SeedWorld();
    }

    private void SeedWorld()
    {
        _world.Create(new PacmanStats(PacmanConfig.InitialLives, PacmanConfig.InitialLevel, PacmanMaze.PelletCount));

        var playerCenter = PacmanMaze.CenterOf(new PacmanCell(14, 23));
        _world.Create(
            new RenderId(PlayerRenderId),
            new PacmanTransform(playerCenter.X, playerCenter.Y),
            new PacmanMotion(),
            new PacmanFacing(PacmanDirection.Left),
            new PacmanSprite(PacmanSpriteKind.Player, 255, 255, 0),
            new PacmanPlayer());

        CreateGhost(PacmanGhostRole.Blinky, new PacmanCell(13, 11), PacmanDirection.Left, 220, 40, 40);
        CreateGhost(PacmanGhostRole.Pinky, new PacmanCell(15, 11), PacmanDirection.Right, 255, 184, 255);
        CreateGhost(PacmanGhostRole.Inky, new PacmanCell(12, 17), PacmanDirection.Left, 0, 220, 220);
        CreateGhost(PacmanGhostRole.Clyde, new PacmanCell(16, 17), PacmanDirection.Right, 255, 184, 82);

        CreatePellets();
    }

    private void CreateGhost(
        PacmanGhostRole role,
        PacmanCell cell,
        PacmanDirection direction,
        byte r,
        byte g,
        byte b)
    {
        var center = PacmanMaze.CenterOf(cell);
        var kind = role switch
        {
            PacmanGhostRole.Blinky => PacmanSpriteKind.Blinky,
            PacmanGhostRole.Pinky => PacmanSpriteKind.Pinky,
            PacmanGhostRole.Inky => PacmanSpriteKind.Inky,
            _ => PacmanSpriteKind.Clyde,
        };

        var renderId = FirstGhostRenderId + (int)role;
        _world.Create(
            new RenderId(renderId),
            new PacmanTransform(center.X, center.Y),
            new PacmanMotion(),
            new PacmanFacing(direction),
            new PacmanSprite(kind, r, g, b),
            new PacmanGhostState(role, PacmanGhostMode.Scatter, cell));
    }

    private void CreatePellets()
    {
        var renderId = FirstPelletRenderId;
        foreach (var cell in PacmanMaze.PelletCells)
        {
            var center = PacmanMaze.CenterOf(cell);
            var power = PacmanMaze.ContentAt(cell) == '*';
            _world.Create(
                new RenderId(renderId++),
                new PacmanTransform(center.X, center.Y),
                new PacmanMotion(),
                new PacmanFacing(PacmanDirection.None),
                new PacmanSprite(
                    power ? PacmanSpriteKind.PowerPellet : PacmanSpriteKind.Pellet,
                    255,
                    255,
                    255),
                new PacmanPellet(power));
        }
    }

    private void DestroyPellets()
    {
        var entities = new Entity[_world.Size];
        _world.GetEntities(new QueryDescription(), entities.AsSpan());
        foreach (var entity in entities)
        {
            if (_world.IsAlive(entity) && _world.Has<PacmanPellet>(entity)) _world.Destroy(entity);
        }
    }

    private IReadOnlyList<PacmanSpriteState> BuildSnapshot()
    {
        var entities = new Entity[_world.Size];
        _world.GetEntities(new QueryDescription(), entities.AsSpan());
        var states = new List<PacmanSpriteState>(entities.Length);

        foreach (var entity in entities)
        {
            if (!_world.IsAlive(entity) || !_world.Has<PacmanSprite>(entity) ||
                !_world.Has<PacmanTransform>(entity) || !_world.Has<RenderId>(entity)) continue;
            states.Add(ToState(entity));
        }

        states.Sort((left, right) =>
        {
            var kind = RenderLayer(left.Kind).CompareTo(RenderLayer(right.Kind));
            return kind != 0 ? kind : left.Id.CompareTo(right.Id);
        });
        return states;
    }

    private PacmanSpriteState ToState(Entity entity)
    {
        var sprite = _world.Get<PacmanSprite>(entity);
        var transform = _world.Get<PacmanTransform>(entity);
        var motion = _world.Get<PacmanMotion>(entity);
        var facing = _world.Get<PacmanFacing>(entity);
        var kind = (PacmanSpriteKind)sprite.Kind;
        var mode = _world.Has<PacmanGhostState>(entity)
            ? _world.Get<PacmanGhostState>(entity).GhostMode
            : PacmanGhostMode.Scatter;

        return new PacmanSpriteState(
            _world.Get<RenderId>(entity).Id,
            transform.X,
            transform.Y,
            transform.PreviousX,
            transform.PreviousY,
            motion.VelocityX,
            motion.VelocityY,
            RotationFor(facing.CurrentDirection),
            kind,
            facing.CurrentDirection,
            mode,
            sprite.Visible,
            sprite.R,
            sprite.G,
            sprite.B);
    }

    private static int RenderLayer(PacmanSpriteKind kind) => kind switch
    {
        PacmanSpriteKind.Pellet or PacmanSpriteKind.PowerPellet => 0,
        PacmanSpriteKind.Blinky or PacmanSpriteKind.Pinky or PacmanSpriteKind.Inky or PacmanSpriteKind.Clyde => 1,
        _ => 2,
    };

    private static float RotationFor(PacmanDirection direction) => direction switch
    {
        PacmanDirection.Right => 0f,
        PacmanDirection.Down => MathF.PI / 2f,
        PacmanDirection.Left => MathF.PI,
        PacmanDirection.Up => -MathF.PI / 2f,
        _ => 0f,
    };

    private PacmanStats ReadStats()
    {
        lock (_sync)
        {
            var entity = FindStats();
            return entity == Entity.Null ? new PacmanStats(0, 0, 0) : _world.Get<PacmanStats>(entity);
        }
    }

    private Entity FindStats()
    {
        var entities = new Entity[_world.Size];
        _world.GetEntities(new QueryDescription(), entities.AsSpan());
        foreach (var entity in entities)
        {
            if (_world.IsAlive(entity) && _world.Has<PacmanStats>(entity)) return entity;
        }

        return Entity.Null;
    }

    private static bool TryParseDirection(string? value, out PacmanDirection direction)
    {
        direction = value?.ToLowerInvariant() switch
        {
            "up" => PacmanDirection.Up,
            "left" => PacmanDirection.Left,
            "down" => PacmanDirection.Down,
            "right" => PacmanDirection.Right,
            _ => PacmanDirection.None,
        };
        return direction != PacmanDirection.None;
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _systems.Dispose();
        World.Destroy(_world);
    }
}
