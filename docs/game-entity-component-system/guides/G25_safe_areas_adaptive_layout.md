# G25 — Safe Areas & Adaptive Layout


> **Category:** Guide · **Related:** [G19 Display & Resolution](./G19_display_resolution_viewports.md) · [G24 Window & Display Management](./G24_window_display_management.md) · [G5 UI Framework](./G5_ui_framework.md) · [G21 Coordinate Systems](./G21_coordinate_systems.md)

---

## What Are Safe Areas?

Modern phones and tablets have screen regions that are partially or fully obscured by hardware:

- **Notch** (iPhone X through 14): A cutout at the top of the screen for the front camera and Face ID sensors
- **Dynamic Island** (iPhone 14 Pro+): A pill-shaped cutout at the top center that can expand for system notifications
- **Home indicator** (all Face ID iPhones): A thin bar at the bottom of the screen used for swipe-to-home gestures
- **Rounded corners** (all modern iPhones and iPads): The physical display corners clip content

The **safe area** is the region of the screen guaranteed to be fully visible and not obscured by any of these hardware elements. Placing critical UI or interaction targets outside the safe area means players can't see or tap them reliably.

---

## Safe Area Insets (iOS)

iOS provides safe area insets through UIKit. These are the distances from each screen edge to the safe area boundary, in UIKit points (not pixels).

### Getting Insets in MonoGame

```csharp
#if IOS
using UIKit;
#endif

/// <summary>
/// Returns safe area insets in native pixels (not UIKit points).
/// All values are 0 on non-iOS platforms.
/// </summary>
public static (float Top, float Bottom, float Left, float Right) GetSafeAreaInsets()
{
#if IOS
    UIEdgeInsets insets = UIApplication.SharedApplication.KeyWindow?.SafeAreaInsets
        ?? UIEdgeInsets.Zero;

    // Convert UIKit points to native pixels
    float scale = (float)UIScreen.MainScreen.Scale;
    return (
        Top: (float)insets.Top * scale,
        Bottom: (float)insets.Bottom * scale,
        Left: (float)insets.Left * scale,
        Right: (float)insets.Right * scale
    );
#else
    return (0f, 0f, 0f, 0f);
#endif
}
```

### Typical Inset Values

| Device | Top | Bottom | Left | Right | Notes |
|--------|-----|--------|------|-------|-------|
| iPhone SE (3rd) | 0 | 0 | 0 | 0 | No notch, no home indicator |
| iPhone 15 | 59 | 34 | 0 | 0 | Dynamic Island + home indicator (points) |
| iPhone 15 Pro | 59 | 34 | 0 | 0 | Dynamic Island + home indicator (points) |
| iPhone 15 Pro Max | 59 | 34 | 0 | 0 | Same insets at larger display |
| iPad (10th) | 0 | 0 | 0 | 0 | No obstructions (with home button) |
| iPad Pro (M-series) | 0 | 0 | 0 | 0 | Rounded corners only (minimal) |

**In landscape orientation**, top/bottom insets become left/right (the notch is on the side). iOS automatically rotates the insets to match orientation.

Landscape insets for iPhone 15 (typical):
- Top: 0, Bottom: 21, Left: 59, Right: 59

---

## Converting Insets to Game Coordinates

Safe area insets are in native pixels. If you're using a virtual resolution, convert them:

```csharp
/// <summary>
/// Convert safe area insets from native pixels to virtual resolution coordinates.
/// </summary>
public static (float Top, float Bottom, float Left, float Right) GetVirtualSafeInsets(
    VirtualResolution virtualRes)
{
    var (top, bottom, left, right) = GetSafeAreaInsets();

    // Scale from native pixels to virtual resolution
    float scale = virtualRes.Scale;
    return (
        Top: top / scale,
        Bottom: bottom / scale,
        Left: left / scale,
        Right: right / scale
    );
}
```

### Safe Rectangle in Virtual Coordinates

```csharp
/// <summary>Get the safe drawing area in virtual resolution coordinates.</summary>
public static Rectangle GetSafeRect(VirtualResolution virtualRes)
{
    var (top, bottom, left, right) = GetVirtualSafeInsets(virtualRes);

    int x = (int)MathF.Ceiling(left);
    int y = (int)MathF.Ceiling(top);
    int w = virtualRes.VirtualWidth - (int)MathF.Ceiling(left + right);
    int h = virtualRes.VirtualHeight - (int)MathF.Ceiling(top + bottom);

    return new Rectangle(x, y, w, h);
}
```

---

## What to Place in the Safe Area

### Critical (Must Be in Safe Area)

- **Interactive buttons** — players must be able to tap them reliably
- **Essential HUD** — health, score, ammo, minimap
- **Text** — any readable text, especially small text
- **Dialog boxes and menus** — modal UI that requires interaction
- **Tutorial prompts** — first-time instructions

### Acceptable Outside Safe Area

- **Full-screen backgrounds** — extend art to screen edges for a premium feel
- **Decorative UI borders** — visual frames that don't carry information
- **Ambient particles** — non-critical visual effects
- **Gameplay world** — the game world itself should fill the full screen; only HUD needs safe area constraints

### Rule of Thumb

> **Game world fills the full screen. HUD and interactive UI stay inside the safe area.**

---

## HUD Anchoring Patterns

Instead of placing HUD at absolute positions, anchor to safe area edges:

```csharp
/// <summary>HUD anchor points relative to safe area.</summary>
public sealed class SafeAreaHud
{
    private Rectangle _safeRect;

    public void Update(VirtualResolution virtualRes)
    {
        _safeRect = GetSafeRect(virtualRes);
    }

    /// <summary>Top-left of safe area (e.g., health bar).</summary>
    public Vector2 TopLeft => new(_safeRect.X + 10, _safeRect.Y + 10);

    /// <summary>Top-right of safe area (e.g., score).</summary>
    public Vector2 TopRight => new(_safeRect.Right - 10, _safeRect.Y + 10);

    /// <summary>Bottom-left of safe area (e.g., inventory).</summary>
    public Vector2 BottomLeft => new(_safeRect.X + 10, _safeRect.Bottom - 10);

    /// <summary>Bottom-right of safe area (e.g., minimap).</summary>
    public Vector2 BottomRight => new(_safeRect.Right - 10, _safeRect.Bottom - 10);

    /// <summary>Top-center of safe area (e.g., level name).</summary>
    public Vector2 TopCenter => new(_safeRect.Center.X, _safeRect.Y + 10);

    /// <summary>Bottom-center of safe area (e.g., action prompt).</summary>
    public Vector2 BottomCenter => new(_safeRect.Center.X, _safeRect.Bottom - 10);
}
```

### Drawing HUD with Safe Area

```csharp
// Game world draws fullscreen (no safe area constraint)
spriteBatch.Begin(transformMatrix: camera.GetViewMatrix());
DrawWorld(spriteBatch);
spriteBatch.End();

// HUD draws within safe area
spriteBatch.Begin(); // No camera transform — screen space
DrawHealthBar(spriteBatch, _hud.TopLeft);
DrawScore(spriteBatch, _hud.TopRight);
DrawMinimap(spriteBatch, _hud.BottomRight);
spriteBatch.End();
```

---

## Aspect Ratio Differences

The same game runs on dramatically different aspect ratios:

| Device Category | Aspect Ratio | Landscape Dimensions (Example) |
|----------------|--------------|-------------------------------|
| iPhone (modern) | ~19.5:9 | Very wide, short (2556x1179) |
| iPad | ~4:3 | Almost square (2388x1668) |
| Desktop (16:9) | 16:9 | Standard widescreen (1920x1080) |
| Desktop (16:10) | 16:10 | Slightly taller (1920x1200) |
| Ultrawide | 21:9 | Very wide (3440x1440) |
| Steam Deck | 16:10 | 1280x800 |

### Handling Variable Aspect Ratios

**Expand strategy** (recommended for cross-platform): Define a minimum visible area and use `Math.Min` for the scale factor, then expand the render target to fill the display. The game always fills the entire screen — no black bars on any device. See [G19](./G19_display_resolution_viewports.md) for the full implementation.

**Concrete example** with a 560x315 pixel art base:

| Display | Aspect | Virtual Resolution | Scale | Extra |
|---------|--------|--------------------|-------|-------|
| Desktop 1280x720 | 16:9 | 560x315 | ~2.29x | Exact match |
| iPad 2388x1668 | ~1.43:1 | 560x391 | ~4.27x | +76px height |
| iPhone 2556x1179 | ~2.17:1 | 683x315 | ~3.74x | +123px width |
| Ultrawide 3440x1440 | 21:9 | 753x315 | ~4.57x | +193px width |

```
iPhone (19.5:9):  ████████████████████████████
                  ←────── 683 virtual px ──────→
                  Minimum 560 guaranteed + extra width

iPad (4:3):       ████████████████
                  ████████████████
                  ████████████████
                  ↕ 391 virtual px (minimum 315 + extra height)

Desktop (16:9):   ████████████████████
                  ████████████████████
                  560x315 — base resolution
```

`GetSafeRect()` automatically adapts — it reads the dynamic `VirtualWidth`/`VirtualHeight` each frame, so HUD anchoring stays correct as the virtual resolution expands.

**Letterbox strategy**: Same visible area everywhere. Black bars where the display doesn't match. Simpler but wastes screen space.

### What This Means for Game Design

- **Don't place critical gameplay elements at screen edges** — on some aspect ratios they'll be cut off
- **Center-weighted design** — most important action happens in the center 70% of the screen
- **Test on both extremes** — iPhone (widest) and iPad (tallest) represent the full range
- **HUD anchors to safe rect, not virtual edges** — safe rect adjusts for both notch insets and expand mode dimensions
- **Menus should reflow** — a main menu designed for 16:9 may need scrolling on 4:3

---

## Gum UI and Safe Areas

If using Gum.MonoGame for UI, configure layout containers to respect safe area margins:

```csharp
// When setting up Gum layout, apply safe area as margin on the root container
var (top, bottom, left, right) = GetVirtualSafeInsets(virtualRes);

rootContainer.Margin = new Margin(
    left: left,
    top: top,
    right: right,
    bottom: bottom
);
```

Gum's anchor system handles responsive layout within the constrained container — elements anchored to edges will respect the safe area automatically.

---

## Testing Different Devices on Desktop

During development, simulate different device aspect ratios by resizing the desktop window:

```csharp
/// <summary>Simulate a device's aspect ratio for testing safe areas.</summary>
public void SimulateDevice(string device)
{
    (int w, int h, float safeTop, float safeBottom, float safeLeft, float safeRight) = device switch
    {
        "iphone15" => (2556, 1179, 0, 63, 177, 177),  // Landscape, native pixels
        "iphone_se" => (1334, 750, 0, 0, 0, 0),
        "ipad" => (2360, 1640, 0, 0, 0, 0),
        "ultrawide" => (3440, 1440, 0, 0, 0, 0),
        "steam_deck" => (1280, 800, 0, 0, 0, 0),
        _ => (1920, 1080, 0, 0, 0, 0),
    };

    // Scale down to fit desktop window
    int scale = Math.Max(1, Math.Min(w / 640, h / 360));
    _graphics.PreferredBackBufferWidth = w / scale;
    _graphics.PreferredBackBufferHeight = h / scale;
    _graphics.ApplyChanges();

    _virtualResolution.Recalculate();

    // Optionally draw safe area overlay (debug visualization)
    _debugSafeInsets = (safeTop / scale, safeBottom / scale, safeLeft / scale, safeRight / scale);
}
```

### Debug Safe Area Overlay

Draw a semi-transparent overlay showing the unsafe regions:

```csharp
/// <summary>Draw debug overlay showing unsafe screen regions.</summary>
public void DrawSafeAreaDebug(SpriteBatch spriteBatch, Texture2D pixel,
    int screenWidth, int screenHeight, float top, float bottom, float left, float right)
{
    Color unsafeColor = new(255, 0, 0, 60); // Semi-transparent red

    // Top unsafe region
    if (top > 0)
        spriteBatch.Draw(pixel, new Rectangle(0, 0, screenWidth, (int)top), unsafeColor);

    // Bottom unsafe region
    if (bottom > 0)
        spriteBatch.Draw(pixel, new Rectangle(0, screenHeight - (int)bottom,
            screenWidth, (int)bottom), unsafeColor);

    // Left unsafe region
    if (left > 0)
        spriteBatch.Draw(pixel, new Rectangle(0, 0, (int)left, screenHeight), unsafeColor);

    // Right unsafe region
    if (right > 0)
        spriteBatch.Draw(pixel, new Rectangle(screenWidth - (int)right, 0,
            (int)right, screenHeight), unsafeColor);
}
```

---

## Common Pitfalls

**Hardcoding UI positions:** `new Vector2(10, 10)` for a health bar works on desktop but overlaps the notch on iPhone. Always use safe area offsets.

**Forgetting landscape inset rotation:** In landscape, the notch is on the left or right side, not the top. iOS rotates insets automatically, but verify your conversion code handles both orientations.

**Not testing on iPad:** iPad's 4:3 aspect ratio is radically different from iPhone's 19.5:9. UI that fits on iPhone may overflow or look wrong on iPad.

**Ignoring the home indicator area:** The bottom 34pt (in portrait) or 21pt (in landscape) is reserved for the home gesture. Placing buttons there causes accidental swipe-to-home.

**`SafeAreaInsets` is zero before window is visible:** On iOS, query safe area insets after the window is fully loaded (in `Initialize()` or later), not in the constructor.

---

## See Also

- [G19 Display, Resolution & Viewports](./G19_display_resolution_viewports.md) — virtual resolution setup
- [G21 Coordinate Systems & Transforms](./G21_coordinate_systems.md) — coordinate conversion for insets
- [G24 Window & Display Management](./G24_window_display_management.md) — window config, iOS fullscreen
- [G5 UI Framework](./G5_ui_framework.md) — Gum layout system
