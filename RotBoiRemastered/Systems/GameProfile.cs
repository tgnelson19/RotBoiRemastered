using System.Reflection;
using System.Text.Json;

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
    public string HudMode { get; set; } = "compact";
    public double TextSize { get; set; } = 1.0;

    /// <summary>Action id -> key code (as int) or null for unbound. See Keybinds.cs.</summary>
    public Dictionary<string, int?> Keybinds { get; set; } = new();
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
    public static string SavePath { get; set; } = "data/profile.json";

    public static GameProfileData Profile { get; set; } = LoadProfile();

    public static GameProfileData LoadProfile(string? path = null)
    {
        path ??= SavePath;
        try
        {
            string text = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<GameProfileData>(text);
            if (loaded is not null)
                return loaded;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            // Missing file, corrupt JSON, or an unreadable path -- fall back to defaults.
        }
        return new GameProfileData();
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
