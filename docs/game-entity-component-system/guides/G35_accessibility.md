# G35 — Accessibility


> **Category:** Guide · **Related:** [G5 UI Framework](./G5_ui_framework.md) · [G7 Input Handling](./G7_input_handling.md) · [G27 Shaders & Visual Effects](./G27_shaders_and_effects.md) · [G6 Audio](./G6_audio.md)

> **Stack:** MonoGame · Arch ECS · Gum UI

Making your game accessible isn't charity — it's good design. Around 15–20% of players have some form of disability. This guide covers practical patterns for implementing accessibility in a MonoGame 2D game.

---

## Visual Accessibility

### Colorblind Modes

About 8% of men and 0.5% of women have some form of color vision deficiency. The three common types:

| Type | Affected Colors | Prevalence | Design Impact |
|------|----------------|------------|---------------|
| **Protanopia** | Red–green (no red cones) | ~1% of males | Red appears dark/black |
| **Deuteranopia** | Red–green (no green cones) | ~5% of males | Green/red indistinguishable |
| **Tritanopia** | Blue–yellow (no blue cones) | ~0.01% | Blue/yellow confused |

#### Shader-Based Colorblind Simulation Filter

Apply a post-processing shader that shifts your palette into distinguishable ranges:

```hlsl
// ColorblindFilter.fx
float4x4 ColorMatrix;

float4 MainPS(float2 texCoord : TEXCOORD0) : COLOR0
{
    float4 color = tex2D(TextureSampler, texCoord);
    float3 corrected = mul(float4(color.rgb, 1.0), ColorMatrix).rgb;
    return float4(corrected, color.a);
}
```

```csharp
// Daltonization matrices (LMS color space correction)
public static class ColorblindMatrices
{
    // Protanopia simulation → correction
    public static readonly Matrix Protanopia = new Matrix(
        0.567f, 0.433f, 0.000f, 0f,
        0.558f, 0.442f, 0.000f, 0f,
        0.000f, 0.242f, 0.758f, 0f,
        0f,     0f,     0f,     1f
    );

    // Deuteranopia
    public static readonly Matrix Deuteranopia = new Matrix(
        0.625f, 0.375f, 0.000f, 0f,
        0.700f, 0.300f, 0.000f, 0f,
        0.000f, 0.300f, 0.700f, 0f,
        0f,     0f,     0f,     1f
    );

    // Tritanopia
    public static readonly Matrix Tritanopia = new Matrix(
        0.950f, 0.050f, 0.000f, 0f,
        0.000f, 0.433f, 0.567f, 0f,
        0.000f, 0.475f, 0.525f, 0f,
        0f,     0f,     0f,     1f
    );
}
```

#### Beyond Color: Redundant Indicators

Never use color alone to convey information. Always pair with at least one other channel:

- **Shape** — Gems use distinct silhouettes (circle, diamond, star), not just red/blue/green
- **Icons** — Status effects show an icon alongside the colored border
- **Pattern** — Hatching, stripes, or dots on colored zones in a puzzle game
- **Text labels** — "FIRE +3" not just a red number

### High Contrast Mode

Provide a toggle that:

- Increases outline thickness on interactive elements (2px → 4px)
- Adds dark backdrops behind UI text
- Reduces background visual noise (particle density, parallax layers)
- Boosts foreground/background luminance ratio to ≥ 7:1 (WCAG AAA)

### Text Scaling

```csharp
public class TextScaleSettings
{
    public float UIScale { get; set; } = 1.0f; // 1.0, 1.25, 1.5, 2.0
    
    public float ScaledSize(float baseSize) => baseSize * UIScale;
}
```

With Gum UI, bind your text elements to a scale factor and ensure containers reflow. Test at 200% — if your UI breaks, your layout isn't flexible enough.

---

## Motor Accessibility

### Remappable Controls

Every input binding should be data-driven, not hardcoded:

```csharp
public class InputProfile
{
    public Dictionary<GameAction, Keys> KeyBindings { get; set; }
    public Dictionary<GameAction, Buttons> GamepadBindings { get; set; }
    
    public InputProfile()
    {
        // Defaults
        KeyBindings = new Dictionary<GameAction, Keys>
        {
            { GameAction.Jump, Keys.Space },
            { GameAction.Attack, Keys.Z },
            { GameAction.Dash, Keys.X },
            { GameAction.Interact, Keys.C },
        };
    }
    
    public bool IsActionPressed(GameAction action)
    {
        if (KeyBindings.TryGetValue(action, out var key))
            return Keyboard.GetState().IsKeyDown(key);
        return false;
    }
}
```

Store profiles in JSON so players can share configs. Allow multiple keys bound to the same action.

### One-Handed Modes

- Allow all actions to be mapped to one side of the keyboard (left-hand: WASD + QEF, right-hand: IJKL + UOP)
- Support mouse-only play where feasible (click-to-move, radial menus)
- Gamepad: allow shoulder-button combos to replace face buttons

### Hold vs Toggle

Any action that requires sustained input should offer a toggle alternative:

| Action | Hold (Default) | Toggle Option |
|--------|---------------|---------------|
| Run | Hold Shift | Press Shift to toggle run |
| Aim | Hold RMB | Click RMB to enter/exit aim |
| Crouch | Hold Ctrl | Press Ctrl to toggle crouch |
| Block | Hold button | Press to toggle block stance |

### Adjustable Timing Windows

For mechanics with timing (parry, dodge, rhythm):

```csharp
public class TimingSettings
{
    // Multiplier for input windows. 1.0 = default, 2.0 = double the window
    public float InputWindowMultiplier { get; set; } = 1.0f;
    
    public int AdjustedFrames(int baseFrames) 
        => (int)(baseFrames * InputWindowMultiplier);
}
```

### Auto-Aim / Aim Assist

For games with projectile or targeting mechanics:

- **Snap targeting** — Nearest enemy auto-selected when aiming
- **Aim magnetism** — Cursor/reticle pulled toward valid targets within a cone
- **Lock-on** — Toggle to lock aim to a specific enemy

---

## Cognitive Accessibility

### Difficulty Options

Don't gate accessibility behind a single "Easy/Normal/Hard" slider. Offer granular controls:

| Setting | Options | Effect |
|---------|---------|--------|
| Game speed | 50%, 75%, 100% | Slows all gameplay |
| Enemy health | 50%, 75%, 100%, 150% | Scales HP |
| Player damage taken | 0%, 50%, 100% | Reduces incoming damage |
| Lives / retries | Limited, Unlimited | Removes fail state |
| Timer pressure | On, Extended, Off | Relaxes time limits |
| Puzzle hints | Off, Subtle, Explicit | Progressive hint system |

### Clear UI Principles

- **Consistent iconography** — Same icon always means the same thing
- **Objective markers** — Optional waypoints/arrows showing where to go
- **Tutorial recall** — Let players re-read any tutorial from the pause menu
- **Journal/log** — Track current objectives and recent story beats
- **Minimal HUD clutter** — Show only what's needed; let players toggle elements

### Save Anywhere

Autosave frequently. Let players manually save at any point. Losing 30 minutes of progress because someone had to stop playing isn't difficulty — it's hostility.

---

## Audio Accessibility

### Visual Alternatives for Audio Cues

Every gameplay-relevant sound needs a visual counterpart:

| Audio Cue | Visual Alternative |
|-----------|--------------------|
| Enemy footsteps approaching | Directional indicator on screen edge |
| Low health warning beep | Screen vignette pulse (red) |
| Item pickup chime | Floating icon + text popup |
| Off-screen projectile | Arrow indicator pointing to threat |
| Rhythm game beats | Visual pulse on note highway |

### Subtitle System

```csharp
public class SubtitleSettings
{
    public bool Enabled { get; set; } = true;
    public bool SpeakerNames { get; set; } = true;     // "[Guard] Stop right there!"
    public bool SoundDescriptions { get; set; } = false; // "[door creaks]"
    public float TextSize { get; set; } = 1.0f;
    public bool Background { get; set; } = true;        // Dark backdrop for readability
    public Color SpeakerColor { get; set; } = Color.Yellow;
}
```

**Speaker identification:** Use consistent colors per character. Place speaker name in brackets. For directional audio, add an indicator: `[Guard — left]`.

---

## Implementation: Accessibility Settings Manager

Centralize all accessibility state in one serializable object:

```csharp
public class AccessibilitySettings
{
    // Visual
    public ColorblindMode ColorblindMode { get; set; } = ColorblindMode.None;
    public bool HighContrast { get; set; } = false;
    public float UIScale { get; set; } = 1.0f;
    
    // Motor
    public float InputWindowMultiplier { get; set; } = 1.0f;
    public bool ToggleRun { get; set; } = false;
    public bool ToggleAim { get; set; } = false;
    public AimAssistLevel AimAssist { get; set; } = AimAssistLevel.Off;
    
    // Cognitive
    public float GameSpeed { get; set; } = 1.0f;
    public float DamageMultiplier { get; set; } = 1.0f;
    public bool InfiniteLives { get; set; } = false;
    
    // Audio
    public bool Subtitles { get; set; } = true;
    public bool SpeakerNames { get; set; } = true;
    public bool VisualSoundCues { get; set; } = false;
    public float SubtitleScale { get; set; } = 1.0f;
    
    public void Save(string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(this));
    
    public static AccessibilitySettings Load(string path) =>
        JsonSerializer.Deserialize<AccessibilitySettings>(
            File.ReadAllText(path)) ?? new();
}

public enum ColorblindMode { None, Protanopia, Deuteranopia, Tritanopia }
public enum AimAssistLevel { Off, Low, Medium, High }
```

### Integrating with Arch ECS

Create a singleton resource that systems can query:

```csharp
// On game start
world.Set(AccessibilitySettings.Load("settings/accessibility.json"));

// In any system
ref var settings = ref world.Get<AccessibilitySettings>();
float adjustedSpeed = baseSpeed * settings.GameSpeed;
```

### Scalable UI with Gum

Gum layouts should use percentage-based positioning and relative font sizes. Bind the `UIScale` factor to Gum's global scale property so all text and containers resize uniformly.

---

## Case Study: Celeste's Assist Mode

Celeste is the gold standard for accessible difficulty. Its Assist Mode offers:

| Option | Range | What It Does |
|--------|-------|-------------|
| Game Speed | 50%–100% | Slows everything proportionally |
| Infinite Stamina | On/Off | Removes climbing stamina limit |
| Dash Mode | Normal / Two Dashes / Infinite | Extra air dashes |
| Invincible | On/Off | Can't die |

**What makes it great:**

1. **No judgment** — A gentle disclaimer, no penalty, no locked achievements
2. **Granular** — Players tune exactly what they need, nothing more
3. **Accessible from pause** — Change mid-level, no restart required
4. **Preserves the experience** — Slowing the game to 70% keeps the feel while reducing difficulty

**Lesson for your game:** Don't think "easy mode." Think "which specific barriers can I lower independently?"

---

## Xbox Accessibility Guidelines (XAG) — Indie 2D Checklist

Microsoft's XAG is the most comprehensive industry standard. Key guidelines relevant to 2D indie games:

| XAG # | Guideline | Priority |
|--------|-----------|----------|
| 101 | Text/UI scale to 200% | High |
| 102 | Contrast ratio ≥ 4.5:1 for text | High |
| 103 | Don't rely on color alone | High |
| 104 | Subtitles on by default | High |
| 106 | Remappable controls | High |
| 107 | Allow toggle for holds | Medium |
| 108 | Adjustable difficulty | Medium |
| 110 | Screen narration / text-to-speech | Low (but nice) |
| 112 | Adjust game speed | Medium |
| 115 | No flashing >3 Hz over 25% of screen | High (seizure risk) |

Full guidelines: [Xbox Accessibility Guidelines](https://learn.microsoft.com/en-us/gaming/accessibility/xbox-accessibility-guidelines/101)

---

## The Business Case

### Audience Numbers

- **1 billion** people worldwide live with some form of disability (WHO)
- **~400 million** gamers have a disability (estimated)
- **8%** of male players are colorblind — that's potentially thousands of your players

### Platform Requirements

| Platform | Accessibility Requirements |
|----------|--------------------------|
| **Steam** | No formal requirements, but accessibility tags boost visibility in search |
| **Xbox** | XAG compliance expected for Game Pass features; required for some certifications |
| **PlayStation** | Accessibility features evaluated in TRC (Technical Requirements Checklist) |
| **Nintendo** | Lotcheck checks for seizure-risk content (flashing) |
| **iOS** | Apple encourages VoiceOver, Dynamic Type; not strictly required for games |

### ROI

- Accessibility features often improve UX for *all* players (subtitles, remappable controls, difficulty options)
- Positive press coverage and community goodwill
- Access to the Xbox/PlayStation accessibility showcase programs
- Reduced refund rates from frustrated players

---

## Quick-Start Checklist

- [ ] Remappable keyboard and gamepad controls
- [ ] Subtitles on by default with speaker names
- [ ] Never use color as the sole indicator
- [ ] Offer at least one colorblind palette option
- [ ] UI text scales to at least 150%
- [ ] Hold-to-X actions have a toggle option
- [ ] At least one difficulty-reducing option (more damage, slower speed, etc.)
- [ ] No flashing content >3 Hz over large screen areas
- [ ] Test with actual assistive technology users if possible

---

## Further Reading

- [Game Accessibility Guidelines](https://gameaccessibilityguidelines.com/) — Community-maintained checklist
- [Xbox Accessibility Guidelines](https://learn.microsoft.com/en-us/gaming/accessibility/xbox-accessibility-guidelines/101)
- [AbleGamers Includification Guide](https://accessible.games/includification/)
- [Ian Hamilton's GDC Talks](https://www.youtube.com/results?search_query=ian+hamilton+gdc+accessibility) — Practical game accessibility advice
- Celeste source/decompilation for Assist Mode implementation reference
