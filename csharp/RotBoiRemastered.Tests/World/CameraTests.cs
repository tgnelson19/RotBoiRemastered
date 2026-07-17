using Microsoft.Xna.Framework;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.World;

/// <summary>
/// Ported from tests/test_camera_rotation.py's coordinate-transform coverage.
/// The two rendering-geometry tests (_wall_screen_geometry/
/// _decoration_screen_rect) aren't ported here -- those belong to the raised-
/// scenery drawing pass, deferred alongside the rest of tile/wall rendering.
/// </summary>
public class CameraTests
{
    private static Camera NewCamera() => new() { Lock = Vector2.Zero };

    [Fact]
    public void QuarterTurnEast_ProjectsWorldRight_TowardScreenUp()
    {
        var camera = NewCamera();
        camera.SetQuarterTurns(1);
        var screen = camera.WorldToScreen(new Vector2(600, 650), new Vector2(500, 650), Vector2.Zero);
        Assert.Equal(0.0, (double)screen.X, precision: 4);
        Assert.Equal(-100.0, (double)screen.Y, precision: 4);
    }

    [Fact]
    public void QuarterTurnWest_ProjectsWorldRight_TowardScreenDown()
    {
        var camera = NewCamera();
        camera.SetQuarterTurns(-1);
        var screen = camera.WorldToScreen(new Vector2(600, 650), new Vector2(500, 650), Vector2.Zero);
        Assert.Equal(0.0, (double)screen.X, precision: 4);
        Assert.Equal(100.0, (double)screen.Y, precision: 4);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17.5)]
    [InlineData(45)]
    [InlineData(93)]
    [InlineData(181.25)]
    [InlineData(319)]
    public void ScreenAndWorldConversion_AreInversesAtEveryAngle(float angle)
    {
        var camera = NewCamera();
        camera.SetAngle(angle);
        var player = new Vector2(500, 650);
        var world = new Vector2(731.5f, 412.25f);

        var screen = camera.WorldToScreen(world, player, Vector2.Zero);
        var roundTripped = camera.ScreenToWorld(screen, player, Vector2.Zero);

        Assert.Equal((double)world.X, (double)roundTripped.X, precision: 3);
        Assert.Equal((double)world.Y, (double)roundTripped.Y, precision: 3);
    }

    [Fact]
    public void ScreenRelativeMovement_RotatesBackToWorld()
    {
        var camera = NewCamera();
        camera.SetQuarterTurns(1);
        // At the E orientation, screen-right lies along world-down.
        var world = camera.ScreenVectorToWorld(new Vector2(10, 0));
        Assert.Equal(0.0, (double)world.X, precision: 4);
        Assert.Equal(10.0, (double)world.Y, precision: 4);
    }

    [Fact]
    public void ArbitraryAngle_RotatesFluidly()
    {
        var camera = NewCamera();
        camera.SetAngle(45);
        var screen = camera.WorldToScreen(new Vector2(600, 650), new Vector2(500, 650), Vector2.Zero);
        Assert.Equal(70.710678, (double)screen.X, precision: 4);
        Assert.Equal(-70.710678, (double)screen.Y, precision: 4);
    }

    [Fact]
    public void DegreeRotation_WrapsCleanly()
    {
        var camera = NewCamera();
        camera.SetAngle(355);
        camera.Rotate(10);
        Assert.Equal(5.0, (double)camera.AngleDegrees, precision: 4);
    }

    [Fact]
    public void SetAngle_NormalizesNegativeDegrees()
    {
        var camera = NewCamera();
        camera.SetAngle(-10);
        Assert.Equal(350.0, (double)camera.AngleDegrees, precision: 4);
    }
}
