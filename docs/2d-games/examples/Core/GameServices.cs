// ============================================================================
// GameServices.cs — Shared Services Container
// Extracted from: G38 — Scene & Game State Management
// Part of: Universal 2D Engine Toolkit Examples
// ============================================================================

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace U2DToolkit.Examples.Core;

/// <summary>
/// Container for services that live for the entire application lifetime.
/// Passed into every <see cref="Scene"/> so they can access hardware,
/// audio, input, scene management, and other cross-cutting concerns.
/// <para>
/// Services in this container are <b>not</b> scene-local — they persist
/// across scene transitions. Scene-local data lives in the scene's own
/// ECS World or ContentManager.
/// </para>
/// </summary>
/// <remarks>
/// <list type="table">
///   <listheader>
///     <term>Data</term>
///     <description>Where It Lives</description>
///   </listheader>
///   <item>
///     <term>Audio manager</term>
///     <description>GameServices — music continues across transitions</description>
///   </item>
///   <item>
///     <term>Input manager</term>
///     <description>GameServices — input polling is hardware-level</description>
///   </item>
///   <item>
///     <term>Player settings</term>
///     <description>GameServices — volume, keybinds persist</description>
///   </item>
///   <item>
///     <term>ECS entities</term>
///     <description>Scene.World — destroyed when scene unloads</description>
///   </item>
///   <item>
///     <term>Loaded textures</term>
///     <description>Scene.Content — released when scene unloads</description>
///   </item>
///   <item>
///     <term>Score, HP</term>
///     <description>SceneContext — passed between scenes</description>
///   </item>
/// </list>
/// </remarks>
public sealed class GameServices
{
    /// <summary>The MonoGame graphics device for rendering.</summary>
    public GraphicsDevice GraphicsDevice { get; init; } = null!;

    /// <summary>
    /// Service provider for creating per-scene <see cref="Microsoft.Xna.Framework.Content.ContentManager"/>
    /// instances. Typically <c>Game.Content.ServiceProvider</c>.
    /// </summary>
    public IServiceProvider ContentServiceProvider { get; init; } = null!;

    /// <summary>Global audio manager for music and sound effects.</summary>
    public AudioManager Audio { get; init; } = null!;

    /// <summary>
    /// Global input manager. Polled once per frame in <c>Game.Update</c>,
    /// then queried by any scene.
    /// </summary>
    public InputManager Input { get; init; } = null!;

    /// <summary>Player settings (volume, keybinds, display preferences).</summary>
    public SettingsManager Settings { get; init; } = null!;

    /// <summary>The scene manager that owns the scene stack.</summary>
    public SceneManager SceneManager { get; set; } = null!;

    /// <summary>
    /// The game state machine that maps <see cref="GameState"/> enum values
    /// to scene factories.
    /// </summary>
    public GameStateMachine StateMachine { get; set; } = null!;

    /// <summary>
    /// Shared game context for passing data between scenes
    /// (score, lives, level number, etc.).
    /// </summary>
    public SceneContext GameContext { get; set; } = new();

    // Add more services as your engine grows.
}

/// <summary>
/// Bag of data passed between scenes. Carries persistent player state
/// (score, lives, health) across scene transitions.
/// <para>
/// Scene-local data (entity positions, loaded textures) lives in the scene's
/// own ECS World and ContentManager. This context carries only data that
/// must survive scene changes.
/// </para>
/// </summary>
public sealed class SceneContext
{
    // ── Level data ───────────────────────────────────────────────────────
    
    /// <summary>Current level number.</summary>
    public int LevelNumber { get; set; } = 1;
    
    /// <summary>Content path to the level map file.</summary>
    public string? LevelPath { get; set; }

    // ── Player state (carried across levels) ─────────────────────────────
    
    /// <summary>Accumulated score.</summary>
    public int Score { get; set; }
    
    /// <summary>Remaining lives.</summary>
    public int Lives { get; set; } = 3;
    
    /// <summary>Current player health.</summary>
    public int PlayerHealth { get; set; } = 100;

    // ── Inventory / unlocks ──────────────────────────────────────────────
    
    /// <summary>Set of unlocked item/ability identifiers.</summary>
    public HashSet<string> Unlocked { get; set; } = new();

    // ── Misc ─────────────────────────────────────────────────────────────
    
    /// <summary>Total play time accumulated across scenes.</summary>
    public TimeSpan PlayTime { get; set; }

    /// <summary>
    /// Deep-copy for branching (e.g., "restart level" keeps old score
    /// but resets health).
    /// </summary>
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

/// <summary>
/// High-level game states that map to scene factories via
/// <see cref="GameStateMachine"/>.
/// </summary>
public enum GameState
{
    Splash,
    MainMenu,
    Gameplay,
    Pause,
    GameOver,
    Credits
}

/// <summary>
/// Maps <see cref="GameState"/> values to scene factory functions.
/// Keeps scene creation centralized so you can inject dependencies,
/// pass context, etc.
/// </summary>
public sealed class GameStateMachine
{
    private readonly SceneManager _sceneManager;
    private readonly Dictionary<GameState, Func<SceneContext?, Scene>> _factories = new();

    /// <summary>The currently active game state.</summary>
    public GameState CurrentState { get; private set; }

    /// <summary>Creates a new state machine bound to the given scene manager.</summary>
    public GameStateMachine(SceneManager sceneManager)
    {
        _sceneManager = sceneManager;
    }

    /// <summary>Register a factory function for a game state.</summary>
    public void Register(GameState state, Func<SceneContext?, Scene> factory)
    {
        _factories[state] = factory;
    }

    /// <summary>
    /// Transition to a new state, replacing the entire scene stack.
    /// </summary>
    public void GoTo(GameState state, SceneContext? ctx = null,
                     SceneTransition? transition = null)
    {
        if (!_factories.TryGetValue(state, out var factory))
            throw new InvalidOperationException($"No factory registered for {state}");

        CurrentState = state;
        _sceneManager.ChangeScene(factory(ctx), transition);
    }

    /// <summary>
    /// Push an overlay state (pause, inventory) without clearing the stack.
    /// </summary>
    public void PushOverlay(GameState state, SceneContext? ctx = null)
    {
        if (!_factories.TryGetValue(state, out var factory))
            throw new InvalidOperationException($"No factory registered for {state}");

        CurrentState = state;
        _sceneManager.PushScene(factory(ctx));
    }

    /// <summary>
    /// Pop the overlay and revert to the previous state.
    /// </summary>
    public void PopOverlay(GameState returnState)
    {
        CurrentState = returnState;
        _sceneManager.PopScene();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// Placeholder service interfaces — replace with real implementations.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>Placeholder audio manager. Replace with your real implementation.</summary>
public class AudioManager
{
    // Music playback, sound effects, volume control, etc.
}

/// <summary>Placeholder input manager. Replace with your real implementation.</summary>
public class InputManager
{
    /// <summary>Poll input devices. Call once per frame in Game.Update.</summary>
    public void Update() { }
    
    /// <summary>Returns true if any key was just pressed this frame.</summary>
    public bool AnyKeyPressed() => false;
}

/// <summary>Placeholder settings manager. Replace with your real implementation.</summary>
public class SettingsManager
{
    // Volume, keybinds, display preferences, saved to disk.
}
