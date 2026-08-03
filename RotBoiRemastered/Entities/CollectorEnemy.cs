using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// Steals loose XP temporarily, grows, then returns it with a bonus on
/// death. Ported from enemyTypes.py's CollectorEnemy. Needs
/// EnemyUpdateContext.ExperienceBubbles (Python read `cS.experienceList`
/// directly) to find and absorb nearby bubbles.
/// </summary>
public sealed class CollectorEnemy : Enemy
{
    private double _storedExperience;
    private readonly float _baseSize;
    private readonly double _fleeThreshold;

    public CollectorEnemy(float worldX, float worldY, float speed, float size, Color color, double damage, double hp,
        double expValue, double difficulty, float awarenessRange, string archetype = "collector",
        string difficultyTier = "easy", Random? rng = null)
        : base(worldX, worldY, speed, size, color, damage, hp, expValue, difficulty, awarenessRange, archetype, difficultyTier, rng)
    {
        _baseSize = size;
        _fleeThreshold = expValue * (1.2 + .4 * TierRank);
    }

    public override void Update(EnemyUpdateContext context)
    {
        AdvanceAge();
        var battleground = context.Battleground;
        float centerX = WorldX + Size / 2f, centerY = WorldY + Size / 2f;
        float playerDistance = Vector2.Distance(
            new Vector2(centerX, centerY),
            new Vector2(context.PlayerWorldX, context.PlayerWorldY));
        if (!UpdateAwareness(playerDistance))
        {
            Wander(battleground, .2f);
            FinishMovementTracking();
            return;
        }

        bool handledBubble = false;
        foreach (var bubble in context.ExperienceBubbles.ToList())
        {
            var bubbleRect = bubble.WorldRect();
            float distance = Vector2.Distance(new Vector2(bubbleRect.Center.X, bubbleRect.Center.Y), new Vector2(centerX, centerY));
            if (distance <= Simulation.TileSize * (2.5f + TierRank))
            {
                if (distance <= Size)
                {
                    _storedExperience += bubble.Value;
                    context.ExperienceBubbles.Remove(bubble);
                }
                else
                {
                    var (dx, dy, _) = EnemyCatalogData.Normalise(bubbleRect.Center.X - centerX, bubbleRect.Center.Y - centerY);
                    float step = Speed * .55f * (float)Simulation.GetFrameScale();
                    TryAxisMove(dx * step, "x", battleground);
                    TryAxisMove(dy * step, "y", battleground);
                }
                handledBubble = true;
                break;
            }
        }
        if (!handledBubble)
        {
            var (dx, dy, _) = EnemyCatalogData.Normalise(context.PlayerWorldX - centerX, context.PlayerWorldY - centerY);
            int direction = _storedExperience >= _fleeThreshold ? -1 : 1;
            float step = Speed * (direction < 0 ? .65f : .28f) * (float)Simulation.GetFrameScale();
            TryAxisMove(dx * step * direction, "x", battleground);
            TryAxisMove(dy * step * direction, "y", battleground);
        }
        Size = Math.Min(_baseSize * 1.55f, _baseSize * (float)(1 + _storedExperience / Math.Max(1, _fleeThreshold) * .3));
        FinishMovementTracking();
    }

    public override bool IsDead()
    {
        if (Hp <= 0 && _storedExperience != 0)
        {
            ExpValue += _storedExperience * 1.15;
            _storedExperience = 0;
        }
        return Hp <= 0;
    }

    public override void Draw(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        base.Draw(spriteBatch, camera, playerWorldPosition, screenShake);
        var rect = RenderPose(camera, playerWorldPosition, screenShake).Rect;
        var points = new[]
        {
            new Vector2(rect.Center.X, rect.Top),
            new Vector2(rect.Right, rect.Center.Y),
            new Vector2(rect.Center.X, rect.Bottom),
            new Vector2(rect.Left, rect.Center.Y),
        };
        Primitives2D.FillPolygon(spriteBatch, points, UiTheme.Green);
        for (int index = 0; index < 3; index++)
        {
            float angle = Age * .08f + index * MathF.Tau / 3f;
            Vector2 mote = new(rect.Center.X + MathF.Cos(angle) * Size * .38f,
                rect.Center.Y + MathF.Sin(angle) * Size * .2f);
            Primitives2D.FillRect(spriteBatch,
                new Rectangle((int)mote.X - 2, (int)mote.Y - 2, 4, 4),
                index == 0 ? UiTheme.Cream : UiTheme.Green);
        }
    }
}
