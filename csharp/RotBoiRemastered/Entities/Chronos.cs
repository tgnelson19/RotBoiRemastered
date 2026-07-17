using Microsoft.Xna.Framework;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// "THE LAST SECOND" -- Ishe's final-boss counterpart. Ported from
/// bossTypes.py's Chronos, which only overrode the final-boss color/scale/
/// cooldown/shot fields (everything else inherited from Ishe). The `with`
/// expression against <see cref="Ishe.IsheConfig"/> mirrors that partial
/// class-attribute override exactly.
/// </summary>
public sealed class Chronos : Ishe
{
    public static readonly PathChaseBossConfig ChronosConfig = IsheConfig with
    {
        BossName = "CHRONOS", Subtitle = "THE LAST SECOND", FinalBoss = true,
        OwnerPrefix = "chronos_sight",
        FinalBodyColor = new Color(81, 164, 204), FinalAccentColor = new Color(244, 166, 73),
        FinalBodyScale = 1.7, FinalCooldownSeconds = .92, FinalShotSpeed = 2.15, FinalShotScale = .22,
    };

    public Chronos(float worldX, float worldY, Battleground battleground, Random? rng = null)
        : base(worldX, worldY, battleground, ChronosConfig, rng)
    {
    }
}
