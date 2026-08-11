namespace RotBoiRemastered.Systems;

public enum CampaignWorld { Body, Soul }
public enum CampaignActivity { Body, Soul, Arena, Core, Aphantasia }

[Flags]
public enum ChallengeClear
{
    None = 0,
    NoHealing = 1,
    NoExtract = 2,
    Both = 4,
}

public enum StatueMaterial { Silver, Gold }

public sealed class StatueProgress
{
    public bool Unlocked { get; set; }
    public ChallengeClear ChallengeClears { get; set; }
    public bool Rainbow => ChallengeClears.HasFlag(ChallengeClear.Both);
}

/// <summary>Versioned, permanent gates for the linear Mind campaign.</summary>
public sealed class CampaignProgressData
{
    public const int CurrentVersion = 1;
    public int Version { get; set; } = CurrentVersion;
    public bool BodyCompleted { get; set; }
    public bool SoulUnlocked { get; set; }
    public HashSet<string> ArenaUnlocks { get; set; } = new();
    public Dictionary<string, StatueProgress> SilverStatues { get; set; } = new();
    public Dictionary<string, StatueProgress> GoldStatues { get; set; } = new();
    public bool AphantasiaUnlocked { get; set; }

    public bool CoreUnlocked => CampaignProgression.SenseKeys.All(ArenaUnlocks.Contains);
}

public static class CampaignProgression
{
    public static readonly string[] SenseKeys =
        ["sound", "touch", "sight", "chemesthesis", "phantasia"];

    public static CampaignProgressData Data => GameProfile.Profile.Campaign;

    public static void Normalize(CampaignProgressData? data)
    {
        data ??= new CampaignProgressData();
        data.ArenaUnlocks ??= new();
        data.SilverStatues ??= new();
        data.GoldStatues ??= new();
        data.ArenaUnlocks.RemoveWhere(key => !SenseKeys.Contains(key));
        foreach (string sense in SenseKeys)
        {
            data.SilverStatues.TryAdd(sense, new StatueProgress());
            data.GoldStatues.TryAdd(sense, new StatueProgress());
        }
        data.SoulUnlocked |= data.BodyCompleted;
        data.AphantasiaUnlocked = AllStatuesRainbow(data);
        data.Version = CampaignProgressData.CurrentVersion;
    }

    public static bool PortalUnlocked(string key)
    {
        if (GameProfile.Profile.DevUnlockTesting && CampaignDevOverrides.PortalUnlocks.Contains(key))
            return true;
        return key switch
    {
        "body" => true,
        "soul" => Data.SoulUnlocked,
        "core" => Data.CoreUnlocked,
        "aphantasia" => Data.AphantasiaUnlocked,
        _ when SenseKeys.Contains(key) => Data.ArenaUnlocks.Contains(key),
        _ => false,
    };
    }

    public static void CompleteBody()
    {
        Data.BodyCompleted = true;
        Data.SoulUnlocked = true;
        Save();
    }

    public static void CompleteSoul(string sense)
    {
        RequireSense(sense);
        Data.ArenaUnlocks.Add(sense);
        Save();
    }

    public static void CompleteStatue(string sense, StatueMaterial material,
        bool noHealing, bool noExtract)
    {
        RequireSense(sense);
        StatueProgress statue = (material == StatueMaterial.Silver
            ? Data.SilverStatues : Data.GoldStatues)[sense];
        statue.Unlocked = true;
        if (noHealing) statue.ChallengeClears |= ChallengeClear.NoHealing;
        if (noExtract) statue.ChallengeClears |= ChallengeClear.NoExtract;
        if (noHealing && noExtract) statue.ChallengeClears |= ChallengeClear.Both;
        Data.AphantasiaUnlocked = AllStatuesRainbow(Data);
        Save();
    }

    public static bool AllStatuesRainbow(CampaignProgressData data) =>
        SenseKeys.All(sense =>
            data.SilverStatues.GetValueOrDefault(sense)?.Rainbow == true
            && data.GoldStatues.GetValueOrDefault(sense)?.Rainbow == true);

    public static void Reset()
    {
        GameProfile.Profile.Campaign = new CampaignProgressData();
        Normalize(GameProfile.Profile.Campaign);
        Save();
    }

    private static void Save()
    {
        Normalize(Data);
        GameProfile.SaveProfile();
    }

    private static void RequireSense(string sense)
    {
        if (!SenseKeys.Contains(sense))
            throw new ArgumentOutOfRangeException(nameof(sense));
    }
}

/// <summary>Session-only visual/gate overrides; never serialized or counted as completion.</summary>
public static class CampaignDevOverrides
{
    public static HashSet<string> PortalUnlocks { get; } = new();
    public static Dictionary<string, ChallengeClear> SilverStatues { get; } = new();
    public static Dictionary<string, ChallengeClear> GoldStatues { get; } = new();

    public static void Reset()
    {
        PortalUnlocks.Clear();
        SilverStatues.Clear();
        GoldStatues.Clear();
    }

    public static void TogglePortal(string key)
    {
        if (PortalUnlocks.Remove(key))
            return;
        PortalUnlocks.Add(key);
        // The endgame entrances are physically arranged in one line. A dev
        // override for a later chamber opens its prerequisite corridor too.
        if (key is "core" or "aphantasia")
            PortalUnlocks.Add("sight");
        if (key == "aphantasia")
            PortalUnlocks.Add("core");
    }

    public static void ToggleAllArenas()
    {
        bool enable = CampaignProgression.SenseKeys.Any(sense => !PortalUnlocks.Contains(sense));
        foreach (string sense in CampaignProgression.SenseKeys)
            if (enable) PortalUnlocks.Add(sense); else PortalUnlocks.Remove(sense);
        if (enable) PortalUnlocks.Add("core"); else PortalUnlocks.Remove("core");
    }

    public static void CycleStatues(StatueMaterial material)
    {
        foreach (string sense in CampaignProgression.SenseKeys)
            CycleStatue(sense, material);
    }

    public static void CycleStatue(string sense, StatueMaterial material)
    {
        if (!CampaignProgression.SenseKeys.Contains(sense))
            throw new ArgumentOutOfRangeException(nameof(sense));
        Dictionary<string, ChallengeClear> values = material == StatueMaterial.Silver
            ? SilverStatues : GoldStatues;
        if (!values.TryGetValue(sense, out ChallengeClear current))
        {
            // First click creates the intact base statue.
            values[sense] = ChallengeClear.None;
            return;
        }
        values[sense] = current switch
        {
            ChallengeClear.None => ChallengeClear.NoHealing,
            ChallengeClear.NoHealing => ChallengeClear.NoExtract,
            ChallengeClear.NoExtract => ChallengeClear.NoHealing | ChallengeClear.NoExtract,
            ChallengeClear.NoHealing | ChallengeClear.NoExtract =>
                ChallengeClear.NoHealing | ChallengeClear.NoExtract | ChallengeClear.Both,
            _ => ChallengeClear.None,
        };
        if (current.HasFlag(ChallengeClear.Both))
            values.Remove(sense);
    }

    public static void ToggleAllRainbow()
    {
        bool enable = CampaignProgression.SenseKeys.Any(sense =>
            !SilverStatues.GetValueOrDefault(sense).HasFlag(ChallengeClear.Both)
            || !GoldStatues.GetValueOrDefault(sense).HasFlag(ChallengeClear.Both));
        foreach (string sense in CampaignProgression.SenseKeys)
        {
            SilverStatues[sense] = enable
                ? ChallengeClear.NoHealing | ChallengeClear.NoExtract | ChallengeClear.Both : ChallengeClear.None;
            GoldStatues[sense] = SilverStatues[sense];
        }
        if (enable)
        {
            PortalUnlocks.Add("sight");
            PortalUnlocks.Add("core");
            PortalUnlocks.Add("aphantasia");
        }
        else
        {
            PortalUnlocks.Remove("aphantasia");
        }
    }
}
