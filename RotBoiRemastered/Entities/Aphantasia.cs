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
public sealed partial class Aphantasia : Enemy, IBossArenaController, IBossArenaOcclusion, IBossFloorOcclusion
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
    /// For the last stretch of the Void Finale, five long-lasting lasers
    /// hold evenly-spaced directions and sweep together -- <see
    /// cref="FinaleSweepLaserClockwiseDegrees"/> clockwise over <see
    /// cref="FinaleSweepLaserClockwiseDuration"/> seconds, then <see
    /// cref="FinaleSweepLaserCounterclockwiseDegrees"/> back counterclockwise
    /// over <see cref="FinaleSweepLaserCounterclockwiseDuration"/> seconds,
    /// repeating -- so the array continuously precesses (net +90 degrees
    /// clockwise per 7.5s cycle) rather than resetting each cycle.
    /// </summary>
    public const double FinaleSweepLaserWindowDuration = 20.0;
    public const int FinaleSweepLaserCount = 5;
    public const float FinaleSweepLaserClockwiseDegrees = 120f;
    public const double FinaleSweepLaserClockwiseDuration = 5.0;
    public const float FinaleSweepLaserCounterclockwiseDegrees = 30f;
    public const double FinaleSweepLaserCounterclockwiseDuration = 2.5;
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

    /// <summary>
    /// Seconds a movement must have been on screen before it is allowed to
    /// hand over to the next scripted beat. Sits at the bottom of the
    /// authored fifteen-to-twenty-five second phase band the rest of the
    /// bosses use, so steady damage cannot delete a movement any more than a
    /// single burst can.
    /// </summary>
    public const double MinimumGateSeconds = 15.0;
    public const double VoidVortexGrowDuration = 6.0;
    public const double TesseractTransitionDuration = 5.0;
    /// <summary>
    /// Duration of <see cref="DrawVoidTransition"/>'s dark burst marking the
    /// body flipping into or out of its voided monochrome look (see
    /// <see cref="VoidedBodyActive"/>). Deliberately shorter than
    /// <see cref="TesseractTransitionDuration"/>'s full 5s -- this fires on
    /// every Phase 3/4 survival window (and its end), several times per
    /// fight, so a full-length burst would repeatedly stall combat pacing;
    /// 2.2s is enough to read as a real event without doing that.
    /// </summary>
    public const double VoidTransitionDuration = 2.2;
    public const int ProjectileCapacityMultiplier = 5;
    public const int ActiveThreatSoftCap = 320 * ProjectileCapacityMultiplier;
    public const int PerimeterThreatReserve = 24;
    public const double PerimeterPressureCadence = 1.8;
    public const int PerimeterPressureCount = 8;
    public const float MinimumProjectileSizeTiles = .25f;
    /// <summary>
    /// Shots at or above this size, fired during Phase 3+, render as the
    /// expensive tumbling 3D diamond instead of their ordinary flat shape --
    /// reserved for the sparse "giant" attacks (portal seeds, void anchors,
    /// bombs) rather than the dense small-shot volleys, which stay on the
    /// cheap flat 2D render.
    /// </summary>
    public const float LargeShot3DSizeTiles = .5f;
    /// <summary>
    /// An ordinary Phase 3+ subphase occasionally sprinkles in an array of
    /// slow, one-directional persistent lasers -- unlike the Void Finale's
    /// scripted five-armed sweep, the arm count and spin direction are
    /// re-rolled fresh each spawn (see <see cref="PersistentLaserArmCounts"/>)
    /// so "2 opposite each other," "3 at 120 degrees," etc. surface as
    /// incidental variety across a run rather than a single fixed shape.
    /// Because the rotation never reverses, the built-in per-frame
    /// <c>AngularSpeed</c> turn on <see cref="EnemyProjectile"/> drives it
    /// directly -- no boss-held references or manual per-frame steering
    /// needed, unlike <see cref="FinaleSweepLaserWindowDuration"/>'s array.
    /// </summary>
    public const double PersistentLaserCadence = 14.0;
    public const double PersistentLaserLifetime = 11.0;
    public const float PersistentLaserAngularSpeed = .22f;
    public const float PersistentLaserSizeTiles = .38f;
    public static readonly IReadOnlyList<int> PersistentLaserArmCounts = [1, 2, 2, 3, 3, 4, 5];
    /// <summary>
    /// The Blender subphase (Phase 3's "blender" pattern): three long-lasting
    /// lasers pinned to the boss center, spun continuously via a fixed
    /// <see cref="EnemyProjectile.AngularSpeed"/> rather than the manual
    /// per-frame steering the Void Finale's five-armed sweep needs, since
    /// Blender's rotation never reverses direction (see
    /// <see cref="UpdateBlenderLasers"/>). Alongside it, both Minis abandon
    /// their usual aimed-at-player fire for four simultaneous cardinal-axis
    /// streams while orbiting the boss (see <see cref="FireMiniCardinalBlender"/>
    /// and Aphantasia.Minis.cs's dedicated orbit anchor) -- the whole point is
    /// a single readable shape instead of another aimed-ring/curtain remix.
    /// </summary>
    public const int BlenderLaserCount = 3;
    public const float BlenderLaserAngularSpeed = .11f;
    public const float BlenderLaserSizeTiles = .3f;
    public const float BlenderMiniOrbitRadiusRatio = .5f;
    public const float BlenderMiniOrbitSpeed = .15f;
    public const float BlenderMiniStreamCadence = .5f;
    public const float BlenderMiniStreamSpeed = .5f;
    public const float BlenderMiniStreamSizeTiles = .22f;
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
        new("blender", "BLENDER", AphantasiaMovementMode.Standing, AphantasiaSpecialAttack.None),
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
    // Sized for the fixed light/dark/body trio plus Phase 4's eight
    // persistent void tentacles (the largest of the two tentacle counts) --
    // reused as a List rather than a fixed array now that the tentacle
    // count varies with phase.
    private readonly List<(string Part, Rectangle Rect)> _worldHitboxes = new(11);
    private readonly List<(string Part, Rectangle Rect)> _screenHitboxes = new(11);
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
    private double _persistentLaserRemaining = 6.0;
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
    /// <summary>Last frame's <see cref="VoidedBodyActive"/>, checked once per frame in <see cref="Update"/> to catch the flip and arm <see cref="_voidTransitionRemaining"/>.</summary>
    private bool _wasVoidedBodyActive;
    private double _voidTransitionRemaining;
    /// <summary>True while <see cref="_voidTransitionRemaining"/> is counting down a flip into the voided look; false while counting down a flip back out of it.</summary>
    private bool _voidTransitionEntering;
    /// <summary>
    /// Subphases played since the current phase began (0 for the first
    /// subphase of the phase). Drives <see cref="PerimeterPressureRampCount"/>
    /// and <see cref="HalfPressureRampMultiplier"/> so Phase 3 and Phase 4's
    /// always-on ambient pressure fades in across each phase's opening
    /// subphases instead of switching on at full intensity the instant the
    /// phase starts.
    /// </summary>
    private int _subphasesSincePhaseStart = -1;
    /// <summary>
    /// Live references to the Void Finale's five sweeping lasers, held
    /// directly rather than re-fired each frame so their <c>Direction</c>
    /// can be driven continuously without the telegraph/re-arm flicker a
    /// fresh <see cref="FireAphantasiaLaser"/>-style call would cause. Slots
    /// are null outside the finale's closing window.
    /// </summary>
    private readonly EnemyProjectile?[] _finaleSweepLasers = new EnemyProjectile?[FinaleSweepLaserCount];
    private bool _finaleSweepLasersActive;
    private double _finaleSweepElapsed;
    /// <summary>Live references to the Blender pattern's three spinning lasers -- see <see cref="BlenderLaserCount"/>'s doc comment.</summary>
    private readonly EnemyProjectile?[] _blenderLasers = new EnemyProjectile?[BlenderLaserCount];
    private bool _blenderActive;

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
    /// <summary>
    /// True specifically during Phase 3's Survival(GrandChoice) ->
    /// MiniExecution -> Finale(EssenceFinale) window and Phase 4's
    /// Survival(VoidEclipse) -> Finale(VoidFinale) window --
    /// <see cref="PresentationSurvivalActive"/> alone would also cover an
    /// earlier phase's mini eclipse survival windows, which stay in the
    /// ordinary rainbow palette; only Phase 3+ switches the body's own
    /// render into the monochrome voided look (<see cref="VoidTone"/>) via
    /// <see cref="DrawBossBody"/> and <see cref="DrawMini"/>'s Empowered
    /// glow, entered/exited through <see cref="DrawVoidTransition"/>.
    /// </summary>
    private bool VoidedBodyActive => PresentationSurvivalActive && Phase >= 3;
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
    /// <summary>
    /// Debug and test hook: satisfies the minimum on-screen hold so the next
    /// health gate resolves immediately instead of shielding first.
    /// </summary>
    public void DebugCompleteGateHold() =>
        _subphaseCombatElapsed = Math.Max(_subphaseCombatElapsed, MinimumGateSeconds);

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

        // Checked once per frame regardless of EncounterState (same reason
        // as the void vortex growth just above) so the transition burst can
        // still play out even if this frame's state branch returns early --
        // e.g. the flip out of the voided look lands exactly on the frame
        // Finale hands off into Transforming.
        bool voidedBodyActive = VoidedBodyActive;
        if (voidedBodyActive != _wasVoidedBodyActive)
        {
            _voidTransitionRemaining = VoidTransitionDuration;
            _voidTransitionEntering = voidedBodyActive;
            _wasVoidedBodyActive = voidedBodyActive;
        }
        else if (_voidTransitionRemaining > 0)
        {
            _voidTransitionRemaining = Math.Max(0, _voidTransitionRemaining - dt);
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
            UpdatePersistentRotatingLaser(context, dt);
            UpdateBlenderLasers(context, dt);
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
        UpdateFinaleSweepLasers(context, dt);
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

        EndFinaleSweepLasers();
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

    /// <summary>
    /// Drives the Void Finale's closing five-laser sweep: spawns the array
    /// once <see cref="SurvivalRemaining"/> enters the last <see
    /// cref="FinaleSweepLaserWindowDuration"/> seconds, then rotates every
    /// arm in lockstep each frame. The sweep angle accumulates across cycles
    /// (see <see cref="FinaleSweepLaserClockwiseDegrees"/>) instead of
    /// resetting, so the back-and-forth motion is continuous.
    /// </summary>
    private void UpdateFinaleSweepLasers(EnemyUpdateContext context, double dt)
    {
        bool eligible = SurvivalKind == AphantasiaSurvivalKind.VoidFinale
            && SurvivalRemaining > 0
            && SurvivalRemaining <= FinaleSweepLaserWindowDuration;
        if (!eligible)
        {
            if (_finaleSweepLasersActive)
                EndFinaleSweepLasers();
            return;
        }
        if (!_finaleSweepLasersActive)
            BeginFinaleSweepLasers(context);

        _finaleSweepElapsed += dt;
        const double cycleDuration = FinaleSweepLaserClockwiseDuration
            + FinaleSweepLaserCounterclockwiseDuration;
        double cyclePos = _finaleSweepElapsed % cycleDuration;
        int fullCycles = (int)(_finaleSweepElapsed / cycleDuration);
        const float netDegreesPerCycle = FinaleSweepLaserClockwiseDegrees
            - FinaleSweepLaserCounterclockwiseDegrees;
        float withinCycleDegrees = cyclePos < FinaleSweepLaserClockwiseDuration
            ? FinaleSweepLaserClockwiseDegrees
                * (float)(cyclePos / FinaleSweepLaserClockwiseDuration)
            : FinaleSweepLaserClockwiseDegrees
                - FinaleSweepLaserCounterclockwiseDegrees
                    * (float)((cyclePos - FinaleSweepLaserClockwiseDuration)
                        / FinaleSweepLaserCounterclockwiseDuration);
        // Positive degrees reads as clockwise on screen: this engine's world
        // Y axis increases downward, so an increasing atan2 angle sweeps
        // clockwise rather than the counterclockwise sense it would have in
        // a standard Y-up math convention.
        float sweepAngle = MathHelper.ToRadians(
            fullCycles * netDegreesPerCycle + withinCycleDegrees);
        for (int index = 0; index < _finaleSweepLasers.Length; index++)
        {
            EnemyProjectile? laser = _finaleSweepLasers[index];
            if (laser is null || laser.RemFlag)
                continue;
            laser.Direction = sweepAngle + index * MathF.Tau / FinaleSweepLaserCount;
        }
    }

    private void BeginFinaleSweepLasers(EnemyUpdateContext context)
    {
        _finaleSweepLasersActive = true;
        _finaleSweepElapsed = 0;
        List<EnemyProjectile> staged = BeginVolley();
        Vector2 origin = ArenaCenter;
        for (int index = 0; index < FinaleSweepLaserCount; index++)
        {
            float direction = index * MathF.Tau / FinaleSweepLaserCount;
            var laser = new EnemyProjectile(
                origin.X, origin.Y, direction, 0f,
                Damage * .85f, Simulation.TileSize * .5f,
                travelRange: ArenaRadius * 2.1f,
                color: Rainbow(index / (float)FinaleSweepLaserCount),
                shape: "diamond", path: "laser",
                lifetime: (float)(FinaleSweepLaserWindowDuration + 3.0),
                owner: "aphantasia_finale_sweep_laser",
                longLastingLaser: true)
            {
                TelegraphDuration = 1.8f,
            };
            _finaleSweepLasers[index] = laser;
            staged.Add(laser);
        }
        CommitVolley(context.ProjectileSink);
    }

    private void EndFinaleSweepLasers()
    {
        for (int index = 0; index < _finaleSweepLasers.Length; index++)
        {
            if (_finaleSweepLasers[index] is { } laser)
                laser.RemFlag = true;
            _finaleSweepLasers[index] = null;
        }
        _finaleSweepLasersActive = false;
        _finaleSweepElapsed = 0;
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

    private void BeginPhaseHandoff()
    {
        _phaseHandoffRemaining = Math.Max(
            _phaseHandoffRemaining,
            PhaseHandoffDuration);
        MilestoneHealRequested = true;
        // Sweep the outgoing phase's shots off the screen rather than
        // leaving them to expire on their own authored lifetimes -- the
        // handoff's own camera settle reads as a clean beat, not one still
        // littered with the last phase's danger.
        TransitionSweepRequested = true;
        // Those swept shots accelerate away and are close to undodgeable, so
        // the handoff carries the player's grace for its whole length.
        PhaseInterludeInvulnerabilitySeconds = PhaseHandoffDuration;
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
        _subphasesSincePhaseStart++;
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
            // Two separate holds, both resolved through _pendingGate.
            //
            // A single hit big enough to blow past the cap for the active bar
            // shields the boss for DamageCapInvincibilityDuration so a burst
            // nuke can't skip straight through a scripted beat.
            //
            // Accumulated damage is held too: a movement never hands over
            // before it has been on screen for MinimumGateSeconds, so a player
            // who chews through a bar with steady damage still has to dodge
            // the movement rather than deleting it.
            double capFraction = Phase <= 2 ? DamageCapSharedPhaseFraction : DamageCapSoloPhaseFraction;
            bool burstNuke = requested >= _barMaxHp * capFraction;
            double remainingHold = Math.Max(0, MinimumGateSeconds - _subphaseCombatElapsed);
            if (burstNuke || remainingHold > 0)
            {
                _burstShieldRemaining = burstNuke
                    ? Math.Max(DamageCapInvincibilityDuration, remainingHold)
                    : remainingHold;
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
        _subphasesSincePhaseStart = -1;
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

    /// <summary>
    /// Every key the debug console's `/testphase` command can jump this boss
    /// straight to: one entry per authored combat pattern (see
    /// <see cref="AllPatterns"/>) plus the named survival/finale sequences and
    /// the mini-execution beat, keyed and labelled for
    /// <see cref="RotBoiRemastered.UI.DevConsole"/>'s autocomplete list.
    /// </summary>
    public static IReadOnlyList<(string Key, string Label)> DebugTestPhaseKeys { get; } =
        AllPatterns.Select(pattern => (pattern.Key, pattern.Label))
            .Concat(new (string, string)[]
            {
                ("survival_phase1", "SURVIVAL: FIRST ECLIPSE"),
                ("survival_phase2", "SURVIVAL: SECOND ECLIPSE"),
                ("survival_phase3", "SURVIVAL: GRAND CHOICE"),
                ("survival_phase4", "SURVIVAL: VOID ECLIPSE"),
                ("finale_phase3", "FINALE: ESSENCE FINALE"),
                ("finale_phase4", "FINALE: VOID FINALE"),
                ("mini_execution", "MINI EXECUTION"),
            })
            .ToArray();

    /// <summary>
    /// Jumps straight to the pattern or sequence named by <paramref name="key"/>
    /// (one of <see cref="DebugTestPhaseKeys"/>) -- backing
    /// <see cref="RotBoiRemastered.Systems.GameSession.DebugJumpToTestPhase"/>.
    /// A recognized pattern key reuses <see cref="DebugSetPhase"/> to land on
    /// that pattern's phase, then walks <see cref="DebugAdvanceSubPhase"/>
    /// forward (same technique the test suite's own SelectPattern helper
    /// uses) until the shuffled bag lands on it -- at most one full cycle of
    /// that phase's pattern count, and pure bookkeeping with no projectiles
    /// fired, so it is instant and side-effect-free from the caller's view.
    /// Returns false for an unrecognized key.
    /// </summary>
    public bool DebugJumpToTestPhase(string key)
    {
        AphantasiaPattern? pattern = AllPatterns.FirstOrDefault(candidate =>
            string.Equals(candidate.Key, key, StringComparison.OrdinalIgnoreCase));
        if (pattern is not null)
        {
            int phase = PhaseOnePatterns.Contains(pattern) ? 1
                : PhaseTwoPatterns.Contains(pattern) ? 2
                : PhaseThreePatterns.Contains(pattern) ? 3
                : 4;
            DebugSetPhase(phase);
            for (int attempt = 0; attempt < PatternSelectionCycleCount(phase); attempt++)
            {
                if (CurrentPattern.Key == pattern.Key)
                    return true;
                DebugAdvanceSubPhase();
            }
            return CurrentPattern.Key == pattern.Key;
        }

        switch (key.ToLowerInvariant())
        {
            case "survival_phase1":
                DebugSetPhase(1);
                BeginSurvival(AphantasiaSurvivalKind.FirstEclipse, EarlySurvivalDuration);
                return true;
            case "survival_phase2":
                DebugSetPhase(2);
                BeginSurvival(AphantasiaSurvivalKind.SecondEclipse, EarlySurvivalDuration);
                return true;
            case "survival_phase3":
                DebugSetPhase(3);
                BeginSurvival(AphantasiaSurvivalKind.GrandChoice, PhaseThreeSurvivalDuration);
                return true;
            case "survival_phase4":
                DebugSetPhase(4);
                BeginSurvival(AphantasiaSurvivalKind.VoidEclipse, PhaseFourSurvivalDuration);
                return true;
            case "finale_phase3":
                DebugSetPhase(3);
                BeginFinale(AphantasiaSurvivalKind.EssenceFinale, PhaseThreeSurvivalDuration);
                return true;
            case "finale_phase4":
                DebugSetPhase(4);
                BeginFinale(AphantasiaSurvivalKind.VoidFinale, PhaseFourFinaleDuration);
                return true;
            case "mini_execution":
                // Mirrors EndSurvival's GrandChoice completion (one mini
                // destroyed, the other empowered) so the beat's own
                // preconditions hold without having to actually play the
                // Grand Choice survival out first.
                DebugSetPhase(3);
                Dark.PermanentlyDestroyed = true;
                Dark.Hp = 0;
                Light.Empowered = true;
                Light.MaxHp = EmpoweredMiniHealth;
                Light.Hp = Light.MaxHp;
                Light.Aggressive = true;
                _phaseThreeChoiceDone = true;
                BeginMiniExecution();
                return true;
            default:
                return false;
        }
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
        _worldHitboxes.Clear();
        _worldHitboxes.Add(("light", Light.Alive
            ? CenteredRect(Light.Position, miniSize) : Rectangle.Empty));
        _worldHitboxes.Add(("dark", Dark.Alive
            ? CenteredRect(Dark.Position, miniSize) : Rectangle.Empty));
        _worldHitboxes.Add(("body", WorldRect()));
        AddPersistentTentacleHitboxes(_worldHitboxes, BossCenter);
        return _worldHitboxes;
    }

    public override IReadOnlyList<(string Part, Rectangle Rect)> GetScreenHitboxes(
        Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        float miniSize = MiniSize;
        Vector2 light = camera.WorldToScreen(Light.Position, playerWorldPosition, screenShake);
        Vector2 dark = camera.WorldToScreen(Dark.Position, playerWorldPosition, screenShake);
        Vector2 body = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        Vector2 center = camera.WorldToScreen(BossCenter, playerWorldPosition, screenShake);
        _screenHitboxes.Clear();
        _screenHitboxes.Add(("light", Light.Alive
            ? CenteredRect(light, miniSize) : Rectangle.Empty));
        _screenHitboxes.Add(("dark", Dark.Alive
            ? CenteredRect(dark, miniSize) : Rectangle.Empty));
        _screenHitboxes.Add(("body", new Rectangle((int)body.X, (int)body.Y, (int)Size, (int)Size)));
        AddPersistentTentacleHitboxes(_screenHitboxes, center);
        return _screenHitboxes;
    }

    /// <summary>
    /// Phase 3's four and Phase 4's eight persistent void tentacles
    /// (<see cref="DrawPersistentTentacles"/>) are part of the boss's body
    /// now, not just decoration -- they hurt on contact exactly like the
    /// "body" hitbox above, using the same <see cref="Enemy.Damage"/> the
    /// rest of Aphantasia already deals. Each tentacle's hitbox is a
    /// generous axis-aligned box around its straight reach from
    /// <paramref name="center"/> out to its tip (the same
    /// <see cref="PersistentTentacleLayout"/> the draw call uses), not a
    /// precise trace of its cosmetic wiggle -- a forgiving box is the same
    /// tradeoff every other rectangular hitbox in this file already makes.
    /// </summary>
    private void AddPersistentTentacleHitboxes(
        List<(string Part, Rectangle Rect)> sink, Vector2 center)
    {
        var layout = PersistentTentacleLayout();
        for (int index = 0; index < layout.Length; index++)
        {
            (float baseAngle, float length, float width) = layout[index];
            Vector2 direction = new(MathF.Cos(baseAngle), MathF.Sin(baseAngle));
            sink.Add(($"tentacle_{index}",
                SegmentRect(center, center + direction * length, width)));
        }
    }

    private static Rectangle SegmentRect(Vector2 a, Vector2 b, float width)
    {
        float minX = Math.Min(a.X, b.X) - width, maxX = Math.Max(a.X, b.X) + width;
        float minY = Math.Min(a.Y, b.Y) - width, maxY = Math.Max(a.Y, b.Y) + width;
        return new Rectangle((int)minX, (int)minY,
            Math.Max(1, (int)(maxX - minX)), Math.Max(1, (int)(maxY - minY)));
    }

    private static Rectangle CenteredRect(Vector2 center, float size) =>
        new((int)(center.X - size / 2f), (int)(center.Y - size / 2f),
            Math.Max(1, (int)size), Math.Max(1, (int)size));

}
