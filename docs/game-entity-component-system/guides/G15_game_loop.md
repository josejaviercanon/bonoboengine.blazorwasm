# G15 — Game Loop


> **Category:** Guide · **Related:** [G13 C# Performance](./G13_csharp_performance.md) · [G9 Networking](./G9_networking.md) · [G16 Debugging](./G16_debugging.md)

---

## Fixed Timestep with Accumulator

Games must run physics and logic at a fixed timestep (typically 50-60 Hz) regardless of frame rate. Variable timesteps cause non-deterministic physics — the same inputs produce different results across machines.

### MonoGame Integration

In MonoGame, `IsFixedTimeStep = true` makes the framework call `Update()` at a regular interval controlled by `TargetElapsedTime`. The game's `Update()` then uses an accumulator to run logic at a fixed 60Hz rate, independent of the framework's tick rate:

```csharp
/// <summary>Fixed logic timestep: game simulation always runs at 60Hz.</summary>
private const float FixedLogicDt = 1f / 60f;
private float _logicAccumulator;

protected override void Update(GameTime gameTime)
{
    _logicAccumulator += (float)gameTime.ElapsedGameTime.TotalSeconds;

    while (_logicAccumulator >= FixedLogicDt)
    {
        _sceneManager.Update(FixedLogicDt);
        _logicAccumulator -= FixedLogicDt;
    }

    base.Update(gameTime);
}
```

This decouples logic rate from display rate. When `TargetElapsedTime` matches `FixedLogicDt` (both 1/60), each `Update()` call runs exactly one logic tick. When `TargetElapsedTime` is 1/120 (ProMotion), each `Update()` runs zero or one logic tick — but `Draw()` still runs at 120fps for smooth visuals.

**Cap maximum accumulated time** to prevent "spiral of death" where slow frames cause more simulation steps, causing slower frames. In practice, MonoGame's `IsFixedTimeStep` already handles this by capping to `MaxElapsedTime`.

### Interpolation (Optional)

For games that need sub-tick visual smoothness, interpolate between previous and current state using the leftover accumulator fraction (`alpha = _logicAccumulator / FixedLogicDt`). This adds one frame of visual latency but eliminates micro-stutter at high refresh rates. Many 2D games skip interpolation and accept the minor visual quantization.

---

## Culling and Batching

**Frustum culling:** Test object bounds against camera viewport, rejecting objects outside before rendering. Typically eliminates 50-80% of objects.

**Sprite batching:** Accumulate vertex data for all sprites sharing the same render state, flush as a single draw call when state changes.

**Batches break on:** Texture changes, shader changes, blend mode changes, or buffer fullness.

**Strategy:**
- Use texture atlases (2048x2048 safe for all hardware)
- Minimize shader variants
- Sort draw order to minimize state changes

**Overdraw:** Target ~1x overdraw. Sort opaque sprites front-to-back. Trim transparent sprite boundaries tightly. Each fullscreen post-processing effect adds 100% overdraw.

---

## Mobile-Specific Optimization

Thermal throttling is the constraint most developers miss. GPU frequency drops 30-40% under throttling.

**Strategies:**
- Cap to 30fps on heating devices (reduces GPU load ~50%)
- Implement automatic quality scaling (resolution, particle count, post-processing)
- 120Hz increases display power by 31-44% vs 60Hz — lock non-action games to 60Hz
- ASTC compression reduces 2048x2048 RGBA textures from 16MB to ~1.6MB

---

## iOS Game Lifecycle

MonoGame's game loop on iOS is driven by `CADisplayLink`, not a blocking `while` loop. This creates two initialization hazards:

**1. Display link NRE:** MonoGame 3.8.4's `Game()` constructor starts the display link before `_platform` is assigned → `NullReferenceException` in `Game.get_IsActive()`. Fix: defer game creation via `NSRunLoop.Main.InvokeOnMainThread()`.

**2. Non-blocking `Game.Run()`:** On iOS, `Run()` starts the display link and returns immediately (unlike Desktop where it blocks until exit). The game instance must be stored as a class field to prevent GC collection.

```csharp
[Register("AppDelegate")]
internal class AppDelegate : UIApplicationDelegate
{
    private Core.GameApp? _game;  // Field, not local — prevents GC

    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        NSRunLoop.Main.InvokeOnMainThread(() =>
        {
            _game = new Core.GameApp();
            _game.Run();
        });
        return true;
    }
}
```

Full iOS project structure and .csproj: see [R3 Project Structure](../reference/R3_project_structure.md).

---

## Variable Render Rate (ProMotion / High Refresh)

### Why

Logic determinism requires a fixed 60Hz tick rate, but ProMotion displays (iPad Pro, iPhone 13 Pro+) support 120Hz rendering. The accumulator pattern makes this possible: logic always runs at 60Hz, while `TargetElapsedTime` controls the display rate.

### How It Works

| Setting | `TargetElapsedTime` | Update Hz | Logic ticks/frame | Draw FPS |
|---------|---------------------|-----------|--------------------|----------|
| 60Hz (default) | 1/60 | 60 | 1 | 60 |
| 120Hz ProMotion | 1/120 | 120 | 0 or 1 | 120 |
| 30Hz power save | 1/30 | 30 | 2 | 30 |

At 120Hz, half the `Update()` calls produce zero logic ticks (accumulator hasn't reached `FixedLogicDt` yet) but `Draw()` still runs — producing smoother animation from the same game state.

### Platform Callback Pattern

`GameApp` exposes a `PlatformTargetFpsChanged` action that platform launchers hook into:

```csharp
/// <summary>
/// Platform-specific hook called after TargetElapsedTime changes.
/// iOS uses this to configure CADisplayLink frame rate for ProMotion.
/// </summary>
public Action? PlatformTargetFpsChanged { get; set; }
```

### iOS: CADisplayLink Reflection

MonoGame 3.8.4 uses the deprecated `CADisplayLink.FrameInterval` API which caps at 60Hz on ProMotion displays. The fix is reflection to patch MonoGame's own display link with `PreferredFrameRateRange`:

```csharp
private static void SetDisplayLinkFrameRate(Game game, int targetFps)
{
    try
    {
        BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;

        // MonoGame 3.8.4: Game.Platform is an internal field
        FieldInfo? platformField = typeof(Game).GetField("Platform", flags);
        object? platform = platformField?.GetValue(game);
        if (platform == null) return;

        // iOSGamePlatform._displayLink is the CADisplayLink
        FieldInfo? dlField = platform.GetType().GetField("_displayLink", flags);
        if (dlField?.GetValue(platform) is not CADisplayLink displayLink) return;

        displayLink.PreferredFrameRateRange = new CAFrameRateRange
        {
            Minimum = 30,
            Maximum = targetFps,
            Preferred = targetFps
        };
    }
    catch
    {
        // Reflection failed — MonoGame internals may have changed.
    }
}
```

**Required setup:**
- **Info.plist:** Add `<key>CADisableMinimumFrameDurationOnPhone</key><true/>` (required for iPhone ProMotion; iPad works without it)
- **iOS .csproj:** Add `<TrimmerRootAssembly Include="MonoGame.Framework" />` to preserve MonoGame fields from the IL trimmer
- **`CAFrameRateRange`** in .NET iOS has no 3-argument constructor — use object initializer syntax
- **MonoGame 3.8.4 field names:** `Game.Platform` (not `Game._platform`), `iOSGamePlatform._displayLink`

### Desktop: Sleep-Based Limiting

On desktop, `SynchronizeWithVerticalRetrace = false` with `IsFixedTimeStep = true` uses sleep-based frame limiting. MonoGame's game loop sleeps until the next target tick. No platform-specific hooks needed — `TargetElapsedTime` alone controls the rate.

---

## Final Principles

1. **Measure before optimizing** — Profile on target hardware in release builds
2. **Focus on p99 frame times** — Not averages (one 50ms spike per second ruins the feel)
3. **Simplest correct solution** — Often performs adequately
4. **High-impact optimizations first** — Object pooling → [G1](./G1_custom_code_recipes.md), frustum culling, spatial partitioning → [G14](./G14_data_structures.md), fixing algorithmic complexity
5. **Ship games** — Architecture serves the game, not vice versa
