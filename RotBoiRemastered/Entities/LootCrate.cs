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
public class LootCrate
{
    private const float BaseSize = Simulation.TileSize * 0.6f;
    /// <summary>A unique-holding crate draws (and can be interacted with, see GameSession.UpdateCrateInteraction) at 2x a normal crate's footprint -- big enough that the treasure sprite and its orbiting aura actually read as a standout drop instead of blending in with common loot.</summary>
    private const float UniqueSizeMultiplier = 2f;

    public float WorldX { get; }
    public float WorldY { get; }
    public virtual float Size => ContainsUnique ? BaseSize * UniqueSizeMultiplier : BaseSize;
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
        if (CoreAccent is Color coreAccent)
            return coreAccent;
        var rarityOrder = Upgrades.RarityOrder.ToList();
        var best = Items.OrderByDescending(item => rarityOrder.IndexOf(item.Rarity)).First();
        return UiTheme.RarityColors.TryGetValue(best.Rarity, out var color) ? color : UiTheme.Border;
    }

    /// <summary>"Unique" isn't in Upgrades.RarityOrder (crates can't roll one the normal way -- only a boss's fixed drop table grants one), so this is checked directly rather than through Tint()'s rarity-order ranking.</summary>
    public bool ContainsUnique => Items.Any(item => item.Rarity == "Unique");

    /// <summary>
    /// Best Legendary/Mythical rarity among contained items, or null if
    /// there isn't one. Before the item-system rework, a crate's orbiting
    /// aura (DrawTreasureAura) only ever fired for ContainsUnique -- a crate
    /// holding three Legendaries looked identical to one holding three
    /// Commons. This is what lets DrawAt give Legendary/Mythical crates
    /// their own scaled-down version of that same fanfare instead, so the
    /// rarity of what's inside is legible before you've even opened it.
    /// </summary>
    public string? BestHighTierRarity => Items
        .Select(item => item.Rarity)
        .Where(rarity => rarity is "Legendary" or "Mythical")
        .OrderByDescending(rarity => rarity == "Mythical")
        .FirstOrDefault();

    public bool ContainsCoreForged => Items.Any(item => RotBoiRemastered.Systems.Items.CoreForgeFor(item) is not null);

    public Color? CoreAccent
    {
        get
        {
            var core = Items.Select(RotBoiRemastered.Systems.Items.CoreForgeFor).FirstOrDefault(value => value is not null);
            return core is not null ? GamePaths.PathsByKey[core.PathKey].Accent : null;
        }
    }

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
    public virtual void Draw(
        SpriteBatch spriteBatch,
        Camera camera,
        Vector2 playerWorldPosition,
        Vector2 screenShake,
        float animationTime = 0f)
    {
        Vector2 worldCenter = new(WorldX + Size / 2f, WorldY + Size / 2f);
        Vector2 screenCenter = camera.ApplyZoom(camera.WorldToScreen(worldCenter, playerWorldPosition, screenShake));
        float size = Size * camera.Zoom;
        DrawAt(spriteBatch, screenCenter, size, animationTime);
    }

    public void DrawInWorldPass(
        SpriteBatch spriteBatch,
        Camera camera,
        Vector2 playerWorldPosition,
        Vector2 screenShake,
        float animationTime)
    {
        Vector2 worldCenter = new(WorldX + Size / 2f, WorldY + Size / 2f);
        Vector2 screenCenter = camera.WorldToScreen(
            worldCenter, playerWorldPosition, screenShake);
        DrawAt(spriteBatch, screenCenter, Size, animationTime);
    }

    protected virtual void DrawAt(
        SpriteBatch spriteBatch,
        Vector2 screenCenter,
        float size,
        float animationTime)
    {
        var rect = new Rectangle((int)(screenCenter.X - size / 2f), (int)(screenCenter.Y - size / 2f), (int)size, (int)size);
        Color accent = Tint();

        if (CoreAccent is Color coreAccent)
            DrawCoreAura(spriteBatch, rect, coreAccent, animationTime);

        var shadowRect = new Rectangle(rect.X + 4, (int)(rect.Bottom - rect.Height * 0.18f), rect.Width, (int)(rect.Height * 0.18f));
        Primitives2D.FillEllipse(spriteBatch, shadowRect, UiTheme.Shadow);

        if (ContainsUnique)
            DrawTreasureAura(spriteBatch, rect, animationTime, 1f, UiTheme.Gold);
        else if (BestHighTierRarity is { } highTierRarity)
            DrawTreasureAura(spriteBatch, rect, animationTime,
                highTierRarity == "Mythical" ? .68f : .4f,
                UiTheme.RarityColors.GetValueOrDefault(highTierRarity, UiTheme.Gold));

        int border = Math.Max(2, (int)(size * 0.08f));
        Primitives2D.FillRect(spriteBatch, rect, UiTheme.Ink);
        Primitives2D.RectOutline(spriteBatch, rect, accent, border);

        float seconds = animationTime;
        float activity = (float)GameProfile.Profile.VisualEffectsIntensity;
        float lidLift = MathF.Round(
            (.5f + .5f * MathF.Sin(seconds * 2.3f + WorldX * .01f)) * 2f * activity);
        float lidY = rect.Y + rect.Height * 0.35f - lidLift;
        Primitives2D.Line(spriteBatch, new Vector2(rect.X, lidY), new Vector2(rect.Right, lidY), accent, Math.Max(2, (int)(size * 0.06f)));
        Primitives2D.Line(spriteBatch, new Vector2(rect.Center.X, rect.Y), new Vector2(rect.Center.X, rect.Bottom), accent, Math.Max(1, (int)(size * 0.04f)));
        if (activity > 0)
        {
            float phase = (seconds * .32f + WorldY * .013f) % 1f;
            Vector2 glint = new(
                MathHelper.Lerp(rect.Left + border, rect.Right - border, phase),
                lidY);
            int glintSize = Math.Max(2, (int)(size * .08f));
            Primitives2D.FillRect(spriteBatch,
                new Rectangle((int)glint.X - glintSize / 2,
                    (int)glint.Y - glintSize / 2, glintSize, glintSize),
                UiTheme.Cream * ((.35f + activity * .5f)
                    * VisualAnimation.SeamFade(phase)));
        }
    }

    /// <summary>
    /// Two colored points orbit the chest in a flattened ellipse (matching
    /// the squashed shadow ellipse drawn above), each dragging a fading
    /// trail of shrinking dots behind it -- reads as a beam of light
    /// swirling around the chest rather than a static glow. Originally
    /// Unique-only and hardcoded gold; now shared by Legendary/Mythical
    /// crates too (see LootCrate.BestHighTierRarity), each at their own
    /// `intensity` (orbit reach, trail length, and brightness all scale down
    /// together) and tinted with their own rarity color instead of gold, so
    /// a Legendary crate's aura doesn't get mistaken for a Unique one.
    /// </summary>
    private static void DrawTreasureAura(
        SpriteBatch spriteBatch,
        Rectangle rect,
        float animationTime,
        float intensity,
        Color color)
    {
        const double period = 2.4; // seconds per full orbit
        int trailSegments = Math.Max(4, (int)(16 * intensity));
        const float trailSpan = MathHelper.TwoPi * .5f; // how much of the orbit the fading trail covers

        float headAngle = (float)(animationTime % period / period * MathHelper.TwoPi);
        float orbitRx = rect.Width * .85f * (.75f + .25f * intensity);
        float orbitRy = rect.Height * .5f * (.75f + .25f * intensity);
        var center = new Vector2(rect.Center.X, rect.Center.Y);

        for (int beam = 0; beam < 2; beam++)
        {
            float beamOffset = beam * MathHelper.Pi;
            for (int i = 0; i < trailSegments; i++)
            {
                float t = i / (float)(trailSegments - 1);
                float angle = headAngle + beamOffset - t * trailSpan;
                float alpha = (1f - t) * .85f * intensity;
                float dotRadius = MathHelper.Lerp(3.5f, 1f, t) * (.6f + .4f * intensity);
                var point = center + new Vector2(MathF.Cos(angle) * orbitRx, MathF.Sin(angle) * orbitRy);
                Primitives2D.FillCircle(spriteBatch, point, dotRadius, color * alpha);
            }
        }
    }

    /// <summary>Path-colored glow plus drifting motes for any crate carrying a Core-Forged item.</summary>
    private static void DrawCoreAura(
        SpriteBatch spriteBatch,
        Rectangle rect,
        Color color,
        float animationTime)
    {
        double seconds = animationTime;
        float pulse = .9f + .12f * MathF.Sin((float)seconds * 4f);
        var center = rect.Center.ToVector2();
        for (int layer = 3; layer >= 1; layer--)
        {
            float radius = rect.Width * (.62f + layer * .13f) * pulse;
            Primitives2D.CircleOutline(spriteBatch, center, radius, color * (.16f + (3 - layer) * .08f), layer + 1);
        }

        const int particles = 12;
        for (int index = 0; index < particles; index++)
        {
            float phase = index * MathHelper.TwoPi / particles + (float)seconds * (index % 2 == 0 ? .9f : -.7f);
            float radius = rect.Width * (.75f + .18f * MathF.Sin((float)seconds * 1.7f + index));
            var point = center + new Vector2(MathF.Cos(phase) * radius, MathF.Sin(phase) * radius * .62f);
            float moteSize = 1.5f + (index % 3) * .7f;
            Primitives2D.FillCircle(spriteBatch, point, moteSize, color * (.55f + .3f * MathF.Sin(phase + index)));
        }
    }
}
