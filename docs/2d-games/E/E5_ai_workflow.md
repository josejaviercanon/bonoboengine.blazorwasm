# E5 — AI-Assisted Development Workflow
> **Category:** Explanation · **Related:** [E4 Project Management](./E4_project_management.md) · [E9 Solo Dev Playbook](./E9_solo_dev_playbook.md) · [R3 Project Structure](../R/R3_project_structure.md) · [G11 Programming Principles](../G/G11_programming_principles.md) · [E8 MonoGameStudio Post-Mortem](./E8_monogamestudio_postmortem.md)

---

## The Core Principle: AI Handles the "How," You Own the "Why"

AI excels at "write a state machine for enemy behavior" but fails at "make this boss fight feel rewarding." Every technique in this document flows from that distinction. The developers getting the best results treat AI as a talented but over-eager junior developer who needs guardrails — they write lightweight specifications before prompting, invest in test coverage, and trace through every line of generated code before shipping.

Realistic expectations matter: developers who tracked their productivity with AI assistance report 10–20% gains on their first project (not the 50–60% they expected), improving over time through better workflow integration. The biggest wins come not from dramatic acceleration but from eliminating "papercut" tasks — small backlog items that individually take 15–30 minutes but collectively represent weeks of demoralizing work.

---

## Why ECS Is Uniquely AI-Friendly

Arch ECS architecture is one of the most AI-compatible patterns in game development. Components are pure data structs. Systems are pure logic functions. Each unit is self-contained, testable in isolation, and follows predictable query-iterate-transform patterns — exactly what LLMs handle best.

The highest-value AI tasks for MonoGame/Arch ECS:

- **Component struct generation** — describe a game design concept, get C# record structs
- **System scaffolding** — boilerplate for querying specific component archetypes
- **Unit test generation** — ECS systems' pure-function nature makes them highly testable
- **Documentation generation** — start a comment and receive comprehensive XML docs

The critical caveat: **MonoGame has a smaller community than Unity or Unreal**, meaning less training data for AI models. Expect more errors with MonoGame-specific APIs. Paste Arch ECS's README and key interface definitions into your LLM context — this single step dramatically improves output quality for niche frameworks.

---

## Structuring Code for AI

Vertical slice architecture is the most AI-friendly pattern. Organize by feature:

```
Features/
  Combat/
    CombatSystem.cs
    DamageEvent.cs
    ICombatService.cs
    CombatTests.cs
```

This achieves **context isolation** — AI tools can understand a self-contained feature without the entire codebase.

**Rules for AI-friendly code:**
- Keep files under 200–300 lines, one class per file, named identically to the class
- Define interfaces before implementations — AI produces dramatically better code with clear contracts
- Use explicit types over `var` so AI can read type information
- Write XML doc comments on public APIs

---

## CONTEXT.md

Create a CONTEXT.md file in your project root. Feed it to AI with every prompt:

```markdown
# Project: FireStarter
## Architecture: MonoGame + Arch ECS + Composed Libraries
## Patterns: Service Locator for ambient services, DI for game logic
## Arch owns: ALL entities (player, NPCs, enemies, particles, simulation)
## Key Libraries: Apos.Input, Gum.MonoGame, FontStashSharp, BrainAI
## Custom Code: Scene manager, render layers, SpatialHash, tweens
## Coding conventions: C# 12, nullable enabled, readonly structs for data
```

Update this after every major architectural change. AI will generate code that drifts from your architecture without this anchor.

---

## CLAUDE.md for Claude Code

If using Claude Code, create a `CLAUDE.md` in your project root with concise, universally-applicable rules. Key principles from community best practice:

- **Less is more** — Claude Code's system prompt already contains ~50 instructions. Your CLAUDE.md should be as lean as possible (~25–50 additional instructions max)
- **Use progressive disclosure** — don't inline everything; tell Claude where to find information (`See docs/engine_toolkit/ for architecture reference`)
- **Don't duplicate linter work** — use `.editorconfig` and `dotnet format` for style enforcement, not CLAUDE.md instructions
- **Document workflows, not trivia** — branch naming, test commands, deployment steps, and architectural boundaries
- **Iterate over time** — use `#` to add rules when Claude repeatedly makes mistakes; delete rules that aren't pulling their weight

Structure the file with clear markdown headers to prevent instruction bleeding between sections. Include: project context (one-liner), architecture boundaries (what AI can/cannot modify), commands (build, test, publish), and file boundaries (what to read, what not to touch).

---

## What AI Is Good At (Use It For)

- **Boilerplate:** Component classes, interface implementations, data models
- **Test generation:** Unit tests for deterministic systems (damage calc, state machines)
- **Documentation:** XML doc comments, README sections
- **Data file templates:** JSON level definitions, item databases, wave configurations
- **Exploring unfamiliar APIs:** "How do I use Arch command buffers?"
- **Refactoring:** Extracting interfaces, splitting god classes, renaming
- **Pattern implementation:** Give it a pattern description, get a concrete implementation
- **Papercut bug fixes:** Small backlog items that are tedious but straightforward
- **Design brainstorming:** Describe a system's intended behavior in natural language, have AI propose a component+system decomposition, then critique and refine together before implementing

> **Deep dive:** [E9 Solo Dev Playbook](./E9_solo_dev_playbook.md) — realistic productivity data (10–20% gains), ECS-specific AI synergies, cognitive atrophy risk, brainstorming as top non-code use

---

## What AI Is Bad At (Write It Yourself)

- Core game loop and fixed timestep integration → [G15](../G/G15_game_loop.md)
- Game feel tuning (but AI **can** scaffold the tooling that helps you tune) → [G30](../G/G30_game_feel_tooling.md)
- Physics and collision resolution edge cases → [G3](../G/G3_physics_and_collision.md)
- State machine transitions with subtle timing requirements
- Performance-critical inner loops (measure, don't trust AI's optimization instincts)
- Anything involving your game's unique "feel" — jump arcs, attack timing, camera behavior
- Complex multi-step architecture decisions
- Shader hot paths without manual profiling
- Preserving creative distinctiveness — AI flattens creative decisions toward the median

> **Deep dive:** [E9 Solo Dev Playbook](./E9_solo_dev_playbook.md) — AI art pipeline (70/30 rule, img2img workflow, LoRA training, ComfyUI), "AI slop" reputational risk

---

## AI Code Review Checklist

AI-generated code has systematic failure patterns. Check every piece for:

1. **Hallucinated APIs** — methods/classes that don't exist in your libraries
2. **Performance anti-patterns** — O(n²) where O(n) exists, LINQ in hot paths, string concatenation in loops → [G13](../G/G13_csharp_performance.md)
3. **Missing edge cases** — null inputs, empty collections, boundary values, integer overflow
4. **Incorrect error handling** — swallowing exceptions, catching too broadly
5. **Stale patterns** — using obsolete APIs from older .NET versions
6. **Memory leaks** — event handlers not unsubscribed, async tasks not cancelled → [G13](../G/G13_csharp_performance.md)
7. **Thread safety assumptions** — AI often ignores concurrency concerns
8. **ECS-specific failures** — modifying entities during iteration, incorrect query components, missing command buffer usage, structural changes without deferred execution

---

## Workflow

1. Write the interface/contract yourself
2. Ask AI to implement it, providing CONTEXT.md and relevant files
3. Review output against the checklist above
4. Run it — verify behavior matches intent
5. Commit immediately after each successful chunk
6. Schedule regular refactoring sprints — AI generates "good enough" not optimal

**Spend 5 minutes reviewing for every 1 minute of generation.** Technical debt accumulates faster with AI assistance because AI code is locally correct but globally incoherent — it doesn't know your architectural vision.

---

## Avoiding AI Slop: Art Pipeline

The term "AI slop" describes content that looks polished on the surface but lacks depth, originality, or coherent artistic vision. Steam has a curator page flagging AI-generated games, itch.io enforces a "No Slop" policy, and games have been shut down within days of announcement due to AI art backlash. Raw AI art in a final product is a reputational risk for indie developers.

### The 70/30 Rule

AI handles ~70% of initial grunt work (base compositions, color exploration, rough layouts). Humans contribute the critical 30% that gives art soul — details, storytelling, emotional weight, intentional imperfection.

### Iterative img2img Workflow

1. **Rough sketch by hand** — even a crude shape establishes human creative direction
2. **Feed into Stable Diffusion img2img** at 0.7–0.8 denoising strength
3. **Cherry-pick the best result**, paint over unwanted elements manually
4. **Feed modified image back** at lower denoising (0.5–0.6) for refinement
5. **Repeat 2–3 times**, then do final cleanup in Aseprite

### Style Consistency

- **Train a custom LoRA** (15–30 reference images of your target style, 30–60 minutes training, trigger word in prompts) for consistent aesthetic across assets
- **ControlNet** for structural guidance: Canny for outlines, OpenPose for character poses, Tile for seamless textures
- **ComfyUI** node-based interface for building reproducible pipelines

### Budget Reality

AI art generation is never the hard part — post-processing is. Budget 50%+ of art time for manual refinement. The games that ship without backlash are the ones whose AI contribution is invisible in the final product.

Target specs for 3/4 perspective: 16×16 pixel tiles, 480×270 native resolution scaled 4×, characters at 16×32 pixels. See [G28](../G/G28_top_down_perspective.md).

---

## The Cognitive Atrophy Risk

You can fall into a loop of asking AI for code, scanning it superficially, testing it, and asking it to fix mistakes without engaging deeply. This behavior extrapolated to general AI use erodes the skills you need when AI fails on niche problems.

A CHI PLAY 2024 study of 3,091 indie dev posts found that while AI can jumpstart game design, it risks homogenizing creative output, creating additional workload in prompt engineering, and distracting from core game development.

**Countermeasures:**
- Periodically code without AI to maintain fundamentals
- Write all game-feel code by hand — this is where your game's identity lives
- Use AI for brainstorming but make the final creative call yourself
- Document your architectural decisions so AI-generated code can't silently erode them

---

## AI as a Scope Creep Amplifier

When generating a new enemy type takes minutes instead of days, the temptation to add "just one more" becomes constant. AI tools amplify both productivity and chaos. Without deliberate scope control, AI becomes a scope creep accelerator rather than a shipping accelerator.

Every AI-generated feature still needs: testing, balancing, art polish, sound, UI integration, documentation, and bug fixing. The generation step is often less than 20% of the total work. See [E4](./E4_project_management.md) for scope management techniques.

---

## Tool Recommendations

| Tool | Best For | Notes |
|------|----------|-------|
| Claude (Opus/Sonnet) | Complex reasoning, architecture discussions, code review | Better for nuanced design decisions |
| GitHub Copilot | In-editor autocomplete, boilerplate | Fast for line-by-line suggestions |
| Claude Code | Terminal-based agentic coding, multi-file changes | Uses CLAUDE.md for project context |
| Stable Diffusion + ComfyUI | Art pipeline (with LoRA + ControlNet) | Local, customizable, reproducible |
| Aseprite | Final pixel art cleanup and animation | Human touch on all final assets |

---

## The Bottom Line

AI compresses the timeline for parts that were never the bottleneck. The bottleneck is always creative direction, scope discipline, and architectural coherence. Use AI to eliminate tedium, not to replace judgment.
