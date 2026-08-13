# E4 — Solo Project Management
> **Category:** Explanation · **Related:** [E5 AI Workflow](../ai-workflow/E5_ai_workflow.md) · [E9 Solo Dev Playbook](./E9_solo_dev_playbook.md) · [R3 Project Structure](../../monogame-arch/reference/R3_project_structure.md) · [E6 Game Design Fundamentals](../game-design/E6_game_design_fundamentals.md) · [E8 MonoGameStudio Post-Mortem](../../monogame-arch/architecture/E8_monogamestudio_postmortem.md)

---

## Table of Contents
1. [Vertical Slice Development](#vertical-slice-development)
2. [Task Structure: Five Levels](#task-structure-five-levels)
3. [Scope Management](#scope-management)
4. [Kanban for Solo Devs](#kanban-for-solo-devs)
5. [Risk Management & Pre-Mortem](#risk-management--pre-mortem)
6. [Energy Management & Burnout Prevention](#energy-management--burnout-prevention)
7. [Project Health Metrics](#project-health-metrics)
8. [The Pivot Decision](#the-pivot-decision)
9. [Financial Planning for Solo Dev](#financial-planning-for-solo-dev)
10. [Tool Recommendations](#tool-recommendations)
11. [Version Control Workflow](#version-control-workflow)
12. [Technical Debt Management](#technical-debt-management)
13. [Build Automation](#build-automation)
14. [Documentation That Compounds](#documentation-that-compounds)
15. [Lessons from Successful Solo Developers](#lessons-from-successful-solo-developers)
16. [The Fix/Polish Phase](#the-fixpolish-phase)
17. [Common Project Management Mistakes](#common-project-management-mistakes)

---

## Vertical Slice Development

Build vertically, not horizontally. A vertical slice is a fully-polished, feature-complete thin cross-section — not a prototype with placeholders, but a small piece built to final quality across code, art, audio, and UI.

If you run out of time with 10 complete features and 10 unstarted, cutting is straightforward. With 20 half-finished features, everything breaks.

Each 1–2 week sprint should end with a playable build:
1. Pick 1–2 vertical slices
2. Break into tasks (code, art, audio, integration)
3. Execute
4. Playtest

**Build the Minimum Viable Game Loop first** — the absolute core mechanic stripped of UI, story, and polish. Validate it's fun before investing in anything else. If the core loop isn't fun with programmer art and no sound, no amount of polish will save it.

### Vertical Slice Checklist

Before moving to the next slice, verify the current one meets these criteria:

- [ ] **Playable end-to-end** — a player can engage with the mechanic from start to finish
- [ ] **No placeholder blockers** — programmer art is fine, broken flows are not
- [ ] **Tested by someone else** — even one external playtest catches what you're blind to
- [ ] **Performance acceptable** — hitting target framerate on target hardware
- [ ] **Save/load compatible** — if the game has persistence, this slice integrates with it
- [ ] **Committed to version control** — with a meaningful commit message

### When Slicing Gets Hard

Some features resist vertical slicing — multiplayer netcode, procedural generation, save systems. These are **horizontal infrastructure** that multiple vertical slices depend on. The rule: build the minimum horizontal infrastructure needed for the *next* vertical slice, not for all theoretical future slices.

Example: Your game has 5 biomes. Don't build the full procedural generation pipeline for all 5. Build enough to generate one biome, then create a vertical slice of gameplay *in that biome*. Extend the generator only when the next slice requires a new biome.

> **Deep dive:** [E9 Solo Dev Playbook](./E9_solo_dev_playbook.md) — 5-level goal hierarchy, Kanban vs sprints for solo dev, case studies from Stardew Valley/Balatro/Vampire Survivors

---

## Task Structure: Five Levels

Structure goals across five levels, from vision down to individual bugs:

### Level 1: Design Pillars (3–5 statements)

Define what makes the game unique. Every feature decision gets evaluated against these. If a new idea doesn't serve a pillar, it goes in FUTURE_IDEAS.md.

Example pillars for a roguelike:
- "Every run teaches the player something new"
- "Risk vs reward in every decision"
- "5-minute runs, 500-hour mastery"

Pillars are not features — they're principles. "Has crafting" is a feature. "The world is alive and reacts to player choices" is a pillar. Revisit pillars quarterly; if they no longer excite you, the project may need a direction shift or a conscious recommitment.

### Level 2: Major Milestones

Follow the arc: **Prototype → Demo → Early Access → Full Release**. Each milestone has concrete exit criteria:

| Milestone | Exit Criteria | Typical Duration |
|-----------|--------------|-----------------|
| **Prototype** | Core loop playable, fun validated with 3+ external testers | 1–3 months |
| **Demo / Vertical Slice** | 15–30 min polished experience, art direction established, trailer-ready | 3–6 months |
| **Early Access** | 2–4 hours content, save/load, settings, crash-free on target platforms | 6–12 months |
| **Full Release** | All planned content, tutorial, accessibility, localization, platform compliance | 12–24 months |

Not every game needs all milestones. A jam game might go Prototype → Release. A commercial game might skip Early Access. Choose the milestones that match your distribution strategy.

### Level 3: Feature Categories

Group work into **Core Mechanics, Content, UI/UX, Audio, Art, and Systems**. Each feature is classified Must-Have, Should-Have, Could-Have, or Won't-Have (MoSCoW).

### Level 4: Implementation Tasks

Break features into actions completable in **1–4 hours**. These flow through a Kanban board: Backlog → To Do → In Progress → Done. If a task takes more than 4 hours, it's too big — split it.

### Level 5: Bug Tracking

Use severity tiers:
- **P0** — Game-breaking (crash, data loss, soft-lock). Fix immediately.
- **P1** — Major functionality broken. Fix this sprint.
- **P2** — Minor issues. Fix before next milestone.
- **P3** — Cosmetic. Fix in polish phase.

Track bugs in the same system as tasks. A separate bug database creates friction that means bugs don't get logged.

---

## Scope Management

### MoSCoW Prioritization

Apply MoSCoW ruthlessly:
- **Must Have:** Core loop, win/lose conditions, basic UI
- **Should Have:** Sound effects, particles, tutorial
- **Could Have:** Achievements, extra content
- **Won't Have:** Multiplayer, level editor, mod support

Multiply every time estimate by 2–3×. Bug fixing typically consumes 30% of development time. Integration between systems always takes longer than building either system individually.

### The Polaris Framework

For prioritizing within scope, use three tiers:

- **Essentials** — without them the game loses its unique selling point
- **Baseline** — minimum for a complete, shippable game
- **Accessories** — not necessary to ship

Within each tier, prioritize: Core Mechanics → Content → Quality of Life → Polish. Reserve the last 20–30% of development time for pure polish and bug fixing. During that phase, **no new features are allowed**.

### Living Game Design Document

Write a design doc (DESIGN.md) with game pillars, target audience, core loop, and feature list with MoSCoW priorities. Updated continuously, not written once and shelved. Cross out old decisions with strikethrough and dates when they change — this prevents scope creep by making every addition a conscious decision against a documented plan, and creates a decision history that explains *why* the game evolved the way it did.

### Scope Creep Defenses

Scope creep is the universal project killer. Every successful indie game documented scope growing beyond initial plans. The developers who ship are the ones who manage growth deliberately:

- **Design pillars as filter** — every new idea must support a pillar or it's deferred
- **Hard deadlines with cuts, not delays** — make cuts to meet the deadline instead of pushing back for more content
- **A separate FUTURE_IDEAS.md** — when a cool idea strikes, write it down outside current scope. Acknowledged but deferred
- **Intentional constraints** — limit color palette, level count, mechanic count. Toby Fox's deliberately simple pixel art let one person handle all of Undertale's visuals
- **The "weekend test"** — if you haven't thought about a proposed feature after a weekend away, it wasn't important
- **AI amplifies scope creep** — when generating a new feature takes minutes, the temptation to add "just one more" is constant. Every AI-generated feature still needs testing, balancing, art, sound, UI, and bug fixing. See [E5](../ai-workflow/E5_ai_workflow.md)

### The 80/20 Rule of Game Features

Players spend 80% of their time engaging with 20% of your features. Identify which features are in that 20% through early playtesting and double down on them. The other 80% of features should be functional but don't need the same level of polish. Most games that feel "polished" aren't polished everywhere — they're deeply polished in the places players spend the most time.

> **Deep dive:** [E9 Solo Dev Playbook](./E9_solo_dev_playbook.md) — scope creep as the universal killer, AI amplification risk, Polaris Framework for fix/polish phase, design pillars as filter

---

## Kanban for Solo Devs

Across developer communities, **Kanban combined with vertical slicing** is the dominant project management approach for solo developers. Its advantages over formal Agile/Scrum:

- Continuous flow — no sprint ceremonies to hold with yourself
- Visual progress tracking at a glance
- Natural WIP limits prevent context-switching overload
- No estimation rituals (which are unreliable for creative work)

### Minimum Viable Board

**Backlog → To Do (max 3) → In Progress (max 2) → Done**

The WIP limits are the key — they force you to finish things before starting new ones. Context-switching between tasks is the single biggest productivity killer for solo devs. Every task switch costs 15–30 minutes of "ramp-up" time to reload the mental model.

### Column Rules

| Column | Rules |
|--------|-------|
| **Backlog** | Unordered. Everything goes here first. Weekly review to promote items. |
| **To Do** | Max 3 items. Ordered by priority. These are commitments for this sprint/week. |
| **In Progress** | Max 2 items. One primary task + one "waiting" task (e.g., waiting for playtest feedback). |
| **Done** | Committed to version control. Move items here ONLY after commit. |

### When Kanban Breaks Down

Kanban struggles with long-horizon planning. You can see what you're doing *now* but not whether you're on track for a milestone 6 months away. Supplement with:

- **Monthly milestone check** — are you closer to the next milestone than last month?
- **Burndown tracking** — count remaining Must-Have features. Plot over time. If the line isn't trending down, something is wrong.
- **Quarterly scope review** — revisit the full feature list. Cut anything that hasn't moved in 3 months — if it hasn't been prioritized in a quarter, it's not important.

---

## Risk Management & Pre-Mortem

Most solo game projects fail not from bad code but from foreseeable risks that were never identified. A **pre-mortem** is a planning exercise where you assume the project has already failed, then work backward to identify what caused the failure.

### Running a Pre-Mortem

At the start of each major milestone:

1. **Assume failure.** Write: "It's [milestone date]. The project failed. Why?"
2. **List every reason you can think of.** Be honest. Common solo dev failure modes:
   - Burned out and lost motivation
   - Scope grew until shipping felt impossible
   - A critical technical assumption was wrong (e.g., "multiplayer will be easy")
   - Life event (job change, health, family) disrupted the schedule
   - Ran out of money before finishing
   - The core mechanic wasn't fun and I didn't pivot early enough
   - Never showed anyone the game; built the wrong thing
3. **Rank by likelihood × impact.** Focus on the top 3–5 risks.
4. **Create mitigations.** Each risk gets a concrete action:

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Burnout | High | Fatal | Schedule weekly rest days. Take 1 week off every 3 months. |
| Scope creep | High | High | Monthly scope review. Hard feature-freeze 3 months before release. |
| Core mechanic not fun | Medium | Fatal | External playtest within first 6 weeks. Kill or pivot if feedback is negative. |
| Technical blocker | Medium | High | Prototype risky systems first. Have a fallback design that avoids the risky system. |
| Funding runs out | Low–Medium | Fatal | Calculate runway. Set a "decision date" 3 months before money runs out. |

### Risk Register

Maintain a `RISKS.md` file alongside your design doc. Review it monthly. Add new risks as they emerge. Close risks that are no longer relevant. The act of reviewing forces you to confront uncomfortable truths instead of ignoring them.

### The "Bus Factor" for Solo Devs

Your bus factor is 1. If something happens to you, the project stops. Mitigations:
- **Document everything** — future-you after a 2-month break is effectively a different person
- **Automate builds and deploys** — don't rely on tribal knowledge of "run this script, then that one"
- **Keep credentials accessible** — store API keys, platform passwords, and account info somewhere recoverable
- **Back up off-machine** — git remote + cloud backup. Local disk failure shouldn't kill the project

---

## Energy Management & Burnout Prevention

Burnout is the #1 reason solo games never ship. Not bad code, not wrong tools, not insufficient talent — running out of motivation before running out of features. Managing energy is more important than managing time.

### The Energy Model

Not all development hours are equal. Creative tasks (design, art, writing) require different energy than systematic tasks (bug fixing, optimization, documentation). Match tasks to energy levels:

| Energy Level | Best Tasks | Worst Tasks |
|-------------|-----------|------------|
| **Peak** (first 2–3 hours) | Core mechanic design, difficult programming, creative art | Email, organization, bug triage |
| **Steady** (mid-session) | Feature implementation, content creation, level design | Novel problem-solving |
| **Low** (end of session) | Bug fixes, documentation, asset organization, playtesting | Anything requiring architectural decisions |

### Burnout Warning Signs

Catch these early — by the time you dread opening the project, recovery takes months:

- **Avoidance** — finding excuses not to work on the game (reorganizing tools, researching engines, "refactoring" code that works fine)
- **Diminishing sessions** — 4-hour sessions become 2-hour sessions become 30 minutes of staring at the screen
- **Feature tourism** — jumping between systems instead of finishing anything
- **Cynicism about the project** — "nobody will play this anyway"
- **Physical symptoms** — sleep disruption, persistent fatigue, tension headaches during work

### Prevention Strategies

**Protect non-game time.** If game dev consumes all your free time, resentment builds. Schedule explicit non-game-dev time that you defend as aggressively as your dev sessions.

**Celebrate completions, not just launches.** Solo devs go months or years without external validation. Create internal milestones that feel like wins: "Finished the combat system. That's a big deal." Mark Done items on the board. Review what you've accomplished monthly.

**Vary the work.** Alternating between code, art, sound, and design within a week prevents the "I've been staring at pathfinding for 3 weeks" feeling. Not all switching is bad — variety within a single project restores creative energy.

**Take real breaks.** Not "breaks where you watch game dev YouTube" — real breaks from game dev entirely. Weekly: one full day off. Monthly: one weekend off. Quarterly: one full week off. Every successful solo dev in documented post-mortems describes at least one extended break during development.

**Ship small things in between.** Game jams, tiny tools, blog posts. Completing something in 48 hours reminds you that you *can* finish things — which counters the "this will never be done" feeling that comes with multi-year projects.

**The "Tuesday Test."** If you consistently dread working on the game on a random Tuesday (not Monday, not after a bad day — a normal Tuesday), something needs to change. Either the current task, the project direction, or the schedule.

### Recovery from Burnout

If burnout has already set in:

1. **Stop completely** for at least 2 weeks. No commits, no design docs, no thinking about the game.
2. **Play games** — especially games in your genre. Remember why you wanted to make one.
3. **Return with the easiest possible task.** Not the hard problem you left off on. Something you can complete in 30 minutes. Rebuild the habit of completing things.
4. **Reassess scope.** Post-burnout is the best time to cut features because you have emotional distance from them.

---

## Project Health Metrics

Without metrics, "are we on track?" degrades into optimistic guessing. Track these monthly — they don't require tooling, just 15 minutes of honest assessment.

### Core Metrics

**1. Must-Have Feature Burndown**

Count remaining Must-Have features. Plot monthly. The line should trend down. If it's flat, you're adding scope as fast as you're completing it. If it's going up, you have a scope creep crisis.

```
Month  |  Must-Haves Remaining
  1    |  ████████████████ 24
  2    |  ████████████████ 24  ← danger: no progress or scope grew
  3    |  ██████████████ 21
  4    |  ██████████ 16
  5    |  ██████ 10
  6    |  ███ 5
```

**2. Playtest-to-Change Ratio**

After each playtest, count how many issues were found vs how many you actually fixed. Early on, this ratio is high (lots of issues, lots of fixes). As the game matures, it should decrease. If the ratio stays high milestone after milestone, either the game has fundamental design issues or your implementation quality isn't improving.

**3. Session Consistency**

Track how many dev sessions you completed this month vs how many you planned. A healthy project runs at 70–80% session completion. Below 50% consistently means the schedule doesn't match your life, and you should adjust the plan rather than guilt-trip yourself. Above 90% consistently and you might be in crunch mode — check for burnout signs.

**4. Time-to-Fun**

For new players, how long from "start game" to "first fun moment"? Track this across playtests. It should decrease over development as you add tutorials, onboarding, and quality-of-life features. If it's increasing, you're adding complexity faster than you're adding clarity.

**5. Build Health**

- Does the game compile and run from a clean checkout? (Yes/No)
- Time since last successful clean build?
- Number of known P0/P1 bugs?

If any of these are unhealthy, stop adding features and fix them. A project that doesn't build cleanly is a project accumulating invisible technical debt.

### Monthly Health Check Template

```markdown
## Month [X] Health Check — [Date]

### Metrics
- Must-Haves remaining: [X] / [original total]
- Sessions completed: [X] / [planned]
- P0/P1 bugs open: [X]
- Last external playtest: [date]
- Time-to-fun (last test): [X] minutes

### What went well
- [...]

### What's stuck
- [...]

### Biggest risk right now
- [...]

### Next month's focus
- [...]
```

---

## The Pivot Decision

Every solo developer will face the question: "Should I keep going or kill this project?" Making this decision well is arguably the most important project management skill.

### Signals It's Time to Consider Pivoting

- **External testers consistently don't find the core loop fun** — after multiple iterations. Not after one playtest, but after you've tried 3+ times to make it click and it hasn't.
- **You've been in "pre-production" for more than 6 months** — still exploring instead of building. Analysis paralysis disguised as planning.
- **The technical foundation keeps needing replacement** — if you've rewritten the core architecture twice and it still doesn't work, the design may be the problem, not the code.
- **You can't explain why someone would play your game** — if you struggle to articulate the value proposition in one sentence, players will struggle to understand it too.
- **Market conditions changed fundamentally** — a dominant competitor launched, the genre saturated, or the platform you're targeting is declining.

### Signals to Keep Going

- **Playtesters have fun but want more** — this is the best signal. The core works, you need content.
- **You're still excited about the core idea** — not about a specific feature or technical challenge, but about the fundamental player experience.
- **You've been making steady progress** — even slow progress is progress. The project that ships after 4 years beats the one abandoned at month 8.
- **You haven't shown it to anyone yet** — you can't make the pivot decision without external feedback. Get playtests before deciding.

### Types of Pivots

Not all pivots are project death:

| Pivot Type | What Changes | Example |
|-----------|-------------|---------|
| **Scope pivot** | Reduce to a smaller, shippable version | 20 levels → 8 levels, 5 mechanics → 3 |
| **Audience pivot** | Target different players | Hardcore → casual, PC → mobile |
| **Mechanic pivot** | Keep theme/art, change core gameplay | Tower defense → auto-battler using same assets |
| **Platform pivot** | Different distribution | Premium PC → free mobile with IAP |
| **Full pivot** | New project, carry forward lessons | This is the "kill" — but do a post-mortem first |

### The Kill Criteria

Before starting the project, write kill criteria in your design doc:

```markdown
## Kill Criteria
If ANY of these are true after [date], stop and reassess:
- [ ] 3+ external testers find the core loop boring after [X] iterations
- [ ] Prototype not playable after [X] months
- [ ] Monthly dev hours drop below [X] for 3 consecutive months
- [ ] Remaining runway drops below [X] months
```

Writing these when you're objective (project start) prevents emotional decision-making when you're invested (month 12).

### After the Decision

If you decide to stop:
1. **Write a post-mortem** — see [P15 Post-Mortem Template](./P15_postmortem_template.md). This is mandatory. Without it, you learn nothing.
2. **Archive, don't delete** — you might reuse assets, code patterns, or design ideas.
3. **Take a real break** — don't immediately start the next project. Process what happened.
4. **Extract reusable code** — libraries, shaders, systems that aren't game-specific. These compound across projects.

---

## Financial Planning for Solo Dev

Money is a risk factor, not a taboo. Running out of funding is the second most common reason solo games don't ship (after burnout). Plan for it explicitly.

### Calculate Your Runway

```
Monthly expenses (rent, food, insurance, tools, subscriptions)
+ Monthly savings target (don't go to $0)
+ Game-specific costs (assets, music, marketing, platform fees)
= Monthly burn rate

Available savings / Monthly burn rate = Runway in months
```

If your runway is shorter than your estimated development time (after multiplying by 2–3×), you need to either:
- Reduce scope to fit within runway
- Keep your day job and develop part-time
- Seek funding (publisher, grants, savings goal before starting)

### The Decision Date

Set a **decision date** 3–6 months before your runway ends. On that date, assess:
- Can the game ship in the remaining time? If yes, continue.
- Can Early Access generate enough revenue to extend the runway? If maybe, prepare for EA launch.
- Neither? Start job hunting while wrapping up what you can.

Making this decision at 3 months remaining is strategic. Making it at 0 months remaining is a crisis.

### Revenue Planning

Revenue planning for indie games is inherently uncertain, but frameworks help:

**Wishlists as a Signal:** On Steam, the median conversion rate from wishlist to first-week sale is ~10–15%. If you need 5,000 first-week sales to be sustainable, you need ~35,000–50,000 wishlists at launch. Track wishlists monthly after your Steam page goes live.

**The Long Tail:** Most successful indie games earn more revenue in months 2–12 than in month 1. Plan finances around the long tail, not launch-day revenue. Steam sales, content updates, and word-of-mouth compound over time.

**Platform Revenue Shares:**
| Platform | Revenue Share | Notes |
|----------|-------------|-------|
| Steam | 70/30 (→75/25 at $10M, →80/20 at $50M) | De facto standard |
| Epic | 88/12 | Better cut, smaller audience |
| itch.io | Developer sets the split | Best for free/pay-what-you-want |
| GOG | 70/30 | DRM-free audience, smaller |
| Console | 70/30 (typical) | Requires dev kit + certification costs |

**Don't Price at $0.** Unless your game is genuinely a portfolio piece, price it. Even $4.99 signals "this is a real product" and generates revenue for the long tail. Free games with IAP require a fundamentally different design approach — don't bolt it on.

### Costs Most Solo Devs Forget

- Music licensing or commissioning ($500–$5,000 for a soundtrack)
- Sound effects (free packs exist, but custom SFX are $200–$1,000)
- Localization ($0.05–0.12/word × 15,000–50,000 words × number of languages)
- Platform fees (Steam $100, Apple $99/yr, console dev kits $500–$2,500)
- Legal (EULA, privacy policy, GDPR compliance, business entity formation)
- Marketing budget (capsule art, trailer, press kits, booth at events)
- Taxes on revenue (set aside 25–30% immediately)

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
| **Notion** | Rich documents + databases + Kanban in one tool | Solo devs who want design docs and task management unified |
| **HackerPlan** | Game dev-specific PM tool with GDD integration | Teams that want a dedicated game dev PM platform |

The "clever variant" from the Hacker News solo dev community: name your todo file `todo.diff` so the text editor color-codes lines starting with `+` (to add) and `-` (to remove).

### Tool Selection Principles

- **Use what you'll actually use.** A perfect system you don't maintain is worse than a text file you update daily.
- **One source of truth.** Tasks live in ONE place. Not some in GitHub Issues, some in Trello, some in a notebook.
- **Version-controlled when possible.** Markdown files in your repo survive tool company shutdowns.
- **Separate creative tools from tracking tools.** Don't design your game in your task tracker or track tasks in your design doc.

**Warning:** Over-engineering the PM system is itself a form of procrastination. If you spend more time organizing tasks than completing them, simplify.

---

## Version Control Workflow

Use trunk-based Git with direct commits to main for day-to-day work. Create short-lived branches only for risky experiments you might abandon.

### Branching Strategy

```
main ──────●──────●──────●──────●──────●──── (daily work)
               \                  /
                ●────●────●────●          (risky experiment)
                  \
                   ●── (abandoned, deleted)
```

Branches live for hours to days, not weeks. If a branch survives longer than a week, either merge it or kill it.

### Tag Releases

Use semantic versioning adapted for games:
- **MAJOR:** milestones (alpha → beta → release)
- **MINOR:** new features or content drops
- **PATCH:** bug fixes

Example: `v0.1.0` (prototype), `v0.5.0` (demo), `v0.9.0` (beta), `v1.0.0` (release), `v1.1.0` (post-launch content update).

### Git LFS

**Set up Git LFS on day one** for binary assets (textures, audio, fonts, Aseprite files). Adding LFS to an existing repo with binary history is painful. Do it at project creation.

```bash
git lfs track "*.png" "*.wav" "*.ogg" "*.mp3" "*.aseprite" "*.psd" "*.ttf" "*.otf"
```

### Workflow Rules

- **`git pull --rebase`** instead of merge commits (cleaner history)
- **`git rebase -i`** to squash messy WIP commits before milestone tags
- **Commit hygiene:** Each commit is one logical change. *"Add fire propagation system"* not *"work on stuff"*. This makes `git bisect` usable for finding regression sources.
- **Commit every session.** Never leave uncommitted work overnight. The commit is the "save" button for your project.

---

## Technical Debt Management

Refactor code you're actively working in; leave stable code alone. The **Three Strikes Rule:** First duplication is fine, second you wince, third you refactor.

### When to Refactor

- **Before extending a system** — clean the area you're about to build on
- **After a playtest reveals design issues** — refactor to support the new direction
- **When you can't explain the code** — if you can't explain it, you can't maintain it

### When NOT to Refactor

- **Code that works and isn't being changed** — leave it alone
- **During the final polish phase** — refactoring introduces regressions
- **As procrastination** — if the refactor isn't blocking a feature, it's not urgent

### The Strangler Fig Pattern

For larger refactors: build the new system alongside the old, migrate callers gradually, delete the old. Never do a "big bang" rewrite of a system that's currently working — the risk of introducing regressions across the entire game is too high.

### Prototype vs Production Code

Prototype code written to answer "is this fun?" should be thrown away. Prototype code written to solve a known problem cleanly can be kept. Know which type you're writing before you start, and comment it accordingly:

```
// PROTOTYPE: Testing whether grapple-swing feels good.
// If yes, rewrite with proper state machine integration.
// If no, delete this file.
```

### Allocating Debt Time

Allocate 10–20% of each sprint to tech debt. Track debt items alongside feature tasks on the same board. A separate "tech debt backlog" that nobody looks at is useless. The debt items should compete with features for priority — because they do compete for your time whether you acknowledge it or not.

---

## Build Automation

Set up CI on day one. Catching build failures early is infinitely cheaper than debugging them later.

### GitHub Actions Template

```yaml
# .github/workflows/build.yml
name: Build
on: [push, pull_request]
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with:
          lfs: true
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

### Automated Deployment

Add Butler for automated itch.io deployment on tagged releases:

```yaml
# .github/workflows/deploy.yml
name: Deploy
on:
  push:
    tags: ['v*']
jobs:
  deploy:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with:
          lfs: true
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      - run: dotnet publish -c Release -r win-x64 --self-contained -o ./publish-win
      - run: dotnet publish -c Release -r linux-x64 --self-contained -o ./publish-linux
      - run: dotnet publish -c Release -r osx-arm64 --self-contained -o ./publish-mac
      - uses: manleydev/butler-publish-itchio-action@master
        env:
          BUTLER_CREDENTIALS: ${{ secrets.BUTLER_API_KEY }}
          CHANNEL: windows
          ITCH_GAME: your-game
          ITCH_USER: your-username
          PACKAGE: ./publish-win
```

### Build Verification Checklist

Your CI should catch, at minimum:
- [ ] Compilation succeeds
- [ ] No warnings treated as errors
- [ ] Content pipeline processes all assets
- [ ] Game launches and reaches the main menu (smoke test)
- [ ] Unit tests pass (if you have them)

---

## Documentation That Compounds

Solo game development commonly spans 2–5 years. You will forget why you made critical decisions unless you document them. Four elements form the minimum viable documentation system:

### Architecture Decision Records

Record significant choices in version control alongside the code:

```markdown
# ADR-001: Use Arch ECS for all entities
Status: Accepted
Date: 2026-02-09
Context: Need entity management for both mass and unique entities...
Decision: Use Arch ECS exclusively, no separate EC system...
Consequences: One entity model, simpler architecture, no bridge code...
```

An ADR takes 5 minutes to write and saves hours of "why did I do it this way?" six months later. Record decisions when you make them — you won't remember the context later.

### Living Game Design Document (DESIGN.md)

Core concept, design pillars, main mechanics, target audience, art direction, feature list with priority tiers. Updated continuously, not written once and shelved. Strikethrough outdated decisions with dates. This is the "constitution" of your game — all feature debates reference it.

### Weekly Development Notes

A brief private summary of what was accomplished, blockers, and next-week plans. Maintains continuity across sessions — especially critical when you return to a feature after weeks away. Three to five lines is enough. The act of summarizing forces reflection on whether you're making real progress.

### Public Devlog

Monthly or biweekly, serves triple duty: external accountability, community building, and forced reflection. Start only after you have a playable prototype — the worst devlogs are the ones where the first post is just text about how great the game will be. Screenshots and GIFs are mandatory; walls of text are skipped.

### ECS-Specific Documentation

Document your component and system design decisions. When an AI generates a system six months from now, you need to know why MovementSystem handles collision checking rather than a separate CollisionSystem, or why HealthComponent stores max health as a float rather than an int. These micro-decisions compound, and without a record, AI-generated code will slowly erode architectural coherence.

### Documentation Minimum Viable Set

```
project-root/
├── DESIGN.md           # Game design document
├── CONTEXT.md          # AI context file → see E5
├── CHANGELOG.md        # What changed, when
├── README.md           # How to build and run
├── docs/
│   ├── adr/            # Architecture Decision Records
│   └── devlog/         # Development notes
└── FUTURE_IDEAS.md     # Parking lot for scope creep
```

Keep a CHANGELOG.md. Comment code with *why*, not *what*. Create a **CONTEXT.md** file in your project root for AI-assisted development → [E5](../ai-workflow/E5_ai_workflow.md).

---

## Lessons from Successful Solo Developers

Studying Stardew Valley, Hollow Knight, Celeste, Undertale, Vampire Survivors, Balatro, Papers Please, and Brotato reveals patterns so consistent they form a template:

### Pattern 1: Low Expectations Enable Great Work

**Every hit started as a personal passion project with low expectations.** Eric Barone began Stardew Valley to practice C# and would have been satisfied selling 10,000 copies. LocalThunk started Balatro during three weeks of vacation time. Luca Galante wanted 100 itch.io players for Vampire Survivors. This absence of commercial pressure enables authentic creative work that resonates with players.

### Pattern 2: Process Over Tools

**None used formal project management tools.** Team Cherry barely used Trello. LocalThunk used no documented methodology. Barone worked solo without formal processes. What they used instead was obsessive single focus — LocalThunk describes redirecting every stray thought: "You can't do that right now, you're in the middle of something else."

### Pattern 3: Strategic Breaks

**Strategic creative breaks prevent burnout.** LocalThunk deliberately stopped working in March 2022 when his drive faded, returning refreshed two months later. Barone took a month off in 2014 to make a small mobile game. The breaks prevented negative associations with the project. No successful solo developer describes shipping without at least one major break.

### Pattern 4: Failure Is Prerequisite

**Almost none succeeded on their first game.** Barone had mostly unfinished prior projects. LocalThunk had been making games for ten years. Thomas Gervraud (Brotato) shipped three games before his hit. Scott Cawthon made roughly 70 games before Five Nights at Freddy's. The current project doesn't need to be the hit — it needs to be the one that teaches you enough for the next one.

### Pattern 5: Known Tech Stack

**Stardew Valley was built on C#/XNA transitioning to MonoGame** — the exact tech stack in this toolkit. Balatro used LÖVE (Lua). Vampire Survivors used a custom engine. Celeste used C#/FNA. None used cutting-edge engines or frameworks. They used what they already knew. Learning a new engine AND making a game simultaneously is two projects, not one.

### The Critical Social Milestone

The biggest risk of solo development is not how you manage a todo list, but that you'll build the wrong thing because you waited to get feedback. Schedule regular "social milestones" — times to show someone your work. External feedback prevents tunnel vision.

Social milestone schedule:
- **Week 2:** Show the core mechanic to one person
- **Month 1:** Get 3 people to play the prototype
- **Month 3:** Playtest with 5+ people who don't know you
- **Each milestone:** Public demo, festival submission, or community post

---

## The Fix/Polish Phase

The transition from "building features" to "fixing and polishing" is one of the hardest for solo developers. Two principles:

1. **Set a hard feature-freeze date** — after this date, only bug fixes, performance, and polish. No exceptions, no "just one small thing."
2. **Triage everything** — P0 bugs are the only mandatory work. P1 bugs are strongly recommended. P2/P3 bugs are polish and can be cut if time runs short.

### What Polish Actually Means

Polish isn't a vague "make it feel better." It's specific, targetable work:

| Polish Category | Examples | Impact |
|----------------|---------|--------|
| **Game feel** | Screen shake, hitstop, particles on impact, camera effects | Transforms "functional" into "satisfying" |
| **Audio** | Footstep variation, UI click sounds, ambient loops, music transitions | Players notice bad audio immediately |
| **Juice** | Number popups, trail effects, squash/stretch, easing curves | Makes the game feel alive |
| **QoL** | Rebindable controls, colorblind options, subtitle sizing, auto-save | Prevents negative reviews |
| **Onboarding** | Tutorial prompts, first-time-user experience, difficulty curve | Determines whether players survive the first 5 minutes |
| **Performance** | Stable framerate, fast load times, no hitches | Below 60fps is the #1 complaint in negative Steam reviews |

### Polish Prioritization

Polish has outsized impact on perceived quality. Allocate real time for it — it's not what you do "if there's time left." Priority order:

1. **Performance** — nothing else matters if the game stutters
2. **Game feel on core mechanics** — the action players do most should feel best
3. **Audio** — surprisingly high impact for relatively low effort
4. **Onboarding** — every player experiences this, so ROI is 100%
5. **Quality of life** — prevents negative reviews
6. **Visual juice** — icing on the cake

---

## Common Project Management Mistakes

### 1. Planning the Whole Game Before Building

Writing a 40-page GDD before any code exists. The GDD will be wrong — game design is discovered through play, not documentation. Write enough to start building, then update the doc based on what you learn.

### 2. Tracking Time Instead of Output

"I worked 6 hours today" means nothing. "I finished the save system and it's committed" means everything. Track deliverables, not time spent. Some of your best work will happen in 90 minutes; some 8-hour days will produce nothing useful.

### 3. Ignoring the "Dark Middle"

Every project has a painful middle period where the initial excitement is gone, the finish line is invisible, and the work feels like a grind. This is normal. It's not a sign the project is bad. Push through with small, completable tasks and the finish line will eventually appear.

### 4. Comparing to Other Projects

"They shipped in 6 months, why can't I?" Because their 6 months was full-time with savings, and yours is evenings after work. Or because they had prior engine experience, and this is your first project. Or because survivorship bias means you only see the successes, not the 100 abandoned projects for every one that shipped.

### 5. Never Showing Anyone

The most insidious mistake because it feels productive. You're "not ready" to show people. You will never feel ready. Show people anyway. External feedback is the only way to know if you're building the right thing.

### 6. Sunk Cost Continuation

Continuing a project because you've "invested too much to stop" — not because you believe in it. The time is spent regardless. The question is: "Would I start this project today, knowing what I know now?" If no, stop. See [The Pivot Decision](#the-pivot-decision).

---

## Cross-References

- [P0 Master Playbook](./P0_master_playbook.md) — comprehensive project lifecycle from concept to post-launch
- [P1 Pre-Production](./P1_pre_production.md) — detailed pre-production planning and validation
- [P2 Production Milestones](./P2_production_milestones.md) — milestone definitions and exit criteria
- [P3 Daily Workflow](./P3_daily_workflow.md) — the daily development loop and session structure
- [P4 Playtesting](./P4_playtesting.md) — playtesting methodology, templates, and analysis
- [P7 Launch Checklist](./P7_launch_checklist.md) — pre-launch and launch-day checklist
- [P8 Pitfalls](./P8_pitfalls.md) — common development pitfalls and how to avoid them
- [P9 GDD Template](./P9_gdd_template.md) — game design document template
- [P11 Polish Checklist](./P11_polish_checklist.md) — comprehensive polish and QA checklist
- [P13 Release Pipeline](./P13_release_pipeline.md) — build, deploy, and release automation
- [P14 Marketing Timeline](./P14_marketing_timeline.md) — marketing schedule aligned with milestones
- [P15 Post-Mortem Template](./P15_postmortem_template.md) — structured post-mortem analysis
- [E5 AI Workflow](../ai-workflow/E5_ai_workflow.md) — AI-assisted development workflow
- [E9 Solo Dev Playbook](./E9_solo_dev_playbook.md) — deep dive on solo development patterns
