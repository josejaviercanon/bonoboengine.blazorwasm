using Game.Engine.ECS;
using Game.Engine.ECS.Asteroids;
using Xunit;

namespace Game.Tests;

/// <summary>
///     Unit tests for the authoritative asteroids simulation (Box2D.NET physics in C#,
///     C# sole authority - ADR-001/002/006). Rules are ported from the reference game
///     (<c>src/Temp/AsteroidsWasm/Asteroids.Standard</c>).
/// </summary>
public class AsteroidsSimulationTests
{
    [Fact]
    public void Start_Marks_Game_As_Started()
    {
        using var sim = new AsteroidsSimulation(seed: 42, startTimer: false);
        Assert.False(sim.IsStarted);

        sim.Start();
        Assert.True(sim.IsStarted);
        Assert.False(sim.IsGameOver);
        Assert.Equal(0, sim.Score);
        Assert.Equal(AsteroidsConfig.InitialLives, sim.Lives);
        Assert.Equal(AsteroidsConfig.StartAsteroidCount, sim.Level);
    }

    [Fact]
    public void Snapshot_Contains_Ship_And_Four_Large_Asteroids()
    {
        using var sim = new AsteroidsSimulation(seed: 42, startTimer: false);

        var snapshot = sim.Snapshot();
        Assert.Equal(AsteroidsConfig.StartAsteroidCount + 1, snapshot.Count);
        Assert.Single(snapshot, s => s.Kind == (byte)AsteroidsSpriteKind.Ship);
        Assert.Equal(AsteroidsConfig.StartAsteroidCount,
            snapshot.Count(s => s.Kind == (byte)AsteroidsSpriteKind.Asteroid));
        Assert.All(snapshot.Where(s => s.Kind == (byte)AsteroidsSpriteKind.Asteroid),
            s => Assert.Equal(AsteroidsConfig.AsteroidRadiusLarge, s.Size));
    }

    [Fact]
    public void Reset_Restores_Initial_World()
    {
        using var sim = new AsteroidsSimulation(seed: 42, startTimer: false);
        sim.Start();
        sim.ApplyInput(new AsteroidsInputRequest(false, false, false, true, false));
        Assert.Equal(1, sim.BulletCountForTest());

        sim.Reset();

        Assert.Equal(AsteroidsConfig.StartAsteroidCount + 1, sim.Snapshot().Count);
        Assert.Equal(0, sim.Score);
        Assert.False(sim.IsGameOver);
        Assert.False(sim.IsStarted);
        Assert.Equal(0, sim.BulletCountForTest());
    }

    [Fact]
    public void Fire_Creates_Bullet_Up_To_Four()
    {
        using var sim = new AsteroidsSimulation(seed: 42, startTimer: false);
        sim.Start();

        for (var i = 0; i < 5; i++)
        {
            sim.ApplyInput(new AsteroidsInputRequest(false, false, false, true, false));
        }

        Assert.Equal(AsteroidsConfig.MaxBullets, sim.BulletCountForTest());
    }

    [Fact]
    public void Bullet_Lives_One_Second()
    {
        using var sim = new AsteroidsSimulation(seed: 42, startTimer: false);
        sim.Start();
        sim.ApplyInput(new AsteroidsInputRequest(false, false, false, true, false));
        Assert.Equal(1, sim.BulletCountForTest());

        for (var i = 0; i < 65; i++)
        {
            sim.StepOnce();
        }

        Assert.Equal(0, sim.BulletCountForTest());
    }

    [Fact]
    public void Thrust_Accelerates_Ship_To_Decay_Equilibrium()
    {
        using var sim = new AsteroidsSimulation(seed: 42, startTimer: false);
        sim.Start();

        for (var i = 0; i < 120; i++)
        {
            sim.ApplyInput(new AsteroidsInputRequest(true, false, false, false, false));
        }

        var state = sim.ShipStateForTest();
        Assert.True(state.Alive);
        // Classic decay equilibrium: accel / (1 - decay) = 7.2 px/s; ~87% there after 2 s.
        var speed = MathF.Sqrt((state.VX * state.VX) + (state.VY * state.VY));
        Assert.InRange(speed, 4f, 8f);
    }

    [Fact]
    public void Rotation_Turns_Ship()
    {
        using var sim = new AsteroidsSimulation(seed: 42, startTimer: false);
        sim.Start();

        for (var i = 0; i < 60; i++)
        {
            sim.ApplyInput(new AsteroidsInputRequest(false, false, true, false, false));
        }

        var state = sim.ShipStateForTest();
        Assert.True(state.Alive);
        Assert.InRange(state.Rotation, 2f, 4f);
    }

    [Fact]
    public void Bullet_Hits_Large_Asteroid_Splits_Into_Two_Medium_And_Scores_50()
    {
        using var sim = new AsteroidsSimulation(seed: 42, startTimer: false);
        sim.Start();
        sim.ClearAsteroidsForTest();
        sim.PlaceAsteroidAt(AsteroidSize.Large, 100f, 100f, 0f, 0f);

        // Bullet starts outside the collision circle (radius ~53) and flies at the center.
        sim.PlaceBulletAt(180f, 100f, -AsteroidsConfig.BulletSpeed, 0f);
        for (var i = 0; i < 30; i++)
        {
            sim.StepOnce();
        }

        Assert.Equal(2, sim.AsteroidCountForTest());
        Assert.Equal(50, sim.Score);
        Assert.All(sim.Snapshot().Where(s => s.Kind == (byte)AsteroidsSpriteKind.Asteroid),
            s => Assert.Equal(AsteroidsConfig.AsteroidRadiusMedium, s.Size));
    }

    [Fact]
    public void Bullet_Hits_Small_Asteroid_Destroys_It_And_Scores_250()
    {
        using var sim = new AsteroidsSimulation(seed: 42, startTimer: false);
        sim.Start();
        sim.ClearAsteroidsForTest();
        sim.PlaceAsteroidAt(AsteroidSize.Small, 100f, 100f, 0f, 0f);

        // Small asteroid radius ~18; bullet starts outside the collision circle.
        sim.PlaceBulletAt(140f, 100f, -AsteroidsConfig.BulletSpeed, 0f);
        for (var i = 0; i < 30; i++)
        {
            sim.StepOnce();
        }

        Assert.Equal(0, sim.AsteroidCountForTest());
        Assert.Equal(250, sim.Score);
    }

    [Fact]
    public void Ship_Hit_By_Asteroid_Loses_Life_And_Respawns_When_Center_Safe()
    {
        using var sim = new AsteroidsSimulation(seed: 42, startTimer: false);
        sim.Start();
        sim.ClearAsteroidsForTest();

        // Ship sits at the court center (400, 300). Send a large asteroid at it from
        // outside the combined collision radii (12 + 53 = 65 px).
        sim.PlaceAsteroidAt(AsteroidSize.Large, 480f, 300f, -100f, 0f);
        for (var i = 0; i < 30; i++)
        {
            sim.StepOnce();
        }

        Assert.False(sim.ShipStateForTest().Alive);
        Assert.Equal(1, sim.ExplosionCountForTest());

        // Remove the asteroid so the center becomes safe, then age out the explosion.
        sim.ClearAsteroidsForTest();
        for (var i = 0; i < 70; i++)
        {
            sim.StepOnce();
        }

        Assert.True(sim.ShipStateForTest().Alive);
        Assert.Equal(AsteroidsConfig.InitialLives - 1, sim.Lives);
    }

    [Fact]
    public void Game_Over_When_No_Reserve_Ships()
    {
        using var sim = new AsteroidsSimulation(seed: 42, startTimer: false);
        sim.Start();
        sim.ClearAsteroidsForTest();
        sim.SetLivesForTest(1);

        sim.PlaceAsteroidAt(AsteroidSize.Large, 480f, 300f, -100f, 0f);
        for (var i = 0; i < 30; i++)
        {
            sim.StepOnce();
        }
        sim.ClearAsteroidsForTest();
        for (var i = 0; i < 70; i++)
        {
            sim.StepOnce();
        }

        Assert.True(sim.IsGameOver);
        Assert.False(sim.ShipStateForTest().Alive);
    }

    [Fact]
    public void Saucer_Spawns_After_1000_Points_Earned_In_A_Tick()
    {
        using var sim = new AsteroidsSimulation(seed: 42, startTimer: false);
        sim.Start();
        sim.ClearAsteroidsForTest();

        // Four stationary small asteroids (radius ~18), each shot from outside its circle.
        // 4 x 250 points = 1000 => the saucer appears on that tick.
        var xs = new[] { 80f, 240f, 400f, 560f };
        foreach (var x in xs)
        {
            sim.PlaceAsteroidAt(AsteroidSize.Small, x, 300f, 0f, 0f);
        }
        foreach (var x in xs)
        {
            sim.PlaceBulletAt(x + 30f, 300f, -AsteroidsConfig.BulletSpeed, 0f);
        }
        for (var i = 0; i < 30; i++)
        {
            sim.StepOnce();
        }

        Assert.Equal(1000, sim.Score);
        Assert.True(sim.SaucerAliveForTest());
    }

    [Fact]
    public void Belt_Respawns_At_Level_Plus_One_When_Cleared()
    {
        using var sim = new AsteroidsSimulation(seed: 42, startTimer: false);
        sim.Start();
        sim.ClearAsteroidsForTest();

        sim.StepOnce();

        Assert.Equal(AsteroidsConfig.StartAsteroidCount + 1, sim.AsteroidCountForTest());
        Assert.Equal(AsteroidsConfig.StartAsteroidCount + 1, sim.Level);
    }

    [Fact]
    public void Seeded_Simulations_Are_Deterministic()
    {
        using var a = new AsteroidsSimulation(seed: 7, startTimer: false);
        using var b = new AsteroidsSimulation(seed: 7, startTimer: false);
        a.Start();
        b.Start();

        for (var i = 0; i < 120; i++)
        {
            var input = new AsteroidsInputRequest(i % 40 < 20, false, i % 2 == 0, i % 30 == 0, false);
            a.ApplyInput(input);
            b.ApplyInput(input);
        }

        var snapshotA = a.Snapshot();
        var snapshotB = b.Snapshot();
        Assert.Equal(snapshotA.Count, snapshotB.Count);
        for (var i = 0; i < snapshotA.Count; i++)
        {
            Assert.Equal(snapshotA[i], snapshotB[i]);
        }
    }
}
