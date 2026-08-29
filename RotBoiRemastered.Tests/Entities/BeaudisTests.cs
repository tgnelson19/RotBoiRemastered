using Microsoft.Xna.Framework;
using RotBoiRemastered.Core;
using RotBoiRemastered.Entities;
using RotBoiRemastered.Systems;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.Entities;

public class BeaudisTests
{
    private static Battleground MakeBattleground() => Battleground.GenerateSound();

    private static Beaudis MakeBoss(Battleground? battleground = null, int seed = 1)
    {
        battleground ??= MakeBattleground();
        float size = Simulation.TileSize * 1.55f;
        return new Beaudis(
            battleground.Width * Simulation.TileSize / 2f - size / 2f,
            battleground.Height * Simulation.TileSize / 2f - size / 2f,
            awarenessRange: 500f, rng: new Random(seed));
    }

    private static EnemyUpdateContext MakeContext(Beaudis boss, Battleground battleground,
        Vector2? offset = null)
    {
        Vector2 playerOffset = offset ?? new Vector2(Simulation.TileSize * 8f, 0);
        return new EnemyUpdateContext
        {
            PlayerWorldX = boss.WorldX + boss.Size / 2f + playerOffset.X,
            PlayerWorldY = boss.WorldY + boss.Size / 2f + playerOffset.Y,
            Battleground = battleground,
        };
    }

    private static void Step(Beaudis boss, EnemyUpdateContext context)
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

    private static void ReachDeclarations(Beaudis boss, EnemyUpdateContext context, int count)
    {
        for (int tick = 0; tick < 1800 && boss.PhaseDeclarations < count; tick++)
            Step(boss, context);
        Assert.True(boss.PhaseDeclarations >= count);
    }

    private static void ClearPhaseProtection(Beaudis boss, EnemyUpdateContext context)
    {
        for (int tick = 0; tick < 70; tick++)
            Step(boss, context);
        Assert.Equal(0, boss.PhaseDeclarations);
    }

    private sealed record Pressure(int Peak, int Overflow, int Hits, IReadOnlySet<string> Owners);

    private static Pressure SimulatePressure(int phase, double duration = 15.0)
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var boss = MakeBoss(battleground, 100 + phase);
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(phase);
        var context = MakeContext(boss, battleground);
        int playerSize = (int)(Simulation.TileSize * .75f);
        var playerRect = new Rectangle(
            (int)(context.PlayerWorldX - playerSize / 2f),
            (int)(context.PlayerWorldY - playerSize / 2f),
            playerSize, playerSize);
        var hitThreats = new HashSet<EnemyProjectile>();
        var owners = new HashSet<string>();
        int peak = 0;
        int overflow = 0;

        for (int tick = 0; tick < duration * Simulation.FrameRate; tick++)
        {
            Step(boss, context);
            foreach (var projectile in context.ProjectileSink)
            {
                if (projectile.Collides(playerRect) && hitThreats.Add(projectile) &&
                    projectile.Owner is not null)
                    owners.Add(projectile.Owner);
            }
            peak = Math.Max(peak, context.ProjectileSink.Count);
            if (context.ProjectileSink.Count > GameSession.MaxBossProjectiles)
                overflow += context.ProjectileSink.Count - GameSession.MaxBossProjectiles;
        }
        return new Pressure(peak, overflow, hitThreats.Count, owners);
    }

    [Fact]
    public void Constructor_UsesAuthoredSoundMidpointStatsAndFiveMovements()
    {
        var boss = MakeBoss();

        Assert.Equal(50000, boss.MaxHp);
        Assert.Equal(220, boss.Damage);
        Assert.Equal(1, boss.Phase);
        Assert.Equal("APPROACH", boss.PhaseLabel);
        Assert.Equal(20.0, boss.SurvivalDuration);
        Assert.Equal(2, Beaudis.MinimumDamagePhaseDeclarations);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(5)]
    public void EveryGrammarSurrendersOnlyItsOwnBudgetThenRunsItsClock(int phase)
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var boss = MakeBoss(battleground);
        boss.EntranceRemaining = 0;
        var context = MakeContext(boss, battleground);

        boss.DebugSetPhase(phase);
        boss.DebugPhaseLocked = false;
        ClearPhaseProtection(boss, context);
        boss.DebugRebasePhaseHealth();
        int before = boss.Hp;

        boss.TakeDamage(boss.MaxHp);
        Assert.Equal(
            before - (int)Math.Round(boss.MaxHp * BossPhaseGovernor.DefaultThresholdFraction),
            boss.Hp);
        Assert.Equal(phase, boss.Phase);
        Assert.False(boss.Dying);

        // A level-ten encounter releases the phase seven seconds after the
        // threshold rather than riding the whole clock. The stagger this hit
        // also earns parks the clock while it plays, so allow for that.
        for (int tick = 0;
             tick < Simulation.FrameRate * 30 && boss.Phase == phase; tick++)
            Step(boss, context);
        Assert.NotEqual(phase, boss.Phase);
        Assert.True(boss.PhaseClockElapsed
            <= BossPhaseGovernor.LowerTierHoldSeconds + 1.0);
    }

    [Fact]
    public void InterferenceIsTheClosingTwentySecondSurvivalThatEndsTheFight()
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var boss = MakeBoss(battleground, 4);
        boss.EntranceRemaining = 0;
        var context = MakeContext(boss, battleground);
        ClearPhaseProtection(boss, context);
        boss.Hp = 2;
        boss.DebugRebasePhaseHealth();

        boss.TakeDamage(boss.MaxHp);

        Assert.Equal(3, boss.Phase);
        Assert.Equal("INTERFERENCE", boss.PhaseLabel);
        Assert.True(boss.SurvivalActive);
        Assert.Equal(4, boss.ProjectilePortals.Count);
        Assert.Equal(20.0, boss.SurvivalRemaining);
        Assert.True(boss.TakeDamage(1000).Blocked);

        for (int tick = 0;
             tick < Simulation.FrameRate * (20 + BossPhaseInterlude.DefaultDuration) + 5;
             tick++)
            Step(boss, context);

        Assert.False(boss.SurvivalActive);
        Assert.True(boss.Dying);
    }

    [Fact]
    public void StaggerStillRewardsSustainedDirectHits()
    {
        var boss = MakeBoss();
        for (int hit = 0; hit < 22; hit++)
            boss.TakeDamage(1);
        Assert.False(boss.IsStaggered);

        boss.TakeDamage(1);

        Assert.True(boss.IsStaggered);
        int hpBefore = boss.Hp;
        var result = boss.TakeDamage(10);
        Assert.Equal(12, result.Amount);
        Assert.Equal(hpBefore - 12, boss.Hp);
    }

    [Theory]
    [InlineData(1, "beaudis_call")]
    [InlineData(2, "beaudis_answer_left")]
    [InlineData(4, "beaudis_press")]
    [InlineData(5, "beaudis_shot")]
    public void EachDamageMovementHasItsOwnSoundPhrase(int phase, string expectedOwner)
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var boss = MakeBoss(battleground);
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(phase);
        var context = MakeContext(boss, battleground);

        ReachDeclarations(boss, context, 1);

        Assert.Contains(context.ProjectileSink, projectile => projectile.Owner == expectedOwner);
        Assert.All(context.ProjectileSink.Where(projectile =>
            projectile.Owner?.StartsWith("beaudis") == true),
            projectile => Assert.InRange(projectile.Damage, 90f, 100f));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void EveryMovementThreatensAnOuterPlayerWithinItsAuthoredBudget(int phase)
    {
        var pressure = SimulatePressure(phase);

        Assert.True(pressure.Hits > 0,
            $"Beaudis phase {phase} never threatened the stationary outer player. Peak={pressure.Peak}.");
        Assert.InRange(pressure.Peak, 1, 48);
        Assert.Equal(0, pressure.Overflow);
    }

    [Fact]
    public void FinaleFadeCompletesBeforeTheMidpointCanBeRemoved()
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var boss = MakeBoss(battleground);
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(5);
        boss.DebugPhaseLocked = false;
        var context = MakeContext(boss, battleground);
        ClearPhaseProtection(boss, context);
        // A single hit only removes one grammar's budget, so the bar is walked
        // down to its last sliver first. Interference is already spent here.
        boss.DebugCompleteClosingSurvival();
        boss.Hp = 2;
        boss.DebugRebasePhaseHealth();

        var result = boss.TakeDamage(boss.MaxHp);
        Assert.True(boss.Dying);
        Assert.False(result.Killed);
        Assert.True(boss.MidpointSurvived);
        Assert.False(boss.IsDead());

        for (int tick = 0; tick < Simulation.FrameRate * boss.DeathDuration + 5; tick++)
            Step(boss, context);

        Assert.True(boss.IsDead());
    }
}
