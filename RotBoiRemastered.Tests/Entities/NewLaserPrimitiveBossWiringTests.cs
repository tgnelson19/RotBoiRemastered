using Microsoft.Xna.Framework;
using RotBoiRemastered.Core;
using RotBoiRemastered.Entities;
using RotBoiRemastered.Systems;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.Entities;

/// <summary>
/// Confirms each boss actually wires up its new laser-report primitive
/// (proximity mines, homing, breathing pools, arena bounce, tethered pairs)
/// rather than just having the shared EnemyProjectile plumbing compile.
/// </summary>
public class NewLaserPrimitiveBossWiringTests
{
    private static Battleground MakeBattleground() => Battleground.GenerateSound();

    // -- Rot ---------------------------------------------------------------

    private static EnemyUpdateContext RotContext(Rot boss, Battleground battleground) => new()
    {
        PlayerWorldX = boss.ArenaCenter.X + boss.ArenaRadius * .78f,
        PlayerWorldY = boss.ArenaCenter.Y,
        Battleground = battleground,
    };

    [Fact]
    public void Rot_RootLance_SproutsSlowerThanAnOrdinaryLaser()
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var boss = new Rot(1000, 1000, battleground, new Random(11));
        var context = RotContext(boss, battleground);
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(7);

        for (int tick = 0; tick < 3000 && !context.ProjectileSink.Any(shot => shot.Owner == "rot_touch_burial_root"); tick++)
            boss.Update(context);

        var lance = context.ProjectileSink.First(shot => shot.Owner == "rot_touch_burial_root");
        Assert.Equal("laser", lance.Path);
        Assert.True(lance.SproutSeconds > EnemyProjectile.LaserSproutDuration,
            "Rot's root should sprout slower than the shared default.");
    }

    [Fact]
    public void Rot_BuriedCharge_IsADormantProximityMine()
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var boss = new Rot(1000, 1000, battleground, new Random(12));
        var context = RotContext(boss, battleground);
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(3);

        for (int tick = 0; tick < 3000 && !context.ProjectileSink.Any(shot => shot.Owner == "rot_touch_buried_charge"); tick++)
            boss.Update(context);

        var charge = context.ProjectileSink.First(shot => shot.Owner == "rot_touch_buried_charge");
        Assert.Equal("mine", charge.Path);
        Assert.True(charge.ProximityRadius > 0f);
    }

    // -- Ache ----------------------------------------------------------------

    private static EnemyUpdateContext AcheContext(Ache boss, Battleground battleground) => new()
    {
        PlayerWorldX = boss.WorldX + 600,
        PlayerWorldY = boss.WorldY + 120,
        Battleground = battleground,
        BossAfflictions = new BossAfflictions(),
    };

    [Fact]
    public void Ache_WrongWayBurst_IncludesOneHomingSpore()
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var boss = new Ache(1000, 1000, battleground, new Random(13));
        var context = AcheContext(boss, battleground);
        boss.EntranceRemaining = 0;

        for (int tick = 0; tick < 4000
            && !context.ProjectileSink.Any(shot => shot.Owner == "ache_chemesthesis_wrong_way_hazard" && shot.HomingTurnRate != 0f);
            tick++)
            boss.Update(context);

        Assert.Contains(context.ProjectileSink,
            shot => shot.Owner == "ache_chemesthesis_wrong_way_hazard" && shot.HomingTurnRate != 0f);
    }

    // -- Malady ----------------------------------------------------------------

    private static EnemyUpdateContext MaladyContext(Malady boss, Battleground battleground) => new()
    {
        PlayerWorldX = boss.WorldX + 500,
        PlayerWorldY = boss.WorldY,
        Battleground = battleground,
        DreamState = new DreamState(),
    };

    [Fact]
    public void Malady_SurvivalPool_Breathes_WhileOrdinaryPoolsDoNot()
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var boss = new Malady(1000, 1000, battleground, new Random(14));
        var context = MaladyContext(boss, battleground);
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(6); // Intermission -- Malady's survival phase.

        for (int tick = 0; tick < 3000 && !context.ProjectileSink.Any(shot => shot.Path == "pool"); tick++)
            boss.Update(context);

        var pool = context.ProjectileSink.First(shot => shot.Path == "pool");
        Assert.True(pool.PoolPulseAmplitude > 0f);
    }

    [Fact]
    public void Malady_NonSurvivalPool_DoesNotBreathe()
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        var boss = new Malady(1000, 1000, battleground, new Random(15));
        var context = MaladyContext(boss, battleground);
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(2); // Petal Flood -- ordinary pool phase, not survival.

        for (int tick = 0; tick < 3000 && !context.ProjectileSink.Any(shot => shot.Path == "pool"); tick++)
            boss.Update(context);

        var pool = context.ProjectileSink.First(shot => shot.Path == "pool");
        Assert.Equal(0f, pool.PoolPulseAmplitude);
    }

    // -- Dissonance ----------------------------------------------------------------

    [Fact]
    public void Dissonance_Ricochet_BouncesOffTheArenaBoundary()
    {
        var boss = new Dissonance(1000, 1000, 400f, MakeBattleground(), new Random(16));
        var sink = new List<EnemyProjectile>();
        boss.CallbackIndex = 3;
        boss.SpecialAttackCooldown = 0;

        boss.UpdateSpecialAttacks(boss.WorldX + 300, boss.WorldY, sink, dt: 0);

        var ricochet = Assert.Single(sink, shot => shot.Owner == "dissonance_ricochet");
        Assert.Equal("bounce", ricochet.Path);
        Assert.True(ricochet.BounceRadius > 0f);
        Assert.Equal(boss.ArenaCenter, ricochet.BounceCenter);
    }

    // -- Chronos ----------------------------------------------------------------

    private static EnemyUpdateContext ChronosContext(Chronos boss, Battleground battleground) => new()
    {
        PlayerWorldX = boss.WorldX + 600,
        PlayerWorldY = boss.WorldY,
        Battleground = battleground,
    };

    [Fact]
    public void Chronos_ThornHands_ProducesATetheredOrbitPair()
    {
        Simulation.ResetForTests();
        var battleground = Battleground.GenerateSound();
        var boss = new Chronos(1000, 1000, battleground, new Random(17));
        var context = ChronosContext(boss, battleground);
        boss.EntranceRemaining = 0;
        boss.DebugSetPhase(6);

        for (int tick = 0; tick < 3000 && !context.ProjectileSink.Any(shot => shot.Path == "tether"); tick++)
            boss.Update(context);

        var tether = context.ProjectileSink.First(shot => shot.Path == "tether");
        Assert.NotNull(tether.TetherStart);
        Assert.NotNull(tether.TetherEnd);
        Assert.Equal("orbit", tether.TetherStart!.Path);
        Assert.Equal("orbit", tether.TetherEnd!.Path);
    }
}
