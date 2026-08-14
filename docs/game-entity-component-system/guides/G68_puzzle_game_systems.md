# G68 — Puzzle Game Systems

> **Category:** Guide · **Related:** [G10 Custom Game Systems §8 Undo/Redo](./G10_custom_game_systems.md) · [G38 Scene Management](./G38_scene_management.md) · [G5 UI Framework](./G5_ui_framework.md) · [G41 Tweening](./G41_tweening.md) · [G42 Screen Transitions](./G42_screen_transitions.md) · [G7 Input Handling](./G7_input_handling.md) · [G61 Tutorial & Onboarding](./G61_tutorial_onboarding.md) · [G30 Game Feel Tooling](./G30_game_feel_tooling.md)

> A complete implementation guide for puzzle game architecture in MonoGame + Arch ECS. Covers grid-based puzzle state, undo/redo with state snapshots, level loading and progression, move counting and scoring, hint systems, puzzle validation, level select, star ratings, and genre-specific patterns for Sokoban, match-3, sliding puzzles, and logic grids. Everything is composable — use the pieces your puzzle game needs.

---

## Table of Contents

1. [Design Philosophy](#1--design-philosophy)
2. [Grid & Board Components](#2--grid--board-components)
3. [Puzzle State Snapshots](#3--puzzle-state-snapshots)
4. [Advanced Undo/Redo System](#4--advanced-undoredo-system)
5. [Level Data & Level Loading](#5--level-data--level-loading)
6. [Level Select & Progression](#6--level-select--progression)
7. [Move Counter & Scoring](#7--move-counter--scoring)
8. [Star Rating & Par System](#8--star-rating--par-system)
9. [Puzzle Validation & Win Detection](#9--puzzle-validation--win-detection)
10. [Hint System](#10--hint-system)
11. [Replay & Ghost System](#11--replay--ghost-system)
12. [Timer & Time Pressure](#12--timer--time-pressure)
13. [Animated Piece Movement](#13--animated-piece-movement)
14. [Puzzle Reset & Restart](#14--puzzle-reset--restart)
15. [Genre: Sokoban / Push-Block](#15--genre-sokoban--push-block)
16. [Genre: Match-3 / Tile-Matching](#16--genre-match-3--tile-matching)
17. [Genre: Sliding Puzzle](#17--genre-sliding-puzzle)
18. [Genre: Logic Grid / Nonogram](#18--genre-logic-grid--nonogram)
19. [Genre: Physics Puzzle](#19--genre-physics-puzzle)
20. [Save/Load Integration](#20--saveload-integration)
21. [Accessibility](#21--accessibility)
22. [Common Mistakes & Anti-Patterns](#22--common-mistakes--anti-patterns)
23. [Tuning Reference](#23--tuning-reference)

---

## 1 — Design Philosophy

### Puzzles Are State Machines

Every puzzle game — Sokoban, match-3, Sudoku, sliding puzzles — is fundamentally a state machine over a finite board. A "move" transforms one board state into another. This insight drives the architecture:

```
Board State A  →  Player Action  →  Board State B  →  Win Check  →  Continue / Victory
                                                    →  Undo?     →  Board State A
```

If you can snapshot, restore, and diff board states, you can implement undo, replay, hints, star ratings, and validation for *any* puzzle type.

### Why Not Just Use G10 §8?

G10 §8 provides a generic command-pattern undo/redo framework. That's a good foundation, but puzzle games need more:

| G10 §8 Provides | Puzzle Games Also Need |
|-----------------|----------------------|
| Command stack with execute/undo | Full state snapshots (for branching undo, instant rewind) |
| Single redo branch | Multi-branch undo trees (explored path A, went back, tried path B) |
| Per-command granularity | "Undo to checkpoint" (skip animations, undo N moves at once) |
| Execute/undo methods | State diffing (what changed? highlight it) |
| — | Move counting against par |
| — | Replay serialization |

This guide builds on G10 §8's `ICommand` pattern but adds a snapshot layer underneath for robust puzzle-specific behavior.

### ECS Fit for Puzzles

Puzzles map to ECS differently than action games:

| Concept | ECS Role |
|---------|----------|
| Board/Grid | Singleton component or resource (one per puzzle) |
| Pieces/Tiles | Entities with `GridPosition`, `PieceType`, visual components |
| Moves | Events (short-lived entities) processed by systems |
| Undo history | Resource managed outside ECS (stack of snapshots) |
| Win condition | Query-based validation system |
| Level data | External JSON/binary loaded into ECS on level start |
| Score/Stars | Singleton component updated by move system |

### The Puzzle Frame Pipeline

Every frame in a puzzle game follows this pipeline:

```
1. Input System          → Read player input, generate MoveRequest
2. Validation System     → Check if move is legal
3. Execute System        → Apply move, snapshot state, increment counter
4. Cascade System        → Handle chain reactions (match-3 cascades, gravity)
5. Win Check System      → Test victory/loss conditions
6. Animation System      → Animate piece movement, VFX
7. UI Update System      → Update move counter, timer, score display
8. Hint System           → Update hint availability (optional)
```

Systems 2-4 may loop (cascading matches trigger more gravity, which triggers more matches). The pipeline handles this with a `PuzzleBusy` flag that blocks input until cascades resolve.

---

## 2 — Grid & Board Components

### Core Grid

The board is the heart of every puzzle. Use a flat array with row-major indexing for cache-friendly access.

```csharp
namespace MyGame.Puzzle;

/// <summary>
/// The puzzle board. Singleton component — one per active puzzle.
/// Stores cell values as integers. Meaning depends on puzzle type:
///   Sokoban: 0=empty, 1=wall, 2=box, 3=goal, 4=box-on-goal, 5=player, 6=player-on-goal
///   Match-3: 0=empty, 1-6=gem colors, 7+=special gems
///   Sliding: 0=empty slot, 1-15=numbered tiles
///   Logic:   0=unknown, 1=filled, 2=crossed-out
/// </summary>
public record struct PuzzleBoard
{
    public int Width;
    public int Height;
    public int[] Cells;  // row-major: index = y * Width + x

    public PuzzleBoard(int width, int height)
    {
        Width = width;
        Height = height;
        Cells = new int[width * height];
    }

    public readonly int Get(int x, int y) => Cells[y * Width + x];
    public void Set(int x, int y, int value) => Cells[y * Width + x] = value;

    public readonly bool InBounds(int x, int y)
        => x >= 0 && x < Width && y >= 0 && y < Height;

    /// <summary>Deep clone for snapshots.</summary>
    public readonly PuzzleBoard Clone()
    {
        var clone = new PuzzleBoard(Width, Height);
        Array.Copy(Cells, clone.Cells, Cells.Length);
        return clone;
    }

    /// <summary>Check if two boards are identical (for win detection, cycle detection).</summary>
    public readonly bool Equals(PuzzleBoard other)
    {
        if (Width != other.Width || Height != other.Height) return false;
        return Cells.AsSpan().SequenceEqual(other.Cells.AsSpan());
    }

    /// <summary>Count cells matching a predicate.</summary>
    public readonly int Count(Func<int, bool> predicate)
    {
        int count = 0;
        foreach (var cell in Cells)
            if (predicate(cell)) count++;
        return count;
    }

    /// <summary>Find first cell matching a value. Returns (-1,-1) if not found.</summary>
    public readonly (int X, int Y) Find(int value)
    {
        for (int i = 0; i < Cells.Length; i++)
        {
            if (Cells[i] == value)
                return (i % Width, i / Width);
        }
        return (-1, -1);
    }

    /// <summary>Find all cells matching a value.</summary>
    public readonly List<(int X, int Y)> FindAll(int value)
    {
        var results = new List<(int, int)>();
        for (int i = 0; i < Cells.Length; i++)
        {
            if (Cells[i] == value)
                results.Add((i % Width, i / Width));
        }
        return results;
    }

    /// <summary>Get 4-connected neighbors (up, right, down, left).</summary>
    public readonly void GetNeighbors(int x, int y, Span<(int X, int Y, int Value)> neighbors, out int count)
    {
        count = 0;
        ReadOnlySpan<(int dx, int dy)> dirs = stackalloc (int, int)[]
        {
            (0, -1), (1, 0), (0, 1), (-1, 0)
        };

        foreach (var (dx, dy) in dirs)
        {
            int nx = x + dx, ny = y + dy;
            if (InBounds(nx, ny))
                neighbors[count++] = (nx, ny, Get(nx, ny));
        }
    }
}
```

### Piece Components

Individual pieces live as ECS entities for rendering and animation:

```csharp
/// <summary>Piece position on the puzzle grid.</summary>
public record struct PiecePosition(int X, int Y);

/// <summary>What kind of piece this is.</summary>
public record struct PieceType(int TypeId, string SpriteKey);

/// <summary>Tag: this piece is currently animating (don't accept input).</summary>
public record struct Animating(float TimeRemaining);

/// <summary>Visual offset from grid position (for smooth movement).</summary>
public record struct VisualOffset(float OffsetX, float OffsetY);

/// <summary>Tag: this piece was just placed/moved (for highlighting).</summary>
public record struct JustMoved(float HighlightTime);

/// <summary>Tag: this piece should be destroyed after animation completes.</summary>
public record struct PendingDestroy;

/// <summary>Tag: this piece is selected by the player.</summary>
public record struct Selected;

/// <summary>Piece is falling due to gravity (match-3).</summary>
public record struct Falling(float Velocity, int TargetY);
```

### Board Metadata (Singleton)

```csharp
/// <summary>
/// Tracks puzzle session state. Singleton component alongside PuzzleBoard.
/// </summary>
public record struct PuzzleSession
{
    public string LevelId;
    public int MoveCount;
    public int ParMoves;        // target moves for 3-star rating
    public float ElapsedTime;
    public bool IsComplete;
    public bool IsBusy;         // true during cascades/animations — block input
    public int HintsUsed;
    public int UndosUsed;
    public int CascadeDepth;    // current cascade chain (match-3)
    public int MaxCascade;      // deepest cascade this puzzle (for scoring)
}

/// <summary>Per-puzzle configuration loaded from level data.</summary>
public record struct PuzzleConfig
{
    public bool AllowUndo;          // some puzzles disable undo
    public int MaxUndos;            // -1 = unlimited
    public bool HasTimer;
    public float TimeLimitSeconds;  // 0 = no limit
    public bool HasMoveLimit;
    public int MoveLimit;           // 0 = no limit
    public bool GravityEnabled;     // pieces fall when unsupported (match-3, Tetris)
    public float CascadeDelay;      // seconds between cascade steps (visual pacing)
    public bool ShowHints;
    public int MaxHints;            // -1 = unlimited
}
```

---

## 3 — Puzzle State Snapshots

The command pattern (G10 §8) records *what changed*. Snapshots record *everything*. For puzzles, snapshots are more powerful because:

1. **Random-access rewind** — jump to any previous state without replaying N undo commands.
2. **Branching exploration** — player undoes 5 moves, tries a different path. Command undo would lose the original branch.
3. **Cheap for small boards** — a 10×10 board is 400 bytes. Even 1000 snapshots = 400KB.

### Snapshot Structure

```csharp
/// <summary>
/// Immutable snapshot of puzzle state at a point in time.
/// Cheap to create (array copy), supports branching history.
/// </summary>
public readonly struct PuzzleSnapshot
{
    public readonly int[] BoardCells;
    public readonly int PlayerX;
    public readonly int PlayerY;
    public readonly int MoveNumber;
    public readonly float Timestamp;
    public readonly string MoveDescription;  // "Push box right", "Swap (2,3)↔(2,4)"

    public PuzzleSnapshot(
        PuzzleBoard board,
        int playerX, int playerY,
        int moveNumber, float timestamp,
        string moveDescription)
    {
        BoardCells = new int[board.Cells.Length];
        Array.Copy(board.Cells, BoardCells, board.Cells.Length);
        PlayerX = playerX;
        PlayerY = playerY;
        MoveNumber = moveNumber;
        Timestamp = timestamp;
        MoveDescription = moveDescription;
    }

    /// <summary>Restore this snapshot onto a board.</summary>
    public void RestoreTo(ref PuzzleBoard board, out int playerX, out int playerY)
    {
        Array.Copy(BoardCells, board.Cells, BoardCells.Length);
        playerX = PlayerX;
        playerY = PlayerY;
    }

    /// <summary>Compute which cells differ from another snapshot.</summary>
    public List<(int X, int Y, int OldValue, int NewValue)> Diff(
        PuzzleSnapshot other, int boardWidth)
    {
        var changes = new List<(int, int, int, int)>();
        for (int i = 0; i < BoardCells.Length; i++)
        {
            if (BoardCells[i] != other.BoardCells[i])
            {
                changes.Add((
                    i % boardWidth,
                    i / boardWidth,
                    BoardCells[i],
                    other.BoardCells[i]
                ));
            }
        }
        return changes;
    }
}
```

### Delta Compression (Large Boards)

For boards larger than ~30×30, full snapshots become expensive. Use delta compression:

```csharp
/// <summary>
/// Compressed snapshot that stores only changes from the previous state.
/// Use for large boards (30×30+) where full snapshots waste memory.
/// </summary>
public readonly struct DeltaSnapshot
{
    public readonly (int Index, int OldValue, int NewValue)[] Changes;
    public readonly int PlayerX;
    public readonly int PlayerY;
    public readonly int MoveNumber;
    public readonly string MoveDescription;

    public DeltaSnapshot(
        ReadOnlySpan<int> oldCells,
        ReadOnlySpan<int> newCells,
        int playerX, int playerY,
        int moveNumber, string moveDescription)
    {
        var changes = new List<(int, int, int)>();
        for (int i = 0; i < oldCells.Length; i++)
        {
            if (oldCells[i] != newCells[i])
                changes.Add((i, oldCells[i], newCells[i]));
        }
        Changes = changes.ToArray();
        PlayerX = playerX;
        PlayerY = playerY;
        MoveNumber = moveNumber;
        MoveDescription = moveDescription;
    }

    /// <summary>Apply this delta forward (redo).</summary>
    public void ApplyForward(Span<int> cells)
    {
        foreach (var (idx, _, newVal) in Changes)
            cells[idx] = newVal;
    }

    /// <summary>Apply this delta backward (undo).</summary>
    public void ApplyBackward(Span<int> cells)
    {
        foreach (var (idx, oldVal, _) in Changes)
            cells[idx] = oldVal;
    }
}
```

### Memory Budget

| Board Size | Full Snapshot | Delta (avg 3 changes) | 1000 Snapshots (Full) | 1000 Snapshots (Delta) |
|-----------|--------------|----------------------|----------------------|----------------------|
| 8×8 (chess) | 256 B | ~36 B | 250 KB | 35 KB |
| 10×10 | 400 B | ~36 B | 390 KB | 35 KB |
| 15×15 | 900 B | ~36 B | 879 KB | 35 KB |
| 30×30 | 3.6 KB | ~36 B | 3.5 MB | 35 KB |
| 50×50 | 10 KB | ~36 B | 9.8 MB ⚠️ | 35 KB |

**Rule of thumb:** Full snapshots for boards ≤20×20. Delta compression for anything larger. Most puzzle games use boards ≤15×15 so full snapshots are fine.

---

## 4 — Advanced Undo/Redo System

Builds on G10 §8's command pattern but adds snapshot integration, branching history, and bulk undo.

### Snapshot History Manager

```csharp
/// <summary>
/// Manages puzzle state history with branching support.
/// Supports: undo, redo, jump-to-move, branch exploration, replay export.
/// </summary>
public class PuzzleHistory
{
    private readonly List<PuzzleSnapshot> _snapshots = new();
    private int _currentIndex = -1;
    private readonly int _maxSnapshots;

    // Branch tracking: when player undoes and takes a new path,
    // the old "future" is preserved in a branch.
    private readonly List<(int BranchPoint, List<PuzzleSnapshot> Snapshots)> _branches = new();

    public int MoveCount => _currentIndex;
    public int TotalSnapshots => _snapshots.Count;
    public int BranchCount => _branches.Count;
    public bool CanUndo => _currentIndex > 0;
    public bool CanRedo => _currentIndex < _snapshots.Count - 1;

    public event Action<int>? OnUndo;      // moveNumber after undo
    public event Action<int>? OnRedo;      // moveNumber after redo
    public event Action? OnBranchCreated;

    public PuzzleHistory(int maxSnapshots = 5000)
    {
        _maxSnapshots = maxSnapshots;
    }

    /// <summary>Record the initial board state (move 0).</summary>
    public void RecordInitial(PuzzleBoard board, int playerX, int playerY)
    {
        _snapshots.Clear();
        _branches.Clear();
        _currentIndex = 0;
        _snapshots.Add(new PuzzleSnapshot(
            board, playerX, playerY, 0, 0f, "Initial state"));
    }

    /// <summary>Record a new move. If we're mid-history, branches the old future.</summary>
    public void RecordMove(PuzzleBoard board, int playerX, int playerY,
        float timestamp, string description)
    {
        // If we're not at the end, the old future becomes a branch
        if (_currentIndex < _snapshots.Count - 1)
        {
            var branchedMoves = _snapshots.GetRange(
                _currentIndex + 1,
                _snapshots.Count - _currentIndex - 1);
            _branches.Add((_currentIndex, new List<PuzzleSnapshot>(branchedMoves)));
            _snapshots.RemoveRange(_currentIndex + 1, _snapshots.Count - _currentIndex - 1);
            OnBranchCreated?.Invoke();
        }

        _currentIndex++;
        _snapshots.Add(new PuzzleSnapshot(
            board, playerX, playerY, _currentIndex, timestamp, description));

        // Trim oldest if over limit (keep initial state)
        if (_snapshots.Count > _maxSnapshots)
        {
            int trimCount = _snapshots.Count - _maxSnapshots;
            _snapshots.RemoveRange(1, trimCount); // keep index 0
            _currentIndex -= trimCount;
            // Invalidate branches that reference trimmed snapshots
            _branches.RemoveAll(b => b.BranchPoint < trimCount);
        }
    }

    /// <summary>Undo one move. Returns the restored snapshot.</summary>
    public PuzzleSnapshot? Undo()
    {
        if (!CanUndo) return null;
        _currentIndex--;
        OnUndo?.Invoke(_currentIndex);
        return _snapshots[_currentIndex];
    }

    /// <summary>Redo one move. Returns the restored snapshot.</summary>
    public PuzzleSnapshot? Redo()
    {
        if (!CanRedo) return null;
        _currentIndex++;
        OnRedo?.Invoke(_currentIndex);
        return _snapshots[_currentIndex];
    }

    /// <summary>Jump to any move in history (random access rewind).</summary>
    public PuzzleSnapshot? JumpTo(int moveNumber)
    {
        if (moveNumber < 0 || moveNumber >= _snapshots.Count) return null;
        _currentIndex = moveNumber;
        return _snapshots[_currentIndex];
    }

    /// <summary>Undo N moves at once (e.g., "undo to last checkpoint").</summary>
    public PuzzleSnapshot? UndoMultiple(int count)
    {
        int target = Math.Max(0, _currentIndex - count);
        return JumpTo(target);
    }

    /// <summary>Get the current snapshot (for display, comparison).</summary>
    public PuzzleSnapshot Current => _snapshots[_currentIndex];

    /// <summary>Get the initial state (for full reset).</summary>
    public PuzzleSnapshot Initial => _snapshots[0];

    /// <summary>Export move list for replay.</summary>
    public List<PuzzleSnapshot> ExportReplay()
        => _snapshots.GetRange(0, _currentIndex + 1);

    /// <summary>Get description of the next undo action (for UI tooltip).</summary>
    public string? PeekUndoDescription()
        => CanUndo ? _snapshots[_currentIndex].MoveDescription : null;

    /// <summary>Get description of the next redo action (for UI tooltip).</summary>
    public string? PeekRedoDescription()
        => CanRedo ? _snapshots[_currentIndex + 1].MoveDescription : null;

    /// <summary>Restore a branch (switch to an alternate explored path).</summary>
    public bool RestoreBranch(int branchIndex)
    {
        if (branchIndex < 0 || branchIndex >= _branches.Count) return false;

        var (branchPoint, branchedSnapshots) = _branches[branchIndex];

        // Save current future as a new branch before switching
        if (_currentIndex == branchPoint && _snapshots.Count > branchPoint + 1)
        {
            var currentFuture = _snapshots.GetRange(
                branchPoint + 1,
                _snapshots.Count - branchPoint - 1);
            _branches.Add((branchPoint, currentFuture));
        }

        // Trim current history to branch point
        _snapshots.RemoveRange(branchPoint + 1, _snapshots.Count - branchPoint - 1);

        // Append the branched snapshots
        _snapshots.AddRange(branchedSnapshots);
        _currentIndex = branchPoint;

        // Remove the restored branch
        _branches.RemoveAt(branchIndex);

        return true;
    }
}
```

### Undo/Redo ECS System

```csharp
/// <summary>Request to undo/redo. Created by input system, consumed by UndoRedoSystem.</summary>
public record struct UndoRequest;
public record struct RedoRequest;
public record struct ResetRequest;
public record struct JumpToMoveRequest(int MoveNumber);

public partial class UndoRedoSystem : BaseSystem<World, float>
{
    private readonly PuzzleHistory _history;

    public UndoRedoSystem(World world, PuzzleHistory history) : base(world)
        => _history = history;

    public override void Update(in float deltaTime)
    {
        ProcessUndos();
        ProcessRedos();
        ProcessResets();
        ProcessJumps();
    }

    private void ProcessUndos()
    {
        var undoQuery = new QueryDescription().WithAll<UndoRequest>();
        World.Query(undoQuery, (Entity e) =>
        {
            ref var session = ref World.Get<PuzzleSession>(World.Query<PuzzleSession>());
            if (session.IsBusy) { World.Destroy(e); return; }

            var snapshot = _history.Undo();
            if (snapshot.HasValue)
            {
                ref var board = ref World.Get<PuzzleBoard>(World.Query<PuzzleBoard>());
                snapshot.Value.RestoreTo(ref board, out int px, out int py);
                session.MoveCount = snapshot.Value.MoveNumber;
                session.UndosUsed++;
                SyncPieceEntities(board);
            }
            World.Destroy(e);
        });
    }

    private void ProcessRedos()
    {
        var redoQuery = new QueryDescription().WithAll<RedoRequest>();
        World.Query(redoQuery, (Entity e) =>
        {
            ref var session = ref World.Get<PuzzleSession>(World.Query<PuzzleSession>());
            if (session.IsBusy) { World.Destroy(e); return; }

            var snapshot = _history.Redo();
            if (snapshot.HasValue)
            {
                ref var board = ref World.Get<PuzzleBoard>(World.Query<PuzzleBoard>());
                snapshot.Value.RestoreTo(ref board, out int px, out int py);
                session.MoveCount = snapshot.Value.MoveNumber;
                SyncPieceEntities(board);
            }
            World.Destroy(e);
        });
    }

    private void ProcessResets()
    {
        var resetQuery = new QueryDescription().WithAll<ResetRequest>();
        World.Query(resetQuery, (Entity e) =>
        {
            ref var session = ref World.Get<PuzzleSession>(World.Query<PuzzleSession>());
            var initial = _history.Initial;
            ref var board = ref World.Get<PuzzleBoard>(World.Query<PuzzleBoard>());
            initial.RestoreTo(ref board, out int px, out int py);
            session.MoveCount = 0;
            session.ElapsedTime = 0;
            session.HintsUsed = 0;
            session.IsComplete = false;
            _history.RecordInitial(board, px, py);
            SyncPieceEntities(board);
            World.Destroy(e);
        });
    }

    private void ProcessJumps()
    {
        var jumpQuery = new QueryDescription().WithAll<JumpToMoveRequest>();
        World.Query(jumpQuery, (Entity e, ref JumpToMoveRequest req) =>
        {
            ref var session = ref World.Get<PuzzleSession>(World.Query<PuzzleSession>());
            if (session.IsBusy) { World.Destroy(e); return; }

            var snapshot = _history.JumpTo(req.MoveNumber);
            if (snapshot.HasValue)
            {
                ref var board = ref World.Get<PuzzleBoard>(World.Query<PuzzleBoard>());
                snapshot.Value.RestoreTo(ref board, out int px, out int py);
                session.MoveCount = snapshot.Value.MoveNumber;
                SyncPieceEntities(board);
            }
            World.Destroy(e);
        });
    }

    /// <summary>
    /// After restoring a snapshot, sync visual piece entities to match board state.
    /// Destroys/creates entities as needed.
    /// </summary>
    private void SyncPieceEntities(PuzzleBoard board)
    {
        // Destroy all existing piece entities
        var pieceQuery = new QueryDescription().WithAll<PiecePosition, PieceType>();
        var toDestroy = new List<Entity>();
        World.Query(pieceQuery, (Entity e) => toDestroy.Add(e));
        foreach (var e in toDestroy) World.Destroy(e);

        // Recreate from board state
        for (int y = 0; y < board.Height; y++)
        {
            for (int x = 0; x < board.Width; x++)
            {
                int cell = board.Get(x, y);
                if (cell > 0 && !IsStaticCell(cell)) // skip empty and walls
                {
                    World.Create(
                        new PiecePosition(x, y),
                        new PieceType(cell, GetSpriteKey(cell)),
                        new VisualOffset(0, 0)
                    );
                }
            }
        }
    }

    private static bool IsStaticCell(int cell) => cell == 1; // walls don't need entities
    private static string GetSpriteKey(int cell) => $"piece_{cell}";
}
```

### Undo Timeline UI

A visual timeline showing move history. Players can click any point to jump there:

```csharp
/// <summary>
/// Renders an interactive undo timeline bar.
/// Shows: initial state → moves → current position → future (grayed).
/// Click to jump to any move. Drag to scrub through history.
/// </summary>
public class UndoTimelineRenderer
{
    private readonly PuzzleHistory _history;
    private Rectangle _barBounds;
    private bool _isDragging;

    public UndoTimelineRenderer(PuzzleHistory history, Rectangle barBounds)
    {
        _history = history;
        _barBounds = barBounds;
    }

    public void Draw(SpriteBatch spriteBatch, SpriteFont font, Texture2D pixel)
    {
        int total = _history.TotalSnapshots;
        if (total <= 1) return;

        // Background bar
        spriteBatch.Draw(pixel, _barBounds, new Color(40, 40, 40));

        // Played portion (filled)
        float playedRatio = (float)_history.MoveCount / (total - 1);
        var playedRect = new Rectangle(
            _barBounds.X, _barBounds.Y,
            (int)(_barBounds.Width * playedRatio), _barBounds.Height);
        spriteBatch.Draw(pixel, playedRect, new Color(80, 160, 255));

        // Current position marker
        int markerX = _barBounds.X + (int)(_barBounds.Width * playedRatio);
        var markerRect = new Rectangle(markerX - 3, _barBounds.Y - 4,
            6, _barBounds.Height + 8);
        spriteBatch.Draw(pixel, markerRect, Color.White);

        // Branch indicators
        // (small dots above the bar at branch points)
        foreach (var branch in GetBranchPoints())
        {
            float branchRatio = (float)branch / (total - 1);
            int bx = _barBounds.X + (int)(_barBounds.Width * branchRatio);
            var dot = new Rectangle(bx - 2, _barBounds.Y - 8, 4, 4);
            spriteBatch.Draw(pixel, dot, new Color(255, 200, 50));
        }

        // Move counter text
        string text = $"Move {_history.MoveCount}/{total - 1}";
        var textPos = new Vector2(
            _barBounds.X + _barBounds.Width / 2 - font.MeasureString(text).X / 2,
            _barBounds.Y + _barBounds.Height + 4);
        spriteBatch.DrawString(font, text, textPos, Color.White);
    }

    /// <summary>Handle click/drag on the timeline. Returns target move or -1.</summary>
    public int HandleInput(MouseState mouse, MouseState prevMouse)
    {
        var mousePoint = new Point(mouse.X, mouse.Y);

        if (mouse.LeftButton == ButtonState.Pressed &&
            (_isDragging || _barBounds.Contains(mousePoint)))
        {
            _isDragging = true;
            float ratio = MathHelper.Clamp(
                (float)(mouse.X - _barBounds.X) / _barBounds.Width, 0f, 1f);
            return (int)(ratio * (_history.TotalSnapshots - 1));
        }

        if (mouse.LeftButton == ButtonState.Released)
            _isDragging = false;

        return -1;
    }

    private List<int> GetBranchPoints()
    {
        // Expose branch points from history for visualization
        return new List<int>(); // simplified — real impl queries _history.Branches
    }
}
```

---

## 5 — Level Data & Level Loading

### Level File Format

JSON is the simplest format for puzzle levels. It's human-readable, editable, and trivial to parse:

```json
{
  "id": "world1_level3",
  "name": "The Warehouse",
  "author": "Level Designer",
  "version": 1,
  "puzzleType": "sokoban",
  "width": 10,
  "height": 8,
  "par": 24,
  "difficulty": "medium",
  "tags": ["tutorial", "corner-push"],
  "cells": [
    1,1,1,1,1,1,1,1,1,1,
    1,0,0,0,0,0,0,0,0,1,
    1,0,0,2,0,0,2,0,0,1,
    1,0,0,0,0,0,0,0,0,1,
    1,0,3,0,0,0,0,3,0,1,
    1,0,0,0,5,0,0,0,0,1,
    1,0,0,0,0,0,0,0,0,1,
    1,1,1,1,1,1,1,1,1,1
  ],
  "metadata": {
    "hint": "Push the left box down first",
    "solution": "RRDDDLLUURRDDD",
    "bestKnownMoves": 18
  }
}
```

### Level Data Model

```csharp
/// <summary>
/// Deserialized level definition. Immutable after loading.
/// </summary>
public sealed class LevelData
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Author { get; init; } = "";
    public int Version { get; init; } = 1;
    public string PuzzleType { get; init; } = "generic";
    public int Width { get; init; }
    public int Height { get; init; }
    public int Par { get; init; }
    public string Difficulty { get; init; } = "medium";
    public string[] Tags { get; init; } = Array.Empty<string>();
    public int[] Cells { get; init; } = Array.Empty<int>();
    public LevelMetadata Metadata { get; init; } = new();

    /// <summary>Validate the level data is well-formed.</summary>
    public (bool Valid, string? Error) Validate()
    {
        if (string.IsNullOrEmpty(Id))
            return (false, "Level ID is required");
        if (Width <= 0 || Height <= 0)
            return (false, $"Invalid dimensions: {Width}×{Height}");
        if (Cells.Length != Width * Height)
            return (false, $"Cell count {Cells.Length} doesn't match {Width}×{Height}={Width * Height}");
        if (Par <= 0)
            return (false, "Par must be positive");
        return (true, null);
    }
}

public sealed class LevelMetadata
{
    public string? Hint { get; init; }
    public string? Solution { get; init; }  // encoded move string
    public int BestKnownMoves { get; init; }
}
```

### Level Pack & World Structure

Group levels into packs (worlds) for progression:

```csharp
/// <summary>
/// A collection of related levels (world, chapter, difficulty tier).
/// </summary>
public sealed class LevelPack
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string IconKey { get; init; } = "";
    public int UnlockRequirement { get; init; }  // stars needed to unlock this pack
    public List<string> LevelIds { get; init; } = new();
}

/// <summary>
/// Manages all level packs and provides ordered level access.
/// </summary>
public sealed class LevelManifest
{
    public List<LevelPack> Packs { get; init; } = new();
    public int TotalLevels => Packs.Sum(p => p.LevelIds.Count);

    /// <summary>Get the next level after completing the given one.</summary>
    public string? GetNextLevelId(string currentLevelId)
    {
        foreach (var pack in Packs)
        {
            int idx = pack.LevelIds.IndexOf(currentLevelId);
            if (idx >= 0 && idx < pack.LevelIds.Count - 1)
                return pack.LevelIds[idx + 1];
            if (idx == pack.LevelIds.Count - 1)
            {
                // Last level in pack — try first level of next pack
                int packIdx = Packs.IndexOf(pack);
                if (packIdx < Packs.Count - 1)
                    return Packs[packIdx + 1].LevelIds.FirstOrDefault();
            }
        }
        return null; // no next level
    }
}
```

### Level Loader

```csharp
/// <summary>
/// Loads level JSON files from Content or filesystem.
/// Caches loaded levels to avoid repeated disk I/O.
/// </summary>
public sealed class LevelLoader
{
    private readonly Dictionary<string, LevelData> _cache = new();
    private readonly string _basePath;

    public LevelLoader(string basePath)
    {
        _basePath = basePath;
    }

    /// <summary>Load a single level by ID.</summary>
    public LevelData Load(string levelId)
    {
        if (_cache.TryGetValue(levelId, out var cached))
            return cached;

        string path = Path.Combine(_basePath, $"{levelId}.json");
        if (!File.Exists(path))
            throw new FileNotFoundException($"Level not found: {path}");

        string json = File.ReadAllText(path);
        var level = JsonSerializer.Deserialize<LevelData>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException($"Failed to parse level: {levelId}");

        var (valid, error) = level.Validate();
        if (!valid)
            throw new InvalidDataException($"Invalid level {levelId}: {error}");

        _cache[levelId] = level;
        return level;
    }

    /// <summary>Load a level pack manifest.</summary>
    public LevelManifest LoadManifest(string manifestPath)
    {
        string json = File.ReadAllText(manifestPath);
        return JsonSerializer.Deserialize<LevelManifest>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException($"Failed to parse manifest: {manifestPath}");
    }

    /// <summary>Preload all levels in a pack (call during loading screen).</summary>
    public void PreloadPack(LevelPack pack)
    {
        foreach (var id in pack.LevelIds)
            Load(id);
    }

    /// <summary>Clear the cache (e.g., when switching packs).</summary>
    public void ClearCache() => _cache.Clear();
}
```

### Level-to-ECS Loader

```csharp
/// <summary>
/// Converts LevelData into ECS entities and components.
/// Call when starting a new level or restarting.
/// </summary>
public static class LevelSpawner
{
    /// <summary>
    /// Spawn a level into the ECS world. Clears existing puzzle entities first.
    /// Returns the puzzle session entity for reference.
    /// </summary>
    public static Entity SpawnLevel(World world, LevelData level, PuzzleHistory history)
    {
        // 1. Clear existing puzzle entities
        ClearPuzzleEntities(world);

        // 2. Create the board
        var board = new PuzzleBoard(level.Width, level.Height);
        Array.Copy(level.Cells, board.Cells, level.Cells.Length);

        // 3. Find player start position
        var (playerX, playerY) = board.Find(5); // 5 = player

        // 4. Create puzzle config
        var config = new PuzzleConfig
        {
            AllowUndo = true,
            MaxUndos = -1,
            HasTimer = false,
            TimeLimitSeconds = 0,
            HasMoveLimit = false,
            MoveLimit = 0,
            GravityEnabled = level.PuzzleType == "match3",
            CascadeDelay = 0.15f,
            ShowHints = level.Metadata.Hint != null,
            MaxHints = 3
        };

        // 5. Create session
        var session = new PuzzleSession
        {
            LevelId = level.Id,
            MoveCount = 0,
            ParMoves = level.Par,
            ElapsedTime = 0,
            IsComplete = false,
            IsBusy = false,
            HintsUsed = 0,
            UndosUsed = 0
        };

        // 6. Spawn singleton entities
        var boardEntity = world.Create(board, config, session);

        // 7. Spawn piece entities for rendering
        for (int y = 0; y < level.Height; y++)
        {
            for (int x = 0; x < level.Width; x++)
            {
                int cell = board.Get(x, y);
                if (cell > 0 && cell != 1) // skip empty and walls (walls can be tilemap)
                {
                    world.Create(
                        new PiecePosition(x, y),
                        new PieceType(cell, GetSpriteKeyForType(level.PuzzleType, cell)),
                        new VisualOffset(0, 0)
                    );
                }
            }
        }

        // 8. Initialize undo history with starting state
        history.RecordInitial(board, playerX, playerY);

        return boardEntity;
    }

    private static void ClearPuzzleEntities(World world)
    {
        var query = new QueryDescription().WithAny<PiecePosition, PuzzleBoard, PuzzleSession>();
        var entities = new List<Entity>();
        world.Query(query, (Entity e) => entities.Add(e));
        foreach (var e in entities) world.Destroy(e);
    }

    private static string GetSpriteKeyForType(string puzzleType, int cellValue) => puzzleType switch
    {
        "sokoban" => cellValue switch
        {
            2 => "box",
            3 => "goal",
            4 => "box_on_goal",
            5 => "player",
            6 => "player_on_goal",
            _ => $"cell_{cellValue}"
        },
        "match3" => $"gem_{cellValue}",
        "sliding" => $"tile_{cellValue}",
        _ => $"piece_{cellValue}"
    };
}
```

### String-Based Level Format (Compact)

For Sokoban-style puzzles, a text format is more readable in source files:

```csharp
/// <summary>
/// Parses classic Sokoban text format:
///   # = wall, . = goal, $ = box, @ = player
///   * = box on goal, + = player on goal, (space) = floor
/// </summary>
public static class SokobanParser
{
    private static readonly Dictionary<char, int> CharMap = new()
    {
        { ' ', 0 }, { '#', 1 }, { '$', 2 }, { '.', 3 },
        { '*', 4 }, { '@', 5 }, { '+', 6 }
    };

    public static LevelData Parse(string levelText, string id, string name, int par)
    {
        var lines = levelText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        int height = lines.Length;
        int width = lines.Max(l => l.Length);

        var cells = new int[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                char c = x < lines[y].Length ? lines[y][x] : ' ';
                cells[y * width + x] = CharMap.GetValueOrDefault(c, 0);
            }
        }

        return new LevelData
        {
            Id = id,
            Name = name,
            PuzzleType = "sokoban",
            Width = width,
            Height = height,
            Par = par,
            Cells = cells
        };
    }
}
```

Usage:
```csharp
var level = SokobanParser.Parse(@"
#####
#   #
# $ #
# . #
# @ #
#####", "tutorial_1", "First Push", 3);
```

---

## 6 — Level Select & Progression

### Progression Data

```csharp
/// <summary>
/// Persistent progression data for a single level.
/// Stored in save file, not ECS.
/// </summary>
public sealed class LevelProgress
{
    public string LevelId { get; set; } = "";
    public bool Completed { get; set; }
    public int BestMoves { get; set; } = int.MaxValue;
    public float BestTime { get; set; } = float.MaxValue;
    public int Stars { get; set; }          // 0-3
    public int Attempts { get; set; }
    public DateTime? FirstCompleted { get; set; }
    public DateTime? LastPlayed { get; set; }
}

/// <summary>
/// Persistent progression across all levels.
/// </summary>
public sealed class PuzzleProgressData
{
    public Dictionary<string, LevelProgress> Levels { get; set; } = new();
    public int TotalStars => Levels.Values.Sum(l => l.Stars);
    public int TotalCompleted => Levels.Values.Count(l => l.Completed);

    public LevelProgress GetOrCreate(string levelId)
    {
        if (!Levels.TryGetValue(levelId, out var progress))
        {
            progress = new LevelProgress { LevelId = levelId };
            Levels[levelId] = progress;
        }
        return progress;
    }

    /// <summary>Record a level completion. Updates best if improved.</summary>
    public void RecordCompletion(string levelId, int moves, float time, int stars)
    {
        var progress = GetOrCreate(levelId);
        progress.Completed = true;
        progress.Attempts++;
        progress.LastPlayed = DateTime.UtcNow;
        progress.FirstCompleted ??= DateTime.UtcNow;

        if (moves < progress.BestMoves) progress.BestMoves = moves;
        if (time < progress.BestTime) progress.BestTime = time;
        if (stars > progress.Stars) progress.Stars = stars;
    }

    /// <summary>Check if a level pack is unlocked.</summary>
    public bool IsPackUnlocked(LevelPack pack)
        => TotalStars >= pack.UnlockRequirement;
}
```

### Level Select Screen

```csharp
/// <summary>
/// Grid-based level select screen with star displays and lock states.
/// Handles pack tabs, level buttons, and progression display.
/// </summary>
public class LevelSelectScreen
{
    private readonly LevelManifest _manifest;
    private readonly PuzzleProgressData _progress;
    private readonly LevelLoader _loader;

    private int _selectedPackIndex;
    private int _hoveredLevelIndex = -1;

    // Layout constants
    private const int COLUMNS = 5;
    private const int CELL_SIZE = 80;
    private const int CELL_PADDING = 12;
    private const int STAR_SIZE = 12;

    public event Action<string>? OnLevelSelected;
    public event Action<int>? OnPackChanged;

    public LevelSelectScreen(
        LevelManifest manifest,
        PuzzleProgressData progress,
        LevelLoader loader)
    {
        _manifest = manifest;
        _progress = progress;
        _loader = loader;
    }

    /// <summary>Get the currently visible pack.</summary>
    public LevelPack CurrentPack => _manifest.Packs[_selectedPackIndex];

    public void Update(MouseState mouse, MouseState prevMouse, KeyboardState keyboard)
    {
        // Tab switching (left/right arrows or click pack tabs)
        if (IsNewPress(keyboard, Keys.Left) && _selectedPackIndex > 0)
        {
            _selectedPackIndex--;
            OnPackChanged?.Invoke(_selectedPackIndex);
        }
        if (IsNewPress(keyboard, Keys.Right) && _selectedPackIndex < _manifest.Packs.Count - 1)
        {
            _selectedPackIndex++;
            OnPackChanged?.Invoke(_selectedPackIndex);
        }

        // Level hover and click
        var pack = CurrentPack;
        _hoveredLevelIndex = -1;

        for (int i = 0; i < pack.LevelIds.Count; i++)
        {
            var bounds = GetLevelButtonBounds(i);
            if (bounds.Contains(mouse.X, mouse.Y))
            {
                _hoveredLevelIndex = i;

                if (mouse.LeftButton == ButtonState.Pressed &&
                    prevMouse.LeftButton == ButtonState.Released)
                {
                    string levelId = pack.LevelIds[i];
                    if (IsLevelAccessible(levelId, i))
                        OnLevelSelected?.Invoke(levelId);
                }
            }
        }
    }

    public void Draw(SpriteBatch spriteBatch, SpriteFont font,
        SpriteFont smallFont, Texture2D pixel, Texture2D starTex, Texture2D lockTex)
    {
        var pack = CurrentPack;
        bool packUnlocked = _progress.IsPackUnlocked(pack);

        // Pack title
        string title = packUnlocked
            ? pack.Name
            : $"{pack.Name} (🔒 {pack.UnlockRequirement} stars needed)";
        spriteBatch.DrawString(font, title, new Vector2(40, 20), Color.White);

        // Pack progress
        int packStars = pack.LevelIds
            .Sum(id => _progress.GetOrCreate(id).Stars);
        int maxStars = pack.LevelIds.Count * 3;
        string progressText = $"★ {packStars}/{maxStars}";
        spriteBatch.DrawString(smallFont, progressText, new Vector2(40, 50), Color.Gold);

        // Level grid
        for (int i = 0; i < pack.LevelIds.Count; i++)
        {
            var bounds = GetLevelButtonBounds(i);
            string levelId = pack.LevelIds[i];
            var levelProgress = _progress.GetOrCreate(levelId);
            bool accessible = IsLevelAccessible(levelId, i);

            // Background
            Color bgColor = !accessible ? new Color(40, 40, 40) :       // locked
                            i == _hoveredLevelIndex ? new Color(80, 120, 180) :  // hovered
                            levelProgress.Completed ? new Color(50, 90, 50) :    // completed
                            new Color(60, 60, 80);                               // available
            spriteBatch.Draw(pixel, bounds, bgColor);

            // Border
            Color borderColor = i == _hoveredLevelIndex ? Color.White : new Color(100, 100, 100);
            DrawBorder(spriteBatch, pixel, bounds, borderColor, 2);

            if (!accessible)
            {
                // Lock icon
                var lockPos = new Vector2(
                    bounds.X + bounds.Width / 2 - 12,
                    bounds.Y + bounds.Height / 2 - 12);
                spriteBatch.Draw(lockTex, lockPos, Color.Gray);
            }
            else
            {
                // Level number
                string num = (i + 1).ToString();
                var numSize = font.MeasureString(num);
                spriteBatch.DrawString(font, num,
                    new Vector2(
                        bounds.X + bounds.Width / 2 - numSize.X / 2,
                        bounds.Y + 10),
                    Color.White);

                // Stars (0-3)
                int stars = levelProgress.Stars;
                float starsWidth = 3 * STAR_SIZE + 2 * 4; // 3 stars + gaps
                float startX = bounds.X + bounds.Width / 2 - starsWidth / 2;
                for (int s = 0; s < 3; s++)
                {
                    var starPos = new Vector2(startX + s * (STAR_SIZE + 4),
                        bounds.Y + bounds.Height - STAR_SIZE - 8);
                    Color starColor = s < stars ? Color.Gold : new Color(60, 60, 60);
                    spriteBatch.Draw(starTex,
                        new Rectangle((int)starPos.X, (int)starPos.Y, STAR_SIZE, STAR_SIZE),
                        starColor);
                }

                // Best moves (if completed)
                if (levelProgress.Completed)
                {
                    string best = $"{levelProgress.BestMoves}";
                    var bestSize = smallFont.MeasureString(best);
                    spriteBatch.DrawString(smallFont, best,
                        new Vector2(
                            bounds.X + bounds.Width / 2 - bestSize.X / 2,
                            bounds.Y + 38),
                        new Color(180, 180, 180));
                }
            }
        }

        // Pack navigation arrows
        if (_selectedPackIndex > 0)
            spriteBatch.DrawString(font, "◄", new Vector2(10, 40), Color.White);
        if (_selectedPackIndex < _manifest.Packs.Count - 1)
            spriteBatch.DrawString(font, "►",
                new Vector2(spriteBatch.GraphicsDevice.Viewport.Width - 30, 40), Color.White);
    }

    private Rectangle GetLevelButtonBounds(int index)
    {
        int col = index % COLUMNS;
        int row = index / COLUMNS;
        return new Rectangle(
            40 + col * (CELL_SIZE + CELL_PADDING),
            80 + row * (CELL_SIZE + CELL_PADDING),
            CELL_SIZE,
            CELL_SIZE);
    }

    /// <summary>
    /// A level is accessible if:
    /// 1. The pack is unlocked
    /// 2. It's the first level OR the previous level is completed
    /// </summary>
    private bool IsLevelAccessible(string levelId, int indexInPack)
    {
        var pack = CurrentPack;
        if (!_progress.IsPackUnlocked(pack)) return false;
        if (indexInPack == 0) return true;

        string prevId = pack.LevelIds[indexInPack - 1];
        return _progress.GetOrCreate(prevId).Completed;
    }

    private static bool IsNewPress(KeyboardState keyboard, Keys key)
        => keyboard.IsKeyDown(key); // simplified — real impl tracks previous state

    private static void DrawBorder(SpriteBatch sb, Texture2D pixel,
        Rectangle rect, Color color, int thickness)
    {
        sb.Draw(pixel, new Rectangle(rect.X, rect.Y, rect.Width, thickness), color);
        sb.Draw(pixel, new Rectangle(rect.X, rect.Bottom - thickness, rect.Width, thickness), color);
        sb.Draw(pixel, new Rectangle(rect.X, rect.Y, thickness, rect.Height), color);
        sb.Draw(pixel, new Rectangle(rect.Right - thickness, rect.Y, thickness, rect.Height), color);
    }
}
```

---

## 7 — Move Counter & Scoring

### Move Tracking System

```csharp
/// <summary>
/// Tracks moves, validates move legality, and fires events.
/// Plugs into the puzzle pipeline between input and execution.
/// </summary>
public partial class MoveTrackingSystem : BaseSystem<World, float>
{
    private readonly PuzzleHistory _history;

    public event Action<int>? OnMoveExecuted;   // total moves
    public event Action<int>? OnMoveLimit;      // remaining moves when limited

    public MoveTrackingSystem(World world, PuzzleHistory history) : base(world)
        => _history = history;

    /// <summary>
    /// Call after a move is validated and applied to the board.
    /// Records the snapshot and increments the counter.
    /// </summary>
    public void RecordMove(ref PuzzleBoard board, ref PuzzleSession session,
        int playerX, int playerY, string description)
    {
        session.MoveCount++;
        _history.RecordMove(board, playerX, playerY,
            session.ElapsedTime, description);

        OnMoveExecuted?.Invoke(session.MoveCount);

        // Check move limit
        ref var config = ref World.Get<PuzzleConfig>(World.Query<PuzzleConfig>());
        if (config.HasMoveLimit)
        {
            int remaining = config.MoveLimit - session.MoveCount;
            OnMoveLimit?.Invoke(remaining);

            if (remaining <= 0 && !session.IsComplete)
            {
                // Out of moves — puzzle failed
                World.Create(new PuzzleFailed { Reason = FailReason.MovesExhausted });
            }
        }
    }
}

public record struct PuzzleFailed
{
    public FailReason Reason;
}

public enum FailReason
{
    MovesExhausted,
    TimeExpired,
    Deadlock,       // Sokoban: box stuck in corner
    PlayerDied      // physics puzzles
}
```

### Scoring Models

Different puzzle types need different scoring. Here are three common models:

```csharp
/// <summary>
/// Scoring strategies for different puzzle types.
/// </summary>
public static class PuzzleScoring
{
    /// <summary>
    /// Move-based scoring (Sokoban, sliding puzzles).
    /// Fewer moves = higher score. Par defines baseline.
    /// </summary>
    public static int ScoreByMoves(int movesTaken, int par)
    {
        if (movesTaken <= 0) return 0;

        // Base: 1000 points at par
        // Better than par: +50 per move saved (up to 2x base)
        // Worse than par: -25 per extra move (floor: 100)
        int diff = par - movesTaken;
        int score = 1000 + diff * (diff > 0 ? 50 : 25);
        return Math.Clamp(score, 100, 2000);
    }

    /// <summary>
    /// Cascade/combo scoring (match-3).
    /// Bigger chains = exponential bonuses.
    /// </summary>
    public static int ScoreCascade(int matchSize, int cascadeDepth, int basePerGem = 10)
    {
        // Match size bonus: 3=1x, 4=1.5x, 5=2x, 6+=3x
        float sizeMultiplier = matchSize switch
        {
            <= 3 => 1.0f,
            4 => 1.5f,
            5 => 2.0f,
            _ => 3.0f
        };

        // Cascade depth: each level doubles the score
        float cascadeMultiplier = MathF.Pow(2, cascadeDepth);

        return (int)(matchSize * basePerGem * sizeMultiplier * cascadeMultiplier);
    }

    /// <summary>
    /// Time-based scoring (timed puzzles).
    /// Faster = higher score with a time bonus.
    /// </summary>
    public static int ScoreByTime(float timeTaken, float parTime, int baseMoveScore)
    {
        float timeRatio = parTime / MathF.Max(timeTaken, 1f);
        float timeBonus = MathF.Min(timeRatio, 2.0f); // cap at 2x
        return (int)(baseMoveScore * timeBonus);
    }

    /// <summary>
    /// Combined scoring for puzzles with moves + time + bonus objectives.
    /// </summary>
    public static int ScoreCombined(
        int movesTaken, int par,
        float timeTaken, float parTime,
        int hintsUsed, int undosUsed,
        bool bonusObjective = false)
    {
        int moveScore = ScoreByMoves(movesTaken, par);
        float timeMultiplier = MathF.Clamp(parTime / MathF.Max(timeTaken, 1f), 0.5f, 1.5f);

        // Penalties
        int hintPenalty = hintsUsed * 100;
        int undoPenalty = undosUsed * 10;  // light penalty — don't discourage undo

        // Bonus
        int bonus = bonusObjective ? 500 : 0;

        return Math.Max(100,
            (int)(moveScore * timeMultiplier) - hintPenalty - undoPenalty + bonus);
    }
}
```

---

## 8 — Star Rating & Par System

### Star Calculation

Stars are the universal puzzle progression currency. Players earn 1-3 stars per level:

```csharp
/// <summary>
/// Star rating calculation. Configurable per puzzle type.
/// </summary>
public static class StarRating
{
    /// <summary>
    /// Move-based stars (Sokoban, sliding puzzle).
    /// 3 stars = at or under par
    /// 2 stars = within 50% over par
    /// 1 star = completed (any move count)
    /// </summary>
    public static int CalculateByMoves(int movesTaken, int par)
    {
        if (movesTaken <= par) return 3;
        if (movesTaken <= (int)(par * 1.5f)) return 2;
        return 1;
    }

    /// <summary>
    /// Score-based stars (match-3, combo puzzles).
    /// 3 stars = hit target score
    /// 2 stars = hit 60% of target
    /// 1 star = completed (any score)
    /// </summary>
    public static int CalculateByScore(int score, int targetScore)
    {
        if (score >= targetScore) return 3;
        if (score >= (int)(targetScore * 0.6f)) return 2;
        return 1;
    }

    /// <summary>
    /// Time-based stars (time attack).
    /// 3 stars = under par time
    /// 2 stars = within 2x par time
    /// 1 star = completed
    /// </summary>
    public static int CalculateByTime(float timeTaken, float parTime)
    {
        if (timeTaken <= parTime) return 3;
        if (timeTaken <= parTime * 2f) return 2;
        return 1;
    }

    /// <summary>
    /// Composite stars (multiple criteria).
    /// Returns the minimum of individual ratings.
    /// </summary>
    public static int CalculateComposite(
        int movesTaken, int movePar,
        float timeTaken, float timePar,
        int hintsUsed)
    {
        int moveStars = CalculateByMoves(movesTaken, movePar);
        int timeStars = CalculateByTime(timeTaken, timePar);

        // Hint penalty: each hint reduces max possible stars by 1
        int maxStars = Math.Max(1, 3 - hintsUsed);

        return Math.Min(Math.Min(moveStars, timeStars), maxStars);
    }
}
```

### Victory Screen

```csharp
/// <summary>
/// Data for the puzzle completion screen.
/// Populated when PuzzleSession.IsComplete becomes true.
/// </summary>
public sealed class VictoryData
{
    public string LevelId { get; init; } = "";
    public string LevelName { get; init; } = "";
    public int MovesTaken { get; init; }
    public int Par { get; init; }
    public float TimeTaken { get; init; }
    public int Score { get; init; }
    public int Stars { get; init; }
    public int PreviousBestMoves { get; init; }
    public int PreviousStars { get; init; }
    public bool IsNewBest { get; init; }
    public bool IsNewStarRecord { get; init; }
    public string? NextLevelId { get; init; }
    public int TotalStarsEarned { get; init; }  // across all levels

    /// <summary>Build victory data from session state.</summary>
    public static VictoryData Build(
        PuzzleSession session,
        LevelData level,
        PuzzleProgressData progress,
        LevelManifest manifest)
    {
        var levelProgress = progress.GetOrCreate(level.Id);
        int stars = StarRating.CalculateByMoves(session.MoveCount, session.ParMoves);
        int score = PuzzleScoring.ScoreByMoves(session.MoveCount, session.ParMoves);

        return new VictoryData
        {
            LevelId = level.Id,
            LevelName = level.Name,
            MovesTaken = session.MoveCount,
            Par = session.ParMoves,
            TimeTaken = session.ElapsedTime,
            Score = score,
            Stars = stars,
            PreviousBestMoves = levelProgress.BestMoves,
            PreviousStars = levelProgress.Stars,
            IsNewBest = session.MoveCount < levelProgress.BestMoves,
            IsNewStarRecord = stars > levelProgress.Stars,
            NextLevelId = manifest.GetNextLevelId(level.Id),
            TotalStarsEarned = progress.TotalStars + Math.Max(0, stars - levelProgress.Stars)
        };
    }
}
```

---

## 9 — Puzzle Validation & Win Detection

### Generic Win Condition System

```csharp
/// <summary>
/// Interface for puzzle-specific win conditions.
/// Checked every frame after move execution.
/// </summary>
public interface IWinCondition
{
    /// <summary>Check if the puzzle is solved.</summary>
    bool IsSatisfied(PuzzleBoard board, PuzzleSession session);

    /// <summary>Progress toward completion (0.0 - 1.0). For UI progress bars.</summary>
    float Progress(PuzzleBoard board, PuzzleSession session);

    /// <summary>Human-readable description of what's needed.</summary>
    string Description { get; }
}

/// <summary>Sokoban: all boxes on goals.</summary>
public class AllBoxesOnGoals : IWinCondition
{
    public string Description => "Push all boxes onto goals";

    public bool IsSatisfied(PuzzleBoard board, PuzzleSession session)
    {
        // No loose boxes (type 2) remaining — all are box-on-goal (type 4)
        for (int i = 0; i < board.Cells.Length; i++)
            if (board.Cells[i] == 2) return false;
        return true;
    }

    public float Progress(PuzzleBoard board, PuzzleSession session)
    {
        int totalBoxes = board.Count(c => c == 2 || c == 4);
        int placedBoxes = board.Count(c => c == 4);
        return totalBoxes > 0 ? (float)placedBoxes / totalBoxes : 1f;
    }
}

/// <summary>Match-3: reach target score.</summary>
public class ReachTargetScore : IWinCondition
{
    private readonly int _targetScore;

    public ReachTargetScore(int targetScore) => _targetScore = targetScore;
    public string Description => $"Reach {_targetScore:N0} points";

    public bool IsSatisfied(PuzzleBoard board, PuzzleSession session)
        => PuzzleScoring.ScoreCascade(0, 0) >= _targetScore; // simplified

    public float Progress(PuzzleBoard board, PuzzleSession session)
        => MathF.Min(1f, 0f / _targetScore); // simplified — use actual score tracking
}

/// <summary>Sliding puzzle: tiles in numerical order.</summary>
public class TilesInOrder : IWinCondition
{
    public string Description => "Arrange all tiles in order";

    public bool IsSatisfied(PuzzleBoard board, PuzzleSession session)
    {
        int expected = 1;
        for (int y = 0; y < board.Height; y++)
        {
            for (int x = 0; x < board.Width; x++)
            {
                if (y == board.Height - 1 && x == board.Width - 1)
                    return board.Get(x, y) == 0; // last cell is empty
                if (board.Get(x, y) != expected) return false;
                expected++;
            }
        }
        return true;
    }

    public float Progress(PuzzleBoard board, PuzzleSession session)
    {
        int total = board.Width * board.Height - 1;
        int correct = 0;
        int expected = 1;
        for (int y = 0; y < board.Height; y++)
            for (int x = 0; x < board.Width; x++)
            {
                if (y == board.Height - 1 && x == board.Width - 1) break;
                if (board.Get(x, y) == expected) correct++;
                expected++;
            }
        return (float)correct / total;
    }
}

/// <summary>Logic grid: all cells correctly filled.</summary>
public class AllCellsCorrect : IWinCondition
{
    private readonly int[] _solution;

    public AllCellsCorrect(int[] solution) => _solution = solution;
    public string Description => "Fill in all cells correctly";

    public bool IsSatisfied(PuzzleBoard board, PuzzleSession session)
        => board.Cells.AsSpan().SequenceEqual(_solution.AsSpan());

    public float Progress(PuzzleBoard board, PuzzleSession session)
    {
        int correct = 0;
        for (int i = 0; i < board.Cells.Length; i++)
            if (board.Cells[i] == _solution[i]) correct++;
        return (float)correct / board.Cells.Length;
    }
}

/// <summary>Composite: multiple conditions must all be satisfied.</summary>
public class AllConditions : IWinCondition
{
    private readonly IWinCondition[] _conditions;

    public AllConditions(params IWinCondition[] conditions) => _conditions = conditions;
    public string Description => string.Join(" AND ", _conditions.Select(c => c.Description));

    public bool IsSatisfied(PuzzleBoard board, PuzzleSession session)
        => _conditions.All(c => c.IsSatisfied(board, session));

    public float Progress(PuzzleBoard board, PuzzleSession session)
        => _conditions.Average(c => c.Progress(board, session));
}
```

### Win Check ECS System

```csharp
public partial class WinCheckSystem : BaseSystem<World, float>
{
    private readonly IWinCondition _winCondition;
    private readonly PuzzleProgressData _progressData;
    private readonly LevelManifest _manifest;
    private readonly LevelData _currentLevel;

    public event Action<VictoryData>? OnVictory;
    public event Action<FailReason>? OnFailure;

    public WinCheckSystem(
        World world,
        IWinCondition winCondition,
        PuzzleProgressData progressData,
        LevelManifest manifest,
        LevelData currentLevel) : base(world)
    {
        _winCondition = winCondition;
        _progressData = progressData;
        _manifest = manifest;
        _currentLevel = currentLevel;
    }

    public override void Update(in float deltaTime)
    {
        ref var session = ref World.Get<PuzzleSession>(World.Query<PuzzleSession>());
        if (session.IsComplete || session.IsBusy) return;

        // Update elapsed time
        session.ElapsedTime += deltaTime;

        // Check timer expiry
        ref var config = ref World.Get<PuzzleConfig>(World.Query<PuzzleConfig>());
        if (config.HasTimer && session.ElapsedTime >= config.TimeLimitSeconds)
        {
            session.IsComplete = true;
            OnFailure?.Invoke(FailReason.TimeExpired);
            return;
        }

        // Check win condition
        ref var board = ref World.Get<PuzzleBoard>(World.Query<PuzzleBoard>());
        if (_winCondition.IsSatisfied(board, session))
        {
            session.IsComplete = true;
            var victory = VictoryData.Build(session, _currentLevel, _progressData, _manifest);

            // Update persistent progress
            _progressData.RecordCompletion(
                _currentLevel.Id,
                session.MoveCount,
                session.ElapsedTime,
                victory.Stars);

            OnVictory?.Invoke(victory);
        }

        // Check failure conditions
        CheckFailures(ref board, ref session, ref config);
    }

    private void CheckFailures(ref PuzzleBoard board,
        ref PuzzleSession session, ref PuzzleConfig config)
    {
        // Process PuzzleFailed events
        var failQuery = new QueryDescription().WithAll<PuzzleFailed>();
        World.Query(failQuery, (Entity e, ref PuzzleFailed fail) =>
        {
            session.IsComplete = true;
            OnFailure?.Invoke(fail.Reason);
            World.Destroy(e);
        });
    }
}
```

### Deadlock Detection (Sokoban)

Sokoban has a special failure state: a box pushed into a corner or against a wall where it can never reach a goal. Detecting this saves players from wasting moves on an unsolvable state:

```csharp
/// <summary>
/// Detects deadlocked states in Sokoban puzzles.
/// A box is deadlocked if it can never reach any goal.
/// </summary>
public static class SokobanDeadlock
{
    /// <summary>
    /// Simple deadlock check: box in a corner with no adjacent goal.
    /// Catches ~80% of deadlocks. Fast (O(boxes)).
    /// </summary>
    public static bool HasCornerDeadlock(PuzzleBoard board)
    {
        for (int y = 0; y < board.Height; y++)
        {
            for (int x = 0; x < board.Width; x++)
            {
                if (board.Get(x, y) != 2) continue; // not a loose box

                // Check if box is in a corner (two adjacent walls at a right angle)
                bool wallUp = !board.InBounds(x, y - 1) || board.Get(x, y - 1) == 1;
                bool wallDown = !board.InBounds(x, y + 1) || board.Get(x, y + 1) == 1;
                bool wallLeft = !board.InBounds(x - 1, y) || board.Get(x - 1, y) == 1;
                bool wallRight = !board.InBounds(x + 1, y) || board.Get(x + 1, y) == 1;

                bool isCorner = (wallUp && wallLeft) || (wallUp && wallRight) ||
                                (wallDown && wallLeft) || (wallDown && wallRight);

                if (isCorner)
                {
                    // Corner deadlock — box stuck forever (no goal at this position)
                    // (box-on-goal = type 4, which was already filtered by the type 2 check)
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Wall deadlock check: box against a wall with no goal reachable along that wall.
    /// More thorough than corner check.
    /// </summary>
    public static bool HasWallDeadlock(PuzzleBoard board)
    {
        // Check horizontal walls (top and bottom edges, and internal wall lines)
        for (int y = 0; y < board.Height; y++)
        {
            for (int x = 0; x < board.Width; x++)
            {
                if (board.Get(x, y) != 2) continue;

                // Box against top wall
                bool wallAbove = !board.InBounds(x, y - 1) || board.Get(x, y - 1) == 1;
                if (wallAbove && !GoalReachableAlongWall(board, x, y, true))
                    return true;

                // Box against bottom wall
                bool wallBelow = !board.InBounds(x, y + 1) || board.Get(x, y + 1) == 1;
                if (wallBelow && !GoalReachableAlongWall(board, x, y, true))
                    return true;

                // Box against left wall
                bool wallLeft = !board.InBounds(x - 1, y) || board.Get(x - 1, y) == 1;
                if (wallLeft && !GoalReachableAlongWall(board, x, y, false))
                    return true;

                // Box against right wall
                bool wallRight = !board.InBounds(x + 1, y) || board.Get(x + 1, y) == 1;
                if (wallRight && !GoalReachableAlongWall(board, x, y, false))
                    return true;
            }
        }
        return false;
    }

    private static bool GoalReachableAlongWall(
        PuzzleBoard board, int boxX, int boxY, bool horizontal)
    {
        // Check if any goal exists along the wall line
        if (horizontal)
        {
            for (int x = 0; x < board.Width; x++)
            {
                int cell = board.Get(x, boxY);
                if (cell == 3 || cell == 6) return true; // goal or player-on-goal
            }
        }
        else
        {
            for (int y = 0; y < board.Height; y++)
            {
                int cell = board.Get(boxX, y);
                if (cell == 3 || cell == 6) return true;
            }
        }
        return false;
    }
}
```

---

## 10 — Hint System

### Hint Provider

```csharp
/// <summary>
/// Multi-tier hint system. Goes from vague to specific.
/// Tier 1: General direction ("Try pushing the left box first")
/// Tier 2: Specific highlight (highlight the piece to move)
/// Tier 3: Show the exact move (arrow overlay)
/// </summary>
public class HintSystem
{
    private readonly LevelData _level;
    private readonly PuzzleHistory _history;
    private int _hintTier;

    public bool HintAvailable { get; private set; }
    public int HintsRemaining { get; private set; }

    public event Action<HintData>? OnHintRevealed;

    public HintSystem(LevelData level, PuzzleHistory history, int maxHints)
    {
        _level = level;
        _history = history;
        HintsRemaining = maxHints;
    }

    /// <summary>Request the next tier of hint. Each call reveals more.</summary>
    public HintData? RequestHint(PuzzleBoard currentBoard, PuzzleSession session)
    {
        if (HintsRemaining <= 0) return null;

        _hintTier++;
        HintsRemaining--;

        var hint = _hintTier switch
        {
            1 => GenerateTextHint(currentBoard),
            2 => GenerateHighlightHint(currentBoard),
            _ => GenerateMoveHint(currentBoard)
        };

        OnHintRevealed?.Invoke(hint);
        return hint;
    }

    /// <summary>Reset hint tier when player makes a move.</summary>
    public void OnMoveMade() => _hintTier = 0;

    private HintData GenerateTextHint(PuzzleBoard board)
    {
        // Use level metadata if available
        if (_level.Metadata.Hint != null)
        {
            return new HintData
            {
                Tier = HintTier.Text,
                Message = _level.Metadata.Hint,
            };
        }

        // Generic hint based on puzzle state analysis
        return new HintData
        {
            Tier = HintTier.Text,
            Message = AnalyzeForTextHint(board)
        };
    }

    private HintData GenerateHighlightHint(PuzzleBoard board)
    {
        // Find the piece that should move next
        var (x, y) = FindNextMovePiece(board);
        return new HintData
        {
            Tier = HintTier.Highlight,
            Message = "Move this piece",
            HighlightX = x,
            HighlightY = y
        };
    }

    private HintData GenerateMoveHint(PuzzleBoard board)
    {
        // Parse solution string if available
        if (_level.Metadata.Solution != null)
        {
            int moveIndex = _history.MoveCount;
            if (moveIndex < _level.Metadata.Solution.Length)
            {
                char dir = _level.Metadata.Solution[moveIndex];
                var (dx, dy) = DirectionFromChar(dir);
                var (px, py) = board.Find(5); // player position

                return new HintData
                {
                    Tier = HintTier.ExactMove,
                    Message = $"Move {DirectionName(dir)}",
                    HighlightX = px,
                    HighlightY = py,
                    ArrowDX = dx,
                    ArrowDY = dy
                };
            }
        }

        // Fallback: just highlight
        return GenerateHighlightHint(board);
    }

    private string AnalyzeForTextHint(PuzzleBoard board)
    {
        // Simple heuristics for Sokoban
        var goals = board.FindAll(3); // unfilled goals
        if (goals.Count == 1)
            return "Only one goal remaining — focus on it!";

        // Check for boxes near goals
        foreach (var (gx, gy) in goals)
        {
            Span<(int X, int Y, int Value)> neighbors = stackalloc (int, int, int)[4];
            board.GetNeighbors(gx, gy, neighbors, out int count);
            for (int i = 0; i < count; i++)
            {
                if (neighbors[i].Value == 2) // box adjacent to goal
                    return "There's a box very close to an open goal";
            }
        }

        return "Look for a box that can reach a goal without blocking others";
    }

    private (int X, int Y) FindNextMovePiece(PuzzleBoard board)
    {
        // Simple: return player position
        return board.Find(5);
    }

    private static (int DX, int DY) DirectionFromChar(char c) => c switch
    {
        'U' or 'u' => (0, -1),
        'D' or 'd' => (0, 1),
        'L' or 'l' => (-1, 0),
        'R' or 'r' => (1, 0),
        _ => (0, 0)
    };

    private static string DirectionName(char c) => c switch
    {
        'U' or 'u' => "up",
        'D' or 'd' => "down",
        'L' or 'l' => "left",
        'R' or 'r' => "right",
        _ => "?"
    };
}

public struct HintData
{
    public HintTier Tier;
    public string Message;
    public int HighlightX;
    public int HighlightY;
    public int ArrowDX;
    public int ArrowDY;
}

public enum HintTier
{
    Text,       // just a message
    Highlight,  // highlight a piece
    ExactMove   // show the exact move with arrow
}
```

---

## 11 — Replay & Ghost System

### Move Replay

Record and play back puzzle solutions. Used for:
- Post-victory replay
- Tutorial demonstrations
- Solution sharing
- Ghost comparison ("play against your best run")

```csharp
/// <summary>
/// Records and plays back puzzle move sequences.
/// </summary>
public class ReplaySystem
{
    private readonly List<ReplayFrame> _frames = new();
    private int _playbackIndex;
    private float _playbackTimer;
    private bool _isPlaying;

    public bool IsRecording { get; private set; }
    public bool IsPlaying => _isPlaying;
    public int FrameCount => _frames.Count;

    /// <summary>Start recording moves.</summary>
    public void StartRecording()
    {
        _frames.Clear();
        IsRecording = true;
    }

    /// <summary>Record a single frame (call after each move).</summary>
    public void RecordFrame(PuzzleBoard board, int playerX, int playerY, float timestamp)
    {
        if (!IsRecording) return;
        _frames.Add(new ReplayFrame
        {
            BoardSnapshot = (int[])board.Cells.Clone(),
            PlayerX = playerX,
            PlayerY = playerY,
            Timestamp = timestamp
        });
    }

    /// <summary>Stop recording and return the replay data.</summary>
    public ReplayData StopRecording()
    {
        IsRecording = false;
        return new ReplayData
        {
            Frames = _frames.ToArray(),
            TotalTime = _frames.Count > 0 ? _frames[^1].Timestamp : 0
        };
    }

    /// <summary>Load and start playback.</summary>
    public void StartPlayback(ReplayData data, float speedMultiplier = 1.0f)
    {
        _frames.Clear();
        _frames.AddRange(data.Frames);
        _playbackIndex = 0;
        _playbackTimer = 0;
        _isPlaying = true;
    }

    /// <summary>Advance playback. Returns current frame or null if finished.</summary>
    public ReplayFrame? UpdatePlayback(float deltaTime, float moveInterval = 0.5f)
    {
        if (!_isPlaying || _playbackIndex >= _frames.Count) return null;

        _playbackTimer += deltaTime;
        if (_playbackTimer >= moveInterval)
        {
            _playbackTimer -= moveInterval;
            var frame = _frames[_playbackIndex];
            _playbackIndex++;

            if (_playbackIndex >= _frames.Count)
                _isPlaying = false;

            return frame;
        }
        return null;
    }

    public void StopPlayback() => _isPlaying = false;
}

public struct ReplayFrame
{
    public int[] BoardSnapshot;
    public int PlayerX;
    public int PlayerY;
    public float Timestamp;
}

public sealed class ReplayData
{
    public ReplayFrame[] Frames { get; init; } = Array.Empty<ReplayFrame>();
    public float TotalTime { get; init; }

    /// <summary>Serialize to JSON for sharing/saving.</summary>
    public string Serialize()
        => JsonSerializer.Serialize(this);

    /// <summary>Deserialize from JSON.</summary>
    public static ReplayData? Deserialize(string json)
        => JsonSerializer.Deserialize<ReplayData>(json);
}
```

### Ghost Comparison

Show the player's best run as a transparent "ghost" while they play:

```csharp
/// <summary>
/// Renders a ghost of a previous playthrough alongside the current game.
/// Ghost advances one move for each move the player makes.
/// </summary>
public class GhostRenderer
{
    private readonly ReplayData _ghostData;
    private int _ghostMoveIndex;
    private readonly Color _ghostTint = new Color(255, 255, 255, 80); // semi-transparent

    public bool IsAhead { get; private set; }  // ghost is ahead of player
    public int GhostMoveCount => _ghostMoveIndex;

    public GhostRenderer(ReplayData bestRun)
    {
        _ghostData = bestRun;
    }

    /// <summary>Called when player makes a move. Advances ghost.</summary>
    public void OnPlayerMove(int playerMoveCount)
    {
        _ghostMoveIndex = Math.Min(playerMoveCount, _ghostData.Frames.Length - 1);
        IsAhead = _ghostMoveIndex < playerMoveCount;
    }

    /// <summary>Get the ghost's current board state for rendering.</summary>
    public int[]? GetGhostBoard()
    {
        if (_ghostMoveIndex < 0 || _ghostMoveIndex >= _ghostData.Frames.Length)
            return null;
        return _ghostData.Frames[_ghostMoveIndex].BoardSnapshot;
    }

    /// <summary>Draw ghost pieces (semi-transparent overlay).</summary>
    public void Draw(SpriteBatch spriteBatch, Texture2D spriteSheet,
        int tileSize, Vector2 boardOffset)
    {
        var board = GetGhostBoard();
        if (board == null) return;

        // Render ghost pieces with transparency
        // (implementation depends on your sprite system)
    }
}
```

---

## 12 — Timer & Time Pressure

### Puzzle Timer System

```csharp
/// <summary>
/// Manages puzzle timers: count-up (tracking), count-down (time limit),
/// and pause/resume.
/// </summary>
public partial class PuzzleTimerSystem : BaseSystem<World, float>
{
    private bool _isPaused;

    public event Action? OnTimeExpired;
    public event Action<float>? OnTimeWarning; // fires at 30s, 10s, 5s remaining

    private readonly float[] _warningThresholds = { 30f, 10f, 5f };
    private readonly HashSet<float> _firedWarnings = new();

    public PuzzleTimerSystem(World world) : base(world) { }

    public void Pause() => _isPaused = true;
    public void Resume() => _isPaused = false;

    public override void Update(in float deltaTime)
    {
        if (_isPaused) return;

        ref var session = ref World.Get<PuzzleSession>(World.Query<PuzzleSession>());
        if (session.IsComplete) return;

        session.ElapsedTime += deltaTime;

        ref var config = ref World.Get<PuzzleConfig>(World.Query<PuzzleConfig>());
        if (!config.HasTimer) return;

        float remaining = config.TimeLimitSeconds - session.ElapsedTime;

        // Fire warnings
        foreach (float threshold in _warningThresholds)
        {
            if (remaining <= threshold && !_firedWarnings.Contains(threshold))
            {
                _firedWarnings.Add(threshold);
                OnTimeWarning?.Invoke(remaining);
            }
        }

        // Time expired
        if (remaining <= 0)
        {
            session.IsComplete = true;
            World.Create(new PuzzleFailed { Reason = FailReason.TimeExpired });
            OnTimeExpired?.Invoke();
        }
    }

    public void Reset()
    {
        _firedWarnings.Clear();
        _isPaused = false;
    }
}
```

### Timer Display

```csharp
/// <summary>
/// Formats and renders the puzzle timer.
/// Changes color as time runs low: green → yellow → red.
/// </summary>
public static class TimerDisplay
{
    public static string Format(float seconds, bool showMilliseconds = false)
    {
        int min = (int)(seconds / 60);
        int sec = (int)(seconds % 60);
        if (showMilliseconds)
        {
            int ms = (int)((seconds % 1) * 100);
            return $"{min}:{sec:D2}.{ms:D2}";
        }
        return $"{min}:{sec:D2}";
    }

    public static Color GetTimerColor(float remaining, float total)
    {
        float ratio = remaining / total;
        if (ratio > 0.5f) return Color.LightGreen;
        if (ratio > 0.2f) return Color.Yellow;
        return Color.Red;
    }

    /// <summary>
    /// Pulse effect for low time. Returns scale multiplier.
    /// Pulses faster as time runs out.
    /// </summary>
    public static float GetPulseScale(float remaining, float gameTime)
    {
        if (remaining > 10f) return 1f;
        float pulseSpeed = remaining < 5f ? 8f : 4f;
        float pulse = MathF.Sin(gameTime * pulseSpeed) * 0.1f;
        return 1f + pulse;
    }
}
```

---

## 13 — Animated Piece Movement

Smooth piece animation is essential for puzzle games to feel good. Never teleport pieces — always lerp.

### Movement Animator

```csharp
/// <summary>
/// Smoothly animates ECS piece entities between grid positions.
/// Uses VisualOffset component — rendering uses GridPosition + VisualOffset.
/// </summary>
public partial class PieceAnimationSystem : BaseSystem<World, float>
{
    private const float MOVE_DURATION = 0.12f;   // seconds per tile move
    private const float FALL_DURATION = 0.06f;   // faster for gravity
    private const float SWAP_DURATION = 0.15f;   // match-3 swap

    public PieceAnimationSystem(World world) : base(world) { }

    public override void Update(in float deltaTime)
    {
        AnimateMoving(deltaTime);
        AnimateFalling(deltaTime);
        AnimateDestroying(deltaTime);
    }

    /// <summary>Animate pieces that have a visual offset (moving to new position).</summary>
    private void AnimateMoving(float dt)
    {
        var query = new QueryDescription().WithAll<PiecePosition, VisualOffset>()
            .WithNone<PendingDestroy>();

        World.Query(query, (Entity e, ref PiecePosition pos, ref VisualOffset offset) =>
        {
            if (MathF.Abs(offset.OffsetX) < 0.01f && MathF.Abs(offset.OffsetY) < 0.01f)
            {
                offset = new VisualOffset(0, 0);
                return;
            }

            // Lerp toward zero offset (piece slides to its grid position)
            float speed = 1f / MOVE_DURATION;
            float newX = MathHelper.Lerp(offset.OffsetX, 0, speed * dt);
            float newY = MathHelper.Lerp(offset.OffsetY, 0, speed * dt);

            // Snap when close
            if (MathF.Abs(newX) < 0.5f) newX = 0;
            if (MathF.Abs(newY) < 0.5f) newY = 0;

            offset = new VisualOffset(newX, newY);
        });
    }

    /// <summary>Animate falling pieces (match-3 gravity).</summary>
    private void AnimateFalling(float dt)
    {
        var query = new QueryDescription().WithAll<PiecePosition, Falling, VisualOffset>();
        World.Query(query, (Entity e, ref PiecePosition pos,
            ref Falling fall, ref VisualOffset offset) =>
        {
            fall.Velocity += 2000f * dt; // gravity acceleration (pixels/sec²)
            float newOffsetY = offset.OffsetY + fall.Velocity * dt;

            float targetOffset = 0; // falling toward grid position
            if (newOffsetY >= targetOffset)
            {
                // Landed
                offset = new VisualOffset(offset.OffsetX, 0);
                World.Remove<Falling>(e);

                // Bounce effect
                World.Add(e, new Bouncing(0.15f, 3f));
            }
            else
            {
                offset = new VisualOffset(offset.OffsetX, newOffsetY);
            }
        });
    }

    /// <summary>Scale-down animation for destroyed pieces (match-3 clears).</summary>
    private void AnimateDestroying(float dt)
    {
        var query = new QueryDescription().WithAll<PendingDestroy, Animating>();
        var toDestroy = new List<Entity>();

        World.Query(query, (Entity e, ref Animating anim) =>
        {
            anim.TimeRemaining -= dt;
            if (anim.TimeRemaining <= 0)
                toDestroy.Add(e);
        });

        foreach (var e in toDestroy) World.Destroy(e);
    }

    /// <summary>
    /// Initiate a piece move. Sets the visual offset so it animates from old → new position.
    /// Call AFTER updating PiecePosition to the new grid cell.
    /// </summary>
    public static void AnimateMove(World world, Entity piece,
        int fromX, int fromY, int toX, int toY, int tileSize)
    {
        // Set offset = old position - new position (in pixels)
        // The animation system will lerp this offset back to zero
        float offsetX = (fromX - toX) * tileSize;
        float offsetY = (fromY - toY) * tileSize;
        world.Set(piece, new VisualOffset(offsetX, offsetY));
    }
}

/// <summary>Landing bounce effect.</summary>
public record struct Bouncing(float TimeRemaining, float Intensity);
```

### Easing Functions

For non-linear animation (pieces that accelerate, decelerate, or bounce):

```csharp
/// <summary>
/// Common easing functions for puzzle piece animation.
/// t is always 0.0 (start) to 1.0 (end).
/// </summary>
public static class PuzzleEasing
{
    /// <summary>Smooth start (accelerates).</summary>
    public static float EaseIn(float t) => t * t;

    /// <summary>Smooth end (decelerates). Best for pieces landing.</summary>
    public static float EaseOut(float t) => 1f - (1f - t) * (1f - t);

    /// <summary>Smooth start and end. Best for swaps.</summary>
    public static float EaseInOut(float t)
        => t < 0.5f ? 2f * t * t : 1f - MathF.Pow(-2f * t + 2f, 2f) / 2f;

    /// <summary>Overshoot then settle. Best for piece snapping to grid.</summary>
    public static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * MathF.Pow(t - 1f, 3f) + c1 * MathF.Pow(t - 1f, 2f);
    }

    /// <summary>Bounce at the end. Best for falling pieces landing.</summary>
    public static float EaseOutBounce(float t)
    {
        const float n1 = 7.5625f;
        const float d1 = 2.75f;

        if (t < 1 / d1) return n1 * t * t;
        if (t < 2 / d1) return n1 * (t -= 1.5f / d1) * t + 0.75f;
        if (t < 2.5 / d1) return n1 * (t -= 2.25f / d1) * t + 0.9375f;
        return n1 * (t -= 2.625f / d1) * t + 0.984375f;
    }

    /// <summary>Spring oscillation. Best for "wrong move" shake feedback.</summary>
    public static float EaseOutElastic(float t)
    {
        if (t is 0 or 1) return t;
        return MathF.Pow(2f, -10f * t) *
               MathF.Sin((t * 10f - 0.75f) * (2f * MathF.PI / 3f)) + 1f;
    }
}
```

---

## 14 — Puzzle Reset & Restart

### Reset Strategies

Different puzzles need different reset behaviors:

```csharp
/// <summary>
/// Handles puzzle reset/restart with different strategies.
/// </summary>
public class PuzzleResetManager
{
    private readonly World _world;
    private readonly LevelLoader _loader;
    private readonly PuzzleHistory _history;

    public event Action? OnResetStart;
    public event Action? OnResetComplete;

    public PuzzleResetManager(World world, LevelLoader loader, PuzzleHistory history)
    {
        _world = world;
        _loader = loader;
        _history = history;
    }

    /// <summary>Full restart: reload level from scratch, clear all history.</summary>
    public void FullRestart(string levelId)
    {
        OnResetStart?.Invoke();
        var level = _loader.Load(levelId);
        LevelSpawner.SpawnLevel(_world, level, _history);
        OnResetComplete?.Invoke();
    }

    /// <summary>Soft reset: jump to initial state but keep undo history.</summary>
    public void SoftReset()
    {
        OnResetStart?.Invoke();
        var initial = _history.Initial;
        ref var board = ref _world.Get<PuzzleBoard>(_world.Query<PuzzleBoard>());
        initial.RestoreTo(ref board, out int px, out int py);

        ref var session = ref _world.Get<PuzzleSession>(_world.Query<PuzzleSession>());
        session.MoveCount = 0;
        session.ElapsedTime = 0;
        session.IsComplete = false;
        session.IsBusy = false;
        // Note: don't reset HintsUsed or UndosUsed — they count across resets

        // Record this as a move in history (so they can undo the reset)
        _history.RecordMove(board, px, py, 0, "Reset puzzle");
        OnResetComplete?.Invoke();
    }

    /// <summary>Checkpoint reset: return to a marked position.</summary>
    public void ResetToCheckpoint(int checkpointMoveNumber)
    {
        OnResetStart?.Invoke();
        var snapshot = _history.JumpTo(checkpointMoveNumber);
        if (snapshot.HasValue)
        {
            ref var board = ref _world.Get<PuzzleBoard>(_world.Query<PuzzleBoard>());
            snapshot.Value.RestoreTo(ref board, out int px, out int py);

            ref var session = ref _world.Get<PuzzleSession>(_world.Query<PuzzleSession>());
            session.MoveCount = snapshot.Value.MoveNumber;
        }
        OnResetComplete?.Invoke();
    }

    /// <summary>Random restart: shuffle the board to a new solvable state.</summary>
    public void RandomRestart(Func<PuzzleBoard, bool> isSolvable, int maxAttempts = 1000)
    {
        OnResetStart?.Invoke();
        ref var board = ref _world.Get<PuzzleBoard>(_world.Query<PuzzleBoard>());
        var rng = new Random();

        // Fisher-Yates shuffle of movable pieces only
        var movableIndices = new List<int>();
        for (int i = 0; i < board.Cells.Length; i++)
        {
            int cell = board.Cells[i];
            if (cell != 0 && cell != 1) // not empty or wall
                movableIndices.Add(i);
        }

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            // Shuffle movable cells
            var shuffled = board.Clone();
            for (int i = movableIndices.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int idxA = movableIndices[i];
                int idxB = movableIndices[j];
                (shuffled.Cells[idxA], shuffled.Cells[idxB]) =
                    (shuffled.Cells[idxB], shuffled.Cells[idxA]);
            }

            if (isSolvable(shuffled))
            {
                Array.Copy(shuffled.Cells, board.Cells, board.Cells.Length);
                var (px, py) = board.Find(5);
                _history.RecordInitial(board, px, py);

                ref var session = ref _world.Get<PuzzleSession>(_world.Query<PuzzleSession>());
                session.MoveCount = 0;
                session.ElapsedTime = 0;
                session.IsComplete = false;
                break;
            }
        }
        OnResetComplete?.Invoke();
    }
}
```

---

## 15 — Genre: Sokoban / Push-Block

### Sokoban Move System

The classic: push boxes onto goals. One player, grid-based, no diagonal movement.

```csharp
/// <summary>
/// Sokoban movement rules:
/// - Player moves in 4 directions (UDLR)
/// - Player can push ONE box at a time
/// - Boxes cannot be pulled
/// - Win when all boxes are on goals
/// </summary>
public partial class SokobanMoveSystem : BaseSystem<World, float>
{
    private readonly PuzzleHistory _history;
    private readonly MoveTrackingSystem _moveTracker;

    // Cell types
    private const int EMPTY = 0;
    private const int WALL = 1;
    private const int BOX = 2;
    private const int GOAL = 3;
    private const int BOX_ON_GOAL = 4;
    private const int PLAYER = 5;
    private const int PLAYER_ON_GOAL = 6;

    public SokobanMoveSystem(World world, PuzzleHistory history,
        MoveTrackingSystem moveTracker) : base(world)
    {
        _history = history;
        _moveTracker = moveTracker;
    }

    public override void Update(in float deltaTime)
    {
        var moveQuery = new QueryDescription().WithAll<MoveRequest>();
        World.Query(moveQuery, (Entity e, ref MoveRequest req) =>
        {
            ref var session = ref World.Get<PuzzleSession>(World.Query<PuzzleSession>());
            if (session.IsBusy || session.IsComplete)
            {
                World.Destroy(e);
                return;
            }

            ref var board = ref World.Get<PuzzleBoard>(World.Query<PuzzleBoard>());
            TryMove(ref board, ref session, req.DX, req.DY);
            World.Destroy(e);
        });
    }

    private void TryMove(ref PuzzleBoard board, ref PuzzleSession session, int dx, int dy)
    {
        // Find player
        var (px, py) = board.Find(PLAYER);
        if (px == -1)
            (px, py) = board.Find(PLAYER_ON_GOAL);
        if (px == -1) return; // no player!?

        int targetX = px + dx;
        int targetY = py + dy;

        if (!board.InBounds(targetX, targetY)) return;

        int targetCell = board.Get(targetX, targetY);
        string moveDesc;

        // Moving into empty space or goal
        if (targetCell == EMPTY || targetCell == GOAL)
        {
            // Simple move
            MovePlayer(ref board, px, py, targetX, targetY);
            moveDesc = $"Walk {DirectionName(dx, dy)}";
        }
        // Pushing a box
        else if (targetCell == BOX || targetCell == BOX_ON_GOAL)
        {
            int behindX = targetX + dx;
            int behindY = targetY + dy;

            if (!board.InBounds(behindX, behindY)) return;

            int behindCell = board.Get(behindX, behindY);

            // Can only push into empty or goal
            if (behindCell != EMPTY && behindCell != GOAL) return;

            // Push the box
            MoveBox(ref board, targetX, targetY, behindX, behindY);
            MovePlayer(ref board, px, py, targetX, targetY);
            moveDesc = $"Push box {DirectionName(dx, dy)}";
        }
        else
        {
            return; // wall or invalid
        }

        // Record move
        _moveTracker.RecordMove(ref board, ref session, px + dx, py + dy, moveDesc);

        // Check for deadlock after pushing
        if (SokobanDeadlock.HasCornerDeadlock(board))
        {
            // Don't auto-fail — just warn. Player might want to undo.
            // Visual feedback: flash deadlocked box red
        }
    }

    private void MovePlayer(ref PuzzleBoard board, int fromX, int fromY, int toX, int toY)
    {
        // Remove player from old cell (restore what was under them)
        int oldCell = board.Get(fromX, fromY);
        board.Set(fromX, fromY, oldCell == PLAYER_ON_GOAL ? GOAL : EMPTY);

        // Place player on new cell
        int newCell = board.Get(toX, toY);
        board.Set(toX, toY, newCell == GOAL ? PLAYER_ON_GOAL : PLAYER);
    }

    private void MoveBox(ref PuzzleBoard board, int fromX, int fromY, int toX, int toY)
    {
        // Remove box from old cell
        int oldCell = board.Get(fromX, fromY);
        board.Set(fromX, fromY, oldCell == BOX_ON_GOAL ? GOAL : EMPTY);

        // Place box on new cell
        int newCell = board.Get(toX, toY);
        board.Set(toX, toY, newCell == GOAL ? BOX_ON_GOAL : BOX);
    }

    private static string DirectionName(int dx, int dy) => (dx, dy) switch
    {
        (0, -1) => "up",
        (0, 1) => "down",
        (-1, 0) => "left",
        (1, 0) => "right",
        _ => "?"
    };
}

/// <summary>Directional move input.</summary>
public record struct MoveRequest(int DX, int DY);
```

### Sokoban Level Packs

The standard Sokoban level format is widely available. Here's how to load classic level packs:

```csharp
/// <summary>
/// Loads classic Sokoban level pack files (.sok, .txt).
/// Format: multiple levels separated by blank lines.
/// </summary>
public static class SokobanPackLoader
{
    public static List<LevelData> LoadPack(string filePath, string packPrefix)
    {
        var levels = new List<LevelData>();
        var lines = File.ReadAllLines(filePath);
        var currentLevel = new List<string>();
        int levelNum = 1;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) && currentLevel.Count > 0)
            {
                // End of level
                var levelText = string.Join('\n', currentLevel);
                levels.Add(SokobanParser.Parse(
                    levelText,
                    $"{packPrefix}_level{levelNum}",
                    $"Level {levelNum}",
                    EstimatePar(currentLevel)));
                currentLevel.Clear();
                levelNum++;
            }
            else if (line.StartsWith(';'))
            {
                // Comment line — skip
            }
            else if (line.Any(c => "#@$.*+ ".Contains(c)))
            {
                currentLevel.Add(line);
            }
        }

        // Don't forget the last level
        if (currentLevel.Count > 0)
        {
            var levelText = string.Join('\n', currentLevel);
            levels.Add(SokobanParser.Parse(
                levelText,
                $"{packPrefix}_level{levelNum}",
                $"Level {levelNum}",
                EstimatePar(currentLevel)));
        }

        return levels;
    }

    /// <summary>Rough par estimate based on board size and box count.</summary>
    private static int EstimatePar(List<string> lines)
    {
        int boxes = lines.Sum(l => l.Count(c => c == '$' || c == '*'));
        int area = lines.Sum(l => l.Count(c => c != '#'));
        return Math.Max(boxes * 8, area / 2); // very rough heuristic
    }
}
```

---

## 16 — Genre: Match-3 / Tile-Matching

### Match-3 Core

Match-3 adds cascading complexity: matches → gravity → new matches → repeat.

```csharp
/// <summary>
/// Match-3 game logic: swap adjacent gems, detect matches, cascade.
/// Board values: 0=empty, 1-6=gem colors.
/// </summary>
public class Match3System
{
    private readonly Random _rng = new();
    private readonly int _colorCount;

    public event Action<List<(int X, int Y)>>? OnMatchFound;
    public event Action<int, int>? OnCascade; // cascadeDepth, gemsCleared
    public event Action? OnBoardSettled;

    public Match3System(int colorCount = 6)
    {
        _colorCount = colorCount;
    }

    /// <summary>
    /// Try to swap two adjacent gems. Returns true if the swap creates a match.
    /// If no match, swap back (invalid move).
    /// </summary>
    public bool TrySwap(ref PuzzleBoard board, int x1, int y1, int x2, int y2)
    {
        // Must be adjacent
        int dist = Math.Abs(x1 - x2) + Math.Abs(y1 - y2);
        if (dist != 1) return false;

        // Perform swap
        int temp = board.Get(x1, y1);
        board.Set(x1, y1, board.Get(x2, y2));
        board.Set(x2, y2, temp);

        // Check if swap creates a match
        var matches = FindAllMatches(board);
        if (matches.Count == 0)
        {
            // No match — swap back
            board.Set(x2, y2, board.Get(x1, y1));
            board.Set(x1, y1, temp);
            return false;
        }

        return true;
    }

    /// <summary>Find all matches of 3+ in a row/column.</summary>
    public List<List<(int X, int Y)>> FindAllMatches(PuzzleBoard board)
    {
        var matches = new List<List<(int X, int Y)>>();
        var matched = new HashSet<(int, int)>();

        // Horizontal matches
        for (int y = 0; y < board.Height; y++)
        {
            for (int x = 0; x < board.Width - 2; x++)
            {
                int color = board.Get(x, y);
                if (color == 0) continue;

                int runLength = 1;
                while (x + runLength < board.Width && board.Get(x + runLength, y) == color)
                    runLength++;

                if (runLength >= 3)
                {
                    var match = new List<(int, int)>();
                    for (int i = 0; i < runLength; i++)
                    {
                        match.Add((x + i, y));
                        matched.Add((x + i, y));
                    }
                    matches.Add(match);
                    x += runLength - 1; // skip past this match
                }
            }
        }

        // Vertical matches
        for (int x = 0; x < board.Width; x++)
        {
            for (int y = 0; y < board.Height - 2; y++)
            {
                int color = board.Get(x, y);
                if (color == 0) continue;

                int runLength = 1;
                while (y + runLength < board.Height && board.Get(x, y + runLength) == color)
                    runLength++;

                if (runLength >= 3)
                {
                    var match = new List<(int, int)>();
                    for (int i = 0; i < runLength; i++)
                    {
                        match.Add((x, y + i));
                        matched.Add((x, y + i));
                    }
                    matches.Add(match);
                    y += runLength - 1;
                }
            }
        }

        return matches;
    }

    /// <summary>
    /// Clear matched gems (set to 0) and return count cleared.
    /// </summary>
    public int ClearMatches(ref PuzzleBoard board, List<List<(int X, int Y)>> matches)
    {
        int cleared = 0;
        var allMatched = new HashSet<(int, int)>();
        foreach (var match in matches)
        {
            OnMatchFound?.Invoke(match);
            foreach (var (x, y) in match)
                allMatched.Add((x, y));
        }

        foreach (var (x, y) in allMatched)
        {
            board.Set(x, y, 0);
            cleared++;
        }

        return cleared;
    }

    /// <summary>
    /// Apply gravity: gems fall to fill empty spaces.
    /// Returns the list of moves for animation.
    /// </summary>
    public List<(int X, int FromY, int ToY)> ApplyGravity(ref PuzzleBoard board)
    {
        var moves = new List<(int, int, int)>();

        for (int x = 0; x < board.Width; x++)
        {
            int writeY = board.Height - 1; // bottom of column

            // Scan from bottom to top
            for (int readY = board.Height - 1; readY >= 0; readY--)
            {
                int cell = board.Get(x, readY);
                if (cell != 0)
                {
                    if (readY != writeY)
                    {
                        board.Set(x, writeY, cell);
                        board.Set(x, readY, 0);
                        moves.Add((x, readY, writeY));
                    }
                    writeY--;
                }
            }
        }

        return moves;
    }

    /// <summary>
    /// Fill empty spaces at top with new random gems.
    /// Returns spawned gems for animation.
    /// </summary>
    public List<(int X, int Y, int Color)> FillEmpty(ref PuzzleBoard board)
    {
        var spawned = new List<(int, int, int)>();

        for (int x = 0; x < board.Width; x++)
        {
            for (int y = 0; y < board.Height; y++)
            {
                if (board.Get(x, y) == 0)
                {
                    int color = _rng.Next(1, _colorCount + 1);
                    board.Set(x, y, color);
                    spawned.Add((x, y, color));
                }
            }
        }

        return spawned;
    }

    /// <summary>
    /// Run a full cascade: match → clear → gravity → fill → repeat.
    /// Returns total gems cleared and max cascade depth.
    /// Used for immediate resolution (no animation).
    /// For animated cascades, run each step with delays.
    /// </summary>
    public (int TotalCleared, int MaxDepth) RunCascade(ref PuzzleBoard board)
    {
        int totalCleared = 0;
        int depth = 0;

        while (true)
        {
            var matches = FindAllMatches(board);
            if (matches.Count == 0) break;

            int cleared = ClearMatches(ref board, matches);
            ApplyGravity(ref board);
            FillEmpty(ref board);

            totalCleared += cleared;
            depth++;
            OnCascade?.Invoke(depth, cleared);
        }

        OnBoardSettled?.Invoke();
        return (totalCleared, depth);
    }

    /// <summary>
    /// Generate a board with no initial matches.
    /// </summary>
    public PuzzleBoard GenerateBoard(int width, int height)
    {
        var board = new PuzzleBoard(width, height);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int color;
                int attempts = 0;
                do
                {
                    color = _rng.Next(1, _colorCount + 1);
                    board.Set(x, y, color);
                    attempts++;
                }
                while (attempts < 100 && WouldMatch(board, x, y));
            }
        }

        return board;
    }

    private bool WouldMatch(PuzzleBoard board, int x, int y)
    {
        int color = board.Get(x, y);

        // Check horizontal (left 2)
        if (x >= 2 && board.Get(x - 1, y) == color && board.Get(x - 2, y) == color)
            return true;

        // Check vertical (up 2)
        if (y >=