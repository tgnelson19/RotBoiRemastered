using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// Touch's second-oldest ancient: placid, burdensome, and vastly stronger than
/// its half-buried silhouette suggests. Rot does not rush the player; it makes
/// the arena carry its weight through slow fronts, bombs, and ground-level poison.
/// </summary>
public sealed class Rot : PathChaseBoss
{
    public const int AbsorptionParticleCount = 16;
    public const int FinaleAbsorptionParticleCount = 22;
    public const int ActiveBurdenSoftCap = 112;
    public const int MinimumDamagePhaseDeclarations = 2;
    public const int ReliefStepsNeeded = 3;
    public const int ReliefHazardClearCount = 10;
    public const int BurialStrataCount = 5;
    private static readonly IReadOnlyDictionary<int, (string Label, string Flavor, Color Accent)> PhaseMetadata =
        new Dictionary<int, (string, string, Color)>
        {
            [1] = ("SEEP", "Rot does not advance. The ground carries its touch outward.", new Color(119, 137, 64)),
            [2] = ("SILT", "A patient weight settles everywhere except one bank.", new Color(145, 112, 57)),
            [3] = ("SLUMP", "The ancient sinks by an inch. The whole floor answers.", new Color(117, 86, 52)),
            [4] = ("CHOKING STILLNESS", "Placidity is not weakness. Find the clean edge.", new Color(192, 176, 112)),
            [5] = ("BLOOM", "Discarded matter returns to the guardian that bore it.", new Color(108, 151, 66)),
            [6] = ("MIASMA", "Immense power commands the room without giving chase.", new Color(145, 82, 54)),
            [7] = ("BURIAL", "Thirty-five seconds beneath the burden the others feared to move.", new Color(177, 104, 50)),
        };

    public static readonly PathChaseBossConfig RotConfig = PathChaseBossConfig.Default with
    {
        BossName = "ROT", Subtitle = "THE BURIED ANCIENT", FinalBoss = true,
        OwnerPrefix = "rot_touch", Pattern = "boulder",
        PhaseLabels = PhaseMetadata.OrderBy(pair => pair.Key).Select(pair => pair.Value.Label).ToArray(),
        FinalBodyColor = new Color(91, 65, 42), FinalAccentColor = new Color(154, 118, 55),
        FinalBodyScale = 2.85, FinalCooldownSeconds = 2.05,
        FinalShotSpeed = .30, FinalShotDamage = 590, FinalShotScale = .32,
        MovementSpeed = .045, ArenaShape = "square", ArenaScale = 10.2,
        MovementModes = new[] { "static", "path", "static", "static", "path", "static", "static" },
        FinalHealth = 330000, FinalContactDamage = 980, FinalRewardExperience = 900,
        FinaleDuration = 35.0,
    };

    public bool MidpointSurvivalActive { get; private set; }
    public bool MidpointSurvivalCleared { get; private set; }
    public double MidpointSurvivalDuration { get; } = 22.0;
    public double MidpointSurvivalRemaining { get; private set; }
    public int PatternRotation { get; private set; }
    public int PhaseDeclarations { get; private set; }
    public float SafeCorridorAngle { get; private set; }
    public int SludgePoolsCreated { get; private set; }
    public int ReliefProgress { get; private set; }
    public int ReliefTriggers { get; private set; }
    public int ReliefHazardsCleared { get; private set; }
    public double ReliefPulseRemaining { get; private set; }
    public int BurialLayerCount => FinaleActive
        ? Math.Min(BurialStrataCount, 1 + (int)(FinaleProgress * BurialStrataCount))
        : 0;
    private double _survivalCooldown;
    private bool _corridorInitialized;
    private readonly int _corridorTurnSign;

    protected override bool VisualSurvivalActive => MidpointSurvivalActive || FinaleActive || base.VisualSurvivalActive;
    protected override bool TargetRealPlayerDuringPathMovement => true;

    public Rot(float worldX, float worldY, Battleground battleground, Random? rng = null)
        : base(worldX, worldY, battleground, RotConfig, rng)
    {
        _corridorTurnSign = Rng.Next(2) == 0 ? -1 : 1;
        ApplyPhase(1);
    }

    private void ApplyPhase(int phase)
    {
        int previousPhase = Phase;
        Phase = Math.Clamp(phase, 1, PhaseMetadata.Count);
        (PhaseLabel, PhaseFlavor, PhaseAccent) = PhaseMetadata[Phase];
        PhaseElapsed = 0.0;
        PhaseDeclarations = 0;
        VisualTransitionRemaining = 1.5;
        AttackCooldown = Math.Min(AttackCooldown ?? 0f, Simulation.FrameRate * .7f);
        // Rot is accumulation made animate. Minor damage movements retain the
        // previous layer; survival boundaries still wash the room clean so the
        // authored endurance checks always begin from a fair, known state.
        bool accumulatingTransition = (previousPhase, Phase) is (1, 2) or (2, 3) or (5, 6);
        TransitionCleanupRequested = !accumulatingTransition;
    }

    private void BeginMidpointSurvival()
    {
        if (MidpointSurvivalActive || MidpointSurvivalCleared)
            return;
        Hp = Math.Max(1, (int)Math.Round(MaxHp * .5));
        ApplyPhase(4);
        MidpointSurvivalActive = true;
        MidpointSurvivalRemaining = MidpointSurvivalDuration;
        _survivalCooldown = .25;
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
            desired = ratio > .25 ? 5 : 6;
        }
        if (desired != Phase && PhaseDeclarations >= MinimumDamagePhaseDeclarations)
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

        if (MidpointSurvivalCleared && Phase == 6 &&
            PhaseDeclarations < MinimumDamagePhaseDeclarations)
        {
            double phasePermitted = Math.Max(0, Hp - 1);
            if (phasePermitted <= 0)
                return new HitResult(false, false, 0, true);
            var declarationGated = base.TakeDamage(Math.Min(amount, phasePermitted), partId, source);
            return new HitResult(declarationGated.Applied, false,
                declarationGated.Amount, declarationGated.Blocked);
        }

        double floorRatio = !MidpointSurvivalCleared
            ? Phase switch { 1 => .84, 2 => .67, _ => .50 }
            : Phase == 5 ? .25 : 0.0;
        int floor = Math.Max(0, (int)Math.Round(MaxHp * floorRatio));
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
            ApplyPhase(7);
        return new HitResult(result.Applied, false, result.Amount, result.Blocked);
    }

    private void SludgePool(List<EnemyProjectile> sink, Vector2 position, float scale = 1f, float lifetime = 11f)
    {
        float size = Simulation.TileSize * 2.35f * scale;
        sink.Add(new EnemyProjectile(position.X - size / 2f, position.Y - size / 2f, 0f, 0f, 420, size,
            color: new Color(105, 91, 43), shape: "pool", path: "pool", lifetime: lifetime,
            owner: "rot_touch_floor_lava", ignoreWalls: true)
        {
            TelegraphDuration = 1.65f,
            PersistentHazard = true,
            Affliction = "slow",
            AfflictionDuration = 1.6,
            AfflictionStrength = .16,
            Exposure = .85,
        });
        SludgePoolsCreated++;
    }

    private void RotBomb(List<EnemyProjectile> sink, Vector2 target, float damage = 610)
    {
        var center = Center();
        float size = Size * .3f;
        sink.Add(new EnemyProjectile(center.X, center.Y, 0f, 0f, damage, size,
            color: new Color(175, 99, 46), shape: "bomb", path: "bomb", lifetime: 4.2f,
            target: target, owner: "rot_touch_bomb", ignoreWalls: true)
        {
            FuseDuration = 3.0f,
            BlastRadius = Simulation.TileSize * 2.05f,
            BurstCount = 4,
            BurstRangeTiles = 10f,
        });
    }

    private static float AngleDifference(float a, float b) =>
        MathF.Abs(MathF.Atan2(MathF.Sin(a - b), MathF.Cos(a - b)));

    private Vector2 SquareBankPoint(float angle, float inset = .84f)
    {
        var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        float squareScale = ArenaRadius / Math.Max(.001f, Math.Max(MathF.Abs(direction.X), MathF.Abs(direction.Y)));
        return ArenaCenter + direction * squareScale * inset;
    }

    private bool AdvanceSafeCorridor(float playerX, float playerY)
    {
        if (!_corridorInitialized)
        {
            SafeCorridorAngle = MathF.Atan2(playerY - ArenaCenter.Y, playerX - ArenaCenter.X);
            _corridorInitialized = true;
            return false;
        }
        if (PatternRotation > 0 && PatternRotation % 2 == 0)
        {
            float turn = Phase >= 5 ? .36f : .27f;
            SafeCorridorAngle += _corridorTurnSign * turn;
            return true;
        }
        return false;
    }

    private void ResolveRelief(float playerX, float playerY, List<EnemyProjectile> sink,
        bool corridorTurned)
    {
        if (!corridorTurned)
            return;

        var offset = new Vector2(playerX, playerY) - ArenaCenter;
        float playerAngle = MathF.Atan2(offset.Y, offset.X);
        bool followedCleanBank = offset.Length() >= ArenaRadius * .42f &&
            AngleDifference(playerAngle, SafeCorridorAngle) <= .40f;
        ReliefProgress = followedCleanBank ? ReliefProgress + 1 : 0;
        if (ReliefProgress < ReliefStepsNeeded)
            return;

        ReliefProgress = 0;
        ReliefTriggers++;
        ReliefPulseRemaining = 1.2;
        int cleared = 0;
        foreach (var burden in sink.Where(projectile =>
                     !projectile.RemFlag &&
                     projectile.Owner?.StartsWith("rot_touch", StringComparison.Ordinal) == true &&
                     (projectile.PersistentHazard || projectile.Path is "bank" or "bomb"))
                 .Take(ReliefHazardClearCount))
        {
            burden.RemFlag = true;
            cleared++;
        }
        ReliefHazardsCleared += cleared;
    }

    private void SlowFront(List<EnemyProjectile> sink, float direction, int lanes, float gapAngle,
        string suffix, float gapHalfWidth = .55f)
    {
        float step = MathF.Tau / lanes;
        for (int index = 0; index < lanes; index++)
        {
            float angle = direction + index * step;
            if (AngleDifference(angle, gapAngle) <= gapHalfWidth)
                continue;
            float size = Size * .26f;
            var boundary = SquareBankPoint(angle, 1.0f);
            var origin = ArenaCenter + (boundary - ArenaCenter) * .48f;
            float remaining = Vector2.Distance(origin, boundary) * 1.12f;
            sink.Add(new EnemyProjectile(origin.X - size / 2f, origin.Y - size / 2f, angle,
                .72f + (index % 3) * .05f, 540, size,
                travelRange: remaining, color: PhaseAccent, shape: "diamond", path: "bank",
                lifetime: 24f,
                owner: $"rot_touch_{suffix}", ignoreWalls: true)
            {
                TelegraphDuration = .8f,
                Affliction = "slow",
                AfflictionDuration = 1.3,
                AfflictionStrength = .12,
                Exposure = .55,
            });
        }
    }

    private void SeedOuterBanks(List<EnemyProjectile> sink, float safeAngle, int count = 8)
    {
        for (int index = 0; index < count; index++)
        {
            float angle = index * MathF.Tau / count + PatternRotation * .08f;
            if (AngleDifference(angle, safeAngle) < .7f)
                continue;
            SludgePool(sink, SquareBankPoint(angle, .9f), .78f + .08f * (index % 2), lifetime: 9.5f);
        }
    }

    private void SeedInnerPools(List<EnemyProjectile> sink, float safeAngle, int count, float radiusScale = .47f)
    {
        for (int index = 0; index < count; index++)
        {
            float angle = index * MathF.Tau / count + PatternRotation * .19f;
            float difference = MathF.Abs(MathF.Atan2(MathF.Sin(angle - safeAngle), MathF.Cos(angle - safeAngle)));
            if (difference < .7f)
                continue;
            float radius = ArenaRadius * (radiusScale + .08f * (index % 2));
            SludgePool(sink, ArenaCenter + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius,
                .82f + .1f * (index % 3));
        }
    }

    protected override void FirePattern(float playerX, float playerY, List<EnemyProjectile> sink)
    {
        bool corridorTurned = AdvanceSafeCorridor(playerX, playerY);
        ResolveRelief(playerX, playerY, sink, corridorTurned);
        int activeBurden = sink.Count(projectile =>
            !projectile.RemFlag &&
            projectile.Owner?.StartsWith("rot_touch", StringComparison.Ordinal) == true);
        if (activeBurden >= ActiveBurdenSoftCap)
        {
            // Rot does not become frantic when the room is already full. It
            // settles, lets the existing strata advance, then adds more mass.
            MarkAttack(.35f);
            return;
        }
        switch (Phase)
        {
            case 1:
                if (PatternRotation % 2 == 0)
                    SeedInnerPools(sink, SafeCorridorAngle, 7, .34f);
                else
                    SeedOuterBanks(sink, SafeCorridorAngle);
                break;
            case 2:
                SlowFront(sink, PatternRotation * .16f, 10, SafeCorridorAngle, "silt_front", .62f);
                break;
            case 3:
                SeedInnerPools(sink, SafeCorridorAngle, 9);
                // The first bomb marks the clean bank the player just occupied;
                // the others land in the heavy opposite side. Three seconds of
                // fuse makes relocation deliberate rather than twitchy.
                RotBomb(sink, SquareBankPoint(SafeCorridorAngle, .78f));
                for (int index = -1; index <= 1; index += 2)
                {
                    float angle = SafeCorridorAngle + MathF.PI + index * .55f;
                    RotBomb(sink, ArenaCenter + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * ArenaRadius * .52f);
                }
                break;
            case 5:
                if (PatternRotation % 2 == 0)
                {
                    if (PatternRotation % 4 == 0)
                        SeedOuterBanks(sink, SafeCorridorAngle, 10);
                    else
                        SeedInnerPools(sink, SafeCorridorAngle, 9, .5f);
                }
                else
                    SlowFront(sink, PatternRotation * .11f, 10, SafeCorridorAngle, "bloom_spores", .48f);
                break;
            case 6:
                if (PatternRotation % 2 == 0)
                    SlowFront(sink, PatternRotation * .18f, 10, SafeCorridorAngle, "miasma_front", .42f);
                else
                {
                    SeedInnerPools(sink, SafeCorridorAngle, 8, .5f);
                    RotBomb(sink, SquareBankPoint(SafeCorridorAngle, .8f), 680);
                }
                break;
            default:
                if (PatternRotation % 3 == 0)
                {
                    SeedInnerPools(sink, SafeCorridorAngle, 9, .5f);
                    SeedOuterBanks(sink, SafeCorridorAngle, 8);
                }
                else if (PatternRotation % 3 == 1)
                {
                    SlowFront(sink, PatternRotation * .14f, 12, SafeCorridorAngle, "burial_front", .38f);
                    RotBomb(sink, SquareBankPoint(SafeCorridorAngle, .8f), 700);
                }
                else
                {
                    for (int index = 0; index < 3; index++)
                    {
                        float angle = SafeCorridorAngle + MathF.PI + (index - 1f) * .5f;
                        RotBomb(sink, ArenaCenter + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * ArenaRadius * (.28f + index * .09f), 700);
                    }
                    SlowFront(sink, PatternRotation * .09f, 10, SafeCorridorAngle, "burial_weight", .5f);
                }
                break;
        }
        PatternRotation++;
        PhaseDeclarations++;
        MarkAttack(.7f);
    }

    public override void Update(EnemyUpdateContext context)
    {
        double dt = Seconds();
        ReliefPulseRemaining = Math.Max(0.0, ReliefPulseRemaining - dt);
        if (!MidpointSurvivalActive)
        {
            base.Update(context);
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
            bool corridorTurned = AdvanceSafeCorridor(
                context.PlayerWorldX, context.PlayerWorldY);
            ResolveRelief(context.PlayerWorldX, context.PlayerWorldY,
                context.ProjectileSink, corridorTurned);
            if (PatternRotation % 4 == 0)
                SeedInnerPools(context.ProjectileSink, SafeCorridorAngle, 11, .52f);
            else if (PatternRotation % 4 == 2)
                SeedOuterBanks(context.ProjectileSink, SafeCorridorAngle, 10);
            else
                SlowFront(context.ProjectileSink, PatternRotation * .12f, 10, SafeCorridorAngle, "stillness_front", .5f);
            PatternRotation++;
            PhaseDeclarations++;
            _survivalCooldown = 2.35;
        }
        if (MidpointSurvivalRemaining <= 0 && !DebugPhaseLocked)
        {
            MidpointSurvivalActive = false;
            MidpointSurvivalCleared = true;
            Hp = Math.Max(1, (int)Math.Round(MaxHp * .5));
            ApplyPhase(5);
        }
    }

    protected override void DrawBossBody(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        Vector2 screen = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        Vector2 center = screen + new Vector2(Size / 2f, Size / 2f);
        DrawBurialStrata(spriteBatch, camera, playerWorldPosition, screenShake);
        if (Dying)
        {
            for (int ring = 0; ring < 5; ring++)
            {
                float cycle = (DeathProgress * 2.4f + ring / 5f) % 1f;
                var collapsePool = new Rectangle((int)(center.X - Size * (1f + cycle * 1.8f)),
                    (int)(center.Y - Size * (.18f + cycle * .4f)), (int)(Size * (2f + cycle * 3.6f)),
                    (int)(Size * (.36f + cycle * .8f)));
                Primitives2D.EllipseOutline(spriteBatch, collapsePool,
                    (ring % 2 == 0 ? new Color(91, 124, 55) : new Color(137, 69, 42)) * (1f - cycle * .72f), 3);
            }
            BossVisuals.Disassemble(spriteBatch, center, Age, DeathProgress, Size * 1.2f,
                new Color(90, 64, 41), new Color(113, 143, 61), 22);
            return;
        }

        float sink = Size * (.1f + .018f * MathF.Sin(Age * .009f));
        var poolBack = new Rectangle((int)(center.X - Size * .88f), (int)(center.Y + Size * .08f),
            (int)(Size * 1.76f), (int)(Size * .58f));
        Primitives2D.FillEllipse(spriteBatch, new Rectangle(poolBack.X + 8, poolBack.Y + 9, poolBack.Width, poolBack.Height), UiTheme.Shadow);
        Primitives2D.FillEllipse(spriteBatch, poolBack, new Color(68, 64, 35));
        for (int ring = 0; ring < 3; ring++)
        {
            float breathe = 1f + MathF.Sin(Age * .008f + ring * 1.4f) * .08f;
            var ripple = new Rectangle((int)(center.X - Size * (.48f + ring * .16f) * breathe),
                (int)(center.Y + Size * (.22f - ring * .025f)), (int)(Size * (.96f + ring * .32f) * breathe),
                (int)(Size * (.18f + ring * .055f)));
            Primitives2D.EllipseOutline(spriteBatch, ripple, new Color(104, 121, 55) * (.34f + ring * .08f), 2);
        }
        if (ReliefPulseRemaining > 0)
        {
            float relief = (float)(1.0 - ReliefPulseRemaining / 1.2);
            for (int ring = 0; ring < 3; ring++)
            {
                float radius = Size * (.62f + relief * 1.25f + ring * .18f);
                Primitives2D.CircleOutline(spriteBatch, center, radius,
                    new Color(214, 196, 121) * (float)(ReliefPulseRemaining / 1.2),
                    Math.Max(2, 5 - ring), 40);
            }
        }

        int cubes = FinaleActive ? FinaleAbsorptionParticleCount : AbsorptionParticleCount;
        for (int index = 0; index < cubes; index++)
        {
            float fall = (Age * (.0042f + index % 4 * .00035f) + index * .137f) % 1f;
            float x = MathF.Sin(index * 4.17f + Age * .0012f) * Size * (.22f + index % 5 * .075f);
            var point = center + new Vector2(x, -Size * .92f + fall * Size * 1.18f + sink);
            float cube = Size * (.055f + (index % 3) * .018f) * (1f - fall * .32f);
            Color particle = (index % 3) switch
            {
                0 => new Color(116, 79, 43),
                1 => new Color(126, 55, 39),
                _ => new Color(92, 126, 55),
            };
            BossVisuals.Cube(spriteBatch, point, cube, particle, PhaseAccent, index * .7f + Age * .002f);
        }

        Vector2 slabCenter = center + new Vector2(0, sink + Size * .1f);
        BossVisuals.Cuboid(spriteBatch, slabCenter, Size * 1.18f, Size * .8f,
            new Color(82, 59, 39), new Color(113, 143, 61), Age * .0008f);
        for (int crack = 0; crack < 5; crack++)
        {
            float x = slabCenter.X + Size * (-.42f + crack * .21f);
            var points = new[]
            {
                new Vector2(x, slabCenter.Y - Size * .25f),
                new Vector2(x + MathF.Sin(crack * 2.1f) * Size * .055f, slabCenter.Y - Size * .03f),
                new Vector2(x - MathF.Cos(crack * 1.7f) * Size * .04f, slabCenter.Y + Size * .13f),
            };
            Primitives2D.Polyline(spriteBatch, points, false, crack % 2 == 0 ? new Color(105, 132, 58) : new Color(128, 61, 39), 3);
        }

        // The foreground pool hides the slab's lower half and visibly absorbs
        // every falling cube, which sells Rot's weight better than a floating body.
        var poolFront = new Rectangle((int)(center.X - Size * .78f), (int)(center.Y + Size * .24f),
            (int)(Size * 1.56f), (int)(Size * .42f));
        Primitives2D.FillEllipse(spriteBatch, poolFront, new Color(74, 67, 36));
        Primitives2D.EllipseOutline(spriteBatch, poolFront, new Color(112, 132, 57), 3);
        DrawBossHealth(spriteBatch, new Rectangle((int)(center.X - Size * .46f), (int)(center.Y - Size * .7f), (int)(Size * .92f), 6));
    }

    private void DrawBurialStrata(SpriteBatch spriteBatch, Camera camera,
        Vector2 playerWorldPosition, Vector2 screenShake)
    {
        if (!FinaleActive)
            return;

        Vector2 arena = camera.WorldToScreen(ArenaCenter, playerWorldPosition, screenShake);
        float progress = (float)FinaleProgress;
        int layers = BurialLayerCount;
        for (int layer = 0; layer < layers; layer++)
        {
            float delay = layer * .11f;
            float collapse = Math.Clamp((progress - delay) / Math.Max(.01f, 1f - delay), 0f, 1f);
            float extent = ArenaRadius * (1f - collapse * .68f + layer * .025f);
            var stratum = new Rectangle(
                (int)(arena.X - extent), (int)(arena.Y - extent),
                (int)(extent * 2f), (int)(extent * 2f));
            Color layerColor = layer % 2 == 0
                ? new Color(112, 132, 57)
                : new Color(143, 80, 47);
            Primitives2D.RectOutline(spriteBatch, stratum,
                UiTheme.Ink * (.28f + collapse * .24f), 9);
            Primitives2D.RectOutline(spriteBatch, stratum,
                layerColor * (.34f + collapse * .36f), 4);

            float crack = Math.Max(18f, extent * .13f);
            foreach (var corner in new[]
                     {
                         new Vector2(stratum.Left, stratum.Top),
                         new Vector2(stratum.Right, stratum.Top),
                         new Vector2(stratum.Right, stratum.Bottom),
                         new Vector2(stratum.Left, stratum.Bottom),
                     })
            {
                Vector2 toward = Vector2.Normalize(arena - corner);
                Primitives2D.Line(spriteBatch, corner,
                    corner + toward * crack, layerColor * .52f, 3);
            }
        }

        // A curtain of absorbed cubes falls through the contracting strata.
        // It is intentionally translucent and behind the slab so current
        // ground telegraphs retain visual priority.
        for (int index = 0; index < 18; index++)
        {
            float fall = (Age * (.0034f + index % 4 * .0003f) + index * .091f) % 1f;
            float x = arena.X + MathF.Sin(index * 3.71f) * ArenaRadius * .82f;
            float y = arena.Y - ArenaRadius + fall * ArenaRadius * 1.72f;
            float cube = Simulation.TileSize * (.22f + index % 3 * .055f);
            Color color = index % 2 == 0
                ? new Color(112, 132, 57) * .58f
                : new Color(143, 80, 47) * .55f;
            BossVisuals.Cube(spriteBatch, new Vector2(x, y), cube, color,
                Color.Lerp(color, UiTheme.Cream, .22f), index * .41f + Age * .001f);
        }
    }
}
