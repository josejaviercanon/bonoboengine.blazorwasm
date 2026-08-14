# G28 — 3/4 Top-Down Perspective


> **Category:** Guide · **Related:** [G2 Rendering & Graphics](./G2_rendering_and_graphics.md) · [G3 Physics & Collision](./G3_physics_and_collision.md) · [G20 Camera Systems](./G20_camera_systems.md) · [G22 Parallax & Depth Layers](./G22_parallax_depth_layers.md)

# Building games in three-quarter perspective: a technical deep dive

**The 3/4 top-down view — used by Stardew Valley, Chrono Trigger, CrossCode, and dozens of landmark RPGs — relies on a surprisingly consistent set of art conventions and engine patterns.** Understanding these patterns before writing code saves months of refactoring. The core insight: art decisions about tile size, sprite proportions, and layering directly dictate your render pipeline, collision system, and entity architecture. This report distills the technical approaches of shipped games into actionable patterns for a C#/MonoGame + Arch ECS stack.

---

## How shipped games solved the 3/4 view problem

The games that define this perspective share remarkable technical DNA despite spanning three decades of hardware. **16×16 pixel tiles are the dominant standard**, inherited from SNES-era hardware tiles and carried forward by nearly every successful modern game in this style. Stardew Valley, CrossCode, Hyper Light Drifter, Chrono Trigger, Zelda: A Link to the Past, and Secret of Mana all use 16×16 tiles. Undertale is a notable outlier at 20×20. Graveyard Keeper uses roughly 32×32 for its "hi-bit" aesthetic.

Native render resolution clusters around **480×270** for modern pixel art games targeting 1080p displays — this gives clean 4× integer scaling. Stardew Valley and Hyper Light Drifter both render at exactly this resolution. CrossCode renders at 568×320 (~3.4× to 1080p). Eastward uses a 360p base. The pattern is clear: pick a resolution that divides evenly into common display resolutions to avoid sub-pixel artifacts.

Character sprites follow a consistent proportional convention: **one tile wide, two tiles tall** (typically 16×32 pixels). This creates the visual height illusion while keeping pathfinding and collision on a single-tile footprint. Chrono Trigger's Crono (~15×36) and Zelda's Link (~24×32) are slightly wider for expressiveness, but their collision still maps to roughly one tile width. The "feet" occupy the bottom tile for collision while the upper body visually overlaps whatever is behind the character — this is the fundamental mechanism that makes the 3/4 depth illusion work.

The engine architectures behind these games reveal two camps. Unity powers Graveyard Keeper, Moonlighter, and Enter the Gungeon. GameMaker drives Undertale, Deltarune, and Hyper Light Drifter. Stardew Valley uses C#/XNA (migrated to MonoGame in 2021), making it the closest analog to your stack. CrossCode rewrote ~90% of Impact.js into a custom HTML5 engine with JSON-driven data and modular features. Eastward built "Gii," a custom engine on the open-source MOAI framework, to achieve its 3D lighting on pixel art. Teams build custom engines when they need specific technical features — CrossCode's Z-height system, Eastward's bump-mapped lighting — that off-the-shelf tools don't provide.

### Three approaches to depth illusion

Every game in this category uses one or more of these strategies to fake 3D depth:

**Y-sort rendering** is universal. Objects lower on screen render on top of objects higher on screen. GameMaker's classic idiom `depth = -y` captures the principle. This single technique, applied consistently, produces convincing depth for most scenes. Stardew Valley's actual render pipeline (confirmed via GPU frame capture) is: clear → ground tiles → distance-sorted objects → weather overlay.

**Multi-layer tile systems** handle static depth. Stardew Valley uses five named layers in Tiled: Back (ground), Buildings (collision/obstacle), Front (always in front of characters), AlwaysFront (above everything), and Paths (spawn markers). The critical split is between tiles that render below entities and tiles that render above them. Tree trunks go on the entity-level layer; tree canopies go on Front.

**Z-height systems** go beyond simple layers. CrossCode implements actual pseudo-3D height values on all graphics, allowing characters to jump onto elevated platforms and walk under roofs. This required building a custom 3D collision system on top of what was originally a 2D side-scroller engine. Eastward takes this further — despite appearing 2D, it reconstructs all pixel art assets in a 3D environment with hand-painted bump maps, enabling SSAO, volumetric fog, and physically-based lighting on sprite art.

---

## The engine patterns that 3/4 art demands

### Y-sorting: sort by feet, not by center

The sort key must be the **bottom edge of the entity's collision box** — not the sprite center, not the entity position. A tall tree's sort position is at its base. A character's sort position is at their feet. As Elias Daler's widely-referenced tutorial explains: sort all visible objects by `boundingBox.top + boundingBox.height` from lowest to highest.

Edge cases require additional handling. Entities at the same Y need a secondary sort key (X position or entity ID) to prevent z-fighting flicker. Flying or jumping entities need a logical Z-height component — sort first by Z-layer, then by Y within each layer. The visual sprite offsets upward for a jump while the shadow and sort-point stay on the ground. Multi-tile objects like archways must be split into separately-sorted parts: column bases sort at ground level, the crossbar sorts at a higher Z-layer that always draws above ground-level entities.

**For static objects, sort once and cache the result.** Only re-sort moving entities each frame, then merge with the static list. This eliminates most per-frame sorting cost in scenes with many trees, rocks, and buildings.

### Collision shapes must represent the ground plane

This is the single most important principle for 3/4 view collision: **collision boxes represent ground footprint, not visual sprite area.** A 16×32 character sprite gets a collision box of roughly 12×8 pixels at the feet. If you use the full sprite for collision, the player can't walk close to objects and everything feels wrong.

Combat games like CrossCode need further separation: a small movement collision box at feet (prevents walking through walls), a larger hurtbox covering the character body (receives damage), attack hitboxes activated only during attack frames matching the weapon animation, and an interaction trigger zone slightly larger than the visual base for dialogue and item pickup. Rendering an elliptical shadow at the entity's ground position serves double duty — it communicates to the player where collision happens and anchors the sprite visually to the ground plane.

### Wall and building rendering through split sprites

The standard technique for objects that entities can walk both in front of and behind is the **split sprite**. A building's bottom half (walls, door) renders at or below the entity layer. The top half (roof, upper wall) renders on an overlay layer above all Y-sorted entities. The render order becomes: ground tiles → ground decorations → wall/object bases → Y-sorted entities → wall/object tops → weather/lighting → UI.

SNES games achieved this with per-tile priority bits — individual tiles on the same background layer could be flagged to render above or below sprites. Modern engines recreate this with explicit overlay layers. In Tiled, this means a "BelowEntities" layer for fence bases and wall bottoms and an "AboveEntities" layer for tree canopies and rooftops. Stardew Valley's map structure directly reflects this: the Front and AlwaysFront layers exist specifically for overhead elements that should occlude the player.

For building entry, games use either full scene transitions (fade to black, load interior map) or roof removal (fade out the building's overlay sprite when the player crosses the door threshold, revealing the interior beneath). Pokémon uses the latter approach for many buildings.

### Camera systems and pixel-perfect rendering

The standard 2D camera uses a transformation matrix passed to SpriteBatch. Smooth following uses either linear interpolation (`lerp`) or critically-damped spring behavior (`SmoothDamp`). SmoothDamp is generally preferred — it avoids the "never quite reaches target" problem of naïve lerp. A dead zone (rectangular region where the player can move without camera movement) prevents constant camera drift during small movements.

**Bounds clamping must happen after smoothing** to prevent jitter at world edges. Camera leading — offsetting slightly in the player's movement direction — shows more of where the player is heading. Hyper Light Drifter's developers describe this as their primary camera behavior.

For pixel art, the camera position must **snap to whole pixels** before rendering to prevent sub-pixel artifacts (shimmering, jitter). Render the entire game scene to a RenderTarget2D at native resolution (e.g., 480×270), then scale up to the display resolution using `SamplerState.PointClamp`.

---

## 2.5D techniques that elevate flat sprites

### Normal maps are the highest-ROI visual upgrade

Normal maps encode surface direction per pixel using RGB channels. When paired with dynamic light sources, a shader calculates per-pixel lighting using the normal data instead of geometric normals, producing convincing 3D volume on flat sprites. **Graveyard Keeper and Eastward both hand-paint normal/bump maps** for their sprites, enabling dynamic day/night lighting, point lights from torches, and directional sunlight that responds to time of day — all without redrawing any art.

The rendering pipeline uses a two-pass deferred approach: draw all sprite diffuse textures to one render target, draw all normal maps to a second render target, then a final lighting shader combines both and calculates per-pixel illumination for each light source. Budget **8–16 light sources per shader pass**. Tools for generating normal maps from 2D art include Laigter (free, open-source, supports sprite sheets with tile splitting), SpriteIlluminator (commercial, auto-inflates surface from transparency), and Sprite Lamp (combines multiple hand-shaded versions lit from different directions).

Graveyard Keeper's lighting system adds several clever touches: a shader that checks whether a light source is in front of or behind a sprite on the vertical axis and fades intensity accordingly, shadow sprites that rotate via vertex shader based on light position, and wind animation that deforms x-coordinates based on y-coordinate (top of foliage moves most, root stays fixed).

### LUT color grading transforms mood cheaply

A Look-Up Table (LUT) texture remaps every pixel color based on time of day or location. **The entire scene's atmosphere shifts without modifying any art.** Graveyard Keeper and Eastward both use this technique. Implementation is a single texture lookup in a post-processing shader — negligible performance cost for dramatic visual impact. Interpolating between daytime and nighttime LUT textures creates smooth atmospheric transitions throughout a day/night cycle.

### The HD-2D approach and 3D-to-2D pipelines

Octopath Traveler's "HD-2D" places **2D pixel art sprites as flat billboards in fully 3D Unreal Engine environments** with depth-of-field, volumetric lighting, and particle effects. Point lights fire simultaneously with visual effects to cast character shadows on 3D surfaces. The team of just 6 programmers relied heavily on UE4's built-in material editor, allowing artists to iterate on visual style without programmer involvement.

Dead Cells demonstrates the modern pre-rendered 3D pipeline: a single artist modeled and animated all characters in 3ds Max, rendered them through a homebrew "pixelation" tool that converts 3D renders into pixel art sprites alongside normal maps. **Asset reuse was the key advantage** — old 3D rigs could be repurposed for new monsters, saving hundreds of hours versus hand-drawing frame-by-frame. The open-source project Flare shows the full Blender-based version of this pipeline: Python scripts render 32-frame animations in 8 directions, then ImageMagick assembles 256 images into one sprite sheet.

### Post-processing stack for pixel art

A combined stack of bloom, chromatic aberration, vignette, and LUT grading is computationally cheap and dramatically elevates pixel art. Hyper Light Drifter achieves its signature glow through soft light overlays — gaussian blur applied on separate layers with screen/multiply blend modes — plus shaders for full-screen animated effects. All of these are simple fragment shaders operating on screen-space pixels at negligible performance cost.

Water reflections require a second camera or render pass that captures flipped sprites onto a render texture, then distorts with scrolling noise in the water shader. Pixelated UV sampling (`floor(UV * pixelAmount) / pixelAmount`) maintains style consistency with surrounding pixel art.

---

## How art constraints shape the engine architecture

The relationship between art decisions and engine design is bidirectional and deep. **Tile size dictates collision grid resolution, movement granularity, spatial hash bucket size, and map editor workflow.** A 16×16 tile grid means your spatial hash cells are 16×16, your A* pathfinding grid is 16×16, and your Tiled editor snaps to 16×16. Sub-tile movement (per-pixel) requires the entity system to track floating-point positions while the collision system rounds to tile boundaries for broad-phase checks.

Sprite layering requirements drive the render pipeline's architecture. The need for Y-sorted entities between ground tiles and overlay tiles means **at minimum three SpriteBatch.Begin/End blocks per frame**, each with potentially different sort modes and shader configurations. If trees need a wind shader but characters don't, that's another batch split. Every visual effect that touches only one layer requires its own render pass.

Animation frame counts and sprite sheet layouts affect memory and loading strategy. Moonlighter's developers noted the cost explosion: "if you want to add more weapons or armors, you need to create a crazy amount of sprites, in 4 views." Packing all entity sprites into one large texture atlas minimizes texture switches during the sorted entity draw pass. Maximum safe atlas size is **4096×4096** across platforms; 2048×2048 for broad compatibility.

The 3/4 perspective's depth illusion requires every entity to carry a sort offset (distance from position to foot collision bottom), a render layer assignment, and potentially split-sprite references for tall objects. This directly shapes the ECS component design — you need `Position`, `SortOffset`, `RenderLayer`, and `SplitSprite` components at minimum, and the render system must collect, sort, and draw rather than iterate in arbitrary archetype order.

Wall and building rendering (showing fronts and tops simultaneously) drives tile metadata complexity. Each tile definition needs walkability flags, optional custom collision shapes, elevation values for cliff systems, animation data for water/fire tiles, and layer assignment. In Tiled, this translates to Object layers for collision shapes, custom properties on tile definitions, and multiple tile layers with clear naming conventions.

---

## Implementation patterns for MonoGame + Arch ECS

### Render pipeline architecture

The render pipeline for a 3/4 view game in MonoGame requires multiple SpriteBatch passes with different configurations. Each layer gets its own Begin/End block:

The ground tile pass uses `SpriteSortMode.Deferred` (draw order matches iteration order) with `SamplerState.PointClamp` and the camera transform matrix. Only tiles within the camera viewport are drawn — calculate the visible tile range from camera bounds and iterate only that subset. For large maps, divide into **16×16 tile chunks** with pre-built vertex buffers; only render chunks intersecting the viewport.

The Y-sorted entity pass is the most complex. Arch ECS queries iterate in archetype/chunk order with no guaranteed ordering, so you must **collect entities into a buffer, sort by foot Y position, then draw in `SpriteSortMode.Deferred`**. The query filters for entities with `Position`, `Sprite`, `RenderLayer`, and `SortOffset` components. The sort key is `position.Y + sortOffset.YOffset`. This buffer-sort-draw pattern runs every frame for moving entities; static objects can be pre-sorted and cached.

The overlay pass renders tree canopies, rooftops, and bridge tops — always above all entities, no sorting needed. Weather, lighting, and screen effects follow. Finally, the UI pass renders without the camera transform.

For pixel-perfect rendering, all world-space drawing targets a `RenderTarget2D` at native resolution (e.g., 480×270). After all world passes complete, reset to the backbuffer and draw the render target scaled to window size with `SamplerState.PointClamp`. UI can render directly to the backbuffer at full resolution for crisp text.

### Component design for the 3/4 view

The core components for Arch ECS map directly to the perspective's requirements. `Position` holds world coordinates as floats. `Sprite` references a texture atlas region, origin point, and tint. `SortOffset` stores the Y offset from Position to the entity's foot collision bottom — this is what makes a tall tree sort correctly at its base. `RenderLayer` is an integer or enum (ground=0, entities=1, overlay=2). `Collider` stores offset and dimensions relative to Position, representing the small ground-plane footprint. `AnimationState` tracks current animation name, frame, timer, and playback speed.

Arch's archetype storage means entities with identical component sets share memory chunks — **16KB chunks fitting L1 cache** for maximum iteration speed. Use `record struct` for hot-path components (Position, Velocity, Sprite) to keep them on the stack. Arch queries with `world.Query(in queryDescription, (ref Position pos, ref Velocity vel) => { })` inline beautifully for update systems but cannot be used directly for sorted rendering.

**Structural changes during queries require `CommandBuffer`** — you cannot add or remove components while iterating. Record creates, destroys, and component additions in the buffer, then play back after the query completes. This is essential for spawning projectiles during combat system iteration or destroying entities on death.

### Tiled map integration

DotTiled is the recommended TMX parser for new projects (actively maintained, supports TMX and JSON formats). TiledCS is simpler with no dependencies and has MonoGame examples. MonoGame.Extended includes a built-in Tiled map loader that works with the content pipeline.

The recommended Tiled layer structure for 3/4 view games uses six layers: Ground (base terrain), GroundDecor (flowers, puddles, paths), BelowEntities (bottom halves of walls, fence bases), an Object layer for collision shapes (invisible, not rendered), AboveEntities (tree canopies, rooftops), and a second Object layer for entity spawn points, triggers, and NPC positions.

### Aseprite integration

MonoGame.Aseprite (NuGet package, v6.3.1) loads `.aseprite` files directly without the MGCB content pipeline. It produces `SpriteSheet`, `AnimatedSprite`, `Tilemap`, and `TextureAtlas` objects. Aseprite tags map to animation names, slices provide per-frame collision rectangles. This gives you a complete art-to-engine pipeline: create sprite art in Aseprite → tag animations → export → load directly in MonoGame → create animated sprites from tags.

For the ECS animation system, extract frame rectangles and timing data from the Aseprite file at load time and store them in a `SpriteAnimation` component. The `AnimationSystem` queries all entities with `AnimationState` and `Sprite`, advances frame timers, and updates the `Sprite.SourceRect` when frames change.

---

## Developer resources and further reading

Several developer resources stand out for their technical depth on 3/4 view pipelines. The **CrossCode developer blog** (radicalfishgames.com) documents their modular architecture, JSON-driven data pipeline, and the decision to rewrite Impact.js. Radical Fish's lead developer Lachsen details how they separated assets from code — critical for an RPG with hundreds of maps — and built a feature-based modular system where camera, combat, and GUI are reusable extensions.

**Eastward's creators** shared their art pipeline in a Game Developer interview: pixel art in Aseprite → divided into layers (rooftop, wall) → rebuilt in 3D → hand-painted with bump maps → imported into their Gii engine. This is perhaps the most sophisticated pixel art pipeline of any indie game, turning flat sprites into dynamically-lit assets.

The **Unreal Engine spotlight on Octopath Traveler** details how a team of just 6 programmers achieved HD-2D by placing pixel sprite billboards in 3D UE4 environments with point lights synchronized to visual effects. The **Hyper Light Drifter GDC 2017 talk** ("Secrets of Kickstarter, Design, & Pizza") reveals their custom in-game level editor built inside GameMaker — overcoming tiling issues and enabling artists to place assets directly in game context.

**Graveyard Keeper's porting postmortem** on Game Developer covers their Unity asset optimization for consoles, including a "fake object" system that stores lightweight placeholder data during loading and replaces with real game objects afterward — essential for managing large tile-based worlds on limited hardware.

For SNES-era technical depth, **The Cutting Room Floor** (tcrf.net) has exhaustive technical analyses of Zelda: A Link to the Past and Chrono Trigger, including the tile color compression techniques, per-tile priority bit usage, and Mode 1 graphics layer architecture that established the conventions modern games still follow.

---

## Conclusion

The 3/4 top-down perspective imposes a specific set of constraints that have been solved the same way across three decades of games: **16×16 tiles, 480×270 native resolution scaled 4×, characters at 16×32 with foot-area collision, Y-sorting by collision box bottom, and a minimum three-layer render pipeline** (ground, Y-sorted entities, overlay). These aren't arbitrary conventions — they emerge from the geometry of the perspective itself.

For a MonoGame + Arch ECS implementation, the critical architectural decisions are: render to a RenderTarget2D at native resolution for pixel-perfect scaling; use multiple SpriteBatch passes (one per layer) rather than trying to sort everything in one pass; collect Y-sorted entities into a buffer before sorting since Arch queries don't guarantee order; keep collision boxes small and at entity feet, completely separate from visual sprite bounds; and integrate Tiled for maps and Aseprite for sprites via DotTiled and MonoGame.Aseprite respectively.

The highest-value visual upgrade beyond basic sprite rendering is **normal maps with dynamic lighting** — Graveyard Keeper and Eastward prove this transforms static pixel art into atmospheric, living scenes. Combined with LUT color grading for day/night cycles and a minimal post-processing stack (bloom, vignette), these techniques deliver disproportionate visual impact for modest implementation effort. Start with the fundamentals — correct Y-sorting, proper collision shapes, clean layer separation — then layer in 2.5D techniques once the foundation is solid.