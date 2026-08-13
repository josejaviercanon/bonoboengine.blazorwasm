// =============================================================================
// FogRenderer.cs — Fog of war rendering with blur and composite passes
// Extracted from: G54 — Fog of War & Visibility Systems (Sections 5–6)
// Guide: /G/G54_fog_of_war.md
// =============================================================================

using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace U2DToolkit.Examples.FogOfWar
{
    /// <summary>
    /// Renders fog of war as a smooth overlay on top of the world.
    /// <para>
    /// Pipeline:
    /// 1. Build a low-resolution fog texture (one pixel per tile) from <see cref="FogGrid"/>.
    /// 2. Apply a two-pass Gaussian blur for smooth tile-boundary transitions.
    /// 3. Composite the blurred fog over the world render using a fog shader
    ///    that darkens unexplored areas and desaturates explored areas.
    /// </para>
    /// <para>
    /// Render order:
    /// <code>
    /// Draw world → Build fog texture → Blur → Composite fog over world → Draw HUD
    /// </code>
    /// </para>
    /// </summary>
    public class FogRenderer : IDisposable
    {
        private RenderTarget2D _fogTarget;
        private RenderTarget2D _fogBlurred;
        private readonly Effect _blurEffect;
        private readonly Effect _fogComposite;
        private readonly int _tileSize;

        /// <param name="gd">Graphics device.</param>
        /// <param name="mapWidth">Map width in tiles.</param>
        /// <param name="mapHeight">Map height in tiles.</param>
        /// <param name="tileSize">Pixel size of one tile.</param>
        /// <param name="blurEffect">Compiled Gaussian blur shader (FogBlur.fx).</param>
        /// <param name="fogComposite">Compiled fog composite shader (FogComposite.fx).</param>
        public FogRenderer(GraphicsDevice gd, int mapWidth, int mapHeight,
                           int tileSize, Effect blurEffect, Effect fogComposite)
        {
            _tileSize = tileSize;
            // One pixel per tile for the fog mask
            _fogTarget  = new RenderTarget2D(gd, mapWidth, mapHeight,
                false, SurfaceFormat.Color, DepthFormat.None);
            _fogBlurred = new RenderTarget2D(gd, mapWidth, mapHeight,
                false, SurfaceFormat.Color, DepthFormat.None);
            _blurEffect    = blurEffect;
            _fogComposite  = fogComposite;
        }

        /// <summary>
        /// Build the raw fog texture from the <see cref="FogGrid"/> state.
        /// Encodes visibility into the red channel:
        /// 0 = unexplored (black), 128 = explored (dimmed), 255 = visible (full).
        /// </summary>
        public void BuildFogTexture(GraphicsDevice gd, FogGrid fog)
        {
            var pixels = new Color[fog.Width * fog.Height];
            for (int y = 0; y < fog.Height; y++)
            for (int x = 0; x < fog.Width;  x++)
            {
                var state = fog[x, y];
                byte val = state switch
                {
                    VisibilityState.Visible  => 255,
                    VisibilityState.Explored => 128,
                    _                        => 0
                };
                pixels[y * fog.Width + x] = new Color(val, val, val, 255);
            }
            _fogTarget.SetData(pixels);
        }

        /// <summary>
        /// Apply a two-pass separable Gaussian blur to soften tile edges.
        /// Pass 1: horizontal → _fogBlurred. Pass 2: vertical → _fogTarget (ping-pong).
        /// </summary>
        public void BlurFog(GraphicsDevice gd, SpriteBatch sb)
        {
            // Horizontal pass → _fogBlurred
            gd.SetRenderTarget(_fogBlurred);
            gd.Clear(Color.Black);
            _blurEffect.Parameters["TexelSize"]?.SetValue(
                new Vector2(1f / _fogTarget.Width, 0));
            sb.Begin(effect: _blurEffect, samplerState: SamplerState.LinearClamp);
            sb.Draw(_fogTarget, _fogTarget.Bounds, Color.White);
            sb.End();

            // Vertical pass → _fogTarget (ping-pong)
            gd.SetRenderTarget(_fogTarget);
            gd.Clear(Color.Black);
            _blurEffect.Parameters["TexelSize"]?.SetValue(
                new Vector2(0, 1f / _fogBlurred.Height));
            sb.Begin(effect: _blurEffect, samplerState: SamplerState.LinearClamp);
            sb.Draw(_fogBlurred, _fogBlurred.Bounds, Color.White);
            sb.End();

            gd.SetRenderTarget(null);
        }

        /// <summary>
        /// Draw the fog overlay on top of the world using the composite shader.
        /// The shader handles desaturation for explored areas and blackout
        /// for unexplored areas.
        /// </summary>
        public void DrawFog(SpriteBatch sb, Rectangle worldBounds)
        {
            sb.Begin(
                effect: _fogComposite,
                blendState: BlendState.AlphaBlend,
                samplerState: SamplerState.LinearClamp);
            sb.Draw(_fogTarget, worldBounds, Color.White);
            sb.End();
        }

        public void Dispose()
        {
            _fogTarget?.Dispose();
            _fogBlurred?.Dispose();
        }
    }

    // =========================================================================
    // HLSL Shader Reference (for Content Pipeline)
    // =========================================================================
    //
    // --- FogBlur.fx ---
    // Simple separable Gaussian blur:
    //
    //   sampler TextureSampler : register(s0);
    //   float2 TexelSize; // (1/width, 0) horizontal, (0, 1/height) vertical
    //
    //   static const float Weights[5] = { 0.227027, 0.194596, 0.121622, 0.054054, 0.016216 };
    //   static const float Offsets[5] = { 0.0, 1.0, 2.0, 3.0, 4.0 };
    //
    //   float4 PS_Blur(float2 texCoord : TEXCOORD0) : COLOR0
    //   {
    //       float4 color = tex2D(TextureSampler, texCoord) * Weights[0];
    //       for (int i = 1; i < 5; i++)
    //       {
    //           float2 offset = TexelSize * Offsets[i];
    //           color += tex2D(TextureSampler, texCoord + offset) * Weights[i];
    //           color += tex2D(TextureSampler, texCoord - offset) * Weights[i];
    //       }
    //       return color;
    //   }
    //
    // --- FogComposite.fx ---
    // Applies fog of war over the scene:
    //
    //   sampler SceneSampler : register(s0);
    //   texture FogTexture;
    //   float ExploredBrightness; // e.g., 0.35
    //   float ExploredSaturation; // e.g., 0.3
    //
    //   float4 PS_FogComposite(float2 texCoord : TEXCOORD0) : COLOR0
    //   {
    //       float4 scene = tex2D(SceneSampler, texCoord);
    //       float fog = tex2D(FogSampler, texCoord).r;
    //       if (fog > 0.75) return scene;                    // Visible
    //       if (fog > 0.25) { /* desaturate + darken */ }     // Explored
    //       return float4(0, 0, 0, scene.a);                  // Unexplored
    //   }
    //
    // See G54 Section 6 for the full shader source.
    // =========================================================================
}
