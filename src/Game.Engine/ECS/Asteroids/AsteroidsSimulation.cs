using System.Diagnostics;
using Arch.Core;
using Arch.Systems;
using Game.Engine.ECS.Systems;

namespace Game.Engine.ECS;

/// <summary>
///     Batched render signal for the asteroids scene. Emitted once per 60 Hz physics
///     tick while a game is running, carrying the full sprite list plus score/lives/
///     level/game-over flags. <see cref="Exploded"/>, <see cref="Fired"/>,
///     <see cref="SaucerSpawned"/>, <see cref="LevelUp"/> and <see cref="LifeGained"/>
///     are ECS-originated edge events consumed once by the client (sound/particle triggers).
/// </summary>
public sealed record AsteroidsRenderSignal(
    long Seq,
    int EntityCount,
    double TickMs,
    IReadOnlyList<AsteroidsSpriteState> Sprites,
    int Score,
    int HighScore,
    int Lives,
    int Level,
    bool GameOver,
    bool Started,
    bool ThrustOn,
    bool Exploded,
    bool Fired,
    bool SaucerSpawned,
    bool LevelUp,
    bool LifeGained);

/// <summary>
///     Owns the asteroids Arch ECS world + Box2D physics world. The sim ticks at
///     60 Hz; the Box2D world is the authoritative mover and collider (ADR-002), the
///     gameplay system resolves contacts into split/explode/score events and emits
///     one batched <see cref="AsteroidsRenderSignal"/> per tick. Input is suggested
///     by the client and applied by the simulation (C# sole authority, ADR-001/006).
/// </summary>
public sealed class AsteroidsSimulation : IDisposable
{
    private const double TickIntervalSeconds = AsteroidsConfig.TickIntervalSeconds;

    private readonly World _world;
    private readonly AsteroidsContext _ctx;
    private readonly AsteroidsGameSystem _gameSystem;
    private readonly Group<double> _systems;
    private readonly Timer? _timer;

    // Guards the world (step mutation + snapshot reads across timer and request threads).
    private readonly object _sync = new();
    private long _seq;

    public event Action<AsteroidsRenderSignal>? OnRenderSignal;

    public AsteroidsSimulation() : this(new Random())
    {
    }

    /// <param name="startTimer">When false the 60 Hz timer is not started (deterministic tests step manually).</param>
    public AsteroidsSimulation(bool startTimer) : this(new Random(), startTimer)
    {
    }

    /// <param name="seed">Seeded for deterministic tests.</param>
    public AsteroidsSimulation(int seed) : this(new Random(seed))
    {
    }

    /// <param name="seed">Seeded for deterministic tests.</param>
    /// <param name="startTimer">When false the 60 Hz timer is not started (deterministic tests step manually).</param>
    public AsteroidsSimulation(int seed, bool startTimer) : this(new Random(seed), startTimer)
    {
    }

    /// <param name="random">Inject a <see cref="Random"/> for full control over spawns.</param>
    public AsteroidsSimulation(Random random) : this(random, startTimer: true)
    {
    }

    /// <param name="random">Inject a <see cref="Random"/> for full control over spawns.</param>
    /// <param name="startTimer">When false the 60 Hz timer is not started (deterministic tests step manually).</param>
    public AsteroidsSimulation(Random random, bool startTimer)
    {
        _world = World.Create();
        _ctx = new AsteroidsContext(random);
        _gameSystem = new AsteroidsGameSystem(_world, _ctx);
        _systems = new Group<double>("Asteroids", _gameSystem);
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
                var statsEntity = AsteroidsHelpers.FindStats(_world);
                return statsEntity == Entity.Null ? 0 : _world.Get<AsteroidsStats>(statsEntity).Score;
            }
        }
    }

    public int HighScore
    {
        get
        {
            lock (_sync)
            {
                var statsEntity = AsteroidsHelpers.FindStats(_world);
                return statsEntity == Entity.Null ? 0 : _world.Get<AsteroidsStats>(statsEntity).HighScore;
            }
        }
    }

    public int Lives
    {
        get
        {
            lock (_sync)
            {
                var statsEntity = AsteroidsHelpers.FindStats(_world);
                return statsEntity == Entity.Null ? 0 : _world.Get<AsteroidsStats>(statsEntity).Lives;
            }
        }
    }

    public int Level
    {
        get
        {
            lock (_sync)
            {
                var statsEntity = AsteroidsHelpers.FindStats(_world);
                return statsEntity == Entity.Null ? 0 : _world.Get<AsteroidsStats>(statsEntity).Level;
            }
        }
    }

    public bool IsGameOver
    {
        get
        {
            lock (_sync)
            {
                var statsEntity = AsteroidsHelpers.FindStats(_world);
                return statsEntity != Entity.Null && _world.Get<AsteroidsStats>(statsEntity).GameOver;
            }
        }
    }

    public bool IsStarted
    {
        get
        {
            lock (_sync)
            {
                var statsEntity = AsteroidsHelpers.FindStats(_world);
                return statsEntity != Entity.Null && _world.Get<AsteroidsStats>(statsEntity).Started;
            }
        }
    }

    /// <summary>Starts a fresh game. If the previous run is over, the world is reset first.</summary>
    public void Start()
    {
        lock (_sync)
        {
            var statsEntity = AsteroidsHelpers.FindStats(_world);
            var stats = statsEntity != Entity.Null
                ? _world.Get<AsteroidsStats>(statsEntity)
                : new AsteroidsStats(0, AsteroidsConfig.InitialLives, AsteroidsConfig.StartAsteroidCount);
            if (stats.GameOver)
            {
                ResetWorld();
                statsEntity = AsteroidsHelpers.FindStats(_world);
            }
            _world.Set(statsEntity, new AsteroidsStats(0, AsteroidsConfig.InitialLives,
                AsteroidsConfig.StartAsteroidCount, started: true));
        }
    }

    /// <summary>Current world snapshot for the initial SSR payload (court visible before first SSE tick).</summary>
    public IReadOnlyList<AsteroidsSpriteState> Snapshot()
    {
        lock (_sync)
        {
            return BuildSnapshot();
        }
    }

    /// <summary>Client-suggested input. Held flags are absolute; fire/hyperspace are
    /// one-shot edges applied (and validated) by the gameplay system.</summary>
    public void QueueInput(AsteroidsInputRequest request)
    {
        lock (_sync)
        {
            _ctx.Input.Thrust = request.Thrust;
            _ctx.Input.Left = request.Left;
            _ctx.Input.Right = request.Right;
            _ctx.Input.Fire |= request.Fire;
            _ctx.Input.Hyperspace |= request.Hyperspace;
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

    /// <summary>Deterministic test hook: applies the suggested input, then runs one full tick.</summary>
    public void ApplyInput(AsteroidsInputRequest request)
    {
        lock (_sync)
        {
            _ctx.Input.Thrust = request.Thrust;
            _ctx.Input.Left = request.Left;
            _ctx.Input.Right = request.Right;
            _ctx.Input.Fire |= request.Fire;
            _ctx.Input.Hyperspace |= request.Hyperspace;
            _systems.BeforeUpdate(TickIntervalSeconds);
            _systems.Update(TickIntervalSeconds);
            _systems.AfterUpdate(TickIntervalSeconds);
        }
    }

    /// <summary>Deterministic test hook: one full tick (1/60 s).</summary>
    public void StepOnce()
    {
        lock (_sync)
        {
            _systems.BeforeUpdate(TickIntervalSeconds);
            _systems.Update(TickIntervalSeconds);
            _systems.AfterUpdate(TickIntervalSeconds);
        }
    }

    /// <summary>Test hook: teleports the ship to the given center with zero velocity.</summary>
    internal void PlaceShipAt(float x, float y)
    {
        lock (_sync)
        {
            var ship = AsteroidsHelpers.FindShip(_world);
            if (ship == Entity.Null) return;
            _world.Set(ship, new Position(x, y));
            _world.Set(ship, new Velocity(0f, 0f));
        }
    }

    /// <summary>Test hook: spawns a bullet at the given position with the given velocity (px/s).</summary>
    internal void PlaceBulletAt(float x, float y, float vx, float vy)
    {
        lock (_sync)
        {
            var entity = _world.Create(
                new RenderId(_ctx.NextRenderId()),
                new Position(x, y),
                new Velocity(vx, vy),
                new Rotation(MathF.Atan2(vx, -vy)),
                new SpriteColor(248, 250, 252),
                new BulletTag(0f));
            _ctx.Register(entity);
            _gameSystem.CreateBodyForTest(entity, AsteroidsConfig.BulletRadius,
                AsteroidsConfig.CatBullet, AsteroidsConfig.MaskBullet, isBullet: true);
        }
    }

    /// <summary>Test hook: spawns a single asteroid of the given size at the given position.</summary>
    internal void PlaceAsteroidAt(AsteroidSize size, float x, float y)
    {
        lock (_sync)
        {
            _gameSystem.SpawnAsteroid(_world, size, x, y);
        }
    }

    /// <summary>Test hook: spawns a single asteroid with an explicit velocity (px/s).</summary>
    internal void PlaceAsteroidAt(AsteroidSize size, float x, float y, float vx, float vy)
    {
        lock (_sync)
        {
            _gameSystem.SpawnAsteroid(_world, size, x, y, vx, vy);
        }
    }

    /// <summary>Test hook: removes every asteroid from the court.</summary>
    internal void ClearAsteroidsForTest()
    {
        lock (_sync)
        {
            foreach (var asteroid in AsteroidsHelpers.FindAsteroids(_world))
            {
                _gameSystem.DestroyEntity(_world, asteroid);
            }
        }
    }

    /// <summary>Test hook: current ship center/velocity/rotation, or NaN when dead.</summary>
    internal (float X, float Y, float VX, float VY, float Rotation, bool Alive) ShipStateForTest()
    {
        lock (_sync)
        {
            var ship = AsteroidsHelpers.FindShip(_world);
            if (ship == Entity.Null) return (float.NaN, float.NaN, 0f, 0f, 0f, false);
            var pos = _world.Get<Position>(ship);
            var vel = _world.Get<Velocity>(ship);
            var rot = _world.Get<Rotation>(ship);
            return (pos.X, pos.Y, vel.X, vel.Y, rot.Value, true);
        }
    }

    internal int AsteroidCountForTest()
    {
        lock (_sync)
        {
            return AsteroidsHelpers.FindAsteroids(_world).Count;
        }
    }

    internal int BulletCountForTest()
    {
        lock (_sync)
        {
            return AsteroidsHelpers.CountBullets(_world);
        }
    }

    internal int ExplosionCountForTest()
    {
        lock (_sync)
        {
            return AsteroidsHelpers.CountExplosions(_world);
        }
    }

    internal bool SaucerAliveForTest()
    {
        lock (_sync)
        {
            return AsteroidsHelpers.FindSaucer(_world) != Entity.Null;
        }
    }

    /// <summary>Test hook: sets the remaining lives directly.</summary>
    internal void SetLivesForTest(int lives)
    {
        lock (_sync)
        {
            var statsEntity = AsteroidsHelpers.FindStats(_world);
            if (statsEntity == Entity.Null) return;
            var stats = _world.Get<AsteroidsStats>(statsEntity);
            _world.Set(statsEntity, stats with { Lives = lives });
        }
    }

    private void SeedWorld()
    {
        _world.Create(new AsteroidsStats(0, AsteroidsConfig.InitialLives, AsteroidsConfig.StartAsteroidCount));
        _gameSystem.SpawnShip(_world);
        _gameSystem.SpawnBelt(_world, AsteroidsConfig.StartAsteroidCount);
    }

    private void ResetWorld()
    {
        _gameSystem.DestroyAll(_world);
        _ctx.Input.Thrust = false;
        _ctx.Input.Left = false;
        _ctx.Input.Right = false;
        _ctx.Input.Fire = false;
        _ctx.Input.Hyperspace = false;
        SeedWorld();
    }

    private void Tick(object? _)
    {
        lock (_sync)
        {
            var statsEntity = AsteroidsHelpers.FindStats(_world);
            if (statsEntity == Entity.Null) return;
            var stats = _world.Get<AsteroidsStats>(statsEntity);
            if (!stats.Started || stats.GameOver) return;

            var stopwatch = Stopwatch.StartNew();
            var dt = TickIntervalSeconds;
            _systems.BeforeUpdate(in dt);
            _systems.Update(in dt);
            _systems.AfterUpdate(in dt);
            stopwatch.Stop();

            stats = _world.Get<AsteroidsStats>(statsEntity);

            // Consume the edge-event flags; the next signal starts clean. ThrustOn is
            // continuous state (client keeps the flame/sound alive while true).
            _world.Set(statsEntity, new AsteroidsStats(stats.Score, stats.Lives, stats.Level, stats.HighScore,
                stats.NextSaucerPoints, stats.GameOver, stats.Started, stats.ThrustOn));

            _seq++;
            OnRenderSignal?.Invoke(new AsteroidsRenderSignal(
                _seq, _world.Size, stopwatch.Elapsed.TotalMilliseconds,
                BuildSnapshot(), stats.Score, stats.HighScore, stats.Lives, stats.Level, stats.GameOver, stats.Started,
                stats.ThrustOn, stats.Exploded, stats.Fired, stats.SaucerSpawned, stats.LevelUp, stats.LifeGained));
        }
    }

    /// <summary>Builds the render snapshot: asteroids, saucer, missile, ship, bullets, explosions.</summary>
    private IReadOnlyList<AsteroidsSpriteState> BuildSnapshot()
    {
        var states = new List<AsteroidsSpriteState>();
        var entities = new Entity[_world.Size];
        _world.GetEntities(new QueryDescription(), entities.AsSpan());

        foreach (var entity in entities)
        {
            if (!_world.IsAlive(entity)) continue;
            if (_world.Has<AsteroidTag>(entity))
            {
                var tag = _world.Get<AsteroidTag>(entity);
                states.Add(ToState(entity, AsteroidsSpriteKind.Asteroid, RadiusOf(tag.Size)));
            }
        }

        foreach (var entity in entities)
        {
            if (!_world.IsAlive(entity)) continue;
            if (_world.Has<SaucerTag>(entity))
            {
                states.Add(ToState(entity, AsteroidsSpriteKind.Saucer, 0f));
            }
            else if (_world.Has<MissileTag>(entity))
            {
                states.Add(ToState(entity, AsteroidsSpriteKind.Missile, 0f));
            }
        }

        foreach (var entity in entities)
        {
            if (!_world.IsAlive(entity)) continue;
            if (_world.Has<ShipTag>(entity))
            {
                states.Add(ToState(entity, AsteroidsSpriteKind.Ship, 0f));
            }
        }

        foreach (var entity in entities)
        {
            if (!_world.IsAlive(entity)) continue;
            if (_world.Has<BulletTag>(entity))
            {
                states.Add(ToState(entity, AsteroidsSpriteKind.Bullet, 0f));
            }
        }

        foreach (var entity in entities)
        {
            if (!_world.IsAlive(entity)) continue;
            if (_world.Has<ExplosionTag>(entity))
            {
                var tag = _world.Get<ExplosionTag>(entity);
                // Size carries the age fraction [0..1]; the client scales the ring.
                states.Add(ToState(entity, AsteroidsSpriteKind.Explosion, tag.Age / tag.Lifetime));
            }
        }

        return states;
    }

    private AsteroidsSpriteState ToState(Entity entity, AsteroidsSpriteKind kind, float size)
    {
        var pos = _world.Get<Position>(entity);
        var rot = _world.Has<Rotation>(entity) ? _world.Get<Rotation>(entity).Value : 0f;
        var vel = _world.Has<Velocity>(entity) ? _world.Get<Velocity>(entity) : default;
        var color = _world.Get<SpriteColor>(entity);
        return new AsteroidsSpriteState(_world.Get<RenderId>(entity).Id, pos.X, pos.Y, rot,
            vel.X, vel.Y, (byte)kind, size, color.R, color.G, color.B);
    }

    private static float RadiusOf(AsteroidSize size) => size switch
    {
        AsteroidSize.Small => AsteroidsConfig.AsteroidRadiusSmall,
        AsteroidSize.Medium => AsteroidsConfig.AsteroidRadiusMedium,
        AsteroidSize.Large => AsteroidsConfig.AsteroidRadiusLarge,
        _ => 0f
    };

    public void Dispose()
    {
        _timer?.Dispose();
        _systems.Dispose();
        _ctx.Dispose();
        World.Destroy(_world);
    }
}
