using RotBoiRemastered.Systems;

namespace RotBoiRemastered.Tests.Systems;

public enum AuditProfileStage { Fresh, MidProgression, FullyUnlocked }

internal static class AuditProfileScenarios
{
    public static GameProfileData Create(AuditProfileStage stage)
    {
        var profile = new GameProfileData();
        CampaignProgression.Normalize(profile.Campaign);
        if (stage == AuditProfileStage.Fresh)
            return profile;

        profile.MindTokens = stage == AuditProfileStage.MidProgression ? 4 : 30;
        profile.SkillLevels["tempered_soul"] = stage == AuditProfileStage.MidProgression ? 2 : 5;
        profile.SkillLevels["wide_grasp"] = stage == AuditProfileStage.MidProgression ? 1 : 5;
        profile.PathMastery["sound"] = stage == AuditProfileStage.MidProgression ? 1 : 8;
        profile.NewGamePlusUnlocked["sound"] = stage == AuditProfileStage.MidProgression ? 1 : 7;
        profile.Storage.Add(new StoredItemData("Iron Sword", "Rare", "B", "Balanced"));

        IEnumerable<string> silverSenses = stage == AuditProfileStage.MidProgression
            ? CampaignProgression.SenseKeys.Take(2)
            : CampaignProgression.SenseKeys;
        foreach (string sense in silverSenses)
            profile.Campaign.SilverStatues[sense].Unlocked = true;

        if (stage == AuditProfileStage.FullyUnlocked)
        {
            profile.Campaign.BodyCompleted = true;
            foreach (string sense in CampaignProgression.SenseKeys)
            {
                profile.Campaign.ArenaUnlocks.Add(sense);
                profile.Campaign.GoldStatues[sense].Unlocked = true;
                profile.PathMastery[sense] = 8;
                profile.NewGamePlusUnlocked[sense] = 7;
            }
            foreach (SkillNode node in MetaProgression.SkillNodes)
                profile.SkillLevels[node.Key] = node.MaxLevel;
            profile.CompletedQuests = MetaProgression.Quests
                .Select(quest => quest.Key).ToList();
        }
        CampaignProgression.Normalize(profile.Campaign);
        return profile;
    }
}

[Collection("GameProfileState")]
public sealed class GameProfileAuditScenarioTests : IDisposable
{
    private readonly GameProfileData _original = GameProfile.Profile;
    private readonly string _originalPath = GameProfile.SavePath;
    private readonly string _directory =
        Directory.CreateTempSubdirectory("rotboi-audit-profiles-").FullName;

    public void Dispose()
    {
        GameProfile.Profile = _original;
        GameProfile.SavePath = _originalPath;
        Directory.Delete(_directory, recursive: true);
    }

    [Theory]
    [InlineData(AuditProfileStage.Fresh)]
    [InlineData(AuditProfileStage.MidProgression)]
    [InlineData(AuditProfileStage.FullyUnlocked)]
    public void DisposableAuditProfileRoundTripsWithoutUsingTheDeveloperSave(
        AuditProfileStage stage)
    {
        string path = Path.Combine(_directory, $"{stage}.json");
        GameProfile.Profile = AuditProfileScenarios.Create(stage);
        GameProfile.SavePath = path;

        Assert.True(GameProfile.SaveProfile());
        GameProfileData loaded = GameProfile.LoadProfile(path);

        Assert.Equal(8, loaded.CarriedInventory.Count);
        Assert.Equal(CampaignProgression.SenseKeys.Length,
            loaded.Campaign.SilverStatues.Count);
        Assert.All(loaded.SkillLevels, pair =>
            Assert.InRange(pair.Value, 0,
                MetaProgression.SkillNodesByKey[pair.Key].MaxLevel));
        if (stage == AuditProfileStage.Fresh)
            Assert.False(loaded.Campaign.BodyUnlocked);
        if (stage == AuditProfileStage.MidProgression)
            Assert.Equal(2, loaded.Campaign.SilverStatues.Values.Count(value => value.Unlocked));
        if (stage == AuditProfileStage.FullyUnlocked)
        {
            Assert.True(loaded.Campaign.BodyUnlocked);
            Assert.True(loaded.Campaign.AphantasiaUnlocked);
            Assert.All(CampaignProgression.SenseKeys,
                sense => Assert.Equal(7, loaded.NewGamePlusUnlocked[sense]));
        }
    }
}
