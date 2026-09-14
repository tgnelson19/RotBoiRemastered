using Microsoft.Xna.Framework;
using RotBoiRemastered.Core;
using RotBoiRemastered.Entities;
using RotBoiRemastered.Systems;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.Systems;

[Collection("GameProfileState")]
public sealed class EgoRunTests
{
    private const float View = 1920f;

    [Fact]
    public void GeneratorPlacesSeparatedHoldoutsAndVeteransBeyondTheirMinimumDistance()
    {
        var run = new EgoRun(7, View);
        Assert.Equal(EgoWorldGenerator.Width, run.Battleground.Width);
        var holdouts = run.Holdouts.ToList();
        Assert.True(holdouts.Count >= 6, $"only {holdouts.Count} holdouts placed");
        Assert.Contains(holdouts, region => region.Kind == EgoRegionKind.VeteranHoldout);
        Assert.Contains(holdouts, region => region.Kind == EgoRegionKind.Holdout);
        float separation = View * EgoWorldGenerator.HoldoutSeparationViews;
        for (int a = 0; a < holdouts.Count; a++)
        {
            Assert.True(Vector2.Distance(holdouts[a].Center, run.Spawn) >= separation);
            Assert.False(run.Battleground.TileAt(
                (int)(holdouts[a].Center.X / Battleground.TileSize),
                (int)(holdouts[a].Center.Y / Battleground.TileSize)).IsSolid());
            if (holdouts[a].IsVeteran)
                Assert.True(Vector2.Distance(holdouts[a].Center, run.Spawn) >= run.VeteranMinDistance);
            for (int b = a + 1; b < holdouts.Count; b++)
            {
                float edgeGap = Vector2.Distance(holdouts[a].Center, holdouts[b].Center)
                    - holdouts[a].RadiusWorld - holdouts[b].RadiusWorld;
                Assert.True(edgeGap >= separation - 1f, $"holdouts {a} and {b} are {edgeGap} apart");
            }
        }
        Assert.Contains(run.Regions, region => region.Kind == EgoRegionKind.Senseless);
        Assert.Contains(run.Regions, region => region.Kind == EgoRegionKind.VeteranSenseless);
        Assert.All(run.Regions.Where(region => !region.IsHoldout),
            cell => Assert.DoesNotContain(holdouts, holdout => holdout.Contains(cell.Center)));
    }

    [Fact]
    public void SameSeedIsDeterministic()
    {
        var a = new EgoRun(99, View);
        var b = new EgoRun(99, View);
        Assert.Equal(a.Holdouts.Select(r => r.Center), b.Holdouts.Select(r => r.Center));
        Assert.Equal(a.Holdouts.Select(r => r.SenseKey), b.Holdouts.Select(r => r.SenseKey));
    }

    [Fact]
    public void MidpointDropIsOneInTwentyFiveAndVeteranLeadersAlwaysDrop()
    {
        var run = new EgoRun(3, View);
        var rng = new Random(5);
        int drops = 0;
        const int trials = 25_000;
        for (int index = 0; index < trials; index++)
        {
            var grunt = new WanderingRangedEnemy(100, 100, 1f, 30f, Color.White, 10, 10, 1, 1) { ContentPath = "sight" };
            if (run.RollDungeonDrop(grunt, grunt.ContentPath, rng) is { } portal)
            {
                drops++;
                Assert.False(portal.Veteran);
                Assert.Equal("sight", portal.SenseKey);
            }
        }
        Assert.InRange(drops / (double)trials, .03, .05);

        var leader = new VeteranGuardianBoss(200, 200, "touch", 500f, rng);
        var veteran = run.RollDungeonDrop(leader, "touch", rng);
        Assert.NotNull(veteran);
        Assert.True(veteran!.Veteran);
        Assert.Null(run.RollDungeonDrop(leader, null, rng));
    }

    [Fact]
    public void DungeonLifecycleCountsMidpointAndVeteranClears()
    {
        var run = new EgoRun(11, View);
        var mid = run.AddPortal(new EgoDungeonPortal("sound", false, new Vector2(10, 10)));
        var vet = run.AddPortal(new EgoDungeonPortal("sound", true, new Vector2(20, 20)));
        Assert.False(run.CompleteDungeon());
        Assert.True(run.EnterDungeon(mid, new Vector2(1, 1)));
        Assert.Equal(new Vector2(1, 1), run.SuspendedReturnPosition);
        Assert.True(run.CompleteDungeon());
        Assert.Equal(1, run.MidpointBossesDefeated);
        Assert.DoesNotContain(mid, run.DungeonPortals);
        Assert.True(run.EnterDungeon(vet, new Vector2(2, 2)));
        Assert.True(run.CompleteDungeon());
        Assert.Contains("sound", run.VeteranBossesDefeated);
        Assert.Empty(run.DungeonPortals);
        Assert.False(run.EnterDungeon(mid, Vector2.Zero));
    }

    [Fact]
    public void EgoDungeonsAreSingleFloorMidpointOrVeteranBossRuns()
    {
        var run = new EgoRun(12, View);
        var mid = PathRun.CreateEgoDungeon(run, new EgoDungeonPortal("chemesthesis", false, Vector2.Zero), new Random(1));
        Assert.True(mid.IsEgoDungeon);
        Assert.True(mid.IsSecretDungeon);
        Assert.Equal(PathFloorBossTier.Midpoint, mid.BossTier);
        Assert.Equal("chemesthesis", mid.CurrentSenseKey);
        mid.NotifyBossDefeated();
        Assert.True(mid.IsComplete);
        Assert.False(mid.ExitPortalOpen);

        var vet = PathRun.CreateEgoDungeon(run, new EgoDungeonPortal("phantasia", true, Vector2.Zero), new Random(1));
        Assert.Equal(PathFloorBossTier.Finale, vet.BossTier);
        Assert.Equal("phantasia", vet.CurrentSenseKey);
    }

    [Fact]
    public void SpawnDirectorRespawnRulesRequireTimerAndPlayerExit()
    {
        var region = new EgoRegion { Kind = EgoRegionKind.Holdout, Center = Vector2.Zero, RadiusWorld = 100 };
        float spawnRadius = 1000;
        Assert.True(EgoSpawnDirector.CanPopulate(region, 500, spawnRadius));
        Assert.False(EgoSpawnDirector.CanPopulate(region, 1500, spawnRadius));

        // Cleared: timer + must step out of range before re-arming.
        region.RespawnTimer = EgoWorldGenerator.HoldoutRespawnSeconds;
        region.AwaitingPlayerExit = true;
        EgoSpawnDirector.TickRegion(region, 500, spawnRadius, EgoWorldGenerator.HoldoutRespawnSeconds + 1);
        Assert.Equal(0, region.RespawnTimer);
        Assert.False(EgoSpawnDirector.CanPopulate(region, 500, spawnRadius), "player never left");
        EgoSpawnDirector.TickRegion(region, 1500, spawnRadius, 0);
        Assert.False(region.AwaitingPlayerExit);
        Assert.True(EgoSpawnDirector.CanPopulate(region, 500, spawnRadius));

        region.Populated = true;
        Assert.False(EgoSpawnDirector.CanPopulate(region, 500, spawnRadius));
    }

    [Fact]
    public void SessionSpawnsOutsideViewAndDespawnsBeyondFourViews()
    {
        var session = new GameSession(Battleground.GenerateMind(), 1280, 720, new Random(1));
        session.StartEgo(new Random(2), ignoreHandsCheck: true);
        Assert.NotNull(session.Ego);
        Assert.Equal(CampaignActivity.Ego, session.CampaignActivity);
        Assert.True(GameSession.HandsEmpty(session.State));
        session.State.CurrentLevel = 4;
        session.State.GracePeriod = 0;
        var rng = new Random(3);
        for (int frame = 0; frame < 600 && session.State.EnemyHolster.Count == 0; frame++)
            session.HandleEnemyCreation(rng);
        Assert.NotEmpty(session.State.EnemyHolster);
        float view = session.Ego!.ViewWidth;
        foreach (Enemy enemy in session.State.EnemyHolster)
        {
            float distance = Vector2.Distance(EgoRun.EnemyCenter(enemy), session.PlayerWorldCenter);
            Assert.True(distance >= view * .9f, $"spawned {distance} from the player, inside the view");
            Assert.True(distance <= session.Ego.DespawnRadius);
        }

        // Teleport far away: every ordinary enemy is shed.
        Vector2 far = session.PlayerWorldCenter + new Vector2(session.Ego.DespawnRadius * 3, 0);
        session.Player.SetPosition(far.X, far.Y);
        session.HandleEnemyCreation(rng);
        Assert.DoesNotContain(session.State.EnemyHolster,
            enemy => Vector2.Distance(EgoRun.EnemyCenter(enemy), session.PlayerWorldCenter) > session.Ego.DespawnRadius);
    }

    [Fact]
    public void EventBossAppearsAtLevelTwentyAndItsDeathOpensAphantasia()
    {
        var session = new GameSession(Battleground.GenerateMind(), 1280, 720, new Random(1));
        session.StartEgo(new Random(2), ignoreHandsCheck: true);
        Assert.False(session.Ego!.EventBossSpawned);
        session.State.CurrentLevel = Progression.FinalBossLevel;
        session.EnsureEgoEventBoss(new Random(4));
        Assert.True(session.Ego.EventBossSpawned);
        FracturedAphantasia shard = Assert.Single(session.State.EnemyHolster.OfType<FracturedAphantasia>());
        Assert.True(Vector2.Distance(EgoRun.EnemyCenter(shard), session.PlayerWorldCenter) >= session.Ego.ViewWidth * .9f);
        var bounty = session.SelectBountyTarget();
        Assert.NotNull(bounty);
        Assert.Same(shard, bounty!.Target);

        session.State.EnemyHolster.Remove(shard);
        session.Ego.RecordEventBossDefeated(new Vector2(500, 500));
        Assert.True(session.Ego.EventBossDefeated);
        Assert.Equal(new Vector2(500, 500), session.Ego.AphantasiaPortalWorld);
        Assert.Same(session.Ego, session.SelectBountyTarget()!.Target);
    }

    [Fact]
    public void EgoAphantasiaHasNoDraftsDenserShotsAndForcedPhaseFour()
    {
        var session = new GameSession(Battleground.GenerateMind(), 1280, 720, new Random(1));
        session.StartEgo(new Random(2), ignoreHandsCheck: true);
        session.State.SetHardMode(false);
        session.State.SetNoExtract(false);
        session.EnterEgoAphantasia(new Random(3));
        Assert.True(session.EgoAphantasiaActive);
        Assert.False(session.InEgoOverworld);
        Assert.False(session.AphantasiaPrecombatDraftsPending);
        Assert.Equal(0, session.State.PendingLevelUps);
        Aphantasia boss = Assert.IsType<Aphantasia>(session.State.ActiveBoss);
        Assert.True(boss.PhaseFourEligible);
        Assert.Equal(Aphantasia.EgoDensityScale, boss.DensityScale);
        Assert.False(session.CanExtract);
    }

    [Fact]
    public void RegularAphantasiaKeepsUnitDensityAndBrazierGate()
    {
        var arena = BossArenaFactory.Create("aphantasia", Progression.FinalBossLevel);
        var boss = new Aphantasia(100, 100, arena, new Random(1), noHealing: true, noExtract: false);
        Assert.Equal(1f, boss.DensityScale);
        Assert.False(boss.PhaseFourEligible);
    }
}
