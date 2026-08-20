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
    public void CompletingQuest_RecordsItOnTheActiveRunStateForTheDebrief()
    {
        var state = new RunState();

        GameProfile.IncrementQuest("enemies_defeated", 49, state);

        Assert.Empty(state.QuestsCompletedThisRun);

        GameProfile.IncrementQuest("enemies_defeated", 1, state);

        Assert.Equal(new[] { "first_steps" }, state.QuestsCompletedThisRun);

        // Crossing the same counter again must not re-record an
        // already-completed quest on a later debrief.
        GameProfile.IncrementQuest("enemies_defeated", 1, state);

        Assert.Equal(new[] { "first_steps" }, state.QuestsCompletedThisRun);
    }

    [Fact]
    public void CompletingQuest_WithNoRunStateDoesNotThrow()
    {
        GameProfile.IncrementQuest("enemies_defeated", 100);

        Assert.Contains("first_steps", GameProfile.Profile.CompletedQuests);
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

    [Theory]
    [InlineData(1, false, 2)]
    [InlineData(3, false, 8)]
    [InlineData(3, true, 16)]
    public void CompletedNewGamePlusPath_DoublesClearRewardPerTierAndUnlocksTheNextTier(
        int level, bool hardMode, int expectedTokens)
    {
        GameProfile.Profile.CompletedQuests = MetaProgression.Quests.Select(quest => quest.Key).ToList();
        var state = new RunState();
        state.SetHardMode(hardMode);
        state.SetNewGamePlusLevel(level);

        MetaProgression.RecordExtraction(state, "sound", completed: true);

        Assert.Equal(expectedTokens, GameProfile.Profile.SoulTokens);
        Assert.Equal(Math.Min(7, level + 1), NewGamePlus.UnlockedLevel("sound"));
        Assert.Equal(level, GameProfile.Profile.ExtractedRuns[0].NewGamePlusLevel);
    }

    [Fact]
    public void ProgressionNeutralCompletionRecordsRunWithoutRewardsOrUnlocks()
    {
        GameProfile.Profile.CompletedQuests = MetaProgression.Quests.Select(quest => quest.Key).ToList();
        var state = new RunState();

        MetaProgression.RecordExtraction(state, NewGamePlus.DungeonKey,
            completed: true, grantCompletionRewards: false);

        Assert.Equal(RunOutcomes.RunComplete,
            GameProfile.Profile.ExtractedRuns[0].Outcome);
        Assert.Equal(0, GameProfile.Profile.MindTokens);
        Assert.Equal(0, GameProfile.Profile.PathMastery.GetValueOrDefault(NewGamePlus.DungeonKey));
        Assert.Equal(0, NewGamePlus.UnlockedLevel(NewGamePlus.DungeonKey));
    }

    [Fact]
    public void RunExtractedWithoutTouchingTheForge_FlagsTheNoReforgeCosmeticUnlock()
    {
        var state = new RunState();

        MetaProgression.RecordExtraction(state, "sound", completed: false);

        Assert.True(GameProfile.Profile.NoReforgeRunCompleted);
    }

    [Fact]
    public void RunExtractedAfterUsingTheForge_LeavesTheNoReforgeFlagUnset()
    {
        var state = new RunState();
        state.ReforgeUsedThisRun = true;

        MetaProgression.RecordExtraction(state, "sound", completed: false);

        Assert.False(GameProfile.Profile.NoReforgeRunCompleted);
    }

    [Fact]
    public void RunExtractedInHardMode_FlagsTheHardModeCosmeticUnlock()
    {
        var state = new RunState();
        state.SetHardMode(true);

        MetaProgression.RecordExtraction(state, "sound", completed: false);

        Assert.True(GameProfile.Profile.HardModeRunCompleted);
    }

    [Fact]
    public void RecordCoreOfTheVoidDefeat_SetsTheFlagOnceAndSaves()
    {
        Assert.False(GameProfile.Profile.DefeatedCoreOfTheVoid);

        MetaProgression.RecordCoreOfTheVoidDefeat();

        Assert.True(GameProfile.Profile.DefeatedCoreOfTheVoid);

        // Calling again must not throw or double-save in a way that breaks state.
        MetaProgression.RecordCoreOfTheVoidDefeat();
        Assert.True(GameProfile.Profile.DefeatedCoreOfTheVoid);
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
