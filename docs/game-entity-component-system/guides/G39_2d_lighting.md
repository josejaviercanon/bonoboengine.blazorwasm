# G39 — 2D Lighting & Shadows


> **Category:** Guide · **Related:** [G2 Rendering & Graphics](./G2_rendering_and_graphics.md) · [G27 Shaders & Visual Effects](./G27_shaders_and_effects.md) · [G23 Particles](./G23_particles.md) · [G22 Parallax & Depth Layers](./G22_parallax_depth_layers.md)

---

Complete 2D lighting and shadow system built on MonoGame + Arch ECS. Covers the lightmap compositing approach, point/spot/ambient lights, ray-cast shadow volumes, normal-map lighting, light cookies, and performance strategies — with full HLSL shaders and production C# code.

> **Recommended starting point: Simple Lightmap.** The full system below covers shadow casting, normal maps, and advanced techniques. But for most 2D games — especially top-down — a simple lightmap with radial gradient textures, additive light rendering, and multiply compositing is all you need. This approach powers FireStarter's entire lighting system in ~230 lines.
>
> **Quick-start recipe (covers 90% of use cases):**
>
> 1. Generate a **128×128 radial gradient texture** with smoothstep falloff — one texture serves all lights
> 2. Each frame, render lights to a **lightmap RT** cleared to ambient color (~3% gray for night)
> 3. Draw lights as **additive-blended** sprites (white circle, scaled by radius, tinted by intensity)
> 4. Composite the lightmap over the scene with **multiply blending**:
>    ```csharp
>    private static readonly BlendState MultiplyBlend = new()
>    {
>        ColorBlendFunction = BlendFunction.Add,
>        ColorSourceBlend = Blend.DestinationColor,
>        ColorDestinationBlend = Blend.Zero,
>        AlphaBlendFunction = BlendFunction.Add,
>        AlphaSourceBlend = Blend.DestinationAlpha,
>        AlphaDestinationBlend = Blend.Zero
>    };
>    ```
> 5. For fire/torch flicker, modulate intensity with a multi-sine formula:
>    `0.7 + sin(t*8)*0.15 + sin(t*13)*0.1`
> 6. For particle lights, query tagged particles from the particle pool and draw small additive lights at each position
>
> Shadow casting and normal maps (covered below) are advanced techniques — add them only when the simple lightmap doesn't give enough visual depth.

---

## Table of Contents

1. [The Lightmap Approach](#the-lightmap-approach)
2. [Point Lights](#point-lights)
3. [Spot Lights](#spot-lights)
4. [Ambient Light](#ambient-light)
5. [2D Shadow Casting](#2d-shadow-casting)
6. [Normal Map Lighting](#normal-map-lighting)
7. [Light Masking / Light Cookies](#light-masking--light-cookies)
8. [Performance](#performance)
9. [ECS Integration](#ecs-integration)
10. [Practical Examples](#practical-examples)

---

## The Lightmap Approach

The standard technique for 2D lighting: render all lights into a separate `RenderTarget2D` (the **lightmap**), then multiply it over the final scene. Dark areas stay dark, lit areas show through.

### How It Works

```
Scene (full brightness)  ×  Lightmap (black = dark, white = full light)  =  Final output
```

1. **Draw your scene** to a render target (or the back buffer) at full brightness — as if every pixel were fully lit.
2. **Switch** to the lightmap render target. Clear it to the **ambient color** (e.g., dark blue for nighttime, white for full daylight).
3. **Draw each light** as an additive-blended sprite/shape onto the lightmap.
4. **Composite**: draw the scene, then multiply the lightmap on top using `BlendState` or a shader.

### Lightmap Render Target Setup

```csharp
public class LightingManager
{
    private RenderTarget2D _lightmap;
    private RenderTarget2D _sceneTarget;
    private readonly GraphicsDevice _graphics;

    public Color AmbientColor { get; set; } = new Color(30, 30, 50); // dark blue night

    public LightingManager(GraphicsDevice graphics)
    {
        _graphics = graphics;
        CreateTargets();
    }

    public void CreateTargets()
    {
        int w = _graphics.PresentationParameters.BackBufferWidth;
        int h = _graphics.PresentationParameters.BackBufferHeight;

        _lightmap?.Dispose();
        _sceneTarget?.Dispose();

        _sceneTarget = new RenderTarget2D(_graphics, w, h);
        _lightmap = new RenderTarget2D(_graphics, w, h);
    }

    public void BeginScene()
    {
        _graphics.SetRenderTarget(_sceneTarget);
        _graphics.Clear(Color.Transparent);
    }

    public void BeginLightmap()
    {
        _graphics.SetRenderTarget(_lightmap);
        _graphics.Clear(AmbientColor);
    }

    public void EndAndComposite(SpriteBatch spriteBatch)
    {
        _graphics.SetRenderTarget(null);
        _graphics.Clear(Color.Black);

        // Draw scene
        spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);
        spriteBatch.Draw(_sceneTarget, Vector2.Zero, Color.White);
        spriteBatch.End();

        // Multiply lightmap on top
        spriteBatch.Begin(SpriteSortMode.Deferred, MultiplyBlend);
        spriteBatch.Draw(_lightmap, Vector2.Zero, Color.White);
        spriteBatch.End();
    }

    /// <summary>Multiply blend: Dest = Dest × Source</summary>
    private static readonly BlendState MultiplyBlend = new()
    {
        ColorBlendFunction = BlendFunction.Add,
        ColorSourceBlend = Blend.DestinationColor,
        ColorDestinationBlend = Blend.Zero,
        AlphaBlendFunction = BlendFunction.Add,
        AlphaSourceBlend = Blend.DestinationAlpha,
        AlphaDestinationBlend = Blend.Zero,
    };
}
```

> **Key insight:** The lightmap is cleared to the ambient color, not black. This means unlit areas still receive ambient light. Lights are drawn additively *on top* of that ambient base.

### Lightmap Compositing Shader (Alternative)

If you need more control (gamma correction, HDR tone-mapping), use a shader instead of `BlendState`:

```hlsl
#if OPENGL
    #define SV_POSITION POSITION
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0_level_9_1
    #define PS_SHADERMODEL ps_4_0_level_9_1
#endif

Texture2D SceneTexture;
sampler2D SceneSampler = sampler_state { Texture = <SceneTexture>; };

Texture2D LightmapTexture;
sampler2D LightmapSampler = sampler_state { Texture = <LightmapTexture>; };

float4 CompositePS(float4 pos : SV_POSITION, float4 color : COLOR0,
                   float2 uv : TEXCOORD0) : COLOR
{
    float4 scene = tex2D(SceneSampler, uv);
    float4 light = tex2D(LightmapSampler, uv);

    float4 result;
    result.rgb = scene.rgb * light.rgb;
    result.a = scene.a;

    return result;
}

technique LightmapComposite
{
    pass P0
    {
        PixelShader = compile PS_SHADERMODEL CompositePS();
    }
};
```

```csharp
// Using the shader for compositing
Effect compositeShader = Content.Load<Effect>("effects/LightmapComposite");
compositeShader.Parameters["LightmapTexture"].SetValue(_lightmap);

spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, effect: compositeShader);
spriteBatch.Draw(_sceneTarget, Vector2.Zero, Color.White);
spriteBatch.End();
```

---

## Point Lights

A point light radiates outward from a position with a given radius, color, and intensity. The simplest implementation: a radial-gradient texture drawn additively onto the lightmap.

### Light Gradient Texture

Generate a soft radial gradient at runtime or use a pre-made texture (a white circle fading to transparent at the edges). Runtime generation:

```csharp
public static Texture2D CreateRadialGradient(GraphicsDevice graphics, int size = 256)
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
```

### Falloff Curves

Different falloff functions control how light intensity decreases with distance:

| Falloff   | Formula                        | Visual                              |
|-----------|--------------------------------|-------------------------------------|
| Linear    | `1 - d`                       | Hard edges, unrealistic             |
| Quadratic | `(1 - d)²`                    | Natural, good default               |
| Smooth    | `smoothstep(1, 0, d)`         | Very soft edges, cinematic          |
| Inverse   | `1 / (1 + k·d²)`             | Physically-based, never reaches 0   |

Where `d` = distance normalized to `[0, 1]` by dividing by the light radius.

```csharp
public enum LightFalloff
{
    Linear,
    Quadratic,
    Smooth,
    Inverse
}

public static float CalculateFalloff(float normalizedDist, LightFalloff falloff)
{
    float d = MathHelper.Clamp(normalizedDist, 0f, 1f);
    return falloff switch
    {
        LightFalloff.Linear    => 1f - d,
        LightFalloff.Quadratic => (1f - d) * (1f - d),
        LightFalloff.Smooth    => d * d * (3f - 2f * d), // smoothstep inverted
        LightFalloff.Inverse   => 1f / (1f + 25f * d * d),
        _                      => 1f - d
    };
}
```

### Drawing Point Lights to Lightmap

```csharp
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

    // Scale radius by camera zoom (extract scale from matrix)
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

// Usage during lightmap pass:
spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive);
// Draw each point light...
DrawPointLight(spriteBatch, _gradient, light.Position, light.Radius,
               light.Color, light.Intensity, camera.TransformMatrix);
spriteBatch.End();
```

---

## Spot Lights

A spot light emits in a cone from a position along a direction. It has an inner angle (full intensity) and outer angle (falloff to zero).

### Spot Light Shader

The most flexible approach — use a shader that clips a radial gradient to a cone:

```hlsl
#if OPENGL
    #define SV_POSITION POSITION
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0_level_9_1
    #define PS_SHADERMODEL ps_4_0_level_9_1
#endif

Texture2D SpriteTexture;
sampler2D SpriteTextureSampler = sampler_state { Texture = <SpriteTexture>; };

float2 LightDirection;   // normalized direction vector
float InnerAngleCos;     // cos(innerAngle / 2)
float OuterAngleCos;     // cos(outerAngle / 2)

float4 SpotLightPS(float4 pos : SV_POSITION, float4 color : COLOR0,
                   float2 uv : TEXCOORD0) : COLOR
{
    float4 texColor = tex2D(SpriteTextureSampler, uv);

    // UV 0..1 → -1..1 centered
    float2 offset = uv * 2.0 - 1.0;
    float dist = length(offset);

    if (dist > 1.0)
        return float4(0, 0, 0, 0);

    // Direction from center to this pixel (normalized)
    float2 dir = normalize(offset);

    // Angle between light direction and pixel direction
    float angleCos = dot(dir, LightDirection);

    // Cone attenuation: 1 inside inner angle, fade to 0 at outer angle
    float coneAtten = smoothstep(OuterAngleCos, InnerAngleCos, angleCos);

    // Radial falloff
    float radialAtten = (1.0 - dist) * (1.0 - dist);

    float atten = coneAtten * radialAtten;

    return texColor * color * atten;
}

technique SpotLight
{
    pass P0
    {
        PixelShader = compile PS_SHADERMODEL SpotLightPS();
    }
};
```

### C# Spot Light Drawing

```csharp
public static void DrawSpotLight(
    SpriteBatch spriteBatch,
    Effect spotShader,
    Texture2D gradientTexture,
    Vector2 worldPosition,
    float radius,
    float directionRadians,
    float innerAngleDeg,
    float outerAngleDeg,
    Color color,
    float intensity,
    Matrix cameraTransform)
{
    Vector2 dir = new Vector2(
        MathF.Cos(directionRadians),
        MathF.Sin(directionRadians));

    spotShader.Parameters["LightDirection"].SetValue(dir);
    spotShader.Parameters["InnerAngleCos"].SetValue(
        MathF.Cos(MathHelper.ToRadians(innerAngleDeg / 2f)));
    spotShader.Parameters["OuterAngleCos"].SetValue(
        MathF.Cos(MathHelper.ToRadians(outerAngleDeg / 2f)));

    Vector2 screenPos = Vector2.Transform(worldPosition, cameraTransform);
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
        (byte)255);

    spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive, effect: spotShader);
    spriteBatch.Draw(gradientTexture, destRect, lightColor);
    spriteBatch.End();
}
```

---

## Ambient Light

Ambient light is the baseline illumination for the entire scene. In the lightmap approach, it's simply the **clear color** of the lightmap render target.

### Day/Night Cycle Integration

Reference [G10 Day/Night System](./G10_custom_game_systems.md) for time-of-day management. The ambient color interpolates across the day:

```csharp
public static class AmbientLighting
{
    // Key ambient colors across 24h
    private static readonly (float hour, Color color)[] DayCurve =
    {
        (0f,  new Color(15, 15, 30)),     // Midnight — deep blue
        (5f,  new Color(25, 25, 50)),     // Pre-dawn
        (6f,  new Color(80, 60, 50)),     // Dawn — warm orange tint
        (8f,  new Color(200, 200, 210)),  // Morning
        (12f, new Color(255, 255, 255)),  // Noon — full brightness
        (17f, new Color(230, 200, 170)),  // Late afternoon — golden
        (19f, new Color(100, 50, 50)),    // Sunset — deep orange/red
        (21f, new Color(30, 30, 60)),     // Dusk
        (24f, new Color(15, 15, 30)),     // Midnight again
    };

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

// In your update loop:
float gameHour = timeSystem.CurrentHour; // 0-24 float
lightingManager.AmbientColor = AmbientLighting.GetAmbientColor(gameHour);
```

### Indoor Ambient Override

When a player enters a building/dungeon, override ambient independently:

```csharp
public record struct IndoorZone(Color AmbientOverride, bool IsActive);

// In the lighting system:
Color ambient = indoorZone.IsActive
    ? indoorZone.AmbientOverride
    : AmbientLighting.GetAmbientColor(gameHour);
lightingManager.AmbientColor = ambient;
```

---

## 2D Shadow Casting

Shadow casting adds occlusion — lights are blocked by walls and obstacles, creating shadow volumes. The classic algorithm computes a **visibility polygon** from each light's perspective.

### Shadow Geometry: Occluder Edges

Occluders are defined as line segments (edges). For a rectangular wall tile, that's 4 edges. For the tilemap, extract only the **boundary edges** (edges between a solid tile and an empty tile).

```csharp
public readonly record struct Edge(Vector2 A, Vector2 B);

/// <summary>Extract boundary edges from a tilemap for shadow casting.</summary>
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
```

### Visibility Polygon Algorithm

For each light, compute which areas are visible (not blocked by occluders). The algorithm:

1. Collect all occluder endpoints within the light's radius.
2. Cast rays to each endpoint (and slightly offset rays ±ε to catch corners).
3. Sort rays by angle.
4. Build a triangle fan from the sorted intersection points.

```csharp
public static class VisibilityPolygon
{
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
                angles.Add(angle - 0.0001f); // nudge rays to see around corners
                angles.Add(angle + 0.0001f);
            }
        }

        // Add boundary rays (circle edge at 0, 90, 180, 270)
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
```

### Rendering the Visibility Polygon to the Lightmap

Convert the visibility polygon to a triangle fan and render it using `GraphicsDevice.DrawUserPrimitives`:

```csharp
public static class ShadowRenderer
{
    public static void DrawVisibilityPolygon(
        GraphicsDevice graphics,
        BasicEffect basicEffect,
        Vector2 lightPos,
        List<Vector2> polygon,
        Color lightColor,
        float intensity,
        Matrix cameraTransform)
    {
        if (polygon.Count < 3) return;

        // Build triangle fan: center + polygon vertices
        var vertices = new VertexPositionColor[polygon.Count * 3];
        Color c = new Color(
            (byte)(lightColor.R * intensity),
            (byte)(lightColor.G * intensity),
            (byte)(lightColor.B * intensity),
            (byte)(255 * intensity));
        Color edge = Color.Transparent;

        int idx = 0;
        for (int i = 0; i < polygon.Count; i++)
        {
            int next = (i + 1) % polygon.Count;

            vertices[idx++] = new VertexPositionColor(
                new Vector3(lightPos, 0), c);
            vertices[idx++] = new VertexPositionColor(
                new Vector3(polygon[i], 0), edge);
            vertices[idx++] = new VertexPositionColor(
                new Vector3(polygon[next], 0), edge);
        }

        basicEffect.World = Matrix.Identity;
        basicEffect.View = Matrix.Identity;
        basicEffect.Projection = cameraTransform
            * Matrix.CreateOrthographicOffCenter(
                0, graphics.Viewport.Width,
                graphics.Viewport.Height, 0,
                0, 1);
        basicEffect.VertexColorEnabled = true;
        basicEffect.TextureEnabled = false;

        foreach (var pass in basicEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            graphics.DrawUserPrimitives(
                PrimitiveType.TriangleList,
                vertices, 0, polygon.Count);
        }
    }
}
```

### Tilemap Shadow Extraction

For large tilemaps, extract edges only for the visible area plus a buffer:

```csharp
public static List<Edge> ExtractVisibleEdges(
    bool[,] solidMap, int tileSize,
    Rectangle cameraBounds, float lightRadius)
{
    // Expand bounds by light radius
    int buffer = (int)(lightRadius / tileSize) + 2;
    int startX = Math.Max(0, cameraBounds.Left / tileSize - buffer);
    int startY = Math.Max(0, cameraBounds.Top / tileSize - buffer);
    int endX = Math.Min(solidMap.GetLength(0), cameraBounds.Right / tileSize + buffer);
    int endY = Math.Min(solidMap.GetLength(1), cameraBounds.Bottom / tileSize + buffer);

    var edges = new List<Edge>();
    for (int y = startY; y < endY; y++)
    {
        for (int x = startX; x < endX; x++)
        {
            if (!solidMap[x, y]) continue;

            float px = x * tileSize;
            float py = y * tileSize;
            float s = tileSize;

            if (y == 0 || !solidMap[x, y - 1])
                edges.Add(new Edge(new(px, py), new(px + s, py)));
            if (y == solidMap.GetLength(1) - 1 || !solidMap[x, y + 1])
                edges.Add(new Edge(new(px + s, py + s), new(px, py + s)));
            if (x == 0 || !solidMap[x - 1, y])
                edges.Add(new Edge(new(px, py + s), new(px, py)));
            if (x == solidMap.GetLength(0) - 1 || !solidMap[x + 1, y])
                edges.Add(new Edge(new(px + s, py), new(px + s, py + s)));
        }
    }

    return edges;
}
```

---

## Normal Map Lighting

Normal maps store per-pixel surface direction, allowing 2D sprites to respond to dynamic light positions — creating the illusion of 3D depth on flat sprites.

### How Normal Maps Work in 2D

A normal map encodes the surface normal at each pixel as an RGB color:
- **R** → X direction (left/right)
- **G** → Y direction (up/down)
- **B** → Z direction (toward camera)

A flat surface facing the camera is `(128, 128, 255)` — that characteristic lavender/blue color.

### Normal Map Lighting Shader

This shader takes the scene's diffuse texture and a normal map, then applies per-pixel lighting from a point light:

```hlsl
#if OPENGL
    #define SV_POSITION POSITION
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0_level_9_1
    #define PS_SHADERMODEL ps_4_0_level_9_1
#endif

Texture2D SpriteTexture;
sampler2D SpriteSampler = sampler_state { Texture = <SpriteTexture>; };

Texture2D NormalMapTexture;
sampler2D NormalSampler = sampler_state { Texture = <NormalMapTexture>; };

// Light parameters (in screen / texture space)
float3 LightPosition;      // x, y in screen coords, z = height above surface
float3 LightColor;         // RGB 0-1
float  LightIntensity;     // multiplier
float  LightRadius;        // in pixels (screen space)
float3 AmbientColor;       // baseline RGB 0-1

float4 NormalMapLightPS(float4 pos : SV_POSITION, float4 color : COLOR0,
                        float2 uv : TEXCOORD0) : COLOR
{
    float4 diffuse = tex2D(SpriteSampler, uv);
    if (diffuse.a < 0.01)
        discard;

    // Sample normal map: convert 0..1 → -1..1
    float3 normal = tex2D(NormalSampler, uv).rgb * 2.0 - 1.0;
    normal = normalize(normal);

    // Light direction in screen space
    // pos.xy is the screen position of this pixel
    float3 pixelPos = float3(pos.xy, 0);
    float3 lightDir = LightPosition - pixelPos;
    float dist = length(lightDir);
    lightDir = normalize(lightDir);

    // Attenuation
    float atten = saturate(1.0 - dist / LightRadius);
    atten *= atten; // quadratic falloff

    // Diffuse lighting (N dot L)
    float NdotL = max(dot(normal, lightDir), 0.0);

    float3 lighting = AmbientColor + LightColor * LightIntensity * NdotL * atten;

    float4 result;
    result.rgb = diffuse.rgb * lighting;
    result.a = diffuse.a;

    return result * color;
}

technique NormalMapLighting
{
    pass P0
    {
        PixelShader = compile PS_SHADERMODEL NormalMapLightPS();
    }
};
```

### Multi-Light Normal Map Shader

For multiple lights, either make multiple draw passes (additive blend) or pack light data into arrays:

```hlsl
#define MAX_LIGHTS 8

float3 LightPositions[MAX_LIGHTS];
float3 LightColors[MAX_LIGHTS];
float  LightIntensities[MAX_LIGHTS];
float  LightRadii[MAX_LIGHTS];
int    ActiveLightCount;
float3 AmbientColor;

Texture2D SpriteTexture;
sampler2D SpriteSampler = sampler_state { Texture = <SpriteTexture>; };

Texture2D NormalMapTexture;
sampler2D NormalSampler = sampler_state { Texture = <NormalMapTexture>; };

float4 MultiLightPS(float4 pos : SV_POSITION, float4 color : COLOR0,
                    float2 uv : TEXCOORD0) : COLOR
{
    float4 diffuse = tex2D(SpriteSampler, uv);
    if (diffuse.a < 0.01)
        discard;

    float3 normal = normalize(tex2D(NormalSampler, uv).rgb * 2.0 - 1.0);
    float3 totalLight = AmbientColor;

    for (int i = 0; i < ActiveLightCount; i++)
    {
        float3 lightDir = LightPositions[i] - float3(pos.xy, 0);
        float dist = length(lightDir);
        lightDir = normalize(lightDir);

        float atten = saturate(1.0 - dist / LightRadii[i]);
        atten *= atten;

        float NdotL = max(dot(normal, lightDir), 0.0);
        totalLight += LightColors[i] * LightIntensities[i] * NdotL * atten;
    }

    float4 result;
    result.rgb = diffuse.rgb * totalLight;
    result.a = diffuse.a;
    return result * color;
}

technique MultiNormalMapLighting
{
    pass P0
    {
        PixelShader = compile PS_SHADERMODEL MultiLightPS();
    }
};
```

### Creating Normal Maps for Pixel Art

Tools for generating normal maps from 2D sprites:

| Tool                | Price        | Notes                                      |
|---------------------|--------------|--------------------------------------------|
| **Laigter**         | Free / FOSS  | Auto-generates from sprite, adjustable     |
| **SpriteIlluminator** | $40        | Professional, real-time preview            |
| **GIMP + plugin**   | Free         | Manual painting, full control              |
| **Sprite Lamp**     | $25          | Multiple source images for accuracy        |

**Workflow:**
1. Paint or auto-generate normal maps with one of the tools above.
2. Name convention: `player.png` → `player_n.png` for the normal map.
3. Load both in MonoGame and bind to the shader.

```csharp
// Loading paired textures
Texture2D diffuse = Content.Load<Texture2D>("sprites/player");
Texture2D normalMap = Content.Load<Texture2D>("sprites/player_n");

normalShader.Parameters["NormalMapTexture"].SetValue(normalMap);

spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend,
                  effect: normalShader);
spriteBatch.Draw(diffuse, position, Color.White);
spriteBatch.End();
```

---

## Light Masking / Light Cookies

Light cookies (or light masks) are textures applied to lights that create patterned shadows — like light shining through window blinds, a torch with flickering noise, or a stained glass window.

### Cookie Texture Application

Instead of a plain radial gradient, multiply the light by a cookie texture:

```hlsl
Texture2D SpriteTexture;
sampler2D SpriteSampler = sampler_state { Texture = <SpriteTexture>; };

Texture2D CookieTexture;
sampler2D CookieSampler = sampler_state
{
    Texture = <CookieTexture>;
    AddressU = Clamp;
    AddressV = Clamp;
};

float CookieRotation;  // radians
float Time;            // for animation

float4 CookieLightPS(float4 pos : SV_POSITION, float4 color : COLOR0,
                     float2 uv : TEXCOORD0) : COLOR
{
    float4 base = tex2D(SpriteSampler, uv);

    // Center UV, rotate, then sample cookie
    float2 centered = uv - 0.5;
    float cs = cos(CookieRotation);
    float sn = sin(CookieRotation);
    float2 rotated = float2(
        centered.x * cs - centered.y * sn,
        centered.x * sn + centered.y * cs
    );
    rotated += 0.5;

    float cookie = tex2D(CookieSampler, rotated).r;

    return base * color * cookie;
}

technique CookieLight
{
    pass P0
    {
        PixelShader = compile PS_SHADERMODEL CookieLightPS();
    }
};
```

### Torch Flicker via Noise

Animate light intensity and radius with Perlin noise for natural torch flicker:

```csharp
public static class LightFlicker
{
    private static readonly Random Rng = new();

    /// <summary>
    /// Returns a flicker multiplier in [1 - strength, 1 + strength].
    /// Call each frame with a unique seed per light.
    /// </summary>
    public static float GetFlicker(float time, float speed, float strength, int seed)
    {
        // Simple layered sine approximation of noise
        float t = time * speed + seed * 73.7f;
        float noise =
            MathF.Sin(t * 1.0f) * 0.5f +
            MathF.Sin(t * 2.3f) * 0.3f +
            MathF.Sin(t * 5.7f) * 0.2f;

        return 1f + noise * strength;
    }
}

// Usage:
float flicker = LightFlicker.GetFlicker(gameTime, speed: 3f, strength: 0.15f, seed: lightId);
light.Intensity = baseIntensity * flicker;
light.Radius = baseRadius * (0.95f + 0.05f * flicker);
```

---

## Performance

Lighting can be expensive. Here are the key optimizations:

### 1. Cull Lights Outside Camera

Only process lights whose bounding circle intersects the camera viewport:

```csharp
public static bool IsLightVisible(Vector2 lightPos, float lightRadius, Rectangle cameraBounds)
{
    // Expand camera bounds by light radius
    var expanded = new Rectangle(
        cameraBounds.X - (int)lightRadius,
        cameraBounds.Y - (int)lightRadius,
        cameraBounds.Width + (int)(lightRadius * 2),
        cameraBounds.Height + (int)(lightRadius * 2));

    return expanded.Contains((int)lightPos.X, (int)lightPos.Y);
}
```

### 2. Limit Active Light Count

Sort by distance to camera center, keep only the N brightest/closest:

```csharp
public static void SelectActiveLights(
    Span<LightData> allLights,
    Vector2 cameraCenter,
    int maxLights,
    List<LightData> output)
{
    output.Clear();

    // Score = intensity / distance (closer + brighter = higher priority)
    Span<(int index, float score)> scored = stackalloc (int, float)[allLights.Length];
    for (int i = 0; i < allLights.Length; i++)
    {
        float dist = Vector2.Distance(allLights[i].Position, cameraCenter);
        scored[i] = (i, allLights[i].Intensity / MathF.Max(dist, 1f));
    }

    // Partial sort — only need top maxLights
    scored.Sort((a, b) => b.score.CompareTo(a.score));

    int count = Math.Min(maxLights, allLights.Length);
    for (int i = 0; i < count; i++)
        output.Add(allLights[scored[i].index]);
}
```

### 3. Lower-Resolution Lightmap

Render the lightmap at half (or quarter) resolution, then upscale. Lights are soft by nature, so the quality loss is minimal:

```csharp
public void CreateTargets(float lightmapScale = 0.5f)
{
    int w = _graphics.PresentationParameters.BackBufferWidth;
    int h = _graphics.PresentationParameters.BackBufferHeight;

    _sceneTarget = new RenderTarget2D(_graphics, w, h);
    _lightmap = new RenderTarget2D(_graphics,
        (int)(w * lightmapScale),
        (int)(h * lightmapScale));
}

// When drawing to lightmap, scale the camera transform:
Matrix lightmapCamera = cameraTransform * Matrix.CreateScale(lightmapScale);

// When compositing, the lightmap stretches to fill the screen — SpriteBatch handles this.
```

### 4. Light LOD (Level of Detail)

Distant or small lights can skip shadow computation:

```csharp
public enum LightLOD { Full, NoShadows, SimplifiedRadius }

public static LightLOD GetLightLOD(float distToCamera, float lightRadius)
{
    if (distToCamera > lightRadius * 4f) return LightLOD.SimplifiedRadius;
    if (distToCamera > lightRadius * 2f) return LightLOD.NoShadows;
    return LightLOD.Full;
}
```

### 5. Cache Shadow Geometry

For static lights (lights that don't move) with static occluders, compute the visibility polygon once and cache it:

```csharp
public class CachedShadow
{
    public List<Vector2>? CachedPolygon { get; private set; }
    public Vector2 LastPosition { get; private set; }
    public bool IsDirty { get; set; } = true;

    public List<Vector2> GetOrCompute(Vector2 lightPos, float radius, List<Edge> edges)
    {
        if (!IsDirty && CachedPolygon != null && LastPosition == lightPos)
            return CachedPolygon;

        CachedPolygon = VisibilityPolygon.Compute(lightPos, radius, edges);
        LastPosition = lightPos;
        IsDirty = false;
        return CachedPolygon;
    }
}
```

---

## ECS Integration

All lighting data lives in Arch ECS components. A `LightRenderSystem` reads them each frame.

### Components

```csharp
using Arch.Core;

/// <summary>Point light emitting radially from the entity's position.</summary>
public record struct PointLight(
    float Radius = 200f,
    Color? Color = null,
    float Intensity = 1f,
    LightFalloff Falloff = LightFalloff.Quadratic,
    bool CastsShadows = false
)
{
    public Color Color { get; set; } = Color ?? Microsoft.Xna.Framework.Color.White;
}

/// <summary>Spot light with cone direction.</summary>
public record struct SpotLight(
    float Radius = 300f,
    Color? Color = null,
    float Intensity = 1f,
    float DirectionRadians = 0f,
    float InnerAngleDeg = 15f,
    float OuterAngleDeg = 30f,
    LightFalloff Falloff = LightFalloff.Quadratic,
    bool CastsShadows = false
)
{
    public Color Color { get; set; } = Color ?? Microsoft.Xna.Framework.Color.White;
}

/// <summary>Global ambient light — attach to a singleton entity.</summary>
public record struct AmbientLight(
    Color? Color = null,
    float Intensity = 1f
)
{
    public Color Color { get; set; } = Color ?? new Color(30, 30, 50);
}

/// <summary>Marks an entity as a shadow occluder with edge geometry.</summary>
public record struct Occluder(Edge[] Edges);

/// <summary>Flicker effect for lights (torches, candles).</summary>
public record struct LightFlickerEffect(
    float Speed = 3f,
    float Strength = 0.15f,
    int Seed = 0,
    float BaseIntensity = 1f,
    float BaseRadius = 200f
);

/// <summary>Light cookie/mask texture reference.</summary>
public record struct LightCookie(
    string TextureName,
    float Rotation,
    float RotationSpeed
);

/// <summary>Standard position component (shared with other systems).</summary>
public record struct Position(Vector2 Value);
```

### Light Render System

```csharp
using Arch.Core;
using Arch.Core.Extensions;

public class LightRenderSystem
{
    private readonly World _world;
    private readonly LightingManager _lighting;
    private readonly SpriteBatch _spriteBatch;
    private readonly Texture2D _gradientTexture;
    private readonly Effect _spotShader;
    private readonly BasicEffect _basicEffect;
    private readonly List<Edge> _cachedEdges = new();
    private readonly List<LightData> _activeLights = new();

    private readonly QueryDescription _pointLightQuery = new QueryDescription()
        .WithAll<Position, PointLight>();
    private readonly QueryDescription _spotLightQuery = new QueryDescription()
        .WithAll<Position, SpotLight>();
    private readonly QueryDescription _ambientQuery = new QueryDescription()
        .WithAll<AmbientLight>();
    private readonly QueryDescription _occluderQuery = new QueryDescription()
        .WithAll<Position, Occluder>();

    public LightRenderSystem(
        World world,
        LightingManager lighting,
        SpriteBatch spriteBatch,
        Texture2D gradientTexture,
        Effect spotShader,
        GraphicsDevice graphics)
    {
        _world = world;
        _lighting = lighting;
        _spriteBatch = spriteBatch;
        _gradientTexture = gradientTexture;
        _spotShader = spotShader;
        _basicEffect = new BasicEffect(graphics);
    }

    public void Render(Matrix cameraTransform, Rectangle cameraBounds, float gameTime)
    {
        // 1. Resolve ambient
        _world.Query(in _ambientQuery, (ref AmbientLight ambient) =>
        {
            _lighting.AmbientColor = new Color(
                (byte)(ambient.Color.R * ambient.Intensity),
                (byte)(ambient.Color.G * ambient.Intensity),
                (byte)(ambient.Color.B * ambient.Intensity));
        });

        // 2. Begin lightmap pass
        _lighting.BeginLightmap();

        // 3. Collect occluder edges
        _cachedEdges.Clear();
        _world.Query(in _occluderQuery, (ref Position pos, ref Occluder occ) =>
        {
            foreach (var edge in occ.Edges)
            {
                _cachedEdges.Add(new Edge(
                    edge.A + pos.Value,
                    edge.B + pos.Value));
            }
        });

        // 4. Render point lights
        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive);

        _world.Query(in _pointLightQuery,
            (ref Position pos, ref PointLight light) =>
        {
            if (!IsLightVisible(pos.Value, light.Radius, cameraBounds))
                return;

            if (light.CastsShadows && _cachedEdges.Count > 0)
            {
                _spriteBatch.End();

                var polygon = VisibilityPolygon.Compute(
                    pos.Value, light.Radius, _cachedEdges);

                ShadowRenderer.DrawVisibilityPolygon(
                    _spriteBatch.GraphicsDevice, _basicEffect,
                    pos.Value, polygon, light.Color, light.Intensity,
                    cameraTransform);

                _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive);
            }
            else
            {
                DrawPointLight(_spriteBatch, _gradientTexture,
                    pos.Value, light.Radius, light.Color,
                    light.Intensity, cameraTransform);
            }
        });

        _spriteBatch.End();

        // 5. Render spot lights
        _world.Query(in _spotLightQuery,
            (ref Position pos, ref SpotLight light) =>
        {
            if (!IsLightVisible(pos.Value, light.Radius, cameraBounds))
                return;

            DrawSpotLight(_spriteBatch, _spotShader, _gradientTexture,
                pos.Value, light.Radius, light.DirectionRadians,
                light.InnerAngleDeg, light.OuterAngleDeg,
                light.Color, light.Intensity, cameraTransform);
        });
    }

    private static bool IsLightVisible(Vector2 pos, float radius, Rectangle cam)
    {
        return pos.X + radius > cam.Left && pos.X - radius < cam.Right &&
               pos.Y + radius > cam.Top && pos.Y - radius < cam.Bottom;
    }
}
```

### Flicker Update System

```csharp
public class LightFlickerSystem
{
    private readonly World _world;
    private readonly QueryDescription _query = new QueryDescription()
        .WithAll<PointLight, LightFlickerEffect>();

    public LightFlickerSystem(World world) => _world = world;

    public void Update(float totalSeconds)
    {
        _world.Query(in _query,
            (ref PointLight light, ref LightFlickerEffect flicker) =>
        {
            float f = LightFlicker.GetFlicker(
                totalSeconds, flicker.Speed, flicker.Strength, flicker.Seed);

            light.Intensity = flicker.BaseIntensity * f;
            light.Radius = flicker.BaseRadius * (0.95f + 0.05f * f);
        });
    }
}
```

---

## Practical Examples

### Example 1: Dungeon with Torchlight and Shadows

```csharp
public class DungeonLightingExample
{
    private World _world;
    private LightingManager _lighting;
    private LightRenderSystem _lightSystem;
    private LightFlickerSystem _flickerSystem;

    public void Initialize(GraphicsDevice graphics, SpriteBatch spriteBatch)
    {
        _world = World.Create();
        _lighting = new LightingManager(graphics);

        // Dark dungeon ambient
        var ambientEntity = _world.Create(
            new AmbientLight(new Color(10, 10, 15), 1f));

        // Wall occluder tiles (from tilemap)
        bool[,] solidMap = LoadDungeonSolids(); // your tilemap data
        var edges = ExtractOccluderEdges(solidMap, tileSize: 16);
        _world.Create(
            new Position(Vector2.Zero),
            new Occluder(edges.ToArray()));

        // Place torches along dungeon walls
        Vector2[] torchPositions = { new(128, 96), new(320, 96), new(224, 256) };

        for (int i = 0; i < torchPositions.Length; i++)
        {
            _world.Create(
                new Position(torchPositions[i]),
                new PointLight(
                    Radius: 120f,
                    Color: new Color(255, 180, 80),  // warm orange
                    Intensity: 0.9f,
                    Falloff: LightFalloff.Quadratic,
                    CastsShadows: true),
                new LightFlickerEffect(
                    Speed: 4f,
                    Strength: 0.2f,
                    Seed: i * 17,
                    BaseIntensity: 0.9f,
                    BaseRadius: 120f));
        }
    }

    public void Update(GameTime gameTime)
    {
        float totalSec = (float)gameTime.TotalGameTime.TotalSeconds;
        _flickerSystem.Update(totalSec);
    }

    public void Draw(SpriteBatch spriteBatch, Matrix cameraTransform, Rectangle cameraBounds,
                     float gameTime)
    {
        // 1. Draw scene at full brightness
        _lighting.BeginScene();
        DrawDungeonTiles(spriteBatch, cameraTransform);
        DrawEntities(spriteBatch, cameraTransform);

        // 2. Render lightmap
        _lightSystem.Render(cameraTransform, cameraBounds, gameTime);

        // 3. Composite
        _lighting.EndAndComposite(spriteBatch);
    }
}
```

### Example 2: Outdoor Day/Night Lighting

```csharp
public class OutdoorLightingExample
{
    private World _world;
    private Entity _ambientEntity;
    private float _gameHour = 12f; // start at noon
    private float _timeScale = 60f; // 1 real second = 1 game minute

    public void Initialize()
    {
        _world = World.Create();

        // Ambient follows day/night cycle
        _ambientEntity = _world.Create(
            new AmbientLight(Color.White, 1f));

        // Street lamps — only visible at night (controlled by intensity)
        for (int i = 0; i < 5; i++)
        {
            _world.Create(
                new Position(new Vector2(100 + i * 200, 300)),
                new PointLight(
                    Radius: 150f,
                    Color: new Color(255, 240, 200),
                    Intensity: 0f, // start off, enabled at dusk
                    Falloff: LightFalloff.Smooth,
                    CastsShadows: false));
        }

        // Glowing windows in buildings
        _world.Create(
            new Position(new Vector2(350, 220)),
            new PointLight(
                Radius: 60f,
                Color: new Color(255, 220, 150),
                Intensity: 0f,
                Falloff: LightFalloff.Quadratic,
                CastsShadows: false));
    }

    public void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _gameHour += dt * _timeScale / 60f;
        _gameHour %= 24f;

        // Update ambient
        Color ambient = AmbientLighting.GetAmbientColor(_gameHour);
        _world.Set(_ambientEntity, new AmbientLight(ambient, 1f));

        // Toggle street lamps: on from 19:00 to 06:00
        bool nightTime = _gameHour >= 19f || _gameHour < 6f;
        float lampIntensity = nightTime ? 0.8f : 0f;

        var lampQuery = new QueryDescription().WithAll<Position, PointLight>();
        _world.Query(in lampQuery, (ref PointLight light) =>
        {
            // Only affect street lamps (identified by color or tag)
            if (light.Color == new Color(255, 240, 200))
                light.Intensity = lampIntensity;
        });
    }
}
```

### Example 3: Flashlight in a Dark Room

```csharp
public class FlashlightExample
{
    private World _world;
    private Entity _playerEntity;
    private Entity _flashlightEntity;

    public void Initialize()
    {
        _world = World.Create();

        // Pitch black room
        _world.Create(new AmbientLight(new Color(5, 5, 8), 1f));

        // Room walls as occluders
        var roomEdges = new Edge[]
        {
            new(new(0, 0), new(640, 0)),       // top wall
            new(new(640, 0), new(640, 480)),   // right wall
            new(new(640, 480), new(0, 480)),   // bottom wall
            new(new(0, 480), new(0, 0)),       // left wall
            // Interior walls
            new(new(200, 0), new(200, 200)),   // partial wall
            new(new(400, 280), new(400, 480)), // partial wall
        };

        _world.Create(
            new Position(Vector2.Zero),
            new Occluder(roomEdges));

        // Player with flashlight
        _playerEntity = _world.Create(
            new Position(new Vector2(100, 240)));

        _flashlightEntity = _world.Create(
            new Position(new Vector2(100, 240)),
            new SpotLight(
                Radius: 350f,
                Color: new Color(240, 240, 255),  // cool white
                Intensity: 1.2f,
                DirectionRadians: 0f,
                InnerAngleDeg: 12f,
                OuterAngleDeg: 25f,
                Falloff: LightFalloff.Quadratic,
                CastsShadows: true));

        // Small player glow (so you can see yourself)
        _world.Create(
            new Position(new Vector2(100, 240)),
            new PointLight(
                Radius: 30f,
                Color: new Color(200, 200, 220),
                Intensity: 0.3f,
                Falloff: LightFalloff.Smooth,
                CastsShadows: false));
    }

    public void Update(Vector2 playerPos, Vector2 mouseWorldPos)
    {
        // Move flashlight with player
        _world.Set(_flashlightEntity, new Position(playerPos));
        _world.Set(_playerEntity, new Position(playerPos));

        // Point flashlight toward mouse
        Vector2 dir = mouseWorldPos - playerPos;
        float angle = MathF.Atan2(dir.Y, dir.X);

        ref var spot = ref _world.Get<SpotLight>(_flashlightEntity);
        spot.DirectionRadians = angle;
    }
}
```

---

## Summary Checklist

| Feature | Technique | Shader Needed? |
|---------|-----------|----------------|
| Basic lighting | Lightmap × Scene | Multiply BlendState (or composite shader) |
| Point lights | Radial gradient + additive blend | No (texture-based) |
| Spot lights | Cone clipping shader | Yes — `SpotLight.fx` |
| Ambient | Lightmap clear color | No |
| Shadows | Visibility polygon → triangle fan | No (BasicEffect geometry) |
| Normal maps | Per-pixel N·L lighting | Yes — `NormalMapLighting.fx` |
| Light cookies | Textured mask on light | Yes — `CookieLight.fx` |
| Flicker | Noise-based intensity modulation | No (C# update) |

**Rendering order each frame:**
1. `BeginScene()` → draw all sprites/tiles at full brightness
2. `BeginLightmap()` → clear to ambient → draw all lights (additive) → draw shadow geometry
3. `EndAndComposite()` → multiply lightmap over scene → final output

> **Tip:** Start simple — ambient + point lights with no shadows. Add shadow casting only where it matters (player torch, boss room). Normal maps are a polish step that dramatically improves visual quality for minimal performance cost.
