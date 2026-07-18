using RotBoiRemastered.Systems;

namespace RotBoiRemastered.Tests.Systems;

/// <summary>
/// Ported from characterStats.py's default/reset values and
/// combarinoPlayerStats' stat-combination math.
///
/// RunState.Reset() calls MetaProgression.ApplySkills(this), which reads
/// GameProfile.Profile.SkillLevels -- and GameProfile.Profile is process-wide
/// static state seeded from whatever the developer's real %APPDATA% save
/// happens to contain. Every test here asserts exact stat values assuming no
/// purchased skills are in play, so this needs the same reset-to-defaults
/// isolation as MetaProgressionTests/UiThemeTests, or these go flaky/fail
/// outright the moment a live save has actually purchased a skill.
/// </summary>
[Collection("GameProfileState")]
public class RunStateTests : IDisposable
{
    private readonly GameProfileData _originalProfile = GameProfile.Profile;

    public RunStateTests() => GameProfile.Profile = new GameProfileData();

    public void Dispose() => GameProfile.Profile = _originalProfile;

    [Fact]
    public void Reset_EstablishesExpectedDefaults()
    {
        var state = new RunState();
        Assert.Equal(1000, state.HealthPoints);
        Assert.Equal(1000, state.MaxHealthPoints);
        Assert.Equal(0, state.CurrentLevel);
        Assert.Equal(40, state.ExpNeededForNextLevel);
        Assert.Empty(state.EnemyHolster);
        Assert.Empty(state.BulletHolster);
        Assert.Equal(5, state.Equipment.Count);
        Assert.All(state.Equipment.Values, Assert.Null);
        Assert.Equal(8, state.Inventory.Count);
        Assert.All(state.Inventory, Assert.Null);
    }

    [Fact]
    public void CombinePlayerStats_CombinesBaseAdditiveAndMultiplicativeStacks()
    {
        var state = new RunState();
        state.Stats["Bullet Damage"].Additive.Add(20);
        state.Stats["Bullet Damage"].Multiplicative.Add(1.5);

        state.CombinePlayerStats();

        // (100 base + 20 additive) * 1.5 multiplicative = 180
        Assert.Equal(180, state.BulletDamage);
    }

    [Fact]
    public void CombinePlayerStats_HealsByExactMaxHealthIncrease_WithoutOverfilling()
    {
        var state = new RunState();
        state.HealthPoints = 500; // simulate prior damage
        state.Stats["Health"].Additive.Add(200);

        state.CombinePlayerStats();

        Assert.Equal(1200, state.MaxHealthPoints);
        Assert.Equal(700, state.HealthPoints); // 500 + (1200-1000) increase, not fully refilled
    }

    [Fact]
    public void CombinePlayerStats_ClampsAttackCooldownToReadableFloor()
    {
        var state = new RunState();
        state.Stats["Attack Speed"].Additive.Add(-1000);

        state.CombinePlayerStats();

        Assert.Equal(5, state.AttackCooldownStat);
    }

    [Fact]
    public void RecordUpgrade_TracksTypeAndRarityCounts_AndHistory()
    {
        var state = new RunState();
        state.RecordUpgrade("Defense", "Common", "additive");
        state.RecordUpgrade("Defense", "Rare", "multiplicative");

        Assert.Equal(2, state.UpgradeTypeCounts["Defense"]);
        Assert.Equal(1, state.UpgradeRarityCounts["Common"]);
        Assert.Equal(1, state.UpgradeRarityCounts["Rare"]);
        Assert.Equal(2, state.UpgradeHistory.Count);
    }

    [Fact]
    public void RecoverHealth_AccumulatesFractionalVitality_AndStopsAtMax()
    {
        var state = new RunState();
        state.HealthPoints = 999;
        state.RecoverHealth();
        Assert.True(state.HealthPoints <= state.MaxHealthPoints);

        state.HealthPoints = state.MaxHealthPoints;
        state.RecoverHealth();
        Assert.Equal(state.MaxHealthPoints, state.HealthPoints);
        Assert.Equal(0.0, state.HealthRecoveryBuffer);
    }

    [Fact]
    public void DreamState_AlterBelief_ClampsBetweenZeroAndTen()
    {
        var dream = new DreamState();
        dream.AlterBelief(50);
        Assert.Equal(10.0, dream.Belief);
        dream.AlterBelief(-100);
        Assert.Equal(0.0, dream.Belief);
    }

    [Fact]
    public void BossAfflictions_Apply_Slow_ReducesMovementMultiplier()
    {
        var afflictions = new BossAfflictions();
        double before = afflictions.MovementMultiplier();
        afflictions.Apply("slow", duration: 5.0, strength: .3);
        Assert.True(afflictions.MovementMultiplier() < before);
    }

    [Fact]
    public void BossAfflictions_Update_ClearsSlowAfterDurationElapses()
    {
        var afflictions = new BossAfflictions();
        afflictions.Apply("slow", duration: 1.0, strength: .3);
        afflictions.Update(2.0);
        Assert.Equal(1.0, afflictions.MovementMultiplier());
    }
}
