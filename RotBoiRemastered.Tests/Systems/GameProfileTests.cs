using RotBoiRemastered.Systems;

namespace RotBoiRemastered.Tests.Systems;

/// <summary>Ported from tests/test_game_profile.py.</summary>
[Collection("GameProfileState")]
public class GameProfileTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("rotboi-profile-tests-").FullName;

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private static void AssertMatchesDefaults(GameProfileData profile)
    {
        var defaults = new GameProfileData();
        Assert.Equal(defaults.BestLevel, profile.BestLevel);
        Assert.Equal(defaults.BestKills, profile.BestKills);
        Assert.Equal(defaults.CompletedRuns, profile.CompletedRuns);
        Assert.Equal(defaults.AutoFire, profile.AutoFire);
        Assert.Equal(defaults.CasualMode, profile.CasualMode);
        Assert.Equal(defaults.TutorialHints, profile.TutorialHints);
        Assert.Equal(defaults.ScreenShake, profile.ScreenShake);
        Assert.Equal(defaults.DamageNumbers, profile.DamageNumbers);
        Assert.Equal(defaults.AimGuide, profile.AimGuide);
        Assert.Equal(defaults.HighContrast, profile.HighContrast);
        Assert.Equal(defaults.HudMode, profile.HudMode);
        Assert.Equal(defaults.TextSize, profile.TextSize);
        Assert.Equal(defaults.PlayerCoreColor, profile.PlayerCoreColor);
        Assert.Equal(defaults.PlayerEdgeColor, profile.PlayerEdgeColor);
        Assert.Equal(defaults.ProjectileColor, profile.ProjectileColor);
        Assert.Equal(defaults.ProjectileDesign, profile.ProjectileDesign);
        Assert.Empty(profile.Keybinds);
    }

    [Fact]
    public void MissingProfile_UsesAllDefaults()
    {
        var profile = GameProfile.LoadProfile(Path.Combine(_tempDir, "missing.json"));
        AssertMatchesDefaults(profile);
    }

    [Fact]
    public void UnknownFieldsAreIgnored_KnownFieldsAreLoaded()
    {
        string path = Path.Combine(_tempDir, "profile.json");
        File.WriteAllText(path, """{"BestLevel": 12, "Unknown": "ignored"}""");

        var profile = GameProfile.LoadProfile(path);

        Assert.Equal(12, profile.BestLevel);
        Assert.True(profile.CasualMode); // untouched fields keep their default
    }

    [Fact]
    public void CorruptProfile_FallsBackSafely()
    {
        string path = Path.Combine(_tempDir, "profile.json");
        File.WriteAllText(path, "not json");

        var profile = GameProfile.LoadProfile(path);

        AssertMatchesDefaults(profile);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAllFields()
    {
        string path = Path.Combine(_tempDir, "profile.json");
        var original = GameProfile.Profile;
        var originalSavePath = GameProfile.SavePath;
        try
        {
            GameProfile.Profile = new GameProfileData
            {
                BestLevel = 20,
                BestKills = 193,
                CompletedRuns = 2,
                HudMode = "expanded",
                TextSize = 1.3,
                Keybinds = new Dictionary<string, int?> { ["dash"] = 42, ["move_up"] = null },
            };
            GameProfile.SavePath = path;

            Assert.True(GameProfile.SaveProfile());
            var reloaded = GameProfile.LoadProfile(path);

            Assert.Equal(20, reloaded.BestLevel);
            Assert.Equal(193, reloaded.BestKills);
            Assert.Equal(2, reloaded.CompletedRuns);
            Assert.Equal("expanded", reloaded.HudMode);
            Assert.Equal(1.3, reloaded.TextSize);
            Assert.Equal(42, reloaded.Keybinds["dash"]);
            Assert.Null(reloaded.Keybinds["move_up"]);
        }
        finally
        {
            GameProfile.Profile = original;
            GameProfile.SavePath = originalSavePath;
        }
    }

    [Fact]
    public void RecordRun_TracksBestsAndCompletionCount()
    {
        var original = GameProfile.Profile;
        var originalSavePath = GameProfile.SavePath;
        try
        {
            GameProfile.Profile = new GameProfileData();
            GameProfile.SavePath = Path.Combine(_tempDir, "profile.json");

            GameProfile.RecordRun(level: 10, kills: 50);
            GameProfile.RecordRun(level: 5, kills: 80, completed: true);

            Assert.Equal(10, GameProfile.Profile.BestLevel); // max, not overwritten by the lower run
            Assert.Equal(80, GameProfile.Profile.BestKills);
            Assert.Equal(1, GameProfile.Profile.CompletedRuns);
        }
        finally
        {
            GameProfile.Profile = original;
            GameProfile.SavePath = originalSavePath;
        }
    }

    [Fact]
    public void Toggle_FlipsBooleanFieldByName()
    {
        var original = GameProfile.Profile;
        try
        {
            GameProfile.Profile = new GameProfileData { CasualMode = true };
            var result = GameProfile.Toggle(nameof(GameProfileData.CasualMode));

            Assert.False(result);
            Assert.False(GameProfile.Profile.CasualMode);
        }
        finally
        {
            GameProfile.Profile = original;
        }
    }

    [Fact]
    public void Toggle_IgnoresNonBooleanFields()
    {
        var original = GameProfile.Profile;
        try
        {
            GameProfile.Profile = new GameProfileData { BestLevel = 7 };
            var result = GameProfile.Toggle(nameof(GameProfileData.BestLevel));

            Assert.Null(result);
            Assert.Equal(7, GameProfile.Profile.BestLevel);
        }
        finally
        {
            GameProfile.Profile = original;
        }
    }
}
