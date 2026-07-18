using RotBoiRemastered.Entities;
using RotBoiRemastered.Systems;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.Entities;

/// <summary>Ported from bossTypes.py's Rot (no dedicated Python test file to mirror).</summary>
public class RotTests
{
    private static Battleground MakeBattleground() => Battleground.GenerateSound();

    private static EnemyUpdateContext MakeContext(float playerX, float playerY, Battleground battleground,
        BossAfflictions? afflictions = null, PlayerBuildSnapshot? buildSnapshot = null) => new()
    {
        PlayerWorldX = playerX, PlayerWorldY = playerY, Battleground = battleground,
        BossAfflictions = afflictions, PlayerBuildSnapshot = buildSnapshot,
    };

    [Fact]
    public void Constructor_UsesFinalStatsAndSevenPhasesAndFourVents()
    {
        var rot = new Rot(1000, 1000, MakeBattleground(), new Random(1));
        Assert.Equal(48000, rot.Hp);
        Assert.Equal(1, rot.Phase);
        Assert.Equal("CROWN", rot.PhaseLabel);
        Assert.Equal(4, rot.CleansingVents.Count);
        Assert.Empty(rot.CrystalWalls);
    }

    [Fact]
    public void TakeDamage_BlockedDuringInitialActTransition()
    {
        var rot = new Rot(1000, 1000, MakeBattleground(), new Random(1));
        var result = rot.TakeDamage(1000);
        Assert.True(result.Blocked);
        Assert.False(result.Applied);
    }

    private static void ClearActTransition(Rot rot, EnemyUpdateContext context)
    {
        for (int i = 0; i < 400 && rot.TakeDamage(0).Blocked; i++)
            rot.Update(context);
    }

    [Fact]
    public void UpdateTerrain_VentTriggeredByExposure_ResetsAfflictionsAndGrowsWall()
    {
        var battleground = MakeBattleground();
        var rot = new Rot(battleground.SpawnPosition.X, battleground.SpawnPosition.Y, battleground, new Random(1));
        var vent = rot.CleansingVents[0];
        var afflictions = new BossAfflictions();
        afflictions.Apply("pull", duration: 1.0, strength: .1, exposure: 1.0);
        var context = MakeContext(vent.X, vent.Y, battleground, afflictions);

        rot.Update(context);

        Assert.Equal(1, rot.VentsUsed);
        Assert.Single(rot.CrystalWalls);
        Assert.Equal(0.0, afflictions.Exposure);
        Assert.True(rot.PeakExposure >= 1.0);
    }

    [Fact]
    public void MovementObstacles_IncludesFreshlyGrownNonWarningWall()
    {
        var battleground = MakeBattleground();
        var rot = new Rot(battleground.SpawnPosition.X, battleground.SpawnPosition.Y, battleground, new Random(1));
        var vent = rot.CleansingVents[0];
        var afflictions = new BossAfflictions();
        afflictions.Apply("pull", duration: 1.0, strength: .1, exposure: 1.0);
        var context = MakeContext(vent.X, vent.Y, battleground, afflictions);
        rot.Update(context);

        var obstacles = rot.MovementObstacles();

        Assert.Single(obstacles);
        Assert.Equal(rot.CrystalWalls[0].Rect, obstacles[0]);
    }

    [Fact]
    public void GetScreenHitboxes_IncludesBrittleCrystalWallEntry()
    {
        var battleground = MakeBattleground();
        var rot = new Rot(battleground.SpawnPosition.X, battleground.SpawnPosition.Y, battleground, new Random(1));
        var vent = rot.CleansingVents[0];
        var afflictions = new BossAfflictions();
        afflictions.Apply("pull", duration: 1.0, strength: .1, exposure: 1.0);
        var context = MakeContext(vent.X, vent.Y, battleground, afflictions);
        rot.Update(context);
        Assert.Equal("brittle", rot.CrystalWalls[0].Kind); // patternRotation starts at 0 -> brittle

        var hitboxes = rot.GetScreenHitboxes(new Camera(), new Microsoft.Xna.Framework.Vector2(rot.WorldX, rot.WorldY), Microsoft.Xna.Framework.Vector2.Zero);

        Assert.Contains(hitboxes, h => h.Part == "crystal:0");
    }

    [Fact]
    public void TakeDamage_CrystalPart_DamagesOnlyThatWallAndPopsWhenDepleted()
    {
        var battleground = MakeBattleground();
        var rot = new Rot(battleground.SpawnPosition.X, battleground.SpawnPosition.Y, battleground, new Random(1));
        var vent = rot.CleansingVents[0];
        var afflictions = new BossAfflictions();
        afflictions.Apply("pull", duration: 1.0, strength: .1, exposure: 1.0);
        var context = MakeContext(vent.X, vent.Y, battleground, afflictions);
        rot.Update(context);
        ClearActTransition(rot, context);
        int hpBefore = rot.Hp;

        var result = rot.TakeDamage(500, "crystal:0");

        Assert.True(result.Applied);
        Assert.Equal(hpBefore, rot.Hp); // crystal hits never touch the boss's own HP
        Assert.Empty(rot.CrystalWalls); // 420 hp brittle wall popped by a 500-damage hit
    }

    [Fact]
    public void TakeDamage_InvalidCrystalIndex_IsBlocked()
    {
        var rot = new Rot(1000, 1000, MakeBattleground(), new Random(1));
        var result = rot.TakeDamage(500, "crystal:0");
        Assert.True(result.Blocked);
    }

    [Fact]
    public void SetSinPhase_ClearsCrystalWalls()
    {
        var battleground = MakeBattleground();
        var rot = new Rot(battleground.SpawnPosition.X, battleground.SpawnPosition.Y, battleground, new Random(1));
        var vent = rot.CleansingVents[0];
        var afflictions = new BossAfflictions();
        afflictions.Apply("pull", duration: 1.0, strength: .1, exposure: 1.0);
        var context = MakeContext(vent.X, vent.Y, battleground, afflictions);
        rot.Update(context);
        Assert.NotEmpty(rot.CrystalWalls);

        rot.DebugSetPhase(2);

        Assert.Empty(rot.CrystalWalls);
    }

    private static void FireUntilProjectiles(Rot boss, EnemyUpdateContext context)
    {
        for (int i = 0; i < 400 && context.ProjectileSink.Count == 0; i++)
            boss.Update(context);
        Assert.NotEmpty(context.ProjectileSink);
    }

    [Fact]
    public void FireSinPattern_PhaseOne_PrideParallelLanes()
    {
        var battleground = MakeBattleground();
        var rot = new Rot(battleground.SpawnPosition.X, battleground.SpawnPosition.Y, battleground, new Random(1));
        var context = MakeContext(rot.WorldX + 500, rot.WorldY, battleground);

        FireUntilProjectiles(rot, context);

        Assert.Contains(context.ProjectileSink, p => p.Owner == "rot_chemesthesis_pride_crown" && p.Path == "laser");
    }

    [Fact]
    public void FireSinPattern_PhaseTwo_GreedHoardAndOrbitingCoins()
    {
        var battleground = MakeBattleground();
        var rot = new Rot(battleground.SpawnPosition.X, battleground.SpawnPosition.Y, battleground, new Random(1));
        rot.DebugSetPhase(2);
        var context = MakeContext(rot.WorldX + 500, rot.WorldY, battleground);

        FireUntilProjectiles(rot, context);

        Assert.Contains(context.ProjectileSink, p => p.Owner == "rot_chemesthesis_greed_hoard" && p.Shape == "mine");
        Assert.Contains(context.ProjectileSink, p => p.Owner == "rot_chemesthesis_greed_coin" && p.Path == "orbit");
    }

    [Fact]
    public void FireSinPattern_PhaseThree_LustPullAffliction()
    {
        var battleground = MakeBattleground();
        var rot = new Rot(battleground.SpawnPosition.X, battleground.SpawnPosition.Y, battleground, new Random(1));
        rot.DebugSetPhase(3);
        var context = MakeContext(rot.WorldX + 500, rot.WorldY, battleground);

        FireUntilProjectiles(rot, context);

        Assert.Contains(context.ProjectileSink, p => p.Owner == "rot_chemesthesis_lust_pull" && p.Affliction == "pull");
        Assert.Contains(context.ProjectileSink, p => p.Owner == "rot_chemesthesis_lust_lure" && p.Path == "bomb");
    }

    [Fact]
    public void FireSinPattern_PhaseFour_EnvyReadsPlayerBuildSnapshotDominantOffense()
    {
        var battleground = MakeBattleground();
        var rot = new Rot(battleground.SpawnPosition.X, battleground.SpawnPosition.Y, battleground, new Random(1));
        rot.DebugSetPhase(4);
        var build = new PlayerBuildSnapshot(
            new Dictionary<string, int>(), new Dictionary<string, int>(),
            new Dictionary<string, double> { ["projectile_count"] = 5 }, "critical");
        var context = MakeContext(rot.WorldX + 500, rot.WorldY, battleground, buildSnapshot: build);

        FireUntilProjectiles(rot, context);

        Assert.Contains(context.ProjectileSink, p => p.Owner == "rot_chemesthesis_envy_critical" && p.Path == "laser");
        Assert.Contains(context.ProjectileSink, p => p.Owner == "rot_chemesthesis_envy_reflection");
    }

    [Fact]
    public void FireSinPattern_PhaseSix_WrathFanAndCrossedLanes()
    {
        var battleground = MakeBattleground();
        var rot = new Rot(battleground.SpawnPosition.X, battleground.SpawnPosition.Y, battleground, new Random(1));
        rot.DebugSetPhase(6);
        var context = MakeContext(rot.WorldX + 500, rot.WorldY, battleground);

        FireUntilProjectiles(rot, context);

        Assert.Contains(context.ProjectileSink, p => p.Owner == "rot_chemesthesis_wrath_retort");
        Assert.Contains(context.ProjectileSink, p => p.Owner == "rot_chemesthesis_wrath_answer" && p.Path == "laser");
        Assert.Contains(context.ProjectileSink, p => p.Owner == "rot_chemesthesis_wrath_cross" && p.Path == "laser");
    }

    [Fact]
    public void FireSinPattern_PhaseSeven_SlothAppliesSlowAffliction()
    {
        var battleground = MakeBattleground();
        var rot = new Rot(battleground.SpawnPosition.X, battleground.SpawnPosition.Y, battleground, new Random(1));
        rot.DebugSetPhase(7);
        var context = MakeContext(rot.WorldX + 500, rot.WorldY, battleground);

        FireUntilProjectiles(rot, context);

        Assert.Contains(context.ProjectileSink, p => p.Owner == "rot_chemesthesis_sloth_rot" && p.Affliction == "slow");
    }

    [Fact]
    public void ChallengeResults_DefaultsToAllCleanWhenNothingHappened()
    {
        var rot = new Rot(1000, 1000, MakeBattleground(), new Random(1));
        var results = rot.ChallengeResults();
        Assert.True(results["clean_traversal"]);
        Assert.True(results["vent_discipline"]);
        Assert.True(results["uncontaminated"]);
    }

    [Fact]
    public void ChallengeResults_ReflectsExposureAndVentUsage()
    {
        var battleground = MakeBattleground();
        var rot = new Rot(battleground.SpawnPosition.X, battleground.SpawnPosition.Y, battleground, new Random(1));
        var vent = rot.CleansingVents[0];
        var afflictions = new BossAfflictions();
        afflictions.Apply("pull", duration: 1.0, strength: .1, exposure: 5.0);
        var context = MakeContext(vent.X, vent.Y, battleground, afflictions);
        rot.Update(context);

        var results = rot.ChallengeResults();

        Assert.False(results["clean_traversal"]); // peakExposure 5.0 > 3.0
        Assert.False(results["uncontaminated"]); // peakExposure 5.0 > .25
        Assert.True(results["vent_discipline"]); // ventsUsed 1 <= 1
    }
}
