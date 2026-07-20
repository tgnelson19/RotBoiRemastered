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

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public void CompletedPath_AwardsBaseTokensAndDoublesThemInHardMode(bool hardMode, int expectedTokens)
    {
        // Isolate the direct path-clear award from quest completion rewards
        // (the first extraction otherwise completes its own one-token quest).
        GameProfile.Profile.CompletedQuests = MetaProgression.Quests.Select(quest => quest.Key).ToList();
        var state = new RunState();
        state.SetHardMode(hardMode);

        MetaProgression.RecordExtraction(state, "sound", completed: true);

        Assert.Equal(expectedTokens, GameProfile.Profile.SoulTokens);
    }

    [Fact]
    public void SyncCarriedItems_RoundTripsEquipmentAndInventoryIntoProfile()
    {
        var state = new RunState();
        state.Equipment["weapon"] = Items.Deserialize(new StoredItemData("Iron Dagger", "Epic"));
        state.Inventory[0] = Items.Deserialize(new StoredItemData("Rusty Sword", "Common"));

        MetaProgression.SyncCarriedItems(state);

        Assert.Equal("Iron Dagger", GameProfile.Profile.CarriedEquipment["weapon"].Name);
        Assert.Equal("Rusty Sword", GameProfile.Profile.CarriedInventory[0]!.Name);
        Assert.All(GameProfile.Profile.CarriedInventory.Skip(1), Assert.Null);
    }

    [Fact]
    public void ClearCarriedItems_EmptiesEquipmentAndInventory()
    {
        GameProfile.Profile.CarriedEquipment["weapon"] = new StoredItemData("Iron Dagger", "Epic");
        GameProfile.Profile.CarriedInventory[0] = new StoredItemData("Rusty Sword", "Common");

        MetaProgression.ClearCarriedItems();

        Assert.Empty(GameProfile.Profile.CarriedEquipment);
        Assert.All(GameProfile.Profile.CarriedInventory, Assert.Null);
    }
}
