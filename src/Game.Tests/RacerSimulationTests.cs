using Game.Engine.ECS;
using Game.Engine.ECS.Racer;
using Xunit;

namespace Game.Tests;

public class RacerSimulationTests
{
    [Fact]
    public void Track_Contains_Source_Road_And_Start_Finish_Markers()
    {
        using var sim = new RacerSimulation(seed: 42, startTimer: false);
        var track = sim.TrackSnapshot();

        Assert.True(track.Segments.Count > 1000);
        Assert.Equal(track.Segments.Count * RacerConfig.SegmentLength, track.TrackLength);
        Assert.Equal(RacerConfig.SegmentLength, track.SegmentLength);
        Assert.Contains(track.Segments, segment => segment.Color == (byte)RacerSegmentColor.Start);
        Assert.Contains(track.Segments, segment => segment.Color == (byte)RacerSegmentColor.Finish);
        Assert.NotEmpty(track.Sprites);
    }

    [Fact]
    public void Seeded_Simulations_Produce_Identical_Snapshots()
    {
        using var first = new RacerSimulation(seed: 7, startTimer: false);
        using var second = new RacerSimulation(seed: 7, startTimer: false);

        for (var i = 0; i < 180; i++)
        {
            var input = new RacerInputRequest(
                Left: i % 40 < 10,
                Right: i % 40 >= 20 && i % 40 < 30,
                Faster: true,
                Slower: false);
            first.ApplyInput(input);
            second.ApplyInput(input);
            first.StepOnce();
            second.StepOnce();
        }

        Assert.Equal(first.PlayerSnapshot(), second.PlayerSnapshot());
        Assert.Equal(first.SnapshotCars(), second.SnapshotCars());
    }

    [Fact]
    public void Faster_Input_Increases_Speed_And_Slower_Input_Brakes()
    {
        using var sim = new RacerSimulation(seed: 42, startTimer: false);

        for (var i = 0; i < 30; i++)
        {
            sim.ApplyInput(new RacerInputRequest(false, false, true, false));
            sim.StepOnce();
        }

        var accelerated = sim.PlayerSnapshot().Speed;
        Assert.True(accelerated > 0f);

        for (var i = 0; i < 10; i++)
        {
            sim.ApplyInput(new RacerInputRequest(false, false, false, true));
            sim.StepOnce();
        }

        Assert.True(sim.PlayerSnapshot().Speed < accelerated);
    }

    [Fact]
    public void Config_Is_Clamped_To_Reference_Tweak_Ranges()
    {
        using var sim = new RacerSimulation(seed: 42, startTimer: false);

        sim.ApplyConfig(new RacerConfigRequest(
            Lanes: 99,
            RoadWidth: 99999f,
            CameraHeight: -1f,
            DrawDistance: 9999,
            FieldOfView: 999f,
            FogDensity: -10f,
            ResolutionScale: 99f));

        Assert.Equal(4, sim.Settings.Lanes);
        Assert.Equal(3000f, sim.Settings.RoadWidth);
        Assert.Equal(500f, sim.Settings.CameraHeight);
        Assert.Equal(500, sim.Settings.DrawDistance);
        Assert.Equal(140f, sim.Settings.FieldOfView);
        Assert.Equal(0f, sim.Settings.FogDensity);
        Assert.Equal(1.5f, sim.Settings.ResolutionScale);
    }

    [Fact]
    public void Pause_Stops_Fixed_Ticks_Until_Resume()
    {
        using var sim = new RacerSimulation(seed: 42, startTimer: false);
        sim.ApplyInput(new RacerInputRequest(false, false, true, false));
        sim.StepOnce();
        var beforePause = sim.PlayerSnapshot();

        sim.Pause();
        sim.StepOnce();
        Assert.Equal(beforePause, sim.PlayerSnapshot());

        sim.Resume();
        sim.StepOnce();
        Assert.True(sim.PlayerSnapshot().Z > beforePause.Z);
    }

    [Fact]
    public void Initial_World_Has_Two_Hundred_Traffic_Cars()
    {
        using var sim = new RacerSimulation(seed: 42, startTimer: false);

        Assert.Equal(RacerConfig.TotalCars, sim.SnapshotCars().Count);
        Assert.All(sim.SnapshotCars(), car => Assert.InRange(car.SpriteKind,
            (byte)RacerSpriteKind.Semi, (byte)RacerSpriteKind.Car01));
    }
}
