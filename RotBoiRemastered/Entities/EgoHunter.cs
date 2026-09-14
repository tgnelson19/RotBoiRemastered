using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// The Ego's hunter: a lone elite of one sense that is released behind the
/// player every few minutes and walks toward them across the whole map. It is
/// never despawned by distance, sees three views out, and once it has seen
/// you it does not lose you. Killing it rolls a dungeon door at 1 in 5.
/// </summary>
public sealed class EgoHunter : WanderingRangedEnemy
{
    public const double DungeonDropChance = .2;
    public string SenseKey { get; }
    public bool HasSeenPlayer { get; private set; }

    public EgoHunter(float worldX, float worldY, string senseKey, float awarenessRange, int level, Random? rng = null)
        : base(worldX, worldY,
            speed: 1.35f,
            size: Simulation.TileSize * 1.35f,
            color: GamePaths.PathsByKey[senseKey].Accent,
            damage: 120 + level * 9,
            hp: 9_000 + level * 900,
            expValue: 240 + level * 18,
            difficulty: 4.0,
            archetype: "hunter",
            difficultyTier: "hard",
            rng: rng)
    {
        SenseKey = senseKey;
        ContentPath = senseKey;
        Family = "ego_hunter";
        CombatRole = "elite";
        ThreatCost = 26;
        AwarenessRange = awarenessRange;
        DisengageRange = float.PositiveInfinity;
        AttackRangeTiles = 11f;
        AttackCooldownMax = Simulation.FrameRate * 1.05f;
        AttackCooldown = AttackCooldownMax;
    }

    public override void Update(EnemyUpdateContext context)
    {
        float centerX = WorldX + Size / 2f, centerY = WorldY + Size / 2f;
        var (dx, dy, distance) = EnemyCatalogData.Normalise(context.PlayerWorldX - centerX, context.PlayerWorldY - centerY);
        if (distance <= AwarenessRange)
            HasSeenPlayer = true;
        if (distance > AwarenessRange && !HasSeenPlayer)
        {
            // Tracking: walk the scent even before the target is in range.
            AdvanceAge();
            Move(dx, dy, .55f, context.Battleground);
            FinishMovementTracking();
            return;
        }
        base.Update(context);
    }

    protected override void Fire(float playerWorldX, float playerWorldY, List<EnemyProjectile> projectileSink)
    {
        MarkAttack();
        float centerX = WorldX + Size / 2f, centerY = WorldY + Size / 2f;
        float aim = MathF.Atan2(playerWorldY - centerY, playerWorldX - centerX);
        float shot = Math.Max(12f, Size * .28f);
        // Three-lane volley plus a slow tracking orb.
        for (int lane = -1; lane <= 1; lane++)
            projectileSink.Add(new EnemyProjectile(centerX - shot / 2f, centerY - shot / 2f, aim + lane * .11f,
                speed: 1.7f, damage: Damage * .5f, size: shot, travelRange: Simulation.TileSize * 16,
                color: Color, shape: "diamond"));
        projectileSink.Add(new EnemyProjectile(centerX - shot, centerY - shot, aim,
            speed: .85f, damage: Damage * .9f, size: shot * 2f, travelRange: Simulation.TileSize * 20,
            color: Color.Lerp(Color, UiTheme.Cream, .4f), shape: "circle",
            target: new Vector2(playerWorldX, playerWorldY), acceleration: .004f));
    }

    public override void Draw(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        base.Draw(spriteBatch, camera, playerWorldPosition, screenShake);
        EnemyRenderPose pose = RenderPose(camera, playerWorldPosition, screenShake);
        // A single unblinking eye in the sense's colour: the hunter is watching.
        float r = pose.Rect.Width * .16f;
        Primitives2D.FillCircle(spriteBatch, pose.Center, r, UiTheme.Cream);
        Primitives2D.FillCircle(spriteBatch, pose.Center + pose.Facing * r * .35f, r * .55f, Color);
        Primitives2D.CircleOutline(spriteBatch, pose.Center, pose.Rect.Width * .55f, Color * .55f, 2);
    }
}
