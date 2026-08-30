using RotBoiRemastered.Core;
using RotBoiRemastered.Systems;

namespace RotBoiRemastered.UI;

public sealed record StatDisplayDefinition(
    string Id,
    string Abbreviation,
    string IconKey,
    Func<RunState, string> Format,
    string Tooltip);

/// <summary>Shared ordering and compact formatting for player-facing stats.</summary>
public static class StatDisplay
{
    public static readonly IReadOnlyList<StatDisplayDefinition> Definitions =
    [
        new("damage", "DMG", "Bullet Damage", s => $"{s.BulletDamage:N0}", "Damage dealt by each normal projectile hit."),
        new("attack_rate", "A.R.", "Attack Speed", s => $"{AttacksPerSecond(s):0.00}/s", "Volleys fired each second."),
        new("shots", "SHOT", "Bullet Count", s => $"{ExpectedProjectiles(s):0.00}", "Expected projectiles per volley; the decimal is the bonus-shot chance."),
        new("crit_chance", "C.%", "Crit Chance", s => $"{s.CritChance * 100:0}%", "Chance for a projectile to deal critical damage."),
        new("crit_damage", "C.D.", "Crit Damage", s => $"+{Math.Max(0, s.CritDamage - 1) * 100:0}%", "Extra damage dealt by a critical hit."),
        new("pierce", "PRC", "Bullet Pierce", s => $"{ExpectedEnemiesHit(s):0.00}", "Expected enemies hit by one projectile, including its first target."),
        new("defense", "DEF", "Defense", s => $"{s.Defense:N0}", "Flat damage removed from each incoming hit."),
        new("vitality", "VIT", "Vitality", s => $"{s.Vitality:N0}/s", "Health recovered each second."),
        new("move_speed", "MOV", "Player Speed", s => $"{s.PlayerSpeed:0.00}", "Player movement speed."),
        new("range", "RNG", "Bullet Range", s => $"{s.BulletRange / Simulation.TileSize:0.0}t", "Projectile travel distance in tiles."),
        new("bullet_speed", "B.S.", "Bullet Speed", s => $"{s.BulletSpeed:0.00}", "Projectile travel speed."),
        new("bullet_size", "SIZE", "Bullet Size", s => $"{s.BulletSize:0.00}x", "Projectile size multiplier."),
    ];

    public static double AttacksPerSecond(RunState state) =>
        Simulation.FrameRate / Math.Max(1, state.AttackCooldownStat);

    public static double ExpectedProjectiles(RunState state) => state.ProjectileCount;

    public static double ExpectedEnemiesHit(RunState state) => state.BulletPierce + 1;

    public static StatDisplayDefinition ById(string id) =>
        Definitions.First(definition => definition.Id == id);

    /// <summary>
    /// The Mind's footer stat grid used to sit mostly empty (no run is active
    /// there, so combat Definitions has nothing to show) -- this is its own
    /// 10-card set of meta-progression numbers instead, filling the same
    /// StatSlots geometry FooterHud already reserves. Format still takes a
    /// RunState (matching StatDisplayDefinition's shape and letting the
    /// carried-stash card read live run data) but every other card reads
    /// permanent profile/campaign state that exists outside any run.
    /// </summary>
    public static readonly IReadOnlyList<StatDisplayDefinition> HubDefinitions =
    [
        new("mind_tokens", "TOK", "Mind Tokens", _ => $"{GameProfile.Profile.MindTokens:N0}",
            "Permanent currency earned from runs, spent on skills and Vault space."),
        new("vault", "VLT", "Vault",
            _ => $"{GameProfile.Profile.Storage.Count}/{MetaProgression.StorageCapacity}",
            "Items held in permanent storage, safe between runs."),
        new("carried_stash", "STSH", "Carried Stash",
            s => $"{s.Inventory.Count(item => item is not null)}/{InformationSheet.InventorySlotCount}",
            "Items currently carried into your next run."),
        new("quests", "QST", "Quests",
            _ => $"{GameProfile.Profile.CompletedQuests.Count}/{MetaProgression.Quests.Count}",
            "Meta-progression quests completed."),
        new("skills", "SKL", "Skills",
            _ => $"{GameProfile.Profile.SkillLevels.Count(pair => pair.Value > 0)}/{MetaProgression.SkillNodes.Count}",
            "Permanent skill nodes purchased in the Mind's skill tree."),
        new("best_dps", "DPS", "Best DPS", _ => $"{GameProfile.Profile.BestDummyDps:N0}",
            "Highest damage-per-second recorded on the training dummy."),
        new("new_game_plus", "NG+", "New Game Plus",
            _ => $"{(GameProfile.Profile.NewGamePlusUnlocked.Count == 0 ? 0 : GameProfile.Profile.NewGamePlusUnlocked.Values.Max())}",
            "Highest New Game Plus tier unlocked on any path."),
        new("statues", "STAT", "Statues",
            _ => $"{CampaignProgression.SenseKeys.Count(sense => CampaignProgression.Data.SilverStatues.GetValueOrDefault(sense)?.Unlocked == true) + CampaignProgression.SenseKeys.Count(sense => CampaignProgression.Data.GoldStatues.GetValueOrDefault(sense)?.Unlocked == true)}/10",
            "Silver and Gold sense statues unlocked."),
        new("campaign", "CMP", "Campaign",
            _ => CampaignProgression.Data.SoulUnlocked ? "SOUL" : CampaignProgression.Data.BodyUnlocked ? "BODY" : "BODY LOCKED",
            "Which stage of the linear Mind campaign is currently open."),
        new("discoveries", "DISC", "Discoveries", _ => $"{GameProfile.Profile.DiscoveredItems.Count}",
            "Distinct items identified so far across all runs."),
    ];
}
