using System.Reflection;
using System.Text.Json;
using RotBoiRemastered.Core;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Systems;

/// <summary>
/// The persisted settings blob. Ported from gameProfile.py's DEFAULTS dict.
/// Unlike the Python original's loosely-typed dict (needed there for JSON
/// flexibility), this is a strongly-typed POCO: System.Text.Json's default
/// deserialization already does exactly what the Python version implemented
/// by hand (missing properties keep their class defaults, unknown JSON
/// properties are silently ignored), so LoadProfile below needs no manual
/// merge step. Property names are PascalCase per C# convention rather than
/// keeping the original snake_case JSON keys -- this is a new save file for
/// a new application, not required to stay byte-compatible with
/// data/profile.json.
/// </summary>
public sealed class GameProfileData
{
    private bool _noHealingEnabled;
    private int _mindTokens;
    public int BestLevel { get; set; }
    public int BestKills { get; set; }
    public int CompletedRuns { get; set; }
    public bool AutoFire { get; set; } = true;
    public bool CasualMode { get; set; } = true;
    public bool TutorialHints { get; set; } = true;
    public double ScreenShake { get; set; } = 0.65;
    public bool DamageNumbers { get; set; } = true;
    public bool AimGuide { get; set; }
    public bool HighContrast { get; set; }
    /// <summary>
    /// Gamepad input is fully inert until this is on: RotBoiGame.CollectInput
    /// polls the pad every frame regardless, but only writes real values into
    /// InputState's Controller*/Ui* fields when this is true, so a plugged-in
    /// controller can't nudge movement, menus, or firing while it's off.
    /// Defaults off while the right-trigger-fire / orbiting-reticle aim
    /// scheme is new and unproven.
    /// </summary>
    public bool ControllerSupportBeta { get; set; }
    /// <summary>
    /// Density of optional ambience, trails, and debris. Essential combat
    /// telegraphs and basic pose animation deliberately ignore this value.
    /// </summary>
    public double VisualEffectsIntensity { get; set; } = 1.0;
    /// <summary>Three stable IDs from FooterStats displayed in the combat footer.</summary>
    public List<string> FooterStats { get; set; } = global::RotBoiRemastered.UI.FooterStats.Defaults.ToList();
    public double TextSize { get; set; } = 1.0;
    public double GuiScale { get; set; } = 1.0;
    public double DamageTextSize { get; set; } = 0.8;
    /// <summary>Multiplier applied to the resolution-aware starting camera zoom.</summary>
    public double CameraZoom { get; set; } = 1.0;
    public string PlayerCoreColor { get; set; } = "midnight";
    public string PlayerEdgeColor { get; set; } = "ink";
    public string ProjectileColor { get; set; } = "reference";
    public string ProjectileDesign { get; set; } = "bulb";
    /// <summary>Native-resolution fullscreen, toggled via F11 or the pause menu's OPTIONS tab (RotBoiGame.ApplyFullscreen). Defaults off -- windowed is friendlier for a desktop app that hasn't asked first.</summary>
    public bool Fullscreen { get; set; }
    /// <summary>Fixed update/draw ceiling. VSync may impose a lower effective presentation rate.</summary>
    public int MaxFrameRate { get; set; } = FramePacing.DefaultFrameRate;
    /// <summary>Synchronize buffer presentation to the active display when supported by the graphics driver.</summary>
    public bool VSync { get; set; } = true;
    /// <summary>Legacy No Healing field retained so pre-campaign profiles migrate safely.</summary>
    public bool HardModeEnabled { get => _noHealingEnabled; set => _noHealingEnabled = value; }
    public bool NoHealingEnabled { get => _noHealingEnabled; set => _noHealingEnabled = value; }
    public bool NoExtractEnabled { get; set; }
    public bool DevUnlockTesting { get; set; }
    public bool DeveloperArmory { get; set; }
    /// <summary>Set by MetaProgression.RecordExtraction when a run ends (extract or full clear) without ever using the Golden Forge. Feeds the "Never use a reforge token and complete a run" cosmetic unlock.</summary>
    public bool NoReforgeRunCompleted { get; set; }
    /// <summary>Set by MetaProgression.RecordExtraction when a run ends (extract or full clear) while Hard Mode (no healing) was active.</summary>
    public bool HardModeRunCompleted { get; set; }
    /// <summary>Set when Aphantasia is defeated in Phase 4 with both Hard Mode braziers (no healing, no extract) lit -- "Aphantasia, Core of The Void" (True Hard Mode). See MetaProgression.RecordCoreOfTheVoidDefeat.</summary>
    public bool DefeatedCoreOfTheVoid { get; set; }
    /// <summary>Third Mind brazier, revealed once both Hard Mode braziers are lit. Replaces the run's health pool with a 3-hit chunk system and turns XP pickups into instant level grants -- see RunState.GoldenFlameMode.</summary>
    public bool GoldenFlameEnabled { get; set; }
    /// <summary>Fourth Mind brazier, found behind the secret wall east of the brazier alcove. One-hit death, no health bar, bigger instant level grants than Golden Flame -- see RunState.VoidMode.</summary>
    public bool VoidEnabled { get; set; }
    /// <summary>Permanent once true: the secret wall east of the brazier alcove has been shot open, revealing The Void's brazier. Never reset by Normalize -- a found secret stays found.</summary>
    public bool VoidPassageDiscovered { get; set; }
    /// <summary>
    /// Cosmetic unlock cache, formatted "{category}:{id}" (e.g. "core:emerald"). Populated by
    /// GameProfile.Normalize's grandfather step for whatever a save already has equipped, so an
    /// existing player never loses access to a look they were already wearing. Cosmetics.IsUnlocked
    /// also checks each option's own unlock condition live, so this list only needs to carry
    /// grandfathered entries -- earned unlocks stay unlocked because their underlying counters
    /// and flags are themselves permanent.
    /// </summary>
    public List<string> UnlockedCosmetics { get; set; } = new();

    /// <summary>Action id -> key code (as int) or null for unbound. See Keybinds.cs.</summary>
    public Dictionary<string, int?> Keybinds { get; set; } = new();
    /// <summary>Permanent currency, renamed with the safe hub.</summary>
    public int MindTokens { get => _mindTokens; set => _mindTokens = value; }
    /// <summary>Legacy JSON migration source. New code always uses MindTokens.</summary>
    public int SoulTokens { get => _mindTokens; set => _mindTokens = value; }
    public CampaignProgressData Campaign { get; set; } = new();
    public double BestDummyDps { get; set; }
    public Dictionary<string, int> SkillLevels { get; set; } = new();
    public Dictionary<string, long> QuestProgress { get; set; } = new();
    public List<string> CompletedQuests { get; set; } = new();
    /// <summary>The Vault -- safe, permanent storage. Capacity-limited (see MetaProgression.StorageCapacity).</summary>
    public List<StoredItemData> Storage { get; set; } = new();
    /// <summary>
    /// What's currently carried into runs, mirroring RunState.Equipment (slot key -> item,
    /// absence = empty). Synced from the live run whenever it ends without dying
    /// (MetaProgression.SyncCarriedItems), cleared on death (ClearCarriedItems), and loaded
    /// back into a fresh RunState by GameSession.LoadCarriedItems.
    /// </summary>
    public Dictionary<string, StoredItemData> CarriedEquipment { get; set; } = new();
    /// <summary>
    /// Mirrors RunState.Inventory (8 slots, nullable) the same way CarriedEquipment mirrors
    /// Equipment. Pre-padded to 8 nulls here (matching RunState.Inventory's own Reset())
    /// rather than relying solely on GameProfile.Normalize() to pad it, so a freshly
    /// constructed GameProfileData is already index-safe without going through LoadProfile.
    /// </summary>
    public List<StoredItemData?> CarriedInventory { get; set; } = Enumerable.Repeat<StoredItemData?>(null, 8).ToList();
    public List<ExtractedRunData> ExtractedRuns { get; set; } = new();
    public List<string> DiscoveredItems { get; set; } = new();
    public Dictionary<string, int> PathMastery { get; set; } = new();
    /// <summary>Highest selectable NG+ tier per path. Missing means only the normal path is available.</summary>
    public Dictionary<string, int> NewGamePlusUnlocked { get; set; } = new();
    /// <summary>Last selected NG+ tier per path, clamped against NewGamePlusUnlocked when read.</summary>
    public Dictionary<string, int> SelectedNewGamePlus { get; set; } = new();
    /// <summary>
    /// Most recent aggregate boss encounters for local balance inspection.
    /// This list contains no player identity or frame-level input history.
    /// </summary>
    public List<BossEncounterTelemetryData> RecentBossEncounters { get; set; } = new();
}

/// <summary>
/// Persisted item identity. Grade and Modifier are legacy fields from before
/// the rarity-ladder rework (Items.cs no longer has a Grade concept, and
/// Modifiers are no longer individually rolled/stored -- see
/// ItemDefinition.ModifierLadder) -- kept here only so an existing save file
/// with those properties still deserializes instead of throwing; Items.Deserialize
/// never reads them.
/// </summary>
public sealed record StoredItemData(
    string Name,
    string Rarity,
    string Grade = "S",
    string Modifier = "Balanced",
    string? CoreForge = null);

public sealed class ExtractedRunData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset ExtractedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Path { get; set; } = "Unknown Path";
    public string Outcome { get; set; } = RunOutcomes.Extracted;
    public int Level { get; set; }
    public int Kills { get; set; }
    public double Seconds { get; set; }
    public int NewGamePlusLevel { get; set; }
}

/// <summary>
/// Small, dependency-free persistent profile and accessibility settings
/// store. Ported from gameProfile.py.
/// </summary>
public static class GameProfile
{
    /// <summary>
    /// Mutable (not const) so tests can redirect saves to a temp location --
    /// the same purpose Python's tests served with
    /// mock.patch.object(gameProfile, "save_profile", ...), but here it
    /// actually exercises a real save/load round trip against a scratch file.
    /// </summary>
    public static string SavePath { get; set; } = DefaultSavePath();

    /// <summary>
    /// A per-user AppData folder (`%APPDATA%\RotBoiRemastered\profile.json`
    /// on Windows; `Environment.SpecialFolder.ApplicationData` resolves to
    /// the platform-appropriate equivalent elsewhere) rather than a path
    /// relative to the working directory -- a real installed build's working
    /// directory isn't guaranteed writable or even stable (e.g. Program
    /// Files), unlike a `dotnet run` dev invocation where the project folder
    /// always is.
    /// </summary>
    private static string DefaultSavePath()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "RotBoiRemastered", "profile.json");
    }

    public static GameProfileData Profile { get; set; } = LoadProfile();

    public static GameProfileData LoadProfile(string? path = null)
    {
        path ??= SavePath;
        try
        {
            string text = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<GameProfileData>(text);
            if (loaded is not null)
            {
                Normalize(loaded);
                return loaded;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            // Missing file, corrupt JSON, or an unreadable path -- fall back to defaults.
        }
        var defaults = new GameProfileData();
        Normalize(defaults);
        return defaults;
    }

    /// <summary>
    /// Retained for migration helpers and other discrete settings.
    /// </summary>
    private static double SnapToNearest(IReadOnlyList<double> levels, double value)
    {
        double closest = levels[0];
        double bestDiff = Math.Abs(value - closest);
        foreach (double level in levels)
        {
            double diff = Math.Abs(value - level);
            if (diff < bestDiff)
            {
                bestDiff = diff;
                closest = level;
            }
        }
        return closest;
    }

    private static void Normalize(GameProfileData profile)
    {
        profile.BestLevel = Math.Max(0, profile.BestLevel);
        profile.BestKills = Math.Max(0, profile.BestKills);
        profile.CompletedRuns = Math.Max(0, profile.CompletedRuns);
        profile.MindTokens = Math.Max(0, profile.MindTokens);
        profile.BestDummyDps = double.IsFinite(profile.BestDummyDps)
            ? Math.Max(0, profile.BestDummyDps)
            : 0;
        profile.Campaign ??= new CampaignProgressData();
        CampaignProgression.Normalize(profile.Campaign);
        profile.TextSize = Math.Clamp(profile.TextSize, UiTheme.MinTextScale, UiTheme.MaxTextScale);
        profile.GuiScale = Math.Clamp(profile.GuiScale, UiTheme.MinGuiScale, UiTheme.MaxGuiScale);
        profile.DamageTextSize = Math.Clamp(profile.DamageTextSize, UiTheme.MinDamageTextScale, UiTheme.MaxDamageTextScale);
        profile.CameraZoom = Math.Clamp(profile.CameraZoom, Camera.MinDefaultZoomScale, Camera.MaxDefaultZoomScale);
        profile.VisualEffectsIntensity = Math.Clamp(profile.VisualEffectsIntensity, 0.0, 1.0);
        profile.FooterStats = global::RotBoiRemastered.UI.FooterStats.NormalizeSelection(profile.FooterStats);
        profile.MaxFrameRate = FramePacing.NormalizeFrameRate(profile.MaxFrameRate);
        profile.Keybinds ??= new();
        profile.SkillLevels ??= new();
        profile.QuestProgress ??= new();
        profile.CompletedQuests ??= new();
        profile.Storage ??= new();
        profile.CarriedEquipment ??= new();
        profile.CarriedInventory ??= new();
        // GameSession.LoadCarriedItems indexes this by position up to InventorySlotCount,
        // same as RunState.Inventory itself -- pad/truncate so a missing/short/corrupt
        // save (or the very first launch, where it's just an empty list) can't index out
        // of range.
        if (profile.CarriedInventory.Count < InformationSheet.InventorySlotCount)
            profile.CarriedInventory.AddRange(Enumerable.Repeat<StoredItemData?>(null,
                InformationSheet.InventorySlotCount - profile.CarriedInventory.Count));
        else if (profile.CarriedInventory.Count > InformationSheet.InventorySlotCount)
            profile.CarriedInventory.RemoveRange(InformationSheet.InventorySlotCount,
                profile.CarriedInventory.Count - InformationSheet.InventorySlotCount);
        profile.ExtractedRuns ??= new();
        profile.DiscoveredItems ??= new();
        profile.PathMastery ??= new();
        profile.NewGamePlusUnlocked ??= new();
        profile.SelectedNewGamePlus ??= new();
        profile.RecentBossEncounters ??= new();
        profile.UnlockedCosmetics ??= new();
        // Grandfather whatever's currently equipped, so a wardrobe gated after this save was
        // created never locks a look the player already had. Duplicates are harmless (Select
        // and IsUnlocked both do a simple Contains check) but avoided anyway for a tidy save file.
        foreach (string entry in new[]
        {
            $"core:{profile.PlayerCoreColor}",
            $"edge:{profile.PlayerEdgeColor}",
            $"projectile:{profile.ProjectileColor}",
            $"design:{profile.ProjectileDesign}",
        })
        {
            if (!profile.UnlockedCosmetics.Contains(entry))
                profile.UnlockedCosmetics.Add(entry);
        }
        foreach (string key in profile.SkillLevels.Keys.ToList())
        {
            if (!MetaProgression.SkillNodesByKey.TryGetValue(key, out SkillNode? node))
                profile.SkillLevels.Remove(key);
            else
                profile.SkillLevels[key] = Math.Clamp(profile.SkillLevels[key], 0, node.MaxLevel);
        }
        foreach (string key in profile.QuestProgress.Keys.ToList())
        {
            if (string.IsNullOrWhiteSpace(key))
                profile.QuestProgress.Remove(key);
            else
                profile.QuestProgress[key] = Math.Max(0, profile.QuestProgress[key]);
        }
        var validQuestKeys = MetaProgression.Quests.Select(quest => quest.Key)
            .ToHashSet(StringComparer.Ordinal);
        profile.CompletedQuests = profile.CompletedQuests
            .Where(validQuestKeys.Contains)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        profile.Storage = profile.Storage
            .Where(item => Items.Deserialize(item) is not null)
            .Take(MetaProgression.StorageCapacity)
            .ToList();
        foreach (string slot in profile.CarriedEquipment.Keys.ToList())
        {
            StoredItemData stored = profile.CarriedEquipment[slot];
            if (!RunState.EquipmentSlotKeys.Contains(slot)
                || Items.Deserialize(stored) is null)
            {
                profile.CarriedEquipment.Remove(slot);
            }
        }
        for (int index = 0; index < profile.CarriedInventory.Count; index++)
            if (Items.Deserialize(profile.CarriedInventory[index]) is null)
                profile.CarriedInventory[index] = null;
        profile.ExtractedRuns = profile.ExtractedRuns
            .OfType<ExtractedRunData>()
            .Take(10)
            .ToList();
        foreach (ExtractedRunData run in profile.ExtractedRuns)
        {
            run.Id = string.IsNullOrWhiteSpace(run.Id)
                ? Guid.NewGuid().ToString("N")
                : run.Id.Trim();
            run.Path = string.IsNullOrWhiteSpace(run.Path)
                ? "Unknown Path"
                : run.Path.Trim();
            run.Outcome = RunOutcomes.IsSuccess(run.Outcome)
                ? run.Outcome
                : RunOutcomes.Extracted;
            run.Level = Math.Max(0, run.Level);
            run.Kills = Math.Max(0, run.Kills);
            run.Seconds = FiniteNonNegative(run.Seconds);
            run.NewGamePlusLevel = NewGamePlus.ClampLevel(run.NewGamePlusLevel);
        }
        foreach (string pathKey in profile.PathMastery.Keys.ToList())
            profile.PathMastery[pathKey] = Math.Max(0, profile.PathMastery[pathKey]);
        profile.RecentBossEncounters = profile.RecentBossEncounters
            .OfType<BossEncounterTelemetryData>()
            .TakeLast(50)
            .ToList();
        foreach (BossEncounterTelemetryData encounter in profile.RecentBossEncounters)
        {
            encounter.BossKey = string.IsNullOrWhiteSpace(encounter.BossKey)
                ? "unknown"
                : encounter.BossKey.Trim();
            encounter.SenseKey = string.IsNullOrWhiteSpace(encounter.SenseKey)
                ? "unknown"
                : encounter.SenseKey.Trim();
            encounter.FloorNumber = Math.Max(0, encounter.FloorNumber);
            encounter.ClearSeconds = FiniteNonNegative(encounter.ClearSeconds);
            encounter.DamageTaken = Math.Max(0, encounter.DamageTaken);
            encounter.SkippedBranchRooms = Math.Max(0, encounter.SkippedBranchRooms);
            encounter.SkippedBranchThreat = FiniteNonNegative(encounter.SkippedBranchThreat);
            encounter.CarriedEnemyThreat = FiniteNonNegative(encounter.CarriedEnemyThreat);
            encounter.LocalPlayerCount = Math.Max(1, encounter.LocalPlayerCount);
            encounter.Phases = (encounter.Phases ?? new())
                .OfType<BossPhaseTelemetryData>()
                .Select(phase => new BossPhaseTelemetryData
                {
                    Label = string.IsNullOrWhiteSpace(phase.Label)
                        ? "UNKNOWN"
                        : phase.Label.Trim(),
                    Seconds = FiniteNonNegative(phase.Seconds),
                })
                .ToList();
        }
        // Pre-NG+ saves already recorded ordinary clears in PathMastery. Preserve
        // that accomplishment by opening NG+1, but never infer higher tiers from
        // the old repeat-clear count because those clears had no NG+ difficulty.
        foreach (var (pathKey, clears) in profile.PathMastery)
            if (clears > 0 && profile.NewGamePlusUnlocked.GetValueOrDefault(pathKey) < 1)
                profile.NewGamePlusUnlocked[pathKey] = 1;
        foreach (string pathKey in profile.NewGamePlusUnlocked.Keys.ToList())
            profile.NewGamePlusUnlocked[pathKey] = NewGamePlus.ClampLevel(profile.NewGamePlusUnlocked[pathKey]);
        foreach (string pathKey in profile.SelectedNewGamePlus.Keys.ToList())
            profile.SelectedNewGamePlus[pathKey] = Math.Min(
                NewGamePlus.ClampLevel(profile.NewGamePlusUnlocked.GetValueOrDefault(pathKey)),
                NewGamePlus.ClampLevel(profile.SelectedNewGamePlus[pathKey]));
    }

    private static double FiniteNonNegative(double value) =>
        double.IsFinite(value) ? Math.Max(0, value) : 0;

    public static bool SaveProfile(string? path = null)
    {
        path ??= SavePath;
        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(path, JsonSerializer.Serialize(Profile, options));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static void RecordRun(int level, int kills, bool completed = false)
    {
        Profile.BestLevel = Math.Max(Profile.BestLevel, level);
        Profile.BestKills = Math.Max(Profile.BestKills, kills);
        if (completed)
            Profile.CompletedRuns += 1;
        SaveProfile();
    }

    public static void RecordBossEncounter(BossEncounterTelemetryData encounter)
    {
        Profile.RecentBossEncounters.Add(encounter);
        if (Profile.RecentBossEncounters.Count > 50)
            Profile.RecentBossEncounters.RemoveAt(0);
        SaveProfile();
    }

    /// <summary>
    /// Bumps a lifetime quest counter. When <paramref name="state"/> is the
    /// active run's RunState, any quest that becomes ready as a result is
    /// recorded on it so the end-of-run debrief can call it out; pass null
    /// (the default) from contexts with no active run, such as The Mind's
    /// DPS effigy.
    /// </summary>
    public static void IncrementQuest(string counter, long amount = 1, RunState? state = null)
    {
        Profile.QuestProgress[counter] = Math.Max(0, Profile.QuestProgress.GetValueOrDefault(counter) + amount);
        var completed = MetaProgression.CompleteReadyQuests();
        if (state is not null)
            foreach (var quest in completed)
                state.QuestsCompletedThisRun.Add(quest.Key);
    }

    public static void RecordDummyDps(double dps)
    {
        if (dps <= Profile.BestDummyDps)
            return;
        Profile.BestDummyDps = dps;
        SaveProfile();
    }

    public static void DiscoverItem(string name, RunState? state = null)
    {
        if (Profile.DiscoveredItems.Contains(name))
            return;
        Profile.DiscoveredItems.Add(name);
        IncrementQuest("items_found", state: state);
    }

    /// <summary>
    /// Toggle a boolean field on Profile by (PascalCase) name, matching
    /// Python's toggle(key), which worked on any dict entry whose current
    /// value happened to be a bool. Kept generic via reflection -- rather
    /// than named per-field setters -- because the pause menu's GAMEPLAY tab
    /// (menus.py's _GAMEPLAY_OPTIONS) drives its toggle rows from a
    /// data-driven list of field names.
    /// </summary>
    public static bool? Toggle(string fieldName)
    {
        PropertyInfo? property = typeof(GameProfileData).GetProperty(
            fieldName, BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);
        if (property is null || property.PropertyType != typeof(bool))
            return null;
        bool updated = !(bool)property.GetValue(Profile)!;
        property.SetValue(Profile, updated);
        SaveProfile();
        return updated;
    }
}
