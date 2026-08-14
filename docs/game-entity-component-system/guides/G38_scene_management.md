# G38 — Scene & Game State Management



> **Category:** Guide · **Related:** [G12 Design Patterns](./G12_design_patterns.md) · [G15 Game Loop](./G15_game_loop.md) · [G42 Screen Transitions](./G42_screen_transitions.md) · [G26 Resource Loading & Caching](./G26_resource_loading_caching.md)

---

## Table of Contents

1. [Scene Architecture](#1-scene-architecture)
2. [Scene Manager](#2-scene-manager)
3. [Game State Machine](#3-game-state-machine)
4. [Scene Transitions](#4-scene-transitions)
5. [ECS World Per Scene](#5-ecs-world-per-scene)
6. [Loading Screens](#6-loading-screens)
7. [Scene Communication](#7-scene-communication)
8. [Pause System](#8-pause-system)
9. [Overlay Scenes](#9-overlay-scenes)
10. [Practical Example](#10-practical-example)
11. [Composable Scene Subsystems](#11-composable-scene-subsystems)

---

## 1. Scene Architecture

A **Scene** is the primary organizational unit in a MonoGame game. Each scene encapsulates
its own Arch ECS `World`, registered systems, loaded content, and runtime state. Think of
scenes as self-contained slices of your game: a main menu is a scene, gameplay is a scene,
the pause overlay is a scene.

### What a Scene Owns

| Concern | Owned By Scene |
|---------|---------------|
| ECS World | Yes — created on `Initialize`, destroyed on `UnloadContent` |
| Systems | Yes — registered per scene type |
| Content (textures, sounds) | Yes — loaded in `LoadContent`, released in `UnloadContent` |
| Camera / viewport | Yes |
| Shared services (audio, input) | No — injected via `GameServices` |

### Base Scene Class

```csharp
using Arch.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

/// <summary>
/// Base class for all scenes. Mirrors MonoGame's own lifecycle
/// (Initialize → LoadContent → Update/Draw loop → UnloadContent).
/// </summary>
public abstract class Scene
{
    // ── Injected references ──────────────────────────────────────
    public GameServices Services { get; internal set; } = null!;
    public ContentManager Content { get; private set; } = null!;
    public GraphicsDevice GraphicsDevice => Services.GraphicsDevice;

    // ── ECS ──────────────────────────────────────────────────────
    public World World { get; private set; } = null!;

    // ── State flags ──────────────────────────────────────────────
    public bool IsInitialized { get; private set; }
    public bool IsContentLoaded { get; private set; }

    /// <summary>If true, scenes below this one on the stack still receive Draw calls.</summary>
    public virtual bool IsTransparent => false;

    /// <summary>If true, scenes below this one on the stack still receive Update calls.</summary>
    public virtual bool AllowUpdateBelow => false;

    // ── Lifecycle ────────────────────────────────────────────────

    public void InternalInitialize(GameServices services)
    {
        Services = services;
        Content = new ContentManager(services.ContentServiceProvider, "Content");
        World = World.Create();
        IsInitialized = true;
        Initialize();
    }

    /// <summary>Register ECS systems, set up initial state.</summary>
    protected virtual void Initialize() { }

    public void InternalLoadContent()
    {
        LoadContent();
        IsContentLoaded = true;
    }

    /// <summary>Load textures, fonts, sounds via <see cref="Content"/>.</summary>
    protected virtual void LoadContent() { }

    /// <summary>Called every frame when this scene is the active (top) scene,
    /// or when a scene above it has <see cref="AllowUpdateBelow"/> set.</summary>
    public virtual void Update(GameTime gameTime) { }

    /// <summary>Called every frame when visible (not occluded by a non-transparent scene).</summary>
    public virtual void Draw(GameTime gameTime, SpriteBatch spriteBatch) { }

    public void InternalUnloadContent()
    {
        UnloadContent();
        World.Dispose();
        Content.Unload();
        IsContentLoaded = false;
        IsInitialized = false;
    }

    /// <summary>Tear down scene-specific resources.</summary>
    protected virtual void UnloadContent() { }

    // ── Hooks for scene manager ──────────────────────────────────

    /// <summary>Called when this scene becomes the top scene (gains focus).</summary>
    public virtual void OnEnter() { }

    /// <summary>Called when another scene is pushed on top or this scene is popped.</summary>
    public virtual void OnExit() { }
}
```

### GameServices — Shared Across All Scenes

```csharp
/// <summary>
/// Container for services that live for the entire application lifetime.
/// Passed into every Scene so they can access hardware, audio, input, etc.
/// </summary>
public sealed class GameServices
{
    public GraphicsDevice GraphicsDevice { get; init; } = null!;
    public IServiceProvider ContentServiceProvider { get; init; } = null!;
    public AudioManager Audio { get; init; } = null!;
    public InputManager Input { get; init; } = null!;
    public SettingsManager Settings { get; init; } = null!;

    // Add more as your engine grows.
}
```

The `Scene` class deliberately mirrors MonoGame's `Game` lifecycle so the mental model
stays consistent. Each scene is a mini-game inside the larger application.

---

## 2. Scene Manager

The **SceneManager** owns the scene stack, drives lifecycle calls, and coordinates
transitions. It lives as a singleton-style service on your main `Game` class.

### Scene Stack Model

```
┌─────────────────────┐  ← Top (active, receives input)
│   PauseScene        │     IsTransparent = true
├─────────────────────┤
│   GameplayScene     │     Drawn because PauseScene is transparent
├─────────────────────┤
│   (earlier scenes   │     Not drawn — GameplayScene is opaque
│    already popped)  │
└─────────────────────┘
```

### SceneManager Implementation

```csharp
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

public sealed class SceneManager
{
    private readonly List<Scene> _sceneStack = new();
    private readonly GameServices _services;

    // Pending operations (applied between frames to avoid mutation during iteration)
    private readonly Queue<Action> _pendingOps = new();

    // Transition support
    private SceneTransition? _activeTransition;

    public SceneManager(GameServices services)
    {
        _services = services;
    }

    /// <summary>The topmost (active) scene, or null if the stack is empty.</summary>
    public Scene? ActiveScene => _sceneStack.Count > 0
        ? _sceneStack[^1]
        : null;

    // ── Public API ───────────────────────────────────────────────

    /// <summary>Replace the entire stack with a single new scene.</summary>
    public void ChangeScene(Scene next, SceneTransition? transition = null)
    {
        _pendingOps.Enqueue(() => DoChangeScene(next, transition));
    }

    /// <summary>Push a scene on top (e.g. pause overlay).</summary>
    public void PushScene(Scene scene)
    {
        _pendingOps.Enqueue(() => DoPushScene(scene));
    }

    /// <summary>Pop the top scene off the stack.</summary>
    public void PopScene()
    {
        _pendingOps.Enqueue(DoPopScene);
    }

    // ── Frame hooks (called from Game.Update / Game.Draw) ────────

    public void Update(GameTime gameTime)
    {
        // Apply any queued operations first.
        FlushPendingOps();

        // If a transition is running, update it instead of normal scene logic.
        if (_activeTransition is not null)
        {
            _activeTransition.Update(gameTime);
            if (_activeTransition.IsComplete)
                _activeTransition = null;
            return;
        }

        // Walk the stack top-down; stop when a scene doesn't AllowUpdateBelow.
        for (int i = _sceneStack.Count - 1; i >= 0; i--)
        {
            _sceneStack[i].Update(gameTime);
            if (!_sceneStack[i].AllowUpdateBelow)
                break;
        }
    }

    public void Draw(GameTime gameTime, SpriteBatch spriteBatch)
    {
        if (_activeTransition is not null)
        {
            _activeTransition.Draw(gameTime, spriteBatch);
            return;
        }

        // Find the lowest visible scene (first opaque scene from top).
        int firstVisible = _sceneStack.Count - 1;
        for (int i = _sceneStack.Count - 1; i >= 0; i--)
        {
            firstVisible = i;
            if (!_sceneStack[i].IsTransparent)
                break;
        }

        // Draw bottom-up so overlays paint on top.
        for (int i = firstVisible; i < _sceneStack.Count; i++)
        {
            _sceneStack[i].Draw(gameTime, spriteBatch);
        }
    }

    // ── Internal operations ──────────────────────────────────────

    private void FlushPendingOps()
    {
        while (_pendingOps.Count > 0)
            _pendingOps.Dequeue().Invoke();
    }

    private void DoChangeScene(Scene next, SceneTransition? transition)
    {
        if (transition is not null)
        {
            Scene? current = ActiveScene;
            _activeTransition = transition;
            transition.Start(current, next, () =>
            {
                // Callback: actually swap when transition says so.
                ClearStack();
                BootScene(next);
            });
        }
        else
        {
            ClearStack();
            BootScene(next);
        }
    }

    private void DoPushScene(Scene scene)
    {
        ActiveScene?.OnExit();
        BootScene(scene);
    }

    private void DoPopScene()
    {
        if (_sceneStack.Count == 0) return;

        var top = _sceneStack[^1];
        top.OnExit();
        top.InternalUnloadContent();
        _sceneStack.RemoveAt(_sceneStack.Count - 1);

        ActiveScene?.OnEnter();
    }

    private void BootScene(Scene scene)
    {
        scene.InternalInitialize(_services);
        scene.InternalLoadContent();
        _sceneStack.Add(scene);
        scene.OnEnter();
    }

    private void ClearStack()
    {
        for (int i = _sceneStack.Count - 1; i >= 0; i--)
        {
            _sceneStack[i].OnExit();
            _sceneStack[i].InternalUnloadContent();
        }
        _sceneStack.Clear();
    }
}
```

### Key Design Decisions

- **Deferred operations**: `ChangeScene` / `PushScene` / `PopScene` queue lambdas applied
  at the start of the next `Update`. This prevents stack mutation while iterating.
- **Visibility walk**: Draw only scenes that are visible — walk down from the top until
  you hit an opaque scene, then draw bottom-up.
- **Update walk**: Only the top scene updates by default. Overlay scenes opt in to
  updating below via `AllowUpdateBelow`.

---

## 3. Game State Machine

A finite state machine (FSM) maps high-level game states to scene instances. This gives
you a declarative picture of the flow rather than ad-hoc `ChangeScene` calls scattered
through your code.

### State Enum

```csharp
public enum GameState
{
    Splash,
    MainMenu,
    Gameplay,
    Pause,
    GameOver,
    Credits
}
```

### State-to-Scene Mapping

```csharp
/// <summary>
/// Maps GameState values to scene factory functions. Keeps scene creation
/// centralized so you can inject dependencies, pass context, etc.
/// </summary>
public sealed class GameStateMachine
{
    private readonly SceneManager _sceneManager;
    private readonly Dictionary<GameState, Func<SceneContext?, Scene>> _factories = new();

    public GameState CurrentState { get; private set; }

    public GameStateMachine(SceneManager sceneManager)
    {
        _sceneManager = sceneManager;
    }

    public void Register(GameState state, Func<SceneContext?, Scene> factory)
    {
        _factories[state] = factory;
    }

    /// <summary>Transition to a new state, replacing the scene stack.</summary>
    public void GoTo(GameState state, SceneContext? ctx = null,
                     SceneTransition? transition = null)
    {
        if (!_factories.TryGetValue(state, out var factory))
            throw new InvalidOperationException($"No factory registered for {state}");

        CurrentState = state;
        _sceneManager.ChangeScene(factory(ctx), transition);
    }

    /// <summary>Push an overlay state (pause, inventory) without clearing the stack.</summary>
    public void PushOverlay(GameState state, SceneContext? ctx = null)
    {
        if (!_factories.TryGetValue(state, out var factory))
            throw new InvalidOperationException($"No factory registered for {state}");

        CurrentState = state;
        _sceneManager.PushScene(factory(ctx));
    }

    /// <summary>Pop the overlay and revert to the previous state.</summary>
    public void PopOverlay(GameState returnState)
    {
        CurrentState = returnState;
        _sceneManager.PopScene();
    }
}
```

### Registration at Startup

```csharp
// In your Game.Initialize():
var gsm = new GameStateMachine(sceneManager);

gsm.Register(GameState.Splash,    _ => new SplashScene());
gsm.Register(GameState.MainMenu,  _ => new MainMenuScene());
gsm.Register(GameState.Gameplay,  ctx => new GameplayScene(ctx));
gsm.Register(GameState.Pause,     _ => new PauseScene());
gsm.Register(GameState.GameOver,  ctx => new GameOverScene(ctx));
gsm.Register(GameState.Credits,   _ => new CreditsScene());

gsm.GoTo(GameState.Splash);
```

### Typical Flow

```
Splash ──(timer/click)──► MainMenu
MainMenu ──(Play)──────► Gameplay
Gameplay ──(Esc)───────► Pause        (push overlay)
Pause ──(Resume)───────► Gameplay     (pop overlay)
Gameplay ──(HP ≤ 0)────► GameOver
GameOver ──(Retry)─────► Gameplay
GameOver ──(Menu)──────► MainMenu
MainMenu ──(Credits)───► Credits
Credits ──(Back)───────► MainMenu
```

---

## 4. Scene Transitions

Transitions animate between two scenes. The lifecycle is:

1. **Freeze** the current scene (capture its last frame to a `RenderTarget2D`).
2. **Animate out** (fade to black, slide off-screen, dissolve, etc.).
3. **Swap** — unload old scene, load new scene.
4. **Animate in** (fade from black, slide in, etc.).

> Full transition effect catalog: see [G42 Screen Transitions](./G42_screen_transitions.md).

### Transition Base Class

```csharp
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

public abstract class SceneTransition
{
    protected Scene? OldScene { get; private set; }
    protected Scene? NewScene { get; private set; }
    private Action? _swapCallback;

    public bool IsComplete { get; protected set; }
    protected bool HasSwapped { get; private set; }

    public void Start(Scene? oldScene, Scene? newScene, Action swapCallback)
    {
        OldScene = oldScene;
        NewScene = newScene;
        _swapCallback = swapCallback;
        OnStart();
    }

    protected virtual void OnStart() { }

    public abstract void Update(GameTime gameTime);
    public abstract void Draw(GameTime gameTime, SpriteBatch spriteBatch);

    /// <summary>Call this at the midpoint to actually perform the scene swap.</summary>
    protected void PerformSwap()
    {
        if (HasSwapped) return;
        HasSwapped = true;
        _swapCallback?.Invoke();
    }
}
```

### Fade-to-Black Transition

```csharp
public sealed class FadeTransition : SceneTransition
{
    private readonly float _duration;
    private float _elapsed;
    private float _alpha;
    private RenderTarget2D? _frozenFrame;

    public FadeTransition(float durationSeconds = 0.5f)
    {
        _duration = durationSeconds;
    }

    protected override void OnStart()
    {
        // Capture the old scene's last frame.
        if (OldScene is not null)
        {
            var gd = OldScene.GraphicsDevice;
            _frozenFrame = new RenderTarget2D(gd, gd.Viewport.Width, gd.Viewport.Height);
        }
    }

    public override void Update(GameTime gameTime)
    {
        _elapsed += (float)gameTime.ElapsedGameTime.TotalSeconds;
        float half = _duration / 2f;

        if (_elapsed < half)
        {
            // Phase 1: fade out (old scene → black)
            _alpha = _elapsed / half;
        }
        else
        {
            if (!HasSwapped)
                PerformSwap();

            // Phase 2: fade in (black → new scene)
            _alpha = 1f - ((_elapsed - half) / half);
        }

        if (_elapsed >= _duration)
        {
            _alpha = 0f;
            IsComplete = true;
        }
    }

    public override void Draw(GameTime gameTime, SpriteBatch spriteBatch)
    {
        var gd = (OldScene ?? NewScene)!.GraphicsDevice;

        // Draw the appropriate scene underneath.
        if (!HasSwapped && OldScene is not null)
            OldScene.Draw(gameTime, spriteBatch);
        else if (HasSwapped && NewScene is not null)
            NewScene.Draw(gameTime, spriteBatch);

        // Draw the fade overlay.
        spriteBatch.Begin();
        var overlay = new Texture2D(gd, 1, 1);
        overlay.SetData(new[] { Color.Black });
        spriteBatch.Draw(overlay, gd.Viewport.Bounds,
                         Color.White * MathHelper.Clamp(_alpha, 0f, 1f));
        spriteBatch.End();
        overlay.Dispose();
    }
}
```

### Usage

```csharp
gsm.GoTo(GameState.Gameplay, context, new FadeTransition(0.6f));
```

---

## 5. ECS World Per Scene

Each scene creates its own Arch `World`. This provides clean isolation — entities from the
menu don't leak into gameplay, and disposing a scene destroys all its entities in one shot.

### World Lifecycle

```csharp
protected override void Initialize()
{
    // World is already created by Scene.InternalInitialize().
    // Register systems here.
    _movementSystem = new MovementSystem(World);
    _renderSystem = new RenderSystem(World);
    _collisionSystem = new CollisionSystem(World);
}

protected override void UnloadContent()
{
    // World.Dispose() is called by Scene.InternalUnloadContent().
    // Clean up any non-ECS resources here.
}
```

### ECS Components (Record Structs)

```csharp
public record struct Position(float X, float Y);
public record struct Velocity(float Dx, float Dy);
public record struct Sprite(Texture2D Texture, Rectangle Source);
public record struct Health(int Current, int Max);
public record struct PlayerTag();
```

### System Registration Per Scene Type

Different scene types register different systems. A `GameplayScene` needs physics and AI;
a `MainMenuScene` only needs UI rendering.

```csharp
public sealed class GameplayScene : Scene
{
    private MovementSystem _movement = null!;
    private CollisionSystem _collision = null!;
    private RenderSystem _render = null!;
    private HudSystem _hud = null!;

    protected override void Initialize()
    {
        _movement = new MovementSystem(World);
        _collision = new CollisionSystem(World);
        _render = new RenderSystem(World);
        _hud = new HudSystem(World, Services);
    }

    public override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _movement.Update(dt);
        _collision.Update(dt);
        _hud.Update(dt);
    }

    public override void Draw(GameTime gameTime, SpriteBatch spriteBatch)
    {
        _render.Draw(spriteBatch);
        _hud.Draw(spriteBatch);
    }
}
```

### Shared Services vs. Scene-Local State

| Data | Where It Lives | Why |
|------|---------------|-----|
| Audio manager | `GameServices` | Music continues across scene transitions |
| Input manager | `GameServices` | Input polling is hardware-level |
| Player settings | `GameServices` | Volume, keybinds persist |
| ECS entities | `Scene.World` | Destroyed when scene unloads |
| Loaded textures | `Scene.Content` | Released when scene unloads |
| Score, HP | `SceneContext` | Passed between scenes (see §7) |

---

## 6. Loading Screens

Heavy scenes (large levels, many textures) need async loading with a progress indicator.
The pattern: push a `LoadingScene`, have it spawn a background task that creates the real
scene, then swap when ready.

### Thread-Safe Progress Tracker

```csharp
/// <summary>
/// Tracks loading progress from a background thread.
/// All members are thread-safe via Interlocked.
/// </summary>
public sealed class LoadingProgress
{
    private volatile string _status = "Loading...";
    private int _completed;
    private int _total = 1;

    public string Status => _status;
    public float Fraction => (float)_completed / _total;
    public bool IsDone => _completed >= _total;

    public void SetTotal(int total) => Interlocked.Exchange(ref _total, total);
    public void Increment() => Interlocked.Increment(ref _completed);
    public void SetStatus(string status) => _status = status;
}
```

### LoadingScene

```csharp
public sealed class LoadingScene : Scene
{
    private readonly Func<LoadingProgress, Scene> _factory;
    private readonly SceneTransition? _transition;
    private LoadingProgress _progress = new();
    private Scene? _loadedScene;
    private Task? _loadTask;
    private SpriteFont _font = null!;

    /// <param name="factory">
    /// A function that creates and fully initializes the target scene.
    /// Called on a background thread — do NOT touch GraphicsDevice here.
    /// Defer GPU work to the scene's own LoadContent.
    /// </param>
    public LoadingScene(Func<LoadingProgress, Scene> factory,
                        SceneTransition? transition = null)
    {
        _factory = factory;
        _transition = transition;
    }

    protected override void LoadContent()
    {
        _font = Content.Load<SpriteFont>("Fonts/Default");
    }

    public override void OnEnter()
    {
        _loadTask = Task.Run(() =>
        {
            _loadedScene = _factory(_progress);
        });
    }

    public override void Update(GameTime gameTime)
    {
        if (_loadTask is { IsCompleted: true } && _loadedScene is not null)
        {
            // Scene is built — tell the scene manager to swap.
            Services.SceneManager.ChangeScene(_loadedScene, _transition);
        }
    }

    public override void Draw(GameTime gameTime, SpriteBatch spriteBatch)
    {
        GraphicsDevice.Clear(Color.Black);

        spriteBatch.Begin();
        string text = $"{_progress.Status}  {_progress.Fraction * 100:F0}%";
        var pos = new Vector2(100, GraphicsDevice.Viewport.Height - 80);
        spriteBatch.DrawString(_font, text, pos, Color.White);

        // Progress bar
        var barBg = new Rectangle(100, (int)pos.Y + 30, 400, 16);
        var barFg = barBg with { Width = (int)(barBg.Width * _progress.Fraction) };
        DrawRect(spriteBatch, barBg, Color.Gray * 0.5f);
        DrawRect(spriteBatch, barFg, Color.CornflowerBlue);
        spriteBatch.End();
    }

    private void DrawRect(SpriteBatch sb, Rectangle rect, Color color)
    {
        var pixel = new Texture2D(GraphicsDevice, 1, 1);
        pixel.SetData(new[] { Color.White });
        sb.Draw(pixel, rect, color);
        pixel.Dispose();
    }
}
```

### Kicking Off a Loading Screen

```csharp
var loading = new LoadingScene(progress =>
{
    progress.SetTotal(4);
    progress.SetStatus("Loading tilemap...");
    var tileData = LoadTileMap("level_03"); // CPU-only work
    progress.Increment();

    progress.SetStatus("Parsing entities...");
    var entityDefs = ParseEntities(tileData);
    progress.Increment();

    progress.SetStatus("Building scene...");
    var scene = new GameplayScene(entityDefs, context);
    progress.Increment();

    progress.SetStatus("Done");
    progress.Increment();

    return scene;
}, new FadeTransition(0.3f));

sceneManager.ChangeScene(loading);
```

> **Important:** The background thread must not call any `GraphicsDevice` or `ContentManager`
> methods — those are not thread-safe. Do CPU-only prep (parsing files, building data
> structures), and defer GPU uploads to the scene's `LoadContent` which runs on the main thread.

---

## 7. Scene Communication

Scenes need to pass data: the menu tells gameplay which level to load; gameplay tells
game-over the final score. Use a **SceneContext** object.

### SceneContext

```csharp
/// <summary>
/// Bag of data passed between scenes. Add properties as your game grows.
/// </summary>
public sealed class SceneContext
{
    // ── Level data ───────────────────────────────────────────────
    public int LevelNumber { get; set; } = 1;
    public string? LevelPath { get; set; }

    // ── Player state (carried across levels) ─────────────────────
    public int Score { get; set; }
    public int Lives { get; set; } = 3;
    public int PlayerHealth { get; set; } = 100;

    // ── Inventory / unlocks ──────────────────────────────────────
    public HashSet<string> Unlocked { get; set; } = new();

    // ── Misc ─────────────────────────────────────────────────────
    public TimeSpan PlayTime { get; set; }

    /// <summary>Deep-copy for branching (e.g. "restart level" keeps old score).</summary>
    public SceneContext Clone() => new()
    {
        LevelNumber = LevelNumber,
        LevelPath = LevelPath,
        Score = Score,
        Lives = Lives,
        PlayerHealth = PlayerHealth,
        Unlocked = new HashSet<string>(Unlocked),
        PlayTime = PlayTime,
    };
}
```

### Persistent vs. Scene-Local Data

| Data | Persistent? | Mechanism |
|------|------------|-----------|
| Score, lives | Yes | Stored in `SceneContext`, passed forward |
| Entity positions | No | Live in scene's ECS `World`, destroyed on unload |
| Settings (volume) | Yes | `GameServices.Settings`, saved to disk |
| Loaded textures | No | Scene's `ContentManager`, unloaded on exit |
| Unlocked items | Yes | `SceneContext.Unlocked` or a save file |

### Passing Context Through the State Machine

```csharp
// Gameplay ends — pass score to GameOver:
var ctx = new SceneContext { Score = _currentScore, LevelNumber = _level };
gsm.GoTo(GameState.GameOver, ctx);

// GameOver "Retry" — reuse context:
public sealed class GameOverScene : Scene
{
    private readonly SceneContext? _ctx;

    public GameOverScene(SceneContext? ctx) => _ctx = ctx;

    private void OnRetry()
    {
        // Reset health but keep score progression.
        var retryCtx = _ctx?.Clone() ?? new SceneContext();
        retryCtx.PlayerHealth = 100;
        Services.StateMachine.GoTo(GameState.Gameplay, retryCtx);
    }
}
```

---

## 8. Pause System

Pausing means stopping game logic while keeping UI interactive. Two techniques:

### Time Scale Pattern

```csharp
/// <summary>Global time scaling. Set to 0 to freeze game logic.</summary>
public static class TimeScale
{
    public static float Value { get; set; } = 1f;

    /// <summary>Returns scaled delta time.</summary>
    public static float DeltaTime(GameTime gt) =>
        (float)gt.ElapsedGameTime.TotalSeconds * Value;
}
```

Game systems multiply by `TimeScale.DeltaTime(gt)` instead of raw `ElapsedGameTime`:

```csharp
public override void Update(GameTime gameTime)
{
    float dt = TimeScale.DeltaTime(gameTime);
    if (dt <= 0f) return; // Paused — skip movement, physics, AI.

    _movementSystem.Update(dt);
    _collisionSystem.Update(dt);
    _aiSystem.Update(dt);
}
```

### Overlay Approach (Preferred)

The cleaner pattern is to push a `PauseScene` overlay. Because `GameplayScene` does
**not** set `AllowUpdateBelow = true` and `PauseScene` doesn't either, the gameplay
scene simply stops receiving `Update` calls while the pause menu is on top.

```csharp
public sealed class PauseScene : Scene
{
    public override bool IsTransparent => true;    // Draw gameplay behind us.
    public override bool AllowUpdateBelow => false; // Freeze gameplay updates.

    private SpriteFont _font = null!;

    protected override void LoadContent()
    {
        _font = Content.Load<SpriteFont>("Fonts/Menu");
    }

    public override void Update(GameTime gameTime)
    {
        if (Services.Input.IsJustPressed(Keys.Escape))
        {
            Services.StateMachine.PopOverlay(GameState.Gameplay);
        }
    }

    public override void Draw(GameTime gameTime, SpriteBatch spriteBatch)
    {
        // Semi-transparent darken overlay.
        spriteBatch.Begin();
        var pixel = new Texture2D(GraphicsDevice, 1, 1);
        pixel.SetData(new[] { Color.White });
        spriteBatch.Draw(pixel, GraphicsDevice.Viewport.Bounds, Color.Black * 0.6f);

        string text = "PAUSED — Press ESC to resume";
        var size = _font.MeasureString(text);
        var center = new Vector2(
            (GraphicsDevice.Viewport.Width - size.X) / 2f,
            (GraphicsDevice.Viewport.Height - size.Y) / 2f);
        spriteBatch.DrawString(_font, text, center, Color.White);
        spriteBatch.End();
        pixel.Dispose();
    }
}
```

This approach is superior because:

- No `TimeScale` global to manage and reset.
- The gameplay scene's `Update` is simply never called while paused.
- Drawing the gameplay scene frozen behind the pause menu happens naturally because
  `PauseScene.IsTransparent = true`.

---

## 9. Overlay Scenes

Overlays extend the pause pattern to any scene that renders on top of another: HUDs,
dialogue boxes, inventory screens, debug consoles.

### Overlay Base

```csharp
/// <summary>
/// Convenience base for overlay scenes that draw on top of whatever is below.
/// </summary>
public abstract class OverlayScene : Scene
{
    public override bool IsTransparent => true;

    /// <summary>Override to true if the scene below should keep updating.</summary>
    public override bool AllowUpdateBelow => false;
}
```

### Dialogue Overlay Example

```csharp
public sealed class DialogueOverlayScene : OverlayScene
{
    public override bool AllowUpdateBelow => false; // Freeze gameplay during dialogue.

    private readonly string[] _lines;
    private int _lineIndex;
    private SpriteFont _font = null!;

    public DialogueOverlayScene(string[] lines) => _lines = lines;

    protected override void LoadContent()
    {
        _font = Content.Load<SpriteFont>("Fonts/Dialogue");
    }

    public override void Update(GameTime gameTime)
    {
        if (Services.Input.IsJustPressed(Keys.Space) ||
            Services.Input.IsJustPressed(Keys.Enter))
        {
            _lineIndex++;
            if (_lineIndex >= _lines.Length)
                Services.SceneManager.PopScene();
        }
    }

    public override void Draw(GameTime gameTime, SpriteBatch spriteBatch)
    {
        spriteBatch.Begin();

        // Dialogue box background.
        var boxRect = new Rectangle(40, GraphicsDevice.Viewport.Height - 160,
                                    GraphicsDevice.Viewport.Width - 80, 140);
        var pixel = new Texture2D(GraphicsDevice, 1, 1);
        pixel.SetData(new[] { Color.White });
        spriteBatch.Draw(pixel, boxRect, Color.Black * 0.85f);

        // Current line.
        if (_lineIndex < _lines.Length)
        {
            spriteBatch.DrawString(_font, _lines[_lineIndex],
                new Vector2(boxRect.X + 20, boxRect.Y + 20), Color.White);
        }

        spriteBatch.End();
        pixel.Dispose();
    }
}
```

### HUD Overlay (Updates Below)

```csharp
public sealed class HudOverlayScene : OverlayScene
{
    // HUD should NOT freeze the game — gameplay keeps running.
    public override bool AllowUpdateBelow => true;

    private SpriteFont _font = null!;

    protected override void LoadContent()
    {
        _font = Content.Load<SpriteFont>("Fonts/Hud");
    }

    public override void Update(GameTime gameTime)
    {
        // HUD can poll game state from context or shared service.
    }

    public override void Draw(GameTime gameTime, SpriteBatch spriteBatch)
    {
        spriteBatch.Begin();
        spriteBatch.DrawString(_font, $"Score: {Services.GameContext.Score}",
                               new Vector2(16, 16), Color.White);
        spriteBatch.DrawString(_font, $"HP: {Services.GameContext.PlayerHealth}",
                               new Vector2(16, 48), Color.White);
        spriteBatch.End();
    }
}
```

### Scene Layering

```
┌──────────────────────┐  HudOverlayScene     (transparent, AllowUpdateBelow=true)
├──────────────────────┤  DialogueOverlay      (transparent, AllowUpdateBelow=false)
├──────────────────────┤  GameplayScene        (opaque, drawn because above are transparent)
└──────────────────────┘

Update order:  HUD ✓ → Dialogue ✓ → Gameplay ✗ (Dialogue blocks updates below)
Draw order:    Gameplay → Dialogue → HUD  (bottom up)
```

---

## 10. Practical Example

A complete scene flow wiring everything together. This shows the `Game1` entry point,
all five scenes, and the full state machine.

### Game1 — Entry Point

```csharp
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

public class Game1 : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch = null!;
    private SceneManager _sceneManager = null!;
    private GameStateMachine _gsm = null!;

    public Game1()
    {
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 720,
        };
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
    }

    protected override void Initialize()
    {
        var services = new GameServices
        {
            GraphicsDevice = GraphicsDevice,
            ContentServiceProvider = Content.ServiceProvider,
            Audio = new AudioManager(),
            Input = new InputManager(),
            Settings = new SettingsManager(),
        };

        _sceneManager = new SceneManager(services);
        _gsm = new GameStateMachine(_sceneManager);

        // Bind services back so scenes can reach the manager and FSM.
        services.SceneManager = _sceneManager;
        services.StateMachine = _gsm;
        services.GameContext = new SceneContext();

        // Register all states.
        _gsm.Register(GameState.Splash,    _ => new SplashScene());
        _gsm.Register(GameState.MainMenu,  _ => new MainMenuScene());
        _gsm.Register(GameState.Gameplay,  ctx => new GameplayScene(ctx));
        _gsm.Register(GameState.Pause,     _ => new PauseScene());
        _gsm.Register(GameState.GameOver,  ctx => new GameOverScene(ctx));
        _gsm.Register(GameState.Credits,   _ => new CreditsScene());

        _gsm.GoTo(GameState.Splash);

        base.Initialize();
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
    }

    protected override void Update(GameTime gameTime)
    {
        Services.Input.Update();          // Poll input once per frame.
        _sceneManager.Update(gameTime);
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.Black);
        _sceneManager.Draw(gameTime, _spriteBatch);
        base.Draw(gameTime);
    }
}
```

### SplashScene

```csharp
public sealed class SplashScene : Scene
{
    private float _elapsed;
    private const float Duration = 2.5f;
    private SpriteFont _font = null!;

    protected override void LoadContent()
    {
        _font = Content.Load<SpriteFont>("Fonts/Title");
    }

    public override void Update(GameTime gameTime)
    {
        _elapsed += (float)gameTime.ElapsedGameTime.TotalSeconds;

        if (_elapsed >= Duration || Services.Input.AnyKeyPressed())
        {
            Services.StateMachine.GoTo(GameState.MainMenu, transition: new FadeTransition(0.4f));
        }
    }

    public override void Draw(GameTime gameTime, SpriteBatch spriteBatch)
    {
        GraphicsDevice.Clear(Color.Black);
        spriteBatch.Begin();
        float alpha = MathHelper.Clamp(_elapsed / 0.5f, 0f, 1f)
                    * MathHelper.Clamp((Duration - _elapsed) / 0.5f, 0f, 1f);
        spriteBatch.DrawString(_font, "MY GAME STUDIO", new Vector2(400, 320),
                               Color.White * alpha);
        spriteBatch.End();
    }
}
```

### MainMenuScene

```csharp
public sealed class MainMenuScene : Scene
{
    private SpriteFont _titleFont = null!;
    private SpriteFont _menuFont = null!;
    private readonly string[] _options = { "Play", "Credits", "Quit" };
    private int _selected;

    protected override void LoadContent()
    {
        _titleFont = Content.Load<SpriteFont>("Fonts/Title");
        _menuFont = Content.Load<SpriteFont>("Fonts/Menu");
    }

    public override void Update(GameTime gameTime)
    {
        if (Services.Input.IsJustPressed(Keys.Up))
            _selected = (_selected - 1 + _options.Length) % _options.Length;
        if (Services.Input.IsJustPressed(Keys.Down))
            _selected = (_selected + 1) % _options.Length;

        if (Services.Input.IsJustPressed(Keys.Enter))
        {
            switch (_options[_selected])
            {
                case "Play":
                    var ctx = new SceneContext { LevelNumber = 1 };
                    Services.StateMachine.GoTo(GameState.Gameplay, ctx,
                                               new FadeTransition(0.5f));
                    break;
                case "Credits":
                    Services.StateMachine.GoTo(GameState.Credits,
                                               transition: new FadeTransition(0.3f));
                    break;
                case "Quit":
                    Services.Game.Exit();
                    break;
            }
        }
    }

    public override void Draw(GameTime gameTime, SpriteBatch spriteBatch)
    {
        GraphicsDevice.Clear(new Color(20, 20, 40));
        spriteBatch.Begin();

        spriteBatch.DrawString(_titleFont, "AWESOME GAME",
                               new Vector2(420, 120), Color.Gold);

        for (int i = 0; i < _options.Length; i++)
        {
            Color color = i == _selected ? Color.Yellow : Color.Gray;
            string prefix = i == _selected ? "> " : "  ";
            spriteBatch.DrawString(_menuFont, prefix + _options[i],
                                   new Vector2(540, 300 + i * 50), color);
        }

        spriteBatch.End();
    }
}
```

### GameplayScene

```csharp
public sealed class GameplayScene : Scene
{
    private readonly SceneContext? _ctx;
    private MovementSystem _movement = null!;
    private RenderSystem _render = null!;
    private SpriteFont _font = null!;
    private int _score;
    private int _hp = 100;

    public GameplayScene(SceneContext? ctx) => _ctx = ctx;

    protected override void Initialize()
    {
        _movement = new MovementSystem(World);
        _render = new RenderSystem(World);

        // Restore state from context if available.
        if (_ctx is not null)
        {
            _score = _ctx.Score;
            _hp = _ctx.PlayerHealth;
        }

        // Spawn player entity.
        World.Create(
            new Position(640, 360),
            new Velocity(0, 0),
            new PlayerTag()
        );
    }

    protected override void LoadContent()
    {
        _font = Content.Load<SpriteFont>("Fonts/Hud");
    }

    public override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

        // Pause on Escape.
        if (Services.Input.IsJustPressed(Keys.Escape))
        {
            Services.StateMachine.PushOverlay(GameState.Pause);
            return;
        }

        _movement.Update(dt);

        // Simulate scoring and damage for demo purposes.
        _score += (int)(10 * dt);

        // Check game over.
        if (_hp <= 0)
        {
            var ctx = new SceneContext { Score = _score, LevelNumber = _ctx?.LevelNumber ?? 1 };
            Services.StateMachine.GoTo(GameState.GameOver, ctx, new FadeTransition(0.6f));
        }
    }

    public override void Draw(GameTime gameTime, SpriteBatch spriteBatch)
    {
        GraphicsDevice.Clear(new Color(30, 30, 60));
        _render.Draw(spriteBatch);

        // Inline HUD (or use HudOverlayScene for separation).
        spriteBatch.Begin();
        spriteBatch.DrawString(_font, $"Score: {_score}", new Vector2(16, 16), Color.White);
        spriteBatch.DrawString(_font, $"HP: {_hp}", new Vector2(16, 48), Color.Red);
        spriteBatch.DrawString(_font,
            $"Level {_ctx?.LevelNumber ?? 1}", new Vector2(16, 80), Color.CornflowerBlue);
        spriteBatch.End();
    }
}
```

### GameOverScene

```csharp
public sealed class GameOverScene : Scene
{
    private readonly SceneContext? _ctx;
    private SpriteFont _titleFont = null!;
    private SpriteFont _menuFont = null!;
    private readonly string[] _options = { "Retry", "Main Menu" };
    private int _selected;

    public GameOverScene(SceneContext? ctx) => _ctx = ctx;

    protected override void LoadContent()
    {
        _titleFont = Content.Load<SpriteFont>("Fonts/Title");
        _menuFont = Content.Load<SpriteFont>("Fonts/Menu");
    }

    public override void Update(GameTime gameTime)
    {
        if (Services.Input.IsJustPressed(Keys.Up))
            _selected = (_selected - 1 + _options.Length) % _options.Length;
        if (Services.Input.IsJustPressed(Keys.Down))
            _selected = (_selected + 1) % _options.Length;

        if (Services.Input.IsJustPressed(Keys.Enter))
        {
            switch (_options[_selected])
            {
                case "Retry":
                    var retryCtx = _ctx?.Clone() ?? new SceneContext();
                    retryCtx.PlayerHealth = 100;
                    Services.StateMachine.GoTo(GameState.Gameplay, retryCtx,
                                               new FadeTransition(0.4f));
                    break;
                case "Main Menu":
                    Services.StateMachine.GoTo(GameState.MainMenu,
                                               transition: new FadeTransition(0.5f));
                    break;
            }
        }
    }

    public override void Draw(GameTime gameTime, SpriteBatch spriteBatch)
    {
        GraphicsDevice.Clear(new Color(60, 10, 10));
        spriteBatch.Begin();

        spriteBatch.DrawString(_titleFont, "GAME OVER", new Vector2(460, 180), Color.Red);
        spriteBatch.DrawString(_menuFont,
            $"Final Score: {_ctx?.Score ?? 0}", new Vector2(480, 280), Color.White);

        for (int i = 0; i < _options.Length; i++)
        {
            Color color = i == _selected ? Color.Yellow : Color.Gray;
            string prefix = i == _selected ? "> " : "  ";
            spriteBatch.DrawString(_menuFont, prefix + _options[i],
                                   new Vector2(520, 380 + i * 50), color);
        }

        spriteBatch.End();
    }
}
```

### Complete Flow Diagram

```
┌──────────────┐
│  SplashScene  │──── 2.5s or keypress ────►┌────────────────┐
└──────────────┘      FadeTransition        │  MainMenuScene  │
                                            └───────┬────────┘
                                        Play │      │ Credits
                                             ▼      ▼
                                    ┌──────────┐  ┌──────────────┐
                                    │ Gameplay  │  │ CreditsScene │
                                    │  Scene    │  └──────┬───────┘
                                    └────┬──────┘    Back │
                                    Esc  │  HP≤0         ▼
                                  ┌──────┤         MainMenuScene
                          (push)  ▼      ▼
                        ┌────────────┐ ┌──────────────┐
                        │ PauseScene │ │ GameOverScene │
                        │  (overlay) │ └──┬───────┬───┘
                        └─────┬──────┘  Retry   Menu
                        Resume│  (pop)    │       │
                              ▼           ▼       ▼
                         GameplayScene  Gameplay  MainMenu
```

---

## 11. Composable Scene Subsystems

Scenes don't need to be monolithic. Instead of overriding `Update()` and `Draw()` with hundreds of lines, compose behavior from `IUpdatable` and `IRenderable` lists:

```csharp
public interface IUpdatable { void Update(float dt); }
public interface IRenderable { void Draw(SpriteBatch batch); }

public abstract class Scene
{
    public List<IUpdatable> Updatables { get; } = new();
    public List<IRenderable> Renderables { get; } = new();

    public virtual void Update(float dt)
    {
        foreach (IUpdatable u in Updatables)
            u.Update(dt);
    }

    public virtual void Draw(SpriteBatch batch)
    {
        foreach (IRenderable r in Renderables)
            r.Draw(batch);
    }

    /// <summary>Draw HUD/overlays at native resolution (after virtual resolution scaling).</summary>
    public virtual void DrawOverlay(SpriteBatch batch) { }

    public virtual void Unload() { }
}
```

### Benefits

- **Subsystems are self-contained**: A minimap, pause menu, or performance overlay implements `IUpdatable` and/or `IRenderable` and registers itself. No massive `Update()` switch statement.
- **Easy to add/remove**: `Updatables.Add(new Minimap(...))` — done. Remove it and nothing else changes.
- **Scene doesn't need to own ECS World**: Constructor DI of shared services (world, camera, etc.) works fine. The Scene base class stays lightweight.

### DrawOverlay for HUD

Instead of a full overlay scene stack (which requires its own scene lifecycle, input routing, and render targets), use `DrawOverlay()` for simple HUD elements that draw at native resolution after the virtual resolution scaling pass:

```csharp
// In GameApp.Draw():
_virtualRes.BeginDraw();         // Set virtual-res RT
_sceneManager.Draw(batch);       // Scene draws at virtual res
_virtualRes.EndDraw(batch);      // Scale to display

_sceneManager.DrawOverlay(batch); // HUD at native res (pause menu, perf overlay)
```

This avoids the complexity of overlay scene management while still supporting pause menus, debug overlays, and game-over screens that render outside the virtual resolution pipeline.

---

## Summary

| Concept | Key Class | Responsibility |
|---------|-----------|---------------|
| Scene lifecycle | `Scene` | Initialize → Load → Update/Draw → Unload |
| Stack management | `SceneManager` | Push/pop/change scenes, visibility & update walk |
| State flow | `GameStateMachine` | Maps `GameState` enum to scene factories |
| Transitions | `SceneTransition` | Animate between scenes (fade, wipe, etc.) |
| ECS isolation | `World` per scene | Clean entity lifecycle, no cross-scene leaks |
| Async loading | `LoadingScene` | Background prep + progress bar |
| Data passing | `SceneContext` | Carries score, level, HP between scenes |
| Pause | Overlay with `AllowUpdateBelow=false` | Freezes gameplay, draws behind |
| Overlays | `OverlayScene` | HUD, dialogue, inventory layered on top |

The scene system is the backbone of your game's architecture. Every screen the player sees
is a scene. Every transition between them is managed. Every piece of state is either
scene-local (entities, textures) or passed explicitly (context objects, shared services).
Build on this foundation and your game stays organized as it grows.

---

*Last updated: 2026-03-07*
