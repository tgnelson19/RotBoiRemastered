using Microsoft.Xna.Framework;

namespace RotBoiRemastered.Systems;

/// <summary>
/// Gameplay-neutral wardrobe options persisted in the player profile.
/// <paramref name="Tier"/> 0 is always unlocked (the original launch catalog, plus anything
/// grandfathered by GameProfile.Normalize). Tiers 1-3 require <paramref name="UnlockCondition"/>
/// to return true against the live profile -- see Cosmetics.IsUnlocked. <paramref name="UnlockHint"/>
/// is shown to the player for tier 1/2 options so the goal is visible; tier 3 options intentionally
/// show "???" instead (see Cosmetics.LockDescription) to keep the rarest looks a surprise.
/// </summary>
public sealed record CosmeticColor(
    string Id, string Name, Color Color,
    int Tier = 0, string? UnlockHint = null, Func<GameProfileData, bool>? UnlockCondition = null);

public sealed record ProjectilePalette(
    string Id, string Name, Color Core, Color Edge,
    int Tier = 0, string? UnlockHint = null, Func<GameProfileData, bool>? UnlockCondition = null);

public sealed record ProjectileDesign(
    string Id, string Name, string Description,
    int Tier = 0, string? UnlockHint = null, Func<GameProfileData, bool>? UnlockCondition = null);

/// <summary>Data-driven, gameplay-neutral wardrobe options persisted in the player profile.</summary>
public static class Cosmetics
{
    public const string LockedHint = "???";

    // ---- Unlock condition helpers -------------------------------------------------
    // Every condition here reads from permanent, monotonic profile state (an
    // ever-increasing QuestProgress counter or a one-way flag), so once true it stays
    // true -- Cosmetics.IsUnlocked can simply re-evaluate it live with no extra bookkeeping.
    private static long Counter(GameProfileData profile, string key) =>
        profile.QuestProgress.GetValueOrDefault(key);

    private static Func<GameProfileData, bool> AtLeast(string counterKey, long target) =>
        profile => Counter(profile, counterKey) >= target;

    private static readonly Func<GameProfileData, bool> DefeatedCoreOfTheVoid = profile => profile.DefeatedCoreOfTheVoid;
    private static readonly Func<GameProfileData, bool> NoReforgeRun = profile => profile.NoReforgeRunCompleted;
    private static readonly Func<GameProfileData, bool> HardModeRun = profile => profile.HardModeRunCompleted;

    public static readonly IReadOnlyList<CosmeticColor> CoreColors = new[]
    {
        new CosmeticColor("midnight", "Midnight", new Color(0, 0, 120)),
        new CosmeticColor("cobalt", "Cobalt", new Color(42, 72, 196)),
        new CosmeticColor("sky", "Sky", new Color(30, 158, 218)),
        new CosmeticColor("teal", "Teal", new Color(20, 139, 145)),
        new CosmeticColor("emerald", "Emerald", new Color(34, 157, 91)),
        new CosmeticColor("lime", "Lime", new Color(132, 190, 58)),
        new CosmeticColor("amber", "Amber", new Color(224, 170, 46)),
        new CosmeticColor("ember", "Ember", new Color(218, 92, 42)),
        new CosmeticColor("crimson", "Crimson", new Color(190, 45, 66)),
        new CosmeticColor("rose", "Rose", new Color(211, 72, 137)),
        new CosmeticColor("violet", "Violet", new Color(126, 75, 200)),
        new CosmeticColor("ivory", "Ivory", new Color(226, 218, 194)),

        // Tier 1 -- roughly one run.
        new CosmeticColor("coral", "Coral", new Color(240, 128, 101),
            1, "Extract from one run.", AtLeast("runs_extracted", 1)),
        new CosmeticColor("moss", "Moss", new Color(94, 122, 60),
            1, "Defeat 20 enemies.", AtLeast("enemies_defeated", 20)),
        new CosmeticColor("steel", "Steel", new Color(120, 132, 148),
            1, "Travel 2,000 world units.", AtLeast("distance_traveled", 2000)),

        // Tier 2 -- roughly 5-15 runs.
        new CosmeticColor("orchid", "Orchid", new Color(176, 96, 168),
            2, "Complete 3 paths.", AtLeast("path_clears", 3)),
        new CosmeticColor("citrine", "Citrine", new Color(196, 168, 54),
            2, "Discover 18 distinct items.", AtLeast("items_found", 18)),
        new CosmeticColor("abyss", "Abyss", new Color(24, 40, 64),
            2, "Complete a run in Hard Mode.", HardModeRun),

        // Tier 3 -- roughly 25+ runs, or True Hard Mode. Hint deliberately hidden.
        new CosmeticColor("voidbloom", "Voidbloom", new Color(72, 20, 92),
            3, LockedHint, DefeatedCoreOfTheVoid),
        new CosmeticColor("starlight", "Starlight", new Color(232, 238, 255),
            3, LockedHint, AtLeast("runs_extracted", 25)),
        new CosmeticColor("corebound", "Corebound", new Color(46, 18, 74),
            3, LockedHint, AtLeast("bosses_defeated", 30)),
    };

    public static readonly IReadOnlyList<CosmeticColor> EdgeColors = new[]
    {
        new CosmeticColor("ink", "Ink", new Color(18, 20, 27)),
        new CosmeticColor("slate", "Slate", new Color(63, 72, 88)),
        new CosmeticColor("white", "White", new Color(238, 241, 232)),
        new CosmeticColor("ice", "Ice", new Color(117, 220, 232)),
        new CosmeticColor("azure", "Azure", new Color(28, 151, 226)),
        new CosmeticColor("mint", "Mint", new Color(91, 220, 157)),
        new CosmeticColor("acid", "Acid", new Color(190, 226, 69)),
        new CosmeticColor("gold", "Gold", new Color(239, 190, 65)),
        new CosmeticColor("flame", "Flame", new Color(242, 105, 55)),
        new CosmeticColor("blood", "Blood", new Color(224, 53, 72)),
        new CosmeticColor("pink", "Pink", new Color(235, 92, 183)),
        new CosmeticColor("arcane", "Arcane", new Color(169, 105, 235)),

        // Tier 1
        new CosmeticColor("sand", "Sand", new Color(214, 193, 146),
            1, "Land 10 critical hits.", AtLeast("critical_hits", 10)),
        new CosmeticColor("fern", "Fern", new Color(108, 150, 96),
            1, "Discover 5 distinct items.", AtLeast("items_found", 5)),
        new CosmeticColor("storm", "Storm", new Color(96, 108, 128),
            1, "Fire 150 projectiles.", AtLeast("shots_fired", 150)),

        // Tier 2
        new CosmeticColor("sunset", "Sunset", new Color(232, 140, 84),
            2, "Kill 50 Phantasia minions.", AtLeast("kills_sense_phantasia", 50)),
        new CosmeticColor("glacier", "Glacier", new Color(150, 210, 224),
            2, "Extract 6 runs.", AtLeast("runs_extracted", 6)),
        new CosmeticColor("wine", "Wine", new Color(110, 32, 58),
            2, "Complete a run without using a reforge token.", NoReforgeRun),

        // Tier 3 -- hidden hint.
        new CosmeticColor("eclipse", "Eclipse", new Color(18, 14, 28),
            3, LockedHint, DefeatedCoreOfTheVoid),
        new CosmeticColor("moonveil", "Moonveil", new Color(206, 214, 255),
            3, LockedHint, AtLeast("damage_dealt", 800000)),
        new CosmeticColor("corelight", "Corelight", new Color(196, 120, 255),
            3, LockedHint, DefeatedCoreOfTheVoid),
    };

    public static readonly IReadOnlyList<ProjectilePalette> ProjectileColors = new[]
    {
        new ProjectilePalette("reference", "Reference Blue", new Color(70, 72, 204), new Color(8, 164, 225)),
        new ProjectilePalette("ghost", "Ghost Light", new Color(219, 230, 242), new Color(112, 200, 234)),
        new ProjectilePalette("verdant", "Verdant", new Color(33, 133, 82), new Color(93, 224, 144)),
        new ProjectilePalette("toxic", "Toxic", new Color(114, 156, 42), new Color(207, 239, 64)),
        new ProjectilePalette("solar", "Solar", new Color(216, 137, 34), new Color(255, 218, 74)),
        new ProjectilePalette("ember", "Ember", new Color(188, 54, 36), new Color(248, 114, 49)),
        new ProjectilePalette("blood", "Blood Moon", new Color(132, 31, 55), new Color(232, 55, 81)),
        new ProjectilePalette("rose", "Roseglass", new Color(159, 48, 116), new Color(241, 104, 184)),
        new ProjectilePalette("arcane", "Arcane", new Color(83, 55, 166), new Color(169, 101, 232)),
        new ProjectilePalette("void", "Void", new Color(37, 36, 60), new Color(107, 91, 157)),
        new ProjectilePalette("copper", "Copper", new Color(129, 72, 39), new Color(222, 139, 73)),
        new ProjectilePalette("mono", "Monochrome", new Color(101, 109, 122), new Color(229, 232, 224)),

        // Tier 1
        new ProjectilePalette("amber_dust", "Amber Dust", new Color(214, 150, 60), new Color(250, 214, 120),
            1, "Deal 5,000 damage to the DPS effigy.", AtLeast("dummy_damage", 5000)),
        new ProjectilePalette("seafoam", "Seafoam", new Color(52, 142, 132), new Color(146, 224, 210),
            1, "Reach level 8.", profile => profile.BestLevel >= 8),
        new ProjectilePalette("clay", "Clay", new Color(150, 92, 58), new Color(214, 150, 104),
            1, "Extract from one run.", AtLeast("runs_extracted", 1)),

        // Tier 2
        new ProjectilePalette("orchidglow", "Orchid Glow", new Color(120, 58, 140), new Color(214, 120, 220),
            2, "Defeat 10 bosses.", AtLeast("bosses_defeated", 10)),
        new ProjectilePalette("frostbite", "Frostbite", new Color(60, 120, 160), new Color(160, 224, 255),
            2, "Deal 60,000 damage to the DPS effigy.", AtLeast("dummy_damage", 60000)),
        new ProjectilePalette("wildfire", "Wildfire", new Color(180, 50, 20), new Color(255, 140, 40),
            2, "Land 350 critical hits.", AtLeast("critical_hits", 350)),

        // Tier 3 -- hidden hint.
        new ProjectilePalette("corevoid", "Core of the Void", new Color(30, 10, 42), new Color(150, 60, 220),
            3, LockedHint, DefeatedCoreOfTheVoid),
        new ProjectilePalette("starforge", "Starforge", new Color(60, 60, 90), new Color(255, 255, 240),
            3, LockedHint, AtLeast("kills_sense_touch", 400)),
        new ProjectilePalette("phantom_veil", "Phantom Veil", new Color(40, 44, 60), new Color(220, 220, 255),
            3, LockedHint, AtLeast("critical_hits", 1500)),
    };

    public static readonly IReadOnlyList<ProjectileDesign> ProjectileDesigns = new[]
    {
        new ProjectileDesign("bulb", "Bulb", "Broad leading bulb with a narrow trailing stem."),
        new ProjectileDesign("shard", "Shard", "A compact faceted point with a squared tail."),
        new ProjectileDesign("lance", "Lance", "Long, narrow and strongly directional."),
        new ProjectileDesign("comet", "Comet", "Round leading core with a tapered wake."),
        new ProjectileDesign("fork", "Fork", "Split trailing fins behind a solid striking head."),
        new ProjectileDesign("prism", "Prism", "A tumbling faceted diamond."),
        new ProjectileDesign("cog", "Cog", "A hard-edged spinning gear."),
        new ProjectileDesign("satellite", "Satellite", "A core escorted by two orbiting pixels."),
        new ProjectileDesign("wave", "Wave", "A flexing, directional waveform."),
        new ProjectileDesign("sigil", "Cross-Sigil", "A rotating geometric cross."),

        // Tier 1
        new ProjectileDesign("arrow", "Arrow", "A simple, direct arrowhead.",
            1, "Defeat 20 enemies.", AtLeast("enemies_defeated", 20)),
        new ProjectileDesign("orb", "Orb", "A plain, rounded shot with no facets.",
            1, "Extract from one run.", AtLeast("runs_extracted", 1)),

        // Tier 2
        new ProjectileDesign("blade", "Blade", "A single-edged blade tilted hard into its swing.",
            2, "Complete 3 paths.", AtLeast("path_clears", 3)),
        new ProjectileDesign("spark", "Spark", "A jagged bolt of stored current.",
            2, "Discover 18 distinct items.", AtLeast("items_found", 18)),
        new ProjectileDesign("banner", "Banner", "A trailing ribbon that ripples as it flies.",
            2, "Complete a run in Hard Mode.", HardModeRun),

        // Tier 3 -- animated, hidden hint, reserved for True Hard Mode.
        new ProjectileDesign("halo", "Halo", "A rotating ring of orbiting motes around a bright core.",
            3, LockedHint, DefeatedCoreOfTheVoid),
        new ProjectileDesign("specter", "Specter", "A phasing double-image that flickers as it drifts.",
            3, LockedHint, DefeatedCoreOfTheVoid),
    };

    public static CosmeticColor SelectedCore => Find(CoreColors, GameProfile.Profile.PlayerCoreColor, "midnight");
    public static CosmeticColor SelectedEdge => Find(EdgeColors, GameProfile.Profile.PlayerEdgeColor, "ink");
    public static ProjectilePalette SelectedProjectile => Find(ProjectileColors, GameProfile.Profile.ProjectileColor, "reference");
    public static ProjectileDesign SelectedDesign => Find(ProjectileDesigns, GameProfile.Profile.ProjectileDesign, "bulb");

    private static T Find<T>(IReadOnlyList<T> options, string id, string fallback) where T : class
    {
        static string Key(T value) => value switch
        {
            CosmeticColor color => color.Id,
            ProjectilePalette palette => palette.Id,
            ProjectileDesign design => design.Id,
            _ => "",
        };
        return options.FirstOrDefault(option => Key(option) == id)
            ?? options.First(option => Key(option) == fallback);
    }

    private static (int Tier, string? Hint, Func<GameProfileData, bool>? Condition)? Lookup(string category, string id)
    {
        switch (category)
        {
            case "core":
                foreach (var option in CoreColors)
                    if (option.Id == id) return (option.Tier, option.UnlockHint, option.UnlockCondition);
                return null;
            case "edge":
                foreach (var option in EdgeColors)
                    if (option.Id == id) return (option.Tier, option.UnlockHint, option.UnlockCondition);
                return null;
            case "projectile":
                foreach (var option in ProjectileColors)
                    if (option.Id == id) return (option.Tier, option.UnlockHint, option.UnlockCondition);
                return null;
            case "design":
                foreach (var option in ProjectileDesigns)
                    if (option.Id == id) return (option.Tier, option.UnlockHint, option.UnlockCondition);
                return null;
            default:
                return null;
        }
    }

    /// <summary>True if this cosmetic is either free (tier 0), grandfathered, or its unlock condition is currently met.</summary>
    public static bool IsUnlocked(string category, string id)
    {
        if (Lookup(category, id) is not { } entry)
            return false;
        if (entry.Tier <= 0)
            return true;
        if (GameProfile.Profile.UnlockedCosmetics.Contains($"{category}:{id}"))
            return true;
        return entry.Condition?.Invoke(GameProfile.Profile) ?? true;
    }

    /// <summary>What to show the player for a locked option: the real hint for tiers 1-2, "???" for tier 3.</summary>
    public static string? LockDescription(string category, string id) =>
        Lookup(category, id)?.Hint;

    public static bool Select(string category, string id)
    {
        bool valid = category switch
        {
            "core" => CoreColors.Any(option => option.Id == id),
            "edge" => EdgeColors.Any(option => option.Id == id),
            "projectile" => ProjectileColors.Any(option => option.Id == id),
            "design" => ProjectileDesigns.Any(option => option.Id == id),
            _ => false,
        };
        if (!valid || !IsUnlocked(category, id)) return false;
        if (category == "core") GameProfile.Profile.PlayerCoreColor = id;
        if (category == "edge") GameProfile.Profile.PlayerEdgeColor = id;
        if (category == "projectile") GameProfile.Profile.ProjectileColor = id;
        if (category == "design") GameProfile.Profile.ProjectileDesign = id;
        GameProfile.SaveProfile();
        return true;
    }
}
