using System.Globalization;

namespace RotBoiRemastered.Systems;

/// <summary>A single equipment adjustment. Multipliers use 1.0 as neutral.</summary>
public sealed record ItemStatModifier(string Stat, double Additive = 0, double Multiplier = 1);

/// <summary>
/// A named, reusable bundle of stat/status bonuses. Where the old system
/// randomly rolled one of these onto a dropped item, that RNG step is gone:
/// every <see cref="ItemDefinition"/> now authors its own fixed
/// <see cref="ItemDefinition.ModifierLadder"/> -- an ordered list of Modifier
/// names drawn from this shared catalog -- and how many of that ladder are
/// active is purely a function of the item's current Rarity (see
/// <see cref="Items.ModifierUnlockCount"/>). A Modifier's identity is
/// therefore locked to the item the moment it's authored; reforging can
/// raise Rarity to unlock further rungs of that same fixed ladder, but it
/// never changes which Modifiers an item can ever have. SlotType keeps the
/// catalog honest: weapon Modifiers cannot silently appear in an armor's
/// ladder and vice versa.
/// </summary>
public sealed record ItemModifierDefinition(
    string Name,
    string SlotType,
    string Description,
    IReadOnlyList<ItemStatModifier> StatModifiers,
    IReadOnlyDictionary<string, double>? StatusChances = null);

/// <summary>
/// An item's Legendary/Mythical-only signature power -- the one part of the
/// new rarity ladder that isn't drawn from the shared Modifier catalog at
/// all. Authored per item, in the same non-scaling, flavorful spirit as
/// <see cref="CoreForgeDefinition"/> and the hand-built <see cref="Items.Uniques"/>:
/// this is what makes a Legendary+ copy of an item feel like a different
/// object, not just a bigger number. EffectIds (optional) name a bespoke
/// on-hit hook dispatched by UniqueEffects.OnPlayerHit -- see
/// <see cref="Items.ActiveEffectIds"/> for how a Signature's EffectIds and a
/// Unique's own Definition-level EffectIds are merged into one lookup.
/// </summary>
public sealed record ItemSignatureDefinition(
    string Name,
    string Description,
    IReadOnlyList<ItemStatModifier> StatModifiers,
    IReadOnlyDictionary<string, double>? StatusChances = null,
    IReadOnlyList<string>? EffectIds = null,
    string? EffectFlavorText = null);

/// <summary>
/// Hard-Mode-only path imprint. Unlike a Modifier, a core is immutable:
/// reforging never creates, removes, or replaces it.
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
/// ModifierLadder is the ordered list of <see cref="Items.ModifiersByName"/>
/// entries this specific item can ever unlock -- see
/// <see cref="Items.ModifierUnlockCount"/> for how many of them a given
/// Rarity actually activates. Signature is this item's Legendary/Mythical-
/// only bespoke power (see <see cref="ItemSignatureDefinition"/>). Both are
/// empty/null for every <see cref="Items.Uniques"/> entry -- a Unique's
/// power is already fully baked into its own Modifiers/EffectIds and its
/// Rarity never changes, so it has no ladder to climb.
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
    string? EffectFlavorText = null,
    IReadOnlyList<string>? ModifierLadder = null,
    ItemSignatureDefinition? Signature = null)
{
    public IReadOnlyList<string> ModifierLadder { get; init; } = ModifierLadder ?? Array.Empty<string>();
}

/// <summary>
/// Rarity is now the *only* power dial an item has (Grade was removed
/// entirely -- see the README/venture report for why). CoreForge remains
/// the separate, Hard-Mode-only immutable overlay described on
/// <see cref="CoreForgeDefinition"/>.
/// </summary>
public sealed record ItemDrop(
    ItemDefinition Definition,
    string Rarity,
    string? CoreForge = null)
{
    public string Name => Definition.Name;
    public string SlotType => Definition.SlotType;
    public string DisplayName => Name;
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

/// <summary>One rung of an item's rarity/Modifier-unlock ladder, used to render the tooltip visualizer (see ItemCards.DrawModifierLadder).</summary>
public sealed record ModifierLadderRung(
    string Rarity,
    bool Unlocked,
    bool IsSignature,
    string Name,
    string Description);

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
    /// Reduced from the old Grade-and-Modifier-reroll era's 5: with Grade
    /// gone and Modifiers no longer randomly rerolled, this is the single
    /// remaining Reforge cost -- one Rarity step -- so it's priced lower to
    /// match how much less it now buys per purchase, not because Fragments
    /// themselves became more plentiful.
    /// </summary>
    public const int ReforgeFragmentCost = 3;

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
    /// touch the other, or they'll silently disagree.
    /// </summary>
    private static ItemStatModifier Mult(string stat, double percent) =>
        new(stat, Multiplier: stat == "Attack Speed" ? 100.0 / percent : percent / 100.0);
    private static IReadOnlyList<ItemStatModifier> Mods(params ItemStatModifier[] modifiers) => modifiers;
    private static IReadOnlyDictionary<string, double> Status(string kind, double chance) =>
        new Dictionary<string, double> { [kind] = chance };

    /// <summary>
    /// Shared Modifier catalog. Every non-weapon slot has exactly four
    /// entries, which is deliberate: <see cref="ModifierUnlockCount"/> tops
    /// out at 4 (Mythical), so every armor/ring/accessory item's ladder ends
    /// up drawing on this whole pool by the time it reaches Mythical, with
    /// per-item variety coming from *order* (which bonus unlocks first) and,
    /// for accessories, which one entry each item's four-slot ladder leaves
    /// out in favor of "Boundless." The weapon pool is intentionally larger
    /// (six) so a weapon's four-slot ladder can leave two out entirely --
    /// see the venture report for why that's flagged as an area to expand
    /// for the other three slots too.
    /// </summary>
    public static readonly IReadOnlyList<ItemModifierDefinition> Modifiers = new[]
    {
        new ItemModifierDefinition("Lazy", "weapon", "Slow projectiles linger farther and land harder.",
            Mods(Mult("Bullet Speed", 72), Mult("Bullet Range", 130), Mult("Bullet Damage", 122))),
        new ItemModifierDefinition("Fast", "weapon", "Quick projectiles trade reach and impact for velocity.",
            Mods(Mult("Bullet Speed", 140), Mult("Bullet Range", 75), Mult("Bullet Damage", 84))),
        new ItemModifierDefinition("Bloody", "weapon", "A cruel edge adds damage and a chance to bleed.",
            Mods(Mult("Bullet Damage", 106)), Status("bleed", .12)),
        new ItemModifierDefinition("Scattershot", "weapon", "Adds a projectile but makes every shot smaller and lighter.",
            Mods(Add("Bullet Count", 1), Mult("Bullet Size", 82), Mult("Bullet Damage", 78))),
        new ItemModifierDefinition("Giantkiller", "weapon", "Massive, slow attacks favor deliberate hits.",
            Mods(Mult("Bullet Damage", 135), Mult("Bullet Size", 130), Mult("Attack Speed", 78))),
        new ItemModifierDefinition("Godly", "weapon", "A rare all-around blessing with no direct tradeoff.",
            Mods(Mult("Bullet Damage", 108), Mult("Bullet Speed", 108), Mult("Bullet Range", 108),
                Mult("Attack Speed", 108), Add("Bullet Count", .25))),

        new ItemModifierDefinition("Tanky", "armor", "Health and defense rise at the cost of movement.",
            Mods(Add("Health", 180), Add("Defense", 20), Mult("Player Speed", 88))),
        new ItemModifierDefinition("Fleet", "armor", "Light construction favors speed and recovery over protection.",
            Mods(Mult("Player Speed", 122), Add("Vitality", 8), Add("Defense", -8))),
        new ItemModifierDefinition("Regenerative", "armor", "A steady restorative weave bolsters health and vitality.",
            Mods(Add("Health", 100), Add("Vitality", 18), Add("Defense", 8))),
        new ItemModifierDefinition("Godforged", "armor", "A broad blessing improves every defensive pillar.",
            Mods(Add("Health", 120), Add("Defense", 12), Add("Vitality", 10), Mult("Player Speed", 104))),

        new ItemModifierDefinition("Sharpsighted", "ring", "Precision and distant lethality improve together.",
            Mods(Add("Crit Chance", .10), Add("Crit Damage", .25), Mult("Bullet Range", 110))),
        new ItemModifierDefinition("Echoing", "ring", "Occasional extra shots arrive faster but strike more softly.",
            Mods(Add("Bullet Count", .50), Mult("Attack Speed", 110), Mult("Bullet Damage", 92))),
        new ItemModifierDefinition("Vampiric", "ring", "Violence feeds recovery and carries a trace of bleed.",
            Mods(Add("Vitality", 12), Mult("Bullet Damage", 106)), Status("bleed", .06)),
        new ItemModifierDefinition("Sovereign", "ring", "A measured blessing improves core offensive stats.",
            Mods(Mult("Bullet Damage", 106), Mult("Bullet Speed", 106), Add("Crit Chance", .04))),

        new ItemModifierDefinition("Sage", "accessory", "Experience comes faster, but hard-won knowledge is physically taxing.",
            Mods(Mult("Exp Multiplier", 120), Mult("Aura Size", 115), Add("Health", -50))),
        new ItemModifierDefinition("Magnetic", "accessory", "A wide collection aura trades away a little movement.",
            Mods(Mult("Aura Size", 145), Mult("Exp Multiplier", 108), Mult("Player Speed", 95))),
        new ItemModifierDefinition("Giant", "accessory", "Projectiles swell in size and damage while losing speed.",
            Mods(Mult("Bullet Size", 140), Mult("Bullet Damage", 112), Mult("Bullet Speed", 85))),
        new ItemModifierDefinition("Windborne", "accessory", "Movement and collection improve at the cost of maximum health.",
            Mods(Mult("Player Speed", 118), Mult("Aura Size", 110), Add("Health", -80))),
        new ItemModifierDefinition("Boundless", "accessory", "A rare all-around blessing with no direct tradeoff.",
            Mods(Mult("Exp Multiplier", 106), Mult("Aura Size", 106), Mult("Player Speed", 104))),
    };

    public static readonly IReadOnlyDictionary<string, ItemModifierDefinition> ModifiersByName =
        Modifiers.ToDictionary(modifier => modifier.Name);

    public static IReadOnlyList<ItemModifierDefinition> ModifiersFor(string slotType) =>
        Modifiers.Where(modifier => modifier.SlotType == slotType).ToList();

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
    /// dagger -> sword -> spear -> bow -> wand. Each weapon's ModifierLadder
    /// picks four of the six shared weapon Modifiers (see Modifiers above),
    /// ordered low-rarity-unlocks-first, chosen to bend that weapon's
    /// existing tempo/control/precision identity rather than fight it.
    /// </summary>
    public static readonly IReadOnlyList<ItemDefinition> Definitions = new[]
    {
        new ItemDefinition("Iron Dagger", "weapon", "Close enough to hear the cut.", "dagger",
            Mods(Mult("Bullet Damage", 205), Mult("Bullet Range", 40), Mult("Attack Speed", 128)),
            ModifierLadder: new[] { "Fast", "Bloody", "Scattershot", "Godly" },
            Signature: new ItemSignatureDefinition("Silent Edge",
                "The blade forgets nothing it has opened -- every cut lands a little deeper than the last.",
                Mods(Mult("Bullet Damage", 118), Mult("Attack Speed", 112)), Status("bleed", .18))),
        new ItemDefinition("Bloody Dagger", "weapon", "It remembers every hand that slipped.", "dagger",
            Mods(Mult("Bullet Damage", 182), Mult("Bullet Range", 44), Mult("Attack Speed", 122)), Status("bleed", .20),
            ModifierLadder: new[] { "Fast", "Scattershot", "Giantkiller", "Godly" },
            Signature: new ItemSignatureDefinition("Hemorrhage",
                "Old wounds reopen faster than new ones can close.",
                Mods(Mult("Bullet Damage", 112)), Status("bleed", .28))),
        new ItemDefinition("Rusty Sword", "weapon", "The ruined edge asks for many swings.", "sword",
            Mods(Mult("Bullet Damage", 62), Mult("Bullet Range", 58), Mult("Attack Speed", 217)),
            ModifierLadder: new[] { "Fast", "Scattershot", "Bloody", "Godly" },
            Signature: new ItemSignatureDefinition("Persistence",
                "It has broken before. It has never once stopped swinging.",
                Mods(Mult("Attack Speed", 118), Mult("Bullet Damage", 110)))),
        new ItemDefinition("Iron Sword", "weapon", "A dependable answer at arm's length.", "sword",
            Mods(Mult("Bullet Damage", 155), Mult("Bullet Range", 62), Mult("Attack Speed", 106)),
            ModifierLadder: new[] { "Bloody", "Lazy", "Giantkiller", "Godly" },
            Signature: new ItemSignatureDefinition("Forge-Touched",
                "Struck true enough, it hums -- and something answers back from the Golden Forge.",
                Mods(Mult("Bullet Damage", 112), Add("Crit Chance", .06)),
                EffectIds: new[] { "forge_touched_crit_fragment" },
                EffectFlavorText: "On Critical Hit: small chance to conjure a Fragment out of the impact.")),
        new ItemDefinition("Bloody Sword", "weapon", "Warm stains bead along the fuller.", "sword",
            Mods(Mult("Bullet Damage", 138), Mult("Bullet Range", 65), Mult("Attack Speed", 111)), Status("bleed", .16),
            ModifierLadder: new[] { "Fast", "Giantkiller", "Scattershot", "Godly" },
            Signature: new ItemSignatureDefinition("Fuller's Thirst",
                "The groove along the blade was cut for a reason.",
                Mods(Mult("Bullet Damage", 114)), Status("bleed", .22))),
        new ItemDefinition("Iron Spear", "weapon", "Distance, leverage, and one clean line.", "spear",
            Mods(Mult("Bullet Damage", 128), Mult("Bullet Range", 102), Mult("Attack Speed", 91), Add("Bullet Pierce", .60)),
            ModifierLadder: new[] { "Lazy", "Giantkiller", "Bloody", "Godly" },
            Signature: new ItemSignatureDefinition("Long Reach",
                "One clean line, extended past where the eye expects it to stop.",
                Mods(Mult("Bullet Range", 118), Add("Bullet Pierce", .40)))),
        new ItemDefinition("Bone Spear", "weapon", "A pale point made for finding gaps.", "spear",
            Mods(Mult("Bullet Damage", 116), Mult("Bullet Range", 108), Add("Crit Chance", .08), Add("Bullet Pierce", .40)),
            ModifierLadder: new[] { "Fast", "Bloody", "Giantkiller", "Godly" },
            Signature: new ItemSignatureDefinition("Marrow-Find",
                "It has never once failed to find the gap between the ribs.",
                Mods(Add("Crit Chance", .10), Mult("Bullet Damage", 112)))),
        new ItemDefinition("Hunting Bow", "weapon", "The string hums before danger arrives.", "bow",
            Mods(Mult("Bullet Damage", 92), Mult("Bullet Range", 172), Mult("Bullet Speed", 128), Mult("Attack Speed", 114)),
            ModifierLadder: new[] { "Lazy", "Scattershot", "Bloody", "Godly" },
            Signature: new ItemSignatureDefinition("Early Warning",
                "The string hums before danger arrives -- and answers before it can close the distance.",
                Mods(Mult("Bullet Range", 114), Mult("Attack Speed", 110)))),
        new ItemDefinition("Yew Longbow", "weapon", "Patience drawn into a distant point.", "bow",
            Mods(Mult("Bullet Damage", 98), Mult("Bullet Range", 202), Mult("Bullet Speed", 142), Mult("Attack Speed", 85)),
            ModifierLadder: new[] { "Lazy", "Giantkiller", "Scattershot", "Godly" },
            Signature: new ItemSignatureDefinition("Drawn Patience",
                "Held a heartbeat longer than courage recommends -- and worth every heartbeat.",
                Mods(Mult("Bullet Damage", 122), Mult("Bullet Range", 108)))),
        new ItemDefinition("Ash Wand", "weapon", "A faint ember reaches beyond the dark.", "wand",
            Mods(Mult("Bullet Damage", 72), Mult("Bullet Range", 215), Mult("Bullet Speed", 134), Mult("Bullet Size", 82)),
            ModifierLadder: new[] { "Fast", "Scattershot", "Lazy", "Godly" },
            Signature: new ItemSignatureDefinition("Ember Trail",
                "The ember doesn't go out. It just moves to the next thing.",
                Mods(Mult("Bullet Range", 116), Mult("Bullet Speed", 112)))),
        new ItemDefinition("Glass Wand", "weapon", "Fragile light travels farther than courage.", "wand",
            Mods(Mult("Bullet Damage", 64), Mult("Bullet Range", 245), Mult("Bullet Speed", 155), Add("Crit Chance", .12)),
            ModifierLadder: new[] { "Fast", "Bloody", "Lazy", "Godly" },
            Signature: new ItemSignatureDefinition("Brittle Light",
                "It could shatter at any moment. It simply chooses not to.",
                Mods(Add("Crit Chance", .10), Mult("Bullet Speed", 112)))),

        new ItemDefinition("Leather Vest", "armor", "Scuffed hide that leaves room to breathe.", "vest",
            Mods(Add("Defense", 18), Mult("Player Speed", 108)),
            ModifierLadder: new[] { "Fleet", "Regenerative", "Tanky", "Godforged" },
            Signature: new ItemSignatureDefinition("Room to Breathe",
                "Scuffed, worn, and never once slowed anyone down.",
                Mods(Mult("Player Speed", 110), Add("Vitality", 10)))),
        new ItemDefinition("Bloodstained Garb", "armor", "The cloth refuses to let another drop fall.", "vest",
            Mods(Add("Defense", 24), Add("Vitality", 12), Mult("Player Speed", 103)),
            ModifierLadder: new[] { "Regenerative", "Fleet", "Tanky", "Godforged" },
            Signature: new ItemSignatureDefinition("Refusal",
                "It has already soaked up more than it should have survived.",
                Mods(Add("Health", 140), Add("Vitality", 14)))),
        new ItemDefinition("Chainmail", "armor", "Linked rings trade a little speed for certainty.", "chain",
            Mods(Add("Defense", 42), Mult("Player Speed", 92)),
            ModifierLadder: new[] { "Tanky", "Regenerative", "Fleet", "Godforged" },
            Signature: new ItemSignatureDefinition("Certainty",
                "Every ring answers for the one beside it.",
                Mods(Add("Defense", 16), Add("Health", 90)))),
        new ItemDefinition("Plate Armor", "armor", "A walking wall, heavy but never absolute.", "plate",
            Mods(Add("Defense", 76), Mult("Player Speed", 78)),
            ModifierLadder: new[] { "Tanky", "Fleet", "Regenerative", "Godforged" },
            Signature: new ItemSignatureDefinition("Never Absolute",
                "A wall, yes -- but walls that move are so much harder to plan around.",
                Mods(Add("Defense", 14), Mult("Player Speed", 108)))),
        new ItemDefinition("Rusty Plate", "armor", "Missing rivets make the old shell surprisingly nimble.", "plate",
            Mods(Add("Defense", 55), Mult("Player Speed", 90)),
            ModifierLadder: new[] { "Fleet", "Tanky", "Regenerative", "Godforged" },
            Signature: new ItemSignatureDefinition("Missing Rivets",
                "What it lost in coverage, it gained back in not being where the hit landed.",
                Mods(Mult("Player Speed", 112), Add("Defense", 10)))),

        new ItemDefinition("Copper Ring", "ring", "A warm band that keeps the hands moving.", "band",
            Mods(Mult("Attack Speed", 114)),
            ModifierLadder: new[] { "Echoing", "Sharpsighted", "Vampiric", "Sovereign" },
            Signature: new ItemSignatureDefinition("Warm Band",
                "The hands never quite stop moving once it's on.",
                Mods(Mult("Attack Speed", 112), Add("Bullet Count", .25)))),
        new ItemDefinition("Silver Band", "ring", "Cold metal steadies a hurried aim.", "band",
            Mods(Add("Crit Chance", .10), Mult("Bullet Speed", 112)),
            ModifierLadder: new[] { "Sharpsighted", "Echoing", "Vampiric", "Sovereign" },
            Signature: new ItemSignatureDefinition("Steadied Aim",
                "Cold enough to still a hurried hand entirely.",
                Mods(Add("Crit Chance", .10), Add("Crit Damage", .20)))),
        new ItemDefinition("Signet Ring", "ring", "A forgotten crest still carries authority.", "signet",
            Mods(Mult("Bullet Damage", 116), Add("Defense", 8)),
            ModifierLadder: new[] { "Vampiric", "Sharpsighted", "Echoing", "Sovereign" },
            Signature: new ItemSignatureDefinition("Old Authority",
                "The crest is forgotten. What it commands is not.",
                Mods(Mult("Bullet Damage", 114), Add("Defense", 8)))),
        new ItemDefinition("Thorn Ring", "ring", "Its tiny barbs promise that wounds linger.", "signet",
            Mods(Add("Crit Chance", .05)), Status("bleed", .08),
            ModifierLadder: new[] { "Vampiric", "Echoing", "Sharpsighted", "Sovereign" },
            Signature: new ItemSignatureDefinition("Lingering Promise",
                "The barbs were never the point. The waiting was.",
                Mods(Add("Crit Chance", .08)), Status("bleed", .16))),

        new ItemDefinition("Lucky Charm", "accessory", "Small enough to lose; stubborn enough to return.", "charm",
            Mods(Add("Crit Chance", .08), Mult("Exp Multiplier", 112)),
            ModifierLadder: new[] { "Magnetic", "Giant", "Windborne", "Boundless" },
            Signature: new ItemSignatureDefinition("Stubborn Return",
                "It has been lost more times than it has been kept -- and it always comes back.",
                Mods(Add("Crit Chance", .06), Mult("Exp Multiplier", 110)))),
        new ItemDefinition("Old Locket", "accessory", "The portrait is gone, but the promise remains.", "locket",
            Mods(Add("Health", 120), Add("Vitality", 8)),
            ModifierLadder: new[] { "Magnetic", "Sage", "Giant", "Boundless" },
            Signature: new ItemSignatureDefinition("The Promise Remains",
                "The portrait faded years ago. The promise never did.",
                Mods(Add("Health", 150), Add("Vitality", 12)))),
        new ItemDefinition("Traveler's Badge", "accessory", "Every scratch points toward another road.", "badge",
            Mods(Mult("Player Speed", 110), Add("Aura Size", 14)),
            ModifierLadder: new[] { "Windborne", "Magnetic", "Sage", "Boundless" },
            Signature: new ItemSignatureDefinition("Another Road",
                "Every scratch points somewhere. None of them point back.",
                Mods(Mult("Player Speed", 114), Add("Aura Size", 18)))),
        new ItemDefinition("Venom Vial", "accessory", "A green drop waits behind thin glass.", "vial",
            Mods(Mult("Bullet Damage", 94)), Status("poison", .15),
            ModifierLadder: new[] { "Giant", "Sage", "Magnetic", "Boundless" },
            Signature: new ItemSignatureDefinition("Thin Glass",
                "The glass gets thinner every time it's refilled.",
                Mods(Mult("Bullet Damage", 112)), Status("poison", .24))),
        new ItemDefinition("Frost Bell", "accessory", "Its silent note makes the world hesitate.", "bell",
            Mods(Mult("Bullet Range", 110)), Status("slow", .14),
            ModifierLadder: new[] { "Sage", "Windborne", "Giant", "Boundless" },
            Signature: new ItemSignatureDefinition("Silent Note",
                "The world doesn't just hesitate anymore. It listens.",
                Mods(Mult("Bullet Range", 114)), Status("slow", .22))),
    };

    public static readonly IReadOnlyDictionary<string, ItemDefinition> DefinitionsByName =
        Definitions.ToDictionary(definition => definition.Name);

    /// <summary>
    /// Named boss items (see ItemDefinition's doc comment) -- never rolled by
    /// GenerateDrop/GenerateDrops and always retain Unique rarity, which
    /// never changes (there is nothing above it in Upgrades.RarityOrder, and
    /// Reforge only ever upgrades Rarity for items that appear in that
    /// order). Still obtainable only via RollUniqueDrop when the boss named
    /// in DropsFromBossKey is defeated.
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
    public static ItemDrop? RollUniqueDrop(string bossKey, Random? rng = null, int newGamePlusLevel = 0)
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
    private static readonly int[] PathDropCounts = { 0, 1, 2, 3 };
    // Composite Path mode moves reliable equipment acquisition into branch
    // treasure rooms. Ordinary enemies average roughly .23 items instead of
    // the arena mode's .75, while never making a combat drop impossible.
    private static readonly double[] PathDropCountWeights = { 82, 14, 3.5, .5 };

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

    public static int RollPathDropCount(Random? rng = null)
    {
        rng ??= Random.Shared;
        return WeightedChoice(PathDropCounts, PathDropCountWeights, rng);
    }

    private static IReadOnlyList<double> ImproveWeights(IReadOnlyList<double> baseWeights, int newGamePlusLevel, double rankBoost)
    {
        int level = NewGamePlus.ClampLevel(newGamePlusLevel);
        double factor = 1 + rankBoost * level;
        return baseWeights.Select((weight, rank) => weight * Math.Pow(factor, rank)).ToList();
    }

    public static string RollItemRarity(Random? rng = null, int newGamePlusLevel = 0)
    {
        rng ??= Random.Shared;
        var weights = ImproveWeights(Upgrades.RarityOrder.Select(rarity => Upgrades.RarityWeights[rarity]).ToList(),
            newGamePlusLevel, .18);
        return WeightedChoice(Upgrades.RarityOrder, weights, rng);
    }

    private static ItemDrop CreateRolledDrop(ItemDefinition definition, string rarity) => new(definition, rarity);

    public static ItemDrop GenerateDrop(ItemDefinition definition, string rarity, Random? rng = null, int newGamePlusLevel = 0) =>
        CreateRolledDrop(definition, rarity);

    public static ItemDrop GenerateDrop(Random? rng = null, int newGamePlusLevel = 0)
    {
        rng ??= Random.Shared;
        var definition = Definitions[rng.Next(Definitions.Count)];
        return CreateRolledDrop(definition, RollItemRarity(rng, newGamePlusLevel));
    }

    /// <summary>
    /// Base Core-Forge roll chance before the True Hard Mode multiplier.
    /// Only Epic+ drops are eligible at all -- a Common/Rare item can never
    /// carry a core, mirroring the old design's intent even though Grade is
    /// gone.
    /// </summary>
    public static double CoreForgeChance(string rarity, int newGamePlusLevel = 0, bool trueHardMode = false)
    {
        double baseChance = rarity switch
        {
            "Epic" => .10,
            "Legendary" => .20,
            "Mythical" => .35,
            _ => 0,
        };
        double chance = baseChance * (1 + .25 * NewGamePlus.ClampLevel(newGamePlusLevel));
        // True Hard Mode -- both braziers lit in The Mind -- triples the
        // roll chance rather than guaranteeing it outright, so a triple-
        // lit run still feels like better odds at a valuable pull instead
        // of turning every eligible drop into a certainty.
        if (trueHardMode)
            chance *= 3.0;
        return Math.Min(.90, chance);
    }

    public static bool IsCoreForgeEligible(ItemDrop drop) =>
        drop.Rarity != "Unique" && CoreForgeChance(drop.Rarity) > 0;

    /// <summary>
    /// Attempts the one immutable path-core roll made when an item first
    /// drops. hardModeActive means "at least one Hard Mode brazier is lit"
    /// (RunState.AnyHardModeActive) -- that alone is enough for a
    /// themed-area drop to be eligible; trueHardMode (RunState.IsTrueHardMode,
    /// both braziers lit) only affects the roll odds via CoreForgeChance.
    /// </summary>
    public static ItemDrop RollCoreForge(ItemDrop drop, bool hardModeActive, string pathKey, Random? rng = null,
        int newGamePlusLevel = 0, bool trueHardMode = false)
    {
        rng ??= Random.Shared;
        if (drop.CoreForge is not null)
            return drop;
        if (!hardModeActive || !IsCoreForgeEligible(drop)
            || !CoreForgesByPathKey.TryGetValue(pathKey, out var core)
            || rng.NextDouble() >= CoreForgeChance(drop.Rarity, newGamePlusLevel, trueHardMode))
            return drop;
        return drop with { CoreForge = core.Key };
    }

    public static List<ItemDrop> GenerateDrops(int count, Random? rng = null, bool hardModeActive = false, string? pathKey = null,
        int newGamePlusLevel = 0, bool trueHardMode = false)
    {
        rng ??= Random.Shared;
        return Enumerable.Range(0, count)
            .Select(_ => GenerateDrop(rng, newGamePlusLevel))
            .Select(drop => pathKey is null ? drop : RollCoreForge(drop, hardModeActive, pathKey, rng, newGamePlusLevel, trueHardMode))
            .ToList();
    }

    /// <summary>
    /// How many rungs of an item's ModifierLadder are active at this Rarity.
    /// Common gets the item's bare authored Modifiers and nothing else --
    /// deliberately not "weaker Modifiers," just none -- and each step up
    /// unlocks one more, topping out at all four on Mythical. Unique isn't
    /// part of this ladder at all (see ItemDefinition's doc comment).
    /// </summary>
    public static int ModifierUnlockCount(string rarity) => rarity switch
    {
        "Common" => 0,
        "Rare" => 1,
        "Epic" => 2,
        "Legendary" => 3,
        "Mythical" => 4,
        _ => 0,
    };

    /// <summary>Legendary and Mythical are the only Rarities that activate an item's Signature.</summary>
    public static bool SignatureUnlocked(string rarity) => rarity is "Legendary" or "Mythical";

    public static bool CanUpgradeRarity(ItemDrop drop) =>
        Upgrades.RarityOrder.Contains(drop.Rarity) && drop.Rarity != Upgrades.RarityOrder[^1];

    public static int? RarityUpgradeCost(ItemDrop drop) =>
        CanUpgradeRarity(drop) ? ReforgeFragmentCost : null;

    public static ItemDrop UpgradeRarity(ItemDrop drop)
    {
        var order = Upgrades.RarityOrder;
        int index = order.ToList().IndexOf(drop.Rarity);
        return index >= 0 && index < order.Count - 1
            ? drop with { Rarity = order[index + 1] }
            : drop;
    }

    /// <summary>The ordered Modifier definitions currently *active* on this drop -- i.e. the first ModifierUnlockCount(drop.Rarity) rungs of its ladder.</summary>
    public static IReadOnlyList<ItemModifierDefinition> ActiveModifiers(ItemDrop drop)
    {
        int unlocked = Math.Min(ModifierUnlockCount(drop.Rarity), drop.Definition.ModifierLadder.Count);
        if (unlocked <= 0)
            return Array.Empty<ItemModifierDefinition>();
        var result = new List<ItemModifierDefinition>(unlocked);
        for (int index = 0; index < unlocked; index++)
            if (ModifiersByName.TryGetValue(drop.Definition.ModifierLadder[index], out var modifier))
                result.Add(modifier);
        return result;
    }

    public static ItemSignatureDefinition? ActiveSignature(ItemDrop drop) =>
        SignatureUnlocked(drop.Rarity) ? drop.Definition.Signature : null;

    /// <summary>Every EffectId currently live on this item -- a Unique's fixed Definition-level EffectIds, or (for a regular item) its Signature's EffectIds once Legendary/Mythical unlocks it. Dispatched by UniqueEffects.OnPlayerHit.</summary>
    public static IReadOnlyList<string> ActiveEffectIds(ItemDrop drop)
    {
        if (drop.Definition.EffectIds is { Count: > 0 } baseIds)
            return baseIds;
        return ActiveSignature(drop)?.EffectIds ?? Array.Empty<string>();
    }

    /// <summary>The flavor callout for whichever EffectIds are currently active (Unique base text, or the Signature's once unlocked) -- see ItemDefinition's doc comment for why this is separate from Description.</summary>
    public static string? ActiveEffectFlavorText(ItemDrop drop) =>
        drop.Definition.EffectFlavorText ?? ActiveSignature(drop)?.EffectFlavorText;

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

    /// <summary>
    /// Every currently-active stat source on this drop, unscaled: the item's
    /// own base Modifiers, each unlocked ModifierLadder rung's
    /// StatModifiers, the Signature's StatModifiers once Legendary/Mythical
    /// unlocks it, and any equipped Core Forge. Nothing here multiplies
    /// against a Rarity or Grade power curve anymore -- an authored number
    /// is exactly the number the item grants once its rung unlocks, which is
    /// the whole point of "the item itself holds the power."
    /// </summary>
    public static IReadOnlyList<ItemEffectView> Effects(ItemDrop drop)
    {
        var effects = new List<ItemEffectView>();
        void AddRange(IEnumerable<ItemStatModifier> modifiers) =>
            effects.AddRange(modifiers.Select(modifier => new ItemEffectView(modifier.Stat, modifier.Additive, modifier.Multiplier)));

        AddRange(drop.Definition.Modifiers);
        foreach (var modifier in ActiveModifiers(drop))
            AddRange(modifier.StatModifiers);
        if (ActiveSignature(drop) is { } signature)
            AddRange(signature.StatModifiers);
        if (CoreForgeFor(drop) is { } core)
            AddRange(core.Modifiers);
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
        void AddSource(IReadOnlyDictionary<string, double>? source)
        {
            if (source is null)
                return;
            foreach (var (kind, chance) in source)
                result[kind] = Math.Min(.65, result.GetValueOrDefault(kind) + chance);
        }

        AddSource(drop.Definition.StatusChances);
        foreach (var modifier in ActiveModifiers(drop))
            AddSource(modifier.StatusChances);
        if (ActiveSignature(drop) is { } signature)
            AddSource(signature.StatusChances);
        return result;
    }

    /// <summary>
    /// Every rarity tier from Common up to Mythical, paired with the
    /// Modifier (or, at Legendary/Mythical, the Signature) it unlocks on
    /// this specific item and whether the drop's current Rarity has reached
    /// it yet -- powers the tooltip's unlock-ladder visualizer (see
    /// ItemCards.DrawModifierLadder). Common never gets its own row here:
    /// it's the "nothing unlocked yet" baseline every ladder starts from.
    /// </summary>
    public static IReadOnlyList<ModifierLadderRung> ModifierUnlockPreview(ItemDrop drop)
    {
        var rungs = new List<ModifierLadderRung>();
        var ladder = drop.Definition.ModifierLadder;
        string[] tiers = { "Rare", "Epic", "Legendary", "Mythical" };
        int currentUnlocked = ModifierUnlockCount(drop.Rarity);
        for (int index = 0; index < tiers.Length && index < ladder.Count; index++)
        {
            if (!ModifiersByName.TryGetValue(ladder[index], out var modifier))
                continue;
            rungs.Add(new ModifierLadderRung(tiers[index], index < currentUnlocked, false,
                modifier.Name, modifier.Description));
        }
        if (drop.Definition.Signature is { } signature)
            rungs.Add(new ModifierLadderRung("Legendary", SignatureUnlocked(drop.Rarity), true,
                signature.Name, signature.Description));
        return rungs;
    }

    /// <summary>Developer-only perfect copy; never participates in ordinary drop rolls.</summary>
    public static ItemDrop DeveloperArmoryDrop(ItemDefinition definition) =>
        new(definition, Uniques.Contains(definition) ? "Unique" : "Mythical");

    public static StoredItemData Serialize(ItemDrop drop) =>
        new(drop.Name, drop.Rarity, CoreForge: drop.CoreForge);

    /// <summary>Checked by name against Uniques first (their stored Rarity is always "Unique", which Upgrades.RarityOrder deliberately doesn't contain) before falling back to the regular, tiered-rarity-validated lookup. StoredItemData's legacy Grade/Modifier fields (pre-dating this rarity-ladder rework) are intentionally never read here -- old saves still deserialize fine, they just land on the new system's equivalent (all Modifiers implied by Rarity, no separate roll to restore).</summary>
    public static ItemDrop? Deserialize(StoredItemData? data)
    {
        if (data is null)
            return null;
        if (UniquesByName.TryGetValue(data.Name, out var unique))
            return NormalizeDrop(new ItemDrop(unique, "Unique", data.CoreForge));
        return DefinitionsByName.TryGetValue(data.Name, out var definition) && Upgrades.RarityOrder.Contains(data.Rarity)
            ? NormalizeDrop(new ItemDrop(definition, data.Rarity, data.CoreForge))
            : null;
    }

    private static ItemDrop NormalizeDrop(ItemDrop drop) =>
        drop.CoreForge is not null && (!CoreForgesByKey.ContainsKey(drop.CoreForge) || !IsCoreForgeEligible(drop))
            ? drop with { CoreForge = null }
            : drop;
}
