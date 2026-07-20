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
    public void UpgradeGrade_SpendsIncreasingCost_WithoutChangingRarityOrModifier()
    {
        var (state, handler) = EquippedWeapon();
        state.ExpCount = 500;

        Assert.True(handler.TryUpgradeGrade(state));
        var upgraded = state.Equipment["weapon"]!;

        Assert.Equal("D", upgraded.Grade);
        Assert.Equal("Legendary", upgraded.Rarity);
        Assert.Equal("Bloody", upgraded.Modifier);
        Assert.Equal(500 - Items.GradeUpgradeCosts["F"], state.ExpCount);
        Assert.True(Items.GradeUpgradeCosts["D"] > Items.GradeUpgradeCosts["F"]);
    }

    [Fact]
    public void UpgradeGrade_StopsAtS_AndDoesNotSpendExperience()
    {
        var (state, handler) = EquippedWeapon(grade: "S");
        state.ExpCount = 500;

        Assert.False(handler.TryUpgradeGrade(state));

        Assert.Equal(500, state.ExpCount);
        Assert.Equal("S", state.Equipment["weapon"]!.Grade);
    }

    [Fact]
    public void RerollModifier_ChangesAffixAndPreservesGradeAndRarity()
    {
        var (state, handler) = EquippedWeapon(grade: "B", modifier: "Bloody");
        state.ExpCount = 500;
        int cost = Items.ModifierRerollCosts["B"];

        Assert.True(handler.TryRerollModifier(state, new Random(8)));
        var rerolled = state.Equipment["weapon"]!;

        Assert.NotEqual("Bloody", rerolled.Modifier);
        Assert.Equal("B", rerolled.Grade);
        Assert.Equal("Legendary", rerolled.Rarity);
        Assert.Equal(500 - cost, state.ExpCount);
        Assert.Contains(Items.AffixesFor("weapon"), affix => affix.Name == rerolled.Modifier);
    }

    [Fact]
    public void ReforgeFailsCleanlyWhenStoredExperienceIsInsufficient()
    {
        var (state, handler) = EquippedWeapon();
        state.ExpCount = 1;
        var before = state.Equipment["weapon"];

        Assert.False(handler.TryUpgradeGrade(state));
        Assert.False(handler.TryRerollModifier(state, new Random(2)));
        Assert.Same(before, state.Equipment["weapon"]);
        Assert.Equal(1, state.ExpCount);
    }

    [Fact]
    public void ReforgingNeverChangesCoreForgeOrRarity()
    {
        var (state, handler) = EquippedWeapon(grade: "F", modifier: "Bloody");
        state.Equipment["weapon"] = state.Equipment["weapon"]! with { CoreForge = "dissonance" };
        state.ExpCount = 500;

        Assert.True(handler.TryUpgradeGrade(state));
        Assert.True(handler.TryRerollModifier(state, new Random(3)));

        Assert.Equal("dissonance", state.Equipment["weapon"]!.CoreForge);
        Assert.Equal("Legendary", state.Equipment["weapon"]!.Rarity);
    }
}
