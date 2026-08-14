# G6 — Audio


> **Category:** Guide · **Related:** [R1 Library Stack](../reference/R1_library_stack.md) · [C1 Genre Reference](../../core/game-design/C1_genre_reference.md)

> MonoGame built-in audio and FMOD via FmodForFoxes — setup, integration, and advanced features.

---

### 1 MonoGame Built-in Audio

```csharp
// Loading
SoundEffect sfx = Content.Load<SoundEffect>("explosion");
Song bgm = Content.Load<Song>("menu_music");

// SoundEffect — short sounds, multiple simultaneous instances
sfx.Play(volume: 0.8f, pitch: 0f, pan: 0f); // fire-and-forget

// SoundEffectInstance — controllable playback
SoundEffectInstance instance = sfx.CreateInstance();
instance.Volume = 0.5f;
instance.IsLooped = true;
instance.Play();

// Song — streaming music (only one at a time)
MediaPlayer.Volume = 0.6f;
MediaPlayer.IsRepeating = true;
MediaPlayer.Play(bgm);
```

**Key limitations:** `SoundEffectInstance` pool is limited (~256 on most platforms). Song only supports one track. No bus mixing, no effects.

### 2 Audio Manager Pattern

```csharp
public class AudioManager
{
    public static AudioManager Instance { get; } = new();

    private float _masterVolume = 1f;
    private readonly Dictionary<string, float> _categoryVolumes = new()
    {
        ["music"] = 0.7f, ["sfx"] = 1f, ["ambient"] = 0.5f, ["ui"] = 0.8f
    };

    private readonly Dictionary<string, SoundEffect> _sounds = new();
    private readonly SoundPool _sfxPool = new(maxInstances: 32);

    public void LoadSound(string name, SoundEffect sfx) => _sounds[name] = sfx;

    public void SetCategoryVolume(string category, float vol)
    {
        _categoryVolumes[category] = MathHelper.Clamp(vol, 0, 1);
    }

    public float GetEffectiveVolume(string category) =>
        _masterVolume * _categoryVolumes.GetValueOrDefault(category, 1f);

    public void PlaySfx(string name, float pitch = 0f, float pan = 0f)
    {
        if (!_sounds.TryGetValue(name, out var sfx)) return;
        _sfxPool.Play(sfx, GetEffectiveVolume("sfx"), pitch, pan);
    }
}
```

### 3 Sound Pooling

```csharp
public class SoundPool
{
    private readonly SoundEffectInstance[] _pool;
    private int _cursor;

    public SoundPool(int maxInstances = 32)
    {
        _pool = new SoundEffectInstance[maxInstances];
    }

    public void Play(SoundEffect sfx, float volume, float pitch, float pan)
    {
        // Find a stopped slot or overwrite oldest
        int slot = -1;
        for (int i = 0; i < _pool.Length; i++)
        {
            int idx = (_cursor + i) % _pool.Length;
            if (_pool[idx] == null || _pool[idx].State == SoundState.Stopped)
            {
                slot = idx; break;
            }
        }
        if (slot == -1) // all playing — steal oldest
        {
            slot = _cursor;
            _pool[slot]?.Stop();
        }

        _pool[slot]?.Dispose();
        _pool[slot] = sfx.CreateInstance();
        _pool[slot].Volume = volume;
        _pool[slot].Pitch = pitch;
        _pool[slot].Pan = pan;
        _pool[slot].Play();
        _cursor = (slot + 1) % _pool.Length;
    }
}
```

### 4 Music Crossfading

```csharp
public class MusicManager
{
    private SoundEffectInstance _current, _next;
    private float _fadeTimer, _fadeDuration;
    private bool _fading;

    public void PlayTrack(SoundEffect track, float fadeDuration = 1.5f)
    {
        _next?.Dispose();
        _next = track.CreateInstance();
        _next.IsLooped = true;
        _next.Volume = 0f;
        _next.Play();
        _fadeDuration = fadeDuration;
        _fadeTimer = 0f;
        _fading = true;
    }

    public void Update(float dt, float categoryVolume)
    {
        if (!_fading) return;
        _fadeTimer += dt;
        float t = MathHelper.Clamp(_fadeTimer / _fadeDuration, 0, 1);

        if (_current != null) _current.Volume = (1f - t) * categoryVolume;
        _next.Volume = t * categoryVolume;

        if (t >= 1f)
        {
            _current?.Stop();
            _current?.Dispose();
            _current = _next;
            _next = null;
            _fading = false;
        }
    }
}
```

### 5 FMOD via FmodForFoxes

For professional audio, FMOD provides bus mixing, DSP effects, parameters, and Studio integration.

**Setup:**
1. Register at fmod.com, download FMOD Studio API (tested: v2.02.19)
2. Copy native libs (`fmod.dll` / `libfmod.so`) to project root, set "Copy to Output"
3. Install NuGet packages:

```bash
dotnet add package FmodForFoxes        # core API
dotnet add package FmodForFoxes.Desktop # platform bindings
```

**Initialization:**
```csharp
using FmodForFoxes;
using FmodForFoxes.Studio;

public class Game1 : Game
{
    protected override void Initialize()
    {
        // Initialize with path to FMOD native libs
        FmodManager.Init(new DesktopNativePlatform(), "Content/FMOD");
        base.Initialize();
    }

    protected override void Update(GameTime gameTime)
    {
        FmodManager.Update(); // must call every frame
        base.Update(gameTime);
    }

    protected override void Dispose(bool disposing)
    {
        FmodManager.Unload();
        base.Dispose(disposing);
    }
}
```

**Playing sounds with Studio events & parameters:**
```csharp
// Load a bank (created in FMOD Studio)
var masterBank = CoreSystem.LoadBank("Master.bank");
var stringsBank = CoreSystem.LoadBank("Master.strings.bank");

// Play an event
var footstepEvent = StudioSystem.GetEvent("event:/SFX/Footstep");
var instance = footstepEvent.CreateInstance();
instance.SetParameterByName("Surface", 2.0f); // e.g., 0=grass, 1=stone, 2=wood
instance.Start();

// Bus mixing
var sfxBus = StudioSystem.GetBus("bus:/SFX");
sfxBus.Volume = 0.8f;
sfxBus.Paused = false;

var musicBus = StudioSystem.GetBus("bus:/Music");
musicBus.Volume = 0.6f;
```

### 6 2D Spatial Audio

```csharp
public static class SpatialAudio2D
{
    public static (float volume, float pan) Calculate(
        Vector2 source, Vector2 listener, float maxDistance = 800f)
    {
        float dist = Vector2.Distance(source, listener);
        float volume = MathHelper.Clamp(1f - (dist / maxDistance), 0f, 1f);
        volume *= volume; // quadratic falloff sounds more natural

        float dx = source.X - listener.X;
        float pan = MathHelper.Clamp(dx / maxDistance, -1f, 1f);

        return (volume, pan);
    }
}

// Usage in Update:
var (vol, pan) = SpatialAudio2D.Calculate(enemy.Position, player.Position);
AudioManager.Instance.PlaySfx("enemy_growl", pan: pan, volume: vol);
```

### 7 Vertical Layering (Dynamic Music)

```csharp
public class LayeredMusicSystem
{
    private readonly SoundEffectInstance[] _layers;
    private readonly float[] _targetVolumes;

    public LayeredMusicSystem(params SoundEffect[] layers)
    {
        _layers = new SoundEffectInstance[layers.Length];
        _targetVolumes = new float[layers.Length];
        for (int i = 0; i < layers.Length; i++)
        {
            _layers[i] = layers[i].CreateInstance();
            _layers[i].IsLooped = true;
            _layers[i].Volume = i == 0 ? 1f : 0f; // only base audible
            _targetVolumes[i] = _layers[i].Volume;
        }
    }

    public void StartAll()
    {
        foreach (var l in _layers) l.Play(); // all play in sync
    }

    public void SetLayerVolume(int layer, float target) =>
        _targetVolumes[layer] = MathHelper.Clamp(target, 0, 1);

    public void Update(float dt, float lerpSpeed = 3f)
    {
        for (int i = 0; i < _layers.Length; i++)
            _layers[i].Volume = MathHelper.Lerp(
                _layers[i].Volume, _targetVolumes[i], lerpSpeed * dt);
    }
}

// Usage: layer 0=ambient, 1=percussion, 2=melody, 3=combat strings
// musicSystem.SetLayerVolume(3, inCombat ? 1f : 0f);
```

### 8 Sound Variation & Audio Feel

```csharp
public class SoundVariant
{
    private readonly SoundEffect[] _variants;
    private readonly Random _rng = new();
    private int _lastIndex = -1;

    public SoundVariant(params SoundEffect[] variants) => _variants = variants;

    public void Play(SoundPool pool, float volume, float pan = 0f)
    {
        // Avoid repeating the same sound
        int idx;
        do { idx = _rng.Next(_variants.Length); }
        while (idx == _lastIndex && _variants.Length > 1);
        _lastIndex = idx;

        // Randomize pitch slightly (±5%)
        float pitch = (_rng.NextSingle() - 0.5f) * 0.1f;
        pool.Play(_variants[idx], volume, pitch, pan);
    }
}

// Impact layering for game feel:
// Play multiple sounds simultaneously for a single game event
public void PlayImpact(Vector2 position)
{
    var (vol, pan) = SpatialAudio2D.Calculate(position, _listener);
    _transientSfx.Play(_pool, vol * 1.0f, pan);  // sharp attack (click/crack)
    _bodySfx.Play(_pool, vol * 0.8f, pan);        // weight/substance (thud)
    _sweetenerSfx.Play(_pool, vol * 0.4f, pan);   // character (shatter/ring)
    // Tail (reverb/decay) typically handled by DSP or a longer sample
}
```

---

