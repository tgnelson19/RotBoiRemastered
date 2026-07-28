using RotBoiRemastered.Entities;
using RotBoiRemastered.Systems;

namespace RotBoiRemastered.Tests.Entities;

public sealed class TreasureChestTests
{
    [Fact]
    public void Constructor_RequiresAtLeastTwoGuaranteedItems()
    {
        var oneDrop = Items.GenerateDrops(1, new Random(1));
        Assert.Throws<ArgumentException>(() => new TreasureChest(0, 0, oneDrop));
    }

    [Fact]
    public void Constructor_PreservesMultiItemRewardAndUsesLargeFootprint()
    {
        var drops = Items.GenerateDrops(3, new Random(2));
        var chest = new TreasureChest(100, 200, drops);

        Assert.Equal(3, chest.Items.Count);
        Assert.True(chest.Size > new LootCrate(0, 0, drops).Size);
        Assert.Equal(100, chest.WorldRect().X);
        Assert.Equal(200, chest.WorldRect().Y);
    }

    [Fact]
    public void Constructor_PreservesOptionalSenseTheme()
    {
        var drops = Items.GenerateDrops(2, new Random(5));
        var chest = new TreasureChest(0, 0, drops, "phantasia");

        Assert.Equal("phantasia", chest.ThemeKey);
        Assert.Throws<ArgumentException>(() => new TreasureChest(0, 0, drops, "unknown"));
    }
}
