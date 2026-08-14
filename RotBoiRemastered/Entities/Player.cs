using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
using RotBoiRemastered.Presentation;
using RotBoiRemastered.Systems;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Entities;

/// <summary>
/// The player entity: world position, movement/dash, and rendering. Ported
/// from character.py's movePlayer()/drawPlayer(). Most player-facing stats
/// (speed, dash timers, health) live on <see cref="RunState"/> rather than
/// here, since they're run-scoped data Player's movement/draw logic reads
/// and writes, not identity Player itself owns -- Player is deliberately
/// thin: just the position and the two methods that move/render it.
///
/// Cleanup vs. the Python original: `playerRect` (a screen-space rect at the
/// camera lock position, cached and updated every frame purely so drawPlayer
/// could read it) is gone -- Draw computes it fresh from `camera.Lock`
/// on demand instead of caching a value that's trivial to recompute.
///
/// Boss-specific arena constraints remain in GameSession so this entity stays
/// boss-agnostic. Move accepts keyboard directions plus an analog controller
/// vector and resolves shared wall/obstacle collision.
/// </summary>
public sealed class Player
{
    public float WorldX { get; private set; }
    public float WorldY { get; private set; }
    private float _visualAge;
    private float _fireRecoil;
    private bool _visualMoved;
    private Vector2 _visualMotionDirection = Vector2.UnitX;
    private Vector2 _visualAimDirection = -Vector2.UnitY;
    private Vector2 _lastVisualPosition;

    public Player(float worldX, float worldY)
    {
        WorldX = worldX;
        WorldY = worldY;
        _lastVisualPosition = new Vector2(worldX, worldY);
    }

    public void MarkFired() => _fireRecoil = 1f;

    internal void SetAimDirection(Vector2 screenDirection)
    {
        if (screenDirection.LengthSquared() <= .0001f)
            return;
        _visualAimDirection = Vector2.Normalize(screenDirection);
    }

    public void SetPosition(float worldX, float worldY)
    {
        WorldX = worldX;
        WorldY = worldY;
        _lastVisualPosition = new Vector2(worldX, worldY);
        _visualMoved = false;
    }

    /// <summary>
    /// Presentation-aware movement for post-collision constraints and scripted
    /// portal travel. Unlike <see cref="SetPosition"/>, this preserves the
    /// previous visual sample so the next presentation tick reads the actual
    /// on-screen displacement rather than snapping or animating a teleport.
    /// </summary>
    internal void SetAnimatedPosition(float worldX, float worldY)
    {
        WorldX = worldX;
        WorldY = worldY;
    }

    internal void AdvanceVisuals(double seconds)
    {
        float visualSeconds = (float)Math.Clamp(seconds, 0.0, .05);
        _visualAge += visualSeconds;
        _fireRecoil = Math.Max(0, _fireRecoil - visualSeconds * 8f);
        Vector2 current = new(WorldX, WorldY);
        Vector2 visualDelta = current - _lastVisualPosition;
        _visualMoved = visualDelta.LengthSquared() > .0004f;
        if (_visualMoved)
            _visualMotionDirection = Vector2.Normalize(visualDelta);
        _lastVisualPosition = current;
    }

    internal float PresentationTime => _visualAge;
    internal bool VisualMoved => _visualMoved;

    public Rectangle WorldRect(RunState state) => new((int)WorldX, (int)WorldY, (int)state.PlayerSize, (int)state.PlayerSize);

    /// <summary>
    /// World-space footprint of the square drawn at the camera lock. At zero
    /// camera rotation this is the ordinary WorldRect; at other angles it is
    /// the inverse-rotated screen-aligned square seen by the player.
    /// </summary>
    public Vector2[] WorldCollisionPolygon(RunState state, Camera camera, float? worldX = null, float? worldY = null)
    {
        float x = worldX ?? WorldX, y = worldY ?? WorldY;
        float half = (float)state.PlayerSize / 2f;
        var center = new Vector2(x + half, y + half);
        var screenOffsets = new[]
        {
            new Vector2(-half, -half), new Vector2(half, -half),
            new Vector2(half, half), new Vector2(-half, half),
        };
        return screenOffsets.Select(offset => center + camera.ScreenVectorToWorld(offset)).ToArray();
    }

    /// <summary>Ported from character.py's movePlayer().</summary>
    public void Move(RunState state, Battleground battleground, Camera camera,
        bool moveLeft, bool moveRight, bool moveUp, bool moveDown, bool dashPressed,
        IReadOnlyList<Rectangle>? obstacles = null, Vector2 controllerMove = default,
        bool useArenaBoundaryConstraint = false)
    {
        double seconds = Simulation.GetTimerStep() / Math.Max(1, Simulation.FrameRate);
        state.BossAfflictions.Update(seconds);
        state.DreamState.Update(seconds);

        float inputX = (moveLeft ? 1f : 0f) - (moveRight ? 1f : 0f);
        float inputY = (moveUp ? 1f : 0f) - (moveDown ? 1f : 0f);
        inputX -= controllerMove.X;
        inputY -= controllerMove.Y;
        // Match the Python movement rule: two active axes each receive 1/sqrt(2),
        // keeping diagonal travel the same speed as cardinal travel.
        float directionScale = (inputX != 0 && inputY != 0) ? 0.70710678f : 1.0f;
        inputX *= directionScale;
        inputY *= directionScale;
        // WASD stays relative to the monitor after a camera turn.
        var worldInput = camera.ScreenVectorToWorld(new Vector2(inputX, inputY));
        inputX = worldInput.X;
        inputY = worldInput.Y;

        if (dashPressed && state.CurrDashCooldown <= 0 && (inputX != 0 || inputY != 0))
        {
            state.Dashing = true;
            state.CurrDashCooldown = state.DashCooldownMax;
            state.FdX = inputX;
            state.FdY = inputY;
            state.PlayerInvulnerabilityTimer = Math.Max(state.PlayerInvulnerabilityTimer, state.DashDuration);
        }

        if (state.CurrDashCooldown > 0)
            state.CurrDashCooldown = Math.Max(0, state.CurrDashCooldown - Simulation.GetTimerStep());

        float frameScale = (float)Simulation.GetFrameScale();
        if (!state.Dashing)
        {
            float afflictionScale = (float)state.BossAfflictions.MovementMultiplier();
            state.DX = inputX * (float)state.PlayerSpeed * frameScale * afflictionScale;
            state.DY = inputY * (float)state.PlayerSpeed * frameScale * afflictionScale;
        }
        else
        {
            state.DX = state.FdX * (float)state.DashModifier * (float)state.PlayerSpeed * frameScale;
            state.DY = state.FdY * (float)state.DashModifier * (float)state.PlayerSpeed * frameScale;
            if (state.CurrDashCooldown <= state.DashCooldownMax - state.DashDuration)
                state.Dashing = false;
        }

        if (state.BossAfflictions.PullSource.HasValue && state.BossAfflictions.PullRemaining > 0 && !state.Dashing)
        {
            float playerCenterX = WorldX + state.PlayerSize / 2f, playerCenterY = WorldY + state.PlayerSize / 2f;
            var pullSource = state.BossAfflictions.PullSource.Value;
            float pullX = pullSource.X - playerCenterX, pullY = pullSource.Y - playerCenterY;
            float pullDistance = Math.Max(1.0f, MathF.Sqrt(pullX * pullX + pullY * pullY));
            float force = (float)state.BossAfflictions.Pull * frameScale;
            state.DX -= pullX / pullDistance * force;
            state.DY -= pullY / pullDistance * force;
        }

        float newAbsPosX = WorldX - state.DX;
        float newAbsPosY = WorldY - state.DY;

        float halfSize = (float)state.PlayerSize / 2f;
        float playerSize = (float)state.PlayerSize;
        Vector2 nextXAnchor = new Vector2(newAbsPosX + halfSize, WorldY + halfSize)
            + camera.ScreenVectorToWorld(new Vector2(-halfSize, -halfSize));
        if ((useArenaBoundaryConstraint
                || !battleground.ScreenAlignedRectangleHitsWall(nextXAnchor, playerSize, playerSize, camera))
            && !HitsObstacle(nextXAnchor, playerSize, camera, obstacles))
            WorldX = newAbsPosX;
        else
            state.DX = 0;

        Vector2 nextYAnchor = new Vector2(WorldX + halfSize, newAbsPosY + halfSize)
            + camera.ScreenVectorToWorld(new Vector2(-halfSize, -halfSize));
        if ((useArenaBoundaryConstraint
                || !battleground.ScreenAlignedRectangleHitsWall(nextYAnchor, playerSize, playerSize, camera))
            && !HitsObstacle(nextYAnchor, playerSize, camera, obstacles))
            WorldY = newAbsPosY;
        else
            state.DY = 0;

    }

    private static bool HitsObstacle(
        Vector2 worldAnchor,
        float size,
        Camera camera,
        IReadOnlyList<Rectangle>? obstacles)
    {
        if (obstacles is null)
            return false;
        for (int i = 0; i < obstacles.Count; i++)
        {
            if (Battleground.ScreenAlignedRectangleIntersectsRectangle(
                    worldAnchor, size, size, camera, obstacles[i]))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Ported from character.py's drawPlayer(). Draws at the camera's screen
    /// lock, not a world-transformed position. <paramref name="sizeScale"/>
    /// is a purely cosmetic render-time multiplier (e.g. SoulHub's portal
    /// pull-in shrink) -- it never touches RunState.PlayerSize, so collision/
    /// combat sizing is untouched and there's nothing to reset when a real
    /// run starts drawing at the default scale of 1.
    /// </summary>
    public void Draw(SpriteBatch spriteBatch, RunState state, Camera camera, float sizeScale = 1f)
    {
        Color color = ResolveBodyColor(state);
        float drawSize = state.PlayerSize * sizeScale;
        DrawCoreForgeRings(
            spriteBatch, state, camera.Lock, drawSize,
            _visualAge);

        int half = (int)Math.Round(drawSize / 2f);
        float step = _visualMoved
            ? MathF.Abs(MathF.Sin(_visualAge * 11f))
            : .5f + .5f * MathF.Sin(_visualAge * 2.2f);
        int squash = (int)MathF.Round(drawSize * (_visualMoved ? .06f : .018f) * step);
        Vector2 motionDirection =
            camera.WorldVectorToScreen(_visualMotionDirection);
        if (motionDirection.LengthSquared() > .0001f)
            motionDirection.Normalize();
        (Vector2 axisX, Vector2 axisY, Vector2 facing) =
            ScreenOrientation(_visualAimDirection);
        Vector2 recoil =
            -facing * MathF.Round(_fireRecoil * drawSize * .08f);
        var rect = new Rectangle(
            (int)(camera.Lock.X - half + recoil.X - squash / 2f),
            (int)(camera.Lock.Y - half + recoil.Y + squash),
            (int)drawSize + squash,
            Math.Max(4, (int)drawSize - squash));

        float intensity = (float)GameProfile.Profile.VisualEffectsIntensity;
        Vector2 bodyCenter = rect.Center.ToVector2();
        Vector2 P(float x, float y) =>
            bodyCenter
            + axisX * (x * rect.Width * .5f)
            + axisY * (y * rect.Height * .5f);
        PlayerRegaliaRenderer.DrawRear(
            spriteBatch, state, bodyCenter, axisX, axisY,
            drawSize, _visualAge, intensity);
        if (state.Dashing && intensity > 0)
        {
            int ghosts = Math.Max(1, (int)MathF.Ceiling(3 * intensity));
            for (int index = ghosts; index >= 1; index--)
            {
                Vector2 offset =
                    motionDirection * -index * drawSize * .28f;
                Primitives2D.FillQuad(spriteBatch,
                    P(-1, -1) + offset, P(1, -1) + offset,
                    P(1, 1) + offset, P(-1, 1) + offset,
                    state.PlayerEdgeColor * (.08f + (ghosts - index) * .06f));
            }
        }

        Vector2 worldShadow = axisX * 4f + axisY * 4f;
        Primitives2D.FillPolygonSpan(spriteBatch, stackalloc Vector2[]
        {
            P(-1, -1) + worldShadow, P(1, -1) + worldShadow,
            P(1, 1) + worldShadow, P(-1, 1) + worldShadow,
        }, UiTheme.Shadow);
        Span<Vector2> body = stackalloc Vector2[]
        {
            P(-1, -1), P(1, -1), P(1, 1), P(-1, 1),
        };
        Primitives2D.FillPolygonSpan(spriteBatch, body, color);
        Primitives2D.PolygonOutlineSpan(spriteBatch, body,
            ResolveEdgeColor(state), 3);
        Primitives2D.FillPolygonSpan(spriteBatch, stackalloc Vector2[]
        {
            P(0, -.18f), P(.18f, 0), P(0, .18f), P(-.18f, 0),
        }, UiTheme.Lighten(color, 45));
        PlayerRegaliaRenderer.DrawFront(
            spriteBatch, state, bodyCenter, axisX, axisY,
            drawSize, _visualAge);

        if (_fireRecoil > 0)
        {
            Vector2 muzzle = new Vector2(rect.Center.X, rect.Center.Y) + facing * drawSize * .7f;
            int muzzleSize = Math.Max(3, (int)(drawSize * .12f * _fireRecoil));
            Primitives2D.FillRect(spriteBatch,
                new Rectangle((int)muzzle.X - muzzleSize / 2,
                    (int)muzzle.Y - muzzleSize / 2, muzzleSize, muzzleSize),
                state.BulletEdgeColor);
        }
        if (state.HealthPoints < state.MaxHealthPoints * .25)
        {
            float warning = .35f + .25f * MathF.Sin(_visualAge * 7f);
            var warningRect = rect;
            warningRect.Inflate(4, 4);
            Primitives2D.RectOutline(spriteBatch, warningRect,
                UiTheme.Red * warning, 2);
        }
        if (state.BossAfflictions.Exposure > 0
            || state.BossAfflictions.SlowRemaining > 0
            || state.BossAfflictions.PullRemaining > 0)
        {
            Color affliction = state.BossAfflictions.PullRemaining > 0
                ? UiTheme.Purple
                : state.BossAfflictions.SlowRemaining > 0
                    ? UiTheme.Blue
                    : UiTheme.Gold;
            float strength = (float)Math.Clamp(
                .25 + state.BossAfflictions.Exposure * .06, .25, .85);
            var afflictionRect = rect;
            afflictionRect.Inflate(2, 2);
            Primitives2D.RectOutline(spriteBatch, afflictionRect,
                affliction * strength, 2);
            for (int index = 0; index < 3; index++)
            {
                float phase = _visualAge * (2.2f + index * .2f) + index * 2.1f;
                Vector2 mote = new(
                    rect.Center.X + MathF.Cos(phase) * drawSize * .65f,
                    rect.Center.Y + MathF.Sin(phase) * drawSize * .35f);
                Primitives2D.FillRect(spriteBatch,
                    new Rectangle((int)mote.X, (int)mote.Y, 3, 3),
                    affliction * strength);
            }
        }

        DrawHealthBar(spriteBatch, state, rect);
    }

    /// <summary>
    /// The player is a screen-space anchor while the world rotates beneath
    /// it. Aim is already supplied in screen space, so the body, regalia,
    /// recoil, and muzzle follow the cursor/right stick without inheriting
    /// camera yaw or changing collision geometry.
    /// </summary>
    internal static (
        Vector2 AxisX,
        Vector2 AxisY,
        Vector2 Facing) ScreenOrientation(Vector2 screenAim)
    {
        Vector2 facing = screenAim.LengthSquared() <= .0001f
            ? -Vector2.UnitY
            : Vector2.Normalize(screenAim);
        Vector2 axisY = -facing;
        Vector2 axisX = new(axisY.Y, -axisY.X);
        return (axisX, axisY, facing);
    }

    internal static Color ResolveBodyColor(RunState state)
    {
        // A dash grants a short mechanical invulnerability window, but that
        // window is not a damage reaction and must not replace the selected
        // core cosmetic with the hit-flash tint.
        bool damageFlash = state.PlayerInvulnerabilityTimer
                > state.DashDuration
            && (int)(state.PlayerInvulnerabilityTimer / 4) % 2 == 0;
        return damageFlash
            ? new Color(235, 245, 255)
            : state.PlayerColor;
    }

    internal static Color ResolveEdgeColor(RunState state) =>
        state.Dashing ? UiTheme.Cream : state.PlayerEdgeColor;

    internal static int HealthBarFillWidth(
        int barWidth,
        int health,
        int maximumHealth)
    {
        float ratio = Math.Clamp(
            health / (float)Math.Max(1, maximumHealth),
            0f, 1f);
        return (int)MathF.Round(Math.Max(0, barWidth) * ratio);
    }

    private static void DrawHealthBar(
        SpriteBatch spriteBatch,
        RunState state,
        Rectangle playerRect)
    {
        int width = Math.Max(28, playerRect.Width);
        const int height = 5;
        var bar = new Rectangle(
            playerRect.Center.X - width / 2,
            playerRect.Bottom + 7,
            width,
            height);
        Primitives2D.FillRect(spriteBatch, bar, UiTheme.Ink);
        var inner = new Rectangle(
            bar.X + 1, bar.Y + 1,
            Math.Max(0, bar.Width - 2),
            Math.Max(1, bar.Height - 2));
        int fillWidth = HealthBarFillWidth(
            inner.Width,
            state.HealthPoints,
            state.MaxHealthPoints);
        if (fillWidth > 0)
        {
            Color fill = state.HealthPoints
                <= state.MaxHealthPoints * .25
                ? UiTheme.Red
                : UiTheme.Green;
            Primitives2D.FillRect(
                spriteBatch,
                new Rectangle(
                    inner.X, inner.Y,
                    fillWidth, inner.Height),
                fill);
        }
        Primitives2D.RectOutline(
            spriteBatch, bar,
            UiTheme.Border * .9f, 1);
    }

    private static void DrawCoreForgeRings(
        SpriteBatch spriteBatch,
        RunState state,
        Vector2 center,
        float drawSize,
        float animationTime)
    {
        float pulse = .96f + .035f * MathF.Sin(animationTime * 3.85f);
        int coreIndex = 0;
        for (int pathIndex = 0; pathIndex < GamePaths.Paths.Count; pathIndex++)
        {
            string pathKey = GamePaths.Paths[pathIndex].Key;
            if (!Items.CoreForgesByPathKey.TryGetValue(pathKey, out var core))
                continue;
            bool equipped = false;
            foreach (ItemDrop? item in state.Equipment.Values)
            {
                if (item?.CoreForge == core.Key)
                {
                    equipped = true;
                    break;
                }
            }
            if (!equipped)
                continue;
            float radius = drawSize * Math.Max(.58f, .82f - coreIndex * .06f) * pulse;
            Color color = GamePaths.PathsByKey[pathKey].Accent;
            Primitives2D.CircleOutline(spriteBatch, center, radius, color * .88f, Math.Max(2, (int)(drawSize * .055f)));
            coreIndex++;
        }
    }
}
