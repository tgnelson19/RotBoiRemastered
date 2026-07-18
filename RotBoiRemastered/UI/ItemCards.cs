using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.Systems;

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

    public static void DrawItemSymbol(SpriteBatch spriteBatch, string slotType, Rectangle rect, Color? color = null,
        string? visualKind = null)
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
                if (visualKind == "bow")
                {
                    var top = new Vector2(cx + 2 * unit, cy - 9 * unit);
                    var middle = new Vector2(cx - 4 * unit, cy);
                    var bottom = new Vector2(cx + 2 * unit, cy + 9 * unit);
                    DrawLine(spriteBatch, drawColor, top, middle, stroke);
                    DrawLine(spriteBatch, drawColor, middle, bottom, stroke);
                    DrawLine(spriteBatch, drawColor, top, bottom, Math.Max(1, stroke * .55f));
                    DrawLine(spriteBatch, drawColor, new Vector2(cx - 8 * unit, cy), new Vector2(cx + 8 * unit, cy), stroke);
                }
                else if (visualKind == "wand")
                {
                    DrawLine(spriteBatch, drawColor, new Vector2(cx - 6 * unit, cy + 8 * unit), new Vector2(cx + 3 * unit, cy - 4 * unit), stroke);
                    Primitives2D.CircleOutline(spriteBatch, new Vector2(cx + 5 * unit, cy - 7 * unit), MathF.Round(3 * unit), drawColor, (int)stroke);
                    foreach (float angle in new[] { 0f, 90f, 180f, 270f })
                    {
                        float radians = MathHelper.ToRadians(angle);
                        var direction = new Vector2(MathF.Cos(radians), MathF.Sin(radians));
                        DrawLine(spriteBatch, drawColor, new Vector2(cx + 5 * unit, cy - 7 * unit) + direction * 4 * unit,
                            new Vector2(cx + 5 * unit, cy - 7 * unit) + direction * 6 * unit, Math.Max(1, stroke * .7f));
                    }
                }
                else if (visualKind == "spear")
                {
                    DrawLine(spriteBatch, drawColor, new Vector2(cx - 7 * unit, cy + 9 * unit), new Vector2(cx + 5 * unit, cy - 6 * unit), stroke);
                    Primitives2D.FillPolygon(spriteBatch, new[]
                    {
                        new Vector2(cx + 5 * unit, cy - 9 * unit), new Vector2(cx + 9 * unit, cy - 9 * unit),
                        new Vector2(cx + 7 * unit, cy - 4 * unit),
                    }, drawColor);
                }
                else
                {
                    float bladeLength = visualKind == "dagger" ? 7 : 12;
                    var tip = new Vector2(cx + bladeLength * .55f * unit, cy - bladeLength * .72f * unit);
                    var guardPoint = new Vector2(cx - 1 * unit, cy + 2 * unit);
                    var pommel = new Vector2(cx - 6 * unit, cy + 8 * unit);
                    DrawLine(spriteBatch, drawColor, tip, guardPoint, stroke);
                    DrawLine(spriteBatch, drawColor, guardPoint, pommel, Math.Max(1f, stroke * .7f));
                    Vector2 bladeDir = Vector2.Normalize(guardPoint - tip);
                    Vector2 perp = new Vector2(-bladeDir.Y, bladeDir.X) * (visualKind == "dagger" ? 2.5f : 4f) * unit;
                    DrawLine(spriteBatch, drawColor, guardPoint - perp, guardPoint + perp, stroke);
                    Primitives2D.FillCircle(spriteBatch, pommel, Math.Max(1f, MathF.Round(unit * 1.5f)), drawColor);
                }
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
                if (visualKind == "chain")
                {
                    for (int row = -3; row <= 4; row += 3)
                        DrawLine(spriteBatch, drawColor, new Vector2(cx - 5 * unit, cy + row * unit), new Vector2(cx + 5 * unit, cy + row * unit), Math.Max(1, stroke * .45f));
                }
                else if (visualKind == "plate")
                {
                    DrawLine(spriteBatch, drawColor, new Vector2(cx, cy - 1 * unit), new Vector2(cx, cy + 7 * unit), stroke);
                    DrawLine(spriteBatch, drawColor, new Vector2(cx - 6 * unit, cy - 5 * unit), new Vector2(cx - 9 * unit, cy - 1 * unit), stroke);
                    DrawLine(spriteBatch, drawColor, new Vector2(cx + 6 * unit, cy - 5 * unit), new Vector2(cx + 9 * unit, cy - 1 * unit), stroke);
                }
                break;
            }
            case "ring":
            {
                Primitives2D.CircleOutline(spriteBatch, new Vector2(cx, cy + 2 * unit), MathF.Round(7 * unit), drawColor, (int)stroke);
                if (visualKind != "band")
                {
                    var gem = new[]
                    {
                        new Vector2(cx, cy - 9 * unit), new Vector2(cx + 3 * unit, cy - 5 * unit),
                        new Vector2(cx, cy - 2 * unit), new Vector2(cx - 3 * unit, cy - 5 * unit),
                    };
                    Primitives2D.PolygonOutline(spriteBatch, gem, drawColor, (int)stroke);
                }
                break;
            }
            case "accessory":
            {
                if (visualKind == "vial")
                {
                    var bottle = new Rectangle((int)(cx - 5 * unit), (int)(cy - 3 * unit), (int)(10 * unit), (int)(12 * unit));
                    Primitives2D.RectOutline(spriteBatch, bottle, drawColor, (int)stroke);
                    DrawLine(spriteBatch, drawColor, new Vector2(cx - 2 * unit, cy - 8 * unit), new Vector2(cx - 2 * unit, cy - 3 * unit), stroke);
                    DrawLine(spriteBatch, drawColor, new Vector2(cx + 2 * unit, cy - 8 * unit), new Vector2(cx + 2 * unit, cy - 3 * unit), stroke);
                    DrawLine(spriteBatch, drawColor, new Vector2(cx - 4 * unit, cy + 4 * unit), new Vector2(cx + 4 * unit, cy + 4 * unit), stroke);
                }
                else if (visualKind == "bell")
                {
                    Primitives2D.PolygonOutline(spriteBatch, new[]
                    {
                        new Vector2(cx, cy - 8 * unit), new Vector2(cx + 7 * unit, cy + 6 * unit),
                        new Vector2(cx - 7 * unit, cy + 6 * unit),
                    }, drawColor, (int)stroke);
                    Primitives2D.FillCircle(spriteBatch, new Vector2(cx, cy + 8 * unit), MathF.Round(1.8f * unit), drawColor);
                }
                else if (visualKind == "badge")
                {
                    Primitives2D.PolygonOutline(spriteBatch, new[]
                    {
                        new Vector2(cx, cy - 9 * unit), new Vector2(cx + 7 * unit, cy - 3 * unit),
                        new Vector2(cx + 4 * unit, cy + 8 * unit), new Vector2(cx, cy + 5 * unit),
                        new Vector2(cx - 4 * unit, cy + 8 * unit), new Vector2(cx - 7 * unit, cy - 3 * unit),
                    }, drawColor, (int)stroke);
                }
                else
                {
                    Primitives2D.CircleOutline(spriteBatch, new Vector2(cx, cy - 7 * unit), MathF.Round(2.5f * unit), drawColor, (int)stroke);
                    var gem = new[]
                    {
                        new Vector2(cx, cy - 2 * unit), new Vector2(cx + 5 * unit, cy + 4 * unit),
                        new Vector2(cx, cy + 9 * unit), new Vector2(cx - 5 * unit, cy + 4 * unit),
                    };
                    Primitives2D.PolygonOutline(spriteBatch, gem, drawColor, (int)stroke);
                }
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

    public static Rectangle DrawItemCard(SpriteBatch spriteBatch, Rectangle rect, ItemDrop item, bool hovered = false)
    {
        Color rarityColor = UiTheme.RarityColors.TryGetValue(item.Rarity, out var color) ? color : UiTheme.Border;
        var shadow = new Rectangle(rect.X + Math.Max(2, rect.Width / 12), rect.Y + Math.Max(2, rect.Width / 12), rect.Width, rect.Height);
        Primitives2D.FillRect(spriteBatch, shadow, UiTheme.Shadow);
        Primitives2D.FillRect(spriteBatch, rect, hovered ? UiTheme.Lighten(rarityColor, 24) : rarityColor);
        Primitives2D.RectOutline(spriteBatch, rect, UiTheme.Ink, Math.Max(2, rect.Width / 14));
        var inner = rect;
        inner.Inflate((int)(-rect.Width * .15f), (int)(-rect.Height * .18f));
        DrawItemSymbol(spriteBatch, item.SlotType, inner, UiTheme.Ink, item.Definition.VisualKind);
        return rect;
    }
}
