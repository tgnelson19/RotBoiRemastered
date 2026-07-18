using System.Globalization;

namespace RotBoiRemastered.Systems;

/// <summary>A single equipment adjustment. Multipliers use 1.0 as neutral.</summary>
public sealed record ItemStatModifier(string Stat, double Additive = 0, double Multiplier = 1);

/// <summary>
/// Authored equipment archetype. VisualKind drives the deliberately generic
/// silhouette (dagger, sword, spear, bow, wand, vest, and so on) while the
/// modifiers keep all balance data out of rendering code.
/// </summary>
public sealed record ItemDefinition(
    string Name,
    string SlotType,
    string Description,
    string VisualKind,
    IReadOnlyList<ItemStatModifier> Modifiers,
    IReadOnlyDictionary<string, double>? StatusChances = null);

public sealed record ItemDrop(ItemDefinition Definition, string Rarity)
{
    public string Name => Definition.Name;
    public string SlotType => Definition.SlotType;
}

public sealed record ItemEffectView(string Stat, double Additive, double Multiplier)
{
    public string DisplayValue
    {
        get
        {
            double direction = Stat == "Attack Speed" ? -1 : 1;
            if (Math.Abs(Additive) > .0001)
            {
                double value = Additive * direction;
                return $"{(value >= 0 ? "+" : "")}{value.ToString("0.##", CultureInfo.InvariantCulture)}";
            }
            double percent = (Multiplier - 1) * 100 * direction;
            return $"{(percent >= 0 ? "+" : "")}{percent.ToString("0.##", CultureInfo.InvariantCulture)}%";
        }
    }

    public bool IsBeneficial => Stat == "Attack Speed"
        ? Additive < 0 || Multiplier < 1
        : Additive >= 0 && Multiplier >= 1;
}

public static class Items
{
    public const double MinBulletDamage = 20;
    public const double MaxBulletDamage = 700;
    public const double MinBulletRange = 60;
    public const double MaxBulletRange = 900;
    public const int MaxDefense = 90;

    public static readonly IReadOnlyList<string> SlotTypes =
        new[] { "weapon", "armor", "ring", "accessory" };

    private static ItemStatModifier Add(string stat, double value) => new(stat, Additive: value);
    private static ItemStatModifier Mult(string stat, double value) => new(stat, Multiplier: value);
    private static IReadOnlyList<ItemStatModifier> Mods(params ItemStatModifier[] modifiers) => modifiers;
    private static IReadOnlyDictionary<string, double> Status(string kind, double chance) =>
        new Dictionary<string, double> { [kind] = chance };

    /// <summary>
    /// Weapons intentionally span a broad damage/range axis:
    /// dagger -> sword -> spear -> bow -> wand. Material adjectives then
    /// bend tempo, control, or precision without erasing that identity.
    /// </summary>
    public static readonly IReadOnlyList<ItemDefinition> Definitions = new[]
    {
        new ItemDefinition("Iron Dagger", "weapon", "Close enough to hear the cut.", "dagger",
            Mods(Mult("Bullet Damage", 2.10), Mult("Bullet Range", .28), Mult("Attack Speed", .78))),
        new ItemDefinition("Bloody Dagger", "weapon", "It remembers every hand that slipped.", "dagger",
            Mods(Mult("Bullet Damage", 1.82), Mult("Bullet Range", .32), Mult("Attack Speed", .82)), Status("bleed", .20)),
        new ItemDefinition("Rusty Sword", "weapon", "The ruined edge asks for many swings.", "sword",
            Mods(Mult("Bullet Damage", .62), Mult("Bullet Range", .58), Mult("Attack Speed", .46))),
        new ItemDefinition("Iron Sword", "weapon", "A dependable answer at arm's length.", "sword",
            Mods(Mult("Bullet Damage", 1.55), Mult("Bullet Range", .62), Mult("Attack Speed", .94))),
        new ItemDefinition("Bloody Sword", "weapon", "Warm stains bead along the fuller.", "sword",
            Mods(Mult("Bullet Damage", 1.38), Mult("Bullet Range", .65), Mult("Attack Speed", .90)), Status("bleed", .16)),
        new ItemDefinition("Iron Spear", "weapon", "Distance, leverage, and one clean line.", "spear",
            Mods(Mult("Bullet Damage", 1.28), Mult("Bullet Range", 1.02), Mult("Attack Speed", 1.10), Add("Bullet Pierce", .60))),
        new ItemDefinition("Bone Spear", "weapon", "A pale point made for finding gaps.", "spear",
            Mods(Mult("Bullet Damage", 1.16), Mult("Bullet Range", 1.08), Add("Crit Chance", .08), Add("Bullet Pierce", .40))),
        new ItemDefinition("Hunting Bow", "weapon", "The string hums before danger arrives.", "bow",
            Mods(Mult("Bullet Damage", .92), Mult("Bullet Range", 1.72), Mult("Bullet Speed", 1.28), Mult("Attack Speed", .88))),
        new ItemDefinition("Yew Longbow", "weapon", "Patience drawn into a distant point.", "bow",
            Mods(Mult("Bullet Damage", .98), Mult("Bullet Range", 2.02), Mult("Bullet Speed", 1.42), Mult("Attack Speed", 1.18))),
        new ItemDefinition("Ash Wand", "weapon", "A faint ember reaches beyond the dark.", "wand",
            Mods(Mult("Bullet Damage", .72), Mult("Bullet Range", 2.55), Mult("Bullet Speed", 1.34), Mult("Bullet Size", .82))),
        new ItemDefinition("Glass Wand", "weapon", "Fragile light travels farther than courage.", "wand",
            Mods(Mult("Bullet Damage", .64), Mult("Bullet Range", 3.00), Mult("Bullet Speed", 1.55), Add("Crit Chance", .12))),

        new ItemDefinition("Leather Vest", "armor", "Scuffed hide that leaves room to breathe.", "vest",
            Mods(Add("Defense", 18), Mult("Player Speed", 1.08))),
        new ItemDefinition("Bloodstained Garb", "armor", "The cloth refuses to let another drop fall.", "vest",
            Mods(Add("Defense", 24), Add("Vitality", 12), Mult("Player Speed", 1.03))),
        new ItemDefinition("Chainmail", "armor", "Linked rings trade a little speed for certainty.", "chain",
            Mods(Add("Defense", 42), Mult("Player Speed", .92))),
        new ItemDefinition("Plate Armor", "armor", "A walking wall, heavy but never absolute.", "plate",
            Mods(Add("Defense", 76), Mult("Player Speed", .78))),
        new ItemDefinition("Rusty Plate", "armor", "Missing rivets make the old shell surprisingly nimble.", "plate",
            Mods(Add("Defense", 55), Mult("Player Speed", .90))),

        new ItemDefinition("Copper Ring", "ring", "A warm band that keeps the hands moving.", "band",
            Mods(Mult("Attack Speed", .88))),
        new ItemDefinition("Silver Band", "ring", "Cold metal steadies a hurried aim.", "band",
            Mods(Add("Crit Chance", .10), Mult("Bullet Speed", 1.12))),
        new ItemDefinition("Signet Ring", "ring", "A forgotten crest still carries authority.", "signet",
            Mods(Mult("Bullet Damage", 1.16), Add("Defense", 8))),
        new ItemDefinition("Thorn Ring", "ring", "Its tiny barbs promise that wounds linger.", "signet",
            Mods(Add("Crit Chance", .05)), Status("bleed", .08)),

        new ItemDefinition("Lucky Charm", "accessory", "Small enough to lose; stubborn enough to return.", "charm",
            Mods(Add("Crit Chance", .08), Mult("Exp Multiplier", 1.12))),
        new ItemDefinition("Old Locket", "accessory", "The portrait is gone, but the promise remains.", "locket",
            Mods(Add("Health", 120), Add("Vitality", 8))),
        new ItemDefinition("Traveler's Badge", "accessory", "Every scratch points toward another road.", "badge",
            Mods(Mult("Player Speed", 1.10), Add("Aura Size", 14))),
        new ItemDefinition("Venom Vial", "accessory", "A green drop waits behind thin glass.", "vial",
            Mods(Mult("Bullet Damage", .94)), Status("poison", .15)),
        new ItemDefinition("Frost Bell", "accessory", "Its silent note makes the world hesitate.", "bell",
            Mods(Mult("Bullet Range", 1.10)), Status("slow", .14)),
    };

    public static readonly IReadOnlyDictionary<string, ItemDefinition> DefinitionsByName =
        Definitions.ToDictionary(definition => definition.Name);

    private static readonly int[] DropCounts = { 0, 1, 2, 3, 4 };
    private static readonly double[] DropCountWeights = { 55, 25, 12, 6, 2 };

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
        return new ItemDrop(Definitions[rng.Next(Definitions.Count)], Upgrades.RollRarity(rng));
    }

    public static List<ItemDrop> GenerateDrops(int count, Random? rng = null)
    {
        rng ??= Random.Shared;
        return Enumerable.Range(0, count).Select(_ => GenerateDrop(rng)).ToList();
    }

    /// <summary>Rarity strengthens an item's identity without making drawbacks lethal.</summary>
    public static double RarityPower(string rarity) => rarity switch
    {
        "Common" => .65,
        "Rare" => .80,
        "Epic" => 1.00,
        "Legendary" => 1.15,
        "Mythical" => 1.30,
        _ => .65,
    };

    public static IReadOnlyList<ItemEffectView> Effects(ItemDrop drop)
    {
        double power = RarityPower(drop.Rarity);
        return drop.Definition.Modifiers.Select(modifier => new ItemEffectView(
            modifier.Stat,
            modifier.Additive * power,
            1 + (modifier.Multiplier - 1) * power)).ToList();
    }

    public static double AdjustStat(string stat, double value, IEnumerable<ItemDrop?> equipment)
    {
        foreach (var drop in equipment.Where(item => item is not null).Cast<ItemDrop>())
        {
            foreach (var effect in Effects(drop).Where(effect => effect.Stat == stat))
                value = (value + effect.Additive) * effect.Multiplier;
        }
        return stat switch
        {
            "Bullet Damage" => Math.Clamp(value, MinBulletDamage, MaxBulletDamage),
            "Bullet Range" => Math.Clamp(value, MinBulletRange, MaxBulletRange),
            "Defense" => Math.Clamp(value, 0, MaxDefense),
            "Attack Speed" => Math.Clamp(value, 5, 180),
            "Player Speed" => Math.Clamp(value, .8, 6.0),
            "Bullet Speed" => Math.Clamp(value, 1.0, 14.0),
            _ => value,
        };
    }

    public static IReadOnlyDictionary<string, double> StatusChances(IEnumerable<ItemDrop?> equipment)
    {
        var result = new Dictionary<string, double>();
        foreach (var drop in equipment.Where(item => item is not null).Cast<ItemDrop>())
        {
            if (drop.Definition.StatusChances is null)
                continue;
            double power = RarityPower(drop.Rarity);
            foreach (var (kind, chance) in drop.Definition.StatusChances)
                result[kind] = Math.Min(.65, result.GetValueOrDefault(kind) + chance * power);
        }
        return result;
    }

    public static StoredItemData Serialize(ItemDrop drop) => new(drop.Name, drop.Rarity);

    public static ItemDrop? Deserialize(StoredItemData? data) => data is not null && DefinitionsByName.TryGetValue(data.Name, out var definition)
        && Upgrades.RarityOrder.Contains(data.Rarity)
        ? new ItemDrop(definition, data.Rarity)
        : null;
}
