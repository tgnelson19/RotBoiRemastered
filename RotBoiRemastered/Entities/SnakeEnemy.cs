using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// A composite enemy with a chain of separately-destructible body segments
/// trailing a head. Ported from enemyTypes.py's SnakeEnemy.
///
/// Cleanup vs. the Python original: segments were identified by their
/// integer `enumerate()` index, and `take_damage(amount, part_id="head")`
/// happily accepted either that int or the string "head"/"body" other
/// enemies use, purely because Python doesn't check parameter types.
/// Segment ids are strings here ("0", "1", ...) instead, so every Enemy's
/// `TakeDamage(double, string partId)` shares one real, statically-checked
/// contract instead of silently depending on dynamic typing.
/// </summary>
public sealed class SnakeEnemy : Enemy
{
    private sealed class Segment
    {
        public required string Id;
        public float X, Y;
        public int Hp;
        public int MaxHp;
    }

    private readonly Random _rng;
    public float HeadSize { get; }
    public float SegmentSize { get; }
    private readonly float _segmentSpacing;
    private readonly List<Segment> _segments = new();
    private readonly int _initialSegmentCount;

    public SnakeEnemy(float worldX, float worldY, float speed, float bodySize, Color color, double damage, double hp,
        double expValue, double difficulty, float awarenessRange, int segmentCount = 5, int? segmentHp = null,
        string archetype = "snake", string difficultyTier = "easy", Random? rng = null)
        : base(worldX, worldY, speed, bodySize, color, damage, hp, expValue, difficulty, awarenessRange, archetype, difficultyTier, rng)
    {
        _rng = rng ?? Random.Shared;
        HeadSize = bodySize * 1.16f;
        SegmentSize = bodySize * .76f;
        Size = HeadSize;
        _segmentSpacing = SegmentSize * 1.18f;
        _initialSegmentCount = Math.Max(1, segmentCount);
        int resolvedSegmentHp = segmentHp ?? (int)Math.Round(MaxHp * .6);

        // Battleground isn't known yet at construction time in this port (Python's
        // background.py exposed it as a module the constructor could reach into
        // directly) -- initial segment placement without wall-avoidance is an
        // acceptable simplification since Update's own follow-the-leader physics
        // corrects overlap within the first few frames regardless.
        for (int index = 0; index < segmentCount; index++)
        {
            float segmentX = worldX - (index + 1) * _segmentSpacing;
            float segmentY = worldY + Size * .2f;
            _segments.Add(new Segment { Id = index.ToString(), X = segmentX, Y = segmentY, Hp = resolvedSegmentHp, MaxHp = resolvedSegmentHp });
        }
        AttackCooldown = Simulation.FrameRate * (float)(_rng.NextDouble() * (2.2 - 1.2) + 1.2);
    }

    /// <summary>Lose almost all chase speed as the snake loses its protective body.</summary>
    private float MovementSpeedMultiplier()
    {
        float remaining = _segments.Count / (float)_initialSegmentCount;
        return .05f + .95f * remaining * remaining;
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
            Wander(battleground, .18f);
            FinishMovementTracking();
            return;
        }

        float weave = MathF.Sin(Age * .075f) * .32f;
        (directionX, directionY) = (directionX - directionY * weave, directionY + directionX * weave);
        (directionX, directionY, _) = EnemyCatalogData.Normalise(directionX, directionY);
        float step = Speed * MovementSpeedMultiplier() * (float)Simulation.GetFrameScale();
        TryAxisMove(directionX * step, "x", battleground);
        TryAxisMove(directionY * step, "y", battleground);

        float previousX = WorldX + (HeadSize - SegmentSize) / 2f;
        float previousY = WorldY + (HeadSize - SegmentSize) / 2f;
        foreach (var segment in _segments)
        {
            float followX = previousX - segment.X;
            float followY = previousY - segment.Y;
            float followDistance = MathF.Sqrt(followX * followX + followY * followY);
            if (followDistance > _segmentSpacing)
            {
                float amount = (followDistance - _segmentSpacing) / followDistance;
                segment.X += followX * amount;
                segment.Y += followY * amount;
            }
            previousX = segment.X;
            previousY = segment.Y;
        }

        if (distance <= Simulation.TileSize * 15f && AttackCooldown <= 0)
        {
            float centerX = WorldX + HeadSize / 2f, centerY = WorldY + HeadSize / 2f;
            float projectileSize = SegmentSize * .54f;
            MarkAttack(.25f);
            float baseDirection = MathF.Atan2(context.PlayerWorldY - centerY, context.PlayerWorldX - centerX);
            for (int index = 0; index < TierRank; index++)
            {
                float offset = (index - (TierRank - 1) / 2f) * .18f;
                context.ProjectileSink.Add(new EnemyProjectile(
                    centerX - projectileSize / 2f, centerY - projectileSize / 2f, baseDirection + offset,
                    speed: .9f + .08f * (TierRank - 1), damage: Damage * (.65f / TierRank), size: projectileSize,
                    travelRange: Simulation.TileSize * (14 + TierRank), color: UiTheme.Purple, shape: "diamond"));
            }
            AttackCooldown = Simulation.FrameRate * (float)(_rng.NextDouble() * ((3.15 - .2 * (TierRank - 1)) - (2.0 - .18 * (TierRank - 1))) + (2.0 - .18 * (TierRank - 1)));
        }
        FinishMovementTracking();
    }

    public override void Draw(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        for (int i = _segments.Count - 1; i >= 0; i--)
        {
            var segment = _segments[i];
            Vector2 screenPos = camera.WorldToScreen(new Vector2(segment.X, segment.Y), playerWorldPosition, screenShake);
            float slither = Moved ? MathF.Sin(Age * .18f - i * .72f) * SegmentSize * .09f : MathF.Sin(Age * .035f + i) * 1.2f;
            var rect = new Rectangle((int)(screenPos.X + slither), (int)(screenPos.Y - Math.Abs(slither) * .18f), (int)SegmentSize, (int)SegmentSize);
            Primitives2D.FillRect(spriteBatch, new Rectangle(rect.X + 3, rect.Y + 3, rect.Width, rect.Height), UiTheme.Shadow);
            Primitives2D.FillRect(spriteBatch, rect, new Color(72, 145, 104));
            Primitives2D.RectOutline(spriteBatch, rect, UiTheme.Ink, Math.Max(2, (int)(SegmentSize * .09f)));
            if (segment.Hp < segment.MaxHp)
            {
                var fill = new Rectangle(rect.X, rect.Y - 6, (int)(rect.Width * segment.Hp / (float)segment.MaxHp), 4);
                Primitives2D.FillRect(spriteBatch, fill, UiTheme.Green);
            }
        }

        Vector2 headScreenPos = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        float attack = VisualAttackTimer > 0 ? MathF.Sin(Math.Clamp(VisualAttackTimer / (Simulation.FrameRate * .25f), 0f, 1f) * MathF.PI) : 0f;
        float headBob = Moved ? Math.Abs(MathF.Sin(Age * .18f)) * HeadSize * .055f : MathF.Sin(Age * .035f) * 1.5f;
        int headWidth = (int)(HeadSize * (1f + attack * .16f));
        int headHeight = (int)(HeadSize * (1f - attack * .1f));
        var headRect = new Rectangle((int)(headScreenPos.X + HeadSize / 2f - headWidth / 2f),
            (int)(headScreenPos.Y + HeadSize - headHeight - headBob), headWidth, headHeight);
        Primitives2D.FillRect(spriteBatch, new Rectangle(headRect.X + 5, headRect.Y + 5, headRect.Width, headRect.Height), UiTheme.Shadow);
        Primitives2D.FillRect(spriteBatch, headRect, UiTheme.Purple);
        Primitives2D.RectOutline(spriteBatch, headRect, UiTheme.Ink, Math.Max(3, (int)(HeadSize * .09f)));
        int eyeSize = Math.Max(3, (int)(HeadSize * .12f));
        Primitives2D.FillRect(spriteBatch, new Rectangle((int)(headRect.X + headRect.Width * .25f), (int)(headRect.Y + headRect.Height * .27f), eyeSize, eyeSize), UiTheme.Text);
        Primitives2D.FillRect(spriteBatch, new Rectangle((int)(headRect.Right - headRect.Width * .25f - eyeSize), (int)(headRect.Y + headRect.Height * .27f), eyeSize, eyeSize), UiTheme.Text);
        if (attack > .05f)
        {
            var muzzle = new Vector2(headRect.Center.X, headRect.Top - HeadSize * (.12f + attack * .16f));
            Primitives2D.FillPolygon(spriteBatch, new[]
            {
                muzzle + new Vector2(0, -HeadSize * .11f), muzzle + new Vector2(HeadSize * .08f, 0),
                muzzle + new Vector2(0, HeadSize * .11f), muzzle + new Vector2(-HeadSize * .08f, 0),
            }, UiTheme.Cream);
        }
        if (_segments.Count > 0)
        {
            var outline = headRect;
            outline.Inflate(8, 8);
            Primitives2D.RectOutline(spriteBatch, outline, UiTheme.Cream, Math.Max(2, (int)(HeadSize * .06f)));
        }
    }

    public override IReadOnlyList<(string Part, Rectangle Rect)> GetWorldHitboxes()
    {
        var hitboxes = new List<(string, Rectangle)> { ("head", new Rectangle((int)WorldX, (int)WorldY, (int)HeadSize, (int)HeadSize)) };
        hitboxes.AddRange(_segments.Select(segment =>
            (segment.Id, new Rectangle((int)segment.X, (int)segment.Y, (int)SegmentSize, (int)SegmentSize))));
        return hitboxes;
    }

    public override IReadOnlyList<(string Part, Rectangle Rect)> GetScreenHitboxes(
        Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        Vector2 headScreen = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        var hitboxes = new List<(string, Rectangle)>
        {
            ("head", new Rectangle((int)headScreen.X, (int)headScreen.Y, (int)HeadSize, (int)HeadSize)),
        };
        foreach (var segment in _segments)
        {
            Vector2 screenPos = camera.WorldToScreen(new Vector2(segment.X, segment.Y), playerWorldPosition, screenShake);
            hitboxes.Add((segment.Id, new Rectangle((int)screenPos.X, (int)screenPos.Y, (int)SegmentSize, (int)SegmentSize)));
        }
        return hitboxes;
    }

    public override HitResult TakeDamage(double amount, string partId = "head", DamageSource source = DamageSource.Direct)
    {
        int rounded = (int)Math.Round(amount);
        if (source == DamageSource.DamageOverTime)
        {
            Hp -= rounded;
            return new HitResult(true, Hp <= 0, rounded);
        }
        if (partId == "head")
        {
            if (_segments.Count > 0)
                return new HitResult(false, false, 0, true);
            Hp -= rounded;
            return new HitResult(true, Hp <= 0, rounded);
        }

        var segment = _segments.FirstOrDefault(item => item.Id == partId);
        if (segment is null)
            return new HitResult(false, false);
        segment.Hp -= rounded;
        if (segment.Hp <= 0)
            _segments.Remove(segment);
        return new HitResult(true, false, rounded);
    }
}
