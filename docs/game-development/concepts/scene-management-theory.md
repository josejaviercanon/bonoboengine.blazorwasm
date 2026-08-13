# Scene Management -- Theory & Concepts

This document covers engine-agnostic scene and game state management theory. For engine-specific implementations, see the relevant engine module.

---

## What is a Scene?

A **scene** is the primary organizational unit in a game. Each scene encapsulates its own game state, loaded resources, and logic. Think of scenes as self-contained slices of your game: a main menu is a scene, gameplay is a scene, the pause overlay is a scene.

### What a Scene Typically Owns

| Concern | Owned by Scene? |
|---------|----------------|
| Game world / entity state | Yes -- created on init, destroyed on unload |
| Systems / logic | Yes -- registered per scene type |
| Assets (textures, sounds) | Yes -- loaded on enter, released on exit |
| Camera / viewport | Yes |
| Shared services (audio, input, settings) | No -- injected, shared across all scenes |

---

## Scene Lifecycle

Every scene follows a lifecycle mirroring the game's own:

```
Initialize  -->  LoadContent  -->  [Update / Draw loop]  -->  UnloadContent
```

Additional hooks:

- **OnEnter** -- called when this scene becomes the active (top) scene
- **OnExit** -- called when another scene is pushed on top, or this scene is popped

---

## Scene Stack Model

Scenes are managed as a **stack**. The topmost scene is the active one that receives input. Scenes below may or may not receive update/draw calls depending on configuration.

```
+---------------------+  <-- Top (active, receives input)
|   PauseScene        |     IsTransparent = true
+---------------------+
|   GameplayScene     |     Drawn because PauseScene is transparent
+---------------------+
|   (earlier scenes)  |     Not drawn -- GameplayScene is opaque
+---------------------+
```

### Key Properties

- **IsTransparent** -- if true, scenes below this one still receive Draw calls (e.g., pause overlay shows gameplay behind it)
- **AllowUpdateBelow** -- if true, scenes below this one still receive Update calls (e.g., a dialogue overlay while the world keeps animating)

---

## Scene Manager Operations

| Operation | Behavior |
|-----------|----------|
| **ChangeScene(next)** | Clear the entire stack, load the new scene |
| **PushScene(overlay)** | Push a scene on top without removing the current one |
| **PopScene()** | Remove the top scene, return to the one below |

### Deferred Operations

Scene changes should be queued and applied between frames to prevent stack mutation during iteration. Queue the operation during Update, flush the queue at the start of the next frame.

### Visibility Walk (Draw)

Walk down from the top of the stack until you find an opaque scene. Then draw bottom-up so overlays paint on top.

### Update Walk

Only the top scene updates by default. Walk down and continue updating only while each scene's AllowUpdateBelow flag is true.

---

## Game State Machine

A finite state machine maps high-level game states to scene instances. This gives a declarative picture of the flow.

### Typical Flow

```
Splash --(timer/click)--> MainMenu
MainMenu --(Play)-------> Gameplay
Gameplay --(Esc)---------> Pause        (push overlay)
Pause --(Resume)---------> Gameplay     (pop overlay)
Gameplay --(HP <= 0)-----> GameOver
GameOver --(Retry)-------> Gameplay
GameOver --(Menu)--------> MainMenu
MainMenu --(Credits)-----> Credits
```

### State-to-Scene Mapping

Register factory functions that create scene instances for each state. This centralizes scene creation and allows dependency injection.

---

## Scene Transitions

Transitions animate between two scenes. The lifecycle is:

1. **Freeze** the current scene (capture its last frame)
2. **Animate out** (fade to black, slide off-screen, dissolve, etc.)
3. **Swap** -- unload old scene, load new scene
4. **Animate in** (fade from black, slide in, etc.)

### Common Transition Types

- **Fade to black** -- simple, universally appropriate
- **Crossfade** -- old scene fades out while new scene fades in
- **Slide/wipe** -- one scene slides off while the next slides in
- **Dissolve** -- pixel-level dissolve pattern

The transition object receives both the old and new scenes, drives timing, and calls a swap callback at the midpoint.

---

## Loading Screens

For scenes with heavy asset loading:

1. Show a loading scene (simple spinner or progress bar)
2. Load assets asynchronously or across multiple frames
3. When loading completes, transition to the actual scene

**Track progress:** Count total assets and report percentage. Distribute loading across frames to avoid freezing.

---

## Pause System

Pausing is typically implemented as a scene pushed onto the stack:

- The pause scene is **transparent** (gameplay renders behind it)
- The pause scene does **not** allow update below (gameplay logic freezes)
- Time scale can be set to zero for the gameplay scene
- A semi-transparent overlay darkens the game behind the pause menu

### What Should Not Pause

- Audio fade-outs that are in progress
- Input handling for the pause menu itself
- Particle effects that are purely decorative (optional)

---

## Overlay Scenes

Overlays are scenes that sit on top of the main scene without replacing it:

- **Pause menu** -- transparent, blocks update below
- **Inventory screen** -- may or may not block update
- **Dialogue box** -- transparent, may allow limited update below
- **Debug overlay** -- transparent, allows full update below

Each overlay controls its own transparency and update-blocking behavior.

---

## Scene Communication

Scenes need to pass data to each other (e.g., which level to load, player stats from game over):

- **Context object** -- a data class passed when creating a scene (e.g., level number, score)
- **Shared services** -- singleton services accessible by all scenes (audio manager, save system)
- **Event bus** -- publish/subscribe system for loose coupling between scenes

Avoid direct scene-to-scene references. Use the state machine or context objects to pass information.

---

## Composable Scene Subsystems

Large scenes benefit from decomposition into subsystems that can be mixed and matched:

- **Camera subsystem**
- **Input subsystem**
- **HUD subsystem**
- **Particle subsystem**

Each subsystem follows the same lifecycle (init, load, update, draw, unload) and is owned by its parent scene.

---

*For MonoGame implementation, see [G38 — Scene Management](../../monogame-arch/guides/G38_scene_management.md). Implementation examples for other engines are available in their respective modules.*
