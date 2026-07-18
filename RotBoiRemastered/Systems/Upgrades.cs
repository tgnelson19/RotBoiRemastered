using System.Globalization;

namespace RotBoiRemastered.Systems;

/// <summary>
/// Data and selection rules for the run's upgrade-card draft. Ported 1:1 from
/// upgrades.py, which deliberately has no rendering dependency -- keeping the
/// rules separate from the card renderer makes balance changes testable and
/// gives future shops/rewards/starting decks one shared source of truth.
/// </summary>
public sealed record UpgradeDefinition(
    string Name,
    string Category,
    double Additive,
    double Multiplicative,
    string Description);

public sealed record UpgradeCard(UpgradeDefinition Definition, string Rarity, string MathType)
{
    public string Name => Definition.Name;
}

public static class Upgrades
{
    // Python 3.7+ dicts preserve insertion order, which upgrades.py relies on
    // (tuple(RARITY_WEIGHTS) for the weighted-choice order, and it's the order
    // lootCrate.py uses to rank rarity severity). C# Dictionary doesn't make
    // that guarantee, so RarityOrder is the explicit single source of truth;
    // the two dictionaries below are for O(1) lookup by name only.
    public static readonly IReadOnlyList<string> RarityOrder =
        new[] { "Common", "Rare", "Epic", "Legendary", "Mythical" };

    public static readonly IReadOnlyDictionary<string, double> RarityMultipliers =
        new Dictionary<string, double>
        {
            ["Common"] = 1.0,
            ["Rare"] = 1.6,
            ["Epic"] = 2.4,
            ["Legendary"] = 4.0,
            ["Mythical"] = 7.0,
        };

    // Explicit probabilities are easier to reason about and tune than a chain
    // of independent "one in N" rolls. Sums to 100.
    public static readonly IReadOnlyDictionary<string, double> RarityWeights =
        new Dictionary<string, double>
        {
            ["Common"] = 69.0,
            ["Rare"] = 21.0,
            ["Epic"] = 7.0,
            ["Legendary"] = 2.5,
            ["Mythical"] = 0.5,
        };

    public static readonly IReadOnlyList<UpgradeDefinition> Definitions = new[]
    {
        new UpgradeDefinition("Defense", "survival", 8, 0.10, "Reduce incoming damage (maximum 90)"),
        new UpgradeDefinition("Health", "survival", 100, 0.10, "Increase current and maximum health"),
        new UpgradeDefinition("Vitality", "survival", 5, 0.12, "Recover health continuously"),
        new UpgradeDefinition("Bullet Pierce", "volley", 0.25, 0.12, "Shots pass through more foes"),
        new UpgradeDefinition("Bullet Count", "volley", 0.25, 0.12, "Fire additional projectiles"),
        new UpgradeDefinition("Spread Angle", "volley", 0.314159, 0.12, "Widen the firing arc"),
        new UpgradeDefinition("Attack Speed", "tempo", -1, -0.04, "Shorten time between attacks"),
        new UpgradeDefinition("Bullet Speed", "precision", 3, 0.18, "Shots reach targets sooner"),
        new UpgradeDefinition("Bullet Range", "precision", 75, 0.18, "Shots travel farther"),
        new UpgradeDefinition("Bullet Damage", "power", 25, 0.16, "Increase every hit"),
        new UpgradeDefinition("Bullet Size", "power", 4, 0.12, "Make shots easier to land"),
        new UpgradeDefinition("Player Speed", "survival", 0.2, 0.16, "Improve repositioning"),
        new UpgradeDefinition("Crit Chance", "critical", 0.08, 0.04, "Land critical hits more often"),
        new UpgradeDefinition("Crit Damage", "critical", 0.25, 0.12, "Critical hits deal more damage"),
        new UpgradeDefinition("Aura Size", "harvest", 8, 0.14, "Collect experience from farther away"),
        new UpgradeDefinition("Aura Strength", "harvest", 0.8, 0.14, "Pull experience in faster"),
        new UpgradeDefinition("Exp Multiplier", "harvest", 0.2, 0.16, "Gain more experience per foe"),
    };

    public static readonly IReadOnlyDictionary<string, UpgradeDefinition> DefinitionsByName =
        Definitions.ToDictionary(definition => definition.Name);

    /// <summary>Weighted pick matching Python's random.choices(items, weights=weights, k=1)[0].</summary>
    private static T WeightedChoice<T>(IReadOnlyList<T> items, IReadOnlyList<double> weights, Random rng)
    {
        double total = weights.Sum();
        double roll = rng.NextDouble() * total;
        double cumulative = 0;
        for (int i = 0; i < items.Count; i++)
        {
            cumulative += weights[i];
            if (roll < cumulative)
                return items[i];
        }
        return items[^1]; // floating-point rounding fallback, mirrors random.choices' own behavior
    }

    public static string RollRarity(Random? rng = null)
    {
        rng ??= Random.Shared;
        var weights = RarityOrder.Select(name => RarityWeights[name]).ToList();
        return WeightedChoice(RarityOrder, weights, rng);
    }

    private static Dictionary<string, int> CategoryCounts(IReadOnlyDictionary<string, int> upgradeTypeCounts)
    {
        var counts = new Dictionary<string, int>();
        foreach (var (name, count) in upgradeTypeCounts)
        {
            if (DefinitionsByName.TryGetValue(name, out var definition))
            {
                counts[definition.Category] = counts.GetValueOrDefault(definition.Category) + count;
            }
        }
        return counts;
    }

    /// <summary>
    /// Return distinct cards, gently weighted toward the run's existing synergies.
    /// The weighting is intentionally modest: a build becomes more coherent
    /// without making off-build pivots disappear. Always distinct stats.
    /// </summary>
    public static List<UpgradeCard> GenerateOffer(
        IReadOnlyDictionary<string, int>? upgradeTypeCounts = null, int count = 3, Random? rng = null)
    {
        rng ??= Random.Shared;
        var categoryCounts = CategoryCounts(upgradeTypeCounts ?? new Dictionary<string, int>());
        var available = new List<UpgradeDefinition>(Definitions);
        var cards = new List<UpgradeCard>();

        for (int i = 0; i < Math.Min(count, available.Count); i++)
        {
            var weights = available
                .Select(item => 1.0 + categoryCounts.GetValueOrDefault(item.Category) * 0.45)
                .ToList();
            var definition = WeightedChoice(available, weights, rng);
            available.Remove(definition);
            var mathType = WeightedChoice(
                new[] { "additive", "multiplicative" }, new[] { 0.62, 0.38 }, rng);
            cards.Add(new UpgradeCard(definition, RollRarity(rng), mathType));
        }

        return cards;
    }

    /// <summary>The value appended to the additive or multiplicative stat stack.</summary>
    public static double CardModifier(UpgradeCard card)
    {
        double rarity = RarityMultipliers[card.Rarity];
        return card.MathType == "additive"
            ? card.Definition.Additive * rarity
            : 1 + card.Definition.Multiplicative * rarity;
    }

    public static string FormatCardValue(UpgradeCard card)
    {
        double modifier = CardModifier(card);
        if (card.MathType == "additive")
        {
            if (card.Definition.Name == "Attack Speed")
                modifier *= -1;
            string formatted = modifier.ToString("G3", CultureInfo.InvariantCulture);
            string sign = modifier >= 0 ? "+" : "";
            return $"{sign}{formatted}";
        }
        double percent = (modifier - 1) * 100;
        if (card.Definition.Name == "Attack Speed")
            percent *= -1;
        return $"{(percent >= 0 ? "+" : "")}{percent.ToString("0.##", CultureInfo.InvariantCulture)}%";
    }
}
