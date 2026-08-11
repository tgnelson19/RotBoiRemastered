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
        Assert.True(CampaignProgression.PortalUnlocked("body"));
        Assert.False(CampaignProgression.PortalUnlocked("soul"));

        CampaignProgression.CompleteBody();
        Assert.True(CampaignProgression.PortalUnlocked("soul"));

        foreach (string sense in CampaignProgression.SenseKeys)
            CampaignProgression.CompleteSoul(sense);
        Assert.True(CampaignProgression.PortalUnlocked("core"));
    }

    [Fact]
    public void AphantasiaRequiresDedicatedBothChallengeClearOnAllTenStatues()
    {
        foreach (string sense in CampaignProgression.SenseKeys)
        {
            CampaignProgression.CompleteStatue(sense, StatueMaterial.Silver, true, false);
            CampaignProgression.CompleteStatue(sense, StatueMaterial.Silver, false, true);
            CampaignProgression.CompleteStatue(sense, StatueMaterial.Gold, true, true);
        }
        Assert.False(CampaignProgression.Data.AphantasiaUnlocked);

        foreach (string sense in CampaignProgression.SenseKeys)
            CampaignProgression.CompleteStatue(sense, StatueMaterial.Silver, true, true);
        Assert.True(CampaignProgression.Data.AphantasiaUnlocked);
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
        Assert.False(CampaignProgression.PortalUnlocked("sound"));

        CampaignDevOverrides.TogglePortal("sound");

        Assert.True(CampaignProgression.PortalUnlocked("sound"));
        Assert.DoesNotContain("sound", CampaignProgression.Data.ArenaUnlocks);

        CampaignDevOverrides.TogglePortal("sound");
        Assert.False(CampaignProgression.PortalUnlocked("sound"));
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
