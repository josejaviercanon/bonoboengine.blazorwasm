using Arch.AOT.SourceGenerator;
using Box2D.NET;

namespace Game.Engine.ECS.Asteroids;

/// <summary>
///     Tuning constants for the asteroids court. Ported from the reference game
///     (<c>src/Temp/AsteroidsWasm/Asteroids.Standard</c>): the reference draws on a
///     virtual 10000 x 7500 canvas; every length/velocity is scaled by 0.08 to a
///     800 x 600 court. All gameplay values are in court pixels (px) unless stated.
/// </summary>
public static class AsteroidsConfig
{
    /// <summary>Scale factor from the reference virtual canvas (10000 x 7500) to the court.</summary>
    public const float ReferenceScale = 0.08f;

    public const float CourtWidth = 800f;
    public const float CourtHeight = 600f;

    /// <summary>Court pixels per Box2D meter. Box2D bodies live in a 80 x 60 m world.</summary>
    public const float PixelsPerMeter = 100f;

    public const double TickIntervalSeconds = 1.0 / 60.0;

    /// <summary>Box2D sub-steps per fixed tick (Bullets use CCD; 4 sub-steps keep contacts crisp).</summary>
    public const int SubStepCount = 4;

    /// <summary>Bodies re-enter on the opposite edge once fully past this margin.</summary>
    public const float WrapMargin = 60f;

    // --- Ship -----------------------------------------------------------------

    /// <summary>Ship rotation speed, 200 deg/s (reference: 12000/FPS degrees per frame).</summary>
    public const float ShipRotateSpeed = 200f * MathF.PI / 180f;

    /// <summary>Thrust acceleration in px/s^2 (reference: 90/FPS virtual px per frame, scaled 0.08).</summary>
    public const float ShipThrustAccel = 9000f * ReferenceScale;

    /// <summary>Maximum per-axis speed in px/s (reference: 5000/FPS px per frame).</summary>
    public const float ShipMaxSpeed = 500000f * ReferenceScale;

    /// <summary>Classic friction: velocity multiplied by (1 - 1/FPS) every tick.</summary>
    public const float ShipDecay = 1f - 1f / 60f;

    /// <summary>Ship collision circle radius in px.</summary>
    public const float ShipRadius = 12f;

    /// <summary>Hyperspace failure probability (reference: 1 in 10).</summary>
    public const int HyperspaceFailDenominator = 10;

    /// <summary>Hyperspace teleport range: 10%..90% of the court.</summary>
    public const float HyperspaceMinFraction = 0.1f;
    public const float HyperspaceMaxFraction = 0.9f;

    // --- Bullets ----------------------------------------------------------------

    /// <summary>Maximum concurrent bullets (reference: 4).</summary>
    public const int MaxBullets = 4;

    /// <summary>Bullet speed in px/s (reference: 100 px per frame on the virtual canvas).</summary>
    public const float BulletSpeed = 100f * ReferenceScale * 600f;

    /// <summary>Bullet lifetime (reference: 1 s).</summary>
    public const float BulletLifetimeSeconds = 1f;

    /// <summary>Bullet collision circle radius in px.</summary>
    public const float BulletRadius = 3f;

    // --- Asteroids ---------------------------------------------------------------

    /// <summary>Asteroids in the first belt and the belt size increment per level.</summary>
    public const int StartAsteroidCount = 4;

    /// <summary>Collision/visual radius per size (reference: size * 220 on the virtual canvas).</summary>
    public const float AsteroidRadiusSmall = 220f * ReferenceScale;
    public const float AsteroidRadiusMedium = 440f * ReferenceScale;
    public const float AsteroidRadiusLarge = 660f * ReferenceScale;

    /// <summary>Velocity factor per size (reference: (Large - Size + 1) * 1.05).</summary>
    public static float AsteroidSpeedFactor(AsteroidSize size) => ((int)AsteroidSize.Large - (int)size + 1) * 1.05f;

    /// <summary>Asteroid spin range in deg/s (reference: rand(10000) - 5000, divided by FPS).</summary>
    public const float AsteroidMaxSpinDegrees = 5000f;

    /// <summary>Score per hit: large->medium 50, medium->small 100, small->destroyed 250.</summary>
    public const int AsteroidScoreLarge = 50;
    public const int AsteroidScoreMedium = 100;
    public const int AsteroidScoreSmall = 250;

    /// <summary>Saucer kill score (reference: 1000).</summary>
    public const int SaucerKillScore = 1000;

    // --- Saucer / Missile ---------------------------------------------------------

    /// <summary>Saucer appears after this many points have been earned since the last one.</summary>
    public const int SaucerScoreThreshold = 1000;

    /// <summary>Saucer speed in px/s (reference: 3000/FPS px per frame).</summary>
    public const float SaucerSpeed = 300000f * ReferenceScale;

    /// <summary>Maximum horizontal passes before the saucer leaves (reference: 3).</summary>
    public const int SaucerMaxPasses = 3;

    /// <summary>Saucer spawn Y range: 10%..90% of court height.</summary>
    public const float SaucerMinYFraction = 0.1f;
    public const float SaucerMaxYFraction = 0.9f;

    /// <summary>Saucer collision circle radius in px (reference SizeLong = 300 virtual px).</summary>
    public const float SaucerRadius = 300f * ReferenceScale;

    /// <summary>Missile speed in px/s (reference: 2000/FPS px per frame).</summary>
    public const float MissileSpeed = 2000f * ReferenceScale;

    /// <summary>Missile turn rate toward the ship, max 5 deg per tick.</summary>
    public const float MissileAlignMax = 5f * MathF.PI / 180f;

    /// <summary>Missile collision circle radius in px.</summary>
    public const float MissileRadius = 4f;

    // --- Lives / score -------------------------------------------------------------

    public const int InitialLives = 3;

    /// <summary>Free ship every 10000 points.</summary>
    public const int FreeShipIncrement = 10000;

    /// <summary>Score wraps at one million (reference behavior).</summary>
    public const int MaxScore = 1000000;

    /// <summary>Respawn safety radius around the court center in px (reference: 2000 virtual px).</summary>
    public const float SafeRespawnDistance = 2000f * ReferenceScale;

    // --- Explosions -----------------------------------------------------------------

    /// <summary>Explosion lifetime factor: asteroids/missiles 1, ship/saucer 2 (reference).</summary>
    public const float ExplosionLifeSeconds = 0.5f;
    public const float ShipExplosionLifeSeconds = 1f;

    // --- Box2D collision categories ---------------------------------------------------

    public const ulong CatShip = 0x0001;
    public const ulong CatAsteroid = 0x0002;
    public const ulong CatBullet = 0x0004;
    public const ulong CatSaucer = 0x0008;
    public const ulong CatMissile = 0x0010;

    /// <summary>Ship collides with asteroids, saucer and missiles (never its own bullets).</summary>
    public const ulong MaskShip = CatAsteroid | CatSaucer | CatMissile;

    /// <summary>Asteroids collide with bullets and the ship only - they pass through each other.</summary>
    public const ulong MaskAsteroid = CatBullet | CatShip;

    /// <summary>Bullets hit asteroids, saucer and missiles (never the ship, never each other).</summary>
    public const ulong MaskBullet = CatAsteroid | CatSaucer | CatMissile;

    /// <summary>Saucer collides with bullets and the ship.</summary>
    public const ulong MaskSaucer = CatBullet | CatShip;

    /// <summary>Missiles collide with bullets and the ship.</summary>
    public const ulong MaskMissile = CatBullet | CatShip;
}

/// <summary>Asteroid size levels: <c>Small = 1</c> ... <c>Large = 3</c> (reference enum).</summary>
public enum AsteroidSize
{
    Dne = 0,
    Small = 1,
    Medium = 2,
    Large = 3
}

/// <summary>Sprite kind discriminator for the client renderer (matches AsteroidsSpriteState.Kind).</summary>
public enum AsteroidsSpriteKind : byte
{
    Ship = 0,
    Asteroid = 1,
    Bullet = 2,
    Saucer = 3,
    Missile = 4,
    Explosion = 5
}

/// <summary>Pixi-style heading of an entity: 0 = up, positive = clockwise (screen coords).</summary>
[Component]
public struct Rotation
{
    public float Value;

    public Rotation(float value)
    {
        Value = value;
    }
}

/// <summary>Size level of an asteroid entity.</summary>
[Component]
public struct AsteroidTag
{
    public AsteroidSize Size;

    public AsteroidTag(AsteroidSize size)
    {
        Size = size;
    }
}

/// <summary>Marks the single ship entity.</summary>
[Component]
public struct ShipTag
{
}

/// <summary>Age of a bullet in seconds; destroyed at AsteroidsConfig.BulletLifetimeSeconds.</summary>
[Component]
public struct BulletTag
{
    public float Age;

    public BulletTag(float age)
    {
        Age = age;
    }
}

/// <summary>Marks the single flying saucer. <see cref="Passes"/> counts edge crossings.</summary>
[Component]
public struct SaucerTag
{
    public int Passes;

    public SaucerTag(int passes)
    {
        Passes = passes;
    }
}

/// <summary>Marks a saucer missile. Heading lives in <see cref="Rotation"/> (screen radians).</summary>
[Component]
public struct MissileTag
{
}

/// <summary>Transient explosion marker; ages out after <see cref="Lifetime"/> seconds.</summary>
[Component]
public struct ExplosionTag
{
    public float Age;
    public float Lifetime;

    public ExplosionTag(float age, float lifetime)
    {
        Age = age;
        Lifetime = lifetime;
    }
}

/// <summary>
///     Box2D body handle owned by the entity. Positions/rotations in ECS are mirrors
///     of the Box2D body state (authoritative physics, ADR-002).
/// </summary>
[Component]
public struct PhysicsBody
{
    public B2BodyId BodyId;

    public PhysicsBody(B2BodyId bodyId)
    {
        BodyId = bodyId;
    }
}

/// <summary>
///     Per-game mutable state kept on a dedicated stats entity. <see cref="Exploded"/>,
///     <see cref="Fired"/>, <see cref="SaucerSpawned"/>, <see cref="LevelUp"/> and
///     <see cref="LifeGained"/> are ECS-originated edge events consumed once per emitted
///     signal (client sound/particle triggers).
/// </summary>
[Component]
public struct AsteroidsStats
{
    public int Score;
    public int Lives;
    public int Level;
    public int HighScore;
    public int NextSaucerPoints;
    public bool GameOver;
    public bool Started;
    public bool ThrustOn;
    public bool Exploded;
    public bool Fired;
    public bool SaucerSpawned;
    public bool LevelUp;
    public bool LifeGained;

    public AsteroidsStats(int score, int lives, int level, int highScore = 0, int nextSaucerPoints = AsteroidsConfig.SaucerScoreThreshold,
        bool gameOver = false, bool started = false, bool thrustOn = false, bool exploded = false, bool fired = false,
        bool saucerSpawned = false, bool levelUp = false, bool lifeGained = false)
    {
        Score = score;
        Lives = lives;
        Level = level;
        HighScore = highScore;
        NextSaucerPoints = nextSaucerPoints;
        GameOver = gameOver;
        Started = started;
        ThrustOn = thrustOn;
        Exploded = exploded;
        Fired = fired;
        SaucerSpawned = saucerSpawned;
        LevelUp = levelUp;
        LifeGained = lifeGained;
    }
}

/// <summary>
///     Plain-data snapshot of one asteroid sprite, serializable for the SSR payload
///     and SSE stream. Carries rotation + velocity (temporal context per ADR-003) so
///     the client can interpolate at display Hz. <c>Size</c> is the asteroid radius in
///     px (explosions: age in seconds).
/// </summary>
public record struct AsteroidsSpriteState(
    int Id, float X, float Y, float Rotation, float VX, float VY, byte Kind, float Size, byte R, byte G, byte B);

/// <summary>Client-suggested asteroid input. Held flags are absolute state; Fire and
/// Hyperspace are one-shot edges consumed by the simulation (C# sole authority).</summary>
public record struct AsteroidsInputRequest(bool Thrust, bool Left, bool Right, bool Fire, bool Hyperspace);
