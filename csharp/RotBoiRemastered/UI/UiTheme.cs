using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.Systems;

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
    public const string FontPath = "Content/Fonts/coolveticarg.otf";

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

    public static readonly IReadOnlyDictionary<string, Color> RarityColors = new Dictionary<string, Color>
    {
        ["Common"] = new Color(190, 195, 202),
        ["Rare"] = Blue,
        ["Epic"] = Purple,
        ["Legendary"] = Gold,
        ["Mythical"] = new Color(245, 241, 220),
    };

    public const int ReferenceWidth = 1920;
    public const int ReferenceHeight = 1080;
    public const float MinDisplayScale = .6f;
    public const float MaxDisplayScale = 2.4f;

    public static readonly IReadOnlyList<double> TextSizeLevels = new[] { 0.85, 1.0, 1.15, 1.3 };
    public static readonly IReadOnlyList<string> TextSizeLabels = new[] { "SMALL", "NORMAL", "LARGE", "HUGE" };

    private static FontSystem? _fontSystem;

    /// <summary>Call once from LoadContent, after the GraphicsDevice exists.</summary>
    public static void Initialize(GraphicsDevice graphicsDevice)
    {
        Primitives2D.Initialize(graphicsDevice);
        _fontSystem = new FontSystem();
        _fontSystem.AddFont(File.ReadAllBytes(FontPath));
    }

    /// <summary>Height-aware UI scale that remains stable across aspect ratios.</summary>
    public static float DisplayScale(int screenWidth, int screenHeight)
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
    /// Italic/bold are accepted for signature parity with uiTheme.py (used by
    /// not-yet-ported bossTypes.py) but not yet implemented -- there's only
    /// one font file and no synthetic style synthesis wired up yet. Both
    /// currently render at regular weight/slant.
    /// </summary>
    public static DynamicSpriteFont Font(double size, bool italic = false, bool bold = false)
    {
        int pixelSize = Math.Max(9, (int)Math.Round(size * TextScaleMultiplier()));
        return _fontSystem!.GetFont(pixelSize);
    }

    public static Rectangle DrawText(SpriteBatch spriteBatch, object value, double size, Color? color = null,
        Vector2? position = null, string anchor = "topleft")
    {
        string text = value.ToString() ?? "";
        var font = Font(size);
        Vector2 pos = position ?? Vector2.Zero;
        Vector2 measured = font.MeasureString(text);
        var rect = AnchoredRect(pos, measured, anchor);
        font.DrawText(spriteBatch, text, new Vector2(rect.X, rect.Y), color ?? Text);
        return rect;
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
            var hr = new Rectangle(visualRect.X + inset, visualRect.Y + inset,
                visualRect.Height - inset * 2, visualRect.Height - inset * 2);
            hintRect = hr;
            Primitives2D.FillRect(spriteBatch, hr, accent);
            DrawText(spriteBatch, keyHint, textSize * 0.72, Ink,
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
        ratio = Math.Clamp(ratio, 0f, 1f);
        Primitives2D.FillRect(spriteBatch, rect, Ink);
        int borderWidth = Math.Max(2, (int)MathF.Round(2 * scale));
        Primitives2D.RectOutline(spriteBatch, rect, Border, borderWidth);
        var inner = rect;
        inner.Inflate(-borderWidth * 2, -borderWidth * 2);
        int fillWidth = (int)(inner.Width * ratio);
        if (fillWidth > 0)
            Primitives2D.FillRect(spriteBatch, new Rectangle(inner.X, inner.Y, fillWidth, inner.Height), color);
        if (segments > 1)
        {
            for (int index = 1; index < segments; index++)
            {
                int x = inner.X + (int)(inner.Width * index / (float)segments);
                Primitives2D.Line(spriteBatch, new Vector2(x, inner.Y), new Vector2(x, inner.Bottom - 1), Ink, 1);
            }
        }
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

    public static Color Lighten(Color color, int amount) => new(
        Math.Min(255, color.R + amount),
        Math.Min(255, color.G + amount),
        Math.Min(255, color.B + amount),
        color.A);
}
