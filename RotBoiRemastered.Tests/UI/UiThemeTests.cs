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
}
