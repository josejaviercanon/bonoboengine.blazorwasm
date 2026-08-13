# G — Stitch UI Workflow for Game Development

> **Category:** Guide · **Engine:** Engine-Agnostic · **Related:** [G5 UI Framework (MonoGame)](../../monogame-arch/guides/G5_ui_framework.md) · [P5 Art Pipeline](../project-management/P5_art_pipeline.md) · [C1 Genre Reference](C1_genre_reference.md)

> How to use Google Stitch — a free AI-native design canvas — to prototype game UI before implementation. Covers prompt engineering for game interfaces, export pipelines, and translation to MonoGame and Godot UI systems.

---

## Table of Contents

1. [What Is Google Stitch?](#1-what-is-google-stitch)
2. [March 2026 Update — The AI-Native Canvas](#2-march-2026-update--the-ai-native-canvas)
3. [Why Use Stitch for Game UI?](#3-why-use-stitch-for-game-ui)
4. [Game UI Prompt Engineering](#4-game-ui-prompt-engineering)
5. [Prototyping Core Game Screens](#5-prototyping-core-game-screens)
6. [Prototyping Game Flows](#6-prototyping-game-flows)
7. [Voice Canvas for Rapid Iteration](#7-voice-canvas-for-rapid-iteration)
8. [Vibe Design for Game Aesthetics](#8-vibe-design-for-game-aesthetics)
9. [DESIGN.md for Game Style Systems](#9-designmd-for-game-style-systems)
10. [Export Pipeline — Stitch to Game Engine](#10-export-pipeline--stitch-to-game-engine)
11. [Integration with MonoGame](#11-integration-with-monogame)
12. [Integration with Godot](#12-integration-with-godot)
13. [Advanced Workflows](#13-advanced-workflows)
14. [Limitations & What Stitch Is Not](#14-limitations--what-stitch-is-not)
15. [Best Practices](#15-best-practices)
16. [Quick Reference — Prompt Templates](#16-quick-reference--prompt-templates)

---

## 1. What Is Google Stitch?

[Google Stitch](https://stitch.withgoogle.com/) is an AI-powered UI design tool built by Google Labs. You describe an interface in natural language — via text prompt, voice, image upload, or sketch — and Stitch generates high-fidelity user interface designs complete with clean HTML and CSS code.

**Key facts:**

- **Free** — No subscription, no credits, no waitlist. Sign in with a Google account.
- **Browser-based** — Runs entirely at [stitch.withgoogle.com](https://stitch.withgoogle.com/). No downloads.
- **Powered by Gemini** — Choose between Gemini 2.5 Flash (fast), Gemini 2.5 Pro (high fidelity), or Gemini 3 (best contextual understanding).
- **Outputs real code** — Every generated design includes exportable HTML/CSS.
- **Interactive prototypes** — Connect screens into clickable flows and preview them.
- **Export integrations** — Send designs to Figma, AI Studio, Antigravity, or export raw code.
- **MCP server & SDK** — Stitch has its own [MCP server](https://stitch.withgoogle.com/docs/mcp/setup/) and [SDK](https://github.com/google-labs-code/stitch-sdk) for tool integration.

### Why Game Developers Should Care

Game UI is one of the most time-consuming parts of development that isn't core gameplay. You need:
- Main menus, settings screens, pause menus
- HUDs (health, mana, score, minimap, hotbar)
- Inventory systems, shops, crafting interfaces
- Dialogue boxes, quest logs, journals
- Game over screens, leaderboards, achievement popups

Traditionally you either wireframe by hand, mock up in Figma, or just code blindly and iterate. Stitch collapses the mockup step to seconds: describe what you want, get a visual design, extract the layout information, and implement it in your engine.

---

## 2. March 2026 Update — The AI-Native Canvas

The March 19, 2026 update transformed Stitch from a text-to-UI generator into a full AI-native design platform. Key additions:

### Infinite Canvas

The workspace is now an infinite canvas where ideas can grow from sketches to working prototypes without switching tools. You can place multiple screen designs, reference images, text notes, and code snippets all on the same canvas. This is perfect for game UI work where you're designing 8-12 connected screens simultaneously.

### Voice Canvas

Speak directly to your canvas using voice interaction powered by Gemini Live. The AI agent:
- Listens to design descriptions and generates UI in real-time
- Asks clarifying questions ("Should the inventory grid be scrollable?")
- Gives real-time design critiques ("The health bar might be hard to read against dark backgrounds")
- Makes live updates as you talk ("Now make it wider... add a tooltip... change the font to something more medieval")

### Vibe Design

Instead of specifying exact components, describe the *feeling* you want:
- "A dark, gritty RPG inventory that feels like Diablo"
- "A cozy, warm shop interface for a farming sim"
- "A retro arcade scoreboard with CRT scan lines"

Stitch generates multiple design directions matching that vibe, letting you explore broadly before committing to a specific look.

### Direct Edits

Manually tweak text, swap images, and adjust details right inside Stitch — no more regenerating the entire design for a typo fix.

### Design Agent & Agent Manager

A Design Agent tracks your entire project history and reasons through different versions. The Agent Manager lets you explore multiple design directions simultaneously without losing any branch — perfect for A/B testing different HUD layouts.

### DESIGN.md

An agent-friendly markdown file for exporting/importing design rules. Extract a design system from any URL, or define your own and apply it across projects. This is game-changing for maintaining visual consistency across all your game screens.

---

## 3. Why Use Stitch for Game UI?

### The Problem

Game UI iteration is slow. The traditional cycle:
1. Sketch layout on paper or whiteboard
2. Mock up in Figma/Photoshop (hours)
3. Implement in engine (hours)
4. Playtest → realize the layout doesn't work
5. Go back to step 1

### The Stitch Shortcut

With Stitch, steps 1-2 collapse into seconds:
1. Describe your UI in a prompt (30 seconds)
2. Get multiple design variants instantly
3. Iterate with voice or text refinements (minutes)
4. Extract layout metrics from the generated HTML/CSS
5. Implement in engine with exact specifications
6. Playtest → refine in Stitch → re-implement

### What Stitch Does Well for Games

| Strength | Game UI Application |
|---|---|
| **Layout composition** | Grid-based inventories, panel arrangements, HUD element positioning |
| **Color palette generation** | Consistent UI color schemes per game aesthetic |
| **Typography hierarchy** | Title screens, dialogue boxes, stat displays |
| **Component repetition** | Item slots, skill trees, achievement grids |
| **Responsive design** | Testing UI at different resolutions (mobile vs desktop) |
| **Multi-screen flows** | Menu → Settings → Gameplay → Pause → Game Over |
| **Visual consistency** | Applying a design system across 10+ screens |

### What Stitch Does NOT Do

- Generate actual game engine UI code (no GDScript Control nodes, no MonoGame Gum layouts)
- Render at pixel-art resolutions (it generates web-scale UI)
- Handle real-time gameplay interactions (hover states, drag & drop, animation)
- Replace your game's rendering pipeline
- Generate sprite-based UI art (pixel art buttons, 9-slice panels)

**Stitch is a design/prototyping tool, not a game engine UI builder.** You use it to figure out *what* your UI looks like, then you implement *how* it works in your engine.

---

## 4. Game UI Prompt Engineering

The quality of Stitch output depends entirely on your prompt. Game UI prompts need specific elements that web/app prompts don't.

### Anatomy of a Good Game UI Prompt

```
[Art Style] + [Screen Type] + [Layout Details] + [Color/Theme] + [Specific Elements]
```

### Key Prompt Elements for Game UI

**1. Art style first** — This is the most impactful part:
- "pixel art style" / "16-bit retro"
- "dark fantasy" / "gothic"
- "clean minimalist" / "flat design"
- "hand-drawn sketch" / "watercolor"
- "sci-fi holographic" / "neon cyberpunk"
- "cozy cartoon" / "Studio Ghibli inspired"

**2. Screen purpose** — Be specific about what the screen does:
- "inventory management screen" not just "inventory"
- "in-game HUD overlay" not "HUD"
- "main menu with play, settings, credits, and quit buttons"

**3. Layout specifications** — Numbers help:
- "6x4 grid of item slots"
- "health bar in top-left, minimap in top-right"
- "three-column layout: stats | equipment | inventory"
- "bottom-anchored hotbar with 8 slots"

**4. UI components** — Name exact elements:
- "tooltip popup showing item name, description, stats, and rarity"
- "tab navigation: Weapons | Armor | Consumables | Quest Items"
- "scrollable quest log with checkboxes"

**5. Theme/mood** — Guide the visual treatment:
- "dark background with gold accents"
- "parchment texture behind text"
- "glowing borders on selected items"
- "semi-transparent overlay that doesn't obscure gameplay"

### Example Prompts by Screen Type

#### Main Menu
```
Dark fantasy main menu screen for a 2D action RPG. Centered logo placeholder 
at top. Four menu buttons stacked vertically: "New Game", "Continue", 
"Settings", "Quit". Subtle particle effects suggested. Background is a 
moody castle silhouette. Buttons have ornate gold borders with hover glow 
effect. Font style: medieval serif.
```

#### In-Game HUD
```
Minimal in-game HUD overlay for a 2D platformer. Top-left: heart-based 
health (5 hearts). Top-right: coin counter with icon. Bottom-center: 
hotbar with 4 ability slots (1-2-3-4 keybinds shown). Top-center: 
level name in small text. All elements semi-transparent. Clean pixel 
art style, light on dark.
```

#### Inventory Screen
```
Pixel art RPG inventory screen with a 6x4 grid of item slots. Left 
panel: character paper doll with equipment slots (head, chest, legs, 
weapon, shield, accessory). Right panel: item grid with scroll. Bottom: 
item tooltip showing name, icon, description, stats comparison (green 
for better, red for worse), and "Equip" / "Drop" buttons. Color scheme: 
dark brown parchment background, gold text, warm amber accents.
```

#### Settings Screen
```
Game settings menu with tabbed navigation: Video | Audio | Controls | 
Gameplay. Video tab shown: resolution dropdown, fullscreen toggle, 
VSync toggle, brightness slider with preview bar, particle quality 
(Low/Medium/High). Bottom row: "Apply", "Reset to Default", "Back" 
buttons. Clean UI, dark theme with blue accent color.
```

#### Dialogue Box
```
RPG dialogue box at bottom of screen, taking up bottom 30% of viewport. 
Left side: character portrait (square, 128x128 placeholder). Right side: 
character name in header, dialogue text area below. Three response 
options shown as selectable buttons. Style: semi-transparent dark panel 
with rounded corners, white text, name in golden color. Include a 
"continue" indicator (animated triangle) in bottom-right corner.
```

#### Shop Interface
```
Fantasy shop interface for a 2D RPG. Two-panel layout: left panel shows 
shop inventory (scrollable grid, 5x4), right panel shows player inventory 
(same grid). Center column: selected item preview with name, icon, 
description, price, and "Buy"/"Sell" buttons. Top: shop name banner 
("Ye Olde Smithy") with shopkeeper portrait. Bottom: player gold counter. 
Style: wooden panel background, medieval parchment texture, warm lighting.
```

#### Crafting Interface
```
Crafting screen for a survival game. Center: 3x3 crafting grid with 
drag-and-drop slots. Right of grid: result slot with arrow. Below: 
recipe book panel showing known recipes as a scrollable list with 
icons and names. Left sidebar: ingredient inventory (filterable by 
category). Top: search bar for recipes. Style: rustic, dark wood 
texture, copper/bronze accents, handwritten-style font for recipe names.
```

### Prompt Anti-Patterns

**Too vague:**
```
❌ "Make me a game UI"
❌ "RPG menu"
❌ "HUD for my game"
```

**Too prescriptive about implementation:**
```
❌ "Create a MonoGame Gum layout with GraphicalUiElement containers"
❌ "Build a Godot Control node tree with VBoxContainer"
```
Stitch generates web UI — describe what it looks like, not how it's coded.

**Mixing concerns:**
```
❌ "Inventory screen with drag-and-drop functionality and real-time 
   stat calculation when hovering items with socket bonuses applied"
```
Focus on the visual layout, not the interaction logic.

---

## 5. Prototyping Core Game Screens

### Screen Categories

Every game needs some subset of these screens. Use Stitch to prototype them before writing a line of engine code.

### HUD (Heads-Up Display)

The HUD is the most visible UI in your game — it's on screen during gameplay. Critical design considerations:

- **Minimize screen coverage** — Every pixel of HUD is a pixel not showing gameplay
- **Corners and edges** — Anchor elements to screen edges so they don't float over the action
- **Information hierarchy** — Health/survival info must be instantly readable; secondary info (score, ammo count) can be smaller
- **Transparency** — Semi-transparent backgrounds prevent the HUD from feeling like a wall

**Prompt template:**
```
[Game genre] in-game HUD overlay. [Position: element1], [Position: element2], 
[Position: element3]. [Style description]. All elements [transparency]. 
Designed for [resolution] base resolution.
```

**Example prompts by genre:**

```
# Platformer HUD
Top-left: 3 red hearts for health. Top-right: gold coin icon with 
"x 047" counter. Top-center: "LEVEL 2-3" text. Bottom-center: timer 
"02:45". Pixel art style, black outlines, vibrant colors on transparent 
background.

# Roguelike HUD  
Top-left: health bar (red) and mana bar (blue) stacked, with numeric 
values. Top-right: minimap (square, showing room layout). Bottom: 
hotbar with 6 ability slots showing cooldown overlays. Left edge: 
vertical stack of buff/debuff icons. Dark UI, green terminal aesthetic.

# Tower Defense HUD
Top bar: wave counter ("Wave 3/20"), enemy counter, player health/lives. 
Right sidebar: tower selection panel (scrollable, 3 columns). Bottom: 
selected tower info panel (stats, upgrade button, sell button). 
Top-right: gold counter with income rate. Clean, readable, bright on dark.
```

### Inventory Systems

Inventory UI is the most complex screen in most games. Key design decisions to prototype:

- **Grid vs list** — Grid is visual but wastes space; list is compact but less visual
- **Paper doll** — Equipment slots arranged on a character silhouette
- **Tooltip design** — What info appears on hover? Stats, description, lore, comparison?
- **Category filtering** — Tabs, dropdown, or sidebar filter?
- **Sort controls** — By name, value, rarity, type, weight?

**Prompt template:**
```
[Style] inventory screen. [Layout type] with [grid dimensions]. 
[Equipment section description]. [Item detail/tooltip description]. 
[Category system]. [Color scheme]. Include [specific UI elements].
```

### Menu Screens

Main menu, pause menu, game over — these are the bookends of your game experience.

**Main menu considerations:**
- Logo/title placement and sizing
- Button layout (vertical stack, horizontal, radial)
- Background art or animated scene
- Version number, social links, legal text placement

**Pause menu considerations:**
- Must overlay gameplay (semi-transparent backdrop)
- Resume must be the primary/largest action
- Settings access, save/quit options
- Minimize the number of clicks to resume

**Game over considerations:**
- Score/stats display
- "Try Again" vs "Return to Menu" prominence
- Emotional tone (celebration vs consolation)

---

## 6. Prototyping Game Flows

Stitch's prototype feature lets you connect screens into interactive flows. This is valuable for testing the *navigation* of your game UI before implementing it.

### Core Game Flow

Design each screen, then connect them:

```
┌──────────┐     ┌──────────┐     ┌──────────┐
│  Title   │────▶│   Main   │────▶│  Game-   │
│  Screen  │     │   Menu   │     │   play   │
└──────────┘     └────┬─────┘     └────┬─────┘
                      │                │
                 ┌────▼─────┐    ┌────▼─────┐
                 │ Settings │    │  Pause   │
                 │          │    │  Menu    │
                 └──────────┘    └────┬─────┘
                                      │
                                 ┌────▼─────┐
                                 │ Game Over│
                                 └──────────┘
```

### How to Prototype Flows in Stitch

1. **Design each screen individually** — Generate all screens on the infinite canvas
2. **Connect screens** — Use Stitch's "Stitch together" feature to link screens via button clicks
3. **Define transitions** — Specify what happens on each click (navigate to screen, open overlay)
4. **Play the prototype** — Click "Play" to walk through the entire flow interactively
5. **Auto-generate next screens** — Stitch can predict and generate logical next screens based on button text

### Flow Design Tips

- **Design the critical path first** — Main Menu → New Game → Gameplay → Pause → Resume
- **Then add branches** — Settings, Inventory, Shop, Quest Log
- **Test depth** — If getting to a feature takes >3 clicks, reconsider the navigation hierarchy
- **Consistent navigation** — Back buttons, escape behavior, breadcrumbs should be uniform
- **Use multi-select theming** — Select all screens and apply a single design system prompt to maintain consistency

### Example Flow Prompt Sequence

Start with the main menu, then iterate:

```
Prompt 1: "Dark fantasy RPG main menu. Logo at top, four buttons: 
New Game, Continue, Settings, Quit."

Prompt 2: "Settings screen for the same game. Tabs: Video, Audio, 
Controls. Video tab showing resolution, fullscreen toggle, brightness 
slider. Back button in bottom-left."

Prompt 3: "In-game HUD for the same game. Health bar top-left, mana 
bar below it, minimap top-right, hotbar bottom-center with 8 slots."

Prompt 4: "Pause menu overlay for the same game. Semi-transparent dark 
backdrop. Resume, Inventory, Map, Settings, Save & Quit buttons stacked 
vertically."

Prompt 5: "Inventory screen for the same game. Left: character paper 
doll with equipment slots. Right: 6x5 item grid. Bottom: item tooltip."

Prompt 6: "Game over screen for the same game. Death message, stats 
summary (enemies killed, gold earned, time played), Retry and Main 
Menu buttons."
```

The key phrase is **"for the same game"** — this tells Stitch to maintain the visual style established in earlier prompts.

---

## 7. Voice Canvas for Rapid Iteration

The voice canvas feature (March 2026) is particularly powerful for game UI iteration because game developers think spatially and often know *what they want to change* but not *how to prompt for it*.

### When to Use Voice vs Text

| Situation | Use Voice | Use Text |
|---|---|---|
| Initial exploration | ✅ "I need some kind of inventory screen..." | |
| Specific layout changes | ✅ "Move the health bar to the top-left" | |
| Precise specifications | | ✅ "6x4 grid, 48px slots, 4px gap" |
| Design critique/review | ✅ "What's wrong with this layout?" | |
| Style exploration | ✅ "Make it more medieval... no, more like a tavern menu board" | |
| Batch updates | | ✅ Multi-select + typed prompt |
| Complex prompts | | ✅ Detailed prompt with multiple requirements |

### Voice Iteration Workflow for Game UI

1. **Start with a text prompt** — Generate the initial screen
2. **Switch to voice for refinements:**
   - "The health bar is too big, make it about half that width"
   - "Can you add a minimap in the top-right corner?"
   - "I don't like the font — try something more rugged and medieval"
   - "Show me three different color schemes for this"
   - "The tooltip is overlapping the inventory grid, fix the positioning"
3. **Use voice for design review:**
   - "Look at this HUD — what would you change to make it less cluttered?"
   - "Compare this inventory layout to the previous version — which is more readable?"
   - "Is the text hierarchy clear? Can you tell what's most important?"
4. **Switch back to text for final specifications** — Lock in exact sizes, colors, spacing

### Voice Prompt Examples for Game UI

```
"I'm designing a HUD for a space shooter. Start with a simple one — 
ship health on the left, shield bar below it, weapon heat on the right, 
and a score counter top-center."

"That looks good but the bars are too thick — they're covering too much 
of the screen. Can you make them thinner, maybe like a slim stripe along 
the edge?"

"Now add an ammo counter near the weapon heat bar. Show it as a circular 
magazine indicator."

"Actually, let's try a completely different approach — what if all the HUD 
info was in a cockpit frame around the edges of the screen, like you're 
looking through a ship viewport?"
```

---

## 8. Vibe Design for Game Aesthetics

Vibe design is Stitch's most powerful feature for games. Instead of describing specific components, you describe the *feel* — and Stitch generates multiple visual directions.

### Game Aesthetic Vibes — Prompt Templates

#### Dark Souls / Soulslike
```
"I want an inventory screen that feels oppressive and weighty, like 
Dark Souls. Muted colors, heavy stone textures, minimal ornamentation. 
Everything should feel like it's carved from a slab. Equipment stats 
are displayed plainly — no flashy animations or color coding. The 
UI should feel like part of the world, not a game overlay."
```

#### Cozy Farm Game
```
"Design a shop interface that feels like walking into a warm general 
store in a farming game like Stardew Valley. Wooden shelves, hand-
written price tags, cheerful colors (warm yellows, soft greens), 
rounded corners on everything. The shopkeeper portrait should be 
friendly and inviting. Nothing should feel stressful or urgent."
```

#### Retro Arcade
```
"Scoreboard screen with a retro arcade cabinet feel. Black background, 
bright neon colors (electric blue, hot pink, lime green), scanline 
effect suggested. Scores displayed in a monospace font with blinking 
cursor. 'INSERT COIN' at the bottom. Dripping with 80s nostalgia."
```

#### Cyberpunk / Sci-Fi
```
"Hacking minigame interface for a cyberpunk game. Dark background with 
glowing circuit-board patterns. Holographic-style panels with slight 
transparency. Neon cyan and magenta accents. Data streams and hex 
codes decorating the margins. Terminal-style font for all text. 
The UI itself should look like it could exist in the game world."
```

#### Fantasy JRPG
```
"Party management screen for a colorful JRPG. Four character portraits 
in a row at top, each with HP/MP bars. Below: detailed stats for the 
selected character (STR, DEF, MAG, SPD, LUK) with equipment slots. 
Bright, clean, with a blue gradient background and golden UI frames. 
Feels polished and cheerful, like Final Fantasy or Dragon Quest menus."
```

#### Horror / Survival Horror
```
"Inventory screen for a survival horror game. Dark, almost black 
background. UI elements are barely visible until interacted with. 
Items shown as shadowy silhouettes until examined. Limited inventory 
slots (4x2) to convey scarcity. Red accent color only for health. 
Grimy, distressed texture on UI panels. Should feel unsafe to have 
the inventory open."
```

#### Minimalist / Indie
```
"In-game HUD for a minimalist indie platformer. Almost no visible UI. 
Health shown through screen vignette (darker edges = lower health). 
Collectible counter as tiny text in corner. No bars, no frames, no 
panels. The game world IS the interface. Only show information when 
it changes, then fade it out."
```

### Using Vibe Design to Establish a Style Guide

1. **Start vague** — "I want my game UI to feel warm and adventurous, like a classic adventure game"
2. **Review the variants** — Stitch generates multiple directions
3. **Pick the closest match** — Select the variant that resonates
4. **Refine the vibe** — "More like this, but with a darker background and more ornate borders"
5. **Extract the design system** — Note the colors, fonts, border styles, spacing, and transparency values
6. **Apply across all screens** — Use DESIGN.md or multi-select theming to propagate

---

## 9. DESIGN.md for Game Style Systems

Stitch's DESIGN.md feature (March 2026) lets you define and export design rules as an agent-friendly markdown file. This is extremely useful for game UI work because games need *all screens to look cohesive*.

### Creating a Game DESIGN.md

After establishing your game's visual style through vibe design, extract or create a DESIGN.md:

```markdown
# Game UI Design System — [Your Game Name]

## Color Palette
- **Background:** #1a1a2e (near-black navy)
- **Panel:** #16213e (dark blue-grey)
- **Primary text:** #e8e8e8 (warm white)
- **Secondary text:** #a0a0b0 (muted grey)
- **Accent:** #c9a227 (gold)
- **Health:** #c0392b (deep red)
- **Mana:** #2980b9 (ocean blue)
- **Success/Heal:** #27ae60 (forest green)
- **Warning:** #e67e22 (amber)
- **Rarity — Common:** #a0a0a0
- **Rarity — Uncommon:** #2ecc71
- **Rarity — Rare:** #3498db
- **Rarity — Epic:** #9b59b6
- **Rarity — Legendary:** #f39c12

## Typography
- **Headings:** serif, bold, uppercase, tracking +2%
- **Body text:** sans-serif, regular, 14-16px equivalent
- **Numbers/stats:** monospace, tabular figures
- **Tooltip titles:** serif, bold, accent color

## Spacing
- **Grid unit:** 8px
- **Panel padding:** 16px (2 grid units)
- **Element gap:** 8px (1 grid unit)
- **Section gap:** 24px (3 grid units)
- **Border radius:** 4px (subtle rounding)

## UI Elements
- **Panels:** solid background with 1px border in slightly lighter shade
- **Buttons:** filled background, 2px border, hover: brighten 10%
- **Slots (inventory/hotbar):** 48x48px, 1px inset border, empty = darker shade
- **Bars (health/mana):** 4px height, rounded ends, background track visible
- **Icons:** 24x24px or 32x32px, consistent line weight
- **Tooltips:** appear above/below element, max-width 280px, 8px padding

## Rules
- Never use pure black (#000) or pure white (#FFF)
- All text must have minimum 4.5:1 contrast ratio
- Interactive elements must have visible focus/hover states
- Panels should never cover more than 60% of the screen
- HUD elements anchor to screen edges with 16px margin
```

### Using DESIGN.md Across Screens

1. Create your DESIGN.md from the first screen you're happy with
2. When designing subsequent screens, import the DESIGN.md to maintain consistency
3. Update the DESIGN.md if you make stylistic decisions that should propagate
4. **Export to engine** — The DESIGN.md doubles as your style specification document when implementing in MonoGame or Godot

### Extracting Design Systems from References

If your game is inspired by an existing game's UI, you can:
1. Take a screenshot of the reference game's UI
2. Upload it to Stitch as an image-to-UI reference
3. Ask Stitch to extract the design system
4. Save it as a DESIGN.md
5. Apply that system to your own screens

---

## 10. Export Pipeline — Stitch to Game Engine

Stitch generates web UI (HTML/CSS). Game engines use their own UI systems. The export pipeline bridges this gap.

### What to Extract from Stitch Output

When you export a Stitch design, you get HTML and CSS. Extract these properties for your game engine implementation:

#### Layout Information
- **Element positions** — x, y coordinates (or relative positioning)
- **Element sizes** — width, height in pixels
- **Spacing** — margins, padding, gaps between elements
- **Alignment** — left/center/right, top/middle/bottom
- **Flow direction** — horizontal vs vertical stacking
- **Grid structure** — rows, columns, cell sizes

#### Visual Properties
- **Colors** — background, text, border, accent (exact hex values)
- **Font sizes** — relative hierarchy (h1 > h2 > body > caption)
- **Border styles** — width, color, radius
- **Opacity/transparency** — alpha values for overlays
- **Shadows** — offset, blur, color (if applicable in your engine)

#### Component Structure
- **Hierarchy** — which elements contain which (panel > grid > slots)
- **Repetition** — item slots, list items, tab buttons (count and pattern)
- **States** — normal, hover, active, disabled, selected appearances

### The Extract-and-Implement Workflow

```
Step 1: Design in Stitch
   └─▶ Generate screen, iterate until satisfied

Step 2: Export HTML/CSS
   └─▶ Copy the generated code

Step 3: Analyze the Layout
   └─▶ Document positions, sizes, colors, hierarchy
   └─▶ Note: grid dimensions, slot sizes, spacing values
   └─▶ Create a specification sheet (or use DESIGN.md)

Step 4: Implement in Engine
   └─▶ Translate HTML containers → engine layout containers
   └─▶ Translate CSS properties → engine styling
   └─▶ Add game-specific interactions (drag/drop, keyboard nav, etc.)

Step 5: Compare
   └─▶ Screenshot your engine implementation
   └─▶ Upload to Stitch alongside the original design
   └─▶ Ask "What differences do you see between these two screens?"
```

### What Does NOT Transfer

- **Hover effects** — Web hover doesn't map to gamepad input; implement highlight/selection separately
- **Animations/transitions** — CSS transitions must be reimplemented with engine tweening
- **Scroll behavior** — Web scrolling is automatic; game engines need manual scroll implementation
- **Font rendering** — Web fonts ≠ game engine font rendering; use the sizes and hierarchy, but actual fonts will differ
- **Responsive behavior** — Web responsive design uses media queries; games use resolution scaling

---

## 11. Integration with MonoGame

MonoGame has two primary UI approaches: **Gum.MonoGame** (the recommended full UI framework) and **ImGui.NET** (for debug/dev UI). Stitch designs translate to both.

### Stitch → Gum.MonoGame

Gum is a layout engine with containers, anchoring, and component trees — structurally similar to how Stitch generates UI.

#### Mapping HTML/CSS to Gum Concepts

| Stitch (HTML/CSS) | Gum Equivalent |
|---|---|
| `<div>` container | `ContainerRuntime` |
| `flex-direction: column` | `ChildrenLayout = TopToBottomStack` |
| `flex-direction: row` | `ChildrenLayout = LeftToRightStack` |
| `width: 200px` | `Width = 200, WidthUnits = Absolute` |
| `width: 50%` | `Width = 50, WidthUnits = RelativeToContainer` |
| `padding: 16px` | Direct property or margin on children |
| `background-color` | `ColoredRectangleRuntime` or tinted `SpriteRuntime` |
| `border` | `NineSliceRuntime` with border sprite |
| `<span>` text | `TextRuntime` |
| `<img>` | `SpriteRuntime` |
| `<button>` | `Button` (Forms control) |
| `gap: 8px` | `StackSpacing = 8` |
| `overflow: scroll` | `ScrollViewer` (Forms control) |
| `grid-template-columns: repeat(6, 1fr)` | `ContainerRuntime` with `Wraps = true`, `WrapsChildren = true` |

#### Example: Translating a Stitch Inventory to Gum

If Stitch generated an inventory with a 6-column grid of 48×48 item slots:

```csharp
// Create the inventory panel (maps to the outer container in Stitch)
var inventoryPanel = new ContainerRuntime
{
    Width = 320,          // From Stitch: container width
    Height = 280,         // From Stitch: container height
    WidthUnits = GeneralUnitType.PixelsFromSmall,
    HeightUnits = GeneralUnitType.PixelsFromSmall,
    ChildrenLayout = ChildrenLayout.TopToBottomStack,
    StackSpacing = 4,     // From Stitch: gap between rows
    XOrigin = HorizontalAlignment.Center,
    XUnits = GeneralUnitType.PixelsFromMiddle,
};

// Create rows of item slots
for (int row = 0; row < 4; row++)
{
    var rowContainer = new ContainerRuntime
    {
        Height = 48,       // From Stitch: slot height
        HeightUnits = GeneralUnitType.PixelsFromSmall,
        Width = 0,
        WidthUnits = GeneralUnitType.RelativeToContainer,
        ChildrenLayout = ChildrenLayout.LeftToRightStack,
        StackSpacing = 4,  // From Stitch: gap between columns
    };

    for (int col = 0; col < 6; col++)
    {
        var slot = CreateItemSlot(48, 48); // 48x48 from Stitch specs
        rowContainer.Children.Add(slot);
    }
    inventoryPanel.Children.Add(rowContainer);
}

GumService.Default.Root.Children.Add(inventoryPanel);
```

```csharp
// Item slot creation — colors and borders from Stitch output
private ContainerRuntime CreateItemSlot(int width, int height)
{
    var slot = new ContainerRuntime
    {
        Width = width,
        Height = height,
        WidthUnits = GeneralUnitType.PixelsFromSmall,
        HeightUnits = GeneralUnitType.PixelsFromSmall,
    };

    // Background — from Stitch: background-color
    var bg = new ColoredRectangleRuntime
    {
        Width = 0, Height = 0,
        WidthUnits = GeneralUnitType.RelativeToContainer,
        HeightUnits = GeneralUnitType.RelativeToContainer,
        Color = new Color(0x16, 0x21, 0x3e),  // From Stitch: #16213e
    };
    slot.Children.Add(bg);

    // Border — from Stitch: border
    var border = new NineSliceRuntime
    {
        Width = 0, Height = 0,
        WidthUnits = GeneralUnitType.RelativeToContainer,
        HeightUnits = GeneralUnitType.RelativeToContainer,
        SourceFileName = "UI/slot_border",  // Your 9-slice border sprite
        Color = new Color(0x3a, 0x3a, 0x4e), // From Stitch: border-color
    };
    slot.Children.Add(border);

    // Item icon (hidden until item is placed)
    var icon = new SpriteRuntime
    {
        Width = width - 8, Height = height - 8,
        WidthUnits = GeneralUnitType.PixelsFromSmall,
        HeightUnits = GeneralUnitType.PixelsFromSmall,
        XOrigin = HorizontalAlignment.Center,
        YOrigin = VerticalAlignment.Center,
        XUnits = GeneralUnitType.PixelsFromMiddle,
        YUnits = GeneralUnitType.PixelsFromMiddle,
        Visible = false,
    };
    slot.Children.Add(icon);

    return slot;
}
```

### Stitch → ImGui.NET (Debug/Dev UI)

ImGui is immediate-mode — you don't create persistent layout trees. Use Stitch designs as a visual reference rather than a direct translation:

```csharp
// Stitch designed a debug stats panel with specific layout
// Use the spacing and sizing values from the Stitch export
ImGui.SetNextWindowSize(new Vector2(280, 200)); // From Stitch dimensions
ImGui.Begin("Debug Stats", ImGuiWindowFlags.NoResize);

ImGui.TextColored(new Vector4(0.79f, 0.63f, 0.15f, 1f), "PLAYER STATS");  // #c9a227
ImGui.Separator();

ImGui.Columns(2, "stats", false);
ImGui.SetColumnWidth(0, 140); // From Stitch: left column width

ImGui.Text("Health:"); ImGui.NextColumn();
ImGui.TextColored(new Vector4(0.75f, 0.22f, 0.17f, 1f), $"{health}/{maxHealth}");
ImGui.NextColumn();

ImGui.Text("Position:"); ImGui.NextColumn();
ImGui.Text($"({pos.X:F1}, {pos.Y:F1})"); ImGui.NextColumn();

ImGui.Columns(1);
ImGui.End();
```

### MonoGame-Specific Extraction Tips

1. **Scale factor** — Stitch designs at web resolution. If your game uses a 320×180 base, divide all pixel values by your scale factor (e.g., 1920÷320 = 6× scale, so a 48px Stitch element ≈ 8px in-game)
2. **Colors are direct** — Hex colors from CSS map directly to `new Color(r, g, b)`
3. **Font sizes don't translate** — Web font sizes and game bitmap/SDF font sizes use different metrics. Use the *hierarchy* (heading > body > caption) rather than exact px values
4. **Z-ordering** — CSS `z-index` maps to child order in Gum (later children render on top)

See [G5 — UI Framework](../../monogame-arch/guides/G5_ui_framework.md) for comprehensive Gum.MonoGame setup and advanced layout techniques.

---

## 12. Integration with Godot

Godot's UI system (Control nodes) maps well to Stitch's generated HTML/CSS because both use a tree-based layout model.

### Stitch → Godot Control Nodes

| Stitch (HTML/CSS) | Godot Equivalent |
|---|---|
| `<div>` container | `Control` or `PanelContainer` |
| `flex-direction: column` | `VBoxContainer` |
| `flex-direction: row` | `HBoxContainer` |
| `display: grid` | `GridContainer` |
| `width: 200px` | `custom_minimum_size.x = 200` |
| `width: 50%` | Anchor presets + size flags `EXPAND_FILL` |
| `padding` | `MarginContainer` with theme override margins |
| `background-color` | `PanelContainer` with `StyleBoxFlat` |
| `border-radius` | `StyleBoxFlat.corner_radius_*` |
| `color` (text) | `Label.add_theme_color_override("font_color", color)` |
| `font-size` | `Label.add_theme_font_size_override("font_size", size)` |
| `<span>` text | `Label` or `RichTextLabel` |
| `<img>` | `TextureRect` |
| `<button>` | `Button` |
| `gap` | Container `theme_override_constants/separation` |
| `overflow: scroll` | `ScrollContainer` |
| `opacity` | `Control.modulate.a` |

#### Example: Translating a Stitch Inventory to Godot

Scene tree structure mapping from a Stitch inventory layout:

```
InventoryScreen (Control — full rect anchor)
├── Background (ColorRect — semi-transparent black overlay)
├── Panel (PanelContainer — centered, fixed size from Stitch)
│   ├── MarginContainer (padding from Stitch CSS padding values)
│   │   ├── VBoxContainer (main vertical layout)
│   │   │   ├── TitleBar (HBoxContainer)
│   │   │   │   ├── Title (Label — "Inventory")
│   │   │   │   └── CloseButton (Button — "X")
│   │   │   ├── HSeparator
│   │   │   ├── Content (HBoxContainer)
│   │   │   │   ├── Equipment (VBoxContainer — paper doll)
│   │   │   │   │   ├── HeadSlot (TextureRect — 48x48)
│   │   │   │   │   ├── ChestSlot (TextureRect — 48x48)
│   │   │   │   │   ├── LegsSlot (TextureRect — 48x48)
│   │   │   │   │   ├── WeaponSlot (TextureRect — 48x48)
│   │   │   │   │   └── ShieldSlot (TextureRect — 48x48)
│   │   │   │   ├── VSeparator
│   │   │   │   └── ScrollContainer
│   │   │   │       └── ItemGrid (GridContainer — columns=6)
│   │   │   │           ├── ItemSlot0 (PanelContainer — 48x48)
│   │   │   │           ├── ItemSlot1 ...
│   │   │   │           └── ItemSlot23 (6x4 = 24 slots)
│   │   │   └── Tooltip (PanelContainer — positioned dynamically)
│   │   │       ├── ItemName (Label)
│   │   │       ├── ItemDescription (Label — word wrap)
│   │   │       └── ItemStats (VBoxContainer)
```

GDScript implementation using values from Stitch export:

```gdscript
extends Control

## Inventory screen — layout values extracted from Stitch prototype

const GRID_COLUMNS := 6
const GRID_ROWS := 4
const SLOT_SIZE := 48  # px from Stitch design
const SLOT_GAP := 4    # px from Stitch CSS gap

# Colors from Stitch export
const COLOR_PANEL_BG := Color("#1a1a2e")
const COLOR_SLOT_BG := Color("#16213e")
const COLOR_SLOT_BORDER := Color("#3a3a4e")
const COLOR_ACCENT := Color("#c9a227")
const COLOR_TEXT := Color("#e8e8e8")
const COLOR_TEXT_MUTED := Color("#a0a0b0")

@onready var item_grid: GridContainer = %ItemGrid
@onready var tooltip: PanelContainer = %Tooltip
@onready var tooltip_name: Label = %ItemName
@onready var tooltip_desc: Label = %ItemDescription

func _ready() -> void:
    # Apply Stitch design values
    item_grid.columns = GRID_COLUMNS
    item_grid.add_theme_constant_override("h_separation", SLOT_GAP)
    item_grid.add_theme_constant_override("v_separation", SLOT_GAP)

    # Create item slots matching Stitch grid
    for i in GRID_COLUMNS * GRID_ROWS:
        var slot := _create_slot()
        item_grid.add_child(slot)

    tooltip.visible = false

func _create_slot() -> PanelContainer:
    var slot := PanelContainer.new()
    slot.custom_minimum_size = Vector2(SLOT_SIZE, SLOT_SIZE)

    # StyleBox matching Stitch CSS
    var style := StyleBoxFlat.new()
    style.bg_color = COLOR_SLOT_BG
    style.border_color = COLOR_SLOT_BORDER
    style.border_width_bottom = 1
    style.border_width_top = 1
    style.border_width_left = 1
    style.border_width_right = 1
    style.corner_radius_top_left = 2
    style.corner_radius_top_right = 2
    style.corner_radius_bottom_left = 2
    style.corner_radius_bottom_right = 2
    slot.add_theme_stylebox_override("panel", style)

    # Item icon
    var icon := TextureRect.new()
    icon.expand_mode = TextureRect.EXPAND_FIT_WIDTH_PROPORTIONAL
    icon.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
    icon.custom_minimum_size = Vector2(SLOT_SIZE - 8, SLOT_SIZE - 8)
    slot.add_child(icon)

    # Hover/click signals
    slot.mouse_entered.connect(_on_slot_hover.bind(slot))
    slot.mouse_exited.connect(_on_slot_unhover)
    slot.gui_input.connect(_on_slot_input.bind(slot))

    return slot

func _on_slot_hover(slot: PanelContainer) -> void:
    # Highlight style from Stitch hover state
    var style: StyleBoxFlat = slot.get_theme_stylebox("panel").duplicate()
    style.border_color = COLOR_ACCENT
    slot.add_theme_stylebox_override("panel", style)
    _show_tooltip(slot)

func _on_slot_unhover() -> void:
    tooltip.visible = false

func _show_tooltip(slot: PanelContainer) -> void:
    # Position tooltip above the slot (from Stitch tooltip positioning)
    tooltip.visible = true
    tooltip.global_position = slot.global_position - Vector2(0, tooltip.size.y + 8)
```

#### Godot Theme Resources from Stitch

Extract a Stitch design system into a Godot Theme resource:

```gdscript
# theme_builder.gd — Run once to generate a theme from Stitch design values

func create_game_theme() -> Theme:
    var theme := Theme.new()

    # Panel style (from Stitch panel CSS)
    var panel_style := StyleBoxFlat.new()
    panel_style.bg_color = Color("#1a1a2e")
    panel_style.border_color = Color("#2a2a4e")
    panel_style.set_border_width_all(1)
    panel_style.set_corner_radius_all(4)
    panel_style.set_content_margin_all(16)  # From Stitch: padding: 16px
    theme.set_stylebox("panel", "PanelContainer", panel_style)

    # Button styles (from Stitch button CSS)
    var btn_normal := StyleBoxFlat.new()
    btn_normal.bg_color = Color("#2a2a4e")
    btn_normal.border_color = Color("#c9a227")
    btn_normal.set_border_width_all(2)
    btn_normal.set_corner_radius_all(4)
    btn_normal.set_content_margin_all(8)
    theme.set_stylebox("normal", "Button", btn_normal)

    var btn_hover := btn_normal.duplicate()
    btn_hover.bg_color = Color("#3a3a5e")  # From Stitch: hover brighten 10%
    theme.set_stylebox("hover", "Button", btn_hover)

    var btn_pressed := btn_normal.duplicate()
    btn_pressed.bg_color = Color("#1a1a3e")
    theme.set_stylebox("pressed", "Button", btn_pressed)

    # Font colors (from Stitch text CSS)
    theme.set_color("font_color", "Label", Color("#e8e8e8"))
    theme.set_color("font_color", "Button", Color("#e8e8e8"))

    # Font sizes (from Stitch text hierarchy)
    theme.set_font_size("font_size", "Label", 14)

    return theme
```

### Godot-Specific Tips

1. **Anchor presets** — Stitch positions with `position: absolute` or flexbox. In Godot, use anchor presets: `PRESET_TOP_LEFT` for HUD corners, `PRESET_CENTER` for popup menus, `PRESET_BOTTOM_WIDE` for dialogue boxes
2. **Control.mouse_filter** — Set to `IGNORE` on HUD elements that shouldn't block clicks to the game world
3. **CanvasLayer** — Put your UI on a separate CanvasLayer so it renders above game content (maps to Stitch's z-index layering)
4. **Theme propagation** — Set the theme on the root UI Control and it cascades to all children (like CSS inheritance)
5. **Size flags** — `SIZE_EXPAND_FILL` in containers maps to CSS `flex: 1`

See [G3 — Signal Architecture](../../godot-arch/guides/G3_signal_architecture.md) for connecting UI events to game systems via the signal bus pattern.

---

## 13. Advanced Workflows

### Multi-Resolution Prototyping

Design the same screen at different resolutions to test how your UI scales:

```
Prompt 1: "RPG inventory screen at 1920x1080 — full desktop layout"
Prompt 2: "Same inventory screen at 1280x720 — what changes?"
Prompt 3: "Same inventory for 320x180 pixel art resolution — 
           extremely condensed, maximum information density"
```

Compare the variants to identify which elements are essential vs optional at each resolution.

### Competitive Analysis Workflow

1. Take screenshots of UI you admire from shipped games
2. Upload to Stitch with image-to-UI conversion
3. Ask Stitch to extract the design system
4. Apply that system to your own game's screens
5. Iterate until it feels original, not derivative

### Collaborative Design Reviews

1. Design a screen in Stitch
2. Use the voice canvas to interview yourself: "Walk me through this screen. What does the player do first? Where do their eyes go?"
3. The AI agent will challenge your assumptions and suggest improvements
4. Iterate based on the critique

### A/B Testing UI Variants

Use the Agent Manager to explore multiple directions:

1. Generate an inventory screen
2. Ask for three variations: "Show me this as a grid, a list, and a card layout"
3. Create a prototype flow for each variant
4. Test which navigation feels best
5. Implement the winner in your engine

### Screenshot Comparison Loop

After implementing your UI in-engine:

1. Screenshot your engine implementation
2. Upload to Stitch alongside the original Stitch design
3. Prompt: "Compare these two screens. The left is the design reference, the right is the implementation. What visual differences should be corrected?"
4. Stitch will identify spacing inconsistencies, color drift, alignment issues, and missing elements

---

## 14. Limitations & What Stitch Is Not

### Stitch Generates Web UI, Not Game UI

This is the single most important thing to understand. Stitch outputs HTML/CSS — it has no concept of:

- **Game engine rendering pipelines** — No SpriteBatch, no Control nodes, no Canvas
- **Game-specific interactions** — No drag-and-drop items, no gamepad navigation, no held-button charges
- **Performance constraints** — Web UI doesn't worry about draw calls or texture atlases
- **Pixel art fidelity** — Stitch generates at web resolution (1×); pixel art at 4× or 8× scale looks different
- **Real-time updates** — Health bars that animate, cooldown timers, damage numbers — these need engine implementation
- **Input abstraction** — No keyboard/gamepad/touch input handling

### Resolution Mismatch

If your game runs at 320×180 (pixel art), Stitch designs at 1920×1080+ will look fundamentally different. The fonts, spacing, and element sizes won't directly translate. Use Stitch for *layout relationships* (what goes where, relative sizes) rather than *exact pixel values*.

### No State Management

Stitch shows you what a screen looks like, not how it behaves. You still need to implement:
- Inventory slot data binding
- Equipment swap logic
- Tooltip positioning and content updates
- Tab switching state
- Scroll position persistence
- Input focus management
- Transition animations

### No Sprite/Texture Generation

Stitch generates UI layouts with placeholder graphics. It does not create:
- Pixel art button sprites
- 9-slice panel textures
- Icon sets
- Character portraits
- Item artwork

You still need art assets from your art pipeline. Stitch designs tell you what *shape and size* those assets need to be.

### Font Rendering Differences

Web fonts render differently than game engine fonts (bitmap fonts, SDF fonts, FontStashSharp). The visual hierarchy from Stitch (heading > subheading > body > caption) transfers, but exact pixel sizing does not.

---

## 15. Best Practices

### Do

- ✅ **Use Stitch for layout and composition** — Where elements go, how they're grouped, relative sizes
- ✅ **Extract color palettes** — Exact hex values transfer perfectly to any engine
- ✅ **Design all screens together** — Use the infinite canvas to see your entire UI system at once
- ✅ **Create a DESIGN.md** — Formalize your design system for consistency
- ✅ **Prototype flows** — Test navigation before implementing it
- ✅ **Use voice for exploration** — Faster than typing when you're not sure what you want
- ✅ **Iterate in Stitch, not in code** — Changing a prompt is cheaper than refactoring engine code
- ✅ **Screenshot-compare your implementation** — Upload your engine screenshots to catch visual drift

### Don't

- ❌ **Don't try to make Stitch generate engine code** — It outputs HTML/CSS, period
- ❌ **Don't use Stitch pixel values directly for pixel art games** — Scale ratios will be wrong
- ❌ **Don't skip engine implementation** — Stitch designs are mockups, not shipping UI
- ❌ **Don't design interactions in Stitch** — Design the *look*, implement the *behavior* in engine
- ❌ **Don't expect perfect 1:1 reproduction** — The goal is 90% visual match with proper engine functionality
- ❌ **Don't forget gamepad/keyboard navigation** — Stitch designs assume mouse; you need to add focus traversal
- ❌ **Don't design one screen at a time** — Inconsistency between screens is the #1 amateur UI problem

### The 80/20 Rule

Stitch handles 80% of the design work (layout, colors, typography, composition) in 20% of the time. The remaining 20% (interaction, animation, input, state management) requires 80% of the time and must happen in-engine. Accept this split — the Stitch phase should be fast and exploratory, not perfectionist.

### Recommended Workflow

```
1. Vibe design    → Establish the aesthetic (5 min)
2. DESIGN.md      → Lock the design system (5 min)
3. Core screens   → Design 6-10 key screens (30 min)
4. Flow prototype → Connect screens, test navigation (15 min)
5. Voice review   → Critique and refine (10 min)
6. Export specs   → Document sizes, colors, spacing (10 min)
7. Engine build   → Implement with extracted specs (hours)
8. Compare loop   → Screenshot → Stitch → fix → repeat
```

Total Stitch time: ~75 minutes for a complete game UI system.
Total engine time: varies, but you're building *with a blueprint* instead of improvising.

---

## 16. Quick Reference — Prompt Templates

Copy-paste these templates and fill in the bracketed sections.

### HUD Prompt
```
[art style] in-game HUD overlay for a [genre] game.
Top-left: [health display type].
Top-right: [secondary info].
Bottom-center: [hotbar/ability bar with N slots].
[Additional HUD elements].
[Transparency and readability notes].
```

### Inventory Prompt
```
[art style] inventory screen for a [genre] game.
Layout: [grid/list/hybrid] with [dimensions].
Left panel: [equipment/character info].
Right panel: [item grid with scroll].
Bottom: [tooltip/item detail area].
[Color scheme description].
```

### Menu Prompt
```
[art style] [menu type] for a [genre] game.
[Logo/title placement].
Buttons: [list of buttons in order].
[Background description].
[Style notes: borders, fonts, effects].
```

### Shop Prompt
```
[art style] shop interface for a [genre] game.
Two panels: shop inventory [grid size] | player inventory [grid size].
Center: selected item [details to show].
Top: [shop name and shopkeeper].
Bottom: [currency display].
[Style and theme description].
```

### Dialogue Box Prompt
```
[art style] dialogue box at [position] of screen, covering [percentage]%.
[Portrait placement and size].
[Character name styling].
[Text area description].
[Response options format].
[Style: transparency, borders, text colors].
```

### Settings Screen Prompt
```
Game settings menu with tabs: [tab names].
[Active tab] showing: [list of settings with control types].
Bottom: [action buttons].
[Style consistency note — "same style as main menu"].
```

### Game Over Prompt
```
[art style] game over screen for a [genre] game.
[Tone: celebratory/somber/dramatic].
[Stats to display].
[Action buttons and their prominence].
[Background treatment].
```

---

## Related Guides

- [G5 — UI Framework (MonoGame Gum)](../../monogame-arch/guides/G5_ui_framework.md) — Full Gum.MonoGame implementation reference
- [P5 — Art Pipeline](../project-management/P5_art_pipeline.md) — UI art creation and asset pipeline
- [C1 — Genre Reference](C1_genre_reference.md) — Genre-specific UI patterns and conventions
- [C2 — Game Feel](C2_game_feel_and_genre_craft.md) — UI juice, screen shake, and feel
- [ui-theory](../concepts/ui-theory.md) — Engine-agnostic UI design theory (if available)
- [G3 — Signal Architecture (Godot)](../../godot-arch/guides/G3_signal_architecture.md) — Connecting Godot UI signals to game systems
