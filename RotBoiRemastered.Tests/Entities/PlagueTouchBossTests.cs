using Microsoft.Xna.Framework;
using RotBoiRemastered.Core;
using RotBoiRemastered.Entities;
using RotBoiRemastered.Systems;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.Entities;

public class PlagueTouchBossTests
{
    private static Battleground MakeBattleground() => Battleground.GenerateSound();

    private static Bair MakeCenteredBair(Battleground battleground, int seed = 1)
    {
        float size = Simulation.TileSize * (float)Bair.BairConfig.BodyScale;
        return new Bair(
            battleground.Width * Simulation.TileSize / 2f - size / 2f,
            battleground.Height * Simulation.TileSize / 2f - size / 2f,
            battleground, new Random(seed));
    }

    private static EnemyUpdateContext MakeContext(Bair boss, Battleground battleground,
        Vector2? arenaOffset = null)
    {
        Vector2 offset = arenaOffset ?? new Vector2(.72f, 0);
        Vector2 player = boss.ArenaCenter + new Vector2(
            offset.X * boss.ArenaRadius, offset.Y * boss.ArenaRadius);
        return new EnemyUpdateContext
        {
            PlayerWorldX = player.X,
            PlayerWorldY = player.Y,
            Battleground = battleground,
        };
    }

    private static void Step(Bair boss, EnemyUpdateContext context)
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

    private static void ReachDeclarations(Bair boss, EnemyUpdateContext context, int count)
    {
        for (int tick = 0; tick < 1600 && boss.PhaseDeclarations < count; tick++)
            Step(boss, context);
        Assert.True(boss.PhaseDeclarations >= count);
    }

    private sealed record BairPressure(int Peak, int Overflow, int Threats,
        IReadOnlySet<string> Owners);

    private static BairPressure SimulatePressure(int phase, Vector2 playerOffset,
        double duration = 18.0, int seed = 200)
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var boss = MakeCenteredBair(battleground, seed + phase);
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
                context.ProjectileSink.RemoveRange(0,
                    context.ProjectileSink.Count - GameSession.MaxBossProjectiles);
            }
        }
        return new BairPressure(peak, overflow, threats.Count, owners);
    }

    [Fact]
    public void Constructor_Bair_UsesAuthoredMidpointBalanceAndFiveMovements()
    {
        var bair = new Bair(1000, 1000, MakeBattleground(), new Random(1));

        Assert.Equal(110000, bair.MaxHp);
        Assert.Equal(380, bair.Damage);
        Assert.Equal(1, bair.Phase);
        Assert.Equal("INTAKE", bair.PhaseLabel);
        Assert.Equal(
            new[] { "INTAKE", "QUARTERING", "MOVING CELL", "RELEASE", "SOLITARY" },
            Bair.BairConfig.PhaseLabels);
        Assert.Equal(20.0, Bair.RuinDuration);
    }

    [Fact]
    public void EveryDeclarationSurrendersOnlyItsOwnBudgetThenRunsItsClock()
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var bair = MakeCenteredBair(battleground);
        var context = MakeContext(bair, battleground);
        bair.EntranceRemaining = 0;
        int opening = bair.Phase;

        bair.TakeDamage(bair.MaxHp);
        Assert.Equal(
            bair.MaxHp - (int)Math.Round(bair.MaxHp * BossPhaseGovernor.DefaultThresholdFraction),
            bair.Hp);
        Assert.Equal(opening, bair.Phase);
        Assert.False(bair.RuinSurvivalActive);

        // A level-ten encounter releases the phase seven seconds after the
        // threshold rather than riding the whole clock.
        for (int tick = 0;
             tick < Simulation.FrameRate * (BossPhaseGovernor.LowerTierHoldSeconds + 1); tick++)
            Step(bair, context);
        Assert.NotEqual(opening, bair.Phase);
    }

    [Fact]
    public void RuinIsTheClosingFourGateTwentySecondSurvivalThatEndsTheFight()
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var bair = MakeCenteredBair(battleground, 4);
        var context = MakeContext(bair, battleground);
        bair.EntranceRemaining = 0;
        bair.Hp = 1;
        bair.DebugRebasePhaseHealth();
        Step(bair, context);

        Assert.True(bair.RuinSurvivalActive);
        Assert.Equal(20.0, bair.RuinSurvivalRemaining);
        Assert.True(bair.TakeDamage(1000).Blocked);
        var gates = bair.GetScreenHitboxes(new Camera(),
            new Vector2(bair.WorldX, bair.WorldY), Vector2.Zero)
            .Count(hitbox => hitbox.Part.StartsWith("portal:"));
        Assert.Equal(4, gates);

        // The survival starts behind a phase interlude: the arena is swept
        // clear and the boss walks back to centre before the clock runs.
        for (int tick = 0;
             tick < Simulation.FrameRate * (20 + BossPhaseInterlude.DefaultDuration) + 5;
             tick++)
            Step(bair, context);

        Assert.False(bair.RuinSurvivalActive);
        Assert.True(bair.RuinSurvivalCleared);
        Assert.True(bair.Dying);
    }

    [Fact]
    public void TouchGatesTakeEightSeparateHitsAndNeverDamageBair()
    {
        var battleground = MakeBattleground();
        var bair = MakeCenteredBair(battleground);
        bair.DebugSetPhase(2);
        var camera = new Camera();
        var player = new Vector2(bair.WorldX, bair.WorldY);
        double hpBefore = bair.Hp;

        for (int hit = 0; hit < 7; hit++)
        {
            var result = bair.TakeDamage(1_000_000, "portal:0");
            Assert.True(result.Applied);
            Assert.False(result.Blocked);
            Assert.Contains(bair.GetScreenHitboxes(camera, player, Vector2.Zero),
                hitbox => hitbox.Part == "portal:0");
        }
        bair.TakeDamage(1_000_000, "portal:0");

        Assert.DoesNotContain(bair.GetScreenHitboxes(camera, player, Vector2.Zero),
            hitbox => hitbox.Part == "portal:0");
        Assert.Equal(hpBefore, bair.Hp);
    }

    [Fact]
    public void TouchGatesWarnBeforeFiringFinitePairedShots()
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var bair = MakeCenteredBair(battleground, 6);
        var context = MakeContext(bair, battleground);
        bair.DebugSetPhase(2);
        bair.EntranceRemaining = 0;

        for (int tick = 0; tick < (int)(Simulation.FrameRate * .75); tick++)
            bair.Update(context);
        Assert.DoesNotContain(context.ProjectileSink,
            projectile => projectile.Owner?.Contains("plague_gate_heavy") == true);

        for (int tick = 0; tick < (int)(Simulation.FrameRate * .4) &&
                           !context.ProjectileSink.Any(projectile =>
                               projectile.Owner?.Contains("plague_gate_heavy") == true);
             tick++)
            bair.Update(context);

        var volley = context.ProjectileSink.Where(projectile =>
            projectile.Owner?.Contains("plague_gate_heavy") == true).ToList();
        Assert.Equal(2, volley.Count);
        Assert.All(volley, projectile =>
        {
            Assert.Equal(7.0f, projectile.Lifetime);
            Assert.True(projectile.RemainingRange <= bair.ArenaRadius * 2.4f);
        });
    }

    [Fact]
    public void RiverDeclaresTwoAdvancingBanksBeforeTheyBecomeDangerous()
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var bair = MakeCenteredBair(battleground);
        var context = MakeContext(bair, battleground);
        bair.EntranceRemaining = 0;

        for (int tick = 0; tick < 300 && context.ProjectileSink.Count == 0; tick++)
            bair.Update(context);

        var current = context.ProjectileSink
            .Where(projectile => projectile.Owner == "bair_touch_river_current")
            .ToList();
        Assert.Equal(2, current.Count);
        Assert.All(current, projectile =>
        {
            Assert.Equal("bank", projectile.Path);
            Assert.True(projectile.TelegraphDuration >= .7f);
        });
        var lockColumn = context.ProjectileSink
            .Where(projectile => projectile.Owner == "bair_touch_river_lock")
            .ToList();
        Assert.Equal(7, lockColumn.Count);
        Assert.All(lockColumn, projectile =>
        {
            Assert.Equal("bank", projectile.Path);
            Assert.True(projectile.TelegraphDuration >= .85f);
        });
        Assert.Equal(2, lockColumn.Select(projectile => projectile.Shape).Distinct().Count());
    }

    [Fact]
    public void BlightMarksCurrentGroundAndBothAdjacentEscapeSides()
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var bair = MakeCenteredBair(battleground, 7);
        var context = MakeContext(bair, battleground);
        bair.DebugSetPhase(3);
        bair.EntranceRemaining = 0;

        for (int tick = 0; tick < 300 && context.ProjectileSink.Count == 0; tick++)
            bair.Update(context);

        var bombs = context.ProjectileSink
            .Where(projectile => projectile.Owner?.StartsWith("bair_touch_blight") == true)
            .ToList();
        Assert.Equal(3, bombs.Count);
        Assert.All(bombs, bomb =>
        {
            Assert.Equal("bomb", bomb.Path);
            Assert.True(bomb.TelegraphDuration >= .9f);
            Assert.True(bomb.FuseDuration >= 2.8f);
        });
        Assert.Equal(3, bombs.Select(bomb => bomb.Target).Distinct().Count());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public void EveryDamageMovementBreaksStationaryEdgeCampingWithoutOverflow(int phase)
    {
        var pressure = SimulatePressure(phase, new Vector2(.78f, 0));

        Assert.True(pressure.Threats >= 1,
            $"Bair phase {phase} never threatened a stationary edge player. Peak={pressure.Peak}.");
        Assert.InRange(pressure.Peak, 1, 72);
        Assert.Equal(0, pressure.Overflow);
    }

    [Fact]
    public void RuinCombinesFourGatesWithBodyDeclarationsWithoutOverflow()
    {
        var pressure = SimulatePressure(4, new Vector2(.72f, .28f), duration: 14.0);

        Assert.True(pressure.Threats >= 2);
        Assert.Contains(pressure.Owners, owner => owner.Contains("plague_gate"));
        Assert.Contains(pressure.Owners, owner => owner.Contains("ruin"));
        Assert.Contains(pressure.Owners, owner => owner.Contains("ruin_procession"));
        Assert.InRange(pressure.Peak, 1, 88);
        Assert.Equal(0, pressure.Overflow);
    }

    [Fact]
    public void RuinLayersCurvedSporesOverItsPlayerAnchoredProcession()
    {
        var battleground = MakeBattleground();
        var bair = MakeCenteredBair(battleground, 26);
        var context = MakeContext(bair, battleground, new Vector2(.68f, .18f));
        bair.DebugSetPhase(4);
        bair.EntranceRemaining = 0;

        bair.Update(context);

        Assert.Equal(5, context.ProjectileSink.Count(projectile =>
            projectile.Owner == "bair_touch_ruin_procession"));
        Assert.Contains(context.ProjectileSink, projectile =>
            projectile.Owner == "bair_touch_ruin_spores" &&
            projectile.Path == "sine" && projectile.Amplitude > 0);
    }

    [Fact]
    public void Constructor_Sting_RetainsLegacyFinalStatsAndTenPhases()
    {
        var sting = new Sting(1000, 1000, MakeBattleground(), new Random(1));

        Assert.Equal(240000, sting.Hp);
        Assert.Equal("BLOOD", sting.PhaseLabel);
        Assert.Equal(10, Sting.StingConfig.PhaseLabels.Count);
    }

    [Fact]
    public void StaticBlightMovementDoesNotMoveBair()
    {
        var battleground = MakeBattleground();
        var bair = MakeCenteredBair(battleground);
        bair.DebugSetPhase(3);
        float startX = bair.WorldX, startY = bair.WorldY;
        var context = MakeContext(bair, battleground);

        for (int tick = 0; tick < 10; tick++)
            bair.Update(context);

        Assert.Equal(startX, bair.WorldX, 2);
        Assert.Equal(startY, bair.WorldY, 2);
    }
}
