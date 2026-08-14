# G19 — Display, Resolution & Viewports


> **Category:** Guide · **Related:** [G2 Rendering & Graphics](./G2_rendering_and_graphics.md) · [G15 Game Loop](./G15_game_loop.md) · [G20 Camera Systems](./G20_camera_systems.md) · [G24 Window & Display Management](./G24_window_display_management.md)

---

## The Core Problem

Your game is designed at one resolution. Players run it on screens ranging from 640x480 windows to 2778x1284 iPhone Pro Max displays to 3440x1440 ultrawides. Something has to give. The question is: **what do you control, and what adapts?**

This guide covers how to pick a design resolution, scale it to any screen, and handle the differences between desktop and mobile.

---

## Design Resolution vs Display Resolution

**Design resolution** (also called virtual resolution or logical resolution) is the coordinate space your game logic uses. Every sprite position, camera bound, and UI anchor is expressed in design-resolution units.

**Display resolution** is the actual pixel count of the window or screen. On desktop this is `Window.ClientBounds`. On iOS this is the native backbuffer size (which is the physical resolution on Retina — e.g., 2556x1179 for iPhone 15 Pro).

The virtual resolution system renders the game at the design resolution using a `RenderTarget2D`, then scales that to fill the display.

---

## Scaling Strategies

### Strategy 1: Viewport Scaling (Pixel Art)

Render at a small design resolution (e.g., 320x180, 384x216, 480x270), then scale up with `SamplerState.PointClamp` for crisp pixels.

**Best for:** Pixel art games, retro aesthetics.

| Pros | Cons |
|------|------|
| Pixel-perfect rendering guaranteed | Limited viewable area |
| Small render targets = fast | Integer scaling wastes screen space on odd resolutions |
| Consistent look across all devices | UI text can be chunky |

**Integer scaling:** For maximum crispness, scale only by whole numbers (2x, 3x, 4x). This prevents pixel shimmer but may leave black borders if the display isn't an exact multiple.

### Strategy 2: Letterbox / Pillarbox (Fixed Aspect Ratio)

Render at a higher design resolution (e.g., 1920x1080) and maintain the aspect ratio. Black bars appear on screens with different aspect ratios.

**Best for:** Narrative games, cinematic games, games where consistent framing matters.

| Pros | Cons |
|------|------|
| Exact artistic control over framing | Black bars on 4:3 iPads or 21:9 ultrawides |
| Simple implementation | Wasted screen space |
| One layout to design for | |

### Strategy 3: Expand (Show More World)

Keep a minimum visible area but expand the viewport on wider/taller screens. Players on ultrawides see more of the world.

**Best for:** Open-world, exploration, multiplayer (fairness concerns aside).

| Pros | Cons |
|------|------|
| No black bars ever | Must design for variable framing |
| Uses full screen real estate | Competitive advantage for wider screens |
| Feels premium on ultrawide | More complex UI anchoring |

### Strategy 4: Canvas Scaling (UI-First)

Render at the display's native resolution but scale all coordinates from a logical space. Like CSS pixels vs device pixels. Common for UI-heavy games.

**Best for:** Card games, menu-heavy games, visual novels.

---

## Decision Table

| Game Type | Recommended Strategy | Base Resolution |
|-----------|---------------------|-----------------|
| Pixel art platformer | Expand (PointClamp) | 480x270 or 560x315 min |
| Pixel art RPG | Expand (PointClamp) | 480x270 or 560x315 min |
| Pixel art cross-platform (desktop + iOS) | Expand (PointClamp) | 560x315 min — no black bars, fills every screen |
| HD 2D (hand-drawn, spine) | Letterbox or expand | 1920x1080 |
| Mobile-first action | Expand | 1920x1080 base, expand height |
| UI-heavy (cards, menus) | Canvas scaling | Match display |
| Desktop only, cinematic | Letterbox | 1920x1080 or 2560x1440 |

> **Expand + PointClamp for pixel art** works great. The minimum visible area stays crisp at integer-like scale ratios, and extra world content shown on wider/taller displays fills the screen naturally. No black bars on any device.

---

## MonoGame Implementation

### Virtual Resolution Class — Expand Mode (~80 lines)

This is the core recipe. Create once in `src/Rendering/`, use everywhere. The expand approach defines a **minimum visible area** and expands whichever axis the display is wider/taller, so the game always fills the entire screen with no black bars.

> **Why expand over letterbox?** Cross-platform games face a massive aspect ratio range: iPhone ~19.5:9, desktop 16:9, iPad ~4:3. Expand mode fills every screen — iPad players see more vertical space, iPhone players see more horizontal space. No wasted screen real estate. Letterbox is just `Math.Min` with a fixed render target if you need it — trivial to implement without a mode flag.

```csharp
namespace MyGame.Rendering;

/// <summary>
/// Renders the game at a dynamic virtual resolution using expand mode.
/// Defines a minimum visible area (base resolution) and expands whichever axis
/// the display is wider/taller, so the game always fills the entire screen with
/// no black bars. Uses PointClamp for crisp pixel art.
/// </summary>
public sealed class VirtualResolution : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly int _minWidth;
    private readonly int _minHeight;
    private RenderTarget2D _renderTarget;
    private Rectangle _destinationRect;
    private float _scale;

    /// <summary>Current virtual width (may exceed minWidth on wider displays).</summary>
    public int VirtualWidth { get; private set; }

    /// <summary>Current virtual height (may exceed minHeight on taller displays).</summary>
    public int VirtualHeight { get; private set; }

    /// <summary>Where on screen the game renders (always full screen in expand mode).</summary>
    public Rectangle DestinationRect => _destinationRect;

    /// <summary>Current scale factor from virtual to display pixels.</summary>
    public float Scale => _scale;

    /// <param name="device">Graphics device.</param>
    /// <param name="minWidth">Minimum guaranteed visible width (base resolution).</param>
    /// <param name="minHeight">Minimum guaranteed visible height (base resolution).</param>
    public VirtualResolution(GraphicsDevice device, int minWidth, int minHeight)
    {
        _device = device;
        _minWidth = minWidth;
        _minHeight = minHeight;
        VirtualWidth = minWidth;
        VirtualHeight = minHeight;
        _renderTarget = new RenderTarget2D(device, minWidth, minHeight);
        Recalculate();
    }

    /// <summary>Call when window resizes or orientation changes.</summary>
    public void Recalculate()
    {
        int displayWidth = _device.PresentationParameters.BackBufferWidth;
        int displayHeight = _device.PresentationParameters.BackBufferHeight;

        // Scale so the minimum area always fits (Math.Min, NOT Math.Max)
        _scale = Math.Min((float)displayWidth / _minWidth, (float)displayHeight / _minHeight);

        // Expand virtual resolution to fill the entire display
        int newWidth = (int)MathF.Ceiling(displayWidth / _scale);
        int newHeight = (int)MathF.Ceiling(displayHeight / _scale);

        // Only recreate render target when dimensions actually change
        if (newWidth != VirtualWidth || newHeight != VirtualHeight)
        {
            VirtualWidth = newWidth;
            VirtualHeight = newHeight;
            _renderTarget?.Dispose();
            _renderTarget = new RenderTarget2D(_device, VirtualWidth, VirtualHeight);
        }

        // Full screen — no bars
        _destinationRect = new Rectangle(0, 0, displayWidth, displayHeight);
    }

    /// <summary>Set render target before drawing game content.</summary>
    public void BeginDraw()
    {
        _device.SetRenderTarget(_renderTarget);
        _device.Clear(Color.Black);
    }

    /// <summary>Draw the virtual resolution buffer scaled to the screen.</summary>
    public void EndDraw(SpriteBatch spriteBatch)
    {
        _device.SetRenderTarget(null);
        _device.Clear(Color.Black);

        spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        spriteBatch.Draw(_renderTarget, _destinationRect, Color.White);
        spriteBatch.End();
    }

    /// <summary>Convert a screen-space point (mouse/touch) to virtual coordinates.</summary>
    public Vector2 ScreenToVirtual(Vector2 screenPosition)
    {
        // No offset subtraction — destination always starts at (0,0) in expand mode
        float x = screenPosition.X / _scale;
        float y = screenPosition.Y / _scale;
        return new Vector2(x, y);
    }

    /// <summary>Convert virtual coordinates to screen-space.</summary>
    public Vector2 VirtualToScreen(Vector2 virtualPosition)
    {
        float x = virtualPosition.X * _scale;
        float y = virtualPosition.Y * _scale;
        return new Vector2(x, y);
    }

    public void Dispose()
    {
        _renderTarget?.Dispose();
    }
}
```

**How it works:** `Math.Min` finds the scale at which the minimum area just fits. Dividing the display size by that scale gives a virtual resolution that's *at least* `minWidth x minHeight` but expands in whichever direction the display is proportionally larger. The render target is recreated at this expanded size, then drawn to fill the entire display — no offset, no bars.

**What the old approach got wrong:** Using `Math.Max` for expand mode scales a *fixed* render target beyond the display bounds, producing negative offsets and cropping content. The correct approach uses `Math.Min` for the scale and expands the *render target* to fill the gap.

| Display | Aspect | Virtual Resolution | Scale |
|---------|--------|--------------------|-------|
| Desktop 1280x720 | 16:9 | 560x315 | ~2.29x |
| iPad 2388x1668 | ~1.43:1 | 560x391 | ~4.27x |
| iPhone 2556x1179 | ~2.17:1 | 683x315 | ~3.74x |
| Ultrawide 3440x1440 | 21:9 | 753x315 | ~4.57x |

### Usage in GameApp

```csharp
public class GameApp : Game
{
    private VirtualResolution _virtualRes;

    protected override void Initialize()
    {
        // 560x315 base = minimum visible area. Expands on wider/taller displays.
        _virtualRes = new VirtualResolution(GraphicsDevice, 560, 315);

        // Recalculate on resize — may change VirtualWidth/VirtualHeight and recreate RT
        Window.ClientSizeChanged += (_, _) => _virtualRes.Recalculate();

        base.Initialize();
    }

    protected override void Draw(GameTime gameTime)
    {
        _virtualRes.BeginDraw();       // Set dynamic render target
        _sceneManager.Draw(_spriteBatch);
        _virtualRes.EndDraw(_spriteBatch); // Scale to full display
        base.Draw(gameTime);
    }
}
```

> **Note:** `VirtualWidth` and `VirtualHeight` can change on window resize. Camera systems, HUD layout, and anything using virtual dimensions should read them each frame rather than caching stale values.

### Integer Scaling for Pixel Art

Replace the `Recalculate` scale calculation:

```csharp
// Integer scaling: round down to nearest whole number
_scale = Math.Max(1, (int)Math.Min(scaleX, scaleY));
```

This guarantees every source pixel maps to exactly NxN display pixels — no sub-pixel interpolation, no shimmer.

---

## Pixel Art: SamplerState Matters

When rendering pixel art through the virtual resolution system:

```csharp
// In scene Draw, use PointClamp for all game sprites
spriteBatch.Begin(
    sortMode: SpriteSortMode.Deferred,
    samplerState: SamplerState.PointClamp,  // Nearest-neighbor, no bleeding
    transformMatrix: camera.GetViewMatrix()
);
```

`SamplerState.PointClamp` prevents texture filtering (blur) and edge bleeding from adjacent atlas frames. Use `SamplerState.LinearClamp` only for HD art that benefits from smooth scaling.

---

## Mobile Display Handling

### iOS Device Resolutions

| Device | Points | Native Pixels | Scale | Aspect |
|--------|--------|---------------|-------|--------|
| iPhone SE (3rd) | 375x667 | 750x1334 | @2x | 16:9 |
| iPhone 15 | 390x844 | 1170x2532 | @3x | ~19.5:9 |
| iPhone 15 Pro | 393x852 | 1179x2556 | @3x | ~19.5:9 |
| iPhone 15 Pro Max | 430x932 | 1290x2796 | @3x | ~19.5:9 |
| iPad (10th) | 820x1180 | 1640x2360 | @2x | ~4:3 |
| iPad Pro 11" | 834x1194 | 1668x2388 | @2x | ~4:3 |
| iPad Pro 12.9" | 1024x1366 | 2048x2732 | @2x | ~4:3 |

**Key insight:** iPhone is ~19.5:9, iPad is ~4:3, Desktop is typically 16:9. That's a massive aspect ratio range. The **Expand** strategy handles this gracefully — iPad players see more vertical space, iPhone players see more horizontal space.

### MonoGame on iOS

MonoGame automatically creates the backbuffer at the device's native pixel resolution when `LaunchScreen.storyboard` is present. No manual HiDPI configuration needed — the framework handles Retina scaling internally.

```csharp
// On iOS, these return the full native resolution (e.g., 2556x1179)
int w = GraphicsDevice.PresentationParameters.BackBufferWidth;
int h = GraphicsDevice.PresentationParameters.BackBufferHeight;
```

**Orientation:** Lock to landscape or portrait via `Info.plist`:

```xml
<key>UISupportedInterfaceOrientations</key>
<array>
    <string>UIInterfaceOrientationLandscapeLeft</string>
    <string>UIInterfaceOrientationLandscapeRight</string>
</array>
```

---

## HiDPI / Retina Displays

On macOS with DesktopGL, MonoGame's backbuffer matches the window size in points, not pixels. A 1920x1080 window on a Retina Mac still gets a 1920x1080 backbuffer by default — MonoGame DesktopGL does **not** automatically render at 2x.

This is actually what you want for the virtual resolution approach: set `PreferredBackBufferWidth/Height` to your design resolution, render to it, and the OS handles DPI scaling of the final window.

If you need true HiDPI rendering (for crisp UI text), render at the actual pixel resolution and use the `ScaleMatrix` to map game coordinates.

---

## Fullscreen vs Windowed

```csharp
// In GameApp constructor
_graphics = new GraphicsDeviceManager(this)
{
    PreferredBackBufferWidth = 1920,
    PreferredBackBufferHeight = 1080,
    IsFullScreen = false,
    HardwareModeSwitch = false,  // false = borderless windowed fullscreen
    SynchronizeWithVerticalRetrace = true,
};
```

**Borderless windowed** (`IsFullScreen = true, HardwareModeSwitch = false`) is the modern standard. It's Alt-Tab friendly, avoids resolution switching, and composites correctly with OS overlays. Use exclusive fullscreen (`HardwareModeSwitch = true`) only if you need guaranteed VSync timing or specific refresh rate control.

**See also:** [G24 Window & Display Management](./G24_window_display_management.md) for complete window configuration.

---

## Aspect Ratio Decision Tree

```
Is your game pixel art?
├── Yes → Expand at 480x270 / 560x315 base + PointClamp
│         No black bars. iPad sees more vertical, iPhone sees more horizontal.
│         Anchor HUD to safe areas (see G25).
│
└── No → Are you targeting a single aspect ratio (e.g., cinematic)?
    ├── Yes → Letterbox at 1920x1080 (or 2560x1440)
    │         Clean black bars, consistent framing
    │
    └── No → Cross-platform (desktop + iOS)?
        ├── Yes → Expand at 1920x1080 base (HD) or 560x315 base (pixel art)
        │         iPad shows more vertical, iPhone shows more horizontal
        │         Anchor UI to safe areas, not screen edges
        │
        └── Desktop only?
            └── Expand or letterbox, your preference
```

---

## Common Pitfalls

**Rendering at native resolution without scaling:** On iPhone 15 Pro Max (2796x1290), you'd be rendering 3.6 million pixels. On an old iPad (2048x1536), the GPU may throttle. Virtual resolution lets you render fewer pixels and scale up cheaply.

**Forgetting to convert touch/mouse input:** Raw input coordinates are in display space. You must convert through `ScreenToVirtual()` and then through the camera's `ScreenToWorld()` to get world coordinates. See [G21 Coordinate Systems](./G21_coordinate_systems.md).

**Caching `VirtualWidth`/`VirtualHeight`:** With expand mode, these values change on window resize (desktop) or orientation change (mobile). Read them each frame — don't store them in a field and forget. Camera bounds clamping, HUD layout, and frustum culling all need the current values.

**Not handling `Window.ClientSizeChanged`:** On desktop, players resize windows. Call `Recalculate()` on resize or the game renders at the old scale with black borders in the wrong places.

**Recreating `RenderTarget2D` every frame:** `Recalculate()` should only dispose and recreate the render target when dimensions actually change. Compare `newWidth != VirtualWidth || newHeight != VirtualHeight` before creating — unnecessary RT allocation is expensive and causes GC pressure.

**Using `Math.Max` for expand mode scale:** This is a common mistake. `Math.Max` scales a fixed-size render target beyond the display bounds, producing negative offsets that crop content. The correct approach uses `Math.Min` for the scale factor, then *expands the render target dimensions* to fill the display.

---

## See Also

- [G20 Camera Systems](./G20_camera_systems.md) — camera view matrix interacts with virtual resolution
- [G21 Coordinate Systems & Transforms](./G21_coordinate_systems.md) — full coordinate conversion chain
- [G24 Window & Display Management](./G24_window_display_management.md) — window setup, fullscreen, VSync
- [G25 Safe Areas & Adaptive Layout](./G25_safe_areas_adaptive_layout.md) — iOS notch/Dynamic Island handling
