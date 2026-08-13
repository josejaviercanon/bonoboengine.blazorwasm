# 12 — Common Pitfalls & Solutions

> **The graveyard of unfinished games is bigger than Steam's entire catalog.**
> This document exists so your game doesn't end up there.

Every mistake here has been made thousands of times by solo and small-team developers. They're predictable, avoidable, and often fatal to projects. Each entry includes a severity rating:

- 🔴 **Critical** — Will likely kill your project
- 🟡 **Moderate** — Will cost you weeks or months
- 🟢 **Minor** — Annoying but survivable

---

## Table of Contents

- [Scope & Planning (1–8)](#scope--planning)
- [Technical (9–16)](#technical)
- [Art & Audio (17–22)](#art--audio)
- [Production & Mental Health (23–30)](#production--mental-health)
- [Bonus Pitfalls (31–33)](#bonus-pitfalls)

---

## Scope & Planning

### 1. Starting Too Big — The "Dream Game" Trap 🔴

**What it looks like:** Your first project is an open-world RPG with crafting, multiplayer, procedural generation, and a branching narrative. You've been designing it in your head for years.

**Why it happens:** You've played hundreds of games and your taste far exceeds your current ability. The gap between "games I love" and "games I can build" feels like it shouldn't exist. It does.

**The fix:** Make 3 small games first. Seriously. A Pong clone. A platformer with 5 levels. A top-down shooter. Each one will teach you more than 6 months of planning your dream game. Your dream game is game #4 at the earliest — and by then, you'll have re-scoped it to something achievable because you'll actually understand what "achievable" means.

**Rule of thumb:** If you can't describe the core loop in one sentence, it's too big.

---

### 2. No Design Document — Building Blind 🔴

**What it looks like:** You open your editor and start coding. The game design lives entirely in your head. When someone asks what the game is about, you give a different answer each time.

**Why it happens:** Writing a design doc feels like busywork when you could be *making the game*. You think you'll remember everything. You won't.

**The fix:** Write a design document. It doesn't need to be 50 pages — even a single page with core mechanics, target audience, art style, and scope is enough to keep you honest. When scope creep whispers "what if the player could also fly?", you check the doc. Flying isn't in the doc. You move on.

📄 **See:** [Playbook 02 — Design Document](02_design_document.md)

---

### 3. Scope Creep — "Just One More Feature" 🔴

**What it looks like:** Your platformer now has a crafting system, a dialogue tree, weather effects, and a photo mode. The core jumping mechanics still feel bad.

**Why it happens:** Adding features feels like progress. It's more fun to build new things than to refine existing ones. Every feature you add makes the game feel more "real" — until none of them work well.

**The fix:**

1. **Define your MVP** (Minimum Viable Product) — the smallest version of the game that's still fun.
2. **Keep a cut list.** Every feature idea goes on the list. After MVP ships, you pick from the list. Most items on the cut list will stay cut, and that's fine.
3. **The "Does this serve the core loop?" test.** If the answer is no, it goes on the cut list.

📄 **See:** [Playbook 02 — Design Document](02_design_document.md) for scoping techniques

---

### 4. Perfectionism — Polishing Too Early 🟡

**What it looks like:** You've spent 3 weeks on the main menu. It has parallax scrolling, particle effects, and animated buttons. The game behind it has one level with placeholder art.

**Why it happens:** Polish is visible progress. It *looks* like you're making a game. And it's safe — you're not solving hard design problems, you're just making things pretty.

**The fix:** Ugly prototype first, polish last. Your first playable should look terrible. Colored rectangles, programmer font, no sound. If the game is fun with rectangles, it'll be fun with art. If it's not fun with rectangles, no amount of polish will save it.

**Mantra:** "Is the game fun yet? No? Then why am I adding screen shake?"

---

### 5. No Milestones — Working Without Deadlines 🟡

**What it looks like:** You've been "working on the game" for 18 months. When someone asks how far along you are, you say "maybe halfway?" You said that 12 months ago too.

**Why it happens:** Without deadlines, everything takes as long as it takes. Parkinson's Law is real — work expands to fill the time available. With infinite time, nothing ever finishes.

**The fix:** Set milestones with dates. Even fake deadlines work. "Playable prototype by March 1st. Three levels by June. Feature-complete by September." Write them down. Tell someone. Accountability turns vague intentions into concrete targets.

📄 **See:** [Playbook 03 — Milestones & Scheduling](03_milestones.md)

---

### 6. Skipping Pre-Production — Jumping Straight to Code 🟡

**What it looks like:** Day 1: you create the project, set up the window, and start coding player movement. By week 3 you realize the core mechanic doesn't work and you need to restructure everything.

**Why it happens:** Coding feels productive. Planning feels like stalling. You want to *see something on screen*.

**The fix:** Spend 1–2 weeks in pre-production before writing real code. Paper prototype the mechanics. Sketch the game flow. List the technical risks. Build a tiny throwaway prototype to test the riskiest assumption. Two weeks of planning saves months of rebuilding.

**Ask yourself:** "What's the riskiest part of this game? Have I proven it works?"

---

### 7. Building an Engine Instead of a Game 🔴

**What it looks like:** You're 6 months in and you've built a custom renderer, a physics engine, a level editor, and an asset pipeline. You haven't made a single level. You'll "get to the game part soon."

**Why it happens:** Engine programming is a comfort zone for technical developers. It's solvable, measurable, and satisfying. Game design is ambiguous and scary. Building tools feels like progress without the risk of your game being... not fun.

**The fix:** Use an existing engine or framework. Godot, Unity, Love2D, Raylib, SDL — pick one and make a game with it. If you genuinely need custom tech (you probably don't), build *only* what you need, *when* you need it. The engine exists to serve the game, not the other way around.

📄 **See:** [R1 — Library & Tool Stack](../R/R1_library_stack.md)

---

### 8. Not Playtesting Early Enough 🔴

**What it looks like:** You've worked on the game for a year. Finally you show it to someone. They can't figure out the controls. They don't understand the objective. They quit after 2 minutes.

**Why it happens:** You're afraid of negative feedback. You want it to be "ready" first. You know every system intimately, so it all makes sense to *you*. You've lost the ability to see the game through fresh eyes.

**The fix:** Get someone else to play your game as soon as it's interactive — even if it's just moving a rectangle around. Watch them play. Don't explain anything. The confusion you see is the real game design, not what's in your head. Playtest monthly at minimum.

**Hard truth:** If your game needs a tutorial to be understood, your game design needs work.

---

## Technical

### 9. Premature Optimization 🟡

**What it looks like:** You're writing a custom spatial hash for your game with 15 enemies on screen. You're implementing object pooling before you've confirmed you have a memory problem. You're debating cache line alignment for a 2D platformer.

**Why it happens:** Optimization feels smart. You've read about what "real" game engines do and you want to do it too. Worrying about performance is easier than worrying about whether your game is fun.

**The fix:** Profile first, optimize second. Don't guess where the bottleneck is — measure it. Most 2D games will never hit a performance wall with naive implementations. If your game runs at 60fps, stop optimizing. When you *do* hit a problem, your profiler will tell you exactly where. Fix that one thing.

📄 **See:** [G33 — Profiling & Optimization](../G/G33_profiling_optimization.md)

---

### 10. Not Using Version Control (Or Using It Wrong) 🔴

**What it looks like:** Your project folder is called `MyGame_final_v3_REAL_final_backup2`. Or you use Git but commit once a month with the message "stuff". Or you've never branched.

**Why it happens:** Version control feels like overhead for a solo project. "I'm the only one working on it, why do I need Git?" Because past-you and future-you are different people, and they disagree about everything.

**The fix:** Use Git. Commit early, commit often, write real messages. "Add double-jump mechanic" is a commit message. "Fixed stuff" is not. Learn branching for experiments — it's a safety net that lets you try wild ideas without nuking your working game. Back up to a remote (GitHub, GitLab, even a USB drive).

📄 **See:** [G44 — Version Control](../G/G44_version_control.md)

---

### 11. Hardcoding Everything 🟡

**What it looks like:** Player speed is `5.0` on line 847. Gravity is `9.8` in three different files. Enemy health is whatever you typed at 2am. Changing one value means searching the entire codebase.

**Why it happens:** It's faster to type a number than to create a constant. You'll "clean it up later." You don't.

**The fix:** Constants, config files, or a data-driven approach from day one. `PLAYER_SPEED = 5.0` at the top of the file is a start. A JSON/TOML config file is better. A live-reloading config that lets you tweak values without restarting is best. Your future self will thank you, and so will your playtesters.

**Bonus:** Data-driven design makes balancing 10x easier. You can tweak every number in one file instead of hunting through code.

---

### 12. Ignoring the Game Loop — Wrong Delta Time Handling 🔴

**What it looks like:** Your game runs beautifully on your machine. On your friend's laptop, everything moves at double speed. On a slow machine, physics breaks and players fall through floors.

**Why it happens:** You move the player `5` pixels per frame instead of `5 * dt` pixels per second. Physics is tied to framerate. Everything works at 60fps because that's what you test on.

**The fix:** Understand the game loop. Use delta time correctly. Separate your update logic from your render logic. For physics, use a fixed timestep with accumulator. This is non-negotiable — it's not an optimization, it's correctness.

📄 **See:** [G15 — The Game Loop](../G/G15_game_loop.md)

---

### 13. Rolling Your Own Everything 🟡

**What it looks like:** You wrote a custom JSON parser. You wrote your own pathfinding algorithm. You wrote a UI framework from scratch. Each one has subtle bugs that take days to fix.

**Why it happens:** Same psychology as building an engine (Pitfall #7). NIH syndrome — "Not Invented Here." You think your use case is special. It almost never is.

**The fix:** Use libraries. Use well-tested, well-documented libraries for everything that isn't your core game mechanic. JSON parsing, pathfinding, physics, UI, networking — someone has already solved these problems better than you will in a weekend.

📄 **See:** [R1 — Library & Tool Stack](../R/R1_library_stack.md)

---

### 14. No Save System Plan 🟡

**What it looks like:** Your game is 80% done. Now you need saves. Your game state is scattered across 40 objects with circular references, local variables, and runtime-generated data. Serializing it is a nightmare.

**Why it happens:** Saving feels like a "later" problem. It's not fun to implement. The game works fine without it during development because you always start from the beginning.

**The fix:** Plan your save system architecture *before* you write game logic. Decide early: what gets saved? How is state serialized? Where does it live? Design your game objects to be serializable from the start. Even if you don't implement saving until month 6, having serializable state from day 1 makes it painless.

**Minimum viable plan:** Can every piece of game state be represented as a simple data structure (numbers, strings, arrays, maps)? If yes, saving is easy. If no, fix that first.

---

### 15. Spaghetti Architecture 🟡

**What it looks like:** Your Player class has 3,000 lines. It handles input, physics, rendering, audio, inventory, dialogue, and saving. Everything references everything else. Changing one thing breaks three others.

**Why it happens:** You start small, and it grows organically. Refactoring feels like wasted time when you could be adding features. "It works, so why change it?"

**The fix:** Separate concerns from the start. You don't need a perfect architecture — you need *an* architecture. Options:
- **Component/ECS pattern:** Entities are bags of data, systems operate on them
- **Scene tree:** Nested nodes with clear parent-child relationships
- **Manager pattern:** Dedicated managers for input, audio, physics, etc.

Pick one. Be consistent. When a class exceeds ~300 lines, it's probably doing too much.

---

### 16. Platform-Specific Code Everywhere 🟢

**What it looks like:** Your input handling checks for `GLFW_KEY_A` directly. Your file paths use backslashes. Your save location is hardcoded to `C:\Users\`. Porting to Mac takes a month.

**Why it happens:** You develop on one platform and don't think about others. It works for you, so it works. Until it doesn't.

**The fix:** Abstract platform differences behind interfaces. Input, file I/O, audio, rendering — each should have a clean API that doesn't leak platform details. Most engines/frameworks already do this. If you're using raw SDL or similar, create thin wrappers early. The cost is trivial; the payoff is huge when you deploy to a second platform.

📄 **See:** [G32 — Deployment & Platform Builds](../G/G32_deployment_platform_builds.md)

---

## Art & Audio

### 17. Final Art Too Early 🟡

**What it looks like:** You have beautiful, polished sprites for a character whose moveset you haven't finalized. You redesign the mechanic, and now 40 hours of art is obsolete.

**Why it happens:** Art is the most visible part of game development. It makes screenshots look good, trailers look real, and devlogs get likes. The dopamine hit of seeing your game "look like a real game" is hard to resist.

**The fix:** Use placeholder art until mechanics are locked. Good placeholders are descriptive (a stick figure with a sword is better than a red rectangle), but they're intentionally ugly so you never get attached. Final art comes after the design is stable — which means after playtesting confirms the mechanics work.

---

### 18. Inconsistent Art Style 🟡

**What it looks like:** Your player character is 16×16 pixels. Your enemies are 32×32. Your UI uses a different pixel density. Some sprites have outlines, others don't. The color palette shifts between warm and cool depending on when you drew it.

**Why it happens:** You make art over months. Your skills improve, your preferences change, and you don't go back to update old assets. Or you grab free assets from multiple sources and they clash.

**The fix:** Define an art style guide before you start:
- **Pixel density:** Pick one base resolution (e.g., 16×16 tiles) and stick to it
- **Color palette:** Use a fixed palette (Lospec has hundreds)
- **Rules:** Outlines or no outlines? Black outlines or colored? Anti-aliased or pixel-perfect?
- **Reference sheet:** Collect 5–10 images that match your target style

When in doubt, consistency beats quality. A game with mediocre-but-consistent art looks vastly better than one with a mix of good and bad.

---

### 19. Forgetting Audio Entirely 🟡

**What it looks like:** Your game is feature-complete and visually polished. It's completely silent. You spend a panicked weekend adding sounds from freesound.org. They don't match. The game feels empty.

**Why it happens:** Audio is invisible work. No one screenshots a sound effect. It doesn't show up in devlogs. It's easy to forget that audio is ~50% of the player's emotional experience.

**The fix:** Add placeholder audio early. Even simple beeps and boops for jump, hit, collect, and death will transform how the game *feels*. Budget time for audio in your milestones. If you can't make audio yourself, plan for it — asset packs, a musician collaborator, or tools like sfxr/Bfxr for retro sounds.

**Quick wins:** Jump sound. Land sound. Hit sound. Death sound. Menu click. These 5 sounds will make your game feel 10x more alive.

---

### 20. No Placeholder Art Strategy 🟢

**What it looks like:** Half your game objects are invisible or identical colored rectangles. You can't tell enemies from pickups from hazards. Playtesting is useless because testers can't parse what they're seeing.

**Why it happens:** "I'll add real art later" becomes an excuse to show nothing now. You know what the red square is, so you forget that nobody else does.

**The fix:** Use informative placeholders:
- **Color-coded shapes:** Red = danger, green = good, blue = interactive
- **Simple icons:** A skull for enemies, a star for collectibles, an arrow for direction
- **Text labels:** Literally write "ENEMY" or "DOOR" on the sprite
- **Borrowed sprites:** Use free assets temporarily (just don't ship them)

Good placeholders communicate intent. They let you and your playtesters evaluate the game design without final art.

---

### 21. Wrong Resolution Choice 🟢

**What it looks like:** You're making a pixel art game rendered at 1920×1080 native resolution. Each pixel is 1 screen pixel. Your "pixel art" characters are 200 pixels tall and take weeks to animate.

**Why it happens:** You think higher resolution means better quality. Or you don't understand the relationship between game resolution and display resolution.

**The fix:** For pixel art, design at a low internal resolution (e.g., 320×180, 384×216, 480×270) and scale up with nearest-neighbor filtering. This gives you chunky, authentic pixels and makes art creation dramatically faster. A 16×16 character at 320×180 is viable. A 16×16 character at 1920×1080 is a speck.

**The formula:** Pick your base tile size (16px, 32px), decide how many tiles wide/tall the screen should be, and that's your internal resolution. Scale to fit the display.

---

### 22. Ignoring UI/UX 🟡

**What it looks like:** Your health bar is a tiny number in the corner. Your inventory is a wall of text. Your menus require reading a manual. Players don't know what button does what because there's no visual feedback.

**Why it happens:** You're a programmer, not a designer. UI "works" as long as the information is technically visible. You know every control because you built them.

**The fix:**
- **Watch someone play your game without instructions.** Every point of confusion is a UI failure.
- **Visual feedback for everything.** Button pressed? Flash it. Damage taken? Screen shake + flash. Item collected? Particle burst.
- **Readability first.** Big, clear text. High contrast. Don't hide critical information.
- **Study games you admire.** How do they show health? How do they teach controls? Copy their patterns — they've already done the UX research.

---

## Production & Mental Health

### 23. Working in Isolation 🟡

**What it looks like:** You've worked on your game for a year. Nobody has seen it. You haven't talked to another developer in months. You're not sure if the game is good or terrible. You suspect terrible.

**Why it happens:** Sharing unfinished work feels vulnerable. You want to reveal the game when it's "ready." Online communities feel intimidating. Negative feedback might kill your motivation.

**The fix:** Join a community. r/gamedev, indie dev Discords, local meetups, game jams — anywhere you can share work-in-progress and get feedback. Post screenshots. Post GIFs. Ask questions. You'll get encouragement, useful criticism, and proof that other people struggle with the same problems.

**The paradox:** The feedback you're avoiding is exactly what you need to make the game good.

---

### 24. Comparing to AAA 🟡

**What it looks like:** You look at your platformer and then look at Celeste. You feel bad. You look at your RPG and then look at Baldur's Gate 3. You feel terrible. You consider quitting.

**Why it happens:** You consume finished, polished games made by teams of 10–300 people over 3–7 years. Then you compare that to your solo project at month 4. The comparison is absurd but your brain makes it anyway.

**The fix:** Compare yourself to other solo devs. Look at first releases, not masterpieces. Look at game jam entries. Look at the *first version* of games that eventually became great — most were rough. Celeste started as a 4-day game jam entry. Your competition isn't AAA. Your competition is "did I finish a game?"

---

### 25. No Marketing Until Launch 🟡

**What it looks like:** You finish the game. You put it on itch.io. Nobody comes. You tweet about it. 3 likes. You wonder why nobody cares about your game that nobody knew existed.

**Why it happens:** Marketing feels gross. You're a developer, not a salesperson. You'll "worry about marketing later." Later arrives and you have zero audience, zero wishlists, zero momentum.

**The fix:** Start sharing the moment you have something to show. Devlog posts, screenshots, GIFs, short clips — drip-feed your progress. Build an audience *during* development, not after. By launch day, hundreds (or thousands) of people should already know your game exists. Even a small following makes launch day feel like an event instead of shouting into the void.

**Minimum viable marketing:** A Twitter/social account for the game. One post per week showing progress. That's it.

---

### 26. Burnout from Crunch 🔴

**What it looks like:** You work on the game every evening after your day job. Weekends too. You skip social events. You can't stop thinking about the game. After 6 months, you're exhausted and resentful. You stop working on it entirely.

**Why it happens:** Passion projects don't have HR departments. Nobody tells you to stop. The excitement of early development masks the unsustainable pace. You think crunch is temporary. It becomes permanent.

**The fix:** Set a sustainable schedule and protect it. 1–2 hours per weekday, a few hours on weekends. Take days off. Take *weeks* off. The game will still be there. A project that takes 18 months at a sustainable pace ships. A project that burns you out in 6 months doesn't ship at all.

**Remember:** You're making a game because you love making games. If the process makes you miserable, something is wrong with the process, not with you.

---

### 27. Feature Envy — Copying Every Game You Play 🟢

**What it looks like:** You play Hades and add a roguelike meta-progression system to your platformer. You play Stardew Valley and add farming. You play Hollow Knight and add a map system. Your game is now an incoherent Frankenstein.

**Why it happens:** Great games inspire you, and inspiration feels like a good idea. "My game would be so much better with *this*." Maybe. But probably not if "this" doesn't serve your core design.

**The fix:** Play games deliberately. Note what you like and *why* it works — in the context of *that* game. Then ask: does this mechanic serve *my* game's core loop? If not, it goes on the cut list (Pitfall #3). Inspiration should inform your design philosophy, not your feature list.

---

### 28. Not Finishing 🔴

**What it looks like:** You have a folder of 12 unfinished projects. Each one has a solid prototype. None of them have a title screen, a game over screen, or a way to win. You keep starting new projects because new projects are exciting and old projects are hard.

**Why it happens:** The last 20% of a game — menus, polish, bug fixing, edge cases, packaging — is the least exciting and most tedious work. Starting a new project gives you the same dopamine as the first week of the current one. The grass is always greener in the new project folder.

**The fix:** Finish something. Anything. A game jam game. A clone. A game with one level and one mechanic. The skills you learn in the last 20% — shipping, packaging, handling edge cases, saying "it's done" — are skills you can *only* learn by finishing. A finished bad game teaches you more than 10 unfinished good ones.

**Hard truth:** If you've never finished a game, you don't yet know how to make games. You know how to start games. Those are different skills.

---

### 29. Ignoring Accessibility 🟢

**What it looks like:** Your game requires fast reflexes, distinguishes red from green for gameplay-critical information, has tiny text, and has no input remapping. ~15% of your potential audience can't play it.

**Why it happens:** You test the game with your own eyes, hands, and reflexes. You don't think about players who are different from you. Accessibility feels like a niche concern — it's not.

**The fix:** The easy wins cost almost nothing:
- **Remappable controls** — let players choose their inputs
- **Colorblind-friendly palettes** — don't rely on red/green distinction alone; use shapes or patterns too
- **Scalable UI text** — not everyone has 20/20 vision
- **Adjustable game speed** — Celeste's assist mode didn't make the game worse for anyone
- **Subtitles** — if you have dialogue, subtitle it

You don't have to make the game playable by everyone. But the low-hanging fruit is embarrassingly easy to implement and meaningfully expands your audience.

📄 **See:** [G35 — Accessibility](../G/G35_accessibility.md)

---

### 30. No Post-Mortem 🟢

**What it looks like:** You finish (or abandon) the game. You immediately start the next one. Six months later, you make the same mistakes.

**Why it happens:** Post-mortems feel like navel-gazing. The project is over — why look back? Because you're about to repeat every mistake you refuse to examine.

**The fix:** After every project (finished or not), write a post-mortem:
- **What went well** — do more of this
- **What went poorly** — avoid this next time
- **What surprised you** — the gaps in your self-knowledge
- **Key metrics** — how long did it take? How does that compare to your estimate?

Keep it short. One page is fine. Read it before starting your next project. You'll be shocked at how much you repeat without this practice.

---

## Bonus Pitfalls

### 31. Tutorial Hell 🟢

**What it looks like:** You've watched 200 hours of YouTube tutorials. You can follow along with any of them. You can't build anything without a tutorial open.

**Why it happens:** Tutorials are comfortable. Someone else makes the decisions. You feel like you're learning because you're typing code. But following instructions isn't the same as problem-solving.

**The fix:** One tutorial to learn the basics, then build something without a tutorial. Get stuck. Google the specific problem. Solve it. That's how you actually learn. The discomfort of not knowing what to do next is where all the real learning happens.

---

### 32. Neglecting the "Game Feel" 🟡

**What it looks like:** Your platformer technically works. The character moves, jumps, and interacts with platforms. But it feels like controlling a cardboard box on ice. Players can't articulate what's wrong — it just doesn't feel *good*.

**Why it happens:** Game feel is the sum of dozens of tiny details: acceleration curves, jump buffering, coyote time, screen shake, hit pause, animation timing. None of them are in any tutorial's "make a platformer in 30 minutes" video.

**The fix:** Study game feel deliberately. Play games you love and ask *how* the movement feels good, not just *what* the character does. Implement the small touches: input buffering, variable jump height, subtle camera lag, animation anticipation frames. Each one is simple; together they transform the experience.

---

### 33. Solo Hero Complex — Refusing Help 🟡

**What it looks like:** You insist on doing everything: code, art, music, sound, marketing, QA, writing, localization. You're mediocre at most of them but won't collaborate because it's "your vision."

**Why it happens:** Control feels safe. Collaboration requires communication, compromise, and trust. Asking for help feels like admitting weakness.

**The fix:** You don't have to do everything. Revenue share, asset packs, freelance musicians, open-source tools, community playtesters — the indie ecosystem is built for collaboration. Focus your energy on what you're best at and find help for the rest. A game with great code and commissioned art ships faster and better than a game where you learned to draw "well enough."

---

## Quick Reference: Severity Summary

| Severity | Pitfalls |
|----------|----------|
| 🔴 **Critical** | #1 Starting too big, #2 No design doc, #3 Scope creep, #7 Building an engine, #8 No playtesting, #10 No version control, #12 Wrong game loop, #26 Burnout, #28 Not finishing |
| 🟡 **Moderate** | #4 Perfectionism, #5 No milestones, #6 Skipping pre-production, #9 Premature optimization, #11 Hardcoding, #13 Rolling your own, #14 No save plan, #15 Spaghetti code, #17 Final art too early, #18 Inconsistent art, #19 Forgetting audio, #22 Ignoring UI/UX, #23 Working in isolation, #24 Comparing to AAA, #25 No marketing, #32 Neglecting game feel, #33 Solo hero complex |
| 🟢 **Minor** | #16 Platform-specific code, #20 No placeholder strategy, #21 Wrong resolution, #27 Feature envy, #29 Ignoring accessibility, #30 No post-mortem, #31 Tutorial hell |

---

## Cross-References

| Topic | Toolkit Document |
|-------|-----------------|
| Design Documents | [Playbook 02](02_design_document.md) |
| Milestones & Scheduling | [Playbook 03](03_milestones.md) |
| Game Loop & Delta Time | [G15](../G/G15_game_loop.md) |
| Deployment & Platforms | [G32](../G/G32_deployment_platform_builds.md) |
| Profiling & Optimization | [G33](../G/G33_profiling_optimization.md) |
| Accessibility | [G35](../G/G35_accessibility.md) |
| Version Control | [G44](../G/G44_version_control.md) |
| Library & Tool Stack | [R1](../R/R1_library_stack.md) |

---

> **The single most important skill in game development is finishing.**
> Every pitfall on this list is, ultimately, something that stops you from finishing.
> Recognize them, avoid them, and ship your game.
