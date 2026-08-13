# E3 — Engine Alternatives Evaluated
> **Category:** Explanation · **Related:** [E1 Architecture Overview](./E1_architecture_overview.md) · [E2 Why Nez Was Dropped](./E2_nez_dropped.md) · [R1 Library Stack](../R/R1_library_stack.md)

---

## Why This Document Exists

After dropping Nez ([E2](./E2_nez_dropped.md)), the natural question was: should we adopt another MonoGame-based framework or engine instead of building a composed stack? Several alternatives exist in the MonoGame ecosystem, each with real strengths. This document records what was evaluated, the criteria used, and why "composed libraries" won.

---

## Evaluation Criteria

Every alternative was measured against these priorities, in order:

1. **Architectural flexibility** — Can the game's architecture evolve without fighting the framework?
2. **ECS support** — Does it work with Arch ECS, or does it impose its own entity model?
3. **Active maintenance** — Is it being updated for modern .NET and MonoGame versions?
4. **Swappability** — If a piece dies, can you replace just that piece?
5. **Community & docs** — Can you get help when stuck?
6. **Learning curve** — How fast can a solo dev become productive?

---

## Murder Engine

### What it is

[Murder](https://github.com/isadorasophia/murder) is a full 2D game engine built on MonoGame by Isadora White (Celeste, Earthblade). It's the most ambitious MonoGame-based engine — a complete editor with ECS, pixel-art rendering pipeline, dialogue system, save/load, and a custom ImGui-based editor.

### Strengths

- **Battle-tested architecture** — designed by someone who shipped Celeste
- **Has its own ECS** — Bang, a C# ECS with archetype-based storage
- **Full editor** — scene editor, asset pipeline, dialogue editor, animation tools
- **Pixel-art focused** — render pipeline optimized for integer scaling, palette effects
- **Active development** — regularly updated, used for real commercial projects

### Why it didn't fit

| Issue | Detail |
|---|---|
| **Brings its own ECS (Bang)** | Can't use Arch ECS without running two entity systems. The whole point of the toolkit is Arch everywhere — one entity model, one query language, one serialization path. |
| **Opinionated architecture** | Murder has strong opinions about game structure — its asset pipeline, scene format, and system lifecycle are all interconnected. Adopting Murder means adopting Murder's way. |
| **Heavy investment** | Learning Murder is like learning a new engine. For a solo dev, the time spent learning Murder's systems could be spent building game-specific systems on a simpler foundation. |
| **Not a library** | You don't `dotnet add package Murder` and compose it with other tools. You clone the repo and build inside its structure. Same monolith problem as Nez, just a more capable monolith. |
| **Pixel-art assumptions** | The rendering pipeline is optimized for pixel art. Games with non-pixel aesthetics would fight the pipeline. |

**Verdict:** Murder is impressive but it's an engine, not a library. If you're making a pixel-art game and are willing to adopt its entire architecture, it's a strong choice. For a composable toolkit built around Arch ECS, it's a mismatch.

---

## FlatRedBall

### What it is

[FlatRedBall](https://flatredball.com/) is one of the oldest MonoGame/XNA frameworks, dating back to 2006. It includes a visual editor (Glue), code generation, collision, animation, and a full project management system. It has shipped commercial games and has extensive documentation.

### Strengths

- **20 years of development** — mature, battle-tested, extensive feature set
- **Glue editor** — visual scene editing, code generation, project management
- **Collision system** — sophisticated collision with shapes, relationships, and performance optimization
- **Documentation** — tutorials, API docs, video series, active community
- **Active maintenance** — still updated for modern MonoGame versions

### Why it didn't fit

| Issue | Detail |
|---|---|
| **Code generation model** | Glue generates C# code from visual definitions. This creates generated files that are hard to reason about and don't compose well with custom ECS architecture. |
| **No ECS** | FlatRedBall uses an inheritance-based entity model (`PositionedObject` hierarchy). Mixing this with Arch ECS would mean running two entity systems — the exact problem the toolkit avoids. |
| **Heavyweight** | The full FlatRedBall stack is massive. You get a lot, but you also carry a lot — including parts you don't need and can't easily remove. |
| **Opinionated project structure** | FlatRedBall expects projects to be structured its way (Glue-managed). Breaking from that structure means losing most of the tooling benefits. |
| **Learning curve** | Despite good docs, FlatRedBall has a large API surface. Learning "the FlatRedBall way" is a significant time investment that doesn't transfer to other approaches. |

**Verdict:** FlatRedBall is a proven engine with impressive longevity. But its code-generation model and inheritance-based entities are fundamentally incompatible with an Arch ECS architecture. You'd be adopting an entirely different paradigm.

---

## Monofoxe

### What it is

[Monofoxe](https://github.com/Martenfur/Monofoxe) is a lightweight MonoGame framework that adds scene management, entity-component system, resource management, cameras, and tilemaps. It aims to be the "missing layer" between raw MonoGame and a full engine.

### Strengths

- **Lightweight** — much thinner than Nez or FlatRedBall
- **Clean API** — well-designed, easy to understand
- **Tiled support** — built-in Tiled map importing and rendering
- **Camera system** — multi-camera support with viewports
- **NuGet packages** — proper package distribution (unlike Nez's submodule approach)

### Why it didn't fit

| Issue | Detail |
|---|---|
| **Own EC model** | Monofoxe has its own Entity-Component system. Like Nez, it's not a true ECS — components contain logic (`Update()`, `Draw()`). Can't cleanly coexist with Arch. |
| **Smaller community** | Less community support and fewer examples than Nez or FlatRedBall. When you hit a problem, you're often reading source code. |
| **Feature overlap** | The features Monofoxe adds (scene management, cameras, tilemaps) are exactly the things the toolkit already handles through composed libraries + ~1,000 lines of custom code. |
| **Still a framework** | Even though it's lighter, adopting Monofoxe still means adopting its scene/entity lifecycle. The "lightweight framework" is still a framework. |

**Verdict:** Monofoxe is the closest to the "right idea" — add a thin layer over MonoGame. But its EC model conflicts with Arch ECS, and the features it provides are small enough to write yourself. The toolkit's custom glue code ([G1](../G/G1_custom_code_recipes.md)) covers the same ground in ~1,000 lines you fully control.

---

## Other Alternatives Considered

### MLEM

[MLEM](https://mlem.ellpeck.de/) is a set of MonoGame extension libraries (MLEM, MLEM.Ui, MLEM.Data, MLEM.Extended) by Ellpeck (same author as the Coroutine package).

- **Strengths:** Proper library approach (not a framework), good text formatting, non-XNB content loading, UI system, NuGet packages
- **Why partially adopted:** MLEM.Data's content loading is useful for text-heavy games. It's listed in [R1](../R/R1_library_stack.md) as Tier 2.
- **Why not adopted wholesale:** MLEM.Ui is less capable than Gum. MLEM doesn't provide ECS, physics, or AI — so you still need the rest of the composed stack anyway.

MLEM is actually a good example of the library composition philosophy done right. It's not trying to be a framework — it's a set of independent utilities. The toolkit cherry-picks what's useful.

### Noppes Engine

A smaller MonoGame framework focused on 2D pixel art games.

- **Why not adopted:** Minimal community, limited documentation, narrow focus. Not enough presence to evaluate long-term viability. The risk/reward ratio didn't justify investigation.

### Raw MonoGame (no libraries)

The purist approach: use MonoGame.Framework.DesktopGL directly and write everything from scratch.

- **Strengths:** Total control, zero dependencies, educational
- **Why not adopted:** The amount of boilerplate is staggering. A solo developer writing their own ECS, physics engine, UI framework, input system, font rendering, and sprite loading from scratch would spend months before writing any game logic. The composed library approach gets 90% of the benefit of raw MonoGame (you understand and control everything) with 10% of the effort.

The toolkit does use "raw MonoGame" for the ~1,000 lines of custom glue code. The key insight is knowing **which** things to write yourself (scene manager, render layers, tweens) and which to get from a library (ECS, physics, UI, fonts).

---

## Decision Matrix

| Criteria | Murder | FlatRedBall | Monofoxe | MLEM | Raw MonoGame | **Composed Stack** |
|---|---|---|---|---|---|---|
| **Arch ECS compatible** | ❌ (Bang) | ❌ (inheritance) | ❌ (own EC) | ✅ (no entity model) | ✅ | ✅ |
| **Swappable parts** | ❌ | ❌ | ❌ | ✅ | ✅ | ✅ |
| **Active maintenance** | ✅ | ✅ | ⚠️ | ✅ | ✅ | ✅ (per library) |
| **NuGet distribution** | ❌ (repo clone) | ⚠️ (partial) | ✅ | ✅ | ✅ | ✅ |
| **Visual editor** | ✅ | ✅ (Glue) | ❌ | ❌ | ❌ | ❌ (build when needed) |
| **Time to first game logic** | Weeks | Weeks | Days | Days | Months | **Days** |
| **Long-term flexibility** | Low | Low | Medium | High | Highest | **High** |
| **Bus factor risk** | Medium | Medium | High | Medium | None | **Low** (distributed) |
| **Learning investment** | High | High | Medium | Low | High | **Low–Medium** |
| **Suitable for solo dev** | ⚠️ | ⚠️ | ✅ | ✅ | ❌ | ✅ |

---

## Why Composed Libraries Won

The decision came down to a single realization: **every framework imposes an entity model, and that entity model isn't Arch ECS.**

Murder uses Bang. FlatRedBall uses PositionedObject inheritance. Monofoxe uses its own EC. Nez used its own EC. Running two entity systems (the framework's model + Arch) creates bridge code, mental overhead, and architectural friction that defeats the purpose of having a clean ECS.

The composed library approach is the only option that:

1. **Uses Arch ECS as the single entity model** — no bridge code, no second system
2. **Lets each piece be replaced independently** — if FontStashSharp dies, swap it; the ECS and physics keep working
3. **Stays on modern .NET** — no waiting for a framework maintainer to update
4. **Costs ~14.5 hours of upfront work** — then you own your architecture forever

### The "no editor" tradeoff

The biggest thing you give up is a visual editor. Murder and FlatRedBall both have editors; the composed stack doesn't (unless you build one — see [G29](../G/G29_game_editor.md) and [E8](./E8_monogamestudio_postmortem.md) for how that went). For many 2D games, Tiled + Aseprite + ImGui debug overlays cover 90% of what an editor provides. The remaining 10% is rarely worth the framework lock-in.

### The meta-lesson

The MonoGame ecosystem is full of talented developers building frameworks and engines. Every one of them made reasonable decisions for their use case. The issue isn't that these tools are bad — it's that adopting any one of them means adopting **all** of its decisions. For a solo developer who wants to control their architecture and use Arch ECS as the foundation, the composed library approach is the only path that doesn't compromise.

The stack described in [E1](./E1_architecture_overview.md) and [R1](../R/R1_library_stack.md) is the result of evaluating all of these alternatives and choosing the parts that work best — from multiple sources, independently swappable, unified by Arch ECS.
