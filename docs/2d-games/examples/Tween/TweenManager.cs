// ============================================================================
// TweenManager.cs — Tween Engine with Object Pooling
// Extracted from: G41 — Tweening & Easing
// Part of: Universal 2D Engine Toolkit Examples
//
// Zero-allocation after warm-up. Supports delay, looping (restart/ping-pong),
// pause/resume, and fluent chaining API.
// ============================================================================

using Microsoft.Xna.Framework;

namespace U2DToolkit.Examples.Tween;

/// <summary>
/// Tween lifecycle states.
/// </summary>
public enum TweenState : byte
{
    /// <summary>Inactive — returned to pool or not yet started.</summary>
    Idle,
    /// <summary>Waiting for delay to elapse before starting.</summary>
    Delayed,
    /// <summary>Actively interpolating.</summary>
    Running,
    /// <summary>Paused — will resume from current position.</summary>
    Paused,
    /// <summary>Finished — will be returned to pool.</summary>
    Complete
}

/// <summary>
/// Loop behavior when a tween reaches its end.
/// </summary>
public enum LoopMode : byte
{
    /// <summary>Play once and complete.</summary>
    None,
    /// <summary>Restart from the beginning each loop.</summary>
    Restart,
    /// <summary>Reverse direction each loop (yoyo effect).</summary>
    PingPong
}

/// <summary>
/// A single tween instance. Interpolates a float value from
/// <see cref="From"/> to <see cref="To"/> over <see cref="Duration"/>
/// seconds using an <see cref="EaseFunc"/> curve.
/// <para>
/// Tweens are pooled by <see cref="TweenManager"/> — do not construct
/// directly. Use <see cref="TweenManager.To"/> to create tweens.
/// </para>
/// </summary>
public class Tween
{
    // ── Value data ───────────────────────────────────────────────────
    
    /// <summary>Starting value.</summary>
    public float From;
    
    /// <summary>Target value.</summary>
    public float To;
    
    /// <summary>Current interpolated value (updated each frame).</summary>
    public float Current;

    // ── Timing ───────────────────────────────────────────────────────
    
    /// <summary>Total duration in seconds.</summary>
    public float Duration;
    
    /// <summary>Elapsed time in seconds.</summary>
    public float Elapsed;
    
    /// <summary>Delay before starting in seconds.</summary>
    public float Delay;
    
    /// <summary>Remaining delay time.</summary>
    public float DelayRemaining;

    // ── Curve ────────────────────────────────────────────────────────
    
    /// <summary>Easing function applied to normalized time.</summary>
    public EaseFunc EaseFunc = Ease.Linear;

    // ── State ────────────────────────────────────────────────────────
    
    /// <summary>Current lifecycle state.</summary>
    public TweenState State;
    
    /// <summary>Loop behavior when the tween completes.</summary>
    public LoopMode Loop;
    
    /// <summary>Remaining loop count. -1 = infinite.</summary>
    public int LoopCount;
    
    /// <summary>Whether the tween is currently playing in reverse (for PingPong).</summary>
    public bool IsReversed;

    // ── Callbacks ────────────────────────────────────────────────────
    
    /// <summary>Called every frame with the current interpolated value.</summary>
    public Action<float>? OnUpdate;
    
    /// <summary>Called once when the tween finishes (after all loops).</summary>
    public Action? OnComplete;

    // ── Pool link (intrusive linked list) ────────────────────────────
    internal Tween? PoolNext;

    /// <summary>Normalized time in [0, 1].</summary>
    public float NormalizedTime => Duration > 0f
        ? MathHelper.Clamp(Elapsed / Duration, 0f, 1f)
        : 1f;

    /// <summary>Reset all fields to defaults for pool reuse.</summary>
    public void Reset()
    {
        From = To = Current = 0f;
        Duration = Elapsed = Delay = DelayRemaining = 0f;
        EaseFunc = Ease.Linear;
        State = TweenState.Idle;
        Loop = LoopMode.None;
        LoopCount = 0;
        IsReversed = false;
        OnUpdate = null;
        OnComplete = null;
    }
}

/// <summary>
/// Object pool for <see cref="Tween"/> instances.
/// Uses an intrusive linked list for zero-allocation rent/return.
/// Pre-warms on construction to avoid GC pressure during gameplay.
/// </summary>
public class TweenPool
{
    private Tween? _head;
    private int _count;

    /// <summary>
    /// Creates a pool with the specified number of pre-allocated tweens.
    /// </summary>
    /// <param name="prewarm">Number of tweens to pre-allocate. 64 is typical.</param>
    public TweenPool(int prewarm = 64)
    {
        for (int i = 0; i < prewarm; i++)
            Return(new Tween());
    }

    /// <summary>
    /// Rent a tween from the pool. Creates a new one if pool is empty.
    /// The tween is reset before being returned.
    /// </summary>
    public Tween Rent()
    {
        if (_head == null)
            return new Tween();

        var t = _head;
        _head = t.PoolNext;
        t.PoolNext = null;
        _count--;
        t.Reset();
        return t;
    }

    /// <summary>
    /// Return a tween to the pool for reuse.
    /// </summary>
    public void Return(Tween t)
    {
        t.Reset();
        t.PoolNext = _head;
        _head = t;
        _count++;
    }

    /// <summary>Number of tweens currently in the pool.</summary>
    public int Count => _count;
}

/// <summary>
/// Central tween manager. Creates, updates, and recycles tweens.
/// Zero-allocation after initial warm-up thanks to <see cref="TweenPool"/>.
/// <para>
/// Usage:
/// <code>
/// var tweens = new TweenManager();
///
/// // Create a tween
/// tweens.To(0f, 100f, 0.5f, Ease.QuadOut)
///     .SetOnUpdate(v => _playerX = v)
///     .SetOnComplete(() => Debug.Log("Done!"));
///
/// // In Update():
/// tweens.Update(deltaTime);
/// </code>
/// </para>
/// </summary>
public class TweenManager
{
    private readonly List<Tween> _active = new(128);
    private readonly TweenPool _pool = new(64);

    /// <summary>
    /// Start a new tween interpolating from → to over duration seconds.
    /// Returns the <see cref="Tween"/> for fluent configuration.
    /// </summary>
    /// <param name="from">Starting value.</param>
    /// <param name="to">Target value.</param>
    /// <param name="duration">Duration in seconds.</param>
    /// <param name="ease">Easing function (defaults to Linear).</param>
    /// <returns>The created tween for chaining.</returns>
    public Tween To(float from, float to, float duration, EaseFunc? ease = null)
    {
        var t = _pool.Rent();
        t.From = from;
        t.To = to;
        t.Current = from;
        t.Duration = duration;
        t.EaseFunc = ease ?? Ease.Linear;
        t.State = TweenState.Running;
        _active.Add(t);
        return t;
    }

    /// <summary>
    /// Start a tween with a delay before it begins.
    /// </summary>
    public Tween ToDelayed(float from, float to, float duration, float delay,
                           EaseFunc? ease = null)
    {
        var t = To(from, to, duration, ease);
        t.Delay = delay;
        t.DelayRemaining = delay;
        t.State = TweenState.Delayed;
        return t;
    }

    /// <summary>
    /// Advance all active tweens. Call once per frame.
    /// </summary>
    /// <param name="dt">Delta time in seconds.</param>
    public void Update(float dt)
    {
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var t = _active[i];

            if (t.State == TweenState.Paused) continue;

            // Handle delay phase
            if (t.State == TweenState.Delayed)
            {
                t.DelayRemaining -= dt;
                if (t.DelayRemaining > 0f) continue;
                dt = -t.DelayRemaining; // overflow into tween time
                t.State = TweenState.Running;
            }

            // Advance elapsed time
            t.Elapsed += dt;

            // Compute eased value
            float raw = t.NormalizedTime;
            float eased = t.EaseFunc(raw);

            if (t.IsReversed)
                t.Current = MathHelper.Lerp(t.To, t.From, eased);
            else
                t.Current = MathHelper.Lerp(t.From, t.To, eased);

            t.OnUpdate?.Invoke(t.Current);

            // Check completion
            if (t.Elapsed >= t.Duration)
            {
                if (t.Loop != LoopMode.None &&
                    (t.LoopCount == -1 || t.LoopCount > 0))
                {
                    if (t.LoopCount > 0) t.LoopCount--;

                    t.Elapsed = 0f;
                    if (t.Loop == LoopMode.PingPong)
                        t.IsReversed = !t.IsReversed;
                }
                else
                {
                    t.State = TweenState.Complete;
                    t.OnComplete?.Invoke();
                    _active.RemoveAt(i);
                    _pool.Return(t);
                }
            }
        }
    }

    /// <summary>Cancel a specific tween and return it to the pool.</summary>
    public void Cancel(Tween t)
    {
        t.State = TweenState.Complete;
        _active.Remove(t);
        _pool.Return(t);
    }

    /// <summary>Cancel all active tweens.</summary>
    public void CancelAll()
    {
        foreach (var t in _active) _pool.Return(t);
        _active.Clear();
    }

    /// <summary>Number of currently active tweens.</summary>
    public int ActiveCount => _active.Count;
}

/// <summary>
/// Fluent extension methods for configuring tweens.
/// </summary>
public static class TweenExtensions
{
    /// <summary>Set the easing function.</summary>
    public static Tween SetEase(this Tween t, EaseFunc ease)
    {
        t.EaseFunc = ease;
        return t;
    }

    /// <summary>
    /// Configure looping behavior.
    /// </summary>
    /// <param name="t">The tween.</param>
    /// <param name="mode">Loop mode (Restart or PingPong).</param>
    /// <param name="count">Number of additional loops. -1 = infinite.</param>
    public static Tween SetLoop(this Tween t, LoopMode mode, int count = -1)
    {
        t.Loop = mode;
        t.LoopCount = count;
        return t;
    }

    /// <summary>Add a delay before the tween starts.</summary>
    public static Tween SetDelay(this Tween t, float delay)
    {
        t.Delay = delay;
        t.DelayRemaining = delay;
        t.State = TweenState.Delayed;
        return t;
    }

    /// <summary>Set the per-frame update callback.</summary>
    public static Tween SetOnUpdate(this Tween t, Action<float> cb)
    {
        t.OnUpdate = cb;
        return t;
    }

    /// <summary>Set the completion callback.</summary>
    public static Tween SetOnComplete(this Tween t, Action cb)
    {
        t.OnComplete = cb;
        return t;
    }

    /// <summary>Pause a running tween.</summary>
    public static void Pause(this Tween t)
    {
        if (t.State is TweenState.Running or TweenState.Delayed)
            t.State = TweenState.Paused;
    }

    /// <summary>Resume a paused tween.</summary>
    public static void Resume(this Tween t)
    {
        if (t.State == TweenState.Paused)
            t.State = TweenState.Running;
    }

    /// <summary>Reverse the tween direction (for manual ping-pong control).</summary>
    public static void Reverse(this Tween t) => t.IsReversed = !t.IsReversed;
}
