using RotBoiRemastered.Entities;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.Entities;

public class ChronosTests
{
    private static Battleground MakeBattleground() => Battleground.GenerateSound();

    private static EnemyUpdateContext Context(Chronos boss, Battleground battleground) => new()
    {
        PlayerWorldX = boss.WorldX + 600,
        PlayerWorldY = boss.WorldY,
        Battleground = battleground,
    };

    [Fact]
    public void Constructor_UsesSlowHeavySightBaneIdentity()
    {
        var boss = new Chronos(1000, 1000, MakeBattleground(), new Random(1));

        Assert.Equal(240000, boss.MaxHp);
        Assert.Equal("CHRONOS", boss.BossDisplayName);
        Assert.Equal("DIRECTIVE", boss.PhaseLabel);
        Assert.Equal("THE KING OF ATTRITION", Chronos.ChronosConfig.Subtitle);
        Assert.Contains("THORN OF TIME", Chronos.ChronosConfig.PhaseLabels);
        Assert.Equal(15, Chronos.AmbientMoteCount);
        Assert.Equal(24, Chronos.FinaleMoteCount);
        Assert.True(Chronos.ChronosConfig.MovementSpeed < Ishe.IsheConfig.MovementSpeed);
    }

    [Fact]
    public void HalfHealthStartsStillSecondAndBlocksDamage()
    {
        var battleground = MakeBattleground();
        var boss = new Chronos(1000, 1000, battleground, new Random(2));
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(3);
        boss.DebugPhaseLocked = false;

        boss.TakeDamage(boss.MaxHp);

        Assert.True(boss.MidpointSurvivalActive);
        Assert.Equal("STILL SECOND", boss.PhaseLabel);
        Assert.Equal(boss.MaxHp / 2, boss.Hp);
        Assert.True(boss.TakeDamage(1000).Blocked);
    }

    [Fact]
    public void HugeOpeningHitStopsAtFirstLessonInsteadOfSkippingToSurvival()
    {
        var boss = new Chronos(1000, 1000, MakeBattleground(), new Random(2));
        boss.EntranceRemaining = 0;

        boss.TakeDamage(boss.MaxHp);

        Assert.Equal((int)Math.Round(boss.MaxHp * .84), boss.Hp);
        Assert.False(boss.MidpointSurvivalActive);
    }

    [Fact]
    public void DirectiveDrawsWholeSegmentedTentacleBeforeItStrikes()
    {
        var battleground = MakeBattleground();
        var boss = new Chronos(1000, 1000, battleground, new Random(3));
        var context = Context(boss, battleground);
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(1);

        for (int tick = 0; tick < 350 && context.ProjectileSink.Count == 0; tick++)
            boss.Update(context);

        var segments = context.ProjectileSink.Where(shot => shot.Owner?.Contains("chronos_directive") == true).ToList();
        Assert.True(segments.Count >= 12);
        Assert.All(segments, segment =>
        {
            Assert.Equal("laser", segment.Path);
            Assert.True(segment.TelegraphDuration >= 1.5f);
            Assert.True(segment.Lifetime > segment.TelegraphDuration);
        });
    }

    [Fact]
    public void StillSecondRadialTentaclesKeepAThreeArmOpening()
    {
        var battleground = MakeBattleground();
        var boss = new Chronos(1000, 1000, battleground, new Random(4));
        var context = Context(boss, battleground);
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(4);

        boss.Update(context);

        var segments = context.ProjectileSink.Where(shot => shot.Owner?.Contains("chronos_still_second") == true).ToList();
        Assert.Equal(35, segments.Count); // seven arms x five segments; three of ten arms are the safe opening
    }

    [Fact]
    public void ThornOfTime_IsOneFullyDeclaredEightSegmentKillingLine()
    {
        var battleground = MakeBattleground();
        var boss = new Chronos(1000, 1000, battleground, new Random(7));
        var context = Context(boss, battleground);
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(6);

        for (int tick = 0; tick < 900 && !context.ProjectileSink.Any(shot => shot.Owner?.Contains("thorn_of_time") == true); tick++)
            boss.Update(context);

        var thorn = context.ProjectileSink.Where(shot => shot.Owner?.Contains("thorn_of_time") == true).ToList();
        Assert.Equal(8, thorn.Count);
        Assert.All(thorn, segment =>
        {
            Assert.Equal("laser", segment.Path);
            Assert.True(segment.TelegraphDuration >= 2.3f);
            Assert.True(segment.Damage >= 1200);
        });
    }

    [Fact]
    public void KingsAttritionIsFortySecondsThenTenSecondCollapse()
    {
        var battleground = MakeBattleground();
        var boss = new Chronos(1000, 1000, battleground, new Random(5));
        var context = Context(boss, battleground);
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(7);

        Assert.True(boss.FinaleActive);
        Assert.Equal(40.0, boss.FinaleRemaining);
        Assert.True(boss.TakeDamage(1000).Blocked);

        for (int tick = 0; tick < 5000 && !boss.Dying; tick++)
            boss.Update(context);
        Assert.True(boss.Dying);
        Assert.Equal(10.0, boss.DeathDuration);

        for (int tick = 0; tick < 1300 && !boss.IsDead(); tick++)
            boss.Update(context);
        Assert.True(boss.IsDead());
    }
}
