using System.Diagnostics;
using Arch.Core;
using Arch.Systems;
using Game.Engine.ECS.Systems;

namespace Game.Engine.ECS;

/// <summary>Plain-data snapshot of one entity, serializable for the SSR payload and SSE stream.</summary>
public record struct SpriteState(int Id, float X, float Y, byte R, byte G, byte B);

/// <summary>Batched render signal emitted at most once per <see cref="EcsSimulation.SignalIntervalSeconds"/>.</summary>
public sealed record EcsRenderSignal(
    long Seq,
    int EntityCount,
    double TickMs,
    IReadOnlyList<SpriteState> Sprites);

/// <summary>
///     Owns the Arch ECS world, ticks the systems at ~60 Hz and emits a single batched
///     <see cref="EcsRenderSignal"/> every second. Throttling avoids flooding the render
///     pipeline with one event per entity per tick.
/// </summary>
public sealed class EcsSimulation : IDisposable
{
    public const double SignalIntervalSeconds = 1.0;
    private const double TickIntervalSeconds = 1.0 / 60.0;
    private const int EntityCount = 10;

    private readonly World _world;
    private readonly Group<double> _systems;
    private readonly Timer _timer;
    private readonly Random _random = new();

    // Guards the world (tick mutation + snapshot reads across timer and request threads).
    private readonly object _sync = new();
    private long _seq;
    private double _elapsedSinceSignal;

    public event Action<EcsRenderSignal>? OnRenderSignal;

    public float Width { get; } = 800;
    public float Height { get; } = 600;

    public EcsSimulation()
    {
        _world = World.Create();
        for (var i = 0; i < EntityCount; i++)
        {
            _world.Create(
                new Position(_random.NextSingle() * Width, _random.NextSingle() * Height),
                new Velocity(NextSingle(-120f, 120f), NextSingle(-120f, 120f)),
                new SpriteColor((byte)_random.Next(0, 256), (byte)_random.Next(0, 256), (byte)_random.Next(0, 256)),
                new RenderId(i)
            );
        }

        _systems = new Group<double>(
            "ECS",
            new MovementSystem(_world, Width, Height),
            new ColorSystem(_world)
        );
        _systems.Initialize();

        _timer = new Timer(Tick, null, TimeSpan.Zero, TimeSpan.FromSeconds(TickIntervalSeconds));
    }

    /// <summary>Current world snapshot for the initial SSR payload (sprites visible before first SSE tick).</summary>
    public IReadOnlyList<SpriteState> Snapshot()
    {
        lock (_sync)
        {
            return BuildSnapshot();
        }
    }

    private void Tick(object? _)
    {
        var signal = BuildSignal();
        if (signal is null) return;
        OnRenderSignal?.Invoke(signal);
    }

    /// <summary>Runs one simulation step and returns a batched signal if the 1 s throttle elapsed.</summary>
    private EcsRenderSignal? BuildSignal()
    {
        lock (_sync)
        {
            var dt = TickIntervalSeconds;
            var stopwatch = Stopwatch.StartNew();
            _systems.BeforeUpdate(in dt);
            _systems.Update(in dt);
            _systems.AfterUpdate(in dt);
            stopwatch.Stop();

            _elapsedSinceSignal += TickIntervalSeconds;
            if (_elapsedSinceSignal < SignalIntervalSeconds) return null;

            _elapsedSinceSignal = 0;
            _seq++;
            return new EcsRenderSignal(_seq, _world.Size, stopwatch.Elapsed.TotalMilliseconds, BuildSnapshot());
        }
    }

    private IReadOnlyList<SpriteState> BuildSnapshot()
    {
        var entities = new Entity[_world.Size];
        _world.GetEntities(new QueryDescription(), entities.AsSpan());

        var states = new List<SpriteState>(entities.Length);
        foreach (var entity in entities)
        {
            if (!_world.IsAlive(entity)) continue;
            states.Add(new SpriteState(
                _world.Get<RenderId>(entity).Id,
                _world.Get<Position>(entity).X,
                _world.Get<Position>(entity).Y,
                _world.Get<SpriteColor>(entity).R,
                _world.Get<SpriteColor>(entity).G,
                _world.Get<SpriteColor>(entity).B
            ));
        }

        return states;
    }

    public void Dispose()
    {
        _timer.Dispose();
        _systems.Dispose();
        World.Destroy(_world);
    }

    private float NextSingle(float min, float max) =>
        _random.NextSingle() * (max - min) + min;
}
