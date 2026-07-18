using Microsoft.Xna.Framework;
using RotBoiRemastered.Entities;
using RotBoiRemastered.Systems;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.Entities;

/// <summary>Ported from bossTypes.py's Malady (no dedicated Python test file to mirror).</summary>
public class MaladyTests
{
    private static Battleground MakeBattleground() => Battleground.GenerateSound();

    private static EnemyUpdateContext MakeContext(Malady boss, Battleground battleground,
        DreamState? dreamState = null, PlayerBuildSnapshot? buildSnapshot = null) => new()
    {
        PlayerWorldX = boss.WorldX + 500, PlayerWorldY = boss.WorldY, Battleground = battleground,
        DreamState = dreamState, PlayerBuildSnapshot = buildSnapshot,
    };

    private static void ClearActTransition(Malady malady, EnemyUpdateContext context)
    {
        for (int i = 0; i < 400 && malady.TakeDamage(0).Blocked; i++)
            malady.Update(context);
    }

    [Fact]
    public void Constructor_UsesFinalStatsAndTenPhases()
    {
        var malady = new Malady(1000, 1000, MakeBattleground(), new Random(1));
        Assert.Equal(48000, malady.Hp);
        Assert.Equal(1, malady.Phase);
        Assert.Equal("THRONE", malady.PhaseLabel);
    }

    [Fact]
    public void TakeDamage_BlockedDuringInitialActTransition()
    {
        var malady = new Malady(1000, 1000, MakeBattleground(), new Random(1));
        var result = malady.TakeDamage(1000);
        Assert.True(result.Blocked);
        Assert.False(result.Applied);
    }

    [Fact]
    public void TakeDamage_DuringSurvivalPhase_IsBlocked()
    {
        var malady = new Malady(1000, 1000, MakeBattleground(), new Random(1));
        malady.DebugSetPhase(4); // in SURVIVAL_PHASES
        Assert.True(malady.SurvivalActive);

        var result = malady.TakeDamage(1000);

        Assert.True(result.Blocked);
    }

    [Fact]
    public void TakeDamage_LethalHit_TriggersCollapseInsteadOfImmediateDeath()
    {
        var battleground = MakeBattleground();
        var malady = new Malady(battleground.SpawnPosition.X, battleground.SpawnPosition.Y, battleground, new Random(1));
        malady.DebugSetPhase(10); // final phase, not a survival phase
        var context = MakeContext(malady, battleground);
        ClearActTransition(malady, context);

        var result = malady.TakeDamage(1_000_000);

        Assert.False(result.Killed);
        Assert.Equal(1, malady.Hp);
        Assert.True(malady.Collapsing);
    }

    [Fact]
    public void Update_WhileCollapsing_CountsDownThenZerosHp()
    {
        var battleground = MakeBattleground();
        var malady = new Malady(battleground.SpawnPosition.X, battleground.SpawnPosition.Y, battleground, new Random(1));
        malady.DebugSetPhase(10);
        var context = MakeContext(malady, battleground);
        ClearActTransition(malady, context);
        malady.TakeDamage(1_000_000);
        Assert.True(malady.Collapsing);

        for (int i = 0; i < 500 && malady.Hp != 0; i++)
            malady.Update(context);

        Assert.Equal(0, malady.Hp);
    }

    [Fact]
    public void Update_CreatesPortalFormationMatchingPhaseOneCount()
    {
        var battleground = MakeBattleground();
        var malady = new Malady(battleground.SpawnPosition.X, battleground.SpawnPosition.Y, battleground, new Random(1));
        var context = MakeContext(malady, battleground);

        malady.Update(context);

        Assert.Equal(3, malady.ProjectilePortals.Count);
    }

    [Fact]
    public void UpdateSpecialRules_SabbathViolation_AltersBeliefWhenPlayerFiresDuringRest()
    {
        var battleground = MakeBattleground();
        var malady = new Malady(battleground.SpawnPosition.X, battleground.SpawnPosition.Y, battleground, new Random(1));
        malady.DebugSetPhase(4);
        var dreamState = new DreamState();
        var bullets = new List<Bullet> { new(0, 0, 1f, 0f, 100f, 10f, Color.White, 1, 10f, false) };
        var context = new EnemyUpdateContext
        {
            PlayerWorldX = malady.WorldX + 500, PlayerWorldY = malady.WorldY, Battleground = battleground,
            DreamState = dreamState, PlayerBullets = bullets,
        };

        for (int i = 0; i < 700 && !malady.RestActive; i++)
            malady.Update(context);

        Assert.True(malady.RestActive);
        Assert.Equal(1, dreamState.FalseRules);
    }

    [Fact]
    public void UpdateSpecialRules_FinalPhaseOfferingPickup_MarksAcceptedAndAltersBelief()
    {
        var battleground = MakeBattleground();
        var malady = new Malady(battleground.SpawnPosition.X, battleground.SpawnPosition.Y, battleground, new Random(1));
        malady.DebugSetPhase(10);
        Assert.Equal(4, malady.OfferingPositions.Count);
        var offering = malady.OfferingPositions[0];
        var dreamState = new DreamState();
        var context = new EnemyUpdateContext
        {
            PlayerWorldX = offering.X, PlayerWorldY = offering.Y, Battleground = battleground, DreamState = dreamState,
        };

        malady.Update(context);

        Assert.True(offering.Taken);
        Assert.Contains(offering.Name, malady.AcceptedOfferings);
        Assert.True(dreamState.Belief > 0);
    }

    private static void FireUntilProjectiles(Malady boss, EnemyUpdateContext context)
    {
        // Entrance (.9s) and the initial act-transition (2.4s) decay in parallel,
        // then the attack cooldown (~1.1s) only starts once both clear -- needs
        // more headroom than the non-final-boss families' 400-tick caps.
        for (int i = 0; i < 600 && context.ProjectileSink.Count == 0; i++)
            boss.Update(context);
        Assert.NotEmpty(context.ProjectileSink);
    }

    [Fact]
    public void FirePhantasiaPattern_PhaseOne_OneOfFourThronesIsReal()
    {
        var battleground = MakeBattleground();
        var malady = new Malady(battleground.SpawnPosition.X, battleground.SpawnPosition.Y, battleground, new Random(1));
        var context = MakeContext(malady, battleground);

        FireUntilProjectiles(malady, context);

        var authorityShots = context.ProjectileSink.Where(p => p.Owner == "malady_phantasia_authority").ToList();
        Assert.NotEmpty(authorityShots);
        Assert.Contains(authorityShots, p => p.Illusory);
        Assert.Contains(authorityShots, p => !p.Illusory);
    }

    [Fact]
    public void FirePhantasiaPattern_PhaseFive_ShotsFractureAcrossTwoGenerations()
    {
        var battleground = MakeBattleground();
        var malady = new Malady(battleground.SpawnPosition.X, battleground.SpawnPosition.Y, battleground, new Random(1));
        malady.DebugSetPhase(5);
        var context = MakeContext(malady, battleground);

        FireUntilProjectiles(malady, context);

        var lineageShots = context.ProjectileSink.Where(p => p.Owner == "malady_phantasia_lineage").ToList();
        Assert.NotEmpty(lineageShots);
        Assert.All(lineageShots, shot => Assert.Equal(2, shot.SplitGeneration));
    }

    [Fact]
    public void FirePhantasiaPattern_PhaseEight_ReadsPlayerBuildSnapshotForStolenShotCount()
    {
        var battleground = MakeBattleground();
        var malady = new Malady(battleground.SpawnPosition.X, battleground.SpawnPosition.Y, battleground, new Random(1));
        malady.DebugSetPhase(8);
        var build = new PlayerBuildSnapshot(
            new Dictionary<string, int>(), new Dictionary<string, int>(),
            new Dictionary<string, double> { ["projectile_count"] = 6 }, "power");
        var context = MakeContext(malady, battleground, buildSnapshot: build);

        FireUntilProjectiles(malady, context);

        var stolenShots = context.ProjectileSink.Where(p => p.Owner == "malady_phantasia_stolen").ToList();
        var unownedShots = context.ProjectileSink.Where(p => p.Owner == "malady_phantasia_unowned").ToList();
        Assert.Equal(8, stolenShots.Count); // round(6) + 2, clamped to [4, 10]
        Assert.Equal(8, unownedShots.Count);
        Assert.All(unownedShots, shot => Assert.True(shot.Illusory));
    }

    [Fact]
    public void ChallengeResults_DefaultsToAllCleanWhenNothingHappened()
    {
        var malady = new Malady(1000, 1000, MakeBattleground(), new Random(1));
        var results = malady.ChallengeResults(new DreamState());
        Assert.True(results["unbelieving"]);
        Assert.True(results["true_witness"]);
        Assert.True(results["content"]);
    }
}
