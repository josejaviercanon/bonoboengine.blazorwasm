# 09 — Art Production Pipeline



A practical guide to creating, organizing, and shipping 2D game art — aimed at solo developers and small teams who may be programmers first and artists second.

---

## Table of Contents

1. [Art Style Selection](#1-art-style-selection)
2. [Aseprite Workflow](#2-aseprite-workflow)
3. [Asset Naming Conventions](#3-asset-naming-conventions)
4. [Sprite Sheet Organization](#4-sprite-sheet-organization)
5. [Animation Pipeline](#5-animation-pipeline)
6. [Tileset Creation](#6-tileset-creation)
7. [Placeholder Art Strategy](#7-placeholder-art-strategy)
8. [UI Art](#8-ui-art)
9. [Particle & Effect Art](#9-particle--effect-art)
10. [Background & Parallax Art](#10-background--parallax-art)
11. [Art Optimization](#11-art-optimization)
12. [Art Production Schedule](#12-art-production-schedule)

---

## 1. Art Style Selection

Choosing an art style is one of the most impactful decisions you'll make. It determines your workflow speed, your tool choices, and what your game *feels* like before a single line of dialogue is read.

### Style Comparison

| Style | Strengths | Weaknesses | Best For |
|---|---|---|---|
| **Pixel Art** | Clear constraints, forgiving at small sizes, nostalgic appeal, small file sizes | Time-consuming at high res, animation is frame-by-frame, can feel "generic" if not distinctive | Platformers, RPGs, roguelikes, any game at ≤320×180 base resolution |
| **Hand-Drawn / Painted** | Unique visual identity, expressive, stands out | Consistency is hard, slow to produce, large file sizes | Story-driven games, visual novels, games where art *is* the hook |
| **Vector / Clean Shape** | Scales perfectly, fast iteration, easy recoloring | Can feel flat or "mobile game", less texture/warmth | UI-heavy games, minimalist aesthetics, games targeting multiple resolutions |

### Resolution Implications

Your art style locks you into a base resolution. Decide this early:

- **Pixel art** — Common base resolutions: 320×180 (16:9), 384×216, 256×144. Rendered at integer scale (2×, 3×, 4×) to fill the screen. Every pixel is deliberate.
- **Hand-drawn** — Work at or above target resolution. A 1920×1080 target means sprites drawn at 1080p or higher, then scaled down for crispness.
- **Vector** — Resolution-independent source files, rasterized at build time or rendered via signed distance fields.

**Rule of thumb:** Pick the smallest base resolution you can get away with. Smaller canvas = fewer pixels to draw = faster iteration.

### Color Palette Strategies

A limited, intentional palette does more for visual cohesion than any amount of rendering skill.

**Limited Palette Approach:**
- Start with 8–16 colors. Expand only when you *need* a color you can't mix.
- Classic palettes to study: PICO-8 (16 colors), Endesga 32, Resurrect 64, DawnBringer 32.
- Constraints breed creativity. A 16-color game with strong design beats a true-color game with no direction.

**Warm/Cool Strategy:**
- Use **warm colors** (reds, oranges, yellows) for foreground, interactive, or dangerous elements.
- Use **cool colors** (blues, purples, greens) for backgrounds, safe zones, environment.
- Shift hue toward blue/purple in shadows, toward yellow/orange in highlights — never just darken or lighten.

**HSL Relationships:**
- **Hue shifting** — As you move from light to shadow, shift the hue 15–30° (e.g., yellow highlights → orange midtones → red-brown shadows). This gives life to flat shading.
- **Saturation** — Midtones are most saturated. Highlights and deep shadows desaturate.
- **Lightness** — Aim for 3–5 value steps per color ramp. Squint at your sprite; if the silhouette reads clearly, your values are working.

### Style Consistency Checklist

Run through this when evaluating your art:

- [ ] **Outline style** — Consistent across all assets? (black outlines, colored outlines, no outlines — pick one)
- [ ] **Line weight** — Same thickness everywhere? (1px for pixel art, consistent brush for hand-drawn)
- [ ] **Shading method** — Flat, cel-shaded, dithered, soft? Don't mix approaches
- [ ] **Light source** — Same direction across all sprites (top-left is conventional)
- [ ] **Proportions** — Character head-to-body ratio consistent? (2-head chibi, 3-head stylized, etc.)
- [ ] **Color palette** — All assets pulling from the same palette?
- [ ] **Detail density** — Similar level of detail per pixel/unit area across assets
- [ ] **Edge treatment** — Anti-aliased or hard edges? Consistent across all art

### Reference and Mood Boards

Before drawing anything, collect references:

1. **Create a folder:** `Reference/` at your project root (not in `Content/` — these don't ship)
2. **Gather 20–30 images** that capture the feeling you want: other games, illustrations, photographs, color palettes, UI screenshots
3. **Organize by category:** `Reference/Characters/`, `Reference/Environments/`, `Reference/UI/`, `Reference/Mood/`
4. **Make a mood board:** A single image collage of your top 8–10 references. Keep it open while you work. Tools: PureRef (free), Milanote, or just a folder of PNGs

The mood board is your north star. When you're unsure if a sprite "fits," hold it against the board.

---

## 2. Aseprite Workflow

Aseprite is the standard tool for 2D game sprites — pixel art especially, but capable for any raster work. This section covers the workflow from blank canvas to exported asset.

### Project Setup

**New File Settings:**
- **Color Mode:** RGB Color (even for limited palettes — you can enforce palette via indexed mode later)
- **Canvas Size:** Match your sprite's *actual* pixel dimensions, not the screen resolution. A 16×16 character is a 16×16 canvas.
- **Background:** Transparent (checkered). Never use a colored background layer unless intentional.

**Common Canvas Sizes:**

| Asset Type | Typical Sizes | Notes |
|---|---|---|
| Character sprite | 16×16, 24×24, 32×32, 48×48, 64×64 | Include room for animations that extend beyond idle bounds |
| Tileset tile | 16×16, 32×32 | Must be exact — no partial pixels |
| UI icon | 8×8, 16×16, 24×24 | Design at native size, don't scale down |
| Large boss / setpiece | 64×64, 128×128, 256×256 | Consider splitting into parts for animation |
| Effect / particle | 4×4, 8×8, 16×16 | Smaller is usually better |

**Palette Setup:**
1. Edit → Options → set your palette as default, or import a `.gpl` / `.pal` file
2. Lock palette early. Sprite → Color Mode → Indexed if you want strict enforcement.
3. Sort palette: lights to darks, grouped by hue ramp

### Layer Naming Conventions

Consistent layer names across all your `.aseprite` files make batch operations and code integration predictable.

```
├── fx          ← Hit sparks, glows, transient effects (optional)
├── weapon      ← Held items, rendered above or below body as needed
├── body        ← Main sprite body
├── shadow      ← Drop shadow or ground shadow
└── [bg]        ← Reference/background layer (hidden on export)
```

**Rules:**
- Lowercase, no spaces, underscore-separated if needed
- Prefix reference-only layers with `[brackets]` — these won't export
- Use layer groups for complex characters: `head/`, `torso/`, `legs/`

### Animation Workflow

1. **Block out key poses first** — Frame 1 (idle/start), apex frame, end frame. Get the motion right with 3 frames before adding in-betweens.
2. **Use onion skinning** — View → Onion Skinning (or press `F3`). Show 1–2 frames before/after. Reduce opacity to 30–40%.
3. **Set frame durations** — Click the frame duration at the bottom of the timeline. Common durations: 100ms (10 fps game feel), 83ms (12 fps smooth), 66ms (15 fps fluid). Use longer holds on key poses (150–200ms on impact frames).
4. **Tag your animations** — Select frames → right-click → New Tag. Name it: `idle`, `walk`, `attack`, `hurt`, `death`. Tags export as named animations in most pipelines.
5. **Loop settings** — Set tag direction: Forward, Reverse, or Ping-Pong. Ping-pong is great for breathing/idle animations (saves frames).

### Aseprite Tips

- **Tile Mode** (View → Tiled Mode) — Essential for seamless tilesets. Shows your canvas tiled in all directions while you paint.
- **Symmetry** (View → Symmetry Options) — Vertical symmetry for front-facing characters. Draw half, get the other free. Break symmetry intentionally for personality.
- **Ink: Shading** — Select the Shading ink mode to cycle through your palette ramps with a single brush stroke. Massive time saver for pixel art shading.
- **Color Curves** (Edit → Adjustments → Curves) — For batch-adjusting value/contrast across frames.
- **Export Spritesheet** (File → Export Sprite Sheet) — Use "By Rows" or "Packed" layout. Set padding to 1–2px to prevent texture bleeding. Check "Trim Cels" to remove empty space per frame. Export the JSON data file alongside the PNG.

### Aseprite → MonoGame Pipeline

The recommended integration uses **MonoGame.Aseprite**, which loads `.aseprite` files directly through the content pipeline — no manual export step needed.

```
YourGame/
└── Content/
    └── Sprites/
        ├── player.aseprite    ← Source file, processed at build time
        ├── enemy_slime.aseprite
        └── ...
```

**Setup:**
1. Add `MonoGame.Aseprite` NuGet package (v6.x+)
2. Load `.aseprite` files at runtime — no content pipeline step needed
3. The library reads tags, layers, and frame data automatically
4. In code, load and create a sprite sheet:

```csharp
// Load the Aseprite file
AsepriteFile aseFile = AsepriteFile.Load("Content/Sprites/player.aseprite");

// Create a SpriteSheet from it (includes all tagged animations)
SpriteSheet spriteSheet = aseFile.CreateSpriteSheet(GraphicsDevice);
```

See [G8 — Content Pipeline](../../monogame-arch/guides/G8_content_pipeline.md) for full content pipeline configuration, including custom processors and build actions.

**Key advantage:** Edit in Aseprite, save, rebuild, see changes. No export-copy-reimport cycle.

---

## 3. Asset Naming Conventions

Consistent naming prevents chaos at scale. Adopt a convention on day one and stick to it.

### File Naming

**Individual sprites (if not using spritesheets):**

```
{entity}_{action}_{direction}_{frame}.png
```

Examples:
```
player_idle_right_01.png
player_walk_right_01.png
player_walk_right_02.png
enemy_slime_idle_down_01.png
npc_merchant_talk_01.png
```

**Spritesheet naming:**

```
{entity}_{action}.png          ← Single animation sheet
{entity}_spritesheet.png       ← All animations packed
{entity}.aseprite              ← Source file (if using MonoGame.Aseprite)
```

**Tileset naming:**

```
tileset_{biome}_{variant}.png
```

Examples:
```
tileset_forest_ground.png
tileset_cave_walls.png
tileset_dungeon_autotile.png
```

### Naming Rules

- **All lowercase** — Avoids cross-platform filename issues (Linux is case-sensitive)
- **Underscores** for word separation — No spaces, no hyphens, no camelCase
- **Zero-padded frame numbers** — `01`, `02`, ... `12` (not `1`, `2`)
- **No special characters** — Stick to `a-z`, `0-9`, `_`
- **Be specific** — `enemy_bat_fly_01.png` not `sprite3_v2_final_FINAL.png`

### Folder Organization

```
Content/
├── Sprites/
│   ├── Player/
│   │   ├── player.aseprite
│   │   └── player_spritesheet.png      ← (if manually exported)
│   ├── Enemies/
│   │   ├── enemy_slime.aseprite
│   │   ├── enemy_bat.aseprite
│   │   └── ...
│   ├── NPCs/
│   │   └── npc_merchant.aseprite
│   └── Props/
│       ├── prop_chest.aseprite
│       └── prop_door.aseprite
├── Tilesets/
│   ├── tileset_forest.aseprite
│   ├── tileset_cave.aseprite
│   └── ...
├── UI/
│   ├── ui_button.png
│   ├── ui_panel_9slice.png
│   ├── ui_healthbar.png
│   └── Icons/
│       ├── icon_sword.png
│       ├── icon_potion.png
│       └── ...
├── Particles/
│   ├── particle_circle_soft.png
│   ├── particle_spark.png
│   └── particle_smoke.png
├── Backgrounds/
│   ├── bg_forest_sky.png
│   ├── bg_forest_mountains.png
│   ├── bg_forest_trees.png
│   └── ...
└── Fonts/
    ├── ui_font.ttf
    └── dialog_font.ttf
```

**Guidelines:**
- Group by **function**, not by file type
- Subfolders for entity categories (Enemies, NPCs), not per-entity unless the entity has many files
- Keep the tree shallow — 2–3 levels max
- Source `.aseprite` files live alongside their exports (or replace exports entirely if using MonoGame.Aseprite)

---

## 4. Sprite Sheet Organization

### Individual Sprites vs Sheets

**Use individual sprites when:**
- Prototyping — easiest to swap in/out
- Sprites are large (>128×128) and few in number
- You're using MonoGame.Aseprite (it handles frame data internally)

**Use sprite sheets when:**
- You have many small sprites (particles, UI icons, items)
- You need to minimize texture swaps for performance
- You're building a texture atlas for an entire entity or scene

**Use a texture atlas when:**
- Combining multiple unrelated sprites into one texture
- Targeting mobile or web where draw calls are expensive
- Shipping final builds where loading time matters

### Atlas Packing Strategies

**Packing layouts:**

| Layout | Pros | Cons |
|---|---|---|
| **Grid (uniform)** | Simple, easy to index by math | Wastes space if sprites vary in size |
| **Strip (single row)** | Good for single animations | Wide textures, poor atlas utilization |
| **Packed (bin-pack)** | Optimal space usage | Needs metadata (JSON/XML) for coordinates |

**Aseprite export settings for sheets:**
1. File → Export Sprite Sheet
2. Layout: "Packed" for best space efficiency, "By Rows" for simplicity
3. **Border Padding:** 1–2px (prevents bleeding at edges of the texture)
4. **Spacing:** 1px between sprites (prevents bleed between adjacent frames)
5. **Inner Padding:** 0px (usually fine; add 1px if you see edge artifacts)
6. Check **Trim Cels** — removes per-frame whitespace, packs tighter
7. Output: PNG + JSON (Array) — the JSON maps frame names to source rectangles

### Atlas Tools

- **Aseprite** — Built-in sheet export, good for per-entity sheets
- **TexturePacker** — Industry standard for combining multiple sources into one atlas. Exports MonoGame-compatible formats.
- **Free alternatives:** ShoeBox (free), Littera (for bitmap fonts), or write a simple packing script with `ImageSharp`
- **MonoGame Content Pipeline** — Can process sprite sheets directly. Configure the `.mgcb` file with the appropriate importer/processor.

### MonoGame Content Pipeline Setup

In your `.mgcb` file:
```
#begin Sprites/player_spritesheet.png
/importer:TextureImporter
/processor:TextureProcessor
/processorParam:GenerateMipmaps=False
/processorParam:TextureFormat=Color
/processorParam:PremultiplyAlpha=True
/build:Sprites/player_spritesheet.png
```

Load in code, then define source rectangles from the JSON metadata or manually:
```csharp
var texture = Content.Load<Texture2D>("Sprites/player_spritesheet");
var frameRect = new Rectangle(0, 0, 32, 32); // First frame
```

See [G8 — Content Pipeline](../../monogame-arch/guides/G8_content_pipeline.md) for advanced content pipeline configuration.

---

## 5. Animation Pipeline

### Frame Count Guidelines

These are starting points — adjust based on your game's feel and your time budget.

| Animation | Frame Count | Duration | Notes |
|---|---|---|---|
| **Idle** | 4–8 frames | 800ms–1.5s loop | Subtle breathing, blinking. Ping-pong saves frames. |
| **Walk** | 6–8 frames | 400–600ms loop | Contact-pass-contact-pass cycle. Even frame counts loop cleanly. |
| **Run** | 6–8 frames | 300–400ms loop | Wider stride, more lean. Faster timing than walk. |
| **Jump (up)** | 2–3 frames | 100–200ms | Anticipation squat → launch. Often a single held frame at apex. |
| **Fall** | 1–2 frames | Held | Distinct from jump-up. Arms/legs trailing. |
| **Land** | 2–3 frames | 100–150ms | Squat impact → recover. Satisfying squash. |
| **Attack (light)** | 4–6 frames | 200–400ms | Anticipation (1–2f) → swing (1–2f) → recovery (1–2f) |
| **Attack (heavy)** | 6–10 frames | 400–700ms | Longer wind-up, bigger follow-through |
| **Hurt/Hit** | 2–3 frames | 150–250ms | Flinch back. Often combined with screen flash or hitstop. |
| **Death** | 4–8 frames | 500ms–1s | Collapse, fade, poof — depends on tone |
| **Interact** | 2–4 frames | 200–400ms | Reaching, picking up, pressing button |

### Animation Principles for Game Sprites

You don't need Disney's 12 principles. You need three:

**1. Anticipation**
Before an action, show preparation. A jump starts with a crouch. An attack starts with a wind-up. Even 1 frame of anticipation makes movement feel intentional.

**2. Squash and Stretch**
Exaggerate compression (landing, winding up) and extension (jumping, swinging). For pixel art, this might be 1–2 pixels of height difference. It's subtle but critical.

**3. Follow-Through**
After the main action, things settle. Hair keeps moving after the character stops. A sword swing continues past the hit point. Recovery frames give weight.

### Timing Guidelines

- **Snappy actions** (attacks, jumps): Front-load the anticipation, make the action itself fast (1–2 frames), linger on follow-through
- **Looping animations** (idle, walk): Even, rhythmic timing. Avoid single frames that pop out
- **Hitstop/Freeze frames**: Pause the animation for 2–5 frames (30–80ms) on impact. Makes hits feel powerful. This is game logic, not art — but design your impact frames to hold well.
- **Holds**: Not every frame needs to be unique. Holding a key pose for 2–3 frames (with the same image) is a valid animation technique.

### Animation State Machines

Your animation system should map game states to animation clips. Common state graph:

```
         ┌──────────┐
    ┌────│   Idle   │────┐
    │    └──────────┘    │
    ▼         │          ▼
┌──────┐      │     ┌────────┐
│ Walk │      │     │ Attack │
└──────┘      │     └────────┘
    │         ▼          │
    │    ┌──────────┐    │
    └───▶│   Jump   │◀───┘
         └──────────┘
              │
         ┌──────────┐
         │   Fall   │
         └──────────┘
              │
         ┌──────────┐
         │   Land   │
         └──────────┘
```

See [G31 — Animation State Machines](../../monogame-arch/guides/G31_animation_state_machines.md) for implementation details on transitions, blend logic, and interrupt priorities.

---

## 6. Tileset Creation

### Tile Sizes

| Size | Use Case | Notes |
|---|---|---|
| **8×8** | Retro/NES-style, detailed maps | Very small — requires discipline |
| **16×16** | Most common for pixel art games | Good balance of detail and efficiency |
| **32×32** | Higher-detail pixel art, hand-drawn | More pixels to fill but more expressive |
| **48×48** | Hand-drawn or detailed pixel | Less common, harder to tile |

**Pick one size and stick with it.** Mixing tile sizes in a single tilemap layer is possible but adds complexity. If you must mix, use multiples (16×16 base with 32×32 or 48×48 decorations).

### Auto-Tiling Systems

Auto-tiling automatically selects the right tile variant based on neighboring tiles. There are two main systems:

**16-Tile (Wang / Minimal):**
- Uses 4-bit bitmask (check up, down, left, right)
- 16 possible combinations = 16 tiles
- Simple to implement, covers most cases
- Looks blocky at corners — no inner corner transitions

```
Tile indices based on UDLR neighbors:
0000 = isolated    0001 = right only
0010 = left only   0011 = left+right (horizontal corridor)
... etc.
```

**47-Tile (Blob / Full):**
- Uses 8-bit bitmask (check all 8 neighbors including diagonals)
- 256 raw combinations collapse to 47 unique visual cases
- Handles inner corners beautifully
- More art to create, but the results are dramatically better

**Practical recommendation:** Start with 16-tile for prototyping, upgrade to 47-tile for final art on your most-used terrain types. Secondary terrain can stay at 16-tile.

### Seamless Tiling Techniques

1. **Use Aseprite's Tile Mode** — View → Tiled Mode → Both Axes. Paint while seeing how edges meet.
2. **Start from the center** — Draw your tile's main content in the center, then work outward to the edges.
3. **Edge matching** — The top edge of a tile must match the bottom edge of the tile above it. Same for left/right. Check all four edges.
4. **Variation tiles** — Create 2–4 variants of common tiles (grass, dirt, stone) and place randomly to break visual repetition.
5. **Transition tiles** — Where biomes meet (grass to dirt), create dedicated transition tiles. Don't rely on just placing them adjacent.

### Tiled Editor Integration

[Tiled](https://www.mapeditor.org/) is the standard tilemap editor. Your workflow:

1. **Export tileset from Aseprite** as a single PNG (grid layout, no spacing unless your engine expects it)
2. **Import into Tiled** — Map → New Tileset → embed or reference the PNG
3. **Paint your map** — Use layers: `ground`, `walls`, `decoration`, `collision` (invisible)
4. **Export as TMX or JSON** — Load in MonoGame with a TMX loader library

See [G37 — Tilemap Systems](../../monogame-arch/guides/G37_tilemap_systems.md) for MonoGame-side tilemap rendering, collision layers, and chunked loading.

**Tiled tips:**
- Use **object layers** for spawn points, triggers, and zones — not tile layers
- Name your layers consistently across all maps
- Use Tiled's **auto-mapping rules** to automate terrain transitions
- Keep collision on a separate layer — toggle visibility to check coverage

---

## 7. Placeholder Art Strategy

Placeholder art is not optional. It's how you test gameplay without wasting weeks on sprites you'll redesign.

### The Placeholder Progression

```
Phase 1: Colored Rectangles
  → Solid color blocks. Red = enemy, blue = player, green = pickup.
  → Size communicates hitbox. Color communicates type. That's it.

Phase 2: Basic Shapes
  → Circles, triangles, arrows showing direction.
  → Add a dot for "front" facing. Distinguishable silhouettes.

Phase 3: Rough Sprites
  → Quick sketches, ~15 min per entity. Not polished.
  → Correct proportions, basic colors, readable animations (2–3 frames).

Phase 4: Final Art
  → Full detail, full animation, final palette. Polish pass.
  → Only for things you're confident won't change.
```

### Why Placeholders Are Essential

- **Gameplay first.** You can't evaluate whether a mechanic feels good if you're spending 4 hours per sprite. Colored rectangles let you iterate on game feel in minutes.
- **Things change.** That enemy you spent 8 hours animating? The design just changed. With a placeholder, that's 5 minutes lost, not a day.
- **Scope clarity.** Placeholders show you how many assets you actually need. The asset list is always longer than you think.
- **Parallel work.** Code with placeholders, art when mechanics are locked.

### Making Good Placeholders

Good placeholders communicate **intent** even though they're rough:

- **Size matters** — Make the placeholder the same size as the final sprite. This affects gameplay (hitboxes, spacing, camera).
- **Color-code consistently** — Player = blue, enemies = red, pickups = yellow, hazards = orange. Document your color code.
- **Show direction** — An arrow, a dot for the eye, asymmetric shape — anything that shows which way the entity faces.
- **Animate minimally** — A 2-frame bounce for idle, a 2-frame shuffle for walk. Enough to see that animation *exists* without investing in it.
- **Label them** — Draw text on the sprite or use a debug overlay: "SLIME", "BOSS", "LEVER". When you have 30 colored rectangles, you'll forget which is which.

### The "Programmer Art" Trap

The danger zone is between placeholder and final art. Here lives **programmer art** — art that took real effort but doesn't look good enough to ship.

**Avoid this trap:**
- Commit to placeholders during prototyping. Don't "just clean it up a little."
- When you're ready for real art, do a proper art pass. Don't polish placeholders — replace them.
- If you're hiring an artist later, placeholders actually help — they communicate exactly what you need without constraining the artist's style.
- If you're doing your own art, batch it. Don't interrupt coding to "quickly draw one sprite" — you'll end up in programmer art limbo.

---

## 8. UI Art

UI is the most-seen art in your game. Players stare at health bars, menus, and inventories constantly. Get it right.

### UI/UX Design Phase — Prototyping with Google Stitch

Before creating any UI art assets, prototype your layouts in [Google Stitch](https://stitch.withgoogle.com/) — a free AI-native design canvas that generates high-fidelity UI mockups from text or voice prompts.

**Stitch in the art pipeline:**

```
Concept → Stitch Prototype → Art Asset Creation → Engine Implementation
          (layout/colors)    (sprites/9-slice)    (Gum/Godot UI)
```

1. **Describe your screen** in Stitch to establish layout, color palette, and typography hierarchy
2. **Use vibe design** to explore aesthetics: "dark souls inventory feel", "cozy farm shop", "retro arcade scoreboard"
3. **Extract the design system** — Colors, spacing, sizes, border styles → feeds directly into your asset creation
4. **Create art assets** to match — Now you know the exact dimensions for 9-slice panels, icon sizes, and font choices
5. **Implement in engine** with both the Stitch layout specs and the art assets you created

This front-loads design decisions before you spend time drawing pixel art or crafting 9-slice panels. Changing a Stitch prompt costs seconds; redrawing UI sprites costs hours.

See [G — Stitch UI Workflow](../game-design/G_stitch_ui_workflow.md) for the complete guide with prompt templates and engine integration details.

### UI Element Sizing

- **Design at native resolution** — UI at your game's base resolution, not the display resolution. A button that's 40×12 pixels at 320×180 base will scale up with the rest of the game.
- **Touch targets** (if targeting touch): minimum 44×44 points (Apple HIG). Even for non-touch, keep clickable areas generous.
- **Consistent margins** — Pick a grid unit (4px, 8px) and align everything to it. 4px margins, 8px padding, 16px between sections.
- **Text clearance** — Leave at least 2px padding between text and container edges.

### 9-Slice / 9-Patch Sprites

9-slice lets you scale UI panels to any size without stretching corners:

```
┌───┬───────┬───┐
│ 1 │   2   │ 3 │   Corners (1,3,7,9): never scaled
├───┼───────┼───┤   Edges (2,4,6,8): stretched in one axis
│ 4 │   5   │ 6 │   Center (5): stretched in both axes
├───┼───────┼───┤
│ 7 │   8   │ 9 │
└───┴───────┴───┘
```

**Design tips:**
- Keep corners small (4–8px) to minimize the unstretched area
- Make the center region a solid color or very simple pattern (it will tile/stretch)
- Edge regions should be simple gradients or repeating patterns
- Export as a single image with consistent corner sizes documented

See [G5 — UI Framework](../../monogame-arch/guides/G5_ui_framework.md) for 9-slice rendering implementation and layout systems.

### Font Selection

For pixel art games:
- Use **bitmap/pixel fonts** at your base resolution. Don't scale TrueType fonts to pixel sizes — they'll look blurry or misaligned.
- Free pixel fonts: m5x7, m3x6, Press Start 2P, Silkscreen, silver

For non-pixel games:
- Use **FontStashSharp** for dynamic font rendering in MonoGame — supports TrueType/OpenType, runtime sizing, and text effects.
- Choose 1–2 fonts max: one for body text, one for headers/titles.
- Prioritize readability over style, especially for body text and dialogue.

### Icon Design at Small Sizes

Icons at 8×8 or 16×16 need extreme clarity:

- **Silhouette first** — The icon should be recognizable as a solid black shape. If the silhouette reads, colors and detail are bonus.
- **One concept per icon** — Sword, potion, key. Not "magic frost sword of the northern realm."
- **Consistent style** — All icons same size, same outline weight, same level of detail. A detailed 16×16 sword next to a simple 16×16 shield looks wrong.
- **Test at 1:1** — Zoom out. If you can't tell what it is at actual size, simplify.

### UI Style Consistency

- Match your game's art style. Pixel art game = pixel art UI. Hand-drawn game = hand-drawn UI elements.
- Pick a UI color palette: typically more muted than game art. Common approach: dark panel backgrounds, light text, one accent color for highlights/selections.
- Avoid pure black (#000000) and pure white (#FFFFFF) — they're harsh. Use near-black (#1a1a2e) and near-white (#eaeaea).

---

## 9. Particle & Effect Art

Particle art is high impact for low effort. A few small sprites, combined by your particle system, produce fire, smoke, sparks, magic, and more.

### Core Particle Sprites

You need surprisingly few source sprites. Start with these:

| Sprite | Size | Description |
|---|---|---|
| `particle_circle_soft.png` | 8×8 or 16×16 | White circle, soft gradient edges (Gaussian falloff). The workhorse. |
| `particle_circle_hard.png` | 8×8 | White circle, hard edge. For sparks, debris. |
| `particle_square.png` | 4×4 or 8×8 | Solid white square. Pixelated debris, confetti. |
| `particle_spark.png` | 8×2 or 16×4 | Elongated white shape. Stretched by velocity for spark trails. |
| `particle_smoke.png` | 16×16 or 32×32 | Irregular soft blob. 2–3 variants for organic look. |
| `particle_ring.png` | 16×16 | White ring/donut shape. For shockwaves, impact rings. |

**Make them white.** Tint via code to any color. One sprite, infinite uses.

### Gradient Textures

For trails and beams, a 1D gradient texture (e.g., 256×1 white-to-transparent) is useful. Map it along a trail's length for smooth falloff.

### Blending Modes

- **Additive blending** — Sprites add light. Perfect for fire, magic, glows, energy. Use white/bright sprites.
- **Alpha blending** — Standard transparency. Use for smoke, dust, debris — things that obscure.
- **Design sprites for their blend mode.** Additive sprites should be bright centers with dark/transparent edges. Alpha-blended sprites need clean alpha channels.

Keep particle art simple. The system does the work — spawning, moving, fading, scaling. Your art just needs to be a good building block.

See [G23 — Particles](../../monogame-arch/guides/G23_particles.md) for particle system implementation, emitter configuration, and pooling.

---

## 10. Background & Parallax Art

Backgrounds set the mood. Parallax layers add depth. Together, they make your 2D world feel alive.

### Layer Design for Parallax

A typical parallax setup has 3–5 layers:

```
Layer 0 (farthest) — Sky / solid color gradient         Speed: 0.0–0.1×
Layer 1            — Distant mountains / clouds          Speed: 0.2–0.3×
Layer 2            — Mid-ground hills / trees            Speed: 0.4–0.6×
Layer 3            — Near background / large props       Speed: 0.7–0.8×
Layer 4 (gameplay) — Tilemap / entities                  Speed: 1.0×
Layer 5 (nearest)  — Foreground decoration / fog         Speed: 1.1–1.3× (overcamera)
```

**Speed** is relative to the camera. Layer 0 barely moves; the gameplay layer moves 1:1; foreground layers can move *faster* than the camera for a strong depth effect.

### Repeating Backgrounds

Each parallax layer typically tiles horizontally:

- **Width:** At least 2× the screen width at that layer's scale. Wider = less obvious repetition.
- **Seamless edges:** The left edge must match the right edge. Use Aseprite's Tile Mode or manually match.
- **Vertical extent:** Usually only needs to be tall enough for the camera's vertical range. For vertical scrolling games, tile vertically too.

### Color and Value Separation

Make layers visually distinct through **atmospheric perspective**:

- **Far layers** — Lower contrast, lower saturation, shifted toward blue/cool. Lighter overall.
- **Mid layers** — Medium contrast and saturation.
- **Near layers** — Full contrast, full saturation, darkest darks.
- **Foreground** — Can be silhouetted (very dark) to frame the action.

This mimics how real atmosphere desaturates and lightens distant objects. It also ensures the gameplay layer is the most visually prominent — players' eyes go there naturally.

### Design Tips

- **Sky layers can be a solid gradient** — No need to draw. Generate a vertical gradient in code or use a 1-pixel-wide texture stretched.
- **Cloud layers** — A few cloud shapes on a transparent background, tiled and scrolling slowly. Additive or alpha-blended.
- **Vary element spacing** — On a mountain layer, don't evenly space peaks. Cluster some, spread others. Even distribution reads as fake.
- **Ground line** — Make sure each layer's ground line (if visible) aligns with the parallax math, or hide it behind the layer in front.

See [G22 — Parallax & Depth Layers](../../monogame-arch/guides/G22_parallax_depth_layers.md) for the rendering and scrolling implementation.

---

## 11. Art Optimization

Art assets are usually the largest part of your game. Optimize without sacrificing quality.

### Texture Sizes

**Power of 2 (POT) vs Arbitrary:**
- **POT textures** (64, 128, 256, 512, 1024, 2048, 4096) — Required by some older GPUs. Always safe. Mipmapping requires POT.
- **Non-POT (NPOT)** — Supported by all modern GPUs and MonoGame. Fine for sprite sheets and UI. Avoids wasted padding.
- **Recommendation:** Use NPOT for sprite sheets (pack tightly), but keep max dimension ≤ 4096×4096. Some mobile GPUs cap at 2048.

### Compression

| Format | Quality | Size | Use Case |
|---|---|---|---|
| **PNG (uncompressed in VRAM)** | Lossless | Full RGBA in memory | Default for 2D. Quality guaranteed. |
| **DXT/BC compression** | Lossy | 4:1 or 8:1 ratio | Large textures, backgrounds. Some artifacts on sharp pixel art. |
| **Indexed color** | Lossless, limited palette | ~4:1 for 16-color | Pixel art with strict palette. Tiny file sizes. |

**For pixel art games:** PNG (Color format in MonoGame) is almost always fine. Your textures are small. Don't compress pixel art with DXT — it smears pixels.

**For hand-drawn / HD games:** Consider DXT compression for large background layers. Test for visible artifacts.

### Memory Budgets

Rough VRAM estimates for textures:

```
Texture Memory = Width × Height × 4 bytes (RGBA)

256×256   = 256 KB
512×512   = 1 MB
1024×1024 = 4 MB
2048×2048 = 16 MB
4096×4096 = 64 MB
```

**Budget guidelines:**
- Aim for total texture memory under 256 MB for a desktop game, under 64 MB for mobile
- A typical 2D indie game uses 20–80 MB of texture memory
- Monitor with `GraphicsDevice.Metrics` in MonoGame

### When to Split vs Combine Atlases

**Combine when:**
- Sprites are used together (same scene, same entity)
- Reduces texture swaps during rendering
- Total atlas fits in a reasonable size (≤2048×2048)

**Split when:**
- Atlas exceeds 2048×2048 (or 4096×4096 absolute max)
- Assets are used in different scenes — don't load dungeon sprites in the forest level
- You need different compression settings for different assets

### General Optimization Tips

- **Don't load what you don't need.** Unload scene-specific atlases when leaving a scene.
- **Trim transparency.** Aseprite's "Trim Cels" on export removes empty space. Your source rectangle handles the offset.
- **Reuse and tint.** One white particle sprite tinted 10 different colors costs less than 10 colored sprites.
- **Mirror programmatically.** Don't draw left-facing and right-facing sprites — flip in code via `SpriteEffects.FlipHorizontally`.

See [G33 — Profiling & Optimization](../../monogame-arch/guides/G33_profiling_optimization.md) for GPU profiling, draw call analysis, and performance budgeting.

---

## 12. Art Production Schedule

The most common mistake: doing final art too early. Art should follow design, not lead it.

### When to Do Art

```
Prototype Phase    → Phase 1 placeholders only (colored rectangles)
                     Focus: Is the game fun? Does the core loop work?

Pre-Production     → Phase 2 placeholders (basic shapes, rough sketches)
                     Focus: Lock mechanics. Define art style. Create style guide.

Production         → Phase 3–4 (rough sprites → final art)
                     Focus: Batch similar assets. Work systematically.

Polish             → Phase 4 finalization, juice, effects
                     Focus: Animation polish, particles, screen shake art, UI shine
```

**The golden rule:** Don't draw final art for a mechanic you haven't tested with placeholders. You *will* redesign it.

### Art Phases Per Asset

| Asset | Placeholder | Blockout | First Pass | Polish | Total |
|---|---|---|---|---|---|
| Player character (all anims) | 30 min | 2 hr | 8–12 hr | 4–6 hr | 15–20 hr |
| Enemy (basic, 3–4 anims) | 15 min | 1 hr | 3–5 hr | 2–3 hr | 6–9 hr |
| Tileset (one biome, 47-tile) | 30 min | 2 hr | 6–10 hr | 3–4 hr | 12–16 hr |
| UI panel / HUD | 15 min | 1 hr | 2–4 hr | 1–2 hr | 4–7 hr |
| Background (one scene, 3–4 parallax layers) | 15 min | 1 hr | 4–6 hr | 2–3 hr | 7–10 hr |
| Particle set (fire/smoke/etc.) | 10 min | 30 min | 1–2 hr | 1 hr | 2–4 hr |
| Icon set (20 icons) | 20 min | 1 hr | 3–4 hr | 1–2 hr | 5–7 hr |

*Times assume a programmer-artist with moderate art skills. Experienced artists will be faster. Your first tileset will take twice as long as your third.*

### Batch Similar Work

Batching is the single biggest productivity multiplier for art:

- **Batch by type** — Draw all enemy idle animations in one session. Then all enemy walk animations. You get into a rhythm.
- **Batch by biome/scene** — All forest assets together. You're in the right color palette and style mindset.
- **Batch by phase** — Do ALL blockouts before ANY first-pass. This reveals scope problems early.

**A productive art session:**
1. Set up reference and palette (5 min)
2. Block out all sprites for this batch as rough sketches (30–60 min)
3. First pass: add color, basic shading (2–4 hr)
4. Polish pass: clean edges, add detail, animate (2–4 hr)
5. Export and test in-game (15 min)

### The Art Asset Tracker

Maintain a simple spreadsheet or markdown table:

```markdown
| Asset | Phase | Est. Hours | Actual | Notes |
|---|---|---|---|---|
| player_idle | ✅ Polish | 2h | 2.5h | Added breathing anim |
| player_walk | 🔶 First Pass | 3h | — | Need 8 frames |
| player_attack | ⬜ Placeholder | 4h | — | Wait for attack redesign |
| enemy_slime | 🔶 First Pass | 3h | — | |
| tileset_forest | ⬜ Blockout | 12h | — | 47-tile, prioritize |
```

Update this weekly. It's your sanity check for scope.

---

## Quick Reference: Art Pipeline Checklist

### Starting a New Project
- [ ] Choose art style (pixel / hand-drawn / vector)
- [ ] Set base resolution
- [ ] Create or choose color palette (≤32 colors to start)
- [ ] Build mood board (20–30 reference images)
- [ ] Set up folder structure (`Content/Sprites/`, etc.)
- [ ] Document naming conventions
- [ ] Create style guide (outline, shading, proportions, light direction)

### Per-Asset Workflow
- [ ] Check if placeholder is sufficient for current phase
- [ ] Set up Aseprite file (correct canvas size, layers named)
- [ ] Block out key poses / layout
- [ ] Apply palette and shading
- [ ] Animate (key poses → in-betweens)
- [ ] Tag animations in Aseprite
- [ ] Export (spritesheet + JSON, or save `.aseprite` for MonoGame.Aseprite)
- [ ] Test in-game at actual size and speed
- [ ] Run through style consistency checklist

### Before Shipping
- [ ] All placeholders replaced with final art
- [ ] Texture atlases packed efficiently
- [ ] Unused assets removed from Content
- [ ] Memory usage within budget
- [ ] All animations tested at target framerate
- [ ] Consistent style across all assets (final review)
- [ ] Mirrored sprites using code flip, not duplicate art

---

*Art doesn't have to be perfect. It has to be consistent, readable, and serve the game. A cohesive 16-color game with strong design beats a scattered high-fidelity one every time. Constrain yourself, batch your work, and don't draw final art for mechanics you haven't proven.*
