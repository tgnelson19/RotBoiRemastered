using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// Slow pressure enemy that fires heavy bursts and births fragile chasers as
/// its health crosses thresholds. Ported from enemyTypes.py's ParentEnemy.
/// Uses the base Enemy.AttackCooldown property (rather than its own field)
/// since Python's `self.attackCooldown` here is exactly that same
/// duck-typed concept RuntimeEncounter's constructor already looks for.
/// </summary>
public sealed class ParentEnemy : Enemy
{
    private static readonly double[] Thresholds = { .70, .40, .15 };

    private readonly Random _rng;
    private float _burstTimer;
    private int _burstRemaining;
    private readonly HashSet<double> _crossedThresholds = new();
    private int _pendingChildren;

    public ParentEnemy(float worldX, float worldY, float speed, float size, Color color, double damage, double hp,
        double expValue, double difficulty, float awarenessRange, string archetype = "parent",
        string difficultyTier = "easy", Random? rng = null)
        : base(worldX, worldY, speed, size, color, damage, hp, expValue, difficulty, awarenessRange, archetype, difficultyTier, rng)
    {
        _rng = rng ?? Random.Shared;
        AttackCooldown = Simulation.FrameRate * (float)(_rng.NextDouble() * (1.8 - 1.0) + 1.0);
    }

    public override HitResult TakeDamage(double amount, string partId = "body")
    {
        double previousRatio = (double)Hp / MaxHp;
        var result = base.TakeDamage(amount, partId);
        double currentRatio = Math.Max(0, Hp) / (double)MaxHp;
        foreach (var threshold in Thresholds)
        {
            if (previousRatio > threshold && threshold >= currentRatio && !_crossedThresholds.Contains(threshold))
            {
                _crossedThresholds.Add(threshold);
                _pendingChildren += TierRank + 1;
            }
        }
        return result;
    }

    private void BirthChild(Battleground battleground)
    {
        float angle = (float)(_rng.NextDouble() * 2 * Math.PI - Math.PI);
        float childSize = Size * .38f;
        float distance = Size * .72f;
        var candidate = new Rectangle(
            (int)(WorldX + Size / 2f + MathF.Cos(angle) * distance - childSize / 2f),
            (int)(WorldY + Size / 2f + MathF.Sin(angle) * distance - childSize / 2f),
            (int)childSize, (int)childSize);
        var safe = battleground.FindNearestOpenRect(candidate);
        var child = new ChildEnemy(
            safe.X, safe.Y, Speed * 3.4f, childSize, UiTheme.Red, Damage * .48, Math.Max(1, MaxHp * .08),
            ExpValue * .12, Difficulty, AwarenessRange, archetype: "runner", difficultyTier: DifficultyTier, rng: _rng);
        SpawnedEnemies.Add(child);
    }

    private void FireBurstShot(float playerWorldX, float playerWorldY, List<EnemyProjectile> projectileSink)
    {
        float centerX = WorldX + Size / 2f, centerY = WorldY + Size / 2f;
        float baseDirection = MathF.Atan2(playerWorldY - centerY, playerWorldX - centerX);
        MarkAttack(.25f);
        int projectileCount = 2 * TierRank + 1;
        for (int index = 0; index < projectileCount; index++)
        {
            float offset = (index - (projectileCount - 1) / 2f) * .16f;
            projectileSink.Add(new EnemyProjectile(
                centerX, centerY, baseDirection + offset, speed: .72f,
                damage: Damage * .42f, size: Size * .42f,
                travelRange: Simulation.TileSize * 18f, color: UiTheme.Purple,
                shape: "diamond", owner: "parent_burst"));
        }
    }

    public override void Update(EnemyUpdateContext context)
    {
        AdvanceAge();
        var battleground = context.Battleground;
        var (directionX, directionY, distance) = EnemyCatalogData.Normalise(
            context.PlayerWorldX - (WorldX + Size / 2f), context.PlayerWorldY - (WorldY + Size / 2f));

        if (!UpdateAwareness(distance))
        {
            Wander(battleground, .14f);
            FinishMovementTracking();
            return;
        }

        if (_pendingChildren > 0)
        {
            BirthChild(battleground);
            _pendingChildren -= 1;
        }

        float step = Speed * .34f * (float)Simulation.GetFrameScale();
        TryAxisMove(directionX * step, "x", battleground);
        TryAxisMove(directionY * step, "y", battleground);
        AttackCooldown -= (float)Simulation.GetTimerStep();
        _burstTimer -= (float)Simulation.GetTimerStep();
        if (_burstRemaining > 0 && _burstTimer <= 0)
        {
            FireBurstShot(context.PlayerWorldX, context.PlayerWorldY, context.ProjectileSink);
            _burstRemaining -= 1;
            _burstTimer = Simulation.FrameRate * .18f;
        }
        else if (AttackCooldown <= 0)
        {
            _burstRemaining = 2 + TierRank;
            _burstTimer = 0;
            AttackCooldown = Simulation.FrameRate * (float)(_rng.NextDouble() * (3.5 - 2.8) + 2.8);
        }
        FinishMovementTracking();
    }

    public override void Draw(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        base.Draw(spriteBatch, camera, playerWorldPosition, screenShake);
        Vector2 screenPosition = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        var rect = new Rectangle((int)screenPosition.X, (int)screenPosition.Y, (int)Size, (int)Size);
        var center = new Vector2(rect.Center.X, rect.Center.Y);
        Primitives2D.FillCircle(spriteBatch, center, (int)(Size * .28f), UiTheme.Purple);
        Primitives2D.FillCircle(spriteBatch, center, (int)(Size * .12f), UiTheme.Cream);
    }
}
