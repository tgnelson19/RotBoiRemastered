namespace RotBoiRemastered.Systems;

/// <summary>
/// Data and selection rules for enemy loot drops. Ported 1:1 from items.py,
/// which deliberately has no rendering dependency, matching Upgrades.cs.
/// Items are placeholder loot for now (name, slot, rarity) with no stat
/// effects -- that comes in a later milestone once the equip/unequip loop
/// feels good to play.
/// </summary>
public sealed record ItemDefinition(string Name, string SlotType, string Description);

public sealed record ItemDrop(ItemDefinition Definition, string Rarity)
{
    public string Name => Definition.Name;
    public string SlotType => Definition.SlotType;
}

public static class Items
{
    public static readonly IReadOnlyList<string> SlotTypes =
        new[] { "weapon", "armor", "ring", "accessory" };

    public static readonly IReadOnlyList<ItemDefinition> Definitions = new[]
    {
        new ItemDefinition("Rusty Sword", "weapon", "A worn blade, better than fists."),
        new ItemDefinition("Iron Dagger", "weapon", "Light and quick to swing."),
        new ItemDefinition("Hunting Bow", "weapon", "Favors distance over power."),
        new ItemDefinition("Leather Vest", "armor", "Simple protection, easy to move in."),
        new ItemDefinition("Chainmail", "armor", "Heavier, sturdier coverage."),
        new ItemDefinition("Plate Armor", "armor", "Slow but nearly impenetrable."),
        new ItemDefinition("Copper Ring", "ring", "A plain band, faintly warm."),
        new ItemDefinition("Silver Band", "ring", "Polished and cool to the touch."),
        new ItemDefinition("Signet Ring", "ring", "Marked with a stranger's crest."),
        new ItemDefinition("Lucky Charm", "accessory", "Small, worn smooth by handling."),
        new ItemDefinition("Old Locket", "accessory", "Hinges creak, but it still shuts."),
        new ItemDefinition("Traveler's Badge", "accessory", "A mark of distance covered."),
    };

    public static readonly IReadOnlyDictionary<string, ItemDefinition> DefinitionsByName =
        Definitions.ToDictionary(definition => definition.Name);

    // Sums to 100. Biased toward 0 so loot doesn't spam every kill -- tune freely.
    // Parallel arrays (not a Dictionary) so enumeration order -- and therefore
    // which RNG draw maps to which count -- is guaranteed and explicit.
    private static readonly int[] DropCounts = { 0, 1, 2, 3, 4 };
    private static readonly double[] DropCountWeights = { 55, 25, 12, 6, 2 };

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
        return items[^1];
    }

    public static int RollDropCount(Random? rng = null)
    {
        rng ??= Random.Shared;
        return WeightedChoice(DropCounts, DropCountWeights, rng);
    }

    public static ItemDrop GenerateDrop(Random? rng = null)
    {
        rng ??= Random.Shared;
        var definition = Definitions[rng.Next(Definitions.Count)];
        return new ItemDrop(definition, Upgrades.RollRarity(rng));
    }

    public static List<ItemDrop> GenerateDrops(int count, Random? rng = null)
    {
        rng ??= Random.Shared;
        var drops = new List<ItemDrop>(count);
        for (int i = 0; i < count; i++)
            drops.Add(GenerateDrop(rng));
        return drops;
    }
}
