using Microsoft.Xna.Framework;

namespace RotBoiRemastered.Systems;

public sealed record CosmeticColor(string Id, string Name, Color Color);
public sealed record ProjectilePalette(string Id, string Name, Color Core, Color Edge);
public sealed record ProjectileDesign(string Id, string Name, string Description);

/// <summary>Data-driven, gameplay-neutral wardrobe options persisted in the player profile.</summary>
public static class Cosmetics
{
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
    };

    public static readonly IReadOnlyList<ProjectileDesign> ProjectileDesigns = new[]
    {
        new ProjectileDesign("bulb", "Bulb", "Broad leading bulb with a narrow trailing stem."),
        new ProjectileDesign("shard", "Shard", "A compact faceted point with a squared tail."),
        new ProjectileDesign("lance", "Lance", "Long, narrow and strongly directional."),
        new ProjectileDesign("comet", "Comet", "Round leading core with a tapered wake."),
        new ProjectileDesign("fork", "Fork", "Split trailing fins behind a solid striking head."),
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
        if (!valid) return false;
        if (category == "core") GameProfile.Profile.PlayerCoreColor = id;
        if (category == "edge") GameProfile.Profile.PlayerEdgeColor = id;
        if (category == "projectile") GameProfile.Profile.ProjectileColor = id;
        if (category == "design") GameProfile.Profile.ProjectileDesign = id;
        GameProfile.SaveProfile();
        return true;
    }
}
