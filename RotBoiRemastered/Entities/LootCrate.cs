using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.Systems;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// A stationary world-space container of loot dropped by a defeated enemy.
/// Ported from lootCrate.py. Stationary, so unlike the other entities here
/// there's no Update -- only Draw.
/// </summary>
public sealed class LootCrate
{
    public float WorldX { get; }
    public float WorldY { get; }
    public float Size { get; } = Simulation.TileSize * 0.6f;
    public List<ItemDrop> Items { get; }

    public LootCrate(float worldX, float worldY, IEnumerable<ItemDrop> drops)
    {
        WorldX = worldX;
        WorldY = worldY;
        Items = new List<ItemDrop>(drops);
    }

    public Rectangle WorldRect() => new((int)WorldX, (int)WorldY, (int)Size, (int)Size);

    /// <summary>Public (Python's `_tint` was underscore-private in name only, and its own test suite called it directly).</summary>
    public Color Tint()
    {
        if (Items.Count == 0)
            return UiTheme.Border;
        var rarityOrder = Upgrades.RarityOrder.ToList();
        var best = Items.OrderByDescending(item => rarityOrder.IndexOf(item.Rarity)).First();
        return UiTheme.RarityColors.TryGetValue(best.Rarity, out var color) ? color : UiTheme.Border;
    }

    /// <summary>
    /// This draws in its own unscaled SpriteBatch pass (see
    /// GameSession.DrawLootCrates's scissor-clipped Begin/End, which has no
    /// zoom transform matrix like the main entity batch does) -- so unlike
    /// bullets/enemies/the player, which get zoom for free from that matrix,
    /// position and size both need Camera.ApplyZoom/Zoom applied by hand here,
    /// or crates stay a fixed screen position/size while everything around
    /// them zooms, reading as if they're floating independently of the world.
    /// </summary>
    public void Draw(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        Vector2 screenPosition = camera.ApplyZoom(camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake));
        float size = Size * camera.Zoom;
        var rect = new Rectangle((int)screenPosition.X, (int)screenPosition.Y, (int)size, (int)size);
        Color accent = Tint();
        int border = Math.Max(2, (int)(size * 0.08f));

        var shadowRect = new Rectangle(rect.X + 4, (int)(rect.Bottom - rect.Height * 0.18f), rect.Width, (int)(rect.Height * 0.18f));
        Primitives2D.FillEllipse(spriteBatch, shadowRect, UiTheme.Shadow);
        Primitives2D.FillRect(spriteBatch, rect, UiTheme.Ink);
        Primitives2D.RectOutline(spriteBatch, rect, accent, border);

        float lidY = rect.Y + rect.Height * 0.35f;
        Primitives2D.Line(spriteBatch, new Vector2(rect.X, lidY), new Vector2(rect.Right, lidY), accent, Math.Max(2, (int)(size * 0.06f)));
        Primitives2D.Line(spriteBatch, new Vector2(rect.Center.X, rect.Y), new Vector2(rect.Center.X, rect.Bottom), accent, Math.Max(1, (int)(size * 0.04f)));
    }
}
