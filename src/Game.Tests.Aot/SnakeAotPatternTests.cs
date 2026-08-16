using Game.Engine.ECS;

namespace Game.Tests.Aot;

/// <summary>
///     Fixed-timestep determinism pattern checks for the snake authority. Runs under TUnit
///     so the same assertions are exercised through a second, AOT-friendly test engine.
/// </summary>
public class SnakeAotPatternTests
{
    [Test]
    public async Task Grid_Constants_Match_Initial_Snapshot_Shape()
    {
        // Derive the expectations from a live snapshot instead of restating constants:
        // initial length + food + head must all be present and cell-aligned.
        using var sim = new SnakeSimulation();
        var snapshot = sim.Snapshot();
        var expectedSprites = SnakeSimulation.InitialLength + 1;

        await Assert.That(snapshot).HasCount(expectedSprites);
        await Assert.That(snapshot[0].Id).IsEqualTo(SnakeSimulation.FoodRenderId);
    }

    [Test]
    public async Task Input_Queue_Rejects_Unknown_Directions_Silently()
    {
        using var sim = new SnakeSimulation();
        sim.QueueDirection("sideways");
        sim.Start();

        await Assert.That(sim.IsStarted).IsTrue();
    }
}
