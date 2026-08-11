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

public enum LightMotionStyle
{
    Player,
    Touch,
    Sight,
    Sound,
    Phantasia,
    Chemesthesis,
}

public readonly record struct LightAnimationSample(
    float Intensity,
    float Radius,
    float Halo,
    float VerticalDrift);

public readonly record struct WorldLightSource(
    Vector2 WorldPosition,
    float Height,
    float Radius,
    float Intensity,
    float Seed,
    LightMotionStyle MotionStyle);

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
            new Color(3, 8, 6), 158,
            new Color(111, 155, 82), new Color(209, 218, 137),
            3.744f, 5.31f),
        "sight" => new LightingTheme(
            new Color(3, 7, 12), 138,
            new Color(102, 195, 226), new Color(218, 247, 255),
            4.29f, 6.018f),
        "phantasia" => new LightingTheme(
            new Color(8, 3, 13), 154,
            new Color(190, 87, 182), new Color(245, 199, 241),
            3.9f, 5.664f),
        "aphantasia" => new LightingTheme(
            new Color(2, 6, 18), 132,
            new Color(58, 116, 216), new Color(150, 205, 255),
            4.8f, 6.4f),
        "chemesthesis" => new LightingTheme(
            new Color(12, 5, 2), 151,
            new Color(222, 91, 43), new Color(255, 197, 107),
            3.9f, 5.546f),
        _ => new LightingTheme(
            new Color(6, 6, 9), 145,
            new Color(211, 184, 119), new Color(255, 235, 185),
            4.056f, 5.664f),
    };

    public static LightMotionStyle StyleForPath(string pathKey) => pathKey switch
    {
        "touch" => LightMotionStyle.Touch,
        "sight" => LightMotionStyle.Sight,
        "phantasia" => LightMotionStyle.Phantasia,
        "chemesthesis" => LightMotionStyle.Chemesthesis,
        _ => LightMotionStyle.Sound,
    };

    public static LightMotionStyle StyleForDecoration(PathDecorationKind kind) => kind switch
    {
        PathDecorationKind.Valve
            or PathDecorationKind.Pump
            or PathDecorationKind.PressureTank => LightMotionStyle.Touch,
        PathDecorationKind.LensBuoy
            or PathDecorationKind.MirrorArch
            or PathDecorationKind.LightningRod => LightMotionStyle.Sight,
        PathDecorationKind.EchoPylon
            or PathDecorationKind.Chime
            or PathDecorationKind.OrganStack => LightMotionStyle.Sound,
        PathDecorationKind.PrismObelisk
            or PathDecorationKind.OrbitShrine => LightMotionStyle.Phantasia,
        PathDecorationKind.LanternSpire
            or PathDecorationKind.FurnaceIdol => LightMotionStyle.Chemesthesis,
        _ => LightMotionStyle.Sound,
    };

    /// <summary>
    /// Samples a band-limited, deterministic animation curve. Every term is
    /// continuous, so a source can never pop even when optional VFX density
    /// changes. Base illumination is represented by the neutral (1, 1, 1, 0)
    /// sample and therefore survives a zero motion-strength setting.
    /// </summary>
    public static LightAnimationSample SampleMotion(
        float time,
        float seed,
        LightMotionStyle style,
        float motionStrength)
    {
        float motion = Math.Clamp(motionStrength, 0f, 1f);
        if (motion <= 0f)
            return new LightAnimationSample(1f, 1f, 1f, 0f);

        if (style == LightMotionStyle.Player)
        {
            float breath = MathF.Sin(time * .57f + seed * 1.13f);
            return new LightAnimationSample(
                1f + breath * .004f * motion,
                1f + breath * .002f * motion,
                1f + breath * .006f * motion,
                0f);
        }

        (float slowSpeed, float shimmerSpeed, float slowAmount,
            float shimmerAmount, float driftAmount, float phaseOffset) = style switch
        {
            LightMotionStyle.Touch => (.58f, 1.45f, .048f, .018f, .55f, .4f),
            LightMotionStyle.Sight => (.52f, 1.28f, .034f, .012f, .28f, 1.2f),
            LightMotionStyle.Phantasia => (.64f, 1.42f, .054f, .020f, 1.35f, 2.1f),
            LightMotionStyle.Chemesthesis => (.76f, 1.75f, .058f, .024f, 1.7f, 2.8f),
            _ => (.70f, 1.62f, .046f, .017f, .8f, 3.6f),
        };

        float slowPhase = time * slowSpeed + seed * 1.73f + phaseOffset;
        float shimmerPhase = time * shimmerSpeed + seed * 3.11f + phaseOffset * .47f;
        float slow = MathF.Sin(slowPhase);
        float shimmer = MathF.Sin(shimmerPhase);
        // A high, even power turns a very slow wave into an occasional soft
        // valley without any threshold or discontinuous branch.
        float dipWave = .5f + .5f * MathF.Sin(time * .31f + seed * 2.17f);
        float softDip = MathF.Pow(dipWave, 8f) * .024f;

        float intensity = 1f + motion * (
            slow * slowAmount + shimmer * shimmerAmount - softDip);
        float radius = 1f + motion * (
            slow * .016f + shimmer * .008f);
        float halo = 1f + motion * (
            MathF.Sin(slowPhase - .72f) * .075f + shimmer * .025f);
        float drift = motion * driftAmount * MathF.Sin(
            time * (slowSpeed * .81f) + seed * 1.37f + phaseOffset);

        return new LightAnimationSample(
            Math.Clamp(intensity, .88f, 1.07f),
            Math.Clamp(radius, .97f, 1.03f),
            Math.Clamp(halo, .88f, 1.12f),
            Math.Clamp(drift, -1.8f, 1.8f));
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
                decoration.Variant * 1.71f + decoration.RoomId * .37f,
                StyleForDecoration(decoration.Kind)));
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
            10,
            12);
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
        LightingTheme theme,
        string pathKey) =>
        new(
            post.WorldPosition,
            35f - Battleground.TileSize * .27f,
            Simulation.TileSize * theme.FixtureRadiusTiles,
            .72f,
            post.Variant * 2.17f + post.WorldPosition.X * .0013f,
            StyleForPath(pathKey));

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
        PathFogOfWar? visibilityFog,
        float motionStrength,
        bool highContrast,
        float darknessScale = 1f,
        float playerLightScale = 1f)
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
                (byte)Math.Clamp((int)Math.Round(theme.DarknessAlpha * darknessScale), 0, 255)));
        spriteBatch.End();

        spriteBatch.Begin(
            blendState: BlendState.Additive,
            samplerState: SamplerState.LinearClamp,
            rasterizerState: ScissorRasterizerState);
        float playerRadius = Simulation.TileSize * theme.PlayerRadiusTiles;
        float playerIntensity = .56f * playerLightScale;
        if (highContrast)
        {
            playerRadius *= 1.12f;
            playerIntensity = .68f;
        }
        DrawLight(
            spriteBatch,
            viewport,
            camera,
            playerWorldPosition,
            screenShake,
            playerWorldPosition,
            height: 0,
            playerRadius,
            playerIntensity,
            theme,
            SampleMotion(time, 0f, LightMotionStyle.Player, motionStrength));
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
                source.Intensity * 1.08f,
                theme,
                SampleMotion(time, source.Seed, source.MotionStyle, motionStrength));
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
        float time,
        float motionStrength)
    {
        LightingTheme theme = ThemeFor(pathKey);
        Vector2 center = camera.WorldToScreen(
            post.WorldPosition, playerWorldPosition, screenShake);
        float floorY = center.Y + Simulation.TileSize * .27f;
        LightAnimationSample light = SampleMotion(
            time,
            post.Variant * 2.17f + post.WorldPosition.X * .0013f,
            StyleForPath(pathKey),
            motionStrength);
        Color metal = Color.Lerp(UiTheme.Ink, theme.Glow, .2f);
        Color core = Color.Lerp(theme.Glow, theme.Core,
            Math.Clamp(.54f + light.Intensity * .16f, 0f, 1f))
            * Math.Clamp(light.Intensity, 0f, 1f);

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

        Vector2 lamp = new(center.X, floorY - 35 + light.VerticalDrift);
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
                    lamp + new Vector2(0, -12 - light.Intensity * 3),
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
            13f + light.Halo * 2f,
            theme.Glow * (.18f + light.Halo * .18f), 2, 20);
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
        LightAnimationSample motion)
    {
        Vector2 logical = camera.WorldToScreen(
            sourceWorld, playerWorldPosition, screenShake)
            - new Vector2(0, height);
        Vector2 display = camera.ApplyZoom(logical)
            + new Vector2(0f, motion.VerticalDrift * camera.Zoom);
        float displayRadius = radius * camera.Zoom * motion.Radius;
        float cullRadius = displayRadius * 1.275f;
        if (display.X + cullRadius < viewport.Left
            || display.X - cullRadius > viewport.Right
            || display.Y + cullRadius < viewport.Top
            || display.Y - cullRadius > viewport.Bottom)
        {
            return;
        }

        float animatedIntensity = intensity * motion.Intensity;
        Rectangle aura = Centered(display, displayRadius * 2.55f);
        spriteBatch.Draw(_radialGlow!, aura,
            theme.Glow * Math.Clamp(animatedIntensity * .16f * motion.Halo, 0f, .38f));
        Rectangle pool = Centered(display, displayRadius * 2f);
        spriteBatch.Draw(_radialGlow!, pool,
            theme.Glow * Math.Clamp(animatedIntensity * .38f, 0f, .72f));
        Rectangle core = Centered(display, displayRadius * .78f);
        spriteBatch.Draw(_radialGlow!, core,
            theme.Core * Math.Clamp(animatedIntensity * .32f, 0f, .62f));
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
