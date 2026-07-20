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
/// timer runs out, a post-lethal "collapse" death choreography instead of
/// dying outright, and a fully custom procedural pillar-and-slab render
/// (replacing `PhantasiaBoss`'s generic arena+ellipse+mask body via
/// <see cref="HasCustomDreamBody"/>).
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
    protected override bool UsesDreamRules => false;
    protected override bool UsesSharedDeathSpectacle => false;
    protected override bool VisualSurvivalActive => SurvivalActive || FinaleActive || base.VisualSurvivalActive;
    private static readonly Dictionary<int, double> SurvivalPhases = new() { [6] = 22.0 };
    private static readonly int[] PortalCounts = { 3, 4, 3, 4, 5, 3, 6, 4, 5, 6 };
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
        FinalBodyScale = 2.55, FinalCooldownSeconds = 1.25,
        MovementSpeed = .15, ArenaScale = 13.5,
        MovementModes = new[] { "path", "path", "static", "path", "path", "static", "path", "static", "path", "static" },
        FinalHealth = 320000, FinalContactDamage = 900, FinalRewardExperience = 880,
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

    private readonly record struct ChainEvent(double Delay, Vector2 Origin, float Direction, float Speed, float Damage);

    public List<ProjectilePortal> ProjectilePortals { get; } = new();
    private int _portalFormationPhase;
    private List<ChainEvent> _sequenceQueue = new();
    private double _poolCooldown = 1.2;
    private readonly float[] _pillarMotion = { 0f, 0f };
    private float _pillarMotionStrength;

    public bool SurvivalActive { get; private set; }
    public double SurvivalRemaining { get; private set; }
    public string AttackPose { get; private set; } = "idle";
    public float AttackAimAngle { get; private set; }
    public double AttackAnimationDuration { get; } = .72;
    public float AttackAnticipation { get; private set; }
    public bool Collapsing => Dying;
    public double CollapseDuration => DeathDuration;
    public double CollapseRemaining => DeathRemaining;

    public Malady(float worldX, float worldY, Battleground battleground, Random? rng = null)
        : base(worldX, worldY, battleground, MaladyConfig, MaladySigilConfig, rng)
    {
        ActTitle = "ACT I // THE FIRST IDEA";
        ActTransitionTimer = ActTransitionDuration;
        PhaseProtectionTimer = ActTransitionDuration;
    }

    protected override void SetDreamPhase(int phase)
    {
        base.SetDreamPhase(phase);
        _sequenceQueue.Clear();
        ClearMaladyPortals();
        _poolCooldown = .8;
        SurvivalActive = SurvivalPhases.TryGetValue(Phase, out var duration);
        SurvivalRemaining = duration;
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
        return base.TakeDamage(amount, partId, source);
    }

    private string AttackPoseForPhase() => Phase switch
    {
        3 or 7 or 9 => "laser",
        4 or 5 => "chain",
        2 or 8 => "radial",
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

    private EnemyProjectile SpawnPool(List<EnemyProjectile> sink, Vector2 position, double duration = 7.0, double scale = 1.0)
    {
        float size = Simulation.TileSize * 2.35f * (float)scale;
        var pool = new EnemyProjectile(position.X - size / 2f, position.Y - size / 2f, 0f, 0f, 285, size,
            color: new Color(147, 57, 190), shape: "pool", path: "pool", lifetime: (float)duration,
            owner: $"{Config.OwnerPrefix}_purple_pool", ignoreWalls: true)
        {
            TelegraphDuration = 1.05f, PersistentHazard = true, TruthMarked = true, BeliefGain = .35,
        };
        sink.Add(pool);
        return pool;
    }

    private void QueueChain(Vector2 origin, float startAngle, float arc, int count = 16, double interval = .055, float speed = .74f, float damage = 335)
    {
        for (int index = 0; index < count; index++)
        {
            float fraction = index / (float)Math.Max(1, count - 1);
            _sequenceQueue.Add(new ChainEvent(index * interval, origin, startAngle + arc * fraction,
                speed * (1.0f - .18f * MathF.Sin(fraction * MathF.PI)), damage));
        }
    }

    private void UpdateSequences(List<EnemyProjectile> sink, double dt)
    {
        var remaining = new List<ChainEvent>();
        foreach (var chainEvent in _sequenceQueue)
        {
            double delay = chainEvent.Delay - dt;
            if (delay <= 0)
            {
                ShotFrom(sink, chainEvent.Origin, chainEvent.Direction, chainEvent.Speed, chainEvent.Damage, "flowing_chain",
                    shape: "diamond", belief: .38, sizeScale: .82f);
            }
            else
            {
                remaining.Add(chainEvent with { Delay = delay });
            }
        }
        _sequenceQueue = remaining;
    }

    private void FirePortalPhrase(List<EnemyProjectile> sink, Vector2 target, bool wide = false)
    {
        if (ProjectilePortals.Count == 0)
            return;
        var portal = ProjectilePortals[PatternRotation % ProjectilePortals.Count];
        var waves = new[]
        {
            new BurstWave(3, .28f, .72f, .30f),
            new BurstWave(5, wide ? .72f : .42f, .96f, .24f),
            new BurstWave(3, .18f, 1.26f, .20f),
        };
        portal.FirePatternBurst(sink, target, waves, waveInterval: .12f, damage: 325, color: PhaseAccent, ownerSuffix: "dream_burst");
    }

    private void UpdatePillarMotion(float previousX, float previousY, Camera? camera)
    {
        if (camera is null)
            return;
        float deltaX = WorldX - previousX, deltaY = WorldY - previousY;
        var screenDelta = camera.WorldVectorToScreen(new Vector2(deltaX, deltaY));
        float magnitude = MathF.Sqrt(screenDelta.X * screenDelta.X + screenDelta.Y * screenDelta.Y);
        if (magnitude > .005f)
        {
            float targetX = screenDelta.X / magnitude, targetY = screenDelta.Y / magnitude;
            _pillarMotion[0] += (targetX - _pillarMotion[0]) * .22f;
            _pillarMotion[1] += (targetY - _pillarMotion[1]) * .22f;
            _pillarMotionStrength += (1.0f - _pillarMotionStrength) * .18f;
        }
        else
        {
            _pillarMotion[0] *= .86f;
            _pillarMotion[1] *= .86f;
            _pillarMotionStrength *= .84f;
        }
    }

    public override void Update(EnemyUpdateContext context)
    {
        double dt = Seconds();
        float previousX = WorldX, previousY = WorldY;
        if (Dying)
        {
            base.Update(context);
            return;
        }

        EnsureMaladyPortals();
        UpdateSequences(context.ProjectileSink, dt);
        foreach (var portal in ProjectilePortals)
        {
            portal.OrbitCenter = ArenaCenter;
            portal.Angle += portal.AngularSpeed * (float)dt;
            portal.Place();
            portal.UpdateBursts(context.ProjectileSink, (float)dt);
        }

        if (EntranceRemaining <= 0 && ActTransitionTimer <= 0)
        {
            _poolCooldown -= dt;
            double poolRate = SurvivalActive ? 1.65 : 4.2;
            if (_poolCooldown <= 0 && (SurvivalActive || Phase is 2 or 5 or 8 or 10))
            {
                float angle = (float)(Rng.NextDouble() * 2 * Math.PI);
                float radius = ArenaRadius * (float)(.18 + Rng.NextDouble() * (.64 - .18));
                var position = ArenaCenter + new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius);
                SpawnPool(context.ProjectileSink, position, duration: SurvivalActive ? 5.8 : 7.5, scale: .82 + Rng.NextDouble() * .36);
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

        base.Update(context);
        UpdatePillarMotion(previousX, previousY, context.Camera);
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
        QueueChain(origin, aimed - arc / 2f, arc, count, interval: .052, speed: speed, damage: 350);
    }

    protected override void FirePhantasiaPattern(float playerX, float playerY, EnemyUpdateContext context)
    {
        var center = Center();
        var target = new Vector2(playerX, playerY);
        float aimed = MathF.Atan2(playerY - center.Y, playerX - center.X);
        AttackAimAngle = aimed;
        AttackPose = AttackPoseForPhase();
        var sink = context.ProjectileSink;

        switch (Phase)
        {
            case 1: // Petals open around a lane pointing directly at the player.
                RadialWithGap(sink, center, target, 14, 1, .58f, 320, "overture_petals", "sine");
                break;
            case 2: // Alternating portals flood inward but preserve the player's current shore.
                for (int index = PatternRotation % 2; index < ProjectilePortals.Count; index += 2)
                    RadialWithGap(sink, PortalOrigin(index), target, 10, 1, .62f, 325, "petal_flood", "sine");
                break;
            case 3: // Two immense actuator arcs assemble an impossible engine around the open seam.
                PortalTentacle(sink, PatternRotation, target, 2.35f, "impossible_engine_left", 24, .64f);
                PortalTentacle(sink, PatternRotation + 2, target, -2.35f, "impossible_engine_right", 24, .64f);
                break;
            case 4: // Long ribbons arrive slowly enough to follow through the court.
                for (int index = 0; index < Math.Min(4, ProjectilePortals.Count); index++)
                    PortalTentacle(sink, index, target, index % 2 == 0 ? 1.8f : -1.8f, "ribbon_court", 18, .7f);
                break;
            case 5: // Splitting tendrils grow outward, never spawning directly inside the marked opening.
                for (int index = -2; index <= 2; index++)
                {
                    if (index == 0)
                        continue;
                    var shot = ShotFrom(sink, center, aimed + index * .38f, .62f, 335, "tentacle_garden", path: "sine");
                    shot.SplitCount = 2;
                    shot.SplitAt = Simulation.TileSize * (3.2f + Math.Abs(index) * .7f);
                    shot.SplitGeneration = 2;
                }
                break;
            case 6: // Halfway survival: a flower closes everywhere except a broad, player-facing wedge.
                RadialWithGap(sink, center, target, 20, 2, .52f, 340, "intermission_flower", "sine");
                PortalTentacle(sink, PatternRotation, target, PatternRotation % 2 == 0 ? 1.7f : -1.7f,
                    "intermission_tentacle", 20, .62f);
                break;
            case 7:
                RadialWithGap(sink, center, target, 18, 2, .68f, 355, "luminous_tide", "sine");
                FirePortalPhrase(sink, target, wide: true);
                break;
            case 8: // A cathedral of fully telegraphed portal lasers leaves two adjacent aisles open.
            {
                int count = Math.Max(1, ProjectilePortals.Count);
                float playerAngle = MathF.Atan2(playerY - ArenaCenter.Y, playerX - ArenaCenter.X);
                int aisle = (int)MathF.Round(((playerAngle % MathF.Tau + MathF.Tau) % MathF.Tau) / (MathF.Tau / count)) % count;
                for (int index = 0; index < count; index++)
                {
                    int distance = Math.Min((index - aisle + count) % count, (aisle - index + count) % count);
                    if (distance <= 1)
                        continue;
                    var origin = PortalOrigin(index);
                    LaserFrom(sink, origin, MathF.Atan2(center.Y - origin.Y, center.X - origin.X), 390, "violet_cathedral");
                }
                break;
            }
            case 9:
                for (int index = 0; index < 3; index++)
                    PortalTentacle(sink, PatternRotation + index * 2, target, (index - 1) * 2.1f, "soul_invasion_tentacle", 26, .72f);
                RadialWithGap(sink, center, target, 22, 2, .6f, 365, "soul_invasion_bloom");
                break;
            default: // Apotheosis cycles the fight's signature ideas for the full forty seconds.
            {
                int movement = PatternRotation % 3;
                if (movement == 0)
                {
                    RadialWithGap(sink, center, target, 24, 2, .7f, 390, "apotheosis_flood", "sine");
                    FirePortalPhrase(sink, target, wide: true);
                }
                else if (movement == 1)
                {
                    for (int index = 0; index < 4; index++)
                        PortalTentacle(sink, PatternRotation + index, target, index % 2 == 0 ? 2.4f : -2.4f,
                            "apotheosis_tentacle", 24, .74f);
                }
                else
                {
                    RadialWithGap(sink, center, target, 28, 3, .58f, 380, "apotheosis_corolla");
                    for (int index = 0; index < ProjectilePortals.Count; index += 2)
                    {
                        var origin = PortalOrigin(index);
                        LaserFrom(sink, origin, MathF.Atan2(center.Y - origin.Y, center.X - origin.X), 410, "apotheosis_laser");
                    }
                }
                break;
            }
        }
        if (!SurvivalPhases.ContainsKey(Phase) && PatternRotation % 2 == 0)
            FirePortalPhrase(sink, target, wide: Phase >= 7);
        PatternRotation++;
        MarkAttack(.72f);
    }

    protected override bool HasCustomDreamBody => true;

    protected override void DrawBossBody(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
        => DrawDreamBody(spriteBatch, camera, playerWorldPosition, screenShake);

    private static Vector2[] OffsetPoints(IReadOnlyList<Vector2> points, float x, float y) =>
        points.Select(p => p + new Vector2(x, y)).ToArray();

    private void DrawLimbBlock(SpriteBatch spriteBatch, Vector2 start, Vector2 end, float width, Color color, float taper = .82f)
    {
        float deltaX = end.X - start.X, deltaY = end.Y - start.Y;
        float length = Math.Max(1.0f, MathF.Sqrt(deltaX * deltaX + deltaY * deltaY));
        float normalX = -deltaY / length, normalY = deltaX / length;
        float startHalf = width / 2f, endHalf = width * taper / 2f;
        var points = new[]
        {
            new Vector2(start.X + normalX * startHalf, start.Y + normalY * startHalf),
            new Vector2(end.X + normalX * endHalf, end.Y + normalY * endHalf),
            new Vector2(end.X - normalX * endHalf, end.Y - normalY * endHalf),
            new Vector2(start.X - normalX * startHalf, start.Y - normalY * startHalf),
        };
        Primitives2D.FillPolygon(spriteBatch, OffsetPoints(points, 5, 7), UiTheme.Shadow);
        Primitives2D.FillPolygon(spriteBatch, points, UiTheme.Ink);
        float inset = Math.Max(2.0f, width * .11f);
        var inner = new[]
        {
            new Vector2(start.X + normalX * (startHalf - inset), start.Y + normalY * (startHalf - inset)),
            new Vector2(end.X + normalX * (endHalf - inset), end.Y + normalY * (endHalf - inset)),
            new Vector2(end.X - normalX * (endHalf - inset), end.Y - normalY * (endHalf - inset)),
            new Vector2(start.X - normalX * (startHalf - inset), start.Y - normalY * (startHalf - inset)),
        };
        Primitives2D.FillPolygon(spriteBatch, inner, color);
        Primitives2D.Line(spriteBatch, inner[0], inner[1], UiTheme.Lighten(color, 38), Math.Max(2, (int)(width * .07f)));
    }

    private void DrawAttackHand(SpriteBatch spriteBatch, Vector2 joint, Vector2 end, float width)
    {
        float deltaX = end.X - joint.X, deltaY = end.Y - joint.Y;
        float length = Math.Max(1.0f, MathF.Sqrt(deltaX * deltaX + deltaY * deltaY));
        var direction = new Vector2(deltaX / length, deltaY / length);
        var perpendicular = new Vector2(-direction.Y, direction.X);
        Color color = PhaseAccent;
        switch (AttackPose)
        {
            case "laser":
                foreach (int side in new[] { -1, 1 })
                {
                    var start = end + perpendicular * width * .22f * side;
                    var tip = start + direction * width * .62f;
                    Primitives2D.Line(spriteBatch, start, tip, UiTheme.Ink, Math.Max(5, (int)(width * .18f)));
                    Primitives2D.Line(spriteBatch, start, tip, UiTheme.Cream, Math.Max(2, (int)(width * .07f)));
                }
                break;
            case "chain":
            {
                var chainCenter = end + direction * width * .28f;
                var diamond = new[]
                {
                    chainCenter + direction * width * .38f, chainCenter + perpendicular * width * .3f,
                    chainCenter - direction * width * .38f, chainCenter - perpendicular * width * .3f,
                };
                Primitives2D.FillPolygon(spriteBatch, OffsetPoints(diamond, 4, 5), UiTheme.Shadow);
                Primitives2D.FillPolygon(spriteBatch, diamond, UiTheme.Ink);
                Primitives2D.PolygonOutline(spriteBatch, diamond, color, Math.Max(2, (int)(width * .1f)));
                break;
            }
            case "burst":
                foreach (float spread in new[] { -.48f, 0f, .48f })
                {
                    float rayX = direction.X * MathF.Cos(spread) - direction.Y * MathF.Sin(spread);
                    float rayY = direction.X * MathF.Sin(spread) + direction.Y * MathF.Cos(spread);
                    var tip = end + new Vector2(rayX, rayY) * width * .65f;
                    Primitives2D.Line(spriteBatch, end, tip, UiTheme.Ink, Math.Max(5, (int)(width * .16f)));
                    Primitives2D.Line(spriteBatch, end, tip, color, Math.Max(2, (int)(width * .07f)));
                }
                break;
            case "radial":
                int radius = Math.Max(4, (int)(width * .36f));
                Primitives2D.FillCircle(spriteBatch, end, radius + 4, UiTheme.Ink);
                Primitives2D.CircleOutline(spriteBatch, end, radius, color, 3);
                Primitives2D.FillCircle(spriteBatch, end, Math.Max(2, radius / 4), UiTheme.Cream);
                break;
        }
    }

    private void DrawPuppetGroundSigil(SpriteBatch spriteBatch, Vector2 center)
    {
        var ground = center + new Vector2(0, Size * .62f);
        var ring = new Rectangle((int)(ground.X - Size * .71f), (int)(ground.Y - Size * .21f), (int)(Size * 1.42f), (int)(Size * .42f));
        var ringOuter = ring;
        ringOuter.Inflate(12, 8);
        Primitives2D.EllipseOutline(spriteBatch, ringOuter, UiTheme.Shadow, 7);
        Primitives2D.EllipseOutline(spriteBatch, ring, Color.Lerp(PhaseAccent, UiTheme.Void, .5f), 3);
        DrawCommandmentSigil(spriteBatch, ground, Size * .34f, 1.0, null, 115, Age * -.0012);
    }

    private void DrawPuppetArm(SpriteBatch spriteBatch, Camera camera, Vector2 core, int side, Color bodyColor, float attackAmount, float reassembly = 0.0f)
    {
        float size = Size;
        float driftX = -_pillarMotion[0] * size * .2f * _pillarMotionStrength;
        float driftY = -_pillarMotion[1] * size * .13f * _pillarMotionStrength;
        float idle = Age * .018f + side * 1.7f;
        var shoulder = new Vector2(core.X + side * size * .3f, core.Y - size * .15f);
        var points = new[]
        {
            shoulder,
            new Vector2(core.X + side * size * .58f + driftX * .25f, core.Y - size * .2f + MathF.Sin(idle) * size * .07f + driftY * .25f),
            new Vector2(core.X + side * size * .88f + driftX * .62f, core.Y - size * .05f + MathF.Sin(idle + .9f) * size * .1f + driftY * .62f),
            new Vector2(core.X + side * size * 1.18f + driftX, core.Y + size * .08f + MathF.Sin(idle + 1.7f) * size * .12f + driftY),
        };
        for (int index = 1; index < 4; index++)
        {
            float scatter = reassembly * size * (.08f + index * .055f);
            points[index] = new Vector2(points[index].X + side * scatter,
                points[index].Y + MathF.Sin(Age * .024f + index * 1.3f + side) * scatter * .7f);
        }

        var aim = camera.WorldVectorToScreen(new Vector2(MathF.Cos(AttackAimAngle), MathF.Sin(AttackAimAngle)));
        float aimLength = Math.Max(1e-6f, MathF.Sqrt(aim.X * aim.X + aim.Y * aim.Y));
        aim /= aimLength;
        var perpendicular = new Vector2(-aim.Y, aim.X);
        if (AttackPose == "laser")
        {
            var target = core + aim * size * 1.28f + perpendicular * side * size * .22f;
            for (int index = 1; index < 4; index++)
            {
                float fraction = index / 3f;
                var attackPoint = shoulder + (target - shoulder) * fraction;
                points[index] = points[index] * (1 - attackAmount) + attackPoint * attackAmount;
            }
        }
        else if (AttackPose == "chain" && side == (aim.X >= 0 ? 1 : -1))
        {
            var target = new Vector2(core.X + aim.X * size * 1.52f, core.Y + aim.Y * size * 1.52f - size * .18f);
            float arc = perpendicular.Y * size * .25f;
            for (int index = 1; index < 4; index++)
            {
                float fraction = index / 3f;
                var attackPoint = new Vector2(
                    shoulder.X + (target.X - shoulder.X) * fraction,
                    shoulder.Y + (target.Y - shoulder.Y) * fraction - MathF.Sin(fraction * MathF.PI) * arc);
                points[index] = points[index] * (1 - attackAmount) + attackPoint * attackAmount;
            }
        }
        else if (AttackPose == "burst")
        {
            points[3] = new Vector2(points[3].X + side * size * .23f * attackAmount, points[3].Y - size * .32f * attackAmount);
            points[2] = new Vector2(points[2].X + side * size * .12f * attackAmount, points[2].Y - size * .17f * attackAmount);
        }
        else if (AttackPose == "radial")
        {
            float orbitAngle = Age * .035f + (side > 0 ? 0f : MathF.PI);
            var target = new Vector2(core.X + MathF.Cos(orbitAngle) * size * 1.18f, core.Y + MathF.Sin(orbitAngle) * size * .72f);
            points[3] = points[3] * (1 - attackAmount) + target * attackAmount;
        }

        float[] widths = { size * .27f, size * .24f, size * .2f };
        for (int index = 0; index < 3; index++)
        {
            DrawLimbBlock(spriteBatch, points[index], points[index + 1], widths[index], bodyColor, .78f);
            if (index < 2)
            {
                Primitives2D.FillCircle(spriteBatch, points[index + 1], (int)(widths[index] * .34f), UiTheme.Ink);
                Primitives2D.FillCircle(spriteBatch, points[index + 1], Math.Max(2, (int)(widths[index] * .18f)), PhaseAccent);
            }
        }
        int handDirection = MathF.Abs(points[3].X - points[2].X) < 1 ? side : (points[3].X > points[2].X ? 1 : -1);
        var handEnd = new Vector2(points[3].X + handDirection * size * .18f, points[3].Y + size * .04f);
        DrawLimbBlock(spriteBatch, points[3], handEnd, size * .28f, PhaseAccent, .62f);
        DrawAttackHand(spriteBatch, points[3], handEnd, size * .28f);
    }

    private void DrawSurvivalDance(SpriteBatch spriteBatch, Vector2 core, Color bodyColor)
    {
        float size = Size;
        bool finale = FinaleActive;
        int count = finale ? 12 : 8;
        float speed = finale ? .031f : .022f;
        for (int index = 0; index < count; index++)
        {
            float angle = Age * speed * (index % 2 == 1 ? -1f : 1f) + index * 2f * MathF.PI / count;
            float radius = size * (finale ? (.98f + .25f * (index % 3)) : (.55f + .1f * (index % 2)));
            var danceCenter = new Vector2(core.X + MathF.Cos(angle) * radius, core.Y + MathF.Sin(angle * (finale ? 1.35f : 1.0f)) * radius * .68f);
            float tangent = angle + MathF.PI / 2f + MathF.Sin(Age * .017f + index) * .38f;
            float half = size * (.13f + .025f * (index % 3));
            var start = new Vector2(danceCenter.X - MathF.Cos(tangent) * half, danceCenter.Y - MathF.Sin(tangent) * half);
            var end = new Vector2(danceCenter.X + MathF.Cos(tangent) * half, danceCenter.Y + MathF.Sin(tangent) * half);
            Color color = index % 3 == 0 ? PhaseAccent : bodyColor;
            DrawLimbBlock(spriteBatch, start, end, size * (.17f + .02f * (index % 2)), color);
        }
        int slabCount = finale ? 3 : 2;
        for (int index = 0; index < slabCount; index++)
        {
            float angle = -Age * (.019f + index * .004f) + index * MathF.PI;
            var point = new Vector2(core.X + MathF.Cos(angle) * size * .42f, core.Y + size * .36f + MathF.Sin(angle) * size * .19f);
            var slabEnd = new Vector2(point.X + MathF.Cos(angle + .3f) * size * .38f, point.Y + MathF.Sin(angle + .3f) * size * .12f);
            DrawLimbBlock(spriteBatch, point, slabEnd, size * .27f, bodyColor, .72f);
        }
    }

    private void DrawCollapseChoreography(SpriteBatch spriteBatch, Vector2 core, Color bodyColor)
    {
        double progress = Math.Clamp(1.0 - CollapseRemaining / CollapseDuration, 0.0, 1.0);
        for (int index = 0; index < 16; index++)
        {
            float angle = index * 2f * MathF.PI / 16f + Age * (index % 2 == 1 ? .017f : -.013f);
            float distance = Size * (.2f + (float)progress * (1.05f + .12f * (index % 4)));
            float fall = Size * (float)(progress * progress) * (.35f + .08f * (index % 5));
            var center = new Vector2(core.X + MathF.Cos(angle) * distance, core.Y + MathF.Sin(angle) * distance * .58f + fall);
            float tangent = angle + MathF.PI / 2f + (float)progress * MathF.PI * (index % 2 == 1 ? 1f : -1f);
            float half = Size * (.08f + .018f * (index % 3)) * (1f - (float)progress * .38f);
            var start = new Vector2(center.X - MathF.Cos(tangent) * half, center.Y - MathF.Sin(tangent) * half);
            var end = new Vector2(center.X + MathF.Cos(tangent) * half, center.Y + MathF.Sin(tangent) * half);
            Color color = index % 3 == 0 ? PhaseAccent : bodyColor;
            DrawLimbBlock(spriteBatch, start, end, Size * (.13f + .015f * (index % 2)), color);
        }
        float beamLength = Size * (1.1f + (float)progress * 2.2f);
        for (int index = 0; index < 6; index++)
        {
            float angle = index * MathF.PI / 3f - Age * .009f;
            var end = core + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * beamLength;
            Primitives2D.Line(spriteBatch, core, end, UiTheme.Ink, 6);
            Primitives2D.Line(spriteBatch, core, end, PhaseAccent, 2);
        }
        float shellScale = Math.Max(.08f, 1f - (float)progress * 1.12f);
        var shell = new Rectangle((int)(core.X - Size * .31f * shellScale), (int)(core.Y - Size * .365f * shellScale),
            (int)(Size * .62f * shellScale), (int)(Size * .73f * shellScale));
        Primitives2D.FillRect(spriteBatch, shell, UiTheme.Ink);
        var shellInner = shell;
        shellInner.Inflate(-6, -6);
        Primitives2D.FillRect(spriteBatch, shellInner, bodyColor);
        int coreRadius = Math.Max(2, (int)(Size * .11f * (progress > .72 ? 1f - (float)progress : 1f + (float)progress * .8f)));
        Primitives2D.FillCircle(spriteBatch, core, coreRadius + 7, UiTheme.Ink);
        Primitives2D.FillCircle(spriteBatch, core, coreRadius, UiTheme.Cream);
        for (int ringIndex = 0; ringIndex < 4; ringIndex++)
        {
            double cycle = (progress * 3 + ringIndex / 4.0) % 1;
            Primitives2D.CircleOutline(spriteBatch, core, Size * (.16f + (float)cycle * 1.3f), PhaseAccent, Math.Max(1, (int)(4 * (1 - cycle))));
        }
    }

    /// <summary>
    /// Ported from _draw_dream_body. Python's own copy mutates
    /// `self.visualAttackTimer` inside this *draw* method -- redundant with
    /// (and double-decrementing against) the decay `Enemy.Update`'s
    /// `AdvanceAge()` already performs every tick. Dropped here, same
    /// Update-mutates/Draw-only-reads split already established for every
    /// other entity in this port (see Enemy.cs's doc comment).
    /// </summary>
    private void DrawLegacyPuppetBody(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        float attackProgress = Math.Min(1.0f, VisualAttackTimer / Math.Max(1f, Simulation.FrameRate * (float)AttackAnimationDuration));
        float attackAmount = Math.Max(MathF.Sin(attackProgress * MathF.PI), AttackAnticipation * .62f);
        float stillness = 1.0f - Math.Min(1.0f, _pillarMotionStrength);
        float bob = MathF.Sin(Age * .025f) * Size * .035f * (.45f + stillness * .55f);
        float leanX = _pillarMotion[0] * Size * .07f * _pillarMotionStrength;
        float leanY = _pillarMotion[1] * Size * .035f * _pillarMotionStrength;
        Vector2 screenPosition = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        var core = new Vector2(screenPosition.X + Size / 2f + leanX, screenPosition.Y + Size * .47f + bob + leanY);
        Color bodyColor = Color.Lerp(Config.FinalBodyColor, PhaseAccent, .18f);
        Color bright = UiTheme.Lighten(bodyColor, 34);
        double transition = Math.Clamp(SigilTransitionTimer / SigilTransitionDuration, 0.0, 1.0);
        float reassembly = (float)(transition * transition * (3 - 2 * transition));

        var shadowCenter = new Vector2(core.X - leanX * .4f, core.Y + Size * .62f);
        var shadow = new Rectangle((int)(shadowCenter.X - Size * .86f), (int)(shadowCenter.Y - Size * .21f), (int)(Size * 1.72f), (int)(Size * .42f));
        Primitives2D.FillEllipse(spriteBatch, shadow, UiTheme.Shadow);
        DrawPuppetGroundSigil(spriteBatch, core);

        if (Collapsing)
        {
            DrawCollapseChoreography(spriteBatch, core, bodyColor);
            return;
        }

        if (SurvivalActive)
        {
            DrawSurvivalDance(spriteBatch, core, bodyColor);
        }
        else
        {
            DrawPuppetArm(spriteBatch, camera, core, -1, bodyColor, attackAmount, reassembly);
            DrawPuppetArm(spriteBatch, camera, core, 1, bodyColor, attackAmount, reassembly);
        }

        float baseShift = -_pillarMotion[0] * Size * .08f * _pillarMotionStrength - reassembly * Size * .12f;
        float baseY = core.Y + Size * .35f;
        var leftBase = new[]
        {
            new Vector2(core.X - Size * .66f + baseShift, baseY - Size * .08f),
            new Vector2(core.X - Size * .05f + baseShift, baseY - Size * .03f),
            new Vector2(core.X - Size * .11f + baseShift, baseY + Size * .27f),
            new Vector2(core.X - Size * .78f + baseShift, baseY + Size * .18f),
        };
        var rightBase = leftBase.Select(p => new Vector2(2 * core.X - p.X + baseShift * 2, p.Y)).ToArray();
        foreach (var basePoints in new[] { leftBase, rightBase })
        {
            Primitives2D.FillPolygon(spriteBatch, OffsetPoints(basePoints, 6, 8), UiTheme.Shadow);
            Primitives2D.FillPolygon(spriteBatch, basePoints, UiTheme.Ink);
            var inset = basePoints.Select(p => new Vector2(p.X + (core.X - p.X) * .06f, p.Y + (core.Y - p.Y) * .06f)).ToArray();
            Primitives2D.FillPolygon(spriteBatch, inset, bodyColor);
        }
        Primitives2D.Line(spriteBatch, leftBase[0], leftBase[1], bright, 3);
        Primitives2D.Line(spriteBatch, rightBase[0], rightBase[1], bright, 3);

        var torso = new Rectangle((int)(core.X - Size * .31f), (int)(core.Y + Size * .02f - Size * .365f), (int)(Size * .62f), (int)(Size * .73f));
        var torsoShadow = new Rectangle(torso.X + 6, torso.Y + 8, torso.Width, torso.Height);
        Primitives2D.FillRect(spriteBatch, torsoShadow, UiTheme.Shadow);
        Primitives2D.FillRect(spriteBatch, torso, UiTheme.Ink);
        var chest = torso;
        chest.Inflate(-(int)(Size * .08f), -(int)(Size * .08f));
        Primitives2D.FillRect(spriteBatch, chest, bodyColor);
        Primitives2D.Line(spriteBatch, new Vector2(chest.Left, chest.Top), new Vector2(chest.Right, chest.Top), bright, 3);

        float headBob = MathF.Sin(Age * .031f + .8f) * Size * .035f;
        var headMidBottom = new Vector2(core.X + leanX * .35f, torso.Top - Size * (.09f + reassembly * .24f) + headBob);
        var head = new Rectangle((int)(headMidBottom.X - Size * .215f), (int)(headMidBottom.Y - Size * .29f), (int)(Size * .43f), (int)(Size * .29f));
        var headShadow = new Rectangle(head.X + 5, head.Y + 7, head.Width, head.Height);
        Primitives2D.FillRect(spriteBatch, headShadow, UiTheme.Shadow);
        Primitives2D.FillRect(spriteBatch, head, UiTheme.Ink);
        var headInner = head;
        headInner.Inflate(-6, -6);
        Primitives2D.FillRect(spriteBatch, headInner, bodyColor);
        float eyeY = head.Center.Y, eyeOffset = Size * .1f;
        Primitives2D.Line(spriteBatch, new Vector2(head.Center.X - eyeOffset, eyeY), new Vector2(head.Center.X + eyeOffset, eyeY),
            PhaseAccent, Math.Max(2, (int)(Size * .025f)));

        var torsoCenter = new Vector2(torso.Center.X, torso.Center.Y);
        DrawCommandmentSigil(spriteBatch, torsoCenter, Size * .19f, 1.0, null, 255, MathF.Sin(Age * .008f) * .08f);
        int coreRadius = Math.Max(4, (int)(Size * (.055f + .012f * MathF.Sin(Age * .05f))));
        Primitives2D.FillCircle(spriteBatch, torsoCenter, coreRadius + 4, UiTheme.Ink);
        Primitives2D.FillCircle(spriteBatch, torsoCenter, coreRadius, UiTheme.Cream);
        if (reassembly > .02f)
        {
            float radius = Size * (.36f + reassembly * .72f);
            Primitives2D.CircleOutline(spriteBatch, torsoCenter, radius, PhaseAccent, Math.Max(1, (int)(4 * reassembly)));
            for (int index = 0; index < 8; index++)
            {
                float angle = index * MathF.PI / 4f + Age * .014f;
                var point = new Vector2(torsoCenter.X + MathF.Cos(angle) * radius, torsoCenter.Y + MathF.Sin(angle) * radius * .58f);
                Primitives2D.FillRect(spriteBatch, new Rectangle((int)(point.X - 2), (int)(point.Y - 2), 4, 4), UiTheme.Cream);
            }
        }
        if (Hp < MaxHp)
        {
            var barMidBottom = new Vector2(core.X, head.Top - 10);
            var bar = new Rectangle((int)(barMidBottom.X - Size * .43f), (int)(barMidBottom.Y - 6), (int)(Size * .86f), 6);
            Primitives2D.FillRect(spriteBatch, bar, UiTheme.Ink);
            var fill = bar;
            fill.Width = (int)(bar.Width * Math.Max(0f, (float)Hp / MaxHp));
            Primitives2D.FillRect(spriteBatch, fill, PhaseAccent);
        }
    }

    /// <summary>
    /// Malady's imperial silhouette: a tall indigo core floating over an
    /// obsidian foundation while loose cubes continuously invent and abandon
    /// possible bodies around it. Attack motion changes the constellation,
    /// never the Empress's composed central posture.
    /// </summary>
    protected override void DrawDreamBody(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        Vector2 screen = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        float bob = MathF.Sin(Age * .018f) * Size * .035f;
        Vector2 core = screen + new Vector2(Size / 2f - _pillarMotion[0] * Size * .045f * _pillarMotionStrength,
            Size * .45f + bob - _pillarMotion[1] * Size * .025f * _pillarMotionStrength);
        Color indigo = new(62, 39, 116);
        Color violet = new(105, 59, 164);
        Color luminous = Color.Lerp(new Color(218, 104, 232), PhaseAccent, .36f);
        float spectacle = FinaleActive ? 1.58f : SurvivalActive ? 1.3f : 1f;
        float attack = Math.Max(AttackAnticipation,
            VisualAttackTimer > 0 ? MathF.Sin(Math.Clamp(VisualAttackTimer / (Simulation.FrameRate * .72f), 0f, 1f) * MathF.PI) : 0f);

        // The slab belongs to the room, not Malady's hitbox or locomotion.
        // Anchor it to the arena center and align its square footprint with
        // the rotating world axes so it remains a stationary floor landmark.
        Vector2 slabCenter = camera.WorldToScreen(ArenaCenter, playerWorldPosition, screenShake);
        Vector2 slabAxisX = camera.WorldVectorToScreen(Vector2.UnitX);
        Vector2 slabAxisY = camera.WorldVectorToScreen(Vector2.UnitY);
        BossVisuals.FloorSlab(spriteBatch, slabCenter, slabAxisX, slabAxisY, Size * 1.55f, Size * .13f,
            new Color(18, 15, 25), new Color(77, 58, 103));

        if (Dying)
        {
            BossVisuals.OscillatingAura(spriteBatch, core, Age, Size * (1f + DeathProgress * 2.2f), luminous, 7, 2.1f);
            for (int ray = 0; ray < 8; ray++)
            {
                float angle = ray * MathF.PI / 4f - Age * .009f;
                var end = core + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * Size * (1.1f + DeathProgress * 2.5f);
                Primitives2D.Line(spriteBatch, core, end, UiTheme.Ink, 7);
                Primitives2D.Line(spriteBatch, core, end, luminous, 2);
            }
            BossVisuals.Disassemble(spriteBatch, core, Age, DeathProgress, Size * 1.35f, indigo, luminous, 24);
            return;
        }

        BossVisuals.OscillatingAura(spriteBatch, core, Age, Size * .72f * spectacle, luminous,
            FinaleActive ? 8 : 5, .78f);
        for (int wave = 0; wave < (FinaleActive ? 7 : 4); wave++)
        {
            float width = Size * (.78f + wave * .24f) * spectacle;
            float height = Size * (.28f + wave * .09f);
            var phrase = new Rectangle((int)(core.X - width), (int)(core.Y - height), (int)(width * 2), (int)(height * 2));
            float start = Age * (.004f + wave * .0006f) + wave * .72f;
            Primitives2D.Arc(spriteBatch, phrase, start, start + MathF.PI * .78f,
                (wave % 2 == 0 ? luminous : UiTheme.Cream) * (.22f + wave * .045f), 2 + wave % 2);
        }

        int cubeCount = FinaleActive ? FinaleBodyCubeCount : SurvivalActive ? 14 : IdleBodyCubeCount;
        var floating = new List<(Vector2 Center, float Angle, float Depth, float Extent)>();
        float aimBias = AttackPose == "laser" ? AttackAimAngle : Age * .006f;
        for (int index = 0; index < cubeCount; index++)
        {
            float angle = index * 2.399963f + Age * (index % 2 == 0 ? .009f : -.007f) + aimBias * attack * .2f;
            float radius = Size * (.42f + (index % 5) * .11f) * spectacle * (1f + attack * .18f);
            float column = ((index % 4) - 1.5f) * Size * .18f;
            var point = core + new Vector2(MathF.Cos(angle) * radius, column + MathF.Sin(angle) * radius * .42f);
            floating.Add((point, angle, MathF.Sin(angle), Size * (.07f + index % 3 * .018f)));
        }

        foreach (var cube in floating.Where(cube => cube.Depth < 0).OrderBy(cube => cube.Depth))
            BossVisuals.RotatingCube3D(spriteBatch, cube.Center, cube.Extent, indigo, violet, luminous,
                cube.Angle, cube.Angle * .53f, Age * .006f);

        BossVisuals.Cuboid(spriteBatch, core, Size * .5f, Size * 1.02f, indigo, luminous,
            MathF.Sin(Age * .005f) * .65f);
        var inspirationSlit = new Rectangle((int)(core.X - Size * .055f), (int)(core.Y - Size * .35f),
            Math.Max(4, (int)(Size * .11f)), Math.Max(8, (int)(Size * .7f)));
        Primitives2D.FillRect(spriteBatch, inspirationSlit, UiTheme.Ink);
        var light = inspirationSlit;
        light.Inflate(-3, -4);
        Primitives2D.FillRect(spriteBatch, light, luminous);
        for (int node = 0; node < 3; node++)
        {
            float y = core.Y - Size * .48f + node * Size * .48f;
            float nodePulse = Size * (.04f + .008f * MathF.Sin(Age * .05f + node));
            Primitives2D.FillCircle(spriteBatch, new Vector2(core.X, y), Math.Max(3, (int)nodePulse + 4), UiTheme.Ink);
            Primitives2D.FillCircle(spriteBatch, new Vector2(core.X, y), Math.Max(2, (int)nodePulse), UiTheme.Cream);
        }

        foreach (var cube in floating.Where(cube => cube.Depth >= 0).OrderBy(cube => cube.Depth))
            BossVisuals.RotatingCube3D(spriteBatch, cube.Center, cube.Extent, indigo, violet, luminous,
                cube.Angle, cube.Angle * .53f, Age * .006f);

        DrawBossHealth(spriteBatch, new Rectangle((int)(core.X - Size * .46f), (int)(core.Y - Size * .75f),
            (int)(Size * .92f), 6));
    }

    public override void Draw(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        base.Draw(spriteBatch, camera, playerWorldPosition, screenShake);
        if (!Dying)
            foreach (var portal in ProjectilePortals)
                portal.Draw(spriteBatch, camera, playerWorldPosition, screenShake);
        if (SurvivalActive || FinaleActive)
        {
            var viewport = spriteBatch.GraphicsDevice.Viewport;
            int width = Math.Min((int)(viewport.Width * .42f), 520);
            var banner = new Rectangle(viewport.Width / 2 - width / 2, (int)(viewport.Height * .21f), width, 54);
            UiTheme.DrawPanel(spriteBatch, banner, UiTheme.PanelRaised, PhaseAccent, shadow: 5);
            double remaining = FinaleActive ? FinaleRemaining : SurvivalRemaining;
            UiTheme.DrawText(spriteBatch, $"SURVIVE  {remaining:00.0}", 16, UiTheme.Cream, new Vector2(banner.Center.X, banner.Y + 16), "center");
            UiTheme.DrawText(spriteBatch, FinaleActive ? "APOTHEOSIS // VITALITY SEALED" : "INTERMISSION // VITALITY SEALED",
                9, PhaseAccent, new Vector2(banner.Center.X, banner.Bottom - 11), "center");
        }
    }
}
