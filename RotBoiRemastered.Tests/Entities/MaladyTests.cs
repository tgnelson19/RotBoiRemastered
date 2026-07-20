using RotBoiRemastered.Entities;
using RotBoiRemastered.Systems;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.Entities;

public class MaladyTests
{
    private static Battleground MakeBattleground() => Battleground.GenerateSound();

    private static EnemyUpdateContext Context(Malady boss, Battleground battleground) => new()
    {
        PlayerWorldX = boss.WorldX + 500,
        PlayerWorldY = boss.WorldY,
        Battleground = battleground,
        DreamState = new DreamState(),
    };

    private static void FireUntilProjectiles(Malady boss, EnemyUpdateContext context, int limit = 700)
    {
        for (int tick = 0; tick < limit && context.ProjectileSink.Count == 0; tick++)
            boss.Update(context);
        Assert.NotEmpty(context.ProjectileSink);
    }

    [Fact]
    public void Constructor_UsesEmpressIdentityAndPillarScale()
    {
        var boss = new Malady(1000, 1000, MakeBattleground(), new Random(1));

        Assert.Equal(320000, boss.MaxHp);
        Assert.Equal("MALADY", boss.BossDisplayName);
        Assert.Equal("OVERTURE", boss.PhaseLabel);
        Assert.Equal("EMPRESS OF INSPIRATION", Malady.MaladyConfig.Subtitle);
        Assert.Contains("IMPOSSIBLE ENGINE", Malady.MaladyConfig.PhaseLabels);
        Assert.Contains("SOUL INCURSION", Malady.MaladyConfig.PhaseLabels);
        Assert.Equal(10, Malady.IdleBodyCubeCount);
        Assert.Equal(18, Malady.FinaleBodyCubeCount);
        Assert.True(Malady.MaladyConfig.FinalBodyScale > Chronos.ChronosConfig.FinalBodyScale);
        Assert.Equal(10, Malady.MaladyConfig.PhaseLabels.Count);
    }

    [Fact]
    public void InitialActTransition_BlocksDamage()
    {
        var boss = new Malady(1000, 1000, MakeBattleground(), new Random(1));

        var result = boss.TakeDamage(1000);

        Assert.True(result.Blocked);
        Assert.False(result.Applied);
    }

    [Fact]
    public void Intermission_IsHalfwaySurvivalAndBlocksDamage()
    {
        var boss = new Malady(1000, 1000, MakeBattleground(), new Random(1));
        boss.DebugSetPhase(6);

        Assert.True(boss.SurvivalActive);
        Assert.Equal(22.0, boss.SurvivalRemaining);
        Assert.True(boss.TakeDamage(1000).Blocked);
    }

    [Fact]
    public void IntermissionCompletionAdvancesToLuminousTideWithoutRetriggering()
    {
        var battleground = MakeBattleground();
        var boss = new Malady(1000, 1000, battleground, new Random(1));
        var context = Context(boss, battleground);
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(6);
        boss.Hp = (int)Math.Round(boss.MaxHp * .5);
        boss.DebugPhaseLocked = false;

        for (int tick = 0; tick < 3200 && boss.SurvivalActive; tick++)
            boss.Update(context);

        Assert.False(boss.SurvivalActive);
        Assert.Equal(7, boss.Phase);
        Assert.Equal("LUMINOUS TIDE", boss.PhaseLabel);
    }

    [Fact]
    public void PortalFormationMatchesOpeningMovement()
    {
        var battleground = MakeBattleground();
        var boss = new Malady(1000, 1000, battleground, new Random(1));

        boss.Update(Context(boss, battleground));

        Assert.Equal(3, boss.ProjectilePortals.Count);
    }

    [Fact]
    public void OvertureFiresSinePetalsWithAReadableGap()
    {
        var battleground = MakeBattleground();
        var boss = new Malady(1000, 1000, battleground, new Random(2));
        var context = Context(boss, battleground);

        FireUntilProjectiles(boss, context);

        var petals = context.ProjectileSink.Where(shot => shot.Owner == "malady_phantasia_overture_petals").ToList();
        Assert.NotEmpty(petals);
        Assert.All(petals, petal => Assert.Equal("sine", petal.Path));
        Assert.True(petals.Count < 14); // at least the player-facing wedge was omitted
    }

    [Fact]
    public void TentacleGardenSplitsAcrossTwoGenerations()
    {
        var battleground = MakeBattleground();
        var boss = new Malady(1000, 1000, battleground, new Random(3));
        boss.DebugSetPhase(5);
        var context = Context(boss, battleground);

        FireUntilProjectiles(boss, context);

        var tendrils = context.ProjectileSink.Where(shot => shot.Owner == "malady_phantasia_tentacle_garden").ToList();
        Assert.NotEmpty(tendrils);
        Assert.All(tendrils, shot => Assert.Equal(2, shot.SplitGeneration));
    }

    [Fact]
    public void VioletCathedralUsesFullyTelegraphedLasers()
    {
        var battleground = MakeBattleground();
        var boss = new Malady(1000, 1000, battleground, new Random(4));
        boss.DebugSetPhase(8);
        var context = Context(boss, battleground);

        FireUntilProjectiles(boss, context);

        var lasers = context.ProjectileSink.Where(shot => shot.Owner == "malady_phantasia_violet_cathedral").ToList();
        Assert.NotEmpty(lasers);
        Assert.All(lasers, laser =>
        {
            Assert.Equal("laser", laser.Path);
            Assert.True(laser.TelegraphDuration >= 1.0f);
        });
        Assert.True(lasers.Count <= boss.ProjectilePortals.Count - 2); // adjacent aisles remain open
    }

    [Fact]
    public void Apotheosis_IsFortySecondsThenTenSecondCollapse()
    {
        var battleground = MakeBattleground();
        var boss = new Malady(1000, 1000, battleground, new Random(5));
        var context = Context(boss, battleground);
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(10);

        Assert.True(boss.FinaleActive);
        Assert.Equal(40.0, boss.FinaleRemaining);
        Assert.True(boss.TakeDamage(1000).Blocked);

        for (int tick = 0; tick < 5000 && !boss.Collapsing; tick++)
            boss.Update(context);
        Assert.True(boss.Collapsing);
        Assert.Equal(10.0, boss.CollapseDuration);

        for (int tick = 0; tick < 1300 && !boss.IsDead(); tick++)
            boss.Update(context);
        Assert.True(boss.IsDead());
    }

    [Fact]
    public void PhantasiaMistress_DoesNotInheritDreamRulesOrOfferings()
    {
        var battleground = MakeBattleground();
        var boss = new Malady(1000, 1000, battleground, new Random(6));
        var context = Context(boss, battleground);
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(10);

        Assert.Empty(boss.OfferingPositions);
        for (int tick = 0; tick < 800; tick++)
            boss.Update(context);

        Assert.False(boss.RestActive);
        Assert.Empty(boss.OfferingPositions);
    }

    [Fact]
    public void ChallengeResults_DefaultToClean()
    {
        var boss = new Malady(1000, 1000, MakeBattleground(), new Random(1));
        var results = boss.ChallengeResults(new DreamState());

        Assert.True(results["unbelieving"]);
        Assert.True(results["true_witness"]);
        Assert.True(results["content"]);
    }
}
