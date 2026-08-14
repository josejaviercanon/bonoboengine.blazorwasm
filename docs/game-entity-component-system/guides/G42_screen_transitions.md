# G42 — Screen Transitions & Loading Screens



> **Category:** Guide · **Related:** [G38 Scene Management](./G38_scene_management.md) · [G27 Shaders & Visual Effects](./G27_shaders_and_effects.md) · [G41 Tweening & Easing](./G41_tweening.md) · [G26 Resource Loading & Caching](./G26_resource_loading_caching.md)

---

## Overview

Screen transitions are the glue between scenes. A hard cut from gameplay to a menu feels jarring; a half-second fade feels intentional. This guide builds a **TransitionManager** that captures render targets from old and new scenes, runs animated transitions between them, and supports async loading screens for heavier scene swaps.

Everything here targets **MonoGame.Framework.DesktopGL** with **Arch ECS v2.1.0** and integrates with the scene lifecycle from [G38](./G38_scene_management.md).

### Simpler Alternative: Progress-Based Transitions

For many games, the 5-phase lifecycle below is more machinery than needed. A simpler model uses a polymorphic base class with a single 0-to-1 progress value — subclasses override `Draw` to composite two render targets:

```csharp
public abstract class SceneTransition
{
    protected float Elapsed;
    protected float Duration;

    public bool IsComplete => Elapsed >= Duration;
    public float Progress => Duration > 0f ? Math.Clamp(Elapsed / Duration, 0f, 1f) : 1f;

    protected SceneTransition(float duration) => Duration = duration;

    public bool Update(float dt) { Elapsed += dt; return IsComplete; }

    /// <summary>Draw the transition composite from two scene RTs.</summary>
    public abstract void Draw(SpriteBatch batch, Texture2D fromRT, Texture2D toRT,
        Rectangle destRect, Texture2D pixel);
}
```

Concrete transitions are trivially short:

```csharp
public class FadeBlackTransition : SceneTransition
{
    public FadeBlackTransition(float duration = 0.6f) : base(duration) { }

    public override void Draw(SpriteBatch batch, Texture2D fromRT, Texture2D toRT,
        Rectangle destRect, Texture2D pixel)
    {
        float t = Progress;
        float fadeOut = Math.Clamp(t * 2f, 0f, 1f);
        float fadeIn = Math.Clamp(t * 2f - 1f, 0f, 1f);

        batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        if (t < 0.5f)
        {
            batch.Draw(fromRT, destRect, Color.White);
            batch.Draw(pixel, destRect, Color.Black * fadeOut);
        }
        else
        {
            batch.Draw(toRT, destRect, Color.White);
            batch.Draw(pixel, destRect, Color.Black * (1f - fadeIn));
        }
        batch.End();
    }
}
```

The SceneManager renders both scenes to separate RTs each frame during the transition, then hands them to the transition's `Draw`. Start with this model; graduate to the full lifecycle below when you need loading screens or async scene swaps.

> **Critical: Temporal sync during transitions.** Both the outgoing and incoming scenes must receive `Update(dt)` every frame while a transition is active. If only the new scene updates, its simulation jumps forward while the old scene's render target shows stale state — visible as hitching in crossfades or mismatched physics in wipes.

---

## 1 — Transition Architecture

### 1.1 Lifecycle

Every transition follows five phases:

```
Start → AnimateOut (old scene) → Swap → AnimateIn (new scene) → Complete
```

| Phase        | What Happens                                              |
| ------------ | --------------------------------------------------------- |
| **Start**    | Capture old scene to a RenderTarget. Freeze old scene.    |
| **AnimateOut** | Draw old scene RT + overlay effect (fade, wipe, etc.). |
| **Swap**     | Unload old scene, load new scene. Capture new scene RT.   |
| **AnimateIn**  | Draw new scene RT + overlay effect reversing in.        |
| **Complete** | Hand control to new scene. Release RenderTargets.         |

### 1.2 Core Types

```csharp
public enum TransitionPhase
{
    None,
    AnimateOut,
    Swap,
    AnimateIn,
    Complete
}

/// <summary>
/// Base class for all transition effects. Subclass to define visual behavior.
/// </summary>
public abstract class Transition
{
    public float Duration { get; set; } = 0.4f;
    public float Progress { get; private set; }
    public TransitionPhase Phase { get; private set; } = TransitionPhase.None;
    public bool IsComplete => Phase == TransitionPhase.Complete;

    protected GraphicsDevice Graphics { get; private set; } = null!;
    protected SpriteBatch SpriteBatch { get; private set; } = null!;

    public void Initialize(GraphicsDevice graphics, SpriteBatch spriteBatch)
    {
        Graphics = graphics;
        SpriteBatch = spriteBatch;
        OnInitialize();
    }

    public void Start() => Phase = TransitionPhase.AnimateOut;

    public void Update(float dt)
    {
        if (Phase is TransitionPhase.None or TransitionPhase.Complete)
            return;

        Progress += dt / Duration;

        if (Progress >= 1f)
        {
            Progress = 1f;

            if (Phase == TransitionPhase.AnimateOut)
            {
                Phase = TransitionPhase.Swap;
                Progress = 0f;
            }
            else if (Phase == TransitionPhase.AnimateIn)
            {
                Phase = TransitionPhase.Complete;
            }
        }
    }

    /// <summary>Advance past the swap phase after scene load finishes.</summary>
    public void BeginAnimateIn()
    {
        Phase = TransitionPhase.AnimateIn;
        Progress = 0f;
    }

    /// <summary>
    /// Draw the transition overlay. Called with the old scene RT during AnimateOut,
    /// new scene RT during AnimateIn, or both for crossfade-style effects.
    /// </summary>
    public abstract void Draw(
        RenderTarget2D? oldScene,
        RenderTarget2D? newScene,
        float progress,
        TransitionPhase phase
    );

    protected virtual void OnInitialize() { }

    public virtual void Dispose() { }
}
```

### 1.3 TransitionManager

The manager owns the lifecycle, captures render targets, and coordinates with the scene manager.

```csharp
public class TransitionManager
{
    private readonly GraphicsDevice _graphics;
    private readonly SpriteBatch _spriteBatch;

    private RenderTarget2D? _oldSceneRT;
    private RenderTarget2D? _newSceneRT;
    private Transition? _active;
    private Action? _onSwap;
    private Action? _onComplete;

    public bool IsTransitioning => _active != null && !_active.IsComplete;

    public TransitionManager(GraphicsDevice graphics, SpriteBatch spriteBatch)
    {
        _graphics = graphics;
        _spriteBatch = spriteBatch;
    }

    /// <summary>
    /// Begin a transition. onSwap is called at the midpoint to load the new scene.
    /// onComplete fires when the full transition ends.
    /// </summary>
    public void Start(Transition transition, Action onSwap, Action? onComplete = null)
    {
        if (IsTransitioning) return;

        _active = transition;
        _active.Initialize(_graphics, _spriteBatch);
        _onSwap = onSwap;
        _onComplete = onComplete;

        // Capture old scene — caller should have rendered current scene to backbuffer
        _oldSceneRT = CaptureBackbuffer();

        _active.Start();
    }

    public void Update(float dt)
    {
        if (_active == null) return;

        _active.Update(dt);

        if (_active.Phase == TransitionPhase.Swap)
        {
            // Execute scene swap (sync or kicks off async load)
            _onSwap?.Invoke();
            _onSwap = null;

            // For non-loading transitions, capture new scene immediately
            _newSceneRT = CaptureBackbuffer();
            _active.BeginAnimateIn();
        }

        if (_active.IsComplete)
        {
            _onComplete?.Invoke();
            Cleanup();
        }
    }

    public void Draw()
    {
        if (_active == null) return;
        _active.Draw(_oldSceneRT, _newSceneRT, _active.Progress, _active.Phase);
    }

    /// <summary>
    /// For async loading: call this once the new scene is ready
    /// so the manager can capture its RT and begin animating in.
    /// </summary>
    public void NotifyLoadComplete()
    {
        if (_active == null) return;
        _newSceneRT = CaptureBackbuffer();
        _active.BeginAnimateIn();
    }

    private RenderTarget2D CaptureBackbuffer()
    {
        var pp = _graphics.PresentationParameters;
        var rt = new RenderTarget2D(
            _graphics, pp.BackBufferWidth, pp.BackBufferHeight,
            false, SurfaceFormat.Color, DepthFormat.None
        );

        // Copy current backbuffer data
        var data = new Color[pp.BackBufferWidth * pp.BackBufferHeight];
        _graphics.GetBackBufferData(data);
        rt.SetData(data);
        return rt;
    }

    private void Cleanup()
    {
        _oldSceneRT?.Dispose();
        _newSceneRT?.Dispose();
        _oldSceneRT = null;
        _newSceneRT = null;
        _active?.Dispose();
        _active = null;
        _onSwap = null;
        _onComplete = null;
    }
}
```

> **Tip:** For smoother captures, render your scene to a dedicated `RenderTarget2D` every frame and hand that to the transition manager instead of reading back from the backbuffer.

---

## 2 — Fade Transition

The simplest and most common transition. Fades to a solid color, swaps the scene at full opacity, then fades the color back out to reveal the new scene.

```csharp
public class FadeTransition : Transition
{
    private Texture2D _pixel = null!;
    public Color FadeColor { get; set; } = Color.Black;

    protected override void OnInitialize()
    {
        _pixel = new Texture2D(Graphics, 1, 1);
        _pixel.SetData(new[] { Color.White });
    }

    public override void Draw(
        RenderTarget2D? oldScene, RenderTarget2D? newScene,
        float progress, TransitionPhase phase)
    {
        var viewport = Graphics.Viewport;

        SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);

        // Draw the underlying scene
        var sceneRT = phase == TransitionPhase.AnimateOut ? oldScene : newScene;
        if (sceneRT != null)
        {
            SpriteBatch.Draw(sceneRT, viewport.Bounds, Color.White);
        }

        // Overlay: alpha ramps up during AnimateOut, ramps down during AnimateIn
        float alpha = phase == TransitionPhase.AnimateOut
            ? EaseInOut(progress)
            : 1f - EaseInOut(progress);

        SpriteBatch.Draw(_pixel, viewport.Bounds, FadeColor * alpha);

        SpriteBatch.End();
    }

    private static float EaseInOut(float t)
    {
        // Smooth step for pleasant fade
        return t * t * (3f - 2f * t);
    }

    public override void Dispose()
    {
        _pixel?.Dispose();
    }
}
```

**Usage:**

```csharp
transitionManager.Start(
    new FadeTransition { Duration = 0.3f, FadeColor = Color.Black },
    onSwap: () => sceneManager.LoadScene<GameplayScene>()
);
```

---

## 3 — Crossfade

Blends the old and new scene render targets simultaneously. Requires both RTs so both scenes must be rendered before the blend begins. This variant captures the new scene during the swap phase and does a single-phase blend.

```csharp
public class CrossfadeTransition : Transition
{
    public override void Draw(
        RenderTarget2D? oldScene, RenderTarget2D? newScene,
        float progress, TransitionPhase phase)
    {
        var viewport = Graphics.Viewport;

        if (phase == TransitionPhase.AnimateOut)
        {
            // Still waiting for new scene — just show old
            SpriteBatch.Begin();
            if (oldScene != null)
                SpriteBatch.Draw(oldScene, viewport.Bounds, Color.White);
            SpriteBatch.End();
            return;
        }

        // AnimateIn: blend old → new
        float t = EaseInOut(progress);

        // Draw old scene at reducing opacity
        SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);
        if (oldScene != null)
            SpriteBatch.Draw(oldScene, viewport.Bounds, Color.White * (1f - t));
        SpriteBatch.End();

        // Draw new scene at increasing opacity (additive-ish via alpha)
        SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);
        if (newScene != null)
            SpriteBatch.Draw(newScene, viewport.Bounds, Color.White * t);
        SpriteBatch.End();
    }

    private static float EaseInOut(float t) => t * t * (3f - 2f * t);
}
```

> **Note:** For a true crossfade set the `Duration` on the `AnimateOut` phase to something very short (e.g., 0.01f) so it effectively becomes a single-phase blend during `AnimateIn`.

---

## 4 — Wipe Transitions

Wipes reveal the new scene by sliding a boundary across the screen.

### 4.1 Directional Wipe (Horizontal / Vertical / Diagonal)

```csharp
public enum WipeDirection { Left, Right, Up, Down, DiagonalTL, DiagonalBR }

public class WipeTransition : Transition
{
    private Texture2D _pixel = null!;
    public WipeDirection Direction { get; set; } = WipeDirection.Left;

    protected override void OnInitialize()
    {
        _pixel = new Texture2D(Graphics, 1, 1);
        _pixel.SetData(new[] { Color.White });
    }

    public override void Draw(
        RenderTarget2D? oldScene, RenderTarget2D? newScene,
        float progress, TransitionPhase phase)
    {
        var vp = Graphics.Viewport;
        float t = phase == TransitionPhase.AnimateOut ? progress : progress;

        // During AnimateOut: old scene gets wiped away revealing black
        // During AnimateIn: black gets wiped away revealing new scene
        var beneath = phase == TransitionPhase.AnimateOut ? null : newScene;
        var above = phase == TransitionPhase.AnimateOut ? oldScene : null;

        // Draw underneath layer (new scene or black)
        SpriteBatch.Begin();
        if (beneath != null)
            SpriteBatch.Draw(beneath, vp.Bounds, Color.White);
        else
            Graphics.Clear(Color.Black);
        SpriteBatch.End();

        // Draw top layer clipped by wipe region
        if (above != null)
        {
            var clipRect = ComputeClipRect(vp, t, Direction);
            SpriteBatch.Begin(
                SpriteSortMode.Deferred, BlendState.AlphaBlend,
                null, null, new RasterizerState { ScissorTestEnable = true }
            );
            Graphics.ScissorRectangle = clipRect;
            SpriteBatch.Draw(above, vp.Bounds, Color.White);
            SpriteBatch.End();
        }
    }

    private static Rectangle ComputeClipRect(Viewport vp, float t, WipeDirection dir)
    {
        int w = vp.Width, h = vp.Height;
        float remaining = 1f - t; // how much of the old scene is still visible

        return dir switch
        {
            WipeDirection.Left    => new Rectangle(0, 0, (int)(w * remaining), h),
            WipeDirection.Right   => new Rectangle((int)(w * t), 0, (int)(w * remaining), h),
            WipeDirection.Up      => new Rectangle(0, 0, w, (int)(h * remaining)),
            WipeDirection.Down    => new Rectangle(0, (int)(h * t), w, (int)(h * remaining)),
            _ => new Rectangle(0, 0, w, h),
        };
    }

    public override void Dispose() => _pixel?.Dispose();
}
```

### 4.2 Circle Wipe (Iris In/Out)

Classic Mario-style circle wipe. Uses a shader for a clean circular mask.

```csharp
public class CircleWipeTransition : Transition
{
    private Effect? _shader;
    public bool IrisOut { get; set; } = true; // true = shrinks to point, then expands

    protected override void OnInitialize()
    {
        // Load the circle wipe shader (see HLSL below)
        // _shader = content.Load<Effect>("Shaders/CircleWipe");
    }

    public override void Draw(
        RenderTarget2D? oldScene, RenderTarget2D? newScene,
        float progress, TransitionPhase phase)
    {
        var vp = Graphics.Viewport;
        var scene = phase == TransitionPhase.AnimateOut ? oldScene : newScene;
        if (scene == null) return;

        float radius;
        if (phase == TransitionPhase.AnimateOut)
            radius = IrisOut ? 1f - progress : progress; // shrink or grow
        else
            radius = IrisOut ? progress : 1f - progress;

        if (_shader != null)
        {
            _shader.Parameters["Progress"]?.SetValue(radius);
            _shader.Parameters["AspectRatio"]?.SetValue((float)vp.Width / vp.Height);
        }

        SpriteBatch.Begin(
            SpriteSortMode.Deferred, BlendState.AlphaBlend,
            null, null, null, _shader
        );
        SpriteBatch.Draw(scene, vp.Bounds, Color.White);
        SpriteBatch.End();
    }
}
```

**CircleWipe.fx (HLSL):**

```hlsl
sampler TextureSampler : register(s0);

float Progress;    // 0 = fully hidden, 1 = fully visible
float AspectRatio; // width / height

float4 MainPS(float2 texCoord : TEXCOORD0) : COLOR0
{
    float4 color = tex2D(TextureSampler, texCoord);

    // Center the coordinates
    float2 center = float2(0.5, 0.5);
    float2 uv = texCoord - center;
    uv.x *= AspectRatio; // correct for aspect ratio

    float dist = length(uv);
    float maxDist = length(float2(0.5 * AspectRatio, 0.5));
    float normalizedDist = dist / maxDist;

    // Discard pixels outside the circle
    float threshold = Progress;
    if (normalizedDist > threshold)
        return float4(0, 0, 0, 1); // black outside

    return color;
}

technique CircleWipe
{
    pass P0
    {
        PixelShader = compile ps_3_0 MainPS();
    }
}
```

### 4.3 Diamond Wipe

Same concept as circle wipe but uses Manhattan distance for a diamond shape.

```hlsl
// DiamondWipe.fx
sampler TextureSampler : register(s0);
float Progress;
float AspectRatio;

float4 MainPS(float2 texCoord : TEXCOORD0) : COLOR0
{
    float4 color = tex2D(TextureSampler, texCoord);

    float2 uv = texCoord - float2(0.5, 0.5);
    uv.x *= AspectRatio;

    // Manhattan distance = diamond shape
    float dist = abs(uv.x) + abs(uv.y);
    float maxDist = 0.5 * AspectRatio + 0.5;

    if (dist / maxDist > Progress)
        return float4(0, 0, 0, 1);

    return color;
}

technique DiamondWipe
{
    pass P0
    {
        PixelShader = compile ps_3_0 MainPS();
    }
}
```

---

## 5 — Shader-Based Transitions

### 5.1 Dissolve Transition (Noise Texture)

A noise texture determines which pixels disappear first. As `Progress` increases from 0→1, pixels whose noise value falls below the threshold are replaced with the new scene.

**Dissolve.fx:**

```hlsl
sampler SceneSampler : register(s0);
texture NoiseTexture;

sampler NoiseSampler = sampler_state
{
    Texture = <NoiseTexture>;
    MinFilter = Linear;
    MagFilter = Linear;
    AddressU  = Wrap;
    AddressV  = Wrap;
};

float Progress;     // 0 = old scene fully visible, 1 = fully dissolved
float EdgeWidth;    // width of the dissolve edge glow (0.02 - 0.1)
float3 EdgeColor;   // color of the dissolve edge (e.g., orange glow)

float4 MainPS(float2 texCoord : TEXCOORD0) : COLOR0
{
    float4 sceneColor = tex2D(SceneSampler, texCoord);
    float noiseVal = tex2D(NoiseSampler, texCoord).r;

    float threshold = Progress;

    // Fully dissolved
    if (noiseVal < threshold - EdgeWidth)
        return float4(0, 0, 0, 0); // transparent — new scene shows through

    // Edge glow region
    if (noiseVal < threshold)
    {
        float edgeFactor = 1.0 - (threshold - noiseVal) / EdgeWidth;
        return float4(lerp(EdgeColor, sceneColor.rgb, edgeFactor), 1.0);
    }

    // Not yet dissolved
    return sceneColor;
}

technique Dissolve
{
    pass P0
    {
        PixelShader = compile ps_3_0 MainPS();
    }
}
```

**C# wrapper:**

```csharp
public class DissolveTransition : Transition
{
    private Effect? _shader;
    private Texture2D? _noiseTexture;

    public Color EdgeColor { get; set; } = Color.OrangeRed;
    public float EdgeWidth { get; set; } = 0.05f;

    public DissolveTransition(Effect shader, Texture2D noiseTexture)
    {
        _shader = shader;
        _noiseTexture = noiseTexture;
    }

    public override void Draw(
        RenderTarget2D? oldScene, RenderTarget2D? newScene,
        float progress, TransitionPhase phase)
    {
        var vp = Graphics.Viewport;

        // Determine which scene dissolves away and which is revealed
        RenderTarget2D? dissolving = phase == TransitionPhase.AnimateOut ? oldScene : null;
        RenderTarget2D? revealed  = phase == TransitionPhase.AnimateIn  ? newScene : null;
        float p = progress;

        // During AnimateOut, the old scene dissolves to black
        // During AnimateIn, black dissolves to reveal new scene
        Graphics.Clear(Color.Black);

        if (revealed != null)
        {
            // Draw new scene underneath (fully visible)
            SpriteBatch.Begin();
            SpriteBatch.Draw(revealed, vp.Bounds, Color.White);
            SpriteBatch.End();

            // Dissolve overlay: invert progress so new scene is revealed
            dissolving = oldScene; // draw old scene dissolving on top
            p = progress;
        }

        if (dissolving != null && _shader != null)
        {
            _shader.Parameters["Progress"]?.SetValue(p);
            _shader.Parameters["EdgeWidth"]?.SetValue(EdgeWidth);
            _shader.Parameters["EdgeColor"]?.SetValue(EdgeColor.ToVector3());
            _shader.Parameters["NoiseTexture"]?.SetValue(_noiseTexture);

            SpriteBatch.Begin(
                SpriteSortMode.Deferred, BlendState.AlphaBlend,
                null, null, null, _shader
            );
            SpriteBatch.Draw(dissolving, vp.Bounds, Color.White);
            SpriteBatch.End();
        }
    }
}
```

### 5.2 Pixelate Transition

Increases pixel block size over time, creating a mosaic effect. At peak pixelation the scene swaps, then resolution increases back to normal.

**Pixelate.fx:**

```hlsl
sampler TextureSampler : register(s0);

float Progress;       // 0 = no pixelation, 1 = max pixelation
float2 ScreenSize;    // viewport width, height
float MaxBlockSize;   // maximum pixel block size (e.g., 32)

float4 MainPS(float2 texCoord : TEXCOORD0) : COLOR0
{
    float blockSize = max(1.0, floor(lerp(1.0, MaxBlockSize, Progress)));

    // Snap UV to block grid
    float2 blockUV = floor(texCoord * ScreenSize / blockSize) * blockSize / ScreenSize;

    return tex2D(TextureSampler, blockUV);
}

technique Pixelate
{
    pass P0
    {
        PixelShader = compile ps_3_0 MainPS();
    }
}
```

**C# wrapper:**

```csharp
public class PixelateTransition : Transition
{
    private Effect? _shader;
    public float MaxBlockSize { get; set; } = 32f;

    public PixelateTransition(Effect shader) => _shader = shader;

    public override void Draw(
        RenderTarget2D? oldScene, RenderTarget2D? newScene,
        float progress, TransitionPhase phase)
    {
        var vp = Graphics.Viewport;
        var scene = phase == TransitionPhase.AnimateOut ? oldScene : newScene;
        if (scene == null || _shader == null) return;

        _shader.Parameters["Progress"]?.SetValue(progress);
        _shader.Parameters["ScreenSize"]?.SetValue(new Vector2(vp.Width, vp.Height));
        _shader.Parameters["MaxBlockSize"]?.SetValue(MaxBlockSize);

        SpriteBatch.Begin(
            SpriteSortMode.Deferred, BlendState.Opaque,
            SamplerState.PointClamp, null, null, _shader
        );
        SpriteBatch.Draw(scene, vp.Bounds, Color.White);
        SpriteBatch.End();
    }
}
```

### 5.3 Custom Shader Transitions (Generic Wrapper)

Any effect with a `Progress` uniform (0→1) can be plugged in:

```csharp
public class ShaderTransition : Transition
{
    private readonly Effect _effect;
    private readonly Action<Effect, float>? _configure;

    /// <param name="effect">Compiled effect with a "Progress" parameter.</param>
    /// <param name="configure">Optional extra parameter setup each frame.</param>
    public ShaderTransition(Effect effect, Action<Effect, float>? configure = null)
    {
        _effect = effect;
        _configure = configure;
    }

    public override void Draw(
        RenderTarget2D? oldScene, RenderTarget2D? newScene,
        float progress, TransitionPhase phase)
    {
        var vp = Graphics.Viewport;
        var scene = phase == TransitionPhase.AnimateOut ? oldScene : newScene;
        if (scene == null) return;

        _effect.Parameters["Progress"]?.SetValue(progress);
        _configure?.Invoke(_effect, progress);

        SpriteBatch.Begin(
            SpriteSortMode.Deferred, BlendState.AlphaBlend,
            null, null, null, _effect
        );
        SpriteBatch.Draw(scene, vp.Bounds, Color.White);
        SpriteBatch.End();
    }
}
```

---

## 6 — Stencil / Mask Transitions

A grayscale mask texture controls the reveal order. White pixels reveal first, black pixels reveal last. This is extremely versatile — any shape (swirl, shatter, curtain) becomes a transition just by changing the mask image.

```csharp
public class MaskTransition : Transition
{
    private Effect? _shader;
    private readonly Texture2D _mask;

    public float Softness { get; set; } = 0.05f;

    public MaskTransition(Texture2D mask, Effect shader)
    {
        _mask = mask;
        _shader = shader;
    }

    public override void Draw(
        RenderTarget2D? oldScene, RenderTarget2D? newScene,
        float progress, TransitionPhase phase)
    {
        var vp = Graphics.Viewport;
        var scene = phase == TransitionPhase.AnimateOut ? oldScene : newScene;
        if (scene == null || _shader == null) return;

        // During AnimateOut: progress 0→1 hides old scene
        // During AnimateIn: progress 0→1 reveals new scene
        float p = phase == TransitionPhase.AnimateOut ? progress : 1f - progress;

        _shader.Parameters["Progress"]?.SetValue(p);
        _shader.Parameters["Softness"]?.SetValue(Softness);
        _shader.Parameters["MaskTexture"]?.SetValue(_mask);

        SpriteBatch.Begin(
            SpriteSortMode.Deferred, BlendState.AlphaBlend,
            null, null, null, _shader
        );
        SpriteBatch.Draw(scene, vp.Bounds, Color.White);
        SpriteBatch.End();
    }
}
```

**MaskTransition.fx:**

```hlsl
sampler SceneSampler : register(s0);
texture MaskTexture;

sampler MaskSampler = sampler_state
{
    Texture = <MaskTexture>;
    MinFilter = Linear;
    MagFilter = Linear;
    AddressU  = Clamp;
    AddressV  = Clamp;
};

float Progress; // 0 = fully visible, 1 = fully hidden
float Softness; // edge feathering (0.01 - 0.2)

float4 MainPS(float2 texCoord : TEXCOORD0) : COLOR0
{
    float4 sceneColor = tex2D(SceneSampler, texCoord);
    float maskVal = tex2D(MaskSampler, texCoord).r;

    // smoothstep gives soft edge between visible and hidden
    float alpha = smoothstep(Progress - Softness, Progress + Softness, maskVal);

    return float4(sceneColor.rgb, sceneColor.a * alpha);
}

technique MaskReveal
{
    pass P0
    {
        PixelShader = compile ps_3_0 MainPS();
    }
}
```

> **Asset tip:** Generate mask textures procedurally (radial gradient, Perlin noise, geometric shapes) or hand-paint them. A 256×256 grayscale PNG is plenty for most transitions.

---

## 7 — Loading Screens

Heavy scenes (large tilemaps, many assets) need async loading with a visible progress indicator.

### 7.1 Async Loading Pattern

```csharp
public class LoadingScreen
{
    private readonly GraphicsDevice _graphics;
    private readonly SpriteBatch _spriteBatch;
    private readonly SpriteFont _font;
    private Texture2D _pixel = null!;

    private float _progress;
    private float _displayedProgress; // smoothly interpolated
    private float _elapsedTime;
    private bool _loadComplete;
    private string _currentHint = "";

    public float MinDisplayTime { get; set; } = 1.5f; // seconds
    public string[] Hints { get; set; } = Array.Empty<string>();

    public LoadingScreen(GraphicsDevice graphics, SpriteBatch spriteBatch, SpriteFont font)
    {
        _graphics = graphics;
        _spriteBatch = spriteBatch;
        _font = font;
        _pixel = new Texture2D(graphics, 1, 1);
        _pixel.SetData(new[] { Color.White });

        if (Hints.Length > 0)
            _currentHint = Hints[Random.Shared.Next(Hints.Length)];
    }

    /// <summary>
    /// Kick off async loading. Returns a Task that completes when loading
    /// AND minimum display time are both satisfied.
    /// </summary>
    public async Task RunAsync(Func<IProgress<float>, Task> loadWork)
    {
        _elapsedTime = 0f;
        _loadComplete = false;
        _progress = 0f;
        _displayedProgress = 0f;

        if (Hints.Length > 0)
            _currentHint = Hints[Random.Shared.Next(Hints.Length)];

        var progress = new Progress<float>(p => _progress = p);

        // Start loading on a background thread
        var loadTask = Task.Run(() => loadWork(progress));

        await loadTask;
        _progress = 1f;
        _loadComplete = true;

        // Wait for minimum display time
        while (_elapsedTime < MinDisplayTime)
        {
            await Task.Delay(16); // ~60fps
        }
    }

    public void Update(float dt)
    {
        _elapsedTime += dt;

        // Smooth the progress bar (never jumps, always catches up)
        _displayedProgress = MathHelper.Lerp(_displayedProgress, _progress, dt * 8f);
        if (_loadComplete)
            _displayedProgress = MathHelper.Lerp(_displayedProgress, 1f, dt * 12f);
    }

    public void Draw()
    {
        var vp = _graphics.Viewport;
        _graphics.Clear(new Color(18, 18, 24)); // dark background

        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);

        // Progress bar background
        int barWidth = (int)(vp.Width * 0.6f);
        int barHeight = 8;
        int barX = (vp.Width - barWidth) / 2;
        int barY = (int)(vp.Height * 0.7f);

        _spriteBatch.Draw(_pixel,
            new Rectangle(barX, barY, barWidth, barHeight),
            Color.Gray * 0.3f
        );

        // Progress bar fill
        int fillWidth = (int)(barWidth * _displayedProgress);
        _spriteBatch.Draw(_pixel,
            new Rectangle(barX, barY, fillWidth, barHeight),
            Color.White
        );

        // Percentage text
        string pctText = $"{(int)(_displayedProgress * 100)}%";
        var pctSize = _font.MeasureString(pctText);
        _spriteBatch.DrawString(_font, pctText,
            new Vector2(vp.Width / 2f - pctSize.X / 2f, barY - pctSize.Y - 8),
            Color.White * 0.9f
        );

        // Hint text
        if (!string.IsNullOrEmpty(_currentHint))
        {
            var hintSize = _font.MeasureString(_currentHint);
            _spriteBatch.DrawString(_font, _currentHint,
                new Vector2(vp.Width / 2f - hintSize.X / 2f, barY + barHeight + 20),
                Color.White * 0.5f
            );
        }

        _spriteBatch.End();
    }

    public bool IsFinished => _loadComplete && _elapsedTime >= MinDisplayTime;
}
```

### 7.2 Reporting Progress From Scene Loading

Inside your scene's load method, report progress through the `IProgress<float>` callback:

```csharp
public class GameplayScene : Scene
{
    public async Task LoadAsync(IProgress<float> progress)
    {
        // Load tilemap (40% of total)
        await Task.Run(() => LoadTilemap());
        progress.Report(0.4f);

        // Load entity prefabs (30%)
        await Task.Run(() => LoadPrefabs());
        progress.Report(0.7f);

        // Load audio (20%)
        await Task.Run(() => LoadAudio());
        progress.Report(0.9f);

        // Final setup (10%)
        InitializeSystems();
        progress.Report(1.0f);
    }
}
```

### 7.3 Integrating Loading Screen With Transitions

A combined flow: fade out → show loading screen → fade in on the new scene.

```csharp
public async Task TransitionWithLoading<TScene>(
    SceneManager sceneManager,
    TransitionManager transitionManager,
    LoadingScreen loadingScreen) where TScene : Scene, new()
{
    // Phase 1: Fade out
    var fadeOut = new FadeTransition { Duration = 0.3f };
    transitionManager.Start(fadeOut, onSwap: () => { });
    while (transitionManager.IsTransitioning)
        await Task.Yield(); // game loop drives Update/Draw

    // Phase 2: Show loading screen, load new scene
    var scene = new TScene();
    await loadingScreen.RunAsync(async progress =>
    {
        await scene.LoadAsync(progress);
    });

    // Phase 3: Activate scene and fade in
    sceneManager.SetActiveScene(scene);
    var fadeIn = new FadeTransition { Duration = 0.3f };
    // ... animate in
}
```

---

## 8 — Transition Presets

Pre-configured transitions for common game scenarios. Drop these in and go.

```csharp
public static class TransitionPresets
{
    /// <summary>Standard level change: fade to black and back.</summary>
    public static FadeTransition LevelChange() => new()
    {
        Duration = 0.35f,
        FadeColor = Color.Black
    };

    /// <summary>Death: slow fade to dark red.</summary>
    public static FadeTransition Death() => new()
    {
        Duration = 0.8f,
        FadeColor = new Color(40, 0, 0)
    };

    /// <summary>Menu open: quick fade to dark overlay.</summary>
    public static FadeTransition MenuOpen() => new()
    {
        Duration = 0.2f,
        FadeColor = new Color(0, 0, 0, 200)
    };

    /// <summary>Boss intro: slow circle wipe (iris in).</summary>
    public static CircleWipeTransition BossIntro() => new()
    {
        Duration = 1.0f,
        IrisOut = true
    };

    /// <summary>Retro level clear: diamond wipe.</summary>
    public static ShaderTransition DiamondWipe(Effect diamondEffect) => new(diamondEffect)
    {
        Duration = 0.6f
    };

    /// <summary>Dream sequence: dissolve with soft white edge.</summary>
    public static DissolveTransition DreamSequence(Effect shader, Texture2D noise) => new(shader, noise)
    {
        Duration = 1.2f,
        EdgeColor = Color.White,
        EdgeWidth = 0.08f
    };

    /// <summary>Glitch/warp: fast pixelate.</summary>
    public static PixelateTransition Glitch(Effect shader) => new(shader)
    {
        Duration = 0.3f,
        MaxBlockSize = 48f
    };

    /// <summary>Horizontal curtain wipe.</summary>
    public static WipeTransition CurtainClose() => new()
    {
        Duration = 0.5f,
        Direction = WipeDirection.Left
    };
}
```

**Using presets:**

```csharp
// Player died
transitionManager.Start(
    TransitionPresets.Death(),
    onSwap: () => sceneManager.ReloadCurrentScene()
);

// Enter boss room
transitionManager.Start(
    TransitionPresets.BossIntro(),
    onSwap: () => sceneManager.LoadScene<BossArenaScene>()
);
```

---

## 9 — Integration With Scene Manager

This section connects transitions to the scene lifecycle from [G38](./G38_scene_management.md).

### 9.1 Scene Manager Extension

```csharp
public class SceneManager
{
    private Scene? _activeScene;
    private readonly TransitionManager _transitions;
    private readonly GraphicsDevice _graphics;
    private readonly SpriteBatch _spriteBatch;

    public Scene? ActiveScene => _activeScene;
    public bool IsTransitioning => _transitions.IsTransitioning;

    public SceneManager(GraphicsDevice graphics, SpriteBatch spriteBatch)
    {
        _graphics = graphics;
        _spriteBatch = spriteBatch;
        _transitions = new TransitionManager(graphics, spriteBatch);
    }

    /// <summary>
    /// Switch scenes with a transition. If no transition is provided, does a hard cut.
    /// </summary>
    public void SwitchTo<TScene>(Transition? transition = null)
        where TScene : Scene, new()
    {
        if (IsTransitioning) return;

        if (transition == null)
        {
            // Hard cut
            _activeScene?.OnExit();
            _activeScene?.Dispose();
            _activeScene = new TScene();
            _activeScene.OnEnter();
            return;
        }

        _transitions.Start(
            transition,
            onSwap: () =>
            {
                _activeScene?.OnExit();
                _activeScene?.Dispose();
                _activeScene = new TScene();
                _activeScene.OnEnter();
            },
            onComplete: () =>
            {
                // Transition finished — scene is fully in control
            }
        );
    }

    /// <summary>Reload the current scene with a transition.</summary>
    public void ReloadCurrentScene(Transition? transition = null)
    {
        if (_activeScene == null) return;
        var sceneType = _activeScene.GetType();

        var t = transition ?? TransitionPresets.LevelChange();
        _transitions.Start(
            t,
            onSwap: () =>
            {
                _activeScene.OnExit();
                _activeScene.Dispose();
                _activeScene = (Scene)Activator.CreateInstance(sceneType)!;
                _activeScene.OnEnter();
            }
        );
    }

    public void Update(float dt)
    {
        _transitions.Update(dt);

        // ⚠️ This only updates the active scene when NOT transitioning.
        // During transitions, BOTH scenes should update to maintain temporal sync.
        // See "Simpler Alternative" above for the correct pattern.
        if (!IsTransitioning)
            _activeScene?.Update(dt);
    }

    public void Draw()
    {
        if (IsTransitioning)
        {
            _transitions.Draw();
        }
        else
        {
            _activeScene?.Draw();
        }
    }
}
```

### 9.2 Game Loop Integration

```csharp
public class MyGame : Game
{
    private SpriteBatch _spriteBatch = null!;
    private SceneManager _sceneManager = null!;

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _sceneManager = new SceneManager(GraphicsDevice, _spriteBatch);

        // Start with the title screen, no transition
        _sceneManager.SwitchTo<TitleScene>();
    }

    protected override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _sceneManager.Update(dt);
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.Black);
        _sceneManager.Draw();
        base.Draw(gameTime);
    }
}
```

### 9.3 Triggering Transitions From Gameplay (Arch ECS)

A system that listens for scene-change requests via Arch ECS components:

```csharp
// Component: tag an entity to request a scene change
public struct SceneChangeRequest
{
    public string TargetScene;
    public Transition? Transition;
}

// System: process scene change requests
public partial class SceneChangeSystem : BaseSystem<World, float>
{
    private readonly SceneManager _sceneManager;
    private QueryDescription _query = new QueryDescription().WithAll<SceneChangeRequest>();

    public SceneChangeSystem(World world, SceneManager sceneManager) : base(world)
    {
        _sceneManager = sceneManager;
    }

    public override void Update(in float dt)
    {
        World.Query(in _query, (Entity entity, ref SceneChangeRequest req) =>
        {
            // Map scene name → type (or use a registry)
            switch (req.TargetScene)
            {
                case "Gameplay":
                    _sceneManager.SwitchTo<GameplayScene>(
                        req.Transition ?? TransitionPresets.LevelChange()
                    );
                    break;
                case "MainMenu":
                    _sceneManager.SwitchTo<MainMenuScene>(
                        req.Transition ?? TransitionPresets.MenuOpen()
                    );
                    break;
            }

            // Remove the request so it doesn't fire again
            World.Destroy(entity);
        });
    }
}
```

**Requesting a transition from any system:**

```csharp
// Player reached the exit door
var request = World.Create(new SceneChangeRequest
{
    TargetScene = "Gameplay",
    Transition = TransitionPresets.LevelChange()
});

// Player died
var request = World.Create(new SceneChangeRequest
{
    TargetScene = "Gameplay",
    Transition = TransitionPresets.Death()
});
```

---

## Quick Reference

| Transition       | Best For                          | Needs Shader? |
| ---------------- | --------------------------------- | ------------- |
| Fade             | General purpose, menus            | No            |
| Crossfade        | Smooth scene blends               | No            |
| Wipe             | Retro feel, directional movement  | No            |
| Circle Wipe      | Mario-style level intro/outro     | Yes           |
| Diamond Wipe     | Retro RPG transitions             | Yes           |
| Dissolve         | Dream/magic/death sequences       | Yes           |
| Pixelate         | Glitch, teleport, retro           | Yes           |
| Mask             | Any custom shape                  | Yes           |

**Performance notes:**
- RenderTarget captures are the main cost. Reuse RTs when possible.
- Shader transitions are essentially free on GPU — the bottleneck is always the RT swap.
- For the loading screen, keep the rendering minimal. A progress bar and text is plenty.
- `MinDisplayTime` on loading screens prevents a jarring flash when loads finish in under a frame.

---

*Previous: [G41 Tweening & Easing](./G41_tweening.md) · Next: [G43 Debug & Dev Tools](./G43_debug_tools.md)*
