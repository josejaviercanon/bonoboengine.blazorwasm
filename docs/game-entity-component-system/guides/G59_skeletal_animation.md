# G59 — 2D Skeletal Animation



> **Category:** Guide · **Related:** [Animation Theory](../../core/concepts/animation-theory.md) · [G31 Animation & Sprite State Machines](./G31_animation_state_machines.md) · [G2 Rendering & Graphics](./G2_rendering_and_graphics.md) · [G8 Content Pipeline](./G8_content_pipeline.md) · [G41 Tweening & Easing](./G41_tweening.md) · [G64 Combat & Damage Systems](./G64_combat_damage_systems.md) · [R1 Library Stack](../reference/R1_library_stack.md)

---

## 1 — Skeletal vs Sprite Animation

Two fundamentally different approaches to 2D character animation, each with clear strengths.

| Aspect | Skeletal | Sprite Sheet |
|---|---|---|
| **Storage** | Bones + mesh + few textures | Many pre-drawn frames |
| **Memory** | Low — one atlas, many animations | High — frame count × resolution |
| **Blending** | Runtime crossfade, additive layers | Hard cuts or manual tweening |
| **Smoothness** | Interpolated, any framerate | Locked to drawn frame count |
| **Art style** | Illustrated, vector-ish, "puppet" | Pixel art, hand-drawn, traditional |
| **Pipeline** | Spine/DragonBones editor → export | Draw every frame in Aseprite/Photoshop |
| **Runtime cost** | Mesh transforms per skeleton | Simple quad draws |
| **Flexibility** | Swap skins, IK, procedural aiming | What you drew is what you get |

**Games using skeletal:** Hollow Knight (partial), Darkest Dungeon, Hades, Dead Cells (hybrid), Clash Royale, most mobile RPGs.

**Games using sprite sheets:** Celeste, Shovel Knight, Cuphead (hand-drawn frames), Streets of Rage 4, older pixel art titles.

**Rule of thumb:** If you need dozens of animations per character, runtime blending, or equipment swapping — skeletal. If you want pixel-perfect art with a limited animation set — sprite sheets.

---

## 2 — How 2D Skeletal Animation Works

### Bone Hierarchy

A skeleton is a tree of **bones**. Each bone has a parent (except the root), a local position, rotation, and scale. Transforming a parent cascades to all children — rotate the upper arm, the forearm and hand follow.

```
root
├── hip
│   ├── left-leg
│   │   └── left-foot
│   └── right-leg
│       └── right-foot
└── torso
    ├── left-arm
    │   └── left-hand
    ├── right-arm
    │   └── right-hand
    └── head
```

### Skinning (Mesh Deformation)

Each image region (arm, torso, head) is a **mesh** — a set of vertices forming triangles. Each vertex is **weighted** to one or more bones. When bones move, vertex positions are calculated as a weighted blend of the bone transforms they're bound to.

```
worldPosition = Σ (weight_i × boneTransform_i × bindPoseInverse_i × localPosition)
```

### Forward Kinematics (FK)

Standard approach: animate each bone's rotation directly. Parent rotation propagates down the chain. Artist keyframes every bone.

### Inverse Kinematics (IK)

Specify where the **end effector** (hand, foot) should be, and the system solves bone rotations backward up the chain. Essential for:
- Feet planting on uneven ground
- Hands reaching toward targets
- Aiming weapons at a cursor

### Keyframe Interpolation

Animations are stored as **keyframes** — snapshots of bone transforms at specific times. Between keyframes, values are interpolated:

- **Linear** — straight lerp, mechanical feel
- **Stepped** — no interpolation, snaps to next keyframe
- **Bezier** — cubic curves with control points for easing in/out, natural motion

Spine uses bezier curves extensively. Each bone property (rotation, translation, scale) can have independent curves.

---

## 3 — Spine Runtime for MonoGame

[Spine](http://esotericsoftware.com/) is the industry standard for 2D skeletal animation. The runtime is split into:

- **spine-csharp** — pure C# core (platform-independent)
- **spine-monogame** — MonoGame-specific rendering

### NuGet / Setup

Add the spine-csharp and spine-monogame packages to your project, or include the source directly from the [spine-runtimes](https://github.com/EsotericSoftware/spine-runtimes) repo. The runtime version **must match** your Spine editor version.

### Loading Skeleton Data

```csharp
using Spine;
using Spine.MonoGame;

public static class SpineAssets
{
    private static readonly Dictionary<string, SkeletonData> _cache = new();

    /// <summary>
    /// Load a Spine skeleton from binary .skel + .atlas files.
    /// Caches by name so multiple entities share the same data.
    /// </summary>
    public static SkeletonData Load(
        GraphicsDevice device, string name, string basePath)
    {
        if (_cache.TryGetValue(name, out var cached))
            return cached;

        // Load atlas (texture pages)
        var atlasPath = Path.Combine(basePath, $"{name}.atlas");
        var atlas = new Atlas(atlasPath, new MonoGameTextureLoader(device));

        // Load skeleton binary
        var skelPath = Path.Combine(basePath, $"{name}.skel");
        var loader = new SkeletonBinary(atlas) { Scale = 1f };
        var skeletonData = loader.ReadSkeletonData(skelPath);

        _cache[name] = skeletonData;
        return skeletonData;
    }

    /// <summary>
    /// Alternative: load from JSON format (larger files, useful for debugging).
    /// </summary>
    public static SkeletonData LoadJson(
        GraphicsDevice device, string name, string basePath)
    {
        if (_cache.TryGetValue(name, out var cached))
            return cached;

        var atlasPath = Path.Combine(basePath, $"{name}.atlas");
        var atlas = new Atlas(atlasPath, new MonoGameTextureLoader(device));

        var jsonPath = Path.Combine(basePath, $"{name}.json");
        var loader = new SkeletonJson(atlas) { Scale = 1f };
        var skeletonData = loader.ReadSkeletonData(jsonPath);

        _cache[name] = skeletonData;
        return skeletonData;
    }
}
```

### Creating a Skeleton Instance

```csharp
public static (Skeleton skeleton, AnimationState animState) CreateInstance(
    SkeletonData data, float defaultMix = 0.2f)
{
    var skeleton = new Skeleton(data);
    skeleton.SetToSetupPose();

    var stateData = new AnimationStateData(data)
    {
        DefaultMix = defaultMix  // crossfade duration in seconds
    };

    var animationState = new AnimationState(stateData);
    return (skeleton, animationState);
}
```

### Rendering with SkeletonRenderer

```csharp
public class SpineRenderHelper
{
    private readonly SkeletonRenderer _renderer;

    public SpineRenderHelper(GraphicsDevice device)
    {
        _renderer = new SkeletonRenderer(device);
        _renderer.PremultipliedAlpha = true;
    }

    public void Draw(
        Skeleton skeleton, AnimationState state,
        float deltaTime, SpriteBatch batch)
    {
        // Update animation state (advances time, applies to skeleton)
        state.Update(deltaTime);
        state.Apply(skeleton);

        // Recalculate world transforms after animation is applied
        skeleton.UpdateWorldTransform(Skeleton.Physics.Update);

        // Render
        _renderer.Begin();
        _renderer.Draw(skeleton);
        _renderer.End();
    }
}
```

### Animation State Management

```csharp
// Play an animation on track 0 (main body), looping
animState.SetAnimation(0, "walk", loop: true);

// Queue an animation after current one finishes
animState.AddAnimation(0, "idle", loop: true, delay: 0f);

// Immediate switch with crossfade (uses DefaultMix or specific mix)
animState.SetAnimation(0, "run", loop: true);

// Set specific crossfade duration between two animations
animState.Data.SetMix("walk", "run", 0.3f);
animState.Data.SetMix("run", "walk", 0.25f);

// Play a one-shot on top (e.g., attack on upper body track)
animState.SetAnimation(1, "attack", loop: false);

// Check current animation
var current = animState.GetCurrent(0);
if (current != null)
{
    string animName = current.Animation.Name;
    float progress = current.AnimationTime / current.Animation.Duration;
    bool isComplete = current.IsComplete;  // played through at least once
}
```

---

## 4 — DragonBones Alternative

[DragonBones](https://dragonbones.github.io/) is an open-source skeletal animation tool. The editor is free (vs Spine's license cost), and the format is well-documented.

### When to Choose DragonBones vs Spine

| Factor | Spine | DragonBones |
|---|---|---|
| **Cost** | $70–$340 license per seat | Free editor |
| **Runtime quality** | Best-in-class, actively maintained | Community-maintained runtimes |
| **MonoGame support** | Official spine-monogame runtime | No official MonoGame runtime — must port or adapt |
| **Features** | Mesh deformation, IK, path constraints, physics | Mesh deformation, IK, basic constraints |
| **Industry adoption** | Extremely widespread | Popular in Chinese/mobile market |
| **Ecosystem** | Massive community, tutorials, examples | Smaller English-language community |

### DragonBones in Practice

DragonBones exports to JSON format. For MonoGame, you'd need to:
1. Parse the DragonBones JSON format (armature, bone, slot, skin definitions)
2. Build your own renderer or adapt an existing C# implementation
3. Handle the animation state machine yourself

**Recommendation:** Unless budget is the primary constraint, Spine's MonoGame runtime saves significant engineering time. If you go DragonBones, budget 2–4 weeks for a custom runtime integration.

---

## 5 — Animation Blending

### Crossfading

When switching animations, Spine interpolates between the outgoing and incoming poses over the **mix duration**. This prevents jarring snaps.

```csharp
// Global default crossfade
stateData.DefaultMix = 0.2f;

// Per-pair overrides for specific transitions
stateData.SetMix("idle", "walk", 0.15f);
stateData.SetMix("walk", "idle", 0.25f);   // slower ease back to idle
stateData.SetMix("walk", "jump", 0.1f);    // fast transition into jump
stateData.SetMix("attack", "idle", 0.3f);  // smooth recovery
```

### Track-Based Layering

Spine supports multiple **tracks** (layers). Higher tracks override lower tracks for the bones they affect. This enables:

```csharp
// Track 0: full-body locomotion
animState.SetAnimation(0, "walk", loop: true);

// Track 1: upper-body attack overlay
// Only affects torso + arm bones (set in Spine editor via bone weighting)
var entry = animState.SetAnimation(1, "slash", loop: false);
entry.MixBlend = MixBlend.Add;     // additive blending
entry.Alpha = 1.0f;                // full strength

// Track 2: head look (subtle additive layer)
var look = animState.SetAnimation(2, "look-up", loop: true);
look.Alpha = 0.5f;                 // half strength — subtle tilt
```

### Blend Weights Over Time

Smoothly ramp blend weights for gradual transitions:

```csharp
public static class AnimationBlendUtil
{
    /// <summary>
    /// Smoothly ramp a track's alpha toward a target value.
    /// Call each frame. Returns true when target is reached.
    /// </summary>
    public static bool RampAlpha(
        AnimationState state, int track, float target, float speed, float dt)
    {
        var entry = state.GetCurrent(track);
        if (entry == null) return true;

        entry.Alpha = MathHelper.Lerp(entry.Alpha, target, speed * dt);
        if (MathF.Abs(entry.Alpha - target) < 0.01f)
        {
            entry.Alpha = target;
            return true;
        }
        return false;
    }
}
```

---

## 6 — Mesh Deformation (FFD)

**Free-Form Deformation** lets you move individual mesh vertices at runtime or via keyframes. This creates effects impossible with rigid bone transforms alone.

### Use Cases

- **Cloth & capes** — flowing fabric that bends and ripples
- **Tails & tentacles** — organic, squishy appendages
- **Facial expressions** — morph mouth shapes, squint eyes, raise brows
- **Squash & stretch** — cartoon impact deformation
- **Breathing** — subtle torso mesh expansion

### How It Works in Spine

1. In Spine editor: attach a **mesh** to a slot instead of a simple region
2. Define vertices and weights (bind to bones)
3. In **FFD keyframes**, offset individual vertices from their weighted positions
4. Runtime interpolates between FFD keyframe states

```csharp
// FFD is applied automatically by AnimationState.Apply()
// No special code needed — just ensure your .skel/.atlas includes mesh data

// You CAN access mesh attachment vertices programmatically:
var slot = skeleton.FindSlot("cape");
if (slot.Attachment is MeshAttachment mesh)
{
    // mesh.Vertices contains the deformed vertex data
    // mesh.WorldVerticesLength gives the count
    float[] worldVerts = new float[mesh.WorldVerticesLength];
    mesh.ComputeWorldVertices(slot, worldVerts);
    // worldVerts now has x,y pairs in world space
}
```

### Performance Note

Mesh deformation is more expensive than rigid region attachments. Each deformed mesh requires per-vertex transform computation. Use meshes where they matter visually; keep simple body parts as regions.

---

## 7 — Runtime Bone Manipulation

### Aiming at a Target

Override bone transforms **after** `AnimationState.Apply()` but **before** `UpdateWorldTransform()`:

```csharp
public static class BoneManipulation
{
    /// <summary>
    /// Rotate a bone to aim at a world-space target position.
    /// Call between Apply() and UpdateWorldTransform().
    /// </summary>
    public static void AimBoneAt(
        Skeleton skeleton, string boneName,
        Vector2 targetWorld, float lerpSpeed, float dt)
    {
        var bone = skeleton.FindBone(boneName);
        if (bone == null) return;

        // Convert target to bone-local space
        float localX, localY;
        bone.Parent?.WorldToLocal(targetWorld.X, targetWorld.Y,
            out localX, out localY);

        if (bone.Parent == null)
        {
            localX = targetWorld.X; localY = targetWorld.Y;
        }

        // Calculate angle from bone to target
        float angle = MathF.Atan2(localY, localX) * (180f / MathF.PI);

        // Smooth rotation
        bone.Rotation = LerpAngle(bone.Rotation, angle, lerpSpeed * dt);
    }

    private static float LerpAngle(float from, float to, float t)
    {
        float diff = ((to - from + 540f) % 360f) - 180f;
        return from + diff * Math.Clamp(t, 0f, 1f);
    }
}
```

### IK Constraints for Aiming

Spine's built-in IK is often cleaner than manual bone rotation:

```csharp
/// <summary>
/// Drive a Spine IK constraint to aim at a world position.
/// The IK constraint must be set up in the Spine editor.
/// </summary>
public static void DriveIKTarget(
    Skeleton skeleton, string ikConstraintName, Vector2 worldTarget)
{
    var ik = skeleton.FindIkConstraint(ikConstraintName);
    if (ik == null) return;

    // The IK target bone's position drives the constraint
    var targetBone = ik.Target;
    // Convert world position to skeleton-local
    float localX = worldTarget.X - skeleton.X;
    float localY = worldTarget.Y - skeleton.Y;
    targetBone.X = localX;
    targetBone.Y = localY;
}
```

### Following Mouse/Touch

```csharp
// In your update loop:
var mouseWorld = Camera.ScreenToWorld(Mouse.GetState().Position.ToVector2());

// Option A: direct bone aim
BoneManipulation.AimBoneAt(skeleton, "gun-arm", mouseWorld, 10f, dt);

// Option B: IK constraint (smoother, respects bone limits)
BoneManipulation.DriveIKTarget(skeleton, "aim-ik", mouseWorld);

// Then update world transforms
skeleton.UpdateWorldTransform(Skeleton.Physics.Update);
```

---

## 8 — Events & Triggers

Spine animations can embed **events** at specific frames — perfect for syncing gameplay with animation timing.

### Common Event Types

- **footstep** — play step sound, spawn dust particle
- **attack-hit** — activate hitbox, deal damage
- **projectile-spawn** — create bullet/arrow at bone position
- **sound** — play specific sound effect
- **effect** — spawn VFX at a bone location

### Listening for Events

```csharp
public static class SpineEventHandler
{
    public static void Register(AnimationState state, Action<string, Skeleton> handler)
    {
        state.Event += (entry, e) =>
        {
            handler(e.Data.Name, entry.AnimationState.Data.SkeletonData
                is not null ? null : null); // skeleton ref needed from outside
        };
    }

    /// <summary>
    /// Full event setup with typed handlers.
    /// </summary>
    public static void SetupEvents(AnimationState state, Skeleton skeleton)
    {
        state.Event += (entry, e) =>
        {
            switch (e.Data.Name)
            {
                case "footstep":
                    var footBone = skeleton.FindBone("foot-" +
                        (e.String ?? "left"));
                    float worldX = footBone?.WorldX ?? skeleton.X;
                    float worldY = footBone?.WorldY ?? skeleton.Y;
                    AudioManager.Play("sfx/step_dirt", volume: e.Float);
                    ParticleManager.Spawn("dust", new Vector2(worldX, worldY));
                    break;

                case "attack-hit":
                    // e.Int could encode damage, e.Float encode knockback
                    CombatSystem.ActivateHitbox(
                        damage: e.Int,
                        knockback: e.Float);
                    break;

                case "projectile":
                    var spawnBone = skeleton.FindBone(e.String ?? "muzzle");
                    if (spawnBone != null)
                    {
                        ProjectileSystem.Spawn(
                            position: new Vector2(
                                spawnBone.WorldX, spawnBone.WorldY),
                            rotation: spawnBone.WorldRotationX);
                    }
                    break;
            }
        };

        // Track completion events
        state.Complete += (entry) =>
        {
            if (entry.Animation.Name == "death")
                EntityManager.MarkForDestroy(/* entity ref */);
        };

        // Track start/end for state tracking
        state.Start += (entry) =>
            Console.WriteLine($"Started: {entry.Animation.Name}");
        state.End += (entry) =>
            Console.WriteLine($"Ended: {entry.Animation.Name}");
    }
}
```

---

## 9 — Skins & Equipment

Spine's skin system lets you swap visual parts at runtime — helmets, weapons, armor — without separate skeletons.

### How Skins Work

A **skin** maps slots to specific attachments. The base skeleton defines all possible slots. Each skin provides attachments for some or all slots. Skins can be **combined** to mix and match.

### Swapping Skins at Runtime

```csharp
public static class SkinManager
{
    /// <summary>
    /// Apply a single named skin.
    /// </summary>
    public static void SetSkin(Skeleton skeleton, string skinName)
    {
        var skin = skeleton.Data.FindSkin(skinName);
        if (skin == null)
        {
            Console.WriteLine($"Skin '{skinName}' not found");
            return;
        }
        skeleton.SetSkin(skin);
        skeleton.SetSlotsToSetupPose();
    }

    /// <summary>
    /// Combine multiple partial skins into one.
    /// Example: base body + iron helmet + leather boots + magic sword
    /// </summary>
    public static void SetCombinedSkin(
        Skeleton skeleton, params string[] skinNames)
    {
        var combined = new Skin("combined");

        foreach (var name in skinNames)
        {
            var skin = skeleton.Data.FindSkin(name);
            if (skin != null)
                combined.AddSkin(skin);
            else
                Console.WriteLine($"Warning: skin '{name}' not found");
        }

        skeleton.SetSkin(combined);
        skeleton.SetSlotsToSetupPose();
    }
}

// Usage — equipping a character:
SkinManager.SetCombinedSkin(skeleton,
    "base-body",           // always present
    "hair/mohawk",         // hair style
    "armor/plate-chest",   // chest armor
    "armor/plate-legs",    // leg armor
    "weapon/fire-sword"    // weapon
);
```

### Equipment System Integration

```csharp
public struct EquipmentComponent
{
    public string BaseSkin;
    public string HairSkin;
    public string ChestSkin;
    public string LegsSkin;
    public string WeaponSkin;
    public bool IsDirty;  // rebuild combined skin when true

    public string[] ToSkinList()
    {
        return new[] { BaseSkin, HairSkin, ChestSkin, LegsSkin, WeaponSkin }
            .Where(s => !string.IsNullOrEmpty(s))
            .ToArray();
    }
}
```

---

## 10 — Performance

### Rendering Cost

Each Spine skeleton submits its own draw calls (one per atlas page per blend mode). A character with one atlas page and standard blending = ~1 draw call.

### Batching

The `SkeletonRenderer` handles batching internally when rendering multiple skeletons that share the same atlas texture. Ensure characters share atlas textures where possible.

### Culling Off-Screen Skeletons

```csharp
public static class SpineCulling
{
    /// <summary>
    /// Skip update + render for skeletons outside the camera view.
    /// Uses the skeleton's bounding box (AABB from setup pose or last frame).
    /// </summary>
    public static bool IsVisible(
        Skeleton skeleton, Rectangle cameraBounds, float padding = 64f)
    {
        // Spine can compute bounds from attachments
        skeleton.GetBounds(
            out float x, out float y,
            out float width, out float height,
            new float[2048]); // scratch buffer

        var skelBounds = new RectangleF(
            x - padding, y - padding,
            width + padding * 2, height + padding * 2);

        return skelBounds.Intersects(cameraBounds);
    }
}
```

### LOD for Distant Skeletons

```csharp
public enum SkeletonLOD { Full, Reduced, Frozen }

public static SkeletonLOD GetLOD(float distanceToCamera)
{
    if (distanceToCamera < 400f) return SkeletonLOD.Full;
    if (distanceToCamera < 800f) return SkeletonLOD.Reduced;
    return SkeletonLOD.Frozen;
}

// Full: update every frame, full mesh deformation
// Reduced: update every 2-3 frames, skip FFD
// Frozen: render last computed pose, no updates
```

### Memory Considerations

- **SkeletonData** is shared — load once, create many instances
- **Atlas textures** are the biggest memory cost. Pack efficiently (power-of-two, tight packing)
- **AnimationState** is per-instance (~small, few KB each)
- Budget ~2–8 MB per character atlas (depending on resolution and frame count)

---

## 11 — ECS Integration

### Components

```csharp
using Arch.Core;

/// <summary>
/// Holds the Spine skeleton instance and its visual state.
/// SkeletonData is shared; Skeleton is per-entity.
/// </summary>
public struct SpineSkeletonComponent
{
    public Skeleton Skeleton;
    public bool FlipX;
    public bool Visible;
}

/// <summary>
/// Manages animation playback for an entity's skeleton.
/// </summary>
public struct SpineAnimationComponent
{
    public AnimationState State;
    public string CurrentAnimation;  // cached for quick queries
    public bool EventsRegistered;
}

/// <summary>
/// Optional: equipment/skin state for the skeleton.
/// </summary>
public struct SpineSkinComponent
{
    public string[] ActiveSkins;
    public bool IsDirty;
}
```

### Spine Update System

```csharp
public class SpineUpdateSystem
{
    private readonly QueryDescription _query = new QueryDescription()
        .All<SpineSkeletonComponent, SpineAnimationComponent, TransformComponent>();

    public void Update(World world, float dt)
    {
        world.Query(in _query, (
            ref SpineSkeletonComponent spine,
            ref SpineAnimationComponent anim,
            ref TransformComponent transform) =>
        {
            if (!spine.Visible) return;

            // Sync skeleton position with ECS transform
            spine.Skeleton.X = transform.Position.X;
            spine.Skeleton.Y = transform.Position.Y;
            spine.Skeleton.ScaleX = spine.FlipX ? -1f : 1f;

            // Advance animation
            anim.State.Update(dt);
            anim.State.Apply(spine.Skeleton);
            spine.Skeleton.UpdateWorldTransform(Skeleton.Physics.Update);

            // Cache current animation name for gameplay queries
            var current = anim.State.GetCurrent(0);
            anim.CurrentAnimation = current?.Animation.Name ?? "";
        });
    }
}
```

### Spine Render System

```csharp
public class SpineRenderSystem
{
    private readonly SkeletonRenderer _renderer;
    private readonly QueryDescription _query = new QueryDescription()
        .All<SpineSkeletonComponent, SpineAnimationComponent>();

    public SpineRenderSystem(GraphicsDevice device)
    {
        _renderer = new SkeletonRenderer(device);
        _renderer.PremultipliedAlpha = true;
    }

    public void Draw(World world, Camera2D camera)
    {
        var cameraBounds = camera.GetVisibleBounds();

        _renderer.Begin();

        world.Query(in _query, (
            ref SpineSkeletonComponent spine,
            ref SpineAnimationComponent anim) =>
        {
            if (!spine.Visible) return;

            // Frustum cull
            var skel = spine.Skeleton;
            if (MathF.Abs(skel.X - camera.Position.X) > cameraBounds.Width)
                return;

            _renderer.Draw(skel);
        });

        _renderer.End();
    }
}
```

### Skin Update System

```csharp
public class SpineSkinSystem
{
    private readonly QueryDescription _query = new QueryDescription()
        .All<SpineSkeletonComponent, SpineSkinComponent>();

    public void Update(World world)
    {
        world.Query(in _query, (
            ref SpineSkeletonComponent spine,
            ref SpineSkinComponent skin) =>
        {
            if (!skin.IsDirty) return;

            SkinManager.SetCombinedSkin(spine.Skeleton, skin.ActiveSkins);
            skin.IsDirty = false;
        });
    }
}
```

### Querying Animation State for Gameplay

```csharp
// Check if an entity is in a specific animation
public static bool IsPlayingAnimation(
    ref SpineAnimationComponent anim, string name)
{
    return anim.CurrentAnimation == name;
}

// Check if attack animation has passed the hit frame
public static bool HasPassedHitFrame(
    ref SpineAnimationComponent anim, float hitNormalizedTime = 0.4f)
{
    var current = anim.State.GetCurrent(0);
    if (current == null || current.Animation.Name != "attack") return false;
    float normalized = current.AnimationTime / current.Animation.Duration;
    return normalized >= hitNormalizedTime;
}

// Set animation only if not already playing (avoids restart)
public static void SetAnimationIfNew(
    ref SpineAnimationComponent anim, string name, bool loop)
{
    if (anim.CurrentAnimation == name) return;
    anim.State.SetAnimation(0, name, loop);
    anim.CurrentAnimation = name;
}
```

---

## 12 — Art Pipeline

### Workflow Overview

```
┌──────────────┐     ┌──────────┐     ┌──────────────┐     ┌──────────┐
│ Art (PSD/PNG) │ ──► │  Spine   │ ──► │ Export (.skel │ ──► │ MonoGame │
│ body parts   │     │  Editor  │     │   + .atlas)  │     │ Runtime  │
└──────────────┘     └──────────┘     └──────────────┘     └──────────┘
```

1. **Artist creates** individual body parts as separate images (arm, torso, head, etc.)
2. **Import into Spine editor** — assemble skeleton, set up bones, mesh weights, animations
3. **Export** — binary `.skel` + `.atlas` + atlas `.png` textures
4. **Load in game** — using the runtime code from Section 3

### File Formats

| Format | Extension | Size | Use Case |
|---|---|---|---|
| **Binary** | `.skel` | Small | Production — fast loading, compact |
| **JSON** | `.spine-json` | Large | Debugging — human-readable, diffable in VCS |
| **Atlas** | `.atlas` | Small | Texture region definitions |
| **Atlas PNG** | `.png` | Varies | Packed texture sheets |

### Export Settings

- **Binary** for production builds (2–10× smaller than JSON)
- **JSON** for version control diffing during development
- **Atlas packing**: use Spine's built-in packer. Settings:
  - Power of two: **Yes** (GPU-friendly)
  - Max size: **2048×2048** (safe for all platforms)
  - Padding: **2px** (prevents bleed)
  - Strip whitespace: **Yes** (saves atlas space)
  - Premultiply alpha: **Yes** (matches runtime setting)

### Version Compatibility

**Critical:** The Spine runtime version must match the editor export version. A skeleton exported from Spine 4.2 requires the 4.2 runtime. Mismatches cause crashes or silent corruption.

Pin your runtime version in your `.csproj` and coordinate editor version across the team:

Spine does **not** distribute via NuGet. Include the runtime source directly from the [spine-runtimes](https://github.com/EsotericSoftware/spine-runtimes) repo:

```xml
<!-- In your .csproj — reference the Spine runtime projects directly -->
<ProjectReference Include="path/to/spine-csharp/spine-csharp.csproj" />
<ProjectReference Include="path/to/spine-monogame/spine-monogame.csproj" />
```

Pin to the release tag matching your editor version (e.g., `4.2.x` tag for Spine Editor 4.2).

### Content Pipeline Integration

For MonoGame's content pipeline, Spine files are loaded directly (not through the Content Pipeline `.mgcb`). Place them in a content directory and copy to output:

```xml
<!-- .csproj — copy Spine assets to output -->
<ItemGroup>
  <Content Include="Content/Spine/**/*.*">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
</ItemGroup>
```

```csharp
// Load from the output directory at runtime
var data = SpineAssets.Load(
    GraphicsDevice,
    "hero",
    Path.Combine(Content.RootDirectory, "Spine"));
```

---

## Quick Reference

| Task | Code |
|---|---|
| Load skeleton | `SpineAssets.Load(device, "name", "path")` |
| Create instance | `CreateInstance(data, mixDuration)` |
| Play animation | `state.SetAnimation(track, "name", loop)` |
| Queue animation | `state.AddAnimation(track, "name", loop, delay)` |
| Set crossfade | `stateData.SetMix("from", "to", duration)` |
| Apply skin | `SkinManager.SetCombinedSkin(skel, skins)` |
| Aim bone | `BoneManipulation.AimBoneAt(skel, "bone", target, speed, dt)` |
| Listen for event | `state.Event += (entry, e) => { ... }` |
| Flip character | `skeleton.ScaleX = facingLeft ? -1f : 1f` |
| Get bone world pos | `var b = skel.FindBone("hand"); pos = (b.WorldX, b.WorldY)` |

---

*Skeletal animation turns a handful of textures into unlimited fluid motion. Spine's runtime handles the hard parts — invest time learning the editor, and your characters will move beautifully with minimal code.*
