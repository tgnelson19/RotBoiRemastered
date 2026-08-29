using Microsoft.Xna.Framework;

namespace RotBoiRemastered.Systems;

/// <summary>
/// How long a damage phase is held open once the player has already dealt the
/// phase's damage threshold.
/// </summary>
public enum BossPhaseHoldStyle
{
    /// <summary>
    /// Tier-twenty sense finales. The phase always runs its authored time
    /// limit, so no amount of burst damage lets the player skip the pattern
    /// they were supposed to learn.
    /// </summary>
    FullTimer,

    /// <summary>
    /// Everything below a sense finale -- level-ten bosses, guardians and
    /// minibosses. The phase is held past the threshold only until the time
    /// limit or <see cref="BossPhaseGovernor.LowerTierHoldSeconds"/> pass,
    /// whichever lands first, so a well-armed player still gets a shorter
    /// fight without skipping the pattern outright.
    /// </summary>
    SevenSecondCap,
}

/// <summary>
/// Picks the next damage phase at random while refusing to repeat anything in
/// recent memory. Generalises the private no-repeat history Dissonance grew
/// for its own act rotation (`_damagePhaseHistory`) so every boss can rotate
/// its arsenal instead of walking a fixed authored order.
/// </summary>
public sealed class BossPhaseRotation
{
    /// <summary>
    /// Hard ceiling on remembered picks. The effective depth is smaller for
    /// small arsenals -- see <see cref="DepthFor"/>.
    /// </summary>
    public const int MaxHistoryDepth = 3;

    private readonly List<int> _history = new(MaxHistoryDepth);

    public IReadOnlyList<int> History => _history;

    public void Reset() => _history.Clear();

    /// <summary>
    /// A flat three-deep history starves a small arsenal: Rot and Chronos
    /// offer only two or three damage phases per half, so remembering three
    /// would empty the pool on every call and the relax path below would
    /// become the only path. Remember at most `count - 2` so a genuine choice
    /// always survives the filter.
    /// </summary>
    public static int DepthFor(int candidateCount) =>
        Math.Clamp(candidateCount - 2, 1, MaxHistoryDepth);

    /// <summary>
    /// Returns a phase from <paramref name="candidates"/> that is neither
    /// <paramref name="current"/> nor among the most recently returned picks.
    /// Never returns <paramref name="current"/> unless it is the only
    /// candidate -- several bosses early-return from their phase setter when
    /// handed the phase they are already in, which would strand the encounter
    /// on a phase whose timer had already expired.
    /// </summary>
    public int Choose(ReadOnlySpan<int> candidates, int current, Random rng)
    {
        if (candidates.Length == 0)
            throw new ArgumentException("A rotation needs at least one candidate.", nameof(candidates));
        if (candidates.Length == 1)
            return candidates[0];

        int depth = DepthFor(candidates.Length);
        Span<int> pool = candidates.Length <= 32
            ? stackalloc int[candidates.Length]
            : new int[candidates.Length];

        // Relax the memory oldest-first rather than giving up: with `depth`
        // honoured the pool is normally non-empty, but a caller that changes
        // its candidate set mid-fight (Rot and Chronos swap arsenals at the
        // midpoint survival) can still present a set the history covers.
        for (int allowed = depth; allowed >= 0; allowed--)
        {
            int size = 0;
            for (int index = 0; index < candidates.Length; index++)
            {
                int candidate = candidates[index];
                if (candidate == current)
                    continue;
                if (RecentlyUsed(candidate, allowed))
                    continue;
                pool[size++] = candidate;
            }
            if (size > 0)
            {
                int chosen = pool[rng.Next(size)];
                Remember(chosen, depth);
                return chosen;
            }
        }

        // Every candidate equals `current`; the guard above already handled a
        // single-entry set, so this only fires on a degenerate all-same set.
        Remember(current, depth);
        return current;
    }

    private bool RecentlyUsed(int candidate, int allowed)
    {
        int start = Math.Max(0, _history.Count - allowed);
        for (int index = start; index < _history.Count; index++)
        {
            if (_history[index] == candidate)
                return true;
        }
        return false;
    }

    private void Remember(int phase, int depth)
    {
        _history.Add(phase);
        while (_history.Count > depth)
            _history.RemoveAt(0);
    }
}

/// <summary>
/// Owns a damage phase's clock and its damage budget.
///
/// Before this existed every boss advanced on hardcoded health ratios, so a
/// player with enough damage could walk a fight from full to dead without
/// dodging a complete pattern. Advancement is now time-driven: the phase runs
/// its authored limit, and the health the boss is allowed to lose inside that
/// window is capped at <see cref="ThresholdFraction"/> of its maximum.
/// </summary>
public sealed class BossPhaseGovernor
{
    /// <summary>
    /// Share of a boss's maximum health the player may remove inside one
    /// damage phase. Reaching it does not end the phase -- it only stops the
    /// health bar, so the remaining time has to be survived rather than
    /// skipped.
    /// </summary>
    public const double DefaultThresholdFraction = .15;

    /// <summary>Hold granted past the threshold under <see cref="BossPhaseHoldStyle.SevenSecondCap"/>.</summary>
    public const double LowerTierHoldSeconds = 7.0;

    private double _thresholdReachedAt = -1;

    public BossPhaseHoldStyle HoldStyle { get; set; } = BossPhaseHoldStyle.FullTimer;
    public double ThresholdFraction { get; set; } = DefaultThresholdFraction;

    /// <summary>
    /// Set while the encounter is deliberately parked -- a debug phase lock,
    /// an entrance, a survival phase or a death spectacle. A suspended
    /// governor never reports <see cref="ReadyToAdvance"/>, which is what
    /// keeps `DebugSetPhase` authoritative for the debug console and tests.
    /// </summary>
    public bool Suspended { get; set; }

    public double Elapsed { get; private set; }
    public double TimeLimit { get; private set; }
    public double DamageDealtThisPhase { get; private set; }
    public int HpAtPhaseStart { get; private set; }
    public int MaxHpAtPhaseStart { get; private set; }

    public double DamageBudget =>
        Math.Max(1, MaxHpAtPhaseStart * ThresholdFraction);

    public bool ThresholdReached => _thresholdReachedAt >= 0;
    public double ThresholdReachedAt => _thresholdReachedAt;

    /// <summary>Fraction of the phase's clock spent, for arena rings and HUD readouts.</summary>
    public float Progress => TimeLimit <= 0
        ? 0f
        : (float)Math.Clamp(Elapsed / TimeLimit, 0.0, 1.0);

    public bool TimerExpired => TimeLimit > 0 && Elapsed >= TimeLimit;

    public bool ReadyToAdvance
    {
        get
        {
            if (Suspended || TimeLimit <= 0)
                return false;
            if (Elapsed >= TimeLimit)
                return true;
            return HoldStyle == BossPhaseHoldStyle.SevenSecondCap
                && ThresholdReached
                && Elapsed >= _thresholdReachedAt + LowerTierHoldSeconds;
        }
    }

    public void BeginPhase(double timeLimit, int hpAtPhaseStart, int maxHp)
    {
        TimeLimit = Math.Max(0, timeLimit);
        Elapsed = 0;
        DamageDealtThisPhase = 0;
        _thresholdReachedAt = -1;
        HpAtPhaseStart = hpAtPhaseStart;
        MaxHpAtPhaseStart = Math.Max(1, maxHp);
    }

    /// <summary>
    /// Re-baselines the health the current phase's budget is measured from,
    /// without disturbing the clock. Needed wherever a boss writes `Hp`
    /// outside a phase change -- survival entry and exit, finale entry, and
    /// New Game+ rescaling all do -- because a stale baseline would otherwise
    /// make the very next phase read as already over budget and block all
    /// damage.
    /// </summary>
    public void RebaseHealth(int hp, int maxHp)
    {
        HpAtPhaseStart = hp;
        MaxHpAtPhaseStart = Math.Max(1, maxHp);
        DamageDealtThisPhase = 0;
        _thresholdReachedAt = -1;
    }

    public void Tick(double dt)
    {
        if (dt > 0)
            Elapsed += dt;
    }

    /// <summary>
    /// Records health actually removed, not damage requested. Several bosses
    /// scale the incoming request before applying it (Chronos's temporal
    /// fracture, the chemesthesis stagger multiplier, Dissonance's flat .45
    /// reduction), so callers pass `previousHp - Hp`.
    /// </summary>
    public void RecordDamage(double applied)
    {
        if (applied <= 0)
            return;
        DamageDealtThisPhase += applied;
        if (_thresholdReachedAt < 0 && DamageDealtThisPhase >= DamageBudget)
            _thresholdReachedAt = Elapsed;
    }

    /// <summary>
    /// The lowest health this phase may leave the boss at: whichever is
    /// higher of the next authored gate and the phase's own damage budget.
    /// </summary>
    public int DamageFloor(int nextGateHp) => Math.Max(
        nextGateHp,
        (int)Math.Round(HpAtPhaseStart - DamageBudget));
}

/// <summary>
/// The signature flourish a boss plays while it returns to the arena centre
/// between phases. Each sense finale gets its own so the beat reads as that
/// boss regrouping rather than as a shared cutscene.
/// </summary>
public enum BossInterludeStyle
{
    /// <summary>Neutral settle -- guardians and minibosses.</summary>
    Settle,

    /// <summary>Rot: the body sinks, drags inward, and swells back up.</summary>
    Compost,

    /// <summary>Chronos: the body rewinds along its own path, spinning backwards.</summary>
    Rewind,

    /// <summary>Ache: a hard flinch outward followed by a snapped recoil to centre.</summary>
    Recoil,

    /// <summary>Malady: a slow, wide, deliberate curtain sweep.</summary>
    Curtain,

    /// <summary>Dissonance: the cube rig scatters on the beat and reassembles.</summary>
    Chord,

    /// <summary>Aphantasia: the void inhales and the tentacles bloom.</summary>
    Eclipse,
}

/// <summary>
/// The between-phase beat every boss now plays: firing stops, the outgoing
/// phase's shots are swept off the arena, the player is granted grace for the
/// duration (those accelerating shots are close to undodgeable), and the body
/// travels back to the arena centre with a per-boss flourish.
///
/// Modelled on Aphantasia's `BeginPhaseHandoff`/`UpdatePhaseHandoff`, which
/// was the only encounter with a real transition before this.
/// </summary>
public sealed class BossPhaseInterlude
{
    /// <summary>
    /// Default beat length. Long enough for a swept shot to clear the arena
    /// at <c>TransitionSweepAcceleration</c> and for the return travel to
    /// read as deliberate, short enough not to stall the fight.
    /// </summary>
    public const double DefaultDuration = 2.6;

    /// <summary>Rate of the exponential settle toward the arena centre.</summary>
    private const float SettleRate = 2.25f;

    public BossInterludeStyle Style { get; set; } = BossInterludeStyle.Settle;
    public double Duration { get; private set; }
    public double Remaining { get; private set; }
    public bool Active => Remaining > 0;

    /// <summary>0 at the start of the beat, 1 at its end.</summary>
    public float Progress => Duration <= 0
        ? 1f
        : (float)Math.Clamp(1.0 - Remaining / Duration, 0.0, 1.0);

    public void Reset()
    {
        Duration = 0;
        Remaining = 0;
    }

    /// <summary>
    /// Starts (or extends) the beat. Returns true only on a genuine start, so
    /// callers latch the projectile sweep and the invulnerability request
    /// once instead of re-arming them every frame -- several bosses call their
    /// phase setter unconditionally from `UpdatePhase`.
    /// </summary>
    public bool Begin(double duration = DefaultDuration)
    {
        bool fresh = !Active;
        Duration = Math.Max(Duration, duration);
        Remaining = Math.Max(Remaining, duration);
        return fresh;
    }

    public void Tick(double dt)
    {
        if (!Active)
            return;
        Remaining = Math.Max(0, Remaining - dt);
        if (Remaining <= 0)
            Duration = 0;
    }

    /// <summary>Exponential settle toward the arena centre, framerate independent.</summary>
    public static Vector2 SettleToward(Vector2 current, Vector2 center, double dt)
    {
        float blend = 1f - MathF.Exp(-SettleRate * (float)dt);
        return Vector2.Lerp(current, center, blend);
    }

    /// <summary>
    /// Eased 0 -> 1 -> 0 arc over the beat, the shared envelope every
    /// flourish below is shaped from.
    /// </summary>
    public float Swell => MathF.Sin(Progress * MathF.PI);

    /// <summary>Body rotation, in radians, contributed by the flourish.</summary>
    public float Spin => Style switch
    {
        BossInterludeStyle.Rewind => -MathHelper.TwoPi * Progress,
        BossInterludeStyle.Chord => MathHelper.TwoPi * Progress * Progress,
        BossInterludeStyle.Recoil => MathHelper.PiOver4 * MathF.Sin(Progress * MathF.PI * 3f),
        BossInterludeStyle.Compost => MathHelper.PiOver2 * Progress,
        BossInterludeStyle.Curtain => MathHelper.Pi * Progress,
        BossInterludeStyle.Eclipse => MathHelper.TwoPi * Progress * .5f,
        _ => MathHelper.PiOver4 * Swell,
    };

    /// <summary>Uniform body scale contributed by the flourish.</summary>
    public float Scale => Style switch
    {
        BossInterludeStyle.Compost => 1f - .34f * Swell,
        BossInterludeStyle.Recoil => 1f + .28f * Swell,
        BossInterludeStyle.Curtain => 1f + .16f * Swell,
        BossInterludeStyle.Chord => 1f - .18f * Swell,
        BossInterludeStyle.Eclipse => 1f + .22f * Swell,
        BossInterludeStyle.Rewind => 1f - .12f * Swell,
        _ => 1f + .1f * Swell,
    };

    /// <summary>
    /// How far the flourish's satellite geometry (orbiting cubes, arms,
    /// petals) is thrown out from the body, as a multiplier on its rest
    /// radius.
    /// </summary>
    public float Detach => Style switch
    {
        BossInterludeStyle.Chord => 1f + 1.5f * Swell,
        BossInterludeStyle.Recoil => 1f + 1.15f * Swell,
        BossInterludeStyle.Eclipse => 1f + .95f * Swell,
        BossInterludeStyle.Curtain => 1f + .8f * Swell,
        BossInterludeStyle.Compost => 1f - .45f * Swell,
        BossInterludeStyle.Rewind => 1f + .6f * Swell,
        _ => 1f + .5f * Swell,
    };
}

/// <summary>
/// The shared difficulty curve applied on top of every boss's authored
/// baseline. Values above one mean "more" (shots per volley, travel speed,
/// waveform amplitude); <see cref="Cadence"/> is below one because it scales
/// the delay between declarations.
/// </summary>
public readonly record struct BossDifficultyScalars(
    double VolleyCount,
    double ProjectileSpeed,
    double Cadence,
    double SpreadVariance,
    double SineAmplitude)
{
    /// <summary>Sense finales: the full requested escalation.</summary>
    public static readonly BossDifficultyScalars Finale =
        new(1.45, 1.22, .70, 1.35, 1.40);

    /// <summary>Level-ten bosses: the same grammar, a step gentler.</summary>
    public static readonly BossDifficultyScalars Midpoint =
        new(1.35, 1.16, .76, 1.25, 1.30);

    /// <summary>Guardians and minibosses: enough to feel the change, not enough to wall a run.</summary>
    public static readonly BossDifficultyScalars Guardian =
        new(1.30, 1.12, .80, 1.20, 1.22);

    /// <summary>Scales an authored shot count, never dropping below the original.</summary>
    public int Shots(int authored) =>
        Math.Max(authored, (int)Math.Round(authored * VolleyCount));

    public float Speed(float authored) => (float)(authored * ProjectileSpeed);

    public double Delay(double authored) => authored * Cadence;

    public float Amplitude(float authored) => (float)(authored * SineAmplitude);
}
