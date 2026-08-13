using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.Presentation;
using RotBoiRemastered.Systems;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

public enum AphantasiaMovementMode
{
    Standing,
    Pathed,
    Chase,
}

public enum AphantasiaMovementKind
{
    Stationary,
    Pathed,
    Chase,
}

public enum AphantasiaMiniKind
{
    Light,
    Dark,
}

public enum AphantasiaMiniDisposition
{
    Passive,
    Aggressive,
    Empowered,
    Destroyed,
}

public enum AphantasiaFieldMood
{
    Mixed,
    TrueLight,
    TrueDark,
    Tesseract,
    Void,
}

public enum AphantasiaEncounterState
{
    Entrance,
    Combat,
    Survival,
    Transforming,
    MiniExecution,
    Finale,
    Dying,
}

public enum AphantasiaSurvivalKind
{
    None,
    FirstEclipse,
    SecondEclipse,
    GrandChoice,
    EssenceFinale,
    VoidFinale,
}

public sealed record AphantasiaPattern(
    string Key,
    string Label,
    AphantasiaMovementMode Movement);

public sealed record AphantasiaPatternDefinition(
    int Index,
    string Key,
    string Label,
    AphantasiaMovementKind Movement,
    bool UsesPortals);

/// <summary>Encounter-owned target used for The Light and The Dark hitboxes.</summary>
public sealed class AphantasiaMini
{
    public required string Name { get; init; }
    public required Color Accent { get; init; }
    public Vector2 Position { get; internal set; }
    public Vector2 Velocity { get; internal set; }
    public int MaxHp { get; internal set; }
    public int Hp { get; internal set; }
    public bool Aggressive { get; internal set; }
    public bool Empowered { get; internal set; }
    public bool PermanentlyDestroyed { get; internal set; }
    public float FireCooldown { get; internal set; }

    public bool Alive => !PermanentlyDestroyed && Hp > 0;
    public float HealthRatio => Math.Clamp(Hp / Math.Max(1f, MaxHp), 0f, 1f);
    public AphantasiaMiniDisposition Disposition => !Alive
        ? AphantasiaMiniDisposition.Destroyed
        : Empowered ? AphantasiaMiniDisposition.Empowered
        : Aggressive ? AphantasiaMiniDisposition.Aggressive
        : AphantasiaMiniDisposition.Passive;
    public bool Vulnerable { get; internal set; }
}

/// <summary>
/// The campaign's final encounter. Aphantasia owns three health bars, two
/// targetable Minis, eighteen authored movement patterns, and every survival
/// gate so the session only needs to route ordinary boss hitboxes and shots.
/// </summary>
public sealed class Aphantasia : Enemy, IBossArenaController, IBossArenaOcclusion
{
    public const string EssenceName = "Aphantasia, Essence of Darkness";
    public const string CoreName = "Aphantasia, Core of The Void";
    public const int BaseBarHealth = 260_000;
    public const int BaseMiniHealth = 13_708;
    public const int EmpoweredMiniHealth = 43_989;
    public const float MiniPathedRadiusRatio = .76f;
    public const int CompactPathedPatternWeight = 2;
    public const int ExpandedPathedPatternWeight = 3;
    public const double SubphaseDuration = 40.0;
    public const double SubphaseDeclarationDuration = .9;
    public const double SequenceTransitionDuration = .55;
    public const double DamageWindowDuration = 5.0;
    public const double EarlySurvivalDuration = 30.0;
    public const double PhaseThreeSurvivalDuration = 40.0;
    public const double PhaseFourFinaleDuration = 60.0;
    public const double TesseractTransitionDuration = 5.0;
    public const int ActiveThreatSoftCap = 320;
    public const int PerimeterThreatReserve = 24;
    public const double PerimeterPressureCadence = 1.8;
    public const int PerimeterPressureCount = 8;

    public static readonly IReadOnlyList<AphantasiaPattern> PhaseOnePatterns =
    [
        new("ordered_bloom", "ORDERED BLOOM", AphantasiaMovementMode.Standing),
        new("horizon_ellipse", "HORIZON ELLIPSE", AphantasiaMovementMode.Pathed),
        new("tidal_pursuit", "TIDAL PURSUIT", AphantasiaMovementMode.Chase),
    ];

    public static readonly IReadOnlyList<AphantasiaPattern> PhaseTwoPatterns =
    [
        new("broken_bloom", "BROKEN BLOOM", AphantasiaMovementMode.Standing),
        new("erratic_eight", "ERRATIC EIGHT", AphantasiaMovementMode.Pathed),
        new("undertow", "UNDERTOW", AphantasiaMovementMode.Chase),
    ];

    public static readonly IReadOnlyList<AphantasiaPattern> PhaseThreePatterns =
    [
        new("prism_bloom", "PRISM BLOOM", AphantasiaMovementMode.Standing),
        new("lattice_curtain", "LATTICE CURTAIN", AphantasiaMovementMode.Standing),
        new("tesseract_eight", "TESSERACT EIGHT", AphantasiaMovementMode.Pathed),
        new("folding_perimeter", "FOLDING PERIMETER", AphantasiaMovementMode.Pathed),
        new("ribbon_pursuit", "RIBBON PURSUIT", AphantasiaMovementMode.Chase),
        new("satellite_spiral", "SATELLITE SPIRAL", AphantasiaMovementMode.Chase),
    ];

    public static readonly IReadOnlyList<AphantasiaPattern> PhaseFourPatterns =
    [
        new("portal_constellation", "PORTAL CONSTELLATION", AphantasiaMovementMode.Standing),
        new("void_clock", "NESTED VOID CLOCK", AphantasiaMovementMode.Standing),
        new("pane_procession", "DRIFTING PANE PROCESSION", AphantasiaMovementMode.Pathed),
        new("portal_lattice", "FOLDING PORTAL LATTICE", AphantasiaMovementMode.Pathed),
        new("portal_wake", "PORTAL WAKE PURSUIT", AphantasiaMovementMode.Chase),
        new("tesseract_hunt", "COLLAPSING TESSERACT HUNT", AphantasiaMovementMode.Chase),
    ];

    public static IReadOnlyList<AphantasiaPattern> AllPatterns { get; } =
        PhaseOnePatterns.Concat(PhaseTwoPatterns)
            .Concat(PhaseThreePatterns).Concat(PhaseFourPatterns).ToArray();

    private static readonly IReadOnlyList<string> FirstEclipseStages =
        ["ORDERED RINGS", "VERTICAL TIDES", "OPPOSING FANS", "CLOSING CROSS-WAVES"];
    private static readonly IReadOnlyList<string> SecondEclipseStages =
        ["BROKEN RINGS", "STAGGERED CURTAINS", "ERRATIC EXCHANGE", "ASYMMETRIC BRAID"];
    private static readonly IReadOnlyList<string> GrandChoiceStages =
        ["RADIANT LANES", "DARK CURLS", "DIVIDED HORIZON", "CHOOSE THE SURVIVOR"];
    private static readonly IReadOnlyList<string> EssenceFinaleStages =
        ["PRISM BLOOM", "FOLDING VERTICAL", "FOLDING HORIZONTAL", "DANCING LATTICE", "TESSERACT CONVERGENCE"];
    private static readonly IReadOnlyList<string> VoidFinaleStages =
        ["PORTAL CONSTELLATION", "NESTED VOID CLOCK", "PANE PROCESSION", "FOLDING PORTAL LATTICE", "PORTAL WAKE", "COLLAPSING TESSERACT"];

    private static readonly int[][] CubeEdges =
    [
        [0, 1], [1, 3], [3, 2], [2, 0],
        [4, 5], [5, 7], [7, 6], [6, 4],
        [0, 4], [1, 5], [2, 6], [3, 7],
    ];

    private static readonly int[][] CubeFaces =
    [
        [0, 1, 3, 2], [4, 6, 7, 5], [0, 4, 5, 1],
        [2, 3, 7, 6], [0, 2, 6, 4], [1, 5, 7, 3],
    ];

    private readonly Random _rng;
    private readonly List<int> _patternBag = [];
    private readonly List<EnemyProjectile> _volleyScratch = new(64);
    private readonly (string Part, Rectangle Rect)[] _worldHitboxes = new (string, Rectangle)[3];
    private readonly (string Part, Rectangle Rect)[] _screenHitboxes = new (string, Rectangle)[3];
    private readonly Vector2[] _arenaMask = new Vector2[96];
    private int _patternIndex = -1;
    private double _subphaseRemaining;
    private double _damageWindowRemaining;
    private bool _damageWindowOpened;
    private double _attackRemaining;
    private double _perimeterPressureRemaining = .8;
    private double _stateElapsed;
    private double _visualTime;
    private double _transitionRemaining;
    private double _deathRemaining;
    private int _transitionTargetPhase;
    private int _survivalMovement;
    private int _patternBagPhase;
    private int _sequenceStage = -1;
    private double _sequenceTransitionRemaining;
    private bool _firstSurvivalDone;
    private bool _secondSurvivalDone;
    private bool _phaseThreeChoiceDone;
    private bool _healthScaleCaptured;
    private int _barMaxHp;
    private bool _displayZeroHealth;
    private bool _finalDeathReady;

    public BossPresentationProfile PresentationProfile { get; } =
        BossPresentationProfile.For(BossMotionTheme.Phantasia, BossVisualTier.Finale);
    public Vector2 ArenaCenter { get; }
    public float ArenaRadius { get; }
    public float Contraction => 0f;
    public IReadOnlyList<Rectangle> MovementObstacles => Array.Empty<Rectangle>();
    public IReadOnlyList<Rectangle> HazardBoundaries => Array.Empty<Rectangle>();
    public int Phase { get; private set; } = 1;
    public string PhaseLabel { get; private set; } = "ESSENCE I";
    public Color PhaseAccent { get; private set; } = new(34, 75, 180);
    public double EntranceRemaining { get; set; } = 4.0;
    public AphantasiaEncounterState EncounterState { get; private set; } = AphantasiaEncounterState.Entrance;
    public AphantasiaSurvivalKind SurvivalKind { get; private set; }
    public double SurvivalRemaining { get; private set; }
    public double SurvivalDuration { get; private set; }
    public bool PresentationSurvivalActive =>
        EncounterState is AphantasiaEncounterState.Survival
            or AphantasiaEncounterState.MiniExecution
            or AphantasiaEncounterState.Finale;
    public bool Dying => EncounterState == AphantasiaEncounterState.Dying;
    /// <summary>True while the terminal collapse is controlling removal.</summary>
    public bool CompletionReady => Dying;
    public bool PhaseFourEligible { get; }
    public bool Phase4Eligible => PhaseFourEligible;
    public bool CapturedNoHealing { get; }
    public bool CapturedNoExtract { get; }
    public bool NoHealingCaptured => CapturedNoHealing;
    public bool NoExtractCaptured => CapturedNoExtract;
    public AphantasiaMini Light { get; }
    public AphantasiaMini Dark { get; }
    public bool TrueLight => Phase <= 2 && Light.Aggressive && Dark.Aggressive;
    public bool TrueDark => Phase <= 2 && !Light.Aggressive && !Dark.Aggressive;
    public AphantasiaFieldMood FieldMood => Phase switch
    {
        4 => AphantasiaFieldMood.Void,
        3 => AphantasiaFieldMood.Tesseract,
        _ when TrueLight => AphantasiaFieldMood.TrueLight,
        _ when TrueDark => AphantasiaFieldMood.TrueDark,
        _ => AphantasiaFieldMood.Mixed,
    };
    public bool DamageWindowActive => _damageWindowRemaining > 0;
    public bool BossDamageable => EncounterState == AphantasiaEncounterState.Combat
        && EntranceRemaining <= 0
        && (Phase > 2 || !Light.Alive && !Dark.Alive && DamageWindowActive);
    public double DamageWindowRemaining => _damageWindowRemaining;
    public double SubphaseRemaining => _subphaseRemaining;
    public int PatternIndex => _patternIndex;
    public int SubPhaseIndex => _patternIndex;
    public double SubPhaseElapsed => Math.Max(0, SubphaseDuration - _subphaseRemaining);
    public AphantasiaPattern CurrentPattern => PatternPool()[Math.Clamp(_patternIndex, 0, PatternPool().Count - 1)];
    public int SequenceStage => Math.Max(0, _sequenceStage);
    public string SequenceStageLabel
    {
        get
        {
            IReadOnlyList<string> labels = SequenceLabelsFor(SurvivalKind);
            if (labels.Count > 0)
                return labels[Math.Clamp(SequenceStage, 0, labels.Count - 1)];
            if (EncounterState == AphantasiaEncounterState.MiniExecution)
                return Light.Alive ? "RADIANT EXECUTION" : "DARK EXECUTION";
            return CurrentPattern.Label;
        }
    }
    public string ObjectiveText => EncounterState switch
    {
        AphantasiaEncounterState.Entrance => "THE VOID AWAKENS",
        AphantasiaEncounterState.Transforming => "TRANSFORMING",
        AphantasiaEncounterState.Dying => "THE VOID COLLAPSES",
        AphantasiaEncounterState.MiniExecution =>
            $"DESTROY {(Light.Alive ? Light.Name : Dark.Name)}",
        AphantasiaEncounterState.Finale => $"SURVIVE // {SurvivalRemaining:0.0}s",
        AphantasiaEncounterState.Survival when SurvivalKind == AphantasiaSurvivalKind.GrandChoice =>
            SurvivalRemaining > 0 ? $"DESTROY ONE // {SurvivalRemaining:0.0}s" : "CHOOSE NOW",
        AphantasiaEncounterState.Survival => $"SURVIVE // {SurvivalRemaining:0.0}s",
        _ when Phase <= 2 && DamageWindowActive => $"VULNERABLE // {DamageWindowRemaining:0.0}s",
        _ when Phase <= 2 => $"{DispositionText()} // BREAK BOTH",
        _ when Phase == 3 && _phaseThreeChoiceDone =>
            $"{(Light.Alive ? Light.Name : Dark.Name)} EMPOWERED",
        _ => "ENDURE THE TESSERACT",
    };
    public string DisplayName => Phase == 4 ? CoreName : EssenceName;
    public double FinaleRemaining => EncounterState == AphantasiaEncounterState.Finale
        ? SurvivalRemaining : 0;
    public float DisplayedHp => _displayZeroHealth ? 0 : Math.Max(0, Hp);
    public float DisplayedMaxHp => Math.Max(1, _barMaxHp > 0 ? _barMaxHp : MaxHp);
    public float ArenaLightIntensity => TrueLight ? 1.2f : TrueDark ? .86f : Phase == 4 ? .95f : 1f;
    public float ArenaDarknessScale => TrueLight ? .56f : TrueDark ? .78f : Phase == 4 ? .82f : .7f;
    public float ArenaPlayerLightScale => TrueLight ? 1.38f : TrueDark ? 1.28f : Phase == 4 ? 1.22f : 1.25f;

    public static int PatternCountForPhase(int phase) => PatternsForPhase(phase).Count;

    public static int PatternSelectionCycleCount(int phase)
    {
        IReadOnlyList<AphantasiaPattern> patterns = phase switch
        {
            1 => PhaseOnePatterns,
            2 => PhaseTwoPatterns,
            3 => PhaseThreePatterns,
            4 => PhaseFourPatterns,
            _ => throw new ArgumentOutOfRangeException(nameof(phase)),
        };
        int pathedWeight = patterns.Count <= 3
            ? CompactPathedPatternWeight
            : ExpandedPathedPatternWeight;
        return patterns.Sum(pattern => pattern.Movement == AphantasiaMovementMode.Pathed
            ? pathedWeight : 1);
    }

    public static IReadOnlyList<AphantasiaPatternDefinition> PatternsForPhase(int phase)
    {
        IReadOnlyList<AphantasiaPattern> source = phase switch
        {
            1 => PhaseOnePatterns,
            2 => PhaseTwoPatterns,
            3 => PhaseThreePatterns,
            4 => PhaseFourPatterns,
            _ => throw new ArgumentOutOfRangeException(nameof(phase)),
        };
        return source.Select((pattern, index) => new AphantasiaPatternDefinition(
            index,
            pattern.Key,
            pattern.Label,
            pattern.Movement switch
            {
                AphantasiaMovementMode.Standing => AphantasiaMovementKind.Stationary,
                AphantasiaMovementMode.Pathed => AphantasiaMovementKind.Pathed,
                _ => AphantasiaMovementKind.Chase,
            },
            UsesPortals: phase == 4)).ToArray();
    }

    public Aphantasia(
        float worldX,
        float worldY,
        Battleground battleground,
        Random? rng = null,
        bool noHealing = false,
        bool noExtract = false)
        : base(worldX, worldY, 1.45f, Simulation.TileSize * 2.15f,
            new Color(8, 22, 72), 420, BaseBarHealth, 2_400, 5.0,
            float.PositiveInfinity, "finale", "hard", rng)
    {
        _rng = rng ?? Random.Shared;
        ArenaCenter = new Vector2(
            battleground.Width * Simulation.TileSize / 2f,
            battleground.Height * Simulation.TileSize / 2f);
        ArenaRadius = BossArenaFactory.DefinitionFor("aphantasia").PlayableRadiusTiles
            * Simulation.TileSize;
        CapturedNoHealing = noHealing;
        CapturedNoExtract = noExtract;
        PhaseFourEligible = noHealing && noExtract;
        ContentPath = "phantasia";
        Family = "aphantasia";
        Light = NewMini("THE LIGHT", new Color(245, 228, 136));
        Dark = NewMini("THE DARK", new Color(43, 69, 166));
        CenterBody();
        StartNextSubphase(revivePair: true);
    }

    public override bool ReceivesKnockback => false;

    private AphantasiaMini NewMini(string name, Color accent)
    {
        return new AphantasiaMini
        {
            Name = name,
            Accent = accent,
            MaxHp = BaseMiniHealth,
            Hp = BaseMiniHealth,
            Position = BossCenter,
            FireCooldown = .4f + (float)_rng.NextDouble() * .35f,
        };
    }

    private void CaptureScaledHealth()
    {
        if (_healthScaleCaptured)
            return;
        _healthScaleCaptured = true;
        _barMaxHp = MaxHp;
        Hp = MaxHp;
    }

    private void CenterBody()
    {
        WorldX = ArenaCenter.X - Size / 2f;
        WorldY = ArenaCenter.Y - Size / 2f;
    }

    private Vector2 BossCenter => new(WorldX + Size / 2f, WorldY + Size / 2f);

    public override void Update(EnemyUpdateContext context)
    {
        CaptureScaledHealth();
        float savedSpeed = Speed;
        bool savedEngagement = EngagementAllowed;
        Speed = 0;
        EngagementAllowed = false;
        base.Update(context);
        Speed = savedSpeed;
        EngagementAllowed = savedEngagement;

        double dt = Simulation.GetTimerStep() / Math.Max(1, Simulation.FrameRate);
        _visualTime += dt;
        _stateElapsed += dt;

        if (EncounterState == AphantasiaEncounterState.Entrance)
        {
            EntranceRemaining = Math.Max(0, EntranceRemaining - dt);
            CenterBody();
            UpdateMinis(context, dt, hazardsOnly: true, allowFire: false);
            if (EntranceRemaining <= 0)
            {
                EncounterState = AphantasiaEncounterState.Combat;
                _stateElapsed = 0;
            }
            return;
        }

        if (Dying)
        {
            _deathRemaining = Math.Max(0, _deathRemaining - dt);
            if (_deathRemaining <= 0)
            {
                _finalDeathReady = true;
                Hp = 0;
            }
            return;
        }

        if (EncounterState is AphantasiaEncounterState.Combat
            or AphantasiaEncounterState.Survival
            or AphantasiaEncounterState.MiniExecution
            or AphantasiaEncounterState.Finale)
        {
            UpdatePerimeterPressure(context, dt);
        }

        switch (EncounterState)
        {
            case AphantasiaEncounterState.Transforming:
                UpdateTransformation(context, dt);
                break;
            case AphantasiaEncounterState.Survival:
                UpdateSurvival(context, dt);
                break;
            case AphantasiaEncounterState.MiniExecution:
                UpdateMiniExecution(context, dt);
                break;
            case AphantasiaEncounterState.Finale:
                UpdateFinale(context, dt);
                break;
            default:
                UpdateCombat(context, dt);
                break;
        }
    }

    private void UpdateCombat(EnemyUpdateContext context, double dt)
    {
        _subphaseRemaining -= dt;
        if (_damageWindowOpened)
            _damageWindowRemaining = Math.Max(0, _damageWindowRemaining - dt);
        UpdateMovement(context, dt);
        bool declarationActive = _stateElapsed < SubphaseDeclarationDuration;
        UpdateMinis(context, dt, hazardsOnly: false, allowFire: !declarationActive);

        if (Phase <= 2 && !Light.Alive && !Dark.Alive && !_damageWindowOpened)
        {
            _damageWindowOpened = true;
            _damageWindowRemaining = DamageWindowDuration;
        }

        _attackRemaining -= dt;
        if (_attackRemaining <= 0)
            FireCurrentPattern(context);

        if (Phase <= 2 && _damageWindowOpened)
        {
            if (_damageWindowRemaining <= 0)
                StartNextSubphase(revivePair: true);
        }
        else if (_subphaseRemaining <= 0)
        {
            StartNextSubphase(revivePair: false);
        }
    }

    private void UpdateTransformation(EnemyUpdateContext context, double dt)
    {
        _transitionRemaining = Math.Max(0, _transitionRemaining - dt);
        CenterBody();
        _attackRemaining -= dt;
        if (_attackRemaining <= 0)
        {
            List<EnemyProjectile> staged = BeginVolley();
            FireTransformationBurst(staged);
            CommitVolley(context.ProjectileSink);
            _attackRemaining = .24;
        }
        if (_transitionRemaining > 0)
            return;

        SetPhase(_transitionTargetPhase);
        EncounterState = AphantasiaEncounterState.Combat;
        _stateElapsed = 0;
        _displayZeroHealth = false;
        Hp = _barMaxHp;
        TransitionCleanupRequested = true;
        StartNextSubphase(revivePair: Phase == 3);
    }

    private void UpdateSurvival(EnemyUpdateContext context, double dt)
    {
        SurvivalRemaining = Math.Max(0, SurvivalRemaining - dt);
        int stageCount = SequenceLabelsFor(SurvivalKind).Count;
        double stageDuration = SurvivalDuration / Math.Max(1, stageCount);
        int desiredStage = Math.Min(stageCount - 1,
            (int)((SurvivalDuration - SurvivalRemaining) / stageDuration));
        bool sequenceReady = PrepareSequenceStage(desiredStage, dt);
        UpdateMinis(context, dt, hazardsOnly: true, allowFire: sequenceReady);
        CenterBody();
        if (sequenceReady)
        {
            _attackRemaining -= dt;
            if (_attackRemaining <= 0)
                FireSurvivalMovement(context);
        }

        if (SurvivalRemaining > 0)
            return;

        if (SurvivalKind == AphantasiaSurvivalKind.GrandChoice
            && Light.Alive && Dark.Alive)
        {
            // The authored clock is forty seconds; at zero the field holds
            // until the player commits to one of the two targets.
            return;
        }

        EndSurvival();
    }

    private void EndSurvival()
    {
        TransitionCleanupRequested = true;
        AphantasiaSurvivalKind completed = SurvivalKind;
        SurvivalKind = AphantasiaSurvivalKind.None;
        SurvivalRemaining = 0;
        SurvivalDuration = 0;
        _stateElapsed = 0;

        if (completed == AphantasiaSurvivalKind.GrandChoice)
        {
            AphantasiaMini survivor = Light.Alive ? Light : Dark;
            AphantasiaMini destroyed = ReferenceEquals(survivor, Light) ? Dark : Light;
            destroyed.PermanentlyDestroyed = true;
            destroyed.Hp = 0;
            survivor.Empowered = true;
            survivor.MaxHp = EmpoweredMiniHealth;
            survivor.Hp = survivor.MaxHp;
            survivor.Aggressive = true;
            _phaseThreeChoiceDone = true;
        }
        else
        {
            ReviveMiniPair();
        }

        EncounterState = AphantasiaEncounterState.Combat;
        StartNextSubphase(revivePair: false);
    }

    private void UpdateMiniExecution(EnemyUpdateContext context, double dt)
    {
        CenterBody();
        UpdateMinis(context, dt, hazardsOnly: false);
        _attackRemaining -= dt;
        if (_attackRemaining <= 0)
        {
            List<EnemyProjectile> staged = BeginVolley();
            FireDancingBullets(staged, slow: true);
            CommitVolley(context.ProjectileSink);
            _attackRemaining = .62;
        }

        if (!Light.Alive && !Dark.Alive)
            BeginFinale(AphantasiaSurvivalKind.EssenceFinale, PhaseThreeSurvivalDuration);
    }

    private void UpdateFinale(EnemyUpdateContext context, double dt)
    {
        SurvivalRemaining = Math.Max(0, SurvivalRemaining - dt);
        CenterBody();
        int stageCount = SequenceLabelsFor(SurvivalKind).Count;
        double stageDuration = SurvivalDuration / Math.Max(1, stageCount);
        int desiredStage = Math.Min(stageCount - 1,
            (int)((SurvivalDuration - SurvivalRemaining) / stageDuration));
        if (PrepareSequenceStage(desiredStage, dt))
        {
            _attackRemaining -= dt;
            if (_attackRemaining <= 0)
                FireFinaleMovement(context);
        }
        if (SurvivalRemaining > 0)
            return;

        if (SurvivalKind == AphantasiaSurvivalKind.EssenceFinale
            && PhaseFourEligible)
        {
            BeginTransformation(4);
        }
        else
        {
            BeginDeath();
        }
    }

    private bool PrepareSequenceStage(int desiredStage, double dt)
    {
        desiredStage = Math.Max(0, desiredStage);
        if (_sequenceStage != desiredStage)
        {
            _sequenceStage = desiredStage;
            _survivalMovement = desiredStage;
            _sequenceTransitionRemaining = SequenceTransitionDuration;
            _attackRemaining = SequenceTransitionDuration;
            _perimeterPressureRemaining = 0;
            TransitionCleanupRequested = true;
            return false;
        }
        if (_sequenceTransitionRemaining <= 0)
            return true;
        _sequenceTransitionRemaining = Math.Max(0, _sequenceTransitionRemaining - dt);
        return false;
    }

    private void UpdateMovement(EnemyUpdateContext context, double dt)
    {
        Vector2 current = new(WorldX + Size / 2f, WorldY + Size / 2f);
        Vector2 target = ArenaCenter;
        float t = (float)_stateElapsed;
        AphantasiaMovementMode mode = CurrentPattern.Movement;
        if (mode == AphantasiaMovementMode.Pathed)
        {
            float radius = ArenaRadius * (Phase >= 3 ? .3f : .24f);
            if (_patternIndex % 2 == 0)
            {
                target += new Vector2(MathF.Cos(t * .43f) * radius,
                    MathF.Sin(t * .86f) * radius * .62f);
            }
            else
            {
                float a = t * .34f;
                target += new Vector2(MathF.Cos(a), MathF.Sin(a))
                    * radius * (.78f + .2f * MathF.Sin(t * .57f));
            }
        }
        else if (mode == AphantasiaMovementMode.Chase)
        {
            Vector2 player = new(context.PlayerWorldX, context.PlayerWorldY);
            Vector2 offset = player - ArenaCenter;
            float limit = ArenaRadius * .58f;
            if (offset.LengthSquared() > limit * limit)
                offset = Vector2.Normalize(offset) * limit;
            target = ArenaCenter + offset;
        }

        float rate = mode == AphantasiaMovementMode.Chase ? 1.35f : .82f;
        if (Phase == 2)
            rate *= .88f;
        float blend = 1f - MathF.Exp(-rate * (float)dt);
        current = Vector2.Lerp(current, target, blend);
        WorldX = current.X - Size / 2f;
        WorldY = current.Y - Size / 2f;
    }

    private void UpdateMinis(EnemyUpdateContext context, double dt, bool hazardsOnly,
        bool allowFire = true)
    {
        if (Phase == 4)
            return;
        Light.Vulnerable = MiniCanTakeDamage(Light);
        Dark.Vulnerable = MiniCanTakeDamage(Dark);
        UpdateMini(Light, -1, context, dt, hazardsOnly, allowFire);
        UpdateMini(Dark, 1, context, dt, hazardsOnly, allowFire);
    }

    private void UpdateMini(AphantasiaMini mini, int side, EnemyUpdateContext context,
        double dt, bool hazardsOnly, bool allowFire)
    {
        if (!mini.Alive)
            return;
        float t = (float)_visualTime;
        Vector2 anchor;
        if (EncounterState == AphantasiaEncounterState.Survival
            && SurvivalKind == AphantasiaSurvivalKind.SecondEclipse
            && SequenceStage == 2)
        {
            anchor = ArenaCenter + new Vector2(
                side * MathF.Cos(t * 1.1f) * ArenaRadius * .42f,
                MathF.Sin(t * 1.34f + side) * ArenaRadius * .2f);
        }
        else if (EncounterState == AphantasiaEncounterState.Survival
            && SurvivalKind == AphantasiaSurvivalKind.GrandChoice)
        {
            anchor = ArenaCenter + new Vector2(
                side * ArenaRadius * .38f,
                MathF.Sin(t * .72f + side) * ArenaRadius * .2f);
        }
        else if (EncounterState == AphantasiaEncounterState.Combat
            && CurrentPattern.Movement == AphantasiaMovementMode.Pathed)
        {
            // Expand the route from the old .42-radius orbit to most of the
            // arena while preserving its approximate tangential speed.
            float previousOrbitRate = (TrueDark ? .16f : .26f) * .42f;
            float angularRate = previousOrbitRate / MiniPathedRadiusRatio;
            float angle = t * angularRate * side + (side < 0 ? MathF.PI : 0);
            anchor = ArenaCenter + new Vector2(
                MathF.Cos(angle) * ArenaRadius * MiniPathedRadiusRatio,
                MathF.Sin(angle * 2f) * ArenaRadius * .42f);
        }
        else if (mini.Aggressive || mini.Empowered || hazardsOnly)
        {
            Vector2 player = new(context.PlayerWorldX, context.PlayerWorldY);
            Vector2 desired = Vector2.Lerp(ArenaCenter, player, mini.Empowered ? .62f : .43f);
            desired += new Vector2(
                MathF.Cos(t * (mini.Empowered ? 1.7f : 1.05f) + side) * ArenaRadius * .12f,
                MathF.Sin(t * (mini.Empowered ? 1.3f : .82f) + side) * ArenaRadius * .12f);
            anchor = desired;
        }
        else
        {
            float speed = TrueDark ? .16f : .26f;
            float angle = t * speed * side + (side < 0 ? MathF.PI : 0);
            anchor = ArenaCenter + new Vector2(MathF.Cos(angle), MathF.Sin(angle) * .55f)
                * ArenaRadius * .42f;
        }
        Vector2 delta = anchor - mini.Position;
        float follow = 1f - MathF.Exp(-(mini.Empowered ? 2.1f : mini.Aggressive ? 1.45f : .72f) * (float)dt);
        mini.Velocity = delta * follow / Math.Max(.001f, (float)dt);
        mini.Position += delta * follow;

        mini.FireCooldown -= (float)dt;
        if (allowFire && mini.FireCooldown <= 0)
        {
            FireMini(mini, context.ProjectileSink,
                new Vector2(context.PlayerWorldX, context.PlayerWorldY));
            mini.FireCooldown = mini.Empowered
                ? .42f
                : mini.Aggressive ? (TrueLight ? .48f : .68f) : (TrueDark ? .58f : .94f);
        }
    }

    private void FireMini(AphantasiaMini mini, List<EnemyProjectile> sink, Vector2 player)
    {
        List<EnemyProjectile> staged = BeginVolley();
        float aim = AngleTo(mini.Position, player);
        int count = mini.Empowered ? (ReferenceEquals(mini, Light) ? 5 : 9)
            : mini.Aggressive ? 4 : TrueDark ? 7 : 5;
        float spread = mini.Empowered ? 1.1f : .72f;
        float speed = mini.Empowered && ReferenceEquals(mini, Light) ? 2.15f
            : mini.Empowered ? 1.05f : mini.Aggressive ? 1.65f : .88f;
        for (int index = 0; index < count; index++)
        {
            float fraction = count == 1 ? .5f : (float)index / (count - 1);
            string path = ReferenceEquals(mini, Dark) && index % 2 == 0 ? "sine" : "linear";
            AddShot(staged, mini.Position, aim - spread / 2f + fraction * spread,
                speed, mini.Empowered ? .34f : .26f, mini.Accent,
                $"mini_{(ReferenceEquals(mini, Light) ? "light" : "dark")}",
                path, path == "sine" ? Simulation.TileSize * .52f : 0f, 8f);
        }
        CommitVolley(sink);
    }

    private void FireCurrentPattern(EnemyUpdateContext context)
    {
        List<EnemyProjectile> staged = BeginVolley();
        if (Phase <= 2)
            FireEssencePattern(context, staged);
        else if (Phase == 3)
            FireTesseractPattern(context, staged);
        else
            FireVoidPattern(context, staged);
        CommitVolley(context.ProjectileSink);
    }

    private void FireEssencePattern(EnemyUpdateContext context, List<EnemyProjectile> sink)
    {
        Vector2 center = new(WorldX + Size / 2f, WorldY + Size / 2f);
        float spin = (float)_visualTime * (Phase == 1 ? .34f : .21f);
        int pattern = _patternIndex;
        bool darkDensity = TrueDark;
        bool lightTempo = TrueLight;
        if (pattern == 0)
        {
            int count = lightTempo ? 10 : darkDensity ? 22 : 16;
            FireRing(sink, center, count, spin, lightTempo ? 2.05f : darkDensity ? .72f : 1.18f,
                darkDensity ? .23f : .3f, "essence_bloom", alternating: true);
            _attackRemaining = lightTempo ? .42 : darkDensity ? .7 : .56;
        }
        else if (pattern == 1)
        {
            FireEdgeCurtain(sink, vertical: ((int)(_stateElapsed * 2) & 1) == 0,
                reverse: Phase == 2 && ((int)_stateElapsed & 1) != 0,
                lanes: darkDensity ? 13 : 9,
                speed: lightTempo ? 1.95f : darkDensity ? .72f : 1.15f,
                owner: "essence_horizon");
            FireRing(sink, center, lightTempo ? 7 : 11, -spin, 1.0f, .24f,
                "essence_cross", alternating: false);
            _attackRemaining = lightTempo ? .46 : .72;
        }
        else
        {
            Vector2 player = new(context.PlayerWorldX, context.PlayerWorldY);
            float aim = AngleTo(center, player);
            int ribbons = lightTempo ? 5 : darkDensity ? 11 : 7;
            for (int index = 0; index < ribbons; index++)
            {
                float offset = (index - (ribbons - 1) / 2f) * .13f;
                AddShot(sink, center, aim + offset, lightTempo ? 2.2f : .9f,
                    .24f + index % 3 * .05f, index % 2 == 0 ? Light.Accent : Dark.Accent,
                    "essence_wake", index % 2 == 0 ? "sine" : "linear",
                    Simulation.TileSize * .44f, 9f);
            }
            _attackRemaining = lightTempo ? .34 : darkDensity ? .58 : .46;
        }
    }

    private void FireTesseractPattern(EnemyUpdateContext context, List<EnemyProjectile> sink)
    {
        Vector2 center = new(WorldX + Size / 2f, WorldY + Size / 2f);
        float spin = (float)_visualTime * .52f;
        switch (_patternIndex)
        {
            case 0:
                FireRing(sink, center, 18, spin, 1.0f, .26f, "prism_outer", true);
                FireRing(sink, center, 11, -spin * 1.4f, 1.48f, .34f, "prism_inner", true);
                _attackRemaining = .62;
                break;
            case 1:
                FireEdgeCurtain(sink, true, ((int)_stateElapsed & 1) == 0, 11, .9f, "lattice_v");
                FireEdgeCurtain(sink, false, ((int)_stateElapsed & 2) == 0, 11, .9f, "lattice_h");
                _attackRemaining = .88;
                break;
            case 2:
                FireRing(sink, center, 12, spin, 1.25f, .25f, "eight_spoke", false);
                FireEdgeCurtain(sink, ((int)_stateElapsed & 1) == 0, false, 7, 1.05f, "eight_fold");
                _attackRemaining = .66;
                break;
            case 3:
                for (int side = 0; side < 4; side++)
                {
                    float angle = side * MathF.PI / 2f + spin;
                    Vector2 origin = ArenaCenter + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * ArenaRadius * .84f;
                    FireFan(sink, origin, angle + MathF.PI, 7, .68f, .88f,
                        side % 2 == 0 ? Light.Accent : Dark.Accent, "folding_inward");
                }
                _attackRemaining = .82;
                break;
            case 4:
                FireAimedRibbon(sink, center, new Vector2(context.PlayerWorldX, context.PlayerWorldY),
                    9, 1.16f, "ribbon_chase");
                _attackRemaining = .48;
                break;
            default:
                FireRing(sink, center, 14, spin * 1.7f, 1.3f, .22f, "satellite_spiral", true);
                FireFan(sink, Light.Position, AngleTo(Light.Position, ArenaCenter), 5, .5f, 1.5f,
                    Light.Accent, "satellite_light");
                FireFan(sink, Dark.Position, AngleTo(Dark.Position, ArenaCenter), 7, .9f, .82f,
                    Dark.Accent, "satellite_dark");
                _attackRemaining = .57;
                break;
        }
    }

    private void FireVoidPattern(EnemyUpdateContext context, List<EnemyProjectile> sink)
        => FireVoidStage(context, sink, _patternIndex);

    private void FireVoidStage(EnemyUpdateContext context, List<EnemyProjectile> sink,
        int stage)
    {
        Vector2 center = new(WorldX + Size / 2f, WorldY + Size / 2f);
        float spin = (float)_visualTime * .38f;
        switch (stage)
        {
            case 0:
                for (int index = 0; index < 5; index++)
                    FirePortalSeed(sink, center, spin + index * MathF.Tau / 5f, .48f, "constellation");
                _attackRemaining = 1.4;
                break;
            case 1:
                FireRing(sink, center, 12, spin, .74f, .42f, "void_clock", true);
                FirePortalSeed(sink, center, -spin, .4f, "clock_hand");
                _attackRemaining = .9;
                break;
            case 2:
                FireEdgePortals(sink, vertical: ((int)_stateElapsed & 1) == 0, "pane_procession");
                _attackRemaining = 1.05;
                break;
            case 3:
                FireEdgePortals(sink, true, "portal_lattice_v");
                FireEdgePortals(sink, false, "portal_lattice_h");
                _attackRemaining = 1.32;
                break;
            case 4:
                FirePortalSeed(sink, center,
                    AngleTo(center, new Vector2(context.PlayerWorldX, context.PlayerWorldY)),
                    .62f, "portal_wake");
                FireAimedRibbon(sink, center, new Vector2(context.PlayerWorldX, context.PlayerWorldY),
                    5, .86f, "void_pursuit");
                _attackRemaining = .62;
                break;
            default:
                for (int index = 0; index < 3; index++)
                    FirePortalSeed(sink, center, spin + index * MathF.Tau / 3f, .7f, "tesseract_hunt");
                FireRing(sink, center, 9, -spin * 2f, 1.42f, .24f, "collapse_ring", true);
                _attackRemaining = .7;
                break;
        }
    }

    private void FireSurvivalMovement(EnemyUpdateContext context)
    {
        double elapsed = SurvivalDuration - SurvivalRemaining;
        List<EnemyProjectile> sink = BeginVolley();
        Vector2 center = ArenaCenter;
        Vector2 player = new(context.PlayerWorldX, context.PlayerWorldY);
        switch (SurvivalKind)
        {
            case AphantasiaSurvivalKind.FirstEclipse:
                FireFirstEclipseStage(sink, elapsed);
                break;
            case AphantasiaSurvivalKind.SecondEclipse:
                FireSecondEclipseStage(sink, elapsed, player);
                break;
            case AphantasiaSurvivalKind.GrandChoice:
                FireGrandChoiceStage(sink, elapsed, player);
                break;
        }
        CommitVolley(context.ProjectileSink);
    }

    private void FireFinaleMovement(EnemyUpdateContext context)
    {
        double elapsed = SurvivalDuration - SurvivalRemaining;
        List<EnemyProjectile> sink = BeginVolley();
        if (SurvivalKind == AphantasiaSurvivalKind.VoidFinale)
        {
            FireVoidStage(context, sink, SequenceStage);
        }
        else
        {
            FireEssenceFinaleStage(sink, elapsed);
        }
        CommitVolley(context.ProjectileSink);
    }

    private void FireFirstEclipseStage(List<EnemyProjectile> sink, double elapsed)
    {
        switch (SequenceStage)
        {
            case 0:
                FireRing(sink, ArenaCenter, 16, (float)elapsed * .23f,
                    .92f, .25f, "first_eclipse_ordered", true);
                _attackRemaining = .64;
                break;
            case 1:
                FireEdgeCurtain(sink, true, ((int)elapsed & 1) == 0,
                    12, .82f, "first_eclipse_vertical");
                _attackRemaining = .74;
                break;
            case 2:
                FireFan(sink, Light.Position, AngleTo(Light.Position, ArenaCenter),
                    7, .72f, 1.24f, Light.Accent, "first_eclipse_light_fan");
                FireFan(sink, Dark.Position, AngleTo(Dark.Position, ArenaCenter),
                    7, .72f, 1.02f, Dark.Accent, "first_eclipse_dark_fan");
                _attackRemaining = .68;
                break;
            default:
                FireEdgeCurtain(sink, true, ((int)elapsed & 1) == 0,
                    10, .9f, "first_eclipse_cross_v");
                FireEdgeCurtain(sink, false, ((int)elapsed & 1) != 0,
                    10, .9f, "first_eclipse_cross_h");
                _attackRemaining = .82;
                break;
        }
    }

    private void FireSecondEclipseStage(List<EnemyProjectile> sink, double elapsed,
        Vector2 player)
    {
        switch (SequenceStage)
        {
            case 0:
                FireBrokenRing(sink, ArenaCenter, 22, (float)elapsed * .31f,
                    .84f, .28f, "second_eclipse_broken");
                _attackRemaining = .58;
                break;
            case 1:
                FireEdgeCurtain(sink, ((int)elapsed & 1) == 0,
                    ((int)(elapsed * 1.5) & 1) == 0, 13, .9f,
                    "second_eclipse_staggered");
                FireBrokenRing(sink, ArenaCenter, 9, -(float)elapsed * .43f,
                    1.18f, .22f, "second_eclipse_offset");
                _attackRemaining = .7;
                break;
            case 2:
                FireFan(sink, Light.Position, AngleTo(Light.Position, player),
                    7, .82f, 1.52f, Light.Accent, "second_eclipse_light_swap");
                FireFan(sink, Dark.Position, AngleTo(Dark.Position, player),
                    9, 1.12f, .82f, Dark.Accent, "second_eclipse_dark_swap");
                _attackRemaining = .6;
                break;
            default:
                Vector2 left = ArenaCenter - new Vector2(ArenaRadius * .86f, ArenaRadius * .42f);
                Vector2 right = ArenaCenter + new Vector2(ArenaRadius * .86f, ArenaRadius * .42f);
                FireAimedRibbon(sink, left, player, 8, .92f, "second_eclipse_braid_left");
                FireAimedRibbon(sink, right, player, 7, .76f, "second_eclipse_braid_right");
                _attackRemaining = .62;
                break;
        }
    }

    private void FireGrandChoiceStage(List<EnemyProjectile> sink, double elapsed,
        Vector2 player)
    {
        switch (SequenceStage)
        {
            case 0:
                FireFan(sink, Light.Position, AngleTo(Light.Position, player),
                    7, .58f, 1.82f, Light.Accent, "grand_choice_radiant");
                FireEdgeCurtain(sink, true, ((int)elapsed & 1) == 0,
                    7, 1.42f, "grand_choice_light_lane");
                _attackRemaining = .52;
                break;
            case 1:
                FireFan(sink, Dark.Position, AngleTo(Dark.Position, player),
                    13, 1.36f, .72f, Dark.Accent, "grand_choice_dark_curl");
                _attackRemaining = .58;
                break;
            case 2:
                FireEdgeCurtain(sink, true, ((int)elapsed & 1) == 0,
                    8, 1.48f, "grand_choice_divide_light");
                FireEdgeCurtain(sink, false, ((int)elapsed & 1) != 0,
                    13, .7f, "grand_choice_divide_dark");
                _attackRemaining = .76;
                break;
            default:
                FireRing(sink, ArenaCenter, 18, (float)elapsed * .37f,
                    .9f, .26f, "grand_choice_convergence", true);
                FireFan(sink, Light.Position, AngleTo(Light.Position, ArenaCenter),
                    5, .52f, 1.6f, Light.Accent, "grand_choice_light_close");
                FireFan(sink, Dark.Position, AngleTo(Dark.Position, ArenaCenter),
                    9, 1.08f, .78f, Dark.Accent, "grand_choice_dark_close");
                _attackRemaining = .68;
                break;
        }
    }

    private void FireEssenceFinaleStage(List<EnemyProjectile> sink, double elapsed)
    {
        switch (SequenceStage)
        {
            case 0:
                FireRing(sink, ArenaCenter, 18, (float)elapsed * .4f,
                    1f, .26f, "essence_finale_prism_outer", true);
                FireRing(sink, ArenaCenter, 11, -(float)elapsed * .56f,
                    1.48f, .32f, "essence_finale_prism_inner", true);
                _attackRemaining = .62;
                break;
            case 1:
                FireEdgeCurtain(sink, true, ((int)elapsed & 1) == 0,
                    13, .94f, "essence_finale_fold_v");
                FireRing(sink, ArenaCenter, 9, (float)elapsed * .31f,
                    1.24f, .23f, "essence_finale_spoke_v", true);
                _attackRemaining = .68;
                break;
            case 2:
                FireEdgeCurtain(sink, false, ((int)elapsed & 1) != 0,
                    13, .94f, "essence_finale_fold_h");
                FireRing(sink, ArenaCenter, 9, -(float)elapsed * .34f,
                    1.24f, .23f, "essence_finale_spoke_h", true);
                _attackRemaining = .68;
                break;
            case 3:
                FireDancingBullets(sink, slow: false);
                FireEdgeCurtain(sink, ((int)elapsed & 1) == 0,
                    ((int)elapsed & 2) == 0, 8, .86f, "essence_finale_lattice");
                _attackRemaining = .58;
                break;
            default:
                float spin = (float)elapsed * .48f;
                for (int side = 0; side < 4; side++)
                {
                    float angle = spin + side * MathF.PI / 2f;
                    Vector2 origin = ArenaCenter + new Vector2(MathF.Cos(angle), MathF.Sin(angle))
                        * ArenaRadius * .84f;
                    FireFan(sink, origin, angle + MathF.PI, 7, .66f, .92f,
                        side % 2 == 0 ? Light.Accent : Dark.Accent,
                        "essence_finale_convergence");
                }
                FireRing(sink, ArenaCenter, 12, -spin, 1.34f, .22f,
                    "essence_finale_core", true);
                _attackRemaining = .82;
                break;
        }
    }

    private void FireDancingBullets(List<EnemyProjectile> sink, bool slow)
    {
        float spin = (float)_visualTime * (slow ? .35f : .72f);
        int count = slow ? 16 : 22;
        for (int index = 0; index < count; index++)
        {
            float angle = spin + index * MathF.Tau / count;
            string path = index % 3 == 0 ? "sine" : "linear";
            AddShot(sink, ArenaCenter, angle, slow ? .78f : 1.18f,
                .2f + index % 4 * .055f, Rainbow(index / (float)count + spin * .05f),
                "dancing_bullets", path, Simulation.TileSize * .46f, 11f);
        }
    }

    private void FireTransformationBurst(List<EnemyProjectile> sink)
    {
        float spin = (float)_visualTime * 1.8f;
        for (int index = 0; index < 8; index++)
            AddShot(sink, ArenaCenter, spin + index * MathF.Tau / 8f,
                .62f + index % 3 * .16f, .18f + index % 2 * .1f,
                Rainbow(index / 8f + spin * .04f), "transformation", "sine",
                Simulation.TileSize * .35f, 3.4f, deliberatelyShortRange: true);
    }

    private void UpdatePerimeterPressure(EnemyUpdateContext context, double dt)
    {
        _perimeterPressureRemaining -= dt;
        if (_perimeterPressureRemaining > 0)
            return;
        _perimeterPressureRemaining = PerimeterPressureCadence;
        List<EnemyProjectile> staged = BeginVolley();
        float rotation = (float)_visualTime * .17f;
        for (int index = 0; index < PerimeterPressureCount; index++)
        {
            float angle = rotation + index * MathF.Tau / PerimeterPressureCount;
            Vector2 origin = ArenaCenter + new Vector2(MathF.Cos(angle), MathF.Sin(angle))
                * ArenaRadius * .93f;
            float opposite = angle + MathF.PI + MathF.Sin(rotation * 1.7f + index) * .24f;
            Vector2 target = ArenaCenter + new Vector2(MathF.Cos(opposite), MathF.Sin(opposite))
                * ArenaRadius * .92f;
            AddShot(staged, origin, AngleTo(origin, target),
                .72f + index % 3 * .08f, .15f + index % 2 * .035f,
                index % 2 == 0 ? Light.Accent * .82f : Dark.Accent * .9f,
                "perimeter_drift", index % 3 == 0 ? "sine" : "linear",
                Simulation.TileSize * .22f, 12f);
        }
        CommitVolley(context.ProjectileSink);
    }

    private void FireRing(List<EnemyProjectile> sink, Vector2 origin, int count,
        float rotation, float speed, float size, string owner, bool alternating)
    {
        for (int index = 0; index < count; index++)
        {
            Color color = alternating
                ? index % 2 == 0 ? Light.Accent : Dark.Accent
                : Rainbow(index / (float)Math.Max(1, count) + rotation * .02f);
            AddShot(sink, origin, rotation + index * MathF.Tau / count,
                speed * (.88f + index % 3 * .08f), size * (.82f + index % 4 * .12f),
                color, owner, index % 4 == 0 ? "sine" : "linear",
                Simulation.TileSize * .34f, 10f);
        }
    }

    private void FireBrokenRing(List<EnemyProjectile> sink, Vector2 origin, int count,
        float rotation, float speed, float size, string owner)
    {
        for (int index = 0; index < count; index++)
        {
            if ((index + (int)(_stateElapsed * 1.7)) % 6 is 2 or 3)
                continue;
            float stagger = index % 2 == 0 ? 0 : .075f;
            AddShot(sink, origin, rotation + index * MathF.Tau / count + stagger,
                speed * (.86f + index % 4 * .08f), size * (.82f + index % 3 * .14f),
                index % 2 == 0 ? Light.Accent : Dark.Accent,
                owner, index % 3 == 0 ? "sine" : "linear",
                Simulation.TileSize * .4f, 10f);
        }
    }

    private void FireFan(List<EnemyProjectile> sink, Vector2 origin, float direction,
        int count, float spread, float speed, Color color, string owner)
    {
        for (int index = 0; index < count; index++)
        {
            float fraction = count == 1 ? .5f : (float)index / (count - 1);
            AddShot(sink, origin, direction - spread / 2f + fraction * spread,
                speed, .25f + index % 3 * .05f, color, owner,
                index % 3 == 0 ? "sine" : "linear", Simulation.TileSize * .38f, 9f);
        }
    }

    private void FireAimedRibbon(List<EnemyProjectile> sink, Vector2 origin,
        Vector2 target, int count, float speed, string owner)
    {
        float aim = AngleTo(origin, target);
        for (int index = 0; index < count; index++)
        {
            float offset = (index - (count - 1) / 2f) * .115f;
            AddShot(sink, origin, aim + offset, speed * (.9f + index % 2 * .16f),
                .22f + index % 4 * .045f,
                index % 2 == 0 ? Light.Accent : Dark.Accent,
                owner, "sine", Simulation.TileSize * (.32f + index % 3 * .12f), 10f);
        }
    }

    private void FireEdgeCurtain(List<EnemyProjectile> sink, bool vertical,
        bool reverse, int lanes, float speed, string owner)
    {
        for (int index = 0; index < lanes; index++)
        {
            float across = -ArenaRadius * .82f + ArenaRadius * 1.64f
                * (index + .5f) / lanes;
            Vector2 origin;
            float direction;
            if (vertical)
            {
                origin = ArenaCenter + new Vector2(across, reverse ? ArenaRadius * .92f : -ArenaRadius * .92f);
                direction = reverse ? -MathF.PI / 2f : MathF.PI / 2f;
            }
            else
            {
                origin = ArenaCenter + new Vector2(reverse ? ArenaRadius * .92f : -ArenaRadius * .92f, across);
                direction = reverse ? MathF.PI : 0;
            }
            if (index % 5 == 2)
                continue; // repeated breathing gaps travel with the wave.
            AddShot(sink, origin, direction + MathF.Sin(index * 1.7f) * .045f,
                speed * (.88f + index % 3 * .08f), .25f + index % 4 * .045f,
                index % 2 == 0 ? Light.Accent : Dark.Accent,
                owner, index % 3 == 0 ? "sine" : "linear", Simulation.TileSize * .3f, 12f);
        }
    }

    private void FireEdgePortals(List<EnemyProjectile> sink, bool vertical, string owner)
    {
        for (int index = -2; index <= 2; index++)
        {
            Vector2 origin = vertical
                ? ArenaCenter + new Vector2(index * ArenaRadius * .28f, -ArenaRadius * .86f)
                : ArenaCenter + new Vector2(-ArenaRadius * .86f, index * ArenaRadius * .28f);
            float direction = vertical ? MathF.PI / 2f : 0;
            FirePortalSeed(sink, origin, direction, .44f + (index + 2) * .035f, owner);
        }
    }

    private void FirePortalSeed(List<EnemyProjectile> sink, Vector2 origin,
        float direction, float speed, string owner)
    {
        float size = Simulation.TileSize * .92f;
        var portal = new EnemyProjectile(
            origin.X - size / 2f, origin.Y - size / 2f, direction, speed,
            Damage * .72f, size,
            travelRange: ArenaRadius * 2.1f,
            color: Rainbow((float)_visualTime * .08f + direction / MathF.Tau),
            shape: "orbit_core", path: "linear", lifetime: 11f,
            owner: $"aphantasia_portal_{owner}", ignoreWalls: true)
        {
            SplitCount = 8,
            SplitAt = Simulation.TileSize * 2.4f,
            SplitSpeedScale = .82f,
            SplitSpread = MathF.Tau,
            SplitRadial = true,
            SplitChildLifetime = 32f,
            ThreatReservationCost = 8,
            SplitTelegraphStartRatio = .72f,
            OriginTelegraphDuration = .68f,
        };
        sink.Add(portal);
    }

    private void AddShot(List<EnemyProjectile> sink, Vector2 origin, float direction,
        float speed, float sizeTiles, Color color, string owner, string path,
        float amplitude, float lifetime, bool deliberatelyShortRange = false)
    {
        float size = Simulation.TileSize * sizeTiles;
        float edgeRange = DistanceToArenaEdge(origin, direction) + size;
        float travelRange = deliberatelyShortRange
            ? Math.Min(ArenaRadius * .42f, edgeRange)
            : edgeRange;
        float requiredLifetime = travelRange
            / Math.Max(.01f, speed * .52f * (float)Simulation.ReferenceFps * .88f)
            + .75f;
        sink.Add(new EnemyProjectile(
            origin.X - size / 2f, origin.Y - size / 2f,
            direction, speed, Damage * .62f, size,
            travelRange: travelRange, color: color,
            shape: "diamond", path: path, amplitude: amplitude,
            lifetime: deliberatelyShortRange ? lifetime : Math.Max(lifetime, requiredLifetime),
            owner: $"aphantasia_{owner}", ignoreWalls: true));
    }

    private float DistanceToArenaEdge(Vector2 origin, float direction)
    {
        Vector2 offset = origin - ArenaCenter;
        Vector2 heading = new(MathF.Cos(direction), MathF.Sin(direction));
        float projection = Vector2.Dot(offset, heading);
        float discriminant = projection * projection
            - (offset.LengthSquared() - ArenaRadius * ArenaRadius);
        if (discriminant <= 0)
            return ArenaRadius * 2f;
        return Math.Max(Simulation.TileSize,
            -projection + MathF.Sqrt(discriminant));
    }

    private List<EnemyProjectile> BeginVolley()
    {
        _volleyScratch.Clear();
        return _volleyScratch;
    }

    private void CommitVolley(List<EnemyProjectile> sink)
    {
        int activeCost = 0;
        foreach (EnemyProjectile projectile in sink)
        {
            if (!projectile.RemFlag
                && projectile.Owner?.StartsWith("aphantasia_", StringComparison.Ordinal) == true)
            {
                activeCost += Math.Max(1, projectile.ThreatReservationCost);
            }
        }
        int stagedCost = 0;
        foreach (EnemyProjectile projectile in _volleyScratch)
            stagedCost += Math.Max(1, projectile.ThreatReservationCost);
        bool perimeterVolley = _volleyScratch.Count > 0
            && _volleyScratch.All(projectile =>
                projectile.Owner == "aphantasia_perimeter_drift");
        int volleyCap = perimeterVolley
            ? ActiveThreatSoftCap
            : ActiveThreatSoftCap - PerimeterThreatReserve;
        if (activeCost + stagedCost <= volleyCap)
            sink.AddRange(_volleyScratch);
        _volleyScratch.Clear();
    }

    private static float AngleTo(Vector2 from, Vector2 to) =>
        MathF.Atan2(to.Y - from.Y, to.X - from.X);

    private string DispositionText()
    {
        if (TrueLight)
            return "TRUE LIGHT";
        if (TrueDark)
            return "TRUE DARK";
        return $"LIGHT {Light.Disposition.ToString().ToUpperInvariant()} / "
            + $"DARK {Dark.Disposition.ToString().ToUpperInvariant()}";
    }

    private static IReadOnlyList<string> SequenceLabelsFor(AphantasiaSurvivalKind kind) =>
        kind switch
        {
            AphantasiaSurvivalKind.FirstEclipse => FirstEclipseStages,
            AphantasiaSurvivalKind.SecondEclipse => SecondEclipseStages,
            AphantasiaSurvivalKind.GrandChoice => GrandChoiceStages,
            AphantasiaSurvivalKind.EssenceFinale => EssenceFinaleStages,
            AphantasiaSurvivalKind.VoidFinale => VoidFinaleStages,
            _ => Array.Empty<string>(),
        };

    private IReadOnlyList<AphantasiaPattern> PatternPool() => Phase switch
    {
        1 => PhaseOnePatterns,
        2 => PhaseTwoPatterns,
        3 => PhaseThreePatterns,
        _ => PhaseFourPatterns,
    };

    private void StartNextSubphase(bool revivePair)
    {
        IReadOnlyList<AphantasiaPattern> pool = PatternPool();
        if (_patternBagPhase != Phase)
        {
            _patternBag.Clear();
            _patternBagPhase = Phase;
        }
        if (_patternBag.Count == 0)
            RefillPatternBag(pool);
        _patternIndex = _patternBag[0];
        _patternBag.RemoveAt(0);
        _subphaseRemaining = SubphaseDuration;
        _damageWindowRemaining = 0;
        _damageWindowOpened = false;
        _attackRemaining = SubphaseDeclarationDuration;
        _stateElapsed = 0;
        if (revivePair)
            ReviveMiniPair();
        if (Phase <= 2)
        {
            Light.Aggressive = _rng.Next(2) == 0;
            Dark.Aggressive = _rng.Next(2) == 0;
        }
    }

    private void RefillPatternBag(IReadOnlyList<AphantasiaPattern> pool)
    {
        _patternBag.Clear();
        int pathedWeight = pool.Count <= 3
            ? CompactPathedPatternWeight
            : ExpandedPathedPatternWeight;
        var weighted = new List<int>(PatternSelectionCycleCount(Phase));
        for (int index = 0; index < pool.Count; index++)
        {
            int weight = pool[index].Movement == AphantasiaMovementMode.Pathed
                ? pathedWeight : 1;
            for (int copy = 0; copy < weight; copy++)
                weighted.Add(index);
        }
        if (!TryBuildPatternOrder(weighted, _patternIndex, _patternBag))
            throw new InvalidOperationException("Unable to arrange Aphantasia's weighted pattern cycle.");
    }

    private bool TryBuildPatternOrder(List<int> remaining, int previous,
        List<int> result)
    {
        if (remaining.Count == 0)
            return true;
        List<int> candidates = remaining.Distinct()
            .Where(index => index != previous)
            .ToList();
        for (int index = candidates.Count - 1; index > 0; index--)
        {
            int swap = _rng.Next(index + 1);
            (candidates[index], candidates[swap]) = (candidates[swap], candidates[index]);
        }
        foreach (int candidate in candidates)
        {
            int removalIndex = remaining.IndexOf(candidate);
            remaining.RemoveAt(removalIndex);
            result.Add(candidate);
            if (TryBuildPatternOrder(remaining, candidate, result))
                return true;
            result.RemoveAt(result.Count - 1);
            remaining.Insert(removalIndex, candidate);
        }
        return false;
    }

    private void ReviveMiniPair()
    {
        Vector2 origin = BossCenter;
        foreach (AphantasiaMini mini in new[] { Light, Dark })
        {
            mini.PermanentlyDestroyed = false;
            mini.Empowered = false;
            mini.MaxHp = BaseMiniHealth;
            mini.Hp = mini.MaxHp;
            mini.Position = origin;
            mini.Velocity = Vector2.Zero;
            mini.FireCooldown = Math.Max(mini.FireCooldown, .65f);
        }
    }

    private bool MiniCanTakeDamage(AphantasiaMini mini)
    {
        if (!mini.Alive || Dying || EntranceRemaining > 0)
            return false;
        if (Phase <= 2)
            return EncounterState == AphantasiaEncounterState.Combat;
        if (Phase == 3 && SurvivalKind == AphantasiaSurvivalKind.GrandChoice)
            return Light.Alive && Dark.Alive;
        if (EncounterState == AphantasiaEncounterState.MiniExecution)
            return mini.Empowered;
        return false;
    }

    private HitResult DamageMini(AphantasiaMini mini, double amount)
    {
        if (!MiniCanTakeDamage(mini))
            return new HitResult(false, false, 0, true);
        int applied = Math.Min(mini.Hp, Math.Max(0, (int)Math.Round(amount)));
        mini.Hp -= applied;
        if (mini.Hp <= 0)
        {
            mini.Hp = 0;
            if (Phase == 3 && SurvivalKind == AphantasiaSurvivalKind.GrandChoice)
                mini.PermanentlyDestroyed = true;
            if (Phase <= 2 && !Light.Alive && !Dark.Alive)
            {
                _damageWindowOpened = true;
                _damageWindowRemaining = DamageWindowDuration;
            }
        }
        Light.Vulnerable = MiniCanTakeDamage(Light);
        Dark.Vulnerable = MiniCanTakeDamage(Dark);
        return new HitResult(true, false, applied, false);
    }

    public override HitResult TakeDamage(double amount, string partId = "body",
        DamageSource source = DamageSource.Direct)
    {
        if (partId is "light" or "mini:light")
            return DamageMini(Light, amount);
        if (partId is "dark" or "mini:dark")
            return DamageMini(Dark, amount);
        if (EncounterState != AphantasiaEncounterState.Combat
            || EntranceRemaining > 0 || Dying)
            return new HitResult(false, false, 0, true);
        if (Phase <= 2 && (Light.Alive || Dark.Alive || !DamageWindowActive))
            return new HitResult(false, false, 0, true);

        int requested = Math.Max(0, (int)Math.Round(amount));
        int floor = 0;
        Action? gate = null;
        if (Phase == 1 && !_firstSurvivalDone)
        {
            floor = (int)Math.Round(_barMaxHp * .75);
            gate = () =>
            {
                _firstSurvivalDone = true;
                BeginSurvival(AphantasiaSurvivalKind.FirstEclipse, EarlySurvivalDuration);
            };
        }
        else if (Phase == 1)
        {
            floor = (int)Math.Round(_barMaxHp * .5);
            gate = () => SetPhase(2);
        }
        else if (Phase == 2 && !_secondSurvivalDone)
        {
            floor = (int)Math.Round(_barMaxHp * .25);
            gate = () =>
            {
                _secondSurvivalDone = true;
                BeginSurvival(AphantasiaSurvivalKind.SecondEclipse, EarlySurvivalDuration);
            };
        }
        else if (Phase == 2)
        {
            floor = 1;
            gate = () => BeginTransformation(3);
        }
        else if (Phase == 3 && !_phaseThreeChoiceDone)
        {
            floor = (int)Math.Round(_barMaxHp * .5);
            gate = () => BeginSurvival(AphantasiaSurvivalKind.GrandChoice, PhaseThreeSurvivalDuration);
        }
        else if (Phase == 3)
        {
            floor = 1;
            gate = BeginMiniExecution;
        }
        else if (Phase == 4)
        {
            floor = 1;
            gate = () => BeginFinale(AphantasiaSurvivalKind.VoidFinale, PhaseFourFinaleDuration);
        }

        int before = Hp;
        Hp = Math.Max(floor, Hp - requested);
        int applied = before - Hp;
        if (Hp <= floor && gate is not null)
        {
            if (floor == 1)
                _displayZeroHealth = true;
            gate();
            TransitionCleanupRequested = true;
        }
        return new HitResult(applied > 0, false, applied, applied <= 0);
    }

    private void SetPhase(int phase)
    {
        Phase = Math.Clamp(phase, 1, 4);
        PhaseLabel = Phase switch
        {
            1 => "ESSENCE I",
            2 => "ESSENCE II",
            3 => "TESSERACT",
            _ => "CORE OF THE VOID",
        };
        PhaseAccent = Phase switch
        {
            1 => new Color(34, 75, 180),
            2 => new Color(29, 53, 132),
            3 => Rainbow(.64f),
            _ => new Color(20, 10, 35),
        };
        _patternIndex = -1;
        _patternBag.Clear();
        _patternBagPhase = Phase;
        _stateElapsed = 0;
        if (Phase == 2)
        {
            TransitionCleanupRequested = true;
            StartNextSubphase(revivePair: true);
        }
    }

    private void BeginSurvival(AphantasiaSurvivalKind kind, double duration)
    {
        EncounterState = AphantasiaEncounterState.Survival;
        SurvivalKind = kind;
        SurvivalDuration = duration;
        SurvivalRemaining = duration;
        _stateElapsed = 0;
        _attackRemaining = SequenceTransitionDuration;
        _sequenceStage = -1;
        _sequenceTransitionRemaining = 0;
        _damageWindowRemaining = 0;
        ReviveMiniPair();
        Light.Aggressive = true;
        Dark.Aggressive = true;
        Light.Vulnerable = false;
        Dark.Vulnerable = false;
    }

    private void BeginTransformation(int targetPhase)
    {
        EncounterState = AphantasiaEncounterState.Transforming;
        _transitionTargetPhase = targetPhase;
        _transitionRemaining = TesseractTransitionDuration;
        _stateElapsed = 0;
        _attackRemaining = 0;
        _displayZeroHealth = true;
        Light.PermanentlyDestroyed = targetPhase >= 4;
        Dark.PermanentlyDestroyed = targetPhase >= 4;
    }

    private void BeginMiniExecution()
    {
        EncounterState = AphantasiaEncounterState.MiniExecution;
        SurvivalKind = AphantasiaSurvivalKind.None;
        _stateElapsed = 0;
        _attackRemaining = .15;
        AphantasiaMini survivor = Light.Alive ? Light : Dark;
        survivor.Empowered = true;
        survivor.Aggressive = true;
        survivor.Hp = survivor.MaxHp;
    }

    private void BeginFinale(AphantasiaSurvivalKind kind, double duration)
    {
        EncounterState = AphantasiaEncounterState.Finale;
        SurvivalKind = kind;
        SurvivalDuration = duration;
        SurvivalRemaining = duration;
        _stateElapsed = 0;
        _attackRemaining = SequenceTransitionDuration;
        _sequenceStage = -1;
        _sequenceTransitionRemaining = 0;
        _displayZeroHealth = true;
        TransitionCleanupRequested = true;
    }

    private void BeginDeath()
    {
        EncounterState = AphantasiaEncounterState.Dying;
        SurvivalKind = AphantasiaSurvivalKind.None;
        _deathRemaining = 4.5;
        _stateElapsed = 0;
        _displayZeroHealth = true;
        Hp = 1;
        TransitionCleanupRequested = true;
    }

    public override bool IsDead() => _finalDeathReady && Hp <= 0;

    public void DebugSetPhase(int phase)
    {
        CaptureScaledHealth();
        int previousPhase = Phase;
        int previousHp = Hp;
        SetPhase(phase);
        EncounterState = AphantasiaEncounterState.Combat;
        EntranceRemaining = 0;
        SurvivalKind = AphantasiaSurvivalKind.None;
        SurvivalRemaining = 0;
        _displayZeroHealth = false;
        Hp = previousPhase <= 2 && Phase <= 2 ? previousHp : _barMaxHp;
        _firstSurvivalDone = Phase >= 2;
        _secondSurvivalDone = Phase >= 3;
        _phaseThreeChoiceDone = Phase >= 4;
        if (Phase >= 3)
        {
            ReviveMiniPair();
        }
        if (Phase == 4)
        {
            _phaseThreeChoiceDone = true;
            Light.PermanentlyDestroyed = true;
            Dark.PermanentlyDestroyed = true;
        }
        if (Phase != 2) // SetPhase starts Phase 2 immediately for the shared-bar handoff.
            StartNextSubphase(revivePair: Phase < 4);
    }

    public void DebugAdvanceSubPhase()
    {
        bool revivePair = !Light.Alive && !Dark.Alive;
        StartNextSubphase(revivePair);
        Light.Vulnerable = MiniCanTakeDamage(Light);
        Dark.Vulnerable = MiniCanTakeDamage(Dark);
    }

    public void DebugSetMiniState(AphantasiaMiniKind kind, int hp,
        AphantasiaMiniDisposition disposition)
    {
        AphantasiaMini mini = kind == AphantasiaMiniKind.Light ? Light : Dark;
        mini.Hp = Math.Clamp(hp, 0, mini.MaxHp);
        mini.PermanentlyDestroyed = disposition == AphantasiaMiniDisposition.Destroyed;
        mini.Empowered = disposition == AphantasiaMiniDisposition.Empowered;
        mini.Aggressive = disposition is AphantasiaMiniDisposition.Aggressive
            or AphantasiaMiniDisposition.Empowered;
        if (mini.PermanentlyDestroyed)
            mini.Hp = 0;
        mini.Vulnerable = MiniCanTakeDamage(mini);
    }

    public void DebugStartSurvival()
    {
        if (Phase == 1)
            BeginSurvival(AphantasiaSurvivalKind.FirstEclipse, EarlySurvivalDuration);
        else if (Phase == 2)
            BeginSurvival(AphantasiaSurvivalKind.SecondEclipse, EarlySurvivalDuration);
        else if (Phase == 3)
            BeginSurvival(AphantasiaSurvivalKind.GrandChoice, PhaseThreeSurvivalDuration);
        else
            BeginFinale(AphantasiaSurvivalKind.VoidFinale, PhaseFourFinaleDuration);
    }

    public void DebugStartFinale()
    {
        BeginFinale(Phase == 4
            ? AphantasiaSurvivalKind.VoidFinale
            : AphantasiaSurvivalKind.EssenceFinale,
            Phase == 4 ? PhaseFourFinaleDuration : PhaseThreeSurvivalDuration);
    }

    public Vector2 ConstrainPlayer(Vector2 playerTopLeft, float playerSize)
    {
        Vector2 center = playerTopLeft + new Vector2(playerSize / 2f);
        Vector2 delta = center - ArenaCenter;
        float limit = ArenaRadius - playerSize * .72f;
        if (delta.LengthSquared() <= limit * limit)
            return playerTopLeft;
        if (delta.LengthSquared() < .0001f)
            delta = Vector2.UnitY;
        center = ArenaCenter + Vector2.Normalize(delta) * limit;
        return center - new Vector2(playerSize / 2f);
    }

    private float MiniSize => Simulation.TileSize * (Phase == 3 ? 1.92f : 1.62f);

    public override IReadOnlyList<(string Part, Rectangle Rect)> GetWorldHitboxes()
    {
        float miniSize = MiniSize;
        _worldHitboxes[0] = ("light", Light.Alive
            ? CenteredRect(Light.Position, miniSize) : Rectangle.Empty);
        _worldHitboxes[1] = ("dark", Dark.Alive
            ? CenteredRect(Dark.Position, miniSize) : Rectangle.Empty);
        _worldHitboxes[2] = ("body", WorldRect());
        return _worldHitboxes;
    }

    public override IReadOnlyList<(string Part, Rectangle Rect)> GetScreenHitboxes(
        Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        float miniSize = MiniSize;
        Vector2 light = camera.WorldToScreen(Light.Position, playerWorldPosition, screenShake);
        Vector2 dark = camera.WorldToScreen(Dark.Position, playerWorldPosition, screenShake);
        Vector2 body = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        _screenHitboxes[0] = ("light", Light.Alive
            ? CenteredRect(light, miniSize) : Rectangle.Empty);
        _screenHitboxes[1] = ("dark", Dark.Alive
            ? CenteredRect(dark, miniSize) : Rectangle.Empty);
        _screenHitboxes[2] = ("body", new Rectangle((int)body.X, (int)body.Y, (int)Size, (int)Size));
        return _screenHitboxes;
    }

    private static Rectangle CenteredRect(Vector2 center, float size) =>
        new((int)(center.X - size / 2f), (int)(center.Y - size / 2f),
            Math.Max(1, (int)size), Math.Max(1, (int)size));

    public override void Draw(SpriteBatch spriteBatch, Camera camera,
        Vector2 playerWorldPosition, Vector2 screenShake)
    {
        Vector2 center = camera.WorldToScreen(
            new Vector2(WorldX + Size / 2f, WorldY + Size / 2f),
            playerWorldPosition, screenShake);
        DrawBossBody(spriteBatch, center);
        if (DamageWindowActive)
        {
            float pulse = .5f + .5f * MathF.Sin((float)_visualTime * 10f);
            Primitives2D.CircleOutline(spriteBatch, center,
                Size * (.66f + pulse * .1f), UiTheme.Ink, 9);
            Primitives2D.CircleOutline(spriteBatch, center,
                Size * (.66f + pulse * .1f), UiTheme.Cream, 4);
        }
        if (Phase < 4)
        {
            DrawMini(spriteBatch, camera, playerWorldPosition, screenShake, Light);
            DrawMini(spriteBatch, camera, playerWorldPosition, screenShake, Dark);
        }
        if (Dying)
            DrawDeath(spriteBatch, center);
    }

    private void DrawBossBody(SpriteBatch spriteBatch, Vector2 center)
    {
        float pulse = 1f + MathF.Sin((float)_visualTime * 2.1f) * .05f;
        if (Phase <= 2)
        {
            float spin = (float)_visualTime * (Phase == 1 ? .82f : .38f);
            Vector2[] cube = ProjectCube(center, Size * .42f * pulse, spin, spin * .63f);
            DrawOrbitingCubes(spriteBatch, center, spin, foreground: false);
            DrawFilledCube(spriteBatch, cube, new Color(3, 14, 58), PhaseAccent);
            DrawOrbitingCubes(spriteBatch, center, spin, foreground: true);
        }
        else if (Phase == 3 || EncounterState == AphantasiaEncounterState.Transforming)
        {
            float spin = (float)_visualTime * .31f;
            Vector2[] outer = ProjectCube(center, Size * .62f * pulse, spin, spin * .71f);
            DrawWireCube(spriteBatch, outer, rainbow: true, fill: new Color(1, 1, 5) * .92f);
            Vector2[] inner = ProjectCube(center, Size * .3f, -spin * .72f, spin * .43f);
            DrawFilledCube(spriteBatch, inner, Rainbow(spin * .08f) * .82f, UiTheme.Cream);
        }
        else
        {
            float spin = (float)_visualTime * .46f;
            DrawSatelliteCube(spriteBatch, center, Size * .34f, Rainbow(spin * .08f));
            for (int index = 0; index < 6; index++)
            {
                float angle = spin * (index % 2 == 0 ? 1f : -.72f) + index * MathF.Tau / 6f;
                float radius = Size * (.55f + .16f * MathF.Sin(spin * 1.7f + index));
                Vector2 pane = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
                float half = Size * (.16f + index % 2 * .04f);
                Vector2 right = new(MathF.Cos(angle + spin), MathF.Sin(angle + spin));
                Vector2 down = new(-right.Y, right.X);
                Color edge = Rainbow(index / 6f + spin * .05f);
                Primitives2D.FillQuad(spriteBatch,
                    pane - right * half - down * half * .55f,
                    pane + right * half - down * half * .55f,
                    pane + right * half + down * half * .55f,
                    pane - right * half + down * half * .55f,
                    new Color(2, 1, 7) * .9f);
                DrawQuadOutline(spriteBatch, pane, right, down, half, half * .55f, edge, 3);
            }
        }
    }

    private void DrawOrbitingCubes(SpriteBatch spriteBatch, Vector2 center,
        float spin, bool foreground)
    {
        const int satellites = 6;
        for (int index = 0; index < satellites; index++)
        {
            float angle = spin * (Phase == 1 ? 1f : .72f)
                + index * MathF.Tau / satellites;
            bool isForeground = MathF.Sin(angle) >= 0;
            if (isForeground != foreground)
                continue;
            float erratic = Phase == 2
                ? MathF.Sin((float)_visualTime * 1.9f + index * 2.3f) * Size * .18f
                : 0;
            Vector2 at = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle) * .34f)
                * (Size * .78f + erratic);
            DrawSatelliteCube(spriteBatch, at, Size * .1f,
                Rainbow(index / (float)satellites + spin * .04f));
        }
    }

    private void DrawMini(SpriteBatch spriteBatch, Camera camera,
        Vector2 playerWorldPosition, Vector2 screenShake, AphantasiaMini mini)
    {
        if (!mini.Alive)
            return;
        Vector2 center = camera.WorldToScreen(mini.Position, playerWorldPosition, screenShake);
        float radius = MiniSize * .45f * (mini.Empowered ? 1.12f : 1f);
        float spin = (float)_visualTime * (mini.Aggressive ? 1.8f : .72f)
            * (ReferenceEquals(mini, Light) ? 1 : -1);
        Vector2 up = new(MathF.Cos(spin - MathF.PI / 2f), MathF.Sin(spin - MathF.PI / 2f));
        Vector2 right = new(-up.Y, up.X);
        Primitives2D.FillQuad(spriteBatch,
            center + up * radius, center + right * radius,
            center - up * radius, center - right * radius, UiTheme.Ink);
        Primitives2D.Polyline(spriteBatch,
            [center + up * radius, center + right * radius, center - up * radius, center - right * radius],
            true, mini.Accent, mini.Empowered ? 6 : 3);
        Primitives2D.FillCircle(spriteBatch, center, radius * .2f,
            mini.Empowered ? UiTheme.Cream : mini.Accent);

        float glyphRadius = radius * 1.28f;
        if (!mini.Vulnerable)
        {
            Primitives2D.CircleOutline(spriteBatch, center, glyphRadius, UiTheme.Ink, 7);
            Primitives2D.CircleOutline(spriteBatch, center, glyphRadius, mini.Accent * .78f, 3);
        }
        else
        {
            Primitives2D.CircleOutline(spriteBatch, center, glyphRadius,
                UiTheme.Cream * (.48f + .2f * MathF.Sin((float)_visualTime * 5f)), 2);
        }

        if (mini.Aggressive)
        {
            for (int index = 0; index < 4; index++)
            {
                float angle = spin + index * MathF.PI / 2f;
                Vector2 direction = new(MathF.Cos(angle), MathF.Sin(angle));
                Primitives2D.Line(spriteBatch,
                    center + direction * glyphRadius * 1.04f,
                    center + direction * glyphRadius * 1.27f,
                    mini.Empowered ? UiTheme.Cream : mini.Accent,
                    mini.Empowered ? 4 : 2);
            }
        }
        else
        {
            Primitives2D.CircleOutline(spriteBatch, center, glyphRadius * 1.14f,
                mini.Accent * .52f, 2);
        }

        if (mini.Empowered)
        {
            float empoweredPulse = .5f + .5f * MathF.Sin((float)_visualTime * 7f);
            Primitives2D.CircleOutline(spriteBatch, center,
                glyphRadius * (1.28f + empoweredPulse * .12f),
                Rainbow((float)_visualTime * .1f), 4);
        }

        if (mini.FireCooldown <= .18f)
        {
            float warning = 1f - Math.Clamp(mini.FireCooldown / .18f, 0f, 1f);
            Primitives2D.CircleOutline(spriteBatch, center,
                glyphRadius * (1.58f - warning * .3f),
                UiTheme.Cream * (.35f + warning * .65f), 3);
        }
        var bar = new Rectangle((int)(center.X - radius), (int)(center.Y - radius - 12),
            Math.Max(8, (int)(radius * 2)), 5);
        UiTheme.DrawProgress(spriteBatch, bar, mini.HealthRatio, mini.Accent, 8);
    }

    private static Vector2[] ProjectCube(Vector2 center, float extent, float yaw, float pitch)
    {
        var result = new Vector2[8];
        for (int index = 0; index < 8; index++)
        {
            float x = (index & 1) == 0 ? -1 : 1;
            float y = (index & 2) == 0 ? -1 : 1;
            float z = (index & 4) == 0 ? -1 : 1;
            float cy = MathF.Cos(yaw), sy = MathF.Sin(yaw);
            float rx = x * cy + z * sy;
            float rz = -x * sy + z * cy;
            float cp = MathF.Cos(pitch), sp = MathF.Sin(pitch);
            float ry = y * cp - rz * sp;
            rz = y * sp + rz * cp;
            float perspective = 1f + rz * .12f;
            result[index] = center + new Vector2(rx, ry) * extent * perspective;
        }
        return result;
    }

    private static void DrawFilledCube(SpriteBatch spriteBatch, Vector2[] points,
        Color fill, Color edge)
    {
        foreach (int[] face in CubeFaces)
            Primitives2D.FillQuad(spriteBatch, points[face[0]], points[face[1]],
                points[face[2]], points[face[3]], fill * .72f);
        foreach (int[] pair in CubeEdges)
        {
            Primitives2D.Line(spriteBatch, points[pair[0]], points[pair[1]], UiTheme.Ink, 7);
            Primitives2D.Line(spriteBatch, points[pair[0]], points[pair[1]], edge, 3);
        }
    }

    private static void DrawWireCube(SpriteBatch spriteBatch, Vector2[] points,
        bool rainbow, Color fill)
    {
        foreach (int[] face in CubeFaces)
            Primitives2D.FillQuad(spriteBatch, points[face[0]], points[face[1]],
                points[face[2]], points[face[3]], fill * .22f);
        for (int index = 0; index < CubeEdges.Length; index++)
        {
            int[] edge = CubeEdges[index];
            Color color = rainbow ? Rainbow(index / (float)CubeEdges.Length) : UiTheme.Purple;
            Primitives2D.Line(spriteBatch, points[edge[0]], points[edge[1]], UiTheme.Ink, 8);
            Primitives2D.Line(spriteBatch, points[edge[0]], points[edge[1]], color, 3);
        }
    }

    private static void DrawSatelliteCube(SpriteBatch spriteBatch, Vector2 center,
        float extent, Color color)
    {
        var rect = CenteredRect(center, extent * 2);
        Primitives2D.FillRect(spriteBatch, rect, UiTheme.Ink);
        var inner = rect;
        inner.Inflate(-3, -3);
        Primitives2D.FillRect(spriteBatch, inner, color);
        Primitives2D.RectOutline(spriteBatch, rect, UiTheme.Cream, 1);
    }

    private static void DrawQuadOutline(SpriteBatch spriteBatch, Vector2 center,
        Vector2 right, Vector2 down, float halfWidth, float halfHeight,
        Color color, int width)
    {
        Vector2[] points =
        [
            center - right * halfWidth - down * halfHeight,
            center + right * halfWidth - down * halfHeight,
            center + right * halfWidth + down * halfHeight,
            center - right * halfWidth + down * halfHeight,
        ];
        Primitives2D.Polyline(spriteBatch, points, true, color, width);
    }

    private void DrawDeath(SpriteBatch spriteBatch, Vector2 center)
    {
        float progress = 1f - (float)(_deathRemaining / 4.5);
        for (int index = 0; index < 12; index++)
        {
            float angle = index * MathF.Tau / 12f + progress * 3.2f;
            Vector2 end = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle))
                * ArenaRadius * (.18f + progress * .75f);
            Primitives2D.Line(spriteBatch, center, end, UiTheme.Ink, 10);
            Primitives2D.Line(spriteBatch, center, end, Rainbow(index / 12f + progress), 3);
        }
        for (int ring = 0; ring < 6; ring++)
            Primitives2D.CircleOutline(spriteBatch, center,
                Size * (.5f + ((progress * 4 + ring / 6f) % 1f) * 4f),
                Rainbow(ring / 6f + progress), 3);
    }

    public void DrawPersistentArena(SpriteBatch spriteBatch, Camera camera,
        Vector2 playerWorldPosition, Vector2 screenShake, Rectangle logicalViewport)
    {
        Vector2 center = camera.WorldToScreen(ArenaCenter, playerWorldPosition, screenShake);
        for (int index = 0; index < _arenaMask.Length; index++)
        {
            float angle = index * MathF.Tau / _arenaMask.Length;
            _arenaMask[index] = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle))
                * (ArenaRadius + 8);
        }
        Primitives2D.DrawOutsideArena(spriteBatch, _arenaMask, logicalViewport);

        Color accent = TrueLight ? new Color(88, 125, 228)
            : TrueDark ? new Color(8, 18, 65)
            : Phase == 4 ? Rainbow((float)_visualTime * .04f) : PhaseAccent;
        const int segments = 96;
        Vector2 previous = default;
        for (int index = 0; index <= segments; index++)
        {
            float angle = index * MathF.Tau / segments;
            float ocean = MathF.Sin(angle * 7f + (float)_visualTime * .42f) * 8f
                + MathF.Sin(angle * 13f - (float)_visualTime * .21f) * 4f;
            Vector2 point = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle))
                * (ArenaRadius + ocean);
            if (index > 0)
            {
                Primitives2D.Line(spriteBatch, previous, point, UiTheme.Ink, 16);
                Primitives2D.Line(spriteBatch, previous, point, accent, Phase == 4 ? 5 : 4);
            }
            previous = point;
        }
        Primitives2D.CircleOutline(spriteBatch, center,
            ArenaRadius + 18f + MathF.Sin((float)_visualTime * .35f) * 6f,
            accent * .42f, 3);
    }

    private static Color Rainbow(float phase)
    {
        phase -= MathF.Floor(phase);
        float r = .5f + .5f * MathF.Sin((phase + 0f) * MathF.Tau);
        float g = .5f + .5f * MathF.Sin((phase + 1f / 3f) * MathF.Tau);
        float b = .5f + .5f * MathF.Sin((phase + 2f / 3f) * MathF.Tau);
        return new Color(r, g, b);
    }
}
