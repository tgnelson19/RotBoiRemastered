using Microsoft.Xna.Framework;
using RotBoiRemastered.Entities;

namespace RotBoiRemastered.Tests.Entities;

/// <summary>Ported from enemyTypes.py's ArsenalMiniBoss phase-transition/invulnerability behavior.</summary>
public class ArsenalMiniBossTests
{
    private static ArsenalMiniBoss MakeBoss(double hp = 100) =>
        new(100, 100, speed: 2, size: 60, Color.Purple, damage: 10, hp: hp, expValue: 15, difficulty: 1,
            awarenessRange: 500f, rng: new Random(1));

    [Fact]
    public void TakeDamage_CrossingTwoThirds_EntersPhaseOneAndInvulnerable()
    {
        var boss = MakeBoss(100);
        var result = boss.TakeDamage(35); // ratio -> .65, <= 2/3
        Assert.True(result.Applied);
        Assert.Equal(1, boss.Phase);
        Assert.True(boss.Invulnerable);
        Assert.True(boss.TransitionCleanupRequested);
    }

    [Fact]
    public void TakeDamage_WhileInvulnerable_IsBlockedAndDealsNoDamage()
    {
        var boss = MakeBoss(100);
        boss.TakeDamage(35);
        int hpAfterTransition = boss.Hp;

        var blocked = boss.TakeDamage(1000);

        Assert.False(blocked.Applied);
        Assert.True(blocked.Blocked);
        Assert.Equal(hpAfterTransition, boss.Hp);
    }

    [Fact]
    public void Update_ClearsInvulnerability_AfterTransitionWindowElapses()
    {
        var boss = MakeBoss(100);
        boss.TakeDamage(35);
        Assert.True(boss.Invulnerable);

        var battleground = EntityTestFixtures.SmallOpenRoom();
        var context = new EnemyUpdateContext { PlayerWorldX = 105, PlayerWorldY = 105, Battleground = battleground };
        // transitionRemaining = frameRate * .8 = 96 frames; GetTimerStep() defaults to 1.0/call pre-simulation-tick.
        for (int i = 0; i < 100 && boss.Invulnerable; i++)
            boss.Update(context);

        Assert.False(boss.Invulnerable);
    }

    [Fact]
    public void TakeDamage_ThatKills_ReturnsKilledEvenFromPhaseZero()
    {
        var boss = MakeBoss(100);
        var result = boss.TakeDamage(1000);
        Assert.True(result.Killed);
    }
}
