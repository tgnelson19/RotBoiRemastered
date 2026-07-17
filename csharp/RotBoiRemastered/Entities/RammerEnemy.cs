using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// Telegraphs a fixed charge, damages enemies in its path, then stalls after
/// impact. Ported from enemyTypes.py's RammerEnemy. Needs
/// EnemyUpdateContext.AllEnemies (Python read `cS.enemyHolster` directly) to
/// find charge-path collisions.
/// </summary>
public sealed class RammerEnemy : Enemy
{
    private string _ramState = "tracking";
    private float _ramTimer;
    private float _ramDirection;
    private readonly HashSet<Enemy> _ramHitTargets = new();

    public RammerEnemy(float worldX, float worldY, float speed, float size, Color color, double damage, double hp,
        double expValue, double difficulty, float awarenessRange, string archetype = "rammer",
        string difficultyTier = "easy", Random? rng = null)
        : base(worldX, worldY, speed, size, color, damage, hp, expValue, difficulty, awarenessRange, archetype, difficultyTier, rng)
    {
        _ramTimer = Simulation.FrameRate * 1.4f;
    }

    public override void Update(EnemyUpdateContext context)
    {
        AdvanceAge();
        _ramTimer -= (float)Simulation.GetTimerStep();
        var battleground = context.Battleground;

        if (_ramState == "stunned")
        {
            if (_ramTimer <= 0)
            {
                _ramState = "tracking";
                _ramTimer = Simulation.FrameRate * 1.5f;
            }
            FinishMovementTracking();
            return;
        }
        if (_ramState == "tracking")
        {
            float distance = Vector2.Distance(new Vector2(context.PlayerWorldX, context.PlayerWorldY), new Vector2(WorldX, WorldY));
            if (!UpdateAwareness(distance))
            {
                Wander(battleground, .14f);
            }
            else if (_ramTimer <= 0)
            {
                _ramDirection = MathF.Atan2(context.PlayerWorldY - WorldY, context.PlayerWorldX - WorldX);
                _ramState = "windup";
                _ramTimer = Simulation.FrameRate * .7f;
            }
        }
        else if (_ramState == "windup")
        {
            if (_ramTimer <= 0)
            {
                _ramState = "charging";
                _ramTimer = Simulation.FrameRate * (1.0f + .2f * TierRank);
                _ramHitTargets.Clear();
                MarkAttack(.4f);
            }
        }
        else
        {
            float step = Speed * (3.0f + .45f * TierRank) * (float)Simulation.GetFrameScale();
            float moveX = MathF.Cos(_ramDirection) * step;
            float moveY = MathF.Sin(_ramDirection) * step;
            bool movedX = Math.Abs(moveX) < .001f || TryAxisMove(moveX, "x", battleground);
            bool movedY = Math.Abs(moveY) < .001f || TryAxisMove(moveY, "y", battleground);
            bool moved = movedX && movedY;
            var ownRect = WorldRect();
            foreach (var enemy in context.AllEnemies)
            {
                if (enemy == this || enemy is RammerEnemy || _ramHitTargets.Contains(enemy))
                    continue;
                if (enemy.GetWorldHitboxes().Any(hitbox => ownRect.Intersects(hitbox.Rect)))
                {
                    _ramHitTargets.Add(enemy);
                    enemy.TakeDamage(Damage * .38);
                    enemy.ApplyKnockback(MathF.Cos(_ramDirection) * Size * .3f, MathF.Sin(_ramDirection) * Size * .3f, battleground);
                }
            }
            if (!moved || _ramTimer <= 0)
            {
                _ramState = "stunned";
                _ramTimer = Simulation.FrameRate * 1.15f;
            }
        }
        FinishMovementTracking();
    }

    public override void Draw(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        base.Draw(spriteBatch, camera, playerWorldPosition, screenShake);
        Vector2 screenPosition = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        var rect = new Rectangle((int)screenPosition.X, (int)screenPosition.Y, (int)Size, (int)Size);
        if (_ramState == "windup")
        {
            var end = new Vector2(rect.Center.X + MathF.Cos(_ramDirection) * Simulation.TileSize * 6,
                rect.Center.Y + MathF.Sin(_ramDirection) * Simulation.TileSize * 6);
            Primitives2D.Line(spriteBatch, new Vector2(rect.Center.X, rect.Center.Y), end, UiTheme.Red, 3);
        }
        var points = new[]
        {
            new Vector2(rect.Right, rect.Center.Y),
            new Vector2(rect.Center.X, rect.Y + 7),
            new Vector2(rect.Center.X, rect.Bottom - 7),
        };
        Primitives2D.FillPolygon(spriteBatch, points, UiTheme.Cream);
    }
}
