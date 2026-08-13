# E4 — Solo Project Management
> **Category:** Explanation · **Related:** [E5 AI Workflow](./E5_ai_workflow.md) · [E9 Solo Dev Playbook](./E9_solo_dev_playbook.md) · [R3 Project Structure](../R/R3_project_structure.md) · [E6 Game Design Fundamentals](./E6_game_design_fundamentals.md) · [E8 MonoGameStudio Post-Mortem](./E8_monogamestudio_postmortem.md)

---

## Vertical Slice Development

Build vertically, not horizontally. A vertical slice is a fully-polished, feature-complete thin cross-section — not a prototype with placeholders, but a small piece built to final quality across code, art, audio, and UI.

If you run out of time with 10 complete features and 10 unstarted, cutting is straightforward. With 20 half-finished features, everything breaks.

Each 1–2 week sprint should end with a playable build:
1. Pick 1–2 vertical slices
2. Break into tasks (code, art, audio, integration)
3. Execute
4. Playtest

**Build the Minimum Viable Game Loop first** — the absolute core mechanic stripped of UI, story, and polish. Validate it's fun before investing in anything else.

> **Deep dive:** [E9 Solo Dev Playbook](./E9_solo_dev_playbook.md) — 5-level goal hierarchy, Kanban vs sprints for solo dev, case studies from Stardew Valley/Balatro/Vampire Survivors

---

## Task Structure: Five Levels

Structure goals across five levels, from vision down to individual bugs:

**Design Pillars (3–5 statements)** define what makes the game unique. Every feature decision gets evaluated against these. If a new idea doesn't serve a pillar, it goes in FUTURE_IDEAS.md.

**Major Milestones** follow the arc: Prototype → Demo → Early Access → Full Release. Each milestone has concrete exit criteria.

**Feature Categories** group work into Core Mechanics, Content, UI/UX, Audio, Art, and Systems. Each feature is classified Must-Have, Should-Have, Could-Have, or Won't-Have (MoSCoW).

**Implementation Tasks** break features into actions completable in 1–4 hours. These flow through a Kanban board: Backlog → To Do → In Progress → Done.

**Bug Tracking** uses severity tiers: P0 game-breaking (fix immediately), P1 major functionality (fix this sprint), P2 minor (fix before next milestone), P3 cosmetic (fix in polish phase).

---

## Scope Management

Apply MoSCoW ruthlessly:
- **Must Have:** Core loop, win/lose conditions, basic UI
- **Should Have:** Sound effects, particles, tutorial
- **Could Have:** Achievements, extra content
- **Won't Have:** Multiplayer, level editor, mod support

Multiply every time estimate by 2–3×. Bug fixing typically consumes 30% of development time.

Write a design doc (DESIGN.md) with game pillars, target audience, core loop, and feature list with MoSCoW priorities. Cross out old decisions with dates when they change. This prevents scope creep by making every addition a conscious decision against a documented plan.

### The Polaris Framework

For prioritizing within scope, use three tiers:

- **Essentials** — without them the game loses its unique selling point
- **Baseline** — minimum for a complete, shippable game
- **Accessories** — not necessary to ship

Within each tier, prioritize: Core Mechanics → Content → Quality of Life → Polish. Reserve the last 20–30% of development time for pure polish and bug fixing. During that phase, **no new features are allowed**.

### Scope Creep Defenses

Scope creep is the universal project killer. Every successful indie game documented scope growing beyond initial plans. The developers who ship are the ones who manage growth deliberately:

- **Design pillars as filter** — every new idea must support a pillar or it's deferred
- **Hard deadlines with cuts, not delays** — make cuts to meet the deadline instead of pushing back for more content
- **A separate FUTURE_IDEAS.md** — when a cool idea strikes, write it down outside current scope. Acknowledged but deferred.
- **Intentional constraints** — limit color palette, level count, mechanic count. Toby Fox's deliberately simple pixel art let one person handle all of Undertale's visuals
- **AI amplifies scope creep** — when generating a new feature takes minutes, the temptation to add "just one more" is constant. Every AI-generated feature still needs testing, balancing, art, sound, UI, and bug fixing. See [E5](./E5_ai_workflow.md)

> **Deep dive:** [E9 Solo Dev Playbook](./E9_solo_dev_playbook.md) — scope creep as the universal killer, AI amplification risk, Polaris Framework for fix/polish phase, design pillars as filter

---

## Kanban for Solo Devs

Across developer communities, **Kanban combined with vertical slicing** is the dominant project management approach for solo developers. Its advantages over formal Agile/Scrum:

- Continuous flow — no sprint ceremonies to hold with yourself
- Visual progress tracking at a glance
- Natural WIP limits prevent context-switching overload
- No estimation rituals (which are unreliable for creative work)

Minimum viable board: **Backlog → To Do (max 3) → In Progress (max 2) → Done**. The WIP limits are the key — they force you to finish things before starting new ones.

---

## Tool Recommendations

The tool landscape spans zero-friction to feature-rich. The pattern most successful solo devs follow: **two complementary tools** — one for design documentation, one for task execution, plus version control.

| Tool | Strengths | Best For |
|------|-----------|----------|
| **Codecks** | Purpose-built for game dev, card-based (trading card aesthetic), doc cards, milestone tracking, Discord/Steam bug collection | Full project management if you want a dedicated tool |
| **Obsidian** | Local markdown files (version-controllable), Kanban plugin, Dataview dashboards, graph view for design connections | Knowledge base + design documentation |
| **GitHub Projects** | Built into your repo, Kanban boards, issue tracking, milestone grouping | Code-adjacent task tracking |
| **Plain text file** | Zero friction, lives in version control, no context switching | Solo devs who find tools add overhead |
| **Trello** | Quick visual boards, free tier | Simple Kanban if you want a GUI |

The "clever variant" from the Hacker News solo dev community: name your todo file `todo.diff` so the text editor color-codes lines starting with `+` (to add) and `-` (to remove).

**Warning:** Over-engineering the PM system is itself a form of procrastination. If you spend more time organizing tasks than completing them, simplify.

---

## Version Control Workflow

Use trunk-based Git with direct commits to main for day-to-day work. Create short-lived branches only for risky experiments you might abandon.

**Tag releases** with semantic versioning adapted for games:
- MAJOR: milestones (alpha → beta → release)
- MINOR: new features
- PATCH: bug fixes

**Set up Git LFS on day one** for binary assets. Use `git pull --rebase` instead of merge commits. Use `git rebase -i` to squash messy WIP commits.

**Commit hygiene:** Each commit should be one logical change. *"Add fire propagation system"* not *"work on stuff"*. This makes `git bisect` usable for finding regression sources.

---

## Technical Debt Management

Refactor code you're actively working in; leave stable code alone. The **Three Strikes Rule:** First duplication is fine, second you wince, third you refactor.

Allocate 10–20% of each sprint to tech debt. Use the **Strangler Fig pattern** for larger refactors: build the new system alongside the old, migrate callers gradually, delete the old.

Prototype code written to answer "is this fun?" should be thrown away. Prototype code written to solve a known problem cleanly can be kept.

---

## Build Automation

Set up GitHub Actions for automated builds:

```yaml
# .github/workflows/build.yml
name: Build
on: [push, pull_request]
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      - run: dotnet build -c Release
      - run: dotnet publish -c Release -o ./publish
      - uses: actions/upload-artifact@v4
        with:
          name: game-build
          path: ./publish
```

Add Butler for automated itch.io deployment on tagged releases.

---

## Documentation That Compounds

Solo game development commonly spans 2–5 years. You will forget why you made critical decisions unless you document them. Four elements form the minimum viable documentation system:

### Architecture Decision Records

Record significant choices in version control alongside the code:

```
# ADR-001: Use Arch ECS for all entities
Status: Accepted
Date: 2026-02-09
Context: Need entity management for both mass and unique entities...
Decision: Use Arch ECS exclusively, no separate EC system...
Consequences: One entity model, simpler architecture, no bridge code...
```

### Living Game Design Document (DESIGN.md)

Core concept, design pillars, main mechanics, target audience, art direction, feature list with priority tiers. Updated continuously, not written once and shelved.

### Weekly Development Notes

A brief private summary of what was accomplished, blockers, and next-week plans. Maintains continuity across sessions — especially critical when you return to a feature after weeks away.

### Public Devlog

Monthly or biweekly, serves triple duty: external accountability, community building, and forced reflection. Start only after you have a playable prototype — the worst devlogs are the ones where the first post is just text about how great the game will be.

### ECS-Specific Documentation

Document your component and system design decisions. When an AI generates a system six months from now, you need to know why MovementSystem handles collision checking rather than a separate CollisionSystem, or why HealthComponent stores max health as a float rather than an int. These micro-decisions compound, and without a record, AI-generated code will slowly erode architectural coherence.

Keep a CHANGELOG.md. Comment code with *why*, not *what*. Create a **CONTEXT.md** file in your project root for AI-assisted development → [E5](./E5_ai_workflow.md).

---

## Lessons from Successful Solo Developers

Studying Stardew Valley, Hollow Knight, Celeste, Undertale, Vampire Survivors, Balatro, Papers Please, and Brotato reveals patterns so consistent they form a template:

**Every hit started as a personal passion project with low expectations.** Eric Barone began Stardew Valley to practice C# and would have been satisfied selling 10,000 copies. LocalThunk started Balatro during three weeks of vacation time. Luca Galante wanted 100 itch.io players for Vampire Survivors. This absence of commercial pressure enables authentic creative work.

**None used formal project management tools.** Team Cherry barely used Trello. LocalThunk used no documented methodology. Barone worked solo without formal processes. What they used instead was obsessive single focus — LocalThunk describes redirecting every stray thought: "You can't do that right now, you're in the middle of something else."

**Strategic creative breaks prevent burnout.** LocalThunk deliberately stopped working in March 2022 when his drive faded, returning refreshed two months later. Barone took a month off in 2014 to make a small mobile game. The breaks prevented negative associations with the project.

**Almost none succeeded on their first game.** Barone had mostly unfinished prior projects. LocalThunk had been making games for ten years. Thomas Gervraud (Brotato) shipped three games before his hit. Scott Cawthon made roughly 70 games before Five Nights at Freddy's. The current project doesn't need to be the hit — it needs to be the one that teaches you enough for the next one.

**Stardew Valley was built on C#/XNA transitioning to MonoGame** — the exact tech stack in this toolkit. The framework is proven for commercial hits.

### The Critical Social Milestone

The biggest risk of solo development is not how you manage a todo list, but that you'll build the wrong thing because you waited to get feedback. Schedule regular "social milestones" — times to show someone your work. External feedback prevents tunnel vision.

---

## The Fix/Polish Phase

The transition from "building features" to "fixing and polishing" is one of the hardest for solo developers. Two principles:

1. **Set a hard feature-freeze date** — after this date, only bug fixes, performance, and polish. No exceptions, no "just one small thing."
2. **Triage everything** — P0 bugs are the only mandatory work. P1 bugs are strongly recommended. P2/P3 bugs are polish and can be cut if time runs short.

Polish has outsized impact on perceived quality. Allocate real time for it — it's not what you do "if there's time left."
