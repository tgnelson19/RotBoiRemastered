using RotBoiRemastered.Entities;

namespace RotBoiRemastered.Tests.Entities;

/// <summary>Ported from bossTypes.py's Beaudis (no dedicated Python test file to mirror -- tests/test_beaudis_boss.py is almost entirely about Dissonance; see Entities/README.md).</summary>
public class BeaudisTests
{
    private static Beaudis MakeBoss() => new(100, 100, awarenessRange: 500f, rng: new Random(1));

    private static EnemyUpdateContext MakeContext(Beaudis boss, float playerX, float playerY) => new()
    {
        PlayerWorldX = playerX, PlayerWorldY = playerY, Battleground = EntityTestFixtures.SmallOpenRoom(),
    };

    [Fact]
    public void Constructor_SetsBossIdentityAndInitialPhase()
    {
        var boss = MakeBoss();
        Assert.Equal(26000, boss.Hp);
        Assert.Equal(26000, boss.MaxHp);
        Assert.Equal(1, boss.Phase);
        Assert.Equal("AWAKEN", boss.PhaseLabel);
    }

    [Fact]
    public void TakeDamage_BuildsStagger_WithoutReachingThreshold()
    {
        var boss = MakeBoss();
        var result = boss.TakeDamage(2);
        Assert.True(result.Applied);
        Assert.Equal(2, result.Amount);
        Assert.Equal(26000 - 2, boss.Hp);
        Assert.Equal(4.0, boss.Stagger); // max(minimumStaggerPerHit=4, 2*.014=.028) == 4
        Assert.False(boss.IsStaggered);
    }

    [Fact]
    public void TakeDamage_ReachingMaxStagger_TriggersStaggerState()
    {
        var boss = MakeBoss();
        for (int i = 0; i < 22; i++) // 22 * 4 == 88 < 90; one more crosses 90
            boss.TakeDamage(1);
        Assert.False(boss.IsStaggered);
        boss.TakeDamage(1);
        Assert.True(boss.IsStaggered);
        Assert.True(boss.TransitionCleanupRequested);
        Assert.Null(boss.TransitionCleanupOwner); // Beaudis's isolated encounter clears every enemy projectile, not one owner
    }

    [Fact]
    public void TakeDamage_WhileStaggered_AppliesBonusMultiplier()
    {
        var boss = MakeBoss();
        for (int i = 0; i < 23; i++)
            boss.TakeDamage(1);
        Assert.True(boss.IsStaggered);
        int hpBefore = boss.Hp;

        var result = boss.TakeDamage(10);

        Assert.Equal(12, result.Amount); // round(10 * 1.25) == round(12.5) -> 12 (banker's rounding, matches Python's round())
        Assert.Equal(hpBefore - 12, boss.Hp);
    }

    [Fact]
    public void TakeDamage_DuringPhaseProtectionWindow_IsBlocked()
    {
        var boss = MakeBoss();
        boss.DebugSetPhase(2); // sets phaseProtectionTimer = .55
        int hpBefore = boss.Hp;

        var result = boss.TakeDamage(1000);

        Assert.True(result.Blocked);
        Assert.False(result.Applied);
        Assert.Equal(hpBefore, boss.Hp);
    }

    [Fact]
    public void TakeDamage_CrossingSurvivalThreshold_EntersSurvivalPhaseFive()
    {
        var boss = MakeBoss();
        // First hit lands mid-phase-protection-free; drop HP straight to the first survival gate (2/3 of 26000).
        var result = boss.TakeDamage(26000 - (int)(26000 * (2.0 / 3)) + 1);
        Assert.True(result.Applied);
        Assert.Equal(5, boss.Phase);
        Assert.True(boss.SurvivalActive);
        Assert.Equal(SurvivalHealthFor(26000, 2.0 / 3), boss.Hp);
    }

    private static int SurvivalHealthFor(int maxHp, double ratio) => Math.Max(1, (int)Math.Round(maxHp * ratio));

    [Fact]
    public void DebugSetPhase_ToFive_ActivatesSurvivalWithFinalePortals()
    {
        var boss = MakeBoss();
        boss.DebugSetPhase(5);
        Assert.Equal(5, boss.Phase);
        Assert.True(boss.SurvivalActive);
        Assert.Equal(4, boss.ProjectilePortals.Count);
        Assert.Equal("ENDURE", boss.PhaseLabel);
    }

    [Fact]
    public void DebugSetPhase_SamePhaseAsCurrent_StillReappliesIt()
    {
        var boss = MakeBoss();
        Assert.Equal(1, boss.Phase);
        boss.DebugSetPhase(1); // Python: phase == self.phase forces self.phase = 0 first so _set_phase doesn't no-op
        Assert.Equal(1, boss.Phase);
        Assert.Equal(2.4, boss.PhaseAnnouncementTimer);
    }

    [Fact]
    public void Update_DuringEntrance_StaysPutUntilEntranceElapses()
    {
        var boss = MakeBoss();
        var context = MakeContext(boss, 300, 100);
        float startX = boss.WorldX;
        boss.Update(context); // one 1/120s tick barely dents the 1.25s entrance
        Assert.True(boss.EntranceRemaining > 0);
        Assert.Equal(startX, boss.WorldX); // no movement/attack processed during entrance
    }

    [Fact]
    public void Update_WhileSurvivalActive_CountsDownAndFiresFromPortals()
    {
        var boss = MakeBoss();
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(5);
        double remainingBefore = boss.SurvivalRemaining;
        var context = MakeContext(boss, 300, 100);

        for (int i = 0; i < 120; i++) // ~1 second, enough for the .75s initial portal cooldown to fire
            boss.Update(context);

        Assert.True(boss.SurvivalRemaining < remainingBefore);
        Assert.NotEmpty(context.ProjectileSink);
    }

    [Fact]
    public void Update_SurvivalCompletionAtFinalIndex_BeginsDeathCountdown()
    {
        var boss = MakeBoss();
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(5); // debug-jumping to 5 always selects the final survival index
        var context = MakeContext(boss, 300, 100);

        for (int i = 0; i < 1700; i++) // survivalDuration (14s) * 120 ticks/s == 1680
            boss.Update(context);

        Assert.True(boss.Dying);
        Assert.True(boss.MidpointSurvived); // MidpointSurvived == Dying || Hp <= 0
    }

    [Fact]
    public void Update_DeathCountdownElapsed_SetsHpToZero()
    {
        var boss = MakeBoss();
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(5);
        var context = MakeContext(boss, 300, 100);
        for (int i = 0; i < 1700; i++)
            boss.Update(context);
        Assert.True(boss.Dying);

        for (int i = 0; i < 400; i++) // deathDuration (3s) * 120 ticks/s == 360
            boss.Update(context);

        Assert.Equal(0, boss.Hp);
        Assert.True(boss.IsDead());
    }

    [Fact]
    public void MidpointSurvived_FalseWhileHealthyAndNotDying()
    {
        var boss = MakeBoss();
        Assert.False(boss.MidpointSurvived);
    }
}
