# G48 — Online Services



> **Category:** Guide · **Related:** [G9 Networking](./G9_networking.md) · [G36 Publishing & Distribution](./G36_publishing_distribution.md) · [G47 Achievements](./G47_achievements.md) · [G10 Custom Game Systems](./G10_custom_game_systems.md)

---

## Overview

Online services go beyond raw networking (covered in [G9](./G9_networking.md)). Where networking deals with packets, sockets, and state synchronization, **online services** are the platform-level features players expect: leaderboards, cloud saves, matchmaking, rich presence, analytics, and authentication.

Three tiers of platform support matter for indie 2D games:

| Platform | Leaderboards | Cloud Saves | Matchmaking | Rich Presence | Auth |
|----------|-------------|-------------|-------------|---------------|------|
| **Steam** | Full (Steamworks.NET) | Steam Cloud | Lobbies + P2P | Steam + Discord | Auth tickets |
| **iOS (Game Center)** | Full (GameKit) | iCloud Key-Value | Game Center matchmaking | Limited | Game Center ID |
| **itch.io** | None built-in | None built-in | None | None | None |

For itch.io and DRM-free builds, you either roll your own backend or provide graceful local-only fallbacks. The abstraction layer in Section 10 makes this manageable.

**Stack:** All examples use MonoGame.Framework.DesktopGL + .NET 8 + Steamworks.NET.

---

## Leaderboards

### Steam Leaderboards via Steamworks.NET

Steam leaderboards are created at runtime or via the Steamworks partner site. Runtime creation is simpler for iteration:

```csharp
public class SteamLeaderboardManager
{
    private SteamLeaderboard_t _currentBoard;
    private CallResult<LeaderboardFindResult_t> _findResult;
    private CallResult<LeaderboardScoreUploaded_t> _uploadResult;
    private CallResult<LeaderboardScoresDownloaded_t> _downloadResult;

    public void FindOrCreateLeaderboard(string name,
        ELeaderboardSortMethod sort = ELeaderboardSortMethod.k_ELeaderboardSortMethodDescending,
        ELeaderboardDisplayType display = ELeaderboardDisplayType.k_ELeaderboardDisplayTypeNumeric)
    {
        var call = SteamUserStats.FindOrCreateLeaderboard(name, sort, display);
        _findResult = CallResult<LeaderboardFindResult_t>.Create(OnLeaderboardFound);
        _findResult.Set(call);
    }

    private void OnLeaderboardFound(LeaderboardFindResult_t result, bool ioFailure)
    {
        if (!ioFailure && result.m_bLeaderboardFound == 1)
        {
            _currentBoard = result.m_hSteamLeaderboard;
        }
    }

    public void UploadScore(int score, int[] details = null)
    {
        if (_currentBoard.m_SteamLeaderboard == 0) return;

        var call = SteamUserStats.UploadLeaderboardScore(
            _currentBoard,
            ELeaderboardUploadScoreMethod.k_ELeaderboardUploadScoreMethodKeepBest,
            score,
            details,
            details?.Length ?? 0);

        _uploadResult = CallResult<LeaderboardScoreUploaded_t>.Create(OnScoreUploaded);
        _uploadResult.Set(call);
    }

    private void OnScoreUploaded(LeaderboardScoreUploaded_t result, bool ioFailure)
    {
        if (!ioFailure && result.m_bSuccess == 1)
        {
            // result.m_bScoreChanged indicates if this was a new personal best
            // result.m_nGlobalRankNew gives the new global rank
        }
    }

    public void DownloadScores(ELeaderboardDataRequest type, int start, int end)
    {
        var call = SteamUserStats.DownloadLeaderboardEntries(
            _currentBoard, type, start, end);

        _downloadResult = CallResult<LeaderboardScoresDownloaded_t>.Create(OnScoresDownloaded);
        _downloadResult.Set(call);
    }

    private void OnScoresDownloaded(LeaderboardScoresDownloaded_t result, bool ioFailure)
    {
        if (ioFailure) return;

        var entries = new List<LeaderboardEntry_t>();
        for (int i = 0; i < result.m_cEntryCount; i++)
        {
            SteamUserStats.GetDownloadedLeaderboardEntry(
                result.m_hSteamLeaderboardEntries, i,
                out LeaderboardEntry_t entry, null, 0);
            entries.Add(entry);
        }
        // entries now contains rank, steamID, score for each row
    }
}
```

**Friends-only vs global:** Pass `ELeaderboardDataRequest.k_ELeaderboardDataRequestFriends` to `DownloadScores` for the friends list. Use `k_ELeaderboardDataRequestGlobal` for the top scores, and `k_ELeaderboardDataRequestGlobalAroundUser` for scores near the current player.

### Local Leaderboard Fallback

For non-Steam builds or offline play, maintain a local JSON leaderboard:

```csharp
public class LocalLeaderboard
{
    private readonly string _filePath;
    private List<LocalScore> _scores = new();
    private const int MaxEntries = 100;

    public LocalLeaderboard(string name)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "YourGame", "Leaderboards");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, $"{name}.json");
        Load();
    }

    public int Submit(string playerName, int score)
    {
        _scores.Add(new LocalScore(playerName, score, DateTime.UtcNow));
        _scores = _scores.OrderByDescending(s => s.Score)
                         .Take(MaxEntries).ToList();
        Save();
        return _scores.FindIndex(s => s.Score == score
            && s.PlayerName == playerName) + 1;
    }

    public IReadOnlyList<LocalScore> GetTopScores(int count = 10)
        => _scores.Take(count).ToList();

    private void Load()
    {
        if (File.Exists(_filePath))
            _scores = JsonSerializer.Deserialize<List<LocalScore>>(
                File.ReadAllText(_filePath)) ?? new();
    }

    private void Save()
        => File.WriteAllText(_filePath, JsonSerializer.Serialize(_scores));
}

public record LocalScore(string PlayerName, int Score, DateTime Timestamp);
```

### Score Validation

Never trust the client blindly. Even on Steam, basic validation helps:

- **Clamp to possible ranges.** If max score per level is 10,000, reject 999,999.
- **Attach replay data** as leaderboard detail bytes — lets you verify suspicious scores.
- **Rate-limit submissions.** One score per level completion, not one per frame.

For server-authoritative validation, see Section 8.

---

## Cloud Saves

### Steam Cloud (ISteamRemoteStorage)

Steam Cloud is the simplest cloud save option — just read/write files to Steam's remote storage:

```csharp
public class SteamCloudSave
{
    public bool Save(string filename, byte[] data)
    {
        return SteamRemoteStorage.FileWrite(filename, data, data.Length);
    }

    public byte[] Load(string filename)
    {
        int size = SteamRemoteStorage.GetFileSize(filename);
        if (size <= 0) return null;

        byte[] buffer = new byte[size];
        int read = SteamRemoteStorage.FileRead(filename, buffer, size);
        return read > 0 ? buffer : null;
    }

    public bool Exists(string filename)
        => SteamRemoteStorage.FileExists(filename);

    public bool Delete(string filename)
        => SteamRemoteStorage.FileDelete(filename);

    public long GetTimestamp(string filename)
        => SteamRemoteStorage.GetFileTimestamp(filename);

    public void ListAllFiles()
    {
        int count = SteamRemoteStorage.GetFileCount();
        for (int i = 0; i < count; i++)
        {
            string name = SteamRemoteStorage.GetFileNameAndSize(i, out int fileSize);
            // name, fileSize available
        }
    }
}
```

Configure in `steam_appid.txt` and the Steamworks partner site under Cloud settings — set your byte quota and file count limit.

### Conflict Resolution

Cloud sync conflicts happen when a player plays offline on two machines. Two approaches:

1. **Latest-timestamp wins** — simple, works for most games. Embed a UTC timestamp in your save data and compare against `GetFileTimestamp`.
2. **Manual merge** — for games where losing progress matters. Show the player both saves and let them pick.

```csharp
public class VersionedSaveData
{
    public int Version { get; set; } = 1;
    public long TimestampUtc { get; set; }
    public string PlayerName { get; set; }
    public Dictionary<string, int> Progress { get; set; } = new();

    public static VersionedSaveData Merge(VersionedSaveData local, VersionedSaveData cloud)
    {
        // Take the higher progress for each key (max-merge)
        var merged = new VersionedSaveData
        {
            Version = Math.Max(local.Version, cloud.Version),
            TimestampUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            PlayerName = cloud.PlayerName
        };

        var allKeys = local.Progress.Keys.Union(cloud.Progress.Keys);
        foreach (var key in allKeys)
        {
            local.Progress.TryGetValue(key, out int lv);
            cloud.Progress.TryGetValue(key, out int cv);
            merged.Progress[key] = Math.Max(lv, cv);
        }
        return merged;
    }
}
```

### Designing Cloud-Compatible Save Data

Keep cloud saves small, versioned, and forward-compatible:

- **Small:** Target under 1 MB. Steam Cloud quotas are generous but not infinite.
- **Versioned:** Always include a `Version` field. Older clients skip unknown fields; newer clients migrate old formats.
- **Forward-compatible:** Use dictionaries/maps over fixed arrays. Adding a new level doesn't break old saves.
- **Deterministic:** Avoid storing platform-specific paths or locale-dependent data.

---

## Matchmaking

### Steam Lobbies

Steam lobbies handle discovery, filtering, and joining. Create a lobby, set metadata, and other players find it:

```csharp
public class SteamMatchmaking
{
    private CallResult<LobbyCreated_t> _createResult;
    private CallResult<LobbyMatchList_t> _matchListResult;
    private CSteamID _currentLobby;

    public void CreateLobby(ELobbyType type, int maxPlayers)
    {
        var call = Steamworks.SteamMatchmaking.CreateLobby(type, maxPlayers);
        _createResult = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
        _createResult.Set(call);
    }

    private void OnLobbyCreated(LobbyCreated_t result, bool ioFailure)
    {
        if (ioFailure || result.m_eResult != EResult.k_EResultOK) return;

        _currentLobby = new CSteamID(result.m_ulSteamIDLobby);

        // Set lobby metadata for filtering
        Steamworks.SteamMatchmaking.SetLobbyData(_currentLobby, "game", "YourGame");
        Steamworks.SteamMatchmaking.SetLobbyData(_currentLobby, "version", "1.0");
        Steamworks.SteamMatchmaking.SetLobbyData(_currentLobby, "map", "forest");
    }

    public void FindLobbies(string mapFilter = null)
    {
        Steamworks.SteamMatchmaking.AddRequestLobbyListStringFilter(
            "game", "YourGame",
            ELobbyComparison.k_ELobbyComparisonEqual);

        if (mapFilter != null)
        {
            Steamworks.SteamMatchmaking.AddRequestLobbyListStringFilter(
                "map", mapFilter,
                ELobbyComparison.k_ELobbyComparisonEqual);
        }

        Steamworks.SteamMatchmaking.AddRequestLobbyListResultCountFilter(20);

        var call = Steamworks.SteamMatchmaking.RequestLobbyList();
        _matchListResult = CallResult<LobbyMatchList_t>.Create(OnLobbyListReceived);
        _matchListResult.Set(call);
    }

    private void OnLobbyListReceived(LobbyMatchList_t result, bool ioFailure)
    {
        if (ioFailure) return;

        for (int i = 0; i < (int)result.m_nLobbiesMatching; i++)
        {
            var lobbyId = Steamworks.SteamMatchmaking.GetLobbyByIndex(i);
            string map = Steamworks.SteamMatchmaking.GetLobbyData(lobbyId, "map");
            int members = Steamworks.SteamMatchmaking.GetNumLobbyMembers(lobbyId);
            int maxMembers = Steamworks.SteamMatchmaking.GetLobbyMemberLimit(lobbyId);
            // Display in lobby browser UI
        }
    }

    public void JoinLobby(CSteamID lobbyId)
    {
        Steamworks.SteamMatchmaking.JoinLobby(lobbyId);
    }
}
```

### Host Migration

Basic host migration for P2P games:

1. Lobby owner is the host. All members know who it is via `GetLobbyOwner`.
2. If the host disconnects, Steam can auto-assign a new owner.
3. The new owner detects this via `LobbyChatUpdate_t` callback and takes over game state authority.
4. Other clients re-route packets to the new host's SteamID.

For 2D indie games, full host migration is often overkill — just end the session and let players re-lobby. Only invest in migration for longer session games.

### When You Need Matchmaking

- **Use matchmaking** when players need to find strangers (competitive, co-op with randoms).
- **Use direct connect** when players already know each other (invite links, friend lists, LAN).
- **Use both** — lobby for discovery, direct connect via Steam networking relay once matched.

---

## Rich Presence

### Steam Rich Presence

Show what the player is doing in their Steam friends list:

```csharp
public static class SteamPresence
{
    public static void SetStatus(string status)
    {
        SteamFriends.SetRichPresence("status", status);
    }

    public static void SetPlaying(string level, int score)
    {
        SteamFriends.SetRichPresence("status",
            $"Playing {level} — Score: {score}");
    }

    public static void SetInMenu()
    {
        SteamFriends.SetRichPresence("status", "In Main Menu");
    }

    public static void SetInLobby(CSteamID lobbyId)
    {
        SteamFriends.SetRichPresence("status", "Waiting in Lobby");
        SteamFriends.SetRichPresence("connect",
            $"+connect_lobby {lobbyId.m_SteamID}");
    }

    public static void Clear()
    {
        SteamFriends.ClearRichPresence();
    }
}
```

Configure rich presence localization tokens in `steam_appid.txt`'s partner site for multi-language support.

### Discord Rich Presence

Discord Game SDK provides rich presence for the massive Discord userbase. Since Discord deprecated their Game SDK library downloads, the simplest approach is a lightweight IPC implementation or using a community-maintained wrapper:

```csharp
public class DiscordPresence : IDisposable
{
    // Using a lightweight Discord RPC client (e.g., discord-rpc-csharp NuGet)
    private DiscordRpcClient _client;

    public void Initialize(string applicationId)
    {
        _client = new DiscordRpcClient(applicationId);
        _client.Initialize();
    }

    public void SetPresence(string details, string state,
        string largeImageKey = "game_icon")
    {
        _client.SetPresence(new RichPresence
        {
            Details = details,      // Line 1: "Playing Forest Level"
            State = state,          // Line 2: "Score: 1,250"
            Assets = new Assets
            {
                LargeImageKey = largeImageKey,
                LargeImageText = "YourGame"
            },
            Timestamps = Timestamps.Now  // Shows elapsed time
        });
    }

    public void Dispose()
    {
        _client?.Dispose();
    }
}
```

Register your application at `discord.com/developers` and upload art assets there.

### Unified Presence Updates

Wrap both behind a single call so game code doesn't care which platforms are active:

```csharp
public static class PresenceManager
{
    private static DiscordPresence _discord;

    public static void Initialize(string discordAppId)
    {
        _discord = new DiscordPresence();
        _discord.Initialize(discordAppId);
    }

    public static void Update(string activity, string detail = "")
    {
        // Steam
        if (SteamManager.Initialized)
            SteamPresence.SetStatus($"{activity} {detail}".Trim());

        // Discord
        _discord?.SetPresence(activity, detail);
    }
}
```

---

## Analytics & Telemetry

### What to Track

Focus on data that directly improves your game:

| Metric | Why It Matters |
|--------|---------------|
| Session length | Are players engaged or bouncing? |
| Level completion rate | Which levels are walls? |
| Death locations (X, Y, level) | Level design heatmaps |
| Feature usage | Are players finding your mechanics? |
| Settings chosen | What resolution/controls are common? |
| Crash reports | Stack traces for stability |

### Privacy Considerations

- **Disclose collection** in your privacy policy and, where required, in-game.
- **No PII** unless explicitly consented. SteamIDs are pseudonymous but still identifiable.
- **Aggregate, don't surveil.** You need "50% of players die at the spike pit," not "PlayerX died 47 times."
- **Opt-out option.** Respect players who disable telemetry.
- **GDPR/CCPA compliance** if you serve EU/California players.

### Simple Custom Analytics Endpoint

A minimal analytics reporter that batches events and posts to your own endpoint:

```csharp
public class AnalyticsManager
{
    private readonly string _endpoint;
    private readonly string _sessionId;
    private readonly List<AnalyticsEvent> _queue = new();
    private readonly HttpClient _http = new();
    private float _flushTimer;
    private const float FlushIntervalSeconds = 60f;

    public bool Enabled { get; set; } = true;

    public AnalyticsManager(string endpoint)
    {
        _endpoint = endpoint;
        _sessionId = Guid.NewGuid().ToString("N")[..12];
    }

    public void Track(string eventName, Dictionary<string, object> data = null)
    {
        if (!Enabled) return;

        _queue.Add(new AnalyticsEvent
        {
            Event = eventName,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            SessionId = _sessionId,
            Data = data ?? new()
        });
    }

    public void Update(float deltaTime)
    {
        _flushTimer += deltaTime;
        if (_flushTimer >= FlushIntervalSeconds && _queue.Count > 0)
        {
            _flushTimer = 0;
            _ = FlushAsync();
        }
    }

    private async Task FlushAsync()
    {
        var batch = new List<AnalyticsEvent>(_queue);
        _queue.Clear();

        try
        {
            var json = JsonSerializer.Serialize(batch);
            await _http.PostAsync(_endpoint,
                new StringContent(json, Encoding.UTF8, "application/json"));
        }
        catch
        {
            // Re-queue on failure (see Section 9: Offline Fallback)
            _queue.InsertRange(0, batch);
        }
    }
}

public class AnalyticsEvent
{
    public string Event { get; set; }
    public long Timestamp { get; set; }
    public string SessionId { get; set; }
    public Dictionary<string, object> Data { get; set; }
}
```

### Heatmap Data Collection

Track death/event positions for level design iteration:

```csharp
// Track a spatial event
analytics.Track("player_death", new Dictionary<string, object>
{
    ["level"] = "forest_01",
    ["x"] = player.Position.X,
    ["y"] = player.Position.Y,
    ["cause"] = "spikes",
    ["time_in_level"] = levelTimer
});

// On your backend, aggregate into grid cells for heatmap visualization.
// A 16x16 pixel grid cell is good for tile-based 2D games.
```

---

## Player Authentication

### Steam Auth Tickets

Auth tickets let your backend verify that a player is who they claim to be:

```csharp
public class SteamAuth
{
    private HAuthTicket _ticketHandle;
    private byte[] _ticketData;

    /// <summary>
    /// Get an auth ticket for validation. Steamworks SDK 1.55+ (Dec 2023)
    /// requires a SteamNetworkingIdentity parameter.
    /// Pass the remote server/peer identity for targeted tickets,
    /// or use a default identity for Web API validation.
    /// </summary>
    public byte[] GetAuthTicket(CSteamID remoteId = default)
    {
        _ticketData = new byte[1024];

        var identity = new SteamNetworkingIdentity();
        if (remoteId.m_SteamID != 0)
            identity.SetSteamID(remoteId);

        _ticketHandle = SteamUser.GetAuthSessionTicket(
            _ticketData, _ticketData.Length, out uint ticketLength,
            ref identity);
        Array.Resize(ref _ticketData, (int)ticketLength);
        return _ticketData;
    }

    public void CancelTicket()
    {
        if (_ticketHandle != HAuthTicket.Invalid)
            SteamUser.CancelAuthTicket(_ticketHandle);
    }

    // Server-side: call SteamUser.BeginAuthSession with the ticket bytes
    // to verify the player's identity. For dedicated servers, use the
    // Web API: ISteamUserAuth/AuthenticateUserTicket
}
```

### Cross-Platform Identity Linking

If your game ships on Steam and iOS, you need a way to link identities:

```csharp
public class CrossPlatformIdentity
{
    public string PlatformType { get; set; }  // "steam", "gamecenter", "local"
    public string PlatformId { get; set; }     // SteamID, GC player ID, etc.
    public string UnifiedId { get; set; }      // Your backend's player GUID

    public static CrossPlatformIdentity FromSteam()
    {
        return new CrossPlatformIdentity
        {
            PlatformType = "steam",
            PlatformId = SteamUser.GetSteamID().m_SteamID.ToString(),
            UnifiedId = null  // Assigned by your backend on first login
        };
    }
}
```

Your backend maps `(PlatformType, PlatformId)` → `UnifiedId`. Players can link multiple platform accounts to one unified profile. Store this mapping in a simple database table.

---

## Anti-Cheat Basics

### What's Realistic for Indie Games

Full anti-cheat (kernel drivers, memory scanning) is expensive and controversial. For indie 2D games, focus on **server-authoritative design** and **basic validation**.

### Server-Authoritative Design

The golden rule: **the server is the source of truth.** The client proposes actions; the server validates and applies them.

```csharp
public class ScoreValidator
{
    // Validate a score before leaderboard upload
    public static bool IsPlausible(int score, float sessionDuration,
        string level, int difficulty)
    {
        // Max theoretical score per second of play
        const int MaxScorePerSecond = 100;
        int maxPossible = (int)(sessionDuration * MaxScorePerSecond);

        if (score > maxPossible) return false;
        if (score < 0) return false;

        // Level-specific caps
        var levelCaps = new Dictionary<string, int>
        {
            ["tutorial"] = 5_000,
            ["forest"] = 25_000,
            ["boss_rush"] = 100_000
        };

        if (levelCaps.TryGetValue(level, out int cap) && score > cap)
            return false;

        return true;
    }
}
```

### Basic Integrity Checks

Simple measures that raise the bar without being invasive:

1. **Timing checks:** A level that takes minimum 30 seconds to complete shouldn't submit a score after 2 seconds.
2. **Checksum save files:** Hash save data with a secret salt. Detect tampering on load.
3. **Replay verification:** For competitive leaderboards, require replay data. Verify server-side by re-simulating.
4. **Rate limiting:** One score upload per level completion. Flag accounts that upload 100 scores per minute.

```csharp
public static class SaveIntegrity
{
    private static readonly byte[] Salt =
        Encoding.UTF8.GetBytes("your-secret-salt-here");

    public static string ComputeChecksum(byte[] saveData)
    {
        using var hmac = new HMACSHA256(Salt);
        byte[] hash = hmac.ComputeHash(saveData);
        return Convert.ToBase64String(hash);
    }

    public static bool Verify(byte[] saveData, string expectedChecksum)
    {
        return ComputeChecksum(saveData) == expectedChecksum;
    }
}
```

> **Reality check:** Determined cheaters will always find a way. Your goal is to keep casual cheating out of leaderboards, not to build Fort Knox. Spend your time making a great game, not an arms race.

---

## Offline Fallback

### Local-First Architecture

Design every online feature with an offline path. The game should be fully playable without internet:

```csharp
public class OfflineQueue
{
    private readonly string _queuePath;
    private List<QueuedAction> _pending = new();

    public OfflineQueue(string gameName)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            gameName, "offline_queue");
        Directory.CreateDirectory(dir);
        _queuePath = Path.Combine(dir, "pending.json");
        Load();
    }

    public void Enqueue(string action, Dictionary<string, object> payload)
    {
        _pending.Add(new QueuedAction
        {
            Action = action,
            Payload = payload,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });
        Save();
    }

    public async Task FlushAsync(IPlatformServices services)
    {
        if (!services.IsOnline || _pending.Count == 0) return;

        var toProcess = new List<QueuedAction>(_pending);
        _pending.Clear();

        foreach (var item in toProcess)
        {
            bool success = item.Action switch
            {
                "leaderboard_score" => await services.UploadScoreAsync(item.Payload),
                "analytics_event" => await services.TrackEventAsync(item.Payload),
                "cloud_save" => await services.UploadSaveAsync(item.Payload),
                _ => false
            };

            if (!success)
                _pending.Add(item); // Re-queue failures
        }
        Save();
    }

    private void Load()
    {
        if (File.Exists(_queuePath))
            _pending = JsonSerializer.Deserialize<List<QueuedAction>>(
                File.ReadAllText(_queuePath)) ?? new();
    }

    private void Save()
        => File.WriteAllText(_queuePath, JsonSerializer.Serialize(_pending));
}

public class QueuedAction
{
    public string Action { get; set; }
    public Dictionary<string, object> Payload { get; set; }
    public long Timestamp { get; set; }
}
```

### Graceful Degradation Checklist

| Feature | Online Behavior | Offline Behavior |
|---------|----------------|-----------------|
| Leaderboards | Upload + show global/friends | Show local-only board |
| Cloud Saves | Sync to platform cloud | Save locally, sync when reconnected |
| Matchmaking | Lobby browser | Local/split-screen only |
| Rich Presence | Update platform status | Skip silently |
| Analytics | Batch and send | Queue to disk, flush later |
| Auth | Validate via platform | Use cached local identity |

---

## Platform Service Abstraction

### The Interface

Same pattern as [G12 Design Patterns § Service Locator](./G12_design_patterns.md). Define what platform services your game needs, then swap implementations:

```csharp
public interface IPlatformServices
{
    // Lifecycle
    bool IsOnline { get; }
    void Initialize();
    void Shutdown();
    void Update(float deltaTime);

    // Identity
    string PlayerName { get; }
    string PlayerId { get; }

    // Leaderboards
    void SubmitScore(string leaderboard, int score);
    void RequestLeaderboard(string leaderboard, int count,
        Action<List<LeaderboardEntry>> callback);

    // Cloud Saves
    bool CloudSave(string filename, byte[] data);
    byte[] CloudLoad(string filename);
    bool CloudFileExists(string filename);

    // Rich Presence
    void SetPresence(string status);
    void ClearPresence();

    // Analytics
    void TrackEvent(string name, Dictionary<string, object> data = null);

    // Async variants for offline queue
    Task<bool> UploadScoreAsync(Dictionary<string, object> payload);
    Task<bool> TrackEventAsync(Dictionary<string, object> payload);
    Task<bool> UploadSaveAsync(Dictionary<string, object> payload);
}

public class LeaderboardEntry
{
    public int Rank { get; set; }
    public string PlayerName { get; set; }
    public int Score { get; set; }
}
```

### Steam Implementation

```csharp
public class SteamPlatformServices : IPlatformServices
{
    private SteamLeaderboardManager _leaderboards = new();
    private AnalyticsManager _analytics;
    private OfflineQueue _offlineQueue;

    public bool IsOnline => SteamManager.Initialized
        && SteamUtils.GetConnectedUniverse() != EUniverse.k_EUniverseInvalid;

    public string PlayerName => SteamFriends.GetPersonaName();
    public string PlayerId => SteamUser.GetSteamID().m_SteamID.ToString();

    public void Initialize()
    {
        _analytics = new AnalyticsManager("https://your-api.example.com/events");
        _offlineQueue = new OfflineQueue("YourGame");
    }

    public void Shutdown()
    {
        ClearPresence();
    }

    public void Update(float deltaTime)
    {
        SteamAPI.RunCallbacks();
        _analytics?.Update(deltaTime);
        _ = _offlineQueue?.FlushAsync(this);
    }

    public void SubmitScore(string leaderboard, int score)
    {
        if (IsOnline)
        {
            _leaderboards.FindOrCreateLeaderboard(leaderboard);
            _leaderboards.UploadScore(score);
        }
        else
        {
            _offlineQueue.Enqueue("leaderboard_score",
                new() { ["board"] = leaderboard, ["score"] = score });
        }
    }

    public void RequestLeaderboard(string leaderboard, int count,
        Action<List<LeaderboardEntry>> callback)
    {
        _leaderboards.FindOrCreateLeaderboard(leaderboard);
        _leaderboards.DownloadScores(
            ELeaderboardDataRequest.k_ELeaderboardDataRequestGlobal, 1, count);
    }

    public bool CloudSave(string filename, byte[] data)
        => SteamRemoteStorage.FileWrite(filename, data, data.Length);

    public byte[] CloudLoad(string filename)
    {
        int size = SteamRemoteStorage.GetFileSize(filename);
        if (size <= 0) return null;
        byte[] buffer = new byte[size];
        SteamRemoteStorage.FileRead(filename, buffer, size);
        return buffer;
    }

    public bool CloudFileExists(string filename)
        => SteamRemoteStorage.FileExists(filename);

    public void SetPresence(string status)
        => SteamFriends.SetRichPresence("status", status);

    public void ClearPresence()
        => SteamFriends.ClearRichPresence();

    public void TrackEvent(string name, Dictionary<string, object> data = null)
        => _analytics?.Track(name, data);

    public Task<bool> UploadScoreAsync(Dictionary<string, object> p) =>
        Task.FromResult(true); // Would call Steam API
    public Task<bool> TrackEventAsync(Dictionary<string, object> p) =>
        Task.FromResult(true);
    public Task<bool> UploadSaveAsync(Dictionary<string, object> p) =>
        Task.FromResult(true);
}
```

### Null Implementation (itch.io / DRM-Free)

```csharp
public class NullPlatformServices : IPlatformServices
{
    private LocalLeaderboard _localBoard;
    private OfflineQueue _offlineQueue;

    public bool IsOnline => false;
    public string PlayerName => Environment.UserName;
    public string PlayerId => "local";

    public void Initialize()
    {
        _localBoard = new LocalLeaderboard("default");
        _offlineQueue = new OfflineQueue("YourGame");
    }

    public void Shutdown() { }
    public void Update(float deltaTime) { }

    public void SubmitScore(string leaderboard, int score)
        => _localBoard.Submit(PlayerName, score);

    public void RequestLeaderboard(string leaderboard, int count,
        Action<List<LeaderboardEntry>> callback)
    {
        var entries = _localBoard.GetTopScores(count)
            .Select((s, i) => new LeaderboardEntry
            {
                Rank = i + 1,
                PlayerName = s.PlayerName,
                Score = s.Score
            }).ToList();
        callback?.Invoke(entries);
    }

    public bool CloudSave(string filename, byte[] data)
    {
        var path = GetLocalSavePath(filename);
        File.WriteAllBytes(path, data);
        return true;
    }

    public byte[] CloudLoad(string filename)
    {
        var path = GetLocalSavePath(filename);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    public bool CloudFileExists(string filename)
        => File.Exists(GetLocalSavePath(filename));

    public void SetPresence(string status) { }
    public void ClearPresence() { }
    public void TrackEvent(string name, Dictionary<string, object> data) { }

    public Task<bool> UploadScoreAsync(Dictionary<string, object> p) =>
        Task.FromResult(false);
    public Task<bool> TrackEventAsync(Dictionary<string, object> p) =>
        Task.FromResult(false);
    public Task<bool> UploadSaveAsync(Dictionary<string, object> p) =>
        Task.FromResult(false);

    private string GetLocalSavePath(string filename)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "YourGame", "Saves");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, filename);
    }
}
```

### Wiring It Up

```csharp
public static class ServiceLocator
{
    public static IPlatformServices Platform { get; private set; }

    public static void Initialize(bool useSteam)
    {
        Platform = useSteam && SteamManager.Initialized
            ? new SteamPlatformServices()
            : new NullPlatformServices();

        Platform.Initialize();
    }
}

// In your Game class:
protected override void Initialize()
{
    ServiceLocator.Initialize(useSteam: true);
    base.Initialize();
}

protected override void Update(GameTime gameTime)
{
    float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
    ServiceLocator.Platform.Update(dt);
}

// Anywhere in game code:
ServiceLocator.Platform.SubmitScore("high_scores", 12500);
ServiceLocator.Platform.SetPresence("Playing Forest — Wave 12");
ServiceLocator.Platform.TrackEvent("level_complete", new()
{
    ["level"] = "forest_01",
    ["time"] = 142.5f,
    ["deaths"] = 3
});
```

---

## Summary

Online services are the glue between your game and the platforms it runs on. The key principles:

1. **Abstract early.** `IPlatformServices` lets you swap Steam/GameCenter/Null without touching game logic.
2. **Offline first.** Every online feature needs a local fallback. Queue actions for later sync.
3. **Validate everything.** Never trust client-submitted scores, saves, or state.
4. **Respect privacy.** Collect what helps you improve the game, nothing more.
5. **Keep it simple.** Steam lobbies + rich presence + leaderboards cover 90% of what indie games need. Don't build infrastructure you won't use.

Start with the abstraction layer and `NullPlatformServices`. Add Steam when you're ready to ship there. The game code doesn't change — only the implementation behind the interface.
