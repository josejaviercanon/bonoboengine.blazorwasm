# Input Handling -- Theory & Concepts

This document covers engine-agnostic input handling theory, including input abstraction, buffering, rebinding, dead zones, and multi-device support. Pseudocode is used throughout. For engine-specific implementations, see the relevant engine module.

---

## Input Abstraction Layer

The most important pattern in input handling: never let game logic reference physical inputs directly. Map physical inputs to game actions.

```
enum GameAction:
    Jump, Attack, Interact, Pause, MoveLeft, MoveRight

input_map = {
    Jump:     [Keyboard.Space, Gamepad.ButtonA],
    Attack:   [Keyboard.Z, Mouse.LeftButton, Gamepad.ButtonX],
    Interact: [Keyboard.E, Gamepad.ButtonY],
    Pause:    [Keyboard.Escape, Gamepad.Start],
}

function is_pressed(action):
    for binding in input_map[action]:
        if binding.just_pressed():
            return true
    return false
```

**Benefits:**
- Game logic is device-agnostic -- works identically for keyboard, gamepad, touch
- Rebinding only changes the map, not gameplay code
- Multiple physical inputs can trigger the same action (OR combinator)
- Compound inputs can require simultaneous presses (AND combinator)

---

## Edge Detection

Raw input state (held/not held) is insufficient. Games need edge detection:

- **Pressed** -- just pressed this frame (rising edge). Use for jumps, attacks, menu confirms.
- **Released** -- just released this frame (falling edge). Use for variable-height jumps, charged attacks.
- **Held** -- currently held down. Use for movement, aiming, sprinting.
- **HeldOnly** -- held but not just pressed. Distinguishes sustained input from the initial press.

```
function update_input_state():
    previous_state = current_state
    current_state = poll_hardware()

    pressed  = current_state AND NOT previous_state
    released = NOT current_state AND previous_state
    held     = current_state
```

---

## Input Buffering

Input buffering stores recent inputs and replays them when the action becomes possible. Without buffering, players who press a button 1-2 frames "too early" get nothing, and the game feels unresponsive.

### Jump Buffering

If the player presses jump while airborne (a few frames before landing), the jump executes the instant they touch ground.

```
BUFFER_WINDOW = 0.133  -- ~8 frames at 60fps

function on_jump_pressed():
    jump_buffer_timer = BUFFER_WINDOW

function update(dt):
    jump_buffer_timer -= dt

    if just_landed AND jump_buffer_timer > 0:
        execute_jump()
        jump_buffer_timer = 0
```

### General Input Buffer (Ring Buffer)

For games buffering multiple action types, store the last N frames of inputs:

```
BUFFER_SIZE = 10
input_ring = circular_buffer(BUFFER_SIZE)

function record_input(frame, actions):
    input_ring.push({ frame: frame, actions: actions })

function was_pressed_recently(action, window_frames):
    for entry in input_ring.last(window_frames):
        if action in entry.actions:
            return true
    return false
```

### Typical Buffer Windows

| Frames (60fps) | Seconds | Feel |
|---|---|---|
| 4-5 | 0.066-0.083 | Tight -- skilled players only |
| 6-8 | 0.100-0.133 | Standard -- feels responsive |
| 9-12 | 0.150-0.200 | Very generous |

---

## Input Consumption (Priority System)

When multiple systems listen for the same input (UI and gameplay), you need a priority system to prevent double-handling.

**Pattern: UI eats input first**

```
function update():
    begin_input_frame()

    -- UI gets first priority
    ui_handled = ui_system.handle_input()   -- uses "tracked" conditions

    -- Gameplay only processes if UI did not consume
    if not ui_handled:
        if is_pressed(Jump):
            player.jump()
        if is_pressed(Attack):
            player.attack()

    end_input_frame()
```

The "tracked" or "consumable" condition pattern: once a system consumes an input, other systems do not see it for that frame. This prevents a menu confirm from also triggering a player jump.

---

## Gamepad Dead Zones

Analog sticks rarely rest exactly at (0, 0). Dead zones filter out noise near the center.

### Axial Dead Zone (Simple, Flawed)

Apply dead zone to each axis independently.

```
function axial_deadzone(x, y, threshold):
    if abs(x) < threshold: x = 0
    if abs(y) < threshold: y = 0
    return (x, y)
```

**Problem:** Creates a cross-shaped dead zone. Diagonal inputs near the threshold feel wrong.

### Radial Dead Zone (Preferred)

Apply dead zone to the combined magnitude.

```
function radial_deadzone(x, y, threshold):
    magnitude = sqrt(x*x + y*y)
    if magnitude < threshold:
        return (0, 0)

    -- Rescale to 0-1 range after dead zone
    normalized = (magnitude - threshold) / (1 - threshold)
    normalized = clamp(normalized, 0, 1)

    -- Preserve direction, apply rescaled magnitude
    direction = (x / magnitude, y / magnitude)
    return direction * normalized
```

### Response Curves

After dead zone filtering, apply a response curve to give more precision at low deflections:

```
-- Quadratic: more precision at small inputs
output = input * input

-- Square root: more precision at large inputs (less common)
output = sqrt(input)
```

### Typical Dead Zone Values

| Value | Use Case |
|---|---|
| 0.10-0.15 | Standard for movement |
| 0.20-0.25 | Worn or loose controllers |
| 0.05-0.10 | Aiming (precision-critical) |

---

## Key Rebinding

A rebinding system lets players change which physical input maps to each game action. The key requirements:

1. **Store bindings as data** -- not hardcoded conditions
2. **Serialize to disk** -- JSON or similar format for persistence
3. **Rebuild conditions at runtime** -- when a binding changes, reconstruct the input condition

```
bindings = {
    Jump:   { key: Space, pad_button: A, mouse: null },
    Attack: { key: Z, pad_button: X, mouse: LeftButton },
}

function rebind(action, new_key):
    bindings[action].key = new_key
    rebuild_condition(action)
    save_bindings_to_file()

function rebuild_condition(action):
    binding = bindings[action]
    conditions = []
    if binding.key:        conditions.add(KeyCondition(binding.key))
    if binding.pad_button: conditions.add(PadCondition(binding.pad_button))
    if binding.mouse:      conditions.add(MouseCondition(binding.mouse))
    input_map[action] = AnyCondition(conditions)
```

### Rebinding UI Flow

1. Player selects an action to rebind
2. Show "Press any key..." prompt
3. Listen for the next physical input
4. Check for conflicts (same key bound to another action)
5. Assign the new binding and rebuild

---

## Simultaneous Multi-Device Input

Many players switch between keyboard and gamepad mid-session, or use both simultaneously (keyboard for menus, gamepad for gameplay).

**Pattern: take the input with greater magnitude**

```
function get_movement():
    digital = (0, 0)
    if key_held(Left):  digital.x -= 1
    if key_held(Right): digital.x += 1
    if key_held(Up):    digital.y -= 1
    if key_held(Down):  digital.y += 1
    if length(digital) > 1: digital = normalize(digital)

    analog = get_stick_with_deadzone(LEFT_STICK)

    -- Use whichever has greater magnitude
    if length(analog) > length(digital):
        return analog
    return digital
```

---

## Touch Input

Touch input maps to game actions through virtual controls:

- **Tap** -- position + press event (maps to attack, interact)
- **Swipe** -- start position + delta vector (maps to dash direction)
- **Virtual joystick** -- touch region that returns normalized (-1 to 1) offset from center
- **Pinch** -- two-finger distance change (maps to zoom)

```
function get_virtual_joystick(touch_pos, joystick_region):
    if not region_contains(joystick_region, touch_pos):
        return (0, 0)

    center = region_center(joystick_region)
    offset = touch_pos - center
    max_radius = joystick_region.width / 2

    if length(offset) > max_radius:
        offset = normalize(offset) * max_radius

    return offset / max_radius  -- normalized -1 to 1
```

---

## Input Recording and Playback

Recording raw inputs per tick enables replays, automated testing, and demo playback.

```
struct InputFrame:
    tick: int
    keys_down: set of keys
    mouse_position: vec2
    mouse_buttons: set of buttons

function record(tick):
    frame = InputFrame(tick, get_pressed_keys(), get_mouse_pos(), get_mouse_buttons())
    recording.append(frame)

function playback(tick):
    if playback_index >= recording.length:
        return null
    if recording[playback_index].tick == tick:
        return recording[playback_index++]
    return null
```

**Requirements for deterministic replay:**
- Fixed timestep (same dt every tick)
- Seed random number generators
- Record inputs at the logic tick level, not the frame level

---

## Haptic Feedback (Rumble)

Gamepad vibration reinforces game events. Most controllers have two motors:

- **Low frequency** -- heavy, bass rumble (landing, explosions)
- **High frequency** -- sharp, buzzy vibration (hits, UI feedback)

```
function rumble(player_index, low_intensity, high_intensity, duration):
    set_vibration(player_index, low_intensity, high_intensity)
    schedule_stop(player_index, duration)
```

Keep rumble short (0.05-0.3 seconds) and proportional to event intensity. Always allow players to disable it.

> Implementation examples for specific engines are available in the engine-specific modules.
