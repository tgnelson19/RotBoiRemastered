using Microsoft.Xna.Framework;
using RotBoiRemastered.Core;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// "THE FIRST LOCK" -- Touch's midpoint lesson. Bair teaches that slow,
/// completely declared weight can be more dangerous than fast bullets:
/// currents establish a lane, marching gates narrow it, blight marks where
/// the player stood, and Ruin asks them to sustain all three lessons.
/// </summary>
public sealed class Bair : PlagueTouchBoss
{
    public const int MinimumDamagePhaseDeclarations = 2;
    public const double RuinDuration = 14.0;

    public static readonly PathChaseBossConfig BairConfig = BaseConfig with
    {
        BossName = "BAIR", Subtitle = "THE FIRST LOCK", OwnerPrefix = "bair_touch",
        PhaseLabels = new[] { "RIVER", "SWARM", "BLIGHT", "RUIN", "SILENCE" },
        MovementModes = new[] { "chase", "path", "static", "static", "path" },
        MovementSpeed = .10, BodyScale = 2.15, CooldownSeconds = 2.05,
        ShotSpeed = .46, ShotDamage = 300, ShotScale = .28, ShotRangeTiles = 22,
        MidHealth = 110000, MidContactDamage = 380, MidRewardExperience = 420,
    };

    public static readonly PlagueSigilConfig BairSigilConfig = new(
        PhaseFlavors: new[]
        {
            "The current carries judgment.", "The small become countless.",
            "The body and field fail together.", "Stone descends; hunger follows.",
            "What remains cannot answer.",
        },
        PhaseColors: new[]
        {
            new Color(137, 48, 45), new Color(76, 135, 80), new Color(126, 104, 61),
            new Color(151, 123, 94), new Color(54, 57, 71),
        },
        PhaseSigils: new[] { 0, 2, 4, 6, 8 });

    private int _phaseDeclarations;

    public bool RuinSurvivalActive { get; private set; }
    public bool RuinSurvivalCleared { get; private set; }
    public double RuinSurvivalRemaining { get; private set; }
    public int PhaseDeclarations => _phaseDeclarations;

    protected override bool VisualSurvivalActive => RuinSurvivalActive || base.VisualSurvivalActive;
    protected override double PortalFireCadence => 1.30;
    protected override double PortalWarningDuration => .55;
    protected override float? PortalProjectileLifetime => 7.0f;
    protected override float? PortalProjectileRange => ArenaRadius * 2.4f;
    protected override float PortalShotDamage => 300f;
    protected override float PortalShotSpeed => .40f;

    public Bair(float worldX, float worldY, Battleground battleground, Random? rng = null)
        : base(worldX, worldY, battleground, BairConfig, BairSigilConfig, rng)
    {
    }

    private void BeginRuinSurvival()
    {
        if (RuinSurvivalActive || RuinSurvivalCleared)
            return;
        Hp = Math.Max(1, (int)Math.Round(MaxHp * .5));
        SetPlaguePhase(4);
    }

    protected override void SetPlaguePhase(int phase)
    {
        base.SetPlaguePhase(phase);
        _phaseDeclarations = 0;
        RuinSurvivalActive = Phase == 4;
        if (Phase >= 5)
            RuinSurvivalCleared = true;
        if (RuinSurvivalActive)
        {
            RuinSurvivalRemaining = RuinDuration;
            PortalCooldown = .25;
        }
    }

    protected override void UpdatePhase()
    {
        if (RuinSurvivalActive)
        {
            if (!DebugPhaseLocked)
            {
                RuinSurvivalRemaining = Math.Max(0.0, RuinSurvivalRemaining - Seconds());
                if (RuinSurvivalRemaining <= 0)
                {
                    RuinSurvivalActive = false;
                    RuinSurvivalCleared = true;
                    Hp = Math.Max(1, (int)Math.Round(MaxHp * .5));
                    SetPlaguePhase(5);
                }
            }
            return;
        }
        if (DebugPhaseLocked || Dying)
            return;

        double ratio = Math.Clamp((double)Hp / MaxHp, 0.0, 1.0);
        if (!RuinSurvivalCleared)
        {
            if (ratio <= .5)
            {
                if (_phaseDeclarations >= MinimumDamagePhaseDeclarations)
                    BeginRuinSurvival();
                return;
            }

            int desired = ratio > .82 ? 1 : ratio > .66 ? 2 : 3;
            if (desired != Phase && _phaseDeclarations >= MinimumDamagePhaseDeclarations)
                SetPlaguePhase(desired);
            return;
        }

        if (Phase != 5)
            SetPlaguePhase(5);
    }

    public override HitResult TakeDamage(double amount, string partId = "body",
        DamageSource source = DamageSource.Direct)
    {
        if (partId.StartsWith("portal:", StringComparison.Ordinal))
            return base.TakeDamage(amount, partId, source);
        if (RuinSurvivalActive || Dying)
            return new HitResult(false, false, 0, true);

        if (!RuinSurvivalCleared)
        {
            double floorRatio = Phase switch { 1 => .82, 2 => .66, _ => .50 };
            int floor = Math.Max(1, (int)Math.Round(MaxHp * floorRatio));
            double permitted = Math.Max(0, Hp - floor);
            if (permitted <= 0)
            {
                if (_phaseDeclarations >= MinimumDamagePhaseDeclarations)
                {
                    if (Phase >= 3)
                        BeginRuinSurvival();
                    else
                        SetPlaguePhase(Phase + 1);
                }
                return new HitResult(false, false, 0, true);
            }

            var result = ApplyPlagueBodyDamage(Math.Min(amount, permitted), partId, source);
            if (Hp <= floor && _phaseDeclarations >= MinimumDamagePhaseDeclarations)
            {
                if (Phase >= 3)
                    BeginRuinSurvival();
                else
                    SetPlaguePhase(Phase + 1);
            }
            return new HitResult(result.Applied, false, result.Amount, result.Blocked);
        }

        if (Phase == 5 && _phaseDeclarations < MinimumDamagePhaseDeclarations)
        {
            double permitted = Math.Max(0, Hp - 1);
            if (permitted <= 0)
                return new HitResult(false, false, 0, true);
            var gated = ApplyPlagueBodyDamage(Math.Min(amount, permitted), partId, source);
            return new HitResult(gated.Applied, false, gated.Amount, gated.Blocked);
        }
        return ApplyPlagueBodyDamage(amount, partId, source);
    }

    private EnemyProjectile DeclaredShot(List<EnemyProjectile> sink, float direction,
        float speed, float damage, string suffix, float scale = .25f, float warning = .68f)
    {
        var shot = Projectile(sink, direction, speed, damage, suffix, scale, "bank");
        shot.TelegraphDuration = warning;
        shot.Lifetime = 7.5f;
        shot.RemainingRange = Math.Min(shot.RemainingRange, ArenaRadius * 2.4f);
        return shot;
    }

    private void DeclaredFan(List<EnemyProjectile> sink, float playerX, float playerY,
        int count, float spread, float speed, float damage, string suffix, float warning)
    {
        var center = Center();
        float aimed = MathF.Atan2(playerY - center.Y, playerX - center.X);
        for (int index = 0; index < count; index++)
        {
            float offset = count == 1 ? 0f : -spread / 2f + spread * index / (count - 1);
            DeclaredShot(sink, aimed + offset, speed, damage, suffix, .27f, warning);
        }
    }

    private void DeclaredRadialWithOpening(List<EnemyProjectile> sink, float playerX,
        float playerY, int count, float speed, float damage, string suffix, float warning)
    {
        var center = Center();
        float opening = MathF.Atan2(playerY - center.Y, playerX - center.X);
        float rotation = PatternRotation * .17f;
        for (int index = 0; index < count; index++)
        {
            float direction = rotation + index * MathF.Tau / count;
            float difference = MathF.Abs(MathF.Atan2(
                MathF.Sin(direction - opening), MathF.Cos(direction - opening)));
            if (difference < .48f)
                continue;
            DeclaredShot(sink, direction, speed, damage, suffix, .25f, warning);
        }
    }

    private void DeclaredBomb(List<EnemyProjectile> sink, Vector2 target, string suffix,
        float damage = 330f)
    {
        var bomb = Projectile(sink, 0, 0, damage, suffix, .34f, "bomb", target);
        bomb.TelegraphDuration = .95f;
        bomb.FuseDuration = 2.8f;
        bomb.BlastRadius = Simulation.TileSize * 1.8f;
        bomb.BurstCount = 6;
        bomb.BurstRangeTiles = 8f;
    }

    protected override void FirePlaguePattern(float playerX, float playerY,
        List<EnemyProjectile> sink)
    {
        switch (Phase)
        {
            case 1:
                // Two slow currents frame one readable dodge instead of
                // pretending difficulty comes from a dense fan. The second
                // pair advances half a lane so the first opening cannot be
                // camped indefinitely.
            {
                var center = Center();
                float aimed = MathF.Atan2(playerY - center.Y, playerX - center.X);
                float advance = PatternRotation % 2 == 0 ? 0f : .19f;
                DeclaredShot(sink, aimed - .19f + advance, .48f, 300,
                    "river_current", .27f, .72f);
                DeclaredShot(sink, aimed + .19f + advance, .48f, 300,
                    "river_current", .27f, .72f);
                break;
            }
            case 2:
                // The gates supply aimed pressure while the body leaves a
                // complete player-facing opening in the rotating swarm.
                DeclaredRadialWithOpening(sink, playerX, playerY, 9, .38f, 285,
                    "swarm_ring", .72f);
                break;
            case 3:
            {
                var player = new Vector2(playerX, playerY);
                var perpendicular = new Vector2(
                    -(playerY - ArenaCenter.Y), playerX - ArenaCenter.X);
                if (perpendicular.LengthSquared() > .001f)
                    perpendicular.Normalize();
                DeclaredBomb(sink, player, "blight_mark");
                DeclaredBomb(sink, player + perpendicular * Simulation.TileSize * 2.15f,
                    "blight_echo", 310);
                DeclaredBomb(sink, player - perpendicular * Simulation.TileSize * 2.15f,
                    "blight_echo", 310);
                break;
            }
            case 4:
                DeclaredRadialWithOpening(sink, playerX, playerY, 10, .36f, 310,
                    "ruin_fall", .88f);
                if (PatternRotation % 2 == 1)
                    DeclaredBomb(sink, new Vector2(playerX, playerY), "ruin_weight", 350);
                break;
            default:
                // Silence is the synthesis: the River pair fixes attention,
                // then alternating Swarm/Blight answers make holding still fail.
                DeclaredFan(sink, playerX, playerY, 3, .42f, .46f, 330,
                    "silence_current", .65f);
                if (PatternRotation % 2 == 0)
                    DeclaredRadialWithOpening(sink, playerX, playerY, 10, .39f, 315,
                        "silence_swarm", .76f);
                else
                    DeclaredBomb(sink, new Vector2(playerX, playerY), "silence_mark", 370);
                break;
        }
        PatternRotation++;
        _phaseDeclarations++;
    }
}
