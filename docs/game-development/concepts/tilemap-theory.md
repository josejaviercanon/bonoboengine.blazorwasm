# Tilemap Systems -- Theory & Concepts

This document covers engine-agnostic tilemap theory for 2D games. For engine-specific implementations, see the relevant engine module.

---

## Core Concepts

A tilemap divides the game world into a regular grid of cells, each referencing a tile from a tileset. Tilemaps define world geometry, collision surfaces, visual layers, and spawn logic.

### Data Structure

- **Tile grid** -- 2D array of tile IDs (integers). 0 typically means empty
- **Tileset** -- a texture containing all tile images in a grid layout, plus metadata (collision, animation, properties)
- **Tile ID to source rectangle** -- given a tile ID and tileset columns, compute the source rectangle in the tileset texture:

```
local_id = tile_id - first_gid
src_x = (local_id % columns) * tile_width
src_y = (local_id / columns) * tile_height
```

### Map Formats

- **TMX (Tiled)** -- XML-based, the most common 2D map editor format
- **TMJ** -- JSON variant of TMX
- **Custom binary** -- for performance-critical loading or proprietary tools

---

## Tilemap Rendering

### Viewport Culling

Only render tiles visible within the camera rectangle. Calculate the visible tile range:

```
start_col = max(0, camera.x / tile_width - 1)
start_row = max(0, camera.y / tile_height - 1)
end_col = min(map_cols - 1, (camera.x + view_width) / tile_width + 1)
end_row = min(map_rows - 1, (camera.y + view_height) / tile_height + 1)
```

Add a 1-tile margin to avoid pop-in at edges. This reduces draw calls from thousands to only what fits on screen.

### Tile Flipping

Tile IDs often encode flip flags in their high bits. Extract and apply horizontal/vertical flipping during rendering.

---

## Multiple Tile Layers

Most games use several layers drawn in order:

| Layer | Purpose | Typical Name |
|-------|---------|-------------|
| Background | Sky, distant terrain | "bg" |
| Ground | Main walkable surface | "ground" |
| Decoration (below entities) | Flowers, puddles | "decor_below" |
| *Entity rendering happens here* | |
| Decoration (above entities) | Tree canopy, overhangs | "decor_above" |
| Foreground | Rain, fog overlays | "fg" |

Draw layers from back to front. Insert entity rendering between the appropriate layers.

---

## Autotiling

Autotiling automatically selects the correct tile sprite based on neighboring tiles.

### Bitmask Approach

| Scheme | Bits | Neighbors Checked | Tile Variants |
|--------|------|-------------------|---------------|
| **4-bit** | 4 | Cardinal (N, E, S, W) | 16 |
| **8-bit** | 8 | Cardinal + Diagonal | 47 unique (256 raw, collapsed via lookup table) |

### 4-Bit Computation

```
mask = 0
if north_is_same_type: mask |= 1    // bit 0
if east_is_same_type:  mask |= 2    // bit 1
if south_is_same_type: mask |= 4    // bit 2
if west_is_same_type:  mask |= 8    // bit 3
tile_index = bitmask_to_tile_lookup[mask]
```

### 8-Bit with Corner Collapse

The full 8-bit scheme checks all 8 neighbors (256 combinations) but collapses to 47 unique visual configurations. Corner bits only matter when both adjacent cardinal neighbors are present.

### When to Recompute

Recompute bitmasks when tiles change (placement, destruction). Only recompute the changed tile and its neighbors.

---

## Tile Collision

### Collision Layer

A separate layer (or tile property) marks tiles as solid. During collision resolution, only check tiles near the entity:

```
// Get tile range overlapping entity bounds
min_tx = max(0, entity_left / tile_size)
max_tx = min(map_width - 1, entity_right / tile_size)
min_ty = max(0, entity_top / tile_size)
max_ty = min(map_height - 1, entity_bottom / tile_size)

for ty in range(min_ty, max_ty + 1):
    for tx in range(min_tx, max_tx + 1):
        if is_solid(tx, ty):
            resolve_collision(entity, tile_rect(tx, ty))
```

### Slope Tiles

See [physics-theory.md](./physics-theory.md) for slope collision details.

---

## Chunk-Based Loading

For large or infinite worlds, divide the map into chunks (e.g., 32x32 tiles each):

- **Load chunks** around the camera position
- **Unload chunks** that move far enough away
- **Stream** chunks from disk or generate procedurally

### Load Radius

Keep chunks loaded within a radius around the camera (typically 1--2 chunks beyond the visible area). Use a ring buffer or dictionary of loaded chunks indexed by chunk coordinates.

### Chunk Coordinates

```
chunk_x = floor(world_x / (chunk_size * tile_size))
chunk_y = floor(world_y / (chunk_size * tile_size))
```

---

## Animated Tiles

Tiles that cycle through frames (water, lava, torches):

- Store a list of frame IDs and durations per animated tile type
- Advance a global timer; all tiles of the same type share the same animation phase
- During rendering, resolve the current frame from the global timer

---

## Tile Properties and Metadata

Tiles can carry custom properties beyond collision:

- **Terrain type** -- grass, stone, water (for footstep sounds, movement speed)
- **Damage** -- lava, spikes
- **Trigger** -- spawn points, zone transitions
- **Light blocking** -- for shadow/lighting systems
- **Pathfinding cost** -- for weighted A*

---

## Isometric and Hexagonal Tilemaps

### Isometric

Screen coordinates from tile coordinates:

```
screen_x = (tile_x - tile_y) * (tile_width / 2)
screen_y = (tile_x + tile_y) * (tile_height / 2)
```

Draw order: sort by row then column (painter's algorithm) to handle overlapping correctly.

### Hexagonal

Two common orientations: flat-top and pointy-top. Use axial or cube coordinates for hex math. Neighbor offsets differ for even/odd rows or columns.

---

## Performance Checklist

- Viewport cull all tile rendering (do not draw tiles outside the camera)
- Use texture atlases (one tileset texture per draw batch)
- Minimize layer count (each layer is a full-screen draw pass)
- For static maps, consider pre-rendering chunks to cached textures
- Profile tile collision -- it should be negligible with proper range clamping

---

*For MonoGame implementation, see [G37 — Tilemap Systems](../../monogame-arch/guides/G37_tilemap_systems.md). Implementation examples for other engines are available in their respective modules.*
