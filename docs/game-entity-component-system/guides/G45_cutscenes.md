# G45 — Cutscenes & Scripted Sequences



> **Category:** Guide · **Related:** [G38 Scene Management](./G38_scene_management.md) · [G20 Camera Systems](./G20_camera_systems.md) · [G41 Tweening & Easing](./G41_tweening.md) · [G10 Custom Game Systems](./G10_custom_game_systems.md) · [G31 Animation State Machines](./G31_animation_state_machines.md)

---

## 1 — What Cutscenes Are in 2D Games

A cutscene is any moment where the game takes narrative control away from the player to present a scripted sequence. Two broad approaches exist:

| Approach | Description | Trade-offs |
|---|---|---|
| **Pre-rendered** | Pre-made video files (`.mp4`, `.webm`) played back at runtime | Large file sizes, fixed resolution, can't adapt to player state |
| **In-engine** | Sequences assembled from the same sprites, cameras, and systems used during gameplay | Tiny footprint, consistent art style, can react to game state |

For indie 2D games, **in-engine cutscenes win almost every time**. Your build stays small, the art style is seamless, and you can branch based on flags the player has set.

### Common Cutscene Types

- **Dialogue sequences** — Characters talk, portraits appear, text scrolls.
- **Camera pans** — The camera sweeps across a landscape to reveal something.
- **Entity choreography** — NPCs walk to marks, perform animations, interact with objects.
- **Boss intros** — Camera zooms in, boss plays an entrance animation, health bar appears.
- **Story beats** — Screen fades, text appears, music shifts — pure mood-setting.

Everything below builds a system that can handle all of these.

---

## 2 — Timeline System

The core abstraction is a **Timeline**: an ordered collection of actions, each with a start time and duration. A **TimelinePlayer** advances elapsed time each frame and activates/updates/completes actions as time passes.

### 2.1 — CutsceneAction Base

```csharp
public abstract class CutsceneAction
{
    public float StartTime { get; set; }
    public float Duration  { get; set; }

    public bool  Started   { get; private set; }
    public bool  Completed { get; private set; }

    protected float Elapsed;

    /// <summary>Normalized progress 0→1. Clamped.</summary>
    protected float Progress => Duration > 0 ? Math.Clamp(Elapsed / Duration, 0f, 1f) : 1f;

    public void Begin(World world)
    {
        Started = true;
        OnStart(world);
    }

    public void Advance(World world, float dt)
    {
        Elapsed += dt;
        OnUpdate(world, dt);

        if (Elapsed >= Duration && !Completed)
        {
            Completed = true;
            OnComplete(world);
        }
    }

    /// <summary>Force-complete for skip. Subclasses apply final state here.</summary>
    public void ForceComplete(World world)
    {
        if (!Started)  Begin(world);
        if (!Completed)
        {
            Elapsed   = Duration;
            Completed = true;
            OnComplete(world);
        }
    }

    protected virtual void OnStart(World world)    { }
    protected virtual void OnUpdate(World world, float dt) { }
    protected virtual void OnComplete(World world) { }
}
```

### 2.2 — Timeline Data Structure

A `CutsceneTimeline` holds actions grouped into **parallel tracks**. Actions within a track are sequential; tracks run in parallel.

```csharp
public enum TrackKind { Camera, Entity, Dialogue, Audio, General }

public sealed class CutsceneTrack
{
    public TrackKind Kind { get; init; }
    public List<CutsceneAction> Actions { get; init; } = new();
}

public sealed class CutsceneTimeline
{
    public string Id   { get; init; } = string.Empty;
    public List<CutsceneTrack> Tracks { get; init; } = new();

    /// <summary>Total duration = latest action end across all tracks.</summary>
    public float TotalDuration =>
        Tracks.SelectMany(t => t.Actions)
              .Select(a => a.StartTime + a.Duration)
              .DefaultIfEmpty(0f)
              .Max();
}
```

### 2.3 — Timeline Player

```csharp
public sealed class TimelinePlayer
{
    readonly CutsceneTimeline _timeline;
    readonly World _world;

    float _elapsed;
    bool  _finished;

    public bool  IsFinished => _finished;
    public float Elapsed    => _elapsed;

    public TimelinePlayer(CutsceneTimeline timeline, World world)
    {
        _timeline = timeline;
        _world    = world;
    }

    public void Update(float dt)
    {
        if (_finished) return;

        _elapsed += dt;

        foreach (var track in _timeline.Tracks)
        {
            foreach (var action in track.Actions)
            {
                // Time to start?
                if (!action.Started && _elapsed >= action.StartTime)
                    action.Begin(_world);

                // Currently active?
                if (action.Started && !action.Completed)
                    action.Advance(_world, dt);
            }
        }

        // All done?
        _finished = _timeline.Tracks
            .SelectMany(t => t.Actions)
            .All(a => a.Completed);
    }

    /// <summary>Skip to end — force-completes every action in order.</summary>
    public void Skip()
    {
        foreach (var track in _timeline.Tracks)
            foreach (var action in track.Actions)
                action.ForceComplete(_world);

        _finished = true;
    }
}
```

---

## 3 — Cutscene Actions

Each concrete action overrides `OnStart`, `OnUpdate`, and/or `OnComplete` from the base class.

### 3.1 — Wait / WaitForInput

```csharp
public sealed class WaitAction : CutsceneAction
{
    // Duration is set via StartTime/Duration — nothing special to do.
}

public sealed class WaitForInputAction : CutsceneAction
{
    bool _received;

    protected override void OnUpdate(World world, float dt)
    {
        // Check for confirm key each frame
        if (InputHelper.JustPressed(Keys.Space) || InputHelper.JustPressed(Buttons.A))
            _received = true;
    }

    // Override: don't use time-based completion
    public new bool Completed => _received;

    protected override void OnComplete(World world) { }
}
```

### 3.2 — Entity Actions

```csharp
public sealed class MoveEntityAction : CutsceneAction
{
    public Entity Target   { get; init; }
    public Vector2 GoalPos { get; init; }

    Vector2 _startPos;

    protected override void OnStart(World world)
    {
        ref var pos = ref world.Get<Position>(Target);
        _startPos = pos.Value;
    }

    protected override void OnUpdate(World world, float dt)
    {
        ref var pos = ref world.Get<Position>(Target);
        float t = EaseFunc.SmoothStep(Progress);
        pos.Value = Vector2.Lerp(_startPos, GoalPos, t);
    }

    protected override void OnComplete(World world)
    {
        ref var pos = ref world.Get<Position>(Target);
        pos.Value = GoalPos;
    }
}

public sealed class PlayAnimationAction : CutsceneAction
{
    public Entity Target       { get; init; }
    public string AnimationKey { get; init; } = "";

    protected override void OnStart(World world)
    {
        ref var anim = ref world.Get<AnimationState>(Target);
        anim.CurrentKey = AnimationKey;
        anim.Frame      = 0;
        anim.Elapsed    = 0f;
    }
}

public sealed class SpawnEntityAction : CutsceneAction
{
    public string Prefab   { get; init; } = "";
    public Vector2 AtPos   { get; init; }

    protected override void OnStart(World world)
    {
        PrefabFactory.Spawn(world, Prefab, AtPos);
    }
}

public sealed class DestroyEntityAction : CutsceneAction
{
    public Entity Target { get; init; }

    protected override void OnStart(World world)
    {
        world.Destroy(Target);
    }
}
```

### 3.3 — Camera Actions

```csharp
public sealed class PanCameraAction : CutsceneAction
{
    public Vector2 GoalPos { get; init; }

    Vector2 _startPos;

    protected override void OnStart(World world)
    {
        _startPos = CameraRig.Position;
    }

    protected override void OnUpdate(World world, float dt)
    {
        float t = EaseFunc.SmoothStep(Progress);
        CameraRig.Position = Vector2.Lerp(_startPos, GoalPos, t);
    }

    protected override void OnComplete(World world)
    {
        CameraRig.Position = GoalPos;
    }
}

public sealed class ZoomCameraAction : CutsceneAction
{
    public float GoalZoom { get; init; }

    float _startZoom;

    protected override void OnStart(World world)
    {
        _startZoom = CameraRig.Zoom;
    }

    protected override void OnUpdate(World world, float dt)
    {
        CameraRig.Zoom = MathHelper.Lerp(_startZoom, GoalZoom, EaseFunc.SmoothStep(Progress));
    }

    protected override void OnComplete(World world)
    {
        CameraRig.Zoom = GoalZoom;
    }
}

public sealed class ShakeCameraAction : CutsceneAction
{
    public float Intensity { get; init; } = 4f;

    protected override void OnUpdate(World world, float dt)
    {
        float fade = 1f - Progress;
        CameraRig.Offset = new Vector2(
            (Random.Shared.NextSingle() - 0.5f) * 2f * Intensity * fade,
            (Random.Shared.NextSingle() - 0.5f) * 2f * Intensity * fade
        );
    }

    protected override void OnComplete(World world)
    {
        CameraRig.Offset = Vector2.Zero;
    }
}
```

### 3.4 — Screen & Audio Actions

```csharp
public sealed class FadeScreenAction : CutsceneAction
{
    public bool FadeOut { get; init; } = true; // true = go to black

    protected override void OnUpdate(World world, float dt)
    {
        float alpha = FadeOut ? Progress : 1f - Progress;
        ScreenOverlay.FadeAlpha = alpha;
    }

    protected override void OnComplete(World world)
    {
        ScreenOverlay.FadeAlpha = FadeOut ? 1f : 0f;
    }
}

public sealed class PlaySoundAction : CutsceneAction
{
    public string SoundId { get; init; } = "";

    protected override void OnStart(World world)
    {
        AudioManager.PlaySfx(SoundId);
    }
}

public sealed class SetFlagAction : CutsceneAction
{
    public string Flag  { get; init; } = "";
    public bool   Value { get; init; } = true;

    protected override void OnStart(World world)
    {
        GameFlags.Set(Flag, Value);
    }
}
```

### 3.5 — Dialogue Action

```csharp
public sealed class ShowDialogueAction : CutsceneAction
{
    public string DialogueNodeId { get; init; } = "";

    bool _dialogueDone;

    protected override void OnStart(World world)
    {
        DialogueRunner.Start(DialogueNodeId, onComplete: () => _dialogueDone = true);
    }

    protected override void OnUpdate(World world, float dt)
    {
        // Duration is ignored — we wait for the dialogue system to finish
        if (_dialogueDone)
        {
            Elapsed = Duration; // triggers base completion
        }
    }
}
```

---

## 4 — Data-Driven Cutscenes

Hardcoding cutscenes in C# works for prototyping, but data-driven JSON lets designers iterate without recompiling.

### 4.1 — JSON Schema

```json
{
  "id": "boss_intro_forest",
  "tracks": [
    {
      "kind": "Camera",
      "actions": [
        { "type": "PanCamera",   "start": 0.0, "duration": 2.0, "goalX": 800, "goalY": 300 },
        { "type": "ZoomCamera",  "start": 2.0, "duration": 1.0, "goalZoom": 1.5 },
        { "type": "ShakeCamera", "start": 3.0, "duration": 0.5, "intensity": 6 }
      ]
    },
    {
      "kind": "Entity",
      "actions": [
        { "type": "PlayAnimation", "start": 0.0, "duration": 2.0, "entity": "forest_boss", "animKey": "emerge" },
        { "type": "MoveEntity",    "start": 2.0, "duration": 1.5, "entity": "forest_boss", "goalX": 750, "goalY": 320 }
      ]
    },
    {
      "kind": "Dialogue",
      "actions": [
        { "type": "ShowDialogue", "start": 3.5, "duration": 99, "nodeId": "boss_taunt_01" }
      ]
    },
    {
      "kind": "Audio",
      "actions": [
        { "type": "PlaySound", "start": 0.0, "duration": 0, "soundId": "rumble_deep" },
        { "type": "PlaySound", "start": 3.0, "duration": 0, "soundId": "boss_roar" }
      ]
    },
    {
      "kind": "General",
      "actions": [
        { "type": "SetFlag",     "start": 0.0, "duration": 0, "flag": "forest_boss_seen", "value": true },
        { "type": "FadeScreen",  "start": 5.5, "duration": 0.5, "fadeOut": false }
      ]
    }
  ]
}
```

### 4.2 — JSON Loader

```csharp
public static class CutsceneLoader
{
    public static CutsceneTimeline Load(string jsonPath, World world)
    {
        var json = File.ReadAllText(jsonPath);
        var doc  = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var timeline = new CutsceneTimeline { Id = root.GetProperty("id").GetString()! };

        foreach (var trackEl in root.GetProperty("tracks").EnumerateArray())
        {
            var kind  = Enum.Parse<TrackKind>(trackEl.GetProperty("kind").GetString()!);
            var track = new CutsceneTrack { Kind = kind };

            foreach (var actEl in trackEl.GetProperty("actions").EnumerateArray())
            {
                var action = ParseAction(actEl, world);
                track.Actions.Add(action);
            }

            timeline.Tracks.Add(track);
        }

        return timeline;
    }

    static CutsceneAction ParseAction(JsonElement el, World world)
    {
        string type  = el.GetProperty("type").GetString()!;
        float start  = el.GetProperty("start").GetSingle();
        float dur    = el.GetProperty("duration").GetSingle();

        CutsceneAction action = type switch
        {
            "PanCamera"     => new PanCameraAction
            {
                GoalPos = new Vector2(el.GetProperty("goalX").GetSingle(),
                                      el.GetProperty("goalY").GetSingle())
            },
            "ZoomCamera"    => new ZoomCameraAction
            {
                GoalZoom = el.GetProperty("goalZoom").GetSingle()
            },
            "ShakeCamera"   => new ShakeCameraAction
            {
                Intensity = el.GetProperty("intensity").GetSingle()
            },
            "MoveEntity"    => new MoveEntityAction
            {
                Target  = EntityLookup.Find(world, el.GetProperty("entity").GetString()!),
                GoalPos = new Vector2(el.GetProperty("goalX").GetSingle(),
                                      el.GetProperty("goalY").GetSingle())
            },
            "PlayAnimation" => new PlayAnimationAction
            {
                Target       = EntityLookup.Find(world, el.GetProperty("entity").GetString()!),
                AnimationKey = el.GetProperty("animKey").GetString()!
            },
            "ShowDialogue"  => new ShowDialogueAction
            {
                DialogueNodeId = el.GetProperty("nodeId").GetString()!
            },
            "PlaySound"     => new PlaySoundAction
            {
                SoundId = el.GetProperty("soundId").GetString()!
            },
            "SetFlag"       => new SetFlagAction
            {
                Flag  = el.GetProperty("flag").GetString()!,
                Value = el.GetProperty("value").GetBoolean()
            },
            "FadeScreen"    => new FadeScreenAction
            {
                FadeOut = el.GetProperty("fadeOut").GetBoolean()
            },
            "SpawnEntity"   => new SpawnEntityAction
            {
                Prefab = el.GetProperty("prefab").GetString()!,
                AtPos  = new Vector2(el.GetProperty("goalX").GetSingle(),
                                     el.GetProperty("goalY").GetSingle())
            },
            "Wait"          => new WaitAction(),
            _ => throw new NotSupportedException($"Unknown cutscene action: {type}")
        };

        action.StartTime = start;
        action.Duration  = dur;
        return action;
    }
}
```

---

## 5 — Camera Choreography

For smooth, non-linear camera paths, use **Catmull-Rom splines** so the camera glides through multiple waypoints rather than lerping between pairs. See [G20 Camera Systems](./G20_camera_systems.md) for the base camera rig.

### 5.1 — Catmull-Rom Helper

```csharp
public static class CatmullRom
{
    /// <summary>
    /// Evaluate a Catmull-Rom spline at parameter t ∈ [0,1] given four control points.
    /// </summary>
    public static Vector2 Evaluate(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;

        return 0.5f * (
            (2f * p1) +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3
        );
    }
}
```

### 5.2 — Spline Camera Path Action

```csharp
public sealed class SplineCameraAction : CutsceneAction
{
    public List<Vector2> Waypoints { get; init; } = new();

    protected override void OnUpdate(World world, float dt)
    {
        if (Waypoints.Count < 2) return;

        // Map progress across segments
        int segCount  = Waypoints.Count - 1;
        float scaled  = Progress * segCount;
        int seg       = Math.Clamp((int)scaled, 0, segCount - 1);
        float localT  = scaled - seg;

        // Clamp control-point indices
        Vector2 p0 = Waypoints[Math.Max(seg - 1, 0)];
        Vector2 p1 = Waypoints[seg];
        Vector2 p2 = Waypoints[Math.Min(seg + 1, Waypoints.Count - 1)];
        Vector2 p3 = Waypoints[Math.Min(seg + 2, Waypoints.Count - 1)];

        CameraRig.Position = CatmullRom.Evaluate(p0, p1, p2, p3, localT);
    }

    protected override void OnComplete(World world)
    {
        if (Waypoints.Count > 0)
            CameraRig.Position = Waypoints[^1];
    }
}
```

### 5.3 — Focus-On-Entity

A common pattern: lock the camera onto an entity for part of the cutscene, then release it.

```csharp
public sealed class FocusCameraOnEntityAction : CutsceneAction
{
    public Entity Target { get; init; }

    protected override void OnUpdate(World world, float dt)
    {
        ref var pos = ref world.Get<Position>(Target);
        float t = EaseFunc.SmoothStep(Math.Min(Progress * 3f, 1f)); // snap-in over first third
        CameraRig.Position = Vector2.Lerp(CameraRig.Position, pos.Value, t);
    }
}
```

---

## 6 — Entity Choreography

Entity choreography means moving NPCs to specific marks, playing animations, and setting facing directions — all timed to the cutscene.

### 6.1 — ECS Components

```csharp
public record struct Facing(int DirectionX); // -1 left, 1 right

public record struct CutsceneActor(string ActorId); // tag component for lookup
```

### 6.2 — Facing Action

```csharp
public sealed class SetFacingAction : CutsceneAction
{
    public Entity Target    { get; init; }
    public int    Direction { get; init; } // -1 or 1

    protected override void OnStart(World world)
    {
        ref var facing = ref world.Get<Facing>(Target);
        facing.DirectionX = Direction;
    }
}
```

### 6.3 — Path-Following Action

For multi-point NPC walks, reuse the same Catmull-Rom approach:

```csharp
public sealed class WalkPathAction : CutsceneAction
{
    public Entity Target          { get; init; }
    public List<Vector2> Points   { get; init; } = new();
    public string WalkAnimKey     { get; init; } = "walk";
    public string IdleAnimKey     { get; init; } = "idle";

    protected override void OnStart(World world)
    {
        ref var anim = ref world.Get<AnimationState>(Target);
        anim.CurrentKey = WalkAnimKey;
    }

    protected override void OnUpdate(World world, float dt)
    {
        if (Points.Count < 2) return;

        int segCount = Points.Count - 1;
        float scaled = Progress * segCount;
        int seg      = Math.Clamp((int)scaled, 0, segCount - 1);
        float localT = scaled - seg;

        Vector2 p0 = Points[Math.Max(seg - 1, 0)];
        Vector2 p1 = Points[seg];
        Vector2 p2 = Points[Math.Min(seg + 1, Points.Count - 1)];
        Vector2 p3 = Points[Math.Min(seg + 2, Points.Count - 1)];

        Vector2 pos = CatmullRom.Evaluate(p0, p1, p2, p3, localT);

        ref var ePos = ref world.Get<Position>(Target);

        // Auto-face movement direction
        if (pos.X > ePos.Value.X + 0.5f)
            world.Get<Facing>(Target).DirectionX = 1;
        else if (pos.X < ePos.Value.X - 0.5f)
            world.Get<Facing>(Target).DirectionX = -1;

        ePos.Value = pos;
    }

    protected override void OnComplete(World world)
    {
        ref var pos = ref world.Get<Position>(Target);
        if (Points.Count > 0)
            pos.Value = Points[^1];

        ref var anim = ref world.Get<AnimationState>(Target);
        anim.CurrentKey = IdleAnimKey;
    }
}
```

---

## 7 — Integration with Dialogue System

Cutscenes frequently pause for dialogue. The `ShowDialogueAction` (§3.5) bridges the timeline with whatever dialogue system you're running — see [G10 Custom Game Systems](./G10_custom_game_systems.md) for a full dialogue runner.

Key design points:

1. **The action starts a dialogue node** by ID and registers a completion callback.
2. **Duration is set to a large sentinel value** (e.g. `99`). The action overrides progress tracking — it only completes when the callback fires.
3. **During skip**, the dialogue action force-completes by closing the dialogue UI instantly and applying any flags/variables the dialogue would have set.

```csharp
// In ShowDialogueAction.ForceComplete override:
protected override void OnComplete(World world)
{
    DialogueRunner.ForceClose();
    // If your dialogue sets flags, apply defaults here
}
```

If your dialogue supports branching, you'll need to decide a **default branch** for skip. Common approach: store a `"skipBranch"` field in the JSON that picks the default choice.

---

## 8 — Player Control Lock

### 8.1 — CutsceneMode Component

```csharp
public record struct CutsceneMode(bool Active, float LetterboxProgress);
```

Attach this to a singleton entity (or use a static). Your `PlayerInputSystem` checks it:

```csharp
public sealed class PlayerInputSystem : BaseSystem<World, float>
{
    readonly QueryDescription _query = new QueryDescription().WithAll<PlayerTag, Velocity>();
    readonly QueryDescription _modeQuery = new QueryDescription().WithAll<CutsceneMode>();

    public PlayerInputSystem(World world) : base(world) { }

    public override void Update(in float dt)
    {
        // If any CutsceneMode is active, skip processing
        bool locked = false;
        World.Query(in _modeQuery, (ref CutsceneMode mode) =>
        {
            if (mode.Active) locked = true;
        });
        if (locked) return;

        // Normal input handling...
        World.Query(in _query, (ref Velocity vel) =>
        {
            vel.Value = InputHelper.MoveAxis * 200f;
        });
    }
}
```

### 8.2 — Letterbox Bars

Cinematic bars slide in from top and bottom. A simple approach using `ScreenOverlay`:

```csharp
public static class LetterboxRenderer
{
    const int BarHeight = 60;

    public static void Draw(SpriteBatch sb, float progress, int screenWidth, int screenHeight)
    {
        int h = (int)(BarHeight * progress);
        if (h <= 0) return;

        // Top bar
        sb.Draw(Pixel.Texture, new Rectangle(0, 0, screenWidth, h), Color.Black);
        // Bottom bar
        sb.Draw(Pixel.Texture, new Rectangle(0, screenHeight - h, screenWidth, h), Color.Black);
    }
}
```

### 8.3 — Transition In / Out

```csharp
public sealed class CutsceneTransitionAction : CutsceneAction
{
    public bool Enter { get; init; } = true; // true = entering cutscene mode

    protected override void OnStart(World world)
    {
        // If entering, lock controls immediately
        if (Enter)
            SetMode(world, active: true);
    }

    protected override void OnUpdate(World world, float dt)
    {
        ref var mode = ref GetMode(world);
        mode.LetterboxProgress = Enter ? Progress : 1f - Progress;
    }

    protected override void OnComplete(World world)
    {
        if (!Enter)
            SetMode(world, active: false);
    }

    static void SetMode(World world, bool active)
    {
        var q = new QueryDescription().WithAll<CutsceneMode>();
        world.Query(in q, (ref CutsceneMode m) => m.Active = active);
    }

    static ref CutsceneMode GetMode(World world)
    {
        ref CutsceneMode result = ref System.Runtime.CompilerServices.Unsafe.NullRef<CutsceneMode>();
        var q = new QueryDescription().WithAll<CutsceneMode>();
        world.Query(in q, (ref CutsceneMode m) => result = ref m);
        return ref result;
    }
}
```

---

## 9 — Skip System

Players should always be able to skip cutscenes. Two tiers:

| Option | Behaviour |
|---|---|
| **Fast-forward** | Run timeline at 4–8× speed. Player sees everything quickly. |
| **Full skip** | Instantly force-complete every action. Jump to post-cutscene state. |

### 9.1 — CutsceneSystem with Skip

```csharp
public sealed class CutsceneSystem : BaseSystem<World, float>
{
    TimelinePlayer? _player;

    public bool IsPlaying => _player is { IsFinished: false };

    public CutsceneSystem(World world) : base(world) { }

    public void Play(CutsceneTimeline timeline)
    {
        _player = new TimelinePlayer(timeline, World);
    }

    public override void Update(in float dt)
    {
        if (_player == null || _player.IsFinished) return;

        float speed = 1f;

        // Fast-forward: hold a button
        if (InputHelper.IsHeld(Keys.RightShift))
            speed = 6f;

        // Full skip: press a key
        if (InputHelper.JustPressed(Keys.Escape))
        {
            _player.Skip();
            return;
        }

        _player.Update(dt * speed);

        if (_player.IsFinished)
            OnCutsceneEnd();
    }

    void OnCutsceneEnd()
    {
        // Restore camera, unlock player, hide letterbox, etc.
        _player = null;
    }
}
```

### 9.2 — State Correctness After Skip

The key guarantee: **`ForceComplete` on every action must leave the world in the same state as if the cutscene had played normally.** This means every action's `OnComplete` must set final values, not deltas. The `MoveEntityAction` example above does this correctly — it sets `pos.Value = GoalPos` in `OnComplete`, regardless of how many frames elapsed.

Checklist for custom actions:

- ✅ `OnComplete` sets the absolute final state.
- ✅ `SetFlag` actions fire even on skip.
- ✅ Spawned entities exist after skip.
- ✅ Destroyed entities are gone after skip.
- ✅ Dialogue skip-branches are applied.

---

## 10 — Trigger System

Cutscenes are started by game events. A trigger evaluates a condition and fires a cutscene ID.

### 10.1 — ECS Components

```csharp
public record struct CutsceneTrigger(
    string CutsceneId,
    TriggerCondition Condition,
    bool OneShot,
    bool Fired
);

public enum TriggerCondition
{
    ZoneEnter,
    BossDefeated,
    ItemPickup,
    FirstVisit,
    FlagSet
}

public record struct TriggerZone(Rectangle Bounds);
```

### 10.2 — Trigger System

```csharp
public sealed class CutsceneTriggerSystem : BaseSystem<World, float>
{
    readonly CutsceneSystem _cutsceneSystem;
    readonly QueryDescription _triggerQuery = new QueryDescription()
        .WithAll<CutsceneTrigger, TriggerZone, Position>();
    readonly QueryDescription _playerQuery = new QueryDescription()
        .WithAll<PlayerTag, Position>();

    public CutsceneTriggerSystem(World world, CutsceneSystem cs) : base(world)
    {
        _cutsceneSystem = cs;
    }

    public override void Update(in float dt)
    {
        if (_cutsceneSystem.IsPlaying) return; // don't trigger during a cutscene

        Vector2 playerPos = Vector2.Zero;
        World.Query(in _playerQuery, (ref Position p) => playerPos = p.Value);

        World.Query(in _triggerQuery, (ref CutsceneTrigger trigger, ref TriggerZone zone, ref Position pos) =>
        {
            if (trigger.Fired && trigger.OneShot) return;

            bool shouldFire = trigger.Condition switch
            {
                TriggerCondition.ZoneEnter  => zone.Bounds.Contains(playerPos.ToPoint()),
                TriggerCondition.FlagSet    => GameFlags.Get(trigger.CutsceneId + "_ready"),
                TriggerCondition.FirstVisit => !GameFlags.Get("visited_" + trigger.CutsceneId),
                _ => false
            };

            if (shouldFire)
            {
                trigger.Fired = true;
                var timeline = CutsceneLoader.Load(
                    $"Content/Cutscenes/{trigger.CutsceneId}.json", World);
                _cutsceneSystem.Play(timeline);
            }
        });
    }
}
```

### 10.3 — One-Shot vs Repeatable

- **One-shot**: Boss intros, story events. `OneShot = true` — once `Fired` is set, it never triggers again. Persist `Fired` in your save data.
- **Repeatable**: Shop-keeper greetings, hint zones. `OneShot = false` — add a cooldown or re-enter check to avoid firing every frame.

---

## 11 — Coroutine-Based Alternative

For simple sequences — "NPC walks over, says something, walks away" — a full timeline system is overkill. The [Ellpeck/Coroutine](https://github.com/Ellpeck/Coroutine) library gives you a lightweight alternative.

### 11.1 — When to Use Which

| Use Coroutines | Use Timeline |
|---|---|
| Linear, non-branching sequences | Parallel tracks (camera + entity + audio simultaneously) |
| Quick prototyping | Data-driven / designer-authored sequences |
| One-off scripted moments | Reusable cutscene infrastructure |
| < 10 steps | Complex choreography |

### 11.2 — Setup

```
dotnet add package Coroutine --version 3.0.0
```

```csharp
using Coroutine;

// In your Game class:
private readonly CoroutineHandler _coroutineHandler = new();

protected override void Update(GameTime gameTime)
{
    _coroutineHandler.Tick(gameTime.ElapsedGameTime);
    base.Update(gameTime);
}
```

### 11.3 — Scripted Sequence Example

```csharp
public static class SimpleSequences
{
    public static IEnumerator<Wait> BossIntro(World world, Entity boss, Entity player)
    {
        // Lock player
        world.Get<CutsceneMode>(GetSingleton(world)).Active = true;

        // Fade in letterbox
        for (float t = 0; t < 0.5f; t += Time.Delta)
        {
            ScreenOverlay.LetterboxProgress = t / 0.5f;
            yield return new Wait(0);
        }

        // Pan camera to boss
        Vector2 camStart = CameraRig.Position;
        Vector2 camEnd   = world.Get<Position>(boss).Value;
        for (float t = 0; t < 1.5f; t += Time.Delta)
        {
            CameraRig.Position = Vector2.Lerp(camStart, camEnd, EaseFunc.SmoothStep(t / 1.5f));
            yield return new Wait(0);
        }

        // Boss animation
        world.Get<AnimationState>(boss).CurrentKey = "roar";
        AudioManager.PlaySfx("boss_roar");
        yield return new Wait(1.0);

        // Show dialogue — wait for it
        bool dialogueDone = false;
        DialogueRunner.Start("boss_intro_01", onComplete: () => dialogueDone = true);
        while (!dialogueDone)
            yield return new Wait(0);

        // Camera back to player
        camStart = CameraRig.Position;
        camEnd   = world.Get<Position>(player).Value;
        for (float t = 0; t < 1.0f; t += Time.Delta)
        {
            CameraRig.Position = Vector2.Lerp(camStart, camEnd, EaseFunc.SmoothStep(t / 1.0f));
            yield return new Wait(0);
        }

        // Fade out letterbox
        for (float t = 0; t < 0.5f; t += Time.Delta)
        {
            ScreenOverlay.LetterboxProgress = 1f - (t / 0.5f);
            yield return new Wait(0);
        }

        // Unlock player
        world.Get<CutsceneMode>(GetSingleton(world)).Active = false;
    }
}

// Trigger it:
_coroutineHandler.Start(SimpleSequences.BossIntro(world, bossEntity, playerEntity));
```

### 11.4 — Skip Support for Coroutines

Coroutines are harder to skip cleanly because state is embedded in the iterator. Two options:

1. **Check a skip flag** at each `yield` point — if set, break out early and apply final state manually.
2. **Wrap in a cancelable runner** that calls a finalize callback on cancel.

```csharp
public static IEnumerator<Wait> Skippable(
    IEnumerator<Wait> sequence,
    Action onSkip)
{
    while (sequence.MoveNext())
    {
        if (InputHelper.JustPressed(Keys.Escape))
        {
            onSkip();
            yield break;
        }
        yield return sequence.Current;
    }
}

// Usage:
_coroutineHandler.Start(Skippable(
    SimpleSequences.BossIntro(world, boss, player),
    onSkip: () =>
    {
        // Apply final state
        GameFlags.Set("forest_boss_seen", true);
        CameraRig.Position = world.Get<Position>(player).Value;
        world.Get<CutsceneMode>(GetSingleton(world)).Active = false;
        ScreenOverlay.LetterboxProgress = 0f;
    }
));
```

---

## Quick Reference

| Concept | Key Type / Class | Section |
|---|---|---|
| Action lifecycle | `CutsceneAction` | §2.1 |
| Parallel tracks | `CutsceneTrack`, `CutsceneTimeline` | §2.2 |
| Playback engine | `TimelinePlayer` | §2.3 |
| JSON loading | `CutsceneLoader` | §4.2 |
| Spline camera | `SplineCameraAction`, `CatmullRom` | §5 |
| NPC paths | `WalkPathAction` | §6.3 |
| Input lock | `CutsceneMode` component | §8.1 |
| Skip | `TimelinePlayer.Skip()`, `ForceComplete` | §9 |
| Triggers | `CutsceneTrigger`, `CutsceneTriggerSystem` | §10 |
| Coroutine alt | Ellpeck `CoroutineHandler` | §11 |

---

*See [G41 Tweening & Easing](./G41_tweening.md) for easing functions used throughout, and [G20 Camera Systems](./G20_camera_systems.md) for the `CameraRig` referenced by camera actions.*
