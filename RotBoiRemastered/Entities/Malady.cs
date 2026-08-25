using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// Empress Malady, youngest and strongest ancient core -- the final boss of the Phantasia content path.
/// Ported from bossTypes.py's Malady. Adds a projectile-portal formation
/// system (reusing <see cref="ProjectilePortal"/>), a delay-queued "flowing
/// chain" shot sequence, survival phases that suppress damage while a
/// timer runs out, and a straight-pillar block body -- a tall purple
/// rectangular prism with a constellation of smaller cube/rectangle "arms"
/// orbiting it, some drawn in front and some behind -- replacing the
/// jointed puppet body and bespoke collapse-death animation the Python
/// original used with the same shared block-geometry/death-spectacle
/// machinery every other ancient core draws from.
///
/// Cleanup vs. the Python original: `vitalitySuppressed` and `puppetFacing`
/// are dropped -- both are written throughout `__init__`/`_set_dream_phase`/
/// `_update_puppet_motion`/`take_damage` but never read by any method on
/// this class (confirmed by reading every method), the same
/// confirmed-dead-code standard already applied to `PathChaseBoss.cs`'s
/// dropped stagger fields.
/// </summary>
public sealed class Malady : PhantasiaBoss
{
    public const int IdleBodyCubeCount = 10;
    public const int FinaleBodyCubeCount = 18;
    public const int InitialApotheosisCrownPetals = 6;
    public const int MinimumDamagePhaseDeclarations = 2;
    public const int ActiveThreatSoftCap = 132;
    private const int PatternThreatReservation = 28;
    protected override bool UsesDreamRules => false;
    protected override bool UsesSharedDeathSpectacle => true;
    protected override bool VisualSurvivalActive => SurvivalActive || FinaleActive || base.VisualSurvivalActive;
    private static readonly Dictionary<int, double> SurvivalPhases = new() { [6] = 18.0 };
    private static readonly int[] PortalCounts = { 3, 4, 3, 4, 5, 3, 6, 6, 5, 6 };
    private static readonly string[] PortalPaths =
        { "orbit", "figure8", "wave", "square", "tornado", "orbit", "square", "figure8", "wave", "tornado" };

    public static readonly PathChaseBossConfig MaladyConfig = BaseConfig with
    {
        BossName = "MALADY", Subtitle = "EMPRESS OF INSPIRATION", FinalBoss = true,
        OwnerPrefix = "malady_phantasia",
        PhaseLabels = new[]
        {
            "OVERTURE", "PETAL FLOOD", "IMPOSSIBLE ENGINE", "RIBBON COURT", "TENTACLE GARDEN",
            "INTERMISSION", "LUMINOUS TIDE", "VIOLET CATHEDRAL", "SOUL INCURSION", "APOTHEOSIS",
        },
        FinalBodyColor = new Color(67, 42, 119), FinalAccentColor = new Color(213, 103, 231),
        FinalBodyScale = 2.55, FinalCooldownSeconds = 1.05,
        MovementSpeed = .15, ArenaScale = 20.25,
        MovementPhases = new[]
        {
            BossMovementPhaseProfile.Fixed(BossPathShape.Ellipse, 11f, .58f, .38f),
            BossMovementPhaseProfile.Fixed(BossPathShape.FigureEight, 10f, .62f, .45f),
            BossMovementPhaseProfile.Stationary(),
            BossMovementPhaseProfile.Fixed(BossPathShape.Ellipse, 10f, .64f, .42f, -1),
            BossMovementPhaseProfile.Fixed(BossPathShape.FigureEight, 9.5f, .66f, .48f),
            BossMovementPhaseProfile.Stationary(),
            BossMovementPhaseProfile.Fixed(BossPathShape.Ellipse, 9f, .68f, .46f),
            BossMovementPhaseProfile.Stationary(),
            BossMovementPhaseProfile.Fixed(BossPathShape.FigureEight, 9f, .70f, .52f, -1),
            BossMovementPhaseProfile.Stationary(),
        },
        FinalHealth = 320000, FinalContactDamage = 900, FinalRewardExperience = 880,
        FinaleDuration = 30.0,
    };

    public static readonly PhantasiaSigilConfig MaladySigilConfig = new(
        PhaseFlavors: new[]
        {
            "A first impossible idea blossoms, and leaves one untouched lane.", "Even inspiration at flood leaves a shore for the worthy.",
            "Novel machinery assembles itself from color and intent.", "Each ribbon sketches an invention no human has named.",
            "The Empress reaches inward on slow, beautiful tendrils.", "Breathe in the still point between thoughts.",
            "Madness bends around one luminous absence.", "Violet arches make a cathedral from unreal geometry.",
            "She slips through imagination toward the Human Soul.", "The youngest ancient unveils every divine terror at once.",
        },
        PhaseColors: new[]
        {
            new Color(233, 192, 78), new Color(193, 84, 215), new Color(111, 174, 228), new Color(235, 228, 185),
            new Color(107, 191, 145), new Color(218, 102, 118), new Color(225, 128, 190), new Color(98, 189, 206),
            new Color(244, 244, 232), new Color(220, 71, 133),
        },
        PhaseSigils: Enumerable.Range(0, 10).ToArray(),
        ActMetadata: new Dictionary<int, string> { [4] = "ACT II // INVENTION", [7] = "ACT III // THE HUMAN SOUL" });

    private readonly record struct ChainEvent(double Delay, Vector2 Origin, float Direction, float Speed, float Damage, string Suffix);

    /// <summary>
    /// Local-space vertices of a flattened lens/orb: the same bipyramid
    /// topology as a gem, but its two poles pulled in close along the depth
    /// axis instead of stretched out, so it reads as a convex disc of stored
    /// inspiration floating at Malady's torso rather than a faceted gem.
    /// </summary>
    private static readonly Vector3[] LensVertices =
    [
        new(0, 0, -.35f), new(0, 0, .35f),
        new(1, 0, 0), new(0, 1, 0), new(-1, 0, 0), new(0, -1, 0),
    ];

    private static readonly int[][] LensFaces =
    [
        [0, 2, 3], [0, 3, 4], [0, 4, 5], [0, 5, 2],
        [1, 3, 2], [1, 4, 3], [1, 5, 4], [1, 2, 5],
    ];

    /// <summary>
    /// Local-space vertex order for a standard box (matches the private
    /// face-index topology <see cref="BossVisuals.RotatingCube3D"/> uses
    /// internally for its own cube), shared between the pillar body and the
    /// elongated-rectangle "arms" below -- only the per-axis scale differs.
    /// </summary>
    private static readonly int[][] BoxFaces =
    [
        [0, 1, 2, 3], [4, 7, 6, 5], [0, 4, 5, 1], [3, 2, 6, 7], [0, 3, 7, 4], [1, 5, 6, 2],
    ];

    /// <summary>A tall, narrow box: Malady's straight pillar torso.</summary>
    private static readonly Vector3[] PillarVertices =
    [
        new(-.32f, -1f, -.32f), new(.32f, -1f, -.32f), new(.32f, 1f, -.32f), new(-.32f, 1f, -.32f),
        new(-.32f, -1f, .32f), new(.32f, -1f, .32f), new(.32f, 1f, .32f), new(-.32f, 1f, .32f),
    ];

    /// <summary>A long, flattened box: the elongated-rectangle flavor of orbiting "arm".</summary>
    private static readonly Vector3[] ArmRectangleVertices =
    [
        new(-1f, -.28f, -.3f), new(1f, -.28f, -.3f), new(1f, .28f, -.3f), new(-1f, .28f, -.3f),
        new(-1f, -.28f, .3f), new(1f, -.28f, .3f), new(1f, .28f, .3f), new(-1f, .28f, .3f),
    ];

    public List<ProjectilePortal> ProjectilePortals { get; } = new();
    private int _portalFormationPhase;
    private readonly List<ChainEvent> _sequenceQueue = new();
    private readonly List<EnemyProjectile> _stagedThreatScratch = new(ActiveThreatSoftCap);
    private readonly EnemyUpdateContext _stagedUpdateContext;
    private readonly (Vector2 Center, float Angle, float Depth, float Extent)[] _floatingCubeScratch =
        new (Vector2, float, float, float)[FinaleBodyCubeCount];
    private double _poolCooldown = 1.2;
    /// <summary>Smoothed body-yaw toward the player while actively advancing, via the shared <see cref="BossFacing"/> helper; falls back to an ambient idle spin otherwise (see <see cref="DrawBlockBody"/>).</summary>
    private float _facingYaw;
    // A soft silk-ribbon trail for the ribbon/chain "tentacle" attacks
    // (Ribbon Court / Tentacle Garden / Soul Incursion), reusing the same
    // DrawTentacleSpike primitive Rot's grasping tendrils use but retinted
    // cool violet/luminous so the two bosses' reach attacks read distinctly.
    // A small ordered waypoint path rather than a single origin/angle: linked
    // tendril attacks touch multiple portals, so the trail needs one spike
    // drawn per link to read as one connected structure, not a single arm.
    private Vector2[]? _tentacleVisualWaypoints;
    private double _tentacleVisualRemaining;
    private bool _patternAdmissionOpen = true;
    private bool _patternDeclaredThisFrame;
    private int _visualPreviousConstellationPhase = 1;
    private int _visualConstellationPhase = 1;
    private float _visualConstellationBlend = 1f;

    public bool SurvivalActive { get; private set; }
    public double SurvivalRemaining { get; private set; }
    public string AttackPose { get; private set; } = "idle";
    public float AttackAimAngle { get; private set; }
    public double AttackAnimationDuration { get; } = .72;
    public float AttackAnticipation { get; private set; }
    public bool Collapsing => Dying;
    public double CollapseDuration => DeathDuration;
    public double CollapseRemaining => DeathRemaining;
    public int PhaseDeclarations { get; private set; }
    public int ApotheosisCrownPetalCount => FinaleActive
        ? Math.Min(FinaleBodyCubeCount, InitialApotheosisCrownPetals +
            (int)(FinaleProgress * (FinaleBodyCubeCount - InitialApotheosisCrownPetals + 1)))
        : 0;
    internal int VisualConstellationPhase => _visualConstellationPhase;
    internal float VisualConstellationBlend => _visualConstellationBlend;

    public Malady(float worldX, float worldY, Battleground battleground, Random? rng = null)
        : base(worldX, worldY, battleground, MaladyConfig, MaladySigilConfig, rng)
    {
        _stagedUpdateContext = new EnemyUpdateContext
        {
            PlayerWorldX = 0,
            PlayerWorldY = 0,
            Battleground = battleground,
        };
        ActTitle = "ACT I // THE FIRST IDEA";
        ActTransitionTimer = ActTransitionDuration;
        PhaseProtectionTimer = ActTransitionDuration;
    }

    protected override void SetDreamPhase(int phase)
    {
        base.SetDreamPhase(phase);
        PhaseDeclarations = 0;
        _sequenceQueue.Clear();
        ClearMaladyPortals();
        _poolCooldown = .8;
        SurvivalActive = SurvivalPhases.TryGetValue(Phase, out var duration);
        SurvivalRemaining = duration;
    }

    protected override void UpdatePhase()
    {
        if (DebugPhaseLocked || FinaleActive || SurvivalActive || Dying)
            return;
        int count = Config.PhaseLabels.Count;
        double ratio = Math.Clamp((double)Hp / MaxHp, 0.0, 1.0);
        int desired = Math.Min(count, (int)((1.0 - ratio) * count + 1e-9) + 1);
        if (desired != Phase && PhaseDeclarations >= MinimumDamagePhaseDeclarations)
            SetDreamPhase(desired);
    }

    public override void DebugSetPhase(int phase)
    {
        base.DebugSetPhase(phase);
        if (Phase == Config.PhaseLabels.Count && !FinaleActive)
            BeginFinaleSequence();
    }

    public override HitResult TakeDamage(double amount, string partId = "body", DamageSource source = DamageSource.Direct)
    {
        if (SurvivalActive || FinaleActive || Dying)
            return new HitResult(false, false, 0, true);
        if (Phase == Config.PhaseLabels.Count &&
            PhaseDeclarations < MinimumDamagePhaseDeclarations)
        {
            double permitted = Math.Max(0, Hp - 1);
            if (permitted <= 0)
                return new HitResult(false, false, 0, true);
            var gated = base.TakeDamage(Math.Min(amount, permitted), partId, source);
            return new HitResult(gated.Applied, false, gated.Amount, gated.Blocked);
        }
        return base.TakeDamage(amount, partId, source);
    }

    private static int ActiveMaladyThreats(List<EnemyProjectile> sink)
    {
        int count = 0;
        foreach (var projectile in sink)
        {
            if (!projectile.RemFlag &&
                projectile.Owner?.StartsWith("malady_phantasia") == true)
            {
                count++;
            }
        }
        return count;
    }

    private EnemyUpdateContext WithProjectileSink(
        EnemyUpdateContext source, List<EnemyProjectile> sink)
    {
        _stagedUpdateContext.PlayerWorldX = source.PlayerWorldX;
        _stagedUpdateContext.PlayerWorldY = source.PlayerWorldY;
        _stagedUpdateContext.Battleground = source.Battleground;
        _stagedUpdateContext.ProjectileSink = sink;
        _stagedUpdateContext.AllEnemies = source.AllEnemies;
        _stagedUpdateContext.ExperienceBubbles = source.ExperienceBubbles;
        _stagedUpdateContext.Camera = source.Camera;
        _stagedUpdateContext.BossAfflictions = source.BossAfflictions;
        _stagedUpdateContext.PlayerBuildSnapshot = source.PlayerBuildSnapshot;
        _stagedUpdateContext.PlayerBullets = source.PlayerBullets;
        _stagedUpdateContext.DreamState = source.DreamState;
        _stagedUpdateContext.PlayerMovementSpeed = source.PlayerMovementSpeed;
        _stagedUpdateContext.MovementSpeedCap = source.MovementSpeedCap;
        return _stagedUpdateContext;
    }

    private bool CommitStagedThreats(List<EnemyProjectile> sink, List<EnemyProjectile> staged)
    {
        if (ActiveMaladyThreats(sink) + staged.Count > ActiveThreatSoftCap)
            return false;
        sink.AddRange(staged);
        return true;
    }

    private string AttackPoseForPhase() => Phase switch
    {
        2 or 6 or 7 => "radial",
        3 => "radial",
        4 or 5 or 9 => "chain",
        8 => "laser",
        10 => (PatternRotation % 3) switch { 0 => "radial", 1 => "chain", _ => "laser" },
        _ => "burst",
    };

    private void ClearMaladyPortals()
    {
        foreach (var portal in ProjectilePortals)
            portal.RemFlag = true;
        ProjectilePortals.Clear();
        _portalFormationPhase = 0;
    }

    private void EnsureMaladyPortals()
    {
        if (_portalFormationPhase == Phase && ProjectilePortals.Count > 0)
            return;
        ClearMaladyPortals();
        var center = ArenaCenter;
        int count = PortalCounts[Phase - 1];
        float radius = ArenaRadius * (SurvivalPhases.ContainsKey(Phase) ? .68f : .56f);
        for (int index = 0; index < count; index++)
        {
            var portal = new ProjectilePortal(center, radius, index * 2f * MathF.PI / count + Phase * .17f,
                angularSpeed: (.22f + Phase * .018f) * (index % 2 == 1 ? -1f : 1f),
                fireInterval: 999f, pelletCount: 5, spread: .78f,
                owner: $"{Config.OwnerPrefix}_portal", color: PhaseAccent,
                polarity: index % 2 == 1 ? -1 : 1, movementPath: PortalPaths[Phase - 1])
            {
                ShowTether = Phase is not (4 or 7 or 10),
            };
            ProjectilePortals.Add(portal);
        }
        _portalFormationPhase = Phase;
    }

    private Vector2 PortalOrigin(int index)
    {
        if (ProjectilePortals.Count == 0)
            return Center();
        var portal = ProjectilePortals[index % ProjectilePortals.Count];
        return new Vector2(portal.WorldX + portal.Size / 2f, portal.WorldY + portal.Size / 2f);
    }

    /// <summary>
    /// Malady's laser signature: a handful of thin strands sharing one
    /// heading instead of PhantasiaBoss.LaserFrom's single solid beam --
    /// each strand rides its own sine wave (amplitude/frequency/wave speed
    /// all slightly offset) so the group visibly drifts in and out of phase,
    /// crossing and re-crossing along the beam's length, and each is tinted
    /// a different point around the color wheel so the crossings read as a
    /// woven, many-colored ribbon rather than a flat plane of light. Used in
    /// place of a single cathedral/apotheosis beam, and -- fired from every
    /// portal at once -- as the converging amalgam veil survival draws on.
    /// </summary>
    private void FlowingRibbonLaser(List<EnemyProjectile> sink, Vector2 origin, float direction, float damage,
        string suffix, int strandCount = 3, float range = 0f, float telegraph = 1.35f, float lifetime = 3.2f)
    {
        float actualRange = range > 0f ? range : Math.Max(Simulation.TileSize * 30f, ArenaRadius * 2.2f);
        for (int strand = 0; strand < strandCount; strand++)
        {
            float lane = strandCount == 1 ? 0f : strand - (strandCount - 1) / 2f;
            float colorPhase = strand / (float)Math.Max(1, strandCount) + PatternRotation * .09f;
            var laser = new EnemyProjectile(origin.X, origin.Y, direction + lane * .06f, 0f, damage, Size * .045f,
                travelRange: actualRange, color: Primitives2D.Rainbow(colorPhase), shape: "laser", path: "laser",
                amplitude: Simulation.TileSize * (.34f + .1f * strand), frequency: .05f + .006f * strand,
                lifetime: lifetime, owner: $"malady_phantasia_{suffix}_{strand}", ignoreWalls: true)
            {
                TelegraphDuration = telegraph,
                LaserWaveSpeed = 1.05f + strand * .34f,
            };
            laser.RequireOriginTelegraphIfRemote(Center(), Size * .65f, telegraph);
            sink.Add(laser);
        }
    }

    /// <summary>
    /// Survival-only: every live portal weaves its own <see cref="FlowingRibbonLaser"/>
    /// bundle inward toward the arena center, so the ribbons overlap into a
    /// slowly rotating amalgam cage the player has to thread rather than a
    /// single obstacle to sidestep.
    /// </summary>
    private void SurvivalAmalgamVeil(List<EnemyProjectile> sink)
    {
        var center = Center();
        for (int index = 0; index < ProjectilePortals.Count; index++)
        {
            var origin = PortalOrigin(index);
            float direction = MathF.Atan2(center.Y - origin.Y, center.X - origin.X);
            FlowingRibbonLaser(sink, origin, direction, 210, "soul_veil", strandCount: 2,
                range: Vector2.Distance(origin, center) * 1.35f, telegraph: 1.15f, lifetime: 2.6f);
        }
    }

    private EnemyProjectile SpawnPool(List<EnemyProjectile> sink, Vector2 position, double duration = 7.0,
        double scale = 1.0, bool breathing = false)
    {
        float size = Simulation.TileSize * 2.35f * (float)scale;
        var pool = new EnemyProjectile(position.X - size / 2f, position.Y - size / 2f, 0f, 0f, 285, size,
            color: new Color(147, 57, 190), shape: "pool", path: "pool", lifetime: (float)duration,
            owner: $"{Config.OwnerPrefix}_purple_pool", ignoreWalls: true)
        {
            TelegraphDuration = 1.05f, PersistentHazard = true, TruthMarked = true, BeliefGain = .35,
        };
        if (breathing)
        {
            // Survival's pools breathe in and out between the ribbon-laser
            // veil instead of sitting static, so the whole room feels alive
            // and closing in rather than just laced with fixed hazards.
            pool.PoolPulseAmplitude = .3f;
            pool.PoolPulseFrequency = .55f;
        }
        sink.Add(pool);
        return pool;
    }

    private void QueueChain(Vector2 origin, float startAngle, float arc, string suffix,
        int count = 16, double interval = .055, float speed = .74f, float damage = 335)
    {
        for (int index = 0; index < count; index++)
        {
            float fraction = index / (float)Math.Max(1, count - 1);
            _sequenceQueue.Add(new ChainEvent(index * interval, origin, startAngle + arc * fraction,
                speed * (1.0f - .18f * MathF.Sin(fraction * MathF.PI)), damage, suffix));
        }
    }

    /// <summary>
    /// Generalizes <see cref="QueueChain"/> from "shots swept across an arc
    /// around one origin" to "shots threaded along a bowed polyline through
    /// several waypoints" -- the mechanism behind the new linked-tendril
    /// system: instead of firing several independent tendrils from different
    /// locations, one continuous chain's spawn points trace a path that
    /// actually visits every waypoint in order.
    /// </summary>
    private void QueueLinkedChain(IReadOnlyList<Vector2> waypoints, string suffix,
        int countPerLink = 7, double interval = .052, float speed = .68f, float damage = 350f,
        float bendStrength = .18f)
    {
        double delay = 0;
        for (int link = 0; link < waypoints.Count - 1; link++)
        {
            Vector2 start = waypoints[link], end = waypoints[link + 1];
            Vector2 span = end - start;
            if (span.LengthSquared() < .001f)
                continue;
            Vector2 tangent = Vector2.Normalize(span);
            Vector2 normal = new(-tangent.Y, tangent.X);
            float linkLength = span.Length();
            float direction = MathF.Atan2(tangent.Y, tangent.X);
            for (int index = 0; index < countPerLink; index++)
            {
                float fraction = index / (float)Math.Max(1, countPerLink - 1);
                // An organic bow through the middle of the link instead of a
                // rigid straight line -- the "funky angle" read.
                float bow = MathF.Sin(fraction * MathF.PI) * linkLength * bendStrength;
                Vector2 origin = Vector2.Lerp(start, end, fraction) + normal * bow;
                _sequenceQueue.Add(new ChainEvent(delay, origin, direction + (fraction - .5f) * .3f,
                    speed * (1.0f - .18f * MathF.Sin(fraction * MathF.PI)), damage, suffix));
                delay += interval;
            }
        }
    }

    /// <summary>Replaces firing two independent <see cref="PortalTentacle"/> sweeps from two different portals with one linked tendril whose path actually connects both origins (with the player's position threaded in as a middle waypoint, keeping the "reaches toward the player" read the old paired calls had).</summary>
    private void PortalTentacleChain(List<EnemyProjectile> sink, int portalIndexA, int portalIndexB,
        Vector2 target, string suffix, int countPerLink = 7, float speed = .68f)
    {
        Vector2 a = PortalOrigin(portalIndexA);
        Vector2 b = PortalOrigin(portalIndexB);
        QueueLinkedChain(new[] { a, target, b }, suffix, countPerLink, speed: speed, damage: 350f);
        _tentacleVisualWaypoints = new[] { a, target, b };
        _tentacleVisualRemaining = countPerLink * 2 * .052 + 1.1;
    }

    private void UpdateSequences(List<EnemyProjectile> sink, double dt)
    {
        int writeIndex = 0;
        for (int readIndex = 0; readIndex < _sequenceQueue.Count; readIndex++)
        {
            var chainEvent = _sequenceQueue[readIndex];
            double delay = chainEvent.Delay - dt;
            if (delay <= 0)
            {
                ShotFrom(sink, chainEvent.Origin, chainEvent.Direction, chainEvent.Speed, chainEvent.Damage, chainEvent.Suffix,
                    shape: "diamond", belief: .38, sizeScale: .82f);
            }
            else
            {
                _sequenceQueue[writeIndex++] = chainEvent with { Delay = delay };
            }
        }
        if (writeIndex < _sequenceQueue.Count)
            _sequenceQueue.RemoveRange(writeIndex, _sequenceQueue.Count - writeIndex);
    }

    private void FirePortalPhrase(List<EnemyProjectile> sink, Vector2 target, bool wide = false)
    {
        if (ProjectilePortals.Count == 0)
            return;
        var portal = ProjectilePortals[PatternRotation % ProjectilePortals.Count];
        var waves = new[]
        {
            // Dissonance establishes a 3/5/4 twelve-shot phrase as the
            // baseline portal barrage. Malady keeps the faster speed ramp,
            // but now carries the same density instead of the old 2/3/2 lull.
            new BurstWave(3, .24f, 1.25f, .32f),
            new BurstWave(5, wide ? .68f : .4f, 1.6f, .25f),
            new BurstWave(4, .16f, 2.0f, .21f),
        };
        portal.FirePatternBurst(sink, target, waves, waveInterval: .12f, damage: 325, color: PhaseAccent, ownerSuffix: "dream_burst");
    }

    public override void Update(EnemyUpdateContext context)
    {
        double dt = Seconds();
        _tentacleVisualRemaining = Math.Max(0.0, _tentacleVisualRemaining - dt);
        if (Dying)
        {
            base.Update(context);
            return;
        }

        _stagedThreatScratch.Clear();
        var stagedThreats = _stagedThreatScratch;
        var stagedContext = WithProjectileSink(context, stagedThreats);
        EnsureMaladyPortals();
        UpdateSequences(stagedThreats, dt);
        foreach (var portal in ProjectilePortals)
        {
            portal.OrbitCenter = ArenaCenter;
            portal.Angle += portal.AngularSpeed * (float)dt;
            portal.Place();
            portal.UpdateBursts(stagedThreats, (float)dt);
        }

        if (EntranceRemaining <= 0 && ActTransitionTimer <= 0)
        {
            _poolCooldown -= dt;
            double poolRate = SurvivalActive ? 2.4 : 4.2;
            if (_poolCooldown <= 0 && (SurvivalActive || Phase is 2 or 5 or 8 or 10))
            {
                float angle = (float)(Rng.NextDouble() * 2 * Math.PI);
                float radius = ArenaRadius * (float)(.18 + Rng.NextDouble() * (.64 - .18));
                var position = ArenaCenter + new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius);
                SpawnPool(stagedThreats, position, duration: SurvivalActive ? 5.8 : 7.5,
                    scale: .82 + Rng.NextDouble() * .36, breathing: SurvivalActive);
                _poolCooldown = poolRate;
            }
            if (SurvivalActive && !DebugPhaseLocked)
            {
                SurvivalRemaining = Math.Max(0.0, SurvivalRemaining - dt);
                if (SurvivalRemaining <= 0)
                {
                    SurvivalActive = false;
                    Hp = Math.Max(1, (int)Math.Round(MaxHp * .4));
                    SetDreamPhase(7);
                }
            }
        }

        _patternDeclaredThisFrame = false;
        int sequenceCountBeforePattern = _sequenceQueue.Count;
        _patternAdmissionOpen =
            ActiveMaladyThreats(context.ProjectileSink) + stagedThreats.Count +
            PatternThreatReservation <= ActiveThreatSoftCap;
        base.Update(stagedContext);
        bool committed = CommitStagedThreats(context.ProjectileSink, stagedThreats);
        if (_patternDeclaredThisFrame)
        {
            if (committed)
            {
                PhaseDeclarations++;
            }
            else if (_sequenceQueue.Count > sequenceCountBeforePattern)
            {
                _sequenceQueue.RemoveRange(sequenceCountBeforePattern,
                    _sequenceQueue.Count - sequenceCountBeforePattern);
            }
        }
        if (MovementProfile.Mode is BossMovementMode.Chase or BossMovementMode.FixedPath)
        {
            _facingYaw = BossFacing.SmoothFacingYaw(_facingYaw, Center(),
                new Vector2(context.PlayerWorldX, context.PlayerWorldY), dt);
        }
        float anticipationWindow = Simulation.FrameRate * .34f;
        if (EntranceRemaining <= 0 && ActTransitionTimer <= 0 && AttackCooldown > 0 && AttackCooldown <= anticipationWindow)
        {
            var center = Center();
            AttackPose = AttackPoseForPhase();
            AttackAimAngle = MathF.Atan2(context.PlayerWorldY - center.Y, context.PlayerWorldX - center.X);
            AttackAnticipation = Math.Clamp(1 - AttackCooldown!.Value / anticipationWindow, 0f, 1f);
        }
        else
        {
            AttackAnticipation = 0f;
        }
        UpdateVisualConstellation((float)dt);
    }

    private void UpdateVisualConstellation(float dt)
    {
        int target = FinaleActive
            ? (PatternRotation % 3) switch { 0 => 2, 1 => 5, _ => 8 }
            : Phase;
        if (target != _visualConstellationPhase)
        {
            _visualPreviousConstellationPhase = _visualConstellationPhase;
            _visualConstellationPhase = target;
            _visualConstellationBlend = 0f;
        }
        else
        {
            _visualConstellationBlend = Math.Min(1f,
                _visualConstellationBlend + dt / .82f);
        }
    }

    private void RadialWithGap(List<EnemyProjectile> sink, Vector2 origin, Vector2 safeTarget, int count,
        int gapHalfWidth, float speed, float damage, string suffix, string path = "linear")
    {
        float safeAngle = MathF.Atan2(safeTarget.Y - origin.Y, safeTarget.X - origin.X);
        float step = MathF.Tau / count;
        int safeIndex = (int)MathF.Round(((safeAngle % MathF.Tau + MathF.Tau) % MathF.Tau) / step) % count;
        for (int index = 0; index < count; index++)
        {
            int distance = Math.Min((index - safeIndex + count) % count, (safeIndex - index + count) % count);
            if (distance <= gapHalfWidth)
                continue;
            ShotFrom(sink, origin, index * step + PatternRotation * .11f, speed, damage, suffix,
                shape: "diamond", path: path, belief: .25);
        }
    }

    private void PortalTentacle(List<EnemyProjectile> sink, int portalIndex, Vector2 target, float arc, string suffix,
        int count = 22, float speed = .68f)
    {
        var origin = PortalOrigin(portalIndex);
        float aimed = MathF.Atan2(target.Y - origin.Y, target.X - origin.X);
        QueueChain(origin, aimed - arc / 2f, arc, suffix, count, interval: .052, speed: speed, damage: 350);
        float reach = Simulation.TileSize * count * .16f;
        _tentacleVisualWaypoints = new[] { origin, origin + new Vector2(MathF.Cos(aimed), MathF.Sin(aimed)) * reach };
        _tentacleVisualRemaining = count * .052 + 1.1;
    }

    protected override void FirePhantasiaPattern(float playerX, float playerY, EnemyUpdateContext context)
    {
        if (!_patternAdmissionOpen)
        {
            MarkAttack(.2f);
            return;
        }
        _patternDeclaredThisFrame = true;
        var center = Center();
        var target = new Vector2(playerX, playerY);
        float aimed = MathF.Atan2(playerY - center.Y, playerX - center.X);
        AttackAimAngle = aimed;
        AttackPose = AttackPoseForPhase();
        var sink = context.ProjectileSink;

        switch (Phase)
        {
            case 1: // Petals open around a lane pointing directly at the player.
                RadialWithGap(sink, center, target, 14, 1, .9f, 320, "overture_petals", "sine");
                break;
            case 2: // Alternating portals flood inward but preserve the player's current shore.
                for (int index = PatternRotation % 2; index < ProjectilePortals.Count; index += 2)
                    RadialWithGap(sink, PortalOrigin(index), target, 8, 1, .95f, 325, "petal_flood", "sine");
                break;
            case 3: // Two rigid portal gears turn around player-facing runs of missing teeth.
                RadialWithGap(sink, PortalOrigin(PatternRotation), target, 12, 2, .9f, 340,
                    "impossible_engine_drive", "linear");
                RadialWithGap(sink, PortalOrigin(PatternRotation + 1), target, 12, 2, .9f, 340,
                    "impossible_engine_counterdrive", "linear");
                break;
            case 4: // One long ribbon now threads between two portals instead of two separate ribbons.
                if (ProjectilePortals.Count >= 2)
                {
                    int ribbonA = PatternRotation % ProjectilePortals.Count;
                    int ribbonB = (ribbonA + Math.Min(2, ProjectilePortals.Count - 1)) % ProjectilePortals.Count;
                    PortalTentacleChain(sink, ribbonA, ribbonB, target, "ribbon_court", 7, .92f);
                }
                break;
            case 5: // Splitting tendrils grow outward, never spawning directly inside the marked opening.
                foreach (int index in new[] { -1, 1 })
                {
                    var shot = ShotFrom(sink, center, aimed + index * .42f, .9f, 335, "tentacle_garden", path: "sine");
                    shot.SplitCount = 2;
                    shot.SplitAt = Simulation.TileSize * 3.8f;
                    shot.SplitGeneration = 2;
                }
                break;
            case 6: // A flower and one reaching thought alternate around a deliberately empty center.
                if (PatternRotation % 2 == 0)
                    RadialWithGap(sink, center, target, 16, 2, .85f, 340, "intermission_flower", "sine");
                else
                    PortalTentacle(sink, PatternRotation, target, PatternRotation % 4 == 1 ? 1.7f : -1.7f,
                        "intermission_tentacle", 7, .86f);
                // Survival draws the portals' light inward: a woven veil of
                // thin ribbon lasers slowly closes the room instead of only
                // ever pushing hazards outward.
                if (SurvivalActive && PatternRotation % 3 == 0)
                    SurvivalAmalgamVeil(sink);
                break;
            case 7:
                RadialWithGap(sink, center, target, 16, 2, 1.0f, 355, "luminous_tide", "sine");
                if (PatternRotation % 2 == 0)
                    FirePortalPhrase(sink, target, wide: true);
                break;
            case 8: // A cathedral of fully telegraphed portal lasers leaves two adjacent aisles open.
            {
                int count = Math.Max(1, ProjectilePortals.Count);
                float playerAngle = MathF.Atan2(playerY - ArenaCenter.Y, playerX - ArenaCenter.X);
                int aisle = (int)MathF.Round(((playerAngle % MathF.Tau + MathF.Tau) % MathF.Tau) / (MathF.Tau / count)) % count;
                int neighbor = (aisle + (PatternRotation % 2 == 0 ? 1 : -1) + count) % count;
                for (int index = 0; index < count; index++)
                {
                    if (index == aisle || index == neighbor)
                        continue;
                    var origin = PortalOrigin(index);
                    FlowingRibbonLaser(sink, origin, MathF.Atan2(center.Y - origin.Y, center.X - origin.X),
                        390, "violet_cathedral", strandCount: 3);
                }
                break;
            }
            case 9:
                if (PatternRotation % 2 == 0)
                {
                    PortalTentacleChain(sink, PatternRotation, PatternRotation + 2, target,
                        "soul_incursion_tentacle", 6, 1.05f);
                }
                else
                {
                    RadialWithGap(sink, center, target, 18, 3, .92f, 365, "soul_incursion_bloom");
                }
                break;
            default: // Apotheosis cycles the fight's signature ideas for thirty final seconds.
            {
                int movement = PatternRotation % 3;
                if (movement == 0)
                {
                    RadialWithGap(sink, center, target, 16, 3, 1.05f, 390, "apotheosis_flood", "sine");
                    FirePortalPhrase(sink, target, wide: true);
                }
                else if (movement == 1)
                {
                    int apotheosisStart = PatternRotation + PatternRotation % 2;
                    PortalTentacleChain(sink, apotheosisStart, apotheosisStart + 2, target,
                        "apotheosis_tentacle", 7, 1.12f);
                }
                else
                {
                    RadialWithGap(sink, center, target, 18, 3, .98f, 380, "apotheosis_corolla");
                    for (int index = 0; index < ProjectilePortals.Count; index += 2)
                    {
                        var origin = PortalOrigin(index);
                        FlowingRibbonLaser(sink, origin, MathF.Atan2(center.Y - origin.Y, center.X - origin.X),
                            410, "apotheosis_laser", strandCount: 3, telegraph: 1.4f, lifetime: 3.6f);
                    }
                }
                break;
            }
        }
        if (!SurvivalPhases.ContainsKey(Phase) && Phase is not (7 or 10) && PatternRotation % 2 == 0)
            FirePortalPhrase(sink, target, wide: Phase >= 7);
        PatternRotation++;
        MarkAttack(.72f);
    }

    protected override void DrawBossBody(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        DrawBlockBody(spriteBatch, camera, playerWorldPosition, screenShake);
        DrawTentacleTrail(spriteBatch, camera, playerWorldPosition, screenShake);
        if (FinaleActive && !Collapsing)
            DrawApotheosisConstellationOverlay(spriteBatch, camera, playerWorldPosition, screenShake);
    }

    /// <summary>The Ribbon Court / Tentacle Garden / Soul Incursion reach attacks had gameplay (QueueChain/PortalTentacle) but no dedicated trail visual -- this gives them a soft, cool-toned silk ribbon distinct from Rot's brown/green grasping tendril, which reuses the same underlying primitive.</summary>
    private void DrawTentacleTrail(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        if (_tentacleVisualRemaining <= 0 || _tentacleVisualWaypoints is not { Length: >= 2 } waypoints)
            return;
        float alpha = (float)Math.Clamp(_tentacleVisualRemaining / .6, 0, 1);
        Color theme = Color.Lerp(new Color(218, 104, 232), PhaseAccent, .3f);
        for (int link = 0; link < waypoints.Length - 1; link++)
        {
            Vector2 start = waypoints[link], end = waypoints[link + 1];
            Vector2 span = end - start;
            if (span.LengthSquared() < .001f)
                continue;
            Vector2 origin = camera.WorldToScreen(start, playerWorldPosition, screenShake);
            Vector2 endScreen = camera.WorldToScreen(end, playerWorldPosition, screenShake);
            float length = Vector2.Distance(origin, endScreen);
            float angle = MathF.Atan2(endScreen.Y - origin.Y, endScreen.X - origin.X);
            Primitives2D.DrawTentacleSpike(spriteBatch, origin, angle,
                length, Size * .09f, link * 1.3f, 0f, VisualAgeSeconds, segments: 20,
                darken: 0f, alpha: .7f * alpha, themeColor: theme);
        }
    }

    /// <summary>Layers the finale's shifting cube constellation and inspiration mandala as an additive spectacle around the pillar body during Apotheosis.</summary>
    private void DrawApotheosisConstellationOverlay(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        Vector2 screenPosition = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        float bob = MathF.Sin(Age * .025f) * Size * .035f;
        var core = new Vector2(screenPosition.X + Size / 2f, screenPosition.Y + Size * .47f + bob);
        Color indigo = new(62, 39, 116);
        Color violet = new(105, 59, 164);
        Color luminous = Color.Lerp(new Color(218, 104, 232), PhaseAccent, .36f);
        float seconds = VisualAgeSeconds;
        DrawApotheosisMandala(spriteBatch, core, luminous);
        float attack = Math.Max(AttackAnticipation, VisualAttackPulse);
        int cubeCount = ApotheosisCrownPetalCount;
        float constellationBlend = BossAnimation.EaseInOutSine(_visualConstellationBlend);
        for (int index = 0; index < cubeCount; index++)
        {
            var previous = ConstellationPoint(_visualPreviousConstellationPhase, index, cubeCount, 1.58f, attack);
            var current = ConstellationPoint(_visualConstellationPhase, index, cubeCount, 1.58f, attack);
            Vector2 offset = Vector2.Lerp(previous.Offset, current.Offset, constellationBlend);
            float angle = MathHelper.Lerp(previous.Angle, current.Angle, constellationBlend);
            float extent = Size * (.07f + index % 3 * .018f);
            BossVisuals.RotatingCube3D(spriteBatch, core + offset, extent, indigo, violet, luminous,
                angle, angle * .53f, seconds * .36f);
        }
    }

    /// <summary>Front pole faces read bright cream (the stored light escaping), back-facing facets fall to the body's own indigo tone.</summary>
    private static Color LensFaceColor(int faceIndex, Color bodyColor) =>
        faceIndex < 4 ? Color.Lerp(bodyColor, UiTheme.Ink, .3f) : UiTheme.Cream;

    /// <summary>
    /// Malady's body: a tall, straight purple rectangular prism with a loose
    /// constellation of smaller block "arms" -- a mix of cubes and elongated
    /// rectangles -- orbiting it, some drawn in front of the pillar and some
    /// behind. Reuses <see cref="ConstellationPoint"/>'s existing per-phase
    /// formations for orbit placement and the same insertion-sort-by-depth
    /// two-pass split the removed (dead) `DrawDreamBody` pioneered for its
    /// floating cube crown -- just applied to every state now, not only a
    /// finale-only path nothing ever called.
    /// </summary>
    private void DrawBlockBody(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        Vector2 screenPosition = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        float bob = MathF.Sin(Age * .025f) * Size * .035f;
        var core = new Vector2(screenPosition.X + Size / 2f, screenPosition.Y + Size * .47f + bob);

        var shadow = new Rectangle((int)(core.X - Size * .78f), (int)(core.Y + Size * .41f), (int)(Size * 1.56f), (int)(Size * .38f));
        Primitives2D.FillEllipse(spriteBatch, shadow, UiTheme.Shadow);

        if (Dying)
        {
            Color deathLuminous = Color.Lerp(Config.FinalAccentColor, UiTheme.Cream, .3f);
            BossVisuals.OscillatingAura(spriteBatch, core, Age, Size * (1f + DeathProgress * 2.2f), deathLuminous, 7, 2.1f);
            BossVisuals.Disassemble(spriteBatch, core, Age, DeathProgress, Size * 1.35f,
                Config.FinalBodyColor, deathLuminous, 24);
            return;
        }

        // Actively-advancing movement (Chase/FixedPath) turns the pillar to
        // face where it's headed via the shared BossFacing helper; every
        // other state (stationary phases, survival, finale) keeps the plain
        // ambient idle spin instead.
        bool facingActive = MovementProfile.Mode is BossMovementMode.Chase or BossMovementMode.FixedPath;
        float idleSpin = Age * .012f;
        float bodyYaw = facingActive ? _facingYaw : idleSpin;
        float bodyPitch = MathF.Sin(Age * .01f) * .1f;

        int armCount = FinaleActive ? ApotheosisCrownPetalCount : SurvivalActive ? 14 : IdleBodyCubeCount;
        float spectacle = FinaleActive ? 1.58f : SurvivalActive ? 1.3f : 1f;
        float attack = Math.Max(AttackAnticipation, VisualAttackPulse);
        float constellationBlend = BossAnimation.EaseInOutSine(_visualConstellationBlend);
        for (int index = 0; index < armCount; index++)
        {
            var previous = ConstellationPoint(_visualPreviousConstellationPhase, index, armCount, spectacle, attack);
            var current = ConstellationPoint(_visualConstellationPhase, index, armCount, spectacle, attack);
            Vector2 offset = Vector2.Lerp(previous.Offset, current.Offset, constellationBlend);
            float angle = MathHelper.Lerp(previous.Angle, current.Angle, constellationBlend);
            float depth = MathHelper.Lerp(previous.Depth, current.Depth, constellationBlend);
            if (index == 0 && attack > .01f)
            {
                // One arm reaches toward the incoming attack line, so a
                // telegraphed shot direction reads on the body itself.
                Vector2 aimOffset = new Vector2(MathF.Cos(AttackAimAngle), MathF.Sin(AttackAimAngle) * .6f) * Size * .92f;
                offset = Vector2.Lerp(offset, aimOffset, attack * .6f);
            }
            _floatingCubeScratch[index] = (core + offset, angle, depth, Size * (.09f + index % 3 * .022f));
        }
        for (int index = 1; index < armCount; index++)
        {
            var candidate = _floatingCubeScratch[index];
            int insertion = index - 1;
            while (insertion >= 0 && _floatingCubeScratch[insertion].Depth > candidate.Depth)
            {
                _floatingCubeScratch[insertion + 1] = _floatingCubeScratch[insertion];
                insertion--;
            }
            _floatingCubeScratch[insertion + 1] = candidate;
        }

        Color primary = Config.FinalBodyColor;
        Color secondary = Color.Lerp(primary, UiTheme.Ink, .3f);
        Color accent = Config.FinalAccentColor;
        float seconds = VisualAgeSeconds;

        int armIndex = 0;
        for (; armIndex < armCount && _floatingCubeScratch[armIndex].Depth < 0; armIndex++)
            DrawArm(spriteBatch, _floatingCubeScratch[armIndex], armIndex, primary, secondary, accent, seconds);

        BossVisuals.RotatingSolid3D(spriteBatch, core, Size * .58f, PillarVertices, BoxFaces,
            faceIndex => BossVisuals.PhysicalCubeFaceColor(faceIndex, primary, secondary, accent),
            bodyYaw, bodyPitch, edgeAccent: accent);

        // The orb-lens turns slowly at the torso, in front of the pillar --
        // a real convex disc of stored inspiration rather than a flat dot.
        BossVisuals.RotatingSolid3D(spriteBatch, core, Size * .13f, LensVertices, LensFaces,
            faceIndex => LensFaceColor(faceIndex, primary),
            Age * .01f, .4f + MathF.Sin(Age * .017f) * .3f, edgeAccent: accent);

        for (; armIndex < armCount; armIndex++)
            DrawArm(spriteBatch, _floatingCubeScratch[armIndex], armIndex, primary, secondary, accent, seconds);

        DrawBossHealth(spriteBatch, new Rectangle((int)(core.X - Size * .46f), (int)(core.Y - Size * .95f), (int)(Size * .92f), 6));
    }

    /// <summary>One orbiting "arm": a cube two-thirds of the time, an elongated rectangle the rest, so the constellation reads as a mix of block shapes rather than one repeated cube.</summary>
    private void DrawArm(SpriteBatch spriteBatch, (Vector2 Center, float Angle, float Depth, float Extent) arm,
        int index, Color primary, Color secondary, Color accent, float seconds)
    {
        float yaw = arm.Angle;
        float pitch = arm.Angle * .53f;
        float roll = seconds * .36f;
        if (index % 3 == 2)
        {
            BossVisuals.RotatingSolid3D(spriteBatch, arm.Center, arm.Extent, ArmRectangleVertices, BoxFaces,
                faceIndex => BossVisuals.PhysicalCubeFaceColor(faceIndex, secondary, primary, accent),
                yaw, pitch, roll, edgeAccent: accent);
        }
        else
        {
            BossVisuals.RotatingCube3D(spriteBatch, arm.Center, arm.Extent, primary, secondary, accent, yaw, pitch, roll);
        }
    }

    /// <summary>
    /// Malady's imperial silhouette: a tall indigo core surrounded by loose
    /// cubes that compose a different visual grammar for every movement.
    /// Attack motion changes the constellation, never the Empress's composed
    /// central posture.
    /// </summary>
    private (Vector2 Offset, float Angle, float Depth) ConstellationPoint(
        int phase, int index, int count, float spectacle, float attack)
    {
        float size = Size;
        float fraction = index / (float)Math.Max(1, count - 1);
        float angle;
        Vector2 offset;
        switch (phase)
        {
            case 1: // A balanced blossom teaches that every formation has an opening.
                angle = index * MathF.Tau / count + Age * (index % 2 == 0 ? .008f : -.006f);
                float petalRadius = size * (.48f + .15f * (index % 2));
                offset = new Vector2(MathF.Cos(angle) * petalRadius, MathF.Sin(angle) * petalRadius * .68f);
                break;
            case 2: // The blossom unspools into an expanding portal-authored spiral.
                angle = index * .86f + Age * (index % 2 == 0 ? .01f : -.007f);
                float floodRadius = size * (.3f + fraction * .55f);
                offset = new Vector2(MathF.Cos(angle) * floodRadius, MathF.Sin(angle) * floodRadius * .62f);
                break;
            case 3: // A rigid lattice briefly arrests the organic motion: the impossible engine.
            {
                float column = index % 5 - 2f;
                float rowCount = MathF.Ceiling(count / 5f);
                float row = index / 5f - (rowCount - 1f) / 2f;
                offset = new Vector2(column * size * .21f, row * size * .28f);
                float turn = MathF.Sin(Age * .006f) * .13f + attack * .08f;
                offset = new Vector2(offset.X * MathF.Cos(turn) - offset.Y * MathF.Sin(turn),
                    offset.X * MathF.Sin(turn) + offset.Y * MathF.Cos(turn));
                angle = turn + (index % 2 == 0 ? 0f : MathF.PI / 2f);
                break;
            }
            case 4: // A single continuous S-curve makes the court read like written calligraphy.
                angle = fraction * MathF.Tau + Age * .012f;
                offset = new Vector2((fraction - .5f) * size * 1.62f,
                    MathF.Sin(angle) * size * (.28f + attack * .08f));
                break;
            case 5: // Paired branches grow away from the core like invented anatomy.
            {
                int branch = index / 2;
                int side = index % 2 == 0 ? -1 : 1;
                float reach = size * (.3f + branch * .12f);
                angle = side < 0 ? MathF.PI : 0f;
                offset = new Vector2(side * reach,
                    (branch - (count / 4f)) * size * .11f + MathF.Sin(Age * .014f + branch) * size * .08f);
                break;
            }
            case 6: // The intermission is defined by the conspicuously empty center.
                angle = index * MathF.Tau / count - Age * .006f;
                float stillRadius = size * (.68f + .06f * (index % 2));
                offset = new Vector2(MathF.Cos(angle) * stillRadius, MathF.Sin(angle) * stillRadius * .58f);
                break;
            case 7: // A broad horizontal wave replaces the closed intermission ring.
                angle = fraction * MathF.Tau + Age * .015f;
                offset = new Vector2((fraction - .5f) * size * 1.7f,
                    MathF.Sin(angle) * size * .34f);
                break;
            case 8: // Paired vertical columns foreshadow the two open cathedral aisles.
            {
                int side = index % 2 == 0 ? -1 : 1;
                int row = index / 2;
                float rows = MathF.Ceiling(count / 2f);
                angle = MathF.PI / 2f;
                offset = new Vector2(side * size * (.48f + .07f * (row % 2)),
                    (row - (rows - 1f) / 2f) * size * .27f);
                break;
            }
            case 9: // The constellation folds inward as Malady reaches for the Human Soul.
                angle = index * 2.399963f - Age * .009f;
                float soulRadius = size * (.82f - fraction * .48f) * (1f - attack * .22f);
                offset = new Vector2(MathF.Cos(angle) * soulRadius, MathF.Sin(angle) * soulRadius * .68f);
                break;
            default: // Before lethal damage, Apotheosis previews all geometries as a double crown.
                angle = index * 2.399963f + Age * (index % 2 == 0 ? .012f : -.01f);
                float crownRadius = size * (.48f + .18f * (index % 3));
                offset = new Vector2(MathF.Cos(angle) * crownRadius, MathF.Sin(angle) * crownRadius * .62f);
                break;
        }
        offset *= spectacle * (1f + attack * .1f);
        return (offset, angle, offset.Y);
    }

    /// <summary>
    /// Apotheosis grows an impossible inspiration flower behind the Empress.
    /// Its petals are non-damaging visual geometry, allowing the finale to
    /// become more extravagant without consuming the projectile budget.
    /// </summary>
    private void DrawApotheosisMandala(SpriteBatch spriteBatch, Vector2 core, Color luminous)
    {
        int petals = ApotheosisCrownPetalCount;
        if (petals == 0)
            return;
        float progress = (float)FinaleProgress;
        float innerRadius = Size * (.7f + progress * .18f);
        float outerRadius = Size * (1.05f + progress * .55f);
        Span<Vector2> petal = stackalloc Vector2[4];
        for (int index = 0; index < petals; index++)
        {
            float angle = index * MathF.Tau / petals + Age *
                (index % 2 == 0 ? .0028f : -.0022f);
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var tangent = new Vector2(-direction.Y, direction.X);
            var inner = core + direction * innerRadius;
            var outer = core + direction * outerRadius;
            float width = Size * (.08f + .035f * MathF.Sin(index * 1.7f + Age * .01f));
            petal[0] = inner - tangent * width;
            petal[1] = outer;
            petal[2] = inner + tangent * width;
            petal[3] = core + direction * innerRadius * .78f;
            Color color = index % 3 == 0 ? UiTheme.Cream : luminous;
            Primitives2D.FillPolygonSpan(
                spriteBatch,
                petal,
                color * (.08f + progress * .08f));
        }
    }

    public override void Draw(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        base.Draw(spriteBatch, camera, playerWorldPosition, screenShake);
        if (!Dying)
            foreach (var portal in ProjectilePortals)
                portal.Draw(spriteBatch, camera, playerWorldPosition, screenShake);
    }
}
