using System.Diagnostics;
using Arch.Core;
using Arch.Systems;
using Game.Engine.ECS.Systems;

namespace Game.Engine.ECS.Racer;

/// <summary>
///     Authoritative pseudo-3D racer simulation. C# owns input validation, motion,
///     traffic, collisions and lap timing; the browser receives batched snapshots.
/// </summary>
public sealed class RacerSimulation : IDisposable
{
    private static readonly RacerSpriteKind[] BillboardSprites =
    {
        RacerSpriteKind.Billboard01, RacerSpriteKind.Billboard02, RacerSpriteKind.Billboard03,
        RacerSpriteKind.Billboard04, RacerSpriteKind.Billboard05, RacerSpriteKind.Billboard06,
        RacerSpriteKind.Billboard07, RacerSpriteKind.Billboard08, RacerSpriteKind.Billboard09,
    };

    private static readonly RacerSpriteKind[] PlantSprites =
    {
        RacerSpriteKind.Tree1, RacerSpriteKind.Tree2, RacerSpriteKind.DeadTree1,
        RacerSpriteKind.DeadTree2, RacerSpriteKind.PalmTree, RacerSpriteKind.Bush1,
        RacerSpriteKind.Bush2, RacerSpriteKind.Cactus, RacerSpriteKind.Stump,
        RacerSpriteKind.Boulder1, RacerSpriteKind.Boulder2, RacerSpriteKind.Boulder3,
    };

    private static readonly RacerSpriteKind[] CarSprites =
    {
        RacerSpriteKind.Car01, RacerSpriteKind.Car02, RacerSpriteKind.Car03,
        RacerSpriteKind.Car04, RacerSpriteKind.Semi, RacerSpriteKind.Truck,
    };

    private readonly World _world;
    private readonly Random _random;
    private readonly object _sync = new();
    private readonly Timer? _timer;
    private readonly List<Entity> _roadSegments = new();
    private readonly List<Entity> _roadSprites = new();
    private Group<double> _systems = null!;
    private RacerSettings _settings;
    private RacerInputRequest _input;
    private float _lastRoadY;
    private float _trackLength;
    private long _seq;
    private long _epoch;
    private bool _paused;

    private readonly IRenderTransport<RacerRenderSignal> _renderTransport;

    /// <summary>
    ///     Batched render signal, delivered through the <see cref="IRenderTransport{TSignal}"/> seam
    ///     (ADR-007). Forwarding event keeps the SSE host subscription contract unchanged.
    /// </summary>
    public event Action<RacerRenderSignal>? OnRenderSignal
    {
        add => _renderTransport.OnSignal += value;
        remove => _renderTransport.OnSignal -= value;
    }

    public RacerSimulation() : this(new Random(), startTimer: true)
    {
    }

    public RacerSimulation(int seed) : this(new Random(seed), startTimer: true)
    {
    }

    public RacerSimulation(int seed, bool startTimer) : this(new Random(seed), startTimer)
    {
    }

    public RacerSimulation(Random random) : this(random, startTimer: true)
    {
    }

    public RacerSimulation(Random random, bool startTimer,
        IRenderTransport<RacerRenderSignal>? renderTransport = null)
    {
        _renderTransport = renderTransport ?? new ServerRenderTransport<RacerRenderSignal>();
        _random = random;
        _settings = RacerConfig.DefaultSettings;
        _world = World.Create();
        BuildWorld();

        _systems = new Group<double>(
            "Racer",
            new RacerInputSystem(_world, ReadInput),
            new RacerTrafficSystem(_world, ReadSettings, () => _roadSegments.Count),
            new RacerPlayerControlSystem(_world, ReadSettings, CurveAt, () => _roadSegments.Count),
            new RacerCollisionSystem(_world, ReadSettings, () => _roadSegments.Count, SegmentP1Z),
            new RacerLapSystem(_world, ReadSettings));
        _systems.Initialize();

        if (startTimer)
        {
            _timer = new Timer(Tick, null, TimeSpan.Zero, TimeSpan.FromSeconds(RacerConfig.TickIntervalSeconds));
        }
    }

    public RacerSettings Settings
    {
        get
        {
            lock (_sync) return _settings;
        }
    }

    public float TrackLength
    {
        get
        {
            lock (_sync) return _trackLength;
        }
    }

    public void QueueInput(RacerInputRequest input)
    {
        lock (_sync) _input = input;
    }

    public void ApplyInput(RacerInputRequest input) => QueueInput(input);

    public bool IsPaused
    {
        get
        {
            lock (_sync) return _paused;
        }
    }

    public void Pause()
    {
        lock (_sync) _paused = true;
    }

    public void Resume()
    {
        lock (_sync) _paused = false;
    }

    public void ApplyConfig(RacerConfigRequest request)
    {
        lock (_sync)
        {
            _settings = new RacerSettings(
                Math.Clamp(request.Lanes, 1, 4),
                ClampFinite(request.RoadWidth, 500f, 3000f, RacerConfig.DefaultRoadWidth),
                ClampFinite(request.CameraHeight, 500f, 5000f, RacerConfig.DefaultCameraHeight),
                Math.Clamp(request.DrawDistance, 100, 500),
                ClampFinite(request.FieldOfView, 80f, 140f, RacerConfig.DefaultFieldOfView),
                ClampFinite(request.FogDensity, 0f, 50f, RacerConfig.DefaultFogDensity),
                ClampFinite(request.ResolutionScale, 0.4f, 1.5f, 1f));
        }
    }

    public void Restart()
    {
        lock (_sync)
        {
            DestroyWorld();
            _input = default;
            _seq = 0;
            _epoch++;
            _paused = false;
            BuildWorld();
        }
    }

    public void Reset() => Restart();

    public RacerTrackPayload TrackSnapshot()
    {
        lock (_sync)
        {
            return BuildTrackSnapshot();
        }
    }

    public RacerPlayerState PlayerSnapshot()
    {
        lock (_sync)
        {
            return BuildPlayerSnapshot();
        }
    }

    public IReadOnlyList<RacerCarState> SnapshotCars()
    {
        lock (_sync)
        {
            return BuildCarSnapshot(includeAll: true);
        }
    }

    /// <summary>Runs one fixed authoritative tick. Exposed for deterministic tests.</summary>
    public void StepOnce()
    {
        lock (_sync)
        {
            StepOnceLocked();
        }
    }

    private void Tick(object? state) => StepOnce();

    private void StepOnceLocked()
    {
        if (_paused) return;

        var stopwatch = Stopwatch.StartNew();
        var dt = RacerConfig.TickIntervalSeconds;
        _systems.BeforeUpdate(in dt);
        _systems.Update(in dt);
        _systems.AfterUpdate(in dt);
        stopwatch.Stop();

        var statsEntity = RacerEcsHelpers.FindStats(_world);
        var stats = statsEntity == Entity.Null
            ? default
            : _world.Get<RacerStatsComponent>(statsEntity);

        _seq++;
        var signal = new RacerRenderSignal(
            _seq,
            _world.Size,
            stopwatch.Elapsed.TotalMilliseconds,
            BuildPlayerSnapshot(),
            BuildCarSnapshot(includeAll: false),
            _settings,
            stats.LapCompleted,
            stats.Collided)
        { Epoch = _epoch };

        if (statsEntity != Entity.Null)
        {
            stats.LapCompleted = false;
            stats.Collided = false;
            _world.Set(statsEntity, stats);
        }

        _renderTransport.Push(signal);
    }

    private RacerInputRequest ReadInput()
    {
        lock (_sync) return _input;
    }

    private RacerSettings ReadSettings() => _settings;

    private float CurveAt(int index)
    {
        if (_roadSegments.Count == 0) return 0f;
        index = Math.Clamp(index, 0, _roadSegments.Count - 1);
        return _world.Get<RoadSegmentComponent>(_roadSegments[index]).Curve;
    }

    private float SegmentP1Z(int index) => index * RacerConfig.SegmentLength;

    private void BuildWorld()
    {
        _roadSegments.Clear();
        _roadSprites.Clear();
        _lastRoadY = 0f;
        BuildRoad();
        BuildSprites();
        BuildCars();

        _world.Create(
            new PlayerTag(),
            new RenderTag(),
            new TransformComponent(0f, 0f, 0f),
            new Velocity(0f, 0f),
            new PlayerInputComponent(false, false, false, false),
            new BoundingBoxComponent(RacerConfig.PlayerSpriteWidth * RacerConfig.SpriteScale, 1f));
        _world.Create(new RacerStatsComponent(180f));
        _trackLength = _roadSegments.Count * RacerConfig.SegmentLength;
    }

    private void DestroyWorld()
    {
        var entities = RacerEcsHelpers.Entities(_world);
        foreach (var entity in entities)
        {
            if (_world.IsAlive(entity)) _world.Destroy(entity);
        }
    }

    private void BuildRoad()
    {
        AddStraight(RoadLength.Short);
        AddLowRollingHills();
        AddSCurves();
        AddCurve(RoadLength.Medium, RoadCurve.Medium, RoadHill.Low);
        AddBumps();
        AddLowRollingHills();
        AddCurve(RoadLength.Long * 2, RoadCurve.Medium, RoadHill.Medium);
        AddStraight();
        AddHill(RoadLength.Medium, RoadHill.High);
        AddSCurves();
        AddCurve(RoadLength.Long, -RoadCurve.Medium, RoadHill.None);
        AddHill(RoadLength.Long, RoadHill.High);
        AddCurve(RoadLength.Long, RoadCurve.Medium, -RoadHill.Low);
        AddBumps();
        AddHill(RoadLength.Long, -RoadHill.Medium);
        AddStraight();
        AddSCurves();
        AddDownhillToEnd();

        var playerZ = _settings.CameraHeight * RacerConfig.CameraDepth(_settings.FieldOfView);
        var playerSegment = RacerEcsHelpers.SegmentIndex(playerZ, _roadSegments.Count, RacerConfig.SegmentLength);
        SetSegmentColor(playerSegment + 2, RacerSegmentColor.Start);
        SetSegmentColor(playerSegment + 3, RacerSegmentColor.Start);
        for (var n = 0; n < RacerConfig.RumbleLength; n++)
        {
            SetSegmentColor(_roadSegments.Count - 1 - n, RacerSegmentColor.Finish);
        }
    }

    private void AddSegment(float curve, float y)
    {
        var index = _roadSegments.Count;
        var color = ((index / RacerConfig.RumbleLength) % 2) == 1
            ? RacerSegmentColor.Dark
            : RacerSegmentColor.Light;
        _roadSegments.Add(_world.Create(
            new RoadSegmentComponent(index, _lastRoadY, y, curve, color),
            new RenderTag()));
        _lastRoadY = y;
    }

    private void AddRoad(int enter, int hold, int leave, float curve, int y)
    {
        var startY = _lastRoadY;
        var endY = startY + y * RacerConfig.SegmentLength;
        var total = enter + hold + leave;
        for (var n = 0; n < enter; n++)
        {
            AddSegment(EaseIn(0f, curve, n / (float)enter),
                EaseInOut(startY, endY, n / (float)total));
        }
        for (var n = 0; n < hold; n++)
        {
            AddSegment(curve, EaseInOut(startY, endY, (enter + n) / (float)total));
        }
        for (var n = 0; n < leave; n++)
        {
            AddSegment(EaseInOut(curve, 0f, n / (float)leave),
                EaseInOut(startY, endY, (enter + hold + n) / (float)total));
        }
    }

    private void AddStraight(int num = RoadLength.Medium) => AddRoad(num, num, num, 0f, 0);

    private void AddHill(int num = RoadLength.Medium, int height = RoadHill.Medium) =>
        AddRoad(num, num, num, 0f, height);

    private void AddCurve(int num = RoadLength.Medium, int curve = RoadCurve.Medium, int height = RoadHill.None) =>
        AddRoad(num, num, num, curve, height);

    private void AddLowRollingHills(int num = RoadLength.Short, int height = RoadHill.Low)
    {
        AddRoad(num, num, num, 0f, height / 2);
        AddRoad(num, num, num, 0f, -height);
        AddRoad(num, num, num, RoadCurve.Easy, height);
        AddRoad(num, num, num, 0f, 0);
        AddRoad(num, num, num, -RoadCurve.Easy, height / 2);
        AddRoad(num, num, num, 0f, 0);
    }

    private void AddSCurves()
    {
        AddRoad(RoadLength.Medium, RoadLength.Medium, RoadLength.Medium, -RoadCurve.Easy, RoadHill.None);
        AddRoad(RoadLength.Medium, RoadLength.Medium, RoadLength.Medium, RoadCurve.Medium, RoadHill.Medium);
        AddRoad(RoadLength.Medium, RoadLength.Medium, RoadLength.Medium, RoadCurve.Easy, -RoadHill.Low);
        AddRoad(RoadLength.Medium, RoadLength.Medium, RoadLength.Medium, -RoadCurve.Easy, RoadHill.Medium);
        AddRoad(RoadLength.Medium, RoadLength.Medium, RoadLength.Medium, -RoadCurve.Medium, -RoadHill.Medium);
    }

    private void AddBumps()
    {
        AddRoad(10, 10, 10, 0f, 5);
        AddRoad(10, 10, 10, 0f, -2);
        AddRoad(10, 10, 10, 0f, -5);
        AddRoad(10, 10, 10, 0f, 8);
        AddRoad(10, 10, 10, 0f, 5);
        AddRoad(10, 10, 10, 0f, -7);
        AddRoad(10, 10, 10, 0f, 5);
        AddRoad(10, 10, 10, 0f, -2);
    }

    private void AddDownhillToEnd(int num = 200) =>
        AddRoad(num, num, num, -RoadCurve.Easy, -(int)(_lastRoadY / RacerConfig.SegmentLength));

    private void SetSegmentColor(int index, RacerSegmentColor color)
    {
        if (_roadSegments.Count == 0) return;
        index %= _roadSegments.Count;
        if (index < 0) index += _roadSegments.Count;
        var entity = _roadSegments[index];
        var segment = _world.Get<RoadSegmentComponent>(entity);
        segment.Color = (byte)color;
        _world.Set(entity, segment);
    }

    private void BuildSprites()
    {
        AddSprite(20, RacerSpriteKind.Billboard07, -1f);
        AddSprite(40, RacerSpriteKind.Billboard06, -1f);
        AddSprite(60, RacerSpriteKind.Billboard08, -1f);
        AddSprite(80, RacerSpriteKind.Billboard09, -1f);
        AddSprite(100, RacerSpriteKind.Billboard01, -1f);
        AddSprite(120, RacerSpriteKind.Billboard02, -1f);
        AddSprite(140, RacerSpriteKind.Billboard03, -1f);
        AddSprite(160, RacerSpriteKind.Billboard04, -1f);
        AddSprite(180, RacerSpriteKind.Billboard05, -1f);

        AddSprite(240, RacerSpriteKind.Billboard07, -1.2f);
        AddSprite(240, RacerSpriteKind.Billboard06, 1.2f);
        AddSprite(_roadSegments.Count - 25, RacerSpriteKind.Billboard07, -1.2f);
        AddSprite(_roadSegments.Count - 25, RacerSpriteKind.Billboard06, 1.2f);

        for (var n = 10; n < 200; n += 4 + n / 100)
        {
            AddSprite(n, RacerSpriteKind.PalmTree, 0.5f + NextFloat() * 0.5f);
            AddSprite(n, RacerSpriteKind.PalmTree, 1f + NextFloat() * 2f);
        }

        for (var n = 250; n < 1000; n += 5)
        {
            AddSprite(n, RacerSpriteKind.Column, 1.1f);
            AddSprite(n + NextInt(0, 5), RacerSpriteKind.Tree1, -1f - NextFloat() * 2f);
            AddSprite(n + NextInt(0, 5), RacerSpriteKind.Tree2, -1f - NextFloat() * 2f);
        }

        for (var n = 200; n < _roadSegments.Count; n += 3)
        {
            AddSprite(n, Choice(PlantSprites), Choice(new[] { 1f, -1f }) * (2f + NextFloat() * 5f));
        }

        for (var n = 1000; n < _roadSegments.Count - 50; n += 100)
        {
            var side = Choice(new[] { 1f, -1f });
            AddSprite(n + NextInt(0, 50), Choice(BillboardSprites), -side);
            for (var i = 0; i < 20; i++)
            {
                AddSprite(n + NextInt(0, 50), Choice(PlantSprites), side * (1.5f + NextFloat()));
            }
        }
    }

    private void AddSprite(int segmentIndex, RacerSpriteKind kind, float offset)
    {
        if (segmentIndex < 0 || segmentIndex >= _roadSegments.Count) return;
        _roadSprites.Add(_world.Create(
            new RoadSpriteComponent(segmentIndex, offset),
            new SpriteComponent(kind),
            new RenderTag()));
    }

    private void BuildCars()
    {
        for (var n = 0; n < RacerConfig.TotalCars; n++)
        {
            var offset = NextFloat() * Choice(new[] { -0.8f, 0.8f });
            var z = NextInt(0, _roadSegments.Count - 1) * RacerConfig.SegmentLength;
            var sprite = Choice(CarSprites);
            var speed = RacerConfig.MaxSpeed / 4f +
                NextFloat() * RacerConfig.MaxSpeed / (sprite == RacerSpriteKind.Semi ? 4f : 2f);
            _world.Create(
                new RenderId(n),
                new RenderTag(),
                new TransformComponent(0f, 0f, z),
                new AICarComponent(offset, speed, sprite),
                new SpriteComponent(sprite),
                new BoundingBoxComponent(RacerConfig.SpriteWidth(sprite) * RacerConfig.SpriteScale, 1f));
        }
    }

    private RacerTrackPayload BuildTrackSnapshot()
    {
        var segments = new List<RacerSegmentState>(_roadSegments.Count);
        foreach (var entity in _roadSegments)
        {
            var segment = _world.Get<RoadSegmentComponent>(entity);
            segments.Add(new RacerSegmentState(
                segment.Index, segment.P1WorldY, segment.P2WorldY, segment.Curve, segment.Color));
        }

        var sprites = new List<RacerSceneryState>(_roadSprites.Count);
        foreach (var entity in _roadSprites)
        {
            var roadSprite = _world.Get<RoadSpriteComponent>(entity);
            var sprite = _world.Get<SpriteComponent>(entity);
            sprites.Add(new RacerSceneryState(roadSprite.SegmentIndex, roadSprite.Offset, sprite.Kind));
        }

        return new RacerTrackPayload(
            segments,
            sprites,
            _trackLength,
            RacerConfig.SegmentLength,
            RacerConfig.RumbleLength);
    }

    private RacerPlayerState BuildPlayerSnapshot()
    {
        var entities = RacerEcsHelpers.Entities(_world);
        var player = RacerEcsHelpers.FindPlayer(_world, entities);
        var statsEntity = RacerEcsHelpers.FindStats(_world, entities);
        if (player == Entity.Null || statsEntity == Entity.Null)
        {
            return new RacerPlayerState(0f, 0f, 0f, 0f, 0f, 180f, 0, 0, false);
        }

        var transform = _world.Get<TransformComponent>(player);
        var motion = _world.Get<Velocity>(player);
        var input = _world.Get<PlayerInputComponent>(player);
        var stats = _world.Get<RacerStatsComponent>(statsEntity);
        var playerZ = _settings.CameraHeight * RacerConfig.CameraDepth(_settings.FieldOfView);
        var segmentIndex = RacerEcsHelpers.SegmentIndex(
            transform.Z + playerZ, _roadSegments.Count, RacerConfig.SegmentLength);
        var segment = _world.Get<RoadSegmentComponent>(_roadSegments[segmentIndex]);
        var steer = input.IsLeft ? -1 : input.IsRight ? 1 : 0;
        return new RacerPlayerState(
            transform.X,
            transform.Z,
            motion.X,
            stats.CurrentLapTime,
            stats.LastLapTime,
            stats.FastLapTime,
            stats.Lap,
            steer,
            segment.P2WorldY > segment.P1WorldY);
    }

    private IReadOnlyList<RacerCarState> BuildCarSnapshot(bool includeAll)
    {
        var entities = RacerEcsHelpers.Entities(_world);
        var player = RacerEcsHelpers.FindPlayer(_world, entities);
        var playerZ = player == Entity.Null ? 0f : _world.Get<TransformComponent>(player).Z;
        var maxDistance = _settings.DrawDistance * RacerConfig.SegmentLength;
        var cars = new List<RacerCarState>(RacerConfig.TotalCars);

        foreach (var entity in entities)
        {
            if (!_world.IsAlive(entity) || !_world.Has<AICarComponent>(entity)) continue;
            var transform = _world.Get<TransformComponent>(entity);
            var car = _world.Get<AICarComponent>(entity);
            var relative = RacerEcsHelpers.Increase(transform.Z - playerZ, 0f, _trackLength);
            if (!includeAll && relative > maxDistance) continue;
            cars.Add(new RacerCarState(
                _world.Get<RenderId>(entity).Id,
                transform.Z,
                car.Offset,
                car.Speed,
                car.Percent,
                car.SpriteKind));
        }

        cars.Sort((a, b) =>
        {
            var aDistance = RacerEcsHelpers.Increase(a.Z - playerZ, 0f, _trackLength);
            var bDistance = RacerEcsHelpers.Increase(b.Z - playerZ, 0f, _trackLength);
            return bDistance.CompareTo(aDistance);
        });
        return cars;
    }

    private float NextFloat() => (float)_random.NextDouble();

    private int NextInt(int minInclusive, int maxInclusive) =>
        _random.Next(minInclusive, maxInclusive + 1);

    private T Choice<T>(IReadOnlyList<T> values) => values[NextInt(0, values.Count - 1)];

    private static float ClampFinite(float value, float min, float max, float fallback) =>
        float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    private static float EaseIn(float a, float b, float percent) => a + (b - a) * percent * percent;

    private static float EaseInOut(float a, float b, float percent) =>
        a + (b - a) * ((-MathF.Cos(percent * MathF.PI) / 2f) + 0.5f);

    private static class RoadLength
    {
        public const int Short = 25;
        public const int Medium = 50;
        public const int Long = 100;
    }

    private static class RoadHill
    {
        public const int None = 0;
        public const int Low = 20;
        public const int Medium = 40;
        public const int High = 60;
    }

    private static class RoadCurve
    {
        public const int None = 0;
        public const int Easy = 2;
        public const int Medium = 4;
        public const int Hard = 6;
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _systems.Dispose();
        World.Destroy(_world);
    }
}
