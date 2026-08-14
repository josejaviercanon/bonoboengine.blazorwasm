# G30 — Game Feel Tooling


> **Category:** Guide · **Related:** [C2 Game Feel & Genre Craft](../../core/game-design/C2_game_feel_and_genre_craft.md) · [G1 Custom Code Recipes](./G1_custom_code_recipes.md) · [G16 Debugging](./G16_debugging.md) · [G20 Camera Systems](./G20_camera_systems.md) · [E5 AI-Assisted Dev Workflow](../../core/ai-workflow/E5_ai_workflow.md) · [G5 UI Framework](./G5_ui_framework.md)

---

**Game feel is the one area where AI can't replace human judgment.** But you can build tools that compress the iteration loop from "change code → recompile → test → repeat" to "drag slider → feel it instantly." This guide covers lightweight ImGui-based tooling for tuning game feel parameters at runtime, recording/comparing iterations, and exporting tuned values for production use.

**Philosophy:** These tools exist to serve the game, not to become the game. Total budget: ~500-800 lines, 1-2 days of work. If you're spending more, you're falling into the [tool-building trap](../architecture/E8_monogamestudio_postmortem.md).

---

## Architecture: Data-Driven Feel

The foundation: **separate feel parameters from code.** Every tunable value lives in a data structure, not hardcoded in a system.

### Feel Profile (Pure Data)

```csharp
/// <summary>
/// All tunable movement/feel parameters in one place.
/// Serializable to JSON for save/load/preset support.
/// </summary>
public struct MovementFeelProfile
{
    // --- Jump ---
    public float JumpHeight { get; set; }            // pixels
    public float TimeToApex { get; set; }            // seconds
    public float FallGravityMultiplier { get; set; } // 1.5-3x
    public float CoyoteTimeMs { get; set; }          // 60-166ms
    public float JumpBufferMs { get; set; }          // 80-166ms
    public float ApexFloatMultiplier { get; set; }   // 0.3-0.7
    public int CornerCorrectionPx { get; set; }      // 2-4

    // --- Horizontal ---
    public float MaxRunSpeed { get; set; }
    public float GroundAcceleration { get; set; }
    public float GroundDeceleration { get; set; }
    public float AirAcceleration { get; set; }
    public float AirDeceleration { get; set; }

    // --- Camera ---
    public float CameraSmoothFactor { get; set; }    // 0.05-0.4
    public float DeadzoneWidth { get; set; }
    public float DeadzoneHeight { get; set; }
    public float LookaheadDistance { get; set; }

    // --- Juice ---
    public float ShakeTraumaDecayRate { get; set; }
    public float ShakeMaxOffset { get; set; }
    public int HitstopFramesLight { get; set; }      // 3-5
    public int HitstopFramesMedium { get; set; }     // 5-8
    public int HitstopFramesHeavy { get; set; }      // 8-13
    public float SquashScaleY { get; set; }           // 0.75
    public float StretchScaleY { get; set; }          // 1.2
    public int SquashFrames { get; set; }             // 2-4
    public int DamageFlashFrames { get; set; }        // 1-2

    /// <summary>Computed gravity from jump height and time to apex.</summary>
    public readonly float Gravity =>
        (-2f * JumpHeight) / (TimeToApex * TimeToApex);

    /// <summary>Computed initial jump velocity.</summary>
    public readonly float JumpVelocity =>
        (2f * JumpHeight) / TimeToApex;

    public static MovementFeelProfile Default => new()
    {
        JumpHeight = 64f,
        TimeToApex = 0.35f,
        FallGravityMultiplier = 2.0f,
        CoyoteTimeMs = 83f,
        JumpBufferMs = 100f,
        ApexFloatMultiplier = 0.5f,
        CornerCorrectionPx = 3,
        MaxRunSpeed = 180f,
        GroundAcceleration = 1200f,
        GroundDeceleration = 1800f,
        AirAcceleration = 900f,
        AirDeceleration = 600f,
        CameraSmoothFactor = 0.15f,
        DeadzoneWidth = 40f,
        DeadzoneHeight = 20f,
        LookaheadDistance = 60f,
        ShakeTraumaDecayRate = 2.0f,
        ShakeMaxOffset = 12f,
        HitstopFramesLight = 4,
        HitstopFramesMedium = 6,
        HitstopFramesHeavy = 10,
        SquashScaleY = 0.75f,
        StretchScaleY = 1.2f,
        SquashFrames = 3,
        DamageFlashFrames = 2
    };
}
```

### Loading the Profile

Systems read from the active profile rather than hardcoded constants:

```csharp
// In your movement system
public void Update(float dt, ref Position pos, ref Velocity vel,
                   in MovementFeelProfile feel)
{
    float gravity = vel.Y < 0 ? feel.Gravity : feel.Gravity * feel.FallGravityMultiplier;
    vel.Y += gravity * dt;
    // ...
}
```

### JSON Serialization

```csharp
public static class FeelProfileIO
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void Save(MovementFeelProfile profile, string path)
        => File.WriteAllText(path, JsonSerializer.Serialize(profile, Options));

    public static MovementFeelProfile Load(string path)
        => JsonSerializer.Deserialize<MovementFeelProfile>(
            File.ReadAllText(path), Options);
}
```

---

## ImGui Tuning Panel

An in-game overlay for adjusting feel parameters at runtime. Toggle with a hotkey (e.g., F1 in debug builds).

### Panel Implementation

```csharp
public class FeelTuningPanel
{
    private MovementFeelProfile _profile;
    private MovementFeelProfile _snapshot; // for undo
    private readonly List<(string Name, MovementFeelProfile Data)> _presets = new();
    private int _selectedPreset = -1;
    private bool _visible;
    private string _presetName = "";

    public ref MovementFeelProfile Profile => ref _profile;
    public bool Visible { get => _visible; set => _visible = value; }

    public FeelTuningPanel(MovementFeelProfile initial)
    {
        _profile = initial;
        _snapshot = initial;
    }

    public void Draw()
    {
        if (!_visible) return;

        ImGui.SetNextWindowSize(new Vector2(380, 600), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Game Feel Tuning", ref _visible)) { ImGui.End(); return; }

        // --- Presets ---
        if (ImGui.CollapsingHeader("Presets", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawPresetControls();
        }

        // --- Jump ---
        if (ImGui.CollapsingHeader("Jump", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.SliderFloat("Jump Height", ref _profile.JumpHeight, 16f, 200f);
            ImGui.SliderFloat("Time to Apex", ref _profile.TimeToApex, 0.1f, 0.8f);
            ImGui.SliderFloat("Fall Gravity ×", ref _profile.FallGravityMultiplier, 1.0f, 4.0f);
            ImGui.SliderFloat("Coyote Time (ms)", ref _profile.CoyoteTimeMs, 0f, 200f);
            ImGui.SliderFloat("Jump Buffer (ms)", ref _profile.JumpBufferMs, 0f, 200f);
            ImGui.SliderFloat("Apex Float ×", ref _profile.ApexFloatMultiplier, 0.1f, 1.0f);
            ImGui.SliderInt("Corner Correction (px)", ref _profile.CornerCorrectionPx, 0, 8);

            // Computed values (read-only display)
            ImGui.Separator();
            ImGui.TextColored(new Vector4(0.6f, 0.8f, 1f, 1f),
                $"Gravity: {_profile.Gravity:F1}  |  Jump Vel: {_profile.JumpVelocity:F1}");
        }

        // --- Horizontal Movement ---
        if (ImGui.CollapsingHeader("Movement"))
        {
            ImGui.SliderFloat("Max Run Speed", ref _profile.MaxRunSpeed, 50f, 500f);
            ImGui.SliderFloat("Ground Accel", ref _profile.GroundAcceleration, 200f, 3000f);
            ImGui.SliderFloat("Ground Decel", ref _profile.GroundDeceleration, 200f, 3000f);
            ImGui.SliderFloat("Air Accel", ref _profile.AirAcceleration, 200f, 2000f);
            ImGui.SliderFloat("Air Decel", ref _profile.AirDeceleration, 100f, 2000f);
        }

        // --- Camera ---
        if (ImGui.CollapsingHeader("Camera"))
        {
            ImGui.SliderFloat("Smooth Factor", ref _profile.CameraSmoothFactor, 0.01f, 0.5f);
            ImGui.SliderFloat("Deadzone W", ref _profile.DeadzoneWidth, 0f, 200f);
            ImGui.SliderFloat("Deadzone H", ref _profile.DeadzoneHeight, 0f, 200f);
            ImGui.SliderFloat("Lookahead", ref _profile.LookaheadDistance, 0f, 200f);
        }

        // --- Juice ---
        if (ImGui.CollapsingHeader("Juice"))
        {
            ImGui.SliderFloat("Shake Decay Rate", ref _profile.ShakeTraumaDecayRate, 0.5f, 5f);
            ImGui.SliderFloat("Shake Max Offset", ref _profile.ShakeMaxOffset, 2f, 30f);
            ImGui.SliderInt("Hitstop Light", ref _profile.HitstopFramesLight, 0, 10);
            ImGui.SliderInt("Hitstop Medium", ref _profile.HitstopFramesMedium, 0, 15);
            ImGui.SliderInt("Hitstop Heavy", ref _profile.HitstopFramesHeavy, 0, 20);
            ImGui.SliderFloat("Squash Y", ref _profile.SquashScaleY, 0.5f, 1.0f);
            ImGui.SliderFloat("Stretch Y", ref _profile.StretchScaleY, 1.0f, 1.5f);
            ImGui.SliderInt("Squash Frames", ref _profile.SquashFrames, 1, 8);
            ImGui.SliderInt("Damage Flash", ref _profile.DamageFlashFrames, 0, 5);
        }

        // --- Actions ---
        ImGui.Separator();
        if (ImGui.Button("Reset to Snapshot")) _profile = _snapshot;
        ImGui.SameLine();
        if (ImGui.Button("Take Snapshot")) _snapshot = _profile;
        ImGui.SameLine();
        if (ImGui.Button("Reset to Default")) _profile = MovementFeelProfile.Default;

        ImGui.End();
    }

    private void DrawPresetControls()
    {
        // Save current as preset
        ImGui.InputText("Name", ref _presetName, 64);
        ImGui.SameLine();
        if (ImGui.Button("Save Preset") && !string.IsNullOrWhiteSpace(_presetName))
        {
            _presets.Add((_presetName, _profile));
            _presetName = "";
        }

        // Load presets
        for (int i = 0; i < _presets.Count; i++)
        {
            if (ImGui.Selectable(_presets[i].Name, _selectedPreset == i))
            {
                _selectedPreset = i;
                _profile = _presets[i].Data;
            }
        }
    }
}
```

---

## Visual Debug Overlays

Overlays that make invisible feel mechanics visible. Draw these in a debug render pass on top of the game.

### Velocity Vector

```csharp
public static void DrawVelocityVector(SpriteBatch batch, Vector2 position,
    Vector2 velocity, float scale = 0.2f)
{
    Vector2 end = position + velocity * scale;
    // Draw line from position to end (use line renderer from G1)
    // Color: green for horizontal, red for vertical, blend for diagonal
    Color color = new(
        Math.Clamp(Math.Abs(velocity.X) / 200f, 0f, 1f),
        Math.Clamp(Math.Abs(velocity.Y) / 200f, 0f, 1f),
        0.2f);
    DrawLine(batch, position, end, color, 2f);
}
```

### Input Timing Visualizer

Shows when inputs were pressed relative to ground contact — essential for tuning coyote time and jump buffering.

```csharp
public class InputTimingVisualizer
{
    private readonly record struct InputEvent(float Time, string Label, Color Color);
    private readonly List<InputEvent> _events = new(64);
    private float _time;
    private const float DisplayDuration = 3f; // seconds to keep visible

    public void RecordJumpPress() =>
        _events.Add(new(_time, "Jump", Color.Cyan));

    public void RecordGroundContact() =>
        _events.Add(new(_time, "Ground", Color.Green));

    public void RecordCoyoteUsed() =>
        _events.Add(new(_time, "Coyote!", Color.Yellow));

    public void RecordBufferUsed() =>
        _events.Add(new(_time, "Buffer!", Color.Orange));

    public void Update(float dt) => _time += dt;

    public void Draw(SpriteBatch batch, Vector2 anchor)
    {
        // Draw a horizontal timeline strip
        float pixelsPerSecond = 200f;
        int y = 0;
        for (int i = _events.Count - 1; i >= 0; i--)
        {
            float age = _time - _events[i].Time;
            if (age > DisplayDuration) { _events.RemoveAt(i); continue; }

            float x = anchor.X - age * pixelsPerSecond;
            float alpha = 1f - (age / DisplayDuration);
            // Draw colored marker at (x, anchor.Y) with label
            // Fade out based on age
        }
    }
}
```

### Hitbox/Hurtbox Overlay

```csharp
public static void DrawColliderOverlay(SpriteBatch batch,
    in BoxCollider collider, in Position pos, bool isHurtbox)
{
    Color fill = isHurtbox
        ? new Color(255, 0, 0, 40)   // red, semi-transparent
        : new Color(0, 255, 0, 40);  // green

    Color outline = isHurtbox ? Color.Red : Color.Lime;

    Rectangle rect = new(
        (int)(pos.X + collider.OffsetX),
        (int)(pos.Y + collider.OffsetY),
        (int)collider.Width,
        (int)collider.Height);

    DrawFilledRect(batch, rect, fill);
    DrawRectOutline(batch, rect, outline, 1f);
}
```

---

## Ghost/Replay System

Record player state each frame, then replay as a translucent ghost while tuning parameters. Compare how the same input sequence feels with different profiles.

```csharp
public class GhostRecorder
{
    public readonly record struct FrameState(
        Vector2 Position, Vector2 Velocity, bool Grounded,
        bool JumpPressed, float ScaleX, float ScaleY);

    private readonly FrameState[] _buffer;
    private int _writeIndex;
    private int _frameCount;
    private bool _recording;

    public GhostRecorder(int maxFrames = 600) // 10 seconds at 60fps
    {
        _buffer = new FrameState[maxFrames];
    }

    public void StartRecording() { _writeIndex = 0; _frameCount = 0; _recording = true; }
    public void StopRecording() => _recording = false;

    public void RecordFrame(in FrameState state)
    {
        if (!_recording || _frameCount >= _buffer.Length) return;
        _buffer[_writeIndex++] = state;
        _frameCount++;
    }

    public FrameState? GetFrame(int index)
        => index < _frameCount ? _buffer[index] : null;

    public int FrameCount => _frameCount;
}

public class GhostPlayback
{
    private readonly GhostRecorder _source;
    private int _playbackFrame;
    private bool _playing;

    public GhostPlayback(GhostRecorder source) => _source = source;

    public void Start() { _playbackFrame = 0; _playing = true; }

    public void Update()
    {
        if (!_playing) return;
        _playbackFrame++;
        if (_playbackFrame >= _source.FrameCount) _playing = false;
    }

    public void Draw(SpriteBatch batch, Texture2D playerSprite)
    {
        if (!_playing) return;
        var frame = _source.GetFrame(_playbackFrame);
        if (frame is not { } f) return;

        batch.Draw(playerSprite, f.Position, null,
            new Color(100, 180, 255, 80), // translucent blue ghost
            0f, Vector2.Zero,
            new Vector2(f.ScaleX, f.ScaleY),
            SpriteEffects.None, 0f);
    }
}
```

### ImGui Controls for Ghost System

```csharp
if (ImGui.CollapsingHeader("Ghost Replay"))
{
    if (ImGui.Button("Start Recording")) _recorder.StartRecording();
    ImGui.SameLine();
    if (ImGui.Button("Stop")) _recorder.StopRecording();
    ImGui.SameLine();
    if (ImGui.Button("Play Ghost")) _playback.Start();

    ImGui.Text($"Recorded: {_recorder.FrameCount} frames " +
               $"({_recorder.FrameCount / 60f:F1}s)");
}
```

---

## Frame-by-Frame Advance

Essential for tuning hitstop, squash/stretch, and animation timing.

```csharp
public class FrameAdvanceController
{
    public bool Paused { get; private set; }
    public bool ShouldAdvance { get; private set; }

    /// <summary>Call each frame. Returns true if the game should update.</summary>
    public bool Update(IInputService input)
    {
        ShouldAdvance = false;

        if (input.WasKeyPressed(Keys.F5))
            Paused = !Paused;

        if (Paused && input.WasKeyPressed(Keys.F6))
            ShouldAdvance = true;

        return !Paused || ShouldAdvance;
    }
}

// In game loop:
if (_frameAdvance.Update(input))
{
    scene.Update(dt);
}
scene.Draw(batch); // always draw, even when paused
```

---

## Curve Visualizer

Display the computed jump arc and easing curves in ImGui. Helps visualize how parameter changes affect the trajectory.

```csharp
public static void DrawJumpArc(in MovementFeelProfile feel)
{
    // Simulate the jump arc from the profile's computed values
    float gravity = feel.Gravity;
    float fallGravity = gravity * feel.FallGravityMultiplier;
    float vy = feel.JumpVelocity;
    float y = 0f;
    float dt = 1f / 60f;

    const int MaxSteps = 120;
    var points = new Vector2[MaxSteps];

    for (int i = 0; i < MaxSteps; i++)
    {
        points[i] = new Vector2(i * 3f, -y * 0.5f); // scale for display
        float g = vy < 0 ? gravity : fallGravity;
        vy += g * dt;
        y += vy * dt;
        if (y < 0 && i > 5) break; // landed
    }

    // Draw as ImGui polyline
    var drawList = ImGui.GetWindowDrawList();
    var origin = ImGui.GetCursorScreenPos() + new Vector2(10, 100);
    for (int i = 1; i < MaxSteps && points[i] != Vector2.Zero; i++)
    {
        drawList.AddLine(
            origin + points[i - 1],
            origin + points[i],
            ImGui.GetColorU32(new Vector4(0.4f, 0.8f, 1f, 1f)),
            2f);
    }

    ImGui.Dummy(new Vector2(MaxSteps * 3f, 120)); // reserve space
}
```

---

## Putting It Together

### Integration into Your Game

```csharp
// In GameApp or your main scene
#if DEBUG
private FeelTuningPanel _tuningPanel;
private InputTimingVisualizer _inputViz;
private GhostRecorder _ghostRecorder;
private GhostPlayback _ghostPlayback;
private FrameAdvanceController _frameAdvance;
#endif

protected override void Initialize()
{
    _feelProfile = MovementFeelProfile.Default;

#if DEBUG
    _tuningPanel = new FeelTuningPanel(_feelProfile);
    _inputViz = new InputTimingVisualizer();
    _ghostRecorder = new GhostRecorder();
    _ghostPlayback = new GhostPlayback(_ghostRecorder);
    _frameAdvance = new FrameAdvanceController();
#endif
}

protected override void Update(GameTime gameTime)
{
    float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

#if DEBUG
    if (!_frameAdvance.Update(_input)) return;
    _feelProfile = _tuningPanel.Profile; // live update from sliders
    _inputViz.Update(dt);
    _ghostPlayback.Update();
#endif

    // All systems read from _feelProfile
    UpdateMovement(dt, ref _player, in _feelProfile);
    UpdateCamera(dt, in _feelProfile);
}
```

### Debug Hotkeys

| Key | Action |
|-----|--------|
| **F1** | Toggle tuning panel |
| **F2** | Toggle visual overlays (velocity, hitboxes, input timing) |
| **F3** | Start/stop ghost recording |
| **F4** | Play ghost |
| **F5** | Pause/unpause (frame advance mode) |
| **F6** | Advance one frame (while paused) |
| **F7** | Save current profile to JSON |
| **F8** | Load profile from JSON |

### Conditional Compilation

All tooling code lives behind `#if DEBUG` preprocessor directives. Release builds have zero overhead — no tooling code compiles in.

```xml
<!-- In .csproj, DEBUG is defined automatically for Debug configuration -->
<PropertyGroup Condition="'$(Configuration)' == 'Debug'">
    <DefineConstants>DEBUG;TRACE</DefineConstants>
</PropertyGroup>
```

---

## Line Count Budget

| Module | ~Lines | Time |
|--------|--------|------|
| MovementFeelProfile struct + IO | ~100 | 1 hour |
| FeelTuningPanel (ImGui) | ~150 | 2 hours |
| Visual overlays (velocity, hitbox, input timing) | ~120 | 2 hours |
| Ghost recorder + playback | ~100 | 1.5 hours |
| Frame advance controller | ~30 | 0.5 hours |
| Curve visualizer | ~60 | 1 hour |
| Integration + hotkeys | ~40 | 0.5 hours |
| **Total** | **~600** | **~8.5 hours** |

---

## What AI Can Generate Here

Following the [E5 workflow](../../core/ai-workflow/E5_ai_workflow.md):

- **Yes:** The `MovementFeelProfile` struct, JSON serialization, ImGui slider boilerplate, ghost recorder data structure, frame advance controller
- **No:** The actual slider ranges (those come from [C2 tuning values](../../core/game-design/C2_game_feel_and_genre_craft.md)), which parameters to expose (that's design judgment), and the final tuned values themselves

Write the interface/contract for each module yourself. Let AI scaffold the implementation. Review against the [AI code review checklist](../../core/ai-workflow/E5_ai_workflow.md#ai-code-review-checklist).

---

## Presets as Design Language

Named presets become a shared vocabulary for discussing game feel:

| Preset | Vibe | Key Differences |
|--------|------|-----------------|
| **Celeste** | Tight, responsive, forgiving | High accel/decel, generous coyote, apex float |
| **Hollow Knight** | Weighty, committed | Lower air control, longer squash, heavier hitstop |
| **Mario** | Floaty, momentum-based | Lower gravity, high air control, gradual decel |
| **Spelunky** | Precise, snappy | Very high gravity, minimal coyote, fast fall |

Build reference presets early. Use them as comparison targets during playtesting: "this feels too floaty — closer to Mario than Celeste."

---

## Production Workflow

1. **Start with defaults** based on [C2 reference values](../../core/game-design/C2_game_feel_and_genre_craft.md)
2. **Tune in-game** using the ImGui panel — iterate until it feels right
3. **Save as a preset** with a descriptive name
4. **Record ghost runs** to compare before/after tuning sessions
5. **Use frame advance** to verify hitstop, squash/stretch, and animation timing frame-by-frame
6. **Export the final profile** to `Resources/feel_profiles/default.json`
7. **Delete the tooling** from release builds (handled automatically by `#if DEBUG`)

The goal: the tools disappear, the feel remains.
