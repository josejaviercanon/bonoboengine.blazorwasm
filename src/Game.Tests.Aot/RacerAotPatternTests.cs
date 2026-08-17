using System.Runtime.CompilerServices;
using Game.Engine.ECS;
using Game.Engine.ECS.Racer;
using TUnit.Core;

namespace Game.Tests.Aot;

[NotInParallel]
public class RacerAotPatternTests
{
    [Test]
    public async Task Source_Constants_Are_Stable()
    {
        await Assert.That(RacerConfig.SegmentLength).IsEqualTo(200f);
        await Assert.That(RacerConfig.RumbleLength).IsEqualTo(3);
        await Assert.That(RacerConfig.DefaultLanes).IsEqualTo(3);
        await Assert.That(RacerConfig.DefaultDrawDistance).IsEqualTo(300);
        await Assert.That(RacerConfig.TotalCars).IsEqualTo(200);
        await Assert.That(RacerConfig.TickIntervalSeconds).IsEqualTo(1.0 / 60.0);
    }

    [Test]
    public async Task Snapshot_Contains_Source_Track_And_Traffic()
    {
        using var sim = new RacerSimulation(seed: 42, startTimer: false);

        await Assert.That(sim.TrackSnapshot().Segments.Count).IsGreaterThan(1000);
        await Assert.That(sim.SnapshotCars()).Count().IsEqualTo(RacerConfig.TotalCars);
    }

    [Test]
    public async Task Seeded_Initial_Cars_Are_Deterministic()
    {
        using var first = new RacerSimulation(seed: 11, startTimer: false);
        using var second = new RacerSimulation(seed: 11, startTimer: false);

        var firstCars = first.SnapshotCars();
        var secondCars = second.SnapshotCars();
        await Assert.That(firstCars.Count).IsEqualTo(secondCars.Count);
        for (var i = 0; i < firstCars.Count; i++)
        {
            await Assert.That(firstCars[i]).IsEqualTo(secondCars[i]);
        }
    }

    [Test]
    public async Task Racer_Components_Are_Blittable()
    {
        await Assert.That(RuntimeHelpers.IsReferenceOrContainsReferences<TransformComponent>()).IsFalse();
        await Assert.That(RuntimeHelpers.IsReferenceOrContainsReferences<PlayerInputComponent>()).IsFalse();
        await Assert.That(RuntimeHelpers.IsReferenceOrContainsReferences<AICarComponent>()).IsFalse();
        await Assert.That(RuntimeHelpers.IsReferenceOrContainsReferences<RoadSegmentComponent>()).IsFalse();
    }
}
