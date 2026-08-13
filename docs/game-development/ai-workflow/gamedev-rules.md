# Game Development AI Rules (Engine-Agnostic)

Universal rules for AI-assisted game development. These apply regardless of engine, language, or architecture.

---

## AI Code Generation Rules

### What AI Should Generate

- **Isolated, well-scoped systems** — One system, one responsibility. Self-contained units that can be tested independently.
- **Pure logic with clear inputs and outputs** — Functions and systems where behavior is determined by parameters, not hidden state.
- **Data-driven configurations** — Constants, tuning values, lookup tables, and configuration structures that define game behavior.
- **Utility functions and helpers** — Math utilities, collision helpers, string formatters, data converters.
- **Boilerplate and repetitive patterns** — Initialization code, factory methods, serialization, event wiring.
- **Test scaffolding** — Unit test stubs, mock data generators, test harnesses.

### What AI Should NOT Generate

- **Large interconnected systems in one shot** — Never generate an entire game loop, scene manager, or multi-system feature all at once. Break it into pieces.
- **Code that depends on unseen context** — If AI does not have visibility into the full dependency chain, it should ask rather than guess.
- **Speculative architecture** — Do not generate abstract base classes, plugin systems, or generic frameworks "just in case." Build what is needed now.
- **Magic numbers without documentation** — Every constant should be named and commented with its purpose and tuning range.

### Code Generation Principles

1. **Small units, always.** Each generated piece should be under ~100 lines. If it is larger, it should be split.
2. **Build after every change.** Never accumulate multiple changes without verifying the build succeeds.
3. **One concern per generation.** If asked to add movement and collision, do movement first, build, then collision.
4. **Explicit over implicit.** Favor verbose, readable code over clever one-liners. Game code is read more than written.
5. **Name everything descriptively.** Variables, functions, classes, files — all should describe their purpose without needing comments.
6. **Respect existing patterns.** Match the code style, naming conventions, and architectural patterns already in the project.
7. **No dead code.** Do not generate commented-out alternatives, unused parameters, or placeholder methods.
8. **No premature optimization.** Write clear code first. Optimize only when profiling reveals a bottleneck.
9. **Preserve file boundaries.** When modifying a file, change only what is necessary. Do not rewrite surrounding code for style preferences.

---

## Art Pipeline Rules

These apply to any 2D or 3D game project.

### Asset Naming

- Use lowercase with underscores: `player_idle_01.png`, `sword_swing.wav`
- Prefix by category: `ui_`, `sfx_`, `bgm_`, `vfx_`, `tile_`, `char_`, `env_`
- Include variant/frame numbers as suffixes: `_01`, `_02`, `_left`, `_right`

### Sprite and Texture Rules

- Define a consistent base resolution and pixel scale for the project. All assets must conform.
- Animation frames should be uniform dimensions within a sprite sheet.
- Use power-of-two texture sizes where the engine requires it (document exceptions).
- Maintain a single source of truth for palette/color constraints if the project uses a limited palette.

### Audio Rules

- Normalize audio levels to a consistent dB target.
- SFX should be short, punchy, and trimmed of silence.
- Music loops must have clean loop points tested in-engine.
- Use consistent sample rates and bit depths across all audio files.

### File Organization

- Assets live in a clearly defined directory structure (e.g., `Content/`, `Assets/`, `resources/`).
- Separate source files (PSD, Aseprite, Audacity projects) from exported/engine-ready files.
- Version control binary assets carefully — consider LFS for large files.

---

## Project Management Rules

### Milestone Structure

- Every milestone has a **concrete deliverable** — a build that demonstrates something playable or testable.
- Milestones are time-boxed. If a milestone is slipping, cut scope, do not extend the deadline.
- Each milestone ends with a retrospective: what worked, what did not, what to change.

### Task Granularity

- Tasks should be completable in **one session** (roughly 2-4 hours of focused work).
- If a task cannot be described in one sentence, it needs to be broken down further.
- Every task has clear **done criteria** — how do you know it is finished?

### Work-in-Progress Limits

- Maximum 2 features in active development at any time.
- Finish what you started before beginning something new.
- Context switching kills productivity. Batch similar work together.

---

## Scope Control

### The Scope Watchdog Rules

1. **If it was not in the plan, it does not happen now.** New ideas go on the backlog.
2. **"Wouldn't it be cool if..." is a trap.** Acknowledge the idea, write it down, keep building what was planned.
3. **Every addition means a subtraction.** Adding a feature means cutting or deferring something else.
4. **Polish is not scope creep.** Making existing features feel good is part of the plan. Adding new features is scope creep.
5. **"Just a small thing" is never small.** Implementation + testing + polish + integration always takes 3x the estimate.

### When to Cut

- The feature does not directly serve the core game loop.
- The effort exceeds the impact on player experience.
- It requires systems that do not exist yet and are not on the critical path.
- You are building it because it is interesting to build, not because the game needs it.

---

## Task Structure

### Standard Task Format

```
## Task: {title}

**Goal:** {one sentence describing the outcome}
**Done when:** {concrete acceptance criteria}
**Estimated effort:** {small | medium | large}
**Dependencies:** {what must exist first}

### Steps
1. {step}
2. {step}
3. {step}

### Notes
{any context, gotchas, or references}
```

### Task Workflow

1. **Pick** — Select the next task from the prioritized list.
2. **Understand** — Read the task, check dependencies, review related code.
3. **Build** — Implement in small increments. Build after each increment.
4. **Test** — Verify the done criteria are met.
5. **Commit** — Clean commit with a descriptive message.
6. **Update** — Mark the task complete. Update any affected docs or session state.

---

## Documentation Rules

### What to Document

- **Architecture decisions** — Why the code is structured the way it is (ADRs).
- **System interactions** — How systems communicate and depend on each other.
- **Configuration and tuning** — What values can be tweaked and what they affect.
- **Known limitations** — What the code does NOT handle and why.

### What NOT to Document

- **Obvious code behavior** — Do not write comments that restate what the code does.
- **Temporary hacks** — Fix them instead of documenting them. If they must stay, mark with `// HACK:` and a reason.
- **Aspirational features** — Only document what exists. Future plans go in the backlog, not in code comments.

### Documentation Practices

- Keep docs close to the code they describe.
- Update docs when the code changes. Stale docs are worse than no docs.
- Use code examples in docs. Abstract descriptions without examples are useless.
- Prefer structured formats (tables, lists, templates) over prose.
