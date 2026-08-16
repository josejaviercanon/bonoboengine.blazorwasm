using Game.Engine.ECS;
using Xunit;

namespace Game.Tests;

/// <summary>Unit tests for the SSR-facing snapshots of both simulations.</summary>
public class SnapshotTests
{
    [Fact]
    public void EcsSnapshot_Has_Ten_Sprites_With_Unique_RenderIds()
    {
        using var sim = new EcsSimulation();
        var snapshot = sim.Snapshot();

        Assert.Equal(10, snapshot.Count);
        Assert.Equal(10, snapshot.Select(s => s.Id).Distinct().Count());
        Assert.All(snapshot, s =>
        {
            Assert.InRange(s.X, 0f, sim.Width);
            Assert.InRange(s.Y, 0f, sim.Height);
        });
    }

    [Fact]
    public void SnakeSnapshot_Encodes_Draw_Order_Food_First_Head_Last()
    {
        using var sim = new SnakeSimulation();
        var snapshot = sim.Snapshot();

        // 1 food + (InitialLength - 1) body segments + 1 head.
        Assert.Equal(SnakeSimulation.InitialLength + 1, snapshot.Count);

        Assert.Equal(SnakeSimulation.FoodRenderId, snapshot[0].Id);

        var head = snapshot[^1];
        Assert.Equal(34, head.R);
        Assert.Equal(197, head.G);
        Assert.Equal(94, head.B);

        // Body render ids must be ascending between food and head.
        var bodyIds = snapshot.Skip(1).Take(snapshot.Count - 2).Select(s => s.Id).ToArray();
        Assert.Equal(bodyIds.Order().ToArray(), bodyIds);
    }

    [Fact]
    public void SnakeSnapshot_Positions_Are_Cell_Centered_Inside_Grid()
    {
        using var sim = new SnakeSimulation();
        var snapshot = sim.Snapshot();

        Assert.All(snapshot, s =>
        {
            // Cells render centered: pos = (cell + 0.5) * CellSize.
            Assert.Equal(SnakeSimulation.CellSize / 2f, s.X % SnakeSimulation.CellSize);
            Assert.Equal(SnakeSimulation.CellSize / 2f, s.Y % SnakeSimulation.CellSize);
            Assert.InRange(s.X, 0f, SnakeSimulation.GridWidth * SnakeSimulation.CellSize);
            Assert.InRange(s.Y, 0f, SnakeSimulation.GridHeight * SnakeSimulation.CellSize);
        });
    }
}
