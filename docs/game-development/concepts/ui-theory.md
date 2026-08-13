# UI Architecture — Theory & Concepts

> **Category:** Concept · **Related:** [G_stitch_ui_workflow](../game-design/G_stitch_ui_workflow.md) · [animation-theory](./animation-theory.md) · [input-handling-theory](./input-handling-theory.md) · [scene-management-theory](./scene-management-theory.md)

Engine-agnostic theory for game UI architecture. Covers rendering paradigms, layout systems, control patterns, screen management, HUD design, accessibility, performance, and common anti-patterns. For engine-specific implementations, see the relevant engine module.

---

## Table of Contents

1. [UI Rendering Paradigms](#1-ui-rendering-paradigms)
2. [Layout System Architecture](#2-layout-system-architecture)
3. [The Visual Tree](#3-the-visual-tree)
4. [Common Controls & Patterns](#4-common-controls--patterns)
5. [Screen & Layer Management](#5-screen--layer-management)
6. [HUD Design](#6-hud-design)
7. [Inventory & Container UI](#7-inventory--container-ui)
8. [Dialogue & Text Systems](#8-dialogue--text-systems)
9. [Theming & Visual States](#9-theming--visual-states)
10. [Data Binding & Reactive UI](#10-data-binding--reactive-ui)
11. [Input Handling & Navigation](#11-input-handling--navigation)
12. [Tooltips, Popups & Contextual UI](#12-tooltips-popups--contextual-ui)
13. [Animation & Transitions](#13-animation--transitions)
14. [Localization](#14-localization)
15. [Accessibility](#15-accessibility)
16. [Resolution & Scaling](#16-resolution--scaling)
17. [Performance](#17-performance)
18. [Anti-Patterns](#18-anti-patterns)
19. [UI Architecture Decision Framework](#19-ui-architecture-decision-framework)

---

## 1. UI Rendering Paradigms

### Immediate Mode

UI is rebuilt every frame from code. No persistent widget state. The calling code IS the layout.

```
function draw_debug_overlay():
    if button("Reload Level"):
        reload_current_level()
    slider("Time Scale", time_scale, 0.0, 2.0)
    label(f"FPS: {fps}")
    label(f"Entities: {entity_count}")
```

**Strengths:** Zero boilerplate, trivial to add/remove elements, no state synchronization bugs, perfect for debug tools and prototypes.

**Weaknesses:** Rebuilds every frame (layout cost scales with element count), stateless design makes complex widgets hard (text editing, drag-and-drop, scroll position), difficult to animate transitions.

**Use for:** Debug overlays, dev tools, rapid prototyping, in-game consoles.

### Retained Mode

UI elements are created once and persist in a tree structure. Properties are modified to update appearance. State lives in the widget objects.

```
function create_main_menu():
    panel = create_panel()
    title = create_label("My Game", font_large)
    start_btn = create_button("Start")
    start_btn.on_click = begin_game
    options_btn = create_button("Options")
    options_btn.on_click = show_options
    panel.add_children([title, start_btn, options_btn])
    return panel
```

**Strengths:** Efficient (only re-layouts on property changes), natural for complex interactions (drag, scroll, focus), supports animation, matches how designers think about UI.

**Weaknesses:** State synchronization (game data ↔ widget state), more setup code, lifecycle management (creating/destroying widgets).

**Use for:** All production game UI — menus, inventory, HUD, shops, dialogue.

### Hybrid Approach

Most shipped games use retained mode for persistent UI (menus, HUD, inventory) and immediate mode for transient debug overlays. Some frameworks support both natively; otherwise, maintain two separate render paths.

### Direct Draw (No Framework)

For minimal HUDs (score counter, health bar, ammo count), drawing sprites and text directly in the render loop can be simpler than integrating a UI framework:

```
function draw_hud():
    draw_sprite(heart_icon, x=10, y=10)
    draw_text(f"{player.hp}/{player.max_hp}", x=40, y=12)
    draw_sprite(coin_icon, x=10, y=40)
    draw_text(f"{player.gold}", x=40, y=42)
```

This avoids framework overhead but becomes unmanageable beyond ~10 elements. Transition to a real framework when you need layout, scrolling, or input handling.

---

## 2. Layout System Architecture

### Position Models

| Model | Description | Use Case |
|-------|-------------|----------|
| **Absolute** | Fixed pixel offset from parent edge | Pixel-art games, fixed-resolution HUDs |
| **Anchored** | Offset from a chosen parent edge/corner | Elements that stick to screen edges |
| **Percentage** | Fraction of parent dimension | Responsive panels, split-screen |
| **Center-relative** | Offset from parent center | Centered menus, title screens |
| **Auto (content-fit)** | Size determined by children | Tooltips, dynamic text boxes |

### Size Models

| Model | Behavior |
|-------|----------|
| **Fixed** | Exact pixel size, ignores parent |
| **Relative** | Percentage of parent size |
| **Fill remaining** | Expand to fill unused space in a stack |
| **Ratio** | Proportional share among siblings |
| **Min/max constrained** | Any model above, clamped to bounds |

### Layout Containers

**Stack (Vertical/Horizontal):** Children arranged sequentially along one axis. Configurable spacing (gap) and alignment (start, center, end, stretch).

```
vertical_stack:
    spacing: 8px
    alignment: center
    children: [title_label, play_button, options_button, quit_button]
```

**Grid:** Children placed in rows × columns. Cell size can be fixed or proportional. Useful for inventory slots, skill trees, card hands.

```
grid:
    columns: 6
    rows: 4
    cell_size: 64x64
    cell_spacing: 4px
```

**Wrap/Flow:** Like a horizontal stack that wraps to the next row when the container width is exceeded. Good for tag lists, achievement badges, variable-count items.

**Canvas/Free:** Children positioned absolutely within the container. No automatic layout. Used for drag-and-drop surfaces, map overlays, and custom layouts.

### Anchoring & Margins

Anchoring controls which parent edges a child attaches to:

```
// Health bar: anchored top-left, 10px margin
health_bar.anchor = TOP_LEFT
health_bar.margin = {left: 10, top: 10}

// Minimap: anchored top-right
minimap.anchor = TOP_RIGHT
minimap.margin = {right: 10, top: 10}

// Dialogue box: anchored bottom, stretched horizontally
dialogue.anchor = BOTTOM_STRETCH
dialogue.margin = {left: 20, right: 20, bottom: 20}
dialogue.height = 120
```

### Layout Calculation Pipeline

```
1. Measure pass (bottom-up)
   - Leaf nodes report their desired size
   - Containers compute desired size from children + padding + spacing

2. Arrange pass (top-down)
   - Root gets the screen rectangle
   - Containers assign final rectangles to each child
   - Children may stretch, shrink, or align within their allotted space

3. Render pass
   - Walk the tree in order, draw each visible element at its final position
```

**Critical rule:** Never modify layout properties during the render pass. This causes layout thrashing — the tree recalculates on the next frame, potentially causing visual flickering or infinite layout loops.

---

## 3. The Visual Tree

Game UI is organized as a tree of nodes (the "visual tree"):

```
Screen Root
├── HUD Layer (always visible during gameplay)
│   ├── Health Bar
│   ├── Ammo Counter
│   └── Minimap
├── Pause Menu (shown when paused)
│   ├── Resume Button
│   ├── Options Button
│   └── Quit Button
└── Dialogue Layer (shown during conversations)
    ├── Speaker Portrait
    ├── Name Label
    └── Text Box
```

### Tree Properties

- **Rendering order:** Children render back-to-front (later children draw on top). Siblings render in tree order.
- **Input order:** Input hits front-to-back (reverse render order). The topmost visible element receives input first.
- **Coordinate inheritance:** Child positions are relative to parent. Moving a parent moves all children.
- **Visibility propagation:** Hiding a parent hides all children. Disabling a parent disables input for all children.
- **Clipping:** A container can clip children to its bounds (scrollable areas, masked panels).

### Z-Order vs Tree Order

For most UI, tree order is sufficient. Use explicit Z-order overrides sparingly — they break the mental model and make debugging harder. If you need an element on top of everything (tooltip, drag preview, notification), place it in a dedicated top-level overlay layer rather than using Z-order hacks on deeply nested elements.

---

## 4. Common Controls & Patterns

### Basic Controls

| Control | Purpose | Key Properties |
|---------|---------|----------------|
| **Label** | Display text | text, font, color, alignment, wrap |
| **Button** | Click/tap action | text, icon, on_click, enabled |
| **Toggle/Checkbox** | Boolean on/off | is_checked, on_toggled |
| **Slider** | Numeric range selection | value, min, max, step, on_changed |
| **TextInput** | Editable text field | text, placeholder, max_length, on_submit |
| **ProgressBar** | Display a fill fraction | value (0-1), fill_color, label |
| **Image** | Display a sprite/texture | texture, stretch_mode, tint |

### Compound Controls

| Control | Composition |
|---------|-------------|
| **Dropdown** | Button + popup list of options |
| **Tab Bar** | Row of toggle buttons + content panels |
| **ScrollView** | Clipping container + scroll bars + content |
| **Spinner/NumericInput** | TextInput + increment/decrement buttons |
| **Color Picker** | Gradient canvas + hue slider + hex input |

### Game-Specific Controls

| Control | Use Case |
|---------|----------|
| **Health Bar** | Segmented or smooth fill with damage flash, heal glow, delayed drain |
| **Cooldown Radial** | Circular fill indicating ability cooldown remaining |
| **Stat Bar** | Horizontal bar with segments (XP, stamina, shield on top of health) |
| **Item Slot** | Grid cell with icon, stack count, rarity border, cooldown overlay |
| **Minimap** | Render target showing top-down world view with player/enemy markers |
| **Damage Number** | Floating text that rises and fades (critical = bigger, different color) |
| **Notification Toast** | Slide-in message that auto-dismisses (achievement, item pickup) |

---

## 5. Screen & Layer Management

### Screen Stack

Organize UI into discrete screens that transition in and out:

```
class UIScreen:
    root: Container
    is_transparent: bool     // true = screen below is still visible
    blocks_input: bool       // true = screens below don't receive input
    blocks_game_update: bool // true = game world pauses

    on_enter(previous_screen): build layout, play enter animation
    on_exit(next_screen): play exit animation, destroy layout
    on_pause(): another screen pushed on top
    on_resume(): top screen popped, this screen is active again
```

```
ScreenManager:
    stack: [GameplayHUD, PauseMenu, OptionsPanel]
                              ↑ top = active screen

    push(screen): stack.push(screen), screen.on_enter()
    pop(): top.on_exit(), stack.pop(), new_top.on_resume()
    replace(screen): pop() then push(screen)
```

### Layer Model

Separate UI into layers that can be independently shown/hidden:

```
Layer 0: World UI (health bars over enemies, floating names)
Layer 1: HUD (player health, ammo, minimap, objective tracker)
Layer 2: Menus (pause, inventory, map, journal)
Layer 3: Popups (confirmation dialogs, item tooltips)
Layer 4: System (loading screen, error messages, notifications)
Layer 5: Debug (FPS counter, console, entity inspector)
```

Each layer has its own input priority. Higher layers consume input first. A modal popup on Layer 3 can block input to Layers 0-2.

### Modal vs Non-Modal

**Modal:** Blocks all input to elements behind it. Use for confirmation dialogs ("Are you sure you want to quit?"), critical errors, and save/load screens.

**Non-modal:** Does not block background input. Use for chat windows, notification toasts, minimaps, and optional overlays.

### Transition Types

| Transition | Effect | Use Case |
|------------|--------|----------|
| **Cut** | Instant switch | Menu tabs, fast navigation |
| **Fade** | Alpha crossfade | Screen transitions, scene changes |
| **Slide** | Screen slides in from edge | Settings panels, inventory drawers |
| **Scale** | Grows from point | Popup dialogs, tooltips |
| **Dissolve** | Noise-based reveal | Stylized transitions |

---

## 6. HUD Design

### Information Hierarchy

Not all HUD elements deserve equal screen real estate. Prioritize by frequency of player need:

```
Always visible:  Health, ammo/resource (elements checked every few seconds)
On change:       Damage indicators, pickup notifications, combo counter
On demand:       Map, quest objectives (player-triggered)
Contextual:      Interaction prompts, tutorial hints (situation-dependent)
Never on HUD:    Detailed stats, skill trees, inventory (use full-screen menus)
```

### HUD Layout Zones

```
┌──────────────────────────────────────────┐
│ [Health]                    [Minimap]     │  ← Top: persistent status
│                                          │
│                                          │
│ [Objective]            [Interaction      │  ← Middle: contextual
│                         Prompt]          │
│                                          │
│                                          │
│ [Chat/Log]     [Abilities] [Ammo/Items]  │  ← Bottom: action-related
└──────────────────────────────────────────┘
```

**Convention:** Players scan top-left first (health), then bottom-center (actions/abilities), then top-right (minimap/resources). Place the most critical survival info at top-left.

### Animated Value Changes

When a numeric value changes (HP, gold, score), animate the transition rather than snapping:

```
class AnimatedCounter:
    display_value: float
    target_value: float
    speed: float = 5.0

    set_target(new_value):
        target_value = new_value

    update(delta):
        display_value = lerp(display_value, target_value, 1 - exp(-speed * delta))

    // For health bars: show damage as a delayed drain
    // Current HP (green) snaps immediately
    // "Ghost" bar (red/yellow) drains slowly to show damage taken
```

### Damage Direction Indicators

Show where damage came from with a screen-edge vignette or directional arrow:

```
function show_damage_indicator(damage_source_position):
    direction = (damage_source_position - player.position).normalized()
    angle = atan2(direction.y, direction.x)
    // Display a red wedge on the screen edge at this angle
    // Fade over 1-2 seconds
    spawn_indicator(angle, duration=1.5, color=red, opacity=0.6)
```

---

## 7. Inventory & Container UI

### Slot-Based Inventory

The most common game inventory model: a grid of slots, each holding an item stack.

```
class InventorySlot:
    item: ItemData or null
    count: int
    max_stack: int

class InventoryGrid:
    slots: array[rows * columns] of InventorySlot
    
    add_item(item, count) -> remainder:
        // First: try to stack with existing matching items
        for slot in slots where slot.item == item and slot.count < slot.max_stack:
            space = slot.max_stack - slot.count
            added = min(count, space)
            slot.count += added
            count -= added
            if count == 0: return 0
        // Then: place in first empty slot
        for slot in slots where slot.item == null:
            slot.item = item
            slot.count = min(count, item.max_stack)
            count -= slot.count
            if count == 0: return 0
        return count  // overflow — inventory full
```

### Drag-and-Drop

Drag-and-drop is the standard interaction model for inventory manipulation:

```
DragDropManager:
    dragging: {item, count, source_slot, ghost_sprite}

    on_slot_press(slot):
        if slot.item != null:
            dragging = {item: slot.item, count: slot.count, source: slot}
            slot.clear()
            create_ghost_sprite(dragging.item)  // follows cursor

    on_slot_release(target_slot):
        if target_slot.item == null:
            target_slot.set(dragging.item, dragging.count)
        elif target_slot.item == dragging.item:
            // Stack merge
            overflow = target_slot.add(dragging.count)
            if overflow > 0:
                dragging.source.set(dragging.item, overflow)
        else:
            // Swap
            temp = target_slot.get()
            target_slot.set(dragging.item, dragging.count)
            dragging.source.set(temp.item, temp.count)
        destroy_ghost()

    on_release_outside():
        // Return to source or drop in world
        dragging.source.set(dragging.item, dragging.count)
        destroy_ghost()
```

### Item Tooltip

Show detailed info on hover/focus. Position the tooltip to avoid going off-screen:

```
function position_tooltip(tooltip, anchor_rect, screen_size):
    // Try right of anchor
    x = anchor_rect.right + 8
    if x + tooltip.width > screen_size.x:
        x = anchor_rect.left - tooltip.width - 8  // flip to left
    // Try below anchor
    y = anchor_rect.top
    if y + tooltip.height > screen_size.y:
        y = screen_size.y - tooltip.height  // clamp to bottom
    tooltip.position = (x, y)
```

---

## 8. Dialogue & Text Systems

### Typewriter Effect

Text reveals character by character for dramatic effect:

```
class TypewriterText:
    full_text: string
    visible_chars: int = 0
    chars_per_second: float = 30.0
    accumulator: float = 0.0

    update(delta):
        if visible_chars >= full_text.length:
            return  // done
        accumulator += chars_per_second * delta
        while accumulator >= 1.0 and visible_chars < full_text.length:
            visible_chars += 1
            accumulator -= 1.0
            // Speed variation: pause longer on punctuation
            if full_text[visible_chars - 1] in '.!?':
                accumulator -= 4.0  // ~4 char delay on sentence end
            elif full_text[visible_chars - 1] == ',':
                accumulator -= 2.0  // ~2 char delay on comma

    skip():
        visible_chars = full_text.length

    is_complete() -> bool:
        return visible_chars >= full_text.length
```

### Dialogue Tree Architecture

```
class DialogueNode:
    speaker: string
    text: string
    choices: list of DialogueChoice  // empty = auto-advance

class DialogueChoice:
    label: string            // button text
    condition: func -> bool  // optional visibility gate
    next_node: DialogueNode
    effects: list of func    // side effects (set flag, give item, change reputation)

class DialogueRunner:
    current_node: DialogueNode
    history: list             // for backtracking/journal

    advance():
        if current_node.choices.empty():
            current_node = current_node.next  // linear advance
        // else: wait for player choice

    choose(index):
        choice = current_node.choices[index]
        for effect in choice.effects:
            effect()
        history.append(current_node)
        current_node = choice.next_node
```

### Rich Text & Inline Effects

Support inline formatting tags for game-specific effects:

```
"The [color=red]dragon[/color] dropped a [item=sword_of_fire]Sword of Fire[/item]!"
"Press [input=interact] to pick it up."
"[shake]EARTHQUAKE![/shake] [wave]The ground trembles...[/wave]"
```

| Tag | Effect |
|-----|--------|
| `[color=X]` | Change text color |
| `[item=id]` | Clickable item link with tooltip |
| `[input=action]` | Shows correct button glyph for current input device |
| `[shake]` | Text shakes in place |
| `[wave]` | Characters oscillate vertically |
| `[pause=N]` | Typewriter pauses for N seconds |

---

## 9. Theming & Visual States

### Control States

Every interactive control has visual states:

| State | Trigger | Visual Change |
|-------|---------|---------------|
| **Normal** | Default | Base appearance |
| **Hovered** | Cursor/focus enters | Slight brighten, outline, scale up 2-5% |
| **Pressed** | Click/tap held | Darken, scale down slightly, inset shadow |
| **Focused** | Keyboard/gamepad selected | Visible focus ring, glow, or outline |
| **Disabled** | Control cannot be used | Desaturated, reduced opacity (40-60%) |
| **Selected** | Toggle is on, tab is active | Accent color, underline, checkmark |

State transitions should be animated (100-200ms tween) rather than instant for polished feel.

### Theme System

Define all visual properties centrally for consistency and easy swapping:

```
Theme:
    colors:
        primary: #4A90D9
        secondary: #7B8D9E
        accent: #FFD700
        background: #1A1A2E
        surface: #2A2A3E
        text: #FFFFFF
        text_dim: #AAAACC
        danger: #FF4444
        success: #44FF44

    fonts:
        heading: "GameFont-Bold", 24px
        body: "GameFont-Regular", 16px
        small: "GameFont-Regular", 12px
        monospace: "GameFont-Mono", 14px

    spacing:
        xs: 4px
        sm: 8px
        md: 16px
        lg: 24px
        xl: 32px

    borders:
        radius: 4px
        width: 2px
        color: primary

    button:
        normal: {bg: surface, text: text, border: primary}
        hovered: {bg: primary, text: text, border: accent}
        pressed: {bg: darken(primary, 20%), text: text}
        disabled: {bg: surface, text: text_dim, opacity: 0.5}
```

### Pixel-Art UI Theming

For pixel-art games, theming has additional constraints:

- Use **9-slice/9-patch** sprites for panels and buttons (scales without distorting corners)
- All sizes must be integer multiples of the pixel scale (no sub-pixel offsets)
- Font sizes must match the pixel grid (bitmap fonts at native resolution)
- Disable anti-aliasing on UI textures
- Hover/press states: swap entire sprite rather than tinting (tinting blurs pixel art)

---

## 10. Data Binding & Reactive UI

### The Synchronization Problem

Game state changes constantly (HP, ammo, score, quest progress). Without a systematic approach, UI code becomes scattered `update_health_bar()` calls throughout the codebase.

### Observer Pattern (Event-Driven)

The game emits events when values change; UI elements subscribe:

```
class ObservableValue:
    _value: any
    _listeners: list of func

    get value(): return _value
    set value(new_val):
        if _value != new_val:
            old = _value
            _value = new_val
            for listener in _listeners:
                listener(new_val, old)

    subscribe(callback): _listeners.append(callback)
    unsubscribe(callback): _listeners.remove(callback)

// Usage
player.hp = ObservableValue(100)
health_bar.fill = player.hp.value / player.max_hp
player.hp.subscribe((new_hp, old_hp) => {
    health_bar.fill = new_hp / player.max_hp
    if new_hp < old_hp:
        health_bar.flash_red()
})
```

**Critical:** Always unsubscribe when the UI element is destroyed, or you get dangling references and memory leaks.

### Polling Pattern (Frame-Driven)

Simpler alternative: UI reads game state every frame. No event wiring needed.

```
class HealthBarUI:
    update():
        health_bar.fill = player.hp / player.max_hp
        ammo_label.text = f"{weapon.ammo}/{weapon.max_ammo}"
```

**Trade-off:** Polling is simpler and can't have dangling listener bugs, but it checks values that rarely change (wasteful) and can't detect the *moment* of change (can't trigger flash-on-damage without comparing to last frame).

### Recommended Hybrid

Use events for values that trigger visual effects (HP, ammo, score — flash, shake, animate). Use polling for values that are displayed passively (FPS counter, timer, coordinates).

---

## 11. Input Handling & Navigation

### Multi-Input Support

Game UI must handle three input methods simultaneously:

| Method | Primary Action | Navigate | Scroll |
|--------|---------------|----------|--------|
| **Mouse** | Click | Hover auto-focuses | Scroll wheel |
| **Keyboard** | Enter/Space | Tab / Arrow keys | Page Up/Down |
| **Gamepad** | A / Cross button | D-pad / Left stick | Right stick |

The UI must respond correctly regardless of which input was used last. Display button prompts matching the most recently used device.

### Focus System

Keyboard and gamepad navigation requires a focus system:

```
FocusManager:
    focused: UIElement or null
    focus_group: list of UIElement  // elements in tab order

    navigate(direction):
        if direction == NEXT:
            focused = next_in_group(focused)
        elif direction == PREV:
            focused = prev_in_group(focused)
        elif direction in [UP, DOWN, LEFT, RIGHT]:
            focused = spatial_nearest(focused, direction)
        
        focused.on_focus_enter()

    activate():
        if focused != null:
            focused.on_activated()  // same as click
```

### Spatial Navigation

For grid-based UI (inventory, skill tree), spatial navigation finds the nearest focusable element in the pressed direction:

```
function spatial_nearest(from, direction):
    candidates = all_focusable_elements.filter(e => e != from and e.visible)
    // Filter to elements in the correct direction
    if direction == RIGHT:
        candidates = candidates.filter(e => e.center.x > from.center.x)
    // Score by alignment (prefer same row) and distance
    best = candidates.min_by(e =>
        distance(from.center, e.center) + 
        perpendicular_offset(from.center, e.center, direction) * 2.0
    )
    return best
```

### Input Action Glyph Mapping

Show the correct button icon based on current input device:

```
GlyphMap:
    keyboard: {"interact": "E", "jump": "Space", "inventory": "I"}
    gamepad_xbox: {"interact": "X_Button_A.png", "jump": "X_Button_A.png", "inventory": "X_Button_Y.png"}
    gamepad_ps: {"interact": "PS_Cross.png", "jump": "PS_Cross.png", "inventory": "PS_Triangle.png"}

    get_glyph(action, device_type):
        return GlyphMap[device_type][action]
```

---

## 12. Tooltips, Popups & Contextual UI

### Tooltip System

```
TooltipManager:
    hover_timer: float = 0
    delay: float = 0.5  // seconds before showing
    active_tooltip: UIElement or null

    on_hover_start(target):
        hover_timer = 0
        pending_target = target

    on_hover_tick(delta):
        hover_timer += delta
        if hover_timer >= delay and active_tooltip == null:
            active_tooltip = create_tooltip(pending_target.tooltip_data)
            position_tooltip(active_tooltip, pending_target.rect)

    on_hover_end():
        if active_tooltip:
            destroy(active_tooltip)
            active_tooltip = null
        hover_timer = 0
```

### Contextual Menus (Right-Click)

```
function show_context_menu(position, options):
    menu = create_vertical_stack()
    for option in options:
        if option.condition == null or option.condition():
            btn = create_button(option.label)
            btn.on_click = () => { option.action(); close_menu(menu) }
            menu.add_child(btn)
    menu.position = clamp_to_screen(position, menu.size)
    overlay_layer.add_child(menu)
    // Close on click outside
    set_click_outside_handler(() => close_menu(menu))
```

### Confirmation Dialog Pattern

```
function show_confirmation(title, message, on_confirm, on_cancel):
    dialog = create_modal_panel()
    dialog.add(create_label(title, font=heading))
    dialog.add(create_label(message, font=body))
    
    row = create_horizontal_stack(spacing=16)
    confirm_btn = create_button("Confirm")
    confirm_btn.on_click = () => { on_confirm(); close(dialog) }
    cancel_btn = create_button("Cancel")
    cancel_btn.on_click = () => { on_cancel(); close(dialog) }
    row.add_children([cancel_btn, confirm_btn])
    // Focus Cancel by default — destructive actions shouldn't be one-button-press away
    cancel_btn.grab_focus()
    
    dialog.add(row)
    show_modal(dialog)
```

### Notification Toast System

```
NotificationManager:
    active_toasts: list
    max_visible: int = 3
    default_duration: float = 3.0

    show(message, icon=null, duration=default_duration):
        toast = create_toast(message, icon)
        toast.slide_in_from(RIGHT, duration=0.3)
        toast.auto_dismiss_after(duration, fade_out=0.5)
        active_toasts.prepend(toast)
        // Shift existing toasts down
        for i, t in active_toasts:
            t.animate_y_to(base_y + i * (toast_height + spacing))
        // Remove excess
        while active_toasts.length > max_visible:
            active_toasts.pop().fade_out(0.2)
```

---

## 13. Animation & Transitions

### Common UI Animations

| Animation | Property | Use Case | Duration |
|-----------|----------|----------|----------|
| **Fade** | Opacity 0→1 or 1→0 | Show/hide panels, toasts | 200-400ms |
| **Scale pop** | Scale 0.8→1.0 | Button press, item acquired | 150-250ms |
| **Slide** | Position offset→final | Panel open, list item enter | 200-400ms |
| **Bounce** | Scale overshoot then settle | New item, level up, reward | 300-500ms |
| **Shake** | Random position offset | Error, invalid action, damage | 200-400ms |
| **Pulse** | Scale 1.0→1.05→1.0 loop | Attention, ready state, timer low | 1000ms loop |
| **Color flash** | Tint white→normal | Damage taken, item used | 100-200ms |

### Easing Functions

Linear animation feels robotic. Use easing for natural motion:

| Easing | Character | Best For |
|--------|-----------|----------|
| **Ease-Out** | Fast start, slow end | Elements arriving (panels opening) |
| **Ease-In** | Slow start, fast end | Elements leaving (panels closing) |
| **Ease-In-Out** | Slow start and end | Position transitions, focus movement |
| **Ease-Out-Back** | Overshoots then settles | Bouncy feel, playful UI |
| **Ease-Out-Elastic** | Spring bounce | Rewards, celebrations, power-ups |

```
// Exponential ease-out (framerate-independent)
function smooth_approach(current, target, speed, delta):
    return current + (target - current) * (1 - exp(-speed * delta))
```

### Staggered Entry

When multiple elements appear at once (menu items, grid cells), stagger their entrance:

```
function stagger_entrance(elements, delay_per_item=0.05):
    for i, element in elements:
        element.opacity = 0
        element.offset_y = 20
        schedule(i * delay_per_item, () => {
            tween(element.opacity, 0, 1, duration=0.2)
            tween(element.offset_y, 20, 0, duration=0.3, ease=ease_out)
        })
```

This creates a cascading "waterfall" effect that feels polished and guides the eye through the content.

---

## 14. Localization

### Text Externalization

Never hardcode strings. Store all player-facing text in lookup tables:

```
// en.json
{ "menu.start": "Start Game", "menu.options": "Options", "hud.health": "HP" }

// ja.json
{ "menu.start": "ゲームスタート", "menu.options": "オプション", "hud.health": "HP" }

// Usage
label.text = localize("menu.start")  // returns "Start Game" or "ゲームスタート"
```

### Localization Challenges for Games

| Challenge | Solution |
|-----------|----------|
| **Text length varies** (German ~30% longer than English) | Use auto-sizing containers, test with pseudolocalization |
| **Font coverage** (CJK needs thousands of glyphs) | Use SDF fonts with fallback chains, or separate bitmap font sets |
| **Pluralization** (`"1 item"` vs `"2 items"`) | Use ICU plural rules or a localization library |
| **Right-to-left languages** (Arabic, Hebrew) | Mirror entire UI layout; use bidirectional text rendering |
| **Dynamic values** (`"Dealt {damage} damage"`) | Use parameterized strings, not string concatenation |
| **Context-dependent text** (verb gender agreement) | Provide context keys to translators |

### Pseudolocalization

Test localization readiness without real translations by transforming English text:

```
"Start Game" → "[Šţåŕţ Ĝåɱé___]"
// Accented chars reveal unlocalized strings (still readable)
// Padding reveals truncation bugs (30% longer)
// Brackets reveal concatenated strings (if bracket is split, it's concatenated)
```

---

## 15. Accessibility

### Essential Game UI Accessibility

| Feature | Implementation | Impact |
|---------|---------------|--------|
| **Text scaling** | Support 100%-200% UI scale independent of game resolution | Vision impaired |
| **High contrast mode** | Increase contrast ratios, add outlines to text over busy backgrounds | Low vision |
| **Colorblind support** | Don't rely on color alone; add icons, patterns, labels | ~8% of males |
| **Screen reader support** | Provide text descriptions for all UI elements | Blind players |
| **Remappable controls** | All UI actions remappable including navigation | Motor impaired |
| **Hold-to-toggle** | Convert button holds to toggle press | Motor impaired |
| **Subtitle options** | Size, background, speaker names, directional indicator | Deaf/HoH |
| **Reduce motion** | Option to disable UI animations, screen shake | Vestibular |

### Color Contrast Guidelines

For text readability:
- **Normal text:** Minimum 4.5:1 contrast ratio against background
- **Large text (>18px):** Minimum 3:1 contrast ratio
- **Interactive elements:** Minimum 3:1 contrast against adjacent colors
- **Never use red/green as the only differentiator** (most common colorblindness)

### Minimum Touch/Click Targets

- **Desktop mouse:** 24×24px minimum, 32×32px recommended
- **Mobile touch:** 44×44pt minimum (Apple HIG), 48×48dp (Material Design)
- **Gamepad (focus highlight):** Ensure the focused element is clearly distinguishable (high-contrast outline or glow, not just a subtle color shift)

---

## 16. Resolution & Scaling

### The Resolution Problem

Games run on screens from 1280×720 (Switch handheld) to 3840×2160 (4K TV). UI must work across all of them.

### Scaling Strategies

| Strategy | How It Works | Best For |
|----------|-------------|----------|
| **Virtual resolution** | Render UI at a fixed resolution (e.g., 1920×1080), scale the final image | Most games — simple, predictable |
| **DPI-aware layout** | Define sizes in density-independent units, multiply by device scale factor | High-DPI displays, mobile |
| **Responsive layout** | Reflow layout based on available space (like CSS media queries) | Multi-platform releases |

### Safe Area

On consoles and phones, the screen edges may be occluded:
- **TV overscan:** 5-10% of edges may be invisible on older TVs (consoles require safe area margins)
- **Mobile notches/cutouts:** Status bar, camera notch, home indicator
- **Ultrawide monitors:** UI in extreme corners requires head/eye movement

**Rule:** Place all critical UI within the safe area. Non-critical elements (decorative borders, ambiance) can extend to full screen.

### Pixel-Perfect UI

For pixel-art games:
1. Render game at native pixel resolution (e.g., 320×180)
2. Render UI at the SAME native resolution OR at a higher resolution with integer scaling
3. Scale the final image to screen using nearest-neighbor filtering (no blur)
4. Ensure all UI positions and sizes are integer values at the native resolution

---

## 17. Performance

### Rendering Cost

| Technique | Impact |
|-----------|--------|
| **Texture atlases** | Batch all UI sprites into one texture → 1 draw call for simple HUDs |
| **Text caching** | Cache rendered text glyphs/bitmaps; re-render only when text changes |
| **Dirty flag system** | Only re-layout and re-render subtrees that actually changed |
| **Visibility culling** | Skip layout and render for off-screen or hidden elements (scrolled out of view) |
| **Virtualized lists** | For 100+ items (inventory, leaderboard): only render visible rows + buffer |

### Layout Cost

The measure + arrange passes walk the entire visible tree. Minimize unnecessary recalculations:

- **Avoid layout in the update loop:** Don't set `.width`, `.text`, or other layout-affecting properties every frame unless the value actually changed
- **Batch property changes:** Set multiple properties, then trigger one layout pass (not one per change)
- **Use fixed sizes** where possible: `fixed 200px` is cheaper than `fit to content` because it skips the measure pass for that subtree

### Virtualized Scrolling

For long lists (inventory with 500 items, leaderboard with 1000 rows), render only the visible portion:

```
class VirtualizedList:
    items: list           // all data (lightweight)
    visible_widgets: list // only ~15-20 UI elements, recycled
    scroll_offset: float
    item_height: float

    update():
        first_visible = floor(scroll_offset / item_height)
        last_visible = first_visible + ceil(viewport_height / item_height) + 1

        for i, widget in visible_widgets:
            data_index = first_visible + i
            if data_index < items.length:
                widget.visible = true
                widget.position_y = data_index * item_height - scroll_offset
                widget.bind_to(items[data_index])  // reuse widget with new data
            else:
                widget.visible = false
```

---

## 18. Anti-Patterns

### 1. Spaghetti Updates

**Wrong:** `deal_damage()` calls `update_health_bar()` calls `flash_screen()` calls `play_sound()` — UI logic scattered across gameplay code.

**Right:** Gameplay emits events. UI subscribes independently. Neither knows about the other.

### 2. Layout Every Frame

**Wrong:** Setting `label.text = score` every frame even when the score hasn't changed, causing full text re-render and layout pass.

**Right:** Only update UI when the underlying value actually changes (dirty flag or event-driven).

### 3. Deep Nesting for Positioning

**Wrong:** Wrapping an element in 5 empty containers just to get the right position.

**Right:** Use anchoring, margins, and padding. If the layout system can't express what you need, it might be the wrong layout system.

### 4. Hardcoded Screen Positions

**Wrong:** `health_bar.position = (50, 680)` — breaks at different resolutions.

**Right:** `health_bar.anchor = BOTTOM_LEFT; health_bar.margin = (50, 40)` — works at any resolution.

### 5. Ignoring Gamepad Navigation

**Wrong:** Building the entire UI for mouse, then bolting on gamepad support as an afterthought.

**Right:** Design for the most constrained input first (gamepad: no hover, no cursor, no precise pointing). Mouse support comes free.

### 6. One Giant UI Scene

**Wrong:** Every menu, popup, and overlay in a single monolithic scene/file.

**Right:** Each screen/panel is its own scene/class. Compose them at runtime via the screen manager.

### 7. No Transition on State Change

**Wrong:** Elements snap in and out instantly — feels cheap and jarring.

**Right:** 200ms ease-out fade/slide costs almost nothing and dramatically improves perceived quality.

---

## 19. UI Architecture Decision Framework

```
What kind of UI do you need?
├── Debug/dev tools only?
│   └── Immediate mode (simplest, rebuild every frame)
├── Simple HUD (< 10 elements)?
│   └── Direct draw (sprites + text, no framework)
├── Complex game UI (menus, inventory, settings)?
│   ├── Does your engine have a built-in UI system?
│   │   ├── Yes → Use it (Godot Control nodes, Unity UGUI/UI Toolkit)
│   │   └── No → Integrate a retained-mode library
│   ├── Pixel art game?
│   │   └── 9-slice sprites, integer positioning, bitmap fonts
│   └── Need to ship on consoles?
│       └── Build gamepad navigation from day one
└── Editor/tool UI?
    └── Immediate mode (ImGui or equivalent)
```

---

## Related Engine Guides

- **MonoGame:** [G5 UI Framework (Gum)](../../monogame-arch/guides/G5_ui_framework.md) — retained-mode UI with Gum.MonoGame
- **MonoGame:** [G55 Settings Menu](../../monogame-arch/guides/G55_settings_menu.md) — practical settings screen implementation
- **MonoGame:** [G61 Tutorial & Onboarding](../../monogame-arch/guides/G61_tutorial_onboarding.md) — tutorial UI patterns
- **Stitch Prototyping:** [G_stitch_ui_workflow](../game-design/G_stitch_ui_workflow.md) — AI-assisted UI design pipeline
- **Core Theory:** [input-handling-theory](./input-handling-theory.md) — input fundamentals for UI navigation
- **Core Theory:** [animation-theory](./animation-theory.md) — animation principles for UI motion
- **Core Theory:** [scene-management-theory](./scene-management-theory.md) — screen stack architecture

---

*All pseudocode is engine-agnostic. See engine-specific modules for implementations using Godot Control nodes, MonoGame Gum, Unity UI Toolkit, or Bevy bevy_ui.*
