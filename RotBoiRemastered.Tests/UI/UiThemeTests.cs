using Microsoft.Xna.Framework;
using RotBoiRemastered.UI;

namespace RotBoiRemastered.Tests.UI;

/// <summary>
/// Ported from tests/test_ui_scaling.py's DisplayScale coverage. The third
/// Python test (test_sidebar_remains_bounded_across_common_aspect_ratios)
/// depends on InformationSheet, which isn't ported yet -- port it alongside
/// UI/InformationSheet.cs.
/// </summary>
public class UiThemeTests
{
    [Fact]
    public void ReferenceResolution_UsesOneToOneUiScale()
    {
        Assert.Equal(1.0f, UiTheme.DisplayScale(1920, 1080));
    }

    [Fact]
    public void ScaleIsHeightLimited_OnUltrawideDisplays()
    {
        float standard = UiTheme.DisplayScale(1920, 1080);
        float ultrawide = UiTheme.DisplayScale(2560, 1080);
        Assert.Equal(standard, ultrawide);
    }

    [Fact]
    public void ScaleClampsToMinimum_OnVerySmallDisplays()
    {
        Assert.Equal(UiTheme.MinDisplayScale, UiTheme.DisplayScale(320, 240));
    }

    [Fact]
    public void ScaleClampsToMaximum_OnVeryLargeDisplays()
    {
        Assert.Equal(UiTheme.MaxDisplayScale, UiTheme.DisplayScale(7680, 4320));
    }

    [Theory]
    [InlineData(1280, 720, 11)]
    [InlineData(1280, 720, 8)]
    [InlineData(1920, 1080, 11)]
    [InlineData(1920, 1080, 8)]
    [InlineData(2560, 1440, 11)]
    [InlineData(2560, 1440, 8)]
    public void ProgressGeometry_HudBarsHaveVisibleFillAtCommonWindowAndFullscreenSizes(
        int screenWidth, int screenHeight, int referenceHeight)
    {
        float scale = UiTheme.DisplayScale(screenWidth, screenHeight);
        int barHeight = Math.Max(1, (int)Math.Round(referenceHeight * scale));
        var rect = new Rectangle(20, 30, Math.Max(1, (int)Math.Round(260 * scale)), barHeight);

        var (inner, fill) = UiTheme.ProgressGeometry(rect, .75f, scale);

        Assert.True(inner.Width > 0);
        Assert.True(inner.Height > 0);
        Assert.True(fill.Width > 0);
        Assert.Equal(inner.Height, fill.Height);
        Assert.True(rect.Contains(fill));
    }

    [Theory]
    [InlineData(-1f, 0)]
    [InlineData(0f, 0)]
    [InlineData(.5f, 48)]
    [InlineData(1f, 96)]
    [InlineData(2f, 96)]
    public void ProgressGeometry_ClampsFillRatio(float ratio, int expectedWidth)
    {
        var (_, fill) = UiTheme.ProgressGeometry(new Rectangle(0, 0, 100, 10), ratio, 1f);
        Assert.Equal(expectedWidth, fill.Width);
        Assert.True(fill.Height > 0);
    }
}
