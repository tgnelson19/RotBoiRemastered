using Microsoft.Xna.Framework;
using RotBoiRemastered.Core;
using RotBoiRemastered.Entities;
using RotBoiRemastered.Systems;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.Entities;

public class AcheTests
{
    private static Battleground MakeBattleground() => Battleground.GenerateSound();

    private static EnemyUpdateContext Context(Ache boss, Battleground battleground) => new()
    {
        PlayerWorldX = boss.WorldX + 600,
        PlayerWorldY = boss.WorldY + 120,
        Battleground = battleground,
        BossAfflictions = new BossAfflictions(),
    };

    private static void ClearOpening(Ache boss, EnemyUpdateContext context)
    {
        boss.EntranceRemaining = 0;
        for (int tick = 0; tick < 400 && boss.TakeDamage(0).Blocked; tick++)
            boss.Update(context);
    }

    private static void StepAche(Ache boss, EnemyUpdateContext context)
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

    private static void ReachAcheDeclarations(
        Ache boss, EnemyUpdateContext context, int count)
    {
        for (int tick = 0; tick < 2400 && boss.PhaseDeclarations < count; tick++)
            StepAche(boss, context);
        Assert.True(boss.PhaseDeclarations >= count);
    }

    private sealed record AchePressure(int PeakProjectiles, int PeakPersistentHazards,
        int OverflowCount, IReadOnlySet<int> HazardQuadrants, IReadOnlySet<string> Owners,
        IReadOnlyList<string> SpawnSignature, IReadOnlyList<(string Owner, float Damage)> SpawnedShots);

    private sealed record AchePlayerPressure(int PeakProjectiles, int OverflowCount,
        int ProjectileThreats, int TerrainThreats, IReadOnlySet<string> ThreatOwners);

    private static AchePressure SimulatePressure(int phase, int seed = 100, double duration = 24.0)
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        float bodySize = Simulation.TileSize * (float)Ache.AcheConfig.FinalBodyScale;
        float spawnX = battleground.Width * Simulation.TileSize / 2f - bodySize / 2f;
        float spawnY = battleground.Height * Simulation.TileSize / 2f - bodySize / 2f;
        var boss = new Ache(spawnX, spawnY, battleground, new Random(seed));
        boss.DebugSetPhase(phase);
        boss.EntranceRemaining = 0;
        var player = boss.ArenaCenter + new Vector2(boss.ArenaRadius * .24f, boss.ArenaRadius * -.16f);
        var context = new EnemyUpdateContext
        {
            PlayerWorldX = player.X,
            PlayerWorldY = player.Y,
            Battleground = battleground,
            BossAfflictions = new BossAfflictions(),
        };
        var known = new HashSet<EnemyProjectile>();
        var quadrants = new HashSet<int>();
        var owners = new HashSet<string>();
        var signature = new List<string>();
        var spawnedShots = new List<(string Owner, float Damage)>();
        int peak = 0, peakPersistent = 0, overflow = 0;
        int ticks = (int)Math.Ceiling(duration * Simulation.FrameRate);

        for (int tick = 0; tick < ticks; tick++)
        {
            boss.Update(context);
            foreach (var projectile in context.ProjectileSink)
            {
                if (!known.Add(projectile))
                    continue;
                if (projectile.Owner is not null)
                {
                    owners.Add(projectile.Owner);
                    signature.Add($"{projectile.Owner}:{MathF.Round(projectile.WorldX / 25f)}:{MathF.Round(projectile.WorldY / 25f)}");
                    spawnedShots.Add((projectile.Owner, projectile.Damage));
                }
            }

            var children = new List<EnemyProjectile>();
            foreach (var projectile in context.ProjectileSink.ToList())
            {
                var center = new Vector2(projectile.WorldX + projectile.Size / 2f,
                    projectile.WorldY + projectile.Size / 2f);
                if (Vector2.Distance(center, boss.ArenaCenter) > boss.ArenaRadius * 1.04f)
                    projectile.RemFlag = true;
                projectile.Update(battleground, casualMode: false);
                children.AddRange(projectile.SpawnedProjectiles);
                projectile.SpawnedProjectiles.Clear();

                if (projectile.Path is "mine" or "pool" or "bomb")
                {
                    var offset = center - boss.ArenaCenter;
                    if (offset.Length() >= boss.ArenaRadius * .1f)
                    {
                        float angle = MathF.Atan2(offset.Y, offset.X);
                        int quadrant = (int)MathF.Floor(((angle + MathF.Tau) % MathF.Tau) / (MathF.PI / 2f));
                        quadrants.Add(quadrant);
                    }
                }
            }
            context.ProjectileSink.RemoveAll(projectile => projectile.RemFlag);
            context.ProjectileSink.AddRange(children);
            peak = Math.Max(peak, context.ProjectileSink.Count);
            peakPersistent = Math.Max(peakPersistent, context.ProjectileSink.Count(projectile =>
                projectile.Path is "mine" or "pool" or "bomb"));
            if (context.ProjectileSink.Count > GameSession.MaxBossProjectiles)
            {
                overflow += context.ProjectileSink.Count - GameSession.MaxBossProjectiles;
                context.ProjectileSink.RemoveRange(0,
                    context.ProjectileSink.Count - GameSession.MaxBossProjectiles);
            }
        }

        return new AchePressure(peak, peakPersistent, overflow, quadrants, owners, signature, spawnedShots);
    }

    private static AchePlayerPressure SimulatePlayerPressure(int phase, Vector2 playerOffset,
        int seed = 900, double duration = 24.0)
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        float bodySize = Simulation.TileSize * (float)Ache.AcheConfig.FinalBodyScale;
        var boss = new Ache(battleground.Width * Simulation.TileSize / 2f - bodySize / 2f,
            battleground.Height * Simulation.TileSize / 2f - bodySize / 2f,
            battleground, new Random(seed + phase));
        boss.DebugSetPhase(phase);
        boss.EntranceRemaining = 0;
        var playerCenter = boss.ArenaCenter + new Vector2(
            playerOffset.X * boss.ArenaRadius, playerOffset.Y * boss.ArenaRadius);
        int playerSize = (int)(Simulation.TileSize * .75f);
        var playerRect = new Rectangle((int)(playerCenter.X - playerSize / 2f),
            (int)(playerCenter.Y - playerSize / 2f), playerSize, playerSize);
        var context = new EnemyUpdateContext
        {
            PlayerWorldX = playerCenter.X,
            PlayerWorldY = playerCenter.Y,
            Battleground = battleground,
            BossAfflictions = new BossAfflictions(),
        };
        var projectileThreats = new HashSet<EnemyProjectile>();
        var terrainThreats = new HashSet<CrystalWall>();
        var owners = new HashSet<string>();
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
                if (!boss.ProjectileWithinArenaBounds(center))
                    projectile.RemFlag = true;
                if (projectile.Collides(playerRect) && projectileThreats.Add(projectile) && projectile.Owner is not null)
                    owners.Add(projectile.Owner);
                projectile.Update(battleground, casualMode: false);
                if (projectile.Collides(playerRect) && projectileThreats.Add(projectile) && projectile.Owner is not null)
                    owners.Add(projectile.Owner);
                children.AddRange(projectile.SpawnedProjectiles);
                projectile.SpawnedProjectiles.Clear();
            }
            foreach (var wall in boss.CrystalWalls.Where(wall => wall.Warning <= 0 && wall.Rect.Intersects(playerRect)))
                terrainThreats.Add(wall);
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

        return new AchePlayerPressure(peak, overflow, projectileThreats.Count,
            terrainThreats.Count, owners);
    }

    [Fact]
    public void Constructor_UsesCorrectChemesthesisIdentityAndBalance()
    {
        var boss = new Ache(1000, 1000, MakeBattleground(), new Random(1));

        Assert.Equal(305000, boss.MaxHp);
        Assert.Equal("ACHE", boss.BossDisplayName);
        Assert.Equal("MISFIRE", boss.PhaseLabel);
        Assert.Equal("THE UNCOMMANDED CORE", Ache.AcheConfig.Subtitle);
        Assert.Equal(3, Ache.OrbitingArmCount);
        Assert.Equal("PHANTOM", Ache.AcheSinConfig.SinSigils[0].Name);
        Assert.Equal("UNBOUND", Ache.AcheSinConfig.SinSigils[^1].Name);
        Assert.True(Ache.AcheConfig.FinalBodyScale < Chronos.ChronosConfig.FinalBodyScale);
        Assert.True(Ache.AcheConfig.FinalCooldownSeconds > Malady.MaladyConfig.FinalCooldownSeconds);
        var malady = new Malady(1000, 1000, MakeBattleground(), new Random(2));
        Assert.True(boss.MaxHp < malady.MaxHp);
        Assert.True(boss.FinaleDuration <= malady.FinaleDuration);
        Assert.Equal(8, Ache.AcheConfig.PhaseLabels.Count);
        Assert.Equal(4, boss.CleansingVents.Count);
        Assert.Equal(140.0, boss.MaxStagger);
        Assert.True(Ache.ActiveThreatSoftCap < Rot.ActiveBurdenSoftCap);
    }

    [Fact]
    public void PrimaryShotBandsKillAStandardDefendedBuildInFiveToEightHits()
    {
        const double health = 1000;
        const double expectedDefense = 25;
        const double chemesthesisDamageScale = .88;
        float[] authoredDamage =
        {
            Ache.MineDamage, Ache.FieldDamage, Ache.RingDamage, Ache.BombDamage, Ache.HeavyDamage,
        };

        foreach (float damage in authoredDamage)
        {
            double tunedDamage = MathF.Round(damage * (float)chemesthesisDamageScale);
            double received = Math.Round(Math.Max(tunedDamage - expectedDefense,
                Math.Min(tunedDamage, Math.Max(25, tunedDamage * .1))));
            int hitsToKill = (int)Math.Ceiling(health / received);
            Assert.InRange(hitsToKill, 5, 8);
        }
    }

    [Fact]
    public void BombFragmentsRemainInTheModerateDamageBand()
    {
        var fragments = Enumerable.Range(1, 6)
            .SelectMany(seed => SimulatePressure(8, seed: 700 + seed).SpawnedShots)
            .Where(shot => shot.Owner == "ache_chemesthesis_discord_bomb_burst")
            .ToList();

        Assert.NotEmpty(fragments);
        Assert.All(fragments, shot => Assert.Equal(Ache.MineDamage, shot.Damage));
    }

    [Fact]
    public void HalfHealth_StartsInvulnerableReflexStorm()
    {
        var battleground = MakeBattleground();
        var boss = new Ache(1000, 1000, battleground, new Random(2));
        var context = Context(boss, battleground);
        ClearOpening(boss, context);
        boss.DebugPhaseLocked = false;
        boss.DebugSetPhase(3);
        boss.DebugPhaseLocked = false;

        boss.TakeDamage(boss.MaxHp);
        Assert.False(boss.MidpointSurvivalActive);
        ReachAcheDeclarations(boss, context, 2);
        StepAche(boss, context);

        Assert.True(boss.MidpointSurvivalActive);
        Assert.Equal("REFLEX STORM", boss.PhaseLabel);
        Assert.Equal(boss.MaxHp / 2, boss.Hp);
        Assert.True(boss.TakeDamage(1000).Blocked);
    }

    [Fact]
    public void HugeOpeningHitStopsAtFirstChaoticLesson()
    {
        var battleground = MakeBattleground();
        var boss = new Ache(1000, 1000, battleground, new Random(2));
        var context = Context(boss, battleground);
        ClearOpening(boss, context);

        boss.TakeDamage(boss.MaxHp);

        Assert.Equal((int)(boss.MaxHp * .84), boss.Hp);
        Assert.False(boss.MidpointSurvivalActive);
    }

    [Theory]
    [InlineData(2, .67, 3)]
    [InlineData(5, .25, 6)]
    [InlineData(6, .12, 7)]
    public void IntermediateDamageMovementsCannotBeSkippedBeforeTwoDeclarations(
        int phase, double floorRatio, int nextPhase)
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var boss = new Ache(1000, 1000, battleground, new Random(30 + phase));
        var context = Context(boss, battleground);
        boss.DebugSetPhase(phase);
        ClearOpening(boss, context);
        boss.DebugPhaseLocked = false;

        boss.TakeDamage(boss.MaxHp);
        Assert.Equal((int)Math.Round(boss.MaxHp * floorRatio), boss.Hp);
        Assert.Equal(phase, boss.Phase);

        ReachAcheDeclarations(boss, context, 2);
        StepAche(boss, context);
        Assert.Equal(nextPhase, boss.Phase);
    }

    [Fact]
    public void WhiteAcheMustDeclareTwiceBeforeOverload()
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var boss = new Ache(1000, 1000, battleground, new Random(38));
        var context = Context(boss, battleground);
        boss.DebugSetPhase(7);
        ClearOpening(boss, context);

        boss.TakeDamage(boss.MaxHp);
        Assert.Equal(1, boss.Hp);
        Assert.False(boss.FinaleActive);

        ReachAcheDeclarations(boss, context, 2);
        boss.TakeDamage(1);
        Assert.True(boss.FinaleActive);
        Assert.Equal(8, boss.Phase);
    }

    [Fact]
    public void ChaosPatternsRemainSparseScatteredAndUseNoPortals()
    {
        var battleground = MakeBattleground();
        var boss = new Ache(1000, 1000, battleground, new Random(4));
        var pressure = SimulatePressure(7, seed: 4);

        Assert.DoesNotContain(pressure.Owners, owner => owner.Contains("portal"));
        Assert.Contains(pressure.Owners, owner => owner.Contains("predicted_lash") || owner.Contains("crossed_nerves"));
        Assert.Contains(pressure.Owners, owner => owner.Contains("mine") || owner.Contains("pocket") || owner.Contains("cluster"));
        Assert.InRange(pressure.PeakProjectiles, 1, 50);
        Assert.InRange(pressure.PeakPersistentHazards, 1, 28);
        Assert.Equal(0, pressure.OverflowCount);
        Assert.True(pressure.HazardQuadrants.Count >= 3);
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
    public void EveryPhaseMaintainsASparseDistributedMinefield(int phase)
    {
        foreach (int seed in new[] { 311 + phase, 547 + phase, 883 + phase })
        {
            var pressure = SimulatePressure(phase, seed);

            Assert.InRange(pressure.PeakProjectiles, 1, 50);
            Assert.InRange(pressure.PeakPersistentHazards, 1, 28);
            Assert.Equal(0, pressure.OverflowCount);
            Assert.True(pressure.HazardQuadrants.Count >= 2,
                $"Phase {phase}, seed {seed} placed hazards in only " +
                $"{pressure.HazardQuadrants.Count} arena quadrants.");
        }
    }

    [Fact]
    public void IndependentSeedsBuildDifferentMinefields()
    {
        var first = SimulatePressure(8, seed: 71);
        var second = SimulatePressure(8, seed: 92);

        Assert.NotEqual(string.Join('|', first.SpawnSignature.Take(16)),
            string.Join('|', second.SpawnSignature.Take(16)));
        Assert.True(first.Owners.Count >= 5);
        Assert.True(second.Owners.Count >= 5);
    }

    [Fact]
    public void CornerPocketTelegraphsThreeSidesAndLeavesOneEscape()
    {
        var battleground = MakeBattleground();
        float bodySize = Simulation.TileSize * (float)Ache.AcheConfig.FinalBodyScale;
        var boss = new Ache(battleground.Width * Simulation.TileSize / 2f - bodySize / 2f,
            battleground.Height * Simulation.TileSize / 2f - bodySize / 2f,
            battleground, new Random(12));
        boss.DebugSetPhase(7);
        boss.EntranceRemaining = 0;
        var player = boss.ArenaCenter + new Vector2(boss.ArenaRadius * .18f, 0);
        var context = new EnemyUpdateContext
        {
            PlayerWorldX = player.X,
            PlayerWorldY = player.Y,
            Battleground = battleground,
            BossAfflictions = new BossAfflictions(),
        };

        for (int tick = 0; tick < 2400 && context.ProjectileSink.Count(projectile =>
                 projectile.Owner == "ache_chemesthesis_corner_pocket") < 3; tick++)
            boss.Update(context);

        var pocket = context.ProjectileSink.Where(projectile =>
            projectile.Owner == "ache_chemesthesis_corner_pocket").Take(3).ToList();
        Assert.Equal(3, pocket.Count);
        Assert.All(pocket, mine =>
        {
            Assert.Equal("mine", mine.Path);
            Assert.True(mine.TelegraphDuration >= 1.15f);
            Assert.False(mine.Collides(new Rectangle((int)mine.WorldX, (int)mine.WorldY,
                (int)mine.Size, (int)mine.Size)));
            float distance = Vector2.Distance(
                new Vector2(mine.WorldX + mine.Size / 2f, mine.WorldY + mine.Size / 2f), player);
            Assert.InRange(distance, Simulation.TileSize * 1.5f, Simulation.TileSize * 2.35f);
        });
        var sectors = pocket.Select(mine =>
        {
            float angle = MathF.Atan2(mine.WorldY + mine.Size / 2f - player.Y,
                mine.WorldX + mine.Size / 2f - player.X);
            return (int)MathF.Floor(((angle + MathF.Tau) % MathF.Tau) / (MathF.PI / 2f));
        }).ToHashSet();
        Assert.Equal(3, sectors.Count);
    }

    [Fact]
    public void VentsCleanseExposureButRaiseANewInnerBorder()
    {
        var battleground = MakeBattleground();
        var boss = new Ache(1000, 1000, battleground, new Random(21));
        var afflictions = new BossAfflictions();
        var vent = boss.CleansingVents[0];
        var context = new EnemyUpdateContext
        {
            PlayerWorldX = vent.X,
            PlayerWorldY = vent.Y,
            Battleground = battleground,
            BossAfflictions = afflictions,
        };
        ClearOpening(boss, context);
        afflictions.Apply("slow", 1.0, .1, exposure: 1.2);

        boss.Update(context);

        Assert.Equal(0, afflictions.Exposure);
        Assert.Equal(1, boss.VentsUsed);
        Assert.Single(boss.CrystalWalls);
        Assert.Equal("brittle", boss.CrystalWalls[0].Kind);
    }

    [Fact]
    public void BreakingABrittleFalseBorderProducesAVisibleReleasePulse()
    {
        var battleground = MakeBattleground();
        var boss = new Ache(1000, 1000, battleground, new Random(22));
        var afflictions = new BossAfflictions();
        var vent = boss.CleansingVents[0];
        var context = new EnemyUpdateContext
        {
            PlayerWorldX = vent.X,
            PlayerWorldY = vent.Y,
            Battleground = battleground,
            BossAfflictions = afflictions,
        };
        ClearOpening(boss, context);
        afflictions.Apply("slow", 1.0, .1, exposure: 1.2);
        boss.Update(context);

        var result = boss.TakeDamage(1000, "crystal:0");

        Assert.True(result.Applied);
        Assert.Empty(boss.CrystalWalls);
        Assert.Equal(1.0, boss.CrystalBreakPulse);
    }

    [Fact]
    public void BreakingThreeBrittleBordersGroundsTheUncommandedCore()
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var boss = new Ache(1000, 1000, battleground, new Random(39));
        var afflictions = new BossAfflictions();
        var openingContext = Context(boss, battleground);
        ClearOpening(boss, openingContext);

        for (int index = 0; index < Ache.NerveBreaksNeeded; index++)
        {
            var vent = boss.CleansingVents[index];
            afflictions.Apply("slow", 1.0, .1, exposure: 1.2);
            var ventContext = new EnemyUpdateContext
            {
                PlayerWorldX = vent.X,
                PlayerWorldY = vent.Y,
                Battleground = battleground,
                BossAfflictions = afflictions,
                ProjectileSink = openingContext.ProjectileSink,
            };
            boss.Update(ventContext);
            Assert.Single(boss.CrystalWalls);
            Assert.Equal("brittle", boss.CrystalWalls[0].Kind);
            boss.TakeDamage(1000, "crystal:0");
        }

        Assert.Equal(1, boss.NerveBreakTriggers);
        Assert.Equal(0, boss.NerveBreakProgress);
        Assert.True(boss.IsStaggered);
        Assert.Equal(boss.MaxStagger, boss.Stagger);
        Assert.True(boss.StaggerRemaining > 0);
    }

    [Fact]
    public void LaterActsGrowTelegraphedCompressionWallsThatMoveInward()
    {
        var battleground = MakeBattleground();
        var boss = new Ache(1000, 1000, battleground, new Random(23));
        var context = Context(boss, battleground);
        boss.DebugSetPhase(5);
        boss.EntranceRemaining = 0;

        for (int tick = 0; tick < 1200 && !boss.CrystalWalls.Any(wall => wall.Compression); tick++)
            boss.Update(context);

        var wall = Assert.Single(boss.CrystalWalls, wall => wall.Compression);
        Assert.True(wall.Warning > 0);
        var initialRect = wall.Rect;
        var initialBossCenter = new Vector2(boss.WorldX + boss.Size / 2f, boss.WorldY + boss.Size / 2f);
        double initialDistance = Vector2.Distance(new Vector2(wall.Rect.Center.X, wall.Rect.Center.Y), initialBossCenter);

        for (int tick = 0; tick < 420; tick++)
            boss.Update(context);

        double movedDistance = Vector2.Distance(new Vector2(wall.Rect.Center.X, wall.Rect.Center.Y), initialBossCenter);
        Assert.NotEqual(initialRect, wall.Rect);
        Assert.True(movedDistance < initialDistance);
        Assert.Contains(wall.Rect, boss.MovementObstacles());
    }

    [Fact]
    public void PatternMemoryPreventsRepeatsAndGuaranteesRegularDirectReactions()
    {
        var battleground = MakeBattleground();
        var boss = new Ache(1000, 1000, battleground, new Random(24));
        var context = Context(boss, battleground);
        boss.DebugSetPhase(1);
        boss.EntranceRemaining = 0;

        for (int tick = 0; tick < 2400 && boss.PatternHistory.Count < 12; tick++)
        {
            boss.Update(context);
            foreach (var projectile in context.ProjectileSink)
                projectile.Update(battleground, casualMode: false);
            context.ProjectileSink.RemoveAll(projectile => projectile.RemFlag);
        }

        var history = boss.PatternHistory.ToList();
        Assert.True(history.Count >= 9);
        for (int index = 1; index < history.Count; index++)
            Assert.NotEqual(history[index - 1], history[index]);
        for (int index = 0; index <= history.Count - 3; index++)
            Assert.Contains(history.Skip(index).Take(3), pattern => pattern is 1 or 5);
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
    public void EveryMovementReliablyThreatensAStationaryOuterPlayerWithoutOverflow(int phase)
    {
        foreach (var position in new[] { new Vector2(.9f, 0), new Vector2(.64f, .64f) })
        {
            var pressure = SimulatePlayerPressure(phase, position);
            int minimumThreats = phase >= 6 ? 2 : 1;

            Assert.True(pressure.ProjectileThreats >= minimumThreats,
                $"Phase {phase} produced only {pressure.ProjectileThreats} direct threats at {position}.");
            Assert.InRange(pressure.PeakProjectiles, 1, 50);
            Assert.Equal(0, pressure.OverflowCount);
        }
    }

    [Fact]
    public void LaterMovementsUseReactiveCountersWithoutBecomingPortalOrPetalDensity()
    {
        var owners = Enumerable.Range(0, 4)
            .Select(seed => SimulatePressure(7, seed: 1100 + seed).Owners)
            .SelectMany(ownerSet => ownerSet)
            .ToHashSet();

        Assert.Contains("ache_chemesthesis_counterreaction", owners);
        Assert.DoesNotContain(owners, owner => owner.Contains("portal") || owner.Contains("petal"));
    }

    [Fact]
    public void AcheStaggerRequiresMoreCommitmentAndDecaysAfterPressureStops()
    {
        var battleground = MakeBattleground();
        var boss = new Ache(1000, 1000, battleground, new Random(25));
        var context = Context(boss, battleground);
        ClearOpening(boss, context);

        for (int hit = 0; hit < 25; hit++)
            boss.TakeDamage(1);
        Assert.False(boss.IsStaggered);
        Assert.Equal(100.0, boss.Stagger);

        for (int tick = 0; tick < 240; tick++)
            boss.Update(context);
        Assert.Equal(100.0, boss.Stagger);
        for (int tick = 0; tick < 240; tick++)
            boss.Update(context);
        Assert.True(boss.Stagger < 100.0);

        for (int hit = 0; hit < 30; hit++)
            boss.TakeDamage(1);
        boss.DebugSetPhase(2);
        Assert.True(boss.Stagger <= boss.MaxStagger * .5);
    }

    [Fact]
    public void OverloadFalseBordersStayTemporaryBoundedAndOffTheMarkedPlayer()
    {
        var battleground = MakeBattleground();
        var boss = new Ache(1000, 1000, battleground, new Random(26));
        boss.DebugSetPhase(8);
        boss.EntranceRemaining = 0;
        var player = boss.ArenaCenter + Vector2.UnitX * boss.ArenaRadius * .72f;
        int playerSize = (int)(Simulation.TileSize * .75f);
        var playerRect = new Rectangle((int)(player.X - playerSize / 2f),
            (int)(player.Y - playerSize / 2f), playerSize, playerSize);
        var context = new EnemyUpdateContext
        {
            PlayerWorldX = player.X,
            PlayerWorldY = player.Y,
            Battleground = battleground,
            BossAfflictions = new BossAfflictions(),
        };
        int peakCompression = 0;
        var observed = new HashSet<CrystalWall>();

        for (int tick = 0; tick < 3600; tick++)
        {
            boss.Update(context);
            foreach (var wall in boss.CrystalWalls.Where(wall => wall.Compression))
            {
                if (observed.Add(wall))
                {
                    Assert.True(wall.Warning > 0);
                    Assert.False(wall.Rect.Intersects(playerRect));
                }
            }
            peakCompression = Math.Max(peakCompression,
                boss.CrystalWalls.Count(wall => wall.Compression));
        }

        Assert.True(observed.Count >= 3);
        Assert.InRange(peakCompression, 1, 4);
    }

    [Fact]
    public void Overload_IsThirtySecondsThenTenSecondDeath()
    {
        var battleground = MakeBattleground();
        var boss = new Ache(1000, 1000, battleground, new Random(5));
        var context = Context(boss, battleground);
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(8);

        Assert.True(boss.FinaleActive);
        Assert.Equal(30.0, boss.FinaleRemaining);
        Assert.Equal(3, boss.OverloadConstellationNodeCount);
        Assert.True(boss.TakeDamage(1000).Blocked);

        for (int tick = 0; tick < Simulation.FrameRate * 16; tick++)
            StepAche(boss, context);
        Assert.InRange(boss.OverloadConstellationNodeCount, 7,
            Ache.OverloadConstellationMaxNodes);

        for (int tick = 0; tick < 5000 && !boss.Dying; tick++)
            StepAche(boss, context);
        Assert.True(boss.Dying);
        Assert.Equal(10.0, boss.DeathDuration);

        for (int tick = 0; tick < 1300 && !boss.IsDead(); tick++)
            boss.Update(context);
        Assert.True(boss.IsDead());
    }
}

public class RotTests
{
    private static Battleground MakeBattleground() => Battleground.GenerateSound();

    private static EnemyUpdateContext Context(Rot boss, Battleground battleground) => new()
    {
        PlayerWorldX = boss.ArenaCenter.X + boss.ArenaRadius * .78f,
        PlayerWorldY = boss.ArenaCenter.Y,
        Battleground = battleground,
    };

    private static void StepRot(Rot boss, EnemyUpdateContext context)
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

    private static void ReachRotDeclarations(Rot boss, EnemyUpdateContext context, int count)
    {
        for (int tick = 0; tick < 1800 && boss.PhaseDeclarations < count; tick++)
            StepRot(boss, context);
        Assert.True(boss.PhaseDeclarations >= count);
    }

    private sealed record RotPressure(int PeakProjectiles, int OverflowCount,
        int ThreatCount, IReadOnlySet<string> ThreatOwners);

    private static RotPressure SimulateRotPressure(int phase, Vector2 playerOffset,
        double duration = 30.0, int seed = 300)
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        float bodySize = Simulation.TileSize * (float)Rot.RotConfig.FinalBodyScale;
        float spawnX = battleground.Width * Simulation.TileSize / 2f - bodySize / 2f;
        float spawnY = battleground.Height * Simulation.TileSize / 2f - bodySize / 2f;
        var boss = new Rot(spawnX, spawnY, battleground, new Random(seed + phase));
        boss.DebugSetPhase(phase);
        boss.EntranceRemaining = 0;
        var playerCenter = boss.ArenaCenter + new Vector2(
            playerOffset.X * boss.ArenaRadius, playerOffset.Y * boss.ArenaRadius);
        int playerSize = (int)(Simulation.TileSize * .75f);
        var playerRect = new Rectangle((int)(playerCenter.X - playerSize / 2f),
            (int)(playerCenter.Y - playerSize / 2f), playerSize, playerSize);
        var context = new EnemyUpdateContext
        {
            PlayerWorldX = playerCenter.X,
            PlayerWorldY = playerCenter.Y,
            Battleground = battleground,
        };
        var threatening = new HashSet<EnemyProjectile>();
        var owners = new HashSet<string>();
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
                if (!boss.ProjectileWithinArenaBounds(center))
                    projectile.RemFlag = true;
                if (projectile.Collides(playerRect) && threatening.Add(projectile) && projectile.Owner is not null)
                    owners.Add(projectile.Owner);
                projectile.Update(battleground, casualMode: false);
                if (projectile.Collides(playerRect) && threatening.Add(projectile) && projectile.Owner is not null)
                    owners.Add(projectile.Owner);
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

        return new RotPressure(peak, overflow, threatening.Count, owners);
    }

    [Fact]
    public void Constructor_UsesTouchIdentityAndSluggishFinalBalance()
    {
        var boss = new Rot(1000, 1000, MakeBattleground(), new Random(1));

        Assert.Equal(330000, boss.MaxHp);
        Assert.Equal("ROT", boss.BossDisplayName);
        Assert.Equal("SEEP", boss.PhaseLabel);
        Assert.Equal("THE BURIED ANCIENT", Rot.RotConfig.Subtitle);
        Assert.Equal(16, Rot.AbsorptionParticleCount);
        Assert.Equal(22, Rot.FinaleAbsorptionParticleCount);
        Assert.True(Rot.RotConfig.FinalBodyScale > Malady.MaladyConfig.FinalBodyScale);
        Assert.Equal("square", Rot.RotConfig.ArenaShape);
        Assert.True(Rot.RotConfig.MovementSpeed < .05);
        Assert.True(Rot.RotConfig.FinalCooldownSeconds > Malady.MaladyConfig.FinalCooldownSeconds);
        Assert.True(Rot.RotConfig.PhaseLabels.Count < Malady.MaladyConfig.PhaseLabels.Count);
        Assert.True(Rot.ActiveBurdenSoftCap < GameSession.MaxBossProjectiles);
    }

    [Fact]
    public void HalfHealth_StartsChokingStillnessAndBlocksDamage()
    {
        var battleground = MakeBattleground();
        var boss = new Rot(1000, 1000, battleground, new Random(2));
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(3);
        boss.DebugPhaseLocked = false;

        boss.TakeDamage(boss.MaxHp);
        Assert.False(boss.MidpointSurvivalActive);
        ReachRotDeclarations(boss, Context(boss, battleground), 2);
        boss.Update(Context(boss, battleground));

        Assert.True(boss.MidpointSurvivalActive);
        Assert.Equal("CHOKING STILLNESS", boss.PhaseLabel);
        Assert.Equal(boss.MaxHp / 2, boss.Hp);
        Assert.True(boss.TakeDamage(5000).Blocked);
    }

    [Fact]
    public void HugeOpeningHitStopsAtSeepGate()
    {
        var boss = new Rot(1000, 1000, MakeBattleground(), new Random(2));
        boss.EntranceRemaining = 0;

        boss.TakeDamage(boss.MaxHp);

        Assert.Equal((int)Math.Round(boss.MaxHp * .84), boss.Hp);
        Assert.False(boss.MidpointSurvivalActive);
    }

    [Fact]
    public void RotCreatesGroundLavaWithLongWarningsAndOuterSafeCorridor()
    {
        var battleground = MakeBattleground();
        var boss = new Rot(1000, 1000, battleground, new Random(3));
        var context = Context(boss, battleground);
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(5);

        for (int tick = 0; tick < 350 && context.ProjectileSink.Count == 0; tick++)
            boss.Update(context);

        var pools = context.ProjectileSink.Where(shot => shot.Owner == "rot_touch_floor_lava").ToList();
        Assert.NotEmpty(pools);
        Assert.All(pools, pool =>
        {
            Assert.Equal("pool", pool.Path);
            Assert.True(pool.TelegraphDuration >= 1.6f);
            float angle = MathF.Atan2(pool.WorldY + pool.Size / 2f - boss.ArenaCenter.Y,
                pool.WorldX + pool.Size / 2f - boss.ArenaCenter.X);
            float difference = MathF.Abs(MathF.Atan2(MathF.Sin(angle - boss.SafeCorridorAngle), MathF.Cos(angle - boss.SafeCorridorAngle)));
            Assert.True(difference >= .65f);
        });
    }

    [Fact]
    public void SquareArenaProjectileBoundsIncludeCornersButRejectOutsidePoints()
    {
        var boss = new Rot(1000, 1000, MakeBattleground(), new Random(3));

        Assert.True(boss.ProjectileWithinArenaBounds(
            boss.ArenaCenter + new Vector2(boss.ArenaRadius, boss.ArenaRadius)));
        Assert.False(boss.ProjectileWithinArenaBounds(
            boss.ArenaCenter + new Vector2(boss.ArenaRadius * 1.08f, 0)));
    }

    [Fact]
    public void SiltFrontKeepsItsShotsOutOfTheCommittedCorridor()
    {
        var battleground = MakeBattleground();
        var boss = new Rot(1000, 1000, battleground, new Random(8));
        var context = Context(boss, battleground);
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(2);

        for (int tick = 0; tick < 400 && !context.ProjectileSink.Any(
                 shot => shot.Owner == "rot_touch_silt_front"); tick++)
            boss.Update(context);

        var front = context.ProjectileSink.Where(shot => shot.Owner == "rot_touch_silt_front").ToList();
        Assert.NotEmpty(front);
        Assert.All(front, shot =>
        {
            float difference = MathF.Abs(MathF.Atan2(
                MathF.Sin(shot.Direction - boss.SafeCorridorAngle),
                MathF.Cos(shot.Direction - boss.SafeCorridorAngle)));
            Assert.True(difference > .6f);
        });
    }

    [Fact]
    public void MinorDamageMovementsRetainAccumulatedTerrain()
    {
        var battleground = MakeBattleground();
        var boss = new Rot(1000, 1000, battleground, new Random(9));
        var context = Context(boss, battleground);
        boss.EntranceRemaining = 0;
        boss.TransitionCleanupRequested = false;

        boss.TakeDamage(boss.MaxHp);
        ReachRotDeclarations(boss, context, 2);
        boss.Update(context);

        Assert.Equal(2, boss.Phase);
        Assert.False(boss.TransitionCleanupRequested);
    }

    [Fact]
    public void MiasmaDamageMovementMustDeclareTwiceBeforeBurial()
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var boss = new Rot(1000, 1000, battleground, new Random(10));
        var context = Context(boss, battleground);
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(6);

        boss.TakeDamage(boss.MaxHp);
        Assert.Equal(1, boss.Hp);
        Assert.False(boss.FinaleActive);

        ReachRotDeclarations(boss, context, 2);
        boss.TakeDamage(1);

        Assert.True(boss.FinaleActive);
        Assert.Equal(7, boss.Phase);
    }

    [Theory]
    [InlineData(2, .67, 3)]
    [InlineData(5, .25, 6)]
    public void IntermediateDamageGatesCannotSkipBeforeTwoDeclarations(
        int phase, double floorRatio, int nextPhase)
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var boss = new Rot(1000, 1000, battleground, new Random(12 + phase));
        var context = Context(boss, battleground);
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(phase);
        boss.DebugPhaseLocked = false;

        boss.TakeDamage(boss.MaxHp);
        Assert.Equal((int)Math.Round(boss.MaxHp * floorRatio), boss.Hp);
        Assert.Equal(phase, boss.Phase);

        ReachRotDeclarations(boss, context, 2);
        boss.Update(context);

        Assert.Equal(nextPhase, boss.Phase);
    }

    [Fact]
    public void PathedBloomDeclaresItsInitialCorridorAtTheRealPlayer()
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var boss = new Rot(1000, 1000, battleground, new Random(19));
        Vector2 player = boss.ArenaCenter + new Vector2(
            MathF.Cos(1.35f), MathF.Sin(1.35f)) * boss.ArenaRadius * .76f;
        var context = new EnemyUpdateContext
        {
            PlayerWorldX = player.X,
            PlayerWorldY = player.Y,
            Battleground = battleground,
        };
        boss.DebugSetPhase(5);
        boss.EntranceRemaining = 0;

        for (int tick = 0; tick < 500 && boss.PatternRotation == 0; tick++)
            boss.Update(context);

        float difference = MathF.Abs(MathF.Atan2(
            MathF.Sin(boss.SafeCorridorAngle - 1.35f),
            MathF.Cos(boss.SafeCorridorAngle - 1.35f)));
        Assert.InRange(difference, 0f, .02f);
    }

    [Fact]
    public void FollowingThreeGeologicalCorridorTurnsShedsOldBurden()
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var boss = new Rot(1000, 1000, battleground, new Random(11));
        var context = Context(boss, battleground);
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(5);

        for (int tick = 0; tick < Simulation.FrameRate * 20 &&
                           boss.ReliefTriggers == 0; tick++)
        {
            StepRot(boss, context);
            context = new EnemyUpdateContext
            {
                PlayerWorldX = boss.ArenaCenter.X +
                    MathF.Cos(boss.SafeCorridorAngle) * boss.ArenaRadius * .78f,
                PlayerWorldY = boss.ArenaCenter.Y +
                    MathF.Sin(boss.SafeCorridorAngle) * boss.ArenaRadius * .78f,
                Battleground = battleground,
                ProjectileSink = context.ProjectileSink,
            };
        }

        Assert.Equal(1, boss.ReliefTriggers);
        Assert.Equal(0, boss.ReliefProgress);
        Assert.True(boss.ReliefHazardsCleared >= 1);
        Assert.True(boss.ReliefPulseRemaining > 0);
    }

    [Fact]
    public void HoldingTheOriginalBankDoesNotEarnRepeatedRelief()
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var boss = new Rot(1000, 1000, battleground, new Random(11));
        var context = Context(boss, battleground);
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(5);

        for (int tick = 0; tick < Simulation.FrameRate * 16; tick++)
            StepRot(boss, context);

        Assert.Equal(0, boss.ReliefTriggers);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void EveryDamageMovementEventuallyPressuresAStationaryCornerWithoutOverflow(int phase)
    {
        var pressure = SimulateRotPressure(phase, new Vector2(.90f, .90f));

        Assert.True(pressure.ThreatCount >= 1,
            $"Phase {phase} never threatened a player holding a square-arena corner.");
        Assert.Equal(0, pressure.OverflowCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void GroundPressureReachesAnEdgeCamperThroughRotSpecificHazards(int phase)
    {
        var pressure = SimulateRotPressure(phase, new Vector2(.93f, 0));

        Assert.True(pressure.ThreatOwners.Any(owner =>
                owner is "rot_touch_floor_lava" or "rot_touch_bomb"),
            $"Phase {phase} did not carry a sludge bank or falling mass to the outer edge.");
        Assert.Equal(0, pressure.OverflowCount);
    }

    [Fact]
    public void Burial_IsThirtyFiveSecondsThenTenSecondExpandingDeath()
    {
        var battleground = MakeBattleground();
        var boss = new Rot(1000, 1000, battleground, new Random(4));
        var context = Context(boss, battleground);
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(7);

        Assert.True(boss.FinaleActive);
        Assert.Equal(35.0, boss.FinaleRemaining);
        Assert.Equal(1, boss.BurialLayerCount);
        Assert.True(boss.TakeDamage(1000).Blocked);

        for (int tick = 0; tick < Simulation.FrameRate * 18; tick++)
            StepRot(boss, context);
        Assert.InRange(boss.BurialLayerCount, 3, Rot.BurialStrataCount);

        for (int tick = 0; tick < 5000 && !boss.Dying; tick++)
            StepRot(boss, context);
        Assert.True(boss.Dying);
        Assert.Equal(10.0, boss.DeathDuration);

        for (int tick = 0; tick < 1300 && !boss.IsDead(); tick++)
            boss.Update(context);
        Assert.True(boss.IsDead());
    }
}
