# 2D Lighting -- Theory & Concepts

This document covers engine-agnostic 2D lighting and shadow theory. For engine-specific implementations, see the relevant engine module.

---

## The Lightmap Approach

The standard technique for 2D lighting: render all lights into a separate texture (the **lightmap**), then multiply it over the final scene.

```
Scene (full brightness)  x  Lightmap (black=dark, white=lit)  =  Final output
```

### Pipeline

1. **Draw the scene** at full brightness (as if fully lit)
2. **Switch** to the lightmap render target. Clear it to the **ambient color** (e.g., dark blue for night, white for full daylight)
3. **Draw each light** as an additive-blended sprite onto the lightmap
4. **Composite:** multiply the lightmap over the scene

**Key insight:** The lightmap is cleared to the ambient color, not black. Unlit areas still receive ambient light. Lights are drawn additively on top of that ambient base.

### Multiply Blending

The compositing step uses multiply blend mode: `final = scene_color * lightmap_color`. Where the lightmap is black, the scene is fully dark. Where the lightmap is white, the scene shows at full brightness.

---

## Light Types

### Point Lights

Radiate outward from a position with a given radius, color, and intensity. Rendered as a radial gradient texture drawn additively onto the lightmap.

**Radial gradient texture:** Generate a soft circle (128x128 or 256x256) where each pixel's brightness decreases from center to edge. One texture serves all point lights -- scale and tint at draw time.

### Falloff Curves

| Falloff | Formula | Visual |
|---------|---------|--------|
| Linear | `1 - d` | Hard edges, unrealistic |
| Quadratic | `(1 - d)^2` | Natural, good default |
| Smoothstep | `d^2 * (3 - 2*d)` inverted | Very soft edges, cinematic |
| Inverse square | `1 / (1 + k*d^2)` | Physically based, never reaches 0 |

Where `d` = distance normalized to [0, 1] by dividing by the light radius.

### Spot Lights

Emit in a cone from a position along a direction. Defined by inner angle (full intensity) and outer angle (falloff to zero). Implemented as a shader that clips a radial gradient to a cone using dot product against the light direction.

### Ambient Light

The baseline illumination for the entire scene. In the lightmap approach, it is simply the clear color of the lightmap render target.

**Day/night cycle:** Interpolate the ambient color across a curve of time-of-day values. Use smoothstep interpolation for natural transitions between periods (dawn, noon, dusk, night).

**Indoor override:** When the player enters a building, override the ambient color independently from the outdoor day/night cycle.

---

## 2D Shadow Casting

Shadow casting adds occlusion -- lights are blocked by walls and obstacles, creating shadow volumes.

### Algorithm Overview

1. **Gather occluder edges** -- walls and solid objects near the light
2. **Cast rays** from the light to each edge vertex
3. **Extend shadow geometry** -- project rays past occluder edges to create shadow polygons
4. **Render the visibility polygon** -- the area visible from the light
5. **Mask the light** with the visibility polygon (only illuminate visible areas)

### Visibility Polygon

The classic algorithm computes which portions of the area are visible from the light source. For each occluder edge, project shadow geometry outward. The remaining area is the lit region.

### Performance Considerations

- Only process occluders within the light's radius
- Pre-compute static occluder edges; only recompute when the level changes
- Shadow casting is per-light -- limit the number of shadow-casting lights (3--5 typically sufficient)
- Most lights in a 2D game do not need shadows -- simple radial gradients look good enough

---

## Normal Map Lighting

For added depth, use normal maps with 2D sprites. The normal map encodes surface direction per pixel, allowing lights to create the illusion of 3D shading.

### How It Works

1. Artist creates a normal map for each sprite (blue-ish texture encoding surface normals)
2. A shader samples the normal map and computes `dot(normal, light_direction)`
3. Brighter where the surface faces the light, darker where it faces away

### When to Use

Normal map lighting adds significant visual quality but requires:
- Normal maps for every animated sprite (doubles art production)
- Per-pixel lighting shader
- Careful art direction to maintain visual consistency

Most 2D games skip normal maps and use the simple lightmap approach with great results.

---

## Light Cookies / Masking

A light cookie is a texture that modulates a light's shape. Instead of a smooth radial gradient, the light projects a pattern (window blinds, foliage shadows, stained glass).

Multiply the cookie texture with the light's gradient during the lightmap draw pass.

---

## Flicker Effects

For torches, fires, and unstable lights, modulate intensity over time:

```
flicker = 0.7 + sin(time * 8) * 0.15 + sin(time * 13) * 0.1
light.intensity = base_intensity * flicker
```

Using multiple sine waves at different frequencies creates organic-looking flicker. Can also add small random position offsets for a more natural look.

---

## Performance Guidelines

- **Lightmap resolution:** Full screen resolution for crisp results, or half resolution for cheaper fill
- **Limit shadow-casting lights:** 3--5 per scene
- **Non-shadow lights are cheap:** Additive-blended sprites have minimal overhead
- **Frustum cull lights:** Skip lights outside the camera bounds
- **Batch by blend mode:** Draw all additive lights in one pass

---

## Render Pipeline Order

```
1. Draw world (terrain, entities)      -> scene render target
2. Clear lightmap to ambient color
3. Draw lights (additive blend)        -> lightmap render target
4. Composite: scene * lightmap         -> screen
5. Draw HUD / UI on top (unaffected by lighting)
```

---

*Implementation examples are available in engine-specific modules.*
