using RotBoiRemastered.Core;
using RotBoiRemastered.Entities;
using RotBoiRemastered.Systems;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.Entities;

/// <summary>Ported from character.py's movePlayer() (boss-obstacle/arena-constraint branches deferred, see Player.cs).</summary>
[Collection("GameProfileState")]
public class PlayerTests
{
    private static RunState MakeState() => new();

    [Fact]
    public void Move_Left_DecreasesWorldX()
    {
        var player = new Player(125, 125);
        var state = MakeState();
        var battleground = EntityTestFixtures.SmallOpenRoom();
        var camera = new Camera();
        float startX = player.WorldX;

        player.Move(state, battleground, camera, moveLeft: true, moveRight: false, moveUp: false, moveDown: false, dashPressed: false);

        Assert.True(player.WorldX < startX);
    }

    [Fact]
    public void Move_Right_IncreasesWorldX()
    {
        var player = new Player(125, 125);
        var state = MakeState();
        var battleground = EntityTestFixtures.SmallOpenRoom();
        var camera = new Camera();
        float startX = player.WorldX;

        player.Move(state, battleground, camera, moveLeft: false, moveRight: true, moveUp: false, moveDown: false, dashPressed: false);

        Assert.True(player.WorldX > startX);
    }

    [Fact]
    public void Move_DiagonalUsesInverseSquareRootScale()
    {
        var player = new Player(125, 125);
        var state = MakeState();
        var battleground = EntityTestFixtures.SmallOpenRoom();
        var camera = new Camera();

        player.Move(state, battleground, camera, moveLeft: true, moveRight: false,
            moveUp: true, moveDown: false, dashPressed: false);

        float expectedAxisDelta = (float)(state.PlayerSpeed * Simulation.GetFrameScale() * 0.70710678);
        Assert.Equal(expectedAxisDelta, Math.Abs(state.DX), 3);
        Assert.Equal(expectedAxisDelta, Math.Abs(state.DY), 3);
    }

    [Fact]
    public void Move_DashPress_ConsumesCooldownAndSetsDashing()
    {
        var player = new Player(125, 125);
        var state = MakeState();
        var battleground = EntityTestFixtures.SmallOpenRoom();
        var camera = new Camera();

        player.Move(state, battleground, camera, moveLeft: true, moveRight: false, moveUp: false, moveDown: false, dashPressed: true);

        Assert.True(state.Dashing);
        // Matches Python: currDashCooldown is set to dashCooldownMax, then immediately
        // ticks down by one timer step in that same frame (movePlayer does both in order).
        Assert.Equal(state.DashCooldownMax - 1, state.CurrDashCooldown);
    }

    [Fact]
    public void Move_DashRequiresDirectionalInput()
    {
        var player = new Player(125, 125);
        var state = MakeState();
        var battleground = EntityTestFixtures.SmallOpenRoom();
        var camera = new Camera();

        player.Move(state, battleground, camera, moveLeft: false, moveRight: false, moveUp: false, moveDown: false, dashPressed: true);

        Assert.False(state.Dashing);
        Assert.Equal(0, state.CurrDashCooldown);
    }

    [Fact]
    public void Move_NeverEntersAWall()
    {
        var player = new Player(52, 125); // just inside the interior next to the left wall
        var state = MakeState();
        var battleground = EntityTestFixtures.SmallOpenRoom();
        var camera = new Camera();

        for (int i = 0; i < 50; i++)
            player.Move(state, battleground, camera, moveLeft: true, moveRight: false, moveUp: false, moveDown: false, dashPressed: false);

        Assert.False(battleground.RectHitsWall(player.WorldRect(state)));
    }

    [Fact]
    public void Move_RotatedCamera_NeverLetsVisibleCornersEnterWall()
    {
        Simulation.ResetForTests();
        var player = new Player(90, 125);
        var state = MakeState();
        var battleground = EntityTestFixtures.SmallOpenRoom();
        var camera = new Camera();
        camera.SetAngle(45);

        // At 45 degrees, screen-left + screen-down resolves to world-left.
        for (int i = 0; i < 60; i++)
        {
            player.Move(state, battleground, camera, moveLeft: true, moveRight: false,
                moveUp: false, moveDown: true, dashPressed: false);
            Assert.False(battleground.ConvexPolygonHitsWall(player.WorldCollisionPolygon(state, camera)));
        }

        // The rotated square must stop before its left visual corner crosses x=50.
        Assert.True(player.WorldX > Battleground.TileSize);
    }

    [Fact]
    public void CollisionPolygon_ProjectsAroundCameraLockCenter()
    {
        var player = new Player(100, 100);
        var state = MakeState();
        var camera = new Camera { Lock = new Microsoft.Xna.Framework.Vector2(400, 300) };
        camera.SetAngle(45);
        var playerCenter = new Microsoft.Xna.Framework.Vector2(
            player.WorldX + state.PlayerSize / 2f, player.WorldY + state.PlayerSize / 2f);

        var projected = player.WorldCollisionPolygon(state, camera)
            .Select(point => camera.WorldToScreen(point, playerCenter, Microsoft.Xna.Framework.Vector2.Zero))
            .ToArray();
        float half = state.PlayerSize / 2f;

        Assert.Equal(camera.Lock.X - half, projected.Min(point => point.X), 3);
        Assert.Equal(camera.Lock.X + half, projected.Max(point => point.X), 3);
        Assert.Equal(camera.Lock.Y - half, projected.Min(point => point.Y), 3);
        Assert.Equal(camera.Lock.Y + half, projected.Max(point => point.Y), 3);
    }

    [Fact]
    public void WorldRect_MatchesPositionAndPlayerSize()
    {
        var player = new Player(100, 200);
        var state = MakeState();
        var rect = player.WorldRect(state);
        Assert.Equal(100, rect.X);
        Assert.Equal(200, rect.Y);
        Assert.Equal((int)state.PlayerSize, rect.Width);
    }
}
