using Arch.AOT.SourceGenerator;

namespace Game.Engine.ECS.Racer;

/// <summary>Fixed values ported from javascript-racer v4.final.</summary>
public static class RacerConfig
{
    public const double TickIntervalSeconds = 1.0 / 60.0;
    public const float SegmentLength = 200f;
    public const int RumbleLength = 3;
    public const float DefaultRoadWidth = 2000f;
    public const int DefaultLanes = 3;
    public const float DefaultCameraHeight = 1000f;
    public const int DefaultDrawDistance = 300;
    public const float DefaultFieldOfView = 100f;
    public const float DefaultFogDensity = 5f;
    public const float Centrifugal = 0.3f;
    public const int TotalCars = 200;
    public const float PlayerSpriteWidth = 80f;
    public const float SpriteScale = 0.3f / PlayerSpriteWidth;

    public static readonly RacerSettings DefaultSettings = new(
        DefaultLanes,
        DefaultRoadWidth,
        DefaultCameraHeight,
        DefaultDrawDistance,
        DefaultFieldOfView,
        DefaultFogDensity,
        1f);

    public static float CameraDepth(float fieldOfView) =>
        1f / MathF.Tan((fieldOfView * 0.5f) * MathF.PI / 180f);

    public static float MaxSpeed => SegmentLength / (float)TickIntervalSeconds;
    public static float Acceleration => MaxSpeed / 5f;
    public static float Braking => -MaxSpeed;
    public static float Deceleration => -MaxSpeed / 5f;
    public static float OffRoadDeceleration => -MaxSpeed / 2f;
    public static float OffRoadLimit => MaxSpeed / 4f;

    public static float SpriteWidth(RacerSpriteKind kind) => kind switch
    {
        RacerSpriteKind.PalmTree => 215f,
        RacerSpriteKind.Billboard08 => 385f,
        RacerSpriteKind.Tree1 => 360f,
        RacerSpriteKind.DeadTree1 => 135f,
        RacerSpriteKind.Billboard09 => 328f,
        RacerSpriteKind.Boulder3 => 320f,
        RacerSpriteKind.Column => 200f,
        RacerSpriteKind.Billboard01 => 300f,
        RacerSpriteKind.Billboard06 => 298f,
        RacerSpriteKind.Billboard05 => 298f,
        RacerSpriteKind.Boulder2 => 298f,
        RacerSpriteKind.Billboard07 => 298f,
        RacerSpriteKind.Tree2 => 282f,
        RacerSpriteKind.Billboard04 => 268f,
        RacerSpriteKind.DeadTree2 => 150f,
        RacerSpriteKind.Boulder1 => 168f,
        RacerSpriteKind.Bush1 => 240f,
        RacerSpriteKind.Cactus => 235f,
        RacerSpriteKind.Bush2 => 232f,
        RacerSpriteKind.Billboard03 => 230f,
        RacerSpriteKind.Billboard02 => 215f,
        RacerSpriteKind.Stump => 195f,
        RacerSpriteKind.Semi => 122f,
        RacerSpriteKind.Truck => 100f,
        RacerSpriteKind.Car03 => 88f,
        _ => 80f,
    };

    public static float SpriteHeight(RacerSpriteKind kind) => kind switch
    {
        RacerSpriteKind.PalmTree => 540f,
        RacerSpriteKind.Billboard08 => 265f,
        RacerSpriteKind.Tree1 => 360f,
        RacerSpriteKind.DeadTree1 => 332f,
        RacerSpriteKind.Billboard09 => 282f,
        RacerSpriteKind.Boulder3 => 220f,
        RacerSpriteKind.Column => 315f,
        RacerSpriteKind.Billboard01 => 170f,
        RacerSpriteKind.Billboard06 => 190f,
        RacerSpriteKind.Billboard05 => 190f,
        RacerSpriteKind.Boulder2 => 140f,
        RacerSpriteKind.Billboard07 => 190f,
        RacerSpriteKind.Tree2 => 295f,
        RacerSpriteKind.Billboard04 => 170f,
        RacerSpriteKind.DeadTree2 => 260f,
        RacerSpriteKind.Boulder1 => 248f,
        RacerSpriteKind.Bush1 => 155f,
        RacerSpriteKind.Cactus => 118f,
        RacerSpriteKind.Bush2 => 152f,
        RacerSpriteKind.Billboard03 => 220f,
        RacerSpriteKind.Billboard02 => 220f,
        RacerSpriteKind.Stump => 140f,
        RacerSpriteKind.Semi => 144f,
        RacerSpriteKind.Truck => 78f,
        RacerSpriteKind.Car03 => 55f,
        RacerSpriteKind.Car02 => 59f,
        RacerSpriteKind.Car04 => 57f,
        RacerSpriteKind.Car01 => 56f,
        _ => 45f,
    };
}

/// <summary>Runtime settings. Server validates and echoes these to the renderer.</summary>
public readonly record struct RacerSettings(
    int Lanes,
    float RoadWidth,
    float CameraHeight,
    int DrawDistance,
    float FieldOfView,
    float FogDensity,
    float ResolutionScale);

public enum RacerSegmentColor : byte
{
    Light,
    Dark,
    Start,
    Finish,
}

/// <summary>Stable discriminator shared by C# snapshots and racer.ts atlas lookup.</summary>
public enum RacerSpriteKind : byte
{
    PalmTree,
    Billboard08,
    Tree1,
    DeadTree1,
    Billboard09,
    Boulder3,
    Column,
    Billboard01,
    Billboard06,
    Billboard05,
    Boulder2,
    Billboard07,
    Tree2,
    Billboard04,
    DeadTree2,
    Boulder1,
    Bush1,
    Cactus,
    Bush2,
    Billboard03,
    Billboard02,
    Stump,
    Semi,
    Truck,
    Car03,
    Car02,
    Car04,
    Car01,
    PlayerUphillLeft,
    PlayerUphillStraight,
    PlayerUphillRight,
    PlayerLeft,
    PlayerStraight,
    PlayerRight,
}

/// <summary>3D transform used by the racer entities. Z is distance along track.</summary>
[Component]
public struct TransformComponent
{
    public float X;
    public float Y;
    public float Z;

    public TransformComponent(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }
}

/// <summary>Held keyboard state. Byte flags keep component layout explicit and blittable.</summary>
[Component]
public struct PlayerInputComponent
{
    public byte Left;
    public byte Right;
    public byte Faster;
    public byte Slower;

    public PlayerInputComponent(bool left, bool right, bool faster, bool slower)
    {
        Left = left ? (byte)1 : (byte)0;
        Right = right ? (byte)1 : (byte)0;
        Faster = faster ? (byte)1 : (byte)0;
        Slower = slower ? (byte)1 : (byte)0;
    }

    public bool IsLeft => Left != 0;
    public bool IsRight => Right != 0;
    public bool IsFaster => Faster != 0;
    public bool IsSlower => Slower != 0;
}

/// <summary>Marks the player entity.</summary>
[Component]
public struct PlayerTag
{
}

/// <summary>Marks an AI traffic car.</summary>
[Component]
public struct AICarComponent
{
    public float Offset;
    public float Speed;
    public float Percent;
    public byte SpriteKind;

    public AICarComponent(float offset, float speed, RacerSpriteKind spriteKind)
    {
        Offset = offset;
        Speed = speed;
        Percent = 0f;
        SpriteKind = (byte)spriteKind;
    }

    public RacerSpriteKind Kind => (RacerSpriteKind)SpriteKind;
}

/// <summary>Axis-aligned width used for authoritative roadside/car collisions.</summary>
[Component]
public struct BoundingBoxComponent
{
    public float Width;
    public float Height;

    public BoundingBoxComponent(float width, float height)
    {
        Width = width;
        Height = height;
    }
}

/// <summary>Client-rendered sprite metadata.</summary>
[Component]
public struct SpriteComponent
{
    public byte Kind;
    public float Width;
    public float Height;

    public SpriteComponent(RacerSpriteKind kind)
    {
        Kind = (byte)kind;
        Width = RacerConfig.SpriteWidth(kind);
        Height = RacerConfig.SpriteHeight(kind);
    }

    public RacerSpriteKind SpriteKind => (RacerSpriteKind)Kind;
}

/// <summary>Marks an entity as part of the batched render snapshot.</summary>
[Component]
public struct RenderTag
{
}

/// <summary>One static road segment. World Z is Index * SegmentLength.</summary>
[Component]
public struct RoadSegmentComponent
{
    public int Index;
    public float P1WorldY;
    public float P2WorldY;
    public float Curve;
    public byte Color;

    public RoadSegmentComponent(int index, float p1WorldY, float p2WorldY, float curve, RacerSegmentColor color)
    {
        Index = index;
        P1WorldY = p1WorldY;
        P2WorldY = p2WorldY;
        Curve = curve;
        Color = (byte)color;
    }
}

/// <summary>Static roadside sprite attached to one segment.</summary>
[Component]
public struct RoadSpriteComponent
{
    public int SegmentIndex;
    public float Offset;

    public RoadSpriteComponent(int segmentIndex, float offset)
    {
        SegmentIndex = segmentIndex;
        Offset = offset;
    }
}

/// <summary>Per-game state kept on one stats entity.</summary>
[Component]
public struct RacerStatsComponent
{
    public float CurrentLapTime;
    public float LastLapTime;
    public float FastLapTime;
    public float PreviousPosition;
    public int Lap;
    public bool Started;
    public bool LapCompleted;
    public bool Collided;

    public RacerStatsComponent(float fastLapTime)
    {
        CurrentLapTime = 0f;
        LastLapTime = 0f;
        FastLapTime = fastLapTime;
        PreviousPosition = 0f;
        Lap = 0;
        Started = true;
        LapCompleted = false;
        Collided = false;
    }
}

public readonly record struct RacerSegmentState(
    int Index, float P1WorldY, float P2WorldY, float Curve, byte Color);

public readonly record struct RacerSceneryState(
    int SegmentIndex, float Offset, byte SpriteKind);

public readonly record struct RacerCarState(
    int Id, float Z, float Offset, float Speed, float Percent, byte SpriteKind);

public readonly record struct RacerPlayerState(
    float X,
    float Z,
    float Speed,
    float CurrentLapTime,
    float LastLapTime,
    float FastLapTime,
    int Lap,
    int Steer,
    bool Uphill);

public sealed record RacerTrackPayload(
    IReadOnlyList<RacerSegmentState> Segments,
    IReadOnlyList<RacerSceneryState> Sprites,
    float TrackLength,
    float SegmentLength,
    int RumbleLength);

public sealed record RacerScenePayload(
    RacerTrackPayload Track,
    RacerPlayerState Player,
    IReadOnlyList<RacerCarState> Cars,
    RacerSettings Settings,
    string StreamUrl);

public sealed record RacerRenderSignal(
    long Seq,
    int EntityCount,
    double TickMs,
    RacerPlayerState Player,
    IReadOnlyList<RacerCarState> Cars,
    RacerSettings Settings,
    bool LapCompleted,
    bool Collided);

/// <summary>Client suggestion; input system copies it into ECS state.</summary>
public readonly record struct RacerInputRequest(bool Left, bool Right, bool Faster, bool Slower);

/// <summary>Validated by RacerSimulation before becoming active settings.</summary>
public readonly record struct RacerConfigRequest(
    int Lanes,
    float RoadWidth,
    float CameraHeight,
    int DrawDistance,
    float FieldOfView,
    float FogDensity,
    float ResolutionScale);
