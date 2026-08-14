# R3 — Project Structure
> **Category:** Reference · **Related:** [R1 Library Stack](./R1_library_stack.md) · [E1 Architecture Overview](../architecture/E1_architecture_overview.md)

---

## Solution Layout

```
MyGame/
├── MyGame.slnx                    (.NET 10+ default; .sln also supported)
│
├── MyGame.Core/                    (shared game logic — 95%+ of code)
│   ├── src/
│   │   ├── Core/                   # App bootstrap, service locator, constants
│   │   │   ├── GameApp.cs          # MonoGame Game subclass, scene manager pump
│   │   │   ├── SceneManager.cs     # Custom scene manager → G1
│   │   │   └── ServiceLocator.cs   # Static access to shared services
│   │   │
│   │   ├── ECS/                    # Arch components, systems, world management
│   │   │   ├── Components/         # Pure data structs (Position, Velocity, BulletData...)
│   │   │   ├── Systems/            # Arch systems (MovementSystem, CollisionSystem...)
│   │   │   ├── Tags/               # Tag components (PlayerTag, EnemyTag, ProjectileTag...)
│   │   │   └── WorldManager.cs     # Arch World lifecycle, system registration
│   │   │
│   │   ├── Scenes/                 # Scene subclasses (MainMenu, Gameplay, Battle...)
│   │   │
│   │   ├── Rendering/              # Custom render layer system → G1, G2
│   │   │   ├── RenderLayerSystem.cs
│   │   │   ├── PostProcessors/     # Custom HLSL shader wrappers
│   │   │   └── Camera/             # Camera follow, shake, deadzone logic
│   │   │
│   │   ├── Collision/              # Custom SpatialHash + shape checks → G1, G3
│   │   │   ├── SpatialHash.cs
│   │   │   └── CollisionShapes.cs
│   │   │
│   │   ├── Systems/                # Game-specific systems → G10
│   │   │   ├── Inventory/
│   │   │   ├── Dialogue/
│   │   │   ├── Crafting/
│   │   │   ├── Combat/
│   │   │   ├── SaveLoad/
│   │   │   └── Procgen/
│   │   │
│   │   ├── AI/                     # BrainAI integration → G4
│   │   │
│   │   ├── UI/                     # Gum-based UI screens → G5
│   │   │
│   │   ├── Audio/                  # Audio manager → G6
│   │   │
│   │   ├── Input/                  # Apos.Input abstraction → G7
│   │   │
│   │   ├── Animation/              # Tweens, coroutines → G1
│   │   │   ├── TweenManager.cs
│   │   │   └── Transitions/        # Screen transition effects
│   │   │
│   │   ├── Shaders/                # Custom HLSL .fx files → G2, G27
│   │   │
│   │   └── Utils/                  # Math helpers, extensions, object pool
│   │       └── ObjectPool.cs       # Generic Pool<T> → G1
│   │
│   ├── Content/                    # MGCB content project → G8
│   │   ├── sprites/
│   │   ├── tilemaps/
│   │   ├── shaders/
│   │   ├── fonts/
│   │   └── audio/
│   │
│   └── Resources/                  # Runtime data (JSON configs, level defs, item databases)
│       ├── items.json
│       ├── dialogue/
│       ├── levels/
│       └── waves/
│
├── MyGame.Desktop/                 # DesktopGL launcher (Program.cs + app.manifest)
├── MyGame.iOS/                     # iOS launcher (AppDelegate + Directory.Build.props)
│   ├── Program.cs                  # UIApplicationDelegate with deferred game creation
│   ├── Directory.Build.props       # Build output redirect (iCloud Drive workaround)
│   └── MyGame.iOS.csproj           # MonoGame.Framework.iOS + MGCB reference
└── MyGame.Android/                 # Android platform (TBD)
```

---

## Key Principles

**95% of code lives in `MyGame.Core/`.** Platform projects (Desktop, iOS, Android) are thin wrappers that just bootstrap the game.

**ECS/ is for Arch-specific code.** Components are pure data structs. Systems are Arch query systems. Tags are zero-size marker components.

**Systems/ is for game logic.** Inventory, dialogue, crafting — these are custom C# modules that may or may not use Arch internally, but they represent game-level features.

**Rendering/ and Collision/ hold the ~500 lines of custom glue code** that replaces Nez's rendering pipeline and collision system.

**Resources/ is for runtime data.** JSON files loaded at runtime. Not compiled by MGCB. Use System.Text.Json for deserialization.

**Content/ is for MGCB-compiled assets.** Textures, tilemaps, shaders, fonts, audio — compiled at build time to .xnb format.

---

## iOS Platform Details

### AppDelegate Pattern

iOS uses UIKit's `UIApplicationDelegate` with deferred game creation. MonoGame 3.8.4 on iOS triggers a display link callback before `_platform` is assigned, causing `NullReferenceException` in `Game.get_IsActive()`.

**Fix:** Defer `new GameApp()` + `Run()` to the next run loop iteration:

```csharp
using System.Reflection;
using CoreAnimation;
using Foundation;
using Microsoft.Xna.Framework;
using UIKit;

[Register("AppDelegate")]
internal class AppDelegate : UIApplicationDelegate
{
    private Core.GameApp? _game;  // MUST be a field — Game.Run() is non-blocking on iOS

    public override UIWindow? Window { get; set; }

    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        NSRunLoop.Main.InvokeOnMainThread(() =>
        {
            _game = new Core.GameApp();

            // Hook ProMotion 120Hz support — MonoGame 3.8.4 uses deprecated
            // FrameInterval API which caps at 60Hz. Patch the display link
            // with PreferredFrameRateRange after each TargetElapsedTime change.
            _game.PlatformTargetFpsChanged = () =>
            {
                int targetFps = (int)Math.Round(1.0 / _game.TargetElapsedTime.TotalSeconds);
                SetDisplayLinkFrameRate(_game, targetFps);
            };

            _game.Run();
        });
        return true;
    }

    /// <summary>
    /// Uses reflection to access MonoGame's internal CADisplayLink and set
    /// PreferredFrameRateRange for ProMotion 120Hz support.
    /// </summary>
    private static void SetDisplayLinkFrameRate(Game game, int targetFps)
    {
        try
        {
            BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;

            // MonoGame 3.8.4: Game.Platform is an internal field
            FieldInfo? platformField = typeof(Game).GetField("Platform", flags);
            object? platform = platformField?.GetValue(game);
            if (platform == null) return;

            // iOSGamePlatform._displayLink is the CADisplayLink
            FieldInfo? dlField = platform.GetType().GetField("_displayLink", flags);
            if (dlField?.GetValue(platform) is not CADisplayLink displayLink) return;

            displayLink.PreferredFrameRateRange = new CAFrameRateRange
            {
                Minimum = 30,
                Maximum = targetFps,
                Preferred = targetFps
            };
        }
        catch
        {
            // Reflection failed — MonoGame internals may have changed.
        }
    }
}

internal static class Program
{
    private static void Main(string[] args)
    {
        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}
```

**Critical:**
- `Game.Run()` is **non-blocking** on iOS (starts display link and returns). Using `using var game` or a local variable causes immediate disposal/GC.
- `OperatingSystem.IsMacOS()` returns **false** on iOS. Use `OperatingSystem.IsIOS()` for iOS detection.
- `CAFrameRateRange` in .NET iOS has no 3-argument constructor — use object initializer syntax.
- `<TrimmerRootAssembly Include="MonoGame.Framework" />` in .csproj preserves MonoGame fields from the IL trimmer so reflection works.

### MyGame.iOS.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-ios</TargetFramework>
    <OutputType>Exe</OutputType>
    <SupportedOSPlatformVersion>15.0</SupportedOSPlatformVersion>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <!-- Code signing for physical device builds -->
  <PropertyGroup Condition="'$(RuntimeIdentifier)' == 'ios-arm64'">
    <CodesignKey>Apple Development</CodesignKey>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\MyGame.Core\MyGame.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="MonoGame.Framework.iOS" Version="3.8.*" />
    <PackageReference Include="MonoGame.Content.Builder.Task" Version="3.8.*" />
  </ItemGroup>

  <!-- Launch screen (required for native resolution) -->
  <ItemGroup>
    <BundleResource Include="LaunchScreen.storyboard" />
  </ItemGroup>

  <!-- Preserve MonoGame internals from IL trimmer so reflection can access
       the private _displayLink field for ProMotion 120Hz support -->
  <ItemGroup>
    <TrimmerRootAssembly Include="MonoGame.Framework" />
  </ItemGroup>

  <ItemGroup>
    <MonoGameContentReference Include="..\MyGame.Core\Content\MyGame.mgcb" />
  </ItemGroup>
</Project>
```

### iCloud Drive Codesign Workaround

If the project lives on iCloud Drive, codesign fails with "resource fork, Finder information, or similar detritus not allowed" — iCloud adds `com.apple.FinderInfo` xattr that codesign rejects. `xattr -cr` does NOT fix it (iCloud re-adds immediately).

**Fix:** Redirect build output via `Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <BaseOutputPath>$(HOME)/.local/share/MyGame/ios-build/bin/</BaseOutputPath>
    <BaseIntermediateOutputPath>$(HOME)/.local/share/MyGame/ios-build/obj/</BaseIntermediateOutputPath>
  </PropertyGroup>
</Project>
```

### LaunchScreen.storyboard

**Required for native resolution.** Without a launch storyboard, iOS runs the app in legacy compatibility mode — scaled down with brown letterbox bars and incorrect touch offset.

```xml
<?xml version="1.0" encoding="UTF-8"?>
<document type="com.apple.InterfaceBuilder3.CocoaTouch.Storyboard.XIB" version="3.0"
    toolsVersion="22154" targetRuntime="iOS.CocoaTouch" propertyAccessControl="none"
    useAutolayout="YES" launchScreen="YES" useTraitCollections="YES"
    useSafeAreas="YES" colorMatched="YES" initialViewController="01J-lp-oVM">
    <dependencies>
        <plugIn identifier="com.apple.InterfaceBuilder.IBCocoaTouchPlugin" version="22130"/>
        <capability name="Safe area layout guides" minToolsVersion="9.0"/>
        <capability name="documents saved in the Xcode 8 format" minToolsVersion="8.0"/>
    </dependencies>
    <scenes>
        <scene sceneID="EHf-IW-A2E">
            <objects>
                <viewController id="01J-lp-oVM" sceneMemberID="viewController">
                    <view key="view" contentMode="scaleToFill" id="Ze5-6b-2t3">
                        <rect key="frame" x="0.0" y="0.0" width="393" height="852"/>
                        <autoresizingMask key="autoresizingMask"
                            widthSizable="YES" heightSizable="YES"/>
                        <viewLayoutGuide key="safeArea" id="Bcu-3y-fUS"/>
                        <color key="backgroundColor" red="0" green="0" blue="0"
                            alpha="1" colorSpace="custom" customColorSpace="sRGB"/>
                    </view>
                </viewController>
                <placeholder placeholderIdentifier="IBFirstResponder"
                    id="iYj-Kq-Ea1" userLabel="First Responder"
                    sceneMemberID="firstResponder"/>
            </objects>
        </scene>
    </scenes>
</document>
```

**Key requirements:**
- `targetRuntime` must be `"iOS.CocoaTouch"` — NOT `"AppleSDK"` (causes ibtool compilation error)
- Must include `<dependencies>` block with `com.apple.InterfaceBuilder.IBCocoaTouchPlugin`
- Must include `<viewLayoutGuide key="safeArea">` when `useSafeAreas="YES"` is set
- Add to .csproj as `<BundleResource Include="LaunchScreen.storyboard" />`
- Add to Info.plist: `<key>UILaunchStoryboardName</key><string>LaunchScreen</string>`

### Info.plist

Key entries for an iOS MonoGame project:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN"
    "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>MyGame</string>
    <key>CFBundleIdentifier</key>
    <string>com.mygame.app</string>
    <key>CFBundleShortVersionString</key>
    <string>1.0</string>
    <key>CFBundleVersion</key>
    <string>1</string>
    <key>MinimumOSVersion</key>
    <string>15.0</string>
    <key>UIDeviceFamily</key>
    <array>
        <integer>1</integer>  <!-- iPhone -->
        <integer>2</integer>  <!-- iPad -->
    </array>
    <key>UISupportedInterfaceOrientations</key>
    <array>
        <string>UIInterfaceOrientationLandscapeLeft</string>
        <string>UIInterfaceOrientationLandscapeRight</string>
    </array>
    <key>UILaunchStoryboardName</key>
    <string>LaunchScreen</string>
    <key>UIStatusBarHidden</key>
    <true/>
    <key>UIRequiresFullScreen</key>
    <true/>
    <key>LSRequiresIPhoneOS</key>
    <true/>
    <!-- Required for ProMotion 120Hz on iPhone (iPad works without it) -->
    <key>CADisableMinimumFrameDurationOnPhone</key>
    <true/>
</dict>
</plist>
```

### Build & Deploy Commands

```bash
# Desktop
dotnet build MyGame.Desktop/
dotnet run --project MyGame.Desktop/

# iOS Simulator (Apple Silicon)
dotnet build MyGame.iOS/ -r iossimulator-arm64
dotnet build MyGame.iOS/ -r iossimulator-arm64 -t:Run

# Physical device (requires code signing)
dotnet build MyGame.iOS/ -r ios-arm64
dotnet build MyGame.iOS/ -r ios-arm64 -t:Run -p:_DeviceName=<UDID>

# Find device UDID
xcrun xctrace list devices
```

> **Note:** iOS incremental builds may not pick up source changes. Use `--no-incremental` to force a full rebuild when in doubt.
