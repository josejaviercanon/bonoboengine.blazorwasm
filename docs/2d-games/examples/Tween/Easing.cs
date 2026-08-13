// ============================================================================
// Easing.cs — Complete Easing Function Library
// Extracted from: G41 — Tweening & Easing
// Part of: Universal 2D Engine Toolkit Examples
//
// Every function takes float t in [0, 1] and returns a mapped float.
// All functions are pure, stateless, and zero-allocation.
// ============================================================================

namespace U2DToolkit.Examples.Tween;

/// <summary>
/// Easing function delegate: maps normalized time t ∈ [0,1] to an output value.
/// </summary>
public delegate float EaseFunc(float t);

/// <summary>
/// Complete library of easing functions covering all standard families:
/// Linear, Quad, Cubic, Quart, Quint, Sine, Expo, Circ, Elastic, Back, Bounce.
/// Each family has In (accelerating), Out (decelerating), and InOut (S-curve) variants.
/// <para>
/// Quick guide:
/// <list type="bullet">
///   <item><b>QuadOut</b> — Natural feel, good default for most animations</item>
///   <item><b>BackOut</b> — Bouncy overshoot, great for UI elements popping in</item>
///   <item><b>ElasticOut</b> — Springy oscillation, good for juicy effects</item>
///   <item><b>SineInOut</b> — Smooth S-curve, great for camera movements</item>
///   <item><b>ExpoOut</b> — Sharp deceleration, good for screen shake decay</item>
/// </list>
/// </para>
/// </summary>
public static class Ease
{
    // ── Linear ──────────────────────────────────────────────────────────
    
    /// <summary>No easing — constant speed.</summary>
    public static float Linear(float t) => t;

    // ── Quadratic ───────────────────────────────────────────────────────

    /// <summary>Slow start, accelerating. t²</summary>
    public static float QuadIn(float t) => t * t;
    
    /// <summary>Fast start, decelerating. Good default for most things.</summary>
    public static float QuadOut(float t) => t * (2f - t);
    
    /// <summary>Slow-fast-slow S-curve.</summary>
    public static float QuadInOut(float t) =>
        t < 0.5f ? 2f * t * t : -1f + (4f - 2f * t) * t;

    // ── Cubic ───────────────────────────────────────────────────────────

    /// <summary>Steeper acceleration than Quad.</summary>
    public static float CubicIn(float t) => t * t * t;
    
    /// <summary>Steeper deceleration than Quad.</summary>
    public static float CubicOut(float t) { float u = t - 1f; return u * u * u + 1f; }
    
    /// <summary>More pronounced S-curve than Quad.</summary>
    public static float CubicInOut(float t) =>
        t < 0.5f ? 4f * t * t * t : 1f + (t - 1f) * (2f * t - 2f) * (2f * t - 2f);

    // ── Quartic ─────────────────────────────────────────────────────────

    /// <summary>Even steeper acceleration.</summary>
    public static float QuartIn(float t) => t * t * t * t;
    
    /// <summary>Even steeper deceleration.</summary>
    public static float QuartOut(float t) { float u = t - 1f; return 1f - u * u * u * u; }
    
    /// <summary>Sharper S-curve.</summary>
    public static float QuartInOut(float t) =>
        t < 0.5f
            ? 8f * t * t * t * t
            : 1f - 8f * (t - 1f) * (t - 1f) * (t - 1f) * (t - 1f);

    // ── Quintic ─────────────────────────────────────────────────────────

    /// <summary>Very steep acceleration.</summary>
    public static float QuintIn(float t) => t * t * t * t * t;
    
    /// <summary>Very steep deceleration.</summary>
    public static float QuintOut(float t) { float u = t - 1f; return 1f + u * u * u * u * u; }
    
    /// <summary>Very sharp S-curve.</summary>
    public static float QuintInOut(float t) =>
        t < 0.5f
            ? 16f * t * t * t * t * t
            : 1f + 16f * (t - 1f) * (t - 1f) * (t - 1f) * (t - 1f) * (t - 1f);

    // ── Sine ────────────────────────────────────────────────────────────

    /// <summary>Gentle, natural acceleration using a sine curve.</summary>
    public static float SineIn(float t) => 1f - MathF.Cos(t * MathF.PI * 0.5f);
    
    /// <summary>Gentle, natural deceleration.</summary>
    public static float SineOut(float t) => MathF.Sin(t * MathF.PI * 0.5f);
    
    /// <summary>Smooth S-curve. Great for camera movements.</summary>
    public static float SineInOut(float t) => 0.5f * (1f - MathF.Cos(MathF.PI * t));

    // ── Exponential ─────────────────────────────────────────────────────

    /// <summary>Near-zero then explosive. Almost no motion until the end.</summary>
    public static float ExpoIn(float t) =>
        t == 0f ? 0f : MathF.Pow(2f, 10f * (t - 1f));
    
    /// <summary>Explosive start then near-stop. Good for screen shake decay.</summary>
    public static float ExpoOut(float t) =>
        t == 1f ? 1f : 1f - MathF.Pow(2f, -10f * t);
    
    /// <summary>Sharp center transition.</summary>
    public static float ExpoInOut(float t)
    {
        if (t == 0f) return 0f;
        if (t == 1f) return 1f;
        return t < 0.5f
            ? 0.5f * MathF.Pow(2f, 20f * t - 10f)
            : 1f - 0.5f * MathF.Pow(2f, -20f * t + 10f);
    }

    // ── Circular ────────────────────────────────────────────────────────

    /// <summary>Quarter-circle curve — slow then fast.</summary>
    public static float CircIn(float t) => 1f - MathF.Sqrt(1f - t * t);
    
    /// <summary>Quarter-circle curve — fast then slow.</summary>
    public static float CircOut(float t) => MathF.Sqrt(1f - (t - 1f) * (t - 1f));
    
    /// <summary>Half-circle S-curve.</summary>
    public static float CircInOut(float t) =>
        t < 0.5f
            ? 0.5f * (1f - MathF.Sqrt(1f - 4f * t * t))
            : 0.5f * (MathF.Sqrt(1f - (2f * t - 2f) * (2f * t - 2f)) + 1f);

    // ── Elastic ─────────────────────────────────────────────────────────

    private const float ElasticP = 0.3f;

    /// <summary>Spring wind-up. Oscillates before reaching target.</summary>
    public static float ElasticIn(float t) =>
        t is 0f or 1f ? t
        : -MathF.Pow(2f, 10f * t - 10f) *
          MathF.Sin((t * 10f - 10.75f) * (2f * MathF.PI / ElasticP));
    
    /// <summary>Spring overshoot oscillation. Great for juicy UI and effects.</summary>
    public static float ElasticOut(float t) =>
        t is 0f or 1f ? t
        : MathF.Pow(2f, -10f * t) *
          MathF.Sin((t * 10f - 0.75f) * (2f * MathF.PI / ElasticP)) + 1f;
    
    /// <summary>Both oscillate — spring on both ends.</summary>
    public static float ElasticInOut(float t)
    {
        if (t is 0f or 1f) return t;
        const float p = ElasticP * 1.5f;
        return t < 0.5f
            ? -0.5f * MathF.Pow(2f, 20f * t - 10f) *
              MathF.Sin((20f * t - 11.125f) * (2f * MathF.PI / p))
            :  0.5f * MathF.Pow(2f, -20f * t + 10f) *
              MathF.Sin((20f * t - 11.125f) * (2f * MathF.PI / p)) + 1f;
    }

    // ── Back ────────────────────────────────────────────────────────────

    private const float S = 1.70158f;

    /// <summary>Pulls back before going forward. Like winding up.</summary>
    public static float BackIn(float t) => t * t * ((S + 1f) * t - S);
    
    /// <summary>Overshoots then settles. Great for UI popups.</summary>
    public static float BackOut(float t)
    {
        float u = t - 1f;
        return u * u * ((S + 1f) * u + S) + 1f;
    }
    
    /// <summary>Wind-up + overshoot on both ends.</summary>
    public static float BackInOut(float t)
    {
        const float s2 = S * 1.525f;
        float u = t * 2f;
        return u < 1f
            ? 0.5f * (u * u * ((s2 + 1f) * u - s2))
            : 0.5f * ((u -= 2f) * u * ((s2 + 1f) * u + s2) + 2f);
    }

    // ── Bounce ──────────────────────────────────────────────────────────

    /// <summary>Bouncing landing — like a ball hitting the floor.</summary>
    public static float BounceOut(float t)
    {
        const float n = 7.5625f;
        const float d = 2.75f;
        if (t < 1f / d)   return n * t * t;
        if (t < 2f / d)   return n * (t -= 1.5f / d) * t + 0.75f;
        if (t < 2.5f / d) return n * (t -= 2.25f / d) * t + 0.9375f;
        return n * (t -= 2.625f / d) * t + 0.984375f;
    }
    
    /// <summary>Bouncing lead-in.</summary>
    public static float BounceIn(float t) => 1f - BounceOut(1f - t);
    
    /// <summary>Bounce on both ends.</summary>
    public static float BounceInOut(float t) =>
        t < 0.5f ? 0.5f * BounceIn(t * 2f) : 0.5f * BounceOut(t * 2f - 1f) + 0.5f;
}
