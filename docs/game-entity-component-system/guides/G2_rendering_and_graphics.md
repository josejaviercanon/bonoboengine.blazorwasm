# G2 — Rendering & Graphics


> **Category:** Guide · **Related:** [G1 Custom Code Recipes](./G1_custom_code_recipes.md) · [R2 Capability Matrix](../reference/R2_capability_matrix.md) · [G8 Content Pipeline](./G8_content_pipeline.md) · [G27 Shaders & Visual Effects](./G27_shaders_and_effects.md)

> Deep dive into the MonoGame 2D rendering pipeline, SpriteBatch internals, optimization strategies, and integration with Arch ECS.

---

## 1. SpriteBatch Deep Dive

`SpriteBatch` is the core 2D rendering API in MonoGame. Every sprite, tile, and UI element passes through it.

### 1.1 Begin/End Sort Modes

`SpriteBatch.Begin()` accepts a `SpriteSortMode` that controls how draw calls are batched and ordered:

| Mode | Behavior | When to Use |
|---|---|---|
| `Deferred` (default) | Queues all draws, sends to GPU on `End()` | General purpose — best default |
| `Immediate` | Each `Draw()` call goes to GPU instantly | Custom shaders mid-batch, render target switches |
| `BackToFront` | Sorts by `layerDepth` descending (1.0 = back) | Overlapping transparent sprites needing depth order |
| `FrontToBack` | Sorts by `layerDepth` ascending (0.0 = front) | Opaque sprites — enables early-Z rejection |
| `Texture` | Sorts by texture to minimize state changes | Maximum batching when depth order doesn't matter |

```csharp
// Typical game world rendering — sorted by depth
_spriteBatch.Begin(
    sortMode: SpriteSortMode.BackToFront,
    blendState: BlendState.AlphaBlend,
    samplerState: SamplerState.PointClamp,
    transformMatrix: _camera.TransformMatrix
);

// UI rendering — deferred is fine, we control draw order
_spriteBatch.Begin(
    sortMode: SpriteSortMode.Deferred,
    blendState: BlendState.AlphaBlend,
    samplerState: SamplerState.PointClamp
);
```

**Performance implications:**
- `Deferred` has minimal CPU overhead — just queues sprites into a buffer.
- `BackToFront` / `FrontToBack` run an internal sort on the queued sprites (O(n log n)).
- `Texture` sorts by texture pointer — maximizes GPU batch size but loses depth control.
- `Immediate` bypasses batching entirely — every `Draw()` is a draw call. Use sparingly.

### 1.2 BlendState Options

| BlendState | Formula | Use Case |
|---|---|---|
| `AlphaBlend` | Premultiplied alpha: `src + dest × (1 - srcAlpha)` | Standard sprites (MonoGame default pipeline uses premultiplied) |
| `NonPremultiplied` | Straight alpha: `src × srcAlpha + dest × (1 - srcAlpha)` | Sprites NOT preprocessed with premultiplied alpha |
| `Additive` | `src + dest` | Particles, glows, fire, lightning |
| `Opaque` | `src` (overwrites dest) | Backgrounds, full-screen clears with a texture |

> **Key gotcha:** MonoGame's content pipeline premultiplies alpha by default. If your sprites come from the pipeline, use `AlphaBlend`. If you load PNGs at runtime with `Texture2D.FromStream()`, they're straight alpha — use `NonPremultiplied` or premultiply manually.

```csharp
// Additive particles on top of the scene
_spriteBatch.Begin(
    blendState: BlendState.Additive,
    samplerState: SamplerState.PointClamp
);
foreach (var particle in _particles)
    _spriteBatch.Draw(particle.Texture, particle.Position, particle.Color);
_spriteBatch.End();
```

### 1.3 SamplerState

| SamplerState | Filtering | Wrapping | Use Case |
|---|---|---|---|
| `PointClamp` | Nearest-neighbor | Clamp to edge | Pixel art — crisp, no bleeding |
| `PointWrap` | Nearest-neighbor | Wrap/tile | Tiling pixel-art backgrounds |
| `LinearClamp` | Bilinear | Clamp | Smooth/HD art, rotated sprites |
| `LinearWrap` | Bilinear | Wrap | Tiling smooth textures |
| `AnisotropicClamp` | Anisotropic | Clamp | Rarely needed in 2D |

**For pixel art games: always use `PointClamp`** unless you intentionally want smooth filtering (e.g., for a zoom-out minimap).

---

## 2. Render Target Management

### 2.1 Creating RenderTarget2D

```csharp
_renderTarget = new RenderTarget2D(
    GraphicsDevice,
    width: 480,   // internal resolution
    height: 270,
    mipMap: false,
    preferredFormat: SurfaceFormat.Color,
    preferredDepthFormat: DepthFormat.None
);
```

**Render to it, then draw to screen:**
```csharp
// Pass 1: Render game world to target
GraphicsDevice.SetRenderTarget(_renderTarget);
GraphicsDevice.Clear(Color.CornflowerBlue);
_spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: _camera.TransformMatrix);
DrawWorld();
_spriteBatch.End();

// Pass 2: Draw target to backbuffer (scaled up)
GraphicsDevice.SetRenderTarget(null);
GraphicsDevice.Clear(Color.Black);
_spriteBatch.Begin(samplerState: SamplerState.PointClamp);
_spriteBatch.Draw(_renderTarget, _destinationRect, Color.White);
_spriteBatch.End();
```

### 2.2 Ping-Pong Buffering for Post-Processing

For chains of post-processing effects (bloom → chromatic aberration → vignette), use two render targets and alternate:

```csharp
private RenderTarget2D _pingTarget;
private RenderTarget2D _pongTarget;

void ApplyPostProcessChain(List<Effect> effects)
{
    var source = _pingTarget;  // initially holds the scene
    var dest = _pongTarget;

    foreach (var effect in effects)
    {
        GraphicsDevice.SetRenderTarget(dest);
        _spriteBatch.Begin(effect: effect, samplerState: SamplerState.PointClamp);
        _spriteBatch.Draw(source, Vector2.Zero, Color.White);
        _spriteBatch.End();

        // Swap
        (source, dest) = (dest, source);
    }

    // 'source' now holds the final result
    GraphicsDevice.SetRenderTarget(null);
    _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
    _spriteBatch.Draw(source, _destinationRect, Color.White);
    _spriteBatch.End();
}
```

### 2.3 Resize Handling

Render targets are **not** automatically resized when the window changes. Handle it explicitly:

```csharp
Window.ClientSizeChanged += (s, e) => RecreateRenderTargets();

void RecreateRenderTargets()
{
    _pingTarget?.Dispose();
    _pongTarget?.Dispose();
    _pingTarget = new RenderTarget2D(GraphicsDevice, _internalWidth, _internalHeight);
    _pongTarget = new RenderTarget2D(GraphicsDevice, _internalWidth, _internalHeight);

    // Recalculate destination rectangle for letterboxing
    _destinationRect = CalculateLetterbox(_internalWidth, _internalHeight,
        Window.ClientBounds.Width, Window.ClientBounds.Height);
}
```

### 2.4 Memory Management

- Each `RenderTarget2D` allocates GPU memory = `width × height × 4 bytes` (for `SurfaceFormat.Color`).
- A 1920×1080 target ≈ 8 MB. At a low internal res (480×270) it's only ~0.5 MB.
- **Always `Dispose()`** render targets you no longer need.
- Minimize target count — reuse when possible (e.g., one shared post-processing pair).

---

## 3. Sprite Batching Optimization

### 3.1 How SpriteBatch Auto-Batching Works

In `Deferred` mode, SpriteBatch accumulates sprites into a vertex buffer. It flushes (issues a draw call) when:

1. `End()` is called
2. The vertex buffer fills up (default: 2048 sprites per batch = 8192 vertices)
3. **The texture changes** between consecutive sprites (in `Deferred` mode only — `Texture` mode sorts to avoid this)

Each flush = 1 GPU draw call. Fewer flushes = better performance.

### 3.2 What Breaks a Batch

| Cause | Why | Mitigation |
|---|---|---|
| Texture change | Different texture = new draw call | Use texture atlases |
| `Begin()`/`End()` pair | Each pair is at least 1 draw call | Minimize separate Begin/End blocks |
| SpriteSortMode change | Requires new Begin/End | Group renders by sort mode |
| BlendState change | Requires new Begin/End | Group renders by blend state |
| Shader/Effect change | Requires new Begin/End | Batch by shader |
| Exceeding 2048 sprites | Internal buffer flush | Rare; not a real concern |

### 3.3 Texture Atlas Benefits

A texture atlas packs many sprites into one texture. Benefits:

- **One texture = one batch** — draw 500 different sprites in 1 draw call
- **Less GPU state switching** — no rebinding textures
- **Better memory utilization** — fewer wasted pixels from power-of-two padding

```csharp
// Without atlas: each Draw may break the batch
_spriteBatch.Draw(_playerTexture, playerPos, Color.White);   // draw call 1
_spriteBatch.Draw(_enemyTexture, enemyPos, Color.White);      // draw call 2 (texture changed!)
_spriteBatch.Draw(_bulletTexture, bulletPos, Color.White);     // draw call 3

// With atlas: all from one texture, source rectangles differ
_spriteBatch.Draw(_atlas, playerPos, _playerRegion, Color.White);   // \
_spriteBatch.Draw(_atlas, enemyPos, _enemyRegion, Color.White);     //  } 1 draw call total
_spriteBatch.Draw(_atlas, bulletPos, _bulletRegion, Color.White);   // /
```

### 3.4 Manual Batch Management Strategy

Organize your draw calls to minimize Begin/End blocks:

```
Begin (Opaque, FrontToBack, PointClamp, camera)     ← opaque world tiles
  Draw all tiles
End

Begin (AlphaBlend, BackToFront, PointClamp, camera)  ← transparent world sprites
  Draw all entities, sorted by depth
End

Begin (Additive, Deferred, PointClamp, camera)       ← particles/glows
  Draw all additive particles
End

Begin (AlphaBlend, Deferred, PointClamp)             ← UI (no camera transform)
  Draw all UI
End
```

**Total: 4 Begin/End pairs.** This is a good baseline for many 2D games.

---

## 4. Render Layer System

### 4.1 Layer Architecture

A render layer system organizes rendering into discrete ordered layers, each with independent settings:

```csharp
public class RenderLayer
{
    public string Name { get; init; }
    public int Order { get; set; }                    // Lower = drawn first (behind)
    public bool Visible { get; set; } = true;
    public Camera2D? Camera { get; set; }              // null = use default/screen-space
    public BlendState BlendState { get; set; } = BlendState.AlphaBlend;
    public SpriteSortMode SortMode { get; set; } = SpriteSortMode.BackToFront;
    public Effect? PostProcessEffect { get; set; }     // per-layer post-processing
    public RenderTarget2D? Target { get; set; }         // if post-processing needed
}
```

### 4.2 Typical Layer Stack

| Order | Layer | Camera | BlendState | Notes |
|---|---|---|---|---|
| 0 | Background | Parallax camera (0.5× scroll) | Opaque | Sky, distant mountains |
| 10 | Tilemap | World camera | AlphaBlend | Ground, walls |
| 20 | Entities | World camera | AlphaBlend | Player, enemies, items |
| 30 | Particles | World camera | Additive | Explosions, sparkles |
| 40 | Weather | World camera (0.8× scroll) | AlphaBlend | Rain, snow overlay |
| 100 | UI | None (screen-space) | AlphaBlend | HUD, menus |
| 110 | Debug | None (screen-space) | AlphaBlend | Collision shapes, stats |

### 4.3 Integration with Arch ECS

Use components to tag entities to layers and control sort order:

```csharp
// Components
public record struct RenderLayerTag(int Layer);
public record struct SortOrder(float Depth);
public record struct Sprite(Texture2D Texture, Rectangle Source, Vector2 Origin);

// Assign during entity creation
var entity = world.Create(
    new Position(100, 200),
    new Sprite(atlas, playerRegion, origin),
    new RenderLayerTag(Layer: 20),        // entities layer
    new SortOrder(Depth: 200f)            // sort by Y position
);
```

**Render system queries by layer:**

```csharp
public class SpriteRenderSystem
{
    private readonly QueryDescription _query = new QueryDescription()
        .WithAll<Position, Sprite, RenderLayerTag, SortOrder>();

    public void Render(World world, SpriteBatch spriteBatch, RenderLayer[] layers)
    {
        // Sort layers by order
        Array.Sort(layers, (a, b) => a.Order.CompareTo(b.Order));

        foreach (var layer in layers)
        {
            if (!layer.Visible) continue;

            var transform = layer.Camera?.TransformMatrix ?? Matrix.Identity;

            if (layer.Target != null)
                spriteBatch.GraphicsDevice.SetRenderTarget(layer.Target);

            spriteBatch.Begin(
                sortMode: layer.SortMode,
                blendState: layer.BlendState,
                samplerState: SamplerState.PointClamp,
                transformMatrix: transform
            );

            world.Query(in _query, (ref Position pos, ref Sprite spr,
                ref RenderLayerTag tag, ref SortOrder sort) =>
            {
                if (tag.Layer != layer.Order) return;

                spriteBatch.Draw(spr.Texture, pos.Value, spr.Source,
                    Color.White, 0f, spr.Origin, 1f,
                    SpriteEffects.None, sort.Depth);
            });

            spriteBatch.End();

            // Per-layer post-processing
            if (layer is { Target: not null, PostProcessEffect: not null })
                ApplyEffect(spriteBatch, layer.Target, layer.PostProcessEffect);
        }
    }
}
```

### 4.4 Y-Sorting for Depth

A common pattern: entities sort by Y position so sprites lower on screen appear in front:

```csharp
// In an update system — normalize Y to 0..1 range for layerDepth
world.Query(in _sortableQuery, (ref Position pos, ref SortOrder sort) =>
{
    sort.Depth = pos.Y / _worldHeight;  // 0 = top (back), 1 = bottom (front)
});
```

With `SpriteSortMode.BackToFront`, higher Depth values draw last (on top).

---

## 5. Animation System

### 5.1 MonoGame.Aseprite Deep Dive

[MonoGame.Aseprite](https://github.com/AristurtleDev/monogame-aseprite) (v6.3.x) loads `.aseprite` / `.ase` files directly — no export step. It provides:

| Type | Purpose |
|---|---|
| `Sprite` | Single static frame |
| `SpriteSheet` | All frames + tags from a file — the workhorse for animation |
| `AnimatedSprite` | Playback controller created from a `SpriteSheet` |
| `TextureAtlas` | Packed frames as regions in a single texture |

**Setup:**
```csharp
// In LoadContent — load the aseprite file via content pipeline
var aseFile = Content.Load<AsepriteFile>("player");

// Create a SpriteSheet (contains all frames and animation tags)
var spriteSheet = aseFile.CreateSpriteSheet(GraphicsDevice);

// Create an AnimatedSprite from it
var animatedSprite = spriteSheet.CreateAnimatedSprite("idle"); // start with "idle" tag
```

**Playback:**
```csharp
// Update — advances frame timing
animatedSprite.Update(gameTime);

// Switch animation (only restarts if tag actually changed)
animatedSprite.Play("run");

// Draw
animatedSprite.Draw(spriteBatch, position);

// Control
animatedSprite.Stop();
animatedSprite.Pause();
animatedSprite.Unpause();
animatedSprite.SetFrame(0);      // jump to specific frame

// Events
animatedSprite.OnFrameBegin += (sender, args) => { /* frame started */ };
animatedSprite.OnFrameEnd += (sender, args) => { /* frame ended */ };
animatedSprite.OnAnimationEnd += (sender, args) => { /* loop complete */ };
```

### 5.2 Animation State Machine

For complex characters, wrap animations in a state machine:

```csharp
public class AnimationStateMachine
{
    private readonly AnimatedSprite _sprite;
    private string _currentState;
    private readonly Dictionary<string, List<Transition>> _transitions = new();

    public record Transition(string To, Func<bool> Condition);

    public void AddTransition(string from, string to, Func<bool> condition)
    {
        if (!_transitions.ContainsKey(from))
            _transitions[from] = new List<Transition>();
        _transitions[from].Add(new Transition(to, condition));
    }

    public void Update(GameTime gameTime)
    {
        _sprite.Update(gameTime);

        if (_transitions.TryGetValue(_currentState, out var transitions))
        {
            foreach (var t in transitions)
            {
                if (t.Condition())
                {
                    _currentState = t.To;
                    _sprite.Play(_currentState);
                    break;
                }
            }
        }
    }

    public void Draw(SpriteBatch sb, Vector2 pos) => _sprite.Draw(sb, pos);
}

// Usage
var sm = new AnimationStateMachine(animatedSprite, "idle");
sm.AddTransition("idle", "run",  () => velocity.Length() > 0.1f);
sm.AddTransition("run",  "idle", () => velocity.Length() <= 0.1f);
sm.AddTransition("run",  "jump", () => justJumped);
sm.AddTransition("idle", "jump", () => justJumped);
sm.AddTransition("jump", "fall", () => velocity.Y > 0);
sm.AddTransition("fall", "idle", () => isGrounded);
```

### 5.3 Animation Events (Sound on Frame)

Use the `OnFrameBegin` event to trigger sounds or effects on specific frames:

```csharp
animatedSprite.OnFrameBegin += (sender, args) =>
{
    var tag = animatedSprite.CurrentTag;

    // Footstep sound on frames 2 and 6 of the "run" animation
    if (tag == "run" && (args.FrameIndex == 2 || args.FrameIndex == 6))
        AudioManager.Play("footstep");

    // Sword whoosh on frame 3 of "attack"
    if (tag == "attack" && args.FrameIndex == 3)
        AudioManager.Play("sword_swing");
};
```

---

## 6. Sprite Trails and Afterimages

### 6.1 Ring Buffer Implementation

A ring buffer stores the N most recent positions/states. Each frame, the oldest entry is overwritten:

```csharp
public class SpriteTrail
{
    private readonly struct TrailFrame
    {
        public Vector2 Position { get; init; }
        public Rectangle Source { get; init; }
        public float Opacity { get; init; }
        public SpriteEffects Effects { get; init; }
    }

    private readonly TrailFrame[] _buffer;
    private int _head;
    private readonly int _capacity;
    private readonly float _opacityStep;

    public bool Active { get; set; }

    public SpriteTrail(int trailLength = 6)
    {
        _capacity = trailLength;
        _buffer = new TrailFrame[_capacity];
        _opacityStep = 1f / (_capacity + 1);
    }

    public void Record(Vector2 position, Rectangle source, SpriteEffects effects)
    {
        if (!Active) return;

        _buffer[_head] = new TrailFrame
        {
            Position = position,
            Source = source,
            Opacity = 1f,
            Effects = effects
        };
        _head = (_head + 1) % _capacity;
    }

    public void Draw(SpriteBatch spriteBatch, Texture2D texture, Vector2 origin)
    {
        if (!Active) return;

        for (int i = 0; i < _capacity; i++)
        {
            // Read from oldest to newest
            int idx = (_head + i) % _capacity;
            ref var frame = ref _buffer[idx];

            if (frame.Source == Rectangle.Empty) continue;

            float age = (float)i / _capacity;  // 0 = oldest, ~1 = newest
            float opacity = age * _opacityStep * 0.6f;

            spriteBatch.Draw(texture, frame.Position, frame.Source,
                Color.White * opacity, 0f, origin, 1f, frame.Effects, 0f);
        }
    }
}
```

### 6.2 Use Cases

- **Dash mechanic:** Enable trail on dash start, disable after dash ends. Record every frame during dash.
- **Speed boost:** Enable when above a velocity threshold. Thin trail = 3 frames, thick = 8+.
- **Ghosting/phasing:** Use a tinted color (e.g., `Color.Cyan * opacity`) for spectral effect.

---

## 7. Normal Mapping for 2D

### 7.1 Concept

Normal maps encode surface direction per-pixel, allowing 2D sprites to react to dynamic lights. Each pixel's RGB channels map to XYZ normal direction.

### 7.2 Generating Normal Maps

- **SpriteIlluminator** or **Laigter** — dedicated tools for generating 2D normal maps from flat sprites.
- **Manual in Aseprite** — paint normals layer by hand (advanced, full artistic control).
- **Runtime generation** — derive from heightmap (grayscale version of sprite) using Sobel filter.

### 7.3 Custom Shader Implementation

```hlsl
// NormalMapLighting.fx
sampler2D SpriteTexture : register(s0);
sampler2D NormalTexture : register(s1);

float3 LightPosition;     // in screen space
float3 LightColor;
float  LightIntensity;
float3 AmbientColor;

float4 PixelShaderFunction(float2 texCoord : TEXCOORD0) : COLOR0
{
    float4 color = tex2D(SpriteTexture, texCoord);
    float3 normal = tex2D(NormalTexture, texCoord).rgb * 2.0 - 1.0; // unpack from [0,1] to [-1,1]
    normal = normalize(normal);

    // Light direction (2D, so Z is "out of screen")
    float3 pixelPos = float3(texCoord, 0);
    float3 lightDir = normalize(LightPosition - pixelPos);

    float diffuse = max(dot(normal, lightDir), 0.0);
    float attenuation = LightIntensity / distance(LightPosition, pixelPos);

    float3 lit = color.rgb * (AmbientColor + LightColor * diffuse * attenuation);
    return float4(lit, color.a);
}

technique NormalLighting
{
    pass Pass0
    {
        PixelShader = compile ps_3_0 PixelShaderFunction();
    }
}
```

**C# side:**
```csharp
_normalEffect.Parameters["LightPosition"].SetValue(new Vector3(mouseX, mouseY, 0.05f));
_normalEffect.Parameters["LightColor"].SetValue(Color.Orange.ToVector3());
_normalEffect.Parameters["LightIntensity"].SetValue(0.8f);
_normalEffect.Parameters["AmbientColor"].SetValue(new Vector3(0.15f, 0.15f, 0.2f));

GraphicsDevice.Textures[1] = _playerNormalMap;  // bind normal map to sampler s1

_spriteBatch.Begin(effect: _normalEffect, samplerState: SamplerState.PointClamp);
_spriteBatch.Draw(_playerTexture, _playerPos, Color.White);
_spriteBatch.End();
```

---

## 8. Render Pipeline Ordering

### 8.1 Complete Frame Sequence

```
┌─────────────────────────────────────────────────┐
│  1. SetRenderTarget(gameTarget)                  │
│  2. Clear(Color.CornflowerBlue)                  │
│                                                   │
│  3. Begin (Opaque, FrontToBack) ← background     │
│     Draw background tiles                         │
│     End                                           │
│                                                   │
│  4. Begin (AlphaBlend, BackToFront) ← world      │
│     Draw tilemap layers                           │
│     Draw entities (Y-sorted)                      │
│     End                                           │
│                                                   │
│  5. Begin (Additive) ← particles/glow            │
│     Draw additive particles                       │
│     End                                           │
│                                                   │
│  6. Begin (AlphaBlend) ← alpha particles          │
│     Draw smoke, dust (non-additive particles)     │
│     End                                           │
│                                                   │
│  7. SetRenderTarget(null) — or ping-pong chain   │
│  8. Apply post-processing (bloom, color grade)    │
│                                                   │
│  9. Begin (AlphaBlend, screen-space) ← UI        │
│     Draw HUD, menus, text                         │
│     End                                           │
│                                                   │
│ 10. Begin (AlphaBlend, screen-space) ← debug     │
│     Draw debug overlays                           │
│     End                                           │
│                                                   │
│ 11. Present() — (implicit at end of Draw)         │
└─────────────────────────────────────────────────┘
```

### 8.2 Premultiplied Alpha — Getting It Right

MonoGame's content pipeline premultiplies alpha: `RGB = RGB × A` at import time. This means:

| Scenario | Correct BlendState |
|---|---|
| Sprites from Content Pipeline | `BlendState.AlphaBlend` (premultiplied) |
| Sprites from `Texture2D.FromStream()` | `BlendState.NonPremultiplied` (straight) |
| Render target as texture | `BlendState.AlphaBlend` (targets store premultiplied) |
| Custom loaded + manually premultiplied | `BlendState.AlphaBlend` |

**Common symptom of wrong blend mode:** dark fringing / black halos around sprite edges.

**Manual premultiplication** if you need to fix runtime-loaded textures:

```csharp
static Texture2D PremultiplyAlpha(Texture2D texture)
{
    var data = new Color[texture.Width * texture.Height];
    texture.GetData(data);
    for (int i = 0; i < data.Length; i++)
    {
        var c = data[i];
        data[i] = new Color(
            (byte)(c.R * c.A / 255),
            (byte)(c.G * c.A / 255),
            (byte)(c.B * c.A / 255),
            c.A
        );
    }
    texture.SetData(data);
    return texture;
}
```

---

## 9. Pixel-Perfect Rendering

### 9.1 The Setup

For pixel art, render at native low resolution and scale up to the window with integer scaling:

```csharp
// Internal game resolution (e.g., 320×180 for 16:9 pixel art)
const int GameWidth = 320;
const int GameHeight = 180;

// Calculate largest integer scale that fits the window
int scaleX = Window.ClientBounds.Width / GameWidth;
int scaleY = Window.ClientBounds.Height / GameHeight;
int scale = Math.Max(1, Math.Min(scaleX, scaleY));

// Center the scaled image (letterbox/pillarbox the remainder)
int drawWidth = GameWidth * scale;
int drawHeight = GameHeight * scale;
int offsetX = (Window.ClientBounds.Width - drawWidth) / 2;
int offsetY = (Window.ClientBounds.Height - drawHeight) / 2;
_destinationRect = new Rectangle(offsetX, offsetY, drawWidth, drawHeight);
```

### 9.2 Camera Snapping

Non-integer camera positions cause sub-pixel rendering, which produces inconsistent pixel sizes and "shimmering":

```csharp
// Snap camera position to whole pixels in game space
public Vector2 SnapToPixel(Vector2 position)
{
    return new Vector2(
        MathF.Round(position.X),
        MathF.Round(position.Y)
    );
}

// Apply in camera update
_cameraPosition = SnapToPixel(_rawCameraPosition);
```

**For smooth camera follow with snapped rendering**, accumulate sub-pixel offset separately:

```csharp
_subPixelOffset += targetPosition - _cameraPosition;
if (MathF.Abs(_subPixelOffset.X) >= 1f)
{
    _cameraPosition.X += MathF.Truncate(_subPixelOffset.X);
    _subPixelOffset.X -= MathF.Truncate(_subPixelOffset.X);
}
// Same for Y
```

### 9.3 Entity Position Snapping

When drawing entities, also snap to integer positions to avoid sub-pixel artifacts:

```csharp
// In your render system
Vector2 drawPos = new Vector2(
    MathF.Round(position.X),
    MathF.Round(position.Y)
);
_spriteBatch.Draw(texture, drawPos, sourceRect, Color.White);
```

### 9.4 Checklist for Pixel-Perfect

- [x] Render to low-res `RenderTarget2D`
- [x] Use `SamplerState.PointClamp` everywhere
- [x] Integer scaling only when drawing target to backbuffer
- [x] Snap camera to integer positions
- [x] Snap entity draw positions to integers
- [x] Use `PointClamp` when drawing the render target to screen
- [x] Letterbox/pillarbox (black bars) for non-integer remainders

---

## 10. Debug Rendering

### 10.1 Drawing Collision Shapes

```csharp
public static class DebugDraw
{
    private static Texture2D _pixel;

    public static void Initialize(GraphicsDevice gd)
    {
        _pixel = new Texture2D(gd, 1, 1);
        _pixel.SetData(new[] { Color.White });
    }

    public static void DrawRect(SpriteBatch sb, Rectangle rect, Color color, int thickness = 1)
    {
        // Top
        sb.Draw(_pixel, new Rectangle(rect.X, rect.Y, rect.Width, thickness), color);
        // Bottom
        sb.Draw(_pixel, new Rectangle(rect.X, rect.Y + rect.Height - thickness, rect.Width, thickness), color);
        // Left
        sb.Draw(_pixel, new Rectangle(rect.X, rect.Y, thickness, rect.Height), color);
        // Right
        sb.Draw(_pixel, new Rectangle(rect.X + rect.Width - thickness, rect.Y, thickness, rect.Height), color);
    }

    public static void DrawLine(SpriteBatch sb, Vector2 start, Vector2 end, Color color, int thickness = 1)
    {
        var diff = end - start;
        float angle = MathF.Atan2(diff.Y, diff.X);
        float length = diff.Length();
        sb.Draw(_pixel, start, null, color, angle, Vector2.Zero,
            new Vector2(length, thickness), SpriteEffects.None, 0f);
    }

    public static void DrawCircle(SpriteBatch sb, Vector2 center, float radius, Color color, int segments = 16)
    {
        float step = MathF.Tau / segments;
        for (int i = 0; i < segments; i++)
        {
            var a = center + new Vector2(MathF.Cos(step * i), MathF.Sin(step * i)) * radius;
            var b = center + new Vector2(MathF.Cos(step * (i + 1)), MathF.Sin(step * (i + 1))) * radius;
            DrawLine(sb, a, b, color);
        }
    }
}
```

### 10.2 Velocity Vectors

```csharp
// In debug render system
world.Query(in _debugQuery, (ref Position pos, ref Velocity vel) =>
{
    DebugDraw.DrawLine(sb, pos.Value, pos.Value + vel.Value * 0.5f, Color.Yellow, 1);
});
```

### 10.3 Grid Overlay

```csharp
public static void DrawGrid(SpriteBatch sb, Camera2D camera, int cellSize, Color color)
{
    var bounds = camera.VisibleArea;
    int startX = ((int)bounds.X / cellSize) * cellSize;
    int startY = ((int)bounds.Y / cellSize) * cellSize;

    for (int x = startX; x <= bounds.Right; x += cellSize)
        DrawLine(sb, new Vector2(x, bounds.Top), new Vector2(x, bounds.Bottom), color * 0.3f);

    for (int y = startY; y <= bounds.Bottom; y += cellSize)
        DrawLine(sb, new Vector2(bounds.Left, y), new Vector2(bounds.Right, y), color * 0.3f);
}
```

### 10.4 ImGui Integration

[ImGui.NET](https://github.com/ImGuiNET/ImGui.NET) with a MonoGame renderer provides an immediate-mode debug UI:

```csharp
// Using MonoGame.ImGuiNet or similar binding
_imGuiRenderer.BeforeLayout(gameTime);

ImGui.Begin("Render Stats");
ImGui.Text($"Draw Calls: {GraphicsDevice.Metrics.DrawCount}");
ImGui.Text($"Sprites: {GraphicsDevice.Metrics.SpriteCount}");
ImGui.Text($"Textures: {GraphicsDevice.Metrics.TextureCount}");
ImGui.Text($"FPS: {1.0 / gameTime.ElapsedGameTime.TotalSeconds:F1}");
ImGui.Text($"Entities: {world.Size}");
ImGui.End();

_imGuiRenderer.AfterLayout();
```

> **Tip:** Wrap all debug rendering behind a `#if DEBUG` or a runtime toggle to strip from release builds.

---

## 11. MonoGame.Extended Rendering Features

[MonoGame.Extended](https://github.com/craftworkgames/MonoGame.Extended) adds higher-level 2D rendering on top of MonoGame:

### 11.1 Feature Overview

| Feature | Class/Namespace | What It Does |
|---|---|---|
| Sprite Sheets | `SpriteSheet`, `SpriteSheetAnimationFactory` | Load atlas + JSON, create named animations |
| Animated Sprites | `AnimatedSprite` | Frame-based animation playback |
| Shapes | `ShapeExtensions` | Draw circles, polygons, lines (debug/prototyping) |
| Particles | `ParticleEffect`, `ParticleEmitter` | 2D particle system with profiles and modifiers |
| Cameras | `OrthographicCamera` | Camera2D with `GetTransformMatrix()`, containment, zoom |
| Bitmap Fonts | `BitmapFont` | Load + render `.fnt` bitmap fonts |
| Tiled Maps | `TiledMap`, `TiledMapRenderer` | Load + render Tiled `.tmx` maps |
| Screen Management | `Screen`, `ScreenManager` | Scene/screen transitions |

### 11.2 When to Use vs Custom Code

| Scenario | Use Extended? | Why |
|---|---|---|
| Quick prototyping | ✅ Yes | Faster iteration, less boilerplate |
| Tiled map rendering | ✅ Yes | Mature `TiledMapRenderer` handles layers, objects |
| Basic particles | ✅ Yes | `ParticleEffect` covers common VFX |
| Pixel-art with Aseprite | ❌ Prefer MonoGame.Aseprite | Direct `.ase` loading, better for Aseprite workflows |
| ECS-integrated animation | ❌ Custom | Extended's animation isn't ECS-aware |
| Advanced post-processing | ❌ Custom | Extended doesn't provide shader chains |
| Production camera | ⚠️ Start with Extended | May outgrow it (add shake, bounds, smoothing) |

### 11.3 Extended Particle System Example

```csharp
// Create a fire particle effect
var particleEffect = new ParticleEffect
{
    Emitters = new List<ParticleEmitter>
    {
        new ParticleEmitter(
            new TextureRegion2D(fireTexture),
            capacity: 500,
            lifeSpan: TimeSpan.FromSeconds(1.2),
            profile: Profile.Circle(radius: 8f, radiate: Profile.CircleRadiation.Out)
        )
        {
            Parameters = new ParticleReleaseParameters
            {
                Speed = new Range<float>(20f, 80f),
                Quantity = new Range<int>(2, 5),
                Rotation = new Range<float>(-MathF.PI, MathF.PI),
                Scale = new Range<float>(0.3f, 1.0f),
                Color = new HslColor(15, 0.9f, 0.5f)  // orange
            },
            Modifiers =
            {
                new AgeModifier { ColorInterpolator = new FastFade() },
                new RotationModifier { RotationRate = 0.5f },
                new ScaleInterpolator { StartScale = 1f, EndScale = 0f }
            }
        }
    }
};

// Update + Draw
particleEffect.Update((float)gameTime.ElapsedGameTime.TotalSeconds);
_spriteBatch.Begin(blendState: BlendState.Additive);
_spriteBatch.Draw(particleEffect);
_spriteBatch.End();
```

---

## Quick Reference Tables

### BlendState Decision Matrix

| What you're drawing | BlendState | SortMode |
|---|---|---|
| Opaque background fill | `Opaque` | `Deferred` |
| Tilemap with transparency | `AlphaBlend` | `Deferred` |
| Game entities (overlapping) | `AlphaBlend` | `BackToFront` |
| Opaque entities (optimize) | `AlphaBlend` | `FrontToBack` |
| Glow / fire / lightning | `Additive` | `Deferred` |
| UI elements | `AlphaBlend` | `Deferred` |
| Runtime-loaded PNGs | `NonPremultiplied` | Per need |

### Performance Checklist

- [ ] Use texture atlases (1 atlas per "group": characters, tiles, UI)
- [ ] Minimize Begin/End pairs (target: 3-6 per frame)
- [ ] Use `SpriteSortMode.Texture` when depth order is irrelevant
- [ ] Check `GraphicsDevice.Metrics.DrawCount` regularly
- [ ] Render at low internal resolution, scale up (saves fill rate)
- [ ] Dispose unused `Texture2D` and `RenderTarget2D` objects
- [ ] Pool particle and trail objects to avoid GC pressure
- [ ] Use `SpriteSortMode.Deferred` for UI (natural draw order)
