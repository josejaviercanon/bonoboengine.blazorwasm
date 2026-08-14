# G33 — Profiling & Optimization Workflow


> **Category:** Guide · **Related:** [G13 C# Performance](./G13_csharp_performance.md) · [G15 Game Loop](./G15_game_loop.md) · [G16 Debugging](./G16_debugging.md) · [G2 Rendering & Graphics](./G2_rendering_and_graphics.md)

> Systematic approach to finding and fixing performance problems in MonoGame + Arch ECS games. Measure first, optimize second, verify always.

---

## 1. Frame Budget Fundamentals

### 1.1 The 16.67ms Budget

At 60fps, every frame must complete in **16.67ms** (1000ms ÷ 60). At 30fps, you have 33.33ms. At 120fps (ProMotion), just 8.33ms.

The frame budget splits between CPU and GPU work:

```
┌──────────────── 16.67ms Frame Budget ────────────────┐
│                                                       │
│  CPU                          GPU                     │
│  ┌─────────────────────┐     ┌─────────────────────┐ │
│  │ Input processing     │     │ Vertex processing    │ │
│  │ ECS system updates   │     │ Pixel shading        │ │
│  │ Physics / collision  │     │ Texture sampling     │ │
│  │ SpriteBatch sorting  │     │ Blending / output    │ │
│  │ Draw call submission │     │                      │ │
│  └─────────────────────┘     └─────────────────────┘ │
│                                                       │
│  CPU and GPU work in parallel (pipelined).            │
│  The SLOWER one determines your frame rate.           │
└───────────────────────────────────────────────────────┘
```

### 1.2 CPU-Bound vs GPU-Bound Identification

The single most important profiling question: **which side is the bottleneck?**

```csharp
public class BoundDetector
{
    private readonly Stopwatch _cpuTimer = new();
    private readonly Stopwatch _gpuTimer = new();
    private double _lastCpuMs;
    private double _lastGpuMs;

    /// <summary>Call at the start of Update()</summary>
    public void BeginCpuWork() => _cpuTimer.Restart();

    /// <summary>Call at the end of Update(), before Draw()</summary>
    public void EndCpuWork()
    {
        _cpuTimer.Stop();
        _lastCpuMs = _cpuTimer.Elapsed.TotalMilliseconds;
    }

    /// <summary>Call at the start of Draw()</summary>
    public void BeginGpuWork() => _gpuTimer.Restart();

    /// <summary>Call at the end of Draw()</summary>
    public void EndGpuWork()
    {
        _gpuTimer.Stop();
        _lastGpuMs = _gpuTimer.Elapsed.TotalMilliseconds;
    }

    public bool IsCpuBound => _lastCpuMs > _lastGpuMs;
    public bool IsGpuBound => _lastGpuMs > _lastCpuMs;

    public string Summary =>
        $"CPU: {_lastCpuMs:F2}ms | GPU: {_lastGpuMs:F2}ms | " +
        $"Bound: {(IsCpuBound ? "CPU" : "GPU")} | " +
        $"Budget: {(_lastCpuMs + _lastGpuMs):F2}/16.67ms";
}
```

> **Note:** `_lastGpuMs` above measures CPU-side draw call submission time, not actual GPU execution. True GPU profiling requires platform-specific tools (Xcode GPU profiler, RenderDoc, PIX). However, for 2D MonoGame games, CPU-side draw submission time correlates strongly with GPU load — more draw calls and state changes means more work on both sides.

**Quick diagnostic rules:**

| Observation | Likely Bound | Action |
|---|---|---|
| Reducing entity count improves fps | CPU | Optimize systems, queries, algorithms |
| Reducing resolution improves fps | GPU (fill-rate) | Lower internal res, reduce overdraw |
| Reducing draw calls improves fps | GPU (state changes) | Better batching, texture atlases |
| Reducing post-processing improves fps | GPU (shader) | Simplify shaders, fewer passes |
| `IsRunningSlowly` is true frequently | CPU | Update() exceeds budget → [G15](./G15_game_loop.md) |

### 1.3 Frame Time vs FPS

Always measure **frame time in milliseconds**, not FPS. FPS is a reciprocal that hides the magnitude of spikes:

| Frame Time | FPS | Perceived |
|---|---|---|
| 16.67ms | 60 | Smooth |
| 20.0ms | 50 | Slight stutter |
| 33.33ms | 30 | Noticeable lag |
| 50.0ms | 20 | Unplayable |
| 100ms → 16.67ms | 10 → 60 | Single bad frame = visible hitch |

A single 50ms frame in a stream of 16ms frames is **imperceptible in FPS averages** but **clearly visible to the player**. Track p99 (99th percentile) frame times, not averages.

---

## 2. Built-in Profiling

### 2.1 Stopwatch-Based System Timing

Wrap every ECS system with `Stopwatch` to track per-system cost:

```csharp
public class SystemTimingData
{
    public string Name { get; init; } = "";
    public double LastMs { get; set; }
    public double AvgMs { get; set; }
    public double MaxMs { get; set; }
    public double MinMs { get; set; } = double.MaxValue;

    private readonly double[] _history = new double[120]; // 2 seconds at 60fps
    private int _historyIndex;
    private int _sampleCount;

    public void Record(double ms)
    {
        LastMs = ms;
        MaxMs = Math.Max(MaxMs, ms);
        MinMs = Math.Min(MinMs, ms);

        _history[_historyIndex] = ms;
        _historyIndex = (_historyIndex + 1) % _history.Length;
        _sampleCount = Math.Min(_sampleCount + 1, _history.Length);

        double sum = 0;
        for (int i = 0; i < _sampleCount; i++) sum += _history[i];
        AvgMs = sum / _sampleCount;
    }

    public ReadOnlySpan<double> History => _history.AsSpan(0, _sampleCount);

    public void Reset()
    {
        LastMs = AvgMs = MaxMs = 0;
        MinMs = double.MaxValue;
        _historyIndex = _sampleCount = 0;
        Array.Clear(_history);
    }
}
```

### 2.2 Timed System Runner

Integrate timing into the system execution pipeline:

```csharp
public class TimedSystemRunner
{
    private readonly List<(string Name, Action<float> Execute)> _systems = new();
    private readonly Dictionary<string, SystemTimingData> _timings = new();
    private readonly Stopwatch _sw = new();
    private double _totalFrameMs;

    public IReadOnlyDictionary<string, SystemTimingData> Timings => _timings;
    public double TotalFrameMs => _totalFrameMs;

    public void Register(string name, Action<float> execute)
    {
        _systems.Add((name, execute));
        _timings[name] = new SystemTimingData { Name = name };
    }

    public void RunAll(float dt)
    {
        _totalFrameMs = 0;

        foreach (var (name, execute) in _systems)
        {
            _sw.Restart();
            execute(dt);
            _sw.Stop();

            double ms = _sw.Elapsed.TotalMilliseconds;
            _timings[name].Record(ms);
            _totalFrameMs += ms;
        }
    }
}
```

**Usage with Arch ECS systems:**

```csharp
// In your game initialization
var runner = new TimedSystemRunner();

runner.Register("Physics",    dt => _physicsSystem.Update(dt));
runner.Register("AI",         dt => _aiSystem.Update(dt));
runner.Register("Animation",  dt => _animationSystem.Update(dt));
runner.Register("Collision",  dt => _collisionSystem.Update(dt));
runner.Register("Rendering",  dt => _renderSystem.Update(dt));

// In Update()
runner.RunAll(fixedDt);
```

### 2.3 ImGui System Timing Overlay

Display per-system costs in a real-time debug panel:

```csharp
public static class ProfilerOverlay
{
    private static bool _showProfiler = true;

    public static void Draw(TimedSystemRunner runner)
    {
#if DEBUG
        if (!_showProfiler) return;

        ImGui.SetNextWindowSize(new System.Numerics.Vector2(350, 0));
        ImGui.Begin("System Profiler", ref _showProfiler,
            ImGuiWindowFlags.NoResize);

        // Frame budget bar
        float budgetUsed = (float)(runner.TotalFrameMs / 16.67);
        var budgetColor = budgetUsed < 0.7f
            ? new System.Numerics.Vector4(0.2f, 0.8f, 0.2f, 1f)  // green
            : budgetUsed < 0.9f
                ? new System.Numerics.Vector4(1f, 0.8f, 0.2f, 1f)  // yellow
                : new System.Numerics.Vector4(1f, 0.2f, 0.2f, 1f); // red

        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, budgetColor);
        ImGui.ProgressBar(Math.Min(budgetUsed, 1f),
            new System.Numerics.Vector2(-1, 20),
            $"Frame: {runner.TotalFrameMs:F2}ms / 16.67ms ({budgetUsed * 100:F0}%)");
        ImGui.PopStyleColor();
        ImGui.Separator();

        // Per-system breakdown
        ImGui.Columns(4, "systems");
        ImGui.SetColumnWidth(0, 120);
        ImGui.SetColumnWidth(1, 70);
        ImGui.SetColumnWidth(2, 70);
        ImGui.SetColumnWidth(3, 70);

        ImGui.Text("System"); ImGui.NextColumn();
        ImGui.Text("Last"); ImGui.NextColumn();
        ImGui.Text("Avg"); ImGui.NextColumn();
        ImGui.Text("Max"); ImGui.NextColumn();
        ImGui.Separator();

        foreach (var (name, data) in runner.Timings)
        {
            // Color code: red if >2ms, yellow if >1ms
            if (data.AvgMs > 2.0)
                ImGui.PushStyleColor(ImGuiCol.Text,
                    new System.Numerics.Vector4(1, 0.3f, 0.3f, 1));
            else if (data.AvgMs > 1.0)
                ImGui.PushStyleColor(ImGuiCol.Text,
                    new System.Numerics.Vector4(1, 0.8f, 0.3f, 1));
            else
                ImGui.PushStyleColor(ImGuiCol.Text,
                    new System.Numerics.Vector4(0.8f, 0.8f, 0.8f, 1));

            ImGui.Text(name); ImGui.NextColumn();
            ImGui.Text($"{data.LastMs:F2}"); ImGui.NextColumn();
            ImGui.Text($"{data.AvgMs:F2}"); ImGui.NextColumn();
            ImGui.Text($"{data.MaxMs:F2}"); ImGui.NextColumn();
            ImGui.PopStyleColor();
        }

        ImGui.Columns(1);
        ImGui.Separator();

        // Frame time graph (last 120 frames)
        var firstTiming = runner.Timings.Values.FirstOrDefault();
        if (firstTiming != null)
        {
            // Aggregate total frame times for graph
            // (simplified — shows first system's history as example)
            var history = firstTiming.History;
            float[] floats = new float[history.Length];
            for (int i = 0; i < history.Length; i++)
                floats[i] = (float)history[i];

            ImGui.PlotLines("Frame History", ref floats[0], floats.Length,
                0, null, 0f, 16.67f, new System.Numerics.Vector2(330, 60));
        }

        // MonoGame GPU metrics
        ImGui.Text($"Draw Calls: {_graphicsDevice?.Metrics.DrawCount ?? 0}");
        ImGui.Text($"Sprites: {_graphicsDevice?.Metrics.SpriteCount ?? 0}");
        ImGui.Text($"Texture Swaps: {_graphicsDevice?.Metrics.TextureCount ?? 0}");

        ImGui.End();
#endif
    }

    private static GraphicsDevice? _graphicsDevice;
    public static void SetGraphicsDevice(GraphicsDevice gd) => _graphicsDevice = gd;
}
```

### 2.4 MonoGame Built-in Metrics

MonoGame exposes GPU metrics every frame — check these before reaching for external tools:

```csharp
var m = GraphicsDevice.Metrics;

// Key metrics:
m.DrawCount;        // Number of draw calls this frame
m.SpriteCount;      // Number of sprites drawn
m.TextureCount;     // Number of texture swaps (batch breaks!)
m.TargetCount;      // Number of render target switches
m.PixelShaderCount; // Shader changes
m.PrimitiveCount;   // Total triangles submitted

// Reset happens automatically each frame in MonoGame
```

**Healthy baselines for 2D games:**

| Metric | Target | Concern |
|---|---|---|
| Draw calls | < 50 | > 100 = batching problem |
| Texture swaps | < 10 | Each swap breaks a batch |
| Render target switches | < 5 | Each is expensive |
| Sprites per frame | < 5000 | Depends on hardware |

---

## 3. .NET Profiling Tools

### 3.1 dotnet-counters (Live Monitoring)

Zero-overhead live monitoring. Install once, use forever:

```bash
# Install
dotnet tool install --global dotnet-counters

# List running .NET processes
dotnet-counters ps

# Monitor key game metrics (replace <pid> with your game's PID)
dotnet-counters monitor --process-id <pid> \
    --counters System.Runtime[gc-heap-size,gen-0-gc-count,gen-1-gc-count,gen-2-gc-count,time-in-gc,alloc-rate,cpu-usage,threadpool-thread-count]

# Monitor with refresh interval
dotnet-counters monitor --process-id <pid> --refresh-interval 1
```

**Key counters for games:**

| Counter | Healthy | Problem |
|---|---|---|
| `alloc-rate` | < 1 MB/s steady-state | > 5 MB/s = GC pressure |
| `gen-0-gc-count` | < 1/sec | > 5/sec = frequent allocations |
| `gen-2-gc-count` | 0 during gameplay | Any = frame hitch risk |
| `time-in-gc` | < 5% | > 10% = major GC problem |
| `gc-heap-size` | Stable | Growing = leak |

### 3.2 dotnet-trace (Detailed Profiling)

Captures detailed traces for offline analysis:

```bash
# Install
dotnet tool install --global dotnet-trace

# Collect a trace (run for 10 seconds of gameplay)
dotnet-trace collect --process-id <pid> --duration 00:00:10

# Collect with specific providers for game profiling
dotnet-trace collect --process-id <pid> \
    --providers Microsoft-Windows-DotNETRuntime:0x1:5 \
    --duration 00:00:10

# Output: trace.nettrace — open in Visual Studio, PerfView, or speedscope.app
```

**Workflow:**
1. Start your game in Release mode
2. Get to the scene you want to profile
3. Run `dotnet-trace collect`
4. Play through the problematic area
5. Stop collection
6. Open `.nettrace` in Visual Studio → Diagnostic Tools or upload to [speedscope.app](https://speedscope.app)

### 3.3 PerfView (Windows, Deep GC Analysis)

PerfView is the gold standard for GC analysis:

```bash
# Download from https://github.com/microsoft/perfview/releases

# Collect GC-focused trace
PerfView.exe /GCCollectOnly collect

# Collect CPU sampling + GC
PerfView.exe /GCCollectOnly /CpuSample collect
```

**PerfView GC analysis workflow:**
1. Open the `.etl` file
2. Go to **GC Stats** → shows every GC pause, generation, and trigger
3. Look for Gen2 collections during gameplay — each one is a potential frame hitch
4. **GC Heap Alloc Stacks** → shows exactly which call stacks are allocating

### 3.4 JetBrains dotTrace

Best integrated CPU profiler for .NET:

```
1. Open dotTrace → Attach to Process → select your game
2. Choose "Timeline" profiling (best for games — shows per-thread activity)
3. Play through the problem area
4. Stop and analyze:
   - Hot Spots view → functions consuming most time
   - Call Tree view → trace from root to leaf
   - Timeline → see CPU usage over time, correlate with GC pauses
```

**Key dotTrace features for games:**
- Timeline mode shows GC pauses as gaps in execution
- Filter by thread to isolate game thread vs render thread
- Compare snapshots before/after optimization

### 3.5 Visual Studio Diagnostic Tools

Built into Visual Studio — no install needed:

```
Debug → Performance Profiler (Alt+F2)
    ✓ CPU Usage          — hot path identification
    ✓ .NET Object Allocation Tracking — per-type allocation counts
    ✓ Memory Usage       — heap snapshots

Run in Release configuration for accurate results.
```

**CPU Usage workflow:**
1. Start profiling with CPU Usage checked
2. Play your game
3. Stop → drill into the hot path
4. Sort by **Self CPU** to find functions that are slow themselves (not just calling slow children)

---

## 4. GC Pressure Tracking

### 4.1 In-Game GC Monitor

Track GC behavior in real-time during development:

```csharp
public class GCMonitor
{
    private long _lastAllocatedBytes;
    private int _lastGen0;
    private int _lastGen1;
    private int _lastGen2;

    // Per-frame stats
    public long FrameAllocatedBytes { get; private set; }
    public int Gen0Collections { get; private set; }
    public int Gen1Collections { get; private set; }
    public int Gen2Collections { get; private set; }

    // Cumulative session stats
    public long TotalAllocatedBytes { get; private set; }
    public int TotalGen0 { get; private set; }
    public int TotalGen1 { get; private set; }
    public int TotalGen2 { get; private set; }

    // Alert state
    public bool Gen2Alert { get; private set; }
    public bool HighAllocAlert { get; private set; }

    private const long HighAllocThreshold = 1024; // bytes per frame

    public void Update()
    {
        // Per-thread allocation tracking (more accurate than GetTotalMemory)
        long currentAlloc = GC.GetAllocatedBytesForCurrentThread();
        FrameAllocatedBytes = currentAlloc - _lastAllocatedBytes;
        _lastAllocatedBytes = currentAlloc;
        TotalAllocatedBytes += FrameAllocatedBytes;

        // GC collection counts
        int gen0 = GC.CollectionCount(0);
        int gen1 = GC.CollectionCount(1);
        int gen2 = GC.CollectionCount(2);

        Gen0Collections = gen0 - _lastGen0;
        Gen1Collections = gen1 - _lastGen1;
        Gen2Collections = gen2 - _lastGen2;

        TotalGen0 += Gen0Collections;
        TotalGen1 += Gen1Collections;
        TotalGen2 += Gen2Collections;

        _lastGen0 = gen0;
        _lastGen1 = gen1;
        _lastGen2 = gen2;

        // Alerts
        Gen2Alert = Gen2Collections > 0;
        HighAllocAlert = FrameAllocatedBytes > HighAllocThreshold;
    }

    public void DrawImGui()
    {
#if DEBUG
        ImGui.Begin("GC Monitor");

        // Current frame
        if (HighAllocAlert)
            ImGui.TextColored(new System.Numerics.Vector4(1, 0, 0, 1),
                $"⚠ Frame Alloc: {FrameAllocatedBytes:N0} bytes");
        else
            ImGui.Text($"Frame Alloc: {FrameAllocatedBytes:N0} bytes");

        // GC collections this frame
        if (Gen2Alert)
            ImGui.TextColored(new System.Numerics.Vector4(1, 0, 0, 1),
                "⚠ GEN 2 COLLECTION THIS FRAME!");

        ImGui.Text($"Gen0: {Gen0Collections} | Gen1: {Gen1Collections} | Gen2: {Gen2Collections}");
        ImGui.Separator();

        // Session totals
        ImGui.Text($"Total Allocated: {TotalAllocatedBytes / 1024.0 / 1024.0:F2} MB");
        ImGui.Text($"Heap Size: {GC.GetTotalMemory(false) / 1024.0 / 1024.0:F2} MB");
        ImGui.Text($"Total GC: {TotalGen0}/{TotalGen1}/{TotalGen2} (G0/G1/G2)");
        ImGui.Text($"GC Latency Mode: {GCSettings.LatencyMode}");

        ImGui.End();
#endif
    }
}
```

### 4.2 Allocation Hot Path Finder

Pinpoint exactly where allocations happen per frame:

```csharp
public static class AllocationTracker
{
    [Conditional("DEBUG")]
    public static void BeginSection(string name)
    {
        _sectionStart[name] = GC.GetAllocatedBytesForCurrentThread();
    }

    [Conditional("DEBUG")]
    public static void EndSection(string name)
    {
        if (!_sectionStart.TryGetValue(name, out long start)) return;

        long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        if (allocated > 0)
        {
            if (!_sectionAllocations.TryGetValue(name, out var data))
            {
                data = new AllocationData();
                _sectionAllocations[name] = data;
            }
            data.TotalBytes += allocated;
            data.FrameCount++;
            data.MaxBytes = Math.Max(data.MaxBytes, allocated);
        }
    }

    [ThreadStatic]
    private static Dictionary<string, long>? _sectionStart;

    // Use a regular field — AllocationTracker itself is debug-only
    private static readonly Dictionary<string, AllocationData> _sectionAllocations = new();

    static AllocationTracker()
    {
        _sectionStart = new Dictionary<string, long>();
    }

    public class AllocationData
    {
        public long TotalBytes;
        public long MaxBytes;
        public int FrameCount;
        public double AvgBytes => FrameCount > 0 ? (double)TotalBytes / FrameCount : 0;
    }

    public static IReadOnlyDictionary<string, AllocationData> Results => _sectionAllocations;
}

// Usage — wrap suspicious sections
AllocationTracker.BeginSection("AI.Update");
_aiSystem.Update(dt);
AllocationTracker.EndSection("AI.Update");

AllocationTracker.BeginSection("Physics.Resolve");
_physicsSystem.Resolve();
AllocationTracker.EndSection("Physics.Resolve");
```

### 4.3 Zero-Allocation Patterns Recap

The goal: **zero bytes allocated per frame** in steady-state gameplay. Key patterns from [G13](./G13_csharp_performance.md):

| Allocation Source | Fix |
|---|---|
| `new T[]` in hot paths | `stackalloc` (< 1KB) or `ArrayPool<T>.Shared` |
| LINQ (`.Where()`, `.Select()`, `.ToList()`) | Manual `for` loops |
| String interpolation `$"Score: {x}"` | `StringBuilder` or cached strings |
| Lambda closures capturing locals | Static lambdas (C# 9+) or cached delegates |
| `Dictionary<K,V>` lookups with struct keys missing `IEquatable<T>` | Implement `IEquatable<T>` |
| Boxing value types via `object` params | Generic constraints `where T : struct` |
| `List<T>` growing during gameplay | Pre-allocate with known capacity at startup |
| `foreach` on non-struct enumerators | `for (int i = ...)` with index |

### 4.4 GC Latency Mode for Games

Configure the GC for low-latency gameplay:

```csharp
// At game startup — use low-latency mode during gameplay
GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
// Prevents full blocking GCs during gameplay
// Gen2 collections still happen but are concurrent (background)

// During loading screens — allow full GC
GCSettings.LatencyMode = GCLatencyMode.Interactive;
GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
```

> **Warning:** `SustainedLowLatency` delays Gen2 collections, increasing memory usage. Only use during active gameplay. Switch back during scene transitions and loading screens, and trigger a full compacting GC.

---

## 5. Draw Call Analysis

### 5.1 SpriteBatch Batch Counting

Track how efficiently SpriteBatch batches your draw calls:

```csharp
public class DrawCallAnalyzer
{
    private int _spriteCount;
    private int _drawCallsBefore;
    private int _textureSwapsBefore;

    // Snapshot metrics before a Begin/End block
    public void BeginTracking(GraphicsDevice gd)
    {
        _spriteCount = 0;
        _drawCallsBefore = gd.Metrics.DrawCount;
        _textureSwapsBefore = gd.Metrics.TextureCount;
    }

    // Call after each sprite draw to count
    public void CountSprite() => _spriteCount++;

    // Snapshot metrics after End()
    public BatchResult EndTracking(GraphicsDevice gd, string passName)
    {
        int drawCalls = gd.Metrics.DrawCount - _drawCallsBefore;
        int textureSwaps = gd.Metrics.TextureCount - _textureSwapsBefore;

        return new BatchResult
        {
            PassName = passName,
            SpriteCount = _spriteCount,
            DrawCalls = drawCalls,
            TextureSwaps = textureSwaps,
            SpritesPerBatch = drawCalls > 0 ? (float)_spriteCount / drawCalls : 0,
            Efficiency = _spriteCount > 0
                ? 1f - ((float)drawCalls / _spriteCount)
                : 1f
        };
    }

    public record struct BatchResult
    {
        public string PassName;
        public int SpriteCount;
        public int DrawCalls;
        public int TextureSwaps;
        public float SpritesPerBatch;
        public float Efficiency; // 1.0 = perfect (1 draw call), 0.0 = worst (1 per sprite)
    }
}
```

### 5.2 Texture Swap Tracking

Identify which textures are breaking batches:

```csharp
public class TextureSwapTracker
{
    private Texture2D? _lastTexture;
    private int _swapCount;
    private readonly Dictionary<string, int> _swapPairs = new();

    public void OnDraw(Texture2D texture, string spriteName)
    {
#if DEBUG
        if (_lastTexture != null && _lastTexture != texture)
        {
            _swapCount++;
            string pair = $"{_lastTexture.Name} → {texture.Name}";
            _swapPairs.TryGetValue(pair, out int count);
            _swapPairs[pair] = count + 1;
        }
        _lastTexture = texture;
#endif
    }

    public void ResetFrame()
    {
        _swapCount = 0;
        _lastTexture = null;
    }

    public void DrawImGui()
    {
#if DEBUG
        ImGui.Begin("Texture Swaps");
        ImGui.Text($"Total Swaps This Frame: {_swapCount}");
        ImGui.Separator();

        // Show most frequent swap pairs — these are your optimization targets
        var sorted = _swapPairs.OrderByDescending(kv => kv.Value);
        foreach (var (pair, count) in sorted)
        {
            ImGui.Text($"{count}x: {pair}");
        }

        if (ImGui.Button("Reset Stats"))
            _swapPairs.Clear();

        ImGui.End();
#endif
    }
}
```

### 5.3 Batching Efficiency Targets

| Scenario | Draw Calls | Assessment |
|---|---|---|
| 500 sprites, 1 atlas, 1 draw call | 1 | Perfect |
| 500 sprites, 5 atlases, 5 draw calls | 5 | Good |
| 500 sprites, 50 individual textures, 50 draw calls | 50 | Needs atlas packing |
| 500 sprites, `SpriteSortMode.Immediate` | 500 | Broken — never use Immediate for bulk sprites |

**Rule of thumb:** If `GraphicsDevice.Metrics.DrawCount` exceeds `Metrics.TextureCount + Begin/End pairs`, you have unnecessary batch breaks. Sort by texture or use atlases → [G2 Section 3](./G2_rendering_and_graphics.md).

---

## 6. Memory Profiling

### 6.1 JetBrains dotMemory

The most accessible .NET memory profiler:

```
1. Open dotMemory → Attach to Process
2. Take a baseline snapshot during idle gameplay
3. Play for 30 seconds
4. Take a second snapshot
5. Compare snapshots:
   - "New objects" → what was allocated between snapshots
   - "Survived objects" → potential leaks
   - Sort by "Retained size" → biggest memory consumers
   - Group by "Type" → find unexpected allocations
```

**What to look for:**
- `byte[]` growing → textures not disposed, ArrayPool buffers not returned
- `String` count increasing → string allocations in hot paths
- `Delegate` or `Action<>` growing → event handler leaks
- Any type count increasing over time → leak

### 6.2 Finding Texture Leaks

Textures are the most common GPU memory leak in MonoGame:

```csharp
public class TextureLeakDetector
{
    private readonly Dictionary<string, WeakReference<Texture2D>> _trackedTextures = new();
    private int _nextId;

    /// <summary>Call when creating or loading a texture</summary>
    public void Track(Texture2D texture, string description)
    {
#if DEBUG
        string key = $"{_nextId++}:{description}";
        _trackedTextures[key] = new WeakReference<Texture2D>(texture);
#endif
    }

    /// <summary>Call periodically (e.g., on scene transition) to find leaks</summary>
    public List<string> FindLeaks()
    {
        var leaks = new List<string>();
        var dead = new List<string>();

        foreach (var (key, weakRef) in _trackedTextures)
        {
            if (weakRef.TryGetTarget(out var texture))
            {
                if (texture.IsDisposed)
                    dead.Add(key); // properly disposed
                else
                    leaks.Add(key); // still alive — potential leak
            }
            else
            {
                dead.Add(key); // GC'd without Dispose — BAD, but not a leak
            }
        }

        foreach (var key in dead)
            _trackedTextures.Remove(key);

        return leaks;
    }
}
```

### 6.3 Event Handler Leak Detection

Event handlers are the #1 source of managed memory leaks in C# games → [G13](./G13_csharp_performance.md):

```csharp
// BAD: anonymous lambda — can never unsubscribe
entity.OnDeath += () => PlayExplosion(entity.Position);
// entity is NEVER garbage collected because the event holds a reference

// GOOD: named method — can unsubscribe in Dispose/Destroy
entity.OnDeath += HandleEntityDeath;
// In cleanup:
entity.OnDeath -= HandleEntityDeath;

// GOOD: weak event pattern for long-lived publishers
public class WeakEvent
{
    private readonly List<WeakReference<Action>> _handlers = new();

    public void Subscribe(Action handler)
    {
        _handlers.Add(new WeakReference<Action>(handler));
    }

    public void Raise()
    {
        for (int i = _handlers.Count - 1; i >= 0; i--)
        {
            if (_handlers[i].TryGetTarget(out var handler))
                handler();
            else
                _handlers.RemoveAt(i); // Auto-cleanup dead references
        }
    }
}
```

### 6.4 Content Manager Scoping

Use separate `ContentManager` instances per scene to enable bulk unloading:

```csharp
public class SceneContentManager : IDisposable
{
    private readonly ContentManager _content;
    private readonly List<IDisposable> _runtimeResources = new();

    public SceneContentManager(IServiceProvider services, string rootDirectory)
    {
        _content = new ContentManager(services, rootDirectory);
    }

    public T Load<T>(string assetName) => _content.Load<T>(assetName);

    /// <summary>Track runtime-created resources for disposal</summary>
    public T TrackRuntime<T>(T resource) where T : IDisposable
    {
        _runtimeResources.Add(resource);
        return resource;
    }

    public void Dispose()
    {
        // Unloads ALL content loaded through this manager
        _content.Unload();
        _content.Dispose();

        // Also dispose runtime-created resources
        foreach (var resource in _runtimeResources)
            resource.Dispose();
        _runtimeResources.Clear();
    }
}

// Usage — each scene gets its own content scope
public class BattleScene : IDisposable
{
    private readonly SceneContentManager _content;

    public BattleScene(IServiceProvider services)
    {
        _content = new SceneContentManager(services, "Content");
        var atlas = _content.Load<Texture2D>("battle/sprites");
        var rt = _content.TrackRuntime(
            new RenderTarget2D(gd, 480, 270));
    }

    public void Dispose() => _content.Dispose(); // Cleans up everything
}
```

---

## 7. Custom Profiling Framework

### 7.1 ProfilingScope Struct

A zero-overhead (in Release) scoped profiler using `IDisposable`:

```csharp
public readonly struct ProfilingScope : IDisposable
{
    private readonly string _name;
    private readonly long _startTicks;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ProfilingScope(string name)
    {
        _name = name;
#if DEBUG
        _startTicks = Stopwatch.GetTimestamp();
#else
        _startTicks = 0;
#endif
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
#if DEBUG
        long elapsed = Stopwatch.GetTimestamp() - _startTicks;
        double ms = (double)elapsed / Stopwatch.Frequency * 1000.0;
        ProfilerService.Instance.RecordSample(_name, ms);
#endif
    }
}

// Usage — natural scoping with using statement
public void UpdatePhysics(float dt)
{
    using var _ = new ProfilingScope("Physics.Update");

    using (new ProfilingScope("Physics.BroadPhase"))
    {
        RunBroadPhase();
    }

    using (new ProfilingScope("Physics.NarrowPhase"))
    {
        RunNarrowPhase();
    }

    using (new ProfilingScope("Physics.Resolve"))
    {
        ResolveCollisions();
    }
}
```

### 7.2 ProfilerService with Ring Buffer History

```csharp
public class ProfilerService
{
    public static ProfilerService Instance { get; } = new();

    private readonly Dictionary<string, ProfileEntry> _entries = new();
    private readonly List<FrameSnapshot> _frameHistory = new();
    private const int MaxFrameHistory = 300; // 5 seconds at 60fps

    private Dictionary<string, double>? _currentFrame;

    public void BeginFrame()
    {
        _currentFrame = new Dictionary<string, double>();
    }

    public void RecordSample(string name, double ms)
    {
        _currentFrame?[name] = ms;

        if (!_entries.TryGetValue(name, out var entry))
        {
            entry = new ProfileEntry(name);
            _entries[name] = entry;
        }
        entry.Record(ms);
    }

    public void EndFrame()
    {
        if (_currentFrame == null) return;

        _frameHistory.Add(new FrameSnapshot
        {
            Samples = _currentFrame,
            Timestamp = Stopwatch.GetTimestamp()
        });

        if (_frameHistory.Count > MaxFrameHistory)
            _frameHistory.RemoveAt(0);

        _currentFrame = null;
    }

    public IReadOnlyDictionary<string, ProfileEntry> Entries => _entries;
    public IReadOnlyList<FrameSnapshot> FrameHistory => _frameHistory;

    public class ProfileEntry
    {
        public string Name { get; }
        public double LastMs { get; private set; }
        public double MinMs { get; private set; } = double.MaxValue;
        public double MaxMs { get; private set; }
        public double AvgMs { get; private set; }

        private readonly double[] _ring = new double[120];
        private int _ringIndex;
        private int _sampleCount;

        public ProfileEntry(string name) => Name = name;

        public void Record(double ms)
        {
            LastMs = ms;
            MinMs = Math.Min(MinMs, ms);
            MaxMs = Math.Max(MaxMs, ms);

            _ring[_ringIndex] = ms;
            _ringIndex = (_ringIndex + 1) % _ring.Length;
            _sampleCount = Math.Min(_sampleCount + 1, _ring.Length);

            double sum = 0;
            for (int i = 0; i < _sampleCount; i++) sum += _ring[i];
            AvgMs = sum / _sampleCount;
        }

        public ReadOnlySpan<double> GetHistory()
        {
            return _ring.AsSpan(0, _sampleCount);
        }
    }

    public class FrameSnapshot
    {
        public Dictionary<string, double> Samples { get; init; } = new();
        public long Timestamp { get; init; }
    }
}
```

### 7.3 ImGui Profiler Panel with Flame Graph

```csharp
public static class ProfilerPanel
{
    private static bool _open = true;
    private static int _selectedFrame = -1;

    public static void Draw()
    {
#if DEBUG
        if (!_open) return;

        var profiler = ProfilerService.Instance;

        ImGui.Begin("Profiler", ref _open);

        // === Flame Graph (stacked bar per frame) ===
        ImGui.Text("Frame Timeline (last 300 frames)");

        var drawList = ImGui.GetWindowDrawList();
        var canvasPos = ImGui.GetCursorScreenPos();
        float canvasWidth = ImGui.GetContentRegionAvail().X;
        float canvasHeight = 80;

        // Background
        drawList.AddRectFilled(
            canvasPos,
            new System.Numerics.Vector2(canvasPos.X + canvasWidth, canvasPos.Y + canvasHeight),
            ImGui.GetColorU32(new System.Numerics.Vector4(0.1f, 0.1f, 0.1f, 1f)));

        // 16.67ms budget line
        float budgetY = canvasPos.Y + canvasHeight * (1f - 16.67f / 20f);
        drawList.AddLine(
            new System.Numerics.Vector2(canvasPos.X, budgetY),
            new System.Numerics.Vector2(canvasPos.X + canvasWidth, budgetY),
            ImGui.GetColorU32(new System.Numerics.Vector4(1f, 0.3f, 0.3f, 0.5f)));

        var history = profiler.FrameHistory;
        if (history.Count > 0)
        {
            float barWidth = canvasWidth / MaxFrameCount;

            // Assign colors to entries
            uint[] palette =
            {
                0xFF4444FF, // red
                0xFF44FF44, // green
                0xFFFF4444, // blue
                0xFF44FFFF, // yellow
                0xFFFF44FF, // magenta
                0xFFFFFF44, // cyan
                0xFF8888FF, // light red
                0xFF88FF88, // light green
            };

            var entryNames = profiler.Entries.Keys.ToArray();

            for (int f = 0; f < history.Count; f++)
            {
                float x = canvasPos.X + f * barWidth;
                float yBottom = canvasPos.Y + canvasHeight;
                float yOffset = 0;

                foreach (var (name, ms) in history[f].Samples)
                {
                    float height = (float)(ms / 20.0) * canvasHeight;
                    int colorIdx = Array.IndexOf(entryNames, name) % palette.Length;
                    if (colorIdx < 0) colorIdx = 0;

                    drawList.AddRectFilled(
                        new System.Numerics.Vector2(x, yBottom - yOffset - height),
                        new System.Numerics.Vector2(x + barWidth - 1, yBottom - yOffset),
                        palette[colorIdx]);

                    yOffset += height;
                }
            }
        }

        ImGui.Dummy(new System.Numerics.Vector2(canvasWidth, canvasHeight));
        ImGui.Separator();

        // === Per-Entry Stats Table ===
        ImGui.Columns(5, "profiler_entries");
        ImGui.Text("Section"); ImGui.NextColumn();
        ImGui.Text("Last"); ImGui.NextColumn();
        ImGui.Text("Avg"); ImGui.NextColumn();
        ImGui.Text("Min"); ImGui.NextColumn();
        ImGui.Text("Max"); ImGui.NextColumn();
        ImGui.Separator();

        foreach (var (name, entry) in profiler.Entries
            .OrderByDescending(kv => kv.Value.AvgMs))
        {
            // Indent nested scopes (names like "Physics.BroadPhase")
            int depth = name.Count(c => c == '.');
            string indent = new(' ', depth * 2);

            var color = entry.AvgMs > 5.0
                ? new System.Numerics.Vector4(1, 0.2f, 0.2f, 1)
                : entry.AvgMs > 2.0
                    ? new System.Numerics.Vector4(1, 0.7f, 0.2f, 1)
                    : new System.Numerics.Vector4(0.8f, 0.8f, 0.8f, 1);

            ImGui.TextColored(color, $"{indent}{name}"); ImGui.NextColumn();
            ImGui.Text($"{entry.LastMs:F3}"); ImGui.NextColumn();
            ImGui.Text($"{entry.AvgMs:F3}"); ImGui.NextColumn();
            ImGui.Text($"{entry.MinMs:F3}"); ImGui.NextColumn();
            ImGui.Text($"{entry.MaxMs:F3}"); ImGui.NextColumn();
        }

        ImGui.Columns(1);

        // === Legend ===
        ImGui.Separator();
        ImGui.TextColored(new System.Numerics.Vector4(1, 0.3f, 0.3f, 0.5f),
            "— Red line = 16.67ms (60fps budget)");

        ImGui.End();
#endif
    }

    private const int MaxFrameCount = 300;
}
```

### 7.4 Integration into Game Loop

```csharp
protected override void Update(GameTime gameTime)
{
    ProfilerService.Instance.BeginFrame();
    _gcMonitor.Update();

    using (new ProfilingScope("Input"))
        _inputSystem.Update();

    using (new ProfilingScope("AI"))
        _aiSystem.Update(fixedDt);

    using (new ProfilingScope("Physics"))
        _physicsSystem.Update(fixedDt);

    using (new ProfilingScope("Animation"))
        _animationSystem.Update(fixedDt);

    ProfilerService.Instance.EndFrame();
    base.Update(gameTime);
}

protected override void Draw(GameTime gameTime)
{
    using (new ProfilingScope("Draw.World"))
        DrawWorld();

    using (new ProfilingScope("Draw.UI"))
        DrawUI();

    using (new ProfilingScope("Draw.Debug"))
    {
        ProfilerOverlay.Draw(_systemRunner);
        ProfilerPanel.Draw();
        _gcMonitor.DrawImGui();
    }

    base.Draw(gameTime);
}
```

---

## 8. Common Bottlenecks in 2D Games

### 8.1 SpriteBatch Sort Overhead

`SpriteSortMode.BackToFront` and `FrontToBack` perform an O(n log n) sort on every `End()` call. With thousands of sprites, this becomes measurable:

| Sprite Count | Sort Time (approx) | Impact |
|---|---|---|
| 100 | ~0.01ms | Negligible |
| 1,000 | ~0.1ms | Fine |
| 5,000 | ~0.5ms | Noticeable |
| 10,000+ | ~2ms+ | Budget concern |

**Mitigations:**
- Use `SpriteSortMode.Deferred` for anything that doesn't need depth sorting (UI, particles, background)
- Use `SpriteSortMode.Texture` when depth doesn't matter but batching does
- Pre-sort your entity draw list yourself and use `Deferred` — sort once in your update loop rather than letting SpriteBatch re-sort every frame
- Split into multiple Begin/End pairs per layer, each using `Deferred` with manual draw order

### 8.2 Render Target Overuse

Each `SetRenderTarget()` call flushes the GPU pipeline:

```
SetRenderTarget(A)     ← flush
  Draw scene
SetRenderTarget(B)     ← flush
  Draw bloom pass
SetRenderTarget(C)     ← flush
  Draw blur pass
SetRenderTarget(null)  ← flush
  Composite

= 4 pipeline flushes per frame
```

**Mitigations:**
- Minimize render target switches — consolidate post-processing passes
- Use ping-pong buffering (2 targets) instead of many → [G2 Section 2.2](./G2_rendering_and_graphics.md)
- Skip post-processing entirely on low-end hardware
- Reuse render targets between scenes (create once at startup)

### 8.3 Shader Compilation Stalls

First-time shader compilation on some platforms causes multi-frame hitches:

```csharp
// BAD: loading and compiling a shader mid-gameplay
if (_playerUsedSpecialAttack)
{
    _glowEffect = Content.Load<Effect>("Shaders/Glow"); // 50-200ms stall!
    ApplyGlow();
}

// GOOD: pre-load ALL shaders during loading screen
protected override void LoadContent()
{
    _glowEffect = Content.Load<Effect>("Shaders/Glow");
    _blurEffect = Content.Load<Effect>("Shaders/Blur");
    _outlineEffect = Content.Load<Effect>("Shaders/Outline");
    // Also "warm" them by drawing a single invisible sprite with each
}
```

### 8.4 Off-Screen Drawing (No Culling)

Drawing sprites that are entirely off-screen wastes both CPU time (vertex submission) and GPU time (clipped triangles):

```csharp
// Simple frustum culling for sprites
public static bool IsVisible(Vector2 position, Vector2 size, Rectangle cameraBounds)
{
    return position.X + size.X > cameraBounds.Left
        && position.X < cameraBounds.Right
        && position.Y + size.Y > cameraBounds.Top
        && position.Y < cameraBounds.Bottom;
}

// In render system — skip off-screen entities
world.Query(in _renderQuery, (ref Position pos, ref Sprite spr) =>
{
    if (!IsVisible(pos.Value, spr.Size, _camera.VisibleBounds))
        return; // Skip — not visible

    _spriteBatch.Draw(spr.Texture, pos.Value, spr.Source, Color.White);
});
```

For large worlds, use spatial partitioning (grid or quadtree) to avoid even iterating off-screen entities → [G14](./G14_data_structures.md).

### 8.5 Large Texture Atlases

Texture atlas sizes have hardware limits and performance implications:

| Atlas Size | Memory (RGBA) | Max Batch Size | Recommendation |
|---|---|---|---|
| 1024×1024 | 4 MB | 2048 sprites | Small games, limited content |
| 2048×2048 | 16 MB | 2048 sprites | **Safe default for all hardware** |
| 4096×4096 | 64 MB | 2048 sprites | Desktop only — mobile GPUs may not support |
| 8192×8192 | 256 MB | 2048 sprites | Risky — some desktop GPUs cap at 4096 |

**Rules:**
- Stay at 2048×2048 for cross-platform safety
- Use multiple atlases grouped by usage (characters, tiles, UI, particles)
- ASTC compression reduces mobile memory by ~10× → [G15](./G15_game_loop.md)

### 8.6 Overdraw

Every pixel drawn multiple times costs fill rate. Measure with a solid color shader that makes overdraw visible:

```csharp
// Debug: render all sprites as semi-transparent red
// Bright red areas = high overdraw
_debugOverdrawEffect.Parameters["OverdrawColor"].SetValue(
    new Vector4(1f, 0f, 0f, 0.1f));

_spriteBatch.Begin(
    blendState: BlendState.Additive,
    effect: _debugOverdrawEffect);
// ... draw all sprites ...
_spriteBatch.End();
```

**Targets:** ~1.5× overdraw is normal for 2D games. 3×+ means you have stacking issues (fullscreen particles, overlapping backgrounds, unnecessary layers).

---

## 9. Optimization Workflow

### 9.1 The Measure → Identify → Fix → Verify Cycle

Never optimize without measuring. Never consider an optimization done without verification.

```
┌──────────────────────────────────────────────────────┐
│                                                       │
│   1. MEASURE                                          │
│      ├─ Profile with tools from Sections 2-6          │
│      ├─ Record baseline metrics (frame time, p99,     │
│      │   draw calls, alloc rate, GC counts)           │
│      └─ Save the numbers — you need them later        │
│                                                       │
│   2. IDENTIFY                                         │
│      ├─ Find the #1 bottleneck (not the #2 or #3)     │
│      ├─ Is it CPU or GPU? Which system? Which call?   │
│      └─ Form a hypothesis about WHY it's slow         │
│                                                       │
│   3. FIX                                              │
│      ├─ Change ONE thing                              │
│      ├─ The simplest fix that addresses the root cause│
│      └─ Don't shotgun — targeted surgery              │
│                                                       │
│   4. VERIFY                                           │
│      ├─ Re-measure with identical conditions           │
│      ├─ Compare against baseline                       │
│      ├─ If improved: commit and document               │
│      └─ If not: revert and re-identify                 │
│                                                       │
│   Repeat until frame time meets budget.               │
└──────────────────────────────────────────────────────┘
```

### 9.2 A/B Benchmarking Pattern

Compare two implementations under identical conditions:

```csharp
public class ABBenchmark
{
    private readonly Stopwatch _sw = new();
    private readonly double[] _samplesA;
    private readonly double[] _samplesB;
    private int _indexA, _indexB;
    private readonly int _sampleCount;

    public bool UseVariantB { get; set; }
    public bool Complete => _indexA >= _sampleCount && _indexB >= _sampleCount;

    public ABBenchmark(int samplesPerVariant = 600) // 10 seconds at 60fps
    {
        _sampleCount = samplesPerVariant;
        _samplesA = new double[samplesPerVariant];
        _samplesB = new double[samplesPerVariant];
    }

    public void BeginMeasure() => _sw.Restart();

    public void EndMeasure()
    {
        _sw.Stop();
        double ms = _sw.Elapsed.TotalMilliseconds;

        if (!UseVariantB && _indexA < _sampleCount)
            _samplesA[_indexA++] = ms;
        else if (UseVariantB && _indexB < _sampleCount)
            _samplesB[_indexB++] = ms;
    }

    public string GetResults()
    {
        if (!Complete) return "Benchmark incomplete";

        Array.Sort(_samplesA, 0, _indexA);
        Array.Sort(_samplesB, 0, _indexB);

        double avgA = _samplesA.Take(_indexA).Average();
        double avgB = _samplesB.Take(_indexB).Average();
        double p99A = _samplesA[(int)(_indexA * 0.99)];
        double p99B = _samplesB[(int)(_indexB * 0.99)];

        double improvement = (avgA - avgB) / avgA * 100;

        return $"""
            Variant A: avg={avgA:F3}ms  p99={p99A:F3}ms
            Variant B: avg={avgB:F3}ms  p99={p99B:F3}ms
            Improvement: {improvement:F1}%
            Winner: {(avgB < avgA ? "B" : "A")}
            """;
    }
}
```

**Usage:**
1. Run 10 seconds with Variant A (original code)
2. Press a key to switch to Variant B (optimized code)
3. Run 10 seconds with Variant B
4. Read the comparison

### 9.3 Regression Detection

Track frame time history to catch performance regressions early:

```csharp
public class PerformanceBaseline
{
    private readonly string _baselinePath;

    public record BaselineData
    {
        public double AvgFrameMs { get; init; }
        public double P99FrameMs { get; init; }
        public int AvgDrawCalls { get; init; }
        public long AvgAllocBytesPerFrame { get; init; }
        public DateTime RecordedAt { get; init; }
        public string Scene { get; init; } = "";
        public string GitCommit { get; init; } = "";
    }

    public PerformanceBaseline(string path) => _baselinePath = path;

    public void SaveBaseline(BaselineData data)
    {
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(_baselinePath, json);
    }

    public BaselineData? LoadBaseline()
    {
        if (!File.Exists(_baselinePath)) return null;
        var json = File.ReadAllText(_baselinePath);
        return JsonSerializer.Deserialize<BaselineData>(json);
    }

    public string Compare(BaselineData current, BaselineData baseline)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Baseline: {baseline.GitCommit} ({baseline.RecordedAt:g})");
        sb.AppendLine($"Current:  {current.GitCommit} ({current.RecordedAt:g})");
        sb.AppendLine();

        CompareMetric(sb, "Avg Frame", baseline.AvgFrameMs, current.AvgFrameMs, "ms");
        CompareMetric(sb, "P99 Frame", baseline.P99FrameMs, current.P99FrameMs, "ms");
        CompareMetric(sb, "Draw Calls", baseline.AvgDrawCalls, current.AvgDrawCalls, "");
        CompareMetric(sb, "Alloc/Frame",
            baseline.AvgAllocBytesPerFrame, current.AvgAllocBytesPerFrame, "bytes");

        return sb.ToString();
    }

    private static void CompareMetric(StringBuilder sb, string name,
        double baseline, double current, string unit)
    {
        double delta = ((current - baseline) / baseline) * 100;
        string arrow = delta > 5 ? "⚠️ ↑" : delta < -5 ? "✅ ↓" : "  ≈";
        sb.AppendLine($"  {arrow} {name}: {baseline:F2} → {current:F2} {unit} ({delta:+0.0;-0.0}%)");
    }
}
```

---

## 10. Mobile-Specific Profiling

### 10.1 Xcode Instruments (iOS)

The definitive iOS profiling tool:

```
Product → Profile (Cmd+I) in Xcode

Key Instruments for MonoGame:
┌──────────────────────────────────────────────────┐
│ Time Profiler      — CPU hot paths per thread    │
│ Allocations        — heap allocations over time  │
│ Leaks              — reference cycles            │
│ Metal System Trace — GPU frame timing, shaders   │
│ Core Animation     — frame drops, compositing    │
│ Energy Log         — battery impact, CPU/GPU/net │
│ Thermal State      — throttling detection        │
└──────────────────────────────────────────────────┘
```

**Metal System Trace workflow:**
1. Select Metal System Trace template
2. Record 10-20 seconds of gameplay
3. Look at GPU timeline: are frames completing within budget?
4. Check for shader compilation stalls (long gaps)
5. Check vertex/fragment shader time ratio

### 10.2 Android GPU Inspector (AGI)

```
1. Download AGI from https://gpuinspector.dev
2. Connect Android device via USB
3. Launch your game
4. Start a System Profile capture
5. Analyze:
   - GPU Counters → shader core utilization, memory bandwidth
   - Frame Timeline → per-frame GPU time
   - Vulkan API calls → draw call overhead
```

### 10.3 Thermal Throttling Detection

Mobile GPUs throttle aggressively — detect and respond:

```csharp
public class ThermalMonitor
{
    private double _avgFrameMs;
    private double _baselineFrameMs;
    private bool _baselineSet;
    private int _frameCount;

    public bool IsThrottling { get; private set; }
    public float ThrottleRatio { get; private set; } // 1.0 = normal, 0.5 = 50% perf

    public void Update(double frameMs)
    {
        _frameCount++;

        // Establish baseline from first 300 frames (5 seconds)
        if (_frameCount < 300)
        {
            _baselineFrameMs = _baselineFrameMs == 0
                ? frameMs
                : _baselineFrameMs * 0.99 + frameMs * 0.01;
            return;
        }

        if (!_baselineSet)
        {
            _baselineSet = true;
        }

        // Smooth current average
        _avgFrameMs = _avgFrameMs * 0.95 + frameMs * 0.05;

        // Detect throttling: frame times 40%+ above baseline
        ThrottleRatio = (float)(_baselineFrameMs / _avgFrameMs);
        IsThrottling = ThrottleRatio < 0.7f;
    }

    /// <summary>
    /// Call this to get recommended quality level (1.0 = full, 0.5 = reduced)
    /// </summary>
    public float RecommendedQuality => Math.Clamp(ThrottleRatio, 0.3f, 1.0f);
}
```

### 10.4 Adaptive Quality System

Respond to thermal throttling automatically:

```csharp
public class AdaptiveQuality
{
    private readonly ThermalMonitor _thermal;
    private float _currentScale = 1.0f;

    public int ParticleLimit => (int)(200 * _currentScale);
    public bool PostProcessingEnabled => _currentScale > 0.7f;
    public int InternalResScale => _currentScale > 0.8f ? 1 : 2; // 1 = full, 2 = half

    public void Update(double frameMs)
    {
        _thermal.Update(frameMs);

        if (_thermal.IsThrottling)
        {
            _currentScale = Math.Max(0.3f, _currentScale - 0.01f);
        }
        else if (_currentScale < 1.0f)
        {
            _currentScale = Math.Min(1.0f, _currentScale + 0.002f); // Recover slowly
        }
    }
}
```

### 10.5 Battery Impact Considerations

| Factor | Battery Impact | Mitigation |
|---|---|---|
| 120Hz vs 60Hz | +31-44% display power | Lock to 60Hz for non-action games → [G15](./G15_game_loop.md) |
| GPU shader complexity | High | Simplify shaders on mobile |
| Network polling | High | Batch requests, reduce frequency |
| Continuous rendering | High | Pause rendering when app is backgrounded |
| Haptics | Low-moderate | Use sparingly |

---

## 11. ECS-Specific Optimization

### 11.1 Query Cost

Arch ECS queries iterate over matching archetypes. Query performance depends on:

```csharp
// FAST: query with specific components — fewer archetypes match
var narrowQuery = new QueryDescription()
    .WithAll<Position, Velocity, ActiveTag>();

// SLOWER: broad query — matches many archetypes
var broadQuery = new QueryDescription()
    .WithAll<Position>();

// FASTEST: inline query (Arch source generator) — zero delegate overhead
world.Query(in narrowQuery, (ref Position pos, ref Velocity vel) =>
{
    pos.Value += vel.Value * dt;
});

// SLOWER: entity-by-entity with World.Get
// (Don't do this — use queries instead)
foreach (var entity in _entityList)
{
    ref var pos = ref world.Get<Position>(entity);  // Hash lookup per call
    ref var vel = ref world.Get<Velocity>(entity);  // Another hash lookup
    pos.Value += vel.Value * dt;
}
```

### 11.2 Archetype Fragmentation

Every unique combination of components creates a new archetype. Excessive fragmentation hurts iteration:

```csharp
// BAD: each entity has slightly different components → many archetypes
world.Create(new Position(), new Velocity(), new PlayerTag());
world.Create(new Position(), new Velocity(), new EnemyTag());
world.Create(new Position(), new Velocity(), new BulletTag());
world.Create(new Position(), new Velocity(), new ItemTag());
// 4 archetypes — query for Position+Velocity iterates 4 small chunks

// BETTER: use an enum tag component instead of separate tag types
public record struct EntityType(EntityKind Kind);
public enum EntityKind { Player, Enemy, Bullet, Item }

world.Create(new Position(), new Velocity(), new EntityType(EntityKind.Player));
world.Create(new Position(), new Velocity(), new EntityType(EntityKind.Enemy));
// 1 archetype — query iterates 1 contiguous chunk

// BUT: separate tag types are fine if you filter on them in queries.
// Arch iterates archetypes efficiently — only measure if you suspect a problem.
```

**Measuring archetype count:**
```csharp
#if DEBUG
// Log archetype stats periodically
ImGui.Text($"Entity Count: {world.Size}");
// Arch doesn't expose archetype count directly in the public API,
// but you can infer fragmentation from query performance:
// If a query matching Position+Velocity is slow relative to entity count,
// you likely have too many archetypes.
#endif
```

### 11.3 System Ordering for Cache Locality

Systems that access the same components should run consecutively — their data will still be in CPU cache:

```csharp
// GOOD ordering: physics systems share Position, Velocity data
runner.Register("Movement",  dt => _movementSystem.Update(dt));   // reads Position, Velocity
runner.Register("Collision", dt => _collisionSystem.Update(dt));  // reads Position, Collider
runner.Register("Physics",   dt => _physicsSystem.Update(dt));    // reads Position, Velocity

// GAP: AI system accesses different components, evicts physics data from cache
runner.Register("AI",        dt => _aiSystem.Update(dt));         // reads AIState, BehaviorTree

// These should NOT be interleaved with physics systems
runner.Register("Animation", dt => _animationSystem.Update(dt));  // reads AnimationState
runner.Register("Rendering", dt => _renderSystem.Update(dt));     // reads Position, Sprite
```

### 11.4 Parallel Queries

Arch supports parallel query execution for systems that don't write to shared state:

```csharp
// Parallel query — safe for read-only or non-overlapping writes
world.ParallelQuery(in _movementQuery, (ref Position pos, ref Velocity vel) =>
{
    pos.Value += vel.Value * dt;
});

// RULES for parallel safety:
// ✅ Each entity's components are modified independently
// ✅ No shared mutable state (counters, lists, etc.)
// ❌ Don't create/destroy entities inside parallel queries
// ❌ Don't access World.Get/Set/Has from inside parallel queries
// ❌ Don't write to shared collections without locking
```

**When to parallelize:**

| Entity Count | Parallel Benefit | Notes |
|---|---|---|
| < 1,000 | None / worse | Thread overhead exceeds gains |
| 1,000-10,000 | Moderate | 1.5-2× speedup on heavy systems |
| 10,000+ | Significant | 2-4× speedup, worth the complexity |

### 11.5 Hot/Cold Component Splitting

Separate frequently-accessed data from rarely-accessed data:

```csharp
// HOT components — accessed every frame by movement/render systems
public record struct Position(Vector2 Value);
public record struct Velocity(Vector2 Value);
public record struct Sprite(Texture2D Texture, Rectangle Source);

// COLD components — accessed rarely (on hit, on inspect, on save)
public record struct EntityName(string Value);
public record struct LootTable(int[] ItemIds);
public record struct DebugInfo(string CreatedBy, int TickCreated);

// Hot and cold components in the same archetype is fine in Arch —
// the ECS only loads the component arrays you actually access in a query.
// But if a cold component is LARGE (e.g., 256+ bytes), splitting it
// into a separate entity or using a lookup dictionary is worthwhile.
```

---

## 12. Performance Testing

### 12.1 Automated Frame Time Tests

Run as part of your test suite to catch regressions:

```csharp
[TestClass]
public class PerformanceTests
{
    [TestMethod]
    public void MainGameplay_MeetsFrameBudget()
    {
        // Arrange
        using var game = new HeadlessGame(); // Game subclass that skips GPU init
        game.LoadScene("main_gameplay");
        game.SpawnTestEntities(500);

        // Act — simulate 600 frames
        var frameTimes = new double[600];
        for (int i = 0; i < 600; i++)
        {
            var sw = Stopwatch.StartNew();
            game.SimulateFrame(1f / 60f);
            sw.Stop();
            frameTimes[i] = sw.Elapsed.TotalMilliseconds;
        }

        // Assert
        double avg = frameTimes.Average();
        Array.Sort(frameTimes);
        double p99 = frameTimes[(int)(frameTimes.Length * 0.99)];

        Assert.IsTrue(avg < 12.0,
            $"Average frame time {avg:F2}ms exceeds 12ms budget");
        Assert.IsTrue(p99 < 16.0,
            $"P99 frame time {p99:F2}ms exceeds 16ms budget");
    }

    [TestMethod]
    public void SteadyState_ZeroAllocations()
    {
        using var game = new HeadlessGame();
        game.LoadScene("main_gameplay");
        game.SpawnTestEntities(200);

        // Warm up
        for (int i = 0; i < 120; i++)
            game.SimulateFrame(1f / 60f);

        // Measure
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 300; i++)
            game.SimulateFrame(1f / 60f);
        long after = GC.GetAllocatedBytesForCurrentThread();

        long allocated = after - before;
        Assert.IsTrue(allocated < 1024,
            $"Steady-state allocated {allocated} bytes over 300 frames " +
            $"({allocated / 300.0:F1} bytes/frame). Target: 0.");
    }
}
```

### 12.2 Stress Test Scenes

Create dedicated scenes that push your game to its limits:

```csharp
public class StressTestScene
{
    private readonly World _world;
    private readonly int _entityCount;

    public static StressTestScene CreateEntityStress(World world, int count)
    {
        var scene = new StressTestScene(world, count);

        for (int i = 0; i < count; i++)
        {
            world.Create(
                new Position(new Vector2(
                    Random.Shared.NextSingle() * 1920,
                    Random.Shared.NextSingle() * 1080)),
                new Velocity(new Vector2(
                    Random.Shared.NextSingle() * 200 - 100,
                    Random.Shared.NextSingle() * 200 - 100)),
                new Sprite(/* default sprite */),
                new Collider(new Rectangle(0, 0, 16, 16)),
                new Health(100)
            );
        }

        return scene;
    }

    public static StressTestScene CreateDrawCallStress(World world)
    {
        // Create entities with DIFFERENT textures to maximize draw calls
        for (int i = 0; i < 1000; i++)
        {
            // Each uses a unique 1x1 texture — worst case for batching
            var tex = new Texture2D(/* graphics device */, 1, 1);
            world.Create(
                new Position(new Vector2(i % 40 * 48, i / 40 * 48)),
                new Sprite(tex, new Rectangle(0, 0, 1, 1))
            );
        }

        return new StressTestScene(world, 1000);
    }
}
```

### 12.3 Entity Count Scaling Tests

Find where your game's performance degrades:

```csharp
[TestMethod]
public void EntityScaling_LinearOrBetter()
{
    var results = new List<(int Count, double AvgMs)>();

    foreach (int count in new[] { 100, 500, 1000, 2000, 5000, 10000 })
    {
        using var game = new HeadlessGame();
        game.LoadScene("empty");
        game.SpawnTestEntities(count);

        // Warm up
        for (int i = 0; i < 60; i++)
            game.SimulateFrame(1f / 60f);

        // Measure
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 300; i++)
            game.SimulateFrame(1f / 60f);
        sw.Stop();

        double avgMs = sw.Elapsed.TotalMilliseconds / 300.0;
        results.Add((count, avgMs));
    }

    // Verify roughly linear scaling
    // Time at 10x entities should be < 15x time (allowing some overhead)
    var baseline = results.First();
    var maxCount = results.Last();

    double scaleFactor = (double)maxCount.Count / baseline.Count;
    double timeRatio = maxCount.AvgMs / baseline.AvgMs;

    Assert.IsTrue(timeRatio < scaleFactor * 1.5,
        $"Non-linear scaling detected: {scaleFactor}x entities = " +
        $"{timeRatio:F1}x time (expected < {scaleFactor * 1.5:F1}x)");

    // Log results for analysis
    foreach (var (count, ms) in results)
        Console.WriteLine($"  {count,6} entities: {ms:F2}ms avg");
}
```

### 12.4 CI Integration

Add performance tests to your CI pipeline:

```yaml
# .github/workflows/perf.yml
name: Performance Regression Check

on:
  pull_request:
    branches: [main]

jobs:
  perf-test:
    runs-on: ubuntu-latest  # or self-hosted for consistent hardware
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'

      - name: Run Performance Tests
        run: |
          dotnet test tests/Performance/ \
            --configuration Release \
            --logger "trx;LogFileName=perf-results.trx" \
            --results-directory ./perf-results

      - name: Upload Results
        uses: actions/upload-artifact@v4
        with:
          name: perf-results
          path: perf-results/

      - name: Comment PR with Results
        if: github.event_name == 'pull_request'
        uses: marocchino/sticky-pull-request-comment@v2
        with:
          path: perf-results/summary.md
```

**Headless game wrapper for CI (no GPU required):**

```csharp
/// <summary>
/// Game subclass that runs Update() without GPU/Draw().
/// Used for automated performance testing in CI environments.
/// </summary>
public class HeadlessGame : IDisposable
{
    private readonly World _world = World.Create();
    private readonly TimedSystemRunner _runner = new();

    public HeadlessGame()
    {
        // Register all gameplay systems (no rendering)
        _runner.Register("Input", dt => { /* mock input */ });
        _runner.Register("AI", dt => _aiSystem.Update(dt));
        _runner.Register("Physics", dt => _physicsSystem.Update(dt));
        _runner.Register("Collision", dt => _collisionSystem.Update(dt));
        _runner.Register("Animation", dt => _animationSystem.Update(dt));
    }

    public void LoadScene(string name) { /* load scene data */ }

    public void SpawnTestEntities(int count)
    {
        for (int i = 0; i < count; i++)
        {
            _world.Create(
                new Position(new Vector2(i * 10f, 0)),
                new Velocity(new Vector2(1, 0)),
                new Health(100)
            );
        }
    }

    public void SimulateFrame(float dt) => _runner.RunAll(dt);

    public void Dispose() => World.Destroy(_world);
}
```

---

## Quick Reference

### Profiling Tool Decision Matrix

| Question | Tool | Section |
|---|---|---|
| "Which system is slowest?" | Built-in Stopwatch timing + ImGui | §2 |
| "Am I CPU or GPU bound?" | BoundDetector + MonoGame Metrics | §1.2 |
| "Where are allocations?" | `GC.GetAllocatedBytesForCurrentThread()` + AllocationTracker | §4 |
| "Why did I get a GC pause?" | dotnet-counters, PerfView | §3.1, §3.3 |
| "What's the hot path?" | dotTrace Timeline, VS CPU Usage | §3.4, §3.5 |
| "Do I have a memory leak?" | dotMemory heap snapshots, TextureLeakDetector | §6 |
| "How efficient is batching?" | `GraphicsDevice.Metrics` + DrawCallAnalyzer | §5 |
| "Is mobile throttling?" | ThermalMonitor, Xcode Instruments | §10 |
| "Did this PR regress perf?" | Automated perf tests in CI | §12.4 |

### Optimization Priority Order

1. **Algorithmic improvements** — O(n²) → O(n log n) dwarfs everything else
2. **Frustum culling** — skip off-screen work entirely → [G15](./G15_game_loop.md)
3. **Texture atlasing** — collapse draw calls → [G2](./G2_rendering_and_graphics.md)
4. **GC elimination** — zero alloc steady-state → [G13](./G13_csharp_performance.md)
5. **Spatial partitioning** — grid/quadtree for collision → [G14](./G14_data_structures.md)
6. **Object pooling** — reuse instead of allocate → [G1](./G1_custom_code_recipes.md)
7. **ECS query optimization** — narrow queries, hot/cold split (§11)
8. **Parallelization** — only after single-thread is optimized (§11.4)
9. **SIMD** — batch math operations → [G13](./G13_csharp_performance.md)
10. **Platform-specific tuning** — mobile quality scaling (§10)

### The Cardinal Rules

1. **Measure before optimizing** — intuition about performance is unreliable
2. **Focus on p99 frame times** — a single 50ms spike per second is visible; averages hide it
3. **Optimize the bottleneck** — making the fast path faster doesn't help
4. **Change one thing at a time** — otherwise you can't attribute the improvement
5. **Keep the optimization** — only if measurement proves it helped
6. **Profile in Release builds** — Debug builds have completely different performance characteristics
7. **Profile on target hardware** — your dev machine is not your target platform
8. **Ship games** — good enough performance that ships beats perfect performance that doesn't
