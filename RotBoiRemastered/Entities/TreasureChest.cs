using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.Systems;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// Guaranteed multi-item reward placed only in generated treasure rooms.
/// It remains compatible with the normal nearby-loot/equipment drag flow by
/// extending LootCrate, but has its own large block-built chest silhouette.
/// </summary>
public sealed class TreasureChest : LootCrate
{
    public const int MinimumItems = 2;
    private const float ChestSize = Simulation.TileSize * 1.15f;

    public override float Size => ChestSize;
    public string? ThemeKey { get; }

    public TreasureChest(float worldX, float worldY, IEnumerable<ItemDrop> drops, string? themeKey = null)
        : base(worldX, worldY, drops)
    {
        if (Items.Count < MinimumItems)
            throw new ArgumentException($"A treasure chest must contain at least {MinimumItems} items.", nameof(drops));
        if (themeKey is not null && !GamePaths.PathsByKey.ContainsKey(themeKey))
            throw new ArgumentException($"Unknown treasure-chest theme: {themeKey}", nameof(themeKey));
        ThemeKey = themeKey;
    }

    protected override void DrawAt(
        SpriteBatch spriteBatch,
        Vector2 center,
        float size,
        float animationTime)
    {
        var body = new Rectangle(
            (int)(center.X - size / 2f),
            (int)(center.Y - size * .38f),
            (int)size,
            (int)(size * .72f));
        var lid = new Rectangle(body.X - Math.Max(2, body.Width / 18), body.Y,
            body.Width + Math.Max(4, body.Width / 9), Math.Max(8, body.Height / 3));
        Color themeAccent = ThemeKey is not null ? GamePaths.PathsByKey[ThemeKey].Accent : UiTheme.Gold;
        Color accent = Items.Count == 0 ? UiTheme.Border : Color.Lerp(Tint(), themeAccent, .62f);

        float pulse = .82f + .12f * MathF.Sin(animationTime * 3.2f);
        Primitives2D.CircleOutline(spriteBatch, center, size * .68f, themeAccent * pulse, Math.Max(2, (int)(size * .035f)));
        Primitives2D.FillEllipse(spriteBatch,
            new Rectangle(body.X + 3, body.Bottom - body.Height / 8, body.Width, Math.Max(5, body.Height / 4)),
            UiTheme.Shadow);

        // Lid, lower coffer, and offset highlights are intentionally square:
        // the same bit-built pseudo-depth used by enemies and raised scenery.
        Primitives2D.FillRect(spriteBatch, new Rectangle(body.X + 5, body.Y + 7, body.Width, body.Height), UiTheme.Shadow);
        Primitives2D.FillRect(spriteBatch, body, new Color(58, 39, 31));
        Primitives2D.FillRect(spriteBatch, lid, new Color(82, 55, 35));
        Primitives2D.RectOutline(spriteBatch, body, UiTheme.Ink, Math.Max(3, (int)(size * .07f)));
        Primitives2D.RectOutline(spriteBatch, lid, accent, Math.Max(3, (int)(size * .06f)));
        Primitives2D.Line(spriteBatch, new Vector2(body.Left, body.Y + body.Height * .48f),
            new Vector2(body.Right, body.Y + body.Height * .48f), accent, Math.Max(2, (int)(size * .045f)));

        int lockSize = Math.Max(7, (int)(size * .18f));
        var chestLock = new Rectangle(body.Center.X - lockSize / 2, body.Y + body.Height / 2 - lockSize / 3,
            lockSize, lockSize);
        Primitives2D.FillRect(spriteBatch, chestLock, UiTheme.Ink);
        Primitives2D.RectOutline(spriteBatch, chestLock, Items.Count == 0 ? UiTheme.Border : UiTheme.Gold,
            Math.Max(2, lockSize / 5));
    }
}
