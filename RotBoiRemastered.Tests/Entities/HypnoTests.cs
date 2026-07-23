using Microsoft.Xna.Framework;
using RotBoiRemastered.Core;
using RotBoiRemastered.Entities;
using RotBoiRemastered.Systems;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.Entities;

public class HypnoTests
{
    private static Battleground MakeBattleground() => Battleground.GenerateSound();

    private static Hypno MakeBoss(Battleground battleground, int seed = 1)
    {
        float size = Simulation.TileSize * (float)Hypno.HypnoConfig.BodyScale;
        return new Hypno(
            battleground.Width * Simulation.TileSize / 2f - size / 2f,
            battleground.Height * Simulation.TileSize / 2f - size / 2f,
            battleground, new Random(seed));
    }

    private static EnemyUpdateContext MakeContext(Hypno boss, Battleground battleground,
        Vector2? offset = null)
    {
        Vector2 playerOffset = offset ?? new Vector2(boss.ArenaRadius * .76f, 0);
        return new EnemyUpdateContext
        {
            PlayerWorldX = boss.ArenaCenter.X + playerOffset.X,
            PlayerWorldY = boss.ArenaCenter.Y + playerOffset.Y,
            Battleground = battleground,
            DreamState = new DreamState(),
        };
    }

    private static void Step(Hypno boss, EnemyUpdateContext context)
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

    private static void ReachDeclarations(Hypno boss, EnemyUpdateContext context, int count)
    {
        for (int tick = 0; tick < 1800 && boss.PhaseDeclarations < count; tick++)
            Step(boss, context);
        Assert.True(boss.PhaseDeclarations >= count);
    }

    private sealed record Pressure(int Peak, int Overflow, int Hits,
        IReadOnlySet<string> Owners);

    private static Pressure SimulatePressure(int phase, double duration = 16.0)
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var boss = MakeBoss(battleground, 200 + phase);
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(phase);
        var context = MakeContext(boss, battleground);
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
            Step(boss, context);
            foreach (var projectile in context.ProjectileSink)
            {
                if (!projectile.Collides(playerRect) || projectile.Illusory ||
                    !threats.Add(projectile) || projectile.Owner is null)
                    continue;
                owners.Add(projectile.Owner);
            }
            peak = Math.Max(peak, context.ProjectileSink.Count);
            if (context.ProjectileSink.Count > GameSession.MaxBossProjectiles)
                overflow += context.ProjectileSink.Count - GameSession.MaxBossProjectiles;
        }
        return new Pressure(peak, overflow, threats.Count, owners);
    }

    [Fact]
    public void Constructor_UsesAuthoredMidpointStatsAndFiveMovements()
    {
        var boss = MakeBoss(MakeBattleground());

        Assert.Equal(107000, boss.MaxHp);
        Assert.Equal(360, boss.Damage);
        Assert.Equal(1, boss.Phase);
        Assert.Equal("IDOL", boss.PhaseLabel);
        Assert.Equal(14.0, Hypno.ChosenSurvivalDuration);
        Assert.Equal(2, Hypno.MinimumDamagePhaseDeclarations);
    }

    [Fact]
    public void PreSurvivalHealthGatesRequireTwoCompleteSuggestions()
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var boss = MakeBoss(battleground);
        boss.EntranceRemaining = 0;
        var context = MakeContext(boss, battleground);
        double[] floors = { .75, .625, .50 };

        for (int phase = 1; phase <= 3; phase++)
        {
            context.ProjectileSink.Clear();
            boss.TakeDamage(boss.MaxHp);
            Assert.Equal((int)Math.Round(boss.MaxHp * floors[phase - 1]), boss.Hp);
            Assert.Equal(phase, boss.Phase);
            Assert.False(boss.ChosenSurvivalActive);

            ReachDeclarations(boss, context, Hypno.MinimumDamagePhaseDeclarations);
            boss.TakeDamage(1);
            Assert.Equal(phase + 1, boss.Phase);
        }

        Assert.True(boss.ChosenSurvivalActive);
    }

    [Fact]
    public void ChosenIsFourteenSecondInvulnerableLessonThenOffering()
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var boss = MakeBoss(battleground, 3);
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(3);
        boss.DebugPhaseLocked = false;
        var context = MakeContext(boss, battleground);

        boss.TakeDamage(boss.MaxHp);
        ReachDeclarations(boss, context, 2);
        boss.TakeDamage(1);

        Assert.True(boss.ChosenSurvivalActive);
        Assert.Equal(4, boss.Phase);
        Assert.Equal("CHOSEN", boss.PhaseLabel);
        Assert.Equal(14.0, boss.ChosenSurvivalRemaining);
        Assert.True(boss.TakeDamage(1000).Blocked);

        for (int tick = 0; tick < Simulation.FrameRate * 14 + 5; tick++)
            Step(boss, context);

        Assert.False(boss.ChosenSurvivalActive);
        Assert.True(boss.ChosenSurvivalCleared);
        Assert.Equal(5, boss.Phase);
        Assert.Equal("OFFERING", boss.PhaseLabel);
        Assert.Equal(4, boss.OfferingPositions.Count);
    }

    [Fact]
    public void OfferingCannotBeBurstDownBeforeTwoDebtDeclarations()
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var boss = MakeBoss(battleground);
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(5);
        boss.DebugPhaseLocked = false;
        var context = MakeContext(boss, battleground);

        boss.TakeDamage(boss.MaxHp);
        Assert.Equal(1, boss.Hp);
        Assert.False(boss.Dying);

        ReachDeclarations(boss, context, 2);
        boss.TakeDamage(1);
        Assert.True(boss.Dying);
        Assert.False(boss.IsDead());
    }

    private static void FireUntilProjectiles(Hypno boss, EnemyUpdateContext context)
    {
        for (int tick = 0; tick < 500 && context.ProjectileSink.Count == 0; tick++)
            boss.Update(context);
        Assert.NotEmpty(context.ProjectileSink);
    }

    [Fact]
    public void Idol_TwoOfThreeShrinesAreIllusory()
    {
        var battleground = MakeBattleground();
        var boss = MakeBoss(battleground);
        var context = MakeContext(boss, battleground);

        FireUntilProjectiles(boss, context);

        var shots = context.ProjectileSink.Where(p => p.Owner == "hypno_phantasia_idol").ToList();
        Assert.Contains(shots, p => p.Illusory);
        Assert.Contains(shots, p => !p.Illusory && p.TruthMarked);
    }

    [Fact]
    public void SpokenRule_ContradictionRevealsTheTrueSigil()
    {
        var battleground = MakeBattleground();
        var boss = MakeBoss(battleground);
        boss.DebugSetPhase(2);
        boss.EntranceRemaining = 0;
        var context = MakeContext(boss, battleground);

        for (int pattern = 0; pattern < 3; pattern++)
        {
            context.ProjectileSink.Clear();
            boss.AttackCooldown = 0;
            boss.Update(context);
        }

        Assert.Equal("REMAIN", boss.RuleText);
        Assert.Contains(context.ProjectileSink,
            projectile => projectile.Owner == "hypno_phantasia_spoken_rule" && projectile.Illusory);
        Assert.Contains(context.ProjectileSink,
            projectile => projectile.Owner == "hypno_phantasia_true_sigil" && !projectile.Illusory);
    }

    [Fact]
    public void Inheritance_FracturesAcrossTwoDescendantGenerations()
    {
        var battleground = MakeBattleground();
        var boss = MakeBoss(battleground);
        boss.DebugSetPhase(3);
        var context = MakeContext(boss, battleground);

        FireUntilProjectiles(boss, context);

        var shots = context.ProjectileSink.Where(p => p.Owner == "hypno_phantasia_lineage").ToList();
        Assert.NotEmpty(shots);
        Assert.All(shots, shot =>
        {
            Assert.Equal(2, shot.SplitCount);
            Assert.Equal(1, shot.SplitGeneration);
        });
    }

    [Fact]
    public void Chosen_PresentsARealVolleyBesideAHarmlessCage()
    {
        var battleground = MakeBattleground();
        var boss = MakeBoss(battleground);
        boss.DebugSetPhase(4);
        var context = MakeContext(boss, battleground);

        FireUntilProjectiles(boss, context);

        Assert.Contains(context.ProjectileSink,
            projectile => projectile.Owner == "hypno_phantasia_chosen" && !projectile.Illusory);
        Assert.Contains(context.ProjectileSink,
            projectile => projectile.Owner == "hypno_phantasia_spared" && projectile.Illusory);
    }

    [Fact]
    public void Offering_CombinesAlternatingRingsWithRealDebt()
    {
        var battleground = MakeBattleground();
        var boss = MakeBoss(battleground);
        boss.DebugSetPhase(5);
        var context = MakeContext(boss, battleground);

        FireUntilProjectiles(boss, context);

        Assert.Contains(context.ProjectileSink,
            projectile => projectile.Owner == "hypno_phantasia_offering");
        Assert.Contains(context.ProjectileSink,
            projectile => projectile.Owner == "hypno_phantasia_debt" && !projectile.Illusory);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void EveryMovementThreatensAnOuterPlayerWithoutOverflow(int phase)
    {
        var pressure = SimulatePressure(phase);

        Assert.True(pressure.Hits > 0,
            $"Hypno phase {phase} never threatened the outer player. Peak={pressure.Peak}.");
        Assert.InRange(pressure.Peak, 1, 64);
        Assert.Equal(0, pressure.Overflow);
    }
}
