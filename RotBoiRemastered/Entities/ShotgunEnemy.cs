using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>Close-range spread-fire wanderer. Ported from enemyTypes.py's ShotgunEnemy.</summary>
public sealed class ShotgunEnemy : WanderingRangedEnemy
{
    public ShotgunEnemy(float worldX, float worldY, float speed, float size, Color color,
        double damage, double hp, double expValue, double difficulty,
        string archetype = "drifter", string difficultyTier = "easy", Random? rng = null)
        : base(worldX, worldY, speed, size, color, damage, hp, expValue, difficulty, archetype, difficultyTier, rng)
    {
        AttackRangeTiles = 8f;
        AttackCooldownMax = Simulation.FrameRate * (2.35f - .22f * (TierRank - 1));
    }

    protected override void Fire(float playerWorldX, float playerWorldY, List<EnemyProjectile> projectileSink)
    {
        float centerX = WorldX + Size / 2f, centerY = WorldY + Size / 2f;
        float baseDirection = MathF.Atan2(playerWorldY - centerY, playerWorldX - centerX);
        MarkAttack(.28f);
        int pelletCount = Rng.Next(4, 8) + 2 * (TierRank - 1);
        float spread = (float)(Rng.NextDouble() * (.78 - .48) + .48);
        for (int index = 0; index < pelletCount; index++)
        {
            float fraction = pelletCount == 1 ? 0f : (float)index / (pelletCount - 1);
            float direction = baseDirection - spread / 2f + spread * fraction + (float)(Rng.NextDouble() * .11 - .055);
            float projectileSize = Size * (float)(Rng.NextDouble() * (.46 - .26) + .26);
            projectileSink.Add(new EnemyProjectile(
                centerX - projectileSize / 2f, centerY - projectileSize / 2f, direction,
                speed: (float)(Rng.NextDouble() * (1.9 - 1.0) + 1.0),
                damage: Damage * (float)(Rng.NextDouble() * (.52 - .28) + .28),
                size: projectileSize,
                travelRange: Simulation.TileSize * (float)(Rng.NextDouble() * (11 - 7) + 7),
                color: UiTheme.Gold));
        }
    }

    public override void Draw(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        base.Draw(spriteBatch, camera, playerWorldPosition, screenShake);
        Vector2 screenPosition = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        var rect = new Rectangle((int)screenPosition.X, (int)screenPosition.Y, (int)Size, (int)Size);
        foreach (float offset in new[] { -.22f, 0f, .22f })
        {
            float y = rect.Center.Y + rect.Height * offset;
            Primitives2D.Line(spriteBatch, new Vector2(rect.X + rect.Width * .25f, y),
                new Vector2(rect.Right - rect.Width * .18f, y), UiTheme.Gold, Math.Max(2, (int)(Size * .07f)));
        }
    }
}
