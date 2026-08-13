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

public sealed record UpgradeEffect(UpgradeDefinition Definition, string MathType, double Strength = 1.0)
{
    public string Stat => Definition.Name;
}

public sealed record UpgradeBundleDefinition(
    string Key,
    string DisplayName,
    string Category,
    string Description,
    IReadOnlyList<string> Stats);

/// <summary>A draft choice containing one legacy standalone effect or a curated multi-stat bundle.</summary>
public sealed record UpgradeCard
{
    public string Name { get; init; }
    public string Category { get; init; }
    public string Description { get; init; }
    public string Rarity { get; init; }
    public string BundleKey { get; init; }
    public IReadOnlyList<UpgradeEffect> Effects { get; init; }

    // Compatibility surface for callers/tests that still construct and inspect single-stat cards.
    public UpgradeDefinition Definition => Effects[0].Definition;
    public string MathType => Effects[0].MathType;

    public UpgradeCard(UpgradeDefinition definition, string rarity, string mathType)
        : this(definition.Name, definition.Category, definition.Description, rarity, definition.Name,
            new[] { new UpgradeEffect(definition, mathType) }) { }

    public UpgradeCard(string name, string category, string description, string rarity, string bundleKey,
        IReadOnlyList<UpgradeEffect> effects)
    {
        if (effects.Count is < 1 or > 3)
            throw new ArgumentOutOfRangeException(nameof(effects), "Cards must contain one to three effects.");
        Name = name;
        Category = category;
        Description = description;
        Rarity = rarity;
        BundleKey = bundleKey;
        Effects = effects;
    }

    public bool Equals(UpgradeCard? other) => other is not null
        && Name == other.Name && Category == other.Category && Description == other.Description
        && Rarity == other.Rarity && BundleKey == other.BundleKey
        && Effects.SequenceEqual(other.Effects);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name); hash.Add(Category); hash.Add(Description); hash.Add(Rarity); hash.Add(BundleKey);
        foreach (var effect in Effects) hash.Add(effect);
        return hash.ToHashCode();
    }
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
        new UpgradeDefinition("Defense", "survival", 10, 0.12, "Reduce incoming damage (maximum 90)"),
        new UpgradeDefinition("Health", "survival", 100, 0.10, "Increase current and maximum health"),
        new UpgradeDefinition("Vitality", "survival", 5, 0.12, "Recover health continuously"),
        new UpgradeDefinition("Bullet Pierce", "volley", 0.25, 0.12, "Shots pass through more foes"),
        new UpgradeDefinition("Bullet Count", "volley", 0.25, 0.12, "Fire additional projectiles"),
        new UpgradeDefinition("Spread Angle", "volley", -0.314159, -0.12,
            "Tighten the firing arc (minimum 1° between shots)"),
        new UpgradeDefinition("Attack Speed", "tempo", -1, -0.04, "Shorten time between attacks"),
        new UpgradeDefinition("Bullet Speed", "precision", 3, 0.18, "Shots reach targets sooner"),
        new UpgradeDefinition("Bullet Range", "precision", 75, 0.18, "Shots travel farther"),
        new UpgradeDefinition("Bullet Damage", "power", 25, 0.16, "Increase every hit"),
        new UpgradeDefinition("Bullet Size", "power", 4, 0.12, "Make shots easier to land"),
        new UpgradeDefinition("Player Speed", "survival", 0.3, 0.20, "Improve repositioning"),
        new UpgradeDefinition("Crit Chance", "critical", 0.08, 0.04, "Land critical hits more often"),
        new UpgradeDefinition("Crit Damage", "critical", 0.25, 0.12, "Critical hits deal more damage"),
        new UpgradeDefinition("Aura Size", "harvest", 8, 0.14, "Collect experience from farther away"),
        new UpgradeDefinition("Aura Strength", "harvest", 0.8, 0.14, "Pull experience in faster"),
        new UpgradeDefinition("Exp Multiplier", "harvest", 0.2, 0.16, "Gain more experience per foe"),
    };

    public static readonly IReadOnlyDictionary<string, UpgradeDefinition> DefinitionsByName =
        Definitions.ToDictionary(definition => definition.Name);

    public static readonly IReadOnlyList<UpgradeBundleDefinition> Bundles = new[]
    {
        new UpgradeBundleDefinition("bulwark", "Bulwark", "survival", "Endure through layered resilience.",
            new[] { "Health", "Defense", "Vitality" }),
        new UpgradeBundleDefinition("skirmisher", "Skirmisher", "survival", "Move, brace, and recover between attacks.",
            new[] { "Player Speed", "Defense", "Vitality" }),
        new UpgradeBundleDefinition("sharpshooter", "Sharpshooter", "precision", "Make every distant shot arrive with purpose.",
            new[] { "Bullet Damage", "Bullet Speed", "Bullet Range" }),
        new UpgradeBundleDefinition("executioner", "Executioner", "critical", "Turn precision into decisive damage.",
            new[] { "Bullet Damage", "Crit Chance", "Crit Damage" }),
        new UpgradeBundleDefinition("storm", "Storm", "volley", "Build a faster and more concentrated volley.",
            new[] { "Bullet Count", "Attack Speed", "Spread Angle" }),
        new UpgradeBundleDefinition("siegebreaker", "Siegebreaker", "power", "Launch heavier shots through clustered foes.",
            new[] { "Bullet Damage", "Bullet Size", "Bullet Pierce" }),
        new UpgradeBundleDefinition("harvester", "Harvester", "harvest", "Gather more experience with less exposure.",
            new[] { "Exp Multiplier", "Aura Size", "Aura Strength" }),
    };

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
        var available = new List<(string Name, string Category, UpgradeDefinition? Single, UpgradeBundleDefinition? Bundle)>();
        available.AddRange(Definitions.Select(d => (d.Name, d.Category, (UpgradeDefinition?)d, (UpgradeBundleDefinition?)null)));
        available.AddRange(Bundles.Select(b => (b.DisplayName, b.Category, (UpgradeDefinition?)null, (UpgradeBundleDefinition?)b)));
        var cards = new List<UpgradeCard>();

        for (int i = 0; i < Math.Min(count, available.Count); i++)
        {
            var weights = available
                .Select(item => 1.0 + categoryCounts.GetValueOrDefault(item.Category) * 0.45)
                .ToList();
            var choice = WeightedChoice(available, weights, rng);
            available.Remove(choice);
            string rarity = RollRarity(rng);
            if (choice.Single is { } definition)
            {
                string mathType = RollMathType(rng);
                cards.Add(new UpgradeCard(definition, rarity, mathType));
            }
            else
            {
                cards.Add(CreateBundleCard(choice.Bundle!, rarity, rng));
            }
        }

        return cards;
    }

    private static string RollMathType(Random rng) =>
        WeightedChoice(new[] { "additive", "multiplicative" }, new[] { 0.62, 0.38 }, rng);

    private static UpgradeCard CreateBundleCard(UpgradeBundleDefinition bundle, string rarity, Random rng)
    {
        double roll = rng.NextDouble();
        int count = roll < .55 ? 1 : roll < .85 ? 2 : 3;
        double totalBudget = BundleBudget(rarity, count);
        // Earlier effects define the bundle identity, while this gentle descending split
        // prevents three offensive stats from each receiving a full standalone roll.
        double[] weights = count switch { 1 => [1], 2 => [.56, .44], _ => [.44, .33, .23] };
        var effects = bundle.Stats.Take(count).Select((stat, index) =>
            new UpgradeEffect(DefinitionsByName[stat], RollMathType(rng), totalBudget * weights[index])).ToList();
        return new UpgradeCard(bundle.DisplayName, bundle.Category, bundle.Description, rarity, bundle.Key, effects);
    }

    public static double BundleBudget(string rarity, int effectCount)
    {
        if (effectCount <= 1) return 1.0;
        return (rarity, effectCount) switch
        {
            ("Common", 2) => 1.05, ("Common", 3) => 1.10,
            ("Rare", 2) => 1.10, ("Rare", 3) => 1.20,
            ("Epic", 2) => 1.15, ("Epic", 3) => 1.30,
            ("Legendary", 2) => 1.25, ("Legendary", 3) => 1.50,
            ("Mythical", 2) => 1.35, ("Mythical", 3) => 1.70,
            _ => 1.0,
        };
    }

    /// <summary>The value appended to the additive or multiplicative stat stack.</summary>
    public static double CardModifier(UpgradeCard card)
        => EffectModifier(card.Rarity, card.Effects[0]);

    public static double EffectModifier(string rarityName, UpgradeEffect effect)
    {
        double rarity = RarityMultipliers[rarityName];
        return effect.MathType == "additive"
            ? effect.Definition.Additive * rarity * effect.Strength
            : 1 + effect.Definition.Multiplicative * rarity * effect.Strength;
    }

    public static string FormatCardValue(UpgradeCard card)
        => FormatEffectValue(card.Rarity, card.Effects[0]);

    public static string FormatEffectValue(string rarity, UpgradeEffect effect)
    {
        double modifier = EffectModifier(rarity, effect);
        if (effect.MathType == "additive")
        {
            if (effect.Definition.Name == "Attack Speed")
                modifier *= -1;
            if (effect.Definition.Name == "Spread Angle")
            {
                double degrees = modifier * 180 / Math.PI;
                return $"{degrees.ToString("0.##", CultureInfo.InvariantCulture)}°";
            }
            string formatted = modifier.ToString("G3", CultureInfo.InvariantCulture);
            string sign = modifier >= 0 ? "+" : "";
            return $"{sign}{formatted}";
        }
        double percent = (modifier - 1) * 100;
        if (effect.Definition.Name == "Attack Speed")
            percent *= -1;
        if (effect.Definition.Name == "Spread Angle")
            return $"{Math.Abs(percent).ToString("0.##", CultureInfo.InvariantCulture)}% tighter";
        return $"{(percent >= 0 ? "+" : "")}{percent.ToString("0.##", CultureInfo.InvariantCulture)}%";
    }
}
