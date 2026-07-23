using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
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
    protected override bool VisualSurvivalActive => MidpointSurvivalActive || FinaleActive || base.VisualSurvivalActive;

    public static readonly PathChaseBossConfig AcheConfig = KageConfig with
    {
        BossName = "ACHE", Subtitle = "THE UNCOMMANDED CORE", FinalBoss = true,
        OwnerPrefix = "ache_chemesthesis",
        FinalBodyColor = new Color(232, 112, 31), FinalAccentColor = new Color(54, 143, 218),
        FinalBodyScale = 1.6, FinalCooldownSeconds = 1.8, FinalShotSpeed = .34, FinalShotScale = .27,
        MovementSpeed = .21,
        MovementModes = new[] { "chase", "path", "chase", "static", "path", "chase", "path", "static" },
        PhaseLabels = new[]
        {
            "MISFIRE", "CROSSED NERVES", "WRONG WAY", "REFLEX STORM",
            "AFTERSHOCK", "FRACTURE", "WHITE ACHE", "OVERLOAD",
        },
        FinalHealth = 305000, FinalContactDamage = 880, FinalRewardExperience = 840,
        FinaleDuration = 30.0,
    };

    public static readonly SinSigilConfig AcheSinConfig = new(
        PhaseFlavors: new[]
        {
            "Ache answers an attacker that was never there.", "Three arms dispute where the border should be.",
            "The core recoils from a future that never happened.", "No command survives contact with the storm.",
            "Unclaimed ground is punished for trespass.", "Power splits wherever obedience should begin.",
            "Every warning points toward a different phantom.", "Thirty seconds of power without a master.",
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
    private double _compressionCooldown = 5.0;
    private double _consumedCrystalPulse;
    private int _lastPattern = -1;
    private int _castsSinceDirectedThreat;
    private readonly List<int> _patternHistory = new();
    private readonly List<ReactiveCounter> _reactiveCounters = new();

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

    protected override double DamageFloorRatio() => Phase switch
    {
        1 => .84,
        2 => .67,
        3 or 4 => .50,
        5 => .25,
        6 => .12,
        _ => 0.0,
    };

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
        if (DebugPhaseLocked || FinaleActive || MidpointSurvivalActive)
            return;
        double ratio = Math.Clamp((double)Hp / MaxHp, 0.0, 1.0);
        int desired;
        if (!MidpointSurvivalCleared)
        {
            if (ratio <= .5)
            {
                if (PhaseDeclarations >= MinimumDamagePhaseDeclarations)
                    BeginMidpointSurvival();
                return;
            }
            desired = ratio > .84 ? 1 : ratio > .67 ? 2 : 3;
        }
        else
        {
            desired = ratio > .25 ? 5 : ratio > .12 ? 6 : 7;
        }
        if (desired != Phase &&
            PhaseDeclarations >= MinimumDamagePhaseDeclarations)
            SetSinPhase(desired);
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

        if (MidpointSurvivalCleared && Phase == 7 &&
            PhaseDeclarations < MinimumDamagePhaseDeclarations)
        {
            double declarationPermitted = Math.Max(0, Hp - 1);
            if (declarationPermitted <= 0)
                return new HitResult(false, false, 0, true);
            var declarationGated = base.TakeDamage(
                Math.Min(amount, declarationPermitted), partId, source);
            return new HitResult(declarationGated.Applied, false,
                declarationGated.Amount, declarationGated.Blocked);
        }

        int floor = Math.Max(0, (int)Math.Round(MaxHp * DamageFloorRatio()));
        double permitted = floor > 0 ? Math.Max(0, Hp - floor) : amount;
        if (floor > 0 && permitted <= 0)
        {
            if (PhaseDeclarations >= MinimumDamagePhaseDeclarations)
                UpdatePhase();
            return new HitResult(false, false, 0, true);
        }
        var result = base.TakeDamage(floor > 0 ? Math.Min(amount, permitted) : amount, partId, source);
        if (!MidpointSurvivalCleared && Hp <= MaxHp * .5 &&
            PhaseDeclarations >= MinimumDamagePhaseDeclarations)
            BeginMidpointSurvival();
        else if (floor > 0 && Hp <= floor &&
                 PhaseDeclarations >= MinimumDamagePhaseDeclarations)
            UpdatePhase();
        if (FinaleActive)
            SetSinPhase(8);
        return new HitResult(result.Applied, false, result.Amount, result.Blocked);
    }

    public override void Update(EnemyUpdateContext context)
    {
        double dt = Seconds();
        UpdateReactiveCounters(context.ProjectileSink, dt);
        if (!MidpointSurvivalActive)
        {
            base.Update(context);
            return;
        }

        EntranceRemaining = Math.Max(0.0, EntranceRemaining - dt);
        VisualTransitionRemaining = Math.Max(0.0, VisualTransitionRemaining - dt);
        ActTransitionTimer = Math.Max(0.0, ActTransitionTimer - dt);
        PhaseProtectionTimer = Math.Max(0.0, PhaseProtectionTimer - dt);
        PhaseElapsed += dt;
        AdvanceAge();
        MidpointSurvivalRemaining = Math.Max(0.0, MidpointSurvivalRemaining - dt);
        _survivalCooldown -= dt;
        if (EntranceRemaining <= 0 && ActTransitionTimer <= 0 && _survivalCooldown <= 0)
        {
            FireSinPattern(context.PlayerWorldX, context.PlayerWorldY, context);
            _survivalCooldown = 1.75 + Rng.NextDouble() * .8;
        }
        if (MidpointSurvivalRemaining <= 0 && !DebugPhaseLocked)
        {
            MidpointSurvivalActive = false;
            MidpointSurvivalCleared = true;
            Hp = Math.Max(1, (int)Math.Round(MaxHp * .5));
            SetSinPhase(5);
        }
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
        float distance = Simulation.TileSize * distanceTiles;
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
            if (wall.Compression && wall.Warning <= 0)
            {
                float deltaX = center.X - wall.CenterX, deltaY = center.Y - wall.CenterY;
                float distance = Math.Max(1.0f, MathF.Sqrt(deltaX * deltaX + deltaY * deltaY));
                if (distance > Simulation.TileSize * 2.25f)
                {
                    float step = Simulation.TileSize * .34f * (float)dt;
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

    public override IReadOnlyList<Rectangle> MovementObstacles() =>
        _crystalWalls.Where(wall => wall.Warning <= 0).Select(wall => wall.Rect).ToList();

    public override IReadOnlyList<(string Part, Rectangle Rect)> GetScreenHitboxes(Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        var hitboxes = base.GetScreenHitboxes(camera, playerWorldPosition, screenShake).ToList();
        for (int index = 0; index < _crystalWalls.Count; index++)
        {
            var wall = _crystalWalls[index];
            if (wall.Kind != "brittle" || wall.Warning > 0)
                continue;
            var rect = wall.Rect;
            var corners = new[]
            {
                camera.WorldToScreen(new Vector2(rect.Left, rect.Top), playerWorldPosition, screenShake),
                camera.WorldToScreen(new Vector2(rect.Right, rect.Top), playerWorldPosition, screenShake),
                camera.WorldToScreen(new Vector2(rect.Right, rect.Bottom), playerWorldPosition, screenShake),
                camera.WorldToScreen(new Vector2(rect.Left, rect.Bottom), playerWorldPosition, screenShake),
            };
            float left = corners.Min(c => c.X), top = corners.Min(c => c.Y);
            float right = corners.Max(c => c.X), bottom = corners.Max(c => c.Y);
            hitboxes.Add(($"crystal:{index}", new Rectangle((int)left, (int)top, Math.Max(1, (int)(right - left)), Math.Max(1, (int)(bottom - top)))));
        }
        return hitboxes;
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
            if (NerveBreakProgress >= NerveBreaksNeeded)
            {
                NerveBreakProgress = 0;
                NerveBreakTriggers++;
                Stagger = MaxStagger;
                IsStaggered = true;
                StaggerRemaining = StaggerDuration;
                TransitionCleanupRequested = true;
                _consumedCrystalPulse = 1.4;
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

        float attackPulse = VisualAttackTimer > 0
            ? MathF.Sin(Math.Clamp(VisualAttackTimer / (Simulation.FrameRate * .58f), 0f, 1f) * MathF.PI)
            : 0f;
        float survivalSpread = VisualSurvivalActive ? 1.58f : 1f;
        float oscillation = 1f + MathF.Sin(Age * .09f) * .08f + attackPulse * .12f;
        float coreExtent = Size * .29f * oscillation;
        Vector2 jittered = center + new Vector2(
            MathF.Sin(Age * .031f) * 4.2f + MathF.Sin(Age * .007f) * 3.4f,
            MathF.Sin(Age * .023f + 1.1f) * 3.8f);

        var arms = new List<(Vector2 Center, float Angle, float Depth)>();
        for (int index = 0; index < OrbitingArmCount; index++)
        {
            float direction = index == 1 ? -1f : 1f;
            float drift = Age * (.009f + index * .0025f) * direction;
            float hesitation = MathF.Sin(Age * (.0034f + index * .0008f) + index * 1.9f) * .62f;
            float angle = drift + hesitation + index * MathF.Tau / OrbitingArmCount;
            float radius = Size * (.62f + .14f * MathF.Sin(Age * (.011f + index * .003f) + index * 1.7f)) * survivalSpread;
            float droop = Size * (.06f + index * .035f) * (.5f + .5f * MathF.Sin(Age * .006f + index));
            var armCenter = jittered + new Vector2(MathF.Cos(angle) * radius,
                MathF.Sin(angle) * radius * .54f + droop);
            arms.Add((armCenter, angle, MathF.Sin(angle)));
            Primitives2D.Line(spriteBatch, jittered, armCenter, UiTheme.Ink, Math.Max(4, (int)(Size * .07f)));
            Primitives2D.Line(spriteBatch, jittered, armCenter, blue * .72f, Math.Max(1, (int)(Size * .025f)));
        }

        foreach (var arm in arms.Where(arm => arm.Depth < 0).OrderBy(arm => arm.Depth))
            BossVisuals.RotatingCube3D(spriteBatch, arm.Center, Size * .12f, blue, new Color(75, 183, 235), orange,
                -arm.Angle * 1.3f, arm.Angle * .73f, Age * .013f);

        BossVisuals.RotatingCube3D(spriteBatch, jittered, coreExtent, orange, deepOrange, blue,
            Age * .041f, .58f + MathF.Sin(Age * .021f) * .32f, MathF.Sin(Age * .017f) * .18f);
        float energyRadius = Size * (.075f + .012f * MathF.Sin(Age * .11f));
        Primitives2D.FillCircle(spriteBatch, jittered, (int)energyRadius + 5, UiTheme.Ink);
        Primitives2D.FillCircle(spriteBatch, jittered, Math.Max(2, (int)energyRadius), blue);
        Primitives2D.CircleOutline(spriteBatch, jittered, energyRadius * 1.45f, UiTheme.Cream, 2);

        foreach (var arm in arms.Where(arm => arm.Depth >= 0).OrderBy(arm => arm.Depth))
            BossVisuals.RotatingCube3D(spriteBatch, arm.Center, Size * .12f, blue, new Color(75, 183, 235), orange,
                -arm.Angle * 1.3f, arm.Angle * .73f, Age * .013f);

        DrawBossHealth(spriteBatch, new Rectangle((int)(center.X - Size * .46f), (int)(center.Y - Size * .78f), (int)(Size * .92f), 6));
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
            var outer = screenRect;
            outer.Inflate(8, 8);
            Primitives2D.FillRect(spriteBatch, outer, UiTheme.Ink);
            if (warning)
                Primitives2D.RectOutline(spriteBatch, screenRect, color, 3);
            else
                Primitives2D.FillRect(spriteBatch, screenRect, color);
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
                Age * (.004f + index * .0003f), index * .31f, -index * .17f);
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

    private void TelegraphLash(List<EnemyProjectile> sink, Vector2 origin, float direction, float damage,
        string suffix, float angularSpeed = 0f)
    {
        sink.Add(new EnemyProjectile(origin.X, origin.Y, direction, 0f, damage, Size * .13f,
            travelRange: Simulation.TileSize * 30f, color: PhaseAccent, shape: "laser", path: "laser",
            lifetime: 2.35f, angularSpeed: angularSpeed, owner: $"ache_chemesthesis_{suffix}", ignoreWalls: true)
        {
            TelegraphDuration = 1.25f,
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
                scale: .22f + (float)Rng.NextDouble() * .08f, shape: "mine", path: "mine",
                lifetime: 10f + (float)Rng.NextDouble() * 3f, speedDecay: .045f, ownerSuffix: "wrong_way_hazard",
                affliction: "slow", afflictionDuration: 1.2, afflictionStrength: .1, exposure: .5);
            mine.TelegraphDuration = .9f;
        }
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
            shape: "mine", path: "mine", lifetime: lifetime + (float)Rng.NextDouble() * 2f,
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

    private static bool IsDirectedPattern(int pattern) => pattern is 1 or 5;

    private int ChoosePattern()
    {
        int[] choices = Phase switch
        {
            1 => new[] { 0, 0, 1, 2, 6 },
            2 => new[] { 0, 1, 1, 6 },
            3 => new[] { 0, 1, 2, 3, 6 },
            4 => new[] { 0, 1, 2, 3, 6, 7 },
            5 => new[] { 1, 2, 4, 4, 6, 7 },
            6 => new[] { 1, 3, 4, 6, 7 },
            7 => new[] { 1, 2, 4, 5, 6, 7 },
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
        var remaining = new List<ReactiveCounter>();
        foreach (var counter in _reactiveCounters)
        {
            double delay = counter.Delay - dt;
            if (delay <= 0)
                TelegraphLash(sink, Center(), counter.Direction, counter.Damage, counter.Suffix);
            else
                remaining.Add(counter with { Delay = delay });
        }
        _reactiveCounters.Clear();
        _reactiveCounters.AddRange(remaining);
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
            // field has enough unresolved mistakes, the next attack is a
            // visible hesitation while existing hazards do the work.
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
                break;
            case 1: // A reactable prediction: the exact route is harmless for 1.25 seconds.
            {
                float predictionError = (float)(Rng.NextDouble() * .5 - .25);
                TelegraphLash(sink, center, aimed + predictionError, HeavyDamage, "predicted_lash");
                if (Phase >= 5)
                    TelegraphLash(sink, center, aimed + MathF.PI + predictionError, HeavyDamage - 10, "reverse_lash");
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
                var mine = Shot(sink, aimed + MathF.PI + (float)(Rng.NextDouble() * .8 - .4), .48f,
                    MineDamage, scale: .25f, shape: "mine", path: "mine", lifetime: 10f,
                    speedDecay: .045f, ownerSuffix: "contamination_debris", affliction: "slow",
                    afflictionDuration: 1.1, afflictionStrength: .1, exposure: .5);
                mine.TelegraphDuration = 1.0f;
                break;
            }
            case 5: // Crossed nerves: two curved warnings sweep only after being fully shown.
                TelegraphLash(sink, center, aimed - .72f, HeavyDamage, "crossed_nerves_left", .11f);
                TelegraphLash(sink, center, aimed + .72f, HeavyDamage, "crossed_nerves_right", -.11f);
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
        _castsSinceDirectedThreat = directed ? 0 : _castsSinceDirectedThreat + 1;
        _lastPattern = pattern;
        _patternHistory.Add(pattern);
        if (_patternHistory.Count > 32)
            _patternHistory.RemoveAt(0);

        if (Phase >= 4 && !directed && PatternRotation % 2 == 1)
            QueueReactiveCounter(aimed);

        if (FinaleActive && PatternRotation % 2 == 0)
        {
            float angle = (float)(Rng.NextDouble() * MathF.Tau);
            TelegraphLash(sink, center, angle, HeavyDamage, "overload_callback", Rng.Next(2) == 0 ? .13f : -.13f);
        }
        PhaseDeclarations++;
        PatternRotation++;
        MarkAttack(.66f);
    }
}
