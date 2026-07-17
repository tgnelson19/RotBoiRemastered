using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// Kite-and-fire base for the ranged enemy family (Shotgun/Volley/Laser/Bomb
/// all derive from this). Ported from enemyTypes.py's WanderingRangedEnemy.
///
/// Cleanup vs. the Python original: updateEnemy decremented `wanderTimer`
/// itself AND called `_wander(.2)` (inherited from Enemy), which internally
/// decrements the same timer again -- a harmless but pointless double-decrement
/// whenever the enemy is disengaged. Dropped the redundant top-level
/// decrement here (and in VolleyEnemy/BombEnemy, which copy the same
/// preamble) since Wander() already owns that timer's decay.
/// </summary>
public class WanderingRangedEnemy : Enemy
{
    protected float AttackRangeTiles { get; set; } = 10f;
    protected readonly Random Rng;

    public WanderingRangedEnemy(float worldX, float worldY, float speed, float size, Color color,
        double damage, double hp, double expValue, double difficulty,
        string archetype = "drifter", string difficultyTier = "easy", Random? rng = null)
        : base(worldX, worldY, speed, size, color, damage, hp, expValue, difficulty,
            awarenessRange: 17f * Simulation.TileSize, archetype: archetype, difficultyTier: difficultyTier, rng: rng)
    {
        Rng = rng ?? Random.Shared;
        WanderAngle = (float)(Rng.NextDouble() * 2 * Math.PI - Math.PI);
        WanderTimer = Rng.Next(45, 111);
        AttackCooldown = Rng.Next(30, 91);
        AttackCooldownMax = Simulation.FrameRate * (1.45f - .16f * (TierRank - 1));
    }

    protected void Move(float directionX, float directionY, float speedMultiplier, Battleground battleground)
    {
        float step = Speed * speedMultiplier * (float)Simulation.GetFrameScale();
        TryAxisMove(directionX * step, "x", battleground);
        TryAxisMove(directionY * step, "y", battleground);
    }

    protected virtual void Fire(float playerWorldX, float playerWorldY, List<EnemyProjectile> projectileSink)
    {
        float centerX = WorldX + Size / 2f, centerY = WorldY + Size / 2f;
        float direction = MathF.Atan2(playerWorldY - centerY, playerWorldX - centerX);
        float projectileSize = Math.Max(12f, Size * .4f);
        MarkAttack();
        int count = TierRank;
        for (int index = 0; index < count; index++)
        {
            float offset = (index - (count - 1) / 2f) * .13f;
            projectileSink.Add(new EnemyProjectile(
                centerX - projectileSize / 2f, centerY - projectileSize / 2f, direction + offset,
                speed: 1.4f + .08f * (TierRank - 1), damage: Damage * (.72f / count), size: projectileSize,
                travelRange: Simulation.TileSize * (16 + TierRank), color: UiTheme.Red, shape: "diamond"));
        }
    }

    public override void Update(EnemyUpdateContext context)
    {
        AdvanceAge();
        AttackCooldown -= (float)Simulation.GetTimerStep();
        var battleground = context.Battleground;

        var (directionX, directionY, distance) = EnemyCatalogData.Normalise(
            context.PlayerWorldX - (WorldX + Size / 2f), context.PlayerWorldY - (WorldY + Size / 2f));

        float attackRange = Simulation.TileSize * AttackRangeTiles;
        if (UpdateAwareness(distance))
        {
            if (CombatRole == "support" && EncounterCombatTarget.HasValue)
            {
                var (guardX, guardY, guardDistance) = EnemyCatalogData.Normalise(
                    EncounterCombatTarget.Value.X - (WorldX + Size / 2f), EncounterCombatTarget.Value.Y - (WorldY + Size / 2f));
                if (guardDistance > Simulation.TileSize * .6f)
                    Move(guardX, guardY, .38f, battleground);
            }
            float preferredMin = attackRange * (CombatRole == "artillery" ? .52f : .38f);
            if (distance < preferredMin)
                Move(-directionX, -directionY, .34f, battleground);
            else if (distance <= attackRange)
            {
                int side = CombatSide == 0 ? 1 : CombatSide;
                Move(-directionY * side, directionX * side, .18f, battleground);
            }
            else
            {
                Move(directionX, directionY, .48f, battleground);
            }
            if (distance <= attackRange && AttackCooldown <= 0)
            {
                Fire(context.PlayerWorldX, context.PlayerWorldY, context.ProjectileSink);
                AttackCooldown = AttackCooldownMax * (float)(Rng.NextDouble() * (1.2 - .85) + .85);
            }
        }
        else
        {
            Wander(battleground, .2f);
        }
        FinishMovementTracking();
    }

    public override void Draw(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        base.Draw(spriteBatch, camera, playerWorldPosition, screenShake);
        Vector2 screenPosition = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        var rect = new Rectangle((int)screenPosition.X, (int)screenPosition.Y, (int)Size, (int)Size);
        var inkRect = rect;
        inkRect.Inflate(-(int)(Size * .35f), -(int)(Size * .35f));
        Primitives2D.FillRect(spriteBatch, inkRect, UiTheme.Ink);
        var redRect = rect;
        redRect.Inflate(-(int)(Size * .58f), -(int)(Size * .58f));
        Primitives2D.FillRect(spriteBatch, redRect, UiTheme.Red);
    }
}
