using Microsoft.Xna.Framework;
using RotBoiRemastered.Core;
using RotBoiRemastered.Entities;
using RotBoiRemastered.Systems;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.Entities;

public class KageTests
{
    private static Battleground MakeBattleground() => Battleground.GenerateSound();

    private static Kage MakeCenteredKage(Battleground battleground, int seed = 1)
    {
        float size = Simulation.TileSize * (float)Kage.KageConfig.BodyScale;
        return new Kage(
            battleground.Width * Simulation.TileSize / 2f - size / 2f,
            battleground.Height * Simulation.TileSize / 2f - size / 2f,
            battleground, new Random(seed));
    }

    private static EnemyUpdateContext MakeContext(Kage boss, Battleground battleground,
        Vector2? arenaOffset = null)
    {
        Vector2 offset = arenaOffset ?? new Vector2(.76f, 0);
        Vector2 player = boss.ArenaCenter + new Vector2(
            offset.X * boss.ArenaRadius, offset.Y * boss.ArenaRadius);
        return new EnemyUpdateContext
        {
            PlayerWorldX = player.X,
            PlayerWorldY = player.Y,
            Battleground = battleground,
            BossAfflictions = new BossAfflictions(),
        };
    }

    private static void Step(Kage boss, EnemyUpdateContext context)
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

    private static void ReachDeclarations(Kage boss, EnemyUpdateContext context, int count)
    {
        for (int tick = 0; tick < 1800 && boss.KagePhaseDeclarations < count; tick++)
            Step(boss, context);
        Assert.True(boss.KagePhaseDeclarations >= count);
    }

    private sealed record KagePressure(int Peak, int Overflow, int Threats,
        IReadOnlySet<string> Owners);

    private static KagePressure SimulatePressure(int phase, Vector2 playerOffset,
        double duration = 18.0, int seed = 300)
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var boss = MakeCenteredKage(battleground, seed + phase);
        boss.DebugSetPhase(phase);
        boss.EntranceRemaining = 0;
        var context = MakeContext(boss, battleground, playerOffset);
        int playerSize = (int)(Simulation.TileSize * .75f);
        var playerRect = new Rectangle(
            (int)(context.PlayerWorldX - playerSize / 2f),
            (int)(context.PlayerWorldY - playerSize / 2f),
            playerSize, playerSize);
        var threats = new HashSet<EnemyProjectile>();
        var owners = new HashSet<string>();
        int peak = 0, overflow = 0;

        for (int tick = 0; tick < duration * Simulation.FrameRate; tick++)
        {
            boss.Update(context);
            var children = new List<EnemyProjectile>();
            foreach (var projectile in context.ProjectileSink.ToList())
            {
                if (projectile.Collides(playerRect) && threats.Add(projectile) &&
                    projectile.Owner is not null)
                    owners.Add(projectile.Owner);
                projectile.Update(battleground, casualMode: false);
                if (projectile.Collides(playerRect) && threats.Add(projectile) &&
                    projectile.Owner is not null)
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
                context.ProjectileSink.RemoveRange(
                    0, context.ProjectileSink.Count - GameSession.MaxBossProjectiles);
            }
        }
        return new KagePressure(peak, overflow, threats.Count, owners);
    }

    [Fact]
    public void Constructor_UsesAuthoredMidpointStatsAndFourMovements()
    {
        var kage = new Kage(1000, 1000, MakeBattleground(), new Random(1));

        Assert.Equal(93000, kage.MaxHp);
        Assert.Equal(340, kage.Damage);
        Assert.Equal("SPARK / FUEL", kage.PhaseLabel);
        Assert.Equal(
            new[] { "SPARK / FUEL", "PRESSURE / HEAT", "SOLVENT / CRYSTAL", "CHAIN REACTION", "CRITICAL MIXTURE" },
            Kage.KageConfig.PhaseLabels);
        Assert.Equal(20.0, Kage.StagnantMirrorDuration);
    }

    [Fact]
    public void EveryReactionSurrendersOnlyItsOwnBudgetThenRunsItsClock()
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var kage = MakeCenteredKage(battleground);
        var context = MakeContext(kage, battleground);
        kage.EntranceRemaining = 0;
        int opening = kage.Phase;

        kage.TakeDamage(kage.MaxHp);
        Assert.Equal(
            kage.MaxHp - (int)Math.Round(kage.MaxHp * BossPhaseGovernor.DefaultThresholdFraction),
            kage.Hp);
        Assert.Equal(opening, kage.Phase);

        // A level-ten encounter releases the phase seven seconds after the
        // threshold rather than riding the whole clock.
        for (int tick = 0;
             tick < Simulation.FrameRate * (BossPhaseGovernor.LowerTierHoldSeconds + 1); tick++)
            Step(kage, context);
        Assert.NotEqual(opening, kage.Phase);
        Assert.False(kage.StagnantMirrorActive);
    }

    [Fact]
    public void StagnantMirrorIsTheClosingTwentySecondSurvivalThatEndsTheFight()
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var kage = MakeCenteredKage(battleground, 4);
        var context = MakeContext(kage, battleground);
        kage.EntranceRemaining = 0;
        kage.Hp = 1;
        kage.DebugRebasePhaseHealth();
        Step(kage, context);

        Assert.True(kage.StagnantMirrorActive);
        Assert.Equal(20.0, kage.StagnantMirrorRemaining);
        Assert.True(kage.TakeDamage(1000).Blocked);

        // The survival starts behind a phase interlude: the arena is swept
        // clear and the boss walks back to centre before the clock runs.
        for (int tick = 0;
             tick < Simulation.FrameRate * (20 + BossPhaseInterlude.DefaultDuration) + 5;
             tick++)
            Step(kage, context);

        Assert.False(kage.StagnantMirrorActive);
        Assert.True(kage.StagnantMirrorCleared);
        Assert.True(kage.Dying);
    }

    [Fact]
    public void FeastUsesLongLivedSpreadingMines()
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var kage = MakeCenteredKage(battleground);
        var context = MakeContext(kage, battleground);
        kage.EntranceRemaining = 0;

        for (int tick = 0; tick < 400 && context.ProjectileSink.Count == 0; tick++)
            kage.Update(context);

        var feast = context.ProjectileSink.Where(projectile =>
            projectile.Owner == "kage_chemesthesis_feast").ToList();
        Assert.Equal(5, feast.Count);
        Assert.All(feast, projectile =>
        {
            Assert.Equal("mine", projectile.Path);
            Assert.True(projectile.Lifetime >= 18f);
        });
    }

    [Fact]
    public void ProvocationAimsThenDeclaresItsRearRetort()
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var kage = MakeCenteredKage(battleground);
        var context = MakeContext(kage, battleground);
        kage.DebugSetPhase(2);
        kage.EntranceRemaining = 0;

        for (int tick = 0; tick < 400 && context.ProjectileSink.Count == 0; tick++)
            kage.Update(context);

        Assert.Contains(context.ProjectileSink,
            projectile => projectile.Owner == "kage_chemesthesis_provocation");
        var retort = Assert.Single(context.ProjectileSink,
            projectile => projectile.Owner == "kage_chemesthesis_retort");
        Assert.Equal("laser", retort.Path);
        Assert.True(retort.TelegraphDuration >= .8f);
    }

    [Fact]
    public void StagnantMirrorCombinesSlowReflectionsAndSettlingMines()
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var kage = MakeCenteredKage(battleground);
        var context = MakeContext(kage, battleground);
        kage.DebugSetPhase(3);
        kage.EntranceRemaining = 0;

        for (int tick = 0; tick < 400 && context.ProjectileSink.Count == 0; tick++)
            kage.Update(context);

        Assert.Contains(context.ProjectileSink, projectile =>
            projectile.Owner == "kage_chemesthesis_stagnant_mirror" &&
            projectile.Path == "sine" &&
            projectile.Amplitude >= Simulation.TileSize);
        Assert.Contains(context.ProjectileSink, projectile =>
            projectile.Owner == "kage_chemesthesis_stagnation" &&
            projectile.Path == "mine");
        var snap = Assert.Single(context.ProjectileSink, projectile =>
            projectile.Owner == "kage_chemesthesis_mirror_snap");
        Assert.Equal("bomb", snap.Path);
        Assert.Equal(new Vector2(context.PlayerWorldX, context.PlayerWorldY),
            snap.Target);
    }

    [Fact]
    public void LureCombinesWideLanesWithAPlayerMarkedBomb()
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var kage = MakeCenteredKage(battleground);
        var context = MakeContext(kage, battleground);
        kage.DebugSetPhase(4);
        kage.EntranceRemaining = 0;

        for (int tick = 0; tick < 400 && context.ProjectileSink.Count == 0; tick++)
            kage.Update(context);

        Assert.Contains(context.ProjectileSink,
            projectile => projectile.Owner == "kage_chemesthesis_lure");
        var bomb = Assert.Single(context.ProjectileSink,
            projectile => projectile.Owner == "kage_chemesthesis_lure_reward");
        Assert.Equal("bomb", bomb.Path);
        Assert.Equal(new Vector2(context.PlayerWorldX, context.PlayerWorldY), bomb.Target);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void EveryMovementThreatensAStationaryOuterPlayerWithoutOverflow(int phase)
    {
        var pressure = SimulatePressure(phase, new Vector2(.76f, 0));

        Assert.True(pressure.Threats >= 1,
            $"Kage phase {phase} did not threaten the stationary player. Peak={pressure.Peak}.");
        Assert.InRange(pressure.Peak, 1, 50);
        Assert.Equal(0, pressure.Overflow);
    }
}
