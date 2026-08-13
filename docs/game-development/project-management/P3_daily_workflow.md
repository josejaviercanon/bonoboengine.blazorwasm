# P3 — Daily Dev Workflow
> **Category:** Project Management · **Related:** [E4 Solo Project Management](./E4_project_management.md) · [E9 Solo Dev Playbook](./E9_solo_dev_playbook.md) · [E5 AI-Assisted Dev Workflow](../ai-workflow/E5_ai_workflow.md) · [P4 Playtesting](./P4_playtesting.md) · [P8 Common Pitfalls](./P8_pitfalls.md)

---

A practical daily routine for solo and small-team 2D game developers. Whether you have two evening hours or a full workday, a consistent workflow keeps you shipping instead of spinning.

---

## Table of Contents

1. [The Daily Development Loop](#1-the-daily-development-loop)
2. [Task Management](#2-task-management)
3. [Estimation & Time Tracking](#3-estimation--time-tracking)
4. [Commit Habits](#4-commit-habits)
5. [The Playtest Loop](#5-the-playtest-loop)
6. [Dev Journal](#6-dev-journal)
7. [AI-Integrated Workflow](#7-ai-integrated-workflow)
8. [Session Handoff & Context Preservation](#8-session-handoff--context-preservation)
9. [Multi-Discipline Day Planning](#9-multi-discipline-day-planning)
10. [The First Hour Problem](#10-the-first-hour-problem)
11. [Focus & Productivity](#11-focus--productivity)
12. [Distraction & Interruption Recovery](#12-distraction--interruption-recovery)
13. [Progress Visualization & Motivation](#13-progress-visualization--motivation)
14. [Weekly Review](#14-weekly-review)
15. [Avoiding Burnout](#15-avoiding-burnout)
16. [Debug Workflow](#16-debug-workflow)
17. [Build & Test Routine](#17-build--test-routine)
18. [Quick Reference — Session Checklists](#quick-reference--session-checklists)
19. [Common Mistakes](#18-common-mistakes)
20. [Tuning Reference](#tuning-reference)

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

### The Pipeline Principle

The loop isn't just a routine — it's a pipeline. Each step produces output that the next step consumes:

```
Review    → produces: context (what's in progress, what failed yesterday)
Plan      → consumes: context → produces: today's target
Build     → consumes: target → produces: working feature/content
Playtest  → consumes: feature → produces: feedback & bug list
Commit    → consumes: working code → produces: checkpoint
Journal   → consumes: everything → produces: tomorrow's context
```

When you skip a step, you break the pipeline. Skipping Review means you start without context and waste 30 minutes re-orienting. Skipping Journal means tomorrow's Review has nothing to work with. Skipping Playtest means you accumulate bugs invisibly.

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

### Weekend Sprint (5–6 hours, compressed)

For developers who have a day job and game dev on weekends:

```
 9:00 AM  Review + Plan (10 min)
 9:10 AM  Deep work block 1 — hardest task (2.5 hr)
11:40 AM  Break (20 min — go outside)
12:00 PM  Deep work block 2 — secondary tasks (2 hr)
 2:00 PM  Playtest + bug fixes (45 min)
 2:45 PM  Commit + Journal + weekly review (30 min)
 3:15 PM  Done — enjoy the rest of your weekend
```

Weekend sprints succeed when you plan during the week. Spend 10 minutes on Thursday/Friday evening deciding exactly what you'll build Saturday morning. Arrive at the desk with zero decision fatigue.

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

### Task Decomposition Strategies

When a feature feels too big to estimate, decompose it using one of these methods:

**Vertical Slice**: Break the feature into thin end-to-end slices, each playable on its own.

```
"Add melee combat" →
  Slice 1: Swing animation plays on button press (no collision)
  Slice 2: Hitbox activates during swing frames → enemy takes damage
  Slice 3: Hit-stop + screen shake on contact
  Slice 4: Knockback + i-frames on enemy
  Slice 5: 3-hit combo chain with buffered input
```

Each slice is playable and demo-able. If you stop after slice 2, you have working combat. Slices 3–5 are polish.

**Component Decomposition**: Break the feature into independent systems.

```
"Add inventory" →
  Data: Inventory class, item definitions, stack/merge logic
  UI: Grid display, slot selection, item tooltip
  Integration: Pickup → inventory, inventory → equipment, save/load
```

Each component can be built and tested independently before integration.

**The 2-Hour Rule**: If you can't imagine finishing a task in 2 hours, it's too big. Keep splitting until every leaf task passes the 2-hour test.

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

### Task Priority Matrix

When you have 20 tasks and don't know which to pick, use the impact/effort matrix:

| | Low Effort | High Effort |
|---|---|---|
| **High Impact** | **Do first** — quick wins that move the game forward | **Schedule** — plan carefully, break down further |
| **Low Impact** | **Fill gaps** — do when you have 30 minutes between tasks | **Drop** — these are traps disguised as tasks |

Impact = "does this make the game more fun, complete, or stable?" If you can't answer yes, the task is low impact regardless of how interesting it seems.

---

## 3. Estimation & Time Tracking

### Why Solo Devs Need Estimation

You're not estimating for a manager — you're estimating for yourself. Good estimates prevent:
- **Overcommitting** in a session (starting 3 tasks, finishing 0)
- **Scope blindness** ("the save system will take a day" — it took two weeks)
- **Demo/release date surprises** ("how is it November already?")

### The Multiplier Method

Solo devs consistently underestimate by 2–3×. Use this until your calibration improves:

```
Gut estimate × 2.5 = realistic estimate
```

A task you think will take 2 hours → budget 5 hours. This isn't pessimism — it accounts for:
- Setup/teardown time you forget to count
- The bug you'll discover halfway through
- The design decision you'll need to make
- Testing and integration time

### Task Size Categories

Rather than precise hour estimates, categorize by session count:

| Size | Sessions | Description | Example |
|------|----------|-------------|---------|
| **XS** | < 1 | Under 30 min. Do it now. | Fix a typo, tweak a constant |
| **S** | 1 | One focused session (2–4h) | Add screen shake, new particle effect |
| **M** | 2–3 | Multiple sessions, one feature | Complete melee attack system |
| **L** | 4–7 | One week of sessions | Full inventory system (data + UI + save) |
| **XL** | 8+ | Multiple weeks — **break it down** | "Add multiplayer" — needs decomposition |

If a task is XL, it's not a task — it's a milestone. Break it into M and S tasks.

### Tracking Time

You don't need precision. Round to 30-minute blocks. The goal is patterns, not payroll:

```markdown
# Time Log — March 2026 (Week 2)
Mon: 2.5h (combat: hitbox + hit-stop)
Tue: 0h (day job crunch)
Wed: 1.5h (combat: knockback — harder than expected)
Thu: 3h (combat: 3-hit combo + VFX)
Fri: 0h
Sat: 5h (enemy AI: patrol + chase states)
Sun: 2h (playtest + bug fixes + journal)
Week total: 14h
```

After a month, you know your actual velocity: "I average 12–15 hours per week and complete about 8 S-tasks or 3 M-tasks." This transforms milestone planning from guesswork to math.

### Estimation Calibration

Keep a simple accuracy log:

```markdown
| Task | Estimated | Actual | Ratio |
|------|-----------|--------|-------|
| Melee hitbox | 2h | 3h | 1.5× |
| Hit-stop | 1h | 1h | 1.0× |
| Knockback | 2h | 5h | 2.5× |
| Combo chain | 3h | 4h | 1.3× |
```

Your average ratio is your personal multiplier. Most solo devs land at 1.5–2.5×. Physics, networking, and save systems tend to be 3×+. UI and polish tend to be closer to 1×.

### Common Estimation Traps

- **The "just" trap**: "I'll just add saving" — save systems touch every game system
- **The "similar" trap**: "I did pathfinding before, this is similar" — integration context differs
- **The "almost done" trap**: The last 10% of a feature takes 50% of the time (edge cases, polish, integration)
- **The art trap**: Art tasks feel fast but have hidden time in iteration and revision
- **The networking trap**: Anything involving networking takes 3× your estimate, always

---

## 4. Commit Habits

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

> 📘 For a deeper dive on version control setup and best practices, see [G44 — Version Control](../../monogame-arch/guides/G44_version_control.md).

---

## 5. The Playtest Loop

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

> 📘 For tools and techniques to measure and improve game feel, see [G30 — Game Feel & Tooling](../../monogame-arch/guides/G30_game_feel_tooling.md).

---

## 6. Dev Journal

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

## 7. AI-Integrated Workflow

AI tools change the daily loop — not by replacing steps, but by accelerating them and introducing new failure modes. This section covers how to integrate AI productively without losing architectural coherence.

### AI-Augmented Build Phase

During the Build step, AI is most effective for these patterns:

**Batch Clearing Papercut Tasks**: The highest-ROI AI use is clearing accumulated small tasks in a single session. AI handles boilerplate-heavy fixes that individually take 15–30 minutes but collectively steal weeks:

```
Session plan: "Clear 8 small combat bugs"
1. Prompt AI with bug description + relevant code context
2. Review generated fix — trace through the logic
3. Test in-game immediately
4. Commit each fix individually (separate commit = easy revert)
5. Repeat — typical throughput: 6–10 small fixes per session
```

**Scaffolding New Systems**: When starting a new system (inventory, dialogue, save/load), AI generates the initial structure while you focus on architecture decisions:

```
1. Write a 2–3 paragraph spec of what the system does
2. Feed the spec + your CONTEXT.md (project conventions, component patterns)
3. Generate the scaffold (data structures, interface, basic logic)
4. REVIEW EVERYTHING — trace through the generated code manually
5. Refine: the scaffold is a starting point, not a finished product
```

**Where AI Fails in Daily Workflow**:
- Architectural decisions ("should this be a component or a system?" — AI picks whatever seems simpler)
- Game feel tuning (AI can't tell if the jump feels floaty)
- Creative direction (AI generates plausible but generic solutions)
- Integration across systems (AI doesn't see your full codebase context)

### The AI Review Ritual

Every line of AI-generated code needs the same review you'd give a junior developer's PR:

1. **Read it** — don't skim. AI code looks correct at a glance but fails at edge cases
2. **Question assumptions** — does this match your architecture? Your naming conventions? Your performance budget?
3. **Test the unhappy path** — AI usually handles the success case well but ignores null states, empty collections, frame-edge conditions
4. **Check for "plausible but wrong" patterns** — AI confidently generates code using deprecated APIs, wrong library versions, or patterns from the wrong engine

A CodeRabbit study found AI-co-authored code contained **1.7× more issues** than human-only code and **3× more readability problems**. The review ritual is not optional.

### Context Files for AI Sessions

AI tools produce dramatically better output when given project context upfront. Maintain these files in your repo root:

**CONTEXT.md** — your project's conventions:
```markdown
## Architecture
- ECS with Arch v2.1.0 (query-iterate-transform pattern)
- Components are record structs, Systems are static classes
- No LINQ in per-frame code (use foreach + query)

## Naming
- Components: PascalCase nouns (Position, Health, DamageFlash)
- Systems: PascalCase verbs (MoveSystem, RenderSystem)
- One system per file, file name matches class name

## Current Focus
- Working on: melee combat system
- Key constraint: must work with existing hitbox/hurtbox pattern in G64
```

**RULES.md** — hard constraints AI must follow:
```markdown
- Target: .NET 8, C# 12, MonoGame 3.8.2
- Never use deprecated MonoGame.Extended APIs
- All physics through Aether.Physics2D fixture-level properties
- Integer health values only (no floating point HP)
- Maximum 16 active entities in object pool per type
```

Update these when your project evolves. Stale context files cause more damage than no context files. See → [E5 AI-Assisted Dev Workflow](../ai-workflow/E5_ai_workflow.md)

### AI-Assisted Playtest Analysis

After your daily playtest, AI can help systematize your observations:

1. Write raw playtest notes as usual
2. Feed the notes to AI: "Categorize these observations by system, prioritize by impact on player experience, and suggest which are bugs vs design issues"
3. AI produces a structured triage list — but YOU decide priorities based on design pillars

This doesn't replace your instincts — it structures them. Particularly useful when you accumulate 20+ playtest observations over a week and need to prioritize.

---

## 8. Session Handoff & Context Preservation

The hardest part of solo dev isn't any individual session — it's the gap between sessions. You forget where you were, why you made a decision, and what's half-finished. Session handoff systems preserve context across the gap.

### The Breadcrumb File

At the end of every session, write a `NEXT.md` (or append to your journal):

```markdown
# Where I Stopped — March 15, 2026

## Current State
- Melee combo chain: 2 of 3 hits working
- Hit 3 needs: longer wind-up animation, wider hitbox, knockback impulse
- Bug: hit 2 → hit 3 transition drops input if buffered during hit-stop

## Files I Was Editing
- src/systems/MeleeSystem.cs (line ~145, the combo state transition)
- assets/animations/player_attack3.json (needs 2 more frames)
- src/components/CombatState.cs (added ComboStep enum, untested)

## Decisions I Made (and why)
- Combo window is 300ms (longer felt sluggish, shorter dropped inputs)
- Hit 3 does 2× base damage (reward for completing the chain)

## What To Do Next
1. Add hit 3 wind-up frames (art task, ~30 min)
2. Wire hit 3 animation to MeleeSystem (code, ~1h)
3. Fix the buffered input bug (debug, unknown time)
4. Playtest full combo feel
```

This takes 5 minutes to write. It saves 30 minutes of re-orientation tomorrow.

### The "Leave a Failing Test" Trick

When stopping mid-feature, write a failing test for exactly where you need to pick up:

```csharp
[Test]
public void Hit3_ShouldApplyDoubleBaseDamage()
{
    // TODO: Implement hit 3 damage multiplier
    // Currently fails because hit 3 transition not wired
    Assert.Fail("Hit 3 not implemented yet — start here tomorrow");
}
```

When you sit down next session: run the tests, see the failure, and you're immediately in context.

### Long Gaps (1+ week away)

If you're taking a vacation or stepping away for a week or more:

1. **Record a 2-minute walkthrough video** — play the game while narrating what works, what's broken, and what's next. Future-you will thank past-you.
2. **Tag the commit**: `git tag -a pause-march-15 -m "Pre-vacation state: combat 75% done, see NEXT.md"`
3. **Write a longer status document** — not just "what's next" but "what's the current state of every system"

The longer the gap, the more context you need to restore. A week away needs breadcrumbs. A month away needs a full status report.

### Context for Part-Time Developers

If you only work on the game 2–3 evenings per week, the gap between sessions is your biggest enemy. Optimize for context restoration:

- **Always leave the project in a buildable state** — nothing kills motivation like coming back to a broken build
- **Keep your IDE state** — don't close tabs, don't clear breakpoints. You want to sit down and see exactly what you were looking at
- **Use your commute/downtime for planning** — by the time you sit at the keyboard, you should already know what to build. Zero decision-making time at the start of the session

---

## 9. Multi-Discipline Day Planning

Game dev spans code, art, audio, design, and writing — each requiring different mental energy. Mismatching discipline to energy level wastes hours.

### Energy-Discipline Mapping

| Energy Level | Best Discipline | Examples |
|---|---|---|
| **Peak** (fresh, alert) | Systems programming, architecture | New game systems, refactoring, networking |
| **High** (focused but not peak) | Gameplay code, level design | Implementing features, building levels |
| **Medium** (functional) | Art creation, UI work | Sprites, tilemaps, menu layouts |
| **Low** (tired but present) | Audio, polish, documentation | Sound effects, bug fixes, devlog writing |
| **Minimal** (almost done) | Organization, planning | Triage bugs, update task board, plan tomorrow |

### The Discipline Block Pattern

For full-day sessions, assign disciplines to time blocks:

```
Morning (peak):    Architecture / hard systems work
Late morning:      Gameplay implementation
After lunch (dip): Art or audio (creative, less cognitive load)
Afternoon:         Polish and bug fixes
End of day:        Playtest, journal, planning
```

**Never mix disciplines within a block.** Switching from code to art to code costs 15–20 minutes of mental context each time. Three switches in a 2-hour session means 45–60 minutes of wasted re-orientation.

### The "Creative Tuesday" Pattern

Some solo devs dedicate entire sessions to one discipline:

```
Monday:     Code (new features)
Tuesday:    Art (sprites, animations, tilemaps)
Wednesday:  Code (integration, bug fixes)
Thursday:   Audio + polish
Friday:     Playtest + planning + devlog
```

This eliminates all within-session switching. The cost: you can't immediately see how your code works with new art. The benefit: deeper focus on each discipline.

### Balancing Content vs Systems

A common solo dev trap: spending 80% of time on systems and 20% on content, when the game needs the reverse. Track your discipline balance monthly:

```markdown
# March Discipline Split
Code (systems):    45%  → Too high, target 30%
Code (gameplay):   20%  → Good
Art:               15%  → Low, target 25%
Level design:       5%  → Critically low
Audio:              5%  → Fine for now
Polish/bugs:       10%  → Good
```

If systems work exceeds 40% for two months straight, you're building an engine, not a game. Ask: "When did I last add content a player would notice?"

---

## 10. The First Hour Problem

The first hour of a dev session determines the next four. Most failed sessions die in the first 30 minutes — the developer sits down, opens the project, and... stalls.

### Why the First Hour Fails

- **Decision paralysis**: "What should I work on?" burns 20 minutes
- **Build failures**: The project doesn't compile because you left it mid-refactor
- **Context amnesia**: "Why did I change this file?" with no journal entry to explain
- **Perfectionism**: Spending 45 minutes refactoring code that works before touching today's task
- **Distraction vulnerability**: Email/social media/news during the vulnerable startup window

### The "Zero Friction Start" Protocol

Eliminate every decision from the first 10 minutes:

1. **Before the session** (previous evening or commute): Decide your ONE task. Write it on a sticky note or in NEXT.md.
2. **Minute 0–2**: Open project. Build. If it doesn't build, THAT is today's first task.
3. **Minute 2–5**: Read your journal/NEXT.md entry. Context restored.
4. **Minute 5–10**: Open the files listed in NEXT.md. Start typing.

The goal: keyboard-to-code in under 10 minutes. Everything before that is system startup, not productive work.

### Warm-Up Tasks

If you can't face the hard task immediately, start with a 15-minute warm-up:

- Fix a known minor bug (gets you into the codebase)
- Tweak a tuning constant and playtest the feel
- Write a test for existing behavior (forces you to read the code)
- Clean up TODOs in the file you'll be working in

Warm-ups work because they create momentum without requiring creative energy. Once you're moving, the hard task feels less imposing.

---

## 11. Focus & Productivity

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

---

## 12. Distraction & Interruption Recovery

Distractions are not just time lost — they destroy the mental model you've built in working memory. A 2-minute interruption costs 10–20 minutes of recovery.

### The Interrupt Journal

When interrupted, immediately jot down your current thought before responding:

```
// INTERRUPTED: was about to add the collision layer check after
// the raycast succeeds — need to mask against ENEMY_LAYER only
```

One sentence. Takes 5 seconds. Saves 15 minutes of reconstruction.

### The "Controlled Interruption" Pattern

Some interruptions are internal — you suddenly realize you need to fix something else, or think of a better approach mid-task. These are productive thoughts but dangerous context-switches.

**Don't act on them immediately.** Write them down and keep going:

```markdown
## Interruption Log — Today
- [ ] Realized knockback should respect wall collision (add to G3 integration)
- [ ] Sound effect missing for hit 3 — add to audio backlog
- [ ] The damage flash shader could use a curve instead of linear fade
```

Process the interruption log during your end-of-session planning, not mid-flow.

### Recovery Strategies by Interruption Length

| Interruption | Recovery Strategy |
|---|---|
| **< 30 seconds** (notification glance) | Ignore it. Look at your code. You'll reconnect in ~1 minute. |
| **1–5 minutes** (quick conversation) | Re-read your interrupt journal note. Scan the last 10 lines of code you wrote. |
| **5–30 minutes** (meeting, phone call) | Re-read your NEXT.md. Rebuild your mental model from scratch — try to explain what you're doing out loud. |
| **30+ minutes** (major interruption) | Treat it as a new session. Do the full Review → Plan startup. |

### Notification Discipline

Turn off everything during build blocks. Everything.

```
Acceptable during dev sessions:
  - Build/compile notifications from your IDE
  - Timer alerts (Pomodoro)
  - Emergency calls (configure Do Not Disturb exceptions)

Turn OFF:
  - Email
  - Chat (Discord, Slack, Teams)
  - Social media
  - News
  - Phone notifications (except emergency)
```

If you can't resist checking your phone, put it in another room. Willpower is finite and shouldn't be wasted on notification discipline.

---

## 13. Progress Visualization & Motivation

Solo dev projects often span 1–3 years. Without visible progress markers, motivation erodes because the endpoint feels permanently far away.

### The Progress Wall

Create a physical or digital board that makes progress visible:

**Screenshot Timeline**: Capture a screenshot every week and pin them in chronological order. After 3 months, the visual improvement is dramatic. This is your most powerful motivation tool during the mid-project slump.

**Feature Checklist** (visible, not buried in a tool):
```
CORE MECHANICS
  ✅ Movement & physics
  ✅ Melee combat (3-hit combo)
  ✅ Enemy AI (patrol + chase)
  🔲 Ranged combat
  🔲 Boss encounters
  🔲 Save/load system

CONTENT
  🔲 World 1 (5 levels)
  🔲 World 2 (5 levels)
  🔲 3 enemy types
  🔲 1 boss
```

Each checkbox flip is a dopamine hit. Design your checklist so you're flipping boxes regularly (weekly, not monthly).

### The "Percentage Complete" Trap

Don't track overall game completion percentage. It's demoralizing because:
- At 20%, you feel like you've barely started (even though core mechanics are done)
- At 50%, the remaining 50% is all content/polish, which takes longer than systems
- At 80%, you discover 100 new tasks, and the number drops back to 60%

Instead, track **milestone progress**: "Demo milestone: 7 of 10 tasks complete." Milestones end. Percentages drift.

### The Devlog as Motivation Engine

Public devlogs create external accountability that internal motivation can't match:

- **Weekly GIF/video** of something new — even small changes look impressive to outsiders
- **Monthly devlog post** — forces you to articulate what you built and why
- **Community response** — even 5 positive comments sustain motivation for weeks

The devlog compounds: after 6 months, you have a visual history of your game's evolution that's more convincing than any pitch deck.

### Milestone Celebrations

Define celebrations before you reach the milestone:

```
Prototype complete → Show 3 friends, get feedback
Demo build → Post on itch.io, share on socials
First outside tester → Buy yourself something nice
Steam page live → Take a day off to celebrate
```

The celebration doesn't have to be big — it has to be deliberate. Acknowledging progress prevents the "nothing is ever done" trap.

---

## 14. Weekly Review

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

## 15. Avoiding Burnout

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

## 16. Debug Workflow

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

> 📘 For detailed debugging setup and ImGui integration, see [G16 — Debugging](../../monogame-arch/guides/G16_debugging.md).

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

## 17. Build & Test Routine

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

> 📘 For profiling tools and optimization techniques, see [G33 — Profiling & Optimization](../../monogame-arch/guides/G33_profiling_optimization.md).
> 📘 For testing strategies and automation, see [G17 — Testing](../../monogame-arch/guides/G17_testing.md).

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

## 18. Common Mistakes

### ❌ Starting Without a Plan → ✅ Decide Before You Sit Down

The most common failure mode: open the project, look at the codebase, and drift toward whatever catches your eye. Thirty minutes later, you're refactoring something that works fine and haven't touched the feature that matters.

**Fix**: Write tomorrow's task during today's journal step. Decision fatigue is eliminated before the session starts.

### ❌ Skipping Playtesting → ✅ Play Every Session, Even 5 Minutes

"I'll test after I finish the feature." This delays feedback loops by days or weeks. Small bugs compound. The jump that feels slightly wrong on Day 1 is normal by Day 5, and players hate it on Day 30.

**Fix**: Playtest is non-negotiable. Set a timer for 5 minutes minimum. If you find nothing, great — confidence earned. If you find something, you caught it early.

### ❌ Heroic Marathon Sessions → ✅ Consistent Short Sessions

A 14-hour weekend marathon feels productive but produces lower-quality work in the last 6 hours and causes a 3-day recovery period. Three 3-hour sessions produce more and better work than one 14-hour session.

**Fix**: Cap sessions at 6 hours for full days, 4 hours for evening sessions. The 2-Day Rule (never skip more than 2 days) maintains momentum better than irregular marathons.

### ❌ Committing Only at the End → ✅ Commit When It Works

Waiting to commit means you can't revert to a known good state when something breaks. You also lose the ability to bisect bugs.

**Fix**: Commit every time something works. "This feature is halfway done" is a valid commit state as long as the project builds and runs.

### ❌ Mixing AI Tasks and Manual Tasks → ✅ Batch AI Work Separately

Context-switching between "write code yourself" and "prompt AI and review output" is more expensive than switching between two manual tasks because the cognitive mode is different (generative vs evaluative).

**Fix**: Batch AI-assisted work into dedicated blocks. "This hour is AI-assisted bug clearing. This hour is manual feature implementation."

### ❌ Perfecting Before Finishing → ✅ Make It Work, Make It Right, Make It Fast

Polish is the last step, not every step. Spending 2 hours perfecting a particle effect before the underlying system works means you'll have a beautiful particle effect on a feature you might cut.

**Fix**: Follow the classic sequence: (1) Make it work — functional, testable, ugly. (2) Make it right — clean code, good architecture. (3) Make it fast — optimize only what profiling shows is slow. Most solo dev projects never need step 3.

### ❌ Ignoring Physical Health → ✅ Ergonomics are Part of the Workflow

Wrist pain, eye strain, and back problems are occupational hazards, not badges of honor. A repetitive strain injury can end your development for months.

**Fix**: 20-20-20 rule (every 20 minutes, look at something 20 feet away for 20 seconds). Stand or stretch every hour. Wrist stretches before long sessions. A good chair pays for itself in avoided medical bills.

---

## Tuning Reference

### Session Duration by Context

| Context | Optimal Session | Max Session | Key Constraint |
|---|---|---|---|
| After day job (tired) | 1.5–2h | 3h | One task only. Don't drain tomorrow's energy. |
| Weekend morning (fresh) | 3–4h | 6h | Hard problem first, polish second. |
| Full-time indie | 5–6h productive | 8h total | Take real breaks. Lunch away from desk. |
| Game jam | 4–6h blocks with 1h gaps | 12h/day max | Sleep > crunch. Quality drops after hour 8. |

### Pomodoro Variants by Discipline

| Discipline | Work Block | Break | Notes |
|---|---|---|---|
| Systems programming | 45 min | 10 min | Context is expensive to rebuild |
| Gameplay code | 35 min | 7 min | Natural stopping points (test & iterate) |
| Pixel art / animation | 25 min | 5 min | Frequent stepping back improves visual judgment |
| Level design | 30 min | 5 min | Alternate between building and playtesting |
| Audio / SFX | 25 min | 5 min | Ear fatigue is real — rest your ears |
| Writing / design docs | 25 min | 5 min | Standard Pomodoro works fine |
| Bug fixing | 20 min | 5 min | Short cycles prevent rabbit holes |

### Common Task Duration Reference

| Task Type | Typical Duration | Multiplier for First Time |
|---|---|---|
| Tweak a constant / fix typo | 15 min | 1× |
| Add a particle effect | 30–60 min | 1.5× |
| Implement screen shake | 30–60 min | 1.5× |
| New enemy type (behavior + art) | 4–8h | 2× |
| Full inventory system | 15–25h | 2.5× |
| Save/load system | 10–20h | 3× |
| Basic multiplayer | 40–80h | 3× |
| Dialogue system with branching | 15–30h | 2× |
| Complete tilemap + editor | 20–40h | 2.5× |
| Full audio implementation | 10–20h | 2× |

These are estimates for a competent solo dev. Your first game will take 2–3× longer on everything. That's normal.

### Weekly Hours → Milestone Timeline

| Hours/Week | Demo (8 weeks content) | Early Access | Full Release |
|---|---|---|---|
| 5h (casual) | 16 weeks | 12–18 months | 2–3 years |
| 10h (part-time) | 8 weeks | 6–9 months | 12–18 months |
| 20h (serious hobby) | 4 weeks | 3–5 months | 6–12 months |
| 40h (full-time) | 2 weeks | 6–10 weeks | 3–6 months |

Assumes a small-scope 2D game (platformer, roguelike, or similar). Multiply by 2–3× for RPGs, open-world, or multiplayer games. These are optimistic estimates for experienced developers — add 50% for first-timers.

---

## Related Docs

- [E4 Solo Project Management](./E4_project_management.md) — broader PM framework with risk, burnout, metrics, and pivot decisions
- [E9 Solo Dev Playbook](./E9_solo_dev_playbook.md) — AI tools, scope management, and patterns from successful indie games
- [E5 AI-Assisted Dev Workflow](../ai-workflow/E5_ai_workflow.md) — deep dive on CONTEXT.md, AI prompting, and MCP integration
- [P0 Master Playbook](./P0_master_playbook.md) — complete project lifecycle from concept to post-launch
- [P1 Pre-Production](./P1_pre_production.md) — vertical slice, design pillars, and prototype planning
- [P4 Playtesting](./P4_playtesting.md) — structured playtesting with external testers
- [P8 Common Pitfalls](./P8_pitfalls.md) — the 20 most common solo dev mistakes
- [P11 Polish Checklist](./P11_polish_checklist.md) — comprehensive polish and pre-launch checklist
- [G16 Debugging](../../monogame-arch/guides/G16_debugging.md) — ImGui integration, debug overlays, logging
- [G17 Testing](../../monogame-arch/guides/G17_testing.md) — unit testing, integration testing, BenchmarkDotNet
- [G33 Profiling & Optimization](../../monogame-arch/guides/G33_profiling_optimization.md) — performance profiling workflow
- [G44 Version Control](../../monogame-arch/guides/G44_version_control.md) — Git strategies for game projects

---

*The best workflow is the one you actually follow. Start with the evening checklist. Add complexity only when the simple version stops working.*
