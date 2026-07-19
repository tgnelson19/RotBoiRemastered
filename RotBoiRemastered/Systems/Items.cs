using System.Globalization;

namespace RotBoiRemastered.Systems;

/// <summary>A single equipment adjustment. Multipliers use 1.0 as neutral.</summary>
public sealed record ItemStatModifier(string Stat, double Additive = 0, double Multiplier = 1);

/// <summary>
/// Authored equipment archetype. VisualKind drives the deliberately generic
/// silhouette (dagger, sword, spear, bow, wand, vest, and so on) while the
/// modifiers keep all balance data out of rendering code.
///
/// EffectIds/DropsFromBossKey/DropChance are only set on entries in
/// <see cref="Items.Uniques"/> (null/default for every regular Definitions
/// entry): EffectIds names zero or more bespoke on-hit behaviors dispatched
/// by UniqueEffects.OnPlayerHit (see its doc comment for why that's a
/// separate hook rather than another StatusChances entry) -- a weapon can
/// list more than one to stack independent effects (e.g. a crowd-control
/// proc and a sustain proc) on the same item, each added as its own case
/// with no knowledge of the others. DropsFromBossKey ties the drop to one
/// specific boss kill rather than the regular loot table, and DropChance is
/// that unique's own independent per-kill odds (see Items.RollUniqueDrop)
/// -- multiple uniques can share a DropsFromBossKey, each with its own
/// DropChance, which is what makes that boss's effective drop table.
/// </summary>
public sealed record ItemDefinition(
    string Name,
    string SlotType,
    string Description,
    string VisualKind,
    IReadOnlyList<ItemStatModifier> Modifiers,
    IReadOnlyDictionary<string, double>? StatusChances = null,
    IReadOnlyList<string>? EffectIds = null,
    string? DropsFromBossKey = null,
    double DropChance = .12);

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
            if (Math.Abs(Additive) > .0001)
            {
                double direction = Stat == "Attack Speed" ? -1 : 1;
                double value = Additive * direction;
                return $"{(value >= 0 ? "+" : "")}{value.ToString("0.##", CultureInfo.InvariantCulture)}";
            }
            // Multiplier is a cooldown ratio for Attack Speed (smaller means
            // a shorter cooldown, i.e. faster attacks) -- invert it to the
            // actual speed ratio before expressing a percent, so a halved
            // cooldown (Multiplier .5, from Items.Mult("Attack Speed", 200))
            // reads as "+100%" (twice the attack rate) instead of the raw,
            // mathematically wrong "+50%" you'd get by applying the same
            // percent-off-1.0 formula every other stat uses directly to a
            // cooldown ratio instead of a rate.
            double speedRatio = Stat == "Attack Speed" ? 1.0 / Multiplier : Multiplier;
            double percent = (speedRatio - 1) * 100;
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
    /// <summary>
    /// Takes a percentage, not a raw ratio -- Mult("Bullet Range", 78) means
    /// 78% (0.78x). "Attack Speed" is the one exception: it's stored as a
    /// frame-count cooldown internally (see RunState.AttackCooldownStat),
    /// where a *smaller* ratio means a shorter cooldown and therefore
    /// *faster* attacks -- backwards from what "200 attack speed" should
    /// intuitively mean. So Attack Speed alone takes the reciprocal
    /// (100/percent) instead of percent/100, making Mult("Attack Speed", 200)
    /// mean "attacks twice as fast" like every other stat's bigger-is-better
    /// convention. ItemEffectView.DisplayValue un-inverts this same way when
    /// showing the tooltip percentage -- if you ever touch one of these two,
    /// touch the other, or they'll silently disagree (this has already
    /// happened once: this method reverted to the plain percent/100 form
    /// while DisplayValue still expected the inverted ratio, which made
    /// Mult("Attack Speed", 200) display as -50% instead of +100%).
    /// </summary>
    private static ItemStatModifier Mult(string stat, double percent) =>
        new(stat, Multiplier: stat == "Attack Speed" ? 100.0 / percent : percent / 100.0);
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
            Mods(Mult("Bullet Damage", 210), Mult("Bullet Range", 28), Mult("Attack Speed", 128))),
        new ItemDefinition("Bloody Dagger", "weapon", "It remembers every hand that slipped.", "dagger",
            Mods(Mult("Bullet Damage", 182), Mult("Bullet Range", 32), Mult("Attack Speed", 122)), Status("bleed", .20)),
        new ItemDefinition("Rusty Sword", "weapon", "The ruined edge asks for many swings.", "sword",
            Mods(Mult("Bullet Damage", 62), Mult("Bullet Range", 58), Mult("Attack Speed", 217))),
        new ItemDefinition("Iron Sword", "weapon", "A dependable answer at arm's length.", "sword",
            Mods(Mult("Bullet Damage", 155), Mult("Bullet Range", 62), Mult("Attack Speed", 106))),
        new ItemDefinition("Bloody Sword", "weapon", "Warm stains bead along the fuller.", "sword",
            Mods(Mult("Bullet Damage", 138), Mult("Bullet Range", 65), Mult("Attack Speed", 111)), Status("bleed", .16)),
        new ItemDefinition("Iron Spear", "weapon", "Distance, leverage, and one clean line.", "spear",
            Mods(Mult("Bullet Damage", 128), Mult("Bullet Range", 102), Mult("Attack Speed", 91), Add("Bullet Pierce", .60))),
        new ItemDefinition("Bone Spear", "weapon", "A pale point made for finding gaps.", "spear",
            Mods(Mult("Bullet Damage", 116), Mult("Bullet Range", 108), Add("Crit Chance", .08), Add("Bullet Pierce", .40))),
        new ItemDefinition("Hunting Bow", "weapon", "The string hums before danger arrives.", "bow",
            Mods(Mult("Bullet Damage", 92), Mult("Bullet Range", 172), Mult("Bullet Speed", 128), Mult("Attack Speed", 114))),
        new ItemDefinition("Yew Longbow", "weapon", "Patience drawn into a distant point.", "bow",
            Mods(Mult("Bullet Damage", 98), Mult("Bullet Range", 202), Mult("Bullet Speed", 142), Mult("Attack Speed", 85))),
        new ItemDefinition("Ash Wand", "weapon", "A faint ember reaches beyond the dark.", "wand",
            Mods(Mult("Bullet Damage", 72), Mult("Bullet Range", 255), Mult("Bullet Speed", 134), Mult("Bullet Size", 82))),
        new ItemDefinition("Glass Wand", "weapon", "Fragile light travels farther than courage.", "wand",
            Mods(Mult("Bullet Damage", 64), Mult("Bullet Range", 300), Mult("Bullet Speed", 155), Add("Crit Chance", .12))),

        new ItemDefinition("Leather Vest", "armor", "Scuffed hide that leaves room to breathe.", "vest",
            Mods(Add("Defense", 18), Mult("Player Speed", 108))),
        new ItemDefinition("Bloodstained Garb", "armor", "The cloth refuses to let another drop fall.", "vest",
            Mods(Add("Defense", 24), Add("Vitality", 12), Mult("Player Speed", 103))),
        new ItemDefinition("Chainmail", "armor", "Linked rings trade a little speed for certainty.", "chain",
            Mods(Add("Defense", 42), Mult("Player Speed", 92))),
        new ItemDefinition("Plate Armor", "armor", "A walking wall, heavy but never absolute.", "plate",
            Mods(Add("Defense", 76), Mult("Player Speed", 78))),
        new ItemDefinition("Rusty Plate", "armor", "Missing rivets make the old shell surprisingly nimble.", "plate",
            Mods(Add("Defense", 55), Mult("Player Speed", 90))),

        new ItemDefinition("Copper Ring", "ring", "A warm band that keeps the hands moving.", "band",
            Mods(Mult("Attack Speed", 114))),
        new ItemDefinition("Silver Band", "ring", "Cold metal steadies a hurried aim.", "band",
            Mods(Add("Crit Chance", .10), Mult("Bullet Speed", 112))),
        new ItemDefinition("Signet Ring", "ring", "A forgotten crest still carries authority.", "signet",
            Mods(Mult("Bullet Damage", 116), Add("Defense", 8))),
        new ItemDefinition("Thorn Ring", "ring", "Its tiny barbs promise that wounds linger.", "signet",
            Mods(Add("Crit Chance", .05)), Status("bleed", .08)),

        new ItemDefinition("Lucky Charm", "accessory", "Small enough to lose; stubborn enough to return.", "charm",
            Mods(Add("Crit Chance", .08), Mult("Exp Multiplier", 112))),
        new ItemDefinition("Old Locket", "accessory", "The portrait is gone, but the promise remains.", "locket",
            Mods(Add("Health", 120), Add("Vitality", 8))),
        new ItemDefinition("Traveler's Badge", "accessory", "Every scratch points toward another road.", "badge",
            Mods(Mult("Player Speed", 110), Add("Aura Size", 14))),
        new ItemDefinition("Venom Vial", "accessory", "A green drop waits behind thin glass.", "vial",
            Mods(Mult("Bullet Damage", 94)), Status("poison", .15)),
        new ItemDefinition("Frost Bell", "accessory", "Its silent note makes the world hesitate.", "bell",
            Mods(Mult("Bullet Range", 110)), Status("slow", .14)),
    };

    public static readonly IReadOnlyDictionary<string, ItemDefinition> DefinitionsByName =
        Definitions.ToDictionary(definition => definition.Name);

    /// <summary>
    /// Fixed-stat named items (see ItemDefinition's doc comment) -- never
    /// rolled by GenerateDrop/GenerateDrops, never rarity-scaled (RarityPower
    /// treats "Unique" as a full, un-diminished 1.0), only obtainable via
    /// RollUniqueDrop when the boss named in DropsFromBossKey is defeated.
    /// </summary>
    public static readonly IReadOnlyList<ItemDefinition> Uniques = new[]
    {

        //Template for new unique items -- list one or more EffectIds to stack independent effects on the same item (see UniqueEffects.OnPlayerHit):
        /*
        new ItemDefinition("Unique Name", "weapon/armor/ring/accessory", "Flavor text.",
            "type_visual (vial/bow/dagger/bell/badge/etc.)", Mods(Mult("Bullet Damage", 100), Mult("Bullet Range", 100), Mult("Bullet Speed", 100)),
            EffectIds: new[] { "custom_effect_name", "second_effect_name" }, DropsFromBossKey: "boss_key", DropChance: .12),
        */

        new ItemDefinition("Bow of Dread", "weapon", "Every arrow carries a whisper of Dread, leaving struck enemies slowed and exposed -- and the bow itself feeds on the fear it causes.",
            "bow", Mods(Mult("Bullet Damage", 135), Mult("Bullet Range", 185), Mult("Bullet Speed", 120)),
            EffectIds: new[] { "dread_on_hit", "dread_lifesteal" }, DropsFromBossKey: "sting", DropChance: .12),

        new ItemDefinition("Grimsbane", "weapon",
            "Darkness clings to the rigid bones, and the shadows it strikes shiver in fear. Every hit marks its target with Bane, a stacking curse that leaves it ever more exposed.",
            "bow", Mods(Mult("Bullet Damage", 50), Mult("Bullet Range", 200), Mult("Attack Speed", 200)), Status("bleed", .05),
            EffectIds: new[] { "bane_on_hit" }, DropsFromBossKey: "dissonance", DropChance: .12),

    };

    public static readonly IReadOnlyDictionary<string, ItemDefinition> UniquesByName =
        Uniques.ToDictionary(unique => unique.Name);

    /// <summary>
    /// Rolls every unique tied to this boss key independently against its
    /// own DropChance -- that's the boss's drop table. See
    /// GameSession.HandleDamagingEnemies' boss-defeat branch, which
    /// guarantees a winning roll a loot crate slot regardless of the regular
    /// RollDropCount roll. Candidates are shuffled first so, on the rare
    /// kill where more than one entry wins its roll, it's not always the
    /// first-declared one that gets returned -- RollUniqueDrop only ever
    /// hands back one item per kill even if several uniques are eligible.
    /// </summary>
    public static ItemDrop? RollUniqueDrop(string bossKey, Random? rng = null)
    {
        rng ??= Random.Shared;
        var candidates = Uniques.Where(unique => unique.DropsFromBossKey == bossKey).OrderBy(_ => rng.NextDouble());
        foreach (var candidate in candidates)
        {
            if (rng.NextDouble() <= candidate.DropChance)
                return new ItemDrop(candidate, "Unique");
        }
        return null;
    }

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

    /// <summary>Rarity strengthens an item's identity without making drawbacks lethal. "Unique" is fixed at a full, un-diminished 1.0 -- see Items.Uniques' doc comment.</summary>
    public static double RarityPower(string rarity) => rarity switch
    {
        "Common" => .65,
        "Rare" => .80,
        "Epic" => 1.00,
        "Legendary" => 1.15,
        "Mythical" => 1.30,
        "Unique" => 1.00,
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

    /// <summary>Checked by name against Uniques first (their stored Rarity is always "Unique", which Upgrades.RarityOrder deliberately doesn't contain) before falling back to the regular, tiered-rarity-validated lookup.</summary>
    public static ItemDrop? Deserialize(StoredItemData? data)
    {
        if (data is null)
            return null;
        if (UniquesByName.TryGetValue(data.Name, out var unique))
            return new ItemDrop(unique, "Unique");
        return DefinitionsByName.TryGetValue(data.Name, out var definition) && Upgrades.RarityOrder.Contains(data.Rarity)
            ? new ItemDrop(definition, data.Rarity)
            : null;
    }
}
