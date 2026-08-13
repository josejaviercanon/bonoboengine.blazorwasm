// ============================================================================
// LightRenderer.cs — Lightmap Rendering System
// Extracted from: G39 — 2D Lighting & Shadows
// Part of: Universal 2D Engine Toolkit Examples
// ============================================================================

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace U2DToolkit.Examples.Lighting;

/// <summary>
/// Manages the lightmap render pipeline. Handles render target creation,
/// scene/lightmap pass coordination, and final compositing with multiply blend.
/// <para>
/// Pipeline each frame:
/// <list type="number">
///   <item><see cref="BeginScene"/> — draw all sprites/tiles at full brightness</item>
///   <item><see cref="BeginLightmap"/> — clear to ambient, draw lights (additive)</item>
///   <item><see cref="EndAndComposite"/> — multiply lightmap over scene</item>
/// </list>
/// </para>
/// </summary>
public class LightingManager
{
    private RenderTarget2D _lightmap = null!;
    private RenderTarget2D _sceneTarget = null!;
    private readonly GraphicsDevice _graphics;

    /// <summary>
    /// Ambient color — the baseline illumination for unlit areas.
    /// The lightmap is cleared to this color. Lights add on top.
    /// </summary>
    public Color AmbientColor { get; set; } = new Color(30, 30, 50); // dark blue night

    /// <summary>Creates a new LightingManager and initializes render targets.</summary>
    public LightingManager(GraphicsDevice graphics)
    {
        _graphics = graphics;
        CreateTargets();
    }

    /// <summary>
    /// (Re)creates render targets at the current back buffer resolution.
    /// Call on window resize.
    /// </summary>
    /// <param name="lightmapScale">
    /// Scale factor for lightmap resolution (0.5 = half res).
    /// Lower values improve performance — lights are soft, so quality loss is minimal.
    /// </param>
    public void CreateTargets(float lightmapScale = 1.0f)
    {
        int w = _graphics.PresentationParameters.BackBufferWidth;
        int h = _graphics.PresentationParameters.BackBufferHeight;

        _lightmap?.Dispose();
        _sceneTarget?.Dispose();

        _sceneTarget = new RenderTarget2D(_graphics, w, h);
        _lightmap = new RenderTarget2D(_graphics,
            (int)(w * lightmapScale),
            (int)(h * lightmapScale));
    }

    /// <summary>
    /// Begin rendering the scene at full brightness.
    /// All subsequent SpriteBatch draw calls go to the scene render target.
    /// </summary>
    public void BeginScene()
    {
        _graphics.SetRenderTarget(_sceneTarget);
        _graphics.Clear(Color.Transparent);
    }

    /// <summary>
    /// Begin rendering the lightmap. Clears to <see cref="AmbientColor"/>.
    /// Draw lights using <see cref="BlendState.Additive"/> after this call.
    /// </summary>
    public void BeginLightmap()
    {
        _graphics.SetRenderTarget(_lightmap);
        _graphics.Clear(AmbientColor);
    }

    /// <summary>
    /// Finalize rendering: compose the scene with the lightmap using
    /// multiply blending. The result goes to the back buffer.
    /// </summary>
    public void EndAndComposite(SpriteBatch spriteBatch)
    {
        _graphics.SetRenderTarget(null);
        _graphics.Clear(Color.Black);

        // Draw scene at full brightness
        spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);
        spriteBatch.Draw(_sceneTarget, Vector2.Zero, Color.White);
        spriteBatch.End();

        // Multiply lightmap on top: Dest = Dest × Source
        spriteBatch.Begin(SpriteSortMode.Deferred, MultiplyBlend);
        spriteBatch.Draw(_lightmap, Vector2.Zero, Color.White);
        spriteBatch.End();
    }

    /// <summary>
    /// Multiply blend state: final = scene_color × lightmap_color.
    /// Black in the lightmap = completely dark, white = fully lit.
    /// </summary>
    private static readonly BlendState MultiplyBlend = new()
    {
        ColorBlendFunction    = BlendFunction.Add,
        ColorSourceBlend      = Blend.DestinationColor,
        ColorDestinationBlend = Blend.Zero,
        AlphaBlendFunction    = BlendFunction.Add,
        AlphaSourceBlend      = Blend.DestinationAlpha,
        AlphaDestinationBlend = Blend.Zero,
    };
}

/// <summary>
/// Static helpers for drawing individual lights onto the lightmap.
/// </summary>
public static class LightDrawing
{
    /// <summary>
    /// Generates a radial gradient texture at runtime for use as the
    /// light sprite. A single 128×128 texture serves all point lights.
    /// Uses quadratic falloff for a natural look.
    /// </summary>
    /// <param name="graphics">The graphics device.</param>
    /// <param name="size">Texture dimensions (square). 128 or 256 recommended.</param>
    /// <returns>A radial gradient texture (white center fading to transparent).</returns>
    public static Texture2D CreateRadialGradient(GraphicsDevice graphics, int size = 128)
    {
        var texture = new Texture2D(graphics, size, size);
        var data = new Color[size * size];
        float center = size / 2f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - center) / center;
                float dy = (y - center) / center;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                float alpha = MathHelper.Clamp(1f - dist, 0f, 1f);

                // Quadratic falloff for natural look
                alpha *= alpha;

                data[y * size + x] = new Color(alpha, alpha, alpha, alpha);
            }
        }

        texture.SetData(data);
        return texture;
    }

    /// <summary>
    /// Draws a point light onto the lightmap as a scaled, tinted radial gradient.
    /// Call within a <see cref="BlendState.Additive"/> SpriteBatch pass.
    /// </summary>
    /// <param name="spriteBatch">Active SpriteBatch in additive mode.</param>
    /// <param name="gradientTexture">Radial gradient texture from <see cref="CreateRadialGradient"/>.</param>
    /// <param name="worldPosition">Light position in world space.</param>
    /// <param name="radius">Light radius in world-space pixels.</param>
    /// <param name="color">Light color tint.</param>
    /// <param name="intensity">Intensity multiplier (0–1+).</param>
    /// <param name="cameraTransform">Camera view/projection matrix.</param>
    public static void DrawPointLight(
        SpriteBatch spriteBatch,
        Texture2D gradientTexture,
        Vector2 worldPosition,
        float radius,
        Color color,
        float intensity,
        Matrix cameraTransform)
    {
        // Transform world position to screen space
        Vector2 screenPos = Vector2.Transform(worldPosition, cameraTransform);

        // Extract camera zoom from the transform matrix
        float zoom = MathF.Sqrt(
            cameraTransform.M11 * cameraTransform.M11 +
            cameraTransform.M12 * cameraTransform.M12);
        float screenRadius = radius * zoom;

        var destRect = new Rectangle(
            (int)(screenPos.X - screenRadius),
            (int)(screenPos.Y - screenRadius),
            (int)(screenRadius * 2),
            (int)(screenRadius * 2));

        Color lightColor = new Color(
            (byte)(color.R * intensity),
            (byte)(color.G * intensity),
            (byte)(color.B * intensity),
            (byte)(255 * intensity));

        spriteBatch.Draw(gradientTexture, destRect, lightColor);
    }

    /// <summary>
    /// Quick visibility check: is a light's bounding circle on screen?
    /// Use to cull off-screen lights before drawing.
    /// </summary>
    public static bool IsLightVisible(Vector2 lightPos, float radius, Rectangle cameraBounds)
    {
        return lightPos.X + radius > cameraBounds.Left &&
               lightPos.X - radius < cameraBounds.Right &&
               lightPos.Y + radius > cameraBounds.Top &&
               lightPos.Y - radius < cameraBounds.Bottom;
    }
}

/// <summary>
/// Extracts occluder boundary edges from a tilemap for shadow casting.
/// Only boundary edges (solid adjacent to empty) are included.
/// </summary>
public static class OccluderExtractor
{
    /// <summary>
    /// Extract boundary edges from a 2D boolean solid map.
    /// Each solid tile adjacent to an empty tile or map boundary
    /// contributes its exposed edges.
    /// </summary>
    /// <param name="solidMap">2D boolean grid: true = solid, false = empty. Indexed [x, y].</param>
    /// <param name="tileSize">Size of each tile in pixels (assumes square tiles).</param>
    /// <returns>List of line segments defining occluder boundaries.</returns>
    public static List<Edge> ExtractOccluderEdges(bool[,] solidMap, int tileSize)
    {
        int width = solidMap.GetLength(0);
        int height = solidMap.GetLength(1);
        var edges = new List<Edge>();

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!solidMap[x, y]) continue;

                float px = x * tileSize;
                float py = y * tileSize;
                float s = tileSize;

                // Top edge: exposed if tile above is empty or out of bounds
                if (y == 0 || !solidMap[x, y - 1])
                    edges.Add(new Edge(new(px, py), new(px + s, py)));

                // Bottom edge
                if (y == height - 1 || !solidMap[x, y + 1])
                    edges.Add(new Edge(new(px + s, py + s), new(px, py + s)));

                // Left edge
                if (x == 0 || !solidMap[x - 1, y])
                    edges.Add(new Edge(new(px, py + s), new(px, py)));

                // Right edge
                if (x == width - 1 || !solidMap[x + 1, y])
                    edges.Add(new Edge(new(px + s, py), new(px + s, py + s)));
            }
        }

        return edges;
    }
}

/// <summary>
/// Computes a visibility polygon from a light's position using ray casting
/// against occluder edges. The resulting polygon defines the lit area.
/// </summary>
public static class VisibilityPolygon
{
    /// <summary>
    /// Compute the visibility polygon for a point light.
    /// Casts rays to all occluder endpoints (with ±epsilon offsets to see
    /// around corners), sorts by angle, and builds the polygon from
    /// closest intersections.
    /// </summary>
    /// <param name="lightPos">World-space position of the light.</param>
    /// <param name="radius">Light radius — rays are clamped to this distance.</param>
    /// <param name="edges">All occluder edges to test against.</param>
    /// <returns>Ordered list of polygon vertices forming the visibility region.</returns>
    public static List<Vector2> Compute(Vector2 lightPos, float radius, List<Edge> edges)
    {
        // Filter edges within range
        var relevant = new List<Edge>();
        float r2 = radius * radius;
        foreach (var e in edges)
        {
            if (Vector2.DistanceSquared(lightPos, e.A) < r2 * 4 ||
                Vector2.DistanceSquared(lightPos, e.B) < r2 * 4)
            {
                relevant.Add(e);
            }
        }

        // Collect unique angles to cast rays toward
        var angles = new List<float>();
        foreach (var e in relevant)
        {
            foreach (var pt in new[] { e.A, e.B })
            {
                float angle = MathF.Atan2(pt.Y - lightPos.Y, pt.X - lightPos.X);
                angles.Add(angle);
                angles.Add(angle - 0.0001f); // nudge to see around corners
                angles.Add(angle + 0.0001f);
            }
        }

        // Add boundary rays
        for (int i = 0; i < 4; i++)
            angles.Add(i * MathF.PI / 2f);

        angles.Sort();

        // Cast each ray and find closest intersection
        var points = new List<Vector2>(angles.Count);
        foreach (float angle in angles)
        {
            var dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var rayEnd = lightPos + dir * radius;

            Vector2 closest = rayEnd;
            float closestDist = radius;

            foreach (var edge in relevant)
            {
                if (RaySegmentIntersect(lightPos, rayEnd, edge.A, edge.B,
                    out Vector2 hit, out float dist))
                {
                    if (dist < closestDist)
                    {
                        closestDist = dist;
                        closest = hit;
                    }
                }
            }

            points.Add(closest);
        }

        return points;
    }

    private static bool RaySegmentIntersect(
        Vector2 rayOrigin, Vector2 rayEnd,
        Vector2 segA, Vector2 segB,
        out Vector2 intersection, out float distance)
    {
        intersection = Vector2.Zero;
        distance = float.MaxValue;

        var r = rayEnd - rayOrigin;
        var s = segB - segA;
        float rxs = Cross(r, s);

        if (MathF.Abs(rxs) < 1e-8f) return false;

        var qp = segA - rayOrigin;
        float t = Cross(qp, s) / rxs;
        float u = Cross(qp, r) / rxs;

        if (t >= 0f && t <= 1f && u >= 0f && u <= 1f)
        {
            intersection = rayOrigin + t * r;
            distance = t * r.Length();
            return true;
        }

        return false;
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
}
