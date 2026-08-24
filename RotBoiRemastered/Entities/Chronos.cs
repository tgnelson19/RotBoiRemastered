using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// Sight's usurper and king of attrition. Chronos is deliberately slower than Ishe: every attack first
/// draws its complete segmented route, then the whole laser-tentacle strikes.
/// The encounter has three opening lessons, an invulnerable half-health exam,
/// two heavy damage movements, and a thirty-five-second King's Attrition finale.
/// </summary>
public sealed class Chronos : Ishe
{
    public const int AmbientMoteCount = 15;
    public const int FinaleMoteCount = 24;
    public const int ActiveRouteSoftCap = 112;
    public const int TemporalInsightNeeded = 3;
    public const double TemporalFractureDuration = 5.5;
    public const double TemporalFractureDamageMultiplier = 1.18;
    public const int MinimumDamagePhaseDeclarations = 2;
    private const int HistoricalRouteSoftCap = 72;
    private static readonly IReadOnlyDictionary<int, (string Label, string Flavor, Color Accent)> PhaseMetadata =
        new Dictionary<int, (string, string, Color)>
        {
            [1] = ("FORK", "Two futures open their eyes at once.", new Color(102, 198, 230)),
            [2] = ("REJECTED HOUR", "The abandoned hour has not forgotten you.", new Color(238, 170, 75)),
            [3] = ("THIRD FUTURE", "Another road appears where none should fit.", new Color(117, 164, 232)),
            [4] = ("STILL SECOND", "The second hand holds its breath.", UiTheme.Cream),
            [5] = ("PARALLAX", "Old futures drift out of alignment.", new Color(91, 191, 218)),
            [6] = ("THORN OF TIME", "Valia's last horizon returns.", new Color(235, 125, 72)),
            [7] = ("KING'S ATTRITION", "The king has all the time you do not.", new Color(244, 186, 82)),
        };

    public static readonly PathChaseBossConfig ChronosConfig = IsheConfig with
    {
        BossName = "CHRONOS", Subtitle = "THE KING OF ATTRITION", FinalBoss = true,
        OwnerPrefix = "chronos_sight",
        PhaseLabels = PhaseMetadata.OrderBy(pair => pair.Key).Select(pair => pair.Value.Label).ToArray(),
        FinalBodyColor = new Color(101, 190, 228), FinalAccentColor = new Color(203, 239, 250),
        FinalBodyScale = 1.75, FinalCooldownSeconds = 2.0,
        FinalShotSpeed = .42, FinalShotDamage = 760, FinalShotScale = .22,
        MovementSpeed = .12, ArenaScale = 11.8,
        MovementPhases = new[]
        {
            BossMovementPhaseProfile.Fixed(BossPathShape.Triangle, 8f, .58f, .58f),
            BossMovementPhaseProfile.Stationary(),
            BossMovementPhaseProfile.Fixed(BossPathShape.Triangle, 8f, .62f, .62f, -1),
            BossMovementPhaseProfile.Stationary(),
            BossMovementPhaseProfile.Fixed(BossPathShape.Triangle, 8f, .64f, .64f),
            BossMovementPhaseProfile.Stationary(),
            BossMovementPhaseProfile.Stationary(),
        },
        FinalHealth = 310000, FinalContactDamage = 880, FinalRewardExperience = 860,
        FinaleDuration = 35.0,
    };

    public bool MidpointSurvivalActive { get; private set; }
    public bool MidpointSurvivalCleared { get; private set; }
    public double MidpointSurvivalDuration { get; } = 20.0;
    public double MidpointSurvivalRemaining { get; private set; }
    public int PatternRotation { get; private set; }
    private double _survivalCooldown;
    private readonly List<PendingDeclaration> _pendingDeclarations = new();
    private readonly List<PendingSafeRoute> _pendingSafeRoutes = new();
    private readonly List<HistoricalRoute> _historicalRoutes = new();
    private readonly List<EnemyProjectile> _routeScratch = new(64);
    private float? _rememberedAim;
    private int _phaseDeclarations;

    private readonly record struct PendingDeclaration(double Delay, float Direction, float Bend, float Damage,
        string Suffix, float Telegraph, int Segments, float SegmentTiles);
    private readonly record struct PendingSafeRoute(double Delay, Vector2 Origin, float Direction, float HalfWidth);
    private readonly record struct HistoricalRoute(Vector2 Start, Vector2 End, double Remaining, double Duration);

    public int TemporalInsight { get; private set; }
    public double TemporalFractureRemaining { get; private set; }
    public bool TemporalFractureActive => TemporalFractureRemaining > 0;
    public int HistoricalRouteCount => _historicalRoutes.Count;
    public int PhaseDeclarations => _phaseDeclarations;

    protected override bool UsesIsheEncounter => false;

    public Chronos(float worldX, float worldY, Battleground battleground, Random? rng = null)
        : base(worldX, worldY, battleground, ChronosConfig, rng)
    {
        ApplyPhase(1);
    }

    private void ApplyPhase(int phase)
    {
        Phase = Math.Clamp(phase, 1, PhaseMetadata.Count);
        (PhaseLabel, PhaseFlavor, PhaseAccent) = PhaseMetadata[Phase];
        PhaseElapsed = 0.0;
        _phaseDeclarations = 0;
        _pendingDeclarations.Clear();
        _pendingSafeRoutes.Clear();
        _historicalRoutes.Clear();
        VisualTransitionRemaining = 1.4;
        AttackCooldown = Math.Min(AttackCooldown ?? 0f, Simulation.FrameRate * .6f);
        TransitionCleanupRequested = true;
    }

    private void BeginMidpointSurvival()
    {
        if (MidpointSurvivalActive || MidpointSurvivalCleared)
            return;
        Hp = Math.Max(1, (int)Math.Round(MaxHp * .5));
        ApplyPhase(4);
        MidpointSurvivalActive = true;
        MidpointSurvivalRemaining = MidpointSurvivalDuration;
        _survivalCooldown = .35;
    }

    protected override void UpdatePhase()
    {
        if (DebugPhaseLocked || FinaleActive || MidpointSurvivalActive)
            return;
        double ratio = Math.Clamp((double)Hp / MaxHp, 0.0, 1.0);
        int desired;
        if (!MidpointSurvivalCleared)
        {
            if (ratio <= .5)
            {
                if (_phaseDeclarations < MinimumDamagePhaseDeclarations)
                    return;
                BeginMidpointSurvival();
                return;
            }
            desired = ratio > .84 ? 1 : ratio > .67 ? 2 : 3;
        }
        else
        {
            desired = ratio > .25 ? 5 : 6;
        }
        if (desired != Phase && _phaseDeclarations >= MinimumDamagePhaseDeclarations)
            ApplyPhase(desired);
    }

    public override void DebugSetPhase(int phase)
    {
        phase = Math.Clamp(phase, 1, 7);
        DebugPhaseLocked = true;
        MidpointSurvivalActive = false;
        if (phase >= 5)
            MidpointSurvivalCleared = true;
        ApplyPhase(phase);
        AttackCooldown = 0f;
        if (phase == 4)
        {
            MidpointSurvivalActive = true;
            MidpointSurvivalRemaining = MidpointSurvivalDuration;
            _survivalCooldown = 0;
        }
        else if (phase == 7 && !FinaleActive)
        {
            BeginFinaleSequence();
        }
    }

    public override HitResult TakeDamage(double amount, string partId = "body", DamageSource source = DamageSource.Direct)
    {
        if (MidpointSurvivalActive || FinaleActive || Dying)
            return new HitResult(false, false, 0, true);
        double adjustedAmount = amount * (TemporalFractureActive ? TemporalFractureDamageMultiplier : 1.0);

        if (!MidpointSurvivalCleared)
        {
            double floorRatio = Phase switch { 1 => .84, 2 => .67, _ => .50 };
            int floor = Math.Max(1, (int)Math.Round(MaxHp * floorRatio));
            double permitted = Math.Max(0, Hp - floor);
            if (permitted <= 0)
            {
                if (_phaseDeclarations >= MinimumDamagePhaseDeclarations)
                    UpdatePhase();
                return new HitResult(false, false, 0, true);
            }
            var result = base.TakeDamage(Math.Min(adjustedAmount, permitted), partId, source);
            if (Hp <= MaxHp * .5 && _phaseDeclarations >= MinimumDamagePhaseDeclarations)
                BeginMidpointSurvival();
            return new HitResult(result.Applied, false, result.Amount, result.Blocked);
        }

        if (Phase == 5)
        {
            int floor = Math.Max(1, (int)Math.Round(MaxHp * .25));
            double permitted = Math.Max(0, Hp - floor);
            if (permitted <= 0)
            {
                if (_phaseDeclarations >= MinimumDamagePhaseDeclarations)
                    UpdatePhase();
                return new HitResult(false, false, 0, true);
            }
            var gated = base.TakeDamage(Math.Min(adjustedAmount, permitted), partId, source);
            return new HitResult(gated.Applied, false, gated.Amount, gated.Blocked);
        }

        if (Phase == 6 && _phaseDeclarations < MinimumDamagePhaseDeclarations)
        {
            double permitted = Math.Max(0, Hp - 1);
            if (permitted <= 0)
                return new HitResult(false, false, 0, true);
            var gated = base.TakeDamage(Math.Min(adjustedAmount, permitted), partId, source);
            return new HitResult(gated.Applied, false, gated.Amount, gated.Blocked);
        }
        var finalResult = base.TakeDamage(adjustedAmount, partId, source);
        if (FinaleActive)
            ApplyPhase(7);
        return finalResult;
    }

    private void Tentacle(List<EnemyProjectile> sink, float baseDirection, float bend, float damage,
        string suffix, float telegraph = 1.45f, int segments = 6, float segmentTiles = 2.15f)
    {
        Vector2 origin = Center();
        float segmentLength = Simulation.TileSize * segmentTiles;
        for (int segment = 0; segment < segments; segment++)
        {
            float fraction = segment / (float)Math.Max(1, segments - 1);
            float direction = baseDirection
                + MathF.Sin(fraction * MathF.PI * 1.35f + PatternRotation * .53f) * bend
                + (fraction - .5f) * bend * .35f;
            float width = Size * (.075f + segment * .006f);
            // Chronos's clockwork lasers strike and vanish -- the long,
            // fully-shown telegraph is the fairness; once fired, the beam
            // itself lingers only briefly rather than sweeping or drifting.
            var laser = new EnemyProjectile(origin.X, origin.Y, direction, 0f, damage, width,
                travelRange: segmentLength, color: PhaseAccent, shape: "laser", path: "laser",
                lifetime: telegraph + .3f, owner: $"chronos_{suffix}_segment_{segment}", ignoreWalls: true)
            {
                TelegraphDuration = telegraph,
            };
            if (segment > 0)
                laser.RequireOriginTelegraph(telegraph);
            sink.Add(laser);
            origin += new Vector2(MathF.Cos(direction), MathF.Sin(direction)) * segmentLength;
        }
    }

    private static int ActiveChronosRoutes(List<EnemyProjectile> sink) =>
        sink.Count(projectile => !projectile.RemFlag &&
            projectile.Owner?.StartsWith("chronos_", StringComparison.Ordinal) == true);

    private static bool CommitDeclaredRoutes(List<EnemyProjectile> sink, List<EnemyProjectile> declaration)
    {
        if (declaration.Count == 0)
            return false;
        if (ActiveChronosRoutes(sink) + declaration.Count > ActiveRouteSoftCap)
            return false;
        sink.AddRange(declaration);
        return true;
    }

    private void ScheduleTentacle(double delay, float direction, float bend, float damage,
        string suffix, float telegraph, int segments, float segmentTiles = 2.15f) =>
        _pendingDeclarations.Add(new PendingDeclaration(delay, direction, bend, damage,
            suffix, telegraph, segments, segmentTiles));

    private void UpdatePendingDeclarations(double dt, List<EnemyProjectile> sink)
    {
        if (_pendingDeclarations.Count == 0)
            return;
        int writeIndex = 0;
        for (int readIndex = 0; readIndex < _pendingDeclarations.Count; readIndex++)
        {
            var declaration = _pendingDeclarations[readIndex];
            double delay = declaration.Delay - dt;
            if (delay > 0)
            {
                _pendingDeclarations[writeIndex++] =
                    declaration with { Delay = delay };
                continue;
            }

            _routeScratch.Clear();
            Tentacle(_routeScratch, declaration.Direction, declaration.Bend, declaration.Damage,
                declaration.Suffix, declaration.Telegraph, declaration.Segments, declaration.SegmentTiles);
            CommitDeclaredRoutes(sink, _routeScratch);
        }
        if (writeIndex < _pendingDeclarations.Count)
            _pendingDeclarations.RemoveRange(
                writeIndex,
                _pendingDeclarations.Count - writeIndex);
    }

    private void ScheduleSafeRoute(double delay, Vector2 origin, float direction, float halfWidth) =>
        _pendingSafeRoutes.Add(new PendingSafeRoute(delay, origin, direction, halfWidth));

    private void UpdateSafeRoutes(double dt, float playerX, float playerY)
    {
        if (_pendingSafeRoutes.Count == 0)
            return;
        int writeIndex = 0;
        for (int readIndex = 0; readIndex < _pendingSafeRoutes.Count; readIndex++)
        {
            var route = _pendingSafeRoutes[readIndex];
            double delay = route.Delay - dt;
            if (delay > 0)
            {
                _pendingSafeRoutes[writeIndex++] =
                    route with { Delay = delay };
                continue;
            }

            float playerDirection = MathF.Atan2(playerY - route.Origin.Y, playerX - route.Origin.X);
            if (MathF.Abs(NormalizeAngle(playerDirection - route.Direction)) <= route.HalfWidth)
            {
                TemporalInsight++;
                if (TemporalInsight >= TemporalInsightNeeded)
                {
                    TemporalInsight = 0;
                    TemporalFractureRemaining = TemporalFractureDuration;
                }
            }
        }
        if (writeIndex < _pendingSafeRoutes.Count)
            _pendingSafeRoutes.RemoveRange(
                writeIndex,
                _pendingSafeRoutes.Count - writeIndex);
    }

    private static float NormalizeAngle(float angle)
    {
        while (angle > MathF.PI)
            angle -= MathF.Tau;
        while (angle < -MathF.PI)
            angle += MathF.Tau;
        return angle;
    }

    private void CaptureFinaleHistory(IEnumerable<EnemyProjectile> declaration)
    {
        if (!FinaleActive)
            return;
        const double duration = 4.2;
        foreach (var route in declaration.Where(projectile => projectile.Path == "laser"))
        {
            var start = new Vector2(route.WorldX, route.WorldY);
            var end = start + new Vector2(MathF.Cos(route.Direction), MathF.Sin(route.Direction)) * route.RemainingRange;
            _historicalRoutes.Add(new HistoricalRoute(start, end, duration, duration));
        }
        if (_historicalRoutes.Count > HistoricalRouteSoftCap)
            _historicalRoutes.RemoveRange(0, _historicalRoutes.Count - HistoricalRouteSoftCap);
    }

    private void UpdateHistoricalRoutes(double dt)
    {
        for (int index = _historicalRoutes.Count - 1; index >= 0; index--)
        {
            var route = _historicalRoutes[index];
            double remaining = route.Remaining - dt;
            if (remaining <= 0)
                _historicalRoutes.RemoveAt(index);
            else
                _historicalRoutes[index] = route with { Remaining = remaining };
        }
    }

    private void DirectivePair(float playerX, float playerY, List<EnemyProjectile> sink, bool crossed)
    {
        var center = Center();
        float aimed = MathF.Atan2(playerY - center.Y, playerX - center.X);
        float opening = crossed ? .52f : .34f;
        Tentacle(sink, aimed - opening, crossed ? .42f : .22f, crossed ? 820 : 730,
            crossed ? "crosscut_left" : "directive_left", telegraph: crossed ? 1.65f : 1.5f);
        Tentacle(sink, aimed + opening, crossed ? -.42f : -.22f, crossed ? 820 : 730,
            crossed ? "crosscut_right" : "directive_right", telegraph: crossed ? 1.65f : 1.5f);
    }

    private void RadialTentacles(float playerX, float playerY, List<EnemyProjectile> sink, int count, int gaps,
        float bend, float damage, string suffix, float telegraph = 1.55f)
    {
        var center = Center();
        float playerAngle = MathF.Atan2(playerY - center.Y, playerX - center.X);
        float step = MathF.Tau / count;
        float rotation = PatternRotation * .17f;
        float unrotatedPlayerAngle = playerAngle - rotation;
        int gapIndex = (int)MathF.Round(
            ((unrotatedPlayerAngle % MathF.Tau + MathF.Tau) % MathF.Tau) / step) % count;
        for (int index = 0; index < count; index++)
        {
            int distance = Math.Min((index - gapIndex + count) % count, (gapIndex - index + count) % count);
            if (distance < gaps)
                continue;
            float direction = index * step + rotation;
            Tentacle(sink, direction, index % 2 == 0 ? bend : -bend, damage, $"{suffix}_{index}", telegraph, segments: 5);
        }
    }

    private void FireSurvivalPattern(float playerX, float playerY, List<EnemyProjectile> sink)
    {
        var declaration = new List<EnemyProjectile>(40);
        RadialTentacles(playerX, playerY, declaration, 10, 2, .34f, 760, "still_second", 1.7f);
        var center = Center();
        float aimed = MathF.Atan2(playerY - center.Y, playerX - center.X);
        ClockShards(declaration, aimed + MathF.PI, 4, 2.2f, "frozen_clock_shard");
        if (CommitDeclaredRoutes(sink, declaration))
        {
            ScheduleSafeRoute(1.76, center, aimed, .46f);
            ScheduleTentacle(.48, aimed,
                PatternRotation % 2 == 0 ? .12f : -.12f, 690,
                "still_second_hand", 1.52f, 6, 2.3f);
        }
        PatternRotation++;
    }

    private void ThornOfTime(float playerX, float playerY, List<EnemyProjectile> sink,
        string suffix = "thorn_of_time", bool withEcho = false)
    {
        var center = Center();
        float aimed = MathF.Atan2(playerY - center.Y, playerX - center.X);
        // The fabled strike is the encounter's most lethal attack, but it is
        // also its fairest: one complete eight-segment route is visible for
        // over two seconds and never retargets after being declared.
        Tentacle(sink, aimed, PatternRotation % 2 == 0 ? .075f : -.075f, 1260, suffix,
            telegraph: 2.35f, segments: 8, segmentTiles: 2.45f);
        if (withEcho)
        {
            float side = PatternRotation % 2 == 0 ? 1f : -1f;
            Tentacle(sink, aimed + side * .42f, -side * .18f, 620,
                "temporal_echo", telegraph: 2.35f, segments: 7, segmentTiles: 2.25f);
        }
    }

    private void ClockShards(List<EnemyProjectile> sink, float aimed, int count,
        float spread, string suffix)
    {
        var center = Center();
        for (int index = 0; index < count; index++)
        {
            float offset = count == 1 ? 0f : -spread / 2f + spread * index / (count - 1);
            float size = Size * (.12f + .012f * (index % 3));
            var shard = new EnemyProjectile(
                center.X - size / 2f, center.Y - size / 2f,
                aimed + offset, .58f + .055f * (index % 3),
                Phase >= 6 ? 680 : 590, size,
                travelRange: ArenaRadius * 1.9f, color: PhaseAccent,
                shape: index % 2 == 0 ? "diamond" : "square", path: "sine",
                amplitude: Simulation.TileSize * (.72f + .18f * (index % 3)),
                frequency: .042f + .005f * (index % 2), lifetime: 10f,
                owner: $"chronos_{suffix}", ignoreWalls: true);
            if (index == count / 2)
            {
                shard.SplitCount = 2;
                shard.SplitAt = Simulation.TileSize * 5.5f;
            }
            sink.Add(shard);
        }
    }

    private void ClockHandSweep(List<EnemyProjectile> sink, Vector2 origin, float startDirection,
        float angularSpeed, float damage, string suffix, float telegraph = 1.6f, float sweepSeconds = 2.2f)
    {
        // A clock hand ticks, it doesn't sweep: Chronos's beams otherwise
        // hold almost perfectly still once fired, so this creeps at a bare
        // fraction of its old rate -- barely perceptible drift rather than a
        // rotating danger zone -- and burns out quickly to match every other
        // Chronos laser's short, static strike-and-vanish rhythm.
        var beam = new EnemyProjectile(origin.X, origin.Y, startDirection, 0f, damage,
            Size * .1f, travelRange: Simulation.TileSize * 9f, color: PhaseAccent,
            shape: "laser", path: "laser", lifetime: telegraph + Math.Min(sweepSeconds, .8f),
            angularSpeed: angularSpeed * .16f, owner: $"chronos_sweep_{suffix}", ignoreWalls: true)
        {
            TelegraphDuration = telegraph,
        };
        sink.Add(beam);
    }

    /// <summary>
    /// A literal pair of clock hands: two shots orbit Chronos at opposite
    /// points on the same circle, connected by a live damaging line (the
    /// new "tether" primitive) that sweeps the room between them rather
    /// than as two separate points to dodge independently. Fully telegraphed
    /// at the spawn point before either hand starts moving, matching every
    /// other Chronos attack's "show the whole thing first" fairness.
    /// </summary>
    private void ClockHandTether(List<EnemyProjectile> sink, float startAngle, float angularSpeed, float damage,
        string suffix, float radiusTiles = 3.4f, float lifetime = 6f, float telegraph = 1.5f)
    {
        var center = Center();
        float radius = Simulation.TileSize * radiusTiles;
        float handSize = Size * .12f;
        EnemyProjectile Hand(float angle, string handSuffix) => new(
            center.X + MathF.Cos(angle) * radius - handSize / 2f,
            center.Y + MathF.Sin(angle) * radius - handSize / 2f,
            0f, 0f, damage, handSize,
            travelRange: float.PositiveInfinity, color: PhaseAccent, shape: "diamond", path: "orbit",
            orbitCenter: center, orbitRadius: radius, orbitAngle: angle, angularSpeed: angularSpeed,
            lifetime: lifetime, owner: $"chronos_{suffix}_hand", ignoreWalls: true)
        {
            OriginTelegraphDuration = telegraph,
        };
        var handA = Hand(startAngle, suffix);
        var handB = Hand(startAngle + MathF.PI, suffix);
        var tether = new EnemyProjectile(center.X, center.Y, 0f, 0f, damage * .75f, Size * .09f,
            color: PhaseAccent, path: "tether", lifetime: lifetime, owner: $"chronos_{suffix}_tether", ignoreWalls: true)
        {
            OriginTelegraphDuration = telegraph,
            TetherStart = handA,
            TetherEnd = handB,
        };
        sink.Add(handA);
        sink.Add(handB);
        sink.Add(tether);
    }

    protected override void FirePattern(float playerX, float playerY, List<EnemyProjectile> sink)
    {
        var declaration = new List<EnemyProjectile>(52);
        int pendingBefore = _pendingDeclarations.Count;
        int safeRoutesBefore = _pendingSafeRoutes.Count;
        var center = Center();
        float aimed = MathF.Atan2(playerY - center.Y, playerX - center.X);
        bool scheduleSecondHand = false;
        switch (Phase)
        {
            case 1:
                DirectivePair(playerX, playerY, declaration, crossed: false);
                ScheduleSafeRoute(1.56, center, aimed, .24f);
                scheduleSecondHand = true;
                break;
            case 2:
                DirectivePair(playerX, playerY, declaration, crossed: true);
                ScheduleSafeRoute(1.71, center, aimed, .20f);
                scheduleSecondHand = true;
                break;
            case 3:
            {
                Tentacle(declaration, aimed - .72f, .68f, 850, "oracle_outer_left", 1.7f, 7);
                Tentacle(declaration, aimed + .72f, -.68f, 850, "oracle_outer_right", 1.7f, 7);
                ScheduleTentacle(.35, aimed + MathF.PI, PatternRotation % 2 == 0 ? .55f : -.55f,
                    810, "oracle_rear", 1.7f, 6);
                ScheduleSafeRoute(1.76, center, aimed, .22f);
                scheduleSecondHand = true;
                break;
            }
            case 5:
                DirectivePair(playerX, playerY, declaration, crossed: PatternRotation % 2 == 0);
                ScheduleTentacle(.30, PatternRotation * .71f, .76f, 870,
                    "parallax_flail", 1.8f, 7);
                ClockHandSweep(declaration, center, aimed - 1.75f,
                    (PatternRotation % 2 == 0 ? 1f : -1f) * .85f, 640, "parallax", 1.6f, 2.6f);
                scheduleSecondHand = true;
                break;
            case 6:
                if (PatternRotation % 3 == 0)
                {
                    RadialTentacles(playerX, playerY, declaration, 12, 2, .48f, 910, "thorn_crown", 1.8f);
                    ScheduleSafeRoute(1.86, center, aimed, .38f);
                    scheduleSecondHand = true;
                }
                else if (PatternRotation % 3 == 1)
                    ThornOfTime(playerX, playerY, declaration, withEcho: true);
                else
                    ClockHandTether(declaration, aimed, (PatternRotation % 2 == 0 ? 1f : -1f) * .32f,
                        700, "thorn_hands");
                break;
            default:
            {
                int movement = PatternRotation % 5;
                if (movement == 0)
                {
                    DirectivePair(playerX, playerY, declaration, crossed: true);
                    scheduleSecondHand = true;
                    if (_rememberedAim is float remembered)
                    {
                        float side = PatternRotation % 8 == 0 ? 1f : -1f;
                        Tentacle(declaration, remembered + side * .18f, -side * .26f, 560,
                            "attrition_memory_echo", 1.95f, 6);
                    }
                }
                else if (movement == 1)
                {
                    RadialTentacles(playerX, playerY, declaration, 12, 2, .55f, 930, "attrition_crown", 1.6f);
                    ScheduleSafeRoute(1.66, center, aimed, .36f);
                    scheduleSecondHand = true;
                }
                else if (movement == 2)
                {
                    for (int index = -1; index <= 1; index++)
                        Tentacle(declaration, aimed + MathF.PI + index * .7f, (index == 0 ? 1 : index) * .62f,
                            920, $"attrition_lash_{index + 1}", 1.55f, 7);
                    scheduleSecondHand = true;
                }
                else if (movement == 4)
                {
                    ClockHandSweep(declaration, center, aimed - .55f,
                        (PatternRotation % 2 == 0 ? 1f : -1f) * 1.35f, 980, "attrition", 1.5f, 1.0f);
                    scheduleSecondHand = true;
                }
                else
                    ThornOfTime(playerX, playerY, declaration, "attrition_thorn", withEcho: true);
                break;
            }
        }
        if (scheduleSecondHand)
        {
            ScheduleTentacle(.48, aimed,
                PatternRotation % 2 == 0 ? .12f : -.12f,
                Phase >= 6 ? 760 : 660, "second_hand",
                1.52f, 6, 2.3f);
        }
        if (Phase >= 5)
            ClockShards(declaration, aimed, Phase >= 7 ? 6 : 4,
                Phase >= 7 ? 1.9f : 1.35f,
                Phase >= 7 ? "attrition_clock_shard" : "parallax_clock_shard");
        bool committed = CommitDeclaredRoutes(sink, declaration);
        if (!committed && _pendingDeclarations.Count > pendingBefore)
            _pendingDeclarations.RemoveRange(pendingBefore, _pendingDeclarations.Count - pendingBefore);
        if (!committed && _pendingSafeRoutes.Count > safeRoutesBefore)
            _pendingSafeRoutes.RemoveRange(safeRoutesBefore, _pendingSafeRoutes.Count - safeRoutesBefore);
        if (committed)
        {
            _rememberedAim = aimed;
            CaptureFinaleHistory(declaration);
            _phaseDeclarations++;
        }
        PatternRotation++;
        MarkAttack(.82f);
    }

    private void ApplyAuthoredCadence()
    {
        // Chronos's clockwork strikes come around faster than most bosses'
        // attacks -- each one alone is brief and nearly motionless, so the
        // encounter's pressure comes from ticking frequently rather than
        // from any single beam lingering or sweeping.
        double seconds = Phase switch
        {
            1 => 1.7,
            2 => 1.55,
            3 => 1.4,
            5 => 1.16,
            6 => .98,
            _ => .78,
        };
        AttackCooldown = Simulation.FrameRate * (float)(seconds * (.94 + Rng.NextDouble() * .12));
    }

    public override void Update(EnemyUpdateContext context)
    {
        double dt = Seconds();
        TemporalFractureRemaining = Math.Max(0.0, TemporalFractureRemaining - dt);
        UpdateHistoricalRoutes(dt);
        UpdateSafeRoutes(dt, context.PlayerWorldX, context.PlayerWorldY);
        if (!MidpointSurvivalActive)
        {
            UpdatePendingDeclarations(dt, context.ProjectileSink);
            int patternBefore = PatternRotation;
            base.Update(context);
            if (PatternRotation != patternBefore)
                ApplyAuthoredCadence();
            return;
        }

        EntranceRemaining = Math.Max(0.0, EntranceRemaining - dt);
        VisualTransitionRemaining = Math.Max(0.0, VisualTransitionRemaining - dt);
        PhaseElapsed += dt;
        AdvanceAge();
        MidpointSurvivalRemaining = Math.Max(0.0, MidpointSurvivalRemaining - dt);
        _survivalCooldown -= dt;
        if (EntranceRemaining <= 0 && _survivalCooldown <= 0)
        {
            FireSurvivalPattern(context.PlayerWorldX, context.PlayerWorldY, context.ProjectileSink);
            double elapsed = MidpointSurvivalDuration - MidpointSurvivalRemaining;
            _survivalCooldown = elapsed < MidpointSurvivalDuration * .5 ? 2.08 : 1.82;
        }
        if (MidpointSurvivalRemaining <= 0 && !DebugPhaseLocked)
        {
            MidpointSurvivalActive = false;
            MidpointSurvivalCleared = true;
            Hp = Math.Max(1, (int)Math.Round(MaxHp * .5));
            ApplyPhase(5);
        }
        FinishMovementTracking();
    }

    private void DrawSafeRouteTelegraphs(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        // The safe-route mechanic used to be legible only as "the gap in whatever
        // attack fired" — this draws the actual countdown lane on the arena floor
        // so standing in it reads as a deliberate choice, not a guess.
        foreach (var route in _pendingSafeRoutes)
        {
            float glow = 1f - Math.Clamp((float)(route.Delay / 2.0), 0f, 1f);
            if (glow <= 0f)
                continue;
            Vector2 left = route.Origin + new Vector2(MathF.Cos(route.Direction - route.HalfWidth),
                MathF.Sin(route.Direction - route.HalfWidth)) * ArenaRadius;
            Vector2 right = route.Origin + new Vector2(MathF.Cos(route.Direction + route.HalfWidth),
                MathF.Sin(route.Direction + route.HalfWidth)) * ArenaRadius;
            Vector2 originScreen = camera.WorldToScreen(route.Origin, playerWorldPosition, screenShake);
            Vector2 leftScreen = camera.WorldToScreen(left, playerWorldPosition, screenShake);
            Vector2 rightScreen = camera.WorldToScreen(right, playerWorldPosition, screenShake);
            Color laneColor = UiTheme.Cream * (glow * .3f);
            Primitives2D.Line(spriteBatch, originScreen, leftScreen, laneColor, 2);
            Primitives2D.Line(spriteBatch, originScreen, rightScreen, laneColor, 2);
        }
    }

    protected override void DrawBossBody(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        DrawSafeRouteTelegraphs(spriteBatch, camera, playerWorldPosition, screenShake);
        foreach (var route in _historicalRoutes)
        {
            float fade = (float)(route.Remaining / route.Duration);
            Vector2 start = camera.WorldToScreen(route.Start, playerWorldPosition, screenShake);
            Vector2 end = camera.WorldToScreen(route.End, playerWorldPosition, screenShake);
            Color history = Color.Lerp(new Color(102, 198, 230), UiTheme.Cream, .45f) * (.06f + fade * .16f);
            Primitives2D.Line(spriteBatch, start, end, history, fade > .55f ? 2 : 1);
        }

        Vector2 screen = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        Vector2 center = screen + new Vector2(Size / 2f, Size / 2f);
        if (Dying)
        {
            BossVisuals.Disassemble(spriteBatch, center, Age, DeathProgress, Size * 1.15f,
                new Color(102, 198, 230), new Color(207, 241, 250), 16);
            return;
        }

        bool survival = MidpointSurvivalActive || FinaleActive;
        float auraScale = survival ? 1.42f : 1f;
        Color sky = new(103, 197, 231);
        Color ice = new(194, 235, 248);
        float seconds = VisualAgeSeconds;

        // The body no longer spins freely: it snaps forward in discrete clock ticks
        // (a brief eased swing, then a hold) so the boss visibly keeps time rather
        // than rotating like generic machinery.
        const float TickInterval = .85f;
        const float TickAngle = MathF.PI / 3f;
        float tickIndex = MathF.Floor(seconds / TickInterval);
        float tickLocalT = (seconds - tickIndex * TickInterval) / TickInterval;
        float swing = BossAnimation.EaseOutBack(Math.Clamp(tickLocalT / .3f, 0f, 1f));
        float yaw = MathHelper.Lerp(tickIndex * TickAngle, (tickIndex + 1) * TickAngle, swing);
        float pitch = .58f + MathF.Sin(seconds * .54f) * .26f;
        float roll = MathF.Sin(seconds * .39f) * .24f;

        // Afterimage echoes hold each of the body's own recent ticked poses instead
        // of drifting independently, so the trail reads as "the recent past" rather
        // than generic motion blur.
        int echoCount = survival ? 3 : 2;
        for (int echo = echoCount; echo >= 1; echo--)
        {
            float echoTickIndex = tickIndex - echo;
            if (echoTickIndex < 0)
                continue;
            float echoYaw = (echoTickIndex + 1) * TickAngle;
            float echoAlpha = MathHelper.Lerp(.22f, 0f, echo / (float)(echoCount + 1));
            float echoExtent = Size * .34f * (0.92f - echo * .05f);
            BossVisuals.RotatingCube3D(spriteBatch, center, echoExtent,
                sky * echoAlpha, ice * echoAlpha, PhaseAccent * echoAlpha, echoYaw, pitch, roll);
        }

        for (int index = 0; index < (FinaleActive ? FinaleMoteCount : AmbientMoteCount); index++)
        {
            float angle = index * 2.399963f + seconds * (index % 2 == 0 ? .36f : -.27f);
            float radius = Size * (.54f + (index % 5) * .14f) * auraScale;
            float pulse = .55f + .45f * MathF.Sin(seconds * 1.38f + index * 1.3f);
            var mote = center + new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius * .58f);
            int moteSize = 2 + index % 3;
            Primitives2D.FillRect(spriteBatch, new Rectangle((int)mote.X - moteSize, (int)mote.Y - moteSize,
                moteSize * 2, moteSize * 2), Color.Lerp(sky, UiTheme.Cream, pulse));
        }

        BossVisuals.RotatingCube3D(spriteBatch, center, Size * .34f, sky, ice, PhaseAccent, yaw, pitch, roll);
        float sweep = BossAnimation.EaseInOutSine(BossAnimation.LoopPhase(seconds, 3.8f));
        Vector2 sweepStart = center + new Vector2(-Size * .23f, Size * (.16f - sweep * .32f));
        Primitives2D.Line(spriteBatch, sweepStart,
            sweepStart + new Vector2(Size * .46f, -Size * .08f),
            UiTheme.Cream * BossAnimation.SeamFade(BossAnimation.LoopPhase(seconds, 3.8f), .18f), 2);
        for (int index = 0; index < TemporalInsightNeeded; index++)
        {
            float angle = -MathF.PI / 2f + index * MathF.Tau / TemporalInsightNeeded;
            Vector2 pip = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * Size * .46f;
            Color pipColor = index < TemporalInsight ? UiTheme.Cream : new Color(48, 124, 167);
            Primitives2D.FillRect(spriteBatch,
                new Rectangle((int)pip.X - 3, (int)pip.Y - 3, 6, 6), pipColor);
        }
        if (TemporalFractureActive)
        {
            float pulse = .5f + .5f * MathF.Sin(seconds * 7.2f);
            Primitives2D.CircleOutline(spriteBatch, center, Size * (.43f + pulse * .05f),
                UiTheme.Cream * (.55f + pulse * .35f), 3, 32);
            Primitives2D.CircleOutline(spriteBatch, center, Size * (.52f + pulse * .07f),
                PhaseAccent * (.28f + pulse * .22f), 2, 32);
            Primitives2D.Line(spriteBatch, center - new Vector2(Size * .18f, Size * .22f),
                center + new Vector2(Size * .04f, Size * .03f), UiTheme.Void, 4);
            Primitives2D.Line(spriteBatch, center + new Vector2(Size * .04f, Size * .03f),
                center + new Vector2(Size * .2f, Size * .16f), UiTheme.Cream, 3);
        }
        float inset = Size * (.065f + .01f * MathF.Sin(Age * .037f));
        var playerLikeCore = new Rectangle((int)(center.X - inset), (int)(center.Y - inset),
            Math.Max(3, (int)(inset * 2)), Math.Max(3, (int)(inset * 2)));
        Primitives2D.FillRect(spriteBatch, playerLikeCore, UiTheme.Cream);
        Primitives2D.RectOutline(spriteBatch, playerLikeCore, new Color(48, 124, 167), 2);
        DrawBossHealth(spriteBatch, new Rectangle((int)(center.X - Size * .46f), (int)(center.Y - Size * .72f), (int)(Size * .92f), 6));
    }
}
