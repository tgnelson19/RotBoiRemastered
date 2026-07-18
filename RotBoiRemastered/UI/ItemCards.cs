using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;

namespace RotBoiRemastered.UI;

/// <summary>
/// Resolution-independent icons and mini cards for equippable loot items.
/// Ported from itemCards.py.
///
/// Known difference from the Python original: pygame.draw.rect's
/// `border_radius` (rounded corners on the armor icon body and the card
/// chrome) has no Primitives2D equivalent (no rounded-rect primitive exists
/// yet) -- both render as sharp-cornered rects instead. Purely cosmetic;
/// revisit if a rounded-rect primitive gets added for some other module.
/// </summary>
public static class ItemCards
{
    private static void DrawLine(SpriteBatch spriteBatch, Color color, Vector2 start, Vector2 end, float width)
        => Primitives2D.Line(spriteBatch, start, end, color, Math.Max(1, (int)width));

    public static void DrawItemSymbol(SpriteBatch spriteBatch, string slotType, Rectangle rect, Color? color = null)
    {
        Color drawColor = color ?? UiTheme.Text;
        float cx = rect.Center.X, cy = rect.Center.Y;
        float unit = Math.Max(1f, Math.Min(rect.Width, rect.Height) / 20f);
        float stroke = Math.Max(1f, MathF.Round(unit * 1.7f));
        string name = slotType.ToLowerInvariant();

        switch (name)
        {
            case "weapon":
            {
                var tip = new Vector2(cx + 6 * unit, cy - 9 * unit);
                var guardPoint = new Vector2(cx - 1 * unit, cy + 2 * unit);
                var pommel = new Vector2(cx - 6 * unit, cy + 8 * unit);
                DrawLine(spriteBatch, drawColor, tip, guardPoint, stroke);
                DrawLine(spriteBatch, drawColor, guardPoint, pommel, Math.Max(1f, stroke * .7f));
                Vector2 bladeDir = Vector2.Normalize(guardPoint - tip);
                Vector2 perp = new Vector2(-bladeDir.Y, bladeDir.X) * 4 * unit;
                DrawLine(spriteBatch, drawColor, guardPoint - perp, guardPoint + perp, stroke);
                Primitives2D.FillCircle(spriteBatch, pommel, Math.Max(1f, MathF.Round(unit * 1.5f)), drawColor);
                break;
            }
            case "armor":
            {
                var body = new Rectangle(0, 0, (int)(13 * unit), (int)(15 * unit));
                body.X = (int)(cx - body.Width / 2f);
                body.Y = (int)(cy + 1 * unit - body.Height / 2f);
                Primitives2D.RectOutline(spriteBatch, body, drawColor, (int)stroke);
                DrawLine(spriteBatch, drawColor, new Vector2(cx, cy - 6 * unit), new Vector2(cx - 3 * unit, cy - 1 * unit), Math.Max(1f, stroke * .7f));
                DrawLine(spriteBatch, drawColor, new Vector2(cx, cy - 6 * unit), new Vector2(cx + 3 * unit, cy - 1 * unit), Math.Max(1f, stroke * .7f));
                break;
            }
            case "ring":
            {
                Primitives2D.CircleOutline(spriteBatch, new Vector2(cx, cy + 2 * unit), MathF.Round(7 * unit), drawColor, (int)stroke);
                var gem = new[]
                {
                    new Vector2(cx, cy - 9 * unit),
                    new Vector2(cx + 3 * unit, cy - 5 * unit),
                    new Vector2(cx, cy - 2 * unit),
                    new Vector2(cx - 3 * unit, cy - 5 * unit),
                };
                Primitives2D.PolygonOutline(spriteBatch, gem, drawColor, (int)stroke);
                break;
            }
            case "accessory":
            {
                Primitives2D.CircleOutline(spriteBatch, new Vector2(cx, cy - 7 * unit), MathF.Round(2.5f * unit), drawColor, (int)stroke);
                var gem = new[]
                {
                    new Vector2(cx, cy - 2 * unit),
                    new Vector2(cx + 5 * unit, cy + 4 * unit),
                    new Vector2(cx, cy + 9 * unit),
                    new Vector2(cx - 5 * unit, cy + 4 * unit),
                };
                Primitives2D.PolygonOutline(spriteBatch, gem, drawColor, (int)stroke);
                break;
            }
            default:
                Primitives2D.CircleOutline(spriteBatch, new Vector2(cx, cy), MathF.Round(7 * unit), drawColor, (int)stroke);
                break;
        }
    }

    /// <summary>Draws a rarity-backed mini card with an item slot-type icon.</summary>
    public static Rectangle DrawItemCard(SpriteBatch spriteBatch, Rectangle rect, string slotType, string rarity, bool hovered = false)
    {
        Color rarityColor = UiTheme.RarityColors.TryGetValue(rarity, out var color) ? color : UiTheme.Border;
        var shadow = new Rectangle(rect.X + Math.Max(2, rect.Width / 12), rect.Y + Math.Max(2, rect.Width / 12), rect.Width, rect.Height);
        Primitives2D.FillRect(spriteBatch, shadow, UiTheme.Shadow);
        Color fill = hovered ? UiTheme.Lighten(rarityColor, 24) : rarityColor;
        Primitives2D.FillRect(spriteBatch, rect, fill);
        Primitives2D.RectOutline(spriteBatch, rect, UiTheme.Ink, Math.Max(2, rect.Width / 14));
        var inner = rect;
        inner.Inflate((int)(-rect.Width * .18f), (int)(-rect.Height * .22f));
        DrawItemSymbol(spriteBatch, slotType, inner, UiTheme.Ink);
        return rect;
    }
}
