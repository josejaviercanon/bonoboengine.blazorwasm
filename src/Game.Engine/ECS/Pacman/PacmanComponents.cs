using Arch.AOT.SourceGenerator;

namespace Game.Engine.ECS.Pacman;

/// <summary>Fixed gameplay and render constants for the Pacman scene.</summary>
public static class PacmanConfig
{
    public const float TickIntervalSeconds = 1f / 60f;
    public const float CellSize = 8f;
    public const int InitialLives = 3;
    public const int InitialLevel = 1;
    public const float PlayerSpeed = 72f;
    public const float GhostSpeed = 66f;
    public const float FrightenedGhostSpeed = 44f;
    public const float EyesSpeed = 96f;
    public const float FrightenedDurationSeconds = 6f;
    public const float ModeFirstScatterSeconds = 7f;
    public const float ModeFirstChaseSeconds = 20f;
    public const float ModeSecondScatterSeconds = 7f;
    public const float ModeSecondChaseSeconds = 20f;
    public const float ModeThirdScatterSeconds = 5f;
    public const float ModeThirdChaseSeconds = 20f;
    public const float ModeFourthScatterSeconds = 5f;
    public const float ModeFourthChaseSeconds = 9999f;
    public const int PelletScore = 10;
    public const int PowerPelletScore = 50;
    public const int GhostScore = 200;
}

public enum PacmanDirection : byte
{
    Up,
    Left,
    Down,
    Right,
    None,
}

public enum PacmanSpriteKind : byte
{
    Player,
    Blinky,
    Pinky,
    Inky,
    Clyde,
    Pellet,
    PowerPellet,
}

public enum PacmanGhostRole : byte
{
    Blinky,
    Pinky,
    Inky,
    Clyde,
}

public enum PacmanGhostMode : byte
{
    Scatter,
    Chase,
    Frightened,
    Eyes,
}

/// <summary>Grid coordinate used by maze and ghost decision logic.</summary>
public readonly record struct PacmanCell(int X, int Y);

/// <summary>Continuous screen-space transform. Previous position supports client interpolation.</summary>
[Component]
public struct PacmanTransform
{
    public float X;
    public float Y;
    public float PreviousX;
    public float PreviousY;

    public PacmanTransform(float x, float y)
    {
        X = x;
        Y = y;
        PreviousX = x;
        PreviousY = y;
    }
}

/// <summary>Velocity and scalar speed for a moving actor.</summary>
[Component]
public struct PacmanMotion
{
    public float VelocityX;
    public float VelocityY;
    public float Speed;

    public PacmanMotion(float speed = 0f)
    {
        VelocityX = 0f;
        VelocityY = 0f;
        Speed = speed;
    }

    public PacmanMotion(float velocityX, float velocityY, float speed)
    {
        VelocityX = velocityX;
        VelocityY = velocityY;
        Speed = speed;
    }
}

/// <summary>Current and requested direction. Client input only changes Requested.</summary>
[Component]
public struct PacmanFacing
{
    public int Current;
    public int Requested;

    public PacmanFacing(PacmanDirection current, PacmanDirection requested = PacmanDirection.None)
    {
        Current = (int)current;
        Requested = (int)requested;
    }

    public PacmanDirection CurrentDirection => (PacmanDirection)Current;
    public PacmanDirection RequestedDirection => (PacmanDirection)Requested;
}

/// <summary>Presentation metadata; gameplay systems never depend on renderer state.</summary>
[Component]
public struct PacmanSprite
{
    public int Kind;
    public byte R;
    public byte G;
    public byte B;
    public bool Visible;

    public PacmanSprite(PacmanSpriteKind kind, byte r, byte g, byte b, bool visible = true)
    {
        Kind = (int)kind;
        R = r;
        G = g;
        B = b;
        Visible = visible;
    }
}

/// <summary>Marks the single Pacman player entity.</summary>
[Component]
public struct PacmanPlayer
{
}

/// <summary>Marks a ghost entity and stores all ghost AI state as plain data.</summary>
[Component]
public struct PacmanGhostState
{
    public int Role;
    public int Mode;
    public int HomeX;
    public int HomeY;

    public PacmanGhostState(PacmanGhostRole role, PacmanGhostMode mode, PacmanCell home)
    {
        Role = (int)role;
        Mode = (int)mode;
        HomeX = home.X;
        HomeY = home.Y;
    }

    public PacmanGhostRole GhostRole => (PacmanGhostRole)Role;
    public PacmanGhostMode GhostMode => (PacmanGhostMode)Mode;
    public PacmanCell HomeCell => new(HomeX, HomeY);
}

/// <summary>Marks a pellet entity. Power pellets activate frightened mode.</summary>
[Component]
public struct PacmanPellet
{
    public bool Power;

    public PacmanPellet(bool power)
    {
        Power = power;
    }
}

/// <summary>Per-game state stored in a dedicated ECS entity.</summary>
[Component]
public struct PacmanStats
{
    public int Score;
    public int Lives;
    public int Level;
    public int PelletsRemaining;
    public bool Started;
    public bool GameOver;
    public bool Frightened;
    public int ModeIndex;
    public float ModeRemaining;
    public float FrightenedRemaining;
    public int GhostChain;
    public bool AtePellet;
    public bool AtePowerPellet;
    public bool GhostEaten;
    public bool Died;
    public bool LevelUp;

    public PacmanStats(int lives, int level, int pelletsRemaining)
    {
        Score = 0;
        Lives = lives;
        Level = level;
        PelletsRemaining = pelletsRemaining;
        Started = false;
        GameOver = false;
        Frightened = false;
        ModeIndex = 0;
        ModeRemaining = PacmanConfig.ModeFirstScatterSeconds;
        FrightenedRemaining = 0f;
        GhostChain = 0;
        AtePellet = false;
        AtePowerPellet = false;
        GhostEaten = false;
        Died = false;
        LevelUp = false;
    }
}
