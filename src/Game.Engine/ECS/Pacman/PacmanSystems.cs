using Arch.Core;
using Arch.Systems;

namespace Game.Engine.ECS.Pacman;

/// <summary>Consumes client suggestions and stores only validated direction data.</summary>
public sealed class PacmanInputSystem : BaseSystem<World, float>
{
    private readonly Queue<PacmanDirection> _pending;

    public PacmanInputSystem(World world, Queue<PacmanDirection> pending) : base(world)
    {
        _pending = pending;
    }

    public override void Update(in float t)
    {
        var player = FindPlayer();
        if (player == Entity.Null)
        {
            _pending.Clear();
            return;
        }

        if (_pending.Count == 0) return;

        var facing = World.Get<PacmanFacing>(player);
        while (_pending.Count > 0)
        {
            facing.Requested = (int)_pending.Dequeue();
        }

        World.Set(player, facing);
    }

    private Entity FindPlayer()
    {
        var entities = new Entity[World.Size];
        World.GetEntities(new QueryDescription(), entities.AsSpan());
        foreach (var entity in entities)
        {
            if (World.IsAlive(entity) && World.Has<PacmanPlayer>(entity)) return entity;
        }

        return Entity.Null;
    }
}

/// <summary>
/// Advances Pacman gameplay. Ghost AI uses deterministic junction decisions inspired
/// by the reference project and BrainAI graph concepts, while all mutable state stays
/// in ECS components or system-owned scratch state.
/// </summary>
public sealed class PacmanStepSystem : BaseSystem<World, float>
{
    private static readonly PacmanDirection[] DecisionOrder =
    [
        PacmanDirection.Up,
        PacmanDirection.Left,
        PacmanDirection.Down,
        PacmanDirection.Right,
    ];

    private readonly Random _random;

    public PacmanStepSystem(World world, Random random) : base(world)
    {
        _random = random;
    }

    public override void Update(in float dt)
    {
        var statsEntity = FindStats();
        var player = FindPlayer();
        if (statsEntity == Entity.Null || player == Entity.Null) return;

        var stats = World.Get<PacmanStats>(statsEntity);
        if (!stats.Started || stats.GameOver) return;

        BeginFrame();
        UpdateMode(ref stats, dt);
        MovePlayer(player, ref stats, dt);
        MoveGhosts(player, stats, dt);
        ConsumePellet(player, ref stats);
        HandleCollisions(player, ref stats);

        World.Set(statsEntity, stats);
    }

    public void ResetActors(in PacmanStats stats)
    {
        var player = FindPlayer();
        if (player != Entity.Null)
        {
            var start = PacmanMaze.CenterOf(new PacmanCell(14, 23));
            World.Set(player, new PacmanTransform(start.X, start.Y));
            World.Set(player, new PacmanMotion());
            World.Set(player, new PacmanFacing(PacmanDirection.Left));
        }

        foreach (var ghost in FindGhosts())
        {
            var state = World.Get<PacmanGhostState>(ghost);
            var home = PacmanMaze.CenterOf(state.HomeCell);
            World.Set(ghost, new PacmanTransform(home.X, home.Y));
            World.Set(ghost, new PacmanMotion());
            World.Set(ghost, new PacmanFacing(PacmanDirection.Left));
            state.Mode = (int)BaseMode(stats);
            World.Set(ghost, state);
        }
    }

    private void BeginFrame()
    {
        var entities = new Entity[World.Size];
        World.GetEntities(new QueryDescription(), entities.AsSpan());
        foreach (var entity in entities)
        {
            if (!World.IsAlive(entity) || !World.Has<PacmanTransform>(entity)) continue;
            var transform = World.Get<PacmanTransform>(entity);
            transform.PreviousX = transform.X;
            transform.PreviousY = transform.Y;
            World.Set(entity, transform);
        }
    }

    private void UpdateMode(ref PacmanStats stats, float dt)
    {
        if (stats.Frightened)
        {
            stats.FrightenedRemaining -= dt;
            if (stats.FrightenedRemaining > 0f) return;

            stats.Frightened = false;
            stats.FrightenedRemaining = 0f;
            stats.GhostChain = 0;
            foreach (var ghost in FindGhosts())
            {
                var state = World.Get<PacmanGhostState>(ghost);
                if (state.GhostMode != PacmanGhostMode.Eyes)
                {
                    state.Mode = (int)BaseMode(stats);
                    World.Set(ghost, state);
                }
            }

            return;
        }

        stats.ModeRemaining -= dt;
        if (stats.ModeRemaining > 0f) return;

        if (stats.ModeIndex < ModeDurations.Length - 1)
        {
            stats.ModeIndex++;
            stats.ModeRemaining = ModeDurations[stats.ModeIndex];
            var newMode = BaseMode(stats);
            foreach (var ghost in FindGhosts())
            {
                var state = World.Get<PacmanGhostState>(ghost);
                if (state.GhostMode != PacmanGhostMode.Eyes)
                {
                    state.Mode = (int)newMode;
                    World.Set(ghost, state);

                    var facing = World.Get<PacmanFacing>(ghost);
                    facing.Current = (int)PacmanMaze.Opposite(facing.CurrentDirection);
                    World.Set(ghost, facing);
                }
            }
        }
        else
        {
            stats.ModeRemaining = PacmanConfig.ModeFourthChaseSeconds;
        }
    }

    private void MovePlayer(Entity player, ref PacmanStats stats, float dt)
    {
        var transform = World.Get<PacmanTransform>(player);
        var facing = World.Get<PacmanFacing>(player);
        var cell = PacmanMaze.CellFromPosition(transform.X, transform.Y);

        if (PacmanMaze.IsNearCenter(transform.X, transform.Y, cell))
        {
            var center = PacmanMaze.CenterOf(cell);
            transform.X = center.X;
            transform.Y = center.Y;

            var requested = facing.RequestedDirection;
            if (PacmanMaze.CanMove(cell, requested)) facing.Current = (int)requested;

            if (!PacmanMaze.CanMove(cell, facing.CurrentDirection))
            {
                facing.Current = (int)PacmanDirection.None;
            }
        }

        var direction = facing.CurrentDirection;
        var speed = direction == PacmanDirection.None ? 0f : PacmanConfig.PlayerSpeed;
        Advance(ref transform, direction, speed, dt, cell);
        World.Set(player, transform);
        World.Set(player, facing);
        World.Set(player, new PacmanMotion(PacmanMaze.VectorFor(direction).X * speed,
            PacmanMaze.VectorFor(direction).Y * speed, speed));
    }

    private void MoveGhosts(Entity player, in PacmanStats stats, float dt)
    {
        var playerTransform = World.Get<PacmanTransform>(player);
        var playerCell = PacmanMaze.CellFromPosition(playerTransform.X, playerTransform.Y);
        var blinkyCell = playerCell;

        foreach (var ghost in FindGhosts())
        {
            var ghostState = World.Get<PacmanGhostState>(ghost);
            var transform = World.Get<PacmanTransform>(ghost);
            var facing = World.Get<PacmanFacing>(ghost);
            var cell = PacmanMaze.CellFromPosition(transform.X, transform.Y);
            var mode = ghostState.GhostMode;

            if (ghostState.GhostRole == PacmanGhostRole.Blinky)
            {
                blinkyCell = cell;
            }

            if (PacmanMaze.IsNearCenter(transform.X, transform.Y, cell))
            {
                var center = PacmanMaze.CenterOf(cell);
                transform.X = center.X;
                transform.Y = center.Y;

                if (mode == PacmanGhostMode.Eyes && cell == ghostState.HomeCell)
                {
                    ghostState.Mode = (int)BaseMode(stats);
                    mode = ghostState.GhostMode;
                }

                var target = mode == PacmanGhostMode.Eyes
                    ? ghostState.HomeCell
                    : GetTarget(ghostState.GhostRole, cell, playerCell, blinkyCell,
                        World.Get<PacmanFacing>(player).CurrentDirection, mode);
                facing.Current = (int)PickDirection(cell, facing.CurrentDirection, target, mode);
            }

            var speed = mode switch
            {
                PacmanGhostMode.Frightened => PacmanConfig.FrightenedGhostSpeed,
                PacmanGhostMode.Eyes => PacmanConfig.EyesSpeed,
                _ => PacmanConfig.GhostSpeed,
            };
            Advance(ref transform, facing.CurrentDirection, speed, dt, cell);
            World.Set(ghost, transform);
            World.Set(ghost, facing);
            World.Set(ghost, ghostState);
            var vector = PacmanMaze.VectorFor(facing.CurrentDirection);
            World.Set(ghost, new PacmanMotion(vector.X * speed, vector.Y * speed, speed));
        }
    }

    private void ConsumePellet(Entity player, ref PacmanStats stats)
    {
        var transform = World.Get<PacmanTransform>(player);
        var cell = PacmanMaze.CellFromPosition(transform.X, transform.Y);
        if (!PacmanMaze.IsNearCenter(transform.X, transform.Y, cell)) return;

        var pellet = FindPelletAt(cell);
        if (pellet == Entity.Null) return;

        var power = World.Get<PacmanPellet>(pellet).Power;
        World.Destroy(pellet);
        stats.PelletsRemaining--;
        stats.Score += power ? PacmanConfig.PowerPelletScore : PacmanConfig.PelletScore;
        stats.AtePellet = !power;
        stats.AtePowerPellet = power;

        if (power)
        {
            stats.Frightened = true;
            stats.FrightenedRemaining = PacmanConfig.FrightenedDurationSeconds;
            stats.GhostChain = 0;
            foreach (var ghost in FindGhosts())
            {
                var state = World.Get<PacmanGhostState>(ghost);
                if (state.GhostMode == PacmanGhostMode.Eyes) continue;
                state.Mode = (int)PacmanGhostMode.Frightened;
                World.Set(ghost, state);
                var facing = World.Get<PacmanFacing>(ghost);
                facing.Current = (int)PacmanMaze.Opposite(facing.CurrentDirection);
                World.Set(ghost, facing);
            }
        }
    }

    private void HandleCollisions(Entity player, ref PacmanStats stats)
    {
        var playerTransform = World.Get<PacmanTransform>(player);
        var playerCell = PacmanMaze.CellFromPosition(playerTransform.X, playerTransform.Y);

        foreach (var ghost in FindGhosts())
        {
            var ghostTransform = World.Get<PacmanTransform>(ghost);
            if (PacmanMaze.CellFromPosition(ghostTransform.X, ghostTransform.Y) != playerCell) continue;

            var state = World.Get<PacmanGhostState>(ghost);
            if (state.GhostMode == PacmanGhostMode.Eyes) continue;

            if (state.GhostMode == PacmanGhostMode.Frightened)
            {
                var multiplier = 1 << Math.Min(stats.GhostChain, 3);
                stats.Score += PacmanConfig.GhostScore * multiplier;
                stats.GhostChain++;
                stats.GhostEaten = true;
                state.Mode = (int)PacmanGhostMode.Eyes;
                World.Set(ghost, state);
                continue;
            }

            stats.Lives--;
            stats.Died = true;
            stats.Frightened = false;
            stats.FrightenedRemaining = 0f;
            stats.GhostChain = 0;
            if (stats.Lives <= 0)
            {
                stats.Lives = 0;
                stats.GameOver = true;
                stats.Started = false;
            }
            else
            {
                ResetActors(stats);
            }

            return;
        }
    }

    private PacmanDirection PickDirection(
        PacmanCell cell,
        PacmanDirection current,
        PacmanCell target,
        PacmanGhostMode mode)
    {
        var choices = new List<PacmanDirection>(4);
        foreach (var direction in DecisionOrder)
        {
            if (!PacmanMaze.CanMove(cell, direction)) continue;
            if (current != PacmanDirection.None && direction == PacmanMaze.Opposite(current)) continue;
            if (mode is PacmanGhostMode.Scatter or PacmanGhostMode.Chase &&
                PacmanMaze.IsSpecialIntersection(cell) && direction == PacmanDirection.Up)
            {
                continue;
            }

            choices.Add(direction);
        }

        if (choices.Count == 0)
        {
            var reverse = PacmanMaze.Opposite(current);
            return PacmanMaze.CanMove(cell, reverse) ? reverse : current;
        }

        if (mode == PacmanGhostMode.Frightened)
        {
            return choices[_random.Next(choices.Count)];
        }

        var best = choices[0];
        var bestDistance = PacmanMaze.DistanceSquared(PacmanMaze.NextCell(cell, best), target);
        for (var i = 1; i < choices.Count; i++)
        {
            var candidate = choices[i];
            var distance = PacmanMaze.DistanceSquared(PacmanMaze.NextCell(cell, candidate), target);
            if (distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best;
    }

    private static PacmanCell GetTarget(
        PacmanGhostRole role,
        PacmanCell ghostCell,
        PacmanCell playerCell,
        PacmanCell blinkyCell,
        PacmanDirection playerDirection,
        PacmanGhostMode mode)
    {
        if (mode == PacmanGhostMode.Frightened)
        {
            return role switch
            {
                PacmanGhostRole.Blinky => new PacmanCell(0, 0),
                PacmanGhostRole.Pinky => new PacmanCell(2, 0),
                PacmanGhostRole.Inky => new PacmanCell(PacmanMaze.Width - 2, PacmanMaze.Height - 1),
                _ => new PacmanCell(0, PacmanMaze.Height - 1),
            };
        }

        if (mode == PacmanGhostMode.Scatter)
        {
            return role switch
            {
                PacmanGhostRole.Blinky => new PacmanCell(25, 0),
                PacmanGhostRole.Pinky => new PacmanCell(2, 0),
                PacmanGhostRole.Inky => new PacmanCell(PacmanMaze.Width - 2, PacmanMaze.Height - 1),
                _ => new PacmanCell(0, PacmanMaze.Height - 1),
            };
        }

        return role switch
        {
            PacmanGhostRole.Blinky => playerCell,
            PacmanGhostRole.Pinky => FourAhead(playerCell, playerDirection),
            PacmanGhostRole.Inky => InkyTarget(playerCell, playerDirection, blinkyCell),
            PacmanGhostRole.Clyde => PacmanMaze.DistanceSquared(ghostCell, playerCell) >= 64
                ? playerCell
                : new PacmanCell(0, PacmanMaze.Height - 1),
            _ => playerCell,
        };
    }

    private static PacmanCell FourAhead(PacmanCell cell, PacmanDirection direction)
    {
        var vector = PacmanMaze.VectorFor(direction);
        var x = cell.X + (int)(vector.X * 4f);
        var y = cell.Y + (int)(vector.Y * 4f);
        if (direction == PacmanDirection.Up) x -= 4;
        return new PacmanCell(Math.Clamp(x, 0, PacmanMaze.Width - 1), Math.Clamp(y, 0, PacmanMaze.Height - 1));
    }

    private static PacmanCell InkyTarget(PacmanCell playerCell, PacmanDirection playerDirection, PacmanCell blinkyCell)
    {
        var vector = PacmanMaze.VectorFor(playerDirection);
        var twoAhead = new PacmanCell(
            Math.Clamp(playerCell.X + (int)(vector.X * 2f), 0, PacmanMaze.Width - 1),
            Math.Clamp(playerCell.Y + (int)(vector.Y * 2f), 0, PacmanMaze.Height - 1));
        return new PacmanCell(
            Math.Clamp(blinkyCell.X + ((twoAhead.X - blinkyCell.X) * 2), 0, PacmanMaze.Width - 1),
            Math.Clamp(blinkyCell.Y + ((twoAhead.Y - blinkyCell.Y) * 2), 0, PacmanMaze.Height - 1));
    }

    private static PacmanGhostMode BaseMode(in PacmanStats stats) =>
        stats.ModeIndex % 2 == 0 ? PacmanGhostMode.Scatter : PacmanGhostMode.Chase;

    private static readonly float[] ModeDurations =
    [
        PacmanConfig.ModeFirstScatterSeconds,
        PacmanConfig.ModeFirstChaseSeconds,
        PacmanConfig.ModeSecondScatterSeconds,
        PacmanConfig.ModeSecondChaseSeconds,
        PacmanConfig.ModeThirdScatterSeconds,
        PacmanConfig.ModeThirdChaseSeconds,
        PacmanConfig.ModeFourthScatterSeconds,
        PacmanConfig.ModeFourthChaseSeconds,
    ];

    private static void Advance(
        ref PacmanTransform transform,
        PacmanDirection direction,
        float speed,
        float dt,
        PacmanCell cell)
    {
        var vector = PacmanMaze.VectorFor(direction);
        transform.X += vector.X * speed * dt;
        transform.Y += vector.Y * speed * dt;

        if (PacmanMaze.IsTunnel(cell))
        {
            if (transform.X < 0f) transform.X += PacmanMaze.BoardWidth;
            if (transform.X >= PacmanMaze.BoardWidth) transform.X -= PacmanMaze.BoardWidth;
        }
    }

    private Entity FindStats()
    {
        var entities = new Entity[World.Size];
        World.GetEntities(new QueryDescription(), entities.AsSpan());
        foreach (var entity in entities)
        {
            if (World.IsAlive(entity) && World.Has<PacmanStats>(entity)) return entity;
        }

        return Entity.Null;
    }

    private Entity FindPlayer()
    {
        var entities = new Entity[World.Size];
        World.GetEntities(new QueryDescription(), entities.AsSpan());
        foreach (var entity in entities)
        {
            if (World.IsAlive(entity) && World.Has<PacmanPlayer>(entity)) return entity;
        }

        return Entity.Null;
    }

    private Entity[] FindGhosts()
    {
        var entities = new Entity[World.Size];
        World.GetEntities(new QueryDescription(), entities.AsSpan());
        var ghosts = new List<Entity>(4);
        foreach (var entity in entities)
        {
            if (World.IsAlive(entity) && World.Has<PacmanGhostState>(entity)) ghosts.Add(entity);
        }

        ghosts.Sort((left, right) => World.Get<PacmanGhostState>(left).Role.CompareTo(World.Get<PacmanGhostState>(right).Role));
        return ghosts.ToArray();
    }

    private Entity FindPelletAt(PacmanCell cell)
    {
        var entities = new Entity[World.Size];
        World.GetEntities(new QueryDescription(), entities.AsSpan());
        foreach (var entity in entities)
        {
            if (!World.IsAlive(entity) || !World.Has<PacmanPellet>(entity)) continue;
            var transform = World.Get<PacmanTransform>(entity);
            if (PacmanMaze.CellFromPosition(transform.X, transform.Y) == cell) return entity;
        }

        return Entity.Null;
    }

}
