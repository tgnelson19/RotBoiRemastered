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
        Assert.Equal(18.0, boss.SurvivalRemaining);
        Assert.True(boss.TakeDamage(1000).Blocked);
    }

    [Theory]
    [InlineData(1, .9, 2)]
    [InlineData(5, .5, 6)]
    [InlineData(9, .1, 10)]
    public void DamageMovementsCannotSkipTheirSecondIdea(int phase, double floorRatio, int nextPhase)
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var boss = new Malady(1000, 1000, battleground, new Random(20 + phase));
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(phase);
        boss.DebugPhaseLocked = false;
        var context = Context(boss, battleground);

        ReachDeclarations(boss, context, 1);
        boss.TakeDamage(boss.MaxHp * 4.0);

        Assert.Equal((int)Math.Round(boss.MaxHp * floorRatio), boss.Hp);
        Assert.Equal(phase, boss.Phase);
        Assert.False(boss.FinaleActive);

        ReachDeclarations(boss, context, Malady.MinimumDamagePhaseDeclarations);
        Step(boss, context);

        Assert.Equal(nextPhase, boss.Phase);
        if (nextPhase == 6)
            Assert.True(boss.SurvivalActive);
        Assert.False(boss.FinaleActive);
    }

    [Fact]
    public void ApotheosisRequiresTwoPhaseTenDeclarationsBeforeVitalitySeals()
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var boss = new Malady(1000, 1000, battleground, new Random(31));
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(9);
        boss.DebugPhaseLocked = false;
        var context = Context(boss, battleground);

        ReachDeclarations(boss, context, 1);
        boss.TakeDamage(boss.MaxHp * 4.0);
        ReachDeclarations(boss, context, 2);
        Step(boss, context);
        Assert.Equal(10, boss.Phase);

        boss.TakeDamage(boss.MaxHp * 4.0);
        Assert.Equal(1, boss.Hp);
        Assert.False(boss.FinaleActive);

        ReachDeclarations(boss, context, 2);
        boss.TakeDamage(1);
        Assert.True(boss.FinaleActive);
        Assert.Equal(30.0, boss.FinaleRemaining);
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
        Assert.Equal(6, boss.ProjectilePortals.Count);
        Assert.Equal(boss.ProjectilePortals.Count - 2, lasers.Count); // exactly two adjacent aisles remain open
        Assert.Equal("laser", boss.AttackPose);
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
        Assert.Contains("malady_phantasia_apotheosis_laser", pressure.EdgeOwners);
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
    public void Apotheosis_IsThirtySecondsThenTenSecondCollapse()
    {
        var battleground = MakeBattleground();
        var boss = new Malady(1000, 1000, battleground, new Random(5));
        var context = Context(boss, battleground);
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(10);

        Assert.True(boss.FinaleActive);
        Assert.Equal(30.0, boss.FinaleRemaining);
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
        for (int tick = 0; tick < Simulation.FrameRate * 15; tick++)
            boss.Update(context);
        Assert.InRange(boss.ApotheosisCrownPetalCount, 12, 13);
        for (int tick = 0; tick < Simulation.FrameRate * 14.5; tick++)
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
