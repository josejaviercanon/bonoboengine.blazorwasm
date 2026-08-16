using Game.Engine;
using Xunit;

namespace Game.Tests;

public class GameSimulationTests
{
    [Fact]
    public void PublishHello_Raises_RenderMessage_Event()
    {
        var sim = new GameSimulation();
        RenderMessageEvent? received = null;

        sim.OnRenderMessage += e => received = e;
        sim.PublishHello();

        Assert.NotNull(received);
        Assert.Equal("Hello world to PixiJs Gaming!", received!.Message);
    }

    [Fact]
    public void PublishHello_Without_Subscribers_Is_NoOp()
    {
        var sim = new GameSimulation();
        sim.PublishHello(); // must not throw
        Assert.True(true);
    }
}
