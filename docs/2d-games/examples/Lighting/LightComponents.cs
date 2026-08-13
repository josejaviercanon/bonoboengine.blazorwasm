// ============================================================================
// LightComponents.cs — Light ECS Components
// Extracted from: G39 — 2D Lighting & Shadows
// Part of: Universal 2D Engine Toolkit Examples
// ============================================================================

using Arch.Core;
using Microsoft.Xna.Framework;

namespace U2DToolkit.Examples.Lighting;

/// <summary>
/// Light falloff modes controlling how intensity decreases with distance.
/// </summary>
public enum LightFalloff
{
    /// <summary>Intensity = 1 - d. Hard edges, unrealistic.</summary>
    Linear,

    /// <summary>Intensity = (1 - d)². Natural, good default.</summary>
    Quadratic,

    /// <summary>Intensity = smoothstep(1, 0, d). Very soft edges, cinematic.</summary>
    Smooth,

    /// <summary>Intensity = 1 / (1 + k·d²). Physically-based, never reaches zero.</summary>
    Inverse
}

/// <summary>
/// Point light ECS component. Emits radially from the entity's position
/// with configurable radius, color, intensity, and falloff curve.
/// <para>
/// Rendered as an additive-blended radial gradient texture onto the lightmap.
/// </para>
/// </summary>
/// <param name="Radius">Light radius in world-space pixels.</param>
/// <param name="Color">Light color tint.</param>
/// <param name="Intensity">Intensity multiplier (0–1+ range, can exceed 1 for bloom).</param>
/// <param name="Falloff">How intensity decreases with distance from center.</param>
/// <param name="CastsShadows">Whether this light computes a visibility polygon for shadows.</param>
public record struct PointLight(
    float Radius,
    Color Color,
    float Intensity,
    LightFalloff Falloff,
    bool CastsShadows
);

/// <summary>
/// Spot light ECS component. Emits in a cone from the entity's position
/// along a direction. Has inner (full intensity) and outer (falloff to zero) angles.
/// <para>
/// Requires an HLSL shader for cone clipping. See LightComposite.fx
/// for the spot light pass.
/// </para>
/// </summary>
/// <param name="Radius">Maximum reach of the spot light in pixels.</param>
/// <param name="Color">Light color tint.</param>
/// <param name="Intensity">Intensity multiplier.</param>
/// <param name="DirectionRadians">Direction the spotlight points, in radians.</param>
/// <param name="InnerAngleDeg">Half-angle of full-intensity cone, in degrees.</param>
/// <param name="OuterAngleDeg">Half-angle of outer falloff cone, in degrees.</param>
/// <param name="Falloff">Radial falloff curve.</param>
/// <param name="CastsShadows">Whether this light computes shadow geometry.</param>
public record struct SpotLight(
    float Radius,
    Color Color,
    float Intensity,
    float DirectionRadians,
    float InnerAngleDeg,
    float OuterAngleDeg,
    LightFalloff Falloff,
    bool CastsShadows
);

/// <summary>
/// Global ambient light component. Attach to a singleton entity.
/// Sets the base illumination for the entire scene — the lightmap
/// is cleared to this color before any lights are drawn.
/// <para>
/// Dark blue (~10, 10, 15) for dungeons, warm white for daylight.
/// Integrate with a day/night cycle via <see cref="AmbientLighting"/>.
/// </para>
/// </summary>
/// <param name="Color">Base ambient color.</param>
/// <param name="Intensity">Intensity multiplier applied to the color.</param>
public record struct AmbientLight(
    Color Color,
    float Intensity
);

/// <summary>
/// Flicker effect for torches, candles, campfires, etc.
/// Uses a multi-sine approximation of noise to modulate intensity and radius.
/// <para>
/// Formula: <c>0.7 + sin(t * speed) * 0.15 + sin(t * speed * 1.7) * 0.1</c>
/// </para>
/// </summary>
/// <param name="Speed">Flicker speed multiplier. 3–5 for torches.</param>
/// <param name="Strength">Flicker amplitude. 0.15–0.25 for subtle flickering.</param>
/// <param name="Seed">Per-light seed to desynchronize multiple lights.</param>
/// <param name="BaseIntensity">Resting intensity when not flickering.</param>
/// <param name="BaseRadius">Resting radius when not flickering.</param>
public record struct LightFlickerEffect(
    float Speed,
    float Strength,
    int Seed,
    float BaseIntensity,
    float BaseRadius
);

/// <summary>
/// Marks an entity as a shadow occluder with edge geometry.
/// Edges define the boundaries of opaque objects (walls, pillars)
/// that block light and cast shadows.
/// </summary>
/// <param name="Edges">Array of line segments defining the occluder boundary.</param>
public record struct Occluder(Edge[] Edges);

/// <summary>
/// A line segment used for shadow casting.
/// </summary>
/// <param name="A">Start point of the edge.</param>
/// <param name="B">End point of the edge.</param>
public readonly record struct Edge(Vector2 A, Vector2 B);

/// <summary>
/// Standard position component (shared with other systems).
/// </summary>
public record struct LightPosition(Vector2 Value);

/// <summary>
/// Static utility for computing ambient color across a 24-hour day/night cycle.
/// Interpolates between key colors using smoothstep for natural transitions.
/// </summary>
public static class AmbientLighting
{
    /// <summary>Key ambient colors across the 24-hour day.</summary>
    private static readonly (float hour, Color color)[] DayCurve =
    {
        (0f,  new Color(15, 15, 30)),      // Midnight — deep blue
        (5f,  new Color(25, 25, 50)),      // Pre-dawn
        (6f,  new Color(80, 60, 50)),      // Dawn — warm orange tint
        (8f,  new Color(200, 200, 210)),   // Morning
        (12f, new Color(255, 255, 255)),   // Noon — full brightness
        (17f, new Color(230, 200, 170)),   // Late afternoon — golden
        (19f, new Color(100, 50, 50)),     // Sunset — deep orange/red
        (21f, new Color(30, 30, 60)),      // Dusk
        (24f, new Color(15, 15, 30)),      // Midnight again
    };

    /// <summary>
    /// Returns the ambient color for a given hour of day (0–24 float).
    /// Uses smoothstep interpolation between key colors.
    /// </summary>
    /// <param name="hourOfDay">Hour as a float, e.g., 14.5 = 2:30 PM.</param>
    /// <returns>Interpolated ambient color.</returns>
    public static Color GetAmbientColor(float hourOfDay)
    {
        hourOfDay %= 24f;

        for (int i = 0; i < DayCurve.Length - 1; i++)
        {
            var (h0, c0) = DayCurve[i];
            var (h1, c1) = DayCurve[i + 1];

            if (hourOfDay >= h0 && hourOfDay <= h1)
            {
                float t = (hourOfDay - h0) / (h1 - h0);
                // Smoothstep for natural transitions
                t = t * t * (3f - 2f * t);
                return Color.Lerp(c0, c1, t);
            }
        }

        return DayCurve[0].color;
    }
}

/// <summary>
/// Calculates light falloff based on normalized distance and falloff mode.
/// </summary>
public static class LightFalloffHelper
{
    /// <summary>
    /// Calculate the falloff attenuation for a given normalized distance.
    /// </summary>
    /// <param name="normalizedDist">Distance from light center, normalized to [0, 1] by radius.</param>
    /// <param name="falloff">The falloff curve to use.</param>
    /// <returns>Attenuation value in [0, 1].</returns>
    public static float Calculate(float normalizedDist, LightFalloff falloff)
    {
        float d = MathHelper.Clamp(normalizedDist, 0f, 1f);
        return falloff switch
        {
            LightFalloff.Linear    => 1f - d,
            LightFalloff.Quadratic => (1f - d) * (1f - d),
            LightFalloff.Smooth    => d * d * (3f - 2f * d),
            LightFalloff.Inverse   => 1f / (1f + 25f * d * d),
            _                      => 1f - d
        };
    }
}

/// <summary>
/// Static utility for generating light flicker values.
/// Uses a layered sine approximation of noise for natural-looking
/// torch/candle flicker without requiring a noise texture.
/// </summary>
public static class LightFlicker
{
    /// <summary>
    /// Returns a flicker multiplier centered around 1.0.
    /// Range: approximately [1 - strength, 1 + strength].
    /// </summary>
    /// <param name="time">Total elapsed game time in seconds.</param>
    /// <param name="speed">Flicker speed. 3–5 for torches.</param>
    /// <param name="strength">Flicker amplitude. 0.15 = subtle.</param>
    /// <param name="seed">Per-light seed for desynchronization.</param>
    /// <returns>Multiplier to apply to base intensity.</returns>
    public static float GetFlicker(float time, float speed, float strength, int seed)
    {
        float t = time * speed + seed * 73.7f;
        float noise =
            MathF.Sin(t * 1.0f) * 0.5f +
            MathF.Sin(t * 2.3f) * 0.3f +
            MathF.Sin(t * 5.7f) * 0.2f;

        return 1f + noise * strength;
    }
}
