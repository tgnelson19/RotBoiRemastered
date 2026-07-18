using RotBoiRemastered.Entities;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.Entities;

/// <summary>Ported from bossTypes.py's Hypno (no dedicated Python test file to mirror).</summary>
public class HypnoTests
{
    private static Battleground MakeBattleground() => Battleground.GenerateSound();

    private static EnemyUpdateContext MakeContext(Hypno boss, Battleground battleground) => new()
    {
        PlayerWorldX = boss.WorldX + 500, PlayerWorldY = boss.WorldY, Battleground = battleground,
    };

    [Fact]
    public void Constructor_UsesMidStatsAndFivePhases()
    {
        var hypno = new Hypno(1000, 1000, MakeBattleground(), new Random(1));
        Assert.Equal(29000, hypno.Hp);
        Assert.Equal(1, hypno.Phase);
        Assert.Equal("IDOL", hypno.PhaseLabel);
    }

    private static void FireUntilProjectiles(Hypno boss, EnemyUpdateContext context)
    {
        for (int i = 0; i < 400 && context.ProjectileSink.Count == 0; i++)
            boss.Update(context);
        Assert.NotEmpty(context.ProjectileSink);
    }

    [Fact]
    public void FirePhantasiaPattern_PhaseOne_TwoOfThreeShrinesAreIllusory()
    {
        var battleground = MakeBattleground();
        var hypno = new Hypno(battleground.SpawnPosition.X, battleground.SpawnPosition.Y, battleground, new Random(1));
        var context = MakeContext(hypno, battleground);

        FireUntilProjectiles(hypno, context);

        var idolShots = context.ProjectileSink.Where(p => p.Owner == "hypno_phantasia_idol").ToList();
        Assert.NotEmpty(idolShots);
        Assert.Contains(idolShots, p => p.Illusory);
        Assert.Contains(idolShots, p => !p.Illusory && p.TruthMarked);
    }

    [Fact]
    public void FirePhantasiaPattern_PhaseTwo_RuleBannerMatchesRealVolley()
    {
        var battleground = MakeBattleground();
        var hypno = new Hypno(battleground.SpawnPosition.X, battleground.SpawnPosition.Y, battleground, new Random(1));
        hypno.DebugSetPhase(2);
        var context = MakeContext(hypno, battleground);

        FireUntilProjectiles(hypno, context);

        Assert.Contains(context.ProjectileSink, p => p.Owner == "hypno_phantasia_spoken_rule");
        bool ruleIsFalse = hypno.RuleText == "REMAIN";
        Assert.Equal(ruleIsFalse, context.ProjectileSink.Any(p => p.Owner == "hypno_phantasia_spoken_rule" && p.Illusory));
        Assert.Equal(ruleIsFalse, context.ProjectileSink.Any(p => p.Owner == "hypno_phantasia_true_sigil"));
    }

    [Fact]
    public void FirePhantasiaPattern_PhaseThree_ShotsAreSetToFractureIntoDescendants()
    {
        var battleground = MakeBattleground();
        var hypno = new Hypno(battleground.SpawnPosition.X, battleground.SpawnPosition.Y, battleground, new Random(1));
        hypno.DebugSetPhase(3);
        var context = MakeContext(hypno, battleground);

        FireUntilProjectiles(hypno, context);

        var lineageShots = context.ProjectileSink.Where(p => p.Owner == "hypno_phantasia_lineage").ToList();
        Assert.NotEmpty(lineageShots);
        Assert.All(lineageShots, shot => Assert.Equal(3, shot.SplitCount));
    }

    [Fact]
    public void FirePhantasiaPattern_PhaseFour_RealVolleyBesideHarmlessCage()
    {
        var battleground = MakeBattleground();
        var hypno = new Hypno(battleground.SpawnPosition.X, battleground.SpawnPosition.Y, battleground, new Random(1));
        hypno.DebugSetPhase(4);
        var context = MakeContext(hypno, battleground);

        FireUntilProjectiles(hypno, context);

        Assert.Contains(context.ProjectileSink, p => p.Owner == "hypno_phantasia_chosen" && !p.Illusory);
        Assert.Contains(context.ProjectileSink, p => p.Owner == "hypno_phantasia_spared" && p.Illusory);
    }

    [Fact]
    public void FirePhantasiaPattern_PhaseFive_OfferingRingAndDebtFan()
    {
        var battleground = MakeBattleground();
        var hypno = new Hypno(battleground.SpawnPosition.X, battleground.SpawnPosition.Y, battleground, new Random(1));
        hypno.DebugSetPhase(5);
        var context = MakeContext(hypno, battleground);

        FireUntilProjectiles(hypno, context);

        Assert.Contains(context.ProjectileSink, p => p.Owner == "hypno_phantasia_offering");
        Assert.Contains(context.ProjectileSink, p => p.Owner == "hypno_phantasia_debt" && !p.Illusory);
    }
}
