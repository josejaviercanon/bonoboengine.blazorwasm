namespace Game.Engine.ECS.Pacman;

/// <summary>
/// Immutable Pacman maze topology and grid math. Legacy source used inconsistent
/// 28/29/30/31 bounds; this port makes its 29 x 31 contract explicit.
/// </summary>
public static class PacmanMaze
{
    public const int Width = 29;
    public const int Height = 31;
    public const float CellSize = PacmanConfig.CellSize;
    public const float BoardWidth = Width * CellSize;
    public const float BoardHeight = Height * CellSize;

    private static readonly IReadOnlyList<string> Layout = new[]
    {
        "                             ",
        " oooooooooooo  oooooooooooo  ",
        " o    o     o  o     o    o  ",
        " *    o     o  o     o    *  ",
        " o    o     o  o     o    o  ",
        " oooooooooooooooooooooooooo  ",
        " o    o  o        o  o    o  ",
        " o    o  o        o  o    o  ",
        " oooooo  oooo  oooo  oooooo  ",
        "      o     +  +     o       ",
        "      o     +  +     o       ",
        "      o  ++++++++++  o       ",
        "      o  +        +  o       ",
        "      o  +        +  o       ",
        "++++++o+++        +++o+++++++",
        "      o  +        +  o       ",
        "      o  +        +  o       ",
        "      o  ++++++++++  o       ",
        "      o  +        +  o       ",
        "      o  +        +  o       ",
        " oooooooooooo  oooooooooooo  ",
        " o    o     o  o     o    o  ",
        " o    o     o  o     o    o  ",
        " *oo  ooooooo++ooooooo  oo*  ",
        "   o  o  o        o  o  o    ",
        "   o  o  o        o  o  o    ",
        " oooooo  oooo  oooo  oooooo  ",
        " o          o  o          o  ",
        " o          o  o          o  ",
        " oooooooooooooooooooooooooo  ",
        "                             ",
    };

    private static readonly IReadOnlyList<PacmanCell> PelletCellsInternal = BuildPelletCells();

    public static IReadOnlyList<string> Rows => Layout;
    public static IReadOnlyList<PacmanCell> PelletCells => PelletCellsInternal;
    public static int PelletCount => PelletCellsInternal.Count;

    public static bool IsInside(PacmanCell cell) =>
        cell.X >= 0 && cell.X < Width && cell.Y >= 0 && cell.Y < Height;

    public static bool IsWall(PacmanCell cell) =>
        !IsInside(cell) || Layout[cell.Y][cell.X] == ' ';

    public static bool IsOpen(PacmanCell cell) => !IsWall(cell);

    public static bool IsTunnel(PacmanCell cell) =>
        cell.Y == 14 && (cell.X <= 5 || cell.X >= 22);

    public static bool IsSpecialIntersection(PacmanCell cell) =>
        cell == new PacmanCell(12, 11) ||
        cell == new PacmanCell(15, 11) ||
        cell == new PacmanCell(12, 26) ||
        cell == new PacmanCell(15, 26);

    public static char ContentAt(PacmanCell cell) =>
        IsInside(cell) ? Layout[cell.Y][cell.X] : ' ';

    public static PacmanCell NextCell(PacmanCell cell, PacmanDirection direction)
    {
        var next = direction switch
        {
            PacmanDirection.Up => new PacmanCell(cell.X, cell.Y - 1),
            PacmanDirection.Left => new PacmanCell(cell.X - 1, cell.Y),
            PacmanDirection.Down => new PacmanCell(cell.X, cell.Y + 1),
            PacmanDirection.Right => new PacmanCell(cell.X + 1, cell.Y),
            _ => cell,
        };

        if (IsTunnel(cell))
        {
            if (next.X < 0) next = new PacmanCell(Width - 1, next.Y);
            if (next.X >= Width) next = new PacmanCell(0, next.Y);
        }

        return next;
    }

    public static bool CanMove(PacmanCell cell, PacmanDirection direction) =>
        direction != PacmanDirection.None && IsOpen(NextCell(cell, direction));

    public static PacmanCell CellFromPosition(float x, float y)
    {
        var cellX = Math.Clamp((int)MathF.Round((x - (CellSize / 2f)) / CellSize), 0, Width - 1);
        var cellY = Math.Clamp((int)MathF.Round((y - (CellSize / 2f)) / CellSize), 0, Height - 1);
        return new PacmanCell(cellX, cellY);
    }

    public static (float X, float Y) CenterOf(PacmanCell cell) =>
        ((cell.X + 0.5f) * CellSize, (cell.Y + 0.5f) * CellSize);

    public static bool IsNearCenter(float x, float y, PacmanCell cell, float tolerance = 0.8f)
    {
        var center = CenterOf(cell);
        var dx = x - center.X;
        var dy = y - center.Y;
        return (dx * dx) + (dy * dy) <= tolerance * tolerance;
    }

    public static PacmanDirection Opposite(PacmanDirection direction) => direction switch
    {
        PacmanDirection.Up => PacmanDirection.Down,
        PacmanDirection.Left => PacmanDirection.Right,
        PacmanDirection.Down => PacmanDirection.Up,
        PacmanDirection.Right => PacmanDirection.Left,
        _ => PacmanDirection.None,
    };

    public static (float X, float Y) VectorFor(PacmanDirection direction) => direction switch
    {
        PacmanDirection.Up => (0f, -1f),
        PacmanDirection.Left => (-1f, 0f),
        PacmanDirection.Down => (0f, 1f),
        PacmanDirection.Right => (1f, 0f),
        _ => (0f, 0f),
    };

    public static float DistanceSquared(PacmanCell left, PacmanCell right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        return (dx * dx) + (dy * dy);
    }

    private static IReadOnlyList<PacmanCell> BuildPelletCells()
    {
        var cells = new List<PacmanCell>();
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var content = Layout[y][x];
                if (content is 'o' or '*') cells.Add(new PacmanCell(x, y));
            }
        }

        return cells;
    }
}
