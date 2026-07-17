using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// Compact three-phase elite whose variants reorder the shared attacks.
/// Ported from enemyTypes.py's ArsenalMiniBoss. `TransitionCleanupOwner` used
/// Python's `id(self)` (object identity/memory address) to build a unique
/// per-instance projectile-owner tag -- a static incrementing counter here
/// instead, avoiding any dependence on object identity hashing.
/// </summary>
public sealed class ArsenalMiniBoss : Enemy
{
    private static int _nextInstanceId = 1;

    public IReadOnlyList<string> PhaseOrder { get; }
    public int Phase { get; private set; }
    public bool Invulnerable { get; private set; }
    public bool TransitionCleanupRequested { get; set; }
    public string TransitionCleanupOwner { get; }

    private float _transitionRemaining;

    public ArsenalMiniBoss(float worldX, float worldY, float speed, float size, Color color, double damage, double hp,
        double expValue, double difficulty, float awarenessRange, IReadOnlyList<string>? phaseOrder = null,
        string archetype = "miniboss", string difficultyTier = "medium", Random? rng = null)
        : base(worldX, worldY, speed, size, color, damage, hp, expValue, difficulty, awarenessRange, archetype, difficultyTier, rng)
    {
        PhaseOrder = phaseOrder ?? new[] { "volley", "laser", "bomb" };
        AttackCooldown = Simulation.FrameRate * 1.2f;
        TransitionCleanupOwner = $"miniboss_{_nextInstanceId++}";
    }

    public override HitResult TakeDamage(double amount, string partId = "body")
    {
        if (Invulnerable)
            return new HitResult(false, false, 0, true);
        int previousPhase = Phase;
        var result = base.TakeDamage(amount, partId);
        if (result.Killed)
            return result;
        double ratio = (double)Hp / MaxHp;
        int desiredPhase = ratio <= 1.0 / 3 ? 2 : ratio <= 2.0 / 3 ? 1 : 0;
        if (desiredPhase > previousPhase)
        {
            Phase = desiredPhase;
            Invulnerable = true;
            _transitionRemaining = Simulation.FrameRate * .8f;
            TransitionCleanupRequested = true;
        }
        return result;
    }

    private void FireVolley(float playerWorldX, float playerWorldY, List<EnemyProjectile> projectileSink)
    {
        MarkAttack(.2f);
        float centerX = WorldX + Size / 2f, centerY = WorldY + Size / 2f;
        float baseDirection = MathF.Atan2(playerWorldY - centerY, playerWorldX - centerX);
        for (int index = 0; index < 7; index++)
        {
            projectileSink.Add(new EnemyProjectile(
                centerX, centerY, baseDirection - .48f + index * .16f, 1.25f, Damage * .12f, Size * .15f,
                travelRange: Simulation.TileSize * 14f, color: UiTheme.Gold, owner: TransitionCleanupOwner));
        }
    }

    private void FireLaser(float playerWorldX, float playerWorldY, List<EnemyProjectile> projectileSink)
    {
        MarkAttack(.2f);
        float centerX = WorldX + Size / 2f, centerY = WorldY + Size / 2f;
        float baseDirection = MathF.Atan2(playerWorldY - centerY, playerWorldX - centerX);
        foreach (float offset in new[] { 0f, MathF.PI })
        {
            var laser = new EnemyProjectile(
                centerX, centerY, baseDirection + offset, 0, Damage * .42f, Size * .13f,
                travelRange: Simulation.TileSize * 18f, color: UiTheme.Red, path: "laser",
                lifetime: 2.4f, angularSpeed: .12f, owner: TransitionCleanupOwner, ignoreWalls: true);
            laser.TelegraphDuration = 1.0f;
            projectileSink.Add(laser);
        }
    }

    private void FireBomb(float playerWorldX, float playerWorldY, List<EnemyProjectile> projectileSink)
    {
        MarkAttack(.2f);
        float centerX = WorldX + Size / 2f, centerY = WorldY + Size / 2f;
        for (int index = 0; index < 3; index++)
        {
            float angle = index * 2f * MathF.PI / 3f;
            var target = new Vector2(playerWorldX + MathF.Cos(angle) * Simulation.TileSize * 2f, playerWorldY + MathF.Sin(angle) * Simulation.TileSize * 2f);
            var bomb = new EnemyProjectile(
                centerX, centerY, 0, 0, Damage * .38f, Size * .2f,
                travelRange: Simulation.TileSize * 30f, color: UiTheme.Purple, shape: "bomb", path: "bomb",
                target: target, owner: TransitionCleanupOwner, ignoreWalls: true);
            bomb.FuseDuration = 2.7f;
            bomb.BlastRadius = Simulation.TileSize * 1.7f;
            bomb.BurstCount = 6;
            projectileSink.Add(bomb);
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
            Wander(battleground, .12f);
            FinishMovementTracking();
            return;
        }
        if (Invulnerable)
        {
            _transitionRemaining -= (float)Simulation.GetTimerStep();
            if (_transitionRemaining <= 0)
            {
                Invulnerable = false;
                AttackCooldown = Simulation.FrameRate * .65f;
            }
            FinishMovementTracking();
            return;
        }

        float orbit = MathF.Sin(Age * .035f) * .32f;
        float step = Speed * .24f * (float)Simulation.GetFrameScale();
        TryAxisMove((directionX - directionY * orbit) * step, "x", battleground);
        TryAxisMove((directionY + directionX * orbit) * step, "y", battleground);
        AttackCooldown -= (float)Simulation.GetTimerStep();
        if (AttackCooldown <= 0)
        {
            string attack = PhaseOrder[Phase];
            float cooldown = attack switch { "volley" => 2.4f, "laser" => 3.8f, "bomb" => 4.4f, _ => 3.0f };
            switch (attack)
            {
                case "volley": FireVolley(context.PlayerWorldX, context.PlayerWorldY, context.ProjectileSink); break;
                case "laser": FireLaser(context.PlayerWorldX, context.PlayerWorldY, context.ProjectileSink); break;
                case "bomb": FireBomb(context.PlayerWorldX, context.PlayerWorldY, context.ProjectileSink); break;
            }
            AttackCooldown = Simulation.FrameRate * cooldown;
        }
        FinishMovementTracking();
    }

    public override void Draw(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        Vector2 screenPosition = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        var rect = new Rectangle((int)screenPosition.X, (int)screenPosition.Y, (int)Size, (int)Size);
        Color[] phaseColors = { UiTheme.Gold, UiTheme.Red, UiTheme.Purple };

        // Mini-bosses deliberately use a rigid, low-spectacle silhouette so the
        // smooth final-boss rendering remains visually exceptional.
        Primitives2D.FillRect(spriteBatch, new Rectangle(rect.X + 6, rect.Y + 6, rect.Width, rect.Height), UiTheme.Shadow);
        Primitives2D.FillRect(spriteBatch, rect, Color);
        Primitives2D.RectOutline(spriteBatch, rect, UiTheme.Ink, Math.Max(5, (int)(Size * .08f)));
        var core = rect;
        core.Inflate(-(int)(Size * .38f), -(int)(Size * .38f));
        Primitives2D.FillRect(spriteBatch, core, UiTheme.Void);
        Primitives2D.RectOutline(spriteBatch, core, phaseColors[Phase], 5);
        for (int notch = 0; notch <= Phase; notch++)
        {
            var notchRect = new Rectangle(rect.X + 8 + notch * 10, rect.Bottom - 14, 6, 6);
            Primitives2D.FillRect(spriteBatch, notchRect, phaseColors[Phase]);
        }
        if (Invulnerable)
        {
            var invulnRect = rect;
            invulnRect.Inflate(12, 12);
            Primitives2D.RectOutline(spriteBatch, invulnRect, UiTheme.Cream, 5);
        }
        if (Hp < MaxHp)
        {
            var bar = new Rectangle(rect.X, rect.Y - 10, rect.Width, 5);
            Primitives2D.FillRect(spriteBatch, bar, UiTheme.Ink);
            Primitives2D.FillRect(spriteBatch,
                new Rectangle(bar.X, bar.Y, (int)(bar.Width * Math.Max(0f, (float)Hp / MaxHp)), bar.Height), UiTheme.Red);
        }
    }
}
