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
