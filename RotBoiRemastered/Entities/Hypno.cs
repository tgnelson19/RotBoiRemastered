using Microsoft.Xna.Framework;
using RotBoiRemastered.Core;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// "THE ORNATE SUGGESTION" -- a mid-boss establishing the Phantasia family's
/// illusion/truth pattern language. Ported from bossTypes.py's Hypno.
///
/// Python's `shotRangeTiles = 28` class attribute is dropped: confirmed
/// dead by reading every method on `PhantasiaBoss`/`Hypno` --
/// `ShotFrom`/`FanFrom`/`LaserFrom` all hardcode their own travel range
/// (34/35 tiles) rather than reading `self.shotRangeTiles`, unlike plain
/// `PathChaseBoss._fire_pattern` (which `PhantasiaBoss` never calls, having
/// its own `_fire_pattern` override).
/// </summary>
public sealed class Hypno : PhantasiaBoss
{
    public const int MinimumDamagePhaseDeclarations = 2;
    public const int ActiveThreatSoftCap = 48;
    public const double ChosenSurvivalDuration = 14.0;
    protected override bool UsesSharedDreamHealthGates => false;
    protected override bool VisualSurvivalActive =>
        ChosenSurvivalActive || base.VisualSurvivalActive;

    public static readonly PathChaseBossConfig HypnoConfig = BaseConfig with
    {
        BossName = "HYPNO", Subtitle = "THE ORNATE SUGGESTION",
        PhaseLabels = new[] { "IDOL", "SPOKEN RULE", "INHERITANCE", "CHOSEN", "OFFERING" },
        OwnerPrefix = "hypno_phantasia",
        BodyColor = new Color(151, 56, 144), AccentColor = new Color(211, 91, 183),
        MovementSpeed = .18, BodyScale = 1.8, CooldownSeconds = 1.8,
        MidHealth = 107000, MidContactDamage = 360, MidRewardExperience = 410,
    };

    public static readonly PhantasiaSigilConfig HypnoSigilConfig = new(
        PhaseFlavors: new[]
        {
            "Surely you recognize the one before you.", "A command is true because it is spoken.",
            "What sleeps in one generation wakes in the next.", "You chose. Do not pretend otherwise.",
            "Everything offered was already mine.",
        },
        PhaseColors: new[]
        {
            new Color(214, 89, 188), new Color(111, 164, 224), new Color(227, 180, 75),
            new Color(126, 205, 159), new Color(211, 105, 115),
        },
        PhaseSigils: new[] { 0, 2, 4, 6, 9 },
        ActMetadata: new Dictionary<int, string>());

    public Hypno(float worldX, float worldY, Battleground battleground, Random? rng = null)
        : base(worldX, worldY, battleground, HypnoConfig, HypnoSigilConfig, rng)
    {
    }

    public int PhaseDeclarations { get; private set; }
    public bool ChosenSurvivalActive { get; private set; }
    public bool ChosenSurvivalCleared { get; private set; }
    public double ChosenSurvivalRemaining { get; private set; }

    private int ActiveHypnoThreatBurden(List<EnemyProjectile> sink) =>
        sink.Where(projectile => !projectile.RemFlag &&
                projectile.Owner?.StartsWith(HypnoConfig.OwnerPrefix) == true)
            .Sum(projectile => projectile.SplitCount > 1
                ? (int)Math.Pow(projectile.SplitCount, projectile.SplitGeneration + 1)
                : 1);

    private int PatternThreatReservation() => Phase switch
    {
        1 => 9,
        2 => 11,
        3 => 13,
        4 => 11,
        _ => 15,
    };

    protected override void SetDreamPhase(int phase)
    {
        base.SetDreamPhase(phase);
        PhaseDeclarations = 0;
        if (Phase == 4 && !ChosenSurvivalCleared)
        {
            ChosenSurvivalActive = true;
            ChosenSurvivalRemaining = ChosenSurvivalDuration;
        }
        else
        {
            ChosenSurvivalActive = false;
        }
    }

    private void BeginChosenSurvival()
    {
        if (ChosenSurvivalActive || ChosenSurvivalCleared)
            return;
        Hp = Math.Max(1, (int)Math.Round(MaxHp * .5));
        SetDreamPhase(4);
        ChosenSurvivalActive = true;
        ChosenSurvivalRemaining = ChosenSurvivalDuration;
        TransitionCleanupRequested = true;
    }

    protected override void UpdatePhase()
    {
        if (DebugPhaseLocked || ChosenSurvivalActive || Dying)
            return;
        double ratio = Math.Clamp((double)Hp / MaxHp, 0.0, 1.0);
        if (!ChosenSurvivalCleared)
        {
            if (ratio <= .5)
            {
                if (PhaseDeclarations >= MinimumDamagePhaseDeclarations)
                    BeginChosenSurvival();
                return;
            }
            int desired = ratio > .75 ? 1 : ratio > .625 ? 2 : 3;
            if (desired != Phase && PhaseDeclarations >= MinimumDamagePhaseDeclarations)
                SetDreamPhase(desired);
            return;
        }
        if (Phase != 5)
            SetDreamPhase(5);
    }

    public override void DebugSetPhase(int phase)
    {
        phase = Math.Clamp(phase, 1, 5);
        DebugPhaseLocked = true;
        ChosenSurvivalActive = false;
        if (phase >= 5)
            ChosenSurvivalCleared = true;
        SetDreamPhase(phase);
        AttackCooldown = 0f;
        if (phase == 4)
        {
            ChosenSurvivalCleared = false;
            ChosenSurvivalActive = true;
            ChosenSurvivalRemaining = ChosenSurvivalDuration;
        }
    }

    public override HitResult TakeDamage(double amount, string partId = "body", DamageSource source = DamageSource.Direct)
    {
        if (ChosenSurvivalActive || Dying)
            return new HitResult(false, false, 0, true);

        if (!ChosenSurvivalCleared)
        {
            double floorRatio = Phase switch { 1 => .75, 2 => .625, _ => .50 };
            int floor = Math.Max(1, (int)Math.Round(MaxHp * floorRatio));
            double permitted = Math.Max(0, Hp - floor);
            if (permitted <= 0)
            {
                if (PhaseDeclarations >= MinimumDamagePhaseDeclarations)
                    UpdatePhase();
                return new HitResult(false, false, 0, true);
            }
            var gated = base.TakeDamage(Math.Min(amount, permitted), partId, source);
            if (Hp <= floor && PhaseDeclarations >= MinimumDamagePhaseDeclarations)
                UpdatePhase();
            return new HitResult(gated.Applied, false, gated.Amount, gated.Blocked);
        }

        if (Phase == 5 && PhaseDeclarations < MinimumDamagePhaseDeclarations)
        {
            double permitted = Math.Max(0, Hp - 1);
            if (permitted <= 0)
                return new HitResult(false, false, 0, true);
            var gated = base.TakeDamage(Math.Min(amount, permitted), partId, source);
            return new HitResult(gated.Applied, false, gated.Amount, gated.Blocked);
        }
        return base.TakeDamage(amount, partId, source);
    }

    public override void Update(EnemyUpdateContext context)
    {
        if (!ChosenSurvivalActive)
        {
            base.Update(context);
            return;
        }

        base.Update(context);
        if (!ChosenSurvivalActive || Dying)
            return;
        ChosenSurvivalRemaining = Math.Max(0.0, ChosenSurvivalRemaining - Seconds());
        if (ChosenSurvivalRemaining <= 0 && !DebugPhaseLocked)
        {
            ChosenSurvivalActive = false;
            ChosenSurvivalCleared = true;
            Hp = Math.Max(1, (int)Math.Round(MaxHp * .5));
            SetDreamPhase(5);
        }
    }

    protected override void FirePhantasiaPattern(float playerX, float playerY, EnemyUpdateContext context)
    {
        if (ActiveHypnoThreatBurden(context.ProjectileSink) + PatternThreatReservation() >
            ActiveThreatSoftCap)
        {
            MarkAttack(.2f);
            return;
        }
        var center = Center();
        var target = new Vector2(playerX, playerY);
        var sink = context.ProjectileSink;
        switch (Phase)
        {
            case 1: // Idol: two of three shrines are illusory.
                for (int index = 0; index < 3; index++)
                {
                    float angle = index * 2f * MathF.PI / 3f;
                    var origin = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * Simulation.TileSize * 2.8f;
                    FanFrom(sink, origin, target, 3, .55f, .78f, 260, "idol", illusion: index != PatternRotation % 3);
                }
                break;
            case 2: // Spoken Rule: the banner may lie about which way to move.
                RuleTruth = PatternRotation % 3 != 2;
                RuleText = RuleTruth ? "MOVE" : "REMAIN";
                FanFrom(sink, center, target, 5, 1.25f, .82f, 270, "spoken_rule", illusion: !RuleTruth, path: "sine");
                if (!RuleTruth)
                    RadialFrom(sink, center, 6, .5f, 245, "true_sigil");
                break;
            case 3: // Inheritance: three lineages each bifurcate across two generations.
                for (int index = 0; index < 3; index++)
                {
                    float direction = MathF.Atan2(playerY - center.Y, playerX - center.X) + (index - 1) * .42f;
                    var shot = ShotFrom(sink, center, direction, .72f, 260, "lineage");
                    shot.SplitCount = 2;
                    shot.SplitAt = Simulation.TileSize * (3.2f + index);
                    shot.SplitGeneration = 1;
                }
                ShotFrom(sink, center, MathF.Atan2(playerY - center.Y, playerX - center.X),
                    .78f, 245, "inheritance_claim");
                break;
            case 4: // Chosen: a real volley beside a harmless illusory cage.
                FanFrom(sink, center, target, 3, .42f, .92f, 275, "chosen");
                RadialFrom(sink, center, 8, .38f, 230, "spared", illusion: true);
                break;
            default: // Offering: alternating real/illusory rings plus a debt fan.
                RadialFrom(sink, center, 10, .42f, 275, "offering", illusion: PatternRotation % 2 == 0);
                FanFrom(sink, center, target, 5, .8f, .8f, 285, "debt");
                break;
        }
        PatternRotation++;
        PhaseDeclarations++;
        MarkAttack(.52f);
    }
}
