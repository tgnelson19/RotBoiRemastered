using RotBoiRemastered.Systems;

namespace RotBoiRemastered.Tests.Systems;

public sealed class BossPhaseRotationTests
{
    [Fact]
    public void Choose_NeverReturnsTheCurrentPhase()
    {
        var rotation = new BossPhaseRotation();
        var rng = new Random(1234);
        ReadOnlySpan<int> candidates = stackalloc int[] { 1, 2, 4, 5, 7, 8 };
        int current = 1;
        for (int step = 0; step < 500; step++)
        {
            int next = rotation.Choose(candidates, current, rng);
            Assert.NotEqual(current, next);
            Assert.Contains(next, candidates.ToArray());
            current = next;
        }
    }

    [Fact]
    public void Choose_DoesNotRepeatWithinItsHistoryDepth()
    {
        var rotation = new BossPhaseRotation();
        var rng = new Random(99);
        int[] candidates = { 1, 2, 4, 5, 7, 8 };
        int depth = BossPhaseRotation.DepthFor(candidates.Length);
        Assert.Equal(3, depth);

        var recent = new List<int>();
        int current = 1;
        for (int step = 0; step < 400; step++)
        {
            int next = rotation.Choose(candidates, current, rng);
            Assert.DoesNotContain(next, recent);
            recent.Add(next);
            if (recent.Count > depth)
                recent.RemoveAt(0);
            current = next;
        }
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 2)]
    [InlineData(5, 3)]
    [InlineData(9, 3)]
    public void DepthFor_ShrinksForSmallArsenalsSoAChoiceAlwaysSurvives(int candidates, int expected) =>
        Assert.Equal(expected, BossPhaseRotation.DepthFor(candidates));

    [Fact]
    public void Choose_WithTwoCandidates_AlternatesInsteadOfDeadlocking()
    {
        var rotation = new BossPhaseRotation();
        var rng = new Random(7);
        int[] candidates = { 5, 6 };
        int current = 5;
        for (int step = 0; step < 40; step++)
        {
            int next = rotation.Choose(candidates, current, rng);
            Assert.NotEqual(current, next);
            current = next;
        }
    }

    [Fact]
    public void Choose_WithASingleCandidate_ReturnsIt()
    {
        var rotation = new BossPhaseRotation();
        Assert.Equal(3, rotation.Choose(stackalloc int[] { 3 }, current: 3, new Random(1)));
    }

    [Fact]
    public void Choose_SwitchingArsenalsMidFight_RelaxesHistoryInsteadOfThrowing()
    {
        var rotation = new BossPhaseRotation();
        var rng = new Random(3);
        // Drive the pre-midpoint arsenal until the history is saturated...
        int[] early = { 1, 2, 3 };
        int current = 1;
        for (int step = 0; step < 10; step++)
            current = rotation.Choose(early, current, rng);

        // ...then hand it a disjoint post-midpoint arsenal, the shape Rot and
        // Chronos take when their midpoint survival completes.
        int next = rotation.Choose(stackalloc int[] { 5, 6 }, current, rng);
        Assert.Contains(next, new[] { 5, 6 });
    }

    [Fact]
    public void Reset_ClearsTheMemory()
    {
        var rotation = new BossPhaseRotation();
        rotation.Choose(stackalloc int[] { 1, 2, 3, 4 }, current: 1, new Random(5));
        Assert.NotEmpty(rotation.History);
        rotation.Reset();
        Assert.Empty(rotation.History);
    }
}

public sealed class BossPhaseGovernorTests
{
    private static BossPhaseGovernor Governor(
        double timeLimit = 20.0,
        int hp = 100_000,
        int maxHp = 100_000,
        BossPhaseHoldStyle hold = BossPhaseHoldStyle.FullTimer)
    {
        var governor = new BossPhaseGovernor { HoldStyle = hold };
        governor.BeginPhase(timeLimit, hp, maxHp);
        return governor;
    }

    [Fact]
    public void FullTimer_HoldsThePhaseOpenAfterTheThresholdUntilTheLimit()
    {
        var governor = Governor();
        governor.Tick(2.0);
        governor.RecordDamage(20_000); // well past the 15% budget
        Assert.True(governor.ThresholdReached);

        governor.Tick(10.0);
        Assert.False(governor.ReadyToAdvance);

        governor.Tick(8.0);
        Assert.True(governor.ReadyToAdvance);
    }

    [Fact]
    public void SevenSecondCap_ReleasesEarlyOnceTheThresholdIsSevenSecondsOld()
    {
        var governor = Governor(timeLimit: 20.0, hold: BossPhaseHoldStyle.SevenSecondCap);
        governor.Tick(2.0);
        governor.RecordDamage(20_000);

        governor.Tick(6.0);
        Assert.False(governor.ReadyToAdvance);

        governor.Tick(1.5);
        Assert.True(governor.ReadyToAdvance);
        Assert.True(governor.Elapsed < governor.TimeLimit);
    }

    [Fact]
    public void SevenSecondCap_WithoutTheThreshold_StillRunsTheFullTimer()
    {
        var governor = Governor(timeLimit: 15.0, hold: BossPhaseHoldStyle.SevenSecondCap);
        governor.Tick(14.0);
        Assert.False(governor.ThresholdReached);
        Assert.False(governor.ReadyToAdvance);
        governor.Tick(1.5);
        Assert.True(governor.ReadyToAdvance);
    }

    [Fact]
    public void Suspended_NeverAdvances()
    {
        var governor = Governor(timeLimit: 5.0);
        governor.Suspended = true;
        governor.Tick(60.0);
        governor.RecordDamage(50_000);
        Assert.False(governor.ReadyToAdvance);

        governor.Suspended = false;
        Assert.True(governor.ReadyToAdvance);
    }

    [Fact]
    public void DamageFloor_CapsThePhaseAtFifteenPercentOfMaximumHealth()
    {
        var governor = Governor(hp: 100_000, maxHp: 100_000);
        Assert.Equal(85_000, governor.DamageFloor(nextGateHp: 1));
    }

    [Fact]
    public void DamageFloor_NeverDropsBelowTheNextAuthoredGate()
    {
        var governor = Governor(hp: 60_000, maxHp: 100_000);
        // 15% of maximum would allow 45,000, but the midpoint gate sits higher.
        Assert.Equal(50_000, governor.DamageFloor(nextGateHp: 50_000));
    }

    [Fact]
    public void RecordDamage_AccumulatesAcrossHitsRatherThanRequiringOneBigOne()
    {
        var governor = Governor(maxHp: 100_000);
        for (int hit = 0; hit < 14; hit++)
            governor.RecordDamage(1_000);
        Assert.False(governor.ThresholdReached);

        governor.RecordDamage(1_000);
        Assert.True(governor.ThresholdReached);
    }

    [Fact]
    public void RebaseHealth_MovesTheBudgetBaselineWithoutResettingTheClock()
    {
        var governor = Governor(hp: 100_000, maxHp: 100_000);
        governor.Tick(8.0);
        governor.RecordDamage(20_000);

        governor.RebaseHealth(50_000, 100_000);
        Assert.Equal(8.0, governor.Elapsed, 3);
        Assert.False(governor.ThresholdReached);
        Assert.Equal(35_000, governor.DamageFloor(nextGateHp: 1));
    }

    [Fact]
    public void Progress_TracksTheClockForArenaRings()
    {
        var governor = Governor(timeLimit: 20.0);
        governor.Tick(5.0);
        Assert.Equal(.25f, governor.Progress, 3);
        governor.Tick(40.0);
        Assert.Equal(1f, governor.Progress, 3);
    }
}

public sealed class BossPhaseInterludeTests
{
    [Fact]
    public void Begin_ReportsAFreshStartOnlyOnce()
    {
        var interlude = new BossPhaseInterlude();
        Assert.True(interlude.Begin(2.6));
        Assert.False(interlude.Begin(2.6));

        interlude.Tick(3.0);
        Assert.False(interlude.Active);
        Assert.True(interlude.Begin(2.6));
    }

    [Fact]
    public void Tick_RunsTheBeatToCompletion()
    {
        var interlude = new BossPhaseInterlude();
        interlude.Begin(2.0);
        Assert.Equal(0f, interlude.Progress, 3);
        interlude.Tick(1.0);
        Assert.Equal(.5f, interlude.Progress, 3);
        interlude.Tick(1.0);
        Assert.False(interlude.Active);
    }

    [Fact]
    public void SettleToward_ConvergesOnTheArenaCentre()
    {
        var center = new Microsoft.Xna.Framework.Vector2(500, 500);
        var current = new Microsoft.Xna.Framework.Vector2(100, 900);
        for (int step = 0; step < 240; step++)
            current = BossPhaseInterlude.SettleToward(current, center, 1.0 / 120.0);
        Assert.True(Microsoft.Xna.Framework.Vector2.Distance(current, center) < 40f);
    }

    [Theory]
    [InlineData(BossInterludeStyle.Compost)]
    [InlineData(BossInterludeStyle.Rewind)]
    [InlineData(BossInterludeStyle.Recoil)]
    [InlineData(BossInterludeStyle.Curtain)]
    [InlineData(BossInterludeStyle.Chord)]
    [InlineData(BossInterludeStyle.Eclipse)]
    [InlineData(BossInterludeStyle.Settle)]
    public void EveryStyle_ReturnsToRestAtBothEndsOfTheBeat(BossInterludeStyle style)
    {
        var interlude = new BossPhaseInterlude { Style = style };
        interlude.Begin(2.0);
        Assert.Equal(1f, interlude.Scale, 2);
        Assert.Equal(1f, interlude.Detach, 2);

        interlude.Tick(1.0);
        Assert.True(Math.Abs(interlude.Scale - 1f) > .01f
            || Math.Abs(interlude.Detach - 1f) > .01f);

        interlude.Tick(1.0);
        Assert.Equal(1f, interlude.Scale, 2);
        Assert.Equal(1f, interlude.Detach, 2);
    }

    [Fact]
    public void StylesAreDistinguishableAtTheMidpoint()
    {
        var seen = new HashSet<(float, float, float)>();
        foreach (BossInterludeStyle style in Enum.GetValues<BossInterludeStyle>())
        {
            var interlude = new BossPhaseInterlude { Style = style };
            interlude.Begin(2.0);
            interlude.Tick(1.0);
            seen.Add((interlude.Spin, interlude.Scale, interlude.Detach));
        }
        Assert.Equal(Enum.GetValues<BossInterludeStyle>().Length, seen.Count);
    }
}

public sealed class BossDifficultyScalarsTests
{
    [Fact]
    public void FinaleScalarsSitInsideTheRequestedThirtyToFiftyPercentBand()
    {
        var scalars = BossDifficultyScalars.Finale;
        Assert.InRange(scalars.VolleyCount, 1.30, 1.50);
        Assert.InRange(scalars.SineAmplitude, 1.30, 1.50);
        Assert.InRange(scalars.ProjectileSpeed, 1.10, 1.30);
        Assert.InRange(scalars.Cadence, .65, .85);
    }

    [Fact]
    public void EscalationOrdersFinaleAboveMidpointAboveGuardian()
    {
        Assert.True(BossDifficultyScalars.Finale.VolleyCount
            > BossDifficultyScalars.Midpoint.VolleyCount);
        Assert.True(BossDifficultyScalars.Midpoint.VolleyCount
            > BossDifficultyScalars.Guardian.VolleyCount);
        Assert.True(BossDifficultyScalars.Finale.Cadence
            < BossDifficultyScalars.Guardian.Cadence);
    }

    [Fact]
    public void Shots_NeverReturnsFewerThanTheAuthoredCount()
    {
        var scalars = BossDifficultyScalars.Finale;
        Assert.Equal(1, scalars.Shots(1));
        Assert.Equal(12, scalars.Shots(8));
        Assert.True(scalars.Shots(20) >= 20);
    }
}
