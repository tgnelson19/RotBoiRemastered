using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// "THE NEAR HORIZON" -- Sight's fast level-ten lesson. Ishe declares attacks
/// from its current position, moves away, then lets those afterimages fire.
/// Flash turns the triangle itself into a readable horizon-lane survival exam;
/// Afterglow combines the learned declarations without adding hidden threats.
/// </summary>
public class Ishe : PathChaseBoss
{
    public const int MinimumIsheDamagePhaseDeclarations = 2;
    private static readonly IReadOnlyDictionary<int, (string Label, string Flavor, Color Accent)> PhaseMetadata =
        new Dictionary<int, (string, string, Color)>
        {
            [1] = ("GLIMPSE", "The first line is visible before the eye can follow.", new Color(107, 190, 221)),
            [2] = ("BLINK", "The body has moved. Its former position has not finished speaking.", new Color(235, 142, 59)),
            [3] = ("FLASH", "The triangle closes everywhere except the declared horizon.", UiTheme.Cream),
            [4] = ("AFTERGLOW", "Two former positions answer in sequence.", new Color(238, 188, 83)),
        };

    public static readonly PathChaseBossConfig IsheConfig = PathChaseBossConfig.Default with
    {
        BossName = "ISHE", Subtitle = "THE NEAR HORIZON",
        PhaseLabels = PhaseMetadata.OrderBy(pair => pair.Key).Select(pair => pair.Value.Label).ToArray(),
        Pattern = "rush", OwnerPrefix = "ishe_sight",
        BodyColor = new Color(107, 190, 221), AccentColor = new Color(235, 142, 59),
        MovementSpeed = .43, BodyScale = 1.42, CooldownSeconds = 1.4,
        ShotSpeed = 1.45, ShotDamage = 215, ShotScale = .18, ShotRangeTiles = 24,
        ArenaShape = "triangle", ArenaScale = 11.2,
        MovementModes = new[] { "chase", "path", "static", "path" },
        MidHealth = 75000, MidContactDamage = 300, MidRewardExperience = 360,
    };

    private sealed record PendingVolley(double Delay, Vector2 Origin, float Direction, int Count,
        float Spread, float Speed, float Damage, string Suffix);

    private readonly List<PendingVolley> _pendingVolleys = new();
    private Vector2? _lastDeclarationOrigin;
    private double _flashCooldown;
    private int _ishePatternRotation;
    private int _phaseDeclarations;

    public bool FlashSurvivalActive { get; private set; }
    public bool FlashSurvivalCleared { get; private set; }
    public double FlashSurvivalDuration { get; } = 12.0;
    public double FlashSurvivalRemaining { get; private set; }
    public int IshePatternRotation => _ishePatternRotation;
    public int IshePhaseDeclarations => _phaseDeclarations;
    protected virtual bool UsesIsheEncounter => true;
    protected override bool VisualSurvivalActive => FlashSurvivalActive || base.VisualSurvivalActive;
    protected override bool TargetRealPlayerDuringPathMovement => true;

    protected static readonly IReadOnlyDictionary<string, (string Name, Vector2[][] Strokes)> SightSymbols =
        new Dictionary<string, (string, Vector2[][])>
        {
            ["GLIMPSE"] = ("GLIMPSE", new[]
            {
                new[] { new Vector2(-.7f, 0), new Vector2(0, -.45f), new Vector2(.7f, 0), new Vector2(0, .45f), new Vector2(-.7f, 0) },
                new[] { new Vector2(0, -.18f), new Vector2(0, .18f) },
            }),
            ["BLINK"] = ("BLINK", new[]
            {
                new[] { new Vector2(-.72f, -.3f), new Vector2(0, 0), new Vector2(.72f, -.3f) },
                new[] { new Vector2(-.72f, .3f), new Vector2(0, 0), new Vector2(.72f, .3f) },
            }),
            ["FLASH"] = ("FLASH", new[]
            {
                new[]
                {
                    new Vector2(0, -.76f), new Vector2(-.18f, -.14f), new Vector2(.3f, -.14f),
                    new Vector2(-.22f, .76f), new Vector2(0, .12f), new Vector2(-.34f, .12f), new Vector2(0, -.76f),
                },
            }),
            ["AFTERGLOW"] = ("AFTERGLOW", new[]
            {
                new[] { new Vector2(-.7f, 0), new Vector2(-.16f, -.42f), new Vector2(.48f, -.18f) },
                new[] { new Vector2(-.48f, .18f), new Vector2(.16f, .42f), new Vector2(.7f, 0) },
                new[] { new Vector2(-.14f, -.18f), new Vector2(.14f, .18f) },
            }),
        };

    private static readonly IReadOnlyList<string> SightSymbolOrder = new[] { "GLIMPSE", "BLINK", "FLASH", "AFTERGLOW" };

    public Ishe(float worldX, float worldY, Battleground battleground, Random? rng = null)
        : this(worldX, worldY, battleground, IsheConfig, rng)
    {
    }

    protected Ishe(float worldX, float worldY, Battleground battleground, PathChaseBossConfig config, Random? rng = null)
        : base(worldX, worldY, battleground, config, rng)
    {
        if (!config.FinalBoss)
            ApplyIshePhase(1);
    }

    private void ApplyIshePhase(int phase)
    {
        Phase = Math.Clamp(phase, 1, PhaseMetadata.Count);
        (PhaseLabel, PhaseFlavor, PhaseAccent) = PhaseMetadata[Phase];
        PhaseElapsed = 0.0;
        _phaseDeclarations = 0;
        VisualTransitionRemaining = 1.1;
        AttackCooldown = Math.Min(AttackCooldown ?? 0f, Simulation.FrameRate * .45f);
        _pendingVolleys.Clear();
        TransitionCleanupRequested = true;
    }

    private void BeginFlashSurvival()
    {
        if (FlashSurvivalActive || FlashSurvivalCleared)
            return;
        Hp = Math.Max(1, (int)Math.Round(MaxHp * .5));
        ApplyIshePhase(3);
        FlashSurvivalActive = true;
        FlashSurvivalRemaining = FlashSurvivalDuration;
        _flashCooldown = .25;
    }

    protected override void UpdatePhase()
    {
        if (!UsesIsheEncounter)
        {
            base.UpdatePhase();
            return;
        }
        if (DebugPhaseLocked || FlashSurvivalActive)
            return;
        double ratio = Math.Clamp((double)Hp / MaxHp, 0.0, 1.0);
        int desired;
        if (!FlashSurvivalCleared)
        {
            if (ratio <= .5)
            {
                if (_phaseDeclarations < MinimumIsheDamagePhaseDeclarations)
                    return;
                BeginFlashSurvival();
                return;
            }
            if (Phase == 1 && ratio <= .72 && _phaseDeclarations < MinimumIsheDamagePhaseDeclarations)
                return;
            desired = ratio > .72 ? 1 : 2;
        }
        else
        {
            desired = 4;
        }
        if (desired != Phase)
            ApplyIshePhase(desired);
    }

    public override void DebugSetPhase(int phase)
    {
        if (!UsesIsheEncounter)
        {
            base.DebugSetPhase(phase);
            return;
        }
        phase = Math.Clamp(phase, 1, 4);
        DebugPhaseLocked = true;
        FlashSurvivalActive = false;
        if (phase >= 4)
            FlashSurvivalCleared = true;
        ApplyIshePhase(phase);
        AttackCooldown = 0f;
        if (phase == 3)
        {
            FlashSurvivalActive = true;
            FlashSurvivalRemaining = FlashSurvivalDuration;
            _flashCooldown = 0;
        }
    }

    public override HitResult TakeDamage(double amount, string partId = "body", DamageSource source = DamageSource.Direct)
    {
        if (!UsesIsheEncounter)
            return base.TakeDamage(amount, partId, source);
        if (FlashSurvivalActive || Dying)
            return new HitResult(false, false, 0, true);

        if (!FlashSurvivalCleared)
        {
            double floorRatio = Phase == 1 ? .72 : .50;
            int floor = Math.Max(1, (int)Math.Round(MaxHp * floorRatio));
            double permitted = Math.Max(0, Hp - floor);
            if (permitted <= 0)
            {
                if (_phaseDeclarations < MinimumIsheDamagePhaseDeclarations)
                    return new HitResult(false, false, 0, true);
                if (Phase == 1)
                    ApplyIshePhase(2);
                else
                    BeginFlashSurvival();
                return new HitResult(false, false, 0, true);
            }
            var gated = base.TakeDamage(Math.Min(amount, permitted), partId, source);
            if (Hp <= MaxHp * .5 && _phaseDeclarations >= MinimumIsheDamagePhaseDeclarations)
                BeginFlashSurvival();
            else if (Hp <= MaxHp * .72 && _phaseDeclarations >= MinimumIsheDamagePhaseDeclarations)
                ApplyIshePhase(2);
            return new HitResult(gated.Applied, false, gated.Amount, gated.Blocked);
        }

        if (Phase == 4 && _phaseDeclarations < MinimumIsheDamagePhaseDeclarations)
        {
            double permitted = Math.Max(0, Hp - 1);
            if (permitted <= 0)
                return new HitResult(false, false, 0, true);
            var gated = base.TakeDamage(Math.Min(amount, permitted), partId, source);
            return new HitResult(gated.Applied, false, gated.Amount, gated.Blocked);
        }
        return base.TakeDamage(amount, partId, source);
    }

    private void WarningLine(List<EnemyProjectile> sink, Vector2 origin, float direction,
        float duration, string suffix)
    {
        var warning = new EnemyProjectile(origin.X, origin.Y, direction, 0f, 0f, Size * .045f,
            travelRange: ArenaRadius * 2.1f, color: PhaseAccent, shape: "laser", path: "laser",
            lifetime: duration, owner: $"ishe_{suffix}_warning", ignoreWalls: true)
        {
            TelegraphDuration = duration + .1f,
            Illusory = true,
        };
        sink.Add(warning);
    }

    private void DeclareVolley(List<EnemyProjectile> sink, Vector2 origin, Vector2 target,
        int count, float spread, double delay, float speed, float damage, string suffix)
    {
        float aimed = MathF.Atan2(target.Y - origin.Y, target.X - origin.X);
        for (int index = 0; index < count; index++)
        {
            float offset = count == 1 ? 0f : -spread / 2f + spread * index / (count - 1);
            WarningLine(sink, origin, aimed + offset, (float)delay, suffix);
        }
        _pendingVolleys.Add(new PendingVolley(delay, origin, aimed, count, spread, speed, damage, suffix));
        _lastDeclarationOrigin = origin;
    }

    private void UpdatePendingVolleys(double dt, List<EnemyProjectile> sink)
    {
        if (_pendingVolleys.Count == 0)
            return;
        var remaining = new List<PendingVolley>(_pendingVolleys.Count);
        foreach (var volley in _pendingVolleys)
        {
            double delay = volley.Delay - dt;
            if (delay > 0)
            {
                remaining.Add(volley with { Delay = delay });
                continue;
            }
            for (int index = 0; index < volley.Count; index++)
            {
                float offset = volley.Count == 1 ? 0f :
                    -volley.Spread / 2f + volley.Spread * index / (volley.Count - 1);
                float shotSize = Size * .18f;
                sink.Add(new EnemyProjectile(
                    volley.Origin.X - shotSize / 2f, volley.Origin.Y - shotSize / 2f,
                    volley.Direction + offset, volley.Speed, volley.Damage, shotSize,
                    travelRange: ArenaRadius * 2.2f, color: PhaseAccent, shape: "diamond",
                    owner: $"ishe_{volley.Suffix}_afterimage", ignoreWalls: true));
            }
        }
        _pendingVolleys.Clear();
        _pendingVolleys.AddRange(remaining);
    }

    protected override void FirePattern(float playerX, float playerY, List<EnemyProjectile> sink)
    {
        if (!UsesIsheEncounter)
        {
            base.FirePattern(playerX, playerY, sink);
            return;
        }
        var center = Center();
        var target = new Vector2(playerX, playerY);
        switch (Phase)
        {
            case 1:
                DeclareVolley(sink, center, target, 3, .34f, .68, 1.38f, 205, "glimpse");
                break;
            case 2:
            {
                Vector2 echo = _lastDeclarationOrigin ?? center;
                if (Vector2.DistanceSquared(echo, center) < Simulation.TileSize * Simulation.TileSize)
                {
                    float aimed = MathF.Atan2(playerY - center.Y, playerX - center.X);
                    echo = center + new Vector2(-MathF.Sin(aimed), MathF.Cos(aimed)) * Simulation.TileSize * 2.2f;
                }
                DeclareVolley(sink, center, target, 2, .24f, .56, 1.48f, 215, "blink_present");
                DeclareVolley(sink, echo, target, 2, .24f, .84, 1.42f, 215, "blink_past");
                break;
            }
            default:
            {
                Vector2 echo = _lastDeclarationOrigin ?? center;
                DeclareVolley(sink, center, target, 5, .72f, .62, 1.48f, 225, "afterglow_present");
                DeclareVolley(sink, echo, target, 3, .42f, .98, 1.36f, 215, "afterglow_past");
                break;
            }
        }
        _ishePatternRotation++;
        _phaseDeclarations++;
        MarkAttack(.46f);
    }

    private void FireFlashHorizon(float playerX, float playerY, List<EnemyProjectile> sink)
    {
        var vertices = ArenaVertices();
        int edgeIndex = _ishePatternRotation % vertices.Count;
        Vector2 start = vertices[edgeIndex];
        Vector2 end = vertices[(edgeIndex + 1) % vertices.Count];
        Vector2 edge = end - start;
        float edgeLengthSq = Math.Max(1f, edge.LengthSquared());
        float playerProjection = Math.Clamp(
            Vector2.Dot(new Vector2(playerX, playerY) - start, edge) / edgeLengthSq, 0f, 1f);
        const int laneCount = 7;
        int playerLane = Math.Clamp((int)MathF.Round(playerProjection * (laneCount - 1)), 0, laneCount - 1);
        int safeLane = Math.Clamp(playerLane, 1, laneCount - 2);
        if (_ishePatternRotation > 0)
        {
            int shiftDirection = playerLane < laneCount / 2 ? 1 :
                playerLane > laneCount / 2 ? -1 :
                _ishePatternRotation % 2 == 0 ? 1 : -1;
            safeLane = Math.Clamp(playerLane + shiftDirection * 2, 1, laneCount - 2);
        }
        Vector2 inward = ArenaCenter - (start + end) / 2f;
        inward.Normalize();
        float direction = MathF.Atan2(inward.Y, inward.X);
        for (int lane = 0; lane < laneCount; lane++)
        {
            if (Math.Abs(lane - safeLane) <= 1)
                continue;
            float fraction = .08f + lane / (float)(laneCount - 1) * .84f;
            Vector2 origin = Vector2.Lerp(start, end, fraction);
            var laser = new EnemyProjectile(origin.X, origin.Y, direction, 0f, 235, Size * .08f,
                travelRange: ArenaRadius * 1.75f, color: PhaseAccent, shape: "laser", path: "laser",
                lifetime: 1.52f, owner: $"ishe_flash_horizon_{edgeIndex}_{lane}", ignoreWalls: true)
            {
                TelegraphDuration = 1.05f,
            };
            sink.Add(laser);
        }
        _ishePatternRotation++;
        MarkAttack(.5f);
    }

    private void ApplyIsheCadence()
    {
        double seconds = Phase switch { 1 => 1.72, 2 => 1.48, _ => 1.28 };
        AttackCooldown = Simulation.FrameRate * (float)(seconds * (.94 + Rng.NextDouble() * .12));
    }

    public override void Update(EnemyUpdateContext context)
    {
        if (!UsesIsheEncounter)
        {
            base.Update(context);
            return;
        }

        double dt = Seconds();
        UpdatePendingVolleys(dt, context.ProjectileSink);
        if (!FlashSurvivalActive)
        {
            int patternBefore = _ishePatternRotation;
            base.Update(context);
            if (_ishePatternRotation != patternBefore)
                ApplyIsheCadence();
            return;
        }

        EntranceRemaining = Math.Max(0.0, EntranceRemaining - dt);
        VisualTransitionRemaining = Math.Max(0.0, VisualTransitionRemaining - dt);
        PhaseElapsed += dt;
        AdvanceAge();
        FlashSurvivalRemaining = Math.Max(0.0, FlashSurvivalRemaining - dt);
        _flashCooldown -= dt;
        if (EntranceRemaining <= 0 && _flashCooldown <= 0)
        {
            FireFlashHorizon(context.PlayerWorldX, context.PlayerWorldY, context.ProjectileSink);
            double elapsed = FlashSurvivalDuration - FlashSurvivalRemaining;
            _flashCooldown = elapsed < FlashSurvivalDuration * .5 ? 1.75 : 1.48;
        }
        if (FlashSurvivalRemaining <= 0 && !DebugPhaseLocked)
        {
            FlashSurvivalActive = false;
            FlashSurvivalCleared = true;
            Hp = Math.Max(1, (int)Math.Round(MaxHp * .5));
            ApplyIshePhase(4);
        }
    }

    public override void Draw(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        base.Draw(spriteBatch, camera, playerWorldPosition, screenShake);
        Vector2 screenPosition = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        var rect = new Rectangle((int)screenPosition.X, (int)screenPosition.Y, (int)Size, (int)Size);
        var (name, strokes) = SightSymbols[SightSymbolOrder[(Phase - 1) % SightSymbolOrder.Count]];
        float radius = Size * .34f;
        foreach (var stroke in strokes)
        {
            var points = stroke.Select(p => new Vector2(rect.Center.X + p.X * radius, rect.Center.Y + p.Y * radius)).ToArray();
            Primitives2D.Polyline(spriteBatch, points, false, UiTheme.Ink, Math.Max(4, (int)(radius * .13f)));
            Primitives2D.Polyline(spriteBatch, points, false, PhaseAccent, Math.Max(2, (int)(radius * .06f)));
            Primitives2D.Polyline(spriteBatch, points, false, UiTheme.Cream, 1);
        }
        if (EntranceRemaining > 0)
        {
            UiTheme.DrawText(spriteBatch, name, 9, PhaseAccent, new Vector2(rect.Center.X, rect.Y - 12), "midbottom");
        }
    }
}
