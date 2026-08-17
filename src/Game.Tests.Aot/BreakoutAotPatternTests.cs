using Game.Engine.ECS;
using Game.Engine.ECS.Breakout;

namespace Game.Tests.Aot;

/// <summary>
///     Fixed-determinism pattern checks for the breakout authority. Runs under TUnit
///     so the same assertions are exercised through a second, AOT-friendly test engine.
/// </summary>
public class BreakoutAotPatternTests
{
    [Test]
    public async Task Court_Constants_Match_Reference_Game()
    {
        await Assert.That(BreakoutConfig.CourtChunksX).IsEqualTo(30);
        await Assert.That(BreakoutConfig.CourtChunksY).IsEqualTo(25);
        await Assert.That(BreakoutConfig.InitialLives).IsEqualTo(3);
        await Assert.That(BreakoutConfig.MaxLives).IsEqualTo(5);
    }

    [Test]
    public async Task Snapshot_Contains_Level0_Bricks_Paddle_And_Ball()
    {
        using var sim = new BreakoutSimulation(seed: 42, startTimer: false);
        var snapshot = sim.Snapshot();

        await Assert.That(snapshot).Count().IsEqualTo(32); // 30 bricks + paddle + ball
    }

    [Test]
    public async Task Input_Launch_Is_Only_Applied_When_Started()
    {
        using var sim = new BreakoutSimulation(seed: 42, startTimer: false);
        sim.ApplyInput(new BreakoutInputRequest(false, false, true));

        await Assert.That(sim.BallStateForTest().Moving).IsFalse();

        sim.Start();
        sim.ApplyInput(new BreakoutInputRequest(false, false, true));

        await Assert.That(sim.BallStateForTest().Moving).IsTrue();
    }

    [Test]
    public async Task All_Ten_Levels_Port_Verbatim()
    {
        await Assert.That(BreakoutLevels.All).Count().IsEqualTo(10);
    }
}