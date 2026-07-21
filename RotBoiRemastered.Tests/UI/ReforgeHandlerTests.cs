using RotBoiRemastered.Systems;
using RotBoiRemastered.UI;

namespace RotBoiRemastered.Tests.UI;

[Collection("GameProfileState")]
public class ReforgeHandlerTests
{
    private static (RunState State, ReforgeHandler Handler) EquippedWeapon(string grade = "F", string modifier = "Bloody")
    {
        var state = new RunState();
        state.SetEquipment(new Dictionary<string, ItemDrop?>
        {
            ["weapon"] = new ItemDrop(Items.DefinitionsByName["Iron Sword"], "Legendary", grade, modifier),
        });
        return (state, new ReforgeHandler(1280, 720));
    }

    [Fact]
    public void UpgradeGrade_SpendsFiveFragments_WithoutChangingRarityModifierOrExperience()
    {
        var (state, handler) = EquippedWeapon();
        state.Fragments = 10;
        state.ExpCount = 500;

        Assert.True(handler.TryUpgradeGrade(state));
        var upgraded = state.Equipment["weapon"]!;

        Assert.Equal("D", upgraded.Grade);
        Assert.Equal("Legendary", upgraded.Rarity);
        Assert.Equal("Bloody", upgraded.Modifier);
        Assert.Equal(5, state.Fragments);
        Assert.Equal(500, state.ExpCount);
        Assert.Equal(5, Items.GradeUpgradeCost(upgraded));
    }

    [Fact]
    public void UpgradeGrade_StopsAtS_AndDoesNotSpendFragments()
    {
        var (state, handler) = EquippedWeapon(grade: "S");
        state.Fragments = 10;

        Assert.False(handler.TryUpgradeGrade(state));

        Assert.Equal(10, state.Fragments);
        Assert.Equal("S", state.Equipment["weapon"]!.Grade);
    }

    [Fact]
    public void RerollModifier_ChangesAffixAndPreservesGradeAndRarity()
    {
        var (state, handler) = EquippedWeapon(grade: "B", modifier: "Bloody");
        state.Fragments = 10;
        state.ExpCount = 500;
        int cost = Items.ReforgeFragmentCost;

        Assert.True(handler.TryRerollModifier(state, new Random(8)));
        var rerolled = state.Equipment["weapon"]!;

        Assert.NotEqual("Bloody", rerolled.Modifier);
        Assert.Equal("B", rerolled.Grade);
        Assert.Equal("Legendary", rerolled.Rarity);
        Assert.Equal(10 - cost, state.Fragments);
        Assert.Equal(500, state.ExpCount);
        Assert.Contains(Items.AffixesFor("weapon"), affix => affix.Name == rerolled.Modifier);
    }

    [Fact]
    public void ReforgeFailsCleanlyWhenFragmentsAreInsufficientEvenWithStoredExperience()
    {
        var (state, handler) = EquippedWeapon();
        state.Fragments = 4;
        state.ExpCount = 500;
        var before = state.Equipment["weapon"];

        Assert.False(handler.TryUpgradeGrade(state));
        Assert.False(handler.TryRerollModifier(state, new Random(2)));
        Assert.Same(before, state.Equipment["weapon"]);
        Assert.Equal(4, state.Fragments);
        Assert.Equal(500, state.ExpCount);
    }

    [Fact]
    public void ReforgingNeverChangesCoreForgeOrRarity()
    {
        var (state, handler) = EquippedWeapon(grade: "F", modifier: "Bloody");
        state.Equipment["weapon"] = state.Equipment["weapon"]! with { CoreForge = "dissonance" };
        state.Fragments = 10;

        Assert.True(handler.TryUpgradeGrade(state));
        Assert.True(handler.TryRerollModifier(state, new Random(3)));

        Assert.Equal("dissonance", state.Equipment["weapon"]!.CoreForge);
        Assert.Equal("Legendary", state.Equipment["weapon"]!.Rarity);
    }
}
