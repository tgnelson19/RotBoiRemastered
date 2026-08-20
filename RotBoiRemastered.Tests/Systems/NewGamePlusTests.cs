using Microsoft.Xna.Framework;
using RotBoiRemastered.Entities;
using RotBoiRemastered.Systems;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.Systems;

[Collection("GameProfileState")]
public sealed class NewGamePlusTests : IDisposable
{
    private readonly GameProfileData _originalProfile = GameProfile.Profile;

    public NewGamePlusTests() => GameProfile.Profile = new GameProfileData();

    public void Dispose() => GameProfile.Profile = _originalProfile;

    [Fact]
    public void Selection_IsPathSpecificAndRequiresThePreviousTierClear()
    {
        Assert.Equal(0, NewGamePlus.UnlockedLevel("sound"));
        Assert.False(NewGamePlus.TrySelect("sound", 1, save: false));

        NewGamePlus.RecordCompletion("sound", completedLevel: 0);

        Assert.Equal(1, NewGamePlus.UnlockedLevel("sound"));
        Assert.True(NewGamePlus.TrySelect("sound", 1, save: false));
        Assert.False(NewGamePlus.TrySelect("sound", 2, save: false));
        Assert.Equal(0, NewGamePlus.UnlockedLevel("touch"));
    }

    [Fact]
    public void Completion_UnlocksSequentiallyAndCapsAtNewGamePlusSeven()
    {
        for (int level = 0; level <= 10; level++)
            NewGamePlus.RecordCompletion("sound", level);

        Assert.Equal(7, NewGamePlus.UnlockedLevel("sound"));
        Assert.True(NewGamePlus.TrySelect("sound", 7, save: false));
        Assert.False(NewGamePlus.TrySelect("sound", 8, save: false));
    }

    [Theory]
    [InlineData(0, 1.0, 1)]
    [InlineData(1, 1.5, 2)]
    [InlineData(3, 3.375, 8)]
    [InlineData(7, 17.0859375, 128)]
    public void TierMultipliers_FollowOnePointFiveCombatAndDoubleRewards(int level, double enemy, int reward)
    {
        Assert.Equal(enemy, NewGamePlus.EnemyMultiplier(level), precision: 8);
        Assert.Equal(reward, NewGamePlus.RewardMultiplier(level));
    }

    [Fact]
    public void ApplyEnemyHealth_ScalesCurrentAndMaximumHealthOnlyOnce()
    {
        var enemy = new Enemy(0, 0, 1, 20, Color.Red, 10, 100, 1, 1, 200);

        NewGamePlus.ApplyEnemyHealth(enemy, 2);
        NewGamePlus.ApplyEnemyHealth(enemy, 2);

        Assert.Equal(225, enemy.MaxHp);
        Assert.Equal(225, enemy.Hp);
        Assert.Equal(2, enemy.NewGamePlusLevelApplied);
    }

    [Fact]
    public void LootRolls_ShiftTowardHigherRaritiesAtNewGamePlusSeven()
    {
        // Grade is gone -- Rarity is the only loot-roll axis New Game Plus
        // still has to shift.
        const int rolls = 30_000;
        Random normalRarityRng = new(240), plusRarityRng = new(240);
        double AverageRarity(Random rng, int level) => Enumerable.Range(0, rolls)
            .Average(_ => Upgrades.RarityOrder.ToList().IndexOf(Items.RollItemRarity(rng, level)));

        Assert.True(AverageRarity(plusRarityRng, 7) > AverageRarity(normalRarityRng, 0) + .65);
    }

    [Fact]
    public void CoreForgeChance_IncreasesEveryTierAndRemainsCapped()
    {
        Assert.Equal(.10, Items.CoreForgeChance("Epic", 0), precision: 8);
        Assert.Equal(.125, Items.CoreForgeChance("Epic", 1), precision: 8);
        Assert.Equal(.275, Items.CoreForgeChance("Epic", 7), precision: 8);
        Assert.Equal(.55, Items.CoreForgeChance("Legendary", 7), precision: 8);
        Assert.Equal(.90, Items.CoreForgeChance("Mythical", 7), precision: 8);
    }

    [Fact]
    public void RunState_CapturesTheSelectedTierForTheActivePath()
    {
        string pathKey = GamePaths.Active().Key;
        GameProfile.Profile.NewGamePlusUnlocked[pathKey] = 4;
        GameProfile.Profile.SelectedNewGamePlus[pathKey] = 3;

        var state = new RunState();

        Assert.Equal(3, state.NewGamePlusLevel);
    }

    [Fact]
    public void DungeonRun_CapturesItsOwnSelectedNewGamePlusTier()
    {
        GameProfile.Profile.NewGamePlusUnlocked[NewGamePlus.DungeonKey] = 3;
        GameProfile.Profile.SelectedNewGamePlus[NewGamePlus.DungeonKey] = 2;
        var session = new GameSession(Battleground.GenerateSound(), 1280, 720,
            new Random(31));

        session.StartPathRun(new Random(32));

        Assert.True(session.IsPathMode);
        Assert.Equal(2, session.State.NewGamePlusLevel);
    }

    [Fact]
    public void DungeonCompletionUnlocksItsNextTierIndependentlyOfArenaPaths()
    {
        NewGamePlus.RecordCompletion(NewGamePlus.DungeonKey, completedLevel: 0);

        Assert.Equal(1, NewGamePlus.UnlockedLevel(NewGamePlus.DungeonKey));
        Assert.Equal(0, NewGamePlus.UnlockedLevel("sound"));
    }

    [Fact]
    public void GenerateDrops_PassesNewGamePlusThroughRarityAndCorePipelines()
    {
        var normal = Items.GenerateDrops(10_000, new Random(990), hardModeActive: true, pathKey: "touch",
            newGamePlusLevel: 0);
        var plusSeven = Items.GenerateDrops(10_000, new Random(990), hardModeActive: true, pathKey: "touch",
            newGamePlusLevel: 7);

        Assert.True(plusSeven.Count(drop => drop.CoreForge is not null)
            > normal.Count(drop => drop.CoreForge is not null) * 4);
        Assert.True(plusSeven.Average(drop => Upgrades.RarityOrder.ToList().IndexOf(drop.Rarity))
            > normal.Average(drop => Upgrades.RarityOrder.ToList().IndexOf(drop.Rarity)) + .6);
    }
}
