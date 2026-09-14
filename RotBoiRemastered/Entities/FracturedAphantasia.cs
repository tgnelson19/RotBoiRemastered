using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// The Ego's level-20 event boss: a lone, fractured shard of Aphantasia's
/// Essence (phase 1/2 theming) roaming the veteran senseless wilds. Defeating
/// it drops the portal to the true Aphantasia fight
/// (<see cref="Systems.GameSession.EnterEgoAphantasia"/>).
///
/// This is a deliberate stub: it chases, keeps Aphantasia's palette, and
/// fires fractured rings/curtains built from plain EnemyProjectile primitives.
/// The real fractured phase 1/2 pattern set is still to be authored.
/// </summary>
public sealed class FracturedAphantasia : WanderingRangedEnemy
{
    public const string BossName = "FRACTURED ESSENCE";
    public const double HealthFraction = .35;
    public static readonly Color VoidBlue = new(8, 22, 72);
    public static readonly Color ShardLight = new(196, 214, 255);
    public static readonly Color ShardDark = new(64, 24, 96);

    private int _volley;

    public FracturedAphantasia(float worldX, float worldY, float awarenessRange, Random? rng = null)
        : base(worldX, worldY,
            speed: 1.25f,
            size: Simulation.TileSize * 1.9f,
            color: VoidBlue,
            damage: 300,
            hp: (int)Math.Round(Aphantasia.BaseBarHealth * HealthFraction),
            expValue: 1_800,
            difficulty: 5.0,
            archetype: "finale",
            difficultyTier: "hard",
            rng: rng)
    {
        AwarenessRange = awarenessRange;
        DisengageRange = awarenessRange * 2.5f;
        Family = "fractured_essence";
        CombatRole = "elite";
        ThreatCost = 80;
        ContentPath = "phantasia";
        AttackRangeTiles = 13f;
        AttackCooldownMax = Simulation.FrameRate * 1.35f;
        AttackCooldown = AttackCooldownMax;
    }

    public string DisplayName => BossName;

    protected override void Fire(float playerWorldX, float playerWorldY, List<EnemyProjectile> projectileSink)
    {
        // TODO(ego): fractured phase 1/2 pattern set (broken rings, fractured
        // curtains, light/dark tempo swaps) authored from Aphantasia.Attacks.
        MarkAttack();
        float centerX = WorldX + Size / 2f, centerY = WorldY + Size / 2f;
        float aimed = MathF.Atan2(playerWorldY - centerY, playerWorldX - centerX);
        float shot = Math.Max(14f, Size * .22f);
        _volley++;
        if (_volley % 3 == 0)
        {
            // Broken ring: a full circle with two random gaps torn out of it.
            int count = 22;
            int gapA = Rng.Next(count), gapB = (gapA + count / 2 + Rng.Next(-2, 3)) % count;
            for (int index = 0; index < count; index++)
            {
                if (Math.Abs(index - gapA) <= 1 || Math.Abs(index - gapB) <= 1)
                    continue;
                float angle = index * MathF.Tau / count + _volley * .17f;
                projectileSink.Add(new EnemyProjectile(centerX - shot / 2f, centerY - shot / 2f, angle,
                    speed: 1.05f, damage: Damage * .6f, size: shot,
                    travelRange: Simulation.TileSize * 18, color: index % 2 == 0 ? ShardLight : ShardDark, shape: "diamond"));
            }
        }
        else
        {
            // Fractured fan: a tight aimed spread whose lanes drift apart.
            int lanes = 7;
            for (int index = 0; index < lanes; index++)
            {
                float offset = (index - (lanes - 1) / 2f) * .16f;
                projectileSink.Add(new EnemyProjectile(centerX - shot / 2f, centerY - shot / 2f, aimed + offset,
                    speed: 1.5f + Math.Abs(offset) * .6f, damage: Damage * .55f, size: shot * .9f,
                    travelRange: Simulation.TileSize * 20, color: ShardLight, shape: "diamond",
                    path: "sine", amplitude: 6f + Math.Abs(offset) * 30f));
            }
        }
    }

    public override void Draw(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        EnemyRenderPose pose = RenderPose(camera, playerWorldPosition, screenShake);
        Vector2 center = pose.Center;
        float radius = pose.Rect.Width * .5f;
        // Void core with a cracked, shifting shard halo -- Aphantasia's
        // Essence, but broken into pieces that never quite line up.
        Primitives2D.FillCircle(spriteBatch, center, radius, VoidBlue);
        int shards = 7;
        for (int index = 0; index < shards; index++)
        {
            float angle = index * MathF.Tau / shards + (float)(Age * .015) + index * .35f;
            float reach = radius * (1.05f + .18f * MathF.Sin((float)(Age * .05) + index));
            Vector2 tip = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * reach;
            Vector2 baseA = center + new Vector2(MathF.Cos(angle + .28f), MathF.Sin(angle + .28f)) * radius * .55f;
            Vector2 baseB = center + new Vector2(MathF.Cos(angle - .28f), MathF.Sin(angle - .28f)) * radius * .55f;
            Color shard = index % 2 == 0 ? ShardLight : ShardDark;
            Primitives2D.Line(spriteBatch, baseA, tip, shard, 3);
            Primitives2D.Line(spriteBatch, baseB, tip, shard, 3);
        }
        Primitives2D.CircleOutline(spriteBatch, center, radius * .45f,
            Color.Lerp(ShardLight, UiTheme.Cream, pose.AttackPulse), 2);
    }
}
