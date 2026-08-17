using Game.Engine.ECS;
using Game.Engine.ECS.Systems;
using Xunit;

namespace Game.Tests;

/// <summary>Unit tests for the authoritative breakout simulation (C# sole authority, ADR-001/006).</summary>
public class BreakoutSimulationTests
{
    [Fact]
    public void Start_Marks_Game_As_Started()
    {
        using var sim = new BreakoutSimulation(startTimer: false);
        Assert.False(sim.IsStarted);

        sim.Start();
        Assert.True(sim.IsStarted);
        Assert.False(sim.IsGameOver);
        Assert.Equal(0, sim.Score);
        Assert.Equal(BreakoutConfig.InitialLives, sim.Lives);
        Assert.Equal(0, sim.Level);
    }

    [Fact]
    public void Snapshot_Contains_Level0_Bricks_Paddle_And_Ball()
    {
        using var sim = new BreakoutSimulation(startTimer: false);

        var snapshot = sim.Snapshot();
        // Level 0: 30 bricks (5 rows x 6 runs) + paddle (id 1) + ball (id 2).
        Assert.Equal(32, snapshot.Count);
        Assert.Equal(30, snapshot.Count(s => s.Id >= BreakoutConfig.BrickRenderIdStart));
        Assert.Contains(snapshot, s => s.Id == 1);
        Assert.Contains(snapshot, s => s.Id == 2);
        // Brick sprite carries its width/height so the client can render merged runs.
        var brick = snapshot.First(s => s.Id >= BreakoutConfig.BrickRenderIdStart);
        Assert.True(brick.Width >= BreakoutConfig.ChunkSize);
        Assert.Equal(BreakoutConfig.ChunkSize, brick.Height);
    }

    [Fact]
    public void QueueInput_All_False_Is_A_No_Op()
    {
        using var sim = new BreakoutSimulation(startTimer: false);
        sim.QueueInput(new BreakoutInputRequest(false, false, false));
        Assert.False(sim.IsStarted);
        Assert.False(sim.IsGameOver);
    }

    [Fact]
    public void Reset_Restores_Initial_World()
    {
        using var sim = new BreakoutSimulation(startTimer: false);
        sim.Start();
        sim.ApplyInput(new BreakoutInputRequest(false, false, true));
        sim.StepOnce();

        sim.Reset();

        Assert.Equal(32, sim.Snapshot().Count);
        Assert.Equal(0, sim.Score);
        Assert.False(sim.IsGameOver);
        Assert.False(sim.IsStarted);
    }

    [Fact]
    public void ApplyInput_Launch_Fires_The_Ball()
    {
        using var sim = new BreakoutSimulation(startTimer: false);
        sim.Start();

        var before = sim.BallStateForTest();
        Assert.False(before.Moving);

        sim.ApplyInput(new BreakoutInputRequest(false, false, true));

        var after = sim.BallStateForTest();
        Assert.True(after.Moving);
        Assert.Equal(BreakoutConfig.BaseBallSpeed, after.Speed, 0.001f);
        Assert.Equal(BreakoutConfig.BaseBallSpeed, MathF.Sqrt((after.X * after.X) + (after.Y * after.Y)), 0.001f);
        Assert.True(after.Y < 0f); // launched upward

        sim.StepOnce();
        var moved = sim.Snapshot().Single(s => s.Id == 2);
        Assert.True(moved.Y < BreakoutConfig.CourtHeight - BreakoutConfig.PaddleHeight);
    }

    [Fact]
    public void Paddle_Movement_Clamps_At_Court_Edges()
    {
        using var sim = new BreakoutSimulation(seed: 42, startTimer: false);
        sim.Start();

        sim.PlacePaddleAt(0f);
        Assert.Equal(BreakoutConfig.PaddleWidth / 2f, sim.PaddleXForTest(), 0.001f);

        sim.PlacePaddleAt(BreakoutConfig.CourtWidth);
        Assert.Equal(BreakoutConfig.CourtWidth - (BreakoutConfig.PaddleWidth / 2f), sim.PaddleXForTest(), 0.001f);

        // Held input moves the paddle left one step.
        sim.PlacePaddleAt(200f);
        sim.ApplyInput(new BreakoutInputRequest(true, false, false));
        sim.StepOnce();
        Assert.True(sim.PaddleXForTest() < 200f);
    }

    [Fact]
    public void Ball_Bounces_Off_Left_Wall()
    {
        using var sim = new BreakoutSimulation(startTimer: false);
        sim.Start();
        sim.ClearBricksForTest(); // keep the court free so only the left wall is in the sweep

        sim.PlaceBallAt(8f, 200f, -BreakoutConfig.BaseBallSpeed, 0f);
        sim.StepOnce();

        var state = sim.BallStateForTest();
        var pos = sim.Snapshot().Single(s => s.Id == 2);
        Assert.True(state.X > 0f,
            $"expected rightward velocity after left-wall bounce: vx={state.X} vy={state.Y} speed={state.Speed} moving={state.Moving} pos=({pos.X},{pos.Y})");
        Assert.True(state.Moving);
    }

    [Fact]
    public void Ball_Bounces_Off_Top_Wall()
    {
        using var sim = new BreakoutSimulation(startTimer: false);
        sim.Start();

        sim.PlaceBallAt(300f, 8f, 0f, -BreakoutConfig.BaseBallSpeed);
        sim.StepOnce();

        var state = sim.BallStateForTest();
        Assert.True(state.Y > 0f, $"expected downward velocity after top-wall bounce, got {state.Y}");
    }

    [Fact]
    public void Paddle_Top_Hit_Reflects_Ball_With_Angle()
    {
        using var sim = new BreakoutSimulation(seed: 42, startTimer: false);
        sim.Start();

        // Off-center hit: paddle at 300, ball to the right -> rebound gains rightward x.
        sim.PlacePaddleAt(300f);
        sim.PlaceBallAt(350f, 400f, 0f, 6000f);
        sim.StepOnce();

        var state = sim.BallStateForTest();
        Assert.True(state.Y < 0f, $"expected upward velocity after paddle hit, got {state.Y}");
        Assert.True(state.X > 0f, $"expected angled (rightward) rebound off paddle center-right, got {state.X}");
    }

    [Fact]
    public void Brick_Hit_Adds_Score_Destroys_Brick_And_Accelerates_Ball()
    {
        using var sim = new BreakoutSimulation(startTimer: false);
        sim.Start();

        // Two bricks: the target at chunk (14, 10) plus a decoy far left so hitting the
        // target does not complete the level. Ball approaches from below at walking pace.
        sim.ClearBricksForTest();
        var score = (BreakoutConfig.CourtChunksY - 10) * 5;
        sim.PlaceBrick(14, 10, 1, score);
        sim.PlaceBrick(2, 10, 1, score);
        var launchSpeed = 60f;
        sim.PlaceBallAt(300f, 227f, 0f, -launchSpeed);
        sim.StepOnce();

        Assert.Equal(score, sim.Score);
        // The target brick was hit and destroyed; only the decoy remains.
        Assert.Equal(1, sim.Snapshot().Count(s => s.Id >= BreakoutConfig.BrickRenderIdStart));
        var state = sim.BallStateForTest();
        Assert.True(state.Y > 0f, "ball should bounce off the brick bottom and head down");
        // Decay curve: speed += 10 * (1 - speed/maxspeed), from the placed 60 px/s launch.
        var expectedSpeed = launchSpeed + (10f * (1f - (launchSpeed / BreakoutConfig.BallMaxSpeed)));
        Assert.Equal(expectedSpeed, state.Speed, 0.001f);
    }

    [Fact]
    public void Level_Complete_Advances_Level_And_Grants_Life()
    {
        using var sim = new BreakoutSimulation(startTimer: false);
        sim.Start();
        sim.ClearBricksForTest();

        sim.PlaceBrick(14, 10, 1, 75);
        sim.PlaceBallAt(300f, 227f, 0f, -60f);
        sim.StepOnce();

        Assert.Equal(1, sim.Level);
        Assert.Equal(BreakoutConfig.InitialLives + 1, sim.Lives);
        Assert.False(sim.IsGameOver);
        var state = sim.BallStateForTest();
        Assert.True(state.Moving, "ball should be relaunched after a level win");
        Assert.Equal(BreakoutConfig.BaseBallSpeed, state.Speed, 0.001f);
    }

    [Fact]
    public void Lose_Ball_Decrements_Lives()
    {
        using var sim = new BreakoutSimulation(startTimer: false);
        sim.Start();

        sim.ForceLoseBall();

        Assert.Equal(BreakoutConfig.InitialLives - 1, sim.Lives);
        Assert.False(sim.IsGameOver);
        Assert.False(sim.BallStateForTest().Moving, "ball should reset to the paddle after a lost life");
    }

    [Fact]
    public void Game_Over_When_Lives_Depleted()
    {
        using var sim = new BreakoutSimulation(startTimer: false);
        sim.Start();

        for (var i = 0; i < BreakoutConfig.InitialLives; i++)
        {
            sim.ForceLoseBall();
        }

        Assert.Equal(0, sim.Lives);
        Assert.True(sim.IsGameOver);
    }

    [Fact]
    public void Fast_Ball_Corner_Bounce_Does_Not_Tunnel()
    {
        using var sim = new BreakoutSimulation(startTimer: false);
        sim.Start();

        // A very fast ball in the top-left corner sweeps past both the left and top walls
        // in a single step: the recursive update must resolve both bounces and keep the
        // ball inside the court heading back down-right.
        sim.PlaceBallAt(10f, 10f, -3000f, -3000f);
        sim.StepOnce();

        var state = sim.BallStateForTest();
        Assert.True(state.X > 0f, $"expected rightward velocity after corner bounces, got {state.X}");
        Assert.True(state.Y > 0f, $"expected downward velocity after corner bounces, got {state.Y}");
        var pos = sim.Snapshot().Single(s => s.Id == 2);
        Assert.True(pos.X >= 0f && pos.X <= BreakoutConfig.CourtWidth);
        Assert.True(pos.Y >= 0f && pos.Y <= BreakoutConfig.CourtHeight);
    }

    [Fact(Timeout = 15_000)]
    public async Task Signals_Fire_While_Running_With_Increasing_Seq()
    {
        using var sim = new BreakoutSimulation(seed: 42);
        var ct = TestContext.Current.CancellationToken;
        var signals = new List<BreakoutRenderSignal>();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void Handler(BreakoutRenderSignal s)
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
            Assert.True(signals.Count >= 4, $"expected >= 4 signals, got {signals.Count}");
            for (var i = 1; i < signals.Count; i++)
            {
                Assert.Equal(signals[i - 1].Seq + 1, signals[i].Seq);
            }
            Assert.All(signals, s => Assert.True(s.Started));
            Assert.All(signals, s => Assert.False(s.GameOver));
        }
    }
}
