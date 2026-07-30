using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// Carries a directional shield that can protect nearby allies. Ported from
/// enemyTypes.py's WarderEnemy. `ShieldHp` stays a double, not an int --
/// Python's `self.shieldHp -= amount` (in take_damage) never rounds, so the
/// value becomes fractional after the first hit; an int field would silently
/// truncate that.
/// </summary>
public sealed class WarderEnemy : WanderingRangedEnemy
{
    public float ShieldAngle { get; private set; }
    public double ShieldHp { get; private set; }
    public double MaxShieldHp { get; }

    public WarderEnemy(float worldX, float worldY, float speed, float size, Color color,
        double damage, double hp, double expValue, double difficulty,
        string archetype = "drifter", string difficultyTier = "easy", Random? rng = null)
        : base(worldX, worldY, speed, size, color, damage, hp, expValue, difficulty, archetype, difficultyTier, rng)
    {
        ShieldHp = Math.Round(MaxHp * (.35 + .2 * TierRank));
        MaxShieldHp = ShieldHp;
    }

    public override void Update(EnemyUpdateContext context)
    {
        ShieldAngle = MathF.Atan2(context.PlayerWorldY - (WorldY + Size / 2f), context.PlayerWorldX - (WorldX + Size / 2f));
        base.Update(context);
    }

    private Rectangle ShieldRect(float x, float y)
    {
        float centerX = x + Size / 2f, centerY = y + Size / 2f;
        float shieldSize = Size * (1.15f + .55f * (TierRank - 1));
        float shieldX = centerX + MathF.Cos(ShieldAngle) * Size * .58f;
        float shieldY = centerY + MathF.Sin(ShieldAngle) * Size * .58f;
        return new Rectangle((int)(shieldX - shieldSize * .15f), (int)(shieldY - shieldSize / 2f),
            (int)(shieldSize * .3f), (int)shieldSize);
    }

    private Rectangle ShieldWorldRect() => ShieldRect(WorldX, WorldY);

    private Rectangle ShieldScreenRect(Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        Vector2 screenPosition = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        return ShieldRect(screenPosition.X, screenPosition.Y);
    }

    public override IReadOnlyList<(string Part, Rectangle Rect)> GetWorldHitboxes()
    {
        var hitboxes = base.GetWorldHitboxes();
        if (ShieldHp <= 0)
            return hitboxes;
        var withShield = new List<(string, Rectangle)> { ("shield", ShieldWorldRect()) };
        withShield.AddRange(hitboxes);
        return withShield;
    }

    public override IReadOnlyList<(string Part, Rectangle Rect)> GetScreenHitboxes(
        Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        var hitboxes = base.GetScreenHitboxes(camera, playerWorldPosition, screenShake);
        if (ShieldHp <= 0)
            return hitboxes;
        var withShield = new List<(string, Rectangle)> { ("shield", ShieldScreenRect(camera, playerWorldPosition, screenShake)) };
        withShield.AddRange(hitboxes);
        return withShield;
    }

    public override HitResult TakeDamage(double amount, string partId = "body", DamageSource source = DamageSource.Direct)
    {
        if (partId == "shield" && ShieldHp > 0)
        {
            ShieldHp -= amount;
            return new HitResult(true, false, amount, true);
        }
        return base.TakeDamage(amount, partId, source);
    }

    public override void Draw(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        base.Draw(spriteBatch, camera, playerWorldPosition, screenShake);
        if (ShieldHp > 0)
        {
            var shield = ShieldScreenRect(camera, playerWorldPosition, screenShake);
            EnemyRenderPose pose = RenderPose(camera, playerWorldPosition, screenShake);
            Vector2 raw = camera.WorldToScreen(
                new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
            shield.Offset(pose.Rect.X - (int)raw.X, pose.Rect.Y - (int)raw.Y);
            var inked = shield;
            inked.Inflate(5, 5);
            Primitives2D.FillRect(spriteBatch, inked, UiTheme.Ink);
            Primitives2D.FillRect(spriteBatch, shield, UiTheme.Blue);
            Primitives2D.RectOutline(spriteBatch, shield, UiTheme.Cream, 2);
            float ratio = (float)Math.Clamp(ShieldHp / Math.Max(1, MaxShieldHp), 0, 1);
            int cracks = ratio < .67f ? ratio < .34f ? 3 : 2 : 0;
            for (int crack = 0; crack < cracks; crack++)
            {
                float y = shield.Top + shield.Height * (crack + 1f) / (cracks + 1f);
                Primitives2D.Line(spriteBatch,
                    new Vector2(shield.Left, y),
                    new Vector2(shield.Right, y + (crack % 2 == 0 ? 5 : -5)),
                    UiTheme.Ink, 2);
            }
            if (VisualHitTimer > 0)
            {
                var ripple = shield;
                ripple.Inflate(6, 8);
                Primitives2D.RectOutline(spriteBatch, ripple, UiTheme.Cream, 2);
            }
        }
    }
}
