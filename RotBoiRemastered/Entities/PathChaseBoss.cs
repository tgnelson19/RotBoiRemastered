using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.Systems;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// Configurable three-phase placeholder for rapidly prototyping path bosses.
/// Ported from bossTypes.py's `PathChaseBoss` class-attribute config (every
/// field below was an overridable Python class attribute -- `bossName`,
/// `phaseLabels`, `bodyColor`, `movementSpeed`, ... -- inherited/overridden
/// per concrete subclass). Rather than mirror that with C# virtual
/// properties (which would require calling virtual members from the base
/// constructor to compute `size`/`speed`/etc. before `base(...)` runs -- a
/// well-known C# hazard), each subclass builds one of these immutable
/// records instead and passes it up explicitly. Subclasses of a subclass
/// (e.g. `Chronos` overriding `Ishe`) use a `with` expression against the
/// parent's config, mirroring Python's partial class-attribute override
/// exactly.
/// </summary>
public sealed record PathChaseBossConfig(
    string BossName,
    string Subtitle,
    IReadOnlyList<string> PhaseLabels,
    bool FinalBoss,
    string Pattern,
    string OwnerPrefix,
    Color BodyColor,
    Color FinalBodyColor,
    Color AccentColor,
    Color FinalAccentColor,
    double MovementSpeed,
    double BodyScale,
    double FinalBodyScale,
    double CooldownSeconds,
    double FinalCooldownSeconds,
    double ShotSpeed,
    double FinalShotSpeed,
    double ShotDamage,
    double FinalShotDamage,
    double ShotScale,
    double FinalShotScale,
    double ShotRangeTiles,
    string ArenaShape,
    double ArenaScale,
    BossMotionTheme MotionTheme,
    IReadOnlyList<BossMovementPhaseProfile> MovementPhases,
    int MidHealth = 29000,
    int FinalHealth = 240000,
    int MidContactDamage = 270,
    int FinalContactDamage = 780,
    double MidRewardExperience = 280,
    double FinalRewardExperience = 760,
    double FinaleDuration = 40.0)
{
    public static readonly PathChaseBossConfig Default = new(
        BossName: "PATH BOSS", Subtitle: "CONTENT PLACEHOLDER",
        PhaseLabels: new[] { "HUNT", "PRESS", "OVERWHELM" }, FinalBoss: false,
        Pattern: "fan", OwnerPrefix: "path",
        BodyColor: new Color(91, 103, 53), FinalBodyColor: new Color(48, 82, 48),
        AccentColor: new Color(132, 119, 63), FinalAccentColor: new Color(74, 125, 67),
        MovementSpeed: .21, BodyScale: 1.9, FinalBodyScale: 2.35,
        CooldownSeconds: 2.85, FinalCooldownSeconds: 2.35,
        ShotSpeed: .68, FinalShotSpeed: .82, ShotDamage: 275, FinalShotDamage: 360,
        ShotScale: .30, FinalShotScale: .34, ShotRangeTiles: 18,
        ArenaShape: "circle", ArenaScale: 10.4,
        MotionTheme: BossMotionTheme.Touch,
        MovementPhases: new[]
        {
            BossMovementPhaseProfile.Chase(),
            BossMovementPhaseProfile.Stationary(),
            BossMovementPhaseProfile.Fixed(BossPathShape.Circle, 12f),
        });
}

/// <summary>
/// Shared base for the alternate mid/final bosses used by the non-"sound"
/// content paths. Natural path selection is mapped by <c>GamePaths</c> and
/// <c>BossCatalog</c>; legacy bosses can still be selected by their debug keys.
///
/// Cleanup vs. the Python original:
/// - `stagger`/`maxStagger`/`isStaggered`/`perfectStagger`/
///   `staggerRecoveryRemaining`/`runeSilenceRemaining`/`survivalActive`/
///   `survivalRemaining` are all set in Python's `__init__` but never read
///   by `PathChaseBoss` itself, `Ishe`/`Chronos`, or the Touch family
///   (`PlagueTouchBoss`/`Bair`/`Sting`) -- confirmed by reading every
///   method on all of them. They're only meaningful on
///   `SinChemesthesisBoss` (which owns its real stagger system). They remain
///   out of this base so other path families do not carry unused state.
/// - `ArenaCenter`/`ArenaRadius` are computed once from an explicit
///   `Battleground` constructor parameter (same cleanup as `Dissonance`'s
///   `_arena_center()` -> cached field) instead of reading a
///   `background.py` global from both update- and draw-side methods.
/// </summary>
public class PathChaseBoss : Enemy, IBossArenaController, IBossArenaOcclusion
{
    protected readonly Random Rng;
    protected PathChaseBossConfig Config { get; }

    /// <summary>
    /// This boss's authored phase names in order (index 0 = phase 1), for the
    /// debug console's `/testphase` numbered-phase list -- see
    /// <see cref="RotBoiRemastered.Systems.GameSession.DebugTestPhaseOptions"/>.
    /// </summary>
    public IReadOnlyList<string> PhaseLabels => Config.PhaseLabels;
    public Vector2 ArenaCenter { get; }
    public float ArenaRadius { get; }
    public float Contraction => 0f;
    IReadOnlyList<Rectangle> IBossArenaController.MovementObstacles => Array.Empty<Rectangle>();

    public int Phase { get; protected set; } = 1;
    public string PhaseLabel { get; protected set; }
    public string PhaseFlavor { get; protected set; }
    public Color PhaseAccent { get; protected set; }
    public string BossDisplayName => Config.BossName;
    public BossPresentationProfile PresentationProfile { get; }
    public double EntranceRemaining { get; set; } = .9;
    public bool DebugPhaseLocked { get; set; }
    public double PhaseElapsed { get; set; }
    public double PhaseTimeLimit { get; }
    /// <summary>
    /// Seconds elapsed for the arena boundary's "lit" telegraph ring only --
    /// deliberately separate from <see cref="PhaseElapsed"/>, which is reset
    /// to 0 on every ordinary phase change (see ApplyPhase/ApplyIshePhase
    /// overrides) and once at finale entry (BeginFinaleSequence). Driving the
    /// ring from PhaseElapsed used to make it visibly snap backward at the
    /// exact frame the boss freezes into its transition pose -- this field
    /// only ever counts up, so the ring's cycle never jumps.
    /// </summary>
    public double ArenaRingSeconds { get; set; }
    protected readonly float[] ArenaSeed;
    private readonly Vector2[] _arenaWorldVertices;
    private readonly Vector2[] _arenaScreenVertices;
    private readonly EnemyUpdateContext _movementUpdateContext;
    private readonly BossLocomotionController _locomotion;
    private float _arenaVerticesAge = float.NaN;
    public bool Dying { get; protected set; }
    public double DeathRemaining { get; protected set; }
    public double DeathDuration { get; }
    public bool FinaleActive { get; protected set; }
    public double FinaleRemaining { get; protected set; }
    public double FinaleDuration { get; }
    public double VisualTransitionRemaining { get; protected set; }

    protected virtual bool VisualSurvivalActive => EntranceRemaining > 0 || VisualTransitionRemaining > 0 || FinaleActive;
    public bool PresentationSurvivalActive => VisualSurvivalActive;
    protected virtual bool UsesSharedDeathSpectacle => true;
    protected virtual bool UsesFinaleSequence => Config.FinalBoss;
    protected float DeathProgress => Dying ? (float)Math.Clamp(1.0 - DeathRemaining / DeathDuration, 0.0, 1.0) : 0f;
    public double FinaleProgress => FinaleActive ? Math.Clamp(1.0 - FinaleRemaining / FinaleDuration, 0.0, 1.0) : 0.0;

    // ---- Shared phase choreography ------------------------------------
    // Every boss used to advance on hardcoded health ratios, which let a
    // high-damage player walk a fight without ever completing a pattern.
    // The governor below makes advancement time-driven and caps how much
    // health one phase may surrender; the rotation picks the next phase at
    // random instead of walking a fixed order; the interlude is the beat
    // between phases. See Systems/BossPhaseChoreography.cs.

    protected readonly BossPhaseGovernor PhaseGovernor = new();
    protected readonly BossPhaseRotation PhaseRotation = new();
    protected readonly BossPhaseInterlude PhaseInterlude = new();

    /// <summary>
    /// Sense finales ride the whole phase timer; everything below one is
    /// released seven seconds after the player hits the damage threshold so
    /// the shorter fights stay short.
    /// </summary>
    protected virtual BossPhaseHoldStyle PhaseHoldStyle => Config.FinalBoss
        ? BossPhaseHoldStyle.FullTimer
        : BossPhaseHoldStyle.SevenSecondCap;

    /// <summary>The signature flourish played while returning to the arena centre.</summary>
    protected virtual BossInterludeStyle InterludeStyle => BossInterludeStyle.Settle;

    protected virtual double InterludeDuration => BossPhaseInterlude.DefaultDuration;

    /// <summary>
    /// Authored seconds for a damage phase. Finales sit at the long end of
    /// the 15-25s band, mid bosses at the short end; override to weight an
    /// individual phase by how intense it is.
    /// </summary>
    protected virtual double PhaseTimeLimitFor(int phase) => Config.FinalBoss ? 21.0 : 17.0;

    /// <summary>Difficulty curve applied on top of this boss's authored baseline.</summary>
    protected new virtual BossDifficultyScalars Difficulty => Config.FinalBoss
        ? BossDifficultyScalars.Finale
        : BossDifficultyScalars.Midpoint;

    /// <summary>
    /// This boss's own survival phase, if it has one. Deliberately separate
    /// from <see cref="VisualSurvivalActive"/>, which also folds in the
    /// purely cosmetic <see cref="VisualTransitionRemaining"/> -- parking the
    /// phase clock on a visual timer would stall the fight's opening beat.
    /// </summary>
    protected virtual bool EncounterSurvivalActive => false;

    /// <summary>
    /// True while the encounter is deliberately parked and the phase clock
    /// must not advance the fight: the entrance, a survival phase, the
    /// finale, the death spectacle, a debug lock, or the interlude itself.
    /// </summary>
    protected virtual bool PhaseClockParked =>
        DebugPhaseLocked || Dying || FinaleActive
        || EntranceRemaining > 0 || PhaseInterlude.Active
        || EncounterSurvivalActive;

    /// <summary>
    /// Debug and test hook: fast-forwards the current phase's clock so the
    /// next Update rotates to a new movement.
    /// </summary>
    public void DebugCompletePhaseClock() =>
        PhaseGovernor.Tick(PhaseGovernor.TimeLimit + 1.0);

    /// <summary>
    /// Debug and test hook: re-baselines the phase damage budget after an
    /// external write to <see cref="Enemy.Hp"/>.
    /// </summary>
    public void DebugRebasePhaseHealth() => RebasePhaseHealth();

    public bool PhaseInterludeActive => PhaseInterlude.Active;
    public float PhaseInterludeProgress => PhaseInterlude.Progress;
    public double PhaseClockElapsed => PhaseGovernor.Elapsed;
    public double PhaseClockLimit => PhaseGovernor.TimeLimit;
    public bool PhaseDamageThresholdReached => PhaseGovernor.ThresholdReached;

    /// <summary>
    /// Firing stops for the whole interlude -- the arena is being cleared,
    /// not contested.
    /// </summary>
    protected bool FiringSuppressed => PhaseInterlude.Active;

    /// <summary>
    /// Past the halfway mark the boss commits to a second form: more
    /// saturated colour and denser, layered geometry. Reported as a 0-1 ramp
    /// so bodies can ease into it rather than popping.
    /// </summary>
    public virtual bool SecondFormActive => !Dying && Hp <= MaxHp * .5;
    public float SecondFormBlend => SecondFormActive
        ? (float)Math.Clamp((MaxHp * .5 - Hp) / Math.Max(1.0, MaxHp * .28), 0.0, 1.0)
        : 0f;

    /// <summary>
    /// Advances the phase clock and the interlude. Must run before any
    /// early return in a subclass Update override -- several bosses skip
    /// `base.Update` entirely on survival and burrow frames, and a clock
    /// ticked only in the base would freeze exactly where it is needed.
    /// </summary>
    protected void TickEncounterClock(double dt)
    {
        PhaseInterlude.Style = InterludeStyle;
        PhaseInterlude.Tick(dt);
        // The transition beat clears the arena and walks the boss back to
        // centre; the endurance test only starts once it lands.
        if (!PhaseInterlude.Active)
            TickSurvivalPhase(dt);
        PhaseGovernor.HoldStyle = PhaseHoldStyle;
        PhaseGovernor.Suspended = PhaseClockParked;
        if (!PhaseClockParked)
            PhaseGovernor.Tick(dt);
    }

    /// <summary>
    /// Advances a boss's own survival-phase countdown. Bair and Kage used to
    /// run theirs inside <see cref="UpdatePhase"/>, which the interlude now
    /// skips -- a survival entered straight from a phase transition would
    /// have stalled forever. Hooked here so it runs ahead of every early
    /// return instead.
    /// </summary>
    protected virtual void TickSurvivalPhase(double dt)
    {
    }

    /// <summary>
    /// Eases the body back toward the arena centre for the duration of the
    /// interlude. Modelled on Aphantasia's phase handoff, the only encounter
    /// that had a real transition before this.
    /// </summary>
    protected void SettleDuringInterlude(double dt)
    {
        if (!PhaseInterlude.Active)
            return;
        Vector2 settled = BossPhaseInterlude.SettleToward(Center(), ArenaCenter, dt);
        WorldX = settled.X - Size / 2f;
        WorldY = settled.Y - Size / 2f;
    }

    /// <summary>
    /// The shared phase-entry hook. Resets the clock against the new phase's
    /// authored limit, re-baselines the damage budget, and opens the
    /// interlude -- latching the projectile sweep and the player's grace
    /// exactly once, because several bosses call their phase setter
    /// unconditionally from `UpdatePhase`.
    /// </summary>
    protected void EnterPhase(int phase, bool interlude = true)
    {
        PhaseGovernor.BeginPhase(PhaseTimeLimitFor(phase), Hp, MaxHp);
        bool firstPhase = !_hasEnteredAPhase;
        _hasEnteredAPhase = true;
        // The opening phase is set from the constructor and again while the
        // entrance plays: there is no outgoing pattern to sweep and nothing to
        // travel back from, so the beat would only stall the fight's start.
        if (!interlude || firstPhase || EntranceRemaining > 0 || Dying || DebugPhaseLocked)
            return;
        if (PhaseInterlude.Begin(InterludeDuration))
        {
            TransitionSweepRequested = true;
            PhaseInterludeInvulnerabilitySeconds = InterludeDuration;
        }
    }

    private bool _hasEnteredAPhase;

    /// <summary>
    /// Re-baselines the damage budget after a health write that is not a
    /// phase change (survival entry and exit, finale entry, New Game+
    /// rescaling). Without this the next phase reads as already over budget
    /// and blocks every hit.
    /// </summary>
    protected void RebasePhaseHealth() => PhaseGovernor.RebaseHealth(Hp, MaxHp);

    public PathChaseBoss(float worldX, float worldY, Battleground battleground, PathChaseBossConfig config, Random? rng = null)
        : base(worldX, worldY,
            (float)(config.MovementSpeed * (config.FinalBoss ? 1.16 : 1.0)),
            Simulation.TileSize * (float)(config.FinalBoss ? config.FinalBodyScale : config.BodyScale),
            config.FinalBoss ? config.FinalBodyColor : config.BodyColor,
            config.FinalBoss ? config.FinalContactDamage : config.MidContactDamage,
            config.FinalBoss ? config.FinalHealth : config.MidHealth,
            config.FinalBoss ? config.FinalRewardExperience : config.MidRewardExperience,
            config.FinalBoss ? 4.0 : 3.3,
            float.PositiveInfinity, $"{config.OwnerPrefix}_boss", "hard")
    {
        Config = config;
        PresentationProfile = BossPresentationProfile.For(config.MotionTheme,
            config.FinalBoss ? BossVisualTier.Finale : BossVisualTier.Midpoint);
        Rng = rng ?? Random.Shared;
        ArenaCenter = new Vector2(battleground.Width * Simulation.TileSize / 2f, battleground.Height * Simulation.TileSize / 2f);
        PhaseLabel = config.PhaseLabels[0];
        PhaseFlavor = ToTitleCase(config.Subtitle);
        PhaseAccent = config.FinalBoss ? config.FinalAccentColor : config.AccentColor;
        AttackCooldown = Simulation.FrameRate * 1.1f;
        AttackCooldownMax = Simulation.FrameRate * (float)(config.FinalBoss ? config.FinalCooldownSeconds : config.CooldownSeconds);
        ArenaRadius = Simulation.TileSize * (float)config.ArenaScale;
        PhaseTimeLimit = config.FinalBoss ? 28.0 : 24.0;
        DeathDuration = config.FinalBoss ? 10.0 : 2.8;
        FinaleDuration = config.FinaleDuration;
        ArenaSeed = Enumerable.Range(0, 28).Select(_ => (float)(Rng.NextDouble() * .3 - .15)).ToArray();
        _locomotion = new BossLocomotionController(config.MotionTheme, ArenaSeed);
        int arenaVertexCount = config.ArenaShape switch
        {
            "square" => 4,
            "triangle" => 3,
            "jagged" => 28,
            _ => 64,
        };
        _arenaWorldVertices = new Vector2[arenaVertexCount];
        _arenaScreenVertices = new Vector2[arenaVertexCount];
        _movementUpdateContext = new EnemyUpdateContext
        {
            PlayerWorldX = 0,
            PlayerWorldY = 0,
            Battleground = battleground,
        };
        // Seed the opening phase's clock and damage budget here rather than
        // relying on a subclass constructor to call its phase setter: several
        // do not, and an unseeded governor reports a zero health baseline,
        // which reads as "already over budget" and blocks the whole fight.
        PhaseGovernor.HoldStyle = PhaseHoldStyle;
        PhaseGovernor.BeginPhase(PhaseTimeLimitFor(Phase), Hp, MaxHp);
        // Construction *is* the opening phase entry, whether or not this
        // subclass routes it through its phase setter (Ache and Malady do
        // not). Marking it here means the first real rotation is treated as
        // a rotation and gets its interlude; the EntranceRemaining guard in
        // EnterPhase still suppresses a beat for setters called from a
        // subclass constructor.
        _hasEnteredAPhase = true;
    }

    private static string ToTitleCase(string text) => string.Join(" ", text.Split(' ').Select(
        word => word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()));

    protected static double Seconds() => Simulation.GetTimerStep() / Math.Max(1, Simulation.FrameRate);

    protected Vector2 Center() => new(WorldX + Size / 2f, WorldY + Size / 2f);

    public BossMovementPhaseProfile MovementProfile =>
        Config.MovementPhases[(Phase - 1) % Config.MovementPhases.Count];

    public override bool ReceivesKnockback => false;

    protected bool UpdateDeathSpectacle()
    {
        if (!Dying)
            return false;
        AdvanceAge();
        FinishMovementTracking();
        DeathRemaining = Math.Max(0.0, DeathRemaining - Seconds());
        if (DeathRemaining <= 0)
            Hp = 0;
        return true;
    }

    protected void BeginFinaleSequence()
    {
        if (FinaleActive || Dying)
            return;
        Hp = 1;
        FinaleActive = true;
        FinaleRemaining = FinaleDuration;
        PhaseElapsed = 0.0;
        VisualTransitionRemaining = 1.8;
        TransitionCleanupRequested = true;
    }

    protected bool UpdateFinaleSequence(double dt)
    {
        if (!FinaleActive)
            return false;
        FinaleRemaining = Math.Max(0.0, FinaleRemaining - dt);
        if (FinaleRemaining > 0)
            return false;
        FinaleActive = false;
        BeginDeathSpectacle();
        return true;
    }

    protected void BeginDeathSpectacle()
    {
        if (Dying)
            return;
        Hp = 1;
        Dying = true;
        DeathRemaining = DeathDuration;
        TransitionCleanupRequested = true;
    }

    public override HitResult TakeDamage(double amount, string partId = "body", DamageSource source = DamageSource.Direct)
    {
        if (Dying || FinaleActive)
            return new HitResult(false, false, 0, true);
        var result = base.TakeDamage(amount, partId, source);
        if (result.Killed && UsesFinaleSequence)
        {
            BeginFinaleSequence();
            return new HitResult(result.Applied, false, result.Amount, result.Blocked);
        }
        if (result.Killed && UsesSharedDeathSpectacle)
        {
            BeginDeathSpectacle();
            return new HitResult(result.Applied, false, result.Amount, result.Blocked);
        }
        return result;
    }

    public override bool IsDead() => Dying ? DeathRemaining <= 0 : Hp <= 0;

    protected Vector2[] ArenaVertices()
    {
        if (_arenaVerticesAge == Age)
            return _arenaWorldVertices;

        _arenaVerticesAge = Age;
        float radius = ArenaRadius;
        if (Config.ArenaShape == "square")
        {
            _arenaWorldVertices[0] =
                new Vector2(ArenaCenter.X - radius, ArenaCenter.Y - radius);
            _arenaWorldVertices[1] =
                new Vector2(ArenaCenter.X + radius, ArenaCenter.Y - radius);
            _arenaWorldVertices[2] =
                new Vector2(ArenaCenter.X + radius, ArenaCenter.Y + radius);
            _arenaWorldVertices[3] =
                new Vector2(ArenaCenter.X - radius, ArenaCenter.Y + radius);
            return _arenaWorldVertices;
        }
        if (Config.ArenaShape == "triangle")
        {
            for (int index = 0; index < 3; index++)
            {
                float angle = -MathF.PI / 2f + index * 2f * MathF.PI / 3f;
                _arenaWorldVertices[index] = new Vector2(
                    ArenaCenter.X + MathF.Cos(angle) * radius,
                    ArenaCenter.Y + MathF.Sin(angle) * radius);
            }
            return _arenaWorldVertices;
        }
        int count = _arenaWorldVertices.Length;
        for (int index = 0; index < count; index++)
        {
            float angle = index * 2f * MathF.PI / count;
            float localRadius;
            if (Config.ArenaShape == "jagged")
                localRadius = radius * (1 + ArenaSeed[index] + MathF.Sin(Age * .013f + index * 1.71f) * .13f);
            else if (Config.ArenaShape == "atomic")
                localRadius = radius * (.88f + .1f * MathF.Sin(angle * 3 + Age * .008f) + .045f * MathF.Sin(angle * 7 - Age * .011f));
            else
                localRadius = radius;
            _arenaWorldVertices[index] = new Vector2(
                ArenaCenter.X + MathF.Cos(angle) * localRadius,
                ArenaCenter.Y + MathF.Sin(angle) * localRadius);
        }
        return _arenaWorldVertices;
    }

    /// <summary>
    /// Projectile lifetime bounds for the authored arena shape. The former
    /// radial-only cleanup clipped every square arena to its inscribed circle,
    /// leaving Touch's four corners as permanent projectile-free shelters.
    /// Other arena families retain their deliberately soft radial envelope.
    /// </summary>
    public bool ProjectileWithinArenaBounds(Vector2 point, float allowance = 1.04f)
    {
        var offset = point - ArenaCenter;
        if (Config.ArenaShape == "square")
            return MathF.Abs(offset.X) <= ArenaRadius * allowance &&
                   MathF.Abs(offset.Y) <= ArenaRadius * allowance;
        return offset.LengthSquared() <= ArenaRadius * ArenaRadius * allowance * allowance;
    }

    protected static bool PointInPolygon(Vector2 point, IReadOnlyList<Vector2> vertices)
    {
        bool inside = false;
        var previous = vertices[^1];
        foreach (var current in vertices)
        {
            if ((current.Y > point.Y) != (previous.Y > point.Y))
            {
                // The crossing test must preserve the edge's sign. Replacing a
                // negative denominator with epsilon classifies half of a clockwise
                // polygon as exterior and repeatedly drags the player to its center.
                float crossingX = (previous.X - current.X) * (point.Y - current.Y) / (previous.Y - current.Y) + current.X;
                if (point.X < crossingX)
                    inside = !inside;
            }
            previous = current;
        }
        return inside;
    }

    /// <summary>Return the nearest point, segment, and squared distance on a polygon.</summary>
    protected static (Vector2 Point, int Segment, float DistanceSq) ClosestBoundaryPoint(Vector2 point, IReadOnlyList<Vector2> vertices)
    {
        Vector2 bestPoint = vertices[0];
        int bestSegment = 0;
        float bestDistance = float.PositiveInfinity;
        for (int index = 0; index < vertices.Count; index++)
        {
            var start = vertices[index];
            var end = vertices[(index + 1) % vertices.Count];
            float dx = end.X - start.X, dy = end.Y - start.Y;
            float lengthSq = dx * dx + dy * dy;
            float amount = lengthSq <= 1e-9f ? 0f : Math.Clamp(((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSq, 0f, 1f);
            var candidate = new Vector2(start.X + dx * amount, start.Y + dy * amount);
            float distance = (point.X - candidate.X) * (point.X - candidate.X) + (point.Y - candidate.Y) * (point.Y - candidate.Y);
            if (distance < bestDistance)
            {
                bestPoint = candidate;
                bestSegment = index;
                bestDistance = distance;
            }
        }
        return (bestPoint, bestSegment, bestDistance);
    }

    /// <summary>Ported from constrain_player_position(). Called by GameSession's movement-constraint branch for any boss exposing this arena shape.</summary>
    public (float X, float Y) ConstrainPlayerPosition(float playerX, float playerY, float playerSize)
    {
        var playerCenter = new Vector2(playerX + playerSize / 2f, playerY + playerSize / 2f);
        var vertices = ArenaVertices();
        var (nearest, segmentIndex, distanceSq) = ClosestBoundaryPoint(playerCenter, vertices);
        // A center-only test permits half the player body to leak through diagonal
        // edges. Keep a circular body margin inside every segment instead.
        float margin = playerSize * .72f;
        bool inside = PointInPolygon(playerCenter, vertices);
        if (inside && distanceSq >= margin * margin)
            return (playerX, playerY);

        var start = vertices[segmentIndex];
        var end = vertices[(segmentIndex + 1) % vertices.Length];
        float dx = end.X - start.X, dy = end.Y - start.Y;
        float length = Math.Max(1e-9f, MathF.Sqrt(dx * dx + dy * dy));
        float signedArea = 0f;
        for (int index = 0; index < vertices.Length; index++)
        {
            var a = vertices[index];
            var b = vertices[(index + 1) % vertices.Length];
            signedArea += a.X * b.Y - b.X * a.Y;
        }
        // These world polygons currently wind with positive signed area. The left
        // segment normal is therefore inward; retain support for reversed winding.
        var normal = signedArea >= 0 ? new Vector2(-dy / length, dx / length) : new Vector2(dy / length, -dx / length);
        var corrected = nearest + normal * margin;

        // Mildly concave animated boundaries can place a local normal outside an
        // adjacent spike. Fall back to a short centerward inset only in that case.
        if (!PointInPolygon(corrected, vertices))
        {
            float towardX = ArenaCenter.X - nearest.X, towardY = ArenaCenter.Y - nearest.Y;
            float towardLength = Math.Max(1e-9f, MathF.Sqrt(towardX * towardX + towardY * towardY));
            corrected = new Vector2(nearest.X + towardX / towardLength * margin, nearest.Y + towardY / towardLength * margin);
        }
        return (corrected.X - playerSize / 2f, corrected.Y - playerSize / 2f);
    }

    protected virtual void UpdatePhase()
    {
        if (DebugPhaseLocked || FinaleActive)
            return;
        double ratio = Math.Max(0.0, (double)Hp / MaxHp);
        int newPhase = ratio <= .34 ? 3 : ratio <= .67 ? 2 : 1;
        if (newPhase != Phase)
        {
            Phase = newPhase;
            PhaseLabel = Config.PhaseLabels[newPhase - 1];
            AttackCooldown = Math.Min(AttackCooldown!.Value, Simulation.FrameRate * .7f);
            VisualTransitionRemaining = 1.2;
        }
    }

    /// <summary>Dev/testing hotkey support. Ported from debug_set_phase().</summary>
    public virtual void DebugSetPhase(int phase)
    {
        Phase = Math.Clamp(phase, 1, 3);
        PhaseLabel = Config.PhaseLabels[Phase - 1];
        DebugPhaseLocked = true;
        AttackCooldown = 0f;
    }

    protected virtual void FirePattern(float playerX, float playerY, List<EnemyProjectile> sink)
    {
        var center = Center();
        float direction = MathF.Atan2(playerY - center.Y, playerX - center.X);
        int count = Config.Pattern switch
        {
            "minefield" => Config.FinalBoss ? new[] { 2, 3, 5 }[Phase - 1] : new[] { 1, 2, 3 }[Phase - 1],
            "mirage" => Config.FinalBoss ? new[] { 3, 5, 7 }[Phase - 1] : new[] { 2, 3, 5 }[Phase - 1],
            _ => Config.FinalBoss ? new[] { 1, 2, 3 }[Phase - 1] : new[] { 1, 1, 2 }[Phase - 1],
        };
        float spread = Config.Pattern switch { "rush" => .22f, "minefield" => 2.5f, "mirage" => 1.15f, _ => .34f };
        for (int index = 0; index < count; index++)
        {
            float offset = count == 1 ? 0f : -spread / 2f + spread * index / (count - 1);
            float shotSize = Size * (float)(Config.FinalBoss ? Config.FinalShotScale : Config.ShotScale);
            string shape = Config.Pattern == "minefield" ? "mine" : Config.Pattern is "rush" or "mirage" ? "diamond" : "square";
            sink.Add(new EnemyProjectile(
                center.X - shotSize / 2f, center.Y - shotSize / 2f, direction + offset,
                (float)(Config.FinalBoss ? Config.FinalShotSpeed : Config.ShotSpeed),
                (float)(Config.FinalBoss ? Config.FinalShotDamage : Config.ShotDamage),
                shotSize, travelRange: Simulation.TileSize * (float)Config.ShotRangeTiles, color: PhaseAccent,
                shape: shape, path: Config.Pattern == "mirage" ? "sine" : "linear",
                amplitude: Config.Pattern == "mirage" ? Simulation.TileSize * .65f : 0f,
                lifetime: Config.Pattern == "minefield" ? 20.0f : null,
                speedDecay: Config.Pattern == "minefield" ? .08f : 0f,
                owner: $"{Config.OwnerPrefix}_{(Config.FinalBoss ? "final" : "mid")}", ignoreWalls: Config.Pattern == "minefield"));
        }
        // Touch's final boss retains the initial slow radial cage placeholder.
        if (Config.Pattern == "boulder" && Config.FinalBoss && Phase == 3)
        {
            for (int index = 0; index < 8; index++)
            {
                sink.Add(new EnemyProjectile(center.X, center.Y, index * MathF.PI / 4f, .48f, 300f, Size * .23f,
                    travelRange: Simulation.TileSize * 11f, color: PhaseAccent, shape: "diamond", owner: $"{Config.OwnerPrefix}_ring"));
            }
        }
        MarkAttack(.42f);
    }

    public Vector2 ConstrainPlayer(Vector2 playerTopLeft, float playerSize)
    {
        var constrained = ConstrainPlayerPosition(
            playerTopLeft.X, playerTopLeft.Y, playerSize);
        return new Vector2(constrained.X, constrained.Y);
    }

    /// <summary>
    /// Invokes <see cref="Enemy.Update"/> directly, bypassing this class's own
    /// movement-mode dispatch. Ported from SinChemesthesisBoss.updateEnemy's call to
    /// `Enemy.updateEnemy(self, ...)` -- Python calls its grandparent method directly,
    /// skipping PathChaseBoss's override entirely, which C# has no direct syntax for.
    /// </summary>
    protected void ChaseUpdate(EnemyUpdateContext context) => base.Update(context);

    /// <summary>
    /// Reuses one context while scripted locomotion substitutes a waypoint for
    /// the real player. Boss updates are sequential, so retaining this mutable
    /// carrier avoids a small managed allocation on every non-chase frame.
    /// </summary>
    protected EnemyUpdateContext MovementContext(
        EnemyUpdateContext source,
        float targetX,
        float targetY)
    {
        _movementUpdateContext.PlayerWorldX = targetX;
        _movementUpdateContext.PlayerWorldY = targetY;
        _movementUpdateContext.Battleground = source.Battleground;
        _movementUpdateContext.ProjectileSink = source.ProjectileSink;
        _movementUpdateContext.AllEnemies = source.AllEnemies;
        _movementUpdateContext.ExperienceBubbles = source.ExperienceBubbles;
        _movementUpdateContext.Camera = source.Camera;
        _movementUpdateContext.BossAfflictions = source.BossAfflictions;
        _movementUpdateContext.PlayerBuildSnapshot = source.PlayerBuildSnapshot;
        _movementUpdateContext.PlayerBullets = source.PlayerBullets;
        _movementUpdateContext.DreamState = source.DreamState;
        _movementUpdateContext.PlayerMovementSpeed = source.PlayerMovementSpeed;
        _movementUpdateContext.MovementSpeedCap = MovementProfile.Mode == BossMovementMode.Chase
            ? source.PlayerMovementSpeed
            : float.PositiveInfinity;
        return _movementUpdateContext;
    }

    /// <summary>
    /// Advances the shared locomotion controller and applies its target through
    /// Enemy's existing collision-safe axis movement. Stationary and burrow
    /// profiles advance visual time only, so their world position is exact.
    /// </summary>
    protected void UpdateLocomotion(EnemyUpdateContext context)
    {
        BossLocomotionFrame frame = _locomotion.Update(
            Phase,
            MovementProfile,
            Center(),
            new Vector2(context.PlayerWorldX, context.PlayerWorldY),
            ArenaCenter,
            ArenaRadius,
            Speed,
            Seconds());
        if (frame.Stationary)
        {
            AdvanceAge();
            FinishMovementTracking();
            return;
        }

        float originalSpeed = Speed;
        Speed = frame.SpeedPerReferenceTick;
        ChaseUpdate(MovementContext(context, frame.Target.X, frame.Target.Y));
        Speed = originalSpeed;
    }

    public override void Update(EnemyUpdateContext context)
    {
        TickEncounterClock(Seconds());
        if (UpdateDeathSpectacle())
            return;
        double dt = Seconds();
        if (UpdateFinaleSequence(dt))
            return;
        EntranceRemaining = Math.Max(0.0, EntranceRemaining - dt);
        VisualTransitionRemaining = Math.Max(0.0, VisualTransitionRemaining - dt);
        PhaseElapsed += dt;
        ArenaRingSeconds += dt;
        if (PhaseInterlude.Active)
        {
            SettleDuringInterlude(dt);
            AdvanceAge();
            FinishMovementTracking();
            return;
        }
        UpdatePhase();
        UpdateLocomotion(context);
        AttackCooldown -= (float)Simulation.GetTimerStep();
        if (EntranceRemaining <= 0 && AttackCooldown <= 0)
        {
            FirePattern(context.PlayerWorldX, context.PlayerWorldY, context.ProjectileSink);
            double rate = 1.0 - .11 * (Phase - 1);
            AttackCooldownMax ??= Simulation.FrameRate * (float)(Config.FinalBoss ? Config.FinalCooldownSeconds : Config.CooldownSeconds);
            AttackCooldown = AttackCooldownMax.Value * (float)(rate * (.9 + Rng.NextDouble() * .22));
        }
    }

    /// <summary>
    /// Draw the shaped arena after every other world layer. This remains
    /// independent of boss-body painter order and culling so the exterior
    /// cannot reveal dungeon scenery during any encounter transition.
    /// </summary>
    public void DrawPersistentArena(
        SpriteBatch spriteBatch,
        Camera camera,
        Vector2 playerWorldPosition,
        Vector2 screenShake,
        Rectangle logicalViewport)
    {
        var worldVertices = ArenaVertices();
        for (int index = 0; index < worldVertices.Length; index++)
        {
            _arenaScreenVertices[index] = camera.WorldToScreen(
                worldVertices[index],
                playerWorldPosition,
                screenShake);
        }
        Vector2[] vertices = _arenaScreenVertices;
        if (vertices.Length < 3)
            return;
        Primitives2D.DrawOutsideArena(
            spriteBatch,
            vertices,
            logicalViewport);
        Primitives2D.PolygonOutlineSpan(
            spriteBatch, vertices, UiTheme.Ink, 8);
        Primitives2D.PolygonOutlineSpan(
            spriteBatch, vertices, PhaseAccent, 3);
        // Drive the ring from the live phase clock rather than a free-running
        // counter: it now reads as "time left in this phase", which is the
        // information the timer-driven rotation made worth showing.
        double progress = PhaseGovernor.TimeLimit > 0
            ? 1 - PhaseGovernor.Progress
            : 1 - (ArenaRingSeconds % PhaseTimeLimit) / PhaseTimeLimit;
        int lit = Math.Max(2, (int)(vertices.Length * progress));
        Primitives2D.PolylineSpan(
            spriteBatch,
            vertices.AsSpan(0, lit),
            false,
            UiTheme.Cream,
            2);
        int markerIndex = Math.Min(vertices.Length - 1, (int)((1 - progress) * (vertices.Length - 1)));
        Primitives2D.FillCircle(spriteBatch, vertices[markerIndex], 5, UiTheme.Cream);
    }

    protected virtual void DrawBossBody(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        Vector2 screenPosition = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        var rect = new Rectangle((int)screenPosition.X, (int)screenPosition.Y, (int)Size, (int)Size);
        float walkBob = Moved ? MathF.Sin(Age * .24f) * Size * .035f : 0f;
        var center = rect.Center.ToVector2() + new Vector2(0, walkBob);
        if (Dying)
        {
            BossVisuals.Disassemble(spriteBatch, center, Age, DeathProgress, Size, new Color(67, 157, 211), new Color(244, 142, 50));
            return;
        }
        float attack = VisualAttackPulse;
        float detach = VisualSurvivalActive ? 1.55f : 1f;
        float coreSize = Size * (.58f + attack * .09f);
        BossVisuals.OrbitingCubes(spriteBatch, center, Age, Config.FinalBoss ? 8 : 6, Size * .58f, Size * .18f,
            new Color(67, 157, 211), new Color(244, 142, 50), detach, 1.15f + attack * 2.4f);
        BossVisuals.Cube(spriteBatch, center, coreSize, new Color(54, 139, 204), new Color(246, 151, 57), Age * .009f);
        var core = new Rectangle((int)(center.X - coreSize * .15f), (int)(center.Y - coreSize * .15f), (int)(coreSize * .3f), (int)(coreSize * .3f));
        Primitives2D.FillRect(spriteBatch, core, new Color(246, 151, 57));
        Primitives2D.RectOutline(spriteBatch, core, UiTheme.Ink, 2);
        DrawBossHealth(spriteBatch, new Rectangle((int)(center.X - Size * .46f), (int)(center.Y - Size * .65f), (int)(Size * .92f), 6));
    }

    protected void DrawBossHealth(SpriteBatch spriteBatch, Rectangle bar)
    {
        // Full bosses use GameSession's single shared HUD. This method stays
        // as a compatibility hook for the authored body renderers.
    }

    public override void Draw(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        DrawBossBody(spriteBatch, camera, playerWorldPosition, screenShake);
    }
}
