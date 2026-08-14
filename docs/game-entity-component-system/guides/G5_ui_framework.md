# G5 — UI Framework


> **Category:** Guide · **Related:** [R1 Library Stack](../reference/R1_library_stack.md) · [C1 Genre Reference](../../core/game-design/C1_genre_reference.md)

> Complete setup and usage guide for Gum.MonoGame — the official MonoGame UI recommendation.

---


### 1 Setup & Initialization

Install via NuGet (actively updated — latest is `2026.3.4.1`):

```bash
dotnet add package Gum.MonoGame
```

Minimal Game class integration:

```csharp
using MonoGameGum;
using Gum.Forms;
using Gum.Forms.Controls;

public class Game1 : Game
{
    private GraphicsDeviceManager _graphics;
    GumService GumUI => GumService.Default;

    public Game1()
    {
        _graphics = new GraphicsDeviceManager(this);
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
    }

    protected override void Initialize()
    {
        // V3 is current default visuals version
        GumUI.Initialize(this, DefaultVisualsVersion.V3);
        base.Initialize();
    }

    protected override void Update(GameTime gameTime)
    {
        GumUI.Update(gameTime);
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.CornflowerBlue);
        GumUI.Draw();
        base.Draw(gameTime);
    }
}
```

> **Nez/MonoGame.Extended:** Works alongside them. For Nez, call `GumUI.Initialize(Core.GraphicsDevice, ...)` instead.

### 2 Layout System

Gum uses a property-based layout engine with flexible units for position and size.

**Position units** — set via `XUnits` / `YUnits`:
- `PixelsFromLeft/Top` — absolute pixel offset from parent edge
- `PercentageWidth/Height` — percentage of parent dimension
- `PixelsFromCenter` — offset from parent center
- `PixelsFromRight/Bottom` — offset from opposite edge

**Size units** — set via `WidthUnits` / `HeightUnits`:
- `Absolute` — fixed pixel size
- `RelativeToContainer` — percentage of parent
- `RelativeToChildren` — auto-size to fit children (with optional padding)
- `Ratio` — proportional sharing of remaining space

**Stacking (Children Layout):**
```csharp
var panel = new ContainerRuntime();
panel.ChildrenLayout = ChildrenLayout.TopToBottomStack; // vertical list
// Also: LeftToRightStack, AutoGridHorizontal, AutoGridVertical
panel.StackSpacing = 8; // gap between children
panel.WrapsChildren = true; // flow-wrap when exceeding bounds
```

**Anchoring pattern (center a panel):**
```csharp
var dialog = new ContainerRuntime();
dialog.XOrigin = HorizontalAlignment.Center;
dialog.YOrigin = VerticalAlignment.Center;
dialog.XUnits = GeneralUnitType.PixelsFromCenter;
dialog.YUnits = GeneralUnitType.PixelsFromCenter;
dialog.X = 0; dialog.Y = 0;
dialog.Width = 400; dialog.Height = 300;
```

**Docking (full-width top bar):**
```csharp
var topBar = new ContainerRuntime();
topBar.WidthUnits = DimensionUnitType.RelativeToContainer;
topBar.Width = 0; // 0 = 100% of parent
topBar.Height = 64;
```

### 3 Forms Controls

Gum Forms provides standard UI controls. All are added to the visual tree via `.AddToRoot()` or as children of containers.

```csharp
// Button
var btn = new Button();
btn.Text = "Start Game";
btn.Click += (s, e) => StartGame();
btn.AddToRoot();

// TextBox
var input = new TextBox();
input.Placeholder = "Enter name...";
input.Width = 200;
input.TextChanged += (s, e) => playerName = input.Text;

// ListBox
var list = new ListBox();
list.Width = 250; list.Height = 300;
list.Items.Add("Warrior"); list.Items.Add("Mage"); list.Items.Add("Rogue");
list.SelectionChanged += (s, e) => selectedClass = list.SelectedItem?.ToString();

// Slider
var volumeSlider = new Slider();
volumeSlider.Minimum = 0; volumeSlider.Maximum = 100;
volumeSlider.Value = 75;
volumeSlider.ValueChanged += (s, e) => SetVolume(volumeSlider.Value / 100f);

// CheckBox
var fullscreen = new CheckBox();
fullscreen.Text = "Fullscreen";
fullscreen.IsChecked = false;
fullscreen.Checked += (s, e) => ToggleFullscreen(true);
fullscreen.Unchecked += (s, e) => ToggleFullscreen(false);

// ScrollViewer — wraps any content that overflows
var scroll = new ScrollViewer();
scroll.Width = 300; scroll.Height = 400;
scroll.InnerPanel.ChildrenLayout = ChildrenLayout.TopToBottomStack;
// Add children to scroll.InnerPanel

// ComboBox (dropdown)
var combo = new ComboBox();
combo.Items.Add("Easy"); combo.Items.Add("Normal"); combo.Items.Add("Hard");
combo.SelectedIndex = 1;

// TreeView
var tree = new TreeView();
var rootNode = new TreeViewItem { Header = "Inventory" };
rootNode.Items.Add(new TreeViewItem { Header = "Weapons" });
rootNode.Items.Add(new TreeViewItem { Header = "Potions" });
tree.Items.Add(rootNode);
```

### 4 Theming & Custom Visuals

Gum V3 default visuals provide a clean baseline. Override by customizing runtime objects:

```csharp
// Custom button styling via runtime manipulation
var btn = new Button();
btn.Visual.Color = new Color(40, 40, 60);    // background tint
btn.Visual.SetProperty("FontSize", 18);

// Font integration — Gum supports custom fonts via BitmapFont or
// integration with FontStashSharp for runtime font rendering:
// 1. Add FontStashSharp NuGet
// 2. Create fonts at runtime
// 3. Assign to Gum text objects via custom renderers
```

**States for hover/press/disabled** are built into Forms controls — they automatically apply visual state changes. You can customize these by modifying the underlying `GraphicalUiElement` states.

### 5 Screen Management Pattern

```csharp
public abstract class UiScreen
{
    protected ContainerRuntime Root { get; private set; }

    public void Show()
    {
        Root = new ContainerRuntime();
        Root.WidthUnits = DimensionUnitType.RelativeToContainer;
        Root.HeightUnits = DimensionUnitType.RelativeToContainer;
        Root.Width = 0; Root.Height = 0; // fill parent
        BuildLayout(Root);
        Root.AddToRoot();
    }

    public void Hide() => Root?.RemoveFromParent();
    protected abstract void BuildLayout(ContainerRuntime root);
}

public class MainMenuScreen : UiScreen
{
    protected override void BuildLayout(ContainerRuntime root)
    {
        root.ChildrenLayout = ChildrenLayout.TopToBottomStack;
        root.StackSpacing = 16;

        var title = new Label { Text = "My Game" };
        var playBtn = new Button { Text = "Play" };
        playBtn.Click += (_, _) => ScreenManager.Show<GameplayScreen>();

        var optBtn = new Button { Text = "Options" };
        root.Children.Add(title.Visual);
        root.Children.Add(playBtn.Visual);
        root.Children.Add(optBtn.Visual);
    }
}
```

### 6 Touch & Gamepad Navigation

Gum Forms controls support focus navigation. For gamepad:

```csharp
// Enable gamepad navigation
FormsUtilities.EnableGamepadNavigation = true;

// Controls expose IsFocused, Tab order follows visual tree order
// Use DPad Up/Down to navigate, A to activate
```

For touch: Gum handles touch input as mouse input on mobile platforms — no special configuration needed.

### 7 Performance Tips

- **Texture atlases:** Pack UI sprites into atlases to minimize draw calls. Gum batches rendering automatically when textures share an atlas.
- **Minimize layout recalculation:** Avoid changing layout properties every frame. Cache references.
- **Visibility:** Set `Visible = false` on off-screen elements — Gum skips both layout and render.
- **Use retained mode** (default) for persistent UI. Use `GumBatch` immediate mode only for transient debug overlays.

### 8 Framework Comparison

| Feature | **Gum.MonoGame** | **Myra** | **Custom SpriteBatch** |
|---|---|---|---|
| Layout engine | Full (anchoring, stacking, percentage, auto-size) | CSS-like grid/stack | Manual calculation |
| Forms controls | ✅ Complete set (Button, TextBox, ListBox, etc.) | ✅ Rich widget set | ❌ Build from scratch |
| WYSIWYG editor | ✅ Gum Tool (standalone app) | ✅ MyraPad | ❌ None |
| Learning curve | Medium | Medium | Low (but high total effort) |
| Performance | Good (batched, retained) | Good | Best (full control) |
| Active development | ✅ Very active (2026) | Moderate | N/A |
| Best for | Production UI, complex menus, data-heavy screens | Rapid prototyping, tool UIs | Simple HUDs, minimal UI |

**When to use each:**
- **Gum** → Full game UI: menus, inventory, dialogue, HUD. Best layout engine in the MonoGame ecosystem.
- **Myra** → Quick prototyping or dev tools. Good if you prefer CSS-like styling.
- **Custom SpriteBatch** → Simple health bars, score displays, debug text. When you need <5 UI elements and full rendering control.

---

### 9 UI Prototyping with Google Stitch

Before writing any Gum code, consider prototyping your UI layouts in [Google Stitch](https://stitch.withgoogle.com/) — a free AI-native design canvas that generates high-fidelity UI mockups from natural language prompts.

**Why prototype first:**
- Changing a text prompt is faster than refactoring Gum layout code
- See your entire UI system (menus, HUD, inventory, settings) on an infinite canvas
- Extract exact colors, spacing, and sizing values from the generated HTML/CSS
- Test screen flows (Main Menu → Settings → Gameplay → Pause) as interactive prototypes

**Stitch → Gum translation quick reference:**

| Stitch (HTML/CSS) | Gum Equivalent |
|---|---|
| `<div>` container | `ContainerRuntime` |
| `flex-direction: column` | `ChildrenLayout = TopToBottomStack` |
| `flex-direction: row` | `ChildrenLayout = LeftToRightStack` |
| `width: 50%` | `Width = 50, WidthUnits = RelativeToContainer` |
| `gap: 8px` | `StackSpacing = 8` |
| `overflow: scroll` | `ScrollViewer` (Forms control) |

**Workflow:**
1. Describe your screen in Stitch ("dark fantasy RPG inventory with 6x4 item grid, equipment paper doll on left, tooltip at bottom")
2. Iterate with voice or text refinements until the layout is right
3. Export the HTML/CSS and extract layout values (positions, sizes, colors, spacing)
4. Implement in Gum using the extracted specifications
5. Screenshot your Gum implementation, upload to Stitch, and ask it to compare — it'll flag visual differences

**Note:** Stitch generates web UI, not game engine UI. Use it for *layout design and visual specification*, then implement the behavior (drag-and-drop, gamepad navigation, tooltips) in Gum code.

See [G — Stitch UI Workflow](../../core/game-design/G_stitch_ui_workflow.md) for the complete guide with prompt templates, DESIGN.md integration, and detailed Stitch-to-Gum translation examples.

---

