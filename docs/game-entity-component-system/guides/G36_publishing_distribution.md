# G36 — Publishing & Distribution


> **Category:** Guide · **Related:** [G32 Deployment & Platform Builds](./G32_deployment_platform_builds.md) · [E4 Solo Project Management](../../core/project-management/E4_project_management.md) · [E9 Solo Dev Playbook](../../core/project-management/E9_solo_dev_playbook.md)

> **Stack:** MonoGame · .NET · Multi-platform

You built the game. Now ship it. This guide covers platform-specific publishing, build automation, marketing, and post-launch operations for indie 2D games built with MonoGame.

---

## Steam (Steamworks)

Steam is the primary PC storefront. Expect 70–80% of your PC revenue to come from here.

### Steamworks SDK Integration

MonoGame doesn't ship with Steamworks bindings. Use **Steamworks.NET** (C# wrapper):

```bash
dotnet add package Steamworks.NET
```

```csharp
public class SteamManager : IDisposable
{
    public bool Initialized { get; private set; }
    
    public void Initialize(uint appId)
    {
        // Set app ID for dev (remove for shipping — steam_appid.txt handles it)
        Environment.SetEnvironmentVariable("SteamAppId", appId.ToString());
        
        Initialized = SteamAPI.Init();
        if (!Initialized)
        {
            Console.WriteLine("Steam not running or AppID invalid.");
            return;
        }
    }
    
    public void Update()
    {
        if (Initialized) SteamAPI.RunCallbacks();
    }
    
    public void Dispose()
    {
        if (Initialized) SteamAPI.Shutdown();
    }
}
```

Call `SteamAPI.RunCallbacks()` every frame (or at least every 100ms) to process Steam events.

### Key Steamworks Features

| Feature | Implementation | Notes |
|---------|---------------|-------|
| **Achievements** | `SteamUserStats.SetAchievement("ACH_ID")` → `StoreStats()` | Define in Steamworks dashboard first |
| **Cloud Saves** | Enable in app settings; configure paths | Steam auto-syncs files in configured folders |
| **Overlay** | Works automatically when SDK initialized | Test with Shift+Tab |
| **Rich Presence** | `SteamFriends.SetRichPresence("status", "In Level 3")` | Shows in friends list |
| **Leaderboards** | `SteamUserStats.FindOrCreateLeaderboard(...)` | Async callbacks |
| **Workshop** | `SteamUGC` API for mod upload/download | Great for community content |

### Cloud Save Setup

In your Steamworks app config, set the cloud paths:

```
Root: [Steam Auto-Cloud]
Path: saves/
Pattern: *.sav
```

MonoGame save location:

```csharp
public static string SaveDirectory => 
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
                 "YourGame", "saves");
```

### Store Page Optimization

Your store page sells the game. Optimize it:

| Element | Best Practice |
|---------|--------------|
| **Capsule art** | Bold, readable at small sizes. Title legible at 200×93 px |
| **Screenshots** | First 4 are critical — show gameplay, not menus. 1920×1080 |
| **Trailer** | 60–90 seconds. Gameplay in first 5 seconds. No long logo intros |
| **Description** | First paragraph is everything (above-the-fold). Features as bullet points |
| **Tags** | Use all 15+ relevant tags. Check competitor games for ideas |
| **Short description** | 1–2 sentences that hook. This shows in search results |

### Launch Visibility

Steam's algorithm rewards **wishlist velocity** and **first-week sales**:

1. **Build wishlists before launch** — Target 10,000+ for algorithm visibility
2. **Launch discount** — 10% launch discount is standard and boosts conversion
3. **Steam Next Fest** — Free demo event. Apply 3+ months ahead. Massive wishlist generator
4. **Launch timing** — Tuesday–Thursday. Avoid major AAA releases. Check [GamesIndustry calendar](https://www.gamesindustry.biz/release-dates)
5. **First 2 weeks matter most** — Algorithm evaluates early sales for recommendation placement

---

## itch.io

itch.io is ideal for early builds, game jams, niche games, and building a community before a Steam launch.

### Butler CLI for Uploads

```bash
# Install butler
curl -L https://broth.itch.ovh/butler/linux-amd64/LATEST/archive/default -o butler.zip
unzip butler.zip && chmod +x butler

# Login
./butler login

# Push a build (creates channels automatically)
./butler push ./build/windows yourname/yourgame:windows
./butler push ./build/linux yourname/yourgame:linux
./butler push ./build/mac yourname/yourgame:mac
```

Butler uses **binary patching** — only uploads changed bytes. Fast incremental updates.

### Pricing Strategies

| Model | When to Use |
|-------|-------------|
| **Free** | Game jam entries, demos, portfolio pieces |
| **Name your price** (min $0) | Early builds, generous pay-what-you-want |
| **Name your price** (min $X) | Soft paywall with flexibility |
| **Fixed price** | Final release, typically $5–$20 for indie 2D |

**Tip:** itch.io lets you set "pay more than minimum" suggestions. A $5 minimum with a $10 suggestion often averages $7–8.

### Community Building

- Enable devlogs on your itch.io page
- Post update logs with screenshots/GIFs
- itch.io comments are low-volume but high-engagement
- Use itch.io as a demo host while building Steam wishlists

---

## iOS App Store

### MonoGame iOS Pipeline

MonoGame supports iOS via **Xamarin.iOS / .NET for iOS**:

```bash
# Create iOS project
dotnet new mgios -n MyGame.iOS
```

Build requirements:
- **macOS** with Xcode installed
- Apple Developer account ($99/year)
- Provisioning profiles and signing certificates

### Submission Process

1. **TestFlight** — Upload builds via Xcode or `altool`. Test with up to 10,000 external testers
2. **App Store Connect** — Configure metadata, screenshots (6.7", 6.5", 5.5" sizes), pricing
3. **App Review** — Typically 24–48 hours. Common rejection reasons:
   - Crashes on launch
   - Misleading screenshots
   - Missing privacy policy
   - Non-functional links
4. **Release** — Manual or automatic after approval

### App Review Guidelines — Key Points for Games

| Guideline | Requirement |
|-----------|-------------|
| **3.1.1** | In-App Purchases for digital goods (no Stripe/PayPal for in-game items) |
| **2.3.7** | Accurate screenshots — must reflect actual gameplay |
| **5.1.1** | Privacy policy URL required |
| **4.0** | No beta/test/trial in the app name or description |
| **2.1** | App must be complete — no placeholder content |

### In-App Purchases

If your game has IAP (cosmetics, expansions, currency):

```csharp
// Use Plugin.InAppBilling or StoreKit directly
// Define products in App Store Connect first
var productIds = new[] { "com.yourgame.expansion1", "com.yourgame.coins500" };
```

Apple takes 30% (15% for Small Business Program if under $1M revenue).

---

## Console Ports

An overview of indie console programs. Each requires a separate application and dev kit.

| Platform | Program | Dev Kit Cost | Key Requirements |
|----------|---------|-------------|------------------|
| **Xbox** | [ID@Xbox](https://www.xbox.com/en-US/developers/id) | Free (dev mode on retail Xbox) | Apply with game concept; GDK access |
| **Nintendo** | [Nintendo Developer Portal](https://developer.nintendo.com/) | ~$450 (Switch dev kit) | Apply with company info + game; NintendoSDK |
| **PlayStation** | [PlayStation Partners](https://partners.playstation.com/) | Free (PS4 test kit on approval) | Apply with studio info; PS SDK |

### MonoGame on Consoles

MonoGame doesn't officially support console SDKs. Options:

1. **FNA** — Andrew Russell's reimplementation; has shipped on all major consoles
2. **Custom port** — Use MonoGame's architecture but replace the graphics/input backend
3. **Engine switch** — Some developers port to a console-friendly engine for that platform

**Reality check:** Console ports are significant work. Ship on PC first, validate market fit, then port if sales justify it.

---

## Build Automation

### GitHub Actions for Multi-Platform Builds

```yaml
# .github/workflows/build.yml
name: Build & Package

on:
  push:
    tags: ['v*']

jobs:
  build-windows:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      - run: dotnet publish -c Release -r win-x64 --self-contained -o build/windows
      - uses: actions/upload-artifact@v4
        with:
          name: windows-build
          path: build/windows/

  build-linux:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      - run: dotnet publish -c Release -r linux-x64 --self-contained -o build/linux
      - uses: actions/upload-artifact@v4
        with:
          name: linux-build
          path: build/linux/

  build-mac:
    runs-on: macos-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      - run: dotnet publish -c Release -r osx-arm64 --self-contained -o build/mac
      - uses: actions/upload-artifact@v4
        with:
          name: mac-build
          path: build/mac/

  deploy-itch:
    needs: [build-windows, build-linux, build-mac]
    runs-on: ubuntu-latest
    steps:
      - uses: actions/download-artifact@v4
      - uses: josephbmanley/butler-publish-itchio-action@v1.0.3
        env:
          BUTLER_CREDENTIALS: ${{ secrets.ITCH_BUTLER_KEY }}
          CHANNEL: windows
          ITCH_GAME: yourname/yourgame
          PACKAGE: windows-build/
      # Repeat for linux and mac channels

  deploy-steam:
    needs: [build-windows, build-linux, build-mac]
    runs-on: ubuntu-latest
    steps:
      - uses: actions/download-artifact@v4
      - uses: game-ci/steam-deploy@v3
        with:
          username: ${{ secrets.STEAM_USERNAME }}
          configVdf: ${{ secrets.STEAM_CONFIG_VDF }}
          appId: 123456
          buildDescription: ${{ github.ref_name }}
          rootPath: .
          depot1Path: windows-build/
          depot2Path: linux-build/
          depot3Path: mac-build/
```

### Versioning

Tag releases in git. Use semantic versioning: `v1.2.3`

```csharp
// Embed version at build time
public static string Version => 
    Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "dev";
```

Set in `.csproj`:

```xml
<PropertyGroup>
  <Version>1.2.3</Version>
  <InformationalVersion>$(Version)-$(Configuration)</InformationalVersion>
</PropertyGroup>
```

---

## Marketing

### Timeline

| Phase | Timing | Actions |
|-------|--------|---------|
| **Pre-announcement** | 12+ months before launch | Build social presence, post dev GIFs |
| **Announcement** | 6–9 months before | Trailer, Steam page live, start wishlist push |
| **Demo / Next Fest** | 3–6 months before | Playable demo, Steam Next Fest participation |
| **Launch push** | 1 month before | Press outreach, streamer keys, ramp social |
| **Launch** | Day 0 | Launch discount, social blitz, respond to feedback |
| **Post-launch** | Ongoing | Updates, community, DLC, sales events |

### Devlog Cadence

Post regular updates to build audience:

| Platform | Frequency | Format |
|----------|-----------|--------|
| Twitter/Bluesky | 2–3x/week | GIFs, screenshots, short dev insights |
| YouTube/TikTok | Monthly | Devlog videos (5–10 min) |
| itch.io / Steam | Bi-weekly | Written updates with images |
| Reddit | When relevant | r/gamedev, r/indiegaming, game-specific subs |
| Discord | Daily–weekly | Behind-the-scenes, polls, community interaction |

### Press Kit

Create a **presskit page** (use [presskit()](https://dopresskit.com/) or a simple webpage):

- Game description (short + long)
- Key features (bullet points)
- Screenshots (PNG, full resolution, no watermarks)
- Logo and key art (transparent PNG + dark/light backgrounds)
- Trailer (YouTube link + downloadable MP4)
- Team info and contact email
- Release date, platforms, price
- Previous coverage / awards

### Trailer Tips

1. **Gameplay first** — Show gameplay within the first 3 seconds
2. **60–90 seconds** — Attention spans are short
3. **Music matters** — Licensed or original. Sets the tone
4. **End with CTA** — "Wishlist now on Steam" with logo
5. **No dev commentary** — Save that for devlog videos
6. **Capture at 1080p60** — Even if your game is pixel art

### Streamer/Content Creator Keys

- Use [Keymailer](https://www.keymailer.co/), [Woovit](https://woovit.com/), or [Terminals.io](https://terminals.io/) for key distribution
- Target creators with 1K–50K followers (more likely to play and more engaged audiences)
- Send keys 1–2 weeks before launch
- Include a one-paragraph pitch + presskit link

---

## Legal Essentials

### EULA

Keep it simple for indie games. Cover:

- License grant (personal, non-commercial use)
- Restrictions (no reverse engineering, redistribution)
- Warranty disclaimer
- Limitation of liability
- Termination clause

Steam provides a default EULA. You can use it unless you have specific needs.

### Privacy Policy

**Required for:** iOS App Store, Google Play, Steam (if collecting any data), GDPR compliance.

Must disclose:

- What data you collect (analytics, crash reports, save data)
- How you use it
- Third parties (Steam, analytics services)
- How to request deletion
- Contact information

**For mobile:** This is a hard requirement. Apple rejects apps without a privacy policy URL.

### Age Ratings

| System | Region | Process | Cost |
|--------|--------|---------|------|
| **IARC** | Global (digital) | Questionnaire via storefront | Free (Steam, Google, Nintendo) |
| **ESRB** | North America | Through IARC for digital; paid for physical | Free digital / $3,000 physical |
| **PEGI** | Europe | Through IARC for digital | Free digital |
| **USK** | Germany | Through IARC for digital | Free digital |

**Steam/itch.io:** Use the IARC questionnaire during store setup. It generates ratings for all major systems.

**Console:** Each platform guides you through their rating process during submission.

---

## Post-Launch

### Patch Cadence

| Timeframe | Priority |
|-----------|----------|
| **Day 1–3** | Critical bugs, crashes, save corruption — hotfix immediately |
| **Week 1–2** | High-priority bugs, balance issues, performance |
| **Month 1** | First content update or quality-of-life patch |
| **Ongoing** | Monthly or bi-monthly updates |

**Communicate:** Post patch notes on Steam, Discord, and social media. Players appreciate transparency.

### Community Management

| Channel | Purpose | Effort |
|---------|---------|--------|
| **Steam forums** | Bug reports, general discussion | Monitor daily at launch |
| **Discord** | Active community, beta testing, feedback | High — needs moderation |
| **Twitter/social** | Announcements, engagement | Medium |
| **Reddit** | Occasional posts, community-driven | Low — participate, don't dominate |

**Key principles:**

- Respond to bug reports promptly (even just "we're aware, investigating")
- Don't argue with negative reviews — fix the issues, then reply
- Celebrate community content (fan art, speedruns, mods)
- Be transparent about development roadmap

### DLC Planning

If your game warrants expansion content:

| DLC Type | Pricing | Timeline |
|----------|---------|----------|
| **Cosmetic pack** | $2–5 | Can ship alongside or shortly after launch |
| **Content expansion** | $5–15 | 3–6 months post-launch |
| **Major expansion** | $10–20 | 6–12 months post-launch |
| **Soundtrack** | $5–10 | Ship with base game or shortly after |

**Steam DLC page** should have its own capsule art, description, and screenshots.

### Sales Events

| Event | When | Typical Discount |
|-------|------|-----------------|
| Steam Summer Sale | June | 20–40% |
| Steam Winter Sale | December | 20–50% |
| Steam Autumn Sale | November | 20–40% |
| Steam Next Fest | Feb/Jun/Oct | Free demo (no discount) |
| Publisher/themed sales | Various | 15–30% |
| itch.io sales | Creator-scheduled | Any |

**Discount strategy:** Start small (10–20%) and deepen over time. Don't hit 75% off in the first year — it devalues the game and frustrates early buyers.

---

## Checklist

- [ ] Steamworks SDK integrated and tested (achievements, cloud saves)
- [ ] Steam store page live with capsule art, screenshots, trailer, tags
- [ ] itch.io page set up with butler CI/CD
- [ ] GitHub Actions building for Windows, Linux, macOS
- [ ] Press kit page with all assets
- [ ] Privacy policy URL (especially if targeting mobile)
- [ ] IARC age rating questionnaire completed
- [ ] Streamer keys distributed 1–2 weeks before launch
- [ ] Discord server set up with roles, channels, moderation
- [ ] Launch discount configured (10% standard)
- [ ] Post-launch patch plan documented
- [ ] Analytics/crash reporting integrated for day-1 issue triage

---

## Further Reading

- [Steamworks Documentation](https://partner.steamgames.com/doc/home) — Official Steamworks SDK docs
- [Steamworks.NET](https://steamworks.github.io/) — C# Steamworks wrapper
- [How to Market a Game](https://howtomarketagame.com/) — Chris Zukowski's marketing blog (data-driven)
- [GDC Vault](https://gdcvault.com/) — Search for "indie publishing" and "marketing" talks
- [presskit()](https://dopresskit.com/) — Free press kit generator
- [ID@Xbox](https://www.xbox.com/en-US/developers/id) — Xbox indie program
- [Nintendo Developer Portal](https://developer.nintendo.com/) — Switch development access
