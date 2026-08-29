using Microsoft.Xna.Framework;
using RotBoiRemastered.Core;
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

    private static void Step(Malady boss, EnemyUpdateContext context)
    {
        boss.Update(context);
        var children = new List<EnemyProjectile>();
        foreach (var projectile in context.ProjectileSink.ToList())
        {
            projectile.Update(context.Battleground, casualMode: false);
            children.AddRange(projectile.SpawnedProjectiles);
            projectile.SpawnedProjectiles.Clear();
        }
        context.ProjectileSink.RemoveAll(projectile => projectile.RemFlag);
        context.ProjectileSink.AddRange(children);
    }

    /// <summary>
    /// Runs off the opening act transition, which the constructor arms and
    /// which blocks all damage while it plays.
    /// </summary>
    private static void ClearOpening(Malady boss, EnemyUpdateContext context)
    {
        boss.EntranceRemaining = 0;
        for (int tick = 0; tick < 600 && boss.TakeDamage(0).Blocked; tick++)
            Step(boss, context);
        boss.DebugRebasePhaseHealth();
    }

    private static void ReachDeclarations(Malady boss, EnemyUpdateContext context, int count)
    {
        for (int tick = 0; tick < 2400 && boss.PhaseDeclarations < count; tick++)
            Step(boss, context);
        Assert.True(boss.PhaseDeclarations >= count,
            $"Phase {boss.Phase} produced only {boss.PhaseDeclarations} declarations.");
    }

    private sealed record PhasePressure(int PeakProjectiles, int OverflowCount,
        int EdgeProjectileCount, int PlayerThreatProjectileCount,
        IReadOnlySet<string> EdgeOwners, IReadOnlySet<string> PlayerThreatOwners);

    private static PhasePressure SimulatePhasePressure(int phase, bool casualMode, double duration = 30.0)
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var boss = new Malady(1000, 1000, battleground, new Random(100 + phase));
        boss.DebugSetPhase(phase);
        boss.EntranceRemaining = 0;
        var playerCenter = boss.ArenaCenter + Vector2.UnitX * boss.ArenaRadius * .93f;
        int playerSize = (int)(Simulation.TileSize * .75f);
        var playerRect = new Rectangle((int)(playerCenter.X - playerSize / 2f),
            (int)(playerCenter.Y - playerSize / 2f), playerSize, playerSize);
        var context = new EnemyUpdateContext
        {
            PlayerWorldX = playerCenter.X,
            PlayerWorldY = playerCenter.Y,
            Battleground = battleground,
            DreamState = new DreamState(),
        };
        var edgeOwners = new HashSet<string>();
        var threatOwners = new HashSet<string>();
        var edgeProjectiles = new HashSet<EnemyProjectile>();
        var threateningProjectiles = new HashSet<EnemyProjectile>();
        int peak = 0, overflow = 0;
        int ticks = (int)Math.Ceiling(duration * Simulation.FrameRate);

        for (int tick = 0; tick < ticks; tick++)
        {
            boss.Update(context);
            var children = new List<EnemyProjectile>();
            foreach (var projectile in context.ProjectileSink.ToList())
            {
                var center = new Vector2(projectile.WorldX + projectile.Size / 2f,
                    projectile.WorldY + projectile.Size / 2f);
                float radius = Vector2.Distance(center, boss.ArenaCenter);
                bool spansArena = projectile.Path == "laser" &&
                    projectile.RemainingRange >= boss.ArenaRadius * 2f;
                if (radius > boss.ArenaRadius * 1.04f)
                    projectile.RemFlag = true;
                else if ((radius >= boss.ArenaRadius * .92f || spansArena) && projectile.Owner is not null)
                {
                    edgeOwners.Add(projectile.Owner);
                    edgeProjectiles.Add(projectile);
                }
                if (projectile.Collides(playerRect) && projectile.Owner is not null)
                {
                    threatOwners.Add(projectile.Owner);
                    threateningProjectiles.Add(projectile);
                }

                projectile.Update(battleground, casualMode);
                center = new Vector2(projectile.WorldX + projectile.Size / 2f,
                    projectile.WorldY + projectile.Size / 2f);
                if (Vector2.Distance(center, boss.ArenaCenter) >= boss.ArenaRadius * .92f &&
                    projectile.Owner is not null)
                {
                    edgeOwners.Add(projectile.Owner);
                    edgeProjectiles.Add(projectile);
                }
                children.AddRange(projectile.SpawnedProjectiles);
                projectile.SpawnedProjectiles.Clear();
            }
            context.ProjectileSink.RemoveAll(projectile => projectile.RemFlag);
            context.ProjectileSink.AddRange(children);
            peak = Math.Max(peak, context.ProjectileSink.Count);
            if (context.ProjectileSink.Count > GameSession.MaxBossProjectiles)
            {
                overflow += context.ProjectileSink.Count - GameSession.MaxBossProjectiles;
                context.ProjectileSink.RemoveRange(0,
                    context.ProjectileSink.Count - GameSession.MaxBossProjectiles);
            }
        }

        return new PhasePressure(peak, overflow, edgeProjectiles.Count, threateningProjectiles.Count,
            edgeOwners, threatOwners);
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
        Assert.Equal(6, Malady.InitialApotheosisCrownPetals);
        Assert.True(Malady.MaladyConfig.FinalBodyScale > Chronos.ChronosConfig.FinalBodyScale);
        Assert.Equal(10, Malady.MaladyConfig.PhaseLabels.Count);
        Assert.Equal(1.05, Malady.MaladyConfig.FinalCooldownSeconds, 2);
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
        Assert.Equal(20.0, boss.SurvivalRemaining);
        Assert.True(boss.TakeDamage(1000).Blocked);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(9)]
    public void EveryMovementSurrendersOnlyItsOwnBudgetThenRunsItsClock(int phase)
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var boss = new Malady(1000, 1000, battleground, new Random(20 + phase));
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(phase);
        boss.DebugPhaseLocked = false;
        var context = Context(boss, battleground);
        ClearOpening(boss, context);
        int before = boss.Hp;

        boss.TakeDamage(boss.MaxHp * 4.0);

        // A movement surrenders at most fifteen percent of maximum health,
        // whatever arrives, so the gallery cannot be walked by burst damage.
        Assert.Equal(
            before - (int)Math.Round(boss.MaxHp * BossPhaseGovernor.DefaultThresholdFraction),
            boss.Hp);
        Assert.Equal(phase, boss.Phase);
        Assert.False(boss.FinaleActive);

        boss.DebugCompletePhaseClock();
        Step(boss, context);

        Assert.NotEqual(phase, boss.Phase);
        Assert.False(boss.FinaleActive);
    }

    [Fact]
    public void ApotheosisOpensOnceTheLastMovementRunsTheHealthBarOut()
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var boss = new Malady(1000, 1000, battleground, new Random(31));
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(9);
        boss.DebugPhaseLocked = false;
        ClearOpening(boss, Context(boss, battleground));

        boss.TakeDamage(boss.MaxHp * 4.0);
        Assert.False(boss.FinaleActive);

        boss.Hp = (int)Math.Round(boss.MaxHp * .1);
        boss.DebugRebasePhaseHealth();
        boss.TakeDamage(boss.MaxHp * 4.0);

        Assert.True(boss.FinaleActive);
        Assert.Equal(25.0, boss.FinaleRemaining);
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

        // The intermission now hands off to a randomly chosen movement rather
        // than always to Luminous Tide, and never back to itself.
        Assert.False(boss.SurvivalActive);
        Assert.NotEqual(6, boss.Phase);
        Assert.False(boss.FinaleActive);
        Assert.Equal((int)Math.Round(boss.MaxHp * .5), boss.Hp);

        for (int tick = 0; tick < 600; tick++)
            boss.Update(context);
        Assert.False(boss.SurvivalActive);
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
        // The corolla is authored at fourteen slots and scaled by the shared
        // difficulty curve; whatever it resolves to, the player-facing wedge
        // is still omitted, so fewer petals fire than there are slots.
        int slots = BossDifficultyScalars.Finale.Shots(14);
        Assert.True(petals.Count < slots,
            $"Expected fewer than {slots} petals, saw {petals.Count}.");
        Assert.True(petals.Count > 14,
            $"Expected the corolla to have grown past its authored fourteen, saw {petals.Count}.");
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

        // Each open aisle now carries a small woven bundle of thin ribbon
        // strands (FlowingRibbonLaser) rather than one solid beam.
        var lasers = context.ProjectileSink
            .Where(shot => shot.Owner?.StartsWith("malady_phantasia_violet_cathedral", StringComparison.Ordinal) == true)
            .ToList();
        Assert.NotEmpty(lasers);
        Assert.All(lasers, laser =>
        {
            Assert.Equal("laser", laser.Path);
            Assert.True(laser.TelegraphDuration >= 1.0f);
        });
        Assert.Equal(6, boss.ProjectilePortals.Count);
        Assert.Equal((boss.ProjectilePortals.Count - 2) * 3, lasers.Count); // exactly two adjacent aisles remain open, three strands per open aisle
        Assert.Equal("laser", boss.AttackPose);
    }

    [Fact]
    public void LuminousTidePortalShotgunMatchesDissonanceBaselineDensity()
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var boss = new Malady(1000, 1000, battleground, new Random(44));
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(7);
        var context = Context(boss, battleground);

        ReachDeclarations(boss, context, 1);
        const string owner = "malady_phantasia_portal_dream_burst";
        Assert.Equal(3, context.ProjectileSink.Count(shot => shot.Owner == owner));

        foreach (var portal in boss.ProjectilePortals)
            portal.UpdateBursts(context.ProjectileSink, .13f);
        foreach (var portal in boss.ProjectilePortals)
            portal.UpdateBursts(context.ProjectileSink, .13f);

        Assert.Equal(12, context.ProjectileSink.Count(shot => shot.Owner == owner));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    public void EveryPhaseReachesAndThreatensTheArenaEdgeWithoutOverflow(int phase)
    {
        foreach (bool casualMode in new[] { false, true })
        {
            var pressure = SimulatePhasePressure(phase, casualMode);
            string mode = casualMode ? "casual" : "standard";

            Assert.True(pressure.EdgeProjectileCount >= 5,
                $"Phase {phase} in {mode} mode carried only {pressure.EdgeProjectileCount} " +
                "projectiles to the arena edge.");
            Assert.True(pressure.PlayerThreatProjectileCount >= 2,
                $"Phase {phase} in {mode} mode threatened a player holding the arena edge with only " +
                $"{pressure.PlayerThreatProjectileCount} projectiles.");
            Assert.True(pressure.OverflowCount == 0,
                $"Phase {phase} in {mode} mode exceeded the " +
                $"{GameSession.MaxBossProjectiles}-projectile budget by {pressure.OverflowCount} " +
                $"total projectiles (peak {pressure.PeakProjectiles}).");
            Assert.InRange(pressure.PeakProjectiles, 1, Malady.ActiveThreatSoftCap + 8);
        }
    }

    [Fact]
    public void ApotheosisCarriesEverySignatureMovementAcrossTheArena()
    {
        var pressure = SimulatePhasePressure(10, casualMode: false);

        Assert.Contains("malady_phantasia_apotheosis_flood", pressure.EdgeOwners);
        Assert.Contains("malady_phantasia_apotheosis_tentacle", pressure.EdgeOwners);
        Assert.Contains("malady_phantasia_apotheosis_corolla", pressure.EdgeOwners);
        // The apotheosis laser is now a small bundle of thin ribbon strands
        // (owners suffixed "_0"/"_1"/"_2"), not one single-owner beam.
        Assert.Contains(pressure.EdgeOwners,
            owner => owner.StartsWith("malady_phantasia_apotheosis_laser", StringComparison.Ordinal));
        Assert.True(pressure.PlayerThreatOwners.Count >= 2,
            "Apotheosis should pressure an edge camper through more than one pattern family.");
    }

    [Fact]
    public void ImpossibleEngineUsesRigidGearsAndRadialPose()
    {
        var battleground = MakeBattleground();
        var boss = new Malady(1000, 1000, battleground, new Random(3));
        boss.DebugSetPhase(3);
        var context = Context(boss, battleground);

        for (int tick = 0; tick < 700 && !context.ProjectileSink.Any(
                 shot => shot.Owner == "malady_phantasia_impossible_engine_drive"); tick++)
            boss.Update(context);

        var teeth = context.ProjectileSink.Where(shot =>
            shot.Owner?.StartsWith("malady_phantasia_impossible_engine_") == true).ToList();
        Assert.NotEmpty(teeth);
        Assert.All(teeth, shot => Assert.Equal("linear", shot.Path));
        Assert.Equal("radial", boss.AttackPose);
    }

    [Fact]
    public void RibbonCourtUsesNamedChainsAndMatchingAttackPose()
    {
        var battleground = MakeBattleground();
        var boss = new Malady(1000, 1000, battleground, new Random(5));
        boss.DebugSetPhase(4);
        var context = Context(boss, battleground);

        for (int tick = 0; tick < 700 && !context.ProjectileSink.Any(
                 shot => shot.Owner == "malady_phantasia_ribbon_court"); tick++)
            boss.Update(context);

        Assert.Contains(context.ProjectileSink, shot => shot.Owner == "malady_phantasia_ribbon_court");
        Assert.Equal("chain", boss.AttackPose);
    }

    [Fact]
    public void Apotheosis_IsTheStandardTwentyFiveSecondSurvivalThenTenSecondCollapse()
    {
        var battleground = MakeBattleground();
        var boss = new Malady(1000, 1000, battleground, new Random(5));
        var context = Context(boss, battleground);
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(10);

        Assert.True(boss.FinaleActive);
        Assert.Equal(25.0, boss.FinaleRemaining);
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
    public void ApotheosisBuildsFromSixPetalsIntoTheFullEighteenCubeCrown()
    {
        var battleground = MakeBattleground();
        var boss = new Malady(1000, 1000, battleground, new Random(9));
        boss.DebugSetPhase(10);
        var context = Context(boss, battleground);

        Assert.Equal(Malady.InitialApotheosisCrownPetals, boss.ApotheosisCrownPetalCount);
        for (int tick = 0; tick < Simulation.FrameRate * 12.5; tick++)
            boss.Update(context);
        Assert.InRange(boss.ApotheosisCrownPetalCount, 12, 13);
        for (int tick = 0; tick < Simulation.FrameRate * 12; tick++)
            boss.Update(context);
        Assert.Equal(Malady.FinaleBodyCubeCount, boss.ApotheosisCrownPetalCount);
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
