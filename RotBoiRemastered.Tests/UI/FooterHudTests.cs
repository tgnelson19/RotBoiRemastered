using Microsoft.Xna.Framework;
using RotBoiRemastered.Systems;
using RotBoiRemastered.UI;

namespace RotBoiRemastered.Tests.UI;

[Collection("GameProfileState")]
public sealed class FooterHudTests
{
    public static TheoryData<int, int, double, double> LayoutCases => new()
    {
        { 640, 360, UiTheme.MinGuiScale, UiTheme.MinTextScale },
        { 640, 360, UiTheme.MaxGuiScale, UiTheme.MaxTextScale },
        { 1280, 720, 1.0, 1.0 },
        { 1920, 1080, UiTheme.MaxGuiScale, UiTheme.MaxTextScale },
        { 2560, 1440, UiTheme.MinGuiScale, UiTheme.MinTextScale },
        { 3440, 1440, 1.0, 1.0 },
        { 1024, 768, UiTheme.MaxGuiScale, UiTheme.MaxTextScale },
    };

    [Theory]
    [MemberData(nameof(LayoutCases))]
    public void CalculateLayout_KeepsAllRequestedFooterRegionsOnScreen(
        int width, int height, double guiScale, double textScale)
    {
        GameProfile.Profile.GuiScale = guiScale;
        GameProfile.Profile.TextSize = textScale;
        float scale = UiTheme.DisplayScale(width, height);

        FooterLayout layout = FooterHud.CalculateLayout(width, height, scale);
        var screen = new Rectangle(0, 0, width, height);

        Assert.True(screen.Contains(layout.Bounds));
        Assert.True(layout.Bounds.Contains(layout.Health));
        Assert.True(layout.Bounds.Contains(layout.Equipment));
        Assert.True(layout.Bounds.Contains(layout.Resources));
        Assert.True(layout.Bounds.Contains(layout.Stats));
        Assert.True(layout.Bounds.Contains(layout.Experience));
        Assert.Equal(5, layout.EquipmentSlots.Count);
        Assert.Equal(3, layout.StatSlots.Count);
        Assert.All(layout.EquipmentSlots, slot => Assert.True(layout.Bounds.Contains(slot)));
        Assert.All(layout.StatSlots, slot => Assert.True(layout.Bounds.Contains(slot)));
        Assert.True(layout.Experience.Width >= layout.Bounds.Width * .9);
        Assert.True(FooterHud.SafeArea(width, height).Bottom <= layout.Bounds.Top + 12 * scale);
    }

    [Fact]
    public void MinimumWindow_UsesCompactTwoRowLayout()
    {
        FooterLayout layout = FooterHud.CalculateLayout(640, 360,
            UiTheme.DisplayScale(640, 360));

        Assert.True(layout.Compact);
        Assert.True(layout.Resources.Top >= layout.Health.Bottom);
    }
}
