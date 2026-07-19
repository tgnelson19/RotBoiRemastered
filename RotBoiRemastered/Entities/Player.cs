using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RotBoiRemastered.Core;
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

    public Player(float worldX, float worldY)
    {
        WorldX = worldX;
        WorldY = worldY;
    }

    public void SetPosition(float worldX, float worldY)
    {
        WorldX = worldX;
        WorldY = worldY;
    }

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
        IReadOnlyList<Rectangle>? obstacles = null, Vector2 controllerMove = default)
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

        var nextXPolygon = WorldCollisionPolygon(state, camera, newAbsPosX, WorldY);
        if (!battleground.ConvexPolygonHitsWall(nextXPolygon) && !HitsObstacle(nextXPolygon, obstacles))
            WorldX = newAbsPosX;
        else
            state.DX = 0;

        var nextYPolygon = WorldCollisionPolygon(state, camera, WorldX, newAbsPosY);
        if (!battleground.ConvexPolygonHitsWall(nextYPolygon) && !HitsObstacle(nextYPolygon, obstacles))
            WorldY = newAbsPosY;
        else
            state.DY = 0;
    }

    private static bool HitsObstacle(IReadOnlyList<Vector2> polygon, IReadOnlyList<Rectangle>? obstacles)
    {
        if (obstacles is null)
            return false;
        for (int i = 0; i < obstacles.Count; i++)
        {
            if (Battleground.ConvexPolygonIntersectsRectangle(polygon, obstacles[i]))
                return true;
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
        bool flashOn = state.PlayerInvulnerabilityTimer > 0 && (int)(state.PlayerInvulnerabilityTimer / 4) % 2 == 0;
        Color color = flashOn ? new Color(235, 245, 255) : state.PlayerColor;
        float drawSize = state.PlayerSize * sizeScale;
        int half = (int)Math.Round(drawSize / 2f);
        var rect = new Rectangle((int)camera.Lock.X - half, (int)camera.Lock.Y - half,
            (int)drawSize, (int)drawSize);

        Primitives2D.FillRect(spriteBatch, new Rectangle(rect.X + 4, rect.Y + 4, rect.Width, rect.Height), UiTheme.Shadow);
        Primitives2D.FillRect(spriteBatch, rect, color);
        Primitives2D.RectOutline(spriteBatch, rect, state.Dashing ? UiTheme.Cream : state.PlayerEdgeColor, 3);
        var inset = rect;
        inset.Inflate(-(int)(drawSize * .42f), -(int)(drawSize * .42f));
        Primitives2D.FillRect(spriteBatch, inset, UiTheme.Lighten(color, 45));
    }
}
