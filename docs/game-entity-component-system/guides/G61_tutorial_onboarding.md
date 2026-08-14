# G61 — Tutorial & Onboarding Systems



> **Category:** Guide · **Related:** [G5 UI Framework](./G5_ui_framework.md) · [G45 Cutscenes](./G45_cutscenes.md) · [G10 Custom Game Systems](./G10_custom_game_systems.md) · [G7 Input Handling](./G7_input_handling.md) · [E6 Game Design Fundamentals](../../core/game-design/E6_game_design_fundamentals.md)

---

## 1 — Tutorial Design Philosophy

Great tutorials are invisible. The player never thinks "I'm in a tutorial" — they think "I'm playing the game."

**Core principles:**

- **Show, don't tell.** Let the player discover mechanics through interaction, not text dumps. A pit they must jump over teaches jumping better than "Press A to Jump."
- **Teach through gameplay.** Every tutorial moment should feel like play. The learning *is* the game.
- **Gradual complexity.** Introduce one mechanic at a time. Never stack two new concepts in the same moment.
- **The Nintendo approach:** Safe space → introduce mechanic → test with low stakes → combine with previous mechanics. *Super Mario Bros* World 1-1 is the gold standard — the entire level is a tutorial and most players never notice.
- **Respect the player's time.** Veteran players should be able to skip or blast through tutorials. Never lock experienced players in unskippable hand-holding.

**Anti-patterns to avoid:**

- Walls of text before gameplay starts
- Forcing the player to wait while dialogue finishes
- Teaching mechanics the player already demonstrated understanding of
- Repeating tutorials on death/reload

---

## 2 — Tutorial Trigger System

Tutorials fire in response to game events — entering a zone, picking up an item, encountering an enemy. Each trigger fires at most once per save file.

### ECS Components

```csharp
public record struct TutorialTrigger(
    string TutorialId,
    TutorialTriggerType Type,
    bool OneShot = true
);

public enum TutorialTriggerType
{
    ZoneEnter,
    ItemPickup,
    CombatEncounter,
    Checkpoint,
    Interaction,
    AbilityUnlock,
    Death
}

public record struct TutorialZone(
    Rectangle Bounds,
    string TutorialId
);
```

### Trigger Detection System

```csharp
public class TutorialTriggerSystem : BaseSystem<World, float>
{
    private readonly TutorialManager _tutorials;
    private readonly QueryDescription _zoneQuery;
    private readonly QueryDescription _playerQuery;

    public TutorialTriggerSystem(World world, TutorialManager tutorials) : base(world)
    {
        _tutorials = tutorials;
        _zoneQuery = new QueryDescription().WithAll<TutorialZone, Position>();
        _playerQuery = new QueryDescription().WithAll<Player, Position>();
    }

    public override void Update(in float dt)
    {
        var playerPos = Vector2.Zero;
        World.Query(in _playerQuery, (ref Player p, ref Position pos) =>
        {
            playerPos = pos.Value;
        });

        World.Query(in _zoneQuery, (Entity entity, ref TutorialZone zone, ref Position pos) =>
        {
            var worldBounds = new Rectangle(
                (int)pos.Value.X + zone.Bounds.X,
                (int)pos.Value.Y + zone.Bounds.Y,
                zone.Bounds.Width, zone.Bounds.Height
            );

            if (worldBounds.Contains(playerPos.ToPoint()))
            {
                _tutorials.TryStart(zone.TutorialId);
            }
        });
    }
}
```

### Event-Based Triggers

For non-spatial triggers (item pickup, first death), fire through the event bus:

```csharp
// In your item pickup system:
eventBus.Publish(new TutorialEvent("first_item_pickup", TutorialTriggerType.ItemPickup));

// In TutorialManager:
public void HandleEvent(TutorialEvent evt)
{
    if (evt.Type == TutorialTriggerType.ItemPickup && !IsCompleted(evt.TutorialId))
        TryStart(evt.TutorialId);
}
```

---

## 3 — Prompt System

Context-sensitive input prompts that show the correct button for the player's current input device.

### Components and Data

```csharp
public record struct InputPrompt(
    string ActionName,       // "Jump", "Attack", "Interact"
    string MessageTemplate,  // "Press {0} to {1}"
    float FadeAlpha,
    float TimeVisible,
    float MaxVisibleTime,
    bool DismissOnAction,
    PromptAnchor Anchor
);

public enum PromptAnchor
{
    NearObject,     // Float near the relevant entity
    ScreenBottom,   // Fixed at bottom-center
    ScreenTop,
    AbovePlayer
}

public record struct PromptTarget(Entity Target); // Entity the prompt is attached to
```

### Controller-Aware Icon Mapping

```csharp
public class InputIconMapper
{
    private readonly Dictionary<string, Dictionary<InputDeviceType, string>> _mappings = new()
    {
        ["Jump"] = new()
        {
            [InputDeviceType.Keyboard] = "Space",
            [InputDeviceType.Xbox] = "icon_xbox_a",
            [InputDeviceType.PlayStation] = "icon_ps_cross",
            [InputDeviceType.Switch] = "icon_switch_b"
        },
        ["Attack"] = new()
        {
            [InputDeviceType.Keyboard] = "Z",
            [InputDeviceType.Xbox] = "icon_xbox_x",
            [InputDeviceType.PlayStation] = "icon_ps_square",
            [InputDeviceType.Switch] = "icon_switch_y"
        },
        ["Interact"] = new()
        {
            [InputDeviceType.Keyboard] = "E",
            [InputDeviceType.Xbox] = "icon_xbox_y",
            [InputDeviceType.PlayStation] = "icon_ps_triangle",
            [InputDeviceType.Switch] = "icon_switch_x"
        }
    };

    public string GetIcon(string action, InputDeviceType device)
    {
        return _mappings.TryGetValue(action, out var devices)
            && devices.TryGetValue(device, out var icon)
            ? icon : "?";
    }
}
```

### Prompt Render System

```csharp
public class PromptRenderSystem : BaseSystem<World, float>
{
    private readonly SpriteBatch _spriteBatch;
    private readonly InputIconMapper _iconMapper;
    private readonly InputDeviceTracker _deviceTracker;
    private readonly QueryDescription _query;

    public PromptRenderSystem(World world, SpriteBatch sb,
        InputIconMapper mapper, InputDeviceTracker tracker) : base(world)
    {
        _spriteBatch = sb;
        _iconMapper = mapper;
        _deviceTracker = tracker;
        _query = new QueryDescription().WithAll<InputPrompt, Position>();
    }

    public override void Update(in float dt)
    {
        World.Query(in _query, (Entity entity, ref InputPrompt prompt, ref Position pos) =>
        {
            prompt.TimeVisible += dt;

            // Fade in over 0.3s
            if (prompt.TimeVisible < 0.3f)
                prompt.FadeAlpha = prompt.TimeVisible / 0.3f;
            // Fade out near end
            else if (prompt.MaxVisibleTime > 0 &&
                     prompt.TimeVisible > prompt.MaxVisibleTime - 0.5f)
                prompt.FadeAlpha = MathHelper.Max(0,
                    (prompt.MaxVisibleTime - prompt.TimeVisible) / 0.5f);
            else
                prompt.FadeAlpha = 1f;

            // Auto-dismiss
            if (prompt.MaxVisibleTime > 0 && prompt.TimeVisible >= prompt.MaxVisibleTime)
            {
                World.Destroy(entity);
                return;
            }

            var icon = _iconMapper.GetIcon(prompt.ActionName, _deviceTracker.Current);
            var text = string.Format(prompt.MessageTemplate, icon, prompt.ActionName);
            var drawPos = ResolvePosition(prompt.Anchor, pos.Value);

            _spriteBatch.DrawString(_font, text,
                drawPos, Color.White * prompt.FadeAlpha);
        });
    }

    private Vector2 ResolvePosition(PromptAnchor anchor, Vector2 objectPos)
    {
        return anchor switch
        {
            PromptAnchor.ScreenBottom => new Vector2(_viewport.Width / 2f, _viewport.Height - 60),
            PromptAnchor.AbovePlayer => objectPos + new Vector2(0, -48),
            PromptAnchor.NearObject => objectPos + new Vector2(24, -32),
            _ => objectPos
        };
    }
}
```

---

## 4 — UI Highlighting

Draw attention to specific UI elements by darkening everything else and highlighting the target.

```csharp
public record struct UIHighlight(
    Rectangle TargetBounds,
    float PulseTimer,
    float PulseSpeed,
    Color GlowColor,
    bool ShowArrow,
    ArrowDirection ArrowDir
);

public enum ArrowDirection { Up, Down, Left, Right }

public class UIHighlightRenderer
{
    private readonly SpriteBatch _spriteBatch;
    private readonly Texture2D _pixel;
    private readonly Texture2D _arrowTexture;

    public void Draw(UIHighlight highlight, float dt)
    {
        highlight.PulseTimer += dt * highlight.PulseSpeed;
        float pulse = (MathF.Sin(highlight.PulseTimer) + 1f) / 2f; // 0..1

        // Full-screen dark overlay
        _spriteBatch.Draw(_pixel, new Rectangle(0, 0, _viewportW, _viewportH),
            Color.Black * 0.6f);

        // Cut out the highlighted area (draw it bright)
        // In practice: render the UI element on top of the overlay
        // or use a stencil buffer approach

        // Pulsing border
        var borderColor = Color.Lerp(highlight.GlowColor, Color.White, pulse * 0.3f);
        int borderWidth = 2 + (int)(pulse * 2);
        DrawBorder(highlight.TargetBounds, borderColor, borderWidth);

        // Arrow indicator
        if (highlight.ShowArrow)
        {
            var arrowPos = GetArrowPosition(highlight.TargetBounds, highlight.ArrowDir);
            float bob = MathF.Sin(highlight.PulseTimer * 2f) * 6f;
            _spriteBatch.Draw(_arrowTexture, arrowPos + new Vector2(0, bob),
                null, Color.White, GetArrowRotation(highlight.ArrowDir),
                Vector2.Zero, 1f, SpriteEffects.None, 0f);
        }
    }

    private void DrawBorder(Rectangle rect, Color color, int width)
    {
        _spriteBatch.Draw(_pixel, new Rectangle(rect.X - width, rect.Y - width,
            rect.Width + width * 2, width), color); // top
        _spriteBatch.Draw(_pixel, new Rectangle(rect.X - width, rect.Bottom,
            rect.Width + width * 2, width), color); // bottom
        _spriteBatch.Draw(_pixel, new Rectangle(rect.X - width, rect.Y,
            width, rect.Height), color); // left
        _spriteBatch.Draw(_pixel, new Rectangle(rect.Right, rect.Y,
            width, rect.Height), color); // right
    }
}
```

---

## 5 — Gating / Forced Tutorials

Sometimes the player *must* perform an action before continuing. Use sparingly.

```csharp
public record struct TutorialGate(
    string TutorialId,
    GateType Type,
    string RequiredAction,   // "Jump", "Attack", etc.
    bool Completed
);

public enum GateType
{
    Hard,   // Cannot proceed until action performed
    Soft    // Hint shown, but player can walk past after delay
}

public class TutorialGateSystem : BaseSystem<World, float>
{
    private readonly TutorialManager _tutorials;
    private readonly QueryDescription _gateQuery;
    private readonly QueryDescription _playerQuery;

    public override void Update(in float dt)
    {
        if (_tutorials.AreTutorialsDisabled) return; // Respect user setting

        World.Query(in _gateQuery, (Entity entity, ref TutorialGate gate,
            ref Position pos, ref Collider col) =>
        {
            if (gate.Completed) return;

            if (!IsPlayerNear(pos.Value, col.Bounds)) return;

            if (gate.Type == GateType.Hard)
            {
                BlockPlayerMovement(pos.Value, gate.RequiredAction);
                _tutorials.ShowPrompt(gate.RequiredAction);
            }
            else // Soft
            {
                _tutorials.ShowHint(gate.RequiredAction);
                // Player can still move through after 3 seconds
            }
        });
    }

    private void BlockPlayerMovement(Vector2 gatePos, string action)
    {
        // Push player back from the gate boundary
        // until they perform the required action
    }
}
```

**Settings integration:**

```csharp
// In your options menu / settings data
public record struct GameSettings(
    bool TutorialsEnabled,   // Master toggle
    bool ShowInputPrompts,   // Can hide prompts separately
    HintLevel HintFrequency  // Off / Subtle / Normal / Frequent
);
```

---

## 6 — Tooltip System

Proximity or hover tooltips for items, NPCs, and interactable objects.

```csharp
public record struct Tooltip(
    string Title,
    string Description,
    string IconId,
    TooltipStyle Style,
    float VisibleTimer
);

public enum TooltipStyle { Simple, Rich, Stat }

public record struct TooltipStats(
    int Damage,
    int Defense,
    float Speed,
    string Rarity
);
```

### Tooltip Positioning

```csharp
public class TooltipRenderer
{
    private const int Padding = 8;
    private const int MaxWidth = 240;

    public void Draw(SpriteBatch sb, Tooltip tooltip, Vector2 objectPos,
        Rectangle viewport, SpriteFont font)
    {
        var textSize = font.MeasureString(tooltip.Description);
        var boxWidth = MathHelper.Min((int)textSize.X + Padding * 2, MaxWidth);
        var boxHeight = (int)textSize.Y + Padding * 2 + 24; // 24 for title

        // Default: above the object
        var boxPos = new Vector2(
            objectPos.X - boxWidth / 2f,
            objectPos.Y - boxHeight - 12
        );

        // Clamp to screen edges
        boxPos.X = MathHelper.Clamp(boxPos.X, 4, viewport.Width - boxWidth - 4);
        boxPos.Y = MathHelper.Clamp(boxPos.Y, 4, viewport.Height - boxHeight - 4);

        // If clamped above pushed it below the object, flip to below
        if (boxPos.Y > objectPos.Y - 8)
            boxPos.Y = objectPos.Y + 32;

        // Background
        sb.Draw(_pixel, new Rectangle((int)boxPos.X, (int)boxPos.Y,
            boxWidth, boxHeight), Color.Black * 0.85f);

        // Border
        DrawBorder(sb, new Rectangle((int)boxPos.X, (int)boxPos.Y,
            boxWidth, boxHeight), GetRarityColor(tooltip));

        // Icon + title
        if (tooltip.IconId != null)
            sb.Draw(_icons[tooltip.IconId],
                boxPos + new Vector2(Padding, Padding), Color.White);

        sb.DrawString(font, tooltip.Title,
            boxPos + new Vector2(Padding + 20, Padding), Color.Gold);

        // Description
        sb.DrawString(font, tooltip.Description,
            boxPos + new Vector2(Padding, Padding + 24), Color.LightGray);
    }
}
```

### Rich Tooltips with Stats

```csharp
public void DrawStatTooltip(SpriteBatch sb, Tooltip tooltip,
    TooltipStats stats, Vector2 pos)
{
    // ... positioning as above, then:
    var y = startY + 40;

    if (stats.Damage > 0)
    {
        sb.Draw(_swordIcon, new Vector2(x, y), Color.White);
        sb.DrawString(_font, $"ATK {stats.Damage}", new Vector2(x + 18, y), Color.Red);
        y += 18;
    }
    if (stats.Defense > 0)
    {
        sb.Draw(_shieldIcon, new Vector2(x, y), Color.White);
        sb.DrawString(_font, $"DEF {stats.Defense}", new Vector2(x + 18, y), Color.SkyBlue);
        y += 18;
    }
}
```

---

## 7 — Tutorial State Machine

Multi-step tutorials with sequential instruction flow.

```csharp
public class TutorialSequence
{
    public string Id { get; init; }
    public List<TutorialStep> Steps { get; init; } = new();
    public int CurrentStep { get; set; }
    public bool IsComplete => CurrentStep >= Steps.Count;

    public TutorialStep Current => IsComplete ? null : Steps[CurrentStep];

    public bool TryAdvance(string completedAction)
    {
        if (IsComplete) return false;
        if (Current.RequiredAction == completedAction)
        {
            CurrentStep++;
            return true;
        }
        return false;
    }
}

public class TutorialStep
{
    public string Instruction { get; init; }    // "Move to the right"
    public string RequiredAction { get; init; } // "MoveRight"
    public float? TimeLimit { get; init; }      // Optional time pressure
    public string PromptAction { get; init; }   // Input prompt to show
    public Vector2? HighlightPos { get; init; } // Optional world highlight
}
```

### Driving the State Machine

```csharp
public class TutorialSequenceSystem : BaseSystem<World, float>
{
    private readonly TutorialManager _manager;

    public override void Update(in float dt)
    {
        var active = _manager.ActiveSequence;
        if (active == null || active.IsComplete) return;

        var step = active.Current;

        // Show the current instruction
        _manager.ShowInstruction(step.Instruction);

        // Show input prompt for this step
        if (step.PromptAction != null)
            _manager.ShowPrompt(step.PromptAction);

        // Show world highlight if specified
        if (step.HighlightPos.HasValue)
            _manager.HighlightWorldPosition(step.HighlightPos.Value);

        // Check completion via the action tracker
        if (_manager.WasActionPerformed(step.RequiredAction))
        {
            active.TryAdvance(step.RequiredAction);

            if (active.IsComplete)
                _manager.CompleteSequence(active.Id);
        }
    }
}
```

**Example sequence — basic movement tutorial:**

| Step | Instruction | Required Action |
|------|-------------|-----------------|
| 1 | "Move to the right →" | MoveRight |
| 2 | "Now try jumping!" | Jump |
| 3 | "Attack the training dummy" | Attack |
| 4 | "Open your inventory" | OpenInventory |

---

## 8 — First-Time-User Experience (FTUE)

The first 5 minutes decide if the player keeps playing. Every second counts.

### The FTUE Loop

```
Hook (0-30s) → Teach Core Mechanic (30s-2m) → First Reward (2-3m)
     → Teach Secondary Mechanic (3-4m) → First Challenge (4-5m)
```

### FTUE Manager

```csharp
public class FTUEManager
{
    private readonly TutorialManager _tutorials;
    private readonly AnalyticsTracker _analytics;

    private readonly List<FTUEMilestone> _milestones = new()
    {
        new("first_move", "Player moved", TimeSpan.FromSeconds(10)),
        new("first_jump", "Player jumped", TimeSpan.FromSeconds(30)),
        new("first_enemy", "Defeated first enemy", TimeSpan.FromMinutes(2)),
        new("first_item", "Collected first item", TimeSpan.FromMinutes(3)),
        new("first_save", "Reached first checkpoint", TimeSpan.FromMinutes(5)),
    };

    public void TrackMilestone(string id)
    {
        var milestone = _milestones.FirstOrDefault(m => m.Id == id);
        if (milestone == null || milestone.Reached) return;

        milestone.Reached = true;
        milestone.TimeReached = _playTimer.Elapsed;

        _analytics.Track("ftue_milestone", new
        {
            id,
            elapsed = milestone.TimeReached.TotalSeconds,
            expected = milestone.ExpectedTime.TotalSeconds
        });
    }
}

public class FTUEMilestone
{
    public string Id { get; init; }
    public string Description { get; init; }
    public TimeSpan ExpectedTime { get; init; }
    public bool Reached { get; set; }
    public TimeSpan TimeReached { get; set; }

    public FTUEMilestone(string id, string desc, TimeSpan expected)
    {
        Id = id; Description = desc; ExpectedTime = expected;
    }
}
```

**Progressive disclosure:** Don't show the full HUD at start. Reveal UI elements as they become relevant — health bar appears when the player first takes damage, inventory icon appears when they pick up their first item.

---

## 9 — Contextual Help

If the player is stuck, help should arrive — subtly at first, then more directly.

```csharp
public class ContextualHelpSystem : BaseSystem<World, float>
{
    private float _stuckTimer;
    private Vector2 _lastPlayerPos;
    private string _lastObjective;
    private int _hintLevel; // 0 = none, 1 = subtle, 2 = direct, 3 = explicit

    private readonly float[] _hintThresholds = { 15f, 30f, 60f }; // seconds

    public override void Update(in float dt)
    {
        var playerPos = GetPlayerPosition();
        var currentObjective = _objectiveTracker.CurrentId;

        // Reset if player made progress
        if (Vector2.Distance(playerPos, _lastPlayerPos) > 64f ||
            currentObjective != _lastObjective)
        {
            _stuckTimer = 0f;
            _hintLevel = 0;
            _lastPlayerPos = playerPos;
            _lastObjective = currentObjective;
            return;
        }

        _stuckTimer += dt;

        // Escalate hints based on difficulty setting
        float difficultyMod = _settings.Difficulty switch
        {
            Difficulty.Easy => 0.6f,    // Hints come sooner
            Difficulty.Normal => 1.0f,
            Difficulty.Hard => 1.5f,    // Hints come later
            Difficulty.Expert => 99f,   // Basically never
            _ => 1f
        };

        for (int i = 0; i < _hintThresholds.Length; i++)
        {
            if (_stuckTimer > _hintThresholds[i] * difficultyMod && _hintLevel <= i)
            {
                _hintLevel = i + 1;
                ShowHint(_hintLevel, currentObjective);
            }
        }
    }

    private void ShowHint(int level, string objective)
    {
        var hintData = _hintDatabase.Get(objective);
        if (hintData == null) return;

        switch (level)
        {
            case 1: // Subtle — small particle effect near the solution
                SpawnHintParticles(hintData.TargetPosition);
                break;
            case 2: // Direct — text hint at screen edge
                _tutorials.ShowHint(hintData.DirectHint);
                break;
            case 3: // Explicit — arrow pointing to solution
                _tutorials.ShowArrow(hintData.TargetPosition);
                _tutorials.ShowHint(hintData.ExplicitHint);
                break;
        }
    }
}
```

---

## 10 — Tutorial Data Model

Define tutorials in JSON so designers can iterate without recompiling.

### Tutorial Definition Format

```json
{
  "tutorials": [
    {
      "id": "basic_movement",
      "category": "core",
      "priority": 1,
      "gateType": "hard",
      "canSkipAfterSeconds": 0,
      "steps": [
        {
          "instruction": "Use {move_right} to walk forward",
          "requiredAction": "MoveRight",
          "promptAction": "MoveRight",
          "timeLimit": null
        },
        {
          "instruction": "Press {jump} to leap over obstacles",
          "requiredAction": "Jump",
          "promptAction": "Jump",
          "timeLimit": null
        }
      ],
      "triggers": [
        { "type": "ZoneEnter", "zoneId": "tutorial_start_zone" }
      ],
      "reward": {
        "type": "none"
      }
    },
    {
      "id": "combat_basics",
      "category": "combat",
      "priority": 2,
      "gateType": "soft",
      "canSkipAfterSeconds": 5,
      "steps": [
        {
          "instruction": "Press {attack} to swing your sword",
          "requiredAction": "Attack",
          "promptAction": "Attack",
          "highlightPosition": { "x": 320, "y": 180 }
        },
        {
          "instruction": "Press {dodge} to evade enemy attacks",
          "requiredAction": "Dodge",
          "promptAction": "Dodge",
          "timeLimit": 10.0
        }
      ],
      "triggers": [
        { "type": "CombatEncounter", "firstOnly": true }
      ]
    }
  ],
  "hints": {
    "find_key": {
      "targetPosition": { "x": 512, "y": 300 },
      "subtle": null,
      "direct": "The key might be hidden nearby...",
      "explicit": "Check behind the waterfall to the east"
    }
  }
}
```

### Loading and Persistence

```csharp
public class TutorialDataStore
{
    private const string TutorialDefPath = "Content/Data/tutorials.json";
    private const string SaveKey = "tutorial_progress";

    public TutorialDatabase LoadDefinitions()
    {
        var json = File.ReadAllText(TutorialDefPath);
        return JsonSerializer.Deserialize<TutorialDatabase>(json);
    }

    public CompletedTutorials LoadProgress(SaveManager save)
    {
        return save.TryLoad<CompletedTutorials>(SaveKey)
            ?? new CompletedTutorials();
    }

    public void SaveProgress(SaveManager save, CompletedTutorials completed)
    {
        save.Save(SaveKey, completed);
    }
}

public class CompletedTutorials
{
    public HashSet<string> Completed { get; set; } = new();
    public Dictionary<string, int> PartialProgress { get; set; } = new(); // step index
    public bool SkipAll { get; set; }

    public bool IsCompleted(string tutorialId) => SkipAll || Completed.Contains(tutorialId);

    public void Complete(string tutorialId)
    {
        Completed.Add(tutorialId);
        PartialProgress.Remove(tutorialId);
    }
}
```

### Save Data JSON

```json
{
  "completed": ["basic_movement", "combat_basics", "first_item_pickup"],
  "partialProgress": {
    "advanced_combat": 2
  },
  "skipAll": false
}
```

---

## 11 — ECS Integration

Bringing it all together — the `TutorialManager` service connects triggers, state machines, prompts, and persistence.

### TutorialManager Service

```csharp
public class TutorialManager
{
    private readonly World _world;
    private readonly TutorialDataStore _dataStore;
    private readonly InputIconMapper _iconMapper;
    private readonly EventBus _eventBus;

    private TutorialDatabase _database;
    private CompletedTutorials _progress;
    private TutorialSequence _activeSequence;

    public TutorialSequence ActiveSequence => _activeSequence;
    public bool AreTutorialsDisabled => _progress.SkipAll;

    public void Initialize(SaveManager saveManager)
    {
        _database = _dataStore.LoadDefinitions();
        _progress = _dataStore.LoadProgress(saveManager);
        _eventBus.Subscribe<TutorialEvent>(HandleEvent);
        _eventBus.Subscribe<ActionPerformedEvent>(HandleAction);
    }

    public bool TryStart(string tutorialId)
    {
        if (_progress.IsCompleted(tutorialId)) return false;
        if (_activeSequence != null && !_activeSequence.IsComplete) return false;

        var def = _database.Get(tutorialId);
        if (def == null) return false;

        _activeSequence = new TutorialSequence
        {
            Id = tutorialId,
            Steps = def.Steps.Select(s => new TutorialStep
            {
                Instruction = s.Instruction,
                RequiredAction = s.RequiredAction,
                PromptAction = s.PromptAction,
                TimeLimit = s.TimeLimit,
                HighlightPos = s.HighlightPosition.HasValue
                    ? new Vector2(s.HighlightPosition.Value.X, s.HighlightPosition.Value.Y)
                    : null
            }).ToList(),
            CurrentStep = _progress.PartialProgress.GetValueOrDefault(tutorialId, 0)
        };

        return true;
    }

    public void ShowPrompt(string actionName)
    {
        var entity = _world.Create<InputPrompt, Position>();
        _world.Set(entity, new InputPrompt(
            ActionName: actionName,
            MessageTemplate: "Press {0} to {1}",
            FadeAlpha: 0f,
            TimeVisible: 0f,
            MaxVisibleTime: 8f,
            DismissOnAction: true,
            Anchor: PromptAnchor.ScreenBottom
        ));
    }

    public void ShowInstruction(string text)
    {
        _eventBus.Publish(new UIEvent("tutorial_instruction", text));
    }

    public void ShowHint(string text)
    {
        _eventBus.Publish(new UIEvent("tutorial_hint", text));
    }

    public void ShowArrow(Vector2 target)
    {
        _eventBus.Publish(new UIEvent("tutorial_arrow", target));
    }

    public void HighlightWorldPosition(Vector2 pos)
    {
        _eventBus.Publish(new UIEvent("tutorial_highlight_world", pos));
    }

    private readonly HashSet<string> _frameActions = new();

    private void HandleAction(ActionPerformedEvent evt)
    {
        _frameActions.Add(evt.ActionName);
    }

    public bool WasActionPerformed(string action) => _frameActions.Contains(action);

    public void EndFrame()
    {
        _frameActions.Clear();
    }

    public void CompleteSequence(string tutorialId)
    {
        _progress.Complete(tutorialId);
        _activeSequence = null;
        _eventBus.Publish(new TutorialCompletedEvent(tutorialId));
    }

    public void SaveProgress(SaveManager saveManager)
    {
        if (_activeSequence != null && !_activeSequence.IsComplete)
        {
            _progress.PartialProgress[_activeSequence.Id] = _activeSequence.CurrentStep;
        }
        _dataStore.SaveProgress(saveManager, _progress);
    }

    private void HandleEvent(TutorialEvent evt) => TryStart(evt.TutorialId);
}
```

### System Registration

```csharp
public void RegisterTutorialSystems(World world, TutorialManager tutorials,
    SpriteBatch spriteBatch, InputIconMapper mapper, InputDeviceTracker tracker)
{
    world.AddSystem(new TutorialTriggerSystem(world, tutorials));
    world.AddSystem(new TutorialSequenceSystem(world, tutorials));
    world.AddSystem(new TutorialGateSystem(world, tutorials));
    world.AddSystem(new ContextualHelpSystem(world, tutorials));
    world.AddSystem(new PromptRenderSystem(world, spriteBatch, mapper, tracker));
}
```

### Entity Setup Example

```csharp
// Create a tutorial trigger zone in your level loader
public Entity CreateTutorialZone(World world, Vector2 position,
    Rectangle bounds, string tutorialId)
{
    var entity = world.Create<TutorialZone, Position>();
    world.Set(entity, new TutorialZone(bounds, tutorialId));
    world.Set(entity, new Position(position));
    return entity;
}

// Level loading — place tutorial zones from Tiled or level data
var jumpZone = CreateTutorialZone(world,
    new Vector2(400, 200),
    new Rectangle(0, 0, 96, 128),
    "basic_movement"
);

// Create a gated area
var combatGate = world.Create<TutorialGate, Position, Collider>();
world.Set(combatGate, new TutorialGate(
    TutorialId: "combat_basics",
    Type: GateType.Soft,
    RequiredAction: "Attack",
    Completed: false
));
```

---

## Quick Reference

| Component | Purpose |
|-----------|---------|
| `TutorialTrigger` | Marks an entity as a tutorial trigger |
| `TutorialZone` | Spatial trigger area with bounds |
| `TutorialGate` | Blocks progress until action performed |
| `InputPrompt` | Context-sensitive button prompt |
| `Tooltip` | Hover/proximity information popup |
| `UIHighlight` | Draws attention to UI element |

| Service / System | Purpose |
|-------------------|---------|
| `TutorialManager` | Central coordinator for all tutorial logic |
| `TutorialTriggerSystem` | Detects zone entry, fires tutorials |
| `TutorialSequenceSystem` | Drives multi-step tutorial state |
| `PromptRenderSystem` | Renders input prompts with correct icons |
| `ContextualHelpSystem` | Escalating hints when player is stuck |
| `FTUEManager` | Tracks first-time milestones |

**Golden rule:** If your tutorial needs a paragraph of text to explain a mechanic, redesign the mechanic — or redesign the tutorial.
