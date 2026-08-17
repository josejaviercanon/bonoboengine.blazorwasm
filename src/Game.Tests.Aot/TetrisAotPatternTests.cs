using Game.Engine.ECS;
using Game.Engine.ECS.Tetris;

namespace Game.Tests.Aot;

/// <summary>
///     Fixed-determinism pattern checks for the tetris authority. Runs under TUnit
///     so the same assertions are exercised through a second, AOT-friendly test engine.
/// </summary>
public class TetrisAotPatternTests
{
    [Test]
    public async Task Grid_Constants_Match_Reference_Game()
    {
        var width = 10;
        var height = 20;

        await Assert.That(TetrisSimulation.GridWidth).IsEqualTo(width);
        await Assert.That(TetrisSimulation.GridHeight).IsEqualTo(height);
    }

    [Test]
    public async Task Snapshot_Contains_Active_Piece_Four_Cells()
    {
        var expected = 4;
        using var sim = new TetrisSimulation(seed: 42);
        var snapshot = sim.Snapshot();

        await Assert.That(snapshot).Count().IsEqualTo(expected);
    }

    [Test]
    public async Task Input_Queue_Rejects_Unknown_Commands_Silently()
    {
        using var sim = new TetrisSimulation(seed: 42);
        sim.QueueInput("sideways");
        sim.Start();

        await Assert.That(sim.IsStarted).IsTrue();
        await Assert.That(sim.ApplyInput("sideways")).IsFalse();
    }

    [Test]
    public async Task Line_Clear_Removes_Full_Rows()
    {
        var expected = 1;
        var activeOnly = 4;
        using var sim = new TetrisSimulation(seed: 42);

        for (var x = 0; x < TetrisSimulation.GridWidth; x++)
        {
            sim.PlaceBlock(x, TetrisSimulation.GridHeight - 1, TetrominoType.O);
        }

        var cleared = sim.ClearLinesForTest();

        await Assert.That(cleared).IsEqualTo(expected);
        await Assert.That(sim.Snapshot()).Count().IsEqualTo(activeOnly); // active piece only
    }
}