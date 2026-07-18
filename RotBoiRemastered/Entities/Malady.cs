using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// "THE DREAM MADE ILL" -- the final boss of the Phantasia content path.
/// Ported from bossTypes.py's Malady. Adds a projectile-portal formation
/// system (reusing <see cref="ProjectilePortal"/>), a delay-queued "flowing
/// chain" shot sequence, survival phases that suppress damage while a
/// timer runs out, a post-lethal "collapse" death choreography instead of
/// dying outright, and a fully custom procedural puppet-body render
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
    private static readonly Dictionary<int, double> SurvivalPhases = new() { [4] = 13.0, [7] = 15.0 };
    private static readonly int[] PortalCounts = { 3, 4, 3, 4, 5, 3, 6, 4, 5, 6 };
    private static readonly string[] PortalPaths =
        { "orbit", "figure8", "wave", "square", "tornado", "orbit", "square", "figure8", "wave", "tornado" };

    public static readonly PathChaseBossConfig MaladyConfig = BaseConfig with
    {
        BossName = "MALADY", Subtitle = "THE DREAM MADE ILL", FinalBoss = true,
        OwnerPrefix = "malady_phantasia",
        PhaseLabels = new[]
        {
            "THRONE", "GRAVEN HALL", "THE NAME", "SABBATH", "THE HOUSE",
            "THE UNSTRUCK", "THE VOW", "MINE AND YOURS", "THE WITNESS", "ENOUGH",
        },
        FinalBodyColor = new Color(99, 48, 126), FinalAccentColor = new Color(225, 95, 178),
        FinalBodyScale = 2.35, FinalCooldownSeconds = 1.25,
        MovementSpeed = .25, ArenaScale = 13.5,
        MovementModes = new[] { "static", "path", "chase", "static", "path", "chase", "path", "static", "chase", "path" },
    };

    public static readonly PhantasiaSigilConfig MaladySigilConfig = new(
        PhaseFlavors: new[]
        {
            "Kneel to the source.", "An image need not be real to wound.",
            "Do not spend what you cannot name.", "On the seventh beat, be still.",
            "Every child carries the first design.", "Power is proven by what it spares.",
            "A chosen path remembers betrayal.", "Possession is merely a change of color.",
            "Only one witness keeps its line.", "Was what you carried ever not enough?",
        },
        PhaseColors: new[]
        {
            new Color(233, 192, 78), new Color(193, 84, 215), new Color(111, 174, 228), new Color(235, 228, 185),
            new Color(107, 191, 145), new Color(218, 102, 118), new Color(225, 128, 190), new Color(98, 189, 206),
            new Color(244, 244, 232), new Color(220, 71, 133),
        },
        PhaseSigils: Enumerable.Range(0, 10).ToArray(),
        ActMetadata: new Dictionary<int, string> { [4] = "ACT II // THE COVENANT", [7] = "ACT III // THE TESTIMONY" });

    private readonly record struct ChainEvent(double Delay, Vector2 Origin, float Direction, float Speed, float Damage);

    public List<ProjectilePortal> ProjectilePortals { get; } = new();
    private int _portalFormationPhase;
    private List<ChainEvent> _sequenceQueue = new();
    private double _poolCooldown = 1.2;
    private readonly float[] _puppetMotion = { 0f, 0f };
    private float _puppetMotionStrength;

    public bool SurvivalActive { get; private set; }
    public double SurvivalRemaining { get; private set; }
    public string AttackPose { get; private set; } = "idle";
    public float AttackAimAngle { get; private set; }
    public double AttackAnimationDuration { get; } = .72;
    public float AttackAnticipation { get; private set; }
    public bool Collapsing { get; private set; }
    public double CollapseDuration { get; } = 3.6;
    public double CollapseRemaining { get; private set; }

    public Malady(float worldX, float worldY, Battleground battleground, Random? rng = null)
        : base(worldX, worldY, battleground, MaladyConfig, MaladySigilConfig, rng)
    {
        ActTitle = "ACT I // THE DOCTRINE";
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

    public override HitResult TakeDamage(double amount, string partId = "body", DamageSource source = DamageSource.Direct)
    {
        if (SurvivalActive || Collapsing)
            return new HitResult(false, false, 0, true);
        var result = base.TakeDamage(amount, partId, source);
        if (result.Killed)
        {
            Hp = 1;
            Collapsing = true;
            CollapseRemaining = CollapseDuration;
            _sequenceQueue.Clear();
            ClearMaladyPortals();
            TransitionCleanupRequested = true;
            PhaseAnnouncementTimer = 0.0;
            return new HitResult(result.Applied, false, result.Amount, result.Blocked);
        }
        return result;
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

    private void UpdatePuppetMotion(float previousX, float previousY, Camera? camera)
    {
        if (camera is null)
            return;
        float deltaX = WorldX - previousX, deltaY = WorldY - previousY;
        var screenDelta = camera.WorldVectorToScreen(new Vector2(deltaX, deltaY));
        float magnitude = MathF.Sqrt(screenDelta.X * screenDelta.X + screenDelta.Y * screenDelta.Y);
        if (magnitude > .005f)
        {
            float targetX = screenDelta.X / magnitude, targetY = screenDelta.Y / magnitude;
            _puppetMotion[0] += (targetX - _puppetMotion[0]) * .22f;
            _puppetMotion[1] += (targetY - _puppetMotion[1]) * .22f;
            _puppetMotionStrength += (1.0f - _puppetMotionStrength) * .18f;
        }
        else
        {
            _puppetMotion[0] *= .86f;
            _puppetMotion[1] *= .86f;
            _puppetMotionStrength *= .84f;
        }
    }

    public override void Update(EnemyUpdateContext context)
    {
        double dt = Seconds();
        float previousX = WorldX, previousY = WorldY;
        if (Collapsing)
        {
            AdvanceAge();
            CollapseRemaining = Math.Max(0.0, CollapseRemaining - dt);
            PhaseAnnouncementTimer = Math.Max(0.0, PhaseAnnouncementTimer - dt);
            AttackAnticipation = 0f;
            if (CollapseRemaining <= 0)
                Hp = 0;
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
                    int phaseCount = Config.PhaseLabels.Count;
                    Hp = (int)(MaxHp * (double)(phaseCount - Phase) / phaseCount);
                    SurvivalActive = false;
                    UpdatePhase();
                }
            }
        }

        base.Update(context);
        UpdatePuppetMotion(previousX, previousY, context.Camera);
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
            case 1: // Authority: one of four thrones speaks true.
            {
                int throne = PatternRotation % 4;
                for (int index = 0; index < 4; index++)
                    FanFrom(sink, PortalOrigin(index), target, 4, .62f, .92f, 330, "authority", illusion: index != throne);
                break;
            }
            case 2: // Image: four graven copies, one real.
            {
                for (int index = 0; index < 4; index++)
                {
                    float angle = index * MathF.PI / 2f + MathF.PI / 4f;
                    var origin = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * Simulation.TileSize * 3f;
                    RadialFrom(sink, origin, 6, .7f, 320, "image", illusion: index != PatternRotation % 4);
                }
                break;
            }
            case 3: // The Name: a truth-or-lie banner backed by a real volley.
            {
                RuleTruth = PatternRotation % 3 != 1;
                RuleText = RuleTruth ? "PRESERVE THE NAME" : "BREAK THE NAME";
                LaserFrom(sink, PortalOrigin(PatternRotation), aimed, 350, "name", illusion: !RuleTruth);
                FanFrom(sink, center, target, 7, 1.4f, .72f, 325, "reverence");
                break;
            }
            case 4: // Sabbath: a slow sweeping chain plus a portal phrase (REST is enforced by the base class).
            {
                var origin = PortalOrigin(PatternRotation);
                float sweep = MathF.Atan2(target.Y - origin.Y, target.X - origin.X) - .95f;
                QueueChain(origin, sweep, 1.9f, count: 18, interval: .05, speed: .68f);
                UpdateSequences(sink, 0.0);
                FirePortalPhrase(sink, target, wide: true);
                break;
            }
            case 5: // The House: shots that fracture across two generations.
            {
                for (int index = 0; index < 5; index++)
                {
                    var shot = ShotFrom(sink, center, aimed + (index - 2) * .3f, .8f, 330, "lineage");
                    shot.SplitCount = 3;
                    shot.SplitAt = Simulation.TileSize * (2.8f + index * .55f);
                    shot.SplitGeneration = 2;
                }
                break;
            }
            case 6: // The Unstruck: a real fan behind a harmless, damageless procession.
            {
                FanFrom(sink, center, target, 3, .3f, 1.0f, 350, "mercy");
                for (int index = 0; index < 12; index++)
                    ShotFrom(sink, center, index * 2f * MathF.PI / 12f, .42f, 0, "procession", illusion: true);
                break;
            }
            case 7: // The Vow: half the portals lie inward, one chosen chain answers true.
            {
                int chosen = PatternRotation % Math.Max(1, ProjectilePortals.Count);
                for (int index = 0; index < ProjectilePortals.Count; index += 2)
                {
                    var origin = PortalOrigin(index);
                    float inward = MathF.Atan2(center.Y - origin.Y, center.X - origin.X);
                    LaserFrom(sink, origin, inward, 355, "vow", illusion: index == chosen);
                }
                var chainOrigin = PortalOrigin(chosen);
                float tangent = MathF.Atan2(center.Y - chainOrigin.Y, center.X - chainOrigin.X) + MathF.PI / 2f;
                QueueChain(chainOrigin, tangent - .7f, 1.4f, count: 20, interval: .045, speed: .82f, damage: 345);
                UpdateSequences(sink, 0.0);
                FirePortalPhrase(sink, target);
                break;
            }
            case 8: // Mine And Yours: a real ring of "stolen" shots beside a harmless "unowned" one, sized off the player's own build.
            {
                var build = context.PlayerBuildSnapshot;
                double projectileCountStat = build?.Stats.GetValueOrDefault("projectile_count") ?? 1.0;
                int count = Math.Clamp((int)Math.Round(projectileCountStat) + 2, 4, 10);
                RadialFrom(sink, center, count, .82f, 345, "stolen");
                RadialFrom(sink, center, count, .45f, 0, "unowned", illusion: true);
                break;
            }
            case 9: // The Witness: one of five lasers is real.
            {
                int trueIndex = PatternRotation % 5;
                TruthIndex = trueIndex;
                for (int index = 0; index < 5; index++)
                    LaserFrom(sink, center, aimed + (index - 2) * .34f, 370, "witness", illusion: index != trueIndex);
                break;
            }
            default: // Enough: intensity scales with how many offerings were accepted.
            {
                int intensity = AcceptedOfferings.Count;
                RadialFrom(sink, center, 10 + intensity * 2, .62f + intensity * .08f, 350, "enough");
                FanFrom(sink, center, target, 5 + intensity, 1.25f, 1.0f, 365, "covetous", path: "sine");
                break;
            }
        }
        if (!SurvivalPhases.ContainsKey(Phase) && PatternRotation % 2 == 0)
            FirePortalPhrase(sink, target, wide: Phase >= 8);
        PatternRotation++;
        MarkAttack(.62f);
    }

    protected override bool HasCustomDreamBody => true;

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
        float driftX = -_puppetMotion[0] * size * .2f * _puppetMotionStrength;
        float driftY = -_puppetMotion[1] * size * .13f * _puppetMotionStrength;
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
        bool finale = Phase == 7;
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
    protected override void DrawDreamBody(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        float attackProgress = Math.Min(1.0f, VisualAttackTimer / Math.Max(1f, Simulation.FrameRate * (float)AttackAnimationDuration));
        float attackAmount = Math.Max(MathF.Sin(attackProgress * MathF.PI), AttackAnticipation * .62f);
        float stillness = 1.0f - Math.Min(1.0f, _puppetMotionStrength);
        float bob = MathF.Sin(Age * .025f) * Size * .035f * (.45f + stillness * .55f);
        float leanX = _puppetMotion[0] * Size * .07f * _puppetMotionStrength;
        float leanY = _puppetMotion[1] * Size * .035f * _puppetMotionStrength;
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

        float baseShift = -_puppetMotion[0] * Size * .08f * _puppetMotionStrength - reassembly * Size * .12f;
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

    public override void Draw(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        base.Draw(spriteBatch, camera, playerWorldPosition, screenShake);
        foreach (var portal in ProjectilePortals)
            portal.Draw(spriteBatch, camera, playerWorldPosition, screenShake);
        if (SurvivalActive)
        {
            var viewport = spriteBatch.GraphicsDevice.Viewport;
            int width = Math.Min((int)(viewport.Width * .42f), 520);
            var banner = new Rectangle(viewport.Width / 2 - width / 2, (int)(viewport.Height * .21f), width, 54);
            UiTheme.DrawPanel(spriteBatch, banner, UiTheme.PanelRaised, PhaseAccent, shadow: 5);
            UiTheme.DrawText(spriteBatch, $"SURVIVE  {SurvivalRemaining:00.0}", 16, UiTheme.Cream, new Vector2(banner.Center.X, banner.Y + 16), "center");
            UiTheme.DrawText(spriteBatch, "VITALITY SEALED", 9, PhaseAccent, new Vector2(banner.Center.X, banner.Bottom - 11), "center");
        }
    }
}
