using System.Diagnostics;
using Arch.Core;
using Arch.Systems;
using Game.Engine.ECS.Systems;

namespace Game.Engine.ECS.Breakout;

/// <summary>
///     Batched render signal for the breakout scene. Emitted once per physics tick
///     while a game is running, carrying the full sprite list plus score/lives/level/
///     game-over flags. <see cref="BrickHit"/>, <see cref="PaddleHit"/>,
///     <see cref="LevelUp"/> and <see cref="LoseLife"/> are ECS-originated edge events
///     consumed once by the client (sound triggers).
/// </summary>
public sealed partial record BreakoutRenderSignal(
    long Seq,
    int EntityCount,
    double TickMs,
    IReadOnlyList<BreakoutSpriteState> Sprites,
    int Score,
    int Lives,
    int Level,
    bool GameOver,
    bool Started,
    bool BrickHit,
    bool PaddleHit,
    bool LevelUp,
    bool LoseLife);

public partial record BreakoutRenderSignal
{
    public double StepMs { get; init; } = (1d / 60d) * 1000d;
    public long Epoch { get; init; }
}

/// <summary>
///     Owns the breakout Arch ECS world. The sim ticks at 60 Hz; the physics system
///     moves the paddle and ball, resolves swept collisions (reference recursive
///     interception) and emits one batched <see cref="BreakoutRenderSignal"/> per tick.
///     C# is the sole authority: input is suggested by the client and applied by
///     <see cref="BreakoutInputSystem"/>; bricks, paddle and ball are ECS entities.
/// </summary>
public sealed class BreakoutSimulation : IDisposable
{
    private const double TickIntervalSeconds = 1.0 / 60.0;
    private static int _brickIdCounter = BreakoutConfig.BrickRenderIdStart;

    /// <summary>Next unique render id for a brick entity.</summary>
    public static int NextBrickId() => Interlocked.Increment(ref _brickIdCounter);

    private readonly World _world;
    private readonly Group<double> _systems;
    private readonly Timer? _timer;
    private readonly Random _random;
    private readonly BreakoutInputState _input = new();
    private readonly BreakoutPhysicsSystem _physicsSystem;

    // Guards the world (step mutation + snapshot reads across timer and request threads).
    private readonly object _sync = new();
    private long _seq;
    private long _epoch;

    private readonly IRenderTransport<BreakoutRenderSignal> _renderTransport;

    /// <summary>
    ///     Batched render signal, delivered through the <see cref="IRenderTransport{TSignal}"/> seam
    ///     (ADR-007). Forwarding event keeps the SSE host subscription contract unchanged.
    /// </summary>
    public event Action<BreakoutRenderSignal>? OnRenderSignal
    {
        add => _renderTransport.OnSignal += value;
        remove => _renderTransport.OnSignal -= value;
    }

    public BreakoutSimulation() : this(new Random())
    {
    }

    /// <param name="startTimer">When false the 60 Hz timer is not started (deterministic tests step manually).</param>
    public BreakoutSimulation(bool startTimer) : this(new Random(), startTimer)
    {
    }

    /// <param name="seed">Seeded for deterministic tests.</param>
    public BreakoutSimulation(int seed) : this(new Random(seed))
    {
    }

    /// <param name="seed">Seeded for deterministic tests.</param>
    /// <param name="startTimer">When false the 60 Hz timer is not started (deterministic tests step manually).</param>
    public BreakoutSimulation(int seed, bool startTimer) : this(new Random(seed), startTimer)
    {
    }

    /// <param name="random">Inject a <see cref="Random"/> for full control over paddle placement.</param>
    public BreakoutSimulation(Random random) : this(random, startTimer: true)
    {
    }

    /// <param name="random">Inject a <see cref="Random"/> for full control over paddle placement.</param>
    /// <param name="startTimer">When false the 60 Hz timer is not started (deterministic tests step manually).</param>
    public BreakoutSimulation(Random random, bool startTimer,
        IRenderTransport<BreakoutRenderSignal>? renderTransport = null)
    {
        _renderTransport = renderTransport ?? new ServerRenderTransport<BreakoutRenderSignal>();
        _random = random;
        _world = World.Create();
        _physicsSystem = new BreakoutPhysicsSystem(_world);
        _systems = new Group<double>("Breakout",
            new BreakoutInputSystem(_world, _input),
            _physicsSystem);
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
                var statsEntity = BreakoutHelpers.FindStats(_world);
                return statsEntity == Entity.Null ? 0 : _world.Get<BreakoutStats>(statsEntity).Score;
            }
        }
    }

    public int Lives
    {
        get
        {
            lock (_sync)
            {
                var statsEntity = BreakoutHelpers.FindStats(_world);
                return statsEntity == Entity.Null ? 0 : _world.Get<BreakoutStats>(statsEntity).Lives;
            }
        }
    }

    public int Level
    {
        get
        {
            lock (_sync)
            {
                var statsEntity = BreakoutHelpers.FindStats(_world);
                return statsEntity == Entity.Null ? 0 : _world.Get<BreakoutStats>(statsEntity).Level;
            }
        }
    }

    public bool IsGameOver
    {
        get
        {
            lock (_sync)
            {
                var statsEntity = BreakoutHelpers.FindStats(_world);
                return statsEntity != Entity.Null && _world.Get<BreakoutStats>(statsEntity).GameOver;
            }
        }
    }

    public bool IsStarted
    {
        get
        {
            lock (_sync)
            {
                var statsEntity = BreakoutHelpers.FindStats(_world);
                return statsEntity != Entity.Null && _world.Get<BreakoutStats>(statsEntity).Started;
            }
        }
    }

    /// <summary>Starts a fresh game. If the previous run is over, the world is reset first.</summary>
    public void Start()
    {
        lock (_sync)
        {
            var statsEntity = BreakoutHelpers.FindStats(_world);
            var stats = statsEntity != Entity.Null
                ? _world.Get<BreakoutStats>(statsEntity)
                : new BreakoutStats(0, BreakoutConfig.InitialLives, 0);
            if (stats.GameOver)
            {
                ResetWorld();
                statsEntity = BreakoutHelpers.FindStats(_world);
            }
            _world.Set(statsEntity, new BreakoutStats(0, BreakoutConfig.InitialLives, 0, started: true));
            ResetBallOnPaddle(statsEntity);
        }
    }

    /// <summary>Current world snapshot for the initial SSR payload (board visible before first SSE tick).</summary>
    public IReadOnlyList<BreakoutSpriteState> Snapshot()
    {
        lock (_sync)
        {
            return BuildSnapshot();
        }
    }

    /// <summary>Client-suggested input (held paddle direction + one-shot launch). Applied by the input system.</summary>
    public void QueueInput(BreakoutInputRequest request)
    {
        lock (_sync)
        {
            _input.Left = request.Left;
            _input.Right = request.Right;
            _input.Launch |= request.Launch;
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

    /// <summary>Deterministic test hook: applies the suggested input synchronously.</summary>
    public void ApplyInput(BreakoutInputRequest request)
    {
        lock (_sync)
        {
            _input.Left = request.Left;
            _input.Right = request.Right;
            _input.Launch |= request.Launch;
            _systems.BeforeUpdate(TickIntervalSeconds);
            _systems.Update(TickIntervalSeconds);
            _systems.AfterUpdate(TickIntervalSeconds);
        }
    }

    /// <summary>Deterministic test hook: one physics step (1/60 s).</summary>
    public void StepOnce()
    {
        lock (_sync)
        {
            _physicsSystem.Update(TickIntervalSeconds);
        }
    }

    /// <summary>Test hook: places the ball at a given center with velocity (px/s) and launches it.</summary>
    internal void PlaceBallAt(float x, float y, float vx, float vy)
    {
        lock (_sync)
        {
            var ball = BreakoutHelpers.FindBall(_world);
            if (ball == Entity.Null) return;
            var b = _world.Get<BreakoutBall>(ball);
            var speed = MathF.Sqrt((vx * vx) + (vy * vy));
            _world.Set(ball, new Position(x, y));
            _world.Set(ball, new Velocity(vx, vy));
            _world.Set(ball, new BreakoutBall(b.Radius, speed, moving: speed > 0f));
        }
    }

    /// <summary>Test hook: positions the paddle center at x (clamped to the court).</summary>
    internal void PlacePaddleAt(float x)
    {
        lock (_sync)
        {
            var paddle = BreakoutHelpers.FindPaddle(_world);
            if (paddle == Entity.Null) return;
            var p = _world.Get<BreakoutPaddle>(paddle);
            var pos = _world.Get<Position>(paddle);
            var clamped = Math.Clamp(x, p.Width / 2f, BreakoutConfig.CourtWidth - (p.Width / 2f));
            _world.Set(paddle, new Position(clamped, pos.Y));
        }
    }

    /// <summary>Test hook: removes every brick from the court.</summary>
    internal void ClearBricksForTest()
    {
        lock (_sync)
        {
            foreach (var brick in BreakoutHelpers.FindBricks(_world))
            {
                _world.Destroy(brick);
            }
        }
    }

    /// <summary>Test hook: forces a ball-loss (life decrement / game over) without waiting for the bottom.</summary>
    internal void ForceLoseBall()
    {
        lock (_sync)
        {
            var ball = BreakoutHelpers.FindBall(_world);
            var statsEntity = BreakoutHelpers.FindStats(_world);
            if (ball == Entity.Null || statsEntity == Entity.Null) return;
            _physicsSystem.LoseBall(statsEntity, ball);
        }
    }

    /// <summary>Test hook: creates a single brick (board construction for collision tests).</summary>
    internal void PlaceBrick(int chunkX, int chunkY, int widthChunks, int score)
    {
        lock (_sync)
        {
            var width = widthChunks * BreakoutConfig.ChunkSize;
            _world.Create(
                new RenderId(NextBrickId()),
                new Position((chunkX * BreakoutConfig.ChunkSize) + (width / 2f),
                    (chunkY * BreakoutConfig.ChunkSize) + (BreakoutConfig.ChunkSize / 2f)),
                new SpriteColor(200, 200, 200),
                new BreakoutBrick(score, width));
        }
    }

    /// <summary>Test hook: current ball velocity, speed and moving flag.</summary>
    internal (float X, float Y, float Speed, bool Moving) BallStateForTest()
    {
        lock (_sync)
        {
            var ball = BreakoutHelpers.FindBall(_world);
            if (ball == Entity.Null) return default;
            var v = _world.Get<Velocity>(ball);
            var b = _world.Get<BreakoutBall>(ball);
            return (v.X, v.Y, b.Speed, b.Moving);
        }
    }

    /// <summary>Test hook: current paddle center X.</summary>
    internal float PaddleXForTest()
    {
        lock (_sync)
        {
            var paddle = BreakoutHelpers.FindPaddle(_world);
            return paddle == Entity.Null ? float.NaN : _world.Get<Position>(paddle).X;
        }
    }

    private void ResetWorld()
    {
        _epoch++;
        var entities = new Entity[_world.Size];
        _world.GetEntities(new QueryDescription(), entities.AsSpan());
        foreach (var entity in entities)
        {
            if (_world.IsAlive(entity)) _world.Destroy(entity);
        }
        _input.Left = false;
        _input.Right = false;
        _input.Launch = false;
        SeedWorld();
    }

    private void SeedWorld()
    {
        _world.Create(new BreakoutStats(0, BreakoutConfig.InitialLives, 0));
        _world.Create(
            new RenderId(1),
            new Position(BreakoutConfig.CourtWidth / 2f, BreakoutConfig.CourtHeight - BreakoutConfig.PaddleHeight / 2f),
            new SpriteColor(245, 111, 37),
            new BreakoutPaddle(BreakoutConfig.PaddleWidth, BreakoutConfig.PaddleHeight));
        _world.Create(
            new RenderId(2),
            new Position(BreakoutConfig.CourtWidth / 2f, BreakoutConfig.CourtHeight - BreakoutConfig.PaddleHeight - BreakoutConfig.BallRadius),
            new Velocity(0f, 0f),
            new SpriteColor(248, 250, 252),
            new BreakoutBall(BreakoutConfig.BallRadius, BreakoutConfig.BaseBallSpeed, moving: false));
        _physicsSystem.SpawnBricks(0);
    }

    private void ResetBallOnPaddle(Entity statsEntity)
    {
        // Deterministic launch point: reset paddle to a seeded position and stick the ball on it.
        var paddle = BreakoutHelpers.FindPaddle(_world);
        if (paddle == Entity.Null) return;
        var p = _world.Get<BreakoutPaddle>(paddle);
        var minX = p.Width / 2f;
        var maxX = BreakoutConfig.CourtWidth - (p.Width / 2f);
        var paddleX = minX + (_random.NextSingle() * (maxX - minX));
        _world.Set(paddle, new Position(paddleX, BreakoutConfig.CourtHeight - (p.Height / 2f)));

        var ball = BreakoutHelpers.FindBall(_world);
        if (ball == Entity.Null) return;
        var radius = _world.Get<BreakoutBall>(ball).Radius;
        _world.Set(ball, new Position(paddleX, BreakoutConfig.CourtHeight - p.Height - radius));
        _world.Set(ball, new Velocity(0f, 0f));
        _world.Set(ball, new BreakoutBall(radius, BreakoutConfig.BaseBallSpeed, moving: false));
    }

    private void Tick(object? _)
    {
        lock (_sync)
        {
            var statsEntity = BreakoutHelpers.FindStats(_world);
            if (statsEntity == Entity.Null) return;
            var stats = _world.Get<BreakoutStats>(statsEntity);
            if (!stats.Started || stats.GameOver) return;

            var stopwatch = Stopwatch.StartNew();
            var dt = TickIntervalSeconds;
            _systems.BeforeUpdate(in dt);
            _systems.Update(in dt);
            _systems.AfterUpdate(in dt);
            stopwatch.Stop();

            stats = _world.Get<BreakoutStats>(statsEntity);

            // Consume the edge-event flags; the next signal starts clean.
            _world.Set(statsEntity, new BreakoutStats(stats.Score, stats.Lives, stats.Level,
                stats.GameOver, stats.Started));

            _seq++;
            _renderTransport.Push(new BreakoutRenderSignal(
                _seq, _world.Size, stopwatch.Elapsed.TotalMilliseconds,
                BuildSnapshot(), stats.Score, stats.Lives, stats.Level, stats.GameOver, stats.Started,
                stats.BrickHit, stats.PaddleHit, stats.LevelUp, stats.LoseLife)
            { Epoch = _epoch });
        }
    }

    /// <summary>Builds the render snapshot: bricks, then paddle, then ball (on top).</summary>
    private IReadOnlyList<BreakoutSpriteState> BuildSnapshot()
    {
        var entities = new Entity[_world.Size];
        _world.GetEntities(new QueryDescription(), entities.AsSpan());

        var states = new List<BreakoutSpriteState>(entities.Length);
        foreach (var entity in entities)
        {
            if (!_world.IsAlive(entity) || !_world.Has<BreakoutBrick>(entity)) continue;
            var pos = _world.Get<Position>(entity);
            var brick = _world.Get<BreakoutBrick>(entity);
            var color = _world.Get<SpriteColor>(entity);
            states.Add(ToState(_world.Get<RenderId>(entity).Id, pos, brick.Width, BreakoutConfig.ChunkSize, color));
        }

        var paddle = BreakoutHelpers.FindPaddle(_world);
        if (paddle != Entity.Null)
        {
            var pos = _world.Get<Position>(paddle);
            var p = _world.Get<BreakoutPaddle>(paddle);
            var color = _world.Get<SpriteColor>(paddle);
            states.Add(ToState(_world.Get<RenderId>(paddle).Id, pos, p.Width, p.Height, color));
        }

        var ball = BreakoutHelpers.FindBall(_world);
        if (ball != Entity.Null)
        {
            var pos = _world.Get<Position>(ball);
            var b = _world.Get<BreakoutBall>(ball);
            var color = _world.Get<SpriteColor>(ball);
            var diameter = b.Radius * 2f;
            states.Add(ToState(_world.Get<RenderId>(ball).Id, pos, diameter, diameter, color));
        }

        return states;
    }

    private static BreakoutSpriteState ToState(int id, Position pos, float width, float height, SpriteColor color) =>
        new(id, pos.X, pos.Y, width, height, color.R, color.G, color.B);

    public void Dispose()
    {
        _timer?.Dispose();
        _systems.Dispose();
        World.Destroy(_world);
    }
}
