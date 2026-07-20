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
    public void LootRolls_ShiftTowardHigherGradesAndRaritiesAtNewGamePlusSeven()
    {
        const int rolls = 30_000;
        Random normalGradeRng = new(140), plusGradeRng = new(140);
        Random normalRarityRng = new(240), plusRarityRng = new(240);
        double AverageGrade(Random rng, int level) => Enumerable.Range(0, rolls)
            .Average(_ => Items.GradeOrder.ToList().IndexOf(Items.RollGrade(rng, level)));
        double AverageRarity(Random rng, int level) => Enumerable.Range(0, rolls)
            .Average(_ => Upgrades.RarityOrder.ToList().IndexOf(Items.RollItemRarity(rng, level)));

        Assert.True(AverageGrade(plusGradeRng, 7) > AverageGrade(normalGradeRng, 0) + 1.3);
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
    public void GenerateDrops_PassesNewGamePlusThroughRarityGradeAndCorePipelines()
    {
        var normal = Items.GenerateDrops(10_000, new Random(990), hardMode: true, pathKey: "touch",
            newGamePlusLevel: 0);
        var plusSeven = Items.GenerateDrops(10_000, new Random(990), hardMode: true, pathKey: "touch",
            newGamePlusLevel: 7);

        Assert.True(plusSeven.Count(drop => drop.CoreForge is not null)
            > normal.Count(drop => drop.CoreForge is not null) * 4);
        Assert.True(plusSeven.Average(drop => Items.GradeOrder.ToList().IndexOf(drop.Grade))
            > normal.Average(drop => Items.GradeOrder.ToList().IndexOf(drop.Grade)) + 1.2);
        Assert.True(plusSeven.Average(drop => Upgrades.RarityOrder.ToList().IndexOf(drop.Rarity))
            > normal.Average(drop => Upgrades.RarityOrder.ToList().IndexOf(drop.Rarity)) + .6);
    }
}
