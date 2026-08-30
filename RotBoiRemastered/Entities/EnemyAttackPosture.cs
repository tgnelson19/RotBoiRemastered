namespace RotBoiRemastered.Entities;

/// <summary>
/// Where an enemy is in the wind-up / release / recover cycle of a declared
/// attack.
/// </summary>
public enum EnemyAttackStance
{
    /// <summary>Not committed to anything; free to move and to start an attack.</summary>
    Idle,

    /// <summary>
    /// Committed and charging. The enemy is braced -- direct damage is refused
    /// and a thin ring is drawn around it -- so the window reads as "dodge the
    /// tell, do not trade with it".
    /// </summary>
    Windup,

    /// <summary>
    /// A zero-duration latch on the single frame the wind-up completes, so an
    /// emitter can ask "is this my frame to fire?" rather than reconstructing
    /// the edge from timers.
    /// </summary>
    Release,

    /// <summary>
    /// The follow-through. The enemy is fully vulnerable and cannot start
    /// another attack -- this is the punish window the wind-up paid for.
    /// </summary>
    Recover,
}

/// <summary>
/// The wind-up clock shared by every enemy that declares an attack.
///
/// Before this, an ordinary enemy fired the instant its cooldown expired, with
/// no tell and no consequence for standing next to it. A declared attack now
/// costs the enemy a braced wind-up (during which it cannot be damaged) and
/// buys the player a recovery window (during which it cannot fight back).
///
/// Ticked from <see cref="Enemy.AdvanceAge"/> rather than from `Update`.
/// `AdvanceAge` is the one method every enemy's frame reaches exactly once,
/// including the subclasses that replace `Update` wholesale and return early
/// -- so the clock can never freeze the way a clock ticked in a base `Update`
/// body would.
/// </summary>
public sealed class EnemyAttackPosture
{
    /// <summary>
    /// Ceiling on how much of an attack's cooldown may be spent braced. Keeps
    /// an enemy vulnerable for the clear majority of its cycle, so a crowd of
    /// them can never read as untouchable.
    /// </summary>
    public const double MaxWindupShareOfCooldown = .35;

    /// <summary>
    /// How long an unconsumed release latch is held before the posture gives
    /// up and recovers on its own. Only reached when an enemy loses its target
    /// mid-wind-up and never calls <see cref="ConsumeRelease"/>.
    /// </summary>
    private const double AbandonedReleaseSeconds = .5;

    private double _remaining;

    public EnemyAttackStance Stance { get; private set; } = EnemyAttackStance.Idle;

    /// <summary>
    /// Whether this enemy's wind-up actually confers immunity. Easy-tier
    /// enemies still wind up -- the tell is what makes an attack readable --
    /// but they stay killable throughout, so early floors are not a wall of
    /// ringed trash and the ring keeps meaning something where it appears.
    /// </summary>
    public bool WindupGrantsImmunity { get; set; }

    public double WindupSeconds { get; private set; }
    public double RecoverSeconds { get; private set; }

    /// <summary>True while direct damage should be refused.</summary>
    public bool Invincible =>
        Stance == EnemyAttackStance.Windup && WindupGrantsImmunity;

    /// <summary>True while the enemy is committed and must not start something else.</summary>
    public bool Busy => Stance is EnemyAttackStance.Windup or EnemyAttackStance.Recover;

    /// <summary>0 at the start of the wind-up, 1 at its end. Drives the ring's intensity.</summary>
    public float WindupProgress => Stance == EnemyAttackStance.Windup && WindupSeconds > 0
        ? (float)Math.Clamp(1.0 - _remaining / WindupSeconds, 0.0, 1.0)
        : 0f;

    /// <summary>
    /// Commits to an attack. Returns false when already committed, so callers
    /// can drive this straight from a cooldown check without guarding.
    /// </summary>
    public bool BeginWindup(double windupSeconds, double recoverSeconds, bool grantsImmunity)
    {
        if (Busy)
            return false;
        WindupSeconds = Math.Max(0, windupSeconds);
        RecoverSeconds = Math.Max(0, recoverSeconds);
        WindupGrantsImmunity = grantsImmunity;
        _remaining = WindupSeconds;
        Stance = WindupSeconds > 0 ? EnemyAttackStance.Windup : EnemyAttackStance.Release;
        return true;
    }

    /// <summary>
    /// Consumes the one-frame release latch. Returns true exactly once per
    /// committed attack, on the frame the wind-up completes.
    /// </summary>
    public bool ConsumeRelease()
    {
        if (Stance != EnemyAttackStance.Release)
            return false;
        Stance = RecoverSeconds > 0 ? EnemyAttackStance.Recover : EnemyAttackStance.Idle;
        _remaining = RecoverSeconds;
        return true;
    }

    /// <summary>Abandons any commitment -- used when an enemy dies, is staggered, or resets.</summary>
    public void Reset()
    {
        Stance = EnemyAttackStance.Idle;
        _remaining = 0;
        WindupSeconds = 0;
        RecoverSeconds = 0;
    }

    /// <summary>
    /// Advances the clock. Called once per frame from
    /// <see cref="Enemy.AdvanceAge"/>; see the class comment for why that is
    /// the correct hook rather than `Update`.
    /// </summary>
    public void Tick(double seconds)
    {
        switch (Stance)
        {
            case EnemyAttackStance.Windup:
                _remaining -= seconds;
                if (_remaining <= 0)
                    Stance = EnemyAttackStance.Release;
                break;
            case EnemyAttackStance.Recover:
                _remaining -= seconds;
                if (_remaining <= 0)
                    Stance = EnemyAttackStance.Idle;
                break;
            case EnemyAttackStance.Release:
                // Held until an emitter consumes it. If nothing does -- an
                // enemy that lost its target mid-wind-up -- fall through to
                // recovery after a grace period so the stance can never wedge.
                _remaining -= seconds;
                if (_remaining <= -AbandonedReleaseSeconds)
                    ConsumeRelease();
                break;
        }
    }
}
