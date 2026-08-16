using Arch.AOT.SourceGenerator;

namespace Game.Engine.ECS;

/// <summary>The seven tetromino kinds. Int-backed so the component stays blittable.</summary>
public enum TetrominoType
{
    I,
    J,
    L,
    O,
    S,
    T,
    Z
}

/// <summary>Marks a settled block entity occupying one court cell.</summary>
[Component]
public struct TetrisBlock
{
}

/// <summary>
///     Marks the single active-piece entity. Its top-left/anchor cell is stored in
///     <see cref="GridCell"/>; the four occupied cells are derived from the type's
///     rotation mask. One entity per active piece, not per cell.
/// </summary>
[Component]
public struct TetrisPiece
{
    public int Type;
    public int Rotation;

    public TetrisPiece(TetrominoType type, int rotation)
    {
        Type = (int)type;
        Rotation = rotation;
    }
}

/// <summary>Per-game mutable state kept on a dedicated stats entity.
/// <see cref="Locked"/> and <see cref="LinesCleared"/> are set by the gravity system and
/// consumed by the simulation when building the next render signal (client edge events).
/// <see cref="Dirty"/> is set whenever a system changed the board; the simulation emits a
/// render signal exactly once per dirty batch, then clears the flag.</summary>
[Component]
public struct TetrisStats
{
    public int Score;
    public int Rows;
    public int Level;
    public bool GameOver;
    public bool Started;
    public bool Locked;
    public int LinesCleared;
    public bool Dirty;

    public TetrisStats(int score, int rows, int level, bool gameOver, bool started = false, bool locked = false,
        int linesCleared = 0, bool dirty = false)
    {
        Score = score;
        Rows = rows;
        Level = level;
        GameOver = gameOver;
        Started = started;
        Locked = locked;
        LinesCleared = linesCleared;
        Dirty = dirty;
    }
}

/// <summary>
///     Tetromino geometry from the reference game (<c>src/Temp/javascript-tetris</c>).
///     Each entry holds the 4 rotation masks (16 bits = 4x4 box, MSB = top-left) and
///     the piece size, matching the reference exactly.
/// </summary>
public static class Tetromino
{
    public static readonly int[][] Masks =
    {
        new[] { 0x0F00, 0x2222, 0x00F0, 0x4444 }, // I
        new[] { 0x44C0, 0x8E00, 0x6440, 0x0E20 }, // J
        new[] { 0x4460, 0x0E80, 0xC440, 0x2E00 }, // L
        new[] { 0xCC00, 0xCC00, 0xCC00, 0xCC00 }, // O
        new[] { 0x06C0, 0x8C40, 0x6C00, 0x4620 }, // S
        new[] { 0x0E40, 0x4C40, 0x4E00, 0x4640 }, // T
        new[] { 0x0C60, 0x4C80, 0xC600, 0x2640 }, // Z
    };

    public static readonly int[] Sizes = { 4, 3, 3, 2, 3, 3, 3 };

    public static readonly SpriteColor[] Colors =
    {
        new(34, 211, 238),  // I cyan
        new(59, 130, 246),  // J blue
        new(249, 115, 22),  // L orange
        new(250, 204, 21),  // O yellow
        new(34, 197, 94),   // S green
        new(168, 85, 247),  // T purple
        new(239, 68, 68),   // Z red
    };

    public static int Mask(TetrominoType type, int rotation) =>
        Masks[(int)type][(rotation & 3)];

    public static int Size(TetrominoType type) => Sizes[(int)type];

    public static SpriteColor Color(TetrominoType type) => Colors[(int)type];

    /// <summary>Next rotation index (wraps 0..3, matching the reference).</summary>
    public static int NextRotation(int rotation) => (rotation + 1) & 3;
}