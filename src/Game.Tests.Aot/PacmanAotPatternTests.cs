using Game.Engine.ECS.Pacman;
using System.Reflection;
using TUnit.Assertions;

namespace Game.Tests.Aot;

public sealed class PacmanAotPatternTests
{
    [Test]
    public async Task Pacman_Components_Are_Value_Types()
    {
        var componentTypes = new[]
        {
            typeof(PacmanTransform),
            typeof(PacmanMotion),
            typeof(PacmanFacing),
            typeof(PacmanSprite),
            typeof(PacmanPlayer),
            typeof(PacmanGhostState),
            typeof(PacmanPellet),
            typeof(PacmanStats),
        };

        foreach (var type in componentTypes)
        {
            await Assert.That(type.IsValueType).IsTrue();
        }
    }

    [Test]
    public async Task Snapshot_Contains_Interpolation_And_Actor_Contract()
    {
        using var sim = new PacmanSimulation(seed: 8, startTimer: false);
        var state = sim.Snapshot().First(s => s.Kind == PacmanSpriteKind.Player);

        await Assert.That(state.Id).IsEqualTo(PacmanSimulation.PlayerRenderId);
        await Assert.That(state.PreviousX).IsEqualTo(state.X);
        await Assert.That(state.PreviousY).IsEqualTo(state.Y);
        await Assert.That(state.Kind).IsEqualTo(PacmanSpriteKind.Player);
    }

    [Test]
    public async Task Render_Signal_Exposes_Batched_Gameplay_State()
    {
        var properties = typeof(PacmanRenderSignal)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToArray();

        await Assert.That(properties).Contains("Seq");
        await Assert.That(properties).Contains("Sprites");
        await Assert.That(properties).Contains("Score");
        await Assert.That(properties).Contains("Lives");
        await Assert.That(properties).Contains("Frightened");
    }

    [Test]
    public async Task Same_Seed_Remains_Deterministic_Under_Aot_Test_Host()
    {
        using var first = new PacmanSimulation(seed: 99, startTimer: false);
        using var second = new PacmanSimulation(seed: 99, startTimer: false);
        first.Start();
        second.Start();

        for (var i = 0; i < 30; i++)
        {
            first.StepOnce();
            second.StepOnce();
        }

        await Assert.That(first.Snapshot().SequenceEqual(second.Snapshot())).IsTrue();
    }
}
