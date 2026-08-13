# Camera Systems -- Theory & Concepts

This document covers engine-agnostic camera theory for 2D games. The concepts apply regardless of framework or language. For engine-specific implementations, see the relevant engine module.

---

## Core Camera Concepts

A 2D camera is a view transformation that maps world coordinates to screen coordinates. It is defined by:

- **Position** -- where in the world the camera is looking
- **Zoom** -- scale factor (1.0 = normal, >1 = zoomed in, <1 = zoomed out)
- **Rotation** -- angle of the view (rarely used in 2D, but available)
- **View matrix** -- the combined transformation passed to the renderer

**World-space vs screen-space rendering:** Objects in the game world (entities, tiles, particles) are drawn using the camera transform. HUD and UI elements are drawn without it, so they stay fixed on screen.

---

## Camera Follow Patterns

### 1. Direct Follow (Lock to Target)

Camera position equals the target position every frame. No smoothing, no lag.

- **Best for:** Fast-paced games where the player must always be centered, prototyping
- **Drawback:** Feels rigid; every micro-movement of the target causes camera movement

### 2. Smoothed Follow (Lerp)

Camera moves toward the target at a rate proportional to the distance. Creates a natural "catch up" feel.

```
camera_position = lerp(camera_position, target_position, smooth_speed * dt)
```

**Frame-rate independence:** Using a fixed `t` value without delta time creates frame-rate-dependent smoothing. Use exponential decay for consistent behavior across frame rates:

```
t = 1 - pow(smoothing_factor, dt)    // smoothing_factor: lower = smoother (e.g., 0.01)
camera_position = lerp(camera_position, target_position, t)
```

- **Typical smooth_speed range:** 3--8 (higher = snappier)

### 3. Deadzone

Define an inner rectangle around the screen center. The camera does not move until the target exits this region. This prevents micro-movements from causing camera jitter.

```
if target.x > camera.x + half_deadzone_width:
    camera.x = target.x - half_deadzone_width
else if target.x < camera.x - half_deadzone_width:
    camera.x = target.x + half_deadzone_width
// Same for Y axis
```

- **Best for:** Platformers, top-down RPGs -- slight player movement should not move the camera

### 4. Look-Ahead

Shift the camera ahead of the player's movement direction so they can see what is coming.

```
if target_velocity.length_squared > threshold:
    look_ahead_offset = normalize(target_velocity) * look_ahead_distance
else:
    look_ahead_offset = (0, 0)

desired_position = target_position + look_ahead_offset
camera_position = lerp(camera_position, desired_position, t)
```

- **Best for:** Platformers (look ahead horizontally), shooters (look toward aim direction)
- **Typical look-ahead distance:** 60--120 pixels

---

## Camera Bounds (Clamping to Map)

Prevent the camera from showing areas outside the world by clamping the camera position so the visible rectangle stays within map bounds.

```
clamped_x = clamp(camera.x, map_left, map_right - view_width)
clamped_y = clamp(camera.y, map_top, map_bottom - view_height)
```

**Call order:** Update camera follow first, then clamp. If the map is smaller than the visible area, center the camera on the map instead of clamping.

**Dynamic view dimensions:** If your game supports window resizing or virtual resolution scaling, read the view dimensions each frame rather than caching them.

---

## Camera Shake

Screen shake adds impact to hits, explosions, and events. The approach: add a random offset to the camera position each frame, decaying over time.

```
function update_shake(dt):
    elapsed += dt
    progress = elapsed / duration
    current_intensity = base_intensity * (1 - progress)    // linear decay

    offset_x = random(-1, 1) * current_intensity
    offset_y = random(-1, 1) * current_intensity

// Apply after follow + clamp:
camera_position = follow_position + shake_offset
```

**Key concepts:**

- **Random shake** feels violent and impactful (good for explosions)
- **Perlin noise shake** feels smoother and more natural (good for earthquakes, rumbling)
- **World-space vs screen-space shake:** World-space moves the camera (simpler, works with parallax). Screen-space offsets the final render
- **Shake after clamp pitfall:** If you apply shake before clamping to bounds, the shake gets eaten at map edges. Apply shake after clamping, or use screen-space shake

---

## Camera Zoom

### Smooth Zoom

```
camera.zoom = lerp(camera.zoom, target_zoom, zoom_speed * dt)
```

### Zoom to Point

When zooming with the scroll wheel, zoom toward the mouse cursor (not the screen center) for a natural feel. The technique: record the world point under the cursor before zoom, apply zoom, then adjust the camera position so that same world point stays under the cursor.

---

## Multi-Target Camera

For games that need to frame multiple targets (co-op, boss fights):

1. Compute the bounding box of all targets
2. Set camera position to the center of that bounding box
3. Set zoom so the bounding box fits within the view (with padding)

---

## Frustum Culling

Use the camera's visible rectangle to skip rendering objects outside the view. Test each object's bounds against the camera bounds; if they do not intersect, skip the draw call. This typically eliminates 50--80% of draw calls.

---

## Split Screen

For local multiplayer, render each player's view to a separate viewport region. Each player gets their own camera instance targeting their character. Restore the full viewport for shared UI.

---

## Common Pitfalls

- **Camera position semantics:** Some engines treat camera position as the center of the view, others as the top-left corner. Know which your engine uses
- **Integer positions for pixel art:** Round camera position to whole numbers to prevent sub-pixel jitter
- **Zoom affects visible area:** Zooming out means more objects pass frustum culling. At extreme zoom-out levels, you may need draw distance limits or LOD

---

*Implementation examples are available in engine-specific modules.*
