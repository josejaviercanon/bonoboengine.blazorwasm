---
hide:
  - navigation
  - toc
---

# Universal 2D Engine Toolkit

**Build games, not frameworks.** A complete knowledge base for building 2D games with Arch ECS + PixiJS + composed libraries in C# — the Bonobo engine stack.

> **Related:** `docs/game-development/` is the curated, engine-agnostic subset of this toolkit (theory, game design, project management, AI workflow). Use it when guidance should stay engine-agnostic; use this tree when you need the Bonobo-aligned stack, reference docs, or working examples.

<div class="grid cards" markdown>

-   :material-book-open-variant:{ .lg .middle } **93 Documents**

    ---

    Guides, reference, explanations, catalogs, and production playbook — everything you need from first line of code to Steam launch.

    [:octicons-arrow-right-24: Browse the docs](#how-to-navigate)

-   :material-code-braces:{ .lg .middle } **63 Implementation Guides**

    ---

    Step-by-step guides for every 2D game system: rendering, physics, AI, UI, audio, networking, tilemaps, shaders, and more.

    [:octicons-arrow-right-24: Jump to Guides](G/G1_custom_code_recipes.md)

-   :material-rocket-launch:{ .lg .middle } **Ship Your Game**

    ---

    16 playbook documents covering pre-production through post-mortem. Checklists, pipelines, templates.

    [:octicons-arrow-right-24: Start the Playbook](Playbook/00_master_playbook.md)

-   :material-github:{ .lg .middle } **Code Examples**

    ---

    Working C# examples extracted from the guides. Scene managers, pathfinding, procedural generation, character controllers, and more.

    [:octicons-arrow-right-24: View Examples](examples/index.md) · [:octicons-play-24: Interactive Demos](examples/demos.md)

</div>

---

## The Stack

```
.NET 10 (C# 14)                  — Simulation runtime + hosts
Arch ECS (vendored source)       — Entity Component System for all game objects
PixiJS v8 (WebGL/WebGPU)         — 2D rendering, sprites, filters, text
Blazor + Tailwind CSS v4         — UI components, HUD, menus
System.Text.Json (source gen)    — AOT-safe serialization / delta frames
Vite + TypeScript                 — Asset pipeline → wwwroot/dist
Game.Web / Game.Maui             — Blazor Web App + MAUI Hybrid hosts
```

## How to Navigate

This documentation follows the [Diátaxis framework](https://diataxis.fr/):

| Category | When to use |
|----------|-------------|
| **Reference** (R) | While coding — "what's the API?" |
| **Explanation** (E) | Before coding — "why this approach?" |
| **Guides** (G) | During coding — "how do I build this?" |
| **Catalog** (C) | During planning — "what does my game need?" |
| **Playbook** (P) | During production — "how do I ship this?" |

## Quick Start

1. **Understand** → [E1 Architecture Overview](E/E1_architecture_overview.md)
2. **Install** → [R1 Library Stack](R/R1_library_stack.md)
3. **Structure** → [R3 Project Structure](R/R3_project_structure.md)
4. **Build** → [G1 Custom Code Recipes](G/G1_custom_code_recipes.md)
5. **Pick your genre** → [C1 Genre Reference](C/C1_genre_reference.md)
6. **Ship it** → [P0 Master Playbook](Playbook/00_master_playbook.md)

---

## Support This Toolkit

This entire knowledge base — 93 docs, 63 guides, 30+ interactive demos, and working code examples — is free and will stay free. No paywalls, no gated content, no email signup walls.

If it saved you time, helped you learn something, or kept you from banging your head against an engine problem for another hour, consider dropping a small tip:

[:material-heart: Support on GitHub Sponsors](https://github.com/sponsors/sbenson2){ .md-button .md-button--primary }

**Where it goes:** Keeping the docs maintained, adding new guides as the stack evolves, and funding the mass amounts of coffee required to write about ECS architecture at 2am.

Even $1 helps — it tells me people are actually using this, which is the real motivation to keep it growing.
