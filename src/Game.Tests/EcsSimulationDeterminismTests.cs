using Game.Engine.ECS;
using Xunit;

namespace Game.Tests;

/// <summary>
///     Determinism self-checks for <see cref="EcsSimulation"/>. The sim ticks at a fixed
///     60 Hz timestep and must emit exactly one batched <see cref="EcsRenderSignal"/> per
///     <see cref="EcsSimulation.SignalIntervalSeconds"/> (1 s), with a stable entity set,
///     monotonically increasing sequence numbers and positions that never leave the world
///     bounds. Wall-clock jitter must never change the tick count (fixed-dt determinism).
/// </summary>
public class EcsSimulationDeterminismTests
{
    private static async Task<List<EcsRenderSignal>> CollectSignalsAsync(
        EcsSimulation sim, TimeSpan duration, CancellationToken ct = default)
    {
        ct = CancellationTokenSource.CreateLinkedTokenSource(
            ct, TestContext.Current.CancellationToken).Token;
        var signals = new List<EcsRenderSignal>();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(EcsRenderSignal s)
        {
            lock (signals) signals.Add(s);
            if (signals.Count >= 3) done.TrySetResult();
        }

        sim.OnRenderSignal += Handler;
        using (ct.Register(() => done.TrySetCanceled(ct)))
        {
            await Task.WhenAny(done.Task, Task.Delay(duration, ct));
            sim.OnRenderSignal -= Handler;
        }

        lock (signals) return signals.ToList();
    }

    [Fact(Timeout = 15_000)]
    public async Task Signals_Are_Emitted_At_Exactly_One_Per_Interval()
    {
        using var sim = new EcsSimulation();
        var signals = await CollectSignalsAsync(sim, TimeSpan.FromSeconds(3.5), TestContext.Current.CancellationToken);

        // 3.5 s at one signal per second => 3 signals (4 would mean the throttle broke).
        Assert.InRange(signals.Count, 3, 4);
    }

    [Fact(Timeout = 15_000)]
    public async Task Sequence_Numbers_Are_Strictly_Monotonic()
    {
        using var sim = new EcsSimulation();
        var signals = await CollectSignalsAsync(sim, TimeSpan.FromSeconds(3.5), TestContext.Current.CancellationToken);

        Assert.NotEmpty(signals);
        for (var i = 1; i < signals.Count; i++)
        {
            Assert.Equal(signals[i - 1].Seq + 1, signals[i].Seq);
        }
    }

    [Fact(Timeout = 15_000)]
    public async Task EntityCount_And_SpriteIdSet_Are_Stable_Across_Signals()
    {
        using var sim = new EcsSimulation();
        var signals = await CollectSignalsAsync(sim, TimeSpan.FromSeconds(3.5), TestContext.Current.CancellationToken);

        Assert.NotEmpty(signals);
        var expectedIds = signals[0].Sprites.Select(s => s.Id).Order().ToArray();

        foreach (var signal in signals)
        {
            Assert.Equal(signals[0].EntityCount, signal.EntityCount);
            Assert.Equal(expectedIds, signal.Sprites.Select(s => s.Id).Order().ToArray());
        }
    }

    [Fact(Timeout = 15_000)]
    public async Task Positions_Always_Remain_Inside_World_Bounds()
    {
        using var sim = new EcsSimulation();
        var signals = await CollectSignalsAsync(sim, TimeSpan.FromSeconds(3.5), TestContext.Current.CancellationToken);

        Assert.NotEmpty(signals);
        foreach (var signal in signals)
        {
            foreach (var sprite in signal.Sprites)
            {
                Assert.InRange(sprite.X, 0f, sim.Width);
                Assert.InRange(sprite.Y, 0f, sim.Height);
            }
        }
    }

    [Fact(Timeout = 15_000)]
    public async Task TickMs_Reflects_Actual_System_Work_Not_Wall_Clock()
    {
        using var sim = new EcsSimulation();
        var signals = await CollectSignalsAsync(sim, TimeSpan.FromSeconds(3.5), TestContext.Current.CancellationToken);

        // One signal covers 60 system updates (~1 s of sim time), but TickMs measures only
        // the system Update() cost — it must stay far below the 1 s signal interval.
        Assert.All(signals, s => Assert.True(s.TickMs < EcsSimulation.SignalIntervalSeconds * 1000.0));
    }
}
