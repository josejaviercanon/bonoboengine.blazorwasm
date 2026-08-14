# G57 — Weather & Environmental Effects



> **Category:** Guide · **Related:** [G23 Particles](./G23_particles.md) · [G27 Shaders & Visual Effects](./G27_shaders_and_effects.md) · [G10 Custom Game Systems](./G10_custom_game_systems.md) · [G39 2D Lighting](./G39_2d_lighting.md) · [G22 Parallax & Depth Layers](./G22_parallax_depth_layers.md)

Weather transforms a flat 2D world into a living place. Rain hammering rooftops, fog creeping through a swamp, lightning splitting the sky — these aren't cosmetic. They shape mood, guide the player's attention, and create gameplay opportunities. This guide builds a complete weather system on MonoGame + Arch ECS, from architecture through every major weather type, with full code.

---

## Table of Contents

1. [Weather System Architecture](#1-weather-system-architecture)
2. [Rain](#2-rain)
3. [Snow](#3-snow)
4. [Wind](#4-wind)
5. [Fog](#5-fog)
6. [Lightning & Thunder](#6-lightning--thunder)
7. [Sandstorm / Dust](#7-sandstorm--dust)
8. [Environmental Ambiance](#8-environmental-ambiance)
9. [Water Effects](#9-water-effects)
10. [Weather Impact on Gameplay](#10-weather-impact-on-gameplay)
11. [ECS Integration](#11-ecs-integration)
12. [Practical Example — Full Weather Cycle](#12-practical-example--full-weather-cycle)

---

## 1. Weather System Architecture

Weather is global state that many systems read but few write. Model it as a singleton resource that the `WeatherManager` owns and other systems query.

### Weather States

```csharp
public enum WeatherType
{
    Clear,
    Cloudy,
    Rain,
    HeavyRain,
    Snow,
    Blizzard,
    Fog,
    Storm,      // HeavyRain + Lightning
    Sandstorm
}
```

### WeatherState Resource

```csharp
public record struct WeatherState(
    WeatherType Current,
    WeatherType Previous,
    float TransitionProgress,   // 0 = fully Previous, 1 = fully Current
    float Intensity,            // 0..1 overall weather intensity
    float WindDirection,        // radians
    float WindForce,            // 0..1
    float FogDensity,           // 0..1
    float TimeInCurrentWeather, // seconds
    Color AmbientTint           // global color overlay
);
```

### WeatherManager

The manager drives transitions, blending smoothly between states. It lives outside ECS as a service that writes the `WeatherState` resource each frame.

```csharp
public class WeatherManager
{
    private WeatherState _state;
    private WeatherType _targetWeather;
    private float _transitionDuration = 5f; // seconds
    private float _transitionTimer;
    private readonly record struct WeatherPreset(
        float WindForce, float WindDirection, float FogDensity,
        float Intensity, Color AmbientTint);

    private static readonly Dictionary<WeatherType, WeatherPreset> Presets = new()
    {
        [WeatherType.Clear]     = new(0.0f, 0f, 0.0f, 0.0f, Color.White),
        [WeatherType.Cloudy]    = new(0.1f, 0f, 0.05f, 0.2f, new Color(200, 200, 210)),
        [WeatherType.Rain]      = new(0.3f, 0.2f, 0.1f, 0.5f, new Color(170, 175, 190)),
        [WeatherType.HeavyRain] = new(0.5f, 0.3f, 0.2f, 0.8f, new Color(140, 145, 160)),
        [WeatherType.Snow]      = new(0.15f, -0.1f, 0.15f, 0.5f, new Color(220, 225, 240)),
        [WeatherType.Blizzard]  = new(0.8f, 0.5f, 0.5f, 1.0f, new Color(200, 205, 220)),
        [WeatherType.Fog]       = new(0.05f, 0f, 0.7f, 0.6f, new Color(190, 195, 200)),
        [WeatherType.Storm]     = new(0.7f, 0.4f, 0.25f, 1.0f, new Color(100, 105, 120)),
        [WeatherType.Sandstorm] = new(0.9f, 0.6f, 0.6f, 1.0f, new Color(210, 180, 120)),
    };

    public ref WeatherState State => ref _state;

    public void TransitionTo(WeatherType target, float duration = 5f)
    {
        if (target == _state.Current) return;
        _state.Previous = _state.Current;
        _state.Current = target;
        _targetWeather = target;
        _transitionDuration = duration;
        _transitionTimer = 0f;
        _state.TransitionProgress = 0f;
    }

    public void Update(float dt)
    {
        _state.TimeInCurrentWeather += dt;

        if (_state.TransitionProgress < 1f)
        {
            _transitionTimer += dt;
            _state.TransitionProgress = MathHelper.Clamp(
                _transitionTimer / _transitionDuration, 0f, 1f);

            // Smooth step for natural blending
            float t = _state.TransitionProgress;
            t = t * t * (3f - 2f * t);

            var from = Presets[_state.Previous];
            var to = Presets[_state.Current];

            _state.Intensity = MathHelper.Lerp(from.Intensity, to.Intensity, t);
            _state.WindForce = MathHelper.Lerp(from.WindForce, to.WindForce, t);
            _state.WindDirection = MathHelper.Lerp(from.WindDirection, to.WindDirection, t);
            _state.FogDensity = MathHelper.Lerp(from.FogDensity, to.FogDensity, t);
            _state.AmbientTint = Color.Lerp(from.AmbientTint, to.AmbientTint, t);
        }
    }
}
```

### Time-Based vs Event-Driven

Two approaches, often combined:

- **Time-based:** A `WeatherSchedule` defines weather changes at specific in-game times. The `WeatherManager` checks the game clock (see [G10](./G10_custom_game_systems.md) for day/night cycles) and transitions automatically.
- **Event-driven:** Game events trigger weather — entering a volcano biome starts heat haze, a boss spawning triggers a storm. Call `TransitionTo()` from any system.

```csharp
// Time-based schedule entry
public readonly record struct ScheduledWeather(
    float GameHour,       // 0-24
    WeatherType Weather,
    float TransitionTime  // seconds to blend in
);
```

---

## 2. Rain

Rain is the bread and butter of weather effects. Two rendering strategies: **world-space particles** that interact with terrain, and **screen-space overlays** for cheap wide coverage.

### Raindrop Particle

```csharp
public record struct Raindrop(
    Vector2 Position,
    Vector2 Velocity,
    float Length,       // visual trail length in pixels
    float Life,         // remaining lifetime
    float Alpha
);
```

### Particle Pool

Thousands of raindrops need a struct pool — no allocations per frame.

```csharp
public class RainSystem
{
    private readonly Raindrop[] _drops;
    private int _activeCount;
    private readonly int _maxDrops;
    private readonly Random _rng = new();

    // Screen dimensions for spawning
    private readonly int _screenWidth;
    private readonly int _screenHeight;

    public RainSystem(int maxDrops, int screenWidth, int screenHeight)
    {
        _maxDrops = maxDrops;
        _drops = new Raindrop[maxDrops];
        _screenWidth = screenWidth;
        _screenHeight = screenHeight;
    }

    public void Update(float dt, ref WeatherState weather)
    {
        bool isRaining = weather.Current is WeatherType.Rain
            or WeatherType.HeavyRain or WeatherType.Storm;
        float targetCount = isRaining ? weather.Intensity * _maxDrops : 0;

        // Spawn new drops
        while (_activeCount < (int)targetCount && _activeCount < _maxDrops)
        {
            float angle = MathHelper.ToRadians(-80f + weather.WindDirection * 30f);
            float speed = 600f + (float)_rng.NextDouble() * 400f;

            _drops[_activeCount] = new Raindrop(
                Position: new Vector2(
                    _rng.Next(-100, _screenWidth + 100),
                    -_rng.Next(0, 50)),
                Velocity: new Vector2(
                    MathF.Sin(angle) * speed,
                    MathF.Cos(angle) * speed),
                Length: 8f + (float)_rng.NextDouble() * 16f,
                Life: 2f,
                Alpha: 0.3f + (float)_rng.NextDouble() * 0.4f
            );
            _activeCount++;
        }

        // Update active drops
        for (int i = 0; i < _activeCount; i++)
        {
            ref var drop = ref _drops[i];
            drop = drop with
            {
                Position = drop.Position + drop.Velocity * dt,
                Life = drop.Life - dt
            };

            // Off-screen or dead — swap-remove
            if (drop.Life <= 0 || drop.Position.Y > _screenHeight + 20)
            {
                _drops[i] = _drops[--_activeCount];
                i--;
            }
        }
    }

    public void Draw(SpriteBatch batch, Texture2D pixel)
    {
        for (int i = 0; i < _activeCount; i++)
        {
            ref var drop = ref _drops[i];
            float angle = MathF.Atan2(drop.Velocity.X, drop.Velocity.Y);
            batch.Draw(pixel, drop.Position, null,
                Color.LightSteelBlue * drop.Alpha,
                angle, Vector2.Zero,
                new Vector2(1f, drop.Length),
                SpriteEffects.None, 0f);
        }
    }
}
```

### Splash Particles

When a raindrop hits the ground (Y exceeds ground level), spawn a short-lived splash:

```csharp
public record struct Splash(
    Vector2 Position,
    float Timer,      // counts down from ~0.15s
    int Frame         // animation frame index
);
```

Use a small spritesheet (4-5 frames) of concentric circles. Swap-remove from a pool just like raindrops.

### Puddle Sprites

Puddles appear during prolonged rain. Track accumulated rain time and spawn puddle entities at predefined ground positions when thresholds are hit. Puddles use a sine-wave alpha wobble to simulate surface ripples. They evaporate (fade out) after rain stops.

### Screen-Space Rain Overlay

For cheap "background rain," render a scrolling semi-transparent texture over the entire screen after all world rendering. Tile a rain texture and scroll it at the rain angle. Layer two passes at different speeds for depth parallax.

### Sound Layering

Layer three audio tracks, crossfaded by intensity:
- **Light patter** (0.0–0.3 intensity)
- **Steady rain** (0.3–0.7)
- **Downpour** (0.7–1.0)

Crossfade with `SoundEffectInstance.Volume` driven by weather intensity.

---

## 3. Snow

Snow is gentler than rain. Snowflakes drift, swirl, and accumulate.

### Snowflake Particle

```csharp
public record struct Snowflake(
    Vector2 Position,
    float FallSpeed,
    float DriftPhase,     // sine wave offset
    float DriftAmplitude, // horizontal sway range
    float Size,           // 1-4 pixels
    float Rotation,
    float RotationSpeed,
    float Life
);
```

### Drift Motion

The key to natural snow is sine-wave horizontal drift:

```csharp
public void UpdateSnow(float dt, ref WeatherState weather, float gameTime)
{
    for (int i = 0; i < _activeSnow; i++)
    {
        ref var flake = ref _snowflakes[i];
        float drift = MathF.Sin(gameTime * 1.5f + flake.DriftPhase)
                    * flake.DriftAmplitude;

        flake = flake with
        {
            Position = flake.Position + new Vector2(
                drift + weather.WindForce * 80f * dt,
                flake.FallSpeed * dt),
            Rotation = flake.Rotation + flake.RotationSpeed * dt,
            Life = flake.Life - dt
        };

        if (flake.Position.Y > _groundLevel || flake.Life <= 0)
        {
            _snowflakes[i] = _snowflakes[--_activeSnow];
            i--;
        }
    }
}
```

### Accumulation

Track snow depth per tile column as a float. When snowflakes "land," increment the column's depth. Render a white overlay rectangle on top of ground tiles, height proportional to depth. This gradually buries the landscape.

```csharp
// Per-column snow depth
private readonly float[] _snowDepth; // indexed by tile column

// When a snowflake lands:
int col = (int)(flake.Position.X / TileSize);
if (col >= 0 && col < _snowDepth.Length)
    _snowDepth[col] = MathHelper.Clamp(_snowDepth[col] + 0.02f, 0, MaxSnowDepth);
```

### Blizzard Variant

A blizzard is snow with high wind force, dense particle count, and reduced visibility (combine with fog). Set `WindForce > 0.7` and `Intensity > 0.8`. Add a white screen overlay at 20-40% opacity.

### Snow Depth Affecting Movement

Expose snow depth to the movement system:

```csharp
float depth = GetSnowDepthAt(playerPosition.X);
float speedMultiplier = MathHelper.Lerp(1f, 0.4f, depth / MaxSnowDepth);
```

---

## 4. Wind

Wind is a shared force that multiple systems consume: particles, vegetation, projectiles, and player movement.

### Wind State

Wind lives in the `WeatherState` resource (`WindDirection`, `WindForce`). Add gust support:

```csharp
public class WindSystem
{
    private float _gustTimer;
    private float _gustIntensity;
    private readonly Random _rng = new();

    public void Update(float dt, ref WeatherState weather)
    {
        _gustTimer -= dt;
        if (_gustTimer <= 0)
        {
            _gustTimer = 3f + (float)_rng.NextDouble() * 8f;
            _gustIntensity = (float)_rng.NextDouble() * 0.4f;
        }

        // Gust decays
        _gustIntensity = MathHelper.Lerp(_gustIntensity, 0f, dt * 2f);

        // Effective wind = base + gust
        float effectiveWind = weather.WindForce + _gustIntensity;
        // Other systems read weather.WindForce; write back the combined value
        weather = weather with { WindForce = effectiveWind };
    }
}
```

### Leaf / Debris Particles

Spawn small leaf or dust particles that ride the wind. They tumble with rotation, decelerate when wind drops, and settle to the ground. Reuse the same struct pool pattern from rain.

### Tree & Grass Sway Shader (HLSL)

Apply this to vegetation sprites. It offsets vertices horizontally based on wind:

```hlsl
// VegetationSway.fx
sampler2D TextureSampler : register(s0);

float Time;
float WindForce;     // 0..1
float WindDirection; // -1..1 horizontal bias
float SwayAmount;    // pixels of max sway

float4 MainPS(float2 texCoord : TEXCOORD0) : COLOR0
{
    // Top of sprite sways more than bottom
    float swayFactor = 1.0 - texCoord.y; // 0 at bottom, 1 at top
    swayFactor = swayFactor * swayFactor; // quadratic falloff

    float sway = sin(Time * 2.0 + texCoord.x * 3.0) * SwayAmount
               * WindForce * swayFactor;
    sway += WindDirection * WindForce * SwayAmount * 0.5 * swayFactor;

    float2 displaced = texCoord + float2(sway / 256.0, 0); // assuming 256px wide
    return tex2D(TextureSampler, displaced);
}

technique Sway
{
    pass P0
    {
        PixelShader = compile ps_3_0 MainPS();
    }
}
```

---

## 5. Fog

Fog is a screen-space post-process. It's cheap, atmospheric, and versatile.

### Fog Overlay Shader (HLSL)

```hlsl
// Fog.fx
sampler2D SceneSampler : register(s0);

float FogDensity;    // 0..1
float4 FogColor;     // RGBA

float4 MainPS(float2 texCoord : TEXCOORD0) : COLOR0
{
    float4 scene = tex2D(SceneSampler, texCoord);

    // Perlin-style noise for non-uniform fog (use a noise texture)
    // For simplicity, uniform blend:
    float fogAmount = FogDensity;

    return lerp(scene, FogColor, fogAmount);
}

technique FogOverlay
{
    pass P0
    {
        PixelShader = compile ps_3_0 MainPS();
    }
}
```

### Distance-Based Fog

For a 2D game, "distance" is distance from the camera center. Objects further from screen center fade more into fog:

```csharp
public void DrawWithDistanceFog(SpriteBatch batch, Vector2 cameraCenter,
    Vector2 objectPos, Texture2D tex, float fogDensity, Color fogColor)
{
    float dist = Vector2.Distance(cameraCenter, objectPos);
    float maxDist = 600f;
    float fogAmount = MathHelper.Clamp(dist / maxDist * fogDensity, 0f, 0.85f);
    Color tinted = Color.Lerp(Color.White, fogColor, fogAmount);
    batch.Draw(tex, objectPos, tinted);
}
```

### Fog Color Palette

| Fog Type | Color | Use Case |
|----------|-------|----------|
| Morning mist | `(230, 235, 245, 180)` | Dawn transitions |
| Swamp fog | `(120, 160, 100, 160)` | Poison areas, marshes |
| Hellscape | `(180, 60, 40, 140)` | Volcanic / demonic zones |
| Underwater | `(60, 120, 160, 120)` | Submerged sections |
| Silent Hill | `(200, 200, 200, 220)` | Horror, near-total whiteout |

---

## 6. Lightning & Thunder

Lightning is a punctuation mark — rare, dramatic, impossible to ignore.

### Lightning Controller

```csharp
public class LightningSystem
{
    private float _nextStrikeTimer;
    private float _flashTimer;
    private float _flashIntensity;
    private float _thunderDelay;
    private bool _thunderPending;
    private readonly Random _rng = new();

    // Config
    public float MinInterval = 4f;
    public float MaxInterval = 15f;
    public float FlashDuration = 0.12f;

    public float FlashIntensity => _flashIntensity;

    public void Update(float dt, ref WeatherState weather)
    {
        bool stormy = weather.Current is WeatherType.Storm;
        if (!stormy)
        {
            _flashIntensity = 0;
            return;
        }

        // Flash decay
        if (_flashTimer > 0)
        {
            _flashTimer -= dt;
            // Two-pulse flash: bright → dim → bright → fade
            float t = 1f - (_flashTimer / FlashDuration);
            _flashIntensity = t < 0.3f ? 1f
                            : t < 0.5f ? 0.3f
                            : t < 0.7f ? 0.8f
                            : MathHelper.Lerp(0.8f, 0f, (t - 0.7f) / 0.3f);
        }
        else
        {
            _flashIntensity = 0;
        }

        // Thunder (delayed sound)
        if (_thunderPending)
        {
            _thunderDelay -= dt;
            if (_thunderDelay <= 0)
            {
                // Play thunder sound at volume based on "distance"
                PlayThunder(0.6f + (float)_rng.NextDouble() * 0.4f);
                _thunderPending = false;
            }
        }

        // Next strike countdown
        _nextStrikeTimer -= dt;
        if (_nextStrikeTimer <= 0)
        {
            _nextStrikeTimer = MinInterval
                + (float)_rng.NextDouble() * (MaxInterval - MinInterval);
            _flashTimer = FlashDuration;

            // Thunder follows 0.5–2s later (simulating distance)
            _thunderDelay = 0.5f + (float)_rng.NextDouble() * 1.5f;
            _thunderPending = true;
        }
    }

    private void PlayThunder(float volume)
    {
        // Play from your audio system; pick randomly from 3-4 thunder variants
    }
}
```

### Screen Flash Rendering

After drawing the scene, draw a full-screen white quad with alpha = `FlashIntensity`:

```csharp
if (_lightning.FlashIntensity > 0.01f)
{
    batch.Draw(_pixel, new Rectangle(0, 0, screenWidth, screenHeight),
        Color.White * _lightning.FlashIntensity);
}
```

### Silhouette Effect

During a lightning flash, render all foreground entities as solid black by temporarily overriding their color to `Color.Black`. Background layers stay lit. This creates a dramatic silhouette.

```csharp
Color entityColor = _lightning.FlashIntensity > 0.5f
    ? Color.Black
    : Color.White;
```

---

## 7. Sandstorm / Dust

Sandstorms are aggressive — dense particles, low visibility, potential gameplay damage.

### Sandstorm Particles

Reuse the rain particle pool, but horizontal:

```csharp
public void SpawnSandParticle(ref WeatherState weather)
{
    float angle = weather.WindDirection + MathHelper.ToRadians(-10 + _rng.Next(20));
    float speed = 300f + (float)_rng.NextDouble() * 200f;

    _particles[_activeCount++] = new WeatherParticle(
        Position: new Vector2(-20, _rng.Next(0, _screenHeight)),
        Velocity: new Vector2(
            MathF.Cos(angle) * speed,
            MathF.Sin(angle) * speed + _rng.Next(-30, 30)),
        Size: 1f + (float)_rng.NextDouble() * 3f,
        Life: 3f,
        Alpha: 0.4f + (float)_rng.NextDouble() * 0.3f,
        Color: new Color(210, 185, 130)
    );
}
```

### Visibility Reduction

Apply a sand-colored fog overlay that increases with intensity:

```csharp
Color sandOverlay = new Color(210, 180, 120) * (weather.Intensity * 0.6f);
batch.Draw(_pixel, _fullScreenRect, sandOverlay);
```

### Gameplay Damage

In a sandstorm, exposed entities take gradual damage:

```csharp
// In DamageSystem.Update():
if (weather.Current == WeatherType.Sandstorm && !entity.Has<Sheltered>())
{
    ref var health = ref entity.Get<Health>();
    health = health with
    {
        Current = health.Current - SandstormDPS * dt
    };
}
```

Mark entities under roofs or indoors with a `Sheltered` tag component.

---

## 8. Environmental Ambiance

These small particle effects run independently of the main weather system and bring specific areas alive.

### Ambient Particle Definitions

```csharp
public enum AmbientType { DustMotes, Fireflies, Pollen, FallingLeaves, Bubbles }

public record struct AmbientZone(
    Rectangle Area,
    AmbientType Type,
    int MaxParticles,
    float SpawnRate  // particles per second
);

public record struct AmbientParticle(
    Vector2 Position,
    Vector2 Velocity,
    float Life,
    float Alpha,
    float Size,
    Color Tint,
    float Phase   // for sine oscillation
);
```

### Behavior per Type

| Type | Motion | Visual |
|------|--------|--------|
| Dust motes | Slow random drift, gently rising | Tiny white dots, low alpha |
| Fireflies | Random walk + sine bob, occasional blink | Yellow-green glow, pulse alpha |
| Pollen | Wind-carried float, very slow fall | Small yellow circles |
| Falling leaves | Sine-wave horizontal + steady fall + rotation | Leaf sprites, varied color |
| Bubbles | Rise with slight horizontal wobble | Circle outlines, pop at surface |

### Firefly Glow

Fireflies blink. Use a sine wave on alpha with a sharp threshold:

```csharp
float glow = MathF.Sin(gameTime * 3f + particle.Phase);
particle.Alpha = glow > 0.7f ? 0.9f : MathHelper.Max(0.05f, glow * 0.15f);
```

For extra atmosphere, render each firefly with additive blending as a small glow sprite.

---

## 9. Water Effects

### Rain Ripples

When it's raining, spawn ripple circles on water surface tiles:

```csharp
public record struct Ripple(
    Vector2 Position,
    float Timer,     // 0 → MaxTime
    float MaxTime,
    float MaxRadius
);

public void DrawRipple(SpriteBatch batch, Texture2D circle, in Ripple ripple)
{
    float t = ripple.Timer / ripple.MaxTime;
    float radius = ripple.MaxRadius * t;
    float alpha = 1f - t; // fade out as it expands
    float scale = radius / (circle.Width * 0.5f);

    batch.Draw(circle,
        ripple.Position, null,
        Color.White * alpha * 0.4f,
        0f, new Vector2(circle.Width / 2f, circle.Height / 2f),
        scale, SpriteEffects.None, 0f);
}
```

### Flowing Water Texture Scrolling

Scroll UV coordinates over time for rivers and streams:

```csharp
_waterUVOffset += new Vector2(0.3f, 0.1f) * dt; // direction of flow
// Pass to shader or adjust source rectangle:
var sourceRect = new Rectangle(
    (int)(_waterUVOffset.X * tileSize) % tileSize,
    (int)(_waterUVOffset.Y * tileSize) % tileSize,
    tileSize, tileSize);
```

### Reflection Shimmer

Render a wavy distortion below reflective surfaces using a simple sine displacement shader:

```hlsl
// WaterShimmer.fx
sampler2D TextureSampler : register(s0);
float Time;
float WaveAmplitude; // ~0.003

float4 MainPS(float2 texCoord : TEXCOORD0) : COLOR0
{
    float2 uv = texCoord;
    uv.x += sin(uv.y * 40.0 + Time * 3.0) * WaveAmplitude;
    uv.y += cos(uv.x * 30.0 + Time * 2.5) * WaveAmplitude * 0.5;
    float4 color = tex2D(TextureSampler, uv);
    color.a *= 0.5; // semi-transparent reflection
    return color;
}

technique Shimmer
{
    pass P0
    {
        PixelShader = compile ps_3_0 MainPS();
    }
}
```

---

## 10. Weather Impact on Gameplay

Weather shouldn't just be visual. Wire it into gameplay for meaningful consequences.

### Slippery Surfaces

```csharp
// In MovementSystem:
public void ApplyWeatherModifiers(ref Velocity vel, in WeatherState weather)
{
    bool wet = weather.Current is WeatherType.Rain or WeatherType.HeavyRain
        or WeatherType.Storm;
    bool icy = weather.Current is WeatherType.Snow or WeatherType.Blizzard;

    if (wet)
    {
        // Reduce friction → character slides more
        vel = vel with { Friction = vel.Friction * 0.6f };
    }
    if (icy)
    {
        vel = vel with { Friction = vel.Friction * 0.3f };
    }
}
```

### Visibility Radius

In fog or sandstorms, limit the player's sight radius. This can mean clamping the lighting reveal radius (see [G39](./G39_2d_lighting.md)) or darkening distant tiles:

```csharp
float visibilityMultiplier = 1f - weather.FogDensity * 0.7f;
float effectiveRadius = BaseVisibility * visibilityMultiplier;
```

### Wind Affecting Projectiles

```csharp
// In ProjectileSystem:
ref var pos = ref entity.Get<Position>();
ref var projVel = ref entity.Get<Velocity>();

projVel = projVel with
{
    X = projVel.X + weather.WindForce * MathF.Cos(weather.WindDirection) * 60f * dt,
    Y = projVel.Y + weather.WindForce * MathF.Sin(weather.WindDirection) * 30f * dt
};
```

### Fire Extinguished by Rain

```csharp
// In FireSystem:
if (weather.Current is WeatherType.Rain or WeatherType.HeavyRain or WeatherType.Storm)
{
    ref var fire = ref entity.Get<FireState>();
    fire = fire with
    {
        Fuel = fire.Fuel - weather.Intensity * ExtinguishRate * dt
    };
    if (fire.Fuel <= 0)
        world.Destroy(entity);
}
```

### Design Hooks Summary

| Weather | Gameplay Effect | Design Use |
|---------|----------------|------------|
| Rain | Slippery, fire out | Force melee, limit ranged fire |
| Snow | Slow movement, tracks | Stealth visibility, survival |
| Fog | Short sight range | Horror, ambush encounters |
| Storm | All rain + lightning damage | Boss fight atmosphere |
| Sandstorm | DOT, low visibility | Timed shelter-seeking |
| Wind | Projectile drift | Archery skill challenges |

---

## 11. ECS Integration

### Components and Resources

```csharp
// Global weather resource — query via World
public record struct WeatherState(
    WeatherType Current,
    WeatherType Previous,
    float TransitionProgress,
    float Intensity,
    float WindDirection,
    float WindForce,
    float FogDensity,
    float TimeInCurrentWeather,
    Color AmbientTint
);

// Tag: entity is sheltered from weather
public record struct Sheltered;

// Tag: entity is affected by wind
public record struct WindAffected(float Drag); // 0..1, how much wind moves it

// Vegetation sway
public record struct SwayAnimation(
    float SwayAmount,
    float Phase
);
```

### WeatherParticleSystem

Runs each frame, manages all weather particle pools (rain, snow, sand, ambient):

```csharp
public class WeatherParticleSystem : ISystem
{
    private readonly RainSystem _rain;
    private readonly SnowSystem _snow;
    private readonly SandSystem _sand;

    public void Update(float dt, World world)
    {
        ref var weather = ref world.Get<WeatherState>();

        _rain.Update(dt, ref weather);
        _snow.Update(dt, ref weather);
        _sand.Update(dt, ref weather);
    }
}
```

### WeatherRenderSystem

Draws all weather visuals in the correct order — particles, overlays, flashes:

```csharp
public class WeatherRenderSystem : ISystem
{
    private readonly RainSystem _rain;
    private readonly SnowSystem _snow;
    private readonly LightningSystem _lightning;
    private readonly SpriteBatch _batch;
    private readonly Effect _fogShader;
    private readonly Texture2D _pixel;

    public void Draw(World world, RenderTarget2D sceneTarget)
    {
        ref var weather = ref world.Get<WeatherState>();

        // 1. World-space particles (behind UI, after scene)
        _batch.Begin(blendState: BlendState.AlphaBlend);
        _rain.Draw(_batch, _pixel);
        _snow.Draw(_batch, _pixel);
        _batch.End();

        // 2. Screen-space overlays
        // Fog
        if (weather.FogDensity > 0.01f)
        {
            _fogShader.Parameters["FogDensity"].SetValue(weather.FogDensity);
            _fogShader.Parameters["FogColor"].SetValue(
                weather.AmbientTint.ToVector4());
            _batch.Begin(effect: _fogShader);
            _batch.Draw(sceneTarget, Vector2.Zero, Color.White);
            _batch.End();
        }

        // Ambient tint
        _batch.Begin(blendState: BlendState.AlphaBlend);
        Color tint = weather.AmbientTint;
        tint.A = (byte)(weather.Intensity * 60);
        _batch.Draw(_pixel, new Rectangle(0, 0,
            sceneTarget.Width, sceneTarget.Height), tint);

        // Lightning flash
        if (_lightning.FlashIntensity > 0.01f)
        {
            _batch.Draw(_pixel, new Rectangle(0, 0,
                sceneTarget.Width, sceneTarget.Height),
                Color.White * _lightning.FlashIntensity);
        }
        _batch.End();
    }
}
```

### Weather Affecting Other Systems

Systems that need weather data simply query the resource:

```csharp
ref var weather = ref world.Get<WeatherState>();
// Use weather.WindForce, weather.FogDensity, weather.Current, etc.
```

This is the beauty of the singleton resource pattern — no coupling, no events, no observers. Any system reads the current weather whenever it needs to.

---

## 12. Practical Example — Full Weather Cycle

A complete day-long weather cycle: **clear morning → afternoon clouds → evening rain → night storm → dawn fog → clear**.

### Weather Schedule

```csharp
public class WeatherCycleController
{
    private readonly WeatherManager _weatherManager;
    private readonly LightningSystem _lightning;
    private readonly WindSystem _wind;

    private readonly ScheduledWeather[] _schedule = new[]
    {
        new ScheduledWeather(GameHour: 6.0f,  Weather: WeatherType.Clear,     TransitionTime: 8f),
        new ScheduledWeather(GameHour: 13.0f, Weather: WeatherType.Cloudy,    TransitionTime: 10f),
        new ScheduledWeather(GameHour: 17.0f, Weather: WeatherType.Rain,      TransitionTime: 6f),
        new ScheduledWeather(GameHour: 20.0f, Weather: WeatherType.Storm,     TransitionTime: 4f),
        new ScheduledWeather(GameHour: 4.0f,  Weather: WeatherType.Fog,       TransitionTime: 10f),
        // Wraps back to 6:00 Clear
    };

    private int _currentIndex = 0;

    public WeatherCycleController(WeatherManager manager, LightningSystem lightning,
        WindSystem wind)
    {
        _weatherManager = manager;
        _lightning = lightning;
        _wind = wind;
    }

    /// <summary>
    /// Called each frame with the current in-game hour (0-24 float).
    /// See G10 for day/night time tracking.
    /// </summary>
    public void Update(float dt, float gameHour)
    {
        _weatherManager.Update(dt);
        _lightning.Update(dt, ref _weatherManager.State);
        _wind.Update(dt, ref _weatherManager.State);

        // Check if we've reached the next scheduled weather
        int nextIndex = (_currentIndex + 1) % _schedule.Length;
        var next = _schedule[nextIndex];

        if (HasReachedHour(gameHour, next.GameHour))
        {
            _weatherManager.TransitionTo(next.Weather, next.TransitionTime);
            _currentIndex = nextIndex;
        }
    }

    private bool HasReachedHour(float current, float target)
    {
        // Handle day wrap (e.g., current=23.9, target=4.0)
        float diff = target - current;
        return diff >= 0 && diff < 0.02f; // within ~1 game-minute
    }
}
```

### Wiring It All Together

```csharp
public class WeatherGame : Game
{
    private SpriteBatch _batch;
    private Texture2D _pixel;
    private RenderTarget2D _sceneTarget;

    private World _world;
    private WeatherManager _weatherManager;
    private WeatherCycleController _cycleController;
    private RainSystem _rainSystem;
    private LightningSystem _lightningSystem;
    private WindSystem _windSystem;

    private float _gameHour = 6f;           // start at dawn
    private const float GameMinutesPerRealSecond = 2f; // 1 real second = 2 game minutes

    protected override void Initialize()
    {
        _world = World.Create();
        _weatherManager = new WeatherManager();
        _lightningSystem = new LightningSystem();
        _windSystem = new WindSystem();
        _cycleController = new WeatherCycleController(
            _weatherManager, _lightningSystem, _windSystem);

        var vp = GraphicsDevice.Viewport;
        _rainSystem = new RainSystem(
            maxDrops: 4000, screenWidth: vp.Width, screenHeight: vp.Height);

        // Register weather state as a world resource
        _world.Set(new WeatherState(
            Current: WeatherType.Clear,
            Previous: WeatherType.Clear,
            TransitionProgress: 1f,
            Intensity: 0f,
            WindDirection: 0f,
            WindForce: 0f,
            FogDensity: 0f,
            TimeInCurrentWeather: 0f,
            AmbientTint: Color.White
        ));

        base.Initialize();
    }

    protected override void LoadContent()
    {
        _batch = new SpriteBatch(GraphicsDevice);
        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });

        var vp = GraphicsDevice.Viewport;
        _sceneTarget = new RenderTarget2D(
            GraphicsDevice, vp.Width, vp.Height);
    }

    protected override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

        // Advance game clock
        _gameHour += GameMinutesPerRealSecond * dt / 60f;
        if (_gameHour >= 24f) _gameHour -= 24f;

        // Drive the weather cycle
        _cycleController.Update(dt, _gameHour);

        // Sync the world resource with the manager's state
        _world.Set(_weatherManager.State);

        // Update weather particles
        ref var weather = ref _world.Get<WeatherState>();
        _rainSystem.Update(dt, ref weather);

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        ref var weather = ref _world.Get<WeatherState>();

        // 1. Draw scene to render target
        GraphicsDevice.SetRenderTarget(_sceneTarget);
        GraphicsDevice.Clear(Color.CornflowerBlue);
        _batch.Begin();
        // ... draw your world here ...
        _batch.End();
        GraphicsDevice.SetRenderTarget(null);

        // 2. Draw scene
        _batch.Begin();
        _batch.Draw(_sceneTarget, Vector2.Zero, Color.White);
        _batch.End();

        // 3. Weather particles (world-space)
        _batch.Begin(blendState: BlendState.AlphaBlend);
        _rainSystem.Draw(_batch, _pixel);
        _batch.End();

        // 4. Ambient tint overlay
        _batch.Begin(blendState: BlendState.AlphaBlend);
        Color tint = weather.AmbientTint;
        tint.A = (byte)(weather.Intensity * 50);
        _batch.Draw(_pixel, GraphicsDevice.Viewport.Bounds, tint);

        // 5. Lightning flash
        if (_lightningSystem.FlashIntensity > 0.01f)
        {
            _batch.Draw(_pixel, GraphicsDevice.Viewport.Bounds,
                Color.White * _lightningSystem.FlashIntensity);
        }
        _batch.End();

        base.Draw(gameTime);
    }
}
```

### What's Happening Each Hour

| Game Hour | Weather | Visual | Sound |
|-----------|---------|--------|-------|
| 06:00 | Clear | Bright tint, no particles | Birds, morning ambiance |
| 13:00 | Cloudy | Slight grey tint, low fog | Wind picking up |
| 17:00 | Rain | Raindrop particles, puddles | Rain patter → steady |
| 20:00 | Storm | Heavy rain + lightning flashes | Downpour + thunder |
| 04:00 | Fog | White overlay, reduced visibility | Muted, eerie quiet |
| 06:00 | Clear | Cycle restarts | Dawn chorus |

---

## Performance Notes

- **Struct pools** for all particles — zero GC pressure during gameplay.
- **Swap-remove** for particle death — O(1) removal, no shifting.
- **Screen-space overlays** for fog, tint, and flash — single draw call each.
- **Shader-based** vegetation sway — GPU handles thousands of sprites.
- **Budget:** 4000 raindrops at ~0.3ms on modest hardware. Snow can use fewer (1000–2000) since flakes are larger and slower.
- **LOD particles:** If the camera zooms out, reduce particle count proportionally. If zoomed in, particles are bigger and you need fewer.

---

## Checklist

- [ ] `WeatherManager` with state transitions and blending
- [ ] Rain particles with splash, puddles, sound layers
- [ ] Snow with drift, accumulation, blizzard variant
- [ ] Wind system with gusts, affecting particles and gameplay
- [ ] Fog shader with color variants
- [ ] Lightning with multi-pulse flash and delayed thunder
- [ ] Sandstorm particles with DOT damage
- [ ] Ambient particles (fireflies, dust, leaves, bubbles)
- [ ] Water ripples and shimmer shader
- [ ] Gameplay hooks (friction, visibility, projectile drift, fire)
- [ ] ECS resource pattern for cross-system weather access
- [ ] Full weather cycle controller with scheduled transitions

---

*Weather is a promise to the player: this world doesn't just exist for you — it exists on its own, and you're living in it.*
