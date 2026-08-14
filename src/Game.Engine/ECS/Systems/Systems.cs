using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.Systems;
using Game.Engine.ECS;

namespace Game.Engine.ECS.Systems;

/// <summary>
///     Moves entities by their velocity and bounces them inside the world bounds.
///     The per-entity methods are called by source generated *Query methods
///     (<see cref="Arch.Systems.SourceGenerator"/>) when <see cref="BaseSystem{W,T}.Update"/> runs.
/// </summary>
public partial class MovementSystem : BaseSystem<World, double>
{
    private readonly float _width;
    private readonly float _height;

    public MovementSystem(World world, float width, float height) : base(world)
    {
        _width = width;
        _height = height;
    }

    [Query]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Move([Data] double dt, ref Position pos, ref Velocity vel)
    {
        pos.X += (float)(dt * vel.X);
        pos.Y += (float)(dt * vel.Y);
    }

    [Query]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Bounce(ref Position pos, ref Velocity vel)
    {
        if (pos.X < 0) { pos.X = 0; vel.X = -vel.X; }
        if (pos.Y < 0) { pos.Y = 0; vel.Y = -vel.Y; }
        if (pos.X > _width) { pos.X = _width; vel.X = -vel.X; }
        if (pos.Y > _height) { pos.Y = _height; vel.Y = -vel.Y; }
    }
}

/// <summary>
///     Drifts each entity's sprite color slowly over time.
/// </summary>
public partial class ColorSystem : BaseSystem<World, double>
{
    public ColorSystem(World world) : base(world) { }

    [Query]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ShiftColor([Data] double dt, ref SpriteColor color)
    {
        color.R = (byte)((color.R + 1) % 256);
        color.G = (byte)((color.G + (byte)(dt * 8)) % 256);
        color.B = (byte)((color.B + (byte)(dt * 12)) % 256);
    }
}
