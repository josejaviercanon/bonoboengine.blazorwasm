using Game.Engine.ECS;
using Game.Engine.ECS.Asteroids;
using TUnit.Core;

namespace Game.Tests.Aot;

/// <summary>
///     Fixed-determinism pattern checks for the asteroids authority. Runs under TUnit
///     so the same assertions are exercised through a second, AOT-friendly test engine.
///     Not in parallel: Box2D keeps a static world table shared by every simulation.
/// </summary>
[NotInParallel]
public class AsteroidsAotPatternTests
{
    [Test]
    public async Task Court_Constants_Match_Reference_Game()
    {
        await Assert.That(AsteroidsConfig.CourtWidth).IsEqualTo(800f);
        await Assert.That(AsteroidsConfig.CourtHeight).IsEqualTo(600f);
        await Assert.That(AsteroidsConfig.StartAsteroidCount).IsEqualTo(4);
        await Assert.That(AsteroidsConfig.InitialLives).IsEqualTo(3);
        await Assert.That(AsteroidsConfig.SaucerScoreThreshold).IsEqualTo(1000);
        await Assert.That(AsteroidsConfig.SaucerKillScore).IsEqualTo(1000);
        await Assert.That(AsteroidsConfig.FreeShipIncrement).IsEqualTo(10000);
        await Assert.That(AsteroidsConfig.MaxBullets).IsEqualTo(4);
        await Assert.That(AsteroidsConfig.BulletLifetimeSeconds).IsEqualTo(1f);
        await Assert.That(AsteroidsConfig.TickIntervalSeconds).IsEqualTo(1.0 / 60.0);
    }

    [Test]
    public async Task Snapshot_Contains_Ship_And_Four_Large_Asteroids()
    {
        using var sim = new AsteroidsSimulation(seed: 42, startTimer: false);
        var snapshot = sim.Snapshot();

        await Assert.That(snapshot).Count().IsEqualTo(AsteroidsConfig.StartAsteroidCount + 1);
    }

    [Test]
    public async Task Input_Fire_Is_Only_Applied_When_Started()
    {
        using var sim = new AsteroidsSimulation(seed: 42, startTimer: false);
        sim.ApplyInput(new AsteroidsInputRequest(false, false, false, true, false));

        await Assert.That(sim.BulletCountForTest()).IsEqualTo(0);

        sim.Start();
        sim.ApplyInput(new AsteroidsInputRequest(false, false, false, true, false));

        await Assert.That(sim.BulletCountForTest()).IsEqualTo(1);
    }

    [Test]
    public async Task Seeded_Simulations_Are_Deterministic()
    {
        using var a = new AsteroidsSimulation(seed: 7, startTimer: false);
        using var b = new AsteroidsSimulation(seed: 7, startTimer: false);
        a.Start();
        b.Start();

        for (var i = 0; i < 60; i++)
        {
            var input = new AsteroidsInputRequest(i % 20 < 10, false, true, i % 30 == 0, false);
            a.ApplyInput(input);
            b.ApplyInput(input);
        }

        await Assert.That(a.Snapshot().Count).IsEqualTo(b.Snapshot().Count);
        for (var i = 0; i < a.Snapshot().Count; i++)
        {
            await Assert.That(a.Snapshot()[i]).IsEqualTo(b.Snapshot()[i]);
        }
    }

    [Test]
    public async Task PhysicsBody_Component_Is_Blittable()
    {
        var type = typeof(PhysicsBody);

        await Assert.That(type.IsValueType).IsTrue();
        await Assert.That(System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<PhysicsBody>()).IsFalse();
    }
}
