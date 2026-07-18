using RotBoiRemastered.Core;
using RotBoiRemastered.Entities;
using RotBoiRemastered.Systems;
using RotBoiRemastered.UI;

namespace RotBoiRemastered.Tests.Entities;

/// <summary>Ported from tests/test_loot_crate.py's world-rect and rarity-tint coverage.</summary>
public class LootCrateTests
{
    private static ItemDrop Drop(string rarity) => new(Items.Definitions[0], rarity);

    [Fact]
    public void WorldRect_MatchesPositionAndSixtyPercentTileSize()
    {
        var crate = new LootCrate(120, 340, Array.Empty<ItemDrop>());
        var rect = crate.WorldRect();
        Assert.Equal(120, rect.X);
        Assert.Equal(340, rect.Y);
        Assert.Equal((int)(Simulation.TileSize * 0.6f), rect.Width);
        Assert.Equal((int)(Simulation.TileSize * 0.6f), rect.Height);
    }

    [Fact]
    public void EmptyCrate_TintsAsBorder()
    {
        var crate = new LootCrate(0, 0, Array.Empty<ItemDrop>());
        Assert.Equal(UiTheme.Border, crate.Tint());
    }

    [Fact]
    public void Tint_PicksTheHighestRarityColor()
    {
        var crate = new LootCrate(0, 0, new[] { Drop("Common"), Drop("Legendary") });
        Assert.Equal(UiTheme.RarityColors["Legendary"], crate.Tint());
    }
}
