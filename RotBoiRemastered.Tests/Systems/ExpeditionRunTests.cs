using RotBoiRemastered.Systems;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.Systems;

public sealed class ExpeditionRunTests
{
    [Theory]
    [InlineData(CampaignWorld.Body)]
    [InlineData(CampaignWorld.Soul)]
    public void EverySeedCreatesExactlyOneSecretPerSense(CampaignWorld world)
    {
        for (int seed = 0; seed < 20; seed++)
        {
            var run = new ExpeditionRun(world, seed,
                world == CampaignWorld.Soul ? "sight" : null);
            Assert.Equal(5, run.Secrets.Count);
            Assert.Equal(CampaignProgression.SenseKeys.OrderBy(x => x),
                run.Secrets.Select(secret => secret.SenseKey).OrderBy(x => x));
            Assert.Single(run.Secrets.Where(secret => secret.IsFinale));
            Assert.Equal(ExpeditionWorldGenerator.Width, run.Battleground.Width);
            Assert.Equal(ExpeditionWorldGenerator.Height, run.Battleground.Height);
        }
    }

    [Fact]
    public void FinaleCannotBeSolvedUntilFourGuardiansReturn()
    {
        var run = new ExpeditionRun(CampaignWorld.Body, 44, "touch");
        Assert.False(run.SolveSecret("touch"));
        foreach (ExpeditionSecret secret in run.Secrets.Where(secret => !secret.IsFinale))
        {
            Assert.True(run.SolveSecret(secret.SenseKey));
            Assert.True(run.EnterDungeon(secret.SenseKey, secret.WorldPosition));
            Assert.True(run.CompleteDungeon());
        }
        Assert.True(run.SolveSecret("touch"));
    }

    [Fact]
    public void SecretDungeonUsesGuardianThenConfiguredFinaleTier()
    {
        var expedition = new ExpeditionRun(CampaignWorld.Soul, 9, "phantasia");
        ExpeditionSecret guardian = expedition.Secrets.First(secret => !secret.IsFinale);
        var guardianRun = PathRun.CreateSecretDungeon(expedition, guardian, new Random(1));
        Assert.Equal(PathFloorBossTier.Guardian, guardianRun.BossTier);

        foreach (ExpeditionSecret secret in expedition.Secrets.Where(secret => !secret.IsFinale))
        {
            expedition.SolveSecret(secret.SenseKey);
            expedition.EnterDungeon(secret.SenseKey, secret.WorldPosition);
            expedition.CompleteDungeon();
        }
        ExpeditionSecret finale = expedition.Secrets.Single(secret => secret.IsFinale);
        var finaleRun = PathRun.CreateSecretDungeon(expedition, finale, new Random(2));
        Assert.Equal(PathFloorBossTier.Finale, finaleRun.BossTier);
        Assert.Equal("phantasia", finaleRun.CurrentSenseKey);
    }
}
