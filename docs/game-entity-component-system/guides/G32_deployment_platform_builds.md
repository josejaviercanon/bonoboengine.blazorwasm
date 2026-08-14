# G32 — Deployment & Platform Builds


> **Category:** Guide · **Related:** [G8 Content Pipeline](./G8_content_pipeline.md) · [G24 Window & Display Management](./G24_window_display_management.md) · [G25 Safe Areas & Adaptive Layout](./G25_safe_areas_adaptive_layout.md) · [E4 Solo Project Management](../../core/project-management/E4_project_management.md)

> Everything needed to ship a MonoGame + Arch ECS game: dotnet publish flags, platform-specific builds, Steam/itch.io distribution, code signing, CI/CD pipelines, and troubleshooting deployment failures.

---

## 1. Critical .NET Publish Flags

MonoGame games have specific runtime requirements that differ from typical .NET applications. Getting the publish flags wrong causes micro-stutters, missing dependencies, or broken builds.

### 1.1 The Essential Command

```bash
dotnet publish -c Release \
  -r <RID> \
  -p:PublishReadyToRun=false \
  -p:TieredCompilation=false \
  --self-contained
```

Every flag matters:

| Flag | Value | Why |
|------|-------|-----|
| `-c Release` | Release config | Enables optimizations, strips debug symbols |
| `-r <RID>` | Runtime identifier | Targets a specific OS/arch (e.g., `win-x64`) |
| `-p:PublishReadyToRun=false` | Disable R2R | R2R pre-compiles to native code but **causes micro-stutters** during fallback to JIT for uncovered code paths |
| `-p:TieredCompilation=false` | Disable tiered JIT | Tiered compilation initially runs unoptimized code, then recompiles hot paths — the recompilation causes **frame hitches** during gameplay |
| `--self-contained` | Bundle .NET runtime | Players don't need .NET installed; guarantees the correct runtime version |

### 1.2 Why ReadyToRun Hurts Games

ReadyToRun (R2R) pre-compiles assemblies to native code ahead of time. Sounds great — but R2R images contain only *partial* native code. Methods not covered by the R2R compilation still require JIT at runtime. When the JIT kicks in mid-gameplay to compile a code path hit for the first time (a new enemy type, a boss phase, a menu opened once), it causes a visible frame spike.

For business applications, this is fine. For games running at 60fps with a 16.67ms frame budget, a 50ms JIT pause is a 3-frame stutter.

### 1.3 Why TieredCompilation Hurts Games

.NET's tiered compilation runs methods at "Tier 0" (minimal optimization) first, then promotes frequently-called methods to "Tier 1" (full optimization) in the background. The promotion involves recompiling and patching method pointers, which causes unpredictable frame-time spikes during the first few minutes of gameplay.

With `TieredCompilation=false`, the JIT compiles every method at full optimization on first call. Startup is slightly slower, but frame timing is consistent from the first frame.

### 1.4 Self-Contained vs Framework-Dependent

| Mode | Bundle Size | Player Requirement | Recommendation |
|------|------------|-------------------|----------------|
| **Self-contained** (`--self-contained`) | ~60-80 MB larger | None | ✅ **Always use for distribution** |
| **Framework-dependent** (default) | Smaller | Player must install matching .NET runtime | ❌ Don't ship this |

Framework-dependent saves disk space but creates a support nightmare. Players won't have .NET 8 installed. They'll see a cryptic error. They'll leave a negative review. Ship self-contained.

### 1.5 Trimming (Optional)

```bash
dotnet publish -c Release -r win-x64 \
  -p:PublishReadyToRun=false \
  -p:TieredCompilation=false \
  -p:PublishTrimmed=true \
  -p:TrimMode=partial \
  --self-contained
```

Trimming removes unused .NET assemblies, reducing the self-contained bundle by 20-40 MB. Use `TrimMode=partial` (safe) rather than `full` (aggressive — may break reflection).

> **Warning:** If you use `System.Text.Json` with reflection-based serialization, trimming will break it. Use source generators (`[JsonSerializable]`) or switch to `TrimMode=partial`.

### 1.6 .csproj Defaults

Set publish flags in the project file so you don't forget them:

```xml
<PropertyGroup Condition="'$(Configuration)' == 'Release'">
  <PublishReadyToRun>false</PublishReadyToRun>
  <TieredCompilation>false</TieredCompilation>
  <SelfContained>true</SelfContained>
  <!-- Optional trimming -->
  <PublishTrimmed>true</PublishTrimmed>
  <TrimMode>partial</TrimMode>
</PropertyGroup>
```

---

## 2. DesktopGL vs WindowsDX

MonoGame ships two desktop backends. Choose the right one before you start — switching mid-project is possible but tedious.

### 2.1 Comparison

| Feature | DesktopGL | WindowsDX |
|---------|-----------|-----------|
| **Graphics API** | OpenGL 3+ (via SDL2) | DirectX 11 |
| **Platforms** | Windows, macOS, Linux | Windows only |
| **Shader language** | GLSL (cross-compiled from HLSL) | HLSL |
| **Performance** | Slightly lower on Windows | Slightly higher on Windows |
| **Compatibility** | Widest — runs everywhere | Windows exclusive |
| **Native dependencies** | SDL2, OpenAL | DirectX runtime |
| **Steam Deck** | ✅ Native | ❌ Requires Proton |
| **macOS** | ✅ Native | ❌ Not supported |
| **Linux** | ✅ Native | ❌ Not supported |

### 2.2 When to Use Which

**Use DesktopGL** (recommended default):
- You want to ship on macOS and/or Linux
- You want native Steam Deck support
- You're building with a shared Core project for iOS/Android
- You want a single codebase for all desktop platforms

**Use WindowsDX** only when:
- Windows-exclusive game with no plans for other platforms
- You need a specific DirectX 11 feature (rare for 2D games)
- Performance profiling shows meaningful GPU-bound improvement (unlikely for 2D)

### 2.3 DirectX Runtime Requirement

WindowsDX builds require the DirectX End-User Runtime on the player's machine. Most Windows PCs have it, but not all. Bundle the installer or link to it:

```
# Include in your Windows distribution
https://www.microsoft.com/en-us/download/details.aspx?id=35
```

DesktopGL has no such requirement — SDL2 and OpenAL libraries are bundled with the MonoGame NuGet package.

### 2.4 Switching Between Backends

The only change is the NuGet package reference:

```xml
<!-- DesktopGL -->
<PackageReference Include="MonoGame.Framework.DesktopGL" Version="3.8.*" />

<!-- WindowsDX -->
<PackageReference Include="MonoGame.Framework.WindowsDX" Version="3.8.*" />
```

Game code is identical. Content pipeline builds are the same (MGCB compiles shaders for the target platform). The `.mgcb` file's `/platform:` directive should match: `DesktopGL` or `WindowsDX`.

---

## 3. dotnet publish Per Platform

### 3.1 Windows (win-x64)

```bash
dotnet publish -c Release -r win-x64 \
  -p:PublishReadyToRun=false \
  -p:TieredCompilation=false \
  --self-contained \
  -o publish/windows
```

Output: `publish/windows/` contains the `.exe`, all DLLs, content, and the bundled .NET runtime. Zip the folder and distribute.

### 3.2 macOS (osx-x64 and osx-arm64)

```bash
# Intel Mac
dotnet publish -c Release -r osx-x64 \
  -p:PublishReadyToRun=false \
  -p:TieredCompilation=false \
  --self-contained \
  -o publish/osx-x64

# Apple Silicon (M1/M2/M3/M4)
dotnet publish -c Release -r osx-arm64 \
  -p:PublishReadyToRun=false \
  -p:TieredCompilation=false \
  --self-contained \
  -o publish/osx-arm64
```

### 3.3 Universal macOS Binary (lipo)

Ship a single binary that runs natively on both Intel and Apple Silicon:

```bash
# 1. Publish for both architectures
dotnet publish -c Release -r osx-x64 \
  -p:PublishReadyToRun=false -p:TieredCompilation=false \
  --self-contained -o publish/osx-x64

dotnet publish -c Release -r osx-arm64 \
  -p:PublishReadyToRun=false -p:TieredCompilation=false \
  --self-contained -o publish/osx-arm64

# 2. Create output directory
mkdir -p publish/osx-universal

# 3. Copy one architecture as the base (gets all non-binary files)
cp -R publish/osx-arm64/ publish/osx-universal/

# 4. Merge native binaries with lipo
# Find all Mach-O binaries and merge them
find publish/osx-arm64 -type f | while read -r file; do
  relative="${file#publish/osx-arm64/}"
  x64_file="publish/osx-x64/$relative"
  out_file="publish/osx-universal/$relative"

  if [ -f "$x64_file" ] && file "$file" | grep -q "Mach-O"; then
    lipo -create "$file" "$x64_file" -output "$out_file" 2>/dev/null || true
  fi
done
```

**Simpler approach** — merge just the main executable and known native libraries:

```bash
# Merge the main game executable
lipo -create \
  publish/osx-x64/MyGame \
  publish/osx-arm64/MyGame \
  -output publish/osx-universal/MyGame

# Merge native libraries (libSDL2, libOpenAL, etc.)
for lib in libSDL2.dylib libopenal.1.dylib; do
  lipo -create \
    "publish/osx-x64/$lib" \
    "publish/osx-arm64/$lib" \
    -output "publish/osx-universal/$lib" 2>/dev/null || true
done

# Verify
lipo -info publish/osx-universal/MyGame
# Output: Architectures in the fat file: x86_64 arm64
```

### 3.4 Linux (linux-x64)

```bash
dotnet publish -c Release -r linux-x64 \
  -p:PublishReadyToRun=false \
  -p:TieredCompilation=false \
  --self-contained \
  -o publish/linux
```

Mark the executable as runnable:
```bash
chmod +x publish/linux/MyGame
```

### 3.5 Build Script (All Platforms)

```bash
#!/bin/bash
# build_all.sh — Build for all desktop platforms

GAME="MyGame"
COMMON_FLAGS="-c Release -p:PublishReadyToRun=false -p:TieredCompilation=false --self-contained"

echo "=== Building Windows ==="
dotnet publish $COMMON_FLAGS -r win-x64 -o "publish/windows"

echo "=== Building macOS (x64) ==="
dotnet publish $COMMON_FLAGS -r osx-x64 -o "publish/osx-x64"

echo "=== Building macOS (arm64) ==="
dotnet publish $COMMON_FLAGS -r osx-arm64 -o "publish/osx-arm64"

echo "=== Creating Universal macOS Binary ==="
mkdir -p publish/osx-universal
cp -R publish/osx-arm64/ publish/osx-universal/
lipo -create publish/osx-x64/$GAME publish/osx-arm64/$GAME \
  -output publish/osx-universal/$GAME

echo "=== Building Linux ==="
dotnet publish $COMMON_FLAGS -r linux-x64 -o "publish/linux"
chmod +x publish/linux/$GAME

echo "=== Done ==="
ls -la publish/
```

---

## 4. macOS Specifics

macOS users expect a `.app` bundle, not a naked executable. Without code signing and notarization, Gatekeeper will block the app.

### 4.1 App Bundle Structure

```
MyGame.app/
└── Contents/
    ├── Info.plist              # App metadata
    ├── MacOS/
    │   └── MyGame              # The executable (or launcher script)
    └── Resources/
        ├── MyGame.icns         # App icon
        └── Content/            # Game content (if not alongside executable)
```

### 4.2 Creating the App Bundle

```bash
#!/bin/bash
# create_macos_bundle.sh

GAME="MyGame"
APP_NAME="$GAME.app"
BUNDLE_ID="com.yourstudio.mygame"
VERSION="1.0.0"

# 1. Create bundle structure
mkdir -p "$APP_NAME/Contents/MacOS"
mkdir -p "$APP_NAME/Contents/Resources"

# 2. Copy published files into MacOS directory
cp -R publish/osx-universal/* "$APP_NAME/Contents/MacOS/"

# 3. Copy icon (create .icns from PNG using iconutil)
# See Section 4.3 for icon creation
cp MyGame.icns "$APP_NAME/Contents/Resources/"

# 4. Create Info.plist
cat > "$APP_NAME/Contents/Info.plist" << EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN"
  "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>$GAME</string>
    <key>CFBundleDisplayName</key>
    <string>$GAME</string>
    <key>CFBundleIdentifier</key>
    <string>$BUNDLE_ID</string>
    <key>CFBundleVersion</key>
    <string>$VERSION</string>
    <key>CFBundleShortVersionString</key>
    <string>$VERSION</string>
    <key>CFBundlePackagetype</key>
    <string>APPL</string>
    <key>CFBundleExecutable</key>
    <string>$GAME</string>
    <key>CFBundleIconFile</key>
    <string>$GAME</string>
    <key>LSMinimumSystemVersion</key>
    <string>10.15</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>LSApplicationCategoryType</key>
    <string>public.app-category.games</string>
</dict>
</plist>
EOF

echo "Created $APP_NAME"
```

### 4.3 Creating .icns Icon

```bash
# From a 1024x1024 PNG source
mkdir MyIcon.iconset
sips -z 16 16     icon_1024.png --out MyIcon.iconset/icon_16x16.png
sips -z 32 32     icon_1024.png --out MyIcon.iconset/icon_16x16@2x.png
sips -z 32 32     icon_1024.png --out MyIcon.iconset/icon_32x32.png
sips -z 64 64     icon_1024.png --out MyIcon.iconset/icon_32x32@2x.png
sips -z 128 128   icon_1024.png --out MyIcon.iconset/icon_128x128.png
sips -z 256 256   icon_1024.png --out MyIcon.iconset/icon_128x128@2x.png
sips -z 256 256   icon_1024.png --out MyIcon.iconset/icon_256x256.png
sips -z 512 512   icon_1024.png --out MyIcon.iconset/icon_256x256@2x.png
sips -z 512 512   icon_1024.png --out MyIcon.iconset/icon_512x512.png
sips -z 1024 1024 icon_1024.png --out MyIcon.iconset/icon_512x512@2x.png
iconutil -c icns MyIcon.iconset -o MyGame.icns
rm -rf MyIcon.iconset
```

### 4.4 Code Signing

Without code signing, macOS Gatekeeper shows "App is damaged and can't be opened" or requires users to right-click → Open.

```bash
# Sign with your Developer ID Application certificate
codesign --force --deep --options runtime \
  --sign "Developer ID Application: Your Name (TEAMID)" \
  --entitlements entitlements.plist \
  MyGame.app
```

**entitlements.plist** (minimal for games):
```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN"
  "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>com.apple.security.cs.allow-jit</key>
    <true/>
    <key>com.apple.security.cs.allow-unsigned-executable-memory</key>
    <true/>
    <key>com.apple.security.cs.disable-library-validation</key>
    <true/>
</dict>
</plist>
```

> **Why `allow-jit` and `allow-unsigned-executable-memory`?** The .NET runtime uses JIT compilation, which requires writing and executing code in memory. Without these entitlements, the hardened runtime blocks the JIT and the game crashes on launch.

> **Why `disable-library-validation`?** MonoGame loads native libraries (SDL2, OpenAL) that aren't signed with your certificate. Without this entitlement, the hardened runtime refuses to load them.

### 4.5 Notarization

Apple requires notarization for apps distributed outside the App Store. Notarization uploads your app to Apple's servers for automated malware scanning and returns a "ticket" that Gatekeeper trusts.

```bash
# 1. Create a zip for notarization
ditto -c -k --keepParent MyGame.app MyGame.zip

# 2. Submit for notarization (uses App Store Connect API key or Apple ID)
xcrun notarytool submit MyGame.zip \
  --apple-id "your@email.com" \
  --password "app-specific-password" \
  --team-id "TEAMID" \
  --wait

# 3. Check status (if not using --wait)
xcrun notarytool info <submission-id> \
  --apple-id "your@email.com" \
  --password "app-specific-password" \
  --team-id "TEAMID"

# 4. Staple the notarization ticket to the app
xcrun stapler staple MyGame.app

# 5. Verify
spctl --assess --type exec -vvv MyGame.app
```

**Using a stored keychain profile** (avoids typing credentials each time):

```bash
# One-time setup — store credentials in keychain
xcrun notarytool store-credentials "MyGameProfile" \
  --apple-id "your@email.com" \
  --password "app-specific-password" \
  --team-id "TEAMID"

# Submit using stored profile
xcrun notarytool submit MyGame.zip --keychain-profile "MyGameProfile" --wait
```

**Notarization takes 2-15 minutes.** The `--wait` flag blocks until complete. If it fails, check the log:

```bash
xcrun notarytool log <submission-id> --keychain-profile "MyGameProfile"
```

### 4.6 DMG Distribution

Package the signed, notarized `.app` in a DMG for distribution:

```bash
# Create a DMG
hdiutil create -volname "MyGame" -srcfolder MyGame.app \
  -ov -format UDZO MyGame.dmg

# Sign the DMG
codesign --sign "Developer ID Application: Your Name (TEAMID)" MyGame.dmg

# Notarize the DMG
xcrun notarytool submit MyGame.dmg --keychain-profile "MyGameProfile" --wait
xcrun stapler staple MyGame.dmg
```

---

## 5. Linux Specifics

### 5.1 Library Dependencies

MonoGame DesktopGL on Linux requires SDL2 and OpenAL. These are bundled with the MonoGame NuGet package as native libraries, but some distributions may need system packages:

```bash
# Ubuntu/Debian
sudo apt install libsdl2-2.0-0 libopenal1

# Fedora
sudo dnf install SDL2 openal-soft

# Arch Linux
sudo pacman -S sdl2 openal
```

When publishing self-contained, the native libraries from the NuGet package are included in the output directory. Most users won't need to install anything.

### 5.2 AppImage Packaging

AppImage creates a single portable executable that runs on most Linux distributions without installation:

```bash
# 1. Create AppDir structure
mkdir -p MyGame.AppDir/usr/bin
mkdir -p MyGame.AppDir/usr/share/icons/hicolor/256x256/apps

# 2. Copy published game
cp -R publish/linux/* MyGame.AppDir/usr/bin/

# 3. Create .desktop file
cat > MyGame.AppDir/MyGame.desktop << EOF
[Desktop Entry]
Type=Application
Name=MyGame
Exec=MyGame
Icon=mygame
Categories=Game;
Terminal=false
EOF

# 4. Copy icon
cp icon_256.png MyGame.AppDir/usr/share/icons/hicolor/256x256/apps/mygame.png
cp icon_256.png MyGame.AppDir/mygame.png

# 5. Create AppRun script
cat > MyGame.AppDir/AppRun << 'EOF'
#!/bin/bash
SELF=$(readlink -f "$0")
HERE=${SELF%/*}
export LD_LIBRARY_PATH="${HERE}/usr/bin:${LD_LIBRARY_PATH}"
exec "${HERE}/usr/bin/MyGame" "$@"
EOF
chmod +x MyGame.AppDir/AppRun

# 6. Download appimagetool and create AppImage
wget -q https://github.com/AppImage/AppImageKit/releases/download/continuous/appimagetool-x86_64.AppImage
chmod +x appimagetool-x86_64.AppImage
./appimagetool-x86_64.AppImage MyGame.AppDir MyGame-x86_64.AppImage
```

### 5.3 .desktop File

For manual installation or package managers:

```ini
[Desktop Entry]
Type=Application
Name=MyGame
Comment=A fantastic 2D adventure
Exec=/opt/mygame/MyGame
Icon=/opt/mygame/icon.png
Categories=Game;ActionGame;
Terminal=false
StartupWMClass=MyGame
```

Install to `/usr/share/applications/` or `~/.local/share/applications/`.

### 5.4 Steam Runtime

When distributing on Steam for Linux, Steam provides the **Steam Linux Runtime** (based on a fixed set of libraries). Your game runs inside this container, which provides consistent library versions regardless of the user's distribution.

For MonoGame self-contained builds, this usually "just works" because you bundle the .NET runtime and MonoGame bundles SDL2/OpenAL. If you depend on additional system libraries, test inside the Steam Runtime:

```bash
# Test with Steam Runtime Scout (deprecated) or Sniper
~/.steam/steam/ubuntu12_32/steam-runtime/run.sh ./MyGame
```

---

## 6. iOS Deployment

### 6.1 Project Setup

iOS builds require a separate platform project that references your Core game project:

```xml
<!-- MyGame.iOS.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-ios</TargetFramework>
    <SupportedOSPlatformVersion>15.0</SupportedOSPlatformVersion>
    <RuntimeIdentifier>ios-arm64</RuntimeIdentifier>
    <OutputType>Exe</OutputType>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="MonoGame.Framework.iOS" Version="3.8.*" />
    <PackageReference Include="MonoGame.Content.Builder.Task" Version="3.8.*" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\MyGame.Core\MyGame.Core.csproj" />
    <MonoGameContentReference Include="..\MyGame.Core\Content\Content.mgcb" />
  </ItemGroup>

  <!-- Required for .NET runtime reflection (JIT/AOT) -->
  <ItemGroup>
    <TrimmerRootAssembly Include="MonoGame.Framework" />
  </ItemGroup>
</Project>
```

### 6.2 Provisioning & Signing

iOS apps must be signed to run on physical devices. You need:

1. **Apple Developer Account** ($99/year)
2. **Signing certificate** — Development (for testing) or Distribution (for App Store/TestFlight)
3. **Provisioning profile** — links your certificate, App ID, and authorized devices

```xml
<!-- In MyGame.iOS.csproj -->
<PropertyGroup Condition="'$(Configuration)' == 'Release'">
  <CodesignKey>Apple Distribution: Your Name (TEAMID)</CodesignKey>
  <CodesignProvision>MyGame_AppStore_Profile</CodesignProvision>
</PropertyGroup>

<PropertyGroup Condition="'$(Configuration)' == 'Debug'">
  <CodesignKey>Apple Development: Your Name (TEAMID)</CodesignKey>
  <CodesignProvision>MyGame_Dev_Profile</CodesignProvision>
</PropertyGroup>
```

Or let Xcode handle signing automatically by building through Xcode after generating the project.

### 6.3 Building for Device

```bash
# Debug build to connected device
dotnet build -c Debug -r ios-arm64

# Release build (creates .ipa)
dotnet publish -c Release -r ios-arm64
```

### 6.4 TestFlight

TestFlight is Apple's beta testing platform. To submit:

1. Build a Release `.ipa`
2. Open **Xcode → Window → Organizer** (or use `xcrun altool`)
3. Upload the `.ipa` to App Store Connect
4. In App Store Connect, add testers and submit for TestFlight review

```bash
# Upload via command line
xcrun altool --upload-app \
  --type ios \
  --file MyGame.ipa \
  --apiKey "YOUR_KEY_ID" \
  --apiIssuer "YOUR_ISSUER_ID"
```

### 6.5 App Store Submission

Beyond TestFlight, full App Store submission requires:

- **Screenshots** for each supported device size (6.7", 6.1", 5.5" for iPhone; 12.9" for iPad)
- **App description, keywords, category** in App Store Connect
- **Privacy policy URL** (required even for games with no data collection)
- **Age rating questionnaire** completed in App Store Connect
- **App Review compliance** — no private API usage, no crashing, content guidelines

### 6.6 Required Info.plist Entries

```xml
<!-- Orientation (landscape game) -->
<key>UISupportedInterfaceOrientations</key>
<array>
    <string>UIInterfaceOrientationLandscapeLeft</string>
    <string>UIInterfaceOrientationLandscapeRight</string>
</array>

<!-- Prevent iPad Split View -->
<key>UIRequiresFullScreen</key>
<true/>

<!-- Required device capabilities -->
<key>UIRequiredDeviceCapabilities</key>
<array>
    <string>arm64</string>
    <string>metal</string>
</array>

<!-- Status bar (hidden for games) -->
<key>UIStatusBarHidden</key>
<true/>
<key>UIViewControllerBasedStatusBarAppearance</key>
<false/>

<!-- Launch screen (required for native resolution) -->
<key>UILaunchStoryboardName</key>
<string>LaunchScreen</string>
```

### 6.7 iOS Gotchas

- **AOT compilation is mandatory** — iOS doesn't allow JIT. .NET for iOS uses Mono AOT. Reflection-heavy code may fail.
- **Add `Arch.AOT.SourceGenerator`** — Arch ECS needs this for iOS AOT compatibility.
- **`TitleContainer.OpenStream()` is the only way to load content** — `File.ReadAllText()` won't find bundled content.
- **Metal is the only graphics API** — OpenGL ES is deprecated and removed on modern iOS. MonoGame.Framework.iOS uses Metal.
- **Maximum binary size** — App Store has a 4 GB limit. Self-contained .NET + game content typically fits well under this.

---

## 7. Android Deployment

### 7.1 Project Setup

```xml
<!-- MyGame.Android.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-android</TargetFramework>
    <SupportedOSPlatformVersion>21</SupportedOSPlatformVersion>
    <OutputType>Exe</OutputType>
    <ApplicationId>com.yourstudio.mygame</ApplicationId>
    <ApplicationVersion>1</ApplicationVersion>
    <ApplicationDisplayVersion>1.0.0</ApplicationDisplayVersion>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="MonoGame.Framework.Android" Version="3.8.*" />
    <PackageReference Include="MonoGame.Content.Builder.Task" Version="3.8.*" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\MyGame.Core\MyGame.Core.csproj" />
    <MonoGameContentReference Include="..\MyGame.Core\Content\Content.mgcb" />
  </ItemGroup>
</Project>
```

### 7.2 APK vs AAB

| Format | Use Case | Size Limit |
|--------|----------|-----------|
| **APK** (Android Package) | Direct distribution (itch.io, sideloading) | 150 MB (Google Play), no limit for direct |
| **AAB** (Android App Bundle) | Google Play Store (required since 2021) | 150 MB base + 2 GB via Play Asset Delivery |

```bash
# Build APK (for itch.io, sideloading)
dotnet publish -c Release -r android-arm64 -f net8.0-android

# Build AAB (for Google Play)
dotnet publish -c Release -r android-arm64 -f net8.0-android \
  -p:AndroidPackageFormat=aab
```

### 7.3 Signing

Android requires all APKs/AABs to be signed for installation.

**Generate a keystore** (one-time):
```bash
keytool -genkey -v \
  -keystore mygame-release.keystore \
  -alias mygame \
  -keyalg RSA -keysize 2048 \
  -validity 10000
```

> **Back up your keystore!** If you lose it, you can never update your app on Google Play.

**Configure signing in .csproj:**
```xml
<PropertyGroup Condition="'$(Configuration)' == 'Release'">
  <AndroidKeyStore>true</AndroidKeyStore>
  <AndroidSigningKeyStore>mygame-release.keystore</AndroidSigningKeyStore>
  <AndroidSigningKeyAlias>mygame</AndroidSigningKeyAlias>
  <AndroidSigningKeyPass>your-password</AndroidSigningKeyPass>
  <AndroidSigningStorePass>your-password</AndroidSigningStorePass>
</PropertyGroup>
```

> **Security:** Don't commit passwords to source control. Use environment variables or CI secrets:
> ```xml
> <AndroidSigningKeyPass>$(ANDROID_KEY_PASS)</AndroidSigningKeyPass>
> ```

### 7.4 Target SDK Requirements

Google Play requires targeting recent Android SDK versions:

```xml
<PropertyGroup>
  <!-- Minimum API level (Android 5.0) -->
  <SupportedOSPlatformVersion>21</SupportedOSPlatformVersion>
  <!-- Target API level (must meet Google Play's annual requirement) -->
  <TargetSdkVersion>34</TargetSdkVersion>
</PropertyGroup>
```

Google Play's target SDK requirement typically increases annually. Check the [Google Play target API level requirements](https://developer.android.com/google/play/requirements/target-sdk) for the current minimum.

### 7.5 Google Play Submission

1. Create a developer account ($25 one-time fee)
2. Create an app in Google Play Console
3. Upload signed AAB
4. Fill out store listing (description, screenshots, feature graphic)
5. Complete content rating questionnaire
6. Set pricing and distribution countries
7. Submit for review

### 7.6 Android Gotchas

- **Case-sensitive file paths** — Android's filesystem is case-sensitive. `Content/Sprites/Player.png` ≠ `Content/sprites/player.png`.
- **OpenGL ES** — Android uses OpenGL ES, not desktop OpenGL. Some shader features may differ.
- **Multiple architectures** — Ship `android-arm64` at minimum. Add `android-arm` for older 32-bit devices if needed.
- **Content inside APK** — Use `TitleContainer.OpenStream()` for all content access (same as iOS).
- **Screen sizes vary wildly** — Test on multiple aspect ratios. See [G25 Safe Areas](./G25_safe_areas_adaptive_layout.md).

---

## 8. Steam Integration

### 8.1 Steamworks.NET Setup

[Steamworks.NET](https://github.com/rlabrecque/Steamworks.NET) is a C# wrapper for the Steamworks SDK.

```bash
dotnet add package Steamworks.NET --version 20.2.0
```

### 8.2 Initialization

```csharp
using Steamworks;

public class SteamManager
{
    private static bool _initialized;

    public static bool Initialize()
    {
        try
        {
            if (!Packsize.Test())
            {
                Debug.WriteLine("[Steam] Packsize test failed — wrong Steamworks.NET version");
                return false;
            }

            if (!DllCheck.Test())
            {
                Debug.WriteLine("[Steam] DllCheck failed — native library mismatch");
                return false;
            }

            _initialized = SteamAPI.Init();

            if (!_initialized)
            {
                Debug.WriteLine("[Steam] SteamAPI.Init() failed — is Steam running?");
                return false;
            }

            Debug.WriteLine($"[Steam] Initialized. User: {SteamFriends.GetPersonaName()}");
            return true;
        }
        catch (DllNotFoundException e)
        {
            Debug.WriteLine($"[Steam] Native library not found: {e.Message}");
            return false;
        }
    }

    public static void Update()
    {
        if (_initialized)
            SteamAPI.RunCallbacks();
    }

    public static void Shutdown()
    {
        if (_initialized)
            SteamAPI.Shutdown();
    }
}
```

Call `SteamManager.Update()` every frame in your game loop. Call `SteamManager.Shutdown()` on exit.

### 8.3 steam_appid.txt

During development, create `steam_appid.txt` in your build output directory containing your App ID (or `480` for the Spacewar test app):

```
480
```

This file tells the Steam API which app you're running when launched outside of Steam. **Do not ship this file** — Steam provides the App ID automatically when launched through the Steam client.

```xml
<!-- In .csproj — copy during Debug, exclude from Release -->
<ItemGroup Condition="'$(Configuration)' == 'Debug'">
  <None Include="steam_appid.txt" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

### 8.4 Steam Overlay

The Steam overlay (Shift+Tab) works automatically with DesktopGL. Ensure your game doesn't block it:

```csharp
// Don't grab the mouse exclusively — prevents overlay from appearing
// This is handled correctly by default in MonoGame DesktopGL

// Register for overlay activation (pause the game when overlay opens)
Callback<GameOverlayActivated_t>.Create(data =>
{
    if (data.m_bActive != 0)
        PauseGame();
    else
        ResumeGame();
});
```

### 8.5 Achievements

```csharp
public static void UnlockAchievement(string achievementId)
{
    if (!_initialized) return;

    SteamUserStats.SetAchievement(achievementId);
    SteamUserStats.StoreStats(); // Upload to Steam servers
}

public static bool IsAchievementUnlocked(string achievementId)
{
    if (!_initialized) return false;

    SteamUserStats.GetAchievement(achievementId, out bool achieved);
    return achieved;
}

// Usage
SteamManager.UnlockAchievement("ACH_FIRST_BOSS");
```

Define achievements in the Steamworks partner portal under **App Admin → Stats & Achievements**.

### 8.6 Cloud Saves

```csharp
public static bool CloudSave(string fileName, byte[] data)
{
    if (!_initialized) return false;
    return SteamRemoteStorage.FileWrite(fileName, data, data.Length);
}

public static byte[] CloudLoad(string fileName)
{
    if (!_initialized) return null;

    int size = SteamRemoteStorage.GetFileSize(fileName);
    if (size <= 0) return null;

    byte[] data = new byte[size];
    SteamRemoteStorage.FileRead(fileName, data, size);
    return data;
}

// Usage
string saveJson = JsonSerializer.Serialize(saveData);
SteamManager.CloudSave("save1.json", Encoding.UTF8.GetBytes(saveJson));
```

Enable Cloud Saves in the Steamworks partner portal under **App Admin → Cloud**.

### 8.7 Depot Configuration

Steam uses depots to organize your game's files. Configure in the Steamworks partner portal:

**App Configuration:**
```vdf
"appid" "YOUR_APP_ID"
"desc" "MyGame"
"depots"
{
    "YOUR_DEPOT_ID_WINDOWS" "Windows Depot"
    "YOUR_DEPOT_ID_MAC" "macOS Depot"
    "YOUR_DEPOT_ID_LINUX" "Linux Depot"
}
```

**Depot build script** (`depot_build_windows.vdf`):
```vdf
"DepotBuildConfig"
{
    "DepotID" "YOUR_DEPOT_ID_WINDOWS"
    "contentroot" "publish\windows"
    "FileMapping"
    {
        "LocalPath" "*"
        "DepotPath" "."
        "recursive" "1"
    }
    "FileExclusion" "*.pdb"
}
```

Upload via SteamCMD:
```bash
steamcmd +login your_steam_username +run_app_build depot_build.vdf +quit
```

---

## 9. itch.io Publishing

### 9.1 butler CLI

[butler](https://itch.io/docs/butler/) is itch.io's command-line tool for uploading builds. It handles delta patching, versioning, and channel management.

```bash
# Install butler
# macOS
brew install butler

# Or download from https://itch.io/docs/butler/installing.html

# Login (one-time)
butler login
```

### 9.2 Channel Naming Convention

Channels identify platform builds. itch.io auto-detects the platform from channel names:

| Channel Name | Platform Detection | Downloads As |
|-------------|-------------------|--------------|
| `windows` | Windows | `.zip` download |
| `mac` or `osx` | macOS | `.zip` download |
| `linux` | Linux | `.zip` download |
| `android` | Android | `.apk` download |

### 9.3 Pushing Builds

```bash
# Push Windows build
butler push publish/windows yourusername/mygame:windows --userversion 1.0.0

# Push macOS build (zip the .app bundle)
butler push MyGame.app yourusername/mygame:mac --userversion 1.0.0

# Push Linux build
butler push publish/linux yourusername/mygame:linux --userversion 1.0.0

# Push Android APK
butler push MyGame.apk yourusername/mygame:android --userversion 1.0.0
```

### 9.4 Versioning

butler tracks versions automatically. Use `--userversion` for human-readable versions:

```bash
# Semantic versioning
butler push publish/windows yourusername/mygame:windows --userversion 1.2.3

# Or let butler auto-increment
butler push publish/windows yourusername/mygame:windows
```

### 9.5 Build Script for itch.io

```bash
#!/bin/bash
# push_itch.sh — Build and push all platforms to itch.io

ITCH_USER="yourusername"
GAME="mygame"
VERSION=$(git describe --tags --always)

echo "=== Pushing v$VERSION to itch.io ==="

butler push publish/windows "$ITCH_USER/$GAME:windows" --userversion "$VERSION"
butler push MyGame.app "$ITCH_USER/$GAME:mac" --userversion "$VERSION"
butler push publish/linux "$ITCH_USER/$GAME:linux" --userversion "$VERSION"

echo "=== Done ==="
butler status "$ITCH_USER/$GAME"
```

### 9.6 itch.io Page Setup

In your game's itch.io dashboard:

- **Kind of project:** Downloadable
- **Pricing:** Set your price or "No payments" / "Name your own price"
- **Uploads:** butler-pushed channels appear automatically with platform tags
- **Minimum system requirements:** Add to description
- **Screenshots:** Upload 3-5 gameplay screenshots (recommended 1920x1080)
- **Cover image:** 315x250 minimum, 630x500 recommended

---

## 10. Version Management

### 10.1 Assembly Versioning

```xml
<!-- In MyGame.Core.csproj or Directory.Build.props -->
<PropertyGroup>
  <Version>1.0.0</Version>
  <AssemblyVersion>1.0.0.0</AssemblyVersion>
  <FileVersion>1.0.0.0</FileVersion>
  <InformationalVersion>1.0.0-dev</InformationalVersion>
</PropertyGroup>
```

| Property | Format | Purpose |
|----------|--------|---------|
| `Version` | `Major.Minor.Patch` | NuGet version, default for others |
| `AssemblyVersion` | `Major.Minor.Build.Revision` | .NET assembly identity |
| `FileVersion` | `Major.Minor.Build.Revision` | Windows file properties |
| `InformationalVersion` | Any string | Display version (shown in About screens) |

### 10.2 Git-Based Version Injection

Automatically set the version from git tags during build:

```xml
<!-- Directory.Build.props (at solution root) -->
<Project>
  <PropertyGroup>
    <MinVerSkip Condition="'$(Configuration)' == 'Debug'">true</MinVerSkip>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="MinVer" Version="5.*" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

[MinVer](https://github.com/adamralph/minver) reads git tags (e.g., `v1.2.3`) and sets all version properties automatically. Tag a commit with `git tag v1.2.3` and MinVer handles the rest.

**Alternative — manual git describe:**

```xml
<!-- In .csproj -->
<Target Name="SetVersionFromGit" BeforeTargets="PrepareForBuild"
        Condition="'$(Configuration)' == 'Release'">
  <Exec Command="git describe --tags --always" ConsoleToMsBuild="true">
    <Output TaskParameter="ConsoleOutput" PropertyName="GitVersion" />
  </Exec>
  <PropertyGroup>
    <InformationalVersion>$(GitVersion)</InformationalVersion>
  </PropertyGroup>
</Target>
```

### 10.3 Displaying Version In-Game

```csharp
public static string GetVersion()
{
    var assembly = typeof(GameApp).Assembly;
    var infoVersion = assembly.GetCustomAttribute<
        System.Reflection.AssemblyInformationalVersionAttribute>();

    return infoVersion?.InformationalVersion ?? "dev";
}

// Draw in corner of title screen
spriteBatch.DrawString(font, $"v{GetVersion()}", new Vector2(10, 10), Color.Gray);
```

### 10.4 Build Numbers for Mobile

iOS and Android require incrementing integer build numbers:

```xml
<!-- iOS -->
<PropertyGroup>
  <!-- CFBundleVersion — must increment for each TestFlight/App Store upload -->
  <ApplicationVersion>42</ApplicationVersion>
  <!-- CFBundleShortVersionString — human-readable -->
  <ApplicationDisplayVersion>1.2.3</ApplicationDisplayVersion>
</PropertyGroup>

<!-- Android -->
<PropertyGroup>
  <!-- android:versionCode — must increment for each Google Play upload -->
  <ApplicationVersion>42</ApplicationVersion>
  <!-- android:versionName — human-readable -->
  <ApplicationDisplayVersion>1.2.3</ApplicationDisplayVersion>
</PropertyGroup>
```

Automate build number from CI run count:

```xml
<PropertyGroup>
  <ApplicationVersion>$(GITHUB_RUN_NUMBER)</ApplicationVersion>
</PropertyGroup>
```

---

## 11. CI/CD Pipeline

### 11.1 GitHub Actions — Multi-Platform Build

```yaml
# .github/workflows/build.yml
name: Build & Release

on:
  push:
    tags: ['v*']
  workflow_dispatch:

jobs:
  build:
    strategy:
      matrix:
        include:
          - os: windows-latest
            rid: win-x64
            artifact: windows
          - os: macos-latest
            rid: osx-arm64
            artifact: mac-arm64
          - os: macos-13
            rid: osx-x64
            artifact: mac-x64
          - os: ubuntu-latest
            rid: linux-x64
            artifact: linux

    runs-on: ${{ matrix.os }}

    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'

      - name: Restore
        run: dotnet restore

      - name: Publish
        run: >
          dotnet publish -c Release
          -r ${{ matrix.rid }}
          -p:PublishReadyToRun=false
          -p:TieredCompilation=false
          --self-contained
          -o publish/${{ matrix.artifact }}

      - name: Upload Artifact
        uses: actions/upload-artifact@v4
        with:
          name: ${{ matrix.artifact }}
          path: publish/${{ matrix.artifact }}

  # Create universal macOS binary
  mac-universal:
    needs: build
    runs-on: macos-latest
    steps:
      - name: Download mac-arm64
        uses: actions/download-artifact@v4
        with:
          name: mac-arm64
          path: mac-arm64

      - name: Download mac-x64
        uses: actions/download-artifact@v4
        with:
          name: mac-x64
          path: mac-x64

      - name: Create Universal Binary
        run: |
          cp -R mac-arm64 mac-universal
          GAME_NAME=$(ls mac-arm64 | grep -v '\.dll$\|\.json$\|\.pdb$\|Content' | head -1)
          lipo -create "mac-arm64/$GAME_NAME" "mac-x64/$GAME_NAME" \
            -output "mac-universal/$GAME_NAME"

      - name: Upload Universal Artifact
        uses: actions/upload-artifact@v4
        with:
          name: mac-universal
          path: mac-universal

  # Create GitHub Release
  release:
    needs: [build, mac-universal]
    runs-on: ubuntu-latest
    if: startsWith(github.ref, 'refs/tags/v')

    steps:
      - name: Download All Artifacts
        uses: actions/download-artifact@v4

      - name: Create Archives
        run: |
          cd windows && zip -r ../MyGame-windows.zip . && cd ..
          cd mac-universal && zip -r ../MyGame-mac.zip . && cd ..
          cd linux && tar czf ../MyGame-linux.tar.gz . && cd ..

      - name: Create Release
        uses: softprops/action-gh-release@v1
        with:
          files: |
            MyGame-windows.zip
            MyGame-mac.zip
            MyGame-linux.tar.gz
          generate_release_notes: true
```

### 11.2 itch.io Deploy Step

Add to the release job:

```yaml
      - name: Push to itch.io
        env:
          BUTLER_API_KEY: ${{ secrets.BUTLER_API_KEY }}
        run: |
          curl -L -o butler.zip https://broth.itch.ovh/butler/linux-amd64/LATEST/archive/default
          unzip butler.zip && chmod +x butler
          ./butler push windows yourusername/mygame:windows \
            --userversion ${GITHUB_REF#refs/tags/v}
          ./butler push mac-universal yourusername/mygame:mac \
            --userversion ${GITHUB_REF#refs/tags/v}
          ./butler push linux yourusername/mygame:linux \
            --userversion ${GITHUB_REF#refs/tags/v}
```

### 11.3 Steam Deploy Step

```yaml
      - name: Push to Steam
        env:
          STEAM_USERNAME: ${{ secrets.STEAM_USERNAME }}
          STEAM_CONFIG_VDF: ${{ secrets.STEAM_CONFIG_VDF }}
        run: |
          mkdir -p ~/.steam
          echo "$STEAM_CONFIG_VDF" | base64 -d > ~/.steam/config.vdf
          steamcmd +login $STEAM_USERNAME +run_app_build depot_build.vdf +quit
```

> **Steam CI authentication** is complex. Consider using [game-ci/steam-deploy](https://github.com/game-ci/steam-deploy) GitHub Action or managing SteamCMD auth tokens via Steamworks partner portal.

---

## 12. Distribution Checklist

### 12.1 Pre-Release Testing

- [ ] **Fresh machine test** — install on a machine that has never had .NET or your game. Does it launch?
- [ ] **Antivirus test** — some AV flags .NET self-contained builds. Submit false-positive reports to major AV vendors.
- [ ] **All platforms tested** — Windows, macOS (Intel + Apple Silicon), Linux
- [ ] **Content complete** — no missing textures, audio, or data files
- [ ] **Save/load cycle** — save a game, quit, relaunch, load. Does it work?
- [ ] **Resolution test** — 1080p, 1440p, 4K, ultrawide, Steam Deck (1280x800)
- [ ] **Fullscreen toggle** — windowed ↔ fullscreen without crash
- [ ] **Controller support** — if applicable, test with Xbox controller, PS controller, Steam Deck controls
- [ ] **Crash handling** — does the game show a graceful error or silently crash?

### 12.2 Files to Include

| File | Purpose | Required? |
|------|---------|-----------|
| `README.txt` | Basic instructions, controls, known issues | ✅ Yes |
| `LICENSE.txt` | Your game's license (proprietary or open source) | ✅ Yes |
| `THIRD_PARTY_LICENSES.txt` | Licenses for MonoGame, Arch, libraries, fonts, assets | ✅ Yes |
| `CHANGELOG.txt` | Version history | Recommended |

### 12.3 Third-Party License Compliance

MonoGame (Ms-PL), Arch (Apache 2.0), SDL2 (zlib), OpenAL Soft (LGPL 2.1), FontStashSharp (MIT), etc. — each has license terms. Create a `THIRD_PARTY_LICENSES.txt`:

```text
This game uses the following open-source software:

MonoGame Framework — Microsoft Public License (Ms-PL)
https://github.com/MonoGame/MonoGame/blob/develop/LICENSE.txt

Arch ECS — Apache License 2.0
https://github.com/genaray/Arch/blob/master/LICENSE

SDL2 — zlib License
https://www.libsdl.org/license.php

FontStashSharp — MIT License
https://github.com/FontStashSharp/FontStashSharp/blob/main/LICENSE

[... additional libraries, fonts, sound effects, etc.]
```

### 12.4 Minimum System Requirements

Define and publish these:

```
MINIMUM SYSTEM REQUIREMENTS
---
OS: Windows 10 64-bit / macOS 10.15+ / Ubuntu 20.04+
Processor: Any 64-bit CPU (x64 or ARM64 on macOS)
Memory: 2 GB RAM
Graphics: OpenGL 3.0 compatible
Storage: [X] MB available space
```

For 2D MonoGame games, requirements are extremely low. Be honest — don't list higher specs than needed.

---

## 13. Troubleshooting

### 13.1 Common Deployment Failures

| Problem | Cause | Fix |
|---------|-------|-----|
| **"App is damaged and can't be opened" (macOS)** | Not code-signed or notarized | Sign and notarize the .app bundle (see Section 4) |
| **Game launches then immediately closes** | Missing native library (SDL2, OpenAL) | Check publish output includes native libs; verify self-contained build |
| **`ContentLoadException: file not found`** | Content not copied to output, or case mismatch | Verify `MonoGameContentReference` in .csproj; check case sensitivity on Linux |
| **White/pink screen on launch** | Content directory missing entirely | Ensure Content/ folder is alongside the executable in the publish output |
| **Micro-stutters during first 2 minutes** | ReadyToRun or TieredCompilation enabled | Set `-p:PublishReadyToRun=false -p:TieredCompilation=false` |
| **`DllNotFoundException: libSDL2`** | SDL2 native library not found | DesktopGL NuGet should bundle it; verify `runtimes/` folder in output |
| **`DllNotFoundException: steam_api64`** | Steamworks native DLL missing | Copy `steam_api64.dll` / `libsteam_api.so` / `libsteam_api.dylib` to output |
| **Steam overlay not appearing** | Game not launched through Steam, or overlay disabled | Launch via Steam client; check Steam settings → In-Game → Enable overlay |
| **Black screen on Linux** | OpenGL driver issues | Install latest Mesa drivers; try `MESA_GL_VERSION_OVERRIDE=3.3` |
| **App crashes on iOS launch** | Trimmer removed reflection targets | Add `<TrimmerRootAssembly Include="MonoGame.Framework" />` to .csproj |
| **Android crash: `java.lang.UnsatisfiedLinkError`** | Wrong architecture or missing native lib | Ensure building for correct RID (`android-arm64`); check native lib inclusion |
| **itch.io "No compatible downloads"** | Wrong channel name | Use `windows`, `mac`, `linux` as channel names for auto-detection |

### 13.2 Diagnosing Missing Content

```csharp
// Add to game startup to verify content directory
protected override void Initialize()
{
    string contentPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content");
    Debug.WriteLine($"Content path: {contentPath}");
    Debug.WriteLine($"Content exists: {Directory.Exists(contentPath)}");

    if (Directory.Exists(contentPath))
    {
        foreach (string file in Directory.GetFiles(contentPath, "*", SearchOption.AllDirectories))
            Debug.WriteLine($"  {file}");
    }

    base.Initialize();
}
```

### 13.3 Platform-Specific Debugging

**Windows:**
```bash
# Run from command line to see console output
cd publish/windows
.\MyGame.exe
```

**macOS:**
```bash
# Run the binary directly (bypass Gatekeeper for testing)
cd MyGame.app/Contents/MacOS
./MyGame

# Check crash logs
open ~/Library/Logs/DiagnosticReports/
```

**Linux:**
```bash
# Check for missing shared libraries
cd publish/linux
ldd ./MyGame | grep "not found"

# Run with debug output
MONO_LOG_LEVEL=debug ./MyGame
```

### 13.4 Content Build Errors in CI

The most common CI failure is content not building because MGCB tools or pipeline extensions aren't installed:

```yaml
# Ensure MGCB tools are available
- name: Install MGCB
  run: |
    dotnet tool install --global dotnet-mgcb
    dotnet tool install --global dotnet-mgcb-editor

# If using custom pipeline extensions, restore them first
- name: Restore
  run: dotnet restore
```

### 13.5 Self-Contained Build Size Reduction

If your self-contained build is too large:

| Technique | Savings | Risk |
|-----------|---------|------|
| `PublishTrimmed=true` + `TrimMode=partial` | 20-40 MB | Low (safe for most code) |
| `PublishTrimmed=true` + `TrimMode=full` | 30-50 MB | Medium (may break reflection) |
| `PublishSingleFile=true` | No size reduction, but single exe | Low (convenience, not size) |
| Compress with 7z/zip instead of raw folder | 30-50% smaller download | None |
| Remove `.pdb` files | 5-15 MB | Lose stack traces in crash reports |

```xml
<PropertyGroup Condition="'$(Configuration)' == 'Release'">
  <DebugSymbols>false</DebugSymbols>
  <DebugType>none</DebugType>
</PropertyGroup>
```

---

## Quick Reference

### Publish Commands Cheat Sheet

```bash
# Common flags (always use these)
FLAGS="-c Release -p:PublishReadyToRun=false -p:TieredCompilation=false --self-contained"

# Windows
dotnet publish $FLAGS -r win-x64 -o publish/windows

# macOS Intel
dotnet publish $FLAGS -r osx-x64 -o publish/osx-x64

# macOS Apple Silicon
dotnet publish $FLAGS -r osx-arm64 -o publish/osx-arm64

# Linux
dotnet publish $FLAGS -r linux-x64 -o publish/linux

# iOS
dotnet publish -c Release -r ios-arm64

# Android APK
dotnet publish -c Release -r android-arm64 -f net8.0-android

# Android AAB (Google Play)
dotnet publish -c Release -r android-arm64 -f net8.0-android -p:AndroidPackageFormat=aab
```

### Distribution Targets

| Platform | Tool | Command |
|----------|------|---------|
| Steam | SteamCMD | `steamcmd +login user +run_app_build depot.vdf +quit` |
| itch.io | butler | `butler push folder user/game:channel --userversion X.Y.Z` |
| GitHub | gh CLI | `gh release create vX.Y.Z *.zip --generate-notes` |
| App Store | xcrun | `xcrun altool --upload-app --file MyGame.ipa` |
| Google Play | Console | Upload AAB via Google Play Console |

### Critical Flags Summary

| Flag | Value | Why |
|------|-------|-----|
| `PublishReadyToRun` | `false` | Prevents JIT stutter from partial R2R coverage |
| `TieredCompilation` | `false` | Prevents recompilation hitches during gameplay |
| `--self-contained` | Always | Players don't need .NET installed |
| `PublishTrimmed` | `true` (optional) | Reduces bundle size by removing unused code |
| `TrimMode` | `partial` | Safe trimming that preserves reflection |
