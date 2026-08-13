# 17 — Release Build Pipeline



A step-by-step guide for building, testing, signing, and uploading release builds of your MonoGame DesktopGL game. This covers everything from `dotnet publish` to getting your game on Steam and itch.io.

> **Related docs:**
> - [G32 — Deployment & Platform Builds](../../monogame-arch/guides/G32_deployment_platform_builds.md)
> - [G36 — Publishing & Distribution](../../monogame-arch/guides/G36_publishing_distribution.md)
> - [G51 — Crash Reporting](../../monogame-arch/guides/G51_crash_reporting.md)

---

## 1. Build Configurations

### Debug vs Release vs Distribution

| Configuration | Purpose | Optimizations | Debug Symbols | Assertions |
|---|---|---|---|---|
| **Debug** | Day-to-day development | None | Full | Enabled |
| **Release** | Testing pre-release builds | Enabled | Portable PDB | Disabled |
| **Distribution** | Final shipping build | Full + trimming | None | Disabled |

**Debug** is what you use during development. It's slow but gives you full stack traces, hot reload, and breakpoints. Never ship this.

**Release** enables compiler optimizations and strips most debug info. Use this for playtesting and beta builds.

**Distribution** is a custom configuration you create for the final shipping build — it adds trimming, single-file publish, and removes all debug artifacts.

### Creating a Distribution Configuration

Add this to your `.csproj`:

```xml
<PropertyGroup Condition="'$(Configuration)' == 'Distribution'">
  <Optimize>true</Optimize>
  <DebugType>none</DebugType>
  <DebugSymbols>false</DebugSymbols>
  <DefineConstants>DISTRIBUTION</DefineConstants>
</PropertyGroup>
```

Now you can wrap debug-only code:

```csharp
#if !DISTRIBUTION
    DrawDebugOverlay(spriteBatch);
#endif
```

### .NET Publish Basics

The core command:

```bash
dotnet publish -c Release -r <runtime-id> --self-contained
```

Key flags:

| Flag | What It Does |
|---|---|
| `-c Release` | Build configuration |
| `-r win-x64` | Target runtime identifier |
| `--self-contained` | Bundle .NET runtime (players don't need .NET installed) |
| `-p:PublishSingleFile=true` | Pack into one executable |
| `-p:PublishTrimmed=true` | Remove unused framework code |
| `-p:IncludeNativeLibrariesForSelfExtract=true` | Bundle native libs into single file |
| `-o ./build/win` | Output directory |

### Self-Contained vs Framework-Dependent

**Always use self-contained for game releases.** Players should never have to install .NET. The tradeoff is a larger file size (~30-60 MB overhead), but that's negligible for a game.

Framework-dependent builds are fine for internal testing or dev tools.

### Single-File Publish

Single-file bundles your entire app into one executable. Players see one `.exe` (plus your `Content/` folder) instead of dozens of DLLs.

```xml
<!-- In your .csproj -->
<PropertyGroup Condition="'$(Configuration)' == 'Distribution'">
  <PublishSingleFile>true</PublishSingleFile>
  <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
  <IncludeAllContentForSelfExtract>true</IncludeAllContentForSelfExtract>
</PropertyGroup>
```

### Trimming Considerations

Trimming removes unused .NET framework code, reducing file size by 20-50%. But it can break things.

**Safe for MonoGame:** General trimming works fine for most game code.

**Watch out for:**
- Reflection-heavy code (JSON serializers, plugin systems)
- `Type.GetType()` calls with string names
- Any runtime assembly loading

If trimming breaks something, add trim roots:

```xml
<ItemGroup>
  <TrimmerRootAssembly Include="YourGame" />
  <TrimmerRootAssembly Include="MonoGame.Framework" />
</ItemGroup>
```

**Recommendation:** Test trimmed builds thoroughly. If you hit weird runtime errors that don't appear in Debug, trimming is usually the culprit. When in doubt, skip trimming — the size difference isn't worth shipping a broken game.

---

## 2. Windows Build

### Step-by-Step

**1. Publish the build:**

```bash
dotnet publish -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o ./build/windows
```

**2. Verify output folder structure:**

```
build/windows/
├── YourGame.exe          # Main executable
├── Content/              # All game assets
│   ├── Fonts/
│   ├── Textures/
│   ├── Audio/
│   └── ...
└── (possibly some native .dlls)
```

**3. Test the build:**

Run `YourGame.exe` directly from the output folder. Make sure:
- The game launches without errors
- All assets load (textures, audio, fonts)
- Save/load works
- No console window appears (see below)

**4. Hide the console window:**

Add to your `.csproj`:

```xml
<PropertyGroup>
  <OutputType>WinExe</OutputType>
</PropertyGroup>
```

### Testing on a Clean Machine

This is **critical**. Your dev machine has .NET, Visual Studio runtimes, and who knows what else installed. A player's machine doesn't.

Options:
- Use a VM (Hyper-V, VirtualBox) with a fresh Windows install
- Use Windows Sandbox (built into Windows 10/11 Pro)
- Ask a friend to test it

### Common Windows Issues

**"VCRUNTIME140.dll not found"**
Your game needs the Visual C++ Redistributable. Options:
- Bundle `vc_redist.x64.exe` with your installer
- Use `IncludeNativeLibrariesForSelfExtract=true` to bundle it

**Content folder not found**
The `Content/` directory must be next to the executable. Verify your `.csproj` copies content:

```xml
<ItemGroup>
  <Content Include="Content\**\*.*">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
</ItemGroup>
```

**Game crashes on launch with no error**
Usually a missing native library. Run from command line to see the error:

```cmd
cd build\windows
YourGame.exe
```

### Code Signing (Optional)

Without signing, Windows shows "Windows protected your PC" (SmartScreen warning). Players can still run it, but it looks sketchy.

**Free option:** None, unfortunately. Code signing certificates cost $200-400/year.

**If you have a certificate:**

```powershell
# Using signtool (from Windows SDK)
signtool sign /f YourCert.pfx /p "password" /t http://timestamp.digicert.com /fd sha256 build\windows\YourGame.exe
```

**Practical advice for indie devs:** Skip code signing for your first release. Most players know how to click through SmartScreen. If you're on Steam, Steam's launcher bypasses this entirely.

### Creating an Installer (Inno Setup)

[Inno Setup](https://jrsoftware.org/isinfo.php) is free and creates professional Windows installers.

**1. Download and install Inno Setup.**

**2. Create a script (`installer.iss`):**

```iss
[Setup]
AppName=Your Game
AppVersion=1.0.0
DefaultDirName={autopf}\YourGame
DefaultGroupName=Your Game
OutputDir=.\installer
OutputBaseFilename=YourGame-Setup-1.0.0
Compression=lzma2
SolidCompression=yes
UninstallDisplayIcon={app}\YourGame.exe

[Files]
Source: "build\windows\*"; DestDir: "{app}"; Flags: recursesubdirs

[Icons]
Name: "{group}\Your Game"; Filename: "{app}\YourGame.exe"
Name: "{commondesktop}\Your Game"; Filename: "{app}\YourGame.exe"

[Run]
Filename: "{app}\YourGame.exe"; Description: "Launch Your Game"; Flags: postinstall nowait
```

**3. Build the installer:**

```bash
# From command line (Inno Setup must be in PATH)
iscc installer.iss
```

Output: `installer/YourGame-Setup-1.0.0.exe`

---

## 3. Linux Build

### Step-by-Step

**1. Publish the build:**

```bash
dotnet publish -c Release -r linux-x64 --self-contained \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o ./build/linux
```

**2. Make the binary executable:**

```bash
chmod +x build/linux/YourGame
```

**3. Verify output folder structure:**

```
build/linux/
├── YourGame              # Main executable (no extension)
├── Content/
│   ├── Fonts/
│   ├── Textures/
│   ├── Audio/
│   └── ...
└── (native .so files if not single-file)
```

### Creating an AppImage

AppImage is the most portable Linux distribution format — one file, runs on most distros.

**1. Download `appimagetool`:**

```bash
wget https://github.com/AppImage/AppImageKit/releases/download/continuous/appimagetool-x86_64.AppImage
chmod +x appimagetool-x86_64.AppImage
```

**2. Create AppDir structure:**

```bash
mkdir -p AppDir/usr/bin AppDir/usr/share/icons/hicolor/256x256/apps

# Copy your build
cp -r build/linux/* AppDir/usr/bin/

# Add your game icon (256x256 PNG)
cp icon.png AppDir/usr/share/icons/hicolor/256x256/apps/yourgame.png
cp icon.png AppDir/yourgame.png
```

**3. Create desktop entry (`AppDir/yourgame.desktop`):**

```ini
[Desktop Entry]
Type=Application
Name=Your Game
Exec=YourGame
Icon=yourgame
Categories=Game;
```

**4. Create AppRun script (`AppDir/AppRun`):**

```bash
#!/bin/bash
HERE="$(dirname "$(readlink -f "$0")")"
exec "$HERE/usr/bin/YourGame" "$@"
```

```bash
chmod +x AppDir/AppRun
```

**5. Build the AppImage:**

```bash
./appimagetool-x86_64.AppImage AppDir YourGame-1.0.0-x86_64.AppImage
```

### Testing

- **WSL2:** Works for basic testing but doesn't support GPU acceleration well. Good for "does it launch?"
- **VM:** Use Ubuntu LTS in VirtualBox/VMware. Best for realistic testing.
- **Docker:** Doesn't support GUI without extra setup. Not recommended.

### Steam Runtime Considerations

If shipping on Steam for Linux, your game runs inside the Steam Linux Runtime (a container with fixed libraries). This usually *helps* compatibility, but test with:

```bash
# In Steam, set launch options:
PROTON_LOG=1 %command%
```

### Common Linux Issues

**Missing `libSDL2`**
MonoGame DesktopGL needs SDL2. On most distros it's pre-installed, but if not:

```bash
# Ubuntu/Debian
sudo apt install libsdl2-2.0-0

# Fedora
sudo dnf install SDL2
```

For AppImage, bundle `libSDL2-2.0.so.0` in your AppDir.

**Case-sensitive file paths**
Linux file systems are case-sensitive. `Content/Textures/Player.png` ≠ `Content/textures/player.png`. This is the #1 bug when a game works on Windows but crashes on Linux.

**Fix:** Be consistent with casing everywhere. Lowercase everything is simplest.

**Missing OpenAL**
MonoGame uses OpenAL for audio:

```bash
sudo apt install libopenal1
```

---

## 4. macOS Build

### Step-by-Step

**1. Publish for both architectures:**

```bash
# Intel Macs
dotnet publish -c Release -r osx-x64 --self-contained \
  -p:PublishSingleFile=true \
  -o ./build/osx-x64

# Apple Silicon Macs
dotnet publish -c Release -r osx-arm64 --self-contained \
  -p:PublishSingleFile=true \
  -o ./build/osx-arm64
```

**2. Create a universal binary (optional but recommended):**

```bash
# Combine with lipo
lipo -create build/osx-x64/YourGame build/osx-arm64/YourGame \
  -output build/osx-universal/YourGame
```

> **Note:** Universal binaries with single-file publish can be tricky. An alternative is to ship separate Intel and Apple Silicon builds.

### Creating a .app Bundle

macOS apps are actually folders with a specific structure:

```bash
mkdir -p "build/YourGame.app/Contents/MacOS"
mkdir -p "build/YourGame.app/Contents/Resources"

# Copy the binary
cp build/osx-arm64/YourGame "build/YourGame.app/Contents/MacOS/"

# Copy content
cp -r build/osx-arm64/Content "build/YourGame.app/Contents/Resources/"
```

**Create `build/YourGame.app/Contents/Info.plist`:**

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN"
  "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>
  <string>Your Game</string>
  <key>CFBundleDisplayName</key>
  <string>Your Game</string>
  <key>CFBundleIdentifier</key>
  <string>com.yourstudio.yourgame</string>
  <key>CFBundleVersion</key>
  <string>1.0.0</string>
  <key>CFBundleShortVersionString</key>
  <string>1.0.0</string>
  <key>CFBundleExecutable</key>
  <string>YourGame</string>
  <key>CFBundleIconFile</key>
  <string>AppIcon</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>LSMinimumSystemVersion</key>
  <string>10.15</string>
  <key>NSHighResolutionCapable</key>
  <true/>
</dict>
</plist>
```

**Add your icon:**

```bash
# Convert a 1024x1024 PNG to .icns (macOS only)
mkdir AppIcon.iconset
sips -z 16 16     icon.png --out AppIcon.iconset/icon_16x16.png
sips -z 32 32     icon.png --out AppIcon.iconset/icon_16x16@2x.png
sips -z 32 32     icon.png --out AppIcon.iconset/icon_32x32.png
sips -z 64 64     icon.png --out AppIcon.iconset/icon_32x32@2x.png
sips -z 128 128   icon.png --out AppIcon.iconset/icon_128x128.png
sips -z 256 256   icon.png --out AppIcon.iconset/icon_128x128@2x.png
sips -z 256 256   icon.png --out AppIcon.iconset/icon_256x256.png
sips -z 512 512   icon.png --out AppIcon.iconset/icon_256x256@2x.png
sips -z 512 512   icon.png --out AppIcon.iconset/icon_512x512.png
sips -z 1024 1024 icon.png --out AppIcon.iconset/icon_512x512@2x.png
iconutil -c icns AppIcon.iconset
cp AppIcon.icns "build/YourGame.app/Contents/Resources/"
```

### Code Signing

Without signing, macOS Gatekeeper blocks your app entirely (not just a warning — it won't run).

**1. Get an Apple Developer ID ($99/year)**
You need a "Developer ID Application" certificate from [developer.apple.com](https://developer.apple.com).

**2. Sign the app:**

```bash
codesign --deep --force --verify --verbose \
  --sign "Developer ID Application: Your Name (TEAMID)" \
  --options runtime \
  --entitlements entitlements.plist \
  "build/YourGame.app"
```

**Create `entitlements.plist`:**

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

> **Why these entitlements?** .NET apps use JIT compilation, which requires executable memory permissions.

### Notarization

Apple requires notarization for apps distributed outside the App Store.

```bash
# Create a ZIP for notarization
ditto -c -k --keepParent "build/YourGame.app" "YourGame.zip"

# Submit for notarization
xcrun notarytool submit "YourGame.zip" \
  --apple-id "your@email.com" \
  --team-id "TEAMID" \
  --password "app-specific-password" \
  --wait

# Staple the notarization ticket
xcrun stapler staple "build/YourGame.app"
```

### Creating a DMG

```bash
# Simple DMG
hdiutil create -volname "Your Game" -srcfolder "build/YourGame.app" \
  -ov -format UDZO "YourGame-1.0.0.dmg"
```

For a fancier DMG with a background image and Applications shortcut, use [create-dmg](https://github.com/create-dmg/create-dmg):

```bash
brew install create-dmg

create-dmg \
  --volname "Your Game" \
  --window-pos 200 120 \
  --window-size 600 400 \
  --icon-size 100 \
  --icon "YourGame.app" 150 190 \
  --app-drop-link 450 190 \
  "YourGame-1.0.0.dmg" \
  "build/YourGame.app"
```

### Common macOS Issues

**"YourGame is damaged and can't be opened"**
The app isn't signed or notarized. Players can work around it with:

```bash
xattr -cr /path/to/YourGame.app
```

But this is a terrible user experience. Sign and notarize your builds.

**Content path resolution**
Inside a .app bundle, the working directory isn't where you'd expect. Use this to find your Content folder:

```csharp
var appDir = AppDomain.CurrentDomain.BaseDirectory;
var contentDir = Path.Combine(appDir, "..", "Resources", "Content");
```

Or set it in your entry point:

```csharp
Environment.CurrentDirectory = Path.Combine(
    AppDomain.CurrentDomain.BaseDirectory, "..", "Resources");
```

---

## 5. Steam Upload

### Steamworks Setup

**1. Create a Steamworks account** at [partner.steamgames.com](https://partner.steamgames.com). Requires the $100 Steam Direct fee per app.

**2. Get your App ID** from the Steamworks dashboard after creating your app.

**3. Download the Steamworks SDK** from the Steamworks partner site.

**4. Install `steamcmd`:**

```bash
# macOS (Homebrew)
brew install steamcmd

# Linux
sudo apt install steamcmd

# Windows: download from https://developer.valvesoftware.com/wiki/SteamCMD
```

### Depot Configuration

Depots are containers for your game files on Steam. You need one per platform.

In your Steamworks dashboard:
- **Depot 1** (e.g., ID `1000001`): Windows build
- **Depot 2** (e.g., ID `1000002`): Linux build
- **Depot 3** (e.g., ID `1000003`): macOS build

### SteamPipe Build Scripts

Create a folder structure for your Steam uploads:

```
steam_upload/
├── scripts/
│   ├── app_build.vdf
│   ├── depot_windows.vdf
│   ├── depot_linux.vdf
│   └── depot_macos.vdf
├── content_windows/    ← your Windows build goes here
├── content_linux/      ← your Linux build goes here
└── content_macos/      ← your macOS build goes here
```

**`scripts/app_build.vdf`:**

```
"AppBuild"
{
  "AppID" "YOUR_APP_ID"
  "Desc" "v1.0.0 release build"
  "ContentRoot" "..\\"
  "BuildOutput" "..\\output\\"
  "Depots"
  {
    "YOUR_DEPOT_ID_WIN" "depot_windows.vdf"
    "YOUR_DEPOT_ID_LINUX" "depot_linux.vdf"
    "YOUR_DEPOT_ID_MAC" "depot_macos.vdf"
  }
}
```

**`scripts/depot_windows.vdf`:**

```
"DepotBuildConfig"
{
  "DepotID" "YOUR_DEPOT_ID_WIN"
  "contentroot" "content_windows\\"
  "FileMapping"
  {
    "LocalPath" "*"
    "DepotPath" "."
    "recursive" "1"
  }
}
```

Create similar files for `depot_linux.vdf` and `depot_macos.vdf`, pointing to their respective content folders.

### Uploading with steamcmd

```bash
steamcmd +login "your_steam_username" +run_app_build \
  "/full/path/to/steam_upload/scripts/app_build.vdf" +quit
```

You'll be prompted for your password and Steam Guard code.

### Branch Management

Steam supports multiple branches for different build stages:

| Branch | Purpose |
|---|---|
| `default` | What players download (your stable release) |
| `beta` | Opt-in beta for adventurous players |
| `testing` | Private branch for internal QA (password-protected) |

Set branches in the Steamworks dashboard under **SteamPipe > Builds**. After uploading, set the build live on the appropriate branch.

### Build Verification

After uploading:

1. In Steamworks, go to **SteamPipe > Builds**
2. Verify the build shows up with correct file sizes
3. Set it live on the `testing` branch first
4. Download and test through Steam
5. Once verified, set live on `default`

### Launch Configuration

In Steamworks dashboard, set launch options:

| OS | Executable | Arguments |
|---|---|---|
| Windows | `YourGame.exe` | (none) |
| Linux | `YourGame` | (none) |
| macOS | `YourGame.app` | (none) |

### Steam Input API

If you want controller support (recommended), see [G32](../../monogame-arch/guides/G32_deployment_platform_builds.md) for Steam Input integration details. At minimum, configure default controller templates in the Steamworks dashboard so controllers work out of the box.

---

## 6. itch.io Upload

### butler CLI Setup

[butler](https://itch.io/docs/butler/) is itch.io's command-line upload tool. It handles incremental uploads (only uploads changed files).

**1. Install butler:**

```bash
# macOS (Homebrew)
brew install butler

# Or download from https://itch.io/docs/butler/installing.html
```

**2. Login:**

```bash
butler login
```

This opens a browser to authenticate.

### Uploading Builds

Butler uses "channels" to organize platform-specific builds:

```bash
# Windows
butler push build/windows yourusername/yourgame:windows
# → creates channel "windows"

# Linux
butler push build/linux yourusername/yourgame:linux
# → creates channel "linux"

# macOS (push the .app as a zip or the DMG)
butler push build/YourGame.app yourusername/yourgame:mac
# → creates channel "mac"
```

### Versioning

Tag uploads with version numbers:

```bash
butler push build/windows yourusername/yourgame:windows --userversion 1.0.0
```

Butler also auto-generates incrementing build numbers.

### Channel Naming Conventions

Stick to these — itch.io uses channel names to auto-detect platforms:

| Channel | Platform |
|---|---|
| `windows` | Windows |
| `linux` | Linux |
| `mac` or `osx` | macOS |

### Setting Up the itch.io Page

1. Go to [itch.io/game/new](https://itch.io/game/new)
2. Set **Kind of project** → Downloadable
3. Set **Pricing** → whatever you want (free, paid, PWYW)
4. Upload the builds (or use butler — they'll appear automatically)
5. Set each upload's platform tag correctly
6. Add screenshots, description, tags
7. Publish

### Checking Upload Status

```bash
butler status yourusername/yourgame
```

---

## 7. Content Pipeline Build

### How MonoGame Content Works in Release

The MonoGame Content Builder (MGCB) compiles raw assets (`.png`, `.wav`, `.fx`) into optimized `.xnb` files. These built assets go in your `Content/` folder.

### Ensuring Content is Included

Your `.csproj` should reference the content project:

```xml
<ItemGroup>
  <MonoGameContentReference Include="Content\Content.mgcb" />
</ItemGroup>
```

This tells MSBuild to run MGCB during the build and copy results to the output.

### Content.mgcb Configuration for Release

Open your `Content.mgcb` in the MGCB Editor and verify:

- **Platform** matches your target (DesktopGL)
- **Compress** is enabled for release (smaller files)
- All assets are listed and building without errors

### Pre-Building Content

For faster CI builds, pre-build content and check in the built `.xnb` files:

```bash
# Build content separately
dotnet mgcb Content/Content.mgcb /platform:DesktopGL

# Or using the MGCB tool directly
mgcb-editor Content/Content.mgcb  # GUI
```

### Common Content Pipeline Errors in Release

**"Content file not found" at runtime**
The content wasn't copied to the output. Check your `.csproj`:

```xml
<MonoGameContentReference Include="Content\Content.mgcb" />
```

**"Could not load asset" for specific files**
- Check file paths are case-correct (Linux!)
- Make sure the importer/processor is set correctly in `Content.mgcb`
- Verify the asset isn't referencing a path with spaces

**Content builds locally but fails in CI**
- Missing fonts: Install them in CI or use bundled `.spritefont` with a font file
- Missing MGCB tools: Add `dotnet tool restore` to your CI pipeline
- Platform mismatch: Build content for `DesktopGL`, not `Windows`

**Effect (.fx) compilation failures on non-Windows**
MonoGame's effect compiler has platform-specific quirks. If effects fail on Linux/macOS CI:
- Pre-build effects on Windows and commit the `.xnb` files
- Or use the `mgfxc` tool with Wine on Linux

---

## 8. Pre-Release Testing Checklist

Go through this on **every platform** before you ship.

### Core Functionality

- [ ] Game launches without errors
- [ ] All menus are navigable
- [ ] Core gameplay loop works start to finish
- [ ] Game can be completed (if applicable)
- [ ] Save game works
- [ ] Load game works (including saves from older versions if applicable)
- [ ] Settings are saved and restored between sessions

### Assets & Content

- [ ] All textures load (no purple/missing squares)
- [ ] All audio plays (music, SFX)
- [ ] All fonts render correctly
- [ ] No placeholder art left in
- [ ] No "TODO" or debug text visible

### Debug Code

- [ ] No debug overlays showing (FPS counter, hitboxes, etc.)
- [ ] No console window appearing (Windows)
- [ ] No debug logging to stdout/stderr (or properly suppressed)
- [ ] `#if DEBUG` / `#if !DISTRIBUTION` guards are correct
- [ ] No cheat keys enabled

### Performance

- [ ] Consistent frame rate on target minimum spec
- [ ] No memory leaks during extended play
- [ ] Loading times are acceptable
- [ ] No stuttering on scene transitions

### Platform-Specific

- [ ] **Windows:** SmartScreen warning is acceptable (or signed)
- [ ] **Linux:** File paths are case-correct, SDL2/OpenAL available
- [ ] **macOS:** App bundle structure is correct, signed and notarized
- [ ] Controller input works (if supported)
- [ ] Fullscreen/windowed toggle works
- [ ] Resolution changes work

### Release Infrastructure

- [ ] Crash reporting is enabled and tested — see [G51](../../monogame-arch/guides/G51_crash_reporting.md)
- [ ] Version number is correct and displayed in-game
- [ ] Store page text and screenshots are up to date
- [ ] System requirements listed accurately

### Fresh Install Test

The most important test: install your game on a machine that has **never** had your development environment on it. This catches:
- Missing DLLs/libraries
- Hardcoded paths
- Dependencies you forgot to bundle
- Content pipeline assumptions

---

## 9. Version Numbering

### Semantic Versioning for Games

Use **Major.Minor.Patch** format:

| Component | When to bump | Example |
|---|---|---|
| **Major** | Big content updates, breaking save changes | 1.0.0 → 2.0.0 |
| **Minor** | New features, new content, balance changes | 1.0.0 → 1.1.0 |
| **Patch** | Bug fixes, small tweaks | 1.0.0 → 1.0.1 |

For early access or pre-release: `0.x.y` (major = 0 signals "not 1.0 yet").

### Embedding Version in the Build

**1. Set version in `.csproj`:**

```xml
<PropertyGroup>
  <Version>1.0.0</Version>
  <AssemblyVersion>1.0.0.0</AssemblyVersion>
  <FileVersion>1.0.0.0</FileVersion>
  <InformationalVersion>1.0.0</InformationalVersion>
</PropertyGroup>
```

**2. Read it at runtime:**

```csharp
public static class GameVersion
{
    public static string Current =>
        System.Reflection.Assembly
            .GetExecutingAssembly()
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unknown";
}
```

**3. Or use a generated file (simpler):**

Create a build step that writes the version:

```xml
<!-- In .csproj -->
<Target Name="WriteVersion" BeforeTargets="CoreCompile">
  <WriteLinesToFile
    File="$(ProjectDir)VersionInfo.g.cs"
    Lines="// Auto-generated&#10;static partial class GameInfo { public const string Version = &quot;$(Version)&quot;%3B }"
    Overwrite="true" />
</Target>
```

### Displaying Version In-Game

Show it on your title screen or settings menu:

```csharp
spriteBatch.DrawString(font, $"v{GameVersion.Current}",
    new Vector2(10, screenHeight - 30), Color.Gray * 0.5f);
```

This is invaluable for bug reports — players can tell you which version they're on.

### Git Tags for Releases

Tag every release in Git:

```bash
git tag -a v1.0.0 -m "Release 1.0.0"
git push origin v1.0.0
```

This creates a permanent reference point. If you need to hotfix, you know exactly which commit was shipped.

---

## 10. Automated Build Pipeline

### GitHub Actions CI/CD

Automate your builds so every release is consistent. No more "it worked on my machine."

### Example Workflow

Create `.github/workflows/release.yml`:

```yaml
name: Release Build

on:
  push:
    tags:
      - 'v*'  # Trigger on version tags (v1.0.0, v1.1.0, etc.)

env:
  DOTNET_VERSION: '8.0.x'
  PROJECT_NAME: YourGame
  PROJECT_PATH: src/YourGame/YourGame.csproj

jobs:
  build:
    strategy:
      matrix:
        include:
          - os: windows-latest
            rid: win-x64
            artifact: windows
          - os: ubuntu-latest
            rid: linux-x64
            artifact: linux
          - os: macos-latest
            rid: osx-x64
            artifact: macos-x64
          - os: macos-latest
            rid: osx-arm64
            artifact: macos-arm64

    runs-on: ${{ matrix.os }}

    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: ${{ env.DOTNET_VERSION }}

      - name: Restore
        run: dotnet restore ${{ env.PROJECT_PATH }}

      - name: Publish
        run: |
          dotnet publish ${{ env.PROJECT_PATH }} \
            -c Release \
            -r ${{ matrix.rid }} \
            --self-contained \
            -p:PublishSingleFile=true \
            -p:IncludeNativeLibrariesForSelfExtract=true \
            -o ./publish/${{ matrix.artifact }}

      - name: Make executable (Linux/macOS)
        if: matrix.os != 'windows-latest'
        run: chmod +x ./publish/${{ matrix.artifact }}/${{ env.PROJECT_NAME }}

      - name: Upload artifact
        uses: actions/upload-artifact@v4
        with:
          name: ${{ env.PROJECT_NAME }}-${{ matrix.artifact }}
          path: ./publish/${{ matrix.artifact }}

  release:
    needs: build
    runs-on: ubuntu-latest
    permissions:
      contents: write

    steps:
      - name: Download all artifacts
        uses: actions/download-artifact@v4
        with:
          path: ./artifacts

      - name: Create ZIPs
        run: |
          cd artifacts
          for dir in */; do
            zip -r "../${dir%/}.zip" "$dir"
          done

      - name: Create GitHub Release
        uses: softprops/action-gh-release@v2
        with:
          draft: true
          generate_release_notes: true
          files: '*.zip'
```

### What This Does

1. **Triggers** when you push a version tag (`git push origin v1.0.0`)
2. **Builds** for Windows, Linux, and macOS (Intel + ARM) in parallel
3. **Uploads** build artifacts
4. **Creates** a draft GitHub Release with all platform ZIPs attached

### Adding Steam/itch.io Upload to CI

You can extend the workflow to auto-upload to stores:

```yaml
  deploy-steam:
    needs: build
    runs-on: ubuntu-latest
    if: startsWith(github.ref, 'refs/tags/v')
    steps:
      - name: Download Windows artifact
        uses: actions/download-artifact@v4
        with:
          name: ${{ env.PROJECT_NAME }}-windows
          path: ./steam/content_windows

      - name: Upload to Steam
        uses: game-ci/steam-deploy@v3
        with:
          username: ${{ secrets.STEAM_USERNAME }}
          configVdf: ${{ secrets.STEAM_CONFIG_VDF }}
          appId: YOUR_APP_ID
          buildDescription: ${{ github.ref_name }}
          rootPath: ./steam
          depot1Path: content_windows
          releaseBranch: beta  # Upload to beta first, manually promote to default

  deploy-itch:
    needs: build
    runs-on: ubuntu-latest
    if: startsWith(github.ref, 'refs/tags/v')
    steps:
      - name: Download artifacts
        uses: actions/download-artifact@v4
        with:
          path: ./artifacts

      - name: Install butler
        run: |
          curl -L -o butler.zip https://broth.itch.ovh/butler/linux-amd64/LATEST/archive/default
          unzip butler.zip
          chmod +x butler
          ./butler -V

      - name: Push to itch.io
        env:
          BUTLER_API_KEY: ${{ secrets.BUTLER_API_KEY }}
        run: |
          VERSION=${GITHUB_REF#refs/tags/v}
          ./butler push artifacts/${{ env.PROJECT_NAME }}-windows yourusername/yourgame:windows --userversion $VERSION
          ./butler push artifacts/${{ env.PROJECT_NAME }}-linux yourusername/yourgame:linux --userversion $VERSION
          ./butler push artifacts/${{ env.PROJECT_NAME }}-macos-arm64 yourusername/yourgame:mac --userversion $VERSION
```

### Secrets You'll Need

Add these in GitHub repo settings → Secrets:

| Secret | Source |
|---|---|
| `STEAM_USERNAME` | Your Steam partner account username |
| `STEAM_CONFIG_VDF` | Base64-encoded `config.vdf` from steamcmd login |
| `BUTLER_API_KEY` | From itch.io → User settings → API keys |

---

## 11. Hotfix Process

When a critical bug ships and players are affected.

### Step 1: Assess Severity

| Severity | Examples | Response Time |
|---|---|---|
| **Critical** | Game crashes on launch, save corruption, softlock | Same day |
| **High** | Major feature broken, progression blocker | 1-2 days |
| **Medium** | Visual glitch, minor gameplay issue | Next patch |
| **Low** | Typo, cosmetic issue | Whenever |

### Step 2: Fix on a Branch

```bash
# Create a hotfix branch from the release tag
git checkout -b hotfix/1.0.1 v1.0.0

# Fix the bug
# ... make your changes ...

git add .
git commit -m "Fix: [describe the bug]"
```

### Step 3: Test

Run through the abbreviated testing checklist:
- [ ] Bug is fixed
- [ ] Fix doesn't break anything else
- [ ] Game launches on all platforms
- [ ] Save/load still works
- [ ] Quick playthrough of affected area

### Step 4: Tag and Build

```bash
git tag -a v1.0.1 -m "Hotfix: [describe]"
git push origin hotfix/1.0.1 v1.0.1

# Merge back into main
git checkout main
git merge hotfix/1.0.1
git push origin main
```

If you have CI set up, the tag push triggers automated builds.

### Step 5: Push the Update

**Steam:**
```bash
# Upload new build
steamcmd +login "username" +run_app_build "/path/to/app_build.vdf" +quit

# In Steamworks dashboard: set new build live on "default" branch
```

Steam auto-updates for all players.

**itch.io:**
```bash
butler push build/windows yourusername/yourgame:windows --userversion 1.0.1
butler push build/linux yourusername/yourgame:linux --userversion 1.0.1
butler push build/YourGame.app yourusername/yourgame:mac --userversion 1.0.1
```

Butler does incremental patches — players only download changed files.

### Step 6: Communicate

- Post a patch note on your Steam store page (Steamworks → Posts)
- Update the itch.io devlog
- Post on your social media / Discord
- Be honest about what happened and what you fixed

**Template:**

> **Patch 1.0.1**
>
> Fixed a critical bug where [description]. Sorry about that — the fix is live now. If you still experience issues, please reach out on [Discord/email].

---

## 12. Build Checklist Template

Copy this for every release. Print it, put it in a Notion page, whatever works for you.

---

### Release Build Checklist — v____

**Date:** _______________

#### Pre-Build

- [ ] Version number updated in `.csproj`
- [ ] All changes committed and pushed
- [ ] `CHANGELOG.md` updated
- [ ] No uncommitted debug/test code
- [ ] Git tag created: `git tag -a v___ -m "___"`

#### Build

- [ ] Windows build: `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o ./build/windows`
- [ ] Linux build: `dotnet publish -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o ./build/linux`
- [ ] macOS build: `dotnet publish -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true -o ./build/macos`
- [ ] All builds compile without warnings/errors

#### Test — Windows

- [ ] Launches on clean machine
- [ ] All content loads
- [ ] Save/load works
- [ ] No console window
- [ ] No debug overlays

#### Test — Linux

- [ ] Launches in VM/WSL
- [ ] All content loads (case-sensitive paths OK)
- [ ] Audio works

#### Test — macOS

- [ ] .app bundle launches
- [ ] Signed and notarized
- [ ] Gatekeeper doesn't block it
- [ ] Works on Apple Silicon

#### Upload

- [ ] Steam: uploaded via steamcmd/CI
- [ ] Steam: build set live on `testing` branch
- [ ] Steam: tested install from Steam
- [ ] Steam: promoted to `default` branch
- [ ] itch.io: pushed via butler for all platforms
- [ ] itch.io: verified downloads work
- [ ] GitHub: release draft published

#### Post-Release

- [ ] Patch notes posted (Steam, itch.io, social media)
- [ ] Crash reporting confirmed working — see [G51](../../monogame-arch/guides/G51_crash_reporting.md)
- [ ] Monitor for bug reports (24-48 hours)
- [ ] Celebrate! You shipped a game! 🎉

---

> **See also:**
> - [G32 — Deployment & Platform Builds](../../monogame-arch/guides/G32_deployment_platform_builds.md) for platform-specific build details
> - [G36 — Publishing & Distribution](../../monogame-arch/guides/G36_publishing_distribution.md) for store setup and marketing
> - [G51 — Crash Reporting](../../monogame-arch/guides/G51_crash_reporting.md) for setting up crash analytics
