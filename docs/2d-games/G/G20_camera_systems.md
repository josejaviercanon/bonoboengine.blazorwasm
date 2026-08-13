# G20 — Camera Systems
> **Category:** Guide · **Related:** [G2 Rendering & Graphics](./G2_rendering_and_graphics.md) · [G19 Display & Resolution](./G19_display_resolution_viewports.md) · [G21 Coordinate Systems](./G21_coordinate_systems.md) · [G22 Parallax & Depth Layers](./G22_parallax_depth_layers.md)

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
float t = 1f - MathF.Pow(0.01f, dt); // 0.01 = smoothing factor (lower = smoother)
camera.Position = Vector2.Lerp(current, targetPosition, t);
```

### 3. Deadzone (Only Move When Target Exits Region)

Define an inner rectangle around the screen center. The camera doesn't move until the target exits this region. This prevents micro-movements from causing camera jitter.

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

**Call order:** Update camera follow first, then clamp. If the map is smaller than the visible area, center the camera on the map instead of clamping.

---

## Camera Shake

Screen shake adds impact to hits, explosions, and environmental events. The approach: add a random offset to the camera position each frame, decaying over time.

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
        _intensity = intensity;
        _duration = duration;
        _elapsed = 0f;
    }

    /// <summary>Call every frame. Returns offset to add to camera position.</summary>
    public void Update(float dt)
    {
        if (_elapsed >= _duration)
        {
            Offset = Vector2.Zero;
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
// In your camera update:
_shake.Update(dt);
camera.Position = followPosition + _shake.Offset;
```

**Shake in screen-space vs world-space:** The above applies shake in world-space (moves the camera). For screen-space shake (offset the final render), apply the offset to the SpriteBatch transform instead. World-space shake is simpler and looks correct with parallax layers.

**Perlin noise shake:** For smoother, more natural-feeling shake, replace the random offsets with Perlin noise sampled at increasing time values. Random shake feels violent; Perlin shake feels like an earthquake.

---

## Camera Zoom

### Smooth Zoom

```csharp
/// <summary>Smoothly zoom toward a target zoom level.</summary>
public void UpdateZoom(OrthographicCamera camera, float targetZoom, float dt)
{
    float zoomSpeed = 5f;
    camera.Zoom = MathHelper.Lerp(camera.Zoom, targetZoom, zoomSpeed * dt);
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

---

## Frustum Culling with Camera

Use the camera's bounding rectangle to skip rendering objects outside the view:

```csharp
RectangleF visibleArea = camera.BoundingRectangle;

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

This typically eliminates 50-80% of draw calls. See [G15 Game Loop](./G15_game_loop.md) for more on culling and batching.

---

## ECS Integration

### Components

```csharp
/// <summary>Tag: this entity is the camera's follow target.</summary>
public struct PlayerTag { }

/// <summary>Which direction the entity is facing (for camera look-ahead).</summary>
public struct FacingDirection { public float X; public float Y; }
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

---

## Common Pitfalls

**Camera position vs camera center:** `OrthographicCamera.Position` is the **center** of the view, not the top-left corner. If you set it to (0,0), you'll see from (-halfWidth, -halfHeight) to (halfWidth, halfHeight).

**Integer positions for pixel art:** If your game uses pixel art, round the camera position to whole numbers to prevent sub-pixel jitter:

```csharp
camera.Position = new Vector2(MathF.Round(camera.Position.X), MathF.Round(camera.Position.Y));
```

**Shake before clamp:** Always apply camera shake after clamping to bounds, or the shake will be eaten at map edges. Or apply shake as a screen-space offset instead.

**Zoom affects visible area:** Zooming out means the `BoundingRectangle` grows, which means more objects pass frustum culling. At extreme zoom-out, you may need to limit draw distance or LOD.

---

## See Also

- [G19 Display, Resolution & Viewports](./G19_display_resolution_viewports.md) — virtual resolution interacts with camera
- [G21 Coordinate Systems & Transforms](./G21_coordinate_systems.md) — full conversion chain from screen to world
- [G22 Parallax & Depth Layers](./G22_parallax_depth_layers.md) — parallax layers use camera position
- [G2 Rendering & Graphics](./G2_rendering_and_graphics.md) — render pipeline overview
