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
        Assert.Equal("AWAKEN", boss.PhaseLabel);
        Assert.Equal(14.0, boss.SurvivalDuration);
        Assert.Equal(2, Beaudis.MinimumDamagePhaseDeclarations);
    }

    [Fact]
    public void FourDamageMovementsRequireTwoDeclarationsAtEveryGate()
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var boss = MakeBoss(battleground);
        boss.EntranceRemaining = 0;
        var context = MakeContext(boss, battleground);

        int[] expectedFloors =
        {
            (int)Math.Round(boss.MaxHp * .75),
            (int)Math.Round(boss.MaxHp * .50),
            (int)Math.Round(boss.MaxHp * .25),
            1,
        };
        int[] phases = { 1, 2, 4, 5 };

        for (int index = 0; index < phases.Length; index++)
        {
            context.ProjectileSink.Clear();
            boss.DebugSetPhase(phases[index]);
            boss.DebugPhaseLocked = false;
            ClearPhaseProtection(boss, context);
            boss.TakeDamage(boss.MaxHp);
            Assert.Equal(expectedFloors[index], boss.Hp);
            Assert.Equal(phases[index], boss.Phase);
            Assert.False(boss.Dying);

            ReachDeclarations(boss, context, Beaudis.MinimumDamagePhaseDeclarations);
            boss.TakeDamage(1);
            if (phases[index] == 5)
                Assert.True(boss.Dying);
            else
                Assert.Equal(phases[index] + 1, boss.Phase);
        }
    }

    [Fact]
    public void EndureIsOneHalfHealthSurvivalThenOpensPress()
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var boss = MakeBoss(battleground, 4);
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(2);
        boss.DebugPhaseLocked = false;
        var context = MakeContext(boss, battleground);
        ClearPhaseProtection(boss, context);

        boss.TakeDamage(boss.MaxHp);
        ReachDeclarations(boss, context, 2);
        boss.TakeDamage(1);

        Assert.Equal(3, boss.Phase);
        Assert.Equal("ENDURE", boss.PhaseLabel);
        Assert.True(boss.SurvivalActive);
        Assert.Equal(4, boss.ProjectilePortals.Count);
        Assert.Equal(14.0, boss.SurvivalRemaining);
        Assert.True(boss.TakeDamage(1000).Blocked);

        for (int tick = 0; tick < Simulation.FrameRate * 14 + 5; tick++)
            Step(boss, context);

        Assert.False(boss.SurvivalActive);
        Assert.Equal(4, boss.Phase);
        Assert.Equal("PRESS", boss.PhaseLabel);
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
        ReachDeclarations(boss, context, 2);

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
