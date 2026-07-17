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
/// Explicitly deferred: the boss-obstacle-avoidance and arena-radius
/// constraint branches in Python's movePlayer (`movement_obstacles`,
/// `constrain_player_position`, `arenaRadius`) -- there's no boss type to
/// provide them yet (see bossTypes.py in Entities/README.md's deferred
/// list). Movement here is plain wall collision only; boss arenas will need
/// to hook back in once that content exists. Controller input
/// (`vH.controllerMoveX/Y`) also isn't wired up (no controller state ported
/// yet) -- Move only takes keyboard-shaped directional booleans for now.
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

    /// <summary>Ported from character.py's movePlayer().</summary>
    public void Move(RunState state, Battleground battleground, Camera camera,
        bool moveLeft, bool moveRight, bool moveUp, bool moveDown, bool dashPressed)
    {
        double seconds = Simulation.GetTimerStep() / Math.Max(1, Simulation.FrameRate);
        state.BossAfflictions.Update(seconds);
        state.DreamState.Update(seconds);

        float inputX = (moveLeft ? 1f : 0f) - (moveRight ? 1f : 0f);
        float inputY = (moveUp ? 1f : 0f) - (moveDown ? 1f : 0f);
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

        var nextXRect = new Rectangle((int)newAbsPosX, (int)WorldY, (int)state.PlayerSize, (int)state.PlayerSize);
        if (!battleground.RectHitsWall(nextXRect))
            WorldX = newAbsPosX;
        else
            state.DX = 0;

        var nextYRect = new Rectangle((int)WorldX, (int)newAbsPosY, (int)state.PlayerSize, (int)state.PlayerSize);
        if (!battleground.RectHitsWall(nextYRect))
            WorldY = newAbsPosY;
        else
            state.DY = 0;
    }

    /// <summary>Ported from character.py's drawPlayer(). Draws at the camera's screen lock, not a world-transformed position.</summary>
    public void Draw(SpriteBatch spriteBatch, RunState state, Camera camera)
    {
        bool flashOn = state.PlayerInvulnerabilityTimer > 0 && (int)(state.PlayerInvulnerabilityTimer / 4) % 2 == 0;
        Color color = flashOn ? new Color(235, 245, 255) : state.PlayerColor;
        var rect = new Rectangle((int)camera.Lock.X, (int)camera.Lock.Y, (int)state.PlayerSize, (int)state.PlayerSize);

        Primitives2D.FillRect(spriteBatch, new Rectangle(rect.X + 4, rect.Y + 4, rect.Width, rect.Height), UiTheme.Shadow);
        Primitives2D.FillRect(spriteBatch, rect, color);
        Primitives2D.RectOutline(spriteBatch, rect, state.Dashing ? UiTheme.Cream : UiTheme.Ink, 3);
        var inset = rect;
        inset.Inflate(-(int)(state.PlayerSize * .42f), -(int)(state.PlayerSize * .42f));
        Primitives2D.FillRect(spriteBatch, inset, UiTheme.Lighten(color, 45));
    }
}
