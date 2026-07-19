using Microsoft.Xna.Framework.Graphics;

namespace RotBoiRemastered.Core;

/// <summary>
/// Optional sprite overlay for the game's otherwise fully-procedural
/// (Primitives2D-drawn) rendering. Raw-loads PNGs straight off disk via
/// Texture2D.FromStream, the same way UiTheme.Initialize loads the font --
/// bypassing the MonoGame Content Pipeline (Content.mgcb) entirely so a new
/// sprite just needs to be dropped under Content/Sprites/ (copied to the
/// output directory by the wildcard &lt;None&gt; glob in the .csproj) with no
/// pipeline manifest entry to maintain per file.
///
/// Every lookup is a soft miss: a key with no matching file returns null
/// rather than throwing, so callers (ItemCards, ProjectileVisuals, Player)
/// can fall back to their existing procedural drawing for anything not yet
/// skinned. That's what lets sprites be added one at a time instead of
/// needing full art coverage before any of it can ship.
/// </summary>
public static class Sprites
{
    private const string RootDirectory = "Content/Sprites";

    private static GraphicsDevice? _graphicsDevice;
    private static readonly Dictionary<string, Texture2D?> Cache = new();

    public static void Initialize(GraphicsDevice graphicsDevice)
    {
        _graphicsDevice = graphicsDevice;
        Cache.Clear();
    }

    /// <summary>
    /// Looks up e.g. "Weapons/dagger" against Content/Sprites/Weapons/dagger.png
    /// (relative to the running executable, matching where the build copies
    /// everything else). Keys are derived directly from existing data --
    /// ItemDefinition.VisualKind, ProjectileDesign.Id -- rather than a
    /// hand-maintained id-to-file table, so there's nothing to keep in sync.
    /// </summary>
    public static Texture2D? TryGet(string key)
    {
        if (Cache.TryGetValue(key, out var cached))
            return cached;
        var texture = Load(key);
        Cache[key] = texture;
        return texture;
    }

    private static Texture2D? Load(string key)
    {
        if (_graphicsDevice is null)
            return null;
        string path = Path.Combine(AppContext.BaseDirectory, RootDirectory, key + ".png");
        if (!File.Exists(path))
            return null;
        using var stream = File.OpenRead(path);
        // MonoGame's DesktopGL Texture2D.FromStream does not premultiply
        // alpha, unlike content-pipeline-built textures -- fine with the
        // default BlendState.AlphaBlend for fully opaque or fully
        // transparent pixels, but semi-transparent edges (soft-antialiased
        // sprite edges) may show a faint dark fringe. Switch affected draw
        // calls to BlendState.NonPremultiplied if that's visible once real
        // art with soft edges is in.
        return Texture2D.FromStream(_graphicsDevice, stream);
    }
}
