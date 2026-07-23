using Microsoft.Xna.Framework;
using RotBoiRemastered.UI;

namespace RotBoiRemastered.Entities;

/// <summary>Ported from enemyTypes.py's TIER_BALANCE dict values.</summary>
public readonly record struct TierBalanceEntry(int Rank, double Speed, double Health, double Damage, double Experience, double Threat);

/// <summary>Ported from enemyTypes.py's FAMILY_IDENTITIES dict values: (combat_role, tags).</summary>
public readonly record struct FamilyIdentity(string CombatRole, IReadOnlySet<string> Tags);

/// <summary>Ported from enemyTypes.py's MODIFIER_RULES dict values.</summary>
public readonly record struct ModifierRule(int MinLevel, IReadOnlySet<string> Roles, Color Color);

/// <summary>Ported from enemyTypes.py's EncounterPackage frozen dataclass.</summary>
public sealed record EncounterPackage(
    string Key, int MinLevel, int MaxLevel, IReadOnlyList<string> Families,
    double Weight = 1.0, int MaxConcurrent = 1);

/// <summary>
/// Shared tables/constants from the top of enemyTypes.py. Pure data, no
/// pygame/rendering dependency beyond the modifier accent colors.
/// </summary>
public static class EnemyCatalogData
{
    public const double BaseEnemySpeedScale = .66;

    public static readonly IReadOnlyDictionary<string, TierBalanceEntry> TierBalance = new Dictionary<string, TierBalanceEntry>
    {
        ["easy"] = new TierBalanceEntry(1, 1.0, 1.0, 1.0, 1.0, 1.0),
        ["medium"] = new TierBalanceEntry(2, 1.06, 1.45, 1.28, 1.65, 1.55),
        ["hard"] = new TierBalanceEntry(3, 1.12, 2.05, 1.65, 2.5, 2.25),
    };

    public static readonly IReadOnlyDictionary<string, FamilyIdentity> FamilyIdentities = new Dictionary<string, FamilyIdentity>
    {
        ["runner"] = new FamilyIdentity("pressure", new HashSet<string> { "melee", "mobile" }),
        ["drifter"] = new FamilyIdentity("pressure", new HashSet<string> { "melee", "mobile" }),
        ["skirmisher"] = new FamilyIdentity("pressure", new HashSet<string> { "melee", "flanker" }),
        ["bulwark"] = new FamilyIdentity("tank", new HashSet<string> { "melee", "durable" }),
        ["ranged_wanderer"] = new FamilyIdentity("artillery", new HashSet<string> { "ranged", "mobile" }),
        ["shotgunner"] = new FamilyIdentity("artillery", new HashSet<string> { "ranged", "close_range" }),
        ["snake"] = new FamilyIdentity("tank", new HashSet<string> { "composite", "ranged" }),
        ["parent"] = new FamilyIdentity("squad", new HashSet<string> { "summoner", "ranged" }),
        ["pillar"] = new FamilyIdentity("control", new HashSet<string> { "stationary", "radial" }),
        ["volley"] = new FamilyIdentity("artillery", new HashSet<string> { "ranged", "cone" }),
        ["laser"] = new FamilyIdentity("control", new HashSet<string> { "ranged", "beam" }),
        ["bomb"] = new FamilyIdentity("control", new HashSet<string> { "ranged", "area" }),
        ["banner"] = new FamilyIdentity("squad", new HashSet<string> { "leader", "melee" }),
        ["rammer"] = new FamilyIdentity("pressure", new HashSet<string> { "charge", "terrain" }),
        ["warder"] = new FamilyIdentity("support", new HashSet<string> { "shield", "ranged" }),
        ["splitter"] = new FamilyIdentity("artillery", new HashSet<string> { "ranged", "splitting" }),
        ["collector"] = new FamilyIdentity("economy", new HashSet<string> { "mobile", "xp" }),
        ["sound_echoer"] = new FamilyIdentity("artillery", new HashSet<string> { "ranged", "sound", "wave" }),
        ["sound_resonator"] = new FamilyIdentity("control", new HashSet<string> { "ranged", "sound", "radial" }),
        ["touch_clasper"] = new FamilyIdentity("control", new HashSet<string> { "ranged", "touch", "heavy" }),
        ["touch_mirekeeper"] = new FamilyIdentity("control", new HashSet<string> { "ranged", "touch", "area" }),
        ["sight_blinker"] = new FamilyIdentity("artillery", new HashSet<string> { "ranged", "sight", "quick" }),
        ["sight_lens"] = new FamilyIdentity("control", new HashSet<string> { "ranged", "sight", "beam" }),
        ["chem_cinderpod"] = new FamilyIdentity("control", new HashSet<string> { "ranged", "chemesthetic", "minefield" }),
        ["chem_sporecaster"] = new FamilyIdentity("artillery", new HashSet<string> { "ranged", "chemesthetic", "splitting" }),
        ["phantasia_mirage"] = new FamilyIdentity("artillery", new HashSet<string> { "ranged", "phantasian", "illusion" }),
        ["phantasia_dreamweaver"] = new FamilyIdentity("control", new HashSet<string> { "ranged", "phantasian", "orbit" }),
        ["miniboss"] = new FamilyIdentity("elite", new HashSet<string> { "phased" }),
    };

    public static readonly IReadOnlyDictionary<string, ModifierRule> ModifierRules = new Dictionary<string, ModifierRule>
    {
        ["hasty"] = new ModifierRule(5, new HashSet<string> { "pressure", "artillery" }, UiTheme.Gold),
        ["armored"] = new ModifierRule(6, new HashSet<string> { "tank", "support", "squad" }, UiTheme.Blue),
        ["volatile"] = new ModifierRule(8, new HashSet<string> { "pressure", "control" }, UiTheme.Red),
        ["regenerating"] = new ModifierRule(10, new HashSet<string> { "tank", "artillery", "support" }, UiTheme.Green),
        ["champion"] = new ModifierRule(12, new HashSet<string> { "pressure", "tank", "artillery", "control" }, UiTheme.Purple),
    };

    public static readonly IReadOnlyList<EncounterPackage> EncounterPackages = new[]
    {
        new EncounterPackage("shield_wall", 5, 14, new[] { "warder", "shotgunner", "shotgunner" }, 1.2),
        new EncounterPackage("royal_procession", 6, 14, new[] { "banner", "collector" }, .8),
        new EncounterPackage("demolition_crew", 7, 18, new[] { "rammer", "bomb", "bomb" }, 1.0),
        new EncounterPackage("crossfire", 7, 17, new[] { "pillar", "volley", "volley" }, 1.0),
        new EncounterPackage("brood_guard", 8, 18, new[] { "parent", "warder" }, .8),
        new EncounterPackage("fractured_choir", 10, 20, new[] { "splitter", "splitter", "laser" }, 1.0),
        new EncounterPackage("stampede", 11, 20, new[] { "banner", "rammer" }, .7),
        new EncounterPackage("salvage_team", 6, 16, new[] { "collector", "bulwark", "ranged_wanderer" }, .9),
    };

    /// <summary>Normalized direction plus original length, matching Python's `_normalise(x, y)` -> (dx, dy, length).</summary>
    public static (float DirX, float DirY, float Length) Normalise(float x, float y)
    {
        float length = Math.Max(1.0f, MathF.Sqrt(x * x + y * y));
        return (x / length, y / length, length);
    }
}
