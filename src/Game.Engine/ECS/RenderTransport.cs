namespace Game.Engine.ECS;

/// <summary>
///     Transport seam between a simulation and its presentation layer (ADR-007).
///     The simulation pushes batched render signals; the implementation decides how
///     they reach the consumer — an in-process event consumed by the SSE endpoints
///     (server-authoritative host), or a direct memory path for the co-located
///     single-player host (Phase 2: pinned buffer read as a <c>Float32Array</c>).
///     Replaces the per-simulation <c>OnRenderSignal?.Invoke</c> emission sites so a
///     co-located build can bypass the HTTP/SSE/JSON boundary without touching
///     simulation logic (ADR-001/003 stay intact).
/// </summary>
/// <typeparam name="TSignal">Batched render-signal record emitted by the simulation.</typeparam>
public interface IRenderTransport<TSignal>
{
    /// <summary>Raised for every pushed signal. The SSE host endpoints subscribe here.</summary>
    event Action<TSignal>? OnSignal;

    /// <summary>Pushes one batched render signal toward the presentation side.</summary>
    void Push(TSignal signal);
}

/// <summary>
///     Default transport for the server-authoritative host (ADR-001): signals are
///     delivered as in-process events that the ASP.NET Core SSE endpoints serialize
///     to JSON and stream to the browser. Behavior is identical to the pre-ADR-007
///     per-simulation <c>OnRenderSignal</c> events.
/// </summary>
public sealed class ServerRenderTransport<TSignal> : IRenderTransport<TSignal>
{
    public event Action<TSignal>? OnSignal;

    public void Push(TSignal signal) => OnSignal?.Invoke(signal);
}
