# G55 — Settings & Options Menu



> **Category:** Guide · **Related:** [G5 UI Framework](./G5_ui_framework.md) · [G6 Audio](./G6_audio.md) · [G7 Input Handling](./G7_input_handling.md) · [G19 Display & Resolution](./G19_display_resolution_viewports.md) · [G35 Accessibility](./G35_accessibility.md) · [G24 Window & Display Management](./G24_window_display_management.md)

---

## 1 — Settings Data Model

A single serializable class holds every user-facing option. Fields use simple types so `System.Text.Json` handles them without custom converters.

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Xna.Framework.Input;

/// <summary>Canonical settings blob — serialized to JSON, one file per user profile.</summary>
public sealed class GameSettings
{
    // ── schema version (bump when adding/removing fields) ──
    public int Version { get; set; } = 2;

    // ── audio ──
    public float MasterVolume  { get; set; } = 0.8f;
    public float MusicVolume   { get; set; } = 0.7f;
    public float SfxVolume     { get; set; } = 1.0f;
    public bool  MasterMuted   { get; set; }
    public bool  MusicMuted    { get; set; }
    public bool  SfxMuted      { get; set; }

    // ── video ──
    public int    ResolutionWidth  { get; set; } = 1920;
    public int    ResolutionHeight { get; set; } = 1080;
    public int    FullscreenMode   { get; set; } = 0; // 0=Windowed, 1=Fullscreen, 2=Borderless
    public bool   VSync            { get; set; } = true;
    public float  Brightness       { get; set; } = 1.0f;

    // ── gameplay ──
    public int    Difficulty       { get; set; } = 1; // 0=Easy, 1=Normal, 2=Hard
    public string Language         { get; set; } = "en";
    public bool   CameraShake      { get; set; } = true;
    public bool   ScreenFlash      { get; set; } = true;
    public bool   TutorialHints    { get; set; } = true;
    public int    AutoSaveMinutes  { get; set; } = 5;

    // ── accessibility ──
    public int   ColorblindMode       { get; set; } // 0=Off, 1=Protanopia, 2=Deuteranopia, 3=Tritanopia
    public float ScreenShakeIntensity { get; set; } = 1.0f;
    public float TextSizeMultiplier   { get; set; } = 1.0f;
    public bool  HighContrastMode     { get; set; }
    public bool  SubtitlesEnabled     { get; set; } = true;
    public int   SubtitleSize         { get; set; } = 1; // 0=Small, 1=Medium, 2=Large
    public bool  SubtitleBackground   { get; set; } = true;

    // ── controls (string keys for JSON compat) ──
    public Dictionary<string, Keys> KeyBindings { get; set; } = DefaultKeyBindings();
    public Dictionary<string, Buttons> PadBindings { get; set; } = DefaultPadBindings();

    // ── defaults ──
    public static Dictionary<string, Keys> DefaultKeyBindings() => new()
    {
        ["MoveUp"]    = Keys.W,
        ["MoveDown"]  = Keys.S,
        ["MoveLeft"]  = Keys.A,
        ["MoveRight"] = Keys.D,
        ["Jump"]      = Keys.Space,
        ["Attack"]    = Keys.J,
        ["Dash"]      = Keys.LeftShift,
        ["Interact"]  = Keys.E,
        ["Pause"]     = Keys.Escape,
        ["Inventory"] = Keys.Tab,
    };

    public static Dictionary<string, Buttons> DefaultPadBindings() => new()
    {
        ["Jump"]      = Buttons.A,
        ["Attack"]    = Buttons.X,
        ["Dash"]      = Buttons.RightTrigger,
        ["Interact"]  = Buttons.Y,
        ["Pause"]     = Buttons.Start,
        ["Inventory"] = Buttons.Back,
    };

    /// <summary>Deep-clone via round-trip serialization.</summary>
    public GameSettings Clone()
    {
        var json = JsonSerializer.Serialize(this, SettingsPersistence.JsonOpts);
        return JsonSerializer.Deserialize<GameSettings>(json, SettingsPersistence.JsonOpts)!;
    }
}
```

**Why a flat class?** Nested objects add friction for binding UI sliders to values. A flat shape means every setting is one property-path deep, which simplifies both the JSON and the Gum bindings.

---

## 2 — Settings Persistence

```csharp
using System.Runtime.InteropServices;
using System.Text.Json;

public static class SettingsPersistence
{
    private const string FileName = "settings.json";

    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Platform-appropriate settings directory.</summary>
    public static string GetSettingsDir(string appName = "MyGame")
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                appName);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support", appName);

        // Linux / fallback
        string xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
                      ?? Path.Combine(Environment.GetFolderPath(
                             Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(xdg, appName);
    }

    public static string GetFilePath(string appName = "MyGame")
        => Path.Combine(GetSettingsDir(appName), FileName);

    /// <summary>Load from disk, or return defaults on first run / corruption.</summary>
    public static GameSettings Load(string appName = "MyGame")
    {
        string path = GetFilePath(appName);
        if (!File.Exists(path))
            return new GameSettings();

        try
        {
            string json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<GameSettings>(json, JsonOpts)
                           ?? new GameSettings();
            return Migrate(settings);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Settings] Load failed: {ex.Message}");
            return new GameSettings();
        }
    }

    public static void Save(GameSettings settings, string appName = "MyGame")
    {
        string dir = GetSettingsDir(appName);
        Directory.CreateDirectory(dir);
        string json = JsonSerializer.Serialize(settings, JsonOpts);
        File.WriteAllText(GetFilePath(appName), json);
    }

    /// <summary>Forward-compat: migrate older schema versions.</summary>
    private static GameSettings Migrate(GameSettings s)
    {
        if (s.Version < 2)
        {
            // v1 had no accessibility block — apply safe defaults
            s.ScreenShakeIntensity = 1.0f;
            s.TextSizeMultiplier   = 1.0f;
            s.SubtitlesEnabled     = true;
            s.Version = 2;
        }
        return s;
    }
}
```

**Versioning strategy:** Bump `Version` whenever you add or remove a field. The `Migrate` method patches old files forward. `System.Text.Json` silently ignores unknown keys, so removing a field is safe — old files just carry dead weight until re-saved.

---

## 3 — Audio Settings

Audio volumes map to three logical buses. The effective volume of any sound is `MasterVolume × CategoryVolume` (both subject to their mute flag).

```csharp
/// <summary>Central audio bus that the rest of the engine queries.</summary>
public static class AudioBus
{
    private static GameSettings _s = null!;

    public static void Bind(GameSettings settings) => _s = settings;

    public static float EffectiveMusic =>
        (_s.MasterMuted ? 0f : _s.MasterVolume) *
        (_s.MusicMuted  ? 0f : _s.MusicVolume);

    public static float EffectiveSfx =>
        (_s.MasterMuted ? 0f : _s.MasterVolume) *
        (_s.SfxMuted    ? 0f : _s.SfxVolume);
}
```

When the player drags a volume slider, apply the change **immediately** so they hear the difference. No "Apply" button for audio — instant feedback is expected.

```csharp
// Inside the settings UI update loop:
void OnMasterSliderChanged(float value)
{
    _pending.MasterVolume = value;
    AudioBus.Bind(_pending); // live preview
    // Play a short SFX so the user hears the new level
    SoundManager.PlayPreview("ui_click");
}
```

> **Tip:** Store volumes as `0.0–1.0` floats. Display as `0–100%` in the UI with `(int)(value * 100)`. This avoids integer rounding drift on repeated load/save cycles.

---

## 4 — Video Settings

Video changes are the most dangerous category — a bad resolution can lock the player out. The standard pattern: preview the change, start a 15-second countdown, revert if the player doesn't confirm.

### 4.1 Enumerating Resolutions

```csharp
public static class VideoHelper
{
    /// <summary>Returns supported resolutions sorted descending.</summary>
    public static List<(int W, int H)> EnumerateResolutions()
    {
        var modes = GraphicsAdapter.DefaultAdapter.SupportedDisplayModes;
        return modes
            .Where(m => m.Width >= 1024) // skip ancient modes
            .Select(m => (m.Width, m.Height))
            .Distinct()
            .OrderByDescending(r => r.Width)
            .ThenByDescending(r => r.Height)
            .ToList();
    }

    public static string FormatResolution(int w, int h) => $"{w} × {h}";
}
```

### 4.2 Applying Video Changes

```csharp
public sealed class VideoApplier
{
    private readonly GraphicsDeviceManager _gdm;
    private GameSettings _snapshot; // pre-change snapshot for revert
    private float _revertTimer;
    private bool _awaitingConfirm;

    public VideoApplier(GraphicsDeviceManager gdm) => _gdm = gdm;

    public void Apply(GameSettings settings)
    {
        _snapshot = settings.Clone();

        _gdm.PreferredBackBufferWidth  = settings.ResolutionWidth;
        _gdm.PreferredBackBufferHeight = settings.ResolutionHeight;
        _gdm.SynchronizeWithVerticalRetrace = settings.VSync;

        switch (settings.FullscreenMode)
        {
            case 0: // Windowed
                _gdm.IsFullScreen = false;
                _gdm.HardwareModeSwitch = true;
                break;
            case 1: // Exclusive fullscreen
                _gdm.IsFullScreen = true;
                _gdm.HardwareModeSwitch = true;
                break;
            case 2: // Borderless
                _gdm.IsFullScreen = true;
                _gdm.HardwareModeSwitch = false;
                break;
        }

        _gdm.ApplyChanges();
        _revertTimer = 15f;
        _awaitingConfirm = true;
    }

    /// <summary>Call every frame while confirmation dialog is visible.</summary>
    public bool UpdateRevertCountdown(float dt, out int secondsLeft)
    {
        secondsLeft = (int)Math.Ceiling(_revertTimer);
        if (!_awaitingConfirm) return false;

        _revertTimer -= dt;
        if (_revertTimer <= 0f)
        {
            Revert();
            return true; // reverted
        }
        return false;
    }

    public void Confirm() => _awaitingConfirm = false;

    public void Revert()
    {
        _awaitingConfirm = false;
        Apply(_snapshot); // recursive but _awaitingConfirm will be re-set — Confirm() immediately
        Confirm();
    }
}
```

**Brightness** is best handled as a post-process multiply on the final render target. Store `Brightness` as `0.5–1.5` and pass it to a fullscreen shader:

```hlsl
// brightness.fx
float Brightness;
texture ScreenTexture;
sampler s = sampler_state { Texture = <ScreenTexture>; };

float4 PS(float2 uv : TEXCOORD0) : COLOR0
{
    return tex2D(s, uv) * Brightness;
}
```

---

## 5 — Input Rebinding

Integrates with **Apos.Input** for the actual input queries, but rebinding state is stored in `GameSettings.KeyBindings`.

### 5.1 Rebinding Flow

```csharp
public sealed class RebindingManager
{
    private string? _pendingAction;    // e.g. "Jump"
    private GameSettings _settings;

    public bool IsListening => _pendingAction != null;
    public string? ListeningAction => _pendingAction;

    public RebindingManager(GameSettings settings) => _settings = settings;

    /// <summary>Enter "press any key" mode for one action.</summary>
    public void StartListening(string action) => _pendingAction = action;

    public void Cancel() => _pendingAction = null;

    /// <summary>Call every frame while listening. Returns conflict info or null.</summary>
    public RebindResult? Update(KeyboardState kb)
    {
        if (_pendingAction == null) return null;

        foreach (Keys key in Enum.GetValues<Keys>())
        {
            if (key == Keys.None) continue;
            if (!kb.IsKeyDown(key)) continue;

            // Check for conflict
            string? conflict = _settings.KeyBindings
                .Where(kv => kv.Key != _pendingAction && kv.Value == key)
                .Select(kv => kv.Key)
                .FirstOrDefault();

            if (conflict != null)
                return new RebindResult(_pendingAction, key, conflict);

            // No conflict — bind directly
            _settings.KeyBindings[_pendingAction] = key;
            _pendingAction = null;
            return RebindResult.Success;
        }
        return null; // still waiting
    }

    /// <summary>Force-bind even with conflict (swap the two actions).</summary>
    public void ResolveConflict(string action, Keys newKey, string conflictAction)
    {
        Keys oldKey = _settings.KeyBindings[action];
        _settings.KeyBindings[conflictAction] = oldKey; // swap
        _settings.KeyBindings[action] = newKey;
        _pendingAction = null;
    }

    public void ResetToDefaults()
        => _settings.KeyBindings = GameSettings.DefaultKeyBindings();
}

public record struct RebindResult(string Action, Keys Key, string? ConflictAction)
{
    public static readonly RebindResult Success = new("", Keys.None, null);
    public bool IsSuccess => ConflictAction == null && Action == "";
    public bool HasConflict => ConflictAction != null;
}
```

### 5.2 Displaying Bindings in UI

```csharp
// For each bindable action, show: [Action Label]  [Current Key]  [Rebind Button]
foreach (var (action, key) in settings.KeyBindings)
{
    string label = Localize($"input.{action}"); // "Jump", "Attack", etc.
    string keyName = _rebinder.IsListening && _rebinder.ListeningAction == action
        ? "Press any key..."
        : key.ToString();

    DrawBindingRow(label, keyName, onClickRebind: () => _rebinder.StartListening(action));
}
```

Controller bindings work identically — swap `Keys` for `Buttons` and poll `GamePadState`. Keep two separate dictionaries so players can have both configured simultaneously.

---

## 6 — Accessibility Settings

Every accessibility option should default to the **least restrictive** setting (effects on, standard colors) so players who need adjustments opt in. See [G35 Accessibility](./G35_accessibility.md) for the full accessibility framework.

| Setting | Type | Values | Purpose |
|---|---|---|---|
| Colorblind Mode | enum | Off / Protanopia / Deuteranopia / Tritanopia | Shifts palette via shader |
| Screen Shake Intensity | float | 0.0–1.0 | Multiplier on camera shake amplitude |
| Screen Flash | bool | on/off | Disables white-flash effects |
| Text Size | float | 0.75–2.0 | Scales all UI text |
| High Contrast | bool | on/off | Adds outlines, darkens backgrounds |
| Subtitles | bool | on/off | Toggles subtitle rendering |
| Subtitle Size | enum | Small / Medium / Large | Subtitle font scale |
| Subtitle Background | bool | on/off | Dark box behind subtitle text |

```csharp
/// <summary>Query accessibility state anywhere in the engine.</summary>
public static class AccessibilityQuery
{
    private static GameSettings _s = null!;
    public static void Bind(GameSettings s) => _s = s;

    public static float ShakeMultiplier =>
        _s.CameraShake ? _s.ScreenShakeIntensity : 0f;

    public static bool AllowScreenFlash => _s.ScreenFlash;
    public static float TextScale => _s.TextSizeMultiplier;
    public static bool HighContrast => _s.HighContrastMode;

    public static int ColorblindMode => _s.ColorblindMode;
}
```

Camera shake code multiplies its amplitude by `AccessibilityQuery.ShakeMultiplier`. Screen flash effects check `AllowScreenFlash` before firing. This keeps accessibility logic **out** of gameplay code — it's just a multiplier lookup.

---

## 7 — Gameplay Settings

```csharp
public static class GameplayDefaults
{
    public static readonly string[] DifficultyLabels = { "Easy", "Normal", "Hard" };
    public static readonly int[] AutoSaveOptions = { 1, 3, 5, 10, 0 }; // 0 = disabled
    public static readonly string[] AutoSaveLabels = { "1 min", "3 min", "5 min", "10 min", "Off" };
}
```

- **Difficulty** — Mapped to gameplay multipliers elsewhere (damage dealt/received, resource drops). The settings menu only stores the index.
- **Language** — Stores an ISO 639-1 code (`"en"`, `"es"`, `"ja"`). On change, reload the string table from [G34 Localization](./G34_localization.md). Apply immediately so the settings menu itself re-renders in the new language.
- **Camera Shake / Screen Flash** — Duplicated from accessibility for discoverability. Both point to the same backing fields. Changing one updates the other.
- **Tutorial Hints** — Toggle in-game hint popups. Stored as bool.
- **Auto-save Frequency** — Index into `AutoSaveOptions`. The auto-save system reads `settings.AutoSaveMinutes`.

---

## 8 — Settings UI Layout

A tab strip across the top, content panel below. Each tab swaps the visible Gum container.

```
┌─────────────────────────────────────────────────────┐
│  [Audio]  [Video]  [Controls]  [Access.]  [Gameplay]│
├─────────────────────────────────────────────────────┤
│                                                     │
│   Master Volume    ████████░░  80%                  │
│   Music Volume     ███████░░░  70%                  │
│   SFX Volume       ██████████  100%                 │
│   □ Mute Master    □ Mute Music    □ Mute SFX      │
│                                                     │
│                          [ Apply ]  [ Cancel ]      │
└─────────────────────────────────────────────────────┘
```

### 8.1 Gum Widget Patterns

```csharp
/// <summary>Reusable settings widgets built with Gum containers.</summary>
public static class SettingsWidgets
{
    /// <summary>Horizontal slider: label on the left, bar + value on the right.</summary>
    public static ContainerRuntime CreateSlider(
        string label, float initial, Action<float> onChange)
    {
        var row = new ContainerRuntime
        {
            WidthUnits = DimensionUnitType.RelativeToParent, Width = 0,
            HeightUnits = DimensionUnitType.Absolute, Height = 40,
            ChildrenLayout = ChildrenLayout.LeftToRight,
        };

        var lbl = new TextRuntime { Text = label, Width = 200 };
        var slider = new SliderRuntime
        {
            Minimum = 0, Maximum = 100, Value = (int)(initial * 100),
            Width = 250, Height = 24,
        };
        var valText = new TextRuntime { Text = $"{(int)(initial * 100)}%", Width = 60 };

        slider.ValueChanged += (_, _) =>
        {
            float normalized = slider.Value / 100f;
            valText.Text = $"{slider.Value}%";
            onChange(normalized);
        };

        row.Children.Add(lbl);
        row.Children.Add(slider);
        row.Children.Add(valText);
        return row;
    }

    /// <summary>Dropdown: label + clickable current-value that opens a list.</summary>
    public static ContainerRuntime CreateDropdown(
        string label, string[] options, int selected, Action<int> onChange)
    {
        var row = new ContainerRuntime
        {
            WidthUnits = DimensionUnitType.RelativeToParent, Width = 0,
            Height = 40,
            ChildrenLayout = ChildrenLayout.LeftToRight,
        };

        var lbl = new TextRuntime { Text = label, Width = 200 };
        var combo = new ComboBoxRuntime { Width = 250, Height = 32 };
        foreach (string opt in options) combo.Items.Add(opt);
        combo.SelectedIndex = selected;
        combo.SelectionChanged += (_, _) => onChange(combo.SelectedIndex);

        row.Children.Add(lbl);
        row.Children.Add(combo);
        return row;
    }

    /// <summary>Toggle checkbox with label.</summary>
    public static ContainerRuntime CreateToggle(
        string label, bool initial, Action<bool> onChange)
    {
        var row = new ContainerRuntime
        {
            Height = 36,
            ChildrenLayout = ChildrenLayout.LeftToRight,
        };

        var check = new CheckBoxRuntime { IsChecked = initial, Width = 28, Height = 28 };
        var lbl = new TextRuntime { Text = label, Width = 300, Y = 4 };
        check.Checked += (_, _) => onChange(true);
        check.Unchecked += (_, _) => onChange(false);

        row.Children.Add(check);
        row.Children.Add(lbl);
        return row;
    }

    /// <summary>Key-capture button: shows current key, enters listen mode on click.</summary>
    public static ContainerRuntime CreateKeyCapture(
        string action, Keys current, Action onClick)
    {
        var row = new ContainerRuntime
        {
            Height = 36,
            ChildrenLayout = ChildrenLayout.LeftToRight,
        };

        var lbl = new TextRuntime { Text = action, Width = 200 };
        var btn = new ButtonRuntime { Width = 150, Height = 32 };
        btn.Text = current.ToString();
        btn.Click += (_, _) => onClick();

        row.Children.Add(lbl);
        row.Children.Add(btn);
        return row;
    }
}
```

### 8.2 Tab Controller

```csharp
public sealed class SettingsTabController
{
    private readonly ContainerRuntime[] _panels;
    private int _activeTab;

    public SettingsTabController(params ContainerRuntime[] panels)
    {
        _panels = panels;
        ShowTab(0);
    }

    public void ShowTab(int index)
    {
        _activeTab = index;
        for (int i = 0; i < _panels.Length; i++)
            _panels[i].Visible = (i == index);
    }
}
```

---

## 9 — Apply / Revert Pattern

Settings changes follow a **pending → apply → confirm** pipeline:

```
[Saved on disk] ──Load──▶ [Applied/Active] ──Clone──▶ [Pending/UI edits]
                                ▲                            │
                                │         Apply              │
                                └────────────────────────────┘
                                          │
                                  (video changes?)
                                    yes ──▶ Confirm dialog (15s timeout)
                                    no  ──▶ Committed immediately
```

```csharp
public sealed class SettingsManager
{
    public GameSettings Applied { get; private set; }
    public GameSettings Pending { get; private set; }

    private readonly VideoApplier _videoApplier;

    public SettingsManager(GraphicsDeviceManager gdm)
    {
        Applied = SettingsPersistence.Load();
        Pending = Applied.Clone();
        _videoApplier = new VideoApplier(gdm);

        // Bind global accessors
        AudioBus.Bind(Applied);
        AccessibilityQuery.Bind(Applied);
    }

    /// <summary>Has the user changed anything?</summary>
    public bool HasPendingChanges =>
        JsonSerializer.Serialize(Applied) != JsonSerializer.Serialize(Pending);

    /// <summary>Apply pending settings. Returns true if video confirm is needed.</summary>
    public bool Apply()
    {
        bool videoChanged =
            Applied.ResolutionWidth  != Pending.ResolutionWidth  ||
            Applied.ResolutionHeight != Pending.ResolutionHeight ||
            Applied.FullscreenMode   != Pending.FullscreenMode;

        Applied = Pending.Clone();
        AudioBus.Bind(Applied);
        AccessibilityQuery.Bind(Applied);

        if (videoChanged)
        {
            _videoApplier.Apply(Applied);
            return true; // caller should show confirm dialog
        }

        SettingsPersistence.Save(Applied);
        return false;
    }

    public void ConfirmVideo()
    {
        _videoApplier.Confirm();
        SettingsPersistence.Save(Applied);
    }

    public void RevertVideo()
    {
        _videoApplier.Revert();
        Applied = SettingsPersistence.Load();
        Pending = Applied.Clone();
        AudioBus.Bind(Applied);
        AccessibilityQuery.Bind(Applied);
    }

    /// <summary>Discard all pending edits.</summary>
    public void Cancel()
    {
        Pending = Applied.Clone();
        AudioBus.Bind(Applied); // restore live audio preview
    }

    public void UpdateRevertTimer(float dt, out bool reverted, out int secondsLeft)
    {
        reverted = _videoApplier.UpdateRevertCountdown(dt, out secondsLeft);
        if (reverted) RevertVideo();
    }
}
```

**Key rules:**
- Audio sliders apply to `Pending` **and** live-preview via `AudioBus.Bind(Pending)` while the menu is open. On cancel, rebind to `Applied`.
- Video changes only take effect on explicit "Apply". The confirm dialog renders on top with: *"Keep these settings? Reverting in {n}s…"* plus **Keep** / **Revert** buttons.
- Non-video, non-audio settings (gameplay, accessibility, controls) apply on "Apply" with no confirmation needed.

---

## 10 — Platform Defaults

```csharp
public static class PlatformDefaults
{
    public static GameSettings Create()
    {
        var s = new GameSettings();
        var adapter = GraphicsAdapter.DefaultAdapter;

        // Resolution: use current display mode or a safe fallback
        s.ResolutionWidth  = Math.Max(adapter.CurrentDisplayMode.Width, 1280);
        s.ResolutionHeight = Math.Max(adapter.CurrentDisplayMode.Height, 720);

        // Desktop defaults
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
        {
            s.FullscreenMode = 2; // borderless by default — safest
            s.VSync = true;
        }

        // Mobile defaults (if targeting Android/iOS via MonoGame)
        if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS())
        {
            s.FullscreenMode = 1; // true fullscreen
            s.TextSizeMultiplier = 1.25f; // larger text for small screens
            s.SubtitleSize = 2; // large subtitles
        }

        // Respect OS high-contrast (Windows)
        if (OperatingSystem.IsWindows())
        {
            try
            {
                s.HighContrastMode = System.Windows.Forms.SystemInformation.HighContrast;
            }
            catch { /* WinForms not available — ignore */ }
        }

        return s;
    }
}
```

Modify the Load path to use platform defaults on first run:

```csharp
// In SettingsPersistence.Load:
if (!File.Exists(path))
    return PlatformDefaults.Create();
```

---

## 11 — Practical Example: Wiring It All Together

Complete settings screen that ties data model, persistence, UI, and apply/revert into one cohesive system.

```csharp
public sealed class SettingsScreen
{
    private readonly SettingsManager _manager;
    private readonly SettingsTabController _tabs;
    private readonly ContainerRuntime _root;
    private readonly ContainerRuntime _confirmDialog;
    private bool _showConfirmDialog;

    // Shorthand
    private GameSettings P => _manager.Pending;

    public SettingsScreen(SettingsManager manager)
    {
        _manager = manager;
        _root = new ContainerRuntime
        {
            WidthUnits = DimensionUnitType.RelativeToParent, Width = 0,
            HeightUnits = DimensionUnitType.RelativeToParent, Height = 0,
        };

        // ── Build tab panels ──
        var audioPanel   = BuildAudioPanel();
        var videoPanel   = BuildVideoPanel();
        var controlPanel = BuildControlsPanel();
        var accessPanel  = BuildAccessibilityPanel();
        var gamePanel    = BuildGameplayPanel();

        _tabs = new SettingsTabController(
            audioPanel, videoPanel, controlPanel, accessPanel, gamePanel);

        // ── Tab buttons ──
        string[] tabNames = { "Audio", "Video", "Controls", "Accessibility", "Gameplay" };
        var tabBar = new ContainerRuntime
        {
            Height = 48, ChildrenLayout = ChildrenLayout.LeftToRight,
        };
        for (int i = 0; i < tabNames.Length; i++)
        {
            int idx = i;
            var btn = new ButtonRuntime { Text = tabNames[i], Width = 130, Height = 40 };
            btn.Click += (_, _) => _tabs.ShowTab(idx);
            tabBar.Children.Add(btn);
        }

        // ── Apply / Cancel ──
        var footer = new ContainerRuntime
        {
            Height = 50, ChildrenLayout = ChildrenLayout.LeftToRight,
        };
        var applyBtn  = new ButtonRuntime { Text = "Apply",  Width = 120, Height = 40 };
        var cancelBtn = new ButtonRuntime { Text = "Cancel", Width = 120, Height = 40 };
        applyBtn.Click  += (_, _) => OnApply();
        cancelBtn.Click += (_, _) => OnCancel();
        footer.Children.Add(applyBtn);
        footer.Children.Add(cancelBtn);

        // ── Confirm dialog (hidden by default) ──
        _confirmDialog = BuildConfirmDialog();
        _confirmDialog.Visible = false;

        // Assemble
        _root.Children.Add(tabBar);
        _root.Children.Add(audioPanel);
        _root.Children.Add(videoPanel);
        _root.Children.Add(controlPanel);
        _root.Children.Add(accessPanel);
        _root.Children.Add(gamePanel);
        _root.Children.Add(footer);
        _root.Children.Add(_confirmDialog);
    }

    // ── Panel Builders ──

    private ContainerRuntime BuildAudioPanel()
    {
        var panel = new ContainerRuntime { ChildrenLayout = ChildrenLayout.TopToBottom };
        panel.Children.Add(SettingsWidgets.CreateSlider("Master Volume", P.MasterVolume,
            v => { P.MasterVolume = v; AudioBus.Bind(P); }));
        panel.Children.Add(SettingsWidgets.CreateSlider("Music Volume", P.MusicVolume,
            v => { P.MusicVolume = v; AudioBus.Bind(P); }));
        panel.Children.Add(SettingsWidgets.CreateSlider("SFX Volume", P.SfxVolume,
            v => { P.SfxVolume = v; AudioBus.Bind(P); }));
        panel.Children.Add(SettingsWidgets.CreateToggle("Mute Master", P.MasterMuted,
            v => { P.MasterMuted = v; AudioBus.Bind(P); }));
        panel.Children.Add(SettingsWidgets.CreateToggle("Mute Music", P.MusicMuted,
            v => { P.MusicMuted = v; AudioBus.Bind(P); }));
        panel.Children.Add(SettingsWidgets.CreateToggle("Mute SFX", P.SfxMuted,
            v => { P.SfxMuted = v; AudioBus.Bind(P); }));
        return panel;
    }

    private ContainerRuntime BuildVideoPanel()
    {
        var panel = new ContainerRuntime { ChildrenLayout = ChildrenLayout.TopToBottom };
        var resolutions = VideoHelper.EnumerateResolutions();
        string[] resLabels = resolutions
            .Select(r => VideoHelper.FormatResolution(r.W, r.H)).ToArray();
        int currentRes = resolutions.FindIndex(r =>
            r.W == P.ResolutionWidth && r.H == P.ResolutionHeight);

        panel.Children.Add(SettingsWidgets.CreateDropdown("Resolution", resLabels,
            Math.Max(currentRes, 0), i => {
                P.ResolutionWidth  = resolutions[i].W;
                P.ResolutionHeight = resolutions[i].H;
            }));
        panel.Children.Add(SettingsWidgets.CreateDropdown("Display Mode",
            new[] { "Windowed", "Fullscreen", "Borderless" }, P.FullscreenMode,
            i => P.FullscreenMode = i));
        panel.Children.Add(SettingsWidgets.CreateToggle("VSync", P.VSync,
            v => P.VSync = v));
        panel.Children.Add(SettingsWidgets.CreateSlider("Brightness", P.Brightness,
            v => P.Brightness = 0.5f + v)); // map 0–1 slider to 0.5–1.5
        return panel;
    }

    private ContainerRuntime BuildControlsPanel()
    {
        var panel = new ContainerRuntime { ChildrenLayout = ChildrenLayout.TopToBottom };
        foreach (var (action, key) in P.KeyBindings)
        {
            panel.Children.Add(SettingsWidgets.CreateKeyCapture(action, key,
                () => _rebinder.StartListening(action)));
        }
        var resetBtn = new ButtonRuntime { Text = "Reset to Defaults", Width = 200, Height = 36 };
        resetBtn.Click += (_, _) => {
            _rebinder.ResetToDefaults();
            // Rebuild panel to reflect new keys
        };
        panel.Children.Add(resetBtn);
        return panel;
    }

    private ContainerRuntime BuildAccessibilityPanel()
    {
        var panel = new ContainerRuntime { ChildrenLayout = ChildrenLayout.TopToBottom };
        panel.Children.Add(SettingsWidgets.CreateDropdown("Colorblind Mode",
            new[] { "Off", "Protanopia", "Deuteranopia", "Tritanopia" },
            P.ColorblindMode, i => P.ColorblindMode = i));
        panel.Children.Add(SettingsWidgets.CreateSlider("Shake Intensity",
            P.ScreenShakeIntensity, v => P.ScreenShakeIntensity = v));
        panel.Children.Add(SettingsWidgets.CreateToggle("Screen Flash", P.ScreenFlash,
            v => P.ScreenFlash = v));
        panel.Children.Add(SettingsWidgets.CreateSlider("Text Size",
            (P.TextSizeMultiplier - 0.75f) / 1.25f, // normalize 0.75–2.0 → 0–1
            v => P.TextSizeMultiplier = 0.75f + v * 1.25f));
        panel.Children.Add(SettingsWidgets.CreateToggle("High Contrast", P.HighContrastMode,
            v => P.HighContrastMode = v));
        panel.Children.Add(SettingsWidgets.CreateToggle("Subtitles", P.SubtitlesEnabled,
            v => P.SubtitlesEnabled = v));
        panel.Children.Add(SettingsWidgets.CreateDropdown("Subtitle Size",
            new[] { "Small", "Medium", "Large" }, P.SubtitleSize,
            i => P.SubtitleSize = i));
        panel.Children.Add(SettingsWidgets.CreateToggle("Subtitle Background",
            P.SubtitleBackground, v => P.SubtitleBackground = v));
        return panel;
    }

    private ContainerRuntime BuildGameplayPanel()
    {
        var panel = new ContainerRuntime { ChildrenLayout = ChildrenLayout.TopToBottom };
        panel.Children.Add(SettingsWidgets.CreateDropdown("Difficulty",
            GameplayDefaults.DifficultyLabels, P.Difficulty,
            i => P.Difficulty = i));
        panel.Children.Add(SettingsWidgets.CreateToggle("Camera Shake", P.CameraShake,
            v => P.CameraShake = v));
        panel.Children.Add(SettingsWidgets.CreateToggle("Tutorial Hints", P.TutorialHints,
            v => P.TutorialHints = v));
        panel.Children.Add(SettingsWidgets.CreateDropdown("Auto-Save",
            GameplayDefaults.AutoSaveLabels,
            Array.IndexOf(GameplayDefaults.AutoSaveOptions, P.AutoSaveMinutes),
            i => P.AutoSaveMinutes = GameplayDefaults.AutoSaveOptions[i]));
        return panel;
    }

    private ContainerRuntime BuildConfirmDialog()
    {
        var dialog = new ContainerRuntime { Width = 400, Height = 160 };
        var label = new TextRuntime { Text = "Keep these display settings?" };
        var timer = new TextRuntime { Name = "TimerText", Text = "Reverting in 15s..." };
        var keepBtn   = new ButtonRuntime { Text = "Keep",   Width = 100, Height = 36 };
        var revertBtn = new ButtonRuntime { Text = "Revert", Width = 100, Height = 36 };
        keepBtn.Click   += (_, _) => { _manager.ConfirmVideo(); _showConfirmDialog = false; };
        revertBtn.Click += (_, _) => { _manager.RevertVideo();  _showConfirmDialog = false; };
        dialog.Children.Add(label);
        dialog.Children.Add(timer);
        dialog.Children.Add(keepBtn);
        dialog.Children.Add(revertBtn);
        return dialog;
    }

    // ── Actions ──

    private readonly RebindingManager _rebinder = new(new GameSettings());

    private void OnApply()
    {
        bool needsConfirm = _manager.Apply();
        if (needsConfirm)
        {
            _showConfirmDialog = true;
            _confirmDialog.Visible = true;
        }
    }

    private void OnCancel()
    {
        _manager.Cancel();
        // Close settings screen / return to pause menu
    }

    public void Update(float dt)
    {
        if (_showConfirmDialog)
        {
            _manager.UpdateRevertTimer(dt, out bool reverted, out int secs);
            var timerText = _confirmDialog.GetChild("TimerText") as TextRuntime;
            if (timerText != null) timerText.Text = $"Reverting in {secs}s...";
            if (reverted)
            {
                _showConfirmDialog = false;
                _confirmDialog.Visible = false;
            }
        }
    }
}
```

### Example `settings.json` on Disk

```json
{
  "Version": 2,
  "MasterVolume": 0.8,
  "MusicVolume": 0.55,
  "SfxVolume": 1.0,
  "MasterMuted": false,
  "MusicMuted": false,
  "SfxMuted": false,
  "ResolutionWidth": 2560,
  "ResolutionHeight": 1440,
  "FullscreenMode": 2,
  "VSync": true,
  "Brightness": 1.0,
  "Difficulty": 1,
  "Language": "en",
  "CameraShake": true,
  "ScreenFlash": true,
  "TutorialHints": true,
  "AutoSaveMinutes": 5,
  "ColorblindMode": 0,
  "ScreenShakeIntensity": 1.0,
  "TextSizeMultiplier": 1.0,
  "HighContrastMode": false,
  "SubtitlesEnabled": true,
  "SubtitleSize": 1,
  "SubtitleBackground": true,
  "KeyBindings": {
    "MoveUp": "W",
    "MoveDown": "S",
    "MoveLeft": "A",
    "MoveRight": "D",
    "Jump": "Space",
    "Attack": "J",
    "Dash": "LeftShift",
    "Interact": "E",
    "Pause": "Escape",
    "Inventory": "Tab"
  },
  "PadBindings": {
    "Jump": "A",
    "Attack": "X",
    "Dash": "RightTrigger",
    "Interact": "Y",
    "Pause": "Start",
    "Inventory": "Back"
  }
}
```

### Bootstrap in Game1

```csharp
public class Game1 : Game
{
    private SettingsManager _settingsManager;

    protected override void Initialize()
    {
        _settingsManager = new SettingsManager(_graphics);

        // Apply loaded video settings on startup (no confirm needed)
        var s = _settingsManager.Applied;
        _graphics.PreferredBackBufferWidth  = s.ResolutionWidth;
        _graphics.PreferredBackBufferHeight = s.ResolutionHeight;
        _graphics.SynchronizeWithVerticalRetrace = s.VSync;
        _graphics.IsFullScreen = s.FullscreenMode != 0;
        _graphics.HardwareModeSwitch = s.FullscreenMode == 1;
        _graphics.ApplyChanges();

        base.Initialize();
    }
}
```

---

## Design Checklist

| Concern | Solution |
|---|---|
| First run with no file | `PlatformDefaults.Create()` returns sensible values |
| Corrupt JSON | `Load()` catches exceptions, returns defaults |
| Old schema version | `Migrate()` patches forward, bumps version |
| Bad resolution locks player out | 15-second revert timeout |
| Key binding conflict | Swap with conflicting action, or warn and cancel |
| Audio feedback while adjusting | Live-preview via `AudioBus.Bind(Pending)` |
| Cancel discards everything | `Pending = Applied.Clone()` |
| Accessibility discoverable | Duplicated shake/flash toggles in Gameplay tab |
| Cross-platform paths | `RuntimeInformation` dispatch in persistence layer |

---

*Settings done right are invisible — players change what they need, everything just works, and nobody gets locked out of their own game.*
