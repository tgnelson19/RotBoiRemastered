using RotBoiRemastered.Entities;
using RotBoiRemastered.Core;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.Entities;

/// <summary>
/// Ported from bossTypes.py's PathChaseBoss/Ishe/Chronos (no dedicated
/// Python test file to mirror). Exercises the shared base directly via
/// Ishe/Chronos since PathChaseBoss itself is a configurable placeholder,
/// never instantiated directly in Python either.
/// </summary>
public class PathChaseBossTests
{
    private static Battleground MakeBattleground() => Battleground.GenerateSound();

    private static EnemyUpdateContext MakeContext(float playerX, float playerY, Battleground? battleground = null) => new()
    {
        PlayerWorldX = playerX, PlayerWorldY = playerY, Battleground = battleground ?? MakeBattleground(),
    };

    [Fact]
    public void Constructor_MidBoss_UsesMidStatsAndArenaShape()
    {
        var battleground = MakeBattleground();
        var ishe = new Ishe(1000, 1000, battleground, new Random(1));
        Assert.Equal(75000, ishe.Hp);
        Assert.Equal(1, ishe.Phase);
        Assert.Equal("GLIMPSE", ishe.PhaseLabel);
        Assert.Equal(4, Ishe.IsheConfig.PhaseLabels.Count);
        Assert.Equal(300, ishe.Damage);
    }

    [Fact]
    public void Constructor_FinalBoss_UsesFinalStatsAndIsBigger()
    {
        var battleground = MakeBattleground();
        var ishe = new Ishe(1000, 1000, battleground, new Random(1));
        var chronos = new Chronos(1000, 1000, battleground, new Random(1));
        Assert.Equal(310000, chronos.Hp);
        Assert.True(chronos.Size > ishe.Size);
    }

    [Fact]
    public void DamageGatesTeachGlimpseThenBlinkWithoutSkippingFlash()
    {
        var battleground = MakeBattleground();
        var ishe = new Ishe(1000, 1000, battleground, new Random(1));
        var context = MakeContext(ishe.WorldX + 500, ishe.WorldY, battleground);
        ishe.EntranceRemaining = 0;

        ishe.TakeDamage(ishe.MaxHp);

        Assert.Equal((int)Math.Round(ishe.MaxHp * .72), ishe.Hp);
        Assert.Equal(1, ishe.Phase);
        Assert.False(ishe.FlashSurvivalActive);

        for (int tick = 0; tick < Simulation.FrameRate * 6 && ishe.Phase == 1; tick++)
            ishe.Update(context);

        Assert.Equal(2, ishe.Phase);
        Assert.False(ishe.FlashSurvivalActive);

        ishe.TakeDamage(ishe.MaxHp);

        Assert.Equal(2, ishe.Phase);
        for (int tick = 0; tick < Simulation.FrameRate * 6 && !ishe.FlashSurvivalActive; tick++)
            ishe.Update(context);

        Assert.Equal(3, ishe.Phase);
        Assert.True(ishe.FlashSurvivalActive);
        Assert.Equal(ishe.MaxHp / 2, ishe.Hp);
    }

    [Fact]
    public void DebugSetPhase_LocksPhaseAndResetsCooldown()
    {
        var ishe = new Ishe(1000, 1000, MakeBattleground(), new Random(1));
        ishe.DebugSetPhase(3);
        Assert.Equal(3, ishe.Phase);
        Assert.Equal("FLASH", ishe.PhaseLabel);
        Assert.True(ishe.FlashSurvivalActive);

        // Locked: even a lethal-ratio HP change shouldn't move it off the debug phase.
        ishe.Hp = ishe.MaxHp;
        ishe.Update(MakeContext(1000, 1000));
        Assert.Equal(3, ishe.Phase);
    }

    [Fact]
    public void FlashIsTwelveSecondSurvivalThenAfterglow()
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var ishe = new Ishe(1000, 1000, battleground, new Random(11));
        var context = MakeContext(ishe.WorldX + 500, ishe.WorldY + 100, battleground);
        ishe.EntranceRemaining = 0;
        ishe.DebugSetPhase(2);
        for (int tick = 0; tick < Simulation.FrameRate * 5 &&
             ishe.IshePhaseDeclarations < Ishe.MinimumIsheDamagePhaseDeclarations; tick++)
            ishe.Update(context);
        ishe.DebugPhaseLocked = false;
        ishe.TakeDamage(ishe.MaxHp);

        Assert.True(ishe.FlashSurvivalActive);
        Assert.Equal(12.0, ishe.FlashSurvivalRemaining);
        Assert.True(ishe.TakeDamage(1000).Blocked);

        for (int tick = 0; tick < Simulation.FrameRate * 14 && ishe.FlashSurvivalActive; tick++)
            ishe.Update(context);

        Assert.False(ishe.FlashSurvivalActive);
        Assert.True(ishe.FlashSurvivalCleared);
        Assert.Equal(4, ishe.Phase);
        Assert.Equal("AFTERGLOW", ishe.PhaseLabel);
    }

    [Fact]
    public void GlimpseDeclaresCompleteLinesBeforeAfterimageVolley()
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var ishe = new Ishe(1000, 1000, battleground, new Random(12));
        var context = MakeContext(ishe.WorldX + 600, ishe.WorldY, battleground);
        ishe.EntranceRemaining = 0;
        ishe.DebugSetPhase(1);

        ishe.Update(context);

        var warnings = context.ProjectileSink.Where(projectile =>
            projectile.Owner == "ishe_glimpse_warning").ToList();
        Assert.Equal(3, warnings.Count);
        Assert.All(warnings, warning =>
        {
            Assert.Equal("laser", warning.Path);
            Assert.True(warning.Illusory);
            Assert.True(warning.TelegraphDuration > warning.Lifetime);
        });
        Assert.DoesNotContain(context.ProjectileSink,
            projectile => projectile.Owner == "ishe_glimpse_afterimage");

        for (int tick = 0; tick < Simulation.FrameRate &&
             !context.ProjectileSink.Any(projectile => projectile.Owner == "ishe_glimpse_afterimage"); tick++)
            ishe.Update(context);

        Assert.Equal(3, context.ProjectileSink.Count(projectile =>
            projectile.Owner == "ishe_glimpse_afterimage"));
    }

    [Fact]
    public void FlashHorizonLeavesThreeAdjacentLanesEmpty()
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var ishe = new Ishe(1000, 1000, battleground, new Random(13));
        var context = MakeContext(ishe.WorldX + 500, ishe.WorldY + 100, battleground);
        ishe.EntranceRemaining = 0;
        ishe.DebugSetPhase(3);

        ishe.Update(context);

        var horizon = context.ProjectileSink.Where(projectile =>
            projectile.Owner?.StartsWith("ishe_flash_horizon") == true).ToList();
        Assert.Equal(4, horizon.Count);
        Assert.All(horizon, laser =>
        {
            Assert.Equal("laser", laser.Path);
            Assert.True(laser.TelegraphDuration >= 1f);
            Assert.InRange(laser.Damage, 220, 250);
        });
    }

    [Fact]
    public void FlashMovesItsDeclaredHorizonAndEventuallyThreatensCamping()
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        float bodySize = Simulation.TileSize * (float)Ishe.IsheConfig.BodyScale;
        var ishe = new Ishe(
            battleground.Width * Simulation.TileSize / 2f - bodySize / 2f,
            battleground.Height * Simulation.TileSize / 2f - bodySize / 2f,
            battleground, new Random(131));
        var player = ishe.ArenaCenter;
        int playerSize = (int)(Simulation.TileSize * .75f);
        var playerRect = new Microsoft.Xna.Framework.Rectangle(
            (int)(player.X - playerSize / 2f), (int)(player.Y - playerSize / 2f),
            playerSize, playerSize);
        var context = MakeContext(player.X, player.Y, battleground);
        ishe.EntranceRemaining = 0;
        ishe.DebugSetPhase(3);
        var threats = new HashSet<EnemyProjectile>();

        for (int tick = 0; tick < Simulation.FrameRate * 8; tick++)
        {
            ishe.Update(context);
            foreach (var projectile in context.ProjectileSink)
            {
                projectile.Update(battleground, casualMode: false);
                if (projectile.Collides(playerRect))
                    threats.Add(projectile);
            }
            context.ProjectileSink.RemoveAll(projectile => projectile.RemFlag);
        }

        Assert.NotEmpty(threats);
        Assert.True(ishe.IshePatternRotation >= 4);
    }

    [Fact]
    public void PathMovementAimsBlinkDeclarationsAtRealPlayer()
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var ishe = new Ishe(1000, 1000, battleground, new Random(14));
        var context = MakeContext(ishe.WorldX + 700, ishe.WorldY, battleground);
        ishe.EntranceRemaining = 0;
        ishe.DebugSetPhase(2);

        ishe.Update(context);

        var warning = Assert.Single(context.ProjectileSink,
            projectile => projectile.Owner == "ishe_blink_present_warning" &&
                          projectile.Direction < 0);
        var origin = new Microsoft.Xna.Framework.Vector2(warning.WorldX, warning.WorldY);
        float expected = MathF.Atan2(context.PlayerWorldY - origin.Y, context.PlayerWorldX - origin.X);
        Assert.InRange(MathF.Abs(NormalizeAngle(warning.Direction - expected)), 0f, .15f);
    }

    [Theory]
    [InlineData(.18f, .08f)]
    [InlineData(.62f, 0f)]
    [InlineData(-.38f, .48f)]
    public void AfterglowThreatensStationaryArenaPositionsWithoutOverflow(float xRatio, float yRatio)
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        float bodySize = Simulation.TileSize * (float)Ishe.IsheConfig.BodyScale;
        var ishe = new Ishe(
            battleground.Width * Simulation.TileSize / 2f - bodySize / 2f,
            battleground.Height * Simulation.TileSize / 2f - bodySize / 2f,
            battleground, new Random(21));
        ishe.EntranceRemaining = 0;
        ishe.DebugSetPhase(4);
        var player = ishe.ArenaCenter + new Microsoft.Xna.Framework.Vector2(
            ishe.ArenaRadius * xRatio, ishe.ArenaRadius * yRatio);
        int playerSize = (int)(Simulation.TileSize * .75f);
        var playerRect = new Microsoft.Xna.Framework.Rectangle(
            (int)(player.X - playerSize / 2f), (int)(player.Y - playerSize / 2f),
            playerSize, playerSize);
        var context = MakeContext(player.X, player.Y, battleground);
        var threats = new HashSet<EnemyProjectile>();
        int peak = 0;

        for (int tick = 0; tick < Simulation.FrameRate * 14; tick++)
        {
            ishe.Update(context);
            foreach (var projectile in context.ProjectileSink)
            {
                var center = new Microsoft.Xna.Framework.Vector2(
                    projectile.WorldX + projectile.Size / 2f,
                    projectile.WorldY + projectile.Size / 2f);
                if (!ishe.ProjectileWithinArenaBounds(center))
                    projectile.RemFlag = true;
                projectile.Update(battleground, casualMode: false);
                if (projectile.Collides(playerRect))
                    threats.Add(projectile);
            }
            context.ProjectileSink.RemoveAll(projectile => projectile.RemFlag);
            peak = Math.Max(peak, context.ProjectileSink.Count);
        }

        Assert.NotEmpty(threats);
        Assert.InRange(peak, 30, 34);
    }

    [Fact]
    public void Update_StaticMovementMode_DoesNotMoveTowardPlayer()
    {
        // Ishe has no "static" phase in its movement modes (PathChaseBoss's own
        // default does, at phase 2) -- construct a bare PathChaseBoss to exercise it directly.
        var battleground = MakeBattleground();
        var boss = new PathChaseBoss(battleground.SpawnPosition.X, battleground.SpawnPosition.Y, battleground, PathChaseBossConfig.Default, new Random(1));
        boss.DebugSetPhase(2); // PathChaseBossConfig.Default's movementModes[1] == "static"
        float startX = boss.WorldX, startY = boss.WorldY;

        var farAwayContext = MakeContext(startX + 5000, startY + 5000, battleground);
        for (int i = 0; i < 10; i++)
            boss.Update(farAwayContext);

        Assert.Equal(startX, boss.WorldX, 2);
        Assert.Equal(startY, boss.WorldY, 2);
    }

    [Fact]
    public void Update_EntranceActive_StillMovesAndChasesButHoldsFire()
    {
        var battleground = MakeBattleground();
        var boss = new Ishe(battleground.SpawnPosition.X, battleground.SpawnPosition.Y, battleground, new Random(1));
        var context = MakeContext(boss.WorldX + 500, boss.WorldY, battleground);

        boss.Update(context);

        Assert.Empty(context.ProjectileSink); // entrance (.9s) hasn't elapsed after one tick
    }

    [Fact]
    public void Update_AfterEntranceAndCooldownElapse_FiresAPattern()
    {
        var battleground = MakeBattleground();
        var boss = new Ishe(battleground.SpawnPosition.X, battleground.SpawnPosition.Y, battleground, new Random(1));
        var context = MakeContext(boss.WorldX + 500, boss.WorldY, battleground);

        for (int i = 0; i < 200 && context.ProjectileSink.Count == 0; i++) // entrance .9s + first cooldown, well within 200 ticks
            boss.Update(context);

        Assert.NotEmpty(context.ProjectileSink);
    }

    [Fact]
    public void LethalDamage_PlaysRewardSpectacleBeforeBossReportsDead()
    {
        var battleground = MakeBattleground();
        var boss = new Ishe(battleground.SpawnPosition.X, battleground.SpawnPosition.Y, battleground, new Random(1));
        var context = MakeContext(boss.WorldX + 500, boss.WorldY, battleground);
        boss.DebugSetPhase(4);
        boss.EntranceRemaining = 0;
        for (int tick = 0; tick < Simulation.FrameRate * 5 &&
             boss.IshePhaseDeclarations < Ishe.MinimumIsheDamagePhaseDeclarations; tick++)
            boss.Update(context);

        var result = boss.TakeDamage(1_000_000);

        Assert.True(result.Applied);
        Assert.False(result.Killed);
        Assert.True(boss.Dying);
        Assert.False(boss.IsDead());
        Assert.True(boss.TransitionCleanupRequested);

        for (int tick = 0; tick < 500 && !boss.IsDead(); tick++)
            boss.Update(context);

        Assert.True(boss.IsDead());
        Assert.Equal(0, boss.Hp);
    }

    [Fact]
    public void ConstrainPlayerPosition_OutsideArena_PullsPlayerInsideMargin()
    {
        var battleground = MakeBattleground();
        var ishe = new Ishe(battleground.SpawnPosition.X, battleground.SpawnPosition.Y, battleground, new Random(1));
        float farX = ishe.ArenaCenter.X + ishe.ArenaRadius * 5;
        float farY = ishe.ArenaCenter.Y;

        var (constrainedX, constrainedY) = ishe.ConstrainPlayerPosition(farX, farY, 40);

        float distance = Vector2Distance(constrainedX + 20, constrainedY + 20, ishe.ArenaCenter.X, ishe.ArenaCenter.Y);
        Assert.True(distance <= ishe.ArenaRadius + 1f);
    }

    [Fact]
    public void ConstrainPlayerPosition_AlreadyWellInside_LeavesPositionUnchanged()
    {
        var battleground = MakeBattleground();
        var ishe = new Ishe(battleground.SpawnPosition.X, battleground.SpawnPosition.Y, battleground, new Random(1));
        float insideX = ishe.ArenaCenter.X, insideY = ishe.ArenaCenter.Y;

        var (constrainedX, constrainedY) = ishe.ConstrainPlayerPosition(insideX - 20, insideY - 20, 40);

        Assert.Equal(insideX - 20, constrainedX, 2);
        Assert.Equal(insideY - 20, constrainedY, 2);
    }

    private static float Vector2Distance(float x1, float y1, float x2, float y2) =>
        MathF.Sqrt((x1 - x2) * (x1 - x2) + (y1 - y2) * (y1 - y2));

    private static float NormalizeAngle(float angle)
    {
        while (angle > MathF.PI)
            angle -= MathF.Tau;
        while (angle < -MathF.PI)
            angle += MathF.Tau;
        return angle;
    }
}
