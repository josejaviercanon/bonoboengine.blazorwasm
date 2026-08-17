using Arch.Core;
using Arch.Systems;

namespace Game.Engine.ECS.Breakout;

/// <summary>Client-suggested breakout input. Held flags are absolute state (no lost key-up
/// edges); <see cref="Launch"/> is a one-shot consumed by the input system.</summary>
public sealed class BreakoutInputState
{
    public bool Left;
    public bool Right;
    public bool Launch;
}

/// <summary>Collision-side labels (which edge of a rect the ball hits).</summary>
public enum BreakoutSide
{
    Left = 0,
    Right = 1,
    Top = 2,
    Bottom = 3
}

/// <summary>
///     Pure geometry helpers ported from the reference game's <c>Game.Math</c>
///     (<c>src/Temp/javascript-breakout/game.js</c>): swept line-segment interception
///     and ball-vs-rect interception with the ball inflated by its radius.
/// </summary>
public static class BreakoutMath
{
    public readonly record struct Rect(float Left, float Top, float Right, float Bottom);

    public readonly record struct HitPoint(float X, float Y, BreakoutSide Side);

    /// <summary>
    ///     Interception of segment (x1,y1)->(x2,y2) with the vertical/horizontal edge
    ///     of the ball-inflated rect. Mirrors <c>Game.Math.intercept</c>: only the edge
    ///     the ball is approaching is tested (dx sign picks the vertical edge, dy sign
    ///     the horizontal one).
    /// </summary>
    public static HitPoint? BallIntercept(float bx, float by, float nx, float ny, Rect rect, float radius)
    {
        HitPoint? pt = null;
        if (nx < 0f)
        {
            pt = Intercept(bx, by, bx + nx, by + ny,
                rect.Right + radius, rect.Top - radius, rect.Right + radius, rect.Bottom + radius,
                BreakoutSide.Right);
        }
        else if (nx > 0f)
        {
            pt = Intercept(bx, by, bx + nx, by + ny,
                rect.Left - radius, rect.Top - radius, rect.Left - radius, rect.Bottom + radius,
                BreakoutSide.Left);
        }

        if (pt is null)
        {
            if (ny < 0f)
            {
                pt = Intercept(bx, by, bx + nx, by + ny,
                    rect.Left - radius, rect.Bottom + radius, rect.Right + radius, rect.Bottom + radius,
                    BreakoutSide.Bottom);
            }
            else if (ny > 0f)
            {
                pt = Intercept(bx, by, bx + nx, by + ny,
                    rect.Left - radius, rect.Top - radius, rect.Right + radius, rect.Top - radius,
                    BreakoutSide.Top);
            }
        }

        return pt;
    }

    private static HitPoint? Intercept(float x1, float y1, float x2, float y2,
        float x3, float y3, float x4, float y4, BreakoutSide side)
    {
        var denom = ((y4 - y3) * (x2 - x1)) - ((x4 - x3) * (y2 - y1));
        if (denom == 0f) return null;

        var ua = (((x4 - x3) * (y1 - y3)) - ((y4 - y3) * (x1 - x3))) / denom;
        if (ua < 0f || ua > 1f) return null;

        var ub = (((x2 - x1) * (y1 - y3)) - ((y2 - y1) * (x1 - x3))) / denom;
        if (ub < 0f || ub > 1f) return null;

        return new HitPoint(x1 + (ua * (x2 - x1)), y1 + (ua * (y2 - y1)), side);
    }
}

/// <summary>Entity lookup helpers shared by the breakout systems.</summary>
public static class BreakoutHelpers
{
    public static Entity FindStats(World world)
    {
        var entities = new Entity[world.Size];
        world.GetEntities(new QueryDescription(), entities.AsSpan());
        foreach (var entity in entities)
        {
            if (world.IsAlive(entity) && world.Has<BreakoutStats>(entity)) return entity;
        }
        return Entity.Null;
    }

    public static Entity FindBall(World world)
    {
        var entities = new Entity[world.Size];
        world.GetEntities(new QueryDescription(), entities.AsSpan());
        foreach (var entity in entities)
        {
            if (world.IsAlive(entity) && world.Has<BreakoutBall>(entity)) return entity;
        }
        return Entity.Null;
    }

    public static Entity FindPaddle(World world)
    {
        var entities = new Entity[world.Size];
        world.GetEntities(new QueryDescription(), entities.AsSpan());
        foreach (var entity in entities)
        {
            if (world.IsAlive(entity) && world.Has<BreakoutPaddle>(entity)) return entity;
        }
        return Entity.Null;
    }

    public static List<Entity> FindBricks(World world)
    {
        var bricks = new List<Entity>();
        var entities = new Entity[world.Size];
        world.GetEntities(new QueryDescription(), entities.AsSpan());
        foreach (var entity in entities)
        {
            if (world.IsAlive(entity) && world.Has<BreakoutBrick>(entity)) bricks.Add(entity);
        }
        return bricks;
    }
}

/// <summary>
///     Consumes the client-suggested input state: applies the held paddle direction
///     and fires the one-shot launch when the ball is resting on the paddle.
///     C# remains the sole authority (ADR-001).
/// </summary>
public partial class BreakoutInputSystem : BaseSystem<World, double>
{
    private readonly BreakoutInputState _input;

    public BreakoutInputSystem(World world, BreakoutInputState input) : base(world)
    {
        _input = input;
    }

    public override void Update(in double t)
    {
        var statsEntity = BreakoutHelpers.FindStats(World);
        if (statsEntity == Entity.Null) return;
        var stats = World.Get<BreakoutStats>(statsEntity);
        if (!stats.Started || stats.GameOver) return;

        var paddle = BreakoutHelpers.FindPaddle(World);
        if (paddle != Entity.Null)
        {
            var p = World.Get<BreakoutPaddle>(paddle);
            World.Set(paddle, new BreakoutPaddle(p.Width, p.Height, _input.Left, _input.Right));
        }

        if (!_input.Launch) return;
        _input.Launch = false;

        var ball = BreakoutHelpers.FindBall(World);
        if (ball == Entity.Null) return;
        var b = World.Get<BreakoutBall>(ball);
        if (b.Moving) return;

        var velocity = new Velocity(1f, -1f);
        var magnitude = MathF.Sqrt((velocity.X * velocity.X) + (velocity.Y * velocity.Y));
        var speed = BreakoutConfig.BaseBallSpeed;
        World.Set(ball, new Velocity(velocity.X / magnitude * speed, velocity.Y / magnitude * speed));
        World.Set(ball, new BreakoutBall(b.Radius, speed, moving: true));
    }
}

/// <summary>
///     Physics authority: moves the paddle from held input, and advances the ball with
///     swept collision detection ported from the reference game. Multiple collisions in
///     a single frame are resolved recursively — the ball is moved to the closest
///     interception point, its direction flipped, and the remaining time is re-swept
///     (reference "Find the closest collision -> move -> flip -> recurse"). This
///     prevents fast balls tunnelling through bricks or the paddle.
/// </summary>
public partial class BreakoutPhysicsSystem : BaseSystem<World, double>
{
    private const int MaxCollisionDepth = 12;

    private readonly BreakoutMath.Rect _topWall;
    private readonly BreakoutMath.Rect _leftWall;
    private readonly BreakoutMath.Rect _rightWall;

    public BreakoutPhysicsSystem(World world) : base(world)
    {
        // Walls flush with the court edges (reference layout: one wall chunk around the court).
        _topWall = new BreakoutMath.Rect(-BreakoutConfig.WallSize, -2f * BreakoutConfig.WallSize,
            BreakoutConfig.CourtWidth + BreakoutConfig.WallSize, 0f);
        _leftWall = new BreakoutMath.Rect(-BreakoutConfig.WallSize, -2f * BreakoutConfig.WallSize,
            0f, BreakoutConfig.CourtHeight);
        _rightWall = new BreakoutMath.Rect(BreakoutConfig.CourtWidth, -2f * BreakoutConfig.WallSize,
            BreakoutConfig.CourtWidth + BreakoutConfig.WallSize, BreakoutConfig.CourtHeight);
    }

    public override void Update(in double t)
    {
        var statsEntity = BreakoutHelpers.FindStats(World);
        if (statsEntity == Entity.Null) return;
        var stats = World.Get<BreakoutStats>(statsEntity);
        if (!stats.Started || stats.GameOver) return;

        var paddle = BreakoutHelpers.FindPaddle(World);
        if (paddle != Entity.Null) MovePaddle(paddle, t);

        var ball = BreakoutHelpers.FindBall(World);
        if (ball != Entity.Null) StepBall(statsEntity, ball, (float)t);
    }

    private void MovePaddle(Entity paddle, double t)
    {
        var p = World.Get<BreakoutPaddle>(paddle);
        var pos = World.Get<Position>(paddle);
        var direction = (p.RightHeld ? 1f : 0f) - (p.LeftHeld ? 1f : 0f);
        var minX = p.Width / 2f;
        var maxX = BreakoutConfig.CourtWidth - (p.Width / 2f);
        var x = Math.Clamp(pos.X + (direction * (float)t * BreakoutConfig.PaddleSpeed), minX, maxX);
        World.Set(paddle, new Position(x, pos.Y));
    }

    private void StepBall(Entity statsEntity, Entity ball, float t)
    {
        var ballComponent = World.Get<BreakoutBall>(ball);
        var pos = World.Get<Position>(ball);

        if (!ballComponent.Moving)
        {
            var paddle = BreakoutHelpers.FindPaddle(World);
            if (paddle == Entity.Null) return;
            var paddleY = World.Get<Position>(paddle).Y;
            World.Set(ball, new Position(World.Get<Position>(paddle).X, paddleY - ballComponent.Radius));
            return;
        }

        var remaining = t;
        for (var depth = 0; depth < MaxCollisionDepth; depth++)
        {
            var vel = World.Get<Velocity>(ball);
            var nx = vel.X * remaining;
            var ny = vel.Y * remaining;

            var closest = FindClosestCollision(pos.X, pos.Y, nx, ny, ballComponent.Radius);
            if (closest is null)
            {
                var x = pos.X + nx;
                var y = pos.Y + ny;
                if (y - ballComponent.Radius > BreakoutConfig.CourtHeight)
                {
                    LoseBall(statsEntity, ball);
                    return;
                }
                World.Set(ball, new Position(x, y));
                return;
            }

            var (point, kind, entity) = closest.Value;
            World.Set(ball, new Position(point.X, point.Y));

            if (kind == CollisionKind.Brick)
            {
                if (HitBrick(statsEntity, ball, entity))
                {
                    return; // level won: ball was reset and relaunched inside
                }
                if (!World.Get<BreakoutBall>(ball).Moving) return;
            }

            if (kind == CollisionKind.Paddle && point.Side == BreakoutSide.Top)
            {
                var paddle = entity;
                var p = World.Get<BreakoutPaddle>(paddle);
                var paddlePos = World.Get<Position>(paddle);
                var relative = (point.X - paddlePos.X) / (p.Width / 2f); // -1..1
                var current = World.Get<Velocity>(ball);
                var speed = MathF.Sqrt((current.X * current.X) + (current.Y * current.Y));
                var dx = speed * relative;
                var dy = current.Y >= 0f ? -speed : speed; // ball leaves the paddle upward
                var magnitude = MathF.Sqrt((dx * dx) + (dy * dy));
                World.Set(ball, new Velocity(dx / magnitude * speed, dy / magnitude * speed));
                MarkPaddleHit(statsEntity);
            }
            else
            {
                var current = World.Get<Velocity>(ball);
                switch (point.Side)
                {
                    case BreakoutSide.Left:
                    case BreakoutSide.Right:
                        World.Set(ball, new Velocity(-current.X, current.Y));
                        break;
                    case BreakoutSide.Top:
                    case BreakoutSide.Bottom:
                        World.Set(ball, new Velocity(current.X, -current.Y));
                        break;
                }
            }

            var segLength = MathF.Sqrt((nx * nx) + (ny * ny));
            if (segLength <= 0.0001f) break;
            var travelled = MathF.Sqrt(((point.X - pos.X) * (point.X - pos.X)) + ((point.Y - pos.Y) * (point.Y - pos.Y)));
            var udt = remaining * (travelled / segLength);
            remaining -= udt;
            if (remaining <= 0.0001f) break;

            pos = World.Get<Position>(ball);
            ballComponent = World.Get<BreakoutBall>(ball);
        }
    }

    private enum CollisionKind
    {
        Paddle,
        Brick,
        Wall
    }

    private (BreakoutMath.HitPoint Point, CollisionKind Kind, Entity Entity)? FindClosestCollision(
        float bx, float by, float nx, float ny, float radius)
    {
        var closestDistance = float.PositiveInfinity;
        (BreakoutMath.HitPoint Point, CollisionKind Kind, Entity Entity)? closest = null;

        void Consider(BreakoutMath.Rect rect, CollisionKind kind, Entity entity)
        {
            var point = BreakoutMath.BallIntercept(bx, by, nx, ny, rect, radius);
            if (point is null) return;
            var dx = point.Value.X - bx;
            var dy = point.Value.Y - by;
            var distance = MathF.Sqrt((dx * dx) + (dy * dy));
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closest = (point.Value, kind, entity);
            }
        }

        var paddle = BreakoutHelpers.FindPaddle(World);
        if (paddle != Entity.Null)
        {
            var p = World.Get<BreakoutPaddle>(paddle);
            var pp = World.Get<Position>(paddle);
            Consider(new BreakoutMath.Rect(pp.X - (p.Width / 2f), pp.Y - (p.Height / 2f),
                pp.X + (p.Width / 2f), pp.Y + (p.Height / 2f)), CollisionKind.Paddle, paddle);
        }

        Consider(_topWall, CollisionKind.Wall, Entity.Null);
        Consider(_leftWall, CollisionKind.Wall, Entity.Null);
        Consider(_rightWall, CollisionKind.Wall, Entity.Null);

        var bricks = BreakoutHelpers.FindBricks(World);
        foreach (var brick in bricks)
        {
            var b = World.Get<BreakoutBrick>(brick);
            var bp = World.Get<Position>(brick);
            Consider(new BreakoutMath.Rect(bp.X - (b.Width / 2f), bp.Y - (BreakoutConfig.ChunkSize / 2f),
                bp.X + (b.Width / 2f), bp.Y + (BreakoutConfig.ChunkSize / 2f)), CollisionKind.Brick, brick);
        }

        return closest;
    }

    /// <summary>Destroys the brick, applies score + speed decay, and returns true when the
    /// level was completed (ball reset inside).</summary>
    private bool HitBrick(Entity statsEntity, Entity ball, Entity brick)
    {
        var brickComponent = World.Get<BreakoutBrick>(brick);
        World.Destroy(brick);

        var b = World.Get<BreakoutBall>(ball);
        var vel = World.Get<Velocity>(ball);
        var magnitude = MathF.Sqrt((vel.X * vel.X) + (vel.Y * vel.Y));
        var newSpeed = Math.Min(BreakoutConfig.BallMaxSpeed,
            b.Speed + (10f * (1f - (b.Speed / BreakoutConfig.BallMaxSpeed))));
        World.Set(ball, new BreakoutBall(b.Radius, newSpeed, moving: true));
        World.Set(ball, new Velocity(vel.X / magnitude * newSpeed, vel.Y / magnitude * newSpeed));

        var stats = World.Get<BreakoutStats>(statsEntity);
        World.Set(statsEntity, new BreakoutStats(stats.Score + brickComponent.Score, stats.Lives, stats.Level,
            stats.GameOver, stats.Started, brickHit: true, paddleHit: stats.PaddleHit,
            levelUp: stats.LevelUp, loseLife: stats.LoseLife));

        if (BreakoutHelpers.FindBricks(World).Count == 0)
        {
            WinLevel(statsEntity, ball);
            return true;
        }
        return false;
    }

    private void WinLevel(Entity statsEntity, Entity ball)
    {
        var stats = World.Get<BreakoutStats>(statsEntity);
        var lives = Math.Min(BreakoutConfig.MaxLives, stats.Lives + 1);
        var level = (stats.Level + 1) % BreakoutLevels.All.Count;

        foreach (var brick in BreakoutHelpers.FindBricks(World))
        {
            World.Destroy(brick);
        }
        SpawnBricks(level);

        ResetBallOnPaddle(ball, launch: true);
        World.Set(statsEntity, new BreakoutStats(stats.Score, lives, level, stats.GameOver, stats.Started,
            brickHit: stats.BrickHit, paddleHit: stats.PaddleHit,
            levelUp: true, loseLife: stats.LoseLife));
    }

    internal void LoseBall(Entity statsEntity, Entity ball)
    {
        var stats = World.Get<BreakoutStats>(statsEntity);
        var lives = stats.Lives - 1;
        if (lives <= 0)
        {
            World.Set(statsEntity, new BreakoutStats(stats.Score, 0, stats.Level,
                gameOver: true, stats.Started, brickHit: stats.BrickHit, paddleHit: stats.PaddleHit,
                levelUp: stats.LevelUp, loseLife: true));
            return;
        }

        ResetBallOnPaddle(ball, launch: false);
        World.Set(statsEntity, new BreakoutStats(stats.Score, lives, stats.Level,
            stats.GameOver, stats.Started, brickHit: stats.BrickHit, paddleHit: stats.PaddleHit,
            levelUp: stats.LevelUp, loseLife: true));
    }

    private void MarkPaddleHit(Entity statsEntity)
    {
        var stats = World.Get<BreakoutStats>(statsEntity);
        World.Set(statsEntity, new BreakoutStats(stats.Score, stats.Lives, stats.Level,
            stats.GameOver, stats.Started, brickHit: stats.BrickHit, paddleHit: true,
            levelUp: stats.LevelUp, loseLife: stats.LoseLife));
    }

    /// <summary>Places the ball on the paddle; optionally launches it immediately.</summary>
    private void ResetBallOnPaddle(Entity ball, bool launch)
    {
        var paddle = BreakoutHelpers.FindPaddle(World);
        if (paddle == Entity.Null) return;
        var paddlePos = World.Get<Position>(paddle);
        var radius = World.Get<BreakoutBall>(ball).Radius;
        var paddleTop = paddlePos.Y - (World.Get<BreakoutPaddle>(paddle).Height / 2f);
        World.Set(ball, new Position(paddlePos.X, paddleTop - radius));

        if (launch)
        {
            var speed = BreakoutConfig.BaseBallSpeed;
            var magnitude = MathF.Sqrt(2f);
            World.Set(ball, new Velocity(1f / magnitude * speed, -1f / magnitude * speed));
            World.Set(ball, new BreakoutBall(radius, speed, moving: true));
        }
        else
        {
            World.Set(ball, new BreakoutBall(radius, World.Get<BreakoutBall>(ball).Speed, moving: false));
        }
    }

    /// <summary>Builds brick entities for a level (one per contiguous same-char run).</summary>
    public void SpawnBricks(int level)
    {
        var layout = BreakoutLevels.All[level];
        for (var y = 0; y < layout.Rows.Length && y < BreakoutConfig.CourtChunksY; y++)
        {
            var row = layout.Rows[y].TrimEnd();
            var score = (BreakoutConfig.CourtChunksY - y) * 5;
            var runStart = -1;
            char? runChar = null;
            for (var x = 0; x <= row.Length; x++)
            {
                var atEnd = x == row.Length;
                var c = atEnd ? '\0' : row[x];
                if (!atEnd && c != ' ' && (runChar is null || c == runChar.Value))
                {
                    if (runChar is null) { runStart = x; runChar = c; }
                    continue;
                }

                if (runChar is not null)
                {
                    SpawnBrick(layout, runChar.Value, runStart, x - 1, y, score);
                    runChar = null;
                }
                if (!atEnd && c != ' ')
                {
                    runStart = x;
                    runChar = c;
                }
            }
        }
    }

    private void SpawnBrick(BreakoutLevels.BreakoutLevel layout, char colorChar, int x1, int x2, int y, int score)
    {
        var width = (x2 - x1 + 1) * BreakoutConfig.ChunkSize;
        var color = layout.Colors.TryGetValue(char.ToLowerInvariant(colorChar), out var known)
            ? known
            : new SpriteColor(255, 255, 255);
        var centerX = (x1 * BreakoutConfig.ChunkSize) + (width / 2f);
        var centerY = (y * BreakoutConfig.ChunkSize) + (BreakoutConfig.ChunkSize / 2f);
        World.Create(
            new RenderId(BreakoutSimulation.NextBrickId()),
            new Position(centerX, centerY),
            color,
            new BreakoutBrick(score, width));
    }
}