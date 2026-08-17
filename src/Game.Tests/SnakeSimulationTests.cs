using Game.Engine.ECS;
using Game.Engine.ECS.Snake;
using Xunit;

namespace Game.Tests;

/// <summary>Unit tests for the authoritative snake simulation (C# sole authority, ADR-001/006).</summary>
public class SnakeSimulationTests
{
    [Fact]
    public void Start_Marks_Game_As_Started()
    {
        using var sim = new SnakeSimulation();
        Assert.False(sim.IsStarted);

        sim.Start();
        Assert.True(sim.IsStarted);
        Assert.False(sim.IsGameOver);
        Assert.Equal(0, sim.Score);
    }

    [Fact]
    public void QueueDirection_Ignores_Invalid_Input_Without_Throwing()
    {
        using var sim = new SnakeSimulation();

        // The client only *suggests* directions; invalid strings must be dropped silently
        // by the authority, never crash the tick loop.
        sim.QueueDirection("diagonal");
        sim.QueueDirection("");
        sim.QueueDirection("UPPER");
        sim.Start();
        sim.QueueDirection(null!);

        Assert.True(sim.IsStarted);
    }

    [Fact]
    public void Reset_Restores_Initial_World()
    {
        using var sim = new SnakeSimulation();
        sim.Start();
        var before = sim.Snapshot();

        sim.Reset();
        var after = sim.Snapshot();

        Assert.Equal(before.Count, after.Count);
        Assert.Equal(SnakeSimulation.FoodRenderId, after[0].Id);
        Assert.Equal(0, sim.Score);
        Assert.False(sim.IsGameOver);
    }

    [Fact(Timeout = 15_000)]
    public async Task Signals_Fire_At_Grid_Step_Rate_With_Increasing_Seq()
    {
        using var sim = new SnakeSimulation();
        var ct = TestContext.Current.CancellationToken;
        var signals = new List<SnakeRenderSignal>();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void Handler(SnakeRenderSignal s)
        {
            lock (signals) signals.Add(s);
            if (signals.Count >= 8) done.TrySetResult();
        }

        sim.OnRenderSignal += Handler;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cancel.CancelAfter(TimeSpan.FromSeconds(3));
        cancel.Token.Register(() => done.TrySetCanceled(cancel.Token));
        await done.Task.WaitAsync(cancel.Token);

        sim.OnRenderSignal -= Handler;
        lock (signals)
        {
            // 8 Hz grid steps: up to 3 s must produce at least 8 signals.
            Assert.True(signals.Count >= 8, $"expected >= 8 signals, got {signals.Count}");
            for (var i = 1; i < signals.Count; i++)
            {
                Assert.Equal(signals[i - 1].Seq + 1, signals[i].Seq);
            }
        }
    }
}
