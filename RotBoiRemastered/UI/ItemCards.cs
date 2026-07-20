using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.Systems;
using RotBoiRemastered.World;

namespace RotBoiRemastered.UI;

/// <summary>Resolution-independent icons and mini cards for equippable loot items. Ported from itemCards.py.</summary>
public static class ItemCards
{
    /// <summary>Sprite folder per slot type -- see Content/Sprites/README.md. Filename is the item's VisualKind, so e.g. slot "weapon" + kind "dagger" looks up Content/Sprites/Weapons/dagger.png.</summary>
    private static readonly Dictionary<string, string> SpriteFolderBySlot = new()
    {
        ["weapon"] = "Weapons", ["armor"] = "Armor", ["ring"] = "Rings", ["accessory"] = "Accessories",
    };

    private static void DrawLine(SpriteBatch spriteBatch, Color color, Vector2 start, Vector2 end, float width)
        => Primitives2D.Line(spriteBatch, start, end, color, Math.Max(1, (int)width));

    /// <summary>
    /// Draws a sprite for (slotType, itemName, visualKind) if one has been
    /// added under Content/Sprites, scaled to fill rect. Tries the specific
    /// item name first (e.g. Weapons/bow_of_dread.png) so a unique -- or any
    /// item that outgrows sharing its VisualKind's generic art -- can get a
    /// sprite distinct from every other item of that same silhouette,
    /// falling back to the shared VisualKind sprite otherwise. Unlike the
    /// procedural symbol below, neither tier applies `color` -- authored art
    /// carries its own palette rather than being flattened to a single ink tint.
    /// </summary>
    private static bool TryDrawItemSprite(SpriteBatch spriteBatch, string slotType, string? itemName, string visualKind, Rectangle rect)
    {
        if (!SpriteFolderBySlot.TryGetValue(slotType.ToLowerInvariant(), out var folder))
            return false;
        var sprite = (itemName is not null ? Sprites.TryGet($"{folder}/{Slug(itemName)}") : null)
            ?? Sprites.TryGet($"{folder}/{visualKind}");
        if (sprite is null)
            return false;
        spriteBatch.Draw(sprite, rect, Color.White);
        return true;
    }

    /// <summary>"Bow of Dread" -> "bow_of_dread", matching how a sprite file for a specific item should be named.</summary>
    private static string Slug(string name)
    {
        var cleaned = new string(name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray());
        return string.Join('_', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    public static void DrawItemSymbol(SpriteBatch spriteBatch, string slotType, Rectangle rect, Color? color = null,
        string? visualKind = null, string? itemName = null)
    {
        if (visualKind is not null && TryDrawItemSprite(spriteBatch, slotType, itemName, visualKind, rect))
            return;

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
                Primitives2D.RoundedRectOutline(spriteBatch, body, drawColor, (int)stroke, (int)MathF.Round(3 * unit));
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

    public static Rectangle DrawItemCard(SpriteBatch spriteBatch, Rectangle rect, ItemDrop item, bool hovered = false)
    {
        Color rarityColor = UiTheme.RarityColors.TryGetValue(item.Rarity, out var color) ? color : UiTheme.Border;
        var core = Items.CoreForgeFor(item);
        Color? coreColor = core is not null ? GamePaths.PathsByKey[core.PathKey].Accent : null;
        int cornerRadius = Math.Max(2, rect.Width / 8);

        if (coreColor is Color glowColor)
            DrawCoreGlow(spriteBatch, rect, glowColor, cornerRadius);
        var shadow = new Rectangle(rect.X + Math.Max(2, rect.Width / 12), rect.Y + Math.Max(2, rect.Width / 12), rect.Width, rect.Height);
        Primitives2D.FillRoundedRect(spriteBatch, shadow, UiTheme.Shadow, cornerRadius);

        // Uniques trade the regular rarity-tinted card (a flat wash of the
        // rarity color) for a dark, near-opaque backdrop with a gold border
        // -- reads as "a special mounted piece", not "an orange square" --
        // plus an animated shine glinting across it (DrawUniqueSheen).
        bool isUnique = item.Rarity == "Unique";
        Color fill = isUnique ? (hovered ? UiTheme.Lighten(UiTheme.Ink, 14) : UiTheme.Ink) : (hovered ? UiTheme.Lighten(rarityColor, 24) : rarityColor);
        Primitives2D.FillRoundedRect(spriteBatch, rect, fill, cornerRadius);
        Primitives2D.RoundedRectOutline(spriteBatch, rect, coreColor ?? (isUnique ? rarityColor : UiTheme.Ink),
            Math.Max(2, rect.Width / 14) + (isUnique ? 1 : 0), cornerRadius);

        var inner = rect;
        inner.Inflate((int)(-rect.Width * .15f), (int)(-rect.Height * .18f));
        // The procedural symbol's line color has to flip to something light
        // for uniques -- it's normally Ink-on-bright-fill, and Ink-on-Ink
        // would be invisible now that the fill itself is dark. Sprites
        // ignore this color entirely either way (see TryDrawItemSprite).
        DrawItemSymbol(spriteBatch, item.SlotType, inner, isUnique ? UiTheme.Gold : UiTheme.Ink, item.Definition.VisualKind, item.Name);

        if (isUnique)
            DrawUniqueSheen(spriteBatch, rect);

        Color gradeColor = UiTheme.GradeColors.GetValueOrDefault(item.Grade, UiTheme.Muted);
        int badgeSize = Math.Max(12, rect.Width / 3);
        var gradeBadge = new Rectangle(rect.Right - badgeSize, rect.Bottom - badgeSize, badgeSize, badgeSize);
        Primitives2D.FillRoundedRect(spriteBatch, gradeBadge, UiTheme.Ink, Math.Max(2, badgeSize / 5));
        Primitives2D.RoundedRectOutline(spriteBatch, gradeBadge, gradeColor, Math.Max(1, badgeSize / 10), Math.Max(2, badgeSize / 5));
        UiTheme.DrawText(spriteBatch, item.Grade, Math.Max(8, badgeSize * .58), gradeColor, gradeBadge.Center.ToVector2(), "center");

        if (coreColor is Color coreBadgeColor)
        {
            float pulse = .75f + .2f * MathF.Sin(Environment.TickCount64 / 210f);
            Primitives2D.FillCircle(spriteBatch,
                new Vector2(rect.X + badgeSize * .45f, rect.Y + badgeSize * .45f),
                Math.Max(3, badgeSize * .20f), coreBadgeColor * pulse);
        }
        return rect;
    }

    private static void DrawCoreGlow(SpriteBatch spriteBatch, Rectangle rect, Color color, int cornerRadius)
    {
        float pulse = .55f + .25f * MathF.Sin(Environment.TickCount64 / 240f);
        for (int layer = 3; layer >= 1; layer--)
        {
            var glow = rect;
            glow.Inflate(layer * 2, layer * 2);
            Primitives2D.RoundedRectOutline(spriteBatch, glow, color * (pulse / (layer + 1)),
                Math.Max(1, layer), cornerRadius + layer);
        }
    }

    /// <summary>
    /// A bright diagonal gold band -- like a bar of light glancing off a
    /// polished surface -- sweeps left-to-right across the card, pauses,
    /// then repeats every 1.6s. Drawn one horizontal strip at a time (a
    /// manual per-row clip, each strip's left/right edges clamped to rect)
    /// rather than a rotated rectangle or a real scissor rect: DrawItemCard
    /// is called mid-batch from callers with their own already-open
    /// SpriteBatch.Begin(), so a scissor rect here would mean an unwanted
    /// nested End()/Begin() that clobbers whatever blend/transform state the
    /// caller's batch was using. The band's travel range starts and ends far
    /// enough outside rect (by its own width/slope) that it's fully
    /// off-card -- and therefore fully clamped away to nothing -- at both
    /// ends of the sweep, so it visibly enters and exits corner-first rather
    /// than popping in/out. Public so InformationSheet's item-tooltip header
    /// icon (a separate small draw path, not DrawItemCard) can apply the
    /// same shine to stay visually consistent.
    ///
    /// Timed off the real-time system clock (Environment.TickCount64)
    /// rather than any RunState/SoulHub clock passed in -- RunState.RunTimeSeconds
    /// specifically only advances during UpdateGameRun (by design: it's a
    /// gameplay stat, not a frame clock), so it sits frozen while browsing
    /// the Soul's inventory sidebar, which is exactly where a unique's card
    /// is most commonly just sitting there being looked at. A wall-clock
    /// read keeps this purely-cosmetic animation running in every game
    /// state uniformly, with no per-call-site plumbing required.
    /// </summary>
    public static void DrawUniqueSheen(SpriteBatch spriteBatch, Rectangle rect)
    {
        const double period = 1.6;
        const double activeFraction = .7; // sweeps for 70% of the cycle, pauses for the rest
        double t = Environment.TickCount64 / 1000.0 % period / period;
        if (t > activeFraction)
            return;
        float sweep = (float)(t / activeFraction); // 0..1 across the active window

        const float slope = .5f; // horizontal drift per row -- the band's diagonal tilt
        float bandWidth = Math.Max(4f, rect.Width * .30f);
        float coreWidth = bandWidth * .35f;
        float margin = bandWidth + rect.Height * slope; // keeps the band fully off-card at sweep 0/1
        float bandTopX = MathHelper.Lerp(rect.Left - margin, rect.Right + margin, sweep);

        for (int y = rect.Top; y < rect.Bottom; y += 2)
        {
            float centerX = bandTopX + (y - rect.Top) * slope;
            DrawClampedStrip(spriteBatch, rect, y, centerX, bandWidth, UiTheme.Gold * .5f);
            DrawClampedStrip(spriteBatch, rect, y, centerX, coreWidth, Color.White * .75f);
        }
    }

    private static void DrawClampedStrip(SpriteBatch spriteBatch, Rectangle rect, int y, float centerX, float width, Color color)
    {
        int left = (int)Math.Max(rect.Left, centerX - width / 2f);
        int right = (int)Math.Min(rect.Right, centerX + width / 2f);
        if (right > left)
            Primitives2D.FillRect(spriteBatch, new Rectangle(left, y, right - left, 2), color);
    }
}
