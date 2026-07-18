using Microsoft.Xna.Framework;
using RotBoiRemastered.Core;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>"THE THING THE PRISON KEPT" -- the Touch content path's final boss. Ported from bossTypes.py's Sting.</summary>
public sealed class Sting : PlagueTouchBoss
{
    public static readonly PathChaseBossConfig StingConfig = BaseConfig with
    {
        BossName = "STING", Subtitle = "THE THING THE PRISON KEPT", FinalBoss = true,
        OwnerPrefix = "sting_touch",
        PhaseLabels = new[] { "BLOOD", "FROGS", "GNATS", "FLIES", "PESTILENCE", "BOILS", "HAIL", "LOCUSTS", "DARKNESS", "FIRSTBORN" },
        MovementModes = new[] { "chase", "path", "static", "path", "static", "chase", "static", "path", "static", "chase" },
        MovementSpeed = .075, FinalCooldownSeconds = 1.7, FinalBodyScale = 2.65,
    };

    // phaseFlavors in Python is computed as `name.title()` over PLAGUE_SIGILS's own names.
    public static readonly PlagueSigilConfig StingSigilConfig = new(
        PhaseFlavors: new[]
        {
            "Corruption", "Overrun", "Infestation", "Invasion", "Pestilence",
            "Affliction", "Impact", "Devour", "Darkness", "Severance",
        },
        PhaseColors: new[]
        {
            new Color(142, 38, 43), new Color(69, 139, 75), new Color(112, 91, 57), new Color(91, 76, 53), new Color(93, 122, 67),
            new Color(166, 71, 61), new Color(137, 169, 194), new Color(117, 139, 58), new Color(45, 46, 61), new Color(189, 163, 119),
        },
        PhaseSigils: Enumerable.Range(0, 10).ToArray());

    public Sting(float worldX, float worldY, Battleground battleground, Random? rng = null)
        : base(worldX, worldY, battleground, StingConfig, StingSigilConfig, rng)
    {
    }

    protected override void FirePlaguePattern(float playerX, float playerY, List<EnemyProjectile> sink)
    {
        switch (Phase)
        {
            case 1:
                Radial(sink, 10, .3f, 330, "blood");
                break;
            case 2:
                foreach (int offset in new[] { -1, 0, 1 })
                    Projectile(sink, 0, 0, 340, "frogs", .31f, "bomb", new Vector2(playerX + offset * Simulation.TileSize * 1.4f, playerY));
                break;
            case 3:
                Radial(sink, 16, .52f, 305, "gnats");
                break;
            case 4:
                Fan(sink, playerX, playerY, 7, 1.6f, .5f, 325, "flies");
                break;
            case 5:
                Radial(sink, 12, .36f, 350, "pestilence");
                break;
            case 6:
                Projectile(sink, 0, 0, 380, "boils", .4f, "bomb", new Vector2(playerX, playerY));
                break;
            case 7:
                for (int offset = -2; offset <= 2; offset++)
                    Projectile(sink, MathF.PI / 2f + offset * .13f, .58f, 365, "hail", .28f);
                break;
            case 8:
                Fan(sink, playerX, playerY, 11, 2.1f, .48f, 340, "locusts");
                break;
            case 9:
                Fan(sink, playerX, playerY, 5, .72f, .28f, 390, "darkness");
                break;
            default:
                Radial(sink, 10, .48f, 380, "severance");
                Fan(sink, playerX, playerY, 3, .26f, .72f, 400, "firstborn");
                break;
        }
        PatternRotation += 1;
    }
}
