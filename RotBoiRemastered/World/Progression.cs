namespace RotBoiRemastered.World;

/// <summary>Twenty-level run pacing shared by encounters, enemies, and rewards. Ported from progression.py.</summary>
public readonly record struct EnemyStatScales(double Speed, double Health, double Damage, double Experience);

public readonly record struct EncounterCaps(int EnemyCap, double ThreatCap, double PopulationThreatCap);

public readonly record struct EncounterPacing(
    int PatrolSize, int MaxWorldEncounters, double SpawnIntervalSeconds, double CuratedChance);

public static class Progression
{
    public const int MidBossLevel = 10;
    public const int FinalBossLevel = 20;
    public const int MaxLevel = FinalBossLevel;

    /// <summary>One world miniboss introduces phase attacks in each half of the run.</summary>
    public static readonly IReadOnlyList<(int Level, string Key)> MinibossGates = new[]
    {
        (5, "miniboss_arsenal"),
        (15, "miniboss_siege"),
    };

    private static double LateProgress(int level) => Math.Clamp((level - MidBossLevel) / 10.0, 0.0, 1.0);

    /// <summary>
    /// Stretch the old ten-level 1.08 curve across twenty levels. The back
    /// half adds a controlled health/damage ramp so upgraded builds still
    /// meet resistance without making movement speeds or early enemies
    /// oppressive. Experience rises alongside that extra durability.
    /// </summary>
    public static EnemyStatScales EnemyStatScales(int level)
    {
        level = Math.Clamp(level, 0, MaxLevel);
        double stretched = Math.Pow(1.08, level / 2.0);
        double late = LateProgress(level);
        return new EnemyStatScales(
            Speed: stretched,
            Health: stretched * (1.0 + .22 * late),
            Damage: stretched * (1.0 + .12 * late),
            Experience: stretched * (1.0 + .35 * late));
    }

    /// <summary>Raise simultaneous pressure after Beaudis instead of only inflating HP.</summary>
    public static EncounterCaps EncounterCaps(int level)
    {
        int lateLevels = Math.Clamp(level - MidBossLevel, 0, 10);
        return new EncounterCaps(
            EnemyCap: 50 + lateLevels,
            ThreatCap: 36.0 + lateLevels * 1.2,
            PopulationThreatCap: 60.0 + lateLevels * 1.8);
    }

    /// <summary>Shape discrete fights rather than continuously accumulating loose enemies.</summary>
    public static EncounterPacing EncounterPacing(int level)
    {
        level = Math.Clamp(level, 0, MaxLevel);
        return new EncounterPacing(
            // Patrols begin as real encounters rather than pairs, then gain one
            // body every four levels until the final approach to Dissonance.
            PatrolSize: Math.Min(9, 5 + Math.Max(0, level - 1) / 4),
            // Larger patrols need fewer simultaneous map slots to preserve
            // recovery space and keep the physical cap meaningful.
            MaxWorldEncounters: Math.Min(5, 3 + level / 8),
            SpawnIntervalSeconds: Math.Max(4.4, 7.0 - level * .13),
            CuratedChance: level < 5 ? 0 : Math.Min(.48, .28 + (level - 5) * .012));
    }
}
