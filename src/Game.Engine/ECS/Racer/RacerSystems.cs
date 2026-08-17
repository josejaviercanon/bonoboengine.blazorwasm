using Arch.Core;
using Arch.Systems;

namespace Game.Engine.ECS.Racer;

internal static class RacerEcsHelpers
{
    public static Entity[] Entities(World world)
    {
        var entities = new Entity[world.Size];
        world.GetEntities(new QueryDescription(), entities.AsSpan());
        return entities;
    }

    public static Entity FindPlayer(World world, Entity[]? entities = null)
    {
        entities ??= Entities(world);
        foreach (var entity in entities)
        {
            if (world.IsAlive(entity) && world.Has<PlayerTag>(entity)) return entity;
        }
        return Entity.Null;
    }

    public static Entity FindStats(World world, Entity[]? entities = null)
    {
        entities ??= Entities(world);
        foreach (var entity in entities)
        {
            if (world.IsAlive(entity) && world.Has<RacerStatsComponent>(entity)) return entity;
        }
        return Entity.Null;
    }

    public static int SegmentIndex(float z, int segmentCount, float segmentLength)
    {
        if (segmentCount <= 0) return 0;
        var wrapped = z % (segmentCount * segmentLength);
        if (wrapped < 0f) wrapped += segmentCount * segmentLength;
        return Math.Clamp((int)MathF.Floor(wrapped / segmentLength), 0, segmentCount - 1);
    }

    public static float Increase(float start, float increment, float max)
    {
        var result = start + increment;
        while (result >= max) result -= max;
        while (result < 0f) result += max;
        return result;
    }

    public static float PercentRemaining(float value, float total)
    {
        var remainder = value % total;
        if (remainder < 0f) remainder += total;
        return remainder / total;
    }

    public static float Accelerate(float velocity, float acceleration, float dt) => velocity + acceleration * dt;

    public static bool Overlap(float x1, float width1, float x2, float width2, float percent = 1f)
    {
        var half = percent / 2f;
        var min1 = x1 - width1 * half;
        var max1 = x1 + width1 * half;
        var min2 = x2 - width2 * half;
        var max2 = x2 + width2 * half;
        return !(max1 < min2 || min1 > max2);
    }
}

/// <summary>Copies queued browser input into the player component once per fixed tick.</summary>
public sealed class RacerInputSystem : BaseSystem<World, double>
{
    private readonly Func<RacerInputRequest> _readInput;

    public RacerInputSystem(World world, Func<RacerInputRequest> readInput) : base(world)
    {
        _readInput = readInput;
    }

    public override void Update(in double t)
    {
        var entities = RacerEcsHelpers.Entities(World);
        var player = RacerEcsHelpers.FindPlayer(World, entities);
        if (player == Entity.Null) return;

        var input = _readInput();
        World.Set(player, new PlayerInputComponent(input.Left, input.Right, input.Faster, input.Slower));
    }
}

/// <summary>Integrates player position, lateral movement and speed.</summary>
public sealed class RacerPlayerControlSystem : BaseSystem<World, double>
{
    private readonly Func<RacerSettings> _readSettings;
    private readonly Func<int, float> _curveAt;
    private readonly Func<int> _segmentCount;

    public RacerPlayerControlSystem(
        World world,
        Func<RacerSettings> readSettings,
        Func<int, float> curveAt,
        Func<int> segmentCount) : base(world)
    {
        _readSettings = readSettings;
        _curveAt = curveAt;
        _segmentCount = segmentCount;
    }

    public override void Update(in double t)
    {
        var entities = RacerEcsHelpers.Entities(World);
        var player = RacerEcsHelpers.FindPlayer(World, entities);
        var statsEntity = RacerEcsHelpers.FindStats(World, entities);
        if (player == Entity.Null || statsEntity == Entity.Null) return;

        var settings = _readSettings();
        var dt = (float)t;
        var transform = World.Get<TransformComponent>(player);
        var motion = World.Get<Velocity>(player);
        var input = World.Get<PlayerInputComponent>(player);
        var stats = World.Get<RacerStatsComponent>(statsEntity);

        var startPosition = transform.Z;
        var maxSpeed = RacerConfig.MaxSpeed;
        var speedPercent = maxSpeed <= 0f ? 0f : motion.X / maxSpeed;
        var dx = dt * 2f * speedPercent;
        var playerZ = settings.CameraHeight * RacerConfig.CameraDepth(settings.FieldOfView);
        var playerSegment = RacerEcsHelpers.SegmentIndex(
            transform.Z + playerZ, _segmentCount(), RacerConfig.SegmentLength);
        var curve = _curveAt(playerSegment);

        transform.Z = RacerEcsHelpers.Increase(transform.Z, dt * motion.X,
            _segmentCount() * RacerConfig.SegmentLength);

        if (input.IsLeft)
        {
            transform.X -= dx;
        }
        else if (input.IsRight)
        {
            transform.X += dx;
        }

        transform.X -= dx * speedPercent * curve * RacerConfig.Centrifugal;

        if (input.IsFaster)
        {
            motion.X = RacerEcsHelpers.Accelerate(motion.X, RacerConfig.Acceleration, dt);
        }
        else if (input.IsSlower)
        {
            motion.X = RacerEcsHelpers.Accelerate(motion.X, RacerConfig.Braking, dt);
        }
        else
        {
            motion.X = RacerEcsHelpers.Accelerate(motion.X, RacerConfig.Deceleration, dt);
        }

        if ((transform.X < -1f || transform.X > 1f) && motion.X > RacerConfig.OffRoadLimit)
        {
            motion.X = RacerEcsHelpers.Accelerate(motion.X, RacerConfig.OffRoadDeceleration, dt);
        }

        transform.X = Math.Clamp(transform.X, -3f, 3f);
        motion.X = Math.Clamp(motion.X, 0f, maxSpeed);
        stats.PreviousPosition = startPosition;

        World.Set(player, transform);
        World.Set(player, motion);
        World.Set(statsEntity, stats);
    }
}

/// <summary>Advances AI traffic and ports the reference steering heuristic.</summary>
public sealed class RacerTrafficSystem : BaseSystem<World, double>
{
    private readonly Func<RacerSettings> _readSettings;
    private readonly Func<int> _segmentCount;

    public RacerTrafficSystem(World world, Func<RacerSettings> readSettings, Func<int> segmentCount) : base(world)
    {
        _readSettings = readSettings;
        _segmentCount = segmentCount;
    }

    public override void Update(in double t)
    {
        var entities = RacerEcsHelpers.Entities(World);
        var player = RacerEcsHelpers.FindPlayer(World, entities);
        if (player == Entity.Null) return;

        var playerTransform = World.Get<TransformComponent>(player);
        var playerMotion = World.Get<Velocity>(player);
        var settings = _readSettings();
        var count = _segmentCount();
        if (count == 0) return;

        var playerZ = settings.CameraHeight * RacerConfig.CameraDepth(settings.FieldOfView);
        var playerSegment = RacerEcsHelpers.SegmentIndex(
            playerTransform.Z + playerZ, count, RacerConfig.SegmentLength);
        var playerWidth = RacerConfig.PlayerSpriteWidth * RacerConfig.SpriteScale;
        var cars = new List<Entity>(RacerConfig.TotalCars);
        foreach (var entity in entities)
        {
            if (World.IsAlive(entity) && World.Has<AICarComponent>(entity)) cars.Add(entity);
        }

        var dt = (float)t;
        foreach (var carEntity in cars)
        {
            var transform = World.Get<TransformComponent>(carEntity);
            var car = World.Get<AICarComponent>(carEntity);
            var oldSegment = RacerEcsHelpers.SegmentIndex(transform.Z, count, RacerConfig.SegmentLength);
            car.Offset += UpdateCarOffset(carEntity, oldSegment, playerSegment, playerTransform.X,
                playerMotion.X, playerWidth, cars, settings.DrawDistance, count);
            transform.Z = RacerEcsHelpers.Increase(transform.Z, dt * car.Speed,
                count * RacerConfig.SegmentLength);
            car.Percent = RacerEcsHelpers.PercentRemaining(transform.Z, RacerConfig.SegmentLength);
            World.Set(carEntity, transform);
            World.Set(carEntity, car);
        }
    }

    private float UpdateCarOffset(
        Entity carEntity,
        int carSegment,
        int playerSegment,
        float playerX,
        float playerSpeed,
        float playerWidth,
        List<Entity> cars,
        int drawDistance,
        int segmentCount)
    {
        var car = World.Get<AICarComponent>(carEntity);
        var carWidth = CarWidth(carEntity);

        if (carSegment - playerSegment > drawDistance) return 0f;

        for (var i = 1; i < 20; i++)
        {
            var segment = (carSegment + i) % segmentCount;
            if (segment == playerSegment && car.Speed > playerSpeed &&
                RacerEcsHelpers.Overlap(playerX, playerWidth, car.Offset, carWidth, 1.2f))
            {
                var direction = car.Offset > playerX ? 1f : -1f;
                if (playerX > 0.5f) direction = -1f;
                else if (playerX < -0.5f) direction = 1f;
                return direction * (1f / i) * (car.Speed - playerSpeed) / RacerConfig.MaxSpeed;
            }

            foreach (var otherEntity in cars)
            {
                if (otherEntity == carEntity || !World.IsAlive(otherEntity)) continue;
                var otherTransform = World.Get<TransformComponent>(otherEntity);
                if (RacerEcsHelpers.SegmentIndex(otherTransform.Z, segmentCount, RacerConfig.SegmentLength) != segment)
                    continue;

                var other = World.Get<AICarComponent>(otherEntity);
                var otherWidth = CarWidth(otherEntity);
                if (car.Speed <= other.Speed ||
                    !RacerEcsHelpers.Overlap(car.Offset, carWidth, other.Offset, otherWidth, 1.2f)) continue;

                var direction = car.Offset > other.Offset ? 1f : -1f;
                if (other.Offset > 0.5f) direction = -1f;
                else if (other.Offset < -0.5f) direction = 1f;
                return direction * (1f / i) * (car.Speed - other.Speed) / RacerConfig.MaxSpeed;
            }
        }

        if (car.Offset < -0.9f) return 0.1f;
        if (car.Offset > 0.9f) return -0.1f;
        return 0f;
    }

    private float CarWidth(Entity entity)
    {
        var sprite = World.Get<SpriteComponent>(entity);
        return sprite.Width * RacerConfig.SpriteScale;
    }
}

/// <summary>Resolves roadside and traffic collisions against the player.</summary>
public sealed class RacerCollisionSystem : BaseSystem<World, double>
{
    private readonly Func<RacerSettings> _readSettings;
    private readonly Func<int> _segmentCount;
    private readonly Func<int, float> _segmentP1Z;

    public RacerCollisionSystem(
        World world,
        Func<RacerSettings> readSettings,
        Func<int> segmentCount,
        Func<int, float> segmentP1Z) : base(world)
    {
        _readSettings = readSettings;
        _segmentCount = segmentCount;
        _segmentP1Z = segmentP1Z;
    }

    public override void Update(in double t)
    {
        var entities = RacerEcsHelpers.Entities(World);
        var player = RacerEcsHelpers.FindPlayer(World, entities);
        var statsEntity = RacerEcsHelpers.FindStats(World, entities);
        if (player == Entity.Null || statsEntity == Entity.Null) return;

        var settings = _readSettings();
        var count = _segmentCount();
        var playerTransform = World.Get<TransformComponent>(player);
        var playerMotion = World.Get<Velocity>(player);
        var stats = World.Get<RacerStatsComponent>(statsEntity);
        stats.Collided = false;

        var playerZ = settings.CameraHeight * RacerConfig.CameraDepth(settings.FieldOfView);
        var playerSegment = RacerEcsHelpers.SegmentIndex(
            playerTransform.Z + playerZ, count, RacerConfig.SegmentLength);
        var playerWidth = RacerConfig.PlayerSpriteWidth * RacerConfig.SpriteScale;

        if (playerTransform.X < -1f || playerTransform.X > 1f)
        {
            foreach (var entity in entities)
            {
                if (!World.IsAlive(entity) || !World.Has<RoadSpriteComponent>(entity)) continue;
                var roadSprite = World.Get<RoadSpriteComponent>(entity);
                if (roadSprite.SegmentIndex != playerSegment || !World.Has<SpriteComponent>(entity)) continue;

                var sprite = World.Get<SpriteComponent>(entity);
                var spriteWidth = sprite.Width * RacerConfig.SpriteScale;
                var spriteCenter = roadSprite.Offset +
                    spriteWidth * 0.5f * (roadSprite.Offset > 0f ? 1f : -1f);
                if (!RacerEcsHelpers.Overlap(playerTransform.X, playerWidth, spriteCenter, spriteWidth)) continue;

                playerMotion.X = RacerConfig.MaxSpeed / 5f;
                playerTransform.Z = RacerEcsHelpers.Increase(
                    _segmentP1Z(playerSegment), -playerZ, count * RacerConfig.SegmentLength);
                stats.Collided = true;
                break;
            }
        }

        if (!stats.Collided)
        {
            foreach (var entity in entities)
            {
                if (!World.IsAlive(entity) || !World.Has<AICarComponent>(entity)) continue;
                var carTransform = World.Get<TransformComponent>(entity);
                var car = World.Get<AICarComponent>(entity);
                if (RacerEcsHelpers.SegmentIndex(carTransform.Z, count, RacerConfig.SegmentLength) != playerSegment ||
                    playerMotion.X <= car.Speed) continue;

                var carWidth = World.Get<SpriteComponent>(entity).Width * RacerConfig.SpriteScale;
                if (!RacerEcsHelpers.Overlap(playerTransform.X, playerWidth, car.Offset, carWidth, 0.8f)) continue;

                playerMotion.X = car.Speed <= 0f ? 0f : car.Speed * (car.Speed / playerMotion.X);
                playerTransform.Z = RacerEcsHelpers.Increase(
                    carTransform.Z, -playerZ, count * RacerConfig.SegmentLength);
                stats.Collided = true;
                break;
            }
        }

        World.Set(player, playerTransform);
        World.Set(player, playerMotion);
        World.Set(statsEntity, stats);
    }
}

/// <summary>Tracks lap time and emits one-shot lap completion state.</summary>
public sealed class RacerLapSystem : BaseSystem<World, double>
{
    private readonly Func<RacerSettings> _readSettings;

    public RacerLapSystem(World world, Func<RacerSettings> readSettings) : base(world)
    {
        _readSettings = readSettings;
    }

    public override void Update(in double t)
    {
        var entities = RacerEcsHelpers.Entities(World);
        var player = RacerEcsHelpers.FindPlayer(World, entities);
        var statsEntity = RacerEcsHelpers.FindStats(World, entities);
        if (player == Entity.Null || statsEntity == Entity.Null) return;

        var transform = World.Get<TransformComponent>(player);
        var stats = World.Get<RacerStatsComponent>(statsEntity);
        var settings = _readSettings();
        var playerZ = settings.CameraHeight * RacerConfig.CameraDepth(settings.FieldOfView);
        stats.LapCompleted = false;

        if (transform.Z > playerZ)
        {
            if (stats.CurrentLapTime > 0f && stats.PreviousPosition < playerZ)
            {
                stats.LastLapTime = stats.CurrentLapTime;
                stats.FastLapTime = MathF.Min(stats.FastLapTime, stats.LastLapTime);
                stats.CurrentLapTime = 0f;
                stats.Lap++;
                stats.LapCompleted = true;
            }
            else
            {
                stats.CurrentLapTime += (float)t;
            }
        }

        World.Set(statsEntity, stats);
    }
}
