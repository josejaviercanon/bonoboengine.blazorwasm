using Arch.Core;
using Arch.Systems;
using Game.Engine.ECS;

namespace Game.Engine.ECS.Snake;

/// <summary>
///     Consumes buffered player input (queued by <see cref="SnakeSimulation.QueueDirection"/>)
///     and steers the head entity. Turns that would reverse the current direction are dropped,
///     so the snake can never move into itself. C# remains the sole authority: the client only
///     suggests directions, this system validates and applies them.
/// </summary>
public partial class SnakeInputSystem : BaseSystem<World, double>
{
    private readonly Queue<SnakeDir> _pending;

    public SnakeInputSystem(World world, Queue<SnakeDir> pending) : base(world)
    {
        _pending = pending;
    }

    public override void Update(in double t)
    {
        if (_pending.Count == 0) return;

        var head = FindHead();
        if (head == Entity.Null)
        {
            _pending.Clear();
            return;
        }

        var current = World.Get<SnakeDirection>(head).Dir;
        while (_pending.Count > 0)
        {
            var dir = _pending.Dequeue();
            if (IsInverse(dir, current)) continue;
            World.Set(head, new SnakeDirection(dir));
            return;
        }
    }

    private Entity FindHead()
    {
        var entities = new Entity[World.Size];
        World.GetEntities(new QueryDescription(), entities.AsSpan());
        foreach (var entity in entities)
        {
            if (World.IsAlive(entity) && World.Has<SnakeHead>(entity)) return entity;
        }
        return Entity.Null;
    }

    private static bool IsInverse(SnakeDir a, SnakeDir b) =>
        (a == SnakeDir.Up && b == SnakeDir.Down) ||
        (a == SnakeDir.Down && b == SnakeDir.Up) ||
        (a == SnakeDir.Left && b == SnakeDir.Right) ||
        (a == SnakeDir.Right && b == SnakeDir.Left);
}

/// <summary>
///     Advances the snake one grid cell per call: moves the head, shifts every body
///     segment into its predecessor's cell, grows on food, respawns food and flags
///     game over on wall/self collision. State lives in components; the system only
///     orchestrates (the snake's chain order is expressed through <see cref="RenderId"/>).
/// </summary>
public partial class SnakeStepSystem : BaseSystem<World, double>
{
    private readonly int _gridWidth;
    private readonly int _gridHeight;
    private readonly Random _random;
    private readonly SpriteColor _bodyColor;
    private readonly SpriteColor _headColor;
    private readonly SpriteColor _foodColor;
    private readonly Func<int> _nextFoodId;
    private int _nextSegmentId;

    public SnakeStepSystem(World world, int gridWidth, int gridHeight, int initialLength,
        SpriteColor bodyColor, SpriteColor headColor, SpriteColor foodColor, Random random, Func<int> nextFoodId)
        : base(world)
    {
        _gridWidth = gridWidth;
        _gridHeight = gridHeight;
        _nextSegmentId = initialLength;
        _bodyColor = bodyColor;
        _headColor = headColor;
        _foodColor = foodColor;
        _random = random;
        _nextFoodId = nextFoodId;
    }

    public override void Update(in double t)
    {
        var entities = CollectEntities(out var statsEntity, out var head);
        if (statsEntity == Entity.Null || head == Entity.Null) return;

        var stats = World.Get<SnakeStats>(statsEntity);
        if (!stats.Started || stats.GameOver) return;

        var headCell = World.Get<GridCell>(head);
        var dir = World.Get<SnakeDirection>(head).Dir;
        var newCell = NextCell(headCell, dir);

        if (IsWall(newCell) || BodyOccupies(entities, newCell))
        {
            World.Set(statsEntity, new SnakeStats(stats.Score, true, stats.Started));
            return;
        }

        var body = CollectBody(entities);
        var oldTail = body.Length > 0 ? World.Get<GridCell>(body[^1]) : headCell;
        // Any food at the target cell (normal cyan or fallen black) reacts with the
        // head: black food is a deadly obstacle, normal food is eaten and respawned.
        var foodAtCell = FindFoodAtCell(entities, newCell);

        if (foodAtCell != Entity.Null && World.Has<FoodFall>(foodAtCell))
        {
            // The black food is a deadly obstacle: touching it ends the game.
            World.Set(statsEntity, new SnakeStats(stats.Score, true, stats.Started));
            return;
        }

        // Shift: every segment takes the cell of the segment in front of it; the
        // head's old cell moves into the first body segment, the tail cell is dropped.
        var prev = headCell;
        foreach (var segment in body)
        {
            var next = World.Get<GridCell>(segment);
            World.Set(segment, prev);
            prev = next;
        }
        World.Set(head, newCell);

        if (foodAtCell != Entity.Null)
        {
            // Destroy the eaten food so the eaten cell frees up; a new normal food
            // replaces it immediately.
            World.Destroy(foodAtCell);
            World.Create(
                new RenderId(_nextSegmentId++),
                oldTail,
                _bodyColor,
                new SnakeBody());
            World.Set(statsEntity, new SnakeStats(stats.Score + 1, false, stats.Started, ate: true));
            RespawnFood(statsEntity);
        }
    }

    private Entity[] CollectEntities(out Entity statsEntity, out Entity head)
    {
        var entities = new Entity[World.Size];
        World.GetEntities(new QueryDescription(), entities.AsSpan());

        statsEntity = Entity.Null;
        head = Entity.Null;
        foreach (var entity in entities)
        {
            if (!World.IsAlive(entity)) continue;
            if (World.Has<SnakeStats>(entity)) statsEntity = entity;
            else if (World.Has<SnakeHead>(entity)) head = entity;
        }
        return entities;
    }

    /// <summary>Returns the food entity (normal or black) occupying <paramref name="cell"/>, if any.</summary>
    private Entity FindFoodAtCell(Entity[] entities, GridCell cell)
    {
        foreach (var entity in entities)
        {
            if (!World.IsAlive(entity) || !World.Has<SnakeFood>(entity)) continue;
            if (SameCell(World.Get<GridCell>(entity), cell)) return entity;
        }
        return Entity.Null;
    }

    private Entity[] CollectBody(Entity[] entities)
    {
        var body = new List<Entity>();
        foreach (var entity in entities)
        {
            if (!World.IsAlive(entity) || !World.Has<SnakeBody>(entity)) continue;
            body.Add(entity);
        }
        body.Sort((a, b) => World.Get<RenderId>(a).Id.CompareTo(World.Get<RenderId>(b).Id));
        return body.ToArray();
    }

    private bool BodyOccupies(Entity[] entities, GridCell cell)
    {
        foreach (var entity in entities)
        {
            if (!World.IsAlive(entity) || !World.Has<GridCell>(entity)) continue;
            if (World.Has<SnakeFood>(entity)) continue;
            if (SameCell(World.Get<GridCell>(entity), cell)) return true;
        }
        return false;
    }

    private void RespawnFood(Entity statsEntity)
    {
        var entities = new Entity[World.Size];
        World.GetEntities(new QueryDescription(), entities.AsSpan());
        var occupied = new HashSet<long>();
        foreach (var entity in entities)
        {
            if (!World.IsAlive(entity) || !World.Has<GridCell>(entity)) continue;
            var cell = World.Get<GridCell>(entity);
            occupied.Add((cell.Y * (long)_gridWidth) + cell.X);
        }

        for (var attempt = 0; attempt < 100; attempt++)
        {
            var cell = new GridCell(_random.Next(_gridWidth), _random.Next(_gridHeight));
            if (occupied.Contains((cell.Y * (long)_gridWidth) + cell.X)) continue;
            World.Create(
                new RenderId(_nextFoodId()),
                cell,
                _foodColor,
                new FoodAge(0f),
                new SnakeFood());
            var stats = World.Get<SnakeStats>(statsEntity);
            World.Set(statsEntity, new SnakeStats(stats.Score, stats.GameOver, stats.Started, stats.Ate, foodSpawned: true));
            return;
        }
    }

    private static GridCell NextCell(GridCell cell, SnakeDir dir) => dir switch
    {
        SnakeDir.Up => new GridCell(cell.X, cell.Y - 1),
        SnakeDir.Down => new GridCell(cell.X, cell.Y + 1),
        SnakeDir.Left => new GridCell(cell.X - 1, cell.Y),
        SnakeDir.Right => new GridCell(cell.X + 1, cell.Y),
        _ => cell,
    };

    private bool IsWall(GridCell cell) =>
        cell.X < 0 || cell.X >= _gridWidth || cell.Y < 0 || cell.Y >= _gridHeight;

    private static bool SameCell(GridCell a, GridCell b) => a.X == b.X && a.Y == b.Y;
}
