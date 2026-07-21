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

    private sealed record AchePressure(int PeakProjectiles, int PeakPersistentHazards,
        int OverflowCount, IReadOnlySet<int> HazardQuadrants, IReadOnlySet<string> Owners,
        IReadOnlyList<string> SpawnSignature, IReadOnlyList<(string Owner, float Damage)> SpawnedShots);

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

    [Fact]
    public void Constructor_UsesCorrectChemesthesisIdentityAndBalance()
    {
        var boss = new Ache(1000, 1000, MakeBattleground(), new Random(1));

        Assert.Equal(280000, boss.MaxHp);
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
        Assert.True(boss.FinaleDuration < malady.FinaleDuration);
        Assert.Equal(8, Ache.AcheConfig.PhaseLabels.Count);
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

    [Fact]
    public void ChaosPatternsRemainSparseScatteredAndUseNoPortals()
    {
        var battleground = MakeBattleground();
        var boss = new Ache(1000, 1000, battleground, new Random(4));
        var pressure = SimulatePressure(7, seed: 4);

        Assert.DoesNotContain(pressure.Owners, owner => owner.Contains("portal"));
        Assert.Contains(pressure.Owners, owner => owner.Contains("predicted_lash") || owner.Contains("crossed_nerves"));
        Assert.Contains(pressure.Owners, owner => owner.Contains("mine") || owner.Contains("pocket") || owner.Contains("cluster"));
        Assert.InRange(pressure.PeakProjectiles, 1, 40);
        Assert.InRange(pressure.PeakPersistentHazards, 1, 26);
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

            Assert.InRange(pressure.PeakProjectiles, 1, 40);
            Assert.InRange(pressure.PeakPersistentHazards, 1, 26);
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
    public void Overload_IsTwentyEightSecondsThenTenSecondDeath()
    {
        var battleground = MakeBattleground();
        var boss = new Ache(1000, 1000, battleground, new Random(5));
        var context = Context(boss, battleground);
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(8);

        Assert.True(boss.FinaleActive);
        Assert.Equal(28.0, boss.FinaleRemaining);
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

public class RotTests
{
    private static Battleground MakeBattleground() => Battleground.GenerateSound();

    private static EnemyUpdateContext Context(Rot boss, Battleground battleground) => new()
    {
        PlayerWorldX = boss.ArenaCenter.X + boss.ArenaRadius * .78f,
        PlayerWorldY = boss.ArenaCenter.Y,
        Battleground = battleground,
    };

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
    public void Burial_IsFortySecondsThenTenSecondExpandingDeath()
    {
        var battleground = MakeBattleground();
        var boss = new Rot(1000, 1000, battleground, new Random(4));
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
