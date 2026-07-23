using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// Path-exclusive ranged families. Each family keeps one readable mechanic
/// across easy/medium/hard tiers while adding projectiles or pattern
/// complexity as TierRank rises.
/// </summary>
public sealed class PathVariantEnemy : WanderingRangedEnemy
{
    private static readonly IReadOnlyDictionary<string, (float Range, float Cooldown)> Settings =
        new Dictionary<string, (float, float)>
        {
            ["sound_echoer"] = (13f, 2.25f),
            ["sound_resonator"] = (10f, 3.4f),
            ["touch_clasper"] = (9f, 3.1f),
            ["touch_mirekeeper"] = (11f, 4.15f),
            ["sight_blinker"] = (8f, 1.45f),
            ["sight_lens"] = (15f, 3.05f),
            ["chem_cinderpod"] = (12f, 3.8f),
            ["chem_sporecaster"] = (12f, 2.75f),
            ["phantasia_mirage"] = (13f, 2.45f),
            ["phantasia_dreamweaver"] = (11f, 3.7f),
        };

    public string Variant { get; }

    public PathVariantEnemy(float worldX, float worldY, float speed, float size, Color color,
        double damage, double hp, double expValue, double difficulty, string variant,
        string difficultyTier = "easy", Random? rng = null)
        : base(worldX, worldY, speed, size, color, damage, hp, expValue, difficulty,
            variant, difficultyTier, rng)
    {
        if (!Settings.TryGetValue(variant, out var settings))
            throw new ArgumentException($"Unknown path enemy variant: {variant}", nameof(variant));
        Variant = variant;
        AttackRangeTiles = settings.Range;
        AttackCooldownMax = Simulation.FrameRate * Math.Max(.85f, settings.Cooldown - .22f * (TierRank - 1));
    }

    protected override void Fire(float playerWorldX, float playerWorldY, List<EnemyProjectile> projectileSink)
    {
        float centerX = WorldX + Size / 2f;
        float centerY = WorldY + Size / 2f;
        float direction = MathF.Atan2(playerWorldY - centerY, playerWorldX - centerX);
        MarkAttack(.3f);

        switch (Variant)
        {
            case "sound_echoer":
                FireEchoes(projectileSink, centerX, centerY, direction);
                break;
            case "sound_resonator":
                FireResonance(projectileSink, centerX, centerY, direction);
                break;
            case "touch_clasper":
                FireWeightBanks(projectileSink, centerX, centerY, direction);
                break;
            case "touch_mirekeeper":
                SeedMire(projectileSink, playerWorldX, playerWorldY);
                break;
            case "sight_blinker":
                FireBlinkFan(projectileSink, centerX, centerY, direction);
                break;
            case "sight_lens":
                FireSightLines(projectileSink, centerX, centerY, direction);
                break;
            case "chem_cinderpod":
                SeedCinders(projectileSink, playerWorldX, playerWorldY);
                break;
            case "chem_sporecaster":
                FireSpores(projectileSink, centerX, centerY, direction);
                break;
            case "phantasia_mirage":
                FireMirage(projectileSink, centerX, centerY, direction);
                break;
            case "phantasia_dreamweaver":
                WeaveDreamCourt(projectileSink, playerWorldX, playerWorldY);
                break;
        }
    }

    private void FireEchoes(List<EnemyProjectile> sink, float x, float y, float direction)
    {
        int count = 1 + TierRank;
        float size = Math.Max(10f, Size * .28f);
        for (int index = 0; index < count; index++)
        {
            float side = index % 2 == 0 ? 1f : -1f;
            float lane = (index / 2) * .1f * side;
            sink.Add(new EnemyProjectile(
                x - size / 2f, y - size / 2f, direction + lane,
                speed: 1.05f + TierRank * .08f, damage: Damage * (.68f / count), size: size,
                travelRange: Simulation.TileSize * (15 + TierRank), color: UiTheme.Gold,
                shape: "diamond", path: "sine", amplitude: side * Simulation.TileSize * (.55f + TierRank * .12f),
                frequency: .026f + TierRank * .003f, owner: "sound_echoer"));
        }
    }

    private void FireResonance(List<EnemyProjectile> sink, float x, float y, float direction)
    {
        int spokes = 4 + TierRank * 2;
        float phase = TierRank == 1 ? direction : Age * .018f;
        float size = Math.Max(11f, Size * .24f);
        for (int index = 0; index < spokes; index++)
        {
            sink.Add(new EnemyProjectile(
                x - size / 2f, y - size / 2f, phase + index * MathF.Tau / spokes,
                speed: .72f + TierRank * .08f, damage: Damage * (.54f / Math.Max(1, TierRank)), size: size,
                travelRange: Simulation.TileSize * (9 + TierRank * 2), color: UiTheme.Cream,
                shape: "diamond", owner: "sound_resonator"));
        }
    }

    private void FireWeightBanks(List<EnemyProjectile> sink, float x, float y, float direction)
    {
        int count = TierRank;
        for (int index = 0; index < count; index++)
        {
            float offset = (index - (count - 1) / 2f) * .26f;
            var bank = new EnemyProjectile(
                x, y, direction + offset, speed: .46f + .04f * TierRank,
                damage: Damage * (.72f / count), size: Size * (.78f + .08f * TierRank),
                travelRange: Simulation.TileSize * (8 + TierRank), color: new Color(103, 91, 55),
                path: "bank", owner: "touch_clasper");
            bank.TelegraphDuration = 1.05f - .1f * TierRank;
            sink.Add(bank);
        }
    }

    private void SeedMire(List<EnemyProjectile> sink, float playerX, float playerY)
    {
        int count = TierRank;
        for (int index = 0; index < count; index++)
        {
            float angle = index * MathF.Tau / count + Age * .01f;
            float radius = index == 0 ? 0f : Simulation.TileSize * (1.3f + .25f * TierRank);
            float size = Simulation.TileSize * (1.35f + .18f * TierRank);
            var pool = new EnemyProjectile(
                playerX + MathF.Cos(angle) * radius - size / 2f,
                playerY + MathF.Sin(angle) * radius - size / 2f,
                0, 0, Damage * (.52f / count), size,
                color: new Color(79, 101, 55), path: "pool",
                lifetime: 5.5f + TierRank, owner: "touch_mirekeeper", ignoreWalls: true)
            {
                TelegraphDuration = 1.15f,
                PersistentHazard = true,
            };
            sink.Add(pool);
        }
    }

    private void FireBlinkFan(List<EnemyProjectile> sink, float x, float y, float direction)
    {
        int count = 2 + TierRank;
        float size = Math.Max(7f, Size * .23f);
        for (int index = 0; index < count; index++)
        {
            float fraction = count == 1 ? .5f : index / (float)(count - 1);
            float offset = -.22f + .44f * fraction;
            sink.Add(new EnemyProjectile(
                x - size / 2f, y - size / 2f, direction + offset,
                speed: 1.75f + .16f * TierRank, damage: Damage * (.62f / count), size: size,
                travelRange: Simulation.TileSize * (8 + TierRank), color: new Color(135, 210, 230),
                shape: "diamond", owner: "sight_blinker"));
        }
    }

    private void FireSightLines(List<EnemyProjectile> sink, float x, float y, float direction)
    {
        int count = TierRank == 1 ? 1 : 2;
        for (int index = 0; index < count; index++)
        {
            float offset = count == 1 ? 0f : (index == 0 ? -.12f : .12f);
            var laser = new EnemyProjectile(
                x, y, direction + offset, 0, Damage * (.58f / count), Size * .12f,
                travelRange: Simulation.TileSize * (14 + TierRank), color: new Color(228, 142, 63),
                path: "laser", lifetime: .9f + TierRank * .18f,
                angularSpeed: TierRank == 3 ? (index == 0 ? .08f : -.08f) : 0f,
                owner: "sight_lens", ignoreWalls: true)
            {
                TelegraphDuration = .72f - .06f * TierRank,
            };
            sink.Add(laser);
        }
    }

    private void SeedCinders(List<EnemyProjectile> sink, float playerX, float playerY)
    {
        int count = 1 + TierRank;
        for (int index = 0; index < count; index++)
        {
            float angle = index * MathF.Tau / count + Age * .013f;
            float radius = Simulation.TileSize * (1.1f + .55f * index);
            float size = Simulation.TileSize * (.42f + .05f * TierRank);
            var mine = new EnemyProjectile(
                playerX + MathF.Cos(angle) * radius - size / 2f,
                playerY + MathF.Sin(angle) * radius - size / 2f,
                0, 0, Damage * (.48f / Math.Max(1, TierRank)), size,
                color: new Color(211, 91, 38), shape: "mine", path: "mine",
                lifetime: 7f + TierRank * 1.5f, owner: "chem_cinderpod", ignoreWalls: true)
            {
                TelegraphDuration = .95f + .08f * index,
                PersistentHazard = true,
            };
            sink.Add(mine);
        }
    }

    private void FireSpores(List<EnemyProjectile> sink, float x, float y, float direction)
    {
        int count = TierRank == 3 ? 2 : 1;
        for (int index = 0; index < count; index++)
        {
            float size = Math.Max(12f, Size * .32f);
            var spore = new EnemyProjectile(
                x - size / 2f, y - size / 2f,
                direction + (index - (count - 1) / 2f) * .2f,
                speed: .66f, damage: Damage * (.7f / count), size: size,
                travelRange: Simulation.TileSize * 16f, color: new Color(92, 120, 50),
                shape: "diamond", path: "sine", amplitude: Simulation.TileSize * .35f,
                frequency: .022f, owner: "chem_sporecaster")
            {
                SplitCount = 1 + TierRank,
                SplitAt = Simulation.TileSize * (3.5f + TierRank),
                SplitGeneration = TierRank == 3 ? 1 : 0,
            };
            sink.Add(spore);
        }
    }

    private void FireMirage(List<EnemyProjectile> sink, float x, float y, float direction)
    {
        int count = 3 + TierRank * 2;
        int realIndex = (int)(Age + TierRank) % count;
        float size = Math.Max(9f, Size * .25f);
        for (int index = 0; index < count; index++)
        {
            float offset = (index - (count - 1) / 2f) * .13f;
            bool real = index == realIndex || (TierRank == 3 && index == count - 1 - realIndex);
            var shot = new EnemyProjectile(
                x - size / 2f, y - size / 2f, direction + offset,
                speed: 1.02f + .06f * TierRank, damage: real ? Damage * .62f : 0f, size: size,
                travelRange: Simulation.TileSize * (13 + TierRank), color: new Color(202, 85, 174),
                shape: "diamond", owner: "phantasia_mirage")
            {
                Illusory = !real,
                TruthMarked = real,
            };
            sink.Add(shot);
        }
    }

    private void WeaveDreamCourt(List<EnemyProjectile> sink, float playerX, float playerY)
    {
        int count = 2 + TierRank;
        float radius = Simulation.TileSize * (1.15f + .25f * TierRank);
        float size = Simulation.TileSize * .32f;
        var center = new Vector2(playerX, playerY);
        for (int index = 0; index < count; index++)
        {
            float angle = index * MathF.Tau / count;
            sink.Add(new EnemyProjectile(
                playerX + MathF.Cos(angle) * radius - size / 2f,
                playerY + MathF.Sin(angle) * radius - size / 2f,
                0, 0, Damage * (.46f / Math.Max(1, TierRank)), size,
                color: new Color(161, 57, 147), shape: "diamond", path: "orbit",
                lifetime: 4.2f + TierRank * .6f, orbitCenter: center, orbitRadius: radius,
                orbitAngle: angle, angularSpeed: .55f + TierRank * .12f,
                owner: "phantasia_dreamweaver", ignoreWalls: true));
        }
    }

    public override void Draw(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        base.Draw(spriteBatch, camera, playerWorldPosition, screenShake);
        Vector2 screen = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        var rect = new Rectangle((int)screen.X, (int)screen.Y, (int)Size, (int)Size);
        var center = new Vector2(rect.Center.X, rect.Center.Y);
        int stroke = Math.Max(2, (int)(Size * .045f));
        int radius = Math.Max(4, (int)(Size * .2f));

        if (Variant.StartsWith("sound_"))
        {
            Primitives2D.Arc(spriteBatch, new Rectangle(rect.Center.X - radius, rect.Center.Y - radius, radius * 2, radius * 2),
                -MathF.PI / 2, MathF.PI / 2, UiTheme.Cream, stroke);
            Primitives2D.Arc(spriteBatch, new Rectangle(rect.Center.X - radius / 2, rect.Center.Y - radius / 2, radius, radius),
                -MathF.PI / 2, MathF.PI / 2, UiTheme.Gold, stroke);
        }
        else if (Variant.StartsWith("touch_"))
        {
            Primitives2D.Line(spriteBatch, new Vector2(rect.Left + radius, rect.Center.Y),
                new Vector2(rect.Right - radius, rect.Center.Y), UiTheme.Cream, stroke + 1);
            Primitives2D.FillCircle(spriteBatch, center, Math.Max(3, radius / 3), UiTheme.Ink);
        }
        else if (Variant.StartsWith("sight_"))
        {
            Primitives2D.CircleOutline(spriteBatch, center, radius, UiTheme.Cream, stroke);
            Primitives2D.FillCircle(spriteBatch, center, Math.Max(3, radius / 3), UiTheme.Red);
        }
        else if (Variant.StartsWith("chem_"))
        {
            for (int index = 0; index < 3; index++)
            {
                float angle = index * MathF.Tau / 3f + Age * .01f;
                Primitives2D.FillCircle(spriteBatch,
                    center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius,
                    Math.Max(2, radius / 4), UiTheme.Cream);
            }
        }
        else
        {
            var diamond = new[]
            {
                center + new Vector2(0, -radius), center + new Vector2(radius, 0),
                center + new Vector2(0, radius), center + new Vector2(-radius, 0),
            };
            Primitives2D.PolygonOutline(spriteBatch, diamond, UiTheme.Cream, stroke);
            Primitives2D.FillCircle(spriteBatch, center, Math.Max(2, radius / 4), UiTheme.Gold);
        }
    }
}
