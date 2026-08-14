# G1 — Custom Code Recipes


> **Category:** Guide · **Related:** [E1 Architecture Overview](../architecture/E1_architecture_overview.md) · [R1 Library Stack](../reference/R1_library_stack.md) · [R3 Project Structure](../reference/R3_project_structure.md) · [G30 Game Feel Tooling](./G30_game_feel_tooling.md)

---

These are the ~1,000 lines of custom code that replace Nez's framework features. Each module is self-contained and can be implemented independently. Total effort: ~14.5 hours.

---

## 1. Scene Manager (~150 lines)

Scenes are containers holding an update/draw loop. The scene manager handles lifecycle and transitions.

```csharp
public abstract class Scene
{
    public List<IUpdatable> Updatables { get; } = new();
    public List<IRenderable> Renderables { get; } = new();
    
    public virtual void Initialize() { }
    public virtual void Update(float dt) { foreach (var u in Updatables) u.Update(dt); }
    public virtual void Draw(SpriteBatch batch) { foreach (var r in Renderables) r.Draw(batch); }
    public virtual void Unload() { }
}

public class SceneManager
{
    private Scene _current;
    private Scene _next;
    
    public void TransitionTo(Scene scene) => _next = scene;
    
    public void Update(float dt)
    {
        if (_next != null)
        {
            _current?.Unload();
            _current = _next;
            _next = null;
            _current.Initialize();
        }
        _current?.Update(dt);
    }
    
    public void Draw(SpriteBatch batch) => _current?.Draw(batch);
}
```

**Why custom:** Zero dependency. You control the lifecycle. No framework lock-in. Trivial to add transition effects (see Screen Transitions below).

> **Growth path:** As your game grows, add `IUpdatable`/`IRenderable` lists to the `Scene` base class for composable subsystems — minimap, pause menu, and debug overlays register themselves rather than cluttering a monolithic `Update()`. Add a `DrawOverlay()` method for HUD elements that render outside virtual resolution scaling. `OnComplete` callback chaining on transitions handles sequencing without a formal sequence manager.

---

## 2. Render Layer System (~200 lines)

MonoGame's SpriteBatch handles sprite rendering. This adds render layer ordering and optional per-layer post-processing.

```csharp
public class RenderLayer
{
    public int Order { get; set; }
    public List<IRenderable> Items { get; } = new();
    public Effect PostProcessor { get; set; }      // Optional shader
    public RenderTarget2D Target { get; set; }      // For post-processing
}
```

**Pattern:** Sort layers by `Order`. For each layer: if it has a `PostProcessor`, draw to its `RenderTarget2D`, apply the shader, then composite to screen. Otherwise draw directly. MonoGame.Extended's `OrthographicCamera` provides the transformation matrix for `SpriteBatch.Begin()`.

**See also:** [G2 Rendering & Graphics](./G2_rendering_and_graphics.md) for post-processor shaders and lighting.

---

## 3. SpatialHash Broadphase (~80 lines)

Fast spatial queries for collision detection, raycasts, and proximity checks. Grid-based bucket system. Set cell size to 2x average object radius. Power-of-2 cell sizes enable bit-shift instead of division.

```csharp
public class SpatialHash<T>
{
    private readonly float _cellSize;
    private readonly Dictionary<long, List<T>> _cells = new();

    public SpatialHash(float cellSize)
    {
        _cellSize = cellSize;
    }

    private long GetKey(float x, float y)
    {
        int cx = (int)(x / _cellSize);
        int cy = (int)(y / _cellSize);
        return ((long)cx << 32) | (uint)cy;
    }

    public void Insert(float x, float y, T obj)
    {
        var key = GetKey(x, y);
        if (!_cells.TryGetValue(key, out var list))
        {
            list = new List<T>(8);
            _cells[key] = list;
        }
        list.Add(obj);
    }

    public void QueryArea(float x, float y, float radius, List<T> results)
    {
        int minCx = (int)((x - radius) / _cellSize);
        int maxCx = (int)((x + radius) / _cellSize);
        int minCy = (int)((y - radius) / _cellSize);
        int maxCy = (int)((y + radius) / _cellSize);

        for (int cx = minCx; cx <= maxCx; cx++)
        for (int cy = minCy; cy <= maxCy; cy++)
        {
            var key = ((long)cx << 32) | (uint)cy;
            if (_cells.TryGetValue(key, out var list))
                results.AddRange(list);
        }
    }

    public void Clear()
    {
        foreach (var list in _cells.Values) list.Clear();
    }
}
```

**Usage:** Rebuild each frame (call `Clear()`, re-insert all collidable entities). Query for nearby entities before running narrow-phase shape checks.

**Choosing the right spatial structure:** See [G14 Data Structures](./G14_data_structures.md) for when to use brute-force, spatial hash, or quadtrees.

**See also:** [G3 Physics & Collision](./G3_physics_and_collision.md) for narrow-phase shape collision and Aether.Physics2D.

---

## 4. Collision Shapes (~150 lines)

Narrow-phase collision checks. MonoGame.Extended provides these too, but they're simple enough to own.

**AABB vs AABB:**
```csharp
public static bool AABBOverlap(Rectangle a, Rectangle b)
    => a.Left < b.Right && a.Right > b.Left && a.Top < b.Bottom && a.Bottom > b.Top;
```

**Circle vs Circle:**
```csharp
public static bool CircleOverlap(Vector2 aPos, float aR, Vector2 bPos, float bR)
    => Vector2.DistanceSquared(aPos, bPos) < (aR + bR) * (aR + bR);
```

**Polygon vs Polygon (SAT):** ~50 lines implementing the Separating Axis Theorem. Project both polygons onto each edge normal; if any axis has no overlap, they don't collide.

**Collision layer filtering** — use bit flags for O(1) checks. See [G14 Data Structures](./G14_data_structures.md) for the full implementation.

---

## 5. Tween System (~100 lines)

Interpolation + easing curve + timer. Drives any numeric property.

```csharp
public class Tween
{
    private float _elapsed, _duration;
    private Action<float> _setter;
    private Func<float, float> _easing;
    private float _from, _to;
    
    public bool IsComplete => _elapsed >= _duration;
    
    public Tween(float from, float to, float duration, Action<float> setter, 
                 Func<float, float> easing = null)
    {
        _from = from; _to = to; _duration = duration;
        _setter = setter; _easing = easing ?? (t => t); // linear default
    }
    
    public void Update(float dt)
    {
        _elapsed = Math.Min(_elapsed + dt, _duration);
        float t = _easing(_elapsed / _duration);
        _setter(_from + (_to - _from) * t);
    }
}

// Common easing functions
public static class Ease
{
    public static float InQuad(float t) => t * t;
    public static float OutQuad(float t) => 1 - (1 - t) * (1 - t);
    public static float InOutQuad(float t) => t < 0.5f ? 2 * t * t : 1 - MathF.Pow(-2 * t + 2, 2) / 2;
    public static float OutBounce(float t) { /* standard bounce formula */ return t; }
}
```

**Manage with a `TweenManager`** that holds a `List<Tween>`, calls `Update()` on each, and removes completed ones.

**Alternative:** `Apos.Tweens` NuGet for a fluent API.

> **Growth path:** `OnComplete` callback chaining is sufficient for sequencing (slide in → then fade text). Only add pooling when you exceed ~100 concurrent tweens, generic `Tween<T>` when you frequently interpolate Vector2/Color, or a formal `TweenSequence` class when callback chains become unreadable.

---

## 6. Screen Transitions (~100 lines)

Render current scene to RenderTarget A, render next scene to RenderTarget B, interpolate.

```csharp
// Fade transition
float alpha = elapsedTime / transitionDuration;
batch.Draw(sceneA, Vector2.Zero, Color.White * (1 - alpha));
batch.Draw(sceneB, Vector2.Zero, Color.White * alpha);
```

**Wipe:** Draw sceneA, then draw sceneB with a clip rectangle that expands over time.

**Circle-in/out:** Shader that reads distance-from-center and discards pixels based on a progress uniform.

**Pixelate:** Shader that reduces resolution progressively, blending into the new scene.

All are variations of the same pattern: two RenderTargets + a progress float + a shader or blend mode.

> **Growth path:** Start with a progress-based polymorphic model (0-to-1 progress, subclasses override `Draw` with two RTs). Critical requirement: both scenes must update every frame during transitions for temporal sync. Graduate to a 5-phase lifecycle only when you need async loading screens.

---

## 7. Post-Processor Pipeline (~150 lines)

Renders the scene to a RenderTarget, applies a chain of shader effects, outputs to screen. See [G27](./G27_shaders_and_effects.md) for HLSL shader implementations and [G2](./G2_rendering_and_graphics.md) for the effect roster.

```csharp
public class PostProcessorPipeline
{
    private List<Effect> _effects = new();
    private RenderTarget2D _bufferA, _bufferB;
    
    public void Process(GraphicsDevice device, RenderTarget2D sceneTarget)
    {
        var source = sceneTarget;
        for (int i = 0; i < _effects.Count; i++)
        {
            var dest = (i % 2 == 0) ? _bufferA : _bufferB;
            device.SetRenderTarget(dest);
            // Draw source with _effects[i] applied
            // ...
            source = dest;
        }
        // Final result is in 'source' — draw to backbuffer
    }
}
```

---

## 8. Object Pool (~60 lines)

Eliminates frame-time spikes from frequent object creation and GC. Pre-allocate during loading, recycle instead of create/destroy.

```csharp
public class ObjectPool<T> where T : class, new()
{
    private readonly Queue<T> _available = new();
    private readonly HashSet<T> _inUse = new();
    private readonly Action<T> _onGet;
    private readonly Action<T> _onReturn;

    public ObjectPool(int initialSize, Action<T> onGet = null, Action<T> onReturn = null)
    {
        _onGet = onGet;
        _onReturn = onReturn;
        for (int i = 0; i < initialSize; i++)
            _available.Enqueue(new T());
    }

    public T Get()
    {
        var instance = _available.Count > 0 ? _available.Dequeue() : new T();
        _inUse.Add(instance);
        _onGet?.Invoke(instance);
        return instance;
    }

    public void Return(T instance)
    {
        if (!_inUse.Remove(instance)) return;
        _onReturn?.Invoke(instance);
        _available.Enqueue(instance);
    }
}
```

**Rules:** Pool only objects that allocate frequently, are expensive to create, and cause measurable GC spikes. Over-pooling adds complexity and risks use-after-return bugs. Every pooled object must be fully reset on return via the `_onReturn` callback.

---

## 9. Line Renderer (~50 lines)

Draw thick lines by generating quads from line segments. For each segment: compute perpendicular offset from the line direction, create 4 vertices forming a quad, draw as a triangle strip or two triangles.

For smooth joins: use miter joins (bisect the angle between segments) or rounded caps (draw a circle at each vertex). MonoGame.Extended also provides primitive drawing if you prefer a library.
