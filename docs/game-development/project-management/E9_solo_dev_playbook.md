# E9 — Solo Dev Playbook: AI Tools and Project Management
> **Category:** Explanation · **Related:** [E4 Solo Project Management](./E4_project_management.md) · [E5 AI-Assisted Dev Workflow](../ai-workflow/E5_ai_workflow.md) · [E8 MonoGameStudio Post-Mortem](../../monogame-arch/architecture/E8_monogamestudio_postmortem.md)

---

**AI amplifies both productivity and chaos.** Without deliberate systems for tracking progress, maintaining creative vision, and knowing when to say no, AI becomes a scope creep accelerator rather than a shipping accelerator. This doc synthesizes community wisdom, case studies from developers like ConcernedApe and LocalThunk, and practical techniques for solo game development with AI tools.

---

## AI and ECS: Where It Works

ECS architecture is **one of the most AI-friendly patterns in game development**. Components are pure data structs. Systems are pure logic functions. Each unit is self-contained, testable in isolation, and follows predictable query-iterate-transform patterns — exactly what LLMs handle best.

**Highest-value AI tasks for MonoGame/Arch ECS:**
- **Component struct generation** — describe a game mechanic, get C# record structs
- **System scaffolding** — boilerplate for querying specific component archetypes
- **Unit test generation** — ECS systems' pure-function nature makes them highly testable
- **Documentation generation** — start a comment block and get comprehensive docs

The critical boundary: **AI handles the "how," humans must own the "why."** AI excels at "write a state machine for enemy behavior" but fails at "make this boss fight feel rewarding."

**The numbers:** A CodeRabbit analysis (December 2025) found AI-co-authored pull requests contained **1.7x more issues** than human-only code, including 3x more readability problems and 2.74x more security vulnerabilities. Treat AI as a talented but over-eager junior developer who needs guardrails. Write lightweight specifications before prompting, invest in test coverage, and trace through every line of generated code.

**MonoGame-specific caveat:** Smaller community than Unity/Unreal means less training data. Expect more errors with MonoGame-specific APIs. **Paste Arch ECS's README and key interface definitions into your LLM context** — this single step dramatically improves output quality for niche frameworks. See → [E5 CONTEXT.md](../ai-workflow/E5_ai_workflow.md#contextmd)

---

## Realistic Productivity Expectations

Productivity gains from AI are real but consistently overstated. One developer who built two games with AI assistance reported only a **10–20% productivity gain** on the first game (expecting 50–60%), improving on the second through better workflow integration.

**Where AI actually saves time:** Eliminating "papercut" tasks. One developer described Cursor fixing 27 backlog issues in a weekend sprint — tasks that individually took 15–30 minutes but collectively represented weeks of demoralizing work. The biggest gains come from batch-clearing small annoying tasks, not from dramatic acceleration of complex work.

**Best uses:** Boilerplate code, well-documented framework APIs, unit tests, code review, quick prototyping, language/pattern translation.

**Where AI actively hurts:** Niche frameworks where models lack context, complex multi-step architecture, subtle logic errors that look plausible but fail at runtime, preserving creative distinctiveness.

**Cognitive atrophy risk:** "You can get into a loop of asking AI for code, scanning it, testing it, and then asking it to fix the mistakes without engaging deeply in the problem." (Clemson University CHI PLAY 2024, studying 3,091 indie dev posts.) Periodically code without AI to maintain the skills you'll need when AI fails on niche problems.

**Brainstorming is the top non-code use.** Describe a system's intended behavior in natural language, have the AI propose a component-plus-system decomposition, critique and refine together before implementing. Use AI to break through blank-page paralysis, not to replace creative judgment.

---

## AI Art Pipeline

Raw AI art in a final product is a reputational risk. Steam has a curator page ("AI Generated Slop") that flags games, itch.io enforces a "No Slop" policy, and a Postal franchise game was **shut down within two days** of announcement due to AI art backlash. Indie developers are valued for handcrafted, personal creative voice.

### The 70/30 Rule

AI handles ~70% of initial grunt work (base compositions, color exploration, rough layouts). Humans contribute the critical 30% — details, storytelling, emotional weight, intentional imperfection. The **iterative img2img workflow:**

1. Rough sketch by hand — even crude shapes establish human creative direction
2. Feed into Stable Diffusion img2img at 0.7–0.8 denoising strength
3. Cherry-pick the best result, paint over unwanted elements manually
4. Feed modified image back at lower denoising (0.5–0.6) for refinement
5. Repeat 2–3 times, then final cleanup

### Style Consistency

**LoRA training** maintains consistent style: 15–30 reference images, 30–60 minutes training, trigger word in prompts. **ControlNet** provides structural guidance — Canny for outlines, OpenPose for character poses, Tile for seamless textures. **ComfyUI** is the gold standard for reproducible node-based pipelines.

**Bottom line:** AI art generation is never the hard part — post-processing is. **Budget 50%+ of art time for manual refinement.** The games that ship without backlash are ones where AI contribution is invisible.

---

## Goal Hierarchy and Kanban

**Kanban + vertical slicing** is the dominant approach for solo game developers. Kanban's continuous flow (Backlog → To Do → In Progress → Done) requires no sprint ceremonies, provides visual progress, and supports WIP limits that prevent context-switching overload. See → [E4 Vertical Slice Development](./E4_project_management.md#vertical-slice-development)

### The 5-Level Goal Hierarchy

| Level | What | Example |
|-------|------|---------|
| **1. Design Pillars** | 3–5 statements defining what makes the game unique | "Every system creates emergent interactions" |
| **2. Milestones** | Prototype → Demo → Early Access → Full Game | "Playable demo by month 6" |
| **3. Feature Categories** | Core Mechanics, Content, UI/UX, Audio, Art, Systems | Each classified Must-Have / Nice-to-Have / Wishlist |
| **4. Tasks** | Actionable items completable in 1–4 hours | "Add fire propagation to grass tiles" |
| **5. Bugs** | Severity tiers: game-breaking → major → minor → cosmetic | Triage weekly |

The most important insight (338-upvote Hacker News thread): **"The biggest risk of solo development is not how I manage a todo list, but that I'll build the wrong thing because I waited to get feedback."** Schedule regular "social milestones" — times to show someone your work. External feedback prevents tunnel vision.

---

## Tool Recommendations

### The Two-Tool Pattern

Most successful solo devs use exactly two complementary tools:

| Role | Options |
|------|---------|
| **Design docs & knowledge** | Obsidian (local markdown, graph view, Kanban plugin) or Notion |
| **Task execution** | Codecks (game-dev-specific), Trello, or plain text in version control |

Plus version control for code. Over-engineering the PM system is itself procrastination.

**Codecks** is purpose-built for game developers — card-based with doc cards, milestone tracking, and Discord/Steam bug collection. 50,000+ organizations use it.

**Obsidian** excels as a knowledge base. Local-first markdown files are version-controllable. Graph view reveals relationships between creative and technical notes. Limitation: plugin configuration can become its own rabbit hole.

**Plain text** is surprisingly popular among experienced developers. A `todo.txt` in version control, current tasks at top, ideas at bottom. One variant: name it `todo.diff` so the editor color-codes `+` and `-` lines.

---

## Patterns from Successful Solo Games

Studying Stardew Valley, Hollow Knight, Celeste, Undertale, Vampire Survivors, Balatro, Papers Please, and Brotato reveals consistent patterns:

**Passion projects with low expectations.** Barone began Stardew Valley to practice C# and would have been happy selling 10,000 copies (sold 50M). LocalThunk started Balatro during vacation and expected "possibly 2 copies" (sold 5M). Galante wanted 100 itch.io players for Vampire Survivors.

**No formal PM tools.** Team Cherry "barely used Trello." LocalThunk used no documented methodology. Barone worked without formal processes. What they did instead: obsessive single focus + strategic breaks. LocalThunk deliberately stopped working when his drive faded, returning refreshed two months later. Barone took a month off to make a small mobile game.

**Almost none succeeded on their first game.** Barone had "mostly unfinished" prior projects. LocalThunk had been making games for ten years. Gervraud (Brotato) shipped three games first. Cawthon made ~70 games before Five Nights at Freddy's.

**Stardew Valley was built on C#/XNA → MonoGame** — the exact tech stack described in this toolkit.

---

## Scope Creep and AI Amplification

Every case study documented scope growing beyond initial expectations. Celeste expanded from 200 planned levels to 600+. Hollow Knight's DLC became a full sequel (Silksong, still in development 7+ years later). Papers Please's 6-month estimate became 9 months.

AI amplifies this risk — when generating a new enemy type takes minutes instead of days, "just one more" is constant temptation.

### Antidotes

- **Design pillars as filter** — every new idea must support your 3–5 core pillars
- **Hard deadlines with cuts, not delays** — "Make cuts to the game to meet the deadline instead of pushing the deadline back for more content"
- **Separate "Future Ideas" document** — acknowledge ideas but defer them outside current scope
- **Intentional constraints** — limit color palette, level count, mechanic count; Toby Fox's deliberately simple pixel art let one person handle all of Undertale's visuals
- **The Vampire Survivors approach** — Galante kept a "secret roadmap" but refused to share it because "expectations create pressure"

### The Polaris Framework (Fix/Polish Phase)

Categorize everything as:
1. **Essentials** — without them the game loses its USP
2. **Baseline** — minimum for a complete game
3. **Accessories** — not necessary to ship

Within each tier: Core Mechanics → Content → Quality of Life → Polish. Reserve 20–30% of development time for pure polish and bug fixing. **No new features during polish phase.**

---

## Living Documentation

Solo development commonly spans 2–5 years. Over these timescales, you will forget why you made critical decisions unless you document them.

### The Four-Element System

1. **Living GDD** — core concept, design pillars, mechanics, target audience, art direction, feature list with priority tiers. Updated continuously, not written once and shelved. Cross out old decisions with dates when they change.

2. **Architecture Decision Records** — date, context, decision, reasoning, status. Stored in `decisions/` alongside project code. Critical for ECS: when AI generates a system six months from now, you need to know *why* your MovementSystem handles collision checking rather than a separate CollisionSystem. See → [E4 Documentation That Compounds](./E4_project_management.md#documentation-that-compounds)

3. **Weekly development notes** — brief private summary of what was accomplished, blockers, and next-week plans. Maintains continuity across sessions.

4. **Public devlog** — monthly or biweekly. Serves as external accountability, community building, and forced reflection. Start only after you have a playable prototype.

---

## Decision Journal Workflow

A decision journal is a guided process for documenting project decisions as Architecture Decision Records (ADRs). Use it when facing any significant choice — framework selection, architectural patterns, feature scope, art direction.

### The Process

1. **Identify the decision** — What's the question? What triggered it? What's the cost of deciding wrong?
2. **Explore options** — For each option, evaluate: effort, risk, alignment with design pillars, reversibility
3. **Check against design pillars** — If you have defined pillars (in `DESIGN.md` or your GDD), evaluate every option against them
4. **Decide and document** — Write an ADR file in `decisions/ADR-NNN_title.md` with context, options considered, decision, reasoning, and consequences
5. **Review periodically** — Revisit ADRs when assumptions change. Update status to Superseded when a new ADR replaces an old decision

ADR files live in `decisions/` at the project root. Format: `ADR-NNN_snake_case_title.md`. Auto-numbered by scanning existing files. Use the `/session` slash command to document decisions interactively (decision focus path).

---

## Planning Session Workflow

A planning session is a structured kickoff for development work — whether it's a weekly check-in, feature planning, or scope review.

### The Process

1. **Review current state** — Recent git activity, open decisions, blockers
2. **Define session goal** — New feature? Bug triage? Scope review? Weekly planning?
3. **Inventory progress** — What shipped since last session? Any blockers?
4. **Identify priorities** — Walk the 5-level goal hierarchy:
   - Are design pillars defined? If not, define them first
   - What milestone are we in?
   - What are the 1–3 most important tasks for the next work session?
5. **Scope check** — Review backlog and "Future Ideas." Flag scope creep vs. essential work
6. **Surface open decisions** — Any choices that need documenting? Run the decision journal for each
7. **Write session summary** — What was decided, what's next, any blockers

Use the `/session` slash command to run through this process interactively.

---

## The AI+PM Feedback Loop

AI and project management are not separate concerns — they form a feedback loop:

- **Good PM directs AI** → Clear milestones tell you *what* to ask AI to build. Design pillars filter AI suggestions. Vertical slices scope AI-generated work into shippable increments.
- **Good AI accelerates PM** → Faster delivery against milestones. Batch-clearing papercut tasks. Rapid prototyping for design validation.
- **Bad PM lets AI amplify chaos** → No scope boundaries means AI-generated features pile up unchecked. No design pillars means every AI suggestion seems worth pursuing.
- **Bad AI creates PM debt** → Unreviewed AI code introduces subtle bugs. Architectural coherence erodes. Technical debt derails the timeline.

The developers who shipped the decade's biggest indie hits — without any AI tools — did so through obsessive focus, strategic constraints, authentic creative vision, and the discipline to ship finished rather than perfect. AI doesn't change that formula. It compresses the timeline for the parts that were never the bottleneck. **The bottleneck was always creative direction, scope discipline, and the willingness to keep going.**
