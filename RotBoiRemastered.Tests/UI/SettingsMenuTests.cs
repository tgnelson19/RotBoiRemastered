using Microsoft.Xna.Framework;
using RotBoiRemastered.Systems;
using RotBoiRemastered.UI;

namespace RotBoiRemastered.Tests.UI;

[Collection("GameProfileState")]
public sealed class SettingsMenuTests
{
    [Theory]
    [InlineData(100, .60)]
    [InlineData(300, 3.5)]
    [InlineData(200, 2.05)]
    public void TextSizeSliderMapsItsFullTrackToTheSupportedRange(
        int mouseX, double expected)
    {
        var row = new Rectangle(88, 40, 224, 46);

        double value = SettingsMenu.TextSizeForSliderPosition(mouseX, row, 1f);

        Assert.Equal(expected, value, 3);
    }

    [Fact]
    public void GameplaySettingCanToggleDevUnlockTesting()
    {
        GameProfileData originalProfile = GameProfile.Profile;
        string originalPath = GameProfile.SavePath;
        string tempDir = Directory.CreateTempSubdirectory("rotboi-dev-setting-").FullName;
        try
        {
            GameProfile.Profile = new GameProfileData();
            GameProfile.SavePath = Path.Combine(tempDir, "profile.json");

            SettingsMenu.ChangeSetting("DevUnlockTesting", 1);

            Assert.True(GameProfile.Profile.DevUnlockTesting);
            Assert.True(File.Exists(GameProfile.SavePath));

            SettingsMenu.ChangeSetting("DevUnlockTesting", 1);
            Assert.False(GameProfile.Profile.DevUnlockTesting);
        }
        finally
        {
            GameProfile.Profile = originalProfile;
            GameProfile.SavePath = originalPath;
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void GameplaySettingCanToggleDeveloperArmory()
    {
        GameProfileData originalProfile = GameProfile.Profile;
        string originalPath = GameProfile.SavePath;
        string tempDir = Directory.CreateTempSubdirectory("rotboi-armory-setting-").FullName;
        try
        {
            GameProfile.Profile = new GameProfileData();
            GameProfile.SavePath = Path.Combine(tempDir, "profile.json");

            SettingsMenu.ChangeSetting("DeveloperArmory", 1);

            Assert.True(GameProfile.Profile.DeveloperArmory);
            Assert.True(File.Exists(GameProfile.SavePath));
            SettingsMenu.ChangeSetting("DeveloperArmory", 1);
            Assert.False(GameProfile.Profile.DeveloperArmory);
        }
        finally
        {
            GameProfile.Profile = originalProfile;
            GameProfile.SavePath = originalPath;
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
