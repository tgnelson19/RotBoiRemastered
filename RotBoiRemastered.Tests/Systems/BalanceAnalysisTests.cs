using RotBoiRemastered.Entities;
using RotBoiRemastered.Systems;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.Systems;

public class BalanceAnalysisTests
{
    [Theory]
    [InlineData("Iron Dagger", "Bullet Damage", 2.05)]
    [InlineData("Iron Dagger", "Bullet Range", .40)]
    [InlineData("Bloody Dagger", "Bullet Range", .44)]
    [InlineData("Ash Wand", "Bullet Range", 2.15)]
    [InlineData("Glass Wand", "Bullet Range", 2.45)]
    public void WeaponIdentity_UsesRebalancedValues(string itemName, string stat, double expected)
    {
        var drop = new ItemDrop(Items.DefinitionsByName[itemName], "Epic", "S", "Balanced");
        var effect = Assert.Single(Items.Effects(drop).Where(effect => effect.Stat == stat));
        Assert.Equal(expected, effect.Multiplier, precision: 8);
    }

    [Fact]
    public void CombatEstimate_IncludesEquipmentCardsMetaCritVolleyAndPierce()
    {
        var damage = new UpgradeCard(Upgrades.DefinitionsByName["Bullet Damage"], "Rare", "multiplicative");
        var weapon = new ItemDrop(Items.DefinitionsByName["Iron Spear"], "Epic", "S", "Balanced");
        var result = BalanceAnalysis.Estimate(new BalanceAnalysis.BuildInput(
            ProjectileCount: 2, Pierce: 2, Equipment: new ItemDrop?[] { weapon }, Cards: new[] { damage },
            MetaRanks: new Dictionary<string, int> { ["tempered_soul"] = 5 }));

        Assert.True(result.Damage > 100);
        Assert.True(result.ExpectedProjectiles >= 2);
        Assert.True(result.ExpectedCritMultiplier > 1);
        Assert.True(result.CrowdDps > result.SingleTargetDps);
        Assert.True(result.Range > 250);
    }

    [Theory]
    [InlineData(20, false, 1.20, 1.35)]
    [InlineData(40, true, 1.25, 1.45)]
    public void EnemyHealth_MeetsSteadyPressureTarget(int endLevel, bool pathMode, double minimum, double maximum)
    {
        double start = BalanceAnalysis.RelativeTimeToKill(0, pathMode);
        double end = BalanceAnalysis.RelativeTimeToKill(endLevel, pathMode);
        Assert.InRange(end / start, minimum, maximum);

        for (int level = 1; level <= endLevel; level++)
        {
            double previous = BalanceAnalysis.RelativeTimeToKill(level - 1, pathMode);
            double current = BalanceAnalysis.RelativeTimeToKill(level, pathMode);
            Assert.True(current / previous < 1.20);
        }
    }

    [Fact]
    public void EnemySelectionLevel_CanDifferFromStatScalingLevel()
    {
        var normal = EnemyCatalog.Shared.Create("runner", 0, 0, 1, 300, new Random(4));
        var pathScaled = EnemyCatalog.Shared.Create("runner", 0, 0, 1, 300, new Random(4), statScalingLevel: 40);

        Assert.Equal(normal.Family, pathScaled.Family);
        Assert.True(pathScaled.MaxHp > normal.MaxHp * 20);
        Assert.True(pathScaled.ExpValue > normal.ExpValue);
    }

    [Fact]
    public void DropSimulation_IsDeterministicAndPreservesModeCadence()
    {
        var arena = BalanceAnalysis.SimulateDrops(20_000, pathMode: false, seed: 12);
        var repeat = BalanceAnalysis.SimulateDrops(20_000, pathMode: false, seed: 12);
        var path = BalanceAnalysis.SimulateDrops(20_000, pathMode: true, seed: 12);

        Assert.Equal(arena.MeanItems, repeat.MeanItems);
        Assert.Equal(arena.RarityRates, repeat.RarityRates);
        Assert.InRange(arena.MeanItems, .70, .80);
        Assert.InRange(path.MeanItems, .20, .26);
        Assert.True(arena.RarityRates["Common"] > arena.RarityRates["Rare"]);
    }

    [Fact]
    public void FragmentCadence_ProvidesTwoReforgesInAThirtyKillPath()
    {
        Assert.Equal(2, BalanceAnalysis.ExpectedReforges(30), precision: 6);
    }
}
