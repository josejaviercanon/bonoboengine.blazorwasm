// =============================================================================
// Transition.cs — Base transition class and FadeTransition implementation
// Extracted from: G42 — Screen Transitions & Loading Screens (Sections 1–2)
// Guide: /G/G42_screen_transitions.md
// =============================================================================

using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace U2DToolkit.Examples.Transitions
{
    /// <summary>
    /// Transition lifecycle phases.
    /// Flow: Start → AnimateOut (old scene) → Swap → AnimateIn (new scene) → Complete.
    /// </summary>
    public enum TransitionPhase
    {
        None,
        AnimateOut,
        Swap,
        AnimateIn,
        Complete
    }

    /// <summary>
    /// Base class for all screen transition effects. Subclass to define visual behavior.
    /// Manages the five-phase lifecycle (Start → AnimateOut → Swap → AnimateIn → Complete)
    /// and tracks progress within each animation phase.
    /// <para>
    /// The <see cref="TransitionManager"/> captures render targets from the old and new
    /// scenes and passes them to <see cref="Draw"/> each frame during the transition.
    /// </para>
    /// </summary>
    public abstract class Transition
    {
        /// <summary>Duration of each animation phase (out + in) in seconds.</summary>
        public float Duration { get; set; } = 0.4f;

        /// <summary>Current progress within the active phase (0 to 1).</summary>
        public float Progress { get; private set; }

        /// <summary>Current lifecycle phase.</summary>
        public TransitionPhase Phase { get; private set; } = TransitionPhase.None;

        /// <summary>True when the transition has finished completely.</summary>
        public bool IsComplete => Phase == TransitionPhase.Complete;

        protected GraphicsDevice Graphics { get; private set; } = null!;
        protected SpriteBatch SpriteBatch { get; private set; } = null!;

        /// <summary>Initialize with graphics resources. Called by TransitionManager.</summary>
        public void Initialize(GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            Graphics = graphics;
            SpriteBatch = spriteBatch;
            OnInitialize();
        }

        /// <summary>Begin the transition (enters AnimateOut phase).</summary>
        public void Start() => Phase = TransitionPhase.AnimateOut;

        /// <summary>Advance progress. Called each frame by TransitionManager.</summary>
        public void Update(float dt)
        {
            if (Phase is TransitionPhase.None or TransitionPhase.Complete)
                return;

            Progress += dt / Duration;

            if (Progress >= 1f)
            {
                Progress = 1f;

                if (Phase == TransitionPhase.AnimateOut)
                {
                    Phase = TransitionPhase.Swap;
                    Progress = 0f;
                }
                else if (Phase == TransitionPhase.AnimateIn)
                {
                    Phase = TransitionPhase.Complete;
                }
            }
        }

        /// <summary>Advance past the swap phase after scene load finishes.</summary>
        public void BeginAnimateIn()
        {
            Phase = TransitionPhase.AnimateIn;
            Progress = 0f;
        }

        /// <summary>
        /// Draw the transition overlay. Called with the old scene RT during AnimateOut,
        /// new scene RT during AnimateIn, or both for crossfade-style effects.
        /// </summary>
        public abstract void Draw(
            RenderTarget2D? oldScene,
            RenderTarget2D? newScene,
            float progress,
            TransitionPhase phase
        );

        /// <summary>Override for custom initialization (loading textures, effects, etc.).</summary>
        protected virtual void OnInitialize() { }

        /// <summary>Override to clean up resources.</summary>
        public virtual void Dispose() { }

        /// <summary>Smooth step easing for pleasant transitions.</summary>
        protected static float EaseInOut(float t) => t * t * (3f - 2f * t);
    }

    /// <summary>
    /// The simplest and most common transition: fades to a solid color at the midpoint,
    /// swaps scenes, then fades the color back out to reveal the new scene.
    /// </summary>
    public class FadeTransition : Transition
    {
        private Texture2D _pixel = null!;

        /// <summary>Color to fade through (typically black or dark red for death).</summary>
        public Color FadeColor { get; set; } = Color.Black;

        protected override void OnInitialize()
        {
            _pixel = new Texture2D(Graphics, 1, 1);
            _pixel.SetData(new[] { Color.White });
        }

        public override void Draw(
            RenderTarget2D? oldScene, RenderTarget2D? newScene,
            float progress, TransitionPhase phase)
        {
            var viewport = Graphics.Viewport;

            SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);

            // Draw the underlying scene
            var sceneRT = phase == TransitionPhase.AnimateOut ? oldScene : newScene;
            if (sceneRT != null)
            {
                SpriteBatch.Draw(sceneRT, viewport.Bounds, Color.White);
            }

            // Overlay: alpha ramps up during AnimateOut, ramps down during AnimateIn
            float alpha = phase == TransitionPhase.AnimateOut
                ? EaseInOut(progress)
                : 1f - EaseInOut(progress);

            SpriteBatch.Draw(_pixel, viewport.Bounds, FadeColor * alpha);

            SpriteBatch.End();
        }

        public override void Dispose()
        {
            _pixel?.Dispose();
        }
    }

    // =========================================================================
    // TransitionManager — orchestrates the lifecycle
    // =========================================================================

    /// <summary>
    /// Manages the full transition lifecycle: captures render targets from old and new
    /// scenes, runs animated transitions between them, and coordinates scene swapping.
    /// </summary>
    public class TransitionManager
    {
        private readonly GraphicsDevice _graphics;
        private readonly SpriteBatch _spriteBatch;

        private RenderTarget2D? _oldSceneRT;
        private RenderTarget2D? _newSceneRT;
        private Transition? _active;
        private Action? _onSwap;
        private Action? _onComplete;

        /// <summary>True if a transition is currently in progress.</summary>
        public bool IsTransitioning => _active != null && !_active.IsComplete;

        public TransitionManager(GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            _graphics = graphics;
            _spriteBatch = spriteBatch;
        }

        /// <summary>
        /// Begin a transition.
        /// </summary>
        /// <param name="transition">The transition effect to use.</param>
        /// <param name="onSwap">Called at the midpoint to load the new scene.</param>
        /// <param name="onComplete">Called when the full transition ends.</param>
        public void Start(Transition transition, Action onSwap, Action? onComplete = null)
        {
            if (IsTransitioning) return;

            _active = transition;
            _active.Initialize(_graphics, _spriteBatch);
            _onSwap = onSwap;
            _onComplete = onComplete;

            // Capture old scene
            _oldSceneRT = CaptureBackbuffer();
            _active.Start();
        }

        /// <summary>Update the transition each frame.</summary>
        public void Update(float dt)
        {
            if (_active == null) return;

            _active.Update(dt);

            if (_active.Phase == TransitionPhase.Swap)
            {
                _onSwap?.Invoke();
                _onSwap = null;

                _newSceneRT = CaptureBackbuffer();
                _active.BeginAnimateIn();
            }

            if (_active.IsComplete)
            {
                _onComplete?.Invoke();
                Cleanup();
            }
        }

        /// <summary>Draw the transition overlay.</summary>
        public void Draw()
        {
            if (_active == null) return;
            _active.Draw(_oldSceneRT, _newSceneRT, _active.Progress, _active.Phase);
        }

        private RenderTarget2D CaptureBackbuffer()
        {
            var pp = _graphics.PresentationParameters;
            var rt = new RenderTarget2D(
                _graphics, pp.BackBufferWidth, pp.BackBufferHeight,
                false, SurfaceFormat.Color, DepthFormat.None
            );
            var data = new Color[pp.BackBufferWidth * pp.BackBufferHeight];
            _graphics.GetBackBufferData(data);
            rt.SetData(data);
            return rt;
        }

        private void Cleanup()
        {
            _oldSceneRT?.Dispose();
            _newSceneRT?.Dispose();
            _oldSceneRT = null;
            _newSceneRT = null;
            _active?.Dispose();
            _active = null;
            _onSwap = null;
            _onComplete = null;
        }
    }

    // =========================================================================
    // Transition Presets
    // =========================================================================

    /// <summary>Pre-configured transitions for common game scenarios.</summary>
    public static class TransitionPresets
    {
        /// <summary>Standard level change: fade to black and back.</summary>
        public static FadeTransition LevelChange() => new()
        {
            Duration = 0.35f,
            FadeColor = Color.Black
        };

        /// <summary>Death: slow fade to dark red.</summary>
        public static FadeTransition Death() => new()
        {
            Duration = 0.8f,
            FadeColor = new Color(40, 0, 0)
        };

        /// <summary>Menu open: quick fade to dark overlay.</summary>
        public static FadeTransition MenuOpen() => new()
        {
            Duration = 0.2f,
            FadeColor = new Color(0, 0, 0, 200)
        };
    }
}
