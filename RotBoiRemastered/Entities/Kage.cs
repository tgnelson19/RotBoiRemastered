using Microsoft.Xna.Framework;
using RotBoiRemastered.Core;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>"THE FIRST REACTION" -- a mid-boss establishing the seven-sins pattern language. Ported from bossTypes.py's Kage.</summary>
public class Kage : SinChemesthesisBoss
{
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

    protected override void FireSinPattern(float playerX, float playerY, EnemyUpdateContext context)
    {
        var center = Center();
        float aimed = MathF.Atan2(playerY - center.Y, playerX - center.X);
        var sink = context.ProjectileSink;
        switch (Phase)
        {
            case 1: // Gluttony / Greed: a feast of lingering morsels.
                Radial(sink, 5, .34f, 245, "feast", mine: true);
                break;
            case 2: // Wrath / Pride: invitation followed by retaliation.
                Fan(sink, aimed, 5, 1.05f, .82f, 270, "provocation");
                Laser(sink, aimed + MathF.PI, 240, "retort");
                break;
            case 3: // Sloth / Envy: slow mirrors occupy the field.
                foreach (int side in new[] { -1, 1 })
                    Shot(sink, aimed + side * .72f, .42f, 250, path: "sine", ownerSuffix: "stagnant_mirror");
                Radial(sink, 4, .18f, 230, "stagnation", mine: true);
                break;
            default: // Lust / Avarice: converging lanes make tempting gaps.
                Fan(sink, aimed, 7, 2.2f, .56f, 265, "lure");
                Bomb(sink, playerX, playerY, 280, "lure_reward");
                break;
        }
        MarkAttack(.5f);
    }
}
