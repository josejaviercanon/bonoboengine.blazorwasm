# G20 — Camera Systems


> **Category:** Guide · **Engine:** MonoGame + Arch ECS · **Tier:** Free (core) / Pro (advanced patterns)
> **Related:** [G2 Rendering & Graphics](./G2_rendering_and_graphics.md) · [G19 Display & Resolution](./G19_display_resolution_viewports.md) · [G21 Coordinate Systems](./G21_coordinate_systems.md) · [G22 Parallax & Depth Layers](./G22_parallax_depth_layers.md) · [Camera Theory (engine-agnostic)](../../core/concepts/camera-theory.md)

> Comprehensive implementation guide covering all 2D camera patterns for MonoGame: follow modes, deadzone, look-ahead, multi-target framing, cinematic sequences, shake, zoom, split screen, transitions, and ECS integration.

---

## Table of Contents

1. [MonoGame.Extended OrthographicCamera](#monogameextended-orthographiccamera)
2. [Camera Follow Patterns](#camera-follow-patterns)
3. [Camera Limits (Clamping)](#camera-limits-clamping-to-map-bounds)
4. [Camera Shake](#camera-shake)
5. [Camera Zoom](#camera-zoom)
6. [Multi-Target Camera](#multi-target-camera)
7. [Cinematic Camera](#cinematic-camera)
8. [Camera Transitions](#camera-transitions)
9. [Split Screen](#split-screen-multiple-cameras)
10. [Frustum Culling](#frustum-culling-with-camera)
11. [Camera Priority Stack](#camera-priority-stack)
12. [ECS Integration](#ecs-integration)
13. [Troubleshooting](#troubleshooting)
14. [See Also](#see-also)

---

## Camera Pipeline Overview

Every frame, the camera processes inputs through a pipeline. Understanding this order prevents the most common camera bugs:

```
┌─────────────────────────────────────────────────────┐
│                 Camera Update Pipeline                │
├─────────────────────────────────────────────────────┤
│                                                       │
│  1. Resolve Target  ─── Who/what to follow?           │
│         │                (player, multi-target,       │
│         │                 cinematic waypoint)          │
│         ▼                                             │
│  2. Apply Follow   ─── Lerp / deadzone / look-ahead  │
│         │                                             │
│         ▼                                             │
│  3. Clamp to Bounds ── Keep view inside map           │
│         │                                             │
│         ▼                                             │
│  4. Apply Shake    ─── Offset from shake system       │
│         │                                             │
│         ▼                                             │
│  5. Snap Pixels    ─── Round for pixel-art (optional) │
│         │                                             │
│         ▼                                             │
│  6. Build Matrix   ─── camera.GetViewMatrix()         │
│                                                       │
└─────────────────────────────────────────────────────┘
```

> **Critical:** Shake MUST come after clamping. If you shake before clamp, the clamp eats the shake offset at map edges and the screen feels "stuck."

---

## MonoGame.Extended OrthographicCamera

MonoGame.Extended v5.3.1 provides `OrthographicCamera` — the foundation for all camera work. It wraps a view matrix that transforms world coordinates to screen coordinates.

```csharp
using MonoGame.Extended;

// Create during scene initialization
var camera = new OrthographicCamera(GraphicsDevice);
camera.Position = new Vector2(0, 0);
camera.Zoom = 1.0f;
camera.Rotation = 0f;
```

### Passing to SpriteBatch

The camera's view matrix goes into `SpriteBatch.Begin()`:

```csharp
spriteBatch.Begin(
    sortMode: SpriteSortMode.Deferred,
    samplerState: SamplerState.PointClamp,
    transformMatrix: camera.GetViewMatrix()
);
// Draw world-space sprites here
spriteBatch.End();

// UI draws without the camera matrix
spriteBatch.Begin();
// Draw screen-space UI here
spriteBatch.End();
```

World-space objects (entities, tiles, particles) use the camera transform. Screen-space elements (HUD, menus) draw without it.

### Built-in Methods

| Method | Purpose |
|--------|---------|
| `camera.GetViewMatrix()` | Returns the view transformation matrix |
| `camera.ScreenToWorld(screenPos)` | Convert screen pixel to world coordinate |
| `camera.WorldToScreen(worldPos)` | Convert world coordinate to screen pixel |
| `camera.BoundingRectangle` | Visible world-space rectangle (for frustum culling) |

### Coordinate System Reminder

`OrthographicCamera.Position` is the **center** of the view. Setting it to `(0, 0)` means the view spans from `(-halfWidth, -halfHeight)` to `(halfWidth, halfHeight)`. This catches many developers off guard — see [G21 Coordinate Systems](./G21_coordinate_systems.md) for the full conversion chain.

---

## Camera Follow Patterns

### 1. Direct Follow (Lock to Target)

Camera position equals target position. Simplest approach — no smoothing, no lag.

```csharp
public void UpdateCamera(OrthographicCamera camera, Vector2 targetPosition)
{
    camera.Position = targetPosition;
}
```

**Best for:** Fast-paced games where the player must always be centered, prototyping.

### 2. Smoothed Follow (Lerp)

Camera moves toward the target at a rate proportional to distance. Creates a natural "catch up" feel.

```csharp
/// <summary>Smoothly follow a target position.</summary>
public void UpdateCamera(OrthographicCamera camera, Vector2 targetPosition, float dt)
{
    float smoothSpeed = 5f; // Higher = snappier. 3-8 is typical.
    Vector2 current = camera.Position;
    camera.Position = Vector2.Lerp(current, targetPosition, smoothSpeed * dt);
}
```

**Gotcha:** Using `MathHelper.Lerp` with a fixed `t` (like 0.1f) without `dt` creates frame-rate-dependent smoothing. Always multiply by delta time or use exponential decay:

```csharp
// Frame-rate independent exponential smoothing
// smoothingFactor: lower = smoother (0.001 = very smooth, 0.1 = snappy)
float t = 1f - MathF.Pow(0.01f, dt);
camera.Position = Vector2.Lerp(current, targetPosition, t);
```

> **Why exponential decay?** Linear lerp (`smoothSpeed * dt`) can overshoot when `dt` is large (lag spike). Exponential decay (`1 - base^dt`) is mathematically guaranteed to never overshoot — it asymptotically approaches the target. Use exponential decay for any production camera. See [Camera Theory](../../core/concepts/camera-theory.md) for the derivation.

### 3. Deadzone (Only Move When Target Exits Region)

Define an inner rectangle around the screen center. The camera doesn't move until the target exits this region. This prevents micro-movements from causing camera jitter.

```
  ┌──────────────────────────────────┐
  │         Screen / View            │
  │                                  │
  │     ┌──────────────────┐         │
  │     │    DEADZONE      │         │
  │     │                  │         │
  │     │      ☺ player    │         │
  │     │                  │         │
  │     └──────────────────┘         │
  │                                  │
  │  Camera doesn't move while       │
  │  player stays inside deadzone    │
  └──────────────────────────────────┘
```

```csharp
/// <summary>Camera with deadzone — only scrolls when target exits inner rectangle.</summary>
public sealed class DeadzoneCamera
{
    private readonly OrthographicCamera _camera;
    private readonly float _deadzoneWidth;
    private readonly float _deadzoneHeight;

    public DeadzoneCamera(OrthographicCamera camera, float deadzoneWidth, float deadzoneHeight)
    {
        _camera = camera;
        _deadzoneWidth = deadzoneWidth;
        _deadzoneHeight = deadzoneHeight;
    }

    public void Update(Vector2 targetPosition)
    {
        Vector2 camPos = _camera.Position;
        float halfW = _deadzoneWidth / 2f;
        float halfH = _deadzoneHeight / 2f;

        // Only move if target is outside the deadzone
        if (targetPosition.X > camPos.X + halfW)
            camPos.X = targetPosition.X - halfW;
        else if (targetPosition.X < camPos.X - halfW)
            camPos.X = targetPosition.X + halfW;

        if (targetPosition.Y > camPos.Y + halfH)
            camPos.Y = targetPosition.Y - halfH;
        else if (targetPosition.Y < camPos.Y - halfH)
            camPos.Y = targetPosition.Y + halfH;

        _camera.Position = camPos;
    }
}
```

**Best for:** Platformers, top-down RPGs — any game where slight player movement shouldn't move the camera.

**Tuning guide:**
| Genre | Deadzone Width | Deadzone Height | Notes |
|-------|---------------|----------------|-------|
| Platformer | 40-80px | 20-40px | Narrow vertical — show more above/below |
| Top-down RPG | 60-100px | 60-100px | Square — equal movement freedom |
| Metroidvania | 80-120px | 30-60px | Wide horizontal for exploration |
| Strategy | 0px | 0px | No deadzone — follow cursor directly |

### 4. Look-Ahead (Offset in Movement Direction)

Shift the camera ahead of the player's movement direction so they can see what's coming.

```csharp
/// <summary>Offset camera in the direction the target is moving.</summary>
public void UpdateWithLookAhead(OrthographicCamera camera, Vector2 targetPosition,
    Vector2 targetVelocity, float dt)
{
    float lookAheadDistance = 100f;
    float smoothSpeed = 3f;

    Vector2 lookAheadOffset = Vector2.Zero;
    if (targetVelocity.LengthSquared() > 1f)
    {
        Vector2 direction = Vector2.Normalize(targetVelocity);
        lookAheadOffset = direction * lookAheadDistance;
    }

    Vector2 desiredPosition = targetPosition + lookAheadOffset;
    float t = 1f - MathF.Pow(0.001f, dt);
    camera.Position = Vector2.Lerp(camera.Position, desiredPosition, t);
}
```

**Best for:** Platformers (look ahead horizontally), shooters (look toward cursor/aim direction).

### 5. Platformer-Style Vertical Snap

In platformers, vertical camera movement should be different from horizontal. The camera should:
- Smoothly track horizontal movement
- Only snap vertically when the player **lands** (not while airborne)

This prevents the camera from bobbing up and down during jumps, which causes motion sickness.

```csharp
/// <summary>
/// Platformer camera: smooth horizontal follow + vertical snap on land.
/// Vertical position only updates when the player is grounded.
/// </summary>
public sealed class PlatformerCamera
{
    private readonly OrthographicCamera _camera;
    private float _verticalTarget;
    private bool _verticalLocked;

    public PlatformerCamera(OrthographicCamera camera)
    {
        _camera = camera;
        _verticalTarget = camera.Position.Y;
        _verticalLocked = true;
    }

    public void Update(Vector2 targetPos, bool isGrounded, float dt)
    {
        // Horizontal: always smooth follow
        float tx = 1f - MathF.Pow(0.005f, dt);
        float newX = MathHelper.Lerp(_camera.Position.X, targetPos.X, tx);

        // Vertical: only update target when grounded
        if (isGrounded)
        {
            _verticalTarget = targetPos.Y;
            _verticalLocked = true;
        }

        // Smooth vertical follow toward the locked target
        float ty = _verticalLocked ? 1f - MathF.Pow(0.01f, dt) : 0f;
        float newY = MathHelper.Lerp(_camera.Position.Y, _verticalTarget, ty);

        // Emergency snap: if player falls too far below camera, unlock and follow
        if (targetPos.Y > _camera.Position.Y + 200f)
        {
            _verticalTarget = targetPos.Y;
            _verticalLocked = true;
        }

        _camera.Position = new Vector2(newX, newY);
    }
}
```

**Tuning:** The 200px emergency threshold should be ~1.5× your max jump height. Too low = camera jitters on double-jumps. Too high = player disappears off-screen during falls.

### Combining Patterns

Real games combine multiple follow behaviors. A platformer typically uses deadzone + look-ahead + vertical snap. Apply them in order:

```csharp
// 1. Deadzone (coarse movement)
_deadzoneCamera.Update(playerPos);

// 2. Look-ahead (shift toward velocity)
Vector2 lookOffset = GetLookAheadOffset(playerVelocity);

// 3. Smooth the combined result
Vector2 desired = _camera.Position + lookOffset;
float t = 1f - MathF.Pow(0.01f, dt);
_camera.Position = Vector2.Lerp(_camera.Position, desired, t);

// 4. Clamp, then shake (pipeline order!)
ClampToMapBounds(_camera, _mapBounds, viewWidth, viewHeight);
_camera.Position += _shake.Offset;
```

---

## Camera Limits (Clamping to Map Bounds)

Prevent the camera from showing outside the world. Clamp camera position so the visible rectangle stays within map bounds.

> **Warning:** `camera.BoundingRectangle` reads `GraphicsDevice.Viewport` live. During `Update()`, the viewport is the full backbuffer — not your virtual render target. This gives wrong dimensions when using a virtual resolution system. Pass explicit view dimensions from `VirtualResolution.VirtualWidth`/`VirtualHeight` instead.

### Correct Approach (with VirtualResolution)

When using expand mode (see [G19](./G19_display_resolution_viewports.md)), `VirtualWidth`/`VirtualHeight` change on window resize. Read them each frame:

```csharp
/// <summary>
/// Clamp camera so the visible area stays within world bounds.
/// Uses explicit view dimensions (from VirtualResolution) instead of BoundingRectangle.
/// </summary>
public static void ClampToMapBounds(OrthographicCamera camera, Rectangle mapBounds,
    int viewWidth, int viewHeight)
{
    // Camera.Position is top-left; visible area spans [Position, Position + viewSize]
    float clampedX = MathHelper.Clamp(camera.Position.X,
        mapBounds.Left, mapBounds.Right - viewWidth);
    float clampedY = MathHelper.Clamp(camera.Position.Y,
        mapBounds.Top, mapBounds.Bottom - viewHeight);

    camera.Position = new Vector2(clampedX, clampedY);
}

// Usage:
ClampToMapBounds(camera, mapBounds, _virtualRes.VirtualWidth, _virtualRes.VirtualHeight);
```

### Without VirtualResolution (Fixed Resolution)

If you're using a fixed resolution (letterbox mode) and not expand mode, `BoundingRectangle` is safe to use during `Draw()` (when the viewport matches the render target). Even then, prefer explicit dimensions for clarity:

```csharp
/// <summary>Clamp using BoundingRectangle — only safe when viewport matches RT.</summary>
public static void ClampToMapBounds(OrthographicCamera camera, Rectangle mapBounds)
{
    RectangleF visibleArea = camera.BoundingRectangle;
    float halfVisibleW = visibleArea.Width / 2f;
    float halfVisibleH = visibleArea.Height / 2f;

    float clampedX = MathHelper.Clamp(
        camera.Position.X,
        mapBounds.Left + halfVisibleW,
        mapBounds.Right - halfVisibleW);

    float clampedY = MathHelper.Clamp(
        camera.Position.Y,
        mapBounds.Top + halfVisibleH,
        mapBounds.Bottom - halfVisibleH);

    camera.Position = new Vector2(clampedX, clampedY);
}
```

### Small Maps (View Larger Than Map)

When the visible area is larger than the map, clamping produces negative ranges and the camera oscillates. Handle this by centering instead:

```csharp
public static void ClampOrCenter(OrthographicCamera camera, Rectangle mapBounds,
    int viewWidth, int viewHeight)
{
    float x, y;

    if (mapBounds.Width <= viewWidth)
        x = mapBounds.Center.X - viewWidth / 2f;  // Center horizontally
    else
        x = MathHelper.Clamp(camera.Position.X,
            mapBounds.Left, mapBounds.Right - viewWidth);

    if (mapBounds.Height <= viewHeight)
        y = mapBounds.Center.Y - viewHeight / 2f;  // Center vertically
    else
        y = MathHelper.Clamp(camera.Position.Y,
            mapBounds.Top, mapBounds.Bottom - viewHeight);

    camera.Position = new Vector2(x, y);
}
```

**Call order:** Update camera follow first, then clamp. If the map is smaller than the visible area, center the camera on the map instead of clamping.

---

## Camera Shake

Screen shake adds impact to hits, explosions, and environmental events.

### Basic Shake (Random Offset + Decay)

```csharp
/// <summary>Simple camera shake with decay.</summary>
public sealed class CameraShake
{
    private float _intensity;
    private float _duration;
    private float _elapsed;
    private readonly Random _rng = new();

    /// <summary>Current shake offset to add to camera position.</summary>
    public Vector2 Offset { get; private set; }

    /// <summary>Trigger a shake.</summary>
    public void Start(float intensity, float duration)
    {
        _intensity = MathF.Max(intensity, _intensity); // Don't reduce active shake
        _duration = duration;
        _elapsed = 0f;
    }

    /// <summary>Call every frame. Returns offset to add to camera position.</summary>
    public void Update(float dt)
    {
        if (_elapsed >= _duration)
        {
            Offset = Vector2.Zero;
            _intensity = 0f;
            return;
        }

        _elapsed += dt;
        float progress = _elapsed / _duration;
        float currentIntensity = _intensity * (1f - progress); // Linear decay

        float offsetX = ((float)_rng.NextDouble() * 2f - 1f) * currentIntensity;
        float offsetY = ((float)_rng.NextDouble() * 2f - 1f) * currentIntensity;
        Offset = new Vector2(offsetX, offsetY);
    }
}
```

**Usage:**

```csharp
// In your camera update (AFTER follow + clamp):
_shake.Update(dt);
camera.Position += _shake.Offset;
```

### Perlin Noise Shake (Smooth, Cinematic Feel)

Random shake feels violent and jittery. Perlin noise shake feels like an earthquake — smooth oscillations with organic variation. Better for sustained effects (explosions, boss stomps, environmental rumble).

```csharp
/// <summary>
/// Smooth camera shake using Perlin-style noise.
/// Produces natural-feeling oscillations instead of random jitter.
/// </summary>
public sealed class PerlinShake
{
    private float _intensity;
    private float _duration;
    private float _elapsed;
    private float _seed;
    private readonly Random _rng = new();

    public Vector2 Offset { get; private set; }

    public void Start(float intensity, float duration)
    {
        _intensity = MathF.Max(intensity, _intensity);
        _duration = duration;
        _elapsed = 0f;
        _seed = (float)_rng.NextDouble() * 1000f;
    }

    public void Update(float dt)
    {
        if (_elapsed >= _duration)
        {
            Offset = Vector2.Zero;
            _intensity = 0f;
            return;
        }

        _elapsed += dt;
        float progress = _elapsed / _duration;
        float decay = 1f - progress * progress; // Quadratic decay (fast start, slow end)

        // Sample noise at different offsets for X and Y
        float frequency = 25f; // Higher = more oscillations per second
        float noiseX = SampleNoise(_seed + _elapsed * frequency);
        float noiseY = SampleNoise(_seed + 100f + _elapsed * frequency);

        Offset = new Vector2(noiseX, noiseY) * _intensity * decay;
    }

    /// <summary>Simple sine-based noise approximation. Replace with real Perlin for better results.</summary>
    private static float SampleNoise(float t)
    {
        // Layer multiple sine waves at different frequencies for organic feel
        return MathF.Sin(t * 1.0f) * 0.5f
             + MathF.Sin(t * 2.3f) * 0.3f
             + MathF.Sin(t * 4.7f) * 0.2f;
    }
}
```

### Shake Comparison

| Type | Feel | Best For |
|------|------|----------|
| Random | Violent, jittery | Impact hits, gunfire, small explosions |
| Perlin/Sine | Smooth, rolling | Earthquakes, boss stomps, sustained effects |
| Directional | Focused impact | Knockback, directional explosions |

### Directional Shake

For impacts that come from a specific direction (sword hit from the left, explosion to the right):

```csharp
/// <summary>Shake biased in a specific direction.</summary>
public void StartDirectional(float intensity, float duration, Vector2 direction)
{
    _intensity = intensity;
    _duration = duration;
    _elapsed = 0f;
    _direction = Vector2.Normalize(direction);
}

// In Update: bias the offset toward _direction
float biasStrength = 0.7f; // 0 = no bias (random), 1 = fully directional
Vector2 randomOffset = new Vector2(
    ((float)_rng.NextDouble() * 2f - 1f),
    ((float)_rng.NextDouble() * 2f - 1f));
Offset = Vector2.Lerp(randomOffset, _direction, biasStrength) * currentIntensity;
```

**Shake in screen-space vs world-space:** The above applies shake in world-space (moves the camera). For screen-space shake (offset the final render), apply the offset to the SpriteBatch transform instead. World-space shake is simpler and looks correct with parallax layers.

---

## Camera Zoom

### Smooth Zoom

```csharp
/// <summary>Smoothly zoom toward a target zoom level.</summary>
public void UpdateZoom(OrthographicCamera camera, float targetZoom, float dt)
{
    float t = 1f - MathF.Pow(0.001f, dt); // Exponential decay
    camera.Zoom = MathHelper.Lerp(camera.Zoom, targetZoom, t);

    // Clamp to prevent extreme values
    camera.Zoom = MathHelper.Clamp(camera.Zoom, 0.25f, 4f);
}
```

### Scroll Wheel Zoom (Desktop)

```csharp
// In Update, read scroll wheel delta
int scrollDelta = Mouse.GetState().ScrollWheelValue - _previousScrollValue;
_previousScrollValue = Mouse.GetState().ScrollWheelValue;

if (scrollDelta != 0)
{
    float zoomDelta = scrollDelta > 0 ? 0.1f : -0.1f;
    _targetZoom = MathHelper.Clamp(_targetZoom + zoomDelta, 0.25f, 4f);
}
```

### Zoom to Point

When zooming with the scroll wheel, zoom toward the mouse cursor (not the screen center) for a natural feel:

```csharp
/// <summary>Zoom toward a specific world point (e.g., mouse cursor position).</summary>
public void ZoomToPoint(OrthographicCamera camera, Vector2 worldPoint, float newZoom)
{
    // Get the world point under the cursor before zoom
    Vector2 beforeZoom = worldPoint;

    camera.Zoom = newZoom;

    // Get where that same screen point maps to after zoom
    Vector2 afterZoom = camera.ScreenToWorld(camera.WorldToScreen(beforeZoom));

    // Adjust camera to keep the point in the same screen location
    camera.Position += beforeZoom - afterZoom;
}
```

### Zoom Levels for Genre

| Genre | Min Zoom | Default | Max Zoom | Notes |
|-------|----------|---------|----------|-------|
| Platformer | 0.8 | 1.0 | 1.5 | Narrow range, pixel-perfect |
| Top-down RPG | 0.5 | 1.0 | 2.0 | Zoom out for exploration |
| Strategy/RTS | 0.25 | 0.5 | 2.0 | Wide range, overview to detail |
| Editor/Level design | 0.1 | 1.0 | 4.0 | Full range for tooling |

---

## Multi-Target Camera

Frame multiple entities (players in co-op, player + boss, group of enemies). The camera calculates a bounding box around all targets, then positions and zooms to fit them.

```
  ┌─────────────────────────────────┐
  │                                 │
  │    ☺ Player 1                   │
  │         ┌───────────────┐       │
  │         │  Bounding Box │       │
  │         │    (padded)   │       │
  │         │           ☠   │       │
  │         │         Boss  │       │
  │         └───────────────┘       │
  │                   ☺ Player 2    │
  │                                 │
  │  Camera zooms to fit all targets│
  └─────────────────────────────────┘
```

```csharp
/// <summary>
/// Camera that frames multiple targets by adjusting position and zoom.
/// Useful for co-op, boss fights, or any scene with multiple points of interest.
/// </summary>
public sealed class MultiTargetCamera
{
    private readonly OrthographicCamera _camera;
    private readonly int _viewWidth;
    private readonly int _viewHeight;
    private readonly float _padding;
    private readonly float _minZoom;
    private readonly float _maxZoom;

    public MultiTargetCamera(OrthographicCamera camera, int viewWidth, int viewHeight,
        float padding = 80f, float minZoom = 0.3f, float maxZoom = 1.5f)
    {
        _camera = camera;
        _viewWidth = viewWidth;
        _viewHeight = viewHeight;
        _padding = padding;
        _minZoom = minZoom;
        _maxZoom = maxZoom;
    }

    /// <summary>Update camera to frame all target positions.</summary>
    public void Update(ReadOnlySpan<Vector2> targets, float dt)
    {
        if (targets.Length == 0) return;
        if (targets.Length == 1)
        {
            // Single target: just follow
            float t = 1f - MathF.Pow(0.01f, dt);
            _camera.Position = Vector2.Lerp(_camera.Position, targets[0], t);
            return;
        }

        // Calculate bounding box of all targets
        Vector2 min = targets[0];
        Vector2 max = targets[0];
        for (int i = 1; i < targets.Length; i++)
        {
            min = Vector2.Min(min, targets[i]);
            max = Vector2.Max(max, targets[i]);
        }

        // Center point
        Vector2 center = (min + max) * 0.5f;

        // Required size (with padding)
        float requiredWidth = (max.X - min.X) + _padding * 2f;
        float requiredHeight = (max.Y - min.Y) + _padding * 2f;

        // Calculate zoom to fit
        float zoomX = _viewWidth / requiredWidth;
        float zoomY = _viewHeight / requiredHeight;
        float targetZoom = MathHelper.Clamp(MathF.Min(zoomX, zoomY), _minZoom, _maxZoom);

        // Smooth interpolation
        float t2 = 1f - MathF.Pow(0.005f, dt);
        _camera.Position = Vector2.Lerp(_camera.Position, center, t2);
        _camera.Zoom = MathHelper.Lerp(_camera.Zoom, targetZoom, t2);
    }
}
```

**Usage in a boss fight:**

```csharp
// Frame both the player and the boss
Span<Vector2> targets = stackalloc Vector2[2];
targets[0] = playerPosition;
targets[1] = bossPosition;
_multiTargetCamera.Update(targets, dt);
```

**Weighted targets:** To keep the player more centered than the boss, use a weighted average instead of a simple center:

```csharp
// 70% weight on player, 30% on boss
Vector2 weightedCenter = playerPos * 0.7f + bossPos * 0.3f;
```

---

## Cinematic Camera

Scripted camera movements for cutscenes, intros, and dramatic moments. Uses waypoints with easing curves for smooth paths.

```csharp
/// <summary>A waypoint along a cinematic camera path.</summary>
public readonly record struct CameraWaypoint(
    Vector2 Position,
    float Zoom,
    float Duration,     // Seconds to travel to this waypoint
    EaseType Easing     // How to interpolate (see G41 Tweening)
);

/// <summary>Plays a sequence of camera waypoints for cinematic sequences.</summary>
public sealed class CinematicCamera
{
    private readonly OrthographicCamera _camera;
    private CameraWaypoint[] _waypoints = Array.Empty<CameraWaypoint>();
    private int _currentIndex;
    private float _elapsed;
    private Vector2 _startPosition;
    private float _startZoom;

    public bool IsPlaying { get; private set; }

    /// <summary>Fires when the cinematic sequence completes.</summary>
    public event Action? OnComplete;

    public CinematicCamera(OrthographicCamera camera)
    {
        _camera = camera;
    }

    /// <summary>Start a cinematic sequence from the camera's current position.</summary>
    public void Play(CameraWaypoint[] waypoints)
    {
        _waypoints = waypoints;
        _currentIndex = 0;
        _elapsed = 0f;
        _startPosition = _camera.Position;
        _startZoom = _camera.Zoom;
        IsPlaying = true;
    }

    public void Stop()
    {
        IsPlaying = false;
    }

    public void Update(float dt)
    {
        if (!IsPlaying || _currentIndex >= _waypoints.Length) return;

        ref readonly var wp = ref _waypoints[_currentIndex];
        _elapsed += dt;

        float t = MathHelper.Clamp(_elapsed / wp.Duration, 0f, 1f);
        float eased = Ease.Apply(wp.Easing, t); // See G41 Tweening for easing functions

        _camera.Position = Vector2.Lerp(_startPosition, wp.Position, eased);
        _camera.Zoom = MathHelper.Lerp(_startZoom, wp.Zoom, eased);

        if (t >= 1f)
        {
            // Move to next waypoint
            _startPosition = _camera.Position;
            _startZoom = _camera.Zoom;
            _elapsed = 0f;
            _currentIndex++;

            if (_currentIndex >= _waypoints.Length)
            {
                IsPlaying = false;
                OnComplete?.Invoke();
            }
        }
    }
}
```

**Example: Boss intro cinematic**

```csharp
var cinematic = new CinematicCamera(camera);
cinematic.Play(new[]
{
    // Pan from player to boss over 2 seconds
    new CameraWaypoint(bossPosition, 1.0f, 2.0f, EaseType.CubicInOut),
    // Zoom in on boss face
    new CameraWaypoint(bossPosition, 1.8f, 1.0f, EaseType.CubicOut),
    // Hold for 1 second
    new CameraWaypoint(bossPosition, 1.8f, 1.0f, EaseType.Linear),
    // Pull back to gameplay view
    new CameraWaypoint(playerPosition, 1.0f, 1.5f, EaseType.CubicInOut),
});
cinematic.OnComplete += () => _gameState = GameState.Playing;
```

For easing functions, see [G41 Tweening & Interpolation](./G41_tweening.md).

---

## Camera Transitions

Smooth transitions between camera modes or between rooms/scenes.

### Fade Transition

```csharp
/// <summary>Fades the screen to black and back during camera repositioning.</summary>
public sealed class FadeTransition
{
    private float _alpha;       // 0 = clear, 1 = black
    private float _fadeSpeed;
    private TransitionPhase _phase;
    private Action? _onMidpoint; // Called at peak darkness

    private enum TransitionPhase { None, FadeOut, FadeIn }

    /// <summary>Start a fade transition. midpointAction runs when screen is fully black.</summary>
    public void Start(float duration, Action midpointAction)
    {
        _fadeSpeed = 2f / duration; // Half duration fade out, half fade in
        _phase = TransitionPhase.FadeOut;
        _onMidpoint = midpointAction;
        _alpha = 0f;
    }

    public void Update(float dt)
    {
        switch (_phase)
        {
            case TransitionPhase.FadeOut:
                _alpha += _fadeSpeed * dt;
                if (_alpha >= 1f)
                {
                    _alpha = 1f;
                    _onMidpoint?.Invoke(); // Reposition camera, load room, etc.
                    _phase = TransitionPhase.FadeIn;
                }
                break;

            case TransitionPhase.FadeIn:
                _alpha -= _fadeSpeed * dt;
                if (_alpha <= 0f)
                {
                    _alpha = 0f;
                    _phase = TransitionPhase.None;
                }
                break;
        }
    }

    /// <summary>Draw the fade overlay LAST, on top of everything.</summary>
    public void Draw(SpriteBatch spriteBatch, Texture2D pixel, int screenWidth, int screenHeight)
    {
        if (_alpha <= 0f) return;
        spriteBatch.Draw(pixel,
            new Rectangle(0, 0, screenWidth, screenHeight),
            Color.Black * _alpha);
    }
}
```

**Usage (room transition):**

```csharp
_fade.Start(duration: 0.6f, midpointAction: () =>
{
    _currentRoom = nextRoom;
    camera.Position = nextRoom.SpawnPoint;
});
```

### Smooth Camera Cut (Instant Reposition with Smoothing)

For seamless room transitions without a fade (Metroidvania-style):

```csharp
/// <summary>
/// Instantly reposition the camera but smooth the transition so it
/// doesn't look like a hard cut. The camera slides to the new position.
/// </summary>
public void SmoothCut(OrthographicCamera camera, Vector2 newTarget, float slideDuration = 0.4f)
{
    // Store as a "slide" — the camera follow system smooths to it automatically
    // if using exponential smoothing. Just set a faster smoothing factor temporarily.
    _overrideTarget = newTarget;
    _overrideSmoothFactor = 0.0001f; // Very aggressive smoothing
    _overrideTimer = slideDuration;
}
```

---

## Split Screen (Multiple Cameras)

For local multiplayer, render each player's view to a separate viewport region.

```csharp
/// <summary>Draw a split-screen view for two players.</summary>
public void DrawSplitScreen(SpriteBatch spriteBatch,
    OrthographicCamera camera1, OrthographicCamera camera2,
    Action<SpriteBatch> drawWorld)
{
    int halfWidth = GraphicsDevice.Viewport.Width / 2;
    int fullHeight = GraphicsDevice.Viewport.Height;

    // Player 1: left half
    GraphicsDevice.Viewport = new Viewport(0, 0, halfWidth, fullHeight);
    spriteBatch.Begin(transformMatrix: camera1.GetViewMatrix(),
        samplerState: SamplerState.PointClamp);
    drawWorld(spriteBatch);
    spriteBatch.End();

    // Player 2: right half
    GraphicsDevice.Viewport = new Viewport(halfWidth, 0, halfWidth, fullHeight);
    spriteBatch.Begin(transformMatrix: camera2.GetViewMatrix(),
        samplerState: SamplerState.PointClamp);
    drawWorld(spriteBatch);
    spriteBatch.End();

    // Restore full viewport for UI
    GraphicsDevice.Viewport = new Viewport(0, 0, halfWidth * 2, fullHeight);
}
```

**4-player split screen:** Same pattern but divide into quadrants: `(0,0)`, `(halfW,0)`, `(0,halfH)`, `(halfW,halfH)`.

**Dynamic split/merge:** When both players are close enough, merge to a single camera (like Lego games). Use the multi-target camera above when distance < threshold, split screen when distance > threshold + hysteresis.

```csharp
float distance = Vector2.Distance(player1Pos, player2Pos);
const float SplitThreshold = 400f;
const float MergeThreshold = 300f; // Hysteresis prevents flickering

if (_isSplit && distance < MergeThreshold)
    _isSplit = false; // Merge to single camera
else if (!_isSplit && distance > SplitThreshold)
    _isSplit = true;  // Split into two cameras
```

---

## Frustum Culling with Camera

Use the camera's bounding rectangle to skip rendering objects outside the view:

```csharp
RectangleF visibleArea = camera.BoundingRectangle;

// Pad the visible area slightly to prevent pop-in at edges
float cullPadding = 32f; // One tile width
visibleArea.Inflate(cullPadding, cullPadding);

// In your render system query:
world.Query(in renderQuery, (ref Position pos, ref Sprite sprite) =>
{
    RectangleF spriteBounds = new(pos.X - sprite.Origin.X, pos.Y - sprite.Origin.Y,
        sprite.Width, sprite.Height);

    if (!visibleArea.Intersects(spriteBounds))
        return; // Skip — not visible

    spriteBatch.Draw(sprite.Texture, new Vector2(pos.X, pos.Y), Color.White);
});
```

This typically eliminates 50-80% of draw calls. The padding prevents objects from "popping" in at screen edges as the camera moves. See [G15 Game Loop](./G15_game_loop.md) for more on culling and batching.

---

## Camera Priority Stack

Production games need multiple systems fighting for camera control: gameplay follow, cinematic sequences, zoom triggers, boss arenas. A priority stack resolves conflicts cleanly.

```csharp
/// <summary>
/// Camera behavior with a priority level.
/// Higher priority behaviors override lower ones.
/// </summary>
public abstract class CameraBehavior : IComparable<CameraBehavior>
{
    public int Priority { get; init; }
    public bool IsActive { get; set; }

    public abstract Vector2 GetDesiredPosition(float dt);
    public abstract float GetDesiredZoom(float dt);

    public int CompareTo(CameraBehavior? other) =>
        (other?.Priority ?? 0).CompareTo(Priority); // Descending
}

/// <summary>
/// Manages camera behaviors by priority. The highest-priority active behavior
/// controls the camera. Supports smooth blending between behaviors.
/// </summary>
public sealed class CameraStack
{
    private readonly OrthographicCamera _camera;
    private readonly SortedSet<CameraBehavior> _behaviors = new();
    private CameraBehavior? _activeBehavior;
    private float _blendTimer;
    private float _blendDuration = 0.3f;
    private Vector2 _blendStartPos;
    private float _blendStartZoom;

    public CameraStack(OrthographicCamera camera) => _camera = camera;

    public void Add(CameraBehavior behavior) => _behaviors.Add(behavior);
    public void Remove(CameraBehavior behavior) => _behaviors.Remove(behavior);

    public void Update(float dt)
    {
        // Find highest-priority active behavior
        CameraBehavior? best = null;
        foreach (var b in _behaviors)
        {
            if (b.IsActive) { best = b; break; }
        }

        if (best != _activeBehavior)
        {
            // Behavior changed — start blend
            _blendStartPos = _camera.Position;
            _blendStartZoom = _camera.Zoom;
            _blendTimer = 0f;
            _activeBehavior = best;
        }

        if (_activeBehavior == null) return;

        Vector2 targetPos = _activeBehavior.GetDesiredPosition(dt);
        float targetZoom = _activeBehavior.GetDesiredZoom(dt);

        if (_blendTimer < _blendDuration)
        {
            // Blending between old and new behavior
            _blendTimer += dt;
            float t = MathHelper.Clamp(_blendTimer / _blendDuration, 0f, 1f);
            float eased = t * t * (3f - 2f * t); // Smoothstep

            _camera.Position = Vector2.Lerp(_blendStartPos, targetPos, eased);
            _camera.Zoom = MathHelper.Lerp(_blendStartZoom, targetZoom, eased);
        }
        else
        {
            _camera.Position = targetPos;
            _camera.Zoom = targetZoom;
        }
    }
}
```

**Example behaviors:**

```csharp
// Normal gameplay: priority 0
var followBehavior = new FollowBehavior { Priority = 0, IsActive = true };

// Boss arena lock: priority 10 (overrides follow)
var arenaLock = new ArenaCameraBehavior(arenaCenter, arenaZoom) { Priority = 10 };
arenaLock.IsActive = false; // Activate when boss fight starts

// Cinematic: priority 100 (overrides everything)
var cinematic = new CinematicBehavior(waypoints) { Priority = 100 };
cinematic.IsActive = false; // Activate during cutscene
```

---

## ECS Integration

### Components

```csharp
/// <summary>Tag: this entity is the camera's follow target.</summary>
public struct PlayerTag { }

/// <summary>Which direction the entity is facing (for camera look-ahead).</summary>
public struct FacingDirection { public float X; public float Y; }

/// <summary>Tag: entity defines a camera zone (boss arena, trigger region).</summary>
public struct CameraZone
{
    public Rectangle Bounds;   // Zone area
    public float Zoom;         // Override zoom (0 = no override)
    public bool LockToCenter;  // Lock camera to zone center?
}
```

### CameraFollowSystem

The system takes a `VirtualResolution` reference and reads `VirtualWidth`/`VirtualHeight` each frame. With expand mode, these dimensions change on window resize, so the camera's view bounds always match the actual visible area.

```csharp
/// <summary>
/// Smoothly follows the player with frame-rate independent exponential decay.
/// Applies look-ahead offset based on facing direction.
/// Clamps camera to map bounds using dynamic virtual resolution dimensions.
/// </summary>
public partial class CameraFollowSystem : BaseSystem<World, float>
{
    private const float LeadDistance = 40f;

    private readonly OrthographicCamera _camera;
    private readonly Rectangle _mapBounds;
    private readonly VirtualResolution _virtualRes;

    public CameraFollowSystem(World world, OrthographicCamera camera,
        Rectangle mapBounds, VirtualResolution virtualRes) : base(world)
    {
        _camera = camera;
        _mapBounds = mapBounds;
        _virtualRes = virtualRes;
    }

    [Query]
    [All<PlayerTag>]
    private void FollowPlayer([Data] in float dt, in Position pos, in FacingDirection facing)
    {
        // Read dynamic dimensions each frame (expand mode resizes on window change)
        int viewWidth = _virtualRes.VirtualWidth;
        int viewHeight = _virtualRes.VirtualHeight;

        // Frame-rate independent exponential smoothing (lower base = smoother)
        float t = 1f - MathF.Pow(0.005f, dt);
        Vector2 playerPos = new(pos.X, pos.Y);
        Vector2 lead = new(facing.X * LeadDistance, facing.Y * LeadDistance);

        // Camera.Position is top-left; target the center of the view
        Vector2 center = playerPos + lead;
        Vector2 targetPos = center - new Vector2(viewWidth / 2f, viewHeight / 2f);
        _camera.Position = Vector2.Lerp(_camera.Position, targetPos, t);

        // Clamp — visible area spans [Position, Position + viewSize]
        float clampedX = MathHelper.Clamp(_camera.Position.X,
            _mapBounds.Left, _mapBounds.Right - viewWidth);
        float clampedY = MathHelper.Clamp(_camera.Position.Y,
            _mapBounds.Top, _mapBounds.Bottom - viewHeight);
        _camera.Position = new Vector2(clampedX, clampedY);
    }
}
```

> **Key pattern:** Don't put view dimensions in a component or cache them. Pass `VirtualResolution` to the system constructor and read `VirtualWidth`/`VirtualHeight` in the query method. This guarantees correct bounds after every window resize.

### CameraZoneSystem

Detects when the player enters a camera zone and overrides camera behavior:

```csharp
/// <summary>Checks if the player is inside any camera zone and applies overrides.</summary>
public partial class CameraZoneSystem : BaseSystem<World, float>
{
    private readonly OrthographicCamera _camera;
    private CameraZone? _activeZone;

    public CameraZoneSystem(World world, OrthographicCamera camera) : base(world)
    {
        _camera = camera;
    }

    [Query]
    [All<PlayerTag>]
    private void CheckZones([Data] in float dt, in Position playerPos)
    {
        CameraZone? newZone = null;

        // Find the zone the player is inside
        World.Query(in _zoneQuery, (ref CameraZone zone) =>
        {
            if (zone.Bounds.Contains((int)playerPos.X, (int)playerPos.Y))
                newZone = zone;
        });

        if (newZone.HasValue)
        {
            var zone = newZone.Value;
            if (zone.LockToCenter)
            {
                float t = 1f - MathF.Pow(0.01f, dt);
                Vector2 center = zone.Bounds.Center.ToVector2();
                _camera.Position = Vector2.Lerp(_camera.Position, center, t);
            }
            if (zone.Zoom > 0f)
            {
                float t = 1f - MathF.Pow(0.01f, dt);
                _camera.Zoom = MathHelper.Lerp(_camera.Zoom, zone.Zoom, t);
            }
        }
    }
}
```

---

## Troubleshooting

### Camera jitters or vibrates

**Symptom:** Camera shakes slightly every frame, especially during movement.

**Causes & fixes:**
1. **Sub-pixel rendering on pixel art** → Round camera position to integers: `camera.Position = new Vector2(MathF.Round(x), MathF.Round(y));`
2. **Frame-rate dependent smoothing** → Use exponential decay (`1 - base^dt`) instead of `speed * dt`
3. **Follow + clamp fighting** → Ensure follow and clamp don't alternate dominance. If the target is near map edge, the follow pushes one way and clamp pushes back every frame. Solution: apply follow, then clamp, and don't re-follow after clamp.
4. **Floating point accumulation** → After many hours of gameplay, camera position may lose precision. Periodically recenter the world origin for infinite-world games.

### Camera shows black/empty space at edges

**Symptom:** Pulling the camera past the map edge reveals empty space.

**Fix:** Ensure `ClampToMapBounds` is called AFTER follow logic. If using VirtualResolution expand mode, make sure you're reading `VirtualWidth`/`VirtualHeight` (not hardcoded values) — they change on window resize.

### Camera shake doesn't work at map edges

**Symptom:** Shake is weak or absent when the player is near map boundaries.

**Fix:** Apply shake AFTER clamping (see pipeline diagram above). If clamp runs after shake, it undoes the shake offset. Alternatively, apply shake as a screen-space offset on the SpriteBatch transform matrix:

```csharp
// Screen-space shake (immune to clamping)
Matrix shakeMatrix = Matrix.CreateTranslation(_shake.Offset.X, _shake.Offset.Y, 0f);
spriteBatch.Begin(transformMatrix: camera.GetViewMatrix() * shakeMatrix);
```

### Zoom changes visible area unexpectedly

**Symptom:** After zooming, frustum culling misses objects or the clamp bounds are wrong.

**Fix:** Recalculate the visible area dimensions after every zoom change. `BoundingRectangle` updates automatically, but any cached view dimensions need refreshing. Frustum culling padding should account for the zoom level.

### Split screen performance is poor

**Symptom:** Frame rate drops by more than 50% with split screen.

**Fix:** Each viewport renders the full scene — double the draw calls for two players. Solutions:
1. Use frustum culling per viewport (each camera's `BoundingRectangle` is different)
2. Reduce particle counts in split screen mode
3. Lower the render resolution per viewport
4. See [G33 Profiling & Optimization](./G33_profiling_optimization.md) for detailed draw call reduction

### Camera snaps when entering new room/zone

**Symptom:** Hard visual cut when transitioning between camera zones.

**Fix:** Use smooth blending when entering/exiting zones. The `CameraStack` (above) handles this with its blend timer. Alternatively, use a fade transition to hide the repositioning.

### Integer vs float position confusion

**Symptom:** Camera is off by half a pixel, or objects appear to shift when the camera moves.

**Fix:** Be consistent. If your game uses integer tile positions but float camera positions, rounding mismatches cause shimmer. Either:
- Use float everywhere and round only at the final render step
- Use integer positions everywhere and accept the constraint
- See [G21 Coordinate Systems](./G21_coordinate_systems.md) for the full conversion chain

---

## See Also

- **Theory:** [Camera Theory (engine-agnostic)](../../core/concepts/camera-theory.md) — Mathematical foundations, smoothing derivations, pattern catalog
- **Resolution:** [G19 Display, Resolution & Viewports](./G19_display_resolution_viewports.md) — Virtual resolution interacts with camera
- **Coordinates:** [G21 Coordinate Systems & Transforms](./G21_coordinate_systems.md) — Full conversion chain from screen to world
- **Parallax:** [G22 Parallax & Depth Layers](./G22_parallax_depth_layers.md) — Parallax layers use camera position
- **Rendering:** [G2 Rendering & Graphics](./G2_rendering_and_graphics.md) — Render pipeline overview
- **Tweening:** [G41 Tweening & Interpolation](./G41_tweening.md) — Easing functions for cinematic camera
- **Game Feel:** [G30 Game Feel & Juice](./G30_game_feel_tooling.md) — Camera shake as game feel tool
- **Tilemaps:** [G37 Tilemap Systems](./G37_tilemap_systems.md) — Map bounds for camera clamping
- **Side-Scrolling:** [G56 Side-Scrolling Perspective](./G56_side_scrolling.md) — Platformer camera patterns in context
- **Top-Down:** [G28 Top-Down Perspective](./G28_top_down_perspective.md) — Top-down camera patterns in context
- **Profiling:** [G33 Profiling & Optimization](./G33_profiling_optimization.md) — Camera-related performance (culling, split screen)
