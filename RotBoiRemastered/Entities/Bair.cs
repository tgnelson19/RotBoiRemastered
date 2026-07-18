using Microsoft.Xna.Framework;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>"THE FIRST LOCK" -- the Touch content path's mid boss. Ported from bossTypes.py's Bair.</summary>
public sealed class Bair : PlagueTouchBoss
{
    public static readonly PathChaseBossConfig BairConfig = BaseConfig with
    {
        BossName = "BAIR", Subtitle = "THE FIRST LOCK", OwnerPrefix = "bair_touch",
        PhaseLabels = new[] { "RIVER", "SWARM", "BLIGHT", "RUIN", "SILENCE" },
        MovementModes = new[] { "chase", "path", "static", "path", "static" },
        MovementSpeed = .10, CooldownSeconds = 2.2,
    };

    public static readonly PlagueSigilConfig BairSigilConfig = new(
        PhaseFlavors: new[]
        {
            "The current carries judgment.", "The small become countless.",
            "The body and field fail together.", "Stone descends; hunger follows.",
            "What remains cannot answer.",
        },
        PhaseColors: new[]
        {
            new Color(137, 48, 45), new Color(76, 135, 80), new Color(126, 104, 61),
            new Color(151, 123, 94), new Color(54, 57, 71),
        },
        PhaseSigils: new[] { 0, 2, 4, 6, 8 });

    public Bair(float worldX, float worldY, Battleground battleground, Random? rng = null)
        : base(worldX, worldY, battleground, BairConfig, BairSigilConfig, rng)
    {
    }

    protected override void FirePlaguePattern(float playerX, float playerY, List<EnemyProjectile> sink)
    {
        switch (Phase)
        {
            case 1: Fan(sink, playerX, playerY, 3, .55f, .45f, 255, "river"); break;
            case 2: Radial(sink, 7, .34f, 245, "swarm"); break;
            case 3: Projectile(sink, 0, 0, 275, "blight", .34f, "bomb", new Vector2(playerX, playerY)); break;
            case 4: Radial(sink, 8, .48f, 270, "ruin"); break;
            default: Fan(sink, playerX, playerY, 5, 1.1f, .38f, 285, "silence"); break;
        }
        PatternRotation += 1;
    }
}
