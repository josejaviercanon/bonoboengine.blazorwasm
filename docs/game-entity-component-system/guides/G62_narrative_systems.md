# G62 — Narrative & Branching Story Systems



> **Category:** Guide · **Related:** [G10 Custom Game Systems](./G10_custom_game_systems.md) · [G45 Cutscenes](./G45_cutscenes.md) · [G5 UI Framework](./G5_ui_framework.md) · [G47 Achievements](./G47_achievements.md) · [E6 Game Design Fundamentals](../../core/game-design/E6_game_design_fundamentals.md)

---

## Overview

Narrative systems transform a game from a series of mechanics into a **story worth experiencing**. This guide covers the full stack: data-driven story architecture, branching dialogue, world-state tracking, consequence propagation, journal/codex systems, reputation, and integration with industry tools like Yarn Spinner and Ink — all wired into Arch ECS.

The golden rule: **narrative is data, not code**. Writers edit JSON or Yarn files; programmers build the engine that interprets them.

---

## 1 — Narrative Architecture

### Why Data-Driven?

Hardcoding dialogue strings and branch logic into C# is a dead end. Data-driven narrative means:

- **Writers iterate independently** — no recompilation for text changes
- **Localization becomes file swaps** — one JSON per language
- **Modders can extend stories** — drop new files into a folder
- **Hot-reload during development** — change a line, see it immediately

### Story Data Formats

| Format | Strengths | Best For |
|--------|-----------|----------|
| **JSON** | Simple, universal, easy to parse | Linear sequences, barks, journal entries |
| **Yarn (.yarn)** | Visual editor (Yarn Spinner), branching built-in | Complex branching dialogue |
| **Ink (.ink)** | Powerful scripting, knot/stitch/weave model | Deep narrative with state |

### Narrative Manager Service

The `NarrativeManager` sits outside ECS as a **shared service** — any system can request dialogue, query flags, or trigger story events.

```csharp
public class NarrativeManager
{
    private readonly StoryFlagStore _flags = new();
    private readonly DialogueDatabase _dialogueDb = new();
    private readonly JournalStore _journal = new();
    private readonly ReputationStore _reputation = new();
    private readonly ConsequenceQueue _consequences = new();

    public StoryFlagStore Flags => _flags;
    public DialogueDatabase Dialogue => _dialogueDb;
    public JournalStore Journal => _journal;
    public ReputationStore Reputation => _reputation;
    public ConsequenceQueue Consequences => _consequences;

    public void LoadFromDirectory(string path)
    {
        _dialogueDb.LoadAll(Path.Combine(path, "dialogue"));
        _journal.LoadDefinitions(Path.Combine(path, "journal"));
        _flags.LoadDefaults(Path.Combine(path, "flags.json"));
    }
}
```

Register it so every system can access it:

```csharp
// In Game1 initialization
var narrative = new NarrativeManager();
narrative.LoadFromDirectory("Content/Story");
world.Set(narrative); // Arch resource — accessible from any system
```

---

## 2 — Branching Dialogue

### Dialogue Node Structure

Each dialogue node contains the speaker line, optional portrait/emotion, and a list of choices. Each choice can have **conditions** that gate availability.

```csharp
public record struct DialogueNode(
    string Id,
    string Speaker,
    string Text,
    string? Portrait,
    string? Emotion,
    DialogueChoice[] Choices,
    string? NextNode  // fallback if no choices
);

public record struct DialogueChoice(
    string Text,
    string TargetNode,
    DialogueCondition[]? Conditions,
    StoryConsequence[]? Consequences
);

public record struct DialogueCondition(
    string FlagName,
    ConditionOp Op,
    string Value
);

public enum ConditionOp { Equals, NotEquals, GreaterThan, LessThan, HasItem, FactionAbove }
```

### Example Dialogue JSON

```json
{
  "id": "blacksmith_intro",
  "nodes": [
    {
      "id": "start",
      "speaker": "Hilda",
      "text": "Another adventurer? I hope you're not here to cause trouble.",
      "portrait": "hilda_neutral",
      "choices": [
        {
          "text": "I need a sword forged.",
          "targetNode": "forge_request"
        },
        {
          "text": "I heard you know about the ruins.",
          "targetNode": "ruins_info",
          "conditions": [{ "flagName": "ruins_discovered", "op": "Equals", "value": "true" }]
        },
        {
          "text": "[Intimidate] You'll tell me what I want to know.",
          "targetNode": "intimidate",
          "conditions": [{ "flagName": "strength", "op": "GreaterThan", "value": "14" }]
        }
      ]
    },
    {
      "id": "ruins_info",
      "speaker": "Hilda",
      "text": "Keep your voice down! Yes, I explored them years ago...",
      "emotion": "worried",
      "nextNode": "ruins_details"
    }
  ]
}
```

### Evaluating Conditions

```csharp
public class ConditionEvaluator
{
    public static bool Evaluate(DialogueCondition cond, StoryFlagStore flags)
    {
        var value = flags.Get(cond.FlagName);
        return cond.Op switch
        {
            ConditionOp.Equals => value == cond.Value,
            ConditionOp.NotEquals => value != cond.Value,
            ConditionOp.GreaterThan => float.Parse(value) > float.Parse(cond.Value),
            ConditionOp.LessThan => float.Parse(value) < float.Parse(cond.Value),
            ConditionOp.HasItem => flags.GetBool($"inventory_{cond.Value}"),
            ConditionOp.FactionAbove => float.Parse(flags.Get($"rep_{cond.Value}")) 
                                        > float.Parse(cond.Value),
            _ => false
        };
    }

    public static List<DialogueChoice> FilterChoices(
        DialogueChoice[] choices, StoryFlagStore flags)
    {
        return choices.Where(c =>
            c.Conditions == null || c.Conditions.All(cond => Evaluate(cond, flags))
        ).ToList();
    }
}
```

---

## 3 — Story Flags & World State

### The Flag Store

A unified key-value store that every system reads and writes. Flags drive **everything**: which dialogue branches appear, which quests are available, which NPCs are alive, and which ending you get.

```csharp
public class StoryFlagStore
{
    private readonly Dictionary<string, string> _flags = new();
    public event Action<string, string, string>? OnFlagChanged; // key, oldVal, newVal

    public void Set(string key, string value)
    {
        _flags.TryGetValue(key, out var old);
        _flags[key] = value;
        if (old != value) OnFlagChanged?.Invoke(key, old ?? "", value);
    }

    public void SetBool(string key, bool value) => Set(key, value.ToString().ToLower());
    public void SetInt(string key, int value) => Set(key, value.ToString());
    public void Increment(string key, int amount = 1)
        => SetInt(key, GetInt(key) + amount);

    public string Get(string key, string fallback = "")
        => _flags.TryGetValue(key, out var v) ? v : fallback;
    public bool GetBool(string key) => Get(key) == "true";
    public int GetInt(string key) => int.TryParse(Get(key), out var i) ? i : 0;

    public Dictionary<string, string> Snapshot() => new(_flags);

    public void LoadDefaults(string jsonPath)
    {
        if (!File.Exists(jsonPath)) return;
        var defaults = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(jsonPath));
        if (defaults == null) return;
        foreach (var (k, v) in defaults)
            _flags.TryAdd(k, v);
    }

    public void Restore(Dictionary<string, string> snapshot)
    {
        _flags.Clear();
        foreach (var (k, v) in snapshot) _flags[k] = v;
    }
}
```

### Common Flag Patterns

```
quest_blacksmith_started    = "true"         // boolean
reputation_ironhold         = "42"           // integer
chosen_faction              = "rebels"       // string
npc_hilda_alive             = "true"         // boolean
act                         = "2"            // progression gate
endings_merciful_count      = "3"            // accumulator for ending calc
```

### Persistence

Flags serialize directly into your save file. The `Snapshot()` / `Restore()` pair makes this trivial:

```csharp
// Save
saveData.StoryFlags = narrative.Flags.Snapshot();

// Load
narrative.Flags.Restore(saveData.StoryFlags);
```

---

## 4 — Consequence System

### Immediate vs Delayed Consequences

**Immediate:** Choosing to insult an NPC → their disposition drops, dialogue changes right now.

**Delayed:** Sparing a bandit in Act 1 → that bandit returns to help you in Act 3.

Both are modeled as `StoryConsequence` records that fire when conditions are met:

```csharp
public record struct StoryConsequence(
    string Type,       // "set_flag", "add_rep", "unlock_journal", "spawn_entity", "trigger_event"
    string Target,
    string Value,
    string? DelayUntilFlag  // null = immediate, otherwise wait until this flag is true
);

public class ConsequenceQueue
{
    private readonly List<StoryConsequence> _pending = new();

    public void Enqueue(StoryConsequence consequence)
    {
        if (consequence.DelayUntilFlag == null)
            _pending.Add(consequence);
        else
            _deferred.Add(consequence);
    }

    private readonly List<StoryConsequence> _deferred = new();

    public void CheckDeferred(StoryFlagStore flags)
    {
        for (int i = _deferred.Count - 1; i >= 0; i--)
        {
            if (flags.GetBool(_deferred[i].DelayUntilFlag!))
            {
                _pending.Add(_deferred[i]);
                _deferred.RemoveAt(i);
            }
        }
    }

    public List<StoryConsequence> DrainPending()
    {
        var list = new List<StoryConsequence>(_pending);
        _pending.Clear();
        return list;
    }
}
```

### Processing Consequences

```csharp
public void ProcessConsequences(NarrativeManager narrative)
{
    narrative.Consequences.CheckDeferred(narrative.Flags);

    foreach (var c in narrative.Consequences.DrainPending())
    {
        switch (c.Type)
        {
            case "set_flag":
                narrative.Flags.Set(c.Target, c.Value);
                break;
            case "add_rep":
                narrative.Reputation.Modify(c.Target, int.Parse(c.Value));
                break;
            case "unlock_journal":
                narrative.Journal.Unlock(c.Target);
                break;
            case "trigger_event":
                _eventBus.Publish(new NarrativeEvent(c.Target, c.Value));
                break;
        }
    }
}
```

### Branching Story Graph with Merge Points

Design principle: **branch freely, merge deliberately**. Major story beats (act transitions, boss encounters) serve as merge points where all branches converge, keeping the combinatorial explosion manageable.

```
         [Choice A]          [Choice B]
              \                  /
          [A consequence]  [B consequence]
               \              /
            ═══[ACT 2 MERGE POINT]═══
                     |
              [Shared content]
           (with flag-gated variants)
```

---

## 5 — Yarn Spinner / Ink Integration

### Yarn Spinner with MonoGame

Yarn Spinner uses `.yarn` text files with a simple syntax. Install the C# runtime (`YarnSpinner` NuGet) and wire it up:

```yarn
title: Blacksmith_Intro
---
Hilda: Another adventurer, eh?
-> I need a sword.
    Hilda: That'll cost you 50 gold.
    <<if $gold >= 50>>
        -> [Pay 50 gold] Deal.
            <<set $gold to $gold - 50>>
            Hilda: Come back tomorrow, it'll be ready.
            <<set $sword_ordered to true>>
        -> Too rich for my blood.
            Hilda: Then stop wasting my time.
    <<else>>
        Hilda: ...which you clearly can't afford. Come back when you have coin.
    <<endif>>
-> Tell me about the ruins.
    <<if $ruins_discovered>>
        Hilda: Keep your voice down...
        <<jump Ruins_Details>>
    <<else>>
        Hilda: What ruins? I don't know what you're talking about.
    <<endif>>
===
```

### Yarn Runtime Bridge

```csharp
public class YarnNarrativeBridge
{
    private Dialogue _dialogue;
    private MemoryVariableStore _variableStore;

    public event Action<string, string>? OnLine;        // speaker, text
    public event Action<List<string>>? OnChoices;
    public event Action? OnDialogueComplete;

    public void Initialize(string yarnFilePath, StoryFlagStore flags)
    {
        _variableStore = new MemoryVariableStore();

        // Sync flags → Yarn variables
        foreach (var (k, v) in flags.Snapshot())
            _variableStore.SetValue("$" + k, v);

        var program = Compiler.Compile(File.ReadAllText(yarnFilePath));
        _dialogue = new Dialogue(_variableStore)
        {
            LineHandler = HandleLine,
            OptionsHandler = HandleOptions,
            CommandHandler = HandleCommand,
            DialogueCompleteHandler = () => OnDialogueComplete?.Invoke()
        };
        _dialogue.SetProgram(program);
    }

    public void StartNode(string nodeName) => _dialogue.SetNode(nodeName);
    public void Continue() => _dialogue.Continue();
    public void SelectOption(int index) => _dialogue.SetSelectedOption(index);

    private void HandleLine(Line line)
    {
        var parts = line.Text.Value.Split(':', 2);
        OnLine?.Invoke(parts[0].Trim(), parts.Length > 1 ? parts[1].Trim() : parts[0]);
    }

    private void HandleOptions(OptionSet options)
    {
        OnChoices?.Invoke(options.Options.Select(o => o.Line.Text.Value).ToList());
    }

    private void HandleCommand(Command command)
    {
        // Parse custom commands: <<spawn enemy_bandit 10 5>>
        var parts = command.Text.Split(' ');
        // Forward to game systems via event bus
    }
}
```

### Ink Alternative

Ink uses a different syntax but similar concepts. The `inkle/ink` C# runtime works identically:

```ink
=== blacksmith_intro ===
Hilda squints at you from behind the anvil.
* [Ask about the sword] "I need a blade forged."
    "Fifty gold. Take it or leave it."
    ** {gold >= 50} [Pay] "Done."
        ~ gold -= 50
        ~ sword_ordered = true
        -> sword_ordered_response
    ** "Never mind."
        -> END
* {ruins_discovered} [Ask about the ruins]
    -> ruins_conversation
```

Both tools export to a compact bytecode format. Choose whichever your team's writers prefer.

---

## 6 — Journal / Codex / Lore

### Journal Entry Model

```csharp
public record struct JournalEntry(
    string Id,
    string Title,
    string Category,     // "quest", "lore", "character", "bestiary"
    string Content,
    string? Icon,
    string[]? Tags
);

public class JournalStore
{
    private readonly Dictionary<string, JournalEntry> _definitions = new();
    private readonly HashSet<string> _unlocked = new();
    private readonly HashSet<string> _read = new();

    public void LoadDefinitions(string directory)
    {
        foreach (var file in Directory.GetFiles(directory, "*.json"))
        {
            var entries = JsonSerializer.Deserialize<JournalEntry[]>(
                File.ReadAllText(file));
            if (entries == null) continue;
            foreach (var e in entries) _definitions[e.Id] = e;
        }
    }

    public void Unlock(string entryId)
    {
        if (_definitions.ContainsKey(entryId))
            _unlocked.Add(entryId);
    }

    public void MarkRead(string entryId) => _read.Add(entryId);
    public bool IsRead(string entryId) => _read.Contains(entryId);
    public int UnreadCount => _unlocked.Count(id => !_read.Contains(id));

    public IEnumerable<JournalEntry> GetByCategory(string category)
        => _unlocked
            .Where(id => _definitions[id].Category == category)
            .Select(id => _definitions[id]);

    public IEnumerable<JournalEntry> Search(string query)
        => _unlocked
            .Select(id => _definitions[id])
            .Where(e => e.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                     || e.Content.Contains(query, StringComparison.OrdinalIgnoreCase));

    public HashSet<string> GetUnlockedIds() => new(_unlocked);
    public HashSet<string> GetReadIds() => new(_read);
    public void RestoreState(HashSet<string> unlocked, HashSet<string> read)
    {
        _unlocked.Clear(); _read.Clear();
        foreach (var id in unlocked) _unlocked.Add(id);
        foreach (var id in read) _read.Add(id);
    }
}
```

### Example Journal JSON

```json
[
  {
    "id": "lore_ancient_war",
    "title": "The Sundering War",
    "category": "lore",
    "content": "Three centuries ago, the Ember Kingdoms waged war against...",
    "icon": "scroll",
    "tags": ["history", "ember_kingdoms"]
  },
  {
    "id": "char_hilda",
    "title": "Hilda — The Blacksmith",
    "category": "character",
    "content": "A former adventurer who settled in Ironhold after losing her party in the ruins...",
    "icon": "portrait_hilda",
    "tags": ["ironhold", "blacksmith", "ally"]
  }
]
```

---

## 7 — Reputation & Relationship Systems

### Reputation Store

```csharp
public class ReputationStore
{
    private readonly Dictionary<string, int> _scores = new();
    private readonly Dictionary<string, float> _decayRates = new(); // points lost per game-hour

    public void Modify(string faction, int amount)
    {
        _scores.TryGetValue(faction, out var current);
        _scores[faction] = Math.Clamp(current + amount, -100, 100);
    }

    public int Get(string faction)
        => _scores.TryGetValue(faction, out var v) ? v : 0;

    public ReputationTier GetTier(string faction) => Get(faction) switch
    {
        >= 75 => ReputationTier.Revered,
        >= 50 => ReputationTier.Honored,
        >= 25 => ReputationTier.Friendly,
        >= 0  => ReputationTier.Neutral,
        >= -25 => ReputationTier.Unfriendly,
        >= -50 => ReputationTier.Hostile,
        _ => ReputationTier.Hated
    };

    public void SetDecayRate(string faction, float ratePerHour)
        => _decayRates[faction] = ratePerHour;

    public void ApplyDecay(float gameHoursElapsed)
    {
        foreach (var (faction, rate) in _decayRates)
        {
            if (!_scores.ContainsKey(faction)) continue;
            var current = _scores[faction];
            // Decay toward zero
            var decay = (int)(rate * gameHoursElapsed);
            if (current > 0) _scores[faction] = Math.Max(0, current - decay);
            else if (current < 0) _scores[faction] = Math.Min(0, current + decay);
        }
    }

    public Dictionary<string, int> Snapshot() => new(_scores);
    public void Restore(Dictionary<string, int> data)
    {
        _scores.Clear();
        foreach (var (k, v) in data) _scores[k] = v;
    }
}

public enum ReputationTier
{
    Hated, Hostile, Unfriendly, Neutral, Friendly, Honored, Revered
}
```

### Reputation-Gated Content

```csharp
// In dialogue condition evaluation
case ConditionOp.FactionAbove:
    var parts = cond.Value.Split(':'); // "ironhold:50"
    return narrative.Reputation.Get(parts[0]) >= int.Parse(parts[1]);
```

Reputation affects: available quests, shop prices (discount at Honored+), NPC greetings, restricted areas, and ending eligibility.

---

## 8 — Quest Narrative Integration

Quests are the **delivery vehicle** for narrative. Each quest stage can trigger dialogue, unlock journal entries, modify reputation, and set flags that ripple through the entire story.

```csharp
public record struct QuestStageNarrative(
    string QuestId,
    string StageId,
    string? TriggerDialogue,      // dialogue tree to auto-start
    string[]? UnlockJournalIds,   // journal entries to reveal
    StoryConsequence[]? OnComplete // consequences when stage completes
);
```

### Wiring Quests to Narrative

```csharp
public class QuestNarrativeSystem : BaseSystem<World, float>
{
    private NarrativeManager _narrative;
    private readonly Dictionary<string, QuestStageNarrative[]> _stageData = new();

    public QuestNarrativeSystem(World world, NarrativeManager narrative) : base(world)
    {
        _narrative = narrative;
    }

    public override void Update(in float dt)
    {
        // Listen for quest stage completions
        foreach (var evt in _narrative.Flags.RecentChanges("quest_stage_*"))
        {
            var questId = evt.Key.Split('_')[2]; // quest_stage_blacksmith → blacksmith
            if (_stageData.TryGetValue(questId, out var stages))
            {
                var stage = stages.FirstOrDefault(s => s.StageId == evt.Value);
                if (stage.TriggerDialogue != null)
                    StartDialogue(stage.TriggerDialogue);
                if (stage.UnlockJournalIds != null)
                    foreach (var id in stage.UnlockJournalIds)
                        _narrative.Journal.Unlock(id);
                if (stage.OnComplete != null)
                    foreach (var c in stage.OnComplete)
                        _narrative.Consequences.Enqueue(c);
            }
        }
    }

    private void StartDialogue(string dialogueId) { /* push to active dialogue state */ }
}
```

**Optional quests as lore vehicles:** Side quests that don't advance the main plot can still unlock codex entries, reveal character backstories, and foreshadow future events. Reward curious players.

---

## 9 — Multiple Endings

### Decision Tracking

Track major decisions as weighted flags. At ending time, tally them up:

```csharp
public class EndingCalculator
{
    public record EndingDef(
        string Id, string Title, string Description,
        EndingCondition[] Conditions, int Priority
    );

    public record EndingCondition(string Flag, string Op, string Value, int Weight);

    private readonly List<EndingDef> _endings = new();

    public void Load(string jsonPath)
    {
        _endings.AddRange(
            JsonSerializer.Deserialize<List<EndingDef>>(File.ReadAllText(jsonPath))!
        );
    }

    public EndingDef DetermineEnding(StoryFlagStore flags)
    {
        var scored = _endings.Select(e => new
        {
            Ending = e,
            Score = e.Conditions.Sum(c =>
                ConditionEvaluator.Evaluate(
                    new DialogueCondition(c.Flag, Enum.Parse<ConditionOp>(c.Op), c.Value),
                    flags) ? c.Weight : 0
            )
        })
        .OrderByDescending(x => x.Score)
        .ThenByDescending(x => x.Ending.Priority)
        .First();

        return scored.Ending;
    }
}
```

### Epilogue Slides

```json
{
  "endings": [
    {
      "id": "ending_hero",
      "title": "The People's Champion",
      "description": "You united the factions and brought lasting peace.",
      "conditions": [
        { "flag": "rep_ironhold", "op": "GreaterThan", "value": "60", "weight": 3 },
        { "flag": "npc_hilda_alive", "op": "Equals", "value": "true", "weight": 2 },
        { "flag": "endings_merciful_count", "op": "GreaterThan", "value": "4", "weight": 5 }
      ],
      "priority": 10
    }
  ],
  "epilogueSlides": [
    {
      "condition": { "flag": "npc_hilda_alive", "op": "Equals", "value": "true" },
      "image": "epilogue_hilda_forge",
      "text": "Hilda reopened her forge. Adventurers came from distant lands seeking her blades."
    },
    {
      "condition": { "flag": "chosen_faction", "op": "Equals", "value": "rebels" },
      "image": "epilogue_rebels",
      "text": "The rebel council established a new order. Whether it would last... only time would tell."
    }
  ]
}
```

### New Game+ Story Carryover

```csharp
public Dictionary<string, string> GetNewGamePlusFlags(StoryFlagStore flags)
{
    var carry = new Dictionary<string, string>();
    // Carry cosmetic unlocks and meta-knowledge flags
    foreach (var (k, v) in flags.Snapshot())
    {
        if (k.StartsWith("ngplus_") || k.StartsWith("unlock_") || k.StartsWith("codex_"))
            carry[k] = v;
    }
    carry["ngplus_completed"] = "true";
    carry["ngplus_ending_seen"] = flags.Get("ending_id");
    return carry;
}
```

---

## 10 — Narrative UI

### Dialogue Box Component

```csharp
public record struct ActiveDialogue(
    string Speaker,
    string FullText,
    string DisplayedText,      // for typewriter effect
    float CharTimer,
    float CharsPerSecond,
    string? Portrait,
    string? Emotion,
    bool IsComplete,
    List<DialogueChoice> Choices,
    int SelectedChoice
);
```

### Typewriter Effect with Markup

```csharp
public class DialogueRenderSystem : BaseSystem<World, float>
{
    public override void Update(in float dt)
    {
        ref var dlg = ref World.Get<ActiveDialogue>();
        if (dlg.IsComplete) return;

        dlg.CharTimer += dt;
        var charsToShow = (int)(dlg.CharTimer * dlg.CharsPerSecond);

        // Parse markup tags — skip them in character count
        var visibleCount = 0;
        var sb = new StringBuilder();
        for (int i = 0; i < dlg.FullText.Length && visibleCount < charsToShow; i++)
        {
            if (dlg.FullText[i] == '<')
            {
                // Find closing '>' — include full tag without counting
                var end = dlg.FullText.IndexOf('>', i);
                if (end >= 0) { sb.Append(dlg.FullText[i..(end + 1)]); i = end; continue; }
            }
            if (dlg.FullText[i] == '{' && i + 1 < dlg.FullText.Length)
            {
                // Handle {pause:0.5} — add delay
                var close = dlg.FullText.IndexOf('}', i);
                if (close >= 0)
                {
                    var tag = dlg.FullText[(i + 1)..close];
                    if (tag.StartsWith("pause:"))
                    {
                        var pauseSec = float.Parse(tag[6..]);
                        dlg.CharTimer -= pauseSec; // subtract pause from timer
                    }
                    i = close;
                    continue;
                }
            }
            sb.Append(dlg.FullText[i]);
            visibleCount++;
        }

        dlg.DisplayedText = sb.ToString();
        if (visibleCount >= dlg.FullText.Count(c => c != '<' && c != '{'))
            dlg.IsComplete = true;
    }
}
```

### Supported Text Markup

```
Hello, <color:red>traveler</color>. I've been <b>waiting</b> for you.
{pause:0.8}
The ruins hold... <color:gold>terrible secrets</color>.
```

| Tag | Effect |
|-----|--------|
| `<b>text</b>` | Bold |
| `<color:name>text</color>` | Colored text |
| `{pause:N}` | Pause N seconds during typewriter |
| `{shake}` | Screen/text shake effect |
| `<i>text</i>` | Italic |

---

## 11 — Barks & Ambient Dialogue

Barks are short, non-interactive lines that NPCs emit contextually — proximity greetings, combat taunts, reactions to weather or player state.

### Bark Data Model

```csharp
public record struct BarkEntry(
    string Text,
    string? Emotion,
    BarkTrigger Trigger,
    float Priority,            // higher = overrides lower
    float CooldownSeconds,
    DialogueCondition[]? Conditions
);

public enum BarkTrigger
{
    Proximity,    // player walks near
    Combat,       // during fight
    PlayerLowHp,  // player health < 25%
    Weather,      // rain, snow, etc.
    TimeOfDay,    // morning, night
    QuestStage,   // specific quest progress
    Idle          // NPC has been standing around
}
```

### Bark Emitter Component

```csharp
public record struct BarkEmitter(
    string BarkSetId,
    float DetectionRadius,
    float LastBarkTime,
    float GlobalCooldown       // minimum time between any bark
);
```

### Bark Selection System

```csharp
public class BarkSystem : BaseSystem<World, float>
{
    private readonly QueryDescription _emitters = new QueryDescription()
        .WithAll<BarkEmitter, Position>();
    private readonly Dictionary<string, BarkEntry[]> _barkSets = new();

    public override void Update(in float dt)
    {
        var playerPos = GetPlayerPosition();
        var narrative = World.Get<NarrativeManager>();
        var time = World.Get<GameTime>().TotalGameTime.TotalSeconds;

        World.Query(in _emitters, (ref BarkEmitter emitter, ref Position pos) =>
        {
            if (time - emitter.LastBarkTime < emitter.GlobalCooldown) return;

            var dist = Vector2.Distance(pos.Value, playerPos);
            if (dist > emitter.DetectionRadius) return;

            if (!_barkSets.TryGetValue(emitter.BarkSetId, out var barks)) return;

            // Find highest-priority valid bark
            var best = barks
                .Where(b => b.Conditions == null ||
                    b.Conditions.All(c => ConditionEvaluator.Evaluate(c, narrative.Flags)))
                .Where(b => MatchesTrigger(b.Trigger))
                .OrderByDescending(b => b.Priority)
                .FirstOrDefault();

            if (best.Text != null)
            {
                emitter.LastBarkTime = (float)time;
                ShowBark(pos.Value, best.Text, best.Emotion);
            }
        });
    }

    private bool MatchesTrigger(BarkTrigger trigger) => trigger switch
    {
        BarkTrigger.Proximity => true,  // already distance-checked
        BarkTrigger.PlayerLowHp => GetPlayerHealthPercent() < 0.25f,
        BarkTrigger.Combat => World.Has<ActiveCombat>(),
        _ => true
    };

    private void ShowBark(Vector2 pos, string text, string? emotion)
    {
        // Create floating text entity above NPC
        World.Create(new FloatingText(text, pos + new Vector2(0, -20), 3.0f));
    }
}
```

---

## 12 — ECS Integration

### Core ECS Components

```csharp
// Attached to entities that can participate in dialogue
public record struct DialogueComponent(
    string DialogueTreeId,       // which dialogue JSON to load
    bool IsActive,
    string CurrentNodeId
);

// Attached to NPCs that emit barks
// (BarkEmitter defined in §11)

// Attached to entities with reputation interactions
public record struct FactionMember(
    string FactionId
);

// Attached to quest-giving NPCs
public record struct QuestGiver(
    string[] AvailableQuestIds
);
```

### StoryState Resource

```csharp
// World-level resource — access via World.Get<StoryState>()
public record struct StoryState(
    string ActiveDialogueTree,
    string ActiveDialogueNode,
    bool DialogueOpen,
    string? ActiveSpeaker,
    float DialogueTimer
);
```

### NarrativeEventSystem

The central hub that listens for story events and propagates them across all narrative subsystems:

```csharp
public record struct NarrativeEvent(string EventType, string Data);

public class NarrativeEventSystem : BaseSystem<World, float>
{
    private readonly Queue<NarrativeEvent> _eventQueue = new();

    public void Publish(NarrativeEvent evt) => _eventQueue.Enqueue(evt);

    public override void Update(in float dt)
    {
        var narrative = World.Get<NarrativeManager>();

        while (_eventQueue.TryDequeue(out var evt))
        {
            switch (evt.EventType)
            {
                case "dialogue_complete":
                    narrative.Flags.SetBool($"dlg_seen_{evt.Data}", true);
                    break;

                case "npc_killed":
                    narrative.Flags.SetBool($"npc_{evt.Data}_alive", false);
                    narrative.Reputation.Modify(GetNpcFaction(evt.Data), -20);
                    narrative.Journal.Unlock($"death_{evt.Data}");
                    break;

                case "item_acquired":
                    narrative.Flags.SetBool($"inventory_{evt.Data}", true);
                    // Check if any deferred consequences trigger
                    narrative.Consequences.CheckDeferred(narrative.Flags);
                    break;

                case "zone_entered":
                    narrative.Flags.Set("current_zone", evt.Data);
                    CheckZoneTriggers(evt.Data, narrative);
                    break;

                case "quest_complete":
                    narrative.Flags.Set($"quest_{evt.Data}", "complete");
                    ProcessConsequences(narrative);
                    break;
            }
        }

        // Process any consequences generated this frame
        ProcessConsequences(narrative);
    }

    private void CheckZoneTriggers(string zone, NarrativeManager narrative)
    {
        // First visit? Unlock lore
        if (!narrative.Flags.GetBool($"visited_{zone}"))
        {
            narrative.Flags.SetBool($"visited_{zone}", true);
            narrative.Journal.Unlock($"lore_{zone}");
        }
    }

    private void ProcessConsequences(NarrativeManager narrative)
    {
        foreach (var c in narrative.Consequences.DrainPending())
        {
            switch (c.Type)
            {
                case "set_flag": narrative.Flags.Set(c.Target, c.Value); break;
                case "add_rep": narrative.Reputation.Modify(c.Target, int.Parse(c.Value)); break;
                case "unlock_journal": narrative.Journal.Unlock(c.Target); break;
                case "trigger_event": Publish(new NarrativeEvent(c.Target, c.Value)); break;
            }
        }
    }

    private string GetNpcFaction(string npcId) => npcId; // look up from NPC data
}
```

### System Registration Order

```csharp
// In your system setup — order matters
world.Set(new NarrativeManager());
world.Set(new StoryState());

var narrativeEvents = new NarrativeEventSystem(world);

// Register systems in update order:
// 1. Input → 2. DialogueAdvance → 3. QuestNarrative → 4. BarkSystem
// 5. NarrativeEventSystem → 6. ConsequenceProcessing → 7. DialogueRender
```

---

## Design Checklist

- [ ] All dialogue is external data (JSON/Yarn/Ink) — no hardcoded strings
- [ ] Story flags have sensible defaults and are documented
- [ ] Every player choice writes at least one flag
- [ ] Deferred consequences are tested with save/load cycles
- [ ] Journal entries cover all major NPCs, locations, and lore
- [ ] Reputation thresholds are balanced and playtested
- [ ] Barks have cooldowns to avoid spam
- [ ] Multiple endings are reachable — verify with flag matrix
- [ ] Typewriter speed is adjustable in settings
- [ ] New Game+ correctly carries intended flags and strips the rest

---

## Further Reading

- **[G10 Custom Game Systems](./G10_custom_game_systems.md)** — Base dialogue and interaction systems this guide extends
- **[G45 Cutscenes](./G45_cutscenes.md)** — Scripted sequences that complement narrative events
- **[G5 UI Framework](./G5_ui_framework.md)** — Building the dialogue box, journal screen, and choice buttons
- **[G47 Achievements](./G47_achievements.md)** — Achievements triggered by story milestones and endings
- **[E6 Game Design Fundamentals](../../core/game-design/E6_game_design_fundamentals.md)** — Pacing, player agency, and narrative design principles
- [Yarn Spinner Documentation](https://docs.yarnspinner.dev/)
- [Ink by Inkle](https://github.com/inkle/ink)
