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
    private static readonly IReadOnlyDictionary<int, (string Label, string Flavor, Color Accent)> PhaseMetadata =
        new Dictionary<int, (string, string, Color)>
        {
            [1] = ("SEEP", "Rot does not advance. The ground carries its touch outward.", new Color(119, 137, 64)),
            [2] = ("SILT", "A patient weight settles everywhere except one bank.", new Color(145, 112, 57)),
            [3] = ("SLUMP", "The ancient sinks by an inch. The whole floor answers.", new Color(117, 86, 52)),
            [4] = ("CHOKING STILLNESS", "Placidity is not weakness. Find the clean edge.", new Color(192, 176, 112)),
            [5] = ("BLOOM", "Discarded matter returns to the guardian that bore it.", new Color(108, 151, 66)),
            [6] = ("MIASMA", "Immense power commands the room without giving chase.", new Color(145, 82, 54)),
            [7] = ("BURIAL", "Forty seconds beneath the burden the others feared to move.", new Color(177, 104, 50)),
        };

    public static readonly PathChaseBossConfig RotConfig = PathChaseBossConfig.Default with
    {
        BossName = "ROT", Subtitle = "THE BURIED ANCIENT", FinalBoss = true,
        OwnerPrefix = "rot_touch", Pattern = "boulder",
        PhaseLabels = PhaseMetadata.OrderBy(pair => pair.Key).Select(pair => pair.Value.Label).ToArray(),
        FinalBodyColor = new Color(91, 65, 42), FinalAccentColor = new Color(154, 118, 55),
        FinalBodyScale = 2.85, FinalCooldownSeconds = 2.35,
        FinalShotSpeed = .30, FinalShotDamage = 590, FinalShotScale = .32,
        MovementSpeed = .045, ArenaShape = "square", ArenaScale = 10.2,
        MovementModes = new[] { "static", "path", "static", "static", "path", "static", "static" },
        FinalHealth = 330000, FinalContactDamage = 980, FinalRewardExperience = 900,
    };

    public bool MidpointSurvivalActive { get; private set; }
    public bool MidpointSurvivalCleared { get; private set; }
    public double MidpointSurvivalDuration { get; } = 22.0;
    public double MidpointSurvivalRemaining { get; private set; }
    public int PatternRotation { get; private set; }
    public float SafeCorridorAngle { get; private set; }
    public int SludgePoolsCreated { get; private set; }
    private double _survivalCooldown;

    protected override bool VisualSurvivalActive => MidpointSurvivalActive || FinaleActive || base.VisualSurvivalActive;

    public Rot(float worldX, float worldY, Battleground battleground, Random? rng = null)
        : base(worldX, worldY, battleground, RotConfig, rng)
    {
        ApplyPhase(1);
    }

    private void ApplyPhase(int phase)
    {
        Phase = Math.Clamp(phase, 1, PhaseMetadata.Count);
        (PhaseLabel, PhaseFlavor, PhaseAccent) = PhaseMetadata[Phase];
        PhaseElapsed = 0.0;
        VisualTransitionRemaining = 1.5;
        AttackCooldown = Math.Min(AttackCooldown ?? 0f, Simulation.FrameRate * .7f);
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
                BeginMidpointSurvival();
                return;
            }
            desired = ratio > .84 ? 1 : ratio > .67 ? 2 : 3;
        }
        else
        {
            desired = ratio > .25 ? 5 : 6;
        }
        if (desired != Phase)
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

        double floorRatio = !MidpointSurvivalCleared
            ? Phase switch { 1 => .84, 2 => .67, _ => .50 }
            : Phase == 5 ? .25 : 0.0;
        int floor = Math.Max(0, (int)Math.Round(MaxHp * floorRatio));
        double permitted = floor > 0 ? Math.Max(0, Hp - floor) : amount;
        if (floor > 0 && permitted <= 0)
        {
            UpdatePhase();
            return new HitResult(false, false, 0, true);
        }

        var result = base.TakeDamage(floor > 0 ? Math.Min(amount, permitted) : amount, partId, source);
        if (!MidpointSurvivalCleared && Hp <= MaxHp * .5)
            BeginMidpointSurvival();
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
            BurstCount = 12,
        });
    }

    private void SlowFront(List<EnemyProjectile> sink, float direction, int lanes, float gapAngle, string suffix)
    {
        var center = Center();
        float step = MathF.Tau / lanes;
        int gap = (int)MathF.Round(((gapAngle % MathF.Tau + MathF.Tau) % MathF.Tau) / step) % lanes;
        for (int index = 0; index < lanes; index++)
        {
            int distance = Math.Min((index - gap + lanes) % lanes, (gap - index + lanes) % lanes);
            if (distance <= 1)
                continue;
            float angle = direction + index * step;
            float size = Size * .26f;
            sink.Add(new EnemyProjectile(center.X - size / 2f, center.Y - size / 2f, angle,
                .22f + (index % 3) * .035f, 540, size,
                travelRange: ArenaRadius * 1.2f, color: PhaseAccent, shape: "diamond", path: "linear",
                owner: $"rot_touch_{suffix}", ignoreWalls: true)
            {
                Affliction = "slow",
                AfflictionDuration = 1.3,
                AfflictionStrength = .12,
                Exposure = .55,
            });
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
        SafeCorridorAngle = MathF.Atan2(playerY - ArenaCenter.Y, playerX - ArenaCenter.X);
        switch (Phase)
        {
            case 1:
                SeedInnerPools(sink, SafeCorridorAngle, 7, .34f);
                break;
            case 2:
                SlowFront(sink, PatternRotation * .16f, 14, SafeCorridorAngle, "silt_front");
                break;
            case 3:
                SeedInnerPools(sink, SafeCorridorAngle, 9);
                for (int index = -1; index <= 1; index++)
                {
                    float angle = SafeCorridorAngle + MathF.PI + index * .55f;
                    RotBomb(sink, ArenaCenter + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * ArenaRadius * .52f);
                }
                break;
            case 5:
                SeedInnerPools(sink, SafeCorridorAngle, 10, .5f);
                SlowFront(sink, PatternRotation * .11f, 12, SafeCorridorAngle, "bloom_spores");
                break;
            case 6:
                SlowFront(sink, PatternRotation * .18f, 16, SafeCorridorAngle, "miasma_front");
                RotBomb(sink, ArenaCenter - new Vector2(MathF.Cos(SafeCorridorAngle), MathF.Sin(SafeCorridorAngle)) * ArenaRadius * .38f, 680);
                break;
            default:
                if (PatternRotation % 3 == 0)
                    SeedInnerPools(sink, SafeCorridorAngle, 12, .5f);
                else if (PatternRotation % 3 == 1)
                    SlowFront(sink, PatternRotation * .14f, 18, SafeCorridorAngle, "burial_front");
                else
                {
                    for (int index = 0; index < 4; index++)
                    {
                        float angle = SafeCorridorAngle + MathF.PI + (index - 1.5f) * .42f;
                        RotBomb(sink, ArenaCenter + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * ArenaRadius * (.28f + index * .09f), 700);
                    }
                }
                break;
        }
        PatternRotation++;
        MarkAttack(.7f);
    }

    public override void Update(EnemyUpdateContext context)
    {
        if (!MidpointSurvivalActive)
        {
            base.Update(context);
            return;
        }

        double dt = Seconds();
        EntranceRemaining = Math.Max(0.0, EntranceRemaining - dt);
        VisualTransitionRemaining = Math.Max(0.0, VisualTransitionRemaining - dt);
        PhaseElapsed += dt;
        AdvanceAge();
        MidpointSurvivalRemaining = Math.Max(0.0, MidpointSurvivalRemaining - dt);
        _survivalCooldown -= dt;
        if (EntranceRemaining <= 0 && _survivalCooldown <= 0)
        {
            SafeCorridorAngle = MathF.Atan2(context.PlayerWorldY - ArenaCenter.Y, context.PlayerWorldX - ArenaCenter.X);
            if (PatternRotation % 2 == 0)
                SeedInnerPools(context.ProjectileSink, SafeCorridorAngle, 11, .52f);
            else
                SlowFront(context.ProjectileSink, PatternRotation * .12f, 16, SafeCorridorAngle, "stillness_front");
            PatternRotation++;
            _survivalCooldown = 2.75;
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
}
