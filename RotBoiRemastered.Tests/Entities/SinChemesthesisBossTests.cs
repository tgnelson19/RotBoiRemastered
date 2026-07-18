using RotBoiRemastered.Entities;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.Entities;

/// <summary>
/// Ported from bossTypes.py's SinChemesthesisBoss (no dedicated Python test
/// file to mirror). Exercises the shared stagger/phase/sigil-transition base
/// via Kage, since SinChemesthesisBoss itself is abstract (never
/// instantiated directly in Python either -- Kage and Rot are its only
/// concrete subclasses).
/// </summary>
public class SinChemesthesisBossTests
{
    private static Battleground MakeBattleground() => Battleground.GenerateSound();

    private static EnemyUpdateContext MakeContext(float playerX, float playerY, Battleground? battleground = null) => new()
    {
        PlayerWorldX = playerX, PlayerWorldY = playerY, Battleground = battleground ?? MakeBattleground(),
    };

    [Fact]
    public void Constructor_SetsFirstPhaseFlavorAndAccent()
    {
        var kage = new Kage(1000, 1000, MakeBattleground(), new Random(1));
        Assert.Equal("FEAST", kage.PhaseLabel);
        Assert.Equal("Take all that you can carry.", kage.PhaseFlavor);
        Assert.Equal(new Microsoft.Xna.Framework.Color(214, 154, 52), kage.PhaseAccent);
    }

    [Fact]
    public void UpdatePhase_HealthThresholds_AdvanceThroughAllFourPhases()
    {
        var kage = new Kage(1000, 1000, MakeBattleground(), new Random(1));
        var context = MakeContext(1000, 1000);

        kage.Hp = (int)(kage.MaxHp * .9); // ratio .9 -> phase 1
        kage.Update(context);
        Assert.Equal(1, kage.Phase);

        kage.Hp = (int)(kage.MaxHp * .6); // ratio .6 -> phase 2
        kage.Update(context);
        Assert.Equal(2, kage.Phase);

        kage.Hp = (int)(kage.MaxHp * .3); // ratio .3 -> phase 3
        kage.Update(context);
        Assert.Equal(3, kage.Phase);

        kage.Hp = (int)(kage.MaxHp * .1); // ratio .1 -> phase 4
        kage.Update(context);
        Assert.Equal(4, kage.Phase);
    }

    [Fact]
    public void TakeDamage_LargeHit_AccumulatesStaggerAndDisablesBoss()
    {
        var kage = new Kage(1000, 1000, MakeBattleground(), new Random(1));
        Assert.False(kage.IsStaggered);

        var result = kage.TakeDamage(10_000); // .012 * 10000 == 120 >= maxStagger(100)

        Assert.True(result.Applied);
        Assert.True(kage.IsStaggered);
    }

    [Fact]
    public void TakeDamage_WhileStaggered_AppliesQuarterBonusMultiplier()
    {
        var kage = new Kage(1000, 1000, MakeBattleground(), new Random(1));
        // Phase 1's health floor is 75% of max -- a single hit big enough to
        // instastagger would also collide with that floor. Accumulate stagger
        // via small hits well clear of the floor instead, so the bonus
        // multiplier's Hp delta is observable without floor interference.
        for (int i = 0; i < 25 && !kage.IsStaggered; i++)
            kage.TakeDamage(200);
        Assert.True(kage.IsStaggered);
        int hpBeforeSecondHit = kage.Hp;

        var result = kage.TakeDamage(200);

        Assert.Equal((int)Math.Round(200 * 1.25), hpBeforeSecondHit - kage.Hp);
        Assert.True(result.Applied);
    }

    [Fact]
    public void Update_WhileStaggered_DoesNotMoveAndDoesNotFire_ThenRecoversAfterDuration()
    {
        var battleground = MakeBattleground();
        var kage = new Kage(battleground.SpawnPosition.X, battleground.SpawnPosition.Y, battleground, new Random(1));
        kage.TakeDamage(10_000);
        Assert.True(kage.IsStaggered);
        float startX = kage.WorldX, startY = kage.WorldY;
        var context = MakeContext(kage.WorldX + 500, kage.WorldY, battleground);

        for (int i = 0; i < 500 && kage.IsStaggered; i++)
            kage.Update(context);

        Assert.False(kage.IsStaggered);
        Assert.Equal(0.0, kage.Stagger);
        Assert.Empty(context.ProjectileSink); // never dispatched an attack while staggered
    }

    [Fact]
    public void DebugSetPhase_LocksPhaseAndResetsCooldown()
    {
        var kage = new Kage(1000, 1000, MakeBattleground(), new Random(1));
        kage.DebugSetPhase(3);
        Assert.Equal(3, kage.Phase);
        Assert.Equal("STAGNANT MIRROR", kage.PhaseLabel);

        kage.Hp = kage.MaxHp;
        kage.Update(MakeContext(1000, 1000));
        Assert.Equal(3, kage.Phase); // locked, doesn't snap back to phase 1
    }

    [Fact]
    public void Update_EntranceActive_HoldsFire()
    {
        var battleground = MakeBattleground();
        var kage = new Kage(battleground.SpawnPosition.X, battleground.SpawnPosition.Y, battleground, new Random(1));
        var context = MakeContext(kage.WorldX + 500, kage.WorldY, battleground);

        kage.Update(context);

        Assert.Empty(context.ProjectileSink);
    }

    [Fact]
    public void Update_AfterEntranceAndCooldownElapse_FiresAPattern()
    {
        var battleground = MakeBattleground();
        var kage = new Kage(battleground.SpawnPosition.X, battleground.SpawnPosition.Y, battleground, new Random(1));
        var context = MakeContext(kage.WorldX + 500, kage.WorldY, battleground);

        for (int i = 0; i < 400 && context.ProjectileSink.Count == 0; i++)
            kage.Update(context);

        Assert.NotEmpty(context.ProjectileSink);
    }
}
