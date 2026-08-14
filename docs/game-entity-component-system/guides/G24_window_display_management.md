# G24 — Window & Display Management


> **Category:** Guide · **Related:** [G19 Display & Resolution](./G19_display_resolution_viewports.md) · [G15 Game Loop](./G15_game_loop.md) · [G25 Safe Areas](./G25_safe_areas_adaptive_layout.md) · [R3 Project Structure](../reference/R3_project_structure.md)

---

## GraphicsDeviceManager Setup

All window and display configuration starts in the `GameApp` constructor via `GraphicsDeviceManager`. These settings must be set before `Initialize()` runs.

```csharp
public class GameApp : Game
{
    private readonly GraphicsDeviceManager _graphics;

    public GameApp()
    {
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1920,
            PreferredBackBufferHeight = 1080,
            IsFullScreen = false,
            HardwareModeSwitch = false,
            SynchronizeWithVerticalRetrace = true,
            PreferMultiSampling = false,
            GraphicsProfile = GraphicsProfile.HiDef,  // or Reach for max compatibility
        };

        IsFixedTimeStep = true;
        TargetElapsedTime = TimeSpan.FromSeconds(1.0 / 60.0);
        Window.AllowUserResizing = true;

        Content.RootDirectory = "Content";
    }
}
```

---

## Window Size vs Backbuffer Size

These are different concepts:

- **Window size** (`Window.ClientBounds`): The size of the OS window in screen points. On macOS Retina, a 1920x1080 window occupies 1920x1080 points but the actual pixel count may differ.
- **Backbuffer size** (`PreferredBackBufferWidth/Height`): The resolution MonoGame renders at. This is what `GraphicsDevice.PresentationParameters.BackBufferWidth/Height` returns after initialization.

On **DesktopGL**, the backbuffer matches the preferred size you set. The OS scales the window to fit your display.

On **iOS**, the backbuffer automatically matches the device's native pixel resolution (when `LaunchScreen.storyboard` is present). `PreferredBackBufferWidth/Height` is ignored — MonoGame overrides it.

---

## Fullscreen Modes

### Borderless Windowed Fullscreen (Recommended)

```csharp
_graphics.IsFullScreen = true;
_graphics.HardwareModeSwitch = false;  // This makes it borderless
_graphics.ApplyChanges();
```

- Window fills the entire screen without resolution change
- Alt-Tab/Cmd-Tab is instant (no mode switch delay)
- OS overlays, notifications, and screenshot tools work normally
- Composites correctly with other windows
- Slight input latency increase vs exclusive (1-2ms, imperceptible for most games)

### Exclusive Fullscreen

```csharp
_graphics.IsFullScreen = true;
_graphics.HardwareModeSwitch = true;
_graphics.ApplyChanges();
```

- Takes over the GPU output entirely
- Guaranteed VSync timing, lowest input latency
- Alt-Tab causes a visible mode switch (screen flashes)
- Can change display resolution to match backbuffer
- Use only for competitive games or VSync-critical applications

### Windowed

```csharp
_graphics.IsFullScreen = false;
_graphics.ApplyChanges();
```

- Standard resizable (or fixed-size) window
- Best for development and debugging

### Toggle at Runtime

```csharp
/// <summary>Toggle between borderless fullscreen and windowed.</summary>
public void ToggleFullscreen()
{
    if (_graphics.IsFullScreen)
    {
        _graphics.IsFullScreen = false;
        _graphics.PreferredBackBufferWidth = 1920;
        _graphics.PreferredBackBufferHeight = 1080;
    }
    else
    {
        // Borderless fullscreen at current display resolution
        _graphics.PreferredBackBufferWidth = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode.Width;
        _graphics.PreferredBackBufferHeight = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode.Height;
        _graphics.IsFullScreen = true;
        _graphics.HardwareModeSwitch = false;
    }
    _graphics.ApplyChanges();

    // Recalculate virtual resolution scaling
    _virtualResolution?.Recalculate();
}
```

---

## Window Resize Handling

When the player resizes the window (desktop only):

```csharp
protected override void Initialize()
{
    Window.AllowUserResizing = true;
    Window.ClientSizeChanged += OnWindowResize;

    base.Initialize();
}

private void OnWindowResize(object sender, EventArgs e)
{
    // Update backbuffer to match new window size
    _graphics.PreferredBackBufferWidth = Window.ClientBounds.Width;
    _graphics.PreferredBackBufferHeight = Window.ClientBounds.Height;
    _graphics.ApplyChanges();

    // Recalculate virtual resolution scaling
    _virtualResolution?.Recalculate();
}
```

**Gotcha:** `ClientSizeChanged` fires during drag-resize on some platforms, potentially dozens of times per second. If `Recalculate()` is expensive (recreates render targets), debounce it or defer to next `Update()`.

---

## VSync and Frame Timing

### VSync On (Default)

```csharp
_graphics.SynchronizeWithVerticalRetrace = true;
IsFixedTimeStep = true;
TargetElapsedTime = TimeSpan.FromSeconds(1.0 / 60.0);
```

MonoGame syncs `Present()` to the monitor's refresh rate. On a 60Hz display, this gives exactly 60fps with zero tearing. On a 144Hz display, you get 144fps (but `TargetElapsedTime` still controls how often `Update()` runs).

### VSync Off

```csharp
_graphics.SynchronizeWithVerticalRetrace = false;
IsFixedTimeStep = true;
TargetElapsedTime = TimeSpan.FromSeconds(1.0 / 60.0);
```

MonoGame calls `Update()`/`Draw()` as fast as possible, up to `TargetElapsedTime` rate. Without VSync, screen tearing may occur. This mode is useful for benchmarking or when you implement your own frame limiter.

### Relationship to Game Loop

See [G15 Game Loop](./G15_game_loop.md) for the fixed-timestep accumulator pattern. The key interaction:

| Setting | Effect |
|---------|--------|
| `IsFixedTimeStep = true` | MonoGame calls Update/Draw at `TargetElapsedTime` rate |
| `IsFixedTimeStep = false` | MonoGame calls Update/Draw every frame (variable dt) |
| `SynchronizeWithVerticalRetrace = true` | Present() waits for VBlank (caps to display Hz) |
| Both true | Update/Draw at target rate, present synced to display |

For the recommended fixed-timestep accumulator:
- Desktop: `IsFixedTimeStep = true`, `TargetElapsedTime = 1/60`, VSync on
- iOS 60Hz: Same as desktop — MonoGame uses CADisplayLink at 60Hz
- iOS 120Hz ProMotion: `TargetElapsedTime = 1/120`, logic accumulator still ticks at 60Hz

---

## Graphics Profiles

MonoGame supports two graphics profiles:

| Profile | Texture Size | Shader Model | Non-Power-of-2 Textures | NPOT Wrap |
|---------|-------------|--------------|--------------------------|-----------|
| **Reach** | 2048x2048 | SM 2.0 | Yes (no wrap/mip) | No |
| **HiDef** | 4096x4096 | SM 3.0+ | Yes (full support) | Yes |

**Use `Reach`** for maximum compatibility (older mobile devices, integrated GPUs).
**Use `HiDef`** if you need larger textures, wrap-mode on NPOT textures, or advanced shader features. All modern iOS devices and desktop GPUs support HiDef.

```csharp
_graphics.GraphicsProfile = GraphicsProfile.HiDef;
```

---

## Multi-Monitor (Desktop)

Enumerate available displays:

```csharp
foreach (GraphicsAdapter adapter in GraphicsAdapter.Adapters)
{
    DisplayMode mode = adapter.CurrentDisplayMode;
    // mode.Width, mode.Height, mode.Format
}
```

Move the window to a specific monitor by setting `Window.Position`:

```csharp
// Center window on primary monitor
DisplayMode display = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
Window.Position = new Point(
    (display.Width - Window.ClientBounds.Width) / 2,
    (display.Height - Window.ClientBounds.Height) / 2);
```

**Note:** MonoGame DesktopGL (SDL2) handles multi-monitor DPI scaling inconsistently across platforms. Test on actual hardware if multi-monitor support is important.

---

## iOS-Specific Display

iOS apps are always fullscreen. There is no windowed mode, no resize, no multi-monitor.

### What MonoGame Handles Automatically
- Backbuffer at native pixel resolution (when LaunchScreen.storyboard is present)
- Orientation changes (if multiple orientations are allowed in Info.plist)
- Retina/non-Retina scaling

### What You Handle
- **Orientation locking** via Info.plist (see [G19](./G19_display_resolution_viewports.md))
- **Safe area insets** for notch/Dynamic Island (see [G25](./G25_safe_areas_adaptive_layout.md))
- **ProMotion 120Hz** via reflection on CADisplayLink (see game loop docs)
- **`UIRequiresFullScreen = true`** in Info.plist to prevent Slide Over/Split View on iPad

```xml
<!-- Info.plist — prevent iPad split screen -->
<key>UIRequiresFullScreen</key>
<true/>
```

### ProMotion Display Rate

MonoGame 3.8.4 uses the deprecated `CADisplayLink.FrameInterval` which caps at 60Hz. To enable 120Hz on ProMotion devices:

1. Set `TargetElapsedTime` to 1/120 in `GameApp`
2. Hook `GameApp.PlatformTargetFpsChanged` in the iOS AppDelegate
3. Use reflection to set `PreferredFrameRateRange` on MonoGame's internal `CADisplayLink`
4. Add `CADisableMinimumFrameDurationOnPhone` to Info.plist for iPhone

See [G15 Game Loop](./G15_game_loop.md) for the full pattern.

---

## Desktop Window Configuration Recipes

### Development Window (Small, Resizable)

```csharp
_graphics.PreferredBackBufferWidth = 1280;
_graphics.PreferredBackBufferHeight = 720;
_graphics.IsFullScreen = false;
Window.AllowUserResizing = true;
Window.Title = "MyGame (Dev)";
```

### Release Window (Borderless Fullscreen Default)

```csharp
DisplayMode display = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
_graphics.PreferredBackBufferWidth = display.Width;
_graphics.PreferredBackBufferHeight = display.Height;
_graphics.IsFullScreen = true;
_graphics.HardwareModeSwitch = false;
```

### Pixel Art (Fixed Aspect, Integer Scale)

```csharp
_graphics.PreferredBackBufferWidth = 1280;  // 320 * 4
_graphics.PreferredBackBufferHeight = 720;  // 180 * 4
_graphics.IsFullScreen = false;
Window.AllowUserResizing = false;  // Fixed size for pixel-perfect
```

---

## Common Pitfalls

**Calling `ApplyChanges()` in a loop:** `ApplyChanges()` recreates the graphics device, which invalidates all render targets and may cause a visible flash. Call it only when settings actually change.

**Forgetting `LaunchScreen.storyboard` on iOS:** Without it, iOS runs your app in a legacy compatibility mode with a scaled-down resolution. The backbuffer will be smaller than the actual screen, everything looks blurry, and touch coordinates are offset.

**Setting backbuffer size on iOS:** MonoGame ignores `PreferredBackBufferWidth/Height` on iOS — the backbuffer always matches the device. Don't rely on these values being what you set.

**`Window.ClientBounds` is zero during construction:** Window dimensions aren't available until after `Initialize()`. Don't create resolution-dependent resources in the constructor.

---

## See Also

- [G19 Display, Resolution & Viewports](./G19_display_resolution_viewports.md) — virtual resolution, scaling strategies
- [G15 Game Loop](./G15_game_loop.md) — frame timing, fixed timestep, ProMotion
- [G25 Safe Areas & Adaptive Layout](./G25_safe_areas_adaptive_layout.md) — iOS safe areas
- [R3 Project Structure](../reference/R3_project_structure.md) — platform-specific project setup
