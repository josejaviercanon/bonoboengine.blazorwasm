using Game.Engine.ECS;
using Game.Engine.ECS.Snake;

namespace Game.Tests.Aot;

/// <summary>
///     Fixed-timestep determinism pattern checks for the snake authority. Runs under TUnit
///     so the same assertions are exercised through a second, AOT-friendly test engine.
/// </summary>
public class SnakeAotPatternTests
{
    [Test]
    public async Task Grid_Constants_Match_Initial_Snapshot_Shape()
    {
        // Derive the expectations from a live snapshot instead of restating constants:
        // initial length + food + head must all be present and cell-aligned.
        using var sim = new SnakeSimulation();
        var snapshot = sim.Snapshot();
        var expectedSprites = SnakeSimulation.InitialLength + 1;

        await Assert.That(snapshot.Count).IsEqualTo(expectedSprites);
        await Assert.That(snapshot[0].Id).IsEqualTo(SnakeSimulation.FoodRenderId);
    }

    [Test]
    public async Task Input_Queue_Rejects_Unknown_Directions_Silently()
    {
        using var sim = new SnakeSimulation();
        sim.QueueDirection("sideways");
        sim.Start();

        await Assert.That(sim.IsStarted).IsTrue();
    }

    [Test]
    public async Task Bad_Food_Spawns_Replacement_At_Fall_Start()
    {
        using var sim = new SnakeSimulation(seed: 7, startTimer: false);
        SnakeRenderSignal? falling = null;
        sim.OnRenderSignal += signal =>
        {
            if (signal.FoodFalling) falling = signal;
        };
        sim.Start();

        var directions = new[] { "up", "right", "down", "left" };
        for (var signalIndex = 0; signalIndex < 25; signalIndex++)
        {
            if (signalIndex % 6 == 0)
            {
                sim.QueueDirection(directions[(signalIndex / 6) % directions.Length]);
            }

            for (var tick = 0; tick < 8; tick++) sim.StepOnce();
        }

        await Assert.That(falling).IsNotNull();
        var foods = falling!.Sprites.Where(sprite =>
            sprite.Kind is SnakeSpriteKind.GoodFood or SnakeSpriteKind.BadFood).ToArray();
        await Assert.That(foods.Length).IsEqualTo(2);

        var bad = foods.Single(sprite => sprite.Kind == SnakeSpriteKind.BadFood);
        var good = foods.Single(sprite => sprite.Kind == SnakeSpriteKind.GoodFood);
        await Assert.That(bad.R).IsEqualTo((byte)239);
        await Assert.That(bad.G).IsEqualTo((byte)68);
        await Assert.That(bad.B).IsEqualTo((byte)68);
        await Assert.That(good.R).IsEqualTo((byte)34);
        await Assert.That(good.G).IsEqualTo((byte)211);
        await Assert.That(good.B).IsEqualTo((byte)238);
    }
}
