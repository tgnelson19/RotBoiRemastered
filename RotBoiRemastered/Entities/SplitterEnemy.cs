using Microsoft.Xna.Framework;
using RotBoiRemastered.Core;
using RotBoiRemastered.UI;

namespace RotBoiRemastered.Entities;

/// <summary>Fires predictable distance-triggered splitting projectiles. Ported from enemyTypes.py's SplitterEnemy.</summary>
public sealed class SplitterEnemy : WanderingRangedEnemy
{
    public SplitterEnemy(float worldX, float worldY, float speed, float size, Color color,
        double damage, double hp, double expValue, double difficulty,
        string archetype = "drifter", string difficultyTier = "easy", Random? rng = null)
        : base(worldX, worldY, speed, size, color, damage, hp, expValue, difficulty, archetype, difficultyTier, rng)
    {
    }

    protected override void Fire(float playerWorldX, float playerWorldY, List<EnemyProjectile> projectileSink)
    {
        float centerX = WorldX + Size / 2f, centerY = WorldY + Size / 2f;
        var shot = new EnemyProjectile(
            centerX, centerY, MathF.Atan2(playerWorldY - centerY, playerWorldX - centerX),
            .82f + .08f * TierRank, Damage * .72f, Size * .38f,
            travelRange: Simulation.TileSize * 18f, color: UiTheme.Purple,
            shape: "diamond", owner: $"splitter_{DifficultyTier}");
        shot.SplitCount = TierRank + 1;
        shot.SplitAt = Simulation.TileSize * (5.5f - .5f * TierRank);
        shot.SplitGeneration = TierRank == 3 ? 1 : 0;
        projectileSink.Add(shot);
        MarkAttack(.32f);
    }
}
