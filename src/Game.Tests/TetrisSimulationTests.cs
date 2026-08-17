using Game.Engine.ECS;
using Game.Engine.ECS.Tetris;
using Game.Engine.ECS.Systems;
using Xunit;

namespace Game.Tests;

/// <summary>Unit tests for the authoritative tetris simulation (C# sole authority, ADR-001/006).</summary>
public class TetrisSimulationTests
{
    [Fact]
    public void Start_Marks_Game_As_Started()
    {
        using var sim = new TetrisSimulation();
        Assert.False(sim.IsStarted);

        sim.Start();
        Assert.True(sim.IsStarted);
        Assert.False(sim.IsGameOver);
        Assert.Equal(0, sim.Score);
        Assert.Equal(0, sim.Rows);
        Assert.Equal(1, sim.Level);
    }

    [Fact]
    public void QueueInput_Ignores_Invalid_Commands_Without_Throwing()
    {
        using var sim = new TetrisSimulation();

        // The client only *suggests* commands; invalid strings must be dropped silently
        // by the authority, never crash the tick loop.
        sim.QueueInput("diagonal");
        sim.QueueInput("");
        sim.QueueInput("ROTATE");
        sim.Start();
        sim.QueueInput(null!);

        Assert.False(sim.ApplyInput("sideways"));
        Assert.True(sim.IsStarted);
    }

    [Fact]
    public void Reset_Restores_Initial_World()
    {
        using var sim = new TetrisSimulation();
        sim.Start();
        var before = sim.Snapshot();

        sim.Reset();
        var after = sim.Snapshot();

        Assert.Equal(before.Count, after.Count);
        Assert.Equal(4, after.Count);
        Assert.Equal(0, sim.Score);
        Assert.False(sim.IsGameOver);
        Assert.False(sim.IsStarted);
    }

    [Fact]
    public void Snapshot_Contains_Active_Piece_Four_Cells()
    {
        using var sim = new TetrisSimulation();

        // Empty court: only the active piece's four cells are present.
        Assert.Equal(4, sim.Snapshot().Count);
    }

    [Fact]
    public void Move_Left_Clamps_At_Wall()
    {
        using var sim = new TetrisSimulation();
        sim.Start();

        // Enough left inputs must pin the piece's leftmost cell to column 0.
        for (var i = 0; i < 15; i++)
        {
            Assert.True(sim.ApplyInput("left"));
        }

        var minCellX = sim.Snapshot().Min(s => s.X);
        Assert.Equal(0.5f * TetrisSimulation.CellSize, minCellX);
    }

    [Fact]
    public void Rotate_Is_Accepted_Without_Corrupting_State()
    {
        using var sim = new TetrisSimulation();
        sim.Start();

        Assert.True(sim.ApplyInput("rotate"));
        Assert.Equal(4, sim.Snapshot().Count);
        Assert.Equal(0, sim.Score);
    }

    [Fact]
    public void Gravity_Drop_Locks_Piece_And_Spawns_Next()
    {
        using var sim = new TetrisSimulation(seed: 42);
        sim.Start();

        // Empty board: 25 single drops guarantee the piece reaches the floor and locks.
        for (var i = 0; i < 25; i++)
        {
            sim.StepOnce();
        }

        // One lock: 4 settled blocks + a fresh active piece, soft-drop lock bonus of 10.
        Assert.Equal(8, sim.Snapshot().Count);
        Assert.Equal(10, sim.Score);
        Assert.True(sim.IsStarted);
        Assert.False(sim.IsGameOver);
    }

    [Fact]
    public void Line_Clear_Removes_Full_Rows_And_Shifts_Above()
    {
        using var sim = new TetrisSimulation(seed: 42);

        // Two full bottom rows plus a marker block above them.
        for (var x = 0; x < TetrisSimulation.GridWidth; x++)
        {
            sim.PlaceBlock(x, TetrisSimulation.GridHeight - 1, TetrominoType.I);
            sim.PlaceBlock(x, TetrisSimulation.GridHeight - 2, TetrominoType.J);
        }
        sim.PlaceBlock(0, 5, TetrominoType.O);

        var cleared = sim.ClearLinesForTest();

        Assert.Equal(2, cleared);
        Assert.Equal(5, sim.Snapshot().Count); // 1 marker block + 4 active-piece cells remain
        var marker = sim.Snapshot().Single(s =>
            (int)Math.Round(s.Y / TetrisSimulation.CellSize - 0.5) == 7);
        Assert.Equal(0, (int)Math.Round(marker.X / TetrisSimulation.CellSize - 0.5));
    }

    [Fact]
    public void Line_Clear_Scoring_Follows_Reference_Formula()
    {
        // Reference: 1 -> 100, 2 -> 200, 3 -> 400, 4 -> 800.
        Assert.Equal(100, TetrisGravitySystem.LineClearScore(1));
        Assert.Equal(200, TetrisGravitySystem.LineClearScore(2));
        Assert.Equal(400, TetrisGravitySystem.LineClearScore(3));
        Assert.Equal(800, TetrisGravitySystem.LineClearScore(4));
    }

    [Fact]
    public void Spawn_Into_Occupied_Court_Flags_Game_Over()
    {
        using var sim = new TetrisSimulation(seed: 42);
        sim.Start();

        // Fill every possible spawn cell (rows 0-3). Any next piece cannot fit there,
        // so the spawn -> top-out check must flag game over.
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < TetrisSimulation.GridWidth; x++)
            {
                sim.PlaceBlock(x, y, TetrominoType.I);
            }
        }

        sim.ForceSpawnNext();

        Assert.True(sim.IsGameOver);
    }

    [Fact(Timeout = 15_000)]
    public async Task Signals_Fire_On_Board_Mutation_With_Increasing_Seq()
    {
        using var sim = new TetrisSimulation(seed: 42);
        var ct = TestContext.Current.CancellationToken;
        var signals = new List<TetrisRenderSignal>();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void Handler(TetrisRenderSignal s)
        {
            lock (signals) signals.Add(s);
            if (signals.Count >= 4) done.TrySetResult();
        }

        sim.OnRenderSignal += Handler;
        sim.Start();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cancel.CancelAfter(TimeSpan.FromSeconds(4));
        cancel.Token.Register(() => done.TrySetCanceled(cancel.Token));
        await done.Task.WaitAsync(cancel.Token);

        sim.OnRenderSignal -= Handler;
        lock (signals)
        {
            // Gravity drops once per reference step (0.6 s at 0 rows): 4 s must yield >= 4 signals.
            Assert.True(signals.Count >= 4, $"expected >= 4 signals, got {signals.Count}");
            for (var i = 1; i < signals.Count; i++)
            {
                Assert.Equal(signals[i - 1].Seq + 1, signals[i].Seq);
            }
            Assert.All(signals, s => Assert.False(s.GameOver));
        }
    }
}