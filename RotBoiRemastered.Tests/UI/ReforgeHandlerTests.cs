using RotBoiRemastered.Systems;
using RotBoiRemastered.UI;

namespace RotBoiRemastered.Tests.UI;

[Collection("GameProfileState")]
public class ReforgeHandlerTests
{
    // Reforge's whole surface is now one action -- raise Rarity a step, see
    // ReforgeHandler.TryUpgradeRarity -- so this helper's only lever is
    // starting Rarity, unlike the old (grade, modifier) pair.
    private static (RunState State, ReforgeHandler Handler) EquippedWeapon(string rarity = "Epic")
    {
        var state = new RunState();
        state.SetEquipment(new Dictionary<string, ItemDrop?>
        {
            ["weapon"] = new ItemDrop(Items.DefinitionsByName["Iron Sword"], rarity),
        });
        return (state, new ReforgeHandler(1280, 720));
    }

    [Fact]
    public void UpgradeRarity_SpendsReforgeFragmentCost_WithoutTouchingExperience()
    {
        var (state, handler) = EquippedWeapon("Epic");
        state.Fragments = 10;
        state.ExpCount = 500;
        int cost = Items.ReforgeFragmentCost;

        Assert.True(handler.TryUpgradeRarity(state));
        var upgraded = state.Equipment["weapon"]!;

        Assert.Equal("Legendary", upgraded.Rarity);
        Assert.Equal(10 - cost, state.Fragments);
        Assert.Equal(500, state.ExpCount);
    }

    [Fact]
    public void UpgradeRarity_StopsAtMythical_AndDoesNotSpendFragments()
    {
        var (state, handler) = EquippedWeapon("Mythical");
        state.Fragments = 10;

        Assert.False(handler.TryUpgradeRarity(state));

        Assert.Equal(10, state.Fragments);
        Assert.Equal("Mythical", state.Equipment["weapon"]!.Rarity);
        Assert.Null(Items.RarityUpgradeCost(state.Equipment["weapon"]!));
    }

    [Fact]
    public void UpgradeRarity_UnlocksAnotherModifierLadderRung()
    {
        // The direct replacement for the old "reroll changes the affix"
        // test: raising Rarity is what changes an item's active Modifiers
        // now, by unlocking the next fixed rung of its ladder -- nothing is
        // ever rerolled.
        var (state, handler) = EquippedWeapon("Rare");
        state.Fragments = 10;

        int before = Items.Effects(state.Equipment["weapon"]!).Count;
        Assert.True(handler.TryUpgradeRarity(state));
        int after = Items.Effects(state.Equipment["weapon"]!).Count;

        Assert.True(after > before);
    }

    [Fact]
    public void ReforgeFailsCleanlyWhenFragmentsAreInsufficientEvenWithStoredExperience()
    {
        var (state, handler) = EquippedWeapon("Epic");
        state.Fragments = Items.ReforgeFragmentCost - 1;
        state.ExpCount = 500;
        var before = state.Equipment["weapon"];

        Assert.False(handler.TryUpgradeRarity(state));
        Assert.Same(before, state.Equipment["weapon"]);
        Assert.Equal(Items.ReforgeFragmentCost - 1, state.Fragments);
        Assert.Equal(500, state.ExpCount);
    }

    [Fact]
    public void UpgradeRarity_FlagsTheRunAsHavingUsedTheForge()
    {
        var (state, handler) = EquippedWeapon("Epic");
        state.Fragments = 10;

        Assert.False(state.ReforgeUsedThisRun);
        Assert.True(handler.TryUpgradeRarity(state));
        Assert.True(state.ReforgeUsedThisRun);
    }

    [Fact]
    public void FailedReforgeAttempts_DoNotFlagTheRun()
    {
        var (state, handler) = EquippedWeapon("Epic");
        state.Fragments = 0;

        Assert.False(handler.TryUpgradeRarity(state));
        Assert.False(state.ReforgeUsedThisRun);
    }

    [Fact]
    public void ReforgingNeverChangesCoreForge()
    {
        var (state, handler) = EquippedWeapon("Epic");
        state.Equipment["weapon"] = state.Equipment["weapon"]! with { CoreForge = "dissonance" };
        state.Fragments = 10;

        Assert.True(handler.TryUpgradeRarity(state));

        Assert.Equal("dissonance", state.Equipment["weapon"]!.CoreForge);
        Assert.Equal("Legendary", state.Equipment["weapon"]!.Rarity);
    }
}
