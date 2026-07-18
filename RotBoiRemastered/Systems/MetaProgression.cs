namespace RotBoiRemastered.Systems;

public sealed record SkillNode(
    string Key, string Symbol, string Name, string Description, string Stat,
    double ValuePerLevel, string Mode = "additive", int MaxLevel = 5, int BaseCost = 1);

public sealed record QuestDefinition(
    string Key, string Symbol, string Name, string Description, string Counter, long Target, int Reward = 1);

/// <summary>Permanent, UI-independent progression rules shared by the Soul and run startup.</summary>
public static class MetaProgression
{
    public const int StorageCapacity = 10;

    public static readonly IReadOnlyList<SkillNode> SkillNodes = new[]
    {
        new SkillNode("tempered_soul", "DMG", "Tempered Soul", "+2% damage per rank", "Bullet Damage", 1.02, "multiplicative"),
        new SkillNode("quick_memory", "SPD", "Quick Memory", "+2% movement speed per rank", "Player Speed", 1.02, "multiplicative"),
        new SkillNode("deep_reserve", "HP", "Deep Reserve", "+30 maximum health per rank", "Health", 30),
        new SkillNode("patient_hands", "CRT", "Patient Hands", "+1% critical chance per rank", "Crit Chance", .01),
        new SkillNode("wide_grasp", "AUR", "Wide Grasp", "+8 pickup range per rank", "Aura Size", 8),
        new SkillNode("soul_mail", "DEF", "Soul Mail", "+2 defense per rank", "Defense", 2),
        new SkillNode("steady_breath", "VIT", "Steady Breath", "+3 vitality per rank", "Vitality", 3),
        new SkillNode("far_memory", "RNG", "Far Memory", "+3% projectile range per rank", "Bullet Range", 1.03, "multiplicative"),
        new SkillNode("lively_hands", "RATE", "Lively Hands", "2% faster attacks per rank", "Attack Speed", .98, "multiplicative"),
        new SkillNode("keen_echo", "XP", "Keen Echo", "+3% experience per rank", "Exp Multiplier", 1.03, "multiplicative"),
        new SkillNode("heavy_echo", "SIZE", "Heavy Echo", "+2% projectile size per rank", "Bullet Size", 1.02, "multiplicative"),
        new SkillNode("straight_thought", "SHOT", "Straight Thought", "+3% projectile speed per rank", "Bullet Speed", 1.03, "multiplicative"),
    };

    public static readonly IReadOnlyDictionary<string, SkillNode> SkillNodesByKey = SkillNodes.ToDictionary(node => node.Key);

    public static readonly IReadOnlyList<QuestDefinition> Quests = new[]
    {
        new QuestDefinition("first_steps", "SKULL", "First Steps", "Defeat 50 enemies across any runs.", "enemies_defeated", 50),
        new QuestDefinition("crowd_control", "SKULL", "Crowd Control", "Defeat 250 enemies.", "enemies_defeated", 250),
        new QuestDefinition("harvester", "SKULL", "Harvester", "Defeat 1,000 enemies.", "enemies_defeated", 1000, 2),
        new QuestDefinition("curator", "CHEST", "Curator", "Discover 8 distinct items.", "items_found", 8),
        new QuestDefinition("full_shelf", "CHEST", "Full Shelf", "Discover 20 distinct items.", "items_found", 20, 2),
        new QuestDefinition("pathwalker", "PATH", "Pathwalker", "Complete 2 paths.", "path_clears", 2),
        new QuestDefinition("wayfinder", "PATH", "Wayfinder", "Complete 5 paths.", "path_clears", 5, 2),
        new QuestDefinition("afflictor", "DROP", "Afflictor", "Apply 100 status effects.", "statuses_applied", 100),
        new QuestDefinition("plague_hand", "DROP", "Plague Hand", "Apply 500 status effects.", "statuses_applied", 500, 2),
        new QuestDefinition("steady_fire", "SHOT", "Steady Fire", "Fire 1,000 projectiles.", "shots_fired", 1000),
        new QuestDefinition("storm", "SHOT", "Storm", "Fire 10,000 projectiles.", "shots_fired", 10000, 2),
        new QuestDefinition("keen_eye", "CRIT", "Keen Eye", "Land 100 critical hits.", "critical_hits", 100),
        new QuestDefinition("dead_center", "CRIT", "Dead Center", "Land 1,000 critical hits.", "critical_hits", 1000, 2),
        new QuestDefinition("hurt_the_rot", "DMG", "Hurt the Rot", "Deal 100,000 damage.", "damage_dealt", 100000),
        new QuestDefinition("break_the_rot", "DMG", "Break the Rot", "Deal 1,000,000 damage.", "damage_dealt", 1000000, 2),
        new QuestDefinition("boss_hunter", "CROWN", "Boss Hunter", "Defeat 3 bosses.", "bosses_defeated", 3),
        new QuestDefinition("boss_memory", "CROWN", "Boss Memory", "Defeat 15 bosses.", "bosses_defeated", 15, 2),
        new QuestDefinition("safe_return", "DOOR", "Safe Return", "Extract one run.", "runs_extracted", 1),
        new QuestDefinition("salvager", "DOOR", "Salvager", "Extract ten runs.", "runs_extracted", 10, 2),
        new QuestDefinition("student", "LEVEL", "Student", "Gain 25 levels.", "levels_gained", 25),
        new QuestDefinition("veteran", "LEVEL", "Veteran", "Gain 200 levels.", "levels_gained", 200, 2),
        new QuestDefinition("long_walk", "BOOT", "Long Walk", "Travel 10,000 world units.", "distance_traveled", 10000),
        new QuestDefinition("measured_force", "DPS", "Measured Force", "Deal 25,000 damage to the DPS effigy.", "dummy_damage", 25000),
        new QuestDefinition("living_weapon", "DPS", "Living Weapon", "Deal 250,000 damage to the DPS effigy.", "dummy_damage", 250000, 2),
    };

    public static void CompleteReadyQuests(bool save = true)
    {
        bool changed = false;
        foreach (var quest in Quests)
        {
            if (GameProfile.Profile.CompletedQuests.Contains(quest.Key)
                || GameProfile.Profile.QuestProgress.GetValueOrDefault(quest.Counter) < quest.Target)
                continue;
            GameProfile.Profile.CompletedQuests.Add(quest.Key);
            GameProfile.Profile.SoulTokens += quest.Reward;
            changed = true;
        }
        if (changed && save)
            GameProfile.SaveProfile();
    }

    public static bool PurchaseSkill(string key)
    {
        if (!SkillNodesByKey.TryGetValue(key, out var node))
            return false;
        int level = GameProfile.Profile.SkillLevels.GetValueOrDefault(key);
        int cost = node.BaseCost + level / 2;
        if (level >= node.MaxLevel || GameProfile.Profile.SoulTokens < cost)
            return false;
        GameProfile.Profile.SoulTokens -= cost;
        GameProfile.Profile.SkillLevels[key] = level + 1;
        GameProfile.SaveProfile();
        return true;
    }

    public static void ApplySkills(RunState state)
    {
        foreach (var (key, level) in GameProfile.Profile.SkillLevels)
        {
            if (!SkillNodesByKey.TryGetValue(key, out var node) || !state.Stats.TryGetValue(node.Stat, out var stat))
                continue;
            for (int rank = 0; rank < Math.Min(level, node.MaxLevel); rank++)
            {
                if (node.Mode == "multiplicative")
                    stat.Multiplicative.Add(node.ValuePerLevel);
                else
                    stat.Additive.Add(node.ValuePerLevel);
            }
        }
    }

    /// <summary>
    /// Writes the currently carried loadout (Equipment + Inventory) back to the
    /// profile so it survives past this run -- call whenever a run ends
    /// *without* dying (extract, complete, or a plain restart from the pause
    /// menu). GameSession.LoadCarriedItems is the inverse, reading this back
    /// into a fresh RunState.
    /// </summary>
    public static void SyncCarriedItems(RunState state)
    {
        GameProfile.Profile.CarriedEquipment = state.Equipment
            .Where(pair => pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => Items.Serialize(pair.Value!));
        GameProfile.Profile.CarriedInventory = state.Inventory
            .Select(item => item is null ? null : Items.Serialize(item))
            .ToList();
        GameProfile.SaveProfile();
    }

    /// <summary>The one and only "you died" path -- everything carried (not vaulted) is lost.</summary>
    public static void ClearCarriedItems()
    {
        GameProfile.Profile.CarriedEquipment.Clear();
        // Matches RunState.Inventory's fixed 8 slots (see its Reset()) -- kept as a literal
        // here too rather than a cross-layer reference to the UI's InventorySlotCount const.
        GameProfile.Profile.CarriedInventory = Enumerable.Repeat<StoredItemData?>(null, 8).ToList();
        GameProfile.SaveProfile();
    }

    public static void RecordExtraction(RunState state, string path, bool completed)
    {
        var run = new ExtractedRunData
        {
            Path = path,
            Outcome = completed ? "PATH COMPLETE" : "EXTRACTED",
            Level = state.CurrentLevel,
            Kills = state.NumOfEnemiesKilled,
            Seconds = state.RunTimeSeconds,
        };
        GameProfile.Profile.ExtractedRuns.Insert(0, run);
        if (GameProfile.Profile.ExtractedRuns.Count > 10)
            GameProfile.Profile.ExtractedRuns.RemoveRange(10, GameProfile.Profile.ExtractedRuns.Count - 10);
        GameProfile.IncrementQuest("runs_extracted");
        if (completed)
        {
            GameProfile.IncrementQuest("path_clears");
            GameProfile.Profile.PathMastery[path] = GameProfile.Profile.PathMastery.GetValueOrDefault(path) + 1;
        }
        GameProfile.SaveProfile();
    }
}
