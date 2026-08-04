using Microsoft.Xna.Framework;
using RotBoiRemastered.UI;

namespace RotBoiRemastered.Tests.UI;

public sealed class SettingsMenuTests
{
    [Theory]
    [InlineData(100, .85)]
    [InlineData(300, 2.0)]
    [InlineData(200, 1.425)]
    public void TextSizeSliderMapsItsFullTrackToTheSupportedRange(
        int mouseX, double expected)
    {
        var row = new Rectangle(88, 40, 224, 46);

        double value = SettingsMenu.TextSizeForSliderPosition(mouseX, row, 1f);

        Assert.Equal(expected, value, 3);
    }
}
