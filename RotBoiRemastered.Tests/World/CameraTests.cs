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

    [Theory]
    [InlineData(0)]
    [InlineData(45)]
    [InlineData(137)]
    [InlineData(270)]
    public void PlayerWorldCenter_AlwaysMapsToCameraLock(float angle)
    {
        var camera = new Camera { Lock = new Vector2(400, 300) };
        camera.SetAngle(angle);
        var playerCenter = new Vector2(725, 525);

        var projected = camera.WorldToScreen(playerCenter, playerCenter, Vector2.Zero);

        Assert.Equal(camera.Lock.X, projected.X, 4);
        Assert.Equal(camera.Lock.Y, projected.Y, 4);
    }

    [Fact]
    public void Zoom_ClampsAndTransformsAroundPlayerLock()
    {
        var camera = new Camera { Lock = new Vector2(400, 300) };
        camera.SetZoom(99);
        Assert.Equal(Camera.MaxZoom, camera.Zoom);
        camera.SetZoom(1.5f);

        var display = camera.ApplyZoom(new Vector2(500, 300));
        Assert.Equal(new Vector2(550, 300), display);
        Assert.Equal(new Vector2(500, 300), camera.RemoveZoom(display));

        camera.SetZoom(0);
        Assert.Equal(Camera.MinZoom, camera.Zoom);
    }

    [Fact]
    public void ZoomingOut_ExpandsLogicalWorldViewport()
    {
        var camera = new Camera { Lock = new Vector2(400, 300) };
        camera.SetZoom(.8f);

        var logical = camera.LogicalViewport(new Rectangle(0, 0, 800, 600));

        Assert.True(logical.Width > 800);
        Assert.True(logical.Height > 600);
        Assert.True(logical.Contains(camera.Lock.ToPoint()));
    }

    [Theory]
    [InlineData(1280, 720, 0.6666667f)]
    [InlineData(1920, 1080, 1f)]
    [InlineData(3200, 2000, 1.6666667f)]
    [InlineData(3840, 2160, 2f)]
    public void DefaultZoom_MatchesViewportResolution(int width, int height, float expected)
    {
        Assert.Equal(expected, Camera.DefaultZoomForViewport(width, height), precision: 4);
    }

    [Fact]
    public void Resize_PreservesManualZoomRelativeToNewDefault()
    {
        var camera = NewCamera();
        camera.ConfigureViewport(1920, 1080, 1.0, resetZoom: true);
        camera.AdjustZoom(.3f);

        camera.ConfigureViewport(3200, 2000, 1.0);

        Assert.Equal(1.3f * (5f / 3f), camera.Zoom, precision: 4);
    }

    [Fact]
    public void ResetView_RestoresAngleAndResolutionAwareDefaultZoom()
    {
        var camera = NewCamera();
        camera.ConfigureViewport(3200, 2000, 1.2, resetZoom: true);
        camera.Rotate(90);
        camera.SetZoom(Camera.MaxZoom);

        camera.ResetView();

        Assert.Equal(0, camera.AngleDegrees);
        Assert.Equal(camera.DefaultZoom, camera.Zoom);
        Assert.Equal(2f, camera.Zoom, precision: 4);
    }
}
