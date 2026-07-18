using RotBoiRemastered.Entities;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.Entities;

/// <summary>Ported from bossTypes.py's Kage (no dedicated Python test file to mirror).</summary>
public class KageTests
{
    private static Battleground MakeBattleground() => Battleground.GenerateSound();

    private static EnemyUpdateContext MakeContext(Kage boss, Battleground battleground) => new()
    {
        PlayerWorldX = boss.WorldX + 500, PlayerWorldY = boss.WorldY, Battleground = battleground,
    };

    [Fact]
    public void Constructor_UsesMidStatsAndFourPhases()
    {
        var kage = new Kage(1000, 1000, MakeBattleground(), new Random(1));
        Assert.Equal(29000, kage.Hp);
        Assert.Equal(1, kage.Phase);
        Assert.Equal("FEAST", kage.PhaseLabel);
        Assert.False(kage.DebugPhaseLocked);
    }

    private static void FireUntilProjectiles(Kage boss, EnemyUpdateContext context)
    {
        for (int i = 0; i < 400 && context.ProjectileSink.Count == 0; i++)
            boss.Update(context);
        Assert.NotEmpty(context.ProjectileSink);
    }

    [Fact]
    public void FireSinPattern_PhaseOne_FeastsWithMines()
    {
        var battleground = MakeBattleground();
        var kage = new Kage(battleground.SpawnPosition.X, battleground.SpawnPosition.Y, battleground, new Random(1));
        var context = MakeContext(kage, battleground);

        FireUntilProjectiles(kage, context);

        Assert.Contains(context.ProjectileSink, p => p.Owner == "kage_chemesthesis_feast" && p.Shape == "mine");
    }

    [Fact]
    public void FireSinPattern_PhaseTwo_ProvokesThenRetorts()
    {
        var battleground = MakeBattleground();
        var kage = new Kage(battleground.SpawnPosition.X, battleground.SpawnPosition.Y, battleground, new Random(1));
        kage.DebugSetPhase(2);
        var context = MakeContext(kage, battleground);

        FireUntilProjectiles(kage, context);

        Assert.Contains(context.ProjectileSink, p => p.Owner == "kage_chemesthesis_provocation");
        Assert.Contains(context.ProjectileSink, p => p.Owner == "kage_chemesthesis_retort" && p.Path == "laser");
    }

    [Fact]
    public void FireSinPattern_PhaseThree_StagnantMirrorsAndMines()
    {
        var battleground = MakeBattleground();
        var kage = new Kage(battleground.SpawnPosition.X, battleground.SpawnPosition.Y, battleground, new Random(1));
        kage.DebugSetPhase(3);
        var context = MakeContext(kage, battleground);

        FireUntilProjectiles(kage, context);

        Assert.Contains(context.ProjectileSink, p => p.Owner == "kage_chemesthesis_stagnant_mirror" && p.Path == "sine");
        Assert.Contains(context.ProjectileSink, p => p.Owner == "kage_chemesthesis_stagnation" && p.Shape == "mine");
    }

    [Fact]
    public void FireSinPattern_PhaseFour_LureFanAndBomb()
    {
        var battleground = MakeBattleground();
        var kage = new Kage(battleground.SpawnPosition.X, battleground.SpawnPosition.Y, battleground, new Random(1));
        kage.DebugSetPhase(4);
        var context = MakeContext(kage, battleground);

        FireUntilProjectiles(kage, context);

        Assert.Contains(context.ProjectileSink, p => p.Owner == "kage_chemesthesis_lure");
        Assert.Contains(context.ProjectileSink, p => p.Owner == "kage_chemesthesis_lure_reward" && p.Path == "bomb");
    }
}
