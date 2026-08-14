# G22 — Parallax & Depth Layers


> **Category:** Guide · **Related:** [G2 Rendering & Graphics](./G2_rendering_and_graphics.md) · [G1 Custom Code Recipes](./G1_custom_code_recipes.md) · [G20 Camera Systems](./G20_camera_systems.md) · [G19 Display & Resolution](./G19_display_resolution_viewports.md)

---

## What Parallax Does

Parallax scrolling creates an illusion of depth by moving background layers at different speeds relative to the camera. Distant layers (mountains, sky) scroll slowly; close layers (foreground bushes, rain) scroll quickly. The player layer scrolls at 1:1 with the camera.

This is the cheapest and most effective way to add perceived depth to a 2D game.

---

## The Scroll Factor Formula

Each layer has a **scroll factor** (also called parallax factor or scroll rate):

```
layerDrawOffset = cameraPosition * scrollFactor
```

| Scroll Factor | Effect | Example |
|---------------|--------|---------|
| 0.0 | Static — doesn't move at all | Fixed sky backdrop, sun |
| 0.2 | Very slow — far background | Distant mountains, clouds |
| 0.5 | Half speed — mid background | Tree line, buildings |
| 1.0 | Full speed — game layer | Player, enemies, tiles |
| 1.5 | Faster than camera — near foreground | Close bushes, fog |
| 2.0 | Double speed — very close foreground | Rain streaks, dust |

**Key insight:** Factors below 1.0 are behind the player. Factors above 1.0 are in front. The player layer is always 1.0.

---

## Implementation with RenderLayerSystem

Your render layer system from [G1](./G1_custom_code_recipes.md) already handles draw ordering. Parallax adds a per-layer camera offset.

### Approach 1: Modified SpriteBatch Transform per Layer

The simplest approach — adjust the camera matrix for each layer by its scroll factor:

```csharp
/// <summary>Get a view matrix with parallax applied.</summary>
public static Matrix GetParallaxViewMatrix(OrthographicCamera camera, float scrollFactor)
{
    // Scale the camera position by the scroll factor
    Vector2 parallaxPosition = camera.Position * scrollFactor;

    return Matrix.CreateTranslation(-parallaxPosition.X, -parallaxPosition.Y, 0f)
        * Matrix.CreateRotationZ(-camera.Rotation)
        * Matrix.CreateScale(camera.Zoom, camera.Zoom, 1f)
        * Matrix.CreateTranslation(
            camera.BoundingRectangle.Width / 2f * camera.Zoom,
            camera.BoundingRectangle.Height / 2f * camera.Zoom, 0f);
}
```

**Usage in render loop:**

```csharp
// Draw each layer with its parallax factor
foreach (RenderLayer layer in _layers.OrderBy(l => l.Order))
{
    Matrix viewMatrix = GetParallaxViewMatrix(_camera, layer.ScrollFactor);

    spriteBatch.Begin(
        sortMode: SpriteSortMode.Deferred,
        samplerState: SamplerState.PointClamp,
        transformMatrix: viewMatrix);

    foreach (IRenderable item in layer.Items)
        item.Draw(spriteBatch);

    spriteBatch.End();
}
```

### Approach 2: Offset Positions Directly

If you don't want to change the SpriteBatch matrix per layer, offset each sprite's position:

```csharp
// In draw for a parallax layer:
Vector2 offset = _camera.Position * (1f - scrollFactor);
Vector2 drawPosition = spriteWorldPosition - offset;
spriteBatch.Draw(texture, drawPosition, Color.White);
```

This is simpler but doesn't compose as cleanly with camera zoom and rotation.

---

## RenderLayer with ScrollFactor

Extend the `RenderLayer` from [G1](./G1_custom_code_recipes.md):

```csharp
public class RenderLayer
{
    public int Order { get; set; }
    public float ScrollFactor { get; set; } = 1.0f;  // 1.0 = normal game layer
    public List<IRenderable> Items { get; } = new();
    public Effect PostProcessor { get; set; }
    public RenderTarget2D Target { get; set; }
}
```

### Typical Layer Setup

```csharp
var layers = new RenderLayer[]
{
    new() { Order = 0, ScrollFactor = 0.0f },  // Sky (static)
    new() { Order = 1, ScrollFactor = 0.2f },  // Far mountains
    new() { Order = 2, ScrollFactor = 0.5f },  // Mid trees
    new() { Order = 3, ScrollFactor = 0.8f },  // Near buildings
    new() { Order = 4, ScrollFactor = 1.0f },  // Game world (player, enemies, tiles)
    new() { Order = 5, ScrollFactor = 1.0f },  // Game world overlay (effects above entities)
    new() { Order = 6, ScrollFactor = 1.3f },  // Near foreground (bushes, fog)
    new() { Order = 7, ScrollFactor = 0.0f },  // HUD (static, no camera transform)
};
```

---

## Infinite Scrolling / Tiling

Background layers often need to repeat infinitely. Two approaches:

### Texture Wrapping (GPU)

If the background is a single repeating texture, use `SamplerState.LinearWrap` (or `PointWrap` for pixel art) and draw a quad larger than the screen:

```csharp
/// <summary>Draw an infinitely tiling background texture.</summary>
public void DrawTilingBackground(SpriteBatch spriteBatch, Texture2D texture,
    OrthographicCamera camera, float scrollFactor)
{
    // Calculate the visible area at this parallax factor
    Vector2 offset = camera.Position * scrollFactor;
    RectangleF visible = camera.BoundingRectangle;

    // Source rectangle in texture coordinates — wrapping handles the tiling
    Rectangle source = new(
        (int)offset.X, (int)offset.Y,
        (int)visible.Width, (int)visible.Height);

    // Draw fullscreen
    spriteBatch.Begin(samplerState: SamplerState.PointWrap);
    spriteBatch.Draw(texture, Vector2.Zero, source, Color.White);
    spriteBatch.End();
}
```

**Requirement:** The texture must be power-of-2 dimensions (256x256, 512x256, etc.) for `SamplerState.Wrap` to work in MonoGame's Reach profile. `HiDef` profile supports non-power-of-2 wrapped textures.

### Manual Tiling (Multiple Draws)

For non-power-of-2 textures or sprites from an atlas:

```csharp
/// <summary>Draw a horizontally tiling background layer.</summary>
public void DrawTilingLayer(SpriteBatch spriteBatch, Texture2D texture,
    float cameraX, float scrollFactor, int screenWidth, int screenHeight)
{
    float offsetX = (cameraX * scrollFactor) % texture.Width;
    if (offsetX > 0) offsetX -= texture.Width; // Ensure we start from the left

    for (float x = offsetX; x < screenWidth; x += texture.Width)
    {
        spriteBatch.Draw(texture, new Vector2(x, 0), Color.White);
    }
}
```

---

## Y-Sort Rendering (Top-Down Games)

In top-down games (Zelda, Stardew Valley, isometric), entities within the same render layer should be sorted by their Y position. Entities lower on screen (higher Y) draw on top, creating the illusion of depth.

### SpriteBatch Y-Sort

MonoGame's `SpriteSortMode.FrontToBack` combined with `layerDepth` enables Y-sorting without manual sorting:

```csharp
spriteBatch.Begin(
    sortMode: SpriteSortMode.FrontToBack,
    samplerState: SamplerState.PointClamp,
    transformMatrix: camera.GetViewMatrix());

// layerDepth 0 = back, 1 = front
// Normalize Y position to 0-1 range based on world bounds
float layerDepth = pos.Y / worldHeight;
spriteBatch.Draw(texture, position, sourceRect, Color.White,
    rotation: 0f, origin: Vector2.Zero, scale: 1f,
    effects: SpriteEffects.None, layerDepth: layerDepth);

spriteBatch.End();
```

### ECS-Based Y-Sort

Sort entities before drawing:

```csharp
/// <summary>Component to override automatic Y-sort depth.</summary>
public struct SortOffset
{
    public float Y; // Added to entity Y for sort purposes (e.g., feet position vs center)
}

// In render system, collect and sort:
_renderList.Clear();

world.Query(in renderQuery, (Entity entity, ref Position pos, ref Sprite sprite) =>
{
    float sortY = pos.Y + (entity.Has<SortOffset>() ? entity.Get<SortOffset>().Y : 0f);
    _renderList.Add((entity, sortY));
});

_renderList.Sort((a, b) => a.sortY.CompareTo(b.sortY));

foreach (var (entity, _) in _renderList)
{
    ref Position pos = ref entity.Get<Position>();
    ref Sprite sprite = ref entity.Get<Sprite>();
    spriteBatch.Draw(sprite.Texture, new Vector2(pos.X, pos.Y), Color.White);
}
```

**SortOffset is important:** For a character sprite, the sort position should be at their feet (bottom of sprite), not the center. A tall tree's sort position is its trunk base. This creates correct overlapping when entities walk behind trees.

---

## Z-Index Within Layers

When multiple entities share the same render layer and Y-sort isn't appropriate (e.g., a side-view game), use a Z-index component:

```csharp
/// <summary>Draw order within a render layer. Higher = drawn later (on top).</summary>
public struct ZIndex
{
    public int Value;
}
```

Sort by `ZIndex.Value` within each layer before drawing. Entities with the same Z-index are drawn in arbitrary order (or by insertion order if stable sort is used).

### Common Z-Index Assignments

| Z-Index | Contents |
|---------|----------|
| 0 | Ground tiles, floor |
| 10 | Ground decals (shadows, footprints) |
| 20 | Dropped items |
| 50 | Entities (player, enemies, NPCs) — Y-sorted among themselves |
| 60 | Entity overlays (held items, equipment) |
| 80 | Projectiles |
| 90 | Particle effects |
| 100 | Above-entity effects (overhead bridges, canopy) |

Use gaps (10s or 50s) so you can insert new categories later without renumbering.

---

## Combining Parallax + Y-Sort + Z-Index

For a top-down game with parallax backgrounds:

1. **Parallax layers** (order 0-3, scroll factors 0.0-0.8): Background art, no sorting needed
2. **Game layer** (order 4, scroll factor 1.0): Y-sorted entities with Z-index tiebreakers
3. **Foreground parallax** (order 5-6, scroll factor 1.2+): Near decoration, no sorting needed
4. **HUD layer** (order 7, no camera transform): UI elements

```
Draw order:
  Sky (static)          → scrollFactor 0.0
  Mountains             → scrollFactor 0.2
  Trees                 → scrollFactor 0.5
  Ground tiles          → scrollFactor 1.0, zIndex 0
  Player shadow         → scrollFactor 1.0, zIndex 10
  NPCs (Y-sorted)       → scrollFactor 1.0, zIndex 50
  Player (Y-sorted)     → scrollFactor 1.0, zIndex 50
  Projectiles           → scrollFactor 1.0, zIndex 80
  Particles             → scrollFactor 1.0, zIndex 90
  Foreground bushes     → scrollFactor 1.3
  HUD                   → no camera transform
```

---

## Performance Notes

- **Parallax layers are cheap.** Each is typically 1-3 sprite draws (tiling backgrounds). The cost is the SpriteBatch begin/end per layer, which is minimal.
- **Y-sorting has a cost.** Sorting N entities is O(N log N) per frame. For <1000 entities, this is negligible. For more, consider spatial partitioning or only Y-sorting visible entities (post-culling).
- **Avoid too many render layers.** Each layer with a different SpriteBatch configuration is a separate draw call batch. 5-10 layers is typical. 20+ layers may impact mobile performance.

---

## See Also

- [G1 Custom Code Recipes](./G1_custom_code_recipes.md) — RenderLayer system implementation
- [G2 Rendering & Graphics](./G2_rendering_and_graphics.md) — render pipeline, post-processors
- [G20 Camera Systems](./G20_camera_systems.md) — camera transforms for parallax
- [G15 Game Loop](./G15_game_loop.md) — culling and batching strategies
