using RotBoiRemastered.Systems;

namespace RotBoiRemastered.Tests.Systems;

[Collection("GameProfileState")]
public class MetaProgressionTests : IDisposable
{
    private readonly GameProfileData _originalProfile = GameProfile.Profile;
    private readonly string _originalPath = GameProfile.SavePath;
    private readonly string _tempDir = Directory.CreateTempSubdirectory("rotboi-meta-tests-").FullName;

    public MetaProgressionTests()
    {
        GameProfile.Profile = new GameProfileData();
        GameProfile.SavePath = Path.Combine(_tempDir, "profile.json");
    }

    public void Dispose()
    {
        GameProfile.Profile = _originalProfile;
        GameProfile.SavePath = _originalPath;
        Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void QuestCatalog_IsLargeGenericGrid()
    {
        Assert.True(MetaProgression.Quests.Count >= 20);
        Assert.Equal(MetaProgression.Quests.Count, MetaProgression.Quests.Select(quest => quest.Key).Distinct().Count());
    }

    [Fact]
    public void CompletingQuest_AwardsSoulTokensOnce()
    {
        GameProfile.IncrementQuest("enemies_defeated", 50);
        int tokens = GameProfile.Profile.SoulTokens;
        GameProfile.IncrementQuest("enemies_defeated", 50);

        Assert.Equal(1, tokens);
        Assert.Equal(tokens, GameProfile.Profile.SoulTokens);
    }

    [Fact]
    public void PurchasedSkill_AppliesEveryNewRun()
    {
        GameProfile.Profile.SoulTokens = 1;
        Assert.True(MetaProgression.PurchaseSkill("tempered_soul"));

        var state = new RunState();

        Assert.Equal(102, state.BulletDamage);
    }

    [Fact]
    public void ExtractionHistory_KeepsOnlyTenMostRecentRuns()
    {
        var state = new RunState();
        for (int index = 0; index < 12; index++)
        {
            state.CurrentLevel = index;
            MetaProgression.RecordExtraction(state, "sound", completed: false);
        }

        Assert.Equal(10, GameProfile.Profile.ExtractedRuns.Count);
        Assert.Equal(11, GameProfile.Profile.ExtractedRuns[0].Level);
        Assert.Equal(2, GameProfile.Profile.ExtractedRuns[^1].Level);
    }

    [Fact]
    public void TransferFromExtractedRun_RespectsTenSlotCapacity()
    {
        GameProfile.Profile.Storage.AddRange(Enumerable.Range(0, MetaProgression.StorageCapacity)
            .Select(_ => new StoredItemData("Iron Dagger", "Common")));
        GameProfile.Profile.ExtractedRuns.Add(new ExtractedRunData
        {
            Items = new List<StoredItemData> { new("Rusty Sword", "Rare") },
        });

        var run = GameProfile.Profile.ExtractedRuns[0];
        Assert.False(MetaProgression.TransferRunItemToStorage(run.Id, 0));
        Assert.Single(run.Items);
    }

    [Fact]
    public void BeginRun_WithdrawsSelectedStoredCopy()
    {
        var stored = new StoredItemData("Iron Dagger", "Epic");
        GameProfile.Profile.Storage.Add(stored);
        GameProfile.Profile.StartingLoadout["weapon"] = stored;

        var equipment = MetaProgression.BeginRun();

        Assert.Equal("Iron Dagger", equipment["weapon"]!.Name);
        Assert.Empty(GameProfile.Profile.Storage);
        Assert.Empty(GameProfile.Profile.StartingLoadout);
    }
}
