using RotBoiRemastered.Core;
using RotBoiRemastered.Entities;
using RotBoiRemastered.Systems;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

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

    [Fact]
    public void Size_IsTwiceAsLarge_WhenTheCrateHoldsAUniqueDrop()
    {
        var normal = new LootCrate(0, 0, new[] { Drop("Legendary") });
        var unique = new LootCrate(0, 0, new[] { new ItemDrop(Items.UniquesByName["Grimsbane"], "Unique") });

        Assert.Equal((int)(Simulation.TileSize * 0.6f), normal.WorldRect().Width);
        Assert.Equal(normal.Size * 2, unique.Size);
    }

    [Fact]
    public void ContainsUnique_TrueOnlyWhenAUniqueDropIsPresent()
    {
        // "Unique" isn't in Upgrades.RarityOrder (see Tint()'s ranking), so
        // this has to be checked independently rather than falling out of
        // the same rarity-order lookup.
        var withoutUnique = new LootCrate(0, 0, new[] { Drop("Legendary") });
        var withUnique = new LootCrate(0, 0, new[] { Drop("Common"), new ItemDrop(Items.UniquesByName["Grimsbane"], "Unique") });

        Assert.False(withoutUnique.ContainsUnique);
        Assert.True(withUnique.ContainsUnique);
    }

    [Fact]
    public void CoreForgedCrate_UsesItsPathAccentAndReportsSpecialContents()
    {
        var core = new ItemDrop(Items.DefinitionsByName["Iron Sword"], "Epic", "S", "Balanced", "rot");
        var crate = new LootCrate(0, 0, new[] { core, Drop("Legendary") });

        Assert.True(crate.ContainsCoreForged);
        Assert.Equal(GamePaths.PathsByKey["touch"].Accent, crate.CoreAccent);
        Assert.Equal(GamePaths.PathsByKey["touch"].Accent, crate.Tint());
    }
}
