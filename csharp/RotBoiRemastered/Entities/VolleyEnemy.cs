using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// Tiered cone attacker; higher tiers trade mobility for wider volleys.
/// Ported from enemyTypes.py's VolleyEnemy. Overrides Update completely
/// (rather than reusing WanderingRangedEnemy.Update) because its
/// charge-then-fire pattern and simpler kite bands don't match the shared
/// base logic -- same as the Python original's full updateEnemy override.
/// </summary>
public sealed class VolleyEnemy : WanderingRangedEnemy
{
    private static readonly IReadOnlyDictionary<string, (int Count, float Spread, float Charge, float Cooldown, float AttackRange)> TierSettings =
        new Dictionary<string, (int, float, float, float, float)>
        {
            ["small"] = (4, .46f, .25f, 1.75f, 8f),
            ["medium"] = (7, .72f, .55f, 2.45f, 10f),
            ["large"] = (10, 1.02f, .9f, 3.35f, 12f),
        };

    public string Tier { get; }
    private readonly int _pelletCount;
    private readonly float _spread;
    private readonly float _chargeDuration;
    private bool _charging;
    private float _chargeRemaining;

    public VolleyEnemy(float worldX, float worldY, float speed, float size, Color color,
        double damage, double hp, double expValue, double difficulty,
        string tier = "small", string archetype = "drifter", string difficultyTier = "easy", Random? rng = null)
        : base(worldX, worldY, speed, size, color, damage, hp, expValue, difficulty, archetype, difficultyTier, rng)
    {
        Tier = tier;
        var settings = TierSettings[tier];
        _pelletCount = settings.Count;
        _spread = settings.Spread;
        _chargeDuration = settings.Charge;
        AttackCooldownMax = Simulation.FrameRate * settings.Cooldown;
        AttackRangeTiles = settings.AttackRange;
    }

    protected override void Fire(float playerWorldX, float playerWorldY, List<EnemyProjectile> projectileSink)
    {
        float centerX = WorldX + Size / 2f, centerY = WorldY + Size / 2f;
        float baseDirection = MathF.Atan2(playerWorldY - centerY, playerWorldX - centerX);
        MarkAttack(.3f);
        for (int index = 0; index < _pelletCount; index++)
        {
            float fraction = _pelletCount == 1 ? .5f : (float)index / (_pelletCount - 1);
            float direction = baseDirection - _spread / 2f + _spread * fraction;
            projectileSink.Add(new EnemyProjectile(
                centerX, centerY, direction,
                speed: (float)(Rng.NextDouble() * (1.65 - 1.05) + 1.05),
                damage: Damage * (.78f / _pelletCount),
                size: Size * (float)(Rng.NextDouble() * (.34 - .24) + .24),
                travelRange: Simulation.TileSize * AttackRangeTiles,
                color: UiTheme.Gold, owner: $"volley_{Tier}"));
        }
    }

    public override void Update(EnemyUpdateContext context)
    {
        AdvanceAge();
        AttackCooldown -= (float)Simulation.GetTimerStep();
        var battleground = context.Battleground;
        var (directionX, directionY, distance) = EnemyCatalogData.Normalise(
            context.PlayerWorldX - (WorldX + Size / 2f), context.PlayerWorldY - (WorldY + Size / 2f));

        if (!UpdateAwareness(distance))
        {
            _charging = false;
            Wander(battleground, .2f);
            FinishMovementTracking();
            return;
        }

        float attackRange = Simulation.TileSize * AttackRangeTiles;
        if (distance < attackRange * .48f)
            Move(-directionX, -directionY, .28f, battleground);
        else if (distance <= attackRange)
        {
            int side = CombatSide == 0 ? 1 : CombatSide;
            Move(-directionY * side, directionX * side, .12f, battleground);
        }
        else
        {
            Move(directionX, directionY, .38f, battleground);
        }

        if (_charging)
        {
            _chargeRemaining -= (float)Simulation.GetTimerStep();
            if (_chargeRemaining <= 0)
            {
                Fire(context.PlayerWorldX, context.PlayerWorldY, context.ProjectileSink);
                _charging = false;
                AttackCooldown = AttackCooldownMax * (float)(Rng.NextDouble() * (1.12 - .9) + .9);
            }
        }
        else if (distance <= attackRange && AttackCooldown <= 0)
        {
            _charging = true;
            _chargeRemaining = Simulation.FrameRate * _chargeDuration;
        }
        FinishMovementTracking();
    }

    public override void Draw(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        base.Draw(spriteBatch, camera, playerWorldPosition, screenShake);
        Vector2 screenPosition = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        var rect = new Rectangle((int)screenPosition.X, (int)screenPosition.Y, (int)Size, (int)Size);
        int bars = Tier switch { "small" => 1, "medium" => 2, "large" => 3, _ => 1 };
        for (int index = 0; index < bars; index++)
        {
            float offset = (index - (bars - 1) / 2f) * Size * .18f;
            Primitives2D.Line(spriteBatch,
                new Vector2(rect.Center.X - Size * .2f, rect.Center.Y + offset),
                new Vector2(rect.Center.X + Size * .25f, rect.Center.Y + offset), UiTheme.Gold, 3);
        }
        if (_charging)
        {
            float progress = 1 - _chargeRemaining / Math.Max(1f, Simulation.FrameRate * _chargeDuration);
            var inflated = rect;
            inflated.Inflate(10, 10);
            Primitives2D.Arc(spriteBatch, inflated, -MathF.PI / 2, -MathF.PI / 2 + 2 * MathF.PI * progress, UiTheme.Cream, 4);
        }
    }
}
