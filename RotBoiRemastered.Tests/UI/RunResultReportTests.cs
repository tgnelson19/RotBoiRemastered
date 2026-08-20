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
            state.SetNoExtract(true);
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
            Assert.Equal(3, report.MindTokenReward);
            Assert.Equal(1, report.PathMasteryBefore);
            Assert.Equal(2, report.PathMasteryAfter);
            Assert.Equal(2, report.NewGamePlusAfter);
            Assert.True(report.NoExtract);
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
        Assert.Equal(0, report.MindTokenReward);
    }

    [Fact]
    public void Capture_SurfacesQuestsCompletedDuringTheRun()
    {
        GameProfileData original = GameProfile.Profile;
        try
        {
            GameProfile.Profile = new GameProfileData();
            var state = new RunState { RunOutcome = "RUN COMPLETE" };
            GameProfile.IncrementQuest("enemies_defeated", 50, state);

            RunResultReport report = RunResultReport.Capture(state, "sound",
                retained: true, rewards: null);

            QuestCompletionSummary quest = Assert.Single(report.CompletedQuests);
            Assert.Equal("First Steps", quest.Name);
            Assert.Equal(1, quest.Reward);
        }
        finally
        {
            GameProfile.Profile = original;
        }
    }

    [Fact]
    public void Capture_WithNoQuestCompletionsLeavesCompletedQuestsEmpty()
    {
        var state = new RunState { RunOutcome = "DEFEATED" };

        RunResultReport report = RunResultReport.Capture(state, "sound",
            retained: false, rewards: null);

        Assert.Empty(report.CompletedQuests);
    }

    [Fact]
    public void CaptureSeparatesFieldAndBossTimeAndNamesTheDungeon()
    {
        var state = new RunState
        {
            RunOutcome = RunOutcomes.DungeonComplete,
            RunTimeSeconds = 30 * 60,
        };
        state.BossEncounterTelemetry.Add(new BossEncounterTelemetryData
        {
            BossKey = "path_guardian_sound",
            ClearSeconds = 90,
            Victory = true,
        });
        state.BossEncounterTelemetry.Add(new BossEncounterTelemetryData
        {
            BossKey = "dissonance",
            ClearSeconds = 150,
            Victory = true,
        });

        RunResultReport report = RunResultReport.Capture(state,
            NewGamePlus.DungeonKey, retained: true, rewards: null);

        Assert.Equal("THE DUNGEON", report.PathTitle);
        Assert.Equal(240, report.BossSeconds);
        Assert.Equal(1560, report.FieldSeconds);
        Assert.Equal(RunPaceBand.OnTarget, report.PaceBand);
    }
}
