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
        Assert.Equal(defaults.VisualEffectsIntensity, profile.VisualEffectsIntensity);
        Assert.Equal(FooterStats.Defaults, profile.FooterStats);
        Assert.Equal(defaults.TextSize, profile.TextSize);
        Assert.Equal(defaults.GuiScale, profile.GuiScale);
        Assert.Equal(defaults.DamageTextSize, profile.DamageTextSize);
        Assert.Equal(defaults.CameraZoom, profile.CameraZoom);
        Assert.Equal(defaults.PlayerCoreColor, profile.PlayerCoreColor);
        Assert.Equal(defaults.PlayerEdgeColor, profile.PlayerEdgeColor);
        Assert.Equal(defaults.ProjectileColor, profile.ProjectileColor);
        Assert.Equal(defaults.ProjectileDesign, profile.ProjectileDesign);
        Assert.Equal(defaults.MaxFrameRate, profile.MaxFrameRate);
        Assert.Equal(defaults.VSync, profile.VSync);
        Assert.Empty(profile.Keybinds);
        Assert.Empty(profile.NewGamePlusUnlocked);
        Assert.Empty(profile.SelectedNewGamePlus);
        Assert.False(profile.Campaign.AphantasiaStatue.Unlocked);
    }

    [Fact]
    public void MissingProfile_UsesAllDefaults()
    {
        var profile = GameProfile.LoadProfile(Path.Combine(_tempDir, "missing.json"));
        AssertMatchesDefaults(profile);
        Assert.Equal(CampaignProgression.SenseKeys.Length,
            profile.Campaign.SilverStatues.Count);
        Assert.Equal(CampaignProgression.SenseKeys.Length,
            profile.Campaign.GoldStatues.Count);
        Assert.NotNull(profile.Campaign.AphantasiaStatue);
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
    public void MalformedProgressionCollectionsAreClampedFilteredAndCapacitySafe()
    {
        string path = Path.Combine(_tempDir, "malformed-progression.json");
        File.WriteAllText(path, """
        {
          "BestLevel": -4,
          "BestKills": -2,
          "CompletedRuns": -8,
          "MindTokens": -12,
          "SkillLevels": { "tempered_soul": 999, "unknown": 3 },
          "QuestProgress": { "enemies_defeated": -50, "": 4 },
          "CompletedQuests": ["first_steps", "first_steps", "unknown"],
          "Storage": [
            { "Name": "Iron Sword", "Rarity": "Common" },
            { "Name": "missing item", "Rarity": "Common" }
          ],
          "CarriedEquipment": {
            "weapon": { "Name": "Iron Sword", "Rarity": "Common" },
            "helmet": { "Name": "Iron Sword", "Rarity": "Common" },
            "armor": { "Name": "missing item", "Rarity": "Common" }
          },
          "CarriedInventory": [
            { "Name": "missing item", "Rarity": "Common" }
          ],
          "PathMastery": { "sound": -9 },
          "ExtractedRuns": [
            null,
            {
              "Id": " ", "Path": " ", "Outcome": "BROKEN",
              "Level": -3, "Kills": -8, "Seconds": -12,
              "NewGamePlusLevel": 99
            }
          ],
          "RecentBossEncounters": [
            null,
            {
              "BossKey": " ", "SenseKey": " ", "FloorNumber": -2,
              "ClearSeconds": -4, "DamageTaken": -5,
              "SkippedBranchRooms": -1, "SkippedBranchThreat": -6,
              "CarriedEnemyThreat": -7, "LocalPlayerCount": 0,
              "Phases": [null, { "Label": " ", "Seconds": -9 }]
            }
          ]
        }
        """);

        GameProfileData profile = GameProfile.LoadProfile(path);

        Assert.Equal(0, profile.BestLevel);
        Assert.Equal(0, profile.BestKills);
        Assert.Equal(0, profile.CompletedRuns);
        Assert.Equal(0, profile.MindTokens);
        Assert.Equal(5, profile.SkillLevels["tempered_soul"]);
        Assert.DoesNotContain("unknown", profile.SkillLevels);
        Assert.Equal(0, profile.QuestProgress["enemies_defeated"]);
        Assert.DoesNotContain("", profile.QuestProgress);
        Assert.Equal(new[] { "first_steps" }, profile.CompletedQuests);
        Assert.Single(profile.Storage);
        Assert.Equal("Iron Sword", profile.Storage[0].Name);
        Assert.Equal(new[] { "weapon" }, profile.CarriedEquipment.Keys);
        Assert.All(profile.CarriedInventory, Assert.Null);
        Assert.Equal(0, profile.PathMastery["sound"]);
        ExtractedRunData run = Assert.Single(profile.ExtractedRuns);
        Assert.False(string.IsNullOrWhiteSpace(run.Id));
        Assert.Equal("Unknown Path", run.Path);
        Assert.Equal(RunOutcomes.Extracted, run.Outcome);
        Assert.Equal(0, run.Level);
        Assert.Equal(0, run.Kills);
        Assert.Equal(0, run.Seconds);
        Assert.Equal(NewGamePlus.MaxLevel, run.NewGamePlusLevel);
        BossEncounterTelemetryData encounter = Assert.Single(profile.RecentBossEncounters);
        Assert.Equal("unknown", encounter.BossKey);
        Assert.Equal("unknown", encounter.SenseKey);
        Assert.Equal(0, encounter.FloorNumber);
        Assert.Equal(0, encounter.ClearSeconds);
        Assert.Equal(0, encounter.DamageTaken);
        Assert.Equal(0, encounter.SkippedBranchRooms);
        Assert.Equal(0, encounter.SkippedBranchThreat);
        Assert.Equal(0, encounter.CarriedEnemyThreat);
        Assert.Equal(1, encounter.LocalPlayerCount);
        BossPhaseTelemetryData phase = Assert.Single(encounter.Phases);
        Assert.Equal("UNKNOWN", phase.Label);
        Assert.Equal(0, phase.Seconds);
    }

    [Theory]
    [InlineData(-4, 0)]
    [InlineData(.42, .42)]
    [InlineData(9, 1)]
    public void VisualEffectsIntensity_IsClampedAndMigrationSafe(
        double saved,
        double expected)
    {
        string path = Path.Combine(_tempDir, "vfx.json");
        File.WriteAllText(path, $$"""{"VisualEffectsIntensity":{{saved}}}""");

        GameProfileData profile = GameProfile.LoadProfile(path);

        Assert.Equal(expected, profile.VisualEffectsIntensity, precision: 3);
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
                VisualEffectsIntensity = .42,
                MaxFrameRate = 144,
                VSync = false,
                FooterStats = ["projectiles", "critical", "range"],
                Keybinds = new Dictionary<string, int?> { ["dash"] = 42, ["move_up"] = null },
                NewGamePlusUnlocked = new Dictionary<string, int> { ["sound"] = 4 },
                SelectedNewGamePlus = new Dictionary<string, int> { ["sound"] = 3 },
                RecentBossEncounters =
                [
                    new BossEncounterTelemetryData
                    {
                        BossKey = "path_guardian_sound",
                        SenseKey = "sound",
                        FloorNumber = 3,
                        Victory = true,
                        ClearSeconds = 42.5,
                        DamageTaken = 125,
                        ControllerUsed = true,
                        Phases =
                        [
                            new BossPhaseTelemetryData
                            {
                                Label = "PHASE 1 // MURMUR",
                                Seconds = 12.25,
                            },
                        ],
                    },
                ],
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
            Assert.Equal(.42, reloaded.VisualEffectsIntensity);
            Assert.Equal(145, reloaded.MaxFrameRate);
            Assert.False(reloaded.VSync);
            Assert.Equal(new[] { "projectiles", "critical", "range" },
                reloaded.FooterStats);
            Assert.Equal(42, reloaded.Keybinds["dash"]);
            Assert.Null(reloaded.Keybinds["move_up"]);
            Assert.Equal(4, reloaded.NewGamePlusUnlocked["sound"]);
            Assert.Equal(3, reloaded.SelectedNewGamePlus["sound"]);
            var bossTelemetry = Assert.Single(reloaded.RecentBossEncounters);
            Assert.Equal("path_guardian_sound", bossTelemetry.BossKey);
            Assert.Equal(42.5, bossTelemetry.ClearSeconds);
            Assert.Equal(125, bossTelemetry.DamageTaken);
            Assert.True(bossTelemetry.ControllerUsed);
            Assert.Equal(
                "PHASE 1 // MURMUR",
                Assert.Single(bossTelemetry.Phases).Label);
        }
        finally
        {
            GameProfile.Profile = original;
            GameProfile.SavePath = originalSavePath;
        }
    }

    [Theory]
    [InlineData(1, 30)]
    [InlineData(143, 145)]
    [InlineData(999, 360)]
    public void FrameRate_LoadNormalizesToSupportedRangeAndStep(
        int savedFrameRate,
        int expectedFrameRate)
    {
        string path = Path.Combine(_tempDir, $"fps-{savedFrameRate}.json");
        File.WriteAllText(
            path,
            $$"""{"MaxFrameRate":{{savedFrameRate}}}""");

        GameProfileData profile = GameProfile.LoadProfile(path);

        Assert.Equal(expectedFrameRate, profile.MaxFrameRate);
    }

    [Fact]
    public void FooterStats_InvalidAndDuplicateIdsAreNormalizedToThreeDefaults()
    {
        string path = Path.Combine(_tempDir, "footer-invalid.json");
        File.WriteAllText(path,
            """{"FooterStats":["damage","unknown","damage"]}""");

        GameProfileData profile = GameProfile.LoadProfile(path);

        Assert.Equal(new[] { "damage", "attack_rate", "defense" },
            profile.FooterStats);
    }

    [Fact]
    public void FooterStats_SelectingAnExistingStatSwapsSlots()
    {
        var selected = new[] { "damage", "attack_rate", "defense" };

        List<string> swapped = FooterStats.Select(selected, 0, "defense");

        Assert.Equal(new[] { "defense", "attack_rate", "damage" }, swapped);
    }

    [Fact]
    public void BossTelemetry_LoadRetainsOnlyLatestFiftyEncounters()
    {
        string path = Path.Combine(_tempDir, "boss-telemetry.json");
        var profile = new GameProfileData
        {
            RecentBossEncounters = Enumerable.Range(0, 55)
                .Select(index => new BossEncounterTelemetryData
                {
                    BossKey = $"boss-{index}",
                    SenseKey = "sound",
                    FloorNumber = index,
                })
                .ToList(),
        };
        var original = GameProfile.Profile;
        try
        {
            GameProfile.Profile = profile;
            Assert.True(GameProfile.SaveProfile(path));

            var reloaded = GameProfile.LoadProfile(path);

            Assert.Equal(50, reloaded.RecentBossEncounters.Count);
            Assert.Equal("boss-5", reloaded.RecentBossEncounters[0].BossKey);
            Assert.Equal("boss-54", reloaded.RecentBossEncounters[^1].BossKey);
        }
        finally
        {
            GameProfile.Profile = original;
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
