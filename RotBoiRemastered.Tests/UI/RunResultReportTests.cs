using RotBoiRemastered.Systems;
using RotBoiRemastered.UI;

namespace RotBoiRemastered.Tests.UI;

[Collection("GameProfileState")]
public sealed class RunResultReportTests
{
    [Fact]
    public void Capture_FreezesLoadoutBuildAndExactRewardDeltas()
    {
        GameProfileData original = GameProfile.Profile;
        try
        {
            GameProfile.Profile = new GameProfileData();
            var state = new RunState
            {
                RunOutcome = "RUN COMPLETE",
                CurrentLevel = 14,
                NumOfEnemiesKilled = 220,
                RunTimeSeconds = 321,
            };
            state.RecordUpgrade("Bullet Damage", "Common");
            state.SetEquipment(new Dictionary<string, ItemDrop?>
            {
                ["weapon"] = new ItemDrop(
                    Items.DefinitionsByName["Iron Sword"], "Legendary"),
            });
            var rewards = new RunRewardSummary(4, 7, 1, 2, 1, 2,
                EquipmentRetained: true);

            RunResultReport report = RunResultReport.Capture(state, "sound",
                retained: true, rewards);
            state.SetEquipment(new Dictionary<string, ItemDrop?>());
            GameProfile.Profile.SoulTokens = 999;

            Assert.Equal("RUN COMPLETE", report.Outcome);
            Assert.Equal(3, report.SoulTokenReward);
            Assert.Equal(1, report.PathMasteryBefore);
            Assert.Equal(2, report.PathMasteryAfter);
            Assert.Equal(2, report.NewGamePlusAfter);
            Assert.Contains(report.RetainedLoadout,
                item => item.Name == "Iron Sword");
            Assert.Empty(report.LostLoadout);
            Assert.Contains("POWER", report.DominantFamilies[0]);
        }
        finally
        {
            GameProfile.Profile = original;
        }
    }

    [Fact]
    public void DefeatReportMarksCarriedItemsLost()
    {
        var state = new RunState { RunOutcome = "DEFEATED" };
        state.Inventory[0] = new ItemDrop(
            Items.DefinitionsByName["Iron Sword"], "Common");

        RunResultReport report = RunResultReport.Capture(state, "sound",
            retained: false, rewards: null);

        Assert.Empty(report.RetainedLoadout);
        Assert.Single(report.LostLoadout);
        Assert.Equal(0, report.SoulTokenReward);
    }
}
