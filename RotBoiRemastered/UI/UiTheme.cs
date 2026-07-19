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
    public const double MinTextScale = .85;
    public const double MaxTextScale = 2.0;
    public const double MinGuiScale = .85;
    public const double MaxGuiScale = 1.3;
    public const double MinDamageTextScale = .45;
    public const double MaxDamageTextScale = 2.0;

    /// <summary>
    /// Fixed presets rather than a free-form slider for GuiScale -- see
    /// Menus.cs's OPTIONS tab. It used to be a continuous slider up to
    /// 1.8x; at that extreme the pause/title screens' fixed-pixel button
    /// offsets started running off-screen or overlapping themselves.
    /// Capping the range and only exposing a handful of pre-checked steps
    /// keeps every screen usable at every selectable value, at the cost of
    /// not being able to dial in an arbitrary in-between percentage.
    /// TextSize stays a plain slider (capped at MaxTextScale) -- its sidebar
    /// overlap problem was solved instead by InformationSheet's own capped
    /// local text scale (see DrawSheetText/MaxLocalTextBoost there).
    /// </summary>
    public static readonly IReadOnlyList<double> GuiScaleLevels = new[] { .85, 1.0, 1.15, 1.3 };
    public static readonly IReadOnlyList<string> GuiScaleLabels = new[] { "SMALL", "NORMAL", "LARGE", "MAX" };

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

    public static Color Lighten(Color color, int amount) => new(
        Math.Min(255, color.R + amount),
        Math.Min(255, color.G + amount),
        Math.Min(255, color.B + amount),
        color.A);
}
