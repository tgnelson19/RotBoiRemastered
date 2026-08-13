using RotBoiRemastered.Systems;

namespace RotBoiRemastered.Tests.Systems;

[Collection("GameProfileState")]
public sealed class CampaignProgressionTests : IDisposable
{
    private readonly GameProfileData _original = GameProfile.Profile;
    private readonly string _originalPath = GameProfile.SavePath;
    private readonly string _directory = Directory.CreateTempSubdirectory("rotboi-campaign-").FullName;

    public CampaignProgressionTests()
    {
        GameProfile.Profile = new GameProfileData();
        CampaignProgression.Normalize(GameProfile.Profile.Campaign);
        GameProfile.SavePath = Path.Combine(_directory, "profile.json");
        CampaignDevOverrides.Reset();
    }

    public void Dispose()
    {
        CampaignDevOverrides.Reset();
        GameProfile.Profile = _original;
        GameProfile.SavePath = _originalPath;
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void GatesOpenInCampaignOrder()
    {
        Assert.True(CampaignProgression.PortalUnlocked("dungeon"));
        Assert.False(CampaignProgression.PortalUnlocked("core"));
        Assert.All(CampaignProgression.SenseKeys,
            sense => Assert.True(CampaignProgression.PortalUnlocked(sense)));
        Assert.False(CampaignProgression.PortalUnlocked("body"));

        foreach (string sense in CampaignProgression.SenseKeys)
            CampaignProgression.CompleteStatue(sense, StatueMaterial.Silver, false, false);
        Assert.True(CampaignProgression.PortalUnlocked("body"));
        Assert.False(CampaignProgression.PortalUnlocked("aphantasia"));

        foreach (string sense in CampaignProgression.SenseKeys)
            CampaignProgression.CompleteSoul(sense);
        Assert.True(CampaignProgression.PortalUnlocked("aphantasia"));
    }

    [Fact]
    public void FreshUnnormalizedProfileCanEarnItsFirstStatue()
    {
        GameProfile.Profile = new GameProfileData();

        CampaignProgression.CompleteStatue("sound", StatueMaterial.Silver,
            noHealing: false, noExtract: false);

        Assert.True(GameProfile.Profile.Campaign.SilverStatues["sound"].Unlocked);
        Assert.Equal(CampaignProgression.SenseKeys.Length,
            GameProfile.Profile.Campaign.SilverStatues.Count);
    }

    [Fact]
    public void AphantasiaRequiresGoldStatuesButNotChallengeClears()
    {
        foreach (string sense in CampaignProgression.SenseKeys)
        {
            CampaignProgression.CompleteStatue(sense, StatueMaterial.Silver, false, false);
            CampaignProgression.CompleteSoul(sense, false, false);
        }
        Assert.True(CampaignProgression.Data.AphantasiaUnlocked);
        Assert.All(CampaignProgression.Data.GoldStatues.Values,
            statue => Assert.False(statue.Rainbow));
    }

    [Theory]
    [InlineData(false, false, ChallengeClear.None, false)]
    [InlineData(true, false, ChallengeClear.NoHealing, false)]
    [InlineData(false, true, ChallengeClear.NoExtract, false)]
    [InlineData(true, true,
        ChallengeClear.NoHealing | ChallengeClear.NoExtract | ChallengeClear.Both,
        true)]
    public void AphantasiaCompletionErectsThePersistentChallengeTrophy(
        bool noHealing, bool noExtract, ChallengeClear expected, bool rainbow)
    {
        CampaignProgression.CompleteAphantasia(noHealing, noExtract);

        StatueProgress trophy = CampaignProgression.Data.AphantasiaStatue;
        Assert.True(trophy.Unlocked);
        Assert.Equal(expected, trophy.ChallengeClears);
        Assert.Equal(rainbow, trophy.Rainbow);
        Assert.True(File.Exists(GameProfile.SavePath));
    }

    [Fact]
    public void SeparateChallengeVictoriesAccumulateBloodAndCrackWithoutClaimingDualClear()
    {
        CampaignProgression.CompleteAphantasia(noHealing: true, noExtract: false);
        CampaignProgression.CompleteAphantasia(noHealing: false, noExtract: true);

        StatueProgress trophy = CampaignProgression.Data.AphantasiaStatue;
        Assert.Equal(ChallengeClear.NoHealing | ChallengeClear.NoExtract,
            trophy.ChallengeClears);
        Assert.False(trophy.Rainbow);

        CampaignProgression.CompleteAphantasia(noHealing: true, noExtract: true);
        Assert.True(trophy.Rainbow);
    }

    [Fact]
    public void VersionOneSoulCompletionsMigrateToGoldWithoutKeepingDungeonGold()
    {
        var legacy = new CampaignProgressData { Version = 1 };
        legacy.ArenaUnlocks.Add("sound");
        legacy.GoldStatues["touch"] = new StatueProgress { Unlocked = true };

        CampaignProgression.Normalize(legacy);

        Assert.True(legacy.GoldStatues["sound"].Unlocked);
        Assert.False(legacy.GoldStatues["touch"].Unlocked);
    }

    [Fact]
    public void VersionTwoProfilesGainAnEmptyTrophyWithoutReplayingVersionOneMigration()
    {
        var versionTwo = new CampaignProgressData { Version = 2 };
        versionTwo.ArenaUnlocks.Add("sound");
        versionTwo.GoldStatues["touch"] = new StatueProgress { Unlocked = true };
        versionTwo.AphantasiaStatue = null!;

        CampaignProgression.Normalize(versionTwo);

        Assert.Equal(CampaignProgressData.CurrentVersion, versionTwo.Version);
        Assert.False(versionTwo.GoldStatues["sound"].Unlocked);
        Assert.True(versionTwo.GoldStatues["touch"].Unlocked);
        Assert.NotNull(versionTwo.AphantasiaStatue);
        Assert.False(versionTwo.AphantasiaStatue.Unlocked);
    }

    [Fact]
    public void LegacySoulFieldsRoundTripIntoMindAliases()
    {
        GameProfile.Profile.SoulTokens = 17;
        GameProfile.Profile.HardModeEnabled = true;
        Assert.Equal(17, GameProfile.Profile.MindTokens);
        Assert.True(GameProfile.Profile.NoHealingEnabled);
    }

    [Fact]
    public void DevGateOverridesAreReversibleAndNeverAlterSavedProgression()
    {
        GameProfile.Profile.DevUnlockTesting = true;
        Assert.False(CampaignProgression.PortalUnlocked("core"));

        CampaignDevOverrides.TogglePortal("core");

        Assert.True(CampaignProgression.PortalUnlocked("core"));
        Assert.False(CampaignProgression.Data.CoreUnlocked);

        CampaignDevOverrides.TogglePortal("core");
        Assert.False(CampaignProgression.PortalUnlocked("core"));
    }

    [Fact]
    public void DevStatueControlsCycleThroughEachVisualChallengeState()
    {
        string sense = CampaignProgression.SenseKeys[0];

        CampaignDevOverrides.CycleStatues(StatueMaterial.Silver);
        Assert.Equal(ChallengeClear.None,
            CampaignDevOverrides.SilverStatues[sense]);

        CampaignDevOverrides.CycleStatues(StatueMaterial.Silver);
        Assert.Equal(ChallengeClear.NoHealing,
            CampaignDevOverrides.SilverStatues[sense]);

        CampaignDevOverrides.CycleStatues(StatueMaterial.Silver);
        Assert.Equal(ChallengeClear.NoExtract,
            CampaignDevOverrides.SilverStatues[sense]);

        CampaignDevOverrides.CycleStatues(StatueMaterial.Silver);
        Assert.Equal(ChallengeClear.NoHealing | ChallengeClear.NoExtract,
            CampaignDevOverrides.SilverStatues[sense]);

        CampaignDevOverrides.CycleStatues(StatueMaterial.Silver);
        Assert.True(CampaignDevOverrides.SilverStatues[sense]
            .HasFlag(ChallengeClear.Both));

        CampaignDevOverrides.CycleStatues(StatueMaterial.Silver);
        Assert.DoesNotContain(sense, CampaignDevOverrides.SilverStatues);
    }

    [Fact]
    public void DevAphantasiaTrophyControlCyclesEveryVisualWithoutSavingProgress()
    {
        ChallengeClear?[] expected =
        [
            ChallengeClear.None,
            ChallengeClear.NoHealing,
            ChallengeClear.NoExtract,
            ChallengeClear.NoHealing | ChallengeClear.NoExtract,
            ChallengeClear.NoHealing | ChallengeClear.NoExtract | ChallengeClear.Both,
            null,
        ];

        foreach (ChallengeClear? state in expected)
        {
            CampaignDevOverrides.CycleAphantasiaStatue();
            Assert.Equal(state, CampaignDevOverrides.AphantasiaStatue);
        }
        Assert.False(CampaignProgression.Data.AphantasiaStatue.Unlocked);
    }

    [Fact]
    public void DevEndgameOverridesOpenTheirPhysicalPrerequisiteCorridors()
    {
        CampaignDevOverrides.TogglePortal("core");
        Assert.Contains("sight", CampaignDevOverrides.PortalUnlocks);
        Assert.Contains("core", CampaignDevOverrides.PortalUnlocks);

        CampaignDevOverrides.Reset();
        CampaignDevOverrides.TogglePortal("aphantasia");
        Assert.Contains("sight", CampaignDevOverrides.PortalUnlocks);
        Assert.Contains("core", CampaignDevOverrides.PortalUnlocks);
        Assert.Contains("aphantasia", CampaignDevOverrides.PortalUnlocks);
    }
}
