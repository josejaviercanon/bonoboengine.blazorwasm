# 03 — Production Milestones

A milestone-based production guide for solo and small-team 2D game development using MonoGame + Arch ECS. Each milestone has a clear definition of done, actionable checklists, time estimates, and red flags to watch for.

---

## Table of Contents

1. [Milestone Overview](#milestone-overview)
2. [Prototype Phase](#1-prototype-phase)
3. [Vertical Slice](#2-vertical-slice)
4. [Alpha](#3-alpha)
5. [Beta](#4-beta)
6. [Release Candidate](#5-release-candidate)
7. [Gold / Launch](#6-gold--launch)
8. [Milestone Review Template](#milestone-review-template)
9. [Scope Adjustment Framework](#scope-adjustment-framework)

---

## Milestone Overview

Every game follows the same production arc, whether it takes three months or three years. The milestones don't change — the time you spend in each one does.

```
Prototype → Vertical Slice → Alpha → Beta → Release Candidate → Gold
```

### Timeline Estimates

| Phase | % of Total | Small (3-6 mo) | Medium (6-12 mo) | Large (12-24 mo) |
|---|---|---|---|---|
| Prototype | ~10% | 2 weeks | 3-4 weeks | 4-6 weeks |
| Vertical Slice | ~15% | 3-4 weeks | 6-8 weeks | 8-14 weeks |
| Alpha | ~30% | 4-8 weeks | 10-16 weeks | 16-30 weeks |
| Beta | ~25% | 3-6 weeks | 8-12 weeks | 12-24 weeks |
| Release Candidate | ~10% | 1-2 weeks | 3-4 weeks | 4-8 weeks |
| Gold / Launch | ~10% | 1-2 weeks | 2-4 weeks | 4-8 weeks |

**Small scope:** A tight, focused game. Think single-mechanic arcade, jam-style with polish. 5-15 levels or equivalent.  
**Medium scope:** A full indie game. Multiple mechanics, progression, narrative elements. 20-50 levels or equivalent.  
**Large scope:** An ambitious indie title. Complex systems, lots of content, maybe procedural generation. 50+ levels or equivalent.

> **Reality check:** Solo devs consistently underestimate by 2-3x. If your gut says 6 months, plan for 12. Build your schedule around the *pessimistic* column, celebrate if you hit the optimistic one.

---

## 1. Prototype Phase

**Duration:** 2-4 weeks  
**Goal:** Prove the game is fun. Nothing else matters.

### What You're Proving

- The core mechanic works and feels good
- The game is worth building (you'd play this)
- The technical approach is viable with MonoGame + Arch ECS
- The scope is achievable for your team size and timeline

### What You Are NOT Building

- Final art (use colored rectangles, free assets, placeholder sprites)
- Menus, settings, save/load
- Audio (maybe a few sound effects for feel, but no soundtrack)
- Multiple levels or content variety
- Polish of any kind

### Definition of Done

The prototype is done when you (or a trusted playtester) can play the core loop for 5-10 minutes and say "yeah, this is fun" or "no, this isn't working." Both answers are valuable.

### Checklist

- [ ] Core mechanic implemented and playable
- [ ] Player entity spawns and can be controlled
- [ ] At least one "level" or play space exists (can be hardcoded)
- [ ] Basic collision/physics working (if applicable)
- [ ] Core game loop runs: start → play → win/lose → restart
- [ ] Played by at least one person who isn't you
- [ ] Written down: what's fun, what isn't, what surprised you
- [ ] Scope document updated based on what you learned
- [ ] **Go/No-Go decision made** — are you building this game?

### Technical Notes

At this stage your Arch ECS setup should be minimal:

- A few core components (Position, Velocity, Sprite, Player)
- A few systems (Movement, Rendering, Input, basic Collision)
- No concern for architecture purity — this is throwaway code

See [Game Loop fundamentals](../../monogame-arch/guides/G15_game_loop.md) for structuring your update/draw cycle, but don't over-engineer it yet.

### 🚩 Red Flags

- **Week 3 and the core mechanic still doesn't feel right.** If the fundamental interaction isn't fun with placeholder art, more time won't fix it. Rethink the mechanic or pivot.
- **You're building menus.** Stop. You're procrastinating on the hard problem.
- **You're debating ECS architecture.** It doesn't matter yet. Make it work, make it right later.
- **No one has played it but you.** Your opinion is biased. Get outside eyes.

---

## 2. Vertical Slice

**Duration:** 4-8 weeks  
**Goal:** Build one complete, polished slice of the game. This is your quality benchmark.

### What "Vertical Slice" Means

Take one level, area, or sequence of your game and finish it to shippable quality. Final art. Final audio. Final UI. Final gameplay. If someone played only this slice, they'd understand what the full game feels like.

This is the hardest milestone emotionally — you're going deep instead of wide, and it feels like you should be building more content. Resist that urge.

### Definition of Done

One slice of the game is complete enough to show a publisher, put in a trailer, or submit to a festival. It represents the target quality for the entire game.

### Checklist

**Gameplay**
- [ ] Core mechanic polished and responsive
- [ ] One complete level/area/sequence playable start to finish
- [ ] Enemy/obstacle/puzzle behavior finalized for this slice
- [ ] Difficulty tuned for this slice
- [ ] Player feedback (hit reactions, screen shake, particles) implemented

**Art**
- [ ] Final sprites for player character (all states in this slice)
- [ ] Final tileset or environment art for this area
- [ ] Final enemy/NPC sprites for this slice
- [ ] UI mockup implemented (HUD, health, score, etc.)
- [ ] Particle effects and visual feedback polished
- [ ] Consistent art style established — this is the quality bar

**Audio**
- [ ] At least one music track (or adaptive music prototype)
- [ ] Core sound effects: player actions, enemies, UI, ambient
- [ ] Audio mixed and balanced for this slice

**Technical**
- [ ] Stable frame rate in this slice (target 60fps)
- [ ] No crashes during normal play
- [ ] Camera system working smoothly
- [ ] Basic scene transitions (start → gameplay → end of slice)
- [ ] ECS architecture roughed in for scalability (doesn't need to be final)

**Validation**
- [ ] 3-5 external playtesters have completed the slice
- [ ] Feedback collected and categorized
- [ ] Average play session length matches your target
- [ ] Art style guide documented (even if informal)
- [ ] Scope re-evaluated based on how long this slice took

### The Scope Math

This is your most important data point: **if one slice took X weeks, and you have Y slices planned, your content production phase is roughly X × Y weeks.** If that number terrifies you, it's time to cut scope — now, not later. See [Scope Adjustment Framework](#scope-adjustment-framework).

### 🚩 Red Flags

- **The slice took 2-3x longer than expected.** Your scope is too big. Cut now.
- **Playtesters don't understand what to do.** Your onboarding/tutorialization needs work before you build more content.
- **You keep saying "I'll polish that later."** This IS the polish milestone. If you can't polish one slice, you can't polish thirty.
- **The art style keeps changing.** Lock it down. Iteration is fine; indecision is not.
- **Frame rate is already struggling.** Performance problems compound with content. Address now. See [Profiling & Optimization](../../monogame-arch/guides/G33_profiling_optimization.md).

---

## 3. Alpha

**Duration:** Varies heavily by scope (see timeline table)  
**Goal:** Feature-complete. Every system works. All content is stubbed in. It's rough, but the whole game is playable start to finish.

### Definition of Done

A playtester can start the game, play through every level/area (even with placeholder content), and reach the end. Every system exists and functions — even if some are rough or use placeholder assets.

### Checklist

**Core Architecture**
- [ ] ECS architecture locked — component and system structure finalized
- [ ] All entity archetypes defined and spawning correctly
- [ ] World/query patterns established and consistent across systems
- [ ] No major architectural refactors remaining

See [ECS patterns and architecture guidance](../../monogame-arch/guides/G15_game_loop.md) for ensuring your Arch ECS setup is solid.

**Scene & Flow**
- [ ] All scene types implemented and transitionable
- [ ] Title screen → gameplay → pause → game over → credits flow complete
- [ ] Scene transitions smooth (fade, wipe, or cut — whatever your style)
- [ ] Level select or progression system functional

See [Scene Management](../../monogame-arch/guides/G38_scene_management.md) for scene lifecycle patterns.

**Gameplay Systems**
- [ ] Core gameplay loop complete and tunable
- [ ] All player abilities/actions implemented
- [ ] All enemy/obstacle types implemented (can use placeholder art)
- [ ] Scoring/progression/economy system functional
- [ ] Difficulty progression roughed in across all content
- [ ] Collision and physics systems finalized
- [ ] AI/behavior systems working for all entity types

See [Game Loop](../../monogame-arch/guides/G15_game_loop.md) for loop structure and timing.

**Input**
- [ ] Keyboard + mouse support complete
- [ ] Gamepad support complete (if applicable)
- [ ] Touch support complete (if targeting mobile)
- [ ] Input rebinding functional (if planned)
- [ ] All input edge cases handled (disconnect, multi-device)

See [Input Handling](../../monogame-arch/guides/G7_input_handling.md) for input architecture patterns.

**Persistence**
- [ ] Save system functional — game state serializes and deserializes
- [ ] Settings persist between sessions
- [ ] Progress/unlock state saves correctly
- [ ] Save file corruption handled gracefully (fallback to defaults)
- [ ] Multiple save slots (if planned)

**Content (Stubbed)**
- [ ] All levels/areas/chapters exist (even if using placeholder art)
- [ ] All dialogue/narrative written in at least draft form
- [ ] All boss fights or set pieces roughed in
- [ ] Progression pacing roughed in (player power vs. difficulty curve)
- [ ] All collectibles/secrets/optional content placed (even if placeholder)

**UI**
- [ ] HUD displays all necessary gameplay info
- [ ] All menu screens exist (main, pause, settings, inventory, etc.)
- [ ] UI navigation works with all supported input methods
- [ ] Text is readable at target resolution

**Audio (Rough)**
- [ ] Music tracks assigned to all areas (even if placeholder)
- [ ] Core sound effects in place for all interactions
- [ ] Audio system supports volume control and muting

### 🚩 Red Flags

- **Systems are "almost done" for more than two weeks.** "Almost done" is a red flag phrase. Define what's left as concrete tasks with hour estimates.
- **You're still refactoring core architecture.** The architecture should have been locked at the start of Alpha. Refactoring now means your Vertical Slice didn't validate the tech well enough.
- **Playtesters can't finish the game.** Bugs are fine. Soft locks, crashes, and progression blockers are not — those indicate systemic issues.
- **More than 30% of content is still truly missing** (not placeholder — missing). You're behind.
- **Save/load doesn't work reliably.** This is a system that's painful to retrofit. If it's broken now, prioritize it.
- **You haven't played your own game start-to-finish.** Do it. Today.

---

## 4. Beta

**Duration:** Varies by scope (see timeline table)  
**Goal:** Content-complete and polished. All final assets in place. Focus shifts entirely to bugs, performance, and polish.

### Definition of Done

The game is content-complete. Every sprite is final, every sound is in, every level is designed and populated. What remains is fixing bugs, tuning performance, and polishing feel. No new features. No new content.

### The "No New Features" Rule

This is the hardest rule in game development. You will want to add one more thing. You will have a great idea at 2 AM. Write it down for the sequel. **Beta is about finishing what exists, not adding more.**

### Checklist

**Content Complete**
- [ ] All levels/areas finalized with final art and layout
- [ ] All enemy placements and encounter designs finalized
- [ ] All dialogue/narrative in final form
- [ ] All collectibles, secrets, and optional content placed
- [ ] All cutscenes/cinematics implemented (if applicable)
- [ ] Tutorial/onboarding sequence complete
- [ ] Credits screen populated with real credits

**UI Finalized**
- [ ] All UI screens use final art and layout
- [ ] UI animations and transitions polished
- [ ] All text reviewed for typos and clarity
- [ ] UI scales correctly at all supported resolutions
- [ ] Controller/keyboard/touch navigation all work cleanly

**Audio Complete**
- [ ] All music tracks final and assigned
- [ ] All sound effects final and triggered correctly
- [ ] Adaptive music system working (if applicable)
- [ ] Audio mix balanced across all content
- [ ] No missing or placeholder audio remains

**Performance**
- [ ] Stable 60fps (or target frame rate) across all content
- [ ] No memory leaks during extended play sessions
- [ ] Load times acceptable (< 3 seconds for scene transitions)
- [ ] Garbage collection pauses minimized
- [ ] GPU draw calls and batch counts within budget
- [ ] Profiled all heavy scenes — no performance cliffs

See [Profiling & Optimization](../../monogame-arch/guides/G33_profiling_optimization.md) for detailed performance guidance.

**Accessibility**
- [ ] Remappable controls
- [ ] Colorblind-friendly palette or mode
- [ ] Text size options (or already large enough)
- [ ] Screen reader support for menus (if feasible)
- [ ] Difficulty options or assist modes
- [ ] Photosensitivity review (flashing lights, strobes)
- [ ] Subtitles for any voiced content

See [Accessibility](../../monogame-arch/guides/G35_accessibility.md) for a comprehensive accessibility checklist.

**Localization**
- [ ] All player-facing strings externalized (no hardcoded text)
- [ ] String table complete for primary language
- [ ] Text rendering supports target languages (character sets, RTL if needed)
- [ ] UI layout accommodates longer translated strings
- [ ] Localization pipeline tested with at least one additional language (if shipping localized)

See [Localization](../../monogame-arch/guides/G34_localization.md) for localization architecture and workflow.

**Bug Fixing**
- [ ] All known crash bugs fixed
- [ ] All progression blockers fixed
- [ ] Bug tracker triaged — all bugs categorized as Ship-Blocker / Should-Fix / Won't-Fix
- [ ] At least 3 full playthroughs completed by different testers
- [ ] Edge cases tested: alt-tab, minimize, sleep/wake, low memory

**Polish**
- [ ] Screen shake, particles, juice on all interactions
- [ ] Death/damage/victory feedback feels satisfying
- [ ] Camera behavior smooth and intentional everywhere
- [ ] Loading screens or transitions feel seamless
- [ ] Nothing feels "programmer art" — placeholder content is gone

### 🚩 Red Flags

- **New features are still being added.** You're not in Beta. Go back to Alpha.
- **More than 20% of art is still placeholder.** You're not in Beta.
- **Bug count is rising faster than you're fixing.** This suggests systemic issues, not surface bugs. Stop and investigate root causes.
- **Performance is below target in more than a few scenes.** Optimization should be targeted, not wholesale. If everything is slow, the architecture may need rethinking.
- **Playtesters are finding fundamental design issues.** This is very late for design changes. Evaluate whether the issue is critical enough to address or if you can ship with it.
- **You haven't tested on target hardware.** If you're shipping on anything other than your dev machine, test there now.

---

## 5. Release Candidate

**Duration:** 1-4 weeks depending on scope  
**Goal:** Final testing, platform compliance, and store submission preparation. The game should be shippable — you're looking for reasons NOT to ship.

### Definition of Done

The game passes all platform requirements, runs correctly on all target platforms, and is ready for store submission. You can't find any more ship-blocking bugs. You'd be comfortable if this build went to customers.

### Checklist

**Stability**
- [ ] Zero known crash bugs
- [ ] Zero known progression blockers
- [ ] No soft locks or infinite loops in any path
- [ ] 24-hour soak test passed (game running overnight without issues)
- [ ] Memory stable over extended play sessions

**Platform Builds**
- [ ] Windows build tested on multiple machines
- [ ] macOS build tested (if shipping on Mac)
- [ ] Linux build tested (if shipping on Linux)
- [ ] Console builds pass certification requirements (if applicable)
- [ ] All platform-specific features work (Steam overlay, achievements, etc.)

See [Deployment & Platform Builds](../../monogame-arch/guides/G32_deployment_platform_builds.md) for build pipeline and platform-specific guidance.

**Store Submission**
- [ ] Store page created with final screenshots and description
- [ ] Trailer uploaded
- [ ] Store tags and categories set
- [ ] Age rating obtained (ESRB, PEGI, etc. if required)
- [ ] Privacy policy and EULA in place (if required)
- [ ] Press kit prepared (screenshots, logo, description, key art)
- [ ] Review copies sent to press/influencers (if planned)
- [ ] Pricing set
- [ ] Launch date confirmed

See [Publishing & Distribution](../../monogame-arch/guides/G36_publishing_distribution.md) for store requirements and submission guidance.

**Final QA**
- [ ] Full playthrough with fresh save — no shortcuts, no debug tools
- [ ] Tested all difficulty levels
- [ ] Tested all optional/alternate paths
- [ ] Tested all settings combinations (resolution, fullscreen/windowed, audio levels)
- [ ] Tested clean install (no leftover dev files)
- [ ] Tested update path (if you've had beta testers with earlier builds)
- [ ] Controller disconnect/reconnect during gameplay
- [ ] Tested with no internet connection (if game has any online features)

**Release Infrastructure**
- [ ] Build pipeline automated (one command to produce release build)
- [ ] Version number set and baked into the build
- [ ] Crash reporting enabled (if applicable)
- [ ] Analytics enabled (if applicable)
- [ ] Patch pipeline tested (can you ship an update?)

### 🚩 Red Flags

- **New bugs keep appearing in areas you thought were stable.** Regression testing is failing. Slow down and investigate.
- **The store page isn't ready.** Marketing prep takes longer than you think. If you haven't started, you're behind.
- **You haven't tested on a clean machine.** Your dev machine has DLLs, runtimes, and configs that a customer's machine won't have.
- **You're still "just fixing one more thing."** Set a hard deadline. RC is about shipping, not perfecting.

---

## 6. Gold / Launch

**Duration:** 1-2 weeks (launch prep + launch week)  
**Goal:** Ship the game. Monitor the launch. Breathe.

### Pre-Launch Checklist (1 week before)

- [ ] Final build tagged in version control
- [ ] Build uploaded to all storefronts
- [ ] Store page reviewed one final time
- [ ] Launch trailer scheduled/uploaded
- [ ] Social media announcements scheduled
- [ ] Community channels ready (Discord, forums, etc.)
- [ ] Support email/system set up
- [ ] Day-one patch prepared (if needed based on last-minute fixes)
- [ ] Personal: sleep, eat, take a break before launch day

### Launch Day Checklist

- [ ] Store page goes live — verify you can buy and download
- [ ] Download and install from the store as a customer would
- [ ] Play the first 15 minutes of the launched build
- [ ] Social media announcements posted
- [ ] Monitor community channels for first-hour feedback
- [ ] Monitor crash reports and error logs
- [ ] Respond to first wave of player questions/issues
- [ ] Celebrate. You shipped a game. That's rare.

### Post-Launch (First Week)

- [ ] Monitor crash reports daily
- [ ] Track player feedback themes (what's loved, what's confusing)
- [ ] Hotfix critical bugs within 24-48 hours if needed
- [ ] Thank early players and reviewers
- [ ] Document launch metrics (sales, wishlists converted, reviews)
- [ ] Start a post-launch todo list (don't act on it yet — rest first)

### Post-Launch (First Month)

- [ ] Ship 1-2 patches addressing top player issues
- [ ] Evaluate whether additional content or features are worth pursuing
- [ ] Write a post-mortem while it's fresh
- [ ] Update production documents with lessons learned
- [ ] Plan next steps: DLC? Sequel? New project? Rest?

---

## Milestone Review Template

Use this template at the end of every milestone. Be honest — this is for you, not for anyone else.

```markdown
## Milestone Review: [Milestone Name]
**Date:** YYYY-MM-DD
**Planned Duration:** X weeks
**Actual Duration:** X weeks

### What Got Done
- [List completed items]

### What Slipped
- [List items that didn't get finished]
- [For each: why it slipped, and where it moves to]

### Scope Changes
- [Features added since last milestone]
- [Features cut since last milestone]
- [Net scope change: grew / shrank / same]

### What Went Well
- [Things that worked, felt good, went faster than expected]

### What Went Poorly
- [Things that were harder than expected, took too long, felt bad]

### Morale Check (1-10)
- Energy: __/10
- Motivation: __/10
- Confidence in shipping: __/10

### Key Learnings
- [What would you do differently?]

### Next Milestone Plan
- **Target:** [Milestone name]
- **Duration:** X weeks (deadline: YYYY-MM-DD)
- **Top 3 Priorities:**
  1. ...
  2. ...
  3. ...
- **Biggest Risk:** ...
```

### How to Use This

1. Copy the template into a new file: `reviews/milestone_review_[name].md`
2. Fill it in honestly within 24 hours of completing the milestone
3. Read the previous milestone review before starting the next phase
4. If morale drops below 5 on any axis two milestones in a row — something structural needs to change (scope, schedule, or approach)

---

## Scope Adjustment Framework

Scope is the #1 killer of indie games. Not bugs. Not bad art. Scope. This framework gives you a structured way to cut without panicking.

### When to Cut

Evaluate scope at **every milestone boundary** — not mid-milestone (you don't have enough data mid-milestone). Use the milestone review to assess.

**Mandatory scope review triggers:**

- A milestone took more than 1.5x the planned time
- Morale is below 5 on any axis
- You've added features without cutting others
- Your remaining timeline doesn't fit the remaining work
- You've stopped having fun and started dreading the project

### The Scope Triage Matrix

For every feature or content item, categorize:

| Category | Definition | Action |
|---|---|---|
| **Core** | Removing this breaks the game or makes it not fun | Keep. Always. |
| **Important** | Clearly improves the game, players would notice its absence | Keep if on schedule, first to simplify if behind |
| **Nice-to-Have** | Would be cool, but the game works without it | Cut immediately if behind schedule |
| **Wishlist** | "Wouldn't it be awesome if..." | Cut. Add to sequel ideas doc. |

### How to Cut

1. **List everything remaining** — every feature, level, system, asset
2. **Categorize each item** using the matrix above
3. **Cut all Wishlist items** — no debate, no negotiation with yourself
4. **Evaluate Nice-to-Haves** — be ruthless; cut anything that doesn't directly serve the core experience
5. **Simplify Important items** — can a complex feature be done in a simpler way? (3 enemy types instead of 8? 15 levels instead of 30?)
6. **Never cut Core** — if Core items are at risk, you have a fundamental scope or timeline problem

### The Kill Switch

Some features should have a kill switch — a predefined point where you decide "if this isn't working by [date], we cut it." Set kill switches for:

- **Risky mechanics** you haven't prototyped: "If multiplayer isn't fun by week 8, we ship single-player only"
- **Content-heavy features**: "If procedural generation can't produce good levels by Alpha, we hand-craft fewer levels"
- **Nice-to-have systems**: "If the achievement system isn't done by Beta, we ship without it"

Write your kill switches down at the start of the project and **honor them**.

### Scope Adjustment by Milestone

| Milestone | What You Can Cut | What You Can't Cut |
|---|---|---|
| Prototype | Anything. This is the cheapest time to pivot or kill the project. | The core mechanic experiment. |
| Vertical Slice | Content scope, extra mechanics, multiplayer, secondary systems | Quality bar. The slice must represent final quality. |
| Alpha | Content volume (fewer levels), secondary features, optional modes | Core systems, game loop, save/load, any system other systems depend on |
| Beta | Nice-to-have polish, additional languages, bonus content, online features | Content that's already in, core polish, critical bugs |
| RC | Nothing should need cutting. If it does, you're not really in RC. | Everything. You're shipping what you have. |

### The Emotional Cost

Cutting features hurts. You imagined this game with those features. Here's what helps:

- **Write it down.** Keep a "Future/Sequel Ideas" document. Nothing is lost — it's deferred.
- **Remember the alternative.** An unfinished game with 30 features helps nobody. A finished game with 15 features helps players.
- **The best games are focused.** Celeste doesn't need an inventory system. Hollow Knight doesn't need crafting. Your game doesn't need [that feature you're attached to].
- **Shipping is a skill.** Every game you finish makes you better at making games. Every game you abandon teaches you less.

---

## Quick Reference: Milestone Summary

| Milestone | Focus | Key Question | Done When |
|---|---|---|---|
| **Prototype** | Is this fun? | Would I play this? | Core mechanic proven with external feedback |
| **Vertical Slice** | What does "done" look like? | Could this slice ship? | One complete, polished slice at final quality |
| **Alpha** | Does the whole game work? | Can someone play start to finish? | Feature-complete, all content stubbed |
| **Beta** | Is everything in and polished? | Would I pay money for this? | Content-complete, polished, performant |
| **RC** | Is it ready to ship? | Can I find a reason NOT to ship? | All bugs resolved, platforms verified |
| **Gold** | Ship it. | Did I actually press the button? | Published and playable by customers |

---

*Cross-references: [Game Loop](../../monogame-arch/guides/G15_game_loop.md) · [Scene Management](../../monogame-arch/guides/G38_scene_management.md) · [Input Handling](../../monogame-arch/guides/G7_input_handling.md) · [Profiling & Optimization](../../monogame-arch/guides/G33_profiling_optimization.md) · [Accessibility](../../monogame-arch/guides/G35_accessibility.md) · [Localization](../../monogame-arch/guides/G34_localization.md) · [Deployment & Platform Builds](../../monogame-arch/guides/G32_deployment_platform_builds.md) · [Publishing & Distribution](../../monogame-arch/guides/G36_publishing_distribution.md)*
