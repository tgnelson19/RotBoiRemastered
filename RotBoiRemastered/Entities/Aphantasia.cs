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
    VoidEclipse,
    VoidFinale,
}

[Flags]
public enum AphantasiaSpecialAttack
{
    None = 0,
    DoubleHelix = 1,
    Laser = 2,
    Bomb = 4,
}

public sealed record AphantasiaPattern(
    string Key,
    string Label,
    AphantasiaMovementMode Movement,
    AphantasiaSpecialAttack SpecialAttack);

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
    public const int BaseBarHealth = 1_038_000;
    public const int BaseMiniHealth = 54_690;
    public const int EmpoweredMiniHealth = 175_515;
    public const float MiniPathedRadiusRatio = .76f;
    public const double SubphaseDuration = 40.0;
    public const double SubphaseDeclarationDuration = .9;
    public const double SequenceTransitionDuration = .55;
    public const double DamageWindowDuration = 5.0;
    public const double EarlySurvivalDuration = 20.0;
    public const double PhaseThreeSurvivalDuration = 30.0;
    public const double PhaseFourSurvivalDuration = 30.0;
    public const double PhaseFourFinaleDuration = 45.0;
    /// <summary>
    /// A single hit that deals at least this fraction of the active bar's
    /// max HP -- 1/2 for the phase 1-2 shared bar, 1/4 for phases 3 and 4 --
    /// shields the boss and holds the fight open for
    /// <see cref="DamageCapInvincibilityDuration"/> seconds before the gate
    /// it would have triggered actually fires.
    /// </summary>
    public const double DamageCapSharedPhaseFraction = .5;
    public const double DamageCapSoloPhaseFraction = .25;
    public const double DamageCapInvincibilityDuration = 10.0;
    public const double VoidVortexGrowDuration = 6.0;
    public const double TesseractTransitionDuration = 5.0;
    public const int ProjectileCapacityMultiplier = 5;
    public const int ActiveThreatSoftCap = 320 * ProjectileCapacityMultiplier;
    public const int PerimeterThreatReserve = 24;
    public const double PerimeterPressureCadence = 1.8;
    public const int PerimeterPressureCount = 8;
    public const float MinimumProjectileSizeTiles = .25f;
    public const double HelixFireCadence = .68;
    public const double PhaseHandoffDuration = 7.0;
    public const double CombatPhraseDuration = 6.0;
    public const double CombatPhraseBreathDuration = .8;
    public const int ArenaWallPanels = 28;
    public const float ArenaWallHeight = 30f;

    public static readonly IReadOnlyList<AphantasiaPattern> PhaseOnePatterns =
    [
        new("ordered_bloom", "ORDERED BLOOM", AphantasiaMovementMode.Standing, AphantasiaSpecialAttack.DoubleHelix),
        new("horizon_ellipse", "HORIZON ELLIPSE", AphantasiaMovementMode.Pathed, AphantasiaSpecialAttack.Laser),
        new("tidal_pursuit", "TIDAL PURSUIT", AphantasiaMovementMode.Chase, AphantasiaSpecialAttack.Bomb),
    ];

    public static readonly IReadOnlyList<AphantasiaPattern> PhaseTwoPatterns =
    [
        new("broken_bloom", "BROKEN BLOOM", AphantasiaMovementMode.Standing, AphantasiaSpecialAttack.Bomb),
        new("erratic_eight", "ERRATIC EIGHT", AphantasiaMovementMode.Pathed, AphantasiaSpecialAttack.DoubleHelix),
        new("undertow", "UNDERTOW", AphantasiaMovementMode.Chase, AphantasiaSpecialAttack.Laser),
    ];

    public static readonly IReadOnlyList<AphantasiaPattern> PhaseThreePatterns =
    [
        new("prism_bloom", "PRISM BLOOM", AphantasiaMovementMode.Standing, AphantasiaSpecialAttack.DoubleHelix),
        new("lattice_curtain", "LATTICE CURTAIN", AphantasiaMovementMode.Standing, AphantasiaSpecialAttack.Laser),
        new("tesseract_eight", "TESSERACT EIGHT", AphantasiaMovementMode.Pathed, AphantasiaSpecialAttack.Bomb),
        new("folding_perimeter", "FOLDING PERIMETER", AphantasiaMovementMode.Pathed, AphantasiaSpecialAttack.Laser),
        new("ribbon_pursuit", "RIBBON PURSUIT", AphantasiaMovementMode.Chase, AphantasiaSpecialAttack.Bomb),
        new("satellite_spiral", "SATELLITE SPIRAL", AphantasiaMovementMode.Chase, AphantasiaSpecialAttack.DoubleHelix),
    ];

    public static readonly IReadOnlyList<AphantasiaPattern> PhaseFourPatterns =
    [
        new("portal_constellation", "PORTAL CONSTELLATION", AphantasiaMovementMode.Standing, AphantasiaSpecialAttack.Bomb),
        new("void_clock", "NESTED VOID CLOCK", AphantasiaMovementMode.Standing, AphantasiaSpecialAttack.Laser),
        new("pane_procession", "DRIFTING PANE PROCESSION", AphantasiaMovementMode.Pathed, AphantasiaSpecialAttack.DoubleHelix),
        new("portal_lattice", "FOLDING PORTAL LATTICE", AphantasiaMovementMode.Pathed, AphantasiaSpecialAttack.Laser),
        new("portal_wake", "PORTAL WAKE PURSUIT", AphantasiaMovementMode.Chase, AphantasiaSpecialAttack.Bomb),
        new("tesseract_hunt", "COLLAPSING TESSERACT HUNT", AphantasiaMovementMode.Chase, AphantasiaSpecialAttack.DoubleHelix),
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
    private static readonly IReadOnlyList<string> VoidEclipseStages =
        ["PORTAL EQUINOX", "DRIFTING LATTICE", "FRACTURED CONSTELLATION", "VOID CONVERGENCE"];
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

    /// <summary>Cube-local outward normal for each entry in <see cref="CubeFaces"/>, same order.</summary>
    private static readonly Vector3[] CubeFaceNormals =
    [
        new(0, 0, -1), new(0, 0, 1), new(0, -1, 0),
        new(0, 1, 0), new(-1, 0, 0), new(1, 0, 0),
    ];

    /// <summary>Fixed key light used to shade cube faces -- upper-left and slightly toward camera.</summary>
    private static readonly Vector3 CubeLightDirection = Vector3.Normalize(new Vector3(-.35f, -.55f, .75f));

    private readonly Random _rng;
    private readonly List<int> _patternBag = [];
    private readonly List<EnemyProjectile> _volleyScratch = new(64);
    private readonly (string Part, Rectangle Rect)[] _worldHitboxes = new (string, Rectangle)[3];
    private readonly (string Part, Rectangle Rect)[] _screenHitboxes = new (string, Rectangle)[3];
    private readonly Vector2[] _arenaMask = new Vector2[96];
    private readonly Vector2[] _arenaWallGround = new Vector2[ArenaWallPanels + 1];
    private readonly Vector2[] _arenaWallCap = new Vector2[ArenaWallPanels + 1];
    private readonly BiomePalette _wallPalette;
    private int _patternIndex = -1;
    private double _subphaseRemaining;
    private double _damageWindowRemaining;
    private bool _damageWindowOpened;
    private double _attackRemaining;
    private double _perimeterPressureRemaining = .8;
    private double _stateElapsed;
    private float _facingYaw;
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
    private bool _phaseFourSurvivalDone;
    private double _burstShieldRemaining;
    private Action? _pendingGate;
    private bool _pendingGateFloorOne;
    private bool _healthScaleCaptured;
    private int _barMaxHp;
    private bool _displayZeroHealth;
    private bool _finalDeathReady;
    private int _regularVolleyCount;
    private double _helixFireRemaining = .2;
    private readonly double[] _halfPressureRemaining = [.35, .78];
    private int _halfVolleySerial;
    private double _subphaseCombatElapsed;
    private bool _phraseWasBreathing;
    private double _phaseHandoffRemaining;
    private int _survivalGridVolleyCount;
    private bool _voidVortexActive;
    private float _voidVortexProgress;

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
    public bool PresentationSurvivalActive => !PhaseHandoffActive
        && EncounterState is AphantasiaEncounterState.Survival
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
        && EntranceRemaining <= 0 && !DamageCapShieldActive
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
    public bool DamageCapShieldActive => _burstShieldRemaining > 0;
    public double DamageCapShieldRemaining => _burstShieldRemaining;
    public string ObjectiveText => EncounterState switch
    {
        AphantasiaEncounterState.Entrance => "THE VOID AWAKENS",
        AphantasiaEncounterState.Transforming => "TRANSFORMING",
        AphantasiaEncounterState.Dying => "THE VOID COLLAPSES",
        AphantasiaEncounterState.MiniExecution =>
            $"DESTROY {(Light.Alive ? Light.Name : Dark.Name)}",
        _ when DamageCapShieldActive => $"CORE SHIELDED // {DamageCapShieldRemaining:0.0}s",
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
    public float SurvivalTimerProgress => PresentationSurvivalActive && SurvivalDuration > 0
        ? Math.Clamp(1f - (float)(SurvivalRemaining / SurvivalDuration), 0f, 1f)
        : 0f;
    public bool PhaseHandoffActive => _phaseHandoffRemaining > 0;
    private bool CombatPhraseBreathing => EncounterState == AphantasiaEncounterState.Combat
        && _subphaseCombatElapsed >= SubphaseDeclarationDuration
        && (_subphaseCombatElapsed - SubphaseDeclarationDuration) % CombatPhraseDuration
            >= CombatPhraseDuration - CombatPhraseBreathDuration;
    private bool CombatDeclarationActive => EncounterState == AphantasiaEncounterState.Combat
        && _subphaseCombatElapsed < SubphaseDeclarationDuration;
    private bool CombatFiringPaused => CombatDeclarationActive || CombatPhraseBreathing;
    public double PhaseHandoffRemaining => _phaseHandoffRemaining;
    public float PhaseHandoffProgress => PhaseHandoffActive
        ? 1f - (float)(_phaseHandoffRemaining / PhaseHandoffDuration)
        : 1f;
    public float DisplayedHp => _displayZeroHealth ? 0 : Math.Max(0, Hp);
    public float DisplayedMaxHp => Math.Max(1, _barMaxHp > 0 ? _barMaxHp : MaxHp);
    public float ArenaLightIntensity => TrueLight ? 1.2f : TrueDark ? .86f : Phase == 4 ? .95f : 1f;
    public float ArenaDarknessScale => TrueLight ? .56f : TrueDark ? .78f : Phase == 4 ? .82f : .7f;
    public float ArenaPlayerLightScale => TrueLight ? 1.38f : TrueDark ? 1.28f : Phase == 4 ? 1.22f : 1.25f;

    public static int PatternCountForPhase(int phase) => PatternsForPhase(phase).Count;

    /// <summary>
    /// One full shuffle cycle draws every subphase in the phase's pool
    /// exactly once, so this is just the pool size -- kept as its own
    /// method since callers treat it as "how many draws until the set is
    /// guaranteed exhausted" rather than reaching for <c>.Count</c> directly.
    /// </summary>
    public static int PatternSelectionCycleCount(int phase) => phase switch
    {
        1 => PhaseOnePatterns.Count,
        2 => PhaseTwoPatterns.Count,
        3 => PhaseThreePatterns.Count,
        4 => PhaseFourPatterns.Count,
        _ => throw new ArgumentOutOfRangeException(nameof(phase)),
    };

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
        int wallBiome = battleground.BiomeForTile(
            (int)(ArenaCenter.X / Simulation.TileSize),
            (int)(ArenaCenter.Y / Simulation.TileSize));
        _wallPalette = battleground.Palettes[
            Math.Clamp(wallBiome, 0, battleground.Palettes.Count - 1)];
        CapturedNoHealing = noHealing;
        CapturedNoExtract = noExtract;
        PhaseFourEligible = noHealing && noExtract;
        ContentPath = "phantasia";
        Family = "aphantasia";
        Light = NewMini("THE LIGHT", new Color(245, 228, 136));
        Dark = NewMini("THE DARK", new Color(43, 69, 166));
        CenterBody();
        StartNextSubphase(revivePair: true, beginHandoff: false);
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

        // Grows independently of encounter state so the reveal keeps
        // advancing through the finale's phase handoff and the death
        // collapse instead of freezing whenever those pause other timers.
        if (_voidVortexActive && _voidVortexProgress < 1f)
        {
            _voidVortexProgress = Math.Clamp(
                _voidVortexProgress + (float)(dt / VoidVortexGrowDuration), 0f, 1f);
        }

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

        if (PhaseHandoffActive)
        {
            UpdatePhaseHandoff(dt);
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
            UpdateArenaHalfPressure(context, dt);
            UpdateHelixStream(context, dt);
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
        if (_burstShieldRemaining > 0)
        {
            _burstShieldRemaining = Math.Max(0, _burstShieldRemaining - dt);
            if (_burstShieldRemaining <= 0 && _pendingGate is not null)
            {
                Action pending = _pendingGate;
                _pendingGate = null;
                if (_pendingGateFloorOne)
                    _displayZeroHealth = true;
                pending();
                BeginPhaseHandoff();
                return;
            }
        }
        _subphaseCombatElapsed += dt;
        _subphaseRemaining -= dt;
        if (_damageWindowOpened)
            _damageWindowRemaining = Math.Max(0, _damageWindowRemaining - dt);
        UpdateMovement(context, dt);
        bool declarationActive = CombatDeclarationActive;
        bool breathing = CombatPhraseBreathing;
        UpdateMinis(context, dt, hazardsOnly: false,
            allowFire: !declarationActive && !breathing);

        if (Phase <= 2 && !Light.Alive && !Dark.Alive && !_damageWindowOpened)
        {
            _damageWindowOpened = true;
            _damageWindowRemaining = DamageWindowDuration;
        }

        if (!breathing)
            _attackRemaining -= dt;
        if (breathing)
        {
            _phraseWasBreathing = true;
        }
        else if (!declarationActive)
        {
            if (_phraseWasBreathing)
            {
                _phraseWasBreathing = false;
                FireCurrentPattern(context, phraseAccent: true);
            }
            else if (_attackRemaining <= 0)
            {
                FireCurrentPattern(context);
            }
        }

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
        StartNextSubphase(revivePair: Phase == 3, beginHandoff: false);
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
        else if (completed != AphantasiaSurvivalKind.VoidEclipse)
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
        {
            BeginFinale(AphantasiaSurvivalKind.EssenceFinale, PhaseThreeSurvivalDuration);
            BeginPhaseHandoff();
        }
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
        BeginPhaseHandoff();
    }

    private bool PrepareSequenceStage(int desiredStage, double dt)
    {
        desiredStage = Math.Max(0, desiredStage);
        if (_sequenceStage != desiredStage)
        {
            _sequenceStage = desiredStage;
            _survivalMovement = desiredStage;
            _regularVolleyCount = 0;
            _helixFireRemaining = .2;
            _sequenceTransitionRemaining = SequenceTransitionDuration;
            _attackRemaining = SequenceTransitionDuration;
            _perimeterPressureRemaining = 0;
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

        // Turn the body to face where it's headed -- straight at the player
        // while chasing, along its current path while pathing -- smoothed
        // toward the shortest way round rather than snapping. Standing
        // patterns, and every non-combat state, leave this alone and keep
        // the plain idle spin DrawBossBody already had.
        if (mode is AphantasiaMovementMode.Chase or AphantasiaMovementMode.Pathed)
        {
            Vector2 aim = mode == AphantasiaMovementMode.Chase
                ? new Vector2(context.PlayerWorldX, context.PlayerWorldY) - current
                : target - current;
            if (aim.LengthSquared() > 1f)
            {
                float desiredYaw = MathF.Atan2(aim.Y, aim.X);
                float turnDelta = MathF.IEEERemainder(desiredYaw - _facingYaw, MathF.Tau);
                float turnBlend = 1f - MathF.Exp(-3.2f * (float)dt);
                _facingYaw += turnDelta * turnBlend;
            }
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
            string path = index % 2 == (ReferenceEquals(mini, Dark) ? 0 : 1)
                ? "sine" : "linear";
            AddShot(staged, mini.Position, aim - spread / 2f + fraction * spread,
                speed, mini.Empowered ? .34f : .26f, mini.Accent,
                $"mini_{(ReferenceEquals(mini, Light) ? "light" : "dark")}",
                path, path == "sine" ? Simulation.TileSize * .52f : 0f, 8f,
                shape: "diamond");
        }
        CommitVolley(sink);
    }

    private void FireCurrentPattern(EnemyUpdateContext context, bool phraseAccent = false)
    {
        List<EnemyProjectile> staged = BeginVolley();
        Vector2 center = BossCenter;
        Vector2 player = new(context.PlayerWorldX, context.PlayerWorldY);
        if (Phase <= 2)
            FireEssencePattern(context, staged);
        else if (Phase == 3)
            FireTesseractPattern(context, staged);
        else
            FireVoidPattern(context, staged);

        _regularVolleyCount++;
        FireBaselineBossAttack(staged, center, player, _regularVolleyCount);
        AddBossSpecialAttack(staged, center, player, _regularVolleyCount);
        if (phraseAccent)
            FirePhraseAccent(staged, center, player);
        CommitVolley(context.ProjectileSink);
    }

    private void FireBaselineBossAttack(List<EnemyProjectile> sink, Vector2 origin,
        Vector2 player, int volley)
    {
        float aim = AngleTo(origin, player);
        switch (volley % 3)
        {
            case 0:
                AddShot(sink, origin, aim, 1.55f, .24f, PhaseAccent,
                    "baseline_straight", "linear", 0f, 8f);
                break;
            case 1:
                for (int side = -1; side <= 1; side += 2)
                    AddShot(sink, origin, aim + side * .08f, 1.28f, .24f,
                        PhaseAccent, "baseline_sine", "sine",
                        side * Simulation.TileSize * .46f, 8f,
                        frequency: .031f);
                break;
            default:
                FireFan(sink, origin, aim, 5, .72f, 1.12f,
                    PhaseAccent, "baseline_shotgun");
                break;
        }
    }

    private void FirePhraseAccent(List<EnemyProjectile> sink, Vector2 origin,
        Vector2 player)
    {
        float aim = AngleTo(origin, player);
        switch (Phase)
        {
            case 1:
                for (int side = -1; side <= 1; side += 2)
                {
                    AddShot(sink, origin, aim + side * .16f, 1.72f, .24f,
                        side < 0 ? Light.Accent : Dark.Accent,
                        "order_accent_straight", "linear", 0f, 8f,
                        shape: "needle");
                    AddShot(sink, origin, aim + side * .29f, .82f, .31f,
                        side < 0 ? Light.Accent : Dark.Accent,
                        "order_accent_wave", "sine",
                        side * Simulation.TileSize * .72f, 9f,
                        frequency: .014f, shape: "crescent");
                }
                break;
            case 2:
                for (int index = 0; index < 5; index++)
                {
                    if (index == 2)
                        continue;
                    float offset = (index - 2) * .17f + (index % 2 == 0 ? .05f : -.04f);
                    AddShot(sink, origin, aim + offset,
                        index % 2 == 0 ? 1.68f : .74f,
                        index % 2 == 0 ? .22f : .34f,
                        index % 2 == 0 ? Light.Accent : Dark.Accent,
                        "fracture_accent", index % 2 == 0 ? "linear" : "sine",
                        index % 2 == 0 ? 0f : Simulation.TileSize * .38f,
                        9f, frequency: .052f,
                        shape: index % 2 == 0 ? "needle" : "crescent");
                }
                break;
            case 3:
                for (int axis = 0; axis < 4; axis++)
                {
                    float direction = axis * MathF.PI / 2f
                        + (_regularVolleyCount % 2) * MathF.PI / 4f;
                    AddShot(sink, origin, direction, 1.46f, .25f,
                        axis % 2 == 0 ? Light.Accent : Dark.Accent,
                        "refraction_accent", "linear", 0f, 8f,
                        shape: "needle");
                }
                break;
            default:
                for (int side = -2; side <= 2; side++)
                    AddShot(sink, origin, aim + side * .11f,
                        side == 0 ? .64f : 1.88f,
                        side == 0 ? .42f : .2f,
                        Rainbow(side / 5f + (float)_visualTime * .05f),
                        "void_accent", "linear", 0f, 8f,
                        shape: side == 0 ? "orbit_core" : "needle",
                        speedDecay: side == 0 ? .22f : 0f,
                        preserveAuthoredLifetime: side == 0);
                break;
        }
    }

    private void AddBossSpecialAttack(List<EnemyProjectile> sink, Vector2 origin,
        Vector2 player, int volley)
    {
        AphantasiaSpecialAttack specials = ActiveSpecialAttacks();
        if (specials.HasFlag(AphantasiaSpecialAttack.Laser) && volley % 3 == 0)
            FireAphantasiaLaser(sink, origin, player);
        if (specials.HasFlag(AphantasiaSpecialAttack.Bomb) && volley % 4 == 0)
            FireAphantasiaBomb(sink, origin, player);
    }

    private AphantasiaSpecialAttack ActiveSpecialAttacks()
    {
        if (EncounterState is AphantasiaEncounterState.Survival
            or AphantasiaEncounterState.Finale)
        {
            return (SequenceStage % 4) switch
            {
                0 => AphantasiaSpecialAttack.DoubleHelix,
                1 => AphantasiaSpecialAttack.Laser,
                2 => AphantasiaSpecialAttack.Bomb,
                _ => AphantasiaSpecialAttack.None,
            };
        }
        return EncounterState == AphantasiaEncounterState.Combat
            ? CurrentPattern.SpecialAttack
            : AphantasiaSpecialAttack.None;
    }

    private void FireDoubleHelixPair(List<EnemyProjectile> sink, Vector2 origin,
        float direction, string source)
    {
        float amplitude = Simulation.TileSize * .72f;
        float size = Simulation.TileSize * .27f;
        float range = DistanceToArenaEdge(origin, direction) + size;
        const float helixSpeed = 2.05f;
        float lifetime = range
            / (.52f * (float)Simulation.ReferenceFps * helixSpeed) + 1f;
        foreach ((float signedAmplitude, Color color, string strand) in new[]
        {
            (amplitude, Light.Accent, "light"),
            (-amplitude, Dark.Accent, "dark"),
        })
        {
            sink.Add(new EnemyProjectile(
                origin.X - size / 2f, origin.Y - size / 2f,
                direction, helixSpeed, Damage * .56f, size,
                travelRange: range, color: color, shape: "crescent",
                path: "sine", amplitude: signedAmplitude, frequency: .027f,
                lifetime: lifetime, owner: $"aphantasia_double_helix_{source}_{strand}",
                ignoreWalls: true));
        }
    }

    private void BeginPhaseHandoff()
    {
        _phaseHandoffRemaining = Math.Max(
            _phaseHandoffRemaining,
            PhaseHandoffDuration);
        MilestoneHealRequested = true;
    }

    private void UpdatePhaseHandoff(double dt)
    {
        _phaseHandoffRemaining = Math.Max(0, _phaseHandoffRemaining - dt);
        Vector2 current = BossCenter;
        float blend = 1f - MathF.Exp(-2.25f * (float)dt);
        current = Vector2.Lerp(current, ArenaCenter, blend);
        WorldX = current.X - Size / 2f;
        WorldY = current.Y - Size / 2f;
        if (_phaseHandoffRemaining <= 0)
            CenterBody();
    }

    private void UpdateHelixStream(EnemyUpdateContext context, double dt)
    {
        if (CombatFiringPaused)
            return;
        if (!ActiveSpecialAttacks().HasFlag(AphantasiaSpecialAttack.DoubleHelix))
            return;
        _helixFireRemaining -= dt;
        if (_helixFireRemaining > 0)
            return;
        _helixFireRemaining = HelixFireCadence;
        AphantasiaMini? sourceMini = Phase switch
        {
            1 when Light.Alive => Light,
            2 when Dark.Alive => Dark,
            3 when Light.Empowered && Light.Alive => Light,
            3 when Dark.Empowered && Dark.Alive => Dark,
            3 when Light.Alive => Light,
            _ => null,
        };
        if (sourceMini is null && Phase < 4)
            sourceMini = Light.Alive ? Light : Dark.Alive ? Dark : null;
        Vector2 origin = sourceMini?.Position ?? BossCenter;
        string source = sourceMini is null ? "boss"
            : ReferenceEquals(sourceMini, Light) ? "light" : "dark";
        Vector2 player = new(context.PlayerWorldX, context.PlayerWorldY);
        List<EnemyProjectile> staged = BeginVolley();
        FireDoubleHelixPair(staged, origin, AngleTo(origin, player), source);
        CommitVolley(context.ProjectileSink);
    }

    private void FireAphantasiaLaser(List<EnemyProjectile> sink, Vector2 origin,
        Vector2 player)
    {
        float direction = AngleTo(origin, player);
        for (int side = -1; side <= 1; side += 2)
        {
            var laser = new EnemyProjectile(
                origin.X, origin.Y, direction + side * .18f, 0f,
                Damage * .78f, Simulation.TileSize * .28f,
                travelRange: ArenaRadius * 1.95f,
                color: side < 0 ? Light.Accent : Dark.Accent,
                // Lifetime grows by the same amount as the telegraph below,
                // so the extra warning is pure warning -- the beam still
                // burns for its original ~1.6s once it actually fires.
                shape: "diamond", path: "laser", lifetime: 3.1f,
                angularSpeed: side * .09f,
                owner: $"aphantasia_laser_{(side < 0 ? "light" : "dark")}")
            {
                TelegraphDuration = 1.5f,
            };
            sink.Add(laser);
        }
    }

    private void FireAphantasiaBomb(List<EnemyProjectile> sink, Vector2 origin,
        Vector2 player)
    {
        float offsetAngle = (float)_visualTime * .83f;
        Vector2 target = player + new Vector2(MathF.Cos(offsetAngle), MathF.Sin(offsetAngle))
            * Simulation.TileSize * 1.25f;
        float size = Simulation.TileSize * .62f;
        sink.Add(new EnemyProjectile(
            origin.X - size / 2f, origin.Y - size / 2f,
            AngleTo(origin, target), .7f, Damage * .82f, size,
            travelRange: ArenaRadius * 2f, color: Rainbow((float)_visualTime * .1f),
            shape: "orbit_core", path: "bomb", lifetime: 4f,
            owner: "aphantasia_bomb", ignoreWalls: true, target: target)
        {
            FuseDuration = 1.7f,
            BlastRadius = Simulation.TileSize * 1.8f,
            BurstCount = 10,
            BurstDamage = Damage * .68f,
            BurstRangeTiles = 18f,
            ThreatReservationCost = 10,
        });
    }

    private void FireArenaLaserGrid(List<EnemyProjectile> sink, bool diagonal)
    {
        float[] laneOffsets = [-.54f, -.18f, .18f, .54f];
        float[] directions = diagonal
            ? [MathF.PI / 4f, 3f * MathF.PI / 4f]
            : [0f, MathF.PI / 2f];
        string orientation = diagonal ? "anticardinal" : "cardinal";
        foreach (float direction in directions)
        {
            Vector2 heading = new(MathF.Cos(direction), MathF.Sin(direction));
            Vector2 perpendicular = new(-heading.Y, heading.X);
            foreach (float laneRatio in laneOffsets)
            {
                float offset = laneRatio * ArenaRadius;
                float halfChord = MathF.Sqrt(Math.Max(0f,
                    ArenaRadius * ArenaRadius - offset * offset));
                Vector2 origin = ArenaCenter + perpendicular * offset
                    - heading * halfChord * .96f;
                var laser = new EnemyProjectile(
                    origin.X, origin.Y, direction, 0f, Damage * .7f,
                    Simulation.TileSize * .25f,
                    travelRange: halfChord * 1.92f,
                    color: Rainbow(laneRatio + (float)_visualTime * .05f),
                    // Lifetime grows by the same amount as the telegraph
                    // below, so the extra warning is pure warning -- the beam
                    // still burns for its original ~1.65s once it fires.
                    shape: "diamond", path: "laser", lifetime: 3.15f,
                    owner: $"aphantasia_edge_grid_{orientation}")
                {
                    TelegraphDuration = 1.5f,
                    OriginTelegraphDuration = .45f,
                };
                sink.Add(laser);
            }
        }
    }

    private void FireEssencePattern(EnemyUpdateContext context, List<EnemyProjectile> sink)
    {
        Vector2 center = new(WorldX + Size / 2f, WorldY + Size / 2f);
        float spin = (float)_visualTime * (Phase == 1 ? .34f : .21f);
        int pattern = _patternIndex;
        bool darkDensity = TrueDark;
        bool lightTempo = TrueLight;
        if (Phase == 1)
        {
            switch (pattern)
            {
                case 0:
                    FireOrderedRing(sink, center, lightTempo ? 10 : darkDensity ? 18 : 14,
                        spin, lightTempo ? 1.72f : darkDensity ? .82f : 1.16f,
                        .27f, "ordered_bloom_outer", sineEvery: 4);
                    FireOrderedRing(sink, center, lightTempo ? 6 : 8,
                        -spin * .72f, lightTempo ? 1.18f : .72f,
                        .31f, "ordered_bloom_inner", sineEvery: 4);
                    _attackRemaining = lightTempo ? .56 : darkDensity ? .82 : .68;
                    break;
                case 1:
                    FireOrderedCurtain(sink,
                        vertical: (_regularVolleyCount & 1) == 0,
                        reverse: (_regularVolleyCount & 2) != 0,
                        lanes: darkDensity ? 13 : 10,
                        speed: lightTempo ? 1.7f : darkDensity ? .78f : 1.08f,
                        owner: "horizon_ordered");
                    FireOrderedRing(sink, center, 8, -spin, 1.02f, .24f,
                        "horizon_center", sineEvery: 0);
                    _attackRemaining = lightTempo ? .62 : .78;
                    break;
                default:
                    Vector2 player = new(context.PlayerWorldX, context.PlayerWorldY);
                    float aim = AngleTo(center, player);
                    int pairs = lightTempo ? 2 : darkDensity ? 4 : 3;
                    for (int pair = 0; pair < pairs; pair++)
                    {
                        float offset = (pair - (pairs - 1) / 2f) * .22f;
                        Color color = pair % 2 == 0 ? Light.Accent : Dark.Accent;
                        AddShot(sink, center, aim + offset - .055f, 1.88f, .22f,
                            color, "tidal_straight", "linear", 0f, 8f,
                            shape: "needle");
                        AddShot(sink, center, aim + offset + .055f, .78f, .31f,
                            color, "tidal_wave", "sine",
                            (pair % 2 == 0 ? 1f : -1f) * Simulation.TileSize * .74f,
                            10f, frequency: .014f, shape: "crescent");
                    }
                    _attackRemaining = lightTempo ? .48 : darkDensity ? .72 : .6;
                    break;
            }
            return;
        }

        if (pattern == 0)
        {
            int count = lightTempo ? 12 : darkDensity ? 22 : 18;
            FireBrokenRing(sink, center, count, spin,
                lightTempo ? 1.72f : darkDensity ? .7f : 1.04f,
                darkDensity ? .29f : .25f, "broken_bloom");
            FireBrokenRing(sink, center, Math.Max(8, count / 2), -spin * 1.34f,
                lightTempo ? .84f : 1.38f, .22f, "broken_bloom_echo");
            _attackRemaining = lightTempo ? .52 : darkDensity ? .76 : .62;
        }
        else if (pattern == 1)
        {
            FireFracturedCurtain(sink,
                vertical: (_regularVolleyCount & 1) == 0,
                reverse: (_regularVolleyCount & 2) != 0,
                lanes: darkDensity ? 15 : 11,
                owner: "erratic_eight");
            FireBrokenRing(sink, center, lightTempo ? 8 : 11, -spin,
                lightTempo ? 1.55f : .82f, .24f,
                "erratic_eight_cross");
            _attackRemaining = lightTempo ? .5 : .72;
        }
        else
        {
            Vector2 player = new(context.PlayerWorldX, context.PlayerWorldY);
            float aim = AngleTo(center, player);
            int pellets = (_regularVolleyCount & 1) == 0 ? 5 : 9;
            float spread = pellets == 5 ? .68f : 1.24f;
            for (int index = 0; index < pellets; index++)
            {
                float fraction = pellets == 1 ? .5f : index / (float)(pellets - 1);
                bool curl = index % 2 != 0;
                AddShot(sink, center, aim - spread / 2f + fraction * spread
                        + (index % 3 - 1) * .025f,
                    curl ? .76f : 1.62f,
                    curl ? .34f : .22f,
                    index % 2 == 0 ? Light.Accent : Dark.Accent,
                    "undertow", curl ? "sine" : "linear",
                    curl ? (index % 4 < 2 ? 1f : -1f) * Simulation.TileSize * .42f : 0f,
                    9f, frequency: curl ? .052f : .035f,
                    shape: "diamond");
            }
            _attackRemaining = pellets == 5 ? .48 : .72;
        }
    }

    private void FireTesseractPattern(EnemyUpdateContext context, List<EnemyProjectile> sink)
    {
        Vector2 center = new(WorldX + Size / 2f, WorldY + Size / 2f);
        float spin = (float)_visualTime * .52f;
        switch (_patternIndex)
        {
            case 0:
                FireOrderedRing(sink, center, 18, spin, .94f, .27f,
                    "prism_outer", sineEvery: 3);
                FireOrderedRing(sink, center, 10, -spin * 1.4f, 1.52f, .32f,
                    "prism_inner", sineEvery: 5);
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
                FireRefractorPair(sink, center,
                    new Vector2(context.PlayerWorldX, context.PlayerWorldY));
                _attackRemaining = .66;
                break;
            case 3:
                float foldRotation = (_regularVolleyCount & 1) == 0
                    ? 0f
                    : MathF.PI / 4f;
                for (int side = 0; side < 4; side++)
                {
                    float angle = side * MathF.PI / 2f + foldRotation;
                    Vector2 origin = ArenaCenter + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * ArenaRadius * .84f;
                    FireFan(sink, origin, angle + MathF.PI, 7, .68f, .88f,
                        side % 2 == 0 ? Light.Accent : Dark.Accent, "folding_inward");
                }
                _attackRemaining = .82;
                break;
            case 4:
                FireMirroredRibbon(sink, center,
                    new Vector2(context.PlayerWorldX, context.PlayerWorldY));
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
                // 5 -> 6 seeds: closes the widest gap in the constellation
                // fold, which used to leave a walkable wedge between seeds.
                for (int index = 0; index < 6; index++)
                    FirePortalSeed(sink, center, spin + index * MathF.Tau / 6f, .48f, "constellation");
                _attackRemaining = 1.4;
                break;
            case 1:
                FireOrderedRing(sink, center, 12, spin, 1.76f, .2f,
                    "void_clock_needles", sineEvery: 0);
                FireVoidAnchor(sink, center, -spin);
                if ((_regularVolleyCount & 1) == 0)
                    FirePortalSeed(sink, center, spin + MathF.PI, .42f, "clock_hand");
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
                    6, .86f, "void_pursuit");
                _attackRemaining = .62;
                break;
            default:
                // 3 -> 4 seeds and 9 -> 11 ring shots: the collapsing-tesseract
                // finale stage was the easiest one to find a gap in.
                for (int index = 0; index < 4; index++)
                    FirePortalSeed(sink, center, spin + index * MathF.Tau / 4f, .7f, "tesseract_hunt");
                FireRing(sink, center, 11, -spin * 2f, 1.42f, .24f, "collapse_ring", true);
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
            case AphantasiaSurvivalKind.VoidEclipse:
                // Reuses the void finale's own attack pool -- same "typical
                // fun stuff" repertoire, just as a mid-phase-4 checkpoint
                // rather than the closing spectacle.
                FireVoidStage(context, sink, SequenceStage);
                break;
        }
        _regularVolleyCount++;
        AddBossSpecialAttack(sink, BossCenter, player, _regularVolleyCount);
        AddSurvivalLaserGrid(sink);
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
        _regularVolleyCount++;
        AddBossSpecialAttack(sink, BossCenter,
            new Vector2(context.PlayerWorldX, context.PlayerWorldY),
            _regularVolleyCount);
        AddSurvivalLaserGrid(sink);
        CommitVolley(context.ProjectileSink);
    }

    private void FireFirstEclipseStage(List<EnemyProjectile> sink, double elapsed)
    {
        switch (SequenceStage)
        {
            case 0:
                FireOrderedRing(sink, ArenaCenter, 16, (float)elapsed * .23f,
                    .92f, .25f, "first_eclipse_ordered", sineEvery: 4);
                _attackRemaining = .64;
                break;
            case 1:
                FireOrderedCurtain(sink, true, ((int)elapsed & 1) == 0,
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
                FireOrderedCurtain(sink, true, ((int)elapsed & 1) == 0,
                    10, .9f, "first_eclipse_cross_v");
                FireOrderedCurtain(sink, false, ((int)elapsed & 1) != 0,
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
                FireFracturedCurtain(sink, ((int)elapsed & 1) == 0,
                    ((int)(elapsed * 1.5) & 1) == 0, 13,
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
        if (Phase <= 2 || CombatFiringPaused)
            return;
        _perimeterPressureRemaining -= dt;
        if (_perimeterPressureRemaining > 0)
            return;
        _perimeterPressureRemaining = PerimeterPressureCadence;
        List<EnemyProjectile> staged = BeginVolley();
        float rotation = (float)_visualTime * .17f;
        int projectileCount = Phase == 3
            ? PerimeterPressureCount / 2
            : PerimeterPressureCount;
        for (int index = 0; index < projectileCount; index++)
        {
            float angle = rotation + index * MathF.Tau / projectileCount;
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

    private void AddSurvivalLaserGrid(List<EnemyProjectile> sink)
    {
        if (Phase < 3 || EncounterState is not (AphantasiaEncounterState.Survival
            or AphantasiaEncounterState.Finale))
            return;
        _survivalGridVolleyCount++;
        if (_survivalGridVolleyCount % 6 != 0)
            return;
        FireArenaLaserGrid(sink,
            diagonal: (_survivalGridVolleyCount / 6) % 2 == 0);
    }

    private void UpdateArenaHalfPressure(EnemyUpdateContext context, double dt)
    {
        if (Phase <= 2 || CombatFiringPaused)
            return;
        for (int half = 0; half < 2; half++)
        {
            _halfPressureRemaining[half] -= dt;
            if (_halfPressureRemaining[half] > 0)
                continue;

            // Each side rolls its own cadence and projectile grammar. This
            // deliberately makes adjacent lanes disagree about speed, scale,
            // pellet count, and oscillation instead of mirroring one pattern.
            double cadence = .42 + _rng.NextDouble() * 1.18;
            if (Phase == 3)
                cadence *= 2;
            _halfPressureRemaining[half] = cadence;
            int bulletCount = new[] { 1, 1, 3, 5 }[_rng.Next(4)];
            float speed = .46f + (float)_rng.NextDouble() * 1.72f;
            float sizeTiles = .18f + (float)_rng.NextDouble() * .48f;
            bool sinusoidal = _rng.Next(3) != 0;
            float amplitude = sinusoidal
                ? Simulation.TileSize * (.18f + (float)_rng.NextDouble() * .92f)
                : 0f;
            float frequency = sinusoidal
                ? .009f + (float)_rng.NextDouble() * .058f
                : .035f;
            float boundaryAngle = half == 0
                ? MathF.PI / 2f + (float)_rng.NextDouble() * MathF.PI
                : -MathF.PI / 2f + (float)_rng.NextDouble() * MathF.PI;
            Vector2 origin = ArenaCenter
                + new Vector2(MathF.Cos(boundaryAngle), MathF.Sin(boundaryAngle))
                * ArenaRadius * .92f;
            Vector2 player = new(context.PlayerWorldX, context.PlayerWorldY);
            float aim = AngleTo(origin, Vector2.Lerp(ArenaCenter, player, .38f));
            float spread = bulletCount == 1 ? 0f
                : .22f + (float)_rng.NextDouble() * .62f;
            string owner = $"half_{half}_volley_{_halfVolleySerial++}";
            List<EnemyProjectile> staged = BeginVolley();
            for (int pellet = 0; pellet < bulletCount; pellet++)
            {
                float fraction = bulletCount == 1 ? .5f
                    : pellet / (float)(bulletCount - 1);
                AddShot(staged, origin,
                    aim - spread / 2f + fraction * spread,
                    speed * (.9f + pellet % 2 * .13f),
                    sizeTiles * (.88f + pellet % 3 * .12f),
                    half == 0 ? Light.Accent : Dark.Accent,
                    owner, sinusoidal ? "sine" : "linear",
                    amplitude * (pellet % 2 == 0 ? 1f : -1f), 14f,
                    frequency: frequency);
            }
            CommitVolley(context.ProjectileSink);
        }
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

    private void FireOrderedRing(List<EnemyProjectile> sink, Vector2 origin, int count,
        float rotation, float speed, float size, string owner, int sineEvery)
    {
        for (int index = 0; index < count; index++)
        {
            bool sinusoidal = sineEvery > 0 && index % sineEvery == 0;
            AddShot(sink, origin, rotation + index * MathF.Tau / count,
                speed, size,
                index % 2 == 0 ? Light.Accent : Dark.Accent,
                owner, sinusoidal ? "sine" : "linear",
                sinusoidal
                    ? (index % (sineEvery * 2) == 0 ? 1f : -1f)
                        * Simulation.TileSize * .62f
                    : 0f,
                10f, frequency: sinusoidal ? .014f : .035f,
                shape: sinusoidal ? "crescent" : "needle");
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
                index % 3 == 0 ? "sine" : "linear", Simulation.TileSize * .38f, 9f,
                shape: "diamond");
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

    private void FireOrderedCurtain(List<EnemyProjectile> sink, bool vertical,
        bool reverse, int lanes, float speed, string owner)
    {
        int movingGap = (_regularVolleyCount / 2) % Math.Max(1, lanes);
        for (int index = 0; index < lanes; index++)
        {
            if (index == movingGap || index == (movingGap + 1) % lanes)
                continue;
            float across = -ArenaRadius * .82f + ArenaRadius * 1.64f
                * (index + .5f) / lanes;
            Vector2 origin = vertical
                ? ArenaCenter + new Vector2(across,
                    reverse ? ArenaRadius * .92f : -ArenaRadius * .92f)
                : ArenaCenter + new Vector2(
                    reverse ? ArenaRadius * .92f : -ArenaRadius * .92f, across);
            float direction = vertical
                ? reverse ? -MathF.PI / 2f : MathF.PI / 2f
                : reverse ? MathF.PI : 0f;
            bool sinusoidal = index % 4 == 0;
            AddShot(sink, origin, direction, speed, .25f,
                index % 2 == 0 ? Light.Accent : Dark.Accent,
                owner, sinusoidal ? "sine" : "linear",
                sinusoidal ? Simulation.TileSize * .5f : 0f,
                12f, frequency: sinusoidal ? .014f : .035f,
                shape: sinusoidal ? "crescent" : "needle");
        }
    }

    private void FireMirroredRibbon(List<EnemyProjectile> sink, Vector2 origin,
        Vector2 target)
    {
        float aim = AngleTo(origin, target);
        for (int lane = -2; lane <= 2; lane++)
        {
            float offset = lane * .13f;
            for (int mirror = -1; mirror <= 1; mirror += 2)
            {
                AddShot(sink, origin, aim + offset + mirror * .035f,
                    mirror < 0 ? 1.34f : .96f,
                    mirror < 0 ? .22f : .28f,
                    mirror < 0 ? Light.Accent : Dark.Accent,
                    "ribbon_pursuit_mirror", "sine",
                    mirror * Simulation.TileSize * (.38f + Math.Abs(lane) * .08f),
                    10f, frequency: mirror < 0 ? .026f : .038f,
                    shape: "crescent");
            }
        }
    }

    private void FireRefractorPair(List<EnemyProjectile> sink, Vector2 origin,
        Vector2 target)
    {
        float aim = AngleTo(origin, target);
        for (int side = -1; side <= 1; side += 2)
        {
            AddShot(sink, origin, aim + side * .2f, 1.08f, .36f,
                side < 0 ? Light.Accent : Dark.Accent,
                "refractor", "linear", 0f, 12f,
                shape: "star", splitCount: 3, splitProgress: .5f,
                splitSpeedScale: 1.12f, splitSpread: .84f,
                splitChildLifetime: 9f, splitTelegraphStartRatio: .55f);
        }
    }

    private void FireFracturedCurtain(List<EnemyProjectile> sink, bool vertical,
        bool reverse, int lanes, string owner)
    {
        int firstGap = (_regularVolleyCount * 3) % Math.Max(1, lanes);
        for (int index = 0; index < lanes; index++)
        {
            if (index == firstGap || index == (firstGap + 1) % lanes
                || (index + _regularVolleyCount) % 7 == 3)
                continue;
            float across = -ArenaRadius * .82f + ArenaRadius * 1.64f
                * (index + .5f) / lanes;
            Vector2 origin = vertical
                ? ArenaCenter + new Vector2(across,
                    reverse ? ArenaRadius * .92f : -ArenaRadius * .92f)
                : ArenaCenter + new Vector2(
                    reverse ? ArenaRadius * .92f : -ArenaRadius * .92f, across);
            float direction = vertical
                ? reverse ? -MathF.PI / 2f : MathF.PI / 2f
                : reverse ? MathF.PI : 0f;
            bool slowCurl = index % 2 != 0;
            AddShot(sink, origin, direction + (index % 3 - 1) * .035f,
                slowCurl ? .72f : 1.58f,
                slowCurl ? .34f : .22f,
                slowCurl ? Dark.Accent : Light.Accent,
                owner, slowCurl ? "sine" : "linear",
                slowCurl
                    ? (index % 4 < 2 ? 1f : -1f) * Simulation.TileSize * .4f
                    : 0f,
                12f, frequency: slowCurl ? .052f : .035f,
                shape: slowCurl ? "crescent" : "needle");
        }
    }

    private void FireEdgePortals(List<EnemyProjectile> sink, bool vertical, string owner)
    {
        // -3..3 (7 lanes, tightened spacing) instead of -2..2 (5 lanes) --
        // the wider lane gaps used to leave a walkable corridor along the
        // edge before each portal's split caught up to it.
        for (int index = -3; index <= 3; index++)
        {
            Vector2 origin = vertical
                ? ArenaCenter + new Vector2(index * ArenaRadius * .2f, -ArenaRadius * .86f)
                : ArenaCenter + new Vector2(-ArenaRadius * .86f, index * ArenaRadius * .2f);
            float direction = vertical ? MathF.PI / 2f : 0;
            FirePortalSeed(sink, origin, direction, .44f + (index + 3) * .025f, owner);
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

    private void FireVoidAnchor(List<EnemyProjectile> sink, Vector2 origin,
        float direction)
    {
        AddShot(sink, origin, direction, 1.35f, .64f,
            Rainbow(direction / MathF.Tau + (float)_visualTime * .04f),
            "void_anchor", "linear", 0f, 5.5f,
            shape: "orbit_core", speedDecay: .32f,
            preserveAuthoredLifetime: true);
    }

    private void AddShot(List<EnemyProjectile> sink, Vector2 origin, float direction,
        float speed, float sizeTiles, Color color, string owner, string path,
        float amplitude, float lifetime, bool deliberatelyShortRange = false,
        float frequency = .035f, string? shape = null, float speedDecay = 0f,
        bool preserveAuthoredLifetime = false, int splitCount = 0,
        float splitProgress = 0f, float splitSpeedScale = 1.08f,
        float? splitSpread = null, float? splitChildLifetime = null,
        float splitTelegraphStartRatio = 1f)
    {
        float size = Simulation.TileSize
            * Math.Max(MinimumProjectileSizeTiles, sizeTiles);
        float edgeRange = DistanceToArenaEdge(origin, direction) + size;
        float travelRange = deliberatelyShortRange
            ? Math.Min(ArenaRadius * .42f, edgeRange)
            : edgeRange;
        float requiredLifetime = travelRange
            / Math.Max(.01f, speed * .52f * (float)Simulation.ReferenceFps * .88f)
            + .75f;
        string projectileShape = shape ?? (path == "sine" ? "crescent" : "needle");
        var projectile = new EnemyProjectile(
            origin.X - size / 2f, origin.Y - size / 2f,
            direction, speed, Damage * .62f, size,
            travelRange: travelRange, color: color,
            shape: projectileShape, path: path, amplitude: amplitude, frequency: frequency,
            lifetime: deliberatelyShortRange || preserveAuthoredLifetime
                ? lifetime
                : Math.Max(lifetime, requiredLifetime),
            speedDecay: speedDecay,
            owner: $"aphantasia_{owner}", ignoreWalls: true);
        if (splitCount > 1)
        {
            projectile.SplitCount = splitCount;
            projectile.SplitAt = travelRange * Math.Clamp(splitProgress, .05f, .95f);
            projectile.SplitSpeedScale = splitSpeedScale;
            projectile.SplitSpread = splitSpread;
            projectile.SplitChildLifetime = splitChildLifetime;
            projectile.ThreatReservationCost = splitCount;
            projectile.SplitTelegraphStartRatio = splitTelegraphStartRatio;
        }
        sink.Add(projectile);
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
        bool perimeterVolley = _volleyScratch.Count > 0
            && _volleyScratch.All(projectile =>
                projectile.Owner == "aphantasia_perimeter_drift");
        int volleyCap = perimeterVolley
            ? ActiveThreatSoftCap
            : ActiveThreatSoftCap - PerimeterThreatReserve;
        foreach (EnemyProjectile projectile in _volleyScratch)
        {
            int projectileCost = Math.Max(1, projectile.ThreatReservationCost);
            while (activeCost + projectileCost > volleyCap)
            {
                EnemyProjectile? longestLasting = sink
                    .Where(candidate => !candidate.RemFlag
                        && candidate.Owner?.StartsWith("aphantasia_", StringComparison.Ordinal) == true)
                    .MaxBy(candidate => candidate.Age);
                if (longestLasting is null)
                    break;
                sink.Remove(longestLasting);
                activeCost -= Math.Max(1, longestLasting.ThreatReservationCost);
            }
            if (activeCost + projectileCost <= volleyCap)
            {
                sink.Add(projectile);
                activeCost += projectileCost;
            }
        }
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
            AphantasiaSurvivalKind.VoidEclipse => VoidEclipseStages,
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

    private void StartNextSubphase(bool revivePair, bool beginHandoff = true)
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
        _regularVolleyCount = 0;
        _helixFireRemaining = .2;
        _subphaseCombatElapsed = 0;
        _phraseWasBreathing = false;
        _stateElapsed = 0;
        if (revivePair)
            ReviveMiniPair();
        if (Phase <= 2)
        {
            Light.Aggressive = _rng.Next(2) == 0;
            Dark.Aggressive = _rng.Next(2) == 0;
        }
        if (beginHandoff)
            BeginPhaseHandoff();
    }

    /// <summary>
    /// Draws every subphase in the pool exactly once, in a random order
    /// that never repeats the subphase that was just played (including the
    /// one that ended the previous cycle, via <paramref name="pool"/>'s
    /// current <c>_patternIndex</c> passed through as the "previous" seed).
    /// </summary>
    private void RefillPatternBag(IReadOnlyList<AphantasiaPattern> pool)
    {
        _patternBag.Clear();
        var remaining = new List<int>(pool.Count);
        for (int index = 0; index < pool.Count; index++)
            remaining.Add(index);
        if (!TryBuildPatternOrder(remaining, _patternIndex, _patternBag))
            throw new InvalidOperationException("Unable to arrange Aphantasia's pattern cycle.");
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
        if (!mini.Alive || Dying || PhaseHandoffActive || EntranceRemaining > 0)
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
            || EntranceRemaining > 0 || PhaseHandoffActive || Dying)
            return new HitResult(false, false, 0, true);
        if (Phase <= 2 && (Light.Alive || Dark.Alive || !DamageWindowActive))
            return new HitResult(false, false, 0, true);
        if (_burstShieldRemaining > 0)
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
        else if (Phase == 4 && !_phaseFourSurvivalDone)
        {
            floor = (int)Math.Round(_barMaxHp * .5);
            gate = () =>
            {
                _phaseFourSurvivalDone = true;
                BeginSurvival(AphantasiaSurvivalKind.VoidEclipse, PhaseFourSurvivalDuration);
            };
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
            // A single hit big enough to blow past the cap for the active
            // bar shields the boss instead of firing the gate on the spot --
            // the fight holds open for DamageCapInvincibilityDuration so a
            // burst nuke can't skip straight through a scripted beat.
            double capFraction = Phase <= 2 ? DamageCapSharedPhaseFraction : DamageCapSoloPhaseFraction;
            if (requested >= _barMaxHp * capFraction)
            {
                _burstShieldRemaining = DamageCapInvincibilityDuration;
                _pendingGate = gate;
                _pendingGateFloorOne = floor == 1;
            }
            else
            {
                if (floor == 1)
                    _displayZeroHealth = true;
                gate();
                BeginPhaseHandoff();
            }
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
            StartNextSubphase(revivePair: true, beginHandoff: false);
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
        _survivalGridVolleyCount = 0;
        _damageWindowRemaining = 0;
        if (kind != AphantasiaSurvivalKind.VoidEclipse)
        {
            ReviveMiniPair();
            Light.Aggressive = true;
            Dark.Aggressive = true;
            Light.Vulnerable = false;
            Dark.Vulnerable = false;
        }
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
        _survivalGridVolleyCount = 0;
        _displayZeroHealth = true;
        if (kind == AphantasiaSurvivalKind.VoidFinale)
        {
            _voidVortexActive = true;
            _voidVortexProgress = 0f;
        }
    }

    private void BeginDeath()
    {
        EncounterState = AphantasiaEncounterState.Dying;
        SurvivalKind = AphantasiaSurvivalKind.None;
        _deathRemaining = 4.5;
        _stateElapsed = 0;
        _displayZeroHealth = true;
        Hp = 1;
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
        _voidVortexActive = false;
        _voidVortexProgress = 0f;
        _burstShieldRemaining = 0;
        _pendingGate = null;
        _firstSurvivalDone = Phase >= 2;
        _secondSurvivalDone = Phase >= 3;
        _phaseThreeChoiceDone = Phase >= 4;
        _phaseFourSurvivalDone = false;
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
            StartNextSubphase(revivePair: Phase < 4, beginHandoff: false);
    }

    public void DebugAdvanceSubPhase()
    {
        bool revivePair = !Light.Alive && !Dark.Alive;
        StartNextSubphase(revivePair, beginHandoff: false);
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
            BeginSurvival(AphantasiaSurvivalKind.VoidEclipse, PhaseFourSurvivalDuration);
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
        if (!PhaseHandoffActive && CombatDeclarationActive)
            DrawSubphaseDeclaration(spriteBatch, center);
        if (PhaseHandoffActive)
            DrawPhaseHandoff(spriteBatch, center);
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
        DrawGroundShadow(spriteBatch, center, Size * .5f);
        float pulse = 1f + MathF.Sin((float)_visualTime * 2.1f) * .05f;
        Color glowColor = TrueLight ? new Color(88, 125, 228)
            : TrueDark ? new Color(8, 18, 65)
            : Phase >= 3 ? Rainbow((float)_visualTime * .07f)
            : PhaseAccent;
        // Chasing and pathed patterns turn the body to face where it's
        // headed (see UpdateMovement); standing patterns and every
        // non-combat state (survival, handoffs, transformation) keep the
        // plain idle spin. Either way the orbiting decorations always use
        // `orbitSpin`, never `bodyYaw`, so their orbit and direction never
        // change with the body's facing.
        bool facingActive = EncounterState == AphantasiaEncounterState.Combat
            && CurrentPattern.Movement is AphantasiaMovementMode.Chase or AphantasiaMovementMode.Pathed;
        if (Phase <= 2)
        {
            float orbitSpin = (float)_visualTime * (Phase == 1 ? .82f : .38f);
            float bodyYaw = facingActive ? _facingYaw : orbitSpin;
            float bodyPitch = bodyYaw * .63f;
            Vector2[] cube = ProjectCube(center, Size * .42f * pulse, bodyYaw, bodyPitch);
            DrawOrbitingCubes(spriteBatch, center, orbitSpin, foreground: false);
            DrawFilledCube(spriteBatch, cube, new Color(3, 14, 58), PhaseAccent, bodyYaw, bodyPitch);
            DrawOrbitingCubes(spriteBatch, center, orbitSpin, foreground: true);
            if (facingActive)
                DrawFacingMarker(spriteBatch, center, Size * .42f * pulse, bodyYaw, bodyPitch);
        }
        else if (Phase == 3 || EncounterState == AphantasiaEncounterState.Transforming)
        {
            float orbitSpin = (float)_visualTime * .31f;
            float bodyYaw = facingActive ? _facingYaw : orbitSpin;
            float outerPitch = bodyYaw * .71f;
            Vector2[] outer = ProjectCube(center, Size * .62f * pulse, bodyYaw, outerPitch);
            Color outerFill = new(1, 1, 5, 235);
            // The inner cube genuinely nests inside the shell: the shell's
            // far side (facing away from camera, toward the floor) draws
            // first so the solid inner cube covers it, then its near side
            // (facing the camera) draws last, overlapping the inner cube.
            DrawWireCubeLayer(spriteBatch, outer, rainbow: true, outerFill,
                bodyYaw, outerPitch, front: false);
            Vector2[] inner = ProjectCube(center, Size * .3f, -bodyYaw * .72f, bodyYaw * .43f);
            DrawFilledCube(spriteBatch, inner, Rainbow(orbitSpin * .08f) * .82f, UiTheme.Cream,
                -bodyYaw * .72f, bodyYaw * .43f);
            DrawWireCubeLayer(spriteBatch, outer, rainbow: true, outerFill,
                bodyYaw, outerPitch, front: true);
            if (EncounterState == AphantasiaEncounterState.Transforming)
            {
                DrawTransformationSweep(spriteBatch, center, Size * .62f * pulse);
                DrawTransformationTentacles(spriteBatch, center, Size * .62f * pulse);
            }
            if (facingActive)
                DrawFacingMarker(spriteBatch, center, Size * .62f * pulse, bodyYaw, outerPitch);
        }
        else
        {
            float orbitSpin = (float)_visualTime * .46f;
            float bodyYaw = facingActive ? _facingYaw : orbitSpin;
            float bodyPitch = bodyYaw * .6f;
            // Phase 4 is the true final form -- its border weight is bumped
            // noticeably past every earlier phase so the core reads heavier
            // and more final, not just another recolor of the same cube.
            // It's also real cube geometry now rather than a flat satellite
            // square, so it can pick up the same chase/pathed facing turn
            // the earlier phases do.
            Vector2[] core = ProjectCube(center, Size * .34f, bodyYaw, bodyPitch);
            DrawFilledCube(spriteBatch, core, Rainbow(orbitSpin * .08f), UiTheme.Cream,
                bodyYaw, bodyPitch, inkWidth: 8, accentWidth: 4);
            if (facingActive)
                DrawFacingMarker(spriteBatch, center, Size * .34f, bodyYaw, bodyPitch);
            for (int index = 0; index < 6; index++)
            {
                float angle = orbitSpin * (index % 2 == 0 ? 1f : -.72f) + index * MathF.Tau / 6f;
                float radius = Size * (.55f + .16f * MathF.Sin(orbitSpin * 1.7f + index));
                Vector2 pane = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
                float half = Size * (.16f + index % 2 * .04f);
                float tumbleYaw = orbitSpin * 1.4f + index * 1.3f;
                float tumblePitch = orbitSpin * .9f + index * .8f;
                Color edge = Rainbow(index / 6f + orbitSpin * .05f);
                Vector2[] paneCube = ProjectCube(pane, half, tumbleYaw, tumblePitch);
                DrawFilledCube(spriteBatch, paneCube, edge, UiTheme.Cream, tumbleYaw, tumblePitch,
                    inkWidth: 4, accentWidth: 2);
            }
        }
        DrawRimGlow(spriteBatch, center, Size * .5f, Size * .84f, glowColor, hot: Phase >= 3);
        if (SurvivalKind is AphantasiaSurvivalKind.GrandChoice
            or AphantasiaSurvivalKind.VoidEclipse or AphantasiaSurvivalKind.VoidFinale)
            DrawSurvivalTentacles(spriteBatch, center);
    }

    /// <summary>
    /// Large flowing tentacles (same technique as the transformation's,
    /// and as the Aphantasia portal in The Mind) circling the core through
    /// the Phase 3 and Phase 4 survival sub-phases -- ambient spectacle,
    /// not a hazard; the actual attacks are the projectiles.
    /// </summary>
    private void DrawSurvivalTentacles(SpriteBatch spriteBatch, Vector2 center)
    {
        const int spikeCount = 7;
        float targetLength = ArenaRadius * .2f;
        float spin = (float)_visualTime * .22f;
        for (int index = 0; index < spikeCount; index++)
        {
            float baseAngle = index * MathF.Tau / spikeCount + spin;
            float length = targetLength * (.82f + .18f * MathF.Sin((float)_visualTime * 1.1f + index));
            float width = targetLength * .1f;
            DrawTentacleSpikeWithTrail(spriteBatch, center, baseAngle, length, width,
                phase: index * 1.9f, colorPhase: index / (float)spikeCount, segments: 40);
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
            // Satellites swinging behind the core sit smaller, dimmer, and
            // thinner-bordered than the ones swinging in front, so the orbit
            // reads as passing through the body instead of two flat rings.
            // Each one tumbles on its own axis (offset by index) rather than
            // all six spinning in lockstep.
            float depth = foreground ? 1f : .8f;
            float alpha = foreground ? 1f : .72f;
            float tumbleYaw = (float)_visualTime * 1.6f + index * 1.1f;
            float tumblePitch = (float)_visualTime * 1.1f + index * .7f;
            Color tint = Rainbow(index / (float)satellites + spin * .04f) * alpha;
            Vector2[] cube = ProjectCube(at, Size * .1f * depth, tumbleYaw, tumblePitch);
            DrawFilledCube(spriteBatch, cube, tint, UiTheme.Cream * alpha, tumbleYaw, tumblePitch,
                inkWidth: foreground ? 4 : 2, accentWidth: foreground ? 2 : 1);
        }
    }

    private void DrawMini(SpriteBatch spriteBatch, Camera camera,
        Vector2 playerWorldPosition, Vector2 screenShake, AphantasiaMini mini)
    {
        if (!mini.Alive)
            return;
        Vector2 center = camera.WorldToScreen(mini.Position, playerWorldPosition, screenShake);
        float radius = MiniSize * .45f * (mini.Empowered ? 1.12f : 1f);
        DrawGroundShadow(spriteBatch, center, radius * 1.35f);
        float handedness = ReferenceEquals(mini, Light) ? 1f : -1f;
        float spin = (float)_visualTime * (mini.Aggressive ? 1.8f : .72f) * handedness;

        // The body hovers above its own ground shadow instead of sitting
        // pinned to it -- every status readout below still anchors to the
        // true ground point at `center`.
        float bob = MathF.Sin((float)_visualTime * 2.4f + handedness) * radius * .16f;
        Vector2 bodyCenter = center + new Vector2(0, -bob - radius * .1f);
        float pitch = spin * .63f;
        Vector2[] cube = ProjectCube(bodyCenter, radius, spin, pitch);

        // Light is a solid, opaque shard; Dark is a hollow void-glass shell
        // -- "solid light vs. hollow shadow" told through construction, not
        // just color, while both still tumble from the same cube geometry
        // the boss body itself is built from.
        if (ReferenceEquals(mini, Light))
        {
            DrawFilledCube(spriteBatch, cube, mini.Accent, UiTheme.Cream, spin, pitch,
                inkWidth: mini.Empowered ? 6 : 4, accentWidth: mini.Empowered ? 3 : 2);
        }
        else
        {
            DrawWireCube(spriteBatch, cube, rainbow: false, fill: mini.Accent, spin, pitch,
                edgeColor: Color.Lerp(mini.Accent, UiTheme.Cream, .3f));
        }

        if (mini.Empowered)
        {
            // The survivor now visibly carries a fragment of the twin it
            // destroyed: a small hollow shell in the absorbed mini's color,
            // tumbling counter to the outer shell.
            AphantasiaMini absorbed = ReferenceEquals(mini, Light) ? Dark : Light;
            float innerYaw = -spin * 1.4f;
            float innerPitch = -pitch * 1.4f;
            Vector2[] innerCube = ProjectCube(bodyCenter, radius * .42f, innerYaw, innerPitch);
            DrawWireCube(spriteBatch, innerCube, rainbow: false, fill: absorbed.Accent,
                innerYaw, innerPitch, edgeColor: Color.Lerp(absorbed.Accent, UiTheme.Cream, .3f));
        }

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
            DrawRimGlow(spriteBatch, center, glyphRadius * 1.4f, glyphRadius * 2.1f,
                Rainbow((float)_visualTime * .1f), hot: true);
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

    /// <summary>Applies the cube's yaw/pitch rig to one direction, shared by vertex and face-normal transforms.</summary>
    private static Vector3 RotateYawPitch(Vector3 value, float yaw, float pitch)
    {
        float cy = MathF.Cos(yaw), sy = MathF.Sin(yaw);
        float rx = value.X * cy + value.Z * sy;
        float rz = -value.X * sy + value.Z * cy;
        float cp = MathF.Cos(pitch), sp = MathF.Sin(pitch);
        float ry = value.Y * cp - rz * sp;
        rz = value.Y * sp + rz * cp;
        return new Vector3(rx, ry, rz);
    }

    private static Vector2[] ProjectCube(Vector2 center, float extent, float yaw, float pitch)
    {
        var result = new Vector2[8];
        for (int index = 0; index < 8; index++)
        {
            float x = (index & 1) == 0 ? -1 : 1;
            float y = (index & 2) == 0 ? -1 : 1;
            float z = (index & 4) == 0 ? -1 : 1;
            Vector3 rotated = RotateYawPitch(new Vector3(x, y, z), yaw, pitch);
            float perspective = 1f + rotated.Z * .12f;
            result[index] = center + new Vector2(rotated.X, rotated.Y) * extent * perspective;
        }
        return result;
    }

    private static Vector3 RotatedFaceNormal(int faceIndex, float yaw, float pitch) =>
        RotateYawPitch(CubeFaceNormals[faceIndex], yaw, pitch);

    /// <summary>Rotated depth of one cube vertex (index encoded the same way as <see cref="ProjectCube"/>). Positive is toward the camera.</summary>
    private static float CubeVertexDepth(int vertexIndex, float yaw, float pitch)
    {
        float x = (vertexIndex & 1) == 0 ? -1 : 1;
        float y = (vertexIndex & 2) == 0 ? -1 : 1;
        float z = (vertexIndex & 4) == 0 ? -1 : 1;
        return RotateYawPitch(new Vector3(x, y, z), yaw, pitch).Z;
    }

    /// <summary>
    /// Brightness for one cube face against a fixed upper-left key light, kept
    /// in a moderate [.5, 1] band on purpose -- unlit faces stay readable
    /// instead of crushing to black, matching the fight's general preference
    /// for depth conveyed through color/intensity rather than heavy shadow.
    /// </summary>
    private static float FaceLight(int faceIndex, float yaw, float pitch)
    {
        float lit = Vector3.Dot(RotatedFaceNormal(faceIndex, yaw, pitch), CubeLightDirection);
        return .5f + .5f * Math.Clamp(lit, 0f, 1f);
    }

    private static void DrawFilledCube(SpriteBatch spriteBatch, Vector2[] points,
        Color fill, Color edge, float yaw, float pitch, int inkWidth = 7, int accentWidth = 3)
    {
        for (int index = 0; index < CubeFaces.Length; index++)
        {
            int[] face = CubeFaces[index];
            float light = FaceLight(index, yaw, pitch);
            Primitives2D.FillQuad(spriteBatch, points[face[0]], points[face[1]],
                points[face[2]], points[face[3]], fill * (light * .8f));
        }
        foreach (int[] pair in CubeEdges)
        {
            Primitives2D.Line(spriteBatch, points[pair[0]], points[pair[1]], UiTheme.Ink, inkWidth);
            Primitives2D.Line(spriteBatch, points[pair[0]], points[pair[1]], edge, accentWidth);
        }
    }

    private static void DrawWireCube(SpriteBatch spriteBatch, Vector2[] points,
        bool rainbow, Color fill, float yaw, float pitch, Color? edgeColor = null)
    {
        DrawWireCubeLayer(spriteBatch, points, rainbow, fill, yaw, pitch, front: false, edgeColor);
        DrawWireCubeLayer(spriteBatch, points, rainbow, fill, yaw, pitch, front: true, edgeColor);
    }

    /// <summary>
    /// Half of a wire cube's faces/edges -- whichever half is on the near
    /// (front, toward camera) or far (back, toward the floor) side of the
    /// cube, judged by the same rotated Z depth <see cref="ProjectCube"/>
    /// already uses for its perspective scale. <see cref="DrawWireCube"/>
    /// draws both halves back-then-front for its own correct self-occlusion;
    /// Phase 3's nested cube calls the two halves directly so it can sandwich
    /// the inner solid cube between them -- the shell's far side sits behind
    /// the solid, its near side sits in front of it.
    /// </summary>
    private static void DrawWireCubeLayer(SpriteBatch spriteBatch, Vector2[] points,
        bool rainbow, Color fill, float yaw, float pitch, bool front, Color? edgeColor = null)
    {
        for (int index = 0; index < CubeFaces.Length; index++)
        {
            bool faceFront = RotatedFaceNormal(index, yaw, pitch).Z > 0f;
            if (faceFront != front)
                continue;
            int[] face = CubeFaces[index];
            float light = FaceLight(index, yaw, pitch);
            Primitives2D.FillQuad(spriteBatch, points[face[0]], points[face[1]],
                points[face[2]], points[face[3]], fill * (light * .3f));
        }
        for (int index = 0; index < CubeEdges.Length; index++)
        {
            int[] edge = CubeEdges[index];
            float depth = (CubeVertexDepth(edge[0], yaw, pitch)
                + CubeVertexDepth(edge[1], yaw, pitch)) * .5f;
            if ((depth > 0f) != front)
                continue;
            Color color = rainbow ? Rainbow(index / (float)CubeEdges.Length) : edgeColor ?? UiTheme.Purple;
            Primitives2D.Line(spriteBatch, points[edge[0]], points[edge[1]], UiTheme.Ink, 8);
            Primitives2D.Line(spriteBatch, points[edge[0]], points[edge[1]], color, 3);
        }
    }

    /// <summary>
    /// A small bright point marking the cube's local "front" (the +Z face's
    /// outward normal), projected the same way <see cref="ProjectCube"/>
    /// projects vertices. Only drawn while <c>facingActive</c>, so it swings
    /// to track the player during Chase and the travel direction during
    /// Pathed -- a concrete tell for the facing turn beyond the subtler
    /// lighting shift <see cref="FaceLight"/> already gives it.
    /// </summary>
    private void DrawFacingMarker(SpriteBatch spriteBatch, Vector2 center,
        float extent, float yaw, float pitch)
    {
        Vector3 rotatedFront = RotateYawPitch(new Vector3(0, 0, 1), yaw, pitch);
        float perspective = 1f + rotatedFront.Z * .12f;
        Vector2 tip = center + new Vector2(rotatedFront.X, rotatedFront.Y)
            * extent * perspective * 1.22f;
        float pulse = .7f + .3f * MathF.Sin((float)_visualTime * 8f);
        float dotRadius = Math.Max(3f, extent * .09f) * pulse;
        Primitives2D.FillCircle(spriteBatch, tip + new Vector2(2, 3), dotRadius, UiTheme.Shadow);
        Primitives2D.FillCircle(spriteBatch, tip, dotRadius, UiTheme.Cream);
        Primitives2D.CircleOutline(spriteBatch, tip, Math.Max(4f, extent * .13f), UiTheme.Ink, 2);
    }

    /// <summary>
    /// Soft ground-contact shadow: a flattened dark ellipse beneath an
    /// entity, offset down like every other shadow in the game (Player,
    /// ProjectileVisuals, the laser origin telegraph). Kept translucent
    /// rather than solid black so it reads as depth, not a hole in the floor.
    /// </summary>
    private static void DrawGroundShadow(SpriteBatch spriteBatch, Vector2 center,
        float radius, float alpha = 1f)
    {
        var rect = new Rectangle(
            (int)(center.X - radius + radius * .05f),
            (int)(center.Y - radius * .38f + radius * .16f),
            (int)(radius * 2f),
            (int)(radius * .76f));
        Primitives2D.FillEllipse(spriteBatch, rect, UiTheme.Shadow * (.55f * alpha));
    }

    /// <summary>
    /// A soft outward glow -- a handful of widening, fading ring outlines --
    /// used to sell the boss core and empowered minis as light sources
    /// against the darkened arena, rather than flat cutouts. Rainbow is
    /// reserved for this fight's highest-stakes moments (Phase 3+, empowered
    /// minis), so <paramref name="hot"/> gives those a wider, brighter bloom
    /// than the plain phase-accent glow -- color intensity standing in for
    /// urgency rather than more geometry or darker shading.
    /// </summary>
    private static void DrawRimGlow(SpriteBatch spriteBatch, Vector2 center,
        float innerRadius, float outerRadius, Color color, bool hot = false)
    {
        int rings = hot ? 6 : 4;
        float reach = hot ? innerRadius + (outerRadius - innerRadius) * 1.2f : outerRadius;
        float alphaScale = hot ? .38f : .3f;
        for (int index = 0; index < rings; index++)
        {
            float t = (index + 1) / (float)rings;
            float radius = MathF.Sin(t * MathF.PI / 2f) * (reach - innerRadius) + innerRadius;
            float alpha = (1f - t) * alphaScale;
            Primitives2D.CircleOutline(spriteBatch, center, radius, color * alpha,
                Math.Max(2, (int)((reach - innerRadius) * .16f)), 32);
        }
    }

    /// <summary>
    /// A single bright point sweeping one and a half laps around the cube
    /// over the Phase 3 -> 4 transformation, trailing a short rainbow arc.
    /// Purely decorative -- it sells "becoming" as the tesseract cube swap
    /// happens, rather than the swap just snapping between two states.
    /// </summary>
    private void DrawTransformationSweep(SpriteBatch spriteBatch, Vector2 center, float extent)
    {
        float progress = 1f - (float)(_transitionRemaining / TesseractTransitionDuration);
        float sweepAngle = progress * MathF.Tau * 1.5f;
        float radius = extent * 1.35f;
        Color sweepColor = Rainbow(progress * .6f);
        Primitives2D.Arc(spriteBatch,
            new Rectangle((int)(center.X - radius), (int)(center.Y - radius),
                (int)(radius * 2), (int)(radius * 2)),
            sweepAngle - .6f, sweepAngle,
            sweepColor, Math.Max(2, (int)(extent * .05f)), 40);
        Vector2 head = center + new Vector2(MathF.Cos(sweepAngle), MathF.Sin(sweepAngle)) * radius;
        Primitives2D.FillCircle(spriteBatch, head, Math.Max(3f, extent * .07f), UiTheme.Cream);
    }

    /// <summary>
    /// Large flowing tentacles (see <see cref="DrawTentacleSpikeWithTrail"/>,
    /// same technique the Aphantasia portal in The Mind uses) blooming out
    /// from the cube and resolving back to nothing over the transformation,
    /// rather than staying at full length throughout -- energy crackling as
    /// the tesseract remakes itself, not a plain hold. Covers both the
    /// Phase 2 -> 3 and Phase 3 -> 4 transitions, since both share this
    /// same Transforming encounter state.
    /// </summary>
    private void DrawTransformationTentacles(SpriteBatch spriteBatch, Vector2 center, float extent)
    {
        float progress = Math.Clamp(
            1f - (float)(_transitionRemaining / TesseractTransitionDuration), 0f, 1f);
        float bloom = MathF.Sin(progress * MathF.PI);
        if (bloom <= .02f)
            return;
        const int spikeCount = 6;
        float targetLength = ArenaRadius * .2f;
        for (int index = 0; index < spikeCount; index++)
        {
            float baseAngle = index * MathF.Tau / spikeCount + (float)_visualTime * .5f;
            float length = targetLength * bloom;
            float width = targetLength * .11f;
            DrawTentacleSpikeWithTrail(spriteBatch, center, baseAngle, length, width,
                phase: index * 2.1f, colorPhase: index / (float)spikeCount + progress * .6f,
                segments: 40);
        }
    }

    /// <summary>
    /// A tentacle spike with trailing after-image echoes -- darkened,
    /// fading copies of itself evaluated at slightly earlier moments in
    /// time, exactly like the Aphantasia portal decoration in The Mind
    /// (same routine, same technique). Every point on a spike is a pure
    /// function of time, so "what it looked like 50ms ago" is just this
    /// same call re-evaluated at time - .05 with some darken and reduced
    /// alpha -- no history buffer needed. The echo alpha is real
    /// transparency, not just a darker hue: without it, a handful of fully
    /// opaque echoes of a fast wiggle interfere into a rigid, ladder-like
    /// pattern instead of blending into a soft trail (this bit the portal
    /// version before the fix landed there).
    /// </summary>
    private void DrawTentacleSpikeWithTrail(SpriteBatch spriteBatch, Vector2 center,
        float baseAngle, float length, float width, float phase, float colorPhase,
        int segments = 22, int echoCount = 6, float echoDelay = .08f)
    {
        float time = (float)_visualTime;
        for (int echo = echoCount; echo >= 1; echo--)
        {
            float t = echo / (float)(echoCount + 1);
            Primitives2D.DrawTentacleSpike(spriteBatch, center, baseAngle, length, width,
                phase, colorPhase, time - echo * echoDelay, segments,
                darken: t, alpha: 1f - t * .85f);
        }
        Primitives2D.DrawTentacleSpike(spriteBatch, center, baseAngle, length, width,
            phase, colorPhase, time, segments);
    }

    private void DrawDeath(SpriteBatch spriteBatch, Vector2 center)
    {
        float progress = 1f - (float)(_deathRemaining / 4.5);
        const int spikeCount = 10;
        for (int index = 0; index < spikeCount; index++)
        {
            float baseAngle = index * MathF.Tau / spikeCount + progress * 3.2f;
            float length = ArenaRadius * (.18f + progress * .82f);
            float width = Size * (.1f + progress * .06f);
            DrawTentacleSpikeWithTrail(spriteBatch, center, baseAngle, length, width,
                phase: index * 1.7f, colorPhase: index / (float)spikeCount + progress,
                segments: 40);
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
        DrawDistantFragments(spriteBatch, center);
        DrawArenaWall(spriteBatch, center);
        DrawFloorPaneling(spriteBatch, center, PresentationSurvivalActive);

        if (_voidVortexActive)
            DrawVoidVortex(spriteBatch, center);

        if (Phase >= 3 && PresentationSurvivalActive)
            DrawSurvivalScreenMood(spriteBatch, logicalViewport);

        if (PresentationSurvivalActive && SurvivalDuration > 0)
        {
            const int timerSegments = 144;
            float timerRadius = ArenaRadius - 13f;
            Primitives2D.CircleOutline(spriteBatch, center, timerRadius,
                UiTheme.Ink * .88f, 18);
            int completedSegments = Math.Clamp(
                (int)MathF.Ceiling(timerSegments * SurvivalTimerProgress),
                0, timerSegments);
            for (int index = 0; index < completedSegments; index++)
            {
                float startAngle = -MathF.PI / 2f + index * MathF.Tau / timerSegments;
                float endAngle = -MathF.PI / 2f + (index + 1.08f) * MathF.Tau / timerSegments;
                Vector2 start = center + new Vector2(MathF.Cos(startAngle), MathF.Sin(startAngle))
                    * timerRadius;
                Vector2 end = center + new Vector2(MathF.Cos(endAngle), MathF.Sin(endAngle))
                    * timerRadius;
                Primitives2D.Line(spriteBatch, start, end,
                    Rainbow(index / (float)timerSegments + (float)_visualTime * .025f), 10);
            }
        }
    }

    /// <summary>
    /// Polar graph-paper paneling over the arena floor -- present the whole
    /// fight at a barely-there intensity so the room reads as a built
    /// structure throughout, not something that appears from nothing. A
    /// survival gate simply intensifies the same rings/spokes into a dull
    /// rainbow rather than conjuring a new decoration: kept dull and
    /// low-alpha even then on purpose, since a vibrant rainbow here would
    /// read as a telegraphed hazard and none of this actually damages the
    /// player.
    /// </summary>
    private void DrawFloorPaneling(SpriteBatch spriteBatch, Vector2 center, bool survivalIntensity)
    {
        float ringAlpha = survivalIntensity ? .22f : .07f;
        float spokeAlpha = survivalIntensity ? .16f : .05f;
        Color baseTone = _wallPalette.Detail;

        const int rings = 5;
        for (int ring = 1; ring <= rings; ring++)
        {
            float radius = ArenaRadius * (ring / (float)(rings + 1));
            Color tint = survivalIntensity
                ? DullRainbow(ring / (float)rings + (float)_visualTime * .015f)
                : baseTone;
            Primitives2D.CircleOutline(spriteBatch, center, radius, tint * ringAlpha, 2, 64);
        }
        const int spokes = 12;
        for (int spoke = 0; spoke < spokes; spoke++)
        {
            float angle = spoke * MathF.Tau / spokes + (float)_visualTime * .01f;
            Vector2 direction = new(MathF.Cos(angle), MathF.Sin(angle));
            Color tint = survivalIntensity
                ? DullRainbow(spoke / (float)spokes + (float)_visualTime * .015f)
                : baseTone;
            Primitives2D.Line(spriteBatch, center, center + direction * ArenaRadius,
                tint * spokeAlpha, 1);
        }
    }

    /// <summary>
    /// Faceted parapet ring: flat panels (not a smooth curve) so the
    /// boundary reads as built from distinct plates, echoing the boss's
    /// cube geometry instead of contrasting with it. Extrudes a cap (the
    /// rim, seen from above) above a ground ring, mirroring the game's
    /// normal room-wall technique (<see cref="ArenaRenderer.VisibleWallFaces"/>)
    /// with the same <see cref="_wallPalette"/> colors, which the arena
    /// previously never touched. Only the near (south-facing) half of the
    /// ring draws its vertical inner face -- the far half's face falls out
    /// of view behind its own cap, same as every other wall in the game.
    /// </summary>
    private void DrawArenaWall(SpriteBatch spriteBatch, Vector2 center)
    {
        Color accent = TrueLight ? new Color(88, 125, 228)
            : TrueDark ? new Color(8, 18, 65)
            : Phase == 4 ? Rainbow((float)_visualTime * .04f)
            : PresentationSurvivalActive ? Color.Lerp(PhaseAccent, DullRainbow((float)_visualTime * .05f), .45f)
            : PhaseAccent;

        for (int index = 0; index <= ArenaWallPanels; index++)
        {
            float angle = index * MathF.Tau / ArenaWallPanels;
            float ocean = MathF.Sin(angle * 7f + (float)_visualTime * .42f) * 8f
                + MathF.Sin(angle * 13f - (float)_visualTime * .21f) * 4f;
            Vector2 ground = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle))
                * (ArenaRadius + ocean);
            _arenaWallGround[index] = ground;
            _arenaWallCap[index] = ground - new Vector2(0, ArenaWallHeight);
        }

        for (int index = 0; index < ArenaWallPanels; index++)
        {
            int next = index + 1;
            float midAngle = (index + .5f) * MathF.Tau / ArenaWallPanels;
            if (MathF.Sin(midAngle) > .05f)
            {
                Primitives2D.FillQuad(spriteBatch,
                    _arenaWallCap[index], _arenaWallCap[next],
                    _arenaWallGround[next], _arenaWallGround[index],
                    _wallPalette.WallFace);
                Primitives2D.Line(spriteBatch, _arenaWallCap[index], _arenaWallGround[index],
                    _wallPalette.WallFace * .6f, 2);
            }
        }

        // The cap ribbon is visible the whole way around -- near or far,
        // you're always looking at the rim from inside the room.
        for (int index = 0; index < ArenaWallPanels; index++)
        {
            int next = index + 1;
            Vector2 start = _arenaWallCap[index];
            Vector2 end = _arenaWallCap[next];
            Primitives2D.Line(spriteBatch, start, end, _wallPalette.WallTop, 12);
            Primitives2D.Line(spriteBatch, start, end, UiTheme.Ink, 6);
            Primitives2D.Line(spriteBatch, start, end, accent, Phase == 4 ? 5 : 3);
        }

        Primitives2D.CircleOutline(spriteBatch, center,
            ArenaRadius + 18f + MathF.Sin((float)_visualTime * .35f) * 6f,
            accent * .42f, 3);
    }

    /// <summary>
    /// A handful of small, slow-drifting wireframe cube fragments in the
    /// void beyond the arena wall -- debris from the same tesseract,
    /// adrift in the dark, giving the boundary a sense of scale instead of
    /// opening onto flat black. Drawn after <see cref="Primitives2D.DrawOutsideArena"/>
    /// so they show up against that mask instead of being painted over.
    /// </summary>
    private void DrawDistantFragments(SpriteBatch spriteBatch, Vector2 center)
    {
        foreach ((float angle, float radiusRatio, float size, float spinSeed) in DistantFragments)
        {
            float drift = (float)_visualTime * .015f;
            Vector2 direction = new(MathF.Cos(angle + drift), MathF.Sin(angle + drift));
            Vector2 at = center + direction * (ArenaRadius * radiusRatio);
            float yaw = spinSeed + (float)_visualTime * .12f;
            float pitch = spinSeed * .7f + (float)_visualTime * .08f;
            DrawDistantFragment(spriteBatch, at, size, yaw, pitch, UiTheme.Purple, .3f);
        }
    }

    private static void DrawDistantFragment(SpriteBatch spriteBatch, Vector2 center,
        float extent, float yaw, float pitch, Color tint, float alpha)
    {
        Vector2[] points = ProjectCube(center, extent, yaw, pitch);
        for (int index = 0; index < CubeFaces.Length; index++)
        {
            int[] face = CubeFaces[index];
            float light = FaceLight(index, yaw, pitch);
            Primitives2D.FillQuad(spriteBatch, points[face[0]], points[face[1]],
                points[face[2]], points[face[3]], tint * (light * .5f * alpha));
        }
        foreach (int[] edge in CubeEdges)
            Primitives2D.Line(spriteBatch, points[edge[0]], points[edge[1]], tint * alpha, 1);
    }

    /// <summary>
    /// Whole-screen mood for the Phase 3 and Phase 4 survival sub-phases
    /// (GrandChoice/MiniExecution/EssenceFinale, and VoidFinale). Two
    /// deliberately gentle layers: a flat, low-alpha dim across the entire
    /// screen, and a long, soft-edged vignette that leans on a slow rainbow
    /// wash rather than darkness for its intensity -- there is no hard ring
    /// anywhere in it, just a wide gradient of thin, faint rings so the
    /// falloff reads as gradual rather than a sharp cutoff.
    /// </summary>
    private void DrawSurvivalScreenMood(SpriteBatch spriteBatch, Rectangle viewport)
    {
        Primitives2D.FillRect(spriteBatch, viewport, UiTheme.Scrim * .16f);

        Vector2 center = new(viewport.Center.X, viewport.Center.Y);
        float outerRadius = MathF.Sqrt(
            viewport.Width * viewport.Width + viewport.Height * viewport.Height) * .5f;
        float innerRadius = outerRadius * (Phase >= 4 ? .3f : .42f);
        float cycleSpeed = Phase >= 4 ? .05f : .03f;
        float maxAlpha = Phase >= 4 ? .1f : .07f;

        const int rings = 14;
        for (int ring = 0; ring < rings; ring++)
        {
            float t = ring / (float)(rings - 1);
            float radius = innerRadius + (outerRadius - innerRadius) * t;
            Color hue = Rainbow((float)_visualTime * cycleSpeed + t * .5f);
            Color muted = Color.Lerp(hue, UiTheme.Void, .3f);
            float alpha = t * t * maxAlpha;
            Primitives2D.CircleOutline(spriteBatch, center, radius, muted * alpha,
                Math.Max(3, (int)(outerRadius * .1f)), 80);
        }
    }

    /// <summary>
    /// The Phase 4 finale's floor-to-cosmos reveal. A transparent hole opens
    /// at the arena's center and grows outward over
    /// <see cref="VoidVortexGrowDuration"/>, replacing the floor within it
    /// with a static void backdrop, a scattering of star points, and a
    /// handful of slowly drifting, desaturated nebula blooms. Driven by
    /// <see cref="_voidVortexProgress"/>, which keeps advancing through the
    /// phase handoff and the death collapse, so the reveal survives past the
    /// end of the survival timer rather than snapping shut with it.
    /// </summary>
    private void DrawVoidVortex(SpriteBatch spriteBatch, Vector2 center)
    {
        if (_voidVortexProgress <= 0f)
            return;
        float radius = ArenaRadius * _voidVortexProgress;

        Primitives2D.FillCircle(spriteBatch, center, radius, new Color(6, 5, 14) * .92f);

        foreach (Vector2 offset in VoidStarField)
        {
            float starRadiusFraction = offset.Length();
            if (starRadiusFraction > _voidVortexProgress)
                continue;
            Vector2 point = center + offset * ArenaRadius;
            float twinkle = .5f + .5f * MathF.Sin(
                (float)_visualTime * 3f + offset.X * 37f + offset.Y * 19f);
            Primitives2D.FillCircle(spriteBatch, point, 1.3f, Color.White * (.35f + .55f * twinkle));
        }

        foreach ((Vector2 offset, float blobRadius, Color tint) in VoidNebulae)
        {
            if (offset.Length() > _voidVortexProgress + blobRadius)
                continue;
            Vector2 drift = new(
                MathF.Sin((float)_visualTime * .05f + offset.Y * 5f),
                MathF.Cos((float)_visualTime * .04f + offset.X * 5f));
            Vector2 point = center + (offset + drift * .015f) * ArenaRadius;
            Color dusty = Color.Lerp(tint, new Color(18, 15, 28), .7f);
            Primitives2D.FillCircle(spriteBatch, point, blobRadius * ArenaRadius, dusty * .16f);
            Primitives2D.FillCircle(spriteBatch, point, blobRadius * ArenaRadius * .55f, dusty * .26f);
        }

        const int arms = 3;
        const int armSegments = 26;
        for (int arm = 0; arm < arms; arm++)
        {
            float armOffset = arm * MathF.Tau / arms + (float)_visualTime * .3f;
            Vector2 previous = center;
            for (int segment = 1; segment <= armSegments; segment++)
            {
                float t = segment / (float)armSegments;
                float angle = armOffset + t * MathF.Tau * 1.4f;
                Vector2 point = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle))
                    * (radius * t);
                Primitives2D.Line(spriteBatch, previous, point,
                    Rainbow(t + (float)_visualTime * .02f) * (.2f * (1f - t * .4f)), 2);
                previous = point;
            }
        }

        Primitives2D.CircleOutline(spriteBatch, center, radius,
            new Color(120, 90, 200) * .35f, 2);
    }

    private void DrawSubphaseDeclaration(SpriteBatch spriteBatch, Vector2 center)
    {
        float progress = Math.Clamp(
            (float)(_subphaseCombatElapsed / SubphaseDeclarationDuration), 0f, 1f);
        float radius = Size * (1.18f - progress * .42f);
        Color accent = Phase <= 2
            ? Color.Lerp(Light.Accent, Dark.Accent, .5f + .5f
                * MathF.Sin((float)_visualTime * 5f))
            : Rainbow((float)_visualTime * .12f + _patternIndex * .11f);
        Primitives2D.CircleOutline(spriteBatch, center, radius,
            UiTheme.Ink, 8, 32);
        Primitives2D.CircleOutline(spriteBatch, center, radius,
            accent, 4, 32);
        int spokes = Phase switch
        {
            1 => 4,
            2 => 5,
            3 => 8,
            _ => 6,
        };
        float rotation = Phase == 2
            ? MathF.Sin((float)_visualTime * 8f) * .22f
            : (float)_visualTime * (Phase >= 3 ? .42f : .18f);
        for (int index = 0; index < spokes; index++)
        {
            float angle = rotation + index * MathF.Tau / spokes;
            Vector2 direction = new(MathF.Cos(angle), MathF.Sin(angle));
            Primitives2D.Line(spriteBatch,
                center + direction * radius * .72f,
                center + direction * radius,
                index % 2 == 0 ? Light.Accent : Dark.Accent,
                3);
        }
    }

    private void DrawPhaseHandoff(SpriteBatch spriteBatch, Vector2 center)
    {
        float flash = .35f + .25f * MathF.Sin((float)_visualTime * 8.5f);
        Color rainbow = Rainbow((float)_visualTime * .18f);
        Color dullRainbow = Color.Lerp(new Color(58, 55, 68), rainbow, .48f);
        Primitives2D.FillCircle(spriteBatch, center, Size * .5f,
            dullRainbow * flash);
        Primitives2D.CircleOutline(spriteBatch, center, Size * .52f,
            UiTheme.Ink, 9);
        Primitives2D.CircleOutline(spriteBatch, center, Size * .52f,
            dullRainbow, 4);

        for (int crack = 0; crack < 9; crack++)
        {
            float angle = crack * MathF.Tau / 9f + .17f;
            Vector2 radial = new(MathF.Cos(angle), MathF.Sin(angle));
            Vector2 tangent = new(-radial.Y, radial.X);
            Vector2[] points =
            [
                center + radial * Size * .08f,
                center + radial * Size * .22f
                    + tangent * MathF.Sin(crack * 2.4f) * Size * .045f,
                center + radial * Size * .36f
                    - tangent * MathF.Cos(crack * 1.7f) * Size * .055f,
                center + radial * Size * .5f,
            ];
            Primitives2D.Polyline(spriteBatch, points, false, UiTheme.Ink, 7);
            Primitives2D.Polyline(spriteBatch, points, false,
                Rainbow(crack / 9f + (float)_visualTime * .12f) * .78f, 3);
        }
    }

    private static Color Rainbow(float phase) => Primitives2D.Rainbow(phase);

    /// <summary>
    /// A darkened, low-saturation cousin of <see cref="Rainbow"/> for
    /// decorative environment theming that must never be mistaken for a
    /// telegraphed attack.
    /// </summary>
    private static Color DullRainbow(float phase, float alpha = 1f) =>
        Color.Lerp(new Color(26, 24, 34), Rainbow(phase), .5f) * alpha;

    /// <summary>
    /// Fixed unit-disc star positions for <see cref="DrawVoidVortex"/>,
    /// seeded once so the field doesn't reshuffle every frame.
    /// </summary>
    private static readonly Vector2[] VoidStarField = BuildVoidStarField(150);

    private static Vector2[] BuildVoidStarField(int count)
    {
        var rng = new Random(1337);
        var stars = new Vector2[count];
        for (int index = 0; index < count; index++)
        {
            float angle = (float)(rng.NextDouble() * MathF.Tau);
            float radius = MathF.Sqrt((float)rng.NextDouble());
            stars[index] = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
        }
        return stars;
    }

    /// <summary>
    /// Fixed dusty rainbow nebula blobs (offset, radius, tint) for
    /// <see cref="DrawVoidVortex"/>, seeded once for the same reason.
    /// </summary>
    private static readonly (Vector2 Offset, float Radius, Color Tint)[] VoidNebulae =
        BuildVoidNebulae();

    private static (Vector2, float, Color)[] BuildVoidNebulae()
    {
        var rng = new Random(7331);
        var nebulae = new (Vector2, float, Color)[6];
        for (int index = 0; index < nebulae.Length; index++)
        {
            float angle = (float)(rng.NextDouble() * MathF.Tau);
            float radius = .25f + (float)rng.NextDouble() * .55f;
            Vector2 offset = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            float blobRadius = .16f + (float)rng.NextDouble() * .14f;
            nebulae[index] = (offset, blobRadius, Rainbow(index / (float)nebulae.Length));
        }
        return nebulae;
    }

    /// <summary>
    /// Fixed (angle, radius ratio beyond the wall, size, spin seed) tuples
    /// for <see cref="DrawDistantFragments"/>, seeded once for the same
    /// reason as the void star field.
    /// </summary>
    private static readonly (float Angle, float RadiusRatio, float Size, float SpinSeed)[] DistantFragments =
        BuildDistantFragments(9);

    private static (float, float, float, float)[] BuildDistantFragments(int count)
    {
        var rng = new Random(4242);
        var fragments = new (float, float, float, float)[count];
        for (int index = 0; index < count; index++)
        {
            fragments[index] = (
                (float)(rng.NextDouble() * MathF.Tau),
                1.15f + (float)rng.NextDouble() * .55f,
                10f + (float)rng.NextDouble() * 14f,
                (float)(rng.NextDouble() * MathF.Tau));
        }
        return fragments;
    }
}
