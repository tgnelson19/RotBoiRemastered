using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// "THE NEAR HORIZON" -- a simple, fast rush-pattern <see cref="PathChaseBoss"/>
/// with no phase-specific attack overrides. Ported from bossTypes.py's Ishe.
/// Uses `PathChaseBoss`'s stock `take_damage` (base `Enemy.TakeDamage`, no
/// stagger) and stock movement/attack dispatch -- the only override is the
/// sight-symbol icon drawn over the body.
/// </summary>
public class Ishe : PathChaseBoss
{
    public static readonly PathChaseBossConfig IsheConfig = PathChaseBossConfig.Default with
    {
        BossName = "ISHE", Subtitle = "THE NEAR HORIZON",
        PhaseLabels = new[] { "GLIMPSE", "BLINK", "FLASH" },
        Pattern = "rush", OwnerPrefix = "ishe_sight",
        BodyColor = new Color(107, 190, 221), AccentColor = new Color(235, 142, 59),
        MovementSpeed = .43, BodyScale = 1.42, CooldownSeconds = 1.18,
        ShotSpeed = 1.9, ShotScale = .20, ShotRangeTiles = 8,
        ArenaShape = "triangle", ArenaScale = 11.2,
    };

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
        };

    private static readonly IReadOnlyList<string> SightSymbolOrder = new[] { "GLIMPSE", "BLINK", "FLASH" };

    public Ishe(float worldX, float worldY, Battleground battleground, Random? rng = null)
        : this(worldX, worldY, battleground, IsheConfig, rng)
    {
    }

    protected Ishe(float worldX, float worldY, Battleground battleground, PathChaseBossConfig config, Random? rng = null)
        : base(worldX, worldY, battleground, config, rng)
    {
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
