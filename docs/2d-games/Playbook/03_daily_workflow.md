# 07 — Daily Dev Workflow

A practical daily routine for solo and small-team 2D game developers. Whether you have two evening hours or a full workday, a consistent workflow keeps you shipping instead of spinning.

---

## 1. The Daily Development Loop

Every session follows the same six-step loop:

```
Review → Plan → Build → Playtest → Commit → Journal
```

### The Steps

| Step | What You Do | Time (short) | Time (full) |
|------|-------------|:------------:|:-----------:|
| **Review** | Read yesterday's journal entry. Check where you left off. | 5 min | 10 min |
| **Plan** | Pick ONE deliverable for this session. Write it down. | 5 min | 10 min |
| **Build** | Code, create art, compose audio — heads-down work. | 90 min | 5–6 hr |
| **Playtest** | Play the game. Every session. No exceptions. | 10 min | 30 min |
| **Commit** | Commit your work with a meaningful message. Push. | 5 min | 10 min |
| **Journal** | Write 3–5 lines about what happened. | 5 min | 10 min |

### Evening Session (2–4 hours)

```
7:00 PM  Review + Plan (10 min)
7:10 PM  Build (90–150 min)
8:40 PM  Playtest (10–15 min)
8:55 PM  Commit + Journal (10 min)
9:05 PM  Done — walk away clean
```

The key constraint: **one deliverable**. You don't have time to context-switch. Pick the most important thing, finish it, ship it.

### Full Day (8 hours)

```
 9:00 AM  Review + Plan (15 min)
 9:15 AM  Deep work block 1 (2 hr)
11:15 AM  Break (15 min)
11:30 AM  Deep work block 2 (90 min)
 1:00 PM  Lunch + step away (60 min)
 2:00 PM  Deep work block 3 (2 hr)
 4:00 PM  Playtest (30 min)
 4:30 PM  Bug fixes / polish from playtest (60 min)
 5:30 PM  Commit + Journal + prep tomorrow (30 min)
 6:00 PM  Done
```

Full days let you tackle 2–3 deliverables. Front-load hard problems in the morning. Save polish and fixes for the afternoon when focus fades.

---

## 2. Task Management

### Breaking Work Into Tasks

A good task is:

- **Small enough to finish in one session** (1–4 hours)
- **Specific enough to know when it's done** ("add wall-jump" not "improve movement")
- **Independent enough to commit on its own**

Bad task: "Work on combat system"
Good tasks:
- [ ] Implement basic melee attack hitbox (2h)
- [ ] Add hit-stop on enemy contact (1h)
- [ ] Create 3-frame slash animation (2h)
- [ ] Add screen shake on heavy attacks (30m)

### The "One Thing" Rule

Before each session, answer: **"If I could only finish one thing today, what would it be?"**

That's your task. Everything else is bonus. This prevents the trap of starting five things and finishing zero.

### Task Tracking

Pick one method and stick with it:

**Option A — TODO.md** (simplest)
```markdown
## In Progress
- [ ] Add wall-jump mechanic

## Up Next
- [ ] Wall-jump particles
- [ ] Coyote time on ledges

## Done (this week)
- [x] Ground movement polish
- [x] Dust particles on land
```

**Option B — GitHub Issues**
- One issue per task
- Use labels: `gameplay`, `art`, `audio`, `bug`, `polish`
- Milestones for major features or demo targets
- Close issues with commit references

**Option C — Kanban Board** (Trello, Notion, GitHub Projects)
Four columns:

```
Backlog → In Progress → Testing → Done
```

Rules:
- **Backlog**: Anything you might do. No limit.
- **In Progress**: MAX 2 items. If you want to start something new, finish or shelve what's there.
- **Testing**: Needs a playtest to verify it works and feels right.
- **Done**: Committed, pushed, and played.

### Task States

```
Backlog ──→ In Progress ──→ Testing ──→ Done
              │                │
              └── Blocked ─────┘
```

If a task is blocked, write down *why* and move to something else. Don't stare at it.

---

## 3. Commit Habits

### Commit Often

A good rule: **if you'd be upset losing this work, commit it.** At minimum, commit at the end of every session. Better: commit every time something works.

Small, frequent commits let you:
- Bisect bugs easily
- Revert cleanly
- See progress in the log

### Conventional Commit Messages

Use prefixes so your git log tells a story:

```
feat:     New feature or mechanic          feat: add wall-jump with coyote time
fix:      Bug fix                          fix: player falls through one-way platforms
refactor: Code restructure, no behavior    refactor: extract physics into component
art:      Sprite, tilemap, UI art          art: add 8-frame run cycle for player
audio:    Sound effects, music             audio: add footstep sounds on stone tiles
docs:     Documentation                    docs: update input mapping reference
perf:     Performance improvement          perf: batch sprite draw calls
test:     Adding or fixing tests           test: add collision edge-case tests
wip:      Work in progress (end of day)    wip: dash mechanic partially working
```

### Branching Strategy for Solo Dev

**Default: work on `main`.** Solo projects don't need complex branching. Commit to main, push often.

**Branch when:**
- You're experimenting with something risky (new physics system, renderer rewrite)
- You want to keep main in a playable state for demos or testers
- You're trying two different approaches to the same problem

```bash
# Risky experiment
git checkout -b experiment/new-physics
# ... work ...
# If it works:
git checkout main && git merge experiment/new-physics
# If it doesn't:
git checkout main && git branch -D experiment/new-physics
```

**Tag milestones:**
```bash
git tag -a v0.1-movement -m "Basic movement complete"
git tag -a v0.2-combat -m "Melee combat working"
git tag -a demo-1 -m "First playable demo"
```

> 📘 For a deeper dive on version control setup and best practices, see [G44 — Version Control](../G/G44_version_control.md).

---

## 4. The Playtest Loop

### Play Your Game Every Session

This is non-negotiable. Five minutes minimum. You are both developer and first playtester.

### What to Look For

Run through this mental checklist while you play:

- [ ] **Is it fun?** Be honest. Would you keep playing if you hadn't made this?
- [ ] **What feels off?** Jumps too floaty? Attacks too slow? Something "sticky"?
- [ ] **Does the new thing work?** Test what you just added specifically.
- [ ] **Any bugs?** Visual glitches, wrong collisions, broken transitions?
- [ ] **Performance?** Frame drops, hitches, loading delays?

### Write It Down

Keep a running section in your dev journal (or a `PLAYTEST_NOTES.md`):

```markdown
## Playtest — 2026-03-07
- Wall-jump feels good but the window is too tight — increase buffer to 150ms
- Landing on slopes still jitters slightly
- Enemy patrol feels too predictable — randomize wait times
- The new background parallax layer looks great at 0.3x speed
```

These notes become tomorrow's task list.

### The "Fresh Eyes" Trick

Every few days, **don't play for 24 hours**, then sit down and play from the start as if it's your first time. You'll notice things you've gone blind to:
- Confusing UI that you understand only because you built it
- Difficulty spikes you've adapted to
- Missing feedback you mentally fill in

This is the closest a solo dev gets to outside playtesting. Do it at least weekly.

> 📘 For tools and techniques to measure and improve game feel, see [G30 — Game Feel & Tooling](../G/G30_game_feel_tooling.md).

---

## 5. Dev Journal

### Why Bother

A dev journal:
- **Tracks progress** — when you feel like you're going nowhere, read last month's entries
- **Identifies patterns** — "I always get stuck on Tuesdays" or "art tasks take 2x my estimates"
- **Provides material** — devlogs, Steam updates, and Twitter posts write themselves
- **Clears your head** — writing what's stuck often reveals the solution

### Template

Create a `devlog/` folder. One file per entry (or per week, your call):

```markdown
# Dev Log — 2026-03-07 (Sat)

## What I Worked On
- Implemented wall-jump mechanic
- Added particle burst on wall contact

## What Went Well
- Wall-jump felt good on first try — the reference animation helped
- Particles add a lot of juice for very little code

## What's Stuck
- Can't get wall-slide speed to feel right — too fast looks broken,
  too slow feels unresponsive. Try variable speed tomorrow?

## Ideas
- Could reuse the wall-jump buffer system for ledge grabs later
- Need a dust-cloud particle for landing — reuse the jump one?

## Tomorrow
- Polish wall-slide speed
- Add coyote time for wall-jumps
- Playtest the full movement set together

## Time Spent
~2.5 hours
```

### Minimal Version

Don't have time for the full template? Three lines is enough:

```
2026-03-07: Added wall-jump. Feels good. Wall-slide speed still off. (2.5h)
```

Anything beats nothing. The habit matters more than the format.

---

## 6. Focus & Productivity

### Pomodoro for Game Dev

The classic 25/5 split works, but game dev often needs longer focus:

- **Code/systems**: 45 min work / 10 min break (context is expensive to rebuild)
- **Art/animation**: 25 min work / 5 min break (natural stopping points are frequent)
- **Audio**: 30 min work / 5 min break (ear fatigue is real)
- **Writing/design**: 25 min work / 5 min break

Use a timer. When it rings, *actually stop*. Stand up. Look at something far away. Your subconscious will keep working on the problem.

### Avoiding Rabbit Holes

Rabbit holes are the #1 killer of indie dev productivity. You sit down to add a jump and four hours later you're writing a custom particle system.

**The timer trick**: Before exploring anything tangential, set a 20-minute timer. When it rings, decide: is this worth more time, or should I get back to the task?

Typical rabbit holes:
- Premature optimization ("I should batch these draws" — do you have a perf problem? No? Move on.)
- Engine rewrites ("What if I restructured the whole ECS..." — finish the game first.)
- Tool building ("I'll just write a quick level editor..." — use Tiled. Ship the game.)
- Research spirals ("Let me watch 5 more GDC talks on this..." — you already know enough. Build it.)

### The 15-Minute Rule

Stuck on something? Set a 15-minute timer and try your hardest. If you're still stuck when it rings:

1. **Write down exactly what's wrong** (often this solves it)
2. **Move to a different task** — come back tomorrow with fresh eyes
3. **Ask for help** — post in a community, check docs, search Stack Overflow

Do NOT spend 3 hours staring at the same bug. That's not persistence, it's stubbornness.

### Context Switching Costs

Every time you switch between unrelated tasks, you lose 10–20 minutes rebuilding mental context. In a 2-hour session, one switch costs you 10–15% of your productive time.

**Mitigations:**
- Batch similar work (all art in one session, all code in another)
- Leave yourself a note about *exactly where you stopped* and what to do next
- Keep your editor/IDE state — don't close files between sessions
- The dev journal's "Tomorrow" section is your context-restore system

### Environment Tips

- **Music**: Instrumental only while coding. Lyrics compete with your language-processing brain. Video game soundtracks are perfect — they're literally designed for focus during interactive tasks.
- **Notifications**: Off. All of them. Two hours of deep work beats six hours of interrupted work.
- **Same time, same place**: Routine builds momentum. Your brain learns "it's 7 PM at the desk = game dev time."

---

## 7. Weekly Review

### End-of-Week Checklist

Do this every Sunday (or whenever your week ends). 20–30 minutes.

```markdown
# Weekly Review — Week of 2026-03-02

## What Got Done
- [x] Wall-jump mechanic
- [x] Wall-jump particles
- [x] Coyote time
- [ ] Ledge grab (pushed to next week)

## What Slipped & Why
- Ledge grab: underestimated animation complexity. Need to break this
  into smaller tasks.

## Scope Check
- Am I still on track for the demo milestone?
- Any features I should cut?
- Is the game getting more fun or just more complex?

## Playtest Summary
- Movement feels solid now. Ready to move to combat.
- Need to revisit camera in tight corridors — it jitters.

## Next Week's Priorities
1. Basic melee attack (Mon–Tue)
2. Enemy placeholder with health (Wed)
3. Hit reactions and knockback (Thu–Fri)
4. Playtest combat loop (Sat)

## Hours This Week
~14 hours (Mon 2h, Tue 2h, Wed 0h, Thu 3h, Fri 2h, Sat 3h, Sun 2h)
```

### Monthly Review Template

Once a month. 30–45 minutes. Zoom out.

```markdown
# Monthly Review — March 2026

## Major Milestones
- Completed core movement system
- Started combat prototype

## What Worked
- Short evening sessions stayed consistent
- Breaking tasks into 1–2h chunks prevented stalling

## What Didn't Work
- Spent too much time on particle polish — should've moved on sooner
- Skipped playtest sessions twice — noticed bugs piled up

## Scope & Direction
- Original plan: 10 enemy types → revising to 5. Quality > quantity.
- Demo target: still June. On track if combat wraps by end of April.

## Next Month's Goals
1. Complete melee combat loop
2. First enemy type with AI
3. One complete test room (movement + combat + enemy)

## Hours This Month: ~52 hours
```

---

## 8. Avoiding Burnout

### Warning Signs

Watch for these — they sneak up on solo devs:

- **Dreading your dev sessions** — the thing you love feels like a chore
- **Endless "refactoring"** — rewriting working code instead of making progress
- **Scope avalanche** — adding features to avoid finishing
- **Comparing yourself** to other devs constantly
- **Physical symptoms** — headaches, eye strain, wrist pain, poor sleep

### Prevention

**Take rest days.** At least one full day per week with zero game dev. Your brain needs downtime to consolidate what you've learned and generate new ideas.

**Celebrate small wins.** Finished the jump mechanic? That's worth a moment. Got particles working? Nice. Don't wait for "the game is done" to feel good. Mark milestones:
- Record a GIF of the new feature
- Post it somewhere (Twitter, Discord, your devlog)
- Tell someone what you built

**The "Ship Something" Hack.** When motivation drops, ship *anything*:
- A tiny demo to a friend
- A devlog post
- A GIF on social media
- A build to itch.io marked "prototype"

Shipping creates feedback. Feedback creates motivation.

**Switch disciplines for variety.** Tired of code? Spend a session on pixel art. Burned out on sprites? Write some music. Sick of everything? Write a devlog. Variety within the project prevents monotony.

**The 2-Day Rule.** Never skip more than 2 days in a row. Even 20 minutes of light work (organizing tasks, sketching ideas, reading docs) keeps the thread alive. Momentum is easier to maintain than to restart.

---

## 9. Debug Workflow

### The Cycle

```
Reproduce → Isolate → Fix → Verify
```

1. **Reproduce**: Can you make the bug happen reliably? What are the exact steps? If you can't reproduce it, add logging and move on.

2. **Isolate**: What's the smallest scenario that triggers the bug? Disable systems one by one. Comment out code. Use debug overlays to visualize state.

3. **Fix**: Change one thing at a time. If you change three things and the bug disappears, you don't know which one fixed it — and you might have introduced new problems.

4. **Verify**: Confirm the fix. Test edge cases. Play through the area. Check that you didn't break something else.

### Debug Overlays

Use ImGui (or your engine's equivalent) to build real-time debug panels:

- **Collision shapes** — render hitboxes, hurtboxes, ground checks
- **State machine** — display current state, transition history
- **Physics values** — velocity, acceleration, grounded flag
- **Frame data** — FPS, draw calls, entity count

Toggle these with a key (F1–F4 are common). Keep them available in every build, not just debug builds.

> 📘 For detailed debugging setup and ImGui integration, see [G16 — Debugging](../G/G16_debugging.md).

### Logging Strategy

Use log levels and categories:

```
[PHYS] Player velocity: (230, -450)
[COLL] Wall collision detected at (128, 64)
[AI]   Enemy state: CHASE → ATTACK
[ERR]  Tilemap layer "collision" not found!
```

- **Verbose/trace**: Frame-by-frame data. Off by default. Toggle for specific systems.
- **Info**: State transitions, significant events.
- **Warning**: Something unusual but not broken.
- **Error**: Something is broken. Always visible.

Write logs to a file as well as the console. When a tester reports a bug, the log file is your best friend.

### Common 2D Game Bugs — Quick Reference

| Symptom | Likely Cause | First Check |
|---------|-------------|-------------|
| Player falls through floor | Collision not detected at high speed | Enable CCD or cap velocity |
| Jittery movement | Fixed vs variable timestep mismatch | Check delta time usage |
| One-frame flicker | State/animation set then immediately overridden | Check update order |
| Input feels laggy | Processing input after physics/render | Move input poll to start of frame |
| Sprite gaps/lines | Floating point positions or texture filtering | Snap to pixel, use nearest filtering |

---

## 10. Build & Test Routine

### Regular Build Testing

Don't wait until "it's done" to test on target platforms. Platform-specific bugs multiply over time.

| Cadence | What to Test |
|---------|-------------|
| **Every session** | Run and playtest on your dev machine |
| **Weekly** | Clean build from scratch (catches missing assets, build config issues) |
| **Bi-weekly** | Test on each target platform (Windows/Mac/Linux, or web, or console devkits) |
| **Each milestone** | Full playthrough on all targets. Performance profile. |

### Clean Build Checklist

```bash
# Nuke build artifacts
rm -rf build/

# Rebuild from scratch
cmake --build . --clean-first  # or your engine's equivalent

# Run the game — does it boot? Does it crash?
./build/game

# Check for missing assets (watch for file-not-found errors in logs)
```

### Performance Check Cadence

Performance should never be a surprise. Check regularly:

- **Every session**: Glance at FPS counter. Is it smooth?
- **Weekly**: Check memory usage. Is it growing over time? (Memory leak.)
- **Per milestone**: Run the profiler. Where is time being spent?

Red flags:
- FPS drops in areas that used to run fine (regression)
- Memory usage that grows and never shrinks
- Load times getting longer as you add content
- GC pauses (if using a managed language)

> 📘 For profiling tools and optimization techniques, see [G33 — Profiling & Optimization](../G/G33_profiling_optimization.md).
> 📘 For testing strategies and automation, see [G17 — Testing](../G/G17_testing.md).

---

## Quick Reference — Session Checklists

### Evening Session (2–4h) — Checklist

```
□ Read yesterday's journal entry
□ Pick ONE task for tonight
□ Set up environment (music, notifications off)
□ Build (use Pomodoro if helpful)
□ Playtest for 5–10 minutes
□ Commit with a descriptive message
□ Write 3–5 journal lines
□ Note tomorrow's starting point
```

### Full Day (8h) — Checklist

```
□ Read yesterday's journal entry
□ Pick 2–3 tasks for today, prioritized
□ Morning: deep work (hardest task first)
□ Midday: secondary tasks or art/audio
□ Afternoon: playtest (15–30 min)
□ Fix issues from playtest
□ Commit all work
□ Write journal entry
□ Update task board
□ Plan tomorrow
```

### Weekly — Checklist

```
□ Review the week's journal entries
□ Update task board (clean up Done, re-prioritize Backlog)
□ Do a "fresh eyes" playtest
□ Write weekly review
□ Set next week's priorities
□ Clean build test
□ Commit/push everything
□ Back up the project (if not using cloud Git)
```

---

*The best workflow is the one you actually follow. Start with the evening checklist. Add complexity only when the simple version stops working.*
