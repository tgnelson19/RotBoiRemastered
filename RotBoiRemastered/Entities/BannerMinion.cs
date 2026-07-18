using Microsoft.Xna.Framework;
using RotBoiRemastered.Core;

namespace RotBoiRemastered.Entities;

/// <summary>
/// Cheap squad body that holds formation around its Captain, then chases
/// the player directly (with permanently compounding speed) once the
/// Captain falls. Ported from enemyTypes.py's BannerMinion.
/// </summary>
public sealed class BannerMinion : Enemy
{
    public Enemy? Leader { get; set; }
    public float FormationAngle { get; set; }

    public BannerMinion(float worldX, float worldY, float speed, float size, Color color, double damage, double hp,
        double expValue, double difficulty, float awarenessRange, Enemy? leader = null, float formationAngle = 0f,
        string archetype = "runner", string difficultyTier = "easy", Random? rng = null)
        : base(worldX, worldY, speed, size, color, damage, hp, expValue, difficulty, awarenessRange, archetype, difficultyTier, rng)
    {
        Leader = leader;
        FormationAngle = formationAngle;
        ThreatCost = .55;
    }

    public override void Update(EnemyUpdateContext context)
    {
        if (Leader is not null && !Leader.IsDead())
        {
            float radius = Leader.Size * (1.0f + .18f * TierRank);
            float targetX = Leader.WorldX + Leader.Size / 2f + MathF.Cos(FormationAngle) * radius;
            float targetY = Leader.WorldY + Leader.Size / 2f + MathF.Sin(FormationAngle) * radius;
            var (directionX, directionY, distance) = EnemyCatalogData.Normalise(
                targetX - (WorldX + Size / 2f), targetY - (WorldY + Size / 2f));
            if (distance > Size * .4f)
            {
                float step = Speed * .72f * (float)Simulation.GetFrameScale();
                TryAxisMove(directionX * step, "x", context.Battleground);
                TryAxisMove(directionY * step, "y", context.Battleground);
            }
            AdvanceAge();
            FinishMovementTracking();
            return;
        }
        Leader = null;
        Speed *= 1.0015f;
        base.Update(context);
    }
}
