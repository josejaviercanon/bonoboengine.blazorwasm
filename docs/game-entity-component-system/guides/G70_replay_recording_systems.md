# G70 — Replay & Recording Systems

> **Category:** Guide · **Related:** [G7 Input Handling](./G7_input_handling.md) · [G15 Game Loop](./G15_game_loop.md) · [G9 Networking](./G9_networking.md) · [G69 Save/Load Serialization](./G69_save_load_serialization.md) · [G67 Object Pooling](./G67_object_pooling.md) · [G64 Combat & Damage Systems](./G64_combat_damage_systems.md) · [G20 Camera Systems](./G20_camera_systems.md)

> A complete implementation guide for replay, recording, and playback systems in MonoGame + Arch ECS. Covers input recording, deterministic replay, ghost trails, killcams, spectator modes, demo files, and replay UI. Everything is composable — use the recording approach your game needs.

---

## Table of Contents

1. [Design Philosophy](#1--design-philosophy)
2. [Recording Architecture Overview](#2--recording-architecture-overview)
3. [Input Recording](#3--input-recording)
4. [Deterministic Simulation](#4--deterministic-simulation)
5. [State Snapshot Recording](#5--state-snapshot-recording)
6. [Hybrid Recording](#6--hybrid-recording)
7. [Ghost Replay System](#7--ghost-replay-system)
8. [Killcam & Highlight System](#8--killcam--highlight-system)
9. [Spectator & Free Camera](#9--spectator--free-camera)
10. [Demo File Format](#10--demo-file-format)
11. [Replay Playback Engine](#11--replay-playback-engine)
12. [Time Control (Slow-Mo, Rewind, Seek)](#12--time-control-slow-mo-rewind-seek)
13. [Replay Validation & Anti-Cheat](#13--replay-validation--anti-cheat)
14. [Replay UI & HUD](#14--replay-ui--hud)
15. [Network Replay & Server-Side Recording](#15--network-replay--server-side-recording)
16. [Genre-Specific Patterns](#16--genre-specific-patterns)
17. [Performance & Memory](#17--performance--memory)
18. [Common Mistakes & Anti-Patterns](#18--common-mistakes--anti-patterns)
19. [Tuning Reference](#19--tuning-reference)

---

## 1 — Design Philosophy

### Why Replay Systems Matter

Replay systems serve multiple purposes beyond "watch the game again":

- **Player engagement**: Death replays, highlight reels, and killcams turn losses into learning moments and wins into shareable content.
- **Competitive integrity**: Tournament replays let players study opponents and verify fair play.
- **Ghost racing**: Side-by-side comparison against your personal best or world records.
- **Debugging**: Record gameplay to reproduce bugs deterministically — the most powerful debugging tool you'll ever build.
- **Content creation**: Players sharing replays is free marketing.
- **QA & Telemetry**: Record play sessions for heatmaps, balance analysis, and automated regression testing.

### The Two Fundamental Approaches

Every replay system falls into one of two categories:

```
┌──────────────────────────────────────────────────────────────────┐
│                     RECORDING APPROACH                           │
├───────────────────────────┬──────────────────────────────────────┤
│    INPUT RECORDING        │      STATE RECORDING                 │
│                           │                                      │
│  Record: player inputs    │  Record: entity positions,           │
│  per tick                 │  states, events per tick             │
│                           │                                      │
│  Replay: re-simulate      │  Replay: interpolate between         │
│  the entire game          │  snapshots                           │
│                           │                                      │
│  ✅ Tiny file size         │  ✅ Always accurate                  │
│  ✅ Perfect accuracy*      │  ✅ Random-access seek               │
│  ✅ Any camera angle       │  ✅ No determinism needed            │
│                           │                                      │
│  ❌ Requires determinism   │  ❌ Large file size                  │
│  ❌ No random-access seek  │  ❌ Limited to recorded data         │
│  ❌ Fragile across updates │  ❌ Interpolation artifacts          │
│                           │                                      │
│  Best for: fighting games, │  Best for: action games, FPS,       │
│  racing, RTS, roguelikes   │  battle royale, sports              │
│                           │                                      │
│  * if simulation is        │                                      │
│    deterministic           │                                      │
└───────────────────────────┴──────────────────────────────────────┘
```

Most production games use a **hybrid** — input recording for the canonical replay, with periodic state snapshots for seeking and validation.

### ECS Fit

Replay maps cleanly onto ECS:

| Concept | ECS Role |
|---------|----------|
| Input frames | Value-type records stored in a ring buffer or list |
| State snapshots | Serialized component arrays at a given tick |
| Ghost entities | Entities with `Ghost` tag, driven by playback data |
| Replay camera | Entity with `ReplayCamera` + `FreeCamera` or `FollowCamera` |
| Recording state | Singleton resource (`RecordingState`) on the World |
| Playback control | Singleton resource (`PlaybackState`) controlling tick advancement |

---

## 2 — Recording Architecture Overview

### System Pipeline

```
                    RECORDING                          PLAYBACK
                    
  Player Input ──────────────┐
         │                   │
         ▼                   ▼
  ┌─────────────┐   ┌──────────────┐
  │  Game Loop   │   │ Input Buffer │──── serialize ──► Demo File
  │  (simulate)  │   └──────────────┘                     │
  │              │                                        │
  │  tick 0      │   ┌──────────────┐                     │
  │  tick 1      │   │  Snapshots   │──── serialize ──►   │
  │  tick 2      │   │  (periodic)  │                     │
  │   ...        │   └──────────────┘                     │
  └──────┬───────┘                                        │
         │                                                │
         ▼                                                ▼
   Live Gameplay                                ┌─────────────────┐
                                                │  Replay Engine   │
                                                │                  │
                                                │  ► Play/Pause    │
                                                │  ► Speed control │
                                                │  ► Seek (snaps)  │
                                                │  ► Free camera   │
                                                │  ► Slow-mo       │
                                                └─────────────────┘
```

### Core Components

```csharp
namespace MyGame.Replay;

/// <summary>Per-tick input snapshot. Designed as a blittable struct for fast serialization.</summary>
public record struct InputFrame(
    uint Tick,
    byte PlayerId,
    InputActions Actions,   // bitmask of pressed actions
    float MoveX,            // -1..1, quantized to sbyte in serialization
    float MoveY,            // -1..1
    float AimAngle          // radians, quantized to ushort in serialization
);

/// <summary>Bitmask for all possible player actions.</summary>
[Flags]
public enum InputActions : ushort
{
    None        = 0,
    MoveLeft    = 1 << 0,
    MoveRight   = 1 << 1,
    MoveUp      = 1 << 2,
    MoveDown    = 1 << 3,
    Jump        = 1 << 4,
    Attack      = 1 << 5,
    Dash        = 1 << 6,
    Interact    = 1 << 7,
    UseItem     = 1 << 8,
    Block       = 1 << 9,
    Special1    = 1 << 10,
    Special2    = 1 << 11,
    Pause       = 1 << 12
    // bits 13-15 reserved
}

/// <summary>Recording session metadata.</summary>
public record struct ReplayHeader(
    uint Version,           // replay format version
    string GameVersion,     // game build version string
    uint Seed,              // RNG seed for deterministic replay
    uint TotalTicks,        // total tick count in recording
    byte PlayerCount,       // number of players recorded
    long StartTimestamp,    // UTC Unix timestamp
    string LevelId,         // level/map identifier
    string Checksum         // integrity hash of recording data
);

/// <summary>Global recording state — singleton resource on the World.</summary>
public record struct RecordingState(
    bool IsRecording,
    bool IsPlaying,
    uint CurrentTick,
    uint SnapshotInterval   // ticks between state snapshots (0 = disabled)
);
```

### Recording Manager

```csharp
namespace MyGame.Replay;

/// <summary>
/// Central replay manager. Handles recording, serialization, and playback orchestration.
/// One instance per game session.
/// </summary>
public sealed class ReplayManager
{
    // Configuration
    private readonly uint _snapshotInterval;
    private readonly int _maxRecordingTicks;
    
    // Recording buffers
    private readonly List<InputFrame> _inputFrames = new(capacity: 60 * 60 * 10); // ~10 min at 60fps
    private readonly Dictionary<uint, StateSnapshot> _snapshots = new();
    
    // Metadata
    private ReplayHeader _header;
    private uint _currentTick;
    private bool _isRecording;
    private bool _isPlaying;
    
    // Playback
    private int _playbackIndex;
    private float _playbackSpeed = 1.0f;
    
    public ReplayManager(uint snapshotInterval = 300, int maxTicks = 60 * 60 * 30) // snapshots every 5s, max 30min
    {
        _snapshotInterval = snapshotInterval;
        _maxRecordingTicks = maxTicks;
    }
    
    /// <summary>Begin recording a new session.</summary>
    public void StartRecording(string levelId, uint seed, byte playerCount)
    {
        _inputFrames.Clear();
        _snapshots.Clear();
        _currentTick = 0;
        _isRecording = true;
        _isPlaying = false;
        
        _header = new ReplayHeader(
            Version: 1,
            GameVersion: GameConfig.Version,
            Seed: seed,
            TotalTicks: 0,
            PlayerCount: playerCount,
            StartTimestamp: DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            LevelId: levelId,
            Checksum: ""
        );
    }
    
    /// <summary>Record one tick's input. Call once per fixed update per player.</summary>
    public void RecordInput(InputFrame frame)
    {
        if (!_isRecording) return;
        if (_inputFrames.Count >= _maxRecordingTicks) return; // cap recording length
        
        _inputFrames.Add(frame with { Tick = _currentTick });
    }
    
    /// <summary>Record a state snapshot if it's time.</summary>
    public void RecordSnapshot(StateSnapshot snapshot)
    {
        if (!_isRecording) return;
        if (_snapshotInterval == 0) return;
        if (_currentTick % _snapshotInterval != 0) return;
        
        _snapshots[_currentTick] = snapshot;
    }
    
    /// <summary>Advance recording tick. Call once at end of fixed update.</summary>
    public void AdvanceTick()
    {
        if (_isRecording)
        {
            _currentTick++;
            _header = _header with { TotalTicks = _currentTick };
        }
    }
    
    /// <summary>Stop recording and finalize the replay data.</summary>
    public ReplayData StopRecording()
    {
        _isRecording = false;
        _header = _header with
        {
            TotalTicks = _currentTick,
            Checksum = ComputeChecksum()
        };
        
        return new ReplayData(
            Header: _header,
            InputFrames: _inputFrames.ToArray(),
            Snapshots: new Dictionary<uint, StateSnapshot>(_snapshots)
        );
    }
    
    private string ComputeChecksum()
    {
        // Hash header + all input frames for integrity verification
        using var sha = System.Security.Cryptography.SHA256.Create();
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        
        writer.Write(_header.Version);
        writer.Write(_header.GameVersion);
        writer.Write(_header.Seed);
        writer.Write(_header.TotalTicks);
        
        foreach (var frame in _inputFrames)
        {
            writer.Write(frame.Tick);
            writer.Write(frame.PlayerId);
            writer.Write((ushort)frame.Actions);
            writer.Write(frame.MoveX);
            writer.Write(frame.MoveY);
            writer.Write(frame.AimAngle);
        }
        
        var hash = sha.ComputeHash(ms.ToArray());
        return Convert.ToHexString(hash);
    }
}
```

---

## 3 — Input Recording

### The Principle

Input recording captures the minimum information needed to reproduce a game session: what the player pressed, on which tick. If the game simulation is deterministic (same inputs + same seed = same result), you can replay the entire game from just the inputs.

### Compact Input Encoding

Raw `InputFrame` is ~20 bytes per tick per player. At 60 fps, that's 72 KB/min per player. We can compress significantly with delta encoding:

```csharp
namespace MyGame.Replay;

/// <summary>
/// Delta-encoded input frame. Only records changes from the previous frame.
/// Typical size: 1-4 bytes per frame instead of 20.
/// </summary>
public readonly struct DeltaInputFrame
{
    // Bit layout of the flags byte:
    // bit 0: actions changed
    // bit 1: moveX changed
    // bit 2: moveY changed
    // bit 3: aimAngle changed
    // bit 4: tick gap > 1 (followed by uint16 gap)
    // bit 5-7: reserved
    public readonly byte Flags;
    
    // Only present if corresponding flag bit is set
    public readonly ushort? Actions;      // full action bitmask (only when changed)
    public readonly sbyte? MoveX;         // quantized -128..127 → -1..1
    public readonly sbyte? MoveY;
    public readonly ushort? AimAngle;     // quantized 0..65535 → 0..2π
    public readonly ushort? TickGap;      // ticks since last frame (if > 1)
    
    // Most frames where player holds steady = 1 byte (Flags = 0x00)
}

/// <summary>Encodes/decodes delta input streams.</summary>
public static class InputEncoder
{
    /// <summary>Quantize a float axis value (-1..1) to a signed byte.</summary>
    public static sbyte QuantizeAxis(float value) =>
        (sbyte)Math.Clamp(value * 127f, -128f, 127f);
    
    /// <summary>Dequantize a signed byte back to float axis value.</summary>
    public static float DequantizeAxis(sbyte quantized) =>
        quantized / 127f;
    
    /// <summary>Quantize angle (0..2π) to unsigned short.</summary>
    public static ushort QuantizeAngle(float radians) =>
        (ushort)(radians / MathF.Tau * 65535f);
    
    /// <summary>Dequantize unsigned short back to angle (0..2π).</summary>
    public static float DequantizeAngle(ushort quantized) =>
        quantized / 65535f * MathF.Tau;

    /// <summary>
    /// Encode a sequence of raw InputFrames into a compact byte stream.
    /// Returns the compressed buffer.
    /// </summary>
    public static byte[] Encode(ReadOnlySpan<InputFrame> frames)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        
        InputFrame prev = default;
        uint prevTick = 0;
        
        for (int i = 0; i < frames.Length; i++)
        {
            ref readonly var frame = ref frames[i];
            byte flags = 0;
            
            bool actionsChanged = frame.Actions != prev.Actions;
            bool moveXChanged = QuantizeAxis(frame.MoveX) != QuantizeAxis(prev.MoveX);
            bool moveYChanged = QuantizeAxis(frame.MoveY) != QuantizeAxis(prev.MoveY);
            bool aimChanged = QuantizeAngle(frame.AimAngle) != QuantizeAngle(prev.AimAngle);
            uint tickGap = frame.Tick - prevTick;
            bool hasGap = tickGap > 1 || i == 0;
            
            if (actionsChanged) flags |= 0x01;
            if (moveXChanged)   flags |= 0x02;
            if (moveYChanged)   flags |= 0x04;
            if (aimChanged)     flags |= 0x08;
            if (hasGap)         flags |= 0x10;
            
            writer.Write(flags);
            
            if (hasGap) writer.Write((ushort)Math.Min(tickGap, ushort.MaxValue));
            if (actionsChanged) writer.Write((ushort)frame.Actions);
            if (moveXChanged) writer.Write(QuantizeAxis(frame.MoveX));
            if (moveYChanged) writer.Write(QuantizeAxis(frame.MoveY));
            if (aimChanged) writer.Write(QuantizeAngle(frame.AimAngle));
            
            prev = frame;
            prevTick = frame.Tick;
        }
        
        return ms.ToArray();
    }
    
    /// <summary>Decode a compressed byte stream back into InputFrames.</summary>
    public static InputFrame[] Decode(byte[] data, byte playerId)
    {
        var frames = new List<InputFrame>();
        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms);
        
        InputFrame prev = default;
        uint currentTick = 0;
        
        while (ms.Position < ms.Length)
        {
            byte flags = reader.ReadByte();
            
            if ((flags & 0x10) != 0)
            {
                uint gap = reader.ReadUInt16();
                currentTick += gap;
            }
            else
            {
                currentTick++;
            }
            
            var actions = (flags & 0x01) != 0 ? (InputActions)reader.ReadUInt16() : prev.Actions;
            var moveX = (flags & 0x02) != 0 ? DequantizeAxis(reader.ReadSByte()) : prev.MoveX;
            var moveY = (flags & 0x04) != 0 ? DequantizeAxis(reader.ReadSByte()) : prev.MoveY;
            var aim = (flags & 0x08) != 0 ? DequantizeAngle(reader.ReadUInt16()) : prev.AimAngle;
            
            var frame = new InputFrame(currentTick, playerId, actions, moveX, moveY, aim);
            frames.Add(frame);
            prev = frame;
        }
        
        return frames.ToArray();
    }
}
```

### Compression Results

Typical compression ratios with delta encoding:

| Scenario | Raw Size | Delta Size | Ratio | Description |
|----------|----------|------------|-------|-------------|
| Idle player | 72 KB/min | ~3.6 KB/min | 20:1 | No input changes, just flags byte |
| Platformer movement | 72 KB/min | ~18 KB/min | 4:1 | Frequent axis changes, rare actions |
| Fighting game | 72 KB/min | ~24 KB/min | 3:1 | Frequent action + direction changes |
| Twin-stick shooter | 72 KB/min | ~30 KB/min | 2.4:1 | Constant aim angle rotation |
| Average | 72 KB/min | ~15 KB/min | ~5:1 | Typical mixed gameplay |

A 10-minute session compresses to ~150 KB before additional gzip/deflate. With gzip, expect another 30-50% reduction.

### Multi-Player Input Interleaving

For local multiplayer or netplay, interleave frames by tick:

```csharp
/// <summary>
/// Records input from multiple players, interleaved by tick.
/// Each tick stores one InputFrame per player.
/// </summary>
public sealed class MultiPlayerInputRecorder
{
    private readonly byte _playerCount;
    private readonly List<InputFrame>[] _perPlayerFrames;
    
    public MultiPlayerInputRecorder(byte playerCount)
    {
        _playerCount = playerCount;
        _perPlayerFrames = new List<InputFrame>[playerCount];
        for (int i = 0; i < playerCount; i++)
            _perPlayerFrames[i] = new List<InputFrame>(60 * 60 * 10);
    }
    
    public void RecordFrame(InputFrame frame)
    {
        if (frame.PlayerId >= _playerCount) return;
        _perPlayerFrames[frame.PlayerId].Add(frame);
    }
    
    /// <summary>
    /// Serialize all players' inputs into separate streams.
    /// Each stream is independently delta-encoded for best compression.
    /// </summary>
    public Dictionary<byte, byte[]> Encode()
    {
        var result = new Dictionary<byte, byte[]>();
        for (byte p = 0; p < _playerCount; p++)
        {
            var span = System.Runtime.InteropServices.CollectionsMarshal
                .AsSpan(_perPlayerFrames[p]);
            result[p] = InputEncoder.Encode(span);
        }
        return result;
    }
}
```

---

## 4 — Deterministic Simulation

### Why Determinism Matters

Input-based replay requires determinism: given the same inputs and initial state, the simulation must produce **identical** results every time. If a single floating-point operation diverges, the replay desyncs — enemies move differently, projectiles miss, and the replay becomes unwatchable.

### Sources of Non-Determinism

| Source | Problem | Fix |
|--------|---------|-----|
| `Random` class | Different instances produce different sequences | Single seeded `Random` per game session, stored in replay header |
| `Dictionary` iteration | Order is not guaranteed | Use `SortedDictionary` or `List` with explicit sort for simulation-relevant iteration |
| `HashSet` iteration | Order is not guaranteed | Same — use `SortedSet` or sorted arrays |
| Floating-point order | `(a + b) + c ≠ a + (b + c)` | Process entities in deterministic order (by entity ID or spawn order) |
| `DateTime.Now` | Different each run | Use tick count as the only time source in simulation |
| `Parallel.ForEach` | Non-deterministic scheduling | Single-threaded simulation loop (parallelize rendering only) |
| `async/await` | Non-deterministic continuation scheduling | No async in simulation — synchronous only |
| Entity creation order | ECS may assign different IDs between runs | Use a deterministic entity factory with monotonic IDs |
| `float` vs `double` | Precision differences across platforms | Use one type consistently; `float` for gameplay, `double` for accumulation |
| `MathF.Sin`/`Cos` | Platform-dependent implementations | Use lookup tables or fixed-point for critical paths |

### Deterministic Random

```csharp
namespace MyGame.Core;

/// <summary>
/// Deterministic PRNG for gameplay simulation. 
/// Uses xoshiro256** — fast, well-distributed, fully deterministic.
/// NEVER use System.Random in simulation code.
/// </summary>
public sealed class GameRandom
{
    private ulong _s0, _s1, _s2, _s3;
    
    public uint Seed { get; }
    
    public GameRandom(uint seed)
    {
        Seed = seed;
        // SplitMix64 seeding to expand 32-bit seed to 256-bit state
        ulong s = seed;
        _s0 = SplitMix64(ref s);
        _s1 = SplitMix64(ref s);
        _s2 = SplitMix64(ref s);
        _s3 = SplitMix64(ref s);
    }
    
    private static ulong SplitMix64(ref ulong state)
    {
        ulong z = state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
    
    private static ulong Rotl(ulong x, int k) => (x << k) | (x >> (64 - k));
    
    /// <summary>Generate next 64-bit random value.</summary>
    public ulong Next()
    {
        ulong result = Rotl(_s1 * 5, 7) * 9;
        ulong t = _s1 << 17;
        
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = Rotl(_s3, 45);
        
        return result;
    }
    
    /// <summary>Random int in [0, max).</summary>
    public int NextInt(int max) => (int)(Next() % (ulong)max);
    
    /// <summary>Random int in [min, max).</summary>
    public int NextInt(int min, int max) => min + NextInt(max - min);
    
    /// <summary>Random float in [0, 1).</summary>
    public float NextFloat() => (Next() >> 40) / (float)(1UL << 24);
    
    /// <summary>Random float in [min, max).</summary>
    public float NextFloat(float min, float max) => min + NextFloat() * (max - min);
    
    /// <summary>Random bool with given probability.</summary>
    public bool NextBool(float probability = 0.5f) => NextFloat() < probability;
    
    /// <summary>Fisher-Yates shuffle. Deterministic.</summary>
    public void Shuffle<T>(Span<T> span)
    {
        for (int i = span.Length - 1; i > 0; i--)
        {
            int j = NextInt(i + 1);
            (span[i], span[j]) = (span[j], span[i]);
        }
    }
    
    /// <summary>
    /// Save current PRNG state for snapshot. 
    /// Allows restoring exact random state when seeking in replay.
    /// </summary>
    public (ulong s0, ulong s1, ulong s2, ulong s3) SaveState() =>
        (_s0, _s1, _s2, _s3);
    
    /// <summary>Restore PRNG state from snapshot.</summary>
    public void RestoreState((ulong s0, ulong s1, ulong s2, ulong s3) state)
    {
        (_s0, _s1, _s2, _s3) = state;
    }
}
```

### Deterministic Entity Processing

```csharp
namespace MyGame.Replay;

/// <summary>
/// Ensures entities are always processed in the same order.
/// Arch ECS query iteration order is NOT guaranteed across runs.
/// Wrap simulation queries with this to ensure determinism.
/// </summary>
public static class DeterministicQuery
{
    // Reusable buffer to avoid allocation
    [ThreadStatic] private static List<Entity>? _entityBuffer;
    
    /// <summary>
    /// Query entities sorted by a deterministic key (e.g., spawn order component).
    /// Use this for ANY simulation query where processing order affects results.
    /// </summary>
    public static void ForEachSorted<TComp>(
        World world,
        QueryDescription query,
        Action<Entity, ref TComp> action) where TComp : struct
    {
        _entityBuffer ??= new List<Entity>(256);
        _entityBuffer.Clear();
        
        // Collect all matching entities
        world.Query(in query, (Entity entity) => _entityBuffer.Add(entity));
        
        // Sort by spawn order (stable, deterministic)
        _entityBuffer.Sort((a, b) =>
        {
            ref var orderA = ref world.Get<SpawnOrder>(a);
            ref var orderB = ref world.Get<SpawnOrder>(b);
            return orderA.Order.CompareTo(orderB.Order);
        });
        
        // Process in deterministic order
        foreach (var entity in _entityBuffer)
        {
            ref var comp = ref world.Get<TComp>(entity);
            action(entity, ref comp);
        }
    }
}

/// <summary>Monotonically increasing spawn counter. Assigned at entity creation.</summary>
public record struct SpawnOrder(uint Order);

/// <summary>Deterministic entity factory that assigns SpawnOrder.</summary>
public sealed class DeterministicEntityFactory
{
    private readonly World _world;
    private uint _nextOrder;
    
    public DeterministicEntityFactory(World world)
    {
        _world = world;
        _nextOrder = 0;
    }
    
    /// <summary>Create an entity with a deterministic spawn order.</summary>
    public Entity Create(params object[] components)
    {
        var entity = _world.Create(components);
        _world.Add(entity, new SpawnOrder(_nextOrder++));
        return entity;
    }
    
    /// <summary>Reset counter (call at start of each replay).</summary>
    public void Reset() => _nextOrder = 0;
}
```

### Fixed-Point Math for Critical Paths

When cross-platform determinism is essential (networked replays between PC and console), use fixed-point for physics:

```csharp
namespace MyGame.Math;

/// <summary>
/// Fixed-point number with 16.16 format (16 integer bits, 16 fractional bits).
/// Fully deterministic across all platforms — no floating-point variance.
/// Use for: physics position/velocity, damage calculation, cooldown timers.
/// Don't use for: rendering (convert to float at render time).
/// </summary>
public readonly struct Fixed : IEquatable<Fixed>, IComparable<Fixed>
{
    public const int FractionalBits = 16;
    public const int Scale = 1 << FractionalBits; // 65536
    
    public readonly int Raw;
    
    private Fixed(int raw) => Raw = raw;
    
    // Conversion
    public static Fixed FromInt(int value) => new(value * Scale);
    public static Fixed FromFloat(float value) => new((int)(value * Scale));
    public float ToFloat() => Raw / (float)Scale;
    public int ToInt() => Raw >> FractionalBits;
    
    // Arithmetic
    public static Fixed operator +(Fixed a, Fixed b) => new(a.Raw + b.Raw);
    public static Fixed operator -(Fixed a, Fixed b) => new(a.Raw - b.Raw);
    public static Fixed operator *(Fixed a, Fixed b) => new((int)(((long)a.Raw * b.Raw) >> FractionalBits));
    public static Fixed operator /(Fixed a, Fixed b) => new((int)(((long)a.Raw << FractionalBits) / b.Raw));
    public static Fixed operator -(Fixed a) => new(-a.Raw);
    
    // Comparison
    public static bool operator ==(Fixed a, Fixed b) => a.Raw == b.Raw;
    public static bool operator !=(Fixed a, Fixed b) => a.Raw != b.Raw;
    public static bool operator <(Fixed a, Fixed b) => a.Raw < b.Raw;
    public static bool operator >(Fixed a, Fixed b) => a.Raw > b.Raw;
    public static bool operator <=(Fixed a, Fixed b) => a.Raw <= b.Raw;
    public static bool operator >=(Fixed a, Fixed b) => a.Raw >= b.Raw;
    
    // Common values
    public static readonly Fixed Zero = new(0);
    public static readonly Fixed One = FromInt(1);
    public static readonly Fixed Half = new(Scale / 2);
    
    // Math
    public static Fixed Abs(Fixed value) => new(System.Math.Abs(value.Raw));
    public static Fixed Min(Fixed a, Fixed b) => a.Raw < b.Raw ? a : b;
    public static Fixed Max(Fixed a, Fixed b) => a.Raw > b.Raw ? a : b;
    public static Fixed Clamp(Fixed value, Fixed min, Fixed max) => Min(Max(value, min), max);
    
    // Lerp — deterministic
    public static Fixed Lerp(Fixed a, Fixed b, Fixed t) => a + (b - a) * t;
    
    // Approximate sqrt via Newton's method — deterministic
    public static Fixed Sqrt(Fixed value)
    {
        if (value.Raw <= 0) return Zero;
        long n = value.Raw;
        long x = n;
        long y = (x + 1) >> 1;
        while (y < x)
        {
            x = y;
            y = (x + ((n << FractionalBits) / x)) >> 1;
        }
        return new((int)x);
    }
    
    public bool Equals(Fixed other) => Raw == other.Raw;
    public override bool Equals(object? obj) => obj is Fixed other && Equals(other);
    public override int GetHashCode() => Raw;
    public int CompareTo(Fixed other) => Raw.CompareTo(other.Raw);
    public override string ToString() => ToFloat().ToString("F4");
}
```

---

## 5 — State Snapshot Recording

### When to Use State Snapshots

Use state snapshots when:

- Determinism is impractical (third-party physics engines, complex AI)
- Random-access seeking is required (scrub to any point in the replay)
- Recording needs to survive game updates (state is self-describing, inputs are not)
- Replay needs to show entities the camera wasn't looking at

### Snapshot Structure

```csharp
namespace MyGame.Replay;

/// <summary>
/// Complete game state at a single tick. Designed for efficient serialization.
/// Only includes visually-relevant data — skip internal bookkeeping.
/// </summary>
public sealed class StateSnapshot
{
    public uint Tick;
    public (ulong s0, ulong s1, ulong s2, ulong s3) RngState; // for hybrid replay
    
    // Entity states — parallel arrays for SoA layout (cache-friendly serialization)
    public int EntityCount;
    public uint[] EntityIds = Array.Empty<uint>();       // stable entity IDs (not ECS internal IDs)
    public ushort[] EntityTypes = Array.Empty<ushort>();  // type index into entity type registry
    public float[] PosX = Array.Empty<float>();
    public float[] PosY = Array.Empty<float>();
    public float[] Rotation = Array.Empty<float>();
    public float[] ScaleX = Array.Empty<float>();
    public float[] ScaleY = Array.Empty<float>();
    public byte[] Flags = Array.Empty<byte>();            // visible, flipped, etc.
    
    // Animation states
    public ushort[] AnimId = Array.Empty<ushort>();       // current animation index
    public float[] AnimProgress = Array.Empty<float>();   // 0..1 normalized progress
    
    // Health (only for damageable entities — sparse)
    public int HealthCount;
    public uint[] HealthEntityIds = Array.Empty<uint>();
    public float[] HealthCurrent = Array.Empty<float>();
    public float[] HealthMax = Array.Empty<float>();
    
    // Game events that happened this tick (damage numbers, particles, SFX triggers)
    public GameEvent[] Events = Array.Empty<GameEvent>();
}

/// <summary>Discrete game event for replay playback (visual/audio only).</summary>
public record struct GameEvent(
    GameEventType Type,
    float X, float Y,       // world position
    ushort Param1,           // type-specific (e.g., damage amount, particle type)
    byte Param2              // type-specific (e.g., element type, direction)
);

public enum GameEventType : byte
{
    DamageNumber,
    ParticleBurst,
    SoundEffect,
    ScreenShake,
    TextPopup,
    EntitySpawn,
    EntityDeath,
    ProjectileFire,
    Explosion,
    ItemPickup
}
```

### Snapshot Capture System

```csharp
namespace MyGame.Replay;

/// <summary>
/// ECS system that captures state snapshots at configured intervals.
/// Runs after all simulation systems, before rendering.
/// </summary>
public sealed class SnapshotCaptureSystem
{
    private readonly World _world;
    private readonly ReplayManager _replayManager;
    private readonly QueryDescription _renderableQuery;
    private readonly QueryDescription _healthQuery;
    
    // Reusable buffers — avoid allocation per snapshot
    private readonly List<uint> _entityIds = new(512);
    private readonly List<ushort> _entityTypes = new(512);
    private readonly List<float> _posX = new(512);
    private readonly List<float> _posY = new(512);
    private readonly List<float> _rotation = new(512);
    private readonly List<float> _scaleX = new(512);
    private readonly List<float> _scaleY = new(512);
    private readonly List<byte> _flags = new(512);
    private readonly List<ushort> _animId = new(512);
    private readonly List<float> _animProgress = new(512);
    
    public SnapshotCaptureSystem(World world, ReplayManager replayManager)
    {
        _world = world;
        _replayManager = replayManager;
        
        _renderableQuery = new QueryDescription()
            .WithAll<Position, SpriteRenderer, SpawnOrder>();
        
        _healthQuery = new QueryDescription()
            .WithAll<Health, SpawnOrder>();
    }
    
    public void Update(uint currentTick, GameRandom rng)
    {
        // Clear buffers
        _entityIds.Clear();
        _entityTypes.Clear();
        _posX.Clear();
        _posY.Clear();
        _rotation.Clear();
        _scaleX.Clear();
        _scaleY.Clear();
        _flags.Clear();
        _animId.Clear();
        _animProgress.Clear();
        
        // Capture renderable entities (sorted by spawn order for determinism)
        var entities = new List<Entity>();
        _world.Query(in _renderableQuery, (Entity e) => entities.Add(e));
        entities.Sort((a, b) =>
        {
            ref var orderA = ref _world.Get<SpawnOrder>(a);
            ref var orderB = ref _world.Get<SpawnOrder>(b);
            return orderA.Order.CompareTo(orderB.Order);
        });
        
        foreach (var entity in entities)
        {
            ref var pos = ref _world.Get<Position>(entity);
            ref var sprite = ref _world.Get<SpriteRenderer>(entity);
            ref var order = ref _world.Get<SpawnOrder>(entity);
            
            _entityIds.Add(order.Order); // use spawn order as stable ID
            _entityTypes.Add(sprite.TypeId);
            _posX.Add(pos.X);
            _posY.Add(pos.Y);
            _rotation.Add(_world.TryGet<Rotation>(entity, out var rot) ? rot.Angle : 0f);
            _scaleX.Add(_world.TryGet<Scale>(entity, out var scl) ? scl.X : 1f);
            _scaleY.Add(scl.Y); // if no Scale component, default 1f from above
            
            byte flags = 0;
            if (sprite.Visible) flags |= 0x01;
            if (sprite.FlipX) flags |= 0x02;
            if (sprite.FlipY) flags |= 0x04;
            _flags.Add(flags);
            
            if (_world.TryGet<AnimationState>(entity, out var anim))
            {
                _animId.Add(anim.CurrentAnimationId);
                _animProgress.Add(anim.NormalizedTime);
            }
            else
            {
                _animId.Add(0);
                _animProgress.Add(0f);
            }
        }
        
        // Capture health data (sparse — only entities with Health)
        var healthIds = new List<uint>();
        var healthCurrent = new List<float>();
        var healthMax = new List<float>();
        
        _world.Query(in _healthQuery, (Entity e, ref Health h, ref SpawnOrder o) =>
        {
            healthIds.Add(o.Order);
            healthCurrent.Add(h.Current);
            healthMax.Add(h.Max);
        });
        
        var snapshot = new StateSnapshot
        {
            Tick = currentTick,
            RngState = rng.SaveState(),
            EntityCount = _entityIds.Count,
            EntityIds = _entityIds.ToArray(),
            EntityTypes = _entityTypes.ToArray(),
            PosX = _posX.ToArray(),
            PosY = _posY.ToArray(),
            Rotation = _rotation.ToArray(),
            ScaleX = _scaleX.ToArray(),
            ScaleY = _scaleY.ToArray(),
            Flags = _flags.ToArray(),
            AnimId = _animId.ToArray(),
            AnimProgress = _animProgress.ToArray(),
            HealthCount = healthIds.Count,
            HealthEntityIds = healthIds.ToArray(),
            HealthCurrent = healthCurrent.ToArray(),
            HealthMax = healthMax.ToArray(),
            Events = CollectEvents() // see §11 for event collection
        };
        
        _replayManager.RecordSnapshot(snapshot);
    }
    
    private GameEvent[] CollectEvents()
    {
        // Query for one-frame event entities and convert
        var events = new List<GameEvent>();
        var eventQuery = new QueryDescription().WithAll<GameEventComponent>();
        
        _world.Query(in eventQuery, (ref GameEventComponent evt, ref Position pos) =>
        {
            events.Add(new GameEvent(
                evt.Type, pos.X, pos.Y, evt.Param1, evt.Param2
            ));
        });
        
        return events.ToArray();
    }
}
```

### Snapshot Compression

State snapshots are large. Use delta compression between snapshots:

```csharp
namespace MyGame.Replay;

/// <summary>
/// Delta-compresses state snapshots against a reference snapshot.
/// Only stores entities that changed since the reference.
/// Typical compression: 5-20× for slow-paced games, 2-4× for fast action.
/// </summary>
public static class SnapshotDeltaEncoder
{
    private const float PositionThreshold = 0.01f;  // ignore sub-pixel movement
    private const float RotationThreshold = 0.001f; // ~0.06 degrees
    
    public static DeltaSnapshot Encode(StateSnapshot current, StateSnapshot reference)
    {
        var changed = new List<int>();           // indices into current arrays
        var spawned = new List<int>();            // entities in current but not reference
        var despawned = new List<uint>();         // entity IDs in reference but not current
        
        // Build lookup from reference
        var refLookup = new Dictionary<uint, int>(reference.EntityCount);
        for (int i = 0; i < reference.EntityCount; i++)
            refLookup[reference.EntityIds[i]] = i;
        
        var curLookup = new HashSet<uint>(current.EntityCount);
        
        for (int i = 0; i < current.EntityCount; i++)
        {
            uint id = current.EntityIds[i];
            curLookup.Add(id);
            
            if (!refLookup.TryGetValue(id, out int refIdx))
            {
                spawned.Add(i); // new entity
                continue;
            }
            
            // Check if anything changed
            bool posChanged = MathF.Abs(current.PosX[i] - reference.PosX[refIdx]) > PositionThreshold
                           || MathF.Abs(current.PosY[i] - reference.PosY[refIdx]) > PositionThreshold;
            bool rotChanged = MathF.Abs(current.Rotation[i] - reference.Rotation[refIdx]) > RotationThreshold;
            bool animChanged = current.AnimId[i] != reference.AnimId[refIdx];
            bool flagsChanged = current.Flags[i] != reference.Flags[refIdx];
            
            if (posChanged || rotChanged || animChanged || flagsChanged)
                changed.Add(i);
        }
        
        // Find despawned entities
        for (int i = 0; i < reference.EntityCount; i++)
        {
            if (!curLookup.Contains(reference.EntityIds[i]))
                despawned.Add(reference.EntityIds[i]);
        }
        
        return new DeltaSnapshot
        {
            Tick = current.Tick,
            ReferenceTick = reference.Tick,
            ChangedIndices = changed.ToArray(),
            SpawnedIndices = spawned.ToArray(),
            DespawnedIds = despawned.ToArray(),
            FullSnapshot = current // keep reference to extract changed data during serialization
        };
    }
}

public sealed class DeltaSnapshot
{
    public uint Tick;
    public uint ReferenceTick;
    public int[] ChangedIndices = Array.Empty<int>();
    public int[] SpawnedIndices = Array.Empty<int>();
    public uint[] DespawnedIds = Array.Empty<uint>();
    public StateSnapshot FullSnapshot = null!; // backing data
}
```

---

## 6 — Hybrid Recording

### The Best of Both Worlds

Production replay systems combine input recording (small files, perfect fidelity) with periodic state snapshots (seeking, validation):

```
Timeline:    ──────────────────────────────────────────────►
             
Inputs:      ||||||||||||||||||||||||||||||||||||||||||||||||
             
Snapshots:   S         S         S         S         S
             ↑         ↑         ↑         ↑         ↑
             tick 0    tick 300  tick 600  tick 900  tick 1200
             
To seek to tick 450:
  1. Load snapshot at tick 300
  2. Restore game state
  3. Fast-forward simulation from tick 300→450 using recorded inputs
  4. Resume normal-speed playback at tick 450
```

### Hybrid Replay Manager

```csharp
namespace MyGame.Replay;

/// <summary>
/// Hybrid replay that combines input recording with periodic snapshots.
/// Supports: fast play/pause, variable speed, seeking, validation.
/// </summary>
public sealed class HybridReplayManager
{
    private readonly ReplayManager _inputRecorder;
    private readonly uint _snapshotInterval;
    
    // Sorted snapshot keyframes
    private readonly SortedDictionary<uint, StateSnapshot> _keyframes = new();
    
    // Playback state
    private uint _playbackTick;
    private float _playbackSpeed = 1.0f;
    private float _tickAccumulator;
    private bool _isPaused;
    
    public HybridReplayManager(uint snapshotInterval = 300)
    {
        _snapshotInterval = snapshotInterval;
        _inputRecorder = new ReplayManager(snapshotInterval);
    }
    
    /// <summary>Seek to a specific tick. Loads nearest snapshot, then fast-forwards.</summary>
    public SeekResult Seek(uint targetTick, World world, GameRandom rng)
    {
        // Find nearest snapshot at or before target
        uint nearestSnapshotTick = 0;
        StateSnapshot? snapshot = null;
        
        foreach (var kvp in _keyframes)
        {
            if (kvp.Key <= targetTick)
            {
                nearestSnapshotTick = kvp.Key;
                snapshot = kvp.Value;
            }
            else break;
        }
        
        if (snapshot == null)
            return new SeekResult(false, 0, "No snapshot available before target tick");
        
        // Restore game state from snapshot
        RestoreSnapshot(world, snapshot, rng);
        
        // Fast-forward from snapshot to target using recorded inputs
        uint ticksToSimulate = targetTick - nearestSnapshotTick;
        var inputFrames = _inputRecorder.GetFramesInRange(nearestSnapshotTick, targetTick);
        
        for (uint t = nearestSnapshotTick; t < targetTick; t++)
        {
            // Feed recorded inputs for this tick
            var tickInputs = GetInputsForTick(inputFrames, t);
            ApplyInputs(world, tickInputs);
            
            // Simulate one tick (no rendering)
            SimulateTick(world, rng);
        }
        
        _playbackTick = targetTick;
        
        return new SeekResult(true, ticksToSimulate, null);
    }
    
    /// <summary>Validate replay integrity at a snapshot point.</summary>
    public bool ValidateAtTick(uint tick, World world)
    {
        if (!_keyframes.TryGetValue(tick, out var expected))
            return true; // no snapshot to validate against
        
        var actual = CaptureCurrentState(world);
        return CompareSnapshots(expected, actual);
    }
    
    private void RestoreSnapshot(World world, StateSnapshot snapshot, GameRandom rng)
    {
        // Clear world and rebuild from snapshot
        // ... entity recreation logic from snapshot data
        rng.RestoreState(snapshot.RngState);
    }
    
    private static bool CompareSnapshots(StateSnapshot a, StateSnapshot b)
    {
        if (a.EntityCount != b.EntityCount) return false;
        
        for (int i = 0; i < a.EntityCount; i++)
        {
            if (a.EntityIds[i] != b.EntityIds[i]) return false;
            if (MathF.Abs(a.PosX[i] - b.PosX[i]) > 0.1f) return false;
            if (MathF.Abs(a.PosY[i] - b.PosY[i]) > 0.1f) return false;
        }
        
        return true;
    }
    
    // ... additional helper methods
    private InputFrame[] GetInputsForTick(InputFrame[] allFrames, uint tick) =>
        allFrames.Where(f => f.Tick == tick).ToArray();
    
    private void ApplyInputs(World world, InputFrame[] inputs) { /* feed to input system */ }
    private void SimulateTick(World world, GameRandom rng) { /* run one simulation step */ }
    private StateSnapshot CaptureCurrentState(World world) => new(); // capture logic from §5
}

public record struct SeekResult(bool Success, uint TicksSimulated, string? Error);
```

### Choosing Snapshot Interval

| Interval | Seek Latency (60fps) | Storage Overhead | Use Case |
|----------|---------------------|------------------|----------|
| 60 ticks (1s) | <1s fast-forward | ~5-10 KB/s | Fighting games, frame-precise seeking |
| 300 ticks (5s) | <5s fast-forward | ~1-2 KB/s | General purpose (recommended default) |
| 600 ticks (10s) | <10s fast-forward | ~0.5-1 KB/s | Strategy/RTS with slow-paced gameplay |
| 1800 ticks (30s) | <30s fast-forward | ~0.2 KB/s | Long sessions where file size matters |

Rule of thumb: **snapshot interval = maximum acceptable seek delay in ticks**. Players expect seeking to feel instant (<2 seconds), so 120-300 ticks is the sweet spot.

---

## 7 — Ghost Replay System

### Design

Ghost replays show a transparent recording of a previous attempt alongside the current player. Common in racing games, speedrun platformers, and time-trial modes.

```
┌───────────────────────────────────────────────┐
│                                               │
│        👻 Ghost (previous best)               │
│              ↗                                │
│            ↗                                  │
│     🏃 Player (current attempt)               │
│                                               │
│   ═══════════════════════════╗                │
│                              ║                │
│        Δt: +0.3s behind      ║                │
└───────────────────────────────────────────────┘
```

### Ghost Components

```csharp
namespace MyGame.Replay;

/// <summary>Tag for ghost entities. Ghosts are non-interactive visual copies.</summary>
public record struct Ghost(
    byte PlayerId,     // which player this ghost represents
    uint SourceTick    // current playback position in the ghost data
);

/// <summary>Loaded ghost replay data — one per ghost.</summary>
public record struct GhostData(
    GhostFrame[] Frames,
    float TotalTime,
    string Label         // "Personal Best", "World Record", "Friend: Alex"
);

/// <summary>Minimal per-frame ghost data. Keep small — many frames.</summary>
public readonly struct GhostFrame
{
    public readonly float X;
    public readonly float Y;
    public readonly ushort AnimId;
    public readonly byte Flags; // bit 0: flip X, bit 1: flip Y, bit 2: visible
    
    public GhostFrame(float x, float y, ushort animId, byte flags)
    {
        X = x; Y = y; AnimId = animId; Flags = flags;
    }
    
    // 11 bytes per frame = ~39.6 KB/min at 60fps
    // With delta compression: ~5-15 KB/min
}
```

### Ghost Recording System

```csharp
namespace MyGame.Replay;

/// <summary>
/// Records ghost data during gameplay. Captures position + animation each tick.
/// </summary>
public sealed class GhostRecorder
{
    private readonly List<GhostFrame> _frames = new(60 * 60 * 5); // ~5 min capacity
    private readonly byte _playerId;
    private bool _recording;
    
    public GhostRecorder(byte playerId)
    {
        _playerId = playerId;
    }
    
    public void StartRecording()
    {
        _frames.Clear();
        _recording = true;
    }
    
    public void RecordFrame(float x, float y, ushort animId, bool flipX, bool flipY, bool visible)
    {
        if (!_recording) return;
        
        byte flags = 0;
        if (flipX) flags |= 0x01;
        if (flipY) flags |= 0x02;
        if (visible) flags |= 0x04;
        
        _frames.Add(new GhostFrame(x, y, animId, flags));
    }
    
    public GhostData StopRecording(float totalTime, string label)
    {
        _recording = false;
        return new GhostData(_frames.ToArray(), totalTime, label);
    }
}
```

### Ghost Playback System

```csharp
namespace MyGame.Replay;

/// <summary>
/// Drives ghost entities each tick. Interpolates between recorded frames
/// for smooth display even at non-matching framerates.
/// </summary>
public sealed class GhostPlaybackSystem
{
    private readonly World _world;
    private readonly QueryDescription _ghostQuery;
    
    public GhostPlaybackSystem(World world)
    {
        _world = world;
        _ghostQuery = new QueryDescription().WithAll<Ghost, Position, SpriteRenderer>();
    }
    
    public void Update(float gameTime)
    {
        _world.Query(in _ghostQuery, (
            Entity entity,
            ref Ghost ghost,
            ref Position pos,
            ref SpriteRenderer sprite) =>
        {
            ref var data = ref _world.Get<GhostData>(entity);
            
            if (ghost.SourceTick >= data.Frames.Length - 1)
            {
                sprite.Visible = false; // ghost finished
                return;
            }
            
            // Get current and next frame for interpolation
            uint tick = ghost.SourceTick;
            ref readonly var current = ref data.Frames[tick];
            ref readonly var next = ref data.Frames[Math.Min(tick + 1, data.Frames.Length - 1)];
            
            // Fractional interpolation based on accumulated sub-tick time
            float t = 0f; // sub-tick fraction, updated by time control
            
            pos.X = MathHelper.Lerp(current.X, next.X, t);
            pos.Y = MathHelper.Lerp(current.Y, next.Y, t);
            
            sprite.FlipX = (current.Flags & 0x01) != 0;
            sprite.FlipY = (current.Flags & 0x02) != 0;
            sprite.Visible = (current.Flags & 0x04) != 0;
            
            // Apply ghost visual style
            sprite.Color = new Color(255, 255, 255, 100); // semi-transparent
            sprite.Tint = Color.CornflowerBlue;           // colored tint
            
            ghost = ghost with { SourceTick = tick + 1 };
        });
    }
}
```

### Ghost vs Player Time Delta Display

```csharp
namespace MyGame.Replay;

/// <summary>
/// Calculates and displays the time difference between player and ghost.
/// Shows "+0.3s" (behind) or "-0.5s" (ahead) relative to ghost position.
/// </summary>
public sealed class GhostTimeDelta
{
    /// <summary>
    /// Calculate time delta by comparing spatial progress.
    /// Uses distance along the track/level rather than simple position.
    /// </summary>
    public static float CalculateDelta(
        float playerProgress,    // 0..1 normalized level progress
        float ghostProgress,     // 0..1 from ghost's recorded progress
        float ghostTotalTime,    // total time of ghost run
        float playerElapsedTime) // current player elapsed time
    {
        // If ghost is at same progress point as player,
        // delta is: when did ghost reach this point vs when did player?
        
        // Estimate ghost's time at player's current progress
        // (linear interpolation — works for roughly constant-speed sections)
        float ghostTimeAtPlayerProgress = playerProgress * ghostTotalTime;
        
        return playerElapsedTime - ghostTimeAtPlayerProgress;
        // Positive = player is slower (behind), Negative = player is faster (ahead)
    }
    
    /// <summary>
    /// Checkpoint-based delta (more accurate for non-linear levels).
    /// Records timestamps at known checkpoint positions.
    /// </summary>
    public static float CalculateCheckpointDelta(
        int lastCheckpoint,
        float playerCheckpointTime,
        float[] ghostCheckpointTimes)
    {
        if (lastCheckpoint < 0 || lastCheckpoint >= ghostCheckpointTimes.Length)
            return 0f;
        
        return playerCheckpointTime - ghostCheckpointTimes[lastCheckpoint];
    }
}
```

### Ghost File Serialization

```csharp
namespace MyGame.Replay;

/// <summary>
/// Compact ghost file format.
/// Header (24 bytes) + delta-compressed frames.
/// </summary>
public static class GhostFileSerializer
{
    private const uint MagicNumber = 0x47485354; // "GHST"
    private const ushort FormatVersion = 1;
    
    public static void Save(GhostData ghost, string filePath)
    {
        using var fs = File.Create(filePath);
        using var bw = new BinaryWriter(fs);
        
        // Header
        bw.Write(MagicNumber);
        bw.Write(FormatVersion);
        bw.Write(ghost.TotalTime);
        bw.Write(ghost.Frames.Length);
        bw.Write(ghost.Label);
        
        // Delta-encode frames
        float prevX = 0, prevY = 0;
        ushort prevAnim = 0;
        byte prevFlags = 0;
        
        foreach (ref readonly var frame in ghost.Frames.AsSpan())
        {
            // Store deltas as half-precision where possible
            short dx = (short)MathF.Round((frame.X - prevX) * 100f); // 0.01 unit precision
            short dy = (short)MathF.Round((frame.Y - prevY) * 100f);
            
            byte changeMask = 0;
            if (dx != 0) changeMask |= 0x01;
            if (dy != 0) changeMask |= 0x02;
            if (frame.AnimId != prevAnim) changeMask |= 0x04;
            if (frame.Flags != prevFlags) changeMask |= 0x08;
            
            bw.Write(changeMask);
            if ((changeMask & 0x01) != 0) bw.Write(dx);
            if ((changeMask & 0x02) != 0) bw.Write(dy);
            if ((changeMask & 0x04) != 0) bw.Write(frame.AnimId);
            if ((changeMask & 0x08) != 0) bw.Write(frame.Flags);
            
            prevX = frame.X;
            prevY = frame.Y;
            prevAnim = frame.AnimId;
            prevFlags = frame.Flags;
        }
    }
    
    public static GhostData Load(string filePath)
    {
        using var fs = File.OpenRead(filePath);
        using var br = new BinaryReader(fs);
        
        // Header
        uint magic = br.ReadUInt32();
        if (magic != MagicNumber) throw new InvalidDataException("Not a ghost file");
        
        ushort version = br.ReadUInt16();
        if (version > FormatVersion) throw new InvalidDataException($"Unsupported ghost version {version}");
        
        float totalTime = br.ReadSingle();
        int frameCount = br.ReadInt32();
        string label = br.ReadString();
        
        // Delta-decode frames
        var frames = new GhostFrame[frameCount];
        float x = 0, y = 0;
        ushort anim = 0;
        byte flags = 0;
        
        for (int i = 0; i < frameCount; i++)
        {
            byte changeMask = br.ReadByte();
            
            if ((changeMask & 0x01) != 0) x += br.ReadInt16() / 100f;
            if ((changeMask & 0x02) != 0) y += br.ReadInt16() / 100f;
            if ((changeMask & 0x04) != 0) anim = br.ReadUInt16();
            if ((changeMask & 0x08) != 0) flags = br.ReadByte();
            
            frames[i] = new GhostFrame(x, y, anim, flags);
        }
        
        return new GhostData(frames, totalTime, label);
    }
}
```

---

## 8 — Killcam & Highlight System

### Design

Killcams capture a short rolling buffer of recent gameplay. On death (or a highlight-worthy event), the buffer is frozen and played back with cinematic camera control.

```
Normal Gameplay:
  ──────────[rolling 10s buffer]──────────►  DEATH
                                              │
                                              ▼
Killcam Playback:                      ◄──[last 5-10s]──►
  ► Slow-mo approach → Normal speed → Freeze on impact
```

### Rolling Buffer

```csharp
namespace MyGame.Replay;

/// <summary>
/// Fixed-size circular buffer of state snapshots for killcam.
/// Stores the last N seconds of gameplay at every-tick granularity.
/// Memory budget: ~500 KB for 10 seconds of ~50 entities at 60fps.
/// </summary>
public sealed class KillcamBuffer
{
    private readonly StateSnapshot[] _buffer;
    private readonly int _capacity;
    private int _writeIndex;
    private int _count;
    
    /// <param name="durationSeconds">How many seconds of history to keep.</param>
    /// <param name="tickRate">Game simulation tick rate (typically 60).</param>
    public KillcamBuffer(float durationSeconds = 10f, int tickRate = 60)
    {
        _capacity = (int)(durationSeconds * tickRate);
        _buffer = new StateSnapshot[_capacity];
        _writeIndex = 0;
        _count = 0;
    }
    
    /// <summary>Push a snapshot into the rolling buffer. O(1).</summary>
    public void Push(StateSnapshot snapshot)
    {
        _buffer[_writeIndex] = snapshot;
        _writeIndex = (_writeIndex + 1) % _capacity;
        if (_count < _capacity) _count++;
    }
    
    /// <summary>
    /// Freeze the buffer and return an ordered array for playback.
    /// Call this at the moment the killcam should trigger.
    /// </summary>
    public StateSnapshot[] Freeze(float lastNSeconds = 5f, int tickRate = 60)
    {
        int framesToCapture = Math.Min((int)(lastNSeconds * tickRate), _count);
        var result = new StateSnapshot[framesToCapture];
        
        // Read backwards from write position
        int readIndex = (_writeIndex - framesToCapture + _capacity) % _capacity;
        for (int i = 0; i < framesToCapture; i++)
        {
            result[i] = _buffer[(readIndex + i) % _capacity];
        }
        
        return result;
    }
    
    /// <summary>Clear the buffer (e.g., on level reset).</summary>
    public void Clear()
    {
        _writeIndex = 0;
        _count = 0;
    }
}
```

### Killcam Playback Controller

```csharp
namespace MyGame.Replay;

/// <summary>
/// Orchestrates killcam playback with cinematic timing.
/// Phases: lead-in (normal speed) → pre-kill slow-mo → impact freeze → outro.
/// </summary>
public sealed class KillcamController
{
    public enum Phase { LeadIn, SlowMo, Freeze, Outro, Complete }
    
    private StateSnapshot[] _frames = Array.Empty<StateSnapshot>();
    private int _currentFrame;
    private float _tickAccumulator;
    private Phase _phase;
    private float _phaseTimer;
    
    // Configuration
    private readonly KillcamConfig _config;
    
    public Phase CurrentPhase => _phase;
    public bool IsComplete => _phase == Phase.Complete;
    public float PlaybackSpeed { get; private set; }
    
    public KillcamController(KillcamConfig config)
    {
        _config = config;
    }
    
    /// <summary>Start killcam playback from frozen buffer data.</summary>
    public void Start(StateSnapshot[] frames, uint killFrame)
    {
        _frames = frames;
        _currentFrame = 0;
        _tickAccumulator = 0;
        _phase = Phase.LeadIn;
        _phaseTimer = 0;
        PlaybackSpeed = 1.0f;
        
        // Calculate phase boundaries (frame indices)
        int totalFrames = frames.Length;
        int killIndex = (int)(killFrame - frames[0].Tick);
        
        // Phases are relative to kill frame
        _config.SetFrameBoundaries(totalFrames, killIndex);
    }
    
    /// <summary>Advance playback. Returns the current snapshot to render.</summary>
    public StateSnapshot? Update(float deltaTime)
    {
        if (_phase == Phase.Complete || _frames.Length == 0)
            return null;
        
        _phaseTimer += deltaTime;
        
        // Update phase
        switch (_phase)
        {
            case Phase.LeadIn:
                PlaybackSpeed = 1.0f;
                if (_currentFrame >= _config.SlowMoStartFrame)
                {
                    _phase = Phase.SlowMo;
                    _phaseTimer = 0;
                }
                break;
                
            case Phase.SlowMo:
                // Ease into slow-motion
                float slowMoProgress = Math.Min(_phaseTimer / _config.SlowMoRampTime, 1f);
                PlaybackSpeed = MathHelper.Lerp(1.0f, _config.SlowMoSpeed, slowMoProgress);
                
                if (_currentFrame >= _config.FreezeFrame)
                {
                    _phase = Phase.Freeze;
                    _phaseTimer = 0;
                    PlaybackSpeed = 0f;
                }
                break;
                
            case Phase.Freeze:
                PlaybackSpeed = 0f;
                if (_phaseTimer >= _config.FreezeDuration)
                {
                    _phase = Phase.Outro;
                    _phaseTimer = 0;
                }
                break;
                
            case Phase.Outro:
                PlaybackSpeed = _config.OutroSpeed; // usually 1.5-2× for quick exit
                if (_currentFrame >= _frames.Length - 1 || _phaseTimer >= _config.OutroMaxDuration)
                {
                    _phase = Phase.Complete;
                    return null;
                }
                break;
        }
        
        // Advance frame counter based on playback speed
        if (PlaybackSpeed > 0)
        {
            _tickAccumulator += deltaTime * 60f * PlaybackSpeed; // assume 60 tick rate
            while (_tickAccumulator >= 1f && _currentFrame < _frames.Length - 1)
            {
                _currentFrame++;
                _tickAccumulator -= 1f;
            }
        }
        
        return _frames[Math.Min(_currentFrame, _frames.Length - 1)];
    }
    
    /// <summary>Skip killcam (player presses button).</summary>
    public void Skip() => _phase = Phase.Complete;
}

/// <summary>Killcam timing configuration.</summary>
public sealed class KillcamConfig
{
    public float SlowMoSpeed = 0.25f;       // 25% speed during slow-mo
    public float SlowMoRampTime = 0.3f;     // seconds to ease into slow-mo
    public float FreezeDuration = 1.5f;     // seconds to hold freeze frame
    public float OutroSpeed = 2.0f;         // speed for outro phase
    public float OutroMaxDuration = 2.0f;   // max outro length
    public float PreKillSlowMoTime = 1.0f;  // seconds before kill to start slow-mo
    
    // Computed frame boundaries (set by Start)
    internal int SlowMoStartFrame;
    internal int FreezeFrame;
    
    internal void SetFrameBoundaries(int totalFrames, int killIndex)
    {
        int slowMoFrames = (int)(PreKillSlowMoTime * 60f);
        SlowMoStartFrame = Math.Max(0, killIndex - slowMoFrames);
        FreezeFrame = killIndex;
    }
}
```

### Highlight Detection

Automatically detect highlight-worthy moments to save:

```csharp
namespace MyGame.Replay;

/// <summary>
/// Detects replay-worthy moments during gameplay.
/// Scores events and triggers auto-save when score exceeds threshold.
/// </summary>
public sealed class HighlightDetector
{
    private readonly float _threshold;
    private readonly KillcamBuffer _buffer;
    private readonly List<HighlightMoment> _highlights = new();
    
    // Scoring weights
    private const float MultiKillWeight = 30f;
    private const float ClutchKillWeight = 25f;  // kill while low HP
    private const float LongRangeWeight = 15f;
    private const float HeadshotWeight = 10f;
    private const float RevengeWeight = 12f;
    private const float FirstBloodWeight = 20f;
    private const float AceWeight = 50f;
    
    public HighlightDetector(KillcamBuffer buffer, float threshold = 40f)
    {
        _buffer = buffer;
        _threshold = threshold;
    }
    
    /// <summary>Score a kill event. If it exceeds the threshold, save a highlight.</summary>
    public void OnKill(KillEventData killData)
    {
        float score = 0;
        
        // Multi-kill bonus (kills within 4 seconds)
        if (killData.RecentKillCount >= 2)
            score += MultiKillWeight * (killData.RecentKillCount - 1);
        
        // Clutch: killed enemy while below 20% HP
        if (killData.KillerHealthRatio < 0.2f)
            score += ClutchKillWeight;
        
        // Long range
        if (killData.Distance > killData.LongRangeThreshold)
            score += LongRangeWeight;
        
        // Headshot / critical
        if (killData.WasCritical)
            score += HeadshotWeight;
        
        // Revenge: killed the player who last killed you
        if (killData.IsRevenge)
            score += RevengeWeight;
        
        // First blood
        if (killData.IsFirstBlood)
            score += FirstBloodWeight;
        
        // Ace: eliminated all enemies
        if (killData.IsAce)
            score += AceWeight;
        
        if (score >= _threshold)
        {
            var frames = _buffer.Freeze(lastNSeconds: 8f);
            _highlights.Add(new HighlightMoment(
                Score: score,
                Frames: frames,
                KillFrame: killData.Tick,
                Tags: DetermineTags(killData),
                Timestamp: DateTimeOffset.UtcNow
            ));
        }
    }
    
    /// <summary>Get the top N highlights from the match, sorted by score.</summary>
    public HighlightMoment[] GetTopHighlights(int count = 5)
    {
        return _highlights
            .OrderByDescending(h => h.Score)
            .Take(count)
            .ToArray();
    }
    
    private string[] DetermineTags(KillEventData data)
    {
        var tags = new List<string>();
        if (data.RecentKillCount >= 3) tags.Add("Multi-Kill");
        if (data.RecentKillCount >= 5) tags.Add("Rampage");
        if (data.KillerHealthRatio < 0.1f) tags.Add("Clutch");
        if (data.WasCritical) tags.Add("Critical");
        if (data.IsRevenge) tags.Add("Revenge");
        if (data.IsFirstBlood) tags.Add("First Blood");
        if (data.IsAce) tags.Add("Ace");
        return tags.ToArray();
    }
}

public record struct HighlightMoment(
    float Score,
    StateSnapshot[] Frames,
    uint KillFrame,
    string[] Tags,
    DateTimeOffset Timestamp
);

public record struct KillEventData(
    uint Tick,
    int RecentKillCount,
    float KillerHealthRatio,
    float Distance,
    float LongRangeThreshold,
    bool WasCritical,
    bool IsRevenge,
    bool IsFirstBlood,
    bool IsAce
);
```

---

## 9 — Spectator & Free Camera

### Spectator Mode Components

```csharp
namespace MyGame.Replay;

/// <summary>Spectator camera mode for replay viewing.</summary>
public enum SpectatorMode
{
    FollowPlayer,     // camera follows a specific entity
    FreeCam,          // WASD + mouse fly camera
    Cinematic,        // automated cinematic path
    Overhead,         // top-down view of entire arena
    PictureInPicture  // main + secondary view
}

/// <summary>Spectator state — singleton on the replay world.</summary>
public record struct SpectatorState(
    SpectatorMode Mode,
    uint FollowTargetId,     // entity to follow (FollowPlayer mode)
    int FollowTargetIndex,   // index into player list (for next/prev switching)
    float FreeCamX,
    float FreeCamY,
    float FreeCamZoom,
    bool ShowHUD,            // toggle HUD overlay
    bool ShowTrails           // show movement trails
);
```

### Free Camera Controller

```csharp
namespace MyGame.Replay;

/// <summary>
/// Free-fly camera for replay spectating.
/// WASD to move, mouse scroll to zoom, click to snap to nearest entity.
/// </summary>
public sealed class FreeCameraController
{
    private float _x, _y;
    private float _zoom = 1.0f;
    private float _targetZoom = 1.0f;
    private float _moveSpeed = 500f;
    
    // Zoom configuration
    private const float MinZoom = 0.25f;
    private const float MaxZoom = 4.0f;
    private const float ZoomStep = 0.15f;
    private const float ZoomSmoothSpeed = 8f;
    
    public void Update(float deltaTime, InputState input)
    {
        // Movement (WASD or arrow keys)
        float moveX = 0, moveY = 0;
        if (input.IsKeyDown(Keys.W) || input.IsKeyDown(Keys.Up))    moveY -= 1;
        if (input.IsKeyDown(Keys.S) || input.IsKeyDown(Keys.Down))  moveY += 1;
        if (input.IsKeyDown(Keys.A) || input.IsKeyDown(Keys.Left))  moveX -= 1;
        if (input.IsKeyDown(Keys.D) || input.IsKeyDown(Keys.Right)) moveX += 1;
        
        // Normalize diagonal movement
        float length = MathF.Sqrt(moveX * moveX + moveY * moveY);
        if (length > 0)
        {
            moveX /= length;
            moveY /= length;
        }
        
        // Speed scales with zoom (zoomed out = faster panning)
        float effectiveSpeed = _moveSpeed / _zoom;
        // Hold Shift for fast pan
        if (input.IsKeyDown(Keys.LeftShift)) effectiveSpeed *= 3f;
        
        _x += moveX * effectiveSpeed * deltaTime;
        _y += moveY * effectiveSpeed * deltaTime;
        
        // Zoom (mouse wheel)
        int scrollDelta = input.ScrollWheelDelta;
        if (scrollDelta != 0)
        {
            _targetZoom *= scrollDelta > 0 ? (1f + ZoomStep) : (1f - ZoomStep);
            _targetZoom = Math.Clamp(_targetZoom, MinZoom, MaxZoom);
        }
        
        // Smooth zoom interpolation
        _zoom = MathHelper.Lerp(_zoom, _targetZoom, ZoomSmoothSpeed * deltaTime);
    }
    
    /// <summary>Snap camera to world position (e.g., clicked entity).</summary>
    public void SnapTo(float x, float y)
    {
        _x = x;
        _y = y;
    }
    
    public Matrix GetViewMatrix(Viewport viewport) =>
        Matrix.CreateTranslation(-_x, -_y, 0) *
        Matrix.CreateScale(_zoom) *
        Matrix.CreateTranslation(viewport.Width * 0.5f, viewport.Height * 0.5f, 0);
}
```

### Spectator Mode Switcher

```csharp
namespace MyGame.Replay;

/// <summary>
/// Manages switching between spectator modes during replay.
/// Tab = next player, 1-4 = specific player, F = free cam, C = cinematic.
/// </summary>
public sealed class SpectatorModeManager
{
    private readonly World _world;
    private readonly FreeCameraController _freeCam;
    private readonly List<uint> _playerEntityIds = new();
    private SpectatorMode _mode = SpectatorMode.FollowPlayer;
    private int _followIndex;
    
    public SpectatorMode CurrentMode => _mode;
    public uint FollowTargetId => _playerEntityIds.Count > 0 
        ? _playerEntityIds[_followIndex % _playerEntityIds.Count] 
        : 0;
    
    public SpectatorModeManager(World world, FreeCameraController freeCam)
    {
        _world = world;
        _freeCam = freeCam;
    }
    
    /// <summary>Register player entities for follow mode.</summary>
    public void RegisterPlayers(IEnumerable<uint> playerIds)
    {
        _playerEntityIds.Clear();
        _playerEntityIds.AddRange(playerIds);
    }
    
    public void HandleInput(InputState input)
    {
        // Mode switching
        if (input.WasKeyPressed(Keys.F))
        {
            _mode = _mode == SpectatorMode.FreeCam 
                ? SpectatorMode.FollowPlayer 
                : SpectatorMode.FreeCam;
        }
        
        if (input.WasKeyPressed(Keys.C))
            _mode = SpectatorMode.Cinematic;
        
        if (input.WasKeyPressed(Keys.O))
            _mode = SpectatorMode.Overhead;
        
        // Next/previous player (Tab / Shift+Tab)
        if (_mode == SpectatorMode.FollowPlayer && _playerEntityIds.Count > 0)
        {
            if (input.WasKeyPressed(Keys.Tab))
            {
                _followIndex = input.IsKeyDown(Keys.LeftShift)
                    ? (_followIndex - 1 + _playerEntityIds.Count) % _playerEntityIds.Count
                    : (_followIndex + 1) % _playerEntityIds.Count;
            }
            
            // Direct player selection (1-4)
            for (int i = 0; i < Math.Min(4, _playerEntityIds.Count); i++)
            {
                if (input.WasKeyPressed(Keys.D1 + i))
                    _followIndex = i;
            }
        }
    }
    
    /// <summary>Get the camera position for the current mode.</summary>
    public (float x, float y, float zoom) GetCameraTarget(StateSnapshot snapshot)
    {
        return _mode switch
        {
            SpectatorMode.FollowPlayer => GetFollowTarget(snapshot),
            SpectatorMode.FreeCam => (_freeCam.X, _freeCam.Y, _freeCam.Zoom),
            SpectatorMode.Overhead => GetOverheadTarget(snapshot),
            SpectatorMode.Cinematic => GetCinematicTarget(snapshot),
            _ => (0, 0, 1f)
        };
    }
    
    private (float x, float y, float zoom) GetFollowTarget(StateSnapshot snapshot)
    {
        uint targetId = FollowTargetId;
        for (int i = 0; i < snapshot.EntityCount; i++)
        {
            if (snapshot.EntityIds[i] == targetId)
                return (snapshot.PosX[i], snapshot.PosY[i], 1.5f);
        }
        return (0, 0, 1f); // target not found
    }
    
    private (float x, float y, float zoom) GetOverheadTarget(StateSnapshot snapshot)
    {
        // Center on all entities with zoom to fit
        if (snapshot.EntityCount == 0) return (0, 0, 0.5f);
        
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        
        for (int i = 0; i < snapshot.EntityCount; i++)
        {
            minX = MathF.Min(minX, snapshot.PosX[i]);
            maxX = MathF.Max(maxX, snapshot.PosX[i]);
            minY = MathF.Min(minY, snapshot.PosY[i]);
            maxY = MathF.Max(maxY, snapshot.PosY[i]);
        }
        
        float centerX = (minX + maxX) * 0.5f;
        float centerY = (minY + maxY) * 0.5f;
        float span = MathF.Max(maxX - minX, maxY - minY);
        float zoom = Math.Clamp(800f / (span + 200f), 0.2f, 2f);
        
        return (centerX, centerY, zoom);
    }
    
    private (float x, float y, float zoom) GetCinematicTarget(StateSnapshot snapshot)
    {
        // Automated cinematic: slowly pan between action hotspots
        // For a full implementation, see the cinematic camera system in G20
        return GetOverheadTarget(snapshot); // fallback to overhead
    }
}
```

---

## 10 — Demo File Format

### File Structure

```
┌──────────────────────────────────────────────┐
│  DEMO FILE (.demo)                            │
├──────────────────────────────────────────────┤
│  Magic Number        4 bytes  ("DEMO")        │
│  Format Version      2 bytes  (uint16)        │
│  Header Size         4 bytes  (uint32)        │
├──────────────────────────────────────────────┤
│  HEADER SECTION                               │
│  ├─ Game Version     string (length-prefixed) │
│  ├─ Level ID         string                   │
│  ├─ RNG Seed         4 bytes (uint32)         │
│  ├─ Player Count     1 byte                   │
│  ├─ Tick Rate        2 bytes (uint16)         │
│  ├─ Total Ticks      4 bytes (uint32)         │
│  ├─ Total Duration   4 bytes (float, seconds) │
│  ├─ Start Timestamp  8 bytes (int64, UTC)     │
│  ├─ Player Info[]    variable                 │
│  │   ├─ Name         string                   │
│  │   ├─ Character    string                   │
│  │   └─ Team         1 byte                   │
│  └─ Checksum         32 bytes (SHA-256)       │
├──────────────────────────────────────────────┤
│  INPUT SECTION                                │
│  ├─ Section ID       4 bytes ("INPT")         │
│  ├─ Section Size     4 bytes                  │
│  ├─ Compression      1 byte (0=none, 1=gzip)  │
│  └─ Data             compressed input stream  │
│      └─ Per-player delta-encoded InputFrames  │
├──────────────────────────────────────────────┤
│  SNAPSHOT SECTION (optional)                  │
│  ├─ Section ID       4 bytes ("SNAP")         │
│  ├─ Section Size     4 bytes                  │
│  ├─ Snapshot Count   4 bytes                  │
│  ├─ Compression      1 byte                   │
│  └─ Snapshot Index[]                          │
│      ├─ Tick         4 bytes (uint32)         │
│      └─ Offset       4 bytes (relative to     │
│                               section start)  │
│  └─ Snapshot Data[]  compressed snapshots     │
├──────────────────────────────────────────────┤
│  METADATA SECTION (optional)                  │
│  ├─ Section ID       4 bytes ("META")         │
│  ├─ Section Size     4 bytes                  │
│  └─ JSON payload     UTF-8 string             │
│      ├─ score        match score/result       │
│      ├─ highlights   auto-detected highlights │
│      ├─ bookmarks    user-placed markers      │
│      └─ tags         searchable tags          │
├──────────────────────────────────────────────┤
│  EVENT SECTION (optional)                     │
│  ├─ Section ID       4 bytes ("EVNT")         │
│  ├─ Section Size     4 bytes                  │
│  └─ Events[]         game events by tick      │
└──────────────────────────────────────────────┘
```

### Demo File Reader/Writer

```csharp
namespace MyGame.Replay;

using System.IO.Compression;

/// <summary>
/// Reads and writes .demo replay files.
/// Section-based format — new sections can be added without breaking old readers.
/// </summary>
public static class DemoFile
{
    private static readonly byte[] Magic = "DEMO"u8.ToArray();
    private const ushort CurrentVersion = 2;
    
    public static void Write(string filePath, ReplayData replay)
    {
        using var fs = File.Create(filePath);
        using var bw = new BinaryWriter(fs);
        
        // File header
        bw.Write(Magic);
        bw.Write(CurrentVersion);
        
        // Reserve header size slot
        long headerSizePos = fs.Position;
        bw.Write((uint)0); // placeholder
        
        // Write header section
        long headerStart = fs.Position;
        WriteHeader(bw, replay.Header);
        uint headerSize = (uint)(fs.Position - headerStart);
        
        // Patch header size
        long currentPos = fs.Position;
        fs.Seek(headerSizePos, SeekOrigin.Begin);
        bw.Write(headerSize);
        fs.Seek(currentPos, SeekOrigin.Begin);
        
        // Write input section
        WriteSection(bw, "INPT"u8, writer =>
        {
            // Compress with GZip
            writer.Write((byte)1); // compression = gzip
            
            // Encode input data per-player
            using var ms = new MemoryStream();
            writer.Write(replay.Header.PlayerCount);
            
            for (byte p = 0; p < replay.Header.PlayerCount; p++)
            {
                var playerFrames = replay.InputFrames
                    .Where(f => f.PlayerId == p)
                    .ToArray();
                var encoded = InputEncoder.Encode(playerFrames);
                
                writer.Write(encoded.Length);
                writer.Write(encoded);
            }
        });
        
        // Write snapshot section (if any)
        if (replay.Snapshots.Count > 0)
        {
            WriteSection(bw, "SNAP"u8, writer =>
            {
                writer.Write(replay.Snapshots.Count);
                writer.Write((byte)1); // gzip compressed
                
                // Write snapshot index (tick → offset mapping)
                long dataStart = writer.BaseStream.Position + replay.Snapshots.Count * 8;
                foreach (var kvp in replay.Snapshots.OrderBy(s => s.Key))
                {
                    writer.Write(kvp.Key); // tick
                    writer.Write((uint)(dataStart - writer.BaseStream.Position)); // relative offset
                }
                
                // Write snapshot data (compressed)
                foreach (var kvp in replay.Snapshots.OrderBy(s => s.Key))
                {
                    WriteCompressedSnapshot(writer, kvp.Value);
                }
            });
        }
        
        // Write metadata section
        if (replay.Metadata != null)
        {
            WriteSection(bw, "META"u8, writer =>
            {
                var json = System.Text.Json.JsonSerializer.Serialize(replay.Metadata);
                writer.Write(json);
            });
        }
    }
    
    public static ReplayData Read(string filePath)
    {
        using var fs = File.OpenRead(filePath);
        using var br = new BinaryReader(fs);
        
        // Validate magic
        var magic = br.ReadBytes(4);
        if (!magic.AsSpan().SequenceEqual(Magic))
            throw new InvalidDataException("Not a demo file");
        
        ushort version = br.ReadUInt16();
        if (version > CurrentVersion)
            throw new InvalidDataException($"Demo version {version} not supported (max {CurrentVersion})");
        
        uint headerSize = br.ReadUInt32();
        var header = ReadHeader(br, version);
        
        // Read sections until EOF
        InputFrame[] inputs = Array.Empty<InputFrame>();
        var snapshots = new Dictionary<uint, StateSnapshot>();
        object? metadata = null;
        
        while (fs.Position < fs.Length)
        {
            var sectionId = new string(br.ReadChars(4));
            uint sectionSize = br.ReadUInt32();
            long sectionEnd = fs.Position + sectionSize;
            
            switch (sectionId)
            {
                case "INPT":
                    inputs = ReadInputSection(br, header.PlayerCount);
                    break;
                case "SNAP":
                    snapshots = ReadSnapshotSection(br);
                    break;
                case "META":
                    metadata = ReadMetadataSection(br);
                    break;
                default:
                    // Unknown section — skip it (forward compatibility)
                    fs.Seek(sectionEnd, SeekOrigin.Begin);
                    break;
            }
        }
        
        return new ReplayData(header, inputs, snapshots, metadata);
    }
    
    // Helper methods
    private static void WriteSection(BinaryWriter bw, ReadOnlySpan<byte> sectionId, Action<BinaryWriter> writeContent)
    {
        bw.Write(sectionId);
        long sizePos = bw.BaseStream.Position;
        bw.Write((uint)0); // placeholder
        
        long contentStart = bw.BaseStream.Position;
        writeContent(bw);
        uint contentSize = (uint)(bw.BaseStream.Position - contentStart);
        
        long currentPos = bw.BaseStream.Position;
        bw.BaseStream.Seek(sizePos, SeekOrigin.Begin);
        bw.Write(contentSize);
        bw.BaseStream.Seek(currentPos, SeekOrigin.Begin);
    }
    
    private static void WriteHeader(BinaryWriter bw, ReplayHeader header)
    {
        bw.Write(header.GameVersion);
        bw.Write(header.LevelId);
        bw.Write(header.Seed);
        bw.Write(header.PlayerCount);
        bw.Write((ushort)60); // tick rate
        bw.Write(header.TotalTicks);
        bw.Write(header.TotalTicks / 60f); // duration
        bw.Write(header.StartTimestamp);
        bw.Write(header.Checksum);
    }
    
    private static ReplayHeader ReadHeader(BinaryReader br, ushort version)
    {
        return new ReplayHeader(
            Version: version,
            GameVersion: br.ReadString(),
            Seed: (uint)(br.ReadString().GetHashCode()), // skip level ID read, get seed
            TotalTicks: br.ReadUInt32(),
            PlayerCount: br.ReadByte(),
            StartTimestamp: br.ReadInt64(),
            LevelId: "",
            Checksum: br.ReadString()
        );
    }
    
    private static InputFrame[] ReadInputSection(BinaryReader br, byte playerCount)
    {
        byte compression = br.ReadByte();
        byte storedPlayers = br.ReadByte();
        var allFrames = new List<InputFrame>();
        
        for (byte p = 0; p < storedPlayers; p++)
        {
            int encodedLength = br.ReadInt32();
            var encoded = br.ReadBytes(encodedLength);
            var decoded = InputEncoder.Decode(encoded, p);
            allFrames.AddRange(decoded);
        }
        
        // Sort by tick for interleaved playback
        allFrames.Sort((a, b) => a.Tick.CompareTo(b.Tick));
        return allFrames.ToArray();
    }
    
    private static Dictionary<uint, StateSnapshot> ReadSnapshotSection(BinaryReader br) => new(); // implementation follows snapshot decode
    private static object? ReadMetadataSection(BinaryReader br) => System.Text.Json.JsonSerializer.Deserialize<object>(br.ReadString());
    private static void WriteCompressedSnapshot(BinaryWriter bw, StateSnapshot snap) { /* serialize snapshot arrays */ }
}

/// <summary>Complete replay data container.</summary>
public sealed record ReplayData(
    ReplayHeader Header,
    InputFrame[] InputFrames,
    Dictionary<uint, StateSnapshot> Snapshots,
    object? Metadata = null
);
```

---

## 11 — Replay Playback Engine

### Playback Architecture

```csharp
namespace MyGame.Replay;

/// <summary>
/// Core replay playback engine. Drives the game world from recorded data
/// instead of live player input.
/// </summary>
public sealed class ReplayPlaybackEngine
{
    private readonly World _world;
    private readonly GameRandom _rng;
    
    // Replay data
    private ReplayData _replayData = null!;
    private int _inputCursor;
    
    // Playback control
    private uint _currentTick;
    private float _playbackSpeed = 1.0f;
    private float _tickAccumulator;
    private bool _isPaused;
    private PlaybackMode _mode = PlaybackMode.Play;
    
    // Entity mapping: recorded stable ID → live ECS entity
    private readonly Dictionary<uint, Entity> _entityMap = new();
    
    public uint CurrentTick => _currentTick;
    public uint TotalTicks => _replayData?.Header.TotalTicks ?? 0;
    public float PlaybackSpeed => _playbackSpeed;
    public bool IsPaused => _isPaused;
    public float ProgressNormalized => TotalTicks > 0 ? (float)_currentTick / TotalTicks : 0f;
    
    public ReplayPlaybackEngine(World world, GameRandom rng)
    {
        _world = world;
        _rng = rng;
    }
    
    /// <summary>Load a replay and prepare for playback.</summary>
    public void Load(ReplayData data)
    {
        _replayData = data;
        _currentTick = 0;
        _inputCursor = 0;
        _tickAccumulator = 0;
        _entityMap.Clear();
        
        // Restore initial RNG state
        _rng = new GameRandom(data.Header.Seed);
        
        // If first snapshot exists, restore initial world state
        if (data.Snapshots.TryGetValue(0, out var initialSnapshot))
        {
            RestoreFromSnapshot(initialSnapshot);
        }
    }
    
    /// <summary>
    /// Advance playback by deltaTime. Call once per frame (not per tick).
    /// Handles variable framerate by accumulating sub-tick time.
    /// </summary>
    public void Update(float deltaTime)
    {
        if (_isPaused || _replayData == null) return;
        if (_currentTick >= _replayData.Header.TotalTicks)
        {
            _mode = PlaybackMode.Finished;
            return;
        }
        
        // Handle rewind
        if (_mode == PlaybackMode.Rewind)
        {
            _tickAccumulator -= deltaTime * 60f * MathF.Abs(_playbackSpeed);
            while (_tickAccumulator <= -1f && _currentTick > 0)
            {
                _currentTick--;
                _tickAccumulator += 1f;
            }
            RestoreNearestSnapshot();
            return;
        }
        
        // Forward playback
        _tickAccumulator += deltaTime * 60f * _playbackSpeed;
        
        int ticksToProcess = (int)_tickAccumulator;
        _tickAccumulator -= ticksToProcess;
        
        // Cap ticks per frame to prevent spiral of death at high speed
        ticksToProcess = Math.Min(ticksToProcess, 10);
        
        for (int i = 0; i < ticksToProcess; i++)
        {
            if (_currentTick >= _replayData.Header.TotalTicks) break;
            
            // Feed recorded inputs for this tick
            FeedInputsForTick(_currentTick);
            
            // Simulate one tick
            SimulateTick();
            
            _currentTick++;
        }
    }
    
    private void FeedInputsForTick(uint tick)
    {
        // Advance input cursor to current tick
        while (_inputCursor < _replayData.InputFrames.Length 
               && _replayData.InputFrames[_inputCursor].Tick < tick)
        {
            _inputCursor++;
        }
        
        // Feed all inputs for this tick
        while (_inputCursor < _replayData.InputFrames.Length 
               && _replayData.InputFrames[_inputCursor].Tick == tick)
        {
            var frame = _replayData.InputFrames[_inputCursor];
            InjectInput(frame);
            _inputCursor++;
        }
    }
    
    private void InjectInput(InputFrame frame)
    {
        // Write the recorded input into the game's input system
        // so simulation processes it identically to live play
        var inputQuery = new QueryDescription().WithAll<PlayerInput>();
        _world.Query(in inputQuery, (ref PlayerInput input) =>
        {
            if (input.PlayerId == frame.PlayerId)
            {
                input.Actions = frame.Actions;
                input.MoveX = frame.MoveX;
                input.MoveY = frame.MoveY;
                input.AimAngle = frame.AimAngle;
            }
        });
    }
    
    private void SimulateTick()
    {
        // Run the same fixed-update systems as live gameplay.
        // The key insight: replay doesn't need special simulation code.
        // It uses the same game systems — it just feeds recorded inputs.
    }
    
    /// <summary>Restore world state from the nearest snapshot at or before currentTick.</summary>
    private void RestoreNearestSnapshot()
    {
        uint nearestTick = 0;
        StateSnapshot? snapshot = null;
        
        foreach (var kvp in _replayData.Snapshots)
        {
            if (kvp.Key <= _currentTick && kvp.Key >= nearestTick)
            {
                nearestTick = kvp.Key;
                snapshot = kvp.Value;
            }
        }
        
        if (snapshot != null)
        {
            RestoreFromSnapshot(snapshot);
            // Fast-forward from snapshot to current tick
            for (uint t = nearestTick; t < _currentTick; t++)
            {
                FeedInputsForTick(t);
                SimulateTick();
            }
        }
    }
    
    private void RestoreFromSnapshot(StateSnapshot snapshot)
    {
        // Clear existing entities (except camera/UI)
        var gameQuery = new QueryDescription().WithAll<SpawnOrder>();
        var toDestroy = new List<Entity>();
        _world.Query(in gameQuery, (Entity e) => toDestroy.Add(e));
        foreach (var e in toDestroy) _world.Destroy(e);
        
        _entityMap.Clear();
        
        // Recreate entities from snapshot
        for (int i = 0; i < snapshot.EntityCount; i++)
        {
            var entity = _world.Create(
                new Position(snapshot.PosX[i], snapshot.PosY[i]),
                new Rotation(snapshot.Rotation[i]),
                new Scale(snapshot.ScaleX[i], snapshot.ScaleY[i]),
                new SpriteRenderer { TypeId = snapshot.EntityTypes[i] },
                new SpawnOrder(snapshot.EntityIds[i])
            );
            
            _entityMap[snapshot.EntityIds[i]] = entity;
        }
        
        // Restore RNG state
        _rng.RestoreState(snapshot.RngState);
    }
}

public enum PlaybackMode
{
    Play,
    Rewind,
    Paused,
    Finished
}

/// <summary>Player input component — used by both live and replay systems.</summary>
public record struct PlayerInput(
    byte PlayerId,
    InputActions Actions,
    float MoveX,
    float MoveY,
    float AimAngle
);
```

### State-Based Playback (Non-Deterministic Games)

For games that don't guarantee determinism, play back directly from snapshots with interpolation:

```csharp
namespace MyGame.Replay;

/// <summary>
/// State-interpolation playback engine. Does NOT re-simulate the game.
/// Interpolates between recorded state snapshots for smooth rendering.
/// Use when: determinism is impossible or not worth the engineering cost.
/// </summary>
public sealed class StatePlaybackEngine
{
    private StateSnapshot[] _snapshots = Array.Empty<StateSnapshot>();
    private int _snapshotIndex;
    private float _interpolationT; // 0..1 between current and next snapshot
    
    // Entity mapping for interpolation
    private readonly Dictionary<uint, EntityRenderState> _renderStates = new();
    
    public void Load(StateSnapshot[] snapshots)
    {
        _snapshots = snapshots;
        _snapshotIndex = 0;
        _interpolationT = 0;
    }
    
    /// <summary>
    /// Get interpolated render state for all entities at the current playback time.
    /// </summary>
    public IReadOnlyDictionary<uint, EntityRenderState> Update(float deltaTime, float playbackSpeed)
    {
        if (_snapshots.Length < 2) return _renderStates;
        
        // Advance interpolation
        float ticksPerFrame = 60f * playbackSpeed;
        _interpolationT += deltaTime * ticksPerFrame / GetSnapshotGap();
        
        while (_interpolationT >= 1f && _snapshotIndex < _snapshots.Length - 2)
        {
            _snapshotIndex++;
            _interpolationT -= 1f;
        }
        
        // Clamp
        _interpolationT = Math.Clamp(_interpolationT, 0f, 1f);
        
        // Interpolate between snapshots
        var current = _snapshots[_snapshotIndex];
        var next = _snapshots[Math.Min(_snapshotIndex + 1, _snapshots.Length - 1)];
        
        _renderStates.Clear();
        InterpolateSnapshots(current, next, _interpolationT);
        
        return _renderStates;
    }
    
    private void InterpolateSnapshots(StateSnapshot a, StateSnapshot b, float t)
    {
        // Build lookup for snapshot B
        var bLookup = new Dictionary<uint, int>(b.EntityCount);
        for (int i = 0; i < b.EntityCount; i++)
            bLookup[b.EntityIds[i]] = i;
        
        for (int i = 0; i < a.EntityCount; i++)
        {
            uint id = a.EntityIds[i];
            
            if (bLookup.TryGetValue(id, out int bi))
            {
                // Entity exists in both — interpolate
                _renderStates[id] = new EntityRenderState
                {
                    X = MathHelper.Lerp(a.PosX[i], b.PosX[bi], t),
                    Y = MathHelper.Lerp(a.PosY[i], b.PosY[bi], t),
                    Rotation = MathHelper.Lerp(a.Rotation[i], b.Rotation[bi], t),
                    AnimId = t < 0.5f ? a.AnimId[i] : b.AnimId[bi], // snap animation at midpoint
                    AnimProgress = MathHelper.Lerp(a.AnimProgress[i], b.AnimProgress[bi], t),
                    Visible = true,
                    Alpha = 1f
                };
            }
            else
            {
                // Entity despawned — fade out
                _renderStates[id] = new EntityRenderState
                {
                    X = a.PosX[i],
                    Y = a.PosY[i],
                    Rotation = a.Rotation[i],
                    AnimId = a.AnimId[i],
                    Visible = true,
                    Alpha = 1f - t // fade out as we approach next snapshot
                };
            }
        }
        
        // Entities that spawn in B but not in A — fade in
        for (int i = 0; i < b.EntityCount; i++)
        {
            uint id = b.EntityIds[i];
            if (!_renderStates.ContainsKey(id))
            {
                _renderStates[id] = new EntityRenderState
                {
                    X = b.PosX[i],
                    Y = b.PosY[i],
                    Rotation = b.Rotation[i],
                    AnimId = b.AnimId[i],
                    Visible = true,
                    Alpha = t // fade in
                };
            }
        }
    }
    
    private float GetSnapshotGap()
    {
        if (_snapshotIndex >= _snapshots.Length - 1) return 1f;
        return _snapshots[_snapshotIndex + 1].Tick - _snapshots[_snapshotIndex].Tick;
    }
}

public struct EntityRenderState
{
    public float X, Y, Rotation;
    public ushort AnimId;
    public float AnimProgress;
    public bool Visible;
    public float Alpha;
}
```

---

## 12 — Time Control (Slow-Mo, Rewind, Seek)

### Time Control Manager

```csharp
namespace MyGame.Replay;

/// <summary>
/// Unified time control for replay playback.
/// Handles: play, pause, speed control, slow-mo, rewind, seek, frame-step.
/// </summary>
public sealed class TimeController
{
    private float _speed = 1.0f;
    private bool _isPaused;
    private PlaybackDirection _direction = PlaybackDirection.Forward;
    
    // Speed presets
    private static readonly float[] SpeedPresets = { 0.1f, 0.25f, 0.5f, 1.0f, 2.0f, 4.0f, 8.0f, 16.0f };
    private int _speedIndex = 3; // default 1.0x
    
    // Frame stepping
    private bool _stepRequested;
    private int _stepFrames;
    
    public float Speed => _speed;
    public bool IsPaused => _isPaused;
    public PlaybackDirection Direction => _direction;
    
    /// <summary>Toggle play/pause.</summary>
    public void TogglePause() => _isPaused = !_isPaused;
    
    /// <summary>Increase playback speed (cycle through presets).</summary>
    public void SpeedUp()
    {
        _speedIndex = Math.Min(_speedIndex + 1, SpeedPresets.Length - 1);
        _speed = SpeedPresets[_speedIndex];
    }
    
    /// <summary>Decrease playback speed.</summary>
    public void SlowDown()
    {
        _speedIndex = Math.Max(_speedIndex - 1, 0);
        _speed = SpeedPresets[_speedIndex];
    }
    
    /// <summary>Set exact speed (e.g., for slow-mo trigger).</summary>
    public void SetSpeed(float speed) => _speed = Math.Clamp(speed, 0.01f, 32f);
    
    /// <summary>Reset to normal speed.</summary>
    public void ResetSpeed()
    {
        _speedIndex = 3;
        _speed = 1.0f;
    }
    
    /// <summary>Toggle forward/rewind direction.</summary>
    public void ToggleDirection()
    {
        _direction = _direction == PlaybackDirection.Forward 
            ? PlaybackDirection.Backward 
            : PlaybackDirection.Forward;
    }
    
    /// <summary>Advance exactly N frames, then pause. For frame-by-frame analysis.</summary>
    public void StepFrames(int count = 1)
    {
        _stepRequested = true;
        _stepFrames = count;
        _isPaused = false; // briefly unpause to step
    }
    
    /// <summary>
    /// Calculate how many ticks to advance this frame.
    /// Handles fractional tick accumulation internally.
    /// </summary>
    private float _accumulator;
    
    public int GetTicksThisFrame(float deltaTime)
    {
        if (_isPaused && !_stepRequested) return 0;
        
        if (_stepRequested)
        {
            _stepRequested = false;
            _isPaused = true; // re-pause after step
            return _stepFrames;
        }
        
        float effectiveSpeed = _speed * (_direction == PlaybackDirection.Backward ? -1f : 1f);
        _accumulator += deltaTime * 60f * effectiveSpeed;
        
        int ticks = (int)_accumulator;
        _accumulator -= ticks;
        
        return Math.Clamp(ticks, -10, 10); // cap to prevent spiral
    }
    
    /// <summary>Handle keyboard shortcuts for time control.</summary>
    public void HandleInput(InputState input)
    {
        if (input.WasKeyPressed(Keys.Space)) TogglePause();
        if (input.WasKeyPressed(Keys.Right) && _isPaused) StepFrames(1);
        if (input.WasKeyPressed(Keys.Left) && _isPaused) StepFrames(-1);
        
        if (input.WasKeyPressed(Keys.OemPlus) || input.WasKeyPressed(Keys.Add)) SpeedUp();
        if (input.WasKeyPressed(Keys.OemMinus) || input.WasKeyPressed(Keys.Subtract)) SlowDown();
        
        if (input.WasKeyPressed(Keys.R)) ToggleDirection();
        if (input.WasKeyPressed(Keys.D0)) ResetSpeed();
    }
}

public enum PlaybackDirection { Forward, Backward }
```

### Seek Bar Integration

```csharp
namespace MyGame.Replay;

/// <summary>
/// Seek bar logic for replay timeline scrubbing.
/// Converts mouse position to tick, triggers snapshot-based seeking.
/// </summary>
public sealed class SeekBar
{
    private readonly Rectangle _bounds;
    private bool _isDragging;
    private float _dragProgress; // 0..1
    
    public event Action<uint>? OnSeek; // fires with target tick
    
    public SeekBar(Rectangle bounds)
    {
        _bounds = bounds;
    }
    
    public void Update(InputState input, uint currentTick, uint totalTicks)
    {
        var mouse = input.MousePosition;
        bool mouseOver = _bounds.Contains(mouse);
        
        if (mouseOver && input.WasMousePressed(MouseButton.Left))
        {
            _isDragging = true;
        }
        
        if (_isDragging)
        {
            _dragProgress = Math.Clamp(
                (float)(mouse.X - _bounds.X) / _bounds.Width,
                0f, 1f);
            
            if (input.WasMouseReleased(MouseButton.Left))
            {
                _isDragging = false;
                uint targetTick = (uint)(_dragProgress * totalTicks);
                OnSeek?.Invoke(targetTick);
            }
        }
    }
    
    /// <summary>Draw the seek bar with current position and snapshot markers.</summary>
    public void Draw(SpriteBatch batch, uint currentTick, uint totalTicks, 
                     IEnumerable<uint> snapshotTicks, IEnumerable<(uint tick, string label)> bookmarks)
    {
        float progress = totalTicks > 0 ? (float)currentTick / totalTicks : 0f;
        if (_isDragging) progress = _dragProgress;
        
        // Background bar
        batch.Draw(Pixel, _bounds, Color.Gray * 0.5f);
        
        // Filled progress
        var fillRect = new Rectangle(_bounds.X, _bounds.Y, 
            (int)(_bounds.Width * progress), _bounds.Height);
        batch.Draw(Pixel, fillRect, Color.CornflowerBlue);
        
        // Snapshot markers (small ticks on the bar)
        foreach (var snapTick in snapshotTicks)
        {
            float snapProgress = (float)snapTick / totalTicks;
            int x = _bounds.X + (int)(_bounds.Width * snapProgress);
            batch.Draw(Pixel, new Rectangle(x, _bounds.Y, 2, _bounds.Height), Color.White * 0.4f);
        }
        
        // Bookmark markers (larger, colored)
        foreach (var (tick, label) in bookmarks)
        {
            float bmProgress = (float)tick / totalTicks;
            int x = _bounds.X + (int)(_bounds.Width * bmProgress);
            batch.Draw(Pixel, new Rectangle(x - 2, _bounds.Y - 4, 4, _bounds.Height + 8), Color.Gold);
        }
        
        // Playback head
        int headX = _bounds.X + (int)(_bounds.Width * progress);
        batch.Draw(Pixel, new Rectangle(headX - 3, _bounds.Y - 6, 6, _bounds.Height + 12), Color.White);
    }
    
    private static Texture2D Pixel => ContentManager.Instance.Pixel; // 1x1 white texture
}
```

---

## 13 — Replay Validation & Anti-Cheat

### Checksum Validation

```csharp
namespace MyGame.Replay;

/// <summary>
/// Validates replay integrity and detects tampering.
/// Three levels: file checksum, snapshot validation, full re-simulation.
/// </summary>
public static class ReplayValidator
{
    /// <summary>
    /// Level 1: File integrity — verify the stored checksum matches the data.
    /// Fast (~1ms). Catches file corruption and naive edits.
    /// </summary>
    public static ValidationResult ValidateChecksum(ReplayData replay)
    {
        // Recompute checksum from input data
        using var sha = System.Security.Cryptography.SHA256.Create();
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        
        writer.Write(replay.Header.Version);
        writer.Write(replay.Header.GameVersion);
        writer.Write(replay.Header.Seed);
        writer.Write(replay.Header.TotalTicks);
        
        foreach (var frame in replay.InputFrames)
        {
            writer.Write(frame.Tick);
            writer.Write(frame.PlayerId);
            writer.Write((ushort)frame.Actions);
            writer.Write(frame.MoveX);
            writer.Write(frame.MoveY);
            writer.Write(frame.AimAngle);
        }
        
        var hash = sha.ComputeHash(ms.ToArray());
        var computed = Convert.ToHexString(hash);
        
        bool valid = computed == replay.Header.Checksum;
        return new ValidationResult(
            valid ? ValidationLevel.Passed : ValidationLevel.ChecksumFailed,
            valid ? "Checksum valid" : $"Checksum mismatch: expected {replay.Header.Checksum}, got {computed}"
        );
    }
    
    /// <summary>
    /// Level 2: Snapshot validation — re-simulate and compare against recorded snapshots.
    /// Moderate speed (~10-100ms per snapshot). Catches desync from game updates.
    /// </summary>
    public static ValidationResult ValidateSnapshots(ReplayData replay, World world, GameRandom rng)
    {
        // Start simulation from beginning
        rng = new GameRandom(replay.Header.Seed);
        int inputCursor = 0;
        
        foreach (var kvp in replay.Snapshots.OrderBy(s => s.Key))
        {
            uint targetTick = kvp.Key;
            var expectedSnapshot = kvp.Value;
            
            // Simulate up to this snapshot
            while (inputCursor < replay.InputFrames.Length 
                   && replay.InputFrames[inputCursor].Tick < targetTick)
            {
                // Feed input and tick (abbreviated)
                inputCursor++;
            }
            
            // Compare simulation state to recorded snapshot
            var actualSnapshot = CaptureState(world);
            var diff = CompareSnapshots(expectedSnapshot, actualSnapshot);
            
            if (diff.HasDifferences)
            {
                return new ValidationResult(
                    ValidationLevel.SnapshotMismatch,
                    $"Desync at tick {targetTick}: {diff