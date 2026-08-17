using Game.Engine.ECS.Pacman;
using Xunit;

namespace Game.Tests;

public sealed class PacmanSimulationTests
{
    [Fact]
    public void Initial_Snapshot_Contains_Maze_Actors_And_Pellets()
    {
        using var sim = new PacmanSimulation(seed: 7, startTimer: false);
        var snapshot = sim.Snapshot();

        Assert.Equal(29, PacmanMaze.Width);
        Assert.Equal(31, PacmanMaze.Height);
        Assert.Equal(PacmanMaze.PelletCount + 5, snapshot.Count);
        Assert.Equal(4, snapshot.Count(s => s.Kind is
            PacmanSpriteKind.Blinky or PacmanSpriteKind.Pinky or PacmanSpriteKind.Inky or PacmanSpriteKind.Clyde));
        Assert.Equal(4, snapshot.Count(s => s.Kind == PacmanSpriteKind.PowerPellet));
        Assert.False(sim.IsStarted);
        Assert.Equal(PacmanConfig.InitialLives, sim.Lives);
    }

    [Fact]
    public void Maze_Uses_Explicit_Tunnel_And_Wall_Rules()
    {
        Assert.True(PacmanMaze.IsTunnel(new PacmanCell(0, 14)));
        Assert.True(PacmanMaze.IsTunnel(new PacmanCell(28, 14)));
        Assert.Equal(new PacmanCell(28, 14), PacmanMaze.NextCell(new PacmanCell(0, 14), PacmanDirection.Left));
        Assert.Equal(new PacmanCell(0, 14), PacmanMaze.NextCell(new PacmanCell(28, 14), PacmanDirection.Right));
        Assert.True(PacmanMaze.IsWall(new PacmanCell(0, 0)));
        Assert.True(PacmanMaze.CanMove(new PacmanCell(14, 23), PacmanDirection.Left));
    }

    [Fact]
    public void Start_And_Invalid_Input_Keep_Authority_Stable()
    {
        using var sim = new PacmanSimulation(seed: 1, startTimer: false);

        sim.QueueDirection("diagonal");
        sim.QueueDirection(null);
        sim.Start();
        sim.StepOnce();

        Assert.True(sim.IsStarted);
        Assert.False(sim.IsGameOver);
        Assert.True(sim.Snapshot().Count > 0);
    }

    [Fact]
    public void Fixed_Steps_Emit_Batched_Signals_With_Increasing_Sequence()
    {
        using var sim = new PacmanSimulation(seed: 5, startTimer: false);
        var signals = new List<PacmanRenderSignal>();
        sim.OnRenderSignal += signals.Add;
        sim.Start();

        for (var i = 0; i < 5; i++) sim.StepOnce();

        Assert.Equal(5, signals.Count);
        Assert.Equal(new[] { 1L, 2L, 3L, 4L, 5L }, signals.Select(s => s.Seq));
        Assert.All(signals, signal => Assert.Equal(PacmanMaze.PelletCount + 5, signal.EntityCount));
    }

    [Fact]
    public void Same_Seed_And_Input_Produce_Identical_Snapshots()
    {
        using var first = new PacmanSimulation(seed: 42, startTimer: false);
        using var second = new PacmanSimulation(seed: 42, startTimer: false);
        first.Start();
        second.Start();

        for (var i = 0; i < 120; i++)
        {
            if (i == 1) first.QueueDirection("up");
            if (i == 1) second.QueueDirection("up");
            if (i == 40) first.QueueDirection("right");
            if (i == 40) second.QueueDirection("right");
            first.StepOnce();
            second.StepOnce();
        }

        Assert.Equal(first.Snapshot(), second.Snapshot());
        Assert.Equal(first.Score, second.Score);
        Assert.Equal(first.Lives, second.Lives);
    }

    [Fact]
    public void Moving_Actors_Stay_Inside_Open_Maze_Cells()
    {
        using var sim = new PacmanSimulation(seed: 12, startTimer: false);
        sim.Start();

        for (var tick = 0; tick < 480; tick++)
        {
            sim.StepOnce();
            foreach (var sprite in sim.Snapshot().Where(s => s.Kind is not
                         (PacmanSpriteKind.Pellet or PacmanSpriteKind.PowerPellet)))
            {
                var cell = PacmanMaze.CellFromPosition(sprite.X, sprite.Y);
                Assert.True(PacmanMaze.IsOpen(cell), $"{sprite.Kind} entered wall cell {cell}");
            }
        }
    }

    [Fact]
    public void Reset_Restores_Initial_State()
    {
        using var sim = new PacmanSimulation(seed: 3, startTimer: false);
        sim.Start();
        for (var i = 0; i < 30; i++) sim.StepOnce();

        sim.Reset();

        Assert.False(sim.IsStarted);
        Assert.False(sim.IsGameOver);
        Assert.Equal(0, sim.Score);
        Assert.Equal(PacmanConfig.InitialLives, sim.Lives);
        Assert.Equal(PacmanMaze.PelletCount, sim.PelletsRemaining);
        Assert.Equal(PacmanMaze.PelletCount + 5, sim.Snapshot().Count);
    }
}
