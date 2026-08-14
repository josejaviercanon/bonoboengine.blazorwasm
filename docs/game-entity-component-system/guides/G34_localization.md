# G34 — Localization & Internationalization


> **Category:** Guide · **Related:** [G5 UI Framework](./G5_ui_framework.md) · [G8 Content Pipeline](./G8_content_pipeline.md) · [G26 Resource Loading & Caching](./G26_resource_loading_caching.md)

> **Stack:** MonoGame · Arch ECS · FontStashSharp · Gum UI

Localization is the difference between a game that sells in one market and a game that sells globally. This guide covers practical i18n patterns for a MonoGame 2D game — from string externalization to RTL layout.

---

## Core Concepts

| Term | Meaning |
|------|---------|
| **i18n** | Internationalization — designing your code so it *can* be localized |
| **L10n** | Localization — actually translating/adapting for a specific locale |
| **Locale** | Language + region code: `en-US`, `ja-JP`, `pt-BR` |
| **String key** | A stable identifier like `ui.menu.start` that maps to translated text |
| **Fallback** | When a translation is missing, fall back to a default language (usually English) |

**Rule of thumb:** Do i18n from day one. Retrofitting localization into a finished game is painful and expensive.

---

## String Externalization

### JSON String Tables

The simplest approach: one JSON file per locale.

```
Content/
  localization/
    en.json
    ja.json
    de.json
    pt-BR.json
```

```json
// en.json
{
  "menu.start": "Start Game",
  "menu.options": "Options",
  "menu.quit": "Quit",
  "dialog.npc.greeting": "Welcome, traveler!",
  "dialog.npc.greeting.morning": "Good morning, traveler!",
  "item.potion.name": "Health Potion",
  "item.potion.desc": "Restores {0} health points.",
  "ui.gold": "{0:N0} Gold"
}
```

```json
// de.json
{
  "menu.start": "Spiel starten",
  "menu.options": "Einstellungen",
  "menu.quit": "Beenden",
  "dialog.npc.greeting": "Willkommen, Reisender!",
  "dialog.npc.greeting.morning": "Guten Morgen, Reisender!",
  "item.potion.name": "Heiltrank",
  "item.potion.desc": "Stellt {0} Lebenspunkte wieder her.",
  "ui.gold": "{0:N0} Gold"
}
```

### Key Naming Convention

Use a hierarchical dot-notation:

```
[category].[subcategory].[identifier]

menu.start          — UI menus
dialog.npc.greeting — NPC dialogue
item.potion.name    — Item names
combat.miss         — Combat log messages
tutorial.step1      — Tutorial text
achievement.first_kill.name — Achievement title
```

### .resx Resource Files (Alternative)

C# has built-in `ResourceManager` with `.resx` files. Pros: compile-time validation, IDE tooling. Cons: less modding-friendly, harder to hot-reload.

For most indie games, **JSON is preferred** — simpler, easier for translators, and trivial to mod.

---

## LocalizationManager Implementation

```csharp
public class LocalizationManager
{
    private Dictionary<string, string> _strings = new();
    private Dictionary<string, string> _fallback = new();
    private CultureInfo _culture;
    
    public string CurrentLocale { get; private set; }
    public event Action<string>? LocaleChanged;
    
    public void LoadLocale(string locale)
    {
        // Load fallback (English) if not already loaded
        if (_fallback.Count == 0)
            _fallback = LoadFile("en");
        
        _strings = LoadFile(locale);
        _culture = new CultureInfo(locale);
        CurrentLocale = locale;
        LocaleChanged?.Invoke(locale);
    }
    
    private Dictionary<string, string> LoadFile(string locale)
    {
        string path = $"Content/localization/{locale}.json";
        if (!File.Exists(path))
        {
            // Try language-only: "pt-BR" → "pt"
            path = $"Content/localization/{locale.Split('-')[0]}.json";
        }
        if (!File.Exists(path)) return new();
        
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
    }
    
    /// <summary>Get a localized string by key.</summary>
    public string Get(string key)
    {
        if (_strings.TryGetValue(key, out var value)) return value;
        if (_fallback.TryGetValue(key, out var fallback)) return $"[{fallback}]"; // Bracketed = missing translation
        return $"??{key}??"; // Missing entirely
    }
    
    /// <summary>Get with format arguments.</summary>
    public string Get(string key, params object[] args)
    {
        string template = Get(key);
        return string.Format(_culture, template, args);
    }
    
    /// <summary>Hot-reload current locale (for dev workflow).</summary>
    public void Reload() => LoadLocale(CurrentLocale);
}
```

### Registering as an ECS Resource

```csharp
var loc = new LocalizationManager();
loc.LoadLocale("en");
world.Set(loc);

// In any system:
ref var loc = ref world.Get<LocalizationManager>();
string label = loc.Get("menu.start"); // "Start Game"
string desc = loc.Get("item.potion.desc", 50); // "Restores 50 health points."
```

### Hot-Reload in Development

Watch the localization directory for changes and reload on the fly:

```csharp
#if DEBUG
var watcher = new FileSystemWatcher("Content/localization", "*.json");
watcher.Changed += (_, _) => loc.Reload();
watcher.EnableRaisingEvents = true;
#endif
```

This lets translators edit JSON and see results without restarting the game.

---

## Font Support

### FontStashSharp for Unicode

MonoGame's built-in `SpriteFont` can't handle CJK, Arabic, or large Unicode ranges efficiently. Use **FontStashSharp**:

```csharp
// Load a font system with fallback chain
var fontSystem = new FontSystem();
fontSystem.AddFont(File.ReadAllBytes("Content/fonts/NotoSans-Regular.ttf"));      // Latin
fontSystem.AddFont(File.ReadAllBytes("Content/fonts/NotoSansCJK-Regular.ttc"));   // CJK
fontSystem.AddFont(File.ReadAllBytes("Content/fonts/NotoSansArabic-Regular.ttf")); // Arabic

var font = fontSystem.GetFont(24); // 24px, auto-selects glyphs from chain
```

### Font Fallback Chain

Order matters. Place your primary font first, then add fallback fonts for character ranges the primary doesn't cover:

| Priority | Font | Coverage |
|----------|------|----------|
| 1 | Noto Sans | Latin, Cyrillic, Greek |
| 2 | Noto Sans CJK | Chinese, Japanese, Korean |
| 3 | Noto Sans Arabic | Arabic, Persian, Urdu |
| 4 | Noto Sans Hebrew | Hebrew |
| 5 | Noto Emoji | Emoji/symbols |

**Tip:** Google's Noto font family ("No Tofu") is free, covers virtually all scripts, and is specifically designed to eliminate missing-glyph boxes (□ — "tofu").

### Font Size and Readability

| Language | Typical Size Adjustment |
|----------|------------------------|
| English | Baseline (1.0×) |
| German | Same size, but needs wider containers |
| Japanese | Often looks better at 0.9× baseline |
| Chinese | Same size, but needs more line spacing |
| Arabic | May need 1.1× for readability |
| Korean | Same size, slightly more line spacing |

---

## Text Rendering Challenges

### Variable Text Length

The same string in different languages can vary dramatically:

| Language | "Start Game" | Length vs English |
|----------|-------------|-------------------|
| English | Start Game | baseline |
| German | Spiel starten | +30% |
| French | Lancer le jeu | +40% |
| Japanese | ゲームスタート | –20% (but taller) |
| Russian | Начать игру | +10% |
| Chinese | 开始游戏 | –60% |

**Design rule:** Never hardcode text container sizes. Use Gum's auto-sizing or measure text at runtime:

```csharp
Vector2 textSize = font.MeasureString(localizedText);
float containerWidth = Math.Max(minWidth, textSize.X + padding * 2);
```

### Text Wrapping

Implement word-wrap that respects language rules:

- **Latin scripts:** Break at spaces/hyphens
- **CJK:** Can break between any characters (no spaces in Chinese/Japanese)
- **Thai:** No spaces between words — requires dictionary-based segmentation (or use ICU)

### Dynamic Layout

Buttons, menus, and dialog boxes must resize to fit content:

```csharp
// Bad: fixed-width button
var button = new Button { Width = 120, Text = loc.Get("menu.start") };

// Good: auto-width with minimum
var button = new Button 
{ 
    MinWidth = 80,
    AutoWidth = true,
    Padding = new Thickness(16, 8),
    Text = loc.Get("menu.start") 
};
```

---

## Asset Localization

### Text in Images

**Avoid baking text into sprites.** If you must:

```
Content/
  sprites/
    title_screen/
      logo_en.png
      logo_ja.png
      logo_de.png
```

Map in your localization config:

```json
// asset-map.json
{
  "en": { "title_logo": "sprites/title_screen/logo_en" },
  "ja": { "title_logo": "sprites/title_screen/logo_ja" },
  "de": { "title_logo": "sprites/title_screen/logo_de" }
}
```

**Better approach:** Layer text over images using your font renderer. Only localize assets when absolutely necessary (logos, hand-drawn text).

### Audio Localization

For voiced dialogue:

```
Content/
  audio/
    dialog/
      en/
        npc_greeting.ogg
      ja/
        npc_greeting.ogg
```

Most indie 2D games skip voice localization (expensive). If you have voice, prioritize subtitles — they're cheaper and cover more languages.

---

## Date, Number, and Currency Formatting

Use `CultureInfo` for locale-aware formatting:

```csharp
var culture = new CultureInfo("de-DE");

// Numbers
string gold = 12345.ToString("N0", culture);    // "12.345" (German uses . as thousands separator)

// Currency
string price = 9.99m.ToString("C", culture);     // "9,99 €"

// Dates
string date = DateTime.Now.ToString("d", culture); // "07.03.2026"

// Time
string time = DateTime.Now.ToString("t", culture); // "03:28"
```

| Format | en-US | de-DE | ja-JP |
|--------|-------|-------|-------|
| Number: 1234.5 | 1,234.5 | 1.234,5 | 1,234.5 |
| Currency | $9.99 | 9,99 € | ¥999 |
| Short date | 3/7/2026 | 07.03.2026 | 2026/03/07 |

**In-game relevance:** Leaderboard scores, play time, save timestamps, shop prices.

---

## RTL (Right-to-Left) Support

Arabic and Hebrew read right-to-left. This affects both text rendering and UI layout.

### Text Rendering

FontStashSharp doesn't handle RTL shaping natively. Options:

1. **HarfBuzzSharp** — C# bindings for the HarfBuzz text shaping engine. Handles Arabic ligatures, RTL reordering, and bidirectional text.
2. **Pre-shaped text** — Run text through a shaping pass before rendering.

```csharp
// Simplified RTL text reversal (basic — doesn't handle Arabic shaping)
public string PrepareRTL(string text)
{
    // For proper Arabic, use HarfBuzz. This handles simple Hebrew.
    char[] chars = text.ToCharArray();
    Array.Reverse(chars);
    return new string(chars);
}
```

**For production Arabic support, use HarfBuzzSharp.** Arabic letters change shape based on position (initial, medial, final, isolated), and naive reversal won't work.

### UI Mirroring

When the locale is RTL, mirror the entire UI:

| LTR Layout | RTL Layout |
|------------|------------|
| Menu items left-aligned | Menu items right-aligned |
| Back button → top-left | Back button → top-right |
| Health bar fills left→right | Health bar fills right→left |
| Text left-aligned | Text right-aligned |
| List numbering: 1. 2. 3. on left | Numbering on right |

```csharp
public bool IsRTL => CurrentLocale is "ar" or "he" or "fa" or "ur";

public float AnchorX(float x, float width) => 
    IsRTL ? (ScreenWidth - x - width) : x;
```

**Exception:** Don't mirror gameplay controls (left = left, right = right), timelines, or music notation.

---

## Translation Workflow

### 1. String Extraction

Maintain a master `en.json` as the source of truth. Track string additions/changes:

```bash
# Diff to find new/changed keys
diff <(jq -r 'keys[]' en.json | sort) <(jq -r 'keys[]' ja.json | sort)
```

### 2. Translator Handoff

Provide translators with:

- The JSON file for their language
- A **context document** explaining each key (where it appears, character limits)
- Screenshots showing where strings appear in-game

Example context sheet:

| Key | Context | Max Chars | Screenshot |
|-----|---------|-----------|------------|
| `menu.start` | Main menu, primary button | 20 | menu_01.png |
| `dialog.npc.greeting` | First NPC in town, speech bubble | 60 | dialog_01.png |
| `item.potion.desc` | Inventory tooltip. {0} = HP amount | 80 | inv_01.png |

### 3. Translation Testing

- **Pseudo-localization:** Replace all text with accented versions (`Start Game` → `§tàrt Gàmé`) to spot hardcoded strings and layout issues
- **Length testing:** Generate strings at 150% length to find overflow
- **Screenshot comparison:** Capture every screen in every language

```csharp
// Pseudo-localization generator
public static string Pseudolocalize(string input)
{
    var map = new Dictionary<char, char>
    {
        {'a','à'}, {'e','é'}, {'i','ì'}, {'o','ó'}, {'u','ù'},
        {'A','À'}, {'E','É'}, {'I','Ì'}, {'O','Ó'}, {'U','Ù'},
    };
    
    var sb = new StringBuilder("§");
    foreach (char c in input)
        sb.Append(map.GetValueOrDefault(c, c));
    sb.Append('§');
    
    // Pad to 130% length
    int padCount = (int)(input.Length * 0.3f);
    sb.Append(new string('~', padCount));
    
    return sb.ToString();
}
```

---

## Community Translation Support

Make your localization files modding-friendly:

### Discoverable Format

```
MyGame/
  Content/
    localization/
      en.json          ← shipped
      ja.json          ← shipped
  Mods/
    localization/
      pl.json          ← community translation
      tr.json          ← community translation
```

### Load Order

```csharp
private Dictionary<string, string> LoadFile(string locale)
{
    var strings = new Dictionary<string, string>();
    
    // 1. Load base (shipped) translations
    string basePath = $"Content/localization/{locale}.json";
    if (File.Exists(basePath))
        MergeFrom(strings, basePath);
    
    // 2. Override with mod translations
    string modPath = $"Mods/localization/{locale}.json";
    if (File.Exists(modPath))
        MergeFrom(strings, modPath);
    
    return strings;
}
```

### Translation Template

Ship an `_template.json` with all keys and empty values:

```json
{
  "_meta": {
    "language": "",
    "translator": "",
    "version": "1.0",
    "game_version": "1.2.0"
  },
  "menu.start": "",
  "menu.options": "",
  "menu.quit": ""
}
```

This gives community translators a starting point and makes it clear which strings need translation.

---

## Pluralization and Gender

Some languages have complex plural rules (Russian has 3 forms, Arabic has 6):

```csharp
// Simple approach: provide variants per key
// en.json
{
  "item.count.one": "{0} item",
  "item.count.other": "{0} items"
}

// ru.json  (Russian: one, few, many, other)
{
  "item.count.one": "{0} предмет",
  "item.count.few": "{0} предмета",
  "item.count.many": "{0} предметов",
  "item.count.other": "{0} предметов"
}
```

For a robust solution, implement ICU MessageFormat or use a library like `MessageFormat.NET`.

---

## Checklist

- [ ] All player-visible strings externalized to JSON (zero hardcoded strings)
- [ ] FontStashSharp with Noto font fallback chain for CJK/Arabic/Cyrillic
- [ ] UI containers auto-size to fit translated text
- [ ] No text baked into sprites (or localized sprite variants exist)
- [ ] Dates/numbers formatted with `CultureInfo`
- [ ] Pseudo-localization test passes (no broken layouts at 150% string length)
- [ ] Translation context document for each string key
- [ ] Hot-reload works in dev builds
- [ ] Community translation folder scanned at startup
- [ ] RTL layout mirroring implemented (if supporting Arabic/Hebrew)

---

## Further Reading

- [FontStashSharp](https://github.com/FontStashSharp/FontStashSharp) — Runtime font rendering for MonoGame
- [HarfBuzzSharp](https://github.com/nickvdyck/harfbuzz-sharp) — Text shaping for complex scripts
- [ICU MessageFormat](https://unicode-org.github.io/icu/userguide/format_parse/messages/) — Pluralization and gender rules
- [Noto Fonts](https://fonts.google.com/noto) — Free Unicode coverage
- [Localization Best Practices for Games (GDC)](https://www.youtube.com/results?search_query=gdc+localization+games)
