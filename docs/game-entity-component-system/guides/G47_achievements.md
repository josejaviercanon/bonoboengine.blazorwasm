# G47 — Achievements & Progression Systems


> **Category:** Guide · **Related:** [G10 Custom Game Systems](./G10_custom_game_systems.md) · [G36 Publishing & Distribution](./G36_publishing_distribution.md) · [G12 Design Patterns](./G12_design_patterns.md) · [G48 Online Services](./G48_online_services.md)

---

## Overview

Achievements and progression systems give players long-term goals, reward mastery, and increase replayability. This guide covers an event-driven achievement architecture built on **Arch ECS v2.1.0** with persistent storage, platform integration (Steam/Game Center), and UI notifications — all within MonoGame.Framework.DesktopGL.

---

## 1 — Achievement System Architecture

The system is **event-driven**: game logic publishes events, and the achievement manager subscribes to them without coupling gameplay code to achievement definitions.

### Core Components

```
GameEvent (EnemyKilled, ItemCollected, …)
       │
       ▼
 AchievementManager ──▶ checks conditions ──▶ unlocks achievement
       │                                            │
       ▼                                            ▼
 StatTracker (counters)                    AchievementUI (toast)
       │                                            │
       ▼                                            ▼
 SaveManager (persist)                   PlatformBridge (Steam/GC)
```

### Achievement Definition

```csharp
public record struct AchievementDef(
    string Id,
    string Name,
    string Description,
    string IconAsset,
    bool   IsHidden,
    AchievementKind Kind,
    string EventKey,        // which game event triggers checks
    int    TargetCount,     // 1 for booleans, N for cumulative
    string[] RequiredItems  // for collection-type achievements
);

public enum AchievementKind
{
    OneShot,      // single event triggers unlock
    Cumulative,   // counter reaches target
    Collection,   // all required items gathered
    Challenge,    // conditional (no-damage, speed-run, etc.)
    Secret        // hidden until unlocked
}
```

### Achievement State

```csharp
public enum AchievementStatus { Locked, InProgress, Unlocked }

public record struct AchievementState(
    string Id,
    AchievementStatus Status,
    int    CurrentCount,
    HashSet<string> CollectedItems,
    DateTime? UnlockedAt
);
```

---

## 2 — Achievement Types

| Type | Trigger | Example | Storage |
|------|---------|---------|---------|
| **One-shot** | Single event | Defeat final boss | `bool` |
| **Cumulative** | Counter ≥ target | Kill 1 000 enemies | `int` counter |
| **Collection** | All items found | Find all 50 gems | `HashSet<string>` |
| **Challenge** | Conditional completion | Finish level 3 without damage | `bool` + condition check |
| **Secret/Hidden** | Any, hidden until unlocked | Discover hidden room | Same as above, `IsHidden = true` |

### Counter-Based vs Boolean

```csharp
public static class AchievementLogic
{
    public static bool Evaluate(AchievementDef def, AchievementState state)
    {
        return def.Kind switch
        {
            AchievementKind.OneShot    => state.CurrentCount >= 1,
            AchievementKind.Cumulative => state.CurrentCount >= def.TargetCount,
            AchievementKind.Collection => def.RequiredItems.All(
                                            item => state.CollectedItems.Contains(item)),
            AchievementKind.Challenge  => state.CurrentCount >= 1,
            AchievementKind.Secret     => state.CurrentCount >= def.TargetCount,
            _ => false
        };
    }
}
```

---

## 3 — Event-Driven Tracking

### Game Event Bus

A lightweight pub/sub bus decouples game logic from achievement tracking.

```csharp
public readonly record struct GameEvent(string Key, Dictionary<string, object>? Data = null);

public sealed class EventBus
{
    private readonly Dictionary<string, List<Action<GameEvent>>> _subs = new();

    public void Subscribe(string key, Action<GameEvent> handler)
    {
        if (!_subs.TryGetValue(key, out var list))
        {
            list = new List<Action<GameEvent>>();
            _subs[key] = list;
        }
        list.Add(handler);
    }

    public void Unsubscribe(string key, Action<GameEvent> handler)
    {
        if (_subs.TryGetValue(key, out var list))
            list.Remove(handler);
    }

    public void Publish(GameEvent evt)
    {
        if (_subs.TryGetValue(evt.Key, out var list))
            foreach (var handler in list)
                handler(evt);
    }
}
```

### Standard Event Keys

```csharp
public static class Events
{
    public const string EnemyKilled     = "enemy_killed";
    public const string ItemCollected   = "item_collected";
    public const string LevelCompleted  = "level_completed";
    public const string BossDead        = "boss_dead";
    public const string PlayerDied      = "player_died";
    public const string DamageTaken     = "damage_taken";
    public const string SecretFound     = "secret_found";
    public const string DistanceMoved   = "distance_moved";
}
```

### Publishing from Game Logic (No Achievement Coupling)

```csharp
// In combat system — knows nothing about achievements
public void OnEnemyDestroyed(Entity enemy)
{
    var info = World.Get<EnemyInfo>(enemy);
    _eventBus.Publish(new GameEvent(Events.EnemyKilled, new()
    {
        ["enemy_type"] = info.TypeId,
        ["level"]      = _currentLevel
    }));
}
```

---

## 4 — Achievement Data Model

### JSON Definition (`Content/Data/achievements.json`)

```json
{
  "achievements": [
    {
      "id": "first_blood",
      "name": "First Blood",
      "description": "Defeat your first enemy.",
      "icon": "ach_first_blood",
      "hidden": false,
      "kind": "OneShot",
      "eventKey": "enemy_killed",
      "targetCount": 1,
      "requiredItems": []
    },
    {
      "id": "slayer_1000",
      "name": "Legendary Slayer",
      "description": "Defeat 1,000 enemies.",
      "icon": "ach_slayer",
      "hidden": false,
      "kind": "Cumulative",
      "eventKey": "enemy_killed",
      "targetCount": 1000,
      "requiredItems": []
    },
    {
      "id": "gem_collector",
      "name": "Gem Hoarder",
      "description": "Find all 50 hidden gems.",
      "icon": "ach_gems",
      "hidden": false,
      "kind": "Collection",
      "eventKey": "item_collected",
      "targetCount": 50,
      "requiredItems": [
        "gem_01", "gem_02", "gem_03", "gem_04", "gem_05",
        "gem_06", "gem_07", "gem_08", "gem_09", "gem_10"
      ]
    },
    {
      "id": "no_damage_3",
      "name": "Untouchable",
      "description": "Complete Level 3 without taking damage.",
      "icon": "ach_untouchable",
      "hidden": false,
      "kind": "Challenge",
      "eventKey": "level_completed",
      "targetCount": 1,
      "requiredItems": []
    },
    {
      "id": "secret_room",
      "name": "???",
      "description": "Find the hidden developer room.",
      "icon": "ach_secret",
      "hidden": true,
      "kind": "Secret",
      "eventKey": "secret_found",
      "targetCount": 1,
      "requiredItems": []
    }
  ]
}
```

### Loading Definitions

```csharp
public sealed class AchievementRegistry
{
    private readonly Dictionary<string, AchievementDef> _defs = new();

    public IReadOnlyDictionary<string, AchievementDef> All => _defs;

    public void LoadFromJson(string jsonPath)
    {
        var json = File.ReadAllText(jsonPath);
        var root = JsonSerializer.Deserialize<AchievementFile>(json)!;

        foreach (var raw in root.Achievements)
        {
            var def = new AchievementDef(
                raw.Id, raw.Name, raw.Description, raw.Icon,
                raw.Hidden,
                Enum.Parse<AchievementKind>(raw.Kind),
                raw.EventKey, raw.TargetCount,
                raw.RequiredItems ?? Array.Empty<string>()
            );
            _defs[def.Id] = def;
        }
    }
}

// Deserialization DTO
public record AchievementJson(
    string   Id,
    string   Name,
    string   Description,
    string   Icon,
    bool     Hidden,
    string   Kind,
    string   EventKey,
    int      TargetCount,
    string[]? RequiredItems
);

public record AchievementFile(AchievementJson[] Achievements);
```

### Save / Load Progress

```csharp
public sealed class AchievementSaveData
{
    public Dictionary<string, AchievementStateSave> States { get; set; } = new();
    public float PercentComplete { get; set; }
}

public record AchievementStateSave(
    string   Id,
    string   Status,
    int      CurrentCount,
    string[] CollectedItems,
    string?  UnlockedAt
);

public sealed class AchievementPersistence
{
    private const string SaveFile = "achievements.json";

    public static void Save(Dictionary<string, AchievementState> states, int totalCount)
    {
        int unlocked = states.Values.Count(s => s.Status == AchievementStatus.Unlocked);
        var data = new AchievementSaveData
        {
            PercentComplete = totalCount > 0 ? (float)unlocked / totalCount * 100f : 0f,
            States = states.ToDictionary(
                kv => kv.Key,
                kv => new AchievementStateSave(
                    kv.Value.Id,
                    kv.Value.Status.ToString(),
                    kv.Value.CurrentCount,
                    kv.Value.CollectedItems.ToArray(),
                    kv.Value.UnlockedAt?.ToString("o")
                ))
        };

        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SaveFile, json);
    }

    public static Dictionary<string, AchievementState> Load()
    {
        if (!File.Exists(SaveFile))
            return new();

        var json = File.ReadAllText(SaveFile);
        var data = JsonSerializer.Deserialize<AchievementSaveData>(json)!;

        return data.States.ToDictionary(
            kv => kv.Key,
            kv => new AchievementState(
                kv.Value.Id,
                Enum.Parse<AchievementStatus>(kv.Value.Status),
                kv.Value.CurrentCount,
                new HashSet<string>(kv.Value.CollectedItems),
                kv.Value.UnlockedAt != null ? DateTime.Parse(kv.Value.UnlockedAt) : null
            ));
    }
}
```

---

## 5 — Achievement UI

### Toast Notification

A small pop-up slides in when an achievement unlocks, stays visible for a few seconds, then fades out.

```csharp
public sealed class AchievementToast
{
    private readonly record struct ToastEntry(
        AchievementDef Def, float Timer, float Alpha);

    private const float DisplayDuration = 4f;
    private const float FadeDuration    = 0.5f;
    private const int   ToastWidth      = 320;
    private const int   ToastHeight     = 64;
    private const int   Padding         = 12;

    private readonly Queue<AchievementDef> _pending = new();
    private ToastEntry? _current;
    private readonly SpriteFont _font;
    private readonly Dictionary<string, Texture2D> _icons;

    public AchievementToast(SpriteFont font, Dictionary<string, Texture2D> icons)
    {
        _font  = font;
        _icons = icons;
    }

    public void Enqueue(AchievementDef def) => _pending.Enqueue(def);

    public void Update(float dt)
    {
        if (_current is { } entry)
        {
            entry = entry with { Timer = entry.Timer - dt };
            if (entry.Timer <= 0f)
                _current = null;
            else if (entry.Timer < FadeDuration)
                _current = entry with { Alpha = entry.Timer / FadeDuration };
            else
                _current = entry;
        }

        if (_current is null && _pending.Count > 0)
            _current = new ToastEntry(_pending.Dequeue(), DisplayDuration, 1f);
    }

    public void Draw(SpriteBatch sb, int screenWidth)
    {
        if (_current is not { } entry) return;

        int x = screenWidth - ToastWidth - Padding;
        int y = Padding;
        var color = Color.White * entry.Alpha;
        var bgColor = new Color(20, 20, 20, (int)(200 * entry.Alpha));

        // Background
        sb.Draw(PixelTexture, new Rectangle(x, y, ToastWidth, ToastHeight), bgColor);

        // Icon
        if (_icons.TryGetValue(entry.Def.IconAsset, out var icon))
            sb.Draw(icon, new Rectangle(x + 8, y + 8, 48, 48), color);

        // Text
        sb.DrawString(_font, "Achievement Unlocked!", 
            new Vector2(x + 64, y + 8), Color.Gold * entry.Alpha);
        sb.DrawString(_font, entry.Def.Name, 
            new Vector2(x + 64, y + 32), color);
    }

    // 1×1 white pixel; create at init
    public Texture2D PixelTexture { get; set; } = null!;
}
```

### Achievement List Screen

```csharp
public sealed class AchievementListScreen
{
    private readonly AchievementRegistry _registry;
    private readonly Dictionary<string, AchievementState> _states;
    private readonly SpriteFont _titleFont;
    private readonly SpriteFont _bodyFont;
    private readonly Dictionary<string, Texture2D> _icons;
    private int _scrollOffset;

    public AchievementListScreen(
        AchievementRegistry registry,
        Dictionary<string, AchievementState> states,
        SpriteFont titleFont, SpriteFont bodyFont,
        Dictionary<string, Texture2D> icons)
    {
        _registry  = registry;
        _states    = states;
        _titleFont = titleFont;
        _bodyFont  = bodyFont;
        _icons     = icons;
    }

    public void Draw(SpriteBatch sb, Rectangle viewport)
    {
        int y = viewport.Y + 16 - _scrollOffset;
        int unlocked = _states.Values.Count(s => s.Status == AchievementStatus.Unlocked);
        int total    = _registry.All.Count;

        // Header
        sb.DrawString(_titleFont, $"Achievements — {unlocked}/{total}",
            new Vector2(viewport.X + 16, y), Color.White);
        y += 48;

        foreach (var (id, def) in _registry.All)
        {
            var state = _states.GetValueOrDefault(id,
                new AchievementState(id, AchievementStatus.Locked, 0, new(), null));

            bool hidden = def.IsHidden && state.Status != AchievementStatus.Unlocked;
            var name = hidden ? "???" : def.Name;
            var desc = hidden ? "This achievement is hidden." : def.Description;
            var nameColor = state.Status == AchievementStatus.Unlocked 
                ? Color.Gold : Color.Gray;

            sb.DrawString(_bodyFont, name, new Vector2(viewport.X + 80, y), nameColor);
            sb.DrawString(_bodyFont, desc,  new Vector2(viewport.X + 80, y + 20), Color.LightGray);

            // Progress bar for cumulative
            if (def.Kind == AchievementKind.Cumulative &&
                state.Status != AchievementStatus.Unlocked)
            {
                float pct = Math.Clamp((float)state.CurrentCount / def.TargetCount, 0f, 1f);
                DrawProgressBar(sb, viewport.X + 80, y + 42, 200, 10, pct);
                sb.DrawString(_bodyFont, $"{state.CurrentCount}/{def.TargetCount}",
                    new Vector2(viewport.X + 290, y + 38), Color.LightGray);
            }

            y += 72;
        }
    }

    private void DrawProgressBar(SpriteBatch sb, int x, int y, int w, int h, float pct)
    {
        // Background
        sb.Draw(_pixel, new Rectangle(x, y, w, h), new Color(40, 40, 40));
        // Fill
        sb.Draw(_pixel, new Rectangle(x, y, (int)(w * pct), h), Color.Gold);
    }

    private Texture2D _pixel = null!;
    public void SetPixel(Texture2D px) => _pixel = px;

    public void ScrollUp()   => _scrollOffset = Math.Max(0, _scrollOffset - 40);
    public void ScrollDown() => _scrollOffset += 40;
}
```

---

## 6 — Platform Integration

### Steam via Steamworks.NET

```csharp
public sealed class SteamAchievementBridge : IDisposable
{
    private bool _initialized;

    public bool Init(uint appId)
    {
        _initialized = Steamworks.SteamAPI.Init();
        return _initialized;
    }

    /// <summary>
    /// Call when a local achievement unlocks.
    /// Steam achievement API name must match the Id string.
    /// </summary>
    public void Unlock(string achievementId)
    {
        if (!_initialized) return;
        Steamworks.SteamUserStats.SetAchievement(achievementId);
        Steamworks.SteamUserStats.StoreStats();
    }

    /// <summary>
    /// Report incremental progress (Steam shows a progress notification).
    /// </summary>
    public void SetProgress(string achievementId, int current, int max)
    {
        if (!_initialized) return;
        Steamworks.SteamUserStats.IndicateAchievementProgress(
            achievementId, (uint)current, (uint)max);
    }

    /// <summary>
    /// Pull unlock state from Steam to sync on first launch.
    /// </summary>
    public bool IsUnlockedOnPlatform(string achievementId)
    {
        if (!_initialized) return false;
        Steamworks.SteamUserStats.GetAchievement(achievementId, out bool unlocked);
        return unlocked;
    }

    public void Dispose()
    {
        if (_initialized) Steamworks.SteamAPI.Shutdown();
    }
}
```

### iOS Game Center

```csharp
#if IOS
public static class GameCenterBridge
{
    /// <summary>
    /// Report achievement progress (0.0 – 100.0).
    /// Call from the platform project via dependency injection.
    /// </summary>
    public static void ReportProgress(string achievementId, double percentComplete)
    {
        var achievement = new GameKit.GKAchievement(achievementId)
        {
            PercentComplete = percentComplete,
            ShowsCompletionBanner = true
        };
        GameKit.GKAchievement.ReportAchievements(
            new[] { achievement }, error =>
        {
            if (error != null)
                System.Diagnostics.Debug.WriteLine(
                    $"GC report error: {error.LocalizedDescription}");
        });
    }
}
#endif
```

### Platform Abstraction

```csharp
public interface IPlatformAchievements
{
    void Unlock(string id);
    void SetProgress(string id, int current, int max);
    bool IsUnlockedOnPlatform(string id);
}

// Wire up via constructor injection in AchievementManager.
// On unsupported platforms, use a NullPlatformAchievements that no-ops.
public sealed class NullPlatformAchievements : IPlatformAchievements
{
    public void Unlock(string id) { }
    public void SetProgress(string id, int current, int max) { }
    public bool IsUnlockedOnPlatform(string id) => false;
}
```

---

## 7 — Progression Systems

### XP & Leveling

```csharp
public record struct PlayerProgression(
    int   Level,
    float CurrentXP,
    float XPToNextLevel
);

public sealed class LevelingService
{
    // XP curve: each level requires 20% more XP than the last
    private const float BaseXP     = 100f;
    private const float GrowthRate = 1.20f;

    public float XPForLevel(int level) =>
        BaseXP * MathF.Pow(GrowthRate, level - 1);

    public PlayerProgression AddXP(PlayerProgression prog, float xp)
    {
        float current = prog.CurrentXP + xp;
        int   level   = prog.Level;
        float needed  = XPForLevel(level);

        while (current >= needed)
        {
            current -= needed;
            level++;
            needed = XPForLevel(level);
        }

        return new PlayerProgression(level, current, needed);
    }
}
```

### Unlock System

```csharp
public record struct Unlockable(
    string Id,
    string Name,
    UnlockKind Kind,      // Ability, Cosmetic, Level, Weapon
    string Requirement    // "level:10" or "achievement:slayer_1000"
);

public enum UnlockKind { Ability, Cosmetic, Level, Weapon }

public sealed class UnlockManager
{
    private readonly List<Unlockable> _all;
    private readonly HashSet<string>  _unlocked = new();

    public UnlockManager(List<Unlockable> defs) => _all = defs;

    public IReadOnlySet<string> Unlocked => _unlocked;

    public List<Unlockable> CheckNewUnlocks(
        PlayerProgression prog,
        Dictionary<string, AchievementState> achStates)
    {
        var newlyUnlocked = new List<Unlockable>();

        foreach (var u in _all)
        {
            if (_unlocked.Contains(u.Id)) continue;

            bool met = u.Requirement switch
            {
                var r when r.StartsWith("level:") =>
                    prog.Level >= int.Parse(r[6..]),
                var r when r.StartsWith("achievement:") =>
                    achStates.TryGetValue(r[12..], out var s)
                    && s.Status == AchievementStatus.Unlocked,
                _ => false
            };

            if (met)
            {
                _unlocked.Add(u.Id);
                newlyUnlocked.Add(u);
            }
        }

        return newlyUnlocked;
    }
}
```

### New Game+ / Prestige

```csharp
public record struct PrestigeData(
    int   PrestigeLevel,
    float DifficultyMultiplier,  // e.g. 1.5× per prestige
    int[] RetainedUnlocks         // unlockable IDs that carry over
);

public sealed class PrestigeService
{
    public PrestigeData Prestige(PrestigeData current)
    {
        return current with
        {
            PrestigeLevel       = current.PrestigeLevel + 1,
            DifficultyMultiplier = 1f + current.PrestigeLevel * 0.5f
        };
    }

    /// <summary>
    /// Reset player progression but keep prestige-level unlocks.
    /// </summary>
    public PlayerProgression ResetProgression() =>
        new(Level: 1, CurrentXP: 0f, XPToNextLevel: 100f);
}
```

---

## 8 — Statistics Tracking

### Stat Tracker Service

```csharp
public sealed class StatTracker
{
    private readonly Dictionary<string, double> _stats = new();

    public double Get(string key) =>
        _stats.GetValueOrDefault(key, 0.0);

    public void Increment(string key, double amount = 1.0) =>
        _stats[key] = Get(key) + amount;

    public void Set(string key, double value) =>
        _stats[key] = value;

    public IReadOnlyDictionary<string, double> All => _stats;

    // --- Persistence ---
    public void Save(string path)
    {
        var json = JsonSerializer.Serialize(_stats,
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    public void Load(string path)
    {
        if (!File.Exists(path)) return;
        var json = File.ReadAllText(path);
        var data = JsonSerializer.Deserialize<Dictionary<string, double>>(json)!;
        foreach (var (k, v) in data)
            _stats[k] = v;
    }
}
```

### Standard Stat Keys

```csharp
public static class Stats
{
    public const string TotalPlaytime    = "total_playtime_sec";
    public const string EnemiesKilled    = "enemies_killed";
    public const string Deaths           = "deaths";
    public const string DistanceTraveled = "distance_traveled";
    public const string DamageDealt      = "damage_dealt";
    public const string DamageTaken      = "damage_taken";
    public const string ItemsCollected   = "items_collected";
    public const string LevelsCompleted  = "levels_completed";
    public const string JumpsPerformed   = "jumps_performed";
    public const string BossesDefeated   = "bosses_defeated";
}
```

### Wiring Stats to Events

```csharp
// During AchievementManager.Init():
_eventBus.Subscribe(Events.EnemyKilled,    _ => _stats.Increment(Stats.EnemiesKilled));
_eventBus.Subscribe(Events.PlayerDied,     _ => _stats.Increment(Stats.Deaths));
_eventBus.Subscribe(Events.ItemCollected,  _ => _stats.Increment(Stats.ItemsCollected));
_eventBus.Subscribe(Events.LevelCompleted, _ => _stats.Increment(Stats.LevelsCompleted));
_eventBus.Subscribe(Events.BossDead,       _ => _stats.Increment(Stats.BossesDefeated));
```

### Using Stats as Achievement Inputs

Cumulative achievements read directly from the stat tracker rather than maintaining a separate counter:

```csharp
// In the achievement check loop:
if (def.Kind == AchievementKind.Cumulative)
{
    int count = (int)_stats.Get(def.EventKey == Events.EnemyKilled
        ? Stats.EnemiesKilled
        : def.EventKey); // map event key to stat key
    state = state with { CurrentCount = count };
}
```

---

## 9 — Reward Systems

### Reward Definitions (JSON)

```json
{
  "rewards": [
    {
      "achievementId": "slayer_1000",
      "type": "Currency",
      "value": "500"
    },
    {
      "achievementId": "gem_collector",
      "type": "Cosmetic",
      "value": "skin_golden_armor"
    },
    {
      "achievementId": "no_damage_3",
      "type": "Ability",
      "value": "dash_upgrade"
    }
  ]
}
```

### Reward Dispatcher

```csharp
public record struct RewardDef(string AchievementId, RewardType Type, string Value);
public enum RewardType { Currency, Cosmetic, Ability }

public sealed class RewardService
{
    private readonly Dictionary<string, RewardDef> _rewards;
    private readonly UnlockManager _unlocks;

    public RewardService(List<RewardDef> defs, UnlockManager unlocks)
    {
        _rewards  = defs.ToDictionary(r => r.AchievementId);
        _unlocks  = unlocks;
    }

    /// <summary>
    /// Called by AchievementManager when an achievement unlocks.
    /// Returns a human-readable reward description for UI.
    /// </summary>
    public string? GrantReward(string achievementId, ref PlayerProgression prog)
    {
        if (!_rewards.TryGetValue(achievementId, out var reward))
            return null;

        return reward.Type switch
        {
            RewardType.Currency =>
                GrantCurrency(int.Parse(reward.Value)),
            RewardType.Cosmetic =>
                $"Unlocked cosmetic: {reward.Value}",
            RewardType.Ability =>
                $"New ability: {reward.Value}",
            _ => null
        };
    }

    private string GrantCurrency(int amount)
    {
        // Add to player wallet (your currency service here)
        return $"+{amount} coins";
    }
}
```

---

## 10 — ECS Integration

### AchievementManager as a World Service

In Arch ECS, use `World.Set<T>()` to register singleton services that systems can access.

```csharp
public sealed class AchievementManager
{
    private readonly AchievementRegistry _registry;
    private readonly Dictionary<string, AchievementState> _states;
    private readonly EventBus _eventBus;
    private readonly StatTracker _stats;
    private readonly AchievementToast _toast;
    private readonly IPlatformAchievements _platform;
    private readonly RewardService _rewards;

    // Challenge state: track per-level conditions
    private readonly Dictionary<string, bool> _challengeFlags = new();

    public AchievementManager(
        AchievementRegistry registry,
        EventBus eventBus,
        StatTracker stats,
        AchievementToast toast,
        IPlatformAchievements platform,
        RewardService rewards)
    {
        _registry = registry;
        _eventBus = eventBus;
        _stats    = stats;
        _toast    = toast;
        _platform = platform;
        _rewards  = rewards;
        _states   = AchievementPersistence.Load();

        // Ensure every defined achievement has a state entry
        foreach (var (id, _) in _registry.All)
        {
            if (!_states.ContainsKey(id))
                _states[id] = new AchievementState(
                    id, AchievementStatus.Locked, 0, new(), null);
        }

        SubscribeAll();
    }

    private void SubscribeAll()
    {
        // Subscribe to every unique event key used by achievements
        var eventKeys = _registry.All.Values
            .Select(d => d.EventKey).Distinct();

        foreach (var key in eventKeys)
            _eventBus.Subscribe(key, evt => OnGameEvent(evt));

        // Challenge: track damage to invalidate no-damage runs
        _eventBus.Subscribe(Events.DamageTaken, _ =>
            _challengeFlags["no_damage_current_level"] = true);
    }

    private void OnGameEvent(GameEvent evt)
    {
        foreach (var (id, def) in _registry.All)
        {
            if (def.EventKey != evt.Key) continue;
            var state = _states[id];
            if (state.Status == AchievementStatus.Unlocked) continue;

            // Update state based on kind
            state = def.Kind switch
            {
                AchievementKind.OneShot or
                AchievementKind.Cumulative or
                AchievementKind.Secret =>
                    state with { CurrentCount = state.CurrentCount + 1,
                                 Status = AchievementStatus.InProgress },

                AchievementKind.Collection when
                    evt.Data?.TryGetValue("item_id", out var itemObj) == true =>
                    AddCollectionItem(state, (string)itemObj),

                AchievementKind.Challenge when evt.Key == Events.LevelCompleted =>
                    EvaluateChallenge(state, def, evt),

                _ => state
            };

            // Check completion
            if (AchievementLogic.Evaluate(def, state))
            {
                state = state with
                {
                    Status     = AchievementStatus.Unlocked,
                    UnlockedAt = DateTime.UtcNow
                };
                _toast.Enqueue(def);
                _platform.Unlock(id);

                var prog = new PlayerProgression(); // pass real progression
                _rewards.GrantReward(id, ref prog);
            }

            _states[id] = state;
        }

        Save();
    }

    private AchievementState AddCollectionItem(AchievementState state, string itemId)
    {
        var items = new HashSet<string>(state.CollectedItems) { itemId };
        return state with
        {
            CollectedItems = items,
            CurrentCount   = items.Count,
            Status         = AchievementStatus.InProgress
        };
    }

    private AchievementState EvaluateChallenge(
        AchievementState state, AchievementDef def, GameEvent evt)
    {
        // Example: no-damage challenge for a specific level
        bool tookDamage = _challengeFlags.GetValueOrDefault("no_damage_current_level");
        _challengeFlags["no_damage_current_level"] = false; // reset for next level

        if (!tookDamage)
            return state with { CurrentCount = 1, Status = AchievementStatus.InProgress };
        return state;
    }

    public IReadOnlyDictionary<string, AchievementState> States => _states;
    public float PercentComplete =>
        _registry.All.Count > 0
            ? (float)_states.Values.Count(s => s.Status == AchievementStatus.Unlocked)
              / _registry.All.Count * 100f
            : 0f;

    public void Save() =>
        AchievementPersistence.Save(_states, _registry.All.Count);
}
```

### Registering in the ECS World

```csharp
// In Game.Initialize() or your boot sequence:
var eventBus   = new EventBus();
var stats      = new StatTracker();
var registry   = new AchievementRegistry();
registry.LoadFromJson("Content/Data/achievements.json");

var toast      = new AchievementToast(font, iconMap);
var platform   = new NullPlatformAchievements(); // swap for Steam/GC
var rewards    = new RewardService(rewardDefs, unlockManager);

var manager = new AchievementManager(
    registry, eventBus, stats, toast, platform, rewards);

// Register as world-level services for ECS systems to access
world.Set(eventBus);
world.Set(stats);
world.Set(manager);
```

### ECS Systems Publishing Events

```csharp
public partial class CombatSystem : GameSystem
{
    public override void Update(float dt)
    {
        var eventBus = World.Get<EventBus>();

        World.Query(new QueryDescription().WithAll<Health, DamageReceived>(),
            (Entity entity, ref Health hp, ref DamageReceived dmg) =>
        {
            hp.Current -= dmg.Amount;
            if (hp.Current <= 0f)
            {
                eventBus.Publish(new GameEvent(Events.EnemyKilled, new()
                {
                    ["enemy_type"] = World.Get<EnemyInfo>(entity).TypeId
                }));
                World.Destroy(entity);
            }
            World.Remove<DamageReceived>(entity);
        });
    }
}
```

### StatTracker Update System

```csharp
public partial class StatUpdateSystem : GameSystem
{
    public override void Update(float dt)
    {
        var stats = World.Get<StatTracker>();

        // Track playtime
        stats.Increment(Stats.TotalPlaytime, dt);

        // Track distance
        World.Query(new QueryDescription().WithAll<Position, Velocity, PlayerTag>(),
            (ref Position pos, ref Velocity vel) =>
        {
            float dist = vel.Value.Length() * dt;
            stats.Increment(Stats.DistanceTraveled, dist);
        });
    }
}
```

---

## Quick-Start Checklist

1. **Define** achievements in `achievements.json` with IDs, types, and targets
2. **Create** `AchievementRegistry` and load definitions at startup
3. **Wire** `EventBus` into game systems — publish events, never reference achievements directly
4. **Register** `AchievementManager`, `StatTracker`, and `EventBus` on the Arch `World`
5. **Add** `AchievementToast` to your UI draw loop
6. **Persist** state via `AchievementPersistence` on save/quit
7. **Integrate** platform SDK (Steamworks.NET / Game Center) behind `IPlatformAchievements`
8. **Test** — unlock an achievement, close the game, relaunch, verify it stays unlocked

---

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Achievement logic scattered across game code | Route everything through `EventBus` |
| Stats and achievement counters diverge | Use `StatTracker` as the single source of truth for cumulative counts |
| Hidden achievements show name in UI | Check `IsHidden && Status != Unlocked` before displaying |
| Platform sync fails silently | Log errors, fall back to local state, retry on next launch |
| Challenge achievements unlock incorrectly | Reset condition flags at level start, not at achievement check |
| Save corruption loses progress | Write to temp file first, rename on success (atomic write) |

---

*Next: [G48 Online Services](./G48_online_services.md) — leaderboards, cloud saves, and multiplayer matchmaking.*
