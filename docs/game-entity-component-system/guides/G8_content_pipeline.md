# G8 — Content Pipeline


> **Category:** Guide · **Related:** [R1 Library Stack](../reference/R1_library_stack.md) · [G2 Rendering & Graphics](./G2_rendering_and_graphics.md) · [G6 Audio](./G6_audio.md)

> Deep dive into the MonoGame content pipeline: MGCB configuration, custom importers, Aseprite animation, Tiled maps, texture atlases, fonts, audio, data loading, hot-reload, and cross-platform content strategies.

---

## 1. MGCB Setup & Configuration

The MonoGame Content Builder (MGCB) compiles raw assets (PNGs, WAVs, FX shaders) into optimized `.xnb` binary format at build time. This enables platform-specific compression, faster load times, and smaller packages.

### 1.1 Installing the MGCB Tools

```bash
# Install the MGCB Editor (GUI) — globally
dotnet tool install --global dotnet-mgcb-editor

# Install the CLI builder
dotnet tool install --global dotnet-mgcb

# Register file associations (opens .mgcb files in the editor)
mgcb-editor --register
```

After installation, double-clicking a `.mgcb` file opens the MGCB Editor GUI, or you can launch it from the terminal:

```bash
mgcb-editor Content/Content.mgcb
```

### 1.2 The .mgcb File Format

The `.mgcb` file is a plain-text manifest that lists every asset and its build settings:

```
#----------------------------- Global Properties ----------------------------#

/outputDir:bin/$(Platform)
/intermediateDir:obj/$(Platform)
/platform:DesktopGL
/config:
/profile:Reach
/compress:False

#-------------------------------- References ---------------------------------#

/reference:..\..\packages\MonoGame.Extended.Content.Pipeline\lib\MonoGame.Extended.Content.Pipeline.dll

#---------------------------------- Content ----------------------------------#

#begin sprites/player.png
/importer:TextureImporter
/processor:TextureProcessor
/processorParam:ColorKeyColor=255,0,255,255
/processorParam:ColorKeyEnabled=True
/processorParam:GenerateMipmaps=False
/processorParam:PremultiplyAlpha=True
/processorParam:ResizeToPowerOfTwo=False
/processorParam:MakeSquare=False
/processorParam:TextureFormat=Color
/build:sprites/player.png
```

**Key directives:**

| Directive | Purpose |
|---|---|
| `/platform:DesktopGL` | Target platform (also `WindowsDX`, `iOS`, `Android`) |
| `/profile:Reach` | GPU feature level (`Reach` = widest compatibility, `HiDef` = advanced shaders) |
| `/compress:True` | DXT/ETC compression — smaller files, lossy for pixel art |
| `/reference:path.dll` | Add a pipeline extension DLL (for custom importers) |
| `/importer:Name` | Which importer reads the raw file |
| `/processor:Name` | Which processor transforms imported data |
| `/copy:path` | Copy file as-is (no processing) — used for `.ttf`, `.json`, `.tmx` |
| `/build:path` | Build the asset with specified importer/processor |

### 1.3 MGCB Editor GUI

The MGCB Editor provides a visual interface for managing content:

- **Add Existing Item** — browse for files, auto-detects importer/processor
- **Properties panel** — configure processor parameters per-asset
- **Build menu** — compile all or rebuild specific assets
- **Platform dropdown** — switch target platform for testing

> **Tip:** The MGCB Editor is optional. Power users often edit `.mgcb` files by hand and build via CLI. Both are equivalent.

### 1.4 CLI Build Commands

```bash
# Build all content
mgcb Content.mgcb /platform:DesktopGL

# Rebuild everything (ignore cache)
mgcb Content.mgcb /platform:DesktopGL /rebuild

# Clean intermediate files
mgcb Content.mgcb /platform:DesktopGL /clean

# Build for a specific platform
mgcb Content.mgcb /platform:iOS
mgcb Content.mgcb /platform:Android
```

### 1.5 .csproj Integration

MonoGame projects reference the `.mgcb` file in the `.csproj`, which triggers automatic content builds during `dotnet build`:

```xml
<ItemGroup>
  <MonoGameContentReference Include="Content\Content.mgcb" />
</ItemGroup>
```

For shared-project architectures (Core + Platform projects), the platform project references Core's content:

```xml
<!-- In MyGame.iOS.csproj or MyGame.Android.csproj -->
<ItemGroup>
  <MonoGameContentReference Include="..\MyGame.Core\Content\Content.mgcb" />
</ItemGroup>
```

---

## 2. Content Importers & Processors Explained

The MGCB pipeline has two stages: **Import** (read raw file → intermediate object) and **Process** (transform intermediate → final optimized format).

### 2.1 Built-in Importers

| Importer | File Types | Output Type |
|---|---|---|
| `TextureImporter` | `.png`, `.jpg`, `.bmp`, `.tga` | `TextureContent` |
| `WavImporter` | `.wav` | `AudioContent` |
| `Mp3Importer` | `.mp3` | `AudioContent` |
| `OggImporter` | `.ogg` | `AudioContent` |
| `EffectImporter` | `.fx` | `EffectContent` |
| `FontDescriptionImporter` | `.spritefont` | `FontDescription` |
| `XmlImporter` | `.xml` | `object` |

### 2.2 Built-in Processors

| Processor | What It Does |
|---|---|
| `TextureProcessor` | Color key, premultiply alpha, mipmaps, resize, format selection |
| `SoundEffectProcessor` | Converts audio to platform-optimal format (PCM, ADPCM) |
| `SongProcessor` | Prepares audio for `MediaPlayer` streaming playback |
| `EffectProcessor` | Compiles HLSL to platform shaders (GLSL for DesktopGL, Metal for iOS) |
| `FontDescriptionProcessor` | Rasterizes a font at specified size into a glyph atlas |
| `FontTextureProcessor` | Imports a pre-rendered font texture (bitmap font) |

### 2.3 TextureProcessor Parameters

| Parameter | Default | Notes |
|---|---|---|
| `ColorKeyEnabled` | `True` | Replace a color with transparent (typically magenta) |
| `ColorKeyColor` | `255,0,255,255` | The color to key out |
| `PremultiplyAlpha` | `True` | Pre-multiply RGB by alpha — required for `BlendState.AlphaBlend` |
| `GenerateMipmaps` | `False` | For 2D pixel art, always `False` |
| `ResizeToPowerOfTwo` | `False` | Only needed for very old GPUs |
| `TextureFormat` | `Color` | `Color` = uncompressed RGBA, `Compressed` = DXT/ETC |

> **Pixel art rule:** Set `TextureFormat=Color`, `GenerateMipmaps=False`, `PremultiplyAlpha=True`. Never compress pixel art — DXT destroys sharp edges.

---

## 3. Writing Custom Content Importers

When you need to load a proprietary or unsupported format at build time, write a custom importer/processor.

### 3.1 Pipeline Extension Project Setup

Create a separate class library project for your pipeline extensions:

```bash
dotnet new classlib -n MyGame.ContentPipeline
cd MyGame.ContentPipeline
dotnet add package MonoGame.Framework.Content.Pipeline
```

> **Critical:** Pipeline extensions run at **build time** on the dev machine, not at runtime. They reference `MonoGame.Framework.Content.Pipeline`, not `MonoGame.Framework`.

### 3.2 Custom Importer Example — Tiled Collision Data

```csharp
using Microsoft.Xna.Framework.Content.Pipeline;

// The intermediate data type — what the importer produces
public class CollisionMapData
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int TileSize { get; set; }
    public bool[] SolidTiles { get; set; }
}

// The importer — reads a custom .collmap file
[ContentImporter(".collmap", DisplayName = "Collision Map Importer",
    DefaultProcessor = "CollisionMapProcessor")]
public class CollisionMapImporter : ContentImporter<CollisionMapData>
{
    public override CollisionMapData Import(string filename, ContentImporterContext context)
    {
        context.Logger.LogMessage($"Importing collision map: {filename}");

        var lines = File.ReadAllLines(filename);
        // First line: "width,height,tilesize"
        var header = lines[0].Split(',');

        var data = new CollisionMapData
        {
            Width = int.Parse(header[0]),
            Height = int.Parse(header[1]),
            TileSize = int.Parse(header[2]),
            SolidTiles = new bool[int.Parse(header[0]) * int.Parse(header[1])]
        };

        // Remaining lines: rows of 0s and 1s
        for (int y = 0; y < data.Height; y++)
        {
            var row = lines[y + 1].Split(',');
            for (int x = 0; x < data.Width; x++)
                data.SolidTiles[y * data.Width + x] = row[x] == "1";
        }

        return data;
    }
}
```

### 3.3 Custom Processor

```csharp
using Microsoft.Xna.Framework.Content.Pipeline;
using Microsoft.Xna.Framework.Content.Pipeline.Serialization.Compiler;

[ContentProcessor(DisplayName = "Collision Map Processor")]
public class CollisionMapProcessor : ContentProcessor<CollisionMapData, CollisionMapData>
{
    // Processor parameters — configurable in MGCB Editor
    [System.ComponentModel.DefaultValue(16)]
    public virtual int OverrideTileSize { get; set; } = 16;

    public override CollisionMapData Process(CollisionMapData input,
        ContentProcessorContext context)
    {
        if (OverrideTileSize > 0)
            input.TileSize = OverrideTileSize;

        context.Logger.LogMessage($"Processed collision map: {input.Width}x{input.Height}");
        return input;
    }
}
```

### 3.4 Custom Content Writer

```csharp
[ContentTypeWriter]
public class CollisionMapWriter : ContentTypeWriter<CollisionMapData>
{
    protected override void Write(ContentWriter output, CollisionMapData value)
    {
        output.Write(value.Width);
        output.Write(value.Height);
        output.Write(value.TileSize);
        output.Write(value.SolidTiles.Length);
        foreach (bool solid in value.SolidTiles)
            output.Write(solid);
    }

    public override string GetRuntimeReader(TargetPlatform targetPlatform)
    {
        // Fully qualified name of the runtime reader class
        return "MyGame.Content.CollisionMapReader, MyGame.Core";
    }

    public override string GetRuntimeType(TargetPlatform targetPlatform)
    {
        return "MyGame.Collision.CollisionMap, MyGame.Core";
    }
}
```

### 3.5 Runtime Content Reader

This class lives in your **game project** (not the pipeline project):

```csharp
using Microsoft.Xna.Framework.Content;

namespace MyGame.Content;

public class CollisionMap
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int TileSize { get; set; }
    public bool[] SolidTiles { get; set; }

    public bool IsSolid(int tileX, int tileY)
        => tileX >= 0 && tileX < Width && tileY >= 0 && tileY < Height
           && SolidTiles[tileY * Width + tileX];
}

public class CollisionMapReader : ContentTypeReader<CollisionMap>
{
    protected override CollisionMap Read(ContentReader input, CollisionMap existingInstance)
    {
        int width = input.ReadInt32();
        int height = input.ReadInt32();
        int tileSize = input.ReadInt32();
        int count = input.ReadInt32();
        var tiles = new bool[count];
        for (int i = 0; i < count; i++)
            tiles[i] = input.ReadBoolean();

        return new CollisionMap
        {
            Width = width,
            Height = height,
            TileSize = tileSize,
            SolidTiles = tiles
        };
    }
}
```

### 3.6 Registering the Pipeline Extension

In your `.mgcb` file, reference the compiled pipeline DLL:

```
/reference:..\MyGame.ContentPipeline\bin\Debug\net8.0\MyGame.ContentPipeline.dll

#begin levels/world1.collmap
/importer:CollisionMapImporter
/processor:CollisionMapProcessor
/processorParam:OverrideTileSize=16
/build:levels/world1.collmap
```

Then load at runtime:

```csharp
var collisionMap = Content.Load<CollisionMap>("levels/world1");
```

---

## 4. Aseprite Pipeline

### 4.1 MonoGame.Aseprite Setup

[MonoGame.Aseprite](https://github.com/AristurtleDev/monogame-aseprite) (v6.3.x) loads `.aseprite`/`.ase` files directly — no manual export needed. Frame durations, animation tags, layers, and slices are all preserved.

**Install the runtime package:**
```bash
dotnet add package MonoGame.Aseprite --version 6.3.1
```

**Install the content pipeline extension:**
```bash
dotnet add package MonoGame.Aseprite.Content.Pipeline --version 6.3.1
```

**Register in `.mgcb`:**
```
/reference:..\packages\MonoGame.Aseprite.Content.Pipeline\lib\MonoGame.Aseprite.Content.Pipeline.dll
```

Or use the MGCB Editor: **Content → References → Add** and browse to the pipeline DLL.

### 4.2 Adding Aseprite Files to Content

```
#begin sprites/player.aseprite
/importer:AsepriteFileImporter
/processor:AsepriteFileProcessor
/build:sprites/player.aseprite
```

The importer reads the raw `.aseprite` binary format. The processor prepares it for runtime use. No export from Aseprite is needed — save the `.aseprite` file directly into your Content folder.

### 4.3 Runtime Loading — Key Types

MonoGame.Aseprite provides several ways to use the loaded data:

| Type | Created From | Purpose |
|---|---|---|
| `AsepriteFile` | `Content.Load<AsepriteFile>()` | Raw loaded file — create other types from it |
| `Sprite` | `aseFile.CreateSprite()` | Single static frame |
| `SpriteSheet` | `aseFile.CreateSpriteSheet()` | All frames + animation tags — the workhorse |
| `AnimatedSprite` | `spriteSheet.CreateAnimatedSprite()` | Playback controller with frame timing |
| `TextureAtlas` | `aseFile.CreateTextureAtlas()` | Packed frames as named texture regions |

### 4.4 Loading and Creating Animations

```csharp
using MonoGame.Aseprite;

// In LoadContent
AsepriteFile aseFile = Content.Load<AsepriteFile>("sprites/player");

// Create a SpriteSheet — packs all frames into a single GPU texture
SpriteSheet spriteSheet = aseFile.CreateSpriteSheet(GraphicsDevice);

// Create an AnimatedSprite starting with the "idle" tag
AnimatedSprite animatedSprite = spriteSheet.CreateAnimatedSprite("idle");
```

### 4.5 Animation Playback

```csharp
// In Update — advance frame timing
animatedSprite.Update(gameTime);

// Switch animation tag (only restarts if tag actually changes)
animatedSprite.Play("run");

// Play once (no loop) — e.g., attack animation
animatedSprite.Play("attack", loopCount: 0);

// Control
animatedSprite.Stop();
animatedSprite.Pause();
animatedSprite.Unpause();
animatedSprite.FlipHorizontally = true;   // face left
animatedSprite.SetFrame(0);               // jump to specific frame
animatedSprite.Speed = 1.5f;              // 1.5× playback speed

// In Draw
animatedSprite.Draw(spriteBatch, new Vector2(100, 200));
```

### 4.6 Animation Events

Use callbacks to trigger gameplay events on specific frames:

```csharp
animatedSprite.OnFrameBegin += (sender, args) =>
{
    string tag = animatedSprite.CurrentTag;

    // Footstep sounds on run animation frames 2 and 6
    if (tag == "run" && (args.FrameIndex == 2 || args.FrameIndex == 6))
        AudioManager.Play("footstep");

    // Spawn hitbox on attack frame 3
    if (tag == "attack" && args.FrameIndex == 3)
        SpawnAttackHitbox(position, facing);
};

animatedSprite.OnAnimationEnd += (sender, args) =>
{
    // Return to idle after attack finishes
    if (animatedSprite.CurrentTag == "attack")
        animatedSprite.Play("idle");
};
```

### 4.7 ECS Integration Pattern

Store the `AnimatedSprite` as a component and update/draw via systems:

```csharp
// Component
public record struct AnimatedSpriteComponent(AnimatedSprite Sprite);

// Entity creation
var aseFile = Content.Load<AsepriteFile>("sprites/player");
var sheet = aseFile.CreateSpriteSheet(GraphicsDevice);
var anim = sheet.CreateAnimatedSprite("idle");

world.Create(
    new Position(100, 200),
    new AnimatedSpriteComponent(anim),
    new RenderLayerTag(20)
);

// Animation update system
world.Query(in _animQuery, (ref AnimatedSpriteComponent anim) =>
{
    anim.Sprite.Update(gameTime);
});

// Animation render system
world.Query(in _renderQuery, (ref Position pos, ref AnimatedSpriteComponent anim) =>
{
    anim.Sprite.Draw(spriteBatch, pos.Value);
});
```

### 4.8 Aseprite Workflow Tips

- **One `.aseprite` file per character/entity** — keeps all animations (idle, run, jump, attack) as tags in one file.
- **Use Aseprite's tag system** — tag names become animation names in code.
- **Frame durations in Aseprite are authoritative** — set per-frame timing in Aseprite, not code.
- **Layer visibility** — MonoGame.Aseprite flattens all visible layers. Use Aseprite layer visibility to exclude helper layers (guidelines, hitbox markers).
- **Slice support** — define hitboxes or anchor points via Aseprite slices, access them at runtime via `aseFile.Slices`.

---

## 5. Tiled Map Pipeline

### 5.1 MonoGame.Extended Tiled Setup

[MonoGame.Extended](https://github.com/craftworkgames/MonoGame.Extended) provides a complete Tiled `.tmx` map loader and renderer.

**Install:**
```bash
# Runtime
dotnet add package MonoGame.Extended --version 4.0.3
dotnet add package MonoGame.Extended.Tiled --version 4.0.3

# Content pipeline extension (for build-time processing)
dotnet add package MonoGame.Extended.Content.Pipeline --version 4.0.3
```

**Register the pipeline in `.mgcb`:**
```
/reference:..\packages\MonoGame.Extended.Content.Pipeline\lib\MonoGame.Extended.Content.Pipeline.dll
```

### 5.2 Adding Tiled Maps to Content

Tiled maps consist of the `.tmx` file plus tileset images (`.png`). Both must be in the Content folder:

```
Content/
├── tilemaps/
│   ├── level1.tmx          # The map file
│   ├── tileset_ground.png   # Tileset image referenced by .tmx
│   └── tileset_props.png    # Another tileset image
```

In `.mgcb`:
```
#begin tilemaps/level1.tmx
/importer:TiledMapImporter
/processor:TiledMapProcessor
/build:tilemaps/level1.tmx

#begin tilemaps/tileset_ground.png
/importer:TextureImporter
/processor:TextureProcessor
/processorParam:PremultiplyAlpha=True
/build:tilemaps/tileset_ground.png

#begin tilemaps/tileset_props.png
/importer:TextureImporter
/processor:TextureProcessor
/processorParam:PremultiplyAlpha=True
/build:tilemaps/tileset_props.png
```

> **Important:** Tileset images must be added as separate content entries. The `.tmx` references them by relative path — keep the folder structure consistent between Tiled and your Content folder.

### 5.3 Loading and Rendering Tiled Maps

```csharp
using MonoGame.Extended.Tiled;
using MonoGame.Extended.Tiled.Renderers;

private TiledMap _tiledMap;
private TiledMapRenderer _tiledMapRenderer;

protected override void LoadContent()
{
    _tiledMap = Content.Load<TiledMap>("tilemaps/level1");
    _tiledMapRenderer = new TiledMapRenderer(GraphicsDevice, _tiledMap);
}

protected override void Update(GameTime gameTime)
{
    // Required — updates animated tiles
    _tiledMapRenderer.Update(gameTime);
}

protected override void Draw(GameTime gameTime)
{
    GraphicsDevice.Clear(Color.CornflowerBlue);

    // Draw all layers with camera transform
    _tiledMapRenderer.Draw(viewMatrix: _camera.TransformMatrix);
}
```

### 5.4 Rendering Specific Layers

For control over draw order (e.g., draw entities between background and foreground tile layers):

```csharp
// Draw background layers
var bgLayer = _tiledMap.GetLayer<TiledMapTileLayer>("Background");
_tiledMapRenderer.Draw(bgLayer, viewMatrix: _camera.TransformMatrix);

var groundLayer = _tiledMap.GetLayer<TiledMapTileLayer>("Ground");
_tiledMapRenderer.Draw(groundLayer, viewMatrix: _camera.TransformMatrix);

// Draw entities here (between ground and foreground)
DrawEntities();

// Draw foreground layers (trees, roof overhangs)
var fgLayer = _tiledMap.GetLayer<TiledMapTileLayer>("Foreground");
_tiledMapRenderer.Draw(fgLayer, viewMatrix: _camera.TransformMatrix);
```

### 5.5 Object Layers — Spawn Points, Triggers, Regions

Tiled object layers define non-visual data: spawn points, trigger zones, camera bounds.

```csharp
// Access an object layer
var objectLayer = _tiledMap.GetLayer<TiledMapObjectLayer>("Objects");

foreach (var obj in objectLayer.Objects)
{
    switch (obj.Type)
    {
        case "PlayerSpawn":
            SpawnPlayer(new Vector2(obj.Position.X, obj.Position.Y));
            break;

        case "EnemySpawn":
            string enemyType = obj.Properties["EnemyType"];
            int count = int.Parse(obj.Properties["Count"]);
            SpawnEnemies(obj.Position, enemyType, count);
            break;

        case "Trigger":
            var triggerRect = new Rectangle(
                (int)obj.Position.X, (int)obj.Position.Y,
                (int)obj.Size.Width, (int)obj.Size.Height);
            string action = obj.Properties["Action"];
            RegisterTrigger(triggerRect, action);
            break;

        case "CameraBounds":
            _cameraBounds = new Rectangle(
                (int)obj.Position.X, (int)obj.Position.Y,
                (int)obj.Size.Width, (int)obj.Size.Height);
            break;
    }
}
```

### 5.6 Collision from Tiled

**Method 1: Dedicated collision layer** — a tile layer named "Collision" where any non-empty tile is solid:

```csharp
public class TiledCollisionMap
{
    private readonly bool[,] _solid;
    private readonly int _tileWidth;
    private readonly int _tileHeight;

    public TiledCollisionMap(TiledMap map, string layerName = "Collision")
    {
        var layer = map.GetLayer<TiledMapTileLayer>(layerName);
        _tileWidth = map.TileWidth;
        _tileHeight = map.TileHeight;
        _solid = new bool[map.Width, map.Height];

        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                var tile = layer.GetTile((ushort)x, (ushort)y);
                _solid[x, y] = !tile.IsBlank;  // any non-empty tile = solid
            }
        }
    }

    public bool IsSolid(int tileX, int tileY)
        => tileX >= 0 && tileX < _solid.GetLength(0)
        && tileY >= 0 && tileY < _solid.GetLength(1)
        && _solid[tileX, tileY];

    public bool IsSolidAtWorld(float worldX, float worldY)
        => IsSolid((int)(worldX / _tileWidth), (int)(worldY / _tileHeight));

    public Rectangle GetTileBounds(int tileX, int tileY)
        => new(tileX * _tileWidth, tileY * _tileHeight, _tileWidth, _tileHeight);
}
```

**Method 2: Object layer rectangles** — use Tiled's object layer to place precise collision rectangles:

```csharp
var collisionLayer = _tiledMap.GetLayer<TiledMapObjectLayer>("CollisionRects");
var colliders = new List<Rectangle>();

foreach (var obj in collisionLayer.Objects)
{
    colliders.Add(new Rectangle(
        (int)obj.Position.X, (int)obj.Position.Y,
        (int)obj.Size.Width, (int)obj.Size.Height));
}
```

**Method 3: Tile properties** — in Tiled, set a custom boolean property `Solid=true` on specific tiles in the tileset. Then check at runtime:

```csharp
var layer = _tiledMap.GetLayer<TiledMapTileLayer>("Ground");
for (int y = 0; y < _tiledMap.Height; y++)
{
    for (int x = 0; x < _tiledMap.Width; x++)
    {
        var tile = layer.GetTile((ushort)x, (ushort)y);
        if (tile.IsBlank) continue;

        // Access tileset properties for this tile's global ID
        var tileset = _tiledMap.GetTilesetByTileGlobalIdentifier(tile.GlobalIdentifier);
        var tilesetTile = tileset.Tiles.FirstOrDefault(
            t => t.LocalTileIdentifier == tile.GlobalIdentifier - tileset.FirstGlobalIdentifier);

        if (tilesetTile?.Properties.ContainsKey("Solid") == true
            && tilesetTile.Properties["Solid"] == "true")
        {
            _solid[x, y] = true;
        }
    }
}
```

### 5.7 Tiled Workflow Tips

- **Use Tiled's "Embed Tileset" or external `.tsx` files** — external `.tsx` is reusable across maps.
- **Layer naming convention:** `Background`, `Ground`, `Foreground`, `Collision`, `Objects` — consistent names simplify code.
- **Custom properties on objects** are strings — parse them at load time (`int.Parse`, `Enum.Parse`, etc.).
- **Animated tiles in Tiled** — set frame durations in the tileset; `TiledMapRenderer.Update()` handles playback.
- **Isometric maps** — supported by MonoGame.Extended Tiled; set map orientation in Tiled to "Isometric."

---

## 6. Texture Atlas Packing

### 6.1 Why Use Texture Atlases

Drawing 100 sprites from 100 separate textures = up to 100 draw calls. Drawing 100 sprites from 1 atlas = 1 draw call. Atlases minimize GPU state changes and maximize batching.

### 6.2 MonoGame.Extended Texture Atlas (JSON Hash)

MonoGame.Extended can load texture atlases in the **JSON Hash** format (exported by tools like TexturePacker, Aseprite, or free alternatives like Free Texture Packer).

**Atlas JSON format (TexturePacker JSON Hash):**
```json
{
    "frames": {
        "player_idle_0": {
            "frame": { "x": 0, "y": 0, "w": 32, "h": 32 },
            "sourceSize": { "w": 32, "h": 32 }
        },
        "player_idle_1": {
            "frame": { "x": 32, "y": 0, "w": 32, "h": 32 },
            "sourceSize": { "w": 32, "h": 32 }
        },
        "enemy_bat_0": {
            "frame": { "x": 64, "y": 0, "w": 16, "h": 16 },
            "sourceSize": { "w": 16, "h": 16 }
        }
    },
    "meta": {
        "image": "atlas.png",
        "size": { "w": 256, "h": 256 }
    }
}
```

**Add to `.mgcb`:**
```
#begin sprites/atlas.png
/importer:TextureImporter
/processor:TextureProcessor
/build:sprites/atlas.png

#begin sprites/atlas.json
/copy:sprites/atlas.json
```

**Load and use at runtime:**
```csharp
using MonoGame.Extended.Graphics;

// Load texture atlas
Texture2D atlasTexture = Content.Load<Texture2D>("sprites/atlas");

// Load JSON data and create atlas (or parse manually)
// MonoGame.Extended provides TextureAtlas with named regions:
var atlas = TextureAtlas.Create("gameAtlas", atlasTexture, regions);

// Get a specific region
var playerRegion = atlas["player_idle_0"];

// Draw
spriteBatch.Draw(atlasTexture, position, playerRegion.Bounds, Color.White);
```

### 6.3 TexturePacker Workflow

[TexturePacker](https://www.codeandweb.com/texturepacker) is the gold standard for atlas creation:

1. **Import sprites** — drag PNG files or folders into TexturePacker
2. **Configure settings:**
   - Format: **JSON (Hash)**
   - Max size: **2048×2048** (safe across all platforms)
   - Padding: **1–2px** (prevents bleeding between sprites)
   - Trim: **Enabled** (removes transparent borders, saves space)
   - Extrude: **1px** (duplicates edge pixels, prevents tile edge artifacts)
3. **Export** — generates `atlas.png` + `atlas.json`
4. **Copy both into Content folder**

**Free alternatives:** [Free Texture Packer](http://free-tex-packer.com/), [Shoebox](https://renderhjs.net/shoebox/), or Aseprite's built-in sprite sheet export.

### 6.4 Manual Atlas with Source Rectangles

For small projects, you can skip atlas tools and use a manually-packed sprite sheet with hardcoded rectangles:

```csharp
// All sprites on one texture, known positions
Texture2D spriteSheet = Content.Load<Texture2D>("sprites/characters");

// Define source rectangles
Rectangle playerIdle = new(0, 0, 32, 32);
Rectangle playerRun0 = new(32, 0, 32, 32);
Rectangle playerRun1 = new(64, 0, 32, 32);
Rectangle enemyBat   = new(0, 32, 16, 16);
Rectangle itemCoin   = new(16, 32, 16, 16);

// All draw from same texture = 1 batch
spriteBatch.Draw(spriteSheet, playerPos, playerIdle, Color.White);
spriteBatch.Draw(spriteSheet, enemyPos, enemyBat, Color.White);
spriteBatch.Draw(spriteSheet, coinPos, itemCoin, Color.White);
```

### 6.5 Atlas Organization Strategy

| Atlas | Contents | Max Size |
|---|---|---|
| `characters.png` | Player + NPC + enemy sprites | 2048×2048 |
| `tiles.png` | All tile graphics | 2048×2048 |
| `ui.png` | Buttons, panels, icons | 1024×1024 |
| `particles.png` | Particle textures, small effects | 512×512 |
| `items.png` | Inventory icons, pickups | 1024×1024 |

**Rule of thumb:** Group by frequency of co-drawing. Sprites drawn in the same Begin/End block should share an atlas.

---

## 7. Font Pipeline

### 7.1 Option A: MGCB SpriteFont (Build-Time)

MonoGame's built-in approach — rasterizes a system font into a glyph atlas at build time.

**Create `fonts/GameFont.spritefont`:**
```xml
<?xml version="1.0" encoding="utf-8"?>
<XnaContent xmlns:Graphics="Microsoft.Xna.Framework.Content.Pipeline.Graphics">
  <Asset Type="Graphics:FontDescription">
    <FontName>Arial</FontName>
    <Size>14</Size>
    <Spacing>0</Spacing>
    <UseKerning>true</UseKerning>
    <Style>Regular</Style>
    <CharacterRegions>
      <CharacterRegion>
        <Start>&#32;</Start>
        <End>&#126;</End>
      </CharacterRegion>
    </CharacterRegions>
  </Asset>
</XnaContent>
```

**Add to `.mgcb`:**
```
#begin fonts/GameFont.spritefont
/importer:FontDescriptionImporter
/processor:FontDescriptionProcessor
/build:fonts/GameFont.spritefont
```

**Load and draw:**
```csharp
SpriteFont font = Content.Load<SpriteFont>("fonts/GameFont");

spriteBatch.Begin(samplerState: SamplerState.PointClamp);
spriteBatch.DrawString(font, "Hello World", new Vector2(10, 10), Color.White);
spriteBatch.End();
```

**SpriteFont limitations:**
- Fixed size — must create separate `.spritefont` per size
- Font must exist on the **build machine** (not bundled)
- Limited character set (must declare ranges)
- Quality can be poor for pixel art (no runtime rasterization control)
- No dynamic sizing at runtime

### 7.2 Option B: FontStashSharp (Runtime — Recommended)

[FontStashSharp](https://github.com/FontStashSharp/FontStashSharp) loads `.ttf`/`.otf` files at runtime and rasterizes glyphs on demand. Any size, any time.

**Install:**
```bash
dotnet add package FontStashSharp.MonoGame --version 1.3.7
```

**Add font file to Content (copy, don't process):**
```
#begin fonts/JetBrainsMono-Regular.ttf
/copy:fonts/JetBrainsMono-Regular.ttf
```

**Load and use:**
```csharp
using FontStashSharp;

private FontSystem _fontSystem;

protected override void LoadContent()
{
    _fontSystem = new FontSystem();

    // Load font from content (cross-platform safe)
    using Stream fontStream = TitleContainer.OpenStream(
        Path.Combine("Content", "fonts", "JetBrainsMono-Regular.ttf"));
    using MemoryStream ms = new();
    fontStream.CopyTo(ms);
    _fontSystem.AddFont(ms.ToArray());
}

protected override void Draw(GameTime gameTime)
{
    // Get any size on demand — no rebuild needed
    DynamicSpriteFont titleFont = _fontSystem.GetFont(48);
    DynamicSpriteFont bodyFont = _fontSystem.GetFont(16);
    DynamicSpriteFont smallFont = _fontSystem.GetFont(10);

    spriteBatch.Begin(samplerState: SamplerState.PointClamp);
    spriteBatch.DrawString(titleFont, "GAME OVER", new Vector2(100, 50), Color.Red);
    spriteBatch.DrawString(bodyFont, "Score: 12345", new Vector2(100, 110), Color.White);
    spriteBatch.DrawString(smallFont, "Press any key", new Vector2(100, 140), Color.Gray);
    spriteBatch.End();
}
```

### 7.3 FontStashSharp — Multiple Font Weights

```csharp
_fontSystem = new FontSystem();

// Load multiple weights — they're all in one FontSystem
LoadFont("fonts/Roboto-Regular.ttf");
LoadFont("fonts/Roboto-Bold.ttf");
LoadFont("fonts/Roboto-Italic.ttf");

// Or use separate FontSystems for different typefaces
_uiFontSystem = new FontSystem();
LoadFont(_uiFontSystem, "fonts/Inter-Regular.ttf");

_monoFontSystem = new FontSystem();
LoadFont(_monoFontSystem, "fonts/JetBrainsMono-Regular.ttf");

void LoadFont(FontSystem fs, string path)
{
    using Stream stream = TitleContainer.OpenStream(Path.Combine("Content", path));
    using MemoryStream ms = new();
    stream.CopyTo(ms);
    fs.AddFont(ms.ToArray());
}
```

### 7.4 Comparison Table

| Feature | MGCB SpriteFont | FontStashSharp |
|---|---|---|
| Fixed at build time | ✅ Yes | ❌ No — runtime |
| Any size at runtime | ❌ No | ✅ Yes |
| Bundle font file | ❌ System font only | ✅ Ship .ttf in content |
| Character ranges | Manual declaration | Auto — renders on demand |
| Quality | Adequate | Excellent (runtime hinting) |
| Cross-platform | ⚠️ Font must exist on build machine | ✅ Bundled, consistent |
| Emoji / CJK support | ❌ Limited | ✅ With appropriate .ttf |
| Performance | Pre-rasterized = fastest draw | First use of each glyph/size builds cache |

**Recommendation:** Use FontStashSharp for everything. MGCB SpriteFont is legacy.

### 7.5 FontStashSharp Gotchas

- **`.ttc` files not supported** — TrueType Collection files (common on macOS system fonts) cause `stbtt_InitFont failed`. Use individual `.ttf` files.
- **Do NOT load system fonts** — `File.ReadAllBytes("/System/Library/Fonts/...")` fails on iOS sandbox. Always bundle `.ttf` via MGCB `/copy`.
- **Use `TitleContainer.OpenStream()`** — the only cross-platform way to access content files. Works on Desktop, iOS, and Android.
- **Glyph atlas memory** — FontStashSharp creates internal textures for glyph caches. For many sizes/characters, monitor memory. Call `_fontSystem.Reset()` to clear if needed.

---

## 8. Audio Content

### 8.1 Audio Formats in MonoGame

| Format | Type | MGCB Processing | Use Case |
|---|---|---|---|
| `.wav` | Uncompressed PCM | Converts to platform-optimal format | Short sound effects (< 5 seconds) |
| `.ogg` | Compressed Vorbis | Passes through or re-encodes | Music, ambient loops, longer audio |
| `.mp3` | Compressed | Converts to platform format | Alternative to .ogg (licensing considerations) |

### 8.2 Sound Effects (.wav → SoundEffect)

```
#begin audio/sfx/jump.wav
/importer:WavImporter
/processor:SoundEffectProcessor
/processorParam:Quality=Best
/build:audio/sfx/jump.wav
```

**Processor quality settings:**

| Quality | Compression | File Size | Use When |
|---|---|---|---|
| `Best` | None (PCM) | Large | Short critical SFX (jump, hit, menu click) |
| `Medium` | ADPCM 4:1 | ~25% of original | General SFX |
| `Low` | ADPCM with more compression | Smallest | Ambient, less important sounds |

**Load and play:**
```csharp
SoundEffect jumpSfx = Content.Load<SoundEffect>("audio/sfx/jump");

// Fire and forget
jumpSfx.Play(volume: 0.8f, pitch: 0f, pan: 0f);

// Instance for control
SoundEffectInstance jumpInstance = jumpSfx.CreateInstance();
jumpInstance.Volume = 0.8f;
jumpInstance.Pitch = 0.1f;   // slight pitch variation
jumpInstance.Play();
```

### 8.3 Music (.ogg → Song)

```
#begin audio/music/overworld.ogg
/importer:OggImporter
/processor:SongProcessor
/build:audio/music/overworld.ogg
```

**Playback via MediaPlayer:**
```csharp
Song overworldMusic = Content.Load<Song>("audio/music/overworld");

MediaPlayer.IsRepeating = true;
MediaPlayer.Volume = 0.5f;
MediaPlayer.Play(overworldMusic);

// Crossfade or stop
MediaPlayer.Stop();
```

> **MediaPlayer limitations:** Only one `Song` plays at a time. For layered music or crossfading, use `SoundEffect` with longer audio files instead, or a library like [FAudio](https://github.com/FNA-XNA/FAudio).

### 8.4 .wav vs .ogg Decision Guide

| Criterion | .wav + SoundEffectProcessor | .ogg + SongProcessor |
|---|---|---|
| Latency | Near-zero (loaded in memory) | Slight (streamed from disk) |
| Memory usage | High (full PCM in RAM) | Low (streamed) |
| Best for | SFX: jump, hit, coin, explosion | Music: BGM, ambient loops |
| Max practical duration | ~10 seconds | Unlimited |
| Simultaneous playback | Many instances | One at a time (MediaPlayer) |

### 8.5 Audio Content Organization

```
Content/
└── audio/
    ├── sfx/
    │   ├── player/
    │   │   ├── jump.wav
    │   │   ├── land.wav
    │   │   ├── hurt.wav
    │   │   └── footstep_01.wav
    │   ├── ui/
    │   │   ├── click.wav
    │   │   └── hover.wav
    │   └── combat/
    │       ├── sword_swing.wav
    │       └── hit_impact.wav
    └── music/
        ├── overworld.ogg
        ├── dungeon.ogg
        └── boss.ogg
```

---

## 9. JSON/XML Data Loading

Game data — item definitions, dialogue trees, wave spawns, loot tables — should live in structured data files, not code.

### 9.1 Pattern: Copy + Runtime Deserialize

Don't process data files through MGCB. Copy them as-is and deserialize at runtime:

```
#begin data/items.json
/copy:data/items.json

#begin data/dialogue/intro.json
/copy:data/dialogue/intro.json
```

### 9.2 Loading JSON with System.Text.Json

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

// Data model
public class ItemDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("damage")]
    public int Damage { get; set; }

    [JsonPropertyName("rarity")]
    public ItemRarity Rarity { get; set; }

    [JsonPropertyName("sprite")]
    public string SpriteRegion { get; set; }
}

public enum ItemRarity { Common, Uncommon, Rare, Legendary }

public class ItemDatabase
{
    [JsonPropertyName("items")]
    public List<ItemDefinition> Items { get; set; }
}
```

**items.json:**
```json
{
    "items": [
        {
            "id": "sword_iron",
            "name": "Iron Sword",
            "damage": 10,
            "rarity": "Common",
            "sprite": "item_sword_iron"
        },
        {
            "id": "staff_fire",
            "name": "Fire Staff",
            "damage": 25,
            "rarity": "Rare",
            "sprite": "item_staff_fire"
        }
    ]
}
```

**Cross-platform loading:**
```csharp
public static class DataLoader
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static T Load<T>(string contentPath)
    {
        string fullPath = Path.Combine("Content", contentPath);
        using Stream stream = TitleContainer.OpenStream(fullPath);
        return JsonSerializer.Deserialize<T>(stream, _jsonOptions);
    }
}

// Usage
var itemDb = DataLoader.Load<ItemDatabase>("data/items.json");
var swordDef = itemDb.Items.First(i => i.Id == "sword_iron");
```

### 9.3 Dialogue System Example

**dialogue/intro.json:**
```json
{
    "nodes": {
        "start": {
            "speaker": "Elder",
            "text": "The forest grows dark, young one. Will you help us?",
            "choices": [
                { "text": "I'll help!", "next": "accept" },
                { "text": "Not my problem.", "next": "refuse" }
            ]
        },
        "accept": {
            "speaker": "Elder",
            "text": "Brave soul! Take this sword and head north.",
            "giveItem": "sword_iron",
            "next": null
        },
        "refuse": {
            "speaker": "Elder",
            "text": "Then darkness will consume us all...",
            "next": null
        }
    }
}
```

**Data classes:**
```csharp
public class DialogueTree
{
    [JsonPropertyName("nodes")]
    public Dictionary<string, DialogueNode> Nodes { get; set; }
}

public class DialogueNode
{
    [JsonPropertyName("speaker")]
    public string Speaker { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; }

    [JsonPropertyName("choices")]
    public List<DialogueChoice> Choices { get; set; }

    [JsonPropertyName("next")]
    public string Next { get; set; }

    [JsonPropertyName("giveItem")]
    public string GiveItem { get; set; }
}

public class DialogueChoice
{
    [JsonPropertyName("text")]
    public string Text { get; set; }

    [JsonPropertyName("next")]
    public string Next { get; set; }
}
```

### 9.4 Wave/Spawn Data Example

**data/waves/level1_waves.json:**
```json
{
    "waves": [
        {
            "delay": 0,
            "spawns": [
                { "enemy": "slime", "count": 3, "spawnPoint": "left" },
                { "enemy": "bat", "count": 2, "spawnPoint": "top" }
            ]
        },
        {
            "delay": 10.0,
            "spawns": [
                { "enemy": "skeleton", "count": 1, "spawnPoint": "right" },
                { "enemy": "slime", "count": 5, "spawnPoint": "left" }
            ]
        }
    ]
}
```

### 9.5 XML Loading (Legacy or XNA Content)

For XML data files or legacy XNA content:

```csharp
using System.Xml.Serialization;

public static T LoadXml<T>(string contentPath)
{
    string fullPath = Path.Combine("Content", contentPath);
    using Stream stream = TitleContainer.OpenStream(fullPath);
    var serializer = new XmlSerializer(typeof(T));
    return (T)serializer.Deserialize(stream);
}
```

> **Recommendation:** Prefer JSON over XML for new data files. JSON is smaller, faster to parse, and easier to edit by hand.

### 9.6 Resources Folder Pattern

For data files that don't need MGCB processing, you can also keep them in a separate `Resources/` folder outside Content entirely:

```
Resources/
├── items.json
├── dialogue/
│   ├── intro.json
│   └── shopkeeper.json
├── levels/
│   └── level1_spawns.json
└── config/
    └── balance.json
```

Copy to output directory via `.csproj`:
```xml
<ItemGroup>
  <Content Include="Resources\**\*">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
</ItemGroup>
```

> **Tradeoff:** The `Resources/` approach is simpler for Desktop but requires extra setup for iOS/Android bundling. For guaranteed cross-platform support, use MGCB `/copy` and `TitleContainer.OpenStream()`.

---

## 10. Hot-Reload Patterns

### 10.1 File Watcher for Rapid Iteration

During development, reload content when files change on disk — no restart needed:

```csharp
#if DEBUG
public class ContentHotReloader : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly ContentManager _content;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly ConcurrentQueue<string> _pendingReloads = new();

    public event Action<string> OnAssetReloaded;

    public ContentHotReloader(ContentManager content, GraphicsDevice graphicsDevice,
        string watchPath)
    {
        _content = content;
        _graphicsDevice = graphicsDevice;

        _watcher = new FileSystemWatcher(watchPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
            EnableRaisingEvents = true
        };

        _watcher.Changed += (s, e) => _pendingReloads.Enqueue(e.FullPath);
        _watcher.Created += (s, e) => _pendingReloads.Enqueue(e.FullPath);
    }

    public void Update()
    {
        while (_pendingReloads.TryDequeue(out string filePath))
        {
            try
            {
                string ext = Path.GetExtension(filePath).ToLower();

                switch (ext)
                {
                    case ".json":
                        OnAssetReloaded?.Invoke(filePath);
                        break;

                    case ".png":
                        // Reload texture from raw file (bypasses MGCB)
                        ReloadTexture(filePath);
                        break;
                }

                System.Diagnostics.Debug.WriteLine($"[HotReload] Reloaded: {filePath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HotReload] Error: {ex.Message}");
            }
        }
    }

    private void ReloadTexture(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var texture = Texture2D.FromStream(_graphicsDevice, stream);
        // Store in a dictionary keyed by asset name for lookup
        OnAssetReloaded?.Invoke(filePath);
    }

    public void Dispose() => _watcher?.Dispose();
}
#endif
```

### 10.2 JSON Data Hot-Reload

For data-driven games, hot-reloading JSON is the highest-value target:

```csharp
#if DEBUG
// In Game.Initialize
_hotReloader = new ContentHotReloader(Content, GraphicsDevice, "Content/data");
_hotReloader.OnAssetReloaded += path =>
{
    if (path.EndsWith("items.json"))
    {
        _itemDatabase = DataLoader.Load<ItemDatabase>("data/items.json");
        Debug.WriteLine($"Reloaded {_itemDatabase.Items.Count} items");
    }
    if (path.EndsWith("balance.json"))
    {
        _balanceConfig = DataLoader.Load<BalanceConfig>("data/balance.json");
        Debug.WriteLine("Balance config reloaded");
    }
};
#endif
```

### 10.3 Limitations

- **XNB content can't hot-reload** — MGCB-compiled assets require a rebuild. Hot-reload only works for raw files (JSON, copied PNGs, TTFs).
- **Texture2D.FromStream loads non-premultiplied** — use `BlendState.NonPremultiplied` or manually premultiply for hot-reloaded textures.
- **File watcher fires multiple events** — debounce by ignoring rapid successive changes (within ~100ms).
- **DEBUG-only** — wrap all hot-reload code in `#if DEBUG` to avoid shipping file watchers.

---

## 11. Content Organization Best Practices

### 11.1 Recommended Folder Structure

```
MyGame.Core/
├── Content/
│   ├── Content.mgcb              # MGCB manifest
│   ├── sprites/
│   │   ├── player.aseprite       # Aseprite source files
│   │   ├── enemies/
│   │   │   ├── slime.aseprite
│   │   │   └── bat.aseprite
│   │   ├── items/
│   │   │   └── pickups.aseprite
│   │   └── ui/
│   │       └── ui_atlas.png
│   ├── tilemaps/
│   │   ├── tilesets/
│   │   │   ├── dungeon_tiles.png
│   │   │   └── dungeon_tiles.tsx  # External Tiled tileset
│   │   ├── level1.tmx
│   │   └── level2.tmx
│   ├── fonts/
│   │   ├── JetBrainsMono-Regular.ttf
│   │   └── PixelFont.ttf
│   ├── audio/
│   │   ├── sfx/
│   │   │   ├── player/
│   │   │   ├── ui/
│   │   │   └── combat/
│   │   └── music/
│   │       ├── overworld.ogg
│   │       └── boss.ogg
│   ├── shaders/
│   │   ├── bloom.fx
│   │   └── palette_swap.fx
│   └── data/
│       ├── items.json
│       ├── enemies.json
│       ├── dialogue/
│       │   ├── intro.json
│       │   └── shopkeeper.json
│       └── waves/
│           └── level1_waves.json
```

### 11.2 Naming Conventions

| Rule | Example | Why |
|---|---|---|
| Lowercase, underscores | `player_idle.png` | Consistent, filesystem-safe across platforms |
| Prefix by category | `sfx_jump.wav`, `bgm_overworld.ogg` | Easy filtering and identification |
| Number with padding | `frame_01.png`, `frame_02.png` | Correct sort order |
| Match tag names | Aseprite tag "idle" → loaded as `"idle"` | Reduces mapping code |

### 11.3 Content Pipeline Decisions

| Asset Type | Pipeline Strategy | Why |
|---|---|---|
| Sprites / textures | MGCB `TextureProcessor` | Premultiplied alpha, platform optimization |
| Aseprite files | MGCB via MonoGame.Aseprite pipeline | Full animation support |
| Tiled maps | MGCB via Extended pipeline | Pre-parsed at build time |
| Fonts (.ttf) | MGCB `/copy` | FontStashSharp loads raw .ttf |
| Sound effects | MGCB `SoundEffectProcessor` | Platform-optimal compression |
| Music | MGCB `SongProcessor` | Streaming format |
| Shaders (.fx) | MGCB `EffectProcessor` | Compiled to platform shaders |
| JSON data | MGCB `/copy` | Loaded raw at runtime |
| Config files | MGCB `/copy` or `Resources/` | Human-editable, hot-reloadable |

---

## 12. iOS & Android Content Differences

### 12.1 What's the Same (Almost Everything)

The core value of MonoGame's content pipeline is **cross-platform transparency**. The same `Content.Load<T>()` call works everywhere:

```csharp
// This exact code runs on Desktop, iOS, and Android
Texture2D player = Content.Load<Texture2D>("sprites/player");
SoundEffect jump = Content.Load<SoundEffect>("audio/sfx/jump");
TiledMap level = Content.Load<TiledMap>("tilemaps/level1");
```

MGCB auto-compiles platform-appropriate `.xnb` formats when you target each platform.

### 12.2 File Access — The Critical Difference

| Platform | File System | Content Location |
|---|---|---|
| Desktop (DesktopGL) | Full filesystem access | `bin/Content/` directory |
| iOS | Sandboxed app bundle | Inside `.app` bundle |
| Android | APK archive | Inside `.apk` (read-only) |

**Rule: Always use `TitleContainer.OpenStream()` for non-MGCB content files.** It abstracts the platform differences:

```csharp
// ✅ Works everywhere
using Stream stream = TitleContainer.OpenStream(Path.Combine("Content", "data", "items.json"));

// ❌ Fails on iOS — sandboxed, path doesn't exist
string json = File.ReadAllText("Content/data/items.json");

// ❌ Fails on Android — content is inside APK, not on filesystem
string json = File.ReadAllText("Content/data/items.json");
```

### 12.3 Platform-Specific Content Settings

| Setting | Desktop | iOS | Android |
|---|---|---|---|
| `/platform:` | `DesktopGL` | `iOS` | `Android` |
| Texture format | DXT compression available | PVRTC or ETC2 | ETC1/ETC2 |
| Max texture size | 4096+ safe | 2048 safe, 4096 on newer devices | 2048 safe |
| Audio format | Platform choice | AAC preferred | OGG preferred |

> **Safe default:** Keep textures ≤ 2048×2048 and use `TextureFormat=Color` (uncompressed) for pixel art across all platforms.

### 12.4 iOS-Specific Notes

- Content is embedded in the `.app` bundle via `MonoGameContentReference` in `.csproj`.
- No `Resources/` folder access — if you use a separate Resources folder, you must add those files to the iOS project with `BundleResource` build action.
- System fonts aren't accessible — always bundle `.ttf` files.

### 12.5 Android-Specific Notes

- Content is packed inside the APK.
- For large games (>150 MB APK), use Android App Bundle (AAB) or APK expansion files.
- `TitleContainer.OpenStream()` handles reading from the APK transparently.
- Watch for case sensitivity — Android filesystems are case-sensitive unlike Windows.

---

## 13. Build Automation

### 13.1 MSBuild Targets for Content

Add custom MSBuild targets to automate content processing:

```xml
<!-- In MyGame.Core.csproj -->
<Target Name="PreBuildContent" BeforeTargets="Build">
  <!-- Copy data files to content directory -->
  <Copy SourceFiles="@(DataFiles)"
        DestinationFolder="Content\data\%(RecursiveDir)" />
</Target>

<Target Name="ValidateContent" AfterTargets="Build">
  <!-- Verify all expected content files exist after build -->
  <Error Condition="!Exists('$(OutputPath)\Content\data\items.json')"
         Text="Missing required content file: items.json" />
</Target>
```

### 13.2 Texture Atlas Build Automation

If using TexturePacker CLI, trigger atlas generation during build:

```xml
<Target Name="PackAtlases" BeforeTargets="BuildContent"
        Inputs="@(SpriteSource)" Outputs="Content\sprites\atlas.png">
  <Exec Command="TexturePacker --format json-hash --data Content/sprites/atlas.json --sheet Content/sprites/atlas.png raw_sprites/"
        Condition="Exists('raw_sprites')" />
</Target>
```

### 13.3 Content Build in CI/CD

```yaml
# GitHub Actions example
- name: Install MGCB
  run: dotnet tool install --global dotnet-mgcb

- name: Build Content
  run: mgcb Content/Content.mgcb /platform:DesktopGL

- name: Build Game
  run: dotnet build -c Release
```

### 13.4 Platform Matrix Build

Build content for all target platforms:

```bash
#!/bin/bash
# build_all_content.sh

PLATFORMS=("DesktopGL" "iOS" "Android")

for platform in "${PLATFORMS[@]}"; do
    echo "Building content for $platform..."
    mgcb Content/Content.mgcb /platform:$platform /outputDir:bin/$platform
done
```

Or via MSBuild:
```xml
<Target Name="BuildAllPlatforms">
  <Exec Command="mgcb Content.mgcb /platform:DesktopGL /outputDir:bin/DesktopGL" />
  <Exec Command="mgcb Content.mgcb /platform:iOS /outputDir:bin/iOS" />
  <Exec Command="mgcb Content.mgcb /platform:Android /outputDir:bin/Android" />
</Target>
```

---

## 14. Common Content Pipeline Issues

### 14.1 Troubleshooting Table

| Problem | Cause | Fix |
|---|---|---|
| `ContentLoadException: file not found` | Asset not in `.mgcb` or wrong path | Check `.mgcb` entries and file paths (case-sensitive on Linux/Android) |
| Dark fringes around sprites | Wrong BlendState for alpha mode | Use `AlphaBlend` for MGCB sprites, `NonPremultiplied` for `FromStream()` |
| Blurry pixel art | Wrong SamplerState | Use `SamplerState.PointClamp` everywhere |
| Font crashes on iOS | Loading system fonts via `File.ReadAllBytes` | Bundle `.ttf` via `/copy`, use `TitleContainer.OpenStream()` |
| `stbtt_InitFont failed` | FontStashSharp given a `.ttc` file | Use individual `.ttf` files, not `.ttc` collections |
| Tiled map tileset not found | Tileset PNG not added to `.mgcb` | Add tileset images as separate content entries |
| Pipeline DLL not found | Wrong `/reference:` path in `.mgcb` | Update path to point to correct pipeline DLL location |
| Content builds but doesn't appear | Missing `MonoGameContentReference` in `.csproj` | Add `<MonoGameContentReference>` to project file |
| Texture appears as solid color | Texture format incompatible with platform | Use `TextureFormat=Color` for maximum compatibility |
| Audio pops/clicks | WAV sample rate mismatch | Normalize audio to 44100 Hz before import |

### 14.2 Debugging Content Builds

```bash
# Verbose MGCB output — shows every importer/processor decision
mgcb Content.mgcb /platform:DesktopGL /verbose

# List what MGCB would build without building
mgcb Content.mgcb /platform:DesktopGL /launchdebugger
```

In code, verify content is loaded:
```csharp
try
{
    var texture = Content.Load<Texture2D>("sprites/player");
    Debug.WriteLine($"Loaded: {texture.Width}x{texture.Height}");
}
catch (ContentLoadException ex)
{
    Debug.WriteLine($"Content load failed: {ex.Message}");
    Debug.WriteLine($"Content root: {Content.RootDirectory}");
}
```

---

## Quick Reference

### Content Loading Cheat Sheet

```csharp
// MGCB-compiled assets (textures, audio, effects, Aseprite, Tiled)
var tex       = Content.Load<Texture2D>("sprites/player");
var sfx       = Content.Load<SoundEffect>("audio/sfx/jump");
var song      = Content.Load<Song>("audio/music/overworld");
var effect    = Content.Load<Effect>("shaders/bloom");
var aseFile   = Content.Load<AsepriteFile>("sprites/player");
var tiledMap  = Content.Load<TiledMap>("tilemaps/level1");

// Copied files (JSON, TTF) — use TitleContainer
using var stream = TitleContainer.OpenStream(
    Path.Combine("Content", "data", "items.json"));

// FontStashSharp
using var fontStream = TitleContainer.OpenStream(
    Path.Combine("Content", "fonts", "MyFont.ttf"));
```

### Pipeline Strategy Summary

| Asset | Import Method | Runtime Type | Notes |
|---|---|---|---|
| Textures | MGCB `TextureProcessor` | `Texture2D` | `PremultiplyAlpha=True` for pixel art |
| Aseprite | MGCB `AsepriteFileProcessor` | `AsepriteFile` → `SpriteSheet` → `AnimatedSprite` | No manual export needed |
| Tiled maps | MGCB `TiledMapProcessor` | `TiledMap` | Include tileset PNGs separately |
| Fonts | MGCB `/copy` | `DynamicSpriteFont` (FontStashSharp) | Bundle .ttf, load with `TitleContainer` |
| SFX | MGCB `SoundEffectProcessor` | `SoundEffect` | Use .wav source, Quality=Best for short clips |
| Music | MGCB `SongProcessor` | `Song` | Use .ogg source, streamed playback |
| Shaders | MGCB `EffectProcessor` | `Effect` | Auto-compiles to platform shaders |
| Game data | MGCB `/copy` | Deserialized C# objects | JSON + `System.Text.Json` |
| Config | `/copy` or `Resources/` | Deserialized C# objects | Hot-reloadable in DEBUG |
