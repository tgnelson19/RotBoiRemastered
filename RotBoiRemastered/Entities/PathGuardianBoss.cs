using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.Systems;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

public sealed record PathGuardianPhaseProfile(
    string Label,
    string Flavor,
    float CadenceSeconds,
    float PreferredDistanceTiles,
    float OrbitScale,
    float MovementScale);

public sealed record PathGuardianSenseProfile(
    string BossName,
    string Subtitle,
    string TrialLabel,
    string TrialFlavor,
    double TrialDuration,
    Color SecondaryAccent,
    IReadOnlyList<PathGuardianPhaseProfile> Phases);

/// <summary>
/// The reusable high-health boss for ordinary composite Path floors. Its
/// three damage phases surround a short, sense-specific survival trial. The
/// encounter intentionally borrows Malady/Dissonance's boss grammar in a
/// compact floor-guardian form: protected declarations, committed attacks,
/// transition cleanup, an invulnerable intermission, a threat budget, and a
/// readable death beat.
/// </summary>
public sealed class PathGuardianBoss : Enemy
{
    public const int ActiveThreatSoftCap = 62;
    public const int MinimumAttacksPerPhase = 2;
    public const double DeathDuration = 1.8;
    public const double RarePatternChance = .20;

    public static readonly IReadOnlyDictionary<string, PathGuardianSenseProfile> SenseProfiles =
        new Dictionary<string, PathGuardianSenseProfile>
        {
            ["sound"] = new(
                "RESONANT WARDEN", "KEEPER OF THE UNFINISHED CHORD",
                "THE HELD NOTE", "The chord sustains. Find the silence inside it.", 6.4,
                new Color(110, 167, 207),
                new[]
                {
                    new PathGuardianPhaseProfile("MURMUR", "A pulse tests the open ground.", 2.08f, 4.8f, .52f, 1f),
                    new PathGuardianPhaseProfile("RESONANCE", "The answer returns from both sides.", 1.78f, 4.5f, .64f, 1.05f),
                    new PathGuardianPhaseProfile("CRESCENDO", "Every surviving note arrives together.", 1.48f, 4.15f, .78f, 1.12f),
                }),
            ["touch"] = new(
                "PRESSURE ENGINE", "WARDEN OF THE WEIGHT BELOW",
                "LOCKED VALVE", "The chamber seals while pressure seeks release.", 7.1,
                new Color(166, 146, 76),
                new[]
                {
                    new PathGuardianPhaseProfile("DRIP", "Slow weight announces every impact.", 2.62f, 3.7f, .13f, .78f),
                    new PathGuardianPhaseProfile("PRESSURE", "Banks close around the nearest refuge.", 2.25f, 3.25f, .18f, .84f),
                    new PathGuardianPhaseProfile("DELUGE", "The whole sewer becomes a moving wall.", 1.92f, 2.9f, .23f, .9f),
                }),
            ["sight"] = new(
                "DROWNED OCULUS", "LENS AT THE QUICKENED HORIZON",
                "BLIND ANGLE", "The lens turns away; move before it focuses.", 5.8,
                new Color(228, 142, 63),
                new[]
                {
                    new PathGuardianPhaseProfile("REFRACTION", "A narrow fan divides the horizon.", 1.92f, 6.4f, .7f, 1.16f),
                    new PathGuardianPhaseProfile("FOCUS", "The lens fixes a line through motion.", 1.62f, 6.0f, .82f, 1.22f),
                    new PathGuardianPhaseProfile("WHITEOUT", "Speed erases every false opening.", 1.34f, 5.5f, .94f, 1.3f),
                }),
            ["chemesthesis"] = new(
                "CINDER PLAGUE", "CARRIER OF THE BURNING FIELD",
                "INCUBATION", "The field ripens. Empty ground is the only cure.", 7.0,
                new Color(116, 132, 50),
                new[]
                {
                    new PathGuardianPhaseProfile("CONTAGION", "Carriers mark the ground before they spread.", 2.46f, 5.6f, .22f, .92f),
                    new PathGuardianPhaseProfile("DECAY", "Old hazards make room for new infection.", 2.12f, 5.35f, .28f, .96f),
                    new PathGuardianPhaseProfile("EXTINCTION", "The last clean routes begin to close.", 1.78f, 5.0f, .34f, 1.02f),
                }),
            ["phantasia"] = new(
                "DREAMING PRISM", "WARDEN OF THE ORNATE DREAM",
                "FALSE AWAKENING", "Only the marked light belongs to waking.", 6.6,
                new Color(225, 128, 190),
                new[]
                {
                    new PathGuardianPhaseProfile("GLIMMER", "One true petal travels among reflections.", 2.28f, 4.8f, .44f, 1f),
                    new PathGuardianPhaseProfile("DELUSION", "The dream surrounds its moving witness.", 1.96f, 4.55f, .55f, 1.05f),
                    new PathGuardianPhaseProfile("REVELATION", "Truth doubles, but certainty does not.", 1.64f, 4.25f, .68f, 1.12f),
                }),
        };

    private static int _nextId = 1;
    private readonly string _owner;
    private readonly Random _rng;
    private readonly PathGuardianSenseProfile _profile;
    private double _transitionRemaining;
    private int _attacksCompletedInPhase;
    private int _patternRotation;
    private bool _trialStarted;
    private double _gateTransitionDelay;
    private bool _forceRarePattern;

    public string SenseKey { get; }
    public int FloorNumber { get; }
    public int Phase { get; private set; } = 1;
    public int AttacksCompletedInPhase => _attacksCompletedInPhase;
    public int PhaseDeclarations => _attacksCompletedInPhase;
    public string BossDisplayName => _profile.BossName;
    public string BossSubtitle => _profile.Subtitle;
    public string PhaseLabel => TrialActive
        ? _profile.TrialLabel
        : _profile.Phases[Math.Clamp(Phase - 1, 0, _profile.Phases.Count - 1)].Label;
    public string PhaseFlavor => TrialActive
        ? _profile.TrialFlavor
        : _profile.Phases[Math.Clamp(Phase - 1, 0, _profile.Phases.Count - 1)].Flavor;
    public Color PhaseAccent => GamePaths.PathsByKey[SenseKey].Accent;
    public Color SecondaryAccent => _profile.SecondaryAccent;
    public double EntranceRemaining { get; private set; } = 1.15;
    public double TransitionRemaining => _transitionRemaining;
    public double PhaseAnnouncementRemaining { get; private set; } = 2.4;
    public bool TrialActive { get; private set; }
    public double TrialRemaining { get; private set; }
    public double TrialDuration => _profile.TrialDuration + (FloorNumber > 5 ? 1.2 : 0);
    public bool Dying { get; private set; }
    public double DeathRemaining { get; private set; }
    public float AttackAnticipation { get; private set; }
    public Vector2 ArenaCenter { get; }
    public float ArenaRadius { get; }
    public bool PhaseGatePending => _gateTransitionDelay > 0;
    public bool LastPatternWasRare { get; private set; }
    public int RarePatternsCommitted { get; private set; }
    public bool Invulnerable =>
        EntranceRemaining > 0 || _transitionRemaining > 0 || TrialActive || Dying;

    public PathGuardianBoss(float worldX, float worldY, string senseKey, int floorNumber,
        float awarenessRange, Random? rng = null, float? arenaRadius = null)
        : base(worldX, worldY,
            speed: 1.45f + floorNumber * .025f,
            size: Simulation.TileSize * 1.7f,
            color: GamePaths.PathsByKey[senseKey].Accent,
            damage: 135 + floorNumber * 13,
            hp: 5_800 + floorNumber * 1_550,
            expValue: 75 + floorNumber * 16,
            difficulty: 4.0,
            awarenessRange: awarenessRange,
            archetype: "path_guardian",
            difficultyTier: floorNumber > 5 ? "hard" : "medium",
            rng: rng)
    {
        if (!GamePaths.PathsByKey.ContainsKey(senseKey)
            || !SenseProfiles.TryGetValue(senseKey, out var profile))
            throw new KeyNotFoundException($"Unknown guardian sense: {senseKey}");
        SenseKey = senseKey;
        FloorNumber = floorNumber;
        _profile = profile;
        _rng = rng ?? Random.Shared;
        _owner = $"path_guardian_{_nextId++}";
        Family = profile.BossName.ToLowerInvariant();
        ThreatCost = 30;
        AttackCooldown = Simulation.FrameRate * 1.45f;
        AttackCooldownMax = AttackCooldown;
        TransitionCleanupOwner = _owner;
        ArenaCenter = new Vector2(worldX + Size / 2f, worldY + Size / 2f);
        ArenaRadius = arenaRadius ?? Simulation.TileSize * 5.2f;
    }

    private double Seconds() => Simulation.GetTimerStep() / Math.Max(1, Simulation.FrameRate);

    /// <summary>
    /// Deterministic phase control for boss pressure tests and the existing
    /// debug workflow. Natural progression still requires declarations and
    /// the survival trial.
    /// </summary>
    public void DebugSetPhase(int phase)
    {
        Phase = Math.Clamp(phase, 1, 3);
        EntranceRemaining = 0;
        _transitionRemaining = 0;
        TrialActive = false;
        _trialStarted = Phase >= 3;
        TrialRemaining = 0;
        Dying = false;
        DeathRemaining = 0;
        _attacksCompletedInPhase = 0;
        _gateTransitionDelay = 0;
        AttackCooldown = 0;
        PhaseAnnouncementRemaining = 2.4;
        Hp = Phase switch
        {
            1 => MaxHp,
            2 => (int)Math.Ceiling(MaxHp * .67),
            _ => (int)Math.Ceiling(MaxHp * .34),
        };
        TransitionCleanupRequested = true;
    }

    public void DebugStartTrial()
    {
        DebugSetPhase(2);
        _trialStarted = false;
        BeginTrial();
        _transitionRemaining = 0;
    }

    public void DebugQueueRarePattern() => _forceRarePattern = true;

    public override HitResult TakeDamage(double amount, string partId = "body", DamageSource source = DamageSource.Direct)
    {
        if (Invulnerable)
            return new HitResult(false, false, 0, true);

        double floorRatio = Phase switch
        {
            1 => .67,
            2 => .34,
            _ => 0,
        };
        if (floorRatio > 0)
        {
            int healthFloor = (int)Math.Ceiling(MaxHp * floorRatio);
            amount = Math.Min(amount, Math.Max(0, Hp - healthFloor));
            if (amount <= 0)
            {
                if (_attacksCompletedInPhase >= MinimumAttacksPerPhase
                    && _gateTransitionDelay <= 0)
                    BeginNextPhase();
                return new HitResult(false, false, 0, true);
            }
        }

        var result = base.TakeDamage(amount, partId, source);
        if (result.Killed)
        {
            BeginDeath();
            return new HitResult(result.Applied, false, result.Amount, result.Blocked);
        }

        int desiredPhase = Phase;
        if (Phase == 1 && Hp <= (int)Math.Ceiling(MaxHp * .67))
            desiredPhase = 2;
        else if (Phase == 2 && Hp <= (int)Math.Ceiling(MaxHp * .34))
            desiredPhase = 3;
        if (desiredPhase > Phase && _attacksCompletedInPhase >= MinimumAttacksPerPhase)
            BeginNextPhase();
        return result;
    }

    private void BeginNextPhase()
    {
        if (Phase >= 3)
            return;
        if (Phase == 2)
        {
            BeginTrial();
            return;
        }
        Phase += 1;
        _attacksCompletedInPhase = 0;
        _transitionRemaining = SenseKey switch
        {
            "touch" => 1.05,
            "sight" => .62,
            "phantasia" => .95,
            _ => .8,
        };
        TransitionCleanupRequested = true;
        AttackCooldown = Simulation.FrameRate * .55f;
        PhaseAnnouncementRemaining = 2.4;
        BossAudio.Emit(BossAudioCueKind.Stagger, SenseKey);
    }

    private void BeginTrial()
    {
        if (_trialStarted)
            return;
        _trialStarted = true;
        TrialActive = true;
        TrialRemaining = TrialDuration;
        Phase = 3;
        _attacksCompletedInPhase = 0;
        _transitionRemaining = 1.15;
        TransitionCleanupRequested = true;
        AttackCooldown = Simulation.FrameRate * .45f;
        PhaseAnnouncementRemaining = 3.0;
        BossAudio.Emit(BossAudioCueKind.Trial, SenseKey);
    }

    private void CompleteTrial()
    {
        TrialActive = false;
        TrialRemaining = 0;
        _transitionRemaining = .72;
        TransitionCleanupRequested = true;
        AttackCooldown = Simulation.FrameRate * .62f;
        PhaseAnnouncementRemaining = 2.4;
        BossAudio.Emit(BossAudioCueKind.Stagger, SenseKey);
    }

    private void BeginDeath()
    {
        if (Dying)
            return;
        Hp = 1;
        Dying = true;
        DeathRemaining = DeathDuration;
        TransitionCleanupRequested = true;
        PhaseAnnouncementRemaining = DeathDuration;
        BossAudio.Emit(BossAudioCueKind.Death, SenseKey);
    }

    public override bool IsDead() => Dying ? DeathRemaining <= 0 : Hp <= 0;

    public override void Update(EnemyUpdateContext context)
    {
        AdvanceAge();
        double seconds = Seconds();
        PhaseAnnouncementRemaining = Math.Max(0, PhaseAnnouncementRemaining - seconds);
        if (Dying)
        {
            AttackAnticipation = 0;
            DeathRemaining = Math.Max(0, DeathRemaining - seconds);
            if (DeathRemaining <= 0)
                Hp = 0;
            FinishMovementTracking();
            return;
        }
        if (EntranceRemaining > 0)
        {
            AttackAnticipation = 0;
            EntranceRemaining = Math.Max(0, EntranceRemaining - seconds);
            FinishMovementTracking();
            return;
        }
        if (_transitionRemaining > 0)
        {
            AttackAnticipation = 0;
            _transitionRemaining = Math.Max(0, _transitionRemaining - seconds);
            FinishMovementTracking();
            return;
        }
        if (_gateTransitionDelay > 0)
        {
            AttackAnticipation = 0;
            _gateTransitionDelay = Math.Max(0,
                _gateTransitionDelay - seconds);
            if (_gateTransitionDelay <= 0)
                BeginNextPhase();
            FinishMovementTracking();
            return;
        }
        if (TrialActive)
        {
            TrialRemaining = Math.Max(0, TrialRemaining - seconds);
            if (TrialRemaining <= 0)
            {
                CompleteTrial();
                FinishMovementTracking();
                return;
            }

            AttackCooldown = Math.Max(0,
                (AttackCooldown ?? 0) - (float)Simulation.GetTimerStep());
            UpdateAttackAnticipation(.48f);
            if (AttackCooldown <= 0)
            {
                bool committed = TryCommitPattern(context, trial: true);
                float trialCadence = Math.Max(.72f,
                    1.08f - (FloorNumber > 5 ? .14f : 0));
                AttackCooldown = Simulation.FrameRate *
                    (committed ? trialCadence : .22f);
                AttackCooldownMax = AttackCooldown;
            }
            FinishMovementTracking();
            return;
        }

        float centerX = WorldX + Size / 2f, centerY = WorldY + Size / 2f;
        float dx = context.PlayerWorldX - centerX, dy = context.PlayerWorldY - centerY;
        float distance = Math.Max(1f, MathF.Sqrt(dx * dx + dy * dy));
        float directionX = dx / distance, directionY = dy / distance;

        // A slow sideways orbit keeps the body active without obscuring the
        // sense-specific projectile lesson.
        var phaseProfile = _profile.Phases[Phase - 1];
        float orbitScale = phaseProfile.OrbitScale;
        if (SenseKey == "phantasia")
            orbitScale += .1f * MathF.Sin(Age * .011f);
        float preferredDistance = phaseProfile.PreferredDistanceTiles;
        float orbit = MathF.Sin(Age * .025f + Phase) * (orbitScale + Phase * .035f);
        float approach = distance > Simulation.TileSize * (preferredDistance + 1f)
            ? 1f
            : distance < Simulation.TileSize * (preferredDistance - 1f)
                ? -.45f
                : .1f;
        float step = Speed * phaseProfile.MovementScale *
            (.22f + Phase * .025f) * (float)Simulation.GetFrameScale();
        TryAxisMove((directionX * approach - directionY * orbit) * step, "x", context.Battleground);
        TryAxisMove((directionY * approach + directionX * orbit) * step, "y", context.Battleground);
        EnsureCollisionSafePosition(context.Battleground);

        AttackCooldown = Math.Max(0, (AttackCooldown ?? 0) - (float)Simulation.GetTimerStep());
        UpdateAttackAnticipation(.42f);
        if (AttackCooldown <= 0)
        {
            bool committed = TryCommitPattern(context, trial: false);
            if (committed)
            {
                _attacksCompletedInPhase += 1;
                _patternRotation += 1;
                if (Phase < 3
                    && _attacksCompletedInPhase >= MinimumAttacksPerPhase
                    && AtCurrentHealthFloor())
                {
                    // Let the declaration exist long enough to read before
                    // transition cleanup removes its projectile field.
                    _gateTransitionDelay = .68;
                }
            }
            float cooldown = Math.Max(1.02f,
                phaseProfile.CadenceSeconds - (FloorNumber > 5 ? .16f : 0));
            AttackCooldown = Simulation.FrameRate *
                (committed ? cooldown : .24f);
            AttackCooldownMax = AttackCooldown;
        }
        FinishMovementTracking();
    }

    private bool AtCurrentHealthFloor() => Phase switch
    {
        1 => Hp <= (int)Math.Ceiling(MaxHp * .67),
        2 => Hp <= (int)Math.Ceiling(MaxHp * .34),
        _ => false,
    };

    private void UpdateAttackAnticipation(float windowSeconds)
    {
        float window = Simulation.FrameRate * windowSeconds;
        if (AttackCooldown is > 0 && AttackCooldown <= window)
        {
            AttackAnticipation = Math.Clamp(
                1f - AttackCooldown.Value / Math.Max(1f, window), 0f, 1f);
        }
        else
        {
            AttackAnticipation = 0;
        }
    }

    private int ActiveThreatCount(IReadOnlyList<EnemyProjectile> sink) =>
        sink.Count(projectile => !projectile.RemFlag && projectile.Owner == _owner);

    private static EnemyUpdateContext WithProjectileSink(
        EnemyUpdateContext source,
        List<EnemyProjectile> sink) => new()
    {
        PlayerWorldX = source.PlayerWorldX,
        PlayerWorldY = source.PlayerWorldY,
        Battleground = source.Battleground,
        ProjectileSink = sink,
        AllEnemies = source.AllEnemies,
        ExperienceBubbles = source.ExperienceBubbles,
        Camera = source.Camera,
        BossAfflictions = source.BossAfflictions,
        PlayerBuildSnapshot = source.PlayerBuildSnapshot,
        PlayerBullets = source.PlayerBullets,
        DreamState = source.DreamState,
    };

    private bool TryCommitPattern(EnemyUpdateContext context, bool trial)
    {
        var staged = new List<EnemyProjectile>();
        var stagedContext = WithProjectileSink(context, staged);
        bool rare = !trial
            && (_forceRarePattern
                || (_attacksCompletedInPhase >= MinimumAttacksPerPhase
                    && _rng.NextDouble() < RarePatternChance));
        if (trial)
            FireTrialPattern(stagedContext);
        else if (rare)
            FireRareSensePattern(stagedContext);
        else
            FireSensePattern(stagedContext);
        if (staged.Count == 0
            || ActiveThreatCount(context.ProjectileSink) + staged.Count > ActiveThreatSoftCap)
        {
            return false;
        }
        context.ProjectileSink.AddRange(staged);
        _forceRarePattern = false;
        LastPatternWasRare = rare;
        if (rare)
            RarePatternsCommitted++;
        BossAudio.Emit(
            BossAudioCueKind.Declaration,
            SenseKey,
            trial ? .68f : rare ? 1.18f : 1f);
        return true;
    }

    private void FireSensePattern(EnemyUpdateContext context)
    {
        MarkAttack(.3f);
        switch (SenseKey)
        {
            case "sound":
                FireSound(context);
                break;
            case "touch":
                FireTouch(context);
                break;
            case "sight":
                FireSight(context);
                break;
            case "chemesthesis":
                FireChemesthesis(context);
                break;
            case "phantasia":
                FirePhantasia(context);
                break;
        }
    }

    private void FireRareSensePattern(EnemyUpdateContext context)
    {
        MarkAttack(.44f);
        var center = Center();
        float aimed = Aim(context.PlayerWorldX, context.PlayerWorldY);
        switch (SenseKey)
        {
            case "sound":
            {
                int notes = 6 + Phase * 2;
                for (int index = 0; index < notes; index++)
                {
                    float side = index % 2 == 0 ? 1f : -1f;
                    var note = new EnemyProjectile(
                        center.X, center.Y,
                        aimed + (index - (notes - 1) / 2f) * .17f,
                        .82f + Phase * .07f,
                        Damage * .2f,
                        Size * .11f,
                        travelRange: ArenaRadius * 1.5f,
                        color: index % 3 == 0 ? SecondaryAccent : PhaseAccent,
                        shape: "diamond",
                        path: "sine",
                        amplitude: side * Simulation.TileSize * (.7f + Phase * .1f),
                        frequency: .019f + index * .001f,
                        owner: _owner)
                    {
                        TelegraphDuration = .44f,
                    };
                    context.ProjectileSink.Add(note);
                }
                break;
            }

            case "touch":
            {
                int valves = 3 + Phase;
                for (int index = 0; index < valves; index++)
                {
                    float offset = (index - (valves - 1) / 2f) * .31f;
                    var pressure = new EnemyProjectile(
                        center.X, center.Y,
                        aimed + offset,
                        .42f + Phase * .035f,
                        Damage * .31f,
                        Size * (.34f + Phase * .025f),
                        travelRange: ArenaRadius * 1.55f,
                        color: index % 2 == 0 ? SecondaryAccent : PhaseAccent,
                        path: "bank",
                        owner: _owner)
                    {
                        TelegraphDuration = 1.04f,
                    };
                    context.ProjectileSink.Add(pressure);
                }
                break;
            }

            case "sight":
            {
                int lenses = 2 + Phase;
                for (int index = 0; index < lenses; index++)
                {
                    float offset = (index - (lenses - 1) / 2f) * .48f;
                    var laser = new EnemyProjectile(
                        center.X, center.Y,
                        aimed + offset,
                        0,
                        Damage * .28f,
                        Size * .09f,
                        travelRange: ArenaRadius * 2.15f,
                        color: index % 2 == 0 ? PhaseAccent : SecondaryAccent,
                        path: "laser",
                        lifetime: 1.15f + Phase * .12f,
                        angularSpeed: index % 2 == 0 ? .045f : -.045f,
                        owner: _owner,
                        ignoreWalls: true)
                    {
                        TelegraphDuration = .96f,
                    };
                    context.ProjectileSink.Add(laser);
                }
                break;
            }

            case "chemesthesis":
            {
                int pods = 5 + Phase;
                int safeIndex = (int)MathF.Round(
                    ((aimed % MathF.Tau + MathF.Tau) % MathF.Tau)
                    / MathF.Tau * pods) % pods;
                for (int index = 0; index < pods; index++)
                {
                    if (index == safeIndex)
                        continue;
                    float angle = index * MathF.Tau / pods + _patternRotation * .09f;
                    float mineSize = Simulation.TileSize * (.42f + Phase * .035f);
                    Vector2 position = ArenaCenter
                        + new Vector2(MathF.Cos(angle), MathF.Sin(angle))
                        * ArenaRadius * .62f;
                    context.ProjectileSink.Add(new EnemyProjectile(
                        position.X - mineSize / 2f,
                        position.Y - mineSize / 2f,
                        0,
                        0,
                        Damage * .22f,
                        mineSize,
                        color: index % 2 == 0 ? SecondaryAccent : PhaseAccent,
                        shape: "mine",
                        path: "mine",
                        lifetime: 5.2f,
                        owner: _owner,
                        ignoreWalls: true)
                    {
                        TelegraphDuration = 1.02f + index * .04f,
                        PersistentHazard = true,
                    });
                }
                break;
            }

            case "phantasia":
            {
                int petals = 8 + Phase * 2;
                int trueStride = Math.Max(3, 6 - Phase);
                for (int index = 0; index < petals; index++)
                {
                    bool real = index % trueStride == _patternRotation % trueStride;
                    float direction = aimed
                        + (index - (petals - 1) / 2f) * .13f;
                    context.ProjectileSink.Add(new EnemyProjectile(
                        center.X,
                        center.Y,
                        direction,
                        .92f + Phase * .08f,
                        real ? Damage * .22f : 0,
                        Size * .1f,
                        travelRange: ArenaRadius * 1.5f,
                        color: real ? SecondaryAccent : PhaseAccent,
                        shape: "diamond",
                        path: "sine",
                        amplitude: (index % 2 == 0 ? 1 : -1)
                            * Simulation.TileSize * .38f,
                        frequency: .021f,
                        owner: _owner)
                    {
                        Illusory = !real,
                        TruthMarked = real,
                        TelegraphDuration = real ? .52f : .24f,
                    });
                }
                break;
            }
        }
    }

    private void FireTrialPattern(EnemyUpdateContext context)
    {
        MarkAttack(.46f);
        var target = new Vector2(context.PlayerWorldX, context.PlayerWorldY);
        float aimed = MathF.Atan2(target.Y - ArenaCenter.Y,
            target.X - ArenaCenter.X);
        switch (SenseKey)
        {
            case "sound":
            {
                int count = 18 + (FloorNumber > 5 ? 4 : 0);
                float rotation = _patternRotation * .08f;
                int safeIndex = (int)MathF.Round(
                    (((aimed - rotation) % MathF.Tau + MathF.Tau) % MathF.Tau)
                    / MathF.Tau * count) % count;
                for (int index = 0; index < count; index++)
                {
                    int gap = Math.Min((index - safeIndex + count) % count,
                        (safeIndex - index + count) % count);
                    if (gap <= 1)
                        continue;
                    context.ProjectileSink.Add(new EnemyProjectile(
                        ArenaCenter.X, ArenaCenter.Y,
                        index * MathF.Tau / count + rotation,
                        .72f, Damage * .15f, Size * .1f,
                        travelRange: ArenaRadius * 1.15f,
                        color: index % 2 == 0 ? PhaseAccent : SecondaryAccent,
                        shape: "diamond", path: "sine",
                        amplitude: (index % 2 == 0 ? 1 : -1)
                            * Simulation.TileSize * .28f,
                        frequency: .024f, owner: _owner));
                }
                break;
            }
            case "touch":
            {
                for (int bank = -1; bank <= 1; bank++)
                {
                    var projectile = new EnemyProjectile(
                        ArenaCenter.X, ArenaCenter.Y, aimed + bank * .42f,
                        .44f, Damage * .24f, Size * .34f,
                        travelRange: ArenaRadius * 1.45f,
                        color: bank == 0 ? SecondaryAccent : PhaseAccent,
                        path: "bank", owner: _owner)
                    {
                        TelegraphDuration = 1.08f,
                    };
                    context.ProjectileSink.Add(projectile);
                }
                break;
            }
            case "sight":
            {
                float rotation = _patternRotation * .19f;
                int beams = FloorNumber > 5 ? 4 : 3;
                for (int beam = 0; beam < beams; beam++)
                {
                    var laser = new EnemyProjectile(
                        ArenaCenter.X, ArenaCenter.Y,
                        rotation + beam * MathF.PI / beams,
                        0, Damage * .25f, Size * .085f,
                        travelRange: ArenaRadius * 2.2f,
                        color: beam % 2 == 0 ? PhaseAccent : SecondaryAccent,
                        path: "laser", lifetime: 1.05f,
                        angularSpeed: _patternRotation % 2 == 0 ? .045f : -.045f,
                        owner: _owner, ignoreWalls: true)
                    {
                        TelegraphDuration = .92f,
                    };
                    context.ProjectileSink.Add(laser);
                }
                break;
            }
            case "chemesthesis":
            {
                int count = 8 + (FloorNumber > 5 ? 2 : 0);
                float rotation = _patternRotation * .11f;
                int safeIndex = (int)MathF.Round(
                    (((aimed - rotation) % MathF.Tau + MathF.Tau) % MathF.Tau)
                    / MathF.Tau * count) % count;
                for (int index = 0; index < count; index++)
                {
                    int gap = Math.Min((index - safeIndex + count) % count,
                        (safeIndex - index + count) % count);
                    if (gap <= 1)
                        continue;
                    float angle = index * MathF.Tau / count + rotation;
                    float size = Simulation.TileSize * .5f;
                    Vector2 position = ArenaCenter
                        + new Vector2(MathF.Cos(angle), MathF.Sin(angle))
                        * ArenaRadius * .48f;
                    var mine = new EnemyProjectile(
                        position.X - size / 2f, position.Y - size / 2f,
                        0, 0, Damage * .2f, size,
                        color: index % 2 == 0 ? PhaseAccent : SecondaryAccent,
                        shape: "mine", path: "mine", lifetime: 4.2f,
                        owner: _owner, ignoreWalls: true)
                    {
                        TelegraphDuration = .94f + index * .035f,
                        PersistentHazard = true,
                    };
                    context.ProjectileSink.Add(mine);
                }
                break;
            }
            case "phantasia":
            {
                int count = 14;
                float rotation = _patternRotation * .07f;
                int safeIndex = (int)MathF.Round(
                    (((aimed - rotation) % MathF.Tau + MathF.Tau) % MathF.Tau)
                    / MathF.Tau * count) % count;
                for (int index = 0; index < count; index++)
                {
                    int gap = Math.Min((index - safeIndex + count) % count,
                        (safeIndex - index + count) % count);
                    bool real = index % 3 == _patternRotation % 3;
                    if (gap <= 2)
                        real = false;
                    context.ProjectileSink.Add(new EnemyProjectile(
                        ArenaCenter.X, ArenaCenter.Y,
                        index * MathF.Tau / count + rotation,
                        .86f, real ? Damage * .2f : 0, Size * .1f,
                        travelRange: ArenaRadius * 1.3f,
                        color: real ? SecondaryAccent : PhaseAccent,
                        shape: "diamond", path: "sine",
                        amplitude: Simulation.TileSize * .22f,
                        frequency: .02f, owner: _owner)
                    {
                        Illusory = !real,
                        TruthMarked = real,
                        TelegraphDuration = real ? .48f : .22f,
                    });
                }
                break;
            }
        }
        _patternRotation += 1;
    }

    private Vector2 Center() => new(WorldX + Size / 2f, WorldY + Size / 2f);

    private float Aim(float playerX, float playerY)
    {
        var center = Center();
        return MathF.Atan2(playerY - center.Y, playerX - center.X);
    }

    private void FireSound(EnemyUpdateContext context)
    {
        var center = Center();
        int spokes = 5 + Phase * 2 + (FloorNumber > 5 ? 2 : 0);
        float phaseOffset = Age * .012f;
        for (int index = 0; index < spokes; index++)
        {
            float direction = phaseOffset + index * MathF.Tau / spokes;
            var pulse = new EnemyProjectile(
                center.X, center.Y, direction, .82f + Phase * .08f,
                Damage * .18f, Size * .12f,
                travelRange: Simulation.TileSize * (10 + Phase * 2), color: UiTheme.Cream,
                shape: "diamond", owner: _owner)
            {
                TelegraphDuration = .24f,
            };
            context.ProjectileSink.Add(pulse);
        }
        float aimed = Aim(context.PlayerWorldX, context.PlayerWorldY);
        foreach (float side in new[] { -1f, 1f })
        {
            var echo = new EnemyProjectile(
                center.X, center.Y, aimed, 1.05f + Phase * .08f,
                Damage * .26f, Size * .13f,
                travelRange: Simulation.TileSize * 16f, color: PhaseAccent,
                shape: "diamond", path: "sine",
                amplitude: side * Simulation.TileSize * (.45f + Phase * .12f),
                frequency: .026f, owner: _owner)
            {
                TelegraphDuration = .32f,
            };
            context.ProjectileSink.Add(echo);
        }
    }

    private void FireTouch(EnemyUpdateContext context)
    {
        var center = Center();
        float aimed = Aim(context.PlayerWorldX, context.PlayerWorldY);
        int banks = 1 + Phase + (FloorNumber > 5 ? 1 : 0);
        for (int index = 0; index < banks; index++)
        {
            float offset = (index - (banks - 1) / 2f) * .24f;
            var bank = new EnemyProjectile(
                center.X, center.Y, aimed + offset, .48f + Phase * .04f,
                Damage * .3f, Size * (.28f + Phase * .035f),
                travelRange: Simulation.TileSize * 12f, color: new Color(103, 91, 55),
                path: "bank", owner: _owner)
            {
                TelegraphDuration = .78f,
            };
            context.ProjectileSink.Add(bank);
        }
        if (Phase >= 2)
        {
            float poolSize = Simulation.TileSize * (1.25f + Phase * .18f);
            var pool = new EnemyProjectile(
                context.PlayerWorldX - poolSize / 2f, context.PlayerWorldY - poolSize / 2f,
                0, 0, Damage * .23f, poolSize,
                color: new Color(79, 101, 55), path: "pool",
                lifetime: 4.0f + Phase, owner: _owner, ignoreWalls: true)
            {
                TelegraphDuration = 1.0f,
                PersistentHazard = true,
            };
            context.ProjectileSink.Add(pool);
        }
    }

    private void FireSight(EnemyUpdateContext context)
    {
        var center = Center();
        float aimed = Aim(context.PlayerWorldX, context.PlayerWorldY);
        int count = 4 + Phase * 2 + (FloorNumber > 5 ? 2 : 0);
        for (int index = 0; index < count; index++)
        {
            float fraction = index / (float)Math.Max(1, count - 1);
            float offset = -.34f + fraction * .68f;
            var ray = new EnemyProjectile(
                center.X, center.Y, aimed + offset, 1.55f + Phase * .18f,
                Damage * .16f, Size * .085f,
                travelRange: Simulation.TileSize * 14f, color: new Color(135, 210, 230),
                shape: "diamond", owner: _owner)
            {
                TelegraphDuration = .32f,
            };
            context.ProjectileSink.Add(ray);
        }
        if (Phase >= 2 || FloorNumber > 5)
        {
            var laser = new EnemyProjectile(
                center.X, center.Y, aimed, 0, Damage * .32f, Size * .1f,
                travelRange: Simulation.TileSize * 22f, color: new Color(228, 142, 63),
                path: "laser", lifetime: 1.1f + Phase * .18f,
                angularSpeed: Phase == 3 ? (_rng.Next(2) == 0 ? .08f : -.08f) : 0,
                owner: _owner, ignoreWalls: true)
            {
                TelegraphDuration = .86f,
            };
            context.ProjectileSink.Add(laser);
        }
    }

    private void FireChemesthesis(EnemyUpdateContext context)
    {
        int count = 2 + Phase + (FloorNumber > 5 ? 1 : 0);
        for (int index = 0; index < count; index++)
        {
            float angle = index * MathF.Tau / count + Age * .01f;
            float radius = Simulation.TileSize * (1.0f + index * .65f);
            float mineSize = Simulation.TileSize * (.38f + Phase * .05f);
            var mine = new EnemyProjectile(
                context.PlayerWorldX + MathF.Cos(angle) * radius - mineSize / 2f,
                context.PlayerWorldY + MathF.Sin(angle) * radius - mineSize / 2f,
                0, 0, Damage * .22f, mineSize,
                color: new Color(211, 91, 38), shape: "mine", path: "mine",
                lifetime: 6f + Phase, owner: _owner, ignoreWalls: true)
            {
                TelegraphDuration = .82f + index * .08f,
                PersistentHazard = true,
            };
            context.ProjectileSink.Add(mine);
        }

        var center = Center();
        int spores = 3 + Phase + (FloorNumber > 5 ? 1 : 0);
        float aimed = Aim(context.PlayerWorldX, context.PlayerWorldY);
        for (int index = 0; index < spores; index++)
        {
            float offset = (index - (spores - 1) / 2f) * .19f;
            context.ProjectileSink.Add(new EnemyProjectile(
                center.X, center.Y, aimed + offset, .68f,
                Damage * .18f, Size * .13f,
                travelRange: Simulation.TileSize * 18f, color: new Color(116, 132, 50),
                shape: "diamond", path: "sine", amplitude: Simulation.TileSize * .28f,
                frequency: .02f, owner: _owner));
        }
    }

    private void FirePhantasia(EnemyUpdateContext context)
    {
        var center = Center();
        float aimed = Aim(context.PlayerWorldX, context.PlayerWorldY);
        int count = 5 + Phase * 2 + (FloorNumber > 5 ? 2 : 0);
        int realIndex = _rng.Next(count);
        for (int index = 0; index < count; index++)
        {
            float offset = (index - (count - 1) / 2f) * .12f;
            bool real = index == realIndex
                || (Phase == 3 && index == count - 1 - realIndex)
                || (FloorNumber > 5 && Phase >= 2
                    && index == (realIndex + count / 2) % count);
            var petal = new EnemyProjectile(
                center.X, center.Y, aimed + offset, 1.0f + Phase * .08f,
                real ? Damage * .24f : 0, Size * .105f,
                travelRange: Simulation.TileSize * 16f, color: new Color(202, 85, 174),
                shape: "diamond", owner: _owner)
            {
                Illusory = !real,
                TruthMarked = real,
                TelegraphDuration = real ? .38f : .18f,
            };
            context.ProjectileSink.Add(petal);
        }
        if (Phase >= 2)
        {
            int orbitCount = Phase + 1;
            float radius = Simulation.TileSize * (1.45f + Phase * .18f);
            for (int index = 0; index < orbitCount; index++)
            {
                float angle = index * MathF.Tau / orbitCount;
                context.ProjectileSink.Add(new EnemyProjectile(
                    context.PlayerWorldX + MathF.Cos(angle) * radius,
                    context.PlayerWorldY + MathF.Sin(angle) * radius,
                    0, 0, Damage * .16f, Size * .09f,
                    color: PhaseAccent, shape: "diamond", path: "orbit",
                    lifetime: 4.4f, orbitCenter: new Vector2(context.PlayerWorldX, context.PlayerWorldY),
                    orbitRadius: radius, orbitAngle: angle, angularSpeed: .62f + Phase * .1f,
                    owner: _owner, ignoreWalls: true));
            }
        }
    }

    public override void Draw(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        DrawArenaState(spriteBatch, camera, playerWorldPosition, screenShake);
        Vector2 screen = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        int bob = (int)(Math.Abs(MathF.Sin(Age * .06f)) * Size * .04f);
        var body = new Rectangle((int)screen.X, (int)(screen.Y - bob), (int)Size, (int)Size);
        Color accent = PhaseAccent;
        Color bodyColor = Color.Lerp(UiTheme.Ink, accent, .52f);
        var center = body.Center.ToVector2();

        if (Dying)
        {
            float deathProgress = (float)Math.Clamp(
                1.0 - DeathRemaining / DeathDuration, 0.0, 1.0);
            BossVisuals.Disassemble(spriteBatch, center, Age, deathProgress,
                Size, accent, SecondaryAccent);
            return;
        }

        if (AttackAnticipation > 0)
        {
            int warningRadius = (int)(Size * (.58f + AttackAnticipation * .18f));
            Primitives2D.CircleOutline(spriteBatch, center, warningRadius,
                SecondaryAccent * (.35f + AttackAnticipation * .55f),
                Math.Max(2, (int)(Size * .035f)), 28);
        }

        // Offset slabs and a hard shadow retain the game's block-built 2.5D silhouette.
        Primitives2D.FillRect(spriteBatch,
            new Rectangle(body.X + 8, body.Y + 10, body.Width, body.Height), UiTheme.Shadow);
        Primitives2D.FillRect(spriteBatch, body, bodyColor);
        Primitives2D.RectOutline(spriteBatch, body, UiTheme.Ink, Math.Max(5, (int)(Size * .075f)));
        var topSlab = new Rectangle(body.X + 8, body.Y + 7, body.Width - 16, Math.Max(8, body.Height / 5));
        Primitives2D.FillRect(spriteBatch, topSlab, UiTheme.Lighten(bodyColor, 28));
        Primitives2D.Line(spriteBatch, new Vector2(topSlab.Left, topSlab.Bottom),
            new Vector2(topSlab.Right, topSlab.Bottom), accent, 3);

        int radius = Math.Max(9, body.Width / 5);
        DrawSenseArchitecture(spriteBatch, body, center, radius);
        DrawSenseSigil(spriteBatch, center, radius, accent);
        for (int index = 0; index < Phase; index++)
        {
            var pip = new Rectangle(body.X + 8 + index * 11, body.Bottom - 15, 7, 7);
            Primitives2D.FillRect(spriteBatch, pip, accent);
        }
        if (Invulnerable)
        {
            var shield = body;
            shield.Inflate(10, 10);
            Primitives2D.RectOutline(spriteBatch, shield,
                TrialActive ? SecondaryAccent : UiTheme.Cream, 4);
        }

        if (EncounterKey is not null && Hp < MaxHp)
        {
            var health = new Rectangle(body.X, body.Bottom + 9,
                body.Width, Math.Max(6, body.Height / 13));
            Primitives2D.FillRect(spriteBatch, health, UiTheme.Ink);
            var fill = health;
            fill.Inflate(-2, -2);
            fill.Width = (int)(fill.Width * Math.Clamp(
                Hp / (float)Math.Max(1, MaxHp), 0f, 1f));
            Primitives2D.FillRect(spriteBatch, fill, accent);
        }

        if (PhaseAnnouncementRemaining > 0)
        {
            string heading = TrialActive
                ? $"TRIAL // {PhaseLabel}"
                : $"PHASE {Phase} // {PhaseLabel}";
            UiTheme.DrawText(spriteBatch, heading,
                Math.Max(10, Size * .115f), TrialActive ? SecondaryAccent : accent,
                new Vector2(center.X, body.Top - 34), "midbottom");
            UiTheme.DrawText(spriteBatch, PhaseFlavor,
                Math.Max(8, Size * .075f), UiTheme.Cream,
                new Vector2(center.X, body.Top - 19), "midbottom");
        }
    }

    private void DrawArenaState(
        SpriteBatch spriteBatch,
        Camera camera,
        Vector2 playerWorldPosition,
        Vector2 screenShake)
    {
        Vector2 center = camera.WorldToScreen(
            ArenaCenter, playerWorldPosition, screenShake);
        float radius = Math.Max(24f,
            camera.WorldVectorToScreen(Vector2.UnitX * ArenaRadius).Length());
        Color ring = TrialActive ? SecondaryAccent : PhaseAccent;
        float alpha = TrialActive ? .72f : .22f;
        Primitives2D.CircleOutline(spriteBatch, center, radius,
            UiTheme.Ink * (alpha + .12f), TrialActive ? 7 : 4, 64);
        Primitives2D.CircleOutline(spriteBatch, center, radius,
            ring * alpha, TrialActive ? 3 : 1, 64);

        int marks = SenseKey switch
        {
            "sound" => 12,
            "touch" => 4,
            "sight" => 8,
            "chemesthesis" => 6,
            _ => 10,
        };
        float rotation = Age * (TrialActive ? .006f : .0025f)
            * (SenseKey == "touch" ? 0 : 1);
        for (int index = 0; index < marks; index++)
        {
            float angle = rotation + index * MathF.Tau / marks;
            Vector2 direction = new(MathF.Cos(angle), MathF.Sin(angle));
            Vector2 inner = center + direction * (radius - (TrialActive ? 15 : 9));
            Vector2 outer = center + direction * (radius + (TrialActive ? 3 : 0));
            Primitives2D.Line(spriteBatch, inner, outer,
                index % 2 == 0 ? ring * alpha : UiTheme.Cream * (alpha * .65f),
                TrialActive ? 3 : 1);
        }

        if (TrialActive)
        {
            float progress = (float)Math.Clamp(
                TrialRemaining / Math.Max(.01, TrialDuration), 0.0, 1.0);
            Primitives2D.Arc(spriteBatch,
                new Rectangle(
                    (int)(center.X - radius * .82f),
                    (int)(center.Y - radius * .82f),
                    (int)(radius * 1.64f),
                    (int)(radius * 1.64f)),
                -MathF.PI / 2f,
                -MathF.PI / 2f + MathF.Tau * progress,
                UiTheme.Cream, 3, 64);
        }
    }

    private void DrawSenseArchitecture(
        SpriteBatch spriteBatch,
        Rectangle body,
        Vector2 center,
        int radius)
    {
        int stroke = Math.Max(2, body.Width / 28);
        switch (SenseKey)
        {
            case "sound":
                for (int side = -1; side <= 1; side += 2)
                {
                    var speaker = new Rectangle(
                        (int)(center.X + side * body.Width * .44f - radius * .42f),
                        (int)(center.Y - radius * .58f),
                        (int)(radius * .84f), (int)(radius * 1.16f));
                    Primitives2D.Arc(spriteBatch, speaker,
                        side < 0 ? MathF.PI / 2f : -MathF.PI / 2f,
                        side < 0 ? MathF.PI * 1.5f : MathF.PI / 2f,
                        SecondaryAccent, stroke, 18);
                }
                break;
            case "touch":
                Primitives2D.FillRect(spriteBatch,
                    new Rectangle(body.Left - stroke * 2, body.Top + body.Height / 5,
                        stroke * 3, body.Height * 3 / 5), SecondaryAccent);
                Primitives2D.FillRect(spriteBatch,
                    new Rectangle(body.Right - stroke, body.Top + body.Height / 5,
                        stroke * 3, body.Height * 3 / 5), SecondaryAccent);
                Primitives2D.CircleOutline(spriteBatch,
                    center + new Vector2(0, radius * .95f), radius / 2,
                    UiTheme.Cream, stroke, 16);
                break;
            case "sight":
                Primitives2D.PolygonOutline(spriteBatch, new[]
                {
                    center + new Vector2(0, -radius * 1.55f),
                    center + new Vector2(radius * 1.7f, 0),
                    center + new Vector2(0, radius * 1.55f),
                    center + new Vector2(-radius * 1.7f, 0),
                }, SecondaryAccent, stroke);
                break;
            case "chemesthesis":
                for (int index = 0; index < 4; index++)
                {
                    float angle = index * MathF.PI / 2f + MathF.PI / 4f;
                    Primitives2D.FillCircle(spriteBatch,
                        center + new Vector2(MathF.Cos(angle), MathF.Sin(angle))
                        * radius * 1.35f,
                        Math.Max(3, radius / 4),
                        index % 2 == 0 ? SecondaryAccent : PhaseAccent);
                }
                break;
            case "phantasia":
                for (int index = 0; index < 4; index++)
                {
                    float angle = Age * .015f + index * MathF.PI / 2f;
                    Vector2 prism = center
                        + new Vector2(MathF.Cos(angle), MathF.Sin(angle))
                        * radius * 1.45f;
                    Primitives2D.PolygonOutline(spriteBatch, new[]
                    {
                        prism + new Vector2(0, -5),
                        prism + new Vector2(5, 0),
                        prism + new Vector2(0, 5),
                        prism + new Vector2(-5, 0),
                    }, index % 2 == 0 ? SecondaryAccent : PhaseAccent, stroke);
                }
                break;
        }
    }

    private void DrawSenseSigil(SpriteBatch spriteBatch, Vector2 center, int radius, Color accent)
    {
        int stroke = Math.Max(3, radius / 5);
        switch (SenseKey)
        {
            case "sound":
                for (int ring = 1; ring <= 2; ring++)
                    Primitives2D.Arc(spriteBatch,
                        new Rectangle((int)center.X - radius * ring / 2, (int)center.Y - radius * ring / 2,
                            radius * ring, radius * ring),
                        -MathF.PI / 2, MathF.PI / 2, accent, stroke);
                break;
            case "touch":
                Primitives2D.Line(spriteBatch, center + new Vector2(-radius, 0), center + new Vector2(radius, 0), accent, stroke);
                Primitives2D.FillCircle(spriteBatch, center, radius * .35f, UiTheme.Cream);
                break;
            case "sight":
                Primitives2D.CircleOutline(spriteBatch, center, radius, accent, stroke);
                Primitives2D.FillCircle(spriteBatch, center, radius * .32f, UiTheme.Red);
                break;
            case "chemesthesis":
                for (int index = 0; index < 3; index++)
                {
                    float angle = index * MathF.Tau / 3f;
                    Primitives2D.FillCircle(spriteBatch,
                        center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius * .72f,
                        radius * .28f, accent);
                }
                break;
            case "phantasia":
                var diamond = new[]
                {
                    center + new Vector2(0, -radius), center + new Vector2(radius, 0),
                    center + new Vector2(0, radius), center + new Vector2(-radius, 0),
                };
                Primitives2D.PolygonOutline(spriteBatch, diamond, accent, stroke);
                Primitives2D.FillCircle(spriteBatch, center, radius * .22f, UiTheme.Gold);
                break;
        }
    }
}
