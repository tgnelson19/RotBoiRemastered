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
        Assert.Equal(8, Ache.AcheConfig.PhaseLabels.Count);
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
    public void ChaosPatternsRemainReactableAndUseNoPortals()
    {
        var battleground = MakeBattleground();
        var boss = new Ache(1000, 1000, battleground, new Random(4));
        var context = Context(boss, battleground);
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(7);

        for (int tick = 0; tick < 1800; tick++)
            boss.Update(context);

        Assert.NotEmpty(context.ProjectileSink);
        Assert.All(context.ProjectileSink, shot => Assert.DoesNotContain("portal", shot.Owner));
        Assert.Contains(context.ProjectileSink, shot => shot.Path == "laser" && shot.TelegraphDuration >= 1.2f);
        Assert.Contains(context.ProjectileSink, shot => shot.Path is "mine" or "pool" or "bomb");
        Assert.Contains(context.ProjectileSink, shot => shot.Speed <= .4f || shot.TelegraphDuration >= 1.2f);
    }

    [Fact]
    public void Overload_IsFortySecondsThenTenSecondDeath()
    {
        var battleground = MakeBattleground();
        var boss = new Ache(1000, 1000, battleground, new Random(5));
        var context = Context(boss, battleground);
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(8);

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
