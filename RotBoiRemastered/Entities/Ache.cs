using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.Systems;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// A brittle or reinforced terrain obstacle left by an Ache aftershock.
/// Ported from bossTypes.py's per-instance `crystalWalls` dict literals --
/// a small mutable class instead, since `Remaining`/`Warning`/`Rect` mutate
/// every frame (see <see cref="RunState.BossAfflictions"/> for the same
/// mutable-class-over-dict reasoning).
/// </summary>
public sealed class CrystalWall
{
    public Rectangle Rect;
    public float CenterX;
    public float CenterY;
    public double Remaining;
    public double Duration;
    public float Angle;
    public string Kind = "brittle";
    public double? Hp;
    public double Warning;
    public bool Compression;
    /// <summary>Purely cosmetic: a brief flash when a neighboring wall breaks, so the remaining walls visibly react rather than just the one that broke.</summary>
    public double ReactionFlash;
}

/// <summary>Ported from bossTypes.py's per-instance `cleansingVents` dict literals.</summary>
public sealed class CleansingVent
{
    public float X;
    public float Y;
    public float Angle;
    public double Cooldown;
    public double Flash;
}

/// <summary>
/// Chemesthesis's uncommanded collision-born core. Ache never presents a stable
/// rotation: each attack is chosen independently, but every heavy hit has a
/// telegraph and its unaimed mistakes travel slowly enough to react to.
/// </summary>
public sealed class Ache : Kage
{
    public const int OrbitingArmCount = 3;
    public const float MineDamage = 190;
    public const float FieldDamage = 195;
    public const float RingDamage = 200;
    public const float BombDamage = 205;
    public const float HeavyDamage = 230;
    public const int ActiveThreatSoftCap = 36;
    public const int PersistentThreatSoftCap = 20;
    public const int MinimumDamagePhaseDeclarations = 2;
    public const int NerveBreaksNeeded = 3;
    public const int OverloadConstellationMaxNodes = 12;
    protected override bool UsesKageEncounter => false;
    protected override bool EncounterSurvivalActive => MidpointSurvivalActive;

    /// <summary>
    /// Every retaliation Ache owns, drawn from throughout the fight. The
    /// reflex storm (4) and the overload (8) are the fixed points.
    /// </summary>
    private static readonly int[] DamagePhasePool = { 1, 2, 3, 5, 6, 7 };

    protected override BossInterludeStyle InterludeStyle => BossInterludeStyle.Recoil;

    protected override double PhaseTimeLimitFor(int phase) => phase switch
    {
        1 => 17.0,
        2 => 19.0,
        3 => 18.0,
        5 => 21.0,
        6 => 23.0,
        7 => 24.0,
        _ => 20.0,
    };

    protected override bool VisualSurvivalActive => MidpointSurvivalActive || FinaleActive || base.VisualSurvivalActive;

    public static readonly PathChaseBossConfig AcheConfig = KageConfig with
    {
        BossName = "ACHE", Subtitle = "THE UNCOMMANDED CORE", FinalBoss = true,
        OwnerPrefix = "ache_chemesthesis",
        FinalBodyColor = new Color(232, 112, 31), FinalAccentColor = new Color(54, 143, 218),
        FinalBodyScale = 1.6, FinalCooldownSeconds = 1.8, FinalShotSpeed = .34, FinalShotScale = .27,
        MovementSpeed = .21,
        MovementPhases = new[]
        {
            BossMovementPhaseProfile.Chase(),
            BossMovementPhaseProfile.Fixed(BossPathShape.Jagged, 9f, .58f, .52f),
            BossMovementPhaseProfile.Chase(1.18f),
            BossMovementPhaseProfile.Stationary(),
            BossMovementPhaseProfile.Fixed(BossPathShape.Jagged, 8f, .64f, .58f),
            BossMovementPhaseProfile.Chase(1.28f),
            BossMovementPhaseProfile.Fixed(BossPathShape.Jagged, 7f, .68f, .62f, -1),
            BossMovementPhaseProfile.Stationary(),
        },
        PhaseLabels = new[]
        {
            "TRESPASS", "RECOIL", "FALSE ALARM", "PROVOCATION",
            "SPLINTER", "REFLEX STORM", "OVERREACTION", "OVERLOAD",
        },
        FinalHealth = 305000, FinalContactDamage = 880, FinalRewardExperience = 840,
        FinaleDuration = 25.0,
    };

    public static readonly SinSigilConfig AcheSinConfig = new(
        PhaseFlavors: new[]
        {
            "Ache answers an attacker that was never there.", "Three arms dispute where the border should be.",
            "The core recoils from a future that never happened.", "No command survives contact with the storm.",
            "Unclaimed ground is punished for trespass.", "Power splits wherever obedience should begin.",
            "Every warning points toward a different phantom.", "Power gathers around a throne with no master.",
        },
        PhaseColors: new[]
        {
            new Color(232, 122, 36), new Color(57, 146, 218), new Color(82, 176, 228), new Color(244, 226, 174),
            new Color(209, 72, 45), new Color(65, 129, 214), new Color(207, 234, 240), new Color(232, 86, 32),
        },
        SinSigils: new (string, Vector2[][])[]
        {
            ("PHANTOM", new[]
            {
                new[]
                {
                    new Vector2(-.72f, .52f), new Vector2(-.58f, -.22f), new Vector2(-.25f, .08f), new Vector2(0, -.72f),
                    new Vector2(.25f, .08f), new Vector2(.58f, -.22f), new Vector2(.72f, .52f),
                },
                new[] { new Vector2(-.58f, .28f), new Vector2(.58f, .28f) },
                new[] { new Vector2(0, -.72f), new Vector2(0, .68f) },
            }),
            ("BORDER", new[]
            {
                new[]
                {
                    new Vector2(0, -.74f), new Vector2(.62f, -.18f), new Vector2(.42f, .58f), new Vector2(0, .74f),
                    new Vector2(-.42f, .58f), new Vector2(-.62f, -.18f), new Vector2(0, -.74f),
                },
                new[] { new Vector2(-.42f, -.06f), new Vector2(0, .28f), new Vector2(.42f, -.06f) },
                new[] { new Vector2(0, -.42f), new Vector2(0, .74f) },
            }),
            ("RECOIL", new[]
            {
                new[]
                {
                    new Vector2(0, .72f), new Vector2(-.68f, -.04f), new Vector2(-.42f, -.6f), new Vector2(0, -.22f),
                    new Vector2(.42f, -.6f), new Vector2(.68f, -.04f), new Vector2(0, .72f),
                },
                new[] { new Vector2(-.72f, 0), new Vector2(.72f, 0) },
            }),
            ("REFLEX", new[]
            {
                new[]
                {
                    new Vector2(-.74f, 0), new Vector2(-.36f, -.42f), new Vector2(0, 0),
                    new Vector2(-.36f, .42f), new Vector2(-.74f, 0),
                },
                new[]
                {
                    new Vector2(.74f, 0), new Vector2(.36f, -.42f), new Vector2(0, 0),
                    new Vector2(.36f, .42f), new Vector2(.74f, 0),
                },
                new[] { new Vector2(-.36f, 0), new Vector2(.36f, 0) },
            }),
            ("TRESPASS", new[]
            {
                new[] { new Vector2(-.7f, -.34f), new Vector2(-.34f, -.68f), new Vector2(.34f, -.68f), new Vector2(.7f, -.34f) },
                new[] { new Vector2(-.7f, .34f), new Vector2(-.34f, .68f), new Vector2(.34f, .68f), new Vector2(.7f, .34f) },
                new[] { new Vector2(-.7f, -.34f), new Vector2(-.28f, 0), new Vector2(-.7f, .34f) },
                new[] { new Vector2(.7f, -.34f), new Vector2(.28f, 0), new Vector2(.7f, .34f) },
            }),
            ("SPLINTER", new[]
            {
                new[] { new Vector2(-.58f, -.7f), new Vector2(.1f, -.08f), new Vector2(-.18f, .08f), new Vector2(.58f, .7f) },
                new[] { new Vector2(.58f, -.7f), new Vector2(-.1f, -.08f), new Vector2(.18f, .08f), new Vector2(-.58f, .7f) },
                new[] { new Vector2(-.72f, 0), new Vector2(.72f, 0) },
            }),
            ("STATIC", new[]
            {
                new[]
                {
                    new Vector2(-.62f, -.56f), new Vector2(.48f, -.56f), new Vector2(.48f, .34f), new Vector2(-.28f, .34f),
                    new Vector2(-.28f, -.1f), new Vector2(.14f, -.1f), new Vector2(.14f, .06f),
                },
                new[] { new Vector2(0, -.76f), new Vector2(0, -.56f) },
                new[] { new Vector2(-.48f, .62f), new Vector2(0, .76f), new Vector2(.48f, .62f) },
            }),
            ("UNBOUND", new[]
            {
                new[] { new Vector2(-.72f, -.62f), new Vector2(.62f, .72f) },
                new[] { new Vector2(.72f, -.62f), new Vector2(-.62f, .72f) },
                new[] { new Vector2(-.7f, .12f), new Vector2(-.18f, -.18f), new Vector2(.22f, .22f), new Vector2(.7f, -.12f) },
            }),
        },
        ActMetadata: new Dictionary<int, string> { [4] = "REFLEX STORM", [5] = "ACT II // UNCLAIMED GROUND", [8] = "OVERLOAD" });

    private readonly List<CrystalWall> _crystalWalls = new();
    private readonly List<CleansingVent> _cleansingVents = new();
    private readonly List<Rectangle> _movementObstacleScratch = new(6);
    private readonly List<(string Part, Rectangle Rect)> _screenHitboxScratch =
        new(7);
    private double _compressionCooldown = 5.0;
    private double _consumedCrystalPulse;
    private int _lastPattern = -1;
    private int _castsSinceDirectedThreat;
    private readonly List<int> _patternHistory = new();
    private readonly List<ReactiveCounter> _reactiveCounters = new();
    private float _flinchDirection;
    private double _flinchRemaining;
    private bool _overreactionCascadePending;
    private float _facingYaw;

    private readonly record struct ReactiveCounter(double Delay, float Direction, float Damage, string Suffix);

    public IReadOnlyList<CrystalWall> CrystalWalls => _crystalWalls;
    public IReadOnlyList<CleansingVent> CleansingVents => _cleansingVents;
    public IReadOnlyList<int> PatternHistory => _patternHistory;
    public int VentsUsed { get; private set; }
    public double PeakExposure { get; private set; }
    public int PhaseDeclarations { get; private set; }
    public int NerveBreakProgress { get; private set; }
    public int NerveBreakTriggers { get; private set; }
    public int OverloadConstellationNodeCount => FinaleActive
        ? Math.Min(OverloadConstellationMaxNodes,
            3 + (int)(FinaleProgress * (OverloadConstellationMaxNodes - 3)))
        : 0;
    public double CrystalBreakPulse => _consumedCrystalPulse;
    protected override double ConsumedCrystalPulse => _consumedCrystalPulse;
    public override double MaxStagger => 140.0;
    protected override double StaggerDecayDelay => 2.5;
    protected override double StaggerDecayPerSecond => 10.0;

    public bool MidpointSurvivalActive { get; private set; }
    public bool MidpointSurvivalCleared { get; private set; }
    public double MidpointSurvivalDuration { get; } = 20.0;
    public double MidpointSurvivalRemaining { get; private set; }
    private double _survivalCooldown;

    public Ache(float worldX, float worldY, Battleground battleground, Random? rng = null)
        : base(worldX, worldY, battleground, AcheConfig, AcheSinConfig, rng)
    {
        ActTitle = "ACT I // GHOST THREAT";
        ActTransitionTimer = ActTransitionDuration;
        PhaseProtectionTimer = ActTransitionDuration;
        for (int index = 0; index < 4; index++)
        {
            float angle = MathF.PI / 4f + index * MathF.PI / 2f;
            var point = ArenaCenter + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * ArenaRadius * .68f;
            _cleansingVents.Add(new CleansingVent { X = point.X, Y = point.Y, Angle = angle });
        }
    }

    /// <summary>
    /// The chemesthesis base re-clamps health to this floor after applying a
    /// hit, which is what stops the stagger multiplier from overshooting.
    /// Ache reports its live phase budget here rather than a per-phase-index
    /// ratio -- an index ladder cannot express a rotation that revisits
    /// retaliations, and the old one clamped health back *up* whenever the
    /// rotation landed on a low-numbered movement.
    /// </summary>
    protected override double DamageFloorRatio()
    {
        if (MidpointSurvivalActive || FinaleActive || Dying || DebugPhaseLocked)
            return 0.0;
        int nextGate = MidpointSurvivalCleared
            ? 1
            : Math.Max(1, (int)Math.Round(MaxHp * .5));
        return (double)PhaseGovernor.DamageFloor(nextGate) / Math.Max(1, MaxHp);
    }

    private void BeginMidpointSurvival()
    {
        if (MidpointSurvivalActive || MidpointSurvivalCleared)
            return;
        Hp = Math.Max(1, (int)Math.Round(MaxHp * .5));
        SetSinPhase(4);
        MidpointSurvivalActive = true;
        MidpointSurvivalRemaining = MidpointSurvivalDuration;
        _survivalCooldown = .25;
        TransitionCleanupRequested = true;
    }

    protected override void UpdatePhase()
    {
        if (DebugPhaseLocked || FinaleActive || MidpointSurvivalActive || Dying)
            return;
        // Half health is the one health-driven transition left; retaliations
        // otherwise rotate on the phase clock, so provoking Ache no longer
        // lets the player skip past a reaction they were meant to read.
        if (!MidpointSurvivalCleared && Hp <= MaxHp * .5)
        {
            BeginMidpointSurvival();
            return;
        }
        if (PhaseGovernor.ReadyToAdvance)
            SetSinPhase(PhaseRotation.Choose(DamagePhasePool, Phase, Rng));
    }

    public override void DebugSetPhase(int phase)
    {
        phase = Math.Clamp(phase, 1, 8);
        DebugPhaseLocked = true;
        MidpointSurvivalActive = false;
        if (phase >= 5)
            MidpointSurvivalCleared = true;
        SetSinPhase(phase);
        AttackCooldown = 0f;
        if (phase == 4)
        {
            MidpointSurvivalActive = true;
            MidpointSurvivalRemaining = MidpointSurvivalDuration;
            _survivalCooldown = 0;
        }
        else if (phase == 8 && !FinaleActive)
        {
            BeginFinaleSequence();
        }
    }

    public override HitResult TakeDamage(double amount, string partId = "body", DamageSource source = DamageSource.Direct)
    {
        if (MidpointSurvivalActive || FinaleActive || Dying)
            return new HitResult(false, false, 0, true);
        if (partId.StartsWith("crystal:"))
            return base.TakeDamage(amount, partId, source);

        // A retaliation surrenders at most its damage budget, bounded by the
        // next authored gate -- half health before the reflex storm, one
        // afterwards. Reaching the floor no longer advances anything: the
        // phase clock owns that.
        int nextGate = MidpointSurvivalCleared
            ? 1
            : Math.Max(1, (int)Math.Round(MaxHp * .5));
        int floor = PhaseGovernor.DamageFloor(nextGate);
        double permitted = Math.Max(0, Hp - floor);
        if (permitted <= 0)
            return new HitResult(false, false, 0, true);

        int healthBefore = Hp;
        var result = base.TakeDamage(Math.Min(amount, permitted), partId, source);
        PhaseGovernor.RecordDamage(healthBefore - Hp);
        if (!MidpointSurvivalCleared && Hp <= MaxHp * .5)
            BeginMidpointSurvival();
        else if (MidpointSurvivalCleared && Hp <= 1 && !FinaleActive)
            BeginFinaleSequence();
        if (FinaleActive)
            SetSinPhase(8);
        return new HitResult(result.Applied, false, result.Amount, result.Blocked);
    }

    public override void Update(EnemyUpdateContext context)
    {
        double dt = Seconds();
        if (MovementProfile.Mode is BossMovementMode.Chase or BossMovementMode.FixedPath)
            _facingYaw = BossFacing.SmoothFacingYaw(_facingYaw, Center(),
                new Vector2(context.PlayerWorldX, context.PlayerWorldY), dt);
        UpdateReactiveCounters(context.ProjectileSink, dt);
        _flinchRemaining = Math.Max(0.0, _flinchRemaining - dt);
        if (_overreactionCascadePending)
        {
            FireOverreactionCascade(context.ProjectileSink);
            _overreactionCascadePending = false;
        }
        if (!MidpointSurvivalActive)
        {
            base.Update(context);
            return;
        }

        // This branch never reaches base.Update, so the shared phase clock has
        // to be advanced here or it would freeze for the whole survival.
        TickEncounterClock(dt);
        EntranceRemaining = Math.Max(0.0, EntranceRemaining - dt);
        VisualTransitionRemaining = Math.Max(0.0, VisualTransitionRemaining - dt);
        ActTransitionTimer = Math.Max(0.0, ActTransitionTimer - dt);
        PhaseProtectionTimer = Math.Max(0.0, PhaseProtectionTimer - dt);
        PhaseElapsed += dt;
        ArenaRingSeconds += dt;
        AdvanceAge();
        MidpointSurvivalRemaining = Math.Max(0.0, MidpointSurvivalRemaining - dt);
        _survivalCooldown -= dt;
        if (EntranceRemaining <= 0 && ActTransitionTimer <= 0 && _survivalCooldown <= 0)
        {
            FireSinPattern(context.PlayerWorldX, context.PlayerWorldY, context);
            double elapsed = MidpointSurvivalDuration - MidpointSurvivalRemaining;
            _survivalCooldown = elapsed < MidpointSurvivalDuration * .5
                ? 1.62 + Rng.NextDouble() * .34
                : 1.38 + Rng.NextDouble() * .30;
        }
        if (MidpointSurvivalRemaining <= 0 && !DebugPhaseLocked)
        {
            MidpointSurvivalActive = false;
            MidpointSurvivalCleared = true;
            Hp = Math.Max(1, (int)Math.Round(MaxHp * .5));
            RebasePhaseHealth();
            SetSinPhase(5);
        }
        FinishMovementTracking();
    }

    protected override void SetSinPhase(int phase)
    {
        base.SetSinPhase(phase);
        PhaseDeclarations = 0;
        _crystalWalls.Clear();
        _reactiveCounters.Clear();
        _lastPattern = -1;
        _castsSinceDirectedThreat = 0;
        Stagger = Math.Min(Stagger, MaxStagger * .5);
        StaggerDecayTimer = StaggerDecayDelay;
        IsStaggered = false;
        StaggerRemaining = 0.0;
        if (Phase == 8)
        {
            _compressionCooldown = 5.0;
            ActTransitionTimer = 0.0;
            PhaseProtectionTimer = 0.0;
        }
    }

    /// <summary>Ported from _camera_cardinal_angle: the on-screen "right" direction, rotated by quarter turns, expressed in world space.</summary>
    private float CameraCardinalAngle(Camera? camera, int quarterTurn = 0)
    {
        var worldVector = camera?.ScreenVectorToWorld(new Vector2(1, 0)) ?? new Vector2(1, 0);
        float baseAngle = MathF.Atan2(worldVector.Y, worldVector.X);
        return baseAngle + quarterTurn * MathF.PI / 2f;
    }

    private void GrowCrystalWall(float angle, double duration = 8.0, string? kind = null, float distanceTiles = 3.9f, bool compression = false)
    {
        var center = Center();
        // distanceTiles is boss-relative, not ArenaRadius-relative, so it
        // needs ArenaFormationScale to keep the wall's spawn point (and the
        // ground it has to cover to reach the boss) proportional to the
        // bigger arena.
        float distance = Simulation.TileSize * distanceTiles * ArenaFormationScale;
        float wallCenterX = center.X + MathF.Cos(angle) * distance;
        float wallCenterY = center.Y + MathF.Sin(angle) * distance;
        bool horizontal = Math.Abs(MathF.Cos(angle)) < Math.Abs(MathF.Sin(angle));
        float width = Simulation.TileSize * (horizontal ? 3.5f : .72f);
        float height = Simulation.TileSize * (horizontal ? .72f : 3.5f);
        var rect = new Rectangle((int)(wallCenterX - width / 2f), (int)(wallCenterY - height / 2f), (int)width, (int)height);
        string wallKind = kind ?? (PatternRotation % 2 == 0 ? "brittle" : "reinforced");
        _crystalWalls.Add(new CrystalWall
        {
            Rect = rect, Remaining = duration, Duration = duration, Angle = angle, Kind = wallKind,
            CenterX = wallCenterX, CenterY = wallCenterY,
            Hp = wallKind == "brittle" ? 420 : null, Warning = compression ? 2.5 : 0.0, Compression = compression,
        });
        if (_crystalWalls.Count > 6)
            _crystalWalls.RemoveRange(0, _crystalWalls.Count - 6);
    }

    protected override void UpdateTerrain(float playerX, float playerY, double dt, EnemyUpdateContext context)
    {
        var afflictions = context.BossAfflictions;
        if (afflictions is null)
            return;
        PeakExposure = Math.Max(PeakExposure, afflictions.Exposure);
        _consumedCrystalPulse = Math.Max(0.0, _consumedCrystalPulse - dt);
        var center = Center();
        foreach (var wall in _crystalWalls)
        {
            wall.Remaining = Math.Max(0.0, wall.Remaining - dt);
            wall.Warning = Math.Max(0.0, wall.Warning - dt);
            wall.ReactionFlash = Math.Max(0.0, wall.ReactionFlash - dt);
            if (wall.Compression && wall.Warning <= 0)
            {
                float deltaX = center.X - wall.CenterX, deltaY = center.Y - wall.CenterY;
                float distance = Math.Max(1.0f, MathF.Sqrt(deltaX * deltaX + deltaY * deltaY));
                // The boss's own FixedPath patrol loop already scales its
                // radius (and so its linear speed, for the same loop period)
                // with ArenaRadius. A compression wall chases the boss's
                // *current* position every frame, so its catch-up speed and
                // stop threshold need the same ArenaFormationScale to keep
                // pace with a target that now moves proportionally farther
                // and faster.
                if (distance > Simulation.TileSize * 2.25f * ArenaFormationScale)
                {
                    float step = Simulation.TileSize * .34f * ArenaFormationScale * (float)dt;
                    wall.CenterX += deltaX / distance * step;
                    wall.CenterY += deltaY / distance * step;
                    wall.Rect = new Rectangle((int)MathF.Round(wall.CenterX) - wall.Rect.Width / 2,
                        (int)MathF.Round(wall.CenterY) - wall.Rect.Height / 2, wall.Rect.Width, wall.Rect.Height);
                }
            }
        }
        _crystalWalls.RemoveAll(wall => wall.Remaining <= 0);

        foreach (var vent in _cleansingVents)
        {
            vent.Cooldown = Math.Max(0.0, vent.Cooldown - dt);
            vent.Flash = Math.Max(0.0, vent.Flash - dt);
            float distanceToVent = MathF.Sqrt((playerX - vent.X) * (playerX - vent.X) + (playerY - vent.Y) * (playerY - vent.Y));
            if (vent.Cooldown <= 0 && afflictions.Exposure > .25 && distanceToVent <= Simulation.TileSize * 1.05f)
            {
                afflictions.Reset();
                vent.Cooldown = 12.0;
                vent.Flash = 1.0;
                VentsUsed++;
                // Cleansing opens the player's immediate position but seals the
                // corresponding inner route, turning relief into a terrain choice.
                GrowCrystalWall(vent.Angle, 7.0);
            }
        }

        _compressionCooldown = Math.Max(0.0, _compressionCooldown - dt);
        if (Phase >= 5 && EntranceRemaining <= 0 && ActTransitionTimer <= 0 &&
            _compressionCooldown <= 0 && _crystalWalls.Count(wall => wall.Compression) < 4)
        {
            float playerAngle = MathF.Atan2(playerY - center.Y, playerX - center.X);
            int side = Rng.Next(2) == 0 ? -1 : 1;
            float falseAlarm = playerAngle + side * MathF.PI / 2f + (float)(Rng.NextDouble() * .34 - .17);
            string kind = PatternRotation % 3 == 0 ? "reinforced" : "brittle";
            GrowCrystalWall(falseAlarm, duration: Phase == 8 ? 9.5 : 8.0, kind: kind,
                distanceTiles: 7.2f, compression: true);
            _compressionCooldown = Phase == 8 ? 6.2 : 8.4 - (Phase - 5) * .55;
        }
    }

    public override IReadOnlyList<Rectangle> MovementObstacles()
    {
        _movementObstacleScratch.Clear();
        for (int index = 0; index < _crystalWalls.Count; index++)
        {
            CrystalWall wall = _crystalWalls[index];
            if (wall.Warning <= 0)
                _movementObstacleScratch.Add(wall.Rect);
        }
        return _movementObstacleScratch;
    }

    public override IReadOnlyList<(string Part, Rectangle Rect)> GetScreenHitboxes(Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        _screenHitboxScratch.Clear();
        IReadOnlyList<(string Part, Rectangle Rect)> baseHitboxes =
            base.GetScreenHitboxes(
                camera,
                playerWorldPosition,
                screenShake);
        for (int index = 0; index < baseHitboxes.Count; index++)
            _screenHitboxScratch.Add(baseHitboxes[index]);
        for (int index = 0; index < _crystalWalls.Count; index++)
        {
            var wall = _crystalWalls[index];
            if (wall.Kind != "brittle" || wall.Warning > 0)
                continue;
            var rect = wall.Rect;
            Vector2 topLeft = camera.WorldToScreen(
                new Vector2(rect.Left, rect.Top),
                playerWorldPosition,
                screenShake);
            Vector2 topRight = camera.WorldToScreen(
                new Vector2(rect.Right, rect.Top),
                playerWorldPosition,
                screenShake);
            Vector2 bottomRight = camera.WorldToScreen(
                new Vector2(rect.Right, rect.Bottom),
                playerWorldPosition,
                screenShake);
            Vector2 bottomLeft = camera.WorldToScreen(
                new Vector2(rect.Left, rect.Bottom),
                playerWorldPosition,
                screenShake);
            float left = Math.Min(
                Math.Min(topLeft.X, topRight.X),
                Math.Min(bottomRight.X, bottomLeft.X));
            float top = Math.Min(
                Math.Min(topLeft.Y, topRight.Y),
                Math.Min(bottomRight.Y, bottomLeft.Y));
            float right = Math.Max(
                Math.Max(topLeft.X, topRight.X),
                Math.Max(bottomRight.X, bottomLeft.X));
            float bottom = Math.Max(
                Math.Max(topLeft.Y, topRight.Y),
                Math.Max(bottomRight.Y, bottomLeft.Y));
            _screenHitboxScratch.Add((
                $"crystal:{index}",
                new Rectangle(
                    (int)left,
                    (int)top,
                    Math.Max(1, (int)(right - left)),
                    Math.Max(1, (int)(bottom - top)))));
        }
        return _screenHitboxScratch;
    }

    protected override HitResult DamageCrystal(string partId, double amount)
    {
        int index = int.Parse(partId.Split(':', 2)[1]);
        if (index < 0 || index >= _crystalWalls.Count)
            return new HitResult(false, false, 0, true);
        var wall = _crystalWalls[index];
        if (wall.Kind != "brittle")
            return new HitResult(false, false, 0, true);
        double applied = Math.Min(wall.Hp!.Value, Math.Round(amount));
        wall.Hp -= applied;
        if (wall.Hp <= 0)
        {
            _crystalWalls.RemoveAt(index);
            _consumedCrystalPulse = 1.0;
            NerveBreakProgress++;
            // The amalgam of chaos reacts as a whole: a broken wall visibly
            // rattles whatever else is still standing, not just itself.
            foreach (var remaining in _crystalWalls)
                remaining.ReactionFlash = .5;
            if (NerveBreakProgress >= NerveBreaksNeeded)
            {
                NerveBreakProgress = 0;
                NerveBreakTriggers++;
                Stagger = MaxStagger;
                IsStaggered = true;
                StaggerRemaining = StaggerDuration;
                TransitionCleanupRequested = true;
                _consumedCrystalPulse = 1.4;
                _overreactionCascadePending = true;
            }
        }
        return new HitResult(true, false, applied);
    }

    protected override void DrawBossBody(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        Vector2 screen = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        Vector2 center = screen + new Vector2(Size / 2f, Size / 2f);
        Color orange = new(234, 111, 29);
        Color deepOrange = new(178, 67, 26);
        Color blue = new(48, 139, 219);

        if (Dying)
        {
            BossVisuals.Disassemble(spriteBatch, center, Age, DeathProgress, Size * 1.2f, orange, blue, 10);
            return;
        }

        DrawHeatShimmer(spriteBatch, camera, playerWorldPosition, screenShake);

        float seconds = VisualAgeSeconds;
        float attackPulse = VisualAttackPulse;
        float survivalSpread = VisualSurvivalActive ? 1.58f : 1f;
        float oscillation = 1f + BossAnimation.Sine(seconds, .7f) * .08f
            + attackPulse * .12f;
        float coreExtent = Size * .29f * oscillation;
        Vector2 jittered = center + new Vector2(
            BossAnimation.Sine(seconds, 2.15f) * 4.2f
                + BossAnimation.Sine(seconds, 8.9f) * 3.4f,
            BossAnimation.Sine(seconds, 2.73f, .18f) * 3.8f);
        if (_flinchRemaining > 0)
        {
            // A one-off asymmetric kick away from the flinch direction, not a
            // continuous wobble: Ache visibly recoils from its own misfire.
            float flinchT = (float)Math.Clamp(1.0 - _flinchRemaining / .35, 0, 1);
            float kick = (1f - BossAnimation.EaseOutBack(flinchT, overshoot: 1.6f)) * Size * .1f;
            jittered += new Vector2(MathF.Cos(_flinchDirection), MathF.Sin(_flinchDirection)) * kick;
        }
        Color armSignal = Phase is 7 or 8
            ? Color.Lerp(blue, UiTheme.Cream, .48f)
            : blue;
        float disagreement = Phase switch
        {
            2 => 1.18f,
            3 => -1.08f,
            5 => 1.34f,
            6 => -.82f,
            7 => 1.52f,
            8 => 1.72f,
            _ => 1f,
        };

        Span<(Vector2 Center, float Angle, float Depth)> arms =
            stackalloc (Vector2, float, float)[OrbitingArmCount];
        for (int index = 0; index < OrbitingArmCount; index++)
        {
            float direction = index == 1 ? -1f : 1f;
            if (Phase is 3 or 6)
                direction *= -1f;
            float drift = seconds * (.54f + index * .15f) * direction * disagreement;
            float hesitation = BossAnimation.Sine(seconds,
                4.9f - index * .62f, index * .3f) * (.48f + Phase * .025f);
            float angle = drift + hesitation + index * MathF.Tau / OrbitingArmCount;
            float radius = Size * (.62f + .14f * BossAnimation.Sine(seconds,
                1.52f - index * .17f, index * .27f)) * survivalSpread;
            if (index == (Phase - 1) % OrbitingArmCount)
                radius += Size * attackPulse * .16f;
            float droop = Size * (.06f + index * .035f)
                * BossAnimation.CosinePulse(seconds, 3.6f, index * .16f);
            var armCenter = jittered + new Vector2(MathF.Cos(angle) * radius,
                MathF.Sin(angle) * radius * .54f + droop);
            arms[index] = (armCenter, angle, MathF.Sin(angle));
            Primitives2D.Line(spriteBatch, jittered, armCenter, UiTheme.Ink, Math.Max(4, (int)(Size * .07f)));
            Primitives2D.Line(spriteBatch, jittered, armCenter, armSignal * .72f, Math.Max(1, (int)(Size * .025f)));
        }
        if (Phase is 2 or 5 or 8)
        {
            for (int index = 0; index < arms.Length; index++)
                Primitives2D.Line(spriteBatch, arms[index].Center,
                    arms[(index + 1) % arms.Length].Center,
                    (index % 2 == 0 ? orange : armSignal) * .38f, 2);
        }

        for (int index = 1; index < arms.Length; index++)
        {
            var current = arms[index];
            int insertion = index - 1;
            while (insertion >= 0
                && arms[insertion].Depth > current.Depth)
            {
                arms[insertion + 1] = arms[insertion];
                insertion--;
            }
            arms[insertion + 1] = current;
        }

        for (int index = 0;
             index < arms.Length && arms[index].Depth < 0;
             index++)
        {
            var arm = arms[index];
            BossVisuals.RotatingCube3D(
                spriteBatch,
                arm.Center,
                Size * .12f,
                armSignal,
                new Color(75, 183, 235),
                orange,
                -arm.Angle * 1.3f,
                arm.Angle * .73f,
                seconds * (.78f + index * .11f) * (index == 1 ? -1f : 1f), escalation: SecondFormBlend);
        }

        bool facingActive = MovementProfile.Mode is BossMovementMode.Chase or BossMovementMode.FixedPath;
        float coreYaw = facingActive ? _facingYaw : seconds * 2.46f;
        BossVisuals.RotatingCube3D(spriteBatch, jittered, coreExtent, orange, deepOrange, blue,
            coreYaw, .58f + BossAnimation.Sine(seconds, .79f) * .32f,
            BossAnimation.Sine(seconds, .98f) * .18f, escalation: SecondFormBlend);

        float energyRadius = Size * (.075f + .012f * BossAnimation.Sine(seconds, .57f));
        Primitives2D.FillCircle(spriteBatch, jittered, (int)energyRadius + 5, UiTheme.Ink);
        Primitives2D.FillCircle(spriteBatch, jittered, Math.Max(2, (int)energyRadius), blue);

        for (int index = 0; index < arms.Length; index++)
        {
            var arm = arms[index];
            if (arm.Depth < 0)
                continue;
            BossVisuals.RotatingCube3D(
                spriteBatch,
                arm.Center,
                Size * .12f,
                armSignal,
                new Color(75, 183, 235),
                orange,
                -arm.Angle * 1.3f,
                arm.Angle * .73f,
                seconds * (.78f + index * .11f) * (index == 1 ? -1f : 1f), escalation: SecondFormBlend);
        }

        DrawBossHealth(spriteBatch, new Rectangle((int)(center.X - Size * .46f), (int)(center.Y - Size * .78f), (int)(Size * .92f), 6));
    }

    private void DrawHeatShimmer(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        // The jagged arena only carries a shared, generic per-vertex wobble
        // (PathChaseBoss). This decorative haze layer is Ache's own burning/
        // tingling identity on top of it -- an approximate ring is plenty for
        // a heat-mirage effect, no need to reach into the shared boundary's
        // private per-vertex seed.
        const int streaks = 22;
        float seconds = VisualAgeSeconds;
        for (int index = 0; index < streaks; index++)
        {
            float angle = index * MathF.Tau / streaks;
            Vector2 baseWorld = ArenaCenter + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * ArenaRadius;
            Vector2 baseScreen = camera.WorldToScreen(baseWorld, playerWorldPosition, screenShake);
            float pulse = .35f + .35f * BossAnimation.Sine(seconds, 1.6f + index % 5 * .11f, index * .41f);
            Vector2? previous = null;
            const int segments = 5;
            for (int segment = 0; segment <= segments; segment++)
            {
                float t = segment / (float)segments;
                float sway = MathF.Sin(t * MathF.PI * 2.4f + seconds * 3.1f + index * .77f) * 5f * t;
                Vector2 point = baseScreen + new Vector2(sway, -t * 26f);
                if (previous.HasValue)
                    Primitives2D.Line(spriteBatch, previous.Value, point,
                        new Color(214, 88, 40) * (pulse * (1f - t) * .5f), 2);
                previous = point;
            }
        }
    }

    protected override void DrawPersistentTerrain(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        DrawOverloadConstellation(spriteBatch, camera, playerWorldPosition, screenShake);
        foreach (var vent in _cleansingVents)
        {
            var point = camera.WorldToScreen(new Vector2(vent.X, vent.Y), playerWorldPosition, screenShake);
            bool ready = vent.Cooldown <= 0;
            Color color = vent.Flash > 0 ? UiTheme.Cream : ready ? new Color(96, 185, 151) : UiTheme.Border;
            float radius = Simulation.TileSize * (ready ? .42f : .32f);
            Primitives2D.FillCircle(spriteBatch, point, radius + 6, UiTheme.Ink);
            Primitives2D.CircleOutline(spriteBatch, point, radius, color, 4);
            Primitives2D.Line(spriteBatch, new Vector2(point.X - radius, point.Y), new Vector2(point.X + radius, point.Y), color, 2);
            Primitives2D.Line(spriteBatch, new Vector2(point.X, point.Y - radius), new Vector2(point.X, point.Y + radius), color, 2);
        }

        Span<Vector2> wallPoints = stackalloc Vector2[4];
        foreach (var wall in _crystalWalls)
        {
            var rect = wall.Rect;
            var topLeft = camera.WorldToScreen(new Vector2(rect.Left, rect.Top), playerWorldPosition, screenShake);
            var bottomRight = camera.WorldToScreen(new Vector2(rect.Right, rect.Bottom), playerWorldPosition, screenShake);
            var screenRect = new Rectangle(
                (int)Math.Min(topLeft.X, bottomRight.X), (int)Math.Min(topLeft.Y, bottomRight.Y),
                Math.Max(8, (int)Math.Abs(bottomRight.X - topLeft.X)), Math.Max(8, (int)Math.Abs(bottomRight.Y - topLeft.Y)));
            double fade = Math.Min(1.0, wall.Remaining * 2);
            bool warning = wall.Warning > 0;
            Color color = warning ? UiTheme.Cream : UiTheme.Lighten(PhaseAccent, wall.Kind == "brittle" ? 48 : (int)(20 * fade));
            if (wall.ReactionFlash > 0)
                color = Color.Lerp(color, UiTheme.Cream, (float)(wall.ReactionFlash / .5) * .6f);
            var outer = screenRect;
            outer.Inflate(8, 8);
            Primitives2D.FillRect(spriteBatch, outer, UiTheme.Ink);
            if (warning)
                Primitives2D.RectOutline(spriteBatch, screenRect, color, 3);
            else
            {
                Primitives2D.FillRect(spriteBatch, screenRect, color);
                wallPoints[0] = new(screenRect.Left, screenRect.Top);
                wallPoints[1] = new(screenRect.Right, screenRect.Top);
                wallPoints[2] = new(screenRect.Right, screenRect.Bottom);
                wallPoints[3] = new(screenRect.Left, screenRect.Bottom);
                Primitives2D.DrawPolygonBevel(spriteBatch, wallPoints, color, 2);
            }
            int stripeStep = Math.Max(8, (int)(Simulation.TileSize * .4f));
            int span = Math.Max(screenRect.Width, screenRect.Height);
            for (int offset = 0; offset < span; offset += stripeStep)
            {
                if (screenRect.Width >= screenRect.Height)
                {
                    Primitives2D.Line(spriteBatch, new Vector2(screenRect.X + offset, screenRect.Bottom),
                        new Vector2(screenRect.X + offset + 9, screenRect.Y), UiTheme.Cream, 2);
                }
                else
                {
                    Primitives2D.Line(spriteBatch, new Vector2(screenRect.X, screenRect.Y + offset),
                        new Vector2(screenRect.Right, screenRect.Y + offset + 9), UiTheme.Cream, 2);
                }
            }
        }

        if (ActTransitionTimer > 0)
            DrawRoutePreview(spriteBatch, camera, playerWorldPosition, screenShake);
    }

    private void DrawOverloadConstellation(SpriteBatch spriteBatch, Camera camera,
        Vector2 playerWorldPosition, Vector2 screenShake)
    {
        int nodeCount = OverloadConstellationNodeCount;
        if (nodeCount <= 0)
            return;

        Vector2 arena = camera.WorldToScreen(
            ArenaCenter, playerWorldPosition, screenShake);
        float progress = (float)FinaleProgress;
        var nodes = new Vector2[nodeCount];
        for (int index = 0; index < nodeCount; index++)
        {
            float direction = index % 3 == 1 ? -1f : 1f;
            float angle = index * MathF.Tau / nodeCount +
                Age * (.0014f + index % 4 * .00038f) * direction +
                MathF.Sin(Age * (.0021f + index * .00017f) + index * 1.73f) * .34f;
            float radius = ArenaRadius * (.28f + progress * .52f +
                MathF.Sin(Age * .003f + index * 2.17f) * .055f);
            nodes[index] = arena + new Vector2(
                MathF.Cos(angle) * radius,
                MathF.Sin(angle) * radius * (.72f + (index % 3) * .09f));
        }

        Color orange = new Color(232, 112, 31);
        Color blue = new Color(54, 143, 218);
        for (int index = 0; index < nodeCount; index++)
        {
            int destination = (index + 2 + index % 3) % nodeCount;
            Color tether = index % 2 == 0 ? orange : blue;
            Primitives2D.Line(spriteBatch, nodes[index], nodes[destination],
                UiTheme.Ink * (.22f + progress * .18f), 7);
            Primitives2D.Line(spriteBatch, nodes[index], nodes[destination],
                tether * (.28f + progress * .22f), 2);
        }

        for (int index = 0; index < nodeCount; index++)
        {
            Color face = index % 2 == 0 ? orange * .68f : blue * .68f;
            Color edge = index % 2 == 0 ? blue * .58f : orange * .58f;
            float extent = Simulation.TileSize * (.20f + progress * .08f +
                (index % 3) * .025f);
            BossVisuals.RotatingCube3D(spriteBatch, nodes[index], extent,
                face, UiTheme.Lighten(face, 28), edge,
                Age * (.004f + index * .0003f), index * .31f, -index * .17f, escalation: SecondFormBlend);
        }
    }

    private void DrawRoutePreview(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        var ready = _cleansingVents.Where(vent => vent.Cooldown <= 0).ToList();
        if (ready.Count < 2)
            return;
        var color = new Color(96, 185, 151);
        var start = camera.WorldToScreen(new Vector2(ready[0].X, ready[0].Y), playerWorldPosition, screenShake);
        var target = ready[ready.Count > 2 ? 2 : 1];
        var end = camera.WorldToScreen(new Vector2(target.X, target.Y), playerWorldPosition, screenShake);
        Primitives2D.Line(spriteBatch, start, end, UiTheme.Ink, 9);
        Primitives2D.Line(spriteBatch, start, end, color, 3);
        var midpoint = (start + end) / 2f;
        UiTheme.DrawText(spriteBatch, "PREVIEW // CLEAN ROUTE", 9, color, midpoint, "center");
    }

    public IReadOnlyDictionary<string, bool> ChallengeResults() => new Dictionary<string, bool>
    {
        ["clean_traversal"] = PeakExposure <= 3.0,
        ["vent_discipline"] = VentsUsed <= 1,
        ["uncontaminated"] = PeakExposure <= .25,
    };

    private void ContaminationPool(List<EnemyProjectile> sink, Vector2 position, float damage = FieldDamage, float lifetime = 8f)
    {
        float size = Simulation.TileSize * (2.0f + (float)Rng.NextDouble() * .9f);
        sink.Add(new EnemyProjectile(position.X - size / 2f, position.Y - size / 2f, 0f, 0f, damage, size,
            color: new Color(139, 50, 158), shape: "pool", path: "pool", lifetime: lifetime,
            owner: "ache_chemesthesis_contamination", ignoreWalls: true)
        {
            TelegraphDuration = 1.25f,
            PersistentHazard = true,
            Affliction = "slow",
            AfflictionDuration = 1.1,
            AfflictionStrength = .12,
            Exposure = .65,
        });
    }

    /// <summary>
    /// Ache's laser signature: unlike the other bosses' rigid or flowing
    /// beams, hers can bend into a travelling sine wave (<paramref name="amplitude"/>/
    /// <paramref name="frequency"/>/<paramref name="waveSpeed"/>) on top of a
    /// slow full-beam rotation (<paramref name="angularSpeed"/>) -- the two
    /// combined read as an erratic, twisting scan rather than a clean sweep,
    /// matching a boss whose whole identity is chaotic and unpredictable.
    /// Both are zero by default so ordinary straight lashes are unaffected.
    /// </summary>
    private void TelegraphLash(List<EnemyProjectile> sink, Vector2 origin, float direction, float damage,
        string suffix, float angularSpeed = 0f, float amplitude = 0f, float frequency = .05f, float waveSpeed = 0f)
    {
        sink.Add(new EnemyProjectile(origin.X, origin.Y, direction, 0f, damage, Size * .13f,
            travelRange: Simulation.TileSize * 30f, color: PhaseAccent, shape: "laser", path: "laser",
            amplitude: amplitude, frequency: frequency,
            lifetime: 2.35f, angularSpeed: angularSpeed, owner: $"ache_chemesthesis_{suffix}", ignoreWalls: true)
        {
            TelegraphDuration = 1.25f,
            LaserWaveSpeed = waveSpeed,
        });
    }

    private void SlowWrongWayBurst(List<EnemyProjectile> sink, float aimed)
    {
        float wrong = aimed + MathF.PI + (float)(Rng.NextDouble() * 1.4 - .7);
        int count = 2 + Rng.Next(2);
        for (int index = 0; index < count; index++)
        {
            float offset = (index - (count - 1) / 2f) * (.3f + (float)Rng.NextDouble() * .18f);
            var mine = Shot(sink, wrong + offset, .38f + (float)Rng.NextDouble() * .2f, MineDamage,
                scale: .22f + (float)Rng.NextDouble() * .08f, shape: "spore", path: "mine",
                lifetime: 10f + (float)Rng.NextDouble() * 3f, speedDecay: .045f, ownerSuffix: "wrong_way_hazard",
                affliction: "slow", afflictionDuration: 1.2, afflictionStrength: .1, exposure: .5);
            mine.TelegraphDuration = .9f;
            // One spore per volley weakly corrects its aim as it drifts --
            // Ache's mistake slowly noticing it fired the wrong way, rather
            // than every spore holding its spawn heading forever.
            if (index == count - 1)
                mine.HomingTurnRate = .55f;
        }
        var splinter = Shot(sink,
            aimed + MathF.PI + (float)(Rng.NextDouble() * .9 - .45),
            .68f, MineDamage, scale: .19f, shape: "spore",
            ownerSuffix: "misfire_splinter");
        splinter.SplitCount = 3;
        splinter.SplitAt = Simulation.TileSize * (3.4f + (float)Rng.NextDouble() * 1.6f);
    }

    private Vector2 ClampToMinefield(Vector2 position)
    {
        Vector2 offset = position - ArenaCenter;
        float distance = offset.Length();
        float limit = ArenaRadius * .82f;
        return distance <= limit || distance <= 0
            ? position
            : ArenaCenter + offset / distance * limit;
    }

    private Vector2 RandomMinefieldPoint(float innerRadius = .18f, float outerRadius = .8f)
    {
        float angle = (float)(Rng.NextDouble() * MathF.Tau);
        // Square root keeps random deposits spatially even instead of piling
        // most of them near Ache at the center.
        float unitRadius = MathF.Sqrt((float)Rng.NextDouble());
        float radius = ArenaRadius * (innerRadius + (outerRadius - innerRadius) * unitRadius);
        return ArenaCenter + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
    }

    private void PlantDormantMine(List<EnemyProjectile> sink, Vector2 position, string suffix,
        float lifetime = 11f, float damage = MineDamage)
    {
        position = ClampToMinefield(position);
        float size = Simulation.TileSize * (.48f + (float)Rng.NextDouble() * .16f);
        sink.Add(new EnemyProjectile(position.X - size / 2f, position.Y - size / 2f, 0f, 0f,
            damage, size, travelRange: float.PositiveInfinity, color: PhaseAccent,
            shape: "ember", path: "mine", lifetime: lifetime + (float)Rng.NextDouble() * 2f,
            owner: $"ache_chemesthesis_{suffix}", ignoreWalls: true)
        {
            TelegraphDuration = 1.15f + (float)Rng.NextDouble() * .45f,
            Affliction = "slow",
            AfflictionDuration = 1.15,
            AfflictionStrength = .1,
            Exposure = .55,
        });
    }

    private void PlantLazyCluster(List<EnemyProjectile> sink)
    {
        Vector2 anchor = RandomMinefieldPoint(.2f, .72f);
        float axis = (float)(Rng.NextDouble() * MathF.Tau);
        for (int index = 0; index < 2; index++)
        {
            float side = index == 0 ? -1f : 1f;
            float spacing = Simulation.TileSize * (1.05f + (float)Rng.NextDouble() * .65f);
            PlantDormantMine(sink, anchor + new Vector2(MathF.Cos(axis), MathF.Sin(axis)) * spacing * side,
                "lazy_cluster");
        }
    }

    private void PlantCornerPocket(List<EnemyProjectile> sink, Vector2 player)
    {
        int escapeSide = Rng.Next(4);
        float rotation = (float)(Rng.NextDouble() * .42 - .21);
        for (int side = 0; side < 4; side++)
        {
            if (side == escapeSide)
                continue;
            float angle = side * MathF.PI / 2f + rotation + (float)(Rng.NextDouble() * .18 - .09);
            float distance = Simulation.TileSize * (1.55f + (float)Rng.NextDouble() * .75f);
            var offset = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance;
            PlantDormantMine(sink, player + offset, "corner_pocket", 9.5f, FieldDamage);
        }
    }

    private void MarkStationaryReflex(List<EnemyProjectile> sink, Vector2 player)
    {
        var reflex = Bomb(sink, player.X, player.Y, FieldDamage,
            "stationary_reflex", burstCount: 5, fuseDuration: 2.15f,
            burstShotDamage: 145);
        reflex.BlastRadius = Simulation.TileSize * 1.45f;
        reflex.BurstRangeTiles = 6.5f;
        reflex.TelegraphDuration = 1.0f;
    }

    private void ReflexSpiral(List<EnemyProjectile> sink, float aimed)
    {
        const int count = 5;
        for (int index = 0; index < count; index++)
        {
            float direction = aimed + MathF.PI + index * MathF.Tau / count +
                PatternRotation * .16f;
            Shot(sink, direction, .48f + .045f * (index % 2),
                RingDamage - 20, scale: .18f + .012f * (index % 3),
                shape: index % 2 == 0 ? "diamond" : "square",
                path: "sine", lifetime: 8.5f,
                ownerSuffix: "reflex_spiral",
                amplitude: Simulation.TileSize * (.65f + .18f * (index % 3)),
                frequency: .044f + .006f * (index % 2));
        }
    }

    private static bool IsDirectedPattern(int pattern) => pattern is 1 or 5;

    private int ChoosePattern()
    {
        // Widened so no retaliation runs one family for a whole clock: every
        // phase can now reach at least five of the eight reaction types, and
        // the later ones reach nearly all of them.
        int[] choices = Phase switch
        {
            1 => new[] { 0, 0, 1, 2, 3, 6 },
            2 => new[] { 0, 1, 1, 2, 3, 6 },
            3 => new[] { 0, 1, 2, 3, 4, 6 },
            4 => new[] { 0, 1, 2, 3, 4, 6, 7 },
            5 => new[] { 0, 1, 2, 4, 4, 5, 6, 7 },
            6 => new[] { 1, 2, 3, 4, 5, 6, 7 },
            7 => new[] { 0, 1, 2, 4, 5, 6, 7 },
            _ => new[] { 0, 1, 2, 3, 4, 5, 6, 7 },
        };
        var eligible = choices.Where(pattern => pattern != _lastPattern).ToList();
        if (_castsSinceDirectedThreat >= 2)
        {
            var directed = eligible.Where(IsDirectedPattern).ToList();
            if (directed.Count > 0)
                eligible = directed;
        }
        return eligible[Rng.Next(eligible.Count)];
    }

    private void FireOverreactionCascade(List<EnemyProjectile> sink)
    {
        // The Nerve Break stagger previously only set a flag with no visible
        // eruption. This gives the "amalgam of pure chaos" a real payoff: a
        // quick ring of embers erupting outward at the moment it grounds.
        const int count = 6;
        var center = Center();
        for (int index = 0; index < count; index++)
        {
            float direction = index * MathF.Tau / count + (float)(Rng.NextDouble() * .3 - .15);
            Shot(sink, direction, .58f + (float)Rng.NextDouble() * .12f, FieldDamage,
                scale: .17f, shape: "ember", lifetime: 3.2f,
                ownerSuffix: "overreaction_cascade");
        }
    }

    private void SetFlinch(float direction)
    {
        // A real physical spasm tied to Ache's own off-aim mistakes, rather
        // than only the arms' constant orbit-timing disagreement.
        _flinchDirection = direction;
        _flinchRemaining = .35;
    }

    private void QueueReactiveCounter(float aimed)
    {
        int side = Rng.Next(2) == 0 ? -1 : 1;
        _reactiveCounters.Add(new ReactiveCounter(.65, aimed + side * .56f,
            HeavyDamage - 5, "counterreaction"));
    }

    private void UpdateReactiveCounters(List<EnemyProjectile> sink, double dt)
    {
        if (Dying || _reactiveCounters.Count == 0)
        {
            if (Dying)
                _reactiveCounters.Clear();
            return;
        }
        int writeIndex = 0;
        for (int readIndex = 0; readIndex < _reactiveCounters.Count; readIndex++)
        {
            var counter = _reactiveCounters[readIndex];
            double delay = counter.Delay - dt;
            if (delay <= 0)
                TelegraphLash(sink, Center(), counter.Direction, counter.Damage, counter.Suffix);
            else
                _reactiveCounters[writeIndex++] = counter with { Delay = delay };
        }
        if (writeIndex < _reactiveCounters.Count)
            _reactiveCounters.RemoveRange(writeIndex, _reactiveCounters.Count - writeIndex);
    }

    protected override void FireSinPattern(float playerX, float playerY, EnemyUpdateContext context)
    {
        var center = Center();
        float aimed = MathF.Atan2(playerY - center.Y, playerX - center.X);
        var sink = context.ProjectileSink;
        int activeThreats = sink.Count(projectile =>
            projectile.Owner?.StartsWith("ache_chemesthesis") == true && !projectile.RemFlag);
        int persistentThreats = sink.Count(projectile =>
            projectile.Owner?.StartsWith("ache_chemesthesis") == true &&
            (projectile.Path is "mine" or "pool" or "bomb") && !projectile.RemFlag);
        if (activeThreats >= ActiveThreatSoftCap)
        {
            // Ache is chaotically lazy, not infinitely productive: once the
            // field has enough unresolved mistakes, the next attack is only a
            // small reflex marker. Existing hazards keep doing the room-filling
            // work, but a stationary player still incurs a fresh position debt.
            MarkStationaryReflex(sink, new Vector2(playerX, playerY));
            PatternRotation++;
            MarkAttack(.34f);
            return;
        }
        int pattern;
        if (persistentThreats >= PersistentThreatSoftCap)
        {
            if (_castsSinceDirectedThreat >= 2 || _lastPattern == 3)
                pattern = 1;
            else if (_lastPattern == 1)
                pattern = 3;
            else
                pattern = Rng.Next(2) == 0 ? 1 : 3;
        }
        else
        {
            pattern = ChoosePattern();
        }

        switch (pattern)
        {
            case 0: // Deliberately fires away from the player and leaves slow debris behind.
                SlowWrongWayBurst(sink, aimed);
                SetFlinch(aimed + MathF.PI);
                break;
            case 1: // A reactable prediction: the exact route is harmless for 1.25 seconds.
            {
                float predictionError = (float)(Rng.NextDouble() * .5 - .25);
                // Fully visible for the whole telegraph, so the bend is a
                // fair puzzle rather than a hidden gotcha: the route just
                // isn't a straight line.
                TelegraphLash(sink, center, aimed + predictionError, HeavyDamage, "predicted_lash",
                    amplitude: Simulation.TileSize * .55f, frequency: .045f, waveSpeed: 1.1f);
                if (Phase >= 5)
                    TelegraphLash(sink, center, aimed + MathF.PI + predictionError, HeavyDamage - 10, "reverse_lash",
                        amplitude: Simulation.TileSize * .55f, frequency: .045f, waveSpeed: -1.1f);
                break;
            }
            case 2: // Bombs land around, not directly on, the current player position.
            {
                int bombs = Phase >= 5 ? 2 : 1;
                for (int index = 0; index < bombs; index++)
                {
                    float angle = (float)(Rng.NextDouble() * MathF.Tau);
                    float distance = Simulation.TileSize * (1.6f + (float)Rng.NextDouble() * 2.2f);
                    Bomb(sink, playerX + MathF.Cos(angle) * distance, playerY + MathF.Sin(angle) * distance,
                        BombDamage, "discord_bomb", burstCount: 3, fuseDuration: 2.8f,
                        burstShotDamage: MineDamage);
                    SetFlinch(angle);
                }
                break;
            }
            case 3: // Uneven ring with a broad, randomly rotating opening.
            {
                int count = 10;
                int gap = Rng.Next(count);
                for (int index = 0; index < count; index++)
                {
                    int distance = Math.Min((index - gap + count) % count, (gap - index + count) % count);
                    if (distance <= 2)
                        continue;
                    float direction = index * MathF.Tau / count + (float)Rng.NextDouble() * .08f;
                    Shot(sink, direction, .62f + (index % 3) * .09f, RingDamage, ownerSuffix: "discord_ring");
                }
                break;
            }
            case 4: // The visible pool warning is the only reliable part of the choice.
            {
                float angle = (float)(Rng.NextDouble() * MathF.Tau);
                float radius = ArenaRadius * (.18f + (float)Rng.NextDouble() * .55f);
                ContaminationPool(sink, ArenaCenter + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius);
                var debris = Shot(sink, aimed + MathF.PI + (float)(Rng.NextDouble() * .8 - .4), .48f,
                    MineDamage, scale: .25f, shape: "diamond", path: "sine", lifetime: 10f,
                    speedDecay: .045f, ownerSuffix: "contamination_debris", affliction: "slow",
                    afflictionDuration: 1.1, afflictionStrength: .1, exposure: .5,
                    amplitude: Simulation.TileSize * 1.1f, frequency: .05f);
                debris.TelegraphDuration = 1.0f;
                break;
            }
            case 5: // Crossed nerves: two curved warnings sweep only after being fully shown.
                TelegraphLash(sink, center, aimed - .72f, HeavyDamage, "crossed_nerves_left", .11f,
                    amplitude: Simulation.TileSize * .4f, frequency: .06f, waveSpeed: 1.6f);
                TelegraphLash(sink, center, aimed + .72f, HeavyDamage, "crossed_nerves_right", -.11f,
                    amplitude: Simulation.TileSize * .4f, frequency: .06f, waveSpeed: -1.6f);
                ContaminationPool(sink, new Vector2(playerX, playerY), FieldDamage, 7.5f);
                break;
            case 6: // Slothful construction: Ache drops two mines and leaves them to become a later problem.
                PlantLazyCluster(sink);
                break;
            default: // Three random sides close slowly; the fourth remains an observable escape route.
                PlantCornerPocket(sink, new Vector2(playerX, playerY));
                break;
        }

        bool directed = IsDirectedPattern(pattern);
        // Even the predicted lash receives a ground marker: stepping out of
        // the laser is the first dodge, not a complete answer to the phrase.
        // Crossed Nerves already deposits a contamination pool on the player.
        if (pattern != 5)
            MarkStationaryReflex(sink, new Vector2(playerX, playerY));
        if (MidpointSurvivalActive)
            ReflexSpiral(sink, aimed);
        // A second, unbaited layer underneath the retaliation the player did
        // provoke -- Ache's nerve is always firing at something.
        else if (Phase >= 3 && PatternRotation % 3 == 0)
            ReflexSpiral(sink, aimed + MathF.PI);
        _castsSinceDirectedThreat = directed ? 0 : _castsSinceDirectedThreat + 1;
        _lastPattern = pattern;
        _patternHistory.Add(pattern);
        if (_patternHistory.Count > 32)
            _patternHistory.RemoveAt(0);

        if (Phase >= 3 && !directed && PatternRotation % 2 == 1)
            QueueReactiveCounter(aimed);

        if (FinaleActive && PatternRotation % 2 == 0)
        {
            float angle = (float)(Rng.NextDouble() * MathF.Tau);
            // Overload: the finale's most deranged lash, rotating and
            // twisting at once so no two casts trace the same route.
            TelegraphLash(sink, center, angle, HeavyDamage, "overload_callback", Rng.Next(2) == 0 ? .13f : -.13f,
                amplitude: Simulation.TileSize * (.6f + (float)Rng.NextDouble() * .5f),
                frequency: .04f + (float)Rng.NextDouble() * .03f,
                waveSpeed: (Rng.Next(2) == 0 ? 1f : -1f) * (1.4f + (float)Rng.NextDouble() * 1.2f));
        }
        PhaseDeclarations++;
        PatternRotation++;
        MarkAttack(.66f);
    }
}
