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

    [Fact]
    public void Food_Fall_Spawns_Replacement_Immediately_And_Uses_Red_Bad_Food()
    {
        using var sim = new SnakeSimulation(seed: 7, startTimer: false);
        var signals = new List<SnakeRenderSignal>();
        sim.OnRenderSignal += signals.Add;
        sim.Start();

        StepSignalsSafe(sim, 25);

        var fallSignal = Assert.Single(signals, signal => signal.FoodFalling);
        var foods = fallSignal.Sprites.Where(sprite =>
            sprite.Kind is SnakeSpriteKind.GoodFood or SnakeSpriteKind.BadFood).ToArray();

        Assert.Equal(2, foods.Length);
        var bad = Assert.Single(foods, sprite => sprite.Kind == SnakeSpriteKind.BadFood);
        var good = Assert.Single(foods, sprite => sprite.Kind == SnakeSpriteKind.GoodFood);
        Assert.Equal((byte)239, bad.R);
        Assert.Equal((byte)68, bad.G);
        Assert.Equal((byte)68, bad.B);
        Assert.Equal((byte)34, good.R);
        Assert.Equal((byte)211, good.G);
        Assert.Equal((byte)238, good.B);
        Assert.True(fallSignal.FoodSpawned);
    }

    [Fact]
    public void Falling_Bad_Food_Reaches_Authoritative_Bottom_Without_Client_Report()
    {
        using var sim = new SnakeSimulation(seed: 9, startTimer: false);
        var signals = new List<SnakeRenderSignal>();
        sim.OnRenderSignal += signals.Add;
        sim.Start();

        StepSignalsSafe(sim, 25);
        StepSignalsSafe(sim, 5);

        var bad = Assert.Single(sim.Snapshot(), sprite => sprite.Kind == SnakeSpriteKind.BadFood);
        Assert.Equal((SnakeSimulation.GridHeight - 0.5f) * SnakeSimulation.CellSize, bad.Y);
        Assert.Equal(bad.X, bad.PreviousX);
        Assert.Equal(bad.Y, bad.PreviousY);
        Assert.Equal(0f, bad.VelocityY);
    }

    [Fact]
    public void Moving_Snapshot_Contains_Previous_Position_And_Velocity()
    {
        using var sim = new SnakeSimulation(seed: 11, startTimer: false);
        sim.Start();
        StepSignals(sim, 1);

        var head = sim.Snapshot().Single(sprite => sprite.Kind == SnakeSpriteKind.Head);
        Assert.Equal(head.PreviousX - SnakeSimulation.CellSize, head.X);
        Assert.Equal(0f, head.VelocityY);
        Assert.True(head.VelocityX < 0f);
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

    private static void StepSignals(SnakeSimulation sim, int signalCount)
    {
        for (var i = 0; i < signalCount * 8; i++) sim.StepOnce();
    }

    private static void StepSignalsSafe(SnakeSimulation sim, int signalCount)
    {
        var directions = new[] { "up", "right", "down", "left" };
        for (var signal = 0; signal < signalCount; signal++)
        {
            if (signal % 6 == 0) sim.QueueDirection(directions[(signal / 6) % directions.Length]);
            for (var tick = 0; tick < 8; tick++) sim.StepOnce();
        }
    }
}
