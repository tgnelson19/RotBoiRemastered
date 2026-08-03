using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.UI;

namespace RotBoiRemastered.World;

public readonly record struct LightingTheme(
    Color DarknessTint,
    byte DarknessAlpha,
    Color Glow,
    Color Core,
    float PlayerRadiusTiles,
    float FixtureRadiusTiles);

public readonly record struct ArenaLightPost(
    Vector2 WorldPosition,
    int Biome,
    int Variant);

public readonly record struct WorldLightSource(
    Vector2 WorldPosition,
    float Height,
    float Radius,
    float Intensity,
    float Seed);

/// <summary>
/// Screen-space atmosphere and emissive fixtures for combat maps. Darkness
/// is applied before fog-of-war, then deterministic path-colored light is
/// added from the player, authored landmarks, and standalone-arena posts.
/// Fog therefore remains the final authority over unexplored space.
/// </summary>
public sealed class WorldLighting
{
    private const int GlowTextureSize = 128;
    private static readonly RasterizerState ScissorRasterizerState = new()
    {
        ScissorTestEnable = true,
        CullMode = CullMode.None,
    };

    private Texture2D? _radialGlow;
    private GraphicsDevice? _resourceDevice;

    public static LightingTheme ThemeFor(string pathKey) => pathKey switch
    {
        "touch" => new LightingTheme(
            new Color(3, 8, 6), 124,
            new Color(111, 155, 82), new Color(209, 218, 137),
            4.8f, 4.5f),
        "sight" => new LightingTheme(
            new Color(3, 7, 12), 101,
            new Color(102, 195, 226), new Color(218, 247, 255),
            5.5f, 5.1f),
        "phantasia" => new LightingTheme(
            new Color(8, 3, 13), 119,
            new Color(190, 87, 182), new Color(245, 199, 241),
            5.0f, 4.8f),
        "chemesthesis" => new LightingTheme(
            new Color(12, 5, 2), 116,
            new Color(222, 91, 43), new Color(255, 197, 107),
            5.0f, 4.7f),
        _ => new LightingTheme(
            new Color(6, 6, 9), 108,
            new Color(211, 184, 119), new Color(255, 235, 185),
            5.2f, 4.8f),
    };

    public static float Flicker(float time, float seed)
    {
        float slow = MathF.Sin(time * 4.1f + seed * 1.73f) * .035f;
        float quick = MathF.Sin(time * 9.7f + seed * 3.11f) * .018f;
        return Math.Clamp(.94f + slow + quick, .84f, 1f);
    }

    public static bool IsLuminousDecoration(PathDecorationKind kind) => kind is
        PathDecorationKind.Valve
        or PathDecorationKind.Pump
        or PathDecorationKind.PressureTank
        or PathDecorationKind.LensBuoy
        or PathDecorationKind.MirrorArch
        or PathDecorationKind.LightningRod
        or PathDecorationKind.EchoPylon
        or PathDecorationKind.Chime
        or PathDecorationKind.OrganStack
        or PathDecorationKind.PrismObelisk
        or PathDecorationKind.OrbitShrine
        or PathDecorationKind.LanternSpire
        or PathDecorationKind.FurnaceIdol;

    public static List<WorldLightSource> BuildPathLightSources(
        Battleground battleground,
        string pathKey)
    {
        LightingTheme theme = ThemeFor(pathKey);
        var sources = new List<WorldLightSource>();
        foreach (PathDecoration decoration in battleground.PathDecorations)
        {
            if (decoration.Layer != PathDecorationLayer.Raised
                || !IsLuminousDecoration(decoration.Kind))
            {
                continue;
            }

            float scale = decoration.Scale;
            float height = Math.Max(0f,
                LightHeight(decoration.Kind) * scale
                - Battleground.TileSize * .32f);
            float radius = Simulation.TileSize
                * (theme.FixtureRadiusTiles + Math.Min(1.4f, scale * .16f));
            float intensity = Math.Clamp(.46f + scale * .07f, .5f, .9f);
            sources.Add(new WorldLightSource(
                decoration.WorldPosition,
                height,
                radius,
                intensity,
                decoration.Variant * 1.71f + decoration.RoomId * .37f));
        }
        return sources;
    }

    /// <summary>
    /// Places non-colliding light posts on a staggered grid over standalone
    /// arenas. Candidates resolve to nearby walkable tiles, stay away from the
    /// outer shell, and are deduplicated so every map receives broad, stable
    /// coverage without changing navigation.
    /// </summary>
    public static List<ArenaLightPost> BuildArenaLightPosts(
        Battleground battleground)
    {
        int spacing = Math.Clamp(
            Math.Min(battleground.Width, battleground.Height) / 7,
            11,
            15);
        int margin = Math.Max(5, spacing / 2);
        var used = new HashSet<Point>();
        var posts = new List<ArenaLightPost>();
        int row = 0;
        for (int y = margin; y < battleground.Height - margin; y += spacing)
        {
            int stagger = row++ % 2 == 0 ? 0 : spacing / 2;
            for (int x = margin + stagger;
                 x < battleground.Width - margin;
                 x += spacing)
            {
                int hash = Math.Abs(x * 43 + y * 89 + x * y * 3);
                Point requested = new(
                    x + hash % 5 - 2,
                    y + hash / 7 % 5 - 2);
                if (!TryFindWalkableTile(
                        battleground, requested, 4, used, out Point tile))
                {
                    continue;
                }

                used.Add(tile);
                posts.Add(new ArenaLightPost(
                    new Vector2(
                        (tile.X + .5f) * Simulation.TileSize,
                        (tile.Y + .5f) * Simulation.TileSize),
                    battleground.BiomeForTile(tile.X, tile.Y),
                    hash % 4));
            }
        }
        return posts;
    }

    public static WorldLightSource SourceFor(
        ArenaLightPost post,
        LightingTheme theme) =>
        new(
            post.WorldPosition,
            35f - Battleground.TileSize * .27f,
            Simulation.TileSize * theme.FixtureRadiusTiles,
            .72f,
            post.Variant * 2.17f + post.WorldPosition.X * .0013f);

    public void DrawAtmosphere(
        SpriteBatch spriteBatch,
        GraphicsDevice graphicsDevice,
        Rectangle viewport,
        Camera camera,
        Vector2 playerWorldPosition,
        Vector2 screenShake,
        string pathKey,
        float time,
        IReadOnlyList<WorldLightSource> sources,
        PathFogOfWar? visibilityFog)
    {
        if (viewport.Width <= 0 || viewport.Height <= 0)
            return;

        EnsureResources(graphicsDevice);
        LightingTheme theme = ThemeFor(pathKey);
        Rectangle previousScissor = graphicsDevice.ScissorRectangle;
        graphicsDevice.ScissorRectangle = viewport;

        spriteBatch.Begin(
            blendState: BlendState.AlphaBlend,
            samplerState: SamplerState.LinearClamp,
            rasterizerState: ScissorRasterizerState);
        Primitives2D.FillRect(spriteBatch, viewport,
            new Color(
                theme.DarknessTint.R,
                theme.DarknessTint.G,
                theme.DarknessTint.B,
                theme.DarknessAlpha));
        spriteBatch.End();

        spriteBatch.Begin(
            blendState: BlendState.Additive,
            samplerState: SamplerState.LinearClamp,
            rasterizerState: ScissorRasterizerState);
        DrawLight(
            spriteBatch,
            viewport,
            camera,
            playerWorldPosition,
            screenShake,
            playerWorldPosition,
            height: 0,
            Simulation.TileSize * theme.PlayerRadiusTiles,
            intensity: .72f,
            theme,
            Flicker(time, 0));
        for (int index = 0; index < sources.Count; index++)
        {
            WorldLightSource source = sources[index];
            if (visibilityFog is not null
                && !visibilityFog.IsWorldVisible(source.WorldPosition))
            {
                continue;
            }
            DrawLight(
                spriteBatch,
                viewport,
                camera,
                playerWorldPosition,
                screenShake,
                source.WorldPosition,
                source.Height,
                source.Radius,
                source.Intensity,
                theme,
                Flicker(time, source.Seed));
        }
        spriteBatch.End();
        graphicsDevice.ScissorRectangle = previousScissor;
    }

    public static void DrawLightPost(
        SpriteBatch spriteBatch,
        Camera camera,
        Vector2 playerWorldPosition,
        Vector2 screenShake,
        ArenaLightPost post,
        string pathKey,
        float time)
    {
        LightingTheme theme = ThemeFor(pathKey);
        Vector2 center = camera.WorldToScreen(
            post.WorldPosition, playerWorldPosition, screenShake);
        float floorY = center.Y + Simulation.TileSize * .27f;
        float flicker = Flicker(time, post.Variant * 2.17f);
        Color metal = Color.Lerp(UiTheme.Ink, theme.Glow, .2f);
        Color core = Color.Lerp(theme.Glow, theme.Core, flicker * .7f);

        Primitives2D.FillEllipse(spriteBatch,
            new Rectangle((int)center.X - 15, (int)floorY - 4, 32, 11),
            UiTheme.Shadow * .9f);
        Primitives2D.FillRect(spriteBatch,
            new Rectangle((int)center.X - 4, (int)floorY - 35, 8, 36),
            UiTheme.Ink);
        Primitives2D.FillRect(spriteBatch,
            new Rectangle((int)center.X - 2, (int)floorY - 33, 4, 32),
            metal);
        Primitives2D.Line(spriteBatch,
            new Vector2(center.X - 10, floorY),
            new Vector2(center.X, floorY - 8), metal, 3);
        Primitives2D.Line(spriteBatch,
            new Vector2(center.X + 10, floorY),
            new Vector2(center.X, floorY - 8), metal, 3);

        Vector2 lamp = new(center.X, floorY - 35);
        switch (pathKey)
        {
            case "touch":
                Primitives2D.FillRect(spriteBatch,
                    new Rectangle((int)lamp.X - 8, (int)lamp.Y - 7, 16, 14),
                    UiTheme.Ink);
                Primitives2D.FillRect(spriteBatch,
                    new Rectangle((int)lamp.X - 5, (int)lamp.Y - 4, 10, 8),
                    core);
                break;
            case "sight":
                Primitives2D.FillPolygonSpan(spriteBatch, stackalloc Vector2[]
                {
                    lamp + new Vector2(0, -11), lamp + new Vector2(9, 0),
                    lamp + new Vector2(0, 11), lamp + new Vector2(-9, 0),
                }, UiTheme.Ink);
                Primitives2D.FillCircle(spriteBatch, lamp, 5, core);
                break;
            case "phantasia":
                Primitives2D.CircleOutline(spriteBatch, lamp, 12,
                    theme.Glow * .75f, 3, 24);
                Primitives2D.FillCircle(spriteBatch, lamp, 6, core);
                break;
            case "chemesthesis":
                Primitives2D.FillPolygonSpan(spriteBatch, stackalloc Vector2[]
                {
                    lamp + new Vector2(-8, 7), lamp + new Vector2(-5, -3),
                    lamp + new Vector2(0, -12 - flicker * 3),
                    lamp + new Vector2(6, -2), lamp + new Vector2(8, 7),
                }, core);
                Primitives2D.Line(spriteBatch, lamp + new Vector2(-10, 8),
                    lamp + new Vector2(10, 8), metal, 4);
                break;
            default:
                float sway = VisualAnimation.Sine(time + post.Variant, 2.8f) * 2f;
                Primitives2D.Line(spriteBatch, lamp - new Vector2(9, 8),
                    lamp + new Vector2(9, -8), metal, 3);
                Primitives2D.FillRect(spriteBatch,
                    new Rectangle((int)(lamp.X - 6 + sway), (int)lamp.Y - 6, 12, 12),
                    core);
                break;
        }
        Primitives2D.CircleOutline(spriteBatch, lamp,
            13f + flicker * 2f, theme.Glow * (.18f + flicker * .18f), 2, 20);
    }

    private void EnsureResources(GraphicsDevice graphicsDevice)
    {
        if (_radialGlow is not null
            && ReferenceEquals(_resourceDevice, graphicsDevice))
        {
            return;
        }

        _radialGlow?.Dispose();
        _radialGlow = new Texture2D(
            graphicsDevice, GlowTextureSize, GlowTextureSize);
        var pixels = new Color[GlowTextureSize * GlowTextureSize];
        float center = (GlowTextureSize - 1) * .5f;
        for (int y = 0; y < GlowTextureSize; y++)
        {
            for (int x = 0; x < GlowTextureSize; x++)
            {
                float dx = (x - center) / center;
                float dy = (y - center) / center;
                float distance = MathF.Sqrt(dx * dx + dy * dy);
                float falloff = VisualAnimation.SmoothStep(1f - distance);
                falloff *= falloff;
                pixels[y * GlowTextureSize + x] = new Color(
                    255, 255, 255,
                    Math.Clamp((int)MathF.Round(falloff * 255f), 0, 255));
            }
        }
        _radialGlow.SetData(pixels);
        _resourceDevice = graphicsDevice;
    }

    private void DrawLight(
        SpriteBatch spriteBatch,
        Rectangle viewport,
        Camera camera,
        Vector2 playerWorldPosition,
        Vector2 screenShake,
        Vector2 sourceWorld,
        float height,
        float radius,
        float intensity,
        LightingTheme theme,
        float flicker)
    {
        Vector2 logical = camera.WorldToScreen(
            sourceWorld, playerWorldPosition, screenShake)
            - new Vector2(0, height);
        Vector2 display = camera.ApplyZoom(logical);
        float displayRadius = radius * camera.Zoom * flicker;
        if (display.X + displayRadius < viewport.Left
            || display.X - displayRadius > viewport.Right
            || display.Y + displayRadius < viewport.Top
            || display.Y - displayRadius > viewport.Bottom)
        {
            return;
        }

        Rectangle outer = Centered(display, displayRadius * 2f);
        spriteBatch.Draw(_radialGlow!, outer,
            theme.Glow * Math.Clamp(intensity * .42f, 0f, .72f));
        Rectangle inner = Centered(display, displayRadius * .84f);
        spriteBatch.Draw(_radialGlow!, inner,
            theme.Core * Math.Clamp(intensity * .28f, 0f, .58f));
    }

    private static Rectangle Centered(Vector2 center, float size) =>
        new(
            (int)MathF.Round(center.X - size / 2f),
            (int)MathF.Round(center.Y - size / 2f),
            Math.Max(1, (int)MathF.Round(size)),
            Math.Max(1, (int)MathF.Round(size)));

    private static float LightHeight(PathDecorationKind kind) => kind switch
    {
        PathDecorationKind.LanternSpire => 29f,
        PathDecorationKind.LightningRod => 46f,
        PathDecorationKind.PrismObelisk => 34f,
        PathDecorationKind.EchoPylon => 31f,
        PathDecorationKind.OrganStack => 30f,
        PathDecorationKind.MirrorArch => 25f,
        PathDecorationKind.LensBuoy => 24f,
        PathDecorationKind.PressureTank => 25f,
        PathDecorationKind.FurnaceIdol => 22f,
        _ => 20f,
    };

    private static bool TryFindWalkableTile(
        Battleground battleground,
        Point requested,
        int searchRadius,
        HashSet<Point> used,
        out Point result)
    {
        for (int radius = 0; radius <= searchRadius; radius++)
        {
            for (int offsetY = -radius; offsetY <= radius; offsetY++)
            {
                for (int offsetX = -radius; offsetX <= radius; offsetX++)
                {
                    if (radius > 0
                        && Math.Abs(offsetX) != radius
                        && Math.Abs(offsetY) != radius)
                    {
                        continue;
                    }
                    int x = requested.X + offsetX;
                    int y = requested.Y + offsetY;
                    if (x < 2 || y < 2
                        || x >= battleground.Width - 2
                        || y >= battleground.Height - 2)
                    {
                        continue;
                    }
                    Point candidate = new(x, y);
                    TileType tile = battleground.TileAt(x, y);
                    if (!tile.IsSolid() && !used.Contains(candidate))
                    {
                        result = candidate;
                        return true;
                    }
                }
            }
        }
        result = default;
        return false;
    }
}
