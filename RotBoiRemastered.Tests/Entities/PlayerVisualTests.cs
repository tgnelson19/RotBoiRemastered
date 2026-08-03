using Microsoft.Xna.Framework;
using RotBoiRemastered.Entities;
using RotBoiRemastered.Systems;
using RotBoiRemastered.UI;

namespace RotBoiRemastered.Tests.Entities;

public sealed class PlayerVisualTests
{
    [Fact]
    public void FixedScreenOrientation_AlwaysFacesNorth()
    {
        (Vector2 axisX, Vector2 axisY, Vector2 facing) =
            Player.FixedScreenOrientation();

        Assert.Equal(Vector2.UnitX, axisX);
        Assert.Equal(Vector2.UnitY, axisY);
        Assert.Equal(-Vector2.UnitY, facing);
    }

    [Fact]
    public void DashInvulnerability_DoesNotReplaceSelectedBodyColor()
    {
        var state = new RunState
        {
            Dashing = true,
            PlayerInvulnerabilityTimer = 9,
            DashDuration = 9,
        };

        Assert.Equal(state.PlayerColor, Player.ResolveBodyColor(state));
        Assert.Equal(UiTheme.Cream, Player.ResolveEdgeColor(state));

        state.Dashing = false;

        Assert.Equal(state.PlayerColor, Player.ResolveBodyColor(state));
        Assert.Equal(state.PlayerEdgeColor, Player.ResolveEdgeColor(state));
    }

    [Fact]
    public void DamageInvulnerability_PreservesHitFlash()
    {
        var state = new RunState
        {
            PlayerInvulnerabilityTimer = 32,
            DashDuration = 9,
        };

        Assert.Equal(
            new Color(235, 245, 255),
            Player.ResolveBodyColor(state));
    }

    [Theory]
    [InlineData(60, 100, 100, 60)]
    [InlineData(60, 50, 100, 30)]
    [InlineData(60, 0, 100, 0)]
    [InlineData(60, -10, 100, 0)]
    [InlineData(60, 140, 100, 60)]
    [InlineData(60, 10, 0, 60)]
    public void HealthBarFillIsClampedToTheAuthoredWidth(
        int width,
        int health,
        int maximum,
        int expected)
    {
        Assert.Equal(
            expected,
            Player.HealthBarFillWidth(width, health, maximum));
    }

    [Fact]
    public void PresentationClockAdvancesWhileTeleportsDoNotLatchMovement()
    {
        var player = new Player(10, 20);

        player.AdvanceVisuals(.025);
        Assert.Equal(.025f, player.PresentationTime, 5);
        Assert.False(player.VisualMoved);

        player.SetPosition(300, 400);
        player.AdvanceVisuals(.025);
        Assert.False(player.VisualMoved);

        player.SetAnimatedPosition(305, 400);
        player.AdvanceVisuals(.025);
        Assert.True(player.VisualMoved);
    }
}
