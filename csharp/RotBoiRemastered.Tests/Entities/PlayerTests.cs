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
