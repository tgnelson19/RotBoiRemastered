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
    private const float BaseSize = Simulation.TileSize * 0.6f;
    /// <summary>A unique-holding crate draws (and can be interacted with, see GameSession.UpdateCrateInteraction) at 2x a normal crate's footprint -- big enough that the treasure sprite and its orbiting aura actually read as a standout drop instead of blending in with common loot.</summary>
    private const float UniqueSizeMultiplier = 2f;

    public float WorldX { get; }
    public float WorldY { get; }
    public float Size => ContainsUnique ? BaseSize * UniqueSizeMultiplier : BaseSize;
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

    /// <summary>"Unique" isn't in Upgrades.RarityOrder (crates can't roll one the normal way -- only a boss's fixed drop table grants one), so this is checked directly rather than through Tint()'s rarity-order ranking.</summary>
    public bool ContainsUnique => Items.Any(item => item.Rarity == "Unique");

    /// <summary>
    /// This draws in its own unscaled SpriteBatch pass (see
    /// GameSession.DrawLootCrates's scissor-clipped Begin/End, which has no
    /// zoom transform matrix like the main entity batch does) -- so unlike
    /// bullets/enemies/the player, which get zoom for free from that matrix,
    /// position and size both need Camera.ApplyZoom/Zoom applied by hand here,
    /// or crates stay a fixed screen position/size while everything around
    /// them zooms, reading as if they're floating independently of the world.
    ///
    /// Rotates the crate's world-space *center* (WorldX/Y + half its Size),
    /// not its WorldX/Y corner -- rotating the corner and then extending an
    /// axis-aligned rect from it adds a constant screen-space (Size/2, Size/2)
    /// nudge that never itself rotates with the camera, so the rendered box
    /// orbits Camera.Lock on a circle recentered by that fixed offset instead
    /// of the one every correctly-centered entity/the ground grid actually
    /// rotates around. Invisible at the old small crate size, but scales with
    /// Size, so it became an obvious "the chest floats/drifts loose from the
    /// floor" wobble once unique crates got 2x bigger.
    /// </summary>
    public void Draw(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        Vector2 worldCenter = new(WorldX + Size / 2f, WorldY + Size / 2f);
        Vector2 screenCenter = camera.ApplyZoom(camera.WorldToScreen(worldCenter, playerWorldPosition, screenShake));
        float size = Size * camera.Zoom;
        var rect = new Rectangle((int)(screenCenter.X - size / 2f), (int)(screenCenter.Y - size / 2f), (int)size, (int)size);
        Color accent = Tint();

        var shadowRect = new Rectangle(rect.X + 4, (int)(rect.Bottom - rect.Height * 0.18f), rect.Width, (int)(rect.Height * 0.18f));
        Primitives2D.FillEllipse(spriteBatch, shadowRect, UiTheme.Shadow);

        // A crate holding a unique drop gets its own art (Content/Sprites/Misc/treasure.png)
        // instead of the plain procedural box every other crate uses, same
        // "PNG overrides procedural" pattern as ItemCards/ProjectileVisuals --
        // see Content/Sprites/README.md.
        var treasureSprite = ContainsUnique ? Sprites.TryGet("Misc/treasure") : null;
        if (treasureSprite is not null)
        {
            DrawTreasureAura(spriteBatch, rect);
            spriteBatch.Draw(treasureSprite, rect, Color.White);
            return;
        }

        int border = Math.Max(2, (int)(size * 0.08f));
        Primitives2D.FillRect(spriteBatch, rect, UiTheme.Ink);
        Primitives2D.RectOutline(spriteBatch, rect, accent, border);

        float lidY = rect.Y + rect.Height * 0.35f;
        Primitives2D.Line(spriteBatch, new Vector2(rect.X, lidY), new Vector2(rect.Right, lidY), accent, Math.Max(2, (int)(size * 0.06f)));
        Primitives2D.Line(spriteBatch, new Vector2(rect.Center.X, rect.Y), new Vector2(rect.Center.X, rect.Bottom), accent, Math.Max(1, (int)(size * 0.04f)));
    }

    /// <summary>
    /// Two golden points orbit the chest in a flattened ellipse (matching the
    /// squashed shadow ellipse drawn above), each dragging a fading trail of
    /// shrinking dots behind it -- reads as a beam of light swirling around
    /// the chest rather than a static glow. Wall-clock timed
    /// (Environment.TickCount64), same reasoning as ItemCards.DrawUniqueSheen:
    /// keeps running uniformly with no elapsedSeconds threaded through Draw's
    /// call chain.
    /// </summary>
    private static void DrawTreasureAura(SpriteBatch spriteBatch, Rectangle rect)
    {
        const double period = 2.4; // seconds per full orbit
        const int trailSegments = 16;
        const float trailSpan = MathHelper.TwoPi * .5f; // how much of the orbit the fading trail covers

        float headAngle = (float)(Environment.TickCount64 / 1000.0 % period / period * MathHelper.TwoPi);
        float orbitRx = rect.Width * .85f;
        float orbitRy = rect.Height * .5f;
        var center = new Vector2(rect.Center.X, rect.Center.Y);

        for (int beam = 0; beam < 2; beam++)
        {
            float beamOffset = beam * MathHelper.Pi;
            for (int i = 0; i < trailSegments; i++)
            {
                float t = i / (float)(trailSegments - 1);
                float angle = headAngle + beamOffset - t * trailSpan;
                float alpha = (1f - t) * .85f;
                float dotRadius = MathHelper.Lerp(3.5f, 1f, t);
                var point = center + new Vector2(MathF.Cos(angle) * orbitRx, MathF.Sin(angle) * orbitRy);
                Primitives2D.FillCircle(spriteBatch, point, dotRadius, UiTheme.Gold * alpha);
            }
        }
    }
}
