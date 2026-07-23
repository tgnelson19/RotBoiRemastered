using Microsoft.Xna.Framework;
using RotBoiRemastered.Core;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// "THE FIRST REACTION" -- Chemesthesis's midpoint lesson. Kage pairs
/// appetites into readable composites, then suspends damage during Stagnant
/// Mirror so the player must survive the field they allowed to accumulate.
/// </summary>
public class Kage : SinChemesthesisBoss
{
    public const int MinimumKageDamagePhaseDeclarations = 2;
    public const double StagnantMirrorDuration = 14.0;
    public const int KageActiveThreatSoftCap = 36;
    // Rot (bossTypes.py's Rot(Kage)) inherits Kage's shared _fire_pattern
    // building blocks but supplies its own config/sin-sigil content, so the
    // one public constructor below can't be reused as-is -- this protected
    // overload lets a subclass pass its own PathChaseBossConfig/SinSigilConfig
    // through the same chain.
    protected Kage(float worldX, float worldY, Battleground battleground,
        PathChaseBossConfig config, SinSigilConfig sinConfig, Random? rng = null)
        : base(worldX, worldY, battleground, config, sinConfig, rng)
    {
    }

    public static readonly PathChaseBossConfig KageConfig = BaseConfig with
    {
        BossName = "KAGE", Subtitle = "THE FIRST REACTION",
        PhaseLabels = new[] { "FEAST", "PROVOCATION", "STAGNANT MIRROR", "LURE" },
        Pattern = "minefield", OwnerPrefix = "kage_chemesthesis",
        BodyColor = new Color(169, 65, 36), AccentColor = new Color(106, 132, 52),
        MovementSpeed = .055, BodyScale = 2.05, CooldownSeconds = 1.8,
        ShotSpeed = .30, ShotScale = .26, ShotRangeTiles = 34,
        MovementModes = new[] { "chase", "path", "static", "path" },
        MidHealth = 93000, MidContactDamage = 340, MidRewardExperience = 390,
    };

    public static readonly SinSigilConfig KageSinConfig = new(
        PhaseFlavors: new[]
        {
            "Take all that you can carry.", "Strike. I insist.",
            "Stillness learns your shape.", "Come closer. There is plenty.",
        },
        PhaseColors: new[]
        {
            new Color(214, 154, 52), new Color(205, 62, 38),
            new Color(101, 133, 64), new Color(202, 82, 99),
        },
        SinSigils: new (string, Vector2[][])[]
        {
            ("HUNGER / WANT", new[]
            {
                new[]
                {
                    new Vector2(-.72f, -.25f), new Vector2(-.28f, -.72f), new Vector2(.28f, -.72f), new Vector2(.72f, -.25f),
                    new Vector2(.28f, .18f), new Vector2(-.28f, .18f), new Vector2(-.72f, -.25f),
                },
                new[] { new Vector2(0, -.72f), new Vector2(0, .72f) },
                new[] { new Vector2(-.42f, .42f), new Vector2(0, .72f), new Vector2(.42f, .42f) },
            }),
            ("CROWN / RETORT", new[]
            {
                new[]
                {
                    new Vector2(-.7f, .38f), new Vector2(-.52f, -.5f), new Vector2(0, -.12f),
                    new Vector2(.52f, -.5f), new Vector2(.7f, .38f),
                },
                new[] { new Vector2(-.52f, .08f), new Vector2(.52f, .08f) },
                new[] { new Vector2(-.34f, .68f), new Vector2(0, .2f), new Vector2(.34f, .68f) },
            }),
            ("MIRROR / STILLNESS", new[]
            {
                new[]
                {
                    new Vector2(-.68f, -.35f), new Vector2(-.2f, -.68f), new Vector2(-.2f, .5f),
                    new Vector2(-.68f, .18f), new Vector2(-.68f, -.35f),
                },
                new[]
                {
                    new Vector2(.68f, -.35f), new Vector2(.2f, -.68f), new Vector2(.2f, .5f),
                    new Vector2(.68f, .18f), new Vector2(.68f, -.35f),
                },
                new[] { new Vector2(-.2f, .5f), new Vector2(0, .72f), new Vector2(.2f, .5f) },
            }),
            ("LURE / AVARICE", new[]
            {
                new[] { new Vector2(-.68f, -.38f), new Vector2(0, .08f), new Vector2(.68f, -.38f) },
                new[] { new Vector2(-.68f, .38f), new Vector2(0, -.08f), new Vector2(.68f, .38f) },
                new[] { new Vector2(0, -.72f), new Vector2(0, .72f) },
                new[] { new Vector2(-.22f, .48f), new Vector2(0, .72f), new Vector2(.22f, .48f) },
            }),
        },
        ActMetadata: new Dictionary<int, string>());

    public Kage(float worldX, float worldY, Battleground battleground, Random? rng = null)
        : base(worldX, worldY, battleground, KageConfig, KageSinConfig, rng)
    {
    }

    private int _phaseDeclarations;
    protected virtual bool UsesKageEncounter => true;
    public bool StagnantMirrorActive { get; private set; }
    public bool StagnantMirrorCleared { get; private set; }
    public double StagnantMirrorRemaining { get; private set; }
    public int KagePhaseDeclarations => _phaseDeclarations;
    protected override bool VisualSurvivalActive =>
        UsesKageEncounter && StagnantMirrorActive || base.VisualSurvivalActive;

    protected override double DamageFloorRatio()
    {
        if (!UsesKageEncounter)
            return base.DamageFloorRatio();
        return Phase switch { 1 => .75, 2 or 3 => .50, _ => 0.0 };
    }

    private void BeginStagnantMirror()
    {
        if (StagnantMirrorActive || StagnantMirrorCleared)
            return;
        Hp = Math.Max(1, MaxHp / 2);
        SetSinPhase(3);
    }

    protected override void SetSinPhase(int phase)
    {
        base.SetSinPhase(phase);
        if (!UsesKageEncounter)
            return;

        _phaseDeclarations = 0;
        StagnantMirrorActive = Phase == 3;
        if (Phase >= 4)
            StagnantMirrorCleared = true;
        if (StagnantMirrorActive)
            StagnantMirrorRemaining = StagnantMirrorDuration;
    }

    protected override void UpdatePhase()
    {
        if (!UsesKageEncounter)
        {
            base.UpdatePhase();
            return;
        }
        if (StagnantMirrorActive)
        {
            if (!DebugPhaseLocked)
            {
                StagnantMirrorRemaining = Math.Max(
                    0.0, StagnantMirrorRemaining - Seconds());
                if (StagnantMirrorRemaining <= 0)
                {
                    StagnantMirrorActive = false;
                    StagnantMirrorCleared = true;
                    Hp = Math.Max(1, MaxHp / 2);
                    SetSinPhase(4);
                }
            }
            return;
        }
        if (DebugPhaseLocked || Dying)
            return;

        double ratio = Math.Clamp((double)Hp / MaxHp, 0.0, 1.0);
        if (!StagnantMirrorCleared)
        {
            if (ratio <= .5)
            {
                if (_phaseDeclarations >= MinimumKageDamagePhaseDeclarations)
                    BeginStagnantMirror();
                return;
            }
            int desired = ratio > .75 ? 1 : 2;
            if (desired != Phase &&
                _phaseDeclarations >= MinimumKageDamagePhaseDeclarations)
                SetSinPhase(desired);
            return;
        }
        if (Phase != 4)
            SetSinPhase(4);
    }

    public override HitResult TakeDamage(double amount, string partId = "body",
        DamageSource source = DamageSource.Direct)
    {
        if (!UsesKageEncounter)
            return base.TakeDamage(amount, partId, source);
        if (StagnantMirrorActive || Dying)
            return new HitResult(false, false, 0, true);

        if (!StagnantMirrorCleared)
        {
            double floorRatio = Phase == 1 ? .75 : .50;
            int floor = Math.Max(1, (int)Math.Round(MaxHp * floorRatio));
            double permitted = Math.Max(0, Hp - floor);
            if (permitted <= 0)
            {
                if (_phaseDeclarations >= MinimumKageDamagePhaseDeclarations)
                {
                    if (Phase == 1)
                        SetSinPhase(2);
                    else
                        BeginStagnantMirror();
                }
                return new HitResult(false, false, 0, true);
            }
            var result = base.TakeDamage(Math.Min(amount, permitted), partId, source);
            if (Hp <= floor && _phaseDeclarations >= MinimumKageDamagePhaseDeclarations)
            {
                if (Phase == 1)
                    SetSinPhase(2);
                else
                    BeginStagnantMirror();
            }
            return new HitResult(result.Applied, false, result.Amount, result.Blocked);
        }

        if (Phase == 4 && _phaseDeclarations < MinimumKageDamagePhaseDeclarations)
        {
            double permitted = Math.Max(0, Hp - 1);
            if (permitted <= 0)
                return new HitResult(false, false, 0, true);
            var gated = base.TakeDamage(Math.Min(amount, permitted), partId, source);
            return new HitResult(gated.Applied, false, gated.Amount, gated.Blocked);
        }
        return base.TakeDamage(amount, partId, source);
    }

    protected override void FireSinPattern(float playerX, float playerY, EnemyUpdateContext context)
    {
        var center = Center();
        float aimed = MathF.Atan2(playerY - center.Y, playerX - center.X);
        var sink = context.ProjectileSink;
        int activeThreats = sink.Count(projectile =>
            !projectile.RemFlag &&
            projectile.Owner?.StartsWith("kage_chemesthesis", StringComparison.Ordinal) == true);
        if (activeThreats >= KageActiveThreatSoftCap)
        {
            PatternRotation++;
            MarkAttack(.3f);
            return;
        }

        int rotationBefore = PatternRotation;
        switch (Phase)
        {
            case 1: // Gluttony / Greed: a feast of lingering morsels.
                Radial(sink, 5, .34f, 245, "feast", mine: true);
                var claim = Shot(sink, aimed, .46f, 255, scale: .28f,
                    shape: "mine", path: "mine", lifetime: 10f,
                    speedDecay: .04f, ownerSuffix: "feast_claim",
                    affliction: "slow", afflictionDuration: 1.0,
                    afflictionStrength: .08, exposure: .35);
                claim.TelegraphDuration = .85f;
                break;
            case 2: // Wrath / Pride: invitation followed by retaliation.
                KageFan(sink, aimed, 5, 1.05f, .82f, 270,
                    "provocation", 7.0f);
                Laser(sink, aimed + MathF.PI, 240, "retort");
                break;
            case 3: // Sloth / Envy: slow mirrors occupy the field.
                foreach (int side in new[] { -1, 1 })
                    Shot(sink, aimed + side * .72f, .42f, 250,
                        path: "sine", lifetime: 8.5f,
                        ownerSuffix: "stagnant_mirror");
                var mirrorClaim = Shot(sink, aimed, .39f, 250, scale: .24f,
                    path: "sine", lifetime: 8.5f,
                    ownerSuffix: "mirror_claim");
                mirrorClaim.TelegraphDuration = .8f;
                Radial(sink, 4, .18f, 230, "stagnation", mine: true);
                break;
            default: // Lust / Avarice: converging lanes make tempting gaps.
                KageFan(sink, aimed, 7, 2.2f, .56f, 265,
                    "lure", 6.5f);
                var reward = Bomb(sink, playerX, playerY, 280,
                    "lure_reward", burstCount: 4, fuseDuration: 2.8f,
                    burstShotDamage: 170);
                reward.BurstRangeTiles = 4.5f;
                break;
        }
        if (PatternRotation == rotationBefore)
            PatternRotation++;
        _phaseDeclarations++;
        MarkAttack(.5f);
    }

    private void KageFan(List<EnemyProjectile> sink, float baseDirection,
        int count, float spread, float speed, float damage, string suffix,
        float lifetime)
    {
        for (int index = 0; index < count; index++)
        {
            float offset = count == 1
                ? 0f
                : -spread / 2f + spread * index / (count - 1);
            Shot(sink, baseDirection + offset, speed, damage,
                lifetime: lifetime, ownerSuffix: suffix);
        }
    }
}
