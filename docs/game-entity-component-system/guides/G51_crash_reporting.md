# G51 — Crash Reporting & Production Error Handling



> **Category:** Guide · **Related:** [G16 Debugging](./G16_debugging.md) · [G32 Deployment & Platform Builds](./G32_deployment_platform_builds.md) · [G36 Publishing & Distribution](./G36_publishing_distribution.md) · [G33 Profiling & Optimization](./G33_profiling_optimization.md)

---

## Why Production Error Handling

Players don't report crashes. They close the game, maybe leave a bad review, and move on. If you're relying on bug reports to find production issues, you're seeing maybe 1% of what's actually happening.

Development debugging (G16) is interactive — you have a debugger attached, you see the console, you can reproduce issues on demand. Production crash handling is the opposite: the game is running on a stranger's machine, you have no access, and the crash already happened. Your only chance is to capture everything you need *before the process dies* and get that data back to yourself.

The goal is simple: when something goes wrong in the wild, you should know about it within minutes — not weeks later from a Steam review saying "game keeps crashing."

---

## Global Exception Handler

The foundation of production crash handling is catching every unhandled exception at the top level. Set this up before anything else runs.

```csharp
public static class CrashHandler
{
    private static bool _initialized;

    public static void Initialize(string gameVersion)
    {
        if (_initialized) return;
        _initialized = true;

        GameVersion = gameVersion;

        // Catch all unhandled exceptions on the main thread
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        // Catch unobserved Task exceptions (async code that nobody awaited)
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // If the process exits unexpectedly, log that too
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    public static string GameVersion { get; private set; } = "0.0.0";
    public static Action? OnCrashDetected { get; set; } // hook for auto-save

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        WriteCrashDump(ex, "UnhandledException", isTerminating: e.IsTerminating);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        // These don't kill the process by default in .NET 8, but you still want to know
        WriteCrashDump(e.Exception, "UnobservedTaskException", isTerminating: false);
        e.SetObserved(); // prevent escalation
    }

    private static void OnProcessExit(object? sender, EventArgs e)
    {
        // Only useful if you want to log clean exits vs crashes
    }

    public static void WriteCrashDump(Exception? ex, string source, bool isTerminating = true)
    {
        try
        {
            // Attempt auto-save before writing the dump
            if (isTerminating)
                OnCrashDetected?.Invoke();

            var dump = CrashDumpBuilder.Build(ex, source, isTerminating);
            CrashLogWriter.Write(dump);

            // Queue for remote upload on next launch
            RemoteCrashReporter.QueueReport(dump);
        }
        catch
        {
            // Last resort — crash handler itself crashed. Write raw to stderr.
            Console.Error.WriteLine($"CRASH HANDLER FAILED. Original: {ex}");
        }
    }
}
```

Wire it up as the very first thing in `Program.cs`:

```csharp
public static class Program
{
    [STAThread]
    static void Main()
    {
        CrashHandler.Initialize(gameVersion: "1.2.3");
        CrashHandler.OnCrashDetected = () => AutoSaveManager.EmergencySave();

        try
        {
            using var game = new MyGame();
            game.Run();
        }
        catch (Exception ex)
        {
            // This catches exceptions that escape the game loop
            CrashHandler.WriteCrashDump(ex, "MainCatch", isTerminating: true);
        }
    }
}
```

---

## Crash Dump Files

A crash dump is useless if it only says "NullReferenceException." You need context. Here's what to capture and how to structure it.

```csharp
public class CrashDump
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Source { get; set; } = "";
    public bool IsTerminating { get; set; }

    // Exception info
    public string ExceptionType { get; set; } = "";
    public string Message { get; set; } = "";
    public string StackTrace { get; set; } = "";
    public string? InnerException { get; set; }

    // Environment
    public string GameVersion { get; set; } = "";
    public string OsVersion { get; set; } = "";
    public string DotNetVersion { get; set; } = "";
    public string GpuInfo { get; set; } = "";
    public long RamMb { get; set; }
    public string ScreenResolution { get; set; } = "";

    // Game state
    public string CurrentScene { get; set; } = "";
    public TimeSpan Uptime { get; set; }
    public List<string> RecentLogEntries { get; set; } = new();
}

public static class CrashDumpBuilder
{
    private static readonly DateTime ProcessStart = DateTime.UtcNow;

    public static CrashDump Build(Exception? ex, string source, bool isTerminating)
    {
        var dump = new CrashDump
        {
            Source = source,
            IsTerminating = isTerminating,
            GameVersion = CrashHandler.GameVersion,
            OsVersion = Environment.OSVersion.ToString(),
            DotNetVersion = Environment.Version.ToString(),
            RamMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024),
            Uptime = DateTime.UtcNow - ProcessStart,
            CurrentScene = SceneManager.CurrentSceneName ?? "unknown",
            RecentLogEntries = RingBufferLog.GetRecent(50) // last 50 log lines
        };

        if (ex != null)
        {
            dump.ExceptionType = ex.GetType().FullName ?? ex.GetType().Name;
            dump.Message = ex.Message;
            dump.StackTrace = ex.StackTrace ?? "(no stack trace)";
            dump.InnerException = ex.InnerException?.ToString();
        }

        // GPU info — safe to fail
        try
        {
            dump.GpuInfo = GraphicsAdapter.DefaultAdapter?.Description ?? "unknown";
            var vp = GraphicsAdapter.DefaultAdapter?.CurrentDisplayMode;
            dump.ScreenResolution = vp != null ? $"{vp.Width}x{vp.Height}" : "unknown";
        }
        catch { dump.GpuInfo = "unavailable"; }

        return dump;
    }
}
```

The ring buffer log referenced here is a simple circular buffer that keeps the last N log entries in memory — see G16 for the full implementation. A minimal version:

```csharp
public static class RingBufferLog
{
    private static readonly string[] Buffer = new string[200];
    private static int _index;
    private static int _count;
    private static readonly object Lock = new();

    public static void Add(string entry)
    {
        lock (Lock)
        {
            Buffer[_index % Buffer.Length] = $"[{DateTime.UtcNow:HH:mm:ss.fff}] {entry}";
            _index++;
            _count = Math.Min(_count + 1, Buffer.Length);
        }
    }

    public static List<string> GetRecent(int n)
    {
        lock (Lock)
        {
            var result = new List<string>(Math.Min(n, _count));
            int start = (_index - Math.Min(n, _count) + Buffer.Length) % Buffer.Length;
            for (int i = 0; i < Math.Min(n, _count); i++)
                result.Add(Buffer[(start + i) % Buffer.Length]);
            return result;
        }
    }
}
```

---

## Local Crash Log System

Crash dumps need to go to disk immediately — the process may be dying. Write them as plain text files with a structured format.

```csharp
public static class CrashLogWriter
{
    private const int MaxCrashFiles = 20;

    public static string CrashLogDir
    {
        get
        {
            string baseDir;
            if (OperatingSystem.IsWindows())
                baseDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MyGame", "CrashLogs");
            else if (OperatingSystem.IsMacOS())
                baseDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library", "Logs", "MyGame", "CrashLogs");
            else // Linux
                baseDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".local", "share", "MyGame", "CrashLogs");

            Directory.CreateDirectory(baseDir);
            return baseDir;
        }
    }

    public static void Write(CrashDump dump)
    {
        var filename = $"crash_{dump.Timestamp:yyyyMMdd_HHmmss}_{dump.Id}.log";
        var path = Path.Combine(CrashLogDir, filename);

        var sb = new StringBuilder();
        sb.AppendLine("=== CRASH REPORT ===");
        sb.AppendLine($"ID:            {dump.Id}");
        sb.AppendLine($"Timestamp:     {dump.Timestamp:O}");
        sb.AppendLine($"Source:        {dump.Source}");
        sb.AppendLine($"Terminating:   {dump.IsTerminating}");
        sb.AppendLine();
        sb.AppendLine("--- Exception ---");
        sb.AppendLine($"Type:          {dump.ExceptionType}");
        sb.AppendLine($"Message:       {dump.Message}");
        sb.AppendLine($"Stack Trace:");
        sb.AppendLine(dump.StackTrace);
        if (dump.InnerException != null)
        {
            sb.AppendLine($"Inner:         {dump.InnerException}");
        }
        sb.AppendLine();
        sb.AppendLine("--- Environment ---");
        sb.AppendLine($"Game Version:  {dump.GameVersion}");
        sb.AppendLine($"OS:            {dump.OsVersion}");
        sb.AppendLine($".NET:          {dump.DotNetVersion}");
        sb.AppendLine($"GPU:           {dump.GpuInfo}");
        sb.AppendLine($"RAM (MB):      {dump.RamMb}");
        sb.AppendLine($"Resolution:    {dump.ScreenResolution}");
        sb.AppendLine($"Scene:         {dump.CurrentScene}");
        sb.AppendLine($"Uptime:        {dump.Uptime}");
        sb.AppendLine();
        sb.AppendLine("--- Recent Log ---");
        foreach (var entry in dump.RecentLogEntries)
            sb.AppendLine(entry);
        sb.AppendLine();
        sb.AppendLine("=== END REPORT ===");

        File.WriteAllText(path, sb.ToString());
        RotateOldLogs();
    }

    private static void RotateOldLogs()
    {
        try
        {
            var files = Directory.GetFiles(CrashLogDir, "crash_*.log")
                .OrderByDescending(f => f)
                .Skip(MaxCrashFiles)
                .ToList();
            foreach (var old in files)
                File.Delete(old);
        }
        catch { /* rotation failure is non-critical */ }
    }
}
```

---

## Remote Crash Reporting

Local logs are great, but you still need to get data from the player's machine to yours. A simple HTTP POST approach that respects privacy:

```csharp
public static class RemoteCrashReporter
{
    private const string Endpoint = "https://your-game-api.com/api/crashes";
    private static readonly string QueueDir = Path.Combine(CrashLogWriter.CrashLogDir, "queue");

    /// <summary>Queue a report for upload on next launch (process may be dying right now).</summary>
    public static void QueueReport(CrashDump dump)
    {
        try
        {
            Directory.CreateDirectory(QueueDir);
            var json = JsonSerializer.Serialize(dump);
            var path = Path.Combine(QueueDir, $"{dump.Id}.json");
            File.WriteAllText(path, json);
        }
        catch { /* don't crash the crash handler */ }
    }

    /// <summary>Call this early in startup to send any queued reports from last session.</summary>
    public static async Task FlushQueueAsync(bool userConsented)
    {
        if (!userConsented || !Directory.Exists(QueueDir)) return;

        var files = Directory.GetFiles(QueueDir, "*.json");
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        foreach (var file in files)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await http.PostAsync(Endpoint, content);

                if (response.IsSuccessStatusCode)
                    File.Delete(file); // sent successfully, remove from queue
            }
            catch
            {
                // network issue — leave in queue for next launch
            }
        }
    }
}
```

**Privacy consent** — Always ask the player before sending data. Store their preference and respect it:

```csharp
// In your settings/options screen:
public bool CrashReportingOptIn
{
    get => Preferences.GetBool("crash_reporting_enabled", false);
    set => Preferences.SetBool("crash_reporting_enabled", value);
}

// At startup:
await RemoteCrashReporter.FlushQueueAsync(userConsented: settings.CrashReportingOptIn);
```

---

## Sentry Integration

For a more robust solution, [Sentry](https://sentry.io) provides real-time crash reporting with grouping, stack trace symbolication, breadcrumbs, and release tracking. The free tier covers most indie needs (5K errors/month).

```xml
<!-- Add to your .csproj -->
<PackageReference Include="Sentry" Version="4.*" />
```

```csharp
public static class SentryIntegration
{
    private static IDisposable? _sentry;

    public static void Initialize(string dsn, string gameVersion)
    {
        _sentry = SentrySdk.Init(options =>
        {
            options.Dsn = dsn;
            options.Release = $"mygame@{gameVersion}";
            options.Environment = IsDebugBuild() ? "development" : "production";
            options.AutoSessionTracking = true;
            options.IsGlobalModeEnabled = true;

            // Sample rate — 1.0 = send everything, 0.1 = send 10%
            options.SampleRate = 1.0f;

            // Attach breadcrumbs for context
            options.MaxBreadcrumbs = 50;

            // Filter out sensitive data
            options.SetBeforeSend((sentryEvent, hint) =>
            {
                // Strip player file paths from stack traces
                sentryEvent.ServerName = null;
                return sentryEvent;
            });
        });
    }

    /// <summary>Add breadcrumbs throughout your game to trace what happened before a crash.</summary>
    public static void AddBreadcrumb(string message, string category = "game")
    {
        SentrySdk.AddBreadcrumb(message, category);
    }

    /// <summary>Set user context (use anonymous ID, not real names).</summary>
    public static void SetPlayer(string anonymousId)
    {
        SentrySdk.ConfigureScope(scope =>
        {
            scope.User = new SentryUser { Id = anonymousId };
        });
    }

    /// <summary>Capture a non-fatal exception you caught and handled.</summary>
    public static void CaptureHandled(Exception ex, string context)
    {
        SentrySdk.ConfigureScope(scope => scope.SetTag("context", context));
        SentrySdk.CaptureException(ex);
    }

    public static void Shutdown() => _sentry?.Dispose();
    private static bool IsDebugBuild() =>
#if DEBUG
        true;
#else
        false;
#endif
}
```

Sprinkle breadcrumbs through your game so crashes have context:

```csharp
SentryIntegration.AddBreadcrumb("Entered level 3", "navigation");
SentryIntegration.AddBreadcrumb("Player picked up item: sword_01", "gameplay");
SentryIntegration.AddBreadcrumb("Boss fight started: dragon_boss", "gameplay");
// If a crash happens now, Sentry shows these breadcrumbs leading up to it
```

---

## Graceful Degradation in Production

Not every exception should kill the game. Wrap system boundaries in try/catch and disable broken systems instead of crashing.

```csharp
public enum ErrorSeverity { Critical, NonCritical }

public static class GracefulHandler
{
    private static readonly HashSet<string> DisabledSystems = new();

    /// <summary>Run a game system with crash protection. Non-critical failures disable the system.</summary>
    public static void RunProtected(string systemName, Action action,
        ErrorSeverity severity = ErrorSeverity.NonCritical)
    {
        if (DisabledSystems.Contains(systemName)) return;

        try
        {
            action();
        }
        catch (Exception ex)
        {
            RingBufferLog.Add($"[ERROR] {systemName} failed: {ex.Message}");
            SentryIntegration.CaptureHandled(ex, systemName);

            if (severity == ErrorSeverity.Critical)
            {
                // Critical systems (rendering, core game loop) — can't continue
                CrashHandler.WriteCrashDump(ex, $"Critical:{systemName}", isTerminating: true);
                throw; // let it propagate and kill the process
            }
            else
            {
                // Non-critical (particles, achievements, analytics) — disable and keep going
                DisabledSystems.Add(systemName);
                RingBufferLog.Add($"[WARN] Disabled system: {systemName}");
            }
        }
    }

    public static bool IsDisabled(string systemName) => DisabledSystems.Contains(systemName);
}

// Usage in your game loop:
protected override void Update(GameTime gameTime)
{
    // Critical — if these fail, the game can't function
    GracefulHandler.RunProtected("Physics", () => PhysicsSystem.Update(gameTime),
        ErrorSeverity.Critical);
    GracefulHandler.RunProtected("Input", () => InputSystem.Update(gameTime),
        ErrorSeverity.Critical);

    // Non-critical — game can survive without these
    GracefulHandler.RunProtected("Particles", () => ParticleSystem.Update(gameTime));
    GracefulHandler.RunProtected("Audio", () => AudioSystem.Update(gameTime));
    GracefulHandler.RunProtected("Achievements", () => AchievementSystem.Update(gameTime));
}
```

The philosophy: **the show must go on.** A broken particle system shouldn't prevent someone from finishing a boss fight. Log it, report it, disable it, keep playing.

---

## Error Recovery Strategies

```csharp
public static class RecoveryManager
{
    /// <summary>Attempt emergency auto-save before crash.</summary>
    public static void EmergencySave()
    {
        try
        {
            var savePath = Path.Combine(SaveManager.SaveDir, "emergency_autosave.sav");
            SaveManager.WriteSave(savePath, SaveManager.CurrentState);
            RingBufferLog.Add("[RECOVERY] Emergency save written");
        }
        catch (Exception ex)
        {
            RingBufferLog.Add($"[RECOVERY] Emergency save failed: {ex.Message}");
        }
    }

    /// <summary>Try to load a save, falling back to backups if corrupted.</summary>
    public static SaveData? LoadWithRecovery(string savePath)
    {
        // Try primary save
        var data = TryLoadSave(savePath);
        if (data != null) return data;

        // Try backup saves (keep last 3 backups)
        for (int i = 1; i <= 3; i++)
        {
            var backup = $"{savePath}.bak{i}";
            data = TryLoadSave(backup);
            if (data != null)
            {
                RingBufferLog.Add($"[RECOVERY] Loaded backup save #{i}");
                return data;
            }
        }

        // Try emergency autosave
        var emergency = Path.Combine(SaveManager.SaveDir, "emergency_autosave.sav");
        data = TryLoadSave(emergency);
        if (data != null)
        {
            RingBufferLog.Add("[RECOVERY] Loaded emergency autosave");
            return data;
        }

        RingBufferLog.Add("[RECOVERY] All saves corrupted — starting fresh");
        return null; // caller should start a new game
    }

    private static SaveData? TryLoadSave(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<SaveData>(json);
        }
        catch { return null; }
    }

    /// <summary>Load a texture with fallback to a placeholder if the asset is missing/corrupt.</summary>
    public static Texture2D LoadTextureOrFallback(ContentManager content,
        string assetName, Texture2D fallback)
    {
        try
        {
            return content.Load<Texture2D>(assetName);
        }
        catch (Exception ex)
        {
            RingBufferLog.Add($"[RECOVERY] Missing asset '{assetName}': {ex.Message}");
            return fallback; // pink checkerboard or placeholder texture
        }
    }

    /// <summary>Detect if last session crashed and offer safe mode.</summary>
    public static bool DidLastSessionCrash()
    {
        var marker = Path.Combine(CrashLogWriter.CrashLogDir, ".running");
        bool crashed = File.Exists(marker);
        File.WriteAllText(marker, DateTime.UtcNow.ToString("O")); // set marker
        return crashed;
    }

    public static void MarkCleanExit()
    {
        var marker = Path.Combine(CrashLogWriter.CrashLogDir, ".running");
        if (File.Exists(marker)) File.Delete(marker);
    }
}
```

Safe mode startup pattern:

```csharp
if (RecoveryManager.DidLastSessionCrash())
{
    // Offer reduced settings — lower resolution, no mods, skip intro
    Settings.ApplySafeMode();
    ShowCrashRecoveryScreen();
}
```

---

## Platform-Specific Considerations

Each platform has its own crash reporting ecosystem. Layer yours on top.

**Windows** — Windows Error Reporting (WER) collects minidumps automatically. Players can find them in `%LOCALAPPDATA%\CrashDumps`. For your own minidumps, P/Invoke `MiniDumpWriteDump` — but for most indie games, text crash logs with stack traces are sufficient.

**macOS** — Crashes generate `.crash` files in `~/Library/Logs/DiagnosticReports/`. The system crash reporter dialog appears automatically. Your crash log directory at `~/Library/Logs/MyGame/CrashLogs/` keeps your custom reports alongside the system ones.

**Linux** — Core dumps are controlled by `ulimit -c` and `/proc/sys/kernel/core_pattern`. Most distros disable them by default. Your text-based crash logs are the primary source of information here.

**iOS (via MonoGame)** — TestFlight provides crash reports with symbolication. Upload dSYM files to see readable stack traces. Xcode Organizer shows crash logs from TestFlight and App Store users. Sentry also supports iOS symbolication.

```csharp
// Platform-aware crash log directory (already handled in CrashLogWriter above)
// Additional platform hooks:
public static class PlatformCrashHelpers
{
    public static void OpenCrashLogFolder()
    {
        var dir = CrashLogWriter.CrashLogDir;
        if (OperatingSystem.IsWindows())
            Process.Start("explorer.exe", dir);
        else if (OperatingSystem.IsMacOS())
            Process.Start("open", dir);
        else if (OperatingSystem.IsLinux())
            Process.Start("xdg-open", dir);
    }
}
```

---

## Post-Launch Monitoring

Crash reports are only useful if you actually look at them. Set up a workflow:

1. **Track crash rate per version** — `(crashes / sessions) * 100`. A healthy game is under 1%. Over 5% is a fire.
2. **Group by exception type** — Sentry does this automatically. If you're self-hosting, group by `ExceptionType + first line of StackTrace`.
3. **Prioritize by frequency** — Fix the crash 200 players hit before the one that happened once.
4. **Version rollback** — If a release doubles the crash rate, push a hotfix or rollback. Have the pipeline ready *before* you need it.

```csharp
// Lightweight session/crash counter for self-hosted analytics
public static class StabilityTracker
{
    private static readonly string StatsFile = Path.Combine(
        CrashLogWriter.CrashLogDir, "stability.json");

    public static void RecordSession()
    {
        var stats = LoadStats();
        stats.TotalSessions++;
        stats.SessionsByVersion[CrashHandler.GameVersion] =
            stats.SessionsByVersion.GetValueOrDefault(CrashHandler.GameVersion) + 1;
        SaveStats(stats);
    }

    public static void RecordCrash()
    {
        var stats = LoadStats();
        stats.TotalCrashes++;
        stats.CrashesByVersion[CrashHandler.GameVersion] =
            stats.CrashesByVersion.GetValueOrDefault(CrashHandler.GameVersion) + 1;
        SaveStats(stats);
    }

    private static StabilityStats LoadStats()
    {
        try
        {
            if (File.Exists(StatsFile))
                return JsonSerializer.Deserialize<StabilityStats>(
                    File.ReadAllText(StatsFile)) ?? new();
        }
        catch { }
        return new StabilityStats();
    }

    private static void SaveStats(StabilityStats stats) =>
        File.WriteAllText(StatsFile, JsonSerializer.Serialize(stats,
            new JsonSerializerOptions { WriteIndented = true }));
}

public class StabilityStats
{
    public int TotalSessions { get; set; }
    public int TotalCrashes { get; set; }
    public Dictionary<string, int> SessionsByVersion { get; set; } = new();
    public Dictionary<string, int> CrashesByVersion { get; set; } = new();
    public double CrashRate => TotalSessions > 0
        ? Math.Round((double)TotalCrashes / TotalSessions * 100, 2) : 0;
}
```

---

## Player-Facing Error Messages

Never show a raw stack trace to a player. Show a friendly message with an option to help.

```csharp
public class CrashScreen
{
    private SpriteFont _font;
    private string _crashId;
    private bool _reportSent;

    public CrashScreen(SpriteFont font, string crashId)
    {
        _font = font;
        _crashId = crashId;
    }

    public void Draw(SpriteBatch spriteBatch, int screenWidth, int screenHeight)
    {
        spriteBatch.Begin();

        var lines = new[]
        {
            "Something went wrong :(",
            "",
            "The game encountered an unexpected error.",
            "Your progress has been auto-saved.",
            "",
            $"Error ID: {_crashId}",
            "",
            _reportSent
                ? "Crash report sent — thank you!"
                : "Press [S] to send a crash report",
            "Press [R] to restart the game",
            "Press [Q] to quit",
            "",
            "If this keeps happening, try:",
            "  - Verifying game files on Steam",
            "  - Updating your graphics drivers",
            "  - Starting in safe mode (hold Shift at launch)"
        };

        float y = screenHeight / 2f - (lines.Length * 24f) / 2f;
        foreach (var line in lines)
        {
            var size = _font.MeasureString(line);
            var x = (screenWidth - size.X) / 2f;
            spriteBatch.DrawString(_font, line, new Vector2(x, y), Color.White);
            y += 24f;
        }

        spriteBatch.End();
    }
}
```

For the crash recovery screen on next launch:

```csharp
// Show this if RecoveryManager.DidLastSessionCrash() returns true
public class CrashRecoveryScreen
{
    public string Message =>
        "The game didn't shut down properly last time.\n\n" +
        "Would you like to:\n" +
        "  [C] Continue from auto-save\n" +
        "  [S] Start in safe mode (lower settings)\n" +
        "  [N] Start a new game\n";
}
```

---

## Testing Crash Handling

Your crash reporting pipeline is code — it needs testing like everything else. Don't wait for a real crash to find out your handler doesn't work.

```csharp
#if DEBUG
public static class CrashTesting
{
    /// <summary>Debug menu options for testing crash infrastructure.</summary>
    public static void RegisterDebugCommands(DebugConsole console)
    {
        console.Register("crash_null", "Trigger NullReferenceException", () =>
        {
            string? s = null;
            _ = s!.Length; // boom
        });

        console.Register("crash_stackoverflow", "Trigger StackOverflowException", () =>
        {
            static void Recurse() => Recurse();
            Recurse();
        });

        console.Register("crash_oom", "Trigger OutOfMemoryException", () =>
        {
            var lists = new List<byte[]>();
            while (true) lists.Add(new byte[1024 * 1024 * 100]);
        });

        console.Register("crash_task", "Trigger unobserved Task exception", () =>
        {
            _ = Task.Run(() => throw new InvalidOperationException("Test task crash"));
            GC.Collect(); // force finalization to trigger UnobservedTaskException
            GC.WaitForPendingFinalizers();
        });

        console.Register("crash_handled", "Test non-fatal error handling", () =>
        {
            GracefulHandler.RunProtected("TestSystem", () =>
                throw new Exception("Test non-critical failure"));
        });

        console.Register("crash_verify", "Verify crash log was written", () =>
        {
            var files = Directory.GetFiles(CrashLogWriter.CrashLogDir, "crash_*.log");
            var latest = files.OrderByDescending(f => f).FirstOrDefault();
            if (latest != null)
            {
                Console.WriteLine($"Latest crash log: {latest}");
                Console.WriteLine($"Size: {new FileInfo(latest).Length} bytes");
                Console.WriteLine(File.ReadLines(latest).Take(10)
                    .Aggregate((a, b) => $"{a}\n{b}"));
            }
            else
            {
                Console.WriteLine("No crash logs found.");
            }
        });

        console.Register("crash_open_dir", "Open crash log folder", () =>
        {
            PlatformCrashHelpers.OpenCrashLogFolder();
        });
    }

    /// <summary>Automated validation — call in test suite.</summary>
    public static bool ValidateCrashPipeline()
    {
        // 1. Trigger a non-fatal crash
        try { throw new Exception("Pipeline validation test"); }
        catch (Exception ex)
        {
            CrashHandler.WriteCrashDump(ex, "ValidationTest", isTerminating: false);
        }

        // 2. Verify log was written
        var files = Directory.GetFiles(CrashLogWriter.CrashLogDir, "crash_*.log");
        if (files.Length == 0) return false;

        // 3. Verify content
        var latest = files.OrderByDescending(f => f).First();
        var content = File.ReadAllText(latest);
        bool valid = content.Contains("Pipeline validation test")
                  && content.Contains("ValidationTest")
                  && content.Contains("Game Version:");

        // 4. Verify queue file was created for remote upload
        var queueDir = Path.Combine(CrashLogWriter.CrashLogDir, "queue");
        bool queued = Directory.Exists(queueDir)
                   && Directory.GetFiles(queueDir, "*.json").Length > 0;

        Console.WriteLine($"Crash log valid: {valid}, Queued for upload: {queued}");
        return valid;
    }
}
#endif
```

Run `crash_verify` after any test crash to confirm the full pipeline — exception → handler → dump file → queue for upload — actually works. Do this before every release. Better yet, add `ValidateCrashPipeline()` to your CI test suite so it runs automatically.

---

## Putting It All Together

Startup sequence with everything wired up:

```csharp
static void Main()
{
    // 1. Crash handler first — before anything can fail
    CrashHandler.Initialize(gameVersion: "1.2.3");
    CrashHandler.OnCrashDetected = () => RecoveryManager.EmergencySave();

    // 2. Sentry (if opted in)
    SentryIntegration.Initialize("https://your-dsn@sentry.io/12345", "1.2.3");
    SentryIntegration.SetPlayer(PlayerIdManager.GetAnonymousId());

    // 3. Track session
    StabilityTracker.RecordSession();

    // 4. Flush any queued crash reports from last session
    Task.Run(() => RemoteCrashReporter.FlushQueueAsync(userConsented: true));

    // 5. Check if last session crashed
    bool crashed = RecoveryManager.DidLastSessionCrash();

    try
    {
        using var game = new MyGame(startInSafeMode: crashed);
        game.Run();
        RecoveryManager.MarkCleanExit(); // only reached on clean shutdown
    }
    catch (Exception ex)
    {
        StabilityTracker.RecordCrash();
        CrashHandler.WriteCrashDump(ex, "MainCatch", isTerminating: true);
    }
    finally
    {
        SentryIntegration.Shutdown();
    }
}
```

Players will never tell you your game crashed. Build the system that tells you instead.
