using Arch.Core;
using Arch.Systems;
using Game.Engine.ECS;

namespace Game.Engine.ECS.Tetris;

/// <summary>Player commands validated against the court by the input system.</summary>
public enum TetrisCommand
{
    Left,
    Right,
    Rotate,
    Down
}

/// <summary>
///     Pure grid helpers shared by the tetris systems. The court occupancy is derived
///     from <see cref="TetrisBlock"/> entities (C# is the sole authority); cells of a
///     piece come from the reference bit-mask layout.
/// </summary>
public static class TetrisGrid
{
    /// <summary>Enumerates the occupied cells of a piece at (x, y) with the given rotation.</summary>
    public static IEnumerable<(int X, int Y)> Cells(TetrominoType type, int x, int y, int rotation)
    {
        var mask = Tetromino.Mask(type, rotation);
        var row = 0;
        var col = 0;
        for (var bit = 0x8000; bit > 0; bit >>= 1)
        {
            if ((mask & bit) != 0) yield return (x + col, y + row);
            if (++col == 4)
            {
                col = 0;
                ++row;
            }
        }
    }

    /// <summary>True when the piece fits: inside the court and not overlapping a settled block.</summary>
    public static bool Fits(World world, int gridWidth, int gridHeight, TetrominoType type, int x, int y, int rotation)
    {
        var occupied = CollectOccupied(world, gridWidth);
        foreach (var (cx, cy) in Cells(type, x, y, rotation))
        {
            if (cx < 0 || cx >= gridWidth || cy < 0 || cy >= gridHeight) return false;
            if (occupied.Contains((cy * (long)gridWidth) + cx)) return false;
        }
        return true;
    }

    /// <summary>Settled-block cell keys (row * width + col).</summary>
    public static HashSet<long> CollectOccupied(World world, int gridWidth)
    {
        var occupied = new HashSet<long>();
        var entities = new Entity[world.Size];
        world.GetEntities(new QueryDescription(), entities.AsSpan());
        foreach (var entity in entities)
        {
            if (!world.IsAlive(entity) || !world.Has<TetrisBlock>(entity)) continue;
            var cell = world.Get<GridCell>(entity);
            occupied.Add((cell.Y * (long)gridWidth) + cell.X);
        }
        return occupied;
    }

    public static Entity FindPiece(World world)
    {
        var entities = new Entity[world.Size];
        world.GetEntities(new QueryDescription(), entities.AsSpan());
        foreach (var entity in entities)
        {
            if (world.IsAlive(entity) && world.Has<TetrisPiece>(entity)) return entity;
        }
        return Entity.Null;
    }

    public static Entity FindStats(World world)
    {
        var entities = new Entity[world.Size];
        world.GetEntities(new QueryDescription(), entities.AsSpan());
        foreach (var entity in entities)
        {
            if (world.IsAlive(entity) && world.Has<TetrisStats>(entity)) return entity;
        }
        return Entity.Null;
    }
}

/// <summary>
///     Consumes buffered player commands (queued by <see cref="TetrisSimulation.QueueInput"/>)
///     and steers the active piece. Every move/rotation is validated against the court and the
///     settled blocks before it is applied; blocked moves are dropped silently. Soft drop
///     delegates to the gravity system's single-step lock. C# remains the sole authority.
/// </summary>
public partial class TetrisInputSystem : BaseSystem<World, double>
{
    private readonly Queue<TetrisCommand> _pending;
    private readonly TetrisGravitySystem _gravity;
    private readonly int _gridWidth;
    private readonly int _gridHeight;

    public TetrisInputSystem(World world, Queue<TetrisCommand> pending, TetrisGravitySystem gravity,
        int gridWidth, int gridHeight) : base(world)
    {
        _pending = pending;
        _gravity = gravity;
        _gridWidth = gridWidth;
        _gridHeight = gridHeight;
    }

    public override void Update(in double t)
    {
        if (_pending.Count == 0) return;

        var statsEntity = TetrisGrid.FindStats(World);
        if (statsEntity == Entity.Null) { _pending.Clear(); return; }
        var stats = World.Get<TetrisStats>(statsEntity);
        if (!stats.Started || stats.GameOver) { _pending.Clear(); return; }

        var piece = TetrisGrid.FindPiece(World);
        if (piece == Entity.Null) { _pending.Clear(); return; }

        while (_pending.Count > 0)
        {
            var command = _pending.Dequeue();
            Apply(command, statsEntity, piece);
            // A soft drop may lock the piece and destroy this entity; drop the rest.
            if (!World.IsAlive(piece)) { _pending.Clear(); return; }
        }
    }

    private void Apply(TetrisCommand command, Entity statsEntity, Entity piece)
    {
        var cell = World.Get<GridCell>(piece);
        var tp = World.Get<TetrisPiece>(piece);
        var type = (TetrominoType)tp.Type;

        switch (command)
        {
            case TetrisCommand.Left:
                if (TetrisGrid.Fits(World, _gridWidth, _gridHeight, type, cell.X - 1, cell.Y, tp.Rotation))
                {
                    World.Set(piece, new GridCell(cell.X - 1, cell.Y));
                    MarkDirty(statsEntity);
                }
                break;
            case TetrisCommand.Right:
                if (TetrisGrid.Fits(World, _gridWidth, _gridHeight, type, cell.X + 1, cell.Y, tp.Rotation))
                {
                    World.Set(piece, new GridCell(cell.X + 1, cell.Y));
                    MarkDirty(statsEntity);
                }
                break;
            case TetrisCommand.Rotate:
                var newRotation = Tetromino.NextRotation(tp.Rotation);
                if (TetrisGrid.Fits(World, _gridWidth, _gridHeight, type, cell.X, cell.Y, newRotation))
                {
                    World.Set(piece, new TetrisPiece(type, newRotation));
                    MarkDirty(statsEntity);
                }
                break;
            case TetrisCommand.Down:
                _gravity.SoftDrop();
                break;
        }
    }

    private void MarkDirty(Entity statsEntity)
    {
        var s = World.Get<TetrisStats>(statsEntity);
        World.Set(statsEntity, new TetrisStats(s.Score, s.Rows, s.Level, s.GameOver, s.Started, s.Locked, s.LinesCleared, dirty: true));
    }
}

/// <summary>
///     Drives the piece down at the rows-based gravity rate (reference formula
///     <c>max(0.1, 0.6 - 0.005 * rows)</c>). On an impossible move the piece locks:
///     its cells become <see cref="TetrisBlock"/> entities, full rows are removed,
///     scoring is applied and the next piece spawns. A spawn that cannot fit is game over.
/// </summary>
public partial class TetrisGravitySystem : BaseSystem<World, double>
{
    private readonly int _gridWidth;
    private readonly int _gridHeight;
    private readonly Random _random;
    private readonly Queue<TetrisCommand> _pending;
    private readonly List<TetrominoType> _bag = new();
    private double _accumulator;

    public TetrisGravitySystem(World world, int gridWidth, int gridHeight, Random random,
        Queue<TetrisCommand> pending) : base(world)
    {
        _gridWidth = gridWidth;
        _gridHeight = gridHeight;
        _random = random;
        _pending = pending;
    }

    public override void Update(in double t)
    {
        var statsEntity = TetrisGrid.FindStats(World);
        if (statsEntity == Entity.Null) return;
        var stats = World.Get<TetrisStats>(statsEntity);
        if (!stats.Started || stats.GameOver) return;

        _accumulator += t;
        var step = StepIntervalSeconds(stats.Rows);
        while (_accumulator >= step)
        {
            _accumulator -= step;
            if (!SoftDrop()) break;
            var current = World.Get<TetrisStats>(statsEntity);
            if (current.GameOver) break;
        }
    }

    /// <summary>Reference gravity: rows clear speed up the piece.</summary>
    public double StepIntervalSeconds(int rows) => Math.Max(0.1, 0.6 - (0.005 * rows));

    /// <summary>
    ///     Advances the active piece one cell down, or locks it when blocked. Returns
    ///     true when the piece moved (gravity may continue), false after a lock.
    /// </summary>
    public bool SoftDrop()
    {
        var piece = TetrisGrid.FindPiece(World);
        if (piece == Entity.Null) return false;

        var statsEntity = TetrisGrid.FindStats(World);
        if (statsEntity == Entity.Null) return false;
        var stats = World.Get<TetrisStats>(statsEntity);
        if (!stats.Started || stats.GameOver) return false;

        var cell = World.Get<GridCell>(piece);
        var tp = World.Get<TetrisPiece>(piece);
        var type = (TetrominoType)tp.Type;

        if (TetrisGrid.Fits(World, _gridWidth, _gridHeight, type, cell.X, cell.Y + 1, tp.Rotation))
        {
            World.Set(piece, new GridCell(cell.X, cell.Y + 1));
            MarkDirty(statsEntity);
            return true;
        }

        LockPiece(piece, statsEntity, cell, type, tp.Rotation);
        return false;
    }

    private void LockPiece(Entity piece, Entity statsEntity, GridCell cell, TetrominoType type, int rotation)
    {
        foreach (var (x, y) in TetrisGrid.Cells(type, cell.X, cell.Y, rotation))
        {
            World.Create(
                new RenderId(TetrisSimulation.NextBlockId()),
                new GridCell(x, y),
                Tetromino.Color(type),
                new TetrisBlock());
        }
        World.Destroy(piece);

        var stats = World.Get<TetrisStats>(statsEntity);
        var cleared = RemoveLines();
        var score = stats.Score + 10 + (cleared > 0 ? LineClearScore(cleared) : 0);
        var rows = stats.Rows + cleared;
        var gameOver = false;

        SpawnNext();

        var current = World.Get<TetrisStats>(statsEntity);
        gameOver = current.GameOver;

        World.Set(statsEntity, new TetrisStats(score, rows, (rows / 10) + 1, gameOver,
            started: true, locked: true, linesCleared: cleared, dirty: true));
        _pending.Clear();
    }

    /// <summary>Scoring for cleared lines: 1 -> 100, 2 -> 200, 3 -> 400, 4 -> 800.</summary>
    public static int LineClearScore(int lines) => 100 * (1 << (lines - 1));

    /// <summary>
    ///     Removes full rows bottom-up (reference behavior): a full row's block entities are
    ///     destroyed and every block above shifts down one cell; the same row is re-checked
    ///     because content slid into it. Returns the number of cleared rows.
    /// </summary>
    public int RemoveLines()
    {
        var entities = new Entity[World.Size];
        World.GetEntities(new QueryDescription(), entities.AsSpan());

        var blocks = new List<Entity>();
        foreach (var entity in entities)
        {
            if (World.IsAlive(entity) && World.Has<TetrisBlock>(entity)) blocks.Add(entity);
        }

        var cleared = 0;
        for (var y = _gridHeight - 1; y >= 0; --y)
        {
            if (!IsRowFull(blocks, y)) continue;

            foreach (var block in blocks)
            {
                if (!World.IsAlive(block)) continue;
                if (World.Get<GridCell>(block).Y == y) World.Destroy(block);
            }
            foreach (var block in blocks)
            {
                if (!World.IsAlive(block)) continue;
                var cell = World.Get<GridCell>(block);
                if (cell.Y < y) World.Set(block, new GridCell(cell.X, cell.Y + 1));
            }

            cleared++;
            y++; // re-check the same row: blocks above just slid into it
        }
        return cleared;
    }

    private bool IsRowFull(List<Entity> blocks, int y)
    {
        var mask = 0L;
        foreach (var block in blocks)
        {
            if (!World.IsAlive(block)) continue;
            var cell = World.Get<GridCell>(block);
            if (cell.Y == y) mask |= 1L << cell.X;
        }
        return mask == ((1L << _gridWidth) - 1);
    }

    /// <summary>Spawns the next piece from the 4-of-each bag and flags game over if it cannot fit.</summary>
    public void SpawnNext()
    {
        var existing = TetrisGrid.FindPiece(World);
        if (existing != Entity.Null) World.Destroy(existing);

        var type = NextFromBag();
        var size = Tetromino.Size(type);
        var x = _random.Next(_gridWidth - size + 1);
        World.Create(new RenderId(0), new GridCell(x, 0), new TetrisPiece(type, 0));

        var statsEntity = TetrisGrid.FindStats(World);
        if (statsEntity == Entity.Null) return;
        if (!TetrisGrid.Fits(World, _gridWidth, _gridHeight, type, x, 0, 0))
        {
            var stats = World.Get<TetrisStats>(statsEntity);
            World.Set(statsEntity, new TetrisStats(stats.Score, stats.Rows, stats.Level,
                gameOver: true, stats.Started, stats.Locked, stats.LinesCleared, dirty: true));
        }
    }

    private TetrominoType NextFromBag()
    {
        if (_bag.Count == 0)
        {
            for (var type = 0; type < 7; type++)
            {
                for (var copy = 0; copy < 4; copy++) _bag.Add((TetrominoType)type);
            }
        }
        var index = _random.Next(_bag.Count);
        var picked = _bag[index];
        _bag.RemoveAt(index);
        return picked;
    }

    private void MarkDirty(Entity statsEntity)
    {
        var s = World.Get<TetrisStats>(statsEntity);
        World.Set(statsEntity, new TetrisStats(s.Score, s.Rows, s.Level, s.GameOver, s.Started, s.Locked, s.LinesCleared, dirty: true));
    }
}