using RotBoiRemastered.Systems;

namespace RotBoiRemastered.Tests.Systems;

/// <summary>Ported from tests/test_upgrades.py.</summary>
public class UpgradesTests
{
    [Fact]
    public void GenerateOffer_ContainsThreeDistinctStats()
    {
        var cards = Upgrades.GenerateOffer(count: 3, rng: new Random(7));
        Assert.Equal(3, cards.Count);
        Assert.Equal(3, cards.Select(card => card.Name).Distinct().Count());
    }

    [Fact]
    public void CardModifier_UsesRarityAndMathType()
    {
        var definition = Upgrades.DefinitionsByName["Bullet Damage"];
        var additive = new UpgradeCard(definition, "Rare", "additive");
        var multiplicative = new UpgradeCard(definition, "Rare", "multiplicative");

        Assert.Equal(40, Upgrades.CardModifier(additive), precision: 6);
        Assert.Equal(1.256, Upgrades.CardModifier(multiplicative), precision: 6);
    }

    [Fact]
    public void SeededOffer_IsReproducible()
    {
        var left = Upgrades.GenerateOffer(count: 3, rng: new Random(42));
        var right = Upgrades.GenerateOffer(count: 3, rng: new Random(42));
        Assert.Equal(left, right);
    }
}
