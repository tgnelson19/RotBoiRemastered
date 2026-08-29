using Microsoft.Xna.Framework;
using RotBoiRemastered.Core;
using RotBoiRemastered.Entities;
using RotBoiRemastered.Systems;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.Entities;

/// <summary>
/// The cross-boss contract the phase overhaul introduced: no encounter may be
/// walked forward by damage alone, every damage phase surrenders at most its
/// own budget, and each tier closes on its standardized survival length.
/// These assert the shared shape rather than any one boss's patterns.
/// </summary>
public sealed class BossPhaseContractTests
{
    private static Battleground MakeBattleground() => Battleground.GenerateSound();

    private static EnemyUpdateContext Context(Enemy boss, Battleground battleground) => new()
    {
        PlayerWorldX = boss.WorldX + 600,
        PlayerWorldY = boss.WorldY + 120,
        Battleground = battleground,
        BossAfflictions = new BossAfflictions(),
    };

    private static PathChaseBoss MakeSenseFinale(string key, Battleground battleground, int seed) => key switch
    {
        "rot" => new Rot(1000, 1000, battleground, new Random(seed)),
        "chronos" => new Chronos(1000, 1000, battleground, new Random(seed)),
        "ache" => new Ache(1000, 1000, battleground, new Random(seed)),
        _ => new Malady(1000, 1000, battleground, new Random(seed)),
    };

    private static PathChaseBoss MakeMidpointBoss(string key, Battleground battleground, int seed) => key switch
    {
        "ishe" => new Ishe(1000, 1000, battleground, new Random(seed)),
        "kage" => new Kage(1000, 1000, battleground, new Random(seed)),
        "bair" => new Bair(1000, 1000, battleground, new Random(seed)),
        _ => new Hypno(1000, 1000, battleground, new Random(seed)),
    };

    [Theory]
    [InlineData("rot")]
    [InlineData("chronos")]
    [InlineData("ache")]
    [InlineData("malady")]
    public void SenseFinales_SurrenderAtMostFifteenPercentOfHealthPerPhase(string key)
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        PathChaseBoss boss = MakeSenseFinale(key, battleground, 401);
        var context = Context(boss, battleground);
        boss.EntranceRemaining = 0;
        for (int tick = 0; tick < 600 && boss.TakeDamage(0).Blocked; tick++)
            boss.Update(context);
        // Some encounters arm an opening act beat; let it finish so what
        // follows is measured against a settled boss.
        for (int tick = 0; tick < 900 && boss.PhaseInterludeActive; tick++)
            boss.Update(context);
        boss.DebugRebasePhaseHealth();
        int before = boss.Hp;

        boss.TakeDamage(boss.MaxHp * 8.0);

        int budget = (int)Math.Round(boss.MaxHp * BossPhaseGovernor.DefaultThresholdFraction);
        Assert.True(before - boss.Hp <= budget + 1,
            $"{key} surrendered {before - boss.Hp} of a {budget} budget.");
    }

    [Theory]
    [InlineData("rot")]
    [InlineData("chronos")]
    [InlineData("ache")]
    [InlineData("malady")]
    public void SenseFinales_RideTheWholePhaseClockEvenWhenBursted(string key)
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        PathChaseBoss boss = MakeSenseFinale(key, battleground, 402);
        var context = Context(boss, battleground);
        boss.EntranceRemaining = 0;
        for (int tick = 0; tick < 600 && boss.TakeDamage(0).Blocked; tick++)
            boss.Update(context);
        // Some encounters arm an opening act beat; let it finish so what
        // follows is measured against a settled boss.
        for (int tick = 0; tick < 900 && boss.PhaseInterludeActive; tick++)
            boss.Update(context);
        boss.DebugRebasePhaseHealth();
        int opening = boss.Phase;

        boss.TakeDamage(boss.MaxHp * 8.0);
        Assert.True(boss.PhaseDamageThresholdReached);

        // Well past the seven-second release the lower tiers get, and still
        // short of any authored finale clock: a sense finale never shortens.
        for (int tick = 0; tick < Simulation.FrameRate * 10; tick++)
            boss.Update(context);
        Assert.Equal(opening, boss.Phase);
    }

    [Theory]
    [InlineData("ishe")]
    [InlineData("kage")]
    [InlineData("bair")]
    [InlineData("hypno")]
    public void MidpointBosses_ReleaseSevenSecondsAfterTheDamageThreshold(string key)
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        PathChaseBoss boss = MakeMidpointBoss(key, battleground, 403);
        var context = Context(boss, battleground);
        boss.EntranceRemaining = 0;
        for (int tick = 0; tick < 600 && boss.TakeDamage(0).Blocked; tick++)
            boss.Update(context);
        // Some encounters arm an opening act beat; let it finish so what
        // follows is measured against a settled boss.
        for (int tick = 0; tick < 900 && boss.PhaseInterludeActive; tick++)
            boss.Update(context);
        boss.DebugRebasePhaseHealth();
        int opening = boss.Phase;

        boss.TakeDamage(boss.MaxHp * 8.0);
        Assert.True(boss.PhaseDamageThresholdReached);
        Assert.Equal(opening, boss.Phase);

        for (int tick = 0;
             tick < Simulation.FrameRate * 40 && boss.Phase == opening; tick++)
            boss.Update(context);

        Assert.NotEqual(opening, boss.Phase);
        Assert.True(boss.PhaseClockElapsed
            <= BossPhaseGovernor.LowerTierHoldSeconds + 1.5,
            $"{key} held for {boss.PhaseClockElapsed:0.00}s of clock.");
    }

    [Theory]
    [InlineData("rot", 20.0, 25.0)]
    [InlineData("chronos", 20.0, 25.0)]
    [InlineData("ache", 20.0, 25.0)]
    public void SenseFinales_UseTheStandardizedSurvivalLengths(
        string key, double midpointSeconds, double finaleSeconds)
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        PathChaseBoss boss = MakeSenseFinale(key, battleground, 404);

        double midpoint = boss switch
        {
            Rot rot => rot.MidpointSurvivalDuration,
            Chronos chronos => chronos.MidpointSurvivalDuration,
            Ache ache => ache.MidpointSurvivalDuration,
            _ => midpointSeconds,
        };
        Assert.Equal(midpointSeconds, midpoint);
        Assert.Equal(finaleSeconds, boss.FinaleDuration);
    }

    [Theory]
    [InlineData("ishe", 20.0)]
    [InlineData("kage", 20.0)]
    [InlineData("bair", 20.0)]
    [InlineData("hypno", 20.0)]
    public void MidpointBosses_CloseOnATwentySecondSurvival(string key, double seconds)
    {
        double duration = key switch
        {
            "ishe" => new Ishe(1000, 1000, MakeBattleground(), new Random(1)).FlashSurvivalDuration,
            "kage" => Kage.StagnantMirrorDuration,
            "bair" => Bair.RuinDuration,
            _ => Hypno.ChosenSurvivalDuration,
        };
        Assert.Equal(seconds, duration);
        // Five seconds shorter than a sense finale's closing survival.
        Assert.Equal(25.0 - 5.0, duration);
    }

    [Theory]
    [InlineData("rot")]
    [InlineData("chronos")]
    [InlineData("ache")]
    [InlineData("malady")]
    public void SenseFinales_OpenAnInterludeOnEveryPhaseRotation(string key)
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        PathChaseBoss boss = MakeSenseFinale(key, battleground, 405);
        var context = Context(boss, battleground);
        boss.EntranceRemaining = 0;
        for (int tick = 0; tick < 600 && boss.TakeDamage(0).Blocked; tick++)
            boss.Update(context);
        // Some encounters arm an opening act beat; let it finish so what
        // follows is measured against a settled boss.
        for (int tick = 0; tick < 900 && boss.PhaseInterludeActive; tick++)
            boss.Update(context);
        boss.TransitionSweepRequested = false;
        boss.PhaseInterludeInvulnerabilitySeconds = 0;
        int opening = boss.Phase;

        boss.DebugCompletePhaseClock();
        for (int tick = 0; tick < 600 && boss.Phase == opening; tick++)
            boss.Update(context);
        Assert.NotEqual(opening, boss.Phase);

        // The beat sweeps the outgoing phase's shots and buys the player
        // grace for its whole length, because those shots are undodgeable.
        Assert.True(boss.PhaseInterludeActive);
        Assert.True(boss.TransitionSweepRequested);
        Assert.True(boss.PhaseInterludeInvulnerabilitySeconds > 0);
    }

    [Theory]
    [InlineData("rot")]
    [InlineData("chronos")]
    [InlineData("ache")]
    [InlineData("malady")]
    public void SenseFinales_HoldTheirFireForTheWholeInterlude(string key)
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        PathChaseBoss boss = MakeSenseFinale(key, battleground, 406);
        var context = Context(boss, battleground);
        boss.EntranceRemaining = 0;
        for (int tick = 0; tick < 600 && boss.TakeDamage(0).Blocked; tick++)
            boss.Update(context);
        // Some encounters arm an opening act beat; let it finish so what
        // follows is measured against a settled boss.
        for (int tick = 0; tick < 900 && boss.PhaseInterludeActive; tick++)
            boss.Update(context);
        int opening = boss.Phase;

        boss.DebugCompletePhaseClock();
        for (int tick = 0; tick < 600 && boss.Phase == opening; tick++)
            boss.Update(context);
        Assert.True(boss.PhaseInterludeActive);

        context.ProjectileSink.Clear();
        while (boss.PhaseInterludeActive)
            boss.Update(context);

        Assert.Empty(context.ProjectileSink);
    }

    [Theory]
    [InlineData("rot")]
    [InlineData("chronos")]
    [InlineData("ache")]
    [InlineData("malady")]
    public void SenseFinales_SettleBackTowardTheArenaCentreDuringTheInterlude(string key)
    {
        Simulation.ResetForTests();
        var battleground = MakeBattleground();
        PathChaseBoss boss = MakeSenseFinale(key, battleground, 407);
        var context = Context(boss, battleground);
        boss.EntranceRemaining = 0;
        for (int tick = 0; tick < 600 && boss.TakeDamage(0).Blocked; tick++)
            boss.Update(context);
        // Some encounters arm an opening act beat; let it finish so what
        // follows is measured against a settled boss.
        for (int tick = 0; tick < 900 && boss.PhaseInterludeActive; tick++)
            boss.Update(context);
        int opening = boss.Phase;

        boss.DebugCompletePhaseClock();
        for (int tick = 0; tick < 600 && boss.Phase == opening; tick++)
            boss.Update(context);
        Assert.True(boss.PhaseInterludeActive);

        float before = Vector2.Distance(
            new Vector2(boss.WorldX + boss.Size / 2f, boss.WorldY + boss.Size / 2f),
            boss.ArenaCenter);

        while (boss.PhaseInterludeActive)
            boss.Update(context);

        float after = Vector2.Distance(
            new Vector2(boss.WorldX + boss.Size / 2f, boss.WorldY + boss.Size / 2f),
            boss.ArenaCenter);
        // The beat always leaves the body settled near the middle of its own
        // arena, wherever the outgoing movement had wandered to. The lerp
        // itself is covered by BossPhaseInterludeTests.SettleToward.
        Assert.True(after <= Math.Max(before, boss.ArenaRadius * .25f),
            $"{key} ended {after:0} from centre, having started {before:0} away.");
    }

    [Fact]
    public void GuardiansHoldNoSurvivalPhase()
    {
        Simulation.ResetForTests();
        var layout = PathFloorGenerator.Generate("sound", 1, new Random(11));
        var boss = new PathGuardianBoss(
            layout.BossRoom.WorldCenter.X, layout.BossRoom.WorldCenter.Y,
            "sound", 1, float.PositiveInfinity, new Random(12));
        var context = new EnemyUpdateContext
        {
            PlayerWorldX = boss.WorldX + 400,
            PlayerWorldY = boss.WorldY,
            Battleground = layout.Battleground,
            BossAfflictions = new BossAfflictions(),
        };
        while (boss.EntranceRemaining > 0)
            boss.Update(context);

        for (int tick = 0; tick < Simulation.FrameRate * 240 && boss.Phase < 3; tick++)
        {
            boss.DebugCompletePhaseClock();
            boss.Update(context);
            Assert.False(boss.TrialActive);
        }

        Assert.Equal(3, boss.Phase);
        Assert.False(boss.TrialActive);
    }
}
