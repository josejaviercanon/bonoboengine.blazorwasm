using Arch.Core;
using Arch.Systems;
using Box2D.NET;
using static Box2D.NET.B2Types;
using static Box2D.NET.B2Bodies;
using static Box2D.NET.B2Shapes;
using static Box2D.NET.B2MathFunction;
using static Box2D.NET.B2Worlds;

namespace Game.Engine.ECS.Systems;

/// <summary>Client-suggested asteroid input. Held flags are absolute state (no lost
/// key-up edges); Fire and Hyperspace are one-shot edges consumed by the gameplay system.</summary>
public sealed class AsteroidsInputState
{
    public bool Thrust;
    public bool Left;
    public bool Right;
    public bool Fire;
    public bool Hyperspace;
}

/// <summary>Entity lookup helpers shared by the asteroid systems.</summary>
public static class AsteroidsHelpers
{
    public static Entity FindStats(World world)
    {
        var entities = new Entity[world.Size];
        world.GetEntities(new QueryDescription(), entities.AsSpan());
        foreach (var entity in entities)
        {
            if (world.IsAlive(entity) && world.Has<AsteroidsStats>(entity)) return entity;
        }
        return Entity.Null;
    }

    public static Entity FindShip(World world)
    {
        var entities = new Entity[world.Size];
        world.GetEntities(new QueryDescription(), entities.AsSpan());
        foreach (var entity in entities)
        {
            if (world.IsAlive(entity) && world.Has<ShipTag>(entity)) return entity;
        }
        return Entity.Null;
    }

    public static Entity FindSaucer(World world)
    {
        var entities = new Entity[world.Size];
        world.GetEntities(new QueryDescription(), entities.AsSpan());
        foreach (var entity in entities)
        {
            if (world.IsAlive(entity) && world.Has<SaucerTag>(entity)) return entity;
        }
        return Entity.Null;
    }

    public static Entity FindMissile(World world)
    {
        var entities = new Entity[world.Size];
        world.GetEntities(new QueryDescription(), entities.AsSpan());
        foreach (var entity in entities)
        {
            if (world.IsAlive(entity) && world.Has<MissileTag>(entity)) return entity;
        }
        return Entity.Null;
    }

    public static List<Entity> FindAsteroids(World world)
    {
        var asteroids = new List<Entity>();
        var entities = new Entity[world.Size];
        world.GetEntities(new QueryDescription(), entities.AsSpan());
        foreach (var entity in entities)
        {
            if (world.IsAlive(entity) && world.Has<AsteroidTag>(entity)) asteroids.Add(entity);
        }
        return asteroids;
    }

    public static int CountBullets(World world)
    {
        var count = 0;
        var entities = new Entity[world.Size];
        world.GetEntities(new QueryDescription(), entities.AsSpan());
        foreach (var entity in entities)
        {
            if (world.IsAlive(entity) && world.Has<BulletTag>(entity)) count++;
        }
        return count;
    }

    public static int CountExplosions(World world)
    {
        var count = 0;
        var entities = new Entity[world.Size];
        world.GetEntities(new QueryDescription(), entities.AsSpan());
        foreach (var entity in entities)
        {
            if (world.IsAlive(entity) && world.Has<ExplosionTag>(entity)) count++;
        }
        return count;
    }
}

/// <summary>
///     Shared simulation context: the Box2D world (authoritative physics, ADR-002),
///     the seeded random source and the entity-id index used to resolve contact
///     events back to Arch entities. Owned by <see cref="AsteroidsSimulation"/>.
/// </summary>
public sealed class AsteroidsContext : IDisposable
{
    public readonly B2WorldId World;
    public readonly Random Random;
    public readonly AsteroidsInputState Input = new();

    private readonly Dictionary<int, Entity> _byId = new();
    private int _nextRenderId = 1;

    public AsteroidsContext(Random random)
    {
        Random = random;

        var def = b2DefaultWorldDef();
        def.gravity = new B2Vec2(0f, 0f);
        def.enableSleep = false;
        def.workerCount = 1;
        World = b2CreateWorld(def);
    }

    /// <summary>Next unique render id for a spawned entity (client sprite key).</summary>
    public int NextRenderId() => _nextRenderId++;

    public void Register(Entity entity) => _byId[entity.Id] = entity;

    public void Unregister(int entityId) => _byId.Remove(entityId);

    public Entity Resolve(World world, int entityId) =>
        _byId.TryGetValue(entityId, out var entity) && world.IsAlive(entity) ? entity : Entity.Null;

    public void Dispose()
    {
        if (World.index1 != 0 || World.generation != 0)
        {
            b2DestroyWorld(World);
        }
        _byId.Clear();
    }
}

/// <summary>
///     Authoritative asteroids gameplay. One Arch system per fixed tick:
///     (1) input + ship control, (2) saucer traverse + guided missile, (3) Box2D
///     sync + step + contact resolution + wrap, (4) explosion/bullet aging and the
///     respawn/belt/scoring flow. C# is the sole authority (ADR-001/002/006).
/// </summary>
public partial class AsteroidsGameSystem : BaseSystem<World, double>
{
    private readonly AsteroidsContext _ctx;
    private int _scoreAtTickStart;

    public AsteroidsGameSystem(World world, AsteroidsContext ctx) : base(world)
    {
        _ctx = ctx;
    }

    public override void BeforeUpdate(in double dt)
    {
        var statsEntity = AsteroidsHelpers.FindStats(World);
        if (statsEntity != Entity.Null)
        {
            _scoreAtTickStart = World.Get<AsteroidsStats>(statsEntity).Score;
        }
    }

    public override void Update(in double dt)
    {
        var dtf = (float)dt;
        var input = _ctx.Input;
        var world = World;

        var statsEntity = AsteroidsHelpers.FindStats(world);
        if (statsEntity == Entity.Null) return;
        var stats = world.Get<AsteroidsStats>(statsEntity);
        var running = stats.Started && !stats.GameOver;

        // --- 1. Ship control -------------------------------------------------------
        var ship = AsteroidsHelpers.FindShip(world);
        var shipAlive = ship != Entity.Null;
        var thrustOn = running && shipAlive && input.Thrust;

        if (shipAlive && running)
        {
            var vel = world.Get<Velocity>(ship);
            var rot = world.Get<Rotation>(ship);

            // Classic friction applies every tick, then thrust accelerates along the nose.
            var vx = vel.X * AsteroidsConfig.ShipDecay;
            var vy = vel.Y * AsteroidsConfig.ShipDecay;

            var angle = rot.Value
                        + (input.Right ? AsteroidsConfig.ShipRotateSpeed * dtf : 0f)
                        - (input.Left ? AsteroidsConfig.ShipRotateSpeed * dtf : 0f);
            world.Set(ship, new Rotation(NormalizeAngle(angle)));

            if (input.Thrust)
            {
                var fx = MathF.Sin(angle);
                var fy = -MathF.Cos(angle);
                vx = Math.Clamp(vx + (fx * AsteroidsConfig.ShipThrustAccel * dtf),
                    -AsteroidsConfig.ShipMaxSpeed, AsteroidsConfig.ShipMaxSpeed);
                vy = Math.Clamp(vy + (fy * AsteroidsConfig.ShipThrustAccel * dtf),
                    -AsteroidsConfig.ShipMaxSpeed, AsteroidsConfig.ShipMaxSpeed);
            }

            world.Set(ship, new Velocity(vx, vy));

            // Hyperspace: teleport to 10%..90% of the court; 1-in-10 the jump fails fatally.
            if (input.Hyperspace)
            {
                input.Hyperspace = false;
                if (_ctx.Random.Next(AsteroidsConfig.HyperspaceFailDenominator) == 1)
                {
                    ExplodeShip(world, ship, statsEntity);
                    ship = Entity.Null;
                    shipAlive = false;
                }
                else
                {
                    var hx = LerpFraction(_ctx.Random, AsteroidsConfig.HyperspaceMinFraction, AsteroidsConfig.HyperspaceMaxFraction)
                             * AsteroidsConfig.CourtWidth;
                    var hy = LerpFraction(_ctx.Random, AsteroidsConfig.HyperspaceMinFraction, AsteroidsConfig.HyperspaceMaxFraction)
                             * AsteroidsConfig.CourtHeight;
                    world.Set(ship, new Position(hx, hy));
                }
            }
        }
        else
        {
            input.Hyperspace = false;
        }

        // Fire: ship shot while alive, otherwise the reference lets FIRE summon the next ship.
        if (input.Fire)
        {
            input.Fire = false;
            stats = world.Get<AsteroidsStats>(statsEntity);
            if (running)
            {
                if (shipAlive && AsteroidsHelpers.CountBullets(world) < AsteroidsConfig.MaxBullets)
                {
                    SpawnBullet(world, ship, statsEntity);
                }
                else if (!shipAlive && AsteroidsHelpers.CountExplosions(world) == 0
                                     && stats.Lives > 1 && CenterSafe(world))
                {
                    world.Set(statsEntity, stats with { Lives = stats.Lives - 1 });
                    SpawnShip(world);
                }
            }
        }

        if (stats.ThrustOn != thrustOn)
        {
            stats = world.Get<AsteroidsStats>(statsEntity);
            world.Set(statsEntity, stats with { ThrustOn = thrustOn });
        }

        // --- 2. Saucer traverse + missile -------------------------------------------
        var saucer = AsteroidsHelpers.FindSaucer(world);
        if (saucer != Entity.Null && running)
        {
            var saucerPos = world.Get<Position>(saucer);
            var saucerVel = world.Get<Velocity>(saucer);
            var saucerTag = world.Get<SaucerTag>(saucer);

            // Count passes before the wrap teleports the saucer back on-screen.
            var predictedX = saucerPos.X + (saucerVel.X * dtf);
            if (predictedX <= 0f || predictedX >= AsteroidsConfig.CourtWidth)
            {
                saucerTag.Passes++;
                world.Set(saucer, saucerTag);
                if (saucerTag.Passes >= AsteroidsConfig.SaucerMaxPasses)
                {
                    DestroyEntity(world, saucer);
                    saucer = Entity.Null;
                    stats = world.Get<AsteroidsStats>(statsEntity);
                    world.Set(statsEntity, stats with { NextSaucerPoints = AsteroidsConfig.SaucerScoreThreshold });
                }
            }

            if (saucer != Entity.Null)
            {
                var missile = AsteroidsHelpers.FindMissile(world);
                if (missile == Entity.Null)
                {
                    SpawnMissile(world, saucer);
                    missile = AsteroidsHelpers.FindMissile(world);
                }

                if (missile != Entity.Null)
                {
                    var missilePos = world.Get<Position>(missile);
                    var missileRot = world.Get<Rotation>(missile);
                    var target = AsteroidsHelpers.FindShip(world);

                    if (target != Entity.Null)
                    {
                        var targetPos = world.Get<Position>(target);
                        var dx = targetPos.X - missilePos.X;
                        var dy = targetPos.Y - missilePos.Y;
                        var targetAngle = MathF.Atan2(dx, -dy);
                        var delta = NormalizeAngle(targetAngle - missileRot.Value);
                        var step = Math.Clamp(delta, -AsteroidsConfig.MissileAlignMax, AsteroidsConfig.MissileAlignMax);
                        missileRot = new Rotation(NormalizeAngle(missileRot.Value + step));
                        world.Set(missile, missileRot);
                    }

                    var mx = MathF.Sin(missileRot.Value) * AsteroidsConfig.MissileSpeed;
                    var my = -MathF.Cos(missileRot.Value) * AsteroidsConfig.MissileSpeed;
                    world.Set(missile, new Velocity(mx, my));
                    SetBodyVelocity(world, missile, mx, my);
                }
            }
        }

        // --- 3. Box2D physics --------------------------------------------------------
        StepPhysics(world, statsEntity, dtf);

        // --- 4. Flow: respawn / belt / game over / saucer threshold -------------------
        stats = world.Get<AsteroidsStats>(statsEntity);
        if (!stats.Started || stats.GameOver) return;

        var noExplosions = AsteroidsHelpers.CountExplosions(world) == 0;
        ship = AsteroidsHelpers.FindShip(world);

        if (ship == Entity.Null && noExplosions)
        {
            if (stats.Lives <= 1)
            {
                world.Set(statsEntity, stats with { GameOver = true });
            }
            else if (CenterSafe(world))
            {
                world.Set(statsEntity, stats with { Lives = stats.Lives - 1 });
                SpawnShip(world);
            }
        }

        if (noExplosions && AsteroidsHelpers.FindAsteroids(world).Count == 0)
        {
            stats = world.Get<AsteroidsStats>(statsEntity);
            var level = stats.Level + 1;
            world.Set(statsEntity, stats with { Level = level, LevelUp = true });
            SpawnBelt(world, level);
        }

        stats = world.Get<AsteroidsStats>(statsEntity);
        if (AsteroidsHelpers.FindSaucer(world) == Entity.Null && !stats.SaucerSpawned)
        {
            var needed = stats.NextSaucerPoints - (stats.Score - _scoreAtTickStart);
            if (needed <= 0)
            {
                SpawnSaucer(world, statsEntity);
            }
            else
            {
                world.Set(statsEntity, stats with { NextSaucerPoints = needed });
            }
        }
    }

    // ------------------------------------------------------------------ physics core

    private void StepPhysics(World world, Entity statsEntity, float dtf)
    {
        // Sync ship velocity (classic integration) into its Box2D body.
        var ship = AsteroidsHelpers.FindShip(world);
        if (ship != Entity.Null)
        {
            var vel = world.Get<Velocity>(ship);
            SetBodyVelocity(world, ship, vel.X, vel.Y);
        }

        b2World_Step(_ctx.World, dtf, AsteroidsConfig.SubStepCount);

        // Wrap every body past the margin onto the opposite edge (screen wrap).
        var margin = AsteroidsConfig.WrapMargin;
        var spanX = AsteroidsConfig.CourtWidth + (2f * margin);
        var spanY = AsteroidsConfig.CourtHeight + (2f * margin);
        var entities = new Entity[world.Size];
        world.GetEntities(new QueryDescription(), entities.AsSpan());
        foreach (var entity in entities)
        {
            if (!world.IsAlive(entity) || !world.Has<PhysicsBody>(entity)) continue;
            var bodyId = world.Get<PhysicsBody>(entity).BodyId;
            var pos = b2Body_GetPosition(bodyId);
            var px = pos.X * AsteroidsConfig.PixelsPerMeter;
            var py = pos.Y * AsteroidsConfig.PixelsPerMeter;
            var wrapped = false;

            if (px < -margin) { px += spanX; wrapped = true; }
            else if (px > AsteroidsConfig.CourtWidth + margin) { px -= spanX; wrapped = true; }

            if (py < -margin) { py += spanY; wrapped = true; }
            else if (py > AsteroidsConfig.CourtHeight + margin) { py -= spanY; wrapped = true; }

            if (wrapped)
            {
                var rotation = b2Body_GetRotation(bodyId);
                b2Body_SetTransform(bodyId,
                    new B2Vec2(px / AsteroidsConfig.PixelsPerMeter, py / AsteroidsConfig.PixelsPerMeter),
                    rotation);
            }

            world.Set(entity, new Position(px, py));
            if (world.Has<AsteroidTag>(entity))
            {
                // Box2D angles are CCW; Pixi rotation is CW in screen coords.
                world.Set(entity, new Rotation(-b2Rot_GetAngle(b2Body_GetRotation(bodyId))));
            }
        }

        // Resolve begin-touch contacts into game events.
        var events = b2World_GetContactEvents(_ctx.World);
        for (var i = 0; i < events.beginCount; i++)
        {
            var ev = events.beginEvents[i];
            var entityA = ResolveShape(world, ev.shapeIdA);
            var entityB = ResolveShape(world, ev.shapeIdB);
            if (entityA == Entity.Null || entityB == Entity.Null) continue;
            ResolveContact(world, entityA, entityB, statsEntity);
        }

        // Age bullets and explosions.
        entities = new Entity[world.Size];
        world.GetEntities(new QueryDescription(), entities.AsSpan());
        foreach (var entity in entities)
        {
            if (!world.IsAlive(entity)) continue;
            if (world.Has<BulletTag>(entity))
            {
                var bullet = world.Get<BulletTag>(entity);
                bullet.Age += dtf;
                if (bullet.Age >= AsteroidsConfig.BulletLifetimeSeconds)
                {
                    DestroyEntity(world, entity);
                    continue;
                }
                world.Set(entity, bullet);
            }
            else if (world.Has<ExplosionTag>(entity))
            {
                var explosion = world.Get<ExplosionTag>(entity);
                explosion.Age += dtf;
                if (explosion.Age >= explosion.Lifetime)
                {
                    DestroyEntity(world, entity);
                    continue;
                }
                world.Set(entity, explosion);
            }
        }
    }

    private Entity ResolveShape(World world, B2ShapeId shapeId)
    {
        var bodyId = b2Shape_GetBody(shapeId);
        var userData = b2Body_GetUserData(bodyId);
        if (userData.IsEmpty()) return Entity.Null;
        return _ctx.Resolve(world, (int)userData.GetSigned(-1));
    }

    private enum Kind : byte { KindShip, KindAsteroid, KindBullet, KindSaucer, KindMissile, KindNone }

    private static Kind KindOf(World world, Entity entity)
    {
        if (world.Has<ShipTag>(entity)) return Kind.KindShip;
        if (world.Has<AsteroidTag>(entity)) return Kind.KindAsteroid;
        if (world.Has<BulletTag>(entity)) return Kind.KindBullet;
        if (world.Has<SaucerTag>(entity)) return Kind.KindSaucer;
        if (world.Has<MissileTag>(entity)) return Kind.KindMissile;
        return Kind.KindNone;
    }

    private void ResolveContact(World world, Entity a, Entity b, Entity statsEntity)
    {
        var kindA = KindOf(world, a);
        var kindB = KindOf(world, b);
        if (kindA == Kind.KindNone || kindB == Kind.KindNone) return;

        // Normalize so A <= B; cases below are written in that canonical order.
        if (kindA > kindB)
        {
            (a, b) = (b, a);
            (kindA, kindB) = (kindB, kindA);
        }

        switch (kindA, kindB)
        {
            case (Kind.KindShip, Kind.KindAsteroid):
                // The asteroid survives; the ship does not.
                ExplodeShip(world, a, statsEntity);
                break;

            case (Kind.KindShip, Kind.KindSaucer):
            {
                DestroyEntity(world, b);
                var missile = AsteroidsHelpers.FindMissile(world);
                if (missile != Entity.Null) DestroyEntity(world, missile);
                ExplodeShip(world, a, statsEntity);
                AddScore(world, statsEntity, AsteroidsConfig.SaucerKillScore);
                var s = world.Get<AsteroidsStats>(statsEntity);
                world.Set(statsEntity, s with { NextSaucerPoints = AsteroidsConfig.SaucerScoreThreshold });
                break;
            }

            case (Kind.KindShip, Kind.KindMissile):
                DestroyEntity(world, b);
                ExplodeShip(world, a, statsEntity);
                break;

            case (Kind.KindAsteroid, Kind.KindBullet):
                BulletHitsAsteroid(world, a, b, statsEntity);
                break;

            case (Kind.KindBullet, Kind.KindSaucer):
                BulletHitsSaucer(world, a, b, statsEntity);
                break;

            case (Kind.KindBullet, Kind.KindMissile):
            {
                var pos = world.Get<Position>(b);
                DestroyEntity(world, a);
                DestroyEntity(world, b);
                SpawnExplosion(world, pos, AsteroidsConfig.ExplosionLifeSeconds);
                MarkExploded(world, statsEntity);
                break;
            }
        }
    }

    private void BulletHitsAsteroid(World world, Entity asteroid, Entity bullet, Entity statsEntity)
    {
        var pos = world.Get<Position>(asteroid);
        var tag = world.Get<AsteroidTag>(asteroid);
        DestroyEntity(world, asteroid);
        DestroyEntity(world, bullet);

        var newSize = tag.Size - 1;
        switch (newSize)
        {
            case AsteroidSize.Medium:
                AddScore(world, statsEntity, AsteroidsConfig.AsteroidScoreLarge);
                SpawnAsteroid(world, AsteroidSize.Medium, pos.X, pos.Y);
                SpawnAsteroid(world, AsteroidSize.Medium, pos.X, pos.Y);
                break;
            case AsteroidSize.Small:
                AddScore(world, statsEntity, AsteroidsConfig.AsteroidScoreMedium);
                SpawnAsteroid(world, AsteroidSize.Small, pos.X, pos.Y);
                SpawnAsteroid(world, AsteroidSize.Small, pos.X, pos.Y);
                break;
            default:
                AddScore(world, statsEntity, AsteroidsConfig.AsteroidScoreSmall);
                break;
        }

        SpawnExplosion(world, pos, AsteroidsConfig.ExplosionLifeSeconds);
        MarkExploded(world, statsEntity);
    }

    private void BulletHitsSaucer(World world, Entity bullet, Entity saucer, Entity statsEntity)
    {
        var pos = world.Get<Position>(saucer);
        DestroyEntity(world, bullet);
        DestroyEntity(world, saucer);
        var missile = AsteroidsHelpers.FindMissile(world);
        if (missile != Entity.Null) DestroyEntity(world, missile);

        AddScore(world, statsEntity, AsteroidsConfig.SaucerKillScore);
        SpawnExplosion(world, pos, AsteroidsConfig.ShipExplosionLifeSeconds);
        MarkExploded(world, statsEntity);
        var stats = world.Get<AsteroidsStats>(statsEntity);
        world.Set(statsEntity, stats with { NextSaucerPoints = AsteroidsConfig.SaucerScoreThreshold });
    }

    private void ExplodeShip(World world, Entity ship, Entity statsEntity)
    {
        var pos = world.Get<Position>(ship);
        DestroyEntity(world, ship);
        SpawnExplosion(world, pos, AsteroidsConfig.ShipExplosionLifeSeconds);
        MarkExploded(world, statsEntity);
    }

    private void AddScore(World world, Entity statsEntity, int points)
    {
        var stats = world.Get<AsteroidsStats>(statsEntity);
        if (points == 0) return;

        var score = (stats.Score + points) % AsteroidsConfig.MaxScore;
        var highScore = Math.Max(stats.HighScore, score);

        if (score / AsteroidsConfig.FreeShipIncrement > stats.Score / AsteroidsConfig.FreeShipIncrement)
        {
            world.Set(statsEntity, new AsteroidsStats(score, stats.Lives + 1, stats.Level, highScore,
                stats.NextSaucerPoints, stats.GameOver, stats.Started, stats.ThrustOn, stats.Exploded, stats.Fired,
                stats.SaucerSpawned, stats.LevelUp, lifeGained: true));
            return;
        }

        world.Set(statsEntity, stats with { Score = score, HighScore = highScore });
    }

    private static void MarkExploded(World world, Entity statsEntity)
    {
        var stats = world.Get<AsteroidsStats>(statsEntity);
        if (!stats.Exploded)
        {
            world.Set(statsEntity, stats with { Exploded = true });
        }
    }

    // ------------------------------------------------------------------- spawning

    public void SpawnShip(World world)
    {
        var entity = world.Create(
            new RenderId(_ctx.NextRenderId()),
            new Position(AsteroidsConfig.CourtWidth / 2f, AsteroidsConfig.CourtHeight / 2f),
            new Velocity(0f, 0f),
            new Rotation(0f),
            new SpriteColor(248, 250, 252),
            new ShipTag());
        _ctx.Register(entity);
        CreateBody(world, entity, AsteroidsConfig.ShipRadius, AsteroidsConfig.CatShip, AsteroidsConfig.MaskShip, isBullet: false);
    }

    public void SpawnBelt(World world, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var x = _ctx.Random.Next(2) == 0 ? 0f : AsteroidsConfig.CourtWidth - 1f;
            var y = _ctx.Random.NextSingle() * (AsteroidsConfig.CourtHeight - 1f);
            SpawnAsteroid(world, AsteroidSize.Large, x, y);
        }
    }

    public void SpawnAsteroid(World world, AsteroidSize size, float x, float y)
    {
        var (vx, vy) = RandomAsteroidVelocity(size);
        SpawnAsteroid(world, size, x, y, vx, vy);
    }

    /// <summary>Test hook: spawns an asteroid with an explicit velocity (px/s).</summary>
    public void SpawnAsteroid(World world, AsteroidSize size, float x, float y, float vx, float vy)
    {
        var radius = RadiusOf(size);
        var spinDegrees = ((_ctx.Random.NextSingle() * 2f * AsteroidsConfig.AsteroidMaxSpinDegrees)
                           - AsteroidsConfig.AsteroidMaxSpinDegrees);
        var spin = spinDegrees * MathF.PI / 180f;

        var entity = world.Create(
            new RenderId(_ctx.NextRenderId()),
            new Position(x, y),
            new Velocity(vx, vy),
            new Rotation(0f),
            new SpriteColor(212, 212, 212),
            new AsteroidTag(size));
        _ctx.Register(entity);
        CreateBody(world, entity, radius, AsteroidsConfig.CatAsteroid, AsteroidsConfig.MaskAsteroid, isBullet: false, spin);
    }

    public void SpawnBullet(World world, Entity ship, Entity statsEntity)
    {
        var pos = world.Get<Position>(ship);
        var shipVel = world.Get<Velocity>(ship);
        var rot = world.Get<Rotation>(ship);

        var fx = MathF.Sin(rot.Value);
        var fy = -MathF.Cos(rot.Value);
        var vx = (fx * AsteroidsConfig.BulletSpeed) + shipVel.X;
        var vy = (fy * AsteroidsConfig.BulletSpeed) + shipVel.Y;

        var entity = world.Create(
            new RenderId(_ctx.NextRenderId()),
            new Position(pos.X, pos.Y),
            new Velocity(vx, vy),
            new Rotation(MathF.Atan2(vx, -vy)),
            new SpriteColor(248, 250, 252),
            new BulletTag(0f));
        _ctx.Register(entity);
        CreateBody(world, entity, AsteroidsConfig.BulletRadius, AsteroidsConfig.CatBullet, AsteroidsConfig.MaskBullet, isBullet: true);

        var stats = world.Get<AsteroidsStats>(statsEntity);
        if (!stats.Fired)
        {
            world.Set(statsEntity, stats with { Fired = true });
        }
    }

    public void SpawnSaucer(World world, Entity statsEntity)
    {
        var fromLeft = _ctx.Random.Next(2) == 0;
        var x = fromLeft ? 0f : AsteroidsConfig.CourtWidth;
        var y = LerpFraction(_ctx.Random, AsteroidsConfig.SaucerMinYFraction, AsteroidsConfig.SaucerMaxYFraction)
                * AsteroidsConfig.CourtHeight;
        var vx = fromLeft ? AsteroidsConfig.SaucerSpeed : -AsteroidsConfig.SaucerSpeed;

        var entity = world.Create(
            new RenderId(_ctx.NextRenderId()),
            new Position(x, y),
            new Velocity(vx, 0f),
            new Rotation(0f),
            new SpriteColor(212, 212, 212),
            new SaucerTag(0));
        _ctx.Register(entity);
        CreateBody(world, entity, AsteroidsConfig.SaucerRadius, AsteroidsConfig.CatSaucer, AsteroidsConfig.MaskSaucer, isBullet: false);

        var stats = world.Get<AsteroidsStats>(statsEntity);
        world.Set(statsEntity, stats with { NextSaucerPoints = AsteroidsConfig.SaucerScoreThreshold, SaucerSpawned = true });
    }

    public void SpawnMissile(World world, Entity saucer)
    {
        var pos = world.Get<Position>(saucer);
        var entity = world.Create(
            new RenderId(_ctx.NextRenderId()),
            new Position(pos.X, pos.Y),
            new Velocity(0f, -AsteroidsConfig.MissileSpeed),
            new Rotation(0f),
            new SpriteColor(248, 250, 252),
            new MissileTag());
        _ctx.Register(entity);
        CreateBody(world, entity, AsteroidsConfig.MissileRadius, AsteroidsConfig.CatMissile, AsteroidsConfig.MaskMissile, isBullet: false);
    }

    public void SpawnExplosion(World world, Position pos, float lifetime)
    {
        var entity = world.Create(
            new RenderId(_ctx.NextRenderId()),
            new Position(pos.X, pos.Y),
            new SpriteColor(248, 250, 252),
            new ExplosionTag(0f, lifetime));
        _ctx.Register(entity);
    }

    public void DestroyEntity(World world, Entity entity)
    {
        if (!world.IsAlive(entity))
        {
            _ctx.Unregister(entity.Id);
            return;
        }
        if (world.Has<PhysicsBody>(entity))
        {
            var bodyId = world.Get<PhysicsBody>(entity).BodyId;
            if (b2Body_IsValid(bodyId))
            {
                b2DestroyBody(bodyId);
            }
        }
        _ctx.Unregister(entity.Id);
        world.Destroy(entity);
    }

    /// <summary>Test hook: attaches a Box2D body to an already-spawned entity.</summary>
    public void CreateBodyForTest(Entity entity, float radiusPx, ulong category, ulong mask, bool isBullet) =>
        CreateBody(World, entity, radiusPx, category, mask, isBullet);

    /// <summary>Removes every entity and every Box2D body (fresh game reset).</summary>
    public void DestroyAll(World world)
    {
        var entities = new Entity[world.Size];
        world.GetEntities(new QueryDescription(), entities.AsSpan());
        foreach (var entity in entities)
        {
            if (!world.IsAlive(entity)) continue;
            if (world.Has<PhysicsBody>(entity))
            {
                var bodyId = world.Get<PhysicsBody>(entity).BodyId;
                if (b2Body_IsValid(bodyId))
                {
                    b2DestroyBody(bodyId);
                }
            }
            _ctx.Unregister(entity.Id);
            world.Destroy(entity);
        }
    }

    // ------------------------------------------------------------------- internals

    private void CreateBody(World world, Entity entity, float radiusPx, ulong category, ulong mask, bool isBullet, float spin = 0f)
    {
        var pos = world.Get<Position>(entity);
        var vel = world.Get<Velocity>(entity);

        var bodyDef = b2DefaultBodyDef();
        bodyDef.type = B2BodyType.b2_dynamicBody;
        bodyDef.position = new B2Vec2(pos.X / AsteroidsConfig.PixelsPerMeter, pos.Y / AsteroidsConfig.PixelsPerMeter);
        bodyDef.linearVelocity = new B2Vec2(vel.X / AsteroidsConfig.PixelsPerMeter, vel.Y / AsteroidsConfig.PixelsPerMeter);
        bodyDef.angularVelocity = -spin;
        bodyDef.enableSleep = false;
        bodyDef.isBullet = isBullet;
        bodyDef.userData = B2UserData.Signed(entity.Id);
        var bodyId = b2CreateBody(_ctx.World, bodyDef);

        var shapeDef = b2DefaultShapeDef();
        shapeDef.enableContactEvents = true;
        shapeDef.filter = new B2Filter(category, mask, 0);
        b2CreateCircleShape(bodyId, shapeDef, new B2Circle(new B2Vec2(0f, 0f), radiusPx / AsteroidsConfig.PixelsPerMeter));

        world.Add(entity, new PhysicsBody(bodyId));
    }

    private static void SetBodyVelocity(World world, Entity entity, float vx, float vy)
    {
        if (!world.Has<PhysicsBody>(entity)) return;
        var bodyId = world.Get<PhysicsBody>(entity).BodyId;
        b2Body_SetLinearVelocity(bodyId,
            new B2Vec2(vx / AsteroidsConfig.PixelsPerMeter, vy / AsteroidsConfig.PixelsPerMeter));
    }

    private bool CenterSafe(World world)
    {
        var cx = AsteroidsConfig.CourtWidth / 2f;
        var cy = AsteroidsConfig.CourtHeight / 2f;
        foreach (var asteroid in AsteroidsHelpers.FindAsteroids(world))
        {
            var pos = world.Get<Position>(asteroid);
            var dx = pos.X - cx;
            var dy = pos.Y - cy;
            if (MathF.Sqrt((dx * dx) + (dy * dy)) < AsteroidsConfig.SafeRespawnDistance) return false;
        }
        return true;
    }

    private (float X, float Y) RandomAsteroidVelocity(AsteroidSize size)
    {
        var factor = AsteroidsConfig.AsteroidSpeedFactor(size);
        var vx = ((_ctx.Random.NextSingle() * 2000f) - 1000f) * factor * AsteroidsConfig.ReferenceScale;
        var vy = ((_ctx.Random.NextSingle() * 2000f) - 1000f) * factor * AsteroidsConfig.ReferenceScale;
        return (vx, vy);
    }

    private static float RadiusOf(AsteroidSize size) => size switch
    {
        AsteroidSize.Small => AsteroidsConfig.AsteroidRadiusSmall,
        AsteroidSize.Medium => AsteroidsConfig.AsteroidRadiusMedium,
        AsteroidSize.Large => AsteroidsConfig.AsteroidRadiusLarge,
        _ => 0f
    };

    private static float LerpFraction(Random random, float min, float max) =>
        min + (random.NextSingle() * (max - min));

    private static float NormalizeAngle(float angle)
    {
        var tau = MathF.PI * 2f;
        angle %= tau;
        if (angle < 0f) angle += tau;
        return angle;
    }
}
