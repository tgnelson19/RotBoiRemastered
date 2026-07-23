using Microsoft.Xna.Framework;
using RotBoiRemastered.Core;
using RotBoiRemastered.Entities;
using RotBoiRemastered.Systems;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.Entities;

public class ChronosTests
{
    private static Battleground MakeBattleground() => Battleground.GenerateSound();

    private static EnemyUpdateContext Context(Chronos boss, Battleground battleground) => new()
    {
        PlayerWorldX = boss.WorldX + 600,
        PlayerWorldY = boss.WorldY,
        Battleground = battleground,
    };

    [Fact]
    public void Constructor_UsesSlowHeavySightBaneIdentity()
    {
        var boss = new Chronos(1000, 1000, MakeBattleground(), new Random(1));

        Assert.Equal(310000, boss.MaxHp);
        Assert.Equal("CHRONOS", boss.BossDisplayName);
        Assert.Equal("DIRECTIVE", boss.PhaseLabel);
        Assert.Equal("THE KING OF ATTRITION", Chronos.ChronosConfig.Subtitle);
        Assert.Contains("THORN OF TIME", Chronos.ChronosConfig.PhaseLabels);
        Assert.Equal(15, Chronos.AmbientMoteCount);
        Assert.Equal(24, Chronos.FinaleMoteCount);
        Assert.True(Chronos.ChronosConfig.MovementSpeed < Ishe.IsheConfig.MovementSpeed);
        Assert.True(boss.MaxHp < new Malady(1000, 1000, MakeBattleground(), new Random(2)).MaxHp);
        Assert.True(boss.FinaleDuration > new Ache(1000, 1000, MakeBattleground(), new Random(3)).FinaleDuration);
        Assert.True(boss.FinaleDuration <= new Rot(1000, 1000, MakeBattleground(), new Random(4)).FinaleDuration);
        Assert.True(Chronos.ActiveRouteSoftCap < GameSession.MaxBossProjectiles);
    }

    [Fact]
    public void HalfHealthStartsStillSecondAndBlocksDamage()
    {
        var battleground = MakeBattleground();
        var boss = new Chronos(1000, 1000, battleground, new Random(2));
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(3);
        boss.DebugPhaseLocked = false;
        var context = Context(boss, battleground);
        for (int tick = 0; tick < Simulation.FrameRate * 6 &&
             boss.PhaseDeclarations < Chronos.MinimumDamagePhaseDeclarations; tick++)
            boss.Update(context);

        boss.TakeDamage(boss.MaxHp);

        Assert.True(boss.MidpointSurvivalActive);
        Assert.Equal("STILL SECOND", boss.PhaseLabel);
        Assert.Equal(boss.MaxHp / 2, boss.Hp);
        Assert.True(boss.TakeDamage(1000).Blocked);
    }

    [Fact]
    public void HugeOpeningHitStopsAtFirstLessonInsteadOfSkippingToSurvival()
    {
        var boss = new Chronos(1000, 1000, MakeBattleground(), new Random(2));
        boss.EntranceRemaining = 0;

        boss.TakeDamage(boss.MaxHp);

        Assert.Equal((int)Math.Round(boss.MaxHp * .84), boss.Hp);
        Assert.Equal(1, boss.Phase);
        Assert.False(boss.MidpointSurvivalActive);
    }

    [Fact]
    public void DirectiveDrawsWholeSegmentedTentacleBeforeItStrikes()
    {
        var battleground = MakeBattleground();
        var boss = new Chronos(1000, 1000, battleground, new Random(3));
        var context = Context(boss, battleground);
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(1);

        for (int tick = 0; tick < 350 && context.ProjectileSink.Count == 0; tick++)
            boss.Update(context);

        var segments = context.ProjectileSink.Where(shot => shot.Owner?.Contains("chronos_directive") == true).ToList();
        Assert.True(segments.Count >= 12);
        Assert.All(segments, segment =>
        {
            Assert.Equal("laser", segment.Path);
            Assert.True(segment.TelegraphDuration >= 1.5f);
            Assert.True(segment.Lifetime > segment.TelegraphDuration);
        });
    }

    [Fact]
    public void PathMovementStillDeclaresDirectiveAroundRealPlayer()
    {
        var battleground = MakeBattleground();
        var boss = new Chronos(1000, 1000, battleground, new Random(31));
        var context = Context(boss, battleground);
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(1);

        boss.Update(context);

        var firstLeft = Assert.Single(context.ProjectileSink,
            shot => shot.Owner == "chronos_directive_left_segment_0");
        var center = new Vector2(boss.WorldX + boss.Size / 2f, boss.WorldY + boss.Size / 2f);
        float aimed = MathF.Atan2(context.PlayerWorldY - center.Y, context.PlayerWorldX - center.X);
        float expected = aimed - .34f - .5f * .22f * .35f;
        Assert.InRange(MathF.Abs(NormalizeAngle(firstLeft.Direction - expected)), 0f, .015f);
    }

    [Fact]
    public void StillSecondRadialTentaclesKeepAThreeArmOpening()
    {
        var battleground = MakeBattleground();
        var boss = new Chronos(1000, 1000, battleground, new Random(4));
        var context = Context(boss, battleground);
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(4);

        boss.Update(context);

        var segments = context.ProjectileSink.Where(shot => shot.Owner?.Contains("chronos_still_second") == true).ToList();
        Assert.Equal(35, segments.Count); // seven arms x five segments; three of ten arms are the safe opening
    }

    [Fact]
    public void RotatingStillSecondOpeningRemainsCenteredOnPlayer()
    {
        var battleground = MakeBattleground();
        var boss = new Chronos(1000, 1000, battleground, new Random(41));
        var center = new Vector2(boss.WorldX + boss.Size / 2f, boss.WorldY + boss.Size / 2f);
        var context = new EnemyUpdateContext
        {
            PlayerWorldX = center.X + MathF.Cos(.4f) * 600f,
            PlayerWorldY = center.Y + MathF.Sin(.4f) * 600f,
            Battleground = battleground,
        };
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(4);

        boss.Update(context);
        context.ProjectileSink.Clear();
        for (int tick = 0; tick < 500 && boss.PatternRotation < 2; tick++)
            boss.Update(context);

        Assert.Contains(context.ProjectileSink,
            shot => shot.Owner == "chronos_still_second_2_segment_0");
        Assert.DoesNotContain(context.ProjectileSink,
            shot => shot.Owner == "chronos_still_second_9_segment_0");
    }

    [Fact]
    public void ThornOfTime_IsOneFullyDeclaredEightSegmentKillingLine()
    {
        var battleground = MakeBattleground();
        var boss = new Chronos(1000, 1000, battleground, new Random(7));
        var context = Context(boss, battleground);
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(6);

        for (int tick = 0; tick < 900 && !context.ProjectileSink.Any(shot => shot.Owner?.Contains("thorn_of_time") == true); tick++)
            boss.Update(context);

        var thorn = context.ProjectileSink.Where(shot => shot.Owner?.Contains("thorn_of_time") == true).ToList();
        Assert.Equal(8, thorn.Count);
        Assert.All(thorn, segment =>
        {
            Assert.Equal("laser", segment.Path);
            Assert.True(segment.TelegraphDuration >= 2.3f);
            Assert.True(segment.Damage >= 1200);
        });
    }

    [Fact]
    public void WearyingLashDeclaresRearRevisionOnAReadableSecondBeat()
    {
        var battleground = MakeBattleground();
        var boss = new Chronos(1000, 1000, battleground, new Random(71));
        var context = Context(boss, battleground);
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(3);

        boss.Update(context);

        Assert.Equal(14, context.ProjectileSink.Count(shot =>
            shot.Owner?.Contains("chronos_oracle_outer") == true));
        Assert.DoesNotContain(context.ProjectileSink,
            shot => shot.Owner?.Contains("chronos_oracle_rear") == true);

        for (int tick = 0; tick < Simulation.FrameRate && !context.ProjectileSink.Any(shot =>
                 shot.Owner?.Contains("chronos_oracle_rear") == true); tick++)
            boss.Update(context);

        Assert.Equal(6, context.ProjectileSink.Count(shot =>
            shot.Owner?.Contains("chronos_oracle_rear") == true));
    }

    [Fact]
    public void KingsAttritionIsThirtyFiveSecondsThenTenSecondCollapse()
    {
        var battleground = MakeBattleground();
        var boss = new Chronos(1000, 1000, battleground, new Random(5));
        var context = Context(boss, battleground);
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(7);

        Assert.True(boss.FinaleActive);
        Assert.Equal(35.0, boss.FinaleRemaining);
        Assert.True(boss.TakeDamage(1000).Blocked);

        for (int tick = 0; tick < 5000 && !boss.Dying; tick++)
            boss.Update(context);
        Assert.True(boss.Dying);
        Assert.Equal(10.0, boss.DeathDuration);

        for (int tick = 0; tick < 1300 && !boss.IsDead(); tick++)
            boss.Update(context);
        Assert.True(boss.IsDead());
    }

    [Fact]
    public void RouteBudgetRejectsWholeDeclarationInsteadOfTruncatingTentacle()
    {
        var battleground = MakeBattleground();
        var boss = new Chronos(1000, 1000, battleground, new Random(51));
        var context = Context(boss, battleground);
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(1);
        for (int index = 0; index < Chronos.ActiveRouteSoftCap - 4; index++)
        {
            context.ProjectileSink.Add(new EnemyProjectile(0, 0, 0, 0, 1, 1,
                owner: $"chronos_existing_{index}"));
        }

        boss.Update(context);

        Assert.Equal(Chronos.ActiveRouteSoftCap - 4, context.ProjectileSink.Count);
        Assert.DoesNotContain(context.ProjectileSink,
            shot => shot.Owner?.Contains("directive") == true);
    }

    [Fact]
    public void ThornDamagePhaseMustDeclareTwiceBeforeKingsAttrition()
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var boss = new Chronos(1000, 1000, battleground, new Random(56));
        var context = Context(boss, battleground);
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(6);

        boss.TakeDamage(boss.MaxHp);

        Assert.Equal(1, boss.Hp);
        Assert.False(boss.FinaleActive);
        for (int tick = 0; tick < Simulation.FrameRate * 6 &&
             boss.PhaseDeclarations < Chronos.MinimumDamagePhaseDeclarations; tick++)
            boss.Update(context);
        boss.TakeDamage(10);

        Assert.True(boss.FinaleActive);
        Assert.Equal(7, boss.Phase);
    }

    [Fact]
    public void FinaleStaysUnderItsAuthoredRouteBudgetAndUsesMemoryEchoes()
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        float bodySize = Simulation.TileSize * (float)Chronos.ChronosConfig.FinalBodyScale;
        var boss = new Chronos(
            battleground.Width * Simulation.TileSize / 2f - bodySize / 2f,
            battleground.Height * Simulation.TileSize / 2f - bodySize / 2f,
            battleground, new Random(61));
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(7);
        var context = new EnemyUpdateContext
        {
            PlayerWorldX = boss.ArenaCenter.X + boss.ArenaRadius * .28f,
            PlayerWorldY = boss.ArenaCenter.Y - boss.ArenaRadius * .16f,
            Battleground = battleground,
        };
        int peak = 0;
        var owners = new HashSet<string>();

        for (int tick = 0; tick < Simulation.FrameRate * 18; tick++)
        {
            boss.Update(context);
            foreach (var projectile in context.ProjectileSink)
            {
                if (projectile.Owner is not null)
                    owners.Add(projectile.Owner);
                projectile.Update(battleground, casualMode: false);
            }
            context.ProjectileSink.RemoveAll(projectile => projectile.RemFlag);
            peak = Math.Max(peak, context.ProjectileSink.Count);
        }

        Assert.InRange(peak, 90, 105);
        Assert.Contains(owners, owner => owner.Contains("attrition_memory_echo"));
        Assert.Contains(owners, owner => owner.Contains("temporal_echo"));
        Assert.InRange(boss.HistoricalRouteCount, 1, 72);
    }

    [Fact]
    public void SolvingThreeDeclaredRoutesCreatesARewardedFractureWindow()
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var boss = new Chronos(1000, 1000, battleground, new Random(91));
        var context = Context(boss, battleground);
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(1);

        for (int tick = 0; tick < Simulation.FrameRate * 9 && !boss.TemporalFractureActive; tick++)
        {
            boss.Update(context);
            foreach (var projectile in context.ProjectileSink)
                projectile.Update(battleground, casualMode: false);
            context.ProjectileSink.RemoveAll(projectile => projectile.RemFlag);
        }

        Assert.True(boss.TemporalFractureActive);
        Assert.Equal(0, boss.TemporalInsight);
        var rewarded = boss.TakeDamage(1000);
        Assert.Equal(1180, rewarded.Amount);
    }

    [Theory]
    [InlineData(.20f, .10f)]
    [InlineData(.72f, 0f)]
    [InlineData(-.48f, .56f)]
    public void FinaleEventuallyThreatensStationaryPositionsAcrossArena(float xRatio, float yRatio)
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        float bodySize = Simulation.TileSize * (float)Chronos.ChronosConfig.FinalBodyScale;
        var boss = new Chronos(
            battleground.Width * Simulation.TileSize / 2f - bodySize / 2f,
            battleground.Height * Simulation.TileSize / 2f - bodySize / 2f,
            battleground, new Random(81));
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(7);
        var player = boss.ArenaCenter + new Vector2(
            boss.ArenaRadius * xRatio, boss.ArenaRadius * yRatio);
        int playerSize = (int)(Simulation.TileSize * .75f);
        var playerRect = new Rectangle((int)(player.X - playerSize / 2f),
            (int)(player.Y - playerSize / 2f), playerSize, playerSize);
        var context = new EnemyUpdateContext
        {
            PlayerWorldX = player.X,
            PlayerWorldY = player.Y,
            Battleground = battleground,
        };
        var threats = new HashSet<EnemyProjectile>();

        for (int tick = 0; tick < Simulation.FrameRate * 14; tick++)
        {
            boss.Update(context);
            foreach (var projectile in context.ProjectileSink)
            {
                projectile.Update(battleground, casualMode: false);
                if (projectile.Collides(playerRect))
                    threats.Add(projectile);
            }
            context.ProjectileSink.RemoveAll(projectile => projectile.RemFlag);
        }

        Assert.NotEmpty(threats);
    }

    private static float NormalizeAngle(float angle)
    {
        while (angle > MathF.PI)
            angle -= MathF.Tau;
        while (angle < -MathF.PI)
            angle += MathF.Tau;
        return angle;
    }
}
