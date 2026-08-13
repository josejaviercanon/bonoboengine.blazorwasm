// ============================================================================
// LightComposite.fx — HLSL Lightmap Composite + Spot Light Shaders
// Extracted from: G39 — 2D Lighting & Shadows
// Part of: Universal 2D Engine Toolkit Examples
//
// Contains two techniques:
//   1. LightmapComposite — Multiplies a lightmap over the scene texture
//   2. SpotLight — Clips a radial gradient to a cone shape
//
// Usage: Load via Content.Load<Effect>("effects/LightComposite")
// ============================================================================

#if OPENGL
    #define SV_POSITION POSITION
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0_level_9_1
    #define PS_SHADERMODEL ps_4_0_level_9_1
#endif

// ═══════════════════════════════════════════════════════════════════════════
// TECHNIQUE 1: Lightmap Composite
//
// Multiplies the lightmap over the scene for final output.
// Use when you need more control than BlendState multiply
// (e.g., gamma correction, HDR tone-mapping).
//
// Bind: SceneTexture = your rendered scene RT
//       LightmapTexture = your lightmap RT
// Draw: a fullscreen quad with this effect applied
// ═══════════════════════════════════════════════════════════════════════════

Texture2D SceneTexture;
sampler2D SceneSampler = sampler_state
{
    Texture = <SceneTexture>;
    MinFilter = Point;
    MagFilter = Point;
    MipFilter = Point;
};

Texture2D LightmapTexture;
sampler2D LightmapSampler = sampler_state
{
    Texture = <LightmapTexture>;
    MinFilter = Linear;   // Lightmaps can be lower res, linear is fine
    MagFilter = Linear;
    MipFilter = Linear;
};

float4 CompositePS(float4 pos : SV_POSITION, float4 color : COLOR0,
                   float2 uv : TEXCOORD0) : COLOR
{
    float4 scene = tex2D(SceneSampler, uv);
    float4 light = tex2D(LightmapSampler, uv);

    // Core operation: multiply scene by lightmap
    // Black lightmap = full darkness, white = full brightness
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


// ═══════════════════════════════════════════════════════════════════════════
// TECHNIQUE 2: Spot Light
//
// Clips a radial gradient texture to a cone shape defined by
// a direction vector and inner/outer cone angles.
//
// Bind: SpriteTexture = radial gradient texture
//       LightDirection = normalized 2D direction vector
//       InnerAngleCos = cos(innerHalfAngle)
//       OuterAngleCos = cos(outerHalfAngle)
// Draw: as an additive-blended sprite on the lightmap
// ═══════════════════════════════════════════════════════════════════════════

Texture2D SpriteTexture;
sampler2D SpriteTextureSampler = sampler_state
{
    Texture = <SpriteTexture>;
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Linear;
};

// Spotlight parameters
float2 LightDirection;   // Normalized direction vector the spot points toward
float InnerAngleCos;     // cos(innerAngle / 2) — full intensity inside this cone
float OuterAngleCos;     // cos(outerAngle / 2) — fades to zero at this cone edge

float4 SpotLightPS(float4 pos : SV_POSITION, float4 color : COLOR0,
                   float2 uv : TEXCOORD0) : COLOR
{
    float4 texColor = tex2D(SpriteTextureSampler, uv);

    // Convert UV 0..1 to centered -1..1 coordinate space
    float2 offset = uv * 2.0 - 1.0;
    float dist = length(offset);

    // Discard pixels outside the light's radius
    if (dist > 1.0)
        return float4(0, 0, 0, 0);

    // Direction from center to this pixel (normalized)
    float2 dir = normalize(offset);

    // Angle between light direction and pixel direction
    float angleCos = dot(dir, LightDirection);

    // Cone attenuation: smoothstep from outer edge to inner cone
    // Inside inner cone = 1.0 (full intensity)
    // Between inner and outer = smooth falloff
    // Outside outer cone = 0.0
    float coneAtten = smoothstep(OuterAngleCos, InnerAngleCos, angleCos);

    // Radial falloff (quadratic for natural look)
    float radialAtten = (1.0 - dist) * (1.0 - dist);

    // Combined attenuation
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
