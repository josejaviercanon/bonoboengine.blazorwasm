# G7 — Input Handling
> **Category:** Guide · **Related:** [R1 Library Stack](../R/R1_library_stack.md) · [C1 Genre Reference](../C/C1_genre_reference.md)

> Apos.Input deep dive — setup, input buffering, rebinding, multi-device support, and ECS integration.

---

### 1 Setup & Core API

```bash
dotnet add package Apos.Input
```

```csharp
using Apos.Input;
using Track = Apos.Input.Track;

protected override void LoadContent() => InputHelper.Setup(this);

protected override void Update(GameTime gameTime)
{
    InputHelper.UpdateSetup();
    // ... game logic ...
    InputHelper.UpdateCleanup();
}
```

**Condition types:**

| Type | Description |
|---|---|
| `KeyboardCondition` | Single keyboard key |
| `MouseCondition` | Mouse button (Left, Right, Middle, X1, X2) |
| `GamePadCondition` | Gamepad button + player index |
| `AnyCondition` | OR combinator — triggers if **any** child triggers |
| `AllCondition` | AND combinator — triggers if **all** children trigger |
| `Track.KeyboardCondition` | Same as Keyboard but **consumed** after first trigger per frame |
| `Track.MouseCondition` | Tracked mouse button |
| `Track.GamePadCondition` | Tracked gamepad button |

**Edge detection methods** on all `ICondition`:
- `Pressed()` — just pressed this frame (rising edge)
- `Released()` — just released this frame (falling edge)
- `Held()` — currently held
- `HeldOnly()` — held but not just pressed
- `Consume()` — manually mark as consumed for this frame

### 2 Input Abstraction Layer

```csharp
public enum GameAction { Jump, Attack, Interact, Pause, MoveLeft, MoveRight }

public class InputMap
{
    private readonly Dictionary<GameAction, ICondition> _bindings = new();

    public InputMap()
    {
        // Defaults — each action maps to multiple physical inputs
        _bindings[GameAction.Jump] = new AnyCondition(
            new KeyboardCondition(Keys.Space),
            new GamePadCondition(GamePadButton.A, 0));

        _bindings[GameAction.Attack] = new AnyCondition(
            new KeyboardCondition(Keys.Z),
            new MouseCondition(MouseButton.LeftButton),
            new GamePadCondition(GamePadButton.X, 0));

        _bindings[GameAction.Interact] = new AnyCondition(
            new KeyboardCondition(Keys.E),
            new GamePadCondition(GamePadButton.Y, 0));

        _bindings[GameAction.Pause] = new AnyCondition(
            new KeyboardCondition(Keys.Escape),
            new GamePadCondition(GamePadButton.Start, 0));
    }

    public bool Pressed(GameAction a) => _bindings[a].Pressed();
    public bool Held(GameAction a) => _bindings[a].Held();
    public bool Released(GameAction a) => _bindings[a].Released();
    public void Consume(GameAction a) => _bindings[a].Consume();
}
```

### 3 Key Rebinding System

```csharp
public class RebindableInputMap
{
    private Dictionary<GameAction, BindingConfig> _configs;

    public record BindingConfig(Keys? Key, GamePadButton? PadButton, MouseButton? Mouse);

    public void Rebind(GameAction action, Keys newKey)
    {
        _configs[action] = _configs[action] with { Key = newKey };
        RebuildCondition(action);
    }

    public void SaveBindings(string path)
    {
        var json = JsonSerializer.Serialize(_configs);
        File.WriteAllText(path, json);
    }

    public void LoadBindings(string path)
    {
        if (!File.Exists(path)) return;
        _configs = JsonSerializer.Deserialize<Dictionary<GameAction, BindingConfig>>(
            File.ReadAllText(path))!;
        foreach (var action in _configs.Keys)
            RebuildCondition(action);
    }

    private void RebuildCondition(GameAction action)
    {
        var cfg = _configs[action];
        var conditions = new List<ICondition>();
        if (cfg.Key.HasValue) conditions.Add(new KeyboardCondition(cfg.Key.Value));
        if (cfg.PadButton.HasValue) conditions.Add(new GamePadCondition(cfg.PadButton.Value, 0));
        if (cfg.Mouse.HasValue) conditions.Add(new MouseCondition(cfg.Mouse.Value));
        _conditions[action] = new AnyCondition(conditions.ToArray());
    }
}
```

### 4 Gamepad: Dead Zones & Analog

```csharp
public static class GamepadHelper
{
    public static Vector2 GetLeftStick(int playerIndex, float deadZone = 0.15f)
    {
        var state = GamePad.GetState(playerIndex);
        var raw = new Vector2(state.ThumbSticks.Left.X, -state.ThumbSticks.Left.Y);

        // Radial dead zone
        float magnitude = raw.Length();
        if (magnitude < deadZone) return Vector2.Zero;

        // Rescale to 0-1 range after dead zone
        float normalized = (magnitude - deadZone) / (1f - deadZone);
        normalized = MathHelper.Clamp(normalized, 0, 1);

        // Optional: apply response curve (quadratic for precision at low ranges)
        normalized *= normalized;

        return (raw / magnitude) * normalized;
    }

    public static float GetTrigger(int playerIndex, bool left)
    {
        var state = GamePad.GetState(playerIndex);
        float val = left ? state.Triggers.Left : state.Triggers.Right;
        return val > 0.1f ? (val - 0.1f) / 0.9f : 0f; // dead zone
    }

    public static void SetRumble(int playerIndex, float low, float high, float durationSec)
    {
        GamePad.SetVibration(playerIndex, low, high);
        // Schedule stop via timer (MonoGame doesn't auto-stop)
    }
}
```

### 5 Touch Input

```csharp
public class TouchInputHandler
{
    private readonly Dictionary<int, Vector2> _touchStarts = new();

    public void Update()
    {
        var touches = TouchPanel.GetState();
        foreach (var touch in touches)
        {
            switch (touch.State)
            {
                case TouchLocationState.Pressed:
                    _touchStarts[touch.Id] = touch.Position;
                    OnTap(touch.Position);
                    break;
                case TouchLocationState.Released:
                    if (_touchStarts.TryGetValue(touch.Id, out var start))
                    {
                        var delta = touch.Position - start;
                        if (delta.Length() > 50f) // swipe threshold
                            OnSwipe(start, delta);
                        _touchStarts.Remove(touch.Id);
                    }
                    break;
            }
        }

        // Pinch detection
        if (touches.Count >= 2)
        {
            var t0 = touches[0]; var t1 = touches[1];
            if (t0.TryGetPreviousLocation(out var prev0) &&
                t1.TryGetPreviousLocation(out var prev1))
            {
                float prevDist = Vector2.Distance(prev0.Position, prev1.Position);
                float currDist = Vector2.Distance(t0.Position, t1.Position);
                float pinchDelta = currDist - prevDist;
                if (MathF.Abs(pinchDelta) > 2f)
                    OnPinch(pinchDelta);
            }
        }
    }

    // Virtual joystick region
    public Vector2 GetVirtualJoystick(Rectangle region, TouchCollection touches)
    {
        foreach (var t in touches)
        {
            if (region.Contains(t.Position) && t.State != TouchLocationState.Released)
            {
                var center = region.Center.ToVector2();
                var offset = t.Position - center;
                float maxRadius = region.Width / 2f;
                if (offset.Length() > maxRadius)
                    offset = Vector2.Normalize(offset) * maxRadius;
                return offset / maxRadius; // -1 to 1
            }
        }
        return Vector2.Zero;
    }
}
```

### 6 Input Recording & Playback

```csharp
public class InputRecorder
{
    private readonly List<InputFrame> _frames = new();
    private int _playbackIndex;
    private bool _recording, _playing;

    public record InputFrame(int Tick, HashSet<Keys> KeysDown, Vector2 MousePos, bool MouseLeft);

    public void StartRecording() { _frames.Clear(); _recording = true; }
    public void StopRecording() => _recording = false;

    public void Capture(int tick)
    {
        if (!_recording) return;
        var kb = Keyboard.GetState();
        var ms = Mouse.GetState();
        _frames.Add(new InputFrame(
            tick,
            new HashSet<Keys>(kb.GetPressedKeys()),
            new Vector2(ms.X, ms.Y),
            ms.LeftButton == ButtonState.Pressed));
    }

    public InputFrame? GetPlaybackFrame(int tick)
    {
        if (!_playing || _playbackIndex >= _frames.Count) return null;
        if (_frames[_playbackIndex].Tick == tick)
            return _frames[_playbackIndex++];
        return null;
    }

    public void SaveReplay(string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(_frames));

    public void LoadReplay(string path)
    {
        _frames.Clear();
        _frames.AddRange(JsonSerializer.Deserialize<List<InputFrame>>(
            File.ReadAllText(path))!);
        _playbackIndex = 0;
        _playing = true;
    }
}
```

### 7 Simultaneous Keyboard + Gamepad

Apos.Input handles this natively via `AnyCondition`:

```csharp
// This just works — both inputs are polled every frame
ICondition move = new AnyCondition(
    new KeyboardCondition(Keys.Right),
    new GamePadCondition(GamePadButton.DPadRight, 0)
);

// For analog movement, combine digital + analog:
public Vector2 GetMovement(int padIndex)
{
    var digital = Vector2.Zero;
    if (KeyboardCondition.Held(Keys.Left)) digital.X -= 1;
    if (KeyboardCondition.Held(Keys.Right)) digital.X += 1;
    if (KeyboardCondition.Held(Keys.Up)) digital.Y -= 1;
    if (KeyboardCondition.Held(Keys.Down)) digital.Y += 1;
    if (digital.Length() > 1) digital = Vector2.Normalize(digital);

    var analog = GamepadHelper.GetLeftStick(padIndex);

    // Use whichever has greater magnitude
    return analog.Length() > digital.Length() ? analog : digital;
}
```

### 8 Input Priority & Consumption

The Track system prevents double-handling. For UI-eats-gameplay:

```csharp
public void Update(GameTime gameTime)
{
    InputHelper.UpdateSetup();

    // UI gets first priority — uses Track conditions
    bool uiHandled = _uiSystem.HandleInput(); // internally uses Track.* conditions

    // Gameplay only processes if UI didn't consume
    if (!uiHandled)
    {
        if (_inputMap.Pressed(GameAction.Jump))
            _player.Jump();
        if (_inputMap.Pressed(GameAction.Attack))
            _player.Attack();
    }

    InputHelper.UpdateCleanup();
}

// Inside UI system:
public bool HandleInput()
{
    // Using Track conditions — if consumed here, gameplay won't see them
    var confirm = new Track.KeyboardCondition(Keys.Enter);
    if (confirm.Pressed() && _focusedButton != null)
    {
        _focusedButton.Activate();
        return true;
    }
    return false;
}
```

> **Pattern:** UI layer uses `Track.*` conditions. Gameplay uses regular conditions. If the UI consumes a key via Track, the regular condition won't fire for the same frame. This is Apos.Input's killer feature for input layering.
