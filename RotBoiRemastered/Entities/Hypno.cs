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
    public static readonly PathChaseBossConfig HypnoConfig = BaseConfig with
    {
        BossName = "HYPNO", Subtitle = "THE ORNATE SUGGESTION",
        PhaseLabels = new[] { "IDOL", "SPOKEN RULE", "INHERITANCE", "CHOSEN", "OFFERING" },
        OwnerPrefix = "hypno_phantasia",
        BodyColor = new Color(151, 56, 144), AccentColor = new Color(211, 91, 183),
        MovementSpeed = .18, BodyScale = 1.8, CooldownSeconds = 1.8,
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

    protected override void FirePhantasiaPattern(float playerX, float playerY, EnemyUpdateContext context)
    {
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
            case 3: // Inheritance: a shot that fractures into three descendants.
                for (int index = 0; index < 3; index++)
                {
                    float direction = MathF.Atan2(playerY - center.Y, playerX - center.X) + (index - 1) * .42f;
                    var shot = ShotFrom(sink, center, direction, .72f, 260, "lineage");
                    shot.SplitCount = 3;
                    shot.SplitAt = Simulation.TileSize * (3.2f + index);
                    shot.SplitGeneration = 1;
                }
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
        MarkAttack(.52f);
    }
}
