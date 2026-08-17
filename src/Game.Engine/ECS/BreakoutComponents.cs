using Arch.AOT.SourceGenerator;

namespace Game.Engine.ECS;

/// <summary>
///     Tuning constants for the breakout court. Derived from the reference game
///     (<c>src/Temp/javascript-breakout</c>): court 30 x 25 chunks, chunk = 20 px,
///     ball radius 0.3 chunk, base speed 15 chunk/s, paddle 6 x 1 chunks at
///     20 chunk/s, 3 initial lives (max 5).
/// </summary>
public static class BreakoutConfig
{
    public const float ChunkSize = 20f;
    public const int CourtChunksX = 30;
    public const int CourtChunksY = 25;
    public const float CourtWidth = CourtChunksX * ChunkSize;
    public const float CourtHeight = CourtChunksY * ChunkSize;
    public const float WallSize = ChunkSize;
    public const float PaddleWidth = 6f * ChunkSize;
    public const float PaddleHeight = 1f * ChunkSize;
    public const float BallRadius = 0.3f * ChunkSize;
    public const float BaseBallSpeed = 15f * ChunkSize;
    public const float BallMaxSpeed = BaseBallSpeed * 1.5f;
    public const float PaddleSpeed = 20f * ChunkSize;
    public const int InitialLives = 3;
    public const int MaxLives = 5;
    public const int BrickRenderIdStart = 1000;
}

/// <summary>Ball entity: radius, current speed and whether it has been launched.</summary>
[Component]
public struct BreakoutBall
{
    public float Radius;
    public float Speed;
    public bool Moving;

    public BreakoutBall(float radius, float speed, bool moving)
    {
        Radius = radius;
        Speed = speed;
        Moving = moving;
    }
}

/// <summary>Paddle entity: size plus the currently held input direction (client-suggested).</summary>
[Component]
public struct BreakoutPaddle
{
    public float Width;
    public float Height;
    public bool LeftHeld;
    public bool RightHeld;

    public BreakoutPaddle(float width, float height, bool leftHeld = false, bool rightHeld = false)
    {
        Width = width;
        Height = height;
        LeftHeld = leftHeld;
        RightHeld = rightHeld;
    }
}

/// <summary>
///     One brick entity per contiguous run of identical characters in a level row
///     (reference merging: "yyyyy" is a single 5-wide brick). Score derives from the
///     row: <c>(CourtChunksY - row) * 5</c>.
/// </summary>
[Component]
public struct BreakoutBrick
{
    public int Score;
    public float Width;

    public BreakoutBrick(int score, float width)
    {
        Score = score;
        Width = width;
    }
}

/// <summary>
///     Per-game mutable state kept on a dedicated stats entity. <see cref="BrickHit"/>,
///     <see cref="PaddleHit"/>, <see cref="LevelUp"/> and <see cref="LoseLife"/> are
///     ECS-originated edge events consumed once per emitted signal (sound triggers).
/// </summary>
[Component]
public struct BreakoutStats
{
    public int Score;
    public int Lives;
    public int Level;
    public bool GameOver;
    public bool Started;
    public bool BrickHit;
    public bool PaddleHit;
    public bool LevelUp;
    public bool LoseLife;

    public BreakoutStats(int score, int lives, int level, bool gameOver = false, bool started = false,
        bool brickHit = false, bool paddleHit = false, bool levelUp = false, bool loseLife = false)
    {
        Score = score;
        Lives = lives;
        Level = level;
        GameOver = gameOver;
        Started = started;
        BrickHit = brickHit;
        PaddleHit = paddleHit;
        LevelUp = levelUp;
        LoseLife = loseLife;
    }
}

/// <summary>
///     Plain-data snapshot of one breakout sprite, serializable for the SSR payload
///     and SSE stream. Carries width/height so the client can render variable-width
///     bricks, the wide paddle and the circular ball without per-sprite interop.
///     X/Y are the sprite center.
/// </summary>
public record struct BreakoutSpriteState(
    int Id, float X, float Y, float Width, float Height, byte R, byte G, byte B);

/// <summary>Client-suggested breakout input. Held flags are absolute state (no lost key-up
/// edges); <see cref="Launch"/> is a one-shot consumed by the input system.</summary>
public record struct BreakoutInputRequest(bool Left, bool Right, bool Launch);