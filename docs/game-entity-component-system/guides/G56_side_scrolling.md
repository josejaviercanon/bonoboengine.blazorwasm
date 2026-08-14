# G56 — Side-Scrolling Perspective


> **Category:** Guide · **Related:** [G28 3/4 Top-Down Perspective](./G28_top_down_perspective.md) · [G49 Isometric Perspective](./G49_isometric.md) · [G52 Character Controller](./G52_character_controller.md) · [G37 Tilemap Systems](./G37_tilemap_systems.md) · [G20 Camera Systems](./G20_camera_systems.md)

---

## 1 — Side-Scroller Fundamentals

The side-scrolling perspective views the world from a fixed lateral angle. The player character moves left/right along a horizontal plane and up/down via jumping, climbing, or falling. **Gravity is the defining mechanic** — it constantly pulls the player downward, making every jump a commitment and every platform a destination.

### What Makes It Different from Top-Down

| Aspect | Side-Scroller | Top-Down |
|---|---|---|
| Movement axes | Horizontal free, vertical governed by gravity | Both axes free |
| Primary challenge | Platforming, timing, momentum | Navigation, positioning |
| Level flow | Predominantly horizontal with vertical sections | Omnidirectional |
| Physics feel | Weighty, arc-based jumps | Sliding / gliding movement |
| Camera | Follows horizontal travel, dampens vertical | Follows equally on both axes |

### Classic References

- **Celeste** — Tight air control, dash mechanic, screen-sized rooms, forgiving respawn
- **Hollow Knight** — Metroidvania structure, ability gating, interconnected world
- **Dead Cells** — Procedural level chunks, roguelite progression, combat-forward
- **Ori and the Blind Forest** — Fluid movement, environmental storytelling, escape sequences
- **Shovel Knight** — Retro tile-based design, bouncing pogo attack, discrete screens

### Core ECS Components

```csharp
// Gravity and grounded state — the backbone of any side-scroller
public record struct Gravity(float Strength = 980f);

public record struct Grounded(bool Value, float CoyoteTimer = 0f);

public record struct Velocity(Vector2 Value)
{
    public float X => Value.X;
    public float Y => Value.Y;
}

public record struct FacingDirection(int Sign = 1); // -1 left, +1 right

public record struct SideScrollerTag; // marks entities that obey side-scroller physics
```

---

## 2 — Level Design Principles

### Horizontal Flow with Vertical Variation

Side-scrolling levels read left-to-right (or right-to-left) like a sentence. The primary axis of progression is horizontal, but great levels weave in vertical detours — pits to descend into, towers to climb, secret rooms above the ceiling line.

**The golden rule:** the player should always have a sense of *forward*. Even in vertical sections, the exit should funnel back to the main horizontal flow.

### Platforming Rhythm

The best platformers alternate tension and release:

```
[Safe Zone] → [Challenge] → [Breather] → [Harder Challenge] → [Reward/Checkpoint]
```

- **Safe zones** introduce new elements passively (a moving platform with no pit beneath it)
- **Challenge sections** combine known elements (that same platform, now over spikes)
- **Breathers** give the player a moment to absorb, often with a visual reward or pickup
- **Escalation** layers mechanics (platform over spikes, plus a projectile-shooting turret)

### Teaching Through Level Design

Never use text tutorials. Instead:

1. **Isolate** — Introduce one mechanic in a zero-risk environment
2. **Test** — Present a simple challenge using only that mechanic
3. **Combine** — Mix the new mechanic with previously learned ones
4. **Twist** — Subvert expectations (the breakable floor that was always safe suddenly isn't)

### Visual Landmarks

Players need spatial anchors. Use distinct background elements, unique tile decorations, or lighting shifts to differentiate areas. A player lost in identical corridors is a player about to quit.

---

## 3 — Tile Layout for Platformers

### Common Tile Sizes

- **16×16** — The standard. Good balance of detail and grid density. Most retro-inspired games use this.
- **8×8** — Higher resolution grid, useful for slopes and fine detail. Celeste uses 8×8 collision tiles.
- **32×32** — Chunkier feel, fewer tiles per screen, faster to author.

### Tile Type Components

```csharp
public record struct TileCollision(TileCollisionType Type);

public enum TileCollisionType
{
    Empty,
    Solid,
    OneWayUp,        // passable from below, solid on top
    SlopeLeft45,     // rises left-to-right at 45°
    SlopeRight45,    // rises right-to-left at 45°
    SlopeLeft22Low,  // shallow slope, lower half
    SlopeLeft22High, // shallow slope, upper half
    Breakable,
    Hazard,          // spikes, lava surface
    Ladder,
    Water
}

public record struct BreakableTile(float Health, bool Respawns, float RespawnTime);

public record struct InteractiveTile(string TriggerAction); // "open_door", "toggle_switch", etc.
```

### Slope Handling

Slopes require special collision logic. The character's Y position is adjusted based on their X position within the slope tile:

```csharp
public static class SlopeHelper
{
    /// <summary>
    /// Returns the surface Y offset within a tile for a given X position.
    /// tileSize is the pixel dimension (e.g., 16).
    /// </summary>
    public static float GetSlopeY(TileCollisionType slope, float localX, int tileSize)
    {
        float t = localX / tileSize; // 0..1 across the tile
        return slope switch
        {
            TileCollisionType.SlopeLeft45  => tileSize * (1f - t),
            TileCollisionType.SlopeRight45 => tileSize * t,
            _ => 0f
        };
    }
}
```

### One-Way Platforms

A platform the player can jump through from below but land on from above. The key rule: **only collide when the player's bottom edge is at or above the platform's top edge and the player is falling**.

```csharp
public static bool ShouldCollideOneWay(
    float playerBottomY,    // previous frame
    float playerVelocityY,  // positive = falling
    float platformTopY)
{
    return playerVelocityY >= 0f && playerBottomY <= platformTopY + 1f;
}
```

### Layer Separation

Separate your tilemap into distinct layers:

| Layer | Purpose | Rendered | Collides |
|---|---|---|---|
| Background Decor | Parallax tiles, distant scenery | ✓ | ✗ |
| Back Detail | Behind-player decorations (vines, pipes) | ✓ | ✗ |
| Collision | Solid geometry, slopes, one-ways | Optional | ✓ |
| Front Detail | In front of player (foreground foliage) | ✓ | ✗ |

---

## 4 — Parallax Backgrounds

Parallax scrolling creates depth by moving background layers at different speeds relative to camera movement. Layers closer to the camera move faster; distant layers move slower.

### Parallax Layer Component

```csharp
public record struct ParallaxLayer(
    float SpeedX,           // multiplier: 0.0 = static, 1.0 = moves with camera
    float SpeedY,
    bool RepeatX,           // tile horizontally
    bool RepeatY,
    float AutoScrollX,      // pixels/sec for constant drift (clouds, etc.)
    float AutoScrollY,
    int DrawOrder           // lower = further back
);
```

### Parallax Rendering System

```csharp
public static void RenderParallaxLayers(
    World world,
    SpriteBatch spriteBatch,
    Vector2 cameraPosition,
    Rectangle viewport,
    float deltaTime)
{
    var query = new QueryDescription().WithAll<ParallaxLayer, Sprite>();

    world.Query(in query, (ref ParallaxLayer layer, ref Sprite sprite) =>
    {
        // Offset based on camera with parallax factor
        float offsetX = cameraPosition.X * layer.SpeedX + layer.AutoScrollX * deltaTime;
        float offsetY = cameraPosition.Y * layer.SpeedY + layer.AutoScrollY * deltaTime;

        if (layer.RepeatX)
        {
            // Tile the texture across the viewport width
            float texWidth = sprite.SourceRect.Width;
            float startX = -(offsetX % texWidth);
            if (startX > 0) startX -= texWidth;

            for (float x = startX; x < viewport.Width; x += texWidth)
            {
                spriteBatch.Draw(
                    sprite.Texture,
                    new Vector2(x, -offsetY),
                    sprite.SourceRect,
                    Color.White);
            }
        }
        else
        {
            spriteBatch.Draw(
                sprite.Texture,
                new Vector2(-offsetX, -offsetY),
                sprite.SourceRect,
                Color.White);
        }
    });
}
```

### Typical Layer Setup

```
Layer 0 — Sky gradient          (SpeedX: 0.0)   — static, never moves
Layer 1 — Distant mountains     (SpeedX: 0.1)   — barely shifts
Layer 2 — Mid-range hills       (SpeedX: 0.3)   — gentle movement
Layer 3 — Treeline / buildings  (SpeedX: 0.6)   — noticeable parallax
Layer 4 — Tilemap (game world)  (SpeedX: 1.0)   — 1:1 with camera
Layer 5 — Foreground foliage    (SpeedX: 1.2)   — moves faster than camera
```

> See [G22 Parallax & Depth Layers](./G22_parallax_depth_layers.md) for draw-order integration with the main render loop.

---

## 5 — Camera for Side-Scrollers

Side-scroller cameras need special treatment. A naively centered camera feels floaty and disorienting. The player needs to see *where they're going*, not where they've been.

> Full camera math and smoothing covered in [G20 Camera Systems](./G20_camera_systems.md). This section covers side-scroller-specific patterns.

### Side-Scroller Camera Component

```csharp
public record struct SideScrollCamera(
    float LookAheadX,          // pixels ahead of player in facing direction
    float LookAheadSpeed,      // how fast look-ahead shifts when turning
    float DeadZoneY,           // vertical band where camera ignores player movement
    float PlatformSnapSpeed,   // how fast camera settles to grounded Y
    float PeekDistance,         // pixels to shift when player holds up/down
    float PeekSpeed,
    Rectangle LevelBounds,     // camera clamped within these world-space bounds
    float CurrentLookAheadX,   // runtime state: current interpolated offset
    float CurrentPeekY,        // runtime state: current peek offset
    float SnapTargetY          // runtime state: last grounded Y position
);
```

### Camera Update System

```csharp
public static void UpdateSideScrollCamera(
    ref SideScrollCamera cam,
    ref Camera2D camera,
    Vector2 playerPosition,
    bool isGrounded,
    int facingSign,        // -1 or +1
    float inputVertical,   // -1 (look up), 0, +1 (look down)
    Rectangle viewport,
    float dt)
{
    // --- Horizontal look-ahead ---
    float targetLookAhead = facingSign * cam.LookAheadX;
    cam.CurrentLookAheadX = MathHelper.Lerp(
        cam.CurrentLookAheadX, targetLookAhead, cam.LookAheadSpeed * dt);

    float targetX = playerPosition.X + cam.CurrentLookAheadX;

    // --- Vertical dead zone + platform snap ---
    if (isGrounded)
        cam.SnapTargetY = playerPosition.Y;

    float targetY = cam.SnapTargetY;

    // Only break out of dead zone if player is far from snap target
    float verticalDelta = playerPosition.Y - cam.SnapTargetY;
    if (MathF.Abs(verticalDelta) > cam.DeadZoneY)
        targetY = playerPosition.Y - MathF.Sign(verticalDelta) * cam.DeadZoneY;

    // --- Peek (up/down input) ---
    float peekTarget = inputVertical * cam.PeekDistance;
    cam.CurrentPeekY = MathHelper.Lerp(cam.CurrentPeekY, peekTarget, cam.PeekSpeed * dt);
    targetY += cam.CurrentPeekY;

    // --- Smoothing ---
    float smoothX = MathHelper.Lerp(camera.Position.X, targetX, 6f * dt);
    float smoothY = MathHelper.Lerp(camera.Position.Y, targetY, cam.PlatformSnapSpeed * dt);

    // --- Clamp to level bounds ---
    float halfW = viewport.Width * 0.5f / camera.Zoom;
    float halfH = viewport.Height * 0.5f / camera.Zoom;

    smoothX = MathHelper.Clamp(smoothX,
        cam.LevelBounds.Left + halfW,
        cam.LevelBounds.Right - halfW);
    smoothY = MathHelper.Clamp(smoothY,
        cam.LevelBounds.Top + halfH,
        cam.LevelBounds.Bottom - halfH);

    camera.Position = new Vector2(smoothX, smoothY);
}
```

### Speed-Based Look-Ahead

For fast-paced games (Dead Cells, Sonic), scale look-ahead distance with player speed:

```csharp
float speedFactor = MathHelper.Clamp(MathF.Abs(velocity.X) / maxSpeed, 0f, 1f);
float dynamicLookAhead = cam.LookAheadX * speedFactor;
```

---

## 6 — Level Transitions

### Room Structure

```csharp
public record struct Room(
    string Id,
    Rectangle WorldBounds,        // pixel bounds in the world
    Rectangle CameraBounds,       // camera clamp area (may differ from world bounds)
    bool LockCamera               // true for boss rooms / challenge rooms
);

public record struct RoomTransition(
    string FromRoomId,
    string ToRoomId,
    Rectangle TriggerZone,        // overlap zone between rooms
    TransitionStyle Style
);

public enum TransitionStyle
{
    Seamless,        // camera scrolls to new room (Metroid-style)
    FadeToBlack,     // screen fades out, loads new room, fades in
    DoorEnter,       // player enters door, brief black, exits other side
    PipeSlide,       // Mario-style pipe animation
    ScreenWipe       // directional wipe matching travel direction
}
```

### Screen Lock for Boss Fights

When the player enters a boss arena, lock the camera and seal the exits:

```csharp
public record struct ArenaLock(
    Rectangle LockBounds,
    bool IsActive,
    bool SealExits        // spawn blocking tiles at entry/exit points
);

public static void ActivateArenaLock(
    World world,
    ref ArenaLock arena,
    ref SideScrollCamera cam)
{
    arena.IsActive = true;
    // Override camera bounds to the arena
    cam.LevelBounds = arena.LockBounds;
    // Seal exits — spawn solid tile entities at doorway positions
    if (arena.SealExits)
    {
        // Implementation: create temporary solid entities at door positions
    }
}
```

### Fade Transition Helper

```csharp
public record struct ScreenFade(
    float Alpha,           // 0 = transparent, 1 = black
    float Speed,           // fade rate per second
    FadeState State
);

public enum FadeState { None, FadingOut, Hold, FadingIn }

public static void UpdateScreenFade(ref ScreenFade fade, float dt)
{
    switch (fade.State)
    {
        case FadeState.FadingOut:
            fade.Alpha = MathF.Min(fade.Alpha + fade.Speed * dt, 1f);
            if (fade.Alpha >= 1f) fade.State = FadeState.Hold;
            break;
        case FadeState.FadingIn:
            fade.Alpha = MathF.Max(fade.Alpha - fade.Speed * dt, 0f);
            if (fade.Alpha <= 0f) fade.State = FadeState.None;
            break;
    }
}
```

---

## 7 — Common Platformer Hazards

### Hazard Components

```csharp
public record struct Hazard(HazardType Type, float Damage, bool InstantKill);

public enum HazardType
{
    Spike,
    Pit,               // bottomless — triggers respawn
    Projectile,
    MovingObstacle,
    Crusher,
    RotatingBlade
}

public record struct Crusher(
    Vector2 RetractedPos,
    Vector2 ExtendedPos,
    float ExtendSpeed,       // fast — the slam
    float RetractSpeed,      // slow — the wind-up
    float PauseDuration,     // time spent at each end
    float Timer,
    CrusherState State
);

public enum CrusherState { Retracted, Extending, Extended, Retracting }

public record struct RotatingHazard(
    Vector2 Pivot,
    float Radius,
    float AngularSpeed,      // radians/sec
    float CurrentAngle
);
```

### Crusher Update

```csharp
public static void UpdateCrusher(ref Crusher c, ref Position pos, float dt)
{
    c.Timer += dt;
    switch (c.State)
    {
        case CrusherState.Retracted:
            if (c.Timer >= c.PauseDuration)
            {
                c.State = CrusherState.Extending;
                c.Timer = 0f;
            }
            break;
        case CrusherState.Extending:
            float tExtend = MathF.Min(c.Timer * c.ExtendSpeed, 1f);
            pos.Value = Vector2.Lerp(c.RetractedPos, c.ExtendedPos, tExtend);
            if (tExtend >= 1f)
            {
                c.State = CrusherState.Extended;
                c.Timer = 0f;
            }
            break;
        case CrusherState.Extended:
            if (c.Timer >= c.PauseDuration * 0.3f) // short pause at bottom
            {
                c.State = CrusherState.Retracting;
                c.Timer = 0f;
            }
            break;
        case CrusherState.Retracting:
            float tRetract = MathF.Min(c.Timer * c.RetractSpeed, 1f);
            pos.Value = Vector2.Lerp(c.ExtendedPos, c.RetractedPos, tRetract);
            if (tRetract >= 1f)
            {
                c.State = CrusherState.Retracted;
                c.Timer = 0f;
            }
            break;
    }
}
```

### Checkpoint / Respawn System

```csharp
public record struct Checkpoint(Vector2 SpawnPosition, string RoomId, bool Activated);

public record struct RespawnState(
    Vector2 LastCheckpoint,
    string CheckpointRoomId,
    float RespawnDelay,       // brief pause before respawn (Celeste-style)
    float Timer
);

public static void HandlePlayerDeath(
    ref RespawnState respawn,
    ref Position playerPos,
    ref Velocity velocity,
    ref Health health)
{
    // Freeze player, start respawn timer
    velocity.Value = Vector2.Zero;
    respawn.Timer = respawn.RespawnDelay;
    // After timer expires (checked in update loop):
    // playerPos.Value = respawn.LastCheckpoint;
    // health = health with { Current = health.Max };
}
```

---

## 8 — Environmental Mechanics

### Moving Platforms

```csharp
public record struct MovingPlatform(
    Vector2 PointA,
    Vector2 PointB,
    float Speed,
    float WaitTime,        // pause at endpoints
    float Progress,        // 0..1 along path
    int Direction,         // +1 or -1
    float WaitTimer
);

public record struct PlatformPassenger; // tag: entity currently riding a platform

public static void UpdateMovingPlatform(ref MovingPlatform mp, ref Position pos, float dt)
{
    if (mp.WaitTimer > 0f)
    {
        mp.WaitTimer -= dt;
        return;
    }

    float distance = Vector2.Distance(mp.PointA, mp.PointB);
    mp.Progress += mp.Direction * (mp.Speed / distance) * dt;

    if (mp.Progress >= 1f)
    {
        mp.Progress = 1f;
        mp.Direction = -1;
        mp.WaitTimer = mp.WaitTime;
    }
    else if (mp.Progress <= 0f)
    {
        mp.Progress = 0f;
        mp.Direction = 1;
        mp.WaitTimer = mp.WaitTime;
    }

    Vector2 prevPos = pos.Value;
    pos.Value = Vector2.Lerp(mp.PointA, mp.PointB, mp.Progress);

    // The delta is applied to any riding passenger in a separate system
    // platformDelta = pos.Value - prevPos;
}
```

### Environmental Effect Components

```csharp
public record struct ConveyorBelt(float SpeedX);  // applies horizontal force to player

public record struct WindZone(Vector2 Force);       // constant directional push

public record struct WaterZone(
    float DragMultiplier,      // 0.5 = half speed
    float GravityMultiplier,   // 0.3 = floaty
    float SwimForce            // upward force when pressing jump in water
);

public record struct IceSurface(float FrictionMultiplier); // 0.1 = very slippery

public record struct BouncePad(float LaunchVelocityY);     // negative = upward

public record struct GrapplePoint(
    Vector2 AnchorPosition,
    float MaxRopeLength,
    bool IsActive
);
```

### Water Zone Physics

```csharp
public static void ApplyWaterPhysics(
    ref Velocity vel,
    ref Gravity gravity,
    in WaterZone water,
    bool swimInput,
    float dt)
{
    // Reduce gravity
    float waterGravity = gravity.Strength * water.GravityMultiplier;
    vel = vel with { Value = new Vector2(
        vel.Value.X * water.DragMultiplier,
        vel.Value.Y + waterGravity * dt
    )};

    // Cap fall speed in water
    if (vel.Value.Y > 60f)
        vel = vel with { Value = new Vector2(vel.Value.X, 60f) };

    // Swim upward on input
    if (swimInput)
        vel = vel with { Value = new Vector2(vel.Value.X, vel.Value.Y - water.SwimForce * dt) };
}
```

---

## 9 — Enemy Patterns for Side-Scrollers

### Enemy Type Components

```csharp
public record struct PatrolEnemy(
    Vector2 PointA,
    Vector2 PointB,
    float Speed,
    float WaitTime,
    float WaitTimer,
    bool MovingToB
);

public record struct ChaserEnemy(
    float DetectRange,
    float Speed,
    float GiveUpRange,     // stop chasing if player gets this far
    bool IsChasing
);

public record struct FlyerEnemy(
    Vector2 CenterPoint,
    float AmplitudeX,
    float AmplitudeY,
    float FrequencyX,
    float FrequencyY,
    float Phase
);

public record struct TurretEnemy(
    float FireRate,        // shots per second
    float DetectRange,
    float FireTimer,
    Vector2 AimDirection
);

public record struct ChargerEnemy(
    float TelegraphDuration,   // warning flash before charge
    float ChargeSpeed,
    float ChargeDuration,
    float CooldownDuration,
    float Timer,
    ChargerState State
);

public enum ChargerState { Idle, Telegraphing, Charging, Cooldown }
```

### Patrol System

```csharp
public static void UpdatePatrolEnemy(ref PatrolEnemy patrol, ref Position pos, ref FacingDirection facing, float dt)
{
    if (patrol.WaitTimer > 0f)
    {
        patrol.WaitTimer -= dt;
        return;
    }

    Vector2 target = patrol.MovingToB ? patrol.PointB : patrol.PointA;
    Vector2 direction = Vector2.Normalize(target - pos.Value);
    pos.Value += direction * patrol.Speed * dt;
    facing = facing with { Sign = direction.X >= 0 ? 1 : -1 };

    if (Vector2.DistanceSquared(pos.Value, target) < 4f)
    {
        pos.Value = target;
        patrol.MovingToB = !patrol.MovingToB;
        patrol.WaitTimer = patrol.WaitTime;
    }
}
```

### Flyer System (Sine-Wave Movement)

```csharp
public static void UpdateFlyerEnemy(ref FlyerEnemy flyer, ref Position pos, float dt)
{
    flyer.Phase += dt;
    pos.Value = new Vector2(
        flyer.CenterPoint.X + MathF.Sin(flyer.Phase * flyer.FrequencyX) * flyer.AmplitudeX,
        flyer.CenterPoint.Y + MathF.Sin(flyer.Phase * flyer.FrequencyY) * flyer.AmplitudeY
    );
}
```

### Charger System (Telegraph → Dash)

```csharp
public static void UpdateChargerEnemy(
    ref ChargerEnemy charger,
    ref Position pos,
    ref FacingDirection facing,
    Vector2 playerPos,
    float detectRange,
    float dt)
{
    charger.Timer += dt;

    switch (charger.State)
    {
        case ChargerState.Idle:
            if (Vector2.Distance(pos.Value, playerPos) < detectRange)
            {
                charger.State = ChargerState.Telegraphing;
                charger.Timer = 0f;
                facing = facing with { Sign = playerPos.X > pos.Value.X ? 1 : -1 };
            }
            break;

        case ChargerState.Telegraphing:
            // Flash/shake animation plays here
            if (charger.Timer >= charger.TelegraphDuration)
            {
                charger.State = ChargerState.Charging;
                charger.Timer = 0f;
            }
            break;

        case ChargerState.Charging:
            pos.Value += new Vector2(facing.Sign * charger.ChargeSpeed * dt, 0);
            if (charger.Timer >= charger.ChargeDuration)
            {
                charger.State = ChargerState.Cooldown;
                charger.Timer = 0f;
            }
            break;

        case ChargerState.Cooldown:
            if (charger.Timer >= charger.CooldownDuration)
            {
                charger.State = ChargerState.Idle;
                charger.Timer = 0f;
            }
            break;
    }
}
```

### Spawn Triggers and Arena Locks

Enemies can be spawned on-demand when the player enters a trigger zone, rather than existing from level load:

```csharp
public record struct SpawnTrigger(
    Rectangle Zone,
    bool Triggered,
    string[] EnemyPrefabIds     // what to spawn
);

public record struct ArenaLockTrigger(
    Rectangle Zone,
    Rectangle CameraLock,
    bool Triggered,
    int EnemyCount              // unlock when this reaches 0
);
```

---

## 10 — Metroidvania Structure

### World Map Architecture

A Metroidvania world is a graph of interconnected rooms grouped into **regions** (areas/biomes). Each region has a visual theme, unique enemies, and one or more ability gates.

```csharp
public record struct WorldRegion(
    string Id,
    string Name,
    string[] RoomIds,
    Color MapColor           // minimap tint for this region
);

public record struct AbilityGate(
    string RequiredAbility,   // "double_jump", "wall_climb", "dash"
    Rectangle BlockingZone,
    string HintMessage        // subtle visual hint about what's needed
);

public record struct PlayerAbilities(
    bool DoubleJump,
    bool WallClimb,
    bool Dash,
    bool WaterBreathing,
    bool SuperStrength        // break special blocks
);
```

### Map / Minimap System

```csharp
public record struct MapCell(
    int GridX,
    int GridY,
    string RoomId,
    MapCellState State,
    bool HasSavePoint,
    bool HasFastTravel,
    bool HasItem
);

public enum MapCellState
{
    Hidden,       // not yet visited or adjacent
    Revealed,     // adjacent to visited — shown as outline
    Visited       // fully explored — shown filled
}

public record struct MinimapConfig(
    int CellSize,            // pixels per cell on the minimap
    int VisibleRadius,       // cells around the player to show
    Vector2 ScreenPosition,  // where to draw the minimap
    float Opacity
);
```

### Save Rooms and Fast Travel

```csharp
public record struct SavePoint(
    Vector2 Position,
    string RoomId,
    bool IsActive           // lights up when player is nearby
);

public record struct FastTravelNode(
    string Id,
    string DisplayName,
    Vector2 Position,
    string RoomId,
    bool Unlocked           // activated by first visit
);

public static void SaveGame(
    PlayerAbilities abilities,
    RespawnState respawn,
    List<string> collectedItems,
    List<string> unlockedFastTravel,
    MapCell[] mapState)
{
    // Serialize to JSON/binary and write to save file
    var saveData = new SaveData
    {
        Abilities = abilities,
        Checkpoint = respawn.LastCheckpoint,
        CheckpointRoom = respawn.CheckpointRoomId,
        Items = collectedItems,
        FastTravel = unlockedFastTravel,
        Map = mapState
    };
    // File.WriteAllText(savePath, JsonSerializer.Serialize(saveData));
}
```

### Ability Gating Design Principles

- **Soft gates** — skilled players can bypass (e.g., a tricky jump that's trivial with double jump)
- **Hard gates** — physically impossible without the ability (e.g., a wall too high to reach without wall climb)
- **Visual language** — consistent visual cues per ability (blue blocks = dash, green vines = wall climb)
- **Backtracking reward** — old areas should feel different with new abilities, and hide meaningful upgrades

---

## 11 — Endless Runner Variant

The endless runner strips side-scrolling to its core: the world scrolls automatically, the player reacts. No backtracking, no exploration — pure reflex and pattern recognition.

### Core Components

```csharp
public record struct AutoScroller(
    float ScrollSpeed,
    float SpeedIncreaseRate,    // acceleration over time
    float MaxSpeed,
    float DistanceTravelled
);

public record struct ChunkSpawner(
    float SpawnAheadDistance,    // how far ahead of camera to spawn
    float DespawnBehindDistance, // how far behind camera to destroy
    float NextSpawnX,           // world X where next chunk spawns
    int DifficultyTier          // increases over time
);

public record struct ScoreTracker(
    int Score,
    int HighScore,
    float DistanceMultiplier,   // score per pixel traveled
    int PickupBonus             // bonus per collected item
);

public record struct DifficultyRamp(
    float Timer,
    float TierInterval,         // seconds between difficulty increases
    int CurrentTier,
    int MaxTier
);
```

### Chunk-Based Procedural Spawning

Pre-author chunks (hand-designed segments) tagged by difficulty, then randomly select and place them:

```csharp
public record struct LevelChunk(
    string Id,
    int DifficultyMin,
    int DifficultyMax,
    float WidthPixels,
    // Tile data, enemy spawns, pickup positions, etc.
    TileData[] Tiles,
    EnemySpawn[] Enemies,
    Vector2[] Pickups
);

public static class ChunkSpawnerSystem
{
    private static readonly Random _rng = new();

    public static void Update(
        ref ChunkSpawner spawner,
        ref AutoScroller scroller,
        ref DifficultyRamp difficulty,
        LevelChunk[] chunkLibrary,
        World world,
        float cameraRightEdge,
        float dt)
    {
        // Increase difficulty over time
        difficulty.Timer += dt;
        if (difficulty.Timer >= difficulty.TierInterval &&
            difficulty.CurrentTier < difficulty.MaxTier)
        {
            difficulty.CurrentTier++;
            difficulty.Timer = 0f;
        }

        // Increase scroll speed
        scroller.ScrollSpeed = MathF.Min(
            scroller.ScrollSpeed + scroller.SpeedIncreaseRate * dt,
            scroller.MaxSpeed);

        // Spawn chunks ahead of camera
        while (spawner.NextSpawnX < cameraRightEdge + spawner.SpawnAheadDistance)
        {
            // Filter chunks by current difficulty tier
            var eligible = chunkLibrary
                .Where(c => c.DifficultyMin <= difficulty.CurrentTier
                         && c.DifficultyMax >= difficulty.CurrentTier)
                .ToArray();

            if (eligible.Length == 0) break;

            var chunk = eligible[_rng.Next(eligible.Length)];
            SpawnChunk(world, chunk, spawner.NextSpawnX);
            spawner.NextSpawnX += chunk.WidthPixels;
        }
    }

    private static void SpawnChunk(World world, LevelChunk chunk, float startX)
    {
        // Instantiate tile entities, enemies, and pickups
        // offset all positions by startX
        foreach (var pickup in chunk.Pickups)
        {
            world.Create(
                new Position(pickup + new Vector2(startX, 0)),
                new Pickup(),
                new Sprite()
            );
        }
        // Similarly for tiles and enemies...
    }
}
```

### Score System

```csharp
public static void UpdateScore(
    ref ScoreTracker score,
    ref AutoScroller scroller,
    float dt)
{
    // Distance-based scoring
    float distanceThisFrame = scroller.ScrollSpeed * dt;
    scroller.DistanceTravelled += distanceThisFrame;
    score.Score += (int)(distanceThisFrame * score.DistanceMultiplier);

    // High score tracking
    if (score.Score > score.HighScore)
        score.HighScore = score.Score;
}
```

---

## Quick Reference — Component Summary

| Component | Section | Purpose |
|---|---|---|
| `Gravity` | §1 | Downward force constant |
| `Grounded` | §1 | On-ground state + coyote time |
| `Velocity` | §1 | Current movement vector |
| `FacingDirection` | §1 | Left/right facing |
| `TileCollision` | §3 | Per-tile collision type |
| `BreakableTile` | §3 | Destructible tile state |
| `ParallaxLayer` | §4 | Background scrolling config |
| `SideScrollCamera` | §5 | Side-scroller camera behavior |
| `Room` | §6 | Level room definition |
| `RoomTransition` | §6 | Transition between rooms |
| `ScreenFade` | §6 | Fade-to-black effect state |
| `Hazard` | §7 | Damage source typing |
| `Crusher` | §7 | Crushing obstacle behavior |
| `RotatingHazard` | §7 | Spinning hazard |
| `Checkpoint` | §7 | Respawn position |
| `MovingPlatform` | §8 | A-to-B platform motion |
| `ConveyorBelt` | §8 | Horizontal force surface |
| `WindZone` | §8 | Directional push area |
| `WaterZone` | §8 | Swimming physics area |
| `IceSurface` | §8 | Reduced friction surface |
| `BouncePad` | §8 | Vertical launch pad |
| `GrapplePoint` | §8 | Swing/grapple anchor |
| `PatrolEnemy` | §9 | Walk back-and-forth AI |
| `ChaserEnemy` | §9 | Pursue-player AI |
| `FlyerEnemy` | §9 | Sine-wave flight AI |
| `TurretEnemy` | §9 | Stationary shooter AI |
| `ChargerEnemy` | §9 | Telegraph-and-dash AI |
| `SpawnTrigger` | §9 | On-demand enemy spawning |
| `AbilityGate` | §10 | Ability-locked passage |
| `PlayerAbilities` | §10 | Unlocked traversal abilities |
| `MapCell` | §10 | Minimap exploration state |
| `SavePoint` | §10 | Save room location |
| `FastTravelNode` | §10 | Warp point |
| `AutoScroller` | §11 | Endless runner scroll state |
| `ChunkSpawner` | §11 | Procedural level generation |
| `ScoreTracker` | §11 | Score and high-score state |
| `DifficultyRamp` | §11 | Progressive difficulty |

---

*Side-scrolling is the perspective that launched the platformer genre. Gravity turns empty space into danger, platforms into goals, and movement into expression. Whether you're building a tight five-hour Metroidvania or a pick-up-and-play endless runner, the principles here — rhythm, teaching through design, camera that serves the player, and mechanics that layer cleanly — will carry the experience.*
