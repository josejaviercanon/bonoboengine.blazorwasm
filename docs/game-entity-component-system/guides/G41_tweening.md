# G41 — Tweening & Easing


> **Category:** Guide · **Related:** [Tweening Theory](../../core/concepts/tweening-theory.md) · [G1 Custom Code Recipes](./G1_custom_code_recipes.md) · [G30 Game Feel Tooling](./G30_game_feel_tooling.md) · [G5 UI Framework](./G5_ui_framework.md) · [G42 Screen Transitions](./G42_screen_transitions.md) · [G31 Animation State Machines](./G31_animation_state_machines.md) · [G64 Combat & Damage Systems](./G64_combat_damage_systems.md)

Tweening (short for "in-betweening") interpolates values over time with configurable easing curves. This guide covers a complete, zero-allocation tween engine built for MonoGame + Arch ECS.

> **Start simple.** The full engine below covers every edge case, but most games need far less. A production tween system can be as small as ~70 lines — a `TweenManager` that stores a list and an `Update` loop, and a `Tween` class with from/to/duration/easing/callback. `OnComplete` chaining handles sequencing. Only add pooling (>100 concurrent tweens), generic `Tween<T>` (frequent Vector2/Color interpolation), or formal sequences (complex multi-step animations) when you hit those thresholds.
>
> **Minimal production example (~70 LOC):**
>
> ```csharp
> public class TweenManager
> {
>     private readonly List<Tween> _tweens = new();
>
>     public Tween Add(float from, float to, float duration, Action<float> setter,
>                      Func<float, float>? easing = null)
>     {
>         Tween tween = new(from, to, duration, setter, easing);
>         _tweens.Add(tween);
>         return tween;
>     }
>
>     public void Update(float dt)
>     {
>         for (int i = _tweens.Count - 1; i >= 0; i--)
>         {
>             _tweens[i].Update(dt);
>             if (_tweens[i].IsComplete)
>                 _tweens.RemoveAt(i);
>         }
>     }
> }
>
> public class Tween
> {
>     private float _elapsed;
>     private readonly float _duration;
>     private readonly Action<float> _setter;
>     private readonly Func<float, float> _easing;
>     private readonly float _from, _to;
>     private bool _completeFired;
>
>     public float Delay { get; set; }
>     public bool IsComplete => _elapsed >= Delay + _duration;
>     public Action? OnComplete { get; set; }
>
>     public Tween(float from, float to, float duration, Action<float> setter,
>                  Func<float, float>? easing = null)
>     {
>         _from = from; _to = to; _duration = duration;
>         _setter = setter; _easing = easing ?? (t => t);
>     }
>
>     public void Update(float dt)
>     {
>         _elapsed = Math.Min(_elapsed + dt, Delay + _duration);
>         float active = Math.Max(_elapsed - Delay, 0f);
>         float t = _easing(active / _duration);
>         _setter(_from + (_to - _from) * t);
>
>         if (active >= _duration && !_completeFired)
>         {
>             _completeFired = true;
>             OnComplete?.Invoke();
>         }
>     }
> }
> ```
>
> **Sequencing via `OnComplete` chaining:**
>
> ```csharp
> // Slide panel in, then fade text — no TweenSequence class needed
> var slide = _tweens.Add(-200f, 0f, 0.4f, x => _panelX = x, Ease.BackOut);
> slide.OnComplete = () =>
> {
>     _tweens.Add(0f, 1f, 0.3f, a => _textAlpha = a, Ease.SineOut);
> };
> ```

---

## 1 — Easing Functions

Every easing function takes `float t` in `[0, 1]` and returns a mapped `float`. These are pure, stateless, and allocation-free.

```csharp
public static class Ease
{
    // ── Linear ──────────────────────────────────────────
    public static float Linear(float t) => t;

    // ── Quad ────────────────────────────────────────────
    public static float QuadIn(float t) => t * t;
    public static float QuadOut(float t) => t * (2f - t);
    public static float QuadInOut(float t) =>
        t < 0.5f ? 2f * t * t : -1f + (4f - 2f * t) * t;

    // ── Cubic ───────────────────────────────────────────
    public static float CubicIn(float t) => t * t * t;
    public static float CubicOut(float t) { float u = t - 1f; return u * u * u + 1f; }
    public static float CubicInOut(float t) =>
        t < 0.5f ? 4f * t * t * t : 1f + (t - 1f) * (2f * t - 2f) * (2f * t - 2f);

    // ── Quart ───────────────────────────────────────────
    public static float QuartIn(float t) => t * t * t * t;
    public static float QuartOut(float t) { float u = t - 1f; return 1f - u * u * u * u; }
    public static float QuartInOut(float t) =>
        t < 0.5f ? 8f * t * t * t * t : 1f - 8f * (t - 1f) * (t - 1f) * (t - 1f) * (t - 1f);

    // ── Quint ───────────────────────────────────────────
    public static float QuintIn(float t) => t * t * t * t * t;
    public static float QuintOut(float t) { float u = t - 1f; return 1f + u * u * u * u * u; }
    public static float QuintInOut(float t) =>
        t < 0.5f ? 16f * t * t * t * t * t : 1f + 16f * (t - 1f) * (t - 1f) * (t - 1f) * (t - 1f) * (t - 1f);

    // ── Sine ────────────────────────────────────────────
    public static float SineIn(float t) => 1f - MathF.Cos(t * MathF.PI * 0.5f);
    public static float SineOut(float t) => MathF.Sin(t * MathF.PI * 0.5f);
    public static float SineInOut(float t) => 0.5f * (1f - MathF.Cos(MathF.PI * t));

    // ── Expo ────────────────────────────────────────────
    public static float ExpoIn(float t) =>
        t == 0f ? 0f : MathF.Pow(2f, 10f * (t - 1f));
    public static float ExpoOut(float t) =>
        t == 1f ? 1f : 1f - MathF.Pow(2f, -10f * t);
    public static float ExpoInOut(float t)
    {
        if (t == 0f) return 0f;
        if (t == 1f) return 1f;
        return t < 0.5f
            ? 0.5f * MathF.Pow(2f, 20f * t - 10f)
            : 1f - 0.5f * MathF.Pow(2f, -20f * t + 10f);
    }

    // ── Circ ────────────────────────────────────────────
    public static float CircIn(float t) => 1f - MathF.Sqrt(1f - t * t);
    public static float CircOut(float t) => MathF.Sqrt(1f - (t - 1f) * (t - 1f));
    public static float CircInOut(float t) =>
        t < 0.5f
            ? 0.5f * (1f - MathF.Sqrt(1f - 4f * t * t))
            : 0.5f * (MathF.Sqrt(1f - (2f * t - 2f) * (2f * t - 2f)) + 1f);

    // ── Elastic ─────────────────────────────────────────
    private const float ElasticP = 0.3f;
    public static float ElasticIn(float t) =>
        t is 0f or 1f ? t : -MathF.Pow(2f, 10f * t - 10f) * MathF.Sin((t * 10f - 10.75f) * (2f * MathF.PI / ElasticP));
    public static float ElasticOut(float t) =>
        t is 0f or 1f ? t : MathF.Pow(2f, -10f * t) * MathF.Sin((t * 10f - 0.75f) * (2f * MathF.PI / ElasticP)) + 1f;
    public static float ElasticInOut(float t)
    {
        if (t is 0f or 1f) return t;
        const float p = ElasticP * 1.5f;
        return t < 0.5f
            ? -0.5f * MathF.Pow(2f, 20f * t - 10f) * MathF.Sin((20f * t - 11.125f) * (2f * MathF.PI / p))
            :  0.5f * MathF.Pow(2f, -20f * t + 10f) * MathF.Sin((20f * t - 11.125f) * (2f * MathF.PI / p)) + 1f;
    }

    // ── Back ────────────────────────────────────────────
    private const float S = 1.70158f;
    public static float BackIn(float t) => t * t * ((S + 1f) * t - S);
    public static float BackOut(float t) { float u = t - 1f; return u * u * ((S + 1f) * u + S) + 1f; }
    public static float BackInOut(float t)
    {
        const float s2 = S * 1.525f;
        float u = t * 2f;
        return u < 1f
            ? 0.5f * (u * u * ((s2 + 1f) * u - s2))
            : 0.5f * ((u -= 2f) * u * ((s2 + 1f) * u + s2) + 2f);
    }

    // ── Bounce ──────────────────────────────────────────
    public static float BounceOut(float t)
    {
        const float n = 7.5625f;
        const float d = 2.75f;
        if (t < 1f / d)   return n * t * t;
        if (t < 2f / d)   return n * (t -= 1.5f / d) * t + 0.75f;
        if (t < 2.5f / d) return n * (t -= 2.25f / d) * t + 0.9375f;
        return n * (t -= 2.625f / d) * t + 0.984375f;
    }
    public static float BounceIn(float t) => 1f - BounceOut(1f - t);
    public static float BounceInOut(float t) =>
        t < 0.5f ? 0.5f * BounceIn(t * 2f) : 0.5f * BounceOut(t * 2f - 1f) + 0.5f;
}
```

### Delegate alias

```csharp
/// <summary>Easing function signature: t in [0,1] → mapped value.</summary>
public delegate float EaseFunc(float t);
```

---

## 2 — Tween Engine

### 2.1 — Core Tween

```csharp
public enum TweenState : byte { Idle, Delayed, Running, Paused, Complete }

public enum LoopMode : byte { None, Restart, PingPong }

public class Tween
{
    // ── Value data ──
    public float From;
    public float To;
    public float Current;

    // ── Timing ──
    public float Duration;
    public float Elapsed;
    public float Delay;
    public float DelayRemaining;

    // ── Curve ──
    public EaseFunc EaseFunc;

    // ── State ──
    public TweenState State;
    public LoopMode Loop;
    public int LoopCount;        // -1 = infinite, >0 = remaining loops
    public bool IsReversed;

    // ── Callbacks ──
    public Action<float>? OnUpdate;   // receives Current each frame
    public Action?        OnComplete;

    // ── Pool link ──
    internal Tween? PoolNext;

    public float NormalizedTime => Duration > 0f ? MathHelper.Clamp(Elapsed / Duration, 0f, 1f) : 1f;

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
```

### 2.2 — Object Pool

Zero-allocation after warm-up. Pre-allocate a batch on startup; tweens return to the pool when complete or cancelled.

```csharp
public class TweenPool
{
    private Tween? _head;
    private int _count;

    public TweenPool(int prewarm = 64)
    {
        for (int i = 0; i < prewarm; i++)
            Return(new Tween());
    }

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

    public void Return(Tween t)
    {
        t.Reset();
        t.PoolNext = _head;
        _head = t;
        _count++;
    }

    public int Count => _count;
}
```

### 2.3 — TweenManager

```csharp
public class TweenManager
{
    private readonly List<Tween> _active = new(128);
    private readonly TweenPool _pool = new(64);

    /// <summary>Start a new tween. Returns the Tween for chaining setup.</summary>
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

    /// <summary>Start a tween with a delay.</summary>
    public Tween ToDelayed(float from, float to, float duration, float delay, EaseFunc? ease = null)
    {
        var t = To(from, to, duration, ease);
        t.Delay = delay;
        t.DelayRemaining = delay;
        t.State = TweenState.Delayed;
        return t;
    }

    public void Update(float dt)
    {
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var t = _active[i];

            if (t.State == TweenState.Paused) continue;

            // Handle delay
            if (t.State == TweenState.Delayed)
            {
                t.DelayRemaining -= dt;
                if (t.DelayRemaining > 0f) continue;
                dt = -t.DelayRemaining; // overflow into tween
                t.State = TweenState.Running;
            }

            // Advance
            t.Elapsed += dt;

            float raw = t.NormalizedTime;
            float eased = t.EaseFunc(raw);

            if (t.IsReversed)
                t.Current = MathHelper.Lerp(t.To, t.From, eased);
            else
                t.Current = MathHelper.Lerp(t.From, t.To, eased);

            t.OnUpdate?.Invoke(t.Current);

            // Completion
            if (t.Elapsed >= t.Duration)
            {
                if (t.Loop != LoopMode.None && (t.LoopCount == -1 || t.LoopCount > 0))
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

    /// <summary>Cancel a specific tween.</summary>
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

    public int ActiveCount => _active.Count;
}
```

### Usage

```csharp
// In your Game class:
private readonly TweenManager _tweens = new();

// Start a tween
var tw = _tweens.To(0f, 100f, 0.5f, Ease.QuadOut);
tw.OnUpdate = val => _playerX = val;
tw.OnComplete = () => Console.WriteLine("Done!");

// In Update():
_tweens.Update((float)gameTime.ElapsedGameTime.TotalSeconds);
```

---

## 3 — Tween Targets

### 3.1 — Multi-type interpolation delegates

```csharp
/// <summary>Generic interpolation: lerp from A to B by factor t.</summary>
public delegate T LerpFunc<T>(T a, T b, float t);

public static class Lerps
{
    public static float Float(float a, float b, float t) => a + (b - a) * t;

    public static Vector2 Vec2(Vector2 a, Vector2 b, float t) => Vector2.Lerp(a, b, t);

    public static Color ColorLerp(Color a, Color b, float t) => Color.Lerp(a, b, t);

    public static Rectangle Rect(Rectangle a, Rectangle b, float t) => new(
        (int)(a.X + (b.X - a.X) * t),
        (int)(a.Y + (b.Y - a.Y) * t),
        (int)(a.Width  + (b.Width  - a.Width)  * t),
        (int)(a.Height + (b.Height - a.Height) * t)
    );
}
```

### 3.2 — Generic Tween\<T\>

```csharp
public class Tween<T>
{
    public T From;
    public T To;
    public T Current;
    public float Duration;
    public float Elapsed;
    public EaseFunc EaseFunc;
    public LerpFunc<T> LerpFunc;
    public TweenState State;
    public Action<T>? OnUpdate;
    public Action? OnComplete;

    public Tween(T from, T to, float duration, LerpFunc<T> lerp, EaseFunc? ease = null)
    {
        From = from;
        To = to;
        Current = from;
        Duration = duration;
        LerpFunc = lerp;
        EaseFunc = ease ?? Ease.Linear;
        State = TweenState.Running;
    }

    public bool Advance(float dt)
    {
        if (State != TweenState.Running) return false;
        Elapsed += dt;
        float raw = MathHelper.Clamp(Elapsed / Duration, 0f, 1f);
        float eased = EaseFunc(raw);
        Current = LerpFunc(From, To, eased);
        OnUpdate?.Invoke(Current);

        if (Elapsed >= Duration)
        {
            State = TweenState.Complete;
            OnComplete?.Invoke();
            return true; // done
        }
        return false;
    }
}
```

### 3.3 — Property tweens via Action setter

When you can't pass a ref (common in ECS), use an `Action<T>` setter:

```csharp
// Tween a position Vector2 on some object
var posTween = new Tween<Vector2>(
    from: new Vector2(0, 0),
    to:   new Vector2(200, 50),
    duration: 0.6f,
    lerp: Lerps.Vec2,
    ease: Ease.BackOut
);
posTween.OnUpdate = pos => myEntity.Position = pos;

// Tween a color
var colorTween = new Tween<Color>(
    from: Color.White,
    to:   Color.Transparent,
    duration: 1.0f,
    lerp: Lerps.ColorLerp,
    ease: Ease.SineOut
);
colorTween.OnUpdate = c => mySprite.Tint = c;
```

---

## 4 — Tween Sequences

### 4.1 — Sequential chains

```csharp
public class TweenSequence
{
    private readonly List<Tween> _steps = new();
    private int _currentIndex;
    public bool IsComplete => _currentIndex >= _steps.Count;
    public Action? OnSequenceComplete;

    public TweenSequence Append(Tween t)
    {
        t.State = TweenState.Idle; // don't start yet
        _steps.Add(t);
        return this;
    }

    public void Start()
    {
        _currentIndex = 0;
        if (_steps.Count > 0)
            _steps[0].State = TweenState.Running;
    }

    public void Update(float dt)
    {
        if (IsComplete) return;

        var current = _steps[_currentIndex];

        // Advance using simple inline logic (mirrors TweenManager)
        if (current.State == TweenState.Running)
        {
            current.Elapsed += dt;
            float raw = current.NormalizedTime;
            float eased = current.EaseFunc(raw);
            current.Current = MathHelper.Lerp(current.From, current.To, eased);
            current.OnUpdate?.Invoke(current.Current);

            if (current.Elapsed >= current.Duration)
            {
                current.State = TweenState.Complete;
                current.OnComplete?.Invoke();
                _currentIndex++;

                if (_currentIndex < _steps.Count)
                    _steps[_currentIndex].State = TweenState.Running;
                else
                    OnSequenceComplete?.Invoke();
            }
        }
    }
}
```

**Usage: UI panel slides in then fades:**

```csharp
var seq = new TweenSequence();

var slideIn = _tweens.To(-300f, 0f, 0.4f, Ease.BackOut);
slideIn.OnUpdate = x => _panelX = x;

var fade = _tweens.To(0f, 1f, 0.3f, Ease.SineIn);
fade.OnUpdate = a => _panelAlpha = a;

seq.Append(slideIn).Append(fade);
seq.OnSequenceComplete = () => _panelReady = true;
seq.Start();
```

### 4.2 — Parallel (group) tweens

```csharp
public class TweenGroup
{
    private readonly List<Tween> _tweens = new();
    private int _completeCount;
    public bool IsComplete => _completeCount >= _tweens.Count;
    public Action? OnGroupComplete;

    public TweenGroup Add(Tween t)
    {
        _tweens.Add(t);
        return this;
    }

    public void Update(float dt)
    {
        if (IsComplete) return;

        _completeCount = 0;
        foreach (var t in _tweens)
        {
            if (t.State == TweenState.Complete) { _completeCount++; continue; }
            if (t.State != TweenState.Running)  continue;

            t.Elapsed += dt;
            float raw = t.NormalizedTime;
            float eased = t.EaseFunc(raw);
            t.Current = MathHelper.Lerp(t.From, t.To, eased);
            t.OnUpdate?.Invoke(t.Current);

            if (t.Elapsed >= t.Duration)
            {
                t.State = TweenState.Complete;
                t.OnComplete?.Invoke();
                _completeCount++;
            }
        }

        if (IsComplete)
            OnGroupComplete?.Invoke();
    }
}
```

**Usage: scale + fade simultaneously:**

```csharp
var group = new TweenGroup();
group.Add(_tweens.To(0f, 1f, 0.3f, Ease.BackOut));   // scale
group.Add(_tweens.To(0f, 1f, 0.3f, Ease.SineOut));    // alpha
group.OnGroupComplete = () => Debug.Log("Both done");
```

---

## 5 — Tween Controls

### Pause / Resume / Cancel / Reverse

```csharp
// Extension methods for fluid API
public static class TweenExtensions
{
    public static Tween SetEase(this Tween t, EaseFunc ease) { t.EaseFunc = ease; return t; }
    public static Tween SetLoop(this Tween t, LoopMode mode, int count = -1)
    {
        t.Loop = mode;
        t.LoopCount = count;
        return t;
    }
    public static Tween SetDelay(this Tween t, float delay)
    {
        t.Delay = delay;
        t.DelayRemaining = delay;
        t.State = TweenState.Delayed;
        return t;
    }
    public static Tween SetOnUpdate(this Tween t, Action<float> cb) { t.OnUpdate = cb; return t; }
    public static Tween SetOnComplete(this Tween t, Action cb) { t.OnComplete = cb; return t; }

    public static void Pause(this Tween t)
    {
        if (t.State == TweenState.Running || t.State == TweenState.Delayed)
            t.State = TweenState.Paused;
    }
    public static void Resume(this Tween t)
    {
        if (t.State == TweenState.Paused)
            t.State = TweenState.Running;
    }
    public static void Reverse(this Tween t) => t.IsReversed = !t.IsReversed;
}
```

### Looping examples

```csharp
// Infinite ping-pong (yoyo) — great for hover effects
_tweens.To(0f, 10f, 0.8f, Ease.SineInOut)
    .SetLoop(LoopMode.PingPong)
    .SetOnUpdate(y => _iconOffsetY = y);

// Loop 3 times then stop — countdown pulse
_tweens.To(1f, 1.3f, 0.2f, Ease.QuadOut)
    .SetLoop(LoopMode.PingPong, 3)
    .SetOnUpdate(s => _pulseScale = s)
    .SetOnComplete(() => _pulseScale = 1f);

// Delayed start — stagger multiple elements
for (int i = 0; i < 5; i++)
{
    int idx = i;
    _tweens.To(-200f, 0f, 0.4f, Ease.BackOut)
        .SetDelay(i * 0.08f)
        .SetOnUpdate(x => _menuItems[idx].X = x);
}
```

---

## 6 — Common Game Uses

### 6.1 — UI slide in / out

```csharp
void ShowPanel()
{
    _tweens.To(_panel.X, 0f, 0.35f, Ease.BackOut)
        .SetOnUpdate(x => _panel.X = x);
    _tweens.To(0f, 1f, 0.25f, Ease.SineOut)
        .SetOnUpdate(a => _panel.Alpha = a);
}

void HidePanel()
{
    _tweens.To(_panel.X, -_panel.Width, 0.3f, Ease.CubicIn)
        .SetOnUpdate(x => _panel.X = x)
        .SetOnComplete(() => _panel.Visible = false);
}
```

### 6.2 — Damage number float-up

```csharp
void SpawnDamageNumber(Vector2 pos, int amount)
{
    var dmg = new DamageNumber { Position = pos, Text = amount.ToString(), Alpha = 1f };
    _damageNumbers.Add(dmg);

    // Float upward
    _tweens.To(pos.Y, pos.Y - 40f, 0.8f, Ease.QuadOut)
        .SetOnUpdate(y => dmg.Position = new Vector2(dmg.Position.X, y));

    // Fade out in last half
    _tweens.ToDelayed(1f, 0f, 0.4f, 0.4f, Ease.SineIn)
        .SetOnUpdate(a => dmg.Alpha = a)
        .SetOnComplete(() => _damageNumbers.Remove(dmg));
}
```

### 6.3 — Entity knockback

```csharp
void ApplyKnockback(Entity entity, Vector2 dir, float force)
{
    var pos = entity.Get<Position>().Value;
    var target = pos + dir * force;

    _tweens.To(0f, 1f, 0.15f, Ease.ExpoOut)
        .SetOnUpdate(t =>
        {
            var lerped = Vector2.Lerp(pos, target, t);
            entity.Set(new Position(lerped));
        });
}
```

### 6.4 — Camera zoom

```csharp
void ZoomTo(float targetZoom, float duration = 0.5f)
{
    _tweens.To(_camera.Zoom, targetZoom, duration, Ease.SineInOut)
        .SetOnUpdate(z => _camera.Zoom = z);
}
```

### 6.5 — Screen shake decay

```csharp
void ScreenShake(float intensity, float duration = 0.3f)
{
    _tweens.To(intensity, 0f, duration, Ease.ExpoOut)
        .SetOnUpdate(i =>
        {
            _camera.Offset = new Vector2(
                Random.Shared.NextSingle() * 2f * i - i,
                Random.Shared.NextSingle() * 2f * i - i
            );
        })
        .SetOnComplete(() => _camera.Offset = Vector2.Zero);
}
```

### 6.6 — Health bar smooth drain

```csharp
void SetHealth(float newHp)
{
    _tweens.To(_displayedHp, newHp, 0.4f, Ease.QuadOut)
        .SetOnUpdate(hp => _displayedHp = hp);
}
```

### 6.7 — Pickup magnet arc

```csharp
void MagnetPickup(Entity pickup, Entity player)
{
    var start = pickup.Get<Position>().Value;

    _tweens.To(0f, 1f, 0.35f, Ease.QuadIn)
        .SetOnUpdate(t =>
        {
            var end = player.Get<Position>().Value; // track live position
            var mid = (start + end) / 2f + new Vector2(0, -30f); // arc control point
            // Quadratic bezier
            var a = Vector2.Lerp(start, mid, t);
            var b = Vector2.Lerp(mid, end, t);
            pickup.Set(new Position(Vector2.Lerp(a, b, t)));
        })
        .SetOnComplete(() => CollectPickup(pickup));
}
```

---

## 7 — ECS Integration

### 7.1 — TweenComponent

```csharp
public record struct TweenComponent(
    float From,
    float To,
    float Duration,
    float Elapsed,
    EaseFunc EaseFunc,
    TweenTarget Target, // what to tween
    LoopMode Loop,
    int LoopCount,
    bool IsReversed,
    bool RemoveOnComplete
);

/// <summary>Which component field the tween drives.</summary>
public enum TweenTarget : byte
{
    PositionX,
    PositionY,
    ScaleX,
    ScaleY,
    ScaleUniform,
    Rotation,
    Alpha,
    ColorR, ColorG, ColorB
}
```

### 7.2 — Multi-tween support

An entity might need multiple concurrent tweens (e.g. fade alpha AND move X). Use a buffer component:

```csharp
public record struct TweenBuffer(TweenData[] Tweens, int Count);

public struct TweenData
{
    public float From, To, Duration, Elapsed;
    public EaseFunc EaseFunc;
    public TweenTarget Target;
    public LoopMode Loop;
    public int LoopCount;
    public bool IsReversed;
}
```

### 7.3 — TweenSystem

```csharp
public class TweenSystem : ISystem
{
    private readonly QueryDescription _query = new QueryDescription().WithAll<TweenComponent, Position>();

    public void Update(World world, float dt)
    {
        world.Query(in _query, (Entity entity, ref TweenComponent tw, ref Position pos) =>
        {
            tw.Elapsed += dt;
            float raw = MathHelper.Clamp(tw.Elapsed / tw.Duration, 0f, 1f);
            float eased = tw.EaseFunc(raw);

            float value = tw.IsReversed
                ? MathHelper.Lerp(tw.To, tw.From, eased)
                : MathHelper.Lerp(tw.From, tw.To, eased);

            // Apply to target
            switch (tw.Target)
            {
                case TweenTarget.PositionX:
                    pos = pos with { X = value };
                    break;
                case TweenTarget.PositionY:
                    pos = pos with { Y = value };
                    break;
                case TweenTarget.Alpha:
                    if (entity.Has<SpriteColor>())
                    {
                        var sc = entity.Get<SpriteColor>();
                        entity.Set(sc with { A = (byte)(value * 255f) });
                    }
                    break;
                case TweenTarget.ScaleUniform:
                    if (entity.Has<Scale>())
                        entity.Set(new Scale(value, value));
                    break;
                case TweenTarget.Rotation:
                    if (entity.Has<Rotation>())
                        entity.Set(new Rotation(value));
                    break;
            }

            // Check completion
            if (tw.Elapsed >= tw.Duration)
            {
                if (tw.Loop != LoopMode.None && (tw.LoopCount == -1 || tw.LoopCount > 0))
                {
                    if (tw.LoopCount > 0)
                        tw = tw with { LoopCount = tw.LoopCount - 1 };

                    tw = tw with { Elapsed = 0f };

                    if (tw.Loop == LoopMode.PingPong)
                        tw = tw with { IsReversed = !tw.IsReversed };
                }
                else if (tw.RemoveOnComplete)
                {
                    entity.Remove<TweenComponent>();
                }
            }
        });
    }
}
```

### 7.4 — Helper to add tweens to entities

```csharp
public static class TweenEntityExtensions
{
    public static void AddTween(this Entity entity,
        float from, float to, float duration,
        TweenTarget target,
        EaseFunc? ease = null,
        LoopMode loop = LoopMode.None,
        int loopCount = 0,
        bool removeOnComplete = true)
    {
        entity.Add(new TweenComponent(
            From: from,
            To: to,
            Duration: duration,
            Elapsed: 0f,
            EaseFunc: ease ?? Ease.Linear,
            Target: target,
            Loop: loop,
            LoopCount: loopCount,
            IsReversed: false,
            RemoveOnComplete: removeOnComplete
        ));
    }
}

// Usage:
entity.AddTween(0f, 1f, 0.5f, TweenTarget.Alpha, Ease.SineOut);
entity.AddTween(entity.Get<Position>().Y, entity.Get<Position>().Y - 30f,
    0.8f, TweenTarget.PositionY, Ease.QuadOut);
```

---

## 8 — Comparison with Coroutines

| Aspect | Tweens | Coroutines |
|---|---|---|
| **Best for** | Simple A→B interpolation | Complex multi-step sequences |
| **State** | Minimal (from, to, elapsed) | Full execution frame (stack) |
| **Allocation** | Zero with pooling | Iterator allocation per start |
| **Composition** | Sequences/groups API | Natural `await`/`yield` flow |
| **Cancellation** | Explicit `Cancel()` call | Stop iteration |
| **Easing** | Built-in curve library | Manual `Lerp` + `yield` |
| **Debugging** | Inspect tween state | Step through coroutine |

### When to use tweens

- Moving something from point A to point B
- Fading in/out, scaling, rotating
- Any single-property animation with an easing curve
- Performance-critical paths (pooled, zero-alloc)

### When to use coroutines

- Complex enemy behavior (wait 2s, shoot 3 times, dash, repeat)
- Cutscene scripting with branching logic
- Anything that reads more naturally as sequential code
- Logic that needs conditionals mid-animation

### Hybrid: coroutine that fires tweens

```csharp
IEnumerator<float> BossIntro()
{
    // Tween boss position
    var t = _tweens.To(boss.Y, 100f, 1.0f, Ease.QuadOut);
    t.OnUpdate = y => boss.Y = y;

    // Wait for tween to complete
    while (t.State != TweenState.Complete)
        yield return 0f;

    // Flash warning text
    _tweens.To(0f, 1f, 0.3f, Ease.SineInOut)
        .SetLoop(LoopMode.PingPong, 3)
        .SetOnUpdate(a => _warningAlpha = a);

    // Wait fixed time
    float timer = 1.5f;
    while (timer > 0f) { timer -= Time.Delta; yield return 0f; }

    // Start fight
    boss.SetState(BossState.Active);
}
```

---

## 9 — Visual Reference: Easing Curves

Each curve below shows output (vertical) over normalized time (horizontal).

```
Linear                  QuadIn                  QuadOut
1 ┤          ╱           1 ┤          ╱           1 ┤       ╱───
  │        ╱               │        ╱               │     ╱
  │      ╱                 │      ╱                 │   ╱
  │    ╱                   │    ╱                   │  ╱
  │  ╱                     │  ·                     │╱
0 ┤╱─────────           0 ┤·────────            0 ┤──────────
  0         1              0         1              0         1

CubicIn                 CubicOut                SineInOut
1 ┤          ╱           1 ┤      ╱────          1 ┤        ╱──
  │         ╱              │    ╱                  │      ╱
  │        ╱               │  ╱                    │    ·
  │       ╱                │ ╱                     │  ╱
  │     ·                  │╱                      │╱
0 ┤··───────            0 ┤──────────           0 ┤──·───────
  0         1              0         1              0         1

BackIn                  BackOut                 ElasticOut
1 ┤          ╱           1 ┤   ╱─╲──             1 ┤    ╱─╲─╱──
  │         ╱              │  ╱                    │  ╱
  │        ╱               │ ╱                     │ ╱
  │      ╱                 │╱                      │╱
  │    ·                   │                       │
0 ┤──╲·────              0 ┤──────────           0 ┤──────────
  0         1              0         1              0         1

BounceOut               ExpoIn                  CircOut
1 ┤   ╱╲╱╲╱╱─           1 ┤          ╱           1 ┤    ╱─────
  │  ╱                     │         ╱              │  ╱
  │ ╱                      │        ╱               │ ╱
  │╱                       │       ╱                │╱
  │                        │     ·                  │
0 ┤──────────            0 ┤····────             0 ┤──────────
  0         1              0         1              0         1
```

**Curve family cheat sheet:**

| Family | In | Out | InOut |
|---|---|---|---|
| **Quad** | Slow start, accelerating | Fast start, decelerating | Slow-fast-slow |
| **Cubic** | Steeper acceleration | Steeper deceleration | More pronounced S-curve |
| **Quart/Quint** | Even steeper | Even steeper | Sharper S |
| **Sine** | Gentle, natural | Gentle, natural | Smooth S |
| **Expo** | Near-zero then explosive | Explosive then near-stop | Sharp center |
| **Circ** | Quarter-circle curve | Quarter-circle curve | Half-circle S |
| **Back** | Pulls back before going | Overshoots then settles | Both overshoot |
| **Elastic** | Spring wind-up | Spring overshoot oscillation | Both oscillate |
| **Bounce** | Bouncing lead-in | Bouncing landing | Both bounce |

---

## Quick Reference

```csharp
// ── One-liner tweens ──
_tweens.To(0, 100, 0.5f, Ease.QuadOut).SetOnUpdate(v => x = v);
_tweens.To(1, 0, 0.3f, Ease.SineIn).SetOnUpdate(v => alpha = v);
_tweens.To(1, 1.2f, 0.1f, Ease.BackOut).SetLoop(LoopMode.PingPong, 1).SetOnUpdate(v => scale = v);

// ── ECS tween ──
entity.AddTween(0, 1, 0.5f, TweenTarget.Alpha, Ease.SineOut);

// ── Sequence ──
var seq = new TweenSequence();
seq.Append(moveRight).Append(fadeOut);
seq.Start();

// ── Group (parallel) ──
var grp = new TweenGroup();
grp.Add(scaleUp).Add(fadeIn);
```

---

*Tweens are the workhorse of game feel. Almost every piece of juice — UI animations, combat feedback, camera effects — is a tween under the hood. Start with `Ease.QuadOut` for most things (it feels natural), then experiment with `BackOut` for bouncy UI and `ElasticOut` for springy effects.*
