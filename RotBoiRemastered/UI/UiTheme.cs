using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.Presentation;
using RotBoiRemastered.Systems;
using RotBoiRemastered.World;

namespace RotBoiRemastered.UI;

/// <summary>
/// Shared blocky UI primitives for RotBoi Remastered. Ported from uiTheme.py.
///
/// Font rendering note: pygame's font system renders TrueType/OpenType fonts
/// at any continuous pixel size at runtime -- this is what powers the
/// text-size accessibility setting and resolution-based UI scaling. MonoGame's
/// built-in SpriteFont bakes a font at fixed sizes at build time (no
/// continuous scaling), which would be a real regression from the Python
/// original. Using FontStashSharp instead: it renders TTF/OTF fonts
/// dynamically at any size, much closer to pygame's behavior, and its
/// per-size glyph caching mirrors uiTheme.py's own _font_cache.
/// </summary>
public static class UiTheme
{
    /// <summary>
    /// Raw FontStash asset copied beside the executable. Resolve from the app
    /// base rather than the process working directory so installed builds and
    /// `dotnet run --project` work from any launch location.
    /// </summary>
    public static string FontPath => Path.Combine(
        AppContext.BaseDirectory, "Content", "Fonts", "coolveticarg.otf");

    public static readonly Color Ink = new(12, 14, 18);
    public static readonly Color Void = new(17, 20, 27);
    public static readonly Color Panel = new(27, 31, 40);
    public static readonly Color PanelRaised = new(37, 42, 53);
    public static readonly Color PanelHover = new(47, 53, 66);
    public static readonly Color Border = new(78, 87, 104);
    public static readonly Color Text = new(241, 237, 220);
    public static readonly Color Muted = new(157, 164, 177);
    public static readonly Color Cream = new(239, 211, 142);
    public static readonly Color Red = new(214, 78, 74);
    public static readonly Color Green = new(100, 190, 126);
    public static readonly Color Blue = new(92, 151, 222);
    public static readonly Color Gold = new(225, 169, 65);
    public static readonly Color Purple = new(175, 105, 218);
    public static readonly Color Shadow = new(8, 9, 12);

    /// <summary>
    /// Canonical "dim the world behind a modal" color. Every full-screen
    /// confirmation/backdrop should draw through <see cref="DrawScrim"/>
    /// rather than inventing its own translucent black -- three near-
    /// identical one-off values (TitleScreen's quit confirm, SettingsMenu's
    /// pause backdrop, SettingsMenu's destructive-action confirmation) used
    /// to drift slightly from each other for no reason.
    /// </summary>
    public static readonly Color Scrim = new(3, 5, 8, 205);

    public static readonly IReadOnlyDictionary<string, Color> RarityColors = new Dictionary<string, Color>
    {
        ["Common"] = new Color(190, 195, 202),
        ["Rare"] = Blue,
        ["Epic"] = Purple,
        ["Legendary"] = Gold,
        ["Mythical"] = new Color(245, 241, 220),
        ["Unique"] = new Color(224, 96, 43),
    };

    public const int ReferenceWidth = 1920;
    public const int ReferenceHeight = 1080;
    public const float MinDisplayScale = .6f;
    public const float MaxDisplayScale = 2.4f;
    public const double MinTextScale = .60;
    public const double MaxTextScale = 3.5;
    public const double MinGuiScale = .65;
    public const double MaxGuiScale = 2.25;
    public const double MinDamageTextScale = .30;
    public const double MaxDamageTextScale = 4.0;

    /// <summary>
    /// Legacy preset values remain available for compatibility with callers
    /// that present discrete choices. The settings screen now exposes the
    /// complete range continuously, which is substantially more useful on
    /// high-DPI and living-room displays.
    /// </summary>
    public static readonly IReadOnlyList<double> GuiScaleLevels =
        new[] { .70, .85, 1.0, 1.15, 1.30, 1.50, 1.75 };

    private static FontSystem? _fontSystem;
    private const string DungeonGlyphWarmupText =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZ abcdefghijklmnopqrstuvwxyz 0123456789 /-";

    /// <summary>Call once from LoadContent, after the GraphicsDevice exists.</summary>
    public static void Initialize(GraphicsDevice graphicsDevice)
    {
        Primitives2D.Initialize(graphicsDevice);
        _fontSystem = new FontSystem();
        _fontSystem.AddFont(File.ReadAllBytes(FontPath));
    }

    /// <summary>
    /// FontStash rasterizes a glyph the first time it is drawn at a given
    /// pixel size. Dungeon title and room banners appear on transition
    /// frames, so prime their exact configured sizes during LoadContent.
    /// </summary>
    public static void PrewarmDungeonText(SpriteBatch spriteBatch)
    {
        float scale = DisplayScale(spriteBatch);
        double[] sizes = [24 * scale, 15 * scale, 10 * scale];
        spriteBatch.Begin();
        foreach (double size in sizes)
        {
            Font(size).DrawText(
                spriteBatch,
                DungeonGlyphWarmupText,
                new Vector2(-10_000, -10_000),
                Color.White);
        }
        spriteBatch.End();
    }

    /// <summary>Height-aware UI scale that remains stable across aspect ratios.</summary>
    public static float DisplayScale(int screenWidth, int screenHeight)
    {
        float scale = Math.Min((float)screenWidth / ReferenceWidth, (float)screenHeight / ReferenceHeight);
        float resolutionScale = Math.Max(MinDisplayScale, Math.Min(MaxDisplayScale, scale));
        return resolutionScale * (float)Math.Clamp(GameProfile.Profile.GuiScale, MinGuiScale, MaxGuiScale);
    }

    /// <summary>Resolution-only scale for elements with their own accessibility multiplier.</summary>
    public static float ResolutionScale(int screenWidth, int screenHeight)
    {
        float scale = Math.Min((float)screenWidth / ReferenceWidth, (float)screenHeight / ReferenceHeight);
        return Math.Max(MinDisplayScale, Math.Min(MaxDisplayScale, scale));
    }

    public static float DisplayScale(SpriteBatch spriteBatch)
    {
        var viewport = spriteBatch.GraphicsDevice.Viewport;
        return DisplayScale(viewport.Width, viewport.Height);
    }

    /// <summary>User-configurable text size preference, layered on top of DisplayScale.</summary>
    public static double TextScaleMultiplier() => GameProfile.Profile.TextSize;

    /// <summary>
    /// A clean starting point for GuiScale/TextSize/DamageTextSize on the
    /// current display, rather than always defaulting to 100%/100%/80%
    /// regardless of resolution or aspect ratio. DisplayScale already
    /// auto-adjusts raw pixel sizes for resolution (ReferenceWidth/Height),
    /// but that alone assumes a 16:9-ish aspect; an ultrawide or a
    /// narrower-than-16:9 display has less matching room in one axis at the
    /// same DisplayScale, so its GUI/text reads smaller than a 16:9 display
    /// at the same setting would. This nudges the three settings back
    /// toward what 16:9 would have felt like, snapped to 5% steps to match
    /// the sliders' own step size -- a starting point to tune from, not a
    /// scientifically "correct" value.
    /// </summary>
    public static (double GuiScale, double TextSize, double DamageTextSize) SuggestedScales(
        int screenWidth, int screenHeight)
    {
        float aspect = screenWidth / (float)Math.Max(1, screenHeight);
        float referenceAspect = ReferenceWidth / (float)ReferenceHeight;
        float aspectDelta = aspect - referenceAspect;
        double guiScale = 1.0 - aspectDelta * .05;
        double textSize = 1.0 - aspectDelta * .04;
        double damageTextSize = .8 - aspectDelta * .04;
        return (
            SnapScale(guiScale, MinGuiScale, MaxGuiScale),
            SnapScale(textSize, MinTextScale, MaxTextScale),
            SnapScale(damageTextSize, MinDamageTextScale, MaxDamageTextScale));
    }

    private static double SnapScale(double value, double minimum, double maximum) =>
        Math.Round(Math.Clamp(value, minimum, maximum) / .05) * .05;

    /// <summary>
    /// `italic` is accepted for signature parity with uiTheme.py's font()
    /// but not implemented -- there's only one regular-weight font file and
    /// no glyph-shear renderer available through FontStashSharp/SpriteBatch
    /// to synthesize a slant safely. `bold` is real: DrawText below
    /// synthesizes it with a 1px-offset double draw (a standard technique
    /// for a single-weight font file), the same visual effect pygame's
    /// `set_bold(True)` gives a boss's phase-announcement label.
    /// </summary>
    public static DynamicSpriteFont Font(double size, bool italic = false, bool bold = false)
    {
        int pixelSize = Math.Max(9, (int)Math.Round(size * TextScaleMultiplier()));
        return _fontSystem!.GetFont(pixelSize);
    }

    public static DynamicSpriteFont RawFont(double pixelSize) =>
        _fontSystem!.GetFont(Math.Max(8, (int)Math.Round(pixelSize)));

    public static Rectangle DrawRawText(SpriteBatch spriteBatch, object value, double pixelSize, Color color,
        Vector2 position, string anchor = "topleft")
    {
        string text = value.ToString() ?? "";
        var font = RawFont(pixelSize);
        Vector2 measured = font.MeasureString(text);
        var rect = AnchoredRect(position, measured, anchor);
        font.DrawText(spriteBatch, text, new Vector2(rect.X, rect.Y), color);
        return rect;
    }

    public static Rectangle DrawText(SpriteBatch spriteBatch, object value, double size, Color? color = null,
        Vector2? position = null, string anchor = "topleft", bool bold = false)
    {
        string text = value.ToString() ?? "";
        var font = Font(size);
        Vector2 pos = position ?? Vector2.Zero;
        Vector2 measured = font.MeasureString(text);
        var rect = AnchoredRect(pos, measured, anchor);
        Color drawColor = color ?? Text;
        if (bold)
            font.DrawText(spriteBatch, text, new Vector2(rect.X + 1, rect.Y), drawColor);
        font.DrawText(spriteBatch, text, new Vector2(rect.X, rect.Y), drawColor);
        return rect;
    }

    /// <summary>
    /// Greedy word-wrap at the given font size: the fewest lines whose
    /// measured width all fit within maxWidth. A single word wider than
    /// maxWidth on its own still gets its own (overflowing) line rather than
    /// being split mid-word. Text-size-aware callers (card names/
    /// descriptions) need this because a fixed-width container plus a
    /// user-adjustable TextSize multiplier means the same string can outgrow
    /// its box at any font size, not just a handful of unusually long ones.
    /// </summary>
    public static List<string> WrapLines(string text, double fontSize, float maxWidth)
    {
        var font = Font(fontSize);
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        string current = "";
        foreach (var word in words)
        {
            string candidate = current.Length == 0 ? word : $"{current} {word}";
            if (current.Length > 0 && font.MeasureString(candidate).X > maxWidth)
            {
                lines.Add(current);
                current = word;
            }
            else
            {
                current = candidate;
            }
        }
        if (current.Length > 0 || lines.Count == 0)
            lines.Add(current);
        return lines;
    }

    /// <summary>
    /// Draws WrapLines' output centered horizontally on `center.X`, stacked
    /// vertically so the whole block stays centered on `center.Y` -- a
    /// single-line result lands at exactly `center`, matching plain
    /// DrawText's anchor: "center" behavior so wrapping never shifts
    /// already-short text that never needed it.
    /// </summary>
    public static void DrawWrappedText(SpriteBatch spriteBatch, string text, double fontSize, Color color,
        Vector2 center, float maxWidth, bool bold = false)
    {
        var lines = WrapLines(text, fontSize, maxWidth);
        float lineHeight = Font(fontSize).MeasureString("Ag").Y;
        float startY = center.Y - lineHeight * lines.Count / 2f + lineHeight / 2f;
        for (int index = 0; index < lines.Count; index++)
            DrawText(spriteBatch, lines[index], fontSize, color, new Vector2(center.X, startY + index * lineHeight), "center", bold);
    }

    /// <summary>
    /// Positions a box of `size` so that `anchor` (matching pygame Rect's
    /// named attributes) lands at `point`. Covers the anchors actually used
    /// by call sites in the original codebase, not the full pygame Rect set.
    /// </summary>
    private static Rectangle AnchoredRect(Vector2 point, Vector2 size, string anchor)
    {
        float x = anchor switch
        {
            "topright" or "midright" or "bottomright" => point.X - size.X,
            "midtop" or "midbottom" or "center" => point.X - size.X / 2f,
            _ => point.X, // topleft, midleft, bottomleft
        };
        float y = anchor switch
        {
            "bottomleft" or "bottomright" or "midbottom" => point.Y - size.Y,
            "midleft" or "midright" or "center" => point.Y - size.Y / 2f,
            _ => point.Y, // topleft, topright, midtop
        };
        return new Rectangle((int)MathF.Round(x), (int)MathF.Round(y),
            (int)MathF.Ceiling(size.X), (int)MathF.Ceiling(size.Y));
    }

    /// <summary>Dims `rect` (typically the full screen) behind a modal with the shared <see cref="Scrim"/> color.</summary>
    public static void DrawScrim(SpriteBatch spriteBatch, Rectangle rect) =>
        Primitives2D.FillRect(spriteBatch, rect, Scrim);

    public static Rectangle DrawPanel(SpriteBatch spriteBatch, Rectangle rect, Color? fill = null,
        Color? border = null, int shadow = 5, bool hovered = false)
    {
        Color fillColor = fill ?? Panel;
        Color borderColor = border ?? Border;
        float scale = DisplayScale(spriteBatch);
        int shadowSize = Math.Max(0, (int)MathF.Round(shadow * scale));
        int borderWidth = Math.Max(2, (int)MathF.Round(2 * scale));
        if (shadowSize > 0)
        {
            var shadowRect = new Rectangle(rect.X + shadowSize, rect.Y + shadowSize, rect.Width, rect.Height);
            Primitives2D.FillRect(spriteBatch, shadowRect, Shadow);
        }
        Primitives2D.FillRect(spriteBatch, rect, hovered ? PanelHover : fillColor);
        Primitives2D.RectOutline(spriteBatch, rect, borderColor, borderWidth);
        Primitives2D.Line(spriteBatch, new Vector2(rect.Left, rect.Top), new Vector2(rect.Right - 1, rect.Top),
            Lighten(borderColor, 28), borderWidth);
        return rect;
    }

    /// <summary>
    /// Quiet menu chrome: a recessed surface, neutral outline, clipped-corner
    /// brackets, and one short semantic accent rule. Unlike Living/Composite
    /// panels it contains no ambient animation or cycling Soul colors.
    /// </summary>
    public static Rectangle DrawFramedPanel(SpriteBatch spriteBatch, Rectangle rect,
        Color? fill = null, Color? accent = null, int shadow = 4, bool hovered = false)
    {
        Color accentColor = accent ?? Border;
        DrawPanel(spriteBatch, rect, fill ?? Panel, Border, shadow, hovered);
        float scale = DisplayScale(spriteBatch);
        int inset = Math.Max(3, (int)MathF.Round(4 * scale));
        int bracket = Math.Max(6, Math.Min(rect.Width, rect.Height) / 12);
        int width = Math.Max(1, (int)MathF.Round(scale));
        Color quiet = Lighten(Border, 16);

        Primitives2D.Line(spriteBatch, new(rect.Left + inset, rect.Top + bracket),
            new(rect.Left + inset, rect.Top + inset), quiet, width);
        Primitives2D.Line(spriteBatch, new(rect.Left + inset, rect.Top + inset),
            new(rect.Left + bracket, rect.Top + inset), quiet, width);
        Primitives2D.Line(spriteBatch, new(rect.Right - bracket, rect.Bottom - inset),
            new(rect.Right - inset, rect.Bottom - inset), quiet, width);
        Primitives2D.Line(spriteBatch, new(rect.Right - inset, rect.Bottom - inset),
            new(rect.Right - inset, rect.Bottom - bracket), quiet, width);

        int ruleWidth = Math.Max(bracket * 2, Math.Min(rect.Width / 4, (int)(110 * scale)));
        Primitives2D.FillRect(spriteBatch,
            new Rectangle(rect.Left + bracket, rect.Top + inset, ruleWidth, width), accentColor);
        return rect;
    }

    public static Rectangle DrawLivingPanel(
        SpriteBatch spriteBatch,
        Rectangle rect,
        string? pathKey,
        float animationTime,
        Color? fill = null,
        Color? border = null,
        int shadow = 5,
        bool hovered = false,
        bool composite = false)
    {
        PathVisualProfile path = SoulVisualLanguage.Path(pathKey);
        Color accent = border ?? path.Accent;
        DrawPanel(spriteBatch, rect, fill, accent, shadow, hovered);
        int corner = Math.Max(5, Math.Min(rect.Width, rect.Height) / 11);
        int width = Math.Max(1, corner / 5);
        float breathe = .68f + .22f * MathF.Sin(
            animationTime * path.MotionCadence * 2f);
        Color motif = path.Secondary * breathe;
        Primitives2D.Line(spriteBatch,
            new Vector2(rect.Left, rect.Top + corner),
            new Vector2(rect.Left + corner, rect.Top), motif, width);
        Primitives2D.Line(spriteBatch,
            new Vector2(rect.Right - corner, rect.Top),
            new Vector2(rect.Right, rect.Top + corner), motif, width);
        Primitives2D.Line(spriteBatch,
            new Vector2(rect.Left, rect.Bottom - corner),
            new Vector2(rect.Left + corner, rect.Bottom), motif, width);
        Primitives2D.Line(spriteBatch,
            new Vector2(rect.Right - corner, rect.Bottom),
            new Vector2(rect.Right, rect.Bottom - corner), motif, width);

        int segments = composite ? GamePaths.Paths.Count : 3;
        float segmentWidth = Math.Min(rect.Width * .42f,
            170f * DisplayScale(spriteBatch)) / segments;
        float start = rect.Center.X - segmentWidth * segments / 2f;
        int lit = Math.Abs((int)MathF.Floor(animationTime * 3f)) % segments;
        for (int index = 0; index < segments; index++)
        {
            Color segmentColor = composite
                ? GamePaths.Paths[index].Accent
                : index == 1 ? path.Secondary : path.Accent;
            Primitives2D.FillRect(spriteBatch,
                new Rectangle(
                    (int)(start + index * segmentWidth + 2),
                    rect.Top + width,
                    Math.Max(2, (int)segmentWidth - 4),
                    Math.Max(1, width)),
                segmentColor * (index == lit ? .9f : .38f));
        }
        return rect;
    }

    /// <summary>
    /// Neutral five-sense chrome for interfaces which belong to the whole
    /// Soul rather than whichever Path happens to be active.
    /// </summary>
    public static Rectangle DrawCompositePanel(
        SpriteBatch spriteBatch,
        Rectangle rect,
        float animationTime,
        Color? fill = null,
        Color? border = null,
        int shadow = 5,
        bool hovered = false)
    {
        Color accent = border ?? Cream;
        DrawPanel(spriteBatch, rect, fill ?? Panel, accent, shadow, hovered);
        int corner = Math.Max(5, Math.Min(rect.Width, rect.Height) / 10);
        int lineWidth = Math.Max(1, corner / 5);
        float pulse = .56f + .12f * MathF.Sin(animationTime * 1.4f);
        Color motif = Purple * pulse;
        Primitives2D.Line(spriteBatch, new Vector2(rect.Left, rect.Top + corner),
            new Vector2(rect.Left + corner, rect.Top), motif, lineWidth);
        Primitives2D.Line(spriteBatch, new Vector2(rect.Right - corner, rect.Top),
            new Vector2(rect.Right, rect.Top + corner), motif, lineWidth);
        Primitives2D.Line(spriteBatch, new Vector2(rect.Left, rect.Bottom - corner),
            new Vector2(rect.Left + corner, rect.Bottom), motif, lineWidth);
        Primitives2D.Line(spriteBatch, new Vector2(rect.Right - corner, rect.Bottom),
            new Vector2(rect.Right, rect.Bottom - corner), motif, lineWidth);

        float segmentWidth = Math.Min(rect.Width * .46f, 230f * DisplayScale(spriteBatch))
            / GamePaths.Paths.Count;
        float start = rect.Center.X - segmentWidth * GamePaths.Paths.Count / 2f;
        int lit = Math.Abs((int)MathF.Floor(animationTime * 2f)) % GamePaths.Paths.Count;
        for (int index = 0; index < GamePaths.Paths.Count; index++)
        {
            Color color = GamePaths.Paths[index].Accent;
            Primitives2D.FillRect(spriteBatch,
                new Rectangle((int)(start + index * segmentWidth + 2), rect.Top + lineWidth,
                    Math.Max(2, (int)segmentWidth - 4), Math.Max(1, lineWidth)),
                color * (index == lit ? .78f : .28f));
        }
        return rect;
    }

    /// <summary>
    /// One brightened stand-in color per sense, used only for this rose's
    /// petals -- <see cref="GamePath.Accent"/> stays muted everywhere else
    /// (floor runes, path selection, etc.), but the rose's rainbow-ringed
    /// void needs its petals lifted to the same saturation or they read as
    /// washed out next to it.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, Color> SenseVividColors = new Dictionary<string, Color>
    {
        ["sound"] = new Color(255, 216, 115),
        ["touch"] = new Color(63, 227, 116),
        ["sight"] = new Color(79, 214, 255),
        ["chemesthesis"] = new Color(255, 92, 61),
        ["phantasia"] = new Color(227, 79, 224),
    };

    public static void DrawSoulRose(
        SpriteBatch spriteBatch,
        Vector2 center,
        float radius,
        float animationTime,
        float alpha = 1f,
        IReadOnlyDictionary<string, int>? mastery = null)
    {
        float stepped = MathF.Floor(animationTime * 12f) / 12f;
        for (int index = 0; index < GamePaths.Paths.Count; index++)
        {
            GamePath path = GamePaths.Paths[index];
            float angle = -MathF.PI / 2f
                + index * MathF.Tau / GamePaths.Paths.Count
                + stepped * .08f;
            int progress = mastery?.GetValueOrDefault(path.Key) ?? 0;
            float lobeRadius = radius
                * (.52f + Math.Min(3, progress) * .035f);
            // .74 (was .58) keeps every leaf's inner edge clear of the
            // rainbow ring at the rose's heart instead of overlapping it
            Vector2 lobe = center + new Vector2(
                MathF.Cos(angle), MathF.Sin(angle)) * radius * .74f;
            Vector2 radial = Vector2.Normalize(lobe - center);
            Vector2 side = new(-radial.Y, radial.X);
            Color vivid = SenseVividColors.TryGetValue(path.Key, out Color vividColor)
                ? vividColor : path.Accent;
            Primitives2D.FillQuad(spriteBatch,
                lobe + radial * lobeRadius * .48f,
                lobe + side * lobeRadius * .3f,
                lobe - radial * lobeRadius * .42f,
                lobe - side * lobeRadius * .3f,
                vivid * (alpha
                    * (.66f + Math.Min(3, progress) * .09f)));
        }
        DrawRoseCenter(spriteBatch, center, radius, animationTime, alpha);
    }

    /// <summary>
    /// The rose's heart: a rainbow ring (reusing the same cycling palette as
    /// Aphantasia's own rainbow motif via <see cref="Primitives2D.Rainbow"/>)
    /// spinning around two small voided tentacles that chase each other at a
    /// fixed 180-degree offset and identical speed, with a scatter of
    /// twinkling starlight woven between them -- then a plain dark circle on
    /// top so the center itself stays empty.
    /// </summary>
    private static void DrawRoseCenter(SpriteBatch spriteBatch, Vector2 center, float radius,
        float animationTime, float alpha)
    {
        float ringRadius = radius * .34f;
        float voidRadius = radius * .24f;
        int ringWidth = Math.Max(2, (int)(ringRadius - voidRadius));

        const int slices = 40;
        float spin = animationTime * .35f;
        var ringRect = new Rectangle((int)(center.X - ringRadius), (int)(center.Y - ringRadius),
            (int)(ringRadius * 2), (int)(ringRadius * 2));
        for (int slice = 0; slice < slices; slice++)
        {
            float t0 = slice / (float)slices;
            float t1 = (slice + 1) / (float)slices;
            Color sliceColor = Primitives2D.Rainbow(t0 + spin);
            Primitives2D.Arc(spriteBatch, ringRect, t0 * MathF.Tau, t1 * MathF.Tau + .03f,
                sliceColor * alpha, ringWidth, 2);
        }

        float orbitRadius = (ringRadius + voidRadius) * .5f;
        DrawVoidTentacle(spriteBatch, center, orbitRadius, animationTime, 0f,
            new Color(20, 22, 27), Cream * .35f, alpha);
        DrawVoidTentacle(spriteBatch, center, orbitRadius, animationTime, 180f,
            new Color(29, 58, 120), Blue * .45f, alpha);
        DrawStarlight(spriteBatch, center, orbitRadius, animationTime, alpha);

        Primitives2D.FillCircle(spriteBatch, center, voidRadius, Ink * alpha);
    }

    /// <summary>
    /// A tapering chain of beads following an arc around the ring band --
    /// cheap stand-in for a tentacle silhouette that still reads as one
    /// continuous body once it's spinning. Both tentacles share this exact
    /// speed (40 degrees/second, a 9-second revolution) so a 180-degree base
    /// offset keeps them permanently opposite each other rather than one
    /// slowly gaining on the other.
    /// </summary>
    private static void DrawVoidTentacle(SpriteBatch spriteBatch, Vector2 center, float orbitRadius,
        float animationTime, float baseAngleDeg, Color fill, Color outline, float alpha)
    {
        const int beadCount = 6;
        float spinDeg = animationTime * 40f;
        for (int bead = 0; bead < beadCount; bead++)
        {
            float angleDeg = baseAngleDeg + spinDeg - bead * 15.5f;
            float angle = angleDeg * MathF.PI / 180f;
            float wobble = MathF.Sin(bead * 1.3f) * orbitRadius * .08f;
            Vector2 position = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle))
                * (orbitRadius + wobble);
            float beadRadius = Math.Max(orbitRadius * .05f, orbitRadius * .19f - bead * orbitRadius * .024f);
            float beadAlpha = alpha * (.95f - bead * .11f);
            Primitives2D.FillCircle(spriteBatch, position, beadRadius, fill * beadAlpha);
            Primitives2D.CircleOutline(spriteBatch, position, beadRadius, outline * beadAlpha, 1, 10);
        }
    }

    /// <summary>Small twinkling points scattered around the ring band at the golden angle, so they never clump.</summary>
    private static void DrawStarlight(SpriteBatch spriteBatch, Vector2 center, float orbitRadius,
        float animationTime, float alpha)
    {
        const int starCount = 12;
        for (int star = 0; star < starCount; star++)
        {
            float angle = star * 137.5f * MathF.PI / 180f;
            float radius = orbitRadius * (.86f + (star % 4) * .12f);
            Vector2 position = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            float twinkle = .5f + .5f * MathF.Sin(animationTime * 2.4f + star * 1.9f);
            float starRadius = Math.Max(1f, orbitRadius * (.02f + (star % 3) * .01f) * (.6f + twinkle * .8f));
            Primitives2D.FillCircle(spriteBatch, position, starRadius,
                Text * (alpha * (.15f + twinkle * .85f)));
        }
    }

    public static bool DrawButton(SpriteBatch spriteBatch, Rectangle rect, string label, Point mousePosition,
        bool mouseDown = false, bool enabled = true, Color? accentColor = null, string? keyHint = null,
        double textSize = 18)
    {
        Color accent = accentColor ?? Cream;
        float scale = DisplayScale(spriteBatch);
        bool hovered = enabled && rect.Contains(mousePosition);
        bool pressed = hovered && mouseDown;
        var visualRect = new Rectangle(rect.X, rect.Y + (pressed ? 3 : 0), rect.Width, rect.Height);
        Color fill = hovered ? PanelHover : PanelRaised;
        if (!enabled)
        {
            fill = Panel;
            accent = Border;
        }
        DrawPanel(spriteBatch, visualRect, fill: fill, border: accent, shadow: pressed ? 2 : 5);

        int padding = Math.Max(6, (int)MathF.Round(8 * scale));
        Rectangle? hintRect = null;
        if (!string.IsNullOrEmpty(keyHint))
        {
            int inset = padding;
            int boxHeight = visualRect.Height - inset * 2;
            double hintTextSize = textSize * 0.72;
            // A single-key hint ("R", "ESC") stays a square sized off the
            // button's own height, same as before; a longer combo hint like
            // "SPACE / F" instead grows the box to fit its measured width
            // (plus its own padding) rather than overflowing a fixed square.
            int hintPaddingX = Math.Max(8, (int)MathF.Round(10 * scale));
            int textWidth = (int)MathF.Ceiling(Font(hintTextSize).MeasureString(keyHint).X);
            int boxWidth = Math.Max(boxHeight, textWidth + hintPaddingX * 2);
            var hr = new Rectangle(visualRect.X + inset, visualRect.Y + inset, boxWidth, boxHeight);
            hintRect = hr;
            int cornerRadius = Math.Max(3, (int)MathF.Round(6 * scale));
            Primitives2D.FillRoundedRect(spriteBatch, hr, accent, cornerRadius);
            DrawText(spriteBatch, keyHint, hintTextSize, Ink,
                new Vector2(hr.Center.X, hr.Center.Y), "center");
        }

        Vector2 labelCenter;
        float availableWidth;
        if (hintRect.HasValue)
        {
            var hr = hintRect.Value;
            labelCenter = new Vector2((hr.Right + visualRect.Right) / 2f, visualRect.Center.Y);
            availableWidth = (visualRect.Right - padding) - (hr.Right + padding);
        }
        else
        {
            labelCenter = new Vector2(visualRect.Center.X, visualRect.Center.Y);
            availableWidth = visualRect.Width - padding * 2;
        }

        double fittedSize = textSize;
        while (fittedSize > 9 && Font(fittedSize).MeasureString(label).X > availableWidth)
            fittedSize -= 1;
        DrawText(spriteBatch, label, fittedSize, enabled ? Text : Muted, labelCenter, "center");
        return hovered;
    }

    public static void DrawProgress(SpriteBatch spriteBatch, Rectangle rect, float ratio, Color color, int segments = 10)
    {
        float scale = DisplayScale(spriteBatch);
        Primitives2D.FillRect(spriteBatch, rect, Ink);
        int borderWidth = ProgressBorderWidth(rect, scale);
        Primitives2D.RectOutline(spriteBatch, rect, Border, borderWidth);

        var (inner, fill) = ProgressGeometry(rect, ratio, scale);
        if (fill.Width > 0 && fill.Height > 0)
            Primitives2D.FillRect(spriteBatch, fill, color);
        if (segments > 1 && inner.Width > 0 && inner.Height > 0)
        {
            for (int index = 1; index < segments; index++)
            {
                int x = inner.X + (int)(inner.Width * index / (float)segments);
                Primitives2D.Line(spriteBatch, new Vector2(x, inner.Y), new Vector2(x, inner.Bottom - 1), Ink, 1);
            }
        }
    }

    /// <summary>
    /// Returns the drawable interior and fill rectangles used by <see cref="DrawProgress"/>.
    /// Kept independent of GraphicsDevice so resolution-sensitive UI geometry can be tested.
    /// </summary>
    public static (Rectangle Inner, Rectangle Fill) ProgressGeometry(Rectangle rect, float ratio, float displayScale)
    {
        int borderWidth = ProgressBorderWidth(rect, displayScale);
        int innerWidth = Math.Max(0, rect.Width - borderWidth * 2);
        int innerHeight = Math.Max(0, rect.Height - borderWidth * 2);
        var inner = new Rectangle(rect.X + borderWidth, rect.Y + borderWidth, innerWidth, innerHeight);
        int fillWidth = Math.Clamp((int)MathF.Round(innerWidth * Math.Clamp(ratio, 0f, 1f)), 0, innerWidth);
        var fill = new Rectangle(inner.X, inner.Y, fillWidth, innerHeight);
        return (inner, fill);
    }

    private static int ProgressBorderWidth(Rectangle rect, float displayScale)
    {
        int requested = Math.Max(2, (int)MathF.Round(2 * displayScale));
        int maximum = Math.Max(1, Math.Min(rect.Width, rect.Height) / 2);
        return Math.Min(requested, maximum);
    }

    public static Rectangle DrawTag(SpriteBatch spriteBatch, object text, Vector2 position, Color? color = null,
        double textSize = 11)
    {
        Color tagColor = color ?? Blue;
        float scale = DisplayScale(spriteBatch);
        string upper = (text.ToString() ?? "").ToUpperInvariant();
        var font = Font(textSize);
        Vector2 measured = font.MeasureString(upper);
        var rect = new Rectangle((int)position.X, (int)position.Y,
            (int)MathF.Ceiling(measured.X), (int)MathF.Ceiling(measured.Y));
        rect.Inflate((int)MathF.Round(12 * scale), (int)MathF.Round(6 * scale));
        Primitives2D.FillRect(spriteBatch, rect, Ink);
        Primitives2D.RectOutline(spriteBatch, rect, tagColor, Math.Max(1, (int)MathF.Round(scale)));
        var textPos = new Vector2(rect.Center.X - measured.X / 2f, rect.Center.Y - measured.Y / 2f);
        font.DrawText(spriteBatch, upper, textPos, tagColor);
        return rect;
    }

    /// <summary>
    /// Small-chrome corner radius (equipment/stash/icon slots) in reference
    /// pixels. Scale it through <see cref="SmallCornerRadius"/> rather than
    /// re-deriving a one-off 3px/4px rounding per call site -- FooterHud,
    /// InformationSheet, and the HUD icon slots used to each pick a slightly
    /// different radius for the same kind of small rounded box.
    /// </summary>
    public const int SmallCornerRadiusPx = 4;

    /// <summary>Larger decorative corner radius for bigger rounded surfaces (e.g. the Vestment Mirror station).</summary>
    public const int LargeCornerRadiusPx = 16;

    public static int SmallCornerRadius(float displayScale) =>
        Math.Max(2, (int)MathF.Round(SmallCornerRadiusPx * displayScale));

    /// <summary>
    /// Shared rarity-card corner radius, proportional to the card's own
    /// rendered width so it stays correct whether the card is a tiny stash
    /// icon or a large Reforge preview. Used identically by
    /// <c>ItemCards.DrawItemCard</c> and <c>StatCards.DrawUpgradeCard</c> --
    /// keep it here instead of each duplicating the same formula.
    /// </summary>
    public static int CardCornerRadius(int width) => Math.Max(2, width / 8);

    /// <summary>
    /// Clamps a fully-sized tooltip rectangle so it never runs off `bounds`.
    /// A tooltip's rect is always sized to its *actual* wrapped content
    /// before this runs (see <see cref="WrapLines"/>), so when that content
    /// is too tall to fit between its natural anchor point and the bottom of
    /// `bounds`, clamping the Y axis here is what makes the panel appear to
    /// grow upward instead of running off-screen or getting clipped -- the
    /// top edge slides up while the bottom edge settles just inside the
    /// screen, rather than the panel silently overflowing past it. The
    /// Math.Max guards against bounds shorter/narrower than the tooltip
    /// itself (an extremely long description on a small viewport): without
    /// them min could exceed max and Math.Clamp would throw.
    ///
    /// Shared by every DrawTooltip in the game (InformationSheet, FooterHud,
    /// SoulHub) so this "stretch upward, never clip" behavior can't drift
    /// between call sites the way it used to when each one hand-rolled its
    /// own clamp.
    /// </summary>
    public static Rectangle ClampTooltipRect(Rectangle rect, Rectangle bounds, int margin = 0)
    {
        int minX = bounds.X + margin;
        int maxX = Math.Max(minX, bounds.Right - rect.Width - margin);
        int minY = bounds.Y + margin;
        int maxY = Math.Max(minY, bounds.Bottom - rect.Height - margin);
        int x = Math.Clamp(rect.X, minX, maxX);
        int y = Math.Clamp(rect.Y, minY, maxY);
        return new Rectangle(x, y, rect.Width, rect.Height);
    }

    public static Color Lighten(Color color, int amount) => new(
        Math.Min(255, color.R + amount),
        Math.Min(255, color.G + amount),
        Math.Min(255, color.B + amount),
        color.A);

    /// <summary>
    /// Pushes a colour away from its own grey, deepening the hue without
    /// changing which hue it is. <paramref name="amount"/> is 0 for no change
    /// and 1 for a full push to the most saturated form of the same colour;
    /// values above 1 are clamped.
    ///
    /// Added for the half-health second form every boss now commits to: the
    /// body keeps its authored palette and simply stops being washed out.
    /// </summary>
    public static Color Saturate(Color color, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        if (amount <= 0f)
            return color;
        // Rec. 601 luma -- the perceptual grey this colour would collapse to.
        float grey = color.R * .299f + color.G * .587f + color.B * .114f;
        static int Push(float channel, float grey, float amount) =>
            (int)Math.Clamp(MathF.Round(channel + (channel - grey) * amount), 0f, 255f);
        return new Color(
            Push(color.R, grey, amount),
            Push(color.G, grey, amount),
            Push(color.B, grey, amount),
            (int)color.A);
    }
}
