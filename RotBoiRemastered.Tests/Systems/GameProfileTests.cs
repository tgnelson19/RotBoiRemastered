using RotBoiRemastered.Systems;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

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
        Assert.Equal(defaults.TabShowWeaponStats, profile.TabShowWeaponStats);
        Assert.Equal(defaults.TabShowActiveQuests, profile.TabShowActiveQuests);
        Assert.Equal(defaults.TabShowAllQuests, profile.TabShowAllQuests);
        Assert.Equal(defaults.TabShowCosmetics, profile.TabShowCosmetics);
        Assert.Equal(defaults.TextSize, profile.TextSize);
        Assert.Equal(defaults.GuiScale, profile.GuiScale);
        Assert.Equal(defaults.DamageTextSize, profile.DamageTextSize);
        Assert.Equal(defaults.CameraZoom, profile.CameraZoom);
        Assert.Equal(defaults.PlayerCoreColor, profile.PlayerCoreColor);
        Assert.Equal(defaults.PlayerEdgeColor, profile.PlayerEdgeColor);
        Assert.Equal(defaults.ProjectileColor, profile.ProjectileColor);
        Assert.Equal(defaults.ProjectileDesign, profile.ProjectileDesign);
        Assert.Empty(profile.Keybinds);
        Assert.Empty(profile.NewGamePlusUnlocked);
        Assert.Empty(profile.SelectedNewGamePlus);
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
    public void AccessibilityScales_AreNormalizedToSafeSliderLimits()
    {
        string path = Path.Combine(_tempDir, "scales.json");
        File.WriteAllText(path, """{"TextSize":99,"GuiScale":0.1,"DamageTextSize":50,"CameraZoom":99}""");

        var profile = GameProfile.LoadProfile(path);

        Assert.Equal(UiTheme.MaxTextScale, profile.TextSize);
        Assert.Equal(UiTheme.MinGuiScale, profile.GuiScale);
        Assert.Equal(UiTheme.MaxDamageTextScale, profile.DamageTextSize);
        Assert.Equal(Camera.MaxDefaultZoomScale, profile.CameraZoom);
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
                TextSize = 1.4,
                GuiScale = 1.15,
                DamageTextSize = .65,
                TabShowWeaponStats = false,
                TabShowActiveQuests = true,
                TabShowCosmetics = true,
                Keybinds = new Dictionary<string, int?> { ["dash"] = 42, ["move_up"] = null },
                NewGamePlusUnlocked = new Dictionary<string, int> { ["sound"] = 4 },
                SelectedNewGamePlus = new Dictionary<string, int> { ["sound"] = 3 },
            };
            GameProfile.SavePath = path;

            Assert.True(GameProfile.SaveProfile());
            var reloaded = GameProfile.LoadProfile(path);

            Assert.Equal(20, reloaded.BestLevel);
            Assert.Equal(193, reloaded.BestKills);
            Assert.Equal(2, reloaded.CompletedRuns);
            Assert.Equal(1.4, reloaded.TextSize);
            Assert.Equal(1.15, reloaded.GuiScale);
            Assert.Equal(.65, reloaded.DamageTextSize);
            Assert.False(reloaded.TabShowWeaponStats);
            Assert.True(reloaded.TabShowActiveQuests);
            Assert.True(reloaded.TabShowCosmetics);
            Assert.Equal(42, reloaded.Keybinds["dash"]);
            Assert.Null(reloaded.Keybinds["move_up"]);
            Assert.Equal(4, reloaded.NewGamePlusUnlocked["sound"]);
            Assert.Equal(3, reloaded.SelectedNewGamePlus["sound"]);
        }
        finally
        {
            GameProfile.Profile = original;
            GameProfile.SavePath = originalSavePath;
        }
    }

    [Fact]
    public void NewGamePlusSaveData_IsMigrationSafeAndClampedToUnlocksAndTierSeven()
    {
        string oldPath = Path.Combine(_tempDir, "old-profile.json");
        File.WriteAllText(oldPath, """{"BestLevel":5}""");
        var oldProfile = GameProfile.LoadProfile(oldPath);
        Assert.Empty(oldProfile.NewGamePlusUnlocked);
        Assert.Empty(oldProfile.SelectedNewGamePlus);

        string invalidPath = Path.Combine(_tempDir, "invalid-ng.json");
        File.WriteAllText(invalidPath,
            """{"NewGamePlusUnlocked":{"sound":99,"touch":2},"SelectedNewGamePlus":{"sound":99,"touch":7,"sight":4}}""");
        var normalized = GameProfile.LoadProfile(invalidPath);

        Assert.Equal(7, normalized.NewGamePlusUnlocked["sound"]);
        Assert.Equal(7, normalized.SelectedNewGamePlus["sound"]);
        Assert.Equal(2, normalized.SelectedNewGamePlus["touch"]);
        Assert.Equal(0, normalized.SelectedNewGamePlus["sight"]);
    }

    [Fact]
    public void PreNewGamePlusPathMastery_UnlocksTierOneButDoesNotInferHigherTiers()
    {
        string path = Path.Combine(_tempDir, "pre-ng-profile.json");
        File.WriteAllText(path, """{"PathMastery":{"sound":5,"touch":0}}""");

        var migrated = GameProfile.LoadProfile(path);

        Assert.Equal(1, migrated.NewGamePlusUnlocked["sound"]);
        Assert.Equal(0, NewGamePlus.ClampLevel(migrated.NewGamePlusUnlocked.GetValueOrDefault("touch")));
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
