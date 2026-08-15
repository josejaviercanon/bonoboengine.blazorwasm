using Arch.AOT.SourceGenerator;

namespace Game.Engine.ECS;

/// <summary>
///     Position of an entity in world (screen) space. XNA-free replacement of the
///     original <c>BonoboGame.Core.Dx12.Components.Position</c> (Vector2).
/// </summary>
[Component]
public struct Position
{
    public float X;
    public float Y;

    public Position(float x, float y)
    {
        X = x;
        Y = y;
    }
}

/// <summary>
///     Velocity of an entity in pixels per second.
/// </summary>
[Component]
public struct Velocity
{
    public float X;
    public float Y;

    public Velocity(float x, float y)
    {
        X = x;
        Y = y;
    }
}

/// <summary>
///     Sprite fill color. XNA-free replacement of the original <c>Sprite.Color</c>.
/// </summary>
[Component]
public struct SpriteColor
{
    public byte R;
    public byte G;
    public byte B;

    public SpriteColor(byte r, byte g, byte b)
    {
        R = r;
        G = g;
        B = b;
    }
}

/// <summary>
///     Stable numeric id mapping an entity to a client-side PixiJS sprite.
///     The raw Arch <see cref="Arch.Core.Entity"/> is not serializable without Arch.Persistence.
/// </summary>
[Component]
public struct RenderId
{
    public int Id;

    public RenderId(int id)
    {
        Id = id;
    }
}

/// <summary>Cell coordinate on the snake grid. World pixel position is derived as cell * cell size.</summary>
[Component]
public struct GridCell
{
    public int X;
    public int Y;

    public GridCell(int x, int y)
    {
        X = x;
        Y = y;
    }
}

/// <summary>Direction a snake head is facing. Stored as int so the component stays blittable.</summary>
[Component]
public struct SnakeDirection
{
    public int Value;

    public SnakeDirection(SnakeDir dir)
    {
        Value = (int)dir;
    }

    public SnakeDir Dir => (SnakeDir)Value;
}

/// <summary>Direction values used by the snake. Plain int-backed enum, safe for Arch components.</summary>
public enum SnakeDir
{
    Up,
    Down,
    Left,
    Right
}

/// <summary>Marks the single snake head entity.</summary>
[Component]
public struct SnakeHead
{
}

/// <summary>Marks a snake body segment entity.</summary>
[Component]
public struct SnakeBody
{
}

/// <summary>Marks the single food entity.</summary>
[Component]
public struct SnakeFood
{
}

/// <summary>Per-game mutable state kept on a dedicated stats entity.
/// <see cref="Ate"/> and <see cref="FoodSpawned"/> are set by the step system and consumed
/// by the simulation when building the next render signal (they drive client events
/// like the eat sound and food-spawn sound).</summary>
[Component]
public struct SnakeStats
{
    public int Score;
    public bool GameOver;
    public bool Started;
    public bool Ate;
    public bool FoodSpawned;

    public SnakeStats(int score, bool gameOver, bool started = false, bool ate = false, bool foodSpawned = false)
    {
        Score = score;
        GameOver = gameOver;
        Started = started;
        Ate = ate;
        FoodSpawned = foodSpawned;
    }
}
