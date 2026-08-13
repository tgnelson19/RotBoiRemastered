using RotBoiRemastered.Systems;

namespace RotBoiRemastered.Tests.Systems;

/// <summary>Ported from tests/test_upgrades.py.</summary>
public class UpgradesTests
{
    [Fact]
    public void WeakStandaloneCards_UseBuffedValues()
    {
        Assert.Equal(10, Upgrades.DefinitionsByName["Defense"].Additive);
        Assert.Equal(.12, Upgrades.DefinitionsByName["Defense"].Multiplicative);
        Assert.Equal(.30, Upgrades.DefinitionsByName["Player Speed"].Additive);
        Assert.Equal(.20, Upgrades.DefinitionsByName["Player Speed"].Multiplicative);
    }

    [Fact]
    public void CuratedBundles_RollOneToThreeOrderedEffectsAtExpectedRates()
    {
        var rng = new Random(2026);
        var counts = new int[4];
        int bundles = 0;
        for (int sample = 0; sample < 40_000; sample++)
            foreach (var card in Upgrades.GenerateOffer(count: 3, rng: rng).Where(card =>
                Upgrades.Bundles.Any(bundle => bundle.Key == card.BundleKey)))
            {
                bundles++;
                counts[card.Effects.Count]++;
                var definition = Upgrades.Bundles.Single(bundle => bundle.Key == card.BundleKey);
                Assert.Equal(definition.Stats.Take(card.Effects.Count), card.Effects.Select(effect => effect.Stat));
            }

        Assert.True(bundles > 10_000);
        Assert.InRange(counts[1] / (double)bundles, .53, .57);
        Assert.InRange(counts[2] / (double)bundles, .28, .32);
        Assert.InRange(counts[3] / (double)bundles, .13, .17);
    }

    [Theory]
    [InlineData("Common", 3, 1.10)]
    [InlineData("Legendary", 3, 1.50)]
    [InlineData("Mythical", 3, 1.70)]
    public void BundleBudget_DependsOnRarity(string rarity, int effects, double expected) =>
        Assert.Equal(expected, Upgrades.BundleBudget(rarity, effects));

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
        Assert.Equal(
            left.Select(CardFingerprint),
            right.Select(CardFingerprint));

        static string CardFingerprint(UpgradeCard card) =>
            $"{card.Name}|{card.Rarity}|{string.Join(';', card.Effects.Select(effect =>
                $"{effect.Stat}:{effect.MathType}:{effect.Strength}"))}";
    }

    [Fact]
    public void FormatCardValue_UsesPercentIncreaseInsteadOfMultiplierNotation()
    {
        var damage = new UpgradeCard(Upgrades.DefinitionsByName["Bullet Damage"], "Common", "multiplicative");
        var attackSpeed = new UpgradeCard(Upgrades.DefinitionsByName["Attack Speed"], "Common", "multiplicative");

        Assert.Equal("+16%", Upgrades.FormatCardValue(damage));
        Assert.Equal("+4%", Upgrades.FormatCardValue(attackSpeed));
    }

    [Fact]
    public void SpreadAngleCardsTightenRatherThanExpandTheVolley()
    {
        var definition = Upgrades.DefinitionsByName["Spread Angle"];
        var additive = new UpgradeCard(definition, "Common", "additive");
        var multiplicative = new UpgradeCard(definition, "Common", "multiplicative");

        Assert.True(Upgrades.CardModifier(additive) < 0);
        Assert.InRange(Upgrades.CardModifier(multiplicative), 0, .999999);
        Assert.Equal("-18°", Upgrades.FormatCardValue(additive));
        Assert.Equal("12% tighter", Upgrades.FormatCardValue(multiplicative));
        Assert.Contains("minimum 1°", definition.Description);
    }
}
