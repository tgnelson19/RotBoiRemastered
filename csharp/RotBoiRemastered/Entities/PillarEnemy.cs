using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// Stationary pattern enemy that telegraphs jumps and alternating crosses.
/// Ported from enemyTypes.py's PillarEnemy. The Python original's
/// `find_spawn_rect(self.size)` fallback read `background.py`'s
/// `playerPosX`/`playerPosY` module globals implicitly -- here it takes the
/// player position already available as an EnemyUpdateContext field.
/// </summary>
public sealed class PillarEnemy : Enemy
{
    private readonly Random _rng;
    private string _state = "waiting";
    private float _stateTimer;
    private Vector2? _jumpTarget;
    private int _volleyIndex;

    public PillarEnemy(float worldX, float worldY, float speed, float size, Color color, double damage, double hp,
        double expValue, double difficulty, float awarenessRange, string archetype = "pillar",
        string difficultyTier = "easy", Random? rng = null)
        : base(worldX, worldY, speed, size, color, damage, hp, expValue, difficulty, awarenessRange, archetype, difficultyTier, rng)
    {
        _rng = rng ?? Random.Shared;
        _stateTimer = Simulation.FrameRate * (float)(_rng.NextDouble() * (1.4 - .8) + .8);
    }

    private void PickJumpTarget(float playerWorldX, float playerWorldY, Battleground battleground)
    {
        const float minimum = 4f;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            float angle = (float)(_rng.NextDouble() * 2 * Math.PI - Math.PI);
            float radius = Simulation.TileSize * (float)(_rng.NextDouble() * (10 - 5) + 5);
            var candidate = new Rectangle(
                (int)(WorldX + MathF.Cos(angle) * radius), (int)(WorldY + MathF.Sin(angle) * radius),
                (int)Size, (int)Size);
            var safe = battleground.FindNearestOpenRect(candidate);
            float centerX = safe.Center.X, centerY = safe.Center.Y;
            if (Vector2.Distance(new Vector2(centerX, centerY), new Vector2(playerWorldX, playerWorldY)) >= Simulation.TileSize * minimum)
            {
                _jumpTarget = new Vector2(safe.X, safe.Y);
                return;
            }
        }
        var fallback = battleground.FindSpawnRect((int)Size, new Vector2(playerWorldX, playerWorldY));
        _jumpTarget = new Vector2(fallback.X, fallback.Y);
    }

    private void FireCross(List<EnemyProjectile> projectileSink)
    {
        float centerX = WorldX + Size / 2f, centerY = WorldY + Size / 2f;
        float rotation = _volleyIndex % 2 == 0 ? 0f : MathF.PI / 4f;
        MarkAttack(.18f);
        int projectileCount = 2 + TierRank * 2;
        for (int index = 0; index < projectileCount; index++)
        {
            projectileSink.Add(new EnemyProjectile(
                centerX, centerY, rotation + index * 2f * MathF.PI / projectileCount,
                speed: 1.05f, damage: Damage * .55f, size: Size * .24f,
                travelRange: Simulation.TileSize * 20f, color: UiTheme.Gold,
                shape: "diamond", owner: "pillar_cross"));
        }
        _volleyIndex += 1;
    }

    public override void Update(EnemyUpdateContext context)
    {
        AdvanceAge();
        var battleground = context.Battleground;
        float distance = Vector2.Distance(
            new Vector2(context.PlayerWorldX, context.PlayerWorldY),
            new Vector2(WorldX + Size / 2f, WorldY + Size / 2f));
        bool aware = UpdateAwareness(distance);
        _stateTimer -= (float)Simulation.GetTimerStep();

        if (!aware)
        {
            if (_state != "waiting" && _state != "telegraph")
            {
                _state = "waiting";
                _stateTimer = Simulation.FrameRate * 1.5f;
                _volleyIndex = 0;
            }
            if (_stateTimer <= 0)
            {
                PickJumpTarget(context.PlayerWorldX, context.PlayerWorldY, battleground);
                _state = "telegraph";
                _stateTimer = Simulation.FrameRate * .7f;
            }
        }
        else if (_state == "waiting" && _stateTimer <= 0)
        {
            PickJumpTarget(context.PlayerWorldX, context.PlayerWorldY, battleground);
            _state = "telegraph";
            _stateTimer = Simulation.FrameRate * .7f;
        }
        else if (_state == "telegraph" && _stateTimer <= 0)
        {
            if (_jumpTarget.HasValue)
            {
                WorldX = _jumpTarget.Value.X;
                WorldY = _jumpTarget.Value.Y;
            }
            _jumpTarget = null;
            _volleyIndex = 0;
            _state = "landed";
            _stateTimer = Simulation.FrameRate * 1.0f;
        }
        else if (_state == "landed" && _stateTimer <= 0)
        {
            _state = "firing";
            _stateTimer = 0;
        }
        else if (_state == "firing" && _stateTimer <= 0)
        {
            FireCross(context.ProjectileSink);
            if (_volleyIndex >= 5 + TierRank)
            {
                _state = "waiting";
                _stateTimer = Simulation.FrameRate * .7f;
            }
            else
            {
                _stateTimer = Simulation.FrameRate * .48f;
            }
        }
        FinishMovementTracking();
    }

    public override void Draw(SpriteBatch spriteBatch, Camera camera, Vector2 playerWorldPosition, Vector2 screenShake)
    {
        if (_jumpTarget.HasValue)
        {
            Vector2 targetScreen = camera.WorldToScreen(_jumpTarget.Value, playerWorldPosition, screenShake);
            var target = new Rectangle((int)targetScreen.X, (int)targetScreen.Y, (int)Size, (int)Size);
            var inflated = target;
            inflated.Inflate(16, 16);
            Primitives2D.EllipseOutline(spriteBatch, inflated, UiTheme.Red, 4);
            Primitives2D.Line(spriteBatch, new Vector2(target.Center.X, target.Top), new Vector2(target.Center.X, target.Bottom), UiTheme.Cream, 2);
            Primitives2D.Line(spriteBatch, new Vector2(target.Left, target.Center.Y), new Vector2(target.Right, target.Center.Y), UiTheme.Cream, 2);
        }
        base.Draw(spriteBatch, camera, playerWorldPosition, screenShake);
        Vector2 screenPosition = camera.WorldToScreen(new Vector2(WorldX, WorldY), playerWorldPosition, screenShake);
        var rect = new Rectangle((int)screenPosition.X, (int)screenPosition.Y, (int)Size, (int)Size);
        var inset = rect;
        inset.Inflate(-(int)(Size * .45f), -(int)(Size * .14f));
        Primitives2D.FillRect(spriteBatch, inset, UiTheme.Gold);
    }
}
