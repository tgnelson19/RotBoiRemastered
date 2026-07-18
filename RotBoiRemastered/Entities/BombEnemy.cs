using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// Space-control family with honest fuse and blast-radius telegraphs.
/// Ported from enemyTypes.py's BombEnemy. Overrides Update completely (its
/// retreat-after-throwing behavior doesn't match the shared kite bands),
/// same as the Python original's full updateEnemy override.
/// </summary>
public sealed class BombEnemy : WanderingRangedEnemy
{
    // (count, fuse, radiusTiles, cooldown)
    private static readonly IReadOnlyDictionary<string, (int Count, float Fuse, float RadiusTiles, float Cooldown)> TierSettings =
        new Dictionary<string, (int, float, float, float)>
        {
            ["small"] = (1, 2.3f, 1.25f, 3.0f),
            ["medium"] = (1, 2.6f, 1.75f, 3.8f),
            ["large"] = (3, 3.0f, 2.1f, 5.2f),
        };

    public string Tier { get; }
    private float _retreatRemaining;

    public BombEnemy(float worldX, float worldY, float speed, float size, Color color,
        double damage, double hp, double expValue, double difficulty,
        string tier = "small", string archetype = "drifter", string difficultyTier = "easy", Random? rng = null)
        : base(worldX, worldY, speed, size, color, damage, hp, expValue, difficulty, archetype, difficultyTier, rng)
    {
        Tier = tier;
        AttackRangeTiles = tier == "small" ? 9f : 13f;
        AttackCooldownMax = Simulation.FrameRate * TierSettings[tier].Cooldown;
    }

    protected override void Fire(float playerWorldX, float playerWorldY, List<EnemyProjectile> projectileSink)
    {
        var (count, fuse, radiusTiles, _) = TierSettings[Tier];
        float centerX = WorldX + Size / 2f, centerY = WorldY + Size / 2f;
        MarkAttack(.3f);
        for (int index = 0; index < count; index++)
        {
            float offsetAngle = index * 2f * MathF.PI / Math.Max(1, count);
            float offset = count == 1 ? 0f : Simulation.TileSize * 2.4f;
            var target = new Vector2(playerWorldX + MathF.Cos(offsetAngle) * offset, playerWorldY + MathF.Sin(offsetAngle) * offset);
            var bomb = new EnemyProjectile(
                centerX, centerY, 0, speed: 0,
                damage: Damage * (count == 1 ? .68f : .42f),
                size: Size * .38f, travelRange: Simulation.TileSize * 30f,
                color: UiTheme.Gold, shape: "bomb", path: "bomb", target: target,
                owner: $"bomb_{Tier}", ignoreWalls: true);
            bomb.FuseDuration = fuse;
            bomb.BlastRadius = Simulation.TileSize * radiusTiles;
            bomb.BurstCount = Tier == "small" ? 4 : Tier == "medium" ? 6 : 8;
            projectileSink.Add(bomb);
        }
        _retreatRemaining = Simulation.FrameRate * 1.2f;
    }

    public override void Update(EnemyUpdateContext context)
    {
        AdvanceAge();
        AttackCooldown -= (float)Simulation.GetTimerStep();
        _retreatRemaining = Math.Max(0f, _retreatRemaining - (float)Simulation.GetTimerStep());
        var battleground = context.Battleground;
        var (directionX, directionY, distance) = EnemyCatalogData.Normalise(
            context.PlayerWorldX - (WorldX + Size / 2f), context.PlayerWorldY - (WorldY + Size / 2f));

        if (!UpdateAwareness(distance))
        {
            Wander(battleground, .2f);
            FinishMovementTracking();
            return;
        }
        float attackRange = Simulation.TileSize * AttackRangeTiles;
        if (_retreatRemaining > 0)
            Move(-directionX, -directionY, .42f, battleground);
        else
            Move(directionX, directionY, distance <= attackRange ? .15f : .42f, battleground);

        if (distance <= attackRange && AttackCooldown <= 0)
        {
            Fire(context.PlayerWorldX, context.PlayerWorldY, context.ProjectileSink);
            AttackCooldown = AttackCooldownMax * (float)(Rng.NextDouble() * (1.15 - .9) + .9);
        }
        FinishMovementTracking();
    }

    public override void Draw(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        base.Draw(spriteBatch, camera, playerWorldPosition, screenShake);
        Vector2 screenPosition = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        var rect = new Rectangle((int)screenPosition.X, (int)screenPosition.Y, (int)Size, (int)Size);
        var center = new Vector2(rect.Center.X, rect.Center.Y);
        Primitives2D.FillCircle(spriteBatch, center, Math.Max(4, (int)(Size * .22f)), UiTheme.Gold);
        Primitives2D.Line(spriteBatch, center, new Vector2(rect.Center.X, rect.Top), UiTheme.Cream, 3);
    }
}
