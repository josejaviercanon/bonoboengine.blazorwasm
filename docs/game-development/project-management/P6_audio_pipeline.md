# 10 — Audio Production Pipeline



> Audio is half the experience. A game with great art and no audio feels like a silent film nobody asked for.

Most solo devs treat audio as an afterthought — something to bolt on the week before release. That's a mistake. Audio is **game feel** (see [G30 — Game Feel Tooling](../../monogame-arch/guides/G30_game_feel_tooling.md)). The satisfying *thwack* of a hit, the little *ding* when you pick up a coin, the ambient hum of a dungeon — these are what make a game feel *alive*.

---

## Table of Contents

1. [Audio Overview for Game Dev](#1--audio-overview-for-game-dev)
2. [Sound Effect Creation](#2--sound-effect-creation)
3. [Sound Effect Sourcing](#3--sound-effect-sourcing)
4. [Music Workflow](#4--music-workflow)
5. [Audio Implementation in MonoGame](#5--audio-implementation-in-monogame)
6. [Audio Asset Organization](#6--audio-asset-organization)
7. [Mixing Guide](#7--mixing-guide)
8. [Sound Design by Genre](#8--sound-design-by-genre)
9. [Common Audio Mistakes](#9--common-audio-mistakes)
10. [Audio Polish Checklist](#10--audio-polish-checklist)

---

## 1 — Audio Overview for Game Dev

### The Three Pillars

Every game's audio sits on three pillars:

| Pillar | What It Is | Example |
|---|---|---|
| **SFX** | Short, reactive sounds triggered by gameplay | Jump, hit, coin pickup, menu click |
| **Music** | Background tracks that set mood and pace | Level theme, boss fight music, title screen |
| **Ambience** | Continuous environmental texture | Wind, rain, cave drips, crowd noise, forest birds |

Most beginners handle SFX, half-heartedly add music, and completely forget ambience. All three matter.

### Audio as Game Feel

Sound is the fastest feedback channel you have. A player *feels* a hit before they consciously process the animation. This is why:

- **Juice = visuals + audio + screen shake.** Remove any one leg and it falls flat.
- Screen shake without a sound effect feels broken.
- A sound effect without any visual response feels disconnected.
- Together, they create *impact*.

See [G30 — Game Feel Tooling](../../monogame-arch/guides/G30_game_feel_tooling.md) for how audio plugs into your game feel system.

### Budget Your Audio Time

A realistic breakdown for a solo dev:

| Phase | % of Audio Time |
|---|---|
| Gathering/creating SFX | 30% |
| Music (source or create) | 25% |
| Implementation & coding | 20% |
| Mixing & balancing | 15% |
| Polish & variation | 10% |

**Rule of thumb:** Budget 10–15% of total dev time for audio. If your game takes 6 months, that's 3–4 weeks on audio. Not a weekend. Weeks.

---

## 2 — Sound Effect Creation

### Procedural Generation Tools (Free, Fast)

These tools generate retro-style SFX from randomized parameters. Perfect for prototyping and pixel-art games.

| Tool | Platform | Cost | Best For |
|---|---|---|---|
| **[jsfxr](https://sfxr.me/)** | Browser | Free | Quick prototyping, retro SFX. No install needed. |
| **[sfxr](https://www.drpetter.se/project_sfxr.html)** | Windows/Mac/Linux | Free | Same as jsfxr but native app |
| **[bfxr](https://www.bfxr.net/)** | Browser/Desktop | Free | Extended sfxr with more waveforms and filters |
| **[ChipTone](https://sfbgames.itch.io/chiptone)** | Browser | Free | More control than sfxr, still procedural |

**Workflow:** Open jsfxr → click category (jump, hit, powerup, etc.) → randomize until you find something close → tweak parameters → export as WAV.

### Recording Real Sounds

Your phone is a surprisingly decent recording tool.

**What works well:**
- Footstep sounds (walk on different surfaces)
- Impact sounds (hit things together — carefully)
- UI clicks (tap a pen, click a lighter, snap fingers)
- Cloth/swoosh sounds (wave fabric near the mic)
- Nature ambience (go outside, record for 5 minutes)

**Tips for phone recording:**
1. Use a voice memo app or a dedicated recorder (Audio Recorder on Android, Voice Memos on iOS)
2. Record in a quiet room — background noise is the enemy
3. Get close to the source (6–12 inches)
4. Record at the highest quality setting available
5. Record multiple takes — you'll want options
6. Clap once at the start for easy alignment in editing

### Processing in Audacity (Free)

[Audacity](https://www.audacityteam.org/) is the workhorse for SFX editing. Every sound you make or record should go through this pipeline:

1. **Trim** — Cut silence from the start and end. Select → `Edit > Remove Special > Trim Audio`
2. **Normalize** — Even out volume. `Effect > Volume and Compression > Normalize` → set to -1.0 dB
3. **Noise Reduction** — For recorded sounds. Select a silent section → `Effect > Noise Removal and Repair > Noise Reduction` → Get Noise Profile → select all → apply
4. **Fade In/Out** — Prevent clicks. Select first/last 10–50ms → `Effect > Fading > Fade In/Out`
5. **EQ** — Shape the tone. `Effect > EQ and Filters > Filter Curve EQ`. Cut rumble below 80Hz for most SFX. Boost 2–5kHz for "presence"
6. **Compress** (optional) — Tighten dynamics. `Effect > Volume and Compression > Compressor`

### Layering Sounds

Great SFX are often 2–3 sounds combined:

- **Sword hit** = metal clang + whoosh + impact thud
- **Explosion** = low rumble + crackle + debris scatter
- **Magic spell** = synth tone + sparkle + whoosh

In Audacity: import each layer as a separate track, align them, adjust individual volumes, then `Tracks > Mix > Mix and Render`.

### Export Formats

| Format | Use Case | Why |
|---|---|---|
| **WAV** (16-bit, 44100 Hz) | Short SFX (< 3 seconds) | No compression artifacts, instant decode, small file size for short sounds |
| **OGG Vorbis** | Longer sounds, ambience, music | Compressed, ~10x smaller than WAV, good quality |
| **MP3** | Avoid in games | Licensing concerns (historically), gapping issues with loops |

**MonoGame note:** The content pipeline handles format conversion, but starting with WAV for SFX and OGG for music/ambience is the cleanest workflow.

---

## 3 — Sound Effect Sourcing

### Free SFX Libraries

| Source | License | Notes |
|---|---|---|
| **[Freesound.org](https://freesound.org)** | Varies (CC0, CC-BY, CC-BY-NC) | Massive library. Filter by license. Always check per-sound. |
| **[OpenGameArt.org](https://opengameart.org)** | CC0, CC-BY, GPL | Game-focused. Quality varies. Good for retro. |
| **[Sonniss GDC Bundles](https://sonniss.com/gameaudiogdc)** | Royalty-free | Professional quality. Released annually. Grab every year's bundle. |
| **[Kenney.nl](https://kenney.nl)** | CC0 | Small but high-quality game SFX packs |
| **[ZapSplat](https://www.zapsplat.com)** | Free with attribution / paid | Large library, decent search |

### License Types — What They Mean

| License | Can Use Commercially? | Must Credit? | Can Modify? |
|---|---|---|---|
| **CC0 (Public Domain)** | ✅ Yes | ❌ No | ✅ Yes |
| **CC-BY** | ✅ Yes | ✅ Yes (in credits) | ✅ Yes |
| **CC-BY-SA** | ✅ Yes | ✅ Yes | ✅ Yes (same license) |
| **CC-BY-NC** | ❌ Not for commercial | ✅ Yes | ✅ Yes |
| **Royalty-Free** | ✅ Yes (after purchase) | Usually no | Varies |

**Best practice:** Keep a `CREDITS.md` or `audio_licenses.txt` in your project root. Log every sourced sound with its origin and license. Future-you will thank present-you.

### Paid Libraries

| Source | Cost | Best For |
|---|---|---|
| **[Epidemic Sound](https://www.epidemicsound.com)** | Subscription (~$15/mo) | Music + SFX. Great search. |
| **[Artlist](https://artlist.io)** | Subscription (~$15/mo) | Music-focused. Clean, modern. |
| **[Soundsnap](https://www.soundsnap.com)** | Subscription | Professional SFX library |
| **[GameSounds.xyz](https://gamesounds.xyz)** | Free | Curated royalty-free game sounds |

### When to Make vs Source

| Situation | Make It | Source It |
|---|---|---|
| Core player actions (jump, attack) | ✅ Custom feels better | Okay for prototype |
| UI sounds (click, hover, confirm) | ✅ Quick with sfxr | ✅ Also fine to source |
| Ambient loops | Source is faster | ✅ Better variety |
| Music | Only if you're a musician | ✅ Commission or license |
| Unique/signature sounds | ✅ Worth the effort | Won't find your exact vision |

---

## 4 — Music Workflow

### Options for Solo Devs

Be honest with yourself about your music skills. There's no shame in any of these approaches:

**Option 1: Commission Music** (~$50–500 per track)
- Best results if you can afford it
- Find composers on [Fiverr](https://www.fiverr.com), [itch.io](https://itch.io), or game dev Discord servers
- Provide references (link to tracks that match your vision)
- Budget for 3–8 tracks minimum (title, main gameplay, boss, game over, menu)

**Option 2: Royalty-Free Music**
- Fastest option. Search, download, done.
- Risk: other games use the same tracks
- Sources: Epidemic Sound, Artlist, [Incompetech](https://incompetech.com) (Kevin MacLeod, CC-BY)

**Option 3: Learn Basic Music Production**
- Longest time investment, most rewarding
- Start with chip-tune tools (lower barrier to entry)
- Even basic original music beats generic stock music for game identity

### Music Production Tools

| Tool | Cost | Platform | Best For |
|---|---|---|---|
| **[LMMS](https://lmms.io/)** | Free | Win/Mac/Linux | Full DAW, steep learning curve |
| **GarageBand** | Free | macOS/iOS | Beginner-friendly, surprisingly capable |
| **[FL Studio](https://www.image-line.com/)** | $99+ | Win/Mac | Industry standard for indie |
| **[Reaper](https://www.reaper.fm/)** | $60 (discount license) | Win/Mac/Linux | Lightweight, powerful, cheap |

### Chip-Tune Tools (Great for Pixel Art Games)

| Tool | Cost | Notes |
|---|---|---|
| **[BeepBox](https://www.beepbox.co/)** | Free (browser) | Instant gratification. Great for learning. |
| **[Bosca Ceoil](https://terrycavanagh.itch.io/bosca-ceoil)** | Free | Made by the VVVVVV dev. Simple, fun. |
| **[FamiTracker](http://famitracker.com/)** | Free (Windows) | Authentic NES sound. Tracker-style interface. |
| **[FamiStudio](https://famistudio.org/)** | Free | Modern FamiTracker alternative. More intuitive. |

### Making Seamless Loops

Most game music needs to loop. Here's how to make it seamless:

1. **Compose with the loop in mind.** The last bar should flow naturally into the first bar.
2. **Same key, same chord.** End on the same chord (or its dominant) that the track starts on.
3. **Crossfade test:** In Audacity, duplicate the track, offset the copy, and crossfade the overlap. Listen for bumps.
4. **Cut on zero crossings.** Zoom in on the waveform at the loop point. Cut where the wave crosses the center line (zero amplitude). This prevents clicks.
5. **Loop length:** 30 seconds minimum for background music. 1–2 minutes is ideal. Shorter loops become annoying fast.

**Quick loop test:** Set the track to loop in your audio player. Listen for 5 minutes. If you notice the seam, fix it.

### Adaptive / Dynamic Music (Advanced)

For more polish, consider music that responds to gameplay:

- **Horizontal re-sequencing:** Play different sections based on game state (exploration → combat → victory)
- **Vertical layering:** Stack instrument tracks. Add/remove layers based on intensity (e.g., add drums when enemies appear)
- **Stinger system:** Play short musical hits over the base track for events (level up, boss entrance, death)

Implementation: Use multiple `SoundEffect` instances for layers, or swap `Song` tracks with crossfades. Start simple — even just having a "calm" and "intense" version of each track goes a long way.

---

## 5 — Audio Implementation in MonoGame

> For the full API reference, see [G6 — Audio](../../monogame-arch/guides/G6_audio.md).

### SoundEffect vs Song

MonoGame gives you two main audio classes. Use the right one:

| Class | Use For | Loaded Into | Simultaneous Instances |
|---|---|---|---|
| `SoundEffect` | Short SFX, UI sounds, stingers | Memory (fully decoded) | Many (via `CreateInstance()`) |
| `Song` | Background music, long ambience | Streamed from disk | One at a time (via `MediaPlayer`) |

**Rule of thumb:** If it's under 5 seconds, use `SoundEffect`. If it's a music track or long ambient loop, use `Song`.

### Basic Playback

```csharp
// Load in LoadContent()
SoundEffect jumpSound = Content.Load<SoundEffect>("Audio/SFX/sfx_player_jump");
Song levelMusic = Content.Load<Song>("Audio/Music/mus_level1_loop");

// Play SFX (fire-and-forget)
jumpSound.Play(volume: 0.8f, pitch: 0f, pan: 0f);

// Play SFX (with control)
SoundEffectInstance jumpInstance = jumpSound.CreateInstance();
jumpInstance.Volume = 0.8f;
jumpInstance.IsLooped = false;
jumpInstance.Play();

// Play music
MediaPlayer.Volume = 0.5f;
MediaPlayer.IsRepeating = true;
MediaPlayer.Play(levelMusic);
```

### Audio Manager Pattern

Don't scatter `Play()` calls throughout your code. Centralize audio behind a manager:

```csharp
public static class AudioManager
{
    private static Dictionary<string, SoundEffect> _sounds = new();
    private static float _masterVolume = 1.0f;
    private static float _sfxVolume = 0.8f;
    private static float _musicVolume = 0.5f;
    private static float _ambienceVolume = 0.6f;
    
    private static Random _random = new();
    
    public static void LoadSound(string name, SoundEffect sound)
    {
        _sounds[name] = sound;
    }
    
    // Play with volume category applied
    public static void PlaySFX(string name, float volume = 1f, float pitch = 0f, float pan = 0f)
    {
        if (_sounds.TryGetValue(name, out var sound))
        {
            float finalVolume = volume * _sfxVolume * _masterVolume;
            sound.Play(finalVolume, pitch, pan);
        }
    }
    
    // Play with random pitch variation (prevents repetition fatigue)
    public static void PlaySFXVaried(string name, float volume = 1f, float pitchRange = 0.1f)
    {
        float pitch = (float)(_random.NextDouble() * 2 - 1) * pitchRange;
        PlaySFX(name, volume, pitch);
    }
    
    // Play a random sound from a pool
    public static void PlaySFXRandom(string[] names, float volume = 1f)
    {
        string name = names[_random.Next(names.Length)];
        PlaySFX(name, volume);
    }
    
    public static void PlayMusic(Song song)
    {
        MediaPlayer.Volume = _musicVolume * _masterVolume;
        MediaPlayer.IsRepeating = true;
        MediaPlayer.Play(song);
    }
    
    public static void SetMasterVolume(float volume)
    {
        _masterVolume = MathHelper.Clamp(volume, 0f, 1f);
        MediaPlayer.Volume = _musicVolume * _masterVolume;
    }
    
    public static void SetSFXVolume(float volume) =>
        _sfxVolume = MathHelper.Clamp(volume, 0f, 1f);
    
    public static void SetMusicVolume(float volume)
    {
        _musicVolume = MathHelper.Clamp(volume, 0f, 1f);
        MediaPlayer.Volume = _musicVolume * _masterVolume;
    }
}
```

### Positional Audio for 2D

MonoGame's `pan` parameter (-1.0 left to 1.0 right) gives you basic spatial audio:

```csharp
public static void PlaySFXPositional(string name, Vector2 soundPos, Vector2 listenerPos, 
    float maxDistance = 500f, float volume = 1f)
{
    float dx = soundPos.X - listenerPos.X;
    float distance = Vector2.Distance(soundPos, listenerPos);
    
    // Attenuate by distance
    float distanceFactor = 1f - MathHelper.Clamp(distance / maxDistance, 0f, 1f);
    
    // Pan based on horizontal offset
    float pan = MathHelper.Clamp(dx / (maxDistance * 0.5f), -1f, 1f);
    
    PlaySFX(name, volume * distanceFactor, 0f, pan);
}
```

### Limiting Simultaneous Sounds

Too many sounds at once = audio mud. Limit concurrent instances:

```csharp
private static Dictionary<string, int> _activeCounts = new();
private const int MAX_CONCURRENT = 3;

public static void PlaySFXLimited(string name, float volume = 1f)
{
    _activeCounts.TryGetValue(name, out int count);
    if (count >= MAX_CONCURRENT) return;
    
    var instance = _sounds[name].CreateInstance();
    instance.Volume = volume * _sfxVolume * _masterVolume;
    _activeCounts[name] = count + 1;
    instance.Play();
    
    // You'll need a system to decrement when the sound finishes
    // (check instance.State in Update, or use a timer)
}
```

---

## 6 — Audio Asset Organization

### Folder Structure

```
Content/
└── Audio/
    ├── SFX/
    │   ├── Player/
    │   │   ├── sfx_player_jump.wav
    │   │   ├── sfx_player_land.wav
    │   │   ├── sfx_player_hit_01.wav
    │   │   ├── sfx_player_hit_02.wav
    │   │   ├── sfx_player_hit_03.wav
    │   │   └── sfx_player_death.wav
    │   ├── Enemy/
    │   │   ├── sfx_enemy_hit.wav
    │   │   └── sfx_enemy_death.wav
    │   ├── UI/
    │   │   ├── sfx_ui_click.wav
    │   │   ├── sfx_ui_hover.wav
    │   │   ├── sfx_ui_confirm.wav
    │   │   └── sfx_ui_cancel.wav
    │   └── World/
    │       ├── sfx_door_open.wav
    │       ├── sfx_chest_open.wav
    │       └── sfx_coin_pickup.wav
    ├── Music/
    │   ├── mus_title_screen.ogg
    │   ├── mus_level1_loop.ogg
    │   ├── mus_level2_loop.ogg
    │   ├── mus_boss_loop.ogg
    │   └── mus_game_over.ogg
    └── Ambience/
        ├── amb_forest.ogg
        ├── amb_dungeon.ogg
        ├── amb_rain.ogg
        └── amb_wind.ogg
```

### Naming Conventions

| Prefix | Category | Example |
|---|---|---|
| `sfx_` | Sound effects | `sfx_player_jump.wav` |
| `mus_` | Music | `mus_boss_loop.ogg` |
| `amb_` | Ambience | `amb_forest.ogg` |
| `ui_` | UI sounds (or `sfx_ui_`) | `sfx_ui_click.wav` |

**Rules:**
- All lowercase, underscores for spaces
- Category prefix first, then subject, then action
- Number variants with `_01`, `_02`, `_03` (not `_1`, `_2` — keeps sorting clean)
- Add `_loop` suffix for looping sounds

### Content Pipeline Setup

In your `.mgcb` file (via the MonoGame Content Pipeline tool):

- **WAV files:** Processor = Sound Effect, Quality = Best
- **OGG files:** Processor = Song (for music) or Sound Effect (for short ambient)
- Set the build action to compile, not copy, so MonoGame handles format conversion per platform

---

## 7 — Mixing Guide

### Volume Levels by Category

These are starting points. Trust your ears, but start here:

| Category | Volume (0–1 scale) | dB Equivalent | Notes |
|---|---|---|---|
| **Master** | 1.0 | 0 dB | User-controlled |
| **SFX** | 0.7–0.8 | -3 to -2 dB | Loudest category — player needs to hear feedback |
| **Music** | 0.4–0.5 | -8 to -6 dB | Supports, never dominates |
| **Ambience** | 0.3–0.5 | -10 to -6 dB | Felt, not consciously heard |
| **UI** | 0.5–0.6 | -6 to -4 dB | Crisp but not jarring |

### The -6dB Rule

**Never let your final mix peak above -6dB.** This gives you headroom for:
- Multiple sounds playing simultaneously
- Different speaker/headphone volumes
- Platform differences

In Audacity, normalize your sounds to **-6dB** instead of 0dB. Then let your in-game volume system handle the rest.

### Volume Architecture

```
Master Volume (user setting, 0–100%)
├── SFX Volume (user setting, 0–100%)
│   └── Per-sound volume (set by you, the dev)
├── Music Volume (user setting, 0–100%)
│   └── Per-track volume (set by you)
└── Ambience Volume (optional user setting)
    └── Per-ambient volume (set by you)

Final Volume = perSoundVolume × categoryVolume × masterVolume
```

### Volume Curves

Linear volume sliders feel wrong because human hearing is logarithmic. Apply a curve:

```csharp
// Convert a linear slider (0–1) to a perceptually even volume
public static float LinearToLog(float linear)
{
    if (linear <= 0f) return 0f;
    // Attempt a rough equal-loudness curve
    return MathF.Pow(linear, 2.0f);
}
```

A slider at 50% should sound "half as loud," not "barely audible." The squaring curve (`x²`) is a good starting point. Some devs use `x³` for even more dramatic scaling.

### Settings Menu Integration

Players **must** be able to control volume. Minimum:
- Master volume slider
- Music volume slider
- SFX volume slider

See [G55 — Settings Menu](../../monogame-arch/guides/G55_settings_menu.md) for implementation patterns. Save settings to disk so they persist between sessions.

---

## 8 — Sound Design by Genre

### Platformer Sound Checklist

| Sound | Priority | Notes |
|---|---|---|
| Jump | 🔴 Critical | Short, snappy. Pitch-vary slightly each time. |
| Land | 🔴 Critical | Matches surface (soft, hard, splashy) |
| Run/footsteps | 🟡 Important | 2–3 variants, surface-dependent |
| Hit/damage | 🔴 Critical | Distinct from enemy hit. Player needs to know. |
| Death | 🔴 Critical | Dramatic but not annoying (you'll hear it a lot) |
| Coin/collectible | 🔴 Critical | Satisfying, short, high pitch |
| Power-up | 🟡 Important | Rising tone, feels rewarding |
| Enemy hit | 🟡 Important | Different from player hit |
| Enemy death | 🟡 Important | Pop/splat/poof depending on style |
| Checkpoint | 🟢 Nice to have | Confirmation sound |
| Menu navigation | 🟡 Important | Click, confirm, cancel |
| Jump pad / spring | 🟢 Nice to have | Boing! |
| Level complete | 🔴 Critical | Celebratory jingle (2–4 seconds) |

### RPG Sound Checklist

| Sound | Priority | Notes |
|---|---|---|
| Footsteps | 🟡 Important | Surface-dependent: grass, stone, wood |
| Sword/weapon swing | 🔴 Critical | Whoosh + impact |
| Magic/spell cast | 🔴 Critical | Type-dependent: fire, ice, heal |
| Hit/damage taken | 🔴 Critical | |
| Level up | 🔴 Critical | Make this FEEL GOOD. Ascending tones + sparkle. |
| XP gain | 🟢 Nice to have | Subtle ding |
| Menu open/close | 🟡 Important | |
| Item pickup | 🔴 Critical | |
| Equip sound | 🟡 Important | Metal clank for armor, whoosh for weapon |
| Dialogue blip | 🟡 Important | Per-character pitch for personality |
| Gold/currency | 🟡 Important | Distinct from item pickup |
| Door / chest open | 🟡 Important | |
| Save game | 🟢 Nice to have | Confirmation sound |
| Shop buy/sell | 🟢 Nice to have | Ca-ching |

### Roguelike Sound Checklist

| Sound | Priority | Notes |
|---|---|---|
| Footsteps | 🟡 Important | Echo-y for dungeons |
| Attack / weapon | 🔴 Critical | |
| Enemy hit | 🔴 Critical | |
| Player hit | 🔴 Critical | |
| Death | 🔴 Critical | Make it hurt. This is a roguelike. |
| Item pickup | 🔴 Critical | |
| Stairs descend | 🟡 Important | Ominous. You're going deeper. |
| Door open | 🟡 Important | Creaky |
| Trap trigger | 🔴 Critical | Alarming |
| Inventory / menu | 🟡 Important | |
| Dungeon ambience | 🔴 Critical | Drips, distant sounds, wind. Sets the mood. |
| Shop / merchant | 🟢 Nice to have | |
| Boss reveal | 🟡 Important | Musical stinger |

---

## 9 — Common Audio Mistakes

### 1. Everything is Too Loud
Your sounds are fighting each other. Lower everything, then bring up what matters. Mix at a low speaker volume — if it sounds good quiet, it'll sound great loud.

### 2. Too Many Simultaneous Sounds
Cap concurrent instances per sound (see [Limiting Simultaneous Sounds](#limiting-simultaneous-sounds)). Prioritize: player SFX > enemy SFX > environmental. Kill lowest-priority sounds first.

### 3. No Volume Control
**Always** give the player volume sliders. Some people play at 2 AM. Some people play on speakers. Some people are hard of hearing. This is accessibility, not a feature.

### 4. Forgetting Ambience
A silent background is the easiest way to make a game feel empty. Even a subtle low-frequency hum fills the space. Layer ambience: base drone + random one-shots (bird chirp, drip, creak).

### 5. Music is Too Busy
Game music supports gameplay — it's not the main event. Keep melodies simple. Leave sonic space for SFX. If your music has lots of high-frequency content, your high-pitched SFX (coins, UI) will get lost.

### 6. Audio Doesn't Match Art Style
Pixel art + orchestral music = weird. Realistic art + chiptune = weird. Match your audio aesthetic to your visual aesthetic. Consistency sells the illusion.

### 7. No Sound Variation
If the player hears the exact same jump sound 500 times, it becomes grating. Solutions:
- **Pitch variation:** Randomly shift pitch ±5–10% each play
- **Multiple samples:** Record/create 3–5 variants, play randomly
- **Both:** Combine for maximum variety

```csharp
// Minimum viable variation
float pitch = (float)(random.NextDouble() * 0.2 - 0.1); // ±10%
jumpSound.Play(0.8f, pitch, 0f);
```

### 8. Click and Pop Artifacts
Caused by audio starting/stopping at non-zero amplitude. Fixes:
- Add 5–10ms fade-in/fade-out to every sound in Audacity
- Cut audio at zero crossings
- Use `SoundEffectInstance` and ramp volume instead of hard stop

### 9. Music Loops Have a Gap
The loop point clicks or has a moment of silence. Fix in your DAW by ensuring the last sample connects seamlessly to the first. Test by looping the file in a media player for several minutes.

### 10. Ignoring Audio Until the End
Audio informs game feel decisions. If you wait until the last week to add sounds, you'll never get the juicy feedback loops right. Add placeholder sounds from day one. Replace them later.

---

## 10 — Audio Polish Checklist

Run through this list before you ship. Every item should be checked off.

### Variation
- [ ] Repeated sounds have pitch variation (±5–10%)
- [ ] High-frequency sounds (footsteps, hits) have 2+ sample variants
- [ ] No sound plays identically twice in a row

### Transitions
- [ ] Music crossfades between scenes (0.5–1.0s fade)
- [ ] Ambience crossfades between areas
- [ ] No sudden silence when changing scenes
- [ ] Death/game over has appropriate audio transition

### Ducking & Priority
- [ ] Music volume ducks during important SFX (boss intro, dialogue)
- [ ] Simultaneous sound limit is enforced (max 3–5 of the same sound)
- [ ] Player sounds have priority over environmental sounds

### UI Audio
- [ ] Every button has a click/hover sound
- [ ] Confirm and cancel have distinct sounds
- [ ] Slider adjustments play feedback sound
- [ ] Menu open/close has a sound
- [ ] No UI interaction is silent

### Game Feel Sync
- [ ] Hit sounds sync with hitstop/freeze frames (see [G30](../../monogame-arch/guides/G30_game_feel_tooling.md))
- [ ] Screen shake events have accompanying audio
- [ ] Death has both visual and audio feedback
- [ ] Pickups have immediate audio response

### Settings & Accessibility
- [ ] Master volume slider works and saves
- [ ] Music volume slider works and saves
- [ ] SFX volume slider works and saves
- [ ] Volume settings persist between sessions (see [G55](../../monogame-arch/guides/G55_settings_menu.md))
- [ ] Volume 0 = truly silent (no quiet bleed-through)
- [ ] Mute option available (or volume 0 serves this purpose)

### Technical
- [ ] No audio clipping (check with headphones)
- [ ] No click/pop artifacts on any sound
- [ ] Music loops are seamless
- [ ] All audio files are in correct format (WAV for SFX, OGG for music)
- [ ] Unused audio assets removed from content pipeline
- [ ] Audio memory footprint tested (not loading 200MB of WAV into RAM)

### Final Listen Test
- [ ] Play through the entire game with headphones
- [ ] Play through with speakers at low volume
- [ ] Play through with sound off (make sure game is still playable!)
- [ ] Have someone else play and ask "does anything sound weird?"

---

## Quick Reference: Recommended Tool Stack

| Need | Free Option | Paid Option |
|---|---|---|
| SFX generation | [jsfxr](https://sfxr.me/) / [ChipTone](https://sfbgames.itch.io/chiptone) | — |
| Audio editing | [Audacity](https://www.audacityteam.org/) | — |
| Music (beginner) | [BeepBox](https://www.beepbox.co/) / [Bosca Ceoil](https://terrycavanagh.itch.io/bosca-ceoil) | — |
| Music (intermediate) | [LMMS](https://lmms.io/) / GarageBand | [FL Studio](https://www.image-line.com/) / [Reaper](https://www.reaper.fm/) |
| Chip-tune | [FamiStudio](https://famistudio.org/) | — |
| SFX sourcing | [Freesound](https://freesound.org) / [Sonniss GDC](https://sonniss.com/gameaudiogdc) | [Soundsnap](https://www.soundsnap.com) |
| Music sourcing | [Incompetech](https://incompetech.com) | [Epidemic Sound](https://www.epidemicsound.com) |

---

*Audio is the invisible 50% of your game. Players might not notice when it's good, but they'll absolutely notice when it's bad — or missing. Budget the time, use the tools, and run the checklists.*
