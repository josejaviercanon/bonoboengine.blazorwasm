# 08 — Playtesting Guide



> *"You can't read the label from inside the bottle."*

You built the game. You know every mechanic, every shortcut, every hidden path. That's exactly why you're the worst person to judge whether it's fun. This guide gives you a structured, repeatable process for playtesting — from solo self-tests to public betas — so you ship a game people actually enjoy, not just one you *think* works.

---

## Table of Contents

1. [Why Playtest](#1-why-playtest)
2. [Types of Playtesting](#2-types-of-playtesting)
3. [Self-Testing Framework](#3-self-testing-framework)
4. [Finding Testers](#4-finding-testers)
5. [Feedback Collection](#5-feedback-collection)
6. [Recording Sessions](#6-recording-sessions)
7. [Analyzing Feedback](#7-analyzing-feedback)
8. [Iterating on Feedback](#8-iterating-on-feedback)
9. [Playtesting Schedule](#9-playtesting-schedule)
10. [Common Playtesting Mistakes](#10-common-playtesting-mistakes)
11. [Feedback Form Templates](#11-feedback-form-templates)

---

## 1 — Why Playtest

### Playing ≠ Playtesting

When you *play* your game, you're experiencing it. When you *playtest*, you're studying it. The difference is intention:

| Playing | Playtesting |
|---------|-------------|
| Goal: have fun | Goal: find problems |
| React naturally | Observe deliberately |
| Skip past friction | Stop and examine friction |
| "That was fine" | "Why did I hesitate there?" |

You need both, but they're different activities. Don't confuse "I had fun playing my game" with "my game is fun."

### The Curse of Knowledge

The curse of knowledge is the single biggest reason playtesting matters. Once you know something, you can't unknow it. You can't experience your own tutorial as a first-time player. You can't *not* know where the hidden passage is. You can't forget how the dash-cancel works.

This means:

- **Controls that feel intuitive to you** may be baffling to newcomers — you've had hundreds of hours of practice.
- **"Obvious" solutions to puzzles** are only obvious because you designed them.
- **Difficulty spikes you breeze through** might be walls for everyone else — you know the patterns by heart.
- **Story beats that feel clear** may be incoherent to someone who wasn't in your head during writing.

The curse of knowledge isn't a deficiency. It's a structural reality. You cannot design your way out of it. You can only test your way through it.

### What Playtesting Actually Reveals

Things you will never find on your own:

- **Onboarding failures** — Players don't read your tutorial text. They button-mash past it. They try jumping before you've told them how. (See [G61 — Tutorial & Onboarding](../../monogame-arch/guides/G61_tutorial_onboarding.md))
- **Invisible affordances** — That ledge you meant to be climbable? Nobody tried. It doesn't *look* climbable to someone who doesn't know.
- **Fun you assumed** — The core loop that's "obviously" fun might actually be tedious. Or the thing you thought was a throwaway feature might be the most enjoyable part.
- **Accessibility gaps** — Color-coded mechanics, audio-dependent cues, small text, fast timers. You won't notice what you can see/hear/react to. (See [G35 — Accessibility](../../monogame-arch/guides/G35_accessibility.md))

---

## 2 — Types of Playtesting

### Overview

| Type | When to Use | Cost | Signal Quality |
|------|-------------|------|----------------|
| Self-testing | Always, continuously | Free | Low (biased) but essential |
| Friends & family | Early prototypes | Free | Low-medium (social bias) |
| Targeted testers | Core mechanics locked | Low-medium | High |
| Public beta | Feature-complete | Medium | Medium-high (noisy but broad) |

### Self-Testing

**What it is:** You play your own game with structure and intention.

**Best for:** Catching bugs, flow issues, pacing problems, technical regressions.

**Limitations:** You can't surprise yourself. You can't unlearn your own design. Your muscle memory masks input problems.

**When:** Every day you work on the game. Non-negotiable.

### Friends & Family

**What it is:** People who know you play your game, usually in person or over screen share.

**Best for:** Very early validation ("is this concept fun at all?"), watching someone encounter your game for the first time, emotional support.

**Limitations:**
- **Social pressure** — They want to be nice. They'll say "it's fun!" when it's confusing.
- **Non-representative** — Your mom is not your target audience (probably).
- **One-shot** — Once they've played, they can never be first-time players again. Use this resource wisely.

**Tip:** Watch their *face and hands*, not their words. Where do they pause? Where do they squint? Where do they mash buttons in frustration? That's your real feedback.

### Targeted Testers

**What it is:** People from your target audience who you've recruited specifically to test.

**Best for:** Validating core loop, difficulty curve, controls, onboarding. This is your highest-signal testing.

**Limitations:**
- Takes effort to find and coordinate.
- Requires clear structure (forms, tasks, recording) to get actionable data.
- Small sample sizes can be misleading.

**When:** Once your core mechanics are playable and reasonably stable. Don't waste targeted testers on a crashy prototype.

### Public Beta

**What it is:** A broader release (itch.io, Steam Next Fest demo, open beta) where anyone can play and optionally give feedback.

**Best for:** Stress testing, finding edge cases, validating that the game works across hardware, gauging general reception, building wishlists.

**Limitations:**
- **Noisy feedback** — You'll get everything from thoughtful essays to "this sucks lol."
- **First impressions at scale** — If your onboarding is broken, hundreds of people will bounce and never return.
- **Public perception** — A bad beta can hurt your reputation. Don't go public until you're confident in the first 10 minutes.

**When:** Feature-complete or near it. The game should represent your vision, even if rough around the edges.

---

## 3 — Self-Testing Framework

Self-testing is biased by definition, but it's also the most frequent testing you'll do. Make it count with structure.

### The Notepad Method

Every self-test session, keep a notepad (physical or digital) next to you. As you play, jot down:

- 😐 Moments of boredom or tedium
- 😕 Anything that felt confusing (even though you know the answer)
- 🐛 Bugs, visual glitches, audio issues
- ✨ Moments that felt *good* — protect these
- ⏱️ Pacing notes — too fast, too slow, too much downtime
- 🤔 "Would a new player understand this?"

Don't fix things mid-session. Just note them. Fixing while testing breaks your flow and biases your remaining play.

### The 30-Second Test

Start a brand new save. Set a 30-second timer. Stop.

Ask yourself:
- Did anything *interesting* happen in those 30 seconds?
- Is there at least one moment of agency (a choice, an action, a reaction)?
- Would a player on itch.io still be playing, or would they have closed the tab?

If the first 30 seconds are a logo screen, a title crawl, and a text box — you have a problem. Players in 2D browser games and indie titles decide *fast*. (See [G30 — Game Feel Tooling](../../monogame-arch/guides/G30_game_feel_tooling.md) for making those first moments impactful.)

### The First 5 Minutes Test

Play the first 5 minutes as if you've never seen the game. Try to:

- **Ignore your own knowledge** — Pretend you don't know the controls. Read every prompt as if it's new. Does the game teach you, or does it assume you know?
- **Note every assumption** — "The player will know to press down to enter the door." Will they? Really?
- **Check the learning curve** — Are the first 5 minutes all tutorials and no play? Or all play and no teaching? (See [G61 — Tutorial & Onboarding](../../monogame-arch/guides/G61_tutorial_onboarding.md))

### System Isolation Testing

Don't just play the game start-to-finish every time. Test specific systems in isolation:

- **Combat only** — Does a single encounter feel good? Is feedback clear? Do hits feel impactful?
- **Movement only** — Run, jump, dash through an empty level. Does it feel *good* to move? (See [G30 — Game Feel Tooling](../../monogame-arch/guides/G30_game_feel_tooling.md))
- **Menu/UI flow** — Navigate every menu. Open inventory, close it, open map, close it. Is it snappy? Can you get lost?
- **Economy/progression** — Skip to mid-game. Are numbers making sense? Is progression rewarding or grindy?
- **Edge cases** — What happens if you dash into a wall? Jump off the map? Open the menu during a cutscene? Pause during a boss attack?

### Record Your Sessions

Even self-test sessions. You'll catch things on playback that you missed live — a frame hitch you didn't consciously notice, an animation that clips, a moment where you paused and didn't realize why. (See [Recording Sessions](#6-recording-sessions) for setup.)

---

## 4 — Finding Testers

### Where to Look

**Discord Communities**
- Game dev servers (Indie Game Developers, Game Dev League)
- Genre-specific communities (platformer fans, roguelike enthusiasts, metroidvania groups)
- Engine-specific servers (Godot, Unity, GameMaker) often have #playtesting channels
- Your own server, even if it's tiny

**Reddit**
- r/playmygame — Built specifically for this. Read the rules; low-effort posts get ignored.
- r/indiegaming — Good for polished builds; less tolerant of rough prototypes.
- r/gamedev — Feedback Friday threads.
- Genre-specific subreddits (r/metroidvania, r/roguelikes, etc.) — Highly targeted, valuable feedback.

**itch.io**
- Upload a free build and tag it as "in development."
- Join game jams — jam participants expect rough games and give better feedback than general audiences.
- The itch.io community is more forgiving and dev-friendly than Steam.

**Social Media**
- Twitter/X — Post GIFs of your game with #indiedev, #gamedev, #screenshotsaturday. Build an audience, then ask for testers.
- TikTok/YouTube Shorts — Short gameplay clips can attract interested players.
- Mastodon — The gamedev community there is small but engaged.

**Other**
- Local game dev meetups (check Meetup.com, local universities)
- Game testing forums (IndieDB, TIGSource)
- Other solo devs — offer to swap: "I'll test yours if you test mine"

### How to Ask

Bad ask:
> "Hey can someone play my game and tell me what you think?"

Good ask:
> "I'm looking for 3-5 playtesters for my 2D platformer (15-20 min playthrough). It runs in-browser on itch.io. I'm specifically trying to find out if the first level teaches mechanics well enough. I have a short feedback form (5 min). Interested?"

Key elements:
- **Specific number** of testers you want
- **Time commitment** (how long to play + how long to give feedback)
- **Platform/requirements** (browser, download, controller needed?)
- **What you're testing** — specific focus, not "everything"
- **How feedback works** — form, DM, voice call?

### What to Offer in Return

- **Credit** in your game (most testers appreciate this)
- **A free copy** on release
- **Reciprocal testing** — test their game in return
- **Early access** to future builds
- Genuine thanks and follow-up ("here's what I changed based on your feedback")

Don't offer money unless you're running a formal study. It changes the dynamic and attracts people who don't care about your game.

---

## 5 — Feedback Collection

### Observation vs. Opinion

The most valuable data comes from *watching* someone play, not from *asking* them what they think.

| Observation (high value) | Opinion (lower value) |
|---|---|
| "Player died 6 times on spike pit in level 3" | "The game is too hard" |
| "Player didn't notice the health bar for 2 minutes" | "The UI could be better" |
| "Player tried to wall-jump 4 times before realizing they can't" | "You should add wall-jumping" |

Observations tell you **what happened**. Opinions tell you **what they think should change**. You need the *what happened* to make good decisions — the *what should change* is your job.

### Question Design

**Avoid leading questions:**

| ❌ Leading | ✅ Neutral |
|---|---|
| "Did you enjoy the combat?" | "Describe your experience with combat." |
| "Was the tutorial helpful?" | "How did you learn the controls?" |
| "Did the boss feel fair?" | "What happened during the boss fight?" |

**Open-ended questions yield better data than yes/no:**

| ❌ Closed | ✅ Open |
|---|---|
| "Was it fun?" | "What was the most memorable moment?" |
| "Did you get stuck?" | "Was there a point where you weren't sure what to do next?" |
| "Would you recommend it?" | "Who would you show this game to, and why?" |

### The Game Usability Scale (adapted from SUS)

The System Usability Scale is a quick, validated questionnaire used in UX research. Here's a version adapted for games. Have testers rate each statement 1–5 (Strongly Disagree to Strongly Agree):

1. I think I would like to play this game again.
2. I found the game unnecessarily complex.
3. I thought the game was easy to pick up.
4. I think I would need help from another person to understand this game.
5. I found the various mechanics in this game were well integrated.
6. I thought there was too much inconsistency in this game.
7. I imagine that most people would learn this game quickly.
8. I found the game very awkward to play.
9. I felt confident while playing.
10. I needed to learn a lot before I could get going with this game.

**Scoring:** For odd-numbered items, subtract 1 from the score. For even-numbered items, subtract the score from 5. Sum all values and multiply by 2.5. Result is 0–100. Above 68 is above average. Above 80 is good. Below 50 means significant usability problems.

### Pre-Test Questionnaire

Collect this *before* they play:

1. How often do you play 2D games? (Daily / Weekly / Monthly / Rarely / Never)
2. What 2D games have you played recently?
3. What input device will you use? (Keyboard / Controller / Touch / Other)
4. Any accessibility needs? (Color vision, motor, hearing, etc.)
5. How familiar are you with [your game's genre]? (Very / Somewhat / Not at all)

This data lets you contextualize their feedback. A platformer veteran saying "too easy" means something different from a casual player saying the same thing.

### Post-Test Questionnaire

See [Feedback Form Templates](#11-feedback-form-templates) for complete, ready-to-copy questionnaires.

---

## 6 — Recording Sessions

### Why Record

Watching a recording of someone playing your game — especially without commentary — is one of the most valuable things you can do. You'll see:

- **Confusion points** — Where they stop moving and look around. Where they open the menu and close it without doing anything.
- **Death spots** — Patterns in where players die. If 4 out of 5 testers die in the same spot, that's a design problem, not a skill problem.
- **Hesitation** — That half-second pause before a jump. The moment they nearly press a button and don't. Hesitation means uncertainty.
- **Delight** — Where they lean forward. Where they try something creative. Where they smile. Protect these moments — they're your game's soul.
- **Ignored content** — Chests they walked past. NPCs they didn't talk to. Paths they never explored.

### OBS Setup for Playtest Recording

**OBS Studio** (free, cross-platform) is the standard tool.

Quick setup for playtest recording:

1. **Source:** Add Game Capture (Windows) or Window Capture (Mac/Linux) for the game.
2. **Source:** Add Audio Output Capture for game audio.
3. **Source (optional):** Add a webcam source if recording in-person testers (face reactions are invaluable).
4. **Source (optional):** Add Audio Input Capture for tester's microphone (think-aloud protocol).
5. **Settings → Output:**
   - Recording format: MKV (crash-safe; remux to MP4 after via File → Remux)
   - Encoder: x264 or hardware (NVENC/AMF) if available
   - Quality: "High Quality, Medium File Size" is fine — you're reviewing, not publishing
   - Rate control: CRF 20-23 for good quality without massive files
6. **Settings → Video:**
   - Base resolution: Match game resolution
   - Output resolution: 1280×720 is plenty for review
   - FPS: 30 is fine for review

**File naming convention:** `playtest_[tester]_[date]_[build].mkv`
Example: `playtest_alex_2025-03-07_v0.4.2.mkv`

### Other Recording Options

- **Built-in OS tools:** macOS (Cmd+Shift+5), Windows (Win+G Game Bar), Linux (SimpleScreenRecorder)
- **Loom** — Good for remote testers; they can record and share a link without setup.
- **Medal / ShadowPlay** — Lightweight game recording.
- **Discord screen share** — For live remote sessions; not great for review but convenient.

### The Silent Observation Method

The gold standard: Watch someone play your game *without saying anything*.

Rules:
1. **Don't explain anything.** Not before, not during, not after (until the feedback session).
2. **Don't answer questions.** If they ask "how do I...?", say "try to figure it out." Every question is a data point — it means your game didn't communicate clearly enough.
3. **Don't react.** No wincing when they miss something. No smiling when they find a secret. Your body language influences their behavior.
4. **Take notes.** Timestamp + observation. `2:34 — tried to jump to ledge, missed, tried 3 more times, gave up.`
5. **Debrief after.** Only after they've finished (or decided to stop) do you discuss.

This is hard. It will be uncomfortable. It is the most useful thing you can do.

---

## 7 — Analyzing Feedback

### Don't Get Defensive

This is your baby. Someone is going to say it's ugly, boring, confusing, or broken. Your gut reaction will be one of:

- "They just didn't understand it."
- "That's not the target audience."
- "They were playing it wrong."

Sometimes those are true. Usually they're defense mechanisms. Before you dismiss feedback, sit with it for 24 hours. If it still feels wrong after a day, *then* evaluate it critically.

**The rule:** If one person says something, it might be them. If three people say the same thing, it's you.

### Separate the Problem from the Solution

Players are great at identifying **what's wrong**. They are terrible at identifying **how to fix it**.

| What they say (solution) | What they mean (problem) |
|---|---|
| "You should add a double jump" | "I can't reach that platform and it's frustrating" |
| "The sword should do more damage" | "Combat feels like it takes too long" |
| "There should be a minimap" | "I keep getting lost" |
| "Make the enemies easier" | "I don't understand the enemy's attack pattern" |

Listen to the *problem*. Discard the *solution* (unless it's actually a good idea). The fix for "I can't reach that platform" might be a double jump — or it might be moving the platform, adding a visual cue, or changing the jump arc.

### The Frequency × Severity Matrix

Not all feedback is equal. Prioritize using two axes:

```
                    High Severity
                         │
         CRITICAL        │        IMPORTANT
      (Fix immediately)  │    (Fix before release)
                         │
   ──────────────────────┼──────────────────────
                         │
         LOW PRIORITY    │        MONITOR
       (Fix if time)     │    (Note but don't act)
                         │
                    Low Severity

   Low Frequency ───────────────── High Frequency
```

- **Critical** (high frequency + high severity): Multiple testers hit it, and it blocks progress or causes frustration. Fix first.
- **Important** (high frequency + low severity): Many people notice it but it doesn't ruin the experience. Fix before release.
- **Monitor** (low frequency + high severity): One person hit a game-breaking bug. Investigate but don't panic.
- **Low priority** (low frequency + low severity): Minor polish. Nice to fix but won't move the needle.

### Feedback Tracking Spreadsheet

Keep a simple spreadsheet (Google Sheets, Notion, whatever you use):

| # | Date | Tester | Category | Feedback | Severity (1-5) | Frequency | Status | Notes |
|---|------|--------|----------|----------|-----------------|-----------|--------|-------|
| 1 | 3/7 | Alex | Controls | "Couldn't figure out how to dash" | 4 | 3/5 testers | Fixed | Added dash prompt in level 1 |
| 2 | 3/7 | Alex | Difficulty | "Boss 2 too hard" | 3 | 1/5 testers | Monitoring | Alex is casual player; others were fine |
| 3 | 3/7 | Sam | Visual | "Didn't see the spikes" | 5 | 4/5 testers | In progress | Spikes blend with background |

Categories to track: Controls, Difficulty, Visual, Audio, UI/UX, Progression, Bugs, Performance, Onboarding, Other.

---

## 8 — Iterating on Feedback

### The Feedback Cycle

```
Feedback → Hypothesis → Change → Retest
    ↑                                 │
    └─────────────────────────────────┘
```

1. **Feedback:** "4 out of 5 testers didn't find the hidden key in room 3."
2. **Hypothesis:** "The key blends into the background. Making it glow or adding a particle effect will fix it."
3. **Change:** Add a subtle particle effect to the key.
4. **Retest:** Next round of testers — do they find it? If yes, hypothesis confirmed. If no, try a different approach.

Don't skip the hypothesis step. "Players didn't find the key, so I'll add a giant arrow pointing to it" might fix the symptom but could also kill the exploration feel. Think about *why* before you change *what*.

### When to Ignore Feedback

Not all feedback demands action. Ignore (or deprioritize) when:

- **It conflicts with your core vision.** If your game is intentionally hard and someone says "make it easier" — that's not your player. (But consider offering difficulty options — see [G35 — Accessibility](../../monogame-arch/guides/G35_accessibility.md).)
- **It's a preference, not a problem.** "I prefer pixel art to vector art" isn't actionable feedback.
- **It's from a non-representative tester.** Your FPS-only friend struggling with your turn-based RPG is expected.
- **It would require a fundamental redesign.** Late-stage "you should make this an open world" feedback isn't useful. File it for the sequel.
- **Only one person said it** and you can't reproduce the issue.

### When Feedback Conflicts

Tester A says the game is too hard. Tester B says it's too easy. This is normal. Strategies:

- **Look at the data, not the opinions.** Did Tester A die 15 times on level 3 while Tester B breezed through? That's a data point about difficulty variance, not about overall difficulty.
- **Check tester profiles.** Is A a casual player and B a speedrunner? Both are valid — you just need to decide which audience to tune for (or offer difficulty settings).
- **Find the underlying agreement.** A says "too hard," B says "too easy" — maybe the real problem is *inconsistent* difficulty. Some sections are trivial, others are spikes.
- **More data breaks ties.** If you have 3 testers saying hard and 1 saying easy, lean toward the majority (unless the 1 is your exact target player).

### A/B Testing for Indies

You don't need enterprise A/B testing infrastructure. For simple things:

- **Two builds, different testers.** Give half your testers version A (original jump height) and half version B (increased jump height). Compare completion rates.
- **Sequential testing.** Ship build A for a week on itch.io, then build B. Compare feedback and analytics.
- **In-game flags.** A simple config flag that toggles a mechanic variant lets you switch between versions without separate builds.

Keep A/B tests small and focused. One variable at a time. If you change jump height AND enemy speed AND level layout, you won't know what caused the difference.

---

## 9 — Playtesting Schedule

### Start Earlier Than You Think

> "When should I start playtesting?"
> "Earlier."
> "But—"
> "Earlier."

The moment you have a playable prototype — even if it's ugly, even if there's only one level, even if the art is placeholder rectangles — you should start self-testing with structure. External testing can start as soon as your core mechanic is playable.

You are *not* wasting testers on a rough build. You are saving yourself months of building in the wrong direction.

### Recommended Cadence

| Activity | Frequency | Who |
|----------|-----------|-----|
| Structured self-test | Every work session | You |
| The 30-second test | After any UX change | You |
| The first-5-minutes test | Weekly | You |
| Friends & family test | Early prototype, then as needed | Close contacts |
| Targeted external test | Monthly or at milestones | Recruited testers |
| Public beta / demo | 1-2 times before launch | Public |

### Development Phase Mapping

**Pre-Alpha** (prototype → core mechanics)
- Self-testing: Daily.
- External: 1-2 friends or fellow devs for "is this fun?" gut check.
- Focus: Does the core mechanic feel good? Is there a reason to keep playing for more than 60 seconds?

**Alpha** (core mechanics → content production)
- Self-testing: Daily, structured.
- External: Monthly targeted tests (3-5 testers).
- Focus: Difficulty curve, onboarding, pacing, controls. Are mechanics communicating clearly?

**Beta** (content-complete → polished)
- Self-testing: Daily, full playthroughs weekly.
- External: Bi-weekly targeted tests + public demo/beta.
- Focus: Polish, edge cases, performance, accessibility. Full experience testing.

**Release Candidate**
- External: Final round of targeted testing on release build.
- Focus: Show-stopping bugs, platform-specific issues, first-time user experience from install to credits.

### Milestone Playtests

At major milestones, do a "clean playtest": a full playthrough from start to current-end with fresh testers who haven't seen the game before. Milestone testers are precious — don't reuse them unless you're testing changes they specifically flagged.

---

## 10 — Common Playtesting Mistakes

### ❌ Explaining the Game Before They Play

"So this is a metroidvania where you play as a ghost who can possess enemies, and you press Shift to dash, and the blue doors need the blue key, and..."

Stop. If your game needs a verbal introduction to be playable, your game has an onboarding problem. Let them figure it out. Their confusion is your data.

**Exception:** If there's a known broken/missing feature, you can say "the settings menu doesn't work yet, so ignore that." Don't explain mechanics.

### ❌ Watching Over Their Shoulder

Hovering behind them, wincing at their mistakes, making small noises — this changes how they play. They'll play more carefully, take fewer risks, and feel self-conscious. Either sit behind them where they can't see you, or watch the recording later.

### ❌ Taking Feedback Personally

"Your game is confusing" is not "you are a bad designer." Even "I didn't enjoy this" is valuable data. The tester is doing you a favor. Thank them for honest feedback — it's the useful kind.

If you find yourself mentally arguing with feedback as you hear it: stop, write it down, and process it tomorrow.

### ❌ Testing Too Late

If the first time a stranger plays your game is the Steam demo release, you've waited too long. You've potentially built months of content on a broken foundation. Test the core loop within the first few weeks of development.

### ❌ Only Testing with Gamers

Your hardcore platformer fan friends will give you very different feedback than a casual player. Both perspectives matter. If your game is meant for a broad audience, you *need* non-gamers in your testing pool. If it's niche, you still need at least a few people outside the niche to validate onboarding.

### ❌ Not Testing on Target Hardware

Your game runs great on your dev machine with an RTX 4080 and 32GB RAM. Does it run on a 5-year-old laptop? A Steam Deck? A phone? Test on the minimum spec you're targeting. Performance is game feel. (See [G30 — Game Feel Tooling](../../monogame-arch/guides/G30_game_feel_tooling.md))

### ❌ Asking "Is It Fun?"

"Is it fun?" is a terrible question. It's too broad, too subjective, and testers will usually say "yeah!" to be polite. Ask specific questions about specific moments. Watch their behavior instead of asking for their summary.

### ❌ Only Fixing Things, Never Cutting

Sometimes the fix isn't to improve the bad part — it's to cut it. If a mechanic consistently confuses testers despite three iterations of tutorials and tooltips, maybe the mechanic doesn't belong in the game.

### ❌ Not Closing the Loop

If a tester takes 30 minutes to play your game and fill out a feedback form, follow up. Tell them what you changed based on their input. This builds loyalty, and they might test again later.

---

## 11 — Feedback Form Templates

Copy these directly into Google Forms, Notion, or use them as markdown checklists. Adapt to your game.

---

### Template A: First Impression Test

> **Purpose:** Test the first 5-10 minutes. Are players hooked? Do they understand the game?
> **Duration:** 10-15 min play, 5 min form.

#### Pre-Test

1. How often do you play 2D games?
   - [ ] Daily
   - [ ] Weekly
   - [ ] Monthly
   - [ ] Rarely
   - [ ] Never

2. What input device are you using?
   - [ ] Keyboard
   - [ ] Controller
   - [ ] Touch
   - [ ] Other: ___

3. Any accessibility needs we should know about? *(open text)*

#### Play Instructions

Play the game from the start. Stop after approximately 10 minutes or when you feel like stopping — whichever comes first. Don't look anything up. Play as you naturally would.

#### Post-Test

1. At what point (if any) did you almost stop playing? *(open text)*

2. What was the first thing you tried to do? *(open text)*

3. Was there a moment where you didn't know what to do next?
   - [ ] No, it was always clear
   - [ ] Yes, once or twice
   - [ ] Yes, frequently
   - If yes, describe the moment(s): ___

4. In your own words, what is this game about? *(open text)*

5. What mechanic or feature stood out the most? *(open text)*

6. Was there anything that annoyed or frustrated you? *(open text)*

7. On a scale of 1-10, how likely are you to keep playing? ___

8. Why did you give that rating? *(open text)*

9. Anything else you want to mention? *(open text)*

---

### Template B: Core Loop Test

> **Purpose:** Evaluate the central gameplay loop. Is it satisfying? Does it have depth?
> **Duration:** 20-30 min play, 10 min form.

#### Pre-Test

1. How familiar are you with [genre] games?
   - [ ] Very — I play them regularly
   - [ ] Somewhat — I've played a few
   - [ ] Not at all — this is new to me

2. Name 1-3 games in this genre you've enjoyed: ___

#### Play Instructions

Play through levels [X] through [Y]. Focus on the core gameplay — [describe: e.g., "combat encounters," "puzzle solving," "platforming challenges"]. Play at your own pace.

#### Post-Test

1. Describe the core gameplay in your own words. *(open text)*

2. What did you spend most of your time doing? *(open text)*

3. Which of these words describe the gameplay? (check all that apply)
   - [ ] Satisfying
   - [ ] Repetitive
   - [ ] Challenging
   - [ ] Frustrating
   - [ ] Relaxing
   - [ ] Confusing
   - [ ] Rewarding
   - [ ] Boring
   - [ ] Surprising
   - [ ] Unfair
   - [ ] Smooth
   - [ ] Clunky

4. Was there a moment that felt particularly good or rewarding? Describe it. *(open text)*

5. Was there a moment that felt bad or frustrating? Describe it. *(open text)*

6. Did you feel like you were getting better at the game over time?
   - [ ] Yes, clearly
   - [ ] Somewhat
   - [ ] Not really
   - [ ] I got worse / more confused

7. Did anything feel like busywork — something you had to do but didn't enjoy? *(open text)*

8. How did the difficulty feel?
   - [ ] Too easy
   - [ ] Just right
   - [ ] A bit hard but fair
   - [ ] Too hard
   - [ ] Inconsistent — some parts easy, some too hard

9. If you could change one thing about the gameplay, what would it be? *(open text)*

10. Would you want to play more of this?
    - [ ] Definitely
    - [ ] Probably
    - [ ] Maybe
    - [ ] Probably not
    - [ ] No

---

### Template C: Full Playthrough Test

> **Purpose:** Evaluate the complete experience — pacing, narrative, progression, satisfaction.
> **Duration:** Full game, 15 min form. Best for beta-stage builds.

#### Pre-Test

1. Gaming experience level:
   - [ ] Casual (a few hours/week)
   - [ ] Regular (most days)
   - [ ] Hardcore (daily, multiple genres)

2. Have you played a previous build of this game?
   - [ ] No, first time
   - [ ] Yes, an earlier version

#### Post-Test

1. How long did it take you to finish? ___

2. Did you finish?
   - [ ] Yes, completed the game
   - [ ] No, stopped at: ___
   - If you stopped, why?
     - [ ] Got stuck
     - [ ] Got bored
     - [ ] Ran out of time
     - [ ] Technical issue
     - [ ] Other: ___

3. Rate the following (1 = Very Poor, 5 = Excellent):

   | Aspect | 1 | 2 | 3 | 4 | 5 |
   |--------|---|---|---|---|---|
   | Controls / input feel | | | | | |
   | Visual clarity | | | | | |
   | Audio / music | | | | | |
   | Difficulty curve | | | | | |
   | Pacing | | | | | |
   | UI / menus | | | | | |
   | Tutorial / onboarding | | | | | |
   | Overall fun | | | | | |

4. What was the best part of the game? *(open text)*

5. What was the worst part of the game? *(open text)*

6. Was there a point where the game dragged or felt padded? *(open text)*

7. Did the game ever feel unfair? When? *(open text)*

8. Were there any bugs or glitches? Describe them. *(open text)*

9. Did you ever feel lost — unsure where to go or what to do? When? *(open text)*

10. How would you describe this game to a friend? *(open text)*

11. What would make you recommend this game to someone? *(open text)*

12. Final thoughts — anything else? *(open text)*

---

### Template D: UX & Controls Test

> **Purpose:** Focused test on controls, UI, and general usability.
> **Duration:** 15-20 min play, 10 min form.

#### Pre-Test

1. Input device:
   - [ ] Keyboard + Mouse
   - [ ] Controller (which? ___)
   - [ ] Touch
   - [ ] Other: ___

2. Do you typically remap controls in games?
   - [ ] Always
   - [ ] Sometimes
   - [ ] Never

#### Play Instructions

Play through levels [X] through [Y]. Pay attention to how the game *feels* to control. Don't worry about completing everything — focus on the experience of interacting with the game.

#### Post-Test: Controls

1. Did the controls feel natural?
   - [ ] Yes, immediately intuitive
   - [ ] Took a minute but made sense
   - [ ] Confusing at first but I got it
   - [ ] Never felt right

2. Were there any actions you tried to perform but couldn't? *(open text)*

3. Were there any buttons/keys that felt wrong or surprising? *(open text)*

4. Did you try to remap any controls? What would you change? *(open text)*

5. Rate the "feel" of each action (1 = Bad, 5 = Great):

   | Action | 1 | 2 | 3 | 4 | 5 | N/A |
   |--------|---|---|---|---|---|-----|
   | Moving | | | | | | |
   | Jumping | | | | | | |
   | Attacking | | | | | | |
   | Dashing/dodging | | | | | | |
   | Menu navigation | | | | | | |
   | Interacting with objects | | | | | | |

#### Post-Test: UI & Menus

6. Was all important information (health, ammo, objectives) visible and clear?
   - [ ] Yes
   - [ ] Mostly
   - [ ] Some things were hard to read/find
   - [ ] No, I missed important information

7. Did you understand what all the UI elements meant?
   - [ ] Yes, all of them
   - [ ] Most of them
   - [ ] Some were confusing
   - Which ones? ___

8. Was any text too small, too fast, or hard to read? *(open text)*

9. Were menus easy to navigate?
   - [ ] Yes
   - [ ] Mostly
   - [ ] No — describe the problem: ___

10. Game Usability Scale — Rate 1 (Strongly Disagree) to 5 (Strongly Agree):

    1. I think I would like to play this game again.
    2. I found the game unnecessarily complex.
    3. I thought the game was easy to pick up.
    4. I think I would need help from another person to understand this game.
    5. I found the various mechanics were well integrated.
    6. I thought there was too much inconsistency in this game.
    7. I imagine that most people would learn this game quickly.
    8. I found the game very awkward to play.
    9. I felt confident while playing.
    10. I needed to learn a lot before I could get going.

---

## Quick Reference Checklist

Use this before every external playtest session:

```
Before the Session:
  [ ] Build is stable — no known crashes
  [ ] Decided what specifically I'm testing
  [ ] Feedback form/questionnaire is ready
  [ ] Recording software is set up and tested
  [ ] Clear play instructions written (without explaining mechanics)
  [ ] Pre-test questionnaire ready

During the Session:
  [ ] Don't explain the game
  [ ] Don't hover or react visibly
  [ ] Take timestamped notes
  [ ] Let them struggle (note where)
  [ ] Recording is running

After the Session:
  [ ] Tester completes post-test form
  [ ] Thank the tester
  [ ] Review recording within 48 hours
  [ ] Log feedback in tracking spreadsheet
  [ ] Identify top 3 action items
  [ ] Follow up with tester about changes made
```

---

## Related Docs

- [G30 — Game Feel Tooling](../../monogame-arch/guides/G30_game_feel_tooling.md) — Make your controls feel good *before* testing, so feedback is about design, not jank.
- [G61 — Tutorial & Onboarding](../../monogame-arch/guides/G61_tutorial_onboarding.md) — Structure your onboarding so testers can actually play without verbal instructions.
- [G35 — Accessibility](../../monogame-arch/guides/G35_accessibility.md) — Accessibility testing should be part of every playtest round.

---

*The best game designers aren't the ones with the best instincts. They're the ones who test the most.*
