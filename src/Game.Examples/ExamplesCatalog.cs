using System.Text.Json.Serialization;
using Game.Engine.ECS;

namespace Game.Examples;

/// <summary>
/// Static catalog of every demo scene. <see cref="ExampleInfo.Id"/> is the source of
/// truth: the SSR payload embeds it, and the Game.UI TypeScript scene registry keys
/// off the same string. A mismatch renders nothing and logs to the console.
/// </summary>
public static class ExamplesCatalog
{
    public sealed record ExampleInfo(string Id, string Title, string Group, string SourceUrl);

    public static readonly IReadOnlyList<ExampleInfo> All = new List<ExampleInfo>
    {
        new("basic/container", "Container", "Basic", "https://pixijs.com/8.x/examples/basic/container"),
        new("basic/container-pivot", "Container Pivot", "Basic", "https://pixijs.com/8.x/examples/basic/container-pivot"),
        new("basic/blend-modes", "Blend Modes", "Basic", "https://pixijs.com/8.x/examples/basic/blend-modes"),
        new("basic/bitmap-text", "Bitmap Text", "Text", "https://pixijs.com/8.x/examples/text/bitmap-text"),
        new("basic/bitmap-text2", "Bitmap Text 2", "Text", "https://pixijs.com/8.x/examples/text/bitmap-text"),
        new("basic/from-font", "From Font", "Text", "https://pixijs.com/8.x/examples/text/from-font"),
        new("basic/pixi-text", "Pixi Text", "Text", "https://pixijs.com/8.x/examples/text/pixi-text"),
        new("sprite/basic", "Basic Sprite", "Sprite", "https://pixijs.com/8.x/examples/sprite/basic"),
        new("sprite/animated-sprite", "Animated Sprite", "Sprite", "https://pixijs.com/8.x/examples/sprite/animated-sprite"),
        new("sprite/tiling-sprite", "Tiling Sprite", "Sprite", "https://pixijs.com/8.x/examples/sprite/tiling-sprite"),
        new("graphics/simple-graphics", "Simple Graphics", "Graphics", "https://pixijs.com/8.x/examples/graphics/simple"),
        new("filters/blur-filter", "Blur Filter", "Filters", "https://pixijs.com/8.x/examples/filters-blur/blur"),
        new("masks/graphics-mask", "Graphics Mask", "Masks", "https://pixijs.com/8.x/examples/masks/graphics"),
        new("meshes/mesh-rope", "Mesh Rope", "Meshes", "https://pixijs.com/8.x/examples/mesh-and-shaders/snake"),
        new("events/dragging", "Dragging", "Events", "https://pixijs.com/8.x/examples/events/dragging"),
        new("textures/render-texture", "Render Texture", "Textures", "https://pixijs.com/8.x/examples/textures/render-texture"),
        new("assets/asset-bundle", "Asset Bundle", "Assets", "https://pixijs.com/8.x/examples/assets/bundle"),
        new("advanced/star-warp", "Star Warp", "Advanced", "https://pixijs.com/8.x/examples/advanced/star-warp"),
        new("ecs/sprites", "ECS Sprites", "ECS", "https://github.com/genaray/Arch"),
        new("games/snake", "Snake", "Games", "https://github.com/JDStraughan/html5-snake"),
        new("games/tetris", "Tetris", "Games", "https://github.com/jakesgordon/javascript-tetris"),
        new("games/breakout", "Breakout", "Games", "https://github.com/jakesgordon/javascript-breakout"),
        new("games/asteroids", "Asteroids", "Games", "https://github.com/aesalazar/AsteroidsWasm"),
    };

    /// <summary>Group name that separates game scenes from plain PixiJS examples.</summary>
    public const string GamesGroup = "Games";

    /// <summary>Game scenes only (rendered by their own scene pipeline, not the PixiJS example loader).</summary>
    public static IEnumerable<ExampleInfo> Games => All.Where(e => e.Group == GamesGroup);

    /// <summary>Plain PixiJS examples (everything except game scenes).</summary>
    public static IEnumerable<ExampleInfo> Examples => All.Where(e => e.Group != GamesGroup);

    public static ExampleInfo? Find(string id) =>
        All.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));

    public static IEnumerable<string> Groups => All.Select(e => e.Group).Distinct();
}

/// <summary>Serialized into <c>#pixi-viewport[data-message]</c> for the client bootstrap script.
/// <c>Sprites</c>/<c>StreamUrl</c> are ECS-specific and null for plain PixiJS examples.</summary>
public sealed record ExamplePayload(string ExampleId, string Title, string SourceUrl,
    IReadOnlyList<SpriteState>? Sprites = null, string? StreamUrl = null, SnakeScenePayload? Snake = null,
    TetrisScenePayload? Tetris = null, BreakoutScenePayload? Breakout = null, AsteroidsScenePayload? Asteroids = null);

/// <summary>Snake scene payload: initial snapshot, grid metrics and the live SSE stream URL.</summary>
public sealed record SnakeScenePayload(
    IReadOnlyList<SpriteState> Sprites,
    int Score,
    bool GameOver,
    bool Started,
    int GridWidth,
    int GridHeight,
    float CellSize,
    string StreamUrl);

/// <summary>Client input for the snake scene: a suggested direction the sim validates.</summary>
public sealed record SnakeInputRequest(string Direction);

/// <summary>Tetris scene payload: initial snapshot, grid metrics and the live SSE stream URL.</summary>
public sealed record TetrisScenePayload(
    IReadOnlyList<SpriteState> Sprites,
    int Score,
    int Rows,
    int Level,
    bool GameOver,
    bool Started,
    int GridWidth,
    int GridHeight,
    float CellSize,
    string StreamUrl);

/// <summary>Client input for the tetris scene: a suggested command (left/right/rotate/down) the sim validates.</summary>
public sealed record TetrisInputRequest(string Command);

/// <summary>Breakout scene payload: initial snapshot, court metrics and the live SSE stream URL.</summary>
public sealed record BreakoutScenePayload(
    IReadOnlyList<BreakoutSpriteState> Sprites,
    int Score,
    int Lives,
    int Level,
    bool GameOver,
    bool Started,
    float CourtWidth,
    float CourtHeight,
    float ChunkSize,
    string StreamUrl);

/// <summary>Final-position report of the Rapier food drop (cell coordinates).</summary>
public sealed record FoodDroppedRequest(int X, int Y);

/// <summary>Asteroids scene payload: initial snapshot, court metrics and the live SSE stream URL.</summary>
public sealed record AsteroidsScenePayload(
    IReadOnlyList<AsteroidsSpriteState> Sprites,
    int Score,
    int HighScore,
    int Lives,
    int Level,
    bool GameOver,
    bool Started,
    float CourtWidth,
    float CourtHeight,
    string StreamUrl);
