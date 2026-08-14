# G31 — Animation & Sprite State Machines


> **Category:** Guide · **Related:** [G2 Rendering & Graphics](./G2_rendering_and_graphics.md) · [G8 Content Pipeline](./G8_content_pipeline.md) · [G4 AI Systems](./G4_ai_systems.md) · [C2 Game Feel & Genre Craft](../../core/game-design/C2_game_feel_and_genre_craft.md)

> Deep dive into 2D animation systems for MonoGame + Arch ECS: Aseprite playback, ECS animation components, state machines with transitions and priority, directional sprites, frame events, layered animation, and production-ready patterns for game feel.

---

## 1. MonoGame.Aseprite Animation Playback

[MonoGame.Aseprite](https://github.com/AristurtleDev/monogame-aseprite) (v6.3.1) is the foundation. It loads `.aseprite`/`.ase` files directly — no export step, no sprite sheet JSON, no manual frame rectangles.

### 1.1 Key Types

| Type | Purpose | Created From |
|---|---|---|
| `AsepriteFile` | Raw loaded file — the starting point | `Content.Load<AsepriteFile>()` |
| `SpriteSheet` | All frames packed into one GPU texture + animation tag metadata | `aseFile.CreateSpriteSheet(GraphicsDevice)` |
| `AnimatedSprite` | Playback controller — tracks current frame, timing, looping | `spriteSheet.CreateAnimatedSprite("tag")` |
| `AnimationTag` | Named animation within the file (e.g., "idle", "run") | Defined in Aseprite's tag system |
| `TextureAtlas` | Packed frames as named texture regions (no animation logic) | `aseFile.CreateTextureAtlas(GraphicsDevice)` |

### 1.2 Loading and Creating Animations

```csharp
using MonoGame.Aseprite;

// In LoadContent
AsepriteFile aseFile = Content.Load<AsepriteFile>("sprites/player");

// Create a SpriteSheet — packs ALL frames into a single GPU texture
// This is the workhorse: one SpriteSheet per character
SpriteSheet spriteSheet = aseFile.CreateSpriteSheet(GraphicsDevice);

// Create an AnimatedSprite starting with the "idle" tag
AnimatedSprite animatedSprite = spriteSheet.CreateAnimatedSprite("idle");
```

### 1.3 Playback Control

```csharp
// In Update — advance frame timing
animatedSprite.Update(gameTime);

// Switch animation tag (only restarts if tag actually changed)
animatedSprite.Play("run");

// Play once (no loop) — attack, hurt, death
animatedSprite.Play("attack", loopCount: 0);

// Playback control
animatedSprite.Stop();                     // stop and reset to frame 0
animatedSprite.Pause();                    // freeze on current frame
animatedSprite.Unpause();                  // resume from paused frame
animatedSprite.SetFrame(0);               // jump to specific frame
animatedSprite.Speed = 1.5f;              // 1.5× playback speed
animatedSprite.FlipHorizontally = true;   // face left (mirror sprite)
animatedSprite.FlipVertically = false;

// In Draw
animatedSprite.Draw(spriteBatch, new Vector2(100, 200));
```

### 1.4 Animation Events

MonoGame.Aseprite fires callbacks at key moments during playback:

```csharp
// Frame begins — use for gameplay triggers
animatedSprite.OnFrameBegin += (sender, args) =>
{
    // args.FrameIndex = which frame within the current tag just started
};

// Frame ends
animatedSprite.OnFrameEnd += (sender, args) =>
{
    // Frame finished displaying
};

// Full animation loop complete
animatedSprite.OnAnimationEnd += (sender, args) =>
{
    // Fired when a non-looping animation finishes,
    // or when a looping animation completes one full cycle
};

// Animation loop restarted
animatedSprite.OnAnimationLoop += (sender, args) =>
{
    // A looping animation just wrapped back to frame 0
};
```

### 1.5 Accessing Tag & Frame Data

```csharp
// Current state
string currentTag = animatedSprite.CurrentTag;
int currentFrame = animatedSprite.CurrentFrameIndex;
bool isAnimating = animatedSprite.IsAnimating;
bool isPaused = animatedSprite.IsPaused;

// Query available tags from the SpriteSheet
foreach (var tag in spriteSheet.Tags)
{
    Console.WriteLine($"Tag: {tag.Name}, Frames: {tag.FrameCount}, " +
                      $"Direction: {tag.LoopDirection}");
}
```

### 1.6 Aseprite Workflow Tips

| Practice | Why |
|---|---|
| One `.aseprite` file per character/entity | All animations (idle, run, jump, attack) as tags in one file |
| Use Aseprite's tag system for naming | Tag names become animation names in code — zero mapping |
| Set frame durations in Aseprite | Authoritative timing — don't override in code unless needed |
| Use consistent canvas sizes | All frames same size = no origin shifting between animations |
| Hide helper layers | MonoGame.Aseprite flattens visible layers — hide hitbox/guideline layers |

---

## 2. Animation Component for Arch ECS

The `AnimatedSprite` from MonoGame.Aseprite is a class with internal state — it works, but for a pure ECS approach you want lightweight data components that systems operate on.

### 2.1 Core Animation Components

```csharp
/// <summary>
/// Stores the SpriteSheet reference and current AnimatedSprite.
/// This is a managed component (class reference) — Arch handles these fine.
/// </summary>
public record struct AnimationRenderer(AnimatedSprite Sprite, SpriteSheet Sheet);

/// <summary>
/// Pure data: what animation is playing and its state.
/// Systems read/write this to control animation.
/// </summary>
public record struct AnimationState
{
    public string CurrentAnimation;    // tag name: "idle", "run", "attack"
    public string PreviousAnimation;   // for transition detection
    public int CurrentFrame;           // frame index within tag
    public float Timer;                // elapsed time on current frame
    public float PlaybackSpeed;        // multiplier (1.0 = normal, 0.5 = half)
    public bool Looping;               // does this animation loop?
    public bool Finished;              // true when non-looping anim completes
    public bool Locked;                // true = don't allow state machine transitions

    public static AnimationState Default(string startAnim) => new()
    {
        CurrentAnimation = startAnim,
        PreviousAnimation = "",
        CurrentFrame = 0,
        Timer = 0f,
        PlaybackSpeed = 1f,
        Looping = true,
        Finished = false,
        Locked = false
    };
}

/// <summary>
/// Which direction the entity is facing — drives animation name resolution.
/// </summary>
public record struct Facing(FaceDirection Direction);

public enum FaceDirection
{
    Down = 0,
    Up = 1,
    Left = 2,
    Right = 3,
    DownLeft = 4,
    DownRight = 5,
    UpLeft = 6,
    UpRight = 7
}

/// <summary>
/// Optional: visual modifiers applied during rendering.
/// </summary>
public record struct SpriteFlash
{
    public float Duration;      // total flash time
    public float Elapsed;       // time elapsed
    public Color FlashColor;    // typically Color.White for hit flash
    public bool Active => Elapsed < Duration;
}
```

### 2.2 Entity Creation

```csharp
// Load assets
var aseFile = Content.Load<AsepriteFile>("sprites/player");
var sheet = aseFile.CreateSpriteSheet(GraphicsDevice);
var animSprite = sheet.CreateAnimatedSprite("idle");

// Create entity with animation components
var player = world.Create(
    new Position(100, 200),
    new Velocity(0, 0),
    new AnimationRenderer(animSprite, sheet),
    new AnimationState.Default("idle"),
    new Facing(FaceDirection.Right),
    new RenderLayerTag(20),
    new SortOrder(200f)
);
```

### 2.3 Why Separate `AnimationState` from `AnimationRenderer`?

| Concern | Component | Reason |
|---|---|---|
| Game logic reads/writes animation state | `AnimationState` | Pure data — systems query and modify without touching rendering |
| Rendering reads sprite data | `AnimationRenderer` | Contains the MonoGame.Aseprite objects that draw |
| AI/physics can set animations | `AnimationState` | AI system writes `CurrentAnimation = "patrol"` without knowing about sprites |
| Serialization/networking | `AnimationState` | Pure data serializes trivially; `AnimatedSprite` doesn't |

---

## 3. Animation State Machine

This is where most projects fall apart. A naive approach checks a dozen boolean flags every frame. A proper state machine has explicit states, typed transitions, and priority.

### 3.1 State Definition

```csharp
/// <summary>
/// Defines a single animation state with its properties.
/// </summary>
public class AnimStateDefinition
{
    public string Name { get; init; }            // matches Aseprite tag name
    public bool Loops { get; init; } = true;
    public float SpeedMultiplier { get; init; } = 1f;
    public int Priority { get; init; }           // higher = harder to interrupt
    public bool LockUntilComplete { get; init; } // can't be interrupted until done
    public string FallbackState { get; init; }   // state to go to when this finishes (non-looping)
}
```

### 3.2 Transition Rules

```csharp
/// <summary>
/// A transition from one state to another, with a condition and priority.
/// </summary>
public class AnimTransition
{
    public string FromState { get; init; }
    public string ToState { get; init; }
    public Func<AnimTransitionContext, bool> Condition { get; init; }
    public int Priority { get; init; }           // higher = checked first among transitions from same state
}

/// <summary>
/// Context passed to transition conditions — everything they need to decide.
/// </summary>
public readonly struct AnimTransitionContext
{
    public readonly Velocity Velocity;
    public readonly bool IsGrounded;
    public readonly bool JustJumped;
    public readonly bool AttackPressed;
    public readonly bool HurtThisFrame;
    public readonly bool IsDead;
    public readonly bool AnimationFinished;
    public readonly float AnimationTimer;

    public AnimTransitionContext(Velocity velocity, bool isGrounded, bool justJumped,
        bool attackPressed, bool hurtThisFrame, bool isDead,
        bool animationFinished, float animationTimer)
    {
        Velocity = velocity;
        IsGrounded = isGrounded;
        JustJumped = justJumped;
        AttackPressed = attackPressed;
        HurtThisFrame = hurtThisFrame;
        IsDead = isDead;
        AnimationFinished = animationFinished;
        AnimationTimer = animationTimer;
    }
}
```

### 3.3 The State Machine

```csharp
/// <summary>
/// Reusable animation state machine definition.
/// Create ONE of these per character type (player, slime, skeleton).
/// It contains no per-entity state — that lives in AnimationState component.
/// </summary>
public class AnimationStateMachineDefinition
{
    private readonly Dictionary<string, AnimStateDefinition> _states = new();
    private readonly Dictionary<string, List<AnimTransition>> _transitions = new();
    private readonly string _defaultState;

    public AnimationStateMachineDefinition(string defaultState)
    {
        _defaultState = defaultState;
    }

    public AnimationStateMachineDefinition AddState(AnimStateDefinition state)
    {
        _states[state.Name] = state;
        return this;
    }

    public AnimationStateMachineDefinition AddTransition(AnimTransition transition)
    {
        if (!_transitions.ContainsKey(transition.FromState))
            _transitions[transition.FromState] = new List<AnimTransition>();
        _transitions[transition.FromState].Add(transition);
        return this;
    }

    /// <summary>
    /// Add a transition from ANY state (wildcard).
    /// Useful for hurt/death which can interrupt anything.
    /// </summary>
    public AnimationStateMachineDefinition AddGlobalTransition(
        string toState, Func<AnimTransitionContext, bool> condition, int priority = 100)
    {
        foreach (var state in _states.Keys)
        {
            if (state == toState) continue; // don't transition to self
            AddTransition(new AnimTransition
            {
                FromState = state,
                ToState = toState,
                Condition = condition,
                Priority = priority
            });
        }
        return this;
    }

    /// <summary>
    /// Evaluate transitions for the current state.
    /// Returns the new state name, or null if no transition fires.
    /// </summary>
    public string? Evaluate(string currentState, AnimTransitionContext context)
    {
        // If current state is locked and not finished, no transitions
        if (_states.TryGetValue(currentState, out var stateDef))
        {
            if (stateDef.LockUntilComplete && !context.AnimationFinished)
                return null;
        }

        if (!_transitions.TryGetValue(currentState, out var transitions))
            return null;

        // Sort by priority (higher first) — stable sort preserves insertion order for ties
        // Note: sorting each frame is fine for small lists (typically < 10 transitions per state)
        foreach (var t in transitions.OrderByDescending(t => t.Priority))
        {
            // Priority check: can this transition interrupt current state?
            if (_states.TryGetValue(t.ToState, out var targetDef) &&
                _states.TryGetValue(currentState, out var currentDef))
            {
                if (targetDef.Priority < currentDef.Priority && currentDef.LockUntilComplete)
                    continue;
            }

            if (t.Condition(context))
                return t.ToState;
        }

        // If current state finished and has a fallback, use it
        if (context.AnimationFinished && stateDef?.FallbackState != null)
            return stateDef.FallbackState;

        return null;
    }

    public AnimStateDefinition? GetState(string name)
        => _states.TryGetValue(name, out var s) ? s : null;

    public string DefaultState => _defaultState;
}
```

### 3.4 Building a Platformer State Machine

```csharp
public static class PlayerAnimations
{
    public static AnimationStateMachineDefinition Create()
    {
        var sm = new AnimationStateMachineDefinition("idle");

        // --- Define states ---
        sm.AddState(new AnimStateDefinition
        {
            Name = "idle", Loops = true, Priority = 0
        });
        sm.AddState(new AnimStateDefinition
        {
            Name = "run", Loops = true, Priority = 0
        });
        sm.AddState(new AnimStateDefinition
        {
            Name = "jump", Loops = false, Priority = 1,
            LockUntilComplete = false, FallbackState = "fall"
        });
        sm.AddState(new AnimStateDefinition
        {
            Name = "fall", Loops = true, Priority = 1
        });
        sm.AddState(new AnimStateDefinition
        {
            Name = "land", Loops = false, Priority = 2,
            LockUntilComplete = true, FallbackState = "idle"
        });
        sm.AddState(new AnimStateDefinition
        {
            Name = "attack", Loops = false, Priority = 3,
            LockUntilComplete = true, FallbackState = "idle"
        });
        sm.AddState(new AnimStateDefinition
        {
            Name = "hurt", Loops = false, Priority = 5,
            LockUntilComplete = true, FallbackState = "idle"
        });
        sm.AddState(new AnimStateDefinition
        {
            Name = "death", Loops = false, Priority = 10,
            LockUntilComplete = true  // no fallback — stays dead
        });

        // --- Define transitions ---
        // Idle ↔ Run
        sm.AddTransition(new AnimTransition
        {
            FromState = "idle", ToState = "run",
            Condition = ctx => MathF.Abs(ctx.Velocity.X) > 0.1f && ctx.IsGrounded
        });
        sm.AddTransition(new AnimTransition
        {
            FromState = "run", ToState = "idle",
            Condition = ctx => MathF.Abs(ctx.Velocity.X) <= 0.1f && ctx.IsGrounded
        });

        // Ground → Jump
        sm.AddTransition(new AnimTransition
        {
            FromState = "idle", ToState = "jump",
            Condition = ctx => ctx.JustJumped, Priority = 5
        });
        sm.AddTransition(new AnimTransition
        {
            FromState = "run", ToState = "jump",
            Condition = ctx => ctx.JustJumped, Priority = 5
        });

        // Jump → Fall (when rising stops)
        sm.AddTransition(new AnimTransition
        {
            FromState = "jump", ToState = "fall",
            Condition = ctx => ctx.Velocity.Y > 0 || ctx.AnimationFinished
        });

        // Walk off ledge → Fall
        sm.AddTransition(new AnimTransition
        {
            FromState = "idle", ToState = "fall",
            Condition = ctx => !ctx.IsGrounded
        });
        sm.AddTransition(new AnimTransition
        {
            FromState = "run", ToState = "fall",
            Condition = ctx => !ctx.IsGrounded
        });

        // Fall → Land
        sm.AddTransition(new AnimTransition
        {
            FromState = "fall", ToState = "land",
            Condition = ctx => ctx.IsGrounded, Priority = 5
        });

        // Attack (from idle or run)
        sm.AddTransition(new AnimTransition
        {
            FromState = "idle", ToState = "attack",
            Condition = ctx => ctx.AttackPressed, Priority = 3
        });
        sm.AddTransition(new AnimTransition
        {
            FromState = "run", ToState = "attack",
            Condition = ctx => ctx.AttackPressed, Priority = 3
        });

        // Global transitions — hurt and death override (almost) everything
        sm.AddGlobalTransition("death", ctx => ctx.IsDead, priority: 100);
        sm.AddGlobalTransition("hurt", ctx => ctx.HurtThisFrame && !ctx.IsDead, priority: 50);

        return sm;
    }
}
```

### 3.5 State Machine as a Shared Component

```csharp
/// <summary>
/// Tag component referencing a shared state machine definition.
/// All entities of the same type share one definition instance.
/// </summary>
public record struct AnimStateMachineRef(AnimationStateMachineDefinition Definition);

// At init — create ONCE, share across all player entities
AnimationStateMachineDefinition playerSM = PlayerAnimations.Create();

// For each player entity
world.Create(
    // ...other components...
    new AnimStateMachineRef(playerSM),
    AnimationState.Default("idle")
);
```

---

## 4. Directional Animations

Top-down games need sprites facing in 4 or 8 directions. Side-scrollers just flip horizontally. Here's how to handle both.

### 4.1 Naming Conventions in Aseprite

Use a consistent naming scheme for animation tags:

**4-directional (top-down RPG, Zelda-like):**

| Tag Name | Direction |
|---|---|
| `idle_down` | Facing down (toward camera) |
| `idle_up` | Facing up (away from camera) |
| `idle_left` | Facing left |
| `idle_right` | Facing right |
| `run_down` | Running down |
| `run_up` | Running up |
| `run_left` | Running left |
| `run_right` | Running right |
| `attack_down` | Attack facing down |
| ... | ... |

**8-directional (isometric, twin-stick):**

| Tag Name | Direction |
|---|---|
| `idle_down` | South |
| `idle_up` | North |
| `idle_left` | West |
| `idle_right` | East |
| `idle_down_left` | Southwest |
| `idle_down_right` | Southeast |
| `idle_up_left` | Northwest |
| `idle_up_right` | Northeast |

**Side-scroller:** Only one set of tags (`idle`, `run`, `jump`). Use `FlipHorizontally` for left-facing.

### 4.2 Direction Resolution

```csharp
public static class DirectionResolver
{
    /// <summary>
    /// Resolve a velocity vector to a 4-direction facing.
    /// Uses a dead zone to prevent flickering when nearly stopped.
    /// </summary>
    public static FaceDirection Resolve4Dir(Vector2 velocity, FaceDirection current,
        float deadZone = 0.1f)
    {
        if (velocity.LengthSquared() < deadZone * deadZone)
            return current; // keep current facing when stopped

        // Favor horizontal or vertical based on dominant axis
        if (MathF.Abs(velocity.X) >= MathF.Abs(velocity.Y))
            return velocity.X > 0 ? FaceDirection.Right : FaceDirection.Left;
        else
            return velocity.Y > 0 ? FaceDirection.Down : FaceDirection.Up;
    }

    /// <summary>
    /// Resolve a velocity vector to an 8-direction facing.
    /// </summary>
    public static FaceDirection Resolve8Dir(Vector2 velocity, FaceDirection current,
        float deadZone = 0.1f)
    {
        if (velocity.LengthSquared() < deadZone * deadZone)
            return current;

        float angle = MathF.Atan2(velocity.Y, velocity.X);
        // Divide circle into 8 sectors of 45° each
        int sector = (int)MathF.Round(angle / (MathF.PI / 4));

        return sector switch
        {
            0  => FaceDirection.Right,
            1  => FaceDirection.DownRight,
            2  => FaceDirection.Down,
            3  => FaceDirection.DownLeft,
            4 or -4 => FaceDirection.Left,
            -3 => FaceDirection.UpLeft,
            -2 => FaceDirection.Up,
            -1 => FaceDirection.UpRight,
            _  => current
        };
    }

    /// <summary>
    /// Get the suffix for a directional tag name.
    /// </summary>
    public static string GetDirectionSuffix(FaceDirection dir) => dir switch
    {
        FaceDirection.Down      => "_down",
        FaceDirection.Up        => "_up",
        FaceDirection.Left      => "_left",
        FaceDirection.Right     => "_right",
        FaceDirection.DownLeft  => "_down_left",
        FaceDirection.DownRight => "_down_right",
        FaceDirection.UpLeft    => "_up_left",
        FaceDirection.UpRight   => "_up_right",
        _ => "_down"
    };
}
```

### 4.3 Directional Animation Resolution

```csharp
public static class DirectionalAnimResolver
{
    /// <summary>
    /// Resolves the final animation tag name, handling directional variants.
    /// Falls back gracefully: if "run_down_left" doesn't exist, tries "run_left",
    /// then "run_down", then "run".
    /// </summary>
    public static string Resolve(string baseAnim, FaceDirection direction,
        SpriteSheet sheet, out bool flipH)
    {
        flipH = false;

        // Side-scroller mode: no directional suffix, just flip
        string direct = baseAnim + DirectionResolver.GetDirectionSuffix(direction);
        if (sheet.HasTag(direct))
            return direct;

        // Try mirrored direction (left → right + flip, or vice versa)
        var mirror = GetMirrorDirection(direction);
        if (mirror.HasValue)
        {
            string mirrored = baseAnim + DirectionResolver.GetDirectionSuffix(mirror.Value);
            if (sheet.HasTag(mirrored))
            {
                flipH = true;
                return mirrored;
            }
        }

        // Fallback: try without direction suffix
        if (sheet.HasTag(baseAnim))
            return baseAnim;

        // Last resort
        return baseAnim;
    }

    private static FaceDirection? GetMirrorDirection(FaceDirection dir) => dir switch
    {
        FaceDirection.Left      => FaceDirection.Right,
        FaceDirection.Right     => FaceDirection.Left,
        FaceDirection.UpLeft    => FaceDirection.UpRight,
        FaceDirection.UpRight   => FaceDirection.UpLeft,
        FaceDirection.DownLeft  => FaceDirection.DownRight,
        FaceDirection.DownRight => FaceDirection.DownLeft,
        _ => null
    };
}
```

**`HasTag` extension for `SpriteSheet`:**
```csharp
public static class SpriteSheetExtensions
{
    /// <summary>
    /// Check if a SpriteSheet contains a tag with the given name.
    /// </summary>
    public static bool HasTag(this SpriteSheet sheet, string tagName)
    {
        foreach (var tag in sheet.Tags)
        {
            if (tag.Name == tagName) return true;
        }
        return false;
    }
}
```

### 4.4 Side-Scroller Horizontal Flip

For side-scrollers, you only need one set of animations. Flip the sprite based on velocity:

```csharp
// In a FacingUpdateSystem
world.Query(in _facingQuery, (ref Velocity vel, ref Facing facing,
    ref AnimationRenderer renderer) =>
{
    if (MathF.Abs(vel.X) > 0.1f)
    {
        bool faceLeft = vel.X < 0;
        facing.Direction = faceLeft ? FaceDirection.Left : FaceDirection.Right;
        renderer.Sprite.FlipHorizontally = faceLeft;
    }
});
```

---

## 5. Animation Events & Callbacks

Frame events connect animation to gameplay: spawn a projectile on frame 3, play a footstep on frame 2, create a dust cloud on landing.

### 5.1 Event Definition

```csharp
/// <summary>
/// Defines an event that fires on a specific frame of a specific animation.
/// </summary>
public readonly struct AnimationFrameEvent
{
    public readonly string Animation;   // tag name
    public readonly int FrameIndex;     // which frame triggers this
    public readonly AnimEventType Type;
    public readonly string Parameter;   // sound name, effect name, etc.
}

public enum AnimEventType
{
    PlaySound,
    SpawnEffect,
    SpawnHitbox,
    RemoveHitbox,
    CameraShake,
    SpawnProjectile,
    Custom
}
```

### 5.2 Event Map

```csharp
/// <summary>
/// A reusable map of frame events for a character type.
/// Create once, share across all entities of that type.
/// </summary>
public class AnimationEventMap
{
    // Key: "animation:frame" → list of events
    private readonly Dictionary<string, List<AnimationFrameEvent>> _events = new();

    public AnimationEventMap On(string animation, int frame, AnimEventType type,
        string parameter = "")
    {
        string key = $"{animation}:{frame}";
        if (!_events.ContainsKey(key))
            _events[key] = new List<AnimationFrameEvent>();

        _events[key].Add(new AnimationFrameEvent
        {
            Animation = animation,
            FrameIndex = frame,
            Type = type,
            Parameter = parameter
        });
        return this;
    }

    public IReadOnlyList<AnimationFrameEvent> GetEvents(string animation, int frame)
    {
        string key = $"{animation}:{frame}";
        return _events.TryGetValue(key, out var list)
            ? list
            : Array.Empty<AnimationFrameEvent>();
    }
}
```

### 5.3 Building an Event Map

```csharp
public static class PlayerAnimEvents
{
    public static AnimationEventMap Create() => new AnimationEventMap()
        // Run: footsteps on frames 2 and 6
        .On("run", 2, AnimEventType.PlaySound, "sfx/footstep_01")
        .On("run", 6, AnimEventType.PlaySound, "sfx/footstep_02")
        .On("run", 2, AnimEventType.SpawnEffect, "dust_puff")
        .On("run", 6, AnimEventType.SpawnEffect, "dust_puff")

        // Attack: sword whoosh on frame 2, hitbox active frames 3-5
        .On("attack", 2, AnimEventType.PlaySound, "sfx/sword_swing")
        .On("attack", 3, AnimEventType.SpawnHitbox, "sword")
        .On("attack", 5, AnimEventType.RemoveHitbox, "sword")
        .On("attack", 3, AnimEventType.CameraShake, "light")

        // Jump: launch sound on frame 0
        .On("jump", 0, AnimEventType.PlaySound, "sfx/jump")
        .On("jump", 0, AnimEventType.SpawnEffect, "jump_dust")

        // Land: impact on frame 0
        .On("land", 0, AnimEventType.PlaySound, "sfx/land")
        .On("land", 0, AnimEventType.SpawnEffect, "land_dust")
        .On("land", 0, AnimEventType.CameraShake, "light")

        // Hurt: hit sound
        .On("hurt", 0, AnimEventType.PlaySound, "sfx/hurt")
        .On("hurt", 0, AnimEventType.CameraShake, "medium")

        // Death
        .On("death", 0, AnimEventType.PlaySound, "sfx/death");
}
```

### 5.4 ECS Event Component

```csharp
/// <summary>
/// Component referencing the shared event map.
/// </summary>
public record struct AnimEventMapRef(AnimationEventMap Map);

/// <summary>
/// Buffer for frame events that fired THIS frame.
/// The event dispatch system reads and clears this each tick.
/// </summary>
public record struct AnimEventBuffer
{
    public AnimationFrameEvent[] Events;
    public int Count;

    public static AnimEventBuffer Create(int capacity = 8)
    {
        return new AnimEventBuffer
        {
            Events = new AnimationFrameEvent[capacity],
            Count = 0
        };
    }

    public void Add(AnimationFrameEvent evt)
    {
        if (Count < Events.Length)
            Events[Count++] = evt;
    }

    public void Clear() => Count = 0;
}
```

---

## 6. Blend Trees & Layered Animation

Most 2D games don't need full blend trees (that's a 3D concept), but **layered animation** is common: a character walks with their legs while their upper body holds a torch, or an overlay animation (carrying an item) plays on top of the base animation.

### 6.1 Layer Architecture

```csharp
/// <summary>
/// An animation layer renders independently and composites on top.
/// Each layer has its own AnimatedSprite and state.
/// </summary>
public class AnimationLayer
{
    public string Name { get; init; }
    public AnimatedSprite Sprite { get; set; }
    public string CurrentAnimation { get; set; }
    public int Priority { get; set; }             // higher layers draw on top
    public float Opacity { get; set; } = 1f;
    public bool Active { get; set; } = true;
    public Vector2 Offset { get; set; }            // per-layer position offset
    public BlendMode Blend { get; set; } = BlendMode.Alpha;
}

public enum BlendMode
{
    Alpha,      // standard alpha blend (overlay)
    Replace,    // replaces pixels entirely
    Additive    // additive blend (glows)
}
```

### 6.2 Layered Animation Component

```csharp
/// <summary>
/// Multi-layer animation for entities with composite sprites.
/// </summary>
public class LayeredAnimation
{
    private readonly Dictionary<string, AnimationLayer> _layers = new();
    private readonly List<AnimationLayer> _sortedLayers = new();
    private bool _dirty = true;

    public void AddLayer(AnimationLayer layer)
    {
        _layers[layer.Name] = layer;
        _dirty = true;
    }

    public void RemoveLayer(string name)
    {
        _layers.Remove(name);
        _dirty = true;
    }

    public AnimationLayer? GetLayer(string name)
        => _layers.TryGetValue(name, out var l) ? l : null;

    public void SetLayerAnimation(string layerName, string animation)
    {
        if (_layers.TryGetValue(layerName, out var layer))
        {
            if (layer.CurrentAnimation != animation)
            {
                layer.CurrentAnimation = animation;
                layer.Sprite.Play(animation);
            }
        }
    }

    public void SetLayerActive(string layerName, bool active)
    {
        if (_layers.TryGetValue(layerName, out var layer))
            layer.Active = active;
    }

    public void Update(GameTime gameTime)
    {
        foreach (var layer in _layers.Values)
        {
            if (layer.Active)
                layer.Sprite.Update(gameTime);
        }
    }

    public void Draw(SpriteBatch spriteBatch, Vector2 position)
    {
        if (_dirty)
        {
            _sortedLayers.Clear();
            _sortedLayers.AddRange(_layers.Values);
            _sortedLayers.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            _dirty = false;
        }

        foreach (var layer in _sortedLayers)
        {
            if (!layer.Active) continue;

            var layerPos = position + layer.Offset;
            var color = Color.White * layer.Opacity;

            // Note: for different BlendModes, you'd need separate SpriteBatch
            // Begin/End calls. For simplicity, this assumes all layers share
            // the same SpriteBatch call. See G2 for batch management.
            layer.Sprite.Color = color;
            layer.Sprite.Draw(spriteBatch, layerPos);
        }
    }
}
```

### 6.3 Common Layer Patterns

**Upper body + Lower body (top-down RPG):**
```csharp
var layered = new LayeredAnimation();

// Lower body layer — handles walk/idle legs
var legsSprite = legsSheet.CreateAnimatedSprite("walk_down");
layered.AddLayer(new AnimationLayer
{
    Name = "legs",
    Sprite = legsSprite,
    Priority = 0
});

// Upper body layer — handles arm/weapon animations
var torsoSprite = torsoSheet.CreateAnimatedSprite("idle_down");
layered.AddLayer(new AnimationLayer
{
    Name = "torso",
    Sprite = torsoSprite,
    Priority = 1
});

// Equipment overlay — renders on top when carrying something
var itemSprite = itemSheet.CreateAnimatedSprite("torch_hold");
layered.AddLayer(new AnimationLayer
{
    Name = "held_item",
    Sprite = itemSprite,
    Priority = 2,
    Active = false  // activate when player picks up item
});
```

**Character + equipment overlays (RPG with visible gear):**
```csharp
// Base character
layered.AddLayer(new AnimationLayer { Name = "body", Priority = 0, ... });

// Armor overlay (matches body animations frame-for-frame)
layered.AddLayer(new AnimationLayer { Name = "armor", Priority = 1, ... });

// Weapon overlay
layered.AddLayer(new AnimationLayer { Name = "weapon", Priority = 2, ... });

// Hair/helmet (drawn on top of everything)
layered.AddLayer(new AnimationLayer { Name = "head", Priority = 3, ... });

// All layers play the same animation in sync:
void SetAllLayerAnimations(string anim)
{
    layered.SetLayerAnimation("body", anim);
    layered.SetLayerAnimation("armor", $"armor_{anim}");
    layered.SetLayerAnimation("weapon", $"weapon_{anim}");
    layered.SetLayerAnimation("head", $"head_{anim}");
}
```

### 6.4 When NOT to Use Layers

| Scenario | Use Layers? | Alternative |
|---|---|---|
| Simple side-scroller character | ❌ No | Single `AnimatedSprite` is enough |
| Top-down with equipment overlays | ✅ Yes | Each equipment slot is a layer |
| Character with weapon swing | ⚠️ Maybe | Easier to bake weapon into attack animation in Aseprite |
| Particle effects on character | ❌ No | Separate particle entity, positioned relative to parent |
| Character carrying another sprite | ✅ Yes | Overlay layer with offset |

---

## 7. AnimationSystem for Arch ECS

This is the core system that ties everything together: it advances frames, evaluates state machine transitions, fires events, and syncs the `AnimatedSprite` to the `AnimationState` component.

### 7.1 The Complete Animation System

```csharp
using Arch.Core;
using Arch.System;

public class AnimationSystem : BaseSystem<World, GameTime>
{
    private readonly QueryDescription _stateMachineQuery = new QueryDescription()
        .WithAll<AnimationState, AnimationRenderer, AnimStateMachineRef,
                 Velocity, Facing>();

    private readonly QueryDescription _simpleAnimQuery = new QueryDescription()
        .WithAll<AnimationState, AnimationRenderer>()
        .WithNone<AnimStateMachineRef>();

    private readonly QueryDescription _eventQuery = new QueryDescription()
        .WithAll<AnimationState, AnimEventMapRef, AnimEventBuffer>();

    private readonly QueryDescription _flashQuery = new QueryDescription()
        .WithAll<SpriteFlash>();

    // Gameplay context — set by other systems before AnimationSystem runs
    public bool PlayerGrounded { get; set; }
    public bool PlayerJustJumped { get; set; }
    public bool PlayerAttackPressed { get; set; }
    public bool PlayerHurtThisFrame { get; set; }
    public bool PlayerIsDead { get; set; }

    public AnimationSystem(World world) : base(world) { }

    public override void Update(in GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

        // Phase 1: Evaluate state machine transitions
        World.Query(in _stateMachineQuery,
            (ref AnimationState state, ref AnimationRenderer renderer,
             ref AnimStateMachineRef smRef, ref Velocity vel, ref Facing facing) =>
        {
            var sm = smRef.Definition;

            // Build context from components + external state
            var context = new AnimTransitionContext(
                velocity: vel,
                isGrounded: PlayerGrounded,
                justJumped: PlayerJustJumped,
                attackPressed: PlayerAttackPressed,
                hurtThisFrame: PlayerHurtThisFrame,
                isDead: PlayerIsDead,
                animationFinished: state.Finished,
                animationTimer: state.Timer
            );

            // Check for state transition
            string? newState = sm.Evaluate(state.CurrentAnimation, context);
            if (newState != null && newState != state.CurrentAnimation)
            {
                state.PreviousAnimation = state.CurrentAnimation;
                state.CurrentAnimation = newState;
                state.CurrentFrame = 0;
                state.Timer = 0f;
                state.Finished = false;

                // Get state definition for properties
                var stateDef = sm.GetState(newState);
                if (stateDef != null)
                {
                    state.Looping = stateDef.Loops;
                    state.PlaybackSpeed = stateDef.SpeedMultiplier;
                    state.Locked = stateDef.LockUntilComplete;
                }

                // Resolve directional animation name
                string resolvedAnim = DirectionalAnimResolver.Resolve(
                    newState, facing.Direction, renderer.Sheet, out bool flipH);

                renderer.Sprite.Play(resolvedAnim, loopCount: state.Looping ? -1 : 0);
                renderer.Sprite.FlipHorizontally = flipH;
                renderer.Sprite.Speed = state.PlaybackSpeed;
            }
        });

        // Phase 2: Update all AnimatedSprites (both state-machine and simple)
        World.Query(in _stateMachineQuery,
            (ref AnimationState state, ref AnimationRenderer renderer,
             ref AnimStateMachineRef _, ref Velocity __, ref Facing ___) =>
        {
            UpdateSprite(ref state, ref renderer, gameTime);
        });

        World.Query(in _simpleAnimQuery,
            (ref AnimationState state, ref AnimationRenderer renderer) =>
        {
            UpdateSprite(ref state, ref renderer, gameTime);
        });

        // Phase 3: Collect frame events
        World.Query(in _eventQuery,
            (ref AnimationState state, ref AnimEventMapRef mapRef,
             ref AnimEventBuffer buffer) =>
        {
            buffer.Clear();

            // Check if frame changed this tick
            int currentFrame = state.CurrentFrame;
            var events = mapRef.Map.GetEvents(state.CurrentAnimation, currentFrame);
            foreach (var evt in events)
            {
                buffer.Add(evt);
            }
        });

        // Phase 4: Update sprite flash timers
        World.Query(in _flashQuery, (ref SpriteFlash flash) =>
        {
            if (flash.Active)
                flash.Elapsed += dt;
        });
    }

    private void UpdateSprite(ref AnimationState state, ref AnimationRenderer renderer,
        GameTime gameTime)
    {
        int prevFrame = renderer.Sprite.CurrentFrameIndex;
        renderer.Sprite.Update(gameTime);
        state.CurrentFrame = renderer.Sprite.CurrentFrameIndex;
        state.Timer += (float)gameTime.ElapsedGameTime.TotalSeconds;

        // Detect animation completion
        if (!state.Looping && !renderer.Sprite.IsAnimating)
        {
            state.Finished = true;
        }
    }
}
```

### 7.2 Event Dispatch System

A separate system reads the event buffer and executes the actual gameplay effects:

```csharp
public class AnimationEventDispatchSystem : BaseSystem<World, GameTime>
{
    private readonly QueryDescription _query = new QueryDescription()
        .WithAll<AnimEventBuffer, Position>();

    private readonly AudioManager _audio;
    private readonly EffectSpawner _effects;
    private readonly CameraShaker _cameraShaker;

    public AnimationEventDispatchSystem(World world, AudioManager audio,
        EffectSpawner effects, CameraShaker cameraShaker) : base(world)
    {
        _audio = audio;
        _effects = effects;
        _cameraShaker = cameraShaker;
    }

    public override void Update(in GameTime gameTime)
    {
        World.Query(in _query, (ref AnimEventBuffer buffer, ref Position pos) =>
        {
            for (int i = 0; i < buffer.Count; i++)
            {
                ref var evt = ref buffer.Events[i];

                switch (evt.Type)
                {
                    case AnimEventType.PlaySound:
                        _audio.Play(evt.Parameter);
                        break;

                    case AnimEventType.SpawnEffect:
                        _effects.Spawn(evt.Parameter, pos.Value);
                        break;

                    case AnimEventType.SpawnHitbox:
                        // Create a hitbox entity at the character's position
                        SpawnHitbox(pos.Value, evt.Parameter);
                        break;

                    case AnimEventType.RemoveHitbox:
                        RemoveHitbox(evt.Parameter);
                        break;

                    case AnimEventType.CameraShake:
                        float intensity = evt.Parameter switch
                        {
                            "light"  => 2f,
                            "medium" => 5f,
                            "heavy"  => 10f,
                            _ => 3f
                        };
                        _cameraShaker.Shake(intensity, 0.2f);
                        break;

                    case AnimEventType.SpawnProjectile:
                        SpawnProjectile(pos.Value, evt.Parameter);
                        break;
                }
            }
        });
    }

    private void SpawnHitbox(Vector2 position, string hitboxType) { /* ... */ }
    private void RemoveHitbox(string hitboxType) { /* ... */ }
    private void SpawnProjectile(Vector2 position, string projectileType) { /* ... */ }
}
```

### 7.3 Animation Render System

```csharp
public class AnimationRenderSystem : BaseSystem<World, GameTime>
{
    private readonly QueryDescription _query = new QueryDescription()
        .WithAll<Position, AnimationRenderer, RenderLayerTag>();

    private readonly SpriteBatch _spriteBatch;

    public AnimationRenderSystem(World world, SpriteBatch spriteBatch) : base(world)
    {
        _spriteBatch = spriteBatch;
    }

    public override void Update(in GameTime gameTime)
    {
        World.Query(in _query, (Entity entity, ref Position pos,
            ref AnimationRenderer renderer, ref RenderLayerTag _) =>
        {
            Vector2 drawPos = new(MathF.Round(pos.X), MathF.Round(pos.Y));

            // Apply flash effect if present
            Color tint = Color.White;
            if (World.Has<SpriteFlash>(entity))
            {
                ref var flash = ref World.Get<SpriteFlash>(entity);
                if (flash.Active)
                {
                    // Alternate between white and normal for flicker effect
                    float t = flash.Elapsed / flash.Duration;
                    bool showFlash = ((int)(t * 10) % 2) == 0;
                    tint = showFlash ? flash.FlashColor : Color.White;
                }
            }

            renderer.Sprite.Color = tint;
            renderer.Sprite.Draw(_spriteBatch, drawPos);
        });
    }
}
```

### 7.4 System Execution Order

```csharp
// In your main game loop — order matters!
// 1. Input → gameplay state (velocity, grounded, etc.)
inputSystem.Update(gameTime);
physicsSystem.Update(gameTime);

// 2. Set context on animation system
animationSystem.PlayerGrounded = playerGrounded;
animationSystem.PlayerJustJumped = justJumped;
animationSystem.PlayerAttackPressed = attackPressed;
animationSystem.PlayerHurtThisFrame = hurtThisFrame;
animationSystem.PlayerIsDead = isDead;

// 3. Animation state machine + sprite update
animationSystem.Update(gameTime);

// 4. Fire animation events (sounds, hitboxes, effects)
eventDispatchSystem.Update(gameTime);

// 5. Render
animationRenderSystem.Update(gameTime);
```

### 7.5 Per-Entity Context (Multi-Character)

The example above passes global player state. For multiple characters with their own grounded/jumping state, use components:

```csharp
/// <summary>
/// Per-entity gameplay state that the animation system reads.
/// Set by physics/gameplay systems before animation runs.
/// </summary>
public record struct CharacterMotionState
{
    public bool IsGrounded;
    public bool JustJumped;
    public bool AttackPressed;
    public bool HurtThisFrame;
    public bool IsDead;
}

// Updated query includes CharacterMotionState
private readonly QueryDescription _perEntityQuery = new QueryDescription()
    .WithAll<AnimationState, AnimationRenderer, AnimStateMachineRef,
             Velocity, Facing, CharacterMotionState>();

// In the query callback:
World.Query(in _perEntityQuery,
    (ref AnimationState state, ref AnimationRenderer renderer,
     ref AnimStateMachineRef smRef, ref Velocity vel, ref Facing facing,
     ref CharacterMotionState motion) =>
{
    var context = new AnimTransitionContext(
        velocity: vel,
        isGrounded: motion.IsGrounded,
        justJumped: motion.JustJumped,
        attackPressed: motion.AttackPressed,
        hurtThisFrame: motion.HurtThisFrame,
        isDead: motion.IsDead,
        animationFinished: state.Finished,
        animationTimer: state.Timer
    );
    // ... evaluate transitions as before
});
```

---

## 8. Common Animation Patterns

### 8.1 Hit Flash (White Shader)

The classic "flash white when damaged" effect. Two approaches:

**Approach A: Color tint cycling (no shader needed)**
```csharp
// When entity takes damage — add/reset SpriteFlash component
world.Set(entity, new SpriteFlash
{
    Duration = 0.3f,
    Elapsed = 0f,
    FlashColor = Color.White
});

// In render system (already shown above) — alternate tint
```

**Approach B: Custom shader (full white override)**
```hlsl
// HitFlash.fx — forces all visible pixels to white
sampler2D SpriteTexture : register(s0);
float FlashAmount;  // 0 = normal, 1 = full white

float4 PixelShaderFunction(float2 texCoord : TEXCOORD0, float4 color : COLOR0) : COLOR0
{
    float4 tex = tex2D(SpriteTexture, texCoord);
    float3 flashed = lerp(tex.rgb, float3(1, 1, 1), FlashAmount);
    return float4(flashed * color.rgb, tex.a * color.a);
}

technique HitFlash
{
    pass Pass0
    {
        PixelShader = compile ps_3_0 PixelShaderFunction();
    }
}
```

```csharp
// C# — use flash shader during hit
_hitFlashEffect.Parameters["FlashAmount"].SetValue(flashActive ? 1f : 0f);
_spriteBatch.Begin(effect: _hitFlashEffect, samplerState: SamplerState.PointClamp);
animatedSprite.Draw(_spriteBatch, position);
_spriteBatch.End();
```

### 8.2 Squash & Stretch

Apply scale transforms to add life to animations. Works on top of sprite animation:

```csharp
/// <summary>
/// Component for dynamic squash/stretch effects.
/// </summary>
public record struct SquashStretch
{
    public Vector2 Scale;       // current scale (1,1 = normal)
    public Vector2 Target;      // scale we're returning to (usually 1,1)
    public float ReturnSpeed;   // how fast to spring back

    public static SquashStretch Default() => new()
    {
        Scale = Vector2.One,
        Target = Vector2.One,
        ReturnSpeed = 12f
    };

    /// <summary>
    /// Apply a squash (wide + short) — used on landing, getting hit.
    /// </summary>
    public void Squash(float amount = 0.3f)
    {
        Scale = new Vector2(1f + amount, 1f - amount);
    }

    /// <summary>
    /// Apply a stretch (tall + narrow) — used on jump launch.
    /// </summary>
    public void Stretch(float amount = 0.3f)
    {
        Scale = new Vector2(1f - amount, 1f + amount);
    }
}

// Update system — lerp back to normal
public class SquashStretchSystem : BaseSystem<World, GameTime>
{
    private readonly QueryDescription _query = new QueryDescription()
        .WithAll<SquashStretch>();

    public SquashStretchSystem(World world) : base(world) { }

    public override void Update(in GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        World.Query(in _query, (ref SquashStretch ss) =>
        {
            ss.Scale = Vector2.Lerp(ss.Scale, ss.Target, ss.ReturnSpeed * dt);
        });
    }
}

// In render — apply scale around sprite origin
world.Query(in _renderQuery, (ref Position pos, ref AnimationRenderer renderer,
    ref SquashStretch ss) =>
{
    var origin = new Vector2(
        renderer.Sprite.Width / 2f,
        renderer.Sprite.Height  // bottom-center origin for ground characters
    );

    _spriteBatch.Draw(
        renderer.Sprite.TextureRegion.Texture,
        MathF.Round(pos.X), MathF.Round(pos.Y),
        renderer.Sprite.TextureRegion.Bounds,
        Color.White,
        0f,             // rotation
        origin,
        ss.Scale,       // ← squash/stretch applied here
        SpriteEffects.None,
        0f
    );
});
```

**When to trigger:**

| Event | Effect | Amount |
|---|---|---|
| Jump launch | Stretch | `Stretch(0.25f)` |
| Landing | Squash | `Squash(0.3f)` |
| Taking damage | Squash | `Squash(0.15f)` |
| Attacking (anticipation frame) | Squash | `Squash(0.1f)` |
| Attack swing | Stretch | `Stretch(0.2f)` |
| Bouncing | Alternate | Squash on impact, stretch on bounce |

### 8.3 Anticipation Frames

Professional animation uses anticipation: a brief wind-up before the main action. In 2D game animation, this means:

```csharp
// In Aseprite: structure your attack animation as:
// Frame 0-1: Anticipation (wind-up, character pulls back)
// Frame 2:   Active frame (swing starts)
// Frame 3-4: Active frames (hitbox is live)
// Frame 5-6: Recovery (follow-through)
//
// Set frame durations in Aseprite:
// - Anticipation frames: 80-100ms each (slight pause builds tension)
// - Active frames: 50-60ms each (fast, impactful)
// - Recovery frames: 80-100ms each (return to neutral)
```

**In code, lock the character during anticipation:**
```csharp
// The state machine already handles this via LockUntilComplete.
// For movement lockout during anticipation specifically:
sm.AddState(new AnimStateDefinition
{
    Name = "attack",
    Loops = false,
    Priority = 3,
    LockUntilComplete = true,  // can't interrupt with movement
    FallbackState = "idle"
});
```

### 8.4 Animation Canceling

Action games let players cancel recovery frames into other actions. This creates responsive, advanced gameplay:

```csharp
// Attack state with partial lock:
// Locked during anticipation + active frames (0-4),
// but can be canceled during recovery (frames 5+)
sm.AddTransition(new AnimTransition
{
    FromState = "attack",
    ToState = "jump",
    Condition = ctx => ctx.JustJumped && ctx.AnimationTimer > 0.25f,
    // Only allow cancel after anticipation + active frames
    Priority = 5
});

// Dash-cancel: cancel attack recovery into dash
sm.AddTransition(new AnimTransition
{
    FromState = "attack",
    ToState = "dash",
    Condition = ctx => ctx.DashPressed && ctx.AnimationTimer > 0.3f,
    Priority = 5
});

// Attack combo: chain attack into attack2 during late frames
sm.AddTransition(new AnimTransition
{
    FromState = "attack",
    ToState = "attack2",
    Condition = ctx => ctx.AttackPressed && ctx.AnimationTimer > 0.2f,
    Priority = 4
});
```

### 8.5 Speed-Based Animation Playback

Match animation speed to movement speed for natural-looking walk/run cycles:

```csharp
// In animation update system
world.Query(in _speedQuery, (ref AnimationState state,
    ref AnimationRenderer renderer, ref Velocity vel) =>
{
    if (state.CurrentAnimation == "run")
    {
        // Scale animation speed proportional to movement speed
        float moveSpeed = MathF.Abs(vel.X);
        float normalSpeed = 150f; // pixels/sec at which anim plays at 1x
        renderer.Sprite.Speed = MathF.Max(0.3f, moveSpeed / normalSpeed);
    }
});
```

### 8.6 Screen Shake on Animation Events

```csharp
public class CameraShaker
{
    private float _trauma;        // 0 to 1
    private float _decay = 3f;    // trauma per second decay
    private readonly Random _rng = new();

    public Vector2 Offset { get; private set; }

    /// <summary>
    /// Add trauma (0-1). Values above 1 are clamped.
    /// </summary>
    public void Shake(float intensity, float duration)
    {
        _trauma = MathF.Min(_trauma + intensity * 0.1f, 1f);
    }

    public void Update(float dt)
    {
        if (_trauma <= 0)
        {
            Offset = Vector2.Zero;
            return;
        }

        // Shake amount = trauma² (quadratic for natural feel)
        float shake = _trauma * _trauma;
        float maxOffset = shake * 8f; // max pixels of offset

        Offset = new Vector2(
            (float)(_rng.NextDouble() * 2 - 1) * maxOffset,
            (float)(_rng.NextDouble() * 2 - 1) * maxOffset
        );

        _trauma = MathF.Max(0, _trauma - _decay * dt);
    }
}

// Apply in camera transform
Matrix cameraTransform = _camera.TransformMatrix *
    Matrix.CreateTranslation(_cameraShaker.Offset.X, _cameraShaker.Offset.Y, 0);
```

---

## 9. MonoGame.Extended Alternative (Non-Aseprite)

If you're not using Aseprite, [MonoGame.Extended](https://github.com/craftworkgames/MonoGame.Extended) (v5.3.1) provides its own animation system based on texture atlases and JSON definitions.

### 9.1 SpriteSheet + AnimatedSprite (Extended)

```csharp
using MonoGame.Extended.Graphics;
using MonoGame.Extended.Animations;

// Load a texture atlas (exported from TexturePacker or similar)
Texture2D atlasTexture = Content.Load<Texture2D>("sprites/characters");

// Define regions manually or from JSON
var atlas = new TextureAtlas("characters", atlasTexture);
atlas.CreateRegion("idle_0", 0, 0, 32, 32);
atlas.CreateRegion("idle_1", 32, 0, 32, 32);
atlas.CreateRegion("idle_2", 64, 0, 32, 32);
atlas.CreateRegion("idle_3", 96, 0, 32, 32);
atlas.CreateRegion("run_0", 0, 32, 32, 32);
atlas.CreateRegion("run_1", 32, 32, 32, 32);
atlas.CreateRegion("run_2", 64, 32, 32, 32);
atlas.CreateRegion("run_3", 96, 32, 32, 32);
atlas.CreateRegion("run_4", 128, 32, 32, 32);
atlas.CreateRegion("run_5", 160, 32, 32, 32);
```

### 9.2 Creating Animations from Atlas Regions

```csharp
// Create a SpriteSheet with named animations
var spriteSheet = new SpriteSheet("PlayerSheet", atlasTexture);

// Define animations with frame indices and durations
spriteSheet.DefineAnimation("idle", builder =>
{
    builder.IsLooping(true);
    builder.AddFrame(atlas["idle_0"], TimeSpan.FromMilliseconds(150));
    builder.AddFrame(atlas["idle_1"], TimeSpan.FromMilliseconds(150));
    builder.AddFrame(atlas["idle_2"], TimeSpan.FromMilliseconds(150));
    builder.AddFrame(atlas["idle_3"], TimeSpan.FromMilliseconds(150));
});

spriteSheet.DefineAnimation("run", builder =>
{
    builder.IsLooping(true);
    builder.AddFrame(atlas["run_0"], TimeSpan.FromMilliseconds(80));
    builder.AddFrame(atlas["run_1"], TimeSpan.FromMilliseconds(80));
    builder.AddFrame(atlas["run_2"], TimeSpan.FromMilliseconds(80));
    builder.AddFrame(atlas["run_3"], TimeSpan.FromMilliseconds(80));
    builder.AddFrame(atlas["run_4"], TimeSpan.FromMilliseconds(80));
    builder.AddFrame(atlas["run_5"], TimeSpan.FromMilliseconds(80));
});

// Create AnimatedSprite
var animatedSprite = new AnimatedSprite(spriteSheet, "idle");
```

### 9.3 Playback

```csharp
// Update
animatedSprite.Update(gameTime);

// Switch animation
animatedSprite.SetAnimation("run");

// Draw
spriteBatch.Draw(animatedSprite, position);
```

### 9.4 When to Use Extended vs Aseprite

| Scenario | Use Aseprite Pipeline | Use Extended |
|---|---|---|
| Art made in Aseprite | ✅ Direct `.ase` loading | ❌ Extra export step |
| Art from other tools (Photoshop, GIMP) | ❌ Can't read PSD/XCF | ✅ Atlas + JSON |
| Programmer art / placeholders | Either works | ✅ Simpler setup |
| Complex frame timings per-frame | ✅ Set in Aseprite GUI | ⚠️ Manual in code |
| Animation tags | ✅ Built-in | ⚠️ Manual definition |
| Multiple characters, same workflow | ✅ One file per character | ✅ One atlas per character |

> **Recommendation:** If you use Aseprite for art (most indie pixel-art games do), use MonoGame.Aseprite. It eliminates the entire export pipeline and lets artists iterate without rebuilding. Use Extended's approach only when receiving pre-exported sprite sheets from external tools.

---

## 10. Practical Example — Complete Platformer Character

This ties everything together: a full platformer player character with idle/run/jump/fall/attack/hurt/death states.

### 10.1 Aseprite File Setup

In Aseprite, create `player.aseprite` with these tags:

| Tag | Frames | Duration/Frame | Loop | Notes |
|---|---|---|---|---|
| `idle` | 4 | 150ms | ∞ | Breathing animation |
| `run` | 6 | 80ms | ∞ | Full run cycle |
| `jump` | 3 | 80ms | 1× | Launch + rising |
| `fall` | 2 | 120ms | ∞ | Falling loop |
| `land` | 3 | 60ms | 1× | Landing squash |
| `attack` | 7 | varies | 1× | Wind-up → swing → recovery |
| `hurt` | 3 | 80ms | 1× | Knockback reaction |
| `death` | 6 | 120ms | 1× | Death animation |

### 10.2 Component Setup

```csharp
public static class PlayerFactory
{
    // Shared across all player entities
    private static AnimationStateMachineDefinition? _stateMachine;
    private static AnimationEventMap? _eventMap;

    public static Entity Create(World world, GraphicsDevice graphicsDevice,
        ContentManager content, Vector2 spawnPosition)
    {
        // Load Aseprite file
        var aseFile = content.Load<AsepriteFile>("sprites/player");
        var sheet = aseFile.CreateSpriteSheet(graphicsDevice);
        var sprite = sheet.CreateAnimatedSprite("idle");

        // Create shared definitions (once)
        _stateMachine ??= PlayerAnimations.Create();
        _eventMap ??= PlayerAnimEvents.Create();

        // Create entity with all components
        return world.Create(
            new Position(spawnPosition),
            new Velocity(Vector2.Zero),
            new AnimationRenderer(sprite, sheet),
            AnimationState.Default("idle"),
            new AnimStateMachineRef(_stateMachine),
            new AnimEventMapRef(_eventMap),
            AnimEventBuffer.Create(),
            new Facing(FaceDirection.Right),
            new CharacterMotionState(),
            SquashStretch.Default(),
            new RenderLayerTag(20),
            new SortOrder(0f),
            new PlayerTag()  // marker component
        );
    }
}
```

### 10.3 State Machine Definition

```csharp
public static class PlayerAnimations
{
    public static AnimationStateMachineDefinition Create()
    {
        var sm = new AnimationStateMachineDefinition("idle");

        // ─── States ───
        sm.AddState(new AnimStateDefinition
        {
            Name = "idle", Loops = true, Priority = 0
        });
        sm.AddState(new AnimStateDefinition
        {
            Name = "run", Loops = true, Priority = 0
        });
        sm.AddState(new AnimStateDefinition
        {
            Name = "jump", Loops = false, Priority = 1,
            FallbackState = "fall"
        });
        sm.AddState(new AnimStateDefinition
        {
            Name = "fall", Loops = true, Priority = 1
        });
        sm.AddState(new AnimStateDefinition
        {
            Name = "land", Loops = false, Priority = 2,
            LockUntilComplete = true, FallbackState = "idle",
            SpeedMultiplier = 1.5f  // quick landing
        });
        sm.AddState(new AnimStateDefinition
        {
            Name = "attack", Loops = false, Priority = 3,
            LockUntilComplete = true, FallbackState = "idle"
        });
        sm.AddState(new AnimStateDefinition
        {
            Name = "hurt", Loops = false, Priority = 5,
            LockUntilComplete = true, FallbackState = "idle"
        });
        sm.AddState(new AnimStateDefinition
        {
            Name = "death", Loops = false, Priority = 10,
            LockUntilComplete = true
        });

        // ─── Transitions ───

        // Idle ↔ Run
        sm.AddTransition(new AnimTransition
        {
            FromState = "idle", ToState = "run",
            Condition = ctx => MathF.Abs(ctx.Velocity.X) > 0.1f && ctx.IsGrounded
        });
        sm.AddTransition(new AnimTransition
        {
            FromState = "run", ToState = "idle",
            Condition = ctx => MathF.Abs(ctx.Velocity.X) <= 0.1f && ctx.IsGrounded
        });

        // Ground → Jump
        sm.AddTransition(new AnimTransition
        {
            FromState = "idle", ToState = "jump",
            Condition = ctx => ctx.JustJumped, Priority = 10
        });
        sm.AddTransition(new AnimTransition
        {
            FromState = "run", ToState = "jump",
            Condition = ctx => ctx.JustJumped, Priority = 10
        });
        sm.AddTransition(new AnimTransition
        {
            FromState = "land", ToState = "jump",
            Condition = ctx => ctx.JustJumped, Priority = 10
        });

        // Jump → Fall
        sm.AddTransition(new AnimTransition
        {
            FromState = "jump", ToState = "fall",
            Condition = ctx => ctx.Velocity.Y > 0 || ctx.AnimationFinished
        });

        // Walk off ledge → Fall
        sm.AddTransition(new AnimTransition
        {
            FromState = "idle", ToState = "fall",
            Condition = ctx => !ctx.IsGrounded && !ctx.JustJumped
        });
        sm.AddTransition(new AnimTransition
        {
            FromState = "run", ToState = "fall",
            Condition = ctx => !ctx.IsGrounded && !ctx.JustJumped
        });

        // Fall / Jump → Land
        sm.AddTransition(new AnimTransition
        {
            FromState = "fall", ToState = "land",
            Condition = ctx => ctx.IsGrounded, Priority = 5
        });
        sm.AddTransition(new AnimTransition
        {
            FromState = "jump", ToState = "land",
            Condition = ctx => ctx.IsGrounded && ctx.Velocity.Y >= 0, Priority = 5
        });

        // Attack from ground
        sm.AddTransition(new AnimTransition
        {
            FromState = "idle", ToState = "attack",
            Condition = ctx => ctx.AttackPressed && ctx.IsGrounded, Priority = 3
        });
        sm.AddTransition(new AnimTransition
        {
            FromState = "run", ToState = "attack",
            Condition = ctx => ctx.AttackPressed && ctx.IsGrounded, Priority = 3
        });

        // Global: death and hurt override everything
        sm.AddGlobalTransition("death", ctx => ctx.IsDead, priority: 100);
        sm.AddGlobalTransition("hurt",
            ctx => ctx.HurtThisFrame && !ctx.IsDead, priority: 50);

        return sm;
    }
}
```

### 10.4 Event Map

```csharp
public static class PlayerAnimEvents
{
    public static AnimationEventMap Create() => new AnimationEventMap()
        // ─── Run ───
        .On("run", 1, AnimEventType.PlaySound, "sfx/footstep_01")
        .On("run", 4, AnimEventType.PlaySound, "sfx/footstep_02")
        .On("run", 1, AnimEventType.SpawnEffect, "dust_puff")
        .On("run", 4, AnimEventType.SpawnEffect, "dust_puff")

        // ─── Jump ───
        .On("jump", 0, AnimEventType.PlaySound, "sfx/jump")
        .On("jump", 0, AnimEventType.SpawnEffect, "jump_dust")

        // ─── Land ───
        .On("land", 0, AnimEventType.PlaySound, "sfx/land")
        .On("land", 0, AnimEventType.SpawnEffect, "land_dust")
        .On("land", 0, AnimEventType.CameraShake, "light")

        // ─── Attack ───
        // Frame 0-1: anticipation (wind-up)
        // Frame 2: active starts
        .On("attack", 2, AnimEventType.PlaySound, "sfx/sword_swing")
        .On("attack", 2, AnimEventType.SpawnHitbox, "sword_hitbox")
        .On("attack", 4, AnimEventType.RemoveHitbox, "sword_hitbox")
        .On("attack", 2, AnimEventType.CameraShake, "light")

        // ─── Hurt ───
        .On("hurt", 0, AnimEventType.PlaySound, "sfx/player_hurt")
        .On("hurt", 0, AnimEventType.CameraShake, "medium")

        // ─── Death ───
        .On("death", 0, AnimEventType.PlaySound, "sfx/player_death")
        .On("death", 0, AnimEventType.CameraShake, "heavy");
}
```

### 10.5 Gameplay Integration System

```csharp
/// <summary>
/// Bridges gameplay state to animation components.
/// Runs BEFORE AnimationSystem.
/// </summary>
public class PlayerGameplayBridgeSystem : BaseSystem<World, GameTime>
{
    private readonly QueryDescription _query = new QueryDescription()
        .WithAll<PlayerTag, Velocity, CharacterMotionState, SquashStretch,
                 AnimationState, Facing>();

    // These flags come from your input + physics systems
    public bool IsGrounded { get; set; }
    public bool WasGroundedLastFrame { get; set; }
    public bool JustJumped { get; set; }
    public bool AttackPressed { get; set; }
    public bool HurtThisFrame { get; set; }
    public bool IsDead { get; set; }

    public PlayerGameplayBridgeSystem(World world) : base(world) { }

    public override void Update(in GameTime gameTime)
    {
        World.Query(in _query, (ref Velocity vel, ref CharacterMotionState motion,
            ref SquashStretch ss, ref AnimationState animState, ref Facing facing) =>
        {
            // Update motion state for animation system
            motion.IsGrounded = IsGrounded;
            motion.JustJumped = JustJumped;
            motion.AttackPressed = AttackPressed;
            motion.HurtThisFrame = HurtThisFrame;
            motion.IsDead = IsDead;

            // Squash/stretch triggers
            if (JustJumped)
                ss.Stretch(0.25f);

            if (IsGrounded && !WasGroundedLastFrame)
                ss.Squash(0.3f);  // landing squash

            if (HurtThisFrame)
                ss.Squash(0.15f);

            // Update facing from velocity (side-scroller)
            if (MathF.Abs(vel.X) > 0.1f)
            {
                facing.Direction = vel.X > 0
                    ? FaceDirection.Right
                    : FaceDirection.Left;
            }
        });
    }
}
```

### 10.6 Full System Registration

```csharp
public class MyGame : Game
{
    private World _world;
    private AnimationSystem _animationSystem;
    private AnimationEventDispatchSystem _eventSystem;
    private AnimationRenderSystem _renderSystem;
    private SquashStretchSystem _squashSystem;
    private PlayerGameplayBridgeSystem _bridgeSystem;

    protected override void Initialize()
    {
        _world = World.Create();

        _bridgeSystem = new PlayerGameplayBridgeSystem(_world);
        _animationSystem = new AnimationSystem(_world);
        _squashSystem = new SquashStretchSystem(_world);
        _eventSystem = new AnimationEventDispatchSystem(
            _world, _audioManager, _effectSpawner, _cameraShaker);
        _renderSystem = new AnimationRenderSystem(_world, _spriteBatch);

        base.Initialize();
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);

        // Create player
        PlayerFactory.Create(_world, GraphicsDevice, Content, new Vector2(100, 200));
    }

    protected override void Update(GameTime gameTime)
    {
        // 1. Input + Physics (your existing systems)
        _inputSystem.Update(gameTime);
        _physicsSystem.Update(gameTime);

        // 2. Bridge gameplay → animation components
        _bridgeSystem.IsGrounded = /* from physics */;
        _bridgeSystem.WasGroundedLastFrame = /* tracked */;
        _bridgeSystem.JustJumped = /* from input */;
        _bridgeSystem.AttackPressed = /* from input */;
        _bridgeSystem.HurtThisFrame = /* from combat */;
        _bridgeSystem.IsDead = /* from health */;
        _bridgeSystem.Update(gameTime);

        // 3. Animation state machine + sprite advancement
        _animationSystem.Update(gameTime);

        // 4. Squash/stretch spring-back
        _squashSystem.Update(gameTime);

        // 5. Dispatch animation events (sounds, hitboxes, effects)
        _eventSystem.Update(gameTime);

        // 6. Camera shake update
        _cameraShaker.Update((float)gameTime.ElapsedGameTime.TotalSeconds);

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.CornflowerBlue);

        var cameraTransform = _camera.TransformMatrix *
            Matrix.CreateTranslation(_cameraShaker.Offset.X, _cameraShaker.Offset.Y, 0);

        _spriteBatch.Begin(
            sortMode: SpriteSortMode.BackToFront,
            blendState: BlendState.AlphaBlend,
            samplerState: SamplerState.PointClamp,
            transformMatrix: cameraTransform
        );

        _renderSystem.Update(gameTime);

        _spriteBatch.End();

        base.Draw(gameTime);
    }
}
```

---

## Quick Reference

### State Machine Transition Diagram

```
                      ┌────────────┐
                      │   death    │ ← global (from any state)
                      └────────────┘
                             ↑
                      ┌────────────┐
                      │    hurt    │ ← global (from any state except death)
                      └────────────┘
                             ↑
         ┌─────────────────────────────────────┐
         │                                     │
    ┌─────────┐    vel > 0    ┌─────────┐      │
    │  idle   │──────────────→│   run   │      │
    │         │←──────────────│         │      │
    └─────────┘    vel ≈ 0    └─────────┘      │
      │     │                    │    │         │
      │     │ !grounded          │    │ attack  │
      │     ↓                    │    ↓         │
      │  ┌──────┐               │  ┌────────┐  │
      │  │ fall │               │  │ attack │──┘
      │  └──────┘               │  └────────┘
      │     ↑                   │     │ fallback
      │     │ vel.Y > 0         │     ↓
      │  ┌──────┐               │  ┌──────┐
      │  │ jump │←──────────────┘  │ idle │
      │  └──────┘  justJumped      └──────┘
      │     ↑
      └─────┘ justJumped
      
      fall ──grounded──→ land ──finished──→ idle
```

### Component Architecture Summary

| Component | Type | Purpose |
|---|---|---|
| `AnimationRenderer` | `record struct` (class refs) | Holds `AnimatedSprite` + `SpriteSheet` |
| `AnimationState` | `record struct` | Current animation, frame, timer, flags |
| `AnimStateMachineRef` | `record struct` | Reference to shared state machine definition |
| `AnimEventMapRef` | `record struct` | Reference to shared event map |
| `AnimEventBuffer` | `record struct` | Frame events that fired this tick |
| `Facing` | `record struct` | Current direction (4-dir or 8-dir) |
| `CharacterMotionState` | `record struct` | Grounded, jumping, attacking, etc. |
| `SquashStretch` | `record struct` | Dynamic scale for game feel |
| `SpriteFlash` | `record struct` | Hit flash effect state |

### System Execution Order

| Order | System | Phase |
|---|---|---|
| 1 | Input / Physics | Gameplay |
| 2 | `PlayerGameplayBridgeSystem` | Bridge gameplay → animation |
| 3 | `AnimationSystem` | State machine + sprite update |
| 4 | `SquashStretchSystem` | Scale spring-back |
| 5 | `AnimationEventDispatchSystem` | Fire sounds, effects, hitboxes |
| 6 | `CameraShaker.Update()` | Shake decay |
| 7 | `AnimationRenderSystem` | Draw (in Draw phase) |

### Aseprite Tag Naming Conventions

| Pattern | Example | Usage |
|---|---|---|
| `{action}` | `idle`, `run`, `attack` | Side-scroller (flip for direction) |
| `{action}_{dir}` | `idle_down`, `run_left` | 4-directional top-down |
| `{action}_{dir}_{dir}` | `idle_down_left` | 8-directional |
| `{action}_{variant}` | `attack_1`, `attack_2` | Combo chains |

### Performance Notes

- **State machine definitions are shared** — one `AnimationStateMachineDefinition` per character type, not per entity. Zero per-entity allocation for the state machine itself.
- **Event maps are shared** — same pattern. One `AnimationEventMap` per character type.
- **`AnimatedSprite` is per-entity** — each entity needs its own instance for independent playback.
- **`SpriteSheet` can be shared** — multiple `AnimatedSprite` instances can reference one `SpriteSheet`.
- **Keep transition lists short** — typically < 10 transitions per state. The `OrderByDescending` sort is negligible for small lists.
- **Frame event lookup is O(1)** — dictionary keyed by `"animation:frame"` string.
