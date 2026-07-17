using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// Telegraphed beam family: aimed, sweeping, then sector-controlling.
/// Ported from enemyTypes.py's LaserEnemy. Reuses WanderingRangedEnemy's
/// Update as-is (only Fire/Draw are overridden), same as the Python original.
/// </summary>
public sealed class LaserEnemy : WanderingRangedEnemy
{
    // (count, telegraph, lifetime, angularSpeed, cooldown)
    private static readonly IReadOnlyDictionary<string, (int Count, float Telegraph, float Lifetime, float AngularSpeed, float Cooldown)> TierSettings =
        new Dictionary<string, (int, float, float, float, float)>
        {
            ["small"] = (1, .8f, 1.55f, 0f, 2.5f),
            ["medium"] = (1, 1.0f, 2.5f, .16f, 3.5f),
            ["large"] = (2, 1.25f, 3.0f, .1f, 5.0f),
        };

    public string Tier { get; }

    public LaserEnemy(float worldX, float worldY, float speed, float size, Color color,
        double damage, double hp, double expValue, double difficulty,
        string tier = "small", string archetype = "drifter", string difficultyTier = "easy", Random? rng = null)
        : base(worldX, worldY, speed, size, color, damage, hp, expValue, difficulty, archetype, difficultyTier, rng)
    {
        Tier = tier;
        AttackCooldownMax = Simulation.FrameRate * TierSettings[tier].Cooldown;
        AttackRangeTiles = 15f;
    }

    protected override void Fire(float playerWorldX, float playerWorldY, List<EnemyProjectile> projectileSink)
    {
        var (count, telegraph, lifetime, angularSpeed, _) = TierSettings[Tier];
        float centerX = WorldX + Size / 2f, centerY = WorldY + Size / 2f;
        float baseDirection = MathF.Atan2(playerWorldY - centerY, playerWorldX - centerX);
        MarkAttack(.34f);
        for (int index = 0; index < count; index++)
        {
            float direction = baseDirection + index * MathF.PI;
            var laser = new EnemyProjectile(
                centerX, centerY, direction, speed: 0,
                damage: Damage * (count == 1 ? .7f : .48f),
                size: Size * (Tier != "large" ? .16f : .2f),
                travelRange: Simulation.TileSize * 17f, color: UiTheme.Red,
                path: "laser", lifetime: lifetime, angularSpeed: angularSpeed,
                owner: $"laser_{Tier}", ignoreWalls: true);
            laser.TelegraphDuration = telegraph;
            projectileSink.Add(laser);
        }
    }

    public override void Draw(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        base.Draw(spriteBatch, camera, playerWorldPosition, screenShake);
        Vector2 screenPosition = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        var rect = new Rectangle((int)screenPosition.X, (int)screenPosition.Y, (int)Size, (int)Size);
        var center = new Vector2(rect.Center.X, rect.Center.Y);
        Primitives2D.CircleOutline(spriteBatch, center, Math.Max(4, (int)(Size * .2f)), UiTheme.Red, 3);
        Primitives2D.Line(spriteBatch, new Vector2(rect.Left, rect.Center.Y), new Vector2(rect.Right, rect.Center.Y), UiTheme.Cream, 2);
    }
}
