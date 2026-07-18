using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// Tiny leader whose minions form up, then charge on its command. Ported
/// from enemyTypes.py's BannerCaptain. `battleground` is optional at
/// construction (defaulting to placing minions at their raw offset,
/// un-checked against walls) since, unlike Update, no enemy constructor in
/// this port otherwise takes a Battleground -- EnemyCatalog.create (which
/// does have one on hand) passes it through for full wall-avoidance
/// fidelity; direct construction in tests can omit it.
/// </summary>
public sealed class BannerCaptain : Enemy
{
    private readonly Random _rng;
    private float _commandCooldown;

    public BannerCaptain(float worldX, float worldY, float speed, float size, Color color, double damage, double hp,
        double expValue, double difficulty, float awarenessRange, string archetype = "banner",
        string difficultyTier = "easy", Random? rng = null, Battleground? battleground = null)
        : base(worldX, worldY, speed, size, color, damage, hp, expValue, difficulty, awarenessRange, archetype, difficultyTier, rng)
    {
        _rng = rng ?? Random.Shared;
        _commandCooldown = Simulation.FrameRate * 2.6f;

        int count = 3 + TierRank * 2;
        float minionSize = Size * .42f;
        for (int index = 0; index < count; index++)
        {
            float angle = index * 2f * MathF.PI / count;
            var candidate = new Rectangle(
                (int)(WorldX + MathF.Cos(angle) * Size * 1.2f), (int)(WorldY + MathF.Sin(angle) * Size * 1.2f),
                (int)minionSize, (int)minionSize);
            var safe = battleground?.FindNearestOpenRect(candidate) ?? candidate;
            var minion = new BannerMinion(
                safe.X, safe.Y, Speed * 1.65f, minionSize, UiTheme.Red, Damage * .55, MaxHp * .16,
                ExpValue * .12, Difficulty, AwarenessRange, leader: this, formationAngle: angle,
                archetype: "runner", difficultyTier: DifficultyTier, rng: _rng)
            {
                Family = "banner",
            };
            SpawnedEnemies.Add(minion);
        }
        AtomicSpawnGroup = true;
    }

    public override void Update(EnemyUpdateContext context)
    {
        base.Update(context);
        _commandCooldown -= (float)Simulation.GetTimerStep();
        if (AwarenessState != "wandering" && _commandCooldown <= 0)
        {
            MarkAttack(.38f);
            foreach (var minion in context.AllEnemies.OfType<BannerMinion>().Where(m => m.Leader == this))
            {
                float direction = MathF.Atan2(context.PlayerWorldY - minion.WorldY, context.PlayerWorldX - minion.WorldX);
                minion.FormationAngle = direction;
                minion.Speed *= 1.12f;
            }
            _commandCooldown = Simulation.FrameRate * Math.Max(1.5f, 3.0f - .45f * TierRank);
        }
    }

    public override void Draw(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        base.Draw(spriteBatch, camera, playerWorldPosition, screenShake);
        Vector2 screenPosition = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        var rect = new Rectangle((int)screenPosition.X, (int)screenPosition.Y, (int)Size, (int)Size);
        Primitives2D.FillRect(spriteBatch, new Rectangle(rect.Center.X - 3, rect.Y - 10, 6, 16), UiTheme.Gold);
        var pennant = new[]
        {
            new Vector2(rect.Center.X + 3, rect.Y - 10),
            new Vector2(rect.Center.X + Size * .35f, rect.Y - 3),
            new Vector2(rect.Center.X + 3, rect.Y + 3),
        };
        Primitives2D.FillPolygon(spriteBatch, pennant, UiTheme.Cream);
    }
}
