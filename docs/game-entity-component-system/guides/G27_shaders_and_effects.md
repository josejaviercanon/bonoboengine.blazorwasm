# G27 — Shaders & Visual Effects


> **Category:** Guide · **Related:** [G2 Rendering & Graphics](./G2_rendering_and_graphics.md) · [G1 Custom Code Recipes](./G1_custom_code_recipes.md) · [G23 Particles](./G23_particles.md) · [G8 Content Pipeline](./G8_content_pipeline.md)

---

Production-ready HLSL shader code and C# integration patterns for elemental effects (fire, water, wind, earth, lightning, ice), post-processing, and performance guidance — all tuned for pixel-art 2D games using MonoGame's `SpriteBatch` workflow.

---

## MonoGame Shader Pipeline

Every `.fx` file in MonoGame is written in HLSL and compiled through the Content Pipeline into the **MGFX binary format**. The pipeline uses `EffectImporter` and `EffectProcessor`, and on OpenGL platforms, MojoShader automatically translates your HLSL to GLSL at build time. You never write GLSL directly.

A minimal 2D shader has four parts: platform defines, parameters, a pixel shader function, and a technique block. Here is the **canonical template** every shader in this guide builds from:

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
sampler2D SpriteTextureSampler = sampler_state
{
    Texture = <SpriteTexture>;
};

struct VertexShaderOutput
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR0;
    float2 TextureCoordinates : TEXCOORD0;
};

float4 MainPS(VertexShaderOutput input) : COLOR
{
    return tex2D(SpriteTextureSampler, input.TextureCoordinates) * input.Color;
}

technique SpriteDrawing
{
    pass P0
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
};
```

When you only define a pixel shader (no vertex shader in the technique), **MonoGame's built-in `SpriteEffect` vertex shader handles all vertex transformation automatically**. This is the recommended approach for 2D — write pixel shaders only unless you specifically need vertex displacement. SpriteBatch automatically binds the sprite's texture to sampler register `s0` and passes the tint color from `SpriteBatch.Draw()` as `input.Color`.

### C# Integration

```csharp
// Load in LoadContent()
Effect myShader = Content.Load<Effect>("effects/MyShader");

// Set parameters before drawing
myShader.Parameters["Time"]?.SetValue((float)gameTime.TotalGameTime.TotalSeconds);
myShader.Parameters["Intensity"]?.SetValue(0.75f);

// Draw with effect
spriteBatch.Begin(effect: myShader, samplerState: SamplerState.PointClamp);
spriteBatch.Draw(myTexture, position, Color.White);
spriteBatch.End();
```

### Three Critical Gotchas

1. **Stripped parameters** — The HLSL compiler aggressively strips unused parameters. If you declare a `float Intensity` but never reference it, accessing it from C# throws a null reference. Use the `?.SetValue()` pattern during development.
2. **OpenGL defaults** — On OpenGL platforms, default parameter values in HLSL do not work. Always set every parameter from C#.
3. **Pixel art sampling** — Always pass `SamplerState.PointClamp` to prevent sub-pixel filtering that destroys crisp pixels.

### Passing Extra Textures

For additional textures beyond the primary sprite texture (noise maps, gradient ramps, displacement maps), declare them as separate texture/sampler pairs:

```hlsl
texture NoiseTexture;
sampler NoiseSampler = sampler_state
{
    Texture = <NoiseTexture>;
    MagFilter = Point;
    MinFilter = Point;
    AddressU = Wrap;
    AddressV = Wrap;
};
```

Set from C# with `effect.Parameters["NoiseTexture"].SetValue(noiseTexture2D);`.

---

## Procedural Noise Primitives

These reusable building blocks appear throughout the elemental shaders below. Extract them into a shared `.fxh` include file or copy into each shader as needed.

### Hash-Based Value Noise

```hlsl
float hash(float2 p) {
    return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
}

float noise(float2 p) {
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = hash(i);
    float b = hash(i + float2(1, 0));
    float c = hash(i + float2(0, 1));
    float d = hash(i + float2(1, 1));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}
```

### Fractal Brownian Motion (FBM)

Layers multiple noise octaves for organic, natural-looking patterns:

```hlsl
float fbm(float2 p) {
    float value = 0.0;
    float amplitude = 0.5;
    for (int i = 0; i < 5; i++) {
        value += amplitude * noise(p);
        p *= 2.0;
        amplitude *= 0.5;
    }
    return value;
}
```

### Voronoi (Cellular) Noise

Returns F1 (nearest cell distance) and F2 (second-nearest). `F2 - F1` produces sharp cell borders for ice cracks and cobblestone; F1 gives smooth cell interiors:

```hlsl
float2 random2(float2 p)
{
    return frac(sin(float2(
        dot(p, float2(127.1, 311.7)),
        dot(p, float2(269.5, 183.3))
    )) * 43758.5453);
}

float2 Voronoi(float2 uv)
{
    float2 i_st = floor(uv);
    float2 f_st = frac(uv);
    float f1 = 1.0, f2 = 1.0;

    for (int y = -1; y <= 1; y++)
    for (int x = -1; x <= 1; x++)
    {
        float2 neighbor = float2(x, y);
        float2 point = random2(i_st + neighbor);
        float dist = length(neighbor + point - f_st);
        if (dist < f1) { f2 = f1; f1 = dist; }
        else if (dist < f2) { f2 = dist; }
    }
    return float2(f1, f2);
}
```

---

## Fire: Noise-Driven Flames with Color Gradient Mapping

The most effective 2D fire technique combines **scrolling noise with color gradient mapping**. Sample a seamless noise texture with UV coordinates that scroll upward over time, then map the resulting float value to a fire color ramp.

The core idea: noise scrolls upward (simulating rising heat), a gradient texture defines the flame's shape (wide at base, narrow at top), and `step()` or `smoothstep()` creates color bands. Using `step()` gives hard pixel-art-friendly color banding; `smoothstep()` gives softer transitions.

```hlsl
float Time;
float ScrollSpeed;
float NoiseScale;
float3 ColorBright;  // yellow-white core
float3 ColorMid;     // orange
float3 ColorDark;    // deep red

sampler2D NoiseSampler : register(s0);

texture GradientTexture;
sampler GradientSampler = sampler_state { Texture = <GradientTexture>; };

float4 FirePS(VertexShaderOutput input) : COLOR
{
    float2 uv = input.TextureCoordinates;

    // Scroll noise upward
    float noise = tex2D(NoiseSampler, uv * NoiseScale + float2(0, -Time * ScrollSpeed)).r;

    // Shape mask: gradient is bright at base, dark at tip
    float shape = tex2D(GradientSampler, uv).r;

    // Three-band color mapping using step
    float s1 = step(noise, shape);
    float s2 = step(noise, shape - 0.2);
    float s3 = step(noise, shape - 0.4);

    float3 col = lerp(ColorBright, ColorDark, s1 - s2);
    col = lerp(col, ColorMid, s2 - s3);

    return float4(col, s1);
}
```

### Color Gradient Mapping

Three proven approaches for controlling the fire's look:

**Math-based ramp** — red channels ramp up first, green follows, blue appears only at the white-hot core:

```hlsl
float3 FireColor(float t)
{
    float3 col;
    col.r = saturate(t * 3.0);
    col.g = saturate(t * 3.0 - 1.0);
    col.b = saturate(pow(t, 7.0));
    return col;
}
```

**1D gradient texture** — a 256x1 pixel image with your exact fire palette. Sample with `tex2D(gradientSampler, float2(noiseValue, 0.5))`. This lets artists tweak fire colors without touching shader code.

**Procedural noise** — use the hash-based FBM function from the Procedural Noise Primitives section above for fire without a noise texture.

### Heat Haze Distortion

Post-processing pass that offsets UVs using sine waves modulated by distance from the fire source:

```hlsl
float4 HeatHazePS(VertexShaderOutput input) : COLOR
{
    float2 uv = input.TextureCoordinates;
    uv.x += sin(uv.y * 20.0 + Time * 5.0) * 0.003 * (1.0 - uv.y);
    return tex2D(SpriteTextureSampler, uv);
}
```

### Additive Glow

Use **`BlendState.Additive`** in `SpriteBatch.Begin()` when drawing fire sprites. For richer glow, render fire to a separate render target, blur with a Gaussian shader pass, then composite additively over the scene.

---

## Water: Layered Distortion with Reflections and Caustics

Water in a top-down 2D game is built from **layered sine-wave distortion, scrolling noise, and optional reflection**. The foundational technique — confirmed by the shipped Shantae: Risky's Revenge postmortem — is simple UV displacement using sine functions:

```hlsl
float Time;
float Amplitude;   // 0.003 - 0.01 for subtle effect
float Frequency;   // 20.0 - 60.0
float Speed;       // 3.0 - 8.0

float4 WaterPS(VertexShaderOutput input) : COLOR
{
    float2 uv = input.TextureCoordinates;

    // Layer multiple sine waves for organic motion
    float offset = Amplitude * sin(uv.y * Frequency + Time * Speed);
    offset += Amplitude * 0.5 * sin(uv.y * Frequency * 2.3 + Time * Speed * 1.7);
    offset += Amplitude * 0.25 * sin(uv.y * Frequency * 3.7 + Time * Speed * 0.8);

    return tex2D(SpriteTextureSampler, uv + float2(offset, 0));
}
```

### Top-Down Reflections

Draw all reflectable entities flipped vertically into a `RenderTarget2D`, apply wave distortion, then draw under water tiles at reduced opacity:

```hlsl
texture NoiseTexture;
sampler NoiseSampler = sampler_state { Texture = <NoiseTexture>; AddressU = Wrap; AddressV = Wrap; };

float DistortionStrength;
float2 ScrollSpeed;
float4 WaterTint;

float4 ReflectionPS(VertexShaderOutput input) : COLOR
{
    float2 uv = input.TextureCoordinates;
    float2 noise = tex2D(NoiseSampler, uv + Time * ScrollSpeed).rg - 0.5;
    uv += float2(noise.x * DistortionStrength, 0);
    float4 reflection = tex2D(SpriteTextureSampler, uv);
    return lerp(WaterTint, reflection, WaterTint.a);
}
```

### Caustics

Industry-standard dual-texture scrolling: sample the same caustic texture twice at different scroll directions, blend using `min()` (darken mode). The intersection of two moving patterns creates organic, animated caustic light:

```hlsl
float4 CausticsPS(VertexShaderOutput input) : COLOR
{
    float4 scene = tex2D(SpriteTextureSampler, input.TextureCoordinates);
    float2 uv1 = input.TextureCoordinates * 0.1 + Time * float2(0.02, 0.03);
    float2 uv2 = input.TextureCoordinates * 0.14 + Time * float2(-0.03, 0.01);
    float c1 = tex2D(NoiseSampler, uv1).r;
    float c2 = tex2D(NoiseSampler, uv2).r;
    float caustic = min(c1, c2);
    return scene + caustic * 0.3;
}
```

### Ripple Effects

Circular distortion radiating from a point (e.g., player stepping into water) with time-based decay:

```hlsl
float2 RippleCenter;
float RippleTime;

float4 RipplePS(VertexShaderOutput input) : COLOR
{
    float2 uv = input.TextureCoordinates;
    float2 delta = uv - RippleCenter;
    float dist = length(delta);
    float elapsed = Time - RippleTime;
    float wave = sin(dist * 30.0 - elapsed * 8.0);
    float falloff = exp(-dist * 3.0) * exp(-elapsed * 1.5);
    float2 offset = normalize(delta + 0.0001) * wave * 0.01 * saturate(falloff);
    return tex2D(SpriteTextureSampler, uv + offset);
}
```

### Shoreline Foam

Use a gradient encoded in the tilemap (1.0 at water edges, 0.0 at center) combined with an animated sine threshold that creates rippling foam lines at the shore. For pixel art specifically, **posterize your UVs** before sampling noise — `floor(uv * 64.0) / 64.0` — to maintain the chunky pixel look.

---

## Wind: Traveling Sway Waves Across Vegetation

Wind effects in 2D work through **UV displacement anchored at the sprite's base**, creating sway where the bottom stays fixed and the top moves most. The critical detail for field-wide wind is **phase-offsetting by world position** so each grass sprite sways at a slightly different time, creating a visible wave traveling across the landscape.

```hlsl
float Time;
float WindStrength;      // 0.015
float WindSpeed;         // 2.0
float WindFrequency;     // 0.3  (spatial frequency)
float2 WindDirection;    // float2(1, 0)
float2 WorldPos;         // sprite's world position, passed per-draw

float4 WindSwayPS(VertexShaderOutput input) : COLOR
{
    float2 uv = input.TextureCoordinates;

    // Phase offset from world position creates traveling wave
    float phase = dot(WorldPos, WindDirection) * WindFrequency;
    float swayAmount = pow(1.0 - uv.y, 2.0); // quadratic: bottom anchored

    // Multiple overlapping sine waves
    float wind = sin(Time * WindSpeed + phase) * 0.6
               + sin(Time * WindSpeed * 1.3 + phase * 0.7) * 0.3
               + sin(Time * WindSpeed * 2.1 + phase * 1.5) * 0.1;

    uv.x += wind * WindStrength * swayAmount;

    float4 color = tex2D(SpriteTextureSampler, uv) * input.Color;
    if (uv.x < 0.0 || uv.x > 1.0) color.a = 0;
    return color;
}
```

**Important**: SpriteBatch quads have only 4 vertices, so vertex-shader displacement creates parallelogram shears, not smooth curves. Pixel-shader UV displacement (as above) gives per-pixel control and looks much better. Add **transparent padding** around your sprite textures so displaced UVs don't clip at edges.

### Visible Wind Gusts

For the Stardew Valley-style shimmering grass effect, sample a scrolling noise texture at world coordinates and brighten grass where the gust passes:

```hlsl
float2 noiseUV = WorldPos * 0.01 + WindDirection * Time * 0.5;
float gustMask = smoothstep(0.4, 0.8, tex2D(NoiseSampler, noiseUV).r);
color.rgb += float3(0.08, 0.12, 0.05) * gustMask * color.a;
```

---

## Earth: Terrain Blending, Cracks, and Impact Shockwaves

### Splatmap Terrain Blending

An RGBA texture stores blend weights for up to four terrain types, while tiled detail textures provide the actual surface appearance:

```hlsl
texture GrassTexture;
texture DirtTexture;
texture StoneTexture;
texture SplatmapTexture;

sampler GrassSampler = sampler_state { Texture = <GrassTexture>; AddressU = Wrap; AddressV = Wrap; };
sampler DirtSampler = sampler_state { Texture = <DirtTexture>; AddressU = Wrap; AddressV = Wrap; };
sampler StoneSampler = sampler_state { Texture = <StoneTexture>; AddressU = Wrap; AddressV = Wrap; };
sampler SplatSampler = sampler_state { Texture = <SplatmapTexture>; AddressU = Clamp; AddressV = Clamp; };

float TileScale;

float4 TerrainPS(VertexShaderOutput input) : COLOR
{
    float2 tileUV = input.TextureCoordinates * TileScale;
    float4 splat = tex2D(SplatSampler, input.TextureCoordinates);

    float4 grass = tex2D(GrassSampler, tileUV) * splat.r;
    float4 dirt  = tex2D(DirtSampler, tileUV)  * splat.g;
    float4 stone = tex2D(StoneSampler, tileUV) * splat.b;

    return grass + dirt + stone;
}
```

### Height-Based Blending

Makes terrain like sand fill the cracks in cobblestones rather than uniformly fading. Store a height value in each terrain texture's alpha channel, then blend based on combined height + splatmap weight:

```hlsl
float3 HeightBlend(float4 tex1, float a1, float4 tex2, float a2)
{
    float depth = 0.2;
    float ma = max(tex1.a + a1, tex2.a + a2) - depth;
    float b1 = max(tex1.a + a1 - ma, 0);
    float b2 = max(tex2.a + a2 - ma, 0);
    return (tex1.rgb * b1 + tex2.rgb * b2) / (b1 + b2);
}
```

### Ground Cracks

Use a pre-authored crack texture or procedural Voronoi noise. Animate the reveal using a threshold that increases over time — the crack pattern is always there, but `smoothstep` reveals progressively more of it:

```hlsl
float CrackProgress; // 0 = pristine, 1 = fully cracked

float4 CrackPS(VertexShaderOutput input) : COLOR
{
    float4 base = tex2D(SpriteTextureSampler, input.TextureCoordinates);
    float crackMask = tex2D(CrackSampler, input.TextureCoordinates).r;
    float threshold = 1.0 - CrackProgress;
    float edge = smoothstep(threshold - 0.05, threshold, crackMask);
    float inner = smoothstep(threshold, threshold + 0.025, crackMask);

    float4 result = base;
    result = lerp(result, float4(0.6, 0.3, 0.1, 1), edge);  // brown edge
    result = lerp(result, float4(0.1, 0.05, 0, 1), inner);   // dark interior
    return result;
}
```

### Ground Impact Shockwaves

Post-processing effect — an expanding ring that displaces screen UV coordinates outward from the impact point:

```hlsl
float2 ShockCenter;
float ShockRadius;
float ShockThickness;

float4 ShockwavePS(VertexShaderOutput input) : COLOR
{
    float2 uv = input.TextureCoordinates;
    float dist = length(uv - ShockCenter);
    float ring = 1.0 - saturate(abs(dist - ShockRadius) / ShockThickness);
    ring = pow(ring, 2.0);
    float2 dir = normalize(uv - ShockCenter);
    return tex2D(SpriteTextureSampler, uv + dir * ring * 0.03);
}
```

Animate `ShockRadius` from 0 to beyond screen bounds over ~0.4 seconds with an easing curve.

---

## Lightning: Jagged Bolts with Exponential Glow

Lightning bolts use **zigzag modulation** — a repeating sawtooth wave displaces the center line of a vertical stripe, creating the characteristic jagged shape. A `floor(Time * speed)` drives random re-seeding so the bolt reshuffles its shape each frame. Exponential falloff creates the glow around the bright core.

```hlsl
float Time;
float BoltWidth;         // 0.04
float ZigzagAmplitude;   // 0.15
float ZigzagFrequency;   // 12.0
float GlowStrength;      // 80.0
float FlickerSpeed;      // 30.0
float3 CoreColor;        // float3(1, 1, 1)
float3 GlowColor;        // float3(0.4, 0.6, 1.0)

float rand(float x) { return frac(sin(x) * 100000.0); }

float4 LightningPS(VertexShaderOutput input) : COLOR
{
    float2 uv = input.TextureCoordinates;

    // Randomize bolt shape each "frame"
    float timeRand = rand(floor(Time * FlickerSpeed));

    // Zigzag displacement
    float bolt = abs(fmod(uv.y * ZigzagFrequency + timeRand * 3.0, 0.5) - 0.25) - 0.125;
    bolt *= 4.0 * ZigzagAmplitude;

    // Taper at endpoints
    float taper = (0.5 - abs(uv.y - 0.5)) * 2.0;
    bolt *= taper;

    // Distance from bolt center
    float dist = abs(uv.x - 0.5 + bolt);

    // Sharp white core + soft blue glow
    float core = 1.0 - smoothstep(0.0, BoltWidth, dist);
    float glow = exp(-dist * dist * GlowStrength) * taper;

    // Random blink (occasional full flicker off)
    float blink = step(rand(floor(Time * FlickerSpeed * 0.5)) * 0.8, 0.5);

    float3 color = CoreColor * core + GlowColor * glow * 0.7;
    float alpha = max(core, glow * 0.5) * blink;
    return float4(color * blink, alpha);
}
```

### Noise-Displaced Arc

Smoother, more organic than zigzag. Sample a 1D noise texture along the bolt's length with rapid scrolling:

```hlsl
float noiseVal = tex2D(NoiseSampler, float2(uv.y * 0.004 + Time * 2.0, 0.5)).r;
float displacement = (noiseVal - 0.5) * variation;
displacement *= 1.0 - pow(abs(uv.y * 2.0 - 1.0), 2.0); // taper at ends
float dist = abs(uv.x - 0.5 + displacement);
float core = 1.0 - smoothstep(0.0, lineWidth, dist);
float glow = exp(-dist * dist * glowFactor);
```

### Screen Flash

Post-processing overlay on lightning strike. Set `FlashIntensity` to 1.0 on strike, decay at **4.0 per second** in Update:

```hlsl
float FlashIntensity;
float3 FlashColor; // slightly blue-white: float3(0.9, 0.95, 1.0)

float4 FlashPS(VertexShaderOutput input) : COLOR
{
    float4 scene = tex2D(SpriteTextureSampler, input.TextureCoordinates);
    scene.rgb += FlashColor * FlashIntensity;
    return saturate(scene);
}
```

### Branching Lightning

Generate branch geometry on the CPU (recursive midpoint displacement algorithm) and render each branch as a separate sprite with the bolt shader, using different time offsets and reduced widths for sub-branches.

---

## Ice & Frost: Voronoi Crystals with Spreading Freeze

### Freezing Spread Effect

Frost growing outward from a point — distance from an origin combined with noise for an irregular edge. As `FreezeProgress` increases from 0 to 1, ice covers more of the screen:

```hlsl
float FreezeProgress;
float2 FreezeOrigin;
float4 IceTint;        // float4(0.7, 0.85, 1.0, 1.0)

texture FrostTexture;
sampler FrostSampler = sampler_state { Texture = <FrostTexture>; };
texture NoiseTexture;
sampler NoiseSampler = sampler_state { Texture = <NoiseTexture>; AddressU = Wrap; AddressV = Wrap; };

float4 FreezePS(VertexShaderOutput input) : COLOR
{
    float2 uv = input.TextureCoordinates;
    float4 scene = tex2D(SpriteTextureSampler, uv);

    float dist = length(uv - FreezeOrigin);
    float noise = tex2D(NoiseSampler, uv * 3.0).r * 0.15;
    float freezeRadius = FreezeProgress * 1.5;
    float freezeMask = 1.0 - smoothstep(freezeRadius - 0.1, freezeRadius + noise, dist);

    float4 frost = tex2D(FrostSampler, uv * 2.0);
    float gray = dot(scene.rgb, float3(0.299, 0.587, 0.114));
    float4 frozen = lerp(float4(gray, gray, gray, 1), IceTint, 0.5) + frost * 0.3;

    return lerp(scene, frozen, freezeMask);
}
```

### Ice Crystal Patterns

Use the Voronoi function from the Procedural Noise Primitives section. The `F2 - F1` value produces sharp cell borders that look like ice crack lines, while F1 gives smooth cell interiors:

```hlsl
float4 IceCrystalPS(VertexShaderOutput input) : COLOR
{
    float2 v = Voronoi(input.TextureCoordinates * 8.0);
    float edges = v.y - v.x;
    float border = 1.0 - smoothstep(0.0, 0.05, edges);
    float3 ice = lerp(float3(0.7, 0.85, 1.0), float3(1, 1, 1), border);
    ice += v.x * 0.2;
    return float4(ice, 1.0);
}
```

### Cracking Ice

Combine Voronoi borders with an expanding reveal mask. Each Voronoi cell gets a random delay (hashed from cell ID) so cracks don't all appear simultaneously — they propagate outward from the impact point with staggered timing.

---

## Post-Processing & Screen Effects

### Render Target Pattern

The backbone of all screen-space effects. Draw your entire scene to a `RenderTarget2D`, then draw that target to the back buffer with a shader applied:

```csharp
// Create once in LoadContent
sceneTarget = new RenderTarget2D(GraphicsDevice,
    GraphicsDevice.PresentationParameters.BackBufferWidth,
    GraphicsDevice.PresentationParameters.BackBufferHeight);

// In Draw()
GraphicsDevice.SetRenderTarget(sceneTarget);
GraphicsDevice.Clear(Color.Black);
DrawEntireScene(); // all normal drawing

GraphicsDevice.SetRenderTarget(null);
postProcessEffect.Parameters["Time"].SetValue(time);
spriteBatch.Begin(effect: postProcessEffect, samplerState: SamplerState.PointClamp);
spriteBatch.Draw(sceneTarget, Vector2.Zero, Color.White);
spriteBatch.End();
```

See [G1 Custom Code Recipes](./G1_custom_code_recipes.md) for the multi-pass post-processor pipeline.

### Bloom

Standard multi-pass pipeline for 2D pixel art: extract bright pixels above a luminance threshold, Gaussian blur at half resolution (two passes — horizontal then vertical), then additively blend the blurred result over the original scene. The Kosmonaut3d BloomFilter library provides a production-ready implementation. For a simpler approach, the Nez framework includes `BloomExtract.fx`, `GaussianBlur.fx`, and `BloomCombine.fx` as ready-to-use `.fx` files.

### Palette Manipulation

The cleanest technique uses a **lookup texture** — a 256x1 gradient where each horizontal position maps an input luminance to an output color. Swap the palette texture for day/night cycles, damage effects, or elemental coloring:

```hlsl
texture PaletteTexture;
sampler PaletteSampler = sampler_state {
    Texture = <PaletteTexture>;
    MinFilter = Point; MagFilter = Point;
    AddressU = Clamp; AddressV = Clamp;
};

float4 PalettePS(VertexShaderOutput input) : COLOR
{
    float4 color = tex2D(SpriteTextureSampler, input.TextureCoordinates);
    float luma = dot(color.rgb, float3(0.299, 0.587, 0.114));
    float4 mapped = tex2D(PaletteSampler, float2(luma, 0.5));
    mapped.a = color.a;
    return mapped;
}
```

### Dithering

A **4x4 ordered Bayer matrix** with limited color quantization matches the aesthetic of classic 16-bit games.

### Flash White

Damage indicator ubiquitous in pixel-art action games — a single `lerp` between the original color and premultiplied white:

```hlsl
float WhiteAmount;

float4 FlashWhitePS(VertexShaderOutput input) : COLOR
{
    float4 color = tex2D(SpriteTextureSampler, input.TextureCoordinates);
    color.rgb = lerp(color.rgb, float3(1, 1, 1) * color.a, WhiteAmount);
    return color;
}
```

---

## Performance & Batching

### SpriteBatch Batching

In `SpriteSortMode.Deferred` (the default and most efficient mode), SpriteBatch collects all draws and flushes them as minimal GPU draw calls in `End()`. **Every time you change the effect, you must call `End()` and `Begin()` again**, which creates a batch break. Organize your draw order by effect: draw all entities that share the same shader together.

A practical batching helper avoids manual Begin/End management:

```csharp
Effect currentEffect = null;

void DrawSprite(Texture2D sprite, Vector2 pos, Effect effect = null)
{
    if (currentEffect != effect) {
        spriteBatch.End();
        currentEffect = effect;
        spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend,
                         SamplerState.PointClamp, effect: currentEffect);
    }
    spriteBatch.Draw(sprite, pos, Color.White);
}
```

### Vertex Color Encoding

For per-entity shader variation without batch breaks, encode data in the vertex color channel. The `Color` parameter in `SpriteBatch.Draw()` is accessible as `input.Color` in the shader — encode effect intensity in the alpha, or use color channels as flags. An alternative "uber-shader" puts all elemental effects in one shader, switched by a parameter encoded in the vertex color.

### Render Target Management

Create targets once in `LoadContent`, never per-frame. Dispose them manually (they aren't managed by `ContentManager`). For expensive blur passes, **render at half or quarter resolution** — bloom at half-res is visually indistinguishable from full-res and runs 4x faster. For pixel art, render the entire game at native resolution (e.g., **320x180**) to a small render target, then upscale to the window with `SamplerState.PointClamp`.

### When to Avoid Shaders

- **Frame-by-frame sprite animation** — cheaper and more art-directable for discrete effects like explosions
- **Particle systems** — excel when you need hundreds of individual moving objects (sparks, rain). See [G23 Particles](./G23_particles.md)
- **Shaders are ideal for** — per-pixel color manipulation, screen-wide effects, continuous animated distortion, anything requiring too many sprites

### ECS Integration

For Arch ECS architecture: screen-wide effects (bloom, color grading, screen shake) belong in a dedicated rendering system, while per-entity effects (flash white, elemental aura) attach as components that the rendering system reads to select the appropriate shader per batch.

---

## Resources & Shadertoy Conversion

### Essential Repositories

| Repository | What It Provides |
|-----------|-----------------|
| **Nez** (`prime31/Nez`) | Largest collection of production `.fx` files: bloom, heat distortion, palette cycling, dissolve, pixel glitch, vignette, scanlines, deferred lighting. Extract from `DefaultContentSource/effects/`. OpenGL only. |
| **manbeardgames/monogame-hlsl-examples** | Best starting tutorial — four commented example projects covering basic shaders, parameter passing, multi-texture blending, 2D lighting |
| **Kosmonaut3d/BloomFilter-for-Monogame-and-XNA** | High-quality bloom based on UE4 techniques. One C# file + one `.fx`. Supports DirectX and OpenGL. |
| **Penumbra** (`discosultan/penumbra`) | 2D lighting with soft shadows — point/spotlights, hull-based shadow casting. NuGet: `MonoGame.Penumbra.DesktopGL` |
| **MonoGame official shader tutorial** | `docs.monogame.net` grayscale example. Community forum "Pixel Shader Examples" thread is a curated master list. |

### Shadertoy-to-MonoGame HLSL Mapping

| Shadertoy (GLSL) | MonoGame (HLSL) |
|------------------|-----------------|
| `vec2/3/4` | `float2/3/4` |
| `mix()` | `lerp()` |
| `texture()` | `tex2D()` |
| `fract()` | `frac()` |
| `mod()` | `fmod()` |
| `atan(x, y)` | `atan2(y, x)` (swapped!) |
| `iTime` | `float Time` parameter from C# |
| `iResolution` | `float2 Resolution` parameter from C# |
