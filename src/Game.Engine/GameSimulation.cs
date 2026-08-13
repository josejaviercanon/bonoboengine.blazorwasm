namespace Game.Engine;

public record RenderMessageEvent(string Message);

public class GameSimulation
{
    public event Action<RenderMessageEvent>? OnRenderMessage;

    public void PublishHello()
    {
        OnRenderMessage?.Invoke(new RenderMessageEvent("Hello world to PixiJs Gaming!"));
    }
}
