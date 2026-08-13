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
    public const int CurrentVersion = 3;
    public int Version { get; set; } = CurrentVersion;
    public bool BodyCompleted { get; set; }
    public bool SoulUnlocked { get; set; }
    public HashSet<string> ArenaUnlocks { get; set; } = new();
    public Dictionary<string, StatueProgress> SilverStatues { get; set; } = new();
    public Dictionary<string, StatueProgress> GoldStatues { get; set; } = new();
    public bool AphantasiaUnlocked { get; set; }
    /// <summary>
    /// Central Mind trophy earned by defeating Aphantasia. Challenge clears
    /// use the same blood/crack/rainbow language as the sense statues.
    /// </summary>
    public StatueProgress AphantasiaStatue { get; set; } = new();

    public bool BodyUnlocked => CampaignProgression.SenseKeys.All(sense =>
        SilverStatues.GetValueOrDefault(sense)?.Unlocked == true);
    public bool CoreUnlocked => BodyUnlocked; // Legacy/UI alias for the first northern gate.
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
        data.AphantasiaStatue ??= new StatueProgress();
        data.ArenaUnlocks.RemoveWhere(key => !SenseKeys.Contains(key));
        bool legacyV1 = data.Version < 2;
        foreach (string sense in SenseKeys)
        {
            data.SilverStatues.TryAdd(sense, new StatueProgress());
            data.GoldStatues.TryAdd(sense, new StatueProgress());
            if (legacyV1)
            {
                // Version 1 used ArenaUnlocks for completed Soul finales, while
                // dungeon clears could incorrectly create gold statues. Preserve
                // real Soul progress and discard those ambiguous dungeon awards.
                data.GoldStatues[sense].Unlocked = data.ArenaUnlocks.Contains(sense);
                if (!data.GoldStatues[sense].Unlocked)
                    data.GoldStatues[sense].ChallengeClears = ChallengeClear.None;
            }
        }
        data.SoulUnlocked = data.BodyCompleted;
        data.AphantasiaUnlocked = AllGoldStatuesUnlocked(data);
        data.Version = CampaignProgressData.CurrentVersion;
    }

    public static bool PortalUnlocked(string key)
    {
        if (GameProfile.Profile.DevUnlockTesting && CampaignDevOverrides.PortalUnlocks.Contains(key))
            return true;
        return key switch
    {
        "body" => Data.BodyUnlocked,
        "soul" => Data.SoulUnlocked,
        "dungeon" => true,
        "core" => Data.CoreUnlocked,
        "aphantasia" => Data.AphantasiaUnlocked,
        _ when SenseKeys.Contains(key) => true,
        _ => false,
    };
    }

    public static void CompleteBody()
    {
        Data.BodyCompleted = true;
        Data.SoulUnlocked = true;
        Save();
    }

    public static void CompleteSoul(string sense, bool noHealing = false, bool noExtract = false)
    {
        RequireSense(sense);
        Data.ArenaUnlocks.Add(sense);
        CompleteStatue(sense, StatueMaterial.Gold, noHealing, noExtract);
    }

    public static void CompleteAphantasia(bool noHealing = false, bool noExtract = false)
    {
        Normalize(Data);
        RecordChallengeClear(Data.AphantasiaStatue, noHealing, noExtract);
        Save();
    }

    public static void CompleteStatue(string sense, StatueMaterial material,
        bool noHealing, bool noExtract)
    {
        RequireSense(sense);
        // A missing or corrupt profile is normalized on load, but callers and
        // tests may also install a freshly constructed profile directly.
        // Never let the first earned statue fail because its dictionary was
        // not populated yet.
        Normalize(Data);
        StatueProgress statue = (material == StatueMaterial.Silver
            ? Data.SilverStatues : Data.GoldStatues)[sense];
        RecordChallengeClear(statue, noHealing, noExtract);
        Data.AphantasiaUnlocked = AllGoldStatuesUnlocked(Data);
        Save();
    }

    private static void RecordChallengeClear(StatueProgress statue,
        bool noHealing, bool noExtract)
    {
        statue.Unlocked = true;
        if (noHealing) statue.ChallengeClears |= ChallengeClear.NoHealing;
        if (noExtract) statue.ChallengeClears |= ChallengeClear.NoExtract;
        if (noHealing && noExtract) statue.ChallengeClears |= ChallengeClear.Both;
    }

    public static bool AllGoldStatuesUnlocked(CampaignProgressData data) =>
        SenseKeys.All(sense => data.GoldStatues.GetValueOrDefault(sense)?.Unlocked == true);

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
    public static ChallengeClear? AphantasiaStatue { get; private set; }

    public static void Reset()
    {
        PortalUnlocks.Clear();
        SilverStatues.Clear();
        GoldStatues.Clear();
        AphantasiaStatue = null;
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

    public static void CycleAphantasiaStatue()
    {
        AphantasiaStatue = NextStatueState(AphantasiaStatue);
    }

    private static ChallengeClear? NextStatueState(ChallengeClear? current) => current switch
    {
        null => ChallengeClear.None,
        ChallengeClear.None => ChallengeClear.NoHealing,
        ChallengeClear.NoHealing => ChallengeClear.NoExtract,
        ChallengeClear.NoExtract => ChallengeClear.NoHealing | ChallengeClear.NoExtract,
        ChallengeClear.NoHealing | ChallengeClear.NoExtract =>
            ChallengeClear.NoHealing | ChallengeClear.NoExtract | ChallengeClear.Both,
        _ => null,
    };

    public static void ToggleAllRainbow()
    {
        bool enable = AphantasiaStatue?.HasFlag(ChallengeClear.Both) != true
            || CampaignProgression.SenseKeys.Any(sense =>
                !SilverStatues.GetValueOrDefault(sense).HasFlag(ChallengeClear.Both)
                || !GoldStatues.GetValueOrDefault(sense).HasFlag(ChallengeClear.Both));
        foreach (string sense in CampaignProgression.SenseKeys)
        {
            SilverStatues[sense] = enable
                ? ChallengeClear.NoHealing | ChallengeClear.NoExtract | ChallengeClear.Both : ChallengeClear.None;
            GoldStatues[sense] = SilverStatues[sense];
        }
        AphantasiaStatue = enable
            ? ChallengeClear.NoHealing | ChallengeClear.NoExtract | ChallengeClear.Both
            : null;
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
