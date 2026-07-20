using System.Globalization;

namespace RotBoiRemastered.Systems;

/// <summary>A single equipment adjustment. Multipliers use 1.0 as neutral.</summary>
public sealed record ItemStatModifier(string Stat, double Additive = 0, double Multiplier = 1);

/// <summary>
/// A rerollable affix kept separate from both the authored item definition
/// and its rarity. SlotType makes the pools explicit: weapon affixes cannot
/// silently appear on armor, rings, or accessories and vice versa.
/// </summary>
public sealed record ItemAffixDefinition(
    string Name,
    string SlotType,
    string Description,
    IReadOnlyList<ItemStatModifier> Modifiers,
    IReadOnlyDictionary<string, double>? StatusChances = null,
    double Weight = 10);

/// <summary>
/// Hard-Mode-only path imprint. Unlike a normal affix, a core is immutable:
/// reforging never creates, removes, or rerolls it.
/// </summary>
public sealed record CoreForgeDefinition(
    string Key,
    string DisplayName,
    string PathKey,
    string Description,
    IReadOnlyList<ItemStatModifier> Modifiers);

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
///
/// EffectFlavorText is a short, plain-language callout for a unique's
/// EffectIds-driven signature effect (e.g. Grimsbane's Bane stacking isn't
/// chance-based, so it never shows up in the StatusChances "X% ON HIT" rows
/// the way Bloody Dagger's bleed does) -- InformationSheet.DrawItemTooltip
/// draws it right where those status rows would go, distinct from
/// Description's longer prose lower in the tooltip. Deliberately just a
/// string, not a Color, here: Items.cs stays render-agnostic (see this
/// record's own doc comment above), so the color it's drawn in lives in the
/// UI layer instead.
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
    double DropChance = .12,
    string? EffectFlavorText = null);

public sealed record ItemDrop(
    ItemDefinition Definition,
    string Rarity,
    string Grade = "S",
    string Modifier = "Balanced",
    string? CoreForge = null)
{
    public string Name => Definition.Name;
    public string SlotType => Definition.SlotType;
    public string DisplayName => Modifier == "Balanced" ? Name : $"{Modifier} {Name}";
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

    /// <summary>
    /// Grade is independent from rarity and scales the final authored item
    /// and affix deltas toward neutral. Weights total 100; S is exactly 2%,
    /// or one roll in fifty on average.
    /// </summary>
    public static readonly IReadOnlyList<string> GradeOrder =
        new[] { "F", "D", "C", "B", "A", "S" };
    public static readonly IReadOnlyDictionary<string, double> GradePowers =
        new Dictionary<string, double>
        {
            ["F"] = .60, ["D"] = .70, ["C"] = .80,
            ["B"] = .90, ["A"] = .95, ["S"] = 1.00,
        };
    public static readonly IReadOnlyDictionary<string, double> GradeWeights =
        new Dictionary<string, double>
        {
            ["F"] = 34, ["D"] = 26, ["C"] = 19,
            ["B"] = 12, ["A"] = 7, ["S"] = 2,
        };
    public static readonly IReadOnlyDictionary<string, int> GradeUpgradeCosts =
        new Dictionary<string, int>
        {
            ["F"] = 35, ["D"] = 60, ["C"] = 100,
            ["B"] = 160, ["A"] = 250,
        };
    public static readonly IReadOnlyDictionary<string, int> ModifierRerollCosts =
        new Dictionary<string, int>
        {
            ["F"] = 25, ["D"] = 30, ["C"] = 40,
            ["B"] = 55, ["A"] = 75, ["S"] = 100,
        };

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
    /// Placeholder affix catalog. Every non-weapon equipment family has at
    /// least four exclusive choices, giving the reforge system useful data
    /// now without coupling it to future bespoke effect code.
    /// </summary>
    public static readonly IReadOnlyList<ItemAffixDefinition> Affixes = new[]
    {
        new ItemAffixDefinition("Balanced", "*", "No additional strengths or drawbacks.", Mods(), Weight: 20),

        new ItemAffixDefinition("Lazy", "weapon", "Slow projectiles linger farther and land harder.",
            Mods(Mult("Bullet Speed", 72), Mult("Bullet Range", 130), Mult("Bullet Damage", 122)), Weight: 18),
        new ItemAffixDefinition("Fast", "weapon", "Quick projectiles trade reach and impact for velocity.",
            Mods(Mult("Bullet Speed", 140), Mult("Bullet Range", 75), Mult("Bullet Damage", 84)), Weight: 18),
        new ItemAffixDefinition("Bloody", "weapon", "A cruel edge adds damage and a chance to bleed.",
            Mods(Mult("Bullet Damage", 106)), Status("bleed", .12), Weight: 12),
        new ItemAffixDefinition("Scattershot", "weapon", "Adds a projectile but makes every shot smaller and lighter.",
            Mods(Add("Bullet Count", 1), Mult("Bullet Size", 82), Mult("Bullet Damage", 78)), Weight: 12),
        new ItemAffixDefinition("Giantkiller", "weapon", "Massive, slow attacks favor deliberate hits.",
            Mods(Mult("Bullet Damage", 135), Mult("Bullet Size", 130), Mult("Attack Speed", 78)), Weight: 8),
        new ItemAffixDefinition("Godly", "weapon", "A rare all-around blessing with no direct tradeoff.",
            Mods(Mult("Bullet Damage", 108), Mult("Bullet Speed", 108), Mult("Bullet Range", 108),
                Mult("Attack Speed", 108), Add("Bullet Count", .25)), Weight: 2),

        new ItemAffixDefinition("Tanky", "armor", "Health and defense rise at the cost of movement.",
            Mods(Add("Health", 180), Add("Defense", 20), Mult("Player Speed", 88)), Weight: 18),
        new ItemAffixDefinition("Fleet", "armor", "Light construction favors speed and recovery over protection.",
            Mods(Mult("Player Speed", 122), Add("Vitality", 8), Add("Defense", -8)), Weight: 18),
        new ItemAffixDefinition("Regenerative", "armor", "A steady restorative weave bolsters health and vitality.",
            Mods(Add("Health", 100), Add("Vitality", 18), Add("Defense", 8)), Weight: 12),
        new ItemAffixDefinition("Godforged", "armor", "A broad blessing improves every defensive pillar.",
            Mods(Add("Health", 120), Add("Defense", 12), Add("Vitality", 10), Mult("Player Speed", 104)), Weight: 2),

        new ItemAffixDefinition("Sharpsighted", "ring", "Precision and distant lethality improve together.",
            Mods(Add("Crit Chance", .10), Add("Crit Damage", .25), Mult("Bullet Range", 110)), Weight: 18),
        new ItemAffixDefinition("Echoing", "ring", "Occasional extra shots arrive faster but strike more softly.",
            Mods(Add("Bullet Count", .50), Mult("Attack Speed", 110), Mult("Bullet Damage", 92)), Weight: 15),
        new ItemAffixDefinition("Vampiric", "ring", "Violence feeds recovery and carries a trace of bleed.",
            Mods(Add("Vitality", 12), Mult("Bullet Damage", 106)), Status("bleed", .06), Weight: 10),
        new ItemAffixDefinition("Sovereign", "ring", "A measured blessing improves core offensive stats.",
            Mods(Mult("Bullet Damage", 106), Mult("Bullet Speed", 106), Add("Crit Chance", .04)), Weight: 2),

        new ItemAffixDefinition("Sage", "accessory", "Experience comes faster, but hard-won knowledge is physically taxing.",
            Mods(Mult("Exp Multiplier", 120), Mult("Aura Size", 115), Add("Health", -50)), Weight: 15),
        new ItemAffixDefinition("Magnetic", "accessory", "A wide collection aura trades away a little movement.",
            Mods(Mult("Aura Size", 145), Mult("Exp Multiplier", 108), Mult("Player Speed", 95)), Weight: 18),
        new ItemAffixDefinition("Giant", "accessory", "Projectiles swell in size and damage while losing speed.",
            Mods(Mult("Bullet Size", 140), Mult("Bullet Damage", 112), Mult("Bullet Speed", 85)), Weight: 12),
        new ItemAffixDefinition("Windborne", "accessory", "Movement and collection improve at the cost of maximum health.",
            Mods(Mult("Player Speed", 118), Mult("Aura Size", 110), Add("Health", -80)), Weight: 10),
    };

    public static readonly IReadOnlyDictionary<string, ItemAffixDefinition> AffixesByName =
        Affixes.ToDictionary(affix => affix.Name);

    public static readonly IReadOnlyList<CoreForgeDefinition> CoreForges = new[]
    {
        new CoreForgeDefinition("rot", "Core of Rot", "touch",
            "Massive health and defense at a slight cost to damage and movement.",
            Mods(Add("Defense", 40), Add("Health", 400), Mult("Bullet Damage", 90), Mult("Player Speed", 92))),
        new CoreForgeDefinition("malady", "Core of Malady", "phantasia",
            "Slow, deliberate fire whose individual hits are devastating.",
            Mods(Mult("Bullet Speed", 70), Mult("Attack Speed", 75), Mult("Bullet Damage", 160))),
        new CoreForgeDefinition("dissonance", "Core of Dissonance", "sound",
            "Damage and fire rate surge while movement becomes more deliberate.",
            Mods(Mult("Attack Speed", 130), Mult("Bullet Damage", 125), Mult("Player Speed", 88))),
        new CoreForgeDefinition("ache", "Core of Ache", "chemesthesis",
            "Two additional shots erupt across a drastically wider, faster volley.",
            Mods(Add("Bullet Count", 2), Add("Spread Angle", .70), Mult("Attack Speed", 120))),
        new CoreForgeDefinition("chronos", "Core of Chronos", "sight",
            "An additional shot joins modest gains to defense and fire rate.",
            Mods(Add("Bullet Count", 1), Add("Defense", 12), Mult("Attack Speed", 112))),
    };

    public static readonly IReadOnlyDictionary<string, CoreForgeDefinition> CoreForgesByKey =
        CoreForges.ToDictionary(core => core.Key);
    public static readonly IReadOnlyDictionary<string, CoreForgeDefinition> CoreForgesByPathKey =
        CoreForges.ToDictionary(core => core.PathKey);

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
    /// Named boss items (see ItemDefinition's doc comment) -- never rolled by
    /// GenerateDrop/GenerateDrops and always retain Unique rarity power 1.0,
    /// but still roll the same independent grade and slot affix as every
    /// other dropped item. Only obtainable via RollUniqueDrop when the boss
    /// named in DropsFromBossKey is defeated.
    /// </summary>
    public static readonly IReadOnlyList<ItemDefinition> Uniques = new[]
    {

        //Template for new unique items -- list one or more EffectIds to stack independent effects on the same item (see UniqueEffects.OnPlayerHit):
        /*
        new ItemDefinition("Unique Name", "weapon/armor/ring/accessory", "Flavor text.",
            "type_visual (vial/bow/dagger/bell/badge/etc.)", Mods(Mult("Bullet Damage", 100), Mult("Bullet Range", 100), Mult("Bullet Speed", 100)),
            EffectIds: new[] { "custom_effect_name", "second_effect_name" }, DropsFromBossKey: "boss_key", DropChance: .12,
            EffectFlavorText: "Short callout for the signature effect."),
        */

        new ItemDefinition("Bow of Dread", "weapon", "Every arrow carries a whisper of Dread, leaving struck enemies slowed and exposed -- and the bow itself feeds on the fear it causes.",
            "bow", Mods(Mult("Bullet Damage", 135), Mult("Bullet Range", 185), Mult("Bullet Speed", 120)),
            EffectIds: new[] { "dread_on_hit", "dread_lifesteal" }, DropsFromBossKey: "rot", DropChance: .12,
            EffectFlavorText: "On Hit: afflicts Dread, slowing the target and raising damage taken."),

        new ItemDefinition("Grimsbane", "weapon",
            "Darkness clings to the rigid bones, and the shadows it strikes shiver in fear. Every hit marks its target with Bane, a stacking curse that leaves it ever more exposed.",
            "bow", Mods(Mult("Bullet Damage", 50), Mult("Bullet Range", 200), Mult("Attack Speed", 200)), Status("bleed", .05),
            EffectIds: new[] { "bane_on_hit" }, DropsFromBossKey: "dissonance", DropChance: 1,
            EffectFlavorText: "On Hit: stacks Bane, increasing damage taken (max 30 stacks)."),

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
                return CreateRolledDrop(candidate, "Unique", rng);
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

    public static string RollGrade(Random? rng = null)
    {
        rng ??= Random.Shared;
        var weights = GradeOrder.Select(grade => GradeWeights[grade]).ToList();
        return WeightedChoice(GradeOrder, weights, rng);
    }

    public static IReadOnlyList<ItemAffixDefinition> AffixesFor(string slotType) =>
        Affixes.Where(affix => affix.SlotType == slotType || affix.SlotType == "*").ToList();

    public static string RollModifier(string slotType, Random? rng = null, string? excluding = null)
    {
        rng ??= Random.Shared;
        var candidates = AffixesFor(slotType)
            .Where(affix => affix.Name != excluding)
            .ToList();
        return candidates.Count == 0
            ? "Balanced"
            : WeightedChoice(candidates, candidates.Select(affix => affix.Weight).ToList(), rng).Name;
    }

    private static ItemDrop CreateRolledDrop(ItemDefinition definition, string rarity, Random rng) =>
        new(definition, rarity, RollGrade(rng), RollModifier(definition.SlotType, rng));

    public static ItemDrop GenerateDrop(ItemDefinition definition, string rarity, Random? rng = null)
    {
        rng ??= Random.Shared;
        return CreateRolledDrop(definition, rarity, rng);
    }

    public static ItemDrop GenerateDrop(Random? rng = null)
    {
        rng ??= Random.Shared;
        var definition = Definitions[rng.Next(Definitions.Count)];
        return CreateRolledDrop(definition, Upgrades.RollRarity(rng), rng);
    }

    public static double CoreForgeChance(string rarity) => rarity switch
    {
        "Epic" => .10,
        "Legendary" => .20,
        "Mythical" => .35,
        _ => 0,
    };

    public static bool IsCoreForgeEligible(ItemDrop drop) =>
        drop.Rarity != "Unique" && CoreForgeChance(drop.Rarity) > 0;

    /// <summary>Attempts the one immutable path-core roll made when an item first drops.</summary>
    public static ItemDrop RollCoreForge(ItemDrop drop, bool hardMode, string pathKey, Random? rng = null)
    {
        rng ??= Random.Shared;
        if (drop.CoreForge is not null)
            return drop;
        if (!hardMode || !IsCoreForgeEligible(drop)
            || !CoreForgesByPathKey.TryGetValue(pathKey, out var core)
            || rng.NextDouble() >= CoreForgeChance(drop.Rarity))
            return drop;
        return drop with { CoreForge = core.Key };
    }

    public static List<ItemDrop> GenerateDrops(int count, Random? rng = null, bool hardMode = false, string? pathKey = null)
    {
        rng ??= Random.Shared;
        return Enumerable.Range(0, count)
            .Select(_ => GenerateDrop(rng))
            .Select(drop => pathKey is null ? drop : RollCoreForge(drop, hardMode, pathKey, rng))
            .ToList();
    }

    /// <summary>Rarity strengthens an item's identity without making drawbacks lethal. Unique contributes rarity power 1.0; the separate grade multiplier is applied later by Effects.</summary>
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

    public static double GradePower(string grade) => GradePowers.GetValueOrDefault(grade, 1.0);

    public static bool CanUpgradeGrade(ItemDrop drop) => GradeUpgradeCosts.ContainsKey(drop.Grade);

    public static int? GradeUpgradeCost(ItemDrop drop) =>
        GradeUpgradeCosts.TryGetValue(drop.Grade, out int cost) ? cost : null;

    public static int ModifierRerollCost(ItemDrop drop) =>
        ModifierRerollCosts.GetValueOrDefault(drop.Grade, ModifierRerollCosts["S"]);

    public static ItemDrop UpgradeGrade(ItemDrop drop)
    {
        int index = GradeOrder.ToList().IndexOf(drop.Grade);
        return index >= 0 && index < GradeOrder.Count - 1
            ? drop with { Grade = GradeOrder[index + 1] }
            : drop;
    }

    public static ItemDrop RerollModifier(ItemDrop drop, Random? rng = null) =>
        drop with { Modifier = RollModifier(drop.SlotType, rng, drop.Modifier) };

    private static ItemAffixDefinition AffixFor(ItemDrop drop)
    {
        if (AffixesByName.TryGetValue(drop.Modifier, out var affix)
            && (affix.SlotType == drop.SlotType || affix.SlotType == "*"))
            return affix;
        return AffixesByName["Balanced"];
    }

    public static ItemAffixDefinition ModifierDefinition(ItemDrop drop) => AffixFor(drop);

    public static CoreForgeDefinition? CoreForgeFor(ItemDrop drop) =>
        drop.CoreForge is not null ? CoreForgesByKey.GetValueOrDefault(drop.CoreForge) : null;

    public static IReadOnlyList<CoreForgeDefinition> EquippedCoreForges(IEnumerable<ItemDrop?> equipment) =>
        equipment.Where(item => item is not null)
            .Select(item => CoreForgeFor(item!))
            .Where(core => core is not null)
            .Cast<CoreForgeDefinition>()
            .GroupBy(core => core.Key)
            .Select(group => group.First())
            .ToList();

    private static IEnumerable<ItemStatModifier> StandardModifiers(ItemDrop drop) =>
        drop.Definition.Modifiers.Concat(AffixFor(drop).Modifiers);

    public static IReadOnlyList<ItemEffectView> Effects(ItemDrop drop)
    {
        double power = RarityPower(drop.Rarity) * GradePower(drop.Grade);
        var effects = StandardModifiers(drop).Select(modifier => new ItemEffectView(
                modifier.Stat,
                modifier.Additive * power,
                1 + (modifier.Multiplier - 1) * power))
            .ToList();
        // Core identity is an immutable endgame reward, not another quality-
        // scaled affix. Exact promises such as Ache +2 shots and Chronos +1
        // remain exact at every grade and eligible rarity.
        if (CoreForgeFor(drop) is { } core)
            effects.AddRange(core.Modifiers.Select(modifier =>
                new ItemEffectView(modifier.Stat, modifier.Additive, modifier.Multiplier)));
        return effects;
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
            foreach (var (kind, chance) in EffectiveStatusChances(drop))
                result[kind] = Math.Min(.65, result.GetValueOrDefault(kind) + chance);
        }
        return result;
    }

    public static IReadOnlyDictionary<string, double> EffectiveStatusChances(ItemDrop drop)
    {
        var result = new Dictionary<string, double>();
        double power = RarityPower(drop.Rarity) * GradePower(drop.Grade);
        var sources = new[] { drop.Definition.StatusChances, AffixFor(drop).StatusChances };
        foreach (var source in sources.Where(source => source is not null))
            foreach (var (kind, chance) in source!)
                result[kind] = Math.Min(.65, result.GetValueOrDefault(kind) + chance * power);
        return result;
    }

    public static StoredItemData Serialize(ItemDrop drop) =>
        new(drop.Name, drop.Rarity, drop.Grade, drop.Modifier, drop.CoreForge);

    /// <summary>Checked by name against Uniques first (their stored Rarity is always "Unique", which Upgrades.RarityOrder deliberately doesn't contain) before falling back to the regular, tiered-rarity-validated lookup.</summary>
    public static ItemDrop? Deserialize(StoredItemData? data)
    {
        if (data is null)
            return null;
        string storedGrade = data.Grade ?? "S";
        string grade = GradePowers.ContainsKey(storedGrade) ? storedGrade : "S";
        if (UniquesByName.TryGetValue(data.Name, out var unique))
            return NormalizeDrop(new ItemDrop(unique, "Unique", grade, data.Modifier ?? "Balanced", data.CoreForge));
        return DefinitionsByName.TryGetValue(data.Name, out var definition) && Upgrades.RarityOrder.Contains(data.Rarity)
            ? NormalizeDrop(new ItemDrop(definition, data.Rarity, grade, data.Modifier ?? "Balanced", data.CoreForge))
            : null;
    }

    private static ItemDrop NormalizeDrop(ItemDrop drop)
    {
        var affix = AffixesByName.GetValueOrDefault(drop.Modifier);
        var normalized = affix is not null && (affix.SlotType == drop.SlotType || affix.SlotType == "*")
            ? drop
            : drop with { Modifier = "Balanced" };
        if (normalized.CoreForge is not null
            && (!CoreForgesByKey.ContainsKey(normalized.CoreForge) || !IsCoreForgeEligible(normalized)))
            normalized = normalized with { CoreForge = null };
        return normalized;
    }
}
