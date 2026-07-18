using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.World;

/// <summary>
/// New coverage for Progression.cs -- progression.py's enemy_stat_scales/
/// encounter_caps/encounter_pacing aren't directly unit-tested anywhere in
/// the Python original (tests/test_twenty_level_progression.py only imports
/// its constants), so there's nothing to port 1:1 here. These check the
/// formulas at meaningful checkpoints instead.
/// </summary>
public class ProgressionTests
{
    [Fact]
    public void EnemyStatScales_AtLevelZero_IsUnscaled()
    {
        var scales = Progression.EnemyStatScales(0);
        Assert.Equal(1.0, scales.Speed, precision: 6);
        Assert.Equal(1.0, scales.Health, precision: 6);
        Assert.Equal(1.0, scales.Damage, precision: 6);
        Assert.Equal(1.0, scales.Experience, precision: 6);
    }

    [Fact]
    public void EnemyStatScales_PastMidBoss_AddsExtraHealthAndDamageRamp()
    {
        var atMidBoss = Progression.EnemyStatScales(Progression.MidBossLevel);
        var pastMidBoss = Progression.EnemyStatScales(Progression.MidBossLevel + 5);

        // Speed only follows the base curve; health/damage/experience also
        // pick up the late-game ramp once past MidBossLevel.
        Assert.True(pastMidBoss.Health / atMidBoss.Health > pastMidBoss.Speed / atMidBoss.Speed);
        Assert.True(pastMidBoss.Damage / atMidBoss.Damage > pastMidBoss.Speed / atMidBoss.Speed);
    }

    [Fact]
    public void EnemyStatScales_ClampsToMaxLevel()
    {
        var atMax = Progression.EnemyStatScales(Progression.MaxLevel);
        var beyondMax = Progression.EnemyStatScales(Progression.MaxLevel + 50);
        Assert.Equal(atMax, beyondMax);
    }

    [Fact]
    public void EncounterCaps_RiseAfterMidBoss_AndCapAtTenLateLevels()
    {
        var before = Progression.EncounterCaps(Progression.MidBossLevel);
        var after = Progression.EncounterCaps(Progression.MidBossLevel + 10);
        var beyondCap = Progression.EncounterCaps(Progression.MidBossLevel + 50);

        Assert.Equal(50, before.EnemyCap);
        Assert.Equal(60, after.EnemyCap);
        Assert.Equal(after, beyondCap); // late_levels clamps at 10
    }

    [Fact]
    public void EncounterPacing_CuratedChanceStartsAtLevelFive()
    {
        Assert.Equal(0, Progression.EncounterPacing(4).CuratedChance);
        Assert.True(Progression.EncounterPacing(5).CuratedChance > 0);
        Assert.True(Progression.EncounterPacing(Progression.MaxLevel).CuratedChance <= .48);
    }

    [Fact]
    public void EncounterPacing_PatrolSizeGrowsWithLevelUpToNine()
    {
        Assert.Equal(5, Progression.EncounterPacing(1).PatrolSize);
        Assert.Equal(9, Progression.EncounterPacing(Progression.MaxLevel).PatrolSize);
    }
}
