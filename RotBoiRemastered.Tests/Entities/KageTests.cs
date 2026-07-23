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
        Assert.Equal("FEAST", kage.PhaseLabel);
        Assert.Equal(
            new[] { "FEAST", "PROVOCATION", "STAGNANT MIRROR", "LURE" },
            Kage.KageConfig.PhaseLabels);
        Assert.Equal(14.0, Kage.StagnantMirrorDuration);
    }

    [Fact]
    public void DamageGatesRequireTwoCompositeDeclarations()
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var kage = MakeCenteredKage(battleground);
        var context = MakeContext(kage, battleground);
        kage.EntranceRemaining = 0;

        kage.TakeDamage(kage.MaxHp);
        Assert.Equal((int)Math.Round(kage.MaxHp * .75), kage.Hp);
        Assert.Equal(1, kage.Phase);

        ReachDeclarations(kage, context, 2);
        Step(kage, context);
        Assert.Equal(2, kage.Phase);

        kage.TakeDamage(kage.MaxHp);
        Assert.Equal(kage.MaxHp / 2, kage.Hp);
        Assert.False(kage.StagnantMirrorActive);

        ReachDeclarations(kage, context, 2);
        Step(kage, context);
        Assert.True(kage.StagnantMirrorActive);
        Assert.Equal(3, kage.Phase);
    }

    [Fact]
    public void StagnantMirrorIsFourteenSecondInvulnerableSurvivalThenLure()
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var kage = MakeCenteredKage(battleground, 4);
        var context = MakeContext(kage, battleground);
        kage.DebugSetPhase(2);
        kage.DebugPhaseLocked = false;
        kage.EntranceRemaining = 0;
        kage.TakeDamage(kage.MaxHp);
        ReachDeclarations(kage, context, 2);
        Step(kage, context);

        Assert.True(kage.StagnantMirrorActive);
        Assert.Equal(14.0, kage.StagnantMirrorRemaining);
        Assert.True(kage.TakeDamage(1000).Blocked);

        for (int tick = 0; tick < Simulation.FrameRate * 14 + 5; tick++)
            Step(kage, context);

        Assert.False(kage.StagnantMirrorActive);
        Assert.True(kage.StagnantMirrorCleared);
        Assert.Equal(4, kage.Phase);
        Assert.Equal("LURE", kage.PhaseLabel);
    }

    [Fact]
    public void LureCannotBeBurstDownBeforeTwoSynthesisPatterns()
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var kage = MakeCenteredKage(battleground, 5);
        var context = MakeContext(kage, battleground);
        kage.DebugSetPhase(4);
        kage.DebugPhaseLocked = false;
        kage.EntranceRemaining = 0;

        kage.TakeDamage(kage.MaxHp);
        Assert.Equal(1, kage.Hp);
        Assert.False(kage.Dying);

        ReachDeclarations(kage, context, 2);
        kage.TakeDamage(1);
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
            projectile.Path == "sine");
        Assert.Contains(context.ProjectileSink, projectile =>
            projectile.Owner == "kage_chemesthesis_stagnation" &&
            projectile.Path == "mine");
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
