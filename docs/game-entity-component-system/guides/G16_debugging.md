# G16 — Debugging


> **Category:** Guide · **Related:** [G15 Game Loop](./G15_game_loop.md) · [G17 Testing](./G17_testing.md) · [R1 Library Stack](../reference/R1_library_stack.md) · [G13 C# Performance](./G13_csharp_performance.md) · [G14 Data Structures](./G14_data_structures.md) · [G12 Design Patterns](./G12_design_patterns.md)

Systematic debugging methodology, visual symptom diagnosis, MonoGame + Arch ECS debugging, ImGui tooling, structured logging, assertions, and common C# pitfalls.

---

## Debugging as a Systematic Process

Debugging is a learnable, systematic skill — not an art or a talent. Research consistently shows that what separates expert debuggers from novices is not intelligence but method: experts form hypotheses from observed data, search systematically, and maintain mental models at multiple abstraction levels, while novices guess, fixate on their first theory, and make random changes. A multi-institutional study of CS students found they could fix 97% of bugs once located — confirming that *finding* bugs is the hard part.

### The TRAFFIC Framework

Andreas Zeller's **TRAFFIC framework** from *Why Programs Fail* provides the complete systematic workflow:

1. **Track** the problem in your issue tracker
2. **Reproduce** the failure reliably
3. **Automate** and simplify the test case to its minimal form
4. **Find** possible infection origins through the scientific method
5. **Focus** on the most likely origins
6. **Isolate** the infection chain
7. **Correct** the defect and verify

Zeller's key insight is the **defect → infection → failure chain**: a programmer creates a defect in code; when executed, the defect causes an infection in program state; the infection propagates and eventually becomes visible as a failure. Debugging means tracing backward from failure to infection to defect.

### Agans' Nine Rules

David Agans distilled decades of hardware and software debugging into nine rules in his book *Debugging*:

1. **Understand the System** — read the docs, know the fundamentals, know the road map
2. **Make It Fail** — reproduce reliably; stimulate the failure, don't simulate it; automate reproduction to speed iteration
3. **Quit Thinking and Look** — get data first, don't guess at repairs
4. **Divide and Conquer** — binary search the problem space
5. **Change One Thing at a Time** — isolate variables
6. **Keep an Audit Trail** — write down what you did and what happened
7. **Check the Plug** — question basic assumptions (is the right version deployed? is it the right file?)
8. **Get a Fresh View** — explain the problem to someone, or a rubber duck
9. **If You Didn't Fix It, It Ain't Fixed** — verify the fix works and understand *why* it works

### Kernighan and Pike

Kernighan and Pike in *The Practice of Programming* frame debugging as **backward reasoning**: "Something impossible occurred, and the only solid information is that it really did occur." Their practical heuristics include examining the most recent change first, studying the numerology of failures (errors every 1023 bytes suggest an off-by-one in a 1024-byte buffer), and using sentinel values like `0xDEADBEEF` to make uninitialized memory access immediately visible. One university computer center kept a teddy bear near the help desk — students with mysterious bugs had to explain them to the bear before speaking to a human counselor.

---

## What Separates Expert Debuggers from Novices

Iris Vessey's foundational 1984–1985 studies established that **chunking ability** is the primary criterion distinguishing expert from novice debuggers. Drawing on Chase and Simon's chess expertise research, expert programmers group code into meaningful patterns — plans, schemas, idioms — and recall far more relevant information at a glance. Experts display **breadth-first, system-level approaches** with smooth-flowing investigation. Novices display **erratic, depth-first approaches** driven by preconceived ideas rather than observed data.

The mental model difference is stark. Experts maintain well-organized, hierarchical mental models that let them reason about program behavior at multiple abstraction levels simultaneously. Novices have fragmented understanding — they may grasp a single statement but struggle to see how blocks interact. A study of Chinese expert and novice debuggers found the two groups had similar processes for syntax bugs but diverged sharply on semantic and logic bugs, where system-level comprehension matters most.

**Cognitive biases permeate debugging.** Chattopadhyay et al.'s 2020 field study observed 10 developers in situ and found that roughly **70% of observed developer actions involved at least one cognitive bias**. The ten identified bias categories include preconceptions, fixation (anchoring on initial assumptions despite contradictory evidence), and convenience biases (preferring easy fixes over thorough investigation). Confirmation bias is particularly damaging: novice programmers exhibit a positive test bias, writing tests that confirm code works rather than tests designed to break it. The antidote is deliberate falsification — actively trying to prove your hypothesis wrong.

Eye-tracking research reveals how these differences manifest in visual attention. Experts read code **non-linearly**, jumping between relevant sections, while novices tend to read linearly like natural text. Expert programmers form an initial overview ("scan" pattern) before diving into details. Fritz et al. combined eye movements with EEG signals and found that pupil dilation predicted when developers found tasks difficult — a physiological marker of cognitive load during debugging.

The progression from novice to expert debugger follows a learnable path. Julia Evans captures the common incorrect assumptions that cause bugs: "this variable is set to X," "that variable's value can't have changed between X and Y," "this code was doing the right thing before," "I'm editing the right file," "the documentation is correct." Each assumption, when made explicit and questioned, becomes a debugging hypothesis. The expert's advantage is having encountered enough bugs to question these assumptions automatically.

---

## Reading Visual Symptoms

The first step in debugging is precise observation — characterizing exactly what you see before theorizing about causes. Game development offers unusually rich visual feedback, making symptom recognition a powerful diagnostic skill.

### Rendering Artifacts

Each artifact has a characteristic signature:

- **Z-fighting** (flickering where two surfaces overlap) — depth buffer precision issues, typically caused by a near/far clip plane ratio that wastes precision (z-buffer precision is logarithmic, with most resolution near the near plane)
- **Texture stretching/tearing** — UV coordinate errors, wrong sampler state settings, or incorrect source rectangles in sprite atlas lookups. In MonoGame, the default `SamplerState` is `LinearClamp`, which causes bleeding between atlas entries in pixel art — switch to `PointClamp`
- **Black or pink/magenta textures** — failed texture loading. Check MGCB build output, verify content paths match case-sensitively (critical on Linux/macOS), confirm `.xnb` files were built for the correct platform
- **Screen tearing** (visible horizontal splits between frames) — VSync is disabled or frame timing is mismatched → [G15](./G15_game_loop.md)

### Sprite Flickering in MonoGame

`SpriteSortMode.BackToFront` with identical `layerDepth` values does not guarantee stable draw order. Fix: use unique layer depth values or switch to `SpriteSortMode.Deferred` (draws in submission order).

`SpriteBatch.Begin()` modifies `BlendState`, `DepthStencilState`, `RasterizerState`, and `SamplerStates` but does **not** restore them after `End()`. This silently breaks any non-SpriteBatch rendering that follows. Save and restore graphics device state manually:

```csharp
// Save/restore GraphicsDevice state around debug SpriteBatch calls
var prevBlend = GraphicsDevice.BlendState;
var prevDepth = GraphicsDevice.DepthStencilState;
var prevRaster = GraphicsDevice.RasterizerState;
var prevSampler = GraphicsDevice.SamplerStates[0];

debugSpriteBatch.Begin(/* debug draw settings */);
// ... debug drawing ...
debugSpriteBatch.End();

GraphicsDevice.BlendState = prevBlend;
GraphicsDevice.DepthStencilState = prevDepth;
GraphicsDevice.RasterizerState = prevRaster;
GraphicsDevice.SamplerStates[0] = prevSampler;
```

Dark halos around sprites indicate a `BlendState` mismatch — MonoGame's content pipeline premultiplies alpha by default, but textures loaded via `Texture2D.FromStream()` are not premultiplied, requiring `BlendState.NonPremultiplied`.

### Physics Glitches

- **Tunneling** (objects passing through walls) — `velocity × deltaTime > object_width`. Fixes: continuous collision detection (swept shape tests), capping maximum velocity, thicker collision walls, or raycast-based hit detection for fast projectiles → [G3](./G3_physics_and_collision.md)
- **Jittering** — floating-point precision issues, competing forces (gravity fighting collision response each frame), or applying physics outside a fixed timestep → [G15](./G15_game_loop.md)
- **Physics explosions** (objects flying off at extreme velocity) — almost always NaN propagation, enormous forces from division by near-zero distance, or deeply interpenetrating objects causing explosive constraint solver responses. Add `float.IsNaN()` and `float.IsInfinity()` checks on all force calculations

### State Machine Errors

- **Entity stuck in wrong state** — missing or unreachable transition condition → [G12](./G12_design_patterns.md)
- **Rapid state flickering** between two states — conflicting transition conditions. Fix: add hysteresis (different thresholds for entering vs. leaving a state)
- **Animation snapping** between frames — missing interpolation
- **T-pose / default pose** — animation system isn't running on that entity (in ECS, the entity likely lacks the required animation component)

### Logic Errors

- **Off-by-one** — tiles misaligned by one cell, missing edge tiles, one-pixel gaps in tile maps
- **Frame-rate-dependent behavior** (movement speed changing at different FPS) — game logic isn't multiplied by `deltaTime` → [G15](./G15_game_loop.md)
- **Gradual performance degradation with frame hitches** — GC pressure. The sawtooth pattern in memory graphs indicates frequent garbage collection; `% Time in GC` should be under 10% → [G13](./G13_csharp_performance.md)

---

## The Complete Debugging Toolkit

### Breakpoint Strategies in Visual Studio

- **Conditional breakpoints** — right-click a breakpoint, select Conditions, enter an expression like `entity.Id == 42`. The "When changed" option breaks when the expression's value changes between evaluations
- **Data breakpoints** (.NET Core 3+) — break when a memory location changes value: "who is modifying this field?"
- **Logpoints** (tracepoints) — log to the Output window without pausing, supporting expressions in curly braces like `{variableName}`
- **Hit count breakpoints** — break after a specified number of hits, essential for debugging loop misbehavior at specific iterations
- **Dependent breakpoints** (Visual Studio 2022+) — activate only after another breakpoint has been hit first

For C# game development, **Object IDs** are powerful: right-click a reference variable in the Locals window, select Make Object ID, and Visual Studio assigns a persistent identifier (`$1`, `$2`) usable in conditional breakpoints and the Immediate Window even when the object is out of scope. The Immediate Window supports `expression, nse` (No Side Effects) to evaluate without changing application state. The **Diagnostic Tools Window** shows real-time memory usage and GC collection events — yellow spikes represent GC pauses that cause frame hitches. Exception Settings (Debug → Windows → Exception Settings) let you toggle break-on-throw for specific CLR exception types, catching exceptions at their origin rather than their catch site.

### Binary Search Debugging

Insert a probe (breakpoint, print statement, assertion) at the midpoint of the suspected code path, determine which half contains the bug, and repeat. Finding a bug in 1000 lines takes approximately **10 probes instead of 500**. This applies to code paths, input data (throw away half the input, check if the bug persists), and version history (`git bisect` automates this across commits).

The Wolf Fence algorithm is the same idea expressed as analogy: "There's one wolf in Alaska. Build a fence down the middle, wait for the wolf to howl, determine which side it's on. Repeat."

### Delta Debugging & Saff Squeeze

**Delta debugging** (Zeller) automatically minimizes a failure-inducing input to a 1-minimal test case — one where removing any single element makes the test pass. In a case study, it reduced 896 lines of HTML causing a Mozilla crash to a single `<SELECT>` tag, and reduced 95 user actions to 3 relevant ones.

The **Saff Squeeze** (Kent Beck/David Saff) turns a failing system-level test into a minimal failing unit test by repeatedly inlining called methods, adding earlier failing assertions, and pruning irrelevant code. Its unique advantage: you end up with both the root cause and a regression test → [G17](./G17_testing.md).

### Rubber Duck Debugging

Verbalization engages metacognition — thinking about your own thought process — using different brain pathways than silent reading. The key is not skipping over "obvious" parts. Start with the big picture, then go line by line, noting every point of confusion as a potential bug location.

### Print vs Breakpoint Debugging

Not a matter of sophistication but of fit:

| Situation | Use |
|-----------|-----|
| Timing-sensitive bugs where breakpoints alter behavior (Heisenberg effect) | Print/log |
| Multi-process systems where a debugger can't attach | Print/log |
| Need a persistent record of execution flow | Print/log |
| Need to inspect the full call stack | Breakpoint |
| Need to evaluate expressions at runtime | Breakpoint |
| Stepping through unfamiliar code | Breakpoint |
| Avoid modifying source files | Breakpoint |

Kernighan himself said "the most effective debugging tool is still careful thought, coupled with judiciously placed print statements."

---

## Graceful Degradation Over Crashes

In games, prefer graceful degradation. Use guard clauses with early returns for non-critical systems. Use `Debug.Assert` for development-only invariant checks that compile out of Release builds. Use exceptions only for truly unrecoverable situations.

```csharp
// Guard clauses for non-critical systems
public void ApplyDamage(Entity target, int amount)
{
    if (target == null) return; // Don't crash, just skip
    var health = target.GetComponent<HealthComponent>();
    if (health == null) return; // Entity without health — skip
    health.TakeDamage(amount);
}

// Debug.Assert for invariants — compiles out of Release
Debug.Assert(tileX >= 0 && tileX < Width, $"Tile X out of bounds: {tileX}");
Debug.Assert(component != null, "Required component missing");

// Obviously wrong visual defaults for missing assets
public static Texture2D MissingTexture; // Magenta checkerboard — immediately visible
```

Enable nullable reference types in your .csproj (`<Nullable>enable</Nullable>`). The compiler warns when you might dereference null.

---

## Game-Specific Debugging (MonoGame + Arch ECS)

### Debug Infrastructure Priority

The priority order for a solo game developer's debug tooling, from highest immediate value to most sophisticated:

1. **FPS counter and frame timing display** — trivial to implement, immediately useful for detecting performance regressions
2. **Debug draw system** for collision boxes and velocity vectors — catches the majority of visual bugs
3. **Entity inspector via ImGui** listing all components on selected entities — essential for ECS development
4. **Time controls** (pause, slow-motion, frame-step) — makes intermittent bugs reproducible
5. **Conditional logging with ring buffer** — catches logic bugs without flooding output

The core debug draw pattern is a static `DebugDraw` class that collects draw commands during `Update()` and renders them during `Draw()`, supporting lines, rectangles, circles, and text labels with optional duration parameters. Color-code collision bounds (green for no collision, red for currently colliding), draw velocity vectors as arrows showing direction and magnitude, and overlay state labels above entities showing their current state name. Remember to save and restore `GraphicsDevice` state before and after debug `SpriteBatch` calls (see the state save/restore pattern in [Reading Visual Symptoms](#sprite-flickering-in-monogame)).

### ECS Debugging with Arch

The number one ECS debugging challenge is the **silent missing component**: if an entity lacks a required component, it simply won't appear in any query that requires it, producing zero errors or warnings. The entity becomes invisible to the system with no diagnostic output.

Build an entity inspector that displays all components on a selected entity using `world.GetComponentTypes(entity)` and compare against the expected component set. Key Arch APIs for debugging:

- `world.Has<T>(entity)` — check individual components
- `world.GetArchetype(entity)` — returns the archetype (the entity's full "shape")
- `world.CountEntities(in query)` — monitor query match counts each frame to detect unexpected changes
- `world.IsAlive(entity)` — validate entity liveness before access

Diagnostic query pattern for finding misconfigured entities:

```csharp
// Find entities that have Position but are missing Velocity
var allEntities = new QueryDescription();
world.Query(in allEntities, (Entity entity) => {
    bool hasPos = world.Has<Position>(entity);
    bool hasVel = world.Has<Velocity>(entity);
    if (hasPos && !hasVel)
        Console.WriteLine($"Entity {entity.Id} has Position but missing Velocity!");
});
```

Safe component access in debug builds should validate both entity liveness and component existence before access, throwing descriptive exceptions that name the missing component and entity ID rather than allowing silent failures.

**System ordering bugs** are the second most common ECS issue — if System B reads data that System A should have written, but B runs before A, you get stale data with no error. Wrap each system update with `Stopwatch` timing and display the execution order in an ImGui panel. Arch.Extended provides system group infrastructure with lifecycle hooks for this kind of instrumentation. **Component data corruption** from multiple systems modifying shared state can be caught by snapshotting component values before and after each system runs during debug sessions.

### MonoGame-Specific Pitfalls

The **Update/Draw separation** is a common source of bugs. `Update()` runs at fixed timestep (default 60 Hz when `IsFixedTimeStep` is true), while `Draw()` runs as fast as possible or at VSync rate. Putting game logic in `Draw()` causes variable-rate behavior. When `IsFixedTimeStep` is true and Update takes too long, MonoGame calls Update multiple times per frame to catch up, potentially causing a "death spiral" — the `gameTime.IsRunningSlowly` flag indicates this catch-up mode → [G15](./G15_game_loop.md).

**Memory management** in C#/.NET games requires attention to allocation sources that generate GC pressure. The primary culprits in game loops:

- String concatenation — use `StringBuilder` or cached strings
- LINQ queries — generate garbage through iterator allocations; MonoGame docs explicitly warn against LINQ → [G13](./G13_csharp_performance.md)
- Lambda closures capturing variables — create allocations each frame; cache delegates
- Collections resized each frame — pre-allocate with known capacity
- Boxing value types through `object` parameters

Arch ECS components should be structs (`record struct` is idiomatic) for stack allocation and cache-friendly memory layout. For detecting leaks, Visual Studio's "Make Object ID" feature lets you track whether specific objects are collected: create an ID at allocation, then check if it's null after expected cleanup. Non-null means a leak.

Enable `GraphicsAdapter.UseDebugDevice = true` before creating the `GraphicsDeviceManager` to get detailed GPU error messages. Enable native code debugging in Visual Studio project properties to see SharpDX diagnostic messages in the Output window.

---

## In-Game Debug Tooling (ImGui.NET)

ImGui.NET is referenced directly via NuGet (see [R1](../reference/R1_library_stack.md)). AAA studios (the teams behind FF7 Remake, Assassin's Creed, Fallout 76) use Dear ImGui extensively for debug tooling. For a solo developer, the ability to build an entity inspector, system timing display, and tweakable parameters in a single afternoon makes it the highest-leverage debug tool after basic debug drawing. Build debug tools as you build the game:

- Entity inspectors (view/edit component values at runtime)
- Spatial partition visualization (draw grid cells, quadtree nodes)
- Performance graphs (frame time, GC collections, entity count, draw calls)
- State machine visualizer (current state, transition history)
- Collision box wireframes
- System execution order and per-system timing

### Frame-by-Frame Stepping

Essential for debugging physics and collision:

```csharp
#if DEBUG
if (Input.IsKeyPressed(Keys.P)) _paused = !_paused;
if (_paused && Input.IsKeyPressed(Keys.OemPeriod))
    _stepOneFrame = true;

if (!_paused || _stepOneFrame)
{
    UpdateSimulation(FixedDt);
    _stepOneFrame = false;
}
#endif
```

### Time Manipulation

Implement a time scale multiplier applied to `gameTime.ElapsedGameTime` and a pause flag that skips `Update()` calls. Store the time scale and pause state as global debug state toggled by function keys. Slow-motion (0.25× or 0.1× time scale) is invaluable for observing fast-moving interactions that are impossible to catch at full speed.

---

## Structured Logging

Ring buffer-backed logging with crash dump support:

```csharp
public static class GameLog
{
    public enum Level { Debug, Info, Warning, Error }
    public static Level MinLevel { get; set; } = Level.Debug;

    private static readonly RingBuffer<string> _recentLogs = new(200);

    public static void Log(Level level, string message)
    {
        if (level < MinLevel) return;
        var entry = $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {message}";
        _recentLogs.Push(entry);
        System.Diagnostics.Debug.WriteLine(entry);
    }

    // Dump recent logs on crash
    public static void WriteCrashLog(Exception ex)
    {
        var lines = new List<string> { ex.ToString(), "", "=== Recent Log ===" };
        for (int i = 0; i < _recentLogs.Count; i++)
            lines.Add(_recentLogs.Get(i));
        File.WriteAllLines($"crash_{DateTime.Now:yyyyMMdd_HHmmss}.log", lines);
    }
}

// Set to Warning in release builds
#if !DEBUG
GameLog.MinLevel = GameLog.Level.Warning;
#endif
```

`RingBuffer<T>` implementation: → [G14 Data Structures](./G14_data_structures.md)

---

## From Symptoms to Root Cause

### The Systematic Approach

Nicole Tietz-Sokolskaya's systematic approach captures the expert process:

1. **Figure out the symptoms** precisely — what exactly is the bad behavior, when did it start, what environments are affected
2. **Reproduce the bug** — in the same environment first, then reduce to minimal steps
3. **Understand the system** before jumping into the debugger — what code is running? what changed recently? what do "normal" logs look like?
4. **Form a hypothesis about location, not cause** — ask "where is the bug?" rather than "what is the bug?" because location is searchable while cause is not. Each hypothesis should eliminate roughly 50% of possible locations
5. **Test the hypothesis** by validating input/output at component boundaries
6. **Repeat** until the location is narrowed to a specific function or line

### The Debugging Manifesto

Julia Evans' Debugging Manifesto distills the mindset into eight principles, the most important being:

- **"Inspect, don't squash"** — leave the bug in place and understand it fully before fixing
- **"There's always a reason"** — nothing is truly random; intermittent bugs have deterministic causes you haven't identified yet
- **"Trust nobody and nothing"** — even the OS, popular libraries, and documentation can be wrong, though 95% of the time the bug is in your own code

### Symptom → Technique Table

| Symptom | Technique |
|---------|-----------|
| Crash | Read the stack trace bottom-to-top; examine variable state at top frame |
| Wrong behavior | State inspection with conditional breakpoints and watch windows |
| Intermittent failure | Persistent logging, data breakpoints, assertions |
| Performance issue | Profile first, then investigate hotspots → [G13](./G13_csharp_performance.md) |
| State corruption | Data breakpoints, invariant checks, "who modified this?" pattern |
| Unknown bug location | Binary search debugging or `git bisect` |
| Regression ("it used to work") | `git bisect` between known-good and known-bad commit |

### The Obra Methodology

The obra/superpowers debugging methodology claims a ~95% first-time fix rate (versus ~40% with ad hoc approaches) through a strict four-phase process:

1. **Observe** symptoms and gather evidence at component boundaries
2. **Identify** the failing component
3. **Root cause trace** — trace backward through the call stack, following invalid data to its original trigger
4. **Fix and verify** with defense in depth (add validation at multiple layers)

The critical discipline is **no fixes without root cause first**.

---

## Assertions and Sentinel Values

The most pernicious bugs are silent state corruption — where the defect occurs far from where its effects become visible. Assertions collapse this distance to zero. Programs like Firefox devote **10–20% of their code to assertions** checking that the other 80–90% works correctly.

The three categories serve distinct purposes:

- **Preconditions** — validate inputs at function entry
- **Postconditions** — validate outputs at function exit
- **Invariants** — validate state consistency at critical points

Chromium's approach distinguishes `CHECK()` (kills the process if false in all builds), `DCHECK()` (debug-only for expensive checks), and `NOTREACHED()` (marks code paths that should never execute). For C#/MonoGame, the equivalent pattern uses `Debug.Assert()` for development-only checks and explicit guard clauses with descriptive exceptions for critical invariants that should survive release builds:

```csharp
// Debug-only (compiles out of Release)
Debug.Assert(health > 0, $"Entity {id} health went negative: {health}");
Debug.Assert(world.IsAlive(entity), $"Operating on dead entity: {entity.Id}");

// Release-safe guard for critical invariants
if (index < 0 || index >= _items.Length)
    throw new ArgumentOutOfRangeException(nameof(index),
        $"Inventory index {index} out of range [0, {_items.Length})");
```

The two golden rules: **"fail early, fail often"** to catch mistakes as close to their origin as possible, and **"turn bugs into assertions or tests"** so every fixed bug leaves behind a sentinel that catches regression → [G17](./G17_testing.md).

**Sentinel values** make use-before-initialization and use-after-free immediately recognizable. In C# games, initialize fields to obviously wrong values so that improper access manifests as a visible failure rather than a subtle misbehavior:

```csharp
// Sentinel initialization — use-before-set becomes immediately visible
private Vector2 _position = new(float.NaN, float.NaN);
private int _targetIndex = -1;
private Entity? _owner = null; // Nullable — compiler enforces null check
```

---

## Common C# Pitfalls in Game Code

1. **Float equality:** Use epsilon comparison, never `==`
2. **Event handler leaks:** Always unsubscribe in cleanup/dispose → [G12](./G12_design_patterns.md)
3. **Mutable struct dictionary keys:** Hash changes after mutation → lost entries
4. **Struct copying through properties:** Getter returns a copy, not a reference
5. **Modifying collections during iteration:** Iterate backwards with index, or flag for deferred removal
6. **Forgetting to reset pooled objects:** One stale field causes cascading bugs
7. **LINQ in Update loops:** Invisible per-frame allocations → [G13](./G13_csharp_performance.md)
8. **Static closures accidentally capturing instance state**

```csharp
// Pitfall #5: safe removal during iteration
for (int i = _entities.Count - 1; i >= 0; i--)
{
    if (_entities[i].ShouldRemove)
        _entities.RemoveAt(i); // Safe — iterating backwards
}

// Or: deferred removal (better for large lists)
_removeQueue.Clear();
foreach (var entity in _entities)
    if (entity.ShouldRemove) _removeQueue.Add(entity);
foreach (var entity in _removeQueue)
    _entities.Remove(entity);
```
