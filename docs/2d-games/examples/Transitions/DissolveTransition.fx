// =============================================================================
// DissolveTransition.fx — HLSL dissolve shader for screen transitions
// Extracted from: G42 — Screen Transitions & Loading Screens (Section 5.1)
// Guide: /G/G42_screen_transitions.md
//
// A noise texture determines which pixels disappear first. As Progress
// increases from 0 to 1, pixels whose noise value falls below the threshold
// are replaced with the new scene. An optional edge glow adds visual flair
// (fire, magic, etc.) along the dissolve boundary.
//
// Usage:
//   - Set Progress from 0 (fully visible) to 1 (fully dissolved).
//   - Set NoiseTexture to a tileable noise image (Perlin, Worley, etc.).
//   - EdgeWidth controls the width of the glowing dissolve edge (0.02–0.1).
//   - EdgeColor sets the glow color (e.g., orange for fire, white for magic).
// =============================================================================

sampler SceneSampler : register(s0);

texture NoiseTexture;
sampler NoiseSampler = sampler_state
{
    Texture   = <NoiseTexture>;
    MinFilter = Linear;
    MagFilter = Linear;
    AddressU  = Wrap;
    AddressV  = Wrap;
};

// 0 = old scene fully visible, 1 = fully dissolved
float Progress;

// Width of the dissolve edge glow (0.02 - 0.1 recommended)
float EdgeWidth;

// Color of the dissolve edge (e.g., float3(1.0, 0.4, 0.1) for orange glow)
float3 EdgeColor;


float4 MainPS(float2 texCoord : TEXCOORD0) : COLOR0
{
    float4 sceneColor = tex2D(SceneSampler, texCoord);
    float  noiseVal   = tex2D(NoiseSampler, texCoord).r;

    float threshold = Progress;

    // Fully dissolved — transparent so the new scene shows through
    if (noiseVal < threshold - EdgeWidth)
        return float4(0, 0, 0, 0);

    // Edge glow region — lerp between edge color and scene color
    if (noiseVal < threshold)
    {
        float edgeFactor = 1.0 - (threshold - noiseVal) / EdgeWidth;
        return float4(lerp(EdgeColor, sceneColor.rgb, edgeFactor), 1.0);
    }

    // Not yet dissolved — render scene normally
    return sceneColor;
}


technique Dissolve
{
    pass P0
    {
        PixelShader = compile ps_3_0 MainPS();
    }
}
