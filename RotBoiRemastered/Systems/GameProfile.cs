using System.Reflection;
using System.Text.Json;
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
    /// <summary>Sections included in the in-run details view opened with the HUD-detail key (Tab by default).</summary>
    public bool TabShowWeaponStats { get; set; } = true;
    public bool TabShowActiveQuests { get; set; }
    public bool TabShowAllQuests { get; set; }
    public bool TabShowCosmetics { get; set; }
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
    /// <summary>Selected only from the Soul's challenge altar and captured into each new RunState.</summary>
    public bool HardModeEnabled { get; set; }

    /// <summary>Action id -> key code (as int) or null for unbound. See Keybinds.cs.</summary>
    public Dictionary<string, int?> Keybinds { get; set; } = new();
    public int SoulTokens { get; set; }
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
}

/// <summary>
/// Persisted item identity. Grade and Modifier were added after the original
/// two-field save shape; their defaults deliberately preserve the old item's
/// authored strength and avoid inventing an affix when an existing save is
/// migrated.
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
    public string Outcome { get; set; } = "EXTRACTED";
    public int Level { get; set; }
    public int Kills { get; set; }
    public double Seconds { get; set; }
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
        return new GameProfileData();
    }

    /// <summary>
    /// GuiScale is preset-only (see UiTheme.GuiScaleLevels' doc comment) --
    /// snap rather than clamp, so a profile.json saved by an older build
    /// with a continuous in-between value (or the old, wider slider range)
    /// lands on the closest still-valid preset instead of an unreachable-
    /// through-the-UI value that happened to also be in-range. TextSize is
    /// back to being a plain slider (see Menus.cs's "text_size" control),
    /// so it's clamped rather than snapped -- see Normalize() below.
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
        profile.TextSize = Math.Clamp(profile.TextSize, UiTheme.MinTextScale, UiTheme.MaxTextScale);
        profile.GuiScale = SnapToNearest(UiTheme.GuiScaleLevels, profile.GuiScale);
        profile.DamageTextSize = Math.Clamp(profile.DamageTextSize, UiTheme.MinDamageTextScale, UiTheme.MaxDamageTextScale);
        profile.CameraZoom = Math.Clamp(profile.CameraZoom, Camera.MinDefaultZoomScale, Camera.MaxDefaultZoomScale);
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
    }

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

    public static void IncrementQuest(string counter, long amount = 1)
    {
        Profile.QuestProgress[counter] = Math.Max(0, Profile.QuestProgress.GetValueOrDefault(counter) + amount);
        MetaProgression.CompleteReadyQuests();
    }

    public static void RecordDummyDps(double dps)
    {
        if (dps <= Profile.BestDummyDps)
            return;
        Profile.BestDummyDps = dps;
        SaveProfile();
    }

    public static void DiscoverItem(string name)
    {
        if (Profile.DiscoveredItems.Contains(name))
            return;
        Profile.DiscoveredItems.Add(name);
        IncrementQuest("items_found");
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
