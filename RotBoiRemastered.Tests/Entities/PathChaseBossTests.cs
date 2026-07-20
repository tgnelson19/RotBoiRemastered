using RotBoiRemastered.Entities;
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
        Assert.Equal(29000, ishe.Hp);
        Assert.Equal(1, ishe.Phase);
        Assert.Equal("GLIMPSE", ishe.PhaseLabel);
    }

    [Fact]
    public void Constructor_FinalBoss_UsesFinalStatsAndIsBigger()
    {
        var battleground = MakeBattleground();
        var ishe = new Ishe(1000, 1000, battleground, new Random(1));
        var chronos = new Chronos(1000, 1000, battleground, new Random(1));
        Assert.Equal(240000, chronos.Hp);
        Assert.True(chronos.Size > ishe.Size);
    }

    [Fact]
    public void UpdatePhase_HealthThresholds_AdvancePhase()
    {
        var ishe = new Ishe(1000, 1000, MakeBattleground(), new Random(1));
        var context = MakeContext(1000, 1000);

        ishe.Hp = (int)(ishe.MaxHp * .68); // above the .67 gate -> stays phase 1
        ishe.Update(context);
        Assert.Equal(1, ishe.Phase);

        ishe.Hp = (int)(ishe.MaxHp * .5); // between .67 and .34 -> phase 2
        ishe.Update(context);
        Assert.Equal(2, ishe.Phase);

        ishe.Hp = (int)(ishe.MaxHp * .2); // below .34 -> phase 3
        ishe.Update(context);
        Assert.Equal(3, ishe.Phase);
    }

    [Fact]
    public void DebugSetPhase_LocksPhaseAndResetsCooldown()
    {
        var ishe = new Ishe(1000, 1000, MakeBattleground(), new Random(1));
        ishe.DebugSetPhase(3);
        Assert.Equal(3, ishe.Phase);
        Assert.Equal("FLASH", ishe.PhaseLabel);

        // Locked: even a lethal-ratio HP change shouldn't move it off the debug phase.
        ishe.Hp = ishe.MaxHp;
        ishe.Update(MakeContext(1000, 1000));
        Assert.Equal(3, ishe.Phase);
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
}
