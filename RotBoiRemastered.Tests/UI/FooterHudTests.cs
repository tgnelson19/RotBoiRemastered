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

    [Theory]
    [MemberData(nameof(LayoutCases))]
    public void QuickLootLayout_RemainsOneItemTallAboveFooter(
        int width, int height, double guiScale, double textScale)
    {
        GameProfile.Profile.GuiScale = guiScale;
        GameProfile.Profile.TextSize = textScale;
        float scale = UiTheme.DisplayScale(width, height);
        FooterLayout footer = FooterHud.CalculateLayout(width, height, scale);

        QuickLootLayout quick = FooterHud.CalculateQuickLootLayout(footer, scale);
        var screen = new Rectangle(0, 0, width, height);

        Assert.True(screen.Contains(quick.Bounds));
        Assert.True(quick.Bounds.Bottom <= footer.Bounds.Top);
        Assert.Equal(InformationSheet.CrateSlotCount, quick.LootSlots.Count);
        Assert.Equal(InformationSheet.InventorySlotCount, quick.StashSlots.Count);
        Assert.All(quick.LootSlots.Concat(quick.StashSlots), slot =>
        {
            Assert.True(quick.Bounds.Contains(slot));
            Assert.Equal(quick.LootSlots[0].Y, slot.Y);
            Assert.Equal(quick.LootSlots[0].Height, slot.Height);
        });
    }

    [Fact]
    public void EquipmentTargetFor_AccessoryPrefersEmptySlotThenChosenSwapSlot()
    {
        var state = new RunState();
        var item = new ItemDrop(Items.DefinitionsByName["Lucky Charm"], "Common");

        Assert.Equal("accessory_1", FooterHud.EquipmentTargetFor(item, state, 1));
        state.Equipment["accessory_1"] = item;
        Assert.Equal("accessory_2", FooterHud.EquipmentTargetFor(item, state, 0));
        state.Equipment["accessory_2"] = item;
        Assert.Equal("accessory_2", FooterHud.EquipmentTargetFor(item, state, 1));
    }

    [Fact]
    public void QuickLootLayout_ShrinksToActualLootCount()
    {
        FooterLayout footer = FooterHud.CalculateLayout(1280, 720,
            UiTheme.DisplayScale(1280, 720));

        QuickLootLayout oneItem = FooterHud.CalculateQuickLootLayout(footer, 1, lootSlotCount: 1);
        QuickLootLayout fourItems = FooterHud.CalculateQuickLootLayout(footer, 1, lootSlotCount: 4);

        Assert.Single(oneItem.LootSlots);
        Assert.True(oneItem.Bounds.Width < fourItems.Bounds.Width);
        Assert.Equal(InformationSheet.InventorySlotCount, oneItem.StashSlots.Count);
    }
}
