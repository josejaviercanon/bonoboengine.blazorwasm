# G21 — Coordinate Systems & Transforms


> **Category:** Guide · **Related:** [G19 Display & Resolution](./G19_display_resolution_viewports.md) · [G20 Camera Systems](./G20_camera_systems.md) · [G7 Input Handling](./G7_input_handling.md) · [G25 Safe Areas](./G25_safe_areas_adaptive_layout.md)

---

## The Four Coordinate Spaces

Every 2D game has four distinct coordinate spaces. Confusing them is the #1 source of "my click doesn't hit the right thing" and "my UI is in the wrong place" bugs.

```
Screen Space (display pixels)
    ↕  VirtualResolution.ScreenToVirtual()
Viewport Space (virtual/design resolution pixels)
    ↕  Camera.ScreenToWorld() / Camera.WorldToScreen()
World Space (game units)
    ↕  Entity parent transforms
Local Space (relative to parent)
```

### 1. Screen Space

**Origin:** Top-left of the OS window (desktop) or device screen (iOS).
**Units:** Physical display pixels.
**Range:** (0,0) to (Window.ClientBounds.Width, Window.ClientBounds.Height) on desktop. On iOS, (0,0) to (BackBufferWidth, BackBufferHeight) in native pixels.
**Used for:** Raw mouse/touch input (`Mouse.GetState().Position`, `TouchPanel.GetState()`).

### 2. Viewport Space

**Origin:** Top-left of the virtual resolution canvas.
**Units:** Design resolution pixels (e.g., 1920x1080 if that's your virtual resolution).
**Range:** (0,0) to (VirtualWidth, VirtualHeight).
**Used for:** Screen-space UI positioning, HUD elements, anything that doesn't scroll with the camera.

If you're not using a virtual resolution system (rendering directly at display resolution), then viewport space = screen space.

### 3. World Space

**Origin:** The global origin of your game world (typically top-left of the map, or center if you prefer).
**Units:** Game units (typically pixels, but could be tiles or meters for physics).
**Range:** Unbounded — extends as far as your world goes.
**Used for:** Entity positions, tile positions, collision queries, physics simulation, pathfinding.

### 4. Local Space

**Origin:** The parent entity's position.
**Units:** Same as world space.
**Used for:** Weapon attachment offsets, particle spawn offsets, child sprites relative to a parent entity.

---

## MonoGame's Coordinate System

MonoGame uses a **top-left origin with Y-axis pointing down**:

```
(0,0) ─────────────── X+
  │
  │      Screen / World
  │
  Y+
```

- **X increases** to the right
- **Y increases** downward
- **Rotation** is clockwise (positive radians rotate clockwise)

This matches most 2D frameworks and screen coordinate conventions. If your game uses a physics engine with Y-up (like Box2D/Aether), you need to negate Y when converting between physics space and render space.

---

## Conversion Functions

### Screen → World (The Full Chain)

This is what you need for mouse picking, touch input, clicking on entities:

```csharp
/// <summary>
/// Convert raw screen-space input (mouse/touch) to world coordinates.
/// Handles virtual resolution scaling and camera transformation.
/// </summary>
public Vector2 ScreenToWorld(Vector2 screenPosition)
{
    // Step 1: Screen space → Viewport space (undo virtual resolution scaling)
    Vector2 viewportPos = _virtualResolution.ScreenToVirtual(screenPosition);

    // Step 2: Viewport space → World space (undo camera transformation)
    Vector2 worldPos = _camera.ScreenToWorld(viewportPos);

    return worldPos;
}
```

**Critical:** Both steps are required when using a virtual resolution system. If you skip step 1, your input will be offset and scaled incorrectly on any display that doesn't exactly match your design resolution.

### World → Screen (The Reverse)

This is what you need for drawing UI elements at entity positions (health bars, name labels, damage numbers):

```csharp
/// <summary>
/// Convert a world position to screen-space coordinates.
/// Used for placing UI elements at entity positions.
/// </summary>
public Vector2 WorldToScreen(Vector2 worldPosition)
{
    // Step 1: World space → Viewport space (apply camera transformation)
    Vector2 viewportPos = _camera.WorldToScreen(worldPosition);

    // Step 2: Viewport space → Screen space (apply virtual resolution scaling)
    Vector2 screenPos = _virtualResolution.VirtualToScreen(viewportPos);

    return screenPos;
}
```

### Viewport ↔ World (No Virtual Resolution)

If you're not using a virtual resolution system, the conversion simplifies to the camera transform only:

```csharp
Vector2 worldPos = camera.ScreenToWorld(mousePosition);
Vector2 screenPos = camera.WorldToScreen(entityPosition);
```

---

## Transform Matrices

### The Camera View Matrix

`OrthographicCamera.GetViewMatrix()` returns a matrix that transforms from world space to viewport space. It encodes translation (camera position), rotation, and zoom (scale).

```csharp
// What GetViewMatrix() computes internally:
Matrix viewMatrix =
    Matrix.CreateTranslation(-camera.Position.X, -camera.Position.Y, 0f)
    * Matrix.CreateRotationZ(-camera.Rotation)
    * Matrix.CreateScale(camera.Zoom, camera.Zoom, 1f)
    * Matrix.CreateTranslation(viewportCenter.X, viewportCenter.Y, 0f);
```

When passed to `SpriteBatch.Begin(transformMatrix: viewMatrix)`, MonoGame transforms every drawn sprite from world coordinates to viewport coordinates automatically.

### The Scale Matrix (Virtual Resolution)

The `VirtualResolution.ScaleMatrix` transforms from viewport space to screen space. This is used when drawing the final render target to the screen.

### Manual Point Transformation

If you need to transform points manually (outside of SpriteBatch), use `Vector2.Transform`:

```csharp
// World → Viewport (using camera matrix)
Vector2 viewportPos = Vector2.Transform(worldPos, camera.GetViewMatrix());

// Viewport → World (using inverse camera matrix)
Matrix inverseView = Matrix.Invert(camera.GetViewMatrix());
Vector2 worldPos = Vector2.Transform(viewportPos, inverseView);
```

---

## Touch Input on Scaled Viewports (Mobile)

On iOS, touch coordinates are in native pixels. The full conversion chain:

```csharp
TouchCollection touches = TouchPanel.GetState();
foreach (TouchLocation touch in touches)
{
    if (touch.State == TouchLocationState.Pressed)
    {
        // touch.Position is in screen pixels (native resolution)
        Vector2 screenPos = touch.Position;

        // Convert through virtual resolution
        Vector2 viewportPos = _virtualResolution.ScreenToVirtual(screenPos);

        // Convert through camera to get world position
        Vector2 worldPos = _camera.ScreenToWorld(viewportPos);

        // Now worldPos is the game-world location the player tapped
        HandleTap(worldPos);
    }
}
```

**Gotcha:** On iOS, `TouchPanel.GetState()` returns positions in the native backbuffer coordinate space. On iPhone 15 Pro, that's 2556x1179 — not UIKit points. The virtual resolution `ScreenToVirtual()` handles this correctly as long as it knows the backbuffer dimensions.

---

## Local Space and Parent-Child Transforms

Arch ECS doesn't have a built-in transform hierarchy. If you need parent-child relationships (weapon attached to player, particle relative to emitter), compose it manually:

```csharp
/// <summary>Local position offset from a parent entity.</summary>
public struct LocalOffset
{
    public float X;
    public float Y;
}

/// <summary>Reference to a parent entity for transform composition.</summary>
public struct ParentEntity
{
    public Entity Parent;
}

// In your transform system:
world.Query(in childQuery, (ref Position pos, ref LocalOffset offset, ref ParentEntity parent) =>
{
    if (!parent.Parent.IsAlive())
        return;

    ref Position parentPos = ref parent.Parent.Get<Position>();
    pos.X = parentPos.X + offset.X;
    pos.Y = parentPos.Y + offset.Y;
});
```

For rotation-aware local transforms (e.g., weapon rotates with player facing direction):

```csharp
// Rotate local offset by parent's rotation
float cos = MathF.Cos(parentRotation);
float sin = MathF.Sin(parentRotation);
float worldX = parentPos.X + (offset.X * cos - offset.Y * sin);
float worldY = parentPos.Y + (offset.X * sin + offset.Y * cos);
```

---

## Common Conversion Scenarios

| Scenario | Conversion Needed |
|----------|-------------------|
| Mouse click → select entity | Screen → Virtual → World, then spatial query |
| Health bar above entity | World → Virtual (draw in viewport-space UI pass) |
| Minimap blip | World → Minimap local (scale world coords to minimap size) |
| Touch drag on scaled mobile | Screen → Virtual → World for each touch frame |
| Place UI at screen corner | Use viewport-space constants (0,0 for top-left, etc.) |
| Damage number that doesn't scroll | World → Virtual at spawn time, then draw in UI pass |
| Debug text at mouse position | Screen → Virtual only (stays in viewport space) |

---

## Coordinate Debugging Tips

When coordinates seem wrong:

1. **Draw a crosshair at (0,0) world space** — confirms camera is looking where you think
2. **Draw a dot at raw mouse position** without camera transform — confirms screen-space input works
3. **Draw a dot at the converted world position** with camera transform — confirms conversion chain
4. **Log all four coordinate spaces** for a single click: screen, virtual, world, local

```csharp
// Quick debug overlay:
Vector2 screen = Mouse.GetState().Position.ToVector2();
Vector2 viewport = _virtualRes.ScreenToVirtual(screen);
Vector2 world = _camera.ScreenToWorld(viewport);

string debug = $"Screen: {screen}\nViewport: {viewport}\nWorld: {world}";
spriteBatch.DrawString(font, debug, Vector2.Zero, Color.Yellow);
```

---

## See Also

- [G19 Display, Resolution & Viewports](./G19_display_resolution_viewports.md) — virtual resolution setup
- [G20 Camera Systems](./G20_camera_systems.md) — camera transforms and ScreenToWorld
- [G7 Input Handling](./G7_input_handling.md) — raw input capture
- [G25 Safe Areas & Adaptive Layout](./G25_safe_areas_adaptive_layout.md) — safe area coordinate offsets on iOS
