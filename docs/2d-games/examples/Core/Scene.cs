// ============================================================================
// Scene.cs — Base Scene Class
// Extracted from: G38 — Scene & Game State Management
// Part of: Universal 2D Engine Toolkit Examples
// ============================================================================

using Arch.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace U2DToolkit.Examples.Core;

/// <summary>
/// Base class for all scenes in the game. Each scene encapsulates its own
/// Arch ECS <see cref="World"/>, registered systems, loaded content, and
/// runtime state. Mirrors MonoGame's own lifecycle:
/// Initialize → LoadContent → Update/Draw loop → UnloadContent.
/// <para>
/// Scenes are the primary organizational unit — a main menu is a scene,
/// gameplay is a scene, the pause overlay is a scene. Each scene is
/// effectively a mini-game inside the larger application.
/// </para>
/// </summary>
public abstract class Scene
{
    // ── Injected references ──────────────────────────────────────────────
    
    /// <summary>Shared services that live for the entire application lifetime.</summary>
    public GameServices Services { get; internal set; } = null!;
    
    /// <summary>Scene-local content manager. Unloaded when the scene exits.</summary>
    public ContentManager Content { get; private set; } = null!;
    
    /// <summary>Shortcut to the graphics device via services.</summary>
    public GraphicsDevice GraphicsDevice => Services.GraphicsDevice;

    // ── ECS ──────────────────────────────────────────────────────────────
    
    /// <summary>
    /// The Arch ECS World owned by this scene. Created on Initialize,
    /// destroyed on UnloadContent. Provides clean entity isolation —
    /// entities from the menu don't leak into gameplay.
    /// </summary>
    public World World { get; private set; } = null!;

    // ── State flags ──────────────────────────────────────────────────────
    
    /// <summary>Whether Initialize has been called.</summary>
    public bool IsInitialized { get; private set; }
    
    /// <summary>Whether LoadContent has been called.</summary>
    public bool IsContentLoaded { get; private set; }

    /// <summary>
    /// If true, scenes below this one on the stack still receive Draw calls.
    /// Override in overlay/transparent scenes (pause menus, HUDs, dialogue).
    /// </summary>
    public virtual bool IsTransparent => false;

    /// <summary>
    /// If true, scenes below this one on the stack still receive Update calls.
    /// Typically false for pause overlays (freezes gameplay) and true for HUDs.
    /// </summary>
    public virtual bool AllowUpdateBelow => false;

    // ── Lifecycle (called by SceneManager) ───────────────────────────────

    /// <summary>
    /// Internal initialization. Creates the ECS World and ContentManager,
    /// then calls the scene's <see cref="Initialize"/> hook.
    /// Called by <see cref="SceneManager"/> — do not call directly.
    /// </summary>
    public void InternalInitialize(GameServices services)
    {
        Services = services;
        Content = new ContentManager(services.ContentServiceProvider, "Content");
        World = World.Create();
        IsInitialized = true;
        Initialize();
    }

    /// <summary>
    /// Register ECS systems, set up initial state.
    /// Override in concrete scene classes.
    /// </summary>
    protected virtual void Initialize() { }

    /// <summary>
    /// Internal content loading. Calls the scene's <see cref="LoadContent"/> hook.
    /// Called by <see cref="SceneManager"/> — do not call directly.
    /// </summary>
    public void InternalLoadContent()
    {
        LoadContent();
        IsContentLoaded = true;
    }

    /// <summary>
    /// Load textures, fonts, sounds via <see cref="Content"/>.
    /// Override in concrete scene classes.
    /// </summary>
    protected virtual void LoadContent() { }

    /// <summary>
    /// Called every frame when this scene is the active (top) scene,
    /// or when a scene above it has <see cref="AllowUpdateBelow"/> set.
    /// </summary>
    public virtual void Update(GameTime gameTime) { }

    /// <summary>
    /// Called every frame when visible — i.e., not occluded by a
    /// non-transparent scene above it on the stack.
    /// </summary>
    public virtual void Draw(GameTime gameTime, SpriteBatch spriteBatch) { }

    /// <summary>
    /// Internal unload. Disposes the ECS World, unloads content,
    /// then calls the scene's <see cref="UnloadContent"/> hook.
    /// Called by <see cref="SceneManager"/> — do not call directly.
    /// </summary>
    public void InternalUnloadContent()
    {
        UnloadContent();
        World.Dispose();
        Content.Unload();
        IsContentLoaded = false;
        IsInitialized = false;
    }

    /// <summary>
    /// Tear down scene-specific resources. Override for custom cleanup.
    /// ECS World and Content are disposed automatically after this.
    /// </summary>
    protected virtual void UnloadContent() { }

    // ── Hooks for scene manager focus changes ────────────────────────────

    /// <summary>
    /// Called when this scene becomes the top scene (gains focus).
    /// Useful for resuming audio, re-enabling input, etc.
    /// </summary>
    public virtual void OnEnter() { }

    /// <summary>
    /// Called when another scene is pushed on top, or when this scene
    /// is popped off the stack. Useful for pausing audio, saving state.
    /// </summary>
    public virtual void OnExit() { }
}
