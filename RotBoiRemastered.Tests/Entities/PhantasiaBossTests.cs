using RotBoiRemastered.Entities;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.Entities;

/// <summary>
/// Ported from bossTypes.py's PhantasiaBoss (no dedicated Python test file
/// to mirror). Exercises the shared commandment-sigil/act-transition/
/// offering base via Hypno, since PhantasiaBoss itself is abstract (never
/// instantiated directly in Python either -- Hypno and Malady are its only
/// concrete subclasses).
/// </summary>
public class PhantasiaBossTests
{
    private static Battleground MakeBattleground() => Battleground.GenerateSound();

    private static EnemyUpdateContext MakeContext(float playerX, float playerY, Battleground? battleground = null) => new()
    {
        PlayerWorldX = playerX, PlayerWorldY = playerY, Battleground = battleground ?? MakeBattleground(),
    };

    [Fact]
    public void Constructor_SetsFirstPhaseFlavorAndAccent()
    {
        var hypno = new Hypno(1000, 1000, MakeBattleground(), new Random(1));
        Assert.Equal("IDOL", hypno.PhaseLabel);
        Assert.Equal("Surely you recognize the one before you.", hypno.PhaseFlavor);
        Assert.Equal(new Microsoft.Xna.Framework.Color(214, 89, 188), hypno.PhaseAccent);
    }

    [Fact]
    public void UpdatePhase_HealthThresholdAloneCannotSkipTheCurrentSuggestion()
    {
        var hypno = new Hypno(1000, 1000, MakeBattleground(), new Random(1));
        var context = MakeContext(1000, 1000);

        hypno.Hp = (int)(hypno.MaxHp * .75);
        hypno.Update(context);

        Assert.Equal(1, hypno.Phase);
        Assert.Equal(0, hypno.PhaseDeclarations);
    }

    [Fact]
    public void TakeDamage_PinsHealthToCurrentPhaseFloor()
    {
        var hypno = new Hypno(1000, 1000, MakeBattleground(), new Random(1));
        // Idol owns the opening quarter and cannot be burst through before
        // its two complete suggestions.
        var result = hypno.TakeDamage(1_000_000);

        Assert.True(result.Applied);
        Assert.Equal((int)Math.Round(hypno.MaxHp * .75), hypno.Hp);
        Assert.Equal(1, hypno.Phase);
    }

    [Fact]
    public void DebugSetPhase_LocksPhaseAndResetsCooldown()
    {
        var hypno = new Hypno(1000, 1000, MakeBattleground(), new Random(1));
        hypno.DebugSetPhase(3);
        Assert.Equal(3, hypno.Phase);
        Assert.Equal("INHERITANCE", hypno.PhaseLabel);

        hypno.Hp = hypno.MaxHp;
        hypno.Update(MakeContext(1000, 1000));
        Assert.Equal(3, hypno.Phase); // locked, doesn't snap back to phase 1
    }

    [Fact]
    public void Update_EntranceActive_HoldsFire()
    {
        var battleground = MakeBattleground();
        var hypno = new Hypno(battleground.SpawnPosition.X, battleground.SpawnPosition.Y, battleground, new Random(1));
        var context = MakeContext(hypno.WorldX + 500, hypno.WorldY, battleground);

        hypno.Update(context);

        Assert.Empty(context.ProjectileSink);
    }

    [Fact]
    public void Update_AfterEntranceAndCooldownElapse_FiresAPattern()
    {
        var battleground = MakeBattleground();
        var hypno = new Hypno(battleground.SpawnPosition.X, battleground.SpawnPosition.Y, battleground, new Random(1));
        var context = MakeContext(hypno.WorldX + 500, hypno.WorldY, battleground);

        for (int i = 0; i < 400 && context.ProjectileSink.Count == 0; i++)
            hypno.Update(context);

        Assert.NotEmpty(context.ProjectileSink);
    }

    [Fact]
    public void Update_StaticMovementMode_DoesNotMoveTowardPlayer()
    {
        // Hypno's own movementModes (inherited "chase","path","static") puts "static" at index 2 (phase 3).
        var battleground = MakeBattleground();
        var hypno = new Hypno(battleground.SpawnPosition.X, battleground.SpawnPosition.Y, battleground, new Random(1));
        hypno.DebugSetPhase(3);
        float startX = hypno.WorldX, startY = hypno.WorldY;
        var farAwayContext = MakeContext(startX + 5000, startY + 5000, battleground);

        for (int i = 0; i < 10; i++)
            hypno.Update(farAwayContext);

        Assert.Equal(startX, hypno.WorldX, 2);
        Assert.Equal(startY, hypno.WorldY, 2);
    }
}
