using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;

namespace RotBoiRemastered.UI;

/// <summary>Resolution-independent stat symbols and collectible mini upgrade cards. Ported from statCards.py.</summary>
public static class StatCards
{
    private static readonly Dictionary<string, string> NormalizedStatNames = new(StringComparer.Ordinal);

    private static void DrawLine(SpriteBatch spriteBatch, Color color, Vector2 start, Vector2 end, float width)
        => Primitives2D.Line(spriteBatch, start, end, color, Math.Max(1, (int)width));

    private static string NormalizeStatName(string statName)
    {
        if (NormalizedStatNames.TryGetValue(statName, out string? normalized))
            return normalized;

        normalized = statName.ToLowerInvariant();
        NormalizedStatNames[statName] = normalized;
        return normalized;
    }

    private static Vector2 Rotate(Vector2 v, float degrees)
    {
        float radians = MathHelper.ToRadians(degrees);
        float cos = MathF.Cos(radians), sin = MathF.Sin(radians);
        return new Vector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
    }

    /// <summary>Draw a compact silhouette for every upgrade stat using Primitives2D.</summary>
    public static void DrawStatSymbol(SpriteBatch spriteBatch, string statName, Rectangle rect, Color? color = null)
    {
        Color drawColor = color ?? UiTheme.Text;
        float cx = rect.Center.X, cy = rect.Center.Y;
        float unit = Math.Max(1f, Math.Min(rect.Width, rect.Height) / 20f);
        float stroke = Math.Max(1f, MathF.Round(unit * 1.7f));
        string name = NormalizeStatName(statName);

        void Bullet(float x, float y, float scale = 1.0f)
        {
            float length = 10 * unit * scale, radius = 3.5f * unit * scale;
            var body = new Rectangle((int)(x - length * .45f), (int)(y - radius), (int)(length * .65f), (int)(radius * 2));
            Primitives2D.FillRoundedRect(spriteBatch, body, drawColor, Math.Max(1, (int)MathF.Round(radius)));
            Span<Vector2> tip = stackalloc Vector2[]
            {
                new Vector2(body.Right - unit, body.Top),
                new Vector2(x + length * .55f, y),
                new Vector2(body.Right - unit, body.Bottom),
            };
            Primitives2D.FillPolygonSpan(spriteBatch, tip, drawColor);
        }

        switch (name)
        {
            case "defense":
            {
                Span<Vector2> points = stackalloc Vector2[]
                {
                    new Vector2(cx, cy - 8 * unit), new Vector2(cx + 7 * unit, cy - 5 * unit),
                    new Vector2(cx + 6 * unit, cy + 3 * unit), new Vector2(cx, cy + 9 * unit),
                    new Vector2(cx - 6 * unit, cy + 3 * unit), new Vector2(cx - 7 * unit, cy - 5 * unit),
                };
                Primitives2D.PolygonOutlineSpan(spriteBatch, points, drawColor, (int)stroke);
                DrawLine(spriteBatch, drawColor, new Vector2(cx, cy - 6 * unit), new Vector2(cx, cy + 6 * unit), stroke);
                break;
            }
            case "health" or "vitality":
                DrawLine(spriteBatch, drawColor, new Vector2(cx - 7 * unit, cy), new Vector2(cx + 7 * unit, cy), stroke);
                DrawLine(spriteBatch, drawColor, new Vector2(cx, cy - 7 * unit), new Vector2(cx, cy + 7 * unit), stroke);
                if (name == "vitality")
                    Primitives2D.CircleOutline(spriteBatch, new Vector2(cx, cy), MathF.Round(9 * unit), drawColor, (int)stroke);
                break;
            case "bullet pierce":
                Bullet(cx - 2 * unit, cy);
                DrawLine(spriteBatch, drawColor, new Vector2(cx + 5 * unit, cy - 7 * unit), new Vector2(cx + 5 * unit, cy + 7 * unit), stroke);
                DrawLine(spriteBatch, drawColor, new Vector2(cx + 8 * unit, cy - 7 * unit), new Vector2(cx + 8 * unit, cy + 7 * unit), stroke);
                break;
            case "bullet count":
                for (float offset = -5f; offset <= 5f; offset += 5f)
                    Bullet(cx - 1 * unit, cy + offset * unit, .72f);
                break;
            case "spread angle":
            {
                var origin = new Vector2(cx - 7 * unit, cy);
                for (float offset = -7f; offset <= 7f; offset += 7f)
                    DrawLine(spriteBatch, drawColor, origin, new Vector2(cx + 8 * unit, cy + offset * unit), stroke);
                break;
            }
            case "attack speed":
                Primitives2D.CircleOutline(spriteBatch, new Vector2(cx, cy), MathF.Round(8 * unit), drawColor, (int)stroke);
                DrawLine(spriteBatch, drawColor, new Vector2(cx, cy), new Vector2(cx + 5 * unit, cy - 4 * unit), stroke);
                for (float offset = -4f; offset <= 4f; offset += 4f)
                    DrawLine(spriteBatch, drawColor, new Vector2(cx - 11 * unit, cy + offset * unit), new Vector2(cx - 7 * unit, cy + offset * unit), stroke);
                break;
            case "bullet speed":
            {
                Bullet(cx + 2 * unit, cy);
                void SpeedLine(float offset, float length) =>
                    DrawLine(spriteBatch, drawColor, new Vector2(cx - (length + 3) * unit, cy + offset * unit), new Vector2(cx - 3 * unit, cy + offset * unit), stroke);
                SpeedLine(-5f, 5f);
                SpeedLine(0f, 8f);
                SpeedLine(5f, 5f);
                break;
            }
            case "bullet range":
                Bullet(cx - 4 * unit, cy);
                DrawLine(spriteBatch, drawColor, new Vector2(cx + 2 * unit, cy), new Vector2(cx + 9 * unit, cy), stroke);
                DrawLine(spriteBatch, drawColor, new Vector2(cx + 7 * unit, cy - 3 * unit), new Vector2(cx + 10 * unit, cy), stroke);
                DrawLine(spriteBatch, drawColor, new Vector2(cx + 7 * unit, cy + 3 * unit), new Vector2(cx + 10 * unit, cy), stroke);
                break;
            case "bullet damage":
                Bullet(cx - 3 * unit, cy);
                for (int angle = 0; angle < 360; angle += 45)
                {
                    var direction = Rotate(new Vector2(1, 0), angle);
                    DrawLine(spriteBatch, drawColor, new Vector2(cx + direction.X * 4 * unit, cy + direction.Y * 4 * unit),
                        new Vector2(cx + direction.X * 8 * unit, cy + direction.Y * 8 * unit), stroke);
                }
                break;
            case "bullet size":
                Bullet(cx - 2 * unit, cy + 2 * unit, 1.35f);
                DrawLine(spriteBatch, drawColor, new Vector2(cx + 7 * unit, cy + 6 * unit), new Vector2(cx + 7 * unit, cy - 7 * unit), stroke);
                Primitives2D.FillPolygonSpan(spriteBatch, stackalloc Vector2[]
                {
                    new Vector2(cx + 7 * unit, cy - 9 * unit),
                    new Vector2(cx + 3 * unit, cy - 4 * unit),
                    new Vector2(cx + 11 * unit, cy - 4 * unit),
                }, drawColor);
                break;
            case "player speed":
                Primitives2D.FillCircle(spriteBatch, new Vector2(cx + 2 * unit, cy - 6 * unit), MathF.Round(2.5f * unit), drawColor);
                DrawLine(spriteBatch, drawColor, new Vector2(cx + 1 * unit, cy - 3 * unit), new Vector2(cx - 2 * unit, cy + 2 * unit), stroke);
                DrawLine(spriteBatch, drawColor, new Vector2(cx - 2 * unit, cy + 2 * unit), new Vector2(cx + 4 * unit, cy + 7 * unit), stroke);
                DrawLine(spriteBatch, drawColor, new Vector2(cx - 2 * unit, cy + 2 * unit), new Vector2(cx - 7 * unit, cy + 8 * unit), stroke);
                for (float offset = -5f; offset <= 5f; offset += 5f)
                    DrawLine(spriteBatch, drawColor, new Vector2(cx - 10 * unit, cy + offset * unit), new Vector2(cx - 6 * unit, cy + offset * unit), stroke);
                break;
            case "crit chance":
                Primitives2D.CircleOutline(spriteBatch, new Vector2(cx, cy), MathF.Round(8 * unit), drawColor, (int)stroke);
                Primitives2D.CircleOutline(spriteBatch, new Vector2(cx, cy), MathF.Round(3 * unit), drawColor, (int)stroke);
                DrawLine(spriteBatch, drawColor, new Vector2(cx + 2 * unit, cy - 2 * unit), new Vector2(cx + 9 * unit, cy - 9 * unit), stroke);
                break;
            case "crit damage":
            {
                Span<Vector2> points = stackalloc Vector2[16];
                for (int index = 0; index < 16; index++)
                {
                    float radius = (index % 2 == 0 ? 9f : 4f) * unit;
                    var point = Rotate(new Vector2(0, -radius), index * 22.5f);
                    points[index] = new Vector2(cx + point.X, cy + point.Y);
                }
                Primitives2D.PolygonOutlineSpan(spriteBatch, points, drawColor, (int)stroke);
                break;
            }
            case "aura size":
                for (float radius = 3f; radius <= 9f; radius += 3f)
                    Primitives2D.CircleOutline(spriteBatch, new Vector2(cx, cy), MathF.Round(radius * unit), drawColor, (int)stroke);
                break;
            case "aura strength":
                Primitives2D.CircleOutline(spriteBatch, new Vector2(cx, cy), MathF.Round(3 * unit), drawColor, (int)stroke);
                Span<Vector2> arrow = stackalloc Vector2[3];
                for (float angle = 0f; angle < 360f; angle += 90f)
                {
                    var direction = Rotate(new Vector2(0, -1), angle);
                    var start = new Vector2(cx + direction.X * 9 * unit, cy + direction.Y * 9 * unit);
                    var end = new Vector2(cx + direction.X * 4 * unit, cy + direction.Y * 4 * unit);
                    DrawLine(spriteBatch, drawColor, start, end, stroke);
                    var sideA = Rotate(direction, 28);
                    var sideB = Rotate(direction, -28);
                    arrow[0] = end;
                    arrow[1] = new Vector2(end.X + sideA.X * 3 * unit, end.Y + sideA.Y * 3 * unit);
                    arrow[2] = new Vector2(end.X + sideB.X * 3 * unit, end.Y + sideB.Y * 3 * unit);
                    Primitives2D.FillPolygonSpan(spriteBatch, arrow, drawColor);
                }
                break;
            case "exp multiplier":
            {
                Span<Vector2> gem = stackalloc Vector2[]
                {
                    new Vector2(cx, cy - 9 * unit), new Vector2(cx + 7 * unit, cy - 3 * unit),
                    new Vector2(cx + 5 * unit, cy + 7 * unit), new Vector2(cx - 5 * unit, cy + 7 * unit),
                    new Vector2(cx - 7 * unit, cy - 3 * unit),
                };
                Primitives2D.PolygonOutlineSpan(spriteBatch, gem, drawColor, (int)stroke);
                DrawLine(spriteBatch, drawColor, new Vector2(cx - 6 * unit, cy - 3 * unit), new Vector2(cx + 6 * unit, cy - 3 * unit), stroke);
                break;
            }
            case "mind tokens":
                Primitives2D.CircleOutline(spriteBatch, new Vector2(cx, cy), MathF.Round(7 * unit), drawColor, (int)stroke);
                Primitives2D.CircleOutline(spriteBatch, new Vector2(cx, cy), MathF.Round(3.5f * unit), drawColor, Math.Max(1, (int)(stroke * .7f)));
                break;
            case "vault":
            {
                var body = new Rectangle((int)(cx - 6 * unit), (int)(cy - 2 * unit), (int)(12 * unit), (int)(9 * unit));
                Primitives2D.RectOutline(spriteBatch, body, drawColor, (int)stroke);
                Primitives2D.CircleOutline(spriteBatch, new Vector2(cx, cy - 2 * unit), MathF.Round(4 * unit), drawColor, (int)stroke);
                Primitives2D.FillRect(spriteBatch,
                    new Rectangle((int)(cx - unit), (int)(cy + .5f * unit), (int)(2 * unit), (int)(2 * unit)), drawColor);
                break;
            }
            case "quests":
            {
                DrawLine(spriteBatch, drawColor, new Vector2(cx - 6 * unit, cy + 8 * unit), new Vector2(cx - 6 * unit, cy - 8 * unit), stroke);
                Span<Vector2> flag = stackalloc Vector2[]
                {
                    new Vector2(cx - 6 * unit, cy - 8 * unit), new Vector2(cx + 6 * unit, cy - 5 * unit),
                    new Vector2(cx - 6 * unit, cy - 2 * unit),
                };
                Primitives2D.FillPolygonSpan(spriteBatch, flag, drawColor);
                break;
            }
            case "skills":
            {
                for (float angle = 0f; angle < 360f; angle += 72f)
                {
                    var direction = Rotate(new Vector2(0, -1), angle);
                    DrawLine(spriteBatch, drawColor, new Vector2(cx, cy),
                        new Vector2(cx + direction.X * 8 * unit, cy + direction.Y * 8 * unit), stroke);
                }
                Primitives2D.CircleOutline(spriteBatch, new Vector2(cx, cy), MathF.Round(2.5f * unit), drawColor, (int)stroke);
                break;
            }
            default:
                Primitives2D.CircleOutline(spriteBatch, new Vector2(cx, cy), MathF.Round(7 * unit), drawColor, (int)stroke);
                DrawLine(spriteBatch, drawColor, new Vector2(cx, cy - 4 * unit), new Vector2(cx, cy + 4 * unit), stroke);
                break;
        }
    }

    /// <summary>Draws a rarity-backed mini card with a stat icon and scaling corner mark.</summary>
    public static Rectangle DrawUpgradeCard(SpriteBatch spriteBatch, Rectangle rect, string statName, string rarity,
        string mathType, bool hovered = false)
    {
        Color rarityColor = UiTheme.RarityColors.TryGetValue(rarity, out var color) ? color : UiTheme.Border;
        int cornerRadius = UiTheme.CardCornerRadius(rect.Width);
        var shadow = new Rectangle(rect.X + Math.Max(2, rect.Width / 12), rect.Y + Math.Max(2, rect.Width / 12), rect.Width, rect.Height);
        Primitives2D.FillRoundedRect(spriteBatch, shadow, UiTheme.Shadow, cornerRadius);
        Color fill = hovered ? UiTheme.Lighten(rarityColor, 24) : rarityColor;
        Primitives2D.FillRoundedRect(spriteBatch, rect, fill, cornerRadius);
        Primitives2D.RoundedRectOutline(spriteBatch, rect, UiTheme.Ink, Math.Max(2, rect.Width / 14), cornerRadius);
        var inner = rect;
        inner.Inflate((int)(-rect.Width * .18f), (int)(-rect.Height * .22f));
        DrawStatSymbol(spriteBatch, statName, inner, UiTheme.Ink);

        var markCenter = new Vector2(rect.Right - rect.Width * .18f, rect.Y + rect.Height * .17f);
        float mark = rect.Width * .10f;
        int stroke = Math.Max(2, rect.Width / 14);
        if (mathType == "multiplicative")
        {
            DrawLine(spriteBatch, UiTheme.Text, markCenter - new Vector2(mark, mark), markCenter + new Vector2(mark, mark), stroke);
            DrawLine(spriteBatch, UiTheme.Text, markCenter + new Vector2(mark, -mark), markCenter + new Vector2(-mark, mark), stroke);
        }
        else
        {
            DrawLine(spriteBatch, UiTheme.Text, markCenter - new Vector2(mark, 0), markCenter + new Vector2(mark, 0), stroke);
            DrawLine(spriteBatch, UiTheme.Text, markCenter - new Vector2(0, mark), markCenter + new Vector2(0, mark), stroke);
        }
        return rect;
    }
}
