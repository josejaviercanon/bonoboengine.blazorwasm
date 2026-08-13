// ============================================================================
// SceneManager.cs — Scene Stack Manager with Transitions
// Extracted from: G38 — Scene & Game State Management
// Part of: Universal 2D Engine Toolkit Examples
// ============================================================================

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace U2DToolkit.Examples.Core;

/// <summary>
/// Owns the scene stack, drives lifecycle calls, and coordinates transitions.
/// Lives as a service on your main <c>Game</c> class.
/// <para>
/// The stack model: the topmost scene is the active scene that receives input.
/// Scenes marked <see cref="Scene.IsTransparent"/> allow scenes below them to
/// be drawn. Scenes marked <see cref="Scene.AllowUpdateBelow"/> allow scenes
/// below them to receive Update calls.
/// </para>
/// <para>
/// All mutation operations (Change/Push/Pop) are deferred to the start of the
/// next Update to prevent stack mutation during iteration.
/// </para>
/// </summary>
/// <example>
/// <code>
/// ┌─────────────────────┐  ← Top (active, receives input)
/// │   PauseScene        │     IsTransparent = true
/// ├─────────────────────┤
/// │   GameplayScene     │     Drawn because PauseScene is transparent
/// ├─────────────────────┤
/// │   (earlier scenes   │     Not drawn — GameplayScene is opaque
/// │    already popped)  │
/// └─────────────────────┘
/// </code>
/// </example>
public sealed class SceneManager
{
    private readonly List<Scene> _sceneStack = new();
    private readonly GameServices _services;

    // Pending operations applied between frames to avoid mutation during iteration.
    private readonly Queue<Action> _pendingOps = new();

    // Active transition (fade, wipe, etc.)
    private SceneTransition? _activeTransition;

    /// <summary>Creates a new SceneManager with the given shared services.</summary>
    public SceneManager(GameServices services)
    {
        _services = services;
    }

    /// <summary>The topmost (active) scene, or null if the stack is empty.</summary>
    public Scene? ActiveScene => _sceneStack.Count > 0
        ? _sceneStack[^1]
        : null;

    // ── Public API ───────────────────────────────────────────────────────

    /// <summary>
    /// Replace the entire stack with a single new scene.
    /// All existing scenes are unloaded. Optionally plays a transition.
    /// </summary>
    /// <param name="next">The new scene to display.</param>
    /// <param name="transition">Optional transition animation (e.g., fade to black).</param>
    public void ChangeScene(Scene next, SceneTransition? transition = null)
    {
        _pendingOps.Enqueue(() => DoChangeScene(next, transition));
    }

    /// <summary>
    /// Push a scene on top of the stack (e.g., pause overlay, dialogue).
    /// The scene below remains loaded and may continue to draw/update
    /// depending on the pushed scene's transparency and update flags.
    /// </summary>
    public void PushScene(Scene scene)
    {
        _pendingOps.Enqueue(() => DoPushScene(scene));
    }

    /// <summary>
    /// Pop the top scene off the stack, returning to the scene below.
    /// The popped scene is fully unloaded.
    /// </summary>
    public void PopScene()
    {
        _pendingOps.Enqueue(DoPopScene);
    }

    // ── Frame hooks (called from Game.Update / Game.Draw) ────────────────

    /// <summary>
    /// Update the scene stack. Call from <c>Game.Update</c>.
    /// Flushes pending operations, runs transitions, then updates
    /// scenes from top down (stopping when AllowUpdateBelow is false).
    /// </summary>
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

    /// <summary>
    /// Draw the scene stack. Call from <c>Game.Draw</c>.
    /// Finds the lowest visible scene (first opaque from top), then
    /// draws bottom-up so overlays paint on top.
    /// </summary>
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

    // ── Internal operations ──────────────────────────────────────────────

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

    /// <summary>
    /// Initialize, load content, add to stack, and notify a scene it has focus.
    /// </summary>
    private void BootScene(Scene scene)
    {
        scene.InternalInitialize(_services);
        scene.InternalLoadContent();
        _sceneStack.Add(scene);
        scene.OnEnter();
    }

    /// <summary>
    /// Unload and remove all scenes from the stack (bottom to top).
    /// </summary>
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

/// <summary>
/// Base class for scene transitions. Transitions animate between two scenes
/// by freezing the old scene, animating out, swapping, and animating in.
/// </summary>
public abstract class SceneTransition
{
    /// <summary>The scene being transitioned away from.</summary>
    protected Scene? OldScene { get; private set; }
    
    /// <summary>The scene being transitioned to.</summary>
    protected Scene? NewScene { get; private set; }
    
    private Action? _swapCallback;

    /// <summary>Whether the transition has finished.</summary>
    public bool IsComplete { get; protected set; }
    
    /// <summary>Whether the scene swap has occurred (midpoint of transition).</summary>
    protected bool HasSwapped { get; private set; }

    /// <summary>
    /// Initialize the transition. Called by <see cref="SceneManager"/>.
    /// </summary>
    public void Start(Scene? oldScene, Scene? newScene, Action swapCallback)
    {
        OldScene = oldScene;
        NewScene = newScene;
        _swapCallback = swapCallback;
        OnStart();
    }

    /// <summary>Override for transition-specific initialization (e.g., capture frozen frame).</summary>
    protected virtual void OnStart() { }

    /// <summary>Advance the transition animation.</summary>
    public abstract void Update(GameTime gameTime);
    
    /// <summary>Render the transition effect.</summary>
    public abstract void Draw(GameTime gameTime, SpriteBatch spriteBatch);

    /// <summary>
    /// Call this at the midpoint to actually perform the scene swap.
    /// Safe to call multiple times — only executes once.
    /// </summary>
    protected void PerformSwap()
    {
        if (HasSwapped) return;
        HasSwapped = true;
        _swapCallback?.Invoke();
    }
}

/// <summary>
/// A simple fade-to-black transition. First half fades old scene to black,
/// second half fades from black to new scene.
/// </summary>
public sealed class FadeTransition : SceneTransition
{
    private readonly float _duration;
    private float _elapsed;
    private float _alpha;

    /// <param name="durationSeconds">Total transition duration in seconds.</param>
    public FadeTransition(float durationSeconds = 0.5f)
    {
        _duration = durationSeconds;
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public override void Draw(GameTime gameTime, SpriteBatch spriteBatch)
    {
        var gd = (OldScene ?? NewScene)!.GraphicsDevice;

        // Draw the appropriate scene underneath.
        if (!HasSwapped && OldScene is not null)
            OldScene.Draw(gameTime, spriteBatch);
        else if (HasSwapped && NewScene is not null)
            NewScene.Draw(gameTime, spriteBatch);

        // Draw the fade overlay.
        var overlay = new Texture2D(gd, 1, 1);
        overlay.SetData(new[] { Color.Black });

        spriteBatch.Begin();
        spriteBatch.Draw(overlay, gd.Viewport.Bounds,
                         Color.White * MathHelper.Clamp(_alpha, 0f, 1f));
        spriteBatch.End();

        overlay.Dispose();
    }
}
