using Microsoft.Xna.Framework;
using RotBoiRemastered.Systems;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.UI;

[Collection("GameProfileState")]
public sealed class ModeEntrySplashTests
{
    [Fact]
    public void SplashShowsForItsAuthoredDurationThenExpires()
    {
        var splash = new ModeEntrySplash();
        splash.Show("The Mind", "A thought begins.", Color.Purple);

        Assert.True(splash.Active);
        Assert.Equal("The Mind", splash.Title);
        splash.Update(ModeEntrySplash.Duration + 1);

        // Updates are deliberately frame-clamped, so a hitch cannot skip the presentation.
        Assert.True(splash.Active);
        for (int i = 0; i < 100; i++) splash.Update(.05);
        Assert.False(splash.Active);
    }

    [Fact]
    public void EnteringTheMindStartsItsTitleBand()
    {
        GameProfileData originalProfile = GameProfile.Profile;
        try
        {
            GameProfile.Profile = new GameProfileData();
            var session = new GameSession(Battleground.GenerateMind(), 1280, 720, new Random(1));
            new SoulHub().Enter(session);

            Assert.True(session.EntrySplash.Active);
            Assert.Equal("The Mind", session.EntrySplash.Title);
            Assert.NotEmpty(session.EntrySplash.Flavor);
        }
        finally
        {
            GameProfile.Profile = originalProfile;
        }
    }

    [Fact]
    public void StartingDungeonAndExpeditionsUseDistinctTitles()
    {
        var session = new GameSession(Battleground.GenerateMind(), 1280, 720, new Random(1));
        session.StartPathRun(new Random(1));
        Assert.Equal("The Dungeon", session.EntrySplash.Title);

        session.StartExpedition(CampaignWorld.Body, rng: new Random(2));
        Assert.Equal("The Body", session.EntrySplash.Title);
        session.StartExpedition(CampaignWorld.Soul, rng: new Random(3));
        Assert.Equal("The Soul", session.EntrySplash.Title);
    }
}
